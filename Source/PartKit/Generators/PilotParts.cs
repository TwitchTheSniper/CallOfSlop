using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// What a pilot wears and sits in: Constructs/Parts/Pilot, and the carrier demo blueprints (CarrierDeck, DeckPlane).
/// docs/sandbox/CONSTRUCTS.md, "Carrier work", "Pilot safety". Was tools/constructs/pilot_parts.py. The Deck Plane is the Light Plane
/// (Constructs/Blueprints/LightPlane.json, written by WingParts) with an ejection seat: run this after changing it.
/// </summary>
public sealed class PilotParts : IPartGenerator
{
    public string Name => "pilot_parts";

    private const string Out = "Constructs/Parts/Pilot";
    private const string Format = "call-of-slop-construct-blueprint/1";
    private const string Save = "#4f6b3c";
    private const double Pack = 0.095; // half a pack: it fits a backpack's small pouch

    private static Part Worn(string name, string wear, string desc, double mass, string color, string trim)
    {
        var p = new Part(name, Small, "Pilot gear", desc, role: "panel", tool: "wrench", color: color, group: "Flight");
        p.Add(Box([-Pack, 0, -Pack], [Pack, Pack * 1.6, Pack]));
        p.Add(Box([-Pack * 0.6, Pack * 1.6, -Pack * 0.6], [Pack * 0.6, Pack * 1.85, Pack * 0.6], trim, detail: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(mass, 60, 1, 1.0);
        p.Set(("wear", wear));
        return p;
    }

    private static Part EjectSeat()
    {
        var p = new Part("Ejection Seat", Small, "Seats and helms", "A seat with a handle: the key or a signal throws its rider clear on a parachute.", role: "seat", tool: "wrench", color: "#3a3f46", group: "Seats");
        p.Add(Box([-0.25, 0, -0.3], [0.25, 0.25, 0.3]));
        p.Add(Box([-0.25, 0.25, 0.15], [0.25, 1.05, 0.3], "#2f343a", shape: Kw(("use", true))));
        p.Add(Box([-0.1, 1.05, 0.18], [0.1, 1.15, 0.27], "#d8b020", detail: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(40, 120, 3, 1.5);
        p.Set(("seat", Kw(("at", Py.L(0, 0.25, 0)), ("eject", true))));
        return p;
    }

    private static List<(string, Part)> Parts() =>
    [
        ("Gear", Worn("Parachute", "parachute", "In a backpack: jump while falling opens it, jump again cuts it away; it comes off on landing.", 4, Save, "#c8c0a8")),
        ("Gear", Worn("Backpack Glider", "glider", "In a backpack: jump while falling opens it. W dives, S flares, A and D turn.", 6, "#35608a", "#d8d8d8")),
        ("Gear", Worn("Jetpack", "jetpack", "In a backpack: hold jump for four seconds of flight. The ground refills it.", 9, "#5c6066", "#c8642a")),
        ("Seats", EjectSeat()),
    ];

    /// <summary>Catapult and arrestor in line, flush, a row of platforms each side: land over the stern, stop, roll on, launch off the bow.</summary>
    private static PyDict Deck()
    {
        var s = new List<object?> { Kw(("part", "Catapult Medium 16 m")) };
        int Add(string part, int on, string socket, string by, PyDict? config = null)
        {
            var d = Kw(("part", part), ("on", on), ("socket", socket), ("by", by));
            if (config is not null)
                d["config"] = config;
            s.Add(d);
            return s.Count - 1;
        }

        Add("Arrestor Gear Medium 16 m", 0, "+z", "-z");
        const string pad = "Lift Platform Medium 5x5";

        // Eight platforms from z -15 to 20: the keyed one sits at z 0. Their z -> index.
        Dictionary<int, int> Row(int firstOn, string socket, string by)
        {
            var at = new Dictionary<int, int> { [0] = Add(pad, firstOn, socket, by) };
            foreach (var z in new[] { -5, -10, -15 })
                at[z] = Add(pad, at[z + 5], "-z", "+z");
            foreach (var z in new[] { 5, 10, 15, 20 })
                at[z] = Add(pad, at[z - 5], "+z", "-z");
            return at;
        }

        var left = Row(0, "-x", "+x");
        var right = Row(0, "+x", "-x");
        var outer = new Dictionary<int, int>();
        foreach (var z in new[] { 5, 10, 15, 20 })
            outer[z] = Add(pad, right[z], "+x", "-x"); // (the hangar's floor)
        // Everything that stands on the deck stands at its edges: a plane's wings pass over the rest. Four jets up, four across at the ends for the turn.
        int[] up = [Add("Medium Turbojet", left[-15], "-y", "front"), Add("Medium Turbojet", left[20], "-y", "front"), Add("Medium Turbojet", right[-15], "-y", "front"), Add("Medium Turbojet", outer[20], "-y", "front")];
        int[] across = [Add("Medium Turbojet", left[-15], "-x", "front"), Add("Medium Turbojet", left[20], "-x", "front"), Add("Medium Turbojet", right[-10], "+x", "front"), Add("Medium Turbojet", outer[15], "+x", "front")];
        int[] tanks = [Add("Medium Fuel Bunker", left[-10], "-x", "+x"), Add("Medium Fuel Bunker", left[15], "-x", "+x"), Add("Medium Fuel Bunker", right[-15], "+x", "-x"), Add("Medium Fuel Bunker", outer[20], "+x", "-x")];
        Add("Medium Flight Stabilizer", left[-5], "+y.0+", "-y", Kw(("keys", "Rotorcraft"), ("maxTilt", 6)));
        var small = Add("Adapter Medium to Small", left[-5], "+y.0-", "back");
        Add("Bridge Helm", small, "front0.0", "-y");
        Add("Docking Clamp Medium", left[5], "+y.0-", "-y");
        Add("Mooring Mast Medium", outer[20], "+y.1+", "-y");
        Add("Hangar Bay Medium", outer[5], "+y.0-", "-y", Kw(("sizeX", 8), ("sizeY", 5), ("sizeZ", 15), ("atX", 0), ("atY", 2.5), ("atZ", 9.5)));
        var links = new List<object?>();
        foreach (var js in new[] { up, across })
            for (var k = 0; k < js.Length; k++)
                links.Add(Kw(("type", "pipe"), ("from", tanks[k]), ("to", js[k]), ("fromPort", "Fuel out"), ("toPort", "Fuel in")));
        return Kw(("format", Format), ("name", "Carrier Deck"), ("description", "A flying deck. Land over the stern, the arrestor stops you, roll onto the catapult, G hooks, G flings. F arms the clamp; park in the bay."), ("steps", s), ("links", links));
    }

    private static PyDict DeckPlane(PartOutput o)
    {
        var b = (PyDict)o.ReadJson("Constructs/Blueprints/LightPlane.json")!;
        b["name"] = "Deck Plane";
        b["description"] = "A Light Plane with an ejection seat, for the Carrier Deck. Shift and Ctrl set the throttle, S lifts the nose, G hooks and launches, L ejects.";
        foreach (var step in ((List<object?>)b["steps"]!).Cast<PyDict>())
            if ((string?)step["part"] == "Seat")
                step["part"] = "Ejection Seat";
        return b;
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, Parts());
        o.File("Constructs/Blueprints/CarrierDeck.json", PyJson.Dumps(Deck(), 1) + "\n");
        o.File("Constructs/Blueprints/DeckPlane.json", PyJson.Dumps(DeckPlane(o), 1) + "\n");
    }
}
