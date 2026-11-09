using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--zombie-probe [--lag MS] [--loss PCT] [--seconds S] [--size N]</c>: headless, a Sandbox host and a joined client over
/// the real transport (the yard map), a big zombie group spawned in front of the host, then (A) the host moves through it
/// at a car's speed while both peers prepare their crowd frames from a chase camera, and (B) the host sits in the yard's
/// gunner turret and fires at the nearest zombie. Logs (zombieprobe.log): zombies appearing and vanishing in view per
/// minute on each peer, the individuals each side has, what the turret's rounds did, and the server tick's cost.
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
        Brutal.GlfwApi.Glfw.Init();
        SavePaths.UseFolder(SelfTestSaveFolder, "self-test");
        BlueprintStore.ReadOnly = true;
        NavBake.Enabled = false;
        MapBake.Enabled = false;
        MapBake.CacheWrites = false;
        using (_log = new StreamWriter(AppFolders.LogPath("zombieprobe.log"), false))
        {
            Line($"zombie probe {DateTime.Now}: lag {lag} ms, loss {loss}%, {size} zombies, {seconds} s");
            if (Maps.Find(ConstructYardMap) is not { } map)
                return 1;
            map.RestoreProps();
            World.SetMap(map);
            ProbeRun(map, lag, loss, seconds, size);
            Line("DONE");
        }
        return 0;
    }

    private sealed class ProbePeer(string name)
    {
        public readonly string Name = name;
        public readonly CrowdView View = new();
        public readonly List<Vector2> Prev = new(8192), Now = new(8192);
        public Matrix4x4 PrevVp;
        public bool HasPrev;
        public int Appear, Vanish, Frames;
        public long Drawn;
    }

    private static void ProbeRun(Map map, int lag, int loss, double seconds, int size)
    {
        const ushort port = 26371;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
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
        var start = ConstructWorld.Ground(World.Query, 0, -60);
        me.Teleport(start);
        cp.Teleport(ConstructWorld.Ground(World.Query, 30, 20));
        Pump(server, [a], 0.5, ref now, clock);
        var crowdAt = ConstructWorld.Ground(World.Query, 0, 40);
        var ask = server.SandboxSpawnRequest(me, SbSpawnKind.Zombies, BotDifficulty.Normal, SbLoadKind.Any, 0, 0, size, crowdAt);
        Pump(server, [a], 2.0, ref now, clock);
        Line($"spawn: \"{ask ?? "ok"}\"; server crowd {server.Crowd.Count} particles + {server.Crowd.MassTotal} mass, {server.Horde.Count} individuals; map half {World.Map.HalfX}x{World.Map.HalfZ}");

        var hp = new ProbePeer("host");
        var cl = new ProbePeer("client");
        var fog = new Vector2(map.FogStart, map.FogEnd);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 2.4f, 0.05f, MathF.Max(map.FarPlane, 900));
        var tickMs = new List<double>();
        var dropped0 = NetSafety.Dropped;

        // Phase A: the host "drives" (z from -60 to +140 and back, 18 m/s), the client stands at the crowd's side.
        var t0 = clock.Elapsed.TotalSeconds;
        var lastSample = 0.0;
        while (clock.Elapsed.TotalSeconds - t0 < seconds)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            var el = (float)(t - t0);
            var leg = el * 18f % 400f;
            var zpos = leg < 200 ? -60 + leg : 140 - (leg - 200);
            var dirZ = leg < 200 ? 1f : -1f;
            var at = ConstructWorld.Ground(World.Query, 6f * MathF.Sin(el * 0.3f), zpos);
            me.Teleport(at);
            me.Sim.Velocity = new Vector3(0, 0, 18f * dirZ);
            me.Yaw = dirZ > 0 ? MathF.PI : 0f; // (yaw 0 looks along -Z)
            var tk = Stopwatch.GetTimestamp();
            server.Update(now, dt);
            tickMs.Add(Stopwatch.GetElapsedTime(tk).TotalMilliseconds);
            a.Update(now, dt);
            var fwd = new Vector3(0, 0, dirZ);
            ProbeFrame(server, hp, at, fwd, proj, fog, now);
            if (a.Local is { } ac)
                ProbeFrame(a, cl, at, fwd, proj, fog, now); // (the client looks from where the host is: the same view on both)
            if (t - lastSample > 2)
            {
                lastSample = t;
                var near = server.Horde.Count(z => z.Alive && Vector3.Distance(z.Position, at) < 25);
                var cnear = a.Horde.Count(z => z.Alive && Vector3.Distance(z.RenderPos, at) < 25);
                var ci = hp.View.Inst;
                Line($"  t {el:0.0}: host at z {zpos:0}: individuals server {server.Horde.Count(z => z.Alive)} (within 25 m {near}), client {a.Horde.Count(z => z.Alive)} (within 25 m {cnear}); "
                     + $"crowd {server.Crowd.Count} particles, {server.Crowd.MassTotal} mass; host drew {ci.Particles} particles + {ci.Agents} agents (cells drawn {hp.View.Host.CellsDrawn}, culled {hp.View.Host.CellsCulled}, fogged {hp.View.Host.CellsFogged}, hidden {hp.View.Host.CellsHidden}, culled particles {ci.Culled}); "
                     + $"client drew {cl.View.Inst.Total}");
            }
            Thread.Sleep(1);
        }
        var mins = seconds / 60.0;
        foreach (var p in new[] { hp, cl })
            Line($"A {p.Name}: {p.Frames} frames, {p.Drawn / Math.Max(1, p.Frames)} drawn a frame; within 60 m in view: {p.Appear / mins:0} appear and {p.Vanish / mins:0} vanish a minute");
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
        long Hurt() => server.Horde.Where(z => z.Alive).Sum(z => (long)(z.MaxHealth - z.Health));
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
        Line($"B: spawn \"{ask2 ?? "ok"}\", seated {server.SandboxHands.Hand(me.Id).Seated}; turret fired {server.Constructs.ShotsFired - fired0}, rounds landed {server.Constructs.RoundsLanded - landed0}; "
             + $"zombies {total0} -> {Total()} (killed {total0 - Total()}), individuals' wounds {hurt0} -> {Hurt()}; individuals server {server.Horde.Count(z => z.Alive)}, client {a.Horde.Count(z => z.Alive)}");
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
        for (var s = 0; s < inst.Stripes.Length; s++)
        {
            var st = inst.StripeAt(s);
            for (var lod = 0; lod < CrowdInstances.Lods; lod++)
                for (var i = 0; i < st.Count[lod]; i++)
                    p.Now.Add(new Vector2(st.Data[lod][i].X, st.Data[lod][i].Z));
        }
        foreach (var z in g.Horde)
            if (z.Alive)
            {
                var rp = g.IsServer ? z.Position : z.RenderPos;
                if (ZombieAnim.Visible(rp + new Vector3(0, 0.9f, 0), 1.7f))
                    p.Now.Add(new Vector2(rp.X, rp.Z));
            }
        p.Drawn += p.Now.Count;
        p.Frames++;
        if (p.HasPrev)
        {
            p.Appear += Unmatched(p.Now, p.Prev, eye, vp, p.PrevVp);
            p.Vanish += Unmatched(p.Prev, p.Now, eye, p.PrevVp, vp);
        }
        p.Prev.Clear();
        (p.HasPrev, p.PrevVp) = (true, vp);
        p.Prev.AddRange(p.Now);
    }

    /// <summary>Points of <paramref name="a"/> within 60 m, well inside both frames' views, with none of <paramref name="b"/> within 1.5 m.</summary>
    private static int Unmatched(List<Vector2> a, List<Vector2> b, Vector3 eye, Matrix4x4 vpA, Matrix4x4 vpB)
    {
        var grid = new Dictionary<(int, int), List<Vector2>>();
        foreach (var q in b)
        {
            var k = ((int)MathF.Floor(q.X / 2), (int)MathF.Floor(q.Y / 2));
            if (!grid.TryGetValue(k, out var list))
                grid[k] = list = new List<Vector2>();
            list.Add(q);
        }
        var n = 0;
        foreach (var q in a)
        {
            if (Vector2.Distance(q, new Vector2(eye.X, eye.Z)) > 60)
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
                            if (Vector2.DistanceSquared(q, r) < 1.5f * 1.5f)
                            {
                                found = true;
                                break;
                            }
            if (!found)
                n++;
        }
        return n;
    }

    private static bool Inside(Vector3 w, Matrix4x4 vp)
    {
        var c = Vector4.Transform(new Vector4(w, 1), vp);
        return c.W > 0.1f && MathF.Abs(c.X) < 0.85f * c.W && MathF.Abs(c.Y) < 0.85f * c.W;
    }
}
