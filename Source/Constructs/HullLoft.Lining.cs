using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Body styles phase 7a (docs/vehiclekit/BODY-STYLES.md, section 13): a lofted bay's <b>lining</b>, the inside of a walk-in room in its faction's look, drawn by the
/// bay itself (a last word on its key, <c>i</c>, the kind and the mode). It never pokes through the hull and never shares a face's plane with it: every piece stands
/// in from the skin's inner face by <see cref="LineGap"/> or more, its own pieces end short of each other, and it is drawn only (a player stands against the bay's
/// hidden slabs, which reach further in than a following lining). Modes: <c>f</c> follows the hull's own rows (sloped walls, curved roofs, ledges), <c>i</c> is an
/// inset lining, upright walls and a flat ceiling standing in from a sloped or curved hull (a bay where that would stand more than 12 cm off the hull, or under 2.05 m,
/// follows instead). Kinds: <c>r</c> bare hull with ribs and weld beads (Bandit), <c>m</c> orderly olive panels with a rack rail (Militia), <c>c</c> clean pale panels
/// (IT), <c>s</c> showy panels with a gold rail and neon (Scam), <c>t</c> timber planking (Highland), <c>u</c> upholstered moquette under a dado and panelling over
/// it (British). Each kind hangs its own lamp from the ceiling (a glowing fitting: drawn bright and unlit, as a lit lamp is). A door bay's doorway side stays bare
/// below the cant, an upper floor's ceiling isn't lined (its hatch), and a bay's closed ends aren't (7b).
/// </summary>
public static partial class HullLoft
{
    public const string LiningKinds = "rmcstu";

    /// <summary>
    /// Phase 7b: a lined bay's windows, a fourth letter on its lining word: <c>s</c> a slit (Bandit, Militia), <c>p</c> a port (Highland), <c>b</c> a wide band (IT,
    /// Scam), <c>t</c> a tall bus window (British). Cut in each side wall but the doorway's (<see cref="WindowOf"/>), glazed by a pane part on the bay's
    /// <c>win-x</c>/<c>win+x</c> socket (<see cref="RoomPanesOf"/>), so glass counts as closed and a shot breaks the pane, not the bay.
    /// </summary>
    public const string WindowKinds = "spbt";

    /// <summary>A window kind's width along the bay, its sill and its head (metres over the floor).</summary>
    private static (float Width, float Sill, float Head) WindowSize(char kind) => kind switch
    {
        's' => (0.6f, 1.45f, 1.62f),
        'p' => (0.45f, 1.3f, 1.72f),
        'b' => (0.9f, 1.22f, 1.72f),
        _ => (0.6f, 1.2f, 1.82f),
    };

    /// <summary>
    /// A side's window (null: none): the wall rows it is cut in (<c>K0</c> holds its sill, <c>K1</c> its head, the same rows on both sections, each 3 cm or more
    /// from the row's ends, every row between upright: half again as tall as it leans), its sill and head, and where it starts and ends along the bay. Never on a
    /// door bay's doorway side, nor on a bay too short for it and 15 cm of wall either side.
    /// </summary>
    public static (int K0, int K1, float Sill, float Head, float W0, float W1)? WindowOf(Shape sh, float side)
    {
        if (sh.Window == '\0' || sh.Door && side > 0 || sh.Cage != '\0' || sh.Open)
            return null;
        var (w, sill, head) = WindowSize(sh.Window);
        if (sh.Length < w + 0.3f)
            return null;
        sill = MathF.Max(sill, MathF.Round(((sh.A.WaistY + sh.B.WaistY) / 2 + 0.05f) * 100) / 100); // (over the waist's pad, where a pannier or a sponson stands: the pane's seal would meet it)
        if (head - sill < 0.15f)
            return null;
        int RowOf(Section s, float y)
        {
            for (var k = 0; k + 1 < s.P.Length; k++)
                if (y >= s.P[k].Y + 0.03f && y <= s.P[k + 1].Y - 0.03f)
                    return k;
            return -1;
        }
        var (k0, k1) = (RowOf(sh.A, sill), RowOf(sh.A, head));
        if (k0 < 0 || k1 < 0 || RowOf(sh.B, sill) != k0 || RowOf(sh.B, head) != k1)
            return null;
        foreach (var s in new[] { sh.A, sh.B })
            for (var k = k0; k <= k1; k++)
                if (s.P[k + 1].Y - s.P[k].Y < 1.5f * MathF.Abs(s.P[k + 1].X - s.P[k].X))
                    return null;
        var w0 = MathF.Round((sh.Length - w) / 2 * 100) / 100;
        return (k0, k1, sill, head, w0, w0 + w);
    }

