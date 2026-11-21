using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.StationLook;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Hexkey Orbital, the Traders' flat-pack store in orbit (Karl, 2026-11-20/21; layout draft 6, the project's megastore/layout.html,
/// notes in docs/sandbox/constructs/station.md "Hexkey Orbital"). Phase 3, the store's shell: a 160 x 96 m box of four levels.
/// L0 is the warehouse (0 to 8 m): twelve self-serve rack aisles, the Big-item pick-up, the Reduced corner, eight trade arches in a
/// barrier, the ration kiosk and the loading lane to the exit lifts. L1 is the market hall (8 to 13 m): nine department aisles of
/// gondolas with end caps, bargain bins on the main street, the stock room. L2 is the showroom (13 to 19 m): three lanes of
/// room-set bays (14 a side, 12 m apart) along a one-way route, shortcuts between lanes, the planning desks and the canteen.
/// The roof (19 m) has fifteen 20 m pads between the Arrivals and Departures pavilions, and the store's name on a lit board.
/// Room sets, product walls and shelf stock are filled from the store's stock in a later phase: the bays here are empty.
/// <para>
/// Plan metres: px east from the west wall, py south from the north wall (the layout's drawings); <see cref="X"/> and <see cref="Z"/>
/// put them in the station frame (x east, z south, y up, the origin the middle of the warehouse floor). The look is "Nordic in
/// space" (Karl): white panels, thin charcoal lines, birch floors in the showroom, pine signs and a terracotta route line.
/// </para>
/// </summary>
public sealed partial class SpaceStationParts
{
    public const string HexkeyName = "Hexkey Orbital";

    // ---- the Nordic palette
    private const string NWhite = "#eceeea", NBirch = "#d6bf96", NAsh = "#b5b9b7", NDeck = "#8e9290", NFelt = "#9fa4a1", NChar = "#2b2e30",
        NPine = "#3e6b5a", NRoute = "#b8643f", NGlassCol = "#c4d8d5", NPad = "#e4e6e2", NDeal = "#d9b45a", NSignWhite = "#f4f4ef";

    private const string Matte = "painted plating", GlassFin = "glass";

    private const double FW = 160, FD = 96; // footprint, plan metres
    private const double ExtT = 0.3, PartT = 0.2, Rise = 0.25, Run = 0.3;
    private const double HL0 = 0, HL1 = 8, HL2 = 13, HRoof = 19, HCap = 23.5;

    // the floor pieces' grid (L0 to L2): seven columns and four rows, each cell one Gravity Generator's field
    private static readonly double[] ColX = [0, 23, 46, 69, 92, 115, 138, 160], RowZ = [0, 24, 48, 72, 96];

    // the showroom: three lanes (their north room row's back line), rooms 9.5 m deep, a 5 m route between the rows; bay walls along each row
    private static readonly double[] LaneZ = [4, 28, 52];
    private const double RoomD = 9.5, PathW = 5, BayX0 = 8, BayX1 = 152;
    private static readonly double[] BayWalls = [8, 20, 32, 44, 56, 68, 78, 82, 92, 104, 116, 128, 140, 152];

    private static double X(double px) => px - FW / 2;
    private static double Z(double py) => py - FD / 2;

    /// <summary>A plan rectangle (px, py, px, py).</summary>
    private readonly record struct Rect(double X0, double Y0, double X1, double Y1)
    {
        public bool Has(double x, double y) => x >= X0 && x < X1 && y >= Y0 && y < Y1;
    }

    /// <summary>A rectangle less its holes, as boxes: the grid of every hole's edges, each band's free cells merged along x.</summary>
    private static List<Rect> Cut(Rect r, IEnumerable<Rect> holes)
    {
        var hs = holes.Where(h => h.X1 > r.X0 && h.X0 < r.X1 && h.Y1 > r.Y0 && h.Y0 < r.Y1)
            .Select(h => new Rect(Math.Max(h.X0, r.X0), Math.Max(h.Y0, r.Y0), Math.Min(h.X1, r.X1), Math.Min(h.Y1, r.Y1))).ToList();
        var xs = hs.SelectMany(h => new[] { h.X0, h.X1 }).Append(r.X0).Append(r.X1).Distinct().Order().ToList();
        var ys = hs.SelectMany(h => new[] { h.Y0, h.Y1 }).Append(r.Y0).Append(r.Y1).Distinct().Order().ToList();
        var out_ = new List<Rect>();
        for (var j = 0; j + 1 < ys.Count; j++)
        {
            double? start = null;
            for (var i = 0; i + 1 < xs.Count; i++)
            {
                double cx = (xs[i] + xs[i + 1]) / 2, cy = (ys[j] + ys[j + 1]) / 2;
                var free = !hs.Any(h => h.Has(cx, cy));
                if (free && start == null)
                    start = xs[i];
                if (!free && start != null)
                {
                    out_.Add(new Rect(start.Value, ys[j], xs[i], ys[j + 1]));
                    start = null;
                }
            }
            if (start != null)
                out_.Add(new Rect(start.Value, ys[j], xs[^1], ys[j + 1]));
        }
        return out_;
    }

    /// <summary>A floor slab piece: solid (white underneath, the ceiling below) with its floor's skin on top.</summary>
    private static Part Floor(Kit k, string name, Rect r, double top, double thick, string skin, string skinFin, IEnumerable<Rect> holes, string desc)
    {
        var p = k.Piece(name, desc, NWhite);
        foreach (var c in Cut(r, holes))
        {
            Solid(p, X(c.X0), top - thick, Z(c.Y0), X(c.X1), top, Z(c.Y1), NWhite, Matte);
            Detail(p, X(c.X0) + 0.004, top, Z(c.Y0) + 0.004, X(c.X1) - 0.004, top + 0.008, Z(c.Y1) - 0.004, skin, skinFin);
        }
        return p;
    }

    /// <summary>
    /// A wall along a plan line, axis 'x' (runs along px at fixed py) or 'z' (along py at fixed px), from u0 to u1, y0 to y1, centred
    /// on the line (or set inside it by <paramref name="off"/>), with doorways: (centre, width, height). Charcoal frames round each
    /// doorway on both faces (none on a doorway as tall as the wall: a gap).
    /// </summary>
    private static void WallRun(Part p, char axis, double line, double u0, double u1, double y0, double y1, double t, double off,
        (double C, double W, double H)[] doors, string color, string fin, bool trims = true)
    {
        double a0 = line - t / 2 + off, a1 = line + t / 2 + off;
        void Box(double v0, double v1, double h0, double h1)
        {
            if (v1 - v0 < 0.01 || h1 - h0 < 0.01)
                return;
            if (axis == 'x')
                Solid(p, X(v0), h0, Z(a0), X(v1), h1, Z(a1), color, fin);
            else
                Solid(p, X(a0), h0, Z(v0), X(a1), h1, Z(v1), color, fin);
        }
        void Trim(double v0, double v1, double h0, double h1)
        {
            foreach (var (f0, f1) in new[] { (a0 - 0.012, a0 - 0.004), (a1 + 0.004, a1 + 0.012) })
                if (axis == 'x')
                    Detail(p, X(v0), h0, Z(f0), X(v1), h1, Z(f1), NChar, Matte);
                else
                    Detail(p, X(f0), h0, Z(v0), X(f1), h1, Z(v1), NChar, Matte);
        }
        var at = u0;
        foreach (var (c, w, h) in doors.OrderBy(d => d.C))
        {
            double d0 = c - w / 2, d1 = c + w / 2;
            Box(at, d0, y0, y1);
            Box(d0, d1, y0 + h, y1);
            if (trims && h < y1 - y0 - 0.01)
            {
                Trim(d0 - 0.12, d0, y0, y0 + h + 0.12);
                Trim(d1, d1 + 0.12, y0, y0 + h + 0.12);
                Trim(d0, d1, y0 + h, y0 + h + 0.12);
            }
            at = d1;
        }
        Box(at, u1, y0, y1);
    }

