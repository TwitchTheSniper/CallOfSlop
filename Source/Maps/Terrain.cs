using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// One terrain feature from a map file ("hill", "cliff", "ramp"...): its type and parameters, kept exactly as
/// written so a map file round-trips. Values are doubles, strings, a flat number list ("heights") or a list of
/// points ("points": [[x, z], [x, z, y]...]). See Maps/README.md, "Terrain", for every type and parameter.
/// </summary>
public sealed class TerrainFeature
{
    /// <summary>Lower-case type name as written ("hill", "path"...).</summary>
    public required string Type;

    /// <summary>Parameters in file order (canonical key spelling).</summary>
    public readonly List<KeyValuePair<string, object>> Params = new();

    public bool Has(string key) => Params.Any(p => p.Key == key);

    public object? Get(string key)
    {
        foreach (var p in Params)
            if (p.Key == key)
                return p.Value;
        return null;
    }

    public double Num(string key, double fallback) => Get(key) is double d ? d : fallback;

    public string Str(string key, string fallback) => Get(key) is string s ? s : fallback;

    public double[][] Points => Get("points") as double[][] ?? [];

    public double[] List(string key) => Get(key) as double[] ?? [];

    /// <summary>Sets (or replaces) a parameter; for code-built terrains and tests.</summary>
    public TerrainFeature With(string key, object value)
    {
        var i = Params.FindIndex(p => p.Key == key);
        var kv = new KeyValuePair<string, object>(key, value);
        if (i >= 0)
            Params[i] = kv;
        else
            Params.Add(kv);
        return this;
    }
}

/// <summary>Colours and rules that decide grass / dirt / rock / sand on the terrain (by slope and height).</summary>
public sealed class TerrainSurface
{
    public Vector3 Grass = new(0.36f, 0.50f, 0.25f);
    public Vector3 Dirt = new(0.47f, 0.39f, 0.28f);
    public Vector3 Rock = new(0.47f, 0.46f, 0.44f);
    public Vector3 Sand = new(0.70f, 0.64f, 0.47f);

    /// <summary>Slopes at or above this many degrees are dirt.</summary>
    public double DirtSlope = 24;

    /// <summary>Slopes at or above this many degrees are rock (players can walk up to <see cref="World.MaxSlopeDeg"/>).</summary>
    public double RockSlope = 38;

    /// <summary>Gentle ground at or below this height is sand (null: no sand).</summary>
    public double? SandBelow;

    /// <summary>Low-frequency brightness variation drawn on top (0-0.3), so big fields aren't flat colour.</summary>
    public double Variation = 0.06;

    public Vector3 ColorOf(TerrainMaterial m) => m switch
    {
        TerrainMaterial.Dirt => Dirt,
        TerrainMaterial.Rock => Rock,
        TerrainMaterial.Sand => Sand,
        _ => Grass,
    };
}

/// <summary>Terrain surface types (stored per height sample; usable later as Jolt material indices or for footsteps).</summary>
public enum TerrainMaterial : byte
{
    Grass,
    Dirt,
    Rock,
    Sand,
}

/// <summary>
/// The "terrain" object of a map file: grid resolution, a base height, ordered features and surface rules.
/// <see cref="TerrainGen"/> turns it into a <see cref="TerrainData"/> heightfield, deterministically.
/// </summary>
public sealed class TerrainSpec
{
    /// <summary>Distance between height samples in metres (0.5-4; 1 is a good default).</summary>
    public double Cell = 1;

    /// <summary>How far the terrain continues past the playable edge (metres), so the horizon isn't a cut-off.</summary>
    public double Margin = 16;

    /// <summary>Starting height of every sample before the features run.</summary>
    public double Base;

    /// <summary>Optional clamps applied after all features.</summary>
    public double? Min, Max;

    public TerrainSurface Surface = new();
    public readonly List<TerrainFeature> Features = new();

    // ---- Schema ------------------------------------------------------------------------------

    private static readonly string[] Falloffs = ["smooth", "linear", "dome", "gauss"];
    private static readonly string[] LevelModes = ["set", "fill", "cut"];
    private static readonly string[] GridModes = ["add", "set"];
    private static readonly string[] Sides = ["left", "right"];
    private static readonly string[] MaterialNames = ["grass", "dirt", "rock", "sand"];

