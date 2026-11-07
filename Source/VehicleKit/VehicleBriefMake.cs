using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>What the kit knows about a vehicle that its construct alone doesn't say (<see cref="VehicleBriefMake.Make"/>): who it is, its drive check, its loot.</summary>
public sealed class BriefFacts
{
    public string Name = "", Style = "", Shape = "", Role = "", Model = "";
    public int Tier, Kit;

    /// <summary>The style, for its weak point rules (null: the plain ones only: fuel, tyres, ammo, engine).</summary>
    public FactionStyle? StyleDef;

    /// <summary>The drive check's numbers (null: not run).</summary>
    public VehicleDriveCheck.Result? Drive;

    /// <summary>Containers that roll loot (part index, table), and loose cargo pieces.</summary>
    public List<(int Part, string Table)> Loot = new();

    public int Cargo;

    /// <summary>What its blueprint stows in slots (container part index, slot, part, count), and the loose cargo it carries (part, place in the rest frame).</summary>
    public List<(int Part, int Slot, string Name, int Count)> Stored = new();

    public List<(string Name, Vector3 At)> Loose = new();

    /// <summary>Its stowed parts and loose cargo, from a blueprint and the steps' parts on the body built from it.</summary>
    public void CargoOf(ConstructBuild.Blueprint bp, int[] partOf)
    {
        foreach (var (step, slot, name, count) in bp.Stored)
            if (step >= 0 && step < partOf.Length && partOf[step] >= 0)
                Stored.Add((partOf[step], slot, name, count));
        foreach (var spec in bp.Cargo)
            Loose.Add((spec.Part, spec.At));
    }
}

/// <summary>
/// Works out a <see cref="VehicleBrief"/> from a built construct (docs/vehiclekit/VEHICLEKIT.md, "Behaviour data"): its wheels, guns, joints, wires and parts in the rest
/// pose, and rays through what is drawn (the triangles <see cref="VehicleIdentity"/> shoots, <see cref="VehiclePlan.PartTriangles"/>). It reads only the rest frame, so a
/// construct that has been driven (the drive check's) or one standing in the world gives the same answer. It yields as the kit does (it runs in a generation's slices) and
/// allocates freely: it never runs on a tick. 20 to 110 ms for a big vehicle on the 2-core box.
/// </summary>
public static class VehicleBriefMake
{
    private const float Eye = 1.6f, WeakRange = 20f, GunReach = 80f;

    /// <summary>
    /// A generated vehicle's brief, from its blueprint text: the drive check's body when the plan kept was checked with the same steps (its name, notes and loot tables
    /// may have changed since: kit 4 notes the role after the check), else a check run here for its body and numbers.
    /// </summary>
    public static VehicleBrief? ForGenerated(Vehicle v, string text, string shape, ConstructBuild.Blueprint? parsed = null)
    {
        var bp = parsed ?? ConstructBuild.ParseBlueprint(text, v.Name + ".json");
        var r = v.DriveCheck is { Body: not null, Blueprint: { } checkedBp } kept && SameBuild(checkedBp, bp)
            ? kept // (the plan kept was checked with these very steps: its body, no second build)
            : VehicleDriveCheck.Run(text, v.Name + ".json", v.Context?.Gravity ?? 1, gate: false);
        if (r.Body is not { } body || r.PartOf is not { } partOf)
            return null;
        var f = new BriefFacts
        {
            Name = v.Name, Style = v.Style.Name, Shape = shape, StyleDef = v.Style, Kit = v.Kit,
            Role = v.Role?.Role.Name ?? "", Tier = v.Role?.Tier ?? 0, Model = v.Role?.Model?.Key ?? "",
            Drive = r, Cargo = bp.Cargo.Count,
        };
        foreach (var (step, table) in bp.Loot)
            if (step >= 0 && step < partOf.Length && partOf[step] >= 0)
                f.Loot.Add((partOf[step], table));
        f.CargoOf(bp, partOf);
        return Make(body, f);
    }

    /// <summary>Do two blueprints build the same body, part for part (their steps, places, welds, extra links, wires and links), whatever their names and notes say?</summary>
    public static bool SameBuild(ConstructBuild.Blueprint a, ConstructBuild.Blueprint b) =>
        a.Steps.SequenceEqual(b.Steps) && a.StepAt.SequenceEqual(b.StepAt) && a.StepWeld.SequenceEqual(b.StepWeld) && a.Wires.SequenceEqual(b.Wires)
        && a.StepAlso.Count == b.StepAlso.Count && a.Links.Count == b.Links.Count && a.Tracks.Count == b.Tracks.Count && a.Cargo.Count == b.Cargo.Count && a.ExactLinks == b.ExactLinks && a.Tow == b.Tow;

    /// <summary>
    /// A brief for any construct, from what it is (a committed seed's, an old save's, a player's truck): no role, no drive check, loot as the blueprint
    /// <paramref name="bp"/> names it when given. Runs at once on the calling thread: off the tick, or accept the cost (20 to 110 ms for a big vehicle).
    /// </summary>
    public static VehicleBrief Rebuild(Construct c, string name = "", FactionStyle? style = null)
    {
        var f = new BriefFacts { Name = name, Style = style?.Name ?? "", StyleDef = style }; // (which containers rolled loot isn't on a construct: none listed)
        return Make(c, f);
    }

