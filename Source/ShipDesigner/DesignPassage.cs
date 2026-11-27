namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Passages between bodies (Karl, 2026-11-26): a lift or a walkway from a room in one body to a room in another, through the arm that
/// joins them. Its line runs from a door in the first room's wall, along the arm's spine (its Bézier middle line, inside the strut), to
/// a door in the second's; the clear room inside the arm is its narrowest section less the walls.
/// </summary>
public static partial class DesignScene
{
    /// <summary>The walls' allowance inside an arm (m each side), and the door's middle over the floor.</summary>
    public const double ArmWall = 0.08, PassageDoorMid = 1.05;

    public sealed record PassageLay(HullJoin Join, List<double[]> Pts, List<double[]> Ups, double Clear, double[] DoorA, double[] DoorB);

    /// <summary>The arm between two rooms' bodies (the passage's own when it names one): null with none.</summary>
    public static HullJoin? PassageArm(ShipDesign d, Room a, Room b, int join = 0)
    {
        var (ha, hb) = (HomeBody(d, a), HomeBody(d, b));
        if (ha is null || hb is null || ha == hb)
            return null;
        bool Between(HullJoin j) => (j.A.Hull == ha.Id && j.B.Hull == hb.Id) || (j.A.Hull == hb.Id && j.B.Hull == ha.Id);
        if (join != 0 && d.JoinById(join) is { } named && Between(named))
            return named;
        // The nearest of the arms between them (a pod can hang off two).
        var mid = Mid(d, a);
        return d.Joins.Where(Between).MinBy(j => j.Spine(d) is { Count: > 0 } s ? s.Min(f => Dist(f.C, mid)) : double.MaxValue);
    }

    /// <summary>Lays a passage: why it can't be laid ("" when it can), and its line.</summary>
    public static (string Why, PassageLay? Lay) LayPassage(ShipDesign d, Passage k)
    {
        var (ra, rb) = (d.RoomById(k.A), d.RoomById(k.B));
        if (ra is null || rb is null)
            return ("its room is gone", null);
        var (ha, hb) = (HomeBody(d, ra), HomeBody(d, rb));
        if (ha == hb)
            return ($"{Name(ra)} and {Name(rb)} are in the same body: a door, a ladder or a lift joins them", null);
        var j = PassageArm(d, ra, rb, k.Join);
        if (j is null)
            return ($"no arm joins {ha?.Name ?? "?"} and {hb?.Name ?? "?"}: connect the bodies first (Hull tab, Connect)", null);
        var spine = j.Spine(d);
        if (spine.Count < 2)
            return ("the arm has no spine", null);
        // A mirrored arm's spine holds both copies, one after the other: the one nearer the rooms.
        var chains = new List<List<(double[] C, double[] S, double[] U, double W, double H)>>();
        var half = spine.Count / 2;
        if (j.Mirror && spine.Count % 2 == 0 && half >= 2 && Math.Abs(spine[0].C[0] + spine[half].C[0]) < 1e-6 && Math.Abs(spine[0].C[0]) > 1e-3)
            chains.AddRange([spine.GetRange(0, half), spine.GetRange(half, half)]);
        else
            chains.Add(spine);
        var (ma, mb) = (Mid(d, ra), Mid(d, rb));
        double Cost(List<(double[] C, double[] S, double[] U, double W, double H)> c) => Math.Min(Dist(c[0].C, ma) + Dist(c[^1].C, mb), Dist(c[^1].C, ma) + Dist(c[0].C, mb));
        var ch = new List<(double[] C, double[] S, double[] U, double W, double H)>(chains.MinBy(Cost)!);
        if (Dist(ch[^1].C, ma) + Dist(ch[0].C, mb) < Dist(ch[0].C, ma) + Dist(ch[^1].C, mb))
            ch.Reverse();
        var clear = ch.Min(f => Math.Min(f.W, f.H * (1 - Math.Abs(j.Bias)))) - 2 * ArmWall;
        var (da, db) = (DoorOnWall(d, ra, ch[0].C), DoorOnWall(d, rb, ch[^1].C));
        double[] up = d.Stacked ? [0, 0, -1] : [0, 1, 0];
        double[] Lift(double[] p) => [p[0] + up[0] * PassageDoorMid, p[1] + up[1] * PassageDoorMid, p[2] + up[2] * PassageDoorMid];
        var pts = new List<double[]> { Lift(da) };
        var ups = new List<double[]> { up };
        // Its line in the arm: the spine's middle, the ends sunk under the skins left out where they are behind the doors.
        foreach (var f in ch)
        {
            pts.Add(f.C);
            ups.Add(f.U);
        }
        pts.Add(Lift(db));
        ups.Add(up);
        return ("", new PassageLay(j, pts, ups, clear, da, db));
    }

