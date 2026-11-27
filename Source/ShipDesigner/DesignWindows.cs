namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Windows seen through (Karl, 2026-11-27: in the game a window was a pale panel inside and out). A room's wall is opened behind each
/// window that looks out of it, a hull window (<see cref="HullWindow"/>) or a Window piece, as a doorway is (<see cref="WindowAt"/>: the
/// shell, the interior cladding and the room's colliders leave it open); and a hull window's glass, which only the designer's page drew,
/// is made here for the game too (<see cref="HullWindowMesh"/>).
/// </summary>
public static partial class DesignScene
{
    /// <summary>
    /// A window's opening in a room's wall, in a doorway's form (<see cref="DoorAt"/>, world frame): its foot (the sill, in the middle of the
    /// opening along the wall), half its width, its height, the window mark (2), the wall's normal axis, and how far from that point along the
    /// normal a wall is still cut (a room lined out to the hull has its wall on the skin, up to <see cref="HullWindow.MaxGap"/> out).
    /// </summary>
    public static double[] WindowAt(double[] foot, double half, double top, int normal, double reach) =>
        [foot[0], foot[1], foot[2], half, top, 2, normal, reach];

    public static bool IsWindow(double[] q) => q.Length > 7 && q[5] > 1.5;

    /// <summary>A window's opening mark when its inside frame is off (<see cref="HullWindow.FrameIn"/>): still a window (<see cref="IsWindow"/>), framed by nothing.</summary>
    public const double FramelessMark = 3;

    /// <summary>Is opening <paramref name="q"/> framed (a door, or a window with its inside frame)?</summary>
    public static bool IsFramed(double[] q) => !(IsWindow(q) && q[5] > 2.5);

    /// <summary>How far round a window's glass its wall's opening reaches.</summary>
    private const double WindowMargin = 0.05;

    /// <summary>The openings every window in the design makes in the room walls behind it (<see cref="WindowAt"/>).</summary>
    public static List<double[]> WindowWays(ShipDesign d)
    {
        var o = new List<double[]>();
        int up = d.Stacked ? 2 : 1;
        foreach (var w in d.Windows)
        {
            var lay = LayOnce(d, w);
            if (WindowFrame(d, w) is not { } f || !lay.Ok)
                continue; // (a window that lies on no skin has no glass: its wall stays shut)
            var n = Axis(f.N);
            if (n == up)
                continue;
            double hx = f.Hx + WindowMargin, hz = f.Hz + WindowMargin;
            var foot = new double[3];
            for (var k = 0; k < 3; k++)
                foot[k] = f.O[k] - f.V[k] * hz;
            // Its reach: to the skin it lies on (a room lined out to the hull has its wall there, which may be well past the room's box).
            var reach = 0.0;
            for (var k = 0; k + 2 < lay.Pts.Length; k += 3)
                reach = Math.Max(reach, Math.Abs((lay.Pts[k] - f.O[0]) * f.N[0] + (lay.Pts[k + 1] - f.O[1]) * f.N[1] + (lay.Pts[k + 2] - f.O[2]) * f.N[2]));
            var way = WindowAt(foot, hx, 2 * hz, n, reach + 0.3);
            if (!w.FrameIn)
                way[5] = FramelessMark; // (no lining or architrave round its opening: the cladding runs up to it)
            o.Add(way);
        }
        foreach (var p in d.Pieces)
        {
            if (PieceKinds.Find(p.Kind) is not { Socket: "window" } kind || d.Hull(p.Hull) is not { } h)
                continue;
            var sets = new List<(List<double[]> C, double[] In)> { PieceGlass(h, p, kind) };
            if (p.Mirror)
            {
                var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
                sets.Add(Mirrored(sets[0], cx));
            }
            if (p.Pair && h.Copies == 2)
                for (int k = 0, m = sets.Count; k < m; k++)
                    sets.Add(Mirrored(sets[k], 0));
            foreach (var (cs, inward) in sets)
                if (PieceWindow(d, cs, inward).Q is { } q)
                    o.Add(q);
        }
        return o;

        static (List<double[]>, double[]) Mirrored((List<double[]> C, double[] In) a, double cx) =>
            ([.. a.C.Select(c => new[] { 2 * cx - c[0], c[1], c[2] })], [-a.In[0], a.In[1], a.In[2]]);
    }