    /// <summary>A point on row <paramref name="k"/> (its middle surface) at height <paramref name="y"/> and <paramref name="z"/> along a bay from z 0 to <paramref name="len"/>, on the +x side.</summary>
    private static Vector3 WinAt(Section a, Section b, int k, float y, float z, float len)
    {
        Vector2 On(Section s) => Vector2.Lerp(s.P[k], s.P[k + 1], (y - s.P[k].Y) / (s.P[k + 1].Y - s.P[k].Y));
        var v = Vector2.Lerp(On(a), On(b), z / len);
        return new Vector3(v.X, v.Y, z);
    }

    /// <summary>The panes a lined bay's windows take, each with the bay's socket it goes on (its seal on the sill's middle).</summary>
    public static List<(string Socket, PaneShape Pane)> RoomPanesOf(Shape sh)
    {
        var list = new List<(string, PaneShape)>();
        foreach (var side in (float[])[-1, 1])
            if (WindowOf(sh, side) is { } win)
            {
                Vector3 P(float y, float z) { var v = WinAt(sh.A, sh.B, y < (win.Sill + win.Head) / 2 ? win.K0 : win.K1, y, z, sh.Length); return new Vector3(Snap(side * v.X), Snap(v.Y), Snap(z)); } // (a pane's key is in centimetres: the bay's socket is the made pane's)
                var (z0, z1) = (win.W0 - 0.01f, win.W1 + 0.01f); // (into the jambs and the head a centimetre: no hairline round the glass)
                list.Add((side < 0 ? "win-x" : "win+x", new PaneShape([P(win.Sill, z0), P(win.Sill, z1), P(win.Head + 0.01f, z1), P(win.Head + 0.01f, z0)])));
            }
        return list;
    }

    private const float LineGap = 0.004f; // (a lining's outer face this far in from the skin's inner face)
    private const float LineEnds = 0.015f; // (a lining stops this short of its bay's open ends: the next bay's lining is its own; 0.026 at a closed end, past the cap's thickness)

    /// <summary>A lining word for a key, or "" for none.</summary>
    public static string LiningWord(char kind, bool inset, char window = '\0') =>
        LiningKinds.Contains(kind) ? $"i{kind}{(inset ? 'i' : 'f')}{(window != '\0' && WindowKinds.Contains(window) ? window.ToString() : "")}" : "";

    private static bool LiningOk(string w) => w.Length is 3 or 4 && w[0] == 'i' && LiningKinds.Contains(w[1]) && w[2] is 'f' or 'i' && (w.Length == 3 || WindowKinds.Contains(w[3]));

    /// <summary>A key with a lining word added (as its last word), or the key as it was when it is too long for a part's name, caged, open or already lined.</summary>
    public static string WithLining(string key, char kind, bool inset, char window = '\0')
    {
        var word = LiningWord(kind, inset, window);
        if (word.Length == 0 || ShapeOf(key) is not { } sh || sh.Lining.Length > 0 || sh.Cage != '\0' || sh.Open)
            return key;
        var lined = key[..^1] + " " + word + ")";
        if (lined.Length > ConstructParts.MaxRuntimeName && word.Length == 4)
            lined = key[..^1] + " " + word[..3] + ")"; // (no room for the window's letter: lined, unglazed)
        return lined.Length <= ConstructParts.MaxRuntimeName ? lined : key;
    }

    private readonly record struct LineLook(string Low, string High, string Ceiling, float Thick);

    private static LineLook LookOf(char kind) => kind switch
    {
        'm' => new("#4f5843", "#56604a", "#5f6852", 0.014f),
        'c' => new("#cfccc2", "#dcd9cf", "#e8e6df", 0.012f),
        's' => new("#5b2a6e", "#2a2340", "#33294a", 0.012f),
        't' => new("#6e4b2a", "#5c3d22", "#6a4a2c", 0.016f),
        'u' => new("#7a1f2b", "#e4dcc4", "#ece6d2", 0.012f),
        _ => new("#5a4636", "#5a4636", "#5a4636", 0.012f),
    };

    /// <summary>
    /// The lining (above). Returns the height of an inset ceiling's underside (the lever hangs under it), or null where the roof is lined along its rows.
    /// </summary>
    private static float? Lining(Builder p, Shape sh)
    {
        var (kind, inset) = (sh.Lining[1], sh.Lining[2] == 'i');
        var look = LookOf(kind);
        var (a, b, len) = (sh.A, sh.B, sh.Length);
        var z0 = sh.Front ? 0.026f : LineEnds;
        var z1 = len - (sh.Back ? 0.026f : LineEnds);
        if (z1 - z0 < 0.2f)
            return null;
        if (inset && Inset(p, sh, kind, look, z0, z1) is { } done)
            return done.Ceiling;
        Follow(p, sh, kind, look, z0, z1);
        return null;
    }