    /// <summary>Allowed keys per feature type (canonical spelling) and the ones that must be present.</summary>
    private static readonly Dictionary<string, (string[] Keys, string[] Required)> Schema = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hill"] = (["x", "z", "radius", "radiusZ", "height", "falloff", "material"], ["x", "z", "radius", "height"]),
        ["basin"] = (["x", "z", "radius", "radiusZ", "depth", "falloff", "material"], ["x", "z", "radius", "depth"]),
        ["ridge"] = (["points", "width", "height", "falloff", "material"], ["points", "width"]),
        ["valley"] = (["points", "width", "depth", "falloff", "material"], ["points", "width"]),
        ["plateau"] = (["x", "z", "radius", "w", "d", "height", "edge", "mode", "material"], ["x", "z", "height"]),
        ["flatten"] = (["x", "z", "radius", "w", "d", "height", "edge", "mode", "material"], ["x", "z"]),
        ["cliff"] = (["points", "height", "face", "back", "side", "taper", "material"], ["points", "height"]),
        ["ramp"] = (["points", "width", "edge", "mode", "material"], ["points"]),
        ["path"] = (["points", "width", "edge", "mode", "material"], ["points"]),
        ["noise"] = (["amplitude", "scale", "octaves", "persistence", "lacunarity", "seed", "x", "z", "radius"], ["amplitude"]),
        ["smooth"] = (["passes", "x", "z", "radius"], []),
        ["grid"] = (["x0", "z0", "cell", "cols", "rows", "heights", "mode"], ["x0", "z0", "cell", "cols", "rows", "heights"]),
    };

    /// <summary>Every feature type a map file can use.</summary>
    public static IEnumerable<string> FeatureTypes => Schema.Keys;

    // ---- Reading -----------------------------------------------------------------------------

    /// <summary>Reads a map file's "terrain" object. Throws <see cref="MapFileException"/> with a readable message.</summary>
    public static TerrainSpec Parse(JsonElement e, string source)
    {
        var where = $"{source}: terrain";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} should be an object like {{ \"cell\": 1, \"features\": [ ... ] }}");
        var spec = new TerrainSpec();
        foreach (var p in e.EnumerateObject())
        {
            switch (p.Name.ToLowerInvariant())
            {
                case "cell": spec.Cell = Range(Num(p.Value, where, "cell"), 0.25, 8, where, "cell"); break;
                case "margin": spec.Margin = Range(Num(p.Value, where, "margin"), 0, 128, where, "margin"); break;
                case "base": spec.Base = Num(p.Value, where, "base"); break;
                case "min": spec.Min = p.Value.ValueKind == JsonValueKind.Null ? null : Num(p.Value, where, "min"); break;
                case "max": spec.Max = p.Value.ValueKind == JsonValueKind.Null ? null : Num(p.Value, where, "max"); break;
                case "surface": spec.Surface = ParseSurface(p.Value, where); break;
                case "features":
                    if (p.Value.ValueKind != JsonValueKind.Array)
                        throw new MapFileException($"{where}: \"features\" should be a list");
                    var i = 0;
                    foreach (var f in p.Value.EnumerateArray())
                        spec.Features.Add(ParseFeature(f, $"{where}, feature #{++i}"));
                    break;
                default:
                    throw new MapFileException($"{where}: unknown field \"{p.Name}\" (it takes: cell, margin, base, min, max, surface, features)");
            }
        }
        if (spec.Min is { } lo && spec.Max is { } hi && lo > hi)
            throw new MapFileException($"{where}: \"min\" is above \"max\"");
        return spec;
    }

    private static TerrainSurface ParseSurface(JsonElement e, string where)
    {
        where += ".surface";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} should be an object");
        var s = new TerrainSurface();
        foreach (var p in e.EnumerateObject())
        {
            switch (p.Name.ToLowerInvariant())
            {
                case "grass": s.Grass = MapFile.ToColour(p.Value, $"{where}.grass", ""); break;
                case "dirt": s.Dirt = MapFile.ToColour(p.Value, $"{where}.dirt", ""); break;
                case "rock": s.Rock = MapFile.ToColour(p.Value, $"{where}.rock", ""); break;
                case "sand": s.Sand = MapFile.ToColour(p.Value, $"{where}.sand", ""); break;
                case "dirtslope": s.DirtSlope = Range(Num(p.Value, where, "dirtSlope"), 0, 90, where, "dirtSlope"); break;
                case "rockslope": s.RockSlope = Range(Num(p.Value, where, "rockSlope"), 0, 90, where, "rockSlope"); break;
                case "sandbelow": s.SandBelow = p.Value.ValueKind == JsonValueKind.Null ? null : Num(p.Value, where, "sandBelow"); break;
                case "variation": s.Variation = Range(Num(p.Value, where, "variation"), 0, 0.5, where, "variation"); break;
                default:
                    throw new MapFileException($"{where}: unknown field \"{p.Name}\" (it takes: grass, dirt, rock, sand, dirtSlope, rockSlope, sandBelow, variation)");
            }
        }
        return s;
    }

    private static TerrainFeature ParseFeature(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: should be an object like {{ \"type\": \"hill\", ... }}");
        string? type = null;
        foreach (var p in e.EnumerateObject())
            if (p.Name.Equals("type", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                type = p.Value.GetString()!.ToLowerInvariant();
        if (type == null || !Schema.TryGetValue(type, out var schema))
            throw new MapFileException($"{where}: unknown or missing \"type\". Known: {string.Join(", ", Schema.Keys)}");
        where += $" ({type})";
        var f = new TerrainFeature { Type = type };
        foreach (var p in e.EnumerateObject())
        {
            if (p.Name.Equals("type", StringComparison.OrdinalIgnoreCase))
                continue;
            var key = schema.Keys.FirstOrDefault(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                      ?? throw new MapFileException($"{where}: no parameter \"{p.Name}\" (it takes: {string.Join(", ", schema.Keys)})");
            f.Params.Add(new(key, ParseValue(p.Value, key, type, $"{where}.{key}")));
        }
        foreach (var r in schema.Required)
            if (!f.Has(r))
                throw new MapFileException($"{where}: needs \"{r}\"");
        Validate(f, where);
        return f;
    }

    private static object ParseValue(JsonElement v, string key, string type, string where)
    {
        switch (key)
        {
            case "points":
            {
                if (v.ValueKind != JsonValueKind.Array)
                    throw new MapFileException($"{where}: expected a list of points like [[0, 0], [10, 5]]");
                var pts = new List<double[]>();
                foreach (var pt in v.EnumerateArray())
                {
                    if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() is < 2 or > 3)
                        throw new MapFileException($"{where}: each point is [x, z] or [x, z, height]");
                    pts.Add(pt.EnumerateArray().Select(n => Num(n, where, key)).ToArray());
                }
                return pts.ToArray();
            }
            case "heights":
                if (v.ValueKind != JsonValueKind.Array)
                    throw new MapFileException($"{where}: expected a list of numbers");
                return v.EnumerateArray().Select(n => Num(n, where, key)).ToArray();
            case "falloff": return Choice(v, Falloffs, where);
            case "side": return Choice(v, Sides, where);
            case "material": return Choice(v, MaterialNames, where);
            case "mode": return Choice(v, type == "grid" ? GridModes : LevelModes, where);
            default: return Num(v, where, key);
        }
    }

    /// <summary>Range checks that would otherwise produce NaNs or nonsense.</summary>
    private static void Validate(TerrainFeature f, string where)
    {
        void Positive(string k)
        {
            if (f.Has(k) && !(f.Num(k, 1) > 0))
                throw new MapFileException($"{where}: \"{k}\" must be above 0");
        }
        void NotNegative(string k)
        {
            if (f.Has(k) && f.Num(k, 0) < 0)
                throw new MapFileException($"{where}: \"{k}\" can't be negative");
        }
        foreach (var k in new[] { "radius", "radiusZ", "width", "w", "d", "face", "back", "scale", "cell", "lacunarity" })
            Positive(k);
        foreach (var k in new[] { "edge", "taper", "persistence", "passes" })
            NotNegative(k);
        if (f.Has("points"))
        {
            var min = f.Type is "ridge" or "valley" or "cliff" or "ramp" or "path" ? 2 : 1;
            if (f.Points.Length < min)
                throw new MapFileException($"{where}: needs at least {min} points");
            var pts = f.Points;
            for (var i = 1; i < pts.Length; i++)
                if (pts[i][0] == pts[i - 1][0] && pts[i][1] == pts[i - 1][1])
                    throw new MapFileException($"{where}: points {i} and {i + 1} are the same spot");
        }
        if (f.Type is "plateau" or "flatten")
        {
            var hasR = f.Has("radius");
            var hasRect = f.Has("w") || f.Has("d");
            if (hasR == hasRect || (hasRect && !(f.Has("w") && f.Has("d"))))
                throw new MapFileException($"{where}: give either \"radius\" (a circle) or both \"w\" and \"d\" (a rectangle)");
        }
        if (f.Type == "noise" && f.Has("octaves") && f.Num("octaves", 4) is < 1 or > 8)
            throw new MapFileException($"{where}: \"octaves\" is 1-8");
        if (f.Type == "noise" && (f.Has("x") || f.Has("z")) && !(f.Has("x") && f.Has("z") && f.Has("radius")))
            throw new MapFileException($"{where}: a local noise patch needs \"x\", \"z\" and \"radius\"");
        if (f.Type == "smooth" && (f.Has("x") || f.Has("z") || f.Has("radius")) && !(f.Has("x") && f.Has("z") && f.Has("radius")))
            throw new MapFileException($"{where}: a local smooth needs \"x\", \"z\" and \"radius\"");
        if (f.Type == "smooth" && f.Num("passes", 1) > 20)
            throw new MapFileException($"{where}: \"passes\" is at most 20");
        if (f.Type == "grid")
        {
            var cols = f.Num("cols", 0);
            var rows = f.Num("rows", 0);
            if (cols < 2 || rows < 2 || cols != Math.Floor(cols) || rows != Math.Floor(rows))
                throw new MapFileException($"{where}: \"cols\" and \"rows\" are whole numbers, at least 2");
            if (f.List("heights").Length != (int)cols * (int)rows)
                throw new MapFileException($"{where}: \"heights\" needs cols x rows = {(int)cols * (int)rows} numbers, got {f.List("heights").Length}");
        }
    }

    private static string Choice(JsonElement v, string[] options, string where)
    {
        var s = v.ValueKind == JsonValueKind.String ? v.GetString()!.ToLowerInvariant() : null;
        return s != null && options.Contains(s) ? s : throw new MapFileException($"{where}: expected one of {string.Join(", ", options.Select(o => $"\"{o}\""))}, got {v}");
    }

    private static double Num(JsonElement e, string where, string key) =>
        e.ValueKind == JsonValueKind.Number && double.IsFinite(e.GetDouble()) ? e.GetDouble() : throw new MapFileException($"{where}: \"{key}\" expected a number, got {e}");

    private static double Range(double v, double lo, double hi, string where, string key) =>
        v >= lo && v <= hi ? v : throw new MapFileException($"{where}: \"{key}\" must be {lo.ToString(CultureInfo.InvariantCulture)}-{hi.ToString(CultureInfo.InvariantCulture)}, got {v.ToString(CultureInfo.InvariantCulture)}");

    // ---- Writing -----------------------------------------------------------------------------

    /// <summary>The "terrain" object as JSON (one feature per line), indented for a map file.</summary>
    public string ToJson(string indent)
    {
        var sb = new StringBuilder();
        var i2 = indent + "  ";
        sb.Append("{\n");
        sb.Append($"{i2}\"cell\": {N(Cell)},\n");
        sb.Append($"{i2}\"margin\": {N(Margin)},\n");
        if (Base != 0)
            sb.Append($"{i2}\"base\": {N(Base)},\n");
        if (Min is { } lo)
            sb.Append($"{i2}\"min\": {N(lo)},\n");
        if (Max is { } hi)
            sb.Append($"{i2}\"max\": {N(hi)},\n");
        var s = Surface;
        sb.Append($"{i2}\"surface\": {{ \"grass\": {C(s.Grass)}, \"dirt\": {C(s.Dirt)}, \"rock\": {C(s.Rock)}, \"sand\": {C(s.Sand)}, "
                  + $"\"dirtSlope\": {N(s.DirtSlope)}, \"rockSlope\": {N(s.RockSlope)}, "
                  + (s.SandBelow is { } sb2 ? $"\"sandBelow\": {N(sb2)}, " : "")
                  + $"\"variation\": {N(s.Variation)} }},\n");
        sb.Append($"{i2}\"features\": [\n");
        for (var k = 0; k < Features.Count; k++)
        {
            var f = Features[k];
            var parts = new List<string> { $"\"type\": {JsonSerializer.Serialize(f.Type)}" };
            foreach (var (key, v) in f.Params)
                parts.Add($"\"{key}\": {V(v)}");
            sb.Append(i2).Append("  { ").Append(string.Join(", ", parts)).Append(" }").Append(k < Features.Count - 1 ? ",\n" : "\n");
        }
        sb.Append($"{i2}]\n{indent}}}");
        return sb.ToString();
    }

    private static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static string C(Vector3 c) => $"[{c.X.ToString("R", CultureInfo.InvariantCulture)}, {c.Y.ToString("R", CultureInfo.InvariantCulture)}, {c.Z.ToString("R", CultureInfo.InvariantCulture)}]";

    private static string V(object v) => v switch
    {
        double d => N(d),
        string s => JsonSerializer.Serialize(s),
        double[][] pts => "[" + string.Join(", ", pts.Select(p => "[" + string.Join(", ", p.Select(N)) + "]")) + "]",
        double[] list => "[" + string.Join(", ", list.Select(N)) + "]",
        _ => throw new MapFileException($"can't write a terrain {v.GetType().Name}"),
    };
}

