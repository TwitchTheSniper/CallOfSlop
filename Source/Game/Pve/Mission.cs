using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A co-op stage's objective type (docs/pve/DESIGN.md "Objective types"). One byte on the wire: append only.</summary>
public enum StageKind : byte
{
    Eliminate,
    Destroy,
    Defend,
    Reach,
    Collect,
}

/// <summary>
/// A point in a mission file: [x, z] (on the ground at that spot) or [x, y, z] (the floor at or just under y: rooms
/// underground). Resolved against the loaded map when a mission starts (<see cref="Resolve"/>).
/// </summary>
public readonly record struct MissionPoint(float X, float? Y, float Z)
{
    /// <summary>The feet position on the current map: the terrain (or the cave floor) under it, then any floor box a little under that.</summary>
    public Vector3 Resolve()
    {
        var map = World.Map;
        var ground = Y is { } y ? map.GroundHeight(X, Z, y + 0.6f) : map.GroundHeight(X, Z);
        var from = new Vector3(X, (Y ?? ground) + 1.2f, Z);
        var hit = World.Query.Raycast(from, -Vector3.UnitY, 3.5f, QueryFilter.All);
        return new Vector3(X, hit < 3.5f ? from.Y - hit : Y ?? ground, Z);
    }

    public override string ToString() => Y is { } y ? $"[{X:0.#}, {y:0.#}, {Z:0.#}]" : $"[{X:0.#}, {Z:0.#}]";
}

/// <summary>A destroy target, a collect item or a spawn zone: a named point with a radius.</summary>
public sealed record MissionSpot(string Name, MissionPoint At, float Radius);

/// <summary>One stage of a mission: its objective and its overrides.</summary>
public sealed class MissionStage
{
    public StageKind Kind;
    public string Text = "";

    /// <summary>Where the stage happens: the director's attack point and the HUD arrow (defaults to the stage's first point).</summary>
    public MissionPoint? Focus;

    /// <summary>Eliminate: kills needed (plus <see cref="CountPerPlayer"/> per player after the first); collect: items needed.</summary>
    public int Count, CountPerPlayer;

    /// <summary>Eliminate: a named target (a tougher enemy with a marker) whose death completes the stage.</summary>
    public PveArch? TargetArch;

    public string TargetName = "";
    public MissionPoint? TargetAt;
    public float TargetHealth = 4f;

    /// <summary>Destroy: the targets; collect: the items.</summary>
    public readonly List<MissionSpot> Targets = new();

    public float PlantSeconds = 3f, FuseSeconds = 6f;

    /// <summary>Defend / reach / collect delivery zone.</summary>
    public MissionPoint? At;

    public float Radius = 6f;
    public float Seconds = 120f, LoseSeconds = 25f;

    /// <summary>Collect: where items go (null: picking them up is enough).</summary>
    public MissionPoint? Deliver;

    public float DeliverRadius = 4f;

    /// <summary>Fails the stage (a checkpoint restart) when it runs out; 0: none.</summary>
    public float TimeLimit;

    /// <summary>Where players respawn during this stage (empty: the previous stage's, or the mission start).</summary>
    public readonly List<MissionPoint> Checkpoint = new();

    /// <summary>Where the director may spawn (empty: nav nodes in a ring round the focus).</summary>
    public readonly List<MissionSpot> SpawnZones = new();

    /// <summary>Director overrides: share of the alive budget (intensity), a budget multiplier, the enemy mix.</summary>
    public float Intensity = 0.8f, BudgetScale = 1f;

    public (PveArch Arch, float Weight)[]? Mix;

    /// <summary>The point the HUD arrow and the director aim at.</summary>
    public MissionPoint FocusPoint => Focus ?? At ?? TargetAt ?? (Targets.Count > 0 ? Targets[0].At : Checkpoint.Count > 0 ? Checkpoint[0] : default);
}

/// <summary>
/// A co-op mission (docs/pve/DESIGN.md "Data format"): a JSON file in a map pack's Missions folder
/// (Maps/World/Missions/SiloBreach.json), naming its map. Loaded once at startup (<see cref="Missions.All"/>).
/// </summary>
public sealed class Mission
{
    public const string Format = "call-of-slop-mission/1";

    public string Name = "";
    public string Map = "";
    public string Description = "";
    public string Source = "";

    public int Budget = 24, PerPlayer = 8, Max = 80;
    public (PveArch Arch, float Weight)[] Mix = [(PveArch.Rifleman, 1f)];
    public float RespawnScale = 1f;
    public readonly List<MissionPoint> Start = new();
    public readonly List<MissionStage> Stages = new();

