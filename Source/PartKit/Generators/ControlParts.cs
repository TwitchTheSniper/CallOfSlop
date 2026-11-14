using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The hand controls' part files and the control modules': Constructs/Parts/Controls (docs/sandbox/CONSTRUCTS.md, "Logic" and "Control
/// modules"). Was tools/constructs/control_parts.py.
///
/// An Analog Lever is a housing with a slot along its top and a handle that tilts in the slot with the part's value (a "lever" rig, the
/// drawing's moving-box path), a block at each end of the slot for its end stops. The handle is a detail, so a hidden box stands where it
/// swings: the crosshair finds the part there. Recipes come from the part kit's assembling (--gen-parts assembling); run it after this one.
/// <see cref="Lever"/> and <see cref="Config"/> are shared with <see cref="HydraulicsParts"/>.
/// </summary>
public sealed class ControlParts : IPartGenerator
{
    public string Name => "control_parts";

    private const string Out = "Constructs/Parts/Controls";
    private const string Housing = "#3a3f46", Slot = "#16181b", Stop = "#8f98a2", Shaft = "#c9ced4", Knob = "#e0b030";
    private const string Green = "#5ac060", Red = "#d0352c", Amber = "#e0b030", Cap = "#c9ced4", Block = "#2f3a44", Pin = "#b08a4a";

    private static PyDict D(params (string Key, object? Value)[] kv) => Kw(kv);

    /// <summary>dict(d, **kv): a copy with keys set (an existing key keeps its place).</summary>
    private static PyDict With(PyDict d, params (string Key, object? Value)[] kv) => new PyDict(d).Update(kv);

    /// <summary>An Analog Lever's settings (a new list each time).</summary>
    public static List<object?> Config() => Py.L(
        D(("key", "value"), ("label", "Value"), ("group", "General"), ("min", 0), ("max", 1), ("step", 0.01), ("default", 0), ("tooltip", "Where the handle stands, 0 to 1.")),
        D(("key", "snap"), ("label", "Snap step"), ("group", "General"), ("min", 0), ("max", 0.5), ("step", 0.01), ("default", 0), ("tooltip", "The steps the handle clicks through. 0: smooth.")),
        D(("key", "low"), ("label", "Output at 0"), ("group", "Output"), ("min", -100), ("max", 100), ("step", 0.01), ("default", 0), ("tooltip", "What it puts out with the handle at 0.")),
        D(("key", "high"), ("label", "Output at 1"), ("group", "Output"), ("min", -100), ("max", 100), ("step", 0.01), ("default", 1), ("tooltip", "What it puts out with the handle at 1.")),
        D(("key", "spring"), ("type", "toggle"), ("label", "Spring return"), ("group", "Spring"), ("default", false), ("tooltip", "Goes back to its rest value when let go.")),
        D(("key", "rest"), ("label", "Rest value"), ("group", "Spring"), ("min", 0), ("max", 1), ("step", 0.01), ("default", 0), ("tooltip", "Where the spring takes it.")));


    private static void FiveSockets(Part p, double h, double y)
    {
        foreach (var (n, at) in new (string, double[])[] { ("+x", [h, 0, 0]), ("-x", [-h, 0, 0]), ("+z", [0, 0, h]), ("-z", [0, 0, -h]), ("-y", [0, -y, 0]) })
            p.Sock(n, at, n);
    }

    private static object? SmallSeconds(Cls c) => c == Small ? 1.0 : null;

    private static PyDict Rig(string rig, double[] pivot) => Kw(("rig", rig), ("pivot", Py.L(0, pivot[1], 0)));

    /// <summary>s: the scale against the small one, whose housing is a 0.2 m cube round the origin.</summary>
    public static (string, Part) Lever(string name, Cls cls, double s, double mass, double health, int armour, string tool)
    {
        var h = 0.1 * s;
        var p = new Part(name, cls, "Controls", "A handle you drag from 0 to 1, for throttles and speeds.", role: "logic", tool: tool, color: Housing, group: cls == Small ? "Logic" : null);
        p.Add(Box([-h, -h, -h], [h, h, h]));
        p.Add(Box([-h, h + 0.001, -h], [h, h + 0.25 * s, h], hidden: true)); // (the crosshair's way to the handle)
        p.Add(Box([-0.03 * s, h - 0.006 * s, -0.08 * s], [0.03 * s, h + 0.008 * s, 0.08 * s], Slot, detail: true));
        foreach (var z in new[] { -1, 1 })
        {
            double a = z * 0.08 * s, b = z * 0.1 * s;
            var (lo, hi) = a <= b ? (a, b) : (b, a);
            p.Add(Box([-0.035 * s, h - 0.002 * s, lo], [0.035 * s, h + 0.022 * s, hi], Stop, detail: true));
        }
        double[] pivot = [0, h, 0];
        p.Add(Box([-0.014 * s, h, -0.014 * s], [0.014 * s, h + 0.2 * s, 0.014 * s], Shaft, detail: true, shape: Rig("lever", pivot)));
        p.Add(Box([-0.032 * s, h + 0.19 * s, -0.032 * s], [0.032 * s, h + 0.25 * s, 0.032 * s], Knob, detail: true, shape: Rig("lever", pivot)));
        FiveSockets(p, h, h);
        p.Stats(mass, health, armour, SmallSeconds(cls));
        p.Set(("logic", D(("kind", "analoglever"))), ("config", Config()));
        return ("", p);
    }

    private static List<object?> StickConfig() => Py.L(
        D(("key", "value"), ("label", "X"), ("group", "General"), ("min", 0), ("max", 1), ("step", 0.01), ("default", 0.5), ("tooltip", "Where the stick stands side to side, 0 (left) to 1 (right).")),
        D(("key", "valueY"), ("label", "Y"), ("group", "General"), ("min", 0), ("max", 1), ("step", 0.01), ("default", 0.5), ("tooltip", "Where the stick stands fore and aft, 0 (back) to 1 (forward).")),
        D(("key", "snap"), ("label", "Snap step"), ("group", "General"), ("min", 0), ("max", 0.5), ("step", 0.01), ("default", 0), ("tooltip", "The steps the stick clicks through on each axis. 0: smooth.")),
        D(("key", "low"), ("label", "Output at 0"), ("group", "Output"), ("min", -100), ("max", 100), ("step", 0.01), ("default", -1), ("tooltip", "What each axis puts out with the stick at 0.")),
        D(("key", "high"), ("label", "Output at 1"), ("group", "Output"), ("min", -100), ("max", 100), ("step", 0.01), ("default", 1), ("tooltip", "What each axis puts out with the stick at 1.")),
        D(("key", "spring"), ("type", "toggle"), ("label", "Spring return"), ("group", "Spring"), ("default", true), ("tooltip", "Goes back to its rest values when let go.")),
        D(("key", "rest"), ("label", "Rest X"), ("group", "Spring"), ("min", 0), ("max", 1), ("step", 0.01), ("default", 0.5), ("tooltip", "Where the spring takes it side to side.")),
        D(("key", "restY"), ("label", "Rest Y"), ("group", "Spring"), ("min", 0), ("max", 1), ("step", 0.01), ("default", 0.5), ("tooltip", "Where the spring takes it fore and aft.")));

    /// <summary>An Analog Lever's housing with a square gate on top and a stick that tilts both ways in it (a "stick" rig): two axes out.</summary>
    private static (string, Part) Joystick(string name, Cls cls, int s, double mass, double health, int armour, string tool)
    {
        var h = 0.1 * s;
        var p = new Part(name, cls, "Controls", "A stick you push any way: X and Y out, -1 to 1, back to the middle when let go.", role: "logic", tool: tool, color: Housing, group: cls == Small ? "Logic" : null);
        p.Add(Box([-h, -h, -h], [h, h, h]));
        p.Add(Box([-h, h + 0.001, -h], [h, h + 0.25 * s, h], hidden: true)); // (the crosshair's way to the stick)
        p.Add(Box([-0.06 * s, h - 0.006 * s, -0.06 * s], [0.06 * s, h + 0.008 * s, 0.06 * s], Slot, detail: true)); // the gate
        foreach (var x in new[] { -1, 1 })
            foreach (var z in new[] { -1, 1 })
            {
                double xa = x * 0.062 * s, xb = x * 0.09 * s, za = z * 0.062 * s, zb = z * 0.09 * s;
                var (xl, xh) = xa <= xb ? (xa, xb) : (xb, xa);
                var (zl, zh) = za <= zb ? (za, zb) : (zb, za);
                p.Add(Box([xl, h - 0.002 * s, zl], [xh, h + 0.018 * s, zh], Stop, detail: true)); // a corner of the gate's rim
            }
        double[] pivot = [0, h, 0];
        p.Add(Box([-0.03 * s, h - 0.003 * s, -0.03 * s], [0.03 * s, h + 0.028 * s, 0.03 * s], Slot, detail: true, shape: Rig("stick", pivot))); // the boot
        p.Add(Box([-0.013 * s, h + 0.026 * s, -0.013 * s], [0.013 * s, h + 0.16 * s, 0.013 * s], Shaft, detail: true, shape: Rig("stick", pivot)));
        p.Add(Box([-0.026 * s, h + 0.15 * s, -0.026 * s], [0.026 * s, h + 0.25 * s, 0.026 * s], Housing, detail: true, shape: Rig("stick", pivot))); // the grip
        p.Add(Box([-0.016 * s, h + 0.246 * s, -0.016 * s], [0.016 * s, h + 0.262 * s, 0.016 * s], Red, detail: true, shape: Rig("stick", pivot))); // its cap
        FiveSockets(p, h, h);
        p.Stats(mass, health, armour, SmallSeconds(cls));
        p.Set(("logic", D(("kind", "analoglever"), ("axes", 2))), ("config", StickConfig()));
        return ("", p);
    }

    /// <summary>A control module (ConstructModules.cs): a flat box with a row of terminals along its top, each a control it breaks out.</summary>
    private static (string, Part) Module(string name, string kind, string desc, string accent, List<object?>? config = null)
    {
        var p = new Part(name, Small, "Controls", desc, role: "logic", tool: "wrench", color: Housing, group: "Logic");
        double h = 0.15, t = 0.08;
        p.Add(Box([-h, -t, -h], [h, t, h]));
        p.Add(Box([-0.11, t - 0.004, -0.11], [0.11, t + 0.012, -0.03], accent, detail: true));
        for (var k = 0; k < 4; k++)
        {
            var x = -0.09 + 0.06 * k;
            p.Add(Box([x - 0.012, t - 0.004, 0.03], [x + 0.012, t + 0.02, 0.09], Stop, detail: true));
        }
        FiveSockets(p, h, t);
        p.Stats(4, 60, 2, 1.0);
        p.Set(("logic", D(("kind", kind))));
        if (config is { Count: > 0 })
            p.Set(("config", config));
        return ("", p);
    }

    private static List<object?> Stabilised() =>
        Py.L(D(("key", "stabilised"), ("type", "toggle"), ("label", "Stabilised"), ("group", "Aim"), ("default", false), ("tooltip", "With the mouse still, the look and the guns stay on the point they were left on as the craft moves.")));

    // ---- Hand controls, displays, logic blocks, sensors, bundling (docs/sandbox/CONSTRUCTS.md, "Logic blocks")

    private static readonly PyDict Latch0 = D(("key", "latched"), ("label", "Latched"), ("group", "General"), ("default", true), ("tooltip", "On: each press turns it on or off. Off: it springs back after its hold time."));
    private static readonly PyDict Latch1 = D(("key", "hold"), ("label", "Stays on for"), ("group", "General"), ("unit", "s"), ("min", 0.05), ("max", 30), ("step", 0.05), ("default", 0.5), ("tooltip", "How long a press keeps it on (when not latched)."));
    private static List<object?> LatchCfg() => Py.L(Latch0, Latch1);
    private static List<object?> ButtonCfg() => Py.L(With(Latch0, ("default", false)), Latch1);
    private static readonly PyDict Ink = D(("key", "ink"), ("type", "colour"), ("label", "Colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what it shows. Black: its own colours."));
    private static readonly PyDict Ramp = D(("key", "ramp"), ("label", "Colour mode"), ("group", "Display"), ("options", Py.L("Display colour", "Green yellow red", "Gradient", "Heat")), ("default", 0), ("tooltip", "How the colour follows the value."));
    private static readonly PyDict Decimals = D(("key", "decimals"), ("label", "Decimals"), ("group", "General"), ("min", 0), ("max", 4), ("step", 1), ("default", 1), ("tooltip", "Places after the point."));
    private static readonly PyDict Unit = D(("key", "unit"), ("label", "Unit"), ("group", "General"), ("options", Py.L("none", "m", "m/s", "km/h", "%", "rpm", "kW", "kg", "L", "deg", "s", "N")), ("default", 0), ("tooltip", "Shown after the number."));
    private static readonly PyDict Low = D(("key", "low"), ("label", "Min"), ("group", "Scale"), ("min", -10000), ("max", 10000), ("step", 0.1), ("default", 0), ("tooltip", "The bottom of the scale."));
    private static readonly PyDict High = D(("key", "high"), ("label", "Max"), ("group", "Scale"), ("min", -10000), ("max", 10000), ("step", 0.1), ("default", 100), ("tooltip", "The top of the scale."));

    /// <summary>A box 2h wide and long, 2t high, with sockets on its sides and under it; kind its logic kind.</summary>
    private static Part Body(string name, string section, string desc, double h, double t, Cls? cls = null, string tool = "wrench", double mass = 3, double health = 40, int armour = 1,
        string? kind = null, string color = Housing, List<object?>? config = null, params (string Key, object? Value)[] logic)
    {
        cls ??= Small;
        var p = new Part(name, cls, section, desc, role: "logic", tool: tool, color: color, group: cls == Small ? "Logic" : null);
        p.Add(Box([-h, -t, -h], [h, t, h]));
        FiveSockets(p, h, t);
        p.Stats(mass, health, armour, SmallSeconds(cls));
        p.Set(("logic", D(("kind", kind)).Update(logic)));
        if (config is { Count: > 0 })
            p.Set(("config", config));
        return p;
    }

    /// <summary>A rotary knob with stops, or a slider: an Analog Lever by another shape (AnalogLever.cs).</summary>
    private static (string, Part) Analog(string name, string section, string rig, int s = 1, double snap = 0.0, string tool = "wrench", double mass = 3, Cls? cls = null, double health = 40, int armour = 1)
    {
        var desc = rig == "knob" ? "A knob with stops, 0 to 1." : "A slider, 0 to 1.";
        var cfg = Config();
        ((PyDict)cfg[1]!)["default"] = snap;
        double h = 0.1 * s, t = 0.05 * s;
        var p = Body(name, section, desc, h, t, cls, tool, mass, health, armour, "analoglever", config: cfg);
        p.Add(Box([-h, t + 0.001, -h], [h, t + 0.1 * s, h], hidden: true));
        if (rig == "knob")
        {
            p.Add(Box([-0.05 * s, t - 0.002 * s, -0.05 * s], [0.05 * s, t + 0.06 * s, 0.05 * s], Shaft, detail: true, shape: Kw(("rig", "knob"), ("pivot", Py.L(0, t, 0)))));
            p.Add(Box([-0.012 * s, t + 0.056 * s, 0], [0.012 * s, t + 0.075 * s, 0.05 * s], Amber, detail: true, shape: Kw(("rig", "knob"), ("pivot", Py.L(0, t, 0)))));
            for (var k = -5; k < 6; k++)
            {
                var a = k * 27 * (Math.PI / 180.0);
                double x = Math.Sin(a) * 0.085 * s, z = Math.Cos(a) * 0.085 * s;
                p.Add(Box([x - 0.007 * s, t - 0.002 * s, z - 0.007 * s], [x + 0.007 * s, t + 0.012 * s, z + 0.007 * s], Stop, detail: true));
            }
        }
        else
        {
            p.Add(Box([-0.012 * s, t - 0.004 * s, -0.085 * s], [0.012 * s, t + 0.006 * s, 0.085 * s], Slot, detail: true));
            p.Add(Box([-0.04 * s, t - 0.002 * s, -0.02 * s], [0.04 * s, t + 0.05 * s, 0.02 * s], Knob, detail: true, shape: Kw(("rig", "slide"), ("travel", 0.065 * s))));
        }
        return ("", p);
    }

    /// <summary>A switch or button with a part of it that moves: a toggle's bat, a rocker's plate, a mushroom's cap.</summary>
    private static (string, Part) Toggle(string name, string kind, string shape, int s = 1, string tool = "wrench", double mass = 2, Cls? cls = null, double health = 40, int armour = 1, List<object?>? config = null)
    {
        config ??= LatchCfg();
        double h = 0.1 * s, t = 0.05 * s;
        var desc = shape switch { "toggle" => "A flick switch.", "rocker" => "A rocker switch.", _ => "A big red button." };
        var p = Body(name, "Controls", desc, h, t, cls, tool, mass, health, armour, kind, config: config);
        if (shape == "toggle")
            p.Add(Box([-0.02 * s, t, -0.02 * s], [0.02 * s, t + 0.09 * s, 0.02 * s], Cap, detail: true, shape: Kw(("use", true), ("rig", "flip"), ("pivot", Py.L(0, t, 0)))));
        else if (shape == "rocker")
            p.Add(Box([-0.05 * s, t - 0.002 * s, -0.075 * s], [0.05 * s, t + 0.03 * s, 0.075 * s], Green, detail: true, shape: Kw(("use", true), ("rig", "flip"), ("pivot", Py.L(0, t, 0)))));
        else
        {
            p.Add(Box([-0.03 * s, t - 0.002 * s, -0.03 * s], [0.03 * s, t + 0.04 * s, 0.03 * s], Cap, detail: true));
            p.Add(Box([-0.08 * s, t + 0.04 * s, -0.08 * s], [0.08 * s, t + 0.075 * s, 0.08 * s], Red, detail: true, shape: Kw(("use", true), ("rig", "press"), ("travel", 0.03 * s))));
        }
        return ("", p);
    }

    private static (string, Part) KeySwitch()
    {
        var p = Body("Key Switch", "Controls", "Turns for its owner, or with a Key in your hands.", 0.1, 0.05, kind: "keyswitch");
        p.Add(Box([-0.04, 0.048, -0.04], [0.04, 0.066, 0.04], Pin, detail: true));
        p.Add(Box([-0.008, 0.064, -0.03], [0.008, 0.078, 0.03], Slot, detail: true, shape: Kw(("use", true), ("rig", "flip"), ("pivot", Py.L(0, 0.05, 0)))));
        return ("", p);
    }

    private static (string, Part) KeyItem()
    {
        var p = new Part("Key", Small, "Controls", "Hold it to turn any Key Switch.", role: "frame", tool: "wrench", color: Pin, group: "Logic");
        p.Add(Box([-0.03, 0, -0.08], [0.03, 0.012, -0.02]));
        p.Add(Box([-0.01, 0, -0.02], [0.01, 0.012, 0.09], detail: true));
        p.Sock("-y", [0, 0, -0.05], "-y");
        p.Stats(0.2, 10, 0, 0.5);
        return ("", p);
    }

    private static void FrontSockets(Part p, double hw, double hh, double t)
    {
        foreach (var (n, at, d) in new (string, double[], string)[] { ("back", [0, 0, -t], "-z"), ("+x", [hw, 0, 0], "+x"), ("-x", [-hw, 0, 0], "-x"), ("+y", [0, hh, 0], "+y"), ("-y", [0, -hh, 0], "-y") })
            p.Sock(n, at, d);
    }

    /// <summary>Twelve real keys under a screen on the part's front (+z): "use" boxes with "key".</summary>
    private static (string, Part) Keypad(string name, string kind, string desc, List<object?> config, int s = 1)
    {
        double hw = 0.11 * s, hh = 0.15 * s, t = 0.04 * s;
        var p = new Part(name, Small, "Controls", desc, role: "logic", tool: "wrench", color: Housing, group: "Logic");
        p.Add(Box([-hw, -hh, -t], [hw, hh, t]));
        FrontSockets(p, hw, hh, t);
        string[] rows = ["123", "456", "789", "C0E"];
        for (var r = 0; r < rows.Length; r++)
            for (var k = 0; k < 3; k++)
            {
                var ch = rows[r][k];
                var x = (k - 1) * 0.062 * s;
                var y = 0.012 * s - r * 0.044 * s;
                var colour = ch == 'C' ? Red : ch == 'E' ? Green : Cap;
                p.Add(Box([x - 0.026 * s, y - 0.018 * s, t - 0.002], [x + 0.026 * s, y + 0.018 * s, t + 0.012 * s], colour, detail: true, shape: Kw(("use", true), ("key", ch.ToString()))));
            }
        p.Stats(3, 40, 1, 1.0);
        p.Set(("logic", D(("kind", kind))), ("config", config.Append(Ink).ToList()),
            ("panels", Py.L(D(("source", "logic.keypad"), ("at", Py.L(0, 0.09 * s, t + 0.004)), ("size", Py.L(0.18 * s, 0.09 * s)), ("face", "+z"), ("up", "+y"), ("rows", 1)))));
        return ("", p);
    }

    /// <summary>A flat box with one panel on its front (+z), and sockets behind and round it.</summary>
    private static (string, Part) Screen(string name, string source, string desc, double w, double h, int rows, Cls? cls = null, string tool = "wrench", double mass = 3, double health = 30, int armour = 0,
        string kind = "display", List<object?>? config = null, string section = "Displays", params (string Key, object? Value)[] logic)
    {
        cls ??= Small;
        var t = Math.Max(0.03, Math.Min(w, h) * 0.08);
        var p = new Part(name, cls, section, desc, role: "logic", tool: tool, color: Housing, group: cls == Small ? "Logic" : null);
        p.Add(Box([-w / 2, -h / 2, -t], [w / 2, h / 2, t]));
        FrontSockets(p, w / 2, h / 2, t);
        p.Stats(mass, health, armour, SmallSeconds(cls));
        p.Set(("logic", D(("kind", kind)).Update(logic)),
            ("panels", Py.L(D(("source", source), ("at", Py.L(0, 0, t + 0.003)), ("size", Py.L(Py.Round(w - 0.016, 3), Py.Round(h - 0.016, 3))), ("face", "+z"), ("up", "+y"), ("rows", rows)))));
        p.Set(("config", (config ?? []).Append(Ink).ToList()));
        return ("", p);
    }

    /// <summary>
    /// A painted sign board (buildings.md "Signs"): a flat board in its paint (Ministry green unless painted) whose words are
    /// signwritten on it, centred and as big as the board takes: one row holds the whole line, made narrower to fit, never wrapped.
    /// Hung by its back on a wall's face socket, a fascia's front or a canopy's sign socket.
    /// </summary>
    private static (string, Part) SignBoard(string name, string desc, double w, double h, int rows)
    {
        const double t = 0.03;
        var p = new Part(name, Small, "Displays", desc, role: "logic", tool: "wrench", color: "#2f5a3c", group: "Logic");
        p.Add(Box([-w / 2, -h / 2, -t], [w / 2, h / 2, t]));
        p.Add(Box([-w / 2 - 0.012, -h / 2 - 0.012, -t - 0.004], [w / 2 + 0.012, h / 2 + 0.012, -t + 0.012], "#2b2e30", detail: true)); // (the batten frame behind it)
        FrontSockets(p, w / 2, h / 2, t);
        p.Stats(2 + 4 * w * h, 40, 0, SmallSeconds(Small));
        p.Set(("logic", D(("kind", "display"), ("text", 2))),
            ("panels", Py.L(D(("source", "logic.sign"), ("at", Py.L(0, 0, t + 0.003)), ("size", Py.L(Py.Round(w - 0.01, 3), Py.Round(h - 0.01, 3))), ("face", "+z"), ("up", "+y"), ("rows", rows), ("board", true)))));
        p.Set(("config", new List<object?> { Ink }));
        return ("", p);
    }

    private static List<(string, Part)> Displays()
    {
        var o = new List<(string, Part)>
        {
            SignBoard("Sign Board 2 m", "A painted board: one line of words, signwritten to fit. Paint it for its colour.", 2.0, 0.4, 1),
            SignBoard("Sign Board 3 m", "A painted board: one line of words, signwritten to fit. Paint it for its colour.", 3.0, 0.5, 1),
            SignBoard("Notice Board", "A painted board of rules: three lines of words. Paint it for its colour.", 1.2, 0.8, 3),
        };
        List<object?> Readout() => Py.L(Decimals, Unit);
        List<object?> Scale() => Py.L(Decimals, Unit, Low, High);
        List<object?> Plot() => Py.L(Low, High, Ramp); // (no number on these: the whole screen is the bar or the line)
        foreach (var (pre, cls, s, tool, mass, hp, ar) in new[] { ("", Small, 1, "wrench", 3, 30, 0), ("Medium ", Medium, 4, "welder", 30, 120, 2), ("Large ", Large, 16, "welder", 300, 400, 4) })
        {
            o.Add(Screen(pre + "Number Readout", "logic.readout", "Shows the number wired into it.", 0.5 * s, 0.25 * s, 1, cls, tool, mass, hp, ar, config: Readout()));
            o.Add(Screen(pre + "Bar Gauge", "logic.bar", "A bar across the whole screen, from its min to its max.", 0.5 * s, 0.25 * s, 1, cls, tool, mass, hp, ar, config: Plot()));
            o.Add(Screen(pre + "Tall Bar Gauge", "logic.barv", "A bar up the whole screen, from its min to its max.", 0.25 * s, 0.5 * s, 2, cls, tool, mass, hp, ar, config: Plot()));
            o.Add(Screen(pre + "Dial Gauge", "logic.dial", "A needle, from its min to its max.", 0.5 * s, 0.5 * s, 4, cls, tool, mass, hp, ar, config: Scale()));
            o.Add(Screen(pre + "Graph Screen", "logic.graph", "The number wired into it over the last 30 s. Min and max the same: it fits the line.", 1.0 * s, 0.5 * s, 4, cls, tool, mass, hp, ar, config: Plot()));
            o.Add(Screen(pre + "Text Sign", "logic.sign", "Says what you type. A wire on it switches to its second message.", 1.0 * s, 0.5 * s, 3, cls, tool, mass, hp, ar, logic: ("text", 2)));
            o.Add(Screen(pre + "Compass", "logic.compass", "The way you face looking at it. No wiring.", 0.5 * s, 0.25 * s, 2, cls, tool, mass, hp, ar, kind: "instrument", section: "Gauges"));
            o.Add(Screen(pre + "Altimeter", "logic.altimeter", "How high it is. No wiring.", 0.5 * s, 0.25 * s, 1, cls, tool, mass, hp, ar, kind: "instrument", section: "Gauges",
                config: Py.L(With(Decimals, ("default", 0)), With(Unit, ("default", 1)))));
            o.Add(Screen(pre + "Speedometer", "logic.speedometer", "How fast its construct goes. No wiring.", 0.5 * s, 0.5 * s, 4, cls, tool, mass, hp, ar, kind: "instrument", section: "Gauges",
                config: Py.L(D(("key", "units"), ("label", "Units"), ("group", "General"), ("options", Py.L("m/s", "km/h", "mph")), ("default", 1), ("tooltip", "What it reads in.")), With(Low, ("default", 0)), With(High, ("default", 120)))));
            foreach (var n in new[] { 4, 8 })
                o.Add(Screen($"{pre}Light Bank {n}", "logic.lamps", "A labelled lamp for each of its inputs. Type the labels, comma-separated.", 0.5 * s, 0.09 * n * s, n, cls, tool, mass, hp, ar, logic: [("ports", n), ("text", 1)]));
        }
        return o;
    }

    private static List<(string, Part)> Blocks()
    {
        var o = new List<(string, Part)>();
        void Blk(string name, string kind, string desc, List<object?>? config = null, string accent = Green, string section = "Logic gates")
        {
            var p = Body(name, section, desc, 0.1, 0.05, kind: kind, config: config, color: Block);
            p.Add(Box([-0.07, 0.048, -0.07], [0.07, 0.056, -0.02], accent, detail: true));
            for (var k = 0; k < 3; k++)
            {
                var x = -0.05 + 0.05 * k;
                p.Add(Box([x - 0.012, 0.048, 0.02], [x + 0.012, 0.06, 0.07], Pin, detail: true));
            }
            o.Add(("", p));
        }
        var value = D(("key", "value"), ("label", "Value"), ("group", "General"), ("min", -1000), ("max", 1000), ("default", 0), ("tooltip", "What it holds now."));
        Blk("XOR Gate", "xor", "On when an odd number of inputs are on.");
        Blk("NAND Gate", "nand", "Off only when every input is on.");
        Blk("Math Block", "mathblock", "a and b: add, subtract, multiply, divide, min or max.", Py.L(
            D(("key", "operator"), ("label", "Operation"), ("group", "General"), ("options", Py.L("add", "subtract", "multiply", "divide", "min", "max")), ("default", 0), ("tooltip", "What it does with a and b.")),
            D(("key", "offset"), ("label", "b if unwired"), ("group", "General"), ("min", -1000), ("max", 1000), ("step", 0.01), ("default", 0), ("tooltip", "Used when nothing is wired into b."))), Amber);
        Blk("Latch", "latch", "Set turns it on, reset turns it off.", accent: Amber, section: "Memory");
        Blk("Counter", "counter", "Counts up and down on rising inputs, to its limit.", Py.L(
            value, D(("key", "limit"), ("label", "Limit"), ("group", "General"), ("min", 0), ("max", 100000), ("step", 1), ("default", 0), ("tooltip", "The most it counts to. 0: no limit.")),
            D(("key", "wrap"), ("label", "Wrap round"), ("group", "General"), ("default", false), ("tooltip", "Past the limit it starts again."))), Amber, "Memory");
        Blk("Memory Cell", "memory", "Stores value when store rises.", Py.L(value), Amber, "Memory");
        Blk("Filter", "filter", "Follows its input smoothed, clamped and rate limited. Good for hydraulics.", Py.L(
            D(("key", "seconds"), ("label", "Smoothing"), ("group", "General"), ("unit", "s"), ("min", 0), ("max", 30), ("step", 0.05), ("default", 0), ("tooltip", "How slowly it follows. 0: no smoothing.")),
            D(("key", "rate"), ("label", "Rate limit"), ("group", "General"), ("unit", "/s"), ("min", 0), ("max", 1000), ("step", 0.01), ("default", 0), ("tooltip", "The most it changes a second. 0: no limit.")),
            D(("key", "low"), ("label", "Min"), ("group", "Clamp"), ("min", -1000), ("max", 1000), ("step", 0.01), ("default", -1000), ("tooltip", "The least it puts out.")),
            D(("key", "high"), ("label", "Max"), ("group", "Clamp"), ("min", -1000), ("max", 1000), ("step", 0.01), ("default", 1000), ("tooltip", "The most it puts out."))));
        Blk("Multiplexer", "mux", "Puts out the input its select names, 1 to 6.");
        Blk("Random", "randompulse", "A new random number on each rising input, min to max.", Py.L(
            value, D(("key", "low"), ("label", "Min"), ("group", "General"), ("min", -1000), ("max", 1000), ("step", 0.01), ("default", 0), ("tooltip", "The least it gives.")),
            D(("key", "high"), ("label", "Max"), ("group", "General"), ("min", -1000), ("max", 1000), ("step", 0.01), ("default", 1), ("tooltip", "The most it gives."))));
        return o;
    }

    private static List<(string, Part)> Sensors()
    {
        var o = new List<(string, Part)>();
        void Sensor(string name, string kind, string desc, List<object?>? config = null)
        {
            var p = Body(name, "Sensors", desc, 0.08, 0.04, kind: kind, config: config, mass: 1, health: 20, color: Block);
            p.Add(Box([-0.03, 0.038, -0.06], [0.03, 0.05, 0.02], Amber, detail: true)); // (its front, -z)
            o.Add(("", p));
        }
        Sensor("Altitude Sensor", "altitudesensor", "How high it is, m.");
        Sensor("Tilt Sensor", "tiltsensor", "Its pitch, and its roll on the second output, degrees.");
        Sensor("Heading Sensor", "headingsensor", "The way its front faces, degrees from north.");
        Sensor("Level Sensor", "levelsensor", "Link its watch output to a tank or store: how full, %.");
        Sensor("Damage Sensor", "damagesensor", "Link its watch output to a part: on when that part is hit.", Py.L(
            D(("key", "hold"), ("label", "Stays on for"), ("group", "General"), ("unit", "s"), ("min", 0.05), ("max", 30), ("step", 0.05), ("default", 1), ("tooltip", "How long a hit keeps it on."))));
        Sensor("Occupancy Sensor", "occupancysensor", "On while the seat wired into it, or the drive seat, is taken.");
        return o;
    }

    private static List<(string, Part)> Bundling()
    {
        var channel = D(("key", "channel"), ("label", "Channel"), ("group", "General"), ("min", 1), ("max", 99), ("step", 1), ("default", 1), ("tooltip", "A sender and a receiver on the same channel talk."));
        var o = new List<(string, Part)>();
        void Radio(string name, string kind, string desc, List<object?> config)
        {
            var p = Body(name, "Wireless and bus", desc, 0.1, 0.05, kind: kind, config: config, color: Block);
            p.Add(Box([0.05, 0.048, 0.05], [0.07, 0.2, 0.07], Shaft, detail: true));
            o.Add(("", p));
        }
        Radio("Wireless Sender", "radiotx", "Sends its input on a channel, short range.", Py.L(channel, D(("key", "radius"), ("label", "Range"), ("group", "General"), ("unit", "m"), ("min", 5), ("max", 100), ("step", 1), ("default", 30), ("tooltip", "How far it carries."))));
        Radio("Wireless Receiver", "radiorx", "Puts out the nearest sender on its channel.", Py.L(channel));
        foreach (var (name, kind, desc) in new[] { ("Data Bus", "busin", "Eight signals in, one link out to a Bus Breakout."), ("Bus Breakout", "busout", "One link from a Data Bus in, its eight signals out.") })
        {
            var p = Body(name, "Wireless and bus", desc, 0.15, 0.05, kind: kind, color: Block);
            for (var k = 0; k < 8; k++)
            {
                var x = -0.105 + 0.03 * k;
                p.Add(Box([x - 0.009, 0.048, -0.12], [x + 0.009, 0.06, -0.06], Pin, detail: true));
            }
            p.Add(Box([-0.05, 0.048, 0.05], [0.05, 0.07, 0.11], kind == "busin" ? Green : Amber, detail: true));
            o.Add(("", p));
        }
        return o;
    }

    /// <summary>
    /// The operator station of a production line (docs/sandbox/constructs/production.md "Production lines"): a desk with a screen. It is a
    /// Switch: its output, wired to each machine's Run, is the line; the screen lists those machines, and its buttons start and stop the
    /// line, pick a machine and step that machine's recipe (ProductionLines.cs).
    /// </summary>
    private static (string, Part) LineConsole()
    {
        var p = new Part("Line Console", Small, "Controls", "Runs a production line: wire Run to each machine's Run, Data out to any display. Outputs Alarm, Running and Made.",
            role: "logic", tool: "welder", color: "#e0b020", group: "Logic");
        p.Add(Box([-0.45, 0, -0.25], [0.45, 1.0, 0.25])); // the desk
        p.Add(Box([-0.43, 0.99, -0.24], [0.43, 1.62, -0.1])); // the screen's housing, standing on the desk's back
        // The look (look.md, "Machine look"): a desk, not a block. A dark plinth and knee recess leave the body colour as two pedestals and a
        // top rail; a steel work top with the buttons a hand finds (accent, and a red stop); a dark hood and cheeks round the screen.
        PyDict? L(double[] mn, double[] mx, string c, string? f, PyDict? shape = null) => MachineLook.D(mn, mx, c, f, shape: shape);
        MachineLook.Skirt(p, -0.45, 0.45, -0.25, 0.25, 0, 0.1, 0.012);
        p.Add(L([-0.3, 0.1, 0.25], [0.3, 0.82, 0.264], MachineLook.Base, null)); // the knee recess
        p.Add(L([-0.3, 0.1, 0.262], [0.3, 0.13, 0.36], MachineLook.Base, "tread plate")); // a foot rest
        p.Add(L([-0.47, 0.985, -0.04], [0.47, 1.03, 0.3], MachineLook.Steel, "steel")); // the work top, overhanging the front
        p.Add(L([-0.47, 0.94, 0.286], [0.47, 0.985, 0.3], MachineLook.Base, null)); // its front lip
        for (var i = 0; i < 4; i++)
            p.Add(L([-0.36 + i * 0.13, 1.03, 0.1], [-0.27 + i * 0.13, 1.05, 0.19], MachineLook.Accent, null)); // buttons
        p.Add(L([0.24, 1.03, 0.08], [0.36, 1.07, 0.2], MachineLook.Stop, null, shape: D(("shape", "cylinder"), ("segments", 10), ("front", "+y")))); // the stop
        p.Add(L([-0.47, 1.0, -0.26], [-0.43, 1.66, -0.04], MachineLook.Base, null)); // the screen's cheeks and hood
        p.Add(L([0.43, 1.0, -0.26], [0.47, 1.66, -0.04], MachineLook.Base, null));
        p.Add(L([-0.47, 1.62, -0.26], [0.47, 1.67, 0.0], MachineLook.Base, null));
        p.Add(L([-0.43, 1.0, -0.262], [0.43, 1.62, -0.24], MachineLook.Base, null)); // its back
        p.Add(L([0.22, 0.1, -0.275], [0.4, 0.98, -0.25], MachineLook.Base, null)); // a cable trunk down the back, clear of the socket
        p.Add(L([-0.462, 0.2, -0.15], [-0.45, 0.8, 0.15], MachineLook.Base, null)); // side vents
        p.Add(L([0.45, 0.2, -0.15], [0.462, 0.8, 0.15], MachineLook.Base, null));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 0.75, -0.25], "-z");
        p.Stats(40, 120, 2, 3.0);
        // (on the bill page the buttons are Add, Send, Clear and Line: the work orders, WorkOrders.cs)
        var touch = Py.L(
            D(("action", "line.run"), ("at", Py.L(0.02, 0.02, 0.18, 0.13)), ("label", "Start")),
            D(("action", "line.next"), ("at", Py.L(0.19, 0.02, 0.35, 0.13)), ("label", "Next")),
            D(("action", "line.recipe"), ("at", Py.L(0.36, 0.02, 0.52, 0.13)), ("label", "Recipe")),
            D(("action", "line.page"), ("at", Py.L(0.53, 0.02, 0.69, 0.13)), ("label", "Bill")),
            D(("action", "view.up"), ("at", Py.L(0.72, 0.02, 0.85, 0.13)), ("label", "Up")), // (Up and Down scroll this player's own view of a long list)
            D(("action", "view.down"), ("at", Py.L(0.86, 0.02, 0.98, 0.13)), ("label", "Down")));
        p.Set(("logic", D(("kind", "switch"))),
            ("config", Py.L(
                D(("key", "startOn"), ("label", "Starts running"), ("group", "General"), ("default", false), ("tooltip", "The line runs as soon as it is built.")),
                D(("key", "lineStopAfter"), ("label", "Stop after"), ("group", "General"), ("unit", "pieces"), ("min", 0), ("max", 9999), ("step", 1), ("default", 0),
                    ("tooltip", "Stops the line when it has made this many pieces since Start (its Made output). 0: it never stops by itself.")),
                D(("key", "linePick"), ("label", "Machine"), ("group", "General"), ("min", 0), ("max", 31), ("step", 1), ("default", 0), ("hidden", true), ("tooltip", "Which machine its buttons work on.")),
                D(("key", "linePage"), ("label", "Page"), ("group", "General"), ("options", Py.L("Line", "Bill")), ("default", 0), ("hidden", true), ("tooltip", "The line or the bill on its screen.")),
                D(("key", "orderKind"), ("label", "Kind"), ("group", "Bill line"), ("options", Py.L("Piece", "Wheel")), ("default", 0), ("tooltip", "What Add puts on the bill: an Extruder's piece or a Wheel Former's wheel.")),
                D(("key", "profile"), ("label", "Profile"), ("group", "Bill line"), ("options", ExtrusionParts.Profiles()), ("default", 4), ("tooltip", "A piece's section. Round and Pipe take their diameter from Width; Custom needs a machine's cell grid.")),
                D(("key", "width"), ("label", "Width"), ("group", "Bill line"), ("unit", "m"), ("min", 0.1), ("max", 32), ("step", 0.05), ("default", 0.25), ("tooltip", "Across a piece's section, or a wheel's width.")),
                D(("key", "height"), ("label", "Height"), ("group", "Bill line"), ("unit", "m"), ("min", 0.1), ("max", 48), ("step", 0.05), ("default", 0.25), ("tooltip", "Up a piece's section, or a wheel's diameter.")),
                D(("key", "wall"), ("label", "Pipe wall"), ("group", "Bill line"), ("unit", "m"), ("min", 0.01), ("max", 2), ("step", 0.01), ("default", 0.03), ("tooltip", "A Pipe's wall.")),
                D(("key", "cutLength"), ("label", "Length"), ("group", "Bill line"), ("unit", "m"), ("min", 0.05), ("max", 192), ("step", 0.05), ("default", 1), ("tooltip", "A piece's length.")),
                D(("key", "orderMaterial"), ("label", "Material"), ("group", "Bill line"), ("options", Py.L("Metal", "Steel", "Bronze", "Laminate", "Copper", "Wood", "Planks", "Stone", "Concrete", "Glass", "Rubber")), ("default", 0),
                    ("tooltip", "What it is made of. A machine takes an order only in the material it is fed: another material is another line.")),
                D(("key", "orderCount"), ("label", "Count"), ("group", "Bill line"), ("min", 1), ("max", 999), ("step", 1), ("default", 1), ("tooltip", "How many of it.")),
                Ink)),
            ("panels", Py.L(D(("source", "line"), ("at", Py.L(0, 1.31, -0.097)), ("size", Py.L(0.82, 0.56)), ("face", "+z"), ("up", "+y"), ("rows", 10), ("range", 12), ("touch", touch)))));
        return ("", p);
    }

    private static List<(string, Part)> Controls()
    {
        var code = Py.L(
            D(("key", "code"), ("label", "Code"), ("group", "General"), ("min", 0), ("max", 999999), ("step", 1), ("default", 0), ("tooltip", "The number that opens it. Only you can see or change it.")),
            D(("key", "hold"), ("label", "Stays open"), ("group", "General"), ("unit", "s"), ("min", 0), ("max", 600), ("step", 0.5), ("default", 0), ("tooltip", "0: until Clear.")),
            D(("key", "invert"), ("label", "Locked is on"), ("group", "General"), ("default", false), ("tooltip", "On until opened: wire it straight into a hinge's Lock.")));
        var entry = Py.L(
            D(("key", "value"), ("label", "Value"), ("group", "General"), ("min", -1000000), ("max", 1000000), ("default", 0), ("tooltip", "What it puts out.")),
            With(Decimals, ("default", 0), ("tooltip", "Where the point goes in what you type.")));
        return
        [
            Keypad("Numpad", "numpad", "Type a number, Accept puts it out, Clear resets.", entry),
            Keypad("Code Lock", "codelock", "The right code turns it on.", code),
            Analog("Rotary Knob", "Controls", "knob", snap: 0.1),
            Analog("Medium Rotary Knob", "Controls", "knob", s: 5, snap: 0.1, tool: "welder", mass: 30, cls: Medium, health: 180, armour: 4),
            Analog("Slider", "Controls", "slide"),
            Analog("Medium Slider", "Controls", "slide", s: 5, tool: "welder", mass: 30, cls: Medium, health: 180, armour: 4),
            Toggle("Toggle Switch", "switch", "toggle"),
            Toggle("Rocker Switch", "switch", "rocker"),
            Toggle("Mushroom Button", "button", "mushroom", config: ButtonCfg()),
            Toggle("Medium Mushroom Button", "button", "mushroom", s: 5, tool: "welder", mass: 30, cls: Medium, health: 180, armour: 4, config: ButtonCfg()),
            KeySwitch(),
            KeyItem(),
            LineConsole(),
        ];
    }

    public void Make(PartOutput o)
    {
        List<(string, Part)> parts =
        [
            Module("Driving Module", "drivemodule", "Link a seat in and its drive output to the drivetrain: that seat drives. The controls are outputs too.", "#5ac060"),
            Module("Turret Module", "turretmodule", "Link a seat in and its guns output to a gun: that seat aims and fires it.", "#e06040", Stabilised()),
            Lever("Analog Lever", Small, 1, 3, 40, 1, "wrench"),
            Lever("Medium Analog Lever", Medium, 5, 60, 180, 4, "welder"),
            Lever("Large Analog Lever", Large, 20, 600, 600, 8, "welder"),
            Joystick("Joystick", Small, 1, 3, 40, 1, "wrench"),
            Joystick("Medium Joystick", Medium, 5, 60, 180, 4, "welder"),
            Joystick("Large Joystick", Large, 20, 600, 600, 8, "welder"),
            .. Controls(),
            .. Displays(),
            .. Blocks(),
            .. Sensors(),
            .. Bundling(),
        ];
        WriteParts(o, Out, parts);
    }
}