    /// <summary>
    /// A Window piece's glass's four corners (PieceKinds.Glazing: 0.55 x 0.9 in the piece's frame), round the ring (-x -z, +x -z, +x +z, -x +z),
    /// <paramref name="y"/> along its frame's +Y (out of the skin) in its frame's units, and its way in (-Y).
    /// </summary>
    private static (List<double[]> C, double[] In) PieceGlass(HullBody h, Piece p, PieceKind kind, double y = 0, double hx = 0.55, double hz = 0.9)
    {
        var w = Where(h, p);
        var across = kind.Footprint is not null ? p.Hole : 1;
        var corners = new List<double[]>();
        foreach (var (sx, sz) in new[] { (-1.0, -1.0), (1, -1), (1, 1), (-1, 1) })
        {
            var c = new double[3];
            for (var k = 0; k < 3; k++)
                c[k] = w.O[k] - w.Y[k] * p.Recess + ((w.X[k] * sx * hx + w.Z[k] * sz * hz) * across + w.Y[k] * y) * p.Size;
            corners.Add(c);
        }
        return (corners, [-w.Y[0], -w.Y[1], -w.Y[2]]);
    }

    /// <summary>
    /// A Window piece's sleeve (into <paramref name="m"/>, in the ship's frame): from the inner end of its glazing's reveal on in to the opening its
    /// room's wall has for it (<see cref="PieceWindow"/>), each corner to the wall opening's corner on its side, so the gap between the skin and
    /// the wall is lined, not open onto the hull's insides, and the view out stays clear however the skin slopes. Nothing with no room behind.
    /// </summary>
    public static void WindowSleeve(ShipDesign d, HullBody h, Piece p, PieceKind kind, DesignMesh m)
    {
        var (skin, inward) = PieceGlass(h, p, kind);
        if (PieceWindow(d, skin, inward) is not { Q: { } q, Room.ToHull: false })
            return; // (a room lined out to the hull has its wall on the skin: no sleeve stands into it, Karl 2026-11-27)
        var inner = PieceGlass(h, p, kind, -(Math.Max(p.Depth / Math.Max(0.05, p.Size), 0.05) + 0.06)).C; // (where Glazing's well ends)
        int up = d.Stacked ? 2 : 1, n = (int)q[6], al = n == 0 ? (d.Stacked ? 1 : 2) : 0;
        double sm = 0, um = 0;
        foreach (var c in inner)
            (sm, um) = (sm + c[al] / 4, um + c[up] / 4);
        var dir = d.Stacked ? -1 : 1;
        var wall = new List<(double, double, double)>();
        foreach (var c in inner)
        {
            var v = new double[3];
            v[n] = q[n];
            v[al] = q[al] + (c[al] < sm ? -1 : 1) * DoorHalf(q);
            v[up] = q[up] + (c[up] < um == (dir > 0) ? 0 : dir * DoorTop(q));
            wall.Add((v[0], v[1], v[2]));
        }
        var col = DesignMesh.Hex(p.RevealClad ? DesignRecess.RevealColour(PieceWindow(d, skin, inward).Room) : PieceKinds.Gun); // (lined with a wall panel when asked: DesignRecess)
        for (var k = 0; k < 4; k++)
        {
            var (a, b) = (inner[k], inner[(k + 1) % 4]);
            m.Quad((a[0], a[1], a[2]), (b[0], b[1], b[2]), wall[(k + 1) % 4], wall[k], col, false);
        }
    }

    /// <summary>The windows' openings in room r's walls (those <see cref="DoorsOf"/> takes for it).</summary>
    public static List<double[]> RoomWindows(ShipDesign d, Room r) => DoorsOf(d, r, WindowWays(d));

