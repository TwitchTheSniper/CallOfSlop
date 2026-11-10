using System.Numerics;
using System.Text;

namespace CallOfSlop.VehicleKit;

/// <summary>What a vehicle must do, as a generator wants it checked (the defaults suit a light road vehicle).</summary>
public sealed class VehicleCheckOptions
{
    /// <summary>The drive run: seconds on full throttle, and at least this far from a standstill (m).</summary>
    public float DriveSeconds = 6, MinDistance = 30;

    /// <summary>The most it may wander off its line on the drive run (degrees).</summary>
    public float MaxYawDrift = 12;

    /// <summary>The turn: seconds at full steer, and the least it must come round (degrees).</summary>
    public float TurnSeconds = 5, MinTurn = 60;

    /// <summary>Most tilt allowed at any moment of the runs (degrees).</summary>
    public float MaxTilt = 25;

    /// <summary>Part count and mass limits (kg, with its cargo).</summary>
    public int MaxParts = 220;

    public float MinMass = 300, MaxMass = 3000;

    /// <summary>Half the track over the centre of mass's height above the ground: under 1 a hard turn can roll it.</summary>
    public float MinStability = 0.8f;

    /// <summary>The most of its bounding box a body may fill, seen from the side and from above (VEHICLEKIT.md, "Silhouette").</summary>
    public float MaxSideFill = 0.62f, MaxTopFill = 0.84f;

    /// <summary>It's towed: nothing drives it, so only its build, its z-fighting and how it stands are checked.</summary>
    public bool Towed;

    /// <summary>It flies (an aircraft generator, <see cref="IAircraftShape"/>): no road drive or turn; the flight check (<see cref="VehicleFlightCheck"/>) instead.</summary>
    public bool Flies;
}

/// <summary>What <see cref="VehicleChecks.Check"/> found: failures (it is wrong), notes (numbers worth reading).</summary>
public sealed class VehicleReport(string name)
{
    public readonly string Name = name;
    public readonly List<string> Failures = new(), Notes = new();
    public int Parts, Steps, Wires, Links, Cargo, Welds;
    public float Mass, Distance, Yaw, Turned, TopSpeed, WorstTilt, Stability;
    public Vector3 Com;
    public int Shots;

    public bool Passed => Failures.Count == 0;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"{Name}: {(Passed ? "OK" : $"{Failures.Count} problem(s)")}; {Parts} parts from {Steps} steps, {Welds} freeform welds, {Wires} links, {Cargo} cargo, {Mass:0} kg, drive {Distance:0.0} m at {TopSpeed:0.0} m/s yaw {Yaw:0.0} deg, turn {Turned:0} deg, worst tilt {WorstTilt:0.0} deg, stability {Stability:0.00}, {Shots} shots\n");
        foreach (var n in Notes)
            sb.Append("  ").Append(n).Append('\n');
        foreach (var f in Failures)
            sb.Append("  FAIL ").Append(f).Append('\n');
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// The point of the kit, as <c>MapKit.MapChecks</c> is the point of the map kit: a generated vehicle is checked by the real game before anyone
/// drives it. The blueprint text goes back through the loader and is built as any blueprint is; then, on a flat floor, the real physics drive it
/// through its Driving Module and turn it, the real weapons fire its guns through its Turret Module, and the report says what happened. Runs a few simulated seconds.
/// </summary>
public static class VehicleChecks
{
    /// <summary>A towed blueprint by its name, made by its generator so a check runs on what would ship, from any folder.</summary>
    public static ConstructBuild.Blueprint? TowOf(ConstructBuild.Blueprint bp)
    {
        if (bp.Tow.Length == 0)
            return null;
        var at = bp.Tow.LastIndexOf(' ');
        return VehicleRegistry.Find(bp.Tow[..at]) is { } g ? ConstructBuild.ParseBlueprint(VehicleRegistry.Run(g, int.Parse(bp.Tow[(at + 1)..]), new VehicleLayers()), "tow.json") : ConstructBuild.FindBlueprint(bp.Tow);
    }

