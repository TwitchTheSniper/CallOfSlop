using BP = CallOfSlop.PartKit.Generators.BuildingParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Constructs/Blueprints/RV.json, the motorhome blueprint (board item dev-blueprint-rv-rv01). Was tools/constructs/make_rv.py.
///
/// The blueprint is a build script: every step fits a shipped part onto a free socket of an earlier part, through the game's own rules
/// (ConstructBuild.Place). This mirrors those rules (Rot24 turns, the fixed spin order, the 1 cm grid, the overlap test and AutoLink),
/// so the RV is designed in body coordinates (metres) and the generator works out which socket of which earlier part takes each piece.
/// <c>Put</c> finds the first socket combination that lands a part exactly where it is wanted, <c>Near</c> takes the free spot nearest
/// a wanted place, and <c>Hang</c> names the sockets (a hinge on a post, a door on a hinge). Nothing here invents a part: a name that
/// is not in Constructs/Parts stops it. It reads every part file, so run it after the generators that write parts.
/// </summary>
public sealed class MakeRv : IPartGenerator
{
    public string Name => "make_rv";

    private const string Out = "Constructs/Blueprints/RV.json";
    private const double Eps = 0.01; // the game's overlap slack (ConstructBuild.Blocked)
    private const string Description = "A 10 m diesel motorhome you can walk about in.";

    private sealed class Def
    {
        public required string Name;
        public required PyDict D;
        public required List<PyDict> Sockets;
        public required List<(double[] C, double[] H)> Solids;
    }

    private static double[] Vec3(object? v) => [.. ((List<object?>)v!).Select(Py.D)];

    /// <summary>Every part file by name (the last in sorted path order keeps a name), with its solid boxes as centre and half size.</summary>
    private static Dictionary<string, Def> LoadDefs(PartOutput o)
    {
        var defs = new Dictionary<string, Def>();
        var root = Path.Combine(o.Root, "Constructs", "Parts");
        var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(o.Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal);
        foreach (var f in files)
        {
            if (o.ReadJson(f) is not PyDict d || d.Get("name") is not string name)
                continue;
            var solids = new List<(double[], double[])>();
            foreach (var b in (d.Get("boxes") as List<object?> ?? []).Cast<PyDict>())
            {
                if (Py.Truthy(b.Get("detail")))
                    continue;
                double[] mn, mx;
                if (b.Has("min"))
                    (mn, mx) = (Vec3(b["min"]), Vec3(b["max"]));
                else
                {
                    var at = b.Has("at") ? Vec3(b["at"]) : [0, 0, 0];
                    var size = Vec3(b["size"]);
                    mn = [.. Enumerable.Range(0, 3).Select(k => at[k] - size[k] / 2)];
                    mx = [.. Enumerable.Range(0, 3).Select(k => at[k] + size[k] / 2)];
                }
                solids.Add(([.. Enumerable.Range(0, 3).Select(k => (mn[k] + mx[k]) / 2)], [.. Enumerable.Range(0, 3).Select(k => (mx[k] - mn[k]) / 2)]));
            }
            defs[name] = new Def { Name = name, D = d, Sockets = (d.Get("sockets") as List<object?> ?? []).Cast<PyDict>().ToList(), Solids = solids };
        }
        return defs;
    }

    private static string Kind(Def d, int i) => d.Sockets[i].Get("kind") as string ?? "struct";

    private static int DirOf(string d) => Array.IndexOf(BP.Dirs, d);

    private sealed class Placed(string name, Def d, double[] pos, int rot)
    {
        public readonly string Name = name;
        public readonly Def D = d;
        public readonly double[] Pos = pos;
        public readonly int Rot = rot;
        public readonly HashSet<int> Used = [];

        public (double[] At, int Dir) Sock(int i)
        {
            var s = D.Sockets[i];
            var a = BP.Apply(Rot, Vec3(s["at"]));
            return ([Pos[0] + a[0], Pos[1] + a[1], Pos[2] + a[2]], BP.ApplyDir(Rot, DirOf((string)s["dir"]!)));
        }

        public IEnumerable<(double[] C, double[] H)> Boxes()
        {
            foreach (var (c, h) in D.Solids)
            {
                var ac = BP.Apply(Rot, c);
                var ah = BP.Apply(Rot, h).Select(Math.Abs).ToArray();
                yield return ([Pos[0] + ac[0], Pos[1] + ac[1], Pos[2] + ac[2]], ah);
            }
        }

