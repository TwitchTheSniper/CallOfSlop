using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// "Planet gravity" self-test: gravity frames (GravityFrames.cs) and the Pocket Planet map (Maps/Space/02-Planet.json).
/// Ordinary maps must be untouched (identity frames); on a bare test planet a player walks all the way round a great
/// circle and comes back with no turn, jumps land where they started, a grenade arcs down onto the ground, replays are
/// exact, a ragdoll settles on the curve and the horizon hides people; on Pocket Planet the bots' graph covers the
/// sphere and a bot walks from one pole to the other. Short scenarios only (no long matches).
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> PlanetNotes = new();

    private const float TestR = 40f;

    /// <summary>A bare planet of <see cref="TestR"/> m (nothing on it but four spawns).</summary>
    private const string TestPlanetJson = """
        {
          "name": "Planet lab", "halfX": 80, "halfZ": 80,
          "gravity": { "type": "point", "center": [0, 0, 0], "strength": 18 },
          "ops": [
            { "op": "Planet", "radius": 40, "color": [0.5, 0.45, 0.4] },
            { "op": "SurfaceSpawn", "lat": 60, "lon": 0 }, { "op": "SurfaceSpawn", "lat": -60, "lon": 0 },
            { "op": "SurfaceSpawn", "lat": 0, "lon": 90 }, { "op": "SurfaceSpawn", "lat": 0, "lon": -90 }
          ]
        }
        """;

    private static void PLine(string s)
    {
        Line(s);
        PlanetNotes.Add(s);
    }

    private static void CheckPlanetGravity()
    {
        PlanetNotes.Clear();
        var before = World.Map;
        var mode = World.PhysicsMode;
        try
        {
            PlanetUniformChecks();
            var lab = MapFile.Load(TestPlanetJson, "selftest/planet-lab.json", "");
            lab.Validate();
            World.SetMap(lab);
            PlanetLabChecks(lab);
            PlanetMapChecks();
        }
        finally
        {
            World.PhysicsMode = mode;
            World.SetMap(before);
            Ragdolls.Clear();
            if (PlanetNotes.Count > 0)
                Line("::notice title=Planet gravity (results)::" + string.Join("%0A", PlanetNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    /// <summary>Ordinary maps: the frames are the world's own, exactly.</summary>
    private static void PlanetUniformChecks()
    {
        World.SetMap(Maps.All[0]);
        var p = new Vector3(3.5f, 1.2f, -7.25f);
        var same = !GravityFrames.Curved && GravityFrames.UpAt(p) == Vector3.UnitY
                   && GravityFrames.Forward(p, 0.7f, -0.3f) == World.Forward(0.7f, -0.3f)
                   && GravityFrames.FlatRight(p, 1.1f) == World.FlatRight(1.1f)
                   && GravityFrames.Above(p, 1.6f) == p + new Vector3(0, 1.6f, 0)
                   && GravityFrames.ToLocal(p, new Vector3(1, 2, 3)) == new Vector3(1, 2, 3)
                   && GravityFrames.Basis(p) == Matrix4x4.Identity
                   && World.Physics.Gravity is UniformGravity
                   && Maps.All.Where(m => m.Name != "Pocket Planet").All(m => m.PointGravity == null && m.SolidMeshes.Count == 0 && m.PlanetHash == 0);
        var yaw = 0.4f;
        var pitch = 0.2f;
        GravityFrames.Transport(ref yaw, ref pitch, p, p + new Vector3(30, 0, 0));
        same &= yaw == 0.4f && pitch == 0.2f;
        PLine($"ordinary maps: identity frames, uniform gravity, no solid meshes, fingerprints unchanged: {same}");
        if (!same)
            Fail("planet gravity: an ordinary map's frames or gravity aren't the plain world ones");
    }

    private static MoveState Settle(Vector3 feet, int ticks = 30)
    {
        var s = new MoveState { Position = feet, Grounded = false };
        for (var i = 0; i < ticks; i++)
            World.Simulate(ref s, new InputCmd { Yaw = 0, SpeedPct = 100 });
        return s;
    }

    private static void PlanetLabChecks(Map lab)
    {
        var ok = World.Physics.Gravity is PointGravity && GravityFrames.Curved && World.Physics.MeshCount == 1;
        PLine($"planet lab: point gravity {GravityFrames.Curved}, {World.Physics.MeshCount} solid mesh ({lab.MeshPlacements[0].Mesh!.Lods[0].Indices.Length / 3} triangles), fingerprint {lab.ContentHash:x8}");
        if (!ok)
            Fail("planet gravity: the lab planet has no point gravity or no collider");

        // ---- Standing: settles onto the surface, up = away from the centre ----
        var start = Map.SurfaceDir(0, 0) * TestR;
        var s = Settle(Map.SurfaceDir(0, 0) * (TestR + 0.5f));
        var standR = s.Position.Length();
        PLine($"standing: grounded {s.Grounded}, feet {standR - TestR:+0.000;-0.000} m from the sphere");
        if (!s.Grounded || MathF.Abs(standR - TestR) > 0.08f)
            Fail($"planet gravity: a player doesn't stand on the planet (grounded {s.Grounded}, {standR - TestR:0.000} m off)");

        // ---- A full great circle (the z = 0 plane, clear of the frame's axis points), the look carried along ----
        s.Position = Settle(new Vector3(TestR + 0.3f, 0, 0)).Position;
        var origin = s.Position;
        var (yaw, pitch) = GravityFrames.Angles(origin, Vector3.UnitY); // heading for the +Y pole
        var look0 = GravityFrames.Forward(origin, yaw, pitch);
        var angle = 0f;
        var maxOff = 0f;
        var prev = s.Position;
        var ticks = 0;
        var minR = float.MaxValue;
        var maxR = 0f;
        while (angle < MathF.Tau && ticks < 4000)
        {
            GravityFrames.Transport(ref yaw, ref pitch, prev, s.Position);
            prev = s.Position;
            var before = s.Position;
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward, Yaw = yaw, Pitch = pitch, SpeedPct = 100 });
            angle += MathF.Atan2(Vector3.Cross(before, s.Position).Length(), Vector3.Dot(before, s.Position)); // (atan2: exact for tiny steps, unlike acos)
            maxOff = MathF.Max(maxOff, MathF.Abs(s.Position.Z));
            minR = MathF.Min(minR, s.Position.Length());
            maxR = MathF.Max(maxR, s.Position.Length());
            ticks++;
        }
        GravityFrames.Transport(ref yaw, ref pitch, prev, s.Position);
        var look1 = GravityFrames.Forward(s.Position, yaw, pitch);
        var back = Vector3.Distance(s.Position, origin);
        var turn = MathF.Acos(Math.Clamp(Vector3.Dot(look0, look1), -1f, 1f)) * 180f / MathF.PI;
        var circ = MathF.Tau * TestR;
        PLine($"great circle: {circ:0} m in {ticks * World.TickDt:0.0} s ({circ / (ticks * World.TickDt):0.00} m/s), back {back:0.00} m from the start, "
              + $"off the plane {maxOff:0.000} m, look turned {turn:0.00} deg, feet {minR - TestR:+0.00;-0.00}..{maxR - TestR:+0.00;-0.00} m from the sphere");
        if (ticks >= 4000 || back > 1.0f || maxOff > 0.05f || turn > 1.5f || minR < TestR - 0.1f || maxR > TestR + 0.2f || !s.Grounded)
            Fail($"planet gravity: the walk round the planet drifted (back {back:0.00} m, off {maxOff:0.000} m, turned {turn:0.0} deg, {ticks} ticks, feet {minR - TestR:0.00}..{maxR - TestR:0.00}, grounded {s.Grounded})");

        // ---- A jump lands back where it left (standing still) ----
        var j = Settle(Map.SurfaceDir(35, 50) * (TestR + 0.3f));
        var takeoff = j.Position;
        World.Simulate(ref j, new InputCmd { Buttons = Buttons.Jump, SpeedPct = 100 });
        var air = 1;
        var top = 0f;
        while (!j.Grounded && air < 200)
        {
            World.Simulate(ref j, new InputCmd { SpeedPct = 100 });
            top = MathF.Max(top, j.Position.Length() - takeoff.Length());
            air++;
        }
        var landedOff = Vector3.Distance(j.Position, takeoff);
        PLine($"jump: {air * World.TickDt:0.00} s in the air (flat ground: {2 * World.JumpSpeed / World.Gravity:0.00} s), {top:0.00} m up, landed {landedOff * 100:0.0} cm from the take-off");
        if (air >= 200 || landedOff > 0.05f || MathF.Abs(air * World.TickDt - 2 * World.JumpSpeed / World.Gravity) > 0.08f)
            Fail($"planet gravity: a jump didn't come back down where it left ({air} ticks, {top:0.00} m up, landed {landedOff:0.000} m away)");

        // ---- A grenade arcs over and comes to rest on the ground ----
        var eye = GravityFrames.Above(takeoff, World.EyeHeight);
        var (gy, _) = GravityFrames.Angles(takeoff, Vector3.UnitY);
        var gdir = GravityFrames.Forward(takeoff, gy, 0.6f);
        var frag = new Projectile { Kind = ThrowableKind.Frag, Pos = Throwables.LaunchPoint(eye, gdir), Vel = Throwables.LaunchVelocity(Throwables.Get(ThrowableKind.Frag), gdir, Vector3.Zero) };
        var highest = 0f;
        var lowest = float.MaxValue;
        var firstTouch = -1;
        for (var t = 0; t < 6 * World.TickRate; t++)
        {
            if (Throwables.Step(frag, World.TickDt) && firstTouch < 0)
                firstTouch = t;
            highest = MathF.Max(highest, frag.Pos.Length() - TestR);
            lowest = MathF.Min(lowest, frag.Pos.Length() - TestR);
        }
        var thrown = Vector3.Distance(frag.Pos, takeoff);
        var restR = frag.Pos.Length() - TestR;
        PLine($"grenade: up to {highest:0.0} m, first bounce after {firstTouch * World.TickDt:0.00} s, at rest {thrown:0.0} m away, {restR * 100:0.0} cm over the ground "
              + $"(lowest {lowest * 100:0.0} cm), speed {frag.Vel.Length():0.00} m/s");
        if (firstTouch < 0 || thrown < 6f || restR < 0.0f || restR > 0.3f || lowest < 0.0f || frag.Vel.Length() > 0.5f)
            Fail($"planet gravity: a grenade didn't arc down onto the planet and stop on it (first touch tick {firstTouch}, up {highest:0.0} m, rest {thrown:0.0} m away at {restR:0.00} m, lowest {lowest:0.00} m, speed {frag.Vel.Length():0.00})");

        // ---- Replays are exact (prediction): the same commands from the same state give the same bits ----
        var rng = new Random(1234);
        var cmds = new InputCmd[300];
        var cy = 0f;
        for (var i = 0; i < cmds.Length; i++)
        {
            cy = World.WrapAngle(cy + ((float)rng.NextDouble() - 0.5f) * 0.2f);
            var b = (Buttons)(rng.Next(16)) | (rng.Next(6) == 0 ? Buttons.Jump : 0) | (rng.Next(3) == 0 ? Buttons.Sprint : 0) | (rng.Next(12) == 0 ? Buttons.Crouch : 0);
            cmds[i] = new InputCmd { Seq = (uint)i + 1, Buttons = b, Yaw = cy, Pitch = 0.1f, SpeedPct = 100 };
        }
        var r0 = Settle(Map.SurfaceDir(-20, 140) * (TestR + 0.3f));
        var a = r0;
        var mid = r0;
        for (var i = 0; i < cmds.Length; i++)
        {
            World.Simulate(ref a, cmds[i]);
            if (i == 149)
                mid = a;
        }
        var c = mid;
        for (var i = 150; i < cmds.Length; i++)
            World.Simulate(ref c, cmds[i]);
        var exact = a.Position == c.Position && a.Velocity == c.Velocity && a.Grounded == c.Grounded && a.Stance == c.Stance;
        PLine($"replay: 300 random commands, replayed from tick 150: exact {exact} (moved {Vector3.Distance(r0.Position, a.Position):0.0} m)");
        if (!exact)
            Fail("planet gravity: a replay of the same commands gave a different state (prediction would jitter)");

        // ---- The horizon hides people: eyes 60 degrees round the planet can't see each other, 8 m apart they can ----
        var e1 = GravityFrames.Above(Map.SurfaceDir(10, 0) * TestR, World.EyeHeight);
        var e2 = GravityFrames.Above(Map.SurfaceDir(10, 60) * TestR, World.EyeHeight);
        var e3 = GravityFrames.Above(Map.SurfaceDir(10, 0 + 8f / (TestR * MathF.Cos(10 * MathF.PI / 180)) * 180 / MathF.PI) * TestR, World.EyeHeight);
        bool Sees(Vector3 x, Vector3 y)
        {
            var d = y - x;
            return World.RaycastWorld(x, Vector3.Normalize(d), d.Length()) >= d.Length() - 0.05f;
        }
        var far = Sees(e1, e2);
        var near = Sees(e1, e3);
        var horizon = MathF.Sqrt(2 * TestR * World.EyeHeight) * 2;
        PLine($"horizon: 60 deg round the planet seen {far} (blocked), 8 m apart seen {near}; two standing players lose sight at about {horizon:0} m");
        if (far || !near)
            Fail("planet gravity: line of sight doesn't follow the planet's curve (the horizon)");

        // ---- A ragdoll falls over and settles on the curve ----
        Ragdolls.Clear();
        var feet = Map.SurfaceDir(-35, -70) * TestR;
        var body = Ragdolls.SpawnForTest(new RagdollDeath { Feet = feet, Yaw = 0.5f, Stance = Stance.Stand, Victim = 7, Killer = 3, Source = GravityFrames.Above(feet, 1.3f) + GravityFrames.FlatForward(feet, 0.5f) * 4, HasSource = true, GunPower = 3f },
            RagdollLook.Soldier, 0, new Vector3(0.5f));
        var under = 0f;
        var steps = 0;
        for (; steps < 600 && body.Live && !body.Sim.Broken && !body.Sim.Asleep; steps++)
        {
            Ragdolls.StepAll();
            for (var i = 0; i < RagdollRig.Count; i++)
                under = MathF.Min(under, body.Sim.Pos[i].Length() - RagdollRig.Radius[i] - TestR);
        }
        var pelvisR = body.Sim.Pelvis.Length() - TestR;
        var drift = Vector3.Distance(body.Sim.Pelvis, feet);
        PLine($"ragdoll: {(body.Sim.Broken ? "BROKEN" : "ok")}, asleep {body.Sim.Asleep} after {steps * Ragdoll.Step:0.0} s, pelvis {pelvisR:0.00} m over the ground {drift:0.0} m from the feet, deepest {under * 100:0.0} cm into it");
        if (body.Sim.Broken || !body.Sim.Asleep || pelvisR > 0.6f || pelvisR < 0f || under < -0.08f || drift > 3f)
            Fail($"planet gravity: a ragdoll didn't settle on the planet's surface (broken {body.Sim.Broken}, live {body.Live}, asleep {body.Sim.Asleep} after {steps} steps, pelvis {pelvisR:0.00} m up, {drift:0.0} m away, deepest {under:0.000})");
        Ragdolls.Clear();

        // ---- Movement cost: 16 players running round, per tick, and no allocations ----
        var crowd = new MoveState[16];
        for (var i = 0; i < crowd.Length; i++)
            crowd[i] = Settle(Map.SurfaceDir(-60 + i * 8, i * 23) * (TestR + 0.3f), 5);
        for (var w = 0; w < 30; w++)
            for (var i = 0; i < crowd.Length; i++)
                World.Simulate(ref crowd[i], new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = i, SpeedPct = 100 });
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int costTicks = 240;
        for (var t = 0; t < costTicks; t++)
            for (var i = 0; i < crowd.Length; i++)
                World.Simulate(ref crowd[i], new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | (t % 50 == i ? Buttons.Jump : 0), Yaw = i + t * 0.01f, SpeedPct = 100 });
        var us = sw.Elapsed.TotalMilliseconds * 1000 / costTicks;
        var alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        PLine($"cost: 16 players moving on the planet: {us:0} us per tick, {alloc} bytes allocated in {costTicks} ticks");
        if (alloc != 0)
            Fail($"planet gravity: movement on the planet allocated {alloc} bytes");
        if (us > 4000)
            Fail($"planet gravity: movement for 16 on the planet costs {us:0} us per tick");
    }

    private static void PlanetMapChecks()
    {
        var map = Maps.All.FirstOrDefault(m => m.Name == "Pocket Planet");
        if (map == null)
        {
            Fail("planet gravity: Maps/Space/02-Planet.json (Pocket Planet) didn't load");
            return;
        }
        var g = map.PointGravity;
        var o = map.Objectives;
        var hillsOk = o.Hills.Count == 4 && o.Hills.All(h => MathF.Abs(h.Center.Y) < 1f);
        var flagsOk = o.Flags.Length == 2 && o.Flags[0].Y > map.PlanetRadius - 0.5f && o.Flags[1].Y < -map.PlanetRadius + 0.5f;
        var teamsOk = o.TeamSpawns[0].Count == 8 && o.TeamSpawns[1].Count == 8 && o.TeamSpawns[0].All(p => p.Y > 0) && o.TeamSpawns[1].All(p => p.Y < 0);
        PLine($"Pocket Planet: radius {map.PlanetRadius} m, gravity {g?.Strength ?? 0} m/s2 to {g?.Center}, {map.SolidMeshes.Count} solid meshes, {map.MeshPlacements.Count} meshes, "
              + $"{map.Spawns.Count} spawns, max {map.MaxPlayers} players, hills {o.Hills.Count} on the equator {hillsOk}, flags at the poles {flagsOk}, team spawns by pole {teamsOk}, fingerprint {map.ContentHash:x8}");
        if (g == null || map.PlanetRadius < 40 || map.MaxPlayers < 12 || !hillsOk || !flagsOk || !teamsOk)
            Fail($"planet gravity: Pocket Planet's gravity, spawns or objectives are wrong (hills {hillsOk}, flags {flagsOk}, teams {teamsOk}, players {map.MaxPlayers})");

        World.SetMap(map);
        // Every spawn stands on the ground with room for a body.
        var badSpawns = 0;
        foreach (var sp in map.Spawns)
        {
            var s = Settle(sp, 20);
            if (!s.Grounded || Vector3.Distance(s.Position, sp) > 0.4f || !World.BodyFits(s.Position, GravityFrames.UpAt(s.Position)))
                badSpawns++;
        }
        PLine($"Pocket Planet spawns: {map.Spawns.Count - badSpawns} of {map.Spawns.Count} stand clear on the ground");
        if (badSpawns > 0)
            Fail($"planet gravity: {badSpawns} Pocket Planet spawns aren't on clear ground");

        // ---- The bots' graph covers the sphere ----
        var sw = Stopwatch.StartNew();
        var nav = NavGraph.Build(map);
        var buildMs = sw.Elapsed.TotalMilliseconds;
        var north = nav.Nearest(o.Flags[0], needSight: false);
        var south = nav.Nearest(o.Flags[1], needSight: false);
        var path = new int[256];
        var search = new NavSearch();
        var len = north >= 0 && south >= 0 ? search.Find(nav, north, south, path, maxExpand: 20000) : 0;
        var pathM = 0f;
        for (var i = 1; i < len; i++)
            pathM += Vector3.Distance(nav.Nodes[path[i - 1]], nav.Nodes[path[i]]);
        var hemis = nav.MainNodes.Count(i => nav.Nodes[i].Y > 20) > 50 && nav.MainNodes.Count(i => nav.Nodes[i].Y < -20) > 50;
        PLine($"Pocket Planet nav: {nav.Nodes.Length} nodes, {nav.EdgeCount} edges, {nav.MainNodes.Length} in the main area, built in {buildMs:0} ms; "
              + $"pole to pole {len} nodes, {pathM:0} m (half the circumference: {MathF.PI * map.PlanetRadius:0} m), both hemispheres {hemis}");
        if (nav.Nodes.Length < 500 || nav.MainNodes.Length < nav.Nodes.Length * 0.8f || len < 2 || !hemis || pathM > MathF.PI * map.PlanetRadius * 1.5f)
            Fail($"planet gravity: the bots' graph doesn't cover Pocket Planet pole to pole ({nav.Nodes.Length} nodes, {nav.MainNodes.Length} main, path {len} nodes {pathM:0} m, hemispheres {hemis})");

        // ---- A bot walks from the north pole to the south pole (a short scenario: nobody else alive) ----
        var now = 0.0;
        using var game = ModeGame(1, GameMode.FreeForAll);
        game.HotSwapMap(map);
        NavGraph.Pump(double.MaxValue);
        var host = game.Local!;
        var bot = game.Players.Values.First(p => p.Bot != null);
        var goal = o.Flags[1];
        var startAt = o.TeamSpawns[0][0];
        void Hold()
        {
            host.Alive = false;
            host.Health = 0;
            host.WantsRespawn = false;
        }
        Hold();
        game.SelfTestPlace(bot, startAt, 0);
        bot.Bot!.ForcedGoal = goal;
        var minDist = float.MaxValue;
        var reachedAt = -1.0;
        var lowestR = float.MaxValue;
        for (var t = 0.0; t < 40 && reachedAt < 0; t += World.TickDt)
        {
            Hold();
            Run(game, ref now, World.TickDt);
            if (!bot.Alive)
                break;
            var d = Vector3.Distance(bot.Position, goal);
            minDist = MathF.Min(minDist, d);
            lowestR = MathF.Min(lowestR, bot.Position.Length() - map.PlanetRadius);
            if (d < 4f)
                reachedAt = t;
        }
        PLine($"bot pole to pole: {(reachedAt >= 0 ? $"reached the south pole in {reachedAt:0.0} s" : $"closest {minDist:0.0} m in 40 s")}, alive {bot.Alive}, "
              + $"lowest {lowestR:0.00} m from the ground, stuck kills {game.BotStuckKills}");
        if (reachedAt < 0 || lowestR < -0.15f)
            Fail($"planet gravity: a bot couldn't walk from the north pole to the south pole (closest {minDist:0.0} m, alive {bot.Alive}, lowest {lowestR:0.00} m, stuck kills {game.BotStuckKills}, ended at {bot.Position})");

        // ---- 16 players' worth of bot thinking on the planet: cost per tick ----
        for (var i = 0; i < 14; i++)
            game.AddBot(BotDifficulty.Normal, quiet: true);
        Run(game, ref now, 1.0); // warm up (spawns, JIT)
        var sw2 = Stopwatch.StartNew();
        const double secs = 2.0;
        Run(game, ref now, secs);
        var msPerTick = sw2.Elapsed.TotalMilliseconds / (secs * World.TickRate);
        PLine($"16 on the planet (15 bots): {msPerTick:0.00} ms per server tick");
        if (msPerTick > 6)
            Fail($"planet gravity: 15 bots on Pocket Planet cost {msPerTick:0.00} ms per tick");
    }
}
