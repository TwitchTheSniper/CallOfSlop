using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Movers": moving platforms (Movers.cs, MapMovers.cs). Pose determinism, carrying on every mover
/// type, pushing without crushing, momentum when jumping off, ray casts at the current pose, fingerprints, the
/// "Physics Test" map, its nav graph and a short bot match on it.
/// </summary>
public static partial class SelfTest
{
    private const string PhysicsTestMap = "Physics Test";

    private static void CheckMovers()
    {
        var map = Maps.Find(PhysicsTestMap);
        if (map == null || map.SourceFile == null)
        {
            Fail($"Maps/Dev/PhysicsTest.json ('{PhysicsTestMap}') didn't load");
            return;
        }
        Line($"{map.Name}: {map.Movers.Count} movers ({string.Join(", ", map.Movers.Select(m => $"{m.Name} [{m.Kind}, {m.Parts.Length} boxes]"))}), "
            + $"{map.Solid.Count} boxes, {map.Spawns.Count} spawns, {map.Waypoints.Count} waypoints, fingerprint {map.ContentHash:x8}");
        if (map.Movers.Count < 8 || !map.Movers.Any(m => m.Kind == MoverKind.Rotate) || !map.Movers.Any(m => m.Kind == MoverKind.Slide) || !map.Movers.Any(m => m.Kind == MoverKind.Elevator))
            Fail("Physics Test should have rotating platforms, sliders and an elevator");

        // ---- Fingerprints: only maps with movers change, and movers do count ----
        // Movers ship on the Physics Test and in the High Ground pack (lifts, ferries); the older packs have none.
        var withMovers = Maps.All.Where(m => m.Movers.Count > 0 && m.Pack?.Name != "High Ground").Select(m => m.Name).ToList();
        if (withMovers.Count != 1 || withMovers[0] != PhysicsTestMap)
            Fail($"maps with movers: {string.Join(", ", withMovers)} (expected only {PhysicsTestMap} outside High Ground)");
        if (Maps.All.Any(m => m.Movers.Count == 0 && m.MoverHash != 0))
            Fail("a map without movers has a non-zero mover hash (its fingerprint would change)");
        var json = File.ReadAllText(Path.IsPathRooted(map.SourceFile) ? map.SourceFile : Path.Combine(Maps.Folder, map.SourceFile));
        var stripped = MapFile.Load(json[..json.LastIndexOf(',', json.IndexOf("\"movers\"", StringComparison.Ordinal))] + "\n}\n", "Physics Test (no movers)");
        if (stripped.Movers.Count != 0 || stripped.ContentHash == map.ContentHash)
            Fail("removing the movers section didn't change the fingerprint");
        var tweaked = MapFile.Load(json.Replace("\"speed\": 20,", "\"speed\": 21,"), "Physics Test (tweaked)");
        if (tweaked.ContentHash == map.ContentHash)
            Fail("changing a mover's speed didn't change the fingerprint");
        var copy = MapFile.Load(MapFile.ToJson(map), "Physics Test (round trip)"); // the "client's" copy
        if (copy.ContentHash != map.ContentHash || copy.Movers.Count != map.Movers.Count)
            Fail($"movers round trip changed the map: fingerprint {map.ContentHash:x8} -> {copy.ContentHash:x8}");
        try
        {
            MapFile.Load(json.Replace("{ \"op\": \"Spawn\", \"x\": 2, \"z\": -2 }", "{ \"op\": \"Spawn\", \"x\": -22, \"z\": -20 }"), "spawn on a disc");
            Fail("a spawn inside a mover's path was accepted");
        }
        catch (MapFileException ex)
        {
            Line($"spawn inside a mover's path refused: {ex.Message}");
        }
        // The example in the map guide's Movers section loads.
        var guide = Path.Combine(Maps.Folder, "README.md");
        if (File.Exists(guide))
        {
            var text = File.ReadAllText(guide);
            var at = text.IndexOf("## Movers", StringComparison.Ordinal);
            var start = at < 0 ? -1 : text.IndexOf("```json", at, StringComparison.Ordinal);
            var end = start < 0 ? -1 : text.IndexOf("```", start + 7, StringComparison.Ordinal);
            if (end < 0)
                Fail("Maps/README.md has no movers example");
            else
            {
                try
                {
                    var ex = MapFile.Load("{ \"name\": \"Movers example\", \"halfX\": 40, \"halfZ\": 40, \"ops\": [ { \"op\": \"Ground\", \"color\": [0.5, 0.5, 0.5] }, "
                        + "{ \"op\": \"Spawn\", \"x\": -5, \"z\": 38 } ],\n" + text[(start + 7)..end] + "}", "README movers example");
                    Line($"guide movers example loads: {ex.Movers.Count} movers");
                    if (ex.Movers.Count != 4)
                        Fail($"the guide's movers example has {ex.Movers.Count} movers (expected 4)");
                }
                catch (MapFileException e)
                {
                    Fail("the guide's movers example doesn't load: " + e.Message);
                }
            }
        }

        foreach (var w in map.Waypoints)
            if (map.InMoverSweep(w))
                Fail($"waypoint ({w.X:0.#}, {w.Z:0.#}) is inside a mover's sweep");

        // ---- Pose determinism: same tick, same pose, on separately loaded copies; smooth from tick to tick ----
        uint[] ticks = [0, 1, 59, 60, 61, 1234, 99_999, 5_000_000, 4_000_000_000];
        for (var i = 0; i < map.Movers.Count; i++)
        {
            var m = map.Movers[i];
            foreach (var t in ticks)
                if (m.PoseAt(t) != copy.Movers[i].PoseAt(t) || m.PoseAt(t) != m.PoseAt(t))
                    Fail($"{m.Name}: pose at tick {t} differs between host and client copies");
            var maxStep = m.Speed * 1.5f * World.TickDt * 1.02f + MathF.Abs(m.Spin) * m.Reach * World.TickDt * 1.02f + 1e-4f;
            var worst = 0f;
            for (uint t = 1_000_000; t < 1_000_000 + 1800; t++)
            {
                var a = m.PoseAt(t);
                var b = m.PoseAt(t + 1);
                worst = MathF.Max(worst, Vector3.Distance(a.Origin, b.Origin) + MathF.Abs(World.WrapAngle(b.Yaw - a.Yaw)) * m.Reach);
            }
            if (worst > maxStep)
                Fail($"{m.Name}: jumps {worst:0.000} m in one tick (limit {maxStep:0.000})");
        }
        var lift = map.Movers.First(m => m.Kind == MoverKind.Elevator);
        float lo = float.MaxValue, hi = float.MinValue;
        for (var t = 0; t < 60 * 30; t++)
        {
            var y = lift.PoseAt(t).Origin.Y;
            lo = MathF.Min(lo, y);
            hi = MathF.Max(hi, y);
        }
        Line($"elevator: cycle {lift.Cycle:0.00} s, travels {lo:0.00} to {hi:0.00} m");
        if (MathF.Abs(lo - lift.Points[0].Y) > 0.01f || MathF.Abs(hi - lift.Points[1].Y) > 0.01f)
            Fail("the elevator doesn't reach both ends");

        var previous = World.Map;
        World.SetMap(map);
        try
        {
            CheckMoverRides(map);
            CheckMoverPushes(map);
            CheckMoverMomentum(map);
            CheckMoverRays(map);
            var withGrid = CheckMoverPrediction(map);
            World.UseGrid = false; // the collision grid is for static boxes; movers are queried separately either way
            try
            {
                var noGrid = CheckMoverPrediction(map);
                if (noGrid != withGrid)
                    Fail($"movement on a mover differs without the collision grid: {withGrid} vs {noGrid}");
                CheckMoverGrenade(map);
            }
            finally
            {
                World.UseGrid = true;
            }
            CheckMoverGrenade(map);
            CheckMoverNav(map);
        }
        finally
        {
            MoverPhysics.Clock = 0;
            World.SetMap(previous);
        }
        CheckMoverBotMatch(map);
    }

