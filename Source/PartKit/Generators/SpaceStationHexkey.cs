using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.StationLook;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Hexkey Orbital, the Traders' flat-pack store in orbit (Karl, 2026-11-20; layout draft 4, the project's megastore/layout.html,
/// notes in docs/sandbox/constructs/station.md "Hexkey Orbital"). Phase 1, the empty shell: a 144 x 72 m box of four levels (L0 the
/// double-height warehouse 0 to 8 m, L1 the market 8 to 12, L2 the showrooms 12 to 16, the roof at 16) with fifteen 20 m landing pads
/// on the roof between the arrivals and departures pavilions, the showroom loop's walls and doorways, the stairs between floors, a
/// freight pier east of the warehouse with two ship berths, and a Gravity Generator over every 24 m cell.
/// <para>
/// Plan metres: px east from the west wall, py south from the north wall (the layout's drawings); <see cref="X"/> and <see cref="Z"/>
/// put them in the station frame (x east, z south, y up, the origin the middle of the warehouse floor). The look is "Nordic in
/// space" (Karl): white panels, thin charcoal lines, birch floors in the showrooms, one pine green and a terracotta route line.
/// </para>
/// </summary>
public sealed partial class SpaceStationParts
{
    public const string HexkeyName = "Hexkey Orbital";

    // ---- the Nordic palette (layout draft 4)
    private const string NWhite = "#eceeea", NBirch = "#d6bf96", NAsh = "#b5b9b7", NDeck = "#8e9290", NFelt = "#9fa4a1", NChar = "#2b2e30",
        NPine = "#3e6b5a", NRoute = "#b8643f", NGlassCol = "#c4d8d5", NPad = "#e4e6e2";

    private const string Matte = "painted plating", GlassFin = "glass";

    private const double FW = 144, FD = 72; // footprint, plan metres
    private const double ExtT = 0.3, PartT = 0.2, Rise = 0.25, Run = 0.3;

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
    /// doorway on both faces.
    /// </summary>
    private static void WallRun(Part p, char axis, double line, double u0, double u1, double y0, double y1, double t, double off,
        (double C, double W, double H)[] doors, string color, string fin)
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
            Trim(d0 - 0.12, d0, y0, y0 + h + 0.12);
            Trim(d1, d1 + 0.12, y0, y0 + h + 0.12);
            Trim(d0, d1, y0 + h, y0 + h + 0.12);
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
        const double L0 = 0, L1 = 8, L2 = 12, Roof = 16, Cap = 20;
        const string floorDesc = "A floor of Hexkey Orbital, the Traders' flat-pack store: a white slab, its floor laid on top.";

        // ---- the holes in the slabs: the stairs between floors
        var arrivalHole = new Rect(1.5, 21, 7.4, 24); // roof, over the arrival stair down to L2
        var downHole = new Rect(1.5, 66, 7.4, 69); // L2, over the stair down to L1
        var marketHole = new Rect(121.4, 16, 132.2, 19); // L1, over the stair down to the warehouse
        var well = new Rect(134.8, 62.5, 139.2, 71.5); // L1, L2, roof: the exit stair tower's well
        var holes = new Dictionary<string, Rect[]>
        {
            ["L0"] = [],
            ["L1"] = [marketHole, well],
            ["L2"] = [downHole, well],
            ["Roof"] = [arrivalHole, well],
        };

