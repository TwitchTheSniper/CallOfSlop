using CallOfSlop.PartKit.Generators;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.ShipKit;

/// <summary>
/// The kit's rooms (floors, wall runs with their doorways and windows, hatch rails) and the ship in welded pieces (the Zero Day's way).
/// Each big part is drawn whole, then its boxes are dealt out: a deck part keeps its floor (and what stands loose on it) under its own
/// name; each wall run is a wall piece with what is fixed to it (OnWall); a ceiling is a piece; the hull outside the rooms (the skin,
/// its armour and the void behind it) is one piece a side per station band. The keel and the roof are cut at the stations. A socket
/// goes with the piece whose face it is on; wherever two pieces' solid boxes touch they get a facing socket pair (Connect): one pair
/// places a piece (PlaceOrder, out from the keel), the game welds the rest (ConstructBuild.AutoLink).
/// </summary>
public abstract partial class LoftedShip
{
    private readonly record struct WallLine(bool AlongX, double A0, double A1, double C, double Y0, double Y1);

    private readonly Dictionary<Part, List<WallLine>> walls = [];
    private readonly Dictionary<Part, List<(Rect R, double Y0, double Y1)>> rooms = [];
    private readonly Dictionary<Part, double> ceils = [];
    private readonly Dictionary<PyDict, int> stuck = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(string Part, string Sock), string> sockOwner = [];
    private Dictionary<string, int> pieceSteps = [];

    /// <summary>Forgets the pieces of the last run (a generator's Make starts with it).</summary>
    protected void Reset()
    {
        walls.Clear();
        rooms.Clear();
        ceils.Clear();
        stuck.Clear();
        sockOwner.Clear();
        pieceSteps = [];
        WallCol = Style.Wall;
        DadoCol = Style.WallLow;
    }

    // ---- rooms

    protected static List<Rect> Minus(List<Rect> rs, Rect h)
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

    /// <summary>A floor (or a ceiling slab) over the plan from y0 to y1, with holes cut in it.</summary>
    protected static void Floor(Part p, Rect[] plan, double y0, double y1, string color, string finish, params Rect[] holes)
    {
        var rs = plan.ToList();
        foreach (var h in holes)
            rs = Minus(rs, h);
        foreach (var r in rs)
            Solid(p, r.X0, y0, r.Z0, r.X1, y1, r.Z1, color, finish);
    }

    protected Gap Door(double a) => new(a, 1.5, 0, DoorH);