    private static MoverDef Mover(Map map, string name) => map.Movers.First(m => m.Name == name);

    private static MoveState StandOn(MoverDef m, uint tick, Vector3 offset) =>
        new() { Position = m.PoseAt(tick).ToWorld(offset), Grounded = true };

    /// <summary>A player standing still on each mover is carried with it: feet stay at the same spot of the platform.</summary>
    private static void CheckMoverRides(Map map)
    {
        (string Name, Vector3 Offset, int Ticks)[] rides =
        [
            ("Slow disc", new Vector3(3, 0, 0), 600),
            ("Fast spinner", new Vector3(0.8f, 0, 0), 300),
            ("Slider A", Vector3.Zero, 600),
            ("Loop", new Vector3(0.5f, 0, 0), 1400),
            ("Gap ferry", Vector3.Zero, 700),
            ("Elevator", Vector3.Zero, 650),
        ];
        foreach (var (name, offset, n) in rides)
        {
            var m = Mover(map, name);
            const uint t0 = 10_000;
            var s = StandOn(m, t0, offset);
            var local = m.PoseAt(t0).ToLocal(s.Position);
            float worst = 0, lowest = float.MaxValue, turned = 0;
            var notGrounded = 0;
            var travelled = 0f;
            for (var i = 1; i <= n; i++)
            {
                var t = t0 + (uint)i;
                turned += MoverPhysics.SupportYawDelta(s, t);
                var before = s.Position;
                World.Simulate(ref s, new InputCmd { Tick = t, SpeedPct = 100 });
                travelled += Vector3.Distance(before, s.Position);
                var want = m.PoseAt(t).ToWorld(local);
                worst = MathF.Max(worst, Vector3.Distance(want, s.Position));
                lowest = MathF.Min(lowest, s.Position.Y - want.Y);
                if (!s.Grounded)
                    notGrounded++;
            }
            var spin = m.Spin * n * World.TickDt;
            var turnErr = MathF.Abs(World.WrapAngle(turned - spin));
            Line($"ride {name,-13} {n} ticks: travelled {travelled,6:0.00} m, off the platform's spot by {worst:0.0000} m (lowest {lowest:+0.000;-0.000;0.000}), "
                + $"turned {turned * 180 / MathF.PI:0.0} deg (platform {spin * 180 / MathF.PI:0.0}), airborne {notGrounded} ticks");
            if (worst > 0.05f)
                Fail($"{name}: a player standing on it drifted {worst:0.000} m from their spot");
            if (lowest < -0.05f)
                Fail($"{name}: a player fell {-lowest:0.00} m through it");
            if (notGrounded > 0)
                Fail($"{name}: the rider left the ground for {notGrounded} ticks");
            if (turnErr > 0.01f)
                Fail($"{name}: the rider's yaw turned {turned:0.000} rad but the platform {spin:0.000} rad");
            if (travelled < 1f)
                Fail($"{name}: the rider barely moved ({travelled:0.00} m)");
        }

        // Walking on a platform is relative to it: walk across the slow disc for 1 s and arrive about 6 m along it.
        {
            var m = Mover(map, "Slow disc");
            const uint t0 = 20_000;
            var s = StandOn(m, t0, new Vector3(-3, 0, 0));
            var yaw = m.PoseAt(t0).Yaw + MathF.PI / 2; // facing the platform's +X
            for (var i = 1; i <= 60; i++)
            {
                var t = t0 + (uint)i;
                yaw += MoverPhysics.SupportYawDelta(s, t); // the view turns with the platform, as in the game
                World.Simulate(ref s, new InputCmd { Tick = t, Buttons = Buttons.Forward, Yaw = yaw, SpeedPct = 100 });
            }
            var local = m.PoseAt(t0 + 60).ToLocal(s.Position);
            Line($"walk across the slow disc for 1 s: from local (-3, 0) to ({local.X:0.00}, {local.Z:0.00})");
            if (local.X < 1.5f || local.X > 3.5f || MathF.Abs(local.Z) > 0.3f || !s.Grounded)
                Fail($"walking on the slow disc went wrong: local ({local.X:0.00}, {local.Y:0.00}, {local.Z:0.00}), grounded {s.Grounded}");
        }
    }

