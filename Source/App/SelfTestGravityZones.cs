using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Gravity zones" self-test: several gravity regions on one map (GravityZones.cs, NavZones.cs). On a small lab map with
/// a low-gravity box, a quarter pipe that turns gravity round in three 30-degree steps onto a wall, and a rock with its own
/// point field: the field is sampled where expected and fingerprinted, bad files are refused with a clear message, the look
/// is carried across a boundary without turning, the camera's up eases round, a scripted walker goes up the pipe onto the
/// wall, a grenade thrown on the wall arcs down onto it, a long jump crosses a gap in low gravity, replays are exact, the
/// nav graph builds the same twice with nodes on the wall and the rock and a route from the floor to both, and 16 movers
/// cost nothing in allocations. Short, seeded scenarios only.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> ZoneNotes = new();

    // The pipe: centre (PipeX, PipeY), radius PipeR, from the floor (angle 0) round to the wall face at x = WallX (angle 90).
    private const float PipeX = 10f, PipeY = 8f, PipeR = 8f, WallX = 18f;

    private static string ZoneLabJson()
    {
        var ops = new List<string>
        {
            "{ \"op\": \"Ground\", \"color\": [0.4, 0.4, 0.42] }",
            // Low gravity: two 3 m platforms with a 12 m gap between them (a sprint jump at 6 m/s2 goes about three times as far).
            "{ \"op\": \"Block\", \"x0\": -38, \"y0\": 0, \"z0\": -6, \"x1\": -28, \"y1\": 3, \"z1\": 6, \"color\": \"#777780\" }",
            "{ \"op\": \"Block\", \"x0\": -16, \"y0\": 0, \"z0\": -6, \"x1\": 4, \"y1\": 3, \"z1\": 6, \"color\": \"#777780\" }",
            // The wall, and a filler under the pipe so nothing stands under its slabs.
            $"{{ \"op\": \"Block\", \"x0\": {WallX}, \"y0\": 0, \"z0\": -24, \"x1\": {WallX + 2}, \"y1\": 24, \"z1\": 24, \"color\": \"#8a8a94\" }}",
            $"{{ \"op\": \"Block\", \"x0\": {PipeX}, \"y0\": 0, \"z0\": -6, \"x1\": {WallX}, \"y1\": {PipeY - PipeR + 0.3f}, \"z1\": 6, \"color\": \"#55555c\" }}",
            // The rock: a planet of its own, 12 m across, floating with its top at y = 18, reached by a ramp up a pillar and a
            // pier (in a plain-gravity zone of its own, listed before the rock's) ending just short of the rock's top.
            "{ \"op\": \"Planet\", \"radius\": 6, \"x\": 0, \"y\": 12, \"z\": -14, \"color\": [0.5, 0.45, 0.4], \"detail\": 4 }",
            "{ \"op\": \"Ramp\", \"x\": -38, \"z\": -20, \"width\": 4, \"height\": 18, \"alongX\": true, \"dir\": 1, \"color\": \"#6a6a72\" }",
            "{ \"op\": \"Block\", \"x0\": -2, \"y0\": 0, \"z0\": -22, \"x1\": 2, \"y1\": 18.2, \"z1\": -18, \"color\": \"#6a6a72\" }",
            "{ \"op\": \"Block\", \"x0\": -1.5, \"y0\": 17.7, \"z0\": -18, \"x1\": 1.5, \"y1\": 18.2, \"z1\": -14.5, \"color\": \"#9a9aa4\" }",
            "{ \"op\": \"Spawn\", \"x\": 4, \"z\": 0 }", "{ \"op\": \"Spawn\", \"x\": -4, \"z\": 4 }", "{ \"op\": \"Spawn\", \"x\": -30, \"z\": 0, \"y\": 3 }", "{ \"op\": \"Spawn\", \"x\": 2, \"z\": 8 }",
        };
        // The quarter pipe: nine solid slabs 10 degrees apart, each tangent to the circle, rolled to its angle.
        for (var i = 0; i < 9; i++)
        {
            var deg = 5 + 10 * i;
            var (sn, cs) = MathF.SinCos(deg * MathF.PI / 180f);
            var px = PipeX + PipeR * sn;
            var py = PipeY - PipeR * cs;
            const float thick = 0.5f;
            var chord = 2 * PipeR * MathF.Sin(5 * MathF.PI / 180f) + 0.06f;
            ops.Add($"{{ \"op\": \"Mesh\", \"mesh\": \"box\", \"solid\": true, \"x\": {px + thick * sn:0.###}, \"y\": {py - thick * cs:0.###}, \"z\": 0, \"width\": {chord:0.###}, \"height\": {thick}, \"depth\": 10, \"roll\": {deg}, \"color\": \"#9a9aa4\" }}");
        }
        ops.Add("{ \"op\": \"AutoWaypoints\", \"spacing\": 8 }");
        float xa = PipeX + PipeR * MathF.Sin(15 * MathF.PI / 180f), xb = PipeX + PipeR * MathF.Sin(45 * MathF.PI / 180f), xc = PipeX + PipeR * MathF.Sin(75 * MathF.PI / 180f);
        var zones = $$"""
            { "type": "zones", "default": { "type": "uniform" }, "zones": [
              { "shape": "box", "name": "wall", "min": [9, {{PipeY + 0.3f}}, -24], "max": [{{WallX + 0.5f}}, 24, 24], "field": { "type": "uniform", "direction": [1, 0, 0] } },
              { "shape": "box", "name": "low-g", "min": [-40, -1, -12], "max": [10, 30, 12], "field": { "type": "uniform", "strength": 6 } },
              { "shape": "box", "name": "pipe 30", "min": [{{xa:0.###}}, -1, -5], "max": [{{xb:0.###}}, 24, 5], "field": { "type": "uniform", "direction": [0.5, -0.8660254, 0] } },
              { "shape": "box", "name": "pipe 60", "min": [{{xb:0.###}}, -1, -5], "max": [{{xc:0.###}}, 24, 5], "field": { "type": "uniform", "direction": [0.8660254, -0.5, 0] } },
              { "shape": "box", "name": "wall foot", "min": [{{xc:0.###}}, -1, -5], "max": [{{WallX + 0.5f}}, 24, 5], "field": { "type": "uniform", "direction": [1, 0, 0] } },
              { "shape": "box", "name": "pier", "min": [-12, 14, -23], "max": [2.5, 21, -14.5], "field": { "type": "uniform" } },
              { "shape": "sphere", "name": "rock", "center": [0, 12, -14], "radius": 9.5, "field": { "type": "point", "center": [0, 12, -14], "strength": 18 } }
            ] }
            """;
        return "{ \"name\": \"Gravity lab\", \"halfX\": 40, \"halfZ\": 24, \"gravity\": " + zones + ", \"ops\": [\n" + string.Join(",\n", ops) + "\n] }";
    }

    private static void ZoneLine(string s)
    {
        Line(s);
        ZoneNotes.Add(s);
    }

    private static void CheckGravityZones()
    {
        ZoneNotes.Clear();
        var before = World.Map;
        try
        {
            ZoneFileChecks();
            var lab = MapFile.Load(ZoneLabJson(), "selftest/gravity-lab.json", "");
            lab.Validate();
            World.SetMap(lab);
            ZoneFieldChecks(lab);
            ZoneFrameChecks();
            ZoneWalkerChecks();
            ZoneGrenadeChecks();
            ZoneLowGravityChecks();
            ZoneReplayChecks();
            ZoneNavChecks(lab);
            ZoneCostChecks();
        }
        finally
        {
            World.SetMap(before);
            if (ZoneNotes.Count > 0)
                Line("::warning title=Gravity zones (results)::" + string.Join("%0A", ZoneNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    /// <summary>Bad gravity fields are refused with a message naming the problem; the fingerprint follows the zones.</summary>
    private static void ZoneFileChecks()
    {
        string? Error(string gravity)
        {
            try
            {
                MapFile.Load("{ \"name\": \"Bad\", \"halfX\": 20, \"halfZ\": 20, \"gravity\": " + gravity + ", \"ops\": [ { \"op\": \"Ground\", \"color\": [0.4, 0.4, 0.4] } ] }", "bad.json", "");
                return null;
            }
            catch (MapFileException ex)
            {
                return ex.Message;
            }
        }
        var cases = new (string Json, string Expect)[]
        {
            ("{ \"type\": \"zones\" }", "\"zones\" is a list"),
            ("{ \"type\": \"zones\", \"zones\": [] }", "at least one zone"),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"cube\" } ] }", "\"shape\" is \"box\" or \"sphere\""),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"box\", \"min\": [0, 0, 0], \"max\": [1, 1, 1] } ] }", "needs a \"field\""),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"box\", \"min\": [0, 0, 0], \"max\": [1, 0.2, 1], \"field\": { \"type\": \"uniform\" } } ] }", "at least 0.5 m beyond"),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"sphere\", \"center\": [0, 0, 0], \"radius\": 0.5, \"field\": { \"type\": \"uniform\" } } ] }", "\"radius\" is 1 to 400"),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"sphere\", \"center\": [0, 0, 0], \"radius\": 5, \"field\": { \"type\": \"zones\" } } ] }", "can't hold zones"),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"sphere\", \"center\": [0, 0, 0], \"radius\": 5, \"field\": { \"type\": \"uniform\", \"strength\": 0 } } ] }", "\"strength\" is 1 to 60"),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"sphere\", \"center\": [0, 0, 0], \"radius\": 5, \"field\": { \"type\": \"uniform\", \"direction\": [0, 0, 0] } } ] }", "\"direction\" can't be [0, 0, 0]"),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"sphere\", \"center\": [0, 0, 0], \"radius\": 5, \"field\": { \"type\": \"uniform\", \"up\": 1 } } ] }", "unknown key \"up\""),
            ("{ \"type\": \"zones\", \"zones\": [ { \"shape\": \"box\", \"min\": [0, 0, 0], \"max\": [1, 1, 1], \"radius\": 2, \"field\": { \"type\": \"uniform\" } } ] }", "unknown key \"radius\""),
            ("{ \"type\": \"uniform\", \"direction\": [1, 0, 0] }", "sideways gravity goes in a zone"),
            ("{ \"type\": \"zones\", \"default\": { \"type\": \"uniform\", \"direction\": [0, 1, 0] }, \"zones\": [ { \"shape\": \"sphere\", \"center\": [0, 0, 0], \"radius\": 5, \"field\": { \"type\": \"uniform\" } } ] }", "sideways gravity goes in a zone"),
        };
        var bad = 0;
        foreach (var (json, expect) in cases)
        {
            var msg = Error(json);
            if (msg == null || !msg.Contains(expect))
            {
                bad++;
                Fail($"gravity zones: {json} should be refused with '{expect}', got: {msg ?? "accepted"}");
            }
        }
        ZoneLine($"map files: {cases.Length - bad} of {cases.Length} bad gravity fields refused with the expected message");

        // Good files: the fingerprint changes with a zone's bounds, its field and the default, and a plain uniform field is the old null.
        Map Good(string gravity) => MapFile.Load("{ \"name\": \"Good\", \"halfX\": 20, \"halfZ\": 20, \"gravity\": " + gravity + ", \"ops\": [ { \"op\": \"Ground\", \"color\": [0.4, 0.4, 0.4] }, { \"op\": \"Spawn\", \"x\": 0, \"z\": 0 } ] }", "good.json", "");
        const string zone = "{ \"shape\": \"sphere\", \"center\": [0, 2, 0], \"radius\": 5, \"field\": { \"type\": \"point\", \"center\": [0, 2, 0], \"strength\": 12 } }";
        var a = Good("{ \"type\": \"zones\", \"zones\": [ " + zone + " ] }");
        var b = Good("{ \"type\": \"zones\", \"zones\": [ " + zone.Replace("\"radius\": 5", "\"radius\": 6") + " ] }");
        var c = Good("{ \"type\": \"zones\", \"zones\": [ " + zone.Replace("\"strength\": 12", "\"strength\": 13") + " ] }");
        var d = Good("{ \"type\": \"zones\", \"default\": { \"type\": \"uniform\", \"strength\": 9 }, \"zones\": [ " + zone + " ] }");
        var e = Good("{ \"type\": \"uniform\" }");
        var f = Good("{ \"type\": \"uniform\", \"strength\": 6 }");
        var g = Good("{ \"type\": \"zones\", \"zones\": [ " + zone + " ] }");
        var hashes = new[] { a.ContentHash, b.ContentHash, c.ContentHash, d.ContentHash, e.ContentHash, f.ContentHash };
        var distinct = hashes.Distinct().Count() == hashes.Length;
        var stable = a.ContentHash == g.ContentHash;
        var kinds = a.Gravity is ZonedGravity { Zones.Length: 1 } && e.Gravity == null && f.Gravity is UniformGravity { Strength: 6f, StraightDown: true } && d.GravityZones?.Default is UniformGravity { Strength: 9f };
        // Written back and read again: the same field.
        var again = MapFile.Load(MapFile.ToJson(d), "again.json", "");
        var roundTrip = again.ContentHash == d.ContentHash && again.GravityZones?.Zones[0].Field is PointGravity { Strength: 12f };
        ZoneLine($"fingerprints: zones, bounds, field and default each change it {distinct}, the same file twice {stable}, kinds read {kinds}, written back and reloaded {roundTrip}");
        if (!distinct || !stable || !kinds || !roundTrip)
            Fail($"gravity zones: fingerprints or field kinds are wrong (distinct {distinct}, stable {stable}, kinds {kinds}, round trip {roundTrip})");
    }

    /// <summary>The field at a position is the first zone's that holds it, else the default; the engine sees it; no allocation.</summary>
    private static void ZoneFieldChecks(Map lab)
    {
        var z = lab.GravityZones!;
        var ok = GravityFrames.Zoned == z && GravityFrames.Curved && GravityFrames.Point == null && World.Physics.Gravity == z && lab.SolidMeshes.Count == 10 /* the rock and nine slabs */;
        var floor = new Vector3(11, 0, 0);
        var lowG = new Vector3(-30, 3, 0);
        var wall = new Vector3(WallX - 0.1f, 14, 0);
        var rock = new Vector3(0, 12 + 6.5f, -14);
        var side = new Vector3(5, 12, -14);
        ok &= z.ZoneAt(floor) == -1 && GravityFrames.GravityAt(floor) == new Vector3(0, -World.Gravity, 0) && GravityFrames.PlainUpAt(floor);
        ok &= z.ZoneAt(lowG) == 1 && GravityFrames.GravityAt(lowG) == new Vector3(0, -6, 0) && GravityFrames.PlainUpAt(lowG) && GravityFrames.HeightOf(lowG) == 3f;
        ok &= z.ZoneAt(wall) == 0 && GravityFrames.GravityAt(wall) == new Vector3(World.Gravity, 0, 0) && GravityFrames.UpAt(wall) == -Vector3.UnitX && !GravityFrames.PlainUpAt(wall);
        ok &= z.ZoneAt(rock) == 6 && Vector3.Distance(GravityFrames.UpAt(rock), Vector3.UnitY) < 1e-5f && MathF.Abs(GravityFrames.HeightOf(rock) - 6.5f) < 1e-4f;
        ok &= z.ZoneAt(side) == 6 && Vector3.Distance(GravityFrames.UpAt(side), Vector3.UnitX) < 1e-5f;
        ok &= !GravityFrames.PlainUpAlong(floor, wall) && GravityFrames.PlainUpAlong(floor, new Vector3(-30, 0, 0)) && z.ZoneAt(new Vector3(2, 0, 0)) == 1;
        // The frame on the wall: up -X, north -Z (the axis laid flat), right +Y (right-handed).
        GravityFrames.Frame(wall, out var r, out var u, out var n);
        ok &= u == -Vector3.UnitX && n == -Vector3.UnitZ && r == Vector3.UnitY;
        var sum = 0f;
        // The least of a few identical rounds (SelfTestCpuRatio.cs): a real per-call allocation shows in every round, a one-off runtime allocation in one at most.
        var alloc = SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 10000; i++)
                sum += GravityFrames.GravityAt(new Vector3(i % 40 - 20, i % 7, i % 11 - 5)).Y;
        });
        ZoneLine($"field: {z.Zones.Length} zones, floor/low-g/wall/rock/side sampled right {ok}, 10000 lookups allocated {alloc} bytes (sum {sum:0})");
        if (!ok || alloc != 0)
            Fail($"gravity zones: the field isn't sampled as expected (ok {ok}, {alloc} bytes allocated)");
    }

    /// <summary>Across a boundary the look keeps its world direction; the camera's up eases round, never jumping.</summary>
    private static void ZoneFrameChecks()
    {
        var a = new Vector3(PipeX + PipeR * MathF.Sin(10 * MathF.PI / 180f), 1, 0); // in the default field, before the pipe
        var b = new Vector3(PipeX + PipeR * MathF.Sin(20 * MathF.PI / 180f), 2, 0); // in the first pipe zone (up at 30 degrees)
        var yaw = 0.3f;
        var pitch = -0.2f;
        var look0 = GravityFrames.Forward(a, yaw, pitch);
        GravityFrames.Transport(ref yaw, ref pitch, a, b);
        var look1 = GravityFrames.Forward(b, yaw, pitch);
        var turn = MathF.Acos(Math.Clamp(Vector3.Dot(look0, look1), -1, 1)) * 180 / MathF.PI;
        var frameTurn = MathF.Acos(Math.Clamp(Vector3.Dot(GravityFrames.UpAt(a), GravityFrames.UpAt(b)), -1, 1)) * 180 / MathF.PI;
        // The camera: stepping from a to b, its up turns smoothly over a few frames and settles on the new up.
        var cam = Vector3.Zero;
        GravityFrames.SmoothUp(ref cam, a, 1f / 60);
        var startUp = cam;
        var biggest = 0f;
        var settled = -1;
        for (var f = 0; f < 120; f++)
        {
            var prev = cam;
            GravityFrames.SmoothUp(ref cam, b, 1f / 60);
            biggest = MathF.Max(biggest, MathF.Acos(Math.Clamp(Vector3.Dot(prev, cam), -1, 1)) * 180 / MathF.PI);
            if (settled < 0 && Vector3.Distance(cam, GravityFrames.UpAt(b)) < 1e-3f)
                settled = f;
        }
        var okay = turn < 0.01f && frameTurn > 29f && startUp == Vector3.UnitY && settled is >= 5 and <= 60 && biggest < 8f && MathF.Abs(cam.Length() - 1) < 1e-4f;
        ZoneLine($"frames: across the pipe's edge the frame turns {frameTurn:0.0} deg, the look {turn:0.000} deg; the camera's up settles in {settled} frames ({settled / 60f:0.00} s), at most {biggest:0.0} deg per frame");
        if (!okay)
            Fail($"gravity zones: the look turned {turn:0.00} deg across a boundary, or the camera's up didn't ease (settled {settled}, biggest step {biggest:0.0} deg)");
    }

    /// <summary>A scripted walker goes along the floor, up the quarter pipe and onto the wall, where it stands with up = -X.</summary>
    private static void ZoneWalkerChecks()
    {
        var s = Settle(new Vector3(4, 0.2f, 0));
        var lowestY = float.MaxValue;
        var airTicks = 0;
        var biggestStep = 0f;
        var ticks = 0;
        var onWall = -1;
        var yaw = GravityFrames.YawOf(s.Position, Vector3.UnitX);
        var pitch = 0f;
        for (; ticks < 900; ticks++)
        {
            var prev = s.Position;
            GravityFrames.Transport(ref yaw, ref pitch, prev, s.Position);
            // Steer along the pipe: round its centre, then up the wall.
            var p = s.Position;
            var want = p.X < PipeX ? Vector3.UnitX
                : GravityFrames.UpAt(p) == -Vector3.UnitX ? Vector3.UnitY
                : Vector3.Normalize(new Vector3(PipeY - p.Y, p.X - PipeX, 0)); // the tangent of the circle round (PipeX, PipeY)
            yaw = GravityFrames.YawOf(p, want);
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward, Yaw = yaw, Pitch = 0, SpeedPct = 100 });
            biggestStep = MathF.Max(biggestStep, Vector3.Distance(prev, s.Position));
            lowestY = MathF.Min(lowestY, s.Position.Y);
            if (!s.Grounded)
                airTicks++;
            if (onWall < 0 && GravityFrames.UpAt(s.Position) == -Vector3.UnitX && s.Grounded && s.Position.Y > 12f)
                onWall = ticks;
            if (onWall >= 0 && s.Position.Y > 18f)
                break;
        }
        var end = s.Position;
        var ok = onWall >= 0 && end.Y > 18f && MathF.Abs(end.X - WallX) < 0.1f && s.Grounded && lowestY > -0.1f && airTicks < 60 && biggestStep < 0.6f;
        ZoneLine($"walker: floor to the wall in {ticks * World.TickDt:0.0} s (on the wall after {onWall * World.TickDt:0.0} s), ended at ({end.X:0.00}, {end.Y:0.0}, {end.Z:0.0}) grounded {s.Grounded}, "
              + $"{airTicks} ticks in the air, biggest tick move {biggestStep * 100:0} cm, lowest y {lowestY:0.00}");
        if (!ok)
            Fail($"gravity zones: the walker didn't get up the pipe onto the wall (on the wall at tick {onWall}, ended {end}, grounded {s.Grounded}, {airTicks} air ticks, step {biggestStep:0.00} m)");
    }

    /// <summary>A grenade thrown along the wall (gravity +X there) arcs out from it and comes to rest on it.</summary>
    private static void ZoneGrenadeChecks()
    {
        var feet = Settle(new Vector3(WallX - 0.3f, 14, -20)).Position;
        var up = GravityFrames.UpAt(feet);
        var eye = GravityFrames.Above(feet, World.EyeHeight);
        var dir = GravityFrames.Forward(feet, GravityFrames.YawOf(feet, Vector3.UnitZ), 0.3f);
        var frag = new Projectile { Kind = ThrowableKind.Frag, Pos = Throwables.LaunchPoint(eye, dir), Vel = Throwables.LaunchVelocity(Throwables.Get(ThrowableKind.Frag), dir, Vector3.Zero) };
        var highest = 0f;
        var firstTouch = -1;
        var leftZone = false;
        for (var t = 0; t < 5 * World.TickRate; t++)
        {
            if (Throwables.Step(frag, World.TickDt) && firstTouch < 0)
                firstTouch = t;
            highest = MathF.Max(highest, WallX - frag.Pos.X);
            if (GravityFrames.UpAt(frag.Pos) != -Vector3.UnitX)
                leftZone = true;
        }
        var off = WallX - frag.Pos.X;
        var along = frag.Pos.Z - feet.Z;
        var ok = up == -Vector3.UnitX && firstTouch > 0 && highest > 1.5f && along > 3f && off is >= 0f and < 0.3f && frag.Vel.Length() < 0.5f && !leftZone;
        ZoneLine($"grenade on the wall: up to {highest:0.0} m out from the wall, first touch after {firstTouch * World.TickDt:0.00} s, at rest {along:0.0} m along it, {off * 100:0.0} cm off it, speed {frag.Vel.Length():0.00} m/s, stayed in the zone {!leftZone}");
        if (!ok)
            Fail($"gravity zones: a grenade on the wall didn't arc onto it (highest {highest:0.0}, touch {firstTouch}, along {along:0.0}, off {off:0.00}, speed {frag.Vel.Length():0.00}, left zone {leftZone})");
    }

    /// <summary>A sprinting jump crosses the 6 m gap in low gravity, and a jump in plain gravity falls short of it.</summary>
    private static void ZoneLowGravityChecks()
    {
        float JumpRun(Vector3 feet, float yaw)
        {
            var s = Settle(feet + new Vector3(0, 0.1f, 0));
            var start = s.Position;
            for (var i = 0; i < 12; i++)
                World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = yaw, SpeedPct = 100 }); // a run-up
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | Buttons.Jump, Yaw = yaw, SpeedPct = 100 });
            var air = 0;
            while (!s.Grounded && air++ < 400)
                World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = yaw, SpeedPct = 100 });
            return Vector3.Distance(new Vector3(s.Position.X, 0, s.Position.Z), new Vector3(start.X, 0, start.Z));
        }
        var east = GravityFrames.YawOf(new Vector3(-30f, 3, 0), Vector3.UnitX);
        var sLow = Settle(new Vector3(-30f, 3.1f, 0));
        var lowRun = JumpRun(new Vector3(-30f, 3f, 0), east);
        // The same jump from a 3 m platform in plain gravity: a copy of the run on the floor's level is enough to compare air time.
        var sNormal = Settle(new Vector3(4, 0.1f, 0));
        var normalRun = JumpRun(new Vector3(4, 0, 0), GravityFrames.YawOf(new Vector3(4, 0, 0), Vector3.UnitX));
        var landed = Settle(new Vector3(-30f, 3.1f, 0));
        for (var i = 0; i < 12; i++)
            World.Simulate(ref landed, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = east, SpeedPct = 100 });
        World.Simulate(ref landed, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | Buttons.Jump, Yaw = east, SpeedPct = 100 });
        var t = 0;
        while (!landed.Grounded && t++ < 400)
            World.Simulate(ref landed, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = east, SpeedPct = 100 });
        var acrossGap = landed.Grounded && landed.Position.X is > -16f and < 4f && landed.Position.Y > 2.9f;
        var ok = sLow.Grounded && sNormal.Grounded && acrossGap && lowRun > normalRun * 2f && normalRun < 12f;
        ZoneLine($"low gravity: a sprint jump covers {lowRun:0.0} m at 6 m/s2 ({normalRun:0.0} m at {World.Gravity} m/s2), lands on the far platform across the 12 m gap {acrossGap} at ({landed.Position.X:0.0}, {landed.Position.Y:0.0})");
        if (!ok)
            Fail($"gravity zones: the low-gravity jump is wrong (low {lowRun:0.0} m, normal {normalRun:0.0} m, across {acrossGap}, landed at {landed.Position})");
    }

    /// <summary>The same commands from the same state give the same bits, through zone boundaries (prediction replays).</summary>
    private static void ZoneReplayChecks()
    {
        var rng = new Random(4321);
        var cmds = new InputCmd[360];
        var cy = 0f;
        for (var i = 0; i < cmds.Length; i++)
        {
            cy = World.WrapAngle(cy + ((float)rng.NextDouble() - 0.5f) * 0.3f);
            var b = (Buttons)(rng.Next(16)) | (rng.Next(6) == 0 ? Buttons.Jump : 0) | (rng.Next(3) == 0 ? Buttons.Sprint : 0);
            cmds[i] = new InputCmd { Seq = (uint)i + 1, Buttons = b, Yaw = cy, Pitch = 0.1f, SpeedPct = 100 };
        }
        var r0 = Settle(new Vector3(PipeX + 1, 0.3f, 0));
        var a = r0;
        var mid = r0;
        var zonesSeen = 0;
        var last = -2;
        for (var i = 0; i < cmds.Length; i++)
        {
            World.Simulate(ref a, cmds[i]);
            var zi = GravityFrames.Zoned!.ZoneAt(a.Position);
            if (zi != last)
                zonesSeen++;
            last = zi;
            if (i == 179)
                mid = a;
        }
        var c = mid;
        for (var i = 180; i < cmds.Length; i++)
            World.Simulate(ref c, cmds[i]);
        var exact = a.Position == c.Position && a.Velocity == c.Velocity && a.Grounded == c.Grounded && a.Stance == c.Stance;
        ZoneLine($"replay: 360 random commands at the pipe's foot, replayed from tick 180: exact {exact}, {zonesSeen - 1} zone changes, ended {Vector3.Distance(r0.Position, a.Position):0.0} m away");
        if (!exact)
            Fail("gravity zones: a replay of the same commands gave a different state (prediction would jitter)");
    }

    /// <summary>The nav graph builds the same twice, has nodes on the wall and the rock, and routes from the floor to both.</summary>
    private static void ZoneNavChecks(Map lab)
    {
        var sw = Stopwatch.StartNew();
        var nav = NavGraph.Build(lab);
        var ms = sw.Elapsed.TotalMilliseconds;
        var nav2 = NavGraph.Build(lab);
        var same = nav.Nodes.Length == nav2.Nodes.Length && nav.EdgeCount == nav2.EdgeCount && nav.Nodes.SequenceEqual(nav2.Nodes) && nav.EdgeTo.SequenceEqual(nav2.EdgeTo);
        var onWall = nav.MainNodes.Count(i => nav.Nodes[i].X > WallX - 0.2f && nav.Nodes[i].X < WallX + 0.1f && nav.Nodes[i].Y > 9f);
        var onRock = nav.MainNodes.Count(i => Vector3.Distance(nav.Nodes[i], new Vector3(0, 12, -14)) < 6.6f);
        var rockBelow = nav.MainNodes.Count(i => Vector3.Distance(nav.Nodes[i], new Vector3(0, 12, -14)) < 6.6f && nav.Nodes[i].Y < 10f);
        var inPipe = nav.MainNodes.Count(i => GravityFrames.Zoned!.ZoneAt(nav.Nodes[i]) is 2 or 3);
        var lowG = nav.Nodes.Count(n => GravityFrames.Zoned!.ZoneAt(n) == 1 && n.Y > 2.9f); // (the platforms: reached by jumping, so not in the main area)
        var wrongWay = nav.Nodes.Count(n => !GravityFrames.PlainUpAt(n) && GravityFrames.Zoned!.ZoneAt(n) < 0); // (none: every non-+Y node lies in a zone)
        var floor = nav.Nearest(new Vector3(4, 0, 0), needSight: false);
        var wallTop = nav.Nearest(new Vector3(WallX - 0.1f, 20, 0), needSight: false);
        var rockBottom = nav.Nearest(new Vector3(0, 12 - 6f, -14), needSight: false);
        var path = new int[256];
        var search = new NavSearch();
        var toWall = floor >= 0 && wallTop >= 0 ? search.Find(nav, floor, wallTop, path, maxExpand: 20000) : 0;
        var wallEnd = toWall > 0 ? nav.Nodes[path[toWall - 1]] : Vector3.Zero;
        var toRock = floor >= 0 && rockBottom >= 0 ? search.Find(nav, floor, rockBottom, path, maxExpand: 20000) : 0;
        var rockEnd = toRock > 0 ? nav.Nodes[path[toRock - 1]] : Vector3.Zero;
        var wallReached = toWall > 1 && wallEnd.Y > 15f && MathF.Abs(wallEnd.X - WallX) < 0.3f;
        var rockReached = toRock > 1 && Vector3.Distance(rockEnd, new Vector3(0, 12, -14)) < 6.6f && rockEnd.Y < 9f;
        ZoneLine($"nav: {nav.Nodes.Length} nodes, {nav.EdgeCount} edges, {nav.MainNodes.Length} in the main area, built in {ms:0} ms, the same twice {same}; "
              + $"on the wall {onWall}, in the pipe {inPipe}, on the rock {onRock} ({rockBelow} on its underside), low-g platforms {lowG}, {wrongWay} sideways nodes outside zones; "
              + $"floor to the wall's top {toWall} nodes ({wallReached}), floor to the rock's underside {toRock} nodes ({rockReached})");
        if (!same || onWall < 4 || inPipe < 2 || onRock < 20 || rockBelow < 5 || lowG < 4 || wrongWay > 0 || !wallReached || !rockReached || nav.MainNodes.Length < nav.Nodes.Length * 0.7f) // (the low-g platforms are reached by jumping only)
            Fail($"gravity zones: the nav graph is wrong (same {same}, wall {onWall}, pipe {inPipe}, rock {onRock}/{rockBelow}, low-g {lowG}, stray {wrongWay}, to wall {toWall} {wallReached}, to rock {toRock} {rockReached}, main {nav.MainNodes.Length}/{nav.Nodes.Length})");
    }

    /// <summary>16 players moving about the zones: no allocations, cheap.</summary>
    private static void ZoneCostChecks()
    {
        var crowd = new MoveState[16];
        var starts = new[] { new Vector3(4, 0, 0), new Vector3(-32, 3, 0), new Vector3(WallX - 0.3f, 14, 0), new Vector3(0, 18.2f, -14), new Vector3(PipeX + 2, 0.5f, 0), new Vector3(-14, 3, 0), new Vector3(6.2f, 12, -14), new Vector3(8, 0, 4) };
        for (var i = 0; i < crowd.Length; i++)
            crowd[i] = Settle(starts[i % starts.Length] + new Vector3(0, 0.2f, 0), 5);
        const int costTicks = 240;
        void Ticks(int ticks, int turn)
        {
            for (var t = 0; t < ticks; t++)
                for (var i = 0; i < crowd.Length; i++)
                    World.Simulate(ref crowd[i], new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | (t % 50 == i ? Buttons.Jump : 0), Yaw = i + turn * t * 0.01f, SpeedPct = 100 });
        }
        Ticks(120, -1); // warm-up: the same moves through the same loop (scratch lists reach their size, the JIT settles)
        var t0 = Stopwatch.GetTimestamp();
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        Ticks(costTicks, 1);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        var us = Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000 / costTicks;
        // Only if this window saw bytes: the least of it and three more identical rounds (SelfTestCpuRatio.cs), so a one-off runtime allocation can't fail it.
        alloc = RecheckAllocBytes(alloc, () => Ticks(costTicks, 1));
        ZoneLine($"cost: 16 players moving about the zones: {us:0} us per tick, {alloc} bytes allocated in {costTicks} ticks");
        if (alloc != 0 || us > 4000)
            Fail($"gravity zones: movement about the zones allocated {alloc} bytes or costs {us:0} us per tick");
    }
}