    /// <summary>A room's door towards a point: on the wall of its plan nearest it, kept 55 cm off the corners, at its floor.</summary>
    private static double[] DoorOnWall(ShipDesign d, Room r, double[] toward)
    {
        var dk = d.DeckOf(r);
        double pu = toward[0], pv = d.Stacked ? toward[1] : toward[2];
        double u = Math.Clamp(pu, r.X0, r.X1), v = Math.Clamp(pv, r.Z0, r.Z1);
        // Inside its plan (an arm leaving through the ceiling or the floor): the nearest wall.
        if (u > r.X0 && u < r.X1 && v > r.Z0 && v < r.Z1)
        {
            var e = new[] { (u - r.X0, 0), (r.X1 - u, 1), (v - r.Z0, 2), (r.Z1 - v, 3) }.MinBy(q => q.Item1).Item2;
            if (e == 0) u = r.X0; else if (e == 1) u = r.X1; else if (e == 2) v = r.Z0; else v = r.Z1;
        }
        const double c = 0.55;
        if (u <= r.X0 || u >= r.X1)
            v = r.Z1 - r.Z0 > 2 * c ? Math.Clamp(v, r.Z0 + c, r.Z1 - c) : (r.Z0 + r.Z1) / 2;
        else
            u = r.X1 - r.X0 > 2 * c ? Math.Clamp(u, r.X0 + c, r.X1 - c) : (r.X0 + r.X1) / 2;
        return d.World(dk, u, v, 0);
    }

    // ---- The walls of a passage and of a socket's connector (step 4, package J) ------------------------------------------------------
    // Built here once, as convex pieces: the designer draws them (the scene's "mesh" on a passage and a connector link) and the game's
    // design part collides as the very same pieces (DesignParts.Rooms.cs), so nothing is laid out a second time.

    /// <summary>A passage's and a connector's walls (m thick), the head room over a passage's floor, and how far a floor's top sits under the room's it meets (so no faces share a plane).</summary>
    public const double PassageT = 0.05, PassageHead = 2.15, FloorDip = 0.003;

    /// <summary>A passage's clear half width: half its arm's clear room, held between a door's half and 0.7 m.</summary>
    public static double PassageHalf(PassageLay l) => Math.Clamp(l.Clear / 2, DoorHalfDefault, 0.7);

    /// <summary>One point of a passage's line: its floor's top there (world), the way along, across and up (unit, each square to the way along).</summary>
    public readonly record struct PassageFrame(double[] Floor, double[] Along, double[] Side, double[] Up);