    private static int Axis(double[] v) => Math.Abs(v[0]) >= Math.Abs(v[1]) && Math.Abs(v[0]) >= Math.Abs(v[2]) ? 0 : Math.Abs(v[1]) >= Math.Abs(v[2]) ? 1 : 2;

    /// <summary>
    /// A Window piece's opening in the wall of the room it looks out of: the first room's side wall its glass's middle meets going in from
    /// the skin (within <see cref="HullWindow.MaxGap"/> past the piece's own depth), the glass's corners laid flat on that wall. Null: none.
    /// </summary>
    private static (double[]? Q, double T, Room? Room) PieceWindow(ShipDesign d, List<double[]> corners, double[] inward)
    {
        int up = d.Stacked ? 2 : 1;
        var mid = new double[3];
        foreach (var c in corners)
            for (var k = 0; k < 3; k++)
                mid[k] += c[k] / corners.Count;
        double best = HullWindow.MaxGap + 1.5;
        double[]? hit = null;
        Room? room = null;
        foreach (var r in d.Rooms)
        {
            var b = d.Box(r);
            // The ray mid + t * inward into the box (slabs): where it enters, and through which face.
            double t0 = 0, t1 = best;
            var face = -1;
            var ok = true;
            for (var k = 0; k < 3 && ok; k++)
            {
                double lo = b[2 * k], hi = b[2 * k + 1];
                if (Math.Abs(inward[k]) < 1e-9)
                {
                    ok = mid[k] > lo && mid[k] < hi;
                    continue;
                }
                double ta = (lo - mid[k]) / inward[k], tb = (hi - mid[k]) / inward[k];
                if (ta > tb)
                    (ta, tb) = (tb, ta);
                if (ta > t0)
                    (t0, face) = (ta, k);
                t1 = Math.Min(t1, tb);
                ok = t0 <= t1;
            }
            if (!ok || face < 0 || face == up || t0 >= best)
                continue;
            best = t0;
            var al = face == 0 ? (d.Stacked ? 1 : 2) : 0; // (along the wall)
            var plane = inward[face] > 0 ? b[2 * face] : b[2 * face + 1];
            double s0 = double.MaxValue, s1 = double.MinValue, u0 = double.MaxValue, u1 = double.MinValue;
            foreach (var c in corners)
            {
                (s0, s1) = (Math.Min(s0, c[al]), Math.Max(s1, c[al]));
                (u0, u1) = (Math.Min(u0, c[up]), Math.Max(u1, c[up]));
            }
            (s0, s1) = (Math.Max(s0 - WindowMargin, b[2 * al]), Math.Min(s1 + WindowMargin, b[2 * al + 1]));
            (u0, u1) = (Math.Max(u0 - WindowMargin, b[2 * up]), Math.Min(u1 + WindowMargin, b[2 * up + 1]));
            if (s1 - s0 < 0.1 || u1 - u0 < 0.1)
            {
                (hit, room) = (null, null);
                continue;
            }
            var foot = new double[3];
            foot[face] = plane;
            foot[al] = (s0 + s1) / 2;
            foot[up] = d.Stacked ? u1 : u0; // (a stacked plan's floor is its box's top)
            // Its reach: out to the skin (a room lined out to the hull has its lining there, past its box).
            (hit, room) = (WindowAt(foot, (s1 - s0) / 2, u1 - u0, face, Math.Max(0.6, best + 0.3)), r);
        }
        return (hit, hit is null ? 0 : best, room);
    }

