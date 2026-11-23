using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// One kind of plant (or loose rock) a biome grows: how thick, how big, what colour (docs/planets/BIOMES.md).
/// Densities are per square metre at High foliage quality.
/// </summary>
public sealed class BiomePlant
{
    public FoliageType Type;
    public float Density;
    public float SizeMin, SizeMax;
    public Vector3 Color;

    /// <summary>A tree's trunk colour.</summary>
    public Vector3 Bark = new(0.3f, 0.24f, 0.18f);

    /// <summary>How much each plant's colour strays from <see cref="Color"/> (0 to 0.5).</summary>
    public float Jitter = 0.12f;

    /// <summary>0: even cover; 1: in clumps <see cref="PatchScale"/> metres across with bare ground between.</summary>
    public float Patch;

    public float PatchScale = 14;

    /// <summary>The steepest ground it grows on (degrees).</summary>
    public float Slope = 32;

    /// <summary>
    /// The heights it grows at (metres; it thins out over the biomes' <see cref="PlanetBiomes.HeightSoft"/> outside them).
    /// Sean (2026-11-23): pines don't grow in the lowlands, broadleaves don't grow on the mountains.
    /// </summary>
    public float HeightMin = -1e6f, HeightMax = 1e6f;

    /// <summary>
    /// 0 to 1: how much this kind of tree grows in stands of its own kind (Sean, 2026-11-23: "groups of one type of tree are
    /// together"). Each kind has its own stand field over the land (<see cref="PlanetBiomes.StandScale"/> across); where
    /// its field is the highest of the kinds that could grow there it is up to 1 + stand times as thick, elsewhere down to
    /// 1 - stand. Kinds share the field by type, so a world's birches stand together whichever biome they are in.
    /// </summary>
    public float Stand;

    /// <summary>1: sizes even over the range; more: small ones common, big ones rare (a size is min + range * u^skew).</summary>
    public float Skew = 1;

    public int Seed;
}

/// <summary>A biome: where it is (its climate and heights), its ground colours and what grows in it.</summary>
public sealed class PlanetBiome
{
    public string Name = "";
    public float TempMin = -100, TempMax = 100;
    public float WetMin = -1, WetMax = 2;
    public float HeightMin = -1e6f, HeightMax = 1e6f;
    public float SlopeMax = 90;

    /// <summary>Ground colours over the world's own (null: the world's).</summary>
    public Vector3? Grass, Dirt, Rock;

    public readonly List<BiomePlant> Plants = new();
}

/// <summary>
/// A planetoid's biomes (the planetoid's <c>"biomes"</c> object, docs/planets/BIOMES.md). The climate is two smooth fields
/// over the sphere: a temperature from latitude (the +Y pole is <see cref="TempPole"/>, the equator <see cref="TempEquator"/>)
/// less <see cref="Lapse"/> degrees per km of height, broken by noise; and a wetness 0 to 1 from big seamless noise, wetter
/// in low ground. Each biome takes the ground where its ranges hold, fading out over soft edges, and where several hold the
/// ground colour blends and the plants mix by weight. Everything is a function of the direction and the features' height,
/// so any thread may ask and every peer gets the same answer.
/// </summary>
public sealed class PlanetBiomes
{
    public const int MaxBiomes = 12;
    public const int MaxPlants = 12; // per biome

    public int Seed = 1;
    public float TempPole = 0, TempEquator = 20, Lapse = 6, TempNoise = 3, TempScale = 2500;
    public float WetScale = 3000, WetLow = 0, WetLowEdge = 120, WetLowAdd = 0.25f;

    /// <summary>Soft edges: degrees of temperature, wetness and metres of height over which a biome fades out.</summary>
    public float TempSoft = 2, WetSoft = 0.06f, HeightSoft = 40;

    /// <summary>
    /// How strongly every plant's density swells and thins over the land, 0 to 1 (Sean, 2026-11-22: "higher density
    /// areas with more or less foliage"): thickets, open glades and clearings <see cref="ClumpScale"/> m across, shared by
    /// all the plants, so a glade is open of trees and brush alike. 0: even cover.
    /// </summary>
    public float Clumps = 0.75f;