    // ---- Following the rows -----------------------------------------------------------------------------------------

    /// <summary>A lined row's ends, shortened where it meets the next row (so two rows' pieces stand off each other at a corner), as fractions along the row on each section.</summary>
    private static (float F0, float F1)? RowSpan(Section s, int k, float d, bool mirrorEnd)
    {
        var (p0, p1) = (s.P[k], s.P[k + 1]);
        var len = Vector2.Distance(p0, p1);
        if (len < 0.08f)
            return null;
        float Cut(Vector2 dirIn, Vector2 dirOut)
        {
            var cross = dirIn.X * dirOut.Y - dirIn.Y * dirOut.X; // (a turn toward the inside: the rows' inner offsets overlap there)
            var turn = MathF.Acos(Math.Clamp(Vector2.Dot(dirIn, dirOut), -1, 1));
            return cross > 0 ? MathF.Min(0.2f, d * MathF.Tan(MathF.Min(turn, 2.6f) / 2) + 0.008f) : 0.008f;
        }
        var dir = Vector2.Normalize(p1 - p0);
        var c0 = k == 0 ? 0.03f : Cut(Vector2.Normalize(p0 - s.P[k - 1]), dir); // (the lowest row starts 3 cm off the floor)
        float c1;
        if (k + 2 < s.P.Length)
            c1 = Cut(dir, Vector2.Normalize(s.P[k + 2] - p1));
        else if (mirrorEnd) // (a pitched roof's last row meets its mirror on the centreline)
            c1 = Cut(dir, new Vector2(dir.X, -dir.Y)); // (the mirror's row, from the centreline down the other side)
        else
            c1 = 0.008f;
        var (f0, f1) = (c0 / len, 1 - c1 / len);
        return f1 - f0 < 0.05f / len ? null : (f0, f1);
    }

    /// <summary>
    /// A flat piece on row k of side <paramref name="side"/>, from fraction f0 to f1 along it (the same fractions on both sections), z0 to z1 along the bay, standing
    /// in from the skin's inner face by the gap and <paramref name="extra"/>, <paramref name="t"/> thick.
    /// </summary>
    private static void RowPiece(Builder p, Section a, Section b, int k, float side, float f0, float f1, float z0, float z1, float extra, float t, string colour, bool glow = false)
    {
        var q = new Vector3[4];
        var o = new Vector3[4];
        for (var e = 0; e < 2; e++)
        {
            var sec = e == 0 ? a : b;
            var (p0, p1) = (Vector2.Lerp(sec.P[k], sec.P[k + 1], f0), Vector2.Lerp(sec.P[k], sec.P[k + 1], f1));
            var n = RowNormal(sec.P[k], sec.P[k + 1]);
            var off = -(T / 2 + LineGap + extra + t / 2);
            var (m0, m1) = (p0 + n * off, p1 + n * off);
            var z = e == 0 ? z0 : z1;
            q[e == 0 ? 0 : 3] = new Vector3(side * m0.X, m0.Y, z);
            q[e == 0 ? 1 : 2] = new Vector3(side * m1.X, m1.Y, z);
            o[e == 0 ? 0 : 3] = o[e == 0 ? 1 : 2] = new Vector3(side * n.X, n.Y, 0);
        }
        var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        for (var c = 0; c < 4; c++)
            foreach (var sg in (float[])[-1, 1])
                (lo, hi) = (Vector3.Min(lo, q[c] + o[c] * (sg * t / 2)), Vector3.Max(hi, q[c] + o[c] * (sg * t / 2)));
        if (hi.X - lo.X < 0.0105f || hi.Y - lo.Y < 0.0105f || hi.Z - lo.Z < 0.0105f)
            return; // (a sliver)
        if (glow)
        {
            var pts = new List<Vector3>(8);
            for (var c = 0; c < 4; c++)
                foreach (var sg in (float[])[-1, 1])
                    pts.Add(q[c] + o[c] * (sg * t / 2));
            Solid(p, pts, colour, false, true);
            return;
        }
        p.Facet(q, o[0], colour, t, false, o);
    }

    /// <summary>The two sections at fractions u0 and u1 along the bay (the loft's own, point by point).</summary>
    private static (Section, Section) Along(Shape sh, float z0, float z1) => (Between(sh.A, sh.B, z0 / sh.Length), Between(sh.A, sh.B, z1 / sh.Length));

