using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.StationLook;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The station kit's hexagonal blocks (Karl, 2026-11-18: "make the commons buildings hexagonal and varied"). A hex block has a corner
/// pointing east (x), so its north and south walls run along x and the other four are slanted. Its look is drawn with hulls (hex slabs,
/// mitred wall plates, windows on the faces); its collision is axis-aligned hidden boxes: each slab a staircase inside its hexagon, each
/// slanted wall a run of steps just inside its face, so pieces still meet face to face and weld. Rooms stay square, inside a square
/// pressure wall with the service gap between it and the hull.
/// </summary>
public sealed partial class SpaceStationParts
{
    private const double Sqrt3 = 1.7320508075688772, Cos30 = Sqrt3 / 2, WallW = 0.6;

    /// <summary>
    /// A hex block's options. Rooms: four room names a level (north, south, west, east of the core) when it has rooms; else Fit names the
    /// fittings of its open floors (hydro, bunks, stalls, beds, desks, tables, plaza). WinStyle: 's' a strip a deck, 'b' tall bands every
    /// other deck, 'v' vertical slits. PadR: a landing pad on the roof, that half-size.
    /// </summary>
    private sealed record HexOpts(string Job, int Seed, bool Decks = true, Func<int, string[]>? Rooms = null, string BandCol = StationLook.Band,
        string WinCol = Window, char WinStyle = 's', double CoreHalf = CoreH, string Fit = "", double PadR = 0, bool Antenna = false,
        bool Greebles = true, double Keep = 0);

    private static (double X, double Z) HexV(double cx, double cz, double r, int i)
    {
        var a = (i % 6) * Math.PI / 3;
        return (cx + r * Math.Cos(a), cz + r * Math.Sin(a));
    }

    private static (double X, double Z) HexN(int i)
    {
        var a = (30 + 60 * (i % 6)) * Math.PI / 180;
        return (Math.Cos(a), Math.Sin(a));
    }

    /// <summary>A hexagonal prism, circumradius r, drawn only.</summary>
    private static void HexPrism(Part p, double cx, double cz, double r, double y0, double y1, string color, string finish)
    {
        var pts = new List<double[]>();
        for (var i = 0; i < 6; i++)
        {
            var (x, z) = HexV(cx, cz, r, i);
            pts.Add([x, y0, z]);
            pts.Add([x, y1, z]);
        }
        Hull(p, pts, color, finish);
    }

    /// <summary>A plate on face i of the hexagon of circumradius r: u0 to u1 along the face (0 to 1), d0 to d1 out from it.</summary>
    private static void FacePanel(Part p, double cx, double cz, double r, int i, double u0, double u1, double d0, double d1, double y0, double y1, string color, string? finish, bool glow = false)
    {
        var (ax, az) = HexV(cx, cz, r, i);
        var (bx, bz) = HexV(cx, cz, r, i + 1);
        var (nx, nz) = HexN(i);
        var pts = new List<double[]>();
        foreach (var u in new[] { u0, u1 })
            foreach (var d in new[] { d0, d1 })
                foreach (var y in new[] { y0, y1 })
                    pts.Add([ax + (bx - ax) * u + nx * d, y, az + (bz - az) * u + nz * d]);
        Hull(p, pts, color, finish, glow);
    }

    /// <summary>A hull wall plate on face i, mitred at the corners (stopped 3 mm short of them, so neighbours never share the mitre's plane).</summary>
    private static void WallPlate(Part p, double cx, double cz, double r, int i, double y0, double y1, string color, string finish)
    {
        var ri = r - HullT / Cos30;
        var e = 0.003 / r;
        var pts = new List<double[]>();
        foreach (var rr in new[] { r, ri })
        {
            var (ax, az) = HexV(cx, cz, rr, i);
            var (bx, bz) = HexV(cx, cz, rr, i + 1);
            foreach (var u in new[] { e, 1 - e })
                foreach (var y in new[] { y0, y1 })
                    pts.Add([ax + (bx - ax) * u, y, az + (bz - az) * u]);
        }
        Hull(p, pts, color, finish);
    }