    /// <summary>A wall 0.25 m thick centred on c, along x (alongX) or z from a0 to a1, from the floor's top y0 to y1, with its gaps;
    /// a dado on both faces, dark jambs and a hazard head on each doorway, glass in a window.</summary>
    protected void WallRun(Part p, bool alongX, double a0, double a1, double c, double y0, double y1, params Gap[] gaps)
    {
        (walls.TryGetValue(p, out var ws) ? ws : walls[p] = []).Add(new WallLine(alongX, a0, a1, c, y0, y1)); // (each run is a piece of its own)
        void Run(double a, double b, double ya, double yb, string col, string fin, double th, bool detail)
        {
            if (b - a < 0.02 || yb - ya < 0.02)
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
        foreach (var g in gaps.OrderBy(g => g.A))
        {
            Run(at, g.A, y0, y1, WallCol, "painted plating", 0.125, false);
            Run(g.A, g.A + g.W, g.Head + y0, y1, WallCol, "painted plating", 0.125, false);
            if (g.Sill > 0)
            {
                Run(g.A, g.A + g.W, y0, y0 + g.Sill, WallCol, "painted plating", 0.125, false);
                if (g.Glass)
                {
                    Run(g.A, g.A + g.W, y0 + g.Sill, y0 + g.Head, Glass, "glass", 0.02, true);
                    if (alongX)
                        Hidden(p, g.A, y0 + g.Sill, c - 0.125, g.A + g.W, y0 + g.Head, c + 0.125);
                    else
                        Hidden(p, c - 0.125, y0 + g.Sill, g.A, c + 0.125, y0 + g.Head, g.A + g.W);
                }
                Run(g.A - 0.05, g.A + g.W + 0.05, y0 + g.Sill - 0.06, y0 + g.Sill, WallTrim, "painted steel", 0.14, true);
                Run(at + 0.05, g.A - 0.05, y0 + 0.003, y0 + 0.75, DadoCol, "painted plating", 0.137, true);
                // A window's frame: dark sides and head, no hazard.
                Run(g.A - 0.05, g.A, y0 + g.Sill, y0 + g.Head + 0.05, WallTrim, "painted steel", 0.14, true);
                Run(g.A + g.W, g.A + g.W + 0.05, y0 + g.Sill, y0 + g.Head + 0.05, WallTrim, "painted steel", 0.14, true);
                Run(g.A, g.A + g.W, y0 + g.Head, y0 + g.Head + 0.05, WallTrim, "painted steel", 0.14, true);
                at = g.A + g.W;
                continue;
            }
            Run(at + 0.05, g.A - 0.05, y0 + 0.003, y0 + 0.75, DadoCol, "painted plating", 0.137, true);
            // A doorway's frame: dark jambs, a hazard head.
            Run(g.A - 0.07, g.A, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A + g.W, g.A + g.W + 0.07, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A, g.A + g.W, y0 + g.Head, y0 + g.Head + 0.07, Hazard, "hazard paint", 0.145, true);
            at = g.A + g.W;
        }
        Run(at, a1, y0, y1, WallCol, "painted plating", 0.125, false);
        Run(at + 0.05, a1 - 0.05, y0 + 0.003, y0 + 0.75, DadoCol, "painted plating", 0.137, true);
    }

    /// <summary>The socket a door hangs by in a wall's doorway (from Gap a): on the wall's centre line at the doorway's start.</summary>
    protected static void DoorSock(Part p, string name, bool alongX, double a, double c, double floor) =>
        Sock(p, name, alongX ? a : c, floor + 1.125, alongX ? c : a, alongX ? "+x" : "+z");

    /// <summary>A safety rail round a hatch on three sides, open at its back edge (+z), where the ladder lands people.</summary>
    protected void HatchRail(Part p, Rect h, double y)
    {
        Solid(p, h.X0 - 0.06, y, h.Z0 - 0.06, h.X1 + 0.06, y + 1.0, h.Z0, Frame, "painted steel");
        Solid(p, h.X0 - 0.06, y, h.Z0, h.X0, y + 1.0, h.Z1 - 0.3, Frame, "painted steel");
        Solid(p, h.X1, y, h.Z0, h.X1 + 0.06, y + 1.0, h.Z1 - 0.3, Frame, "painted steel");
        Detail(p, h.X0 - 0.075, y + 0.96, h.Z0 - 0.075, h.X1 + 0.075, y + 1.04, h.Z0 + 0.015, Hazard, "hazard paint");
        Detail(p, h.X0 - 0.12, y, h.Z1, h.X1 + 0.12, y + 0.012, h.Z1 + 0.12, Hazard, "hazard paint");
    }

    /// <summary>A light strip under a ceiling.</summary>
    protected static void CeilStrip(Part p, double x0, double x1, double z0, double z1, double ceil, string color = "#d8f0ff") =>
        Detail(p, x0, ceil - 0.025, z0, x1, ceil, z1, color, glow: true);

    /// <summary>The volume a part's rooms take (with their walls): what SplitDeck calls inside; outside it is hull.</summary>
    protected void Room(Part p, Rect r, double y0, double y1) => (rooms.TryGetValue(p, out var rs) ? rs : rooms[p] = []).Add((r, y0, y1));

    /// <summary>The height of a part's ceiling: SplitDeck gives what is above it less 35 cm to the ceiling piece.</summary>
    protected void CeilingAt(Part p, double y) => ceils[p] = y;

    /// <summary>Draws with draw() and gives every box it adds to the part's last wall run (a rail, a pipe, a frame on its face).</summary>
    protected void OnWall(Part p, Action draw)
    {
        var n = p.Boxes.Count;
        draw();
        var w = walls[p].Count - 1;
        for (var i = n; i < p.Boxes.Count; i++)
            stuck[p.Boxes[i]] = w;
    }

    // ---- splitting

    /// <summary>A deck part dealt out into its floor (the part itself), its wall runs, its ceiling and its hull pieces.</summary>
    protected List<Part> SplitDeck(Part core, string shortName)
    {
        var ws = walls.TryGetValue(core, out var w0) ? w0 : [];
        var wallPieces = new Part?[ws.Count];
        var hull = new SortedDictionary<(int Side, int Band), Part>();
        Part? ceiling = null;
        var rs = rooms[core];
        double? ceilY = ceils.TryGetValue(core, out var cy) ? cy : null;
        var what = shortName.ToLowerInvariant();
        bool Inside(double[] c) => rs.Any(q => c[0] > q.R.X0 - 0.02 && c[0] < q.R.X1 + 0.02 && c[2] > q.R.Z0 - 0.02 && c[2] < q.R.Z1 + 0.02 && c[1] > q.Y0 - 0.02 && c[1] < q.Y1 + 0.02);
        int? WallOf(double[] c, double thick, bool alongX, bool any)
        {
            for (var i = 0; i < ws.Count; i++)
            {
                var w = ws[i];
                if (!any && w.AlongX != alongX)
                    continue;
                double u = w.AlongX ? c[0] : c[2], v = w.AlongX ? c[2] : c[0];
                if (Math.Abs(v - w.C) <= 0.15 && thick <= 0.32 && u > Math.Min(w.A0, w.A1) - 0.08 && u < Math.Max(w.A0, w.A1) + 0.08 && c[1] > w.Y0 - 0.01 && c[1] < w.Y1 + 0.1)
                    return i;
            }
            return null;
        }
        Part WallPiece(int i) => wallPieces[i] ??= Big($"{Prefix} {shortName} Wall {i + 1}", $"A wall of the {Prefix}'s {what}, with its door frames and what is fixed to it.", color: Wall);
        Part Of(PyDict b)
        {
            if (stuck.TryGetValue(b, out var wi))
                return WallPiece(wi);
            double[] mn = Vd(b, "min"), mx = Vd(b, "max"), c = Mid3(b);
            foreach (var alongX in new[] { true, false })
                if (WallOf(c, alongX ? mx[2] - mn[2] : mx[0] - mn[0], alongX, false) is int i)
                    return WallPiece(i);
            if (!Inside(c))
            {
                var key = (c[0] < Cx ? -1 : 1, Band(c[2]));
                if (!hull.TryGetValue(key, out var h))
                    hull[key] = h = Big($"{Prefix} {shortName} Hull {(key.Item1 < 0 ? "Port" : "Stbd")} {key.Item2 + 1}", $"A hull piece beside the {Prefix}'s {what}, the {(key.Item1 < 0 ? "port" : "starboard")} side: skin, armour and the void behind them. Shot off, it opens the side.");
                return h;
            }
            if (ceilY is double y && c[1] > y - 0.35)
                return ceiling ??= Big($"{Prefix} {shortName} Ceiling", $"The ceiling of the {Prefix}'s {what}.", color: Ceil);
            return core;
        }
        double mass = Convert.ToDouble(core.D["mass"]), health = Convert.ToDouble(core.D["health"]);
        var boxes = core.Boxes.ToList();
        core.Boxes.Clear();
        foreach (var b in boxes)
            Of(b).Add(b);
        var all = new List<Part> { core };
        all.AddRange(wallPieces.Where(q => q != null)!);
        if (ceiling != null)
            all.Add(ceiling);
        // A hull piece needs something drawn and something solid: one that lacks either joins the nearest band's on its side, else the floor.
        foreach (var (key, h) in hull.ToList())
        {
            if (h.Boxes.Any(Drawn) && h.Boxes.Any(q => !Drawn(q)))
                continue;
            hull.Remove(key);
            var to = hull.Where(q => q.Key.Side == key.Side && q.Value.Boxes.Any(Drawn)).OrderBy(q => Math.Abs(q.Key.Band - key.Band)).Select(q => q.Value).FirstOrDefault() ?? core;
            to.Boxes.AddRange(h.Boxes);
        }
        all.AddRange(hull.Values);
        DealSockets(core, all, p => WallOf(p, 0, true, true) is int i ? WallPiece(i) : null);
        (double Mass, double Hp, int Armour) Kind(Part q) => q == ceiling ? (1200.0, 5000.0, 25) : q.Color == Wall ? (700.0, 3500.0, 25) : (1800.0, 9000.0, 40);
        var others = all.Skip(1).Sum(q => Kind(q).Mass);
        var share = others <= 0 ? 1.0 : Math.Min(1.0, mass * 0.6 / others); // (the pieces take at most 60% of the part's mass; the floor keeps the rest)
        foreach (var q in all.Skip(1))
        {
            var (m, hp, ar) = Kind(q);
            q.Stats(m * share, hp, ar, 6.0);
        }
        core.Stats(mass - others * share, health * 0.5, 30, 15.0);
        return all;
    }

    protected static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    protected static bool Drawn(PyDict b) => b.Get("detail") is true;

    protected static double[] Mid3(PyDict b)
    {
        double[] mn = Vd(b, "min"), mx = Vd(b, "max");
        return [(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2];
    }

    /// <summary>The station band z is in.</summary>
    protected int Band(double z)
    {
        var i = 0;
        while (i < Bands.Length - 2 && z >= Bands[i + 1])
            i++;
        return i;
    }

    /// <summary>The keel or the roof cut at the stations: the band holding coreZ keeps the part's name. A tank's capacity is shared out,
    /// each band with its own Fuel out (a jet takes its fuel from the tank it is on).</summary>
    protected List<Part> SplitBands(Part core, string shortName, double coreZ)
    {
        var keep = Band(coreZ);
        var bands = new SortedDictionary<int, Part>();
        double mass = Convert.ToDouble(core.D["mass"]), health = Convert.ToDouble(core.D["health"]);
        var armour = core.D["armour"];
        var boxes = core.Boxes.ToList();
        core.Boxes.Clear();
        foreach (var b in boxes)
        {
            var k = Band(Mid3(b)[2]);
            if (k == keep)
                core.Add(b);
            else
            {
                if (!bands.TryGetValue(k, out var piece))
                    bands[k] = piece = Big($"{Prefix} {shortName} {k + 1}", $"A length of the {Prefix}'s {shortName.ToLowerInvariant()}, between two of the hull's stations: plating, armour and the void inside.", color: core.Color);
                piece.Add(b);
            }
        }
        // A band holding only drawn details goes to the nearest band with solid boxes.
        foreach (var k in bands.Keys.ToList())
            if (!bands[k].Boxes.Any(bx => !Drawn(bx)))
            {
                var to = bands.Where(e => e.Key != k && e.Value.Boxes.Any(bx => !Drawn(bx))).Select(e => (K: e.Key, P: e.Value)).Append((K: keep, P: core))
                    .MinBy(e => Math.Abs(e.K - k)).P;
                foreach (var bx in bands[k].Boxes)
                    to.Add(bx);
                Console.WriteLine($"{Tag}: {shortName} band {k + 1} has only details; they join {to.D["name"]}");
                bands.Remove(k);
            }
        var all = new List<Part> { core };
        all.AddRange(bands.Values);
        DealSockets(core, all, at => Band(at[2]) == keep ? core : bands.GetValueOrDefault(Band(at[2])));
        foreach (var q in all)
            q.Stats(mass / all.Count, health * 0.45, armour, 10.0);
        if (core.D.Get("fuelTank") is PyDict tank)
        {
            var each = (int)Math.Round(Convert.ToDouble(tank["capacity"]) / all.Count);
            foreach (var q in all)
            {
                q.D["role"] = core.D["role"];
                q.Set(("fuelTank", Kw(("capacity", each))));
                FuelPort(q, -0.4);
            }
        }
        return all;
    }

    /// <summary>A fuel tank's Fuel out on its aft (+z) face, on the centreline at height y (ship metres).</summary>
    protected void FuelPort(Part q, double y)
    {
        var solid = q.Boxes.Where(bx => !Drawn(bx)).ToList();
        var zmax = solid.Max(bx => Convert.ToDouble(((List<object?>)bx["max"]!)[2]));
        var aft = solid.Where(bx => Math.Abs(Vd(bx, "max")[2] - H - zmax) < 0.002 && Vd(bx, "min")[0] <= Cx && Vd(bx, "max")[0] >= Cx).ToList();
        if (aft.Count > 0)
            y = Math.Clamp(y, aft.Min(bx => Vd(bx, "min")[1]) + 0.05, aft.Max(bx => Vd(bx, "max")[1]) - 0.05);
        q.Set(("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. new[] { Cx - H, y - H, zmax }.Cast<object>()], "+z", "fuel"))));
    }

    /// <summary>A split part's sockets: each to the piece whose box face it is on (a solid box first), else to fallback's, else the core.</summary>
    protected void DealSockets(Part core, List<Part> all, Func<double[], Part?> fallback)
    {
        var name = (string)core.D["name"]!;
        var socks = core.Sockets.ToList();
        core.Sockets.Clear();
        foreach (var sk in socks)
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
            owner ??= fallback(at) ?? drawnOwner ?? core;
            owner.Sockets.Add(sk);
            sockOwner[(name, (string)sk["name"]!)] = (string)owner.D["name"]!;
        }
    }

    /// <summary>A facing socket pair wherever two of the parts' solid boxes touch face to face (one pair a pair of parts, on their
    /// biggest contact), unless they already meet at one: what welds the pieces to each other.</summary>
    protected static void Connect(List<Part> parts)
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
    protected static bool Meet(Part a, Part b)
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

    /// <summary>The order to place the structure in: out from the first part over the socket pairs they meet at. Throws on a piece
    /// joined to nothing (a floating bit).</summary>
    protected List<string> PlaceOrder(List<Part> parts, string first)
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
            throw new InvalidOperationException($"{Tag}: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    // ---- the blueprint

    /// <summary>Places the structure in order (the first by First, the rest welded on at the origin) and returns each piece's step.</summary>
    protected Dictionary<string, int> PlaceStructure(BuildingParts.Blueprint bp, List<string> order)
    {
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in order)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        pieceSteps = placed;
        return placed;
    }

    /// <summary>The step that holds socket sock of step on: the piece that took it when its part was split, else on itself.</summary>
    protected int Holder(BuildingParts.Blueprint bp, int on, string sock) =>
        bp.Steps[on]["part"] is string part && sockOwner.TryGetValue((part, sock), out var piece) ? pieceSteps[piece] : on;

    protected int Hang(BuildingParts.Blueprint bp, string part, int on, string sock, string by, int spin = 0) => bp.Hang(part, Holder(bp, on, sock), sock, by, spin);

    protected static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    /// <summary>A bulkhead door in a doorway, in the style's hazard paint.</summary>
    protected int HangDoor(BuildingParts.Blueprint bp, int on, string sock, string? paint = null) => Paint(bp, Hang(bp, "Bulkhead Door 1.5x2.25 m", on, sock, "hang"), paint ?? Hazard, "worn paint");

    protected int Lamp(BuildingParts.Blueprint bp, int on, string sock, string colour = "#fff0d0")
    {
        var s = Hang(bp, "Bulkhead Light", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    protected void Switch(BuildingParts.Blueprint bp, int on, string sock, params int[] lamps) => Switch(bp, on, sock, true, lamps);

    protected void Switch(BuildingParts.Blueprint bp, int on, string sock, bool startOn, params int[] lamps)
    {
        var s = Hang(bp, "Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = startOn };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
    }

    /// <summary>Writes the ship's parts (each name at most 32 characters) into outDir and its blueprint, made from their definitions.</summary>
    protected void Emit(PartOutput o, string outDir, IEnumerable<Part> parts, string blueprintPath, Func<Dictionary<string, PyDict>, PyDict> blueprint)
    {
        var defs = new Dictionary<string, PyDict>();
        o.Own(outDir);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"{Tag}: {name} is longer than 32 characters");
            defs.Add(name, d);
            o.File($"{outDir}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, outDir);
        o.File(blueprintPath, PyJson.Dumps(blueprint(defs), 2) + "\n");
    }
}