    /// <summary>A player in a mover's way is pushed (or lifted), never left inside it or crushed into a wall.</summary>
    private static void CheckMoverPushes(Map map)
    {
        // The spinning bar sweeps across a player standing on the bar disc.
        {
            var disc = Mover(map, "Bar disc");
            const uint t0 = 30_000;
            var s = StandOn(disc, t0, new Vector3(2.5f, 0, 0.6f));
            var inside = 0;
            var pushedMax = 0f;
            var local0 = disc.PoseAt(t0).ToLocal(s.Position);
            for (var i = 1; i <= 360; i++)
            {
                var t = t0 + (uint)i;
                World.Simulate(ref s, new InputCmd { Tick = t, SpeedPct = 100 });
                if (MoverPhysics.Overlaps(s.Position, World.PlayerHeight, t) || World.Blocked(s.Position))
                    inside++;
                pushedMax = MathF.Max(pushedMax, Vector3.Distance(disc.PoseAt(t).ToLocal(s.Position) with { Y = 0 }, local0 with { Y = 0 }));
            }
            Line($"spinning bar: pushed up to {pushedMax:0.00} m across the disc, inside a box on {inside} ticks, ends at y {s.Position.Y:0.00}");
            if (inside > 0)
                Fail($"spinning bar: the player was inside a box on {inside} ticks");
            if (pushedMax < 0.3f)
                Fail("spinning bar: the bar passed through the player instead of pushing them");
            if (s.Position.Y < 0.35f)
                Fail("spinning bar: the player fell through the disc");
        }

        // The elevator comes down on a player standing under it: they're pushed out, not crushed.
        {
            var lift = Mover(map, "Elevator");
            uint top = 0;
            for (uint t = 40_000; t < 40_000 + 1200 && top == 0; t++)
                if (lift.PoseAt(t).Origin.Y > lift.Points[1].Y - 0.01f && lift.PoseAt(t + 1).Origin.Y < lift.PoseAt(t).Origin.Y)
                    top = t; // the moment it starts down
            var s = new MoveState { Position = new Vector3(lift.Points[0].X + 0.3f, 0, lift.Points[0].Z), Grounded = true };
            var inside = 0;
            for (var i = 1; i <= 400; i++)
            {
                var t = top + (uint)i;
                World.Simulate(ref s, new InputCmd { Tick = t, SpeedPct = 100 });
                if (MoverPhysics.Overlaps(s.Position, World.PlayerHeight, t) || World.Blocked(s.Position))
                    inside++;
            }
            var under = MoverPhysics.SupportMover(s.Position, top + 400) >= 0;
            Line($"elevator coming down on a player: ends at ({s.Position.X:0.00}, {s.Position.Y:0.00}, {s.Position.Z:0.00}), inside on {inside} ticks, standing on it {under}");
            if (inside > 0)
                Fail($"elevator: the player was inside it on {inside} ticks");
            if (s.Position.Y > 1f && !under)
                Fail("elevator: the player was squeezed up onto the deck instead of being pushed out sideways");
        }

        // A low slider runs into a player on the floor: it scoops them up (it's a step), it doesn't pass through.
        {
            var slider = Mover(map, "Slider A");
            var s = new MoveState { Position = new Vector3(10, 0, -8), Grounded = true };
            var inside = 0;
            var maxY = 0f;
            for (var i = 1; i <= 600; i++)
            {
                var t = 50_000 + (uint)i;
                World.Simulate(ref s, new InputCmd { Tick = t, SpeedPct = 100 });
                if (MoverPhysics.Overlaps(s.Position, World.PlayerHeight, t))
                    inside++;
                maxY = MathF.Max(maxY, s.Position.Y);
            }
            Line($"slider meets a standing player: highest feet {maxY:0.00} m, inside it on {inside} ticks");
            if (inside > 0 || maxY < 0.35f)
                Fail($"slider: the player was inside it on {inside} ticks / never lifted (max y {maxY:0.00})");
            _ = slider;
        }

        // Never crushed: feet stuck inside a mover box get put on top.
        {
            var m = Mover(map, "Slow disc");
            var p = m.PoseAt(60_000).ToWorld(new Vector3(1, -0.2f, 0));
            var fixedFeet = MoverPhysics.Unstick(p, World.PlayerHeight, 60_000);
            if (MoverPhysics.BlockedAll(fixedFeet, World.PlayerHeight, 60_000) || fixedFeet.Y < m.PoseAt(60_000).Origin.Y - 0.01f)
                Fail($"unstick: feet inside the disc weren't freed (now {fixedFeet})");
        }
    }