    /// <summary>The alive budget for this many players at this difficulty (docs/pve/DESIGN.md), capped at <see cref="Max"/> and the engine's pool.</summary>
    public int BudgetFor(int players, BotDifficulty d) =>
        Math.Clamp((int)MathF.Round((Budget + PerPlayer * Math.Max(0, players - 1)) * PveDifficulty.Budget(d)), 1, Math.Min(Max, Game.PveMaxAlive));

    // ---- Loading ----

    private static readonly string[] TopKeys = ["format", "name", "map", "description", "enemies", "start", "respawnScale", "stages"];
    private static readonly string[] EnemyKeys = ["budget", "perPlayer", "max", "mix"];

    private static readonly string[] StageKeys =
    [
        "type", "text", "focus", "count", "countPerPlayer", "target", "targets", "items", "plantSeconds", "fuseSeconds", "at", "radius", "seconds", "loseSeconds",
        "deliver", "deliverRadius", "timeLimit", "checkpoint", "spawnZones", "intensity", "budgetScale", "mix",
    ];

    /// <summary>Parses a mission file. Throws <see cref="MapFileException"/> with the file and the place on any mistake.</summary>
    public static Mission Load(string json, string source)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{source}: a mission is a JSON object");
        Keys(root, TopKeys, source);
        var m = new Mission { Source = source };
        if (Str(root, "format", source) != Format)
            throw new MapFileException($"{source}: \"format\" must be \"{Format}\"");
        m.Name = Str(root, "name", source);
        m.Map = Str(root, "map", source);
        m.Description = Opt(root, "description") is { } d ? Text(d, source + " description") : "";
        if (m.Name.Length is 0 or > 40 || m.Name.Any(c => c < 32 || c > 126))
            throw new MapFileException($"{source}: \"name\" must be 1-40 ASCII characters");
        if (Opt(root, "enemies") is { } en)
        {
            Keys(en, EnemyKeys, source + " enemies");
            m.Budget = Int(en, "budget", m.Budget, 1, Game.PveMaxAlive, source);
            m.PerPlayer = Int(en, "perPlayer", m.PerPlayer, 0, 40, source);
            m.Max = Int(en, "max", m.Max, 1, Game.PveMaxAlive, source);
            if (Opt(en, "mix") is { } mix)
                m.Mix = ReadMix(mix, source + " enemies.mix");
        }
        m.RespawnScale = Num(root, "respawnScale", 1f, 0.2f, 5f, source);
        if (Opt(root, "start") is not { ValueKind: JsonValueKind.Array } start || start.GetArrayLength() == 0)
            throw new MapFileException($"{source}: \"start\" must list the squad's deploy points");
        foreach (var p in start.EnumerateArray())
            m.Start.Add(Point(p, source + " start"));
        if (Opt(root, "stages") is not { ValueKind: JsonValueKind.Array } stages || stages.GetArrayLength() == 0)
            throw new MapFileException($"{source}: \"stages\" must list at least one stage");
        if (stages.GetArrayLength() > 32)
            throw new MapFileException($"{source}: at most 32 stages");
        var i = 0;
        foreach (var s in stages.EnumerateArray())
            m.Stages.Add(ReadStage(s, $"{source} stage {++i}"));
        return m;
    }

    private static MissionStage ReadStage(JsonElement s, string where)
    {
        if (s.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: a stage is an object");
        Keys(s, StageKeys, where);
        var st = new MissionStage();
        var type = Str(s, "type", where);
        st.Kind = type.ToLowerInvariant() switch
        {
            "eliminate" => StageKind.Eliminate,
            "destroy" => StageKind.Destroy,
            "defend" => StageKind.Defend,
            "reach" or "extract" => StageKind.Reach,
            "collect" => StageKind.Collect,
            _ => throw new MapFileException($"{where}: \"type\" must be eliminate, destroy, defend, reach or collect, not \"{type}\""),
        };
        st.Text = Str(s, "text", where);
        if (st.Text.Length > 60 || st.Text.Any(c => c < 32 || c > 126))
            throw new MapFileException($"{where}: \"text\" must be at most 60 ASCII characters");
        if (Opt(s, "focus") is { } f)
            st.Focus = Point(f, where + " focus");
        if (Opt(s, "at") is { } at)
            st.At = Point(at, where + " at");
        st.Radius = Num(s, "radius", st.Radius, 1f, 40f, where);
        st.Count = Int(s, "count", 0, 0, 999, where);
        st.CountPerPlayer = Int(s, "countPerPlayer", 0, 0, 99, where);
        st.PlantSeconds = Num(s, "plantSeconds", st.PlantSeconds, 0.2f, 30f, where);
        st.FuseSeconds = Num(s, "fuseSeconds", st.FuseSeconds, 0.5f, 60f, where);
        st.Seconds = Num(s, "seconds", st.Seconds, 5f, 1800f, where);
        st.LoseSeconds = Num(s, "loseSeconds", st.LoseSeconds, 2f, 300f, where);
        st.DeliverRadius = Num(s, "deliverRadius", st.DeliverRadius, 1f, 30f, where);
        st.TimeLimit = Num(s, "timeLimit", 0f, 0f, 3600f, where);
        st.Intensity = Num(s, "intensity", st.Intensity, 0.05f, 1f, where);
        st.BudgetScale = Num(s, "budgetScale", st.BudgetScale, 0.1f, 2f, where);
        if (Opt(s, "deliver") is { } dv)
            st.Deliver = Point(dv, where + " deliver");
        if (Opt(s, "mix") is { } mix)
            st.Mix = ReadMix(mix, where + " mix");
        if (Opt(s, "target") is { } t)
        {
            if (t.ValueKind != JsonValueKind.Object)
                throw new MapFileException($"{where}: \"target\" is an object {{ \"arch\", \"name\", \"at\", \"health\" }}");
            Keys(t, ["arch", "name", "at", "health"], where + " target");
            st.TargetArch = (PveArchetypes.Find(Str(t, "arch", where + " target")) ?? throw new MapFileException($"{where}: unknown target archetype")).Arch;
            st.TargetName = Str(t, "name", where + " target");
            st.TargetAt = Point(Opt(t, "at") ?? throw new MapFileException($"{where}: the target needs \"at\""), where + " target.at");
            st.TargetHealth = Num(t, "health", 4f, 1f, 50f, where + " target");
        }
        foreach (var key in new[] { "targets", "items" })
            if (Opt(s, key) is { } list)
                st.Targets.AddRange(Spots(list, $"{where} {key}", 2.5f));
        if (Opt(s, "spawnZones") is { } zones)
            st.SpawnZones.AddRange(Spots(zones, where + " spawnZones", 12f));
        if (Opt(s, "checkpoint") is { } cp)
        {
            if (cp.ValueKind != JsonValueKind.Array)
                throw new MapFileException($"{where}: \"checkpoint\" is a list of points");
            foreach (var p in cp.EnumerateArray())
                st.Checkpoint.Add(Point(p, where + " checkpoint"));
        }
        // What each kind needs.
        switch (st.Kind)
        {
            case StageKind.Eliminate when st.Count == 0 && st.TargetArch == null:
                throw new MapFileException($"{where}: an eliminate stage needs a \"count\" or a \"target\"");
            case StageKind.Destroy when st.Targets.Count is 0 or > 8:
                throw new MapFileException($"{where}: a destroy stage needs 1-8 \"targets\"");
            case StageKind.Defend or StageKind.Reach when st.At == null:
                throw new MapFileException($"{where}: a {type} stage needs \"at\"");
            case StageKind.Collect when st.Targets.Count is 0 or > 8:
                throw new MapFileException($"{where}: a collect stage needs 1-8 \"items\"");
        }
        if (st.Kind == StageKind.Collect && (st.Count == 0 || st.Count > st.Targets.Count))
            st.Count = st.Targets.Count;
        return st;
    }

    private static IEnumerable<MissionSpot> Spots(JsonElement list, string where, float radius)
    {
        if (list.ValueKind != JsonValueKind.Array)
            throw new MapFileException($"{where}: expected a list");
        var n = 0;
        foreach (var e in list.EnumerateArray())
        {
            n++;
            if (e.ValueKind == JsonValueKind.Array)
            {
                yield return new MissionSpot($"{(char)('A' + (n - 1) % 26)}", Point(e, where), radius);
                continue;
            }
            if (e.ValueKind != JsonValueKind.Object)
                throw new MapFileException($"{where}: each entry is a point or {{ \"name\", \"at\", \"radius\" }}");
            Keys(e, ["name", "at", "radius"], where);
            var name = Opt(e, "name") is { } nm ? Text(nm, where) : $"{(char)('A' + (n - 1) % 26)}";
            yield return new MissionSpot(name, Point(Opt(e, "at") ?? throw new MapFileException($"{where}: missing \"at\""), where), Num(e, "radius", radius, 0.5f, 60f, where));
        }
    }

    private static (PveArch, float)[] ReadMix(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: the mix is {{ \"rifleman\": 5, \"rusher\": 2 }}");
        var list = new List<(PveArch, float)>();
        foreach (var p in e.EnumerateObject())
        {
            var a = PveArchetypes.Find(p.Name) ?? throw new MapFileException($"{where}: unknown archetype \"{p.Name}\" (rifleman, rusher, sniper, heavy, grenadier, officer)");
            if (p.Value.ValueKind != JsonValueKind.Number || p.Value.GetSingle() is var w && (w < 0 || w > 100))
                throw new MapFileException($"{where}: \"{p.Name}\" needs a weight 0-100");
            if (p.Value.GetSingle() > 0)
                list.Add((a.Arch, p.Value.GetSingle()));
        }
        if (list.Count == 0)
            throw new MapFileException($"{where}: the mix is empty");
        return list.ToArray();
    }

    private static MissionPoint Point(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() is not (2 or 3) || e.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number))
            throw new MapFileException($"{where}: a point is [x, z] or [x, y, z], got {e}");
        var v = e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if (v.Any(x => !float.IsFinite(x) || MathF.Abs(x) > 5000))
            throw new MapFileException($"{where}: a point is out of range: {e}");
        return v.Length == 2 ? new MissionPoint(v[0], null, v[1]) : new MissionPoint(v[0], v[1], v[2]);
    }

    private static void Keys(JsonElement o, string[] allowed, string where)
    {
        foreach (var p in o.EnumerateObject())
            if (!allowed.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\"");
    }

    private static JsonElement? Opt(JsonElement o, string name)
    {
        foreach (var p in o.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    private static string Text(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.String && e.GetString() is { } s && s.All(c => c is >= ' ' and <= '~') && s.Length <= 300 ? s : throw new MapFileException($"{where}: expected ASCII text");

    private static string Str(JsonElement o, string name, string where) =>
        Opt(o, name) is { } v ? Text(v, $"{where} \"{name}\"") : throw new MapFileException($"{where}: missing \"{name}\"");

    private static float Num(JsonElement o, string name, float fallback, float min, float max, string where)
    {
        if (Opt(o, name) is not { } v)
            return fallback;
        if (v.ValueKind != JsonValueKind.Number || v.GetSingle() is var f && (!float.IsFinite(f) || f < min || f > max))
            throw new MapFileException($"{where}: \"{name}\" must be a number {min}-{max}");
        return v.GetSingle();
    }

    private static int Int(JsonElement o, string name, int fallback, int min, int max, string where)
    {
        if (Opt(o, name) is not { } v)
            return fallback;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i) || i < min || i > max)
            throw new MapFileException($"{where}: \"{name}\" must be a whole number {min}-{max}");
        return i;
    }
}

