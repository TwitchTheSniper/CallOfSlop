namespace CallOfSlop.PartKit;

/// <summary>
/// The Sandbox's part size classes and what every part generator shares: the port of the deleted tools/constructs/scale.py
/// (docs/sandbox/PARTKIT.md; the classes themselves are in docs/sandbox/CONSTRUCTS.md, "Size classes").
///
///     class    scale  cell   plate (kg/m2)  a frame member  what it builds
///     small    x1     0.25   40             0.25 to 2 m     a buggy, a plane
///     medium   x4     1      80             1 to 8 m        a truck, a tank, a bomber
///     large    x16    4      200            4 to 32 m       a hangar, a big walker, a frigate
///     capital  x64    16     500            16 to 128 m     a carrier's hull
///
/// A part is made of cells of its class, its origin the centre of one cell, every socket on a cell face. A socket snaps
/// only to one of its own class. At most 256 boxes, 64 sockets here (the game allows 255), a name of 32 characters.
/// </summary>
public sealed class Cls(string key, string name, int x, double cell, int kgM2, double plate, double seconds, int healthM2, int armour)
{
    public readonly string Key = key; // the "sizeClass" / socket "class" value
    public readonly string Name = name; // the inventory group
    public readonly int X = x;
    public readonly double Cell = cell;
    public readonly double Half = cell / 2;
    public readonly int KgM2 = kgM2;
    public readonly double Plate = plate;
    public readonly double Seconds = seconds;
    public readonly int HealthM2 = healthM2;
    public readonly int Armour = armour;

    /// <summary>Cells in a length (it must be a whole number of them).</summary>
    public int N(double metres)
    {
        var k = metres / Cell;
        if (Math.Abs(k - Math.Round(k)) >= 1e-6)
            throw new InvalidOperationException($"{Key}: {metres} m is not whole cells");
        return (int)Py.RoundInt(k);
    }

    public double Lo() => -Half;
    public double Hi(int n) => n * Cell - Half;
}

/// <summary>One generated part file: p = new Part(...); p.Add(Box(...)); p.Sock(...); p.Stats(...). The port of scale.Part.</summary>
public sealed class Part
{
    public readonly Cls Cls;
    public readonly PyDict D;
    public string Material;
    public string Color;
    public readonly List<PyDict> Boxes = [];
    public readonly List<PyDict> Sockets = [];
    private readonly HashSet<string> _names = [];

    public Part(string name, Cls cls, string section, string desc, string role = "frame", string tool = "welder", string material = "steel", string color = "#7c838c", string? group = null)
    {
        Cls = cls;
        D = new PyDict
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = desc,
            ["group"] = group ?? cls.Name,
            ["section"] = section,
            ["role"] = role,
            ["tool"] = tool,
        };
        if (cls.Key != "small")
            D["sizeClass"] = cls.Key;
        Material = material;
        Color = color;
    }

    public Part Add(PyDict? b)
    {
        if (b is not null)
            Boxes.Add(b);
        return this;
    }

    /// <summary>A socket; cls: another class than the part's (an adapter's other side).</summary>
    public void Sock(string name, double[] at, string dir, Cls? cls = null, string? kind = null)
    {
        if (!_names.Add(name))
            throw new InvalidOperationException($"socket {name} twice");
        var s = new PyDict { ["name"] = name, ["at"] = PartKitMath.Vec(at), ["dir"] = dir };
        if (cls is not null && cls.Key != Cls.Key)
            s["class"] = cls.Key;
        if (!string.IsNullOrEmpty(kind))
            s["kind"] = kind;
        Sockets.Add(s);
    }

    /// <summary>mass, health, armour and seconds as scale.Part.stats: armour and seconds keep their Python type (int or float).</summary>
    public Part Stats(double mass, double health, object armour, object? seconds = null)
    {
        D["mass"] = PartKitMath.R(Math.Max(0.5, Py.Round(mass, 1)));
        D["health"] = (int)Py.RoundInt(health);
        D["armour"] = armour;
        D["material"] = Material;
        D["color"] = Color;
        D["attachSeconds"] = seconds ?? Cls.Seconds;
        return this;
    }

    /// <summary>Any other key of the part file, in order (Part.set(**keys)).</summary>
    public Part Set(params (string Key, object? Value)[] keys)
    {
        D.Update(keys);
        return this;
    }

    public PyDict Json()
    {
        var name = (string)D["name"]!;
        if (Boxes.Count is 0 or > 256 || Sockets.Count is 0 or > 64 || name.Length > 32)
            throw new InvalidOperationException($"{name}: {Boxes.Count} boxes, {Sockets.Count} sockets");
        PartZFight.Resolve(Boxes, Sockets); // no two drawn faces on one plane (CLAUDE.md, "No coplanar faces")
        var d = new PyDict(D)
        {
            ["boxes"] = Boxes.Cast<object?>().ToList(),
            ["sockets"] = Sockets.Cast<object?>().ToList(),
        };
        return d;
    }
}

/// <summary>scale.py's free functions.</summary>
public static class PartKitMath
{
    public static readonly Cls Small = new("small", "", 1, 0.25, 40, 0.05, 2.5, 120, 20);
    public static readonly Cls Medium = new("medium", "Medium", 4, 1.0, 80, 0.1, 4.0, 180, 24);
    public static readonly Cls Large = new("large", "Large", 16, 4.0, 200, 0.2, 8.0, 240, 32);
    public static readonly Cls Capital = new("capital", "Capital", 64, 16.0, 500, 0.5, 15.0, 300, 40);
    public static readonly Cls[] Classes = [Small, Medium, Large, Capital];

    /// <summary>A small-class length at class c.</summary>
    public static double Scaled(double v, Cls c) => v * c.X;