    /// <summary>Hidden boxes filling a hexagon of circumradius r as a staircase (inside it), with a square hole of half-size hole.</summary>
    private static void HexFill(Part p, double cx, double cz, double r, double y0, double y1, int m, double hole = 0)
    {
        var a = r * Cos30;
        if (hole <= 0)
            Hidden(p, cx - r / 2, y0, cz - a, cx + r / 2, y1, cz + a);
        else
        {
            Hidden(p, cx - r / 2, y0, cz - a, cx + r / 2, y1, cz - hole);
            Hidden(p, cx - r / 2, y0, cz + hole, cx + r / 2, y1, cz + a);
            Hidden(p, cx - r / 2, y0, cz - hole, cx - hole, y1, cz + hole);
            Hidden(p, cx + hole, y0, cz - hole, cx + r / 2, y1, cz + hole);
        }
        for (var k = 1; k < m; k++)
        {
            double xa = r / 2 + r / 2 * (k - 1) / m, xb = r / 2 + r / 2 * k / m, hz = a * (r - xb) / (r / 2);
            Hidden(p, cx + xa, y0, cz - hz, cx + xb, y1, cz + hz);
            Hidden(p, cx - xb, y0, cz - hz, cx - xa, y1, cz + hz);
        }
    }

    /// <summary>The steps the slanted walls collide with: n a face, each just inside the face; the two straight walls one box each.</summary>
    private static void HexWallSteps(Part p, double cx, double cz, double r, double y0, double y1, int n)
    {
        var a = r * Cos30;
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                for (var j = 0; j < n; j++)
                {
                    double z0 = a * j / n, z1 = a * (j + 1) / n, xo = r - z1 / Sqrt3;
                    Hidden(p, cx + sx * (xo - WallW), y0, cz + sz * z0, cx + sx * xo, y1, cz + sz * z1);
                }
        foreach (var sz in new[] { -1, 1 })
            Hidden(p, cx - r / 2, y0, cz + sz * (a - WallW), cx + r / 2, y1, cz + sz * a);
    }

