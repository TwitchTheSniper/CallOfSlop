using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A map file's optional "test" block: what the "Map contract" self-test (and "--check-map") must find true of this map
/// (Maps/README.md, "Testing a map"). Every key is optional; the defaults below apply when a key is left out. The block
/// is not part of the map's fingerprint (<see cref="Map.ContentHash"/>): it says what to check, not what the map is.
/// </summary>
public sealed class MapTestSpec
{
    public const string Key = "test";

    private static readonly string[] Keys =
    [
        "shape", "area", "size", "maxPlayers", "minProps", "minSolidProps", "minCosmeticProps", "minBreakableProps", "minDecals",
        "minDecalShare", "minMeshes", "minPlants", "minTrees", "maxDrawnBoxes", "minPackProps", "requireProps", "requireDecals",
        "symmetry", "symmetryTolerance", "unpairedSpawns", "symmetricHills", "symmetricProps", "minHills", "minTeamSpawns", "handPlaced",
        "walkFairness", "hillWalks", "maxNavBuildMs", "minMainNavPct", "minNavNodesAbove", "void", "voidAt", "probes", "fallKill",
        "botMatchSeconds", "botMatchMode", "botMatchPlayers", "maxStuckBots", "spawnsStand", "propsInside", "propsClear", "explosivesClear",
    ];

    /// <summary>"any" (the default), "non-rectangular", or a boundary shape: "rect", "circle", "ellipse", "smooth", "polygon".</summary>
    public string Shape = "any";

    /// <summary>The play area inside the outline must be within [min, max] square metres (null: not checked).</summary>
    public (float Min, float Max)? Area;

    public MapSize? Size;
    public int? MaxPlayers;

    // Dressing floors (0: not checked). MinDecalShare: placed decals as a percentage of those requested.
    public int MinProps, MinSolidProps, MinCosmeticProps, MinBreakableProps, MinDecals, MinMeshes, MinPlants, MinTrees, MinPackProps;
    public int MinDecalShare = 75;
    public int MaxDrawnBoxes = int.MaxValue;
    public string[] RequireProps = [];
    public string[] RequireDecals = [];

    /// <summary>"none", "turn" (twins at (-x, -z)), "mirrorX" (twins at (-x, z)) or "mirrorZ" (twins at (x, -z)).</summary>
    public string Symmetry = "none";
    public float SymmetryTolerance = 0.3f;
    public int UnpairedSpawns;
    public bool SymmetricHills = true;
    public bool SymmetricProps;

    public int MinHills = 3;
    public int MinTeamSpawns = -1; // -1: half the player limit
    public bool HandPlaced = true;

    /// <summary>The walks to each hill and the enemy flag from the two teams' spawns must be within this share of each other (0: only that routes exist).</summary>
    public float WalkFairness = 0.10f;

    /// <summary>"centre": the walk to a hill ends at the node nearest its centre; "zone": at the nearest node anywhere in the zone at the hill's height (raised decks).</summary>
    public string HillWalks = "centre";
    public int MaxNavBuildMs = 3000;
    public int MinMainNavPct = 75;
    public (float Height, int Count)? MinNavNodesAbove;

    /// <summary>A void map (no nav link over the void, nothing below the fall-kill height). Null: fallKill set, no ground slab, no terrain.</summary>
    public bool? Void;
    public Vector2[] VoidAt = [];

    /// <summary>Deck probes: (name, x, z, y): the floor at (x, z) is at y (within 0.1 m) and the nav graph's main area reaches it.</summary>
    public (string Name, float X, float Z, float Y)[] Probes = [];

    /// <summary>The fall kill: a player dropped at Drop dies within 4 s; one put at Safe stands and lives.</summary>
    public (Vector3 Drop, Vector3 Safe)? FallKill;

    public int BotMatchSeconds = 8;
    public GameMode BotMatchMode = GameMode.FreeForAll;
    public int BotMatchPlayers = -1; // -1: the player limit
    public int MaxStuckBots = -1; // -1: not checked (in a match under 12 s most bots are still walking out)

    public bool SpawnsStand;
    public bool PropsInside = true;
    public bool PropsClear = true;
    public float ExplosivesClear;

