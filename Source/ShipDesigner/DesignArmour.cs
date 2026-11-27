using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Stand-off armour plates (Karl, 2026-11-27: "giant armor plates, held off the hull from an arm... more akin to a wall"): a plate that
/// follows its hull's shape at a set stand-off, held off the skin on struts. It is dropped on a body's skin like any piece; its middle is
/// where it was dropped, its width runs along the body (z) and its height round the profile (metres of arc at its middle), and every point
/// of it lies on the skin's normal there, its stand-off out, so it bends with the hull as the skin does. Its outer face takes the hull
/// cladding styles (the same <see cref="DesignScene.LayPlates"/> the bodies and connections clad with); its inner, ship-facing face is
/// plain panels. In the game the plate and its rams' rods ride a slow piston (its leaf, DesignParts.Armour.cs) and the rams' sockets are one convex hull each (ArmourHulls).
/// All its geometry, JSON and page fields are here, so the rest of the designer only calls in.
/// </summary>
public static class DesignArmour
{
    public const string Kind = "Armour plate";
    public const double MinW = 1, MaxW = 80, DefW = 8, MinH = 1, MaxH = 40, DefH = 4, MinT = 0.05, MaxT = 2, DefT = 0.3, MinOff = 0.3, MaxOff = 8, DefOff = 1.2;
    public const string DefClad = "Plates";
    public const double DefCladSize = 1.5;
    /// <summary>How it is held off (Karl, 2026-11-27: "a thick structural or a set of three or four in a line"): one thick arm at its
    /// middle, or a row of three or four posts along its middle.</summary>
    public static readonly string[] StrutKinds = ["Arm", "Row of 3", "Row of 4"];
    public const string DefStruts = "Row of 3";
    /// <summary>The utilities console that works plates by its buttons (Karl, 2026-11-27: physical in-game buttons, each plate set to one):
    /// its kind's name, its buttons, and the leaf its first button is drawn in (DesignFurnishings.UtilitiesConsole).</summary>
    public const string Console = "Utilities console";
    public const int ConsoleButtons = 8, ButtonLeaf = 10;
    /// <summary>The outline's control points along each long edge (Karl, 2026-11-27: "look at the panel from a 2D perspective and sculpt"):
    /// metres each edge stands out past (or, below 0, in from) the plate's height, at <see cref="EdgePoints"/> points evenly along it.</summary>
    public const int EdgePoints = 5;
    public const double EdgeMax = 20, MinEdgeGap = 0.5;

    /// <summary>An edge's offset at share v (0..1) along the plate, between its control points.</summary>
    public static double EdgeAt(double[] e, double v)
    {
        if (e.Length == 0)
            return 0;
        if (e.Length == 1)
            return e[0];
        var q = Math.Clamp(v, 0, 1) * (e.Length - 1);
        var k = Math.Min(e.Length - 2, (int)Math.Floor(q));
        return e[k] + (e[k + 1] - e[k]) * (q - k);
    }

    private static double[] ReadEdge(JsonNode? n, double h)
    {
        var o = new double[EdgePoints];
        if (n is JsonArray a)
            for (var k = 0; k < EdgePoints && k < a.Count; k++)
                o[k] = Math.Clamp(ShipDesign.F(a[k], 0), -h / 2 + MinEdgeGap / 2, EdgeMax);
        return o;
    }

    private static JsonArray WriteEdge(double[] e) => new([.. e.Select(x => (JsonNode)ShipDesign.R(x))]);
    /// <summary>Struts about this far apart (m) each way, at least two each way on a plate long enough.</summary>
    private const double ArmHalf = 0.6, PostHalf = 0.32;
    /// <summary>The inner face's panels: about this size (m), the seam between them, and how far they stand off the plate's back (no shared plane).</summary>
    private const double PanelSize = 1.25, PanelSeam = 0.03, PanelRise = 0.006;
    /// <summary>How much of a cladding cell or inner panel must lie inside a sculpted plate's outline to be laid (cut to the edge).</summary>
    private const double MinInside = 0.4;
    private const string Back = "#3b3f44", Panel = "#62676d";

    public static readonly PieceKind[] Kinds =
    [
        new(Kind, "Armour", "skin", null, "", "A giant stand-off armour plate held off the hull on struts. It follows the hull's shape at its stand-off; set its width (along), height (round the hull), thickness and stand-off. Its outer face takes the hull cladding styles; its inner face is plain panels.", _ => { }),
    ];

    public static bool Is(Piece p) => p.Kind == Kind;

    public static JsonObject Defaults() => new() { ["armW"] = DefW, ["armH"] = DefH, ["armT"] = DefT, ["armOff"] = DefOff, ["armClad"] = DefClad, ["armCladSize"] = DefCladSize, ["armStruts"] = DefStruts, ["armFollow"] = 1, ["armButton"] = 0 };

