using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CallOfSlop.PartKit;

/// <summary>
/// The prop tools that were tools/props/zfight_props.py and zfight_moves.py (CLAUDE.md "No coplanar faces"):
/// <c>--zfight-props FILE...</c> corrects coplanar faces in prop files in place by the smallest amount (<see cref="PartZFight.SettleProps"/>),
/// rewriting only the "size" and "offset" of the parts that move so the file's layout stays; <c>--zfight-moves OLD_REV [NEW_REV]
/// [--paths A B ...] [--files LIST] [--worst N] [--csv FILE]</c> measures how far the boxes of props, construct parts and weapon parts
/// moved between two git versions (the working tree when NEW_REV is left out). Both print what the Python printed, byte for byte.
/// </summary>
public static class PropTools
{
    private static StreamWriter? _tty;

    private static void Say(string line)
    {
        Console.WriteLine(line);
        _tty ??= new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        _tty.WriteLine(line);
    }

    private static readonly UTF8Encoding Utf8 = new(false);

    // ---- --zfight-props ----------------------------------------------------------------------------------------------

    public static int ZFightProps(string[] args)
    {
        var files = args.SkipWhile(a => !a.Equals("--zfight-props", StringComparison.OrdinalIgnoreCase)).Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
        int models = 0, boxes = 0, left = 0;
        foreach (var f in files)
        {
            var text = Utf8.GetString(File.ReadAllBytes(f));
            var (fresh, changed, rest) = SettleText(text);
            left += rest;
            if (rest != 0)
                Say($"{f}: {rest} hit(s) left");
            if (fresh != text)
            {
                models++;
                boxes += changed;
                File.WriteAllBytes(f, Utf8.GetBytes(fresh));
            }
        }
        Say($"{models} file(s), {boxes} part(s) corrected, {left} hit(s) left");
        return left != 0 ? 1 : 0;
    }

    /// <summary>The file's text with its coplanar faces corrected, and (parts changed, hits left).</summary>
    public static (string Text, int Changed, int Left) SettleText(string text)
    {
        if (PyJson.Loads(text) is not PyDict d || d.Get("parts") is not List<object?> parts)
            return (text, 0, 0);
        // A deep copy (the Python's dict(p) copies, and zfight puts new lists in them).
        var drawn = ((List<object?>)PyJson.Loads(PyJson.Dumps(parts))!).Cast<PyDict>().ToList();
        var before = PyJson.Dumps(drawn);
        var (changed, left) = PartZFight.SettleProps(drawn);
        if (PyJson.Dumps(drawn) == before)
            return (text, 0, left);
        var spans = PartSpans(text);
        var edits = new List<(int S, int E, string New)>();
        for (var i = 0; i < Math.Min(parts.Count, Math.Min(drawn.Count, spans.Count)); i++)
        {
            var old = (PyDict)parts[i]!;
            foreach (var key in new[] { "size", "offset" })
                if (!PyEqual(old.Get(key), drawn[i].Get(key)))
                    edits.Add((spans[i][key].S, spans[i][key].E, Vec((List<object?>)drawn[i][key]!)));
        }
        foreach (var (s, e, n) in edits.OrderByDescending(x => x.S).ThenByDescending(x => x.E).ThenByDescending(x => x.New, StringComparer.Ordinal))
            text = text[..s] + n + text[e..];
        return (text, changed, left);
    }

    /// <summary>Python's == on JSON values: 1 == 1.0, lists by element.</summary>
    public static bool PyEqual(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (List<object?> x, List<object?> y) => x.Count == y.Count && x.Zip(y).All(p => PyEqual(p.First, p.Second)),
        (int or long or double, int or long or double) => Py.D(a) == Py.D(b),
        (string x, string y) => x == y,
        (bool x, bool y) => x == y,
        _ => false,
    };