    /// <summary>
    /// A hexagonal block centred at (cx, cz), circumradius r, from y0 to y1: a floor and a roof slab, the six walls as one piece, the decks
    /// (a piece a level when it has rooms, else up to five levels a piece) and the lift core. Returns the floor tops of its levels.
    /// </summary>
    private static List<double> HexBlock(Kit k, string mod, double cx, double cz, double r, double y0, double y1, HexOpts o)
    {
        var a = r * Cos30;
        var n = Math.Max(4, (int)Math.Ceiling(r / 1.5));
        var m = Math.Clamp((int)Math.Round(r / 3), 4, 8);
        var rin = r - WallW - r / (2 * n) - 0.05; // decks stay inside the wall steps
        var rDraw = r - HullT / Cos30 - 0.01;
        var levels = o.Decks ? Levels(y0, y1) : [y0 + Slab];
        double Top(int i) => i + 1 < levels.Count ? levels[i + 1] - DeckSlab : y1 - Slab;
        double wy0 = y0 + Slab, wy1 = y1 - Slab;
        var c = o.CoreHalf;
        var hi = Math.Round(0.634 * rin - 0.3, 1);
        var job = Cap(o.Job);

        void Inside(Part p, int i)
        {
            var f = levels[i];
            if (o.Rooms != null)
            {
                var nm = o.Rooms(i);
                Interior(p, f, Top(i), r, hi, [new(-hi, c + 2, hi, hi, nm[0], 's'), new(-hi, -hi, hi, -c - 2, nm[1], 'n'), new(-hi, -c - 2, -c - 2, c + 2, nm[2], 'e'), new(c + 2, -c - 2, hi, c + 2, nm[3], 'w')], o.Seed + i, grating: false);
            }
            else
                Fittings(p, cx, cz, rin, c, f, Top(i), o.Fit, o.Seed * 31 + i);
        }

        // the floor
        var floor = k.Piece($"{mod} Floor", $"{job}'s floor slab, a hexagon on girders, with the first level on it.");
        HexPrism(floor, cx, cz, r, y0, y0 + Slab, Plate2, Plated);
        HexFill(floor, cx, cz, r - 0.01, y0, y0 + Slab, m);
        for (var x = -r + 3; x < r - 2; x += 6)
        {
            var zh = (Math.Abs(x) <= r / 2 ? a : a * (r - Math.Abs(x) - 0.3) / (r / 2)) - 0.8;
            if (zh > 1)
                Detail(floor, cx + x - 0.3, y0 - 0.45, cz - zh, cx + x + 0.3, y0, cz + zh, Frame, Dirty);
        }
        for (var i = 0; i < 6; i++)
        {
            var (x, z) = HexV(cx, cz, r * 0.85, i);
            Lamp(floor, x - 0.2, y0 - 0.12, z - 0.2, x + 0.2, y0, z + 0.2);
        }
        Inside(floor, 0);
        Finish(floor);

        // the roof
        var roof = k.Piece($"{mod} Roof", $"{job}'s roof slab{(o.PadR > 0 ? ", a landing pad on it" : "")}, its parapet and nav lamps.");
        HexPrism(roof, cx, cz, r, y1 - Slab, y1, Plate2, Plated);
        HexFill(roof, cx, cz, r - 0.01, y1 - Slab, y1, m);
        for (var i = 0; i < 6; i++)
        {
            FacePanel(roof, cx, cz, r, i, 0.01, 0.99, -0.35, -0.02, y1, y1 + 0.4, Frame, Dirty);
            var (x, z) = HexV(cx, cz, r - 0.2, i);
            Detail(roof, x - 0.3, y1, z - 0.3, x + 0.3, y1 + 0.55, z + 0.3, Frame, Dirty);
            Lamp(roof, x - 0.15, y1 + 0.55, z - 0.15, x + 0.15, y1 + 0.8, z + 0.15, x < cx - 0.1 ? NavRed : NavGreen);
        }
        if (o.PadR > 0)
            Pad(k, roof, cx, y1, cz, o.PadR, "pad");
        if (o.Greebles)
            Greebles(roof, cx - r / 2, cx + r / 2, cz - a + 1.5, cz + a - 1.5, y1, Math.Max(o.Keep, o.PadR + 0.5), (int)Math.Clamp(r * 0.6, 4, 14), o.Seed + 7, cx, cz);
        if (o.Antenna)
        {
            var (x, z) = HexV(cx, cz, r * 0.62, 2);
            Cyl(roof, 'y', x, 0, z, 0.25, y1, y1 + 9, Frame, Steel);
            Cyl(roof, 'y', x, 0, z, 0.7, y1, y1 + 0.6, Plate2, Dirty);
            Lamp(roof, x - 0.15, y1 + 9, z - 0.15, x + 0.15, y1 + 9.3, z + 0.15, NavRed);
        }
        Finish(roof);

        // the walls: six mitred plates, ribs, a livery band, windows; the steps they collide with
        var walls = k.Piece($"{mod} Walls", $"{job}'s six hull walls: plate on ribs, the livery band, windows on every deck.");
        HexWallSteps(walls, cx, cz, r, wy0, wy1, n);
        var winLevels = levels.Where(f => f + 2.3 < wy1 - 1.7).ToList();
        var ribs = Math.Max(2, (int)Math.Round(r / 6));
        for (var i = 0; i < 6; i++)
        {
            WallPlate(walls, cx, cz, r, i, wy0, wy1, Plate, Painted);
            var (x, z) = HexV(cx, cz, r, i);
            Detail(walls, x - 0.35, wy0 + 0.005, z - 0.35, x + 0.35, wy1 - 0.005, z + 0.35, Frame, Dirty);
            FacePanel(walls, cx, cz, r, i, 0.03, 0.97, 0, 0.03, wy1 - 1.6, wy1 - 0.6, o.BandCol, Painted);
            for (var j = 1; j <= ribs; j++)
            {
                var u = (double)j / (ribs + 1);
                FacePanel(walls, cx, cz, r, i, u - 0.35 / r, u + 0.35 / r, 0, 0.32, wy0 + 0.3, wy1 - 0.2, Plate2, Dirty);
            }
            switch (o.WinStyle)
            {
                case 'v':
                    foreach (var u in new[] { 0.22, 0.5, 0.78 })
                        FacePanel(walls, cx, cz, r, i, u - 0.5 / r, u + 0.5 / r, 0, 0.026, wy0 + 1.2, wy1 - 2.2, o.WinCol, null, glow: true);
                    break;
                case 'b':
                    for (var li = 0; li < winLevels.Count; li += 2)
                        FacePanel(walls, cx, cz, r, i, 0.08, 0.92, 0, 0.026, winLevels[li] + 0.8, winLevels[li] + 2.6, o.WinCol, null, glow: true);
                    break;
                default:
                    foreach (var f in winLevels)
                        FacePanel(walls, cx, cz, r, i, 0.1, 0.9, 0, 0.026, f + 1.35, f + 2.05, o.WinCol, null, glow: true);
                    break;
            }
        }
        Finish(walls);

        // the decks
        void DeckSlabAt(Part p, double f)
        {
            HexPrism(p, cx, cz, rDraw, f - DeckSlab, f, Plate2, Plated);
            HexFill(p, cx, cz, rin, f - DeckSlab, f, Math.Min(m, 4), c);
        }
        if (o.Rooms != null)
            for (var i = 1; i < levels.Count; i++)
            {
                var deck = k.Piece($"{mod} Deck {i + 1}", $"{job}'s deck {i + 1}: a hex slab round the lift core, the pressure wall and its rooms.");
                DeckSlabAt(deck, levels[i]);
                Inside(deck, i);
                Finish(deck);
            }
        else
            for (var i0 = 1; i0 < levels.Count; i0 += 5)
            {
                var i1 = Math.Min(levels.Count - 1, i0 + 4);
                var deck = k.Piece(i1 > i0 ? $"{mod} Decks {i0 + 1}-{i1 + 1}" : $"{mod} Deck {i0 + 1}", $"{job}'s decks {i0 + 1} to {i1 + 1}: hex slabs round the lift core, open floors and their fittings.");
                for (var i = i0; i <= i1; i++)
                {
                    DeckSlabAt(deck, levels[i]);
                    Inside(deck, i);
                }
                Finish(deck);
            }

        // the lift core
        var core = k.Piece($"{mod} Core", $"{job}'s lift core, a door on every level.", Core);
        Solid(core, cx - c, wy0, cz - c, cx + c, wy1, cz + c, Core, Painted);
        foreach (var f in levels)
            foreach (var s in new[] { -1, 1 })
            {
                var z = cz + s * c;
                Detail(core, cx - 1.4, f, z, cx + 1.4, f + 2.6, z + s * 0.05, Hazard, HazardFin);
                Detail(core, cx - 1.1, f, z, cx + 1.1, f + 2.3, z + s * 0.08, Plate3, Painted);
                Lamp(core, cx - 0.8, f + 2.75, z, cx + 0.8, f + 2.9, z + s * 0.06);
            }
        Finish(core);
        return levels;
    }

