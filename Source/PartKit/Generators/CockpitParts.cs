using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The modular cockpit's parts (docs/sandbox/constructs/cockpits.md): Constructs/Parts/Cockpit.
///
/// A Console Bay W x L is a module socket of the cell class (<c>"cells": true</c>): its footprint counts 10 cm cells (W along x, L along z,
/// its origin the middle of its first cell), so it takes the same styles (Proud, Flush, Well, Opening), depth and room settings as any
/// Module Socket, at 5 cm steps. It is a thin bezel with a dark rim that welds onto a dash, a wall or the hull, by its back's sockets or
/// anywhere by the attach tool's socket weld. A Console Plug W x L is its faceplate: a panel 2 mm inside the footprint with a Small
/// socket on each cell (every other one on the biggest), so the panel-size instruments (ControlParts.PanelInstruments, written here)
/// snap to the 10 cm grid; the socket weld puts them anywhere else on it. A console is fitted and taken off by hand (F), never by a
/// fitter. The sizes are the Roamer dash's bays (cockpits.md "The Roamer's dash") and a spread of plain ones.
/// </summary>
public sealed class CockpitParts : IPartGenerator
{
    public string Name => "cockpit_parts";

    private const string Out = "Constructs/Parts/Cockpit";
    private const string Group = "Cockpit";
    private const string BezelC = "#2b2f35", RimC = "#3f454d", FaceC = "#3a4048", FaceTopC = "#30353c";

    /// <summary>The shipped sizes, cells (W <= L): plain ones, then the Roamer dash's (glareshield 1x30, wings 3x6, side panels 5x12, centre 5x8, overhead 4x10).</summary>
    public static readonly (int W, int L)[] Sizes =
        [(1, 4), (1, 10), (2, 2), (2, 4), (3, 3), (4, 4), (4, 6), (6, 6), (6, 10), (1, 30), (3, 6), (5, 12), (5, 8), (4, 10)];

    private const double C = ModuleSocketsCell;
    private const double ModuleSocketsCell = 0.1; // (ModuleSockets.Cell: the part kit doesn't reference the game)
    // (every solid box is at least 1 cm on each side, the part kit's floor: a thinner one is dropped, a thinner detail grown to 16 mm)
    private const double Back = -0.035; // a bay's back
    private const double Top = 0.0; // its face, where its "module" socket is
    private const double Drawn = 0.002; // the drawn face stands this far under the socket
    private const double Lip = 0.0102; // the bezel's raised rim, and a faceplate's layers
    private const double PlugT = 0.0262; // a faceplate's thickness (its socket 4 mm under it)
    private const double Inset = 0.002; // a faceplate stands this far inside the footprint
    private const double Rim = 0.016; // the bezel's frame round the hole an Opening shows

    private static string Size(int w, int l) => $"{w}x{l}";

    /// <summary>The footprint's edges in the part's frame: x from -C/2 to (W - 0.5) C.</summary>
    private static (double X0, double X1, double Z0, double Z1) Edges(int w, int l) => (-C / 2, (w - 0.5) * C, -C / 2, (l - 0.5) * C);

    private static PyDict? Mid(PyDict? b)
    {
        b!["middle"] = true;
        return b;
    }

    /// <summary>A slab as a frame <see cref="Rim"/> wide round the footprint's edge and a middle (left undrawn in an Opening).</summary>
    private static void Framed(Part p, double x0, double x1, double z0, double z1, double y0, double y1, int w, int l, string colour)
    {
        var (ex0, ex1, ez0, ez1) = Edges(w, l);
        double ix0 = ex0 + Rim, ix1 = ex1 - Rim, iz0 = ez0 + Rim, iz1 = ez1 - Rim;
        p.Add(Box([x0, y0, z0], [x1, y1, iz0], colour));
        p.Add(Box([x0, y0, iz1], [x1, y1, z1], colour));
        p.Add(Box([x0, y0, iz0], [ix0, y1, iz1], colour));
        p.Add(Box([ix1, y0, iz0], [x1, y1, iz1], colour));
        p.Add(Mid(Box([ix0, y0, iz0], [ix1, y1, iz1], colour, detail: true)));
    }