    /// <summary>A plate's JSON fields (only a plate's: every other piece's JSON stays as it was, so its design's hash does).</summary>
    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (!Is(p))
            return o;
        o["armW"] = ShipDesign.R(p.ArmW);
        o["armH"] = ShipDesign.R(p.ArmH);
        o["armT"] = ShipDesign.R(p.ArmT);
        o["armOff"] = ShipDesign.R(p.ArmOff);
        o["armClad"] = p.ArmClad;
        o["armCladSize"] = ShipDesign.R(p.ArmCladSize);
        o["armStruts"] = p.ArmStruts;
        if (p.ArmButton > 0)
            o["armButton"] = p.ArmButton;
        if (p.ArmTop.Any(x => x != 0) || p.ArmBot.Any(x => x != 0))
        {
            o["armTop"] = WriteEdge(p.ArmTop);
            o["armBot"] = WriteEdge(p.ArmBot);
        }
        if (p.ArmFollow < 1)
            o["armFollow"] = ShipDesign.R(p.ArmFollow);
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        if (!Is(p))
            return p;
        p.ArmW = Math.Clamp(ShipDesign.F(o["armW"], DefW), MinW, MaxW);
        p.ArmH = Math.Clamp(ShipDesign.F(o["armH"], DefH), MinH, MaxH);
        p.ArmT = Math.Clamp(ShipDesign.F(o["armT"], DefT), MinT, MaxT);
        p.ArmOff = Math.Clamp(ShipDesign.F(o["armOff"], DefOff), MinOff, MaxOff);
        p.ArmClad = HullBody.CladKinds.Contains((string?)o["armClad"]) ? (string)o["armClad"]! : DefClad;
        p.ArmCladSize = Math.Clamp(ShipDesign.F(o["armCladSize"], DefCladSize), HullBody.CladSizeMin, HullBody.CladSizeMax);
        p.ArmStruts = StrutKinds.Contains((string?)o["armStruts"]) ? (string)o["armStruts"]! : DefStruts;
        p.ArmTop = ReadEdge(o["armTop"], p.ArmH);
        p.ArmBot = ReadEdge(o["armBot"], p.ArmH);
        p.ArmFollow = Math.Clamp(ShipDesign.F(o["armFollow"], 1), 0, 1);
        p.ArmButton = (int)Math.Clamp(ShipDesign.F(o["armButton"], 0), 0, ConsoleButtons);
        return p;
    }

    /// <summary>The page's setPiece fields; an error text, or empty.</summary>
    public static string Set(Piece p, JsonObject o)
    {
        if (!Is(p))
            return "";
        static double Snap(double v, double s) => Math.Round(v / s) * s;
        if (o["armW"] is not null) p.ArmW = Math.Clamp(Snap(ShipDesign.F(o["armW"], DefW), 0.05), MinW, MaxW);
        if (o["armH"] is not null) p.ArmH = Math.Clamp(Snap(ShipDesign.F(o["armH"], DefH), 0.05), MinH, MaxH);
        if (o["armT"] is not null) p.ArmT = Math.Clamp(Snap(ShipDesign.F(o["armT"], DefT), 0.01), MinT, MaxT);
        if (o["armOff"] is not null) p.ArmOff = Math.Clamp(Snap(ShipDesign.F(o["armOff"], DefOff), 0.05), MinOff, MaxOff);
        if (o["armCladSize"] is not null) p.ArmCladSize = Math.Clamp(Snap(ShipDesign.F(o["armCladSize"], DefCladSize), 0.05), HullBody.CladSizeMin, HullBody.CladSizeMax);
        if (o["armButton"] is not null) p.ArmButton = (int)Math.Clamp(Math.Round(ShipDesign.F(o["armButton"], 0)), 0, ConsoleButtons);
        if (o["armFollow"] is not null) p.ArmFollow = Math.Clamp(Snap(ShipDesign.F(o["armFollow"], 1), 0.05), 0, 1);
        if (o["armTop"] is JsonArray) p.ArmTop = ReadEdge(o["armTop"], p.ArmH);
        if (o["armBot"] is JsonArray) p.ArmBot = ReadEdge(o["armBot"], p.ArmH);
        // One control point dragged in the plate screen: which edge ("top" or "bot"), which point and its offset.
        if ((string?)o["armEdge"] is { } edge && o["k"] is not null && o["at"] is not null)
        {
            var e = edge == "top" ? p.ArmTop : edge == "bot" ? p.ArmBot : null;
            var k = (int)ShipDesign.F(o["k"], -1);
            if (e is null || k < 0 || k >= e.Length)
                return "no such outline point";
            e[k] = Math.Clamp(Snap(ShipDesign.F(o["at"], 0), 0.05), -p.ArmH / 2 + MinEdgeGap / 2, EdgeMax);
        }
        // The plate screen's Reset: its outline square and following the hull again.
        if ((bool?)o["armReset"] == true)
        {
            p.ArmTop = new double[EdgePoints];
            p.ArmBot = new double[EdgePoints];
            p.ArmFollow = 1;
        }
        if ((string?)o["armStruts"] is { } st)
        {
            if (!StrutKinds.Contains(st))
                return $"an armour plate is held by one of these: {string.Join(", ", StrutKinds)}";
            p.ArmStruts = st;
        }
        if ((string?)o["armClad"] is { } c)
        {
            if (!HullBody.CladKinds.Contains(c))
                return $"an armour plate is clad one of these ways: {string.Join(", ", HullBody.CladKinds)}";
            p.ArmClad = c;
        }
        return "";
    }

    /// <summary>The page's plate fields.</summary>
    public static string SceneJson(Piece p) => !Is(p) ? "" :
        new StringBuilder(",\"armour\":true,\"armW\":").Append(ShipDesign.S(p.ArmW)).Append(",\"armH\":").Append(ShipDesign.S(p.ArmH))
            .Append(",\"armT\":").Append(ShipDesign.S(p.ArmT)).Append(",\"armOff\":").Append(ShipDesign.S(p.ArmOff))
            .Append(",\"armClad\":").Append(DesignScene.Q(p.ArmClad)).Append(",\"armCladSize\":").Append(ShipDesign.S(p.ArmCladSize)).Append(",\"armStruts\":").Append(DesignScene.Q(p.ArmStruts))
            .Append(",\"armFollow\":").Append(ShipDesign.S(p.ArmFollow)).Append(",\"armButton\":").Append(p.ArmButton)
            .Append(",\"armTop\":[").AppendJoin(',', p.ArmTop.Select(ShipDesign.S)).Append("],\"armBot\":[").AppendJoin(',', p.ArmBot.Select(ShipDesign.S)).Append(']').ToString();

    // ---- where it lies

    /// <summary>
    /// A plate's grid in the ship's frame: (Nz + 1) x (Nt + 1) points on the skin (S) with the way out of it (N, smoothed over the profile's
    /// corners so the plate rounds them at its stand-off), row i along z, column j round the profile; and its length and arc in metres.
    /// </summary>
    public sealed record Grid(int Nz, int Nt, double[][] S, double[][] N, double Len, double Arc, double[][] K, double[] A0, double[] A1, double[] ArcTab)
    {
        /// <summary>The outline's lowest and highest edge round the profile, in metres of arc (rows reach between their own A0 and A1).</summary>
        public double Amin { get; } = A0.Min();
        public double Amax { get; } = A1.Max();

        /// <summary>A row's own share u for a point at share <paramref name="w"/> of the whole outline's span (Amin..Amax, so it keeps metres
        /// whatever the row's height) at share v along; clamped to the row's own edges, so what lies past a sculpted edge is cut to it.</summary>
        public double LocalU(double w, double v)
        {
            var (lo, hi) = RowAt(v);
            double t0 = TOf(ArcTab, lo), t1 = TOf(ArcTab, hi);
            if (t1 - t0 < 1e-9)
                return 0.5;
            return (TOf(ArcTab, Math.Clamp(Amin + (Amax - Amin) * w, lo, hi)) - t0) / (t1 - t0);
        }

        /// <summary>The share of the span w0..w1 (shares of Amin..Amax) inside the outline at share v along.</summary>
        public double Inside(double w0, double w1, double v)
        {
            var (lo, hi) = RowAt(v);
            double a0 = Amin + (Amax - Amin) * w0, a1 = Amin + (Amax - Amin) * w1;
            return a1 - a0 < 1e-9 ? 0 : Math.Max(0, Math.Min(a1, hi) - Math.Max(a0, lo)) / (a1 - a0);
        }

        private (double Lo, double Hi) RowAt(double v)
        {
            var (i, fv) = Cell(v, Nz);
            return (A0[i] + (A0[i + 1] - A0[i]) * fv, A1[i] + (A1[i + 1] - A1[i]) * fv);
        }

        /// <summary>The skin itself under a point at shares (u, v): where a ram stands (S is the plate's base, off the skin when it doesn't follow the hull).</summary>
        public double[] LerpK(double u, double v)
        {
            var (j, fu) = Cell(u, Nt);
            var (i, fv) = Cell(v, Nz);
            double[] P(int a, int b) => K[a * (Nt + 1) + b];
            var r = new double[3];
            for (var k = 0; k < 3; k++)
                r[k] = (P(i, j)[k] * (1 - fu) + P(i, j + 1)[k] * fu) * (1 - fv) + (P(i + 1, j)[k] * (1 - fu) + P(i + 1, j + 1)[k] * fu) * fv;
            return r;
        }

        public double[] At(int i, int j, double off) { var (s, n) = (S[i * (Nt + 1) + j], N[i * (Nt + 1) + j]); return [s[0] + n[0] * off, s[1] + n[1] * off, s[2] + n[2] * off]; }
        public double[] Nrm(int i, int j) => N[i * (Nt + 1) + j];

        /// <summary>A point at shares (u round, v along; 0..1) off the skin by <paramref name="off"/>, bilinear between the grid's points.</summary>
        public double[] Lerp(double u, double v, double off)
        {
            var (j, fu) = Cell(u, Nt);
            var (i, fv) = Cell(v, Nz);
            var a = At(i, j, off); var b = At(i, j + 1, off); var c = At(i + 1, j, off); var d = At(i + 1, j + 1, off);
            var r = new double[3];
            for (var k = 0; k < 3; k++)
                r[k] = (a[k] * (1 - fu) + b[k] * fu) * (1 - fv) + (c[k] * (1 - fu) + d[k] * fu) * fv;
            return r;
        }

        public double[] LerpN(double u, double v)
        {
            var (j, fu) = Cell(u, Nt);
            var (i, fv) = Cell(v, Nz);
            var r = new double[3];
            for (var k = 0; k < 3; k++)
                r[k] = (Nrm(i, j)[k] * (1 - fu) + Nrm(i, j + 1)[k] * fu) * (1 - fv) + (Nrm(i + 1, j)[k] * (1 - fu) + Nrm(i + 1, j + 1)[k] * fu) * fv;
            return Norm(r);
        }

        private static (int, double) Cell(double s, int n)
        {
            var q = Math.Clamp(s, 0, 1) * n;
            var i = Math.Min(n - 1, (int)Math.Floor(q));
            return (i, q - i);
        }
    }

    /// <summary>A plate's grid (null for any other piece, one off the skin, or one with no room on its body).</summary>
    public static Grid? GridOf(ShipDesign d, Piece p)
    {
        if (!Is(p) || p.Surface != "skin" || d.Hull(p.Hull) is not { } h)
            return null;
        var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
        double z0 = Math.Clamp(p.Z - p.ArmW / 2, h.Front + 0.05, h.Back - 0.05), z1 = Math.Clamp(p.Z + p.ArmW / 2, h.Front + 0.05, h.Back - 0.05);
        if (z1 - z0 < 0.2)
            return null;
        // The profile's arc at the plate's middle, to turn its height in metres into a share of t (each row keeps the same shares).
        const int K = 240;
        var arc = new double[K + 1];
        var prev = h.OnSkin(p.Z, 0, p.Side);
        for (var k = 1; k <= K; k++)
        {
            var s = h.OnSkin(p.Z, (double)k / K, p.Side);
            arc[k] = arc[k - 1] + Math.Sqrt((s.X - prev.X) * (s.X - prev.X) + (s.Y - prev.Y) * (s.Y - prev.Y));
            prev = s;
        }
        var total = arc[K];
        if (total < 0.2)
            return null;
        double TAt(double a) => TOf(arc, a);
        var mid = arc[(int)Math.Round(Math.Clamp(p.T, 0, 1) * K)];
        // Its outline (Karl, 2026-11-27: sculpted in the plate screen): each row along it runs round from its own bottom edge to its own top
        // edge, the plate's height grown (or cut) there by the edges' control points.
        int nz = Math.Clamp((int)Math.Ceiling((z1 - z0) / 0.6), 1, 64);
        var ra0 = new double[nz + 1];
        var ra1 = new double[nz + 1];
        var most = 0.0;
        for (var i = 0; i <= nz; i++)
        {
            var v = (double)i / nz;
            double lo = Math.Clamp(mid - p.ArmH / 2 - EdgeAt(p.ArmBot, v), 0, total), hi = Math.Clamp(mid + p.ArmH / 2 + EdgeAt(p.ArmTop, v), 0, total);
            if (hi - lo < MinEdgeGap)
            {
                var c = Math.Clamp((lo + hi) / 2, MinEdgeGap / 2, total - MinEdgeGap / 2);
                (lo, hi) = (c - MinEdgeGap / 2, c + MinEdgeGap / 2);
            }
            (ra0[i], ra1[i]) = (lo, hi);
            most = Math.Max(most, hi - lo);
        }
        if (most < 0.2)
            return null;
        int nt = Math.Clamp((int)Math.Ceiling(most / 0.3), 1, 96);
        var S = new double[(nz + 1) * (nt + 1)][];
        var N = new double[S.Length][];
        var Ks = new double[S.Length][];
        // Flat (Follow 0) to following the hull (1): the plate's base blended between the skin and the plane through the skin at its middle.
        var f = Math.Clamp(p.ArmFollow, 0, 1);
        var sm = h.OnSkin(p.Z, TAt(mid), p.Side);
        double[] om = [cx + sm.X, h.Y + sm.Y, h.Z + sm.Z], nm = Norm([sm.Nx, sm.Ny, 0]);
        var sumH = 0.0;
        for (var i = 0; i <= nz; i++)
        {
            var z = z0 + (z1 - z0) * i / nz;
            double t0 = TAt(ra0[i]), t1 = TAt(ra1[i]);
            sumH += ra1[i] - ra0[i];
            var eps = 0.5 * (t1 - t0) / nt;
            for (var j = 0; j <= nt; j++)
            {
                var t = t0 + (t1 - t0) * j / nt;
                var s = h.OnSkin(z, t, p.Side);
                // Its way out: the skin's either side of it, so a corner of the profile is rounded at the stand-off, not split.
                var sa = h.OnSkin(z, Math.Max(0, t - eps), p.Side);
                var sb = h.OnSkin(z, Math.Min(1, t + eps), p.Side);
                double[] k = [cx + s.X, h.Y + s.Y, h.Z + s.Z];
                var n = Norm([s.Nx + sa.Nx + sb.Nx, s.Ny + sa.Ny + sb.Ny, 0]);
                var idx = i * (nt + 1) + j;
                Ks[idx] = k;
                if (f >= 1)
                    (S[idx], N[idx]) = (k, n);
                else
                {
                    var flat = Add(k, nm, -Dot(Sub(k, om), nm));
                    S[idx] = [flat[0] + (k[0] - flat[0]) * f, flat[1] + (k[1] - flat[1]) * f, flat[2] + (k[2] - flat[2]) * f];
                    N[idx] = Norm([nm[0] + (n[0] - nm[0]) * f, nm[1] + (n[1] - nm[1]) * f, nm[2] + (n[2] - nm[2]) * f]);
                }
            }
        }
        return new Grid(nz, nt, S, N, z1 - z0, sumH / (nz + 1), Ks, ra0, ra1, arc);
    }

    /// <summary>The profile's share t at <paramref name="a"/> metres of arc round it (<paramref name="arc"/>: the arc at K + 1 even steps of t).</summary>
    private static double TOf(double[] arc, double a)
    {
        var K = arc.Length - 1;
        a = Math.Clamp(a, 0, arc[K]);
        var k = 1;
        while (k < K && arc[k] < a) k++;
        var f = arc[k] - arc[k - 1] < 1e-9 ? 0 : (a - arc[k - 1]) / (arc[k] - arc[k - 1]);
        return (k - 1 + f) / K;
    }

    /// <summary>What a plate's mesh depends on beyond its own fields and the bodies' shapes: its body's skin colour and cladding material.</summary>
    public static string Key(ShipDesign d, Piece p)
    {
        if (!Is(p) || d.Hull(p.Hull) is not { } h)
            return "";
        // (the plates further out on its body: their rams pass through it in sleeves, Sleeves)
        var sb = new StringBuilder("|ar").Append(h.Skin);
        foreach (var q in d.Pieces)
            if (q != p && Is(q) && q.Hull == p.Hull && q.ArmOff > p.ArmOff)
                sb.Append('|').Append(q.ToJson().ToJsonString());
        return sb.ToString();
    }

    // ---- the drawing

    /// <summary>Draws a plate (in the ship's frame): its outer face clad, its inner face panelled, its rim and its struts; false for any other piece.</summary>
    public static bool Draw(ShipDesign d, Piece p, DesignMesh m)
    {
        if (!Is(p))
            return false;
        if (GridOf(d, p) is not { } g || d.Hull(p.Hull) is not { } h)
            return true; // (no room on its body: nothing drawn)
        m.World();
        double off = p.ArmOff, top = p.ArmOff + p.ArmT;
        var skin = DesignMesh.Hex(h.Skin);
        var face = DesignMesh.Tone(skin, 0.92);
        var rim = DesignMesh.Tone(skin, 0.75);
        var back = DesignMesh.Hex(Back);
        int nz = g.Nz, nt = g.Nt;
        // The plate and its rams' rods (the arms) are leaf 0, which the game pushes out on the rams and lets go (DesignParts.Pieces.cs,
        // ArmourLeaf); the rams' sockets (bases and barrels) stay on the hull.
        m.Leaf(PlateLeaf, () =>
        {
        for (var i = 0; i < nz; i++)
            for (var j = 0; j < nt; j++)
            {
                var n = g.Nrm(i, j);
                Face(m, g.At(i, j, top), g.At(i + 1, j, top), g.At(i + 1, j + 1, top), g.At(i, j + 1, top), n, face);
                Face(m, g.At(i, j, off), g.At(i + 1, j, off), g.At(i + 1, j + 1, off), g.At(i, j + 1, off), [-n[0], -n[1], -n[2]], back);
            }
        // The rim: the plate's four edges, each facing out of the plate.
        for (var i = 0; i < nz; i++)
        {
            Face(m, g.At(i, 0, off), g.At(i + 1, 0, off), g.At(i + 1, 0, top), g.At(i, 0, top), Sub(g.At(i, 0, off), g.At(i, 1, off)), rim);
            Face(m, g.At(i, nt, off), g.At(i + 1, nt, off), g.At(i + 1, nt, top), g.At(i, nt, top), Sub(g.At(i, nt, off), g.At(i, nt - 1, off)), rim);
        }
        for (var j = 0; j < nt; j++)
        {
            Face(m, g.At(0, j, off), g.At(0, j + 1, off), g.At(0, j + 1, top), g.At(0, j, top), Sub(g.At(0, j, off), g.At(1, j, off)), rim);
            Face(m, g.At(nz, j, off), g.At(nz, j + 1, off), g.At(nz, j + 1, top), g.At(nz, j, top), Sub(g.At(nz, j, off), g.At(nz - 1, j, off)), rim);
        }
        InnerPanels(m, g, off);
        if (p.ArmClad != "Off")
            Clad(m, p, g, top, skin);
        Arms(m, g, p, off);
        Sleeves(d, m, g, p, off, top);
        });
        Sockets(m, g, p, off);
        return true;
    }

    /// <summary>The inner face's plain panels (Karl: "the side facing the ship will be a simple panel cladding style"): a grid of flat panels
    /// about <see cref="PanelSize"/> each, seamed, standing <see cref="PanelRise"/> in off the plate's back, each bent with the plate.</summary>
    private static void InnerPanels(DesignMesh m, Grid g, double off)
    {
        // Laid at their real size over the whole outline's span and cut at its edges (Karl, 2026-11-27: cladding shouldn't stretch with a
        // sculpted plate); one mostly past an edge is left off.
        var span = g.Amax - g.Amin;
        int pu = Math.Max(1, (int)Math.Round(span / PanelSize)), pv = Math.Max(1, (int)Math.Round(g.Len / PanelSize));
        double su = PanelSeam / 2 / span, sv = PanelSeam / 2 / g.Len;
        var col = DesignMesh.Hex(Panel);
        for (var a = 0; a < pu; a++)
            for (var b = 0; b < pv; b++)
            {
                double u0 = (double)a / pu + su, u1 = (double)(a + 1) / pu - su, v0 = (double)b / pv + sv, v1 = (double)(b + 1) / pv - sv;
                if (g.Inside(u0, u1, (v0 + v1) / 2) < MinInside)
                    continue;
                // Each panel in steps no longer than the grid's, so it follows the plate's curve.
                int ku = Math.Max(1, (int)Math.Ceiling((u1 - u0) * g.Nt)), kv = Math.Max(1, (int)Math.Ceiling((v1 - v0) * g.Nz));
                var tone = DesignMesh.Tone(col, 0.92 + 0.08 * ((a * 7 + b * 3) % 3) / 2.0);
                for (var x = 0; x < ku; x++)
                    for (var y = 0; y < kv; y++)
                    {
                        double ua = u0 + (u1 - u0) * x / ku, ub = u0 + (u1 - u0) * (x + 1) / ku, va = v0 + (v1 - v0) * y / kv, vb = v0 + (v1 - v0) * (y + 1) / kv;
                        double[] Q(double u, double v) => g.Lerp(g.LocalU(u, v), v, off - PanelRise);
                        var n = g.LerpN(g.LocalU((ua + ub) / 2, (va + vb) / 2), (va + vb) / 2);
                        Face(m, Q(ua, va), Q(ub, va), Q(ub, vb), Q(ua, vb), [-n[0], -n[1], -n[2]], tone);
                    }
            }
    }

    /// <summary>The outer face clad in a hull cladding style (Plates, Heavy or Hex), the bodies' own plate layer laid over the plate's face.</summary>
    private static void Clad(DesignMesh m, Piece p, Grid g, double top, (byte, byte, byte) skin)
    {
        var size = Math.Clamp(p.ArmCladSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        // Laid at its real size over the whole outline's span (u is a share of Amin..Amax, not of each row's own height) and cut at the
        // sculpted edges (Karl, 2026-11-27: "armour cladding shouldn't stretch"); a cell mostly past an edge is left bare.
        var span = g.Amax - g.Amin;
        int nu = Math.Clamp((int)Math.Round(span / size), 1, 64), nv = Math.Clamp((int)Math.Round(g.Len / size), 1, 64);
        var taken = new bool[nu, nv];
        for (var a = 0; a < nu; a++)
            for (var b = 0; b < nv; b++)
                taken[a, b] = g.Inside((double)a / nu, (double)(a + 1) / nu, (b + 0.5) / nv) < MinInside;
        (double X, double Y, double Z) P(double u, double v) { var q = g.Lerp(g.LocalU(u, v), v, top); return (q[0], q[1], q[2]); }
        (double X, double Y, double Z) Nrm(double u, double v) { var q = g.LerpN(g.LocalU(u, v), v); return (q[0], q[1], q[2]); }
        (double, double, double) W(double u, double v, double o) { var lu = g.LocalU(u, v); var q = g.Lerp(lu, v, top); var n = g.LerpN(lu, v); return (q[0] + n[0] * o, q[1] + n[1] * o, q[2] + n[2] * o); }
        DesignScene.LayPlates(m, nu, nv, span, g.Len, new Random(unchecked(p.Id * 7919 + 4111)), p.ArmClad == "Heavy", 1, skin, P, Nrm, W, taken, hex: p.ArmClad == "Hex");
    }

    /// <summary>Where its struts stand: shares (u round, v along) of the grid and each one's half width. An arm: one at its middle; a row:
    /// three or four along its middle, in from its ends.</summary>
    public static List<(double U, double V, double Half)> StrutSpots(Grid g, Piece p)
    {
        var o = new List<(double, double, double)>();
        if (p.ArmStruts == "Arm")
        {
            o.Add((0.5, 0.5, Math.Min(ArmHalf, 0.2 * Math.Min(g.Arc, g.Len))));
            return o;
        }
        var n = p.ArmStruts == "Row of 4" ? 4 : 3;
        var half = Math.Min(PostHalf, 0.2 * Math.Min(g.Arc, g.Len / n));
        for (var b = 0; b < n; b++)
            o.Add((0.5, (b + 0.5) / n, half));
        return o;
    }

    /// <summary>How far a ram's rod runs (m): a short stroke, slow and heavy (Karl, 2026-11-27: "thick, powerful and slow arms, the kind which
    /// have tiny travel"). Its chrome shows this much plus a little, so the stroke reads.</summary>
    public const double RamTravel = 0.25;

    /// <summary>The plate's leaf in its mesh (the plate and the rams' rods).</summary>
    public const int PlateLeaf = 0;

    /// <summary>Seconds the rams take over their stroke: slow and heavy.</summary>
    public const double RamSeconds = 6;

    /// <summary>A ram's heights over the skin at its spot: its base's top, its barrel's top (the socket the rod runs out of) and the head plate's foot.</summary>
    private static (double BaseTop, double BarrelTop, double Head0) RamHeights(double hw, double off)
    {
        double baseTop = 0.08 + hw * 0.15, head0 = off - 0.12, rod = Math.Min(RamTravel + 0.1, Math.Max(0.05, (head0 - baseTop) * 0.35));
        return (baseTop, Math.Max(baseTop + 0.05, head0 - rod), head0);
    }

    /// <summary>
    /// The rams' sockets on the hull (Karl, 2026-11-27: thick, powerful, slow arms with tiny travel): a heavy base on the skin with clevis
    /// lugs, a thick barrel and a gland ring, which the arm's rod runs out of; an arm's barrel braced fore and aft by gussets at its foot.
    /// </summary>
    private static void Sockets(DesignMesh m, Grid g, Piece p, double off)
    {
        foreach (var (u, v, hw) in StrutSpots(g, p))
        {
            var (s, n, along, x, len) = StrutFrame(g, u, v, off);
            m.Frame(s[0], s[1], s[2], x, n, along);
            var (baseTop, barrelTop, _) = RamHeights(hw, len);
            m.Box(-hw * 1.9, -0.05, -hw * 1.9, hw * 1.9, baseTop, hw * 1.9, PieceKinds.Armour); // (its base, sunk 5 cm into the skin)
            m.Box(-hw * 1.25, baseTop - 0.004, -hw * 0.35, -hw * 0.9, baseTop + hw * 0.9, hw * 0.35, PieceKinds.Gun); // (clevis lugs either side)
            m.Box(hw * 0.9, baseTop - 0.004, -hw * 0.35, hw * 1.25, baseTop + hw * 0.9, hw * 0.35, PieceKinds.Gun);
            m.Frustum(baseTop - 0.01, hw * 0.85, barrelTop, hw * 0.85, 16, PieceKinds.Gun); // (the barrel)
            m.Frustum(barrelTop - 0.08, hw * 0.95, barrelTop + 0.01, hw * 0.92, 16, PieceKinds.Armour); // (its gland ring)
            m.Frustum(baseTop + 0.15, hw * 0.88, baseTop + 0.2, hw * 0.88, 16, PieceKinds.Amber, cap0: false, cap1: false); // (a warning band)
            if (p.ArmStruts == "Arm")
            {
                var gh = Math.Min((barrelTop - baseTop) * 0.6, hw * 2);
                m.Box(-hw * 0.2, baseTop - 0.004, hw * 0.8, hw * 0.2, baseTop + gh, hw * 1.85, PieceKinds.Gun);
                m.Box(-hw * 0.2, baseTop - 0.004, -hw * 1.85, hw * 0.2, baseTop + gh, -hw * 0.8, PieceKinds.Gun);
            }
        }
        m.World();
    }

    /// <summary>The arms that go with the plate: each ram's chrome rod, from a stroke's length down in its barrel up to the head plate under the
    /// armour (so pushed out by <see cref="RamTravel"/> it still sits in its gland), and the head.</summary>
    private static void Arms(DesignMesh m, Grid g, Piece p, double off)
    {
        foreach (var (u, v, hw) in StrutSpots(g, p))
        {
            var (s, n, along, x, len) = StrutFrame(g, u, v, off);
            m.Frame(s[0], s[1], s[2], x, n, along);
            var (baseTop, barrelTop, head0) = RamHeights(hw, len);
            m.Frustum(Math.Max(baseTop + 0.02, barrelTop - RamTravel - 0.05), hw * 0.45, head0 + 0.01, hw * 0.45, 12, PieceKinds.Pale, cap0: false); // (the chrome rod)
            m.Box(-hw * 1.4, head0, -hw * 1.4, hw * 1.4, len - 0.002 - PanelRise, hw * 1.4, PieceKinds.Armour); // (its head, under the panels)
        }
        m.World();
    }

    /// <summary>A ram's frame at shares (u, v): its foot on the skin itself, its axis the plate's normal there, and its height L from the skin
    /// to the plate's back along that axis (the stand-off where the plate follows the hull; more or less where it doesn't).</summary>
    /// <summary>
    /// Plates overlapping at different stand-offs (Karl, 2026-11-27: "overlap for different panels... two different stand-off levels and
    /// struts can meet the hull"): a plate further out on the same body keeps its rams on the skin, so where one passes through this plate
    /// it runs in a sleeve, a collar through this plate's thickness round the rod, drawn with this plate.
    /// </summary>
    private static void Sleeves(ShipDesign d, DesignMesh m, Grid g, Piece p, double off, double top)
    {
        var cell = Math.Max(g.Len / g.Nz, g.Arc / g.Nt);
        foreach (var q in d.Pieces)
        {
            if (q == p || !Is(q) || q.Hull != p.Hull || q.ArmOff <= p.ArmOff || q.Copy != p.Copy || q.Side != p.Side || GridOf(d, q) is not { } gq)
                continue;
            foreach (var (u, v, hw) in StrutSpots(gq, q))
            {
                var (k, n, along, x, _) = StrutFrame(gq, u, v, q.ArmOff);
                // The nearest of this plate's skin points to the ram's foot: the ram passes through this plate only if that is well inside it.
                int bi = -1, bj = -1;
                var best = double.MaxValue;
                for (var i = 0; i <= g.Nz; i++)
                    for (var j = 0; j <= g.Nt; j++)
                    {
                        var dd = Dist2(g.K[i * (g.Nt + 1) + j], k);
                        if (dd < best)
                            (best, bi, bj) = (dd, i, j);
                    }
                if (bi < 0 || Math.Sqrt(best) > cell || bi == 0 || bj == 0 || bi == g.Nz || bj == g.Nt)
                    continue;
                double t0 = Dot(Sub(g.At(bi, bj, off), k), n), t1 = Dot(Sub(g.At(bi, bj, top), k), n);
                m.Frame(k[0], k[1], k[2], x, n, along);
                m.Frustum(t0 - 0.04, hw * 1.3, t1 + 0.04, hw * 1.3, 16, PieceKinds.Armour);
                m.Frustum(t1 + 0.02, hw * 1.45, t1 + 0.06, hw * 1.45, 16, PieceKinds.Gun); // (its lip on the outer face)
            }
        }
        m.World();
    }

    private static double Dist2(double[] a, double[] b) => (a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]);

    private static (double[] S, double[] N, double[] Along, double[] X, double L) StrutFrame(Grid g, double u, double v, double off)
    {
        var s = g.LerpK(u, v);
        var n = g.LerpN(u, v);
        var along = Norm(Sub(g.Lerp(u, Math.Min(1, v + 0.01), 0), g.Lerp(u, Math.Max(0, v - 0.01), 0)));
        along = Norm(Add(along, n, -Dot(along, n)));
        var l = Math.Max(0.3, Dot(Sub(g.Lerp(u, v, off), s), n));
        return (s, n, along, Cross(n, along), l);
    }

    // ---- solid in the game

    /// <summary>The plate's own solid as point sets in the ship's frame, each one convex hull: a cell of the plate (its back and face points,
    /// about <see cref="CellSize"/> each way, at most <see cref="MaxCells"/>). Its rods are left out: thin, and they would reach to the skin.</summary>
    public static List<List<double[]>> PlateCells(ShipDesign d, Piece p)
    {
        var o = new List<List<double[]>>();
        if (GridOf(d, p) is not { } g)
            return o;
        double off = p.ArmOff, top = p.ArmOff + p.ArmT;
        int cu = Math.Clamp((int)Math.Round(g.Arc / CellSize), 1, g.Nt), cv = Math.Clamp((int)Math.Round(g.Len / CellSize), 1, g.Nz);
        while (cu * cv > MaxCells)
            if (cu >= cv) cu--; else cv--;
        for (var a = 0; a < cu; a++)
            for (var b = 0; b < cv; b++)
            {
                int j0 = g.Nt * a / cu, j1 = g.Nt * (a + 1) / cu, i0 = g.Nz * b / cv, i1 = g.Nz * (b + 1) / cv;
                var pts = new List<double[]>();
                for (var i = i0; i <= i1; i++)
                    for (var j = j0; j <= j1; j++)
                    {
                        pts.Add(g.At(i, j, off));
                        pts.Add(g.At(i, j, top));
                    }
                o.Add(pts);
            }
        return o;
    }

    private const double CellSize = 2.5;
    private const int MaxCells = 32;

    /// <summary>The rams' sockets' solids as point sets in the ship's frame, each one convex hull (base to barrel top); the plate's own
    /// solid is its leaf's (DesignParts.Pieces.cs, ArmourLeaf).</summary>
    public static List<List<double[]>> Solids(ShipDesign d, Piece p)
    {
        var o = new List<List<double[]>>();
        if (GridOf(d, p) is not { } g)
            return o;
        foreach (var (u, v, hw) in StrutSpots(g, p))
        {
            var (s, n, along, x, len) = StrutFrame(g, u, v, p.ArmOff);
            var (_, barrelTop, _) = RamHeights(hw, len);
            var pts = new List<double[]>();
            foreach (var sx in new[] { -hw, hw })
                foreach (var sz in new[] { -hw, hw })
                    foreach (var sy in new[] { 0.05, barrelTop })
                        pts.Add([s[0] + x[0] * sx + n[0] * sy + along[0] * sz, s[1] + x[1] * sx + n[1] * sy + along[1] * sz, s[2] + x[2] * sx + n[2] * sy + along[2] * sz]);
            o.Add(pts);
        }
        return o;
    }

    /// <summary>A quad in world space, wound so its face looks along <paramref name="want"/>.</summary>
    private static void Face(DesignMesh m, double[] a, double[] b, double[] c, double[] d, double[] want, (byte, byte, byte) col)
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        if (Dot(n, n) < 1e-16)
            n = Cross(Sub(c, a), Sub(d, a));
        if (Dot(n, want) >= 0)
            m.Quad(T(a), T(b), T(c), T(d), col, false);
        else
            m.Quad(T(a), T(d), T(c), T(b), col, false);
    }

    private static (double, double, double) T(double[] v) => (v[0], v[1], v[2]);
    private static double[] Add(double[] a, double[] b, double k) => [a[0] + b[0] * k, a[1] + b[1] * k, a[2] + b[2] * k];
    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Norm(double[] v) { var l = Math.Sqrt(Dot(v, v)); return l < 1e-9 ? [0, 1, 0] : [v[0] / l, v[1] / l, v[2] / l]; }
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}
