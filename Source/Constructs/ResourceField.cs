using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>One kind of raw resource (<c>Constructs/Resources.json</c>): how its deposits scatter and what they give.</summary>
public sealed class ResourceKind
{
    public string Name = "", Material = "";
    public bool Surface; // surface: trees a Saw Module cuts; else underground: a Drill Module taps it
    public float Cell = 250, Chance = 0.5f, RadiusMin = 50, RadiusMax = 100, AspectMin = 0.5f, AspectMax = 1f;
    public int Units = 1000; // a tree's wood, or an underground deposit's whole amount
}

/// <summary>The table of resource kinds. A bad file gives no kinds with <see cref="Problem"/> (a world without deposits).</summary>
public sealed class ResourceTable
{
    public const string Format = "call-of-slop-resources/1";
    public const int MaxKinds = 16;

    public readonly List<ResourceKind> Kinds = new();
    public float MinWorldHalf = 150; // a smaller world generates nothing (the Construct Yard); its map may still place deposits
    public string Problem = "";

    private static ResourceTable? _current;

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Resources.json");

    /// <summary>The table in use (loaded once).</summary>
    public static ResourceTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (the self-test; null: load the file again).</summary>
    public static void Use(ResourceTable? t) => _current = t;

    public int IndexOf(string name) => Kinds.FindIndex(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static ResourceTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ResourceTable { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    public static ResourceTable Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            var t = new ResourceTable();
            if (root.TryGetProperty("minWorldHalf", out var mh))
                t.MinWorldHalf = mh.GetSingle();
            if (!root.TryGetProperty("kinds", out var kinds) || kinds.ValueKind != JsonValueKind.Array || kinds.GetArrayLength() is 0 or > MaxKinds)
                throw new FormatException($"\"kinds\" lists 1 to {MaxKinds} resource kinds");
            foreach (var e in kinds.EnumerateArray())
                t.Kinds.Add(ReadKind(e));
            if (t.Kinds.Select(k => k.Name.ToLowerInvariant()).Distinct().Count() != t.Kinds.Count)
                throw new FormatException("a resource kind's name is used twice");
            return t;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new ResourceTable { Problem = ex.Message };
        }
    }

    private static ResourceKind ReadKind(JsonElement e)
    {
        var k = new ResourceKind
        {
            Name = e.GetProperty("name").GetString() ?? "",
            Material = e.GetProperty("material").GetString() ?? "",
            Cell = e.GetProperty("cell").GetSingle(),
            Chance = e.GetProperty("chance").GetSingle(),
            Units = e.GetProperty("units").GetInt32(),
        };
        k.Surface = e.GetProperty("layer").GetString() switch
        {
            "surface" => true,
            "underground" => false,
            var l => throw new FormatException($"{k.Name}: \"layer\" is \"surface\" or \"underground\", not \"{l}\""),
        };
        var r = e.GetProperty("radius");
        var a = e.GetProperty("aspect");
        (k.RadiusMin, k.RadiusMax) = (r[0].GetSingle(), r[1].GetSingle());
        (k.AspectMin, k.AspectMax) = (a[0].GetSingle(), a[1].GetSingle());
        if (k.Name.Length == 0 || RecyclingTable.Current.IdOf(k.Material) == 0)
            throw new FormatException($"resource kind \"{k.Name}\": \"{k.Material}\" isn't a bulk material");
        if (k.Cell < 32 || k.Chance is < 0 or > 1 || k.RadiusMin < 4 || k.RadiusMax < k.RadiusMin || k.AspectMin <= 0 || k.AspectMax > 1 || k.AspectMax < k.AspectMin || k.Units < 1)
            throw new FormatException($"resource kind \"{k.Name}\": cell is at least 32, chance 0 to 1, radius at least 4 (low, high), aspect above 0 up to 1 (low, high), units at least 1");
        return k;
    }
}

