using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>The design check's flight (DesignCheck.cs): its drives, nozzles and seats, the aircraft flight check and a sortie from the helm.</summary>
public static partial class DesignCheck
{
    /// <summary>
    /// Flight (step 3): what its drives, nozzles and seats came to, then the aircraft flight check from its helm (climb, hover, forward, a Ship
    /// keys turn) and the sortie; <c>--cones</c> lands it on a traffic cone and lifts off (on its legs and on its belly), <c>--legs</c> works its
    /// landing gear (up in the air, down on the ground), <c>--board</c> climbs aboard from the ground by the boarding hatch's ladder.
    /// </summary>
    private static void Flight(Run r)
    {
        var (world, c, els, bpText) = (r.World, r.Body, r.Els, r.Blueprint);
        var seats = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Role == PartRole.Seat).ToList();
        Console.WriteLine($"[design] flight: {c.ThrusterCount} thrusters ({els.Count(e => e.Kind == "nozzle")} RCS nozzles), {seats.Count} seats ({seats.Count(i => c.DefOf(i).Helm)} helm), stabilizers {c.StabCount}, drive seat {c.DriveSeat}"
            + (c.Effectors is { } fx ? $", the stabilizer found {ConstructEffectors.Found(fx)}" : ""));
        if (bpText != null && !r.Has("--no-fly"))
        {
            var t4 = Stopwatch.GetTimestamp();
            var fr = VehicleKit.VehicleFlightCheck.Run(bpText, "design check.json", gate: false);
            // (The aircraft check's limits are a small craft's: it is a note here, the sortie below is the test.)
            Console.WriteLine($"[design] aircraft flight check in {Ms(t4):0} ms: {(fr.Ok ? "flies" : "refused: " + fr.Why)}; {fr.Line}");
        }
        Seats(c);
        if (c.StabCount > 0 && c.DriveSeat >= 0 && !r.Has("--no-fly"))
        {
            if (Sortie(world, c) is { } bad)
                r.Fail("from its helm: " + bad);
            if (r.Has("--cones"))
                foreach (var belly in new[] { false, true })
                    if (Cones(world, c, belly) is { } stuck)
                        r.Fail("on the cones: " + stuck);
            if (r.Has("--legs") && Legs(world, c) is { } legs)
                r.Fail("its landing legs: " + legs);
        }
        if (r.Has("--board") && Board(r) is { } board)
            r.Fail("boarding from the ground: " + board);
    }

    /// <summary>
    /// Which way each seat faces in the ship (its yaw from the ship's -z, positive to the right seen from above), as its sitter is posed
    /// (ConstructRender.SeatPose, which turns by the def's SeatTurn past its Rot24), with the Rot24 part of it: a designer's free turn shows here.
    /// </summary>
    private static void Seats(Construct c)
    {
        static float Yaw(Vector3 f) => MathF.Atan2(f.X, -f.Z) * 180 / MathF.PI;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.Role != PartRole.Seat)
                continue;
            ConstructRender.SeatPose(c, i, 1, false, out var q, out _);
            var inShip = Quaternion.Conjugate(c.Rot) * q;
            Console.WriteLine($"[design] seat {i}{(d.Helm ? " (helm)" : "")}: faces {Yaw(Vector3.Transform(-Vector3.UnitZ, inShip)):+0.0;-0.0} deg in the ship (its Rot24 alone {Yaw(Rot24.Apply(c.Parts[i].Rot, -Vector3.UnitZ)):+0;-0}), up {Vector3.Transform(Vector3.UnitY, inShip).Y:0.00}"
                + (i == c.DriveSeat ? $", the ship's front {Yaw(c.SeatForward()):+0.0;-0.0} deg" : ""));
        }
    }

    /// <summary>The keys on the helm for <paramref name="seconds"/> (as a seated player's), a world step a tick; <paramref name="each"/> after each step.</summary>
    private static void Hold(ConstructWorld world, Construct c, Buttons keys, float seconds, Action<int>? each = null)
    {
        var seat = c.DriveSeat;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            c.Parts[seat].Keys = (ushort)((ushort)keys | CPart.Occupied);
            ConstructEdits.DriveInput(c, keys, World.TickDt);
            world.Step(World.Query, World.TickDt);
            each?.Invoke(t);
        }
    }

    /// <summary>Its landing legs (the doors of Gear 2): how many, and how many of them rest open.</summary>
    private static (int Legs, int Down, int Moving) Gear(Construct c)
    {
        int n = 0, down = 0, moving = 0;
        for (var k = 0; c.EnsureDoors() && k < c.DoorCount; k++)
            if (c.Parts[c.Doors[k].Part].D.Door is { Gear: 2 })
            {
                n++;
                down += ConstructDoors.SettledOpen(c, c.Doors[k].Part) ? 1 : 0;
                moving += ConstructDoors.IsMoving(c, c.Doors[k].Part) ? 1 : 0;
            }
        return (n, down, moving);
    }

    /// <summary>Every landing leg settled up (false) or down at once (the cones' belly landing: a check's shortcut, not the gear key).</summary>
    private static void SettleGear(Construct c, bool down)
    {
        for (var k = 0; c.EnsureDoors() && k < c.DoorCount; k++)
            if (c.Parts[c.Doors[k].Part].D.Door is { Gear: 2 })
                ConstructDoors.Settle(c, k, down);
        c.GearOut = down;
    }

    /// <summary>
    /// Stood upright on its legs on the ground at <paramref name="x"/>, <paramref name="z"/> (its centre of mass over it), still and let go,
    /// whatever the checks before left it (the doors' and the walk's hold it frozen; the cones may have tipped it): set down from just over its
    /// standing height and stepped 3 s. So each flight check starts from the same ship.
    /// </summary>
    private static void Stand(ConstructWorld world, Construct c, float x, float z)
    {
        c.Frozen = false;
        (c.Driver, c.DriverSeat) = (255, -1);
        for (var k = 0; k < c.StabCount; k++)
        {
            c.Parts[c.Stabs[k].Part].State &= unchecked((byte)~1); // (its stabilizer let go, as landed: armed, it would fly back to the height it held)
            c.Stabs[k].AltHeld = false;
        }
        SettleGear(c, true);
        c.Rot = c.PrevRot = Quaternion.Identity;
        c.Rebuild();
        var low = float.MaxValue;
        for (var i = 0; i < c.PartCount; i++)
            if (c.SolidCount(i) > 0)
            {
                c.PartBounds(i, out var mn, out _);
                low = MathF.Min(low, mn.Y);
            }
        var g = ConstructWorld.Ground(World.Query, x, z).Y;
        c.Pos = c.PrevPos = c.Pos + new Vector3(x - c.WorldCom.X, g + 0.3f - (c.Pos.Y + low), z - c.WorldCom.Z);
        (c.Vel, c.AngVel) = (Vector3.Zero, Vector3.Zero);
        c.Wake();
        for (var t = 0; t < 3 * World.TickRate; t++)
            world.Step(World.Query, World.TickDt);
    }

    private static float TiltOf(Construct c) => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;

    /// <summary>
    /// The cones (<c>--cones</c>, Karl's "can't lift off after landing on traffic cones"): set down from its helm onto the proving ground's
    /// slalom cones, once on its legs with a foot pad coming down on a cone, once with its legs up on its belly over one, then Shift; each second
    /// logged with its height, climb, the stabilizer's Landed time and armed bit, the thrusters' most, broken parts and the world contacts
    /// (their count, how many stand above the ground, so on a cone, and the mean normal of those). Fails if Shift for 6 s lifts it under 5 m.
    /// Found 2026-11-27: the cones ARE solid here (each its 0.3 x 0.7 x 0.3 box; the old count of "1 shape between them" read a list that
    /// Gather clears each call). On its belly (its pods) it lifts off. A pad that comes down on a cone tips it over: the Zero Day's legs stand
    /// 2 m either side of a centre of mass 8 m up, so it tips past 14 degrees (--legs prints the stance), a 0.7 m cone under one pad leans it
    /// about that far, it rolls on over (51 degrees by the time Shift is pressed) and lying on its side Shift can't lift it. With the legs
    /// set wider (its "t" 0.16 for 0.08: 3.4 m, 23 degrees) the same landing stands and lifts off. The legs' place is the design's.
    /// </summary>
    private static string? Cones(ConstructWorld world, Construct c, bool belly)
    {
        // A cone of the wide slalom (ProvingGround.Slalom: 9 m apart along x at z -122 +- 2.6): on its legs, its first leg's pad 0.4 m off the
        // cone's middle (an edge of the pad on it); on its belly, its centre of mass over the cone (its keel under that).
        var cone = new Vector3(-65, 0, -124.6f);
        var under = ConstructWorld.Ground(World.Query, cone.X, cone.Z).Y;
        Stand(world, c, cone.X + 30, cone.Z);
        SettleGear(c, !belly);
        var com0 = c.WorldCom;
        c.Rot = c.PrevRot = Quaternion.Identity; // (upright, whatever the leg before left it)
        c.Pos = c.PrevPos = c.Pos + (com0 - c.WorldCom);
        c.Rebuild();
        var over = c.WorldCom;
        if (!belly)
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i).Door is { Gear: 2 })
                {
                    c.PartBounds(i, out var mn, out var mx);
                    var mid = (mn + mx) * 0.5f;
                    over = c.SegToWorld(c.Parts[i].Seg, mid + new Vector3(MathF.Sign(mid.X - c.Com.X) * 0.2f, 0, 0)); // (the cone under its outboard half)
                    break;
                }
        c.Pos = c.PrevPos = c.Pos + new Vector3(cone.X - over.X, under + 12 - c.WorldCom.Y, cone.Z - over.Z);
        (c.Vel, c.AngVel) = (Vector3.Zero, Vector3.Zero);
        c.Wake();
        var seat = c.DriveSeat;
        c.Driver = 1;
        c.DriverSeat = seat;
        c.Parts[seat].SitterPlus1 = 2;
        var how = belly ? "belly" : "legs";
        // (the world's contacts of the last step, for this diagnosis only: ConstructPhysics keeps them private)
        var contacts = typeof(ConstructPhysics).GetField("_contacts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as ConstructPhysics.Contact[];
        var shapes = new List<ContactShape>();
        World.Query.Gather(cone + new Vector3(-0.4f, 0.1f, -0.4f), cone + new Vector3(0.4f, 0.6f, 0.4f), new QueryFilter(QueryLayers.World & ~QueryLayers.Trunks & ~QueryLayers.Terrain), shapes);
        var solid = shapes.Count;
        float low = 0, lifted = 0;
        void Log(string name, int t)
        {
            low = MathF.Min(low, c.WorldCom.Y - under);
            if ((t + 1) % World.TickRate != 0)
                return;
            ref var st = ref c.Stabs[0];
            var most = 0f;
            for (var k = 0; k < c.ThrusterCount; k++)
                most = MathF.Max(most, c.Thrusters[k].Level);
            var broken = 0;
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].Broken || c.Parts[i].Health <= 0)
                    broken++;
            int touching = 0, high = 0;
            var normal = Vector3.Zero;
            for (var k = 0; contacts != null && k < ConstructPhysics.LastContacts; k++)
            {
                ref var ct = ref contacts[k];
                if (ct.A != -1 && ct.B != -1)
                    continue;
                touching++;
                if (ct.Point.Y > ConstructWorld.Ground(World.Query, ct.Point.X, ct.Point.Z).Y + 0.05f)
                {
                    high++;
                    normal += ct.B == -1 ? ct.Normal : -ct.Normal; // (from the world into the ship)
                }
            }
            Console.WriteLine($"[design] cones {how} {name,-4} {(t + 1) / (float)World.TickRate:0} s: height {c.WorldCom.Y - under:0.00} m, climb {c.Vel.Y:+0.00;-0.00} m/s, landed {st.Landed:0.0} s, armed {(c.Parts[st.Part].State & 1) != 0}, "
                + $"thrusters' most {most:0.00}, held height {st.HoldAlt - ConstructPhysics.HoldHeight(c.WorldCom):+0.00;-0.00} m off, broken {broken}, world contacts {touching} ({high} over the ground{(high > 0 ? $", mean normal {Vector3.Normalize(normal):0.00}" : "")}), tilt {TiltOf(c):0} deg");
        }
        low = c.WorldCom.Y - under;
        Hold(world, c, Buttons.Crouch, 8, t => Log("down", t));
        var sat = c.WorldCom.Y - under;
        var tilted = TiltOf(c);
        var near = float.MaxValue; // (the nearest pad's middle from the cone, along the ground: under 0.85 m, the pad is over it)
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Door is { Gear: 2 })
            {
                c.PartBounds(i, out var mn, out var mx);
                var pw = c.SegToWorld(c.Parts[i].Seg, (mn + mx) * 0.5f);
                near = MathF.Min(near, Vector2.Distance(new Vector2(pw.X, pw.Z), new Vector2(cone.X, cone.Z)));
            }
        low = sat;
        Hold(world, c, Buttons.Sprint, 6, t => Log("lift", t));
        lifted = c.WorldCom.Y - under - low;
        Hold(world, c, 0, 2, t => Log("let", t));
        SettleGear(c, true); // (its legs down again, as it was made)
        c.Parts[seat].SitterPlus1 = 0;
        c.Driver = 255;
        c.DriverSeat = -1;
        Console.WriteLine($"[design] cones {how}: the cone is {(solid > 0 ? "solid" : "NOT solid")} ({solid} shapes); it came to rest {sat:0.00} m up (centre of mass), tilted {tilted:0} deg, its nearest pad's middle {near:0.00} m from the cone, then Shift for 6 s lifted it {lifted:0.0} m");
        return lifted < 5 ? $"on its {how}, Shift for 6 s lifted it {lifted:0.0} m off the cone (at least 5)" : null;
    }

    /// <summary>
    /// The landing legs (<c>--legs</c>): it stands on them as made; the gear key won't raise them standing; up 3 s on Shift, the gear key raises
    /// them (each its own slide into its pod), down on Ctrl it lands on its belly, then the gear key lowers them on the ground and GearLift stands
    /// it on its feet again, as high as it stood. Each leg's door is a foot pad on a spring (ConstructPhysics.GearFeet). Fails on any of these.
    /// </summary>
    private static string? Legs(ConstructWorld world, Construct c)
    {
        Stand(world, c, 0, 0);
        var at = c.WorldCom with { Y = 0 };
        float Height() => c.WorldCom.Y - ConstructWorld.Ground(World.Query, c.WorldCom.X, c.WorldCom.Z).Y;
        var (legs, down, _) = Gear(c);
        if (legs == 0)
            return "no landing legs";
        var seat = c.DriveSeat;
        c.Driver = 1;
        c.DriverSeat = seat;
        c.Parts[seat].SitterPlus1 = 2;
        try
        {
            Hold(world, c, 0, 2);
            var stood = Height();
            var feet = 0f;
            for (var k = 0; k < c.DoorCount; k++)
                feet = MathF.Max(feet, c.Doors[k].Squash);
            // Its stance: the pads' spread about its centre of mass (the ship's frame), and how far it may lean before it tips over the nearer side.
            float side = float.MaxValue, along = float.MaxValue, sole = float.MaxValue;
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i).Door is { Gear: 2 })
                {
                    c.PartBounds(i, out var mn, out var mx);
                    var mid = (mn + mx) * 0.5f - c.Com;
                    (side, along, sole) = (MathF.Min(side, MathF.Abs(mx.X - c.Com.X) > MathF.Abs(mn.X - c.Com.X) ? MathF.Abs(mx.X - c.Com.X) : MathF.Abs(mn.X - c.Com.X)), MathF.Min(along, MathF.Abs(mid.Z)), MathF.Min(sole, mn.Y));
                }
            var tall = c.Com.Y - sole;
            Console.WriteLine($"[design] legs: its pads reach {side:0.00} m to the side of its centre of mass and {along:0.00} m along, which stands {tall:0.00} m over their soles: it tips past {MathF.Atan2(side, tall) * 180 / MathF.PI:0} deg to the side ({MathF.Atan2(along, tall) * 180 / MathF.PI:0} fore and aft)");
            var standing = ConstructDoors.ToggleGear(world, c);
            Console.WriteLine($"[design] legs: {legs} landing legs, {down} down as made; standing, its centre of mass {stood:0.00} m up, the springs in {feet:0.000} m at most; the gear key standing: {standing ?? "raised them (!)"}");
            if (down != legs)
                return $"{legs - down} of its {legs} legs start up";
            if (standing == null)
                return "the gear key raised its legs as it stood on them";
            Hold(world, c, Buttons.Sprint, 3);
            var why = ConstructDoors.ToggleGear(world, c);
            var worst = 0.0;
            for (var t = 0; t < (DesignParts.LegSeconds + 1) * World.TickRate; t++)
            {
                c.Parts[seat].Keys = CPart.Occupied;
                ConstructEdits.DriveInput(c, 0, World.TickDt);
                worst = Math.Max(worst, TimedStep(world));
            }
            var up = Gear(c);
            Console.WriteLine($"[design] legs: up 3 s ({Height():0.0} m), the gear key: {why ?? "raising"}; {DesignParts.LegSeconds + 1:0.0} s on, {up.Down} of {legs} down, {up.Moving} moving, the worst step {worst:0.00} ms, gear state {ConstructDoors.GearState(c)}");
            if (why != null || up.Down != 0 || up.Moving != 0)
                return $"in the air the gear key didn't raise its legs ({why ?? $"{up.Down} still down"})";
            Hold(world, c, Buttons.Crouch, 10);
            Hold(world, c, 0, 2);
            var belly = Height();
            why = ConstructDoors.ToggleGear(world, c);
            worst = 0;
            var tilt = 0f;
            for (var t = 0; t < (DesignParts.LegSeconds + 2) * World.TickRate; t++)
            {
                c.Parts[seat].Keys = CPart.Occupied;
                ConstructEdits.DriveInput(c, 0, World.TickDt);
                worst = Math.Max(worst, TimedStep(world));
                tilt = MathF.Max(tilt, TiltOf(c));
            }
            Hold(world, c, 0, 2);
            var again = Height();
            var dn = Gear(c);
            Console.WriteLine($"[design] legs: landed on its belly, {belly:0.00} m up; the gear key on the ground: {why ?? "lowering"}; then {dn.Down} of {legs} down, {again:0.00} m up (stood {stood:0.00}), the worst tilt {tilt:0} deg, the worst step {worst:0.00} ms, {Vector3.Distance(c.WorldCom with { Y = 0 }, at):0.0} m from where it started");
            if (belly > stood - 1f)
                return $"with its legs up it still stood {belly:0.00} m up (on its legs {stood:0.00})";
            if (why != null || dn.Down != legs)
                return $"on the ground the gear key didn't lower its legs ({why ?? $"{legs - dn.Down} still up"})";
            if (MathF.Abs(again - stood) > 0.3f)
                return $"its legs lowered on the ground stood it {again:0.00} m up, not {stood:0.00} (GearLift)";
            return worst > 8 ? $"a step with its legs moving took {worst:0.0} ms (at most 8)" : null;
        }
        finally
        {
            c.Parts[seat].SitterPlus1 = 0;
            c.Driver = 255;
            c.DriverSeat = -1;
        }
    }

    /// <summary>
    /// Boarding from the ground (<c>--board</c>): standing on its legs, a person stands on the ground under the boarding hatch, at the foot of
    /// its ladder: F there first says the ladder is stowed; F lets the drop down and opens the lid (ConstructRide.OpenWay, as GameSandbox's F
    /// does); then the climb from the ground, played through in the movement step's climb, ends standing clear inside the ship.
    /// </summary>
    private static string? Board(Run r)
    {
        var (world, c) = (r.World, r.Body);
        Stand(world, c, 0, 0);
        ConstructRide.Bodies = world;
        var height = World.HeightOf(Stance.Stand);
        string? fails = null;
        var ladders = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.LadderTop <= 0 || ElementOf(r, i) is not { } e || !e.Id.StartsWith("hatch", StringComparison.Ordinal))
                continue;
            ladders++;
            ref var p = ref c.Parts[i];
            ConstructRide.LadderEnds(d, out var foot, out var top);
            c.WorldFrame(p.Seg, out var q, out var t);
            var (pos, rot) = (p.Pos, p.Rot);
            Vector3 ToWorld(Vector3 l) => t + Vector3.Transform(pos + Rot24.Apply(rot, l), q);
            var fw = ToWorld(foot);
            var g = ConstructWorld.Ground(World.Query, fw.X, fw.Z).Y;
            var s = new MoveState { Position = fw with { Y = g + 0.02f }, Grounded = true };
            var stowed = ConstructRide.ClimbLadder(ref s, c, i, height, frame: true);
            var opened = ConstructRide.OpenWay(world, c, i);
            for (var k = 0; k < 3 * World.TickRate; k++)
                world.Step(World.Query, World.TickDt);
            s = new MoveState { Position = fw with { Y = g + 0.02f }, Grounded = true };
            var why = ConstructRide.ClimbLadder(ref s, c, i, height, frame: true);
            var ticks = 0;
            while (why == null && (s.RideClimb > 0 || s.RideLadder != 0) && ticks++ < 2000)
                ConstructRide.Begin(world, ref s, height, true, out _, out _);
            var end = s.Position;
            var by = why == null ? ConstructRide.BlockedBy(c, -1, end, height) : -1;
            var tw = ToWorld(top);
            Console.WriteLine($"[design] board: {d.Name} ({e.Id}): its foot {fw.Y - g:0.00} m over the ground, its top {tw.Y - g:0.00} m; F from the ground before: {stowed ?? "climbed (!)"}; F: {(opened ? "let it down" : "nothing moved")}; then the climb from the ground: "
                + (why ?? (s.RideLadder != 0 ? "never ended" : by >= 0 ? $"ends in {c.DefOf(by).Name} #{by}" : $"{ticks / (float)World.TickRate:0.0} s, ends {end.Y - g:0.00} m up, {Vector3.Distance(end, tw):0.00} m from its top")));
            if (why != null)
                fails ??= $"from the ground the {d.Name.ToLowerInvariant()} refused: {why}";
            else if (s.RideLadder != 0 || by >= 0 || end.Y < tw.Y - 0.5f)
                fails ??= $"the climb from the ground didn't end standing inside ({(by >= 0 ? $"in {c.DefOf(by).Name}" : $"{end.Y - g:0.00} m up")})";
            else if (stowed == null)
                fails ??= "its drop was climbed stowed";
        }
        return ladders == 0 ? "no boarding hatch ladder" : fails;
    }

    /// <summary>
    /// A sortie from the helm on the proving ground, as a player flies it (keys held on the seat, as a seated player's are): up on Shift, a
    /// hover, ahead on W, a turn on D, down on Ctrl to the ground. Each leg's height, way, heading and speed, and what a world step cost.
    /// Straight ahead it holds its height: it may sink at most 0.5 m below where W was pressed (it sank 7 m before 2026-11-26).
    /// </summary>
    private static string? Sortie(ConstructWorld world, Construct c)
    {
        var seat = c.DriveSeat;
        c.Driver = 1;
        c.DriverSeat = seat;
        c.Parts[seat].SitterPlus1 = 2;
        float Heading() { var f = Vector3.Transform(-Vector3.UnitZ, c.Rot); return MathF.Atan2(f.X, -f.Z) * 180 / MathF.PI; }
        float Tilt() => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;
        var ground = c.WorldCom.Y;
        double worst = 0, sum = 0;
        var worstAt = "";
        var slow = 0;
        var bytes0 = GC.GetAllocatedBytesForCurrentThread();
        var jit0 = _stepJitMs;
        var n = 0;
        var sank = 0f;
        float Leg(string name, Buttons keys, float seconds)
        {
            var p0 = c.WorldCom;
            var h0 = Heading();
            var tilt = 0f;
            var low = float.MaxValue;
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                c.Parts[seat].Keys = (ushort)((ushort)keys | CPart.Occupied);
                ConstructEdits.DriveInput(c, keys, World.TickDt);
                var ms = TimedStep(world); // (less the JIT's compiles in it: the first touchdown's contact loops are compiled there, TimedStep)
                if (ms > 4)
                    slow++;
                if (ms > worst)
                    (worst, worstAt) = (ms, $"{name} {t / (float)World.TickRate:0.00} s");
                (sum, n) = (sum + ms, n + 1);
                tilt = MathF.Max(tilt, Tilt());
                low = MathF.Min(low, c.WorldCom.Y);
            }
            if (name == "ahead")
                sank = p0.Y - low;
            var d = c.WorldCom - p0;
            var turned = ((Heading() - h0) % 360 + 540) % 360 - 180;
            var fired = 0;
            for (var k = 0; k < c.ThrusterCount; k++)
                if (c.Thrusters[k].Level > 0.05f)
                    fired++;
            var line = $"[design] sortie {name,-8} {seconds:0} s: height {c.WorldCom.Y - ground:0.0} m (moved {d.Y:+0.0;-0.0} up, {MathF.Sqrt(d.X * d.X + d.Z * d.Z):0.0} along), turned {turned:+0;-0} deg, speed {c.Vel.Length():0.0} m/s, worst tilt {tilt:0} deg, {fired} of {c.ThrusterCount} thrusters lit, asleep {c.Asleep}";
            Console.WriteLine(line);
            return name == "turn" ? turned : name == "ahead" ? MathF.Sqrt(d.X * d.X + d.Z * d.Z) : c.WorldCom.Y - ground;
        }
        var up = Leg("lift", Buttons.Sprint, 6);
        var hover = Leg("hover", 0, 3);
        var ahead = Leg("ahead", Buttons.Forward, 5);
        Leg("stop", 0, 4);
        var turn = Leg("turn", Buttons.Right, 5);
        Leg("hold", 0, 3);
        Leg("land", Buttons.Crouch, 12);
        var rest = Leg("rest", 0, 3);
        Console.WriteLine($"[design] sortie: straight ahead it sank {sank:0.00} m at the most");
        Console.WriteLine($"[design] sortie: a world step with it flying ({SelfTest.ThreadCpu.Kind}): mean {sum / Math.Max(1, n):0.000} ms, worst {worst:0.00} ms ({worstAt}), {slow} over 4 ms, {_stepJitMs - jit0:0.0} ms of JIT left out, {(GC.GetAllocatedBytesForCurrentThread() - bytes0) / Math.Max(1, n)} bytes allocated a step");
        c.Parts[seat].SitterPlus1 = 0;
        c.Driver = 255;
        c.DriverSeat = -1;
        return up < 10 ? $"Shift for 6 s lifted it {up:0.0} m (at least 10)" : hover < 0.5f * up ? $"let go, it sank to {hover:0.0} m" : ahead < 30 ? $"W for 5 s took it {ahead:0.0} m (at least 30)" : sank > 0.5f ? $"W for 5 s let it sink {sank:0.00} m (at most 0.5)"
            : turn < 30 ? $"D for 5 s turned it {turn:0} degrees (at least 30)" : rest > 1 ? $"Ctrl for 12 s left it {rest:0.0} m up" : worst > 8 ? $"a step took {worst:0.0} ms (at most 8)" : null;
    }
}
