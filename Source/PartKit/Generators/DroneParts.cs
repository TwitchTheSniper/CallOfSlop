using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The wireless seat link's and the cameras' parts (Constructs/Parts/Remote), and the Scout Drone and Control Post blueprints
/// (docs/sandbox/CONSTRUCTS.md, "Drones" and "Cameras and screens"). Was tools/constructs/drone_parts.py.
/// A Drone Receiver has the seat role with nobody in it, so a drone needs no seat. A Remote Console is a seat that sends; a Remote
/// Transmitter lets any seat send. A Camera and a Camera Screen meet on a channel. Recipes come from tools/constructs/assembling.py.
/// </summary>
public sealed class DroneParts : IPartGenerator
{
    public string Name => "drone_parts";

    private const string Out = "Constructs/Parts/Remote";
    private const string Section = "Remote";
    private const string Case = "#3f4c5a", Dark = "#23272c", Glass = "#1b2a33", Lamp = "#39c46a", Mast = "#8a939c";

    private static PyDict Channel() => Kw(("key", "channel"), ("label", "Channel"), ("group", "General"), ("min", 1), ("max", 99), ("step", 1), ("default", 1),
        ("tooltip", "Parts on the same channel talk: a sender and a receiver, a camera and a screen."));

    private static void Cube(Part p, double half, double h)
    {
        p.Add(Box([-half, 0, -half], [half, h, half]));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [half, h / 2, 0], "+x");
        p.Sock("-x", [-half, h / 2, 0], "-x");
        p.Sock("back", [0, h / 2, half], "+z");
    }

    private static Part Receiver()
    {
        var p = new Part("Drone Receiver", Small, Section, "The seat of a thing with no seat. Link it into a Driving or Turret Module: a console or transmitter on its channel then drives it.", role: "seat", tool: "wrench", group: "Logic", color: Case);
        Cube(p, 0.12, 0.1);
        p.Add(Box([-0.01, 0.1, -0.01], [0.01, 0.3, 0.01], Mast, detail: true));
        p.Add(Box([-0.05, 0.1, -0.1], [0.05, 0.12, -0.08], Lamp, detail: true));
        p.Stats(3, 60, 2, 1);
        p.Set(("power", 30), ("seat", Kw(("at", Py.L(0, 0, 0)))), ("remote", Kw(("receiver", true))), ("config", Py.L(Channel())));
        return p;
    }

    private static Part Transmitter(string name, Cls cls, int rng, int power, int mass, double half, double h, double mast)
    {
        var p = new Part(name, cls, Section, $"Wire a seat into it: the seat's controls go out on its channel, up to {Py.G(rng / 1000.0)} km.", role: "logic", tool: mass <= 40 ? "wrench" : "welder", group: "Logic", color: Case);
        Cube(p, half, h);
        p.Add(Box([-0.02, h, -0.02], [0.02, h + mast, 0.02], Mast, detail: true));
        p.Stats(mass, 80, 2, 1.5);
        p.Set(("power", power), ("logic", Kw(("kind", "remotelink"))), ("remote", Kw(("range", rng))), ("config", Py.L(Channel())));
        return p;
    }

    private static Part Console()
    {
        var p = new Part("Remote Console", Small, Section, "A seat with a screen and a transmitter. Sit in it to fly what is on its channel, up to 800 m, seeing through its camera.", role: "seat", tool: "wrench", group: "Logic", color: Case);
        p.Add(Box([-0.25, 0, -0.25], [0.25, 0.45, 0.3]));
        p.Add(Box([-0.25, 0.45, 0.2], [0.25, 1.05, 0.3]));
        p.Add(Box([-0.4, 0, -0.75], [0.4, 0.7, -0.5], Dark));
        p.Add(Box([-0.42, 0.7, -0.78], [0.42, 1.2, -0.72], Dark));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 0.5, 0.3], "+z");
        p.Sock("+x", [0.25, 0.15, 0], "+x");
        p.Sock("-x", [-0.25, 0.15, 0], "-x");
        p.Stats(70, 200, 3, 3);
        p.Set(("power", 120), ("seat", Kw(("at", Py.L(0, -0.55, 0)))), ("remote", Kw(("range", 800))), ("config", Py.L(Channel())),
            ("panels", Py.L(Kw(("source", "camera"), ("at", Py.L(0, 0.95, -0.716)), ("size", Py.L(0.74, 0.42)), ("face", "+z"), ("up", "+y"), ("rows", 3), ("range", 8)))));
        return p;
    }

    private static Part Camera(string name, Cls cls, double half, double deep, int fov, string desc)
    {
        var p = new Part(name, cls, Section, desc, tool: cls == Small ? "wrench" : "welder", group: "Logic", color: Dark, role: "panel");
        p.Add(Box([-half, -half, -deep], [half, half, deep]));
        p.Add(Box([-half * 0.6, -half * 0.6, -deep - half * 0.5], [half * 0.6, half * 0.6, -deep], Glass, detail: true));
        p.Sock("back", [0, 0, deep], "+z");
        p.Sock("-y", [0, -half, 0], "-y");
        p.Sock("+y", [0, half, 0], "+y");
        p.Sock("+x", [half, 0, 0], "+x");
        p.Sock("-x", [-half, 0, 0], "-x");
        p.Stats(cls == Small ? 1.5 : 30, cls == Small ? 40 : 200, 1, 1);
        p.Set(("camera", Kw(("at", Py.L(0, 0, Py.Round(-deep - half * 0.5, 3))), ("fov", fov))), ("config", Py.L(Channel())), ("centreSockets", false));
        return p;
    }

    private static Part Screen(string name, Cls cls, double w, double h, int rng, int power, int mass)
    {
        var p = new Part(name, cls, Section, $"Shows the camera on its channel, while someone stands within {Py.G(rng)} m and looks at it.", role: "panel", tool: mass <= 40 ? "wrench" : "welder", group: "Logic", color: Dark);
        var t = cls == Small ? 0.04 : 0.12;
        p.Add(Box([-w / 2, -h / 2, 0], [w / 2, h / 2, t]));
        p.Sock("back", [0, 0, t], "+z");
        p.Sock("-y", [0, -h / 2, t / 2], "-y");
        p.Sock("+y", [0, h / 2, t / 2], "+y");
        p.Stats(mass, 50, 1, 1);
        p.Set(("power", power), ("config", Py.L(Channel())), ("centreSockets", false),
            ("panels", Py.L(Kw(("source", "camera"), ("at", Py.L(0, 0, Py.Round(-0.004 * (cls == Small ? 1 : 2), 3))), ("size", Py.L(Py.Round(w * 0.97, 3), Py.Round(h * 0.95, 3))),
                ("face", "-z"), ("up", "+y"), ("rows", 3), ("range", rng)))));
        return p;
    }

    private static Part Rotor(string name, int force, double power, double mass, double r)
    {
        var p = new Part(name, Small, Section, $"A small electric rotor: {Py.G(force)} N, pushing away from its nozzle. Light enough for a drone's frame.", role: "thruster", tool: "wrench", group: "Logic", color: Case);
        p.Add(Box([-r, -r, -0.06], [r, r, 0.06]));
        p.Add(Box([-r * 1.3, -r * 1.3, 0.03], [r * 1.3, r * 1.3, 0.08], Dark, detail: true));
        p.Sock("front", [0, 0, -0.06], "-z");
        p.Sock("+x", [r, 0, 0], "+x");
        p.Sock("-x", [-r, 0, 0], "-x");
        p.Stats(mass, 40, 1, 1);
        p.Set(("thruster", Kw(("force", force), ("spool", 0.15), ("exhaust", "+z"), ("at", Py.L(0, 0, 0.06)), ("fuel", "electric"), ("power", power))));
        return p;
    }

    /// <summary>One piece: a cross a metre across, rotor sockets under its arms and two at the back, and the rest on top.</summary>
    private static Part Frame()
    {
        var p = new Part("Drone Frame", Small, Section, "A light cross for a drone: rotors under its arms, two pushers at its back, the rest on top.", tool: "wrench", group: "Logic", color: Case);
        double t = 0.05, h = 0.05;
        p.Add(Box([-0.5, -h, -t], [0.5, h, t]));
        p.Add(Box([-t, -h, -0.5], [t, h, -t], Case));
        p.Add(Box([-t, -h, t], [t, h, 0.5], Case));
        p.Add(Box([-0.1, -h, -0.1], [0.1, 0.06, 0.1], Dark, detail: true));
        foreach (var (name, x, z) in new[] { ("r0", -0.45, 0.0), ("r1", 0.45, 0.0), ("r2", 0.0, -0.45), ("r3", 0.0, 0.45) })
            p.Sock(name, [x, -h, z], "-y");
        p.Sock("nose", [0, 0, -0.5], "-z");
        p.Sock("pushL", [-0.45, 0, t], "+z");
        p.Sock("pushR", [0.45, 0, t], "+z");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("topF", [0, h, -0.3], "+y");
        p.Sock("topB", [0, h, 0.3], "+y");
        p.Stats(8, 120, 1, 1.5);
        p.Set(("centreSockets", false));
        return p;
    }

    /// <summary>Four rotors to lift it, two pushers at the back to drive and turn it. W, A, S, D and the hover hold work them by how they are mounted.</summary>
    private static PyDict Scout()
    {
        var steps = Py.L(Kw(("part", "Drone Frame")));
        int Add(string part, string socket, string by)
        {
            steps.Add(Kw(("part", part), ("on", 0), ("socket", socket), ("by", by)));
            return steps.Count - 1;
        }

        var fans = new List<int>();
        foreach (var s in new[] { "r0", "r1", "r2", "r3" })
            fans.Add(Add("Drone Rotor", s, "front"));
        foreach (var s in new[] { "pushL", "pushR" })
            fans.Add(Add("Drone Pusher", s, "front"));
        Add("Drone Battery", "top", "-y");
        var rx = Add("Drone Receiver", "topF", "bottom");
        var module = Add("Driving Module", "topB", "-y");
        Add("Camera", "nose", "back");
        var links = Py.L(Kw(("type", "data"), ("from", rx), ("to", module)));
        foreach (var fan in fans)
            links.Add(Kw(("type", "data"), ("from", module), ("to", fan), ("toPort", "drive")));
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", "Scout Drone"),
            ("description", "Rotors, a battery, a receiver and a camera, no seat. Fly it from a Remote Console on channel 1: Sprint lifts it into its hover hold."),
            ("steps", steps), ("links", links));
    }

    private static Part Battery()
    {
        var p = new Part("Drone Battery", Small, Section, "A small battery, a few minutes' flight.", role: "battery", tool: "wrench", group: "Logic", color: "#2f3a33");
        p.Add(Box([-0.1, 0, -0.08], [0.1, 0.08, 0.08]));
        p.Sock("-y", [0, 0, 0], "-y");
        p.Sock("+y", [0, 0.08, 0], "+y");
        p.Stats(5, 60, 1, 1);
        p.Set(("battery", Kw(("capacity", 400))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0.1, 0.04, 0)), ("face", "+x")))));
        return p;
    }

    private static PyDict ControlPost() => Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", "Control Post"),
        ("description", "A Remote Console on a slab with a battery: sit in it to fly the drone on channel 1. A Camera Screen beside it shows its camera to anyone near."),
        ("steps", Py.L(
            Kw(("part", "Foundation")),
            Kw(("part", "Remote Console"), ("on", 0), ("socket", "top11"), ("by", "bottom")),
            Kw(("part", "Battery"), ("on", 0), ("socket", "top33"), ("by", "-y")))));

    public void Make(PartOutput o)
    {
        WriteParts(o, Out,
        [
            ("", Receiver()), ("", Console()),
            ("", Transmitter("Remote Transmitter", Small, 800, 60, 6, 0.12, 0.2, 0.5)),
            ("", Transmitter("Medium Remote Transmitter", Medium, 3000, 300, 120, 0.5, 1, 2)),
            ("", Frame()),
            ("", Rotor("Drone Rotor", 200, 0.6, 2.5, 0.08)),
            ("", Rotor("Drone Pusher", 60, 0.2, 1.2, 0.06)),
            ("", Battery()),
            ("", Camera("Camera", Small, 0.06, 0.08, 70, "A small camera. It films along its lens for a screen on its channel, and does nothing until a screen near a player asks.")),
            ("", Camera("Medium Camera", Medium, 0.5, 0.5, 60, "A big camera for a big construct.")),
            ("", Screen("Camera Screen", Small, 0.5, 0.3, 8, 20, 3)),
            ("", Screen("Large Camera Screen", Small, 1, 0.6, 10, 40, 8)),
            ("", Screen("Medium Camera Screen", Medium, 4, 2, 25, 200, 90)),
        ]);
        o.File("Constructs/Blueprints/ScoutDrone.json", PyJson.Dumps(Scout(), 2) + "\n");
        o.File("Constructs/Blueprints/ControlPost.json", PyJson.Dumps(ControlPost(), 2) + "\n");
    }
}