    /// <summary>Metres to 4 decimals, never -0.0 (scale.r).</summary>
    public static double R(double v)
    {
        v = Py.Round(v + 0.0, 4);
        return v == 0 ? 0.0 : v;
    }

    public static List<object?> Vec(params double[] v) => [.. v.Select(x => (object?)R(x))];

    public static double[] V(params double[] v) => v;

    /// <summary>Keyword arguments as a dict, in the order given (a box's **shape, a part's set(**keys) value).</summary>
    public static PyDict Kw(params (string Key, object? Value)[] kv) => new PyDict().Update(kv);

    /// <summary>A length for a part's name: 16, 0.5 (f"{v:g}").</summary>
    public static string Fmt(double v) => Py.G(v);

    /// <summary>The depth a thin detail box is grown to: the game refuses a box under 1 cm, and 16 mm stays over it after the z-fight corrector trims a box by up to 5 mm.</summary>
    public const double DetailMin = 0.016;

    /// <summary>A box from its corners. shape: the other keys, in order. The game refuses a box thinner than 1 cm, so a thinner detail box
    /// (a clock's face, a sign's letters, a seam line) is grown about its middle to <see cref="DetailMin"/> on each thin side, rather than
    /// lost; a thinner solid box, or one under 2 mm (a sliver a generator's sums left), is null and Part.Add skips it.</summary>
    public static PyDict? Box(double[] mn, double[] mx, string? color = null, bool detail = false, bool hidden = false, bool glass = false, PyDict? shape = null)
    {
        var thin = Math.Min(mx[0] - mn[0], Math.Min(mx[1] - mn[1], mx[2] - mn[2]));
        if (thin < 0.0099)
        {
            if (!detail || thin < 0.002)
                return null;
            mn = [.. mn];
            mx = [.. mx];
            for (var k = 0; k < 3; k++)
                if (mx[k] - mn[k] < DetailMin)
                {
                    var mid = (mn[k] + mx[k]) / 2;
                    mn[k] = mid - DetailMin / 2;
                    mx[k] = mid + DetailMin / 2;
                }
        }
        var b = new PyDict { ["min"] = Vec(mn), ["max"] = Vec(mx) };
        if (!string.IsNullOrEmpty(color))
            b["color"] = color;
        if (detail)
            b["detail"] = true;
        if (hidden)
            b["hidden"] = true;
        if (glass)
            b["glass"] = true;
        if (shape is not null)
            b.Update(shape);
        return b;
    }

    /// <summary>Up to m of the indices 0..n-1, evenly spread, both ends kept.</summary>
    public static List<int> Pick(int n, int m)
    {
        if (n <= m)
            return [.. Enumerable.Range(0, n)];
        if (m == 1)
            return [n / 2];
        return [.. Enumerable.Range(0, m).Select(i => (int)Py.RoundInt(i * (n - 1) / (double)(m - 1))).Distinct().Order()];
    }

    /// <summary>Sockets on the faces of an nx x ny x nz block of the part's cells (scale.block_sockets).</summary>
    public static void BlockSockets(Part p, int nx, int ny, int nz, string faces = "xyz", Dictionary<string, (int, int)>? limit = null, string[]? skip = null, int step = 1)
    {
        var c = p.Cls;
        var lim = limit ?? [];
        int[] dims = [nx, ny, nz];
        for (var axis = 0; axis < 3; axis++)
        {
            var a = "xyz"[axis];
            if (!faces.Contains(a))
                continue;
            var uv = Enumerable.Range(0, 3).Where(k => k != axis).ToArray();
            int u = uv[0], v = uv[1];
            foreach (var (sign, sname) in new[] { (-1, "-"), (1, "+") })
            {
                var d = sname + a;
                if (skip is not null && skip.Contains(d))
                    continue;
                var l = lim.TryGetValue(d, out var lv) ? lv : (4, 4);
                foreach (var i in Pick(dims[u], l.Item1))
                    foreach (var j in Pick(dims[v], l.Item2))
                    {
                        if ((i % step != 0 || j % step != 0) && step > 1)
                            continue;
                        var at = new double[3];
                        at[axis] = sign > 0 ? c.Hi(dims[axis]) : c.Lo();
                        at[u] = i * c.Cell;
                        at[v] = j * c.Cell;
                        var nm = dims[u] * dims[v] == 1 ? d : $"{d}.{i}.{j}";
                        p.Sock(nm, at, d);
                    }
            }
        }
    }

    /// <summary>A part's file name from its name (scale.file_name).</summary>
    public static string FileName(string name)
    {
        var s = name.Replace(".", "p").Replace(":", "to").Replace("-", "");
        return new string([.. s.Where(ch => char.IsLetterOrDigit(ch) && ch < 128 || PyIsAlnum(ch))]) + ".json";
    }

    private static bool PyIsAlnum(char ch) => ch >= 128 && char.IsLetterOrDigit(ch);

    /// <summary>
    /// scale.write_parts: [(folder, Part)...] under the folder out, which the generator owns whole (anything else in it is stale).
    /// The text is json.dumps(d, indent=2) and a newline.
    /// </summary>
    public static void WriteParts(PartOutput o, string outDir, IEnumerable<(string Folder, Part P)> parts)
    {
        o.Own(outDir);
        var names = new HashSet<string>();
        foreach (var (folder, p) in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (!names.Add(name))
                throw new InvalidOperationException($"part {name} twice");
            var path = folder.Length > 0 ? $"{outDir}/{folder}/{FileName(name)}" : $"{outDir}/{FileName(name)}";
            o.File(path, PyJson.Dumps(d, 2) + "\n");
        }
    }
}