    /// <summary>An outer wall's outside: a window band (glass with charcoal mullions every 6 m) and, at the top storey, the charcoal
    /// band under the roof line. Drawn a few millimetres off both faces.</summary>
    private static void OuterLook(Part p, char side, double u0, double u1, double y0, double y1, double w0, double w1, bool band)
    {
        // side: n s e w; the wall's outer face is at the footprint edge, its inner face ExtT inside
        (double o0, double o1, double i0, double i1) = side switch
        {
            'n' => (-0.035, -0.006, ExtT + 0.006, ExtT + 0.022),
            's' => (FD + 0.006, FD + 0.035, FD - ExtT - 0.022, FD - ExtT - 0.006),
            'w' => (-0.035, -0.006, ExtT + 0.006, ExtT + 0.022),
            _ => (FW + 0.006, FW + 0.035, FW - ExtT - 0.022, FW - ExtT - 0.006),
        };
        bool along = side is 'n' or 's';
        void D(double v0, double v1, double h0, double h1, double f0, double f1, string col, string? fin = null, bool glow = false)
        {
            if (along)
                Detail(p, X(v0), h0, Z(f0), X(v1), h1, Z(f1), col, fin, glow);
            else
                Detail(p, X(f0), h0, Z(v0), X(f1), h1, Z(v1), col, fin, glow);
        }
        D(u0 + 0.05, u1 - 0.05, w0, w1, o0 + 0.008, o1, NGlassCol, GlassFin);
        D(u0 + 0.05, u1 - 0.05, w0, w1, i0, i1, NGlassCol, GlassFin);
        var outer = side is 'n' or 'w';
        for (var u = Math.Ceiling(u0 / 6) * 6; u <= u1 - 0.1; u += 6)
            if (u > u0 + 0.1)
                D(u - 0.08, u + 0.08, w0 - 0.05, w1 + 0.05, outer ? o0 - 0.012 : o1 - 0.012, outer ? o0 + 0.006 : o1 + 0.012, NChar, Matte);
        if (band)
            D(u0, u1, y1 - 0.9, y1 + 0.3, o0, o1 - 0.002, NChar, Matte);
    }

    private static Kit HexkeyStation()
    {
        var k = new Kit("Hexkey", HexkeyName);
        const string floorDesc = "A floor of Hexkey Orbital, the Traders' flat-pack store: a white slab, its floor laid on top.";

        // ---- the holes in the slabs: the stairs between floors
        var arrivalHole = new Rect(0.9, 18.6, 5.1, 26.1);   // roof, over the arrival stair down to the showroom (it runs along py)
        var marketHole = new Rect(143.9, 83.9, 150.2, 88.1); // L2, over the stair down to the market
        var warehouseHole = new Rect(3.9, 88.9, 13.8, 93.1); // L1, over the stair down to the warehouse
        var holes = new Dictionary<string, Rect[]>
        {
            ["L0"] = [],
            ["L1"] = [warehouseHole],
            ["L2"] = [marketHole],
            ["Roof"] = [arrivalHole],
        };

        // ---- floors: L0 to L2 in the 7 x 4 grid; the roof by pad columns
        var floorPieces = new List<(string Level, Rect R, Part P)>();
        foreach (var (lvl, top, thick, skin, fin) in new[] { ("L0", HL0, 0.6, NDeck, "painted deck plate"), ("L1", HL1, 0.3, NFelt, Matte), ("L2", HL2, 0.3, NBirch, Matte) })
            for (var c = 0; c + 1 < ColX.Length; c++)
                for (var r = 0; r + 1 < RowZ.Length; r++)
                {
                    var rect = new Rect(ColX[c], RowZ[r], ColX[c + 1], RowZ[r + 1]);
                    floorPieces.Add((lvl, rect, Floor(k, $"{lvl} Floor {c + 1}-{r + 1}", rect, top, thick, skin, fin, holes[lvl], floorDesc)));
                }
        double[] roofCols = [0, 26, 48, 70, 92, 114, 136, 160], roofRows = [0, 36, 58, 96];
        for (var c = 0; c + 1 < roofCols.Length; c++)
            for (var r = 0; r + 1 < roofRows.Length; r++)
            {
                var rect = new Rect(roofCols[c], roofRows[r], roofCols[c + 1], roofRows[r + 1]);
                floorPieces.Add(("Roof", rect, Floor(k, $"Roof {c + 1}-{r + 1}", rect, HRoof, 0.3, NAsh, Matte, holes["Roof"],
                    "The roof of Hexkey Orbital: landing pads for Shuttles and Limpets on a grey deck, walkways between them.")));
            }
        Part FloorAt(string lvl, double px, double py) => floorPieces.First(f => f.Level == lvl && f.R.Has(Math.Clamp(px, 0, FW - 0.01), Math.Clamp(py, 0, FD - 0.01))).P;
        double Lvl(string lvl) => lvl switch { "L0" => HL0, "L1" => HL1, "L2" => HL2, _ => HRoof };
        double Ceil(string lvl) => lvl switch { "L0" => HL1 - 0.3, "L1" => HL2 - 0.3, _ => HRoof - 0.3 };

        // a flat patch on a level's floor, split over the floor pieces it crosses
        void Patch(string lvl, double x0, double y0, double x1, double y1, double lift, double thick, string col, bool glow = false)
        {
            var grid = lvl == "Roof" ? (roofCols, roofRows) : (ColX, RowZ);
            for (var i = 0; i + 1 < grid.Item1.Length; i++)
                for (var j = 0; j + 1 < grid.Item2.Length; j++)
                {
                    double sx0 = Math.Max(x0, grid.Item1[i]), sx1 = Math.Min(x1, grid.Item1[i + 1]), sy0 = Math.Max(y0, grid.Item2[j]), sy1 = Math.Min(y1, grid.Item2[j + 1]);
                    if (sx1 - sx0 < 0.05 || sy1 - sy0 < 0.05)
                        continue;
                    var p = FloorAt(lvl, (sx0 + sx1) / 2, (sy0 + sy1) / 2);
                    var y = Lvl(lvl) + lift;
                    Detail(p, X(sx0), y, Z(sy0), X(sx1), y + thick, Z(sy1), col, Matte, glow);
                }
        }
        void Route(string lvl, (double X, double Y)[] pts)
        {
            for (var i = 0; i + 1 < pts.Length; i++)
            {
                var (a, b) = (pts[i], pts[i + 1]);
                var lift = 0.011 + (i % 2) * 0.003; // (crossing strips at their corners don't share a plane)
                Patch(lvl, Math.Min(a.X, b.X) - 0.075, Math.Min(a.Y, b.Y) - 0.075, Math.Max(a.X, b.X) + 0.075, Math.Max(a.Y, b.Y) + 0.075, lift, 0.006, NRoute);
            }
            // arrows along the route every 8 m: a short wide bar ahead of a narrow one
            for (var i = 0; i + 1 < pts.Length; i++)
            {
                var (a, b) = (pts[i], pts[i + 1]);
                double len = Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y), dx = Math.Sign(b.X - a.X), dy = Math.Sign(b.Y - a.Y);
                for (var s = 4.0; s < len - 2; s += 8)
                {
                    double cx = a.X + dx * s, cy = a.Y + dy * s;
                    for (var t = 0; t < 3; t++)
                    {
                        double w = 0.25 + 0.25 * t, back = 0.35 * t, px = cx - dx * back, py = cy - dy * back;
                        // the chevron's rows: widest at the back
                        double hx = dx != 0 ? 0.12 : w, hy = dy != 0 ? 0.12 : w;
                        Patch(lvl, px - hx, py - hy, px + hx, py + hy, 0.021, 0.005, NRoute);
                    }
                }
            }
        }