    public float ClumpScale = 90;

    /// <summary>
    /// The density's swell at a place: about 0 (a clearing) to <see cref="MaxVigour"/> (a thicket). Two octaves of noise,
    /// <see cref="ClumpScale"/> and a third of it, pushed apart so the open and thick ground is common, not the middle.
    /// </summary>
    public float Vigour(double x, double y, double z, double radius)
    {
        if (Clumps <= 0)
            return 1;
        var n = Planetoid.Fbm3(x * radius, y * radius, z * radius, ClumpScale, 2, 0.45, 3, Seed + 131); // (about -0.6 to 0.6)
        var v = 1 + 2.2f * (float)n; // (spread: under 0 is a clearing)
        return 1 + Clumps * (Math.Clamp(v, 0f, MaxVigour) - 1);
    }

    public const float MaxVigour = 2.2f;

    /// <summary>How wide a stand of one kind of tree is (metres; <see cref="BiomePlant.Stand"/>).</summary>
    public float StandScale = 260;

    /// <summary>A kind of plant's stand field at a place (about -1 to 1; the same for every biome).</summary>
    public float StandAt(FoliageType t, double x, double y, double z, double radius) =>
        (float)Planetoid.Fbm3(x * radius, y * radius, z * radius, StandScale, 2, 0.5, 2.3, Seed * 13 + 701 + (int)t * 37);

    /// <summary>Keep the kept flat places (the <c>flatten</c> features) clear of plants.</summary>
    public bool ClearFlats = true;

    public readonly List<PlanetBiome> List = new();

    /// <summary>Temperature (degrees) at a unit direction and height.</summary>
    public float Temperature(double x, double y, double z, double radius, float h)
    {
        var n = TempNoise != 0 ? Planetoid.Fbm3(x * radius, y * radius, z * radius, TempScale, 2, 0.5, 2, Seed + 71) : 0;
        return (float)(TempEquator + (TempPole - TempEquator) * y * y - Lapse * Math.Max(h, 0) / 1000.0 + TempNoise * n * 1.6);
    }

    /// <summary>Wetness (about 0 to 1) at a unit direction and height.</summary>
    public float Wetness(double x, double y, double z, double radius, float h)
    {
        var n = Planetoid.Fbm3(x * radius, y * radius, z * radius, WetScale, 3, 0.5, 2, Seed + 97);
        var low = WetLowAdd * Smooth((WetLow - h) / Math.Max(WetLowEdge, 1f) + 0.5f);
        return (float)(0.5 + n * 0.8) + low;
    }

    /// <summary>
    /// Each biome's share at a place (<paramref name="w"/>, one per biome; they add to 1, or to 0 where no biome holds and
    /// the world's own ground shows). <paramref name="slope"/> is in degrees.
    /// </summary>
    public float Weights(double x, double y, double z, double radius, float h, float slope, Span<float> w)
    {
        var t = Temperature(x, y, z, radius, h);
        var wet = Wetness(x, y, z, radius, h);
        var sum = 0f;
        for (var b = 0; b < List.Count; b++)
        {
            var bi = List[b];
            var m = Range(t, bi.TempMin, bi.TempMax, TempSoft) * Range(wet, bi.WetMin, bi.WetMax, WetSoft)
                    * Range(h, bi.HeightMin, bi.HeightMax, HeightSoft) * Range(slope, -1, bi.SlopeMax, 4);
            m *= m;
            w[b] = m;
            sum += m;
        }
        if (sum > 1e-6f)
        {
            var k = 1f / MathF.Max(sum, 1f); // (where biomes overlap they share; at a lone biome's soft edge it thins to the world's own)
            for (var b = 0; b < List.Count; b++)
                w[b] *= k;
            return MathF.Min(sum, 1f);
        }
        for (var b = 0; b < List.Count; b++)
            w[b] = 0;
        return 0;
    }

