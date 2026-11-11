using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The air cargo parts and blueprints: Constructs/Parts/AirCargo, and the Sky Crane, its pods and the crashed aircraft a world is salvaged
/// for (docs/sandbox/CONSTRUCTS.md, "Flight in survival"). Was tools/constructs/air_cargo.py.
/// What a craft lifts is any body with a Tow Eye, on a Cargo Winch: the only new part. The rest are blueprints of parts that exist.
/// The winch's recipe comes from the part kit's assembling (--gen-parts assembling).
/// </summary>
public sealed class AirCargo : IPartGenerator
{
    public string Name => "air_cargo";

    private const string Out = "Constructs/Parts/AirCargo";
    private const string Format = "call-of-slop-construct-blueprint/1";
    private const string Dark = "#30343a", Gold = "#c8a13a";
    private const int WingLeft = 0, WingRight = 0, Fin = 1; // (the turn about a socket that keeps a piece's lift side up, and a fin's to the side)

    private static Part CargoWinch()
    {
        var h = 0.2;
        var p = new Part("Cargo Winch 16 m", Small, "Air cargo", "A winch a pilot works from the seat: grab takes the nearest eye within its 16 m, or lets go; reel takes it in, pay out lets it down. Lifts 3 t. Needs power.", role: "hitch", tool: "wrench", color: Dark, group: "Vehicles");
        p.Add(Box([-h, -h, -h], [h, h, h]));
        p.Add(Box([-0.05, -0.27, -0.05], [0.05, -h, 0.05], Gold, detail: true));
        p.Sock("+y", [0, h, 0], "+y");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+z", [0, 0, h], "+z");
        p.Sock("-z", [0, 0, -h], "-z");
        p.Stats(35, 200, 6, 3);
        p.Set(("hitch", Kw(("kind", "hook"), ("at", Py.L(0, -0.27, 0)), ("chain", 16), ("winch", Kw(("min", 1), ("speed", 3), ("rating", 54000), ("grab", true))))), ("power", 800));
        return p;
    }

    private static PyDict Blueprint(string name, string description, List<object?> steps, List<object?>? links = null, List<object?>? loot = null)
    {
        var d = Kw(("format", Format), ("name", name), ("description", description), ("steps", steps), ("links", links ?? []));
        if (loot is { Count: > 0 })
            d["loot"] = loot;
        return d;
    }

    private const string FanPart = "Extrusion fan3.straight.6 0x200x0 metal"; // (a 2 m ducted fan off a Fan Former)

    /// <summary>A step list and add(**kw): each step's keys in the order given; returns its index.</summary>
    private sealed class Steps
    {
        public readonly List<object?> S = [];

        public int Add(params (string, object?)[] kw)
        {
            S.Add(Kw(kw));
            return S.Count - 1;
        }
    }

    /// <summary>A hub with four arms, every weight paired across it and a Cargo Winch under its middle, where a load hangs through the centre of mass. The stabilizer finds its fans by itself.</summary>
    private static PyDict SkyCrane()
    {
        var s = new Steps();
        var hub = s.Add(("part", "Frame Block"));
        var arms = new[] { "+x", "-x", "+z", "-z" }.Select(side => s.Add(("part", "Square Beam 2 m"), ("on", hub), ("socket", side), ("by", "-z"))).ToArray();
        int[] hands = [1, 1, 0, 0];
        for (var i = 0; i < arms.Length; i++)
        {
            s.Add(("part", FanPart), ("on", arms[i]), ("socket", "+y7"), ("by", "base"), ("config", Kw(("hand", hands[i]))));
            s.Add(("part", "Square Beam 1 m"), ("on", arms[i]), ("socket", "-y6"), ("by", "+z")); // (a leg: it stands on these, with its winch clear of the ground)
        }
        var seat = s.Add(("part", "Seat"), ("on", hub), ("socket", "+y"), ("by", "bottom"));
        s.Add(("part", "Battery Pack"), ("on", arms[2]), ("socket", "-y2"), ("by", "+y"));
        var pack = s.Add(("part", "Battery Pack"), ("on", arms[3]), ("socket", "-y2"), ("by", "+y"));
        s.Add(("part", "Flight Stabilizer"), ("on", arms[0]), ("socket", "+y2"), ("by", "-y"), ("config", Kw(("keys", "Rotorcraft"), ("apMode", "Hover"), ("maxTilt", 15))));
        s.Add(("part", "Flight Control Panel"), ("on", pack), ("socket", "+x"), ("by", "back"));
        var winch = s.Add(("part", "Cargo Winch 16 m"), ("on", hub), ("socket", "-y"), ("by", "+y"));
        var links = new List<object?>();
        foreach (var (key, sock, port) in new[] { ("fire", "-y2", "grab"), ("aim", "-y3", "reel"), ("reload", "-y4", "pay out") })
        {
            var reader = s.Add(("part", "Input Reader"), ("on", arms[0]), ("socket", sock), ("by", "+y"), ("config", Kw(("setting", key))));
            links.Add(Kw(("type", "data"), ("from", seat), ("to", reader)));
            links.Add(Kw(("type", "data"), ("from", reader), ("to", winch), ("toPort", port)));
        }
        return Blueprint("Sky Crane", "Four ducted fans on a hub, a Cargo Winch under it. Hover over a Tow Eye with the panel's autopilot: fire grabs or lets go, aim reels in, reload lets out. Shift lifts off, Ctrl lands.", s.S, links);
    }