        // ---- roof: the fifteen pads (a flush Docking Hatch and a Docking Clamp Medium each, as Caledon Highport's)
        var n = 0;
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 5; c++)
            {
                n++;
                double x0 = 27 + c * 22, y0 = 15 + r * 22, cx = x0 + 10, cy = y0 + 10;
                var p = FloorAt("Roof", cx, cy);
                Detail(p, X(x0), HRoof + 0.011, Z(y0), X(x0 + 20), HRoof + 0.016, Z(y0 + 20), NPad, Matte);
                foreach (var (a, b, d, e) in new[] { (x0, x0 + 20, y0, y0 + 0.4), (x0, x0 + 20, y0 + 19.6, y0 + 20), (x0, x0 + 0.4, y0 + 0.4, y0 + 19.6), (x0 + 19.6, x0 + 20, y0 + 0.4, y0 + 19.6) })
                    Detail(p, X(a), HRoof + 0.019, Z(d), X(b), HRoof + 0.025, Z(e), NPine, Matte);
                Detail(p, X(cx - 2.2), HRoof + 0.019, Z(cy - 2.5), X(cx - 1.6), HRoof + 0.025, Z(cy + 2.5), NPine, Matte);
                Detail(p, X(cx + 1.6), HRoof + 0.019, Z(cy - 2.5), X(cx + 2.2), HRoof + 0.025, Z(cy + 2.5), NPine, Matte);
                Detail(p, X(cx - 1.6), HRoof + 0.019, Z(cy - 0.3), X(cx + 1.6), HRoof + 0.025, Z(cy + 0.3), NPine, Matte);
                foreach (var (sx, sy) in Corners)
                    Lamp(p, X(cx + sx * 9.5) - 0.15, HRoof + 0.019, Z(cy + sy * 9.5) - 0.15, X(cx + sx * 9.5) + 0.15, HRoof + 0.12, Z(cy + sy * 9.5) + 0.15);
                // the pad's number on a post by its walkway corner, in the board's own letters
                Detail(p, X(x0 - 0.6) - 0.06, HRoof + 0.019, Z(y0 - 0.6) - 0.06, X(x0 - 0.6) + 0.06, HRoof + 2.6, Z(y0 - 0.6) + 0.06, NChar, Matte);
                Detail(p, X(x0 - 0.6) - 0.5, HRoof + 1.7, Z(y0 - 0.6) - 0.04, X(x0 - 0.6) + 0.5, HRoof + 2.5, Z(y0 - 0.6) + 0.04, NPine, Matte);
                Letters(p, n.ToString(), X(x0 - 0.6), HRoof + 1.85, Z(y0 - 0.6), 0.08, 'x', [-0.06, 0.06], NSignWhite, true);
                var tag = $"pad{n}";
                Sock(p, tag + "hatch", X(cx), HRoof, Z(cy) + 0.69, "+y");
                Sock(p, tag + "clamp", X(cx) + 3.5, HRoof, Z(cy) - 3.5, "+y", Medium);
                k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag + "hatch", "Docking Hatch", "base", null, null, null));
                k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag + "clamp", "Docking Clamp Medium", "-y",
                    new PyDict { ["startOn"] = true, ["sizeX"] = 18, ["sizeY"] = 8, ["sizeZ"] = 18, ["atY"] = 4.5, ["atX"] = -3.5, ["atZ"] = 3.5 }, NPine, Matte));
            }
        // walkway lamps between the pad rows and round the pavilions
        for (var x = 30.0; x < 134; x += 8)
            foreach (var y in new[] { 36.0, 58.0 })
                Lamp(FloorAt("Roof", x, y + 0.5), X(x) - 0.3, HRoof + 0.011, Z(y) - 0.08, X(x) + 0.3, HRoof + 0.05, Z(y) + 0.08);

        // ---- outer walls, every level, one piece per grid cell's run
        foreach (var (lvl, w0, w1, top) in new[] { ("L0", 4.6, 7.0, false), ("L1", 9.6, 12.0, false), ("L2", 14.6, 18.0, true) })
        {
            double y0 = Lvl(lvl), y1 = Ceil(lvl);
            for (var c = 0; c + 1 < ColX.Length; c++)
                foreach (var (side, line, off) in new[] { ('n', 0.0, ExtT / 2), ('s', FD, -ExtT / 2) })
                {
                    var p = k.Piece($"{lvl} Wall {char.ToUpperInvariant(side)}{c + 1}", "An outer wall of Hexkey Orbital: white panels, a long window band, thin charcoal mullions.", NWhite);
                    WallRun(p, 'x', line, ColX[c], ColX[c + 1], y0, y1, ExtT, off, [], NWhite, Matte);
                    OuterLook(p, side, ColX[c], ColX[c + 1], y0, y1, w0, w1, top);
                    Finish(p);
                }
            for (var r = 0; r + 1 < RowZ.Length; r++)
                foreach (var (side, line, off) in new[] { ('w', 0.0, ExtT / 2), ('e', FW, -ExtT / 2) })
                {
                    double u0 = Math.Max(ExtT, RowZ[r]), u1 = Math.Min(FD - ExtT, RowZ[r + 1]);
                    var p = k.Piece($"{lvl} Wall {char.ToUpperInvariant(side)}{r + 1}", "An outer wall of Hexkey Orbital: white panels, a long window band, thin charcoal mullions.", NWhite);
                    // the warehouse's east wall opens onto the freight pier, at the Big-item pick-up
                    (double, double, double)[] doors = lvl == "L0" && side == 'e' && r == 0 ? [(16, 10, 6)] : [];
                    WallRun(p, 'z', line, u0, u1, y0, y1, ExtT, off, doors, NWhite, Matte);
                    OuterLook(p, side, u0, u1, y0, y1, w0, w1, top);
                    Finish(p);
                }
        }

        // ---- inside walls: (level, name, axis, line, from, to, doorways)
        const double Door = 3, Staff = 1.0;
        var bayDoors = new List<(double, double, double)>();
        for (var i = 0; i + 1 < BayWalls.Length; i++)
            if (BayWalls[i + 1] - BayWalls[i] > 6)
                bayDoors.Add(((BayWalls[i] + BayWalls[i + 1]) / 2 + 3, Staff, 2.1)); // a staff door in each north bay's back, off centre
        bayDoors.Add((4, Staff, 2.1)); // the service corridor's door from the arrival stair's corner
        (string Lvl, string Name, char Ax, double Line, double U0, double U1, (double, double, double)[] Doors)[] walls =
        [
            // L2: the back of lane A's north rooms (the service corridor behind them), the lane walls (back to back rooms), the south hall
            ("L2", "Service Wall", 'x', LaneZ[0], ExtT, FW - ExtT, bayDoors.ToArray()),
            ("L2", "Lane Wall AB", 'x', LaneZ[1], ExtT, BayX1 + 0.1, [(80, 3.6, Door)]),
            ("L2", "Lane Wall BC", 'x', LaneZ[2], BayX0 - 0.1, FW - ExtT, [(80, 3.6, Door)]),
            ("L2", "Hall Wall", 'x', LaneZ[2] + 24, ExtT, BayX1 + 0.1, []),
            ("L2", "Staff Wall", 'z', 40, LaneZ[2] + 24.1, FD - ExtT, [(86, 1.2, 2.4)]),
            // L1: back of house along the north, the stock room east
            ("L1", "Back Wall", 'x', 8, ExtT, FW - ExtT, [(20, 1.2, 2.4), (80, 1.2, 2.4), (150, 2.4, 2.6)]),
            ("L1", "Stock Wall W", 'z', 144, 8.1, 69.9, []),
            ("L1", "Stock Wall S", 'x', 70, 143.9, FW - ExtT, [(151, 2.4, 2.6)]),
            // L0: the stock-in strip, the entry corridor from the market stair (kept apart from the loading lane)
            ("L0", "Stock Wall", 'x', 6, ExtT, FW - ExtT, [(60, 4, 4), (130, 4, 4)]),
            ("L0", "Entry Wall", 'z', 22, 70.2, FD - ExtT, []),
        ];
        foreach (var (lvl, name, ax, line, u0, u1, doors) in walls)
        {
            var p = k.Piece($"{lvl} {name}", "A partition of Hexkey Orbital: white, with charcoal-framed doorways.", NWhite);
            WallRun(p, ax, line, u0, u1, Lvl(lvl), Ceil(lvl), PartT, 0, doors, NWhite, Matte);
            Finish(p);
        }

        // ---- L2: the room-set bays: a white wall between bays, 9.5 m deep, a birch cap and a charcoal edge at the route
        foreach (var (lane, ln) in LaneZ.Select((z, i) => (z, "ABC"[i])))
            foreach (var (row, z0, z1) in new[] { ('N', lane + 0.1, lane + RoomD), ('S', lane + RoomD + PathW, lane + 24 - 0.1) })
            {
                var p = k.Piece($"L2 Bays {ln}{row}", "The room-set bays of Hexkey Orbital's showroom: white walls 12 m apart, each bay a room furnished from the store's stock.", NWhite);
                foreach (var bx in BayWalls)
                {
                    Solid(p, X(bx - PartT / 2), HL2, Z(z0), X(bx + PartT / 2), Ceil("L2"), Z(z1), NWhite, Matte);
                    // the bay's front edge: a charcoal post on the route side, a birch number plate
                    double front = row == 'N' ? z1 : z0, fo = row == 'N' ? 0.004 : -0.012;
                    Detail(p, X(bx - 0.11), HL2, Z(front + fo), X(bx + 0.11), Ceil("L2"), Z(front + fo + 0.008), NChar, Matte);
                }
                // a ceiling over each bay (the room's own deckhead), 3.2 m up, the plant space above it showing at the front
                for (var i = 0; i + 1 < BayWalls.Length; i++)
                {
                    double a = BayWalls[i] + PartT / 2, b = BayWalls[i + 1] - PartT / 2;
                    if (b - a < 6)
                        continue;
                    Detail(p, X(a) + 0.004, HL2 + 3.2, Z(z0) + 0.004, X(b) - 0.004, HL2 + 3.32, Z(z1) - 0.004, NWhite, Matte);
                    Lamp(p, X((a + b) / 2) - 1.2, HL2 + 3.12, Z((z0 + z1) / 2) - 0.1, X((a + b) / 2) + 1.2, HL2 + 3.196, Z((z0 + z1) / 2) + 0.1);
                    // the room card's stand at the front corner, a tag rail along the front
                    double fz = row == 'N' ? z1 - 0.6 : z0 + 0.6;
                    Detail(p, X(a + 0.4), HL2 + 0.011, Z(fz) - 0.05, X(a + 0.5), HL2 + 1.3, Z(fz) + 0.05, NChar, Matte);
                    Detail(p, X(a + 0.2), HL2 + 1.3, Z(fz) - 0.03, X(a + 0.9), HL2 + 1.75, Z(fz) + 0.03, NPine, Matte);
                }
                Finish(p);
            }

        // ---- L2: lane lamps and the department signs over the route, every 24 m
        foreach (var lane in LaneZ)
        {
            var mid = lane + RoomD + PathW / 2;
            for (var x = 14.0; x < BayX1; x += 12)
                Lamp(FloorAt("L2", x, mid), X(x) - 0.8, Ceil("L2") - 0.12, Z(mid) - 0.08, X(x) + 0.8, Ceil("L2") - 0.012, Z(mid) + 0.08);
            for (var x = 20.0; x < BayX1; x += 24)
            {
                var p = FloorAt("L2", x, mid);
                foreach (var s in new[] { -1, 1 })
                    Detail(p, X(x) - 0.015, HL2 + 4.8, Z(mid + s * 1.5) - 0.015, X(x) + 0.015, Ceil("L2") - 0.004, Z(mid + s * 1.5) + 0.015, NChar, Matte);
                Detail(p, X(x) - 0.04, HL2 + 3.9, Z(mid - 1.8), X(x) + 0.04, HL2 + 4.8, Z(mid + 1.8), NPine, Matte);
                foreach (var f in new[] { -0.052, 0.044 }) // the sign's white name strip both faces (the department's name comes with the stock)
                    Detail(p, X(x) + f, HL2 + 4.2, Z(mid - 1.5), X(x) + f + 0.008, HL2 + 4.5, Z(mid + 1.5), NSignWhite, Matte, glow: true);
            }
        }

        // ---- L2 south hall: the planning desks and the canteen's counter
        {
            var p = k.Piece("L2 Planning Desks", "Hexkey Orbital's planning desks: a long white counter where shoppers look up any piece in the store.", NWhite);
            Solid(p, X(46), HL2, Z(92.4), X(90), HL2 + 0.92, Z(93.4), NWhite, Matte);
            Detail(p, X(46) - 0.01, HL2 + 0.92, Z(92.4) - 0.01, X(90) + 0.01, HL2 + 0.96, Z(93.4) + 0.01, NBirch, Matte);
            for (var x = 48.0; x < 90; x += 4)
                Detail(p, X(x) - 0.3, HL2 + 0.96, Z(92.9) - 0.03, X(x) + 0.3, HL2 + 1.36, Z(92.9) + 0.03, NChar, Matte, glow: true);
            Finish(p);
            var c = k.Piece("L2 Canteen Counter", "The Hexkey Canteen's counter: birch and white, a coffee bar at its end, a window over the pads.", NBirch);
            Solid(c, X(100), HL2, Z(79.6), X(126), HL2 + 1.05, Z(80.6), NBirch, Matte);
            Detail(c, X(100) - 0.01, HL2 + 1.05, Z(79.6) - 0.01, X(126) + 0.01, HL2 + 1.09, Z(80.6) + 0.01, NWhite, Matte);
            Detail(c, X(100), HL2 + 2.7, Z(79.65), X(126), HL2 + 3.3, Z(79.7), NPine, Matte);
            Finish(c);
        }

        // ---- L1 market hall: nine department aisles of gondolas with end caps, bargain bins on the main street
        for (var a = 0; a < 9; a++)
        {
            var x0 = 142 - (a + 1) * 13.2;
            var p = k.Piece($"L1 Aisle {a + 1}", "A department aisle of Hexkey Orbital's market hall: white gondolas with birch shelves, an end cap facing the main street.", NWhite);
            for (var g = 0; g < 3; g++)
            {
                double gx = x0 + 1.2 + g * 4.0;
                Solid(p, X(gx), HL1, Z(16), X(gx + 1.6), HL1 + 2.1, Z(62), NWhite, Matte);
                for (var s = 0; s < 4; s++) // shelf edges both sides, birch
                    foreach (var (f0, f1) in new[] { (gx - 0.012, gx - 0.004), (gx + 1.604, gx + 1.612) })
                        Detail(p, X(f0), HL1 + 0.35 + s * 0.45, Z(16.05), X(f1), HL1 + 0.39 + s * 0.45, Z(61.95), NBirch, Matte);
                // the end cap: a low birch table
                Solid(p, X(gx - 0.2), HL1, Z(62), X(gx + 1.8), HL1 + 0.9, Z(63.2), NBirch, Matte);
            }
            // the aisle's sign, hung over its north end (the department's name comes with the stock)
            Detail(p, X(x0 + 1), HL1 + 3.4, Z(12) - 0.04, X(x0 + 12.2), HL1 + 4.0, Z(12) + 0.04, NPine, Matte);
            foreach (var f in new[] { -0.052, 0.044 })
                Detail(p, X(x0 + 1.6), HL1 + 3.55, Z(12) + f, X(x0 + 11.6), HL1 + 3.85, Z(12) + f + 0.008, NSignWhite, Matte, glow: true);
            foreach (var xx in new[] { x0 + 1.5, x0 + 11.7 })
                Detail(p, X(xx) - 0.015, HL1 + 4.0, Z(12) - 0.015, X(xx) + 0.015, Ceil("L1") - 0.004, Z(12) + 0.015, NChar, Matte);
            Finish(p);
        }
        {
            var p = k.Piece("L1 Bargain Bins", "Bargain bins on Hexkey Orbital's main street: low open crates of small parts.", NDeal);
            for (var i = 0; i < 8; i++)
            {
                double bx = 30 + i * 14;
                Solid(p, X(bx), HL1, Z(80), X(bx + 4), HL1 + 0.8, Z(82), NDeal, Matte);
                Detail(p, X(bx) - 0.01, HL1 + 0.8, Z(80) - 0.01, X(bx + 4) + 0.01, HL1 + 0.84, Z(82) + 0.01, NChar, Matte);
            }
            Finish(p);
        }
        for (var x = 20.0; x < 142; x += 12)
            Lamp(FloorAt("L1", x, 77), X(x) - 0.8, Ceil("L1") - 0.12, Z(77) - 0.08, X(x) + 0.8, Ceil("L1") - 0.012, Z(77) + 0.08);
        for (var a = 0; a < 9; a++)
            for (var z = 20.0; z < 62; z += 10)
            {
                var x = 142 - (a + 1) * 13.2 + 6.6;
                Lamp(FloorAt("L1", x, z), X(x) - 0.08, Ceil("L1") - 0.12, Z(z) - 0.8, X(x) + 0.08, Ceil("L1") - 0.012, Z(z) + 0.8);
            }

        // ---- L0 warehouse: twelve rack aisles, numbered
        for (var i = 0; i < 12; i++)
        {
            double rx = 24 + i * 8;
            var p = k.Piece($"L0 Rack {i + 1}", "A self-serve rack of Hexkey Orbital's warehouse: bins six high for 1 m flat-pack boxes, charcoal uprights, birch beams.", NAsh);
            Solid(p, X(rx), HL0, Z(9), X(rx + 3.2), HL0 + 6.6, Z(61), NAsh, Matte);
            for (var z = 9.0; z <= 61.01; z += 2) // uprights both faces
                foreach (var (f0, f1) in new[] { (rx - 0.03, rx - 0.004), (rx + 3.204, rx + 3.23) })
                    Detail(p, X(f0), HL0, Z(Math.Min(z, 60.92)), X(f1), HL0 + 6.6, Z(Math.Min(z, 60.92) + 0.08), NChar, Matte);
            for (var s = 1; s < 6; s++) // beams
                foreach (var (f0, f1) in new[] { (rx - 0.022, rx - 0.008), (rx + 3.208, rx + 3.222) })
                    Detail(p, X(f0), HL0 + s * 1.1 - 0.06, Z(9.1), X(f1), HL0 + s * 1.1, Z(60.9), NBirch, Matte);
            // the aisle number on a pine board at its south end
            Detail(p, X(rx + 0.4), HL0 + 6.0, Z(61) + 0.004, X(rx + 2.8), HL0 + 7.2, Z(61) + 0.06, NPine, Matte);
            Letters(p, (i + 1).ToString(), X(rx + 1.6), HL0 + 6.25, Z(61) + 0.06, 0.12, 'z', [0.004, 0.016], NSignWhite, false);
            Finish(p);
        }
        for (var x = 26.0; x < 120; x += 8)
            for (var z = 14.0; z < 62; z += 12)
                Lamp(FloorAt("L0", x + 2.4, z), X(x + 2.4) - 0.08, Ceil("L0") - 0.12, Z(z) - 0.8, X(x + 2.4) + 0.08, Ceil("L0") - 0.012, Z(z) + 0.8);

        // ---- L0: the Big-item pick-up counter, the Reduced corner, the trade arches in their barrier, the kiosk, bag-up tables
        {
            var p = k.Piece("L0 Pick-up Counter", "Hexkey Orbital's Big-item pick-up: a long birch counter where the store's robot shells bring out pallets.", NBirch);
            Solid(p, X(122.5), HL0, Z(12), X(123.5), HL0 + 1.05, Z(32), NBirch, Matte);
            Detail(p, X(122.5) - 0.01, HL0 + 1.05, Z(12) - 0.01, X(123.5) + 0.01, HL0 + 1.09, Z(32) + 0.01, NWhite, Matte);
            Detail(p, X(122.45), HL0 + 3.2, Z(16), X(122.5) - 0.004, HL0 + 4.4, Z(28), NPine, Matte);
            Finish(p);
        }
        Patch("L0", 122, 38, 156, 62, 0.011, 0.006, NDeal);
        {
            var p = k.Piece("L0 Reduced Sign", "The Reduced corner's sign: an amber board hung over the last stop before the trade arches.", NDeal);
            Solid(p, X(124), HL0, Z(61.6), X(124.2), HL0 + 2.4, Z(61.8), NChar, Matte);
            Solid(p, X(153.8), HL0, Z(61.6), X(154), HL0 + 2.4, Z(61.8), NChar, Matte);
            Solid(p, X(124), HL0 + 2.4, Z(61.55), X(154), HL0 + 3.6, Z(61.85), NDeal, Matte);
            Letters(p, "REDUCED", X(139), HL0 + 2.65, Z(61.85), 0.11, 'z', [0.004, 0.016], NChar, false);
            Finish(p);
        }
        {
            var arches = new List<(double C, double W, double H)>();
            var p = k.Piece("L0 Trade Arches", "Hexkey Orbital's trade arches: push your trolley through one and the trade is made, goods for goods.", NPine);
            for (var i = 0; i < 8; i++)
            {
                double c = 24 + i * 16 + 4.8;
                arches.Add((c, 3.4, 1.1));
                foreach (var (a0, a1) in new[] { (c - 1.7, c - 1.3), (c + 1.3, c + 1.7) })
                    Solid(p, X(a0), HL0, Z(70.1), X(a1), HL0 + 3.0, Z(70.5), NPine, Matte);
                Solid(p, X(c - 1.7), HL0 + 3.0, Z(70.1), X(c + 1.7), HL0 + 3.4, Z(70.5), NPine, Matte);
                foreach (var f in new[] { 70.096, 70.504 }) // the scanner's light strip under the lintel, both faces
                    Detail(p, X(c - 1.25), HL0 + 2.9, Z(f) - (f < 70.3 ? 0.008 : 0), X(c + 1.25), HL0 + 2.98, Z(f) + (f < 70.3 ? 0 : 0.008), NSignWhite, Matte, glow: true);
            }
            Finish(p);
            var b = k.Piece("L0 Checkout Barrier", "The low white barrier before the trade arches: the only way out is through an arch.", NWhite);
            WallRun(b, 'x', 70.3, 22.1, FW - ExtT, HL0, HL0 + 1.1, PartT, 0, arches.ToArray(), NWhite, Matte, trims: false);
            Finish(b);
        }
        {
            var p = k.Piece("L0 Ration Kiosk", "The ration kiosk after the trade arches: a birch counter, a menu board.", NBirch);
            Solid(p, X(24), HL0, Z(77), X(40), HL0 + 1.1, Z(78), NBirch, Matte);
            Solid(p, X(24), HL0, Z(74.2), X(40), HL0 + 2.4, Z(74.8), NWhite, Matte);
            Detail(p, X(26), HL0 + 2.6, Z(74.796), X(38), HL0 + 3.4, Z(74.84), NPine, Matte);
            Finish(p);
            var t = k.Piece("L0 Bag-up Tables", "Bag-up tables after the trade arches: birch tops where shoppers tie their loads down.", NBirch);
            for (var i = 0; i < 6; i++)
                Solid(t, X(72 + i * 11), HL0, Z(79), X(74 + i * 11), HL0 + 0.9, Z(80), NBirch, Matte);
            Finish(t);
        }
        // the loading lane's kerb lines, the stair zone and the lamps after the arches
        Patch("L0", 22.2, 86, 148, 86.15, 0.011, 0.006, NChar);
        for (var x = 30.0; x < 148; x += 12)
            Lamp(FloorAt("L0", x, 82), X(x) - 0.8, Ceil("L0") - 0.12, Z(82) - 0.08, X(x) + 0.8, Ceil("L0") - 0.012, Z(82) + 0.08);

        // ---- the roof pavilions, the name board
        Pavilion(k, "Arrivals", new Rect(0, 4, 24, 84), HRoof, HCap, 'e', [(25, 4, 3), (47, 4, 3), (69, 4, 3)]);
        Pavilion(k, "Departures", new Rect(136, 4, 160, 84), HRoof, HCap, 'w', [(25, 4, 3), (47, 4, 3), (69, 4, 3)]);
        // (two boards, one word each: a part holds at most 256 boxes)
        foreach (var (word, bx0, bx1) in new[] { ("HEXKEY", 56.0, 78.0), ("ORBITAL", 82.0, 106.0) })
        {
            var p = k.Piece($"Roof Name {(word == "HEXKEY" ? "West" : "East")}", "Hexkey Orbital's name on pine boards along the roof's north edge, lit, read from the approach.", NPine);
            const double bz = 1.2;
            for (var x = bx0 + 2; x <= bx1 - 2; x += 6)
                Solid(p, X(x) - 0.1, HRoof, Z(bz) - 0.1, X(x) + 0.1, HRoof + 0.8, Z(bz) + 0.1, NChar, Matte);
            Solid(p, X(bx0), HRoof + 0.8, Z(bz) - 0.06, X(bx1), HRoof + 5.0, Z(bz) + 0.06, NPine, Matte);
            Letters(p, word, X((bx0 + bx1) / 2), HRoof + 1.4, Z(bz), 0.42, 'x', [-0.078, 0.078], NSignWhite, true);
            Finish(p);
        }

        // ---- stairs
        StraightStair(k, "Arrival Stair", 'z', 26, 3, 4, HL2, HRoof, arrivalHole.Y0, -1);
        StraightStair(k, "Market Stair", 'x', 144, 86, 4, HL1, HL2, marketHole.X1, 1);
        StraightStair(k, "Warehouse Stair", 'x', 4, 91, 4, HL0, HL1, warehouseHole.X1, 1);

        // ---- lift booths (looks: the cars and the trolley lifts come with the trolley)
        foreach (var (lvl, bx, by, bw, bd) in new[]
                 {
                     ("Roof", 1.0, 6.0, 6.0, 6.0), ("L2", 1.0, 6.0, 6.0, 6.0),
                     ("L0", 150.0, 74.0, 6.0, 6.0), ("L0", 150.0, 84.0, 6.0, 6.0), ("Roof", 150.0, 74.0, 6.0, 6.0), ("Roof", 150.0, 62.0, 6.0, 6.0),
                 })
        {
            var y = Lvl(lvl);
            var p = FloorAt(lvl, bx + bw / 2, by + bd / 2);
            Detail(p, X(bx), y + 0.02, Z(by), X(bx + bw), y + 3.2, Z(by + bd), NGlassCol, GlassFin);
            foreach (var (sx, sy) in Corners)
            {
                double cx = sx < 0 ? bx : bx + bw, cy = sy < 0 ? by : by + bd;
                Detail(p, X(cx) - 0.08, y + 0.02, Z(cy) - 0.08, X(cx) + 0.08, y + 3.25, Z(cy) + 0.08, NChar, Matte);
            }
            Detail(p, X(bx) - 0.08, y + 3.25, Z(by) - 0.08, X(bx + bw) + 0.08, y + 3.4, Z(by + bd) + 0.08, NChar, Matte);
        }

        // ---- the route line (terracotta, with arrows), and the pier
        Route("L2", [(3, 26.6), (6.5, 26.6), (6.5, 16), (156, 16), (156, 40), (4, 40), (4, 64), (156, 64), (156, 86), (150.3, 86)]);
        Route("L1", [(143.7, 86), (140, 86), (140, 77), (16, 77), (16, 91), (13.9, 91)]);
        Route("L0", [(3.7, 91), (2.5, 91), (2.5, 65), (118.5, 65), (118.5, 12), (120.5, 12), (120.5, 64.5), (140.8, 64.5), (140.8, 90), (148, 90)]);
        Route("Roof", [(30, 25), (24.5, 25), (12, 25), (12, 16), (3, 16), (3, 18.3)]);
        Pier(k);

        // ---- gravity: a Gravity Generator in every grid cell of the warehouse floor (in a gap between racks), its field the cell, floor to roof
        for (var c = 0; c + 1 < ColX.Length; c++)
            for (var r = 0; r + 1 < RowZ.Length; r++)
            {
                double cx = (ColX[c] + ColX[c + 1]) / 2, cy = (RowZ[r] + RowZ[r + 1]) / 2, gx = cx;
                if (r < 3 && cx > 23 && cx < 118) // between racks: the middle of the nearest gap
                    gx = Enumerable.Range(0, 12).Select(i => 24 + i * 8 + 5.6).Append(21.6).OrderBy(g => Math.Abs(g - cx)).First();
                var p = FloorAt("L0", gx, cy);
                var tag = $"grav{c + 1}{r + 1}";
                Sock(p, tag, X(gx), HL0, Z(cy), "+y");
                k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag, "Gravity Generator", "-y",
                    new PyDict { ["sizeX"] = R(ColX[c + 1] - ColX[c] - 0.1), ["sizeY"] = 23.8, ["sizeZ"] = 23.9, ["atX"] = R(cx - gx), ["atY"] = 11.9 }, NChar, Matte));
            }

        foreach (var f in floorPieces)
            Finish(f.P);
        return k;
    }

    // 5 x 7 block letters for the store's signs (only the ones its words use)
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['X'] = ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['Y'] = ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
        ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['I'] = ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
        ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['C'] = ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['0'] = ["01110", "10011", "10101", "10101", "10101", "11001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00110", "01000", "10000", "11111"],
        ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
    };

    /// <summary>
    /// Block letters on a sign board: centred on (cx, y0 the baseline, cz), each cell <paramref name="cell"/> metres, running along x
    /// (on a board facing ±z, <paramref name="axis"/> 'x') or along... the board's plane is fixed by <paramref name="axis"/>: 'x' letters run
    /// along x on faces at cz plus each of <paramref name="faces"/>; 'z' the same on one face at cz + faces (a board facing +z).
    /// Each row's runs of lit cells are one box; a face at a negative offset reads mirrored from behind, so it is written backwards.
    /// </summary>
    private static void Letters(Part p, string text, double cx, double y0, double cz, double cell, char axis, double[] faces, string color, bool glow)
    {
        double adv = cell * 6, width = text.Length * adv - cell;
        foreach (var f in faces)
        {
            var back = f < 0;
            for (var ci = 0; ci < text.Length; ci++)
            {
                if (!Font.TryGetValue(text[ci], out var rows))
                    continue;
                for (var r = 0; r < 7; r++)
                {
                    var line = rows[r];
                    for (var col = 0; col < 5;)
                    {
                        if (line[col] != '1')
                        {
                            col++;
                            continue;
                        }
                        var end = col;
                        while (end + 1 < 5 && line[end + 1] == '1')
                            end++;
                        double u0 = -width / 2 + ci * adv + col * cell, u1 = -width / 2 + ci * adv + (end + 1) * cell;
                        if (back)
                            (u0, u1) = (-u1, -u0);
                        double h0 = y0 + (6 - r) * cell, h1 = h0 + cell;
                        double f0 = cz + f + (back ? -0.008 : 0), f1 = cz + f + (back ? 0 : 0.008);
                        Detail(p, cx + u0, h0, f0, cx + u1, h1, f1, color, Matte, glow);
                        col = end + 1;
                    }
                }
            }
        }
    }

    /// <summary>A roof pavilion: glass walls on a white sill, a white lid; doorways on one side onto the pads.</summary>
    private static void Pavilion(Kit k, string name, Rect r, double y0, double y1, char doorSide, (double, double, double)[] doors)
    {
        const double t = 0.2;
        var p = k.Piece(name + " Glass", $"The {name.ToLowerInvariant()} pavilion on Hexkey Orbital's roof: glass walls in thin charcoal frames.", NGlassCol);
        double lid = y1 - 0.25;
        WallRun(p, 'x', r.Y0 + t / 2, r.X0, r.X1, y0, lid, t, 0, [], NGlassCol, GlassFin);
        WallRun(p, 'x', r.Y1 - t / 2, r.X0, r.X1, y0, lid, t, 0, [], NGlassCol, GlassFin);
        WallRun(p, 'z', r.X0 + t / 2, r.Y0 + t, r.Y1 - t, y0, lid, t, 0, doorSide == 'w' ? doors : [], NGlassCol, GlassFin);
        WallRun(p, 'z', r.X1 - t / 2, r.Y0 + t, r.Y1 - t, y0, lid, t, 0, doorSide == 'e' ? doors : [], NGlassCol, GlassFin);
        // frames every 4 m, outside
        for (var u = r.Y0 + 4; u < r.Y1 - 1; u += 4)
            foreach (var x in new[] { r.X0 - 0.03, r.X1 + 0.006 })
                Detail(p, X(x), y0, Z(u) - 0.06, X(x) + 0.024, lid, Z(u) + 0.06, NChar, Matte);
        for (var u = r.X0 + 4; u < r.X1 - 1; u += 4)
            foreach (var y in new[] { r.Y0 - 0.03, r.Y1 + 0.006 })
                Detail(p, X(u) - 0.06, y0, Z(y), X(u) + 0.06, lid, Z(y) + 0.024, NChar, Matte);
        Finish(p);
        var lidP = k.Piece(name + " Roof", $"The white lid of the {name.ToLowerInvariant()} pavilion, a charcoal line round its edge.", NWhite);
        Solid(lidP, X(r.X0), lid, Z(r.Y0), X(r.X1), y1, Z(r.Y1), NWhite, Matte);
        Detail(lidP, X(r.X0) - 0.02, lid + 0.02, Z(r.Y0) - 0.02, X(r.X1) + 0.02, lid + 0.12, Z(r.Y1) + 0.02, NChar, Matte);
        // a pine board over the doors with the pavilion's word in white
        var (bx, word) = doorSide == 'e' ? (r.X1 + 0.03, "ARRIVALS") : (r.X0 - 0.03, "EXIT");
        double bz0 = (r.Y0 + r.Y1) / 2 - 9, bz1 = (r.Y0 + r.Y1) / 2 + 9;
        Detail(lidP, X(bx) - 0.012, y1 + 0.05, Z(bz0), X(bx) + 0.012, y1 + 1.6, Z(bz1), NPine, Matte);
        LettersZ(lidP, word, X(bx) + (doorSide == 'e' ? 0.012 : -0.012), y1 + 0.35, Z((bz0 + bz1) / 2), 0.16, doorSide == 'e' ? 1 : -1);
        Finish(lidP);
    }

    /// <summary>Block letters on a board facing ±x (dir), reading left to right for someone facing it.</summary>
    private static void LettersZ(Part p, string text, double fx, double y0, double cz, double cell, int dir)
    {
        double adv = cell * 6, width = text.Length * adv - cell;
        for (var ci = 0; ci < text.Length; ci++)
        {
            if (!Font.TryGetValue(text[ci], out var rows))
                continue;
            for (var r = 0; r < 7; r++)
                for (var col = 0; col < 5;)
                {
                    if (rows[r][col] != '1')
                    {
                        col++;
                        continue;
                    }
                    var end = col;
                    while (end + 1 < 5 && rows[r][end + 1] == '1')
                        end++;
                    // facing +x, left is +z (south)... for a reader looking -x, their left is +z: so u runs -z for dir +1
                    double u0 = -width / 2 + ci * adv + col * cell, u1 = -width / 2 + ci * adv + (end + 1) * cell;
                    double z0 = dir > 0 ? cz - u1 : cz + u0, z1 = dir > 0 ? cz - u0 : cz + u1;
                    double h0 = y0 + (6 - r) * cell;
                    double a = dir > 0 ? fx + 0.004 : fx - 0.012, b = a + 0.008;
                    Detail(p, a, h0, z0, b, h0 + cell, z1, NSignWhite, Matte, glow: true);
                    col = end + 1;
                }
        }
    }

    /// <summary>A straight stair rising along px ('x') or py ('z'), in the direction dir, from its foot at u0 (on y0) to y1, a lane w wide
    /// centred on the cross line c; its top tread runs on to the slab hole's far edge, so it meets the floor above.</summary>
    private static void StraightStair(Kit k, string name, char axis, double u0, double c, double w, double y0, double y1, double holeEdge, int dir)
    {
        var p = k.Piece(name, "A stair of Hexkey Orbital: birch treads on a charcoal stringer, wide enough for two.", NBirch);
        var steps = (int)Math.Round((y1 - y0) / Rise);
        void Tread(double a, double b, double h0, double h1)
        {
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            if (axis == 'x')
                Solid(p, X(lo), h0, Z(c - w / 2), X(hi), h1, Z(c + w / 2), NBirch, Matte);
            else
                Solid(p, X(c - w / 2), h0, Z(lo), X(c + w / 2), h1, Z(hi), NBirch, Matte);
        }
        for (var i = 0; i < steps; i++)
        {
            double top = y0 + Rise * (i + 1), a = u0 + dir * Run * i, b = i == steps - 1 ? holeEdge : u0 + dir * Run * (i + 1);
            Tread(a, b, Math.Max(y0, top - 0.3), top);
        }
        foreach (var s in new[] { -1, 1 })
        {
            double e = c + s * (w / 2 + 0.04), uEnd = u0 + dir * Run * steps;
            if (axis == 'x')
                Bar(p, [X(u0), y0 + 0.2, Z(e)], [X(uEnd), y1 + 0.2, Z(e)], 0.06, NChar, Matte);
            else
                Bar(p, [X(e), y0 + 0.2, Z(u0)], [X(e), y1 + 0.2, Z(uEnd)], 0.06, NChar, Matte);
        }
        Finish(p);
    }

    /// <summary>The freight pier east of the warehouse, at the Big-item pick-up: a deck 54 m out with a Docking Clamp Large on each
    /// side for a ship alongside.</summary>
    private static void Pier(Kit k)
    {
        const double pz0 = 8, pz1 = 29;
        foreach (var (name, x0, x1) in new[] { ("Pier 1", FW, FW + 27), ("Pier 2", FW + 27, FW + 54) })
        {
            var p = k.Piece(name, "Hexkey Orbital's freight pier: a white deck with a charcoal kerb, ships berthed along both sides.", NWhite);
            Solid(p, X(x0), -0.6, Z(pz0), X(x1), 0, Z(pz1), NWhite, Matte);
            Detail(p, X(x0), 0, Z(pz0), X(x1), 0.008, Z(pz1), NDeck, "painted deck plate");
            foreach (var (a, b) in new[] { (pz0, pz0 + 0.3), (pz1 - 0.3, pz1) })
                Detail(p, X(x0), 0.011, Z(a), X(x1), 0.25, Z(b), NChar, Matte);
            Lamp(p, X(x1) - 0.3, 0.011, Z(18) - 0.3, X(x1), 0.3, Z(19) + 0.3);
            if (name == "Pier 2")
                foreach (var (tag, py, sz) in new[] { ("berthN", pz0 + 0.6, -1), ("berthS", pz1 - 0.6, 1) })
                {
                    Sock(p, tag, X(FW + 30), 0, Z(py), "+y", Large);
                    var (sw, sl, sh) = Magpie;
                    k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag, "Docking Clamp Large", "-y", new PyDict
                    {
                        ["sizeX"] = Math.Min(96, sl + 6), ["sizeY"] = Math.Min(96, sh + 4), ["sizeZ"] = Math.Min(96, sw + 4),
                        ["atX"] = 0, ["atY"] = 2, ["atZ"] = R(sz * (sw / 2 + 3)), ["startOn"] = true,
                    }, NPine, Matte));
                }
            Finish(p);
        }
    }
}