        public (double[] Lo, double[] Hi) Bounds()
        {
            double[] lo = [9e9, 9e9, 9e9], hi = [-9e9, -9e9, -9e9];
            foreach (var (c, h) in Boxes())
                for (var k = 0; k < 3; k++)
                {
                    lo[k] = Math.Min(lo[k], c[k] - h[k]);
                    hi[k] = Math.Max(hi[k], c[k] + h[k]);
                }
            return (lo, hi);
        }
    }

    private readonly record struct Cand(int J, int T, int Ds, int Spin, double[] Pos, int Rot);

    private sealed class Blueprint(Dictionary<string, Def> defs)
    {
        public readonly Dictionary<string, Def> Defs = defs;
        public readonly List<Placed> Parts = [];
        public readonly List<PyDict> Steps = [];
        public readonly PyDict Extra = new();
        private readonly Dictionary<(string, int, int), List<int>> _turns = [];

        private Def DefOf(string part) => Defs.TryGetValue(part, out var d) ? d : throw new InvalidOperationException($"make_rv: no part named {part}");

        private List<int> FitsTurns(Def d, int ds, int want)
        {
            var key = (d.Name, ds, want);
            if (_turns.TryGetValue(key, out var cached))
                return cached;
            var dir = DirOf((string)d.Sockets[ds]["dir"]!);
            var turns = Enumerable.Range(0, 24).Where(rr => BP.ApplyDir(rr, dir) == want).ToList();
            for (var a = 0; a < turns.Count; a++)
                for (var b = a + 1; b < turns.Count; b++)
                    if (BP.Upright(turns[b]) > BP.Upright(turns[a]))
                        (turns[a], turns[b]) = (turns[b], turns[a]);
            return _turns[key] = turns;
        }

        private (double[] Pos, int Rot)? Pose(int j, int t, Def d, int ds, int spin)
        {
            var (at, dr) = Parts[j].Sock(t);
            var turns = FitsTurns(d, ds, dr ^ 1);
            if (turns.Count == 0)
                return null;
            var rot = turns[spin % turns.Count];
            var sa = BP.Apply(rot, Vec3(d.Sockets[ds]["at"]));
            return (BP.Quant([at[0] - sa[0], at[1] - sa[1], at[2] - sa[2]]), rot);
        }

        private string? Blocked(Def d, double[] pos, int rot)
        {
            var probe = new Placed("?", d, pos, rot);
            foreach (var (c, h) in probe.Boxes())
                for (var i = 0; i < Parts.Count; i++)
                    foreach (var (oc, oh) in Parts[i].Boxes())
                        if (Math.Abs(c[0] - oc[0]) < h[0] + oh[0] - Eps && Math.Abs(c[1] - oc[1]) < h[1] + oh[1] - Eps && Math.Abs(c[2] - oc[2]) < h[2] + oh[2] - Eps)
                            return Parts[i].Name + " #" + i;
            return null;
        }

        public int First(string part, int rot = 0)
        {
            Parts.Add(new Placed(part, DefOf(part), [0.0, 0.0, 0.0], rot));
            Steps.Add(rot == 0 ? new PyDict { ["part"] = part } : new PyDict { ["part"] = part, ["spin"] = rot });
            return 0;
        }

        private int Commit(string part, int j, int t, int ds, double[] pos, int rot, PyDict step)
        {
            var d = DefOf(part);
            if (Blocked(d, pos, rot) is { } why)
                throw new InvalidOperationException($"make_rv: {part} at [{string.Join(", ", pos)}]: in the way of {why}");
            var nw = new Placed(part, d, pos, rot);
            Parts[j].Used.Add(t);
            nw.Used.Add(ds);
            Parts.Add(nw);
            Steps.Add(step);
            var me = Parts.Count - 1;
            // AutoLink (ConstructBuild.AutoLink): free struct sockets that meet a free struct socket of an earlier part.
            for (var s = 0; s < d.Sockets.Count; s++)
            {
                if (nw.Used.Contains(s) || Kind(d, s) != "struct")
                    continue;
                var (at, dr) = nw.Sock(s);
                for (var jj = 0; jj < Parts.Count - 1; jj++)
                {
                    var other = Parts[jj];
                    var done = false;
                    for (var tt = 0; tt < other.D.Sockets.Count; tt++)
                    {
                        if (other.Used.Contains(tt) || Kind(other.D, tt) != "struct")
                            continue;
                        var (oat, odr) = other.Sock(tt);
                        var d2 = Py.Sum(Enumerable.Range(0, 3).Select(k => Math.Pow(oat[k] - at[k], 2)));
                        if (odr == (dr ^ 1) && d2 < 0.0004)
                        {
                            other.Used.Add(tt);
                            nw.Used.Add(s);
                            done = true;
                            break;
                        }
                    }
                    if (done)
                        break;
                }
            }
            return me;
        }