    private static void Follow(Builder p, Shape sh, char kind, LineLook look, float z0, float z1)
    {
        var (a, b) = Along(sh, z0, z1);
        var flatRoof = sh.A.P[^1].Y == sh.A.P[^2].Y && sh.B.P[^1].Y == sh.B.P[^2].Y;
        var last = a.P.Length - 2;
        var t = look.Thick;
        foreach (var side in (float[])[-1, 1])
            for (var k = 0; k <= last; k++)
            {
                if (sh.Door && side > 0 && a.P[k].Y < DoorTop + 0.05f)
                    continue; // (the doorway's side, and the cant over it)
                if (k == last && flatRoof)
                    continue; // (a flat roof is lined across: below)
                var roofRow = MathF.Abs(a.P[k + 1].Y - a.P[k].Y) < MathF.Abs(a.P[k + 1].X - a.P[k].X);
                if (roofRow && sh.Floor)
                    continue; // (an upper floor's underside: its hatch)
                var mirror = k == last && a.P[^1].X == 0;
                if (RowSpan(a, k, T / 2 + LineGap + t + 0.03f, mirror) is not { } sa || RowSpan(b, k, T / 2 + LineGap + t + 0.03f, mirror) is not { } sb)
                    continue;
                var (f0, f1) = (MathF.Max(sa.F0, sb.F0), MathF.Min(sa.F1, sb.F1));
                if (f1 - f0 < 0.02f)
                    continue;
                if (WindowOf(sh, side) is { } win && k >= win.K0 && k <= win.K1)
                {
                    if (kind != 'r') // (a bare hull's bead stops at a window: none on that row)
                        WindowLining(p, a, b, k, side, f0, f1, z0, z1, kind, look, sh.Length, win);
                    continue;
                }
                RowLining(p, a, b, k, side, f0, f1, z0, z1, kind, look, roofRow, sh.Length);
            }
        if (flatRoof && !sh.Floor)
            FlatCeiling(p, a, b, z0, z1, kind, look);
        if (kind == 'r')
            Ribs(p, sh, a, b, z0, z1, flatRoof);
        FollowLamp(p, sh, kind, z0, z1, flatRoof);
    }

    /// <summary>One row's lining from f0 to f1: pieces at most 0.5 m along the row (0.15 m planks), 6 mm seams, every other one 3 mm shorter along the bay.</summary>
    /// <param name="extra">Phase 7b: stood further in (a window's jambs, so their faces and the pieces' over and under them never meet in a plane on a twisting bay).</param>
    private static void RowLining(Builder p, Section a, Section b, int k, float side, float f0, float f1, float z0, float z1, char kind, LineLook look, bool roofRow, float bayLen, float extra = 0)
    {
        var rowLen = MathF.Max(Vector2.Distance(a.P[k], a.P[k + 1]), Vector2.Distance(b.P[k], b.P[k + 1]));
        if (kind == 'r')
        {
            // Bare hull: a weld bead along the row's middle (wall rows of 20 cm and more), nothing else on the row.
            if (!roofRow && rowLen * (f1 - f0) > 0.2f && k % 2 == 0)
            {
                var (m, h) = ((f0 + f1) / 2, 0.007f / rowLen);
                var cuts = RibsAt(z0, z1, bayLen).Select(z => (z - 0.04f, z + 0.04f)).ToList(); // (beads stop short of the ribs: a twisting row's bead and rib would meet nearly in a plane)
                var from = z0 + 0.004f;
                foreach (var (c0, c1) in cuts.Append((z1 - 0.004f, z1)))
                {
                    if (c0 - from > 0.05f)
                        RowPiece(p, a, b, k, side, m - h, m + h, from, c0, 0.003f, 0.012f, "#8e8a82");
                    from = c1;
                }
            }
            return;
        }
        var step = kind == 't' ? 0.15f : 0.5f;
        var n = Math.Max(1, (int)MathF.Ceiling(rowLen * (f1 - f0) / step - 1e-3f));
        var seam = 0.003f / rowLen;
        for (var i = 0; i < n; i++)
        {
            var (u0, u1) = (f0 + (f1 - f0) * i / n + (i > 0 ? seam : 0), f0 + (f1 - f0) * (i + 1) / n - (i + 1 < n ? seam : 0));
            var inset = i % 2 == 1 ? 0.003f : 0;
            var y = (Vector2.Lerp(a.P[k], a.P[k + 1], (u0 + u1) / 2).Y + Vector2.Lerp(b.P[k], b.P[k + 1], (u0 + u1) / 2).Y) / 2;
            var colour = roofRow ? look.Ceiling : kind switch
            {
                't' => i % 2 == 0 ? look.Low : look.High,
                'u' => y < 1.0f ? look.Low : look.High,
                's' => y < 1.05f ? look.Low : look.High,
                _ => y < 1.1f ? look.Low : look.High,
            };
            var thick = kind == 'u' && y < 1.0f && !roofRow ? 0.024f : look.Thick; // (the moquette is padded)
            RowPiece(p, a, b, k, side, u0, u1, z0 + inset, z1 - inset, extra, thick, colour);
        }
        if (roofRow)
            return;
        // A rail at a height on the wall (a rack rail, a dado, a gold strip), standing proud of the panels.
        var rail = kind switch { 'm' => 1.1f, 'u' => 1.0f, 's' => 1.05f, _ => -1f };
        if (rail < 0)
            return;
        for (var e = 0; e < 1; e++)
        {
            var (ya0, ya1, yb0, yb1) = (a.P[k].Y, a.P[k + 1].Y, b.P[k].Y, b.P[k + 1].Y);
            if (ya1 - ya0 < 0.1f || yb1 - yb0 < 0.1f || rail < MathF.Max(ya0, yb0) + 0.05f || rail > MathF.Min(ya1, yb1) - 0.05f)
                continue; // (the rail's height isn't on this row on both sections)
            var (fa, fb) = ((rail - ya0) / (ya1 - ya0), (rail - yb0) / (yb1 - yb0));
            var f = (fa + fb) / 2;
            var h = 0.022f / rowLen;
            if (f - h < f0 || f + h > f1)
                continue;
            var colour = kind switch { 'm' => "#2b2e31", 'u' => Wood, _ => Gold };
            RowPiece(p, a, b, k, side, f - h, f + h, z0 + 0.006f, z1 - 0.006f, extra + (kind == 'u' ? 0.024f : look.Thick) + 0.003f, kind == 'm' ? 0.03f : 0.018f, colour);
        }
    }