    /// <summary>A ground colour scratch surface per thread (the pages are made on worker threads).</summary>
    [ThreadStatic] private static TerrainSurface? _scratch;
    [ThreadStatic] private static float[]? _w;

    /// <summary>
    /// The world's surface with each colour blended by the biomes at this place (a per-thread scratch copy: read it at once,
    /// don't keep it). <paramref name="local"/> is the ground normal in the frame where +Y is "up".
    /// </summary>
    public TerrainSurface SurfaceAt(TerrainSurface world, double x, double y, double z, double radius, float h, float slope)
    {
        var w = _w ??= new float[MaxBiomes];
        var total = Weights(x, y, z, radius, h, slope, w);
        if (total <= 0)
            return world;
        var s = _scratch ??= new TerrainSurface();
        s.DirtSlope = world.DirtSlope;
        s.RockSlope = world.RockSlope;
        s.SandBelow = world.SandBelow;
        s.Variation = world.Variation;
        s.Snow = world.Snow;
        s.SnowAbove = world.SnowAbove;
        s.Stone = world.Stone;
        s.Dusting = world.Dusting;
        s.Ice = world.Ice;
        s.IcePatches = world.IcePatches;
        s.IceScale = world.IceScale;
        s.Sand = world.Sand;
        var keep = 1 - total;
        Vector3 g = world.Grass * keep, d = world.Dirt * keep, r = world.Rock * keep;
        for (var b = 0; b < List.Count; b++)
        {
            if (w[b] <= 0)
                continue;
            var bi = List[b];
            g += (bi.Grass ?? world.Grass) * w[b];
            d += (bi.Dirt ?? world.Dirt) * w[b];
            r += (bi.Rock ?? world.Rock) * w[b];
        }
        s.Grass = g;
        s.Dirt = d;
        s.Rock = r;
        return s;
    }

    private static float Range(float v, float lo, float hi, float soft)
    {
        if (v >= lo && v <= hi)
            return 1;
        var e = v < lo ? lo - v : v - hi;
        return 1 - Smooth(e / soft);
    }