    /// <summary>Works it out. <paramref name="c"/>'s parts are referenced by index; <see cref="VehicleBrief.Attach"/> gives them their tags once the real body is built.</summary>
    public static VehicleBrief Make(Construct c, BriefFacts f)
    {
        if (c.Dirty)
            c.Rebuild(); // (its caches: the wheels, guns, drive seat)
        var b = new VehicleBrief { Name = f.Name, Style = f.Style, Shape = f.Shape, Role = f.Role, Model = f.Model, Tier = (byte)Math.Clamp(f.Tier, 0, 255), Kit = (byte)Math.Clamp(f.Kit, 0, 255) };
        var g = new Geometry(c);
        VehicleGenJob.Yield();
        var fwd = new Vector3(c.DriveFwd.X, 0, c.DriveFwd.Z);
        b.Forward = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : -Vector3.UnitZ;
        g.Fwd = b.Forward;
        g.Right = Vector3.Cross(b.Forward, Vector3.UnitY);
        Driving(c, b, g, f);
        VehicleGenJob.Yield();
        Seats(c, b, g);
        Guns(c, b, g);
        WeakPoints(c, b, g, f);
        Cargo(c, b, g, f);
        return b;
    }

    // ---- Geometry: rays through what is drawn -----------------------------------------------------------------------------------

    private sealed class Geometry
    {
        public readonly Construct C;
        public readonly Vector3[] Tris;
        public readonly int[] Start, End;
        public readonly Vector3[] Min, Max;
        public readonly float Ground;
        public readonly Vector3 Lo, Hi;
        public Vector3 Fwd, Right;

        public Geometry(Construct c)
        {
            C = c;
            var (tris, owner) = VehiclePlan.PartTriangles(c);
            Tris = tris;
            var n = c.PartCount;
            (Start, End, Min, Max) = (new int[n], new int[n], new Vector3[n], new Vector3[n]);
            Array.Fill(Start, -1);
            for (var t = 0; t < owner.Length; t++)
            {
                if ((t & 4095) == 4095)
                    VehicleGenJob.Yield();
                var i = owner[t];
                if (i < 0 || i >= n)
                    continue;
                if (Start[i] < 0)
                {
                    Start[i] = t;
                    Min[i] = new Vector3(float.MaxValue);
                    Max[i] = new Vector3(float.MinValue);
                }
                End[i] = t + 1;
                for (var k = 0; k < 3; k++)
                {
                    Min[i] = Vector3.Min(Min[i], tris[t * 3 + k]);
                    Max[i] = Vector3.Max(Max[i], tris[t * 3 + k]);
                }
            }
            (Lo, Hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            foreach (var p in tris)
            {
                Lo = Vector3.Min(Lo, p);
                Hi = Vector3.Max(Hi, p);
            }
            if (tris.Length == 0)
                Lo = Hi = Vector3.Zero;
            var ground = float.MaxValue;
            for (var k = 0; k < c.WheelCount; k++)
                if (!c.Wheels[k].Station)
                    ground = MathF.Min(ground, c.Wheels[k].Centre.Y - c.Wheels[k].Travel * 0.5f - c.Wheels[k].Radius);
            Ground = ground == float.MaxValue ? Lo.Y : ground;
        }

        /// <summary>A part's middle in the rest frame (its drawn box's, else its origin).</summary>
        public Vector3 Mid(int i) => Start[i] >= 0 ? (Min[i] + Max[i]) * 0.5f : C.ToRest(C.Parts[i].Seg, C.Parts[i].Pos);

        /// <summary>The part the ray meets first within <paramref name="reach"/> (skipping <paramref name="skip"/>'s parts), or -1.</summary>
        public int FirstHit(Vector3 from, Vector3 way, float reach, bool[]? skip = null)
        {
            var best = reach;
            var who = -1;
            var inv = new Vector3(1 / (MathF.Abs(way.X) < 1e-9f ? 1e-9f : way.X), 1 / (MathF.Abs(way.Y) < 1e-9f ? 1e-9f : way.Y), 1 / (MathF.Abs(way.Z) < 1e-9f ? 1e-9f : way.Z));
            for (var i = 0; i < Start.Length; i++)
            {
                if (Start[i] < 0 || skip != null && skip[i])
                    continue;
                var t0 = (Min[i] - from) * inv;
                var t1 = (Max[i] - from) * inv;
                var lo = Vector3.Min(t0, t1);
                var hi = Vector3.Max(t0, t1);
                var enter = MathF.Max(MathF.Max(lo.X, lo.Y), lo.Z);
                var exit = MathF.Min(MathF.Min(hi.X, hi.Y), hi.Z);
                if (exit < 0 || enter > exit + 1e-4f || enter > best)
                    continue;
                for (var t = Start[i]; t < End[i]; t++)
                {
                    Vector3 a = Tris[t * 3], e1 = Tris[t * 3 + 1] - a, e2 = Tris[t * 3 + 2] - a;
                    var p = Vector3.Cross(way, e2);
                    var det = Vector3.Dot(e1, p);
                    if (MathF.Abs(det) < 1e-9f)
                        continue;
                    var s = from - a;
                    var u = Vector3.Dot(s, p) / det;
                    if (u < 0 || u > 1)
                        continue;
                    var q = Vector3.Cross(s, e1);
                    var v = Vector3.Dot(way, q) / det;
                    if (v < 0 || u + v > 1)
                        continue;
                    var d = Vector3.Dot(e2, q) / det;
                    if (d > 1e-4f && d < best)
                        (best, who) = (d, i);
                }
            }
            return who;
        }

        /// <summary>
        /// Where a part can be hit from: the bearings (of <see cref="VehicleBrief.WeakBins"/>) from which a standing player's eye <see cref="WeakRange"/> metres off
        /// hits it first, at its middle or one of five points round it, and whether a ray from straight above does (as <see cref="VehicleIdentity"/> judges its weak points).
        /// </summary>
        public (ushort Bearings, bool Top) Exposure(int part)
        {
            if (Start[part] < 0)
                return (0, false);
            var (min, max) = (Min[part], Max[part]);
            var mid = (min + max) * 0.5f;
            var half = (max - min) * 0.5f;
            ReadOnlySpan<Vector3> offs = [Vector3.Zero, new(half.X * 0.4f, 0, 0), new(-half.X * 0.4f, 0, 0), new(0, half.Y * 0.4f, 0), new(0, 0, half.Z * 0.4f), new(0, 0, -half.Z * 0.4f)];
            var centre = (Lo + Hi) * 0.5f;
            var reachOut = MathF.Max(WeakRange, new Vector2(Hi.X - Lo.X, Hi.Z - Lo.Z).Length() * 0.5f + 2);
            ushort mask = 0;
            for (var k = 0; k < VehicleBrief.WeakBins; k++)
            {
                var a = k * MathF.Tau / VehicleBrief.WeakBins;
                var flat = Fwd * MathF.Cos(a) + Right * MathF.Sin(a);
                var eye = new Vector3(centre.X + flat.X * reachOut, Ground + Eye, centre.Z + flat.Z * reachOut);
                foreach (var off in offs)
                {
                    var to = mid + off - eye;
                    var len = to.Length();
                    var hit = len > 1e-3f && FirstHit(eye, to / len, len + 1) == part;
                    VehicleGenJob.Yield(); // (a ray through a big body is 0.1 to 0.5 ms: a slice may end after any)
                    if (hit)
                    {
                        mask |= (ushort)(1 << k);
                        break;
                    }
                }
            }
            var top = false;
            var above = Hi.Y + 2;
            foreach (var off in offs)
            {
                var hit = FirstHit(new Vector3(mid.X + off.X, above, mid.Z + off.Z), -Vector3.UnitY, above - Lo.Y + 1) == part;
                VehicleGenJob.Yield();
                if (hit)
                {
                    top = true;
                    break;
                }
            }
            return (mask, top);
        }

        /// <summary>A bearing (degrees, 0 ahead, clockwise) of a direction in the body frame.</summary>
        public float Bearing(Vector3 d)
        {
            var a = MathF.Atan2(Vector3.Dot(d, Right), Vector3.Dot(d, Fwd)) * 180 / MathF.PI;
            return a < 0 ? a + 360 : a;
        }
    }