    public static VehicleReport Check(string text, string file, VehicleCheckOptions? options = null, IReadOnlyList<Room>? rooms = null)
    {
        var o = options ?? new VehicleCheckOptions();
        var bp = ConstructBuild.ParseBlueprint(text, file);
        var r = new VehicleReport(bp.Name) { Steps = bp.Steps.Count, Wires = bp.Wires.Count + bp.Links.Count, Cargo = bp.Cargo.Count, Welds = bp.StepWeld.Count(w => w != null) };
        var q = new SelfTest.DriveWorld();
        var w = new ConstructWorld();
        var tow = TowOf(bp);
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out var why, tow);
        if (c == null)
        {
            r.Failures.Add($"it doesn't build: {why}");
            return r;
        }
        Static(c, w, bp, r, o);
        View(c, r);
        ZFighting(c, r);
        Silhouette(c, r, o);
        var cargoBodies = w.Bodies.Count;
        Settle(w, q, 90);
        Rest(c, r);
        if (w.Bodies.Count != cargoBodies)
            r.Failures.Add($"it came apart standing still: {cargoBodies} bodies became {w.Bodies.Count}");
        if (o.Towed)
            return r;
        if (o.Flies)
        {
            VehicleFlightCheck.Into(text, file, r);
            return r;
        }
        Drive(bp, o, r);
        Turn(bp, o, r);
        Guns(bp, r);
        LootCheck(bp, r);
        foreach (var room in rooms ?? [])
            Interior(bp, room, r);
        return r;
    }

    private const float TinyFight = 6e-4f; // m2

    /// <summary>
    /// A lofted cab's view out (body styles phase 3): from the driver's eye, rays ahead in a fan (30 degrees either way, level and a little up) through what is
    /// drawn by the lofted parts (cab, hood, rooms), glass left out; the one straight ahead must leave the vehicle, and at most a third may meet it within 4 m (its own pillars, a slit's edges).
    /// </summary>
    private static void View(Construct c, VehicleReport r)
    {
        if (c.DriveSeat < 0)
            return;
        var lofted = false;
        for (var i = 0; i < c.PartCount && !lofted; i++)
            lofted = HullLoft.CabOf(c.DefOf(i).Name) != null;
        if (!lofted)
            return;
        var (tris, owner) = VehiclePlan.PartTriangles(c);
        var keep = new List<Vector3>(tris.Length);
        for (var t = 0; t < owner.Length; t++)
        {
            var o = owner[t];
            if (o < 0 || o >= c.PartCount || c.DefOf(o).HasGlass || !c.DefOf(o).Name.StartsWith(HullLoft.Prefix, StringComparison.Ordinal) || HullLoft.IsDressing(c.DefOf(o).Name))
                continue; // (the cab, the hood and the rooms: what the kit lofted; a mast or a trophy in the way is dressing, judged by the eye)
            keep.Add(tris[3 * t]);
            keep.Add(tris[3 * t + 1]);
            keep.Add(tris[3 * t + 2]);
        }
        var solid = keep.ToArray();
        var back = Quaternion.Inverse(c.Rot);
        var eye = Vector3.Transform(ConstructEdits.SeatFeet(c, c.DriveSeat) + Vector3.UnitY * CallOfSlop.World.EyeHeight - c.Pos, back);
        var ahead = Rot24.Apply(c.Parts[c.DriveSeat].Rot, -Vector3.UnitZ);
        var (blocked, rays, straight) = (0, 0, false);
        foreach (var pitch in (float[])[0, 4])
            for (var yaw = -30; yaw <= 30; yaw += 10)
            {
                var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180) * Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(ahead, Vector3.UnitY)), pitch * MathF.PI / 180);
                var way = Vector3.Normalize(Vector3.Transform(ahead, q));
                var hit = VehiclePlan.Hits(solid, eye, way, 4f);
                rays++;
                blocked += hit ? 1 : 0;
                straight |= hit && yaw == 0 && pitch == 0;
            }
        r.Notes.Add($"the driver's view: {rays - blocked} of {rays} rays out of the cab clear");
        if (straight)
            r.Failures.Add("the driver can't see straight ahead out of the cab");
        else if (blocked * 3 > rays)
            r.Failures.Add($"the driver's view: {blocked} of {rays} rays ahead meet the vehicle within 4 m");
    }

    /// <summary>VEHICLEKIT.md, "Silhouette": a body that fills its bounding box from the side or from above is a box, however its corners are cut. Wheels and what's on a joint don't count.</summary>
    private static void Silhouette(Construct c, VehicleReport r, VehicleCheckOptions o)
    {
        var (side, top, roof) = VehiclePlan.Outline(c);
        r.Notes.Add($"silhouette: {side:P0} of its side box, {top:P0} of its plan box, roofline varies {roof:0.00}");
        if (side > o.MaxSideFill)
            r.Failures.Add($"it fills {side:P0} of its bounding box from the side (at most {o.MaxSideFill:P0}): a box. Break the outline: separate masses, a stepped roof, a nose, a tail");
        if (top > o.MaxTopFill)
            r.Failures.Add($"it fills {top:P0} of its bounding box from above (at most {o.MaxTopFill:P0}): a box. Narrow a cab or a tail, or leave a gap");
    }

    private static void Settle(ConstructWorld w, IWorldQuery q, int ticks)
    {
        for (var t = 0; t < ticks; t++)
            w.Step(q, World.TickDt);
    }

    private static void Static(Construct c, ConstructWorld w, ConstructBuild.Blueprint bp, VehicleReport r, VehicleCheckOptions o)
    {
        r.Parts = c.PartCount;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Name.Contains("Fender", StringComparison.Ordinal))
            {
                c.SegPose(c.Parts[i].Seg, out var q, out _);
                if (Vector3.Transform(Rot24.Apply(c.Parts[i].Rot, Vector3.UnitY), q).Y < 0.7f)
                    r.Failures.Add($"{c.Parts[i].D.Name} (part {i}) is upside down or on its side: a fender's crown goes up, over the wheel");
            }
        r.Mass = c.Mass; // (with its cargo: a frozen volume adds what it carries)
        if (bp.Cargo.Count > 0 && c.CargoMass <= 0)
            r.Failures.Add($"its {bp.Cargo.Count} cargo isn't riding in a frozen Cargo Volume");
        if (c.PartCount > o.MaxParts)
            r.Failures.Add($"{c.PartCount} parts: over the limit of {o.MaxParts}");
        if (r.Mass < o.MinMass || r.Mass > o.MaxMass)
            r.Failures.Add($"{r.Mass:0} kg: outside {o.MinMass:0} to {o.MaxMass:0} kg");
        if (c.PartCount < bp.Steps.Count)
            r.Notes.Add($"{bp.Steps.Count - c.PartCount} step(s) became links (old fuel lines or shafts)");
        if (o.Towed || o.Flies)
            return;
        if (!c.HasPowertrain)
            r.Failures.Add($"no working powertrain: {c.DriveProblem}");
        if (c.DriveSeat < 0)
            r.Failures.Add("no seat drives it: link a seat through a Driving Module to the drivetrain");
        if (!c.CanDrive)
            r.Failures.Add($"it can't drive: {c.DriveProblem}");
        if (c.Engine >= 0 && c.DefOf(c.Engine).FuelKind != EngineFuel.Electric && (c.Fuel <= 0 || !ConstructRoutes.PipeFeeds(c, c.Engine))) // (an Electric Motor runs on its battery: a spawn context off world, VehicleContext.cs)
            r.Failures.Add($"no fuel reaches the engine ({c.Fuel:0} l aboard): pipe a tank to its Fuel in");
        r.Notes.Add($"fuel {c.Fuel:0.0} l; power: {(c.ChargeCapacity > 0 ? $"{c.ChargeCapacity:0} of battery" : "no electrics")}");
        var driven = 0;
        var steered = 0;
        float trackL = 0, trackR = 0, zF = float.MaxValue, zB = float.MinValue;
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var wh = ref c.Wheels[k];
            driven += wh.Driven ? 1 : 0;
            steered += wh.Steers ? 1 : 0;
            trackL = MathF.Min(trackL, wh.Centre.X);
            trackR = MathF.Max(trackR, wh.Centre.X);
            zF = MathF.Min(zF, wh.Centre.Z);
            zB = MathF.Max(zB, wh.Centre.Z);
        }
        r.Notes.Add($"wheels: {c.WheelCount}, {driven} driven, {steered} steered; wheelbase {zB - zF:0.00} m");
        if (driven == 0 || steered == 0)
            r.Failures.Add($"{driven} driven and {steered} steered wheels: it needs both");
        var ground = float.MaxValue;
        for (var k = 0; k < c.WheelCount; k++)
            ground = MathF.Min(ground, c.Wheels[k].Centre.Y - c.Wheels[k].Travel * 0.5f - c.Wheels[k].Radius);
        r.Com = c.Com;
        var mid = (trackL + trackR) * 0.5f;
        var height = c.Com.Y - ground;
        r.Stability = (trackR - trackL) * 0.5f / MathF.Max(0.1f, height);
        r.Notes.Add($"centre of mass {height:0.00} m up, {MathF.Abs(c.Com.X - mid):0.00} m off the middle, {(c.Com.Z - zF) / MathF.Max(0.1f, zB - zF) * 100:0}% of the way back along the wheelbase");
        if (c.Com.Z < zF || c.Com.Z > zB || c.Com.X < trackL || c.Com.X > trackR)
            r.Failures.Add("the centre of mass is outside its wheels");
        if (r.Stability < o.MinStability)
            r.Failures.Add($"stability {r.Stability:0.00} (half track over centre of mass height): under {o.MinStability:0.00} it rolls on a hard turn");
    }

    /// <summary>Coplanar faces between parts, each in its paint or colour; a part's own boxes are checked when it loads.</summary>
    private static void ZFighting(Construct c, VehicleReport r)
    {
        var boxes = new List<ZFight.ZBox>();
        for (var i = 0; i < c.PartCount; i++)
            AddBoxes(c, boxes, $"{i}:{c.DefOf(i).Name}", c.DefOf(i), c.Parts[i].Pos, c.Parts[i].Rot, c.Parts[i].Seg, c.Parts[i].Paint);
        var all = ZFight.Find(boxes, sameLookOk: true).Where(h => boxes[h.A].Name != boxes[h.B].Name).ToList();
        var hits = all.Where(h => h.Area >= TinyFight).ToList(); // (sockets land on a 1 cm grid, so a quarter plate's edge can sit in a wall's end: a few cm2 of a plate's edge)
        r.Notes.Add($"coplanar faces between parts: {hits.Count} among {boxes.Count} boxes, and {all.Count - hits.Count} slivers");
        foreach (var h in hits.Take(8))
            r.Failures.Add($"z-fighting: {ZFight.Describe(boxes, h)}");
    }

    /// <summary>One part's boxes as <see cref="ZFighting"/> sees them: rest frame, its paint or colour, a wheel and what's on a joint never at rest.</summary>
    private static void AddBoxes(Construct c, List<ZFight.ZBox> boxes, string name, ConstructPartDef d, Vector3 pos, byte rot24, byte seg, uint paint)
    {
        c.RestPose(seg, out var q, out var t);
        var rot = q * Rot24.Quat(rot24);
        var look = paint != 0 ? (int)paint : 1 + d.Id;
        var moving = d.Role is PartRole.Wheel or PartRole.Suspension || seg != 0 && !c.Rigid(seg);
        foreach (var b in ZFight.Boxes(d))
            boxes.Add(b.Hull != null
                ? b.Moved(rot, t + Vector3.Transform(pos, q)) with { Name = name, Look = look, Moving = moving } // (a faceted "hull" shape)
                : ZFight.ZBox.Of(name, t + Vector3.Transform(pos + Rot24.Apply(rot24, b.C), q), b.H, rot) with { Look = look, Moving = moving });
    }

    /// <summary>
    /// What the z-fighting check would say about <paramref name="def"/> placed as <paramref name="g"/> says, against the parts already on <paramref name="c"/>
    /// near it (null: nothing). Dressing asks before it fits a part (<see cref="Vehicle.Dress"/>): a trophy on a hood's top socket can hang a cable down the
    /// side of an engine's rocker cover that pokes up under the hood, and only the faces tell.
    /// </summary>
    public static string? ZFightsWith(Construct c, ConstructPartDef def, in ConstructGhost g)
    {
        var boxes = new List<ZFight.ZBox>();
        AddBoxes(c, boxes, "new:" + def.Name, def, g.Pos, g.Rot, g.Seg, 0);
        var mine = boxes.Count;
        if (mine == 0)
            return null;
        var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var b in boxes)
            (lo, hi) = (Vector3.Min(lo, b.C - new Vector3(b.Reach)), Vector3.Max(hi, b.C + new Vector3(b.Reach)));
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            c.RestPose(c.Parts[i].Seg, out var q, out var t);
            var mid = t + Vector3.Transform(c.Parts[i].Pos + Rot24.Apply(c.Parts[i].Rot, (d.BoundsMin + d.BoundsMax) * 0.5f), q);
            var r = (d.BoundsMax - d.BoundsMin).Length() * 0.5f + 0.01f;
            if (mid.X + r < lo.X || mid.Y + r < lo.Y || mid.Z + r < lo.Z || mid.X - r > hi.X || mid.Y - r > hi.Y || mid.Z - r > hi.Z)
                continue; // (too far to share a face, or to cover one)
            AddBoxes(c, boxes, $"{i}:{d.Name}", d, c.Parts[i].Pos, c.Parts[i].Rot, c.Parts[i].Seg, c.Parts[i].Paint);
        }
        foreach (var h in ZFight.Find(boxes, sameLookOk: true, split: mine))
            if ((h.A < mine) != (h.B < mine) && h.Area >= TinyFight)
                return ZFight.Describe(boxes, h);
        return null;
    }

    private static void Rest(Construct c, VehicleReport r)
    {
        r.WorstTilt = MathF.Max(r.WorstTilt, Tilt(c));
        if (Tilt(c) > 8)
            r.Failures.Add($"it stands {Tilt(c):0.0} degrees off level");
        if (c.Wheels.Take(c.WheelCount).Any(w => w.Radius <= 0))
            r.Failures.Add("a wheel has no size");
    }

    private static float Tilt(Construct c) =>
        MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;

    private static float Heading(Construct c)
    {
        var f = Vector3.Transform(c.SeatForward(), c.Rot);
        return MathF.Atan2(f.X, -f.Z);
    }

    private static Construct? Fresh(ConstructBuild.Blueprint bp, ConstructWorld w, IWorldQuery q)
    {
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out _, TowOf(bp));
        if (c == null)
            return null;
        Settle(w, q, 30);
        c.Driver = 1;
        c.DriverSeat = c.DriveSeat;
        return c;
    }

    private static void Keys(Construct c, Buttons b)
    {
        if (c.DriveSeat >= 0)
            c.Parts[c.DriveSeat].Keys = (ushort)((ushort)b | CPart.Occupied);
        ConstructEdits.DriveInput(c, b, World.TickDt);
    }

    private static void Drive(ConstructBuild.Blueprint bp, VehicleCheckOptions o, VehicleReport r)
    {
        var q = new SelfTest.DriveWorld();
        var w = new ConstructWorld();
        if (Fresh(bp, w, q) is not { } c)
            return;
        var p0 = c.WorldCom;
        var h0 = Heading(c);
        for (var t = 0; t < o.DriveSeconds * 60; t++)
        {
            Keys(c, Buttons.Forward);
            w.Step(q, World.TickDt);
            r.WorstTilt = MathF.Max(r.WorstTilt, Tilt(c));
            r.TopSpeed = MathF.Max(r.TopSpeed, c.Vel.Length());
            if (t % World.TickRate == World.TickRate - 1)
                ConstructPipes.Tick(w, (uint)t); // (as the server does, once a second)
        }
        Pipes(c, r);
        var moved = c.WorldCom - p0;
        r.Distance = new Vector2(moved.X, moved.Z).Length();
        r.Yaw = World.WrapAngle(Heading(c) - h0) * 180 / MathF.PI;
        var along = Vector2.Dot(new Vector2(moved.X, moved.Z), new Vector2(MathF.Sin(h0), -MathF.Cos(h0)));
        if (r.Distance < o.MinDistance)
            r.Failures.Add($"it drove {r.Distance:0.0} m in {o.DriveSeconds:0} s: under {o.MinDistance:0} m (rpm {c.Rpm:0}, {c.DriveProblem})");
        if (along < 0)
            r.Failures.Add("forward on the Driving Module takes it backwards");
        if (MathF.Abs(r.Yaw) > o.MaxYawDrift)
            r.Failures.Add($"it wandered {r.Yaw:0.0} degrees on a straight run: over {o.MaxYawDrift:0} (a weight off to one side, or a wheel not mirrored)");
        var towed = bp.Tow.Length > 0 ? 1 : 0;
        if (w.Bodies.Count(x => x.PartCount > 0) > 1 + bp.Cargo.Count + towed)
            r.Failures.Add("parts came off on the drive run");
        if (towed > 0 && (w.CouplingCount != 1 || w.Bodies.FirstOrDefault(x => x != c && x.WheelCount > 0) is not { } trailer || Vector3.Distance(trailer.WorldCom, c.WorldCom) > 12))
            r.Failures.Add("its trailer didn't come with it on the drive run");
        var left = w.Bodies.Count(b => b != c && b.WheelCount == 0 && Vector3.Distance(b.WorldCom, c.WorldCom) < 8);
        if (left < bp.Cargo.Count)
            r.Failures.Add($"{bp.Cargo.Count - left} of {bp.Cargo.Count} cargo fell off on the drive run");
    }

    /// <summary>A Pipe Box (docs/sandbox/MEDIA.md, "Pipe Boxes") must have struck up a pipe tune by the end of the drive.</summary>
    private static void Pipes(Construct c, VehicleReport r)
    {
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (!p.D.MediaPipes)
                continue;
            if (p.Media is { State: MediaState.Playing, Entry: { } line } && MediaEntry.Track(line) is { Pipes: not null } tune)
                r.Notes.Add($"pipes: the {p.D.Name} struck up \"{tune.Name}\" while it drove");
            else
                r.Failures.Add($"the {p.D.Name} didn't strike up while it drove (engine {(c.EngineRunning ? "running" : "off")})");
        }
    }

    private static void Turn(ConstructBuild.Blueprint bp, VehicleCheckOptions o, VehicleReport r)
    {
        var q = new SelfTest.DriveWorld();
        var w = new ConstructWorld();
        if (Fresh(bp, w, q) is not { } c)
            return;
        for (var t = 0; t < 180; t++)
        {
            Keys(c, Buttons.Forward);
            w.Step(q, World.TickDt);
        }
        var h0 = Heading(c);
        var turned = 0f;
        var last = h0;
        for (var t = 0; t < o.TurnSeconds * 60; t++)
        {
            Keys(c, Buttons.Forward | Buttons.Right);
            w.Step(q, World.TickDt);
            r.WorstTilt = MathF.Max(r.WorstTilt, Tilt(c));
            var h = Heading(c);
            turned += World.WrapAngle(h - last);
            last = h;
        }
        r.Turned = turned * 180 / MathF.PI;
        if (r.Turned < o.MinTurn)
            r.Failures.Add($"right on the Driving Module turned it {r.Turned:0} degrees in {o.TurnSeconds:0} s: under {o.MinTurn:0} (the sign matters: left is wrong too)");
        if (r.WorstTilt > o.MaxTilt)
            r.Failures.Add($"it tilted {r.WorstTilt:0.0} degrees on the runs: over {o.MaxTilt:0}");
        if (Vector3.Transform(Vector3.UnitY, c.Rot).Y < 0.3f)
            r.Failures.Add("it rolled over on the turn");
    }

    /// <summary>Spawns it a few times: each spawn's containers must take what the table gave them, and two spawns shouldn't all match.</summary>
    private static void LootCheck(ConstructBuild.Blueprint bp, VehicleReport r)
    {
        if (bp.Loot.Count == 0)
            return;
        foreach (var p in ConstructLoot.Problems)
            r.Failures.Add(p);
        var hauls = new List<string>();
        var counts = new List<int>();
        for (var k = 0; k < 6; k++)
        {
            var w = new ConstructWorld();
            w.NextLootSeed((uint)(k * 7919 + 1));
            var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out _);
            if (c == null)
                return;
            var names = new List<string>();
            for (var i = 0; i < c.PartCount; i++)
                if (w.StoreOf(c, i) is { } st)
                    for (var s = 0; s < st.SlotCount; s++)
                        if (st.Def[s] != ConstructParts.None)
                            names.Add($"{ConstructParts.Get(st.Def[s])?.Name} x{st.Count[s]}");
            names.Sort(StringComparer.Ordinal);
            hauls.Add(string.Join(", ", names));
            counts.Add(names.Count);
        }
        r.Notes.Add($"loot: {bp.Loot.Count} containers; {string.Join(", ", counts)} stacks over six spawns; first: {hauls[0]}");
        if (counts.All(n => n == 0))
            r.Failures.Add("loot: six spawns rolled nothing");
        if (hauls.Distinct().Count() < 2)
            r.Failures.Add("loot: six spawns all rolled the same");
    }

    /// <summary>A room: a player fits, walks it, stands on its floor, reaches its seats and containers, is held by the door shut and goes out by it open, with the real mover and the real collision.</summary>
    /// <summary>
    /// Rays out of a room through what's drawn, along the floor's length at three heights each way and up through the roof: any that leave the body is a hole nobody planned. A shut door counts as wall.
    /// Two rays side by side (a hole over 25 cm) fail; one alone is a seam.
    /// </summary>
    private static void Gaps(Construct c, Room room, VehicleReport r)
    {
        var tris = VehiclePlan.Triangles(c);
        var offset = room.FloorRest;
        const float step = 0.25f, reach = 3.5f;
        var holes = new List<Vector3>();
        for (var z = room.R.Z0 + 0.35f; z < room.R.Z1 - 0.3f; z += step)
        {
            var (run, last) = (0, Vector3.Zero);
            foreach (var y in (float[])[0.5f, 1.1f, 1.6f])
                foreach (var side in (float[])[-1, 1])
                {
                    var from = new Vector3(0, room.FloorTop + y, z) + offset;
                    if (!VehiclePlan.Hits(tris, from, new Vector3(side, 0, 0), reach))
                        holes.Add(from + new Vector3(side, 0, 0));
                }
            if (!VehiclePlan.Hits(tris, new Vector3(0, room.FloorTop + 1, z) + offset, Vector3.UnitY, room.Upper != null ? reach + 3 : reach)) // (phase 5: up through a hatch, to the storey's roof)
                holes.Add(new Vector3(0, room.Ceiling, z) + offset);
            _ = run;
            _ = last;
        }
        foreach (var way in (float[])[-1, 1])
            for (var x = -0.6f; x <= 0.6f; x += step)
                foreach (var y in (float[])[0.5f, 1.1f])
                {
                    var from = new Vector3(x, room.FloorTop + y, room.Middle.Z) + offset;
                    if (!VehiclePlan.Hits(tris, from, new Vector3(0, 0, way), (room.R.Z1 - room.R.Z0) * 0.5f + 1))
                        holes.Add(from + new Vector3(0, 0, way * 2));
                }
        var together = holes.Where(h => holes.Any(o => o != h && Vector3.Distance(o, h) < 0.8f)).ToList();
        if (together.Count > 0)
            r.Failures.Add($"the {room.Tag}: {together.Count} rays leave it through a hole, near {together[0] - offset}");
        r.Notes.Add($"the {room.Tag}: {holes.Count} rays out of {room.Tag} leave it");
    }

    /// <summary>
    /// Phase 5: a room whose walls are bars (or an open deck's bulwark) holds a player in: a standing player's body (from just over a step's height to the head) moved
    /// out from <paramref name="start"/> to either side and either end meets something solid before it is past the room's walls. A body, not the walker: the checks'
    /// pinned build is stepped up onto from beside any of its tops, a wall's stacked slabs too, which a rider on a vehicle never is.
    /// </summary>
    private static void Contained(Construct c, Room room, Vector3 start, VehicleReport r)
    {
        foreach (var way in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
        {
            if (Vector3.Dot(way, room.DoorFace) > 0.5f && !room.Caged)
                continue; // (an upper storey's way down is its hatch)
            if (Vector3.Dot(way, room.DoorFace) > 0.5f && room.Caged && MathF.Abs(start.Z - room.DoorMid.Z) < 1f)
                continue; // (out through its doorway: the door's own check)
            var d = Clear(c, start + room.FloorRest, way, MathF.Max(4f, room.R.Z1 - room.R.Z0 + 1.5f));
            var end = start + way * d;
            if (MathF.Abs(end.X) > room.R.X1 + 0.6f || end.Z < room.R.Z0 - 0.6f || end.Z > room.R.Z1 + 0.6f)
            {
                r.Failures.Add($"the {room.Tag}: a player moving {Show(way)} from {Show(start)} gets out, to {Show(end)}");
                return;
            }
        }
        r.Notes.Add($"the {room.Tag}: {(room.Caged ? "caged" : room.Open ? "open" : "enclosed")}, a player is held in each way");
    }

    /// <summary>
    /// Walks a body up a room's stair to its upper storey: along the stair's line from 0.45 m before its foot to 0.35 m past its top, the feet on the highest solid top
    /// under them (within 0.15 m of the line, 5 cm ahead) that is a step or less up, held where a top more than a step up stands in the way or the body (0.36 m round, from just
    /// over a step to the head) meets anything. The height reached (vehicle coordinates) and where it was held.
    /// </summary>
    private static (float Reached, Vector3 Stuck, string By) Climb(Construct c, Room below, Room up)
    {
        var way = below.StairWay;
        var from = below.StairFoot;
        var to = up.DoorMid + way * 0.35f;
        var len = Vector3.Dot(to - from, way);
        var feet = from.Y;
        for (var d = 0f; d <= len; d += 0.05f)
        {
            var p = from + way * d;
            var rest = new Vector3(p.X, 0, p.Z) + below.FloorRest;
            var top = float.MinValue;
            var wall = false;
            for (var i = 0; i < c.PartCount; i++)
            {
                c.RestPose(c.Parts[i].Seg, out var q, out var t);
                for (var k = 0; k < c.DefOf(i).SolidBoxes; k++)
                {
                    c.BoxOf(i, k, out var bc, out var bh);
                    bc = t + Vector3.Transform(bc, q);
                    if (MathF.Abs(rest.X - bc.X) > bh.X + 0.15f)
                        continue;
                    var along = (bc.Z - rest.Z) * way.Z; // (how far ahead of the feet the box's middle is)
                    if (along - bh.Z > 0.05f || along + bh.Z < -0.35f)
                        continue; // (under the feet: a disc across, from a body's half behind to a step's look ahead)
                    var (lo, hi) = (bc.Y - bh.Y - below.FloorRest.Y, bc.Y + bh.Y - below.FloorRest.Y);
                    if (hi <= feet + CallOfSlop.World.StepHeight + 1e-3f)
                        top = MathF.Max(top, hi);
                    if (along + bh.Z < -0.05f)
                        continue; // (behind: it holds the feet up, it isn't in the way)
                    if (hi > feet + CallOfSlop.World.StepHeight + 1e-3f && lo < feet + CallOfSlop.World.PlayerHeight)
                    {
                        wall = true;
                        _blocker = $"{c.DefOf(i).Name[..Math.Min(24, c.DefOf(i).Name.Length)]}... from {lo - feet:0.00} to {hi - feet:0.00} m over the feet";
                    }
                }
            }
            if (wall)
                return (feet, p with { Y = feet }, $"a step too high: {_blocker}");
            if (top > float.MinValue)
                feet = top;
            foreach (var h in (float[])[0.75f, 1.1f, 1.45f])
                if (Hit(c, new Vector3(p.X, feet + h, p.Z) + below.FloorRest, 0.36f, below.Stair!.Index, feet + below.FloorRest.Y + CallOfSlop.World.StepHeight)) // (the stair's own treads, and any top a step up, are walked onto, not into)
                    return (feet, p with { Y = feet }, $"the body {h:0.00} m up meets {_blocker}");
        }
        return (feet, to with { Y = feet }, "");
    }

    private static string _blocker = "";


    /// <summary>How far a standing player (spheres of 0.38 m from 0.7 m over the feet to the head) goes from <paramref name="feet"/> (rest frame) along <paramref name="way"/> before touching a solid box, up to <paramref name="reach"/>.</summary>
    private static float Clear(Construct c, Vector3 feet, Vector3 way, float reach)
    {
        const float r = 0.38f;
        for (var d = 0f; d <= reach; d += 0.05f)
            foreach (var h in (float[])[0.7f, 1.0f, 1.3f, 1.6f])
                if (Hit(c, feet + way * d + new Vector3(0, h, 0), r))
                    return d;
        return reach;
    }

    /// <summary>Does a sphere at <paramref name="at"/> (rest frame) touch any solid box of the build?</summary>
    private static bool Hit(Construct c, Vector3 at, float r, int skip = -1, float stepTop = float.MinValue)
    {
                for (var i = 0; i < c.PartCount; i++)
                {
                    if (i == skip)
                        continue;
                    c.PartBounds(i, out var mn, out var mx);
                    if (c.Parts[i].Seg == 0 && at.X < mn.X - r - 0.5f || at.X > mx.X + r + 0.5f || at.Y < mn.Y - r - 0.5f || at.Y > mx.Y + r + 0.5f || at.Z < mn.Z - r - 0.5f || at.Z > mx.Z + r + 0.5f)
                        continue;
                    c.RestPose(c.Parts[i].Seg, out var q, out var t);
                    for (var k = 0; k < c.DefOf(i).SolidBoxes; k++)
                    {
                        c.BoxOf(i, k, out var bc, out var bh);
                        bc = t + Vector3.Transform(bc, q);
                        if (q != Quaternion.Identity)
                        {
                            var m = Matrix4x4.CreateFromQuaternion(q);
                            bh = new Vector3(MathF.Abs(m.M11) * bh.X + MathF.Abs(m.M21) * bh.Y + MathF.Abs(m.M31) * bh.Z, MathF.Abs(m.M12) * bh.X + MathF.Abs(m.M22) * bh.Y + MathF.Abs(m.M32) * bh.Z,
                                MathF.Abs(m.M13) * bh.X + MathF.Abs(m.M23) * bh.Y + MathF.Abs(m.M33) * bh.Z);
                        }
                        var gap = Vector3.Max(Vector3.Abs(at - bc) - bh, Vector3.Zero);
                        if (gap.LengthSquared() < r * r && bc.Y + bh.Y > stepTop)
                        {
                            _blocker = $"{c.DefOf(i).Name[..Math.Min(24, c.DefOf(i).Name.Length)]}... at {Show(bc - (at with { Y = 0 }))} of it, {Show(bh)} half";
                            return true;
                        }
                    }
                }
        return false;
    }

    private static string Show(Vector3 v) => FormattableString.Invariant($"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})");

    /// <summary>
    /// Phase 5: the storey over a room. A player stands on its floor (away from the hatch), has the headroom under an enclosed one, walks it, is held in by its walls or
    /// bulwark, finds no hole in an enclosed one (the rays, as a room's), and walks up the stair from the room below onto it.
    /// </summary>
    private static void Upper(ConstructWorld w, Construct c, Room below, VehicleReport r)
    {
        var up = below.Upper!;
        Vector3 World(Vector3 v) => c.ToWorld(v + up.FloorRest);
        var z = up.R.Z0 + 0.5f;
        for (var t = up.R.Z0 + 0.5f; t <= up.R.Z1 - 0.5f; t += 0.25f)
            if (MathF.Abs(t - up.DoorMid.Z) > MathF.Abs(z - up.DoorMid.Z))
                z = t; // (as far from the hatch as the floor goes)
        var mid = new Vector3(0, up.FloorTop, z);
        var start = World(mid + new Vector3(0, 0.15f, 0));
        var settle = SelfTest.BuildingWalk(w, start, _ => Vector3.Zero, 40, CallOfSlop.World.PlayerHeight, out _, 0);
        var stood = settle.Position.Y - World(mid).Y;
        for (var t = up.R.Z0 + 0.6f; MathF.Abs(stood) > 0.2f && t <= up.R.Z1 - 0.6f; t += 0.25f)
        {
            // (that end of the floor has a step a player stands on, a bulwark's or a gun's: along it to where one stands on the floor, away from the hatch)
            if (MathF.Abs(t - up.DoorMid.Z) < 0.9f)
                continue;
            mid = new Vector3(0, up.FloorTop, t);
            start = World(mid + new Vector3(0, 0.15f, 0));
            settle = SelfTest.BuildingWalk(w, start, _ => Vector3.Zero, 40, CallOfSlop.World.PlayerHeight, out _, 0);
            stood = settle.Position.Y - World(mid).Y;
        }
        if (MathF.Abs(stood) > 0.2f)
            r.Failures.Add($"the {up.Tag}: a player stands {stood:0.00} m off its floor (at {Show(c.ToLocal(settle.Position) - up.FloorRest)}, floor {up.FloorTop:0.00}, from {Show(mid)}{(Hit(c, c.ToLocal(settle.Position) + new Vector3(0, -0.05f, 0), 0.4f) ? ": on " + _blocker : "")})");
        var head = up.Ceiling - up.FloorTop;
        if (!up.Open && head < CallOfSlop.World.PlayerHeight + 0.1f)
            r.Failures.Add($"the {up.Tag}: {head:0.00} m of headroom: a player is {CallOfSlop.World.PlayerHeight:0.00} m");
        var walked = 0f;
        foreach (var way in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX })
        {
            var far = SelfTest.BuildingWalk(w, start, _ => way, 120, CallOfSlop.World.PlayerHeight, out _);
            walked = MathF.Max(walked, Vector3.Distance(far.Position with { Y = 0 }, start with { Y = 0 }));
        }
        if (walked < 0.4f)
            r.Failures.Add($"the {up.Tag}: a player can't walk on it: {walked:0.00} m whichever way");
        Contained(c, up, mid, r);
        if (!up.Open)
            Gaps(c, up, r);
        // The way up: from the room's floor in front of the stair, up its treads and through the hatch, onto the storey's floor, a body moved along the stair's line
        // (the pinned build's walker steps onto any top from beside it, walls too, which a rider in a vehicle doesn't: Contained).
        var (got, stuck, by) = Climb(c, below, up);
        if (got < up.FloorTop - 0.05f)
            r.Failures.Add($"the {up.Tag}: a player can't walk up the stair to it (got {got - below.FloorTop:0.00} m up of {up.FloorTop - below.FloorTop:0.00}, held at {Show(stuck)}: {by})");
        r.Notes.Add($"the {up.Tag}: {(up.Open ? "an open deck" : $"{head:0.00} m headroom")}, walked {walked:0.0} m{(got < up.FloorTop - 0.05f ? "" : ", the stair walked up to it")}");
    }

    /// <summary>
    /// Phase 6: a room's balcony. A player stands on its deck, walks out onto it from the room through the door (open), and is held in by its rail outward and toward
    /// its closed end (the open end is its ladder's way).
    /// </summary>
    private static void Balcony(ConstructWorld w, Construct c, Room room, (VPart Part, Vector3 Mid, Vector3 Out, Vector3 Closed, float Depth, float Half) b, VehicleReport r)
    {
        Vector3 World(Vector3 v) => c.ToWorld(v + room.FloorRest);
        var settle = SelfTest.BuildingWalk(w, World(b.Mid + new Vector3(0, 0.15f, 0)), _ => Vector3.Zero, 14, CallOfSlop.World.PlayerHeight, out _, 0); // (a few ticks: once it rides the deck, the walker's ride step walks it forward, off an open end)
        Vector3 Here(in MoveState m) => (m.RideUp != Vector3.Zero ? m.Position : c.ToLocal(m.Position)) - room.FloorRest; // (a rider's place is in the body's rest frame)
        var stood = Here(settle).Y - b.Mid.Y;
        if (MathF.Abs(stood) > 0.2f)
            r.Failures.Add($"the {room.Tag}'s balcony: a player stands {stood:0.00} m off its deck (at {Show(Here(settle))}, from {Show(b.Mid)}{(Hit(c, b.Mid + room.FloorRest + new Vector3(0, 1.2f, 0), 0.4f) || Hit(c, b.Mid + room.FloorRest + new Vector3(0, 0.6f, 0), 0.4f) || Hit(c, b.Mid + room.FloorRest + new Vector3(0, 1.6f, 0), 0.4f) ? ": over it, " + _blocker : "")})");
        // Out of the room onto it: from inside the doorway toward the balcony's middle, the walker's own steps.
        var inside = room.DoorMid - room.DoorFace * 0.7f;
        var way = Vector3.Normalize((b.Mid - inside) with { Y = 0 });
        // (phase 7c: stopping once on the deck: walking on, 90 ticks took a player past the middle, along the rail and off the open end, its ladder's way: Bandit
        // Box Truck 3 since the turret seats' merge)
        bool OnDeck(MoveState m) { var h = Here(m); return Vector3.Dot(h - room.DoorMid, b.Out) >= 0.3f && MathF.Abs(h.Y - room.FloorTop) <= 0.1f; }
        var s = SelfTest.BuildingWalk(w, World(inside + new Vector3(0, 0.15f, 0)), _ => way, 90, CallOfSlop.World.PlayerHeight, out _, until: OnDeck);
        var got = Here(s);
        var outAt = Vector3.Dot(got - room.DoorMid, b.Out);
        if (outAt < 0.2f || MathF.Abs(got.Y - room.FloorTop) > 0.2f)
            r.Failures.Add($"the {room.Tag}'s balcony: a player can't walk out onto it from the room (got to {Show(got)})");
        foreach (var dir in new[] { b.Out, b.Closed })
        {
            var d = Clear(c, b.Mid + room.FloorRest, dir, 3f);
            var reach = Vector3.Dot(dir, b.Out) > 0.5f ? b.Depth / 2 : b.Half;
            if (d > reach + 0.6f)
                r.Failures.Add($"the {room.Tag}'s balcony: a player moving {Show(dir)} from its middle gets off it, {d:0.00} m");
        }
        r.Notes.Add($"the {room.Tag}'s balcony: stood on, walked out onto, railed (out {outAt:0.00} m past the door)");
    }

    private static void Interior(ConstructBuild.Blueprint bp, Room room, VehicleReport r)
    {
        const float y0 = SelfTest.BuildingY;
        var w = new ConstructWorld();
        var c = ConstructBuild.Build(w, bp, new Vector3(0, y0 + 2, 0), Quaternion.Identity, out _);
        if (c == null)
            return;
        c.Pinned = true;
        c.Rebuild();
        var offset = room.FloorRest;
        Vector3 World(Vector3 v) => c.ToWorld(v + offset);
        var head = room.Ceiling - room.FloorTop;
        if (head < CallOfSlop.World.PlayerHeight + 0.1f)
            r.Failures.Add($"the {room.Tag}: {head:0.00} m of headroom: a player is {CallOfSlop.World.PlayerHeight:0.00} m");
        var middle = room.Middle;
        var start = World(middle + new Vector3(0, 0.15f, 0));
        var inward = -room.DoorFace;
        // Stand at the middle, then walk towards the far wall.
        var settle = SelfTest.BuildingWalk(w, start, _ => Vector3.Zero, 40, CallOfSlop.World.PlayerHeight, out _, 0);
        var stood = settle.Position.Y - World(middle).Y;
        for (var step = 1; room.Stair != null && MathF.Abs(stood) > 0.2f && step < 80; step++)
        {
            // (phase 5: a stair in the middle of the room, which a player standing there walks onto: the nearest spot along the middle where one stands on the floor;
            // then, when seats and furniture fill the middle's ends, the same along the aisle a body's width off the middle, away from the stair, beside it too)
            var k = step < 40 ? step : step - 39;
            var z = room.Middle.Z + (k % 2 == 0 ? 1 : -1) * 0.125f * ((k + 1) / 2);
            if (z < room.R.Z0 + 0.5f || z > room.R.Z1 - 0.5f)
                continue;
            if (step < 40 && room.Upper is { } su && z > MathF.Min(room.StairFoot.Z, su.DoorMid.Z) - 0.6f && z < MathF.Max(room.StairFoot.Z, su.DoorMid.Z) + 0.6f)
                continue; // (not beside the stair: a body there is pushed out of its blocks)
            var x = step < 40 ? 0f : -MathF.Sign(room.StairFoot.X - room.Middle.X) * 0.3f;
            middle = room.Middle with { X = room.Middle.X + x, Z = z };
            start = World(middle + new Vector3(0, 0.15f, 0));
            settle = SelfTest.BuildingWalk(w, start, _ => Vector3.Zero, 40, CallOfSlop.World.PlayerHeight, out _, 0);
            stood = settle.Position.Y - World(middle).Y;
        }
        var walked = 0f;
        foreach (var way in new[] { inward, Vector3.UnitZ, -Vector3.UnitZ }) // (furniture may stand across one way; a room has to let a player down it somewhere)
        {
            var far = SelfTest.BuildingWalk(w, start, _ => way, 120, CallOfSlop.World.PlayerHeight, out _);
            walked = MathF.Max(walked, Vector3.Distance(far.Position with { Y = 0 }, start with { Y = 0 }));
        }
        if (walked < 0.4f)
            r.Failures.Add($"the {room.Tag}: a player can't walk in it: {walked:0.00} m from its middle, whichever way");
        if (MathF.Abs(stood) > 0.2f)
            r.Failures.Add($"the {room.Tag}: a player stands {stood:0.00} m off its floor (at {settle.Position - c.Pos}, floor {room.FloorTop + offset.Y:0.00}{(room.Stair != null && Hit(c, c.ToLocal(settle.Position) + new Vector3(0, -0.05f, 0), 0.4f) ? ": on " + _blocker : "")})");
        if (room.Caged)
            Contained(c, room, middle, r); // (phase 5: bars seen and shot through by design: a player is held in, not rays)
        else
            Gaps(c, room, r);
        if (room.Upper != null)
            Upper(w, c, room, r);
        var toDoor = Vector3.Normalize((room.DoorMid - room.Middle) with { Y = 0 });
        var doorAt = World(room.DoorMid);
        bool Out()
        {
            var inside = World(room.DoorMid - room.DoorFace * 0.7f + new Vector3(0, 0.15f, 0));
            var s = SelfTest.BuildingWalk(w, inside, _ => room.DoorFace, 90, CallOfSlop.World.PlayerHeight, out _);
            return Vector3.Dot(s.Position - doorAt, room.DoorFace) > 0.2f;
        }
        var shut = Out();
        var hinge = room.Hinge!.Index;
        var joint = c.JointOf(hinge);
        if (joint < 0)
        {
            r.Failures.Add($"the {room.Tag}: its door has no joint");
            return;
        }
        c.Joints[joint].Open = true;
        c.Wake();
        for (var t = 0; t < 150; t++)
            w.Step(new SelfTest.DriveWorld(), CallOfSlop.World.TickDt);
        var open = Out();
        if (!open && !shut) // (a player walking into a door leaf swings it, so a door shut needn't stop them)
            r.Failures.Add($"the {room.Tag}: a player can't get out by its door");
        if (room.Balcony is { } balcony)
            Balcony(w, c, room, balcony, r);
        if (room.Containers.Count == 0)
            r.Failures.Add($"the {room.Tag}: nothing to loot: no wall took a container");
        var reach = 0f;
        foreach (var p in room.Containers.Concat(room.Seats))
        {
            c.PartBounds(p.Index, out var mn, out var mx);
            var d = new Vector2(((mn + mx) * 0.5f).X - (room.Middle + offset).X, 0).Length();
            reach = MathF.Max(reach, d);
        }
        if (reach > ConstructBuild.UseReach)
            r.Failures.Add($"the {room.Tag}: a seat or a store is {reach:0.0} m from the aisle: reach is {ConstructBuild.UseReach:0.0}");
        r.Notes.Add($"the {room.Tag}: {head:0.00} m headroom, walked {walked:0.0} m, door works, {room.Containers.Count} stores, {room.Seats.Count} seats within {reach:0.0} m of the aisle; door sill {room.FloorTop - c.LocalMin.Y + offset.Y:0.00} m up, {(room.Ladder ? "a ladder under it" : "no ladder")}");
    }

    /// <summary>The gunner's look points the gun: round to each side and behind, and up, with the joints it has.</summary>
    private static void Aim(Construct c, ConstructWorld w, IWorldQuery q, int gun, int seat, string name, VehicleReport r)
    {
        var worst = 0f;
        var worstAim = 0f;
        foreach (var (yaw, pitch) in new[] { (MathF.PI / 2, 0f), (-MathF.PI / 2, 0f), (3f, 0f), (0f, 0.25f) })
        {
            for (var t = 0; t < 240; t++)
            {
                c.Parts[seat].Keys = CPart.Occupied;
                c.Parts[seat].AimYaw = yaw;
                c.Parts[seat].AimPitch = pitch;
                w.Step(q, World.TickDt);
            }
            ConstructWeapons.MuzzleWorld(c, gun, out var muzzle, out var fwd);
            fwd = Vector3.Normalize(fwd);
            worst = MathF.Max(worst, MathF.Acos(Math.Clamp(Vector3.Dot(fwd, World.Forward(yaw, pitch)), -1, 1)) * 180 / MathF.PI);
            // What the game points it at: the point its gunner looks at, within its reach (the guns converge on it, ConstructWeapons.GunnerPoint), so a short gun
            // set off its gunner's eye stands off the look's line by design; it is judged by how far it is off that point.
            var point = ConstructWeapons.GunnerPoint(w, c, seat, q, c.Guns[gun].MaxRange);
            var at = point - muzzle;
            ref var cg = ref c.Guns[gun];
            if ((cg.Effects & GunEffect.Shell) == 0 ? cg.RoundV > 0 && ConstructShells.Lead(muzzle, point, cg.RoundV, false, out var lead) : c.DefOf(cg.Part).GunThrust <= 0 && ConstructShells.Lead(muzzle, point, cg.Speed, false, out lead))
                at = lead; // (as the game lays it, ConstructWeapons.AimWay: a slow round is aimed up for its drop)
            if (at.LengthSquared() > 1e-4f)
                worstAim = MathF.Max(worstAim, MathF.Acos(Math.Clamp(Vector3.Dot(fwd, Vector3.Normalize(at)), -1, 1)) * 180 / MathF.PI);
        }
        r.Notes.Add($"the {name} follows the gunner's look within {worst:0.0} degrees (sides, behind, up), {worstAim:0.0} off the point it is laid on");
        if (worstAim > 12 || worst > 30) // (the point it's laid on is the measure: a short gun converges close by and a slow round is aimed up for its drop, so either stands off its gunner's line by design)
            r.Failures.Add($"the {name} is {MathF.Max(worstAim, worst):0} degrees off where its gunner looks: its turntable or hinge doesn't point it");
    }

    /// <summary>How far a turret's gunner's seat may face off its gun's line at rest, in degrees (a sideways seat on a turret arm was a tester's report).</summary>
    public const float SeatGunLimit = 15;

    /// <summary>
    /// How many degrees gun <paramref name="g"/>'s seat faces off the gun's own line at rest, both read level in the body's rest frame: 0 when the gun has no seat
    /// or points straight up or down at rest.
    /// </summary>
    public static float SeatOffGun(Construct c, int g) =>
        c.Guns[g].Seat < 0 || c.Guns[g].Seat >= c.PartCount ? 0 : SeatOffGun(c, c.Guns[g].Part, c.Guns[g].Seat);

    /// <summary>Each seated gun's <see cref="SeatOffGun(Construct, int)"/> on blueprint <paramref name="text"/> as built (for the reports' counts).</summary>
    public static List<float> SeatsOffGuns(string text)
    {
        var w = new ConstructWorld();
        var bp = ConstructBuild.ParseBlueprint(text, "seats.json");
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out _, TowOf(bp));
        var offs = new List<float>();
        if (c == null)
            return offs;
        if (c.Dirty)
            c.Rebuild();
        for (var g = 0; g < c.GunCount; g++)
            if (c.Guns[g].Seat >= 0)
                offs.Add(SeatOffGun(c, g));
        return offs;
    }

    /// <summary>The same for a receiver part and a seat part (the gun points along its receiver's -Z, ConstructWeapons; a seat faces its -Z, Construct.SeatForward).</summary>
    public static float SeatOffGun(Construct c, int receiver, int seat)
    {
        var fg = c.DirToRest(c.Parts[receiver].Seg, Rot24.Apply(c.Parts[receiver].Rot, -Vector3.UnitZ)) with { Y = 0 };
        var fs = c.DirToRest(c.Parts[seat].Seg, Rot24.Apply(c.Parts[seat].Rot, -Vector3.UnitZ)) with { Y = 0 };
        if (fg.LengthSquared() < 0.01f || fs.LengthSquared() < 0.01f)
            return 0;
        return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(fg), Vector3.Normalize(fs)), -1, 1)) * 180 / MathF.PI;
    }

    /// <summary>Every seat with a Turret Module fires its guns: a sitter on Fire for a second after the first load.</summary>
    private static void Guns(ConstructBuild.Blueprint bp, VehicleReport r)
    {
        var q = new SelfTest.DriveWorld();
        var w = new ConstructWorld();
        if (Fresh(bp, w, q) is not { } c)
            return;
        r.Notes.Add($"guns: {c.GunCount}, seats {string.Join(", ", Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Role == PartRole.Seat).Select(i => c.SeatKindOf(i).ToString()))}");
        for (var g = 0; g < c.GunCount; g++)
        {
            ref var gun = ref c.Guns[g];
            var name = c.DefOf(gun.Part).Name;
            if (gun.Problem.Length > 0)
                r.Failures.Add($"the {name} can't fire: {gun.Problem}");
            if (gun.Seat < 0)
            {
                if (c.RoutesInto(gun.Part, RouteType.Data) == 0)
                    r.Failures.Add($"the {name} has no gunner's seat and no wire into it: nothing fires it");
                else
                    r.Notes.Add($"the {name} fires on its wires (not tried here)");
                continue;
            }
            if (gun.AimJoints == 0)
                r.Failures.Add($"the {name} has no joints to aim with");
            var flame = (gun.Effects & GunEffect.Flame) != 0; // (it drinks the construct's fuel)
            if (gun.BoxCount == 0 && gun.FeedRoute < 0 && gun.FeedSocket < 0 && !flame)
                r.Failures.Add($"the {name} has no ammo in reach");
            var seat = gun.Seat;
            if (SeatOffGun(c, g) is var off and > SeatGunLimit)
                r.Failures.Add($"the {name}'s gunner sits {off:0} degrees off the gun's line at rest: a turret's seat faces where its gun points");
            var ticks = gun.ReloadTicks + 10;
            for (var t = 0; t < ticks; t++)
            {
                c.Parts[seat].Keys = CPart.Occupied;
                w.Step(q, World.TickDt);
            }
            var before = w.ShotsFired;
            for (var t = 0; t < 60; t++)
            {
                c.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyFire;
                w.Step(q, World.TickDt);
            }
            var fired = w.ShotsFired - before;
            r.Shots += fired;
            r.Notes.Add($"the {name}: {fired} shots a second");
            if (fired == 0)
                r.Failures.Add($"Fire in the gunner's seat fired the {name} no shots in a second");
            Aim(c, w, q, g, seat, name, r);
            c.Parts[seat].Keys = 0;
        }
    }
}
