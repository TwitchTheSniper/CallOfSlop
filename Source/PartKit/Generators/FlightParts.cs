using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The flight control parts: Constructs/Parts/Flight, and the demo craft (docs/sandbox/CONSTRUCTS.md, "Flight control"). Was
/// tools/constructs/flight_parts.py. Flight Stabilizers in four sizes (a seat's axes in, mixed channels out), Flight Panels (telemetry and
/// autopilot buttons), and the Quadcopter and Stabilised Airship blueprints (the latter is AirParts.Airship's Scout Airship with a
/// stabilizer). Recipes come from the part kit's assembling (--gen-parts assembling).
/// </summary>
public sealed class FlightParts : IPartGenerator
{
    public string Name => "flight_parts";

    private const string Out = "Constructs/Parts/Flight";
    private const string Section = "Flight control";
    private const string Housing = "#39414a", Dark = "#20252b", Arrow = "#e0b030", Pin = "#b08a4a", Screen = "#101416";
    private static readonly string[] Keys = ["Auto", "Rotorcraft", "Plane", "Rocket"];
    private static readonly string[] Modes = ["Hold altitude", "Hold heading", "Hover", "Waypoint", "Return home"];
    private const int FillPct = 22; // (a little short of her weight: the lift jet keeps her up and lets her down, and she needn't vent to come down)

    private static PyDict Number(string key, string label, string group, string unit, object lo, object hi, object step, object def, string tip, bool advanced = false)
    {
        var d = Kw(("key", key), ("label", label), ("group", group), ("unit", unit), ("min", lo), ("max", hi), ("step", step), ("default", def), ("tooltip", tip));
        if (advanced)
            d["advanced"] = true;
        return d;
    }

    private static PyDict Toggle(string key, string label, string group, bool def, string tip) =>
        Kw(("key", key), ("type", "toggle"), ("label", label), ("group", group), ("default", def), ("tooltip", tip));

    private static PyDict Choice(string key, string label, string group, string[] options, int def, string tip) =>
        Kw(("key", key), ("type", "choice"), ("label", label), ("group", group), ("options", options.Cast<object?>().ToList()), ("default", def), ("tooltip", tip));

    private static PyDict Adv(string key, string label, string group, string unit, object lo, object hi, object step, object def, string tip) =>
        Number(key, label, group, unit, lo, hi, step, def, tip, advanced: true);

    private static IEnumerable<object?> Axis(string name, string label, bool rate)
    {
        if (rate)
            yield return Adv($"{name}Rate", "Rate limit", label, "deg/s", 0, 720, 5, 0, "The fastest it turns this way. 0: Max turn.");
        yield return Adv($"{name}Response", "Response (P)", label, "%", 0, 400, 5, 0, "How hard it goes for what is asked. Up: quicker; too much rocks. 0: auto.");
        yield return Adv($"{name}Damping", "Damping (D)", label, "%", 0, 400, 5, 0, "How hard it stops. Up: less overshoot; too much shakes. 0: auto.");
        yield return Adv($"{name}Hold", "Hold (I)", label, "%", 0, 400, 5, 0, "How it works off a steady push. Up: it holds tighter; too much wanders. 0: auto.");
        yield return Adv($"{name}Expo", "Expo", label, "%", 0, 100, 5, 0, "Soft around the middle of a wired stick. A key is all or nothing.");
    }

    private static PyDict Hidden(string key) => Kw(("key", key), ("hidden", true), ("unit", "m"), ("min", -100000), ("max", 100000), ("default", 0));

