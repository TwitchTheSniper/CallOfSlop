using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--zombie-probe [--map NAME] [--seated] [--cap N] [--lag MS] [--loss PCT] [--seconds S] [--size N]</c>: headless, a Sandbox host and a joined client over
/// the real transport (the yard map), a big zombie group spawned in front of the host, then (A) the host moves through it
/// at a car's speed (with <c>--seated</c> it drives the map's car) while both peers prepare their crowd frames from a chase
/// camera, and (B) on a map with a gunner turret (Construct Yard) the host sits in it and fires at the nearest crowd
/// zombie (<c>--cap N</c>: the host's horde cap, so few wake as individuals). Logs (zombieprobe.log): zombies appearing or
/// vanishing at full height in view per minute on each peer, "ghosts" (individuals the client has that the server
/// doesn't), what the turret's rounds did, and the server tick's cost. Proving Ground unless named.
/// </summary>
public static partial class SelfTest
{
    public static int ZombieProbe(string[] args)
    {
        string? After(string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var lag = int.TryParse(After("--lag"), out var l) ? l : 0;
        var loss = int.TryParse(After("--loss"), out var ls) ? ls : 0;
        var seconds = double.TryParse(After("--seconds"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 20;
        var size = int.TryParse(After("--size"), out var n) ? n : 3000;
        ProbeCap = int.TryParse(After("--cap"), out var cap) ? cap : 0;
        Brutal.GlfwApi.Glfw.Init();
        SavePaths.UseFolder(SelfTestSaveFolder, "self-test");
        BlueprintStore.ReadOnly = true;
        NavBake.Enabled = false;
        MapBake.Enabled = false;
        MapBake.CacheWrites = false;
        using (_log = new StreamWriter(AppFolders.LogPath("zombieprobe.log"), false))
        {
            Line($"zombie probe {DateTime.Now}: lag {lag} ms, loss {loss}%, {size} zombies, {seconds} s");
            if (Maps.Find(After("--map") ?? "Proving Ground") is not { } map)
                return 1;
            map.RestoreProps();
            World.SetMap(map);
            ProbeRun(map, lag, loss, seconds, size, args.Any(x => x.Equals("--seated", StringComparison.OrdinalIgnoreCase)));
            Line("DONE");
        }
        return 0;
    }

    private static int ProbeCap;

    private sealed class ProbePeer(string name)
    {
        public readonly string Name = name;
        public readonly CrowdView View = new();
        public readonly List<Vector3> Prev = new(8192), Now = new(8192); // (x, z, kind: 0 a sunk instance, 1 a standing one, 2 an individual)
        public readonly int[] AppearBy = new int[4]; // agent, particle, individual, client instance
        public Matrix4x4 PrevVp;
        public bool HasPrev;
        public int Appear, Vanish, Frames;
        public long Drawn;
    }

    private static void ProbeRun(Map map, int lag, int loss, double seconds, int size, bool seated)
    {
        const ushort port = 26371;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        if (ProbeCap > 0)
            server.HostHordeCap = ProbeCap; // (--cap N: few individuals, so the turret meets the crowd tiers)
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        if (lag > 0 || loss > 0)
        {
            server.Net.SimulateBadNetwork(lag, lag / 5, loss);
            a.Net.SimulateBadNetwork(lag, lag / 5, loss);
        }
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        server.RequestRespawn();
        Pump(server, [a], 1.0, ref now, clock);
        if (server.Local is not { Alive: true } me || a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var cp))
        {
            Line("no host player or the client didn't join");
            return;
        }
        if (!cp.Alive)
            server.SelfTestRespawn(cp, now);
        me.ImmuneUntil = cp.ImmuneUntil = now + 1e6;
        var start = ConstructWorld.Ground(World.Query, 0, -80);
        if (seated && server.Constructs.Bodies.Where(b => b.DriveSeat >= 0).OrderBy(b => b.WorldCom.LengthSquared()).FirstOrDefault() is { } first)
            start = ConstructWorld.Ground(World.Query, first.WorldCom.X + 3, first.WorldCom.Z);
        me.Teleport(start);
        cp.Teleport(ConstructWorld.Ground(World.Query, 30, 20));
        Pump(server, [a], 0.5, ref now, clock);
        var crowdAt = ConstructWorld.Ground(World.Query, start.X, start.Z + 140);
        if (seated)
        {
            var inward = Vector3.Normalize(new Vector3(-start.X, 0, -start.Z));
            crowdAt = ConstructWorld.Ground(World.Query, start.X + inward.X * 90, start.Z + inward.Z * 90);
        }
        var ask = server.SandboxSpawnRequest(me, SbSpawnKind.Zombies, BotDifficulty.Normal, SbLoadKind.Any, 0, 0, size, crowdAt);
        Pump(server, [a], 2.0, ref now, clock);
        Line($"spawn: \"{ask ?? "ok"}\"; server crowd {server.Crowd.Count} particles + {server.Crowd.MassTotal} mass, {server.Horde.Count} individuals; map half {World.Map.HalfX}x{World.Map.HalfZ}");

        var hp = new ProbePeer("host");
        var cl = new ProbePeer("client");
        var fog = new Vector2(map.FogStart, map.FogEnd);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 2.4f, 0.05f, MathF.Max(map.FarPlane, 900));
        var tickMs = new List<double>();
        var dropped0 = NetSafety.Dropped;

        // Seated: the host drives a real car (the map's first with a drive seat), the client rides in another seat if it has one.
        Construct? car = null;
        if (seated)
        {
            car = server.Constructs.Bodies.Where(b => b.DriveSeat >= 0).OrderBy(b => Vector3.Distance(b.WorldCom, start)).FirstOrDefault();
            if (car == null)
            {
                Line("seated: no car");
                return;
            }
            var df = ConstructEdits.SeatFeet(car, car.DriveSeat);
            me.Teleport(df + new Vector3(0, 0.3f, 0));
            Pump(server, [a], 0.3, ref now, clock);
            server.SandboxSend(CActKind.Use, body: car.Id, part: (byte)car.DriveSeat);
            for (var i = 0; i < car.PartCount; i++)
                if (i != car.DriveSeat && car.Parts[i].D.Role == PartRole.Seat)
                {
                    cp.Teleport(ConstructEdits.SeatFeet(car, i) + new Vector3(0, 0.3f, 0));
                    Pump(server, [a], 0.5, ref now, clock);
                    a.SandboxSend(CActKind.Use, body: car.Id, part: (byte)i);
                    break;
                }
            Pump(server, [a], 1.0, ref now, clock);
            Line($"seated: car {car.Id} at {car.WorldCom}, host seated {server.SandboxHands.Hand(me.Id).Seated}, client seated {server.SandboxHands.Hand(cp.Id).Seated}");
        }

        // Phase A: the host "drives" (z from -60 to +140 and back, 18 m/s), the client stands at the crowd's side.
        var t0 = clock.Elapsed.TotalSeconds;
        var wps = new[] { new Vector2(crowdAt.X, crowdAt.Z), new Vector2(crowdAt.X + 60, crowdAt.Z + 50), new Vector2(crowdAt.X - 50, crowdAt.Z + 40), new Vector2(crowdAt.X - 40, crowdAt.Z - 50), new Vector2(crowdAt.X + 50, crowdAt.Z - 40) };
        for (var k = 0; k < wps.Length; k++)
            wps[k] = Vector2.Clamp(wps[k], new Vector2(-170), new Vector2(170));
        var serverIds = new HashSet<ushort>();
        var ghostSeconds = 0.0;
        var ghostPeak = 0;
        var wp = 0;
        var stuckSince = -1.0;
        var backUntil = -1.0;
        var lastSample = 0.0;
        while (clock.Elapsed.TotalSeconds - t0 < seconds)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            var el = (float)(t - t0);
            var leg = el * 18f % 520f;
            var zpos = leg < 260 ? -80 + leg : 180 - (leg - 260);
            var dirZ = leg < 260 ? 1f : -1f;
            Vector3 at;
            var fwd = new Vector3(0, 0, dirZ);
            if (car != null)
            {
                at = car.WorldCom;
                var cf = Vector3.Transform(car.DriveFwd, car.Rot);
                cf.Y = 0;
                cf = cf.LengthSquared() > 1e-4f ? Vector3.Normalize(cf) : Vector3.UnitZ;
                var vf = new Vector3(car.Vel.X, 0, car.Vel.Z);
                fwd = cf; // (a chase camera behind the car)
                var goal = wps[wp % wps.Length];
                var to = new Vector3(goal.X - at.X, 0, goal.Y - at.Z);
                if (to.Length() < 12f)
                    wp++;
                var turn = cf.X * to.Z - cf.Z * to.X; // (sign: which side the goal is on)
                if (car.Vel.Length() < 1f)
                {
                    if (stuckSince < 0)
                        stuckSince = el;
                    else if (el - stuckSince > 2.5)
                        (backUntil, stuckSince) = (el + 1.5, -1);
                }
                else
                    stuckSince = -1;
                var b = el < backUntil ? Buttons.Back : Buttons.Forward;
                if (MathF.Abs(turn) > 0.15f * to.Length())
                    b |= turn > 0 ? Buttons.Right : Buttons.Left;
                server.HeldButtons = b;
                server.LookYaw = MathF.Atan2(cf.X, -cf.Z);
            }
            else
            {
                at = ConstructWorld.Ground(World.Query, 6f * MathF.Sin(el * 0.3f), zpos);
                me.Teleport(at);
                me.Sim.Velocity = new Vector3(0, 0, 18f * dirZ);
                me.Yaw = dirZ > 0 ? MathF.PI : 0f; // (yaw 0 looks along -Z)
            }
            var tk = Stopwatch.GetTimestamp();
            server.Update(now, dt);
            tickMs.Add(Stopwatch.GetElapsedTime(tk).TotalMilliseconds);
            a.Update(now, dt);
            ProbeFrame(server, hp, at, fwd, proj, fog, now);
            if (a.Local is { } ac)
                ProbeFrame(a, cl, at, fwd, proj, fog, now); // (the client looks from where the host is: the same view on both)
            // Ghosts: individuals the client has alive that the server has no living individual of.
            serverIds.Clear();
            foreach (var z in server.Horde)
                if (z.Alive)
                    serverIds.Add(z.Id);
            var ghosts = 0;
            foreach (var z in a.Horde)
                if (z.Alive && !serverIds.Contains(z.Id))
                    ghosts++;
            ghostSeconds += ghosts * dt;
            ghostPeak = Math.Max(ghostPeak, ghosts);
            if (t - lastSample > 2)
            {
                lastSample = t;
                var near = server.Horde.Count(z => z.Alive && Vector3.Distance(z.Position, at) < 25);
                var cnear = a.Horde.Count(z => z.Alive && Vector3.Distance(z.RenderPos, at) < 25);
                var ci = hp.View.Inst;
                Line($"  t {el:0.0}: host at {at.X:0},{at.Z:0} (wp {wp} {(car != null ? car.Vel.Length() : 18f):0} m/s, the server's player at {me.Position.X:0},{me.Position.Z:0}, client's {cp.Position.X:0},{cp.Position.Z:0}): individuals server {server.Horde.Count(z => z.Alive)} (within 25 m {near}), client {a.Horde.Count(z => z.Alive)} (within 25 m {cnear}); "
                     + $"crowd {server.Crowd.Count} particles, {server.Crowd.MassTotal} mass; host drew {ci.Particles} particles + {ci.Agents} agents (cells drawn {hp.View.Host.CellsDrawn}, culled {hp.View.Host.CellsCulled}, fogged {hp.View.Host.CellsFogged}, hidden {hp.View.Host.CellsHidden}, culled particles {ci.Culled}); "
                     + $"client drew {cl.View.Inst.Total}");
            }
            Thread.Sleep(1);
        }
        var mins = seconds / 60.0;
        foreach (var p in new[] { hp, cl })
            Line($"A {p.Name}: {p.Frames} frames, {p.Drawn / Math.Max(1, p.Frames)} drawn a frame; within 60 m in view, standing at once: {p.Appear / mins:0} appear and {p.Vanish / mins:0} vanish a minute "
                 + $"(appearing: agents {p.AppearBy[0] / mins:0}, particles {p.AppearBy[1] / mins:0}, individuals {p.AppearBy[2] / mins:0}, client crowd {p.AppearBy[3] / mins:0}); client gone lists dropped {a.ZGoneRead}");
        Line($"A ghosts: individuals alive on the client that the server doesn't have: {ghostSeconds / mins:0} zombie-seconds a minute, at most {ghostPeak} at once");
        var sorted = tickMs.OrderBy(x => x).ToArray();
        Line($"A server update: mean {tickMs.Average():F2} ms, p95 {sorted[(int)(sorted.Length * 0.95)]:F2}, worst {sorted[^1]:F2}; packets dropped {NetSafety.Dropped - dropped0}");

        // Phase B: the host in the yard's gunner turret, firing at the nearest zombie (any tier) near it.
        var turret = server.Constructs.Bodies.FirstOrDefault(b => b.GunCount > 0 && b.Guns[0].Seat >= 0);
        if (turret == null)
        {
            Line("B: no gunner turret");
            return;
        }
        var seat = turret.Guns[0].Seat;
        var feet = ConstructEdits.SeatFeet(turret, seat);
        me.Teleport(ConstructWorld.Ground(World.Query, feet.X, feet.Z + 1.4f));
        Pump(server, [a], 0.3, ref now, clock);
        server.SandboxSend(CActKind.Use, body: turret.Id, part: (byte)seat);
        Pump(server, [a], 0.5, ref now, clock);
        var tc = turret.WorldCom;
        var ask2 = server.SandboxSpawnRequest(me, SbSpawnKind.Zombies, BotDifficulty.Normal, SbLoadKind.Any, 0, 0, 600, ConstructWorld.Ground(World.Query, tc.X + 30, tc.Z));
        Pump(server, [a], 3.0, ref now, clock);
        int Total() => server.Crowd.Total + server.Horde.Count(z => z.Alive);
        var crowd0 = server.Crowd.Total;
        long Hurt() => server.Horde.Where(z => z.Alive).Sum(z => (long)(z.MaxHealth - z.Health))
                       + Enumerable.Range(0, server.Crowd.Count).Sum(i => (long)MathF.Max(0, server.Crowd.MassHP - server.Crowd.HP[i]));
        var total0 = Total();
        var hurt0 = Hurt();
        var fired0 = server.Constructs.ShotsFired;
        var landed0 = server.Constructs.RoundsLanded;
        server.SandboxSend(CActKind.SeatKeys, part: 1);
        var tb = clock.Elapsed.TotalSeconds;
        while (clock.Elapsed.TotalSeconds - tb < 8)
        {
            // Aim at the nearest zombie the host has (an individual or a crowd particle).
            var eye = ConstructWeapons.SeatEye(turret, seat);
            var best = new Vector3(float.NaN);
            var bd = float.MaxValue;
            if (server.Crowd.Count == 0)
                foreach (var z in server.Horde)
                    if (z.Alive && Vector3.DistanceSquared(z.Position, eye) is var d2 && d2 < bd)
                        (bd, best) = (d2, z.Position + new Vector3(0, 1.2f, 0));
            for (var i = 0; i < server.Crowd.Count; i++)
            {
                var pz = new Vector3(server.Crowd.X[i], server.Crowd.GroundAt(server.Crowd.X[i], server.Crowd.Z[i]) + 1.2f, server.Crowd.Z[i]);
                if (Vector3.DistanceSquared(pz, eye) is var d2 && d2 < bd)
                    (bd, best) = (d2, pz);
            }
            if (!float.IsNaN(best.X))
            {
                var to = best - eye;
                server.LookYaw = MathF.Atan2(to.X, -to.Z);
                server.LookPitch = MathF.Asin(Math.Clamp(to.Y / to.Length(), -1, 1));
            }
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            server.Update(now, dt);
            a.Update(now, dt);
            Thread.Sleep(2);
        }
        server.SandboxSend(CActKind.SeatKeys, part: 0);
        Pump(server, [a], 0.5, ref now, clock);
        Line($"B: spawn \"{ask2 ?? "ok"}\", seated {server.SandboxHands.Hand(me.Id).Seated}; turret fired {server.Constructs.ShotsFired - fired0}, crowd hits {server.CwCrowdHits}, rounds landed {server.Constructs.RoundsLanded - landed0}; "
             + $"zombies {total0} -> {Total()} (killed {total0 - Total()}, the crowd tiers {crowd0} -> {server.Crowd.Total}; crowd {server.Crowd.Count} particles, {server.Crowd.MassTotal} mass), wounds (individuals and particles) {hurt0} -> {Hurt()}; individuals server {server.Horde.Count(z => z.Alive)}, client {a.Horde.Count(z => z.Alive)}");
    }

