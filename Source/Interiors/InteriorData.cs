using System.Text.Json;

namespace CallOfSlop.Interiors;

/// <summary>A data file the interior system refuses, with the reason.</summary>
public sealed class InteriorDataException(string message) : Exception(message);

/// <summary>How a size class lays out its decks (lengths in grid cells of <see cref="GridCell"/> metres).</summary>
public sealed record DeckRules(
    float Storey, float GridCell, int Corridor, int RowDepth, int RoomGrow, int Section, int Margin, int Decks,
    (int W, int D) Stair, (int W, int D) Elevator, string Door, bool ReserveRoute);

/// <summary>A size class by tonnage, like a ship class, with its deck rules and the rooms it asks for.</summary>
public sealed record SizeClass(string Id, string Name, double MinTonnes, double MaxTonnes, DeckRules Deck, IReadOnlyList<string> Programme);

public enum RoomLayout { Grid, Free }
public enum DeckWant { Any, Lower, Upper }
public enum RowWant { Any, Inner, Outer }
public enum EndWant { None, Bow, Stern }

/// <summary>A kind of room: its minimum size (cells along the spine, cells deep), where it likes to be, what fills it and what loot it holds.</summary>
public sealed record RoomType(
    string Id, string Name, int MinW, int MinD, int Want, RoomLayout Layout, DeckWant Deck, RowWant Row, EndWant End,
    bool Critical, bool Exterior, bool System, IReadOnlyList<string> Wants, IReadOnlyList<string> Avoids, string? Modules, string? Loot);

/// <summary>One kind of thing in a room: a part, how many, and whether it fills from the room's loot hook.</summary>
public sealed record ModuleItem(string Part, int Count, int PerCells, bool Loot, string? Role);

public sealed record ModuleSet(string Id, IReadOnlyList<ModuleItem> Items, IReadOnlyList<string> Missing);

/// <summary>
/// The interior system's data (Constructs/Interiors, docs/sandbox/INTERIORS.md): size classes, room types, module sets, the shell's parts and
/// the loot hooks. Loaded on demand by the tools, never at game start-up; a bad file is refused with a reason.
/// </summary>
public sealed class InteriorData
{
    public const string Format = "call-of-slop-interiors/1";

    public readonly List<SizeClass> Classes = new();
    public readonly Dictionary<string, RoomType> Rooms = new(StringComparer.Ordinal);
    public readonly Dictionary<string, ModuleSet> Sets = new(StringComparer.Ordinal);
    public readonly Dictionary<string, string> Shell = new(StringComparer.Ordinal);
    public readonly Dictionary<string, string?> LootHooks = new(StringComparer.Ordinal);
    public bool ProvisionalClasses, ProvisionalLoot;

    public static readonly string[] ShellKeys = ["floor", "stairFloor", "stair", "wall4", "wall2", "doorWall", "corner", "doorCorner", "garageWall", "hinge", "door", "adapter"];

    /// <summary>The data folder next to the exe (the build copies Constructs/**).</summary>
    public static string Folder => Path.Combine(AppFolders.Data, "Constructs", "Interiors");

    /// <summary>The class a body of <paramref name="tonnes"/> falls in, or null below the smallest band.</summary>
    public SizeClass? ClassFor(double tonnes) => Classes.FirstOrDefault(c => tonnes >= c.MinTonnes && tonnes < c.MaxTonnes);

    public static InteriorData Load(string folder)
    {
        var d = new InteriorData();
        d.LoadClasses(Read(folder, "SizeClasses.json"));
        d.LoadRooms(Read(folder, "RoomTypes.json"));
        d.LoadSets(Read(folder, "ModuleSets.json"));
        d.LoadLoot(Read(folder, "LootHooks.json"));
        d.Validate();
        return d;
    }

