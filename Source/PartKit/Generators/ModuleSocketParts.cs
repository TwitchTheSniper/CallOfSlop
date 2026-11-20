using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The module socket standard's parts (docs/sandbox/design/module-sockets.md 2 and 14.1): Constructs/Parts/Modules.
///
/// A Module Socket W x L is a Medium-class plate of a whole-metre footprint (W along x, L along z, its origin the middle of its
/// first metre cell) whose back carries Medium sockets at each metre cell and Small ones on a 0.5 m grid, so it bolts onto
/// Small and Medium hulls; its face carries one "module" socket in the footprint's middle, which takes only a Module Plug of
/// the same footprint. The bolts round its rim (every 0.5 m) and the hazard rim are the look that says "standard socket".
/// A Module Plug W x L is the module's base: a plate 5 mm inside the footprint (no coplanar sides) with its "module" socket under
/// it and a grid of sockets on top to weld anything onto, and a lifting lug at each corner. Its underside sits 15 mm over the plate's
/// drawn face (z-fighting rule). A Module Stand W x L is a plate on a frame, 0.9 m off the floor: a module on it is stored or
/// built (a bench), and nothing couples.
/// </summary>
public sealed class ModuleSocketParts : IPartGenerator
{
    public string Name => "module_sockets";

    private const string Out = "Constructs/Parts/Modules";
    private const string Group = "Ship Modules";
    private const string PlateC = "#3d444c", RimC = "#d8a320", BoltC = "#8c939b", PlugC = "#c9a21c", PlugTopC = "#5a6168", LugC = "#2b2f34", LampC = "#39465a", FrameC = "#4a525b";

    private static readonly (int W, int L)[] Sizes = [(1, 1), (1, 2), (1, 3), (2, 2), (2, 3), (3, 3)];

    private const double Back = -0.5; // a plate's back (the bottom of its first cell)
    private const double PlateTop = -0.38; // the plate's face, where its "module" socket is
    private const double Drawn = 0.003; // the drawn face stands this far under the socket (the plug's 3 mm over it)
    private const double PlugT = 0.1; // the plug's thickness
    private const double Inset = 0.005; // the plug stands this far inside the footprint

    private static string Size(int w, int l) => $"{w}x{l}";

    /// <summary>The back's sockets: Medium at each metre cell, Small on the 0.5 m grid inside each.</summary>
    private static void BackSockets(Part p, int w, int l, double y, string dir)
    {
        for (var i = 0; i < w; i++)
            for (var j = 0; j < l; j++)
            {
                p.Sock($"back.{i}.{j}", [i, y, j], dir);
                for (var a = 0; a < 2; a++)
                    for (var b = 0; b < 2; b++)
                        p.Sock($"b{i}{a}.{j}{b}", [i - 0.25 + a * 0.5, y, j - 0.25 + b * 0.5], dir, Small);
            }
    }

    /// <summary>The bolts round a plate's rim, every 0.5 m, standing on its drawn face (detail).</summary>
    private static void Bolts(Part p, int w, int l, double top)
    {
        double x0 = -0.5 + 0.07, x1 = w - 0.5 - 0.07, z0 = -0.5 + 0.07, z1 = l - 0.5 - 0.07;
        void Bolt(double x, double z) => p.Add(Box([x - 0.022, top - 0.01, z - 0.022], [x + 0.022, top + 0.006, z + 0.022], BoltC, detail: true)); // (6 mm over the face: under the plug, which stands 15 mm over it)
        for (var k = 0; k <= w * 2; k++)
        {
            var x = x0 + (x1 - x0) * k / (w * 2);
            Bolt(x, z0);
            Bolt(x, z1);
        }
        for (var k = 1; k < l * 2; k++)
        {
            var z = z0 + (z1 - z0) * k / (l * 2);
            Bolt(x0, z);
            Bolt(x1, z);
        }
    }

    /// <summary>The plate itself: its slab from <paramref name="y0"/> up to the drawn face, the hazard rim, the bolts, the pad under its socket and the lamp.</summary>
    private static void Plate(Part p, int w, int l, double y0, double top)
    {
        var face = top - Drawn;
        p.Add(Box([-0.5, y0, -0.5], [w - 0.5, face - 0.016, l - 0.5], PlateC));
        // The face: a hazard rim round a plain middle, one slab high.
        p.Add(Box([-0.5 + 0.12, face - 0.016, -0.5 + 0.12], [w - 0.5 - 0.12, face, l - 0.5 - 0.12], PlateC));
        p.Add(Box([-0.5 + 0.004, face - 0.016, -0.5 + 0.004], [w - 0.5 - 0.004, face, -0.5 + 0.12], RimC));
        p.Add(Box([-0.5 + 0.004, face - 0.016, l - 0.5 - 0.12], [w - 0.5 - 0.004, face, l - 0.5 - 0.004], RimC));
        p.Add(Box([-0.5 + 0.004, face - 0.016, -0.5 + 0.12], [-0.5 + 0.12, face, l - 0.5 - 0.12], RimC));
        p.Add(Box([w - 0.5 - 0.12, face - 0.016, -0.5 + 0.12], [w - 0.5 - 0.004, face, l - 0.5 - 0.12], RimC));
        Bolts(p, w, l, face);
        // The keying notch: a dark corner block (a keyed socket takes its module with the notched corners together).
        p.Add(Box([-0.5 + 0.13, face - 0.01, -0.5 + 0.13], [-0.5 + 0.25, face + 0.006, -0.5 + 0.25], LugC, detail: true));
        double cx = (w - 1) * 0.5, cz = (l - 1) * 0.5;
        p.Add(Box([cx - 0.1, top - 0.012, cz - 0.1], [cx + 0.1, top, cz + 0.1], hidden: true)); // (the socket's solid face; never drawn)
        p.Sock("module", [cx, top, cz], "+y", kind: "module");
    }

