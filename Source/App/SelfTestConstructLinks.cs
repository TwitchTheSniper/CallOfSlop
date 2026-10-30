using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Constructs milestone 3, links and power (docs/sandbox/CONSTRUCTS.md, "Milestone 3"): bad part files and map
    /// sections refused for the right reason; a buggy towing the trailer on its tow ball (the hitch points stay together,
    /// the trailer follows round a turn); uncoupling and coupling again with the wrench (bare hands refused), and the hitch
    /// shot off; a wrecking ball swinging on a chain that never stretches; a wall button wired into another construct's
    /// gate opens it, the wire's refusals, and the wire breaking when the button goes out of range; the battery (a flat
    /// one stops a motor, the lamps and the starter; the engine's alternator charges it; a shot battery; a map that
    /// requires power); a motor's push tilting a light body (and not with the reactions off); gears changing the speed
    /// and the pull, and a gear selector; the yard's trailer, wall button and lamp post; a client copy of the links and the
    /// power; no allocation per tick. Short seeded scenarios on the Construct Yard.
    /// </summary>
    private static void CheckConstructLinks()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"construct links: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        LinksRefusals();
        LinksTowing();
        LinksDecouple();
        LinksChain();
        LinksCrossWire();
        LinksPower();
        LinksReaction();
        LinksGears();
        LinksYard(yard);
        LinksNet();
        LinksBudget();
    }

    /// <summary>A buggy at (x, z) facing +x with the trailer coupled behind it on its tow ball.</summary>
    private static (Construct Car, Construct Trailer)? Hitched(ConstructWorld w, float x, float z)
    {
        if (Buggy(w, x, z, MathF.PI / 2) is not { } car || BuildBlueprint(w, "Trailer", x - 3.2f, z, MathF.PI / 2) is not { } tr)
            return null;
        var hitch = PartNamed(car, "Tow Hitch");
        var eye = PartNamed(tr, "Tow Eye");
        tr.Pos += ConstructWorld.HitchPoint(car, hitch) - ConstructWorld.HitchPoint(tr, eye);
        tr.PrevPos = tr.Pos;
        if (w.CanCouple(car, hitch, tr, eye) is { } why)
        {
            Fail($"construct links: the trailer doesn't couple to the buggy: {why}");
            return null;
        }
        w.Couple(car, hitch, tr, eye);
        return (car, tr);
    }

    private static float HitchGap(ConstructWorld w)
    {
        if (w.CouplingCount == 0)
            return float.NaN;
        ref var cp = ref w.Couplings[0];
        return w.Get(cp.BodyA) is { } a && w.Get(cp.BodyB) is { } b ? Vector3.Distance(ConstructWorld.HitchPoint(a, cp.PartA), ConstructWorld.HitchPoint(b, cp.PartB)) : float.NaN;
    }

    private static float Tilt(Construct c) => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;

    private static void LinksRefusals()
    {
        const string hitch = """{ "name": "H", "role": "hitch", "tool": "wrench", "mass": 5, "health": 50, "boxes": [ { "size": [0.2, 0.2, 0.2] } ], "sockets": [ { "at": [0, -0.1, 0], "dir": "-y" } ], "hitch": { "kind": "hook", "at": [0, -0.12, 0], "chain": 2 } }""";
        const string battery = """{ "name": "B", "role": "battery", "tool": "wrench", "mass": 5, "health": 50, "boxes": [ { "size": [0.2, 0.2, 0.2] } ], "sockets": [ { "at": [0, -0.1, 0], "dir": "-y" } ], "battery": { "capacity": 100 } }""";
        const string gauge = """{ "name": "G", "role": "logic", "tool": "wrench", "mass": 1, "health": 10, "boxes": [ { "size": [0.2, 0.2, 0.2] } ], "sockets": [ { "at": [0, -0.1, 0], "dir": "-y" } ], "logic": { "kind": "chargegauge" }, "dial": { "at": [0, 0, 0.1], "face": "+z", "max": 1 } }""";
        const string gears = """{ "name": "T", "role": "transmission", "tool": "wrench", "mass": 5, "health": 50, "boxes": [ { "size": [0.2, 0.2, 0.2] } ], "sockets": [ { "at": [0, -0.1, 0], "dir": "-y" } ], "transmission": { "gears": [2, 1, 0.5], "gear": 2 } }""";
        (string Json, string Want)[] bad =
        [
            (hitch.Replace("\"hook\"", "\"magnet\""), "\"hitch.kind\" is one of"),
            (hitch.Replace("\"hook\"", "\"ball\""), "only a hook has a \"chain\""),
            (hitch.Replace(", \"chain\": 2", ""), "a hook needs a \"chain\""),
            (hitch.Replace("\"at\": [0, -0.12, 0]", "\"at\": [0, -3, 0]"), "more than 1.5 m"),
            (battery.Replace("\"capacity\": 100", "\"charge\": 100"), "unknown key \"charge\""),
            (battery.Replace(", \"battery\": { \"capacity\": 100 }", ""), "needs a \"battery\" block"),
            (gauge.Replace("\"role\": \"logic\"", "\"role\": \"frame\"").Replace(", \"logic\": { \"kind\": \"chargegauge\" }", ""), "only a logic part with an output"),
            (gauge.Replace("\"kind\": \"chargegauge\"", "\"kind\": \"lamp\""), "only a logic part with an output"),
            (gears.Replace("\"gear\": 2", "\"gear\": 4"), "\"transmission.gear\""),
            (gears.Replace("[2, 1, 0.5]", "[40, 9]"), "a gear ratio is 0.2 to 20"),
            (gauge.Replace("\"kind\": \"chargegauge\" }", "\"kind\": \"selector\", \"settings\": [1] }").Replace(", \"dial\": { \"at\": [0, 0, 0.1], \"face\": \"+z\", \"max\": 1 }", ""), "how many positions"),
            (battery.Replace("\"health\": 50,", "\"health\": 50, \"power\": -3,"), "\"power\""),
        ];
        var ok = 0;
        foreach (var good in new[] { hitch, battery, gauge, gears })
            try
            {
                ConstructParts.Parse(good, "good.json");
            }
            catch (ConstructPartException ex)
            {
                Fail($"construct links: a reference part is refused: {ex.Message}");
            }
        foreach (var (json, want) in bad)
            try
            {
                ConstructParts.Parse(json, "bad.json");
                Fail($"construct links: a bad part file loaded (wanted a refusal with {want})");
            }
            catch (ConstructPartException ex)
            {
                if (ex.Message.Contains(want, StringComparison.OrdinalIgnoreCase))
                    ok++;
                else
                    Fail($"construct links: refused for the wrong reason: \"{ex.Message}\" (wanted {want})");
            }
        // The map's section: a coupling to a construct that isn't listed, a wire inside one construct, a bad power rule.
        (string Json, string Want)[] maps =
        [
            ("""{ "constructs": [ { "blueprint": "Buggy", "at": [0, 0] } ], "couplings": [ { "hitch": [0, 23], "eye": [1, 8] } ] }""", "not a step of one of the constructs"),
            ("""{ "constructs": [ { "blueprint": "Buggy", "at": [0, 0] } ], "wires": [ { "from": [0, 2], "to": [0, 3] } ] }""", "both ends are on construct 0"),
            ("""{ "power": "sometimes" }""", "\"optional\" or \"required\""),
            ("""{ "constructs": [ { "blueprint": "Buggy", "at": [0, 0] } ], "wires": [ { "from": [0] } ] }""", "[construct index, blueprint step]"),
        ];
        foreach (var (json, want) in maps)
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                SandboxMap.Read(doc.RootElement, "yard.json");
                Fail($"construct links: a bad sandbox section loaded (wanted {want})");
            }
            catch (MapFileException ex)
            {
                if (ex.Message.Contains(want, StringComparison.OrdinalIgnoreCase))
                    ok++;
                else
                    Fail($"construct links: a sandbox section refused for the wrong reason: \"{ex.Message}\" (wanted {want})");
            }
        Line($"refusals: {ok} of {bad.Length + maps.Length} bad hitch, battery, gauge, gear and sandbox-section files refused with the right reason");
    }

    /// <summary>The buggy tows the trailer: straight, then round a turn; the hitch points stay together and nothing goes through anything.</summary>
    private static void LinksTowing()
    {
        var w = new ConstructWorld();
        if (Hitched(w, -24, 0) is not { } pair)
            return;
        var (car, tr) = pair;
        StepConstructs(w, 30);
        car.Driver = 1;
        car.DriverSeat = car.DriveSeat;
        var t0 = tr.WorldCom;
        var worst = 0f;
        StepConstructs(w, 120, _ => // (2 s: the rescaled buggy is quicker and turns wider, and the yard's plateau is to its left)
        {
            ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt);
            worst = MathF.Max(worst, HitchGap(w));
        });
        var straight = Vector3.Distance(t0, tr.WorldCom);
        var behind = Vector3.Dot(tr.WorldCom - car.WorldCom, Vector3.UnitX);
        var speed = car.Vel.Length();
        var yaw0 = YawOf(tr);
        var maxTilt = 0f;
        StepConstructs(w, 120, _ =>
        {
            ConstructEdits.DriveInput(car, Buttons.Forward | Buttons.Left, World.TickDt);
            worst = MathF.Max(worst, HitchGap(w));
            maxTilt = MathF.Max(maxTilt, Tilt(tr));
        });
        var turned = MathF.Abs(World.WrapAngle(YawOf(tr) - yaw0)) * 180 / MathF.PI;
        StepConstructs(w, 90, _ => ConstructEdits.DriveInput(car, Buttons.Jump, World.TickDt));
        Line($"towing: the trailer ({tr.Mass:0} kg) came {straight:0.0} m in 2 s behind the buggy ({behind:0.0} m behind, the buggy at {speed:0.0} m/s), then turned {turned:0} deg with it (tilting at most {maxTilt:0.0} deg); the hitch points at most {worst * 100:0.0} cm apart; braked: {car.Vel.Length():0.00} and {tr.Vel.Length():0.00} m/s");
        if (straight < 9 || behind > -2 || worst > 0.12f || turned < 25 || maxTilt > 25 || tr.Vel.Length() > 0.6f)
            Fail("construct links: the trailer didn't follow the buggy on its tow ball (or came apart, or rolled over, or didn't stop with it)");
    }

    /// <summary>Uncoupling and coupling again with the wrench; bare hands refused; the hitch shot off lets the trailer go.</summary>
    private static void LinksDecouple()
    {
        var w = new ConstructWorld();
        if (Hitched(w, -24, 8) is not { } pair)
            return;
        var (car, tr) = pair;
        StepConstructs(w, 30);
        var e = new ConstructEdits();
        var hitch = PartNamed(car, "Tow Hitch");
        var eye = ConstructRender.PartCentre(car, hitch) + new Vector3(0, 1.2f, 1.2f);
        var bare = e.Use(w, 3, eye, car.Id, hitch);
        e.SetTool(3, ConstructTool.Wrench);
        var off = e.Use(w, 3, eye, car.Id, hitch);
        var left = w.CouplingCount;
        car.Driver = 1;
        car.DriverSeat = car.DriveSeat;
        var t0 = tr.WorldCom;
        StepConstructs(w, 90, _ => ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt));
        var stayed = Vector3.Distance(t0, tr.WorldCom);
        var tooFar = e.Use(w, 3, ConstructRender.PartCentre(car, PartNamed(car, "Tow Hitch")) + new Vector3(0, 1.2f, 0), car.Id, PartNamed(car, "Tow Hitch"));
        // Back up to it: put the buggy back where the eye meets the ball, couple, and tow it away again.
        StepConstructs(w, 60, _ => ConstructEdits.DriveInput(car, Buttons.Jump, World.TickDt));
        car.Driver = 255;
        hitch = PartNamed(car, "Tow Hitch");
        var back = ConstructWorld.HitchPoint(tr, PartNamed(tr, "Tow Eye")) - ConstructWorld.HitchPoint(car, hitch);
        car.Pos += new Vector3(back.X + 0.3f, 0, back.Z); // (the trailer rests on its nose: the ball a little above and ahead of the eye)
        car.PrevPos = car.Pos;
        car.Wake();
        StepConstructs(w, 20);
        var on = e.Use(w, 3, ConstructRender.PartCentre(car, hitch) + new Vector3(0, 1.2f, 1.2f), car.Id, hitch);
        var again = w.CouplingCount;
        StepConstructs(w, 30);
        var pulled = HitchGap(w);
        // Shot off: the hitch breaks away and the coupling goes with it.
        w.DamagePart(car, hitch, 100000, out _);
        StepConstructs(w, 2);
        var gone = w.CouplingCount;
        Line($"uncoupling: bare hands \"{bare}\"; the wrench uncoupled it ({left} couplings left) and the buggy drove off without it (it moved {stayed:0.00} m); "
             + $"nothing in reach: \"{tooFar}\"; backed up and coupled again: {on ?? "ok"} ({again} coupling, the points {pulled * 100:0.0} cm apart after 0.5 s); the hitch shot off: {gone} couplings");
        if (bare == null || !bare.Contains("wrench") || off != null || left != 0 || stayed > 0.5f || tooFar == null || on != null || again != 1 || pulled > 0.08f || gone != 0)
            Fail("construct links: coupling and uncoupling with the wrench (or the hitch shot off) went wrong");
    }

    /// <summary>A wrecking ball on a chain hook: it swings, never further from the hook than the chain, and doesn't fall.</summary>
    private static void LinksChain()
    {
        var w = new ConstructWorld();
        var q = World.Query;
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, 14, -4), 0) is not { } crane)
            return;
        var b1 = Fit(w, crane, 0, "top11", "Frame Block", "-y");
        var b2 = Fit(w, crane, b1, "+y", "Frame Block", "-y");
        var b3 = Fit(w, crane, b2, "+y", "Frame Block", "-y");
        var top = Fit(w, crane, b3, "+y", "Frame Block", "-y");
        for (var k = 0; k < 5 && top >= 0; k++)
            top = Fit(w, crane, top, "+y", "Frame Block", "-y"); // (a post 4.5 m tall: the chain and the ball hang clear of the ground)
        var arm = Fit(w, crane, top, "+x", "Frame Beam", "x-1");
        if (arm < 0)
            return;
        var hook = Fit(w, crane, arm, "y-3", "Chain Hook", "+y");
        var hp = ConstructWorld.HitchPoint(crane, hook);
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Wrecking Ball"), hp - new Vector3(0, 2.4f + 0.91f, 0), 0) is not { } ball) // (hanging just inside the chain's reach)
            return;
        var refused = w.CanCouple(ball, 0, ball, 0);
        if (w.CanCouple(crane, hook, ball, 0) is { } why)
        {
            Fail($"construct links: the wrecking ball doesn't couple to the chain hook: {why}");
            return;
        }
        w.Couple(crane, hook, ball, 0);
        StepConstructs(w, 60);
        var hang = HitchGap(w);
        ball.Vel = new Vector3(4.5f, 0, 0);
        ball.Wake();
        float longest = 0, far = 0, low = float.MaxValue;
        var x0 = hp.X;
        StepConstructs(w, 240, _ =>
        {
            longest = MathF.Max(longest, HitchGap(w));
            far = MathF.Max(far, MathF.Abs(ball.WorldCom.X - x0));
            low = MathF.Min(low, ball.WorldCom.Y);
        });
        var ground = ConstructWorld.Ground(q, hp.X, hp.Z).Y;
        Line($"chain: the ball ({ball.Mass:0} kg) hangs {hang:0.00} m below the hook (chain {w.Couplings[0].Length:0.0} m, from the ground: {hp.Y - ground:0.0} m); pushed at 4.5 m/s it swung out {far:0.0} m, the chain at most {longest:0.00} m, "
             + $"the ball at least {low - ground:0.00} m off the ground; a ball on itself: \"{refused}\"");
        if (longest > w.Couplings[0].Length + 0.08f || far < 1f || refused == null || hang < w.Couplings[0].Length - 0.1f || low - ground < 0.3f)
            Fail("construct links: the wrecking ball on its chain didn't hang and swing within the chain's length");
    }

    /// <summary>A wall button wired into another construct's gate: the press opens it; the wire's refusals; out of range it breaks.</summary>
    private static void LinksCrossWire()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Motor Gate", -10, -12) is not { } gate || BuildBlueprint(w, "Wall Button", -6, -14) is not { } wall)
            return;
        StepConstructs(w, 10);
        var e = new ConstructEdits();
        var button = PartNamed(wall, "Button");
        var toggle = PartNamed(gate, "Toggle");
        var reader = -1;
        var eye = (ConstructRender.PartCentre(wall, button) + ConstructRender.PartCentre(gate, toggle)) * 0.5f + new Vector3(0, 1, 0);
        var bare = e.Wire(w, 4, eye, wall.Id, button, toggle, gate.Id);
        e.SetTool(4, ConstructTool.Linker);
        var lamp = PartNamed(gate, "Charge Gauge");
        var noInput = e.Wire(w, 4, eye, wall.Id, button, lamp, gate.Id);
        var made = e.Wire(w, 4, eye, wall.Id, button, toggle, gate.Id);
        var twice = e.Wire(w, 4, eye, wall.Id, button, toggle, gate.Id);
        _ = reader;
        var door = gate.JointOf(PartNamed(gate, "Motor Hinge"));
        var shut = gate.Joints[door].Angle;
        var press = e.Use(w, 4, ConstructRender.PartCentre(wall, button) + new Vector3(0, 0.5f, -1.2f), wall.Id, button);
        StepConstructs(w, 150);
        var open = MathF.Abs(gate.Joints[door].Angle);
        // Out of range: the wall button carried 20 m away (an anchored post, moved by hand), the wire breaks; the door stays where it was.
        wall.Pos += new Vector3(0, 0, -16);
        wall.PrevPos = wall.Pos;
        StepConstructs(w, 2);
        var after = w.XWireCount;
        Line($"cross wire: bare hands \"{bare}\"; into a gauge \"{noInput}\"; made {made ?? "ok"}, again \"{twice}\"; the wall button's press {press ?? "ok"} opened the Motor Gate from {shut * 57.3f:0} to {open * 57.3f:0} deg; "
             + $"the button carried 16 m away: {after} wires left");
        if (bare == null || !bare.Contains("linking tool") || noInput == null || made != null || twice == null || press != null || open < 1.2f || after != 0)
            Fail("construct links: a wire from a wall button into another construct's gate didn't work (or didn't break out of range)");
    }

    /// <summary>The battery: a flat one stops the motor, the lamps and the starter; the alternator charges it; shot, power goes; a map that requires power.</summary>
    private static void LinksPower()
    {
        // The Motor Gate's motor on a flat battery: the button doesn't open it.
        {
            var w = new ConstructWorld();
            if (BuildBlueprint(w, "Motor Gate", -10, -12) is not { } gate)
                return;
            StepConstructs(w, 10);
            var full = gate.Charge;
            gate.Charge = 0;
            StepConstructs(w, 2);
            var e = new ConstructEdits();
            var button = PartNamed(gate, "Button");
            e.Use(w, 1, ConstructRender.PartCentre(gate, button) + new Vector3(0, 0.5f, -1.2f), gate.Id, button);
            var door = gate.JointOf(PartNamed(gate, "Motor Hinge"));
            StepConstructs(w, 120);
            var dead = MathF.Abs(gate.Joints[door].Angle);
            var power = gate.PowerOn;
            gate.Charge = full;
            StepConstructs(w, 2);
            e.Use(w, 1, ConstructRender.PartCentre(gate, button) + new Vector3(0, 0.5f, -1.2f), gate.Id, button);
            var drawn = 0f;
            StepConstructs(w, 120, _ => drawn = MathF.Max(drawn, gate.PowerUse));
            var live = MathF.Abs(gate.Joints[door].Angle);
            Line($"power: the Motor Gate (battery {full:0} kJ, electrical {gate.Electrical}) flat: power {power}, the button left the door at {dead * 57.3f:0} deg; charged: it opened to {live * 57.3f:0} deg, drawing up to {drawn:0} W ({full - gate.Charge:0.0} kJ)");
            if (power || dead > 0.15f || live < 1.2f || drawn < 50)
                Fail("construct links: a flat battery didn't stop the Motor Gate's motor (or a charged one didn't run it)");
        }
        // The wired buggy: the lamps on a flat battery; the starter; the alternator charges; a shot battery.
        {
            var w = new ConstructWorld();
            if (BuildBlueprint(w, "Wired Buggy", -24, 0, MathF.PI / 2) is not { } car)
                return;
            StepConstructs(w, 10);
            var seat = car.DriveSeat;
            var lamp = PartNamed(car, "Lamp");
            StepConstructs(w, 2, _ => car.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyAim); // (the toggle: lamps on)
            StepConstructs(w, 6, _ => car.Parts[seat].Keys = CPart.Occupied);
            var lit = car.Parts[lamp].Out;
            car.Charge = 0;
            StepConstructs(w, 4, _ => car.Parts[seat].Keys = CPart.Occupied);
            var flatLamp = car.Parts[lamp].Out;
            car.Driver = 1;
            car.DriverSeat = seat;
            var p0 = car.WorldCom;
            StepConstructs(w, 60, _ => ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt));
            var flatMoved = Vector3.Distance(p0, car.WorldCom);
            var failed = car.StarterFailed;
            // A little charge: enough for the starter; the alternator charges it as it drives.
            car.Charge = ConstructPower.StarterKJ + 0.5f;
            StepConstructs(w, 2, _ => ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt));
            var started = car.EngineRunning;
            var low = car.Charge;
            StepConstructs(w, 180, _ => ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt));
            var charged = car.Charge;
            var moved = Vector3.Distance(p0, car.WorldCom);
            // Shot: the battery is wrecked; no charge, no power.
            w.DamagePart(car, PartNamed(car, "Battery"), 100000, out _);
            StepConstructs(w, 3, _ => car.Parts[seat].Keys = CPart.Occupied);
            var shotLamp = car.Parts[lamp].Out;
            Line($"power: the wired buggy's lamps on {lit:0} with charge, {flatLamp:0} flat; flat, the starter failed {failed} and it moved {flatMoved:0.00} m; with {low:0.0} kJ it started ({started}) and drove {moved:0} m, "
                 + $"the alternator ({car.AlternatorRate:0.0} kW) charging it to {charged:0.0} kJ in 3 s; the battery shot: power {car.PowerOn}, lamps {shotLamp:0}");
            if (lit < 0.5f || flatLamp > 0.1f || !failed || flatMoved > 0.3f || !started || charged < low + 2f || moved < 10 || car.PowerOn || shotLamp > 0.1f)
                Fail("construct links: the wired buggy's battery (lamps, starter, alternator, a shot battery) didn't work as it should");
        }
        // A map that requires power: a buggy with no battery can't start; the default leaves it running as before.
        {
            var w = new ConstructWorld { PowerRequired = true };
            if (Buggy(w, -24, 8, MathF.PI / 2) is not { } car)
                return;
            StepConstructs(w, 20);
            car.Driver = 1;
            car.DriverSeat = car.DriveSeat;
            var p0 = car.WorldCom;
            StepConstructs(w, 90, _ => ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt));
            var moved = Vector3.Distance(p0, car.WorldCom);
            Line($"power required by the map: a buggy with no battery is electrical {car.Electrical}, the starter failed {car.StarterFailed}, moved {moved:0.00} m");
            if (!car.Electrical || !car.StarterFailed || moved > 0.3f)
                Fail("construct links: a map requiring power let a buggy with no battery drive");
        }
    }

    /// <summary>
    /// A motor's push turns the body it is on back (with the reactions off it doesn't): a turntable under a block hanging
    /// from the chain hook swings its arm round, and the hanging block turns the other way.
    /// </summary>
    private static void LinksReaction()
    {
        var turned = new float[2];
        var arm = new float[2];
        for (var run = 0; run < 2; run++)
        {
            ConstructPhysics.JointReactions = run == 0;
            var w = new ConstructWorld();
            var q = World.Query;
            if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, 14, 4), 0) is not { } crane)
                break;
            var top = Fit(w, crane, 0, "top11", "Frame Block", "-y");
            for (var k = 0; k < 11 && top >= 0; k++)
                top = Fit(w, crane, top, "+y", "Frame Block", "-y"); // (6.3 m: the turret hangs clear of the ground)
            var beam = top < 0 ? -1 : Fit(w, crane, top, "+x", "Frame Beam", "end-"); // (the jib straight out 2 m: the swinging beam clears the post)
            var hook = beam < 0 ? -1 : Fit(w, crane, beam, "y-3", "Chain Hook", "+y");
            if (hook < 0)
                break;
            var hp = ConstructWorld.HitchPoint(crane, hook);
            // The hanging body: a block with an eye on top and a turntable under it turning a beam (the turret, upside down).
            if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Block"), hp - new Vector3(0, 2.4f + 0.95f, 0), 0) is not { } c)
                break;
            var eye = Fit(w, c, 0, "+y", "Tow Eye", "+z");
            var table = eye < 0 ? -1 : Fit(w, c, 0, "-y", "Turntable", "base");
            var swung = table < 0 ? -1 : Fit(w, c, table, "top", "Frame Beam", "y-1");
            var sw = swung < 0 ? -1 : Fit(w, c, 0, "+x", "Switch", "-x");
            if (sw < 0 || w.CanCouple(crane, hook, c, eye) is { } why && Fail2($"construct links: the hanging turret doesn't couple: {why}"))
            {
                Fail("construct links: the hanging turret (a block, an eye, a turntable, a beam) doesn't build");
                break;
            }
            w.Couple(crane, hook, c, eye);
            Wire(w, c, sw, table);
            StepConstructs(w, 60);
            var yaw0 = YawOf(c);
            var j0 = c.Joints[0].Angle;
            c.Parts[sw].State = 1;
            StepConstructs(w, 90);
            turned[run] = World.WrapAngle(YawOf(c) - yaw0) * 180 / MathF.PI;
            arm[run] = World.WrapAngle(c.Joints[0].Angle - j0) * 180 / MathF.PI;
        }
        ConstructPhysics.JointReactions = true;
        Line($"joint reaction: a turntable under a block hanging on the chain turned its beam {arm[0]:0} deg in 1.5 s and the block turned {turned[0]:0} deg the other way; with the reactions off it turned {turned[1]:0} deg");
        if (MathF.Abs(turned[0]) < 10 || MathF.Sign(turned[0]) == MathF.Sign(arm[0]) || MathF.Abs(turned[1]) > 2)
            Fail("construct links: a motor's push didn't turn the body it is on back");
    }

    private static bool Fail2(string s)
    {
        Fail(s);
        return false;
    }

    /// <summary>Gears: a low gear pulls harder and tops out slower than a high one; a gear selector wired into the transmission picks it.</summary>
    private static void LinksGears()
    {
        var early = new float[3];
        var late = new float[3];
        for (var g = 0; g < 3; g += 2)
        {
            var w = new ConstructWorld();
            if (Buggy(w, -27, 0, MathF.PI / 2) is not { } car)
                return;
            StepConstructs(w, 30);
            car.Parts[car.Transmission].Setting = (byte)g;
            car.Driver = 1;
            car.DriverSeat = car.DriveSeat;
            var t = 0;
            StepConstructs(w, 270, _ =>
            {
                ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt);
                if (++t == 30)
                    early[g] = car.Vel.Length();
            });
            late[g] = car.Vel.Length();
        }
        var top1 = 0f;
        {
            var w = new ConstructWorld();
            if (Buggy(w, -27, 0, MathF.PI / 2) is { } car)
                top1 = ConstructDrive.TopSpeed(car, ConstructParts.Need("Transmission").Settings[0]);
        }
        // A gear selector wired in: each press steps the gear on.
        var picked = new int[3];
        {
            var w = new ConstructWorld();
            if (Buggy(w, -27, 8, MathF.PI / 2) is not { } car)
                return;
            var sel = Fit(w, car, 0, "x-1", "Gear Selector", "+x");
            Wire(w, car, sel, car.Transmission);
            var e = new ConstructEdits();
            var eye = ConstructRender.PartCentre(car, sel) + new Vector3(0, 1, -1);
            for (var k = 0; k < 3; k++)
            {
                StepConstructs(w, 3);
                picked[k] = car.Gear + 1;
                e.Use(w, 2, eye, car.Id, sel);
            }
        }
        Line($"gears: after 0.5 s gear 1 {early[0]:0.0} m/s, gear 3 {early[2]:0.0}; after 4.5 s gear 1 {late[0]:0.0} (its top {top1:0.0}), gear 3 {late[2]:0.0}; a gear selector wired in picked gears {string.Join(", ", picked)}");
        if (early[0] <= early[2] || late[2] <= late[0] + 2 || late[0] > top1 + 0.5f || picked[0] != 1 || picked[1] != 2 || picked[2] != 3)
            Fail("construct links: the gears didn't change the buggy's pull and top speed (or the selector didn't pick them)");
    }

    /// <summary>The yard as the map sets it up: the trailer on the buggy, the wall button wired into the Motor Gate, the lamp post on its battery.</summary>
    private static void LinksYard(Map yard)
    {
        var w = new ConstructWorld();
        w.Populate(yard.Sandbox!, World.Query);
        StepConstructs(w, 60);
        var trailer = w.Bodies.FirstOrDefault(b => b.PartCount > 0 && PartNamed(b, "Tow Eye") >= 0);
        var post = w.Bodies.FirstOrDefault(b => PartNamed(b, "Proximity Sensor") >= 0 && PartNamed(b, "Battery") >= 0);
        var gap = HitchGap(w);
        var lampLit = -1f;
        if (post != null)
        {
            w.PlayerSpotCount = 1;
            w.PlayerSpots[0] = ConstructRender.PartCentre(post, 0) + new Vector3(2, 0, 0);
            StepConstructs(w, 5, _ =>
            {
                w.PlayerSpotCount = 1;
                w.PlayerSpots[0] = ConstructRender.PartCentre(post, 0) + new Vector3(2, 0, 0);
            });
            lampLit = post.Parts[PartNamed(post, "Lamp")].Out;
        }
        Line($"the yard: {w.Bodies.Count} constructs, {w.CouplingCount} coupling (the trailer {(trailer != null ? $"{gap * 100:0.0} cm off its ball, asleep {trailer.Asleep}" : "missing")}), {w.XWireCount} wire between constructs, "
             + $"the lamp post {(post != null ? $"lit {lampLit:0} with someone near (charge {ConstructPower.Share(post) * 100:0}%)" : "missing")}");
        if (trailer == null || w.CouplingCount != 1 || gap > 0.05f || w.XWireCount != 1 || post == null || lampLit < 0.5f || !post.Electrical)
            Fail("construct links: the yard's trailer, wall button wire or lamp post isn't set up");
    }

    /// <summary>A client's copy: the links and the power come over the wire; a truncated links packet is refused.</summary>
    private static void LinksNet()
    {
        var server = new ConstructWorld();
        if (Hitched(server, -24, 0) is not { } pair || BuildBlueprint(server, "Wall Button", -6, -14) is not { } wall || BuildBlueprint(server, "Motor Gate", -10, -12) is not { } gate
            || BuildBlueprint(server, "Wired Buggy", -3, 12) is not { } wired)
            return;
        var car = pair.Car;
        server.AddXWire(wall, PartNamed(wall, "Button"), gate, PartNamed(gate, "Toggle"));
        StepConstructs(server, 5);
        wired.Charge *= 0.5f;
        wired.Parts[wired.Transmission].Setting = 2;
        StepConstructs(server, 2);
        var client = new ConstructWorld();
        var w = new PacketWriter(16384);
        foreach (var b in server.Bodies)
        {
            ConstructNet.WriteFull(w, b);
            Read(w, client);
        }
        ConstructNet.WriteLinks(w, server);
        var links = w.Span.ToArray();
        Read(w, client);
        var cursor = 0;
        foreach (var b in server.Bodies)
            b.Wake();
        if (ConstructNet.WritePoses(w, server, 10, ref cursor))
        {
            var r = new PacketReader(w.Span[1..]);
            uint last = 0;
            ConstructNet.ReadPoses(ref r, client, 0, ref last);
        }
        var cw = client.Get(wired.Id);
        var truncated = false;
        try
        {
            var r = new PacketReader(links.AsSpan(1, links.Length - 4));
            ConstructNet.Read(ref r, new ConstructWorld(), 0);
        }
        catch (Exception ex) when (ex is MalformedPacketException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            truncated = true;
        }
        var same = client.CouplingCount == 1 && client.Couplings[0].BodyA == car.Id && client.XWireCount == 1 && client.XWires[0].BodyTo == gate.Id && client.XWires[0].To == PartNamed(gate, "Toggle");
        Line($"network: the client has {client.CouplingCount} coupling and {client.XWireCount} wire between constructs (as the server: {same}); the wired buggy electrical {cw?.Electrical}, charge {(cw != null ? ConstructPower.Share(cw) * 100 : 0):0}% (server {ConstructPower.Share(wired) * 100:0}%), gear {cw?.Gear + 1}; a truncated links packet refused {truncated}");
        if (!same || cw == null || !cw.Electrical || MathF.Abs(ConstructPower.Share(cw) - ConstructPower.Share(wired)) > 0.01f || cw.Gear != 2 || !truncated)
            Fail("construct links: a client's copy of the links or the power doesn't match the server");

        static void Read(PacketWriter w, ConstructWorld into)
        {
            var r = new PacketReader(w.Span[1..]);
            ConstructNet.Read(ref r, into, 0);
        }
    }

    /// <summary>No allocation per tick with everything going: towing, a chain, a cross wire, the lamps on a battery, the poses and the links on the wire.</summary>
    private static void LinksBudget()
    {
        var w = new ConstructWorld();
        if (Hitched(w, -24, 0) is not { } pair || BuildBlueprint(w, "Wall Button", -6, -14) is not { } wall || BuildBlueprint(w, "Motor Gate", -10, -12) is not { } gate
            || BuildBlueprint(w, "Wired Buggy", -3, 12, MathF.PI / 2) is not { } wired || BuildBlueprint(w, "Lamp Post", -3, -9) is not { } post)
            return;
        w.AddXWire(wall, PartNamed(wall, "Button"), gate, PartNamed(gate, "Toggle"));
        var car = pair.Car;
        car.Driver = 1;
        car.DriverSeat = car.DriveSeat;
        wired.Driver = 2;
        wired.DriverSeat = wired.DriveSeat;
        var writer = new PacketWriter(16384);
        var cursor = 0;
        void Tick(int t)
        {
            ConstructEdits.DriveInput(car, Buttons.Forward | (t % 120 < 60 ? Buttons.Left : Buttons.Right), World.TickDt);
            ConstructEdits.DriveInput(wired, Buttons.Forward, World.TickDt);
            wired.Parts[wired.DriveSeat].Keys = (ushort)(CPart.Occupied | (t % 40 < 20 ? ConstructParts.KeyFire : 0));
            w.PlayerSpotCount = 1;
            w.PlayerSpots[0] = ConstructRender.PartCentre(post, 0) + new Vector3(1, 0, 0);
            if (t % 50 == 0)
                wall.Parts[PartNamed(wall, "Button")].Count = 30;
            w.Step(World.Query, World.TickDt);
            ConstructNet.WritePoses(writer, w, (uint)t, ref cursor);
            ConstructNet.WriteLinks(writer, w);
            w.Events.Clear();
        }
        for (var t = 0; t < 60; t++)
            Tick(t); // (warm up: the engines start, first-time paths run)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 60; t < 180; t++)
            Tick(t);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var ms = sw.Elapsed.TotalMilliseconds / 120;
        Line($"budget: {w.Bodies.Count} constructs ({w.CouplingCount} coupling, {w.XWireCount} cross wire, {w.Bodies.Count(b => b.Electrical)} electrical), {ConstructPhysics.LastContacts} contacts a tick; {bytes} bytes allocated in 120 ticks, {ms:0.000} ms a tick");
        if (bytes > 0)
            Fail($"construct links: {bytes} bytes allocated in 120 ticks (should be none)");
        if (ms > 2.5)
            Fail($"construct links: {ms:0.00} ms a tick (budget 2.5 ms of the 8 ms tick)");
    }
}