    /// <summary>
    /// Phase 7b: a following lining's row round a window: under it and over it as any row, and a jamb either side 2 cm back from the opening (a reveal), the
    /// window's band taken at its widest over both sections.
    /// </summary>
    private static void WindowLining(Builder p, Section a, Section b, int k, float side, float f0, float f1, float z0, float z1, char kind, LineLook look, float bayLen,
        (int K0, int K1, float Sill, float Head, float W0, float W1) win)
    {
        float F(Section s, float y) => (y - s.P[k].Y) / (s.P[k + 1].Y - s.P[k].Y);
        var rowLen = MathF.Max(Vector2.Distance(a.P[k], a.P[k + 1]), Vector2.Distance(b.P[k], b.P[k + 1]));
        var (g, seam) = (0.02f / rowLen, 0.006f / rowLen);
        var lowTop = k == win.K0 ? MathF.Min(F(a, win.Sill), F(b, win.Sill)) - g : f0 - seam; // (rows between the sill's and the head's: jambs only)
        var highBot = k == win.K1 ? MathF.Max(F(a, win.Head), F(b, win.Head)) + g : f1 + seam;
        if (k == win.K0 && lowTop - f0 > 0.05f / rowLen)
            RowLining(p, a, b, k, side, f0, lowTop, z0, z1, kind, look, false, bayLen);
        if (k == win.K1 && f1 - highBot > 0.05f / rowLen)
            RowLining(p, a, b, k, side, highBot, f1, z0, z1, kind, look, false, bayLen);
        var (j0, j1) = (MathF.Max(f0, lowTop + seam), MathF.Min(f1, highBot - seam));
        if (j1 - j0 < 0.05f / rowLen)
            return;
        if (win.W0 - 0.02f - z0 > 0.05f)
            RowLining(p, a, b, k, side, j0, j1, z0, win.W0 - 0.02f, kind, look, false, bayLen, 0.006f);
        if (z1 - win.W1 - 0.02f > 0.05f)
            RowLining(p, a, b, k, side, j0, j1, win.W1 + 0.02f, z1, kind, look, false, bayLen, 0.006f);
    }

    /// <summary>A flat roof's lining: one panel across under it (planks across a timber one), standing in from the roof's inner face.</summary>
    private static void FlatCeiling(Builder p, Section a, Section b, float z0, float z1, char kind, LineLook look)
    {
        if (kind == 'r')
            return; // (a bare roof: the ribs cross it)
        var (ya, yb) = (a.P[^1].Y, b.P[^1].Y);
        var w = MathF.Min(a.P[^2].X, b.P[^2].X) - 0.06f; // (short of where the roof meets the walls' lining)
        if (w < 0.2f)
            return;
        var y = MathF.Min(ya, yb) - T / 2 - LineGap - look.Thick / 2;
        var pieces = kind == 't' ? Math.Max(1, (int)((z1 - z0) / 0.15f)) : 1;
        for (var i = 0; i < pieces; i++)
        {
            var (c0, c1) = (z0 + (z1 - z0) * i / pieces + (i > 0 ? 0.003f : 0), z0 + (z1 - z0) * (i + 1) / pieces - (i + 1 < pieces ? 0.003f : 0));
            var ww = w - (i % 2 == 1 ? 0.003f : 0);
            var colour = kind == 't' ? i % 2 == 0 ? look.Low : look.High : look.Ceiling;
            Cube(p, new Vector3(-ww, y - look.Thick / 2, c0), new Vector3(ww, y + look.Thick / 2, c1), colour, false);
        }
    }