    private static float Smooth(float e)
    {
        var t = Math.Clamp(e, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    // ---- The map file ----------------------------------------------------------------------------------------------

    public static PlanetBiomes Parse(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} should be an object like {{ \"list\": [ ... ] }}");
        var p = new PlanetBiomes();
        foreach (var kv in e.EnumerateObject())
        {
            var v = kv.Value;
            switch (kv.Name.ToLowerInvariant())
            {
                case "seed": p.Seed = (int)Num(v, where, "seed", -1e9, 1e9); break;
                case "temppole": p.TempPole = (float)Num(v, where, "tempPole", -100, 100); break;
                case "tempequator": p.TempEquator = (float)Num(v, where, "tempEquator", -100, 100); break;
                case "lapse": p.Lapse = (float)Num(v, where, "lapse", 0, 50); break;
                case "tempnoise": p.TempNoise = (float)Num(v, where, "tempNoise", 0, 30); break;
                case "tempscale": p.TempScale = (float)Num(v, where, "tempScale", 50, 1e6); break;
                case "wetscale": p.WetScale = (float)Num(v, where, "wetScale", 50, 1e6); break;
                case "wetlow": p.WetLow = (float)Num(v, where, "wetLow", -1e5, 1e5); break;
                case "wetlowedge": p.WetLowEdge = (float)Num(v, where, "wetLowEdge", 1, 1e5); break;
                case "wetlowadd": p.WetLowAdd = (float)Num(v, where, "wetLowAdd", -1, 1); break;
                case "tempsoft": p.TempSoft = (float)Num(v, where, "tempSoft", 0.1, 50); break;
                case "wetsoft": p.WetSoft = (float)Num(v, where, "wetSoft", 0.01, 1); break;
                case "heightsoft": p.HeightSoft = (float)Num(v, where, "heightSoft", 1, 5000); break;
                case "clumps": p.Clumps = (float)Num(v, where, "clumps", 0, 1); break;
                case "clumpscale": p.ClumpScale = (float)Num(v, where, "clumpScale", 5, 5000); break;
                case "standscale": p.StandScale = (float)Num(v, where, "standScale", 10, 20000); break;
                case "clearflats": p.ClearFlats = v.ValueKind == JsonValueKind.True ? true : v.ValueKind == JsonValueKind.False ? false : throw new MapFileException($"{where}.clearFlats should be true or false"); break;
                case "list":
                    if (v.ValueKind != JsonValueKind.Array)
                        throw new MapFileException($"{where}.list should be a list of biomes");
                    var k = 0;
                    foreach (var b in v.EnumerateArray())
                        p.List.Add(ParseBiome(b, $"{where}.list[{k++}]", p.List.Count));
                    break;
                default:
                    throw new MapFileException($"{where}: no key \"{kv.Name}\" (it takes: seed, tempPole, tempEquator, lapse, tempNoise, tempScale, wetScale, wetLow, wetLowEdge, wetLowAdd, tempSoft, wetSoft, heightSoft, clumps, clumpScale, standScale, clearFlats, list)");
            }
        }
        if (p.List.Count is 0 or > MaxBiomes)
            throw new MapFileException($"{where}.list holds 1 to {MaxBiomes} biomes");
        return p;
    }

    private static PlanetBiome ParseBiome(JsonElement e, string where, int index)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} should be an object like {{ \"name\": \"moor\", \"temp\": [4, 12] }}");
        var b = new PlanetBiome();
        foreach (var kv in e.EnumerateObject())
        {
            var v = kv.Value;
            switch (kv.Name.ToLowerInvariant())
            {
                case "name":
                    b.Name = v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    if (b.Name.Length is 0 or > 40 || b.Name.Any(ch => ch is < ' ' or > '~'))
                        throw new MapFileException($"{where}.name should be a short line of plain text");
                    break;
                case "temp": (b.TempMin, b.TempMax) = Pair(v, where, "temp"); break;
                case "wet": (b.WetMin, b.WetMax) = Pair(v, where, "wet"); break;
                case "height": (b.HeightMin, b.HeightMax) = Pair(v, where, "height"); break;
                case "slope": b.SlopeMax = (float)Num(v, where, "slope", 0, 90); break;
                case "grass": b.Grass = MapFile.ToColour(v, ".grass", where); break;
                case "dirt": b.Dirt = MapFile.ToColour(v, ".dirt", where); break;
                case "rock": b.Rock = MapFile.ToColour(v, ".rock", where); break;
                case "plants":
                    if (v.ValueKind != JsonValueKind.Array)
                        throw new MapFileException($"{where}.plants should be a list");
                    var k = 0;
                    foreach (var pe in v.EnumerateArray())
                        b.Plants.Add(ParsePlant(pe, $"{where}.plants[{k}]", index * 31 + k++));
                    break;
                default:
                    throw new MapFileException($"{where}: no key \"{kv.Name}\" (it takes: name, temp, wet, height, slope, grass, dirt, rock, plants)");
            }
        }
        if (b.Plants.Count > MaxPlants)
            throw new MapFileException($"{where}.plants holds at most {MaxPlants}");
        return b;
    }

    private static BiomePlant ParsePlant(JsonElement e, string where, int seed)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} should be an object like {{ \"type\": \"pine\", \"density\": 0.02 }}");
        var p = new BiomePlant { Seed = seed };
        var typed = false;
        bool sized = false, coloured = false;
        foreach (var kv in e.EnumerateObject())
        {
            var v = kv.Value;
            switch (kv.Name.ToLowerInvariant())
            {
                case "type":
                    var name = v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    if (!Enum.TryParse<FoliageType>(name, true, out var t) || !Enum.IsDefined(t) || int.TryParse(name, out _) || PlanetFlora.LayerOf(t) < 0)
                        throw new MapFileException($"{where}.type: no plant \"{name}\" (planets grow: {string.Join(", ", Enum.GetValues<FoliageType>().Where(x => PlanetFlora.LayerOf(x) >= 0))})");
                    p.Type = t;
                    typed = true;
                    break;
                case "density": p.Density = (float)Num(v, where, "density", 0, 5); break;
                case "size": (p.SizeMin, p.SizeMax) = Pair(v, where, "size"); sized = true; break;
                case "color" or "colour": p.Color = MapFile.ToColour(v, ".color", where); coloured = true; break;
                case "bark": p.Bark = MapFile.ToColour(v, ".bark", where); break;
                case "jitter": p.Jitter = (float)Num(v, where, "jitter", 0, 0.5); break;
                case "patch": p.Patch = (float)Num(v, where, "patch", 0, 1); break;
                case "patchscale": p.PatchScale = (float)Num(v, where, "patchScale", 1, 2000); break;
                case "slope": p.Slope = (float)Num(v, where, "slope", 0, 90); break;
                case "height": (p.HeightMin, p.HeightMax) = Pair(v, where, "height"); break;
                case "stand": p.Stand = (float)Num(v, where, "stand", 0, 1); break;
                case "skew": p.Skew = (float)Num(v, where, "skew", 0.2, 6); break;
                default:
                    throw new MapFileException($"{where}: no key \"{kv.Name}\" (it takes: type, density, size, color, bark, jitter, patch, patchScale, slope, height, stand, skew)");
            }
        }
        if (!typed)
            throw new MapFileException($"{where}: needs \"type\"");
        if (!sized)
            (p.SizeMin, p.SizeMax) = Foliage.DefaultSize(p.Type);
        if (!coloured)
            p.Color = Foliage.DefaultColor(p.Type);
        var biggest = PlanetFlora.LayerOf(p.Type) == PlanetFlora.MonolithLayer ? PlanetFlora.MaxMonolithScale : PlanetFlora.MaxTreeHeight;
        if (p.SizeMin <= 0 || p.SizeMax < p.SizeMin || p.SizeMax > biggest)
            throw new MapFileException($"{where}.size is a range above 0, up to {biggest} for a {p.Type}");
        var max = PlanetFlora.MaxDensity(PlanetFlora.LayerOf(p.Type));
        if (p.Density > max)
            throw new MapFileException($"{where}.density: a {p.Type} grows at most {max:0.###} a square metre");
        return p;
    }

