using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The ammo parts: Constructs/Parts/Ammo (docs/sandbox/CONSTRUCTS.md, "Ammo feeds"). The Ammo Container: a Storage Volume's box that
/// holds bulk "ammo", with an ammo type and a feed out; it stores what a Storage Volume stores and follows its rules, sealing
/// included. The Ammo Switches: 2, 4 and 8 feeds in, one out, a data input picking which. The reels and the Ammo Socket carry a
/// belt to what moves. Was tools/constructs/ammo_parts.py.
/// </summary>
public sealed class AmmoParts : IPartGenerator
{
    public string Name => "ammo_parts";

    private const string Out = "Constructs/Parts/Ammo";
    private const string Section = "Ammo boxes";
    private const string Body = "#4d5a3a", Band = "#c9a227", Brass = "#b08d3c", Steel = "#7a7f88";

    private static PyDict AmmoType() => new()
    {
        ["key"] = "ammoType", ["label"] = "Ammo type", ["type"] = "choice", ["group"] = "Contents",
        ["tooltip"] = "What this holds: any gun takes any type, and the type is what a gun fed from here fires.",
    };

    /// <summary>The Storage Volume's box settings and its creative contents, word for word, then the ammo type.</summary>
    private static List<object?> BoxConfig(PartOutput o)
    {
        var volume = (PyDict)o.ReadJson("Constructs/Parts/Storage/StorageVolume.json")!;
        return [.. (List<object?>)volume["config"]!, AmmoType()];
    }

    private static (string, Part) Container(PartOutput o)
    {
        var p = new Part("Ammo Container", Small, Section, "A box you size, for rounds. Inside your walls and armour: a bunker. Feeds run from here to the guns. It blows up by what is in it.", role: "storage", tool: "wrench", color: Body, group: "Weapons");
        var h = 0.2;
        p.Add(Box([-h, -h, -h], [h, h, h], Body));
        p.Add(Box([-h - 0.01, -h - 0.01, -0.03], [h + 0.01, h + 0.01, 0.03], Band, detail: true));
        p.Add(Box([-0.06, h, -0.06], [0.06, h + 0.04, 0.06], Brass, detail: true));
        p.Add(Box([-0.07, -0.07, h], [0.07, 0.07, h + 0.03], Steel, detail: true));
        foreach (var (d, at) in new (string, double[])[] { ("+x", [h, 0, 0]), ("-x", [-h, 0, 0]), ("+y", [0, h, 0]), ("-y", [0, -h, 0]), ("+z", [0, 0, h]), ("-z", [0, 0, -h]) })
            p.Sock(d, at, d);
        p.Stats(20, 500, 8, 2.0);
        p.Set(("storage", new PyDict { ["ammoStore"] = true, ["bulk"] = new PyDict { ["material"] = "any", ["volume"] = true, ["chuteLoad"] = 160 } }));
        p.Set(("ports", Py.L(
            new PyDict { ["name"] = "Ammo out", ["type"] = "feed", ["dir"] = "out", ["at"] = Py.L(0, h, 0), ["face"] = "+y" },
            new PyDict { ["name"] = "Chute in", ["type"] = "chute", ["dir"] = "in", ["at"] = Py.L(0, 0, h), ["face"] = "+z" })));
        p.Set(("config", BoxConfig(o)));
        return ("", p);
    }