/// <summary>
/// A world's raw resources (decision 14; docs/sandbox/CONSTRUCTS.md "Gathering"): plentiful but grouped by type. Deposits
/// are generated from the map's name (or its "resources" seed) and the table, so every peer holds the same field and
/// no map file has to change; a map's sandbox section may add deposits or turn generation off. A forest grows trees on
/// a fixed lattice, so a tree is known by its lattice cell (<see cref="TreeKey"/>) and only felled ones are stored or
/// sent. The server cuts and drills; a client hears felled trees (<see cref="Msg.ResourceFell"/>). Nothing here
/// allocates per tick except a part-cut tree's entry.
/// </summary>
public sealed class ResourceField
{
    public const float TreeSpacing = 6f; // the forest lattice (metres)
    public const float TreeKeep = 0.8f; // the share of lattice cells in a forest that hold a tree
    public const float EdgeThin = 0.04f; // strength below which the edge of a deposit is bare
    public const float DrawRadius = 800f, DrawMove = 150f;
    public const int MaxDrawnTrees = 30000;
    public const float SawHeight = 6f; // how far from the ground a saw's work box still reaches a trunk

    private struct Deposit
    {
        public int Kind, Left;
        public float X, Z, Rx, Rz, Cos, Sin, Phase;
    }

    private readonly Map _map;
    private readonly ResourceTable _table;
    private readonly uint _seed;
    private readonly List<Deposit> _deposits = new();
    private readonly int[][] _grid; // per kind: a cell's deposit index, or -1
    private readonly int[] _nx, _nz;
    private readonly List<int> _placed = new(); // deposits the map placed (found by a plain scan: there are few)
    private readonly HashSet<uint> _blocked = new(); // lattice cells under the map's solid boxes
    private readonly HashSet<uint> _felled = new();
    private readonly Dictionary<uint, int> _cut = new(); // a tree part cut: the wood taken so far (server)
    private readonly List<FoliageInstance> _drawn = new();
    private Vector3 _drawnAt = new(float.MaxValue);

    /// <summary>Goes up whenever the drawn trees change (the renderer rebuilds only then).</summary>
    public int DrawVersion { get; private set; }

    public IReadOnlyList<FoliageInstance> Drawn => _drawn;
    public IReadOnlyCollection<uint> Felled => _felled;
    public int DepositCount => _deposits.Count;
    public ResourceTable Table => _table;

    /// <summary>The ground under a point.</summary>
    public float Ground(float x, float z) => _map.GroundHeight(x, z);

    /// <summary>What trees give (the first surface kind's material).</summary>
    public string WoodMaterial => _table.Kinds.Find(k => k.Surface)?.Material ?? "";

    public ResourceField(Map map, ResourceTable table, SandboxResources? rules)
    {
        _map = map;
        _table = table;
        _seed = rules is { Seed: not 0 } ? (uint)rules.Seed : Hash(map.Name);
        var n = table.Kinds.Count;
        _grid = new int[n][];
        _nx = new int[n];
        _nz = new int[n];
        var generate = (rules?.Generate ?? true) && Math.Min(map.HalfX, map.HalfZ) >= table.MinWorldHalf;
        for (var k = 0; k < n; k++)
        {
            var kind = table.Kinds[k];
            _nx[k] = generate ? (int)MathF.Ceiling(map.HalfX * 2 / kind.Cell) : 0;
            _nz[k] = generate ? (int)MathF.Ceiling(map.HalfZ * 2 / kind.Cell) : 0;
            _grid[k] = new int[_nx[k] * _nz[k]];
            Array.Fill(_grid[k], -1);
            for (var z = 0; z < _nz[k]; z++)
                for (var x = 0; x < _nx[k]; x++)
                    Generate(k, kind, x, z);
        }
        if (rules != null)
            foreach (var p in rules.Deposits)
            {
                var kind = table.IndexOf(p.Kind);
                if (kind < 0)
                    continue;
                _placed.Add(_deposits.Count);
                var (cos, sin) = (MathF.Cos(p.Yaw * MathF.PI / 180f), MathF.Sin(p.Yaw * MathF.PI / 180f));
                _deposits.Add(new Deposit { Kind = kind, Left = table.Kinds[kind].Units, X = p.X, Z = p.Z, Rx = p.Radius, Rz = p.Radius * p.Aspect, Cos = cos, Sin = sin });
            }
        foreach (var b in map.Solid)
            BlockLattice(b);
    }