    private static Part Socket(int w, int l)
    {
        var p = new Part($"Module Socket {Size(w, l)}", Medium, "Module sockets",
            $"A standard {Size(w, l)} m socket: a Module Plug of its size fits it. F with a module near fits it; F again takes it off.",
            role: "frame", color: PlateC, group: Group);
        Plate(p, w, l, Back, PlateTop);
        p.Add(Box([w - 0.5 - 0.2, PlateTop - Drawn - 0.01, -0.5 + 0.14], [w - 0.5 - 0.14, PlateTop + 0.003, -0.5 + 0.2], LampC, detail: true)); // the lamp: the last box
        BackSockets(p, w, l, Back, "-y");
        var area = w * l;
        p.Stats(area * 85, 120 + area * Medium.HealthM2, Medium.Armour, seconds: 3.0 + area);
        p.Set(("onBreak", "detach"), ("moduleSocket", Kw(("w", w), ("l", l))));
        return p;
    }

    private static Part Plug(int w, int l)
    {
        var p = new Part($"Module Plug {Size(w, l)}", Medium, "Module sockets",
            $"The base of a {Size(w, l)} m module: weld anything onto it, then fit it to a Module Socket of its size. Its lugs are where arms grip it.",
            role: "frame", color: PlugC, group: Group);
        double y0 = Back + 0.012, y1 = Back + PlugT; // (its underside 12 mm over its socket: 15 mm over the plate's drawn face, clear of its bolts)
        double x0 = -0.5 + Inset, x1 = w - 0.5 - Inset, z0 = -0.5 + Inset, z1 = l - 0.5 - Inset;
        p.Add(Box([x0, y0, z0], [x1, y1 - 0.02, z1], PlugC));
        p.Add(Box([x0 + 0.03, y1 - 0.02, z0 + 0.03], [x1 - 0.03, y1, z1 - 0.03], PlugTopC));
        double cx = (w - 1) * 0.5, cz = (l - 1) * 0.5;
        p.Add(Box([cx - 0.1, Back, cz - 0.1], [cx + 0.1, Back + 0.012, cz + 0.1], hidden: true)); // (the socket's solid face)
        // The lifting lugs: a block at each corner with an eye over it (inside the footprint, so never out of the room).
        foreach (var (lx, lz) in new[] { (x0 + 0.02, z0 + 0.02), (x1 - 0.11, z0 + 0.02), (x0 + 0.02, z1 - 0.11), (x1 - 0.11, z1 - 0.11) })
        {
            p.Add(Box([lx, y1, lz], [lx + 0.09, y1 + 0.06, lz + 0.09], LugC));
            p.Add(Box([lx + 0.025, y1 + 0.06, lz + 0.03], [lx + 0.065, y1 + 0.1, lz + 0.06], BoltC, detail: true));
        }
        // The keying notch, matching the plate's corner.
        p.Add(Box([-0.5 + 0.13, y1 - 0.02, -0.5 + 0.13], [-0.5 + 0.25, y1 + 0.004, -0.5 + 0.25], LugC, detail: true));
        p.Sock("module", [cx, Back, cz], "-y", kind: "module");
        for (var i = 0; i < w; i++)
            for (var j = 0; j < l; j++)
            {
                p.Sock($"top.{i}.{j}", [i, y1, j], "+y");
                for (var a = 0; a < 2; a++)
                    for (var b = 0; b < 2; b++)
                        p.Sock($"t{i}{a}.{j}{b}", [i - 0.25 + a * 0.5, y1, j - 0.25 + b * 0.5], "+y", Small);
            }
        var area = w * l;
        p.Stats(area * 60, 100 + area * Medium.HealthM2, Medium.Armour, seconds: 3.0 + area);
        p.Set(("onBreak", "detach"), ("modulePlug", Kw(("w", w), ("l", l))));
        return p;
    }

    private static Part Stand(int w, int l)
    {
        var p = new Part($"Module Stand {Size(w, l)}", Medium, "Module stands",
            $"A {Size(w, l)} m plate on a frame, 0.9 m up: a module stands on it, stored (nothing couples), or is built on it as on a bench.",
            role: "frame", color: FrameC, group: Group);
        const double top = 0.42, slab = 0.3;
        Plate(p, w, l, slab, top);
        // The frame: a leg at each corner and a foot rail round the floor.
        foreach (var (lx, lz) in new[] { (-0.5, -0.5), (w - 0.5 - 0.1, -0.5), (-0.5, l - 0.5 - 0.1), (w - 0.5 - 0.1, l - 0.5 - 0.1) })
            p.Add(Box([lx + 0.01, Back, lz + 0.01], [lx + 0.09, slab, lz + 0.09], FrameC));
        p.Add(Box([-0.5 + 0.1, Back, -0.5 + 0.02], [w - 0.5 - 0.1, Back + 0.08, -0.5 + 0.08], FrameC));
        p.Add(Box([-0.5 + 0.1, Back, l - 0.5 - 0.08], [w - 0.5 - 0.1, Back + 0.08, l - 0.5 - 0.02], FrameC));
        BackSockets(p, w, l, Back, "-y"); // (its feet: on a Medium or a Small floor)
        var area = w * l;
        p.Stats(area * 110, 120 + area * Medium.HealthM2, Medium.Armour, seconds: 3.0 + area);
        p.Set(("moduleSocket", Kw(("w", w), ("l", l), ("stand", true))));
        return p;
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var (w, l) in Sizes)
        {
            parts.Add(("", Socket(w, l)));
            parts.Add(("", Plug(w, l)));
            parts.Add(("", Stand(w, l)));
        }
        WriteParts(o, Out, parts);
    }
}
