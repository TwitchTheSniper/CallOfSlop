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
    public static VehicleReport Check(string text, string file, VehicleCheckOptions? options = null, IReadOnlyList<Room>? rooms = null)
    {
        var o = options ?? new VehicleCheckOptions();
        var bp = ConstructBuild.ParseBlueprint(text, file);
        var r = new VehicleReport(bp.Name) { Steps = bp.Steps.Count, Wires = bp.Wires.Count + bp.Links.Count, Cargo = bp.Cargo.Count, Welds = bp.StepWeld.Count(w => w != null) };
        var q = new SelfTest.DriveWorld();
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out var why);
        if (c == null)
        {
            r.Failures.Add($"it doesn't build: {why}");
            return r;
        }
        Static(c, w, bp, r, o);
        ZFighting(c, r);
        var cargoBodies = w.Bodies.Count;
        Settle(w, q, 90);
        Rest(c, r);
        if (w.Bodies.Count != cargoBodies)
            r.Failures.Add($"it came apart standing still: {cargoBodies} bodies became {w.Bodies.Count}");
        Drive(bp, o, r);
        Turn(bp, o, r);
        Guns(bp, r);
        LootCheck(bp, r);
        foreach (var room in rooms ?? [])
            Interior(bp, room, r);
        return r;
    }

    private const float TinyFight = 6e-4f; // m2

    private static void Settle(ConstructWorld w, IWorldQuery q, int ticks)
    {
        for (var t = 0; t < ticks; t++)
            w.Step(q, World.TickDt);
    }

    private static void Static(Construct c, ConstructWorld w, ConstructBuild.Blueprint bp, VehicleReport r, VehicleCheckOptions o)
    {
        r.Parts = c.PartCount;
        r.Mass = c.Mass; // (with its cargo: a frozen volume adds what it carries)
        if (bp.Cargo.Count > 0 && c.CargoMass <= 0)
            r.Failures.Add($"its {bp.Cargo.Count} cargo isn't riding in a frozen Cargo Volume");
        if (c.PartCount > o.MaxParts)
            r.Failures.Add($"{c.PartCount} parts: over the limit of {o.MaxParts}");
        if (r.Mass < o.MinMass || r.Mass > o.MaxMass)
            r.Failures.Add($"{r.Mass:0} kg: outside {o.MinMass:0} to {o.MaxMass:0} kg");
        if (c.PartCount < bp.Steps.Count)
            r.Notes.Add($"{bp.Steps.Count - c.PartCount} step(s) became links (old fuel lines or shafts)");
        if (!c.HasPowertrain)
            r.Failures.Add($"no working powertrain: {c.DriveProblem}");
        if (c.DriveSeat < 0)
            r.Failures.Add("no seat drives it: link a seat through a Driving Module to the drivetrain");
        if (!c.CanDrive)
            r.Failures.Add($"it can't drive: {c.DriveProblem}");
        if (c.Engine >= 0 && (c.Fuel <= 0 || !ConstructRoutes.PipeFeeds(c, c.Engine)))
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
        {
            var d = c.DefOf(i);
            c.RestPose(c.Parts[i].Seg, out var q, out var t);
            var rot = q * Rot24.Quat(c.Parts[i].Rot);
            foreach (var b in ZFight.Boxes(d))
            {
                var centre = t + Vector3.Transform(c.Parts[i].Pos + Rot24.Apply(c.Parts[i].Rot, b.C), q);
                var look = c.Parts[i].Paint != 0 ? (int)c.Parts[i].Paint : 1 + d.Id;
                boxes.Add(ZFight.ZBox.Of($"{i}:{d.Name}", centre, b.H, rot) with { Look = look, Moving = d.Role is PartRole.Wheel or PartRole.Suspension || c.Parts[i].Seg != 0 && !c.Rigid(c.Parts[i].Seg) }); // (a wheel and what's on a joint never stay at rest)
            }
        }
        var all = ZFight.Find(boxes, sameLookOk: true).Where(h => boxes[h.A].Name != boxes[h.B].Name).ToList();
        var hits = all.Where(h => h.Area >= TinyFight).ToList(); // (sockets land on a 1 cm grid, so a quarter plate's edge can sit in a wall's end: a few cm2 of a plate's edge)
        r.Notes.Add($"coplanar faces between parts: {hits.Count} among {boxes.Count} boxes, and {all.Count - hits.Count} slivers");
        foreach (var h in hits.Take(8))
            r.Failures.Add($"z-fighting: {ZFight.Describe(boxes, h)}");
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
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out _);
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
        }
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
        if (w.Bodies.Count(x => x.PartCount > 0) > 1 + bp.Cargo.Count)
            r.Failures.Add("parts came off on the drive run");
        var left = w.Bodies.Count(b => b != c && Vector3.Distance(b.WorldCom, c.WorldCom) < 8);
        if (left < bp.Cargo.Count)
            r.Failures.Add($"{bp.Cargo.Count - left} of {bp.Cargo.Count} cargo fell off on the drive run");
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
        var start = World(room.Middle + new Vector3(0, 0.15f, 0));
        var inward = -room.DoorFace;
        // Stand at the middle, then walk towards the far wall.
        var settle = SelfTest.BuildingWalk(w, start, _ => Vector3.Zero, 40, CallOfSlop.World.PlayerHeight, out _, 0);
        var stood = settle.Position.Y - World(room.Middle).Y;
        var far = SelfTest.BuildingWalk(w, start, _ => inward, 120, CallOfSlop.World.PlayerHeight, out _);
        var walked = Vector3.Distance(far.Position with { Y = 0 }, start with { Y = 0 });
        if (walked < 0.4f)
            r.Failures.Add($"the {room.Tag}: a player can't walk in it: {walked:0.00} m from its middle towards the far wall");
        if (MathF.Abs(stood) > 0.2f)
            r.Failures.Add($"the {room.Tag}: a player stands {stood:0.00} m off its floor (at {settle.Position - c.Pos}, floor {room.FloorTop + offset.Y:0.00})");
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
        foreach (var (yaw, pitch) in new[] { (MathF.PI / 2, 0f), (-MathF.PI / 2, 0f), (3f, 0f), (0f, 0.25f) })
        {
            for (var t = 0; t < 240; t++)
            {
                c.Parts[seat].Keys = CPart.Occupied;
                c.Parts[seat].AimYaw = yaw;
                c.Parts[seat].AimPitch = pitch;
                w.Step(q, World.TickDt);
            }
            ConstructWeapons.MuzzleWorld(c, gun, out _, out var fwd);
            worst = MathF.Max(worst, MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(fwd), World.Forward(yaw, pitch)), -1, 1)) * 180 / MathF.PI);
        }
        r.Notes.Add($"the {name} follows the gunner's look within {worst:0.0} degrees (sides, behind, up)");
        if (worst > (c.Guns[gun].MaxRange < 30 ? 20 : 12)) // (a short gun converges close by, so it stands further off its gunner's line)
            r.Failures.Add($"the {name} is {worst:0} degrees off where its gunner looks: its turntable or hinge doesn't point it");
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
            if (gun.BoxCount == 0 && !flame)
                r.Failures.Add($"the {name} has no ammo in reach");
            var seat = gun.Seat;
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