    private static void ProbeFrame(Game g, ProbePeer p, Vector3 at, Vector3 fwd, Matrix4x4 proj, Vector2 fog, double now)
    {
        var eye = at - fwd * 7f + new Vector3(0, 3.5f, 0);
        var vp = Matrix4x4.CreateLookAt(eye, at + fwd * 20f, Vector3.UnitY) * proj;
        ZombieAnim.SetFrustum(vp);
        foreach (var z in g.Horde)
            g.ZombieAnimInputFor(z, now, out _);
        p.View.Prepare(g, eye, now, fog.Y > fog.X ? fog.Y : 0f);
        p.Now.Clear();
        var inst = p.View.Inst;
        var ter = World.Map.Terrain;
        for (var s = 0; s < inst.Stripes.Length; s++)
        {
            var st = inst.StripeAt(s);
            for (var lod = 0; lod < CrowdInstances.Lods; lod++)
                for (var i = 0; i < st.Count[lod]; i++)
                {
                    ref var d = ref st.Data[lod][i];
                    var y = (float)d.Y + inst.BaseY;
                    var g0 = ter?.Sample(d.X, d.Z) ?? 0f;
                    p.Now.Add(new Vector3(d.X, d.Z, y > g0 - 0.3f ? 1 : 0));
                }
        }
        foreach (var z in g.Horde)
            if (z.Alive)
            {
                var rp = z.RenderPos;
                if (ZombieAnim.Visible(rp + new Vector3(0, 0.9f, 0), 1.7f))
                    p.Now.Add(new Vector3(rp.X, rp.Z, 2));
            }
        p.Drawn += p.Now.Count;
        p.Frames++;
        if (p.HasPrev)
        {
            p.Appear += Unmatched(p.Now, p.Prev, eye, vp, p.PrevVp, g, p);
            p.Vanish += Unmatched(p.Prev, p.Now, eye, p.PrevVp, vp, null, null);
        }
        p.Prev.Clear();
        (p.HasPrev, p.PrevVp) = (true, vp);
        p.Prev.AddRange(p.Now);
    }