    private static readonly List<object?> Config =
    [
        Choice("keys", "Keys", "Control", Keys, 0, "What the keys mean. Auto: wings and a forward push make it a plane, nozzles a rocket, anything else a rotorcraft. It finds what it can use by itself."),
        Choice("flightPreset", "Preset", "Control", ["Normal", "Docile", "Sport"], 0, "What auto means below: gentle, as it comes, or lively. Anything you set yourself stays."),
        Number("strength", "Strength", "Control", "%", 10, 300, 5, 100, "How hard it corrects. Lower it if the craft shakes."),
        Toggle("level", "Stabilise level", "Control", true, "Off: the keys ask for turning, and it stays at whatever angle you leave it."),
        Toggle("mouseAim", "Mouse aim", "Control", false, "It flies where the seat looks."),
        Toggle("invertPitch", "Invert pitch", "Control", false, "W lifts the nose and S drops it."),
        Number("maxTilt", "Max lean", "Limits", "deg", 0, 80, 1, 0, "The most it leans. 0: the pattern's own."),
        Number("maxTurn", "Max turn", "Limits", "deg/s", 0, 360, 5, 0, "The fastest it turns. 0: the pattern's own."),
        Number("maxClimb", "Max climb", "Limits", "m/s", 0, 200, 0.5, 0, "The fastest it climbs. 0: the pattern's own."),
        Number("maxSpeed", "Max speed", "Limits", "m/s", 0, 1000, 1, 0, "The speed the keys ask for. 0: the pattern's own."),
        Number("maxDescent", "Max descent", "Limits", "m/s", 0, 200, 0.5, 0, "The fastest it comes down. 0: as Max climb."),
        Number("trimPitch", "Trim, nose up", "Trim", "deg", -30, 30, 0.5, 0, "What it counts as level, nose up."),
        Number("trimRoll", "Trim, right wing down", "Trim", "deg", -30, 30, 0.5, 0, "What it counts as level, right wing down."),
        Toggle("autopilot", "Autopilot", "Autopilot", false, "On: it holds what the mode says. Its input and the Flight Panel set this too."),
        Choice("apMode", "Mode", "Autopilot", Modes, 2, "What it holds."),
        Number("targetAlt", "Altitude", "Autopilot", "m", 0, 20000, 1, 0, "The height held. 0: where it was when engaged."),
        Number("targetHeading", "Heading", "Autopilot", "deg", -1, 360, 1, -1, "The heading held, clockwise from north. -1: where it pointed when engaged."),
        Number("waypointX", "Waypoint x", "Autopilot", "m", -100000, 100000, 1, 0, "The point it flies to, world x."),
        Number("waypointZ", "Waypoint z", "Autopilot", "m", -100000, 100000, 1, 0, "The point it flies to, world z."),
        .. Axis("pitch", "Pitch", true),
        .. Axis("roll", "Roll", true),
        .. Axis("yaw", "Yaw", true),
        .. Axis("climb", "Climb", false),
        Adv("throttleCurve", "Throttle curve", "Feel", "%", -100, 100, 5, 0, "Above 0: finer low down. Below 0: finer high up."),
        Adv("maxBank", "Max bank", "Feel", "deg", 0, 85, 1, 0, "The most it banks. 0: Max lean."),
        Adv("maxPitchAngle", "Max pitch", "Feel", "deg", 0, 85, 1, 0, "The most it pitches. 0: Max lean."),
        Adv("turnCoord", "Turn coordination", "Feel", "%", 0, 200, 5, 0, "How much the rudder follows a bank. 0: auto, all of it."),
        Adv("autoLevel", "Auto-level", "Feel", "%", 0, 400, 5, 0, "How hard it comes level when let go. 0: auto."),
        Adv("windHold", "Wind hold", "Feel", "%", 0, 400, 5, 0, "How fast it leans into a steady drift. 0: auto."),
        Adv("landingAssist", "Landing assist", "Feel", "m", 0, 100, 1, 0, "Below this height it comes down gently. 0: off."),
        Adv("collectiveRange", "Collective range", "Feel", "%", 0, 100, 5, 0, "The most of its rotors' push it uses. 0: all of it."),
        Adv("thrustLimit", "Thrust limit", "Feel", "%", 0, 100, 5, 0, "The most of its thrusters' push it uses. 0: all of it."),
        Number("waypointY", "Waypoint y", "Autopilot", "m", -100000, 100000, 1, 0, "The point it flies to, world y: only on a round world, where x and z don't place it."),
        Hidden("homeY"),
        Hidden("homeX"),
        Hidden("homeZ"),
    ];

