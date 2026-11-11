using System.Numerics;

namespace CallOfSlop;

/// <summary>An oriented box: centre, unit axes and half extents (from a unit-cube world matrix). Used by the connectivity checks.</summary>
public readonly record struct ModelBox(Vector3 C, Vector3 X, Vector3 Y, Vector3 Z, Vector3 H)
{
    public static ModelBox Of(Matrix4x4 w)
    {
        var x = new Vector3(w.M11, w.M12, w.M13);
        var y = new Vector3(w.M21, w.M22, w.M23);
        var z = new Vector3(w.M31, w.M32, w.M33);
        float lx = MathF.Max(x.Length(), 1e-9f), ly = MathF.Max(y.Length(), 1e-9f), lz = MathF.Max(z.Length(), 1e-9f);
        return new ModelBox(w.Translation, x / lx, y / ly, z / lz, new Vector3(lx, ly, lz) * 0.5f);
    }

    /// <summary>The box's reach along Z (world): its smallest and largest z.</summary>
    public float MinZ => C.Z - (H.X * MathF.Abs(X.Z) + H.Y * MathF.Abs(Y.Z) + H.Z * MathF.Abs(Z.Z));

    /// <inheritdoc cref="MinZ"/>
    public float MaxZ => C.Z + (H.X * MathF.Abs(X.Z) + H.Y * MathF.Abs(Y.Z) + H.Z * MathF.Abs(Z.Z));

    /// <summary>Whether point <paramref name="p"/> is inside the box (on its surface counts).</summary>
    public bool Contains(Vector3 p)
    {
        var d = p - C;
        return MathF.Abs(Vector3.Dot(d, X)) <= H.X + 1e-5f && MathF.Abs(Vector3.Dot(d, Y)) <= H.Y + 1e-5f && MathF.Abs(Vector3.Dot(d, Z)) <= H.Z + 1e-5f;
    }

    /// <summary>Separating axes: no axis splits the two boxes by more than <paramref name="tol"/>.</summary>
    public bool Touches(ModelBox o, float tol)
    {
        var t = o.C - C;
        Span<Vector3> a = [X, Y, Z];
        Span<Vector3> b = [o.X, o.Y, o.Z];
        for (var k = 0; k < 15; k++)
        {
            var axis = k < 3 ? a[k] : k < 6 ? b[k - 3] : Vector3.Cross(a[(k - 6) / 3], b[(k - 6) % 3]);
            var len = axis.Length();
            if (len < 1e-6f)
                continue;
            axis /= len;
            var ra = H.X * MathF.Abs(Vector3.Dot(X, axis)) + H.Y * MathF.Abs(Vector3.Dot(Y, axis)) + H.Z * MathF.Abs(Vector3.Dot(Z, axis));
            var rb = o.H.X * MathF.Abs(Vector3.Dot(o.X, axis)) + o.H.Y * MathF.Abs(Vector3.Dot(o.Y, axis)) + o.H.Z * MathF.Abs(Vector3.Dot(o.Z, axis));
            if (MathF.Abs(Vector3.Dot(t, axis)) > ra + rb + tol)
                return false;
        }
        return true;
    }
}