    /// <summary>Jumping off a moving platform keeps its momentum.</summary>
    private static void CheckMoverMomentum(Map map)
    {
        var ferry = Mover(map, "Gap ferry");
        // Mid-leg, when the ferry is at full speed.
        uint mid = 0;
        for (uint t = 70_000; t < 70_000 + 1200 && mid == 0; t++)
            if (MathF.Abs(ferry.PoseAt(t).Origin.X - 17f) < 0.05f)
                mid = t;
        var v = (ferry.PoseAt(mid + 1).Origin - ferry.PoseAt(mid).Origin) / World.TickDt;
        var s = StandOn(ferry, mid - 1, Vector3.Zero);
        World.Simulate(ref s, new InputCmd { Tick = mid, SpeedPct = 100 });
        World.Simulate(ref s, new InputCmd { Tick = mid + 1, Buttons = Buttons.Jump, SpeedPct = 100 });
        var start = s.Position;
        for (var i = 2; i <= 11; i++)
            World.Simulate(ref s, new InputCmd { Tick = mid + (uint)i, SpeedPct = 100 });
        var airSpeed = (s.Position.X - start.X) / (10 * World.TickDt);
        Line($"jump off the ferry at {v.X:0.00} m/s: {airSpeed:0.00} m/s through the air over the next 10 ticks, velocity {s.Velocity.X:0.00} m/s, grounded {s.Grounded}");
        if (s.Grounded || MathF.Abs(v.X) < 1f || MathF.Sign(airSpeed) != MathF.Sign(v.X) || MathF.Abs(airSpeed) < MathF.Abs(v.X) * 0.6f)
            Fail($"jumping off the ferry lost its momentum (ferry {v.X:0.00} m/s, in the air {airSpeed:0.00} m/s)");

        // Jumping off the rising elevator goes higher than a normal jump.
        var lift = Mover(map, "Elevator");
        uint rising = 0;
        for (uint t = 80_000; t < 80_000 + 1200 && rising == 0; t++)
        {
            var dy = lift.PoseAt(t + 1).Origin.Y - lift.PoseAt(t).Origin.Y;
            if (dy > 1.4f * World.TickDt && lift.PoseAt(t).Origin.Y < 2f)
                rising = t;
        }
        var e = StandOn(lift, rising - 1, Vector3.Zero);
        World.Simulate(ref e, new InputCmd { Tick = rising, SpeedPct = 100 });
        World.Simulate(ref e, new InputCmd { Tick = rising + 1, Buttons = Buttons.Jump, SpeedPct = 100 });
        Line($"jump off the rising elevator: vertical speed {e.Velocity.Y:0.00} m/s (a normal jump is {World.JumpSpeed:0.00} minus a tick of gravity)");
        if (e.Velocity.Y < World.JumpSpeed + 0.5f)
            Fail($"jumping off the rising elevator didn't keep its upward speed ({e.Velocity.Y:0.00} m/s)");
    }