    private void Generate(int k, ResourceKind kind, int cx, int cz)
    {
        var h = Mix(Mix(Mix(_seed, (uint)k), (uint)cx), (uint)cz);
        if (Unit(ref h) >= kind.Chance)
            return;
        var x = -_map.HalfX + (cx + Unit(ref h)) * kind.Cell;
        var z = -_map.HalfZ + (cz + Unit(ref h)) * kind.Cell;
        var r = kind.RadiusMin + Unit(ref h) * (kind.RadiusMax - kind.RadiusMin);
        var aspect = kind.AspectMin + Unit(ref h) * (kind.AspectMax - kind.AspectMin);
        var yaw = Unit(ref h) * MathF.PI;
        _grid[k][cz * _nx[k] + cx] = _deposits.Count;
        _deposits.Add(new Deposit { Kind = k, Left = kind.Units, X = x, Z = z, Rx = r, Rz = r * aspect, Cos = MathF.Cos(yaw), Sin = MathF.Sin(yaw), Phase = Unit(ref h) * 6.28f });
    }

    private void BlockLattice(Box b)
    {
        var lo = b.Min;
        var hi = b.Max;
        // (ground slabs and walls around a whole map don't stop a forest: only what stands on it does)
        if (hi.Y - lo.Y < 0.4f || hi.X - lo.X > 80 || hi.Z - lo.Z > 80)
            return;
        for (var iz = Lattice(lo.Z - 1); iz <= Lattice(hi.Z + 1); iz++)
            for (var ix = Lattice(lo.X - 1); ix <= Lattice(hi.X + 1); ix++)
                _blocked.Add(TreeKey(ix, iz));
    }

    private static int Lattice(float v) => (int)MathF.Floor(v / TreeSpacing);

    /// <summary>A tree's id: its lattice cell.</summary>
    public static uint TreeKey(int ix, int iz) => (uint)(ix + 32768) << 16 | (uint)(iz + 32768) & 0xFFFF;

    private float Strength(in Deposit d, float x, float z)
    {
        float dx = x - d.X, dz = z - d.Z;
        var u = (dx * d.Cos + dz * d.Sin) / d.Rx;
        var v = (dz * d.Cos - dx * d.Sin) / d.Rz;
        var edge = 1f + 0.22f * MathF.Sin(u * 3.1f + d.Phase) * MathF.Cos(v * 2.7f + d.Phase);
        return edge - MathF.Sqrt(u * u + v * v);
    }

    /// <summary>The strongest deposit of the wanted layer here, or -1. Strength above 0 is inside it.</summary>
    private int At(float x, float z, bool surface, bool withLeft, out float best)
    {
        best = 0;
        var found = -1;
        for (var k = 0; k < _grid.Length; k++)
        {
            var kind = _table.Kinds[k];
            if (kind.Surface != surface || _nx[k] == 0)
                continue;
            var cx = (int)MathF.Floor((x + _map.HalfX) / kind.Cell);
            var cz = (int)MathF.Floor((z + _map.HalfZ) / kind.Cell);
            var reach = (int)MathF.Ceiling(kind.RadiusMax * 1.25f / kind.Cell);
            for (var j = Math.Max(cz - reach, 0); j <= Math.Min(cz + reach, _nz[k] - 1); j++)
                for (var i = Math.Max(cx - reach, 0); i <= Math.Min(cx + reach, _nx[k] - 1); i++)
                {
                    var di = _grid[k][j * _nx[k] + i];
                    if (di >= 0)
                        Consider(di, x, z, withLeft, ref best, ref found);
                }
        }
        foreach (var di in _placed)
            if (_table.Kinds[_deposits[di].Kind].Surface == surface)
                Consider(di, x, z, withLeft, ref best, ref found);
        return found;
    }