/// <summary>
/// Builds the heightfield from a <see cref="TerrainSpec"/>. Everything is plain double arithmetic in a fixed
/// order, and the result is rounded to 1/1024 m, so every machine (and the Python map tools, which port these
/// exact formulas) gets the same heights. Features run in file order over the whole grid.
/// </summary>
public static class TerrainGen
{
    /// <summary>Heights are rounded to multiples of this (about 1 mm).</summary>
    public const double Quantum = 1.0 / 1024.0;

    private sealed class Grid
    {
        public double Ox, Oz, Cell;
        public int Nx, Nz;
        public double[] H = [];
        public byte[] Paint = [];

        public double X(int i) => Ox + i * Cell;
        public double Z(int j) => Oz + j * Cell;

        /// <summary>Bilinear height (clamped to the grid), the same formula as <see cref="TerrainData.Sample"/>.</summary>
        public double Sample(double x, double z)
        {
            var fx = Math.Clamp((x - Ox) / Cell, 0, Nx - 1);
            var fz = Math.Clamp((z - Oz) / Cell, 0, Nz - 1);
            var i = Math.Min((int)Math.Floor(fx), Nx - 2);
            var j = Math.Min((int)Math.Floor(fz), Nz - 2);
            var tx = fx - i;
            var tz = fz - j;
            var h00 = H[j * Nx + i];
            var h10 = H[j * Nx + i + 1];
            var h01 = H[(j + 1) * Nx + i];
            var h11 = H[(j + 1) * Nx + i + 1];
            return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
        }