    /// <summary>Ray casts and grenades hit movers at their current pose, including rotated ones.</summary>
    private static void CheckMoverRays(Map map)
    {
        var spinner = Mover(map, "Fast spinner"); // 3 x 3 m, 90 degrees per second: 45 degrees at tick 30
        var pivot = spinner.Points[0];
        var probe = new Vector3(pivot.X + 1.9f, 5, pivot.Z); // inside the corner of the turned square only
        MoverPhysics.Clock = 30;
        var hitTurned = World.RaycastWorld(probe, -Vector3.UnitY, 10);
        MoverPhysics.Clock = 0;
        var hitSquare = World.RaycastWorld(probe, -Vector3.UnitY, 10);
        Line($"ray down 1.9 m from the spinner's pivot: {hitTurned:0.00} m at 45 degrees (platform top at {5 - pivot.Y:0.00}), {hitSquare:0.00} m at 0 degrees (floor at 5.00)");
        if (MathF.Abs(hitTurned - (5 - pivot.Y)) > 0.01f || MathF.Abs(hitSquare - 5) > 0.01f)
            Fail("ray casts don't follow the rotated platform");

        var ferry = Mover(map, "Gap ferry");
        foreach (var t in new uint[] { 90_000, 90_200, 90_400 })
        {
            MoverPhysics.Clock = t;
            var o = ferry.PoseAt(t).Origin;
            var d = World.RaycastWorld(new Vector3(o.X, o.Y + 3, o.Z), -Vector3.UnitY, 10);
            if (MathF.Abs(d - 3) > 0.01f)
                Fail($"ray down onto the ferry at tick {t} hit at {d:0.00} m (want 3.00)");
            if (!MoverPhysics.PointInside(o + new Vector3(0, -0.2f, 0), Throwables.ProjectileRadius, t))
                Fail("a grenade inside the ferry isn't inside it");
        }
        MoverPhysics.Clock = 0;

        // Someone seen on a turning platform at one tick and carried to another lands where the platform took them.
        var disc = Mover(map, "Slow disc");
        var feet = disc.PoseAt(100).ToWorld(new Vector3(3, 0, 0));
        var yaw = 0f;
        var carried = MoverPhysics.CarryBetween(feet, 100, 160, ref yaw);
        var want = disc.PoseAt(160).ToWorld(new Vector3(3, 0, 0));
        if (Vector3.Distance(carried, want) > 0.01f || MathF.Abs(yaw - disc.Spin) > 0.01f)
            Fail($"CarryBetween: {carried} (want {want}), yaw {yaw:0.000} (want {disc.Spin:0.000})");
    }