        private static int SockIndex(Def d, string name)
        {
            var i = d.Sockets.FindIndex(s => (string)s["name"]! == name);
            return i >= 0 ? i : throw new InvalidOperationException($"make_rv: {d.Name} has no socket {name}");
        }

        /// <summary>A step by socket names; extra: keys added to the step (a setting).</summary>
        public int Hang(string part, int j, string sock, string by, int spin = 0, params (string, object?)[] extra)
        {
            var tgt = Parts[j];
            var d = DefOf(part);
            var t = SockIndex(tgt.D, sock);
            var ds = SockIndex(d, by);
            if (tgt.Used.Contains(t))
                throw new InvalidOperationException($"make_rv: {part}: {tgt.Name} #{j} socket {sock} is already used");
            var (pos, rr) = Pose(j, t, d, ds, spin)!.Value;
            var step = new PyDict { ["part"] = part, ["on"] = j, ["socket"] = sock, ["by"] = by };
            if (spin != 0)
                step["spin"] = spin;
            step.Update(extra);
            return Commit(part, j, t, ds, pos, rr, step);
        }

        /// <summary>Every way to fit `part` on a free struct socket.</summary>
        private List<Cand> Candidates(string part, int? rot, (double[] Lo, double[] Hi)? within)
        {
            var d = DefOf(part);
            var o = new List<Cand>();
            for (var j = 0; j < Parts.Count; j++)
            {
                var tgt = Parts[j];
                for (var t = 0; t < tgt.D.Sockets.Count; t++)
                {
                    if (tgt.Used.Contains(t) || Kind(tgt.D, t) != "struct")
                        continue;
                    if (within is { } w)
                    {
                        var sp = tgt.Sock(t).At;
                        if (!Enumerable.Range(0, 3).All(k => w.Lo[k] <= sp[k] && sp[k] <= w.Hi[k]))
                            continue;
                    }
                    for (var ds = 0; ds < d.Sockets.Count; ds++)
                    {
                        if (Kind(d, ds) != "struct")
                            continue;
                        var n = FitsTurns(d, ds, tgt.Sock(t).Dir ^ 1).Count;
                        for (var spin = 0; spin < n; spin++)
                        {
                            if (Pose(j, t, d, ds, spin) is not { } r)
                                continue;
                            if (rot is not null && r.Rot != rot)
                                continue;
                            o.Add(new Cand(j, t, ds, spin, r.Pos, r.Rot));
                        }
                    }
                }
            }
            return o;
        }

        private PyDict StepOf(string part, Cand c)
        {
            var step = new PyDict { ["part"] = part, ["on"] = c.J, ["socket"] = Parts[c.J].D.Sockets[c.T]["name"], ["by"] = DefOf(part).Sockets[c.Ds]["name"] };
            if (c.Spin != 0)
                step["spin"] = c.Spin;
            return step;
        }

        /// <summary>Fits `part` with its origin at `at` (metres, body frame) turned `rot`.</summary>
        public int Put(string part, double[] at, int rot)
        {
            at = BP.Quant(at);
            foreach (var c in Candidates(part, rot, null))
                if (Math.Abs(c.Pos[0] - at[0]) < 0.004 && Math.Abs(c.Pos[1] - at[1]) < 0.004 && Math.Abs(c.Pos[2] - at[2]) < 0.004)
                    return Commit(part, c.J, c.T, c.Ds, c.Pos, c.Rot, StepOf(part, c));
            throw new InvalidOperationException($"make_rv: no socket takes {part} at [{string.Join(", ", at)}] turned {rot}");
        }

        /// <summary>Fits `part` turned `rot` so its solid boxes' minimum corner is at `lo`.</summary>
        public int PutBox(string part, double[] lo, int rot)
        {
            var (plo, _) = new Placed(part, DefOf(part), [0, 0, 0], rot).Bounds();
            return Put(part, [lo[0] - plo[0], lo[1] - plo[1], lo[2] - plo[2]], rot);
        }