    /// <summary>The Python's vec(): ints as ints, floats rounded to 5 places.</summary>
    private static string Vec(List<object?> v) =>
        "[" + string.Join(", ", v.Select(x => x is long or int ? Convert.ToInt64(x).ToString(CultureInfo.InvariantCulture) : Py.Repr(Py.Round(Py.D(x), 5)))) + "]";

    private static int SkipWs(string t, int i)
    {
        while (t[i] is ' ' or '\t' or '\r' or '\n')
            i++;
        return i;
    }

    private static int SkipString(string t, int i)
    {
        i++;
        while (t[i] != '"')
            i += t[i] == '\\' ? 2 : 1;
        return i + 1;
    }

    private static int SkipValue(string t, int i)
    {
        i = SkipWs(t, i);
        if (t[i] == '"')
            return SkipString(t, i);
        if (t[i] is '[' or '{')
        {
            var depth = 0;
            while (true)
            {
                if (t[i] == '"')
                {
                    i = SkipString(t, i);
                    continue;
                }
                if (t[i] is '[' or '{')
                    depth++;
                else if (t[i] is ']' or '}')
                {
                    depth--;
                    if (depth == 0)
                        return i + 1;
                }
                i++;
            }
        }
        while (t[i] is not (',' or ']' or '}' or ' ' or '\t' or '\r' or '\n'))
            i++;
        return i;
    }

    /// <summary>The keys of the object at i as key: (value start, value end), and the index after it.</summary>
    private static (Dictionary<string, (int S, int E)> Keys, int Next) ObjectKeys(string t, int i)
    {
        var keys = new Dictionary<string, (int S, int E)>(StringComparer.Ordinal);
        i = SkipWs(t, i) + 1;
        while (true)
        {
            i = SkipWs(t, i);
            if (t[i] == '}')
                return (keys, i + 1);
            if (t[i] == ',')
            {
                i++;
                continue;
            }
            var e = SkipString(t, i);
            var key = (string)PyJson.Loads(t[i..e])!;
            i = SkipWs(t, e) + 1; // past the colon
            var s = SkipWs(t, i);
            i = SkipValue(t, s);
            keys[key] = (s, i);
        }
    }

    /// <summary>Where each part's keys sit in the file's text.</summary>
    private static List<Dictionary<string, (int S, int E)>> PartSpans(string text, string listKey = "parts")
    {
        var (top, _) = ObjectKeys(text, SkipWs(text, 0));
        var (s, _) = top[listKey];
        var list = new List<Dictionary<string, (int S, int E)>>();
        var i = s + 1;
        while (true)
        {
            i = SkipWs(text, i);
            if (text[i] == ']')
                return list;
            if (text[i] == ',')
            {
                i++;
                continue;
            }
            (var keys, i) = ObjectKeys(text, i);
            list.Add(keys);
        }
    }

    // ---- --zfight-moves ----------------------------------------------------------------------------------------------

    private static readonly double[] Bins = [0, 0.5, 2, 5, 10, 25, 50, 100, 1e9];
    private static readonly string[] Labels = ["0", "<=0.5", "<=2", "<=5", "<=10", "<=25", "<=50", "<=100", ">100"];