    /// <summary>Prediction: replaying stamped commands from a snapshot gives exactly the server's result.</summary>
    private static Vector3 CheckMoverPrediction(Map map)
    {
        var disc = Mover(map, "Slow disc");
        const uint t0 = 200_000;
        var cmds = new List<InputCmd>();
        var rng = new Random(3);
        for (var i = 1; i <= 240; i++)
        {
            var b = Buttons.None;
            if (rng.Next(3) == 0) b |= Buttons.Forward;
            if (rng.Next(4) == 0) b |= Buttons.Left;
            if (i % 70 == 0) b |= Buttons.Jump;
            cmds.Add(new InputCmd { Seq = (uint)i, Tick = t0 + (uint)i, Buttons = b, Yaw = rng.NextSingle() * 6.28f, SpeedPct = 100 });
        }
        var server = StandOn(disc, t0, new Vector3(2, 0, 1));
        var client = server;
        var snapshots = new List<MoveState>();
        foreach (var c in cmds)
        {
            World.Simulate(ref server, c);
            snapshots.Add(server);
        }
        foreach (var c in cmds)
            World.Simulate(ref client, c);
        var mismatches = 0;
        for (var k = 0; k < cmds.Count; k += 17)
        {
            var replay = snapshots[k];
            MoverPhysics.Clock = 12345; // replays must not depend on the current clock
            for (var j = k + 1; j < cmds.Count; j++)
                World.Simulate(ref replay, cmds[j]);
            if (replay.Position != server.Position || replay.Velocity != server.Velocity)
                mismatches++;
        }
        MoverPhysics.Clock = 0;
        Line($"prediction on the slow disc: client {(client.Position == server.Position ? "matches" : "DIFFERS from")} the server after 240 ticks; "
            + $"{mismatches} replay mismatches; ends at ({server.Position.X:0.00}, {server.Position.Y:0.00}, {server.Position.Z:0.00})");
        if (client.Position != server.Position || mismatches > 0)
            Fail("predicted movement on a mover doesn't replay exactly");
        return server.Position;
    }

