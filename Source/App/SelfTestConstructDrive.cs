using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The drivetrain families (board item dev-sandbox-drivetrain-parts-dp01; docs/sandbox/CONSTRUCTS.md "Drivetrain
    /// parts"): every part file loads and bad drivetrain blocks are refused for the right reason; the rescaled vehicles
    /// match the agreed scale chart; each family's parts change how a vehicle drives (distance, speed, grip on road and
    /// dirt, ride height, turning, climbing); the drive reaches the wheels through every transfer part type and is refused
    /// with a reason when the line is broken; the example blueprints drive; a client mirrors the rpm and the drive note;
    /// several big vehicles at once allocate nothing and stay inside the tick budget. Short scripted runs on a flat test
    /// floor (hard ground or dirt) with a ramp.
    /// </summary>
    private static void CheckConstructDrive()
    {
        DriveLibrary();
        DriveScale();
        DriveWheels();
        DriveSuspensions();
        DriveEngines();
        DriveTransmissions();
        DriveDifferentials();
        DriveTransfer();
        DriveExamples();
        DriveNet();
        DriveBudget();
    }

    /// <summary>A flat floor at y 0, hard ground (a map box) or dirt (terrain), with a ramp rising along +X from <see cref="RampX"/>.</summary>
    internal sealed class DriveWorld : IWorldQuery
    {
        public ShapeKind Kind = ShapeKind.Box;
        public float RampX = float.PositiveInfinity, RampSlope; // y = (x - RampX) * RampSlope beyond RampX
        public ITerrainCollision? Terrain => null;

        public float Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter) =>
            Raycast(origin, dir, maxDist, filter, out var hit) ? hit.Distance : maxDist;

        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            var best = maxDist;
            var found = false;
            // The flat floor (where it is left of the ramp).
            if (dir.Y < -1e-6f && origin.Y >= 0)
            {
                var t = origin.Y / -dir.Y;
                if (t <= best && origin.X + dir.X * t <= RampX)
                {
                    best = t;
                    found = true;
                    hit.Normal = Vector3.UnitY;
                }
            }
            if (float.IsFinite(RampX))
            {
                var n = Vector3.Normalize(new Vector3(-RampSlope, 1, 0));
                var d = -RampSlope * RampX / MathF.Sqrt(RampSlope * RampSlope + 1);
                var nd = Vector3.Dot(n, dir);
                var above = Vector3.Dot(n, origin) - d;
                if (nd < -1e-6f && above >= -0.01f)
                {
                    var t = -above / nd;
                    if (t <= best && origin.X + dir.X * t >= RampX)
                    {
                        best = t;
                        found = true;
                        hit.Normal = n;
                    }
                }
            }
            hit.Distance = best;
            hit.Point = origin + dir * best;
            hit.Shape = new ShapeId(Kind, 0);
            return found;
        }

        public bool SphereCast(Vector3 origin, float radius, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            return false;
        }

        public bool CapsuleCast(in Capsule capsule, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            return false;
        }

        public bool OverlapBox(in Obb box, in QueryFilter filter) => false;
        public bool OverlapCapsule(in Capsule capsule, in QueryFilter filter) => false;
        public bool OverlapBody(Vector3 feet, float height, in QueryFilter filter) => false;
        public bool PointInside(Vector3 point, float radius, in QueryFilter filter) => false;
    }

    /// <summary>A blueprint with every <paramref name="from"/> part swapped for <paramref name="to"/> (the same sockets).</summary>
    private static ConstructBuild.Blueprint Swap(ConstructBuild.Blueprint bp, params (string From, string To)[] swaps)
    {
        var b = new ConstructBuild.Blueprint { Name = bp.Name, File = bp.File, Description = bp.Description };
        foreach (var s in bp.Steps)
        {
            var part = s.Part;
            foreach (var (from, to) in swaps)
                if (part == from)
                    part = to;
            b.Steps.Add(s with { Part = part });
        }
        b.StepSettings.AddRange(bp.StepSettings);
        b.Wires.AddRange(bp.Wires);
        b.Links.AddRange(bp.Links); // (routed links: the buggy's fuel pipe)
        return b;
    }

    private static ConstructBuild.Blueprint? DriveBlueprint(string name)
    {
        var bp = ConstructBuild.FindBlueprint(name);
        if (bp == null)
            Fail($"construct drive: no {name} blueprint");
        return bp;
    }

    private static Construct? DriveSpawn(ConstructWorld w, ConstructBuild.Blueprint bp, float x, float z, float yaw, string what)
    {
        var c = ConstructBuild.BuildOnGround(w, bp, new Vector3(x, 0, z), yaw, out var why);
        if (c == null)
            Fail($"construct drive: {what} doesn't build: {why}");
        return c;
    }

    private static void DriveSteps(ConstructWorld w, IWorldQuery q, int ticks, Action<int>? each = null)
    {
        for (var t = 0; t < ticks; t++)
        {
            each?.Invoke(t);
            w.Step(q, World.TickDt);
        }
    }

    /// <summary>The driver's keys for a tick: the drive input and the seat's keys (the shift keys, input readers).</summary>
    private static void DriveKeys(Construct c, Buttons b, ushort extra = 0)
    {
        if (c.DriveSeat >= 0)
            c.Parts[c.DriveSeat].Keys = (ushort)((ushort)b | extra | CPart.Occupied);
        ConstructEdits.DriveInput(c, b, World.TickDt);
    }

    private static void Seat(Construct c)
    {
        c.Driver = 1;
        c.DriverSeat = c.DriveSeat;
    }

    /// <summary>One scripted run from standstill: settle 0.5 s, then <paramref name="ticks"/> of <paramref name="b"/>. Distance (m), speed (m/s), turn (degrees).</summary>
    private static (float Dist, float Speed, float Yaw, Construct? Car) DriveRun(ConstructBuild.Blueprint bp, DriveWorld q, int ticks, Buttons b, string what, Action<Construct, int>? each = null, float x = 0)
    {
        var w = new ConstructWorld();
        if (DriveSpawn(w, bp, x, 0, MathF.PI / 2, what) is not { } car)
            return (0, 0, 0, null);
        DriveSteps(w, q, 30);
        Seat(car);
        var p0 = car.WorldCom;
        var y0 = YawOf(car);
        DriveSteps(w, q, ticks, t =>
        {
            DriveKeys(car, b);
            each?.Invoke(car, t);
        });
        return (Vector3.Distance(p0 with { Y = 0 }, car.WorldCom with { Y = 0 }), car.Vel.Length(), World.WrapAngle(YawOf(car) - y0) * 180 / MathF.PI, car);
    }

    private static bool Differs(float a, float b, float share = 0.03f) => MathF.Abs(a - b) > share * MathF.Max(MathF.Abs(a), MathF.Abs(b)) + 0.05f;

    // ---- The library and the chart ---------------------------------------------------------------------------

    private static void DriveLibrary()
    {
        foreach (var r in ConstructParts.Refused)
            Fail($"construct drive: a shipped part file is refused: {r}");
        int Count(PartRole role) => ConstructParts.All.Count(p => p.Role == role);
        var kinds = string.Join(", ", Enum.GetValues<DriveKind>().Select(k => $"{k} {ConstructParts.All.Count(p => p.Role == PartRole.Transfer && p.Drive == k)}"));
        Line($"drivetrain parts: {Count(PartRole.Wheel)} wheels, {Count(PartRole.Suspension)} suspensions, {Count(PartRole.Engine)} engines, {Count(PartRole.Transmission)} transmissions, {Count(PartRole.Differential)} differentials, {Count(PartRole.Transfer)} transfer parts ({kinds}); {ConstructParts.All.Count} part types of {ConstructParts.MaxTypes}");
        if (Count(PartRole.Wheel) < 10 || Count(PartRole.Suspension) < 6 || Count(PartRole.Engine) < 6 || Count(PartRole.Transmission) < 7 || Count(PartRole.Differential) < 4)
            Fail("construct drive: a drivetrain family is missing members");
        foreach (var k in Enum.GetValues<DriveKind>())
            if (!ConstructParts.All.Any(p => p.Role == PartRole.Transfer && p.Drive == k))
                Fail($"construct drive: no transfer part of kind {k}");
        // Bad drivetrain blocks, each refused for the right reason.
        const string head = "\"format\": \"call-of-slop-construct-part/1\", \"name\": \"B\", \"tool\": \"wrench\", \"mass\": 5, \"health\": 50, \"boxes\": [ { \"size\": [0.4, 0.4, 0.4] } ], ";
        const string six = "\"sockets\": [ { \"name\": \"+z\", \"at\": [0, 0, 0.2], \"dir\": \"+z\" }, { \"name\": \"-z\", \"at\": [0, 0, -0.2], \"dir\": \"-z\" }, { \"name\": \"-x\", \"at\": [-0.2, 0, 0], \"dir\": \"-x\" } ]";
        const string axle = "\"sockets\": [ { \"name\": \"axle\", \"at\": [-0.2, 0, 0], \"dir\": \"-x\", \"kind\": \"axle\" } ]";
        (string Json, string Want)[] bad =
        [
            ($"{{ {head}\"role\": \"engine\", {six}, \"engine\": {{ \"torque\": 100, \"maxRpm\": 5000, \"peakRpm\": 6000 }} }}", "\"engine.peakRpm\" must be above"),
            ($"{{ {head}\"role\": \"engine\", {six}, \"engine\": {{ \"torque\": 100, \"maxRpm\": 5000, \"curve\": \"steam\" }} }}", "\"engine.curve\" is one of"),
            ($"{{ {head}\"role\": \"engine\", {six}, \"engine\": {{ \"torque\": 100, \"maxRpm\": 5000, \"fuel\": \"electric\", \"burnRate\": 1 }} }}", "only an engine that runs on fuel"),
            ($"{{ {head}\"role\": \"engine\", {six}, \"engine\": {{ \"torque\": 100, \"maxRpm\": 5000, \"fuel\": \"electric\", \"starter\": true }} }}", "needs no \"starter\""),
            ($"{{ {head}\"role\": \"transmission\", {six}, \"transmission\": {{ \"gears\": [1, 2] }} }}", "the gears go from the lowest"),
            ($"{{ {head}\"role\": \"transmission\", {six}, \"transmission\": {{ \"kind\": \"cvt\", \"gears\": [2, 1] }} }}", "a CVT has a \"range\""),
            ($"{{ {head}\"role\": \"transmission\", {six}, \"transmission\": {{ \"kind\": \"reversing\", \"gears\": [3, 2] }} }}", "one ratio"),
            ($"{{ {head}\"role\": \"differential\", {six}, \"differential\": {{ \"kind\": \"welded\" }} }}", "\"differential.kind\" is one of"),
            ($"{{ {head}\"role\": \"transfer\", {six}, \"drive\": {{ \"kind\": \"shaft\", \"ports\": [\"top\"] }} }}", "which isn't one of its sockets"),
            ($"{{ {head}\"role\": \"transfer\", {six}, \"drive\": {{ \"kind\": \"ujoint\" }} }}", "missing \"maxAngle\""),
            ($"{{ {head}\"role\": \"transfer\", {six}, \"drive\": {{ \"kind\": \"shaft\", \"ratio\": 2 }} }}", "only gearboxes, chains, belts and hubs have a \"ratio\""),
            ($"{{ {head}\"role\": \"transfer\", {six}, \"drive\": {{ \"kind\": \"transfercase\", \"ports\": [\"+z\", \"-z\"], \"front\": \"-x\" }} }}", "\"drive.front\" names one of its ports"),
            ($"{{ {head}\"role\": \"transfer\", {six} }}", "needs a \"drive\" block"),
            ($"{{ {head}\"role\": \"wheel\", {axle}, \"wheel\": {{ \"radius\": 0.3, \"track\": {{ \"length\": 3, \"rays\": 9 }} }} }}", "\"wheel.track.rays\""),
            ($"{{ {head}\"role\": \"suspension\", \"sockets\": [ {{ \"name\": \"axle\", \"at\": [0.2, 0, 0], \"dir\": \"+x\", \"kind\": \"axle\" }} ], \"suspension\": {{ \"travel\": 0.3, \"active\": true, \"presets\": [{{ \"label\": \"a\", \"frequency\": 2 }}, {{ \"label\": \"b\", \"frequency\": 3 }}] }} }}", "an active suspension takes its height"),
        ];
        var ok = 0;
        foreach (var (json, want) in bad)
            try
            {
                ConstructParts.Parse(json, "bad.json");
                Fail($"construct drive: a bad file was accepted (wanted {want})");
            }
            catch (ConstructPartException ex)
            {
                if (ex.Message.Contains(want))
                    ok++;
                else
                    Fail($"construct drive: refused for the wrong reason: \"{ex.Message}\" (wanted {want})");
            }
        Line($"refusals: {ok} of {bad.Length} bad drivetrain blocks refused with the right reason");
    }

    /// <summary>The scale chart the developer chose ("Realistic", 2026-10-29): buggy 4.0 x 1.8 m, truck 8 m, crawler 7 m, wheel radii 0.2-1.2 m, engines 0.4-1.0 m.</summary>
    private static void DriveScale()
    {
        (string Name, float Length, float Width)[] chart = [("Buggy", 4.0f, 1.8f), ("4x4 Truck", 8.0f, 0), ("Tracked Crawler", 7.0f, 0)];
        var sizes = new List<string>();
        foreach (var (name, length, width) in chart)
        {
            if (DriveBlueprint(name) is not { } bp || ConstructBuild.Build(new ConstructWorld(), bp, Vector3.Zero, Quaternion.Identity, out _) is not { } c)
                continue;
            ConstructDrive.Size(c, out var l, out var wd, out var h);
            sizes.Add($"{name} {l:0.00} x {wd:0.00} x {h:0.00} m ({c.Mass:0} kg)");
            if (MathF.Abs(l - length) > length * 0.05f || width > 0 && MathF.Abs(wd - width) > 0.05f)
                Fail($"construct drive: the {name} is {l:0.00} x {wd:0.00} m; the chart says {length:0.0} m long{(width > 0 ? $", {width:0.0} m wide" : "")}");
        }
        var radii = ConstructParts.All.Where(p => p.Role == PartRole.Wheel).Select(p => p.WheelRadius).ToArray();
        var engines = ConstructParts.All.Where(p => p.Role == PartRole.Engine && p.FuelKind != EngineFuel.None).Select(p => MathF.Max(p.BoundsMax.X - p.BoundsMin.X, p.BoundsMax.Z - p.BoundsMin.Z)).ToArray();
        Line($"scale: {string.Join("; ", sizes)}; wheel radii {radii.Min():0.00} to {radii.Max():0.00} m; engines {engines.Min():0.0} to {engines.Max():0.0} m long");
        if (radii.Min() > 0.21f || radii.Max() < 1.19f || engines.Min() > 0.41f || engines.Max() < 0.99f || engines.Max() > 1.01f)
            Fail("construct drive: the wheels or engines don't span the chart (wheels 0.2 to 1.2 m radius, engines 0.4 to 1 m)");
        // The buggy's driver: the seat and the steering wheel as before (the seat part is unchanged), the wheel radius as before.
        if (DriveBlueprint("Buggy") is { } b && ConstructBuild.Build(new ConstructWorld(), b, Vector3.Zero, Quaternion.Identity, out _) is { } car)
        {
            var seat = car.Parts[car.DriveSeat];
            var feet = seat.Pos + Rot24.Apply(seat.Rot, seat.D.SeatAt);
            Line($"buggy: wheelbase {MathF.Abs(car.Wheels[0].Centre.Z - car.Wheels[2].Centre.Z):0.00} m, track {MathF.Abs(car.Wheels[0].Centre.X - car.Wheels[1].Centre.X):0.00} m, the driver's feet {feet.Y - car.LocalMin.Y:0.00} m above its lowest point");
        }
    }

    // ---- The families ---------------------------------------------------------------------------------------

    private static readonly string[] WheelFamily = ["Small Wheel", "Large Wheel", "Off-Road Wheel", "Monster Wheel", "Solid Wheel", "Slick Wheel", "Spiked Wheel", "Omni Wheel", "Tank Road Wheel"];

    /// <summary>Each wheel on the buggy: 3 s on hard ground, 3 s on dirt, a turn; a caster can't be driven.</summary>
    private static void DriveWheels()
    {
        if (DriveBlueprint("Buggy") is not { } buggy)
            return;
        var road = new DriveWorld();
        var dirt = new DriveWorld { Kind = ShapeKind.Terrain };
        (float Road, float Dirt, float Turn) Measure(ConstructBuild.Blueprint bp, string what)
        {
            var a = DriveRun(bp, road, 180, Buttons.Forward, what);
            var b = DriveRun(bp, dirt, 180, Buttons.Forward, what);
            var t = DriveRun(bp, road, 150, Buttons.Forward, what, (c, k) => DriveKeys(c, k < 60 ? Buttons.Forward : Buttons.Forward | Buttons.Right));
            return (a.Dist, b.Dist, t.Yaw);
        }
        var std = Measure(buggy, "the buggy");
        var lines = new List<string> { $"Wheel {std.Road:0.0}/{std.Dirt:0.0} m, turn {std.Turn:0}" };
        var same = new List<string>();
        var got = new Dictionary<string, (float Road, float Dirt, float Turn)>();
        foreach (var name in WheelFamily)
        {
            var bp = Swap(buggy, ("Wheel", name));
            if (name == "Monster Wheel")
                for (var i = 0; i < bp.Steps.Count; i++)
                    if (bp.Steps[i].Part == "Plate Steel 1 m")
                        bp.Steps[i] = bp.Steps[i] with { Part = "Square Beam 0.25 m", Own = "-y" }; // (the deck is in the way of wheels 2.4 m across)
                    else if (bp.Steps[i].Part == "Fuel Tank")
                        bp.Steps[i] = bp.Steps[i] with { On = bp.Steps.FindIndex(s => s.Part == "Engine"), Socket = "+y", Own = "-y" }; // (the tank sat on the deck: on the engine instead)
            var m = Measure(bp, $"the buggy on {name}s");
            got[name] = m;
            lines.Add($"{name} {m.Road:0.0}/{m.Dirt:0.0} m, turn {m.Turn:0}");
            if (!Differs(m.Road, std.Road) && !Differs(m.Dirt, std.Dirt) && !Differs(m.Turn, std.Turn))
                same.Add(name);
        }
        var caster = ConstructBuild.Build(new ConstructWorld(), Swap(buggy, ("Wheel", "Caster Wheel")), Vector3.Zero, Quaternion.Identity, out _);
        Line($"wheels (3 s on hard ground / on dirt, degrees turned): {string.Join("; ", lines)}; casters: drives {caster?.CanDrive} ({caster?.DriveProblem})");
        if (same.Count > 0)
            Fail($"construct drive: these wheels drive just like the standard one: {string.Join(", ", same)}");
        if (got.TryGetValue("Slick Wheel", out var slick) && (slick.Road <= std.Road || slick.Dirt >= std.Dirt))
            Fail("construct drive: slicks aren't faster on hard ground and slower on dirt than road wheels");
        if (got.TryGetValue("Off-Road Wheel", out var off) && off.Dirt <= std.Dirt)
            Fail("construct drive: off-road wheels aren't faster on dirt than road wheels");
        if (caster == null || caster.CanDrive || !caster.DriveProblem.Contains("caster"))
            Fail("construct drive: a buggy on casters should be refused: \"a caster wheel can't be driven\"");
        // A punctured tyre: the wheel stays on, flat.
        var w = new ConstructWorld();
        if (DriveSpawn(w, buggy, 0, 0, MathF.PI / 2, "the buggy") is { } car)
        {
            var wheel = PartNamed(car, "Wheel");
            var r0 = car.Wheels[0].Radius;
            w.DamagePart(car, wheel, 100000, out _);
            car.Rebuild();
            var flat = car.WheelCount == 4 && car.Parts[wheel].Broken;
            var r1 = car.Wheels.Take(car.WheelCount).First(x => x.Part == wheel).Radius;
            Line($"a punctured tyre: still on {flat}, radius {r0:0.00} -> {r1:0.00} m");
            if (!flat || r1 >= r0)
                Fail("construct drive: a wrecked tyre should go flat on its rim, not fall off");
        }
    }

    private static readonly string[] SuspensionFamily = ["Stiff Suspension", "Long-Travel Suspension", "Leaf Spring", "Adjustable Suspension", "Rigid Mount", "Hydraulic Suspension"];

    /// <summary>Each suspension on the buggy: its ride height at rest, how far it rolls in a hard turn, 3 s on dirt; the hydraulic one's height follows its signal, the adjustable one's presets.</summary>
    private static void DriveSuspensions()
    {
        if (DriveBlueprint("Buggy") is not { } buggy)
            return;
        var road = new DriveWorld();
        (float Height, float Roll, float Dist) Measure(ConstructBuild.Blueprint bp, string what, Action<ConstructWorld, Construct>? setup = null)
        {
            var w = new ConstructWorld();
            if (DriveSpawn(w, bp, 0, 0, MathF.PI / 2, what) is not { } car)
                return (0, 0, 0);
            setup?.Invoke(w, car);
            DriveSteps(w, road, 90);
            var height = car.WorldCom.Y;
            Seat(car);
            var roll = 0f;
            var p0 = car.WorldCom;
            DriveSteps(w, road, 180, t =>
            {
                DriveKeys(car, t < 90 ? Buttons.Forward : Buttons.Forward | Buttons.Left);
                var right = Vector3.Transform(Vector3.UnitX, car.Rot);
                roll = MathF.Max(roll, MathF.Abs(MathF.Asin(Math.Clamp(right.Y, -1, 1))) * 180 / MathF.PI);
            });
            return (height, roll, Vector3.Distance(p0 with { Y = 0 }, car.WorldCom with { Y = 0 }));
        }
        var std = Measure(buggy, "the buggy");
        var lines = new List<string> { $"Suspension {std.Height:0.00} m, roll {std.Roll:0.0}, {std.Dist:0.0} m" };
        var same = new List<string>();
        foreach (var name in SuspensionFamily)
        {
            var m = Measure(Swap(buggy, ("Suspension", name)), $"the buggy on {name}s");
            lines.Add($"{name} {m.Height:0.00} m, roll {m.Roll:0.0}, {m.Dist:0.0} m");
            if (!Differs(m.Height, std.Height, 0.02f) && !Differs(m.Roll, std.Roll, 0.05f) && !Differs(m.Dist, std.Dist))
                same.Add(name);
        }
        // The hydraulic struts on a switch: low, then high.
        var hyd = Swap(buggy, ("Suspension", "Hydraulic Suspension"));
        var low = Measure(hyd, "the hydraulic buggy").Height;
        float Hydraulic(byte on) => Measure(hyd, "the hydraulic buggy", (w, car) =>
        {
            var sw = Fit(w, car, 0, "-x1", "Switch", "+x");
            for (var i = 0; i < car.PartCount; i++)
                if (car.DefOf(i).ActiveSuspension)
                    Wire(w, car, sw, i);
            car.Parts[sw].State = on;
        }).Height;
        low = Hydraulic(0);
        var high = Hydraulic(1);
        var adj = Swap(buggy, ("Suspension", "Adjustable Suspension"));
        float Preset(string label) => Measure(adj, "the adjustable buggy", (_, car) =>
        {
            for (var i = 0; i < car.PartCount; i++)
                if (car.DefOf(i).SuspFrequency.Length > 0)
                    car.Parts[i].Setting = (byte)Array.IndexOf(car.DefOf(i).SettingLabels, label);
        }).Height;
        var adjHigh = Preset("high");
        var adjLow = Preset("low");
        Line($"suspensions (ride height of the centre of mass, the most it rolls in a turn, 3 s): {string.Join("; ", lines)}; hydraulic low {low:0.00} / high {high:0.00} m; adjustable high {adjHigh:0.00} / low {adjLow:0.00} m");
        if (same.Count > 0)
            Fail($"construct drive: these suspensions ride just like the standard one: {string.Join(", ", same)}");
        if (high - low < 0.2f || adjHigh - adjLow < 0.1f)
            Fail("construct drive: the hydraulic suspension's signal (or the adjustable one's presets) didn't change its ride height");
    }

    /// <summary>A test mule: any engine on the back of a buggy chassis (the transmission on the differential, the engine on its front face, its tank or battery pack on top, a battery for starters).</summary>
    private static ConstructBuild.Blueprint Mule(string engine, string transmission = "Transmission")
    {
        var e = ConstructParts.Need(engine);
        var steps = new List<string>
        {
            """{ "part": "Frame Beam" }""",
            """{ "part": "Frame Beam", "on": 0, "socket": "x+0", "by": "x-0" }""",
            """{ "part": "Seat", "on": 0, "socket": "y+0", "by": "bottom" }""",
            """{ "part": "Differential", "on": 0, "socket": "end+", "by": "-z0" }""",
            """{ "part": "Suspension", "on": 3, "socket": "-x", "by": "mount" }""",
            """{ "part": "Suspension", "on": 3, "socket": "+x", "by": "mount" }""",
            """{ "part": "Frame Block", "on": 0, "socket": "end-", "by": "+z" }""",
            """{ "part": "Frame Block", "on": 1, "socket": "end-", "by": "+z" }""",
            """{ "part": "Steering Pivot", "on": 6, "socket": "-x", "by": "base" }""",
            """{ "part": "Steering Pivot", "on": 7, "socket": "+x", "by": "base" }""",
            """{ "part": "Suspension", "on": 8, "socket": "leaf", "by": "mount" }""",
            """{ "part": "Suspension", "on": 9, "socket": "leaf", "by": "mount" }""",
            """{ "part": "Wheel", "on": 4, "socket": "axle", "by": "axle" }""",
            """{ "part": "Wheel", "on": 5, "socket": "axle", "by": "axle" }""",
            """{ "part": "Wheel", "on": 10, "socket": "axle", "by": "axle" }""",
            """{ "part": "Wheel", "on": 11, "socket": "axle", "by": "axle" }""",
            $$"""{ "part": "{{transmission}}", "on": 3, "socket": "top1", "by": "-y" }""",
            $$"""{ "part": "{{engine}}", "on": 16, "socket": "-z", "by": "+z" }""",
            """{ "part": "Battery", "on": 0, "socket": "y+1", "by": "-y" }""",
        };
        if (e.FuelKind == EngineFuel.Fuel)
            steps.Add("""{ "part": "Small Fuel Tank", "on": 17, "socket": "+y", "by": "-y" }""");
        else if (e.FuelKind == EngineFuel.Electric)
            steps.Add("""{ "part": "Battery Pack", "on": 17, "socket": "+y", "by": "-y", "spin": 1 }""");
        return ConstructBuild.ParseBlueprint($$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "Mule", "steps": [ {{string.Join(", ", steps)}} ] }""", "mule.json");
    }

    private static readonly string[] EngineFamily = ["Engine", "Small Engine", "V8 Engine", "Diesel Engine", "Electric Motor", "Turbine", "Hamster Wheel"];

    /// <summary>Each engine in the mule (an automatic gearbox): speed after 0.5 s and 3 s, its top speed in 6 s, fuel or charge used; a diesel without a battery won't start.</summary>
    private static void DriveEngines()
    {
        var road = new DriveWorld();
        var lines = new List<string>();
        var got = new Dictionary<string, (float Early, float Speed, float Top, float Used)>();
        foreach (var name in EngineFamily)
        {
            var bp = Mule(name, "Automatic Gearbox"); // (an automatic: each engine runs up its own rev range)
            var early = 0f;
            var speed3 = 0f;
            var peak = 0f;
            var fuel0 = 0f;
            var charge0 = 0f;
            var run = DriveRun(bp, road, 360, Buttons.Forward, $"the {name} mule", (c, t) =>
            {
                if (t == 0)
                {
                    fuel0 = c.Fuel;
                    charge0 = c.Charge;
                }
                if (t == 30)
                    early = c.Vel.Length();
                if (t == 180)
                    speed3 = c.Vel.Length();
                peak = MathF.Max(peak, c.Vel.Length());
            });
            var car = run.Car;
            var used = car == null ? 0 : car.DefOf(car.Engine).FuelKind == EngineFuel.Electric ? charge0 - car.Charge : fuel0 - car.Fuel;
            got[name] = (early, speed3, peak, used);
            lines.Add($"{name} {early:0.0} / {speed3:0.0} m/s (top {peak:0.0}), {run.Dist:0} m, used {used:0.00} {(car != null && car.DefOf(car.Engine).FuelKind == EngineFuel.Electric ? "kJ" : "l")}, {car?.Mass:0} kg");
        }
        // A diesel needs a battery for its starter.
        var noBattery = Mule("Diesel Engine", "Automatic Gearbox");
        noBattery.Steps.RemoveAt(18); // (the battery; the tank on the engine stays step 18)
        noBattery.StepSettings.RemoveAt(18);
        var nb = DriveRun(noBattery, road, 60, Buttons.Forward, "the diesel mule without a battery");
        Line($"engines (0.5 s / 3 s from standstill, the top speed and the distance in 6 s): {string.Join("; ", lines)}; a diesel with no battery: {nb.Dist:0.0} m, it won't start {nb.Car?.StarterFailed}");
        if (!got.TryGetValue("Engine", out var std))
            return;
        foreach (var (name, m) in got)
            if (name != "Engine" && !Differs(m.Early, std.Early) && !Differs(m.Speed, std.Speed) && !Differs(m.Top, std.Top))
                Fail($"construct drive: the {name} drives just like the standard engine");
        if (got["Turbine"].Early >= std.Early || got["Turbine"].Top <= std.Top)
            Fail("construct drive: the turbine should be slow off the line (spooling) and then faster than the standard engine");
        if (got["Electric Motor"].Used <= 1 || got["Hamster Wheel"].Speed > 3 || got["Diesel Engine"].Early <= got["Small Engine"].Early)
            Fail("construct drive: the electric motor didn't use its battery (or the hamster was fast, or the diesel pulled less than the small engine)");
        if (nb.Dist > 0.3f || nb.Car is not { StarterFailed: true })
            Fail("construct drive: a diesel with no battery should refuse to start");
    }

    private static readonly string[] TransmissionFamily = ["Transmission", "5-Speed Gearbox", "Automatic Gearbox", "CVT", "Reversing Gearbox", "Low-Range Gearbox", "Sequential Gearbox"];

    /// <summary>Each transmission in the mule: 3 s and 8 s from standstill (shifting up with R at the limiter), climbing a 22 degree ramp; an automatic shifts itself, a CVT holds the engine's peak, a reversing box backs up as fast as it goes.</summary>
    private static void DriveTransmissions()
    {
        var road = new DriveWorld();
        var ramp = new DriveWorld { RampX = 6, RampSlope = MathF.Tan(15 * MathF.PI / 180) };
        var lines = new List<string>();
        var got = new Dictionary<string, (float Dist3, float Speed8, float Climb)>();
        var autoGear = 0;
        var cvtRpm = 0f;
        var shifts = new List<string>();
        foreach (var name in TransmissionFamily)
        {
            var bp = Mule("Engine", name);
            var d3 = 0f;
            var rpmSum = 0f;
            var rpmN = 0;
            var gearsSeen = 0;
            var run = DriveRun(bp, road, 480, Buttons.Forward, $"the {name} mule", (c, t) =>
            {
                if (t == 180)
                    d3 = c.WorldCom.X;
                // The driver shifts up at 95% of the limiter (R, a rising edge).
                var e = c.DefOf(c.Engine);
                DriveKeys(c, Buttons.Forward, c.Rpm > e.MaxRpm * 0.93f && t % 20 == 0 ? ConstructParts.KeyReload : (ushort)0);
                gearsSeen = Math.Max(gearsSeen, c.Gear + 1);
                if (t > 60)
                {
                    rpmSum += c.Rpm;
                    rpmN++;
                }
            });
            var climb = DriveRun(Mule("Small Engine", name), ramp, 480, Buttons.Forward, $"the small {name} mule", x: -2).Car?.WorldCom.Y ?? 0;
            var car = run.Car;
            if (car != null && car.DefOf(car.Transmission).Trans == TransKind.Automatic)
                autoGear = car.Gear;
            if (car != null && car.DefOf(car.Transmission).Trans == TransKind.Cvt)
                cvtRpm = rpmSum / Math.Max(1, rpmN);
            got[name] = (d3, run.Speed, climb);
            lines.Add($"{name} {d3:0} m / {run.Speed:0.0} m/s (gear {gearsSeen}), climbs to {climb:0.0} m");
        }
        // Reverse: a reversing box backs up as fast as it goes forwards.
        var rev = DriveRun(Mule("Engine", "Reversing Gearbox"), road, 180, Buttons.Back, "the reversing mule").Speed;
        var fwd = DriveRun(Mule("Engine", "Reversing Gearbox"), road, 180, Buttons.Forward, "the reversing mule").Speed;
        var rev3 = DriveRun(Mule("Engine", "Transmission"), road, 180, Buttons.Back, "the mule").Speed;
        // Shift times: a sequential box's power cut is shorter than a manual's.
        foreach (var name in new[] { "Transmission", "Sequential Gearbox" })
        {
            var cut = 0;
            DriveRun(Mule("Engine", name), road, 120, Buttons.Forward, $"the {name} mule", (c, t) =>
            {
                DriveKeys(c, Buttons.Forward, t == 60 ? ConstructParts.KeyReload : (ushort)0);
                if (t >= 60 && c.ShiftTimer > 0)
                    cut++;
            });
            shifts.Add($"{name} {cut / 60f:0.00} s");
        }
        var peak = ConstructParts.Need("Engine").PeakRpm;
        Line($"transmissions (3 s / 8 s with the driver shifting up, a small engine climbing a 15 degree ramp): {string.Join("; ", lines)}; the automatic ended in gear {autoGear + 1}; the CVT held {cvtRpm:0} rpm (peak {peak:0}); reversing box back {rev:0.0} / forward {fwd:0.0} m/s (3-speed back {rev3:0.0}); shift cuts {string.Join(", ", shifts)}");
        if (!got.TryGetValue("Transmission", out var std))
            return;
        foreach (var (name, m) in got)
            if (name != "Transmission" && !Differs(m.Dist3, std.Dist3) && !Differs(m.Speed8, std.Speed8) && !Differs(m.Climb, std.Climb))
                Fail($"construct drive: the {name} drives just like the 3-speed transmission");
        if (autoGear < 1 || MathF.Abs(cvtRpm - peak) > peak * 0.2f || rev < fwd * 0.8f || rev <= rev3)
            Fail("construct drive: the automatic didn't shift up (or the CVT didn't hold the peak, or the reversing box didn't back up at full speed)");
        if (got["Low-Range Gearbox"].Climb <= std.Climb)
            Fail("construct drive: the low-range box should climb further than the 3-speed");
    }

    /// <summary>The differentials with a rear wheel in the air (each one's share of the drive to the wheel that touches), and a locked one fighting the steering.</summary>
    private static void DriveDifferentials()
    {
        if (DriveBlueprint("Buggy") is not { } buggy)
            return;
        var road = new DriveWorld();
        var lines = new List<string>();
        var shares = new Dictionary<string, float>();
        foreach (var name in new[] { "Differential", "Limited-Slip Differential", "Locking Differential" })
        {
            var w = new ConstructWorld();
            if (DriveSpawn(w, Swap(buggy, ("Differential", name)), 0, 0, MathF.PI / 2, $"the buggy with a {name}") is not { } car)
                continue;
            if (name == "Locking Differential")
                car.Parts[PartNamed(car, name)].Setting = 1; // locked
            DriveSteps(w, road, 30);
            Seat(car);
            car.Throttle = 1;
            car.EngineRunning = true;
            int lifted = -1, other = -1;
            for (var k = 0; k < car.WheelCount; k++)
                if (car.Wheels[k].Driven)
                {
                    if (lifted < 0)
                        lifted = k;
                    else
                        other = k;
                }
            car.Wheels[lifted].Touching = false;
            ConstructDrive.Tick(car, true, 1, 0, 0, World.TickDt);
            shares[name] = car.Wheels[other].Share;
            var turn = DriveRun(Swap(buggy, ("Differential", name)), road, 150, Buttons.Forward, $"the buggy with a {name}", (c, k) =>
            {
                if (k == 0 && name == "Locking Differential")
                    c.Parts[PartNamed(c, name)].Setting = 1;
                DriveKeys(c, k < 60 ? Buttons.Forward : Buttons.Forward | Buttons.Right);
            }).Yaw;
            lines.Add($"{name}: {shares[name] * 100:0}% to the wheel that touches, turned {turn:0.0} deg");
            shares[name + " turn"] = turn;
        }
        Line($"differentials (a rear wheel in the air): {string.Join("; ", lines)}");
        if (shares.Count < 6 || shares["Differential"] >= 0.2f || shares["Limited-Slip Differential"] < 0.4f || shares["Locking Differential"] < 0.9f)
            Fail("construct drive: open, limited-slip and locked differentials don't share the drive as they should");
        else if (shares["Locking Differential turn"] >= shares["Differential turn"] * 0.95f) // (0.95 since the routed links: the buggy's tank sits on its deck now, and the margin was 10% to the degree)
            Fail("construct drive: a locked differential should fight the steering (turn less than an open one)");
    }

    // ---- Transfer parts ----------------------------------------------------------------------------------------

    /// <summary>A transfer rig: two beams, a seat, an engine, its tank, a transmission on the engine's back face (nothing else driven).</summary>
    private static Construct? Rig(ConstructWorld w)
    {
        var bp = ConstructBuild.ParseBlueprint("""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Rig", "steps": [
              { "part": "Frame Beam" },
              { "part": "Frame Beam", "on": 0, "socket": "x+0", "by": "x-0" },
              { "part": "Seat", "on": 0, "socket": "y+0", "by": "bottom" },
              { "part": "Engine", "on": 1, "socket": "y+1", "by": "-y" },
              { "part": "Fuel Tank", "on": 3, "socket": "+y", "by": "-y" },
              { "part": "Transmission", "on": 3, "socket": "+z", "by": "-z" }
            ] }
            """, "rig.json");
        return ConstructBuild.Build(w, bp, new Vector3(0, 5, 0), Quaternion.Identity, out _);
    }

    /// <summary>Fits <paramref name="part"/> by its socket <paramref name="by"/> onto <paramref name="socket"/> of <paramref name="on"/>, turned so its socket <paramref name="face"/> points along <paramref name="dir"/> (any turn when <paramref name="face"/> is null).</summary>
    private static int RigFit(ConstructWorld w, Construct c, int on, string socket, string part, string by, string? face = null, Vector3 dir = default)
    {
        if (on < 0)
            return -1;
        var def = ConstructParts.Need(part);
        var fi = face == null ? -1 : Array.FindIndex(def.Sockets, s => s.Name == face);
        var i = FitTurned(w, c, on, socket, part, by, rot => fi < 0 || Vector3.Dot(ConstructParts.DirVector(Rot24.ApplyDir(rot, def.Sockets[fi].Dir)), dir) > 0.9f);
        if (i < 0)
            Fail($"construct drive: a {part} doesn't fit on the {c.DefOf(on).Name}'s \"{socket}\"");
        return i;
    }

    /// <summary>A hub with a suspension and a wheel on the end of a drive line (the wheel off the side of the hub, its axle along X).</summary>
    private static int RigHub(ConstructWorld w, Construct c, int on, string socket)
    {
        if (on < 0)
            return -1;
        var def = ConstructParts.Need("Wheel Hub");
        var fx = Array.FindIndex(def.Sockets, s => s.Name == "+x");
        foreach (var dir in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
        {
            var hub = FitTurned(w, c, on, socket, "Wheel Hub", "-z", rot => Vector3.Dot(ConstructParts.DirVector(Rot24.ApplyDir(rot, def.Sockets[fx].Dir)), dir) > 0.9f);
            if (hub < 0)
                continue;
            var s = FitTurned(w, c, hub, "+x", "Suspension", "mount", _ => true);
            if (s >= 0 && FitTurned(w, c, s, "axle", "Wheel", "axle", _ => true) >= 0)
                return hub;
            Fail($"construct drive: a wheel doesn't fit on the hub at the {c.DefOf(on).Name}'s \"{socket}\"");
            return -1;
        }
        Fail($"construct drive: a Wheel Hub doesn't fit on the {c.DefOf(on).Name}'s \"{socket}\"");
        return -1;
    }

    private static int T(Construct c) => c.Transmission >= 0 ? c.Transmission : PartNamed(c, "Transmission");

    private static string RigState(Construct c)
    {
        var driven = 0;
        var ratio = 0f;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Driven)
            {
                driven++;
                ratio = c.Wheels[k].Ratio;
            }
        return c.CanDrive ? $"{driven} driven (x{ratio:0.0#})" : $"refused: {c.DriveProblem}";
    }

    /// <summary>The drive reaches a wheel through every kind of transfer part, with its ratio; broken or wrongly fitted lines are refused with a reason.</summary>
    private static void DriveTransfer()
    {
        var lines = new List<string>();
        var hubRatio = ConstructParts.Need("Wheel Hub").DriveRatio;
        void Check(string what, Func<ConstructWorld, Construct, bool> build, Func<Construct, bool> ok)
        {
            var w = new ConstructWorld();
            if (Rig(w) is not { } c)
            {
                Fail("construct drive: the transfer rig doesn't build");
                return;
            }
            var built = build(w, c);
            c.Rebuild();
            var state = RigState(c);
            lines.Add($"{what}: {state}");
            if (!built || !ok(c))
                Fail($"construct drive: {what}: {state}");
        }
        bool Ratio(Construct c, float want) => c.CanDrive && Enumerable.Range(0, c.WheelCount).Any(k => c.Wheels[k].Driven && MathF.Abs(c.Wheels[k].Ratio - want) < 0.01f);
        Check("shaft", (w, c) => RigHub(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+") >= 0, c => Ratio(c, hubRatio));
        Check("2 m shaft", (w, c) => RigHub(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 2 m", "end-"), "end+") >= 0, c => Ratio(c, hubRatio));
        Check("right-angle gearbox", (w, c) =>
        {
            var g = RigFit(w, c, T(c), "+z", "Right-Angle Gearbox", "-z", "-y", Vector3.UnitY);
            return RigHub(w, c, RigFit(w, c, g, "-y", "Drive Shaft 1 m", "end-"), "end+") >= 0;
        }, c => Ratio(c, hubRatio));
        Check("T gearbox (two hubs)", (w, c) =>
        {
            var g = RigFit(w, c, T(c), "+z", "T Gearbox", "-z", "-y", Vector3.UnitY);
            return RigHub(w, c, RigFit(w, c, g, "+z", "Drive Shaft 1 m", "end-"), "end+") >= 0 && RigHub(w, c, RigFit(w, c, g, "-y", "Drive Shaft 1 m", "end-"), "end+") >= 0;
        }, c => c.CanDrive && c.GroupCount == 2);
        Check("reduction gearbox", (w, c) => RigHub(w, c, RigFit(w, c, RigFit(w, c, T(c), "+z", "Reduction Gearbox", "-z"), "+z", "Drive Shaft 1 m", "end-"), "end+") >= 0, c => Ratio(c, hubRatio * 2));
        foreach (var chain in new[] { "Chain Drive 1:1", "Chain Drive 2:1", "Belt Drive" })
        {
            var r = ConstructParts.Need(chain).DriveRatio;
            Check(chain.ToLowerInvariant(), (w, c) => RigHub(w, c, RigFit(w, c, RigFit(w, c, T(c), "+z", chain, "in", "out", Vector3.UnitZ), "out", "Drive Shaft 1 m", "end-"), "end+") >= 0, c => Ratio(c, hubRatio * r));
        }
        Check("clutch", (w, c) => RigHub(w, c, RigFit(w, c, RigFit(w, c, T(c), "+z", "Clutch", "-z"), "+z", "Drive Shaft 1 m", "end-"), "end+") >= 0, c => c.CanDrive && c.GateCount == 1);
        Check("transfer case (rear and front)", (w, c) =>
        {
            var tc = RigFit(w, c, T(c), "+z", "Transfer Case", "in", "front", -Vector3.UnitX);
            return RigHub(w, c, RigFit(w, c, tc, "rear", "Drive Shaft 1 m", "end-"), "end+") >= 0 && RigHub(w, c, RigFit(w, c, tc, "front", "Drive Shaft 1 m", "end-"), "end+") >= 0;
        }, c => c.CanDrive && c.GroupCount == 2 && c.GateCount == 1);
        Check("U joint into a steering pivot", (w, c) =>
        {
            var u = RigFit(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+", "U Joint", "-x");
            var p = RigFit(w, c, u, "+x", "Steering Pivot", "base");
            return RigFit(w, c, RigFit(w, c, p, "leaf", "Suspension", "mount", "axle", Vector3.UnitZ), "axle", "Wheel", "axle") >= 0;
        }, c => c.CanDrive && c.GateCount == 1);
        Check("CV joint into a steering pivot", (w, c) =>
        {
            var u = RigFit(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+", "CV Joint", "-x");
            var p = RigFit(w, c, u, "+x", "Steering Pivot", "base");
            return RigFit(w, c, RigFit(w, c, p, "leaf", "Suspension", "mount", "axle", Vector3.UnitZ), "axle", "Wheel", "axle") >= 0;
        }, c => c.CanDrive);
        Check("power take-off", (w, c) =>
        {
            var pto = RigFit(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+", "Power Take-Off", "-z");
            return RigFit(w, c, pto, "+y", "Turntable", "base") >= 0;
        }, c => !c.CanDrive && Enumerable.Range(0, c.PartCount).Any(i => c.PtoDriven[i]));
        // Refused, each with its reason.
        Check("a steering pivot with no U or CV joint", (w, c) =>
        {
            var p = RigFit(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+", "Steering Pivot", "base");
            return RigFit(w, c, RigFit(w, c, p, "leaf", "Suspension", "mount", "axle", Vector3.UnitZ), "axle", "Wheel", "axle") >= 0;
        }, c => !c.CanDrive && c.DriveProblem.Contains("U joint or CV joint"));
        Check("a stub axle", (w, c) =>
        {
            var stub = RigFit(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+", "Stub Axle", "-z");
            return RigFit(w, c, RigFit(w, c, stub, "+x", "Suspension", "mount", "axle", Vector3.UnitX), "axle", "Wheel", "axle") >= 0;
        }, c => !c.CanDrive && c.DriveProblem.Contains("stub axle carries no drive"));
        Check("a shaft fitted by its side", (w, c) => RigHub(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 2 m", "bottom"), "end+") >= 0,
            c => !c.CanDrive && c.DriveProblem.Contains("fitted by its side"));
        Check("a wrecked shaft", (w, c) =>
        {
            var s = RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-");
            var ok = RigHub(w, c, s, "end+") >= 0;
            w.DamagePart(c, s, 100000, out _);
            return ok;
        }, c => !c.CanDrive && c.DriveProblem.Contains("drive shaft 1 m is wrecked"));
        Line($"transfer parts: {string.Join("; ", lines)}");
        DriveGates();
    }

    /// <summary>The runtime gates: a clutch out, 2WD and 4WD, a U joint bent past its angle; a PTO's turntable runs only with the engine.</summary>
    private static void DriveGates()
    {
        var notes = new List<string>();
        // A clutch with a switch: out, then in.
        {
            var w = new ConstructWorld();
            if (Rig(w) is not { } c)
                return;
            var cl = RigFit(w, c, T(c), "+z", "Clutch", "-z");
            RigHub(w, c, RigFit(w, c, cl, "+z", "Drive Shaft 1 m", "end-"), "end+");
            var sw = Fit(w, c, 0, "x-1", "Switch", "+x"); // (the rig's old frame beam)
            if (sw < 0 || cl < 0)
                return;
            Wire(w, c, sw, cl);
            Seat(c);
            c.EngineRunning = true;
            w.Step(new DriveWorld(), World.TickDt); // (the logic: the clutch's input)
            ConstructDrive.Tick(c, true, 1, 0, 0, World.TickDt);
            var outNote = c.Note;
            c.Parts[sw].State = 1;
            w.Step(new DriveWorld(), World.TickDt);
            w.Step(new DriveWorld(), World.TickDt);
            ConstructDrive.Tick(c, true, 1, 0, 0, World.TickDt);
            notes.Add($"clutch out: {outNote}, in: {c.Note}");
            if (outNote != DriveNote.ClutchOut || c.Note == DriveNote.ClutchOut)
                Fail("construct drive: a clutch wired to a switch didn't let the drive out and in");
        }
        // The 4x4 truck: 2WD then 4WD (its front wheels), and a U joint at full lock.
        if (DriveBlueprint("4x4 Truck") is { } truck)
        {
            foreach (var joint in new[] { "CV Joint", "U Joint" })
            {
                var w = new ConstructWorld();
                if (DriveSpawn(w, Swap(truck, ("CV Joint", joint)), 0, 0, MathF.PI / 2, $"the truck with {joint}s") is not { } c)
                    continue;
                var tc = PartNamed(c, "Transfer Case");
                DriveSteps(w, new DriveWorld(), 20);
                Seat(c);
                int Reached()
                {
                    var n = 0;
                    for (var k = 0; k < c.WheelCount; k++)
                        if (c.Wheels[k].Driven && (c.Wheels[k].GateMask & ~c.GateOpen) == 0)
                            n++;
                    return n;
                }
                c.Parts[tc].Setting = 0;
                DriveSteps(w, new DriveWorld(), 2, _ => DriveKeys(c, Buttons.Forward));
                var twoWd = Reached();
                c.Parts[tc].Setting = 1;
                DriveSteps(w, new DriveWorld(), 2, _ => DriveKeys(c, Buttons.Forward));
                var fourWd = Reached();
                // Full lock at a crawl.
                DriveSteps(w, new DriveWorld(), 90, _ => DriveKeys(c, Buttons.Forward | Buttons.Left));
                var locked = Reached();
                var lockDeg = 0f;
                for (var j = 0; j < c.JointCount; j++)
                    lockDeg = MathF.Max(lockDeg, MathF.Abs(c.Joints[j].Angle) * 180 / MathF.PI);
                notes.Add($"truck with {joint}s: 2WD {twoWd} wheels, 4WD {fourWd}, at {lockDeg:0} degrees of lock {locked} ({c.Note})");
                if (twoWd != 2 || fourWd != 4 || (joint == "U Joint" ? locked != 2 : locked != 4))
                    Fail($"construct drive: the truck's transfer case or its {joint}s don't gate the drive right");
            }
        }
        // A turntable on a power take-off turns only while the engine runs.
        {
            var w = new ConstructWorld();
            if (Rig(w) is { } c)
            {
                var pto = RigFit(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+", "Power Take-Off", "-z");
                var tt = RigFit(w, c, pto, "+y", "Turntable", "base");
                RigFit(w, c, tt, "top", "Frame Block", "-y"); // (something on it to turn)
                var lever = Fit(w, c, 0, "x-1", "Lever", "+x");
                if (tt < 0 || lever < 0 || c.JointOf(tt) < 0)
                {
                    Fail("construct drive: the PTO rig's turntable or lever didn't fit");
                    return;
                }
                Wire(w, c, lever, tt);
                c.Parts[lever].State = 1; // forward
                var q = new DriveWorld();
                DriveSteps(w, q, 60);
                var off = MathF.Abs(c.Joints[c.JointOf(tt)].Angle);
                Seat(c);
                DriveSteps(w, q, 60, _ => DriveKeys(c, Buttons.Forward));
                var on = MathF.Abs(c.Joints[c.JointOf(tt)].Angle);
                notes.Add($"PTO turntable: {off * 180 / MathF.PI:0} degrees with the engine off, {on * 180 / MathF.PI:0} with it running");
                if (off > 0.05f || on < off + 0.3f)
                    Fail("construct drive: a turntable on a power take-off should turn only while the engine runs");
            }
        }
        Line($"gates: {string.Join("; ", notes)}");
    }

    // ---- Example blueprints, the wire, the budget ---------------------------------------------------------------

    private static readonly string[] Examples = ["Buggy", "4x4 Truck", "Tracked Crawler", "Racer", "Electric Cart"];

    /// <summary>Every example blueprint drives: 4 s on dirt, then a turn (the crawler turns on the spot by skid steering).</summary>
    private static void DriveExamples()
    {
        var dirt = new DriveWorld { Kind = ShapeKind.Terrain };
        var road = new DriveWorld();
        var lines = new List<string>();
        var top = new Dictionary<string, float>();
        foreach (var name in Examples)
        {
            if (DriveBlueprint(name) is not { } bp)
                continue;
            var run = DriveRun(bp, dirt, 240, Buttons.Forward, $"the {name}");
            var fast = DriveRun(bp, road, 480, Buttons.Forward, $"the {name}", (c, t) => DriveKeys(c, Buttons.Forward, c.Rpm > c.DefOf(c.Engine).MaxRpm * 0.93f && t % 20 == 0 ? ConstructParts.KeyReload : (ushort)0));
            var turn = DriveRun(bp, dirt, 120, name == "Tracked Crawler" ? Buttons.Left : Buttons.Forward | Buttons.Left, $"the {name}");
            top[name] = fast.Speed;
            lines.Add($"{name}: {run.Dist:0.0} m in 4 s on dirt, {fast.Speed:0.0} m/s after 8 s on hard ground, turned {turn.Yaw:0} deg in 2 s{(name == "Tracked Crawler" ? " on the spot" : "")}, {(run.Car?.CanDrive == true ? "" : run.Car?.DriveProblem)}");
            if (run.Car is not { CanDrive: true } || run.Dist < 5 || MathF.Abs(turn.Yaw) < 15)
                Fail($"construct drive: the {name} doesn't drive (or turn)");
        }
        Line($"examples: {string.Join("; ", lines)}");
        if (top.TryGetValue("Racer", out var racer) && top.Values.Any(v => v > racer))
            Fail("construct drive: the racer should be the fastest example on hard ground");
    }

    /// <summary>A client's copy carries the engine's rpm and the drive note (protocol 86).</summary>
    private static void DriveNet()
    {
        if (DriveBlueprint("Buggy") is not { } bp)
            return;
        var server = new ConstructWorld();
        if (DriveSpawn(server, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } car)
            return;
        var q = new DriveWorld();
        DriveSteps(server, q, 30);
        Seat(car);
        DriveSteps(server, q, 60, _ => DriveKeys(car, Buttons.Forward));
        car.Note = DriveNote.Shifting;
        var client = new ConstructWorld();
        var w = new PacketWriter(16384);
        ConstructNet.WriteFull(w, car);
        var r = new PacketReader(w.Span[1..]);
        ConstructNet.Read(ref r, client, 0);
        var copy = client.Get(car.Id);
        var full = copy != null && MathF.Abs(copy.Rpm - car.Rpm) <= 1 && copy.Note == DriveNote.Shifting;
        DriveSteps(server, q, 10, _ => DriveKeys(car, Buttons.Forward));
        car.Note = DriveNote.ClutchOut;
        var cursor = 0;
        uint last = 0;
        ConstructNet.WritePoses(w, server, 100, ref cursor);
        var pr = new PacketReader(w.Span[1..]);
        ConstructNet.ReadPoses(ref pr, client, 1.0, ref last);
        var pose = copy != null && MathF.Abs(copy.Rpm - car.Rpm) <= 1 && copy.Note == DriveNote.ClutchOut;
        Line($"network: the client's buggy at {copy?.Rpm:0} rpm (server {car.Rpm:0}), note {copy?.Note}; full body {full}, pose {pose}");
        if (!full || !pose)
            Fail("construct drive: a client's copy didn't get the rpm and the drive note");
    }

    /// <summary>Eight vehicles (two of each big example and the buggies) driving at once: no allocation, and their tick inside the budget.</summary>
    private static void DriveBudget()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld { Kind = ShapeKind.Terrain };
        var cars = new List<Construct>();
        var names = new[] { "4x4 Truck", "4x4 Truck", "Tracked Crawler", "Tracked Crawler", "Racer", "Electric Cart", "Buggy", "Wired Buggy" };
        for (var i = 0; i < names.Length; i++)
            if (DriveBlueprint(names[i]) is { } bp && DriveSpawn(w, bp, 0, i * 12f, MathF.PI / 2, $"the {names[i]}") is { } c) // (each in its own lane)
                cars.Add(c);
        DriveSteps(w, q, 30);
        foreach (var c in cars)
            Seat(c);
        var parts = cars.Sum(c => c.PartCount);
        void Drive(int t)
        {
            var b = t % 120 < 80 ? Buttons.Forward : Buttons.Forward | Buttons.Left;
            foreach (var c in cars)
                DriveKeys(c, b, t % 45 == 0 ? ConstructParts.KeyReload : (ushort)0);
        }
        Action<int> drive = Drive; // (made once: the delegate itself allocates)
        DriveSteps(w, q, 60, drive); // (warm up: JIT)
        var sw = new System.Diagnostics.Stopwatch();
        var before = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        DriveSteps(w, q, 120, drive);
        var ms = sw.Elapsed.TotalMilliseconds / 120;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"budget: {cars.Count} vehicles ({parts} parts) driving for 120 ticks: {ms:0.00} ms a tick, {bytes} bytes allocated");
        if (bytes > 0)
            Fail($"construct drive: {bytes} bytes allocated in 120 ticks of driving");
        if (ms > 4)
            Fail($"construct drive: {ms:0.00} ms a tick for {cars.Count} vehicles (the budget is a share of 8 ms)");
    }
}