    private void Consider(int di, float x, float z, bool withLeft, ref float best, ref int found)
    {
        var d = _deposits[di];
        if (withLeft && d.Left <= 0)
            return;
        var s = Strength(d, x, z);
        if (s > best)
            (best, found) = (s, di);
    }

    /// <summary>The name of the resource layer under a point, or null (the look text and the self-test).</summary>
    public ResourceKind? KindAt(float x, float z, bool surface)
    {
        var i = At(x, z, surface, false, out _);
        return i < 0 ? null : _table.Kinds[_deposits[i].Kind];
    }

    /// <summary>Does the lattice cell hold a tree, felled or not? Same answer on every peer.</summary>
    public bool TreeAt(int ix, int iz, out float x, out float z, out uint hash)
    {
        hash = Mix(Mix(_seed ^ 0x51ed27u, (uint)ix), (uint)iz);
        var keep = Unit(ref hash);
        x = (ix + 0.2f + 0.6f * Unit(ref hash)) * TreeSpacing;
        z = (iz + 0.2f + 0.6f * Unit(ref hash)) * TreeSpacing;
        if (keep >= TreeKeep || MathF.Abs(x) > _map.HalfX - 2 || MathF.Abs(z) > _map.HalfZ - 2 || _blocked.Contains(TreeKey(ix, iz)))
            return false;
        return At(x, z, true, false, out var s) >= 0 && s > EdgeThin;
    }

    /// <summary>The nearest standing tree within <paramref name="reach"/> metres (flat distance) of a point, or false.</summary>
    public bool NearestTree(Vector3 at, float reach, out uint key)
    {
        key = 0;
        var best = reach * reach;
        var found = false;
        for (var iz = Lattice(at.Z - reach); iz <= Lattice(at.Z + reach); iz++)
            for (var ix = Lattice(at.X - reach); ix <= Lattice(at.X + reach); ix++)
            {
                var k = TreeKey(ix, iz);
                if (_felled.Contains(k) || !TreeAt(ix, iz, out var x, out var z, out _))
                    continue;
                var d = (x - at.X) * (x - at.X) + (z - at.Z) * (z - at.Z);
                if (d <= best)
                    (best, key, found) = (d, k, true);
            }
        return found;
    }

    /// <summary>Server: takes wood from a tree. Returns the units, and says when that felled it.</summary>
    public int Cut(uint key, int want, out bool felled)
    {
        var whole = _table.Kinds.Find(k => k.Surface)?.Units ?? 1;
        _cut.TryGetValue(key, out var done);
        var take = Math.Min(want, whole - done);
        felled = done + take >= whole;
        if (felled)
        {
            _cut.Remove(key);
            Fell(key);
        }
        else
            _cut[key] = done + take;
        return take;
    }

    /// <summary>Marks a tree felled (the server's cut, or a client hearing of it).</summary>
    public void Fell(uint key)
    {
        if (_felled.Add(key))
        {
            _drawnAt = new Vector3(float.MaxValue);
            DrawVersion++;
        }
    }

    /// <summary>Server: takes up to <paramref name="want"/> units from the underground deposit under a point.</summary>
    public int Tap(float x, float z, int want, out byte material)
    {
        material = 0;
        var i = At(x, z, false, true, out _);
        if (i < 0)
            return 0;
        var d = _deposits[i];
        material = RecyclingTable.Current.IdOf(_table.Kinds[d.Kind].Material);
        var take = Math.Min(want, d.Left);
        d.Left -= take;
        _deposits[i] = d;
        return take;
    }