    /// <summary>An open floor's fittings on a grid inside the hexagon, kept off the core: planters, bunk pods, stalls, beds, desks, tables.</summary>
    private static void Fittings(Part p, double cx, double cz, double rin, double c, double f, double top, string kind, int seed)
    {
        var s = 0.6 * rin - 0.6;
        var step = Math.Max(2.8, s / 4);
        var n = 0;
        for (var x = -s; x <= s - 1.6; x += step)
            for (var z = -s; z <= s - 1.6; z += step)
            {
                n++;
                if (x + 2.2 > -c - 1.2 && x < c + 1.2 && z + 2.2 > -c - 1.2 && z < c + 1.2)
                    continue;
                if (Rnd(seed, n) < 0.25)
                    continue;
                double X = cx + x, Z = cz + z;
                switch (kind)
                {
                    case "hydro":
                        Detail(p, X, f, Z, X + 2.0, f + 0.8, Z + 1.0, Plate2, Dirty);
                        Detail(p, X + 0.1, f + 0.8, Z + 0.1, X + 1.9, f + 1.15, Z + 0.9, "#4f9a3c", Painted);
                        Lamp(p, X + 0.2, top - 0.08, Z + 0.3, X + 1.8, top, Z + 0.7, "#e8b0ff");
                        break;
                    case "bunks":
                        Detail(p, X, f, Z, X + 2.1, f + 2.2, Z + 1.1, Plate3, Painted);
                        Lamp(p, X + 2.1, f + 1.5, Z + 0.3, X + 2.13, f + 1.8, Z + 0.8);
                        break;
                    case "stalls":
                        Detail(p, X, f, Z, X + 2.0, f + 1.0, Z + 0.7, new[] { Container1, Container2, Container3, Container4 }[(int)(Rnd(seed, n + 50) * 4)], Worn);
                        Detail(p, X - 0.1, f + 2.3, Z - 0.1, X + 2.1, f + 2.4, Z + 0.8, Frame, Dirty);
                        break;
                    case "beds":
                        Detail(p, X, f, Z, X + 2.0, f + 0.65, Z + 0.95, "#e4e6e2", Painted);
                        Lamp(p, X + 0.6, top - 0.06, Z + 0.2, X + 1.4, top, Z + 0.75, "#d8f0ff");
                        break;
                    case "desks":
                        Detail(p, X, f, Z, X + 1.6, f + 0.75, Z + 0.8, Plate2, Painted);
                        Lamp(p, X + 0.4, f + 0.75, Z + 0.2, X + 1.2, f + 1.2, Z + 0.25, Window);
                        break;
                    case "tables":
                        Detail(p, X, f, Z, X + 1.2, f + 0.75, Z + 1.2, Frame, Dirty);
                        if (Rnd(seed, n + 90) < 0.5)
                            Lamp(p, X + 0.4, top - 0.1, Z + 0.4, X + 0.8, top, Z + 0.8, "#ff7ab8");
                        break;
                    case "plaza":
                        Detail(p, X, f, Z, X + 2.2, f + 0.45, Z + 0.6, Plate3, Painted);
                        Lamp(p, X + 0.7, top - 0.08, Z - 0.4, X + 1.5, top, Z + 0.4);
                        break;
                }
            }
        // lamps round the core
        foreach (var (sx, sz) in Corners)
            Lamp(p, cx + sx * (c + 1.2) - 0.4, top - 0.06, cz + sz * (c + 1.2) - 0.4, cx + sx * (c + 1.2) + 0.4, top, cz + sz * (c + 1.2) + 0.4);
    }

