using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "RV blueprint" (Constructs/Blueprints/RV.json, made by tools/constructs/make_rv.py): the motorhome builds from shipped parts with
    /// no refused step, within the construct limits and with no coplanar faces; it drives, steers, climbs the 10 degree hill and does
    /// not tip; a player stands and walks about inside, the door stops them shut and lets them through open; the lamps, the ceiling
    /// switch, the awning and the cargo bay work; a client's copy matches.
    /// </summary>
    private static void CheckRvBlueprint()
    {
        foreach (var r in ConstructBuild.RefusedBlueprints)
            if (r.Contains("RV", StringComparison.Ordinal))
                Fail($"RV blueprint: refused: {r}");
        var bp = ConstructBuild.FindBlueprint("RV");
        if (bp == null)
        {
            Fail("RV blueprint: not loaded");
            return;
        }
        // Only shipped parts: every step names a part of the library (the loader refuses the rest, so a refusal shows above).
        foreach (var s in bp.Steps)
            if (ConstructParts.Find(s.Part) == null)
                Fail($"RV blueprint: step part \"{s.Part}\" is not a shipped part");
        var lines = new List<string>();
        var wrong = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                wrong.Add(what);
        }

        // ---- The build and the limits ---------------------------------------------------------------------------
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, new Vector3(0, BuildingY, 0), 0, out var why);
        if (c == null)
        {
            Fail($"RV blueprint: refused: {why}");
            return;
        }
        var size = c.LocalMax - c.LocalMin;
        lines.Add($"{bp.Steps.Count} steps -> {c.PartCount} parts of {Construct.MaxParts}, {c.JointCount} joints of {Construct.MaxJoints}, {c.WireCount} wires of {Construct.MaxWires}, {c.WheelCount} wheels, {c.Mass:0} kg, {size.X:0.0} x {size.Y:0.0} x {size.Z:0.0} m (wide, tall, long)");
        Want(c.PartCount <= Construct.MaxParts && c.JointCount <= Construct.MaxJoints && c.WireCount <= Construct.MaxWires && c.WheelCount <= Construct.MaxWheels, "inside the construct limits");
        Want(c.PartCount >= 60, "a real vehicle's worth of parts");
        Want(size.Z is > 8f and < 11f && size.Y is > 2.8f and < 4f && size.X is > 2.4f and < 4f, "a motorhome's size");
        Want(c.Mass is > 1500f and < 4500f, "a sensible weight for its diesel");
        Want(c.WireCount == bp.Wires.Count && c.CountRole(PartRole.Seat) == 4, $"its wires and its seats ({c.CountRole(PartRole.Seat)})");
        Want(c.CountRole(PartRole.Battery) >= 1 && c.CountRole(PartRole.Alternator) >= 1 && c.CountRole(PartRole.FuelTank) >= 1, "a battery, an alternator and a fuel tank");

        // Wheels on suspensions, the front pair steering, all four driven.
        var steered = 0;
        var driven = 0;
        var sprung = 0;
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var wh = ref c.Wheels[k];
            if (c.Parts[wh.Suspension].D.Role == PartRole.Suspension)
                sprung++;
            steered += wh.Steers ? 1 : 0;
            driven += wh.Driven ? 1 : 0;
        }
        lines.Add($"wheels: {sprung} of {c.WheelCount} on a suspension, {steered} steered, {driven} driven; can drive {c.CanDrive} {c.DriveProblem}");
        Want(c.WheelCount == 4 && sprung == 4 && steered == 2 && driven == 4 && c.CanDrive, "four sprung wheels, two steered, four driven");

        // ---- No coplanar faces, boxes of the assembled body --------------------------------------------------------
        var boxes = new List<ZFight.ZBox>();
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            foreach (var b in ZFight.Boxes(d))
            {
                var rot = c.Parts[i].Rot;
                var centre = c.Parts[i].Pos + Rot24.Apply(rot, b.C);
                boxes.Add(ZFight.ZBox.Aligned($"{i}:{d.Name}", centre, Rot24.ApplyHalf(rot, b.H)));
            }
        }
        var hits = ZFight.Find(boxes);
        lines.Add($"coplanar faces: {hits.Count} pair(s) among {boxes.Count} boxes");
        foreach (var h in hits.Take(6))
            lines.Add($"  {boxes[h.A].Name} / {boxes[h.B].Name} at {h.At.X:0.000} {h.At.Y:0.000} {h.At.Z:0.000}");
        Want(hits.Count == 0, "no coplanar faces");

        // ---- Headroom and the aisle: a player stands up inside and walks ----------------------------------------------
        float floorTop = 0, roofBottom = float.MaxValue;
        for (var i = 0; i < c.PartCount; i++)
        {
            c.PartBounds(i, out var mn, out var mx);
            var name = c.DefOf(i).Name;
            if (name == "Wood Planks 2 m")
                floorTop = MathF.Max(floorTop, mx.Y);
            else if (name.StartsWith("Sheet Steel", StringComparison.Ordinal) && mn.Y > 2.5f && mx.Y - mn.Y < 0.1f)
                roofBottom = MathF.Min(roofBottom, mn.Y);
        }
        lines.Add($"headroom {roofBottom - floorTop:0.00} m (floor {floorTop:0.00}, roof {roofBottom:0.00}); a player is {World.PlayerHeight:0.00} m");
        Want(roofBottom - floorTop >= World.PlayerHeight + 0.1f, "headroom for a standing player");
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        Vector3 Dir(Vector3 local) => Vector3.TransformNormal(local, Matrix4x4.CreateFromQuaternion(c.Rot));
        // In from the door side (x 0.7, between the dinette and the door, z 1.4) and down the aisle (+z) until the kitchen stops them.
        var from = c.ToWorld(new Vector3(0.7f, floorTop + 0.15f, 1.4f));
        var sAisle = BuildingWalk(w, from, _ => Dir(Vector3.UnitZ), 90, World.PlayerHeight, out _);
        var local = Vector3.Transform(sAisle.Position - c.Pos, Quaternion.Inverse(c.Rot));
        lines.Add($"aisle: walked from z 1.40 to {local.Z:0.00} at height {local.Y - floorTop:0.00} over the floor");
        Want(local.Z > 2.2f && local.Z < 3.2f, "walks the aisle to the kitchen end");
        Want(MathF.Abs(local.Y - floorTop) < 0.12f && up.Y > 0.99f, "stands on the floor");

        // The door: shut it stops them, open they walk out.
        var hinge = PartNamed(c, "Hinge");
        var doorJoint = hinge < 0 ? -1 : c.JointOf(hinge);
        Want(doorJoint >= 0 && PartNamed(c, "Door") >= 0, "a door on a hinge");
        bool Out()
        {
            var start = c.ToWorld(new Vector3(0.7f, floorTop + 0.15f, 1.85f));
            var s = BuildingWalk(w, start, _ => Dir(Vector3.UnitX), 90, World.PlayerHeight, out _);
            return Vector3.Transform(s.Position - c.Pos, Quaternion.Inverse(c.Rot)).X > 1.45f;
        }
        var shut = Out();
        if (doorJoint >= 0)
        {
            c.Joints[doorJoint].Open = true;
            c.Wake();
            for (var t = 0; t < 150; t++)
                w.Step(World.Query, World.TickDt);
        }
        var open = Out();
        lines.Add($"door: walking out shut {(shut ? "passes" : "stops")}, open {(open ? "passes" : "stops")}");
        Want(!shut && open, "the door stops a player shut and lets them out open");
        w.Remove(c);

        // ---- Driving, steering, the hill, tipping -----------------------------------------------------------------
        var dirt = new DriveWorld { Kind = ShapeKind.Terrain };
        var run = DriveRun(bp, dirt, 360, Buttons.Forward, "the RV");
        var turn = DriveRun(bp, dirt, 120, Buttons.Forward | Buttons.Left, "the RV");
        var hill = new DriveWorld { Kind = ShapeKind.Terrain, RampX = 3f, RampSlope = MathF.Tan(10f * MathF.PI / 180f) };
        var climb = DriveRun(bp, hill, 480, Buttons.Forward, "the RV");
        var tilt = turn.Car == null ? 0 : Vector3.Transform(Vector3.UnitY, turn.Car.Rot).Y;
        lines.Add($"drive: {run.Dist:0} m in 6 s ({run.Speed:0.0} m/s); a left turn of {turn.Yaw:0} deg in 2 s (up {tilt:0.00}); the 10 degree hill: {climb.Dist:0} m in 8 s, now {climb.Car?.WorldCom.Y:0.0} m up");
        Want(run.Dist > 25f, "drives forward");
        Want(MathF.Abs(turn.Yaw) > 10f && tilt > 0.7f, "steers without tipping");
        Want(climb.Car != null && climb.Car.WorldCom.Y > 1.5f && climb.Dist > 15f, "climbs the 10 degree hill");
        if (run.Car != null)
        {
            var hCom = run.Car.Com.Y + 0.55f; // (the wheels' bottoms are at -0.55)
            var track = run.Car.Wheels[0].Centre.X - run.Car.Wheels[1].Centre.X;
            var tip = MathF.Atan2(MathF.Abs(track) * 0.5f, hCom) * 180f / MathF.PI;
            lines.Add($"stability: centre of mass {hCom:0.00} m up, track {MathF.Abs(track):0.00} m: tips at {tip:0} degrees");
            Want(tip > 30f, "a centre of mass low enough not to tip");
        }

        // ---- Lamps, the ceiling switch, the awning, the cargo bay ----------------------------------------------------------
        {
            var sw = new ConstructWorld();
            var q = new DriveWorld();
            if (DriveSpawn(sw, bp, 0, 0, MathF.PI / 2, "the RV") is { } car)
            {
                DriveSteps(sw, q, 30);
                var e = new ConstructEdits();
                e.SetTool(1, ConstructTool.None);
                var seat = car.DriveSeat;
                var lamps = new List<int>();
                for (var i = 0; i < car.PartCount; i++)
                    if (car.DefOf(i).Name == "Lamp")
                        lamps.Add(i);
                int Lit() => lamps.Count(l => car.Parts[l].Out > 0.5f && car.Parts[l].Powered);
                var before = Lit();
                DriveSteps(sw, q, 2, _ => car.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyAim);
                DriveSteps(sw, q, 8, _ => car.Parts[seat].Keys = CPart.Occupied);
                var afterAim = Lit();
                var ceiling = PartNamed(car, "Switch", 0);
                var awning = PartNamed(car, "Switch", 1);
                var eye = ConstructRender.PartCentre(car, ceiling) + new Vector3(0, 0.3f, 0.6f);
                var used = e.Use(sw, 1, eye, car.Id, ceiling);
                DriveSteps(sw, q, 8);
                var afterSwitch = Lit();
                var motor = PartNamed(car, "Motor Hinge");
                var mj = motor < 0 ? -1 : car.JointOf(motor);
                var a0 = mj < 0 ? 0 : car.Joints[mj].Angle;
                var used2 = e.Use(sw, 1, ConstructRender.PartCentre(car, awning) + new Vector3(0, 0.3f, 0.6f), car.Id, awning);
                DriveSteps(sw, q, 150);
                var a1 = mj < 0 ? 0 : car.Joints[mj].Angle;
                lines.Add($"lamps: {lamps.Count} on the body, {before} lit at rest, {afterAim} after a tap of aim, {afterSwitch} after the ceiling switch ({used ?? "ok"}); awning angle {a0:0.00} -> {a1:0.00} ({used2 ?? "ok"}); powered {car.Parts[lamps[0]].Powered}");
                Want(lamps.Count == 5 && before == 0 && afterAim == 4 && afterSwitch == 5, "the aim key lights the head and tail lamps and the switch the ceiling");
                Want(mj >= 0 && MathF.Abs(a1 - a0) > 0.2f, "the awning swings out from its switch");

                // The cargo bay: a loose block set down in it is caught (the volume starts frozen) and counted in the mass.
                var frame = ConstructParts.Need("Frame Block");
                var mass0 = car.Mass;
                var load = ConstructBuild.PlaceNew(sw, frame, car.ToWorld(new Vector3(0.0f, 1.2f, 4.1f)), 0);
                if (load == null)
                    wrong.Add("a block set down in the cargo bay");
                else
                {
                    DriveSteps(sw, q, 120);
                    lines.Add($"cargo bay: the block is carried {load.CarriedBy == car.Id}; mass {mass0:0} -> {car.Mass:0} kg");
                    Want(load.CarriedBy == car.Id && car.Mass > mass0 + load.Mass * 0.9f, "the cargo bay catches a loose block");
                }
            }
        }

        // ---- A client's copy -----------------------------------------------------------------------------------------------
        {
            var server = new ConstructWorld();
            var q = new DriveWorld();
            if (DriveSpawn(server, bp, 0, 0, MathF.PI / 2, "the RV") is { } car)
            {
                DriveSteps(server, q, 30);
                var client = new ConstructWorld();
                var pw = new PacketWriter(65536);
                ConstructNet.WriteFull(pw, car);
                var pr = new PacketReader(pw.Span[1..]);
                ConstructNet.Read(ref pr, client, 0);
                var copy = client.Get(car.Id);
                var same = copy != null && copy.PartCount == car.PartCount && copy.JointCount == car.JointCount && copy.WireCount == car.WireCount
                           && copy.WheelCount == car.WheelCount && MathF.Abs(copy.Mass - car.Mass) < 1f;
                if (same && copy != null)
                    for (var i = 0; i < car.PartCount; i++)
                        same &= copy.Parts[i].Def == car.Parts[i].Def && Vector3.Distance(copy.Parts[i].Pos, car.Parts[i].Pos) < 0.01f && copy.Parts[i].Rot == car.Parts[i].Rot;
                lines.Add($"client copy: {copy?.PartCount} parts, {copy?.JointCount} joints, {copy?.WireCount} wires, {copy?.Mass:0} kg ({(same ? "identical" : "DIFFERENT")}), {pw.Length} bytes");
                Want(same, "a client's copy is the same construct");
            }
        }

        foreach (var l in lines)
            Line("RV: " + l);
        if (wrong.Count > 0)
            Fail($"RV blueprint: {string.Join("; ", wrong)}");
    }
}