    /// <summary>
    /// How deep a cargo hatch's well runs in, in its frame's units (Karl, 2026-11-27: its 1.4 m well stood out into the hold as a box): on in
    /// to the wall of the side room behind it, lining the gap, but never past it; a hand's depth in a room lined out to the hull (its wall is
    /// the skin); 1.4 where no side wall is behind it (a hatch through a deck: the hole in the floor).
    /// </summary>
    public static double CargoWell(ShipDesign d, Piece p, PieceKind kind)
    {
        const double flush = 0.15, alone = 1.4;
        if (d.Hull(p.Hull) is not { } h || kind.Opening is not { } op)
            return alone;
        var (cs, inward) = PieceGlass(h, p, kind, 0, op.Hx, op.Hz);
        var (q, t, r) = PieceWindow(d, cs, inward);
        if (q is null || r is null)
            return alone;
        var size = Math.Max(0.2, p.Size);
        return r.ToHull ? flush : Math.Clamp(t / size, flush, Math.Max(flush, kind.Reach / size));
    }

    /// <summary>
    /// A hull window's glass for the game, in the ship's frame, as the designer's page draws it (UiShipDesignerScene.DrawHullWindows): the
    /// outer pane on the skin and the inner one its depth in (double glazed), or the one pane at that depth, a reveal between their rims, and
    /// a sleeve from the inner pane in to the room's wall, so the gap between the wall and the skin is lined. Its panes are glass
    /// (<see cref="DesignMesh.Panes"/>). Empty when the window lies on no skin.
    /// </summary>
    public static DesignMesh HullWindowMesh(ShipDesign d, HullWindow w)
    {
        var m = new DesignMesh();
        m.World();
        var (pts, nu, nv, _, ok, _) = LayOnce(d, w);
        if (!ok || nu < 2 || nv < 2 || WindowFrame(d, w) is not { } f)
            return m;
        var shaped = w.Shape != "Rect";
        double[] P(int k, double depth) => [pts[3 * k] - f.N[0] * depth, pts[3 * k + 1] - f.N[1] * depth, pts[3 * k + 2] - f.N[2] * depth];
        (double, double, double) T(double[] v) => (v[0], v[1], v[2]);
        var glass = DesignMesh.Hex(PieceKinds.Glow);
        var reveal = DesignMesh.Hex(PieceKinds.Gun);
        var depth = Math.Max(0.05, w.Depth);
        void Pane(double dd) => m.Pane(() =>
        {
            for (var j = 0; j < nv - 1; j++)
                for (var i = 0; i < (shaped ? nu : nu - 1); i++)
                {
                    int a = j * nu + i, b = j * nu + (i + 1) % nu, c = a + nu, e = b + nu;
                    if (3 * Math.Max(c, e) + 2 >= pts.Length)
                        continue;
                    m.Quad(T(P(a, dd)), T(P(b, dd)), T(P(e, dd)), T(P(c, dd)), glass, false);
                }
        });
        if (w.Double)
            Pane(0.012);
        Pane(depth);
        // The rim, round the glass (as the page's ring).
        var ring = new List<int>();
        if (shaped)
            for (var i = 0; i < nu; i++)
                ring.Add((nv - 1) * nu + i);
        else
        {
            for (var i = 0; i < nu; i++) ring.Add(i);
            for (var j = 1; j < nv; j++) ring.Add(j * nu + nu - 1);
            for (var i = nu - 2; i >= 0; i--) ring.Add((nv - 1) * nu + i);
            for (var j = nv - 2; j > 0; j--) ring.Add(j * nu);
        }
        // The reveal from the skin to the inner pane, then the sleeve on in to the wall (each rim point's own distance from the wall's plane):
        // not in a room lined out to the hull, whose wall is the skin.
        var sleeve = d.RoomById(w.Room) is { ToHull: false };
        for (var k = 0; k < ring.Count; k++)
        {
            int a = ring[k], b = ring[(k + 1) % ring.Count];
            m.Quad(T(P(a, 0)), T(P(b, 0)), T(P(b, depth + 0.02)), T(P(a, depth + 0.02)), reveal, false);
            if (sleeve && ToWall(a) is var wa && ToWall(b) is var wb && (wa > depth + 0.03 || wb > depth + 0.03))
                m.Quad(T(P(a, depth + 0.02)), T(P(b, depth + 0.02)), T(P(b, Math.Max(wb, depth + 0.02))), T(P(a, Math.Max(wa, depth + 0.02))), reveal, false);
        }
        if (w.FrameOut)
            Boarding(d, w, m, pts, nu, nv, shaped);
        return m;

        double ToWall(int k) => (pts[3 * k] - f.O[0]) * f.N[0] + (pts[3 * k + 1] - f.O[1]) * f.N[1] + (pts[3 * k + 2] - f.O[2]) * f.N[2];
    }

