namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The build stations' part files: Constructs/Parts/Stations (docs/sandbox/CONSTRUCTS.md, "Build stations"). Was tools/constructs/station_parts.py.
///
/// A station is an Inventory Beacon with an aid (storage.beacon names it): it is welded on, wants power and a chute to a store, and the
/// last box of each model is the lamp that lights while it works. Numbers (mass, health, power) are here; ranges are in
/// Constructs/Beacons.json; recipes in tools/constructs/assembling.py. Boxes go through zfight (CLAUDE.md, "No coplanar faces").
/// It writes its own files, not through PartKitMath.WriteParts: corners are kept as given (an int stays an int), and the folder isn't owned.
/// </summary>
public sealed class StationParts : IPartGenerator
{
    public string Name => "station_parts";

    private const string Folder = "Constructs/Parts/Stations";
    private const string Steel = "#46525e", Dark = "#2a3036", Trim = "#9aa4ae", Screen = "#14323a", Lamp = "#39465a";

    private static PyDict B(List<object?> mn, List<object?> mx, string? color = null, bool detail = false)
    {
        var d = new PyDict { ["min"] = mn, ["max"] = mx };
        if (!string.IsNullOrEmpty(color))
            d["color"] = color;
        if (detail)
            d["detail"] = true;
        return d;
    }

    private static List<object?> L(params object?[] v) => Py.L(v);

    /// <summary>A cabinet on a foot plate: every station stands on one, half-widths w and d, h high.</summary>
    private static List<PyDict> Bench(double w, double d, double h) =>
        [B(L(-w - 0.04, 0, -d - 0.04), L(w + 0.04, 0.07, d + 0.04), Dark), B(L(-w, 0.06, -d), L(w, h, d))];

    private static PyDict Sock(string name, List<object?> at, string dir) => new() { ["name"] = name, ["at"] = at, ["dir"] = dir };

    private static List<PyDict> Sockets(double w, double d, double y = 0.4) =>
        [Sock("bottom", L(0, 0, 0), "-y"), Sock("+x", L(w, y, 0), "+x"), Sock("-x", L(-w, y, 0), "-x"), Sock("back", L(0, y, -d), "-z")];

    private static (List<PyDict>, List<PyDict>) MirrorTable()
    {
        double w = 0.6, d = 0.4;
        var m = Bench(w, d, 0.72);
        m.Add(B(L(-w - 0.03, 0.72, -d - 0.03), L(w + 0.03, 0.78, d + 0.03), Trim));
        // The mirror: a bright pane across the table, a frame round it, and a part on each side, the same.
        m.AddRange([B(L(-0.02, 0.78, -0.36), L(0.02, 1.5, 0.36), "#5d6a76"), B(L(-0.03, 0.82, -0.32), L(0.03, 1.46, 0.32), "#cfe6f2", true)]);
        m.AddRange([B(L(0.2, 0.78, -0.12), L(0.44, 0.94, 0.12), "#c7923a", true), B(L(-0.44, 0.78, -0.12), L(-0.2, 0.94, 0.12), "#c7923a", true)]);
        m.Add(B(L(-0.2, 0.3, d - 0.006), L(0.2, 0.56, d + 0.006), Screen, true));
        return (m, Sockets(w, d));
    }

    private static (List<PyDict>, List<PyDict>) LineLayer()
    {
        double w = 0.42, d = 0.75;
        var m = Bench(w, d, 0.62);
        // A bed of rails, a row of beams on it and the carriage that lays them.
        m.AddRange([B(L(-0.3, 0.62, -0.72), L(-0.24, 0.72, 0.72), Trim), B(L(0.24, 0.62, -0.72), L(0.3, 0.72, 0.72), Trim)]);
        for (var k = 0; k < 4; k++)
        {
            var z = -0.6 + k * 0.28;
            m.Add(B(L(-0.2, 0.62, z), L(0.2, 0.74, z + 0.2), "#b0763a", true));
        }
        m.AddRange([B(L(-0.36, 0.72, 0.42), L(0.36, 1.2, 0.56), "#c9a21c"), B(L(-0.1, 0.84, 0.3), L(0.1, 1.02, 0.42), Dark, true)]);
        m.Add(B(L(-0.15, 0.2, d - 0.006), L(0.15, 0.46, d + 0.006), Screen, true));
        return (m, Sockets(w, d));
    }