    /// <summary>
    /// A passage's frames along its line: the floor 1.05 m (<see cref="PassageDoorMid"/>) under each point of its line, each end's at its room's
    /// floor top (the room's shell floor: <see cref="FloorTop"/>), the points between eased from one to the other.
    /// </summary>
    public static List<PassageFrame> PassageFrames(ShipDesign d, Passage k, PassageLay l)
    {
        var (ra, rb) = (d.RoomById(k.A), d.RoomById(k.B));
        double la = ra is null ? 0 : FloorTop(ra), lb = rb is null ? 0 : FloorTop(rb);
        var n = l.Pts.Count;
        var o = new List<PassageFrame>(n);
        for (var i = 0; i < n; i++)
        {
            var (p0, p1) = (l.Pts[Math.Max(0, i - 1)], l.Pts[Math.Min(n - 1, i + 1)]);
            var t = Unit([p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]]);
            var u = l.Ups[i];
            var ut = Dot(u, t);
            var up = Unit([u[0] - t[0] * ut, u[1] - t[1] * ut, u[2] - t[2] * ut]);
            var side = Unit(Cross(t, up));
            var lift = la + (lb - la) * i / Math.Max(1, n - 1) - FloorDip;
            var drop = PassageDoorMid - lift;
            var p = l.Pts[i];
            o.Add(new PassageFrame([p[0] - up[0] * drop, p[1] - up[1] * drop, p[2] - up[2] * drop], t, side, up));
        }
        return o;
    }

    /// <summary>
    /// A passage's walls as the designer draws them and the game collides with them: a span between each two points of its line is a floor,
    /// two walls and a roof, each a thin convex piece (8 corners) into <paramref name="pieces"/> when given. A lift's passage is the same
    /// tube; its car is the game's (DesignParts.Rooms.cs).
    /// </summary>
    public static DesignMesh PassageShell(ShipDesign d, Passage k, PassageLay l, List<double[]>? pieces = null)
    {
        var m = new DesignMesh();
        m.World();
        var fr = PassageFrames(d, k, l);
        double hw = PassageHalf(l), t = PassageT;
        (byte, byte, byte) wall = (0x6d, 0x76, 0x82), deck = (0x9a, 0xa3, 0xad);
        for (var i = 0; i + 1 < fr.Count; i++)
        {
            var (a, b) = (fr[i], fr[i + 1]);
            var seg = Unit(Sub(b.Floor, a.Floor));
            // (each span a hair longer at its inner ends, so a bend leaves no slot between two)
            double ea = i == 0 ? 0 : 0.03, eb = i + 2 == fr.Count ? 0 : 0.03;
            double[] At(PassageFrame f, double e, double s, double u) =>
            [
                f.Floor[0] + seg[0] * e + f.Side[0] * s + f.Up[0] * u,
                f.Floor[1] + seg[1] * e + f.Side[1] * s + f.Up[1] * u,
                f.Floor[2] + seg[2] * e + f.Side[2] * s + f.Up[2] * u,
            ];
            // A piece: across s0..s1 and up u0..u1 at both ends of the span.
            void Piece(double s0, double s1, double u0, double u1, (byte, byte, byte) col)
            {
                double[][] ra = [At(a, -ea, s0, u0), At(a, -ea, s1, u0), At(a, -ea, s1, u1), At(a, -ea, s0, u1)];
                double[][] rb = [At(b, eb, s0, u0), At(b, eb, s1, u0), At(b, eb, s1, u1), At(b, eb, s0, u1)];
                Prism(m, ra, rb, col, pieces);
            }
            Piece(-(hw + t - FloorDip), hw + t - FloorDip, -t, 0, deck); // the floor, under the walls
            Piece(-(hw + t), -hw, -t + FloorDip, PassageHead + t, wall);
            Piece(hw, hw + t, -t + FloorDip, PassageHead + t, wall);
            Piece(-(hw + FloorDip), hw + FloorDip, PassageHead, PassageHead + t, wall); // the roof, its ends inside the walls
        }
        return m;
    }

    /// <summary>A socket's connector shaft: its walls (and a climb's ladder), as the designer draws it and the game builds it.</summary>
    public sealed class Connector
    {
        public readonly DesignMesh Mesh = new();
        public readonly List<double[]> Pieces = []; // its walls and floor: convex pieces, 3 numbers a corner
        public readonly List<(double[] Lo, double[] Hi)> Rails = []; // a climbing shaft's ladder rails (world boxes)
        public double Foot = double.NaN, Top = double.NaN; // its ladder's foot and top (world y; NaN: no ladder)
        public double[] Toward = [0, 0, 1]; // from the climber to its rails (a ladder's +z)
        public string Why = ""; // what it lacks (a slanted climb has no ladder yet), "" when nothing
        public Room? Room; // the room it opens into (null: its way wasn't found)
    }

    /// <summary>A shaft climbs as a ladder when it is this near upright (the cosine to up); one that climbs less steeply than <see cref="ClimbsAt"/> runs level, on a floor.</summary>
    public const double UprightAt = 0.95, ClimbsAt = 0.6;

    /// <summary>
    /// The connector of socket link <paramref name="link"/> (<see cref="Check.Links"/> with a Shaft, Karl 2026-11-24: up to
    /// <see cref="MaxConnector"/> m from the skin to the room a socket is linked to), lined with walls: from the skin to the socket's way into the
    /// room (its hole or door in the room's shell, <see cref="Check.SocketWays"/>), lined to that way's edge so no gap is left round it. An upright
    /// one has a ladder up its side (rails and rungs, every 0.3 m) from the skin to the room's floor; one that runs level, a floor along it.
    /// </summary>
    public static Connector? ConnectorOf(ShipDesign d, Check c, int link)
    {
        var (a0, b0, ok, shaft, service, _, sock) = c.Links[link];
        if (service || !(shaft > 0) || !ok)
            return null;
        // Its room end: the socket's way nearest the link's room point (its hole, or its door, in the room's shell).
        double[] b = b0;
        double half = (shaft + 0.2) / 2;
        Room? room = null;
        var bestD = 1.5;
        foreach (var (way, _) in c.SocketWays)
            if (Dist(way, b0) < bestD)
                (bestD, b) = (Dist(way, b0), way);
        if (bestD < 1.5)
        {
            half = Math.Max(half, DoorHalf(b) + FloorDip + 0.001);
            room = d.Rooms.FirstOrDefault(r => d.Box(r) is var bx && b[0] >= bx[0] - 0.05 && b[0] <= bx[1] + 0.05 && b[2] >= bx[4] - 0.05 && b[2] <= bx[5] + 0.05 && Math.Abs(b[1] - bx[2]) < 0.1);
        }
        var o = new Connector { Room = room };
        o.Mesh.World();
        double[] up = d.Stacked ? [0, 0, -1] : [0, 1, 0];
        var top = room is null ? 0 : FloorTop(room);
        var lid = top - FloorDip; // (its room end just under the room's floor top)
        double[] end = [b[0] + up[0] * lid, b[1] + up[1] * lid, b[2] + up[2] * lid];
        var len = Dist(a0, end);
        if (len < 0.2)
            return null;
        var y = Unit(Sub(end, a0));
        var climb = Math.Abs(Dot(y, up));
        // Its cross axes: an upright one squared to the ship (x, then the other level axis), a level one's z up.
        double[] x, z;
        if (climb >= ClimbsAt)
        {
            x = Unit(Cross(y, d.Stacked ? [0, 1, 0] : [0, 0, 1]));
            z = Unit(Cross(x, y));
        }
        else
        {
            z = Unit(Sub(up, Scale(y, Dot(up, y))));
            x = Unit(Cross(y, z));
        }
        var a = Add(a0, Scale(y, 0.05)); // (from just inside the skin)
        var l = Dist(a, end);
        double[] P(double s, double v, double w) => [a[0] + x[0] * s + y[0] * v + z[0] * w, a[1] + x[1] * s + y[1] * v + z[1] * w, a[2] + x[2] * s + y[2] * v + z[2] * w];
        (byte, byte, byte) wallCol = (0x6d, 0x76, 0x82), steel = (0x9a, 0xa3, 0xad);
        double t = PassageT;
        void Piece(double s0, double s1, double w0, double w1, double v0, double v1, (byte, byte, byte) col, List<double[]>? sink)
        {
            double[][] ra = [P(s0, v0, w0), P(s1, v0, w0), P(s1, v0, w1), P(s0, v0, w1)];
            double[][] rb = [P(s0, v1, w0), P(s1, v1, w0), P(s1, v1, w1), P(s0, v1, w1)];
            Prism(o.Mesh, ra, rb, col, sink);
        }
        if (climb >= ClimbsAt)
        {
            // Four walls round its clear square, the x ones past the z ones' ends.
            Piece(-half - t, -half, -half - t, half + t, 0, l, wallCol, o.Pieces);
            Piece(half, half + t, -half - t, half + t, 0, l, wallCol, o.Pieces);
            Piece(-half + FloorDip, half - FloorDip, -half - t, -half, 0, l, wallCol, o.Pieces);
            Piece(-half + FloorDip, half - FloorDip, half, half + t, 0, l, wallCol, o.Pieces);
            if (climb >= UprightAt)
            {
                // Its ladder on the -z wall: two rails and rungs every 0.3 m, from the skin to the room's floor (where it is stepped off).
                var rz = -half + 0.12;
                foreach (var rx in new[] { -0.22, 0.22 })
                {
                    var (p, q) = (P(rx - 0.02, 0, rz - 0.02), P(rx + 0.02, l, rz + 0.02));
                    o.Rails.Add(([Math.Min(p[0], q[0]), Math.Min(p[1], q[1]), Math.Min(p[2], q[2])], [Math.Max(p[0], q[0]), Math.Max(p[1], q[1]), Math.Max(p[2], q[2])]));
                    Piece(rx - 0.02, rx + 0.02, rz - 0.02, rz + 0.02, 0, l, steel, null);
                }
                for (var v = 0.15; v < l - 0.05; v += 0.3)
                    Piece(-0.22, 0.22, rz - 0.015, rz + 0.015, v - 0.015, v + 0.015, steel, null);
                var (f, tp) = (a0[d.Stacked ? 2 : 1], b[d.Stacked ? 2 : 1] + (d.Stacked ? -top : top));
                (o.Foot, o.Top) = (Math.Min(f, tp), Math.Max(f, tp));
                o.Toward = Scale(z, -1);
            }
            else
                o.Why = $"it climbs {Math.Acos(Math.Min(1, climb)) * 180 / Math.PI:0} degrees off upright: no ladder up it yet";
        }
        else
        {
            // Level (or a ramp's slope): a floor along its line, walls either side and a roof at the head room over it.
            Piece(-half - t + FloorDip, half + t - FloorDip, -t, 0, 0, l, steel, o.Pieces);
            Piece(-half - t, -half, -t + FloorDip, PassageHead + t, 0, l, wallCol, o.Pieces);
            Piece(half, half + t, -t + FloorDip, PassageHead + t, 0, l, wallCol, o.Pieces);
            Piece(-half - FloorDip, half + FloorDip, PassageHead, PassageHead + t, 0, l, wallCol, o.Pieces);
        }
        return o;
    }

    /// <summary>
    /// A convex prism between two rings of corners (the same count, in order): drawn into <paramref name="m"/> (world), its corners (ring a, then
    /// ring b) into <paramref name="sink"/> when given, a convex piece the game collides with.
    /// </summary>
    public static void Prism(DesignMesh m, IReadOnlyList<double[]> a, IReadOnlyList<double[]> b, (byte, byte, byte) col, List<double[]>? sink)
    {
        var n = a.Count;
        static (double, double, double) T(double[] v) => (v[0], v[1], v[2]);
        var side = DesignMesh.Tone(col, 0.85);
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            m.Quad(T(a[i]), T(a[j]), T(b[j]), T(b[i]), side, false);
        }
        for (var i = 1; i + 1 < n; i++)
        {
            m.Tri(T(a[0]), T(a[i + 1]), T(a[i]), col, false);
            m.Tri(T(b[0]), T(b[i]), T(b[i + 1]), col, false);
        }
        if (sink is null)
            return;
        var s = new double[6 * n];
        for (var i = 0; i < n; i++)
        {
            Array.Copy(a[i], 0, s, 3 * i, 3);
            Array.Copy(b[i], 0, s, 3 * (n + i), 3);
        }
        sink.Add(s);
    }

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Add(double[] a, double[] b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];

    private static double[] Scale(double[] a, double s) => [a[0] * s, a[1] * s, a[2] * s];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return l < 1e-12 ? [0, 0, 1] : [a[0] / l, a[1] / l, a[2] / l];
    }

    private static double[] Mid(ShipDesign d, Room r)
    {
        var b = d.Box(r);
        return [(b[0] + b[1]) / 2, (b[2] + b[3]) / 2, (b[4] + b[5]) / 2];
    }

    public static double Dist3(double[] a, double[] b) => Dist(a, b);

    private static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
}
