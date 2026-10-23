using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>The swept mover's part of the "Physics" self-test section (see SelfTestPhysics.cs).</summary>
public static partial class SelfTest
{
    private static readonly float[] LabSteps = [0.30f, 0.40f, 0.55f, 0.60f, 0.70f];
    private static readonly float[] LabRamps = [30f, 40f, 45f, 50f];

    /// <summary>
    /// A hand-built scene, walked towards -Z from z = 0 (yaw 0): single steps of 0.30-0.70 m (x = -54..-30), a 0.4 m staircase
    /// to 2.4 m (x = -22), ramps of 30/40/45/50 degrees (oriented boxes, x = -12..12, added by <see cref="LabRamp"/>), a ceiling
    /// at 1.25 m (x = 22), a 1.2 m crate (x = 30), a 5 cm wall (x = 38..58) and a 5 cm floor plate 10 m up at (0, 30).
    /// </summary>
    private static Map PhysicsLab()
    {
        var grey = new Vector3(0.5f);
        var m = new Map { Name = "Physics lab", Description = "self-test scene", HalfX = 60, HalfZ = 60 };
        m.Ground(grey);
        for (var i = 0; i < LabSteps.Length; i++)
        {
            var x = -54 + i * 6;
            m.Block(x - 2, 0, -8, x + 2, LabSteps[i], -3, grey);
        }
        for (var k = 1; k <= 6; k++)
            m.Block(-24, 0, -12, -20, 0.4f * k, -3 - 0.5f * (k - 1), grey);
        m.Block(20, 1.25f, -10, 24, 2.5f, -3, grey); // ceiling: only a crouch fits under it
        m.Block(28, 0, -6, 32, 1.2f, -3, grey); // a crate to mantle onto
        m.Block(38, 0, -3.05f, 58, 3, -3f, grey); // a thin wall
        m.Block(-2, 10, 28, 2, 10.05f, 32, grey); // a thin floor high up
        m.Spawn(0, 20);
        return m;
    }

    /// <summary>A ramp rising towards -Z from (x, 0, -3), 8 m long along the slope, 4 m wide: an oriented box turned about X.</summary>
    private static Obb LabRamp(float x, float degrees)
    {
        var th = degrees * MathF.PI / 180;
        const float len = 8;
        var (s, c) = MathF.SinCos(th);
        var center = new Vector3(x, -(0.25f * c - len / 2 * s), -3 - (0.25f * s + len / 2 * c));
        return new Obb(center, new Vector3(2, 0.25f, len / 2), Quaternion.CreateFromAxisAngle(Vector3.UnitX, th));
    }

    private static void SetLab(Map lab)
    {
        World.SetMap(lab);
        for (var i = 0; i < LabRamps.Length; i++)
            World.Physics.AddObb(LabRamp(-12 + i * 8, LabRamps[i]));
    }

