using System.Text.Json;

namespace CallOfSlop.Interiors;

/// <summary>A data file the interior system refuses, with the reason.</summary>
public sealed class InteriorDataException(string message) : Exception(message);

/// <summary>How a size class lays out its decks (lengths in grid cells of <see cref="GridCell"/> metres).</summary>
public sealed record DeckRules(
    float Storey, float GridCell, int Corridor, int RowDepth, int RoomGrow, int Section, int Margin, int Decks,
    (int W, int D) Stair, (int W, int D) Elevator, string Door, bool ReserveRoute);

/// <summary>
/// A size class by tonnage, like a ship class, with its deck rules, the rooms it asks for and <paramref name="Fill"/>: the room types a big
/// leftover may become (INTERIORS.md phase 3; empty: it stays spare space).
/// </summary>
public sealed record SizeClass(string Id, string Name, double MinTonnes, double MaxTonnes, DeckRules Deck, IReadOnlyList<string> Programme, IReadOnlyList<string> Fill)
{
    /// <summary>The rooms that go inside a hollow chassis, below the deck (phase 3: planned only; INTERIORS.md "The chassis is the interior").</summary>
    public IReadOnlyList<string> Chassis { get; init; } = [];

    /// <summary>What a fitted construct of this class may use in all (the vehicle and its interior), so players keep the rest (phase 5).</summary>
    public Budget Budget { get; init; } = Budget.None;

    /// <summary>The biggest part size class its rooms may hold (ConstructParts.Small and up; "partClass"): a module item lists its parts biggest first and the builder takes the biggest that is allowed and fits (phase 8).</summary>
    public byte PartClass { get; init; }

    /// <summary>The weighted programme leftovers and generated rooms draw from (phase 8; "fill"): each type's weight, its cap on the share, where it may go.</summary>
    public IReadOnlyList<FillEntry> FillMix { get; init; } = [];

    /// <summary>This class for a hollow chassis's void (phase 4): its <see cref="Chassis"/> rooms as the programme, its fill only what may go below the deck.</summary>
    public SizeClass ForChassis(int decks)
    {
        var mix = FillMix.Where(f => f.Where != "deck").ToList();
        return this with { Deck = Deck with { Decks = decks, ReserveRoute = false }, Programme = Chassis, Fill = mix.Select(f => f.Room).ToList(), FillMix = mix };
    }
}

/// <summary>
/// One room type of a class's weighted programme (phase 8): <paramref name="Weight"/> against the others' (its target share of the rooms the
/// planner makes from leftovers), <paramref name="Cap"/> the most of that share it may take (1: no cap), <paramref name="Where"/> deck, chassis or
/// any, and <paramref name="PerQuarters"/> (0: none) one wanted for every that many crew quarters (mess, medical: the crew's count).
/// </summary>
public sealed record FillEntry(string Room, double Weight, double Cap, string Where, int PerQuarters);

/// <summary>
/// A fitted construct's limits (INTERIORS.md "Phase 5"): the most parts, socket links and joints the vehicle and its interior use together,
/// under the engine's caps (Construct.MaxParts, MaxLinks, MaxJoints); the difference is the players' reserve.
/// </summary>
public sealed record Budget(int Parts, int Links, int Joints, int Doors = Construct.MaxDoors)
{
    public static readonly Budget None = new(Construct.MaxParts, Construct.MaxLinks, Construct.MaxJoints, Construct.MaxDoors);
}

public enum RoomLayout { Grid, Free }
public enum DeckWant { Any, Lower, Upper }
public enum RowWant { Any, Inner, Outer }
public enum EndWant { None, Bow, Stern }

/// <summary>A kind of room: its minimum size (cells along the spine, cells deep), where it likes to be, what fills it and what loot it holds.</summary>
public sealed record RoomType(
    string Id, string Name, int MinW, int MinD, int Want, RoomLayout Layout, DeckWant Deck, RowWant Row, EndWant End,
    bool Critical, bool Exterior, bool System, IReadOnlyList<string> Wants, IReadOnlyList<string> Avoids, string? Modules, string? Loot);