        /// <summary>Fits `part` where its bounding-box centre is nearest `want` (unblocked, inside `box` if given).</summary>
        public int Near(string part, double[] want, (double[] Lo, double[] Hi)? box = null, double limit = 0.6, IReadOnlySet<int>? rots = null)
        {
            var d = DefOf(part);
            (double Dist, Cand C)? best = null;
            (double[], double[])? within = null;
            if (box is { } bx)
                within = ([.. bx.Lo.Select(v => v - 2.3)], [.. bx.Hi.Select(v => v + 2.3)]);
            foreach (var c in Candidates(part, null, within))
            {
                if (rots is not null && !rots.Contains(c.Rot))
                    continue;
                var (lo, hi) = new Placed(part, d, c.Pos, c.Rot).Bounds();
                if (box is { } b && !Enumerable.Range(0, 3).All(k => lo[k] >= b.Lo[k] - 0.011 && hi[k] <= b.Hi[k] + 0.011))
                    continue;
                if (Blocked(d, c.Pos, c.Rot) is not null)
                    continue;
                double[] ctr = [(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2];
                var dist = Math.Sqrt(Math.Pow(ctr[0] - want[0], 2) + Math.Pow(ctr[1] - want[1], 2) + Math.Pow(ctr[2] - want[2], 2));
                if (best is null || dist < best.Value.Dist - 1e-9)
                    best = (dist, c);
            }
            if (best is null || best.Value.Dist > limit)
                throw new InvalidOperationException($"make_rv: nothing takes {part} near [{string.Join(", ", want)}]");
            var bc = best.Value.C;
            return Commit(part, bc.J, bc.T, bc.Ds, bc.Pos, bc.Rot, StepOf(part, bc));
        }
    }

    /// <summary>The steps by key, in the order they were named (the paint lists pick keys by prefix in that order).</summary>
    private sealed class Keys
    {
        public readonly Dictionary<string, int> At = [];
        public readonly List<string> Order = [];

        public int this[string k]
        {
            get => At[k];
            set
            {
                if (!At.ContainsKey(k))
                    Order.Add(k);
                At[k] = value;
            }
        }
    }

    /// <summary>The 4x4 truck's chassis and drive line (Constructs/Blueprints/Truck.json), two beams longer.</summary>
    private static Keys Chassis(Blueprint bp)
    {
        var s = new Keys();
        s["b0"] = bp.First("Frame Beam");
        s["b1"] = bp.Hang("Frame Beam", 0, "x+0", "x-0");
        s["f0"] = bp.Hang("Frame Beam", 0, "end-", "end+");
        s["f1"] = bp.Hang("Frame Beam", 1, "end-", "end+");
        s["r0"] = bp.Hang("Frame Beam", 0, "end+", "end-");
        s["r1"] = bp.Hang("Frame Beam", 1, "end+", "end-");
        s["r2"] = bp.Hang("Frame Beam", s["r0"], "end+", "end-");
        s["r3"] = bp.Hang("Frame Beam", s["r1"], "end+", "end-");
        s["blk0"] = bp.Hang("Frame Block", s["f0"], "end-", "+z");
        s["blk1"] = bp.Hang("Frame Block", s["f1"], "end-", "+z");
        s["fdiff"] = bp.Hang("Wide Differential", s["blk0"], "-z", "+z0");
        s["rdiff"] = bp.Hang("Live Axle", s["r2"], "end+", "-z0");
        s["engine"] = bp.Hang("Diesel Engine", s["f1"], "y+1", "-y0");
        s["gear"] = bp.Hang("Automatic Gearbox", s["engine"], "+z", "-z");
        s["tcase"] = bp.Hang("Transfer Case", s["gear"], "+z", "in", 0, ("setting", 1));
        // front drive: right-angle box, shafts, right-angle box into the wide differential
        s["fr1"] = bp.Hang("Right-Angle Gearbox", s["tcase"], "front", "-z", spin: 2);
        s["fs1"] = bp.Hang("Drive Shaft 2 m", s["fr1"], "-y", "end+");
        s["fs2"] = bp.Hang("Drive Shaft 0.5 m", s["fs1"], "end-", "end+");
        s["fr2"] = bp.Hang("Right-Angle Gearbox", s["fs2"], "end-", "-z");
        // rear drive
        s["rs1"] = bp.Hang("Drive Shaft 2 m", s["tcase"], "rear", "end-");
        s["rs2"] = bp.Hang("Drive Shaft 2 m", s["rs1"], "end+", "end-");
        s["rs4"] = bp.Hang("Drive Shaft 1 m", s["rs2"], "end+", "end-");
        s["rs5"] = bp.Hang("Drive Shaft 0.5 m", s["rs4"], "end+", "end-");
        s["rr"] = bp.Hang("Right-Angle Gearbox", s["rs5"], "end+", "-z");
        s["cvl"] = bp.Hang("CV Joint", s["fdiff"], "-x", "+x");
        s["cvr"] = bp.Hang("CV Joint", s["fdiff"], "+x", "-x");
        s["pvl"] = bp.Hang("Steering Pivot", s["cvl"], "-x", "base");
        s["pvr"] = bp.Hang("Steering Pivot", s["cvr"], "+x", "base");
        s["lfl"] = bp.Hang("Leaf Spring", s["pvl"], "leaf", "mount");
        s["lfr"] = bp.Hang("Leaf Spring", s["pvr"], "leaf", "mount");
        s["lrl"] = bp.Hang("Stiff Suspension", s["rdiff"], "-x", "mount");
        s["lrr"] = bp.Hang("Stiff Suspension", s["rdiff"], "+x", "mount");
        foreach (var k in new[] { "lfl", "lfr", "lrl", "lrr" })
            s["w" + k[1..]] = bp.Hang("Large Wheel", s[k], "axle", "axle");
        return s;
    }