    private static BriefRef Ref(Geometry g, int part) => BriefRef.Of(g.C, part, g.Mid(part));

    private static BriefRef Ref(Construct c, int part, Vector3 at) => BriefRef.Of(c, part, at);

    // ---- Driving --------------------------------------------------------------------------------------------------------------

    private static void Driving(Construct c, VehicleBrief b, Geometry g, BriefFacts f)
    {
        var e = g.Hi - g.Lo;
        b.Length = MathF.Abs(Vector3.Dot(e, new Vector3(MathF.Abs(g.Fwd.X), 0, MathF.Abs(g.Fwd.Z))));
        b.Width = MathF.Abs(Vector3.Dot(e, new Vector3(MathF.Abs(g.Right.X), 0, MathF.Abs(g.Right.Z))));
        b.Height = e.Y;
        b.Mass = c.Mass;
        float power = 0, burn = 0, fuel = 0;
        var electric = false;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.Role == PartRole.Engine)
            {
                power += d.EngineTorque * d.PeakRpm * MathF.Tau / 60f;
                if (d.FuelKind == EngineFuel.Electric)
                    electric = true;
                else if (d.FuelKind == EngineFuel.Fuel)
                    burn += d.BurnRate;
            }
            if (d.Role == PartRole.FuelTank)
                fuel += d.FuelCapacity;
        }
        b.TopSpeed = VehicleRoles.Speed(power, c.Mass, b.Width * b.Height);
        if (f.Drive is { Built: true } r && (r.Ok || r.Moved != 0))
        {
            b.Measured = true;
            b.PulseMoved = r.Moved;
            b.PulseSpeed = r.Speed;
            var t = VehicleDriveCheck.PulseTicks / (float)World.TickRate;
            b.Accel = 2 * MathF.Max(0, r.Moved) / (t * t);
        }
        // Wheels: the wheelbase and track over the ground's wheels, the steered ones' lock.
        float aMin = float.MaxValue, aMax = float.MinValue, sMin = float.MaxValue, sMax = float.MinValue, lockRad = float.MaxValue;
        float steerAlong = 0, fixedAlong = 0;
        int steered = 0, fixedN = 0, wheels = 0, driven = 0;
        var skid = false;
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            if (w.Station || w.Lead)
                continue;
            wheels++;
            if (w.Driven)
                driven++;
            skid |= w.SideDrive;
            var along = Vector3.Dot(w.Centre, g.Fwd);
            var across = Vector3.Dot(w.Centre, g.Right);
            (aMin, aMax, sMin, sMax) = (MathF.Min(aMin, along), MathF.Max(aMax, along), MathF.Min(sMin, across), MathF.Max(sMax, across));
            var s = w.Seg == 0 ? 0 : c.MoveSeg(w.Seg);
            if (s > 0 && c.Joints[s - 1].Type == JointType.Steering)
            {
                ref var j = ref c.Joints[s - 1];
                lockRad = MathF.Min(lockRad, MathF.Min(MathF.Min(1, MathF.Abs(j.Max)), MathF.Min(1, MathF.Abs(j.Min)))); // (the driver's full lock is a radian, within the pivot's limits)
                steerAlong += along;
                steered++;
            }
            else
            {
                fixedAlong += along;
                fixedN++;
            }
        }
        b.Wheels = (byte)Math.Min(wheels, 255);
        b.Driven = (byte)Math.Min(driven, 255);
        b.Wheelbase = wheels > 0 ? aMax - aMin : 0;
        b.Track = wheels > 0 ? sMax - sMin : 0;
        if (steered > 0 && lockRad is > 0.01f and < float.MaxValue)
        {
            b.Steer = BriefSteer.Wheels;
            b.MaxSteer = lockRad * 180 / MathF.PI;
            var wb = fixedN > 0 ? MathF.Abs(steerAlong / steered - fixedAlong / fixedN) : b.Wheelbase;
            b.TurnRadius = wb / MathF.Tan(lockRad) + b.Track * 0.5f;
        }
        else if (skid)
            b.Steer = BriefSteer.Skid;
        // Standing: clearance, the centre of mass, where it tips.
        var low = float.MaxValue;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (g.Start[i] < 0 || d.Role is PartRole.Wheel or PartRole.Suspension || c.Parts[i].Seg != 0 && !c.Rigid(c.Parts[i].Seg))
                continue;
            low = MathF.Min(low, g.Min[i].Y);
        }
        b.Clearance = low == float.MaxValue ? 0 : MathF.Max(0, low - g.Ground);
        b.ComHeight = MathF.Max(0.05f, c.Com.Y - g.Ground);
        if (wheels > 0)
        {
            var comSide = Vector3.Dot(c.Com, g.Right);
            var comAlong = Vector3.Dot(c.Com, g.Fwd);
            b.TipSide = MathF.Atan2(MathF.Max(0, MathF.Min(sMax - comSide, comSide - sMin)), b.ComHeight) * 180 / MathF.PI;
            b.TipEnd = MathF.Atan2(MathF.Max(0, MathF.Min(aMax - comAlong, comAlong - aMin)), b.ComHeight) * 180 / MathF.PI;
        }
        // A climb hint: the drive force at a crawl (60 % of peak power at 3 m/s) against weight, and the driven wheels' grip (0.8 of their share of it).
        if (c.Mass > 0 && power > 0)
        {
            var pull = 0.6f * power / 3f / (c.Mass * 9.81f);
            var grip = wheels > 0 ? 0.8f * driven / wheels : 0;
            var byPull = MathF.Atan(MathF.Max(0, pull - 0.03f));
            var byGrip = MathF.Atan(grip);
            b.Climb = MathF.Min(45, MathF.Min(byPull, byGrip) * 180 / MathF.PI);
        }
        b.Fuel = fuel;
        b.Burn = burn;
        b.Electric = electric;
        b.Charge = c.ChargeCapacity;
        var seconds = electric ? (power > 0 ? c.ChargeCapacity * 1000 / power : 0) : (burn > 0 ? fuel / burn : 0);
        b.Endurance = seconds / 60;
        b.Range = seconds * b.TopSpeed / 1000;
    }

    // ---- Crew -------------------------------------------------------------------------------------------------------------------

    private static void Seats(Construct c, VehicleBrief b, Geometry g)
    {
        var list = new List<BriefSeat>();
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.DefOf(i).Role != PartRole.Seat)
                continue;
            var s = new BriefSeat { Part = Ref(g, i) };
            if (i == c.DriveSeat)
                s.Controls |= SeatControls.Drive;
            // What its wires reach, through a control module one hop on.
            for (var hop = 0; hop < 2; hop++)
                for (var k = 0; k < c.WireCount; k++)
                {
                    var from = c.Wires[k].From;
                    if (hop == 0 ? from != i : !IsModuleFrom(c, i, from))
                        continue;
                    var to = c.Wires[k].To;
                    if (to >= c.PartCount || to == i)
                        continue;
                    var d = c.DefOf(to);
                    if (d.Name.Contains("Winch", StringComparison.OrdinalIgnoreCase))
                        s.Controls |= SeatControls.Winch;
                    else if (d.Role == PartRole.Logic && IsSignal(d.Name))
                        s.Controls |= SeatControls.Signals;
                    else if (hop == 0 && !d.Name.Contains("Module", StringComparison.Ordinal) && d.Role is not (PartRole.Gun or PartRole.Joint))
                        s.Controls |= SeatControls.Other;
                }
            var (bearings, top) = g.Exposure(i);
            s.Open = top || System.Numerics.BitOperations.PopCount(bearings) >= 4;
            list.Add(s);
            VehicleGenJob.Yield();
        }
        b.Seats = list.ToArray();
        var entries = new List<BriefEntry>();
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            EntryKind? kind = d.Role == PartRole.Door ? EntryKind.Door
                : d.Walk == WalkKind.Ladder || d.Name.Contains("Ladder", StringComparison.OrdinalIgnoreCase) ? EntryKind.Ladder
                : d.Name.Contains("Hatch", StringComparison.OrdinalIgnoreCase) ? EntryKind.Hatch
                : null;
            if (kind is { } k)
                entries.Add(new BriefEntry { Part = Ref(g, i), Kind = k });
        }
        b.Entries = entries.ToArray();

        static bool IsModuleFrom(Construct c, int seat, int part)
        {
            if (part >= c.PartCount || !c.DefOf(part).Name.Contains("Module", StringComparison.Ordinal))
                return false;
            for (var k = 0; k < c.WireCount; k++)
                if (c.Wires[k].From == seat && c.Wires[k].To == part)
                    return true;
            return false;
        }
    }

    private static bool IsSignal(string name) =>
        name.Contains("Horn", StringComparison.OrdinalIgnoreCase) || name.Contains("Siren", StringComparison.OrdinalIgnoreCase) || name.Contains("Lamp", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Light", StringComparison.OrdinalIgnoreCase) || name.Contains("Radio", StringComparison.OrdinalIgnoreCase) || name.Contains("Beacon", StringComparison.OrdinalIgnoreCase);

    // ---- Guns -------------------------------------------------------------------------------------------------------------------

    private static void Guns(Construct c, VehicleBrief b, Geometry g)
    {
        var guns = new List<BriefGun>();
        var dpsByBin = new float[VehicleBrief.Bins];
        for (var k = 0; k < c.GunCount && guns.Count < 16; k++)
        {
            ref var cg = ref c.Guns[k];
            if (cg.Part >= c.PartCount)
                continue;
            var muzzle = c.ToRest(cg.Seg, cg.Muzzle);
            var dir0 = c.DirToRest(cg.Seg, cg.Forward);
            if (dir0.LengthSquared() < 1e-6f)
                continue;
            dir0 = Vector3.Normalize(dir0);
            var gun = new BriefGun { Part = Ref(c, cg.Part, muzzle), Seat = -1, Pivot = muzzle };
            // Damage and range.
            var def = cg.Def;
            var interval = Math.Max(1, (int)cg.Interval);
            gun.Dps = def == null ? 0 : def.Damage * Math.Max(1, cg.Shots) * (World.TickRate / (float)interval);
            gun.Range = def?.RangeM ?? cg.MaxRange;
            gun.MaxRange = cg.MaxRange;
            gun.Class = (cg.Effects & (GunEffect.Flame | GunEffect.Harpoon | GunEffect.Shell)) != 0 || cg.Blast >= 2 ? GunClass.Heavy // (as VehicleRoles.ClassOf: an explosive round on a carbine is still a carbine)
                : def is { Mode: FireMode.Auto } ? GunClass.Medium : GunClass.Light;
            gun.Band = (cg.Effects & GunEffect.Flame) != 0 || gun.Range < 25 ? RangeBand.Close : gun.Range < 60 ? RangeBand.Short : gun.Range < 150 ? RangeBand.Medium : RangeBand.Long;
            // Who fires it: the gunner's seat, else a logic part wired into it.
            if (cg.Seat >= 0)
                for (var s = 0; s < b.Seats.Length; s++)
                    if (b.Seats[s].Part.Index == cg.Seat)
                    {
                        gun.Seat = (sbyte)s;
                        b.Seats[s].Controls |= SeatControls.Guns;
                        b.Seats[s].Guns |= (ushort)(1 << guns.Count);
                    }
            if (gun.Seat < 0)
                for (var w = 0; w < c.WireCount; w++)
                    if (c.Wires[w].To == cg.Part && c.Wires[w].From < c.PartCount && c.DefOf(c.Wires[w].From).Role == PartRole.Logic)
                    {
                        gun.Input = Ref(g, c.Wires[w].From);
                        break;
                    }
            // The aim chain: the nearest turntable (or swivel) about the up axis, and the nearest hinge about a level one, up the gun's segments.
            int yawJ = -1, pitchJ = -1;
            for (int s = cg.Seg, guard = 0; s > 0 && s <= c.JointCount && guard <= Construct.MaxJoints; guard++)
            {
                ref var j = ref c.Joints[s - 1];
                if (!j.Freeform)
                {
                    var axis = c.DirToRest(c.FrameOf(j.ParentSeg), j.Axis);
                    var up = MathF.Abs(Vector3.Normalize(axis).Y);
                    if (yawJ < 0 && up > 0.7f && j.Type is JointType.Turntable or JointType.Swivel or JointType.Motor)
                        yawJ = s - 1;
                    else if (pitchJ < 0 && yawJ < 0 && up < 0.3f && j.Type is JointType.Motor or JointType.Turntable or JointType.Swivel)
                        pitchJ = s - 1;
                }
                s = j.ParentSeg;
            }
            gun.Mount = yawJ >= 0 ? BriefMount.Turret : pitchJ >= 0 ? BriefMount.Hinge : BriefMount.Fixed;
            // What it skips when it looks for its own vehicle in the way: whatever turns with it, and its own gun parts.
            var skip = new bool[c.PartCount];
            var turns = yawJ >= 0 ? yawJ + 1 : pitchJ >= 0 ? pitchJ + 1 : -1;
            for (var i = 0; i < c.PartCount; i++)
                skip[i] = turns > 0 && c.Under(i, turns) || i == cg.Part || c.DefOf(i).Role == PartRole.GunPart && Vector3.Distance(g.Mid(i), muzzle) < 2.5f;
            // Yaw and pitch: their joints in the rest frame, their ranges.
            Vector3 yPivot = muzzle, yAxis = Vector3.UnitY, pPivot = muzzle, pAxis = Vector3.UnitX;
            float yMin = 0, yMax = 0, pMin = 0, pMax = 0;
            if (yawJ >= 0)
            {
                ref var j = ref c.Joints[yawJ];
                var frame = c.FrameOf(j.ParentSeg);
                (yPivot, yAxis) = (c.ToRest(frame, j.Pivot), Vector3.Normalize(c.DirToRest(frame, j.Axis)));
                gun.FullCircle = !c.DefOf(j.Hinge).JointLimited || j.Max - j.Min >= MathF.Tau - 1e-3f;
                (yMin, yMax) = gun.FullCircle ? (-MathF.PI, MathF.PI) : (j.Min, j.Max);
                gun.Pivot = yPivot;
            }
            if (pitchJ >= 0)
            {
                ref var j = ref c.Joints[pitchJ];
                var frame = c.FrameOf(j.ParentSeg);
                (pPivot, pAxis) = (c.ToRest(frame, j.Pivot), Vector3.Normalize(c.DirToRest(frame, j.Axis)));
                (pMin, pMax) = c.DefOf(j.Hinge).JointLimited || j.Type == JointType.Motor ? (j.Min, j.Max) : (-0.5f, 0.5f);
                if (pMax - pMin < 1e-3f)
                    (pMin, pMax) = (j.Angle, j.Angle);
            }
            gun.YawMin = yMin * 180 / MathF.PI;
            gun.YawMax = yMax * 180 / MathF.PI;
            // Elevation over the hinge's range, at rest yaw; the pitch samples, nearest level first.
            Span<float> pitch = stackalloc float[6];
            var pn = 0;
            float eLo = float.MaxValue, eHi = float.MinValue;
            for (var s = 0; s < 5; s++)
            {
                var a = pitchJ < 0 ? 0 : pMin + (pMax - pMin) * s / 4f;
                var d = pitchJ < 0 ? dir0 : Vector3.Transform(dir0, Quaternion.CreateFromAxisAngle(pAxis, a));
                var el = MathF.Asin(Math.Clamp(d.Y, -1, 1)) * 180 / MathF.PI;
                (eLo, eHi) = (MathF.Min(eLo, el), MathF.Max(eHi, el));
                pitch[pn++] = a;
                if (pitchJ < 0)
                    break;
            }
            if (pitchJ >= 0 && 0 > pMin && 0 < pMax)
                pitch[pn++] = 0;
            gun.ElevMin = eLo;
            gun.ElevMax = eHi;
            float Elev(float a) => MathF.Asin(Math.Clamp((pitchJ < 0 ? dir0 : Vector3.Transform(dir0, Quaternion.CreateFromAxisAngle(pAxis, a))).Y, -1, 1));
            var levelAt = pitch[0];
            for (var s = 1; s < pn; s++)
                if (MathF.Abs(Elev(pitch[s])) < MathF.Abs(Elev(levelAt)))
                    levelAt = pitch[s];
            // The sweep: each bearing bin it can turn to, at each pitch sample: does the round leave clear of its own vehicle?
            var rest = g.Bearing(dir0);
            var sense = 1f;
            if (yawJ >= 0)
            {
                var turned = g.Bearing(Vector3.Transform(dir0, Quaternion.CreateFromAxisAngle(yAxis, 0.1f)));
                var delta = ((turned - rest + 540) % 360) - 180;
                sense = delta >= 0 ? 1 : -1; // (bearing degrees per radian of the joint's sign)
            }
            for (var bin = 0; bin < VehicleBrief.Bins; bin++)
            {
                float yaw;
                var want = VehicleBrief.BinBearing(bin);
                var off = ((want - rest + 540) % 360) - 180; // (-180..180 degrees from where it rests)
                if (yawJ < 0)
                {
                    if (MathF.Abs(off) > 360f / VehicleBrief.Bins / 2)
                        continue; // (only its own bearing)
                    yaw = 0;
                }
                else
                {
                    yaw = off * sense * MathF.PI / 180;
                    if (!gun.FullCircle)
                    {
                        if (yaw < yMin - 1e-3f && yaw + MathF.Tau <= yMax)
                            yaw += MathF.Tau;
                        else if (yaw > yMax + 1e-3f && yaw - MathF.Tau >= yMin)
                            yaw -= MathF.Tau;
                        if (yaw < yMin - 0.1f || yaw > yMax + 0.1f)
                            continue;
                        yaw = Math.Clamp(yaw, yMin, yMax);
                    }
                }
                var qy = Quaternion.CreateFromAxisAngle(yAxis, yaw);
                for (var s = 0; s < pn; s++)
                {
                    var qp = pitchJ < 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(pAxis, pitch[s]);
                    var m = pPivot + Vector3.Transform(muzzle - pPivot, qp);
                    var d = Vector3.Transform(dir0, qp);
                    m = yPivot + Vector3.Transform(m - yPivot, qy);
                    d = Vector3.Normalize(Vector3.Transform(d, qy));
                    var blocked = g.FirstHit(m + d * 0.02f, d, GunReach, skip) >= 0;
                    VehicleGenJob.Yield();
                    if (blocked)
                        continue;
                    gun.Clear |= 1u << bin;
                    if (pitch[s] == levelAt)
                        gun.Level |= 1u << bin;
                }
            }
            for (var bin = 0; bin < VehicleBrief.Bins; bin++)
                if ((gun.Clear >> bin & 1) != 0)
                    dpsByBin[bin] += MathF.Max(gun.Dps, 1);
            guns.Add(gun);
        }
        b.Guns = guns.ToArray();
        if (guns.Count == 0)
            return;
        // Where to fight: the guns' full-damage range weighed by their damage (a median), and the bearing that most of their damage reaches.
        var byRange = guns.OrderBy(x => x.Range).ToList();
        var total = byRange.Sum(x => MathF.Max(x.Dps, 1));
        var run = 0f;
        foreach (var x in byRange)
        {
            run += MathF.Max(x.Dps, 1);
            if (run >= total / 2)
            {
                b.Engage = x.Range;
                break;
            }
        }
        var best = 0;
        for (var bin = 1; bin < VehicleBrief.Bins; bin++)
            if (dpsByBin[bin] > dpsByBin[best] + 1e-3f || MathF.Abs(dpsByBin[bin] - dpsByBin[best]) <= 1e-3f && Broad(bin) < Broad(best) - 1e-3f)
                best = bin;
        b.BestBin = (byte)best;
        b.BestSide = (byte)VehicleBrief.SideOf(VehicleBrief.BinBearing(best));
        b.BestDps = dpsByBin[best];

        static float Broad(int bin) => MathF.Abs(MathF.Cos(VehicleBrief.BinBearing(bin) * MathF.PI / 180)); // (a tie goes to the broadside: a vehicle shows its length)
    }

    // ---- Weak points ------------------------------------------------------------------------------------------------------------

    private static readonly (string Kind, string Role)[] Plain = [("fuel", "fuel"), ("tyres", "tyres"), ("ammo", "ammo"), ("engine", "engine")];

    private static void WeakPoints(Construct c, VehicleBrief b, Geometry g, BriefFacts f)
    {
        var kinds = new string?[c.PartCount];
        if (f.StyleDef is { } st)
            foreach (var rule in VehicleIdentity.SignatureOf(st).WeakPoints)
                for (var i = 0; i < c.PartCount; i++)
                    if (kinds[i] == null && Picks(c, i, rule.Pick))
                        kinds[i] = rule.Label;
        foreach (var (kind, role) in Plain)
            for (var i = 0; i < c.PartCount; i++)
                if (kinds[i] == null && Picks(c, i, PartPick.ByRole(role)))
                    kinds[i] = kind;
        var list = new List<BriefWeak>();
        Span<float> side = stackalloc float[4];
        for (var i = 0; i < c.PartCount && list.Count < 64; i++)
        {
            if (kinds[i] is not { } kind)
                continue;
            var (mask, top) = g.Exposure(i);
            list.Add(new BriefWeak { Part = Ref(g, i), Kind = kind, Bearings = mask, Top = top });
            var role = c.DefOf(i).Role;
            var weight = role is PartRole.FuelTank or PartRole.AmmoBox ? 3f : role is PartRole.Engine || i == c.DriveSeat ? 2f : 1f; // (what burns or blows up counts most)
            for (var k = 0; k < VehicleBrief.WeakBins; k++)
                if ((mask >> k & 1) != 0)
                    side[VehicleBrief.SideOf(k * 360f / VehicleBrief.WeakBins)] += weight;
            if (role == PartRole.FuelTank && mask != 0)
                b.FuelOpen++;
        }
        b.Weak = list.ToArray();
        var most = -1;
        for (var s = 0; s < 4; s++)
            if (side[s] > 0 && (most < 0 || side[s] > side[most]))
                most = s;
        b.KeepAway = most < 0 ? (byte)255 : (byte)most;
    }

    /// <summary>A weak point rule's pick on a construct, as <see cref="VehicleIdentity"/> reads it ("driver": the seat that drives).</summary>
    private static bool Picks(Construct c, int i, PartPick p)
    {
        var d = c.DefOf(i);
        return p.Names.Length > 0 && Array.IndexOf(p.Names, d.Name) >= 0 || p.Role switch
        {
            "fuel" => d.Role == PartRole.FuelTank,
            "tyres" => d.Role == PartRole.Wheel,
            "engine" => d.Role == PartRole.Engine,
            "ammo" => d.Role == PartRole.AmmoBox,
            "seat" => d.Role == PartRole.Seat,
            "driver" => c.DriveSeat >= 0 ? c.DriveSeat == i : d.Role == PartRole.Seat,
            _ => false,
        };
    }

    // ---- Cargo, loot, hitches -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Its own cargo as the brief lists it: each stowed stack and each loose piece the cargo pass's kinds name (<see cref="Vehicle.StowKindOf"/>; anything else in its
    /// cargo is loot), with the guns each serves: a magazine the guns that take it (the workbench's rule), rounds every gun that loads them, fuel a flame gun, a gun's
    /// parts the guns with the same receiver. Loose pieces of the same kind are one entry a place.
    /// </summary>
    private static BriefStow[] Stowed(Construct c, VehicleBrief b, Geometry g, BriefFacts f)
    {
        var list = new List<BriefStow>();
        uint GunsFor(ConstructPartDef d, StowKind kind)
        {
            uint bits = 0;
            for (var k = 0; k < b.Guns.Length && k < 32; k++)
            {
                var gun = ConstructParts.Def(b.Guns[k].Part.Def);
                var flame = false;
                for (var j = 0; j < c.GunCount; j++)
                    if (c.Guns[j].Part == b.Guns[k].Part.Index)
                        flame = (c.Guns[j].Effects & GunEffect.Flame) != 0;
                var ok = kind switch
                {
                    StowKind.Magazine => ConstructAmmo.GunTakes(gun, d) == null,
                    StowKind.Ammo => !flame,
                    StowKind.Fuel => flame,
                    StowKind.GunPart => d.Role == PartRole.Gun ? d.Id == gun.Id : true,
                    _ => false,
                };
                if (ok)
                    bits |= 1u << k;
            }
            return bits;
        }
        foreach (var (part, slot, name, count) in f.Stored)
            if (part >= 0 && part < c.PartCount && ConstructParts.Find(name) is { } d && Vehicle.StowKindOf(d) is { } kind)
            {
                list.Add(new BriefStow { Part = name, Kind = kind, Count = (byte)Math.Min(count, 255), Container = Ref(g, part), Slot = (sbyte)slot, Guns = GunsFor(d, kind) });
            }
        foreach (var (name, at) in f.Loose)
            if (ConstructParts.Find(name) is { } d && Vehicle.StowKindOf(d) is { } kind)
                list.Add(new BriefStow { Part = name, Kind = kind, Count = 1, Loose = true, At = at, Slot = -1, CooksOff = d.Role == PartRole.AmmoBox, Guns = GunsFor(d, kind) });
        return list.ToArray();
    }

    private static void Cargo(Construct c, VehicleBrief b, Geometry g, BriefFacts f)
    {
        var loot = new List<BriefLoot>();
        foreach (var (part, table) in f.Loot)
            if (part >= 0 && part < c.PartCount)
            {
                var tier = table.Length > 2 && table[^2] == ' ' && char.IsDigit(table[^1]) ? table[^1] - '0' : 2;
                loot.Add(new BriefLoot { Part = Ref(g, part), Table = table, Tier = (byte)tier });
            }
        b.Loot = loot.ToArray();
        b.Cargo = f.Cargo;
        b.Stowed = Stowed(c, b, g, f);
        var hitches = new List<BriefHitch>();
        var winches = new List<BriefRef>();
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = c.DefOf(i);
            if (d.Role == PartRole.Hitch)
                hitches.Add(new BriefHitch { Part = Ref(c, i, c.ToRest(p.Seg, p.Pos + Rot24.Apply(p.Rot, d.HitchAt))), Kind = d.Hitch });
            if (d.Name.Contains("Winch", StringComparison.OrdinalIgnoreCase))
                winches.Add(Ref(g, i));
        }
        b.Hitches = hitches.ToArray();
        b.Winches = winches.ToArray();
    }
}