    /// <summary>A landing pad on a roof at (px, y, pz), half-size r: a hazard frame on tread plate, lamps, a docking hatch and a clamp.</summary>
    private static void Pad(Kit k, Part p, double px, double y, double pz, double r, string tag)
    {
        const double b = 0.6;
        Detail(p, px - r, y, pz - r, px + r, y + 0.03, pz - r + b, Hazard, HazardFin);
        Detail(p, px - r, y, pz + r - b, px + r, y + 0.03, pz + r, Hazard, HazardFin);
        Detail(p, px - r, y, pz - r + b, px - r + b, y + 0.03, pz + r - b, Hazard, HazardFin);
        Detail(p, px + r - b, y, pz - r + b, px + r, y + 0.03, pz + r - b, Hazard, HazardFin);
        Detail(p, px - r + b, y, pz - r + b, px + r - b, y + 0.02, pz + r - b, HazardDark, Tread);
        foreach (var (sx, sz) in Corners)
            Lamp(p, px + sx * (r - 0.3) - 0.2, y + 0.03, pz + sz * (r - 0.3) - 0.2, px + sx * (r - 0.3) + 0.2, y + 0.2, pz + sz * (r - 0.3) + 0.2);
        Sock(p, tag + "hatch", px, y, pz + 0.69, "+y");
        Sock(p, tag + "clamp", px + 3.5, y, pz - 3.5, "+y", Medium);
        k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag + "hatch", "Docking Hatch", "base", null, null, null));
        k.Hangs.Add(new HangSpec((string)p.D["name"]!, tag + "clamp", "Docking Clamp Medium", "-y",
            new PyDict { ["sizeX"] = 14, ["sizeY"] = 8, ["sizeZ"] = 14, ["atY"] = 4.5, ["atX"] = -3.5, ["atZ"] = 3.5 }, Hazard, HazardFin));
    }

    /// <summary>A sky bridge from one hex block's face to another's, at height y: a plated tube with lit strips, drawn only.</summary>
    private static void SkyBridge(Part p, double ax, double az, double bx, double bz, double y)
    {
        double dx = bx - ax, dz = bz - az, l = Math.Sqrt(dx * dx + dz * dz);
        double px = -dz / l, pz = dx / l;
        Bar(p, [ax, y, az], [bx, y, bz], 2.6, Plate, Painted);
        foreach (var s in new[] { -1, 1 })
            Bar(p, [ax + dx / l * 0.4 + s * px * 1.32, y + 0.2, az + dz / l * 0.4 + s * pz * 1.32], [bx - dx / l * 0.4 + s * px * 1.32, y + 0.2, bz - dz / l * 0.4 + s * pz * 1.32], 0.3, Window, Painted, glow: true);
        Bar(p, [ax + dx / l * 0.3, y - 1.45, az + dz / l * 0.3], [bx - dx / l * 0.3, y - 1.45, bz - dz / l * 0.3], 0.4, Frame, Dirty);
    }
}