    /// <summary>A grenade dropped onto the elevator deck while it's parked at the bottom comes to rest on it, not inside it.</summary>
    private static void CheckMoverGrenade(Map map)
    {
        var lift = Mover(map, "Elevator");
        uint parked = 0;
        for (uint t = 300_000; t < 300_000 + 1200 && parked == 0; t++)
            if (lift.PoseAt(t).Origin.Y < lift.Points[0].Y + 0.001f && lift.PoseAt(t + 60).Origin.Y < lift.Points[0].Y + 0.001f)
                parked = t;
        var top = lift.Points[0];
        var g = new Projectile { Pos = top + new Vector3(0.3f, 1.5f, 0.2f) };
        for (var i = 0; i < 60; i++)
        {
            MoverPhysics.Clock = parked + (uint)i;
            Throwables.Step(g, World.TickDt);
        }
        MoverPhysics.Clock = 0;
        Line($"grenade on the parked elevator (grid {(World.UseGrid ? "on" : "off")}): rests at y {g.Pos.Y:0.00} (deck top {top.Y:0.00})");
        if (g.Pos.Y < top.Y || g.Pos.Y > top.Y + 0.3f)
            Fail($"a grenade dropped on the elevator ended at y {g.Pos.Y:0.00} (deck top {top.Y:0.00}, grid {(World.UseGrid ? "on" : "off")})");
    }

    /// <summary>The nav graph keeps out of every mover's sweep, and bots can still get between the spawns.</summary>
    private static void CheckMoverNav(Map map)
    {
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var inside = g.Nodes.Count(n => map.Movers.Any(m =>
        {
            var s = m.Sweep;
            return n.X > s.Min.X && n.X < s.Max.X && n.Z > s.Min.Z && n.Z < s.Max.Z && n.Y + World.PlayerHeight > s.Min.Y;
        }));
        var search = new NavSearch();
        var path = new int[256];
        var unreachable = 0;
        foreach (var a in map.Spawns)
            foreach (var b in map.Spawns)
                if (a != b && search.Find(g, g.Nearest(a), g.Nearest(b, needSight: false), path) == 0)
                    unreachable++;
        Line($"nav graph: {g.Nodes.Length} nodes, {g.EdgeCount} edges, {inside} nodes inside a mover's sweep, {unreachable} unreachable spawn pairs");
        if (g.Nodes.Length < 20)
            Fail("Physics Test nav graph is too small");
        if (inside > 0)
            Fail($"{inside} nav nodes are inside a mover's sweep");
        if (unreachable > map.Spawns.Count)
            Fail($"{unreachable} spawn pairs have no path on Physics Test");
    }

    /// <summary>A short bot match on Physics Test: nobody falls through, gets stuck or ends up inside a mover.</summary>
    private static void CheckMoverBotMatch(Map map)
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: Game.MaxPlayers - 1, botSkill: BotDifficulty.Normal, mapId: map.Id);
        game.MapVoteEnabled = false;
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var moved = new Dictionary<byte, float>();
        var last = new Dictionary<byte, Vector3>();
        var insideTicks = 0;
        var fallen = 0;
        for (var i = 0; i < 45 * World.TickRate; i++)
        {
            now += dt;
            game.Update(now, dt);
            foreach (var p in game.Players.Values)
            {
                if (!p.Alive)
                    continue;
                if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                {
                    Fail($"player {p.Id} position is NaN on Physics Test");
                    return;
                }
                if (MoverPhysics.Overlaps(p.Position, World.HeightOf(p.Sim.Stance), MoverPhysics.Clock))
                    insideTicks++;
                if (p.Position.Y < -0.5f)
                    fallen++;
            }
            if (i % 60 == 0)
                foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                {
                    if (last.TryGetValue(p.Id, out var lp))
                        moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                    last[p.Id] = p.Position;
                }
        }
        var stuck = moved.Count(kv => kv.Value < 10f);
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        Line($"bot match on Physics Test (45 s): kills {kills}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, stuck {stuck}, "
            + $"player-ticks inside a mover {insideTicks}, below the floor {fallen}");
        if (stuck > 1)
            Fail($"Physics Test: {stuck} bots barely moved");
        if (fallen > 0)
            Fail($"Physics Test: players fell below the floor ({fallen} player-ticks)");
        // (A player mid-mantle isn't pushed for those few ticks, so allow a moment; anything more is a real overlap.)
        if (insideTicks > 60)
            Fail($"Physics Test: players were inside a mover on {insideTicks} player-ticks");
    }
}