        /// <summary>Sample index ranges covering a world rectangle (empty if it misses the grid).</summary>
        public (int I0, int I1, int J0, int J1) Range(double minX, double maxX, double minZ, double maxZ) =>
            (Math.Max(0, (int)Math.Ceiling((minX - Ox) / Cell)), Math.Min(Nx - 1, (int)Math.Floor((maxX - Ox) / Cell)),
             Math.Max(0, (int)Math.Ceiling((minZ - Oz) / Cell)), Math.Min(Nz - 1, (int)Math.Floor((maxZ - Oz) / Cell)));

        public (int, int, int, int) All => (0, Nx - 1, 0, Nz - 1);
    }

    public static TerrainData Generate(TerrainSpec spec, float halfX, float halfZ)
    {
        var cell = spec.Cell;
        var extX = Math.Ceiling((halfX + spec.Margin) / cell) * cell;
        var extZ = Math.Ceiling((halfZ + spec.Margin) / cell) * cell;
        var g = new Grid
        {
            Ox = -extX,
            Oz = -extZ,
            Cell = cell,
            Nx = (int)Math.Round(2 * extX / cell) + 1,
            Nz = (int)Math.Round(2 * extZ / cell) + 1,
        };
        g.H = new double[g.Nx * g.Nz];
        g.Paint = new byte[g.H.Length];
        Array.Fill(g.H, spec.Base);
        Array.Fill(g.Paint, (byte)255);

        foreach (var f in spec.Features)
            Apply(g, f);

        var heights = new float[g.H.Length];
        for (var k = 0; k < g.H.Length; k++)
        {
            var h = g.H[k];
            if (spec.Min is { } lo)
                h = Math.Max(h, lo);
            if (spec.Max is { } hi)
                h = Math.Min(h, hi);
            g.H[k] = Math.Round(h / Quantum) * Quantum;
            heights[k] = (float)g.H[k];
        }
        return new TerrainData(g.Nx, g.Nz, (float)cell, (float)g.Ox, (float)g.Oz, heights, Materials(g, spec.Surface), spec.Surface);
    }