    private static (List<PyDict>, List<PyDict>) Replacer()
    {
        double w = 0.5, d = 0.5;
        var m = Bench(w, d, 0.7);
        // A round bed and an arm with a claw above it: one part out, the next in.
        m.AddRange([B(L(-0.36, 0.7, -0.36), L(0.36, 0.78, 0.36), Trim), B(L(-0.12, 0.78, -0.12), L(0.12, 0.98, 0.12), "#4e8f6a", true)]);
        m.AddRange([B(L(-0.46, 0.7, -0.46), L(-0.32, 1.7, -0.32), Steel), B(L(-0.46, 1.62, -0.46), L(0.1, 1.74, -0.32), Steel)]);
        m.AddRange([B(L(-0.04, 1.3, -0.46), L(0.06, 1.62, -0.32), Dark), B(L(-0.16, 1.2, -0.42), L(0.16, 1.3, -0.36), "#c9a21c", true)]);
        m.Add(B(L(-0.18, 0.28, d - 0.006), L(0.18, 0.54, d + 0.006), Screen, true));
        return (m, Sockets(w, d));
    }

    private static (List<PyDict>, List<PyDict>) Gantry()
    {
        double w = 0.9, d = 0.5;
        List<PyDict> m = [B(L(-w - 0.04, 0, -d - 0.04), L(w + 0.04, 0.07, d + 0.04), Dark), B(L(-w, 0.06, -d), L(-w + 0.2, 2.9, -d + 0.2)), B(L(w - 0.2, 0.06, -d), L(w, 2.9, -d + 0.2))];
        m.AddRange([B(L(-w, 0.06, d - 0.2), L(-w + 0.2, 2.9, d)), B(L(w - 0.2, 0.06, d - 0.2), L(w, 2.9, d))]);
        // The portal's top beams, a trolley and a camera on it, and a low desk between the legs.
        m.AddRange([B(L(-w - 0.03, 2.86, -d - 0.03), L(w + 0.03, 3.02, -d + 0.21), "#c9a21c"), B(L(-w - 0.03, 2.87, d - 0.21), L(w + 0.03, 3.03, d + 0.03), "#c9a21c")]);
        m.AddRange([B(L(-0.3, 3.02, -0.5), L(0.3, 3.12, 0.5), Steel), B(L(-0.1, 2.72, -0.1), L(0.1, 3.02, 0.1), Dark, true)]);
        m.AddRange([B(L(-0.34, 0.06, -0.26), L(0.34, 0.82, 0.22), Steel), B(L(-0.26, 0.5, 0.216), L(0.26, 0.78, 0.228), Screen, true)]);
        return (m, [Sock("bottom", L(0, 0, 0), "-y"), Sock("+x", L(w, 0.4, -d + 0.1), "+x"), Sock("-x", L(-w, 0.4, -d + 0.1), "-x"), Sock("back", L(0, 0.4, -0.26), "-z")]);
    }

    private static (List<PyDict>, List<PyDict>) Selector()
    {
        double w = 0.45, d = 0.45;
        var m = Bench(w, d, 0.82);
        // A console under a wire cube: twelve thin edges, the shape of a box select.
        double c = 0.3, h = 0.3;
        double y0 = 1.0, y1 = 1.6;
        (double[], double[])[] edges =
        [
            ([-c, y0, -h], [c, y0 + 0.03, -h + 0.03]), ([-c, y1, -h], [c, y1 + 0.03, -h + 0.03]), ([-c, y0, h - 0.03], [c, y0 + 0.03, h]), ([-c, y1, h - 0.03], [c, y1 + 0.03, h]),
            ([-c, y0, -h], [-c + 0.03, y0 + 0.03, h]), ([-c, y1, -h], [-c + 0.03, y1 + 0.03, h]), ([c - 0.03, y0, -h], [c, y0 + 0.03, h]), ([c - 0.03, y1, -h], [c, y1 + 0.03, h]),
            ([-c, y0, -h], [-c + 0.03, y1, -h + 0.03]), ([c - 0.03, y0, -h], [c, y1, -h + 0.03]), ([-c, y0, h - 0.03], [-c + 0.03, y1, h]), ([c - 0.03, y0, h - 0.03], [c, y1, h]),
        ];
        foreach (var (a, e) in edges)
            m.Add(B(a.Cast<object?>().ToList(), e.Cast<object?>().ToList(), "#54d0e0", true));
        m.AddRange([B(L(-0.04, 0.82, -0.04), L(0.04, 1.0, 0.04), Trim), B(L(-0.24, 0.36, d - 0.006), L(0.24, 0.66, d + 0.006), Screen, true)]);
        return (m, Sockets(w, d));
    }