    private static PyDict St(string part, int on, string socket, string by) => Kw(("part", part), ("on", on), ("socket", socket), ("by", by));

    /// <summary>A deck and a frozen cargo volume under a Tow Eye: what stands in the box flies with it.</summary>
    private static PyDict CargoPod()
    {
        var s = Py.L(Kw(("part", "Frame Block")));
        s.Add(St("Tow Eye", 0, "+y", "bottom"));
        s.Add(Kw(("part", "Cargo Volume"), ("on", 0), ("socket", "-y"), ("by", "+y"), ("config", Kw(("startOn", 1), ("sizeX", 2), ("sizeY", 1.5), ("sizeZ", 2), ("atY", -1.0)))));
        foreach (var side in new[] { "+x", "-x", "+z", "-z" })
            s.Add(St("Square Beam 1 m", 0, side, "-z"));
        return Blueprint("Cargo Pod", "A Cargo Volume under a Tow Eye: load it on the ground, freeze it, and a Sky Crane carries what is in it.", s);
    }

    /// <summary>A Drill Rig with a battery and a Tow Eye: lowered on a winch it sits on a deposit and fills its bin.</summary>
    private static PyDict DrillPod()
    {
        var s = Py.L(Kw(("part", "Drill Module")),
            St("Battery", 0, "-x", "+x"),
            St("Materials Bin", 0, "+x", "-x"),
            St("Square Beam 0.5 m", 0, "back", "-z"),
            St("Square Beam 0.5 m", 3, "-y1", "+z"), // (a foot: with the bit's mast it stands up)
            St("Square Beam 1 m", 3, "+y1", "-z"),
            St("Tow Eye", 5, "+z", "bottom"));
        return Blueprint("Drill Pod", "A Drill Module with a battery and a bin, on a Tow Eye: a Sky Crane lowers it onto a deposit, waits, and lifts it out, while the bin is still light enough.", s,
            Py.L(Kw(("type", "chute"), ("from", 0), ("to", 2), ("fromPort", "Chute out"))));
    }

    /// <summary>What a light aircraft leaves: wings and a tail still on, its Chest of spares thrown open by nobody.</summary>
    private static PyDict CrashedPlane()
    {
        var s = Py.L(Kw(("part", "Frame Block")),
            St("Wing Segment", 0, "+x", "-x"),
            St("Fabric Wing Segment", 1, "+x", "-x"),
            St("Square Beam 2 m", 0, "+z", "-z"),
            St("Tail Plane", 3, "+z", "front"),
            St("Tail Fin", 4, "+y", "-y"),
            St("Rudder", 5, "back", "hinge"),
            St("Square Beam 1 m", 0, "-z", "-z"),
            St("Two-Blade Prop", 7, "+z", "base"),
            St("Chest", 0, "+y", "bottom"));
        return Blueprint("Crashed Plane", "A light plane that came down: salvage its wings and tail, and see what its Chest holds.", s, loot: Py.L(Kw(("step", 9), ("table", "aircraft"))));
    }

    /// <summary>A fast one: a jet on a keel, one swept wing left, a Chest of avionics.</summary>
    private static PyDict CrashedJet()
    {
        var s = Py.L(Kw(("part", "Frame Block")),
            St("Light Turbojet", 0, "+z", "front"),
            St("Swept Wing L", 0, "-x", "+x"),
            St("Aileron", 2, "+y", "hinge"),
            St("Chest", 0, "+y", "bottom"));
        return Blueprint("Crashed Jet", "A jet that came down: its turbojet may be worth more than the Chest it carried.", s, loot: Py.L(Kw(("step", 4), ("table", "aircraft"))));
    }

    private const string WingPart = "Extrusion wing-wing 150x0x400 cloth";

