using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Linked logistics (docs/sandbox/design/linked-logistics.md): Constructs/Parts/LinkedLogistics. The link ends (Logistics Transmitter,
/// Logistics Receiver: one planetoid; the Logistics Relay: the whole star system), the Cargo Dock (a factory's staging hold that a
/// docked ship's Cargo Port trades with), the Cargo Port (a ship's hatch, which keeps its manifest), the Route Computer (a ship's
/// standing route) and the Logistics Dispatcher (a factory's fleet). Each names its kind in a <c>"logiLink"</c> block
/// (ConstructLinks.Parse). The Cargo Dock is a Piece Rack whose hold is its staging hold, with the Crafting Terminal's tray flag: the
/// linked network's remote orders are its own orders (ConstructLinkPlan.cs), so its pieces come by Routers as a terminal's do.
/// The look: the machine look's tones (look.md); masts and dishes are thin over a heavy base so they read from a distance.
/// </summary>
public sealed class LinkedLogisticsParts : IPartGenerator
{
    public string Name => "logistics_link_parts";

    private const string Out = "Constructs/Parts/LinkedLogistics";
    private const string Section = "Linked logistics";
    private const string Body = "#3a6f8f", Screen = "#10161c", Ring = "#d8dde2";

    private static PyDict Link(string kind) => Kw(("kind", kind));

    private static Part Mast(string name, string kind, string desc, bool ring)
    {
        var c = Small;
        var p = new Part(name, c, Section, desc, color: Body, group: "Production");
        // A 1 m base, a 1.6 m mast over it and a whip (or a ring) at its top: 1 x 2.1 x 1 m.
        p.Add(Box([-0.5, -0.125, -0.5], [0.5, 0.075, 0.5], MachineLook.Base));
        p.Add(Box([-0.3, 0.07, -0.3], [0.3, 0.3, 0.3]));
        p.Add(MachineLook.D([-0.22, 0.297, 0.31], [0.22, 0.28, 0.322], Screen)); // its code plate
        p.Add(Box([-0.06, 0.295, -0.06], [0.06, 1.7, 0.06], MachineLook.Steel));
        if (ring)
        {
            p.Add(Box([-0.3, 1.7, -0.3], [0.3, 1.76, -0.24], Ring, detail: true));
            p.Add(Box([-0.3, 1.7, 0.24], [0.3, 1.76, 0.3], Ring, detail: true));
            p.Add(Box([-0.3, 1.704, -0.237], [-0.24, 1.756, 0.237], Ring, detail: true));
            p.Add(Box([0.24, 1.704, -0.237], [0.3, 1.756, 0.237], Ring, detail: true));
        }
        else
        {
            p.Add(Box([-0.02, 1.695, -0.02], [0.02, 2.3, 0.02], Ring, detail: true)); // the whip
            p.Add(Box([-0.05, 2.295, -0.05], [0.05, 2.36, 0.05], MachineLook.Out, detail: true)); // its tip
        }
        p.Add(Box([-0.1, 1.2, -0.1], [0.1, 1.28, 0.1], MachineLook.Accent, detail: true));
        p.Sock("-y", [0, -0.125, 0], "-y");
        p.Stats(120, 400, 2, seconds: 4.0);
        p.Set(("power", 200), ("onBreak", "disable"), ("logiLink", Link(kind)));
        return p;
    }

    private static Part Relay()
    {
        var c = Small; // (a small-class part, big: it stands on a Foundation's own sockets)
        var p = new Part("Logistics Relay", c, Section, "A dish that links this construct's logistics network with another across the star system (a Relay at each end). Shows a code to pair with; takes one. Needs 2 kW.",
            color: Body, group: "Production");
        p.Add(Box([-1.5, -0.5, -1.5], [1.5, 0.0, 1.5], MachineLook.Base));
        p.Add(Box([-0.6, -0.005, -0.6], [0.6, 0.8, 0.6]));
        p.Add(MachineLook.D([-0.4, 0.3, 0.597], [0.4, 0.6, 0.612], Screen));
        p.Add(Box([-0.2, 0.795, -0.2], [0.2, 3.2, 0.2], MachineLook.Steel));
        p.Add(Box([-1.5, 3.195, -1.5], [1.5, 3.4, 1.5], Ring)); // the dish
        p.Add(Box([-1.2, 3.395, -1.2], [1.2, 3.5, 1.2], Ring));
        p.Add(Box([-0.08, 3.495, -0.08], [0.08, 4.4, 0.08], MachineLook.Steel, detail: true)); // the feed's boom
        p.Add(Box([-0.2, 4.395, -0.2], [0.2, 4.5, 0.2], MachineLook.Out, detail: true)); // the feed
        p.Sock("-y", [0, -0.5, 0], "-y");
        p.Stats(2000, 2500, 5, seconds: 10.0);
        p.Set(("power", 2000), ("onBreak", "disable"), ("logiLink", Link("relay")));
        return p;
    }

    private static Part Dock()
    {
        var c = Small;
        var p = new Part("Cargo Dock", c, Section, "A factory's staging hold with a hatch. A ship held by a Docking Clamp on this construct with its Cargo Port over the hatch is loaded with what linked networks ordered here, and unloads what it brings. Needs power.",
            role: "storage", color: Body, group: "Production");
        // A low bay 2.5 m wide, 1 m high and 6.5 m long; its hold is the bay; the hatch is a white ring at its middle.
        const double x0 = -1.25, x1 = 1.25, y0 = -0.125, top = 0.875, z0 = -3.25, z1 = 3.25;
        p.Add(Box([x0, y0, z0], [x1, y0 + 0.12, z1], MachineLook.Base));
        p.Add(Box([x0, y0 + 0.115, z0], [x0 + 0.12, top, z1]));
        p.Add(Box([x1 - 0.12, y0 + 0.115, z0], [x1, top, z1]));
        p.Add(Box([x0 + 0.115, y0 + 0.115, z0], [x1 - 0.115, top, z0 + 0.12]));
        p.Add(MachineLook.D([x0 + 0.02, top - 0.01, -1.0], [x0 + 0.13, top + 0.012, 1.0], MachineLook.Accent));
        p.Add(MachineLook.D([x1 - 0.13, top - 0.01, -1.0], [x1 - 0.02, top + 0.012, 1.0], MachineLook.Accent));
        p.Add(MachineLook.D([-0.6, y0 + 0.118, -0.6], [0.6, y0 + 0.132, 0.6], MachineLook.In)); // the hatch's ring
        MachineLook.Hazard(p, [x0 + 0.13, y0 + 0.118, z1 - 0.3], [x1 - 0.13, y0 + 0.132, z1 - 0.02]);
        p.Add(MachineLook.D([-0.4, top - 0.4, z0 - 0.008], [0.4, top - 0.1, z0 + 0.004], MachineLook.Out)); // the way out
        p.Sock("-y", [0, y0, 0], "-y");
        p.Sock("-y.f", [0, y0, 2.5], "-y");
        p.Sock("-y.b", [0, y0, -2.5], "-y");
        p.Stats(600, 1500, 4, seconds: 6.0);
        p.Set(("power", 1000), ("onBreak", "disable"), ("logiLink", Link("dock")));
        const double section = 2.0, length = 6.0;
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, section)), ("length", length), ("count", 600), ("crafting", true),
            ("hold", Py.L(Py.L(x0 + 0.13, y0 + 0.135, z0 + 0.13), Py.L(x1 - 0.13, top - 0.02, z1 - 0.02))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, 0.375, z1)), ("face", "+z"), ("opening", Py.L(1.0, 1.0))),
            Kw(("name", "Piece out"), ("type", "conveyor"), ("dir", "out"), ("at", Py.L(0, 0.375, z0)), ("face", "-z"), ("opening", Py.L(1.0, 1.0))),
            Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(x0 + 0.3, top, z0 + 0.6)), ("face", "+y")),
            Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Py.L(x1 - 0.3, top, z0 + 0.6)), ("face", "+y")))));
        return p;
    }

    private static Part Port()
    {
        var c = Small;
        var p = new Part("Cargo Port", c, Section, "A ship's cargo hatch: held by a Docking Clamp near a Cargo Dock, the dock loads the ship's Storage Volumes and Piece Racks and unloads them. Keeps the ship's manifest.",
            color: Body, group: "Production");
        p.Add(Box([-0.5, -0.125, -0.5], [0.5, 0.0, 0.5], MachineLook.Base));
        p.Add(Box([-0.5, -0.005, -0.5], [0.5, 0.15, -0.38]));
        p.Add(Box([-0.5, -0.005, 0.38], [0.5, 0.15, 0.5]));
        p.Add(Box([-0.5, -0.005, -0.375], [-0.38, 0.15, 0.375]));
        p.Add(Box([0.38, -0.005, -0.375], [0.5, 0.15, 0.375]));
        p.Add(MachineLook.D([-0.37, -0.004, -0.37], [0.37, 0.012, 0.37], MachineLook.In));
        p.Add(MachineLook.D([-0.48, 0.145, -0.48], [-0.4, 0.165, 0.48], MachineLook.Accent));
        p.Sock("-y", [0, -0.125, 0], "-y");
        p.Sock("+y", [0, 0.15, 0], "+y");
        p.Stats(20, 300, 3, seconds: 3.0);
        p.Set(("power", 100), ("onBreak", "disable"), ("logiLink", Link("port")));
        return p;
    }

    private static Part Console(string name, string kind, string desc, double w, double d, double mass, int watts, string[] keys)
    {
        var c = Small;
        var p = new Part(name, c, Section, desc, color: Body, group: "Production");
        var hw = w / 2;
        var hd = d / 2;
        p.Add(Box([-hw, -0.125, -hd], [hw, 0.0, hd], MachineLook.Base));
        p.Add(Box([-hw + 0.02, -0.005, -hd + 0.02], [hw - 0.02, 0.6, hd - 0.02]));
        p.Add(Box([-hw + 0.04, 0.595, -hd + 0.04], [hw - 0.04, 0.9, -hd + 0.18], MachineLook.Base)); // the screen's housing
        p.Add(MachineLook.D([-hw + 0.07, 0.62, -hd + 0.176], [hw - 0.07, 0.88, -hd + 0.19], Screen));
        for (var k = 0; k < keys.Length; k++)
            p.Add(MachineLook.D([-hw + 0.08 + k * 0.14, 0.598, hd - 0.2], [-hw + 0.18 + k * 0.14, 0.615, hd - 0.08], k == keys.Length - 1 ? MachineLook.Stop : MachineLook.Accent));
        p.Sock("-y", [0, -0.125, 0], "-y");
        p.Stats(mass, 300, 2, seconds: 3.0);
        p.Set(("power", watts), ("onBreak", "disable"), ("logiLink", Link(kind)));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Mast("Logistics Transmitter", "transmitter", "Links this construct's logistics network with one Receiver or more on the same planetoid: they plan as one; goods go by cargo ship. Its panel shows the code a Receiver takes. Needs power.", false)),
        ("", Mast("Logistics Receiver", "receiver", "Links this construct's logistics network with a Transmitter on the same planetoid: type its code in this one's panel. Orders and stock go both ways; goods go by cargo ship. Needs power.", true)),
        ("", Relay()),
        ("", Dock()),
        ("", Port()),
        ("", Console("Route Computer", "route", "Flies its ship on a standing route between Cargo Docks (set in the command view), with the leaving rules you set at each stop. Needs a working Flight Stabilizer.", 0.6, 0.6, 15, 150, ["a", "b"])),
        ("", Console("Logistics Dispatcher", "dispatcher", "Sends this factory's registered route ships when enough cargo waits for a linked network. Needs a link end on this construct.", 1.2, 0.8, 300, 300, ["a", "b", "c"])),
    ]);
}