    private static double Num(JsonElement v, string where, string key, double lo, double hi)
    {
        if (v.ValueKind != JsonValueKind.Number)
            throw new MapFileException($"{where}.{key} should be a number");
        var d = v.GetDouble();
        return d >= lo && d <= hi ? d : throw new MapFileException($"{where}.{key} is {lo} to {hi}, got {d}");
    }

    private static (float, float) Pair(JsonElement v, string where, string key)
    {
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2 || v[0].ValueKind != JsonValueKind.Number || v[1].ValueKind != JsonValueKind.Number)
            throw new MapFileException($"{where}.{key} should be [low, high]");
        float a = (float)v[0].GetDouble(), b = (float)v[1].GetDouble();
        if (b < a)
            throw new MapFileException($"{where}.{key}: the high end is below the low one");
        return (a, b);
    }

    /// <summary>Fingerprint data (only for worlds with biomes: the others keep theirs).</summary>
    public void Mix(Action<float> mix)
    {
        mix(Seed); mix(TempPole); mix(TempEquator); mix(Lapse); mix(TempNoise); mix(TempScale);
        mix(WetScale); mix(WetLow); mix(WetLowEdge); mix(WetLowAdd); mix(ClearFlats ? 1 : 0); mix(Clumps); mix(ClumpScale); mix(StandScale);
        foreach (var b in List)
        {
            mix(b.TempMin); mix(b.TempMax); mix(b.WetMin); mix(b.WetMax); mix(b.HeightMin); mix(b.HeightMax); mix(b.SlopeMax);
            foreach (var p in b.Plants)
            {
                mix((float)p.Type); mix(p.Density); mix(p.SizeMin); mix(p.SizeMax); mix(p.Slope); mix(p.Patch); mix(p.PatchScale);
                mix(p.HeightMin); mix(p.HeightMax); mix(p.Stand); mix(p.Skew);
            }
        }
    }
}
