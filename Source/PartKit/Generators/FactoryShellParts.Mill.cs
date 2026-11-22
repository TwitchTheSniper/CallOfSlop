using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The mill shed (the Zero Day works' one steel shed, layout doc "Exterior": three gabled spans over the crane bays, Sean 2026-11-22):
/// eaves posts that hang off the outer Crane Rail Blocks, half trusses that chain wall to wall over the shared rails, sloped roof sheets,
/// ridge caps and monitors, valley gutters, wall and gable cladding and tall end-wall columns. docs/sandbox/constructs/zeroday-works.md "The shed's shell parts".
///
/// Why the trusses chain rather than sit on the inner column rows: a Hall Crane's bridge is drawn up to 2.62 m over its rails' top and
/// both bays' bridges run over a shared rail, so nothing may stand on an inner rail line under that height. The roof is a chain of half
/// trusses whose bottom chord runs level 4 m over the rail sockets, carried only by the eaves posts on the two outer rows (a construct has
/// no load paths, so a 96 m chain is as sound as a 30 m one).
///
/// Every part's own frame: a half truss runs along its +z from its low end (z 0: an eave or a valley) to its ridge (z = L), rising
/// <see cref="Pitch"/> a metre; a roof sheet lies along the same z from that truss's low top to the ridge, and is a bay wide along +x.
/// </summary>
public sealed partial class FactoryShellParts
{
    /// <summary>The roof's rise a metre (about 17 degrees), the truss's depth at its low end, its chords' depth and half width.</summary>
    public const double Pitch = 0.3, TrussDepth = 1.2, Chord = 0.25, TrussHalfW = 0.12;

    /// <summary>How high an eaves post's seat stands over the rail socket it hangs from: the bridge is drawn 2.62 m over the rail's top
    /// (0.5 m over the socket), so the bracket's underside (0.35 m under the seat) clears it by half a metre.</summary>
    public const double PostSeat = 4.0;

    /// <summary>A wall sheet's back stands this far off the face it hangs on (a Mast Block's or a column's side), girts between.</summary>
    public const double WallOff = 0.5, WallT = 0.06;

    /// <summary>Half spans: bay 1 (30 m), bay 2 (40 m), bay 3 (24 m), and the 20 m and 32 m spans a rail pair allows.</summary>
    public static readonly double[] HalfSpans = [10, 12, 15, 16, 20];

    public static readonly double[] BayWidths = [8, 4];

    private const string Truss = "#4a5058", Clad = "#7d8a86", CladRib = "#6c7874", Trim = "#3a3f46", Louvre = "#2f3438", Roof = "#6a6f6c";

    /// <summary>
    /// A sheared wedge from (z0, y0) to (z1, y1) (centre lines), <paramref name="thick"/> deep in y, cut into pieces each leaning at
    /// most 60 of its thicknesses (the part file's shift limit is 64).
    /// </summary>
    private static void Lean(Part p, double x0, double x1, double z0, double y0, double z1, double y1, double thick, string color)
    {
        var n = Math.Max(1, (int)Math.Ceiling(Math.Abs(y1 - y0) / thick / 60));
        for (var k = 0; k < n; k++)
        {
            double a = z0 + (z1 - z0) * k / n, b = z0 + (z1 - z0) * (k + 1) / n;
            double ya = y0 + (y1 - y0) * k / n, yb = y0 + (y1 - y0) * (k + 1) / n;
            var h = thick / 2;
            p.Add(Box([x0, yb - h, a], [x1, yb + h, b], color, detail: true,
                shape: Kw(("shape", "wedge"), ("taper", Py.L(1, 1)), ("shift", Py.L(0, Py.Round((ya - yb) / thick, 4))))));
        }
    }

    /// <summary>
    /// Hidden solid boxes in steps about 1 m long over a slope whose top runs from (z0, y0) to (z1, y1): each step's underside is the
    /// slope's height at its higher end, so no step reaches under the slope into what the part lies on (a truss's verticals and chord);
    /// what the build rules and feet see is a stair at most 0.3 m proud of the drawn sheet.
    /// </summary>
    private static void Steps(Part p, double x0, double x1, double z0, double y0, double z1, double y1, double thick)
    {
        var n = Math.Max(1, (int)Math.Ceiling(Math.Abs(z1 - z0)));
        for (var k = 0; k < n; k++)
        {
            double a = z0 + (z1 - z0) * k / n, b = z0 + (z1 - z0) * (k + 1) / n;
            var y = Math.Max(y0 + (y1 - y0) * k / n, y0 + (y1 - y0) * (k + 1) / n);
            p.Add(Box([x0, y, Math.Min(a, b)], [x1, y + thick, Math.Max(a, b)], hidden: true));
        }
    }

    private static string M(double v) => v.ToString(v % 1 == 0 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// An Eaves Post: a short H post that bolts onto an outer Crane Rail Block's outer side (its "rail" socket on the block's -x or +x),
    /// standing outside the rail with its foot on the mast's top, and a bracket over the rail whose "seat" carries a half truss's low end
    /// 4 m over the socket, clear of the bridge. Its "wall" socket takes the wall's top sheet (a 3 m Wall Sheet: 1 m under the socket to
    /// 2 m over it, under the eaves' overhang).
    /// </summary>
    private static Part EavesPost()
    {
        const double x0 = -0.4, f = 0.04, w = 0.2, bot = -0.5, by0 = PostSeat - 0.35, xi = -0.023;
        var p = new Part("Eaves Post", Small, "Factory shell", "A post for the top of a shed's side wall: bolt it to the outside of a Crane Rail Block over a mast. Its bracket carries a Roof Truss Half over the rail, clear of the crane.", color: Truss, group: "Building");
        p.Add(Box([x0, bot, -w], [x0 + f, by0, w])); // the outer flange (the wall's face)
        p.Add(Box([xi - f, bot, -w], [xi, by0, w])); // the inner flange, against the rail
        p.Add(Box([x0 + f, bot, -0.02], [xi - f, by0 - 0.004, 0.02])); // the web
        p.Add(Box([xi, -0.4, -0.3], [0, 0.4, 0.3], Trim)); // the plate bolted to the rail's side
        p.Add(Box([x0 + 0.005, by0, -0.15], [1.15, PostSeat, 0.15])); // the bracket over the rail, the seat on its top
        // A gusset under the bracket (stepped plates), stiffeners either side of the web and the plate's bolts.
        for (var k = 0; k < 4; k++)
        {
            var y = by0 - 0.25 * (k + 1);
            p.Add(Box([xi + 0.003, y, -0.012], [0.55 - 0.15 * k, y + 0.252, 0.012], Trim, detail: true));
        }
        foreach (var y in new[] { 0.9, 1.8, 2.7 })
            foreach (var s in new[] { -1, 1 })
                p.Add(Box([x0 + f + 0.003, y, Math.Min(s * 0.023, s * 0.19)], [xi - f - 0.003, y + 0.02, Math.Max(s * 0.023, s * 0.19)], Trim, detail: true)); // a stiffener
        foreach (var (y, z) in new[] { (-0.25, -0.24), (-0.25, 0.24), (0.25, -0.24), (0.25, 0.24) })
            p.Add(Box([xi - 0.025, y - 0.03, z - 0.03], [xi, y + 0.03, z + 0.03], Trim, detail: true, shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "-x"))));
        p.Add(Box([0.85, PostSeat + 0.002, -0.13], [1.13, PostSeat + 0.022, 0.13], Trim, detail: true)); // the seat plate
        p.Sock("rail", [0, 0, 0], "+x");
        p.Sock("seat", [1.0, PostSeat, 0], "+y");
        p.Sock("wall", [x0, 2.5, 0], "-x");
        p.Stats(780, 2200, 18);
        return p;
    }

    /// <summary>A hidden pad under a -y socket at the origin: the solid face the socket sits on, under drawn looks that start higher.</summary>
    private static void Pad(Part p) => p.Add(Box([0, 0, -0.05], [0.2, 0.012, 0.05], hidden: true));

    /// <summary>
    /// A Shed Column: a 0.4 m H column for a shed's end walls, foot on a floor socket. Side sockets every 4 m (1 m, 5 m, 9 m ...) on all
    /// four faces take Wall Sheets, which stack 4 m apiece from the floor; the 22.5 m one stands under a gable truss on a 0.3 m floor.
    /// </summary>
    private static Part ShedColumn(double h)
    {
        const double w = 0.4, f = 0.05;
        var p = new Part($"Shed Column {M(h)} m", Small, "Factory shell", "A tall steel H column for a shed's end walls: stand it on the floor and hang Wall Sheets on its sides.", color: Truss, group: "Building");
        p.Add(Box([-w / 2, 0, -w / 2], [w / 2, h, -w / 2 + f]));
        p.Add(Box([-w / 2, 0, w / 2 - f], [w / 2, h, w / 2]));
        p.Add(Box([-0.025, 0, -w / 2 + f], [0.025, h - 0.004, w / 2 - f]));
        p.Add(Box([-0.35, 0.003, -0.35], [0.35, 0.03, 0.35], Trim, detail: true)); // the base plate
        p.Add(Box([-w / 2 - 0.01, h - 0.02, -w / 2 - 0.01], [w / 2 + 0.01, h - 0.002, w / 2 + 0.01], Trim, detail: true)); // the cap
        for (var y = 4.0; y < h - 0.5; y += 4)
            p.Add(Box([-0.19, y, -w / 2 + f + 0.003], [0.19, y + 0.02, w / 2 - f - 0.003], Trim, detail: true)); // a stiffener at each girt line
        p.Sock("foot", [0, 0, 0], "-y");
        p.Sock("head", [0, h, 0], "+y");
        for (var y = 1.0; y < h; y += 4)
        {
            var n = (int)y;
            p.Sock($"+x{n}", [w / 2, y, 0.17], "+x"); // (on a flange's edge: the web's faces are inside)
            p.Sock($"-x{n}", [-w / 2, y, 0.17], "-x");
            p.Sock($"+z{n}", [0, y, w / 2], "+z");
            p.Sock($"-z{n}", [0, y, -w / 2], "-z");
        }
        p.Stats(h * 0.03 * 7850, 1500 + h * 60, 18);
        return p;
    }

    /// <summary>The top chord's top over the bottom chord's underside at <paramref name="z"/>.</summary>
    private static double Top(double z) => TrussDepth + Pitch * z;

    /// <summary>
    /// A Roof Truss Half <paramref name="l"/> m: a Pratt half truss from its low end (z 0) to the ridge (z = l). Two meet at the ridge by
    /// their "ridge" sockets and at a valley by their "low" ones, so a three-gable roof is one chain from eaves post to eaves post. Its
    /// "seat" sits on an Eaves Post; "topLow" and "topRidge" take roof sheets, ridge caps and gutters; "+x"/"-x" a Gable Sheet.
    /// </summary>
    private static Part TrussHalf(double l)
    {
        double w = TrussHalfW, c = Chord;
        var p = new Part($"Roof Truss Half {M(l)} m", Small, "Factory shell", $"Half of a {M(2 * l)} m gabled roof truss, eave or valley to ridge. Join two at the ridge, chain them across a shed's spans, seat the ends on Eaves Posts.", color: Truss, group: "Building");
        p.Add(Box([-w, 0, 0], [w, c, l])); // the bottom chord
        Lean(p, -w, w, 0, Top(0) - c / 2, l, Top(l) - c / 2, c, Truss); // the top chord
        for (int k = 0, n4 = Math.Max(2, (int)Math.Round(l / 4)); k < n4; k++) // (hidden steps under the top chord's line, each topped at its lower end: a sheet's steps stand over them)
        {
            double a = l * k / n4, b = l * (k + 1) / n4;
            p.Add(Box([-w, Top(a) - c, a], [w, Top(a), b], hidden: true));
        }
        var n = Math.Max(3, (int)Math.Round(l / 2.5));
        var zs = Enumerable.Range(0, n + 1).Select(i => i * l / n).ToArray();
        zs[0] = 0.12; zs[n] = l - 0.12; // (the end posts inside the chords' ends)
        for (var i = 0; i <= n; i++)
        {
            var z = zs[i];
            p.Add(Box([-0.07, c - 0.003, z - 0.06], [0.07, Top(z - 0.06) - c + 0.003, z + 0.06])); // a vertical
            double g0 = Math.Max(0.01, z - 0.2), g1 = Math.Min(l - 0.01, z + 0.2); // (inside the chord's ends: a joined half's gussets meet none of these)
            p.Add(Box([-w - 0.012, 0.02, g0], [-w, c + 0.15, g1], Trim, detail: true)); // gussets either side at its foot
            p.Add(Box([w, 0.02, g0], [w + 0.012, c + 0.15, g1], Trim, detail: true));
        }
        for (var i = 0; i < n; i++)
        {
            double a = zs[i] + 0.07, b = zs[i + 1] - 0.07;
            Lean(p, -0.05, 0.05, a, c + 0.08, b, Top(b) - c - 0.08, 0.15, Trim); // a diagonal, rising to the ridge
        }
        p.Sock("low", [0, 0.12, 0], "-z");
        p.Sock("ridge", [0, 0.12, l], "+z");
        p.Sock("seat", [0, 0, 0], "-y");
        p.Sock("topLow", [0, Top(0), 0], "+y");
        p.Add(Box([-0.1, Top(l) - 0.05, l - 0.2], [0.1, Top(l), l], hidden: true)); // (the ridge socket's face)
        p.Sock("topRidge", [0, Top(l), l], "+y");
        p.Sock("+x", [w, 0.12, 0], "+x");
        p.Sock("-x", [-w, 0.12, 0], "-x");
        for (var z = 2; z < l - 0.5; z += 2)
            p.Sock($"hang{z}", [0, 0, z], "-y"); // (under the bottom chord every 2 m: a High-Bay Pendant hangs here. Added last: saved socket numbers keep)
        p.Stats(l * 260, 1200 + l * 80, 14);
        return p;
    }

    /// <summary>
    /// A Roof Slope: corrugated sheeting a bay wide (+x, truss to truss) from a half truss's low end up to its ridge, its "rest" socket on
    /// the truss's "topLow". An eaves one runs 2 m past the low end, over the wall; a valley one stops 0.3 m short, in the Valley Gutter.
    /// </summary>
    private static Part RoofSlope(double l, double bay, bool eaves)
    {
        double z0 = eaves ? -2.0 : 0.3, z1 = l - 0.06, t = 0.08, lift = 0.003;
        var p = new Part($"Roof Slope {M(bay)}x{M(l)} m {(eaves ? "Eaves" : "Valley")}", Small, "Factory shell",
            eaves ? "Corrugated roofing for a truss bay, eaves to ridge: it overhangs the wall by 2 m." : "Corrugated roofing for a truss bay, valley gutter to ridge.",
            color: Roof, group: "Building");
        double Y(double z) => Pitch * z + lift; // (the sheet's underside)
        Lean(p, 0.004, bay - 0.004, z0, Y(z0) + t / 2, z1, Y(z1) + t / 2, t, Roof);
        Steps(p, 0.004, bay - 0.004, z0, Y(z0), z1, Y(z1), t);
        var ribs = (int)Math.Round(bay / 0.8);
        for (var k = 0; k < ribs; k++)
        {
            var x = (k + 0.5) * bay / ribs;
            Lean(p, x - 0.05, x + 0.05, z0 + 0.003, Y(z0) + t + 0.025, z1 - 0.003, Y(z1) + t + 0.025, 0.05, CladRib); // a corrugation
        }
        if (eaves)
            p.Add(Box([0.004, Y(z0) - 0.2, z0 - 0.12], [bay - 0.004, Y(z0) + t, z0 - 0.003], Trim)); // the eaves gutter's face
        Pad(p);
        p.Sock("rest", [0, 0, 0], "-y");
        var area = bay * (z1 - z0) * 1.044;
        p.Stats(area * 11, area * 50, 3);
        return p;
    }

    /// <summary>A Ridge Cap: the folded flashing over a ridge, a bay wide, its "rest" on a half truss's "topRidge".</summary>
    private static Part RidgeCap(double bay)
    {
        var p = new Part($"Ridge Cap {M(bay)} m", Small, "Factory shell", "The flashing along a roof's ridge, a truss bay long: set it on the ridge where the two slopes meet.", color: Trim, group: "Building");
        const double y0 = 0.15, t = 0.05, r = 0.8;
        Lean(p, 0, bay, -r, y0 - Pitch * r, 0, y0, t, Trim);
        Lean(p, 0, bay, 0, y0, r, y0 - Pitch * r, t, Trim);
        p.Add(Box([0.003, y0 + 0.02, -0.05], [bay - 0.003, y0 + 0.07, 0.05])); // the roll on top
        Pad(p);
        p.Sock("rest", [0, 0, 0], "-y");
        p.Stats(bay * 18, bay * 120, 4);
        return p;
    }

    /// <summary>
    /// A Ridge Monitor: the raised, louvred lantern a mill shed vents its bays through, a bay long, standing over the ridge (its "rest"
    /// on a half truss's "topRidge" in place of a Ridge Cap): louvre walls 1.5 m either side of the ridge and its own low gabled roof.
    /// </summary>
    private static Part RidgeMonitor(double bay)
    {
        var p = new Part($"Ridge Monitor {M(bay)} m", Small, "Factory shell", "A raised louvred vent along a roof's ridge, a truss bay long: set it on the ridge in place of a Ridge Cap.", color: Trim, group: "Building");
        const double half = 1.5, top = 1.6, t = 0.06;
        var sheetTop = 0.003 + 0.08 + 0.05; // (a Roof Slope's corrugations' top over the truss line)
        var foot = sheetTop - Pitch * half + 0.01;
        p.Add(Box([0.003, 0.16, -0.12], [bay - 0.003, 0.3, 0.12])); // the ridge beam (the part's solid)
        foreach (var s in new[] { -1, 1 })
        {
            var zo = s * half;
            double za = Math.Min(zo, zo + s * t), zb = Math.Max(zo, zo + s * t);
            foreach (var x in Enumerable.Range(0, (int)(bay / 2) + 1).Select(i => Math.Clamp(i * 2.0, 0.06, bay - 0.06)))
                p.Add(Box([x - 0.05, foot, za - 0.03], [x + 0.05, top, zb + 0.03], Trim)); // a post
            for (var y = foot + 0.15; y < top - 0.1; y += 0.18)
                Lean(p, 0.06, bay - 0.06, s < 0 ? za - 0.12 : za, s < 0 ? y - 0.06 : y, s < 0 ? zb : zb + 0.12, s < 0 ? y : y - 0.06, 0.03, Louvre); // a louvre blade, leaning out
            p.Add(Box([0.003, top - 0.12, za], [bay - 0.003, top, zb], Trim, detail: true)); // the top rail
        }
        const double over = 2.0, rise = 0.4;
        Lean(p, 0, bay, -over, top + 0.05, 0, top + 0.05 + rise, 0.06, Roof);
        Lean(p, 0, bay, 0, top + 0.05 + rise, over, top + 0.05, 0.06, Roof);
        p.Add(Box([0.003, top + 0.08 + rise, -0.05], [bay - 0.003, top + 0.12 + rise, 0.05], Trim, detail: true)); // its own ridge roll
        Pad(p);
        p.Sock("rest", [0, 0, 0], "-y");
        p.Stats(bay * 90, bay * 300, 5);
        return p;
    }

    /// <summary>A Valley Gutter: the box gutter where two spans' slopes meet over an inner rail row, its "rest" on a "topLow".</summary>
    private static Part ValleyGutter(double bay)
    {
        var p = new Part($"Valley Gutter {M(bay)} m", Small, "Factory shell", "A steel box gutter for the valley between two roof spans, a truss bay long: set it where two Roof Slope Valleys meet.", color: Trim, group: "Building");
        p.Add(Box([0, 0, -0.06], [bay - 0.003, 0.03, 0.06], hidden: true)); // (the socket's face, over the chords' meeting)
        p.Add(Box([0.003, 0.12, -0.34], [bay - 0.003, 0.14, 0.34], Trim, detail: true)); // the sole (over the chords' tops either side)
        foreach (var s in new[] { -1, 1 })
            p.Add(Box([0.003, 0.14, Math.Min(s * 0.32, s * 0.34)], [bay - 0.003, 0.2, Math.Max(s * 0.32, s * 0.34)], Trim, detail: true)); // a side
        p.Add(Box([bay / 2 - 0.08, -0.8, -0.08], [bay / 2 + 0.08, 0.12, 0.08], Trim, detail: true, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+y")))); // the downpipe's head
        p.Sock("rest", [0, 0, 0], "-y");
        p.Stats(bay * 25, bay * 100, 4);
        return p;
    }

    /// <summary>
    /// A Wall Sheet <paramref name="bay"/> wide (+z) and <paramref name="h"/> tall: corrugated cladding standing 0.5 m off the face it hangs
    /// on, by its "back" socket 1 m over its foot, so sheets on a Mast Block column's sides (2 m apart) or a Shed Column's (4 m) stack with
    /// no gap. Girts behind, vertical ribs on its face.
    /// </summary>
    private static Part WallSheet(double bay, double h, double off = WallOff)
    {
        var p = new Part($"Wall Sheet {M(bay)}x{M(h)} m{(off < WallOff ? " Eaves" : "")}", Small, "Factory shell", "Corrugated steel cladding for a shed wall: hang it on a mast, column or Eaves Post side. Its foot is 1 m under the socket.", color: Clad, group: "Building");
        double xo = -off, xi = -off - WallT;
        p.Add(Box([xo, 0.9, 0], [0, 1.1, 0.1], hidden: true)); // (the cleat its socket sits on)
        p.Add(Box([xi, 0, 0.003], [xo, h, bay - 0.003])); // the sheet
        var ribs = (int)Math.Round(bay / 0.5);
        for (var k = 0; k < ribs; k++)
        {
            var z = (k + 0.5) * bay / ribs;
            p.Add(Box([xi - 0.025, 0.004, z - 0.06], [xi, h - 0.004, z + 0.06], CladRib, detail: true)); // a rib
        }
        foreach (var y in new[] { 0.6, h - 0.6 })
            p.Add(Box([xo, y - 0.08, 0.05], [xo + 0.08, y + 0.08, bay - 0.05], Trim, detail: true)); // a girt
        p.Add(Box([xi - 0.035, h - 0.06, 0.008], [xo - 0.005, h + 0.004, bay - 0.008], Trim, detail: true)); // the top flashing
        p.Sock("back", [0, 1, 0], "+x");
        p.Stats(bay * h * 12, bay * h * 60, 4);
        return p;
    }

    /// <summary>
    /// A Gable Sheet: the cladding that closes a gable end over the end wall, from 0.6 m under a half truss's bottom chord up to its roof
    /// line, standing off the truss's side ("rest" on its "+x" or "-x") in line with Wall Sheets on Shed Columns under the truss.
    /// </summary>
    private static Part GableSheet(double l)
    {
        var p = new Part($"Gable Sheet {M(l)} m", Small, "Factory shell", "Cladding for a gable end, over a Roof Truss Half's side from under its bottom chord to the roof line.", color: Clad, group: "Building");
        // (the Shed Column's face is 0.08 m wider than the truss's side, so the sheet stands that much further off to line up)
        double x0 = WallOff + 0.2 - TrussHalfW, x1 = x0 + WallT, y0 = -0.12 - 0.6;
        double Line(double z) => Top(z) - 0.12; // the top chord's top over the socket
        var strips = (int)Math.Ceiling(l);
        for (var k = 0; k < strips; k++)
        {
            double a = k * l / strips, b = (k + 1) * l / strips;
            p.Add(Box([x0, y0, a + 0.003], [x1, Line(b) + 0.02, b - 0.003])); // a strip, its top at its high edge's roof line (the barge board covers the step)
            p.Add(Box([x1, y0 + 0.004, b - 0.06], [x1 + 0.025, Line(b) + 0.01, b + 0.06], CladRib, detail: true)); // the rib at its seam
        }
        Lean(p, x0 - 0.02, x1 + 0.04, 0, Line(0) + 0.15, l, Line(l) + 0.15, 0.5, Trim); // the barge board along the roof line (0.1 m under it to 0.4 m over)
        p.Add(Box([x0 - 0.06, y0, -0.06], [x0, Line(0) + 0.05, 0.06], Trim, detail: true)); // the girt post at the low end
        p.Add(Box([0, -0.1, 0], [x0, 0.1, 0.1], hidden: true)); // (the cleat its socket sits on)
        p.Sock("rest", [0, 0, 0], "-x");
        var area = l * (TrussDepth + 0.7 + Pitch * l / 2);
        p.Stats(area * 12, area * 60, 4);
        return p;
    }

    /// <summary>The shed's part names in the order written: Assembling's Tail and AfterCloche list them so (saved recipe numbers keep).</summary>
    public static IReadOnlyList<string> MillShedNames => MillShedParts().Select(e => (string)e.Item2.D["name"]!).ToList();

    private static IEnumerable<(string, Part)> MillShed() =>
        MillShedParts().Select(e => (e.Item1, e.Item2.Set(("collider", "boxes")))); // (the leaning looks are drawn only: hidden steps collide)

    private static IEnumerable<(string, Part)> MillShedParts()
    {
        yield return ("Shed", EavesPost());
        foreach (var h in new[] { 12.0, 16.0, 20.0, 22.5 })
            yield return ("Shed", ShedColumn(h));
        foreach (var l in HalfSpans)
        {
            yield return ("Shed", TrussHalf(l));
            yield return ("Shed", GableSheet(l));
            foreach (var b in BayWidths)
            {
                yield return ("Shed", RoofSlope(l, b, true));
                yield return ("Shed", RoofSlope(l, b, false));
            }
        }
        foreach (var b in BayWidths)
        {
            yield return ("Shed", RidgeCap(b));
            yield return ("Shed", RidgeMonitor(b));
            yield return ("Shed", ValleyGutter(b));
            foreach (var h in new[] { 4.0, 3.0, 2.0 })
                yield return ("Shed", WallSheet(b, h));
            yield return ("Shed", WallSheet(b, 3.0, WallOff - 0.4)); // (on an Eaves Post's face, 0.4 m out from the rail's: in line with the mast's)
        }
    }
}