    /// <summary>Where a bay's ribs stand along it: one at the middle, two (at a quarter and three quarters) on a bay over 0.9 m.</summary>
    private static float[] RibsAt(float z0, float z1, float length) => length > 0.9f ? [z0 + (z1 - z0) * 0.25f, z0 + (z1 - z0) * 0.75f] : [(z0 + z1) / 2];

    /// <summary>Bandit ribs: a frame following the whole section across the bay (one a bay, two on a bay over 0.9 m), 6 cm deep, standing in from the skin.</summary>
    private static void Ribs(Builder p, Shape sh, Section a, Section b, float z0, float z1, bool flatRoof)
    {
        foreach (var zc in RibsAt(z0, z1, sh.Length))
        {
            var (sa, sb) = Along(sh, zc - 0.025f, zc + 0.025f);
            var last = sa.P.Length - 2;
            foreach (var side in (float[])[-1, 1])
                for (var k = 0; k <= last; k++)
                {
                    if (sh.Door && side > 0 && sa.P[k].Y < DoorTop + 0.05f)
                        continue;
                    if (k == last && flatRoof)
                        continue;
                    var mirror = k == last && sa.P[^1].X == 0;
                    if (RowSpan(sa, k, T / 2 + LineGap + 0.06f, mirror) is not { } r1 || RowSpan(sb, k, T / 2 + LineGap + 0.06f, mirror) is not { } r2)
                        continue;
                    RowPiece(p, sa, sb, k, side, MathF.Max(r1.F0, r2.F0), MathF.Min(r1.F1, r2.F1), zc - 0.025f, zc + 0.025f, 0, 0.06f, "#3f3a35");
                }
            if (flatRoof && !sh.Floor)
            {
                var w = MathF.Min(sa.P[^2].X, sb.P[^2].X) - 0.09f;
                var y = MathF.Min(sa.P[^1].Y, sb.P[^1].Y) - T / 2 - LineGap;
                if (w > 0.2f)
                    Cube(p, new Vector3(-w, y - 0.06f, zc - 0.022f), new Vector3(w, y, zc + 0.022f), "#3f3a35", false); // (3 mm narrower along the bay than the wall ribs: no shared plane)
            }
        }
    }

    /// <summary>The lamp of a following lining: under the roof 0.32 m off the centreline at the bay's middle (the lever hangs on the centreline).</summary>
    private static void FollowLamp(Builder p, Shape sh, char kind, float z0, float z1, bool flatRoof)
    {
        if (sh.Floor || sh.Length < 0.7f)
            return;
        const float x = 0.32f;
        // (the roof's underside there, the lining's or a rib's depth under it, and 2 mm more: no face on its plane)
        var y = MathF.Min(MathF.Min(sh.A.YAt(x - 0.12f), sh.A.YAt(x + 0.12f)), MathF.Min(sh.B.YAt(x - 0.12f), sh.B.YAt(x + 0.12f)));
        var slope = MathF.Abs(MathF.Min(sh.A.YAt(x - 0.12f), sh.B.YAt(x - 0.12f)) - MathF.Min(sh.A.YAt(x + 0.12f), sh.B.YAt(x + 0.12f)));
        y -= T / 2 + LineGap + LookOf(kind).Thick + 0.002f + slope;
        if (y < 2.0f)
            return; // (a low roof: no lamp in a player's way)
        Lamp(p, kind, new Vector3(x, y, z0 + (z1 - z0) * (kind == 'r' && sh.Length <= 0.9f ? 0.2f : 0.5f))); // (a short Bandit bay's rib is at its middle)
    }

