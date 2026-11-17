using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The rust bucket (Karl, 2026-11-14: "a start in a space ship. You have a rust bucket, it has a full interior including small
/// bedroom, bathroom, hydroponics, engine room, cockpit, storage room"). Writes Constructs/Parts/Ship and the Rust Bucket blueprint
/// (docs/sandbox/constructs/ship.md).
///
/// The second hull (Karl, 2026-11-15: the first was a blob, "look at the Zero Day and why it is so good"): a lofted hull of five
/// facets a side through seven rings from a blunt bridge front over the cargo ramp to a tail behind the rooms, about 24 m
/// long, with chine rails along every edge, three hoop frames, bevelled armour slabs and mismatched patch plates panel by panel,
/// fuel lines down the flanks, and things that do a job on the outside: radiator wings, deck cargo, a crane, a mast, the engine
/// pods and saddle tanks. The rooms are the first hull's (the opening's wake points and every fitting's socket still hold).
///
/// It is built of pieces welded together (the ship-design skill, 1b): each storey is drawn whole as before, then its boxes are
/// dealt out to the floor, the hull runs (lower and upper, port and starboard, fore and aft of the midship frame), the chin, the
/// canopy, the sterns, the tail, the three frames and each partition. A hit takes a panel, not a deck.
///
/// The generator works in ship metres (Sx: x from the port side, y from the lower floor's underside, z from the bow back; the bow
/// is -z) and moves them by half a cell; every structural piece has its origin at the body's (the keel's cell 0).
/// </summary>
public sealed class ShipParts : IPartGenerator
{
    public string Name => "ship_parts";

    private const string Out = "Constructs/Parts/Ship";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const double H = 0.125; // half a cell: the body's origin is the keel's cell 0's middle
    private const double T = 0.04; // a facet's thickness
    private const double Cx = 5.0; // the middle line

    // The hull's colours: a rust bucket is patched with whatever was to hand. Base: the tan paint; trim: the dark plate and frames;
    // accent (under a tenth): hazard ochre, primer red, the lamps.
    private const string HullPaint = "#7a6a58", Patch = "#5d7468", Patch2 = "#8c7b52", Rust = "#6e3f26", Belly = "#3b3530",
        RoofDark = "#4b4743", Stripe = "#b08a2e", Frame = "#2c2a28", Armour = "#46423c", Armour2 = "#5a554c", Primer = "#7a3324",
        Wall = "#9a9484", WallLow = "#5f665f", BathWall = "#c9ccc4", Ceiling = "#8f8a7e", Floor = "#5e6264", Deck = "#4a4e50",
        GlassTint = "#7fa6c0", Grow = "#4f8a3a", Leaf = "#6fb04a", LampGlow = "#f2e6c0";

    // Storey heights (Sx metres).
    private const double F0 = 0.25; // lower floor's top
    private const double F1 = 3.0, F1T = 3.25; // upper floor's underside and top
    private const double Ceil = 6.0, CeilT = 6.06; // the upper ceiling's underside and top

    /// <summary>The hull's rings: half width, height and z of its five points a side (belly edge, chine, beam at the deck line, upper
    /// chine, spine edge). Each point has its own z, so a facet can rake. Ring 0 is the bridge's blunt front (Karl, 2026-11-15: the chisel had to go), over the cargo ramp:
    /// its belly and chine points are the overhang's underside, so the lower facets start at ring 1.</summary>
    private static readonly (double W, double Y, double Z)[][] Rings =
    [
        [(1.65, 2.7, -2.3), (1.65, 2.7, -2.3), (1.95, 3.5, -2.7), (1.75, 5.45, -2.2), (1.05, 6.05, -1.6)], // 0 the bridge's blunt front
        [(0.9, -0.6, 0.9), (1.8, 0.0, -0.3), (2.0, 3.0, -1.5), (1.75, 6.0, 1.4), (1.1, 6.8, 2.3)], // 1 the nose: the ramp is its chin
        [(2.7, -0.6, 3.6), (4.8, 0.0, 3.0), (5.3, 3.0, 3.0), (4.95, 6.2, 3.2), (2.6, 7.3, 4.0)], // 2 the shoulder (frame)
        [(2.9, -0.6, 8.5), (4.8, 0.0, 8.5), (5.3, 3.0, 8.5), (4.95, 6.2, 8.5), (2.7, 7.3, 8.5)], // 3 midship (frame)
        [(2.9, -0.6, 13.8), (4.8, 0.0, 14.0), (5.3, 3.0, 14.0), (4.95, 6.2, 14.0), (2.7, 7.3, 13.8)], // 4 the haunch (frame)
        [(2.3, -0.4, 16.06), (4.3, 0.1, 16.06), (4.75, 3.0, 16.06), (4.45, 6.1, 16.06), (2.5, 7.1, 16.06)], // 5 behind the rooms
        [(1.5, 0.0, 18.2), (3.0, 0.5, 18.5), (3.4, 3.0, 18.7), (3.1, 5.6, 18.6), (1.7, 6.5, 18.3)], // 6 the tail
    ];

    private const int B = 0, L = 1, D = 2, U = 3, R = 4; // a ring's points
    private static readonly double[] FrameZ = [3.0, 8.5, 14.0]; // the hoop frames (rings 2, 3, 4)
    private static readonly string[] FrameNames = ["Fore", "Mid", "Aft"];

    // Where people stand (Karl, 2026-11-16: the rooms are what you'd expect inside a ship, a box standing in its own hull as the
    // Zero Day's do, not rooms filling the shell). Aft of the cockpit the rooms have their own walls at these half widths (Lw the
    // hold and the engine room, Uw the upper deck), with a gap of a metre or more out to the skin; hidden voids fill the gap so a
    // round still stops at the skin. The cockpit keeps the whole nose (InHigh), so the canopy's windows are its walls, and the hold
    // has a straight bay at its front, as wide as the ramp lets it be (no walls stepping in with the nose). A socket on the hull's inside is on those walls' faces.
    private const double InLow = 4.63, InHigh = 4.73;
    private const double Lw = 3.75, Uw = 3.5, WallT = 0.06;
    private const double CockpitBack = 4.125; // (the cockpit's back partition's aft face: the upper rooms' walls start here)

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    private static double[] Pt(int ring, int k, int side)
    {
        var (w, y, z) = Rings[ring][k];
        return P(Cx + side * w, y, z);
    }

    // ---- geometry helpers

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Add(double[] a, double[] b, double s = 1) => [a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s];

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return [a[0] / l, a[1] / l, a[2] / l];
    }

    private static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    private static double[] Mid(double[] a, double[] b) => Lerp(a, b, 0.5);

    // ---- what piece a box goes to: drawing tags each box with the piece it belongs to (Tag), "~hull" for a hull run that the
    // splitter places by where it stands, nothing for the storey's own piece (its floor, or the roof).

    private const string HullTag = "~hull";
    private static string? Tag;
    private static readonly Dictionary<PyDict, string> Tags = new(ReferenceEqualityComparer.Instance);

    private static void With(string? tag, Action a)
    {
        var was = Tag;
        Tag = tag;
        a();
        Tag = was;
    }

    private static void Put(Part p, PyDict b)
    {
        if (Tag != null)
            Tags[b] = Tag;
        p.Add(b);
    }

    /// <summary>A flat plate through the points (a convex polygon), T thick about them: a "hull" shape, drawn only.</summary>
    private static void Plate(Part p, double[][] pts, string color, string finish = "rusty paint", bool glass = false, bool paint = false, bool glow = false, double thick = T)
    {
        double[]? n = null;
        for (var i = 2; i < pts.Length && n == null; i++)
        {
            var c = Cross(Sub(pts[1], pts[0]), Sub(pts[i], pts[0]));
            if (Dot(c, c) > 1e-8)
                n = Unit(c);
        }
        if (n == null)
            return;
        var both = new List<double[]>();
        foreach (var q in pts)
            foreach (var s in new[] { -1, 1 })
                both.Add([.. Enumerable.Range(0, 3).Select(k => Py.Round(q[k] + s * n[k] * thick / 2, 4))]);
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()));
        if (!paint)
            shape["paintable"] = false;
        if (!glass && !glow)
            shape["finish"] = finish;
        if (glow)
            shape["glow"] = true;
        Put(p, Box(mn, mx, color, detail: true, glass: glass, shape: shape));
    }

    /// <summary>A quad as one plate when flat, else as two triangles.</summary>
    private static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "rusty paint", bool glass = false, bool paint = false)
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        var flat = Dot(n, n) < 1e-10 || Math.Abs(Dot(Unit(n), Sub(d, a))) < 0.002;
        if (flat)
            Plate(p, [a, b, c, d], color, finish, glass, paint);
        else
        {
            Plate(p, [a, b, c], color, finish, glass, paint);
            Plate(p, [a, c, d], color, finish, glass, paint);
        }
    }

    /// <summary>A convex solid between two outlines (drawn only).</summary>
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish, bool glow = false, bool paint = false)
    {
        var both = a.Concat(b).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()));
        if (!paint)
            shape["paintable"] = false;
        if (glow)
            shape["glow"] = true;
        else
            shape["finish"] = finish;
        Put(p, Box(mn, mx, color, detail: true, shape: shape));
    }

    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null)
    {
        var shape = finish == null ? null : Kw(("finish", finish));
        Put(p, Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, shape: shape));
    }

    private static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false)
    {
        var shape = finish == null && !glow ? null : new PyDict();
        if (finish != null)
            shape!["finish"] = finish;
        if (glow)
            shape!["glow"] = true;
        Put(p, Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, detail: true, shape: shape));
    }

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1) =>
        Put(p, Box(P(Math.Min(x0, x1), y0, Math.Min(z0, z1)), P(Math.Max(x0, x1), y1, Math.Max(z0, z1)), hidden: true));

    /// <summary>A plain socket on a face (Sx metres).</summary>
    private static void Sock(Part p, string name, double x, double y, double z, string dir) => p.Sock(name, P(x, y, z), dir);

    private static double Cm(double v) => Math.Floor(v * 100 + 1e-6) / 100;

    // ---- the skin as numbers: where it is at a height and a z (for the rooms, the voids and what hangs on the outside)

    /// <summary>Point k's half width and height at z, along its own rings (the belly and chine start at the nose ring).</summary>
    private static (double W, double Y) PointAt(int k, double z)
    {
        var r0 = k <= L ? 1 : 0;
        if (z <= Rings[r0][k].Z)
            return (Rings[r0][k].W, Rings[r0][k].Y);
        for (var r = r0; r < Rings.Length - 1; r++)
        {
            var (w0, y0, z0) = Rings[r][k];
            var (w1, y1, z1) = Rings[r + 1][k];
            if (z <= z1)
            {
                var t = z1 - z0 < 1e-9 ? 1 : (z - z0) / (z1 - z0);
                return (w0 + (w1 - w0) * t, y0 + (y1 - y0) * t);
            }
        }
        var (w, y, _) = Rings[^1][k];
        return (w, y);
    }

    /// <summary>The skin's half width at height y and z (0 above or below it).</summary>
    private static double SkinHalf(double z, double y)
    {
        var q = Enumerable.Range(0, 5).Select(k => PointAt(k, z)).ToArray();
        if (y < q[0].Y - 1e-6 || y > q[4].Y + 1e-6)
            return 0;
        for (var i = 0; i < 4; i++)
            if (y <= q[i + 1].Y)
                return q[i + 1].Y - q[i].Y < 1e-9 ? Math.Max(q[i].W, q[i + 1].W) : q[i].W + (q[i + 1].W - q[i].W) * (y - q[i].Y) / (q[i + 1].Y - q[i].Y);
        return q[4].W;
    }

    /// <summary>The least skin half width over a box's z and y range (sampled at its corners, its middle and the profile's kinks).</summary>
    private static double SkinMin(double z0, double z1, double y0, double y1)
    {
        var m = double.MaxValue;
        foreach (var z in new[] { z0, (z0 + z1) / 2, z1 })
        {
            var ys = new List<double> { y0, y1 };
            for (var k = 0; k < 5; k++)
            {
                var y = PointAt(k, z).Y;
                if (y > y0 && y < y1)
                    ys.Add(y);
            }
            foreach (var y in ys)
                m = Math.Min(m, SkinHalf(z, y));
        }
        return m;
    }

    private static readonly (double Y0, double Y1, double In)[] Levels = [(0.0, F1, InLow), (F1T, CeilT, InHigh)];

    /// <summary>A storey's room half width over z0..z1: its wall line, less where the skin comes in.</summary>
    /// <summary>The hold's front (Karl, 2026-11-16: no floor or walls stepping in with the nose) is a straight bay as wide as the
    /// ramp's mouth lets it be, from the ramp to <see cref="HoldWallFront"/>, where a flat wall steps out to the hold's own.</summary>
    private static double RoomHalf(int level, double z0, double z1)
    {
        var raw = SkinHalfIn(level, z0, z1);
        return level == 0 && raw < Lw - 1e-6 ? BayHalf : raw;
    }

    private static double SkinHalfIn(int level, double z0, double z1)
    {
        var (y0, y1, _) = Levels[level];
        var inner = level == 0 ? Lw : z1 <= CockpitBack + 1e-6 ? InHigh : Uw;
        return Cm(Math.Min(inner, SkinMin(z0, z1, y0, y1) - 0.08));
    }

    /// <summary>The ramp bay's half width: the narrowest the nose allows at the ramp, less room for the wall's void.</summary>
    private static double BayHalf => Cm(Strips(LowFront, Back, RoomCuts(0)).Min(t => SkinHalfIn(0, t.Z0, t.Z1)) - 0.04);

    /// <summary>Whether a strip of a storey has the rooms' own wall (a room box in the hull), not the skin, at its sides: the whole
    /// lower deck (the hold, its ramp bay and the engine room), and the upper deck aft of the cockpit.</summary>
    private static bool BoxWalled(int level, double z0, double z1) => level == 0 || z0 >= CockpitBack - 1e-6;

    /// <summary>Where the hold's own walls start: the first strip aft of the nose wide enough for them.</summary>
    private static double HoldWallFront => Strips(LowFront, Back, RoomCuts(0)).First(t => SkinHalfIn(0, t.Z0, t.Z1) >= Lw - 1e-6).Z0;

    /// <summary>A storey's strip cuts: the stair's opening, and upstairs the partitions across the rooms (where a wall's paint changes).</summary>
    private static double[] RoomCuts(int level) => level == 0 ? [.. StairCuts, 12.0] : [.. StairCuts, CockpitBack, 8.0, 12.0];

    /// <summary>Strips along z from a to b: cut at every ring, at each frame's edges, at <paramref name="cuts"/>, and every half metre
    /// where the hull narrows (fore of the shoulder, aft of the haunch).</summary>
    private static List<(double Z0, double Z1)> Strips(double a, double b, params double[] cuts)
    {
        var zs = new SortedSet<double> { a, b };
        void AddZ(double z)
        {
            z = Math.Round(z, 3);
            if (z > a + 0.02 && z < b - 0.02)
                zs.Add(z);
        }
        foreach (var ring in Rings)
            foreach (var q in ring)
                AddZ(q.Z);
        foreach (var f in FrameZ)
        {
            AddZ(f - 0.1);
            AddZ(f + 0.1);
        }
        foreach (var c in cuts)
            AddZ(c);
        for (var z = Math.Ceiling(a * 2) / 2; z < 3.0; z += 0.5)
            AddZ(z);
        for (var z = 14.5; z < b; z += 0.5)
            AddZ(z);
        // (no sliver strips: a cut within 5 cm of another is dropped, the frames' edges kept)
        var l = zs.ToList();
        var o = new List<double> { l[0] };
        for (var i = 1; i < l.Count; i++)
        {
            var keep = FrameZ.Any(f => Math.Abs(l[i] - f + 0.1) < 1e-6 || Math.Abs(l[i] - f - 0.1) < 1e-6) || i == l.Count - 1;
            if (l[i] - o[^1] < 0.05 && !keep)
                continue;
            if (l[i] - o[^1] < 0.05 && keep && o.Count > 1)
                o.RemoveAt(o.Count - 1);
            o.Add(l[i]);
        }
        return [.. Enumerable.Range(0, o.Count - 1).Select(i => (o[i], o[i + 1]))];
    }

    private const double LowFront = -0.3, UpFront = 1.4, Back = 16.06; // the rooms' ends

    private static readonly double[] StairCuts = [8.25, 11.75];

    /// <summary>A storey's floor (the lower one to its walls' inner faces, the upper one out to the deck line), less the holes.</summary>
    private static void FloorOf(Part p, int level, double y0, double y1, string color, (double X0, double Z0, double X1, double Z1)[] holes)
    {
        var front = level == 0 ? LowFront : Rings[1][D].Z;
        foreach (var (z0, z1) in Strips(front, Back, [.. holes.SelectMany(h => new[] { h.Z0, h.Z1 }), .. RoomCuts(level)]))
        {
            // (where the rooms are a box, the floor stops under their walls, upstairs over the hold's, and the gap to the skin is open;
            // the cockpit's runs out to the skin, and the ramp bay to its walls)
            var w = BoxWalled(level, z0, z1) ? (level == 0 ? RoomHalf(0, z0, z1) : Lw) + WallT : level == 0 ? RoomHalf(0, z0, z1) : Cm(SkinMin(z0, z1, y0, y1) - 0.03);
            double xa = Cx - w, xb = Cx + w;
            var xs = new List<(double, double)> { (xa, xb) };
            foreach (var h in holes)
                if (z0 >= h.Z0 - 1e-6 && z1 <= h.Z1 + 1e-6)
                    xs = [.. xs.SelectMany(r => new[] { (r.Item1, Math.Min(r.Item2, h.X0)), (Math.Max(r.Item1, h.X1), r.Item2) }).Where(r => r.Item2 - r.Item1 > 0.05)];
            foreach (var (a, b) in xs)
                Solid(p, a, y0, z0, b, y1, z1, color, "tread plate");
        }
    }

    /// <summary>The walls round a storey. Where the rooms are a box in the hull, each side gets the room's own wall (painted plate,
    /// a dark kick strip along its foot), one run per hull piece, and hidden voids from its back out to the skin; where they hug the
    /// skin (the cockpit), only the hidden wall from the room's face out to the skin. A cap at the back (and the
    /// front, upstairs). They are hull runs: the splitter deals them to the hull pieces and the frames.</summary>
    private static void HullWalls(Part p, int level)
    {
        var (y0, y1, _) = Levels[level];
        var front = level == 0 ? LowFront : UpFront;
        var wallTop = level == 0 ? F1 : CeilT;
        var runs = new List<(double Z0, double Z1, double In, string Color, string Finish, int Side)>();
        With(HullTag, () =>
        {
            foreach (var (z0, z1) in Strips(front, Back, RoomCuts(level)))
            {
                var inner = RoomHalf(level, z0, z1);
                var boxed = BoxWalled(level, z0, z1);
                var back = boxed ? inner + WallT : inner;
                var outer = Math.Max(back + 0.04, Cm(SkinMin(z0, z1, y0, y1) - 0.06));
                foreach (var s in new[] { -1, 1 })
                {
                    if (boxed && Zone(z0, z1) >= 10) // (at a hoop frame the gap holds the frame's rib, wall to skin, seen in a cutaway)
                        Solid(p, Cx + s * back, y0, z0, Cx + s * outer, y1, z1, Frame, "dirty steel");
                    else
                        Hidden(p, Cx + s * back, y0, z0, Cx + s * outer, y1, z1);
                    if (boxed && level == 1 && outer > Lw + WallT + 0.04) // (the floor's edge, over the hold's walls, out to the skin)
                        Hidden(p, Cx + s * (Lw + WallT), F1, z0, Cx + s * outer, F1T, z1);
                    if (!boxed)
                        continue;
                    var bath = level == 1 && s > 0 && z0 >= CockpitBack - 1e-6 && z1 <= 8.0 + 1e-6;
                    var (c, f) = bath ? (BathWall, "gloss paint") : (Wall, "painted plating");
                    if (runs.Count > 0 && runs[^1] is var r && r.Side == s && Math.Abs(r.Z1 - z0) < 1e-6 && Math.Abs(r.In - inner) < 1e-6 && r.Color == c
                        && Zone(r.Z0, r.Z1) == Zone(z0, z1))
                        runs[^1] = r with { Z1 = z1 };
                    else
                        runs.Add((z0, z1, inner, c, f, s));
                }
            }
            foreach (var (z0, z1, inner, c, f, s) in runs)
            {
                var face = Cx + s * inner;
                Solid(p, face, level == 0 ? F0 : y0, z0, face + s * WallT, wallTop, z1, c, f);
                // (the kick strip 4 to 16 mm proud of the wall, its ends 4 mm short of the strip's: no face in a neighbour's plane)
                Detail(p, face - s * 0.004, (level == 0 ? F0 : y0) + 0.006, z0 + 0.004, face - s * 0.016, (level == 0 ? F0 : y0) + 0.896, z1 - 0.004, WallLow, "matte paint");
            }
            var bw = Cm(SkinMin(Back, Back + 0.1, y0, y1) - 0.06);
            Hidden(p, Cx - bw, y0, Back, Cx + bw, y1, Back + 0.1);
            if (level == 1)
            {
                var fw = Cm(SkinMin(UpFront - 0.04, UpFront, y0, y1) - 0.06);
                Hidden(p, Cx - fw, y0, UpFront - 0.04, Cx + fw, y1, UpFront);
            }
            else
            {
                // The ramp bay's aft end: where the hold's own walls start, a flat plate each side from the bay's wall (3 mm into it,
                // no shared face) out to the skin.
                var z = HoldWallFront;
                var bi = BayHalf + 0.003;
                foreach (var s in new[] { -1, 1 })
                {
                    double OutAt(double y) => Math.Max(Lw + WallT, SkinHalf(z, y) - 0.03);
                    // (4 mm inside the bay wall's top and foot, which it overlaps)
                    double ya = F0 + 0.004, yb = F1 - 0.004;
                    double[][] Tri(double zz) => [P(Cx + s * bi, ya, zz), P(Cx + s * OutAt(ya), ya, zz), P(Cx + s * OutAt(yb), yb, zz), P(Cx + s * bi, yb, zz)];
                    Prism(p, Tri(z - 0.06), Tri(z - 0.004), Wall, "painted plating");
                }
            }
        });
    }

    /// <summary>The piece a stretch of hull wall at z0..z1 goes to (as Where deals them): a frame's band, else a hull run.</summary>
    private static int Zone(double z0, double z1)
    {
        for (var i = 0; i < FrameZ.Length; i++)
            if (z0 >= FrameZ[i] - 0.105 && z1 <= FrameZ[i] + 0.105)
                return 10 + i;
        var zc = (z0 + z1) / 2;
        return zc < 2.95 ? 0 : zc > 14.05 ? 3 : zc < 8.5 ? 1 : 2;
    }

    /// <summary>Hidden boxes filling the skin between z0 and z1 over a band of heights, in strips (no room in it: the nose, the roof,
    /// the belly, the tail).</summary>
    private static void Void(Part p, double z0, double z1, double y0, double y1, double minHalf = 0.25)
    {
        foreach (var (a, b) in Strips(z0, z1))
        {
            var w = Cm(SkinMin(a, b, y0, y1) - 0.06);
            if (w >= minHalf)
                Hidden(p, Cx - w, y0, a, Cx + w, y1, b);
        }
    }

    // ---- the skin as plates: facets between rings, chine rails along the edges, slabs and strips on the facets

    /// <summary>The point at (u along the band from ring r to r+1, v across facet k from point k to k+1) on side s.</summary>
    private static double[] OnFacet(int r, int k, int s, double u, double v) =>
        Lerp(Lerp(Pt(r, k, s), Pt(r, k + 1, s), v), Lerp(Pt(r + 1, k, s), Pt(r + 1, k + 1, s), v), u);

    private static double[] FacetNormal(int r, int k, int s)
    {
        var n = Unit(Cross(Sub(OnFacet(r, k, s, 0.5, 1), OnFacet(r, k, s, 0.5, 0)), Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5))));
        var c = OnFacet(r, k, s, 0.5, 0.5);
        double[] axis = [Cx - H, 3.0 - H, c[2]];
        return Dot(n, Sub(c, axis)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>Facet k of the band from ring r to r+1, side s. The odd facets sit 6 mm aft of the even ones and each stops 1.5 mm
    /// short of its band's ends, so where neighbours' plates cross their ends never share a plane.</summary>
    private static void Facet(Part p, int r, int k, int s, string color, string finish = "rusty paint", bool glass = false, bool paint = true, double u0 = 0, double u1 = 1)
    {
        var dz = k % 2 == 1 ? 0.006 : 0;
        double[] Q(double u, double v)
        {
            var q = OnFacet(r, k, s, u, v);
            return [q[0], q[1], q[2] + dz + (u < 0.5 ? 0.0015 : -0.0015)];
        }
        Quad(p, Q(u0, 0), Q(u0, 1), Q(u1, 1), Q(u1, 0), color, finish, glass, paint);
    }

    /// <summary>A bevelled slab on facet k (band r, side s) over (u0..u1, v0..v1), standing d0..d1 out, its face drawn in by bev.</summary>
    private static void Slab(Part p, int r, int k, int s, double u0, double u1, double v0, double v1, double d0, double d1, double bev, string color, string finish = "plated steel", bool paint = false)
    {
        var n = FacetNormal(r, k, s);
        var lu = Math.Sqrt(Dot(Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5)), Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5))));
        var lv = Math.Sqrt(Dot(Sub(OnFacet(r, k, s, 0.5, 1), OnFacet(r, k, s, 0.5, 0)), Sub(OnFacet(r, k, s, 0.5, 1), OnFacet(r, k, s, 0.5, 0))));
        double bu = bev / Math.Max(lu, 0.1), bv = bev / Math.Max(lv, 0.1);
        double[][] inner = [.. new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) }.Select(t => Add(OnFacet(r, k, s, t.Item1, t.Item2), n, d0))];
        double[][] outer = [.. new[] { (u0 + bu, v0 + bv), (u1 - bu, v0 + bv), (u1 - bu, v1 - bv), (u0 + bu, v1 - bv) }.Select(t => Add(OnFacet(r, k, s, t.Item1, t.Item2), n, d1))];
        Prism(p, inner, outer, color, finish, paint: paint);
    }

    /// <summary>A flat strip on a facet, d out from it: a seam, a stripe, a streak of rust, a lamp's lens.</summary>
    private static void FStrip(Part p, int r, int k, int s, double u0, double u1, double v0, double v1, double d, string color, string finish = "worn paint", bool glow = false)
    {
        var n = FacetNormal(r, k, s);
        double[] Q(double u, double v) => Add(OnFacet(r, k, s, u, v), n, d);
        Plate(p, [Q(u0, v0), Q(u1, v0), Q(u1, v1), Q(u0, v1)], color, finish, glow: glow);
    }

    /// <summary>A chine rail along point k (1..4) on side s from ring r to r+1: a faceted bar w wide each way and h proud, its ends
    /// a few mm inside the band so the next band's rail never shares their plane.</summary>
    private static void Chine(Part p, int r, int k, int s, double w, double h, string color)
    {
        double[][] Section(int ring, double inset)
        {
            var z = Rings[ring][k].Z + inset;
            var (cw, cy) = PointAt(k, z);
            var (aw, ay) = PointAt(k - 1, z);
            double[] c = [Cx + s * cw, cy], a = [Cx + s * aw, ay];
            double[] b2;
            if (k == R)
                b2 = [Cx - s * cw, cy];
            else
            {
                var (bw, by) = PointAt(k + 1, z);
                b2 = [Cx + s * bw, by];
            }
            double[] da = U2(a[0] - c[0], a[1] - c[1]), db = U2(b2[0] - c[0], b2[1] - c[1]);
            var n = U2(-(da[0] + db[0]), -(da[1] + db[1]));
            double[] At(double[] d1, double t1, double t2) => P(c[0] + d1[0] * t1 + n[0] * t2, c[1] + d1[1] * t1 + n[1] * t2, z);
            return [At(da, w, 0.01), At(da, w * 0.45, h), At(db, w * 0.45, h), At(db, w, 0.01)];
        }
        Prism(p, Section(r, 0.006), Section(r + 1, -0.006), color, "plated steel");
    }

    private static double[] U2(double x, double y)
    {
        var l = Math.Sqrt(x * x + y * y);
        return l < 1e-9 ? [0, 1] : [x / l, y / l];
    }

    /// <summary>The panels on facet k of band r, side s (v0..v1 across it): about 2.7 m long each, each one of a rust bucket's
    /// repairs: a dark armour slab, a stepped double slab, a mismatched patch plate, two half slabs at different heights, or bare
    /// paint with a seam, a rust streak and a primer scuff. salt varies the pattern band to band and side to side.</summary>
    private static void Plates(Part p, int r, int k, int s, int salt, double v0 = 0.07, double v1 = 0.93)
    {
        var lu = Math.Sqrt(Dot(Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5)), Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5))));
        var n = Math.Max(1, (int)Math.Round(lu / 2.7));
        var gap = 0.05 / lu;
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + gap, u1 = (double)(i + 1) / n - gap;
            var pick = ((k * 7 + i * 3 + salt * 5 + r * 2 + (s > 0 ? 1 : 0)) % 7 + 7) % 7;
            switch (pick)
            {
                case 0:
                case 3:
                    Slab(p, r, k, s, u0, u1, v0, v1, 0.02, 0.12, 0.06, Armour);
                    break;
                case 1:
                    Slab(p, r, k, s, u0, u1, v0, v1, 0.02, 0.1, 0.06, Armour2);
                    Slab(p, r, k, s, u0 + (u1 - u0) * 0.15, u1 - (u1 - u0) * 0.1, v0 + (v1 - v0) * 0.2, v1 - (v1 - v0) * 0.15, 0.1, 0.18, 0.04, Armour);
                    break;
                case 2:
                    Slab(p, r, k, s, u0 + 0.02, u1 - 0.06, v0 + 0.04, v1 - 0.1, 0.02, 0.06, 0.03, (i + salt) % 2 == 0 ? Patch : Patch2, "rusty paint", paint: true);
                    FStrip(p, r, k, s, u0 + 0.1, u0 + 0.16, v0, v0 + (v1 - v0) * 0.4, 0.025, Rust, "rust");
                    break;
                case 4:
                    Slab(p, r, k, s, u0, (u0 + u1) / 2 - 0.01, v0, v1, 0.02, 0.14, 0.07, Armour);
                    Slab(p, r, k, s, (u0 + u1) / 2 + 0.01, u1, v0 + (v1 - v0) * 0.25, v1, 0.02, 0.09, 0.05, Armour2);
                    break;
                case 5:
                    Slab(p, r, k, s, u0, u1, v0, (v0 + v1) / 2 - 0.02, 0.02, 0.11, 0.05, Armour2);
                    FStrip(p, r, k, s, u0 + (u1 - u0) * 0.6, u0 + (u1 - u0) * 0.75, (v0 + v1) / 2 + 0.05, v1 - 0.05, 0.022, Primer, "worn paint");
                    break;
                default:
                    FStrip(p, r, k, s, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.02, Frame, "dirty steel");
                    FStrip(p, r, k, s, u0 + 0.05, u0 + 0.09, v0, v0 + (v1 - v0) * 0.55, 0.021, Rust, "rust");
                    break;
            }
        }
    }

    /// <summary>A hoop frame's rib round ring r: a dark girder standing 0.1 m proud of the skin all the way round (belly, sides,
    /// spine), drawn only; its hidden part is the slices of the walls at the frame (the splitter gives them to the frame).</summary>
    private static void Rib(Part p, int r)
    {
        const double Proud = 0.11;
        double[] Normal2(double[] a, double[] b, int s)
        {
            var d = U2(b[0] - a[0], b[1] - a[1]);
            double[] n = [d[1], -d[0]];
            var c = new[] { (a[0] + b[0]) / 2 - Cx, (a[1] + b[1]) / 2 - 3.0 };
            return n[0] * c[0] + n[1] * c[1] < 0 ? [-n[0], -n[1]] : n;
        }
        // Neighbouring segments overlap at their corners, so every other one is 8 mm thinner (its fore and aft faces stay inside its neighbour).
        void Seg(double[] a, double[] b, int s, bool thick)
        {
            var Half = thick ? 0.1 : 0.096;
            var n = Normal2(a, b, s);
            // (each end runs 15 mm past the ring's point, so a rib's ends never share a plane with the facets' edges there)
            var d = U2(b[0] - a[0], b[1] - a[1]);
            a = [a[0] - d[0] * 0.015, a[1] - d[1] * 0.015, a[2]];
            b = [b[0] + d[0] * 0.015, b[1] + d[1] * 0.015, b[2]];
            double[][] Ring(double dz) => [P(a[0], a[1], a[2] + dz), P(b[0], b[1], b[2] + dz), P(b[0] + n[0] * Proud, b[1] + n[1] * Proud, b[2] + dz), P(a[0] + n[0] * Proud, a[1] + n[1] * Proud, a[2] + dz)];
            Prism(p, Ring(-Half), Ring(Half), Frame, "dirty steel");
        }
        double[] V(int k, int s) => [Cx + s * Rings[r][k].W, Rings[r][k].Y, Rings[r][k].Z];
        foreach (var s in new[] { -1, 1 })
            for (var k = 0; k < 4; k++)
                Seg(V(k, s), V(k + 1, s), s, k % 2 == 1);
        Seg(V(B, -1), V(B, 1), 1, true);
        Seg(V(R, 1), V(R, -1), 1, false);
    }

    /// <summary>A box run along z split at the frames (the frame's rib covers the joint), so each length goes to its own hull piece.</summary>
    private static void RunZ(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish)
    {
        var cuts = new List<double> { z0 };
        foreach (var f in FrameZ)
            if (f > z0 + 0.2 && f < z1 - 0.2)
            {
                cuts.Add(f - 0.04);
                cuts.Add(f + 0.04);
            }
        cuts.Add(z1);
        for (var i = 0; i + 1 < cuts.Count; i += 2)
            Detail(p, x0, y0, cuts[i], x1, y1, cuts[i + 1], color, finish);
    }

    /// <summary>A partition from (x0, z0) to (x1, z1) (one of them equal: a run along an axis, 0.25 m thick about the line), from y0 to
    /// y1, with doorways (along the run: from, to, height over y0) left open. Its own piece.</summary>
    private static void Partition(Part p, double x0, double z0, double x1, double z1, double y0, double y1, string color, string finish, params (double A, double B, double Hgt)[] doors)
    {
        var alongX = Math.Abs(z1 - z0) < 1e-6;
        var a0 = alongX ? Math.Min(x0, x1) : Math.Min(z0, z1);
        var a1 = alongX ? Math.Max(x0, x1) : Math.Max(z0, z1);
        var c = alongX ? z0 : x0;
        var was = Tag;
        Tag = $"Rust Bucket Partition {++PartitionCount}";
        void Run(double a, double b, double ya, double yb)
        {
            if (b - a < 0.02)
                return;
            if (alongX)
                Solid(p, a, ya, c - 0.125, b, yb, c + 0.125, color, finish);
            else
                Solid(p, c - 0.125, ya, a, c + 0.125, yb, b, color, finish);
        }
        var at = a0;
        foreach (var (da, db, hgt) in doors.OrderBy(d => d.A))
        {
            Run(at, da, y0, y1);
            Run(da, db, y0 + hgt, y1); // the lintel
            at = db;
        }
        Run(at, a1, y0, y1);
        // A dark kick strip along the bottom of each face (the low paint), drawn only, 4 mm proud so it never shares the floor's plane.
        foreach (var s in new[] { -1, 1 })
        {
            var face = c + s * 0.13;
            at = a0;
            foreach (var (da, db, _) in doors.OrderBy(d => d.A).Append((a1, a1, 0)))
            {
                if (da - at > 0.05)
                {
                    if (alongX)
                        Detail(p, at, y0 + 0.004, Math.Min(face, face + s * 0.012), da, y0 + 0.9, Math.Max(face, face + s * 0.012), WallLow, "matte paint");
                    else
                        Detail(p, Math.Min(face, face + s * 0.012), y0 + 0.004, at, Math.Max(face, face + s * 0.012), y0 + 0.9, da, WallLow, "matte paint");
                }
                at = db;
            }
        }
        Tag = was;
        LastPartition = $"Rust Bucket Partition {PartitionCount}";
    }

    private static int PartitionCount;
    private static string LastPartition = "";

    /// <summary>What closes the gap between a partition across the hull at z (stopping at the room's wall) and the skin, over y0..y1:
    /// a wedge each side, drawn only, with the last partition.</summary>
    private static void Fillers(Part p, int level, double z, double y0, double y1, string color, string finish, bool port = true, bool starboard = true)
    {
        var inner = RoomHalf(level, z - 0.12, z + 0.12);
        With(LastPartition, () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                if (s < 0 && !port || s > 0 && !starboard)
                    continue;
                double OutAt(double y) => Math.Max(inner, SkinHalf(z, y) - 0.03);
                double[][] Tri(double zz) => [P(Cx + s * inner, y0, zz), P(Cx + s * OutAt(y0), y0, zz), P(Cx + s * OutAt(y1), y1, zz), P(Cx + s * inner, y1, zz)];
                Prism(p, Tri(z - 0.12), Tri(z + 0.12), color, finish);
            }
        });
    }

    private static Part Big(string name, string desc) => new(name, Small, "Rust bucket", desc, role: "frame", tool: "welder", material: "steel", color: HullPaint, group: Group);

    // ---- the keel: the lower deck, the belly, the lower hull, the chin's cheeks, the tail's lower half

    private static Part Keel()
    {
        var p = Big("Rust Bucket Keel", "The rust bucket's lower deck: the hold's and the engine room's floor, the belly and the legs' mounts.");
        // The belly (flat between the belly edges), and the lower facets a side: the bilge (belly edge to chine) and the flared side
        // (chine to deck line), from the nose ring back to the tail.
        for (var r = 1; r < Rings.Length - 1; r++)
            With(r == 5 ? "Rust Bucket Tail" : null, () => End2(p, r, B, Belly, "dirty steel"));
        // (ring 5, the tail's, is drawn below under the tail's own tag: these loops stop at ring 4, or its facets came twice)
        With(HullTag, () =>
        {
            for (var r = 1; r < Rings.Length - 2; r++)
                foreach (var s in new[] { -1, 1 })
                {
                    Facet(p, r, B, s, Belly, "dirty steel", paint: false);
                    var c = r == 1 ? Patch : r == 5 ? RoofDark : (r + (s > 0 ? 1 : 0)) % 3 == 0 ? Patch2 : HullPaint;
                    Facet(p, r, L, s, c, "rusty paint", paint: c == HullPaint);
                }
            // The chine and the beam's rails, the beam's the heavier (the rub strake).
            for (var r = 1; r < Rings.Length - 2; r++)
                foreach (var s in new[] { -1, 1 })
                {
                    Chine(p, r, L, s, 0.16, 0.1, Armour);
                    Chine(p, r, D, s, 0.24, 0.17, Frame);
                }
            // Armour and patches on the flared sides, below and above the fuel lines; the bilge gets a few slabs.
            foreach (var s in new[] { -1, 1 })
                for (var r = 2; r <= 4; r++)
                {
                    Plates(p, r, L, s, r * 3 + 1, 0.06, 0.44);
                    Plates(p, r, L, s, r * 3 + 2, 0.6, 0.94);
                    Plates(p, r, B, s, r + 5, 0.15, 0.85);
                }
            foreach (var s in new[] { -1, 1 })
            {
                Plates(p, 1, L, s, 11, 0.3, 0.9);
                Plates(p, 5, L, s, 12, 0.1, 0.9);
                // A worn ochre stripe along the waist, just under the rub strake.
                for (var r = 2; r <= 4; r++)
                    FStrip(p, r, L, s, 0.02, 0.98, 0.955, 0.985, 0.012, Stripe, "worn paint");
                // The fuel lines: two pipes from the saddle tank aft to the pod's pylon, in the gap between the rows of plates,
                // on brackets.
                foreach (var (v, hy) in new[] { (0.47, 0.08), (0.56, 0.07) })
                {
                    var y = 3.0 * v;
                    var x = 4.8 + 0.5 * v + 0.03;
                    RunZ(p, Cx + s * x, y - hy / 2, 6.9, Cx + s * (x + hy), y + hy / 2, 12.25, v < 0.5 ? "#6b6a60" : Primer, "dirty steel");
                }
                foreach (var z in new[] { 7.5, 9.6, 11.6 })
                    Detail(p, Cx + s * 4.98, 1.3, z - 0.05, Cx + s * 5.2, 1.82, z + 0.05, Frame, "dirty steel");
            }
            // A sealed side hatch on the port bow quarter, welded shut long ago: a frame, the hatch, a hazard edge, a handle.
            Slab(p, 2, L, -1, 0.09, 0.27, 0.2, 0.84, 0.02, 0.07, 0.03, Frame, "dirty steel");
            Slab(p, 2, L, -1, 0.1, 0.26, 0.23, 0.81, 0.07, 0.1, 0.02, Patch2, "rusty paint", paint: true);
            FStrip(p, 2, L, -1, 0.1, 0.26, 0.23, 0.27, 0.105, Stripe, "hazard paint");
            Slab(p, 2, L, -1, 0.21, 0.24, 0.5, 0.62, 0.1, 0.16, 0.01, "#8a8e8a", "steel");
        });
        // The tail's lower half: its facets, and the end plate's lower bands with the heat exchanger's slats across it.
        With("Rust Bucket Tail", () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 5, B, s, Belly, "dirty steel", paint: false);
                Facet(p, 5, L, s, RoofDark, "rusty paint", paint: false);
                Chine(p, 5, L, s, 0.16, 0.1, Armour);
                Chine(p, 5, D, s, 0.24, 0.17, Frame);
            }
            End(p, 6, B, Belly, "dirty steel", paint: false);
            End(p, 6, L, RoofDark, "soot", paint: false);
            for (var y = 0.8; y < 2.75; y += 0.18)
            {
                var t = (y - 0.5) / 2.5;
                var zf = 18.5 + 0.2 * t; // (the end plate leans from the chine at z 18.5 to the beam at 18.7)
                var w = 3.0 + 0.4 * t - 0.3;
                Detail(p, Cx - w, y, zf + 0.03, Cx + w, y + 0.07, zf + 0.1, "#1e1d1c", "dirty steel");
            }
            Void(p, Back + 0.1, 18.3, 0.5, F1);
            Void(p, Back + 0.1, 18.0, 0.05, 0.5, 0.5);
            // Tail lights: white either side of the slats.
            foreach (var s in new[] { -1, 1 })
                Detail(p, Cx + s * 2.95, 2.75, 18.68, Cx + s * 2.75, 2.9, 18.78, "#fff4d8", glow: true);
        });
        // The chin's cheeks either side of the ramp (the ramp is the rest of the chin: Rust Bucket Ramp).
        With("Rust Bucket Chin", () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                var lo = Pt(1, L, s);
                var hi = Pt(1, D, s);
                double[] loIn = [lo[0] - s * 0.1, lo[1], lo[2]], hiIn = [hi[0] - s * 0.1, hi[1], hi[2]];
                Quad(p, loIn, lo, hi, hiIn, Patch);
                // (the chine's rail here is the hull loop's: Where gives it to the chin)
            }
            // Hazard chevrons on the cheeks, the ramp's edge.
            foreach (var s in new[] { -1, 1 })
                Detail(p, Cx + s * 1.86, 0.3, -0.42, Cx + s * 1.96, 2.6, -0.32, Stripe, "hazard paint");
        });
        // The floor; the hidden walls round the hold and the engine room; the engine room's bulkhead; the belly void (the legs' mounts).
        FloorOf(p, 0, 0.0, F0, Floor, []);
        HullWalls(p, 0);
        Partition(p, Cx - Lw, 12.0, Cx + Lw, 12.0, F0, F1, Wall, "painted plating", (4.25, 5.75, 2.25));
        foreach (var (a, b) in Strips(Rings[1][B].Z, Back))
        {
            var y0 = Math.Max(PointAt(B, a).Y, PointAt(B, b).Y);
            var w = Cm(Math.Min(SkinHalf(a, y0 + 0.001), SkinHalf(b, y0 + 0.001)) - 0.06);
            if (w > 0.3 && y0 < -0.05)
                Hidden(p, Cx - w, y0, a, Cx + w, 0.0, b);
        }
        // Under the upper deck: frames across the hold's ceiling, every 2 m (drawn only, a little under the deck), and a cable tray.
        With("Rust Bucket Deck", () =>
        {
            foreach (var z in new[] { 4.0, 6.0, 8.0, 10.0 })
                Detail(p, Cx - Lw + 0.004, F1 - 0.22, z - 0.08, z is 8.0 or 10.0 ? StairHole.X0 - 0.1 : Cx + Lw - 0.004, F1 - 0.012, z + 0.08, Frame, "dirty steel");
            Detail(p, 1.45, F1 - 0.3, 3.4, 1.85, F1 - 0.24, 11.8, "#6b6a60", "dirty steel");
        });
        // Sockets: the ramp's hinge line, the stair's foot, the engine, the pods and saddle tanks (on the skin), the legs, the fittings.
        Sock(p, "ramp", Cx, 0.1, RampHingeZ, "-z");
        Hidden(p, Cx - 0.1, -0.1, RampHingeZ, Cx + 0.1, F0, LowFront); // (what the ramp's socket is on)
        Sock(p, "stair", StairHole.X0 + 0.175, F0, 8.375, "+y");
        Sock(p, "engine", Cx, F0, 14.2, "+y");
        foreach (var (z, i) in new[] { (4.5, 0), (7.5, 1), (10.5, 2) })
            Sock(p, $"shelfL{i}", Cx - Lw, F0 + 0.125, z, "+x");
        Sock(p, "rackR", Cx + Lw, F0 + 0.9, 5.0, "-x");
        Sock(p, "lockerR", Cx + Lw, F0 + 0.125, 6.5, "-x");
        Sock(p, "crate0", 2.4, F0, 10.6, "+y");
        Sock(p, "crate1", 3.2, F0, 10.6, "+y");
        Sock(p, "panelEng", 1.9, F0 + 0.6, 12.125, "+z");
        Sock(p, "boardEng", 8.0, F0 + 0.9, 12.125, "+z");
        Sock(p, "pipesEngL", 3.1, F0 + 2.6, 12.125, "+z");
        Sock(p, "battery", 7.3, F0 + 2.2, 12.125, "+z");
        Sock(p, "switchHoldL", Cx - Lw, F0 + 1.3, 7.2, "+x");
        Sock(p, "switchHoldR", Cx + Lw, F0 + 1.3, 7.4, "-x");
        Sock(p, "switchEng", 6.3, F0 + 1.3, 12.125, "+z");
        foreach (var (z, i) in new[] { (3.6, 0), (7.6, 1) })
        {
            Sock(p, $"lampHoldL{i}", Cx - Lw, 2.5, z + 1.4, "+x");
            Sock(p, $"lampHoldR{i}", Cx + Lw, 2.5, z + 0.4, "-x");
        }
        Sock(p, "lampEngL", Cx - Lw, 2.5, 13.2, "+x");
        Sock(p, "lampEngR", Cx + Lw, 2.5, 13.2, "-x");
        // (each on a small hidden mount just inside the skin, outside the hull run's wall: the pod and the tank hang from the keel)
        foreach (var s in new[] { -1, 1 })
        {
            Sock(p, s < 0 ? "podL" : "podR", Cx + s * PodInner, PodY, PodZ, s < 0 ? "-x" : "+x");
            Hidden(p, Cx + s * Cm(SkinMin(PodZ - 0.1, PodZ + 0.1, PodY - 0.1, PodY + 0.1) - 0.05), PodY - 0.1, PodZ - 0.1, Cx + s * PodInner, PodY + 0.1, PodZ + 0.1);
            Sock(p, s < 0 ? "saddleL" : "saddleR", Cx + s * SadInner, SadY, SadZ, s < 0 ? "-x" : "+x");
            Hidden(p, Cx + s * Cm(SkinMin(SadZ - 0.1, SadZ + 0.1, SadY - 0.1, SadY + 0.1) - 0.05), SadY - 0.1, SadZ - 0.1, Cx + s * SadInner, SadY + 0.1, SadZ + 0.1);
        }
        foreach (var (x, z, n) in new[] { (Cx - 2.4, 4.6, "legFL"), (Cx + 2.4, 4.6, "legFR"), (Cx - 2.4, 12.8, "legBL"), (Cx + 2.4, 12.8, "legBR") })
            Sock(p, n, x, -0.6, z, "-y");
        return p;
    }

    /// <summary>An end's band between points k and k+1 of ring r, across the middle.</summary>
    private static void End(Part p, int r, int k, string color, string finish = "rusty paint", bool glass = false, bool paint = true) =>
        Quad(p, Up(Pt(r, k, -1), 0.003), Up(Pt(r, k, 1), 0.003), Up(Pt(r, k + 1, 1), -0.003), Up(Pt(r, k + 1, -1), -0.003), color, finish, glass, paint);

    /// <summary>The flat across the middle between points k of rings r and r+1 (the belly, the spine).</summary>
    private static void End2(Part p, int r, int k, string color, string finish, bool glass = false) =>
        Quad(p, Pt(r, k, -1), Pt(r, k, 1), Pt(r + 1, k, 1), Pt(r + 1, k, -1), color, finish, glass);

    private static double[] Up(double[] q, double dy) => [q[0], q[1] + dy, q[2]];

    // ---- the ramp: the chin, an event door that swings down to the ground

    private const double RampHingeZ = -0.4; // where the chin's plane crosses the lower floor's top

    private static Part Ramp()
    {
        // Shut it is the chin: a plate from the hinge (the lower floor's top, z RampHingeZ) up the chin's plane to the deck line,
        // between the cheeks. It turns a quarter about x through the hinge, forward and down, to lie 22 degrees below level with
        // its tip on the ground. Its hidden boxes are level treads in the open pose, turned back to the shut one.
        var p = new Part("Rust Bucket Ramp", Small, "Rust bucket", "The rust bucket's chin: F lowers it as a ramp to the ground and raises it again.", role: "door", tool: "welder", material: "steel", color: Patch, group: Group);
        double x0 = Cx - 1.88, x1 = Cx + 1.88, hy = F0, hz = RampHingeZ;
        var ln = Math.Sqrt(3.0 * 3.0 + 1.2 * 1.2);
        double uy = 3.0 / ln, uz = -1.2 / ln; // up the plate, shut
        double ny = uz, nz = -uy; // its outer face's normal, shut (forward and down)
        var len = (3.0 - F0) / uy;
        double[] Shut(double x, double a, double o) => P(x, hy + a * uy + o * ny, hz + a * uz + o * nz);
        void Strip(double a0, double a1, double o, double xa, double xb, string c, string f) =>
            Plate(p, [Shut(xa, a0, o), Shut(xb, a0, o), Shut(xb, a1, o), Shut(xa, a1, o)], c, f, paint: c == Patch);
        Strip(0, len, -0.02, x0, x1, Patch, "rusty paint");
        foreach (var a in new[] { 0.7, 1.5, 2.3 })
            Strip(a - 0.05, a + 0.05, -0.07, x0 + 0.1, x1 - 0.1, Frame, "dirty steel"); // (ribs inside)
        Strip(0.03, 0.3, 0.015, x0 + 0.05, x1 - 0.05, Stripe, "hazard paint"); // (the foot's edge, outside)
        // Treads, open: step i runs from i/n to (i+1)/n of the way out, its top level with the slope at its hinge end. Open is
        // (y, z) -> (z, -y) about the hinge from shut, so shut is (dy, dz) -> (-dz, dy).
        const int N = 10;
        for (var i = 0; i < N; i++)
        {
            double a0 = len * i / N, a1 = len * (i + 1) / N;
            // Open pose (relative to the hinge): forward is -z, the slope's direction is n's turn of u, i.e. (ny, nz).
            double zA = a0 * nz, zB = a1 * nz, top = a0 * ny, bot = a1 * ny - 0.08;
            double oz0 = Math.Min(zA, zB), oz1 = Math.Max(zA, zB), oy0 = bot, oy1 = top;
            // Turn back: (dy, dz) -> (-dz, dy).
            double sy0 = -oz1, sy1 = -oz0, sz0 = oy0, sz1 = oy1;
            Hidden(p, x0, hy + sy0, hz + sz0, x1, hy + sy1, hz + sz1);
        }
        Hidden(p, Cx - 0.1, 0.0, hz - 0.1, Cx + 0.1, 0.2, hz);
        p.Sock("hang", P(Cx, 0.1, hz), "+z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "-x"), ("pivot", Py.L(R(Cx - H), R(hy - H), R(hz - H))), ("angle", 90), ("seconds", 4.0), ("hang", "hang"))));
        return p.Stats(1900, 3000, 30, 8.0);
    }

    // ---- the deck: the upper deck, the canopy and the bridge front, the upper hull, the tail's upper half

    private static readonly (double X0, double Z0, double X1, double Z1) StairHole = (6.55, 8.25, Cx + Uw, 11.75); // (the stair hall, inside the rooms' starboard wall)

    /// <summary>A frame bar from a to b: a thin plate 8 cm wide, moved by <paramref name="shift"/> to stand proud of the glass.</summary>
    private static void Bar(Part p, double[] a, double[] b, double[] shift)
    {
        a = Add(a, shift);
        b = Add(b, shift);
        var d = Unit(Sub(b, a));
        double[] up = Math.Abs(d[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
        var side = Unit(Cross(d, up));
        Plate(p, [Add(a, side, -0.04), Add(a, side, 0.04), Add(b, side, 0.04), Add(b, side, -0.04)], Frame, "dirty steel");
    }

    private static Part DeckPart()
    {
        var p = Big("Rust Bucket Deck", "The rust bucket's upper deck: the floor of the cockpit, the cabins, hydroponics and the galley.");
        // The nose: the bridge front over the ramp, its underside, its plated sides, a windscreen raked over the cockpit, the
        // side windows, the bridge's windows under a visor, a bumper and two searchlights.
        With("Rust Bucket Canopy", () =>
        {
            Quad(p, Pt(0, L, -1), Pt(0, L, 1), Pt(1, D, 1), Pt(1, D, -1), Belly, "dirty steel");
            const double Glass = 0.15; // (the windscreen starts this far back along the nose's top: far enough forward that the pilot sees up and ahead)
            foreach (var s in new[] { -1, 1 })
            {
                Plate(p, [Pt(0, L, s), Pt(0, D, s), Pt(1, D, s)], Armour, "plated steel");
                // The cheeks either side of the bridge front: plate low down, a window at eye height (Karl, 2026-11-16: the pilot
                // looked out through a slot), so the pilot sees ahead and to the side past the front's band.
                const double Cv0 = 0.3;
                Facet(p, 0, D, s, GlassTint, glass: true, paint: false);
                FStrip(p, 0, D, s, 0.01, 0.99, 0.01, Cv0, 0.012, HullPaint, "rusty paint");
                var nc = FacetNormal(0, D, s);
                Bar(p, OnFacet(0, D, s, 0.5, Cv0), OnFacet(0, D, s, 0.5, 0.97), [nc[0] * 0.035, nc[1] * 0.035, nc[2] * 0.035]);
                Bar(p, OnFacet(0, D, s, 0.03, Cv0 + 0.01), OnFacet(0, D, s, 0.97, Cv0 + 0.01), [nc[0] * 0.04, nc[1] * 0.04, nc[2] * 0.04]);
                Facet(p, 0, U, s, HullPaint, u1: Glass);
                Facet(p, 0, U, s, GlassTint, glass: true, paint: false, u0: Glass);
                // The side windows: a band of glass in the plated side, a frame bar under it.
                const double Wu0 = 0.1, Wu1 = 0.92, Wv0 = 0.42, Wv1 = 0.93;
                void Pane(double u0, double u1, double v0, double v1, bool glass) =>
                    Quad(p, OnFacet(1, D, s, u0, v0), OnFacet(1, D, s, u0, v1), OnFacet(1, D, s, u1, v1), OnFacet(1, D, s, u1, v0), glass ? GlassTint : HullPaint, glass: glass, paint: !glass);
                Pane(Wu0, Wu1, Wv0, Wv1, true);
                Pane(0.0015, 0.9985, 0, Wv0, false);
                Pane(0.0015, 0.9985, Wv1, 1, false);
                Pane(0.0015, Wu0, Wv0, Wv1, false);
                Pane(Wu1, 0.9985, Wv0, Wv1, false);
                Plates(p, 1, D, s, 6, 0.06, 0.36);
                Chine(p, 0, D, s, 0.2, 0.14, Frame);
                Chine(p, 0, U, s, 0.12, 0.08, Armour);
                Chine(p, 0, R, s, 0.1, 0.07, Armour);
                Chine(p, 1, U, s, 0.14, 0.09, Armour);
                // The side windows' mullions and the windscreen's bars, proud of the glass.
                var n1 = FacetNormal(1, D, s);
                foreach (var u in new[] { 0.37, 0.65 })
                    Bar(p, OnFacet(1, D, s, u, Wv0), OnFacet(1, D, s, u, Wv1), [n1[0] * 0.025, n1[1] * 0.025, n1[2] * 0.025]);
                Bar(p, OnFacet(1, D, s, Wu0, Wv0 - 0.01), OnFacet(1, D, s, Wu1, Wv0 - 0.01), [n1[0] * 0.03, n1[1] * 0.03, n1[2] * 0.03]);
                var n0 = FacetNormal(0, U, s);
                Bar(p, OnFacet(0, U, s, Glass, 0.03), OnFacet(0, U, s, Glass, 0.97), [n0[0] * 0.025, n0[1] * 0.025, n0[2] * 0.025]);
                Bar(p, OnFacet(0, U, s, 0.72, 0.03), OnFacet(0, U, s, 0.72, 0.97), [n0[0] * 0.025, n0[1] * 0.025, n0[2] * 0.025]);
                // A searchlight on each cheek of the nose.
                Slab(p, 0, D, s, 0.56, 0.66, 0.04, 0.26, 0.05, 0.23, 0.03, Frame, "dirty steel");
                FStrip(p, 0, D, s, 0.575, 0.645, 0.07, 0.23, 0.235, "#fff2c8", glow: true);
            }
            // The spine across the nose: plated forward, glass aft of the windscreen's front bar, a bar up the middle.
            double[] SpineAt(int side, double u) => Lerp(Pt(0, R, side), Pt(1, R, side), u);
            Quad(p, Pt(0, R, -1), Pt(0, R, 1), SpineAt(1, Glass), SpineAt(-1, Glass), HullPaint, paint: true);
            Quad(p, SpineAt(-1, Glass), SpineAt(1, Glass), Pt(1, R, 1), Pt(1, R, -1), GlassTint, glass: true);
            Bar(p, Mid(SpineAt(-1, Glass), SpineAt(1, Glass)), Mid(Pt(1, R, -1), Pt(1, R, 1)), [0, 0.025, 0]);
            // The bridge's front, a blunt raked face like a tug's: a bumper and armour low down, a band of windows at eye height with
            // mullions, and a brow over them that juts out as a sun visor.
            double[] F(int k, double t) => Lerp(Pt(0, k, -1), Pt(0, k, 1), t);
            double[] Band(int k0, int k1, double t, double v) => Lerp(F(k0, t), F(k1, t), v);
            Quad(p, F(L, 0), F(L, 1), F(D, 1), F(D, 0), Armour, "plated steel");
            const double Fv0 = 0.1, Fv1 = 0.9, Ft0 = 0.05, Ft1 = 0.95;
            Quad(p, Band(D, U, 0, 0), Band(D, U, 1, 0), Band(D, U, 1, Fv0), Band(D, U, 0, Fv0), HullPaint, paint: true);
            Quad(p, Band(D, U, 0, Fv1), Band(D, U, 1, Fv1), Band(D, U, 1, 1), Band(D, U, 0, 1), HullPaint, paint: true);
            Quad(p, Band(D, U, 0, Fv0), Band(D, U, Ft0, Fv0), Band(D, U, Ft0, Fv1), Band(D, U, 0, Fv1), HullPaint, paint: true);
            Quad(p, Band(D, U, Ft1, Fv0), Band(D, U, 1, Fv0), Band(D, U, 1, Fv1), Band(D, U, Ft1, Fv1), HullPaint, paint: true);
            Quad(p, Band(D, U, Ft0, Fv0), Band(D, U, Ft1, Fv0), Band(D, U, Ft1, Fv1), Band(D, U, Ft0, Fv1), GlassTint, glass: true);
            var fn = Unit(Cross(Sub(F(D, 1), F(D, 0)), Sub(F(U, 0), F(D, 0))));
            if (fn[2] > 0)
                fn = [-fn[0], -fn[1], -fn[2]];
            // (no mullion: one stood in front of the pilot's crosshair. Karl, 2026-11-16: a clear view ahead)
            Bar(p, Band(D, U, Ft0, Fv0 + 0.005), Band(D, U, Ft1, Fv0 + 0.005), [fn[0] * 0.035, fn[1] * 0.035, fn[2] * 0.035]);
            Quad(p, F(U, 0), F(U, 1), F(R, 1), F(R, 0), RoofDark, "plated steel");
            // The visor: a dark slab from the brow's foot, out over the windows and a little down.
            double[] Out(double[] q, double dy, double dz) => [q[0], q[1] + dy, q[2] + dz];
            double[][] Visor(double dy) => [Out(F(U, -0.04), dy, 0.02), Out(F(U, 1.04), dy, 0.02), Out(F(U, 1.02), dy - 0.08, -0.5), Out(F(U, -0.02), dy - 0.08, -0.5)];
            Prism(p, Visor(-0.02), Visor(0.1), Armour, "plated steel");
            // The bumper: a heavy beam across the face at the beam line, hazard chevrons on it, two headlamps under it.
            double[][] Beam(double dy) => [Out(F(D, -0.06), dy, 0.05), Out(F(D, 1.06), dy, 0.05), Out(F(D, 1.06), dy, -0.28), Out(F(D, -0.06), dy, -0.28)];
            Prism(p, Beam(-0.16), Beam(0.1), Frame, "dirty steel");
            foreach (var t in new[] { 0.12, 0.3, 0.7, 0.88 })
            {
                var c = Out(F(D, t), 0, -0.283);
                Detail(p, c[0] - 0.12, c[1] - 0.12, c[2] - 0.006, c[0] + 0.12, c[1] + 0.06, c[2], Stripe, "hazard paint");
            }
            foreach (var t in new[] { 0.22, 0.78 })
            {
                var c = Lerp(F(L, t), F(D, t), 0.55);
                Detail(p, c[0] - 0.16, c[1] - 0.09, c[2] - 0.06, c[0] + 0.16, c[1] + 0.09, c[2] + 0.04, Frame, "dirty steel");
                Detail(p, c[0] - 0.12, c[1] - 0.06, c[2] - 0.065, c[0] + 0.12, c[1] + 0.06, c[2] - 0.06, "#fff2c8", glow: true);
            }
            // What a round hits in the nose: the void under the windscreen, ahead of the dashboard.
            foreach (var (y0, y1, z0) in new[] { (3.08, 3.6, -2.3), (3.6, 4.2, -2.35), (4.2, 4.8, -2.2), (4.8, 5.4, -1.95) })
                Void(p, z0, Rings[1][D].Z, y0, y1, 0.2);
            foreach (var (y0, y1) in new[] { (F1T, 3.9), (3.9, 4.6), (4.6, 5.3), (5.3, 5.9) })
                Void(p, Rings[1][D].Z, 0.5, y0, y1, 0.2);
        });
        // The upper sides aft of the canopy: painted plate with a row of slabs under the portholes and over them, the upper chine's
        // rail, the portholes (lit from inside).
        With(HullTag, () =>
        {
            for (var r = 2; r <= 4; r++)
                foreach (var s in new[] { -1, 1 })
                {
                    var c = (r + (s < 0 ? 1 : 0)) % 3 == 0 ? Patch : HullPaint;
                    Facet(p, r, D, s, c, paint: c == HullPaint);
                    Chine(p, r, U, s, 0.14, 0.09, Armour);
                    Plates(p, r, D, s, r * 2 + 3, 0.05, 0.3);
                    Plates(p, r, D, s, r * 2 + 4, 0.64, 0.95);
                }
            foreach (var s in new[] { -1, 1 })
                foreach (var (r, u) in new[] { (2, 0.47), (3, 0.36), (3, 0.87) })
                {
                    FStrip(p, r, D, s, u - 0.062, u + 0.062, 0.36, 0.58, 0.016, Frame, "dirty steel");
                    FStrip(p, r, D, s, u - 0.045, u + 0.045, 0.39, 0.55, 0.024, "#d8c890", glow: true);
                }
        });
        With("Rust Bucket Tail", () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 5, D, s, RoofDark, paint: false);
                Chine(p, 5, U, s, 0.14, 0.09, Armour);
                Plates(p, 5, D, s, 9, 0.15, 0.85);
            }
            End(p, 6, D, HullPaint);
            foreach (var (y0, y1) in new[] { (F1, 4.4), (4.4, 5.5) })
                Void(p, Back + 0.1, 18.5, y0, y1, 0.4);
        });
        // The floor (the stair's opening left out); the hidden walls round the rooms (the front's is the canopy's).
        FloorOf(p, 1, F1, F1T, Deck, [StairHole]);
        HullWalls(p, 1);
        // The cockpit's dashboard under the windscreen, in front of where anyone stands: a raked desk with dials and screens.
        // (kept low, its top well under a seated pilot's eye, and the yokes short stubs off the sight line: Karl, 2026-11-16)
        Solid(p, 2.9, F1T, 0.55, 7.1, F1T + 0.55, 1.35, Frame, "dirty steel");
        Detail(p, 3.0, F1T + 0.55, 0.75, 7.0, F1T + 0.69, 1.3, "#3a3d3f", "plastic");
        foreach (var (x, c) in new[] { (3.5, "#5fd06a"), (4.6, "#e0a030"), (5.4, "#5fd06a"), (6.5, "#d04030") })
            Detail(p, x - 0.3, F1T + 0.7, 0.85, x + 0.3, F1T + 0.72, 1.2, c, glow: true);
        foreach (var x in new[] { 4.3, 5.7 })
        {
            Detail(p, x - 0.03, F1T + 0.555, 1.305, x + 0.03, F1T + 0.8, 1.34, Frame, "dirty steel"); // (the yokes' columns)
            Detail(p, x - 0.14, F1T + 0.8, 1.29, x + 0.14, F1T + 0.84, 1.35, Frame, "rubber");
        }
        // Partitions: the cockpit's back wall, the corridor's walls, the cabins' ends, the galley's end; doorways 1.5 by 2.25 m. Ship
        // walls are painted plate; the bathroom's gloss white.
        const double Dh = 2.25;
        Partition(p, Cx - InHigh + 0.01, 4.0, Cx + InHigh - 0.01, 4.0, F1T, Ceil, Wall, "painted plating", (4.25, 5.75, Dh));
        Fillers(p, 1, 4.0, F1T, Ceil, Wall, "painted plating");
        Partition(p, 4.0, 4.125, 4.0, 16.04, F1T, Ceil, Wall, "painted plating", (5.25, 6.75, Dh), (9.25, 10.75, Dh));
        var corridor = LastPartition;
        Partition(p, 6.0, 4.125, 6.0, 7.875, F1T, Ceil, BathWall, "gloss paint", (5.25, 6.75, Dh));
        Partition(p, 6.0, 12.125, 6.0, 16.04, F1T, Ceil, Wall, "painted plating", (13.25, 14.75, Dh));
        Partition(p, Cx - Uw, 8.0, 3.875, 8.0, F1T, Ceil, Wall, "painted plating");
        Partition(p, 6.125, 8.0, Cx + Uw, 8.0, F1T, Ceil, BathWall, "gloss paint");
        Partition(p, 6.125, 12.0, Cx + Uw, 12.0, F1T, Ceil, Wall, "painted plating");
        // Pipes along the corridor's port wall under the ceiling, on brackets: the ship's plumbing, out in the open.
        With(corridor, () =>
        {
            Detail(p, 4.13, 5.62, 4.3, 4.21, 5.7, 15.9, "#6b6a60", "dirty steel");
            Detail(p, 4.13, 5.76, 4.3, 4.23, 5.86, 15.9, Primer, "dirty steel");
            for (var z = 5.0; z < 15.9; z += 1.5)
                Detail(p, 4.13, 5.58, z - 0.03, 4.27, 5.9, z + 0.03, Frame, "dirty steel");
        });
        // The stair's rail along the opening's inboard side, open at its head (aft) to step off into the hall.
        Solid(p, StairHole.X0 - 0.06, F1T, 8.25, StairHole.X0, F1T + 1.0, 10.6, Frame, "steel");
        Detail(p, StairHole.X0 - 0.1, F1T + 0.95, 8.25, StairHole.X0 + 0.04, F1T + 1.05, 10.6, Stripe, "hazard paint");
        // Sockets: the seats, the cabins' furniture, the doors' jambs, the lamps.
        Sock(p, "seatL", 4.0, F1T, 2.45, "+y");
        Sock(p, "seatR", 6.0, F1T, 2.45, "+y");
        Sock(p, "module", 4.0, F1T, 1.6, "+y"); // (on the floor in the pilot's footwell, so the seat's link runs along the floor, not up past the windscreen)
        Sock(p, "chart", 7.4, F1T + 0.475, 3.875, "-z");
        Sock(p, "bed", Cx - Uw, F1T + 0.125, 6.0, "+x");
        Sock(p, "locker", 3.2, F1T + 0.125, 4.125, "+z");
        Sock(p, "desk", 2.9, F1T + 0.125, 7.875, "-z");
        Sock(p, "shower", 7.9, F1T + 0.125, 4.125, "+z");
        Sock(p, "basin", Cx + Uw, F1T + 0.125, 6.1, "-x");
        Sock(p, "toilet", Cx + Uw, F1T + 0.125, 7.3, "-x");
        foreach (var (z, i) in new[] { (9.25, 0), (11.35, 1) })
            Sock(p, $"growL{i}", Cx - Uw, F1T + 0.125, z, "+x");
        Sock(p, "growM0", 3.875, F1T + 0.125, 12.0, "-x");
        Sock(p, "growM1", 3.875, F1T + 0.125, 14.1, "-x");
        Sock(p, "stove", Cx + Uw, F1T + 0.45, 15.2, "-x");
        Sock(p, "table", 7.15, F1T + 0.375, 12.125, "+z");
        Sock(p, "cabinet", Cx + Uw, F1T + 0.85, 13.8, "-x");
        Sock(p, "coffee", 6.125, F1T + 0.125, 15.35, "+x");
        Sock(p, "doorCockpit", 4.25, F1T + 1.125, 4.0, "+x");
        Sock(p, "doorBed", 4.0, F1T + 1.125, 5.25, "+z");
        Sock(p, "doorBath", 6.0, F1T + 1.125, 5.25, "+z");
        Sock(p, "doorHydro", 4.0, F1T + 1.125, 9.25, "+z");
        Sock(p, "doorGalley", 6.0, F1T + 1.125, 13.25, "+z");
        foreach (var (n, x, z, d) in new[]
        {
            ("lampCockpit", 9.3, 3.875, "-z"), ("lampBed", 2.0, 4.125, "+z"), ("lampBath", 8.0, 7.875, "-z"), ("lampHydroA", 3.875, 9.0, "-x"),
            ("lampHydroB", 3.875, 15.0, "-x"), ("lampCorrA", 4.125, 7.0, "+x"), ("lampCorrB", 4.125, 12.5, "+x"), ("lampGalley", 8.0, 12.125, "+z"),
        })
            Sock(p, n, x, F1T + 2.3, z, d);
        Sock(p, "switchFwd", 4.125, F1T + 1.3, 4.6, "+x");
        Sock(p, "switchAft", 5.875, F1T + 1.3, 12.5, "-x");
        return p;
    }

    // ---- the roof: the ceiling, the eaves, the spine, the frames' ribs, and the mounts for what stands on it

    private const double RoofDeck = 7.2, RoofLow = 6.6; // the roof void's top (what the roof gear stands on) and its step

    private static Part Roof()
    {
        var p = Big("Rust Bucket Roof", "The rust bucket's roof: the upper deck's ceiling, the eaves and the spine, and the mounts for the gear on it.");
        for (var r = 1; r <= 4; r++)
        {
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, r, U, s, r == 1 ? Patch : (r + (s > 0 ? 1 : 0)) % 2 == 0 ? HullPaint : RoofDark, paint: r != 1);
                Chine(p, r, R, s, 0.14, 0.1, Frame);
                if (r >= 2)
                    Plates(p, r, U, s, r * 5 + 2, 0.12, 0.88);
            }
            End2(p, r, R, RoofDark, "dirty steel");
        }
        // A walkway of grating down the spine between the frames, and a worn stripe either side of it.
        foreach (var (z0, z1) in new[] { (4.2, 8.38), (8.62, 13.6) })
        {
            Detail(p, Cx - 0.45, 7.3, z0, Cx + 0.45, 7.34, z1, "#3a3836", "grating");
            foreach (var s in new[] { -1, 1 })
                Detail(p, Cx + s * 0.5, 7.3, z0, Cx + s * 0.58, 7.326, z1, Stripe, "worn paint");
        }
        With("Rust Bucket Tail", () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 5, U, s, RoofDark, paint: false);
                Chine(p, 5, R, s, 0.14, 0.1, Frame);
                // Two raked fins on the tail's shoulders.
                double[][] Fin(double dx) =>
                [
                    P(Cx + s * (2.0 + dx), 7.0, 15.6), P(Cx + s * (2.0 + dx), 6.3, 18.3),
                    P(Cx + s * (2.0 + dx), 8.1, 18.9), P(Cx + s * (2.0 + dx), 8.1, 17.8),
                ];
                Prism(p, Fin(-0.04), Fin(0.04), Armour, "plated steel");
                Detail(p, Cx + s * 1.95, 8.1, 17.8, Cx + s * 2.05, 8.16, 18.9, Primer, "worn paint");
                Detail(p, Cx + s * 1.94, 8.04, 18.75, Cx + s * 2.06, 8.2, 18.95, "#d03020", glow: true);
            }
            End2(p, 5, R, RoofDark, "dirty steel");
            End(p, 6, U, RoofDark, "dirty steel", paint: false);
        });
        // The hoop frames: dark girders right round the hull at the shoulder, midship and the haunch.
        for (var i = 0; i < FrameZ.Length; i++)
            With($"Rust Bucket Frame {FrameNames[i]}", () => Rib(p, i + 2));
        // The ceiling (what the partitions meet), and the void over it to the skin, stepped: what the roof gear stands on.
        foreach (var (z0, z1) in Strips(UpFront, Back, RoomCuts(1)))
        {
            var w = RoomHalf(1, z0, z1);
            Solid(p, Cx - w, Ceil, z0, Cx + w, CeilT, z1, Ceiling, "painted plating");
        }
        Void(p, UpFront, Back, CeilT, RoofLow, 0.5);
        Void(p, 4.0, 13.8, RoofLow, RoofDeck, 0.5);
        // A hatch's coaming on the spine aft, shut, with grab rails.
        Solid(p, 3.6, RoofDeck, 13.0, 4.4, 7.42, 13.6, RoofDark, "dirty steel");
        Detail(p, 3.66, 7.42, 13.06, 4.34, 7.47, 13.54, Patch2, "rusty paint");
        foreach (var x in new[] { 3.45, 4.55 })
            Detail(p, x - 0.03, 7.3, 13.0, x + 0.03, 7.62, 13.6, Stripe, "hazard paint");
        // Running lights at the beam's ends: red to port, green to starboard, on the shoulder frame.
        Detail(p, Cx - 5.52, 2.9, 2.94, Cx - 5.42, 3.1, 3.06, "#e02018", glow: true);
        Detail(p, Cx + 5.42, 2.9, 2.94, Cx + 5.52, 3.1, 3.06, "#20d040", glow: true);
        // Mounts for the gear: the deck cargo, the crane, the mast (on the void's top) and the radiator wings (on its sides).
        Sock(p, "cargo", Cx - 0.5, RoofDeck, 9.0, "+y");
        Sock(p, "crane", Cx + 1.7, RoofDeck, 12.4, "+y");
        Sock(p, "mast", Cx - 1.6, RoofDeck, 5.2, "+y");
        Sock(p, "sensor", Cx - 0.7, RoofDeck, 5.2, "+y"); // (the Long Range Sensor beside the mast: the radar's scope)
        Sock(p, "radL", Cx - RadX, 6.3, RadZ, "-x");
        Sock(p, "radR", Cx + RadX, 6.3, RadZ, "+x");
        return p;
    }

    private static readonly double RadX = Cm(Math.Min(SkinHalf(RadZ - 2, RoofLow), SkinHalf(RadZ + 2, RoofLow)) - 0.06);
    private const double RadZ = 11.5;

    // ---- the gear on the roof

    /// <summary>A radiator wing on the eaves: a raked panel 2 m out from the hull with fins along it, the coolant line along its root
    /// and a glow along its edge when it runs hot. Origin on the roof's mount; out is +x for the starboard one.</summary>
    private static Part Radiator(int s)
    {
        var p = new Part(s < 0 ? "Rust Bucket Radiator L" : "Rust Bucket Radiator R", Small, "Rust bucket", "One of the rust bucket's radiator wings: dumps the engine's heat. Glows when it runs hot.", role: "frame", tool: "welder", material: "steel", color: RoofDark, group: Group);
        double X(double u) => s * u;
        p.Add(Box(Lo(X(0), -0.2, -1.7, X(0.55), 0.22, 1.7), Hi(X(0), -0.2, -1.7, X(0.55), 0.22, 1.7), Frame, shape: Kw(("finish", "dirty steel"))));
        double[] Q(double u, double y, double z) => [X(u), y, z];
        void Pl(double[][] pts, string c, string f, bool glow = false, double th = 0.07) => Plate(p, pts, c, f, glow: glow, thick: th);
        Pl([Q(0.5, 0.05, -1.9), Q(2.6, 0.6, -1.9), Q(2.6, 0.6, 1.9), Q(0.5, 0.05, 1.9)], RoofDark, "dirty steel");
        for (var z = -1.7; z <= 1.71; z += 0.38)
        {
            double[][] Rib(double dz) => [Q(0.62, 0.12, z + dz), Q(2.5, 0.61, z + dz), Q(2.5, 0.7, z + dz), Q(0.62, 0.21, z + dz)];
            Prism(p, Rib(-0.02), Rib(0.02), "#2d3033", "steel");
        }
        Pl([Q(2.6, 0.58, -1.85), Q(2.64, 0.6, -1.85), Q(2.64, 0.6, 1.85), Q(2.6, 0.58, 1.85)], "#ff7a3a", "", glow: true, th: 0.05);
        p.Add(Box(Lo(X(0.55), 0.22, -1.8, X(0.68), 0.34, 1.8), Hi(X(0.55), 0.22, -1.8, X(0.68), 0.34, 1.8), Primer, detail: true, shape: Kw(("finish", "dirty steel"))));
        foreach (var (u0, u1, y0) in new[] { (0.55, 1.25, 0.03), (1.25, 1.95, 0.21), (1.95, 2.6, 0.39) })
            p.Add(Box(Lo(X(u0), y0, -1.9, X(u1), y0 + 0.27, 1.9), Hi(X(u0), y0, -1.9, X(u1), y0 + 0.27, 1.9), hidden: true));
        p.Sock("root", [0, 0, 0], s < 0 ? "+x" : "-x");
        return p.Stats(450, 1500, 15, 6.0);
    }

    private static double[] Lo(double x0, double y0, double z0, double x1, double y1, double z1) => [Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)];

    private static double[] Hi(double x0, double y0, double z0, double x1, double y1, double z1) => [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)];

    private static void LBox(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string? color, string? finish = null, bool detail = false, bool hidden = false, bool glow = false)
    {
        PyDict? shape = null;
        if (finish != null || glow)
        {
            shape = new PyDict();
            if (finish != null)
                shape["finish"] = finish;
            if (glow)
                shape["glow"] = true;
        }
        p.Add(hidden ? Box(Lo(x0, y0, z0, x1, y1, z1), Hi(x0, y0, z0, x1, y1, z1), hidden: true) : Box(Lo(x0, y0, z0, x1, y1, z1), Hi(x0, y0, z0, x1, y1, z1), color!, detail: detail, shape: shape));
    }

    /// <summary>Deck cargo: a battered half-height container strapped to a cradle on the spine. Origin on the roof's mount.</summary>
    private static Part DeckCargo()
    {
        var p = new Part("Rust Bucket Deck Cargo", Small, "Rust bucket", "A battered half-height container strapped to the rust bucket's spine. Whatever is in it, nobody wrote it down.", role: "frame", tool: "welder", material: "steel", color: "#3f5e6a", group: Group);
        foreach (var z in new[] { -1.45, 1.45 })
            LBox(p, -1.3, 0, z - 0.15, 1.3, 0.3, z + 0.15, Frame, "dirty steel");
        LBox(p, -1.2, 0.3, -2.0, 1.2, 1.6, 2.0, "#3f5e6a", "rusty paint");
        // Corrugations down the sides, the door end's locking bars, rust, the straps over the top, a stencilled number.
        foreach (var s in new[] { -1, 1 })
            for (var z = -1.8; z <= 1.81; z += 0.3)
                LBox(p, s * 1.2, 0.36, z - 0.05, s * 1.23, 1.54, z + 0.05, "#35505b", "rusty paint", detail: true);
        foreach (var x in new[] { -0.75, -0.3, 0.3, 0.75 })
            LBox(p, x - 0.025, 0.34, 2.0, x + 0.025, 1.56, 2.035, "#8a8e8a", "steel", detail: true);
        LBox(p, -1.18, 0.32, 2.0, -0.4, 0.75, 2.02, Rust, "rust", detail: true);
        foreach (var z in new[] { -1.0, 1.0 })
        {
            LBox(p, -1.25, 1.6, z - 0.06, 1.25, 1.63, z + 0.06, Stripe, "worn paint", detail: true);
            foreach (var s in new[] { -1, 1 })
                LBox(p, s * 1.235, 0.3, z - 0.06, s * 1.255, 1.63, z + 0.06, Stripe, "worn paint", detail: true);
        }
        LBox(p, -1.235, 1.1, -0.6, -1.25, 1.35, 0.4, "#d8d0b0", "worn paint", detail: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(1600, 2500, 20, 8.0);
    }

    /// <summary>A deck crane: a pedestal, a slewing housing with its little cab, and a boom folded forward onto a crutch, its hook
    /// block resting on the spine. Origin on the roof's mount.</summary>
    private static Part Crane()
    {
        var p = new Part("Rust Bucket Crane", Small, "Rust bucket", "The rust bucket's deck crane, folded for flight: for swinging cargo up from the ground. The hook has seen things.", role: "frame", tool: "welder", material: "steel", color: Patch2, group: Group);
        double[][] Oct(double y, double r) => [.. Enumerable.Range(0, 8).Select(i => { var a = Math.PI / 8 + i * Math.PI / 4; return new[] { Py.Round(r * Math.Cos(a), 4), y, Py.Round(r * Math.Sin(a), 4) }; })];
        Prism(p, Oct(0, 0.42), Oct(0.12, 0.42), Frame, "dirty steel");
        Prism(p, Oct(0.12, 0.3), Oct(0.9, 0.3), Patch2, "rusty paint");
        LBox(p, -0.3, 0, -0.3, 0.3, 0.9, 0.3, null, hidden: true);
        LBox(p, -0.45, 0.9, -0.5, 0.45, 1.45, 0.6, Patch2, "rusty paint");
        LBox(p, -0.44, 0.95, 0.6, 0.1, 1.42, 0.63, "#7fa6c0", detail: true);
        LBox(p, -0.47, 1.0, -0.4, -0.45, 1.3, 0.4, Stripe, "hazard paint", detail: true);
        // The boom: a box girder from the housing's front, sloping down to the crutch, with its lightening holes painted dark.
        double[][] Sec(double z, double y, double h) => [[-0.13, y, z], [0.13, y, z], [0.13, y + h, z], [-0.13, y + h, z]];
        Prism(p, Sec(-0.5, 1.05, 0.34), Sec(-4.7, 0.78, 0.2), Patch2, "rusty paint");
        for (var i = 0; i < 6; i++)
        {
            var z = -0.95 - i * 0.62;
            var y = 1.05 - (0.27 * (z + 0.5) / -4.2) + 0.06;
            foreach (var s in new[] { -1, 1 })
                LBox(p, s * 0.131, y, z - 0.18, s * 0.137, y + 0.14, z + 0.18, Frame, "dirty steel", detail: true);
        }
        LBox(p, -0.14, 0.78, -4.704, 0.14, 1.0, -4.45, Stripe, "hazard paint", detail: true);
        LBox(p, -0.12, 0.95, -4.4, 0.12, 1.4, -0.6, null, hidden: true);
        // The ram from the housing to the boom, the crutch, the hook block on its wire.
        LBox(p, -0.05, 0.6, -0.55, 0.05, 1.12, -0.45, "#8a8e8a", "chrome", detail: true);
        LBox(p, -0.08, 0, -4.15, 0.08, 0.78, -3.95, Frame, "dirty steel");
        LBox(p, -0.2, 0.74, -4.2, 0.2, 0.78, -3.9, Frame, "dirty steel");
        LBox(p, -0.01, 0.42, -4.62, 0.01, 0.78, -4.6, "#30302e", detail: true);
        LBox(p, -0.12, 0.12, -4.75, 0.12, 0.42, -4.48, Stripe, "hazard paint");
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(900, 2000, 20, 8.0);
    }

    /// <summary>The mast forward on the spine: a pole with a dish, a radar bar, whip aerials and a red beacon. Origin on the mount.</summary>
    private static Part Mast()
    {
        var p = new Part("Rust Bucket Mast", Small, "Rust bucket", "The rust bucket's mast: a dish for the traffic channel, a radar bar that mostly turns, and a beacon.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        LBox(p, -0.28, 0, -0.28, 0.28, 0.16, 0.28, Frame, "dirty steel");
        LBox(p, -0.07, 0.16, -0.07, 0.07, 2.6, 0.07, "#5a5650", "steel");
        LBox(p, -0.5, 2.2, -0.08, 0.5, 2.3, 0.08, "#3a3d3f", "steel");
        // The dish: an octagon on a yoke, tilted up and forward.
        double[][] Dish(double off, double r)
        {
            var o = new List<double[]>();
            for (var i = 0; i < 8; i++)
            {
                var a = Math.PI / 8 + i * Math.PI / 4;
                double lx = r * Math.Cos(a), ly = r * Math.Sin(a);
                // plane tilted 35 degrees back from facing forward (-z), centred 0.35 m forward of the pole at 1.45 m
                double ct = Math.Cos(0.61), st = Math.Sin(0.61);
                o.Add([Py.Round(lx, 4), Py.Round(1.45 + ly * ct + off * st, 4), Py.Round(-0.35 - off * ct + ly * st, 4)]);
            }
            return [.. o];
        }
        Prism(p, Dish(0, 0.55), Dish(0.08, 0.5), "#c8ccc6", "matte paint");
        LBox(p, -0.04, 1.4, -0.3, 0.04, 1.5, -0.07, Frame, "dirty steel", detail: true);
        foreach (var x in new[] { -0.4, 0.42 })
            LBox(p, x - 0.012, 2.3, -0.012, x + 0.012, 3.3, 0.012, "#30302e", detail: true);
        LBox(p, -0.08, 2.6, -0.08, 0.08, 2.74, 0.08, "#ff3020", glow: true, detail: true);
        LBox(p, -0.07, 0.16, -0.07, 0.07, 2.6, 0.07, null, hidden: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(150, 600, 10, 4.0);
    }

    // ---- the engine pods, the saddle tanks, the drives and the lift jets

    /// <summary>An octagonal nacelle along z round (cx, cy) through the stations (z, radius), banded and patched, its ends capped.</summary>
    private static void Nacelle(Part p, double cx, double cy, (double Z, double Rr)[] st, int band, string capFront, string capBack)
    {
        double[] Ring(double z, double rr, int i)
        {
            var a = Math.PI / 8 + i * Math.PI / 4;
            return P(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a), z);
        }
        for (var k = 0; k < st.Length - 1; k++)
            for (var i = 0; i < 8; i++)
                Quad(p, Ring(st[k].Z, st[k].Rr, i), Ring(st[k].Z, st[k].Rr, i + 1), Ring(st[k + 1].Z, st[k + 1].Rr, i + 1), Ring(st[k + 1].Z, st[k + 1].Rr, i),
                    k == band ? (i % 3 == 0 ? Patch2 : HullPaint) : Patch, paint: k == band && i % 3 != 0);
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => Ring(st[0].Z, st[0].Rr, i))], Frame, capFront);
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => Ring(st[^1].Z, st[^1].Rr, i))], "#2a2624", capBack);
    }

    /// <summary>A ring of armour round a nacelle: an octagonal band from z0 to z1, r0 to r1 out (a cowl, a collar, a fin ring).</summary>
    private static void Collar(Part p, double cx, double cy, double z0, double z1, double r0, double r1, string color, string finish)
    {
        double[][] Ring(double z, double rr) => [.. Enumerable.Range(0, 8).Select(i => { var a = Math.PI / 8 + i * Math.PI / 4; return P(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a), z); })];
        Prism(p, Ring(z0, r0), Ring(z1, r1), color, finish);
    }

    // The stern pods' and the saddle tanks' middles and sizes (Sx metres), the main drives' and lift jets' pushes (N).
    private const double PodX = 6.1, PodY = 1.4, PodR = 0.9, SadX = 5.8, SadY = 1.0, SadR = 0.65, SadZ = 5.6, PodLiftZ = 14.2;
    private const double DriveForce = 600000, LiftForce = 800000, JetBurn = 3.0; // (l/s per MN, as the shipped jets)
    private const double PodZ = 14.5, PodEng = 14.85; // the pod's middle (its socket), the bulkhead between its tank and its engine housing
    internal const double RcsForce = 80000, HeavyRcsForce = 250000; // N: the attitude jets (Karl, 2026-11-15: a ship needs thrusters pointing every way to turn every way)
    private const double PodRcsZ = 15.4, PodRcsDownZ = 15.9, SadRcsZ = 6.1, SadRcsDownZ = 6.95;
    private static readonly double PodInner = Cm(SkinHalf(PodZ, PodY)), SadInner = Cm(SkinHalf(SadZ, SadY));

    private static Part Pod(int side)
    {
        var p = new Part(side < 0 ? "Rust Bucket Pod L" : "Rust Bucket Pod R", Small, "Rust bucket", "One of the rust bucket's engine pods: a patched nacelle full of fuel, a lift jet under it, and at its back the engine housing: the main drive on the tail plate outside, its Drive Core inside behind it, a service hatch in the outboard flank.", role: "fueltank", tool: "welder", material: "steel", color: HullPaint, group: Group);
        double cx = Cx + side * PodX, cy = PodY, r = PodR;
        Nacelle(p, cx, cy, [(10.4, 0.35), (11.3, r), (PodEng, r)], 1, "dirty steel", "soot");
        // The engine housing (ship-works.md 15.4, Karl 2026-11-16: "the nacelles don't have any thrust components in them"): the
        // tank stops at a bulkhead and the pod's tail is hollow round the drive's own Drive Core S, reached through a hatch.
        EngineHousing.Draw(p, P, cx, cy, Flat(r), EngineHousing.HalfFlat(Flat(r)), PodEng, 17.2, side, Patch, HullPaint, Frame, Stripe, "rusty paint", ShipWorksParts.CoreR[0], ShipWorksParts.CoreD[0], ShipWorksParts.CoreD[0]);
        Collar(p, cx, cy, PodEng - 0.08, PodEng + 0.12, r * 1.02, r * 1.08, Armour, "plated steel"); // (the band over the bulkhead)
        // A stepped intake cowl at the front, three cooling rings, a fairing along its top.
        Collar(p, cx, cy, 10.95, 11.45, r * 0.98, r * 1.1, Armour, "plated steel");
        Collar(p, cx, cy, 11.45, 11.75, r * 1.1, r * 1.04, Frame, "dirty steel");
        foreach (var z in new[] { 13.2, 13.5, 13.8 })
            Collar(p, cx, cy, z, z + 0.08, r * 1.01, r * 1.08, "#2d3033", "steel");
        Prism(p, [P(cx - 0.18, cy + r * 0.97, 11.8), P(cx + 0.18, cy + r * 0.97, 11.8), P(cx + 0.1, cy + r * 1.12, 12.3), P(cx - 0.1, cy + r * 1.12, 12.3)],
            [P(cx - 0.18, cy + r * 0.97, PodEng - 0.1), P(cx + 0.18, cy + r * 0.97, PodEng - 0.1), P(cx + 0.1, cy + r * 1.12, PodEng - 0.4), P(cx - 0.1, cy + r * 1.12, PodEng - 0.4)], Armour2, "plated steel");
        Detail(p, cx - side * 0.2, cy + 0.3, 13.6, cx - side * 0.83, cy + 0.42, 14.5, Stripe, "hazard paint");
        // Its engine is mended outside, through the hatch (ship works, Karl 2026-11-16): a grab rail along its outboard flank and a step under it, for a space walk or from the ground.
        foreach (var z in new[] { 14.1, 15.0 })
            Detail(p, cx + side * 0.74, cy + 0.43, z - 0.03, cx + side * 1.06, cy + 0.47, z + 0.03, "#9a9c98", "steel");
        Detail(p, cx + side * 1.03, cy + 0.41, 14.0, cx + side * 1.09, cy + 0.49, 15.1, Stripe, "worn paint");
        Detail(p, cx + side * 0.62, cy - 0.62, 14.75, cx + side * 0.98, cy - 0.58, 15.15, "#3a3836", "tread plate");
        // The pylons to the hull (each meets the skin), and the hidden box for the nacelle.
        var outer = cx - side * r * 0.9;
        foreach (var (z0, z1) in new[] { (12.2, 13.4), (15.0, 16.0) })
        {
            var inner = Cx + side * Cm(Math.Min(SkinHalf(z0, PodY), SkinHalf(z1, PodY)));
            Solid(p, inner, 1.15, z0, outer, 1.65, z1, Frame, "dirty steel");
        }
        Hidden(p, cx - r * 0.9, cy - r * 0.9, 11.3, cx + r * 0.9, cy + r * 0.9, PodEng - 0.004);
        Hidden(p, Cx + side * PodInner, PodY - 0.1, PodZ - 0.1, outer, PodY + 0.1, PodZ + 0.1); // (what the socket is on)
        Sock(p, "hull", Cx + side * PodInner, PodY, PodZ, side < 0 ? "+x" : "-x");
        Works.Mount(p, outside: true); // (the housing's tail plate: the drive bolts to it outside, its Drive Core inside)
        Sock(p, "lift", cx, cy - r * 0.9, PodLiftZ, "-y");
        // The attitude jets: up on the fairing, down under the pod behind the lift jet, out on the outboard flat.
        RcsMount(p, "rcsUp", cx, cy + r * 1.12, PodEng - 0.75, "+y", 0.36, Frame); // (on the fairing, ahead of the housing)
        RcsMount(p, "rcsDown", cx, cy - Flat(r), PodRcsDownZ, "-y", 0.36, Frame);
        RcsMount(p, "rcsOut", cx + side * Flat(r), cy, PodRcsZ, side < 0 ? "-x" : "+x", 0.36, Frame);
        p.Set(("fuelTank", Kw(("capacity", 2500))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx + side * r * 0.9, cy, 13.0).Cast<object>()], side < 0 ? "-x" : "+x", "fuel"))));
        return p.Stats(3500, 6000, 25, 10.0);
    }

    /// <summary>A saddle tank on the hull's flank forward: fuel for the forward lift jet slung under it.</summary>
    private static Part Saddle(int side)
    {
        var p = new Part(side < 0 ? "Rust Bucket Saddle Tank L" : "Rust Bucket Saddle Tank R", Small, "Rust bucket", "A saddle tank on the rust bucket's flank: fuel for the forward lift jet under it.", role: "fueltank", tool: "welder", material: "steel", color: HullPaint, group: Group);
        double cx = Cx + side * SadX, cy = SadY, r = SadR;
        Nacelle(p, cx, cy, [(3.7, 0.22), (4.4, r), (7.3, r), (8.0, 0.3)], 1, "dirty steel", "dirty steel");
        // Two straps round it, a filler cap, and the pylon to the hull on the socket.
        foreach (var z in new[] { 4.9, 6.6 })
            Collar(p, cx, cy, z - 0.05, z + 0.05, r * 1.0, r * 1.06, Frame, "dirty steel");
        Detail(p, cx - 0.08, cy + r * 0.95, 5.6, cx + 0.08, cy + r * 1.12, 5.76, Stripe, "hazard paint");
        var inner = Cx + side * SadInner;
        Solid(p, inner, cy - 0.2, SadZ - 0.5, cx - side * r * 0.7, cy + 0.2, SadZ + 0.5, Frame, "dirty steel");
        Hidden(p, cx - r * 0.9, cy - r * 0.9, 4.4, cx + r * 0.9, cy + r * 0.9, 7.3);
        Sock(p, "hull", inner, cy, SadZ, side < 0 ? "+x" : "-x");
        Sock(p, "lift", cx, cy - r * 0.9, SadZ, "-y");
        // The attitude jets: up between the filler and the aft strap, down behind the lift jet, out on the outboard flat, and a retro on its nose.
        RcsMount(p, "rcsUp", cx, cy + Flat(r), SadRcsZ, "+y", 0.36, Frame);
        RcsMount(p, "rcsDown", cx, cy - Flat(r), SadRcsDownZ, "-y", 0.36, Frame);
        RcsMount(p, "rcsOut", cx + side * Flat(r), cy, SadRcsZ, side < 0 ? "-x" : "+x", 0.36, Frame);
        RcsMount(p, "rcsFore", cx, cy, 3.7, "-z", 0.54, Frame);
        p.Set(("fuelTank", Kw(("capacity", 1000))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx + side * r * 0.9, cy, 6.4).Cast<object>()], side < 0 ? "-x" : "+x", "fuel"))));
        return p.Stats(1200, 3000, 20, 8.0);
    }

    /// <summary>
    /// A mount for an attitude jet (ShipParts.Rcs): a square pad standing <c>0.04</c> m proud of the surface point, its outer face the socket (a socket sits on a solid box's face).
    /// <paramref name="w"/>: the pad's width, a little over the jet's housing.
    /// </summary>
    private static void RcsMount(Part p, string name, double x, double y, double z, string dir, double w, string color)
    {
        var s = dir[0] == '-' ? -1 : 1;
        double[] at = [x, y, z];
        var axis = dir[1] - 'x';
        double[] lo = [x - w / 2, y - w / 2, z - w / 2], hi = [x + w / 2, y + w / 2, z + w / 2];
        lo[axis] = Math.Min(at[axis] - s * 0.11, at[axis] + s * 0.04);
        hi[axis] = Math.Max(at[axis] - s * 0.11, at[axis] + s * 0.04);
        at[axis] += s * 0.04;
        Solid(p, lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], color, "dirty steel");
        Sock(p, name, at[0], at[1], at[2], dir);
    }

    /// <summary>How far an octagon's flat side is from its middle, for corner radius r (the corners at 22.5 degrees off the axes, so the flats face along them).</summary>
    private static double Flat(double r) => Math.Round(r * Math.Cos(Math.PI / 8), 3);

    /// <summary>
    /// An attitude jet: a small housing with a hazard band and a bell, its exhaust away from its base. One part, hung on sockets facing every way: a flight stabilizer
    /// works out what each does, so a ship with them round its ends turns about any axis and stops any way (the Rust Bucket and the Magpie carry them).
    /// </summary>
    internal static Part Rcs(string name, double force, double k)
    {
        var p = new Part(name, Small, "Ship fittings", $"An attitude jet: {force / 1000:0} kN out of a small bell. Fit them round a ship's ends pointing every way, and a flight stabilizer turns it about any axis and stops it any way.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        p.Add(Box([-0.15 * k, -0.16 * k, -0.15 * k], [0.15 * k, -0.003, 0.15 * k], "#3a3836", shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.154 * k, -0.12 * k, -0.154 * k], [0.154 * k, -0.09 * k, 0.154 * k], Stripe, detail: true, shape: Kw(("finish", "hazard paint"))));
        for (var i = 0; i < 8; i++)
            Quad(p, RingY(0.06 * k, -0.16 * k, i), RingY(0.06 * k, -0.16 * k, i + 1), RingY(0.11 * k, -0.3 * k, i + 1), RingY(0.11 * k, -0.3 * k, i), "#2a2624", "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => RingY(0.055 * k, -0.17 * k, i))], "#ff8a3a", glow: true);
        p.Add(Box([-0.11 * k, -0.3 * k, -0.11 * k], [0.11 * k, -0.16 * k, 0.11 * k], hidden: true));
        p.Sock("base", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", force), ("spool", 0.25), ("exhaust", "-y"), ("at", Py.L(0, Py.Round(-0.3 * k, 4), 0)), ("burnRate", Py.Round(force / 1e6 * JetBurn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Math.Round(0.15 * k, 3), Math.Round(-0.05 * k, 3), 0], "+x", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, Py.Round(-0.3 * k, 4), 0)), ("dir", "-y"), ("radius", Py.Round(0.08 * k, 4)), ("length", Py.Round(1.1 * k, 4)), ("kind", "Rocket")))));
        return p.Stats(Math.Round(90 * k * k * k), 1500, 12, 3.0);
    }

    /// <summary>An octagon's corner (part-local, about the origin) at radius rr, height y in the xz plane (a ring across y).</summary>
    private static double[] RingY(double rr, double y, int i)
    {
        var a = Math.PI / 8 + i * Math.PI / 4;
        return [Py.Round(rr * Math.Cos(a), 4), y, Py.Round(rr * Math.Sin(a), 4)];
    }

    /// <summary>The same along z (a ring across z).</summary>
    private static double[] RingZ(double rr, double z, int i)
    {
        var a = Math.PI / 8 + i * Math.PI / 4;
        return [Py.Round(rr * Math.Cos(a), 4), Py.Round(rr * Math.Sin(a), 4), z];
    }

    /// <summary>A main drive: the bell on a pod's back, a glow deep inside. Origin on the pod's end, the bell aft along +z.</summary>
    private static Part Drive()
    {
        var p = new Part("Rust Bucket Drive", Small, "Rust bucket", $"The rust bucket's main drive: {DriveForce / 1000:0} kN from the pod's fuel. Pushes away from its bell.", role: "thruster", tool: "wrench", material: "steel", color: "#2a2624", group: Group);
        for (var i = 0; i < 8; i++)
            Quad(p, RingZ(0.55, 0.0, i), RingZ(0.55, 0.0, i + 1), RingZ(0.72, 0.7, i + 1), RingZ(0.72, 0.7, i), "#2a2624", "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => RingZ(0.5, 0.1, i))], "#ff8a3a", glow: true);
        p.Add(Box([-0.5, -0.5, 0], [0.5, 0.5, 0.55], hidden: true));
        p.Sock("front", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", DriveForce), ("spool", 1.2), ("exhaust", "+z"), ("at", Py.L(0, 0, 0.7)), ("burnRate", Py.Round(DriveForce / 1e6 * JetBurn, 4)), ("gimbal", 12), ("gimbalRate", 20))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0.5, 0, 0.3], "+x", "fuel"))));
        Works.Head(p, Works.Table(), "torch", DriveForce, nacelle: true); // (its engine is in the pod behind it: mended through the hatch, on a space walk or landed; ship-works.md 15)
        return p.Stats(1800, 4000, 12, 6.0);
    }

    /// <summary>A lift jet: a shroud under a tank, its nozzle pointing down. Origin on the tank's underside.</summary>
    private static Part LiftJet()
    {
        var p = new Part("Rust Bucket Lift Jet", Small, "Rust bucket", $"A lift jet: {LiftForce / 1000:0} kN straight down from the tank it hangs under.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        // The shroud (its top a little up inside the tank), louvres down its sides, a hazard band.
        p.Add(Box([-0.46, -0.26, -0.5], [0.46, 0.0, 0.5], Frame, shape: Kw(("finish", "dirty steel"))));
        foreach (var x in new[] { -0.3, -0.1, 0.1, 0.3 })
            foreach (var s in new[] { -1, 1 })
                p.Add(Box([x - 0.04, -0.2, s * 0.5 - 0.012], [x + 0.04, -0.04, s * 0.5 + 0.012], "#1c1b1a", detail: true));
        p.Add(Box([-0.475, -0.25, -0.4], [0.475, -0.2, 0.4], Stripe, detail: true, shape: Kw(("finish", "hazard paint"))));
        // The nozzle, flaring down, sooted, with the glow up inside it.
        for (var i = 0; i < 8; i++)
            Quad(p, RingY(0.34, -0.26, i), RingY(0.34, -0.26, i + 1), RingY(0.44, -0.55, i + 1), RingY(0.44, -0.55, i), "#2a2624", "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => RingY(0.3, -0.3, i))], "#ff8a3a", glow: true);
        p.Add(Box([-0.44, -0.55, -0.44], [0.44, -0.26, 0.44], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", LiftForce), ("spool", 1.0), ("exhaust", "-y"), ("at", Py.L(0, -0.55, 0)), ("burnRate", Py.Round(LiftForce / 1e6 * JetBurn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [-0.46, -0.13, 0], "-x", "fuel"))));
        return p.Stats(2500, 4000, 12, 6.0);
    }

    // ---- the legs

    private static Part Leg()
    {
        var p = new Part("Rust Bucket Leg", Small, "Rust bucket", "A landing leg: a raked strut on a broad pad.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        // Origin at the socket on the belly; the pad 0.45 m down, the strut raked out a little.
        Quad(p, [-0.12, 0, -0.12], [0.12, 0, -0.12], [0.18, -0.4, 0.0], [-0.06, -0.4, 0.0], Frame, "dirty steel");
        Quad(p, [-0.12, 0, 0.12], [0.12, 0, 0.12], [0.18, -0.4, 0.24], [-0.06, -0.4, 0.24], Frame, "dirty steel");
        p.Add(Box([-0.1, -0.4, -0.1], [0.1, 0.0, 0.1], "#3a3836", shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.4, -0.5, -0.4], [0.4, -0.4, 0.4], "#2c2a28", shape: Kw(("finish", "rubber"))));
        p.Add(Box([-0.12, -0.42, -0.12], [0.12, -0.38, 0.12], Stripe, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("top", [0, 0, 0], "+y");
        return p.Stats(400, 1500, 20, 4.0);
    }

    // ---- fittings the ship needed that weren't in the game

    private static Part Fitting(string name, string desc, string role = "frame") =>
        new(name, Small, "Ship fittings", desc, role: role, tool: "wrench", material: "steel", color: "#c8ccc6", group: Group);

    /// <summary>Furniture sockets: on the floor, against a wall (its back at local -z, 0.125 m up, as the colony's furniture).</summary>
    private static void FurnitureSockets(Part p, double back, double top)
    {
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, top, 0], "+y");
        p.Sock("wall", [0, 0.125, -back], "-z");
    }

    private static Part Toilet()
    {
        var p = Fitting("Ship Toilet", "A steel toilet with a vacuum flush. Rinse before use.");
        p.Add(Box([-0.2, 0, -0.35], [0.2, 0.4, 0.15], "#c8ccc6", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.22, 0.4, -0.3], [0.22, 0.44, 0.2], "#30302e", detail: true, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.22, 0.4, -0.35], [0.22, 0.85, -0.2], "#c8ccc6", shape: Kw(("finish", "chrome"))));
        p.Add(Box([0.12, 0.7, -0.2], [0.2, 0.76, -0.12], "#d03020", detail: true, shape: Kw(("finish", "plastic"))));
        FurnitureSockets(p, 0.35, 0.85);
        return p.Stats(30, 120, 4, 2.0);
    }

    private static Part Basin()
    {
        var p = Fitting("Ship Basin", "A small steel basin with a mirror. The cold tap works.");
        p.Add(Box([-0.3, 0.75, -0.22], [0.3, 0.9, 0.18], "#c8ccc6", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.05, 0, -0.22], [0.05, 0.75, -0.12], "#8a8e8a", shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.25, 0.86, -0.17], [0.25, 0.91, 0.13], "#5c6a70", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.03, 0.9, -0.2], [0.03, 1.05, -0.12], "#c8ccc6", detail: true, shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.28, 1.15, -0.22], [0.28, 1.7, -0.19], "#a8c0c8", detail: true, glass: true));
        p.Add(Box([-0.05, 0, -0.22], [0.05, 0.02, -0.12], "#8a8e8a", detail: true));
        FurnitureSockets(p, 0.22, 0.9);
        return p.Stats(20, 80, 3, 2.0);
    }

    private static Part Shower()
    {
        var p = Fitting("Shower Stall", "A one-person shower: a tray, a curtain rail and a head that spits.");
        p.Add(Box([-0.45, 0, -0.45], [0.45, 0.08, 0.45], "#c8ccc6", shape: Kw(("finish", "ceramic tiles"))));
        p.Add(Box([-0.45, 0.08, -0.45], [-0.42, 2.1, 0.45], "#c8ccc6", shape: Kw(("finish", "ceramic tiles"))));
        p.Add(Box([0.42, 0.08, -0.45], [0.45, 2.1, 0.45], "#c8ccc6", shape: Kw(("finish", "ceramic tiles"))));
        p.Add(Box([-0.42, 2.06, 0.4], [0.42, 2.1, 0.44], "#8a8e8a", detail: true, shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.42, 0.3, 0.41], [-0.1, 2.06, 0.43], "#9fb8a8", detail: true, shape: Kw(("finish", "fabric"))));
        p.Add(Box([-0.06, 1.9, -0.43], [0.06, 1.98, -0.3], "#c8ccc6", detail: true, shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.1, 0.081, -0.1], [0.1, 0.095, 0.1], "#30302e", detail: true));
        FurnitureSockets(p, 0.45, 2.1);
        return p.Stats(60, 150, 3, 3.0);
    }

    /// <summary>Sockets of the building kit's furniture (Interior: bottom, top, back at half height, the sides), so a ship's own fitting
    /// takes a stock one's place in the blueprint.</summary>
    private static void StockSockets(Part p, double w, double h, double d)
    {
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("back", [0, h / 2, -d / 2], "-z");
        p.Sock("+x", [w / 2, h / 2, 0], "+x");
        p.Sock("-x", [-w / 2, h / 2, 0], "-x");
    }

    private static PyDict Shelf(double[] mn, double[] mx, string label) => new()
    {
        ["slots"] = new List<object?> { new PyDict { ["min"] = mn.Select(v => (object?)Py.Round(v, 3)).ToList(), ["max"] = mx.Select(v => (object?)Py.Round(v, 3)).ToList(), ["label"] = label } },
    };

    /// <summary>The galley's range, built for a ship that pitches (Karl, 2026-11-15: the stock stove was a house's): bolted to the
    /// bulkhead on a plinth, two induction plates with pan clamps and a fiddle rail, a pot strapped down, a dogged oven door, a grab
    /// bar to hold in a bad burn, an extractor hood. The stock Galley Stove's size and sockets, so it hangs where that did.</summary>
    private static Part GalleyRange()
    {
        double w = 1.0, h = 0.9, d = 0.7;
        var p = Fitting("Ship Galley Range", "A galley range bolted to the bulkhead: two plates with pan clamps, a dogged oven and a grab bar for when the drive coughs.", role: "storage");
        const string Dark = "#2a2826", Chrome = "#8a8e8a";
        // The plinth (set back, dark), the carcass in the hull's green, the hob plate.
        LBox(p, -0.46, 0, -0.33, 0.46, 0.1, 0.31, Dark, "dirty steel");
        // (a shell round the oven, so the oven's slot has room: back, sides, floor, the band under the hob, the stiles round the door)
        LBox(p, -0.5, 0.1, -0.35, 0.5, 0.72, -0.31, Patch, "rusty paint");
        foreach (var sx in new[] { -1, 1 })
        {
            LBox(p, sx * 0.46, 0.1, -0.31, sx * 0.5, 0.72, 0.35, Patch, "rusty paint");
            LBox(p, sx * 0.38, 0.14, 0.33, sx * 0.46, 0.72, 0.35, Patch, "rusty paint");
        }
        LBox(p, -0.46, 0.1, -0.31, 0.46, 0.14, 0.35, Patch, "rusty paint");
        LBox(p, -0.38, 0.14, 0.33, 0.38, 0.17, 0.35, Patch, "rusty paint");
        LBox(p, -0.5, 0.72, -0.35, 0.5, 0.86, 0.35, Patch, "rusty paint");
        LBox(p, -0.505, 0.86, -0.35, 0.505, 0.9, 0.355, "#3a3836", "steel");
        // Two induction plates, the right one warm; a pot on the left one under a clamp bar on posts.
        LBox(p, -0.42, 0.9, -0.22, -0.04, 0.904, 0.16, "#1c1b1a", "steel", detail: true);
        LBox(p, 0.04, 0.9, -0.22, 0.42, 0.904, 0.16, "#1c1b1a", "steel", detail: true);
        LBox(p, 0.12, 0.904, -0.14, 0.34, 0.907, 0.08, "#ff7a2a", detail: true, glow: true);
        LBox(p, -0.36, 0.904, -0.17, -0.1, 1.06, 0.11, Chrome, "steel");
        LBox(p, -0.375, 1.06, -0.185, -0.085, 1.08, 0.125, Dark, "dirty steel", detail: true);
        LBox(p, -0.4, 1.08, -0.045, -0.06, 1.095, -0.015, Stripe, "hazard paint", detail: true);
        foreach (var x in new[] { -0.415, -0.06 })
            LBox(p, x - 0.012, 0.904, -0.05, x + 0.012, 1.095, -0.01, Frame, "dirty steel", detail: true);
        // The fiddle rail round the front and sides, on posts.
        LBox(p, -0.48, 0.975, 0.315, 0.48, 0.995, 0.335, Chrome, "chrome", detail: true);
        foreach (var sx in new[] { -1, 1 })
        {
            LBox(p, sx * 0.462, 0.972, -0.3, sx * 0.482, 0.992, 0.312, Chrome, "chrome", detail: true);
            LBox(p, sx * 0.47 - 0.008, 0.9, 0.318, sx * 0.47 + 0.008, 0.975, 0.332, Chrome, "chrome", detail: true);
        }
        // The control strip (knobs, a red ready light), the oven door with its window, dogs at the corners, a lever latch.
        LBox(p, -0.45, 0.74, 0.35, 0.45, 0.84, 0.372, Dark, "dirty steel", detail: true);
        foreach (var x in new[] { -0.3, -0.15, 0.0 })
            LBox(p, x - 0.025, 0.765, 0.372, x + 0.025, 0.815, 0.392, "#c8ccc6", "plastic", detail: true);
        LBox(p, 0.28, 0.775, 0.372, 0.32, 0.805, 0.38, "#ff3020", detail: true, glow: true);
        LBox(p, -0.4, 0.17, 0.35, 0.4, 0.7, 0.374, "#3a3f45", "painted steel", detail: true);
        LBox(p, -0.25, 0.36, 0.374, 0.25, 0.56, 0.378, "#3a3020", detail: true);
        foreach (var x in new[] { -0.37, 0.37 })
            foreach (var y in new[] { 0.2, 0.67 })
                LBox(p, x - 0.025, y - 0.02, 0.374, x + 0.025, y + 0.02, 0.392, Frame, "dirty steel", detail: true);
        LBox(p, 0.22, 0.6, 0.374, 0.34, 0.64, 0.41, Stripe, "hazard paint", detail: true);
        // The grab bar on stand-offs; bolts through the plinth's flange; a rust streak from a bolt.
        LBox(p, -0.42, 0.875, 0.43, 0.42, 0.9, 0.455, Chrome, "chrome", detail: true);
        foreach (var x in new[] { -0.4, 0.4 })
            LBox(p, x - 0.012, 0.876, 0.355, x + 0.012, 0.899, 0.43, Chrome, "chrome", detail: true);
        foreach (var x in new[] { -0.42, 0.42 })
        {
            LBox(p, x - 0.03, 0.03, 0.31, x + 0.03, 0.07, 0.33, Frame, "dirty steel", detail: true);
            LBox(p, x - 0.01, 0.1, 0.35, x + 0.01, 0.3, 0.354, Rust, "rust", detail: true);
        }
        // The splashback up the bulkhead and the extractor hood over the plates, its grille and hazard edge.
        LBox(p, -0.5, 0.9, -0.35, 0.5, 1.5, -0.32, Chrome, "steel");
        LBox(p, 0.18, 0.93, -0.319, 0.22, 1.45, -0.316, Rust, "rust", detail: true);
        LBox(p, -0.5, 1.5, -0.35, 0.5, 1.75, 0.05, Frame, "dirty steel");
        LBox(p, -0.4, 1.496, -0.28, 0.4, 1.5, 0.0, "#1c1b1a", "dirty steel", detail: true);
        LBox(p, -0.505, 1.51, 0.05, 0.505, 1.55, 0.056, Stripe, "hazard paint", detail: true);
        p.Set(("storage", Shelf([-0.37, 0.18, -0.3], [0.37, 0.7, 0.32], "oven")));
        StockSockets(p, w, h, d);
        return p.Stats(120, 300, 4, 2.0);
    }

    /// <summary>The galley's table: a laminate top with a raised lip so nothing slides off, on two posts bolted to the deck, a shelf
    /// between them. The stock Mess Table's size and sockets.</summary>
    private static Part MessTable()
    {
        double w = 1.8, h = 0.75, d = 0.9;
        var p = Fitting("Ship Mess Table", "A crew table bolted to the deck, with a lip round the edge: what is set on it stays on it.", role: "storage");
        LBox(p, -0.9, 0.7, -0.45, 0.9, 0.75, 0.45, "#6b7068", "plastic");
        LBox(p, -0.9, 0.75, 0.42, 0.9, 0.775, 0.45, Frame, "dirty steel", detail: true);
        LBox(p, -0.9, 0.75, -0.45, 0.9, 0.775, -0.42, Frame, "dirty steel", detail: true);
        foreach (var sx in new[] { -1, 1 })
        {
            LBox(p, sx * 0.87, 0.75, -0.42, sx * 0.9, 0.772, 0.42, Frame, "dirty steel", detail: true);
            var x = sx * 0.55;
            LBox(p, x - 0.06, 0.05, -0.06, x + 0.06, 0.7, 0.06, Frame, "dirty steel");
            LBox(p, x - 0.18, 0, -0.18, x + 0.18, 0.05, 0.18, "#2a2826", "dirty steel");
            foreach (var bx in new[] { -0.13, 0.13 })
                foreach (var bz in new[] { -0.13, 0.13 })
                    LBox(p, x + bx - 0.02, 0.05, bz - 0.02, x + bx + 0.02, 0.07, bz + 0.02, "#8a8e8a", "steel", detail: true);
            // A worn patch where elbows go.
            LBox(p, sx * 0.3 - 0.2, 0.75, 0.12, sx * 0.3 + 0.2, 0.752, 0.38, "#7d8279", "worn paint", detail: true);
        }
        LBox(p, -0.49, 0.3, -0.3, 0.49, 0.33, 0.3, Frame, "dirty steel");
        p.Set(("storage", Shelf([-0.45, 0.34, -0.28], [0.45, 0.66, 0.28], "shelf")));
        StockSockets(p, w, h, d);
        return p.Stats(70, 200, 3, 2.0);
    }

    /// <summary>The rust bucket's flight couch (Karl, 2026-11-15: "incredibly worn ... spring coming out from the bottom, worn fabric.
    /// I'm thinking winnebago vibe"): an old camper's captain's chair on a swivel pedestal, in faded mustard with brown, orange and cream
    /// stripes, a split cushion showing its foam, duct tape on the backrest, a headrest knocked askew, padded armrests (one worn through),
    /// a taped stick and a frayed lap belt. One spring has come up through the cushion. The Seat's
    /// sockets and seat height, so it stands where a Seat did.</summary>
    private static Part WornCouch()
    {
        var p = Fitting("Worn Flight Couch", "A camper's old captain's chair bolted to a swivel post. A spring is coming through the seat, the fabric is older than you, and it is the most comfortable seat aboard. Link it to a Driving or Turret Module.", role: "seat");
        const string Fabric = "#a8875a", Brown = "#6b4226", Orange = "#b8602e", Cream = "#d6c69c", Foam = "#dcc77a", Tape = "#9a9c98", Chrome = "#8a8e8a", Dark = "#2a2826";
        // The pedestal: a foot plate bolted down, the post with a rust streak, the swivel plate, two side rails left of the seat pan.
        LBox(p, -0.26, 0, -0.26, 0.26, 0.04, 0.26, Dark, "dirty steel");
        foreach (var bx in new[] { -0.21, 0.21 })
            foreach (var bz in new[] { -0.21, 0.21 })
                LBox(p, bx - 0.02, 0.04, bz - 0.02, bx + 0.02, 0.055, bz + 0.02, Chrome, "steel", detail: true);
        LBox(p, -0.07, 0.04, -0.07, 0.07, 0.3, 0.07, Chrome, "steel");
        LBox(p, -0.02, 0.06, -0.074, 0.0, 0.26, -0.07, Rust, "rust", detail: true);
        LBox(p, -0.2, 0.3, -0.2, 0.2, 0.33, 0.2, Frame, "dirty steel");
        foreach (var sx in new[] { -1, 1 })
            LBox(p, sx * 0.27, 0.33, -0.28, sx * 0.3, 0.366, 0.3, Frame, "dirty steel");
        // The cushion, sagging at the front, a worn patch where you sit and a split with the foam showing.
        LBox(p, -0.3, 0.37, -0.3, 0.3, 0.52, 0.28, Fabric, "fabric");
        LBox(p, -0.18, 0.508, -0.2, 0.16, 0.525, 0.12, "#c4a878", "fabric", detail: true);
        LBox(p, 0.04, 0.51, -0.285, 0.17, 0.527, -0.21, Foam, "rubber", detail: true);
        LBox(p, -0.12, 0.33, -0.306, 0.1, 0.368, -0.292, Fabric, "fabric", detail: true); // (the torn bottom hanging at the front)
        // The lap belt lies across the cushion, its buckle undone, the other end hanging down the side.
        LBox(p, -0.302, 0.51, -0.13, 0.2, 0.53, -0.07, "#7a2f2a", "fabric", detail: true);
        LBox(p, 0.2, 0.512, -0.14, 0.26, 0.536, -0.06, Chrome, "chrome", detail: true);
        LBox(p, -0.316, 0.24, -0.13, -0.299, 0.527, -0.07, "#7a2f2a", "fabric", detail: true);
        // The backrest with the camper's stripes across its face, duct tape over a split, a patch on its back.
        LBox(p, -0.3, 0.52, 0.2, 0.3, 1.2, 0.38, Fabric, "fabric");
        foreach (var (y0, y1, c) in new[] { (0.68, 0.74, Brown), (0.74, 0.79, Orange), (0.79, 0.83, Cream), (0.83, 0.88, Orange), (0.88, 0.94, Brown) })
            LBox(p, -0.297, y0, 0.194, 0.297, y1, 0.2, c, "fabric", detail: true);
        LBox(p, -0.27, 1.0, 0.184, -0.07, 1.04, 0.2, Tape, "plastic", detail: true);
        LBox(p, -0.19, 0.945, 0.18, -0.15, 1.12, 0.197, Tape, "plastic", detail: true);
        LBox(p, 0.1, 0.56, 0.38, 0.26, 0.8, 0.384, "#5a6a4a", "fabric", detail: true);
        // The headrest on two posts, knocked to one side.
        foreach (var x in new[] { -0.07, 0.13 })
            LBox(p, x - 0.012, 1.19, 0.285, x + 0.012, 1.215, 0.309, Chrome, "chrome", detail: true);
        LBox(p, -0.14, 1.207, 0.23, 0.2, 1.39, 0.36, Fabric, "fabric");
        LBox(p, -0.137, 1.27, 0.226, 0.197, 1.31, 0.23, Orange, "fabric", detail: true);
        // The armrests on their brackets; the right one worn through to the foam, a stick on the left with tape round its grip.
        foreach (var sx in new[] { -1, 1 })
        {
            LBox(p, sx * 0.32 - 0.015, 0.37, -0.18, sx * 0.32 + 0.015, 0.665, -0.14, Chrome, "steel", detail: true);
            LBox(p, sx * 0.31, 0.66, -0.25, sx * 0.37, 0.72, 0.18, Brown, "fabric");
        }
        LBox(p, 0.315, 0.72, -0.1, 0.365, 0.724, 0.06, Foam, "rubber", detail: true);
        LBox(p, -0.355, 0.72, -0.235, -0.325, 0.9, -0.205, Dark, "rubber", detail: true);
        LBox(p, -0.358, 0.79, -0.238, -0.322, 0.82, -0.202, Tape, "plastic", detail: true);
        LBox(p, -0.348, 0.9, -0.228, -0.332, 0.906, -0.212, "#ff3020", detail: true, glow: true);
        // One spring come up through the split in the cushion, right where you sit (Karl: "just one spring").
        for (var i = 0; i < 4; i++)
        {
            double x = 0.105 + i * 0.006, z = -0.2475, y = 0.545 + i * 0.03;
            if (i % 2 == 0)
                LBox(p, x - 0.04, y - 0.01, z - 0.01, x + 0.04, y + 0.01, z + 0.01, Chrome, "steel", detail: true);
            else
                LBox(p, x - 0.01, y - 0.01, z - 0.03, x + 0.01, y + 0.01, z + 0.03, Chrome, "steel", detail: true);
            // (the wire between rings: a detail box grows to 16 mm, so the rings are 20 mm and no face lines up)
            if (i < 3)
                LBox(p, x - 0.008, y - 0.006 - (i == 0 ? 0.035 : 0), z - 0.008, x + 0.008, y + 0.03, z + 0.008, Rust, "rust", detail: true);
        }
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 0.6, 0.38], "+z");
        p.Set(("seat", Kw(("at", Py.L(0, -0.03, 0)))));
        return p.Stats(40, 140, 3, 2.0);
    }

    private static Part GrowRack()
    {
        var p = Fitting("Grow Rack 2 m", "Three trays of hydroponic greens under lamps, fed from a drip line. Lettuce, mostly.");
        p.Add(Box([-1.0, 0, -0.3], [-0.95, 2.0, 0.3], "#8a8e8a", shape: Kw(("finish", "steel"))));
        p.Add(Box([0.95, 0, -0.3], [1.0, 2.0, 0.3], "#8a8e8a", shape: Kw(("finish", "steel"))));
        foreach (var y in new[] { 0.3, 0.95, 1.6 })
        {
            p.Add(Box([-0.95, y, -0.28], [0.95, y + 0.1, 0.28], "#e0e0d8", shape: Kw(("finish", "plastic"))));
            for (var i = 0; i < 6; i++)
            {
                var x = -0.8 + i * 0.32;
                var h = 0.16 + 0.05 * ((i * 7 + (int)(y * 10)) % 3);
                p.Add(Box([x - 0.12, y + 0.1, -0.2], [x + 0.12, y + 0.1 + h, 0.2], i % 2 == 0 ? Leaf : Grow, detail: true, shape: Kw(("finish", "fabric"))));
            }
            p.Add(Box([-0.9, y + 0.5, -0.08], [0.9, y + 0.53, 0.08], "#f0b0f0", detail: true, shape: Kw(("glow", true))));
        }
        p.Add(Box([-0.95, 1.95, -0.3], [0.95, 1.98, -0.26], "#3a6080", detail: true, shape: Kw(("finish", "rubber"))));
        FurnitureSockets(p, 0.3, 2.0);
        return p.Stats(70, 200, 3, 3.0);
    }

    private static Part EngineBlock()
    {
        var p = Fitting("Ship Engine Block", "The rust bucket's main drive core: a fusion bottle older than its pilot, held together with clamps and hope.", role: "frame");
        p.Set(("tool", "welder"));
        p.Add(Box([-1.0, 0, -0.9], [1.0, 0.3, 0.9], "#2c2a28", shape: Kw(("finish", "dirty steel"))));
        // The bottle: an octagonal drum on its side, along z, banded.
        double[] O(double z, double r, int i)
        {
            var a = Math.PI / 8 + i * Math.PI / 4;
            return [r * Math.Cos(a), 1.15 + r * Math.Sin(a), z];
        }
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(-0.85, 0.8, i))], [.. Enumerable.Range(0, 8).Select(i => O(0.85, 0.8, i))], "#6a5a48", "rusty paint");
        p.Add(Box([-0.55, 0.3, -0.55], [0.55, 1.95, 0.55], hidden: true));
        foreach (var z in new[] { -0.5, 0.0, 0.5 })
            Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(z - 0.05, 0.84, i))], [.. Enumerable.Range(0, 8).Select(i => O(z + 0.05, 0.84, i))], Stripe, "hazard paint");
        p.Add(Box([-0.2, 1.9, -0.2], [0.2, 2.25, 0.2], "#3a3836", shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.08, 2.25, -0.08], [0.08, 2.6, 0.08], "#8a8e8a", shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.9, 0.6, 0.86], [-0.5, 1.0, 0.9], "#40ff90", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([0.5, 0.6, 0.86], [0.9, 0.7, 0.9], "#ffb030", detail: true, shape: Kw(("glow", true))));
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(3000, 2500, 30, 12.0);
    }

    // ---- the storeys dealt out into pieces (Karl, 2026-11-15: a hit takes a panel, not a deck). Each box carries the piece it was
    // drawn for (or "~hull": placed by where it stands, or nothing: the storey's own piece). Each socket goes to the piece whose box
    // face it is on, and wherever two pieces' solid boxes touch they get a facing socket pair: one pair places a piece, the game
    // welds the rest (ConstructBuild.AutoLink), so a piece knocked off leaves the rest welded to each other.

    private static readonly Dictionary<string, Part> Pieces = [];
    private static readonly Dictionary<(string Part, string Sock), string> SockOwner = []; // (a storey's socket: the piece that holds it now)

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    private static Part Piece(string name)
    {
        if (Pieces.TryGetValue(name, out var p))
            return p;
        var what = name["Rust Bucket ".Length..];
        var desc = what switch
        {
            "Keel" => "The rust bucket's lower deck: the hold's and the engine room's floor, the belly and the legs' mounts.",
            "Deck" => "The rust bucket's upper deck: the floor of the cockpit, the cabins, hydroponics and the galley.",
            "Roof" => "The rust bucket's roof: the upper deck's ceiling, the eaves and the spine, and the mounts for the gear on it.",
            "Chin" => "The rust bucket's chin: the hull either side of the cargo ramp, round the front of the hold.",
            "Canopy" => "The rust bucket's nose: the bridge front over the ramp, the windscreen, the side windows and the cockpit's front.",
            "Tail" => "The rust bucket's tail behind the rooms: the heat exchanger, the fins and the tail lights.",
            _ when what.StartsWith("Frame") => $"One of the rust bucket's hoop frames ({what[6..].ToLowerInvariant()}): a girder right round the hull. Tough.",
            _ when what.StartsWith("Partition") => "A partition of the rust bucket's rooms, with its doorways.",
            _ => $"A run of the rust bucket's hull ({what.ToLowerInvariant()}): the skin, its plates and rails, and the wall behind them.",
        };
        p = new Part(name, Small, "Rust bucket", desc, role: "frame", tool: "welder", material: "steel", color: what.StartsWith("Partition") ? Wall : HullPaint, group: Group);
        Pieces[name] = p;
        return p;
    }

    /// <summary>The piece a "~hull" box of storey level goes to, by where it stands.</summary>
    private static string Where(PyDict b, int level)
    {
        double[] mn = Vd(b, "min"), mx = Vd(b, "max");
        double xc = (mn[0] + mx[0]) / 2, zc = (mn[2] + mx[2]) / 2;
        if (!Drawn(b))
            for (var i = 0; i < FrameZ.Length; i++)
                if (mn[2] >= FrameZ[i] - 0.105 && mx[2] <= FrameZ[i] + 0.105)
                    return $"Rust Bucket Frame {FrameNames[i]}";
        var deck = level == 0 ? "Lower" : "Upper";
        if (zc < 2.95)
            return level == 0 ? "Rust Bucket Chin" : "Rust Bucket Canopy";
        if (zc > Back + 0.04)
            return "Rust Bucket Tail";
        if (zc > 14.05)
            return $"Rust Bucket {deck} Stern";
        return $"Rust Bucket {deck} {(xc < Cx ? "Port" : "Stbd")} {(zc < 8.5 ? "Fore" : "Aft")}";
    }

    /// <summary>Deals a storey's boxes out to the pieces and its sockets to the piece whose face each is on.</summary>
    private static void Split(Part storey, int level)
    {
        var own = (string)storey.D["name"]!;
        foreach (var b in storey.Boxes)
        {
            var to = Tags.GetValueOrDefault(b);
            var name = to == null ? own : to == HullTag ? Where(b, level) : to;
            Piece(name).Boxes.Add(b);
        }
        foreach (var sk in storey.Sockets)
        {
            var at = ((List<object?>)sk["at"]!).Select(v => Convert.ToDouble(v) + H).ToArray();
            var dir = (string)sk["dir"]!;
            var ax = dir[1] - 'x';
            var plus = dir[0] == '+';
            Part? owner = null, drawnOwner = null;
            foreach (var q in Pieces.Values)
                foreach (var b in q.Boxes)
                {
                    double[] mn = Vd(b, "min"), mx = Vd(b, "max");
                    if (Math.Abs((plus ? mx[ax] : mn[ax]) - at[ax]) > 0.005)
                        continue;
                    var inside = true;
                    for (var a = 0; a < 3; a++)
                        if (a != ax && (at[a] < mn[a] - 0.005 || at[a] > mx[a] + 0.005))
                            inside = false;
                    if (!inside)
                        continue;
                    if (Drawn(b))
                        drawnOwner ??= q;
                    else
                        owner ??= q;
                }
            owner ??= drawnOwner ?? Piece(own);
            owner.Sockets.Add(sk);
            SockOwner[(own, (string)sk["name"]!)] = (string)owner.D["name"]!;
        }
    }

    /// <summary>The pieces' stats: the frames toughest, then the floors, the hull runs, the partitions; the mass near the first hull's.</summary>
    private static void PieceStats()
    {
        foreach (var (name, p) in Pieces)
        {
            var what = name["Rust Bucket ".Length..];
            _ = what switch
            {
                "Keel" => p.Stats(26000, 14000, 30, 20.0),
                "Deck" => p.Stats(12000, 9000, 25, 20.0),
                "Roof" => p.Stats(5000, 5000, 20, 12.0),
                "Canopy" => p.Stats(2500, 2500, 20, 8.0),
                "Chin" => p.Stats(1500, 2500, 25, 8.0),
                "Tail" => p.Stats(3500, 4000, 25, 8.0),
                _ when what.StartsWith("Frame") => p.Stats(2200, 30000, 40, 8.0),
                _ when what.StartsWith("Partition") => p.Stats(400, 1200, 15, 4.0),
                _ when what.EndsWith("Stern") => p.Stats(1100, 3000, 25, 6.0),
                _ when what.StartsWith("Lower") => p.Stats(1800, 3500, 30, 6.0),
                _ => p.Stats(1600, 3500, 25, 6.0),
            };
        }
    }

    /// <summary>A facing socket pair wherever two of the parts' solid boxes touch face to face (one pair a pair of parts, on their
    /// biggest contact), unless they already meet at one: what welds the pieces to each other.</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var count = parts.Select(q => q.Sockets.Count(s => ((string)s["name"]!).StartsWith('j'))).ToArray();
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                if (Meet(parts[i], parts[j]))
                    continue;
                (double Area, double[] At, int Axis, bool IPlus) best = (0, [], 0, false);
                foreach (var A in solid[i])
                    foreach (var Bx in solid[j])
                        for (var ax = 0; ax < 3; ax++)
                        {
                            bool iPlus = Math.Abs(A.Mx[ax] - Bx.Mn[ax]) < 0.001, iMinus = Math.Abs(A.Mn[ax] - Bx.Mx[ax]) < 0.001;
                            if (!iPlus && !iMinus)
                                continue;
                            var at = new double[3];
                            var area = 1.0;
                            var ok = true;
                            for (var a = 0; a < 3 && ok; a++)
                            {
                                if (a == ax)
                                {
                                    at[a] = iPlus ? A.Mx[a] : A.Mn[a];
                                    continue;
                                }
                                double lo = Math.Max(A.Mn[a], Bx.Mn[a]), hi = Math.Min(A.Mx[a], Bx.Mx[a]);
                                if (hi - lo < 0.03)
                                    ok = false;
                                at[a] = Math.Round((lo + hi) / 2, 2);
                                area *= hi - lo;
                            }
                            if (ok && area > best.Area)
                                best = (area, at, ax, iPlus);
                        }
                if (best.Area <= 0)
                    continue;
                var axis = "xyz"[best.Axis];
                Sock(parts[i], $"j{count[i]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "+" : "-") + axis);
                Sock(parts[j], $"j{count[j]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "-" : "+") + axis);
            }
    }

    /// <summary>Whether two parts (at one origin) already have a socket of each at one point, facing each other.</summary>
    private static bool Meet(Part a, Part b)
    {
        foreach (var s in a.Sockets)
            foreach (var t in b.Sockets)
            {
                var (pa, pb) = ((List<object?>)s["at"]!, (List<object?>)t["at"]!);
                if (Enumerable.Range(0, 3).All(i => Math.Abs(Convert.ToDouble(pa[i]) - Convert.ToDouble(pb[i])) < 0.004))
                {
                    var (da, db) = ((string)s["dir"]!, (string)t["dir"]!);
                    if (da[1] == db[1] && da[0] != db[0])
                        return true;
                }
            }
        return false;
    }

    /// <summary>The order to place the structure in: out from the first part over the socket pairs they meet at.</summary>
    private static List<string> PlaceOrder(List<Part> parts, string first)
    {
        var names = parts.Select(q => (string)q.D["name"]!).ToList();
        var seen = new HashSet<int> { names.IndexOf(first) };
        var order = new List<string> { first };
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            for (var j = 0; j < parts.Count; j++)
                if (!seen.Contains(j) && Meet(parts[i], parts[j]))
                {
                    seen.Add(j);
                    order.Add(names[j]);
                    queue.Enqueue(j);
                }
        }
        if (order.Count != parts.Count)
            throw new InvalidOperationException("Rust bucket: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    // ---- the blueprint

    private static List<string> Structure = [];
    private static Dictionary<string, int> PieceSteps = [];

    /// <summary>The step that holds socket sock of step on: a storey's piece that took it when the storey was split, else on itself.</summary>
    private static int Holder(BuildingParts.Blueprint bp, int on, string sock) =>
        bp.Steps[on]["part"] is string part && SockOwner.TryGetValue((part, sock), out var piece) ? PieceSteps[piece] : on;

    private static int Hang(BuildingParts.Blueprint bp, string part, int on, string sock, string by, int spin = 0) => bp.Hang(part, Holder(bp, on, sock), sock, by, spin);

    /// <summary>A ship's flight stabilizer settings: Ship keys (FlightPattern.Ship), the nose on the mouse.</summary>
    internal static PyDict ShipFlight() => new() { ["keys"] = "Ship", ["mouseAim"] = true };

    private static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static int Lamp(BuildingParts.Blueprint bp, int on, string sock, string colour = LampGlow)
    {
        var s = Hang(bp, "Bulkhead Light", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    private static void Switch(BuildingParts.Blueprint bp, int on, string sock, params int[] lamps)
    {
        var s = Hang(bp, "Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = true };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
    }

    private static BuildingParts.Blueprint Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint("Rust Bucket", "Your ship: a patched two-deck tramp with a blunt bridge over its cargo ramp, a cockpit, two cabins, hydroponics, a galley, a hold and an engine room.", defs, maxSteps: 200);
        bp.Conduit = true; // (its lamp wires run along the walls and over the ceilings, not across the rooms)
        double[] zero = [0, 0, 0];
        var steps = new Dictionary<string, int>();
        foreach (var name in Structure)
            steps[name] = steps.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = steps;
        int keel = steps["Rust Bucket Keel"], deck = steps["Rust Bucket Deck"], roof = steps["Rust Bucket Roof"];
        var pods = new[] { Hang(bp, "Rust Bucket Pod L", keel, "podL", "hull"), Hang(bp, "Rust Bucket Pod R", keel, "podR", "hull") };
        var saddles = new[] { Hang(bp, "Rust Bucket Saddle Tank L", keel, "saddleL", "hull"), Hang(bp, "Rust Bucket Saddle Tank R", keel, "saddleR", "hull") };
        foreach (var pod in pods)
        {
            bp.Hang("Rust Bucket Drive", pod, "drive", "front");
            bp.Hang("Drive Core S", pod, "core", "front"); // (its engine, inside the housing on the drive's axis, bolted to the tail plate)
            Works.HangHatch(bp, pod, pod == pods[0] ? -1 : 1);
            bp.Hang("Rust Bucket Lift Jet", pod, "lift", "top");
        }
        var foreLift = saddles.Select(t => bp.Hang("Rust Bucket Lift Jet", t, "lift", "top")).ToArray();
        // Attitude jets pointing every way at both ends, so it turns about any axis and stops any way; heavy retros on the saddle tanks' noses to brake.
        foreach (var t in pods.Concat(saddles))
            foreach (var sock in new[] { "rcsUp", "rcsDown", "rcsOut" })
                bp.Hang("RCS Thruster", t, sock, "base");
        foreach (var t in saddles)
            bp.Hang("Heavy RCS Thruster", t, "rcsFore", "base");
        foreach (var n in new[] { "legFL", "legFR", "legBL", "legBR" })
            Hang(bp, "Rust Bucket Leg", keel, n, "top");
        Hang(bp, "Rust Bucket Ramp", keel, "ramp", "hang");
        Hang(bp, "Steel Wide Stair 3 m", keel, "stair", "b0_0");
        // The gear on the roof.
        Paint(bp, Hang(bp, "Rust Bucket Deck Cargo", roof, "cargo", "bottom"), "#3f5e6a", "rusty paint");
        Hang(bp, "Rust Bucket Crane", roof, "crane", "bottom");
        Hang(bp, "Rust Bucket Mast", roof, "mast", "bottom");
        Hang(bp, "Long Range Sensor", roof, "sensor", "-y"); // (the radar's scope in every seat; no missile rack, so no lock: ConstructRadar.CanLock)
        Hang(bp, "Rust Bucket Radiator L", roof, "radL", "root");
        Hang(bp, "Rust Bucket Radiator R", roof, "radR", "root");
        // The hold: shelving down the port side, a tool rack and a locker opposite, two crates by the stair.
        for (var i = 0; i < 3; i++)
            Paint(bp, Hang(bp, "Store Shelving 2 m", keel, $"shelfL{i}", "wall"), "#6b6f5a", "worn paint");
        Hang(bp, "Tool Rack", keel, "rackR", "back");
        Paint(bp, Hang(bp, "Locker", keel, "lockerR", "wall"), "#5d7468", "worn paint");
        Hang(bp, "Requisition Crate", keel, "crate0", "-y");
        Hang(bp, "Requisition Crate", keel, "crate1", "-y");
        // The engine room. (The Engineer's Panel is where the breakdowns are seen to: GameSandboxBreakdowns reads its name.)
        Hang(bp, "Ship Engine Block", keel, "engine", "bottom");
        Hang(bp, "Engineer's Panel", keel, "panelEng", "back");
        Hang(bp, "Power Board", keel, "boardEng", "back");
        Hang(bp, "Pipe Bank", keel, "pipesEngL", "back");
        Paint(bp, Hang(bp, "Battery", keel, "battery", "-z"), "#3a3d3f");
        Switch(bp, keel, "switchHoldL", Lamp(bp, keel, "lampHoldL0"), Lamp(bp, keel, "lampHoldL1"));
        Switch(bp, keel, "switchHoldR", Lamp(bp, keel, "lampHoldR0"), Lamp(bp, keel, "lampHoldR1"));
        Switch(bp, keel, "switchEng", Lamp(bp, keel, "lampEngL"), Lamp(bp, keel, "lampEngR"));
        // The cockpit: the pilot's seat flies it through a flight stabilizer under the dashboard, on Ship keys with the mouse on the nose.
        var pilot = Hang(bp, "Worn Flight Couch", deck, "seatL", "bottom");
        Hang(bp, "Worn Flight Couch", deck, "seatR", "bottom");
        var stab = Hang(bp, "Flight Stabilizer", deck, "module", "-y"); // (a small gyro: the attitude jets turn it)
        bp.Steps[stab]["config"] = ShipFlight();
        bp.Wires.Add((pilot, stab));
        Hang(bp, "Chart Table", deck, "chart", "back");
        // The bedroom, the bathroom.
        Paint(bp, Hang(bp, "Iron Bed", deck, "bed", "wall"), "#6b4a2e", "worn paint");
        Paint(bp, Hang(bp, "Locker", deck, "locker", "wall"), "#8c7b52", "worn paint");
        Hang(bp, "Steel Desk", deck, "desk", "wall");
        Hang(bp, "Shower Stall", deck, "shower", "wall");
        Hang(bp, "Ship Basin", deck, "basin", "wall");
        Hang(bp, "Ship Toilet", deck, "toilet", "wall");
        // Hydroponics, the galley.
        foreach (var g in new[] { "growL0", "growL1", "growM0", "growM1" })
            Hang(bp, "Grow Rack 2 m", deck, g, "wall");
        Hang(bp, "Ship Galley Range", deck, "stove", "back");
        Hang(bp, "Ship Mess Table", deck, "table", "back");
        Hang(bp, "Medicine Cabinet", deck, "cabinet", "back");
        Hang(bp, "Galley Coffee Station", deck, "coffee", "wall");
        // Doors.
        foreach (var d in new[] { "doorCockpit", "doorBed", "doorBath", "doorHydro", "doorGalley" })
            Paint(bp, Hang(bp, "Steel Swing Door 1.5x2.25 m", deck, d, "hang"), d == "doorBath" ? "#c9ccc4" : "#5d7468", "worn paint");
        // Lamps: forward and aft switches by the corridor's ends.
        Switch(bp, deck, "switchFwd", Lamp(bp, deck, "lampCockpit"), Lamp(bp, deck, "lampBed"), Lamp(bp, deck, "lampBath"), Lamp(bp, deck, "lampCorrA"));
        Switch(bp, deck, "switchAft", Lamp(bp, deck, "lampHydroA", "#f0c8f0"), Lamp(bp, deck, "lampHydroB", "#f0c8f0"), Lamp(bp, deck, "lampCorrB"), Lamp(bp, deck, "lampGalley"));
        return bp;
    }

    public void Make(PartOutput o)
    {
        Pieces.Clear();
        SockOwner.Clear();
        Tags.Clear();
        PartitionCount = 0;
        Part[] storeys = [Keel(), DeckPart(), Roof()];
        for (var i = 0; i < storeys.Length; i++)
            Split(storeys[i], i);
        PieceStats();
        var structure = Pieces.Values.ToList();
        Connect(structure);
        Structure = PlaceOrder(structure, "Rust Bucket Keel");
        List<Part> parts =
        [
            .. structure, Ramp(), Pod(-1), Pod(1), Saddle(-1), Saddle(1), Drive(), LiftJet(), Rcs("RCS Thruster", RcsForce, 1.0), Rcs("Heavy RCS Thruster", HeavyRcsForce, 1.6), Leg(), Radiator(-1), Radiator(1), DeckCargo(), Crane(), Mast(),
            Toilet(), Basin(), Shower(), GalleyRange(), MessTable(), WornCouch(), GrowRack(), EngineBlock(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            if (p.Boxes.Count > 256 || p.Sockets.Count > 64)
                throw new InvalidOperationException($"Rust bucket: {p.D["name"]} has {p.Boxes.Count} boxes and {p.Sockets.Count} sockets");
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        var bp = Blueprint(defs);
        o.File($"{BpOut}/RustBucket.json", PyJson.Dumps(bp.Json(), 2) + "\n");
    }
}