    /// <summary>The turns that send the part's local axis direction (e.g. '+y') to a body direction.</summary>
    private static HashSet<int> Facing(string local, string world) => [.. Enumerable.Range(0, 24).Where(r => BP.ApplyDir(r, DirOf(local)) == DirOf(world))];

    private static (double[], double[]) Bx(double[] lo, double[] hi) => (lo, hi);

    private static Blueprint Build(Dictionary<string, Def> defs)
    {
        var bp = new Blueprint(defs);
        var s = Chassis(bp);
        // floor on three risers over the left rail
        foreach (var (name, beam, sock) in new[] { ("a", s["b0"], "y+1"), ("b", s["r0"], "y+0"), ("c", s["r2"], "y+0") })
            s["riser" + name] = bp.Hang("Frame Block", beam, sock, "-y");
        foreach (var (k, z) in new[] { ("a", -0.875), ("b", 1.125), ("c", 3.125) })
            s["floor" + k] = bp.PutBox("Wood Planks 2 m", [-0.625, 0.75, z], 0);

        int Fit(string part, double[] lo, double[] hi, double tol = 0.03, IReadOnlySet<int>? rots = null)
        {
            double[] want = [(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2];
            var box = Bx([.. lo.Select(v => v - tol)], [.. hi.Select(v => v + tol)]);
            return bp.Near(part, want, box: box, limit: 0.2, rots: rots);
        }

        double x0 = -0.505, x1 = 1.245; // side walls
        HashSet<int> lw = Facing("-y", "+x"), rw = Facing("-y", "-x"); // the back face of a left (right) wall looks into the room
        void WallRow(string side, double z, string partLow, string partHigh, HashSet<int> rots)
        {
            var x = side == "l" ? x0 : x1;
            s[side + "low" + Py.Repr(z)] = Fit(partLow, [x - 0.015, 0.805, z], [x + 0.015, 1.805, z + 2], rots: rots);
            s[side + "high" + Py.Repr(z)] = Fit(partHigh, [x - 0.015, 1.805, z], [x + 0.015, 2.805, z + 2], rots: rots);
        }
        WallRow("l", -0.875, "Sheet Steel 1x2 m", "Glass Pane 1x2 m", lw);
        WallRow("l", 1.125, "Sheet Steel 1x2 m", "Glass Pane 1x2 m", lw);
        s["l2"] = Fit("Sheet Steel 2 m", [x0 - 0.015, 0.805, 3.125], [x0 + 0.015, 2.805, 5.125], rots: lw);
        WallRow("r", -0.875, "Sheet Steel 1x2 m", "Glass Pane 1x2 m", rw);
        s["front"] = Fit("Sheet Steel 1x2 m", [-0.625, 0.805, -0.875], [1.375, 1.805, -0.855]);
        s["glass"] = Fit("Glass Pane 1x2 m", [-0.625, 1.805, -0.875], [1.375, 2.805, -0.855], tol: 0.05);
        s["rear"] = Fit("Sheet Steel 2 m", [-0.625, 0.805, 5.105], [1.375, 2.805, 5.135], tol: 0.05);
        foreach (var (k, z) in new[] { ("0", -0.875), ("2", 3.125) })
            s["roof" + k] = Fit("Sheet Steel 2 m", [-0.625, 2.79, z], [1.375, 2.84, z + 2], tol: 0.06);
        // over the door the roof stops short: the door (raised a hand on its hinge) stands 0.125 into the roof's level
        s["roof1"] = Fit("Sheet Steel 1x2 m", [-0.625, 2.79, 1.125], [0.375, 2.84, 3.125], tol: 0.06);
        s["roof1a"] = Fit("Sheet Steel 0.5x1 m", [0.375, 2.79, 1.125], [0.875, 2.84, 2.125], tol: 0.06);
        s["roof1b"] = Fit("Sheet Steel 0.5x1 m", [0.375, 2.79, 2.125], [0.875, 2.84, 3.125], tol: 0.06);
        EngineRoom(bp, s);
        Interior(bp, s, Fit);
        Electrics(bp, s);
        return bp;
    }

    /// <summary>Fuel tank beside the left rail, battery on the right rail up front, alternator on the diesel; the tank is piped to the engine.</summary>
    private static void EngineRoom(Blueprint bp, Keys s)
    {
        s["tank"] = bp.Near("Large Fuel Tank", [-0.5, 0.0, 2.75], box: Bx([-0.8, -0.3, 2.2], [-0.2, 0.3, 3.3]), limit: 0.2);
        // (turned so its 0.2 m side is across the beam: its 0.3 m ends would be flush with the beam's bolt plate)
        var across = Enumerable.Range(0, 24).Where(r => BP.ApplyDir(r, DirOf("-y")) == DirOf("-y")
            && (BP.ApplyDir(r, DirOf("+x")) == DirOf("+z") || BP.ApplyDir(r, DirOf("+x")) == DirOf("-z"))).ToHashSet();
        s["battery"] = bp.Near("Battery", [0.5, 0.38, -2.75], box: Bx([0.3, 0.2, -2.95], [0.7, 0.6, -2.55]), rots: across, limit: 0.2);
        s["alt"] = bp.Hang("Alternator", s["engine"], "+x", "-x");
        bp.Extra["links"] = new List<object?> { new PyDict { ["type"] = "pipe", ["style"] = "fuel", ["from"] = s["tank"], ["to"] = s["engine"] } };
    }

    /// <summary>Turns that keep a seat upright with its back toward body direction `back`.</summary>
    private static HashSet<int> SeatTurns(string back) =>
        [.. Enumerable.Range(0, 24).Where(r => BP.ApplyDir(r, DirOf("-y")) == DirOf("-y") && BP.ApplyDir(r, DirOf("+z")) == DirOf(back))];

    private delegate int FitFn(string part, double[] lo, double[] hi, double tol = 0.03, IReadOnlySet<int>? rots = null);

    /// <summary>Cab, door, furniture, mounted on stubs off the wall (the floor planks have no free top sockets).</summary>
    private static void Interior(Blueprint bp, Keys s, FitFn fit)
    {
        HashSet<int> r0 = [0];
        // stubs: 0.5 m beams lying across, bottom flush with the floor, on the wall's lowest row
        s["stubL"] = fit("Square Beam 0.5 m", [-0.505, 0.8, -0.5], [0.02, 1.07, 0.0], tol: 0.03);
        s["stubR"] = fit("Square Beam 0.5 m", [0.72, 0.8, -0.5], [1.25, 1.07, 0.0], tol: 0.03);
        s["seatL"] = bp.Near("Seat", [-0.12, 1.53, -0.25], box: Bx([-0.52, 1.0, -0.8], [0.2, 2.1, 0.3]), rots: r0, limit: 0.5);
        // the door: a hinge on the cab window's rear edge, the door, a jamb post on the floor's corner, panels to close the gap
        s["hinge"] = bp.Near("Hinge", [1.25, 1.93, 1.23], box: Bx([1.1, 1.6, 1.1], [1.4, 2.3, 1.4]), limit: 0.1);
        s["door"] = bp.Hang("Door", s["hinge"], "leaf", "hang");
        s["jamb"] = bp.Near("Square Beam 2 m", [1.25, 1.81, 3.0], box: Bx([1.1, 0.8, 2.85], [1.4, 2.82, 3.15]), limit: 0.1);
        s["rwl"] = fit("Sheet Steel 0.5x1 m", [1.23, 0.805, 2.375], [1.28, 1.805, 2.875]);
        s["rwh"] = fit("Glass Pane 0.5x1 m", [1.23, 1.805, 2.375], [1.28, 2.805, 2.875], tol: 0.05);
        s["rear_r"] = fit("Sheet Steel 2 m", [1.23, 0.805, 3.125], [1.28, 2.805, 5.125], tol: 0.05);
        // dinette on the left of the first room: two seats facing over a table
        s["stubD1"] = fit("Square Beam 0.5 m", [-0.505, 0.8, 1.1], [0.02, 1.07, 1.4]);
        s["seatD1"] = bp.Near("Seat", [-0.12, 1.53, 1.25], box: Bx([-0.52, 1.0, 0.9], [0.2, 2.1, 1.6]), rots: SeatTurns("-z"), limit: 0.5);
        s["stubT"] = fit("Square Beam 0.5 m", [-0.505, 1.28, 1.55], [0.02, 1.57, 1.95]);
        s["table"] = bp.Near("Wood Planks 0.5 m", [-0.12, 1.58, 1.85], box: Bx([-0.52, 1.3, 1.45], [0.2, 1.7, 2.2]), limit: 0.5);
        s["stubD2"] = fit("Square Beam 0.5 m", [-0.505, 0.8, 2.35], [0.02, 1.07, 2.65]);
        s["seatD2"] = bp.Near("Seat", [-0.12, 1.53, 2.5], box: Bx([-0.52, 1.0, 2.1], [0.2, 2.1, 2.95]), rots: SeatTurns("+z"), limit: 0.5);
        // kitchen: a chest-cupboard on the left, a parts shelf on the rear wall, a bunk over the left-rear corner
        s["stubK"] = fit("Square Beam 0.5 m", [-0.505, 0.8, 0.35], [0.02, 1.07, 0.65]);
        s["chestK"] = bp.Near("Chest", [-0.115, 1.33, 0.5], box: Bx([-0.52, 1.0, 0.0], [0.3, 1.7, 1.0]), limit: 0.5);
        s["stubS"] = fit("Square Beam 0.5 m", [0.745, 0.8, 4.35], [1.26, 1.07, 4.65]);
        s["shelf"] = bp.Near("Parts Shelf", [0.875, 1.66, 4.5], box: Bx([0.5, 1.0, 3.8], [1.26, 2.4, 5.15]), limit: 0.6);
        s["stubB1"] = fit("Square Beam 0.5 m", [-0.505, 1.29, 3.6], [0.02, 1.57, 3.9]);
        s["stubB2"] = fit("Square Beam 0.5 m", [-0.505, 1.29, 4.85], [0.02, 1.57, 5.15]);
        s["bunk"] = bp.Near("Wood Planks 1x2 m", [0.0, 1.58, 4.1], box: Bx([-0.52, 1.45, 3.0], [0.6, 1.7, 5.15]), limit: 0.6);
        // boarding: a ladder and a step plate hung from the floor's right edge in front of the door
        s["ladder"] = bp.Near("Boarding Ladder", [1.42, 0.16, 1.75], box: Bx([1.3, -0.6, 1.4], [1.6, 0.8, 2.1]), limit: 0.3);
        s["seatR"] = bp.Near("Seat", [0.99, 1.53, -0.25], box: Bx([0.6, 1.0, -0.8], [1.27, 2.1, 0.3]), rots: r0, limit: 0.5);
    }

    /// <summary>The vector `v` (body frame) in the frame of a part turned `rot`.</summary>
    private static double[] LocalOf(int rot, double[] v)
    {
        double[][] cols = [BP.Apply(rot, [1, 0, 0]), BP.Apply(rot, [0, 1, 0]), BP.Apply(rot, [0, 0, 1])];
        return [.. Enumerable.Range(0, 3).Select(a => Py.Sum(Enumerable.Range(0, 3).Select(b => cols[a][b] * v[b])))];
    }

    private static List<object?> Wire(int a, int b) => [a, b];

    /// <summary>Lamps and their wiring, the cargo bay and the awning.</summary>
    private static void Electrics(Blueprint bp, Keys s)
    {
        // headlamps on the front blocks, tail lamps on top of the live axle
        s["head_l"] = bp.Hang("Lamp", s["blk0"], "+y", "-y");
        s["head_r"] = bp.Hang("Lamp", s["blk1"], "+y", "-y");
        s["tail_l"] = bp.Hang("Lamp", s["rdiff"], "top2", "-y");
        s["tail_r"] = bp.Hang("Lamp", s["rdiff"], "top3", "-y");
        // the driver's aim key toggles them: seat -> input reader -> toggle -> lamps
        s["reader"] = bp.Hang("Input Reader", s["seatL"], "+x", "-x", 0, ("setting", 1));
        s["toggle"] = bp.Hang("Toggle", s["stubL"], "+z", "-z");
        // the driver's seat drives through a Driving Module on its other side
        s["drive_mod"] = bp.Hang("Driving Module", s["reader"], "+x", "-x");
        var wires = new List<object?> { Wire(s["seatL"], s["drive_mod"]), Wire(s["drive_mod"], s["engine"]), Wire(s["seatL"], s["reader"]), Wire(s["reader"], s["toggle"]) };
        foreach (var k in new[] { "head_l", "head_r", "tail_l", "tail_r" })
            wires.Add(Wire(s["toggle"], s[k]));
        // the ceiling lamps on a switch by the door
        s["switch"] = bp.Hang("Switch", s["stubD1"], "+z", "-z");
        double[] at = [0.3, 2.7, 2.0];
        s["lamp_a"] = bp.Near("Lamp", at, box: Bx([at[0] - 0.4, 2.55, at[2] - 0.5], [at[0] + 0.4, 2.82, at[2] + 0.5]), limit: 0.6);
        wires.Add(Wire(s["switch"], s["lamp_a"]));
        // the cargo bay under the bunk
        s["cargo"] = bp.Near("Cargo Volume", [0.0, 1.45, 4.1], box: Bx([-0.45, 1.3, 3.4], [0.45, 1.56, 4.8]), limit: 0.7);
        var cp = bp.Parts[s["cargo"]];
        double[] size = [0.9, 0.5, 1.7];
        double[] centre = [0.0, 1.1, 4.1];
        var off = LocalOf(cp.Rot, [centre[0] - cp.Pos[0], centre[1] - cp.Pos[1], centre[2] - cp.Pos[2]]);
        var sz = LocalOf(cp.Rot, size).Select(Math.Abs).ToArray();
        bp.Steps[s["cargo"]]["config"] = new PyDict
        {
            ["startOn"] = true, ["sizeX"] = Py.Round(sz[0], 1), ["sizeY"] = Py.Round(sz[1], 1), ["sizeZ"] = Py.Round(sz[2], 1),
            ["atX"] = Py.Round(off[0], 2), ["atY"] = Py.Round(off[1], 2), ["atZ"] = Py.Round(off[2], 2),
        };
        // the awning: a motor hinge on the roof's right edge, worked by a switch on the passenger stub
        s["motor"] = bp.Near("Motor Hinge", [1.48, 2.81, 3.75], box: Bx([1.3, 2.6, 3.4], [1.7, 3.1, 4.1]), rots: new HashSet<int> { 5, 6 }, limit: 0.3);
        s["awning"] = bp.Hang("Sheet Steel 0.5x1 m", s["motor"], "leaf", "-x0");
        s["switch2"] = bp.Hang("Switch", s["stubR"], "+z", "-z");
        wires.Add(Wire(s["switch2"], s["motor"]));
        bp.Extra["wires"] = wires;
        Paint(bp, s);
    }

    private const string Cream = "#efe6c8", Stripe = "#c8602a", Trim = "#2c2f33", Timber = "#b08a5a";

    /// <summary>Cream body with an orange stripe along the lower walls and the door, dark trim.</summary>
    private static void Paint(Blueprint bp, Keys s)
    {
        void Put(IEnumerable<string> keys, string colour)
        {
            foreach (var k in keys)
                bp.Steps[s[k]]["paint"] = colour;
        }
        Put([.. s.Order.Where(k => k.StartsWith("llow", StringComparison.Ordinal) || k.StartsWith("rlow", StringComparison.Ordinal)), "rwl", "door", "awning"], Stripe);
        Put(["l2", "rear", "rear_r", "front", "roof0", "roof1", "roof1a", "roof1b", "roof2", .. s.Order.Where(k => k.StartsWith("lhigh", StringComparison.Ordinal))], Cream);
        Put(["jamb", "stubL", "stubR", "stubD1", "stubD2", "stubT", "stubK", "stubS", "stubB1", "stubB2", "riserb", "riserc", "ladder"], Trim);
        Put(["table", "bunk"], Timber);
    }

    public void Make(PartOutput o)
    {
        var bp = Build(LoadDefs(o));
        var doc = new PyDict { ["format"] = "call-of-slop-construct-blueprint/1", ["name"] = "RV", ["description"] = Description, ["steps"] = bp.Steps.Cast<object?>().ToList() };
        doc.Update(bp.Extra);
        o.File(Out, PyJson.Dumps(doc, 2) + "\n");
    }
}