    // ---- Shared maths (ported 1:1 to the Python map tools) ------------------------------------

    public static double Smoothstep(double e)
    {
        var t = Math.Clamp(e, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Profile from 1 at the centre (t = 0) to 0 at the edge (t >= 1).</summary>
    public static double Falloff(double t, string kind)
    {
        if (t >= 1)
            return 0;
        return kind switch
        {
            "linear" => 1 - t,
            "dome" => Math.Sqrt(1 - t * t),
            "gauss" => (Math.Exp(-4 * t * t) - Math.Exp(-4)) / (1 - Math.Exp(-4)),
            _ => 1 - Smoothstep(t),
        };
    }

    /// <summary>Lattice hash in -1..1 (32-bit integer maths, wraps like C).</summary>
    public static double Hash(int ix, int iz, int seed)
    {
        unchecked
        {
            var h = (uint)(ix * 374761393 + iz * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0xFFFFFF * 2.0 - 1.0;
        }
    }

    /// <summary>Smooth value noise in -1..1 with one lattice point per unit.</summary>
    public static double ValueNoise(double x, double z, int seed)
    {
        var fx0 = Math.Floor(x);
        var fz0 = Math.Floor(z);
        var ix = (int)fx0;
        var iz = (int)fz0;
        var fx = x - fx0;
        var fz = z - fz0;
        var sx = fx * fx * (3 - 2 * fx);
        var sz = fz * fz * (3 - 2 * fz);
        var h00 = Hash(ix, iz, seed);
        var h10 = Hash(ix + 1, iz, seed);
        var h01 = Hash(ix, iz + 1, seed);
        var h11 = Hash(ix + 1, iz + 1, seed);
        var a = h00 + (h10 - h00) * sx;
        var b = h01 + (h11 - h01) * sx;
        return a + (b - a) * sz;
    }

    /// <summary>Fractal (fBm) value noise in -1..1.</summary>
    public static double Fbm(double x, double z, double scale, int octaves, double persistence, double lacunarity, int seed)
    {
        double sum = 0, ampSum = 0, amp = 1, freq = 1 / scale;
        for (var o = 0; o < octaves; o++)
        {
            sum += amp * ValueNoise(x * freq, z * freq, seed + o * 1013);
            ampSum += amp;
            amp *= persistence;
            freq *= lacunarity;
        }
        return ampSum > 0 ? sum / ampSum : 0;
    }

    private static byte MaterialId(string name) => name switch
    {
        "dirt" => (byte)TerrainMaterial.Dirt,
        "rock" => (byte)TerrainMaterial.Rock,
        "sand" => (byte)TerrainMaterial.Sand,
        _ => (byte)TerrainMaterial.Grass,
    };

    /// <summary>Closest point on a polyline. Segments are clamped except where <paramref name="extendEnds"/> lets the first/last run on.</summary>
    private static (int Seg, double T, double Dist2, double Qx, double Qz) Closest(double[][] pts, double px, double pz, bool extendEnds)
    {
        var best = (Seg: 0, T: 0.0, Dist2: double.MaxValue, Qx: 0.0, Qz: 0.0);
        var last = pts.Length - 2;
        for (var k = 0; k <= last; k++)
        {
            double ax = pts[k][0], az = pts[k][1];
            double abx = pts[k + 1][0] - ax, abz = pts[k + 1][1] - az;
            var t = ((px - ax) * abx + (pz - az) * abz) / (abx * abx + abz * abz);
            if (!extendEnds || k > 0)
                t = Math.Max(t, 0);
            if (!extendEnds || k < last)
                t = Math.Min(t, 1);
            var qx = ax + abx * t;
            var qz = az + abz * t;
            var d2 = (px - qx) * (px - qx) + (pz - qz) * (pz - qz);
            if (d2 < best.Dist2)
                best = (k, t, d2, qx, qz);
        }
        return best;
    }

    private static (double MinX, double MaxX, double MinZ, double MaxZ) Bounds(double[][] pts, double pad) =>
        (pts.Min(p => p[0]) - pad, pts.Max(p => p[0]) + pad, pts.Min(p => p[1]) - pad, pts.Max(p => p[1]) + pad);

    /// <summary>Applies a level change: "set" blends towards the target, "fill" only raises, "cut" only lowers.</summary>
    private static double Level(double h, double target, double mask, string mode)
    {
        var v = h + (target - h) * mask;
        return mode switch
        {
            "fill" => Math.Max(h, v),
            "cut" => Math.Min(h, v),
            _ => v,
        };
    }

    // ---- Features ----------------------------------------------------------------------------

    private static void Apply(Grid g, TerrainFeature f)
    {
        var paint = f.Get("material") is string m ? MaterialId(m) : (byte)255;
        switch (f.Type)
        {
            case "hill":
            case "basin":
            {
                double x = f.Num("x", 0), z = f.Num("z", 0), r = f.Num("radius", 1), rz = f.Num("radiusZ", r);
                var amount = f.Type == "hill" ? f.Num("height", 0) : -f.Num("depth", 0);
                var kind = f.Str("falloff", "smooth");
                var (i0, i1, j0, j1) = g.Range(x - r, x + r, z - rz, z + rz);
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    var dx = (g.X(i) - x) / r;
                    var dz = (g.Z(j) - z) / rz;
                    var v = Falloff(Math.Sqrt(dx * dx + dz * dz), kind);
                    var k = j * g.Nx + i;
                    g.H[k] += amount * v;
                    if (paint != 255 && v >= 0.5)
                        g.Paint[k] = paint;
                }
                break;
            }
            case "ridge":
            case "valley":
            {
                var pts = f.Points;
                var half = f.Num("width", 1) / 2;
                var sign = f.Type == "ridge" ? 1.0 : -1.0;
                var amount = f.Type == "ridge" ? f.Num("height", 0) : f.Num("depth", 0);
                var kind = f.Str("falloff", "smooth");
                var b = Bounds(pts, half);
                var (i0, i1, j0, j1) = g.Range(b.MinX, b.MaxX, b.MinZ, b.MaxZ);
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    var c = Closest(pts, g.X(i), g.Z(j), false);
                    var v = Falloff(Math.Sqrt(c.Dist2) / half, kind);
                    if (v <= 0)
                        continue;
                    var pa = pts[c.Seg];
                    var pb = pts[c.Seg + 1];
                    var a = pa.Length > 2 ? pa[2] : amount;
                    var bb = pb.Length > 2 ? pb[2] : amount;
                    var local = a + (bb - a) * c.T;
                    var k = j * g.Nx + i;
                    g.H[k] += sign * local * v;
                    if (paint != 255 && v >= 0.5)
                        g.Paint[k] = paint;
                }
                break;
            }
            case "plateau":
            case "flatten":
            {
                double x = f.Num("x", 0), z = f.Num("z", 0);
                var plateau = f.Type == "plateau";
                var target = f.Has("height") ? f.Num("height", 0) : g.Sample(x, z);
                var edge = f.Num("edge", plateau ? 6 : 4);
                var mode = f.Str("mode", plateau ? "fill" : "set");
                var circle = f.Has("radius");
                double r = f.Num("radius", 0), hw = f.Num("w", 0) / 2, hd = f.Num("d", 0) / 2;
                double ex = (circle ? r : hw) + edge, ez = (circle ? r : hd) + edge;
                var (i0, i1, j0, j1) = g.Range(x - ex, x + ex, z - ez, z + ez);
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    double dx = g.X(i) - x, dz = g.Z(j) - z, outside;
                    if (circle)
                        outside = Math.Max(0, Math.Sqrt(dx * dx + dz * dz) - r);
                    else
                    {
                        var ax = Math.Max(Math.Abs(dx) - hw, 0);
                        var az = Math.Max(Math.Abs(dz) - hd, 0);
                        outside = Math.Sqrt(ax * ax + az * az);
                    }
                    var mask = edge > 0 ? 1 - Smoothstep(outside / edge) : outside <= 0 ? 1 : 0;
                    if (mask <= 0)
                        continue;
                    var k = j * g.Nx + i;
                    g.H[k] = Level(g.H[k], target, mask, mode);
                    if (paint != 255 && mask >= 0.5)
                        g.Paint[k] = paint;
                }
                break;
            }
            case "cliff":
            {
                var pts = f.Points;
                var height = f.Num("height", 0);
                var face = f.Num("face", 2);
                var back = f.Num("back", 25);
                var left = f.Str("side", "left") == "left";
                var taper = f.Num("taper", 8);
                var cum = new double[pts.Length];
                for (var s = 1; s < pts.Length; s++)
                    cum[s] = cum[s - 1] + Math.Sqrt(Sq(pts[s][0] - pts[s - 1][0]) + Sq(pts[s][1] - pts[s - 1][1]));
                var total = cum[^1];
                var b = Bounds(pts, Math.Max(face, back));
                var (i0, i1, j0, j1) = g.Range(b.MinX, b.MaxX, b.MinZ, b.MaxZ);
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    double px = g.X(i), pz = g.Z(j);
                    var c = Closest(pts, px, pz, true);
                    var pa = pts[c.Seg];
                    var pb = pts[c.Seg + 1];
                    double abx = pb[0] - pa[0], abz = pb[1] - pa[1];
                    var len = cum[c.Seg + 1] - cum[c.Seg];
                    var u = cum[c.Seg] + c.T * len;
                    // Signed distance: positive on the left of the direction of travel (left = (dz, -dx)).
                    var side = (px - c.Qx) * abz - (pz - c.Qz) * abx;
                    var d = Math.Sqrt(c.Dist2);
                    var w = (side >= 0 ? d : -d) * (left ? 1 : -1); // > 0 on the high side
                    var prof = w >= 0 ? 1 - Smoothstep(w / back) : 1 - Smoothstep(-w / face);
                    double tp;
                    if (taper > 0)
                        tp = Smoothstep(Math.Min(Math.Clamp(u / taper, 0, 1), Math.Clamp((total - u) / taper, 0, 1)));
                    else
                        tp = u >= 0 && u <= total ? 1 : 0;
                    var v = prof * tp;
                    if (v <= 0)
                        continue;
                    var k = j * g.Nx + i;
                    g.H[k] += height * v;
                    if (paint != 255 && v >= 0.5)
                        g.Paint[k] = paint;
                }
                break;
            }
            case "ramp":
            case "path":
            {
                var src = f.Points;
                // Points without a height follow the ground as it is before this feature.
                var pts = src.Select(p => new[] { p[0], p[1], p.Length > 2 ? p[2] : g.Sample(p[0], p[1]) }).ToArray();
                var half = f.Num("width", 4) / 2;
                var edge = f.Num("edge", 3);
                var mode = f.Str("mode", "set");
                var b = Bounds(pts, half + edge);
                var (i0, i1, j0, j1) = g.Range(b.MinX, b.MaxX, b.MinZ, b.MaxZ);
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    var c = Closest(pts, g.X(i), g.Z(j), false);
                    var d = Math.Sqrt(c.Dist2);
                    var mask = d <= half ? 1 : edge > 0 ? 1 - Smoothstep((d - half) / edge) : 0;
                    if (mask <= 0)
                        continue;
                    var ya = pts[c.Seg][2];
                    var target = ya + (pts[c.Seg + 1][2] - ya) * c.T;
                    var k = j * g.Nx + i;
                    g.H[k] = Level(g.H[k], target, mask, mode);
                    if (paint != 255 && mask >= 0.5)
                        g.Paint[k] = paint;
                }
                break;
            }
            case "noise":
            {
                var amp = f.Num("amplitude", 0);
                var scale = f.Num("scale", 24);
                var octaves = (int)f.Num("octaves", 4);
                var persistence = f.Num("persistence", 0.5);
                var lacunarity = f.Num("lacunarity", 2);
                var seed = (int)f.Num("seed", 1);
                var local = f.Has("radius");
                double x = f.Num("x", 0), z = f.Num("z", 0), r = f.Num("radius", 1);
                var (i0, i1, j0, j1) = local ? g.Range(x - r, x + r, z - r, z + r) : g.All;
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    double px = g.X(i), pz = g.Z(j);
                    var mask = local ? 1 - Smoothstep(Math.Sqrt(Sq(px - x) + Sq(pz - z)) / r) : 1;
                    if (mask <= 0)
                        continue;
                    g.H[j * g.Nx + i] += amp * Fbm(px, pz, scale, octaves, persistence, lacunarity, seed) * mask;
                }
                break;
            }
            case "smooth":
            {
                var passes = (int)f.Num("passes", 1);
                var local = f.Has("radius");
                double x = f.Num("x", 0), z = f.Num("z", 0), r = f.Num("radius", 1);
                var tmp = new double[g.H.Length];
                var outp = new double[g.H.Length];
                for (var pass = 0; pass < passes; pass++)
                {
                    int nx = g.Nx, nz = g.Nz;
                    for (var j = 0; j < nz; j++)
                    for (var i = 0; i < nx; i++)
                        tmp[j * nx + i] = (g.H[j * nx + Math.Max(i - 1, 0)] + 2 * g.H[j * nx + i] + g.H[j * nx + Math.Min(i + 1, nx - 1)]) * 0.25;
                    for (var j = 0; j < nz; j++)
                    for (var i = 0; i < nx; i++)
                        outp[j * nx + i] = (tmp[Math.Max(j - 1, 0) * nx + i] + 2 * tmp[j * nx + i] + tmp[Math.Min(j + 1, nz - 1) * nx + i]) * 0.25;
                    for (var j = 0; j < nz; j++)
                    for (var i = 0; i < nx; i++)
                    {
                        var k = j * nx + i;
                        if (!local)
                            g.H[k] = outp[k];
                        else
                        {
                            var mask = 1 - Smoothstep(Math.Sqrt(Sq(g.X(i) - x) + Sq(g.Z(j) - z)) / r);
                            g.H[k] = g.H[k] + (outp[k] - g.H[k]) * mask;
                        }
                    }
                }
                break;
            }
            case "grid":
            {
                double x0 = f.Num("x0", 0), z0 = f.Num("z0", 0), gc = f.Num("cell", 1);
                int cols = (int)f.Num("cols", 2), rows = (int)f.Num("rows", 2);
                var hs = f.List("heights");
                var set = f.Str("mode", "add") == "set";
                var (i0, i1, j0, j1) = g.Range(x0, x0 + (cols - 1) * gc, z0, z0 + (rows - 1) * gc);
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    var fx = (g.X(i) - x0) / gc;
                    var fz = (g.Z(j) - z0) / gc;
                    if (fx < 0 || fz < 0 || fx > cols - 1 || fz > rows - 1)
                        continue;
                    var c = Math.Min((int)Math.Floor(fx), cols - 2);
                    var r = Math.Min((int)Math.Floor(fz), rows - 2);
                    var tx = fx - c;
                    var tz = fz - r;
                    var v = (hs[r * cols + c] * (1 - tx) + hs[r * cols + c + 1] * tx) * (1 - tz) + (hs[(r + 1) * cols + c] * (1 - tx) + hs[(r + 1) * cols + c + 1] * tx) * tz;
                    var k = j * g.Nx + i;
                    g.H[k] = set ? v : g.H[k] + v;
                }
                break;
            }
        }
    }

    private static double Sq(double v) => v * v;

    /// <summary>
    /// Material per sample: painted by a feature, else rock / dirt by slope (central differences), else sand
    /// below <see cref="TerrainSurface.SandBelow"/>, else grass. Slopes are compared as squared gradients
    /// against tan² of the thresholds, which keeps the Python port bit-exact.
    /// </summary>
    private static byte[] Materials(Grid g, TerrainSurface s)
    {
        var ids = new byte[g.H.Length];
        var rock2 = Sq(Math.Tan(s.RockSlope * Math.PI / 180.0));
        var dirt2 = Sq(Math.Tan(s.DirtSlope * Math.PI / 180.0));
        for (var j = 0; j < g.Nz; j++)
        for (var i = 0; i < g.Nx; i++)
        {
            var k = j * g.Nx + i;
            if (g.Paint[k] != 255)
            {
                ids[k] = g.Paint[k];
                continue;
            }
            var (gx, gz) = Gradient(g.H, g.Nx, g.Nz, g.Cell, i, j);
            var s2 = gx * gx + gz * gz;
            ids[k] = s2 >= rock2 ? (byte)TerrainMaterial.Rock
                : s2 >= dirt2 ? (byte)TerrainMaterial.Dirt
                : s.SandBelow is { } sand && g.H[k] <= sand ? (byte)TerrainMaterial.Sand
                : (byte)TerrainMaterial.Grass;
        }
        return ids;
    }

    /// <summary>Height gradient at a sample by central differences (one-sided at the grid edge).</summary>
    public static (double Gx, double Gz) Gradient(double[] h, int nx, int nz, double cell, int i, int j)
    {
        double gx, gz;
        if (i == 0)
            gx = (h[j * nx + 1] - h[j * nx]) / cell;
        else if (i == nx - 1)
            gx = (h[j * nx + i] - h[j * nx + i - 1]) / cell;
        else
            gx = (h[j * nx + i + 1] - h[j * nx + i - 1]) / (2 * cell);
        if (j == 0)
            gz = (h[nx + i] - h[i]) / cell;
        else if (j == nz - 1)
            gz = (h[j * nx + i] - h[(j - 1) * nx + i]) / cell;
        else
            gz = (h[(j + 1) * nx + i] - h[(j - 1) * nx + i]) / (2 * cell);
        return (gx, gz);
    }
}
