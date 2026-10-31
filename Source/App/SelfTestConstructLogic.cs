using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Constructs milestone 2 (docs/sandbox/CONSTRUCTS.md): the moving joints (a piston lift reaches its travel, holds its
    /// load and sinks when overloaded; a motorised hinge opens and shuts and can't be pushed; a turntable turns both ways
    /// and holds; steering pivots follow the driver inside their limits; a passive hinge's lock), the logic (gates, the
    /// one-tick wires and a NOT wired into itself as a clock, a timer, a delay, a toggle, compare and scale, a lever, an
    /// input reader, a proximity sensor, lamps), the linking tool's rules (bare hands and the wrong tool refused, refused
    /// links give their reason, cutting wires, settings), the drivetrain as parts (a shot transmission or differential
    /// stops the buggy; a wired engine drives itself; a brake part), the wired buggy's horn and lamps, and in a real game a
    /// lift raising a player standing on it, a passenger riding along and a player run over. Short seeded scenarios.
    /// </summary>
    private static void CheckConstructLogic()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"construct logic: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        ConstructM2Refusals();
        ConstructPiston();
        ConstructMotorHinge();
        ConstructTurntable();
        ConstructSteeringAndLock();
        ConstructLogicBench();
        ConstructDrivetrainParts();
        ConstructRiding(yard);
    }

    private static int PartNamed(Construct c, string name, int nth = 0)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Name == name && nth-- == 0)
                return i;
        return -1;
    }

    private static Construct? BuildBlueprint(ConstructWorld w, string name, float x, float z, float yaw = 0)
    {
        var bp = ConstructBuild.FindBlueprint(name);
        var c = bp == null ? null : ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, x, z), yaw, out _);
        if (c == null)
            Fail($"construct logic: the {name} blueprint doesn't build");
        return c;
    }

    /// <summary>Fits a part onto a named socket of part <paramref name="on"/> through the building rules; -1 (and a failure) if it doesn't fit.</summary>
    private static int Fit(ConstructWorld w, Construct c, int on, string socket, string part, string by)
    {
        var def = ConstructParts.Need(part);
        var ts = Array.FindIndex(c.DefOf(on).Sockets, s => s.Name == socket);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        if (ConstructBuild.Place(c, on, ts, def, ds, 0, out var g) is { } why)
        {
            Fail($"construct logic: a {part} doesn't fit on {c.DefOf(on).Name} \"{socket}\": {why}");
            return -1;
        }
        var i = ConstructBuild.Attach(w, c, def, g);
        c.Rebuild();
        return i;
    }

    private static void Wire(ConstructWorld w, Construct c, int from, int to)
    {
        if (ConstructBuild.CanWire(w, c, from, to) is { } why)
            Fail($"construct logic: wiring {c.DefOf(from).Name} -> {c.DefOf(to).Name} refused: {why}");
        else
            w.AddWire(c, from, to);
        c.Rebuild();
    }

    private static void ConstructM2Refusals()
    {
        const string joint = """{ "name": "J", "role": "joint", "tool": "wrench", "mass": 10, "health": 50, "boxes": [ { "size": [0.4, 0.3, 0.4], "side": "base" }, { "size": [0.42, 0.1, 0.42], "at": [0, 0.1, 0], "detail": true, "side": "leaf" } ], "sockets": [ { "name": "base", "at": [0, -0.15, 0], "dir": "-y" }, { "name": "top", "at": [0, 0.15, 0], "dir": "+y" } ], "joint": { "type": "piston", "axis": "+y", "min": 0, "max": 2, "leaf": "top", "speed": 1, "strength": 1000 } }""";
        const string logic = """{ "name": "L", "role": "logic", "tool": "wrench", "mass": 1, "health": 10, "boxes": [ { "size": [0.2, 0.2, 0.2] } ], "sockets": [ { "at": [0, -0.1, 0], "dir": "-y" } ], "logic": { "kind": "timer", "settings": [1, 2] } }""";
        (string Json, string Want)[] bad =
        [
            (joint.Replace("\"piston\"", "\"catapult\""), "\"joint.type\" is one of"),
            (joint.Replace("\"max\": 2", "\"max\": 9"), "travel"),
            (joint.Replace("\"leaf\": \"top\"", "\"leaf\": \"side\""), "joint \"leaf\""),
            (joint.Replace(", \"strength\": 1000", ""), "missing \"strength\""),
            (logic.Replace("\"timer\"", "\"flipflop\""), "\"logic.kind\" is one of"),
            (logic.Replace(", \"settings\": [1, 2]", ""), "needs \"settings\""),
            (logic.Replace("\"timer\"", "\"and\""), "has no setting"),
            (logic.Replace("\"kind\": \"timer\", \"settings\": [1, 2]", "\"kind\": \"reader\", \"labels\": [\"teleport\"]"), "an input reader reads one of"),
            (logic.Replace("\"settings\": [1, 2]", "\"settings\": [1, 2], \"setting\": 5"), "\"logic.setting\""),
            (logic.Replace("\"tool\": \"wrench\"", "\"tool\": \"linker\""), "\"tool\""),
        ];
        var ok = 0;
        foreach (var good in new[] { joint, logic })
            try
            {
                ConstructParts.Parse(good, "good.json");
            }
            catch (ConstructPartException ex)
            {
                Fail($"construct logic: a reference part is refused: {ex.Message}");
            }
        foreach (var (json, want) in bad)
            try
            {
                ConstructParts.Parse(json, "bad.json");
                Fail($"construct logic: a bad part file loaded (wanted a refusal with {want})");
            }
            catch (ConstructPartException ex)
            {
                if (ex.Message.Contains(want, StringComparison.OrdinalIgnoreCase))
                    ok++;
                else
                    Fail($"construct logic: refused for the wrong reason: \"{ex.Message}\" (wanted {want})");
            }
        // A blueprint wire that breaks the linking tool's rules is refused with the reason.
        var bp = ConstructBuild.ParseBlueprint("""{ "name": "Bad", "steps": [ { "part": "Foundation" }, { "part": "Lamp", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Switch", "on": 0, "socket": "top11", "by": "-y" } ], "wires": [[1, 2]] }""", "bad.json");
        if (ConstructBuild.Build(new ConstructWorld(), bp, Vector3.Zero, Quaternion.Identity, out var why) != null || !why.Contains("no output"))
            Fail($"construct logic: a blueprint wire from a lamp (no output) built: \"{why}\"");
        else
            ok++;
        Line($"refusals: {ok} of {bad.Length + 1} bad joint, logic and blueprint files refused with the right reason");
    }

    /// <summary>The Lift: a button sends the deck up its 2 m and holds it there with riders; overloaded, it sinks; back down.</summary>
    private static void ConstructPiston()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Lift", -24, -4) is not { } lift)
            return;
        StepConstructs(w, 10);
        var e = new ConstructEdits();
        var button = PartNamed(lift, "Button", 1); // (the one on the deck)
        var piston = PartNamed(lift, "Piston");
        var j = lift.JointOf(piston);
        var eye = ConstructRender.PartCentre(lift, PartNamed(lift, "Button")) + new Vector3(1.2f, 1.2f, 0);
        var use = e.Use(w, 2, eye, lift.Id, PartNamed(lift, "Button"));
        var top = 0f;
        var reachTick = -1;
        StepConstructs(w, 240, t =>
        {
            // Two players stand on the deck the whole way up (the load the motor holds).
            lift.RiderBits[j + 1] = 3;
            top = MathF.Max(top, lift.Joints[j].Angle);
            if (reachTick < 0 && lift.Joints[j].Angle > lift.Joints[j].Max - 0.02f)
                reachTick = t;
        });
        var held = lift.Joints[j].Angle;
        var load = lift.Joints[j].Load;
        // Overloaded (as if 64 players stood on it): the motor can't hold it, it sinks to the bottom.
        StepConstructs(w, 240, _ => lift.RiderBits[j + 1] = ulong.MaxValue);
        var overloaded = lift.Joints[j].Angle;
        StepConstructs(w, 60); // unloaded again: back up
        var back = lift.Joints[j].Angle;
        e.Use(w, 2, eye, lift.Id, button);
        StepConstructs(w, 240);
        var down = lift.Joints[j].Angle;
        Line($"piston (Lift): use {use ?? "ok"}; up to {held:0.000} m (max {lift.Joints[j].Max:0.0}, highest {top:0.000}) in {reachTick / 60f:0.00} s holding two riders (load {load:0} N, strength {lift.Joints[j].Strength:0}); overloaded it sank to {overloaded:0.00} m, unloaded back to {back:0.00} m; the deck button sent it down to {down:0.000} m");
        if (use != null || MathF.Abs(held - 2f) > 0.02f || top > 2.0001f || reachTick < 0 || reachTick > 200)
            Fail("construct logic: the lift's piston didn't reach and hold its 2 m travel with riders");
        if (overloaded > 0.05f || back < 0.3f)
            Fail("construct logic: an overloaded piston didn't sink (or didn't recover)");
        if (down > 0.02f)
            Fail("construct logic: the lift didn't come back down");
    }

    private static void ConstructMotorHinge()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Motor Gate", -10, -12) is not { } gate)
            return;
        StepConstructs(w, 10);
        var e = new ConstructEdits();
        var button = PartNamed(gate, "Button");
        var door = PartNamed(gate, "Door");
        var eye = ConstructRender.PartCentre(gate, button) + new Vector3(0, 0.3f, -1.4f);
        var bare = e.Use(w, 1, eye, gate.Id, door); // a wired motor hinge: Interact on the door is refused
        e.Use(w, 1, eye, gate.Id, button);
        StepConstructs(w, 150);
        var open = gate.Joints[0].Angle;
        // Walking into the open door doesn't move it (the motor holds it).
        var leaf = ConstructRender.PartCentre(gate, door);
        var s = new MoveState { Position = leaf with { Y = gate.Pos.Y + 0.31f } + new Vector3(0.6f, 0, 0), Velocity = new Vector3(-5, 0, 0), Grounded = true };
        for (var k = 0; k < 20; k++)
        {
            s.Position += new Vector3(-0.05f, 0, 0);
            w.BlockPlayer(ref s, World.PlayerHeight, server: true);
        }
        StepConstructs(w, 2);
        var pushed = gate.Joints[0].Angle;
        e.Use(w, 1, eye, gate.Id, button);
        StepConstructs(w, 150);
        var shut = gate.Joints[0].Angle;
        Line($"motor hinge (Motor Gate): the door by hand: \"{bare}\"; the button opened it to {open * 180 / MathF.PI:0.0} deg (max {gate.Joints[0].Max * 180 / MathF.PI:0}), a walk-in left it at {pushed * 180 / MathF.PI:0.0} deg, the button again shut it to {shut * 180 / MathF.PI:0.0} deg");
        if (bare == null || MathF.Abs(open - gate.Joints[0].Max) > 0.05f || MathF.Abs(pushed - open) > 0.02f || MathF.Abs(shut) > 0.03f)
            Fail("construct logic: the motorised hinge didn't open and shut on its button (or was pushed, or opened by hand)");
    }

    private static void ConstructTurntable()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Turret", -22, -19) is not { } turret)
            return;
        StepConstructs(w, 10);
        var e = new ConstructEdits();
        var lever = PartNamed(turret, "Lever");
        var eye = ConstructRender.PartCentre(turret, lever) + new Vector3(1.4f, 1f, 0);
        e.Use(w, 1, eye, turret.Id, lever); // forward
        StepConstructs(w, 120);
        var a1 = turret.Joints[0].Angle;
        e.Use(w, 1, eye, turret.Id, lever); // middle: hold
        StepConstructs(w, 30);
        var held0 = turret.Joints[0].Angle;
        StepConstructs(w, 60);
        var held1 = turret.Joints[0].Angle;
        e.Use(w, 1, eye, turret.Id, lever); // back
        StepConstructs(w, 120);
        var a2 = turret.Joints[0].Angle;
        var speed = turret.Joints[0].Speed;
        Line($"turntable (Turret): the lever forward turned it to {a1 * 180 / MathF.PI:0} deg in 2 s (speed {speed * 180 / MathF.PI:0} deg/s), the middle held it ({held0 * 180 / MathF.PI:0.0} -> {held1 * 180 / MathF.PI:0.0} deg), back turned it to {a2 * 180 / MathF.PI:0} deg");
        if (MathF.Abs(a1 - speed * 2) > 0.12f || MathF.Abs(held1 - held0) > 0.01f || MathF.Abs(a2 - (held1 - speed * 2)) > 0.12f)
            Fail("construct logic: the turntable didn't turn both ways at its speed and hold in the middle");
    }

    private static void ConstructSteeringAndLock()
    {
        // Steering pivots follow the driver's steering, inside their limits.
        var w = new ConstructWorld();
        if (Buggy(w, -27, 0, MathF.PI / 2) is not { } car)
            return;
        StepConstructs(w, 30);
        car.Driver = 1;
        car.DriverSeat = car.DriveSeat;
        StepConstructs(w, 60, _ => ConstructEdits.DriveInput(car, Buttons.Right, World.TickDt));
        var pivots = 0;
        var worst = 0f;
        var limited = true;
        for (var j = 0; j < car.JointCount; j++)
            if (car.Joints[j].Type == JointType.Steering)
            {
                pivots++;
                worst = MathF.Max(worst, MathF.Abs(car.Joints[j].Angle + car.Steer));
                limited &= car.Joints[j].Angle >= car.Joints[j].Min - 1e-4f && car.Joints[j].Angle <= car.Joints[j].Max + 1e-4f;
            }
        var steering = car.Wheels.AsSpan(0, car.WheelCount).ToArray().Count(x => x.Steers);
        var driven = car.Wheels.AsSpan(0, car.WheelCount).ToArray().Count(x => x.Driven);
        Line($"steering: {pivots} steering pivots, {steering} wheels steer and {driven} are driven; with steer {car.Steer * 180 / MathF.PI:0.0} deg the pivots are within {worst * 180 / MathF.PI:0.00} deg of it, inside their limits {limited}");
        if (pivots != 2 || steering != 2 || driven != 2 || worst > 0.03f || !limited || MathF.Abs(car.Steer) < 0.1f)
            Fail("construct logic: the buggy's steering pivots don't follow the driver (or the wheels on them aren't the steering ones)");

        // A passive hinge's lock: a switch wired into the gate's hinge.
        var w2 = new ConstructWorld();
        if (BuildBlueprint(w2, "Gate", -16, -12) is not { } gate)
            return;
        var sw = Fit(w2, gate, 0, "z+3", "Switch", "-z");
        var hinge = PartNamed(gate, "Hinge");
        Wire(w2, gate, sw, hinge);
        StepConstructs(w2, 10);
        var e = new ConstructEdits();
        var door = PartNamed(gate, "Door");
        var eye = ConstructRender.PartCentre(gate, door) + new Vector3(0, 0.3f, 1.6f);
        e.Use(w2, 1, eye, gate.Id, sw);
        StepConstructs(w2, 3);
        var locked = e.Use(w2, 1, eye, gate.Id, door);
        var s = new MoveState { Position = ConstructRender.PartCentre(gate, door) with { Y = gate.Pos.Y + 0.31f } + new Vector3(0, 0, 0.6f), Velocity = new Vector3(0, 0, -5), Grounded = true };
        for (var k = 0; k < 20; k++)
        {
            s.Position += new Vector3(0, 0, -0.05f);
            w2.BlockPlayer(ref s, World.PlayerHeight, server: true);
        }
        StepConstructs(w2, 20);
        var stayed = gate.Joints[0].Angle;
        e.Use(w2, 1, eye, gate.Id, sw);
        StepConstructs(w2, 3);
        var unlocked = e.Use(w2, 1, eye, gate.Id, door);
        StepConstructs(w2, 60);
        Line($"hinge lock: locked use \"{locked}\", a walk-in left it at {stayed * 180 / MathF.PI:0.0} deg; unlocked use {unlocked ?? "ok"}, opened to {gate.Joints[0].Angle * 180 / MathF.PI:0} deg");
        if (locked == null || !locked.Contains("locked") || MathF.Abs(stayed) > 0.01f || unlocked != null || gate.Joints[0].Angle < 1f)
            Fail("construct logic: a locked hinge opened (or an unlocked one didn't)");
    }

    /// <summary>A bench of logic parts on a foundation: gates, the one-tick wires, a clock, a timer, a delay, a toggle, compare and scale, a reader, a sensor, the linking tool's rules.</summary>
    private static void ConstructLogicBench()
    {
        var w = new ConstructWorld();
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(World.Query, 10, -2), 0) is not { } c)
        {
            Fail("construct logic: no foundation for the logic bench");
            return;
        }
        int F(string socket, string part) => Fit(w, c, 0, socket, part, "-y");
        int S(string socket, string part) => Fit(w, c, 0, socket, part, socket[0] == 'x' ? (socket[1] == '+' ? "-x" : "+x") : (socket[1] == '+' ? "-z" : "+z"));
        var a = F("top00", "Switch");
        var b = F("top01", "Switch");
        var and = F("top02", "AND Gate");
        var or = F("top03", "OR Gate");
        var not = F("top10", "NOT Gate");
        var clock = F("top11", "NOT Gate");
        var timer = F("top12", "Timer");
        var delay = F("top13", "Delay");
        var toggle = F("top20", "Toggle");
        var button = F("top21", "Button");
        var compare = F("top22", "Compare");
        var scale = F("top23", "Scale");
        var lever = F("top30", "Lever");
        var lamp = F("top31", "Lamp");
        var reader = F("top32", "Input Reader");
        var seat = Fit(w, c, 0, "top33", "Seat", "bottom");
        var near = S("x+0", "Proximity Sensor");
        var n1 = S("x+1", "NOT Gate");
        var n2 = S("x+2", "NOT Gate");
        var n3 = S("x+3", "NOT Gate");
        if (Math.Min(Math.Min(a, b), Math.Min(seat, n3)) < 0)
            return;
        // The linking tool's rules (ConstructEdits.Wire: what a player's request goes through).
        var e = new ConstructEdits();
        var eye = c.WorldCom + new Vector3(0, 1.6f, 2.2f);
        var refusals = new List<string>();
        void Refused(string? why, string want, string what)
        {
            refusals.Add($"{what}: \"{why}\"");
            if (why == null || !why.Contains(want, StringComparison.OrdinalIgnoreCase))
                Fail($"construct logic: linking tool: {what} wasn't refused for \"{want}\" (\"{why}\")");
        }
        Refused(e.Wire(w, 1, eye, c.Id, a, and), "linking tool", "bare hands");
        e.SetTool(1, ConstructTool.Wrench);
        Refused(e.Wire(w, 1, eye, c.Id, a, and), "linking tool", "the wrench");
        e.SetTool(1, ConstructTool.Linker);
        Refused(e.Wire(w, 1, eye, c.Id, 0, lamp), "no output", "from a foundation");
        Refused(e.Wire(w, 1, eye, c.Id, a, 0), "takes no input", "into a foundation");
        Refused(e.Wire(w, 1, eye, c.Id, a, reader), "reads a seat", "a switch into an input reader");
        Refused(e.Wire(w, 1, eye, c.Id, a, a), "takes no input", "a switch into itself");
        string? Link(int from, int to) => e.Wire(w, 1, eye, c.Id, from, to);
        var wired = new[]
        {
            Link(a, and), Link(b, and), Link(a, or), Link(b, or), Link(a, not), Link(clock, clock), Link(a, delay), Link(button, toggle),
            Link(toggle, lamp), Link(lever, scale), Link(lever, compare), Link(seat, reader), Link(a, n1), Link(n1, n2), Link(n2, n3),
        };
        foreach (var why in wired)
            if (why != null)
                Fail($"construct logic: linking tool: a good wire refused: {why}");
        Refused(Link(a, and), "already wired", "the same wire twice");
        Refused(Link(b, delay), "at most 1 input", "a second input into a delay");
        Refused(e.Wire(w, 1, eye + new Vector3(30, 0, 0), c.Id, b, lamp), "too far", "from 30 m away");
        e.Hand(1).SeatBody = c.Id;
        Refused(Link(b, lamp), "seat", "while seated");
        e.Hand(1).SeatBody = 0;
        var wires = c.WireCount;
        var cut = e.Unwire(w, 1, eye, c.Id, n3);
        var afterCut = c.WireCount;
        Link(n2, n3);
        var setting = c.Parts[timer].Setting;
        var set = e.CycleSetting(w, 1, eye, c.Id, timer);
        var cycled = c.Parts[timer].Setting;
        e.CycleSetting(w, 1, eye, c.Id, timer);
        e.CycleSetting(w, 1, eye, c.Id, timer);
        e.CycleSetting(w, 1, eye, c.Id, timer); // (round to 1 s again)
        e.SetTool(1, ConstructTool.Wrench);
        Refused(e.CycleSetting(w, 1, eye, c.Id, timer), "linking tool", "a setting with the wrench");
        Line($"linking tool: {wires} wires; refused: {string.Join("; ", refusals)}; cutting the NOT's wires {cut ?? "ok"} ({wires} -> {afterCut}); the timer's setting {setting} -> {cycled} ({set ?? "ok"})");
        if (cut != null || afterCut != wires - 1 || cycled == setting || set != null)
            Fail("construct logic: cutting wires or changing a setting with the linking tool didn't work");

        // The logic, tick by tick.
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        float O(int part) => c.Parts[part].Out;
        StepConstructs(w, 4);
        Want(O(and) == 0 && O(or) == 0 && O(not) == 1 && O(n1) == 1 && O(n2) == 0 && O(n3) == 1, "at rest: AND 0, OR 0, NOT 1, the NOT chain 1 0 1");
        e.SetTool(1, ConstructTool.None);
        e.Use(w, 1, eye, c.Id, a); // switch A on (bare hands: use)
        var chain = new List<string>();
        int n3At = -1, aAt = -1;
        StepConstructs(w, 7, t =>
        {
            chain.Add($"{O(a):0}{O(n1):0}{O(n2):0}{O(n3):0}");
            if (aAt < 0 && O(a) == 1)
                aAt = t;
            if (n3At < 0 && O(n3) == 0)
                n3At = t;
        });
        Want(O(or) == 1 && O(and) == 0 && O(not) == 0, "A on: OR 1, AND 0, NOT 0");
        Want(aAt >= 0 && n3At - aAt == 3, $"a chain of three NOTs takes three ticks (switch, then n1, n2, n3: {string.Join(" ", chain)}; the switch at {aAt}, n3 at {n3At})");
        e.Use(w, 1, eye, c.Id, b);
        StepConstructs(w, 3);
        Want(O(and) == 1, "A and B on: AND 1");
        // The clock: a NOT wired into itself flips every tick.
        var clockSeq = new System.Text.StringBuilder();
        StepConstructs(w, 8, _ => clockSeq.Append(O(clock) >= 0.5f ? '1' : '0'));
        var clockOk = clockSeq.ToString() is "01010101" or "10101010";
        Want(clockOk, $"a NOT on itself is a one-tick clock ({clockSeq})");
        // The timer (1 s): on half the time, flipping every half second.
        var on = 0;
        var flips = 0;
        var last = O(timer);
        StepConstructs(w, 120, _ =>
        {
            on += O(timer) >= 0.5f ? 1 : 0;
            flips += O(timer) != last ? 1 : 0;
            last = O(timer);
        });
        Want(on is >= 58 and <= 62 && flips is >= 3 and <= 5, $"a 1 s timer: on {on} of 120 ticks, {flips} flips");
        // The delay (0.5 s): switch A off, the delay follows half a second later.
        e.Use(w, 1, eye, c.Id, a);
        var delayAt = -1;
        StepConstructs(w, 60, t =>
        {
            if (delayAt < 0 && O(delay) < 0.5f)
                delayAt = t;
        });
        Want(delayAt is >= 29 and <= 33, $"a 0.5 s delay follows its input after {delayAt} ticks");
        // The toggle: a button press turns the lamp on (and it stays on after the button's half second), another off.
        e.Use(w, 1, eye, c.Id, button);
        StepConstructs(w, 5);
        var lampOn = O(lamp);
        StepConstructs(w, 50);
        var lampStill = O(lamp);
        e.Use(w, 1, eye, c.Id, button);
        StepConstructs(w, 5);
        Want(lampOn == 1 && lampStill == 1 && O(lamp) == 0, $"a button through a toggle switches the lamp on and off ({lampOn} {lampStill} {O(lamp)})");
        // The lever: forward 1 (scale x -1: -1; compare over 0.5: on), then back -1.
        e.Use(w, 1, eye, c.Id, lever);
        StepConstructs(w, 3);
        var fwd = (O(lever), O(scale), O(compare));
        e.Use(w, 1, eye, c.Id, lever);
        e.Use(w, 1, eye, c.Id, lever);
        StepConstructs(w, 3);
        var back = (O(lever), O(scale), O(compare));
        Want(fwd == (1f, -1f, 1f) && back == (-1f, 1f, 0f), $"a lever forward and back through a scale (x -1) and a compare (over 0.5): {fwd}, {back}");
        // The input reader (fire): on while the seat's sitter holds fire.
        StepConstructs(w, 3, _ => c.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyFire);
        var reading = O(reader);
        StepConstructs(w, 3, _ => c.Parts[seat].Keys = CPart.Occupied);
        Want(reading == 1 && O(reader) == 0 && O(seat) == 1, $"an input reader on the seat's fire key ({reading}, then {O(reader)}; the seat {O(seat)})");
        // The proximity sensor (6 m): a player 4 m away, then 10 m.
        var at = ConstructRender.PartCentre(c, near);
        w.PlayerSpots[0] = at + new Vector3(4, 0, 0);
        w.PlayerSpotCount = 1;
        StepConstructs(w, 2);
        var close = O(near);
        w.PlayerSpots[0] = at + new Vector3(10, 0, 0);
        StepConstructs(w, 2);
        Want(close == 1 && O(near) == 0, "a proximity sensor (6 m) sees a player 4 m away and not one 10 m away");
        w.PlayerSpotCount = 0;
        Line($"logic bench: {c.PartCount} parts, {c.WireCount} wires; the NOT chain {string.Join(" ", chain)}; the clock {clockSeq}; the 1 s timer on {on} of 120 ticks ({flips} flips); the delay after {delayAt} ticks; the lever {fwd} / {back}");
        foreach (var p in problems)
            Fail($"construct logic: {p}");
    }

    private static void ConstructDrivetrainParts()
    {
        // A shot transmission, then a shot differential: nothing is driven.
        foreach (var (part, word) in new[] { ("Transmission", "transmission"), ("Differential", "differential") })
        {
            var w = new ConstructWorld();
            if (Buggy(w, -27, 0, MathF.PI / 2) is not { } car)
                return;
            StepConstructs(w, 30);
            car.Driver = 1;
            car.DriverSeat = car.DriveSeat;
            var hit = PartNamed(car, part);
            w.DamagePart(car, hit, 10, out var dealt);
            var chipped = car.Parts[hit].Health < ConstructParts.Need(part).Health;
            w.DamagePart(car, hit, 10000, out _);
            var p0 = car.WorldCom;
            StepConstructs(w, 120, _ => ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt));
            var moved = Vector3.Distance(p0, car.WorldCom);
            Line($"a shot {word}: chipped by a rifle round {chipped} ({dealt:0}), then wrecked: drives {car.CanDrive} ({car.DriveProblem}); rolled {moved:0.00} m in 2 s on full throttle");
            if (!chipped || car.CanDrive || !car.DriveProblem.Contains(word) || moved > 0.5f)
                Fail($"construct logic: a shot {word} didn't stop the buggy");
        }
        // A wired engine runs without a driver; a brake part stops it.
        {
            var w = new ConstructWorld();
            if (Buggy(w, -27, 0, MathF.PI / 2) is not { } car)
                return;
            var go = Fit(w, car, 0, "-x1", "Switch", "+x");
            var stop = Fit(w, car, 0, "-x2", "Switch", "+x");
            var brake = Fit(w, car, 2, "+x1", "Brake", "-x"); // (the right rail)
            Wire(w, car, go, car.Engine);
            Wire(w, car, stop, brake);
            StepConstructs(w, 30);
            var p0 = car.WorldCom;
            car.Parts[go].State = 1;
            StepConstructs(w, 120);
            var ran = Vector3.Distance(p0, car.WorldCom);
            var speed = car.Vel.Length();
            car.Parts[stop].State = 1;
            StepConstructs(w, 120);
            var braked = car.Vel.Length();
            Line($"a switch wired into the engine: {ran:0.0} m in 2 s with nobody in the seat ({speed:0.0} m/s); a switch into a brake part stopped it to {braked:0.00} m/s");
            if (ran < 4 || braked > 0.5f)
                Fail("construct logic: a wired engine didn't drive itself (or the brake part didn't stop it)");
        }
        // The wired buggy: the driver's fire key sounds the horn, a tap of aim switches the lamps on (and they stay on).
        {
            var w = new ConstructWorld();
            if (BuildBlueprint(w, "Wired Buggy", -3, 4) is not { } car)
                return;
            StepConstructs(w, 10);
            var horn = PartNamed(car, "Horn");
            var lamp = PartNamed(car, "Lamp");
            var seat = car.DriveSeat;
            StepConstructs(w, 4, _ => car.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyFire);
            var honk = car.Parts[horn].Out;
            StepConstructs(w, 4, _ => car.Parts[seat].Keys = CPart.Occupied);
            var quiet = car.Parts[horn].Out;
            StepConstructs(w, 2, _ => car.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyAim);
            StepConstructs(w, 6, _ => car.Parts[seat].Keys = CPart.Occupied);
            var lit = car.Parts[lamp].Out;
            var seats = car.CountRole(PartRole.Seat);
            Line($"wired buggy: {car.PartCount} parts, {car.WireCount} wires, {seats} seats (the driver's is part {seat}); fire: horn {honk}, let go {quiet}; a tap of aim: lamps {lit}; drives {car.CanDrive}");
            if (honk != 1 || quiet != 0 || lit != 1 || seats != 2 || !car.CanDrive)
                Fail("construct logic: the wired buggy's horn or lamps don't follow the driver's keys");
        }
    }

    /// <summary>In a real (offline) Sandbox game on the yard: the lift raises a player standing on it, a passenger rides along, a player is run over.</summary>
    private static void ConstructRiding(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("construct logic: riding: no local player");
            return;
        }
        me.ImmuneUntil = 0;
        // The lift: stand on the deck, press the deck's button, ride up.
        var lift = g.Constructs.Bodies.FirstOrDefault(b => PartNamed(b, "Piston") >= 0);
        if (lift == null)
        {
            Fail("construct logic: riding: the yard has no lift");
            return;
        }
        var deck = PartNamed(lift, "Deck Plate");
        lift.PartBounds(deck, out var dmn, out var dmx);
        var deckTop = lift.ToWorld(new Vector3((dmn.X + dmx.X) * 0.5f - 0.3f, dmx.Y, (dmn.Z + dmx.Z) * 0.5f - 0.3f));
        me.Teleport(deckTop + new Vector3(0, 0.05f, 0));
        Run(g, ref now, 0.5);
        var y0 = me.Position.Y;
        g.SandboxSend(CActKind.Use, body: lift.Id, part: (byte)PartNamed(lift, "Button", 1));
        Run(g, ref now, 3.5);
        var risen = me.Position.Y - y0;
        var onDeck = Vector2.Distance(new Vector2(me.Position.X, me.Position.Z), new Vector2(deckTop.X, deckTop.Z));
        var piston = lift.Joints[lift.JointOf(PartNamed(lift, "Piston"))].Angle;
        Line($"lift in a game: the player stood on the deck at y {y0:0.00}, pressed its button and rose {risen:0.00} m (the piston {piston:0.00} m), {onDeck:0.00} m from where they stood");
        if (risen < 1.9f || onDeck > 0.3f)
            Fail("construct logic: the lift didn't raise the player standing on it");
        g.SandboxSend(CActKind.Use, body: lift.Id, part: (byte)PartNamed(lift, "Button", 1));
        Run(g, ref now, 3.0);

        // The wired buggy: the local player drives, a second player rides in the passenger seat, a third stands in the way.
        var car = g.Constructs.Bodies.FirstOrDefault(b => b.CountRole(PartRole.Seat) == 2 && b.CanDrive);
        if (car == null)
        {
            Fail("construct logic: riding: the yard has no wired buggy");
            return;
        }
        var passenger = g.SelfTestAddHuman();
        var victim = g.SelfTestAddHuman();
        g.SelfTestRespawn(passenger, now);
        g.SelfTestRespawn(victim, now);
        var other = PartNamed(car, "Seat", 1);
        var seatFeet = ConstructEdits.SeatFeet(car, car.DriveSeat);
        me.Teleport(ConstructWorld.Ground(World.Query, seatFeet.X - 1.6f, seatFeet.Z));
        passenger.Teleport(ConstructWorld.Ground(World.Query, seatFeet.X - 1.6f, seatFeet.Z - 1f));
        // (pretend remote players move only on the inputs they send: feed them still ones every tick)
        uint seq = 0;
        void Tick()
        {
            seq++;
            passenger.InputQueue.Add(new InputCmd { Seq = seq, Yaw = passenger.Yaw });
            victim.InputQueue.Add(new InputCmd { Seq = seq, Yaw = victim.Yaw });
            Run(g, ref now, World.TickDt);
        }
        for (var k = 0; k < 18; k++)
            Tick();
        g.SandboxSend(CActKind.Use, body: car.Id, part: (byte)car.DriveSeat);
        var rideWhy = g.SandboxHands.Use(g.Constructs, passenger.Id, passenger.Eye, car.Id, other);
        var takenWhy = g.SandboxHands.Use(g.Constructs, victim.Id, car.WorldCom + new Vector3(0, 1, 0), car.Id, other);
        // The car faces -Z (north): the victim stands 9 m ahead of it.
        var fwd = Vector3.Transform(car.SeatForward(), car.Rot);
        victim.Teleport(ConstructWorld.Ground(World.Query, car.WorldCom.X + fwd.X * 9, car.WorldCom.Z + fwd.Z * 9));
        victim.ImmuneUntil = 0;
        for (var k = 0; k < 12; k++)
            Tick();
        var hp0 = victim.Health;
        var p0 = car.WorldCom;
        var worst = 0f;
        g.HeldButtons = Buttons.Forward;
        for (var t = 0; t < 120; t++)
        {
            Tick();
            if (g.SandboxHands.Hand(passenger.Id).Seated)
                worst = MathF.Max(worst, Vector3.Distance(passenger.Position, ConstructEdits.SeatFeet(car, other)));
        }
        g.HeldButtons = Buttons.Jump;
        for (var k = 0; k < 60; k++)
            Tick();
        g.HeldButtons = Buttons.None;
        var drove = Vector3.Distance(p0, car.WorldCom);
        var riding = g.SandboxHands.Hand(passenger.Id).Seated && car.SeatOf(passenger.Id) == other;
        var driving = car.Driver == me.Id;
        var hurt = hp0 - Math.Max(0, victim.Health);
        Line($"riding: the driver {driving}, a passenger in the second seat ({rideWhy ?? "ok"}; a third player refused: \"{takenWhy}\") rode {drove:0.0} m within {worst:0.00} m of the seat, still seated {riding}; a player in the way lost {hurt} health ({(victim.Alive ? "alive" : "killed")}); run-over damage at 6, 10 and 16 m/s for a 700 kg car: {Game.RunOverDamage(6, 700)}, {Game.RunOverDamage(10, 700)}, {Game.RunOverDamage(16, 700)}, at 3 m/s {Game.RunOverDamage(3, 700)}");
        if (!driving || rideWhy != null || takenWhy == null || !riding || drove < 8 || worst > 0.5f)
            Fail("construct logic: a passenger didn't ride along in the second seat (or the driver didn't drive)");
        if (hurt <= 0)
            Fail("construct logic: a player in the buggy's way wasn't hurt by it");
        if (Game.RunOverDamage(3, 700) != 0 || Game.RunOverDamage(10, 700) <= Game.RunOverDamage(6, 700) || Game.RunOverDamage(10, 2000) <= Game.RunOverDamage(10, 700))
            Fail("construct logic: run-over damage doesn't grow with speed and mass (or hurts at a walk)");
        g.SandboxSend(CActKind.Stand);
        Run(g, ref now, 0.2);
    }
}