    private static Part Bay(int w, int l)
    {
        var p = new Part($"Console Bay {Size(w, l)}", Small, "Console bays",
            $"A {Size(w, l)} cell console bay (10 cm cells): a Console Plug of its size fits it, by hand. Proud, Flush, Well or Opening, like any module socket.",
            role: "frame", tool: "wrench", color: BezelC, group: Group);
        var (x0, x1, z0, z1) = Edges(w, l);
        var face = Top - Drawn;
        Framed(p, x0, x1, z0, z1, Back, face - Lip, w, l, BezelC);
        // The face: a lighter raised rim round the bezel's middle, 2 mm in from its sides (no face shared with the slab's), so the middle
        // is a shallow tray the faceplate sits in.
        p.Add(Box([x0 + 0.002, face - Lip, z0 + 0.002], [x1 - 0.002, face, z0 + Rim], RimC));
        p.Add(Box([x0 + 0.002, face - Lip, z1 - Rim], [x1 - 0.002, face, z1 - 0.002], RimC));
        p.Add(Box([x0 + 0.002, face - Lip, z0 + Rim], [x0 + Rim, face, z1 - Rim], RimC));
        p.Add(Box([x1 - Rim, face - Lip, z0 + Rim], [x1 - 0.002, face, z1 - Rim], RimC));
        double cx = (w - 1) * 0.5 * C, cz = (l - 1) * 0.5 * C;
        p.Add(Box([cx - 0.006, Top - 0.012, cz - 0.006], [cx + 0.006, Top, cz + 0.006], hidden: true)); // (the socket's solid face, never drawn)
        p.Sock("module", [cx, Top, cz], "+y", kind: "module");
        p.Sock("bc", [cx, Back, cz], "-y");
        if (w * l > 1)
            foreach (var (n, bx, bz) in new[] { ("b0.0", 0.0, 0.0), ("b1.0", (w - 1) * C, 0.0), ("b0.1", 0.0, (l - 1) * C), ("b1.1", (w - 1) * C, (l - 1) * C) })
                if (Math.Abs(bx - cx) > 0.001 || Math.Abs(bz - cz) > 0.001)
                    p.Sock(n, [bx, Back, bz], "-y");
        var area = w * l * C * C;
        p.Stats(1 + area * 20, 30 + area * 120, 0, seconds: 1.0);
        p.Set(("onBreak", "detach"), ("moduleSocket", Kw(("w", w), ("l", l), ("cells", true))));
        return p;
    }

    private static Part Plug(int w, int l)
    {
        var p = new Part($"Console Plug {Size(w, l)}", Small, "Console bays",
            $"A {Size(w, l)} cell faceplate: put panel instruments on its 10 cm grid (or anywhere with the attach tool), then fit it to a Console Bay of its size.",
            role: "frame", tool: "wrench", color: FaceC, group: Group);
        var (ex0, ex1, ez0, ez1) = Edges(w, l);
        double y0 = Back + 0.004, y1 = Back + PlugT; // (its underside 4 mm over its socket: 6 mm over the bay's drawn face)
        double x0 = ex0 + Inset, x1 = ex1 - Inset, z0 = ez0 + Inset, z1 = ez1 - Inset;
        Framed(p, x0, x1, z0, z1, y0, y1 - Lip, w, l, FaceC);
        Framed(p, x0 + 0.003, x1 - 0.003, z0 + 0.003, z1 - 0.003, y1 - Lip, y1, w, l, FaceTopC);
        double cx = (w - 1) * 0.5 * C, cz = (l - 1) * 0.5 * C;
        p.Add(Box([cx - 0.006, Back, cz - 0.006], [cx + 0.006, Back + 0.012, cz + 0.006], hidden: true)); // (the socket's solid face)
        p.Sock("module", [cx, Back, cz], "-y", kind: "module");
        // A Small socket on each cell's middle (every other cell when that would pass the part's socket limit), then the middle.
        var step = w * l + 2 <= 64 ? 1 : 2;
        for (var i = 0; i < w; i += step)
            for (var j = 0; j < l; j += step)
                p.Sock($"c{i}.{j}", [i * C, y1, j * C], "+y");
        if (w % 2 == 0 || l % 2 == 0 || step > 1)
            p.Sock("tc", [cx, y1, cz], "+y");
        var area = w * l * C * C;
        p.Stats(0.5 + area * 12, 20 + area * 100, 0, seconds: 1.0);
        p.Set(("onBreak", "detach"), ("modulePlug", Kw(("w", w), ("l", l), ("cells", true))));
        return p;
    }

    /// <summary>The shipped parts' names, in their file order: Assembling's Tail and AfterCloche take them one by one.</summary>
    public static IReadOnlyList<string> Names => [.. Parts().Select(e => (string)e.Item2.D["name"]!)];

    private static List<(string, Part)> Parts()
    {
        var parts = new List<(string, Part)>();
        foreach (var (w, l) in Sizes)
        {
            parts.Add(("", Bay(w, l)));
            parts.Add(("", Plug(w, l)));
        }
        parts.AddRange(ControlParts.PanelInstruments());
        return parts;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, Parts());
}