    private static (int Code, string Out) Git(params string[] a)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = Utf8 };
        foreach (var x in a)
            psi.ArgumentList.Add(x);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout);
    }

    private static object? Load(string? rev, string f)
    {
        string text;
        if (rev is null)
        {
            try
            {
                text = File.ReadAllText(f, Encoding.UTF8);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
        else
        {
            var (code, outText) = Git("show", $"{rev}:{f}");
            if (code != 0)
                return null;
            text = outText;
        }
        try
        {
            return PyJson.Loads(text);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private sealed record BoxOf(List<object?> C, List<object?> S, List<object?> Rot);

    private static object Sub(object? a, object? b) => a is long or int && b is long or int ? Convert.ToInt64(a) - Convert.ToInt64(b) : Py.D(a) - Py.D(b);

    /// <summary>(centre, size, rotation) of a prop part, construct box or weapon part box, or null.</summary>
    private static BoxOf? Box(PyDict b)
    {
        if (b.Has("use"))
            return null;
        List<object?> c, s;
        if (b.Has("min"))
        {
            var lo = (List<object?>)b["min"]!;
            var hi = (List<object?>)b["max"]!;
            c = [.. Enumerable.Range(0, 3).Select(k => (object?)((Py.D(lo[k]) + Py.D(hi[k])) / 2))];
            s = [.. Enumerable.Range(0, 3).Select(k => (object?)Sub(hi[k], lo[k]))];
        }
        else if (b.Has("size"))
        {
            s = (List<object?>)b["size"]!;
            c = (List<object?>)(b.Get("offset") ?? b.Get("at") ?? b.Get("pos") ?? Py.L(0L, 0L, 0L));
        }
        else
            return null;
        if (s.Count != 3 || c.Count != 3)
            return null;
        var rot = (List<object?>)(b.Has("rotation") ? b["rotation"]! : b.Has("rot") ? b["rot"]! : Py.L(0L, 0L, 0L));
        return new BoxOf(c, s, rot);
    }

    private static List<(int I, BoxOf? B)>? BoxesOf(object? v)
    {
        if (v is not PyDict d)
            return null;
        var fmt = d.Get("format") as string ?? "";
        object? lst;
        if (fmt.StartsWith("call-of-slop-prop", StringComparison.Ordinal))
            lst = d.Get("parts");
        else if (fmt.StartsWith("call-of-slop-construct-part", StringComparison.Ordinal) || fmt.StartsWith("call-of-slop-part", StringComparison.Ordinal))
            lst = Py.Truthy(d.Get("boxes")) ? d.Get("boxes") : d.Get("model");
        else
            return null;
        if (lst is not List<object?> list)
            return null;
        var r = new List<(int, BoxOf?)>();
        for (var i = 0; i < list.Count; i++)
            if (list[i] is PyDict b)
                r.Add((i, Box(b)));
        return r;
    }

    private static double[][] RotMatrix(List<object?> rot)
    {
        double p = Py.D(rot[0]) * Math.PI / 180, y = Py.D(rot[1]) * Math.PI / 180, r = Py.D(rot[2]) * Math.PI / 180;
        double cp = Math.Cos(p), sp = Math.Sin(p), cy = Math.Cos(y), sy = Math.Sin(y), cr = Math.Cos(r), sr = Math.Sin(r);
        double[][] ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]];
        double[][] rx = [[1, 0, 0], [0, cp, -sp], [0, sp, cp]];
        double[][] rz = [[cr, -sr, 0], [sr, cr, 0], [0, 0, 1]];
        static double[][] Mul(double[][] a, double[][] b) =>
            [.. Enumerable.Range(0, 3).Select(i => Enumerable.Range(0, 3).Select(j => Py.Sum(Enumerable.Range(0, 3).Select(k => a[i][k] * b[k][j]))).ToArray())];
        return Mul(Mul(ry, rx), rz);
    }

    private static (List<double> Moves, double Share) FaceMoves(BoxOf a, BoxOf b)
    {
        var m = RotMatrix(a.Rot);
        var d = Enumerable.Range(0, 3).Select(k => Py.D(b.C[k]) - Py.D(a.C[k])).ToArray();
        var moves = new List<double>();
        var share = 0.0;
        for (var k = 0; k < 3; k++)
        {
            double[] axis = [m[0][k], m[1][k], m[2][k]];
            var along = Py.Sum(Enumerable.Range(0, 3).Select(i => axis[i] * d[i]));
            var ds = (Py.D(b.S[k]) - Py.D(a.S[k])) / 2;
            moves.Add((along + ds) * 1000);
            moves.Add((-along + ds) * 1000);
            share = Math.Max(share, Math.Abs(Py.D(b.S[k]) - Py.D(a.S[k])) / Math.Max(Math.Abs(Py.D(a.S[k])), 1e-6));
        }
        return (moves, share);
    }

    private static List<((int I, BoxOf? B)? O, (int I, BoxOf? B)? N)> Align(List<(int I, BoxOf? B)> old, List<(int I, BoxOf? B)> fresh)
    {
        var pairs = new List<((int, BoxOf?)?, (int, BoxOf?)?)>();
        if (old.Count == fresh.Count)
        {
            for (var i = 0; i < old.Count; i++)
                pairs.Add((old[i], fresh[i]));
            return pairs;
        }
        var used = new HashSet<int>();
        foreach (var o in old)
        {
            (int I, BoxOf? B)? best = null;
            var score = 1e9;
            if (o.B is { } ob)
                foreach (var n in fresh)
                {
                    if (used.Contains(n.I) || n.B is not { } nb)
                        continue;
                    var s = Py.Sum(Enumerable.Range(0, 3).Select(k => Math.Abs(Py.D(ob.C[k]) - Py.D(nb.C[k])) + Math.Abs(Py.D(ob.S[k]) - Py.D(nb.S[k]))));
                    if (s < score)
                    {
                        best = n;
                        score = s;
                    }
                }
            if (best is { } bb && score < 0.25)
            {
                used.Add(bb.I);
                pairs.Add((o, bb));
            }
            else
                pairs.Add((o, null));
        }
        foreach (var n in fresh.Where(n => !used.Contains(n.I)))
            pairs.Add((null, n));
        return pairs;
    }

    private static int BinOf(double mm)
    {
        for (var i = 0; i < Bins.Length; i++)
            if (mm <= Bins[i] + 1e-6)
                return i;
        return Bins.Length - 1;
    }

    /// <summary>Python's repr of a JSON list (ints as ints, floats by repr).</summary>
    private static string ReprList(List<object?> v) =>
        "[" + string.Join(", ", v.Select(x => x is long or int ? Convert.ToInt64(x).ToString(CultureInfo.InvariantCulture) : Py.Repr(Py.D(x)))) + "]";

    /// <summary>f"{x:.Nf}" (the exact value rounded half to even, as Python formats).</summary>
    private static string F(double x, int n) => Py.Round(x, n).ToString("F" + n, CultureInfo.InvariantCulture);

    public static int ZFightMoves(string[] args)
    {
        var rest = args.SkipWhile(a => !a.Equals("--zfight-moves", StringComparison.OrdinalIgnoreCase)).Skip(1).ToList();
        string? oldRev = null, newRev = null, filesFrom = null, csv = null;
        var worst = 25;
        List<string> paths = ["Props", "Maps", "Constructs/Parts", "Parts"];
        for (var i = 0; i < rest.Count; i++)
        {
            var a = rest[i];
            if (a == "--paths")
            {
                paths = [];
                while (i + 1 < rest.Count && !rest[i + 1].StartsWith("--"))
                    paths.Add(rest[++i]);
            }
            else if (a == "--files" && i + 1 < rest.Count)
                filesFrom = rest[++i];
            else if (a == "--worst" && i + 1 < rest.Count)
                worst = int.Parse(rest[++i], CultureInfo.InvariantCulture);
            else if (a == "--csv" && i + 1 < rest.Count)
                csv = rest[++i];
            else if (a.StartsWith("--"))
                break; // another command's options
            else if (oldRev is null)
                oldRev = a;
            else
                newRev ??= a;
        }
        if (oldRev is null)
        {
            Say("--zfight-moves OLD_REV [NEW_REV] [--paths A B ...] [--files LIST] [--worst N] [--csv FILE]");
            return 2;
        }
        var diffArgs = new List<string> { "diff", "--name-only", oldRev };
        if (newRev != null)
            diffArgs.Add(newRev);
        diffArgs.Add("--");
        diffArgs.AddRange(paths);
        var files = Git([.. diffArgs]).Out.Split('\n').Where(f => f.EndsWith(".json", StringComparison.Ordinal)).ToList();
        if (filesFrom != null)
            files = [.. File.ReadAllLines(filesFrom, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0)];
        var hist = new Dictionary<(string, int), int>();
        var moved = new List<(double Top, double Share, string F, int I, string What)>();
        int models = 0, dropped = 0, added = 0, rotated = 0, reshaped = 0, same = 0;
        var shares = new int[4];
        foreach (var f in files)
        {
            var ba = BoxesOf(Load(oldRev, f));
            var bb = BoxesOf(Load(newRev, f));
            if (ba is null || bb is null)
                continue;
            var kind = f.StartsWith("Constructs", StringComparison.Ordinal) ? "construct" : f.StartsWith("Parts", StringComparison.Ordinal) ? "part" : "prop";
            var touched = false;
            foreach (var (o, n) in Align(ba, bb))
            {
                if (o is null)
                {
                    added++;
                    touched = true;
                    continue;
                }
                if (n is null)
                {
                    dropped++;
                    touched = true;
                    moved.Add((1e9, 1, f, o.Value.I, "dropped"));
                    continue;
                }
                if (o.Value.B is not { } ob || n.Value.B is not { } nb)
                    continue;
                if (PyEqual(ob.C, nb.C) && PyEqual(ob.S, nb.S) && PyEqual(ob.Rot, nb.Rot))
                {
                    same++;
                    continue;
                }
                touched = true;
                if (Enumerable.Range(0, 3).Any(k => Math.Abs(Py.D(ob.Rot[k]) - Py.D(nb.Rot[k])) > 1e-6))
                {
                    rotated++;
                    moved.Add((1e8, 1, f, o.Value.I, $"rotation {ReprList(ob.Rot)} -> {ReprList(nb.Rot)}"));
                }
                var (moves, share) = FaceMoves(ob, nb);
                var top = moves.Max(Math.Abs);
                var key = (kind, BinOf(top));
                hist[key] = hist.GetValueOrDefault(key) + 1;
                shares[share <= 0.02 ? 0 : share <= 0.1 ? 1 : share <= 0.25 ? 2 : 3]++;
                if (share > 0.5)
                    reshaped++;
                moved.Add((top, share, f, o.Value.I, $"size {ReprList(ob.S)} -> {ReprList(nb.S)} at {ReprList(ob.C)} -> {ReprList(nb.C)} rot {ReprList(ob.Rot)}"));
            }
            if (touched)
                models++;
        }
        Say($"{models} models changed, {same} boxes unchanged, {moved.Count} boxes moved, {added} added, {dropped} dropped, {rotated} rotated");
        Say("worst face move per box (mm):   " + string.Join(" ", Labels.Select(l => l.PadLeft(7))));
        foreach (var kind in new[] { "prop", "construct", "part" })
            Say($"  {kind,-28}  " + string.Join(" ", Enumerable.Range(0, Labels.Length).Select(i => hist.GetValueOrDefault((kind, i)).ToString(CultureInfo.InvariantCulture).PadLeft(7))));
        Say($"size change as a share of that dimension: <=2% {shares[0]}, <=10% {shares[1]}, <=25% {shares[2]}, >25% {shares[3]} (>50%: {reshaped})");
        var sorted = moved.OrderByDescending(m => m.Top).ThenByDescending(m => m.Share).ToList();
        foreach (var (top, share, f, i, what) in sorted.Take(worst))
            Say($"  {F(top, 1).PadLeft(9)} mm {F(share * 100, 0).PadLeft(5)}%  {f} #{i}: {what}");
        if (csv != null)
        {
            var sb = new StringBuilder();
            foreach (var (top, share, f, i, what) in sorted)
                sb.Append($"{F(top, 2)}\t{F(share, 3)}\t{f}\t{i}\t{what}\n");
            File.WriteAllBytes(csv, Utf8.GetBytes(sb.ToString()));
        }
        return 0;
    }
}