    /// <summary>The nearest underground deposit's edge-side centre: for "the nearest iron vein is 84 m north east".</summary>
    public string NearestUnderground(float x, float z)
    {
        var bestD = float.MaxValue;
        var name = "";
        var (bx, bz) = (0f, 0f);
        foreach (var d in _deposits)
        {
            var kind = _table.Kinds[d.Kind];
            if (kind.Surface || d.Left <= 0)
                continue;
            var dist = (d.X - x) * (d.X - x) + (d.Z - z) * (d.Z - z);
            if (dist < bestD)
                (bestD, name, bx, bz) = (dist, kind.Name, d.X, d.Z);
        }
        if (name.Length == 0)
            return "no deposits known here";
        var ns = bz < z - 10 ? "north" : bz > z + 10 ? "south" : "";
        var ew = bx < x - 10 ? "west" : bx > x + 10 ? "east" : "";
        return $"nearest deposit: {name}, {MathF.Sqrt(bestD):0} m {(ns + " " + ew).Trim()}";
    }

    /// <summary>Everything back as the map starts (a new match).</summary>
    public void Reset()
    {
        for (var i = 0; i < _deposits.Count; i++)
        {
            var d = _deposits[i];
            d.Left = _table.Kinds[d.Kind].Units;
            _deposits[i] = d;
        }
        _cut.Clear();
        if (_felled.Count > 0)
        {
            _felled.Clear();
            _drawnAt = new Vector3(float.MaxValue);
            DrawVersion++;
        }
    }

    /// <summary>Keeps the drawn list around the camera. Rebuilds only after a real move or a felled tree.</summary>
    public void DrawAround(Vector3 cam)
    {
        if (Vector3.DistanceSquared(cam, _drawnAt) < DrawMove * DrawMove)
            return;
        _drawnAt = cam;
        _drawn.Clear();
        foreach (var d in _deposits)
        {
            if (!_table.Kinds[d.Kind].Surface)
                continue;
            var r = d.Rx * 1.25f;
            if (MathF.Abs(d.X - cam.X) > DrawRadius + r || MathF.Abs(d.Z - cam.Z) > DrawRadius + r)
                continue;
            for (var iz = Lattice(d.Z - r); iz <= Lattice(d.Z + r); iz++)
                for (var ix = Lattice(d.X - r); ix <= Lattice(d.X + r); ix++)
                    AddDrawn(ix, iz, d);
        }
        DrawVersion++;
    }

    private void AddDrawn(int ix, int iz, in Deposit d)
    {
        if (_drawn.Count >= MaxDrawnTrees || _felled.Contains(TreeKey(ix, iz)) || !TreeAt(ix, iz, out var x, out var z, out var h)
            || Strength(d, x, z) <= EdgeThin)
            return;
        var pine = Unit(ref h) < 0.45f;
        var height = 7f + 7f * Unit(ref h);
        var tint = 0.85f + 0.3f * Unit(ref h);
        var colour = (pine ? new Vector3(0.20f, 0.33f, 0.20f) : new Vector3(0.30f, 0.43f, 0.20f)) * tint;
        _drawn.Add(new FoliageInstance(pine ? FoliageType.Pine : FoliageType.Tree, new Vector3(x, _map.GroundHeight(x, z), z), height, Unit(ref h), Unit(ref h), colour));
    }

    private static uint Hash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
            h = (h ^ c) * 16777619u;
        return h;
    }

    private static uint Mix(uint h, uint v)
    {
        h ^= v * 0x9E3779B1u;
        h = (h << 13 | h >> 19) * 5u + 0xE6546B64u;
        h ^= h >> 15;
        return h * 0x2C1B3C6Du;
    }

    /// <summary>The next value in [0, 1) from a running hash (integers only up to the division, so the same on every peer).</summary>
    private static float Unit(ref uint h)
    {
        h = Mix(h, 0x7F4A7C15u);
        return (h >> 8) / 16777216f;
    }
}