    /// <summary>A flat box, its arrow showing forward: mount it level, facing the way the craft flies.</summary>
    private static Part Stabilizer(string name, Cls cls, int s, int mass, int hp, int armour, object gyro, string tool)
    {
        var p = new Part(name, cls, Section, "Takes a seat's controls and flies the craft: link its outputs to thrusters, flaps and gimbals.", role: "logic", tool: tool, color: Housing, group: "Flight control");
        double h = 0.15 * s, t = 0.06 * s;
        p.Add(Box([-h, -t, -h], [h, t, h]));
        p.Add(Box([-0.02 * s, t - 0.003 * s, -0.11 * s], [0.02 * s, t + 0.012 * s, 0.05 * s], Arrow, detail: true));
        p.Add(Box([-0.06 * s, t - 0.002 * s, -0.09 * s], [0.06 * s, t + 0.01 * s, -0.05 * s], Arrow, detail: true));
        for (var k = 0; k < 4; k++)
        {
            var x = (-0.09 + 0.06 * k) * s;
            p.Add(Box([x - 0.012 * s, t - 0.003 * s, 0.08 * s], [x + 0.012 * s, t + 0.016 * s, 0.12 * s], Pin, detail: true));
        }
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+z", [0, 0, h], "+z");
        p.Sock("-z", [0, 0, -h], "-z");
        p.Sock("-y", [0, -t, 0], "-y");
        p.Stats(mass, hp, armour, cls == Small ? 2.0 : null);
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", gyro))), ("config", Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p;
    }

    private static Part BoxPanel(string name, Cls cls, int s, int mass, int hp, int armour, string tool, string desc, string source, int rows, List<object?>? touch = null)
    {
        double w = 0.7 * s, h = 0.5 * s, t = 0.03 * s;
        var p = new Part(name, cls, Section, desc, role: "logic", tool: tool, color: Housing, group: "Flight control");
        p.Add(Box([-w / 2, -h / 2, -t], [w / 2, h / 2, t]));
        p.Sock("back", [0, 0, -t], "-z");
        p.Sock("+x", [w / 2, 0, 0], "+x");
        p.Sock("-x", [-w / 2, 0, 0], "-x");
        p.Sock("+y", [0, h / 2, 0], "+y");
        p.Sock("-y", [0, -h / 2, 0], "-y");
        p.Stats(mass, hp, armour, cls == Small ? 1.0 : null);
        var surface = Kw(("source", source), ("at", Py.L(0, 0, Py.Round(t + 0.003, 4))), ("size", Py.L(Py.Round(w - 0.016, 3), Py.Round(h - 0.016, 3))), ("face", "+z"), ("up", "+y"), ("rows", rows));
        if (touch is not null)
            surface["touch"] = touch;
        p.Set(("logic", Kw(("kind", "instrument"))), ("panels", Py.L(surface)));
        return p;
    }

    private static IEnumerable<object?> Row(double y0, double y1, params (string Action, string Label)[] buttons)
    {
        var n = buttons.Length;
        for (var k = 0; k < n; k++)
            yield return Kw(("action", buttons[k].Action), ("at", Py.L(Py.Round(0.02 + k * 0.96 / n, 3), y0, Py.Round(0.02 + (k + 1) * 0.96 / n - 0.01, 3), y1)), ("label", buttons[k].Label));
    }

    private static readonly List<object?> Touch =
    [
        .. Row(0.14, 0.26, ("flight.on", "AP on"), ("flight.off", "AP off"), ("flight.mode", "Mode"), ("flight.home", "Set home")),
        .. Row(0.02, 0.13, ("flight.level", "Level"), ("flight.strength", "Strength"), ("flight.altitude", "Hold alt"), ("flight.heading", "Hold hdg")),
    ];

    /// <summary>Telemetry over the autopilot's and the stabilizer's buttons (ConstructFlight.FillPanel; PanelTouch.cs).</summary>
    private static Part Control(string name, Cls cls, int s, int mass, int hp, int armour, string tool) =>
        BoxPanel(name, cls, s, mass, hp, armour, tool, "Attitude, height, speed, the autopilot and how it is set, with buttons. For the first stabilizer on the construct. No wiring.", "flight.panel", 8, Touch);

    /// <summary>The stabilizer's view: what it found, from above and from the side (ConstructFlight.FillDiagnostic; PanelRender.FlightPlot).</summary>
    private static Part Diagnostic(string name, Cls cls, int s, int mass, int hp, int armour, string tool) =>
        BoxPanel(name, cls, s, mass, hp, armour, tool, "A plot of the craft from above and from the side: an arrow for each thing the stabilizer found, by what it is told; red ones are dead. Names any axis it can't control.", "flight.diagnostic", 6);

    private static PyDict Step(string part, int on, string socket, string by) => Kw(("part", part), ("on", on), ("socket", socket), ("by", by));