/// <summary>One kind of thing in a room: a part, how many, and whether it fills from the room's loot hook.</summary>
public sealed record ModuleItem(string Part, int Count, int PerCells, bool Loot, string? Role)
{
    /// <summary>The part and its alternatives, biggest size class first ("part" as a list): the builder takes the first the class allows and the room fits (phase 8).</summary>
    public IReadOnlyList<string> Parts { get; init; } = [Part];
}

/// <summary>
/// A link the builder lays from another room's things into this room's (phase 8): <paramref name="Link"/> ("chute", "pipe") from the nearest part with
/// role <paramref name="FromRole"/> in a room of a type in <paramref name="From"/> (its port <paramref name="FromPort"/>) to each of this room's parts with
/// role <paramref name="ToRole"/> (port <paramref name="ToPort"/>), laid at ceiling height, at most <paramref name="Reach"/> metres apart.
/// </summary>
public sealed record ModuleFeed(string Link, IReadOnlyList<string> From, string FromRole, string? FromPort, string ToRole, string? ToPort, float Reach);

public sealed record ModuleSet(string Id, IReadOnlyList<ModuleItem> Items, IReadOnlyList<string> Missing)
{
    public IReadOnlyList<ModuleFeed> Feeds { get; init; } = [];
}

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
    public readonly List<string> Panels = new(); // (the shell's long walls, longest first: phase 5; none: 4 m pieces)
    public readonly List<string> FloorPanels = new(); // (the shell's big floors, biggest first: phase 8; none: the 4x4 m slab everywhere)
    public readonly Dictionary<byte, string> Adapters = new(); // (a part class above small to its "Adapter ... to Small": how a bigger part stands on a slab, phase 8)
    public bool ProvisionalClasses, ProvisionalLoot;

    public static readonly string[] ShellKeys = ["floor", "stairFloor", "stair", "wall4", "wall2", "doorWall", "corner", "doorCorner", "garageWall", "hinge", "door", "adapter", "swingDoor", "bulkheadDoor", "doubleDoorL", "doubleDoorR"];

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
            var budget = Budget.None;
            if (c.TryGetProperty("budget", out _))
            {
                var bg = Obj(c, "budget", where);
                budget = new Budget(Int(bg, "parts", where + " budget", 1, Construct.MaxParts), Int(bg, "links", where + " budget", 1, Construct.MaxLinks),
                    Int(bg, "joints", where + " budget", 0, Construct.MaxJoints), Int(bg, "doors", where + " budget", 0, Construct.MaxDoors));
            }
            var mix = new List<FillEntry>();
            if (c.TryGetProperty("fill", out var fl) && fl.ValueKind == JsonValueKind.Array)
                foreach (var e in fl.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String)
                        mix.Add(new FillEntry(e.GetString() ?? "", 1, 1, "any", 0));
                    else
                    {
                        var fw = $"{where} fill";
                        var wh = OptStr(e, "where") ?? "any";
                        if (wh is not ("any" or "deck" or "chassis"))
                            throw new InteriorDataException($"{fw}: \"where\" must be any, deck or chassis");
                        mix.Add(new FillEntry(Str(e, "room", fw), Num(e, "weight", fw, 0.01, 100), e.TryGetProperty("cap", out _) ? Num(e, "cap", fw, 0.01, 1) : 1, wh,
                            e.TryGetProperty("perQuarters", out _) ? Int(e, "perQuarters", fw, 1, 64) : 0));
                    }
            var pc = OptStr(c, "partClass") ?? "small";
            byte partClass = pc switch { "small" => ConstructParts.Small, "medium" => ConstructParts.Medium, "large" => ConstructParts.Large, "capital" => ConstructParts.Capital, _ => throw new InteriorDataException($"{where}: \"partClass\" must be small, medium, large or capital") };
            Classes.Add(new SizeClass(id, Str(c, "name", where), min, max, rules, prog, mix.Select(m => m.Room).ToList()) { Chassis = Names(c, "chassis"), Budget = budget, PartClass = partClass, FillMix = mix });
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
        Panels.AddRange(Names(shell, "panels"));
        FloorPanels.AddRange(Names(shell, "floorPanels"));
        if (shell.TryGetProperty("adapters", out var ad) && ad.ValueKind == JsonValueKind.Object)
            foreach (var a in ad.EnumerateObject())
                Adapters[a.Name switch { "medium" => ConstructParts.Medium, "large" => ConstructParts.Large, "capital" => ConstructParts.Capital, _ => throw new InteriorDataException($"{f.File} shell adapters: \"{a.Name}\" must be medium, large or capital") }] =
                    a.Value.GetString() ?? throw new InteriorDataException($"{f.File} shell adapters: \"{a.Name}\" must be a part name");
        foreach (var s in Arr(f.Root, "sets", f.File))
        {
            var id = Str(s, "id", f.File);
            var where = $"{f.File} set \"{id}\"";
            var items = Arr(s, "items", where).Select(i =>
            {
                var parts = i.TryGetProperty("part", out var pv) && pv.ValueKind == JsonValueKind.Array ? Names(i, "part") : [Str(i, "part", where)];
                if (parts.Count == 0 || parts.Any(n => n.Length == 0))
                    throw new InteriorDataException($"{where}: \"part\" must be a name or a list of names");
                return new ModuleItem(parts[0], Int(i, "count", where, 0, 64), i.TryGetProperty("perCells", out _) ? Int(i, "perCells", where, 1, 64) : 0, Bool(i, "loot"), OptStr(i, "role")) { Parts = parts };
            }).ToList();
            var feeds = new List<ModuleFeed>();
            if (s.TryGetProperty("feeds", out var fd) && fd.ValueKind == JsonValueKind.Array)
                foreach (var e in fd.EnumerateArray())
                {
                    var fw = $"{where} feed";
                    var link = Str(e, "link", fw);
                    if (link is not ("chute" or "pipe" or "power" or "data"))
                        throw new InteriorDataException($"{fw}: \"link\" must be chute, pipe, power or data");
                    feeds.Add(new ModuleFeed(link, e.TryGetProperty("from", out var fv) && fv.ValueKind == JsonValueKind.Array ? Names(e, "from") : [Str(e, "from", fw)], Str(e, "fromRole", fw), OptStr(e, "fromPort"), Str(e, "toRole", fw), OptStr(e, "toPort"), NumF(e, "reach", fw, 4, 400)));
                }
            if (!Sets.TryAdd(id, new ModuleSet(id, items, Names(s, "missing")) { Feeds = feeds }))
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
        foreach (var s2 in Sets.Values)
            foreach (var fe in s2.Feeds)
                foreach (var from in fe.From)
                    if (!Rooms.ContainsKey(from))
                        throw new InteriorDataException($"ModuleSets.json set \"{s2.Id}\": a feed from an unknown room \"{from}\"");
        foreach (var c in Classes)
            foreach (var r in c.Fill.Concat(c.Chassis))
                if (!Rooms.TryGetValue(r, out var t))
                    throw new InteriorDataException($"SizeClasses.json class \"{c.Id}\": fill or chassis names an unknown room \"{r}\"");
                else if (t.System)
                    throw new InteriorDataException($"SizeClasses.json class \"{c.Id}\": fill or chassis names \"{r}\", a system room (spare, void, fuel and shafts are the planner's own)");
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
        var names = Shell.Values.Concat(Panels).Concat(FloorPanels).Concat(Adapters.Values).Concat(Sets.Values.SelectMany(s => s.Items).SelectMany(i => i.Parts)).Distinct();
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