    /// <summary>A faction's lamp hanging from a ceiling whose underside is at <paramref name="at"/>'s height.</summary>
    private static void Lamp(Builder p, char kind, Vector3 at)
    {
        var (x, y, z) = (at.X, at.Y, at.Z);
        switch (kind)
        {
            case 'r': // a bare bulb on a flex
                Cube(p, new Vector3(x - 0.006f, y - 0.14f, z - 0.006f), new Vector3(x + 0.006f, y, z + 0.006f), Dark, false);
                Cube(p, new Vector3(x - 0.022f, y - 0.165f, z - 0.022f), new Vector3(x + 0.022f, y - 0.143f, z + 0.022f), Dark, false);
                Cube(p, new Vector3(x - 0.03f, y - 0.22f, z - 0.03f), new Vector3(x + 0.03f, y - 0.168f, z + 0.03f), "#ffd27a", false, true);
                break;
            case 'm': // a caged bulkhead lamp
                Cube(p, new Vector3(x - 0.07f, y - 0.05f, z - 0.1f), new Vector3(x + 0.07f, y, z + 0.1f), Dark, false);
                Cube(p, new Vector3(x - 0.052f, y - 0.075f, z - 0.075f), new Vector3(x + 0.052f, y - 0.053f, z + 0.075f), "#ffe9b0", false, true);
                break;
            case 'c': // a flush light panel
                Cube(p, new Vector3(x - 0.15f, y - 0.018f, z - 0.3f), new Vector3(x + 0.15f, y, z + 0.3f), "#f4f8ff", false, true);
                break;
            case 's': // a neon tube in the faction's pink
                Cube(p, new Vector3(x - 0.02f, y - 0.03f, z - 0.35f), new Vector3(x + 0.02f, y, z + 0.35f), "#ff4fd8", false, true);
                break;
            case 't': // a lantern on a hook
                Cube(p, new Vector3(x - 0.006f, y - 0.08f, z - 0.006f), new Vector3(x + 0.006f, y, z + 0.006f), Dark, false);
                Cube(p, new Vector3(x - 0.06f, y - 0.11f, z - 0.06f), new Vector3(x + 0.06f, y - 0.083f, z + 0.06f), Dark, false);
                Cube(p, new Vector3(x - 0.045f, y - 0.23f, z - 0.045f), new Vector3(x + 0.045f, y - 0.113f, z + 0.045f), "#ffb347", false, true);
                break;
            default: // 'u': a round dome lamp
                Cube(p, new Vector3(x - 0.13f, y - 0.012f, z - 0.13f), new Vector3(x + 0.13f, y, z + 0.13f), Chrome, false);
                Cube(p, new Vector3(x - 0.1f, y - 0.05f, z - 0.1f), new Vector3(x + 0.1f, y - 0.014f, z + 0.1f), "#fff1c8", false, true);
                break;
        }
    }

