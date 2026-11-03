using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Large drive" (docs/sandbox/CONSTRUCTS.md, "Big drive"): what moves and powers big constructs, all of it driven. The
    /// library (tools/constructs/big_drive_parts.py) and its refused blocks; the fuel and charge pools at scale; a 105,000 t
    /// body through a capital diesel, gearbox, shaft and axle; a traction city on four hub motor bogies (powered by a
    /// boiler, front pair on pivots, then skid steered, then tall over rough ground: no tipping, no shaking); thrusters
    /// holding that body up, wired, electric, out of fuel; plants and stacks; jacks, legs and landing struts; the wire
    /// with a real host and client; the budget.
    /// </summary>
    private static void CheckLargeDrive()
    {
        LdLibrary();
        LdPools();
        LdShaftDrive();
        LdCityDrive();
        LdSkidCity();
        LdRoughGround();
        LdHover();
        LdSmallThrust();
        LdPlants();
        LdLegs();
        LdRatedLine();
        LdNetwork();
        LdBudget();
    }

    // ---- Building rigs ------------------------------------------------------------------------------------------------

    /// <summary>A blueprint written step by step (each Add returns the step's index, what "on" names).</summary>
    private sealed class LdPlan(string name)
    {
        private readonly StringBuilder _s = new();
        private readonly List<string> _links = [];
        private int _n;

        public int Add(string part, int on = -1, string socket = "", string by = "", int spin = 0)
        {
            if (_n > 0)
                _s.Append(',');
            _s.Append($"{{ \"part\": \"{part}\"");
            if (on >= 0)
                _s.Append($", \"on\": {on}, \"socket\": \"{socket}\", \"by\": \"{by}\"");
            if (spin != 0)
                _s.Append($", \"spin\": {spin}");
            _s.Append(" }");
            return _n++;
        }

        public void Pipe(int from, int to) => _links.Add($"{{ \"type\": \"pipe\", \"from\": {from}, \"to\": {to} }}");

        public ConstructBuild.Blueprint Blueprint() =>
            ConstructBuild.ParseBlueprint($"{{ \"format\": \"call-of-slop-construct-blueprint/1\", \"name\": \"{name}\", \"steps\": [ {_s} ], \"links\": [ {string.Join(", ", _links)} ] }}", name + ".json");
    }

    /// <summary>A 3 by 7 slab of Capital Ballast Blocks (21 of them: 105,000 t), its front along -z. grid[x + 1, z + 3] is a step.</summary>
    private static int[,] LdSlab(LdPlan b)
    {
        const string block = "Capital Ballast Block";
        var g = new int[3, 7];
        g[1, 3] = b.Add(block);
        for (var z = 1; z <= 3; z++)
        {
            g[1, 3 + z] = b.Add(block, g[1, 3 + z - 1], "+z", "-z");
            g[1, 3 - z] = b.Add(block, g[1, 3 - z + 1], "-z", "+z");
        }
        for (var z = 0; z < 7; z++)
        {
            g[0, z] = b.Add(block, g[1, z], "-x", "+x");
            g[2, z] = b.Add(block, g[1, z], "+x", "-x");
        }
        return g;
    }

    /// <summary>A bogie at a slab's corner: on a hub motor, on a steering pivot when it is to turn.</summary>
    private static void LdBogie(LdPlan b, int on, string side, bool pivot, string track = "Capital Bogie", string prefix = "Capital")
    {
        var at = on;
        var socket = side;
        if (pivot)
        {
            at = b.Add($"{prefix} Steering Pivot", at, socket, "base");
            socket = "leaf";
        }
        var hub = b.Add($"{prefix} Hub Motor", at, socket, "mount");
        b.Add(track, hub, "axle", "axle");
    }

    /// <summary>Four bogies under a slab, a boiler on its fuel tank, a chimney on the boiler.</summary>
    private static ConstructBuild.Blueprint LdCity(bool pivots, int tower = 0, int boilers = 1)
    {
        var b = new LdPlan(pivots ? "Traction City" : "Skid City");
        var g = LdSlab(b);
        LdBogie(b, g[0, 0], "-x", pivots);
        LdBogie(b, g[2, 0], "+x", pivots);
        LdBogie(b, g[0, 6], "-x", false);
        LdBogie(b, g[2, 6], "+x", false);
        for (var k = 0; k < boilers; k++)
        {
            var boiler = b.Add("Capital Boiler", g[1, 2 + k * 2], "+y", "-y");
            b.Add("Capital Chimney", boiler, "+y", "bottom");
            b.Add("Capital Fuel Bunker", boiler, k == 0 ? "-z" : "+z", k == 0 ? "+z" : "-z");
        }
        b.Add("Capital Battery Bank", g[0, 3], "+y", "-y");
        // A tall town on it: a column of blocks up the middle.
        var top = g[1, 3];
        for (var k = 0; k < tower; k++)
            top = b.Add("Capital Ballast Block", top, "+y", "-y");
        return b.Blueprint();
    }

    private static Construct? LdBuild(ConstructWorld w, ConstructBuild.Blueprint bp, string what, float ground = 0)
    {
        var c = ConstructBuild.BuildOnGround(w, bp, new Vector3(0, ground, 0), 0, out var why);
        if (c == null)
            Fail($"large drive: {what} doesn't build: {why}");
        return c;
    }

    private static void LdDrive(Construct c, Buttons b)
    {
        c.Driver = 1;
        ConstructEdits.DriveInput(c, b, World.TickDt);
    }

    private static float LdTilt(Construct c) => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;

    private static float LdYaw(Construct c)
    {
        var f = Vector3.Transform(-Vector3.UnitZ, c.Rot);
        return MathF.Atan2(-f.X, -f.Z) * 180 / MathF.PI;
    }

    // ---- The library --------------------------------------------------------------------------------------------------

    private static void LdLibrary()
    {
        foreach (var r in ConstructParts.Refused)
            Fail($"large drive: a shipped part file is refused: {r}");
        var big = ConstructParts.All.Where(p => p.Class > ConstructParts.Small && p.Role is PartRole.Engine or PartRole.Generator or PartRole.Thruster or PartRole.Wheel or PartRole.Suspension or PartRole.Transmission or PartRole.Differential or PartRole.Transfer or PartRole.Joint || p.Role == PartRole.Thruster).ToList();
        int Count(PartRole role) => big.Count(p => p.Role == role);
        Line($"big drive parts: {big.Count}: {Count(PartRole.Engine)} engines, {Count(PartRole.Generator)} plants, {Count(PartRole.Thruster)} thrusters, {Count(PartRole.Wheel)} wheels and tracks, {Count(PartRole.Suspension)} suspensions, {Count(PartRole.Transmission)} gearboxes, {Count(PartRole.Differential)} axles, {Count(PartRole.Transfer)} transfer parts, {Count(PartRole.Joint)} joints");
        if (big.Count < 100 || Count(PartRole.Engine) < 12 || Count(PartRole.Generator) < 12 || Count(PartRole.Thruster) != 10 || Count(PartRole.Wheel) < 12)
            Fail("large drive: a big drive family is missing members");
        foreach (var p in big)
            if (ConstructParts.PortProblems(p) is { Count: > 0 } why)
                Fail($"large drive: {p.Name}: {string.Join("; ", why)}");
        // The classes grow: a capital plant out-powers a large one, a capital mass stays inside the part limit, and every class of thruster is stronger.
        var force = big.Where(p => p.Role == PartRole.Thruster && p.ThrustFuel == EngineFuel.Fuel).Select(p => p.ThrustForce).OrderBy(f => f).ToList();
        for (var k = 1; k < force.Count; k++)
            if (force[k] < force[k - 1] * 3)
                Fail($"large drive: thruster sizes too close: {force[k - 1]} and {force[k]} N");
        var bad = new (string Json, string Want)[]
        {
            (LdBad("generator", "\"generator\": { \"rate\": 100 }"), "a generator on fuel has a \"burnRate\""),
            (LdBad("generator", "\"generator\": { \"rate\": 100, \"fuel\": \"none\", \"burnRate\": 2 }"), "a generator on fuel has a \"burnRate\""),
            (LdBad("generator", "\"generator\": { \"rate\": 100, \"fuel\": \"electric\" }"), "is fuel or none"),
            (LdBad("thruster", "\"thruster\": { \"force\": 100, \"exhaust\": \"+z\" }"), "a thruster on fuel has a \"burnRate\""),
            (LdBad("thruster", "\"thruster\": { \"force\": 100, \"fuel\": \"none\", \"exhaust\": \"+z\" }"), "is fuel or electric"),
            (LdBad("thruster", "\"thruster\": { \"force\": 100, \"burnRate\": 1, \"exhaust\": \"up\" }"), "exhaust"),
        };
        foreach (var (json, want) in bad)
            try
            {
                ConstructParts.Parse(json, "bad.json");
                Fail($"large drive: a bad block was taken (wanted \"{want}\")");
            }
            catch (ConstructPartException e)
            {
                if (!e.Message.Contains(want))
                    Fail($"large drive: refused for the wrong reason: {e.Message} (wanted \"{want}\")");
            }
    }

    private static string LdBad(string role, string block) =>
        $"{{ \"format\": \"call-of-slop-construct-part/1\", \"name\": \"B\", \"tool\": \"wrench\", \"mass\": 5, \"health\": 50, \"role\": \"{role}\", \"boxes\": [ {{ \"size\": [0.4, 0.4, 0.4] }} ], \"sockets\": [ {{ \"name\": \"+z\", \"at\": [0, 0, 0.2], \"dir\": \"+z\" }} ], {block} }}";

    // ---- The pools at scale -------------------------------------------------------------------------------------------

    private static void LdPools()
    {
        var c = new Construct { Fuel = 2e7f };
        for (var k = 0; k < 6000; k++)
            c.UseFuel(0.01f);
        var gone = 2e7f - c.Fuel;
        c.ChargeCapacity = 1e10f;
        c.Charge = 5e9f;
        for (var k = 0; k < 6000; k++)
            c.UseCharge(0.05f);
        for (var k = 0; k < 2000; k++)
            c.UseCharge(-0.05f);
        var used = 5e9f - c.Charge;
        Line($"pools: 60 l sipped off 20 Ml: {gone:0} l; 200 kJ net off 5 TJ: {used:0} kJ");
        if (MathF.Abs(gone - 60) > 4 || MathF.Abs(used - 200) > 1024)
            Fail($"large drive: a big pool loses what is taken a tick at a time ({gone} of 60 l, {used} of 200 kJ)");
    }

    // ---- Through shafts ------------------------------------------------------------------------------------------------

    /// <summary>A 3 by 7 slab with a marine diesel under it: gearbox, shaft, axle and big wheels behind, a free axle in front.</summary>
    private static ConstructBuild.Blueprint LdShaftRig(bool jammed = false)
    {
        var b = new LdPlan("Shaft Rig");
        var g = LdSlab(b);
        var engine = b.Add("Capital Diesel Engine", g[1, 3], "-y", "+y");
        var box = b.Add("Capital Gearbox", engine, "+z", "-z");
        var shaft = b.Add("Capital Drive Shaft 48 m", box, "+z", "end-");
        var axle = b.Add("Capital Axle", shaft, "end+", "-z");
        foreach (var side in new[] { "+x", "-x" })
        {
            var s = b.Add("Capital Suspension", axle, side, "mount");
            b.Add("Capital Tyre", s, "axle", "axle");
        }
        b.Add("Capital Fuel Bunker", engine, "-z", "+z");
        b.Add("Capital Battery Bank", box, "+x", "-x");
        var front = b.Add("Capital Ballast Block", g[1, 0], "-y", "+y");
        foreach (var side in new[] { "+x", "-x" })
        {
            var s = b.Add("Capital Suspension", front, side, "mount");
            b.Add("Capital Tyre", s, "axle", "axle");
        }
        return b.Blueprint();
    }

    private static void LdShaftDrive()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld { Kind = ShapeKind.Terrain };
        if (LdBuild(w, LdShaftRig(), "the shaft rig") is not { } c)
            return;
        DriveSteps(w, q, 120);
        var fuel = c.Fuel;
        var settle = c.Pos.Y;
        var start = c.WorldCom;
        var peak = 0f;
        DriveSteps(w, q, 1200, _ =>
        {
            LdDrive(c, Buttons.Forward);
            peak = MathF.Max(peak, c.AngVel.Length());
        });
        var dist = Vector3.Distance(start with { Y = 0 }, c.WorldCom with { Y = 0 });
        Line($"shaft rig: {c.Mass / 1e6f:0} kt, {c.PartCount} parts, drives {c.Wheels.Take(c.WheelCount).Count(k => k.Driven)} of {c.WheelCount} wheels: {dist:0.0} m in 20 s, {c.Vel.Length():0.0} m/s, {c.Rpm:0} rpm, gear {c.Gear + 1}, {c.DriveTorque / 1e6f:0.0} MN m out, {fuel - c.Fuel:0} l burnt, spin {peak:0.000} rad/s");
        if (!c.CanDrive || c.Mass < 1e8f)
            Fail($"large drive: the shaft rig doesn't drive ({c.DriveProblem}, {c.Mass} kg)");
        if (dist < 20 || !c.EngineRunning)
            Fail($"large drive: a marine diesel through real shafts moved 100,000 t only {dist:0.0} m in 20 s (engine {(c.EngineRunning ? "on" : "off")}, note {c.Note})");
        if (fuel - c.Fuel < 100)
            Fail($"large drive: the engine burnt {fuel - c.Fuel:0} l in 20 s");
        if (peak > 0.3f || !float.IsFinite(c.Pos.Length()) || MathF.Abs(c.Pos.Y - settle) > 3)
            Fail($"large drive: the shaft rig isn't steady (spin {peak}, height {c.Pos.Y} from {settle})");
    }

    // ---- Traction city ------------------------------------------------------------------------------------------------

    private static void LdCityDrive()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld { Kind = ShapeKind.Terrain };
        if (LdBuild(w, LdCity(pivots: true), "the traction city") is not { } c)
            return;
        DriveSteps(w, q, 180);
        var fuel = c.Fuel;
        var settled = c.Pos.Y;
        var tiltBefore = LdTilt(c);
        var start = c.WorldCom;
        var worstAcc = 0f;
        var last = c.Vel;
        DriveSteps(w, q, 1800, _ =>
        {
            LdDrive(c, Buttons.Forward);
            worstAcc = MathF.Max(worstAcc, (c.Vel - last).Length() * 60);
            last = c.Vel;
        });
        var dist = Vector3.Distance(start with { Y = 0 }, c.WorldCom with { Y = 0 });
        var speed = c.Vel.Length();
        var yaw0 = LdYaw(c);
        var angle = c.JointCount > 0 ? 0f : 0f;
        DriveSteps(w, q, 900, _ =>
        {
            LdDrive(c, Buttons.Forward | Buttons.Right);
            angle = MathF.Max(angle, MathF.Abs(c.Joints[0].Angle));
        });
        var turned = World.WrapAngle((LdYaw(c) - yaw0) * MathF.PI / 180) * 180 / MathF.PI;
        Line($"traction city: {c.Mass / 1e6f:0} kt, {c.PartCount} parts, {c.WheelCount} contacts, rest tilt {tiltBefore:0.00} deg: {dist:0.0} m in 30 s, {speed:0.0} m/s, draws {c.PowerUse / 1e6f:0} MW, plant {c.PlantOut / 1000f:0} MW, {fuel - c.Fuel:0} l, smoke {c.SmokeLevel:0.00}, worst {worstAcc:0.0} m/s2; then turned {turned:0.0} deg in 15 s, pivots {angle * 180 / MathF.PI:0} deg");
        if (!c.HasHubMotor || c.WheelCount < 12 || c.Mass < 1e8f)
            Fail($"large drive: the traction city's bogies aren't all there (motors {c.HasHubMotor}, {c.WheelCount} contacts, {c.Mass} kg)");
        if (dist < 30 || speed < 2)
            Fail($"large drive: four powered bogies moved 100,000 t {dist:0.0} m in 30 s ({speed:0.0} m/s; charge {ConstructPower.Share(c):0.00})");
        if (speed > 8)
            Fail($"large drive: the hub motors' top speed let it reach {speed:0.0} m/s");
        if (c.Fuel >= fuel || c.SmokeLevel <= 0 || c.PlantOut <= 0)
            Fail($"large drive: the city's boiler didn't feed it (fuel {fuel} to {c.Fuel}, smoke {c.SmokeLevel}, {c.PlantOut} kW)");
        if (MathF.Abs(turned) < 2 || angle < 0.1f)
            Fail($"large drive: the steered bogies turned the city {turned:0.0} deg (pivots {angle})");
        if (worstAcc > 6 || tiltBefore > 2)
            Fail($"large drive: the city isn't steady (worst {worstAcc} m/s2, tilt {tiltBefore})");
        if (MathF.Abs(c.Pos.Y - settled) > 2)
            Fail($"large drive: the city's height went from {settled} to {c.Pos.Y}");
    }

    private static void LdSkidCity()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld { Kind = ShapeKind.Terrain };
        if (LdBuild(w, LdCity(pivots: false), "the skid city") is not { } c)
            return;
        DriveSteps(w, q, 180);
        var yaw0 = LdYaw(c);
        var start = c.WorldCom;
        DriveSteps(w, q, 1200, _ => LdDrive(c, Buttons.Right));
        var turned = World.WrapAngle((LdYaw(c) - yaw0) * MathF.PI / 180) * 180 / MathF.PI;
        var moved = Vector3.Distance(start with { Y = 0 }, c.WorldCom with { Y = 0 });
        Line($"skid city: skid steers {c.HubSkid}: {turned:0.0} deg on the spot in 20 s, wandering {moved:0.0} m");
        if (!c.HubSkid || MathF.Abs(turned) < 10 || moved > 40)
            Fail($"large drive: a city on fixed bogies didn't skid steer ({turned:0.0} deg, {moved:0.0} m)");
    }

    /// <summary>Lumpy ground with kerbs: a height field every ray is marched against.</summary>
    internal sealed class LdRough : IWorldQuery
    {
        public float Amp = 3;
        public ITerrainCollision? Terrain => null;

        public float Height(float x, float z)
        {
            var h = Amp * MathF.Sin(x / 37f + 0.7f) * MathF.Cos(z / 53f) + Amp * 0.5f * MathF.Sin((x + z) / 19f);
            return h + (MathF.Floor(z / 40f) % 3 == 0 ? Amp * 0.4f : 0); // (a kerb every so often)
        }

        public float Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter) =>
            Raycast(origin, dir, maxDist, filter, out var hit) ? hit.Distance : maxDist;

        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            var step = MathF.Max(0.5f, maxDist / 32f);
            var a = 0f;
            if (origin.Y < Height(origin.X, origin.Z))
                return false;
            for (var t = step; ; t += step)
            {
                var d = MathF.Min(t, maxDist);
                var p = origin + dir * d;
                if (p.Y <= Height(p.X, p.Z))
                {
                    var b = d;
                    for (var k = 0; k < 8; k++)
                    {
                        var m = (a + b) / 2;
                        var pm = origin + dir * m;
                        if (pm.Y <= Height(pm.X, pm.Z))
                            b = m;
                        else
                            a = m;
                    }
                    var at = origin + dir * b;
                    var nx = Height(at.X - 1, at.Z) - Height(at.X + 1, at.Z);
                    var nz = Height(at.X, at.Z - 1) - Height(at.X, at.Z + 1);
                    hit.Distance = b;
                    hit.Point = at;
                    hit.Normal = Vector3.Normalize(new Vector3(nx, 2, nz));
                    hit.Shape = new ShapeId(ShapeKind.Terrain, 0);
                    return true;
                }
                if (d >= maxDist)
                    return false;
                a = d;
            }
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

    /// <summary>A tall city (a tower of ballast up its middle) driven over rough ground, kerbs and all: it mustn't tip or shake.</summary>
    private static void LdRoughGround()
    {
        var w = new ConstructWorld();
        var q = new LdRough();
        if (LdBuild(w, LdCity(pivots: true, tower: 9), "the tall city", q.Height(0, 0) + 2) is not { } c)
            return;
        DriveSteps(w, q, 240);
        var tiltMost = 0f;
        var spin = 0f;
        var jolt = 0f;
        var last = c.Vel;
        var start = c.WorldCom;
        DriveSteps(w, q, 2400, t =>
        {
            LdDrive(c, t % 600 < 400 ? Buttons.Forward : Buttons.Forward | Buttons.Left);
            tiltMost = MathF.Max(tiltMost, LdTilt(c));
            spin = MathF.Max(spin, c.AngVel.Length());
            jolt = MathF.Max(jolt, (c.Vel - last).Length() * 60);
            last = c.Vel;
        });
        var dist = Vector3.Distance(start with { Y = 0 }, c.WorldCom with { Y = 0 });
        Line($"tall city on rough ground: {c.Mass / 1e6f:0} kt, centre {c.Com.Y:0} m above the slab: {dist:0} m in 40 s, the most tilt {tiltMost:0.0} deg, spin {spin:0.000} rad/s, worst jolt {jolt:0.0} m/s2");
        if (dist < 30 || !float.IsFinite(c.Pos.Length()))
            Fail($"large drive: the tall city made {dist:0.0} m on rough ground");
        if (tiltMost > 15 || spin > 0.4f || jolt > 14)
            Fail($"large drive: the tall city tips or shakes on rough ground (tilt {tiltMost:0.0} deg, spin {spin:0.00} rad/s, jolt {jolt:0.0} m/s2)");
    }

    // ---- Thrusters -----------------------------------------------------------------------------------------------------

    /// <summary>A 3 by 7 slab on twelve downward jets (or lift fans), with tanks or plants on it.</summary>
    private static ConstructBuild.Blueprint LdFlyer(string thruster)
    {
        var b = new LdPlan("Flyer");
        var g = LdSlab(b);
        for (var x = 0; x < 3; x++)
        {
            foreach (var z in new[] { 0, 2, 4, 6 })
                b.Add(thruster, g[x, z], "-y", "front");
            foreach (var z in new[] { 1, 5 })
                b.Add("Capital Fuel Bunker", g[x, z], "-y", "+y"); // (each touches the jets either side of it)
        }
        b.Add(thruster, g[0, 6], "+z", "front");
        b.Add(thruster, g[2, 6], "+z", "front");
        b.Add("Capital Fuel Bunker", g[1, 6], "+z", "-z"); // (between the rear pair)
        return b.Blueprint();
    }

    private static void LdHover()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        if (LdBuild(w, LdFlyer("Capital Jet"), "the flyer") is not { } c)
            return;
        var bp = c.ThrusterCount;
        DriveSteps(w, q, 120);
        var y0 = c.WorldCom.Y;
        // Up on the driver's sprint for 8 s, then the driver leaves: it holds.
        DriveSteps(w, q, 600, _ => LdDrive(c, Buttons.Sprint));
        c.Driver = 255;
        var held = c.WorldCom.Y;
        var aim = c.HoverY;
        DriveSteps(w, q, 1500);
        var fuel = c.Fuel;
        var low = float.MaxValue;
        var high = float.MinValue;
        var tilt = 0f;
        DriveSteps(w, q, 600, _ =>
        {
            low = MathF.Min(low, c.WorldCom.Y);
            high = MathF.Max(high, c.WorldCom.Y);
            tilt = MathF.Max(tilt, LdTilt(c));
        });
        var burn = (fuel - c.Fuel) / 10f;
        var want = c.Mass * ConstructPhysics.GravityAccel / 1e6f * ConstructParts.Need("Capital Jet").ThrustBurn / (ConstructParts.Need("Capital Jet").ThrustForce / 1e6f);
        Line($"hover: {bp} jets hold {c.Mass / 1e6f:0} kt: rose {held - y0:0.0} m, asked {aim:0.0}, then {low:0.0} to {high:0.0} m, tilt {tilt:0.00} deg, thrust {c.ThrustShow * 100:0}%, burning {burn:0} l/s (weight says {want:0})");
        if (bp < 12 || c.Mass < 1e8f)
            Fail($"large drive: the flyer has {bp} thrusters, {c.Mass} kg");
        if (held - y0 < 8 || !c.HoverOn)
            Fail($"large drive: the jets lifted 100,000 t only {held - y0:0.0} m (hold {c.HoverOn})");
        if (high - low > 1.5f || MathF.Abs((high + low) / 2 - aim) > 3 || tilt > 2)
            Fail($"large drive: the hover wanders ({low:0.0} to {high:0.0} about {aim:0.0}, tilt {tilt:0.00})");
        if (MathF.Abs(burn - want) > want * 0.15f)
            Fail($"large drive: the hover burnt {burn:0} l/s where its weight needs {want:0}");
        // The driver's W and D, in the air: the rear pair push it on and turn it, and the hover keeps holding.
        var from = c.WorldCom;
        var yaw0 = LdYaw(c);
        DriveSteps(w, q, 600, _ => LdDrive(c, Buttons.Forward));
        var run = from.Z - c.WorldCom.Z;
        DriveSteps(w, q, 300, _ => LdDrive(c, Buttons.Right));
        var turn = World.WrapAngle((LdYaw(c) - yaw0) * MathF.PI / 180) * 180 / MathF.PI;
        c.Driver = 255;
        Line($"in the air: W for 10 s {run:0.0} m on, {c.Vel.Length():0.0} m/s; D for 5 s turns it {turn:0.0} deg; height {c.WorldCom.Y:0.0} (holding {c.HoverY:0.0})");
        if (run < 5)
            Fail($"large drive: the rear jets pushed it {run:0.0} m on W");
        if (MathF.Abs(turn) < 1)
            Fail($"large drive: the rear jets turned it {turn:0.0} deg on D");
        // Falling off the fuel brings it down.
        c.Fuel = 1;
        DriveSteps(w, q, 240);
        if (c.HoverOn || c.ThrustShow > 0.01f)
            Fail($"large drive: out of fuel it still hovers (hold {c.HoverOn}, thrust {c.ThrustShow:0.00})");
    }

    /// <summary>The small sizes, a wire and the circuit: a lever throttles a jet with no seat, a lift fan draws its power, and a flat battery stops it.</summary>
    private static void LdSmallThrust()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        var plan = new LdPlan("Small Flyer");
        var beam = plan.Add("Frame Block");
        plan.Add("Light Lift Fan", beam, "-y", "front");
        plan.Add("Battery Pack", beam, "+y", "-y");
        if (LdBuild(w, plan.Blueprint(), "the small flyer") is not { } c)
            return;
        var fan = -1;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.Thruster)
                fan = i;
        DriveSteps(w, q, 30);
        var y0 = c.WorldCom.Y;
        var charge = c.Charge;
        c.Parts[fan].InCount = 0;
        // No lever in reach of the frame's class: the driver's lift (it is a lifter) drives it, then the draw.
        DriveSteps(w, q, 120, _ => LdDrive(c, Buttons.Sprint));
        c.Driver = 255;
        var used = charge - c.Charge;
        Line($"lift fan: rose {c.WorldCom.Y - y0:0.00} m, drew {used:0.0} kJ, {c.PowerUse / 1000f:0.0} kW");
        if (c.WorldCom.Y - y0 < 0.3f)
            Fail($"large drive: an electric lift fan didn't lift ({c.WorldCom.Y - y0:0.00} m)");
        if (used <= 0 || c.PowerUse <= 0)
            Fail($"large drive: a lift fan drew no power ({used} kJ, {c.PowerUse} W)");
        c.Charge = 0;
        DriveSteps(w, q, 120);
        if (c.ThrustShow > 0.01f)
            Fail($"large drive: a flat battery still runs a lift fan ({c.ThrustShow})");

        // A jet on a switch, no seat: the throttle wire; then the height wire.
        var wj = new ConstructWorld();
        var jp = new LdPlan("Wired Jet");
        var jb = jp.Add("Frame Block");
        jp.Add("Frame Block", jb, "+z", "-z");
        var jet = jp.Add("Small Jet", jb, "-y", "front");
        jp.Add("Small Fuel Tank", jet, "+x", "-x");
        if (LdBuild(wj, jp.Blueprint(), "the wired jet") is not { } rig)
            return;
        var js = SpFitAnywhere(wj, rig, "Switch");
        if (js < 0)
            return;
        var thr = rig.Thrusters[0].Part;
        DriveSteps(wj, q, 30);
        var low = rig.WorldCom.Y;
        DriveSteps(wj, q, 60);
        var idle = rig.ThrustShow;
        SpWire(wj, rig, js, thr, 0);
        rig.Parts[js].State = 1;
        DriveSteps(wj, q, 90);
        var wired = rig.WorldCom.Y - low;
        Line($"a jet on a throttle wire, no seat: idle {idle:0.00}, on {rig.ThrustShow:0.00}: {wired:0.0} m up in 1.5 s");
        if (idle > 0 || wired < 1)
            Fail($"large drive: a wired jet with no seat rose {wired:0.0} m (idle {idle})");

        // A hub motor with no power: nothing; a lever on its throttle wire: it goes with no seat.
        var w2 = new ConstructWorld();
        var cart = new LdPlan("Hub Cart");
        var a = cart.Add("Frame Block");
        foreach (var (on, side) in new[] { (a, "+x"), (a, "-x") })
        {
            var hub = cart.Add("Small Hub Motor", on, side, "mount");
            cart.Add("Wheel", hub, "axle", "axle");
        }
        cart.Add("Battery", a, "+y", "-y");
        if (LdBuild(w2, cart.Blueprint(), "the hub cart") is not { } hc)
            return;
        DriveSteps(w2, q, 30);
        var mount = -1;
        for (var i = 0; i < hc.PartCount; i++)
            if (hc.Parts[i].D.MotorTorque > 0)
                mount = i;
        var start = hc.Pos;
        DriveSteps(w2, q, 120, _ => LdDrive(hc, Buttons.Forward));
        var moved = Vector3.Distance(start, hc.Pos);
        hc.Driver = 255;
        DriveSteps(w2, q, 180);
        var stopped = hc.Vel.Length();
        var hs = SpFitAnywhere(w2, hc, "Switch");
        var carried = 0f;
        if (hs >= 0)
        {
            for (var i = 0; i < hc.PartCount; i++)
                if (hc.Parts[i].D.MotorTorque > 0)
                    SpWire(w2, hc, hs, i, 0);
            hc.Parts[hs].State = 1;
            var from = hc.Pos;
            DriveSteps(w2, q, 120);
            carried = Vector3.Distance(from, hc.Pos);
        }
        Line($"small hub cart: {moved:0.0} m in 2 s on its motors, {hc.WheelCount} wheels, no engine: can drive {hc.CanDrive}; let go it stops ({stopped:0.00} m/s); a switch on the throttle wires, no driver: {carried:0.0} m");
        if (moved < 1 || mount < 0 || hc.CanDrive)
            Fail($"large drive: hub motors drove the cart {moved:0.0} m");
        if (carried < 1)
            Fail($"large drive: a hub motor's throttle wire moved the cart {carried:0.0} m");
    }

    // ---- Plants ---------------------------------------------------------------------------------------------------------

    private static void LdPlants()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        var plan = new LdPlan("Plant Rig");
        var slab = plan.Add("Capital Ballast Block");
        var boiler = plan.Add("Capital Boiler", slab, "+y", "-y");
        plan.Add("Capital Stack Section", boiler, "+y", "bottom");
        var lone = plan.Add("Capital Chimney", slab, "+x", "bottom");
        var reactor = plan.Add("Capital Reactor", slab, "-x", "+x");
        var tank = plan.Add("Capital Fuel Bunker", boiler, "-z", "+z");
        var piped = plan.Add("Capital Chimney", slab, "+z", "bottom");
        plan.Pipe(boiler, piped);
        if (LdBuild(w, plan.Blueprint(), "the plant rig") is not { } c)
            return;
        c.Pinned = true;
        c.Rebuild();
        var k = -1;
        var kr = -1;
        for (var i = 0; i < c.PlantCount; i++)
            if (c.DefOf(c.Plants[i]).PlantFuel == EngineFuel.Fuel)
                k = i;
            else
                kr = i;
        var stack = -1;
        var far = -1;
        var fedPipe = false;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Name == "Capital Stack Section")
                stack = i;
            else if (c.Parts[i].D.Name == "Capital Chimney")
            {
                if (c.SmokeFed[i])
                    fedPipe = true;
                else
                    far = i;
            }
        if (!fedPipe || c.RouteCount != 1)
            Fail($"large drive: a chimney on a pipe from a boiler's exhaust isn't fed ({c.RouteCount} pipes)");
        if (k < 0 || kr < 0 || stack < 0 || far < 0)
        {
            Fail("large drive: the plant rig is short of parts");
            return;
        }
        if (!c.Electrical && !c.HasBattery)
            Fail("large drive: a plant didn't make its construct electrical");
        if (!c.SmokeFed[stack] || c.SmokeFed[far])
            Fail($"large drive: a stack on a boiler (fed {c.SmokeFed[stack]}) and a chimney on a ballast block (fed {c.SmokeFed[far]})");
        DriveSteps(w, q, 10);
        // Nothing asked: the buffers are full and the plants idle: no fuel, no smoke.
        var fuel = c.Fuel;
        DriveSteps(w, q, 60);
        if (c.Fuel < fuel || c.SmokeLevel > 0)
            Fail($"large drive: a plant with nothing to feed burns (fuel {fuel} to {c.Fuel}, smoke {c.SmokeLevel})");
        // A hungry circuit: the reactor's output comes up over its spool, a boiler's fuel only if it is asked.
        c.Charge = 0;
        c.UseCharge(0);
        DriveSteps(w, q, 60);
        var early = c.PlantLevel[kr];
        DriveSteps(w, q, 60 * 40);
        var late = c.PlantLevel[kr];
        Line($"plants: reactor {early:0.00} after 1 s, {late:0.00} after 41 s, {c.PlantOut / 1000f:0} MW; boiler {c.PlantLevel[k]:0.00}, fuel used {fuel - c.Fuel:0} l");
        if (early > 0.1f || c.Charge <= 0)
            Fail($"large drive: a reactor came up in a second ({early}), or fed nothing (charge {c.Charge})");
        // A wire: held off, the plant doesn't run; without its tank (wrecked), it can't.
        c.Parts[tank].Broken = true;
        c.Rebuild();
        var fed = c.PlantFed[k];
        if (fed)
            Fail("large drive: a boiler with a wrecked tank is still fed");
        if (reactor < 0)
            Fail("large drive: the rig's reactor step is missing");

        // A wire on a plant's run port: it holds a genset off, or keeps its fire banked with nothing to feed.
        var w2 = new ConstructWorld();
        var rig = new LdPlan("Genset Rig");
        var bed = rig.Add("Frame Block");
        rig.Add("Frame Block", bed, "+z", "-z");
        var gs = rig.Add("Small Genset", bed, "+y", "-y");
        rig.Add("Small Fuel Tank", gs, "+z", "-z");
        if (LdBuild(w2, rig.Blueprint(), "the genset rig") is not { } g)
            return;
        g.Pinned = true;
        g.Rebuild();
        var sw = SpFitAnywhere(w2, g, "Switch");
        if (sw < 0)
            return;
        SpWire(w2, g, sw, g.Plants[0], 0);
        g.Charge = 0;
        DriveSteps(w2, q, 120);
        var off = g.PlantOut;
        var fuel2 = g.Fuel;
        g.Parts[sw].State = 1;
        DriveSteps(w2, q, 300);
        Line($"a genset on a wire: off {off:0} kW, on {g.PlantOut:0} kW, level {g.PlantLevel[0]:0.00}, fuel {fuel2 - g.Fuel:0.00} l, smoke {g.SmokeLevel:0.00}");
        if (off > 0 || g.PlantLevel[0] < ConstructPlant.BankedFire - 0.01f || g.Fuel >= fuel2 || g.SmokeLevel <= 0)
            Fail($"large drive: a wire doesn't run a plant (off {off} kW, level {g.PlantLevel[0]}, fuel {fuel2} to {g.Fuel})");
    }

    // ---- Legs -----------------------------------------------------------------------------------------------------------

    private static void LdLegs()
    {
        // A block on four landing struts stands, sags under its weight only a little, and lands soft from a drop.
        var w = new ConstructWorld();
        var q = new DriveWorld();
        var plan = new LdPlan("Strutted");
        var g = LdSlab(plan);
        foreach (var at in new[] { g[0, 0], g[2, 0], g[0, 6], g[2, 6] })
            plan.Add("Capital Landing Strut", at, "-y", "mount");
        if (LdBuild(w, plan.Blueprint(), "the strutted slab") is not { } c)
            return;
        var dropped = c.Pos.Y;
        DriveSteps(w, q, 300);
        var steady = c.Pos.Y;
        var moved = c.Vel.Length();
        Line($"landing struts: {c.SpringCount} under {c.Mass / 1e6f:0} kt: rests {steady:0.00} m (spawned {dropped:0.00}), speed {moved:0.000} m/s");
        if (c.SpringCount != 4 || moved > 0.2f || steady < 8 || !float.IsFinite(steady))
            Fail($"large drive: the struts don't hold the slab ({c.SpringCount} springs, {moved} m/s, at {steady})");

        // Jacks under a block: extended (F works them), they lift it without a kick; a leg's knee holds.
        var w2 = new ConstructWorld();
        var jack = new LdPlan("Jacked");
        var slab = jack.Add("Capital Ballast Block");
        var mid = new[] { "+x", "-x", "+z", "-z" };
        foreach (var side in mid)
        {
            var b = jack.Add("Capital Ballast Block", slab, side, side[0] == '+' ? "-" + side[1] : "+" + side[1]);
            var j0 = jack.Add("Capital Jack", b, "-y", "base");
            jack.Add("Capital Ballast Block", j0, "top", "+y"); // (its foot)
        }
        if (LdBuild(w2, jack.Blueprint(), "the jacked block") is not { } j)
            return;
        DriveSteps(w2, q, 120);
        var y0 = j.WorldCom.Y;
        for (var k = 0; k < j.JointCount; k++)
            j.Joints[k].Open = true;
        var worst = 0f;
        DriveSteps(w2, q, 900, _ =>
        {
            for (var k = 0; k < j.JointCount; k++)
                j.Joints[k].Open = true;
            j.Wake();
            worst = MathF.Max(worst, j.Vel.Length());
        });
        var rose = j.WorldCom.Y - y0;
        var over = j.JointCount > 0 ? j.Joints[0].Over : 0;
        Line($"jacks: {j.JointCount} under {j.Mass / 1e6f:0} kt: rose {rose:0.0} m in 15 s, top speed {worst:0.00} m/s, load {over:0.00} of its rating, tilt {LdTilt(j):0.0} deg");
        if (j.JointCount != 4 || rose < 4 || worst > 3 || !float.IsFinite(rose) || LdTilt(j) > 5)
            Fail($"large drive: capital jacks lifted their load {rose:0.0} m (top speed {worst}, tilt {LdTilt(j)})");

        // A leg's knee on a pinned block swings a heavy shin up to where it is sent, and holds it.
        var w3 = new ConstructWorld();
        var leg = new LdPlan("Leg");
        var hip = leg.Add("Large Ballast Block");
        var knee = leg.Add("Large Knee Hinge", hip, "+x", "base");
        var shin = leg.Add("Large Ballast Block", knee, "leaf", "-x");
        leg.Add("Large Ballast Block", shin, "+x", "-x");
        if (LdBuild(w3, leg.Blueprint(), "the leg") is not { } lg)
            return;
        lg.Pinned = true;
        lg.Rebuild();
        DriveSteps(w3, q, 30);
        DriveSteps(w3, q, 480, _ =>
        {
            lg.Joints[0].Open = true;
            lg.Wake();
        });
        var bent = lg.Joints[0].Angle * 180 / MathF.PI;
        Line($"a leg's knee: {lg.Joints[0].Angle:0.00} rad ({bent:0} deg), load {lg.Joints[0].Over:0.00} of its rating");
        if (MathF.Abs(bent) < 60 || !float.IsFinite(bent))
            Fail($"large drive: a knee hinge swung its shin {bent:0} deg");
    }

    /// <summary>The drive line's rating: a capped part caps what a wheel gets, and says it slips.</summary>
    private static void LdRatedLine()
    {
        var w = new ConstructWorld();
        if (LdBuild(w, LdShaftRig(), "the shaft rig") is not { } c)
            return;
        var rated = float.PositiveInfinity;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Driven)
                rated = MathF.Min(rated, c.Wheels[k].CapEq);
        var top = ConstructParts.Need("Capital Gearbox").MaxTorque;
        Line($"rated line: the shaft rig's wheels carry {rated / 1e6f:0} MN m out of the box ({top / 1e6f:0} the box's own)");
        if (!(rated <= top) || rated <= 0)
            Fail($"large drive: the drive line's rating didn't reach its wheels ({rated})");
        if (ConstructDrive.NoteText(DriveNote.Slipping).Contains("belt"))
            Fail("large drive: the slip note only speaks of belts");
    }

    // ---- The wire -------------------------------------------------------------------------------------------------------

    private static void LdNetwork()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"large drive: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26391;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue)
        {
            Fail("large drive: the client didn't join");
            return;
        }
        server.Net.SimulateBadNetwork(40, 8, 3);
        a.Net.SimulateBadNetwork(40, 8, 3);
        var w = server.Constructs;
        foreach (var body in w.Bodies.ToList())
            w.Remove(body);
        var plan = new LdPlan("Net Flyer");
        var slab = plan.Add("Large Ballast Block");
        var jet = plan.Add("Large Jet", slab, "-y", "front");
        var boiler = plan.Add("Large Boiler", slab, "+y", "-y");
        plan.Add("Large Fuel Bunker", boiler, "+x", "-x");
        plan.Add("Large Fuel Bunker", jet, "+x", "-x");
        var city = ConstructBuild.BuildOnGround(w, plan.Blueprint(), OpenSpot(w) ?? new Vector3(0, 0, 0), 0, out var why);
        if (city == null)
        {
            Fail($"large drive: the network flyer doesn't build: {why}");
            return;
        }
        city.Fuel = 800000;
        city.Wake();
        Pump(server, [a], 1.0, ref now, clock);
        // Hold it up on the wire's side: the hover on, the plant's smoke, the fuel past what the old field held.
        PumpWith(server, [a], 2.0, ref now, clock, () =>
        {
            city.HoverOn = true;
            city.HoverY = city.WorldCom.Y + 0.5f;
            city.Charge = 0;
            city.Wake();
        });
        var copy = a.Constructs.Get(city.Id);
        var ok = copy != null && copy.HoverOn && copy.ThrustShow > 0.01f && MathF.Abs(copy.Fuel - city.Fuel) < 8000 && copy.Fuel > 7000 && MathF.Abs(copy.ThrustShow - city.ThrustShow) < 0.3f;
        Line($"network: the client's flyer: hover {copy?.HoverOn}, thrust {copy?.ThrustShow:0.00} (host {city.ThrustShow:0.00}), fuel {copy?.Fuel:0} (host {city.Fuel:0}), smoke {copy?.SmokeLevel:0.00} (host {city.SmokeLevel:0.00}), {copy?.ThrusterCount} thrusters");
        if (!ok)
            Fail("large drive: a client's copy didn't get the hover, the thrust or a big tank's fuel");
        if (copy == null || copy.PlantCount != city.PlantCount || copy.ThrusterCount != city.ThrusterCount)
            Fail("large drive: a client's copy hasn't its plants and thrusters");
        foreach (var body in w.Bodies.ToList())
            w.Remove(body);
    }

    // ---- The budget -----------------------------------------------------------------------------------------------------

    private static void LdBudget()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld { Kind = ShapeKind.Terrain };
        var bodies = new List<Construct>();
        var city = LdCity(pivots: true);
        for (var k = 0; k < 3; k++)
            if (ConstructBuild.BuildOnGround(w, city, new Vector3(k * 500, 0, 0), 0, out _) is { } c)
                bodies.Add(c);
        if (ConstructBuild.BuildOnGround(w, LdFlyer("Capital Jet"), new Vector3(1600, 0, 0), 0, out _) is { } f)
            bodies.Add(f);
        DriveSteps(w, q, 60);
        void Drive(int t)
        {
            foreach (var c in bodies)
                LdDrive(c, c.ThrusterCount > 0 ? Buttons.Sprint : Buttons.Forward);
        }
        Action<int> drive = Drive;
        DriveSteps(w, q, 60, drive);
        var sw = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        DriveSteps(w, q, 120, drive);
        var ms = sw.Elapsed.TotalMilliseconds / 120;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"budget: {bodies.Count} big bodies ({bodies.Sum(c => c.PartCount)} parts) for 120 ticks: {ms:0.00} ms a tick, {bytes} bytes allocated");
        if (bytes > 0)
            Fail($"large drive: {bytes} bytes allocated in 120 ticks");
        if (ms > 4)
            Fail($"large drive: {ms:0.00} ms a tick");
    }
}