        // ---- floors: L0 to L2 in 24 m cells; the roof by pad columns
        var floorPieces = new List<(string Level, Rect R, Part P)>();
        foreach (var (lvl, top, thick, skin, fin) in new[] { ("L0", L0, 0.6, NDeck, "painted deck plate"), ("L1", L1, 0.3, NFelt, Matte), ("L2", L2, 0.3, NBirch, Matte) })
            for (var c = 0; c < 6; c++)
                for (var r = 0; r < 3; r++)
                {
                    var rect = new Rect(c * 24, r * 24, c * 24 + 24, r * 24 + 24);
                    floorPieces.Add((lvl, rect, Floor(k, $"{lvl} Floor {c + 1}-{r + 1}", rect, top, thick, skin, fin, holes[lvl], floorDesc)));
                }
        double[] roofCols = [0, 16, 39, 61, 83, 105, 127, 144], roofRows = [0, 25, 47, 72];
        for (var c = 0; c + 1 < roofCols.Length; c++)
            for (var r = 0; r + 1 < roofRows.Length; r++)
            {
                var rect = new Rect(roofCols[c], roofRows[r], roofCols[c + 1], roofRows[r + 1]);
                floorPieces.Add(("Roof", rect, Floor(k, $"Roof {c + 1}-{r + 1}", rect, Roof, 0.3, NAsh, Matte, holes["Roof"],
                    "The roof of Hexkey Orbital: landing pads for Shuttles and Limpets on a grey deck, walkways between them.")));
            }
        Part FloorAt(string lvl, double px, double py) => floorPieces.First(f => f.Level == lvl && f.R.Has(px, py)).P;