/// <summary>Every mission in the Maps folder's packs (their Missions folders), loaded once.</summary>
public static class Missions
{
    /// <summary>The folder inside a map pack that holds its missions (kept out of the map loader, see Maps.InPackAssetFolder).</summary>
    public const string PackFolderName = "Missions";

    private static Mission[]? _all;

    /// <summary>Problems found while loading (the self-test fails on any).</summary>
    public static readonly List<string> LoadErrors = new();

    public static IReadOnlyList<Mission> All => _all ??= LoadAll();

    /// <summary>The missions for a map (by name), in file order.</summary>
    public static IEnumerable<Mission> ForMap(Map? map)
    {
        if (map == null)
            yield break;
        foreach (var m in All)
            if (m.Map.Equals(map.Name, StringComparison.OrdinalIgnoreCase))
                yield return m;
    }

    /// <summary>The first mission for a map, or null.</summary>
    public static Mission? For(Map? map)
    {
        foreach (var m in ForMap(map))
            return m;
        return null;
    }

    private static Mission[] LoadAll()
    {
        var list = new List<Mission>();
        try
        {
            if (Directory.Exists(Maps.Folder))
                foreach (var pack in Directory.GetDirectories(Maps.Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var dir = Path.Combine(pack, PackFolderName);
                    if (!Directory.Exists(dir))
                        continue;
                    foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        var rel = Path.GetRelativePath(Maps.Folder, file).Replace('\\', '/');
                        try
                        {
                            var m = Mission.Load(File.ReadAllText(file), "Maps/" + rel);
                            if (Maps.Find(m.Map) == null)
                                throw new MapFileException($"Maps/{rel}: no map called \"{m.Map}\"");
                            list.Add(m);
                        }
                        catch (Exception ex) when (ex is MapFileException or JsonException or IOException)
                        {
                            LoadErrors.Add(ex.Message);
                            Console.WriteLine($"[coop] mission skipped: {ex.Message}");
                        }
                    }
                }
        }
        catch (IOException ex)
        {
            LoadErrors.Add(ex.Message);
        }
        Console.WriteLine($"[coop] {list.Count} mission(s): {string.Join(", ", list.Select(m => $"{m.Name} ({m.Map}, {m.Stages.Count} stages)"))}");
        return list.ToArray();
    }
}