    private static (List<PyDict>, List<PyDict>) BlueprintDesk()
    {
        double w = 0.7, d = 0.45;
        var m = Bench(w, d, 0.74);
        // A raised drawing board on a shelf, a blue sheet on it, and rolled plans at the side.
        m.AddRange([B(L(-0.62, 0.74, -0.42), L(0.62, 1.08, -0.3), Steel), B(L(-0.62, 0.74, -0.3), L(0.62, 0.82, 0.4), Trim)]);
        m.AddRange([B(L(-0.54, 0.82, -0.26), L(0.54, 0.84, 0.36), "#2c5fa8", true), B(L(-0.3, 0.838, -0.1), L(0.3, 0.852, 0.2), "#dbe8f6", true)]);
        m.AddRange([B(L(0.62, 0.74, -0.3), L(0.68, 1.3, -0.24), "#d8c690", true), B(L(0.62, 0.74, -0.18), L(0.68, 1.2, -0.12), "#d8c690", true)]);
        return (m, Sockets(w, d));
    }

    private static List<object?> MirrorConfig() => Py.L(
        new PyDict
        {
            ["key"] = "mirrorAxis", ["label"] = "Plane", ["group"] = "Mirror", ["options"] = Py.L("off", "side to side", "front to back", "up and down"), ["default"] = 0,
            ["tooltip"] = "Which way parts are mirrored, from the seat of the construct you build on (or its first part).",
        },
        new PyDict
        {
            ["key"] = "mirrorOffset", ["label"] = "Offset", ["group"] = "Mirror", ["unit"] = "m", ["min"] = -64, ["max"] = 64, ["step"] = 0.125, ["default"] = 0,
            ["tooltip"] = "How far the plane sits from the construct's centre.",
        });

    private sealed record Station(string Name, string File, string Aid, int Mass, int Power, string Desc, Func<(List<PyDict>, List<PyDict>)> Model, bool Config);

    private static readonly Station[] Stations =
    [
        new("Mirror Table", "MirrorTable", "mirror", 160, 150, "Mirrors what you build across a plane, in range. Needs power and a chute.", MirrorTable, true),
        new("Line Layer", "LineLayer", "line", 180, 150, "Lays a row or a plate field in one drag, in range. Needs power and a chute.", LineLayer, false),
        new("Replacer", "Replacer", "replace", 170, 150, "Swaps a part for another that fits, in range. Needs power and a chute.", Replacer, false),
        new("Build Gantry", "BuildGantry", "gantry", 420, 300, "A camera that flies round your build, in range. Needs power and a chute.", Gantry, false),
        new("Selector", "Selector", "select", 140, 120, "Select a box of parts to move, copy, delete, paint or attach. Needs power and a chute.", Selector, false),
        new("Blueprint Desk", "BlueprintDesk", "blueprint", 150, 120, "Saves a selection as a blueprint to stamp elsewhere. Needs power and a chute.", BlueprintDesk, false),
    ];

    private static PyDict PartOf(Station s)
    {
        var (boxes, socks) = s.Model();
        var h = boxes.Max(x => Py.D(((List<object?>)x["max"]!)[1]));
        boxes.Add(B(L(-0.05, h - 0.002, -0.05), L(0.05, h + 0.09, 0.05), Lamp, true)); // the lamp: the last box lights while it works
        PartZFight.Resolve(boxes, socks);
        var p = new PyDict
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = s.Name,
            ["description"] = s.Desc,
            ["group"] = "Build stations",
            ["role"] = "storage",
            ["tool"] = "welder",
            ["attachSeconds"] = 4.0,
            ["mass"] = s.Mass,
            ["health"] = 240,
            ["armour"] = 3,
            ["material"] = "steel",
            ["power"] = s.Power,
            ["onBreak"] = "disable",
            ["color"] = Steel,
            ["collider"] = "boxes",
            ["boxes"] = boxes.Cast<object?>().ToList(),
            ["sockets"] = socks.Cast<object?>().ToList(),
            ["storage"] = new PyDict { ["beacon"] = s.Aid },
        };
        if (s.Config)
            p["config"] = MirrorConfig();
        return p;
    }

    public void Make(PartOutput o)
    {
        foreach (var s in Stations)
            o.File($"{Folder}/{s.File}.json", PyJson.Dumps(PartOf(s), 2) + "\n");
    }
}