    // ---- An inset lining ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Upright walls and a flat ceiling standing in from the hull (both sides, or the far one in a door bay); null when the hull is too far off upright for it (more
    /// than 12 cm between where the wall stands and the hull, or a ceiling under 2.05 m): the caller follows the rows instead.
    /// </summary>
    private static (float? Ceiling, bool Ok)? Inset(Builder p, Shape sh, char kind, LineLook look, float z0, float z1)
    {
        var t = look.Thick;
        // The hull's inner face's half width at height y, the least of both sections (a door bay's +x side: the doorway's outline).
        float Inner(float y, float side)
        {
            var w = float.MaxValue;
            foreach (var s in new[] { sh.A, sh.B })
            {
                var x = sh.Door && side > 0 ? DoorOutline(s, y) : s.XAt(y);
                if (x < 0)
                    return -1;
                w = MathF.Min(w, x);
            }
            return w - T / 2 - LineGap - 0.01f; // (a centimetre more: the loft between the two sections bends a little)
        }
        // Where each wall stands: the least of its inner face from 3 cm to 2.05 m up; the most it may stand off the hull, 12 cm.
        var wall = new[] { float.MaxValue, float.MaxValue };
        var most = new[] { 0f, 0f };
        for (var y = 0.03f; y <= 2.05f; y += 0.01f)
            for (var i = 0; i < 2; i++)
            {
                var w = Inner(y, i == 0 ? -1 : 1);
                if (w < 0)
                    return null;
                if (sh.Door && i == 1 && y < DoorTop + 0.05f)
                    continue; // (no wall across the doorway; the ceiling still keeps clear of the cant)
                wall[i] = MathF.Min(wall[i], w);
                most[i] = MathF.Max(most[i], w);
            }
        if (sh.Door)
            (wall[1], most[1]) = (MathF.Min(wall[1], DoorPlane - T / 2 - 0.02f), MathF.Min(most[1], DoorPlane)); // (the ceiling's +x edge short of the doorway's frame)
        if (most[0] - wall[0] > 0.12f || !sh.Door && most[1] - wall[1] > 0.12f)
            return null;
        // The ceiling: as high as both walls stay upright under it and the roof stays over it, from 2.05 m up.
        var top = 2.05f;
        for (var y = 2.06f; y < 3.9f; y += 0.01f)
        {
            if (Inner(y, -1) < wall[0] || Inner(y, 1) < wall[1])
                break;
            top = y;
        }
        // The roof over the ceiling's span: its inner face over every half width out to the walls.
        var roof = float.MaxValue;
        for (var x = 0f; x <= MathF.Max(wall[0], wall[1]); x += 0.02f)
            roof = MathF.Min(roof, MathF.Min(sh.A.YAt(x), sh.B.YAt(x)) - T / 2 - LineGap - 0.01f);
        var ceil = MathF.Min(top - 0.02f, roof); // (the ceiling's top face)
        if (ceil - t < 2.05f)
            return null;
        // The walls: panels 0.5 m tall (planks 0.15 m), a 6 mm seam, every other one 3 mm shorter along the bay; the outer face on the wall's line.
        var under = ceil - t - 0.004f; // (the walls stop 4 mm under the ceiling's underside)
        for (var i = 0; i < 2; i++)
        {
            if (sh.Door && i == 1)
                continue;
            var s = i == 0 ? -1f : 1f;
            var (xo, xi) = (wall[i], wall[i] - t);
            var step = kind == 't' ? 0.15f : 0.5f;
            var n = Math.Max(1, (int)MathF.Ceiling((under - 0.03f) / step - 1e-3f));
            var win = WindowOf(sh, s); // (phase 7b: a window's band cut out of the panels it crosses, 2 cm back from the opening)
            for (var j = 0; j < n; j++)
            {
                var (y0, y1) = (0.03f + (under - 0.03f) * j / n + (j > 0 ? 0.003f : 0), 0.03f + (under - 0.03f) * (j + 1) / n - (j + 1 < n ? 0.003f : 0));
                var ins = j % 2 == 1 ? 0.003f : 0;
                var y = (y0 + y1) / 2;
                var colour = kind switch { 't' => j % 2 == 0 ? look.Low : look.High, 'u' => y < 1.0f ? look.Low : look.High, 's' => y < 1.05f ? look.Low : look.High, _ => y < 1.1f ? look.Low : look.High };
                void Wall(float ya, float yb, float za, float zb)
                {
                    if (yb - ya > 0.0105f && zb - za > 0.05f)
                        Cube(p, new Vector3(MathF.Min(s * xo, s * xi), ya, za), new Vector3(MathF.Max(s * xo, s * xi), yb, zb), colour, false);
                }
                if (win is { } w && y0 < w.Head + 0.02f && y1 > w.Sill - 0.02f)
                {
                    var (lo, hi) = (w.Sill - 0.02f, w.Head + 0.02f);
                    Wall(y0, lo - 0.003f, z0 + ins, z1 - ins);
                    Wall(hi + 0.003f, y1, z0 + ins, z1 - ins);
                    Wall(MathF.Max(y0, lo), MathF.Min(y1, hi), z0 + ins, w.W0 - 0.02f);
                    Wall(MathF.Max(y0, lo), MathF.Min(y1, hi), w.W1 + 0.02f, z1 - ins);
                }
                else
                    Wall(y0, y1, z0 + ins, z1 - ins);
            }
            var rail = kind switch { 'm' => 1.1f, 'u' => 1.0f, 's' => 1.05f, _ => -1f };
            if (rail > 0)
            {
                var deep = kind == 'm' ? 0.03f : 0.018f;
                var (ri, ro) = (xi - 0.003f - deep, xi - 0.003f);
                Cube(p, new Vector3(MathF.Min(s * ri, s * ro), rail - 0.022f, z0 + 0.006f), new Vector3(MathF.Max(s * ri, s * ro), rail + 0.022f, z1 - 0.006f), kind switch { 'm' => Dark, 'u' => Wood, _ => Gold }, false);
            }
            if (kind == 's') // (neon along the wall's top)
                Cube(p, new Vector3(MathF.Min(s * (xi - 0.004f), s * (xi - 0.03f)), under - 0.06f, z0 + 0.01f), new Vector3(MathF.Max(s * (xi - 0.004f), s * (xi - 0.03f)), under - 0.03f, z1 - 0.01f), "#ff4fd8", false, true);
            if (kind == 'c') // (a kick strip)
                Cube(p, new Vector3(MathF.Min(s * (xi - 0.003f), s * (xi - 0.017f)), 0.035f, z0 + 0.006f), new Vector3(MathF.Max(s * (xi - 0.003f), s * (xi - 0.017f)), 0.135f, z1 - 0.006f), "#5f6f7d", false);
        }
        // The ceiling (unless the roof is an upper floor, cut by its hatch), its edges 4 mm in from the walls' outer faces.
        if (!sh.Floor)
        {
            var (xl, xr) = (-(wall[0] - 0.004f), wall[1] - 0.004f);
            Cube(p, new Vector3(xl, ceil - t, z0), new Vector3(xr, ceil, z1), look.Ceiling, false);
            if (sh.Length >= 0.7f)
                Lamp(p, kind, new Vector3(0.32f, ceil - t - 0.002f, (z0 + z1) / 2));
            return (ceil - t, true);
        }
        return (null, true);
    }
}