    /// <summary>The survival plane: what a Workshop-tier base makes. Cloth off an Airframe Former, a wooden prop off a Fan Former, an Engine; a left piece welds by its base, a right one by its tip.</summary>
    private static PyDict FabricFlyer()
    {
        var s = new Steps();
        int A(string part, int on, string socket, string by, params (string, object?)[] more) => s.Add([("part", part), ("on", on), ("socket", socket), ("by", by), .. more]);
        var root = s.Add(("part", "Frame Block"));
        var engine = A("Engine", root, "-z", "-z");
        var prop = A("Extrusion fan0.straight.3 0x150x0 wood", engine, "+z", "base");
        var seat = A("Seat", root, "+y", "bottom");
        var tank = A("Fuel Tank", root, "+z", "-z");
        var boom = A("Square Beam 2 m", tank, "+z", "-z");
        var end = A("Frame Block", boom, "+z", "-z");
        var left = A(WingPart, tank, "-x", "base", ("spin", WingLeft));
        var right = A(WingPart, tank, "+x", "tip", ("spin", WingRight));
        A("Battery", tank, "-y", "+y");
        var ailL = A("Extrusion wing-aileron 50x0x200 cloth", left, "+x2", "-x0", ("spin", 0));
        var ailR = A("Extrusion wing-aileron 50x0x200 cloth", right, "+x1", "-x0", ("spin", 0));
        var tailL = A("Extrusion wing-tail 100x0x100 cloth", end, "-x", "base", ("spin", WingLeft));
        var tailR = A("Extrusion wing-tail 100x0x100 cloth", end, "+x", "tip", ("spin", WingRight));
        var elevL = A("Extrusion wing-elevator 40x0x100 cloth", tailL, "+x0", "-x0", ("spin", 0));
        var elevR = A("Extrusion wing-elevator 40x0x100 cloth", tailR, "+x0", "-x0", ("spin", 0));
        var fin = A("Extrusion wing-fin 100x0x100 cloth", end, "+y", "base", ("spin", Fin));
        var rudd = A("Extrusion wing-rudder 40x0x100 cloth", fin, "+x0", "-x0", ("spin", 0));
        var stab = A("Flight Stabilizer", tank, "+y", "-y", ("config", Kw(("keys", "Plane"))));
        var leg = A("Square Beam 0.5 m", root, "-y", "+z");
        var axle = A("Square Beam 2 m", leg, "-z", "+y3", ("spin", 1));
        foreach (var side in new[] { "-z", "+z" })
        {
            var gear = A("Suspension", axle, side, "mount");
            A("Wheel", gear, "axle", "axle");
        }
        var strut = A("Square Beam 0.5 m", boom, "-y3", "+z"); // (as long as the nose leg: it stands level, so its wheels don't shove it along)
        var block = A("Frame Block", strut, "-z", "+y");
        foreach (var face in new[] { "-x", "+x" })
        {
            var gear = A("Suspension", block, face, "mount");
            A("Wheel", gear, "axle", "axle");
        }
        var links = Py.L(
            Kw(("type", "data"), ("from", seat), ("to", stab)),
            Kw(("type", "data"), ("from", stab), ("fromPort", "throttle"), ("to", prop)),
            Kw(("type", "data"), ("from", stab), ("fromPort", "pitch"), ("to", elevL)),
            Kw(("type", "data"), ("from", stab), ("fromPort", "pitch"), ("to", elevR)),
            Kw(("type", "data"), ("from", stab), ("fromPort", "yaw"), ("to", rudd)),
            Kw(("type", "data"), ("from", stab), ("fromPort", "left aileron"), ("to", ailL)),
            Kw(("type", "data"), ("from", stab), ("fromPort", "right aileron"), ("to", ailR)),
            Kw(("type", "pipe"), ("from", tank), ("to", engine), ("fromPort", "Fuel out"), ("toPort", "Fuel in")));
        return Blueprint("Fabric Flyer", "The plane a workshop can make: cloth off an Airframe Former, a wooden prop off a Fan Former, an Engine. Shift and Ctrl set the throttle, S lifts the nose, A and D bank and turn.", s.S, links);
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [("", CargoWinch())]);
        foreach (var (name, d) in new[] { ("SkyCrane", SkyCrane()), ("CargoPod", CargoPod()), ("DrillPod", DrillPod()), ("CrashedPlane", CrashedPlane()), ("CrashedJet", CrashedJet()), ("FabricFlyer", FabricFlyer()) })
            o.File($"Constructs/Blueprints/{name}.json", PyJson.Dumps(d, 2) + "\n");
    }
}
