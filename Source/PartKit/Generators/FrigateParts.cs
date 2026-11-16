using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Halberd, a mid-size combat frigate (Karl, 2026-11-15: "Give it the Rocinante feel, pdcs (we have gun turrets already so we can
/// use these), med bay, robot area, all the good things. Keep it sci-fi ... layered hull and lived-in interior"; "make new parts too, go
/// wild!"). Our own design: a tail-sitter whose decks stack up the thrust axis like a tower, joined by a ladder spine, with a chamfered
/// octagonal hull, armour slabs standing proud of the skin over a visible frame, point-defence turrets on sponsons and a railgun up the
/// spine. Writes Constructs/Parts/Frigate and the Halberd Frigate blueprint (docs/sandbox/constructs/frigate.md).
///
/// Six storeys, one big part each (floor, walls, partitions, its band of hull and armour, the hidden boxes standing for the drawn
/// shapes): 1 engineering and the airlock, 2 the robot bay and armoury, 3 the med bay and galley, 4 crew quarters and the head, 5 ops
/// (the gunnery stations), 6 the cockpit under the nose. Under them the thrust frame (the fuel tank) with the main drive and four legs;
/// over them the nose with the railgun's muzzle and the sensor mast. Every big part has its origin at the body's: the generator works in
/// ship metres (x across, port to starboard; y up from the lowest floor's underside; z from the bow face back, so forward is -z) and moves
/// them by half a cell. Furniture is the shipped parts and the new fittings below, on sockets the big parts carry where each goes.
///
/// The rust bucket's lessons (Karl): no bathroom tiles anywhere (a ship is panelled), and nothing clips: every fitting stands on a
/// socket on a face, and drawn plates are offset from the walls behind them.
/// </summary>
public sealed class FrigateParts : IPartGenerator
{
    public string Name => "frigate_parts";

    private const string Out = "Constructs/Parts/Frigate";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Halberd frigate";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint: slate armour over a pale skin, a primer red where the paint is worn through, hazard yellow on edges.
    private const string Skin = "#a9aca6", Armour = "#56626c", Armour2 = "#47525b", Primer = "#7a3b2c", Frame = "#26292c", Hazard = "#d8a628",
        Wall = "#8e959a", WallLow = "#4c555c", WallTrim = "#2f3539", Deck = "#4d5357", Grate = "#3a3f43", Ceil = "#6f777c", Glass = "#7fa8c4",
        Pipe = "#7b8287", PipeRed = "#a8402c", PipeBlue = "#2f6a9a", PipeYellow = "#c8a030", PipeGreen = "#3f8a52", Lamp = "#e8eef2",
        MedWhite = "#d6dbd8", MedGreen = "#5aa88a", Console = "#23282c", Screen = "#58c8d8", Amber = "#f0a830";

    // ---- the plan

    private const double Cx = 5.0, Cz = 4.5; // the spine
    private const double Pitch = 3.25, Slab = 0.25; // storey height and floor thickness
    private const double So = 0.27; // the skin's offset outside a room's inner face (a 0.25 m wall and 2 cm)
    private const int Storeys = 6;
    private const double Sq2 = 1.4142135623730951;

    private static double Fl(int k) => k * Pitch; // a storey's floor underside
    private static double Ft(int k) => k * Pitch + Slab; // its floor's top
    private static double Hx(int k) => k < 5 ? 4.0 : 2.75; // a room's inner half width
    private static double Hz(int k) => k < 5 ? 3.5 : 2.5;
    private static double Ci(int k) => k < 5 ? 1.15 : 0.8; // the inner corners' chamfer

    // The ladder spine: two shafts side by side, each storey's ladder in the other one, so its foot stands on solid floor.
    private const double ShaftZ0 = 3.85, ShaftZ1 = 5.1; // a hatch's front and back edge (the ladder hangs on the back edge)
    private static readonly double[] ShaftX = [4.2, 5.8]; // P, Q: the hatches' middles
    private const double ShaftW = 1.25;

    private static int LadderShaft(int k) => k % 2; // storey k's ladder up (to k+1)
    private static int HatchShaft(int k) => (k - 1) % 2; // the hatch in storey k's floor (ladder k-1 comes up through it)

    // The drive end.
    private const double FrameBottom = -2.6, Ground = -6.0;
    private const double NoseTop = 21.6;

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    // ---- geometry helpers (as the rust bucket's)

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return [a[0] / l, a[1] / l, a[2] / l];
    }

    /// <summary>A flat plate through the points (a convex polygon), T thick about them: a "hull" shape, drawn only.</summary>
    private static void Plate(Part p, double[][] pts, string color, string finish = "painted plating", bool glass = false, bool paint = false, bool glow = false, double thick = T)
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
        p.Add(Box(mn, mx, color, detail: true, glass: glass, shape: shape));
    }

    /// <summary>A quad as one plate when flat, else as two triangles.</summary>
    private static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "painted plating", bool glass = false, bool paint = false)
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
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish, bool paint = false)
    {
        var both = a.Concat(b).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("finish", finish));
        if (!paint)
            shape["paintable"] = false;
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null)
    {
        var shape = finish == null ? null : Kw(("finish", finish));
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, shape: shape));
    }

    private static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false)
    {
        var shape = finish == null && !glow ? null : new PyDict();
        if (finish != null)
            shape!["finish"] = finish;
        if (glow)
            shape!["glow"] = true;
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, detail: true, shape: shape));
    }

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1) =>
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));

    private static void Sock(Part p, string name, double x, double y, double z, string dir) => p.Sock(name, P(x, y, z), dir);

    // ---- outlines

    /// <summary>An octagon round the spine: the room's inner outline of storey-like sizes grown by off, its corners chamfered so the
    /// diagonals stay parallel. Points go round from the bow's port end.</summary>
    private static (double X, double Z)[] Oct(double hx, double hz, double ci, double off)
    {
        double ox = hx + off, oz = hz + off, c = ci + off * (2 - Sq2);
        return
        [
            (Cx - ox + c, Cz - oz), (Cx + ox - c, Cz - oz), (Cx + ox, Cz - oz + c), (Cx + ox, Cz + oz - c),
            (Cx + ox - c, Cz + oz), (Cx - ox + c, Cz + oz), (Cx - ox, Cz + oz - c), (Cx - ox, Cz - oz + c),
        ];
    }

    private static (double X, double Z)[] OctK(int k, double off) => Oct(Hx(k), Hz(k), Ci(k), off);

    /// <summary>The band of plates between two outlines (at y0 and y1), edge by edge; skip: edges left out (filled by the caller).</summary>
    private static void Ring(Part p, (double X, double Z)[] a, double y0, (double X, double Z)[] b, double y1, string color, string finish, bool paint, params int[] skip)
    {
        for (var i = 0; i < 8; i++)
        {
            if (skip.Contains(i))
                continue;
            var j = (i + 1) % 8;
            Quad(p, P(a[i].X, y0, a[i].Z), P(a[j].X, y0, a[j].Z), P(b[j].X, y1, b[j].Z), P(b[i].X, y1, b[i].Z), color, finish, paint: paint);
        }
    }

    /// <summary>A flat cap over an outline at y.</summary>
    private static void Cap(Part p, (double X, double Z)[] o, double y, string color, string finish) =>
        Plate(p, [.. o.Select(q => P(q.X, y, q.Z))], color, finish);

    /// <summary>Points along edge i of an outline: t of the way along, pushed out by d (outward normal), at y.</summary>
    private static double[] OnEdge((double X, double Z)[] o, int i, double t, double d, double y)
    {
        var j = (i + 1) % 8;
        double ex = o[j].X - o[i].X, ez = o[j].Z - o[i].Z, l = Math.Sqrt(ex * ex + ez * ez);
        double nx = ez / l, nz = -ex / l; // outward (the outline goes clockwise seen from above with x right and z down)
        return P(o[i].X + ex * t + nx * d, y, o[i].Z + ez * t + nz * d);
    }

    /// <summary>An armour slab on edge i of outline o from t0 to t1 along and y0 to y1 up: a block standing d0..d1 out, its outer face
    /// bevelled in by bev all round, so its edges catch the light. Hidden boxes aren't needed: the wall behind it is the hull.</summary>
    private static void Slab2(Part p, (double X, double Z)[] o, int i, double t0, double t1, double y0, double y1, double d0, double d1, double bev, string color, string finish = "plated steel")
    {
        var j = (i + 1) % 8;
        var len = Math.Sqrt(Math.Pow(o[j].X - o[i].X, 2) + Math.Pow(o[j].Z - o[i].Z, 2));
        var tb = bev / len;
        double[][] inner = [OnEdge(o, i, t0, d0, y0), OnEdge(o, i, t1, d0, y0), OnEdge(o, i, t1, d0, y1), OnEdge(o, i, t0, d0, y1)];
        double[][] outer = [OnEdge(o, i, t0 + tb, d1, y0 + bev), OnEdge(o, i, t1 - tb, d1, y0 + bev), OnEdge(o, i, t1 - tb, d1, y1 - bev), OnEdge(o, i, t0 + tb, d1, y1 - bev)];
        Prism(p, inner, outer, color, finish);
    }

    /// <summary>A flat strip on edge i (a seam, a stripe, a scorch, a hatch's outline), d out from the outline.</summary>
    private static void Strip(Part p, (double X, double Z)[] o, int i, double t0, double t1, double y0, double y1, double d, string color, string finish = "painted plating", bool glow = false, bool glass = false)
    {
        Plate(p, [OnEdge(o, i, t0, d, y0), OnEdge(o, i, t1, d, y0), OnEdge(o, i, t1, d, y1), OnEdge(o, i, t0, d, y1)], color, finish, glow: glow, glass: glass);
    }

    // ---- a storey's shell: the floor with its hatch, the walls, the chamfered corners, the hull band

    private readonly record struct Rect(double X0, double Z0, double X1, double Z1);

    private static List<Rect> Minus(List<Rect> rs, Rect h)
    {
        var o = new List<Rect>();
        foreach (var r in rs)
        {
            if (h.X1 <= r.X0 || h.X0 >= r.X1 || h.Z1 <= r.Z0 || h.Z0 >= r.Z1)
            {
                o.Add(r);
                continue;
            }
            if (h.Z0 > r.Z0) o.Add(r with { Z1 = h.Z0 });
            if (h.Z1 < r.Z1) o.Add(r with { Z0 = h.Z1 });
            double za = Math.Max(r.Z0, h.Z0), zb = Math.Min(r.Z1, h.Z1);
            if (h.X0 > r.X0) o.Add(new Rect(r.X0, za, h.X0, zb));
            if (h.X1 < r.X1) o.Add(new Rect(h.X1, za, r.X1, zb));
        }
        return [.. o.Where(r => r.X1 - r.X0 > 0.02 && r.Z1 - r.Z0 > 0.02)];
    }

    private const int CornerSteps = 4;

    /// <summary>The floor of a room of half sizes hx, hz with chamfer ci at height y0..y1: the middle less the corner squares, then a
    /// stair of strips into each corner up to the diagonal; less the holes.</summary>
    private static void Floor(Part p, double hx, double hz, double ci, double y0, double y1, string color, string finish, params Rect[] holes)
    {
        double x0 = Cx - hx, x1 = Cx + hx, z0 = Cz - hz, z1 = Cz + hz;
        var rs = new List<Rect> { new(x0 + ci, z0, x1 - ci, z1), new(x0, z0 + ci, x0 + ci, z1 - ci), new(x1 - ci, z0 + ci, x1, z1 - ci) };
        var s = ci / CornerSteps;
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            for (var i = 0; i < CornerSteps - 1; i++)
            {
                // Strip i: u (outward along x from the corner square's inner edge) i*s..(i+1)*s, v 0..ci-(i+1)*s.
                double ua = i * s, ub = (i + 1) * s, vb = ci - (i + 1) * s;
                double xa = sx < 0 ? x0 + ci - ub : x1 - ci + ua, xb = sx < 0 ? x0 + ci - ua : x1 - ci + ub;
                double za = sz < 0 ? z0 + ci - vb : z1 - ci, zb = sz < 0 ? z0 + ci : z1 - ci + vb;
                rs.Add(new Rect(xa, za, xb, zb));
            }
        foreach (var h in holes)
            rs = Minus(rs, h);
        foreach (var r in rs)
            Solid(p, r.X0, y0, r.Z0, r.X1, y1, r.Z1, color, finish);
    }

    /// <summary>The walls round a room from y0 to y1: four straight runs (with the doorways and windows in <paramref name="gaps"/>), a
    /// panel across each corner with a stair of hidden boxes behind it, and a dark kick strip along the bottom of each run.</summary>
    private static void Walls(Part p, double hx, double hz, double ci, double y0, double y1, params Gap[] gaps)
    {
        var gy = Math.Round(y0 / Pitch) * Pitch + Slab; // (gaps are measured from the floor's top)
        double x0 = Cx - hx, x1 = Cx + hx, z0 = Cz - hz, z1 = Cz + hz;
        // Runs: 0 the bow (z0), 1 starboard (x1), 2 the stern (z1), 3 port (x0); each from one chamfer to the next.
        for (var side = 0; side < 4; side++)
        {
            var alongX = side % 2 == 0;
            double a0 = alongX ? x0 + ci : z0 + ci, a1 = alongX ? x1 - ci : z1 - ci;
            var face = side switch { 0 => z0, 1 => x1, 2 => z1, _ => x0 };
            var outw = side is 1 or 2 ? 1 : -1;
            void Run(double a, double b, double ya, double yb)
            {
                if (b - a < 0.02 || yb - ya < 0.02)
                    return;
                if (alongX)
                    Solid(p, a, ya, face, b, yb, face + outw * 0.25, Wall, "wall panelling");
                else
                    Solid(p, face, ya, a, face + outw * 0.25, yb, b, Wall, "wall panelling");
            }
            var mine = gaps.Where(g => g.Side == side).OrderBy(g => g.A).ToList();
            var at = a0;
            foreach (var g in mine)
            {
                Run(at, g.A, y0, y1);
                Run(g.A, g.B, y0, g.Sill > 0 ? gy + g.Sill : gy); // (under a window, or the threshold under a door: floor to walk over)
                Run(g.A, g.B, gy + g.Head, y1); // the lintel
                if (g.Sill > 0)
                    Hidden(p, alongX ? g.A : face, gy + g.Sill, alongX ? face : g.A, alongX ? g.B : face + outw * 0.25, gy + g.Head, alongX ? face + outw * 0.25 : g.B);
                at = g.B;
            }
            Run(at, a1, y0, y1);
            // The kick strip, between the gaps that reach the floor.
            at = a0;
            foreach (var g in mine.Where(g => g.Sill <= 0).Append(new Gap(side, a1, a1, 0, 0)))
            {
                if (g.A - at > 0.1)
                {
                    if (alongX)
                        Detail(p, at, gy + 0.003, face - outw * 0.012, g.A, y0 + 0.74, face, WallLow, "matte paint");
                    else
                        Detail(p, face - outw * 0.012, gy + 0.003, at, face, y0 + 0.74, g.A, WallLow, "matte paint");
                }
                at = g.B;
            }
        }
        // The corners: a panel across each (drawn), the hidden stair behind it (the floor's stair's complement), a pipe run up it.
        var s = ci / CornerSteps;
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            double cxa = sx < 0 ? x0 : x1, cza = sz < 0 ? z0 : z1; // the room's corner
            double[] A = P(cxa - sx * ci, y0 + 0.004, cza), B = P(cxa, y0 + 0.004, cza - sz * ci);
            double[] A1 = P(cxa - sx * ci, y1 - 0.004, cza), B1 = P(cxa, y1 - 0.004, cza - sz * ci);
            Plate(p, [A, B, B1, A1], Wall, "wall panelling", thick: 0.03);
            // The stair fills a band 0.25 m deep behind the chamfer (as deep as the walls): no further, or its outer corner would
            // stand out past the hull's diagonal face and stop rounds and people in the air there. Measured out from the room's
            // corner, a step spans ox in [ua - ci, ub - ci] and oz from the chamfer (-ub) to 0.25 m beyond it (0.25 sqrt 2 - ub).
            for (var i = 0; i < CornerSteps; i++)
            {
                double ua = i * s, ub = (i + 1) * s;
                double oxa = ua - ci, oxb = ub - ci, oza = -ub, ozb = 0.25 * Sq2 - ub;
                Hidden(p, cxa + sx * oxa, y0, cza + sz * oza, cxa + sx * oxb, y1, cza + sz * ozb);
            }
        }
    }

    /// <summary>A gap in a wall run: side (0 bow, 1 starboard, 2 stern, 3 port), from A to B along it, open from sill to head over the floor.</summary>
    private readonly record struct Gap(int Side, double A, double B, double Sill, double Head);

    private const double DoorH = 2.25;

    private static Gap Door(int side, double a) => new(side, a, a + 1.5, 0, DoorH);

    /// <summary>A partition from (x0, z0) to (x1, z1) along an axis, 0.25 m thick about the line, y0 to y1, its doorways (from, to)
    /// 2.25 m high; the kick strip on both faces.</summary>
    private static void Partition(Part p, double x0, double z0, double x1, double z1, double y0, double y1, string color, params double[] doors)
    {
        if (!Partitions.TryGetValue(p, out var mine))
            Partitions[p] = mine = [];
        mine.Add((x0, z0, x1, z1));
        var alongX = Math.Abs(z1 - z0) < 1e-6;
        double a0 = alongX ? Math.Min(x0, x1) : Math.Min(z0, z1), a1 = alongX ? Math.Max(x0, x1) : Math.Max(z0, z1), c = alongX ? z0 : x0;
        void Run(double a, double b, double ya, double yb, string col, string fin, double th, bool detail)
        {
            if (b - a < 0.02)
                return;
            if (alongX)
            {
                if (detail) Detail(p, a, ya, c - th, b, yb, c + th, col, fin);
                else Solid(p, a, ya, c - th, b, yb, c + th, col, fin);
            }
            else
            {
                if (detail) Detail(p, c - th, ya, a, c + th, yb, b, col, fin);
                else Solid(p, c - th, ya, a, c + th, yb, b, col, fin);
            }
        }
        var at = a0;
        for (var i = 0; i < doors.Length; i++)
        {
            var d = doors[i];
            Run(at, d, y0, y1, color, "wall panelling", 0.125, false);
            Run(d, d + 1.5, y0 + DoorH, y1, color, "wall panelling", 0.125, false);
            Run(at + 0.05, d - 0.05, y0 + 0.004, y0 + 0.75, WallLow, "matte paint", 0.137, true);
            // The door frame: a hazard-striped sill and dark jambs.
            Run(d - 0.06, d, y0, y0 + DoorH + 0.06, WallTrim, "painted steel", 0.14, true);
            Run(d + 1.5, d + 1.56, y0, y0 + DoorH + 0.06, WallTrim, "painted steel", 0.14, true);
            at = d + 1.5;
        }
        Run(at, a1, y0, y1, color, "wall panelling", 0.125, false);
        Run(at + 0.05, a1 - 0.05, y0 + 0.004, y0 + 0.75, WallLow, "matte paint", 0.137, true);
    }

    /// <summary>The safety rail round storey k's hatch (open at its back edge, where the ladder lands people).</summary>
    private static void HatchRail(Part p, int k)
    {
        var xc = ShaftX[HatchShaft(k)];
        double x0 = xc - ShaftW / 2, x1 = xc + ShaftW / 2, y = Ft(k);
        Solid(p, x0 - 0.06, y, ShaftZ0 - 0.06, x1 + 0.06, y + 1.0, ShaftZ0, Frame, "painted steel");
        Solid(p, x0 - 0.06, y, ShaftZ0, x0, y + 1.0, ShaftZ1 - 0.3, Frame, "painted steel");
        Solid(p, x1, y, ShaftZ0, x1 + 0.06, y + 1.0, ShaftZ1 - 0.3, Frame, "painted steel");
        Detail(p, x0 - 0.075, y + 0.96, ShaftZ0 - 0.075, x1 + 0.075, y + 1.04, ShaftZ0 + 0.015, Hazard, "hazard paint");
        // The hatch's coaming: a hazard band round the opening on the floor.
        Detail(p, x0 - 0.12, y, ShaftZ1, x1 + 0.12, y + 0.012, ShaftZ1 + 0.12, Hazard, "hazard paint");
    }

    private static Rect Hatch(int k)
    {
        var xc = ShaftX[HatchShaft(k)];
        return new Rect(xc - ShaftW / 2, ShaftZ0, xc + ShaftW / 2, ShaftZ1);
    }

    /// <summary>The hull band of a storey: the skin (with its openings), armour slabs over it with the frame showing between them, seams,
    /// worn paint and a little scorching. o: the skin's outline; holes: (edge, t0, t1, y0, y1) left open in the skin.</summary>
    // ---- the hull panels: each storey's four flat faces are parts of their own, so a hit knocks a panel off and opens the room
    // behind it to view instead of taking the whole storey (Karl, 2026-11-15). The corners, the frame ribs and the sponsons stay the
    // storey's. A panel's skin and armour are drawn; a thin hidden slab just outside the storey's wall is what a round hits.

    private static readonly List<Part> Panels = [];
    private static readonly string[] FaceNames = ["Bow", "Stbd", "Stern", "Port"];
    private const double PanelIn = 0.25, PanelOut = 0.3; // (the slab, measured out from the room's face: the wall is 0..0.25, a sponson's back from 0.305)

    /// <summary>Storey k's four hull panels (bow, starboard, stern, port) for the band y0..y1, each with its hidden slab (less the
    /// keep-out on its side: a doorway and what stands outside it) and a socket pair with the storey at the wall's outer face.</summary>
    private static Part[] HullPanels(Part core, int k, string deck, double y0, double y1, params (int Side, double A0, double A1, double Y0, double Y1)[] keepOut)
    {
        double hx = Hx(k), hz = Hz(k), ci = Ci(k), x0 = Cx - hx, x1 = Cx + hx, z0 = Cz - hz, z1 = Cz + hz;
        var o = new Part[4];
        for (var side = 0; side < 4; side++)
        {
            var name = $"Halberd {deck} Hull {FaceNames[side]}";
            var p = new Part(name, Small, Section, $"A hull panel of the Halberd's {deck.ToLowerInvariant()} storey, the {FaceNames[side].ToLowerInvariant()} face: skin and armour slabs. Knocked off, it opens the room behind it.", role: "frame", tool: "welder", material: "steel", color: Skin, group: Group);
            var alongX = side % 2 == 0;
            var outw = side is 1 or 2 ? 1 : -1;
            var face = side switch { 0 => z0, 1 => x1, 2 => z1, _ => x0 };
            double a0 = alongX ? x0 + ci : z0 + ci, a1 = alongX ? x1 - ci : z1 - ci;
            double fa = face + outw * PanelIn, fb = face + outw * PanelOut;
            void Slab(double aa, double ab, double ya, double yb)
            {
                if (ab - aa < 0.05 || yb - ya < 0.05)
                    return;
                if (alongX)
                    Hidden(p, aa, ya, fa, ab, yb, fb);
                else
                    Hidden(p, fa, ya, aa, fb, yb, ab);
            }
            double ylo = y0 + 0.004, yhi = y1 - 0.004;
            var cut = keepOut.Where(q => q.Side == side).ToList();
            if (cut.Count == 0)
                Slab(a0, a1, ylo, yhi);
            else
            {
                var c = cut[0];
                Slab(a0, c.A0, ylo, yhi);
                Slab(c.A1, a1, ylo, yhi);
                Slab(c.A0, c.A1, c.Y1, yhi);
                Slab(c.A0, c.A1, ylo, c.Y0);
            }
            // The socket pair, high on the face (over any doorway: the wall's lintel is there).
            var mid = alongX ? Cx : Cz;
            var dirOut = alongX ? (outw > 0 ? "+z" : "-z") : (outw > 0 ? "+x" : "-x");
            var dirIn = alongX ? (outw > 0 ? "-z" : "+z") : (outw > 0 ? "-x" : "+x");
            var ys = y1 - 0.35;
            if (alongX)
            {
                Sock(core, $"hull{side}", mid, ys, fa, dirOut);
                Sock(p, "back", mid, ys, fa, dirIn);
            }
            else
            {
                Sock(core, $"hull{side}", fa, ys, mid, dirOut);
                Sock(p, "back", fa, ys, mid, dirIn);
            }
            p.Stats(700, 4000, 30, 6.0);
            Panels.Add(p);
            o[side] = p;
        }
        return o;
    }

    /// <summary>Storey k's four panels by name, for the blueprint.</summary>
    private static string PanelName(string deck, int side) => $"Halberd {deck} Hull {FaceNames[side]}";

    private static void HullBand(Part core, Part[] panels, int k, double y0, double y1, (int Edge, double T0, double T1, double Y0, double Y1)[] holes, int armourPattern)
    {
        var o = OctK(k, So);
        Part On(int i) => i % 2 == 0 ? panels[i / 2] : core; // (a flat face is its own part, which a hit can knock off; the corners stay the storey's)
        var p = core;
        // The skin, edge by edge, 3 mm short of the storey's ends so bands above and below never share an edge's plane.
        for (var i = 0; i < 8; i++)
        {
            p = On(i);
            var hs = holes.Where(h => h.Edge == i).ToList();
            double ya = y0 + (i % 2 == 0 ? 0.003 : 0.006), yb = y1 - (i % 2 == 0 ? 0.003 : 0.006);
            if (hs.Count == 0)
            {
                Strip(p, o, i, 0, 1, ya, yb, 0, Skin, "painted plating");
                continue;
            }
            var h = hs[0];
            Strip(p, o, i, 0, h.T0, ya, yb, 0, Skin, "painted plating");
            Strip(p, o, i, h.T1, 1, ya, yb, 0, Skin, "painted plating");
            if (h.Y0 > ya + 0.02)
                Strip(p, o, i, h.T0, h.T1, ya, h.Y0, 0, Skin, "painted plating");
            if (h.Y1 < yb - 0.02)
                Strip(p, o, i, h.T0, h.T1, h.Y1, yb, 0, Skin, "painted plating");
        }
        // The frame: a dark rib up each corner of the octagon (where two edges meet), standing a little proud of the skin.
        p = core;
        for (var i = 0; i < 8; i++)
        {
            var q = o[i];
            Detail(p, q.X - 0.09, y0 + 0.01, q.Z - 0.09, q.X + 0.09, y1 - 0.01, q.Z + 0.09, Frame, "dirty steel");
        }
        // A frame girder round the storey's floor line (the deck's edge), dark.
        foreach (var i in Enumerable.Range(0, 8))
            Strip(On(i), o, i, 0.02, 0.98, y0 + 0.02, y0 + 0.3, 0.05, Frame, "dirty steel");
        // Armour slabs: two per flat face, one on each corner face, leaving the frame and the seams between them; patterns vary the
        // heights so the stack doesn't read as a repeat.
        double dy = y1 - y0;
        double lo = y0 + 0.36, hi = y1 - 0.12;
        double mid = armourPattern switch { 0 => y0 + dy * 0.55, 1 => y0 + dy * 0.4, _ => y0 + dy * 0.68 };
        for (var i = 0; i < 8; i++)
        {
            p = On(i);
            var hs = holes.Where(h => h.Edge == i).ToList();
            var flat = i % 2 == 0; // edges 0, 2, 4, 6 are the flat faces; 1, 3, 5, 7 the corners
            if (flat)
            {
                // Split along the face, the two halves at different heights for a stepped look.
                var col = (i + armourPattern) % 3 == 0 ? Armour2 : Armour;
                if (hs.Count == 0)
                {
                    // One half armoured (stepped: a thick lower slab, a thinner one over it), the other half bare skin with its
                    // panel seams, the armoured side swapping face to face and storey to storey so the pale hull shows through.
                    var left = (i / 2 + k + armourPattern) % 2 == 0;
                    double a0 = left ? 0.04 : 0.52, a1 = left ? 0.48 : 0.96, b0 = left ? 0.52 : 0.04, b1 = left ? 0.96 : 0.48;
                    Slab2(p, o, i, a0, a1, lo, mid - 0.06, 0.02, 0.17, 0.08, col);
                    Slab2(p, o, i, a0, a1, mid, hi, 0.02, 0.13, 0.05, Armour2);
                    Slab2(p, o, i, b0, b1, lo, lo + 0.42, 0.02, 0.1, 0.04, Armour2); // a kick slab along the deck line
                    Strip(p, o, i, (b0 + b1) / 2 - 0.006, (b0 + b1) / 2 + 0.006, lo + 0.46, hi - 0.04, 0.035, Frame, "dirty steel");
                    Strip(p, o, i, b0 + 0.02, b1 - 0.02, mid - 0.012, mid + 0.012, 0.037, Frame, "dirty steel");
                    if ((i + k) % 3 == 0) // a stencilled hazard flash on some bare panels
                        Strip(p, o, i, b0 + 0.06, b0 + 0.2, hi - 0.34, hi - 0.24, 0.036, Hazard, "worn paint");
                }
                else
                {
                    var h = hs[0];
                    if (h.T0 > 0.12)
                        Slab2(p, o, i, 0.04, h.T0 - 0.04, lo, hi, 0.02, 0.17, 0.08, col);
                    if (h.T1 < 0.88)
                        Slab2(p, o, i, h.T1 + 0.04, 0.96, lo, hi, 0.02, 0.17, 0.08, col);
                }
            }
            else if (hs.Count == 0)
            {
                // The corners: a raked slab that steps further out, a primer-red scuff showing on its edge.
                Slab2(p, o, i, 0.08, 0.92, lo + 0.2, hi - 0.1, 0.02, 0.24, 0.1, Armour);
                Strip(p, o, i, 0.2, 0.42, lo + 0.32, lo + 0.5, 0.245, Primer, "worn paint");
            }
        }
    }

    // ---- the storeys

    private static Part Big(string name, string desc) => new(name, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: Skin, group: Group);

    /// <summary>A light on the wall at height 2.4 m over storey k's floor: the socket name and where it is.</summary>
    private static void LampSock(Part p, string name, double x, int k, double z, string dir) => Sock(p, name, x, Ft(k) + 2.45, z, dir);

    /// <summary>The sockets every storey carries: down onto the one below (or the thrust frame), up for the one above, and its ladder up.</summary>
    private static void StackSockets(Part p, int k)
    {
        const double wx = 0.875; // on the port wall's line (storeys 1 to 5), under the cockpit's floor for the last
        Sock(p, "down", wx, Fl(k), Cz, "-y");
        if (k < Storeys - 1)
            Sock(p, "up", k == Storeys - 2 ? wx : wx, Fl(k + 1), Cz, "+y");
        else
            Sock(p, "up", Cx - Hx(k) - 0.125, Fl(k + 1), Cz, "+y");
        if (k < Storeys - 1)
        {
            var xc = ShaftX[LadderShaft(k)];
            Sock(p, "ladder", xc, Ft(k), ShaftZ1 - 0.095, "+y");
        }
    }

    /// <summary>Labelled pipes up a corner panel: three runs (red, blue, yellow) with stencil bands and a valve wheel.</summary>
    private static void CornerPipes(Part p, int k, int sx, int sz, double y0, double y1)
    {
        double hx = Hx(k), hz = Hz(k), ci = Ci(k);
        double cxa = sx < 0 ? Cx - hx : Cx + hx, cza = sz < 0 ? Cz - hz : Cz + hz;
        string[] cols = [PipeRed, PipeBlue, PipeYellow];
        for (var i = 0; i < 3; i++)
        {
            var t = 0.3 + 0.2 * i;
            // A point on the diagonal, 0.12 m in front of it.
            double px = cxa - sx * ci * (1 - t), pz = cza - sz * ci * t;
            px -= sx * 0.12 / Sq2;
            pz -= sz * 0.12 / Sq2;
            Detail(p, px - 0.05, y0 + 0.02, pz - 0.05, px + 0.05, y1 - 0.02, pz + 0.05, cols[i], "painted steel");
            Detail(p, px - 0.062, y0 + 1.5, pz - 0.062, px + 0.062, y0 + 1.62, pz + 0.062, "#e8e4d8", "matte paint"); // (its label)
        }
    }

    private static Part Engineering()
    {
        const int k = 0;
        var p = Big("Halberd Engineering Deck", "The Halberd's lowest storey: the reactor, the drive's plumbing, the workshop and the airlock with the boarding ladder.");
        double y0 = Ft(k), y1 = Fl(k + 1);
        Floor(p, Hx(k), Hz(k), Ci(k), Fl(k), Ft(k), Grate, "painted grating");
        // The airlock: the outer door in the stern wall, a chamber 2.75 by 1.6 m, the inner door forward.
        Walls(p, Hx(k), Hz(k), Ci(k), Fl(k), y1, Door(2, AirlockX));
        Partition(p, 2.6, 6.25, 6.5, 6.25, y0, y1, Wall, AirlockX);
        Partition(p, 3.5, 6.375, 3.5, Cz + Hz(k), y0, y1, Wall);
        Partition(p, 6.5, 6.375, 6.5, Cz + Hz(k), y0, y1, Wall);
        Detail(p, 3.64, y0, 6.4, 6.36, y0 + 0.012, 7.98, Hazard, "hazard paint"); // (the airlock's floor, striped)
        // The ceiling's frames (under storey 2's floor): dark beams across, every 1.75 m.
        foreach (var z in new[] { 2.0, 7.0 })
            Detail(p, Cx - Hx(k) + 1.2, y1 - 0.2, z - 0.07, Cx + Hx(k) - 1.2, y1 - 0.012, z + 0.07, Frame, "dirty steel");
        // The drive's feed: two lagged trunks from the reactor's corner down into the floor, and the fuel main.
        Detail(p, 1.1, y0, 3.8, 1.4, y1 - 0.02, 4.1, "#c9c2a8", "lagging");
        Detail(p, 1.09, y0 + 0.6, 3.79, 1.41, y0 + 0.72, 4.11, PipeRed, "painted steel");
        Detail(p, 1.55, y0, 3.85, 1.75, y1 - 0.02, 4.05, PipeYellow, "painted steel");
        CornerPipes(p, k, 1, -1, y0, y1);
        CornerPipes(p, k, -1, 1, y0, y1);
        var hull = HullPanels(p, k, "Engineering", Fl(k), Fl(k + 1), (2, AirlockX - 0.15, AirlockX + 1.65, Fl(k), Ft(k) + DoorH + 0.15)); // (the doorway, and the platform under it)
        HullBand(p, hull, k, Fl(k), Fl(k + 1), [(4, Tof(4, AirlockX + 1.5, k), Tof(4, AirlockX, k), Ft(k) - 0.004, Ft(k) + DoorH)], 0);
        // The hull at the airlock: a hazard-striped surround and a red/green cycle lamp over it.
        var o = OctK(k, So);
        double ta = Tof(4, AirlockX + 1.5, k), tb = Tof(4, AirlockX, k);
        Strip(hull[2], o, 4, ta - 0.03, tb + 0.03, Ft(k) + DoorH + 0.004, Ft(k) + DoorH + 0.12, 0.012, Hazard, "hazard paint");
        Strip(hull[2], o, 4, tb + 0.01, tb + 0.03, Ft(k), Ft(k) + DoorH, 0.012, Hazard, "hazard paint");
        Strip(hull[2], o, 4, ta - 0.03, ta - 0.01, Ft(k), Ft(k) + DoorH, 0.012, Hazard, "hazard paint");
        Strip(hull[2], o, 4, (ta + tb) / 2 - 0.02, (ta + tb) / 2 + 0.02, Ft(k) + DoorH + 0.2, Ft(k) + DoorH + 0.32, 0.03, "#40e070", glow: true);
        // The boarding platform outside the outer door: a grated step with rails either side, the ladder on its edge.
        double pz0 = Cz + Hz(k) + 0.25, pz1 = Cz + Hz(k) + 1.25;
        Solid(p, AirlockX - 0.1, 0.0, pz0, AirlockX + 1.6, Ft(k), pz1, Grate, "painted grating");
        Detail(p, AirlockX - 0.1, Ft(k) - 0.06, pz1 - 0.012, AirlockX + 1.6, Ft(k), pz1 + 0.004, Hazard, "hazard paint");
        foreach (var x in new[] { AirlockX - 0.1, AirlockX + 1.54 })
        {
            Solid(p, x, Ft(k), pz0 + 0.09, x + 0.06, Ft(k) + 1.05, pz1, Frame, "painted steel");
            Detail(p, x - 0.01, Ft(k) + 1.0, pz0 + 0.05, x + 0.07, Ft(k) + 1.08, pz1 + 0.01, Hazard, "hazard paint");
        }
        foreach (var x in new[] { AirlockX + 0.1, AirlockX + 1.4 }) // (its brackets under it)
            Detail(p, x - 0.05, -0.6, pz0 + 0.02, x + 0.05, -0.002, pz0 + 0.7, Frame, "dirty steel");
        StackSockets(p, k);
        // Fittings: the reactor to port, the boards to starboard, the workshop and stores aft either side of the airlock.
        Sock(p, "reactor", 2.6, y0, 2.85, "+y");
        Sock(p, "board", Cx + Hx(k), y0 + 0.9, 3.0, "-x");
        Sock(p, "panel", Cx + Hx(k), y0 + 0.6, 4.8, "-x");
        Sock(p, "battery0", 8.75, y0, 5.8, "+y");
        Sock(p, "battery1", 8.75, y0, 6.15, "+y");
        Sock(p, "pipes", 5.0, y0 + 2.75, Cz - Hz(k), "+z");
        Sock(p, "rack", Cx - Hx(k), y0 + 0.9, 6.1, "+x");
        Sock(p, "crate0", 7.4, y0, 7.4, "+y");
        Sock(p, "crate1", 7.9, y0, 7.4, "+y");
        Sock(p, "suit0", 3.625, y0 + 0.125, 7.1, "+x");
        Sock(p, "suit1", 6.375, y0 + 0.125, 7.1, "-x");
        Sock(p, "doorOuter", AirlockX, y0 + 1.125, Cz + Hz(k) + 0.125, "+x");
        Sock(p, "doorInner", AirlockX, y0 + 1.125, 6.25, "+x");
        Sock(p, "boarding", AirlockX + 0.75, 0.125, Cz + Hz(k) + 1.25, "+z");
        LampSock(p, "lampA", Cx - Hx(k), k, 4.5, "+x");
        LampSock(p, "lampB", Cx + Hx(k), k, 6.0, "-x");
        LampSock(p, "lampC", 7.3, k, Cz - Hz(k), "+z");
        LampSock(p, "lampLock", 5.0, k, 6.375, "+z");
        Sock(p, "switch", 3.375, y0 + 1.3, 7.0, "-x");
        return p.Stats(13200, 150000, 40, 20.0); // (the storey's frame: tough, so its hull panels go first)
    }

    private const double AirlockX = 4.25; // the airlock's doorways, x 4.25..5.75

    /// <summary>How far along edge e of storey k's skin x (or z) is: for placing an opening.</summary>
    private static double Tof(int e, double a, int k)
    {
        var o = OctK(k, So);
        var j = (e + 1) % 8;
        var alongX = Math.Abs(o[j].Z - o[e].Z) < 1e-6;
        return alongX ? (a - o[e].X) / (o[j].X - o[e].X) : (a - o[e].Z) / (o[j].Z - o[e].Z);
    }

    private static Part RobotBay()
    {
        const int k = 1;
        var p = Big("Halberd Robot Bay Deck", "The Halberd's second storey: robot shells in their cradles with the link couches facing them, and the armoury.");
        double y0 = Ft(k), y1 = Fl(k + 1);
        Floor(p, Hx(k), Hz(k), Ci(k), Fl(k), Ft(k), Deck, "painted deck plate", Hatch(k));
        Detail(p, 1.3, y0, 2.4, 3.2, y0 + 0.012, 6.6, "#2a2d2f", "rubber matting"); // (the robot bay's mat)
        Walls(p, Hx(k), Hz(k), Ci(k), Fl(k), y1);
        HatchRail(p, k);
        // The armoury to starboard behind a partition with a doorway.
        Partition(p, 7.2, Cz - Hz(k) + 0.01, 7.2, Cz + Hz(k) - 0.01, y0, y1, Wall, 5.0);
        // Charging bay markings on the floor in front of each cradle.
        foreach (var z in new[] { 3.0, 4.5, 6.0 })
            Detail(p, 1.02, y0, z - 0.5, 1.9, y0 + 0.013, z + 0.5, Hazard, "hazard paint");
        foreach (var z in new[] { 3.0, 4.5, 6.0 })
            Detail(p, 1.06, y0, z - 0.44, 1.86, y0 + 0.02, z + 0.44, "#2a2d2f", "rubber matting");
        CornerPipes(p, k, -1, -1, y0, y1);
        CornerPipes(p, k, 1, 1, y0, y1);
        HullBand(p, HullPanels(p, k, k == 1 ? "Robot Bay" : "Ops", Fl(k), Fl(k + 1)), k, Fl(k), Fl(k + 1), [], 1);
        // The PDC sponsons on the flanks: an armoured drum on each side (port and starboard), what the turntable goes on.
        foreach (var s in new[] { -1, 1 })
            Sponson(p, s, 0, (y0 + y1) / 2 + 0.3);
        StackSockets(p, k);
        foreach (var (z, i) in new[] { (3.0, 0), (4.5, 1), (6.0, 2) })
            Sock(p, $"cradle{i}", Cx - Hx(k), y0 + 0.125, z, "+x");
        Sock(p, "gun", 5.6, y0, 1.5, "+y");
        Sock(p, "gunSeat", 5.6, y0, 2.4, "+y");
        Sock(p, "couch0", 3.3, y0, 2.4, "+y");
        Sock(p, "couch1", 3.3, y0, 6.6, "+y");
        Sock(p, "armsRack", Cx + Hx(k), y0 + 0.9, 3.7, "-x");
        Sock(p, "locker0", Cx + Hx(k), y0 + 0.125, 5.5, "-x");
        Sock(p, "locker1", Cx + Hx(k), y0 + 0.125, 6.15, "-x");
        Sock(p, "ammo0", 8.1, y0, 2.45, "+y");
        Sock(p, "ammo1", 8.1, y0, 2.95, "+y");
        Sock(p, "doorArmoury", 7.2, y0 + 1.125, 5.0, "+z");
        LampSock(p, "lampA", Cx - Hx(k), k, 3.75, "+x");
        LampSock(p, "lampB", Cx - Hx(k), k, 5.25, "+x");
        LampSock(p, "lampC", Cx + Hx(k), k, 2.6, "-x");
        LampSock(p, "lampD", 4.5, k, Cz + Hz(k), "-z");
        Sock(p, "switch", 7.075, y0 + 1.3, 3.8, "-x");
        Sock(p, "pdcSwitch", 7.075, y0 + 1.3, 4.2, "-x");
        return p.Stats(12200, 150000, 40, 20.0);
    }

    /// <summary>
    /// A PDC housing on face side s (-1 port, 1 starboard; axis 0 x, 1 z) at height y (Karl, 2026-11-15: the guns pull back in under a
    /// hatch that seals): an armoured box standing out of the face, open at its mouth, deep and wide enough for the turret lying level.
    /// A piston on its back plate (the socket pdcPort etc.) pushes the turret out past the mouth; the hatch hangs on the mouth's top edge.
    /// </summary>
    private static void Sponson(Part p, int s, int axis, double y)
    {
        const double Wall = 0.06;
        double face = axis == 0 ? (s < 0 ? Cx - 4.0 - So : Cx + 4.0 + So) : (s < 0 ? Cz - 3.5 - So : Cz + 3.5 + So);
        double along = axis == 0 ? Cz : Cx;
        double[] At(double a, double v, double outv) => axis == 0 ? P(face + s * outv, y + v, along + a) : P(along + a, y + v, face + s * outv);
        // A box between (a0, v0, o0) and (a1, v1, o1) in the housing's frame: drawn and solid, or a drawn trim.
        void Slab(double a0, double a1, double v0, double v1, double o0, double o1, string color, string finish, bool trim = false)
        {
            var c0 = At(a0, v0, o0);
            var c1 = At(a1, v1, o1);
            p.Add(Box([Math.Min(c0[0], c1[0]), Math.Min(c0[1], c1[1]), Math.Min(c0[2], c1[2])], [Math.Max(c0[0], c1[0]), Math.Max(c0[1], c1[1]), Math.Max(c0[2], c1[2])], color, detail: trim, shape: Kw(("finish", finish))));
        }
        const double A = PdcHalfA, V = PdcHalfV, D = PdcDepth, B = 0.035 + Wall;
        Slab(-A - Wall, A + Wall, -V - Wall, V + Wall, 0.035, B, Frame, "dirty steel"); // the back plate on the skin
        Slab(-A - Wall, A + Wall, V, V + Wall, B + 0.003, D, Armour, "plated steel"); // top
        Slab(-A - Wall, A + Wall, -V - Wall, -V, B + 0.003, D, Armour, "plated steel"); // bottom
        Slab(-A - Wall, -A, -V + 0.003, V - 0.003, B + 0.003, D - 0.004, Armour2, "plated steel"); // the two ends
        Slab(A, A + Wall, -V + 0.003, V - 0.003, B + 0.003, D - 0.004, Armour2, "plated steel");
        Slab(-A - Wall - 0.02, A + Wall + 0.02, V + Wall, V + Wall + 0.02, D - 0.12, D + 0.004, Hazard, "hazard paint", true); // hazard lips over and under the mouth
        Slab(-A - Wall - 0.02, A + Wall + 0.02, -V - Wall - 0.02, -V - Wall, D - 0.12, D + 0.004, Hazard, "hazard paint", true);
        Slab(-0.35, 0.35, -V - Wall - 0.06, -V - Wall - 0.02, 0.2, D - 0.3, "#30363a", "dirty steel", true); // a rib under it
        var name = axis == 0 ? (s < 0 ? "pdcPort" : "pdcStbd") : (s < 0 ? "pdcBow" : "pdcStern");
        var dir = axis == 0 ? (s < 0 ? "-x" : "+x") : (s < 0 ? "-z" : "+z");
        p.Sock(name, At(0, 0, B), dir);
        p.Sock(name + "Hatch", At(0, V + Wall, D), dir);
    }

    // The PDC housing's inside (half its width along the face and half its height) and how far its mouth stands out of the skin;
    // the turret lies level in it when pulled in (the gun and its box are 2 m long, 1.4 m of it ahead of the turntable's axis).
    private const double PdcHalfA = 1.5, PdcHalfV = 0.42, PdcDepth = 1.5;
    private const double HatchHold = 3.0; // (seconds a hatch stays open after its turret is pulled in: the piston's whole stroke takes 2.5)
    private const int PdcSpinFlank = 0, PdcSpinEnds = 1; // (the gun's turn on the hinge's leaf: lays each turret level along its housing)

    /// <summary>The hatch over a PDC housing's mouth: an armoured leaf hung from the mouth's top edge that swings up and out when
    /// the turret is pushed out, and shuts over it when pulled in (its Open input: wired from the deck's PDC data point).</summary>
    private static Part PdcHatch()
    {
        var p = new Part("Halberd PDC Hatch", Small, Section, "The armoured hatch over a PDC housing: opens when its turret is pushed out, seals when it is pulled in.", role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
        double a = PdcHalfA + 0.06, v = 2 * (PdcHalfV + 0.06);
        p.Add(Box([-a, -v, 0.004], [a, 0, 0.064], Armour, shape: Kw(("finish", "plated steel"))));
        foreach (var x in new[] { -0.75, 0.0, 0.75 })
            p.Add(Box([x - 0.05, -v + 0.08, 0.064], [x + 0.05, -0.08, 0.09], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-a + 0.04, -v + 0.03, 0.064], [a - 0.04, -v + 0.09, 0.078], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("hang", [0, 0, 0], "-z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+x"), ("pivot", Py.L(0, 0, 0.034)), ("angle", -90), ("seconds", 1.5), ("hang", "hang"))));
        return p.Stats(260, 900, 30, 4.0);
    }

    private static Part MedDeck()
    {
        const int k = 2;
        var p = Big("Halberd Med Deck", "The Halberd's third storey: the med bay forward with two treatment couches, the galley and mess aft.");
        double y0 = Ft(k), y1 = Fl(k + 1);
        Floor(p, Hx(k), Hz(k), Ci(k), Fl(k), Ft(k), Deck, "painted deck plate", Hatch(k));
        Walls(p, Hx(k), Hz(k), Ci(k), Fl(k), y1);
        HatchRail(p, k);
        // The med bay across the bow, behind a partition with its doorway to port; a green stripe and a pale floor.
        Partition(p, Cx - Hx(k) + 0.01, 3.4, Cx + Hx(k) - 0.01, 3.4, y0, y1, MedWhite, 1.75);
        Detail(p, 2.2, y0, 1.03, 7.8, y0 + 0.012, 3.26, "#c9d0cc", "gloss paint");
        Detail(p, 2.2, y0 + 1.2, 1.0 - 0.0, 7.8, y0 + 1.32, 1.012, MedGreen, "gloss paint");
        // The scanner over each couch: a ceiling arm with a ring of light.
        foreach (var x in new[] { 3.4, 6.0 })
        {
            Detail(p, x - 0.06, y1 - 0.55, 2.0 - 0.06, x + 0.06, y1 - 0.01, 2.0 + 0.06, Frame, "painted steel");
            Detail(p, x - 0.35, y1 - 0.62, 2.0 - 0.35, x + 0.35, y1 - 0.55, 2.0 + 0.35, MedWhite, "gloss paint");
            Detail(p, x - 0.28, y1 - 0.635, 2.0 - 0.28, x + 0.28, y1 - 0.62, 2.0 + 0.28, "#b8f0e0", glow: true);
        }
        // The galley: a counter along the stern wall with the coffee machine, the stove and lockers.
        CornerPipes(p, k, -1, 1, y0, y1);
        CornerPipes(p, k, 1, 1, y0, y1);
        HullBand(p, HullPanels(p, k, "Med", Fl(k), Fl(k + 1)), k, Fl(k), Fl(k + 1), [], 2);
        StackSockets(p, k);
        Sock(p, "medL", 3.4, y0 + 0.8, Cz - Hz(k), "+z");
        Sock(p, "medR", 6.0, y0 + 0.8, Cz - Hz(k), "+z");
        Sock(p, "cabinet", 4.7, y0 + 0.85, Cz - Hz(k), "+z");
        Sock(p, "decon", 7.35, y0 + 0.125, Cz - Hz(k), "+z");
        Sock(p, "medLocker", Cx + Hx(k), y0 + 0.125, 2.6, "-x");
        Sock(p, "doorMed", 3.25, y0 + 1.125, 3.4, "-x");
        Sock(p, "stove", 2.75, y0 + 0.45, Cz + Hz(k), "-z");
        Sock(p, "coffee", 3.7, y0 + 0.125, Cz + Hz(k), "-z");
        Sock(p, "galleyLocker", 4.45, y0 + 0.125, Cz + Hz(k), "-z");
        Sock(p, "table", 7.6, y0, 6.5, "+y");
        LampSock(p, "lampMedA", 2.6, k, 3.275, "-z");
        LampSock(p, "lampMedB", 7.4, k, 3.275, "-z");
        LampSock(p, "lampGalleyA", Cx - Hx(k), k, 5.4, "+x");
        LampSock(p, "lampGalleyB", Cx + Hx(k), k, 5.4, "-x");
        Sock(p, "switchMed", 1.5, y0 + 1.3, 3.275, "-z");
        Sock(p, "switch", 1.5, y0 + 1.3, 3.525, "+z");
        return p.Stats(12200, 150000, 40, 20.0);
    }

    private static Part CrewDeck()
    {
        const int k = 3;
        var p = Big("Halberd Crew Deck", "The Halberd's fourth storey: four crash couches in the bunk room, the head, lockers and a desk.");
        double y0 = Ft(k), y1 = Fl(k + 1);
        Floor(p, Hx(k), Hz(k), Ci(k), Fl(k), Ft(k), Deck, "painted deck plate", Hatch(k));
        Walls(p, Hx(k), Hz(k), Ci(k), Fl(k), y1);
        HatchRail(p, k);
        // The bunk room across the bow; the head aft to starboard.
        Partition(p, Cx - Hx(k) + 0.01, 3.6, Cx + Hx(k) - 0.01, 3.6, y0, y1, Wall, 1.75);
        Partition(p, 6.4, 6.25, Cx + Hx(k) - 0.01, 6.25, y0, y1, Wall);
        Partition(p, 6.4, 6.375, 6.4, Cz + Hz(k) - 0.01, y0, y1, Wall, 6.45);
        Detail(p, 6.54, y0, 6.39, 7.8, y0 + 0.012, 7.98, "#5b6268", "rubber matting"); // (the head's floor)
        CornerPipes(p, k, -1, 1, y0, y1);
        HullBand(p, HullPanels(p, k, "Crew", Fl(k), Fl(k + 1)), k, Fl(k), Fl(k + 1), [], 0);
        StackSockets(p, k);
        Sock(p, "couch0", 3.2, y0 + 0.125, Cz - Hz(k), "+z");
        Sock(p, "couch1", 6.8, y0 + 0.125, Cz - Hz(k), "+z");
        Sock(p, "couch2", 4.5, y0 + 0.125, 3.475, "-z");
        Sock(p, "couch3", 6.8, y0 + 0.125, 3.475, "-z");
        Sock(p, "doorBunk", 3.25, y0 + 1.125, 3.6, "-x");
        Sock(p, "doorHead", 6.4, y0 + 1.125, 6.45, "+z");
        Sock(p, "toilet", Cx + Hx(k), y0 + 0.125, 6.6, "-x");
        Sock(p, "basin", 8.0, y0 + 0.125, 6.375, "+z");
        Sock(p, "desk", Cx - Hx(k), y0 + 0.125, 5.4, "+x");
        Sock(p, "locker0", 2.6, y0 + 0.125, Cz + Hz(k), "-z");
        Sock(p, "locker1", 3.25, y0 + 0.125, Cz + Hz(k), "-z");
        Sock(p, "locker2", 3.9, y0 + 0.125, Cz + Hz(k), "-z");
        LampSock(p, "lampBunkA", 3.0, k, 3.475, "-z");
        LampSock(p, "lampBunkB", 7.0, k, 3.475, "-z");
        LampSock(p, "lampHall", Cx - Hx(k), k, 6.4, "+x");
        LampSock(p, "lampHead", 7.6, k, 6.375, "+z");
        Sock(p, "switch", 5.0, y0 + 1.3, 3.725, "+z");
        return p.Stats(12200, 150000, 40, 20.0);
    }

    private static Part OpsDeck()
    {
        const int k = 4;
        var p = Big("Halberd Ops Deck", "The Halberd's fifth storey: the gunnery stations that work the point-defence turrets, the plot table and the ship's locker.");
        double y0 = Ft(k), y1 = Fl(k + 1);
        Floor(p, Hx(k), Hz(k), Ci(k), Fl(k), Ft(k), Deck, "painted deck plate", Hatch(k));
        Walls(p, Hx(k), Hz(k), Ci(k), Fl(k), y1);
        HatchRail(p, k);
        // A wall of screens across the bow over the gunnery stations.
        for (var i = 0; i < 4; i++)
        {
            var x = 2.6 + i * 1.6;
            Detail(p, x - 0.62, y0 + 1.25, Cz - Hz(k) + 0.0, x + 0.62, y0 + 2.05, Cz - Hz(k) + 0.04, Console, "plastic");
            Detail(p, x - 0.55, y0 + 1.31, Cz - Hz(k) + 0.04, x + 0.55, y0 + 1.99, Cz - Hz(k) + 0.056, i % 2 == 0 ? Screen : "#e0a040", glow: true);
        }
        CornerPipes(p, k, -1, 1, y0, y1);
        CornerPipes(p, k, 1, 1, y0, y1);
        HullBand(p, HullPanels(p, k, k == 1 ? "Robot Bay" : "Ops", Fl(k), Fl(k + 1)), k, Fl(k), Fl(k + 1), [], 1);
        // The PDC sponsons at the bow and stern faces.
        foreach (var s in new[] { -1, 1 })
            Sponson(p, s, 1, (y0 + y1) / 2 + 0.2);
        // The shoulder: the band from this storey's skin up to the cockpit's, with hidden boxes under it.
        Shoulder(p);
        StackSockets(p, k);
        Sock(p, "gun", 8.3, y0, Cz, "+y");
        Sock(p, "gunSeat", 7.35, y0, Cz, "+y");
        Sock(p, "plot", 7.6, y0, 6.4, "+y");
        Sock(p, "locker0", 2.55, y0 + 0.125, Cz + Hz(k), "-z");
        Sock(p, "locker1", 3.2, y0 + 0.125, Cz + Hz(k), "-z");
        Sock(p, "armsRack", Cx - Hx(k), y0 + 0.9, 5.6, "+x");
        LampSock(p, "lampA", Cx - Hx(k), k, 3.0, "+x");
        LampSock(p, "lampB", Cx + Hx(k), k, 3.0, "-x");
        LampSock(p, "lampC", Cx + Hx(k), k, 6.0, "-x");
        LampSock(p, "lampD", 4.0, k, Cz + Hz(k), "-z");
        Sock(p, "switch", 4.2, y0 + 1.3, Cz + Hz(k), "-z");
        Sock(p, "pdcSwitch", 4.65, y0 + 1.3, Cz + Hz(k), "-z");
        return p.Stats(12200, 150000, 40, 20.0);
    }

    private const double ShoulderTop = 17.0;

    private static void Shoulder(Part p)
    {
        var a = OctK(4, So);
        var b = OctK(5, So);
        Ring(p, a, Fl(5) + 0.003, b, ShoulderTop, Armour, "plated steel", false);
        // Vents and a sensor blister on the shoulder.
        foreach (var s in new[] { -1, 1 })
            for (var i = 0; i < 4; i++)
            {
                var z = Cz - 1.2 + i * 0.8;
                double x = Cx + s * (Hx(5) + So + 0.55);
                Detail(p, x - 0.2, Fl(5) + 0.45 + 0.0, z - 0.08, x + 0.2, Fl(5) + 0.62, z + 0.08, Frame, "dirty steel");
            }
    }

    private static Part Cockpit()
    {
        const int k = 5;
        var p = Big("Halberd Cockpit Deck", "The Halberd's top storey under the nose: two flight couches before the screens of the outside view, the flight computer behind them.");
        double y0 = Ft(k), y1 = Fl(k + 1);
        // The floor spans storey 5's room (the shoulder's slab outside it is the ops deck's).
        Floor(p, Hx(4), Hz(4), Ci(4), Fl(k), Ft(k), Deck, "painted deck plate", Hatch(k));
        // No window (Karl, 2026-11-15): the bow wall is whole and the pilots fly by the screens across it, 1.1 m high from 1.05 m over the floor.
        const double Sill = 1.05, Head = 2.15;
        Walls(p, Hx(k), Hz(k), Ci(k), Ft(k), y1);
        HatchRail(p, k);
        // The dashboard under the visor: a raked desk with screens and switches.
        Solid(p, 3.1, y0, Cz - Hz(k), 6.9, y0 + 0.75, Cz - Hz(k) + 0.6, Console, "plastic");
        Detail(p, 3.15, y0 + 0.75, Cz - Hz(k) + 0.02, 6.85, y0 + 0.9, Cz - Hz(k) + 0.56, "#30363a", "plastic");
        foreach (var (x, c) in new[] { (3.5, Screen), (4.4, "#5fd06a"), (5.6, Amber), (6.5, Screen) })
            Detail(p, x - 0.32, y0 + 0.905, Cz - Hz(k) + 0.08, x + 0.32, y0 + 0.92, Cz - Hz(k) + 0.48, c, glow: true);
        // The outside view: three screens in a dark bezel across the bow wall where the visor was, an amber strip under them.
        double ya = y0 + Sill, yb = y0 + Head, zw = Cz - Hz(k);
        double bx0 = Cx - Hx(k) + Ci(k) + 0.1, bx1 = Cx + Hx(k) - Ci(k) - 0.1;
        Detail(p, bx0, ya - 0.06, zw + 0.004, bx1, yb + 0.06, zw + 0.03, "#22272a", "plastic");
        var third = (bx1 - bx0) / 3;
        for (var i = 0; i < 3; i++)
            Detail(p, bx0 + i * third + 0.05, ya, zw + 0.03, bx0 + (i + 1) * third - 0.05, yb, zw + 0.034, "#3d6f8f", glow: true);
        Detail(p, bx0 + 0.3, ya - 0.05, zw + 0.03, bx1 - 0.3, ya - 0.035, zw + 0.034, Amber, glow: true);
        HullBand(p, HullPanels(p, k, "Cockpit", ShoulderTop, y1), k, ShoulderTop, y1, [], 2);
        // The skin's lower part between the slab and the shoulder's top (behind the shoulder) isn't drawn; a hidden wall stands there.
        Hidden(p, 0.75, Fl(k), 4.0, 1.0, Fl(k) + 0.05, 5.0); // (what the "down" socket is on, under the shoulder)
        StackSockets(p, k);
        Sock(p, "pilot", 4.1, y0, Cz - Hz(k) + 1.3, "+y");
        Sock(p, "copilot", 5.9, y0, Cz - Hz(k) + 1.3, "+y");
        Sock(p, "gunModule", 5.0, y0 + 0.4, Cz - Hz(k) + 0.6, "+z");
        Sock(p, "computer", 6.6, y0, 6.2, "+y");
        Sock(p, "chart", Cx - Hx(k), y0 + 0.475, 5.3, "+x");
        LampSock(p, "lampA", Cx - Hx(k), k, 4.0, "+x");
        LampSock(p, "lampB", Cx + Hx(k), k, 4.0, "-x");
        LampSock(p, "lampC", 5.0, k, Cz + Hz(k), "-z");
        Sock(p, "switch", 3.4, y0 + 1.3, Cz + Hz(k), "-z");
        return p.Stats(6200, 100000, 40, 16.0);
    }

    // ---- the nose: the cockpit's ceiling, the cap, the sensor mast, the railgun's shroud

    private static Part Nose()
    {
        var p = Big("Halberd Nose", "The Halberd's nose: armoured cap over the cockpit, the railgun's shroud, a sensor mast and a dish.");
        const int k = 5;
        double y0 = Fl(k + 1);
        // The cockpit's ceiling (with a hole for the railgun's breech), lit by a strip.
        double hx = Hx(k), hz = Hz(k), ci = Ci(k);
        Floor(p, hx + 0.25, hz + 0.25, ci + 0.1, y0, y0 + Slab, Ceil, "wall panelling");
        Detail(p, Cx - 1.6, y0 - 0.02, Cz - 0.08, Cx + 1.6, y0, Cz + 0.08, Lamp, glow: true);
        // The cap: from storey 5's skin up and in to a raked top, lower at the bow so the visor sees out.
        var a = OctK(k, So);
        var mid = Oct(hx - 0.6, hz - 0.6, ci * 0.6, So);
        var top = Oct(hx - 1.3, hz - 1.0, ci * 0.4, So);
        double ym = y0 + 0.9;
        for (var i = 0; i < 8; i++)
        {
            var j = (i + 1) % 8;
            double yi = ym - (a[i].Z < Cz ? 0.3 : 0), yj = ym - (a[j].Z < Cz ? 0.3 : 0);
            Quad(p, P(a[i].X, y0 + 0.003, a[i].Z), P(a[j].X, y0 + 0.003, a[j].Z), P(mid[j].X, yj, mid[j].Z), P(mid[i].X, yi, mid[i].Z), i % 2 == 0 ? Armour : Armour2, "plated steel");
            double ti = NoseTop - (top[i].Z < Cz ? 0.5 : 0), tj = NoseTop - (top[j].Z < Cz ? 0.5 : 0);
            Quad(p, P(mid[i].X, yi, mid[i].Z), P(mid[j].X, yj, mid[j].Z), P(top[j].X, tj, top[j].Z), P(top[i].X, ti, top[i].Z), i % 2 == 0 ? Skin : Armour, "painted plating");
        }
        Plate(p, [.. top.Select(q => P(q.X, NoseTop - (q.Z < Cz ? 0.5 : 0), q.Z))], Armour2, "plated steel");
        // Hidden boxes for the cap, round the gun.
        Hidden(p, Cx - hx + 0.3, y0 + Slab, Cz - hz + 0.3, Cx + hx - 0.3, y0 + 0.8, Cz + 0.1);
        Hidden(p, Cx - hx + 0.3, y0 + Slab, Cz + 1.1, Cx + hx - 0.3, y0 + 0.8, Cz + hz - 0.3);
        Hidden(p, Cx - hx + 0.3, y0 + Slab, Cz + 0.1, Cx - 0.5, y0 + 0.8, Cz + 1.1);
        Hidden(p, Cx + 0.5, y0 + Slab, Cz + 0.1, Cx + hx - 0.3, y0 + 0.8, Cz + 1.1);
        // The railgun's shroud: a raised armoured collar round the barrel's exit on top.
        double gz = Cz + 0.6, ty = NoseTop - 0.05;
        double[][] Oct8(double r, double y) => [.. Enumerable.Range(0, 8).Select(i =>
        {
            var an = Math.PI / 8 + i * Math.PI / 4;
            return P(Cx + r * Math.Cos(an), y, gz + r * Math.Sin(an));
        })];
        Prism(p, Oct8(0.7, ty - 0.2), Oct8(0.48, ty + 0.55), Armour, "plated steel");
        Prism(p, Oct8(0.5, ty + 0.55), Oct8(0.46, ty + 0.62), Hazard, "hazard paint");
        // The sensor mast aft, a dish on a stalk to starboard, two antennae with red tips.
        Solid(p, Cx - 0.08, NoseTop - 0.1, Cz + 1.35, Cx + 0.08, NoseTop + 1.9, Cz + 1.51, Frame, "painted steel");
        foreach (var y in new[] { 0.7, 1.2, 1.6 })
            Detail(p, Cx - 0.45, NoseTop + y, Cz + 1.4, Cx + 0.45, NoseTop + y + 0.05, Cz + 1.46, Frame, "painted steel");
        Detail(p, Cx - 0.07, NoseTop + 1.9, Cz + 1.36, Cx + 0.07, NoseTop + 2.04, Cz + 1.5, "#e03020", glow: true);
        Solid(p, Cx + 1.0, NoseTop - 0.3, Cz + 0.9, Cx + 1.12, NoseTop + 0.6, Cz + 1.02, Frame, "painted steel");
        Prism(p, Oct8Off(Cx + 1.06, NoseTop + 0.6, Cz + 0.96, 0.08), Oct8Off(Cx + 1.06, NoseTop + 0.85, Cz + 0.96, 0.5), "#c8ccc8", "painted steel");
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, Cx + s * 1.6 - 0.03, NoseTop - 0.4, Cz - 0.6, Cx + s * 1.6 + 0.03, NoseTop + 1.0, Cz - 0.54, Frame, "steel");
            Detail(p, Cx + s * 1.6 - 0.04, NoseTop + 1.0, Cz - 0.61, Cx + s * 1.6 + 0.04, NoseTop + 1.08, Cz - 0.53, "#e03020", glow: true);
        }
        // Running lights on the cap's corners (port red, starboard green).
        Detail(p, a[7].X + 0.02, y0 + 0.2, a[7].Z - 0.01, a[7].X + 0.14, y0 + 0.32, a[7].Z + 0.11, "#ff3020", glow: true);
        Detail(p, a[2].X - 0.14, y0 + 0.2, a[2].Z - 0.01, a[2].X - 0.02, y0 + 0.32, a[2].Z + 0.11, "#30ff60", glow: true);
        Sock(p, "down", Cx - Hx(k) - 0.125, y0, Cz, "-y");
        Sock(p, "railgun", Cx, y0 + Slab, Cz + 0.6, "+y");
        // The radar's plinth on the cap, to port of the mast: the Long Range Sensor stands on it.
        Solid(p, Cx - 1.27, NoseTop - 0.1, Cz + 0.93, Cx - 0.93, NoseTop + 0.08, Cz + 1.27, Frame, "painted steel");
        Sock(p, "sensor", Cx - 1.1, NoseTop + 0.08, Cz + 1.1, "+y");
        return p.Stats(5000, 14000, 35, 12.0);
    }

    private static double[][] Oct8Off(double cx, double y, double cz, double r) => [.. Enumerable.Range(0, 8).Select(i =>
    {
        var an = Math.PI / 8 + i * Math.PI / 4;
        return P(cx + r * Math.Cos(an), y, cz + r * Math.Sin(an));
    })];

    // ---- the thrust frame (the fuel tank), the drive, the legs

    private const double DriveForce = 4500000, JetForce = 500000, JetBurn = 3.0;

    private static Part ThrustFrame()
    {
        var p = new Part("Halberd Thrust Frame", Small, Section, "The Halberd's thrust frame under the engineering deck: the fuel tankage, the drive's mount and the landing legs' roots.", role: "fueltank", tool: "welder", material: "steel", color: Armour, group: Group);
        var a = OctK(0, So);
        var m = Oct(3.6, 3.1, 1.0, So);
        var b = Oct(2.6, 2.2, 0.8, So);
        const double ym = -1.1;
        Ring(p, a, -0.003, m, ym, Armour, "plated steel", false);
        Ring(p, m, ym, b, FrameBottom, Armour2, "plated steel", false);
        Cap(p, b, FrameBottom, Frame, "soot");
        // Frame ribs down the taper, a hazard band at the waist, primer scuffs, vents.
        for (var i = 0; i < 8; i++)
        {
            double[] t0 = P(a[i].X, -0.02, a[i].Z), t1 = P(m[i].X, ym, m[i].Z), t2 = P(b[i].X, FrameBottom + 0.02, b[i].Z);
            Bar(p, t0, t1, 0.08);
            Bar(p, t1, t2, 0.08);
        }
        for (var i = 0; i < 8; i++)
            Strip(p, m, i, 0.08, 0.92, ym - 0.2, ym - 0.05, 0.03, Hazard, "hazard paint");
        // The thrust ring under it: a heavy dark collar the drive bolts to.
        Prism(p, Oct8Off(Cx, FrameBottom, Cz, 1.35), Oct8Off(Cx, FrameBottom - 0.35, Cz, 1.2), Frame, "dirty steel");
        // Hidden: the tankage, in two blocks (above and below the waist).
        Hidden(p, Cx - 4.0, -1.1, Cz - 3.2, Cx + 4.0, 0.0, Cz + 3.2);
        Hidden(p, 0.75, -0.1, 4.0, 1.0, 0.0, 5.0); // (what the "up" socket is on)
        Hidden(p, Cx - 3.0, FrameBottom, Cz - 2.6, Cx + 3.0, -1.1, Cz + 2.6);
        Hidden(p, Cx - 1.2, FrameBottom - 0.35, Cz - 1.2, Cx + 1.2, FrameBottom, Cz + 1.2);
        Sock(p, "up", 0.875, 0.0, Cz, "+y");
        Sock(p, "drive", Cx, FrameBottom - 0.35, Cz, "-y");
        foreach (var (sx, sz, n) in new[] { (-1, -1, "legFL"), (1, -1, "legFR"), (-1, 1, "legBL"), (1, 1, "legBR") })
            Sock(p, n, Cx + sx * 2.2, FrameBottom, Cz + sz * 1.8, "-y");
        // The manoeuvre jets, under the overhang on the four flat faces (the tankage's floor at the waist).
        foreach (var (dx, dz, n) in new[] { (-3.5, 0.0, "jetPort"), (3.5, 0.0, "jetStbd"), (0.0, -3.0, "jetBow"), (0.0, 3.0, "jetStern") })
            Sock(p, n, Cx + dx, -1.1, Cz + dz, "-y");
        p.Set(("fuelTank", Kw(("capacity", 9000))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx + 3.0, -1.6, Cz).Cast<object>()], "+x", "fuel"))));
        return p.Stats(9000, 24000, 30, 14.0);
    }

    /// <summary>A frame bar from a to b, w wide, drawn proud of the surface by 3 cm along its own outward normal (the plate's).</summary>
    private static void Bar(Part p, double[] a, double[] b, double w)
    {
        var d = Unit(Sub(b, a));
        // Outward: away from the spine in plan.
        double[] mid = [(a[0] + b[0]) / 2, 0, (a[2] + b[2]) / 2];
        double[] outw = Unit([mid[0] - (Cx - H), 0, mid[2] - (Cz - H)]);
        var side = Unit(Cross(d, outw));
        double[] Off(double[] q, double s, double o) => [q[0] + side[0] * s + outw[0] * o, q[1] + side[1] * s, q[2] + side[2] * s + outw[2] * o];
        Plate(p, [Off(a, -w / 2, 0.035), Off(a, w / 2, 0.035), Off(b, w / 2, 0.035), Off(b, -w / 2, 0.035)], Frame, "dirty steel");
    }

    /// <summary>The main drive: a big bell under the thrust ring, a glow deep inside. Origin on the ring; the bell down along -y.</summary>
    private static Part Drive()
    {
        var p = new Part("Halberd Main Drive", Small, Section, $"The Halberd's main drive: {DriveForce / 1e6:0.0} MN down its bell, from the thrust frame's tanks.", role: "thruster", tool: "wrench", material: "steel", color: "#2a2c2e", group: Group);
        double[] RingY(double r, double y, int i, int n = 12)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * Math.Cos(an), 4), y, Py.Round(r * Math.Sin(an), 4)];
        }
        // The throat (a short drum), then the bell flaring to 1.5 m, then a lip.
        (double Y, double R)[] st = [(0, 0.9), (-0.5, 0.75), (-0.8, 0.7), (-1.6, 1.05), (-2.3, 1.38), (-2.6, 1.5)];
        for (var s = 0; s < st.Length - 1; s++)
            for (var i = 0; i < 12; i++)
                Quad(p, RingY(st[s].R, st[s].Y, i), RingY(st[s].R, st[s].Y, i + 1), RingY(st[s + 1].R, st[s + 1].Y, i + 1), RingY(st[s + 1].R, st[s + 1].Y, i), s < 2 ? "#3a3d40" : "#2a2c2e", s < 2 ? "dirty steel" : "soot");
        // Cooling bands round the bell, and the glow up inside it.
        foreach (var (y, r) in new[] { (-1.2, 0.9), (-1.95, 1.22) })
            Prism(p, [.. Enumerable.Range(0, 12).Select(i => RingY(r + 0.04, y - 0.05, i))], [.. Enumerable.Range(0, 12).Select(i => RingY(r + 0.04, y + 0.05, i))], "#5a4a3a", "copper");
        Plate(p, [.. Enumerable.Range(0, 12).Select(i => RingY(0.62, -0.85, i))], "#9ad8ff", glow: true);
        p.Add(Box([-0.75, -0.8, -0.75], [0.75, 0, 0.75], hidden: true));
        p.Add(Box([-1.1, -2.6, -1.1], [1.1, -0.8, 1.1], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", DriveForce), ("spool", 1.5), ("exhaust", "-y"), ("at", Py.L(0, -2.6, 0)), ("burnRate", Py.Round(DriveForce / 1e6 * JetBurn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [1.1, -1.2, 0], "+x", "fuel"))));
        return p.Stats(6000, 9000, 25, 10.0);
    }

    /// <summary>A manoeuvre jet under the thrust frame's overhang: one of four that hold the ship upright and lean it (one engine on the
    /// spine can't, and the flight computer's gyro turns it only about the spine). Origin at its root, pushing up.</summary>
    private static Part ManoeuvreJet()
    {
        var p = new Part("Halberd Manoeuvre Jet", Small, Section, $"A manoeuvre jet: {JetForce / 1000:0} kN down a short bell, fed from the thrust frame. Four of them keep the Halberd upright and lean it.", role: "thruster", tool: "wrench", material: "steel", color: "#2a2c2e", group: Group);
        double[] RingY(double r, double y, int i, int n = 8)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * Math.Cos(an), 4), y, Py.Round(r * Math.Sin(an), 4)];
        }
        // A squat housing bolted under the overhang, a hazard band round it, then the bell.
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => RingY(0.42, -0.003, i))], [.. Enumerable.Range(0, 8).Select(i => RingY(0.38, -0.4, i))], Armour2, "plated steel");
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => RingY(0.43, -0.3, i))], [.. Enumerable.Range(0, 8).Select(i => RingY(0.43, -0.22, i))], Hazard, "hazard paint");
        (double Y, double R)[] st = [(-0.4, 0.26), (-0.55, 0.22), (-0.8, 0.3), (-0.95, 0.36)];
        for (var s = 0; s < st.Length - 1; s++)
            for (var i = 0; i < 8; i++)
                Quad(p, RingY(st[s].R, st[s].Y, i), RingY(st[s].R, st[s].Y, i + 1), RingY(st[s + 1].R, st[s + 1].Y, i + 1), RingY(st[s + 1].R, st[s + 1].Y, i), s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => RingY(0.2, -0.56, i))], "#9ad8ff", glow: true);
        p.Add(Box([-0.4, -0.4, -0.4], [0.4, 0, 0.4], hidden: true));
        p.Add(Box([-0.34, -0.95, -0.34], [0.34, -0.4, 0.34], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", JetForce), ("spool", 0.4), ("exhaust", "-y"), ("at", Py.L(0, -0.95, 0)), ("burnRate", Py.Round(JetForce / 1e6 * JetBurn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0.4, -0.2, 0], "+x", "fuel"))));
        return p.Stats(600, 2500, 15, 6.0);
    }

    /// <summary>A landing leg: a telescopic strut with a hydraulic ram beside it, on a broad pad. Origin at its root.</summary>
    private static Part Leg()
    {
        var p = new Part("Halberd Landing Leg", Small, Section, "A landing leg: a telescopic strut and its ram on a broad pad.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        double bottom = Ground - FrameBottom; // (negative: the pad's underside)
        p.Add(Box([-0.22, -1.6, -0.22], [0.22, 0, 0.22], Armour, shape: Kw(("finish", "plated steel"))));
        p.Add(Box([-0.15, bottom + 0.2, -0.15], [0.15, -1.6, 0.15], "#9aa0a4", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.5, bottom, -0.5], [0.5, bottom + 0.2, 0.5], Frame, shape: Kw(("finish", "rubber"))));
        p.Add(Box([-0.3, bottom + 0.2, -0.3], [0.3, bottom + 0.3, 0.3], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([0.24, -1.4, -0.06], [0.34, -0.2, 0.06], "#4a4e52", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([0.26, bottom + 0.35, -0.04], [0.32, -1.4, 0.04], "#9aa0a4", detail: true, shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.235, -0.5, -0.235], [0.235, -0.38, 0.235], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("top", [0, 0, 0], "+y");
        return p.Stats(900, 3000, 20, 6.0);
    }

    // ---- ladders

    /// <summary>A ladder climbed with F: rails and rungs standing off a back plane (local z 0, the landing behind it), rise metres up
    /// to its landing, the rails a metre past it.</summary>
    private static Part Ladder(string name, double rise, string desc, string sockName, double[] sockAt, string sockDir)
    {
        var p = new Part(name, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        double top = rise + 1.0;
        p.Add(Box([-0.3, 0, -0.19], [0.3, top, -0.005], hidden: true));
        foreach (var x in new[] { -0.25, 0.25 })
        {
            p.Add(Box([x - 0.03, 0.0, -0.18], [x + 0.03, top, -0.12], "#9aa0a4", detail: true, shape: Kw(("finish", "painted steel"))));
            for (var y = 0.6; y < top - 0.2; y += 1.2)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.01], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        for (var y = 0.3; y < rise + 0.1; y += 0.3)
            p.Add(Box([-0.22, y - 0.02, -0.165], [0.22, y + 0.02, -0.135], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.29, top - 0.06, -0.19], [0.29, top, -0.11], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock(sockName, sockAt, sockDir);
        p.Set(("ladder", rise));
        return p.Stats(60 + rise * 12, 400, 6, 3.0);
    }

    // ---- the guns

    /// <summary>A point-defence cannon: a squat rotary breech with a shroud and three barrels, belt-fed from the box bolted to its back.</summary>
    private static Part Pdc()
    {
        var p = new Part("Halberd PDC", Small, Section, "A point-defence cannon: a fast rotary gun for a turret, fed from the ammo box on its back.", role: "gun", tool: "wrench", material: "steel", color: Armour, group: Group);
        p.Add(Box([-0.2, -0.16, -0.3], [0.2, 0.16, 0.3]));
        p.Add(Box([-0.16, -0.12, -0.75], [0.16, 0.12, -0.3], Frame, shape: Kw(("finish", "dirty steel"))));
        foreach (var (x, y) in new[] { (-0.06, 0.04), (0.06, 0.04), (0.0, -0.06) })
            p.Add(Box([x - 0.025, y - 0.025, -1.35], [x + 0.025, y + 0.025, -0.75], "#2a2c2e", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.1, -0.1, -1.4], [0.1, 0.1, -1.3], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.205, 0.09, -0.2], [0.205, 0.13, 0.2], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([0.2, -0.05, -0.1], [0.24, 0.05, 0.18], "#4a4e52", detail: true, shape: Kw(("finish", "steel"))));
        p.Sock("bottom", [0, -0.16, 0], "-y");
        p.Sock("top", [0, 0.16, 0], "+y");
        p.Sock("back", [0, 0, 0.3], "+z");
        p.Set(("gun", Kw(("damage", 16), ("rpm", 1500), ("spread", 1.6), ("range", 220), ("mag", 300), ("reload", 2.5), ("kick", 6), ("fx", "bullet"), ("soundOf", "Bastille"), ("muzzle", Py.L(0, 0, -1.4)))), ("centreSockets", false));
        return p.Stats(180, 500, 14, 3.0);
    }

    /// <summary>The spinal railgun: a long breech in the cockpit's roof and a barrel up through the nose. Fires along its barrel.</summary>
    private static Part Railgun()
    {
        var p = new Part("Halberd Spinal Railgun", Small, Section, "The Halberd's railgun: one slug, very fast, through three plates. Fixed along the spine, so the ship is the aim.", role: "gun", tool: "welder", material: "steel", color: Frame, group: Group);
        p.Add(Box([-0.4, -0.4, -0.8], [0.4, 0.4, 0.8], Armour, shape: Kw(("finish", "plated steel"))));
        p.Add(Box([-0.22, -0.22, -4.8], [0.22, 0.22, -0.8], "#3a3d40", shape: Kw(("finish", "dirty steel"))));
        foreach (var s in new[] { -1, 1 })
            p.Add(Box([s * 0.22 - (s > 0 ? 0 : 0.06), -0.12, -4.75], [s * 0.22 + (s > 0 ? 0.06 : 0), 0.12, -1.0], "#5a4a3a", detail: true, shape: Kw(("finish", "copper"))));
        p.Add(Box([-0.28, -0.28, -4.95], [0.28, 0.28, -4.8], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.18, -0.18, -4.97], [0.18, 0.18, -4.95], "#9ad8ff", detail: true, shape: Kw(("glow", true))));
        foreach (var z in new[] { -0.6, 0.2 })
            p.Add(Box([-0.405, -0.405, z], [0.405, 0.405, z + 0.12], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("back", [0, 0, 0.8], "+z");
        p.Sock("ammo", [0.4, 0, -0.3], "+x"); // (its slugs: an Ammo Box bolted to the breech's side)
        p.Set(("gun", Kw(("damage", 700), ("rpm", 8), ("spread", 0.05), ("range", 600), ("mag", 1), ("reload", 6.0), ("flags", Py.L("penetrating")), ("kick", 2500), ("fx", "cannon"), ("speed", 900), ("soundOf", "Leviathan .50"), ("muzzle", Py.L(0, 0, -4.97)))), ("centreSockets", false));
        return p.Stats(4200, 3000, 25, 8.0);
    }

    // ---- the fittings

    private static Part Fitting(string name, string desc, string role = "frame", string color = "#8e959a") =>
        new(name, Small, "Ship fittings", desc, role: role, tool: "wrench", material: "steel", color: color, group: Group);

    private static void WallSockets(Part p, double back, double top)
    {
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, top, 0], "+y");
        p.Sock("wall", [0, 0.125, -back], "-z");
    }

    /// <summary>The reactor: a squat armoured drum on a plinth, coolant loops round it, a window onto the glow. Makes power.</summary>
    private static Part Reactor()
    {
        var p = Fitting("Halberd Reactor", "The Halberd's fusion reactor: 1.5 MW for the ship's circuits, no fuel. Don't look in the window.", "generator", Armour);
        p.Set(("tool", "welder"));
        p.Add(Box([-0.9, 0, -0.9], [0.9, 0.3, 0.9], Frame, shape: Kw(("finish", "dirty steel"))));
        double[] O(double y, double r, int i)
        {
            var a = Math.PI / 8 + i * Math.PI / 4;
            return [Py.Round(r * Math.Cos(a), 4), y, Py.Round(r * Math.Sin(a), 4)];
        }
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(0.3, 0.78, i))], [.. Enumerable.Range(0, 8).Select(i => O(2.2, 0.78, i))], Armour, "plated steel");
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(2.2, 0.78, i))], [.. Enumerable.Range(0, 8).Select(i => O(2.5, 0.45, i))], Armour2, "plated steel");
        foreach (var y in new[] { 0.7, 1.9 })
            Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(y, 0.82, i))], [.. Enumerable.Range(0, 8).Select(i => O(y + 0.1, 0.82, i))], Hazard, "hazard paint");
        p.Add(Box([-0.55, 0.3, -0.55], [0.55, 2.5, 0.55], hidden: true));
        p.Add(Box([-0.25, 1.0, 0.74], [0.25, 1.6, 0.8], "#a0f0ff", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.32, 0.93, 0.73], [0.32, 1.67, 0.76], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        // Coolant loops: a blue and a red pipe up its flank and into the ceiling.
        p.Add(Box([0.8, 0.3, -0.25], [0.92, 2.75, -0.13], PipeBlue, detail: true, shape: Kw(("finish", "painted steel"))));
        p.Add(Box([0.8, 0.3, 0.13], [0.92, 2.75, 0.25], PipeRed, detail: true, shape: Kw(("finish", "painted steel"))));
        p.Add(Box([-0.6, 0.3, -0.88], [-0.2, 0.62, -0.84], "#40ff90", detail: true, shape: Kw(("glow", true))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", 1500), ("spool", 10), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.15, 0.9)), ("face", "+z")))));
        return p.Stats(4000, 3000, 30, 12.0);
    }

    /// <summary>A robot's charging cradle: a back frame with clamps and a status light. The shell hangs on its front by its back.</summary>
    private static Part Cradle()
    {
        var p = Fitting("Robot Charging Cradle", "A wall cradle for a robot shell: clamps, a charge lead and a status light. Hang the shell on its front.", color: Frame);
        p.Add(Box([-0.4, 0, -0.12], [0.4, 2.1, 0.0], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.45, 0, -0.12], [-0.4, 2.2, 0.06], Hazard, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([0.4, 0, -0.12], [0.45, 2.2, 0.06], Hazard, shape: Kw(("finish", "hazard paint"))));
        foreach (var y in new[] { 0.9, 1.5 })
            p.Add(Box([-0.3, y, 0.0], [0.3, y + 0.08, 0.06], "#5a6064", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.08, 1.95, 0.0], [0.08, 2.02, 0.03], "#40e070", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([0.25, 0.3, 0.0], [0.31, 1.6, 0.05], PipeYellow, detail: true, shape: Kw(("finish", "rubber"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [0, 0.125, -0.12], "-z");
        p.Sock("front", [0, 1.25, 0.0], "+z");
        return p.Stats(60, 300, 6, 3.0);
    }

    /// <summary>A crash couch: a padded acceleration couch with a headrest and harness. Lie in it; it is where you wake (as a bunk).</summary>
    private static Part CrashCouch()
    {
        var p = Fitting("Crash Couch", "An acceleration couch: padded, harnessed, gel-filled. Lie in it, and in survival it is where you wake.", "seat", "#3a4048");
        p.Add(Box([-0.4, 0, -1.0], [0.4, 0.35, 1.0], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.36, 0.35, -0.95], [0.36, 0.5, 0.95], "#3d4a58", shape: Kw(("finish", "fabric"))));
        p.Add(Box([-0.36, 0.5, -0.95], [0.36, 0.85, -0.7], "#3d4a58", shape: Kw(("finish", "fabric"))));
        foreach (var s in new[] { -1, 1 })
        {
            p.Add(Box([s * 0.4 - 0.04, 0.35, -0.6], [s * 0.4 + 0.04, 0.62, 0.5], Frame, detail: true, shape: Kw(("finish", "painted steel"))));
            p.Add(Box([s * 0.22 - 0.03, 0.501, -0.6], [s * 0.22 + 0.03, 0.515, 0.2], Hazard, detail: true, shape: Kw(("finish", "fabric"))));
        }
        p.Add(Box([-0.12, 0.62, -0.2], [0.12, 0.66, 0.0], "#40c0e0", detail: true, shape: Kw(("glow", true))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [-0.4, 0.125, 0], "-x"); // (its long side against a wall)
        p.Set(("seat", Kw(("at", Py.L(0, 0.15, 0)), ("bunk", true))));
        return p.Stats(90, 220, 4, 2.5);
    }

    /// <summary>A flight couch: a raked seat with a headrest, side sticks and a harness, for the cockpit and the gunnery stations.</summary>
    private static Part FlightCouch()
    {
        var p = Fitting("Flight Couch", "A raked flight couch with side sticks: link it to a flight computer or a turret module.", "seat", "#3a4048");
        p.Add(Box([-0.32, 0, -0.32], [0.32, 0.35, 0.32], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.3, 0.35, -0.3], [0.3, 0.5, 0.3], "#3d4a58", shape: Kw(("finish", "fabric"))));
        p.Add(Box([-0.3, 0.5, 0.2], [0.3, 1.35, 0.4], "#3d4a58", shape: Kw(("finish", "fabric"))));
        p.Add(Box([-0.18, 1.35, 0.24], [0.18, 1.55, 0.4], "#3d4a58", shape: Kw(("finish", "fabric"))));
        foreach (var s in new[] { -1, 1 })
        {
            p.Add(Box([s * 0.36 - 0.05, 0.35, -0.25], [s * 0.36 + 0.05, 0.65, 0.3], Frame, detail: true, shape: Kw(("finish", "painted steel"))));
            p.Add(Box([s * 0.36 - 0.025, 0.65, -0.15], [s * 0.36 + 0.025, 0.85, -0.1], "#2a2c2e", detail: true, shape: Kw(("finish", "rubber"))));
            p.Add(Box([s * 0.15 - 0.03, 0.6, 0.19], [s * 0.15 + 0.03, 1.3, 0.2], Hazard, detail: true, shape: Kw(("finish", "fabric"))));
        }
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("seat", Kw(("at", Py.L(0, -0.05, 0)))));
        return p.Stats(45, 160, 4, 2.0);
    }

    /// <summary>The galley's coffee machine on its counter: the most important part of the ship.</summary>
    private static Part Coffee()
    {
        var p = Fitting("Galley Coffee Station", "A counter with a coffee machine on it. It makes one kind of coffee, and makes it badly, and the crew would mutiny without it.", "frame", "#8e959a");
        p.Add(Box([-0.35, 0, -0.3], [0.35, 0.9, 0.3], "#5c646a", shape: Kw(("finish", "painted steel"))));
        p.Add(Box([-0.37, 0.9, -0.32], [0.37, 0.94, 0.32], "#2f3539", detail: true, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.22, 0.94, -0.3], [0.22, 1.45, -0.02], "#c8ccc8", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.14, 1.05, -0.02], [0.14, 1.12, 0.06], "#2a2c2e", detail: true, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.05, 0.94, 0.0], [0.05, 1.03, 0.1], "#f0f0e8", detail: true, shape: Kw(("finish", "gloss paint"))));
        p.Add(Box([0.08, 1.3, -0.019], [0.16, 1.36, -0.005], "#ff4020", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([0.22, 0.94, -0.2], [0.33, 1.1, -0.1], "#6a4a30", detail: true, shape: Kw(("finish", "plastic"))));
        WallSockets(p, 0.3, 1.45);
        return p.Stats(70, 160, 3, 2.0);
    }

    /// <summary>A gunnery station: a console with two screens and a stick housing, in front of the gunner's couch.</summary>
    private static Part GunneryConsole()
    {
        var p = Fitting("Gunnery Console", "A gunner's console: target screens and a stick housing. The turret module sits under it.", color: Console);
        p.Add(Box([-0.45, 0, -0.25], [0.45, 0.8, 0.25], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.45, 0.8, -0.25], [0.45, 1.35, -0.1], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.4, 0.85, -0.1], [0.4, 1.3, -0.085], Screen, detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.4, 0.8, -0.08], [0.4, 0.82, 0.2], "#e0a040", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.46, 0.0, -0.26], [0.46, 0.12, 0.26], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("module", [0.45, 0.4, 0], "+x");
        return p.Stats(60, 200, 4, 2.0);
    }

    /// <summary>The flight computer: a cabinet with the stabiliser's gyro in it, under its own status lights. Flies the ship.</summary>
    private static Part FlightComputer()
    {
        var p = new Part("Halberd Flight Computer", Small, Section, "The Halberd's flight computer: a flight stabilizer with a ship-size gyro in a cabinet. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: Console, group: Group);
        p.Add(Box([-0.4, 0, -0.3], [0.4, 1.4, 0.3], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.35, 1.0, -0.318], [0.35, 1.3, -0.3], Screen, detail: true, shape: Kw(("glow", true))));
        for (var i = 0; i < 5; i++)
            p.Add(Box([-0.3 + i * 0.14, 0.85, -0.318], [-0.25 + i * 0.14, 0.9, -0.3 + 0.002], i % 2 == 0 ? "#40e070" : Amber, detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.05, 1.401, -0.25], [0.05, 1.415, 0.05], Hazard, detail: true, shape: Kw(("finish", "hazard paint")))); // (the arrow, forward)
        p.Add(Box([-0.12, 1.401, -0.25], [0.12, 1.415, -0.17], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 3000000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(300, 600, 8, 3.0);
    }

    // ---- the storeys broken into pieces (Karl, 2026-11-15: "If I attack one deck of it, then the whole deck disappears"; he chose
    // every wall and floor as a part of its own). Each storey is drawn whole above, then its boxes are dealt out to the pieces they
    // belong to: the floor (and what stands loose in the room), the four wall runs, the four corner frames (the chamfer, the frame
    // rib and the corner armour) and each partition. Every socket goes with the piece whose face it is on, and wherever two pieces'
    // solid boxes touch they get a facing socket pair: one pair places a piece, the game welds the rest (ConstructBuild.AutoLink),
    // so a piece knocked off leaves the rest welded to each other.

    private static readonly Dictionary<Part, List<(double X0, double Z0, double X1, double Z1)>> Partitions = [];
    private static readonly string[] DeckNames = ["Engineering", "Robot Bay", "Med", "Crew", "Ops", "Cockpit"];
    private static readonly Dictionary<(string Part, string Sock), string> SockOwner = []; // (a storey's socket: the piece that holds it now)

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    /// <summary>Storey k (its whole part) dealt out into pieces; the part itself keeps the floor.</summary>
    private static List<Part> Split(Part core, int k)
    {
        var deck = DeckNames[k];
        double hx = Hx(k), hz = Hz(k), ci = Ci(k), x0 = Cx - hx, x1 = Cx + hx, z0 = Cz - hz, z1 = Cz + hz;
        var walls = new Part[4];
        var corners = new Part[4];
        var parts = Partitions.TryGetValue(core, out var pr) ? pr : [];
        var partitions = new Part[parts.Count];
        Part Piece(string name, string desc) => new(name, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: Wall, group: Group);
        for (var side = 0; side < 4; side++)
            walls[side] = Piece($"Halberd {deck} Wall {FaceNames[side]}", $"A wall run of the Halberd's {deck.ToLowerInvariant()} storey, the {FaceNames[side].ToLowerInvariant()} side, inside its hull panel.");
        (int Sx, int Sz)[] cs = [(-1, -1), (1, -1), (1, 1), (-1, 1)];
        for (var c = 0; c < 4; c++)
            corners[c] = Piece($"Halberd {deck} Corner {c + 1}", $"A corner frame of the Halberd's {deck.ToLowerInvariant()} storey: the chamfer, the frame rib and the corner's armour. Tough.");
        for (var i = 0; i < parts.Count; i++)
            partitions[i] = Piece($"Halberd {deck} Partition {i + 1}", $"A partition of the Halberd's {deck.ToLowerInvariant()} storey, with its door frames.");
        var floor = new Part(core.D["name"] as string ?? "", Small, Section, $"The Halberd's {deck.ToLowerInvariant()} storey's deck: its floor, and what stands loose on it.", role: "frame", tool: "welder", material: "steel", color: Deck, group: Group);
        Part Of(PyDict b)
        {
            double[] mn = Vd(b, "min"), mx = Vd(b, "max");
            double xc = (mn[0] + mx[0]) / 2, yc = (mn[1] + mx[1]) / 2, zc = (mn[2] + mx[2]) / 2;
            if (yc > Ft(k) - 0.01)
                for (var i = 0; i < parts.Count; i++)
                {
                    var (a, bz, c, d) = parts[i];
                    var alongX = Math.Abs(d - bz) < 1e-6;
                    double lo = alongX ? Math.Min(a, c) : Math.Min(bz, d), hi = alongX ? Math.Max(a, c) : Math.Max(bz, d), line = alongX ? bz : a;
                    double u = alongX ? xc : zc, v = alongX ? zc : xc;
                    if (u > lo - 0.07 && u < hi + 0.07 && Math.Abs(v - line) < 0.145)
                        return partitions[i];
                }
            if (mn[1] > Fl(k) - 0.001 && mx[1] < Ft(k) + 0.001)
                return floor; // (the floor's slab and its corner steps; a threshold; the boarding platform)
            for (var c = 0; c < 4; c++)
            {
                var (sx, sz) = cs[c];
                double ox = sx * (xc - (sx < 0 ? x0 : x1)), oz = sz * (zc - (sz < 0 ? z0 : z1));
                if (ox > -ci - 0.05 && oz > -ci - 0.05 && ox + oz > -ci - 0.3)
                    return corners[c];
            }
            double[] outw = [z0 - zc, xc - x1, zc - z1, x0 - xc];
            var best = 0;
            for (var side = 1; side < 4; side++)
                if (outw[side] > outw[best])
                    best = side;
            if (outw[best] > -0.06 || yc > Fl(k + 1) - 0.001)
                return walls[best]; // (a wall run, what is on its face, what stands outside it: a PDC housing, the shoulder)
            return floor;
        }
        foreach (var b in core.Boxes)
            Of(b).Add(b);
        var all = new List<Part> { floor };
        all.AddRange(walls);
        all.AddRange(corners);
        all.AddRange(partitions);
        all.RemoveAll(q => q.Boxes.Count == 0);
        // The sockets: on the piece whose box face it stands on, facing out of it.
        foreach (var sk in core.Sockets)
        {
            var at = ((List<object?>)sk["at"]!).Select(v => Convert.ToDouble(v) + H).ToArray();
            var dir = (string)sk["dir"]!;
            var ax = dir[1] - 'x';
            var plus = dir[0] == '+';
            Part? owner = null, drawnOwner = null;
            foreach (var q in all)
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
            owner ??= drawnOwner;
            if (owner == null)
                System.Console.WriteLine($"Halberd {deck}: socket {sk["name"]} is on no piece's face; the floor takes it");
            owner ??= floor;
            owner.Sockets.Add(sk);
            SockOwner[((string)core.D["name"]!, (string)sk["name"]!)] = (string)owner.D["name"]!;
        }
        // The stats: the frame is tough (the panels and walls go first), a wall less, a partition least; the floor keeps the storey's
        // furniture and so most of its weight.
        var scale = k == 5 ? 0.6 : 1.0;
        floor.Stats(4200 * scale, 9000 * scale, 35, 10.0);
        foreach (var w in walls)
            w.Stats(1100 * scale, 4000 * scale, 30, 6.0);
        foreach (var c in corners)
            c.Stats(1600 * scale, 30000 * scale, 40, 8.0);
        foreach (var q in partitions)
            q.Stats(350, 1500, 20, 4.0);
        return all;
    }

    /// <summary>A facing socket pair wherever two of the parts' solid boxes touch face to face (one pair a pair of parts, on their
    /// biggest contact), unless they already meet at one: what welds the pieces to each other.</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var bounds = solid.Select(l => (Mn: Enumerable.Range(0, 3).Select(a => l.Count == 0 ? 0 : l.Min(b => b.Mn[a])).ToArray(), Mx: Enumerable.Range(0, 3).Select(a => l.Count == 0 ? 0 : l.Max(b => b.Mx[a])).ToArray())).ToList();
        var count = parts.Select(q => q.Sockets.Count(s => ((string)s["name"]!).StartsWith('j'))).ToArray();
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var (bi, bj) = (bounds[i], bounds[j]);
                if (Enumerable.Range(0, 3).Any(a => bi.Mn[a] > bj.Mx[a] + 0.002 || bj.Mn[a] > bi.Mx[a] + 0.002))
                    continue;
                if (Meet(parts[i], parts[j]))
                    continue;
                (double Area, double[] At, int Axis, bool IPlus) best = (0, [], 0, false);
                foreach (var A in solid[i])
                    foreach (var B in solid[j])
                        for (var ax = 0; ax < 3; ax++)
                        {
                            bool iPlus = Math.Abs(A.Mx[ax] - B.Mn[ax]) < 0.001, iMinus = Math.Abs(A.Mn[ax] - B.Mx[ax]) < 0.001;
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
                                double lo = Math.Max(A.Mn[a], B.Mn[a]), hi = Math.Min(A.Mx[a], B.Mx[a]);
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
            throw new InvalidOperationException("Halberd: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    // ---- the blueprint

    private static List<string> Structure = [];
    private static Dictionary<string, int> PieceSteps = [];

    /// <summary>The step that holds socket sock of step on: a storey's piece that took it when the storey was split, else on itself.</summary>
    private static int Holder(BuildingParts.Blueprint bp, int on, string sock) =>
        bp.Steps[on]["part"] is string part && SockOwner.TryGetValue((part, sock), out var piece) ? PieceSteps[piece] : on;

    private static int Hang(BuildingParts.Blueprint bp, string part, int on, string sock, string by, int spin = 0) => bp.Hang(part, Holder(bp, on, sock), sock, by, spin);

    private static int HangTurned(BuildingParts.Blueprint bp, string part, int on, string sock, string by, int rot) => bp.HangTurned(part, Holder(bp, on, sock), sock, by, rot);

    private static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static int LampAt(BuildingParts.Blueprint bp, int on, string sock, string colour = "#e8f0f4")
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

    // The couch hinges: their turn on the floor socket (the axis across the ship) and the couch's on the leaf (facing the screens),
    // and how far a full burn lays a couch back (lift 1) or tips it forward (lift -1), in degrees.
    private const int CouchHingeSpin = 3, CouchSpin = 0; // (the hinge's axis +x: a plus angle lays the couch back)
    private const int CouchBack = 35, CouchForward = -15;

    private static int CouchHinge(BuildingParts.Blueprint bp, int cockpit, string sock)
    {
        var hinge = Hang(bp, "Motor Hinge", cockpit, sock, "base", CouchHingeSpin);
        bp.Steps[hinge]["config"] = new PyDict { ["min"] = CouchForward, ["max"] = CouchBack, ["speed"] = 20 };
        return hinge;
    }

    /// <summary>A PDC turret in its housing: a piston on the housing's back plate that pushes it out, a turntable on the piston,
    /// a motor hinge on its top for elevation, the gun on the hinge's leaf and its box behind; the hatch on the mouth.</summary>
    private static (int Gun, int Piston, int Hatch) Turret(BuildingParts.Blueprint bp, int on, string sock, int spin)
    {
        var piston = Hang(bp, "Piston 2x2 Medium", on, sock, "base");
        var tt = Hang(bp, "Turntable", piston, "top", "base");
        var hinge = Hang(bp, "Motor Hinge", tt, "top", "base");
        var gun = Hang(bp, "Halberd PDC", hinge, "leaf", "bottom", spin);
        Hang(bp, "Ammo Box", gun, "back", "front");
        var hatch = Paint(bp, Hang(bp, "Halberd PDC Hatch", on, sock + "Hatch", "hang"), Armour, "plated steel");
        return (gun, piston, hatch);
    }

    private static BuildingParts.Blueprint Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint("Halberd Frigate", "A combat frigate that stands on its tail: six decks up a ladder spine, four point-defence turrets and a spinal railgun.", defs, maxSteps: 320);
        bp.Conduit = true; // (a ship isn't anchored, but its lamp wires still run up the walls and over the ceilings, not across the rooms)
        double[] zero = [0, 0, 0];
        var steps = new Dictionary<string, int>();
        foreach (var name in Structure)
            steps[name] = steps.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = steps;
        int eng = steps["Halberd Engineering Deck"], frame = steps["Halberd Thrust Frame"], robot = steps["Halberd Robot Bay Deck"], med = steps["Halberd Med Deck"],
            crew = steps["Halberd Crew Deck"], ops = steps["Halberd Ops Deck"], cockpit = steps["Halberd Cockpit Deck"], nose = steps["Halberd Nose"];
        var drive = Hang(bp, "Halberd Main Drive", frame, "drive", "top");
        foreach (var n in new[] { "legFL", "legFR", "legBL", "legBR" })
            Hang(bp, "Halberd Landing Leg", frame, n, "top");
        foreach (var n in new[] { "jetPort", "jetStbd", "jetBow", "jetStern" })
            Paint(bp, Hang(bp, "Halberd Manoeuvre Jet", frame, n, "top"), Armour2, "plated steel");
        // The ladder spine and the boarding ladder.
        var decks = new[] { eng, robot, med, crew, ops };
        foreach (var d in decks)
            Hang(bp, "Halberd Spine Ladder", d, "ladder", "bottom");
        Hang(bp, "Halberd Boarding Ladder", eng, "boarding", "hull");
        // Engineering: the reactor, the boards, the batteries, the workshop and stores, the airlock's doors and suit lockers.
        Hang(bp, "Halberd Reactor", eng, "reactor", "bottom");
        Hang(bp, "Power Board", eng, "board", "back");
        Hang(bp, "Engineer's Panel", eng, "panel", "back");
        Hang(bp, "Battery", eng, "battery0", "-y");
        Hang(bp, "Battery", eng, "battery1", "-y");
        Hang(bp, "Pipe Bank", eng, "pipes", "back");
        Hang(bp, "Tool Rack", eng, "rack", "back");
        Hang(bp, "Requisition Crate", eng, "crate0", "-y");
        Hang(bp, "Requisition Crate", eng, "crate1", "-y");
        Paint(bp, Hang(bp, "Locker", eng, "suit0", "wall"), Hazard, "worn paint");
        Paint(bp, Hang(bp, "Locker", eng, "suit1", "wall"), Hazard, "worn paint");
        Paint(bp, Hang(bp, "Bulkhead Door 1.5x2.25 m", eng, "doorOuter", "hang"), Armour, "plated steel");
        Paint(bp, Hang(bp, "Bulkhead Door 1.5x2.25 m", eng, "doorInner", "hang"), Armour, "plated steel");
        Switch(bp, eng, "switch", LampAt(bp, eng, "lampA"), LampAt(bp, eng, "lampB"), LampAt(bp, eng, "lampC"), LampAt(bp, eng, "lampLock", "#ff5040"));
        // The robot bay: three cradles with two shells in them, two link couches, the armoury.
        var cradles = Enumerable.Range(0, 3).Select(i => Hang(bp, "Robot Charging Cradle", robot, $"cradle{i}", "wall")).ToArray();
        Hang(bp, "Robot Shell", cradles[0], "front", "back");
        Hang(bp, "Robot Shell", cradles[2], "front", "back");
        HangTurned(bp, "Shell Control Chair", robot, "couch0", "bottom", BuildingParts.RY90); // (facing the cradles)
        HangTurned(bp, "Shell Control Chair", robot, "couch1", "bottom", BuildingParts.RY90);
        Hang(bp, "Tool Rack", robot, "armsRack", "back");
        Paint(bp, Hang(bp, "Locker", robot, "locker0", "wall"), Armour, "worn paint");
        Paint(bp, Hang(bp, "Locker", robot, "locker1", "wall"), Armour, "worn paint");
        Hang(bp, "Ammo Crate", robot, "ammo0", "bottom");
        Hang(bp, "Ammo Crate", robot, "ammo1", "bottom");
        Paint(bp, Hang(bp, "Bulkhead Door 1.5x2.25 m", robot, "doorArmoury", "hang"), Armour, "plated steel");
        Switch(bp, robot, "switch", LampAt(bp, robot, "lampA", "#f0c070"), LampAt(bp, robot, "lampB", "#f0c070"), LampAt(bp, robot, "lampC"), LampAt(bp, robot, "lampD"));
        // The med bay and the galley.
        Hang(bp, "Medical Station", med, "medL", "back");
        Hang(bp, "Medical Station", med, "medR", "back");
        Hang(bp, "Medicine Cabinet", med, "cabinet", "back");
        Paint(bp, Hang(bp, "Shower Stall", med, "decon", "wall"), MedWhite, "gloss paint"); // (the decontamination shower)
        Paint(bp, Hang(bp, "Locker", med, "medLocker", "wall"), MedWhite, "gloss paint");
        Paint(bp, Hang(bp, "Bulkhead Door 1.5x2.25 m", med, "doorMed", "hang"), MedWhite, "gloss paint");
        Hang(bp, "Galley Stove", med, "stove", "back");
        Hang(bp, "Galley Coffee Station", med, "coffee", "wall");
        Paint(bp, Hang(bp, "Locker", med, "galleyLocker", "wall"), "#5c646a", "worn paint");
        HangTurned(bp, "Mess Table", med, "table", "bottom", BuildingParts.RY90);
        Switch(bp, med, "switchMed", LampAt(bp, med, "lampMedA", "#f4fbff"), LampAt(bp, med, "lampMedB", "#f4fbff"));
        Switch(bp, med, "switch", LampAt(bp, med, "lampGalleyA", "#ffe0b0"), LampAt(bp, med, "lampGalleyB", "#ffe0b0"));
        // Crew quarters.
        for (var i = 0; i < 4; i++)
            Hang(bp, "Crash Couch", crew, $"couch{i}", "wall");
        Paint(bp, Hang(bp, "Bulkhead Door 1.5x2.25 m", crew, "doorBunk", "hang"), Armour, "plated steel");
        Paint(bp, Hang(bp, "Steel Swing Door 1.5x2.25 m", crew, "doorHead", "hang"), Armour, "plated steel");
        Hang(bp, "Ship Toilet", crew, "toilet", "wall");
        Hang(bp, "Ship Basin", crew, "basin", "wall");
        Hang(bp, "Steel Desk", crew, "desk", "wall");
        for (var i = 0; i < 3; i++)
            Paint(bp, Hang(bp, "Locker", crew, $"locker{i}", "wall"), i == 1 ? "#5c646a" : Armour, "worn paint");
        Switch(bp, crew, "switch", LampAt(bp, crew, "lampBunkA", "#ffd8a0"), LampAt(bp, crew, "lampBunkB", "#ffd8a0"), LampAt(bp, crew, "lampHall"), LampAt(bp, crew, "lampHead"));
        // Two gunnery stations, each working the turrets on its own deck (a wire reaches 8 m): the robot bay's port and starboard, ops' bow and stern.
        var guns = new[] { Turret(bp, robot, "pdcPort", PdcSpinFlank), Turret(bp, robot, "pdcStbd", PdcSpinFlank), Turret(bp, ops, "pdcBow", PdcSpinEnds), Turret(bp, ops, "pdcStern", PdcSpinEnds) };
        foreach (var (deck, pair) in new[] { (robot, new[] { guns[0], guns[1] }), (ops, new[] { guns[2], guns[3] }) })
        {
            var turn = deck == ops ? BuildingParts.RY270 : BuildingParts.R0; // (ops' station faces starboard, between its two turrets)
            var console = HangTurned(bp, "Gunnery Console", deck, "gun", "bottom", turn);
            var seat = HangTurned(bp, "Flight Couch", deck, "gunSeat", "bottom", turn);
            var module = Hang(bp, "Turret Module", console, "module", "-x");
            bp.Wires.Add((seat, module));
            foreach (var g in pair)
                bp.Wires.Add((module, g.Gun));
            // The PDC data point (Karl, 2026-11-15): an OR gate on the module that pushes the pair out and opens their hatches while
            // any input is high. The station's switch is wired into it; anything else (a sensor, a timer, a radio) can be wired in too.
            // The hatches open with it but shut only once the turrets are back in: a second OR gate holds them open off a Delay
            // for the pistons' stroke (1 m at 0.4 m/s) and a little more.
            var data = Hang(bp, "OR Gate", module, "+x", "-x");
            var lever = Hang(bp, "Bakelite Switch", deck, "pdcSwitch", "back");
            bp.Wires.Add((lever, data));
            var hold = Hang(bp, "Delay", data, "+z", "-z");
            bp.Steps[hold]["config"] = new PyDict { ["seconds"] = HatchHold };
            var hatches = Hang(bp, "OR Gate", hold, "+z", "-z");
            bp.Wires.Add((data, hold));
            bp.Wires.Add((data, hatches));
            bp.Wires.Add((hold, hatches));
            foreach (var g in pair)
            {
                bp.Wires.Add((data, g.Piston));
                bp.Wires.Add((hatches, g.Hatch));
            }
        }
        Hang(bp, "Chart Table", ops, "plot", "bottom");
        Paint(bp, Hang(bp, "Locker", ops, "locker0", "wall"), Armour, "worn paint");
        Paint(bp, Hang(bp, "Locker", ops, "locker1", "wall"), Armour, "worn paint");
        Hang(bp, "Tool Rack", ops, "armsRack", "back");
        Switch(bp, ops, "switch", LampAt(bp, ops, "lampA", "#a0c8ff"), LampAt(bp, ops, "lampB", "#a0c8ff"), LampAt(bp, ops, "lampC"), LampAt(bp, ops, "lampD"));
        // The cockpit: the pilot flies through the flight computer and fires the railgun with it.
        // The flight couches turn as thrust is applied (Karl, 2026-11-15): each stands on a motor hinge across the ship, and the
        // pilot's Driving Module sends its lift (Sprint: the main drive, Crouch: down) into both, so they lie back into a burn.
        // The module's drive output stays unwired: the flight computer flies the ship.
        var pilotHinge = CouchHinge(bp, cockpit, "pilot");
        var pilot = Hang(bp, "Flight Couch", pilotHinge, "leaf", "bottom", CouchSpin);
        var copilotHinge = CouchHinge(bp, cockpit, "copilot");
        Hang(bp, "Flight Couch", copilotHinge, "leaf", "bottom", CouchSpin);
        var fc = Hang(bp, "Halberd Flight Computer", cockpit, "computer", "bottom");
        bp.Wires.Add((pilot, fc));
        var rail = Hang(bp, "Halberd Spinal Railgun", nose, "railgun", "back");
        Hang(bp, "Long Range Sensor", nose, "sensor", "-y"); // (the radar's scope in every seat; the lock waits for a missile rack: ConstructRadar.CanLock)
        Hang(bp, "Ammo Box", rail, "ammo", "left");
        var railModule = Hang(bp, "Turret Module", cockpit, "gunModule", "-z");
        bp.Wires.Add((pilot, railModule));
        bp.Wires.Add((railModule, rail));
        var couchModule = Hang(bp, "Driving Module", railModule, "+x", "-x");
        bp.Wires.Add((pilot, couchModule));
        bp.DataLinks.Add((couchModule, pilotHinge, "lift"));
        bp.DataLinks.Add((couchModule, copilotHinge, "lift"));
        Hang(bp, "Chart Table", cockpit, "chart", "back");
        Switch(bp, cockpit, "switch", LampAt(bp, cockpit, "lampA", "#ff6050"), LampAt(bp, cockpit, "lampB", "#ff6050"), LampAt(bp, cockpit, "lampC"));
        _ = drive;
        return bp;
    }

    public void Make(PartOutput o)
    {
        Panels.Clear();
        Partitions.Clear();
        SockOwner.Clear();
        Part[] storeys = [Engineering(), RobotBay(), MedDeck(), CrewDeck(), OpsDeck(), Cockpit()];
        var nose = Nose();
        var thrust = ThrustFrame();
        var pieces = new List<Part>();
        for (var k = 0; k < Storeys; k++)
            pieces.AddRange(Split(storeys[k], k));
        var structure = new List<Part>(pieces) { nose, thrust };
        structure.AddRange(Panels); // (made with their storeys above)
        Connect(structure);
        Structure = PlaceOrder(structure, "Halberd Engineering Deck");
        List<Part> parts =
        [
            .. structure, Drive(), ManoeuvreJet(), Leg(), PdcHatch(),
            Ladder("Halberd Spine Ladder", Pitch, "A ladder up the Halberd's spine, one storey: F climbs it.", "bottom", [0, 0, -0.095], "-y"),
            Ladder("Halberd Boarding Ladder", Ft(0) - Ground, "The Halberd's boarding ladder, from the ground to the airlock's platform: F climbs it.", "hull", [0, -Ground + 0.125, -0.005], "+z"),
            Pdc(), Railgun(), Reactor(), Cradle(), CrashCouch(), FlightCouch(), Coffee(), GunneryConsole(), FlightComputer(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        var bp = Blueprint(defs);
        o.File($"{BpOut}/HalberdFrigate.json", PyJson.Dumps(bp.Json(), 2) + "\n");
    }
}