    /// <summary>n feeds in along its back, one out at the front, the data input (the part's own) on top.</summary>
    private static (string, Part) Switch(int n)
    {
        var w = 0.1 + 0.07 * n;
        var h = 0.11;
        var d = 0.16;
        var p = new Part($"Ammo Switch {n}", Small, Section, $"{n} ammo feeds in, one out. Its data input picks which goes on: 0 the first, 1 the next. It also stops a fire running along a feed.", role: "ammoswitch", tool: "wrench", color: Body, group: "Weapons");
        p.Add(Box([-w, -h, -d], [w, h, d], Body));
        p.Add(Box([-w - 0.01, h - 0.03, -0.03], [w + 0.01, h + 0.01, 0.03], Band, detail: true));
        p.Add(Box([-0.05, -0.05, -d - 0.03], [0.05, 0.05, -d], Steel, detail: true));
        var ports = Py.L(new PyDict { ["name"] = "Ammo out", ["type"] = "feed", ["dir"] = "out", ["at"] = Py.L(0, 0, -d), ["face"] = "-z" });
        for (var k = 0; k < n; k++)
        {
            var x = Py.Round(-w + (2 * k + 1) * w / n, 4);
            p.Add(Box([x - 0.035, -0.04, d], [x + 0.035, 0.04, d + 0.025], Brass, detail: true));
            ports.Add(new PyDict { ["name"] = $"Ammo in {k + 1}", ["type"] = "feed", ["dir"] = "in", ["at"] = Py.L(x, 0, d), ["face"] = "+z" });
        }
        p.Sock("bottom", [0, -h, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Stats(6 + 2 * n, 120, 6, 1.5);
        p.Set(("ports", ports));
        return ("", p);
    }

    /// <summary>A drum on a cradle: its "Ammo in" takes a feed (or a box bolts to it), its belt pays out from the front to an Ammo Socket.</summary>
    private static (string, Part) Reel(string name, double s, int length, int mass, int health)
    {
        var p = new Part(name, Small, "Hoses and leads", $"A {length} m ammo belt on a drum. Feed it, take the plug to an Ammo Socket on what moves: a turret, or another construct. It stays whole however the turret turns.", role: "hose", tool: "wrench", color: "#3c4046", group: "Hoses");
        double x = 0.3 * s, y = 0.2 * s, z = 0.2 * s;
        p.Add(Box([-x, -0.25 * s, -z], [x, -0.17 * s, z]));
        p.Add(Box([-x, -0.17 * s, -0.04 * s], [-0.24 * s, y, 0.04 * s]));
        p.Add(Box([0.24 * s, -0.17 * s, -0.04 * s], [x, y, 0.04 * s]));
        var drum = Box([-0.24 * s, -0.12 * s, -0.16 * s], [0.24 * s, y, 0.16 * s], Band)!;
        drum["use"] = true;
        p.Add(drum);
        p.Add(Box([-0.05 * s, -0.01 * s, z], [0.05 * s, 0.09 * s, 0.25 * s], "#202226", detail: true));
        p.Sock("-y", [0, -0.25 * s, 0], "-y");
        p.Sock("-z", [0, 0, -z], "-z");
        p.Sock("+x", [x, 0, 0], "+x");
        p.Sock("-x", [-x, 0, 0], "-x");
        p.Stats(mass, health, 2, 1.5);
        p.Set(("hose", new PyDict { ["end"] = "outlet", ["carries"] = "ammo", ["length"] = length, ["at"] = Py.L(0, Py.Round(0.04 * s, 4), Py.Round(0.25 * s, 4)) }));
        p.Set(("ports", Py.L(new PyDict { ["name"] = "Ammo in", ["type"] = "feed", ["dir"] = "in", ["at"] = Py.L(0, -0.25 * s, Py.Round(0.15 * s, 4)), ["face"] = "-y" })));
        return ("", p);
    }

    /// <summary>Bolted to a gun it feeds it; otherwise an "Ammo out" link goes on from it.</summary>
    private static (string, Part) Socket()
    {
        var p = new Part("Ammo Socket", Small, "Hoses and leads", "Takes an ammo reel's plug. Bolted to a gun it feeds it; elsewhere a feed goes on from it.", role: "hose", tool: "wrench", color: "#55595f", group: "Hoses");
        p.Add(Box([-0.12, -0.12, -0.05], [0.12, 0.12, 0.03]));
        var plug = Box([-0.06, -0.06, 0.03], [0.06, 0.06, 0.1], Band)!;
        plug["use"] = true;
        p.Add(plug);
        p.Sock("-z", [0, 0, -0.05], "-z");
        p.Sock("-y", [0, -0.12, 0], "-y");
        p.Sock("left", [-0.12, 0, 0], "-x");
        p.Sock("right", [0.12, 0, 0], "+x");
        p.Sock("top", [0, 0.12, 0], "+y");
        p.Sock("bottom", [0, -0.12, -0.001], "-y");
        p.Sock("back", [0, 0, 0.1], "+z");
        p.Sock("front", [0, 0, -0.051], "-z");
        p.Stats(3, 80, 2, 1.0);
        p.Set(("hose", new PyDict { ["end"] = "inlet", ["carries"] = "ammo", ["at"] = Py.L(0, 0, 0.12) }));
        p.Set(("ports", Py.L(new PyDict { ["name"] = "Ammo out", ["type"] = "feed", ["dir"] = "out", ["at"] = Py.L(0.12, 0, 0), ["face"] = "+x" })));
        return ("", p);
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        Container(o),
        .. new[] { 2, 4, 8 }.Select(Switch),
        Reel("Ammo Reel", 1, 8, 18, 120),
        Reel("Large Ammo Reel", 1.4, 16, 32, 180),
        Socket(),
    ]);
}