    private static MoveState Walk(MoveState s, Buttons b, float yaw, int ticks, ref float maxY, Func<MoveState, bool>? stop = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            World.Simulate(ref s, new InputCmd { Buttons = b, Yaw = yaw, SpeedPct = 100 });
            maxY = MathF.Max(maxY, s.Position.Y);
            if (stop != null && stop(s))
                break;
        }
        return s;
    }

    private static MoveState Stand(float x, float y, float z) => new() { Position = new Vector3(x, y, z), Grounded = true };

    private static void SweptBehaviour()
    {
        var lab = PhysicsLab();
        SetLab(lab);
        var feel = new Dictionary<PhysicsMode, string>();
        foreach (var mover in new IMover[] { ClassicMover.Instance, SweptMover.Instance })
        {
            World.Mover = mover;
            var swept = mover.Mode == PhysicsMode.Swept;
            var notes = new List<string>();
            float maxY = 0;

            // Single steps.
            var climbed = new List<string>();
            for (var i = 0; i < LabSteps.Length; i++)
            {
                maxY = 0;
                var s = Walk(Stand(-54 + i * 6, 0, 0), Buttons.Forward, 0, 60, ref maxY);
                var up = maxY > LabSteps[i] - 0.05f; // climbed it (and may have walked off the far end since)
                climbed.Add($"{LabSteps[i]:0.00}{(up ? "+" : "-")}");
                if (swept && (LabSteps[i] <= World.StepHeight) != up && LabSteps[i] != 0.60f)
                    Fail($"swept mover: a {LabSteps[i]:0.00} m step was {(up ? "climbed" : "not climbed")} (ends at y {s.Position.Y:0.00}, z {s.Position.Z:0.00})");
            }
            notes.Add("steps " + string.Join(" ", climbed));

            // Stairs up and down.
            var upTicks = -1;
            var st = Stand(-22, 0, 0);
            for (var i = 0; i < 240 && upTicks < 0; i++)
            {
                World.Simulate(ref st, new InputCmd { Buttons = Buttons.Forward, Yaw = 0, SpeedPct = 100 });
                if (st.Position.Y > 2.39f)
                    upTicks = i + 1;
            }
            var downAir = 0;
            var dn = Stand(-22, 2.4f, -11);
            for (var i = 0; i < 120; i++)
            {
                World.Simulate(ref dn, new InputCmd { Buttons = Buttons.Forward, Yaw = MathF.PI, SpeedPct = 100 });
                if (!dn.Grounded)
                    downAir++;
            }
            notes.Add($"stairs up {(upTicks < 0 ? "never" : $"{upTicks / 60f:0.00} s")}, down airborne {downAir} ticks");
            if (swept && upTicks < 0)
                Fail("swept mover: couldn't climb the 0.4 m staircase");
            if (swept && (dn.Position.Z < -3 || dn.Position.Y > 0.05f))
                Fail($"{mover.Mode}: walking down the stairs ended at ({dn.Position.X:0.00}, {dn.Position.Y:0.00}, {dn.Position.Z:0.00})");

            // Ramps: walkable up to 42 degrees; steeper ones are walls and you slide down them.
            var ramps = new List<string>();
            for (var i = 0; i < LabRamps.Length && swept; i++) // oriented boxes: the classic mover only knows map boxes
            {
                maxY = 0;
                Walk(Stand(-12 + i * 8, 0, 0), Buttons.Forward, 0, 150, ref maxY);
                var ramp = LabRamp(-12 + i * 8, LabRamps[i]);
                var n = ramp.AxisY;
                var surface = ramp.ToWorld(new Vector3(0, 0.25f, 0));
                var feet = surface + n * (SweptMover.RadiusOf(Stance.Stand) + 0.005f) - Vector3.UnitY * SweptMover.RadiusOf(Stance.Stand);
                var rest = new MoveState { Position = feet, Grounded = true };
                float ignore = 0;
                rest = Walk(rest, Buttons.None, 0, 60, ref ignore);
                var slid = rest.Position.Y - feet.Y;
                ramps.Add($"{LabRamps[i]:0}deg up to {maxY:0.0} m, idle {slid:+0.00;-0.00} m");
                var walkable = LabRamps[i] < World.MaxSlopeDeg;
                if (walkable && maxY < 2f)
                    Fail($"swept mover: couldn't walk up a {LabRamps[i]:0} degree ramp (highest {maxY:0.00} m)");
                if (!walkable && maxY > 0.6f)
                    Fail($"swept mover: walked {maxY:0.00} m up a {LabRamps[i]:0} degree ramp (steeper than {World.MaxSlopeDeg} degrees)");
                if (walkable && MathF.Abs(slid) > 0.05f)
                    Fail($"swept mover: standing on a {LabRamps[i]:0} degree ramp slid {slid:0.00} m");
                if (!walkable && slid > -0.3f)
                    Fail($"swept mover: didn't slide down a {LabRamps[i]:0} degree ramp ({slid:0.00} m)");
            }
            if (swept)
                notes.Add("ramps " + string.Join(", ", ramps));

            // Thin wall: straight on and at an angle at sprint speed, and much faster through the air.
            var through = 0;
            foreach (var (x, yaw, ticks) in new[] { (42f, 0f, 60), (40f, 0.6f, 50), (52f, -0.4f, 50) })
            {
                var s = Stand(x, 0, 0);
                for (var i = 0; i < ticks; i++)
                {
                    World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = yaw, SpeedPct = 100 });
                    if (s.Position.Z < -3.0f)
                        through++;
                }
            }
            var fast = new MoveState { Position = new Vector3(48, 1, 0), Velocity = new Vector3(0, 0, -40) };
            for (var i = 0; i < 30; i++)
            {
                World.Simulate(ref fast, new InputCmd { SpeedPct = 100 });
                if (fast.Position.Z < -3.0f)
                    through++;
            }
            var drop = new MoveState { Position = new Vector3(0, 40, 30), Velocity = new Vector3(0, -30, 0) };
            for (var i = 0; i < 120; i++)
                World.Simulate(ref drop, new InputCmd { SpeedPct = 100 });
            notes.Add($"thin wall passed {through} ticks, 30 m/s drop onto a 5 cm plate ends at y {drop.Position.Y:0.00}");
            if (swept && through > 0)
                Fail($"swept mover: went through a 5 cm wall ({through} ticks beyond it)");
            if (swept && (drop.Position.Y < 9.99f || drop.Position.Y > 10.1f || !drop.Grounded))
                Fail($"swept mover: falling at 30 m/s onto a 5 cm plate ended at y {drop.Position.Y:0.00} (plate top 10.05), grounded {drop.Grounded}");

            // Ceiling at 1.25 m: standing is blocked, crouching gets under, and you can't stand up under it.
            var stand = Walk(Stand(22, 0, 0), Buttons.Forward, 0, 90, ref maxY);
            var crouch = Walk(Stand(22, 0, 0), Buttons.Forward | Buttons.Crouch, 0, 120, ref maxY);
            var under = Walk(crouch, Buttons.None, 0, 10, ref maxY);
            notes.Add($"ceiling 1.25 m: standing stops at z {stand.Position.Z:0.00}, crouched reaches z {crouch.Position.Z:0.00}, stands up under it: {under.Stance == Stance.Stand}");
            if (swept && (stand.Position.Z < -3f || crouch.Position.Z > -5f || under.Stance == Stance.Stand))
                Fail($"swept mover: ceiling test failed (standing z {stand.Position.Z:0.00}, crouched z {crouch.Position.Z:0.00}, stance under it {under.Stance})");

            // Mantle onto a 1.2 m crate.
            var mantled = false;
            var mt = Stand(30, 0, 0);
            for (var i = 0; i < 90; i++)
            {
                World.Simulate(ref mt, new InputCmd { Buttons = Buttons.Forward | Buttons.Jump, Yaw = 0, SpeedPct = 100 });
                mantled |= mt.MantleTicks > 0;
                if (mantled && mt.MantleTicks == 0)
                    break; // on top (don't keep jumping about up there)
            }
            notes.Add($"mantle onto 1.2 m: {mantled}, ends at y {mt.Position.Y:0.00}");
            if (swept && (!mantled || mt.Position.Y < 1.15f))
                Fail($"swept mover: didn't mantle onto the 1.2 m crate (mantled {mantled}, y {mt.Position.Y:0.00})");

            // Feel: walk, sprint, stopping and jumping on flat ground.
            float ign = 0;
            var w0 = Stand(-40, 0, 20);
            var walk = Walk(w0, Buttons.Forward, MathF.PI / 2, 60, ref ign);
            var sprint = Walk(w0, Buttons.Forward | Buttons.Sprint, MathF.PI / 2, 60, ref ign);
            var stop = Walk(sprint, Buttons.None, MathF.PI / 2, 60, ref ign);
            var apex = 0f;
            var jump = Walk(Stand(-40, 0, 40), Buttons.Jump, 0, 1, ref apex);
            Walk(jump, Buttons.None, 0, 59, ref apex);
            notes.Add($"walk 1 s {walk.Position.X - w0.Position.X:0.00} m, sprint 1 s {sprint.Position.X - w0.Position.X:0.00} m, stops in {stop.Position.X - sprint.Position.X:0.00} m, jump apex {apex:0.000} m");
            if (swept && (MathF.Abs(walk.Position.X - w0.Position.X - 5.62f) > 0.3f || apex < 0.85f || apex > 1.05f))
                Fail($"swept mover: flat-ground feel is off (walk {walk.Position.X - w0.Position.X:0.00} m in 1 s, jump apex {apex:0.00} m)");
            feel[mover.Mode] = string.Join("; ", notes);
        }
        World.Mover = ClassicMover.Instance;
        Line("classic: " + feel[PhysicsMode.Classic]);
        Line("swept:   " + feel[PhysicsMode.Swept]);
        PhysicsSummary.Add("lab classic: " + feel[PhysicsMode.Classic]);
        PhysicsSummary.Add("lab swept: " + feel[PhysicsMode.Swept]);
    }

    private static void SweptTerrain()
    {
        var m = LabMap();
        World.SetMap(m);
        var t = m.TerrainCollision!;
        var results = new Dictionary<PhysicsMode, (float Dist, float MinClear, float RampTop, int Nan)>();
        foreach (var mover in new IMover[] { ClassicMover.Instance, SweptMover.Instance })
        {
            World.Mover = mover;
            var dist = 0f;
            var minClear = float.MaxValue;
            var nan = 0;
            for (var k = 0; k < 8; k++)
            {
                var yaw = k * MathF.Tau / 8;
                var x = 5f * MathF.Cos(yaw);
                var z = 5f * MathF.Sin(yaw);
                var s = Stand(x, t.HeightAt(x, z), z);
                for (var i = 0; i < 180; i++)
                {
                    var before = s.Position;
                    World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | (i % 45 == 20 ? Buttons.Jump : Buttons.None), Yaw = yaw, SpeedPct = 100 });
                    if (!float.IsFinite(s.Position.X + s.Position.Y + s.Position.Z))
                    {
                        nan++;
                        break;
                    }
                    dist += new Vector2(s.Position.X - before.X, s.Position.Z - before.Z).Length();
                    minClear = MathF.Min(minClear, s.Position.Y - t.HeightAt(s.Position.X, s.Position.Z));
                }
            }
            // The lab's ramp: 6 m up over 30 m (z = 30 -> 0 at x = 10).
            var r = Stand(10, t.HeightAt(10, 29), 29);
            float top = 0;
            Walk(r, Buttons.Forward, 0, 360, ref top);
            results[mover.Mode] = (dist, minClear, top, nan);
        }
        World.Mover = ClassicMover.Instance;
        var c = results[PhysicsMode.Classic];
        var sw = results[PhysicsMode.Swept];
        Line($"terrain lab, 8 sprints x 3 s: classic {c.Dist:0} m (lowest {c.MinClear:+0.000;-0.000} m above ground), swept {sw.Dist:0} m (lowest {sw.MinClear:+0.000;-0.000}); "
            + $"ramp to 6 m: classic {c.RampTop:0.00} m, swept {sw.RampTop:0.00} m");
        PhysicsSummary.Add($"terrain: sprint distance classic {c.Dist:0} m / swept {sw.Dist:0} m, lowest clearance {c.MinClear:0.000}/{sw.MinClear:0.000}, ramp top {c.RampTop:0.00}/{sw.RampTop:0.00}");
        if (sw.Nan > 0)
            Fail("swept mover: position became NaN on terrain");
        if (sw.MinClear < -0.1f)
            Fail($"swept mover: sank {-sw.MinClear:0.00} m into the terrain");
        if (sw.RampTop < 5.5f)
            Fail($"swept mover: didn't walk up the terrain lab's ramp (highest {sw.RampTop:0.00} m)");
        if (sw.Dist < c.Dist * 0.6f)
            Fail($"swept mover: covered much less ground on terrain than the classic mover ({sw.Dist:0} m vs {c.Dist:0} m)");
    }

    /// <summary>Is the (slightly shrunk) body capsule inside static geometry or a mover at <paramref name="tick"/>?</summary>
    private static bool CapsuleInside(in MoveState s, uint tick)
    {
        var c = SweptMover.CapsuleAt(s.Position, s.Stance, Vector3.UnitY);
        return World.Physics.OverlapCapsule(c with { Radius = c.Radius - 0.03f }, new QueryFilter(QueryLayers.Static | QueryLayers.Movers, tick));
    }

    private static void SweptOnMovers()
    {
        var map = Maps.Find(PhysicsTestMap);
        if (map == null)
            return;
        World.SetMap(map);
        World.Mover = SweptMover.Instance;
        var lines = new List<string>();
        try
        {
            (string Name, Vector3 Offset, int Ticks)[] rides =
            [
                ("Slow disc", new Vector3(3, 0, 0), 600),
                ("Slider A", Vector3.Zero, 600),
                ("Gap ferry", Vector3.Zero, 700),
                ("Elevator", Vector3.Zero, 650),
            ];
            foreach (var (name, offset, n) in rides)
            {
                var m = Mover(map, name);
                const uint t0 = 10_000;
                var s = StandOn(m, t0, offset);
                var local = m.PoseAt(t0).ToLocal(s.Position);
                float worst = 0, lowest = float.MaxValue, travelled = 0;
                var air = 0;
                for (var i = 1; i <= n; i++)
                {
                    var t = t0 + (uint)i;
                    var before = s.Position;
                    World.Simulate(ref s, new InputCmd { Tick = t, SpeedPct = 100 });
                    travelled += Vector3.Distance(before, s.Position);
                    var want = m.PoseAt(t).ToWorld(local);
                    worst = MathF.Max(worst, Vector3.Distance(want, s.Position));
                    lowest = MathF.Min(lowest, s.Position.Y - want.Y);
                    if (!s.Grounded)
                        air++;
                }
                lines.Add($"{name} drift {worst:0.000} m, airborne {air}");
                if (worst > 0.08f || lowest < -0.05f || air > 3 || travelled < 1f)
                    Fail($"swept mover riding {name}: drifted {worst:0.000} m, lowest {lowest:0.000} m, airborne {air} ticks, travelled {travelled:0.0} m");
            }

            // The spinning bar sweeps across a player standing on the bar disc: pushed, never inside.
            {
                var disc = Mover(map, "Bar disc");
                const uint t0 = 30_000;
                var s = StandOn(disc, t0, new Vector3(2.5f, 0, 0.6f));
                var inside = 0;
                var pushed = 0f;
                var local0 = disc.PoseAt(t0).ToLocal(s.Position);
                for (var i = 1; i <= 360; i++)
                {
                    var t = t0 + (uint)i;
                    World.Simulate(ref s, new InputCmd { Tick = t, SpeedPct = 100 });
                    if (CapsuleInside(s, t))
                        inside++;
                    pushed = MathF.Max(pushed, Vector3.Distance(disc.PoseAt(t).ToLocal(s.Position) with { Y = 0 }, local0 with { Y = 0 }));
                }
                lines.Add($"bar pushes {pushed:0.00} m, inside {inside}");
                if (inside > 2 || pushed < 0.3f || s.Position.Y < 0.35f)
                    Fail($"swept mover and the spinning bar: inside on {inside} ticks, pushed {pushed:0.00} m, ends at y {s.Position.Y:0.00}");
            }

            // The elevator comes down on a player under it: out, not crushed.
            {
                var lift = Mover(map, "Elevator");
                uint top = 0;
                for (uint t = 40_000; t < 40_000 + 1200 && top == 0; t++)
                    if (lift.PoseAt(t).Origin.Y > lift.Points[1].Y - 0.01f && lift.PoseAt(t + 1).Origin.Y < lift.PoseAt(t).Origin.Y)
                        top = t;
                var s = Stand(lift.Points[0].X + 0.3f, 0, lift.Points[0].Z);
                var inside = 0;
                for (var i = 1; i <= 400; i++)
                {
                    var t = top + (uint)i;
                    World.Simulate(ref s, new InputCmd { Tick = t, SpeedPct = 100 });
                    if (CapsuleInside(s, t))
                        inside++;
                }
                lines.Add($"elevator down: inside {inside}, ends y {s.Position.Y:0.00}");
                if (inside > 2)
                    Fail($"swept mover: inside the descending elevator on {inside} ticks");
            }

            // Jumping off the ferry keeps its momentum.
            {
                var ferry = Mover(map, "Gap ferry");
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
                var air = (s.Position.X - start.X) / (10 * World.TickDt);
                lines.Add($"ferry jump {air:0.00} of {v.X:0.00} m/s");
                if (MathF.Sign(air) != MathF.Sign(v.X) || MathF.Abs(air) < MathF.Abs(v.X) * 0.6f)
                    Fail($"swept mover: jumping off the ferry lost its momentum ({air:0.00} m/s, ferry {v.X:0.00} m/s)");
            }
        }
        finally
        {
            World.Mover = ClassicMover.Instance;
            MoverPhysics.Clock = 0;
        }
        Line("swept mover on movers: " + string.Join("; ", lines));
        PhysicsSummary.Add("swept movers: " + string.Join("; ", lines));
    }

    private static void PhysicsCosts()
    {
        var results = new List<string>();
        foreach (var map in new[] { FindMap("Farmland", m => m.MaxPlayers == Game.MaxPlayers), Maps.All.FirstOrDefault(m => m.HasTerrain), Maps.Find(PhysicsTestMap) })
        {
            if (map == null)
                continue;
            World.SetMap(map);
            var streams = Enumerable.Range(0, 32).Select(i => InputStream(500 + i, 300, map.Movers.Count > 0 ? 400_000u : 0)).ToArray();
            var parts = new List<string>();
            foreach (var mover in new IMover[] { ClassicMover.Instance, SweptMover.Instance })
            {
                World.Mover = mover;
                var states = new MoveState[32];
                for (var i = 0; i < 32; i++)
                    states[i] = Stand(map.Spawns[i % map.Spawns.Count].X, map.Spawns[i % map.Spawns.Count].Y, map.Spawns[i % map.Spawns.Count].Z);
                var ticks = new List<double>();
                for (var t = 0; t < 300; t++)
                {
                    var t0 = Stopwatch.GetTimestamp();
                    for (var i = 0; i < 32; i++)
                        World.Simulate(ref states[i], streams[i][t]);
                    if (t >= 30)
                        ticks.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                }
                ticks.Sort();
                var mean = ticks.Average();
                var p99 = ticks[(int)(ticks.Count * 0.99)];
                parts.Add($"{mover.Mode} {mean * 1000:0} us (p99 {p99 * 1000:0})");
                if (mover.Mode == PhysicsMode.Swept && p99 > 4)
                    Fail($"{map.Name}: the swept mover took {p99:0.0} ms (p99) for 32 players in one tick (budget 4 ms of the 8 ms tick)");
            }
            results.Add($"{map.Name} 32 players/tick: " + string.Join(", ", parts));
        }
        World.Mover = ClassicMover.Instance;

        // Per query, on the biggest map.
        var big = Maps.All.OrderByDescending(m => m.Solid.Count).First();
        World.SetMap(big);
        var q = World.Query;
        var rng = new Random(3);
        const int n = 4000;
        var pts = new Vector3[n];
        var dirs = new Vector3[n];
        var top = MapTop(big);
        for (var i = 0; i < n; i++)
        {
            pts[i] = new Vector3((float)(rng.NextDouble() * 2 - 1) * big.HalfX, (float)rng.NextDouble() * top, (float)(rng.NextDouble() * 2 - 1) * big.HalfZ);
            dirs[i] = RandomDir(rng, i);
        }
        double Time(Action<int> f)
        {
            var t0 = Stopwatch.GetTimestamp();
            for (var i = 0; i < n; i++)
                f(i);
            return Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000 / n;
        }
        var sink = 0f;
        var ray = Time(i => sink += q.Raycast(pts[i], dirs[i], World.Range, QueryFilter.All));
        var rayHit = Time(i => sink += q.Raycast(pts[i], dirs[i], World.Range, QueryFilter.All, out var h) ? h.Normal.Y : 0);
        var body = Time(i => sink += q.OverlapBody(pts[i], World.PlayerHeight, QueryFilter.StaticOnly) ? 1 : 0);
        var point = Time(i => sink += q.PointInside(pts[i], 0.1f, QueryFilter.All) ? 1 : 0);
        var capsule = Time(i => sink += q.OverlapCapsule(SweptMover.CapsuleAt(pts[i], Stance.Stand, Vector3.UnitY), QueryFilter.All) ? 1 : 0);
        var cast = Time(i => sink += q.CapsuleCast(SweptMover.CapsuleAt(pts[i], Stance.Stand, Vector3.UnitY), dirs[i], 0.3f, QueryFilter.All, out var h) ? h.Distance : 0);
        var sphere = Time(i => sink += q.SphereCast(pts[i], 0.1f, dirs[i], 10f, QueryFilter.All, out var h) ? h.Distance : 0);
        var perQuery = $"{big.Name} ({big.Solid.Count} boxes), per query: ray {ray:0.00} us, ray with hit details {rayHit:0.00} us, body check {body:0.000} us, point {point:0.000} us, "
            + $"capsule overlap {capsule:0.00} us, capsule cast 0.3 m {cast:0.00} us, sphere cast 10 m {sphere:0.00} us{(sink == 12345 ? "!" : "")}";
        Line("costs: " + string.Join("; ", results));
        Line("costs: " + perQuery);
        PhysicsSummary.Add("costs: " + string.Join("; ", results) + "; " + perQuery);
    }

    /// <summary>Short bot matches with the swept mover: nobody stuck or fallen through, the tick budget holds.</summary>
    private static void SweptBotMatch()
    {
        var lines = new List<string>();
        var previous = World.PhysicsMode;
        World.PhysicsMode = PhysicsMode.Swept;
        try
        {
            foreach (var map in new[] { Maps.Find("Courtyard"), Maps.Find(PhysicsTestMap), HighGroundMaps().FirstOrDefault(m => m.HasTerrain) }) // a former High Ground terrain map, now in Legacy
            {
                if (map == null)
                    continue;
                using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                    bots: Math.Min(map.MaxPlayers, 12) - 1, botSkill: BotDifficulty.Normal, mapId: map.Id);
                game.MapVoteEnabled = false;
                const double dt = 1.0 / World.TickRate;
                var now = 0.0;
                var cpu = new TickCpu();
                var moved = new Dictionary<byte, float>();
                var last = new Dictionary<byte, Vector3>();
                var fallen = 0;
                var nan = 0;
                for (var i = 0; i < 30 * World.TickRate; i++)
                {
                    now += dt;
                    cpu.Start();
                    var t0 = Stopwatch.GetTimestamp();
                    game.Update(now, dt);
                    cpu.Stop(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, count: i > 60);
                    foreach (var p in game.Players.Values)
                    {
                        if (!p.Alive)
                            continue;
                        if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                            nan++;
                        else if (p.Position.Y < (map.TerrainCollision?.HeightAt(p.Position.X, p.Position.Z) ?? 0) - 0.5f)
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
                lines.Add($"{map.Name}: {game.Players.Count} players, kills {kills}, avg travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, stuck {stuck}, fallen {fallen}, worst tick {cpu.WorstText}");
                if (nan > 0 || fallen > 0)
                    Fail($"swept bot match on {map.Name}: {nan} NaN and {fallen} below-the-ground player-ticks");
                if (stuck > 2)
                    Fail($"swept bot match on {map.Name}: {stuck} bots barely moved");
                if (cpu.WorstMs > MaxTickWorkMs)
                    Fail($"swept bot match on {map.Name}: a tick did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
            }
        }
        finally
        {
            World.PhysicsMode = previous;
        }
        Line("swept bot matches (30 s): " + string.Join("; ", lines));
        PhysicsSummary.Add("swept bots: " + string.Join("; ", lines));
    }
}