/// <summary><c>--brief "Style Shape" | "Style role" | MODEL_KEY | FILE [--seed S] [--tier T]</c>: a vehicle made as the spawn makes it, and its behaviour brief.</summary>
public static class VehicleBriefCli
{
    private static string? Flag(string[] args, string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

    public static int Run(string what, string[] args)
    {
        var seedGiven = int.TryParse(Flag(args, "--seed"), out var seed);
        var tier = int.TryParse(Flag(args, "--tier"), out var t) ? Math.Clamp(t, 1, VehicleRoles.MaxTier) : 2;
        Vehicle? v = null;
        string shape = "";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (File.Exists(what))
                return OfFile(what);
            if (what.Contains('/'))
            {
                if (!VehicleModel.TryParse(what, out var model, out var whyNot))
                {
                    Console.WriteLine($"--brief {what}: {whyNot}");
                    return 1;
                }
                v = VehicleFamilies.Make(model!);
                shape = v.Role?.Shape ?? "";
            }
            else if (VehicleRegistry.Find(what) is { } g)
            {
                v = VehicleRegistry.Make(g, seedGiven ? seed : g.Seeds.Count > 0 ? g.Seeds[0] : 1);
                shape = VehicleSpawnKit.Names(g).Shape;
            }
            else
            {
                foreach (var s in Styles.All)
                    if (what.StartsWith(s.Name + " ", StringComparison.OrdinalIgnoreCase) && VehicleRoles.Find(what[(s.Name.Length + 1)..]) is { } role)
                    {
                        var sd = seedGiven ? seed : 1;
                        var gen = VehicleRoles.ShapeFor(s, role, sd);
                        shape = VehicleSpawnKit.Names(gen).Shape;
                        v = VehicleRegistry.Make(gen, sd, role: new RoleAsk(role, tier, s) { Shape = shape });
                    }
                if (v == null)
                {
                    Console.WriteLine($"--brief: no generator, role, model or file \"{what}\"");
                    return 1;
                }
            }
        }
        catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
        {
            Console.WriteLine($"--brief {what}: it wasn't made: {ex.Message}");
            return 1;
        }
        var made = watch.Elapsed.TotalMilliseconds;
        var text = v.Text();
        var bp = ConstructBuild.ParseBlueprint(text, v.Name + ".json");
        var checkedAlready = v.DriveCheck is { Body: not null, Blueprint: { } dcb } && VehicleBriefMake.SameBuild(dcb, bp);
        if (!checkedAlready)
            _ = VehicleDriveCheck.Run(text, "warm.json", v.Context?.Gravity ?? 1, gate: false); // (its first run loads the maps and compiles: not the brief's cost)
        watch.Restart();
        var brief = VehicleBriefMake.ForGenerated(v, text, shape, bp);
        var ms = watch.Elapsed.TotalMilliseconds;
        if (brief == null)
        {
            Console.WriteLine($"{v.Name}: no brief (its blueprint didn't build)");
            return 1;
        }
        Print(brief, $"made in {made:0} ms; brief in {ms:0} ms ({(checkedAlready ? "the drive check's body" : "built and drive-checked for it")})");
        return 0;
    }

    private static int OfFile(string file)
    {
        var text = File.ReadAllText(file);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var r = VehicleDriveCheck.Run(text, Path.GetFileName(file), 1, gate: false);
        if (r.Body is not { } body || r.PartOf is not { } partOf)
        {
            Console.WriteLine($"{file}: {r.Why}");
            return 1;
        }
        var name = r.Blueprint!.Name;
        var style = Styles.All.FirstOrDefault(s => name.StartsWith(s.Name + " ", StringComparison.Ordinal));
        var f = new BriefFacts { Name = name, Style = style?.Name ?? "", StyleDef = style, Drive = r, Cargo = r.Blueprint.Cargo.Count };
        foreach (var (step, table) in r.Blueprint.Loot)
            if (step >= 0 && step < partOf.Length && partOf[step] >= 0)
                f.Loot.Add((partOf[step], table));
        f.CargoOf(r.Blueprint, partOf);
        var brief = VehicleBriefMake.Make(body, f);
        Print(brief, $"built, drive-checked and briefed in {watch.Elapsed.TotalMilliseconds:0} ms");
        return 0;
    }

    private static void Print(VehicleBrief brief, string cost)
    {
        foreach (var line in brief.Describe())
            Console.WriteLine(line);
        // The save's round trip: written, read back, the same text.
        var w = new SaveWriter(1024);
        brief.Write(w);
        var back = VehicleBrief.Read(new SaveReader(w.Span.ToArray()));
        var again = new SaveWriter(1024);
        back?.Write(again);
        var same = back != null && again.Span.SequenceEqual(w.Span);
        Console.WriteLine($"  {cost}; saved as {w.Length} bytes, read back {(same ? "the same" : "DIFFERENT")}");
    }
}