    /// <summary>The boarding round a hull window's glass: a band on the skin from the glass's rim out to the cut's (the page's, as laid by the same rays).</summary>
    private static void Boarding(ShipDesign d, HullWindow w, DesignMesh m, double[] pts, int nu, int nv, bool shaped)
    {
        var (fp, fnu, fnv, _, _, _) = LayOnce(d, w, WindowGrow(d, w), 0.045);
        if (fnu < 2 || fnv < 2)
            return;
        static double[] P(double[] q, int k) => [q[3 * k], q[3 * k + 1], q[3 * k + 2]];
        var inner = new List<double[]>();
        var outer = new List<double[]>();
        if (shaped)
            for (var i = 0; i < nu; i++)
            {
                inner.Add(P(pts, (nv - 1) * nu + i));
                outer.Add(P(fp, (fnv - 1) * fnu + i % fnu));
            }
        else
        {
            var si = Sides(pts, nu, nv);
            var so = Sides(fp, fnu, fnv);
            for (var e = 0; e < 4; e++)
            {
                var k = Math.Max(si[e].Count, so[e].Count) * 2;
                var a = Resample(si[e], k);
                var b = Resample(so[e], k);
                a.RemoveAt(a.Count - 1);
                b.RemoveAt(b.Count - 1);
                inner.AddRange(a);
                outer.AddRange(b);
            }
        }
        var col = DesignMesh.Hex(PieceKinds.Armour);
        for (var k = 0; k < inner.Count && k < outer.Count; k++)
        {
            var k1 = (k + 1) % inner.Count;
            m.Quad((inner[k][0], inner[k][1], inner[k][2]), (inner[k1][0], inner[k1][1], inner[k1][2]), (outer[k1][0], outer[k1][1], outer[k1][2]), (outer[k][0], outer[k][1], outer[k][2]), col, false);
        }

        static List<double[]>[] Sides(double[] q, int a, int b)
        {
            var s4 = new[] { new List<double[]>(), new List<double[]>(), new List<double[]>(), new List<double[]>() };
            for (var i = 0; i < a; i++) s4[0].Add(P(q, i));
            for (var j = 0; j < b; j++) s4[1].Add(P(q, j * a + a - 1));
            for (var i = a - 1; i >= 0; i--) s4[2].Add(P(q, (b - 1) * a + i));
            for (var j = b - 1; j >= 0; j--) s4[3].Add(P(q, j * a));
            return s4;
        }

        static List<double[]> Resample(List<double[]> line, int n)
        {
            static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
            var len = new List<double> { 0 };
            for (var i = 1; i < line.Count; i++)
                len.Add(len[i - 1] + Dist(line[i], line[i - 1]));
            var total = len[^1] > 0 ? len[^1] : 1;
            var o = new List<double[]>(n);
            for (var k = 0; k < n; k++)
            {
                var t = total * k / (n - 1);
                var i = 1;
                while (i < line.Count - 1 && len[i] < t)
                    i++;
                var den = len[i] - len[i - 1];
                var f = Math.Clamp((t - len[i - 1]) / (den != 0 ? den : 1), 0, 1);
                o.Add([line[i - 1][0] + (line[i][0] - line[i - 1][0]) * f, line[i - 1][1] + (line[i][1] - line[i - 1][1]) * f, line[i - 1][2] + (line[i][2] - line[i - 1][2]) * f]);
            }
            return o;
        }
    }
}