/// <summary>
/// <c>--check-builder-parts [DIR] [--tol METRES] [--receiver NAME]</c>: weapon-builder parts form one connected piece, on their own
/// and assembled (the C# port of tools/parts/floaters.py; the self-test section "Model connectivity" checks the same things).
/// 1. every part on its own, drawn as on an empty grip; 2. every root grip empty, with one receiver, every receiver, an optic and
/// every other socket filled; 3. every receiver with every action on the Rifle Grip, and (3b) actions placed "under" or "front"
/// don't cut into the grip's handle, a foregrip under the gun or a magazine that leaves them room; 4. every standard build.
/// The exit code is the number of problems.
/// </summary>
public static class BuilderPartsCheck
{
    /// <summary>Intentional separated pieces in part models: the part's name and what joins them in a build.</summary>
    public static readonly Dictionary<string, string> AllowedSeparated = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bastille LMG Receiver"] = "cooling fins round the barrel socket: the fitted barrel runs through them",
        ["Sawmill Receiver"] = "cooling fins round the barrel socket: the fitted barrel runs through them",
        ["Hammerfall Autocannon Receiver"] = "cooling fins round the barrel socket: the fitted barrel runs through them",
        ["Barbed Wire Wrap"] = "two wraps of wire round the hilt's handle: the hilt joins them",
        ["Nail Ring"] = "nails driven into the hilt's handle: the hilt joins them",
    };

    /// <summary>Grips whose upper receiver rows may hang free (part name and why).</summary>
    public static readonly Dictionary<string, string> AllowedFreeRows = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Push Cart"] = "receivers piled in the basket in rows 11 cm apart; only the bottom row stands on the floor",
    };

    /// <summary>Two boxes touch when they overlap or are at most this far apart (1 cm, and 0.1 mm for rounding).</summary>
    public const float ConnectTolerance = 0.0101f;

    /// <summary>Whether boxes i and j touch; a contoured shape (a tube, an arc...) only where its proxy boxes do.</summary>
    public static bool Touch(IReadOnlyList<ModelBox> boxes, ModelBox[]?[]? proxies, int i, int j, float tol) =>
        Touch(boxes[i], proxies?[i], boxes[j], proxies?[j], tol);

    public static bool Touch(ModelBox a, ModelBox[]? pa, ModelBox b, ModelBox[]? pb, float tol)
    {
        if (!a.Touches(b, tol))
            return false;
        if (pa is null)
            return pb is null || pb.Any(y => y.Touches(a, tol));
        if (pb is null)
            return pa.Any(x => x.Touches(b, tol));
        return pa.Any(x => pb.Any(y => x.Touches(y, tol)));
    }

    /// <summary>The boxes outside the main connected piece: the piece holding box <paramref name="anchor"/>, or (-1) the piece with the most volume.</summary>
    public static List<int> FloatingBoxes(IReadOnlyList<ModelBox> boxes, int anchor, ModelBox[]?[]? proxies = null, float tol = ConnectTolerance)
    {
        var n = boxes.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++)
            parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                if (Find(i) != Find(j) && Touch(boxes, proxies, i, j, tol))
                    parent[Find(i)] = Find(j);
        if (n == 0)
            return [];
        int main;
        if (anchor >= 0)
            main = Find(anchor);
        else
        {
            var volume = new Dictionary<int, float>();
            for (var i = 0; i < n; i++)
                volume[Find(i)] = volume.GetValueOrDefault(Find(i)) + boxes[i].H.X * boxes[i].H.Y * boxes[i].H.Z;
            main = volume.MaxBy(kv => kv.Value).Key;
        }
        var list = new List<int>();
        for (var i = 0; i < n; i++)
            if (Find(i) != main)
                list.Add(i);
        return list;
    }

    /// <summary>The proxy boxes of each shaped piece (null for a plain box), from its world matrix and shape id.</summary>
    public static ModelBox[]?[] Proxies(IReadOnlyList<Matrix4x4> mats, IReadOnlyList<ushort> shapes)
    {
        var r = new ModelBox[]?[mats.Count];
        for (var i = 0; i < mats.Count; i++)
            if (shapes[i] != 0)
                r[i] = PartShapes.Proxies(shapes[i]).Select(p => ModelBox.Of(p * mats[i])).ToArray();
        return r;
    }

    /// <summary>A built weapon's boxes, their proxies, and which build node owns each (the deepest whose range holds it).</summary>
    private sealed class Built
    {
        public required ModelBox[] Boxes;
        public required ModelBox[]?[] Proxies;
        public required BuildNode?[] Owner;
        public required BuildNode?[] Top;

        public static Built Of(WeaponBuild b)
        {
            var model = BuildModel.Build(b, Vector3.One);
            var boxes = model.Mesh.Matrices.Select(ModelBox.Of).ToArray();
            var proxies = Proxies(model.Mesh.Matrices, model.Mesh.Parts.Select(x => x.Shape).ToArray());
            var ranges = b.Nodes.Select(n => (Node: n, Range: BuildModel.NodeRange(model, n))).ToList();
            var owner = new BuildNode?[boxes.Length];
            var top = new BuildNode?[boxes.Length];
            for (var i = 0; i < boxes.Length; i++)
            {
                var o = ranges.Where(r => i >= r.Range.From && i < r.Range.To).OrderBy(r => r.Range.To - r.Range.From).Select(r => r.Node).FirstOrDefault();
                owner[i] = o;
                var t = o;
                while (t?.Parent != null && t.Parent != b.Root)
                    t = t.Parent;
                top[i] = t;
            }
            return new Built { Boxes = boxes, Proxies = proxies, Owner = owner, Top = top };
        }

        public IEnumerable<int> Of(PartDef p) => Enumerable.Range(0, Boxes.Length).Where(i => Owner[i]?.Part == p);
    }

    private static string Fmt(Vector3 v) => $"({v.X:0.000}, {v.Y:0.000}, {v.Z:0.000})";

    private static StreamWriter? _tty;

    private static void Say(string line)
    {
        Console.WriteLine(line);
        _tty ??= new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        _tty.WriteLine(line);
    }

    public static int Cli(string[] args)
    {
        var tol = ConnectTolerance;
        var recvName = "Workhorse Receiver";
        string? dir = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("--tol", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                tol = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.Equals("--receiver", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                recvName = args[++i];
            else if (a.Equals("--check-builder-parts", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                dir = args[++i];
        }
        if (dir != null)
        {
            dir = Path.GetFullPath(dir);
            if (!Directory.Exists(dir))
            {
                Say($"--check-builder-parts: no folder {dir}");
                return 1;
            }
            Parts.Reload(dir);
        }
        var folder = dir ?? Parts.Folder;
        foreach (var e in Parts.LoadErrors)
            Say($"load error: {e}");
        var problems = 0;

        // ---- 1. Every part on its own, drawn as on an empty grip ----
        Say($"== 1. parts on their own (tolerance {tol * 100:0.0} cm) ==");
        string Cat(PartDef p) => p.Category.ToString().ToLowerInvariant();
        var order = Parts.All.OrderBy(p => p.Category != PartCategory.Grip).ThenBy(Cat, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
        foreach (var p in order)
        {
            var alone = new BuildNode(p);
            var shown = p.Boxes.Select((b, i) => (b, i)).Where(x => BuildModel.Shown(x.b, alone, false)).ToArray();
            var mats = shown.Select(x => Matrix4x4.CreateScale(x.b.Size) * Matrix4x4.CreateFromQuaternion(x.b.Rotation) * Matrix4x4.CreateTranslation(x.b.Pos)).ToArray();
            var boxes = mats.Select(ModelBox.Of).ToArray();
            var fl = FloatingBoxes(boxes, -1, Proxies(mats, shown.Select(x => x.b.ShapeId).ToArray()), tol);
            if (fl.Count == 0)
                continue;
            var why = AllowedSeparated.GetValueOrDefault(p.Name);
            var where = p.SourceFile is { } sf ? $" ({sf.Replace('\\', '/')})" : "";
            Say($"{Cat(p),-9} {p.Name}{where}{(why != null ? $"  [allowed: {why}]" : "")}: {fl.Count} floating box(es)");
            Say("      " + (fl.Count <= 2
                ? string.Join("; ", fl.Select(i => { var b = shown[i].b; return $"box #{shown[i].i + 1} {b.Shape} at {Fmt(b.Pos)}" + (b.Tag != "" ? $" [{b.Tag}]" : ""); }))
                : $"{fl.Count} boxes"));
            if (why == null)
                problems++;
        }

        PartDef? P(string name) => Parts.Find(name);

        // Boxes of a build outside the grip's piece, allow-lists aside: "owner part at (x, y, z)".
        List<string> Floating(WeaponBuild b)
        {
            var m = Built.Of(b);
            var fl = FloatingBoxes(m.Boxes, 0, m.Proxies, tol);
            var lowest = b.Root!.Part.Sockets.Where(s => s.Type == "receiver").Select(s => s.Pos.Y).DefaultIfEmpty(0).Min();
            var bad = new List<string>();
            foreach (var i in fl)
            {
                var o = m.Owner[i];
                if (o == null || AllowedSeparated.ContainsKey(o.Part.Name))
                    continue; // an allowed part's own separated pieces (fins with no barrel through them yet)
                if (AllowedFreeRows.ContainsKey(b.Root.Part.Name) && m.Top[i]?.ParentSocket is { Type: "receiver" } rs && rs.Pos.Y > lowest + 1e-4f)
                    continue; // a free-row grip: a receiver above the bottom row (and what it carries) may hang free
                bad.Add($"{o.Part.Name} at {Fmt(m.Boxes[i].C)}");
            }
            return bad;
        }
        void Report(string title, List<string> bad)
        {
            Say($"{title}: {bad.Count} floating box(es)");
            foreach (var g in bad.GroupBy(x => x[..x.IndexOf(" at (", StringComparison.Ordinal)]))
                Say($"      {g.Key}: " + (g.Count() <= 2 ? string.Join("; ", g.Select(x => x[(g.Key.Length + 1)..])) : $"{g.Count()} boxes"));
            problems++;
        }

        // ---- 2. Root grips assembled ----
        Say($"\n== 2. assembled grips (receiver: {recvName}) ==");
        var recv = P(recvName);
        if (recv == null)
        {
            Say($"no part called '{recvName}'");
            problems++;
        }
        var fill = new Dictionary<string, PartDef?>
        {
            ["stock"] = P("Fixed Stock"), ["foregrip"] = P("Vertical Foregrip"), ["hopper"] = P("Manifold Hopper"), ["utility"] = P("Laser Sight"),
        };
        var optic = P("Red Dot");
        foreach (var g in order.Where(x => x.Category == PartCategory.Grip && x.Frame != null))
        {
            if (recv == null)
                break;
            var recvSockets = g.Sockets.Where(s => s.Type == "receiver").ToList();
            var opticSockets = optic == null ? [] : g.Sockets.Where(s => s.Type == "optic").ToList();
            var rest = g.Sockets.Where(s => fill.GetValueOrDefault(s.Type) != null).ToList();
            for (var stage = 0; stage < 5; stage++)
            {
                if (stage == 3 && opticSockets.Count == 0 || stage == 4 && rest.Count == 0)
                    continue;
                var b = new WeaponBuild("Connectivity", g);
                if (stage >= 1)
                    foreach (var s in stage == 1 ? recvSockets.Take(1) : recvSockets)
                        b.Root!.Attach(s.Name, recv);
                if (stage >= 3)
                    foreach (var s in opticSockets)
                        b.Root!.Attach(s.Name, optic!);
                if (stage >= 4)
                    foreach (var s in rest)
                        b.Root!.Attach(s.Name, fill[s.Type]!);
                var bad = Floating(b);
                if (bad.Count > 0)
                    Report($"{g.Name} [{stage switch { 0 => "empty", 1 => "one receiver", 2 => "every receiver", 3 => "every receiver + optic", _ => "everything" }}]", bad);
            }
        }

        // ---- 3. Every receiver x every action on the Rifle Grip ----
        Say("\n== 3. every receiver x every action (on the Rifle Grip) ==");
        var receivers = order.Where(p => p.Category == PartCategory.Receiver).ToList();
        var actions = order.Where(p => p.Category == PartCategory.Action).ToList();
        var rifleGrip = P("Rifle Grip");
        if (rifleGrip == null)
        {
            Say("no part called 'Rifle Grip'");
            problems++;
        }
        else
            foreach (var r in receivers)
                foreach (var a in actions)
                {
                    var b = new WeaponBuild("Connectivity", rifleGrip);
                    b.Root!.Attach("receiver_1", r).Attach("action", a);
                    var bad = Floating(b);
                    if (bad.Count > 0)
                        Report($"{a.Name} ({a.Placement ?? "side"}) on the {r.Name}", bad);
                }
        Say($"   {receivers.Count} receivers x {actions.Count} actions checked");

        // ---- 3b. Under/front actions clear the handle, foregrips and magazines ----
        Say("\n== 3b. under/front actions clear the handle, foregrips and magazines ==");
        var (cutting, checks, exempt) = ActionClearance(order);
        problems += cutting;
        Say($"   {checks} checks; {exempt} overlaps with feeds that reach back to the handle (allowed)");

        // ---- 4. Standard builds ----
        Say("\n== 4. standard builds ==");
        var errors = new List<string>();
        foreach (var sb in StandardBuilds.LoadFrom(Path.Combine(folder, "Builds"), Parts.Find, errors).OrderBy(x => x.SourceFile, StringComparer.Ordinal))
        {
            var bad = Floating(sb.Build(Parts.Find));
            if (bad.Count > 0)
                Report($"{sb.Name} ({sb.SourceFile})", bad);
        }
        foreach (var e in errors)
        {
            Say($"build error: {e}");
            problems++;
        }
        Say($"\n{problems} problem(s)");
        return problems;
    }

    /// <summary>
    /// Actions placed "under" or "front" against the handle, foregrips that hang under the gun and every magazine that leaves
    /// room in front of the handle, on the Rifle Grip and the Service Pistol Grip: (problems, checks, exempt).
    /// </summary>
    private static (int Problems, int Checks, int Exempt) ActionClearance(List<PartDef> order)
    {
        const float Tol = -0.001f;
        var receivers = order.Where(p => p.Category == PartCategory.Receiver).ToList();
        var placed = order.Where(p => p.Category == PartCategory.Action && !string.Equals(p.Placement ?? "side", "side", StringComparison.OrdinalIgnoreCase)).ToList();
        var feeds = order.Where(p => p.Category == PartCategory.Feed && string.Equals(p.Mount, "feed", StringComparison.OrdinalIgnoreCase)).ToList();
        var fores = order.Where(p => p.Category == PartCategory.Grip && string.Equals(p.Mount, "foregrip", StringComparison.OrdinalIgnoreCase)
            && p.Boxes.All(b => b.Pos.Y + b.Size.Y / 2 <= 0.001f)).ToList(); // as the self-test: unturned extents
        int problems = 0, checks = 0, exempt = 0;
        (float Min, float Max) Zr(IEnumerable<ModelBox> bs) => (bs.Min(b => b.MinZ), bs.Max(b => b.MaxZ));
        bool Cuts(Built m, List<int> a, Built n, List<int> b) => a.Any(i => b.Any(j => Touch(m.Boxes[i], m.Proxies[i], n.Boxes[j], n.Proxies[j], Tol)));
        foreach (var gname in new[] { "Rifle Grip", "Service Pistol Grip" })
        {
            if (Parts.Find(gname) is not { } g)
            {
                Say($"no part called '{gname}'");
                problems++;
                continue;
            }
            var rs = g.Sockets.First(s => s.Type == "receiver").Name;
            var fs = g.Sockets.FirstOrDefault(s => s.Type == "foregrip")?.Name;
            var empty = Built.Of(new WeaponBuild("t", g));
            var handle = Enumerable.Range(0, empty.Boxes.Length).Where(i => empty.Boxes[i].Contains(BuildModel.GripAnchor)).ToList();
            var front = Zr(handle.Select(i => empty.Boxes[i])).Min;
            var under = new List<(Built M, List<int> Ix)>();
            if (fs != null)
                foreach (var f in fores)
                {
                    var b = new WeaponBuild("t", g);
                    b.Root!.Attach(fs, f);
                    var m = Built.Of(b);
                    under.Add((m, m.Of(f).ToList()));
                }
            foreach (var r in receivers)
            {
                var bore = r.Receiver?.Bore ?? "rifle";
                var mags = new List<(string Name, Built M, List<int> Ix)>();
                foreach (var f in feeds.Where(f => f.Bores.Length == 0 || f.Bores.Contains(bore, StringComparer.OrdinalIgnoreCase)))
                {
                    var b = new WeaponBuild("t", g);
                    b.Root!.Attach(rs, r).Attach("feed", f);
                    var m = Built.Of(b);
                    mags.Add((f.Name, m, m.Of(f).ToList()));
                }
                foreach (var a in placed)
                {
                    var b = new WeaponBuild("t", g);
                    b.Root!.Attach(rs, r).Attach("action", a);
                    var m = Built.Of(b);
                    var ab = m.Of(a).ToList();
                    checks++;
                    if (ab.Count == 0)
                        continue;
                    var (lo, hi) = Zr(ab.Select(i => m.Boxes[i]));
                    var length = hi - lo;
                    var hits = new List<string>();
                    if (Cuts(m, ab, empty, handle))
                        hits.Add("handle");
                    if (under.Any(u => Cuts(m, ab, u.M, u.Ix)))
                        hits.Add("foregrip");
                    foreach (var (fn, fm, fb) in mags)
                    {
                        if (fb.Count == 0 || !Cuts(m, ab, fm, fb))
                            continue;
                        if (front - Zr(fb.Select(i => fm.Boxes[i])).Max < length + 0.001f)
                            exempt++; // reaches back to the handle: no room for a trigger group in front of it
                        else
                            hits.Add(fn);
                    }
                    if (hits.Count > 0)
                    {
                        Say($"{a.Name} ({a.Placement}) on the {r.Name} [{gname}] cuts into: {string.Join(", ", hits)}");
                        problems++;
                    }
                }
            }
        }
        return (problems, checks, exempt);
    }
}
