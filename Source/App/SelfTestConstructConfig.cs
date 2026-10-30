using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct configure" (the configure tool; docs/sandbox/CONSTRUCTS.md, "The configure tool"): every configurable part
    /// declares a valid schema (and the parts that should have settings do), bad schemas are refused with a reason; values
    /// clamp to their range; each setting changes behaviour (a timer's period and duty change its pulses, a delay's mode,
    /// a compare's operator, a scale's offset and limits, a button latched, a speed sensor's units and threshold, a
    /// proximity sensor's range, a piston's travel, a motor hinge's limit and speed, a brake's strength); the server's
    /// checks (the tool, the reach, one kind at a time, someone else's construct refused, "defended" while its owner is
    /// near); settings round-trip through blueprints and the construct message; copy and paste between parts of a kind and
    /// a shift-selection changed together in the panel, drawn headless with no allocation while open; and over the real
    /// transport a client's change reaches the server and comes back to it, and a refusal comes back as a notice.
    /// </summary>
    private static void CheckConstructConfig()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"configure: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        ConfigSchemas();
        ConfigClamp();
        ConfigLogicBehaviour();
        ConfigJointBehaviour();
        ConfigServerChecks();
        ConfigRoundTrip();
        ConfigPanel(yard);
        ConfigOverWire(yard);
    }

    private static void ConfigSchemas()
    {
        var problems = new List<string>();
        var configurable = 0;
        foreach (var d in ConstructParts.All)
        {
            if (!d.Configurable)
                continue;
            configurable++;
            if (d.Config.Length > ConstructConfig.MaxSlots)
                problems.Add($"{d.Name}: {d.Config.Length} settings");
            for (var s = 0; s < d.Config.Length; s++)
            {
                var e = d.Config[s];
                if (d.CfgSlot[(int)e.Id] != s)
                    problems.Add($"{d.Name} {e.Key}: its slot lookup is wrong");
                if (e.Label.Length == 0 || e.Group.Length == 0 || e.UiId.Length == 0 || e.TooltipText.Length == 0)
                    problems.Add($"{d.Name} {e.Key}: no label, group or tooltip");
                if (e.Type == CfgType.Number && (!(e.Max > e.Min) || e.Default < e.Min || e.Default > e.Max || e.Format.Length == 0))
                    problems.Add($"{d.Name} {e.Key}: range {e.Min} to {e.Max}, default {e.Default}");
                if (e.Type is CfgType.Choice or CfgType.Key && (e.Options.Length == 0 || e.Default < 0 || e.Default >= e.Options.Length))
                    problems.Add($"{d.Name} {e.Key}: a choice of {e.Options.Length} defaulting to {e.Default}");
                if (e.Bound && e.Options.Length != d.Settings.Length)
                    problems.Add($"{d.Name} {e.Key}: bound to a setting of {d.Settings.Length} with {e.Options.Length} options");
                foreach (var pv in e.Presets)
                    if (e.Clamp(pv) - pv is var off && MathF.Abs(off) > 1e-3f)
                        problems.Add($"{d.Name} {e.Key}: preset {pv} isn't on its range and step");
            }
            // A new part's values are the defaults, and the defaults are what it did before it had settings.
            var p = new CPart { Def = d.Id, Setting = d.DefaultSetting };
            ConstructConfig.Init(ref p, d);
            if (!ConstructConfig.AllDefault(p, d))
                problems.Add($"{d.Name}: a new part isn't at its defaults");
            if (d.HasJoint && ConstructConfig.Has(d, CfgKey.Speed) && MathF.Abs(ConstructConfig.Get(p, d, CfgKey.Speed, 0) * (d.JointType == JointType.Piston ? 1 : MathF.PI / 180) - d.MotorSpeed) > 1e-3f)
                problems.Add($"{d.Name}: its default speed isn't the part's own");
        }
        string[] want = ["Timer", "Delay", "Compare", "Scale", "Proximity Sensor", "Speed Sensor", "Button", "Switch", "Toggle", "Input Reader", "Lamp", "Horn", "Brake", "Piston", "Motor Hinge", "Turntable", "Hinge", "Suspension", "Gear Selector"];
        var missing = want.Where(n => ConstructParts.Find(n) is not { Configurable: true }).ToArray();
        // Bad schemas are refused with a reason.
        const string head = """{ "name": "T", "role": "logic", "tool": "wrench", "mass": 3, "health": 40, "boxes": [ { "size": [0.2, 0.2, 0.2] } ], "sockets": [ { "name": "-y", "at": [0, -0.1, 0], "dir": "-y" } ], """;
        string Refusal(string tail)
        {
            try
            {
                ConstructParts.Parse(head + tail + " }", "t.json");
                return "accepted";
            }
            catch (ConstructPartException ex)
            {
                return ex.Message;
            }
        }
        var bad = new (string Tail, string Want)[]
        {
            ("\"logic\": { \"kind\": \"lamp\" }, \"config\": [ { \"key\": \"wattage\", \"min\": 0, \"max\": 1 } ]", "\"key\" is one of"),
            ("\"logic\": { \"kind\": \"lamp\" }, \"config\": [ { \"key\": \"brightness\", \"min\": 0, \"max\": 1, \"default\": 5 } ]", "outside"),
            ("\"logic\": { \"kind\": \"lamp\" }, \"config\": [ { \"key\": \"period\", \"min\": 0, \"max\": 1, \"default\": 0.5 } ]", "doesn't use"),
            ("\"logic\": { \"kind\": \"lamp\" }, \"config\": [ { \"key\": \"blink\", \"min\": 1, \"max\": 0, \"default\": 0 } ]", "more than"),
            ("\"logic\": { \"kind\": \"delay\", \"settings\": [1] }, \"config\": [ { \"key\": \"mode\", \"options\": [\"a\"] } ]", "options"),
            ("\"logic\": { \"kind\": \"lamp\" }, \"config\": [ { \"key\": \"blink\", \"min\": 0, \"max\": 1, \"default\": 0 }, { \"key\": \"blink\", \"min\": 0, \"max\": 1, \"default\": 0 } ]", "twice"),
        };
        var refusals = bad.Select(b => (b.Want, Got: Refusal(b.Tail))).ToArray();
        foreach (var (w, got) in refusals)
            if (!got.Contains(w))
                problems.Add($"a bad schema wasn't refused for \"{w}\": \"{got}\"");
        var good = Refusal("\"logic\": { \"kind\": \"timer\", \"settings\": [0.5, 1] }"); // (no "config": the presets become a period setting)
        var auto = ConstructParts.Parse(head + "\"logic\": { \"kind\": \"timer\", \"settings\": [0.5, 1], \"setting\": 1 } }", "t.json");
        Line($"configure schemas: {configurable} configurable parts; missing {(missing.Length == 0 ? "none" : string.Join(", ", missing))}; refused: {string.Join("; ", refusals.Select(r => r.Got))}; "
             + $"a timer without \"config\" {(good == "accepted" ? $"gets a {auto.Config[0].Key} of {auto.Config[0].Default} s with {auto.Config[0].Presets.Length} presets" : good)}");
        if (missing.Length > 0 || problems.Count > 0 || good != "accepted" || auto.Config.Length != 1 || auto.Config[0].Id != CfgKey.Period || auto.Config[0].Default != 1)
            Fail($"configure: schemas: {string.Join("; ", problems.Take(8))}{(missing.Length > 0 ? $"; no settings on {string.Join(", ", missing)}" : "")}");
    }

    private static void ConfigClamp()
    {
        var timer = ConstructParts.Need("Timer");
        var lamp = ConstructParts.Need("Lamp");
        var reader = ConstructParts.Need("Input Reader");
        var p = new CPart { Def = timer.Id, Setting = timer.DefaultSetting };
        ConstructConfig.Init(ref p, timer);
        var period = timer.CfgSlot[(int)CfgKey.Period];
        ConstructConfig.Set(ref p, timer, period, 999);
        var high = ConstructConfig.Get(p, timer, CfgKey.Period, 0);
        ConstructConfig.Set(ref p, timer, period, -5);
        var low = ConstructConfig.Get(p, timer, CfgKey.Period, 0);
        ConstructConfig.Set(ref p, timer, period, 1.234f);
        var stepped = ConstructConfig.Get(p, timer, CfgKey.Period, 0);
        ConstructConfig.Set(ref p, timer, period, float.NaN);
        var nan = ConstructConfig.Get(p, timer, CfgKey.Period, 0);
        var startOn = timer.CfgSlot[(int)CfgKey.StartOn];
        ConstructConfig.Set(ref p, timer, startOn, 0.7f);
        var toggle = ConstructConfig.Get(p, timer, CfgKey.StartOn, 0);
        var lp = new CPart { Def = lamp.Id };
        ConstructConfig.Init(ref lp, lamp);
        ConstructConfig.Set(ref lp, lamp, lamp.CfgSlot[(int)CfgKey.Colour], 1e9f);
        var colour = ConstructConfig.Get(lp, lamp, CfgKey.Colour, 0);
        var rp = new CPart { Def = reader.Id };
        ConstructConfig.Init(ref rp, reader);
        ConstructConfig.Set(ref rp, reader, reader.CfgSlot[(int)CfgKey.Setting], 40.6f);
        var key = rp.Setting;
        Line($"configure clamp: a timer's period 999 -> {high}, -5 -> {low}, 1.234 -> {stepped:0.###} (0.05 s steps), NaN -> {nan} (the default); start on 0.7 -> {toggle}; a lamp's colour 1e9 -> #{(int)colour:x6}; a reader's key 40.6 -> {key} of {reader.Settings.Length}");
        if (high != 60 || low != 0.05f || MathF.Abs(stepped - 1.25f) > 1e-4f || nan != 1 || toggle != 1 || (int)colour != 0xFFFFFF || key != reader.Settings.Length - 1)
            Fail("configure: values don't clamp to their range, step and type");
    }

    /// <summary>A bench of logic parts on a foundation (logic stepped directly: no physics).</summary>
    private static void ConfigLogicBehaviour()
    {
        var w = new ConstructWorld();
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(World.Query, 14, 6), 0) is not { } c)
        {
            Fail("configure: no foundation for the bench");
            return;
        }
        int F(string socket, string part) => Fit(w, c, 0, socket, part, "-y");
        var timer = F("top00", "Timer");
        var sw = F("top01", "Switch");
        var delay = F("top02", "Delay");
        var compare = F("top03", "Compare");
        var scale = F("top10", "Scale");
        var button = F("top11", "Button");
        var speed = F("top12", "Speed Sensor");
        var near = F("top13", "Proximity Sensor");
        var brake = F("top20", "Brake");
        if (Math.Min(timer, brake) < 0)
            return;
        Wire(w, c, sw, delay);
        Wire(w, c, sw, compare);
        Wire(w, c, sw, scale);
        Wire(w, c, sw, brake);
        void Set(int part, CfgKey k, float v) => ConstructConfig.Set(ref c.Parts[part], c.DefOf(part), c.DefOf(part).CfgSlot[(int)k], v);
        void Step(int n)
        {
            for (var t = 0; t < n; t++)
                ConstructLogic.Step(w, c, World.TickDt);
        }
        (int Rises, int OnTicks) Pulses(int ticks)
        {
            var rises = 0;
            var on = 0;
            var was = c.Parts[timer].Out >= 0.5f;
            for (var t = 0; t < ticks; t++)
            {
                ConstructLogic.Step(w, c, World.TickDt);
                var now = c.Parts[timer].Out >= 0.5f;
                if (now && !was)
                    rises++;
                if (now)
                    on++;
                was = now;
            }
            return (rises, on);
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        // The timer: 1 s by default; 0.5 s twice the pulses; duty 25% a quarter of the time on.
        var p1 = Pulses(600);
        Set(timer, CfgKey.Period, 0.5f);
        var p05 = Pulses(600);
        Set(timer, CfgKey.Duty, 25);
        var pd = Pulses(600);
        Want(Math.Abs(p1.Rises - 10) <= 1 && Math.Abs(p05.Rises - 20) <= 1, $"timer pulses {p1.Rises} at 1 s, {p05.Rises} at 0.5 s");
        Want(MathF.Abs(pd.OnTicks / 600f - 0.25f) < 0.04f && MathF.Abs(p1.OnTicks / 600f - 0.5f) < 0.04f, $"timer on {p1.OnTicks}/600 at 50%, {pd.OnTicks}/600 at 25%");
        // The delay: both ways by default; "switching on" lets it go off at once.
        Set(delay, CfgKey.Seconds, 0.5f);
        c.Parts[sw].State = 1;
        Step(10);
        var delayedOn = c.Parts[delay].Out;
        Step(40);
        var onAfter = c.Parts[delay].Out;
        c.Parts[sw].State = 0;
        Step(5);
        var bothOff = c.Parts[delay].Out; // (still on: the off waits too)
        Step(40);
        Set(delay, CfgKey.Mode, 1); // switching on only
        c.Parts[sw].State = 1;
        Step(50);
        c.Parts[sw].State = 0;
        Step(3);
        var onlyOnOff = c.Parts[delay].Out;
        Want(delayedOn == 0 && onAfter == 1 && bothOff == 1 && onlyOnOff == 0, $"delay: on after 10 ticks {delayedOn}, after 50 {onAfter}; off after 5 (both) {bothOff}; off after 3 (switching on only) {onlyOnOff}");
        // Compare: "less than" 0.5 is on while the switch is off; the scale's offset and highest output.
        Set(compare, CfgKey.Operator, 4);
        Set(compare, CfgKey.Threshold, 0.5f);
        Set(scale, CfgKey.Factor, 3);
        Set(scale, CfgKey.Offset, 1);
        Set(scale, CfgKey.High, 2.5f);
        c.Parts[sw].State = 0;
        Step(3);
        var cmpOff = c.Parts[compare].Out;
        var scOff = c.Parts[scale].Out;
        c.Parts[sw].State = 1;
        Step(3);
        var cmpOn = c.Parts[compare].Out;
        var scOn = c.Parts[scale].Out;
        Want(cmpOff == 1 && cmpOn == 0 && MathF.Abs(scOff - 1) < 1e-4f && MathF.Abs(scOn - 2.5f) < 1e-4f, $"compare (<) {cmpOff}/{cmpOn}, scale (x3 +1, at most 2.5) {scOff}/{scOn}");
        // The brake's strength: a half-strength brake sends half its signal to the wheels.
        Set(brake, CfgKey.Strength, 50);
        Step(2);
        var brakeHalf = c.BrakeSignal;
        // A latched button: one press on, the next off; unlatched with a hold of 1 s.
        var e = new ConstructEdits();
        var eye = ConstructRender.PartCentre(c, button) + new Vector3(0, 0.8f, 1f);
        Set(button, CfgKey.Latched, 1);
        e.Use(w, 1, eye, c.Id, button);
        Step(90);
        var latchedOn = c.Parts[button].Out;
        e.Use(w, 1, eye, c.Id, button);
        Step(2);
        var latchedOff = c.Parts[button].Out;
        Set(button, CfgKey.Latched, 0);
        Set(button, CfgKey.Hold, 1);
        e.Use(w, 1, eye, c.Id, button);
        Step(50);
        var held = c.Parts[button].Out;
        Step(20);
        var released = c.Parts[button].Out;
        Want(brakeHalf == 0.5f && latchedOn == 1 && latchedOff == 0 && held == 1 && released == 0, $"brake at 50% {brakeHalf}; latched button {latchedOn}/{latchedOff}; held 1 s {held} then {released}");
        // The speed sensor's units and threshold; the proximity sensor's range.
        c.Vel = new Vector3(10, 0, 0);
        Set(speed, CfgKey.Units, 1);
        ConstructLogic.Step(w, c, World.TickDt);
        var kmh = c.Parts[speed].Out;
        Set(speed, CfgKey.Threshold, 40);
        ConstructLogic.Step(w, c, World.TickDt);
        var over = c.Parts[speed].Out;
        Set(speed, CfgKey.Threshold, 40);
        Set(speed, CfgKey.Units, 0); // (10 m/s is under 40)
        ConstructLogic.Step(w, c, World.TickDt);
        var under = c.Parts[speed].Out;
        c.Vel = Vector3.Zero;
        w.PlayerSpots[0] = ConstructRender.PartCentre(c, near) + new Vector3(9, 0, 0);
        w.PlayerSpotCount = 1;
        ConstructLogic.Step(w, c, World.TickDt);
        var at6 = c.Parts[near].Out;
        Set(near, CfgKey.Radius, 10);
        ConstructLogic.Step(w, c, World.TickDt);
        var at10 = c.Parts[near].Out;
        w.PlayerSpotCount = 0;
        Want(MathF.Abs(kmh - 36) < 0.01f && over == 0 && under == 0, $"speed sensor: 10 m/s in km/h {kmh}; over 40 km/h {over}; over 40 m/s {under}");
        Want(at6 == 0 && at10 == 1, $"proximity at 9 m: range 6 m {at6}, range 10 m {at10}");
        Line($"configure logic: timer {p1.Rises} pulses in 10 s at 1 s, {p05.Rises} at 0.5 s, on {pd.OnTicks * 100 / 600}% at a 25% duty; delay on/off/mode {delayedOn}{onAfter}{bothOff}{onlyOnOff}; "
             + $"compare < {cmpOff}{cmpOn}; scale {scOff}/{scOn}; brake {brakeHalf}; button {latchedOn}{latchedOff}{held}{released}; speed {kmh:0.#} km/h; proximity {at6}{at10}");
        if (problems.Count > 0)
            Fail($"configure: a setting didn't change behaviour: {string.Join("; ", problems)}");
        // Over 40 km/h it is on: 10 m/s is 36 km/h, so a 30 km/h threshold turns it on.
        c.Vel = new Vector3(10, 0, 0);
        Set(speed, CfgKey.Units, 1);
        Set(speed, CfgKey.Threshold, 30);
        ConstructLogic.Step(w, c, World.TickDt);
        if (c.Parts[speed].Out != 1)
            Fail("configure: a speed sensor's threshold doesn't turn it on");
    }

    private static void ConfigJointBehaviour()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Lift", -24, -4) is not { } lift || BuildBlueprint(w, "Motor Gate", -10, -12) is not { } gate)
            return;
        StepConstructs(w, 10);
        var e = new ConstructEdits();
        e.SetTool(2, ConstructTool.Configure);
        var piston = PartNamed(lift, "Piston");
        var pj = lift.JointOf(piston);
        var button = PartNamed(lift, "Button");
        var eye = ConstructRender.PartCentre(lift, button) + new Vector3(1.2f, 1.2f, 0);
        var pd = lift.DefOf(piston);
        byte[] slots = [(byte)pd.CfgSlot[(int)CfgKey.Max]];
        var set = e.Configure(w, 2, ConstructRender.PartCentre(lift, piston) + new Vector3(1.5f, 1f, 0), lift.Id, [(byte)piston], slots, [1.0f], null);
        e.Use(w, 2, eye, lift.Id, button);
        StepConstructs(w, 240);
        var travel = lift.Joints[pj].Angle;
        // The motor hinge: its far limit at 45 degrees and twice its speed.
        var hinge = PartNamed(gate, "Motor Hinge");
        var hj = gate.JointOf(hinge);
        var hd = gate.DefOf(hinge);
        var heye = ConstructRender.PartCentre(gate, hinge) + new Vector3(0, 0.5f, -1.5f);
        var slow = 0;
        e.Use(w, 2, heye, gate.Id, PartNamed(gate, "Button"));
        StepConstructs(w, 300, t =>
        {
            if (slow == 0 && gate.Joints[hj].Angle > 1.2f)
                slow = t;
        });
        var full = gate.Joints[hj].Angle;
        e.Use(w, 2, heye, gate.Id, PartNamed(gate, "Button"));
        StepConstructs(w, 300);
        var set2 = e.Configure(w, 2, heye, gate.Id, [(byte)hinge], [(byte)hd.CfgSlot[(int)CfgKey.Max], (byte)hd.CfgSlot[(int)CfgKey.Speed]], [45, 180], null);
        var fast = 0;
        e.Use(w, 2, heye, gate.Id, PartNamed(gate, "Button"));
        StepConstructs(w, 300, t =>
        {
            if (fast == 0 && gate.Joints[hj].Angle > 0.7f)
                fast = t;
        });
        var limited = gate.Joints[hj].Angle;
        Line($"configure joints: the lift's piston set to 1 m ({set ?? "ok"}) went up to {travel:0.000} m (its own travel 2 m); the motor gate's hinge opened to {full * 180 / MathF.PI:0} deg, "
             + $"set to 45 deg at 180 deg/s ({set2 ?? "ok"}) to {limited * 180 / MathF.PI:0.0} deg (0.7 rad after {fast} ticks; it took {slow} to 1.2 rad at 90 deg/s)");
        if (set != null || MathF.Abs(travel - 1f) > 0.03f)
            Fail("configure: a piston's travel setting didn't change how far it goes");
        if (set2 != null || MathF.Abs(limited - MathF.PI / 4) > 0.03f || full < 1.5f || fast <= 0 || fast > 30)
            Fail("configure: a motor hinge's limit and speed settings didn't change how it opens");
    }

    private static void ConfigServerChecks()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Wired Buggy", 20, -20) is not { } car)
            return;
        var e = new ConstructEdits();
        var lamp = PartNamed(car, "Lamp");
        var lamp2 = PartNamed(car, "Lamp", 1);
        var horn = PartNamed(car, "Horn");
        var ld = car.DefOf(lamp);
        var eye = ConstructRender.PartCentre(car, lamp) + new Vector3(0, 1, 2);
        var colour = (byte)ld.CfgSlot[(int)CfgKey.Colour];
        var refusals = new List<string>();
        void Refused(string? why, string want, string what)
        {
            refusals.Add($"{what}: \"{why}\"");
            if (why == null || !why.Contains(want, StringComparison.OrdinalIgnoreCase))
                Fail($"configure: {what} wasn't refused for \"{want}\" (\"{why}\")");
        }
        Refused(e.Configure(w, 3, eye, car.Id, [(byte)lamp], [colour], [0xFF0000], null), "configure tool", "the wrong tool");
        e.SetTool(3, ConstructTool.Configure);
        Refused(e.Configure(w, 3, eye + new Vector3(30, 0, 0), car.Id, [(byte)lamp], [colour], [0xFF0000], null), "too far", "from 30 m");
        Refused(e.Configure(w, 3, eye, car.Id, [(byte)lamp, (byte)horn], [colour], [0xFF0000], null), "same kind", "a lamp and a horn");
        Refused(e.Configure(w, 3, eye, car.Id, [(byte)lamp], [9], [1], null), "no setting", "slot 9");
        car.Owner = 7;
        Refused(e.Configure(w, 3, eye, car.Id, [(byte)lamp], [colour], [0xFF0000], ConstructConfig.Refusal(car, 3, "Bob", true)), "defended", "someone else's, its owner near");
        var notNear = ConstructConfig.Refusal(car, 3, "Bob", false);
        var mine = ConstructConfig.Refusal(car, 7, "Bob", true);
        car.Owner = 255;
        var nobody = ConstructConfig.Refusal(car, 3, "Bob", true);
        // Both lamps at once.
        var both = e.Configure(w, 3, eye, car.Id, [(byte)lamp, (byte)lamp2], [colour], [0x20FF40], null);
        var c1 = ConstructConfig.Get(car.Parts[lamp], ld, CfgKey.Colour, 0);
        var c2 = ConstructConfig.Get(car.Parts[lamp2], ld, CfgKey.Colour, 0);
        var dirty = w.StructureDirty.Contains(car.Id);
        Line($"configure server: refused {string.Join("; ", refusals)}; an owner away: \"{notNear}\"; the owner {(mine == null ? "allowed" : mine)}; nobody's {(nobody == null ? "allowed" : nobody)}; both lamps at once {both ?? "ok"} (#{(int)c1:x6}, #{(int)c2:x6}, structure resent {dirty})");
        if (notNear == null || !notNear.Contains("Owned by Bob") || mine != null || nobody != null || both != null || (int)c1 != 0x20FF40 || (int)c2 != 0x20FF40 || !dirty)
            Fail("configure: the server's permission checks or a change to several parts went wrong");
    }

    private static void ConfigRoundTrip()
    {
        // A blueprint with settings.
        const string json = """
            { "format": "call-of-slop-construct-blueprint/1", "name": "Config Test", "steps": [
              { "part": "Foundation" },
              { "part": "Timer", "on": 0, "socket": "top00", "by": "-y", "config": { "period": 2.5, "duty": 30, "startOn": true } },
              { "part": "Lamp", "on": 0, "socket": "top01", "by": "-y", "config": { "colour": "#ff2010", "blink": 2 } },
              { "part": "Input Reader", "on": 0, "socket": "top02", "by": "-y", "config": { "setting": "jump" } }
            ] }
            """;
        string bad;
        try
        {
            ConstructBuild.ParseBlueprint(json.Replace("\"blink\": 2", "\"wattage\": 2"), "bad.json");
            bad = "accepted";
        }
        catch (ConstructPartException ex)
        {
            bad = ex.Message;
        }
        var bp = ConstructBuild.ParseBlueprint(json, "test.json");
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 18, 10), 0, out var why);
        if (c == null)
        {
            Fail($"configure: the settings blueprint doesn't build: {why}");
            return;
        }
        var timer = PartNamed(c, "Timer");
        var lamp = PartNamed(c, "Lamp");
        var reader = PartNamed(c, "Input Reader");
        var td = c.DefOf(timer);
        var rd = c.DefOf(reader);
        var period = ConstructConfig.Get(c.Parts[timer], td, CfgKey.Period, 0);
        var readerKey = rd.SettingLabels[c.Parts[reader].Setting];
        var lampJson = ConstructConfig.ToJson(c.Parts[lamp], c.DefOf(lamp));
        var timerJson = ConstructConfig.ToJson(c.Parts[timer], td);
        // ...and back: a step written from the parts builds the same.
        var again = ConstructBuild.ParseBlueprint(json.Replace("{ \"period\": 2.5, \"duty\": 30, \"startOn\": true }", timerJson).Replace("{ \"colour\": \"#ff2010\", \"blink\": 2 }", lampJson), "again.json");
        var c2 = ConstructBuild.BuildOnGround(w, again, ConstructWorld.Ground(World.Query, 24, 10), 0, out _);
        var same = c2 != null;
        for (var i = 0; same && i < c.PartCount; i++)
            for (var s = 0; s < c.DefOf(i).Config.Length; s++)
                same &= ConstructConfig.At(c.Parts[i], c.DefOf(i), s) == ConstructConfig.At(c2!.Parts[i], c2.DefOf(i), s);
        // The network: the construct message to a client's world carries every setting.
        var pw = new PacketWriter(4096);
        ConstructNet.WriteFull(pw, c);
        var client = new ConstructWorld();
        var r = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref r, client, 0);
        var copy = client.Get(c.Id);
        var net = copy != null;
        for (var i = 0; net && i < c.PartCount; i++)
            for (var s = 0; s < c.DefOf(i).Config.Length; s++)
                net &= ConstructConfig.At(c.Parts[i], c.DefOf(i), s) == ConstructConfig.At(copy!.Parts[i], copy.DefOf(i), s);
        Line($"configure round trip: blueprint period {period} s, reader key {readerKey}, lamp {lampJson}, timer {timerJson}; written back and built again the same {same}; through the construct message the same {net} ({pw.Length} bytes); "
             + $"an unknown setting refused: \"{bad}\"");
        if (period != 2.5f || readerKey != "jump" || !lampJson.Contains("#ff2010") || !same || !net || !bad.Contains("no setting \"wattage\""))
            Fail("configure: settings don't round-trip through blueprints and the construct message");
    }

    /// <summary>The panel headless in an offline game: values sent as the host, copy and paste between lamps, reset, a selection, no allocation per frame while open.</summary>
    private static void ConfigPanel(Map yard)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("configure panel: no local player");
            return;
        }
        var before = g.Constructs.Bodies.Count;
        g.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Wired Buggy"));
        Run(g, ref now, 0.5);
        if (g.Constructs.Bodies.Count != before + 1)
        {
            Fail($"configure panel: the Wired Buggy didn't spawn ({g.SandboxNotice})");
            return;
        }
        var car = g.Constructs.Bodies[^1];
        var lamp = PartNamed(car, "Lamp");
        var lamp2 = PartNamed(car, "Lamp", 1);
        var ld = car.DefOf(lamp);
        var colour = ld.CfgSlot[(int)CfgKey.Colour];
        var blink = ld.CfgSlot[(int)CfgKey.Blink];
        me.Teleport(ConstructRender.PartCentre(car, lamp) + new Vector3(0, -0.3f, 2.5f));
        Run(g, ref now, 0.2);
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
        var ui = new Ui();
        ui.InitHeadless(new float2(1600, 900));
        string? error = null;
        void Frames(int n)
        {
            for (var i = 0; i < n && error == null; i++)
            {
                try
                {
                    ui.BeginFrame();
                    ui.DrawSandboxConfig(g, now);
                    ui.EndFrame();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
        }
        ui.OpenSandboxConfig(car, lamp);
        Frames(3);
        ui.SandboxConfigSet(g, colour, 0xFF0000, now);
        ui.SandboxConfigSet(g, blink, 3, now);
        Frames(2);
        var sent = (int)ConstructConfig.Get(car.Parts[lamp], ld, CfgKey.Colour, 0) == 0xFF0000 && ConstructConfig.Get(car.Parts[lamp], ld, CfgKey.Blink, 0) == 3;
        var preview = ConstructRender.PreviewBody == car && ConstructRender.PreviewPart == lamp && (int)ConstructRender.PreviewCfg[colour] == 0xFF0000;
        ui.SandboxConfigCopy();
        ui.OpenSandboxConfig(car, lamp2);
        Frames(2);
        var pasted = ui.SandboxConfigPaste(g, now);
        Frames(2);
        var copied = (int)ConstructConfig.Get(car.Parts[lamp2], ld, CfgKey.Colour, 0) == 0xFF0000 && ConstructConfig.Get(car.Parts[lamp2], ld, CfgKey.Blink, 0) == 3;
        // Paste refused onto another kind.
        var horn = PartNamed(car, "Horn");
        ui.OpenSandboxConfig(car, horn);
        Frames(1);
        var wrongKind = ui.SandboxConfigPaste(g, now);
        // Reset the second lamp; a selection of both changed together.
        ui.OpenSandboxConfig(car, lamp2);
        Frames(1);
        ui.SandboxConfigReset(g, now);
        Frames(1);
        var reset = ConstructConfig.AllDefault(car.Parts[lamp2], ld);
        ui.SandboxConfigClearSelection();
        ui.SandboxConfigSelect(car, lamp);
        ui.SandboxConfigSelect(car, lamp2);
        ui.OpenSandboxConfig(car, lamp);
        Frames(1);
        ui.SandboxConfigSet(g, blink, 5, now);
        Frames(1);
        var together = ConstructConfig.Get(car.Parts[lamp], ld, CfgKey.Blink, 0) == 5 && ConstructConfig.Get(car.Parts[lamp2], ld, CfgKey.Blink, 0) == 5;
        // Steady frames while open: nothing allocated (least of three rounds after a warm-up).
        Frames(40);
        Thread.Sleep(200);
        Frames(20);
        var least = long.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            Frames(60);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - b0);
        }
        ConfigWindow(ui, car, lamp, lamp2, Frames);
        // The tool put away: the program closes the panel (the server refuses a change meanwhile).
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        ui.SandboxConfigSet(g, blink, 1, now);
        var noTool = g.SandboxNotice;
        ui.CloseSandboxConfig();
        Line($"configure panel: drawn headless (error {error ?? "none"}); red with a 3 Hz blink sent {sent}, previewed {preview}; copied and pasted onto the other lamp {pasted}/{copied}; "
             + $"pasted onto a horn {wrongKind}; reset {reset}; both lamps selected changed together {together}; {least} bytes allocated in 60 open frames; with the tool away: \"{noTool}\"");
        if (error != null || !sent || !preview || !pasted || !copied || wrongKind || !reset || !together || !noTool.Contains("configure tool"))
            Fail("configure: the panel doesn't set, copy, paste, reset or apply to a selection");
        if (least > 0)
            Fail($"configure: the open panel allocates ({least} bytes in 60 frames)");
    }

    /// <summary>
    /// The panel is a plain window (board dev-configure-no-dim-cd02): open, it doesn't dim the scene (the pause menu still
    /// does); dragged by its title bar with the headless mouse it moves, and the next panel opens where it was left; dragged
    /// past the screen's edges it stays fully on the screen; snapped back, it is beside the part again.
    /// </summary>
    private static void ConfigWindow(Ui ui, Construct car, int lamp, int lamp2, Action<int> frames)
    {
        var io = ImGui.GetIO();
        void Mouse(float2 at, bool down, int n)
        {
            io.AddMousePosEvent(at.X, at.Y);
            io.AddMouseButtonEvent(0, down);
            frames(n);
        }
        // Drags the window by a point on its title bar to a place on the screen; returns where it ended up.
        float2 Drag(float2 to)
        {
            var (p, _, _) = ui.SandboxConfigWindow;
            var grab = new float2(p.X + 60, p.Y + 6);
            Mouse(grab, false, 2);
            Mouse(grab, true, 2);
            Mouse(new float2((grab.X + to.X) * 0.5f, (grab.Y + to.Y) * 0.5f), true, 2);
            Mouse(to, true, 2);
            Mouse(to, false, 2);
            return ui.SandboxConfigWindow.Pos;
        }
        ui.OpenSandboxConfig(car, lamp);
        frames(2);
        var (home, size, movedAtOpen) = ui.SandboxConfigWindow;
        ui.MenuOpen = false; // (deployed: the loadout menu is shut, as in a match)
        var noDim = ui.SceneDim(false, false);
        ui.PauseOpen = true;
        var pauseDim = ui.SceneDim(false, false);
        ui.PauseOpen = false;
        // A drag 300 left and 100 up.
        var grabAt = new float2(home.X + 60, home.Y + 6);
        var dragged = Drag(new float2(grabAt.X - 300, grabAt.Y - 100));
        var moved = ui.SandboxConfigWindow.Moved && MathF.Abs(dragged.X - (home.X - 300)) < 2 && MathF.Abs(dragged.Y - (home.Y - 100)) < 2;
        // The next panel opens there.
        ui.CloseSandboxConfig();
        ui.OpenSandboxConfig(car, lamp2);
        frames(2);
        var kept = ui.SandboxConfigWindow.Pos;
        var keptOk = MathF.Abs(kept.X - dragged.X) < 1 && MathF.Abs(kept.Y - dragged.Y) < 1;
        // Past the bottom-right corner, then past the top-left one: kept on the screen.
        var br = Drag(new float2(1599, 899));
        size = ui.SandboxConfigWindow.Size;
        var brOk = br.X + size.X <= 1600.5f && br.Y + size.Y <= 900.5f && br.X > 1600 - size.X - 2 && br.Y > 900 - size.Y - 2;
        var tl = Drag(new float2(1, 1));
        var tlOk = tl.X >= -0.5f && tl.Y >= -0.5f && tl.X < 2 && tl.Y < 2;
        // Snapped back beside the part.
        ui.SandboxConfigSnapBack();
        frames(2);
        var back = ui.SandboxConfigWindow;
        var backOk = !back.Moved && MathF.Abs(back.Pos.X - home.X) < 1 && MathF.Abs(back.Pos.Y - home.Y) < 1;
        ui.CloseSandboxConfig();
        var closedDim = ui.SceneDim(false, false);
        Line($"configure window: open, the scene dim {noDim} (with the pause menu {pauseDim}, the mouse free and nothing open {closedDim}); opened at {home.X:0},{home.Y:0} ({size.X:0} x {size.Y:0}), moved at open {movedAtOpen}; "
             + $"dragged to {dragged.X:0},{dragged.Y:0} {moved}; the next panel at {kept.X:0},{kept.Y:0} {keptOk}; past the corners {br.X:0},{br.Y:0} {brOk} and {tl.X:0},{tl.Y:0} {tlOk}; snapped back to {back.Pos.X:0},{back.Pos.Y:0} {backOk}");
        if (noDim != 0 || pauseDim <= 0 || closedDim <= 0)
            Fail("configure window: the settings panel dims the scene (or the pause menu no longer does)");
        if (movedAtOpen || !moved || !keptOk)
            Fail("configure window: a drag doesn't move the panel, or the next panel doesn't open where it was left");
        if (!brOk || !tlOk)
            Fail("configure window: the panel can be dragged off the screen");
        if (!backOk)
            Fail("configure window: snap back doesn't put the panel beside the part");
    }

    private static void ConfigOverWire(Map yard)
    {
        const ushort port = 26347;
        var clock = System.Diagnostics.Stopwatch.StartNew();
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
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("configure over the wire: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var before = server.Constructs.Bodies.Count;
        a.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Motor Gate"));
        Pump(server, [a], 0.6, ref now, clock);
        if (server.Constructs.Bodies.Count != before + 1)
        {
            Fail("configure over the wire: the client's Motor Gate didn't spawn");
            return;
        }
        var gate = server.Constructs.Bodies[^1];
        var hinge = PartNamed(gate, "Motor Hinge");
        var hd = gate.DefOf(hinge);
        var slot = (byte)hd.CfgSlot[(int)CfgKey.Max];
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxConfigure(gate.Id, [(byte)hinge], [slot], [30]);
        Pump(server, [a], 0.5, ref now, clock);
        var onServer = ConstructConfig.Get(gate.Parts[hinge], hd, CfgKey.Max, 0);
        var copy = a.Constructs.Get(gate.Id);
        var onClient = copy != null && hinge < copy.PartCount ? ConstructConfig.Get(copy.Parts[hinge], hd, CfgKey.Max, 0) : -1;
        var jointMax = copy != null && copy.JointOf(hinge) is var cj and >= 0 ? copy.Joints[cj].Max * 180 / MathF.PI : -1;
        // Someone else's (the host's, the host standing beside it): refused, and the reason comes back.
        gate.Owner = server.LocalId;
        if (server.Local is { } host)
        {
            if (!host.Alive)
                server.SelfTestRespawn(host, now);
            host.Teleport(gate.WorldCom + new Vector3(3, 0, 0));
        }
        Pump(server, [a], 0.2, ref now, clock);
        a.SandboxConfigure(gate.Id, [(byte)hinge], [slot], [10]);
        Pump(server, [a], 0.5, ref now, clock);
        var refused = a.SandboxNotice;
        var unchanged = ConstructConfig.Get(gate.Parts[hinge], hd, CfgKey.Max, 0);
        Line($"configure over the wire: a client set its Motor Gate's limit to 30 deg: the server {onServer}, the client's copy {onClient} (joint max {jointMax:0.#} deg); the host's construct: \"{refused}\" (still {unchanged})");
        if (onServer != 30 || onClient != 30 || MathF.Abs(jointMax - 30) > 0.1f || !refused.Contains("defended") || unchanged != 30)
            Fail("configure: a client's settings didn't go over the wire and back (or someone else's construct wasn't refused)");
    }
}
