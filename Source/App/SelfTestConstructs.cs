using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string ConstructYardMap = "Construct Yard";

    /// <summary>
    /// The construct core (docs/sandbox/CONSTRUCTS.md), headless on the Construct Yard: the part files load and bad ones are
    /// refused with a reason; the blueprints build; a buggy settles on its wheels, drives a scripted course (straight, a
    /// turn, the brake) and climbs the ramp onto the deck; a shot-off fuel line stops it; a broken suspension drops its
    /// wheel as a wreck; the gate's hinged door swings; a car built part by part with the tools (bare hands refused, the
    /// wrong tool refused, ghost sockets offered); a client copy from the wire matches; no allocation per tick.
    /// </summary>
    private static void CheckConstructs()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"constructs: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        ConstructLibrary();
        ConstructRefusals();
        ConstructSettleAndDrive(yard);
        ConstructRamp(yard);
        ConstructBreaks(yard);
        ConstructDoor(yard);
        ConstructScriptedBuild(yard);
        ConstructNetRoundTrip(yard);
        ConstructNoAllocations(yard);
        ConstructLoopback(yard);
    }

    /// <summary>
    /// A real host and client in the Sandbox mode: the client gets every construct; it sits in a buggy with bare hands and
    /// drives it with its own keys; the server's buggy moves and the client's copy follows; a part the server breaks
    /// breaks on the client; a client asking to build with bare hands is refused with a reason; a late joiner gets the lot.
    /// </summary>
    private static void ConstructLoopback(Map yard)
    {
        const ushort port = 26291;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("constructs loopback: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        var mirrored = a.Constructs.Bodies.Count == server.Constructs.Bodies.Count && server.Constructs.Bodies.Count > 0 && a.SandboxOn;
        // Sit in the first buggy (bare hands: use only).
        var car = server.Constructs.Bodies.First(b => b.CanDrive);
        var seat = car.Root;
        var seatAt = ConstructEdits.SeatFeet(car, seat);
        sp.Teleport(ConstructWorld.Ground(World.Query, seatAt.X - 1.6f, seatAt.Z));
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxSend(CActKind.Use, body: car.Id, part: (byte)seat);
        Pump(server, [a], 0.5, ref now, clock);
        var seated = car.Driver == a.LocalId && a.SandboxHands.Hand(a.LocalId).Seated;
        var p0 = car.WorldCom;
        a.HeldButtons = Buttons.Forward;
        Pump(server, [a], 1.5, ref now, clock);
        a.HeldButtons = Buttons.Jump;
        Pump(server, [a], 1.0, ref now, clock);
        a.HeldButtons = Buttons.None;
        var drove = Vector3.Distance(p0, car.WorldCom);
        var copy = a.Constructs.Get(car.Id);
        if (copy != null)
            ConstructNet.Blend(a.Constructs, now + 1, 0);
        var follow = copy == null ? 99 : Vector3.Distance(copy.Pos, car.Pos);
        var driverFeet = Vector3.Distance(sp.Position, ConstructEdits.SeatFeet(car, seat));
        // Out of the seat, then bare hands can't build.
        a.SandboxSend(CActKind.Stand);
        Pump(server, [a], 0.4, ref now, clock);
        var stood = car.Driver == 255 && !server.SandboxHands.Hand(a.LocalId).Seated;
        server.SandboxHands.Hand(a.LocalId).Held = ConstructParts.Need("Frame Block").Id; // (as if from the rack)
        a.SandboxSend(CActKind.PlaceNew, at: ConstructWorld.Ground(World.Query, sp.Position.X + 1.5f, sp.Position.Z), yaw: 0);
        Pump(server, [a], 0.5, ref now, clock);
        var notice = a.SandboxNotice;
        var refused = notice.Contains("tool");
        // A part the server breaks breaks on the client.
        var line = PartOf(car, PartRole.FuelLine);
        server.Constructs.DamagePart(car, line, 1000, out _);
        Pump(server, [a], 0.5, ref now, clock);
        copy = a.Constructs.Get(car.Id);
        var broke = copy != null && copy.PartCount == car.PartCount && !copy.CanDrive;
        // Milestone 2: the motor gate's button (server side) swings its door; the client mirrors the joint, the wires and the signals.
        var mgate = server.Constructs.Bodies.First(x => x.JointCount > 0 && x.Joints[0].Type == JointType.Motor);
        var mbutton = PartNamed(mgate, "Button");
        var bpos = ConstructRender.PartCentre(mgate, mbutton);
        var pressed = server.SandboxHands.Use(server.Constructs, sp.Id, bpos + new Vector3(0, 0.5f, -1.5f), mgate.Id, mbutton);
        var turret = server.Constructs.Bodies.First(x => x.JointCount > 0 && x.Joints[0].Type == JointType.Turntable);
        turret.Parts[PartNamed(turret, "Lever")].State = 1; // (the lever forward: the turntable turns)
        Pump(server, [a], 2.0, ref now, clock);
        string Mirror(Game client)
        {
            ConstructNet.Blend(client.Constructs, now + 1, 0);
            var worstJoint = 0f;
            var wiresSame = client.Constructs.TotalWires == server.Constructs.TotalWires && server.Constructs.TotalWires > 0;
            var signalsSame = true;
            foreach (var sc in server.Constructs.Bodies)
            {
                if (client.Constructs.Get(sc.Id) is not { } cc || cc.JointCount != sc.JointCount || cc.WireCount != sc.WireCount)
                    return "a construct's joints or wires differ";
                for (var j = 0; j < sc.JointCount; j++)
                    worstJoint = MathF.Max(worstJoint, MathF.Abs(World.WrapAngle(cc.Joints[j].Angle - sc.Joints[j].Angle)) * (sc.Joints[j].Slides ? 0 : 1));
                for (var i = 0; i < sc.PartCount; i++)
                    if (sc.Parts[i].D.IsSignal && ConstructLogic.Quantise(sc.Parts[i].Out) != ConstructLogic.Quantise(cc.Parts[i].Out) && sc.Parts[i].D.Logic != CLogic.Speed)
                        signalsSame = false;
            }
            // (the turntable keeps turning: 45 deg/s for a pose interval and the round trip is a few degrees)
            return worstJoint > 0.15f ? $"a joint is off by {worstJoint * 180 / MathF.PI:0.0} deg" : !wiresSame ? "the wires differ" : !signalsSame ? "a signal differs" : "";
        }
        var mirrorA = Mirror(a);
        var openA = a.Constructs.Get(mgate.Id) is { } ma ? ma.Joints[0].Angle : 0;
        // A late joiner gets every construct.
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.5, ref now, clock);
        var mirrorB = Mirror(b);
        Line($"loopback, milestone 2: the motor gate's button {pressed ?? "ok"}, its door {mgate.Joints[0].Angle * 180 / MathF.PI:0} deg on the server and {openA * 180 / MathF.PI:0} deg on the client; the client mirrors joints, {a.Constructs.TotalWires} wires and signals: {(mirrorA.Length == 0 ? "yes" : mirrorA)}; the late joiner: {(mirrorB.Length == 0 ? "yes" : mirrorB)}");
        if (pressed != null || mgate.Joints[0].Angle < 1.5f || mirrorA.Length > 0 || mirrorB.Length > 0)
            Fail("constructs loopback: a client (or a late joiner) doesn't mirror the joints, wires and signals");
        var joined = b.Constructs.Bodies.Count == server.Constructs.Bodies.Count && b.Constructs.TotalParts == server.Constructs.TotalParts;
        Line($"loopback: the client mirrors {a.Constructs.Bodies.Count} of {server.Constructs.Bodies.Count} constructs {mirrored}; sat in the buggy {seated}, drove it {drove:0.0} m with its own keys (its copy within {follow:0.00} m, the driver {driverFeet:0.00} m from the seat); stood up {stood}; bare-hands build refused {refused} (\"{notice}\"); a broken fuel line mirrored {broke}; a late joiner got them all {joined}");
        if (!mirrored)
            Fail("constructs loopback: the client doesn't mirror the server's constructs");
        if (!seated || drove < 3 || follow > 0.5f || driverFeet > 0.3f)
            Fail("constructs loopback: the client couldn't sit in the buggy and drive it (or its copy or the driver didn't follow)");
        if (!stood || !refused)
            Fail("constructs loopback: standing up, or the bare-hands refusal, didn't go through");
        if (!broke || !joined)
            Fail("constructs loopback: a broken part or a late joiner didn't get the server's constructs");
    }

    private static Construct? Buggy(ConstructWorld w, float x, float z, float yaw)
    {
        var bp = ConstructBuild.FindBlueprint("Buggy");
        if (bp == null)
        {
            Fail("constructs: no Buggy blueprint");
            return null;
        }
        var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, x, z), yaw, out var why);
        if (c == null)
            Fail($"constructs: the Buggy blueprint doesn't build: {why}");
        return c;
    }

    private static void StepConstructs(ConstructWorld w, int ticks, Action<int>? each = null)
    {
        for (var t = 0; t < ticks; t++)
        {
            each?.Invoke(t);
            w.Step(World.Query, World.TickDt);
        }
    }

    private static float YawOf(Construct c)
    {
        var f = Vector3.Transform(c.SeatForward(), c.Rot);
        return MathF.Atan2(f.X, -f.Z);
    }

    private static void ConstructLibrary()
    {
        var parts = ConstructParts.All;
        foreach (var r in ConstructParts.Refused)
            Fail($"constructs: a shipped part file is refused: {r}");
        foreach (var r in ConstructBuild.RefusedBlueprints)
            Fail($"constructs: a shipped blueprint is refused: {r}");
        var roles = Enum.GetValues<PartRole>().Where(role => !parts.Any(p => p.Role == role)).ToArray();
        if (roles.Length > 0)
            Fail($"constructs: no part for the roles {string.Join(", ", roles)}");
        foreach (var p in parts)
            if (p.Name.Any(ch => ch > 126) || p.Description.Any(ch => ch > 126))
                Fail($"constructs: {p.Name}: player text isn't ASCII");
        Line($"library: {parts.Count} part types ({string.Join(", ", parts.Select(p => p.Name))}), {ConstructBuild.Blueprints.Count} blueprints ({string.Join(", ", ConstructBuild.Blueprints.Select(b => $"{b.Name} {b.Steps.Count} steps"))}), hash {ConstructParts.Hash:X8}");
    }

    private static void ConstructRefusals()
    {
        const string good = """{ "name": "T", "role": "frame", "tool": "welder", "mass": 10, "health": 50, "boxes": [ { "size": [0.5, 0.5, 0.5] } ], "sockets": [ { "at": [0, 0.25, 0], "dir": "+y" } ] }""";
        (string Json, string Want)[] bad =
        [
            (good.Replace("\"frame\"", "\"spaceship\""), "\"role\" is one of"),
            (good.Replace("\"welder\"", "\"hammer\""), "\"tool\""),
            (good.Replace("\"mass\": 10", "\"mass\": -3"), "\"mass\""),
            (good.Replace("[0, 0.25, 0]", "[0, 0.1, 0]"), "not on that face"),
            (good.Replace("\"dir\": \"+y\"", "\"dir\": \"up\""), "a direction is one of"),
            (good.Replace("\"health\": 50", "\"health\": 50, \"colour\": 1"), "unknown key \"colour\""),
            (good.Replace("\"boxes\": [ { \"size\": [0.5, 0.5, 0.5] } ]", "\"boxes\": []"), "\"boxes\""),
            (good.Replace("\"role\": \"frame\"", "\"role\": \"wheel\""), "needs a \"wheel\" block"),
            (good.Replace("\"role\": \"frame\"", "\"role\": \"engine\", \"engine\": { \"force\": 10 }"), "missing \"topSpeed\""),
            (good.Replace("\"role\": \"frame\"", "\"role\": \"frame\", \"engine\": { \"force\": 10, \"topSpeed\": 5 }"), "only for engine parts"),
            (good.Replace("\"name\": \"T\"", "\"name\": \"\""), "\"name\""),
        ];
        var ok = 0;
        try
        {
            ConstructParts.Parse(good, "good.json");
        }
        catch (ConstructPartException ex)
        {
            Fail($"constructs: the reference part is refused: {ex.Message}");
        }
        foreach (var (json, want) in bad)
        {
            try
            {
                ConstructParts.Parse(json, "bad.json");
                Fail($"constructs: a bad part file loaded (wanted a refusal with {want})");
            }
            catch (ConstructPartException ex)
            {
                if (!ex.Message.Contains(want, StringComparison.OrdinalIgnoreCase))
                    Fail($"constructs: refused for the wrong reason: \"{ex.Message}\" (wanted {want})");
                else
                    ok++;
            }
        }
        // A blueprint step that doesn't fit is refused with the step.
        try
        {
            var bp = ConstructBuild.ParseBlueprint("""{ "name": "Bad", "steps": [ { "part": "Frame Block" }, { "part": "Wheel", "on": 0, "socket": "+x", "by": "axle" } ] }""", "bad.json");
            if (ConstructBuild.Build(new ConstructWorld(), bp, Vector3.Zero, Quaternion.Identity, out var why) != null || !why.Contains("step 1"))
                Fail($"constructs: a wheel onto a bare frame block built (it needs a suspension): \"{why}\"");
            else
                ok++;
        }
        catch (ConstructPartException ex)
        {
            Fail($"constructs: blueprint parse: {ex.Message}");
        }
        Line($"refusals: {ok} of {bad.Length + 1} bad files refused with the right reason");
    }

    private static void ConstructSettleAndDrive(Map yard)
    {
        var w = new ConstructWorld();
        if (Buggy(w, -8, 4, 0) is not { } car)
            return;
        var start = car.WorldCom;
        StepConstructs(w, 120);
        var touching = 0;
        for (var k = 0; k < car.WheelCount; k++)
            if (car.Wheels[k].Touching)
                touching++;
        var tilt = Vector3.Transform(Vector3.UnitY, car.Rot).Y;
        var ground = ConstructWorld.Ground(World.Query, car.WorldCom.X, car.WorldCom.Z).Y;
        Line($"settle: {car.PartCount} parts, {car.Mass:0} kg, {car.WheelCount} wheels ({touching} on the ground), {car.PointCount} hull points; centre of mass {car.WorldCom.Y - ground:0.00} m up, tilt {MathF.Acos(Math.Clamp(tilt, -1, 1)) * 180 / MathF.PI:0.0} deg, moved {Vector3.Distance(start with { Y = 0 }, car.WorldCom with { Y = 0 }):0.00} m, asleep {car.Asleep}; drives: {car.CanDrive} {car.DriveProblem}");
        if (touching != 4 || tilt < 0.98f || Vector3.Distance(start with { Y = 0 }, car.WorldCom with { Y = 0 }) > 0.3f || !car.CanDrive)
            Fail("constructs: the buggy doesn't settle level on four wheels (or can't drive)");
        // Drive along the open middle of the yard (z 0, from the west wall heading east): 3 s full throttle, then 1.5 s
        // turning right, then the brake.
        w.Remove(car);
        if (Buggy(w, -27, 0, MathF.PI / 2) is not { } car2)
            return;
        car = car2;
        StepConstructs(w, 30);
        car.Driver = 1;
        car.DriverSeat = car.Root;
        var p0 = car.WorldCom;
        var yaw0 = YawOf(car);
        StepConstructs(w, 180, _ => ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt));
        var speed = car.Vel.Length();
        var straight = Vector3.Distance(p0, car.WorldCom);
        var drift = MathF.Abs(World.WrapAngle(YawOf(car) - yaw0));
        StepConstructs(w, 60, _ => ConstructEdits.DriveInput(car, Buttons.Forward | Buttons.Right, World.TickDt));
        var turned = World.WrapAngle(YawOf(car) - yaw0);
        StepConstructs(w, 150, _ => ConstructEdits.DriveInput(car, Buttons.Jump, World.TickDt));
        var stopped = car.Vel.Length();
        var upright = Vector3.Transform(Vector3.UnitY, car.Rot).Y;
        Line($"drive: {straight:0.0} m in 3 s ({speed:0.0} m/s, heading off by {drift * 180 / MathF.PI:0.0} deg), then {turned * 180 / MathF.PI:0} deg right in 1 s, braked to {stopped:0.00} m/s at {car.WorldCom.X:0}, {car.WorldCom.Z:0}, upright {upright:0.00}, fuel {car.Fuel:0.0} l");
        if (straight < 15 || speed < 8)
            Fail($"constructs: the buggy is too slow ({straight:0.0} m in 3 s, {speed:0.0} m/s)");
        if (drift > 0.08f)
            Fail("constructs: the buggy doesn't drive straight");
        if (turned < 0.5f)
            Fail("constructs: steering right didn't turn the buggy right");
        if (stopped > 0.5f || upright < 0.9f)
            Fail("constructs: the brake didn't stop the buggy (or it rolled)");
    }

    private static void ConstructRamp(Map yard)
    {
        // The ramp runs from x 3 (ground) to x 14.5 (the deck, 2.5 m up) along z -16; the buggy starts facing +X.
        var w = new ConstructWorld();
        if (Buggy(w, -4, -16, MathF.PI / 2) is not { } car)
            return;
        StepConstructs(w, 30);
        car.Driver = 1;
        car.DriverSeat = car.Root;
        var top = 0f;
        StepConstructs(w, 330, t =>
        {
            ConstructEdits.DriveInput(car, car.WorldCom.X < 19 ? Buttons.Forward : Buttons.Jump, World.TickDt);
            top = MathF.Max(top, car.WorldCom.Y);
        });
        var deck = ConstructWorld.Ground(World.Query, car.WorldCom.X, car.WorldCom.Z).Y;
        Line($"ramp: reached x {car.WorldCom.X:0.0}, centre of mass {car.WorldCom.Y:0.00} m (the ground there {deck:0.00} m), upright {Vector3.Transform(Vector3.UnitY, car.Rot).Y:0.00}");
        if (car.WorldCom.X < 15 || deck < 2.3f)
            Fail("constructs: the buggy didn't drive up the ramp onto the deck");
    }

    private static int PartOf(Construct c, PartRole role, int nth = 0)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Role == role && nth-- == 0)
                return i;
        return -1;
    }

    private static void ConstructBreaks(Map yard)
    {
        // A shot-off fuel line: the engine starves, the buggy stops driving.
        var w = new ConstructWorld();
        if (Buggy(w, -8, 4, 0) is not { } car)
            return;
        StepConstructs(w, 60);
        car.Driver = 1;
        car.DriverSeat = car.Root;
        var line = PartOf(car, PartRole.FuelLine);
        var hp = car.Parts[line].Health;
        w.DamagePart(car, line, 5, out var dealt); // armour 0: a pistol round's worth chips it
        var chipped = car.Parts[line].Health < hp;
        w.DamagePart(car, line, 1000, out _);
        StepConstructs(w, 1);
        var p0 = car.WorldCom;
        StepConstructs(w, 120, _ => ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt));
        var moved = Vector3.Distance(p0, car.WorldCom);
        Line($"fuel line shot off: chipped {chipped} ({dealt:0} damage), {car.PartCount} parts left, drives {car.CanDrive} ({car.DriveProblem}), rolled {moved:0.00} m on full throttle");
        if (!chipped || car.CanDrive || moved > 0.5f || !car.DriveProblem.Contains("fuel"))
            Fail("constructs: a broken fuel line didn't stop the buggy");
        // Armour: a panel's 12 shrugs off a small hit.
        var w3 = new ConstructWorld();
        if (ConstructBuild.PlaceNew(w3, ConstructParts.Need("Panel"), ConstructWorld.Ground(World.Query, 0, 0), 0) is { } plate)
        {
            w3.DamagePart(plate, 0, 10, out var through);
            if (through > 0 || plate.Parts[0].Health < ConstructParts.Need("Panel").Health)
                Fail("constructs: a panel's armour didn't stop a 10 damage hit");
        }

        // A broken suspension: its wheel falls away as a wreck, and the car sags on that corner.
        var w2 = new ConstructWorld();
        if (Buggy(w2, -8, 4, 0) is not { } car2)
            return;
        StepConstructs(w2, 60);
        var sus = PartOf(car2, PartRole.Suspension);
        var before = w2.Bodies.Count;
        var wheelsBefore = car2.CountRole(PartRole.Wheel);
        w2.DamagePart(car2, sus, 10000, out _);
        var wreck = w2.Bodies.FirstOrDefault(b => b != car2 && b.PartCount == 1 && b.DefOf(0).Role == PartRole.Wheel);
        var debris = w2.Bodies.FirstOrDefault(b => b != car2 && b.PartCount == 1 && b.DefOf(0).Role == PartRole.Suspension && b.Parts[0].Broken);
        var y0 = wreck?.WorldCom.Y ?? 0;
        StepConstructs(w2, 90);
        Line($"suspension broken: bodies {before} -> {w2.Bodies.Count}, the car has {car2.CountRole(PartRole.Wheel)} of {wheelsBefore} wheels, the wreck: {(wreck == null ? "none" : $"{wreck.PartCount} part ({wreck.DefOf(0).Name}), wreck {wreck.Wreck}, fell {y0 - wreck.WorldCom.Y:0.00} m")}; events: {string.Join("; ", w2.Events)}");
        if (wreck == null || !wreck.Wreck || wreck.Parts[0].Broken || car2.CountRole(PartRole.Wheel) != wheelsBefore - 1 || debris == null || debris.Loose)
            Fail("constructs: a wheel whose suspension broke didn't fall away as a wreck (with the wrecked suspension as debris)");
        else if (wreck.WorldCom.Y > y0 - 0.02f && !wreck.Asleep)
            Fail("constructs: the wrecked wheel didn't fall");
        // A blast: parts near it take damage.
        var hpSum = 0f;
        for (var i = 0; i < car2.PartCount; i++)
            hpSum += car2.Parts[i].Health;
        var hits = w2.Blast(car2.WorldCom + new Vector3(0, 1.5f, 0), 4f, 150f);
        var hpAfter = 0f;
        for (var i = 0; i < car2.PartCount; i++)
            hpAfter += car2.Parts[i].Health;
        Line($"blast over the car: {hits} parts hit, health {hpSum:0} -> {hpAfter:0}, bodies {w2.Bodies.Count}");
        if (hits == 0 || hpAfter >= hpSum)
            Fail("constructs: a blast didn't damage the parts near it");
    }

    private static void ConstructDoor(Map yard)
    {
        var w = new ConstructWorld();
        var bp = ConstructBuild.FindBlueprint("Gate");
        if (bp == null || ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, -16, -12), 0, out var why) is not { } gate)
        {
            Fail("constructs: the Gate blueprint doesn't build");
            return;
        }
        StepConstructs(w, 30);
        var edits = new ConstructEdits();
        var door = PartOf(gate, PartRole.Door);
        gate.WorldFrame(gate.Parts[door].Seg, out var q, out var t);
        var eye = t + Vector3.Transform(gate.Parts[door].Pos, q) + new Vector3(0, 0, 1.5f);
        var pos0 = gate.Pos;
        var use = edits.Use(w, 3, eye, gate.Id, door);
        StepConstructs(w, 60);
        var opened = gate.JointCount > 0 ? gate.Joints[0].Angle : 0;
        edits.Use(w, 3, eye, gate.Id, door);
        StepConstructs(w, 90);
        var shut = gate.JointCount > 0 ? gate.Joints[0].Angle : 9;
        // Walking into the shut door pushes it.
        var s = new MoveState { Position = (t + Vector3.Transform(gate.Parts[door].Pos, q)) with { Y = gate.Pos.Y + 0.31f } + new Vector3(0, 0, 0.6f), Velocity = new Vector3(0, 0, -5), Grounded = true };
        for (var k = 0; k < 20; k++)
        {
            s.Position += new Vector3(0, 0, -0.05f);
            w.BlockPlayer(ref s, World.PlayerHeight, server: true);
        }
        StepConstructs(w, 20);
        var pushed = gate.JointCount > 0 ? gate.Joints[0].Angle : 0;
        Line($"gate: anchored {gate.Anchored}, {gate.JointCount} joint; use: {use ?? "ok"}, opened to {opened * 180 / MathF.PI:0} deg, shut to {shut * 180 / MathF.PI:0} deg, a walk-in pushed it to {pushed * 180 / MathF.PI:0} deg; the gate moved {Vector3.Distance(pos0, gate.Pos):0.000} m");
        if (!gate.Anchored || gate.JointCount != 1 || use != null || MathF.Abs(opened) < 1.0f || MathF.Abs(shut) > 0.1f || MathF.Abs(pushed) < 0.15f || Vector3.Distance(pos0, gate.Pos) > 0.001f)
            Fail("constructs: the gate's hinged door didn't swing open and shut (or the anchored gate moved)");
    }

    /// <summary>A car built part by part through the edit rules, as players do it: tools, timings, ghost sockets.</summary>
    private static void ConstructScriptedBuild(Map yard)
    {
        var w = new ConstructWorld();
        var e = new ConstructEdits();
        const byte me = 4;
        var now = 0.0;
        var ground = ConstructWorld.Ground(World.Query, 0, 10);
        var eye = ground + new Vector3(0, 1.6f, 2.5f);
        string? Act(Func<string?> f) => f();
        void Wait(double s)
        {
            for (var k = 0; k < (int)(s * World.TickRate) + 1; k++)
            {
                now += World.TickDt;
                e.Tick(w, now, _ => eye);
                w.Step(World.Query, World.TickDt);
            }
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        // Bare hands: a part in hand, but no tool: refused.
        var sandbox = yard.Sandbox!;
        var rackFeet = sandbox.Rack + new Vector3(0, 0, -1.5f);
        Want(e.TakeFromRack(sandbox, me, rackFeet) == null && e.Hand(me).Held == ConstructParts.Need(sandbox.RackParts[0]).Id, "the rack hands out its first part");
        while (ConstructParts.Get(e.Hand(me).Held)?.Name != "Frame Beam")
            e.TakeFromRack(sandbox, me, rackFeet);
        Want(e.TakeFromRack(sandbox, me, ground + new Vector3(40, 0, 0)) != null, "the rack is refused from far away");
        Want(e.BeginPlaceNew(w, me, eye, ground, 0, now) is { } bare && bare.Contains("tool"), "bare hands can't build");
        e.SetTool(me, ConstructTool.Wrench);
        Want(e.BeginPlaceNew(w, me, eye, ground, 0, now) is { } wrong && wrong.Contains("welder"), "a frame beam needs the welder, not the wrench");
        e.SetTool(me, ConstructTool.Welder);
        Want(e.BeginPlaceNew(w, me, eye, ground, 0, now) == null, "the welder starts placing the beam");
        Wait(1.0);
        Want(w.Bodies.Count == 0, "placing takes time (nothing after 1 s)");
        Wait(2.0);
        var car = w.Bodies.Count == 1 ? w.Bodies[0] : null;
        Want(car != null && e.Hand(me).Held == ConstructParts.None, "the beam is placed after its time and leaves the hand");
        if (car == null)
        {
            Fail($"constructs: scripted build: {string.Join("; ", problems)}");
            return;
        }
        // Each step: take the part, the right tool, attach onto a named socket, wait out its time.
        int Step(string part, int on, string socket, string by, ConstructTool tool, int spin = 0)
        {
            e.Hand(me).Held = ConstructParts.Need(part).Id; // (as if from the rack)
            e.SetTool(me, tool);
            var def = ConstructParts.Need(part);
            var ts = Array.FindIndex(car.DefOf(on).Sockets, s => s.Name == socket);
            var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
            var why = e.BeginAttach(w, me, eye, car.Id, on, ts, ds * 4 + spin, now);
            if (why != null)
            {
                problems.Add($"{part} on {car.DefOf(on).Name} {socket}: {why}");
                return -1;
            }
            var n = car.PartCount;
            Wait(def.AttachSeconds + 0.1);
            return car.PartCount == n + 1 ? n : -1;
        }
        var b = Step("Frame Beam", 0, "x+0", "x-0", ConstructTool.Welder);
        var seat = Step("Seat", 0, "y+2", "bottom", ConstructTool.Wrench);
        // Ghost sockets for a wheel: only the suspensions' axles take one (none yet), then after a suspension, there it is.
        Span<ConstructGhost> ghosts = stackalloc ConstructGhost[ConstructBuild.MaxGhosts];
        var wheelGhostsBare = ConstructBuild.Ghosts(car, ConstructParts.Need("Wheel"), ghosts, car.Com, 10);
        var beamGhosts = ConstructBuild.Ghosts(car, ConstructParts.Need("Frame Block"), ghosts, car.Com, 10);
        var tank = Step("Fuel Tank", b, "y+0", "-y", ConstructTool.Wrench);
        var eng = Step("Engine", b, "y+3", "-y", ConstructTool.Wrench);
        var l1 = Step("Fuel Line", tank, "+z", "-x", ConstructTool.Wrench);
        Step("Fuel Line", l1, "+x", "-x", ConstructTool.Wrench);
        Step("Transmission", eng, "+z", "-z", ConstructTool.Wrench);
        var diff = Step("Differential", 0, "end+", "-z0", ConstructTool.Wrench);
        var s0 = Step("Suspension", diff, "-x", "mount", ConstructTool.Wrench);
        var wheelGhosts = ConstructBuild.Ghosts(car, ConstructParts.Need("Wheel"), ghosts, car.Com, 10);
        var s1 = Step("Suspension", diff, "+x", "mount", ConstructTool.Wrench);
        var p0 = Step("Steering Pivot", 0, "x-0", "base", ConstructTool.Wrench);
        var p1 = Step("Steering Pivot", b, "x+0", "base", ConstructTool.Wrench);
        var s2 = Step("Suspension", p0, "leaf", "mount", ConstructTool.Wrench);
        var s3 = Step("Suspension", p1, "leaf", "mount", ConstructTool.Wrench);
        // The wrong tool for a wheel is refused; then the right one.
        e.Hand(me).Held = ConstructParts.Need("Wheel").Id;
        e.SetTool(me, ConstructTool.Welder);
        Want(e.BeginAttach(w, me, eye, car.Id, s0, 1, 0, now) is { } weld && weld.Contains("wrench"), "a wheel needs the wrench");
        foreach (var s in new[] { s0, s1, s2, s3 })
            Step("Wheel", s, "axle", "axle", ConstructTool.Wrench);
        // A part already taken can't take another; a part in the way is refused.
        e.Hand(me).Held = ConstructParts.Need("Frame Block").Id;
        e.SetTool(me, ConstructTool.Welder);
        var taken = Array.FindIndex(car.DefOf(0).Sockets, s => s.Name == "x+0");
        Want(e.BeginAttach(w, me, eye, car.Id, 0, taken, 0, now) is { } used && used.Contains("already"), "a used socket is refused");
        Wait(0.5);
        Want(car.CanDrive, $"the scripted car drives ({car.DriveProblem})");
        // Removing: the wrench takes a wheel off into the hand (bare hands can't).
        e.Hand(me).Held = ConstructParts.None;
        e.SetTool(me, ConstructTool.None);
        var wheel = PartOf(car, PartRole.Wheel);
        Want(e.BeginRemove(w, me, eye, car.Id, wheel, now) is { } bareOff && bareOff.Contains("tool"), "bare hands can't take a wheel off");
        e.SetTool(me, ConstructTool.Wrench);
        var parts = car.PartCount;
        Want(e.BeginRemove(w, me, eye, car.Id, wheel, now) == null, "the wrench starts taking the wheel off");
        Wait(ConstructParts.Need("Wheel").AttachSeconds * ConstructBuild.RemoveTimeShare + 0.1);
        Want(car.PartCount == parts - 1 && ConstructParts.Get(e.Hand(me).Held)?.Role == PartRole.Wheel, "the wheel came off into the hand");
        // Use with bare hands: sit in the seat (never edits).
        e.SetTool(me, ConstructTool.None);
        Want(e.Use(w, me, eye, car.Id, seat) == null && car.Driver == me, "bare hands can sit in the seat");
        Want(e.Stand(w, me) != null && car.Driver == 255, "and stand up again");
        Line($"scripted build: {car.PartCount} parts, {w.Bodies.Count} body, drives {car.CanDrive}; ghost sockets for a wheel before any suspension {wheelGhostsBare}, after one {wheelGhosts}; for a frame block {beamGhosts}; edits: {e.Done.Count}");
        Want(wheelGhostsBare == 0 && wheelGhosts == 1 && beamGhosts > 10, "ghost sockets: a wheel goes only on a free suspension axle, a block on many frame faces");
        foreach (var p in problems)
            Fail($"constructs: scripted build: {p}");
    }

    private static void ConstructNetRoundTrip(Map yard)
    {
        var server = new ConstructWorld();
        server.Populate(yard.Sandbox!, World.Query);
        StepConstructs(server, 30);
        var gate = server.Bodies.First(b => b.JointCount > 0);
        gate.Joints[0].Open = true;
        gate.Wake();
        var car = server.Bodies.First(b => b.CanDrive);
        server.DamagePart(car, PartOf(car, PartRole.Engine), 40, out _);
        StepConstructs(server, 30);
        var client = new ConstructWorld();
        var w = new PacketWriter(16384);
        var bytes = 0;
        foreach (var c in server.Bodies)
        {
            ConstructNet.WriteFull(w, c);
            bytes += w.Length;
            var r = new PacketReader(w.Span[1..]);
            ConstructNet.Read(ref r, client, 0);
        }
        var cursor = 0;
        uint last = 0;
        ConstructNet.WritePoses(w, server, 100, ref cursor);
        var poseBytes = w.Length;
        var pr = new PacketReader(w.Span[1..]);
        ConstructNet.ReadPoses(ref pr, client, 1.0, ref last);
        ConstructNet.Blend(client, 1.0 + ConstructNet.PoseEvery / (double)World.TickRate, 0);
        var same = client.Bodies.Count == server.Bodies.Count;
        var worst = 0f;
        foreach (var sc in server.Bodies)
        {
            if (client.Get(sc.Id) is not { } cc || cc.PartCount != sc.PartCount || cc.LinkCount != sc.LinkCount || cc.JointCount != sc.JointCount || cc.CanDrive != sc.CanDrive || cc.Anchored != sc.Anchored)
            {
                same = false;
                continue;
            }
            for (var i = 0; i < sc.PartCount; i++)
                if (cc.Parts[i].Def != sc.Parts[i].Def || cc.Parts[i].Pos != sc.Parts[i].Pos || cc.Parts[i].Rot != sc.Parts[i].Rot || MathF.Abs(cc.Parts[i].Health - MathF.Ceiling(sc.Parts[i].Health)) > 0.01f)
                    same = false;
            worst = MathF.Max(worst, Vector3.Distance(cc.Pos, sc.Pos));
            for (var j = 0; j < sc.JointCount; j++)
                worst = MathF.Max(worst, MathF.Abs(cc.Joints[j].Angle - sc.Joints[j].Angle));
        }
        Line($"wire: {server.Bodies.Count} constructs, {server.TotalParts} parts in {bytes} bytes of structure, one pose packet {poseBytes} bytes; the client copy matches {same}, worst pose error {worst:0.0000}");
        if (!same || worst > 0.01f)
            Fail("constructs: a client's copy from the wire doesn't match the server's");
        // A truncated packet is refused, never half-read into the world.
        ConstructNet.WriteFull(w, car);
        var cut = w.Span[1..(w.Length - 5)].ToArray();
        try
        {
            var rr = new PacketReader(cut);
            ConstructNet.Read(ref rr, new ConstructWorld(), 0);
            Fail("constructs: a truncated construct packet was read");
        }
        catch (MalformedPacketException)
        {
        }
    }

    private static void ConstructNoAllocations(Map yard)
    {
        var w = new ConstructWorld();
        w.Populate(yard.Sandbox!, World.Query);
        var car = w.Bodies.First(b => b.CanDrive);
        car.Driver = 1;
        car.DriverSeat = car.Root;
        // Milestone 2's joints and logic busy too: the turret turning, the lift and the motor gate on their way.
        foreach (var b in w.Bodies)
        {
            if (PartNamed(b, "Lever") is var lever and >= 0)
                b.Parts[lever].State = 1;
            if (PartNamed(b, "Button") is var button and >= 0)
                b.Parts[button].Count = 30;
        }
        var writer = new PacketWriter(16384);
        var cursor = 0;
        StepConstructs(w, 60, _ => ConstructEdits.DriveInput(car, Buttons.Forward | Buttons.Left, World.TickDt)); // (warm up: JIT, caches)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 120; t++)
        {
            ConstructEdits.DriveInput(car, Buttons.Forward | Buttons.Left, World.TickDt);
            w.Step(World.Query, World.TickDt);
            ConstructNet.WritePoses(writer, w, (uint)t, ref cursor);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var ms = sw.Elapsed.TotalMilliseconds / 120;
        var moving = w.Bodies.Sum(b => b.Joints.AsSpan(0, b.JointCount).ToArray().Count(j => MathF.Abs(j.Vel) > 0.05f));
        Line($"steady state: {w.Bodies.Count} constructs ({w.Bodies.Count(b => !b.Asleep && !b.Anchored)} awake, {moving} joints moving, {w.TotalWires} wires), {ConstructPhysics.LastContacts} contacts, {ConstructPhysics.LastRays} rays a tick; {bytes} bytes allocated in 120 ticks, {ms:0.000} ms a tick");
        if (bytes > 0)
            Fail($"constructs: {bytes} bytes allocated in 120 ticks of driving (should be none)");
        if (ms > 2.0)
            Fail($"constructs: {ms:0.00} ms a tick for the yard's constructs (budget 2 ms of the 8 ms tick)");
    }
}