    private static (JsonElement Root, string File) Read(string folder, string file)
    {
        var path = Path.Combine(folder, file);
        if (!File.Exists(path))
            throw new InteriorDataException($"{file}: missing (looked in {folder})");
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement.Clone();
            if (Str(root, "format", file) != Format)
                throw new InteriorDataException($"{file}: format must be \"{Format}\"");
            return (root, file);
        }
        catch (JsonException e)
        {
            throw new InteriorDataException($"{file}: not JSON: {e.Message}");
        }
    }

    private void LoadClasses((JsonElement Root, string File) f)
    {
        ProvisionalClasses = f.Root.TryGetProperty("provisional", out var p) && p.ValueKind == JsonValueKind.True;
        foreach (var c in Arr(f.Root, "classes", f.File))
        {
            var id = Str(c, "id", f.File);
            var where = $"{f.File} class \"{id}\"";
            var dk = Obj(c, "deck", where);
            var rules = new DeckRules(
                NumF(dk, "storey", where, 2.5f, 6f), NumF(dk, "gridCell", where, 2f, 16f), Int(dk, "corridor", where, 1, 4), Int(dk, "rowDepth", where, 1, 8),
                Int(dk, "roomGrow", where, 0, 4), Int(dk, "section", where, 2, 64), Int(dk, "margin", where, 0, 4), Int(dk, "decks", where, 1, 8),
                Pair(dk, "stair", where), Pair(dk, "elevator", where), Str(dk, "door", where), Bool(dk, "reserveRoute"));
            if (MathF.Abs(rules.Storey - 3f) > 0.001f || MathF.Abs(rules.GridCell - 4f) > 0.001f)
                throw new InteriorDataException($"{where}: phase 1 builds with the Building pieces, so storey must be 3 and gridCell 4");
            var min = Num(c, "minTonnes", where, 0, 1e9);
            var max = Num(c, "maxTonnes", where, 0, 1e9);
            if (max <= min)
                throw new InteriorDataException($"{where}: maxTonnes must be above minTonnes");
            var prog = Arr(c, "programme", where).Select(e => e.GetString() ?? "").ToList();
            Classes.Add(new SizeClass(id, Str(c, "name", where), min, max, rules, prog));
        }
        Classes.Sort((a, b) => a.MinTonnes.CompareTo(b.MinTonnes));
        for (var i = 1; i < Classes.Count; i++)
            if (Math.Abs(Classes[i].MinTonnes - Classes[i - 1].MaxTonnes) > 1e-6)
                throw new InteriorDataException($"{f.File}: tonnage bands must meet: \"{Classes[i - 1].Id}\" ends at {Classes[i - 1].MaxTonnes} t, \"{Classes[i].Id}\" starts at {Classes[i].MinTonnes} t");
        if (Classes.Count == 0)
            throw new InteriorDataException($"{f.File}: no classes");
    }

    private void LoadRooms((JsonElement Root, string File) f)
    {
        foreach (var r in Arr(f.Root, "rooms", f.File))
        {
            var id = Str(r, "id", f.File);
            var where = $"{f.File} room \"{id}\"";
            var min = Pair(r, "min", where);
            var t = new RoomType(id, Str(r, "name", where), min.A, min.B, Int(r, "want", where, 1, 4096),
                Enum<RoomLayout>(r, "layout", where, RoomLayout.Grid), Enum<DeckWant>(r, "deck", where, DeckWant.Any), Enum<RowWant>(r, "row", where, RowWant.Any),
                Enum<EndWant>(r, "end", where, EndWant.None), Bool(r, "critical"), Bool(r, "exterior"), Bool(r, "system"),
                Names(r, "wants"), Names(r, "avoids"), OptStr(r, "modules"), OptStr(r, "loot"));
            if (!Rooms.TryAdd(id, t))
                throw new InteriorDataException($"{where}: listed twice");
        }
        foreach (var need in new[] { "stairs", "elevator", "storage", "fuel", "spare", "dead" })
            if (!Rooms.ContainsKey(need))
                throw new InteriorDataException($"{f.File}: the planner needs a room \"{need}\"");
    }

    private void LoadSets((JsonElement Root, string File) f)
    {
        var shell = Obj(f.Root, "shell", f.File);
        foreach (var k in ShellKeys)
            Shell[k] = Str(shell, k, $"{f.File} shell");
        foreach (var s in Arr(f.Root, "sets", f.File))
        {
            var id = Str(s, "id", f.File);
            var where = $"{f.File} set \"{id}\"";
            var items = Arr(s, "items", where).Select(i => new ModuleItem(Str(i, "part", where), Int(i, "count", where, 0, 64),
                i.TryGetProperty("perCells", out _) ? Int(i, "perCells", where, 1, 64) : 0, Bool(i, "loot"), OptStr(i, "role"))).ToList();
            if (!Sets.TryAdd(id, new ModuleSet(id, items, Names(s, "missing"))))
                throw new InteriorDataException($"{where}: listed twice");
        }
    }

    private void LoadLoot((JsonElement Root, string File) f)
    {
        ProvisionalLoot = f.Root.TryGetProperty("provisional", out var p) && p.ValueKind == JsonValueKind.True;
        foreach (var h in Obj(f.Root, "hooks", f.File).EnumerateObject())
            LootHooks[h.Name] = h.Value.ValueKind == JsonValueKind.Null ? null : h.Value.GetString();
    }

    private void Validate()
    {
        foreach (var c in Classes)
            foreach (var r in c.Programme)
                if (!Rooms.TryGetValue(r, out var t))
                    throw new InteriorDataException($"SizeClasses.json class \"{c.Id}\": programme names an unknown room \"{r}\"");
                else if (t.System)
                    throw new InteriorDataException($"SizeClasses.json class \"{c.Id}\": \"{r}\" is a system room the planner places itself");
        foreach (var t in Rooms.Values)
        {
            foreach (var w in t.Wants.Concat(t.Avoids))
                if (!Rooms.ContainsKey(w))
                    throw new InteriorDataException($"RoomTypes.json room \"{t.Id}\": wants or avoids an unknown room \"{w}\"");
            if (t.Modules != null && !Sets.ContainsKey(t.Modules))
                throw new InteriorDataException($"RoomTypes.json room \"{t.Id}\": no module set \"{t.Modules}\" in ModuleSets.json");
            if (t.Loot != null && !LootHooks.ContainsKey(t.Loot))
                throw new InteriorDataException($"RoomTypes.json room \"{t.Id}\": no loot hook \"{t.Loot}\" in LootHooks.json");
            if (t.Layout == RoomLayout.Free && t.System)
                throw new InteriorDataException($"RoomTypes.json room \"{t.Id}\": a system room is placed by the planner on the grid (free layouts are for programme rooms)");
        }
    }

    /// <summary>Every part the data names that this build doesn't have (the shell's are needed; a module's is skipped with a note).</summary>
    public List<string> UnknownParts()
    {
        var names = Shell.Values.Concat(Sets.Values.SelectMany(s => s.Items).Select(i => i.Part)).Distinct();
        return names.Where(n => ConstructParts.Find(n) == null).ToList();
    }

    // (small readers: each names the file and key it refuses)
    private static string Str(JsonElement e, string key, string where) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : throw new InteriorDataException($"{where}: \"{key}\" must be a text");

    private static string? OptStr(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static double Num(JsonElement e, string key, string where, double lo, double hi) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.GetDouble() is var d && d >= lo && d <= hi ? d : throw new InteriorDataException($"{where}: \"{key}\" must be a number from {lo} to {hi}");

    private static float NumF(JsonElement e, string key, string where, float lo, float hi) => (float)Num(e, key, where, (double)lo, hi);

    private static int Int(JsonElement e, string key, string where, int lo, int hi) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i >= lo && i <= hi ? i : throw new InteriorDataException($"{where}: \"{key}\" must be a whole number from {lo} to {hi}");

    private static (int A, int B) Pair(JsonElement e, string key, string where)
    {
        if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 && v[0].TryGetInt32(out var a) && v[1].TryGetInt32(out var b) && a >= 1 && b >= 1 && a <= 64 && b <= 64)
            return (a, b);
        throw new InteriorDataException($"{where}: \"{key}\" must be [width, depth] in cells, 1 to 64");
    }

    private static JsonElement Obj(JsonElement e, string key, string where) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : throw new InteriorDataException($"{where}: \"{key}\" must be an object");

    private static JsonElement.ArrayEnumerator Arr(JsonElement e, string key, string where) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : throw new InteriorDataException($"{where}: \"{key}\" must be a list");

    private static List<string> Names(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();

    private static T Enum<T>(JsonElement e, string key, string where, T fallback) where T : struct, Enum
    {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return fallback;
        return v.ValueKind == JsonValueKind.String && System.Enum.TryParse<T>(v.GetString(), true, out var t)
            ? t
            : throw new InteriorDataException($"{where}: \"{key}\" must be one of {string.Join(", ", System.Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}");
    }
}