    /// <summary>Tanks in a cross, a jet under each corner tank, a seat on the middle one. W leans it on, Q and E sideways, A and D turn it, Shift and Ctrl climb and sink.</summary>
    private static PyDict Quadcopter()
    {
        var steps = Py.L(
            Kw(("part", "Fuel Tank")), // 0: the middle
            Step("Fuel Tank", 0, "-z", "+z"), // 1: the front
            Step("Fuel Tank", 0, "+z", "-z"), // 2: the back
            Step("Fuel Tank", 1, "-x", "+x"), // 3: front left
            Step("Fuel Tank", 1, "+x", "-x"), // 4: front right
            Step("Fuel Tank", 2, "-x", "+x"), // 5: rear left
            Step("Fuel Tank", 2, "+x", "-x"), // 6: rear right
            Step("Light Jet", 3, "-y", "front"),
            Step("Light Jet", 4, "-y", "front"),
            Step("Light Jet", 5, "-y", "front"),
            Step("Light Jet", 6, "-y", "front"),
            Step("Seat", 0, "+y", "bottom"), // 11
            Step("Flight Stabilizer", 2, "+y", "-y"), // 12
            Step("Drone Battery", 6, "+y", "-y"), // 13
            Step("Flight Control Panel", 1, "+y", "-y")); // 14
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", "Quadcopter"),
            ("description", "Four jets and a Flight Stabilizer. Shift lifts off and climbs, Ctrl sinks and lands, W and S lean it on, Q and E sideways, A and D turn. The panel's AP buttons hold what you leave it at."),
            ("steps", steps), ("links", new List<object?>()));
    }

    /// <summary>The Scout Airship with a stabilizer: her engines answer the steering, and a jet under her holds the height.</summary>
    private static PyDict StabilisedAirship()
    {
        var b = AirParts.Airship("Stabilised Airship", "The Scout Airship with a Flight Stabilizer: W and S drive her, A and D turn her by her engines, Shift and Ctrl climb and sink on a lift jet. The panel's AP buttons fly her.",
            "Lift Cell", (10, 12, 24), "helium", FillPct, masts: 2);
        var steps = (List<object?>)b["steps"]!;
        steps.Add(Step("Flight Stabilizer", 3, "+y", "-y"));
        steps.Add(Step("Square Beam 2 m", 3, "+z", "-z"));
        var lift = steps.Count;
        steps.Add(Step("Light Jet", lift - 1, "-y7", "front"));
        steps.Add(Step("Drone Battery", 5, "+z", "-y"));
        steps.Add(Step("Fuel Tank", 0, "-z", "+z"));
        steps.Add(Step("Flight Control Panel", steps.Count - 1, "+y", "-y"));
        ((List<object?>)b["links"]!).Add(Kw(("type", "pipe"), ("from", 0), ("to", lift), ("fromPort", "Fuel out"), ("toPort", "Fuel in")));
        return b;
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out,
        [
            ("", Stabilizer("Flight Stabilizer", Small, 1, 4, 60, 2, 1500, "wrench")),
            ("", Stabilizer("Medium Flight Stabilizer", Medium, 4, 240, 400, 6, 400000, "welder")),
            ("", Stabilizer("Large Flight Stabilizer", Large, 16, 12000, 2000, 12, 3e7, "welder")),
            ("", Stabilizer("Capital Flight Stabilizer", Capital, 64, 500000, 12000, 30, 2e9, "welder")),
            ("", Control("Flight Control Panel", Small, 1, 3, 30, 0, "wrench")),
            ("", Control("Medium Flight Control Panel", Medium, 4, 30, 120, 2, "welder")),
            ("", Control("Large Flight Control Panel", Large, 16, 300, 400, 4, "welder")),
            ("", Control("Capital Flight Control Panel", Capital, 64, 2000, 1600, 8, "welder")),
            ("", Diagnostic("Flight Diagnostic Panel", Small, 1, 3, 30, 0, "wrench")),
            ("", Diagnostic("Medium Flight Diagnostic Panel", Medium, 4, 30, 120, 2, "welder")),
            ("", Diagnostic("Large Flight Diagnostic Panel", Large, 16, 300, 400, 4, "welder")),
        ]);
        o.File("Constructs/Blueprints/Quadcopter.json", PyJson.Dumps(Quadcopter(), 1) + "\n");
        o.File("Constructs/Blueprints/StabilisedAirship.json", PyJson.Dumps(StabilisedAirship(), 1) + "\n");
    }
}