    /// <summary>The block as it was read, written back by <see cref="MapFile.ToJson"/>.</summary>
    public string Raw = "{}";

    public static MapTestSpec Parse(JsonElement e, string source)
    {
        var where = $"{source}: \"{Key}\"";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} must be an object (see Maps/README.md, \"Testing a map\")");
        var s = new MapTestSpec { Raw = e.GetRawText() };
        foreach (var p in e.EnumerateObject())
        {
            var k = Keys.FirstOrDefault(x => x.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new MapFileException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", Keys)})");
            var v = p.Value;
            var at = $"{where}.{k}";
            switch (k)
            {
                case "shape":
                    s.Shape = Word(v, at, "any", "non-rectangular", "rect", "circle", "ellipse", "smooth", "polygon");
                    break;
                case "area":
                    var range = Numbers(v, at, 2);
                    if (range[0] < 0 || range[1] < range[0])
                        throw new MapFileException($"{at}: [min, max] square metres, min <= max");
                    s.Area = (range[0], range[1]);
                    break;
                case "size":
                    s.Size = Enum.Parse<MapSize>(Word(v, at, "small", "medium", "large"), true);
                    break;
                case "maxPlayers":
                    s.MaxPlayers = Int(v, at, 2, Map.HardMaxPlayers);
                    break;
                case "minProps": s.MinProps = Int(v, at, 0); break;
                case "minSolidProps": s.MinSolidProps = Int(v, at, 0); break;
                case "minCosmeticProps": s.MinCosmeticProps = Int(v, at, 0); break;
                case "minBreakableProps": s.MinBreakableProps = Int(v, at, 0); break;
                case "minDecals": s.MinDecals = Int(v, at, 0); break;
                case "minDecalShare": s.MinDecalShare = Int(v, at, 0, 100); break;
                case "minMeshes": s.MinMeshes = Int(v, at, 0); break;
                case "minPlants": s.MinPlants = Int(v, at, 0); break;
                case "minTrees": s.MinTrees = Int(v, at, 0); break;
                case "maxDrawnBoxes": s.MaxDrawnBoxes = Int(v, at, 0); break;
                case "minPackProps": s.MinPackProps = Int(v, at, 0); break;
                case "requireProps": s.RequireProps = Words(v, at); break;
                case "requireDecals": s.RequireDecals = Words(v, at); break;
                case "symmetry":
                    s.Symmetry = Word(v, at, "none", "turn", "mirrorX", "mirrorZ");
                    break;
                case "symmetryTolerance": s.SymmetryTolerance = Float(v, at, 0f, 10f); break;
                case "unpairedSpawns": s.UnpairedSpawns = Int(v, at, 0); break;
                case "symmetricHills": s.SymmetricHills = Bool(v, at); break;
                case "symmetricProps": s.SymmetricProps = Bool(v, at); break;
                case "minHills": s.MinHills = Int(v, at, 0); break;
                case "minTeamSpawns": s.MinTeamSpawns = Int(v, at, 0); break;
                case "handPlaced": s.HandPlaced = Bool(v, at); break;
                case "walkFairness": s.WalkFairness = Float(v, at, 0f, 1f); break;
                case "hillWalks": s.HillWalks = Word(v, at, "centre", "zone"); break;
                case "maxNavBuildMs": s.MaxNavBuildMs = Int(v, at, 0); break;
                case "minMainNavPct": s.MinMainNavPct = Int(v, at, 0, 100); break;
                case "minNavNodesAbove":
                    var above = Numbers(v, at, 2);
                    s.MinNavNodesAbove = (above[0], (int)above[1]);
                    break;
                case "void": s.Void = Bool(v, at); break;
                case "voidAt":
                    s.VoidAt = v.ValueKind == JsonValueKind.Array
                        ? v.EnumerateArray().Select((pt, i) => { var n = Numbers(pt, $"{at}[{i}]", 2); return new Vector2(n[0], n[1]); }).ToArray()
                        : throw new MapFileException($"{at}: a list of [x, z] points");
                    break;
                case "probes":
                    if (v.ValueKind != JsonValueKind.Array)
                        throw new MapFileException($"{at}: a list of [\"name\", x, z, y] probes");
                    s.Probes = v.EnumerateArray().Select((pr, i) =>
                    {
                        var pa = $"{at}[{i}]";
                        if (pr.ValueKind != JsonValueKind.Array || pr.GetArrayLength() != 4 || pr[0].ValueKind != JsonValueKind.String)
                            throw new MapFileException($"{pa}: [\"name\", x, z, y]");
                        return (pr[0].GetString()!, Float(pr[1], pa), Float(pr[2], pa), Float(pr[3], pa));
                    }).ToArray();
                    break;
                case "fallKill":
                    if (v.ValueKind != JsonValueKind.Object || !v.TryGetProperty("drop", out var drop) || !v.TryGetProperty("safe", out var safe))
                        throw new MapFileException($"{at}: {{ \"drop\": [x, y, z], \"safe\": [x, y, z] }}");
                    foreach (var extra in v.EnumerateObject())
                        if (extra.Name is not ("drop" or "safe"))
                            throw new MapFileException($"{at}: unknown key \"{extra.Name}\" (drop, safe)");
                    s.FallKill = (Point(drop, at + ".drop"), Point(safe, at + ".safe"));
                    break;
                case "botMatchSeconds": s.BotMatchSeconds = Int(v, at, 0, 60); break;
                case "botMatchMode":
                    s.BotMatchMode = Word(v, at, "ffa", "tdm", "koth") switch
                    {
                        "tdm" => GameMode.TeamDeathmatch,
                        "koth" => GameMode.KingOfTheHill,
                        _ => GameMode.FreeForAll,
                    };
                    break;
                case "botMatchPlayers": s.BotMatchPlayers = Int(v, at, 2, Map.HardMaxPlayers); break;
                case "maxStuckBots": s.MaxStuckBots = Int(v, at, -1); break;
                case "spawnsStand": s.SpawnsStand = Bool(v, at); break;
                case "propsInside": s.PropsInside = Bool(v, at); break;
                case "propsClear": s.PropsClear = Bool(v, at); break;
                case "explosivesClear": s.ExplosivesClear = Float(v, at, 0f, 100f); break;
            }
        }
        return s;
    }

    private static string Word(JsonElement v, string at, params string[] allowed) =>
        v.ValueKind == JsonValueKind.String && allowed.FirstOrDefault(a => a.Equals(v.GetString(), StringComparison.OrdinalIgnoreCase)) is { } w
            ? w : throw new MapFileException($"{at}: one of {string.Join(", ", allowed.Select(a => $"\"{a}\""))}, got {v}");

    private static string[] Words(JsonElement v, string at) =>
        v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)
            ? v.EnumerateArray().Select(x => x.GetString()!).ToArray() : throw new MapFileException($"{at}: a list of names");

    private static bool Bool(JsonElement v, string at) =>
        v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new MapFileException($"{at}: true or false, got {v}");

    private static int Int(JsonElement v, string at, int min, int max = int.MaxValue) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i >= min && i <= max
            ? i : throw new MapFileException($"{at}: a whole number{(max == int.MaxValue ? $" of at least {min}" : $" from {min} to {max}")}, got {v}");

    private static float Float(JsonElement v, string at, float min = float.MinValue, float max = float.MaxValue) =>
        v.ValueKind == JsonValueKind.Number && v.GetSingle() is var f && f >= min && f <= max
            ? f : throw new MapFileException($"{at}: a number{(min > float.MinValue ? $" from {min} to {max}" : "")}, got {v}");

    private static float[] Numbers(JsonElement v, string at, int count) =>
        v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == count && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number)
            ? v.EnumerateArray().Select(x => x.GetSingle()).ToArray() : throw new MapFileException($"{at}: a list of {count} numbers, got {v}");

    private static Vector3 Point(JsonElement v, string at)
    {
        var n = Numbers(v, at, 3);
        return new Vector3(n[0], n[1], n[2]);
    }
}

public sealed partial class Map
{
    /// <summary>The map file's "test" block, or null when it has none (MapFile.Test.cs).</summary>
    public MapTestSpec? TestSpec;
}