    /// <summary>
    /// Points of <paramref name="a"/> standing (not sunk in the ground) within 60 m, well inside both frames' views, with no
    /// point of <paramref name="b"/> (sunk or not) within 1.5 m: something that appeared or vanished at full height.
    /// </summary>
    private static int Unmatched(List<Vector3> a, List<Vector3> b, Vector3 eye, Matrix4x4 vpA, Matrix4x4 vpB, Game? g, ProbePeer? who)
    {
        var grid = new Dictionary<(int, int), List<Vector3>>();
        foreach (var q in b)
        {
            var k = ((int)MathF.Floor(q.X / 2), (int)MathF.Floor(q.Y / 2));
            if (!grid.TryGetValue(k, out var list))
                grid[k] = list = new List<Vector3>();
            list.Add(q);
        }
        var n = 0;
        foreach (var q in a)
        {
            if (q.Z == 0 || Vector2.Distance(new Vector2(q.X, q.Y), new Vector2(eye.X, eye.Z)) > 60)
                continue;
            var w = new Vector3(q.X, World.Map.Terrain?.Sample(q.X, q.Y) + 1f ?? 1f, q.Y);
            if (!Inside(w, vpA) || !Inside(w, vpB))
                continue;
            var found = false;
            var kx = (int)MathF.Floor(q.X / 2);
            var kz = (int)MathF.Floor(q.Y / 2);
            for (var dx = -1; dx <= 1 && !found; dx++)
                for (var dz = -1; dz <= 1 && !found; dz++)
                    if (grid.TryGetValue((kx + dx, kz + dz), out var list))
                        foreach (var r in list)
                            if ((q.X - r.X) * (q.X - r.X) + (q.Y - r.Y) * (q.Y - r.Y) < 1.5f * 1.5f)
                            {
                                found = true;
                                break;
                            }
            if (found)
                continue;
            n++;
            if (who == null || g == null)
                continue;
            if (q.Z == 2)
                who.AppearBy[2]++;
            else if (!g.IsServer)
                who.AppearBy[3]++;
            else
            {
                var ag = who.View.Host;
                var isAgent = false;
                for (var i = 0; i < ag.Count && !isAgent; i++)
                    isAgent = MathF.Abs(ag.X[i] - q.X) < 0.3f && MathF.Abs(ag.Z[i] - q.Y) < 0.3f;
                who.AppearBy[isAgent ? 0 : 1]++;
            }
        }
        return n;
    }

    private static bool Inside(Vector3 w, Matrix4x4 vp)
    {
        var c = Vector4.Transform(new Vector4(w, 1), vp);
        return c.W > 0.1f && MathF.Abs(c.X) < 0.85f * c.W && MathF.Abs(c.Y) < 0.85f * c.W;
    }
}