        // ---- the fifteen pads (a flush Docking Hatch and a Docking Clamp Medium each, as Caledon Highport's)
        var n = 0;
        for (var c = 0; c < 5; c++)
            for (var r = 0; r < 3; r++)
            {
                n++;
                double x0 = 18 + c * 22, y0 = 4 + r * 22, cx = x0 + 10, cy = y0 + 10;
                var p = FloorAt("Roof", cx, cy);
                Detail(p, X(x0), Roof + 0.011, Z(y0), X(x0 + 20), Roof + 0.016, Z(y0 + 20), NPad, Matte);
                foreach (var (a, b, d, e) in new[] { (x0, x0 + 20, y0, y0 + 0.4), (x0, x0 + 20, y0 + 19.6, y0 + 20), (x0, x0 + 0.4, y0 + 0.4, y0 + 19.6), (x0 + 19.6, x0 + 20, y0 + 0.4, y0 + 19.6) })
                    Detail(p, X(a), Roof + 0.019, Z(d), X(b), Roof + 0.025, Z(e), NPine, Matte);
                // the H, and the pad's number in bars by its corner (one bar a pad in its column's row)
                Detail(p, X(cx - 2.2), Roof + 0.019, Z(cy - 2.5), X(cx - 1.6), Roof + 0.025, Z(cy + 2.5), NPine, Matte);
                Detail(p, X(cx + 1.6), Roof + 0.019, Z(cy - 2.5), X(cx + 2.2), Roof + 0.025, Z(cy + 2.5), NPine, Matte);
                Detail(p, X(cx - 1.6), Roof + 0.019, Z(cy - 0.3), X(cx + 1.6), Roof + 0.025, Z(cy + 0.3), NPine, Matte);
                foreach (var (sx, sy) in Corners)
                    Lamp(p, X(cx + sx * 9.5) - 0.15, Roof + 0.019, Z(cy + sy * 9.5) - 0.15, X(cx + sx * 9.5) + 0.15, Roof + 0.12, Z(cy + sy * 9.5) + 0.15);
                var tag = $"pad{n}";
                Sock(p, tag + "hatch", X(cx), Roof, Z(cy) + 0.69, "+y");
                Sock(p, tag + "clamp", X(cx) + 3.5, Roof, Z(cy) - 3.5, "+y", Medium);
                k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag + "hatch", "Docking Hatch", "base", null, null, null));
                k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag + "clamp", "Docking Clamp Medium", "-y",
                    new PyDict { ["startOn"] = true, ["sizeX"] = 18, ["sizeY"] = 8, ["sizeZ"] = 18, ["atY"] = 4.5, ["atX"] = -3.5, ["atZ"] = 3.5 }, NPine, Matte));
            }

        // ---- outer walls, every level, in 24 m runs
        foreach (var (lvl, y0, y1, w0, w1, top) in new[] { ("L0", L0, L1 - 0.3, 5.0, 7.0, false), ("L1", L1, L2 - 0.3, 9.0, 11.0, false), ("L2", L2, Roof - 0.3, 13.0, 15.0, true) })
        {
            for (var c = 0; c < 6; c++)
                foreach (var (side, line, off) in new[] { ('n', 0.0, ExtT / 2), ('s', FD, -ExtT / 2) })
                {
                    var p = k.Piece($"{lvl} Wall {char.ToUpperInvariant(side)}{c + 1}", "An outer wall of Hexkey Orbital: white panels, a long window band, thin charcoal mullions.", NWhite);
                    WallRun(p, 'x', line, c * 24, c * 24 + 24, y0, y1, ExtT, off, [], NWhite, Matte);
                    OuterLook(p, side, c * 24, c * 24 + 24, y0, y1, w0, w1, top);
                    Finish(p);
                }
            for (var r = 0; r < 3; r++)
                foreach (var (side, line, off) in new[] { ('w', 0.0, ExtT / 2), ('e', FW, -ExtT / 2) })
                {
                    double u0 = Math.Max(ExtT, r * 24), u1 = Math.Min(FD - ExtT, r * 24 + 24);
                    var p = k.Piece($"{lvl} Wall {char.ToUpperInvariant(side)}{r + 1}", "An outer wall of Hexkey Orbital: white panels, a long window band, thin charcoal mullions.", NWhite);
                    // the warehouse's east wall opens onto the freight pier
                    (double, double, double)[] doors = lvl == "L0" && side == 'e' && r == 2 ? [(60, 12, 6)] : [];
                    WallRun(p, 'z', line, u0, u1, y0, y1, ExtT, off, doors, NWhite, Matte);
                    OuterLook(p, side, u0, u1, y0, y1, w0, w1, top);
                    Finish(p);
                }
        }

        // ---- inside walls: (level, name, axis, line, from, to, doorways)
        const double Door = 3, Cut_ = 1.5;
        (string Lvl, string Name, char Ax, double Line, double U0, double U1, (double, double, double)[] Doors)[] walls =
        [
            // L2: the showroom loop (north row R1-R4 east, the quick aisle, south row R5-R8 west), shortcut doors into the aisle
            ("L2", "Aisle Wall N", 'x', 27, 30.1, FW - ExtT, [(43.5, Cut_, 2.5), (72.75, Cut_, 2.5), (101.25, Cut_, 2.5), (129, Door, Door)]),
            ("L2", "Aisle Wall S", 'x', 45, ExtT, FW - ExtT, [(118.5, Door, Door), (91.5, Cut_, 2.5), (64.5, Cut_, 2.5), (37.5, Cut_, 2.5)]),
            ("L2", "Entry Wall", 'z', 30, ExtT, 44.9, [(13.5, Door, Door)]),
            ("L2", "Room Wall 1", 'z', 58.5, ExtT, 26.9, [(13.5, Door, Door)]),
            ("L2", "Room Wall 2", 'z', 87, ExtT, 26.9, [(13.5, Door, Door)]),
            ("L2", "Room Wall 3", 'z', 115.5, ExtT, 26.9, [(13.5, Door, Door)]),
            ("L2", "Room Wall 5", 'z', 105, 45.1, FD - ExtT, [(58.5, Door, Door)]),
            ("L2", "Room Wall 6", 'z', 78, 45.1, FD - ExtT, [(58.5, Door, Door)]),
            ("L2", "Room Wall 7", 'z', 51, 45.1, FD - ExtT, [(58.5, Door, Door)]),
            ("L2", "Room Wall 8", 'z', 24, 45.1, FD - ExtT, [(58.5, Door, Door)]),
            ("L2", "Exit Wall", 'z', 132, 45.1, FD - ExtT, []),
            // L1: the staff rooms, the canteen, the exit tower sealed off
            ("L1", "Staff Wall S", 'x', 45, ExtT, 24.1, []),
            ("L1", "Staff Wall E", 'z', 24, ExtT, 44.9, [(38, 1.2, 2.4)]),
            ("L1", "Canteen Wall N", 'x', 45, 99, FW - ExtT, []),
            ("L1", "Canteen Wall W", 'z', 99, 45.1, FD - ExtT, [(52, Door, Door)]),
            ("L1", "Exit Wall", 'z', 132, 45.1, FD - ExtT, []),
            // L0: the loading lane behind the checkout, the exit hall
            ("L0", "Lane Wall", 'x', 49.5, 24, 120, []),
            ("L0", "Exit Wall N", 'x', 45, 120, FW - ExtT, []),
            ("L0", "Exit Wall W", 'z', 120, 45.1, 49.4, []),
        ];
        foreach (var (lvl, name, ax, line, u0, u1, doors) in walls)
        {
            double y0 = lvl switch { "L0" => L0, "L1" => L1, _ => L2 }, y1 = (lvl switch { "L0" => L1, "L1" => L2, _ => Roof }) - 0.3;
            var p = k.Piece($"{lvl} {name}", "A partition of Hexkey Orbital: white, with charcoal-framed doorways.", NWhite);
            WallRun(p, ax, line, u0, u1, y0, y1, PartT, 0, doors, NWhite, Matte);
            Finish(p);
        }

        // ---- the roof pavilions: glass boxes with a white lid
        Pavilion(k, "Arrivals", new Rect(0, 10, 16, 62), Roof, Cap, 'e', [(15, 4, 3), (36, 4, 3), (57, 4, 3)]);
        Pavilion(k, "Departures", new Rect(128, 40, 144, 72), Roof, Cap, 'w', [(44, 4, 3), (58, 4, 3)]);

        // ---- stairs
        StraightStair(k, "Arrival Stair", 2, 22.5, 3, L2, Roof, arrivalHole.X1);
        StraightStair(k, "Market Stair", 2, 67.5, 3, L1, L2, downHole.X1);
        StraightStair(k, "Warehouse Stair", 122, 17.5, 3, L0, L1, marketHole.X1);
        ExitTower(k, well, L0, Roof);

        // ---- lift booths (looks: the cars come in a later phase)
        foreach (var (lvl, bx, by, bw, bd, y) in new[]
                 {
                     ("Roof", 6.0, 27.0, 6.0, 6.0, Roof), ("Roof", 6.0, 39.0, 6.0, 6.0, Roof), ("L2", 6.0, 27.0, 6.0, 6.0, L2), ("L2", 6.0, 39.0, 6.0, 5.6, L2),
                     ("L2", 3.0, 54.0, 7.5, 9.0, L2), ("L2", 13.5, 54.0, 7.5, 9.0, L2), ("L1", 3.0, 54.0, 7.5, 9.0, L1), ("L1", 13.5, 54.0, 7.5, 9.0, L1),
                     ("L1", 123.0, 4.0, 7.5, 9.0, L1), ("L1", 133.5, 4.0, 7.5, 9.0, L1), ("L0", 123.0, 4.0, 7.5, 9.0, L0), ("L0", 133.5, 4.0, 7.5, 9.0, L0),
                     ("L0", 135.0, 47.0, 6.0, 6.0, L0), ("L0", 135.0, 55.0, 6.0, 6.0, L0), ("Roof", 135.0, 47.0, 6.0, 6.0, Roof), ("Roof", 135.0, 55.0, 6.0, 6.0, Roof),
                 })
        {
            var p = FloorAt(lvl, bx + bw / 2, by + bd / 2);
            Detail(p, X(bx), y + 0.02, Z(by), X(bx + bw), y + 3.2, Z(by + bd), NGlassCol, GlassFin);
            foreach (var (sx, sy) in Corners)
            {
                double cx = sx < 0 ? bx : bx + bw, cy = sy < 0 ? by : by + bd;
                Detail(p, X(cx) - 0.08, y + 0.02, Z(cy) - 0.08, X(cx) + 0.08, y + 3.25, Z(cy) + 0.08, NChar, Matte);
            }
            Detail(p, X(bx) - 0.08, y + 3.25, Z(by) - 0.08, X(bx + bw) + 0.08, y + 3.4, Z(by + bd) + 0.08, NChar, Matte);
        }

        // ---- the route line on the floors (terracotta, thin), and the pier
        RouteLine(FloorAt, "L2", L2, [(9, 36), (9, 13.5), (129, 13.5), (129, 36), (118.5, 36), (118.5, 58.5), (12, 58.5)]);
        RouteLine(FloorAt, "L1", L1, [(12, 58.5), (28.5, 58.5), (28.5, 7.5), (58.5, 7.5), (58.5, 66), (88.5, 66), (88.5, 7.5), (115.5, 7.5), (115.5, 30), (128, 30), (128, 21)]);
        RouteLine(FloorAt, "L0", L0, [(130, 22.5), (130, 27), (114, 27), (114, 45), (28.5, 45), (28.5, 15), (15, 15), (15, 30), (13.5, 30), (13.5, 60), (129, 60)]);
        Pier(k);

        // ---- gravity: a Gravity Generator in the middle of every 24 m cell of the warehouse floor, its box the cell up to 24 m
        for (var c = 0; c < 6; c++)
            for (var r = 0; r < 3; r++)
            {
                double cx = c * 24 + 12, cy = r * 24 + 12;
                if (r == 2 && c == 5)
                    cx = 128; // (clear of the exit tower)
                var p = FloorAt("L0", cx, cy);
                var tag = $"grav{c + 1}{r + 1}";
                Sock(p, tag, X(cx), L0, Z(cy), "+y");
                k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag, "Gravity Generator", "-y",
                    new PyDict { ["sizeX"] = 24, ["sizeY"] = 24, ["sizeZ"] = 24, ["atX"] = R(c * 24 + 12 - cx) }, NChar, Matte));
            }

        foreach (var f in floorPieces)
            Finish(f.P);
        return k;
    }

    private static void RouteLine(Func<string, double, double, Part> floorAt, string lvl, double y, (double X, double Y)[] pts)
    {
        for (var i = 0; i + 1 < pts.Length; i++)
        {
            var (a, b) = (pts[i], pts[i + 1]);
            double x0 = Math.Min(a.X, b.X) - 0.075, x1 = Math.Max(a.X, b.X) + 0.075, y0 = Math.Min(a.Y, b.Y) - 0.075, y1 = Math.Max(a.Y, b.Y) + 0.075;
            // one strip per 24 m cell it crosses, on that cell's floor piece
            for (var cx = Math.Floor(x0 / 24) * 24; cx < x1; cx += 24)
                for (var cy = Math.Floor(y0 / 24) * 24; cy < y1; cy += 24)
                {
                    double sx0 = Math.Max(x0, cx), sx1 = Math.Min(x1, cx + 24), sy0 = Math.Max(y0, cy), sy1 = Math.Min(y1, cy + 24);
                    if (sx1 - sx0 < 0.05 || sy1 - sy0 < 0.05)
                        continue;
                    var p = floorAt(lvl, (sx0 + sx1) / 2, (sy0 + sy1) / 2);
                    var lift = 0.011 + (i % 2) * 0.003; // (crossing strips at their corners don't share a plane)
                    Detail(p, X(sx0), y + lift, Z(sy0), X(sx1), y + lift + 0.006, Z(sy1), NRoute, Matte);
                }
        }
    }

    /// <summary>A roof pavilion: glass walls on a white sill, a white lid; doorways on one side onto the pads.</summary>
    private static void Pavilion(Kit k, string name, Rect r, double y0, double y1, char doorSide, (double, double, double)[] doors)
    {
        const double t = 0.2;
        var p = k.Piece(name + " Glass", $"The {name.ToLowerInvariant()} pavilion on Hexkey Orbital's roof: glass walls in thin charcoal frames.", NGlassCol);
        double lid = y1 - 0.25;
        // north and south walls run the full width; west and east between them
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
        // the sign: the store's name in pine on a white board over the doors (letters in a later phase)
        if (doorSide == 'e')
            Detail(lidP, X(r.X1) + 0.02, y1 + 0.05, Z(r.Y0 + 14), X(r.X1) + 0.3, y1 + 2.2, Z(r.Y1 - 14), NPine, Matte);
        else
            Detail(lidP, X(r.X0) - 0.3, y1 + 0.05, Z(r.Y0 + 8), X(r.X0) - 0.02, y1 + 2.2, Z(r.Y1 - 8), NPine, Matte);
        Finish(lidP);
    }

    /// <summary>A straight stair rising east along px from (px0, its floor y0) to y1, a lane w wide centred on py; its top tread runs
    /// on to the slab hole's east edge, so it meets the floor above.</summary>
    private static void StraightStair(Kit k, string name, double px0, double py, double w, double y0, double y1, double holeEnd)
    {
        var p = k.Piece(name, "A stair of Hexkey Orbital: birch treads on a charcoal stringer.", NBirch);
        var steps = (int)Math.Round((y1 - y0) / Rise);
        for (var i = 0; i < steps; i++)
        {
            double top = y0 + Rise * (i + 1), x0 = px0 + Run * i, x1 = i == steps - 1 ? holeEnd : px0 + Run * (i + 1);
            Solid(p, X(x0), Math.Max(y0, top - 0.3), Z(py - w / 2), X(x1), top, Z(py + w / 2), NBirch, Matte);
        }
        // stringers both sides (drawn only)
        foreach (var s in new[] { -1, 1 })
            Bar(p, [X(px0), y0 + 0.2, Z(py + s * (w / 2 + 0.04))], [X(px0 + Run * steps), y1 + 0.2, Z(py + s * (w / 2 + 0.04))], 0.06, NChar, Matte);
        Finish(p);
    }

    /// <summary>The exit tower: half-turn flights 2 m a flight up the well from the warehouse to the roof, a landing at each turn.</summary>
    private static void ExitTower(Kit k, Rect well, double y0, double y1)
    {
        const double flight = 2.4; // 8 treads of 0.3
        double w0 = well.X0, e0 = w0 + flight, e1 = e0 + 2, laneA0 = well.Y0, laneA1 = well.Y0 + 3, laneB0 = well.Y1 - 3, laneB1 = well.Y1;
        for (double y = y0, s = 1; y < y1 - 0.1; y += 4, s++)
        {
            var p = k.Piece($"Exit Stair {s}", "The exit stair of Hexkey Orbital: birch flights round a well, from the loading lane to the roof.", NBirch);
            for (var i = 0; i < 8; i++)
            {
                double top = y + Rise * (i + 1);
                Solid(p, X(w0 + Run * i), Math.Max(y, top - 0.3), Z(laneA0), X(w0 + Run * (i + 1)), top, Z(laneA1), NBirch, Matte);
            }
            Solid(p, X(e0), y + 1.7, Z(laneA0), X(e1), y + 2, Z(laneB1), NWhite, Matte); // the turn
            for (var j = 0; j < 8; j++)
            {
                double top = y + 2 + Rise * (j + 1);
                Solid(p, X(e0 - Run * (j + 1)), top - 0.3, Z(laneB0), X(e0 - Run * j), top, Z(laneB1), NBirch, Matte);
            }
            if (y + 4 < 8 - 0.01)
                Solid(p, X(w0 - 2), y + 3.7, Z(laneA0), X(w0), y + 4, Z(laneB1), NWhite, Matte); // a landing where no floor is (inside the warehouse's height)
            Finish(p);
        }
    }

    /// <summary>The freight pier east of the warehouse: a deck 54 m out with a Docking Clamp Large on each side for a ship alongside.</summary>
    private static void Pier(Kit k)
    {
        foreach (var (name, x0, x1) in new[] { ("Pier 1", FW, FW + 27), ("Pier 2", FW + 27, FW + 54) })
        {
            var p = k.Piece(name, "Hexkey Orbital's freight pier: a white deck with a charcoal kerb, ships berthed along both sides.", NWhite);
            Solid(p, X(x0), -0.6, Z(48), X(x1), 0, Z(69), NWhite, Matte);
            Detail(p, X(x0), 0, Z(48), X(x1), 0.008, Z(69), NDeck, "painted deck plate");
            foreach (var (a, b) in new[] { (48.0, 48.3), (68.7, 69.0) })
                Detail(p, X(x0), 0.011, Z(a), X(x1), 0.25, Z(b), NChar, Matte);
            Lamp(p, X(x1) - 0.3, 0.011, Z(58) - 0.3, X(x1), 0.3, Z(59) + 0.3);
            if (name == "Pier 2")
                foreach (var (tag, py, sz) in new[] { ("berthN", 48.6, -1), ("berthS", 68.4, 1) })
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
