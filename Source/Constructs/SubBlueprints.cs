using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A use of another saved blueprint inside a blueprint (its "uses" list; docs/sandbox/design/sub-blueprints.md).</summary>
public sealed class UseSpec
{
    public string Id = "", Name = ""; // the sub-blueprint's id (decides) and name (a hint for messages)
    public int Version; // the version it was saved with (a lock: the one it builds; live: the newest is built)
    public bool Lock;
    public byte Turn; // the sub-blueprint's frame in the parent's: a Rot24 turn...
    public Vector3 At; // ...and where its step 0 stands (the parent's step-0 frame, on the 1 cm grid)
    public int After; // its steps come right after this parent step in the build order
    public List<(int Parent, string Socket, int Sub, string SubSocket)> On = new(); // the links joining it to what was built before it (Parent: a step, or SubBlueprints.UseRef)
    public List<StepOver> Over = new(); // paint and settings changed on this use, kept on top of the linked blueprint (the user's answer 4)
}

/// <summary>A use's override of one of its flat steps: paint, finish, setting and settings (null or -1: the blueprint's).</summary>
public sealed record StepOver(int Step, uint Paint, byte Finish, int Setting, string? Config);

/// <summary>One use in a flat blueprint or on a body: which blueprint, its frame, and the use it sits in.</summary>
public sealed class UseTag
{
    public string Id = "", Name = "";
    public int Version;
    public bool Lock;
    public byte Turn; // its frame in the body's (or the flat blueprint's) frame
    public Vector3 Origin;
    public int Parent = -1; // the use it sits in (-1: none)
    public uint Hash; // its parts as stamped (def, place in its own frame): a save compares (SubBlueprints.HashOf)
    public int First, Count; // a flat blueprint's: its steps (its inner uses' too), in order
    public ConstructBuild.Blueprint? Sub; // a flat blueprint's: the use's own flat blueprint, in its own frame (a sub-station builds it)

    public UseTag Clone() => (UseTag)MemberwiseClone();
}

/// <summary>A flat blueprint's uses: each step's innermost use, and each use (inner ones too).</summary>
public sealed class UseMap
{
    public int[] StepUse = []; // per flat step: its innermost use (an index into Tags), or -1
    public List<UseTag> Tags = new();

    public IEnumerable<int> Top()
    {
        for (var i = 0; i < Tags.Count; i++)
            if (Tags[i].Parent < 0)
                yield return i;
    }
}

/// <summary>
/// Sub-blueprints (docs/sandbox/design/sub-blueprints.md): ids and versions of the player's blueprints, their history, the "uses"
/// a blueprint names and their resolving into one ordinary flat blueprint, which every reader builds as it builds any. Pure
/// functions and a small cache; the files are read where the caller is (off the game thread for a stamp and a station's Start).
/// </summary>
public static class SubBlueprints
{
    public const int MaxDepth = 8; // uses inside uses (the user's answer 6)
    public const int MaxUses = 128; // in one blueprint's "uses"
    public const int MaxTreeUses = 1024; // in a whole tree
    public const int KeepVersions = 8; // the last this many old versions are kept, and any a lock names
    public const int CacheEntries = 64;
    public const string ShipPrefix = "ship:";
    public const string VersionsFolder = ".versions", ChecksFolder = ".checks", PacksFolder = "Packs"; // (subfolders of ConstructBlueprints: a list reads only its top)

    // ---- Step references: a step of a use, ["uN", k] in a file -------------------------------------------------------

    /// <summary>Step <paramref name="step"/> of use <paramref name="use"/> (0-based) as one int: -2 and below.</summary>
    public static int UseRef(int use, int step) => -2 - (use << 16 | step);

    public static bool IsUseRef(int r) => r <= -2;

    public static int RefUse(int r) => (-2 - r) >> 16;

    public static int RefStep(int r) => (-2 - r) & 0xFFFF;

    /// <summary>A step: a number, or ["uN", k] (use N, from 1, its step k).</summary>
    public static int ParseRef(JsonElement e, string file, int k)
    {
        if (e.ValueKind == JsonValueKind.Number)
            return e.GetInt32();
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2 && e[0].ValueKind == JsonValueKind.String && e[0].GetString() is { Length: >= 2 } u && u[0] == 'u'
            && int.TryParse(u.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= MaxUses && e[1].ValueKind == JsonValueKind.Number && e[1].GetInt32() is var s && s >= 0 && s < 0xFFFF)
            return UseRef(n - 1, s);
        throw new ConstructPartException($"{file}: {(k >= 0 ? $"steps[{k}]: " : "")}a step is a number or [\"uN\", step] (use N's step)");
    }

    /// <summary>A step reference as file text.</summary>
    public static string RefJson(int r) => IsUseRef(r) ? $"[\"u{RefUse(r) + 1}\", {RefStep(r)}]" : r.ToString(CultureInfo.InvariantCulture);

    public static string? IdRefusal(string id)
    {
        if (id.StartsWith(ShipPrefix, StringComparison.Ordinal))
            return id.Length > ShipPrefix.Length ? null : "a shipped id names its blueprint";
        if (id.Length != 16)
            return "16 hex digits";
        foreach (var ch in id)
            if (!char.IsAsciiHexDigitLower(ch) && !char.IsAsciiDigit(ch))
                return "16 hex digits";
        return null;
    }

    public static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    // ---- Parsing --------------------------------------------------------------------------------------------------------

    private static readonly string[] UseKeys = ["id", "name", "version", "lock", "turn", "at", "after", "on", "over"];

    public static void ParseUses(JsonElement e, string file, ConstructBuild.Blueprint bp)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw new ConstructPartException($"{file}: \"uses\" is a list");
        if (e.GetArrayLength() > MaxUses)
            throw new ConstructPartException($"{file}: {e.GetArrayLength()} uses: at most {MaxUses} in one blueprint");
        var i = 0;
        foreach (var u in e.EnumerateArray())
        {
            var where = $"{file}: uses[{i}]";
            if (u.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{where}: a use is an object");
            var spec = new UseSpec();
            foreach (var p in u.EnumerateObject())
                switch (p.Name)
                {
                    case "id": spec.Id = p.Value.GetString() ?? ""; break;
                    case "name": spec.Name = p.Value.GetString() ?? ""; break;
                    case "version": spec.Version = p.Value.GetInt32(); break;
                    case "lock": spec.Lock = p.Value.ValueKind == JsonValueKind.True; break;
                    case "turn":
                        spec.Turn = p.Value.GetInt32() is var t and >= 0 and < Rot24.Count ? (byte)t : throw new ConstructPartException($"{where}: \"turn\" is 0 to 23");
                        break;
                    case "at":
                        if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() != 3)
                            throw new ConstructPartException($"{where}: \"at\" is [x, y, z]");
                        spec.At = ConstructWorld.Quantise(new Vector3(p.Value[0].GetSingle(), p.Value[1].GetSingle(), p.Value[2].GetSingle()));
                        if (!float.IsFinite(spec.At.X + spec.At.Y + spec.At.Z) || spec.At.Length() > ConstructBuild.MaxWeldReach)
                            throw new ConstructPartException($"{where}: \"at\" is out of reach");
                        break;
                    case "after": spec.After = p.Value.GetInt32(); break;
                    case "on":
                        foreach (var l in p.Value.EnumerateArray())
                        {
                            if (l.ValueKind != JsonValueKind.Array || l.GetArrayLength() != 4)
                                throw new ConstructPartException($"{where}: \"on\" is a list of [step, its socket, sub step, sub socket]");
                            spec.On.Add((ParseRef(l[0], where, -1), l[1].GetString() ?? "", l[2].GetInt32(), l[3].GetString() ?? ""));
                        }
                        break;
                    case "over":
                        foreach (var o in p.Value.EnumerateArray())
                        {
                            int step = -1, setting = -1;
                            uint paint = 0;
                            byte finish = 0;
                            string? cfg = null;
                            foreach (var f in o.EnumerateObject())
                                switch (f.Name)
                                {
                                    case "step": step = f.Value.GetInt32(); break;
                                    case "paint" when f.Value.GetString() == "none": paint = PaintCleared; break;
                                    case "finish" when f.Value.GetString() == "none": finish = FinishCleared; break;
                                    case "paint":
                                        if (!ConstructPaint.TryParse(f.Value.GetString(), out paint))
                                            throw new ConstructPartException($"{where}: over: \"paint\" is a colour like \"#c04020\"");
                                        break;
                                    case "finish": finish = ConstructFinish.Of(f.Value.GetString()); break;
                                    case "setting": setting = f.Value.GetInt32(); break;
                                    case "config": cfg = f.Value.GetRawText(); break;
                                    default: throw new ConstructPartException($"{where}: over: unknown key \"{f.Name}\" (step, paint, finish, setting, config)");
                                }
                            if (step < 0)
                                throw new ConstructPartException($"{where}: over: \"step\" names one of its steps");
                            spec.Over.Add(new StepOver(step, paint, finish, setting, cfg));
                        }
                        break;
                    default: throw new ConstructPartException($"{where}: unknown key \"{p.Name}\" ({string.Join(", ", UseKeys)})");
                }
            if (IdRefusal(spec.Id) is { } bad)
                throw new ConstructPartException($"{where}: \"id\" is {bad}");
            bp.Uses.Add(spec);
            i++;
        }
    }

    public static Dictionary<string, Dictionary<int, string>> ParsePacked(JsonElement e, string file)
    {
        var d = new Dictionary<string, Dictionary<int, string>>();
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: \"packed\" is {{\"id\": {{\"version\": blueprint}}}}");
        foreach (var p in e.EnumerateObject())
        {
            var vs = d[p.Name] = new Dictionary<int, string>();
            foreach (var v in p.Value.EnumerateObject())
                vs[int.Parse(v.Name, CultureInfo.InvariantCulture)] = v.Value.GetRawText();
        }
        return d;
    }

    /// <summary>A step that names a use's step needs that use; the order is checked by the flattening.</summary>
    public static void CheckRefs(ConstructBuild.Blueprint bp, string file)
    {
        void Check(int r, string what)
        {
            if (IsUseRef(r) && RefUse(r) >= bp.Uses.Count)
                throw new ConstructPartException($"{file}: {what} names use {RefUse(r) + 1}, which isn't in \"uses\"");
        }
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            Check(bp.Steps[k].On, $"steps[{k}]");
            if (bp.StepAlso[k] is { } also)
                foreach (var a in also)
                    Check(a.On, $"steps[{k}]");
        }
        foreach (var (a, b) in bp.Wires)
        {
            Check(a, "a wire");
            Check(b, "a wire");
        }
        foreach (var (a, b) in bp.Hoses)
        {
            Check(a, "a hose");
            Check(b, "a hose");
        }
        foreach (var l in bp.Links)
        {
            Check(l.From, "a link");
            Check(l.To, "a link");
            foreach (var h in l.Hosts)
                Check(h, "a link");
        }
        for (var u = 0; u < bp.Uses.Count; u++)
            foreach (var o in bp.Uses[u].On)
                Check(o.Parent, $"uses[{u}]");
    }

    // ---- The library: ids, versions, history ----------------------------------------------------------------------------

    public sealed record LibEntry(string Name, string Path, string Id, int Version);

    private static readonly object Gate = new();
    private static Dictionary<string, LibEntry>? _index;
    private static long _indexKey;

    /// <summary>The player's blueprints by id (those that have one), worked out again when the folder changes.</summary>
    public static Dictionary<string, LibEntry> Index()
    {
        var folder = PlayerBlueprints.Folder;
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
        long key = files.Length;
        foreach (var f in files)
            key = key * 31 + File.GetLastWriteTimeUtc(f).Ticks + f.Length;
        lock (Gate)
            if (_index != null && key == _indexKey)
                return _index;
        var index = new Dictionary<string, LibEntry>();
        foreach (var f in files)
        {
            try
            {
                var (id, version) = IdOf(File.ReadAllText(f));
                if (id.Length > 0 && !index.ContainsKey(id))
                    index[id] = new LibEntry(Path.GetFileNameWithoutExtension(f), f, id, version);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
            }
        }
        lock (Gate)
        {
            _index = index;
            _indexKey = key;
        }
        return index;
    }

    /// <summary>A file's id and version ("", 0: none yet).</summary>
    public static (string Id, int Version) IdOf(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() ?? "" : "";
        var v = root.TryGetProperty("version", out var vv) && vv.ValueKind == JsonValueKind.Number ? vv.GetInt32() : 0;
        return (id, v);
    }

    /// <summary>A blueprint's text with its id and version set (written first, after the opening brace; an old pair is taken out).</summary>
    public static string WithId(string text, string id, int version)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        node.Remove("id");
        node.Remove("version");
        var copy = new System.Text.Json.Nodes.JsonObject();
        var first = true;
        foreach (var (k, v) in node.ToList())
        {
            node.Remove(k);
            copy[k] = v;
            if (first && k == "format")
            {
                copy["id"] = id;
                copy["version"] = version;
            }
            first = false;
        }
        if (!copy.ContainsKey("id"))
        {
            copy["id"] = id;
            copy["version"] = version;
        }
        return copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    /// The player's blueprint <paramref name="name"/> with an id (one is made and the file written again, atomically, the first
    /// time it is used as a sub-blueprint). Its id and version, or null and why.
    /// </summary>
    public static (string Id, int Version, string Text)? EnsureId(string name, out string why)
    {
        why = "";
        try
        {
            var path = PlayerBlueprints.PathOf(name);
            var text = File.ReadAllText(path);
            var (id, version) = IdOf(text);
            if (id.Length > 0 && version > 0)
                return (id, version, text);
            id = id.Length > 0 ? id : NewId();
            text = WithId(text, id, 1);
            if (PlayerBlueprints.WriteFile(path, text) is { } bad)
            {
                why = bad;
                return null;
            }
            return (id, 1, text);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            why = ex.Message;
            return null;
        }
    }

    private static string HistoryPath(string id, int version) => Path.Combine(PlayerBlueprints.Folder, VersionsFolder, id, version.ToString(CultureInfo.InvariantCulture) + ".json");

    /// <summary>
    /// A blueprint saved over one with an id: the old text goes to its history, the new one takes the id and the next version.
    /// Returns the text to write. Old versions past the last <see cref="KeepVersions"/> go unless a lock anywhere names them.
    /// </summary>
    public static string NextVersion(string name, string text)
    {
        try
        {
            var path = PlayerBlueprints.PathOf(name);
            if (!File.Exists(path))
                return text;
            var old = File.ReadAllText(path);
            var (id, version) = IdOf(old);
            if (id.Length == 0)
            {
                var (newId, _) = IdOf(text);
                return newId.Length > 0 ? text : text; // (an old file never used as a sub-blueprint: nothing to keep)
            }
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath(id, version))!);
            if (!File.Exists(HistoryPath(id, version)))
                SavePaths.WriteAtomic(HistoryPath(id, version), old);
            Prune(id, version + 1);
            return WithId(text, id, Math.Max(1, version) + 1);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[subbp] {name}: its old version wasn't kept: {ex.Message}");
            return text;
        }
    }

    /// <summary>Every version some blueprint's lock names, by id.</summary>
    public static HashSet<(string, int)> LockedVersions()
    {
        var set = new HashSet<(string, int)>();
        if (!Directory.Exists(PlayerBlueprints.Folder))
            return set;
        foreach (var f in Directory.GetFiles(PlayerBlueprints.Folder, "*.json"))
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("uses", out var uses) && uses.ValueKind == JsonValueKind.Array)
                    foreach (var u in uses.EnumerateArray())
                        if (u.TryGetProperty("lock", out var l) && l.ValueKind == JsonValueKind.True && u.TryGetProperty("id", out var id) && u.TryGetProperty("version", out var v))
                            set.Add((id.GetString() ?? "", v.GetInt32()));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        return set;
    }

    private static void Prune(string id, int newest)
    {
        var dir = Path.Combine(PlayerBlueprints.Folder, VersionsFolder, id);
        if (!Directory.Exists(dir))
            return;
        var locked = LockedVersions();
        foreach (var f in Directory.GetFiles(dir, "*.json"))
            if (int.TryParse(Path.GetFileNameWithoutExtension(f), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v <= newest - KeepVersions - 1 && !locked.Contains((id, v)))
            {
                try
                {
                    File.Delete(f);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
    }

    /// <summary>The versions of an id kept in its history, oldest first.</summary>
    public static List<int> History(string id)
    {
        var list = new List<int>();
        var dir = Path.Combine(PlayerBlueprints.Folder, VersionsFolder, id);
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.json"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(f), NumberStyles.None, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
        list.Sort();
        return list;
    }

    // ---- Resolving: a use's text ------------------------------------------------------------------------------------------

    /// <summary>Where the sub-blueprints come from: a pack's or an upload's own first, then the shipped ones, then this PC's.</summary>
    public sealed class Source
    {
        public Dictionary<string, Dictionary<int, string>>? Packed;
        public Func<string, ConstructBuild.Blueprint?>? Shipped; // (while the shipped list loads: its own lookup)
        public bool Library = true; // the player's folder (false: a shipped blueprint's tree, which may use shipped ones only)
        public readonly Dictionary<string, int> Resolved = new(); // what was built: id, version (packed again for a job or an upload)
        public readonly Dictionary<string, Dictionary<int, string>> Texts = new(); // ...and their texts
    }

    /// <summary>The text of a use's blueprint (null for a shipped one: <paramref name="shipped"/>), and the version it is. Null and why.</summary>
    private static string? TextOf(Source src, string id, int version, bool locked, string name, out int got, out ConstructBuild.Blueprint? shipped, out string why)
    {
        got = version;
        shipped = null;
        why = "";
        if (src.Packed != null && src.Packed.TryGetValue(id, out var pv) && pv.Count > 0)
        {
            if (locked && pv.TryGetValue(version, out var lt))
                return lt;
            if (!locked)
            {
                got = pv.Keys.Max();
                return pv[got];
            }
        }
        if (id.StartsWith(ShipPrefix, StringComparison.Ordinal))
        {
            var bpName = id[ShipPrefix.Length..];
            shipped = src.Shipped != null ? src.Shipped(bpName) : ConstructBuild.FindBlueprint(bpName);
            got = 0;
            if (shipped == null)
                why = $"the shipped blueprint {bpName} is not in this game";
            return null;
        }
        if (!src.Library)
        {
            why = $"a shipped blueprint may use shipped ones only ({name})";
            return null;
        }
        var index = Index();
        try
        {
            if (index.TryGetValue(id, out var cur))
            {
                if (!locked || cur.Version == version)
                {
                    got = cur.Version;
                    return File.ReadAllText(cur.Path);
                }
                if (File.Exists(HistoryPath(id, version)))
                    return File.ReadAllText(HistoryPath(id, version));
            }
            else if (locked && File.Exists(HistoryPath(id, version)))
                return File.ReadAllText(HistoryPath(id, version));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            why = ex.Message;
            return null;
        }
        why = $"{name} v{version} is not in this game: copy it in (a pack), or save the blueprint that uses it again";
        return null;
    }

    /// <summary>A sub-blueprint resolved, flattened and built once on a scratch world: what a use of it lays out.</summary>
    public sealed class SubEntry
    {
        public ConstructBuild.Blueprint Flat = null!;
        public Construct Body = null!;
        public int[] PartOf = [];
        public int Version;
        public string Why = "";
        public long Used;
        public double Ms;
        public uint Hash;
    }

    private static readonly Dictionary<string, SubEntry> Cache = new();
    private static long _clock;

    public static void ClearCache()
    {
        lock (Gate)
            Cache.Clear();
    }

    /// <summary>One version of a sub-blueprint, from this PC (its history for an old one), built: the save's view of a use.</summary>
    public static SubEntry Resolve(string id, int version, string name) =>
        Get(new Source(), new UseSpec { Id = id, Version = version, Lock = true, Name = name }, new List<string> { id });

    private static SubEntry Get(Source src, UseSpec use, List<string> stack)
    {
        var text = TextOf(src, use.Id, use.Version, use.Lock, use.Name.Length > 0 ? use.Name : use.Id, out var version, out var shipped, out var why);
        if (text == null && shipped == null)
            return new SubEntry { Why = why };
        var key = $"{use.Id}:{version}:{(text == null ? 0 : text.Length)}:{(text == null ? 0 : Fnv(text))}";
        lock (Gate)
            if (Cache.TryGetValue(key, out var hit))
            {
                hit.Used = ++_clock;
                if (hit.Why.Length == 0)
                    Remember(src, use.Id, version, text);
                return hit;
            }
        var e = new SubEntry { Version = version };
        var t0 = Stopwatch.GetTimestamp();
        try
        {
            var bp = shipped ?? ConstructBuild.ParseBlueprint(text!, (use.Name.Length > 0 ? use.Name : use.Id) + ".json");
            if (bp.Id.Length > 0 && bp.Id != use.Id && shipped == null)
                throw new ConstructPartException($"{use.Name}: its file has another id ({bp.Id})");
            stack.Add(use.Id);
            try
            {
                e.Flat = shipped ?? FlattenWith(bp, src, stack);
            }
            finally
            {
                stack.RemoveAt(stack.Count - 1);
            }
            var run = new ConstructBuild.BuildRun { StepsOnly = true };
            foreach (var _ in ConstructBuild.BuildSteps(new ConstructWorld(), e.Flat, Vector3.Zero, Quaternion.Identity, run))
            {
            }
            if (run.Body is not { } body)
                throw new ConstructPartException($"{use.Name} v{version} doesn't build: {run.Why}");
            if (body.Dirty)
                body.Rebuild();
            e.Body = body;
            e.PartOf = run.PartOf!;
            e.Hash = HashWhole(body); // (what a use of it hashes to, as built: a save tells a changed use by it)
        }
        catch (ConstructPartException ex)
        {
            e.Why = ex.Message;
        }
        catch (JsonException ex)
        {
            e.Why = $"{use.Name} v{version}: {ex.Message}";
        }
        e.Ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        if (e.Why.Length == 0)
            Remember(src, use.Id, version, text);
        if (stack.Count > 0 && e.Why.Contains("loop:", StringComparison.Ordinal))
            return e; // (a loop depends on the path that reached it: never kept)
        lock (Gate)
        {
            e.Used = ++_clock;
            Cache[key] = e;
            if (Cache.Count > CacheEntries)
            {
                var oldest = Cache.MinBy(kv => kv.Value.Used).Key;
                Cache.Remove(oldest);
            }
        }
        return e;
    }

    private static void Remember(Source src, string id, int version, string? text)
    {
        src.Resolved[id] = version;
        if (text != null)
        {
            if (!src.Texts.TryGetValue(id, out var vs))
                src.Texts[id] = vs = new Dictionary<int, string>();
            vs[version] = text;
        }
    }

    public static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    // ---- Flattening ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The blueprint with every use's steps as its own (the same blueprint when it has no uses): parent steps in order, each use's
    /// after the step its "after" names, renumbered and turned by its turn and place. Throws ConstructPartException with why.
    /// </summary>
    public static ConstructBuild.Blueprint Flatten(ConstructBuild.Blueprint bp, Source? src = null)
    {
        if (bp.Uses.Count == 0)
            return bp;
        src ??= new Source { Packed = bp.Packed };
        src.Packed ??= bp.Packed;
        var stack = new List<string>();
        if (bp.Id.Length > 0)
            stack.Add(bp.Id);
        return FlattenWith(bp, src, stack);
    }

    /// <summary><see cref="Flatten"/> of a blueprint's text (a file, a stamp's upload, a pack): parsed, then flattened.</summary>
    public static ConstructBuild.Blueprint ParseFlat(string text, string file, Source? src = null) => Flatten(ConstructBuild.ParseBlueprint(text, file), src);

    private static ConstructBuild.Blueprint FlattenWith(ConstructBuild.Blueprint bp, Source src, List<string> stack)
    {
        if (bp.Uses.Count == 0)
            return bp;
        if (stack.Count > MaxDepth)
            throw new ConstructPartException($"{bp.Name}: uses inside uses more than {MaxDepth} deep");
        var subs = new SubEntry[bp.Uses.Count];
        for (var u = 0; u < bp.Uses.Count; u++)
        {
            var use = bp.Uses[u];
            var at = stack.IndexOf(use.Id);
            if (at >= 0 || use.Id == bp.Id && bp.Id.Length > 0)
                throw new ConstructPartException($"loop: {string.Join(" contains ", stack.Skip(Math.Max(0, at)).Select(NameOf).Append(use.Name))}");
            subs[u] = Get(src, use, stack);
            if (subs[u].Why.Length > 0)
                throw new ConstructPartException(subs[u].Why);
        }
        var f = new ConstructBuild.Blueprint
        {
            Name = bp.Name, File = bp.File, Description = bp.Description, Group = bp.Group, ExactLinks = bp.ExactLinks, FrozenBase = bp.FrozenBase,
            Conduit = bp.Conduit, Tow = bp.Tow, Id = bp.Id, Version = bp.Version, Uses = bp.Uses, Packed = bp.Packed,
        };
        var map = new UseMap();
        var stepUse = new List<int>();
        var parentFlat = new int[bp.Steps.Count];
        Array.Fill(parentFlat, -1);
        var useFlat = new int[bp.Uses.Count][];
        var fuseBase = 0;
        foreach (var fu in bp.StepFuse)
            fuseBase = Math.Max(fuseBase, fu);
        for (var u = 0; u < bp.Uses.Count; u++)
            if (bp.Uses[u].After < 0 || bp.Uses[u].After >= bp.Steps.Count)
                throw new ConstructPartException($"{bp.Name}: uses[{u}] ({bp.Uses[u].Name}): \"after\" must name one of its steps");
        int MapRef(int r, string what)
        {
            var flat = IsUseRef(r) ? RefUse(r) < useFlat.Length && useFlat[RefUse(r)] is { } uf && RefStep(r) < uf.Length ? uf[RefStep(r)] : -1
                : r >= 0 && r < parentFlat.Length ? parentFlat[r] : -1;
            if (flat < 0)
                throw new ConstructPartException($"{bp.Name}: {what} names {(IsUseRef(r) ? $"use {RefUse(r) + 1}'s step {RefStep(r)}" : $"step {r}")}, which isn't built before it");
            return flat;
        }
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            var (part, on, sock, own, spin) = bp.Steps[k];
            var also = bp.StepAlso[k];
            parentFlat[k] = f.Steps.Count;
            Copy(bp, k, f, k == 0 ? -1 : MapRef(on, $"steps[{k}]"), sock, own, spin, bp.StepAt[k], bp.StepWeld[k],
                also?.Select(a => (MapRef(a.On, $"steps[{k}]"), a.Socket, a.Own)).ToArray(), bp.StepFuse[k]);
            stepUse.Add(-1);
            for (var u = 0; u < bp.Uses.Count; u++)
                if (bp.Uses[u].After == k)
                    EmitUse(bp, u, subs[u], f, map, stepUse, useFlat, ref fuseBase, MapRef);
        }
        // The lists, with their steps renumbered (a use's own, then the parent's: any may name a use's step).
        for (var u = 0; u < bp.Uses.Count; u++)
            AddLists(subs[u].Flat, f, useFlat[u], bp.Uses[u].Turn, bp.Uses[u].At, subs[u].Body, subs[u].PartOf);
        AddParentLists(bp, f, r => MapRef(r, "a list"));
        f.Map = map;
        map.StepUse = stepUse.ToArray();
        if (map.Tags.Count > MaxTreeUses)
            throw new ConstructPartException($"{bp.Name}: {map.Tags.Count} uses in its tree: at most {MaxTreeUses}");
        if (f.Steps.Count > Construct.MaxParts)
            throw new ConstructPartException($"{bp.Name}: {f.Steps.Count} parts with its uses: at most {Construct.MaxParts}");
        return f;
    }

    private static string NameOf(string id)
    {
        if (id.StartsWith(ShipPrefix, StringComparison.Ordinal))
            return id[ShipPrefix.Length..];
        return Index().TryGetValue(id, out var e) ? e.Name : id;
    }

    /// <summary>One step appended to a flat blueprint, its per-step fields from <paramref name="from"/>'s step <paramref name="k"/>.</summary>
    private static void Copy(ConstructBuild.Blueprint from, int k, ConstructBuild.Blueprint to, int on, string sock, string own, int spin, Vector3? at,
        (Vector3 Pos, Quaternion Rot)? weld, (int On, string Socket, string Own)[]? also, int fuse)
    {
        if (from.OrdersOnly.Contains(k))
            to.OrdersOnly.Add(to.Steps.Count);
        to.Steps.Add((from.Steps[k].Part, on, sock, own, spin));
        to.StepSettings.Add(from.StepSettings[k]);
        to.StepText.Add(from.StepText[k]);
        to.StepMedia.Add(from.StepMedia[k]);
        to.StepConfig.Add(from.StepConfig[k]);
        to.StepAt.Add(at);
        to.StepAlso.Add(also);
        to.StepPaint.Add(from.StepPaint[k]);
        to.StepFuse.Add(fuse);
        to.StepFinish.Add(from.StepFinish[k]);
        to.StepWeld.Add(weld);
    }

    private delegate int RefMapper(int r, string what);

    private static void EmitUse(ConstructBuild.Blueprint bp, int u, SubEntry sub, ConstructBuild.Blueprint f, UseMap map, List<int> stepUse, int[][] useFlat, ref int fuseBase, RefMapper mapRef)
    {
        var use = bp.Uses[u];
        var sf = sub.Flat;
        var body = sub.Body;
        var turn = use.Turn;
        var baseStep = f.Steps.Count;
        var flat = useFlat[u] = new int[sf.Steps.Count];
        for (var s = 0; s < flat.Length; s++)
            flat[s] = baseStep + s;
        // Its tag, then its own tags under it.
        var ti = map.Tags.Count;
        map.Tags.Add(new UseTag { Id = use.Id, Name = use.Name.Length > 0 ? use.Name : sf.Name, Version = sub.Version, Lock = use.Lock, Turn = turn, Origin = use.At, First = baseStep, Count = sf.Steps.Count, Hash = sub.Hash });
        var tagBase = map.Tags.Count;
        if (sf.Map != null)
            foreach (var st in sf.Map.Tags)
                map.Tags.Add(new UseTag
                {
                    Id = st.Id, Name = st.Name, Version = st.Version, Lock = st.Lock, Turn = Rot24.Compose(turn, st.Turn), Origin = use.At + Rot24.Apply(turn, st.Origin),
                    Parent = st.Parent < 0 ? ti : tagBase + st.Parent, First = baseStep + st.First, Count = st.Count, Sub = st.Sub, Hash = st.Hash,
                });
        // The joining links, by the sub step they end at.
        var joins = new List<(int, string, string)>?[sf.Steps.Count];
        foreach (var (parent, socket, subStep, subSocket) in use.On)
        {
            if (subStep < 0 || subStep >= sf.Steps.Count)
                throw new ConstructPartException($"{bp.Name}: uses[{u}] ({use.Name}): \"on\" names its step {subStep}, which it hasn't");
            (joins[subStep] ??= new()).Add((mapRef(parent, $"uses[{u}] ({use.Name})"), socket, subSocket));
        }
        var fuseTop = fuseBase;
        Span<byte> turns = stackalloc byte[4];
        for (var s = 0; s < sf.Steps.Count; s++)
        {
            var (part, on, sock, own, spin) = sf.Steps[s];
            var pk = sub.PartOf[s];
            var inBody = body.FrameOf(body.Parts[pk].Seg) == 0;
            Vector3? at = sf.StepAt[s] is { } a ? inBody ? ConstructWorld.Quantise(use.At + Rot24.Apply(turn, a)) : a : null;
            var weld = sf.StepWeld[s];
            var flatOn = s == 0 ? (joins[0] is { Count: > 0 } j0 ? j0[0].Item1 : 0) : baseStep + on;
            if (s == 0)
            {
                // Step 0 stands at the use's place, turned: no socket, its links from "on".
                (sock, own, at) = ("", "", use.At);
                spin = Rot24.Compose(turn, body.Parts[pk].Rot);
            }
            else if (weld is { } w)
            {
                var po = sub.PartOf[on];
                if (body.FrameOf(body.Parts[po].Seg) == 0)
                    weld = (use.At + Rot24.Apply(turn, w.Pos), Quaternion.Normalize(Rot24.Quat(turn) * w.Rot));
            }
            else if (sock.Length == 0)
                spin = inBody ? Rot24.Compose(turn, body.Parts[pk].Rot) : body.Parts[pk].Rot; // (it stands alone: its turn is its own)
            else if (inBody)
            {
                // A socket step: the same sockets, the turn that gives the part its place turned (a spin counts among the socket's turns).
                var po = sub.PartOf[on];
                var ts = ConstructMerge.SocketIndex(body, po, sock);
                var def = body.DefOf(pk);
                var ds = Array.FindIndex(def.Sockets, x => x.Name == own);
                if (ts < 0 || ds < 0)
                    throw new ConstructPartException($"{use.Name}: step {s}: its sockets aren't on the built parts");
                body.SocketAt(po, ts, out _, out var dir);
                var want = Rot24.Compose(turn, body.Parts[pk].Rot);
                var n = ConstructBuild.SpinTurns(def.Sockets[ds].Dir, Rot24.ApplyDir(turn, dir), turns);
                spin = -1;
                for (var i = 0; i < n; i++)
                    if (turns[i] == want)
                        spin = i;
                if (spin < 0)
                {
                    // (no socket turn gives it: it stands where it is, linked as the sub-blueprint linked it)
                    sock = own = "";
                    at = ConstructWorld.Quantise(use.At + Rot24.Apply(turn, body.Parts[pk].Pos));
                    spin = want;
                    (joins[s] ??= new()).Add((baseStep + on, sf.Steps[s].Socket, sf.Steps[s].Own));
                }
            }
            var also = sf.StepAlso[s]?.Select(x => (baseStep + x.On, x.Socket, x.Own)).ToList();
            if (joins[s] is { } js)
                (also ??= new()).AddRange(js);
            var fuse = sf.StepFuse[s] == 0 ? 0 : fuseBase + sf.StepFuse[s];
            fuseTop = Math.Max(fuseTop, fuse);
            Copy(sf, s, f, flatOn, sock, own, spin, at, weld, also?.ToArray(), fuse);
            var exact = ConstructBuild.Exact(sf, s);
            if (exact != f.ExactLinks)
                (f.FlipExact ??= new()).Add(f.Steps.Count - 1);
            stepUse.Add(sf.Map != null && sf.Map.StepUse[s] >= 0 ? tagBase + sf.Map.StepUse[s] : ti);
        }
        fuseBase = fuseTop;
        // Its overrides: paint and settings kept on this use (the user's answer 4).
        Apply(f, use.Over, o => o < flat.Length ? flat[o] : -1, $"{bp.Name}: {use.Name}");
        map.Tags[ti].Sub = WithOverrides(sf, use.Over, $"{bp.Name}: {use.Name}");
    }

    /// <summary>A use's lists into the flat blueprint, its steps renumbered and its points turned into the parent's frame.</summary>
    private static void AddLists(ConstructBuild.Blueprint sf, ConstructBuild.Blueprint f, int[] flat, byte turn, Vector3 at, Construct body, int[] partOf)
    {
        Vector3 P(Vector3 p) => at + Rot24.Apply(turn, p);
        bool InBody(int step) => step < 0 || step >= partOf.Length || body.FrameOf(body.Parts[partOf[step]].Seg) == 0;
        foreach (var (a, b) in sf.Wires)
            f.Wires.Add((flat[a], flat[b]));
        var linkBase = f.Links.Count;
        foreach (var l in sf.Links)
        {
            var c = new ConstructRoutes.LinkSpec
            {
                Type = l.Type, From = l.From >= 0 ? flat[l.From] : l.From, To = flat[l.To], FromPort = l.FromPort, ToPort = l.ToPort,
                Branch = l.Branch >= 0 ? linkBase + l.Branch : -1, BranchSeg = l.BranchSeg, At = l.Branch >= 0 ? P(l.At) : l.At, Paint = l.Paint, Laid = l.Laid,
                SizeW = l.SizeW, SizeH = l.SizeH,
            };
            for (var i = 0; i < l.Nodes.Count; i++)
                c.Nodes.Add(InBody(i < l.Hosts.Count ? l.Hosts[i] : -1) ? P(l.Nodes[i]) : l.Nodes[i]);
            foreach (var h in l.Hosts)
                c.Hosts.Add(flat[h]);
            f.Links.Add(c);
        }
        foreach (var (a, b) in sf.Hoses)
            f.Hoses.Add((flat[a], flat[b]));
        foreach (var cn in sf.Connectors)
            f.Connectors.Add(cn with { StepA = cn.StepA >= 0 ? flat[cn.StepA] : cn.StepA, StepB = cn.StepB >= 0 ? flat[cn.StepB] : cn.StepB });
        foreach (var nc in sf.NoCollide)
            f.NoCollide.Add(nc with { StepA = nc.StepA >= 0 ? flat[nc.StepA] : nc.StepA, StepB = nc.StepB >= 0 ? flat[nc.StepB] : nc.StepB });
        foreach (var t in sf.Tracks)
            f.Tracks.Add(t with { Wheels = t.Wheels.Select(w => flat[w]).ToArray() });
        foreach (var fj in sf.FrozenJoints)
            f.FrozenJoints.Add(fj >= 0 && fj < flat.Length ? flat[fj] : fj);
        foreach (var cg in sf.Cargo)
            f.Cargo.Add(cg with { At = P(cg.At), Turn = Rot24.Compose(turn, cg.Turn) });
        foreach (var (s, table) in sf.Loot)
            f.Loot.Add((flat[s], table));
        foreach (var st in sf.Stored)
            f.Stored.Add(st with { Step = flat[st.Step] });
        foreach (var (s, order) in sf.Bill)
            f.Bill.Add((flat[s], order));
    }

    private static void AddParentLists(ConstructBuild.Blueprint bp, ConstructBuild.Blueprint f, Func<int, int> m)
    {
        foreach (var (a, b) in bp.Wires)
            f.Wires.Add((m(a), m(b)));
        var linkBase = f.Links.Count;
        foreach (var l in bp.Links)
        {
            var c = new ConstructRoutes.LinkSpec
            {
                Type = l.Type, From = l.Branch >= 0 ? l.From : m(l.From), To = m(l.To), FromPort = l.FromPort, ToPort = l.ToPort,
                Branch = l.Branch >= 0 ? linkBase + l.Branch : -1, BranchSeg = l.BranchSeg, At = l.At, Paint = l.Paint, Laid = l.Laid, SizeW = l.SizeW, SizeH = l.SizeH,
            };
            c.Nodes.AddRange(l.Nodes);
            foreach (var h in l.Hosts)
                c.Hosts.Add(m(h));
            f.Links.Add(c);
        }
        foreach (var (a, b) in bp.Hoses)
            f.Hoses.Add((m(a), m(b)));
        foreach (var cn in bp.Connectors)
            f.Connectors.Add(cn with { StepA = cn.StepA >= 0 ? m(cn.StepA) : cn.StepA, StepB = cn.StepB >= 0 ? m(cn.StepB) : cn.StepB });
        foreach (var nc in bp.NoCollide)
            f.NoCollide.Add(nc with { StepA = nc.StepA >= 0 ? m(nc.StepA) : nc.StepA, StepB = nc.StepB >= 0 ? m(nc.StepB) : nc.StepB });
        foreach (var t in bp.Tracks)
            f.Tracks.Add(t with { Wheels = t.Wheels.Select(m).ToArray() });
        foreach (var fj in bp.FrozenJoints)
            f.FrozenJoints.Add(m(fj));
        f.Cargo.AddRange(bp.Cargo);
        foreach (var (s, table) in bp.Loot)
            f.Loot.Add((m(s), table));
        foreach (var st in bp.Stored)
            f.Stored.Add(st with { Step = m(st.Step) });
        foreach (var (s, order) in bp.Bill)
            f.Bill.Add((m(s), order));
    }

    // ---- Tags on bodies ------------------------------------------------------------------------------------------------

    /// <summary>A flat blueprint's step fitted: its part carries its use (the body takes the blueprint's table with its first tagged part).</summary>
    public static void TagPart(Construct c, UseMap map, int k, int part)
    {
        if (k >= map.StepUse.Length || map.StepUse[k] < 0 || part < 0 || part >= c.PartCount)
            return;
        EnsureTable(c, map);
        c.Parts[part].Use = (ushort)(c.UsesIndex![map.StepUse[k]] + 1);
    }

    /// <summary>
    /// The body holds the flat blueprint's table: each of its tags found among the body's own (the same use: blueprint, version, lock,
    /// frame and outer use; a world loaded mid-build has them, and its later parts tag into them), or added after them.
    /// </summary>
    public static void EnsureTable(Construct c, UseMap map)
    {
        if (c.UsesFrom == map && c.UsesIndex is { } known && known.Length == map.Tags.Count)
            return;
        c.Uses ??= new List<UseTag>();
        var index = new int[map.Tags.Count];
        var claimed = new bool[c.Uses.Count];
        for (var i = 0; i < map.Tags.Count; i++)
        {
            var t = map.Tags[i];
            var parent = t.Parent >= 0 && t.Parent < i ? index[t.Parent] : -1;
            index[i] = -1;
            for (var e = 0; e < claimed.Length && index[i] < 0; e++)
            {
                var have = c.Uses[e];
                if (!claimed[e] && have.Id == t.Id && have.Version == t.Version && have.Lock == t.Lock && have.Turn == t.Turn && have.Parent == parent
                    && Vector3.DistanceSquared(have.Origin, t.Origin) < 0.0001f)
                {
                    claimed[e] = true;
                    index[i] = e;
                }
            }
            if (index[i] >= 0)
                continue;
            var copy = t.Clone();
            copy.Parent = parent;
            index[i] = c.Uses.Count;
            c.Uses.Add(copy);
        }
        c.UsesIndex = index;
        c.UsesFrom = map;
    }

    /// <summary>A stamp's clip: the whole of it is one use of the stamped blueprint, its own uses under it; every tag's parts hashed.</summary>
    public static void TagStamp(Construct body, ConstructBuild.Blueprint flat, string? id = null)
    {
        id = id is { Length: > 0 } ? id : flat.Id; // (a shipped sub-blueprint's flat form carries no id: its use's "ship:<name>" is given)
        if (id.Length == 0)
            return; // (a blueprint without an id yet: stamped as plain parts)
        body.Uses ??= new List<UseTag>();
        var root = body.Uses.Count;
        foreach (var t in body.Uses)
            if (t.Parent < 0)
                t.Parent = root;
        body.Uses.Add(new UseTag { Id = id, Name = flat.Name, Version = flat.Version, Turn = 0, Origin = Vector3.Zero });
        for (var i = 0; i < body.PartCount; i++)
            if (body.Parts[i].Use == 0)
                body.Parts[i].Use = (ushort)(root + 1);
        body.UsesFrom = null;
        HashAll(body);
    }

    /// <summary>Every tag's hash as its parts are now.</summary>
    public static void HashAll(Construct c)
    {
        if (c.Uses == null)
            return;
        for (var t = 0; t < c.Uses.Count; t++)
            c.Uses[t].Hash = HashOf(c, t);
    }

    /// <summary>Is tag <paramref name="t"/> or one of its outer uses the part's use?</summary>
    public static bool In(Construct c, int part, int t)
    {
        var u = c.Parts[part].Use - 1;
        for (var guard = 0; u >= 0 && guard <= MaxTreeUses; guard++)
        {
            if (u == t)
                return true;
            u = u < c.Uses!.Count ? c.Uses[u].Parent : -1;
        }
        return false;
    }

    /// <summary>
    /// A use's parts as they are, in its own frame (part, place on the 1 cm grid, turn; a part on a freeform weld's frame as it is
    /// there), added up so the order doesn't matter. Paint and settings aren't in it: they are a use's own (the user's answer 4).
    /// </summary>
    public static uint HashOf(Construct c, int t)
    {
        var tag = c.Uses![t];
        var inv = Inverse(tag.Turn);
        uint sum = 0, n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (!In(c, i, t))
                continue;
            ref var p = ref c.Parts[i];
            Vector3 pos;
            byte rot;
            if (c.FrameOf(p.Seg) == 0)
            {
                pos = ConstructWorld.Quantise(Rot24.Apply(inv, p.Pos - tag.Origin));
                rot = Rot24.Compose(inv, p.Rot);
            }
            else
                (pos, rot) = (ConstructWorld.Quantise(p.Pos), p.Rot);
            var h = Fnv(c.DefOf(i).Name);
            h = (h ^ (uint)(int)MathF.Round(pos.X * 100)) * 16777619u;
            h = (h ^ (uint)(int)MathF.Round(pos.Y * 100)) * 16777619u;
            h = (h ^ (uint)(int)MathF.Round(pos.Z * 100)) * 16777619u;
            h = (h ^ rot) * 16777619u;
            sum += h * 2654435761u;
            n++;
        }
        return sum ^ n * 40503u;
    }

    /// <summary>A whole body as one use in its own frame (a sub-blueprint's own build): <see cref="HashOf"/>'s sum over every part.</summary>
    public static uint HashWhole(Construct c)
    {
        c.Uses ??= new List<UseTag>();
        var saved = (c.Uses, c.UsesFrom, c.UsesIndex);
        var parts = new ushort[c.PartCount];
        for (var i = 0; i < c.PartCount; i++)
        {
            parts[i] = c.Parts[i].Use;
            c.Parts[i].Use = 1;
        }
        c.Uses = [new UseTag()];
        var h = HashOf(c, 0);
        (c.Uses, c.UsesFrom, c.UsesIndex) = saved;
        for (var i = 0; i < c.PartCount; i++)
            c.Parts[i].Use = parts[i];
        if (c.Uses.Count == 0)
            c.Uses = null;
        return h;
    }

    /// <summary>The turn that undoes <paramref name="r"/>.</summary>
    public static byte Inverse(byte r)
    {
        for (byte k = 0; k < Rot24.Count; k++)
            if (Rot24.Compose(k, r) == 0)
                return k;
        return 0;
    }

    /// <summary>A copy's tags: a new body made of some of a body's parts takes the whole table (an unused tag costs nothing and goes at a save).</summary>
    public static void CopyTable(Construct from, Construct to)
    {
        if (from.Uses == null)
            return;
        to.Uses = from.Uses.Select(t => t.Clone()).ToList();
        to.UsesFrom = null;
    }

    /// <summary>A body merged in (<see cref="ConstructWorld.Merge"/>, a freeform weld): its tags after this one's, turned into its frame.</summary>
    public static void MergeTable(Construct a, Construct b, int partOff, int partCount, byte turn, Vector3 offset, bool moved)
    {
        if (b.Uses == null || b.Uses.Count == 0)
        {
            for (var i = partOff; i < partOff + partCount; i++)
                a.Parts[i].Use = 0;
            return;
        }
        a.Uses ??= new List<UseTag>();
        var tagOff = a.Uses.Count;
        foreach (var t in b.Uses)
        {
            var c = t.Clone();
            if (moved)
            {
                c.Origin = offset + Rot24.Apply(turn, c.Origin);
                c.Turn = Rot24.Compose(turn, c.Turn);
            }
            if (c.Parent >= 0)
                c.Parent += tagOff;
            a.Uses.Add(c);
        }
        for (var i = partOff; i < partOff + partCount; i++)
            if (a.Parts[i].Use != 0)
                a.Parts[i].Use = (ushort)(a.Parts[i].Use + tagOff);
        // (appending keeps the indices of a's own tags: a build's table on it stays valid)
        if (a.Uses.Count > MaxTreeUses * 4)
            Compact(a);
    }

    /// <summary>The table with only the tags some part (or an inner tag's part) names, renumbered.</summary>
    public static void Compact(Construct c)
    {
        if (c.Uses == null)
            return;
        var keep = new bool[c.Uses.Count];
        if (c.UsesFrom != null && c.UsesIndex != null)
            foreach (var t in c.UsesIndex)
                if (t >= 0 && t < keep.Length)
                    keep[t] = true; // (a build's table stays whole: its later parts tag into it)
        for (var i = 0; i < c.PartCount; i++)
            for (int u = c.Parts[i].Use - 1, g = 0; u >= 0 && u < keep.Length && !keep[u] && g < 64; g++)
            {
                keep[u] = true;
                u = c.Uses[u].Parent;
            }
        var map = new int[c.Uses.Count];
        var list = new List<UseTag>();
        for (var t = 0; t < keep.Length; t++)
        {
            map[t] = keep[t] ? list.Count : -1;
            if (keep[t])
                list.Add(c.Uses[t]);
        }
        foreach (var t in list)
            t.Parent = t.Parent >= 0 && t.Parent < map.Length ? map[t.Parent] : -1;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Use != 0)
                c.Parts[i].Use = (ushort)(c.Parts[i].Use - 1 < map.Length && map[c.Parts[i].Use - 1] >= 0 ? map[c.Parts[i].Use - 1] + 1 : 0);
        c.Uses = list.Count > 0 ? list : null;
        if (c.UsesFrom != null && c.UsesIndex != null && c.UsesIndex.All(t => t >= 0 && t < map.Length && map[t] >= 0))
            for (var i = 0; i < c.UsesIndex.Length; i++)
                c.UsesIndex[i] = map[c.UsesIndex[i]];
        else
            (c.UsesFrom, c.UsesIndex) = (null, null);
    }

    /// <summary>A part's use as the look line says it: "part of Turret #2 (v5, live)", or "".</summary>
    public static string LookOf(Construct c, int part)
    {
        if (c.Uses == null || part < 0 || part >= c.PartCount || c.Parts[part].Use == 0 || c.Parts[part].Use > c.Uses.Count)
            return "";
        var t = c.Uses[c.Parts[part].Use - 1];
        var n = 0;
        for (var k = 0; k <= c.Parts[part].Use - 1; k++)
            if (c.Uses[k].Id == t.Id)
                n++;
        var changed = HashOf(c, c.Parts[part].Use - 1) != t.Hash;
        return $"part of {t.Name} #{n} ({(t.Version > 0 ? $"v{t.Version}, " : "")}{(t.Lock ? "locked" : "live")}{(changed ? ", changed" : "")})";
    }

    // ---- The wire and saves: a body's table ---------------------------------------------------------------------------

    /// <summary>A body's tags (at the end of its Full: a Patch carries a change), then runs of parts with one use.</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        var n = c.Uses?.Count ?? 0;
        w.U16((ushort)Math.Min(n, MaxTreeUses * 4));
        for (var t = 0; t < n && t < MaxTreeUses * 4; t++)
        {
            var u = c.Uses![t];
            w.Str(u.Id).Str(u.Name).U16((ushort)Math.Clamp(u.Version, 0, ushort.MaxValue)).U8((byte)((u.Lock ? 1 : 0))).U8(u.Turn).F32(u.Origin.X).F32(u.Origin.Y).F32(u.Origin.Z).U16((ushort)(u.Parent + 1)).U32(u.Hash);
        }
        if (n == 0)
            return;
        var at = w.Length;
        w.U16(0);
        var runs = 0;
        for (var i = 0; i < c.PartCount;)
        {
            var use = c.Parts[i].Use;
            var j = i;
            while (j < c.PartCount && c.Parts[j].Use == use && j - i < ushort.MaxValue)
                j++;
            if (use != 0)
            {
                w.U16((ushort)i).U16((ushort)(j - i)).U16(use);
                runs++;
            }
            i = j;
        }
        w.PatchU16(at, (ushort)runs);
    }

    public static void Read(ref PacketReader r, Construct c)
    {
        if (r.Remaining < 2)
            return; // (an older Full)
        int n = r.U16();
        List<UseTag>? list = n > 0 ? new List<UseTag>(n) : null;
        for (var t = 0; t < n; t++)
            list!.Add(new UseTag { Id = r.Str(), Name = r.Str(), Version = r.U16(), Lock = (r.U8() & 1) != 0, Turn = (byte)(r.U8() % Rot24.Count), Origin = new Vector3(r.F32(), r.F32(), r.F32()), Parent = r.U16() - 1, Hash = r.U32() });
        c.Uses = list;
        c.UsesFrom = null;
        for (var i = 0; i < c.PartCount; i++)
            c.Parts[i].Use = 0;
        if (n == 0)
            return;
        int runs = r.U16();
        for (var k = 0; k < runs; k++)
        {
            int start = r.U16(), count = r.U16(), use = r.U16();
            for (var i = start; i < start + count && i < c.PartCount; i++)
                c.Parts[i].Use = (ushort)(use <= n ? use : 0);
        }
    }

    /// <summary>A body's tags for a save: the table (only tags in use) and its parts' uses as runs ("USES" block, WorldSaveBodies.cs).</summary>
    public static byte[]? SaveBytes(Construct c)
    {
        if (c.Uses == null || c.Uses.Count == 0)
            return null;
        Compact(c);
        if (c.Uses == null)
            return null;
        var w = new PacketWriter(64 + c.Uses.Count * 96 + c.PartCount * 6);
        w.Begin((Msg)0);
        Write(w, c);
        return w.Span[1..].ToArray();
    }

    public static void LoadBytes(Construct c, byte[] bytes)
    {
        var r = new PacketReader(bytes);
        Read(ref r, c);
    }

    // ---- Packs and the stamp's upload ----------------------------------------------------------------------------------

    /// <summary>
    /// A blueprint's text with every version its tree was built from carried along ("packed"), so another PC (the host of a stamp,
    /// a friend's game) builds what this one did. Each live use's version is the one built: a pack builds the same tree anywhere.
    /// </summary>
    public static string Pack(string text, Source src)
    {
        if (src.Texts.Count == 0)
            return text;
        var node = System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        var packed = new System.Text.Json.Nodes.JsonObject();
        foreach (var (id, vs) in src.Texts)
        {
            var o = new System.Text.Json.Nodes.JsonObject();
            foreach (var (v, t) in vs)
            {
                var sub = System.Text.Json.Nodes.JsonNode.Parse(t, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
                sub.Remove("packed"); // (one level: every version is here already)
                o[v.ToString(CultureInfo.InvariantCulture)] = sub;
            }
            packed[id] = o;
        }
        node.Remove("packed");
        node["packed"] = packed;
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>A blueprint's text made a pack: every version its tree names, read from this PC. Null and why.</summary>
    public static string? PackOf(string text, string file, out string why)
    {
        why = "";
        try
        {
            var src = new Source();
            _ = ParseFlat(text, file, src);
            return Pack(text, src);
        }
        catch (ConstructPartException ex)
        {
            why = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Packs dropped in the blueprints folder ("NAME.pack.json") unpacked: each sub-blueprint lands in its id's history, one whose id
    /// isn't here becomes a live blueprint under its name (a name in use gets " (2)"), and the pack becomes its blueprint. The lines say what happened.
    /// </summary>
    public static List<string> Unpack()
    {
        var said = new List<string>();
        var folder = PlayerBlueprints.Folder;
        if (!Directory.Exists(folder))
            return said;
        foreach (var file in Directory.GetFiles(folder, "*.pack.json"))
        {
            try
            {
                var text = File.ReadAllText(file);
                var node = System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
                if (node["packed"] is System.Text.Json.Nodes.JsonObject packed)
                {
                    var index = Index();
                    foreach (var (id, vsNode) in packed)
                    {
                        if (IdRefusal(id) != null || id.StartsWith(ShipPrefix, StringComparison.Ordinal) || vsNode is not System.Text.Json.Nodes.JsonObject vs)
                            continue;
                        var newest = 0;
                        string newestText = "";
                        foreach (var (vk, sub) in vs)
                        {
                            if (!int.TryParse(vk, NumberStyles.None, CultureInfo.InvariantCulture, out var v) || sub == null)
                                continue;
                            var subText = sub.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
                            if (v > newest)
                                (newest, newestText) = (v, subText);
                            if (index.TryGetValue(id, out var have) && have.Version == v)
                                continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath(id, v))!);
                            if (!File.Exists(HistoryPath(id, v)))
                                SavePaths.WriteAtomic(HistoryPath(id, v), subText);
                        }
                        if (!index.ContainsKey(id) && newest > 0)
                        {
                            var name = sub0Name(newestText, id);
                            var free = name;
                            for (var n = 2; PlayerBlueprints.Exists(free); n++)
                                free = $"{name} ({n})";
                            PlayerBlueprints.Write(free, WithId(newestText, id, newest));
                            said.Add($"{free} came in a pack");
                        }
                    }
                    node.Remove("packed");
                }
                var own = Path.GetFileName(file)[..^".pack.json".Length];
                var target = own;
                for (var n = 2; PlayerBlueprints.Exists(target); n++)
                    target = $"{own} ({n})";
                if (PlayerBlueprints.NameRefusal(target) == null && PlayerBlueprints.Write(target, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n") == null)
                {
                    File.Delete(file);
                    said.Add($"pack {own} unpacked as {target}");
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                said.Add($"pack {Path.GetFileName(file)} couldn't be read: {ex.Message}");
            }
        }
        if (said.Count > 0)
            foreach (var s in said)
                Console.WriteLine($"[subbp] {s}");
        return said;

        static string sub0Name(string text, string id)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var n = doc.RootElement.TryGetProperty("name", out var nn) ? nn.GetString() ?? id : id;
                return PlayerBlueprints.NameRefusal(n) == null ? n : id;
            }
            catch (JsonException)
            {
                return id;
            }
        }
    }

    // ---- Broken: does it still build? -----------------------------------------------------------------------------------

    /// <summary>
    /// Why a player's blueprint with uses no longer builds ("" when it does): resolved, flattened and built in a scratch world. Kept
    /// in the folder's ".checks" by the hash of every (id, version) in its tree, so a list shows red without building again.
    /// </summary>
    public static string BrokenWhy(string text, string file)
    {
        var src = new Source();
        ConstructBuild.Blueprint flat;
        try
        {
            flat = ParseFlat(text, file, src);
        }
        catch (Exception ex) when (ex is ConstructPartException or JsonException)
        {
            return ex.Message;
        }
        var key = Fnv(text) ^ Fnv(string.Join(";", src.Resolved.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))) * 31u;
        var path = Path.Combine(PlayerBlueprints.Folder, ChecksFolder, key.ToString("x8", CultureInfo.InvariantCulture) + ".txt");
        try
        {
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        catch (IOException)
        {
        }
        ConstructBuild.Build(new ConstructWorld(), flat, Vector3.Zero, Quaternion.Identity, out var why);
        var built = ConstructBuild.Build(new ConstructWorld(), flat, Vector3.Zero, Quaternion.Identity, out why) != null;
        var result = built ? "" : why;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SavePaths.WriteAtomic(path, result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return result;
    }

    /// <summary>Why a player's blueprint can't be read or flattened (the station panel's line), or null.</summary>
    public static string? WhyNot(string name)
    {
        try
        {
            _ = ParseFlat(File.ReadAllText(PlayerBlueprints.PathOf(name)), name);
            return null;
        }
        catch (Exception ex) when (ex is ConstructPartException or IOException or UnauthorizedAccessException or JsonException)
        {
            return ex.Message;
        }
    }

    /// <summary>The player's blueprints that use <paramref name="id"/> live (a new version reaches them all).</summary>
    public static List<string> UsedLiveBy(string id)
    {
        var list = new List<string>();
        if (!Directory.Exists(PlayerBlueprints.Folder))
            return list;
        foreach (var f in Directory.GetFiles(PlayerBlueprints.Folder, "*.json"))
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("uses", out var uses) && uses.ValueKind == JsonValueKind.Array
                    && uses.EnumerateArray().Any(u => u.TryGetProperty("id", out var i) && i.GetString() == id && !(u.TryGetProperty("lock", out var l) && l.ValueKind == JsonValueKind.True)))
                    list.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        return list;
    }

    /// <summary>The uses of a file as a short line: "uses: Turret x4 (v5 live), Engine Pod x2 (v3 locked)", or "".</summary>
    public static string UsesLine(ConstructBuild.Blueprint bp)
    {
        if (bp.Uses.Count == 0)
            return "";
        var parts = bp.Uses.GroupBy(u => (u.Id, u.Version, u.Lock)).Select(g => $"{g.First().Name} x{g.Count()} (v{g.Key.Version} {(g.Key.Lock ? "locked" : "live")})");
        return "uses: " + string.Join(", ", parts);
    }

    /// <summary>A file's uses switched live or locked (the Blueprints tab's toggles): written as a new version. Null or why not.</summary>
    public static string? SetLock(string name, int use, bool locked)
    {
        try
        {
            var path = PlayerBlueprints.PathOf(name);
            var text = File.ReadAllText(path);
            var node = System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
            if (node["uses"] is not System.Text.Json.Nodes.JsonArray uses || use < 0 || use >= uses.Count || uses[use] is not System.Text.Json.Nodes.JsonObject u)
                return "no such use";
            var id = u["id"]?.GetValue<string>() ?? "";
            var group = uses.OfType<System.Text.Json.Nodes.JsonObject>().Where(x => x["id"]?.GetValue<string>() == id).ToList();
            var version = locked ? u["version"]?.GetValue<int>() ?? 1 : Index().TryGetValue(id, out var cur) ? cur.Version : u["version"]?.GetValue<int>() ?? 1;
            foreach (var x in group)
            {
                x["lock"] = locked;
                x["version"] = version;
            }
            var next = NextVersion(name, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return PlayerBlueprints.Write(name, next);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return ex.Message;
        }
    }

    // ---- The check command (--sub-blueprint NAME) ----------------------------------------------------------------------

    /// <summary>
    /// --sub-blueprint NAME|FILE [--tree-size]: the tree, then the flat build against a build that merges each use's own built body
    /// at its place (every part within 1 mm): the proof that the final join gives the same ship. Exit code: the problems found.
    /// </summary>
    public static int Cli(string nameOrFile, string[] args)
    {
        if (args.Any(a => a.Equals("--tree-size", StringComparison.OrdinalIgnoreCase)))
            return TreeSize(nameOrFile);
        var roundTrip = args.Any(a => a.Equals("--roundtrip", StringComparison.OrdinalIgnoreCase));
        string text, file;
        try
        {
            (text, file) = File.Exists(nameOrFile) ? (File.ReadAllText(nameOrFile), Path.GetFileName(nameOrFile)) : (File.ReadAllText(PlayerBlueprints.PathOf(nameOrFile)), nameOrFile + ".json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"sub-blueprint: {ex.Message}");
            return 1;
        }
        var t0 = Stopwatch.GetTimestamp();
        ConstructBuild.Blueprint flat;
        try
        {
            flat = ParseFlat(text, file);
        }
        catch (Exception ex) when (ex is ConstructPartException or JsonException)
        {
            Console.WriteLine($"sub-blueprint: BROKEN: {ex.Message}");
            return 1;
        }
        var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"{flat.Name}: {flat.Steps.Count} flat steps, {flat.Map?.Tags.Count ?? 0} uses in its tree; resolved and flattened in {ms:0.0} ms");
        if (flat.Map != null)
            foreach (var (t, depth) in TreeOrder(flat.Map))
                Console.WriteLine($"  {new string(' ', depth * 2)}{flat.Map.Tags[t].Name} v{flat.Map.Tags[t].Version} {(flat.Map.Tags[t].Lock ? "locked" : "live")}: steps {flat.Map.Tags[t].First}-{flat.Map.Tags[t].First + flat.Map.Tags[t].Count - 1}, turn {flat.Map.Tags[t].Turn}, at {flat.Map.Tags[t].Origin}");
        var built = ConstructBuild.Build(new ConstructWorld(), flat, Vector3.Zero, Quaternion.Identity, out var why);
        if (built == null)
        {
            Console.WriteLine($"sub-blueprint: BROKEN: the flat build refuses: {why}");
            return 1;
        }
        var bad = flat.Map == null ? 0 : JoinCheck(flat, built);
        Console.WriteLine(bad == 0 ? "join check: every use's own build merged at its place gives the flat build's parts (within 1 mm)" : $"join check: {bad} problems");
        if (roundTrip)
            bad += RoundTrip(flat, built);
        return bad;
    }

    /// <summary>
    /// --roundtrip: the built ship saved again as the save window saves it (its uses kept as uses), read back and built: the same parts
    /// at the same places. Then the same as a stamp (one use of the whole): its inner uses stay uses. Then one part of a use off: that use
    /// goes plain (parts only, the user's answer 4) and the save still builds back whole.
    /// </summary>
    private static int RoundTrip(ConstructBuild.Blueprint flat, Construct built)
    {
        var bad = 0;
        foreach (var mode in new[] { "station build", "stamp", "a part off" })
        {
            var clip = ConstructClip.Of(built, flat.Name);
            if (mode == "stamp")
                TagStamp(clip.Body, flat);
            string? text;
            string why;
            ClipUses? plan;
            if (mode == "a part off")
            {
                var items = Enumerable.Range(0, clip.Body.PartCount).Where(i => i != clip.Body.PartCount - 1).Select(i => (ushort)i).ToArray();
                var cut = ConstructClip.Capture(clip.Body, items, out why, clip.World);
                if (cut == null)
                {
                    Console.WriteLine($"round trip ({mode}): the capture refuses: {why}");
                    bad++;
                    continue;
                }
                clip = cut;
            }
            plan = ClipUses.Plan(clip);
            text = clip.ToBlueprint(flat.Name + " again", "", out why, plan);
            if (text == null)
            {
                Console.WriteLine($"round trip ({mode}): the save refuses: {why}");
                bad++;
                continue;
            }
            foreach (var row in plan?.Rows ?? [])
                Console.WriteLine($"round trip ({mode}): {row.Name} (tag {row.Tag}): {(row.Kept ? $"kept as a use, {row.Parts} parts" : row.Why)}");
            try
            {
                var again = ParseFlat(text, "again.json");
                var back = ConstructBuild.Build(new ConstructWorld(), again, Vector3.Zero, Quaternion.Identity, out why);
                if (back == null)
                {
                    Console.WriteLine($"round trip ({mode}): it doesn't build back: {why}");
                    bad++;
                    continue;
                }
                var want = clip.Body;
                var differ = Differ(want, back);
                var painted = 0;
                for (var i = 0; i < back.PartCount; i++)
                    painted += back.Parts[i].Paint != 0 ? 1 : 0;
                Console.WriteLine($"round trip ({mode}): {again.Uses.Sum(u => u.Over.Count)} overrides saved, {painted} painted parts built back");
                Console.WriteLine($"round trip ({mode}): {again.Uses.Count} uses saved, {again.Steps.Count} flat steps; built back {back.PartCount} of {want.PartCount} parts{(differ.Length > 0 ? ": DIFFERS: " + differ : ", every part in place")}");
                if (differ.Length > 0 || back.PartCount != want.PartCount)
                    bad++;
            }
            catch (ConstructPartException ex)
            {
                Console.WriteLine($"round trip ({mode}): it doesn't read back: {ex.Message}");
                bad++;
            }
        }
        return bad;
    }

    /// <summary>The first part of <paramref name="a"/> with no part of the same kind, place (1 cm) and turn in <paramref name="b"/>, frames aligned at each one's lowest corner. "" when all match.</summary>
    private static string Differ(Construct a, Construct b)
    {
        Vector3 Lo(Construct c)
        {
            var lo = new Vector3(float.MaxValue);
            for (var i = 0; i < c.PartCount; i++)
                lo = Vector3.Min(lo, c.Parts[i].Pos);
            return lo;
        }
        var la = Lo(a);
        var lb = Lo(b);
        var have = new Dictionary<(string, int, int, int, byte), int>();
        for (var i = 0; i < b.PartCount; i++)
        {
            var p = b.Parts[i].Pos - lb;
            var key = (b.DefOf(i).Name, (int)MathF.Round(p.X * 100), (int)MathF.Round(p.Y * 100), (int)MathF.Round(p.Z * 100), b.Parts[i].Rot);
            have[key] = have.GetValueOrDefault(key) + 1;
        }
        for (var i = 0; i < a.PartCount; i++)
        {
            var p = a.Parts[i].Pos - la;
            var key = (a.DefOf(i).Name, (int)MathF.Round(p.X * 100), (int)MathF.Round(p.Y * 100), (int)MathF.Round(p.Z * 100), a.Parts[i].Rot);
            if (have.GetValueOrDefault(key) <= 0)
                return $"{a.DefOf(i).Name} at {p} turn {a.Parts[i].Rot}";
            have[key]--;
        }
        return "";
    }

    private static IEnumerable<(int Tag, int Depth)> TreeOrder(UseMap map)
    {
        IEnumerable<(int, int)> Under(int parent, int depth)
        {
            for (var t = 0; t < map.Tags.Count; t++)
                if (map.Tags[t].Parent == parent)
                {
                    yield return (t, depth);
                    foreach (var x in Under(t, depth + 1))
                        yield return x;
                }
        }
        return Under(-1, 0);
    }

    /// <summary>
    /// The parent's own steps built, then each top-level use built on its own world from its flat sub-blueprint and merged in at its
    /// place as the station's big fit merges it (ConstructWorld.Merge with the use's turn): every flat part must be there within 1 mm.
    /// </summary>
    private static int JoinCheck(ConstructBuild.Blueprint flat, Construct reference)
    {
        var bad = 0;
        var world = new ConstructWorld();
        var partOf = new int[flat.Steps.Count];
        Array.Fill(partOf, -1);
        var root = world.Create(Vector3.Zero, Quaternion.Identity)!;
        var tops = flat.Map!.Top().ToList();
        // The flat order: the parent's steps as they come, each use whole where it starts.
        for (var k = 0; k < flat.Steps.Count; k++)
        {
            var u = tops.FirstOrDefault(t => flat.Map.Tags[t].First == k, -1);
            if (u >= 0)
            {
                var tag = flat.Map.Tags[u];
                if (!SubBodyOf(flat, tag, out var sub, out var subPartOf, out var subWhy))
                {
                    Console.WriteLine($"  {tag.Name}: {subWhy}");
                    return 1;
                }
                var off = root.PartCount;
                var plan = new JoinPlan { Other = sub.Body, Turn = tag.Turn, Offset = tag.Origin };
                var j0 = Stopwatch.GetTimestamp();
                if (!JoinAt(world, root, sub, plan, flat, tag, subPartOf, partOf, out var jw))
                {
                    Console.WriteLine($"  {tag.Name}: the merge refuses: {jw}");
                    return 1;
                }
                var joinMs = (Stopwatch.GetTimestamp() - j0) * 1000.0 / Stopwatch.Frequency;
                if (tag.Count >= 100)
                    Console.WriteLine($"  the big fit of {tag.Name} ({tag.Count} parts into {off}): {joinMs:0.00} ms with the copy into this world, the merge alone {LastMergeMs:0.00} ms");
                k = tag.First + tag.Count - 1;
                continue;
            }
            if (ConstructBuild.FitStep(world, root, flat, k, partOf) is { } refused)
            {
                Console.WriteLine($"  step {k}: {refused}");
                return 1;
            }
        }
        if (root.PartCount != reference.PartCount)
        {
            Console.WriteLine($"  {root.PartCount} parts against the flat build's {reference.PartCount}");
            bad++;
        }
        for (var k = 0; k < flat.Steps.Count && bad < 10; k++)
        {
            int a = partOf[k];
            if (a < 0)
            {
                Console.WriteLine($"  step {k} ({flat.Steps[k].Part}) was not placed");
                bad++;
                continue;
            }
            // The reference's step k: the flat build places step k as its part k (one part a step).
            var rp = reference.Parts[k];
            var jp = root.Parts[a];
            if (reference.DefOf(k).Name != root.DefOf(a).Name || Vector3.Distance(rp.Pos, jp.Pos) > 0.001f || rp.Rot != jp.Rot)
            {
                Console.WriteLine($"  step {k} ({flat.Steps[k].Part}): {jp.Pos}/{jp.Rot} against {rp.Pos}/{rp.Rot}");
                bad++;
            }
        }
        return bad;
    }

    /// <summary>A top-level use's own flat sub-blueprint, built on a world of its own as a sub-station builds it (steps only). False and why.</summary>
    public static bool SubBodyOf(ConstructBuild.Blueprint flat, UseTag tag, out (ConstructWorld World, Construct Body) sub, out int[] partOf, out string why)
    {
        sub = default;
        partOf = [];
        var bp = SubFlat(flat, tag);
        var world = new ConstructWorld();
        var run = new ConstructBuild.BuildRun { StepsOnly = true };
        foreach (var _ in ConstructBuild.BuildSteps(world, bp, Vector3.Zero, Quaternion.Identity, run))
        {
        }
        why = run.Why;
        if (run.Body == null)
            return false;
        sub = (world, run.Body);
        partOf = run.PartOf!;
        return true;
    }

    /// <summary>A top-level use's own flat blueprint (its paint and settings overrides on it), in its own frame: what a sub-station builds.</summary>
    public static ConstructBuild.Blueprint SubFlat(ConstructBuild.Blueprint flat, UseTag tag) =>
        tag.Sub ?? throw new ConstructPartException($"{tag.Name}: its own blueprint isn't known here");

    /// <summary>A sub-blueprint with a use's overrides on its steps (a copy; the lists are shared, never changed).</summary>
    private static ConstructBuild.Blueprint WithOverrides(ConstructBuild.Blueprint sf, List<StepOver> over, string where)
    {
        if (over.Count == 0)
            return sf;
        var f = new ConstructBuild.Blueprint
        {
            Name = sf.Name, File = sf.File, Description = sf.Description, Group = sf.Group, ExactLinks = sf.ExactLinks, FrozenBase = sf.FrozenBase, Conduit = sf.Conduit,
            Id = sf.Id, Version = sf.Version, Uses = sf.Uses, Map = sf.Map, FlipExact = sf.FlipExact, Wires = sf.Wires, Links = sf.Links, Hoses = sf.Hoses,
            Connectors = sf.Connectors, NoCollide = sf.NoCollide, Tracks = sf.Tracks, FrozenJoints = sf.FrozenJoints, Cargo = sf.Cargo, Loot = sf.Loot, Stored = sf.Stored, Bill = sf.Bill,
        };
        for (var k = 0; k < sf.Steps.Count; k++)
            Copy(sf, k, f, sf.Steps[k].On, sf.Steps[k].Socket, sf.Steps[k].Own, sf.Steps[k].Spin, sf.StepAt[k], sf.StepWeld[k], sf.StepAlso[k], sf.StepFuse[k]);
        Apply(f, over, k => k, where);
        return f;
    }

    private static void Apply(ConstructBuild.Blueprint f, List<StepOver> over, Func<int, int> step, string where)
    {
        foreach (var o in over)
        {
            if (o.Step < 0)
                continue;
            var k = step(o.Step);
            if (k < 0 || k >= f.Steps.Count)
                continue;
            if (o.Paint != 0)
                f.StepPaint[k] = o.Paint == PaintCleared ? 0 : o.Paint;
            if (o.Finish != 0)
                f.StepFinish[k] = o.Finish == FinishCleared ? (byte)0 : o.Finish;
            if (o.Setting >= 0)
                f.StepSettings[k] = o.Setting;
            if (o.Config != null && ConstructParts.Find(f.Steps[k].Part) is { } cdef)
            {
                using var doc = JsonDocument.Parse(o.Config);
                f.StepConfig[k] = ConstructConfig.ParseValues(cdef, doc.RootElement, $"{where}'s step {o.Step}");
            }
        }
    }

    public const uint PaintCleared = 0xFFFFFFFF; // an override that takes a step's paint off ("paint": "none")
    public const byte FinishCleared = 255; // ...its texture ("finish": "none")

    /// <summary>
    /// The big fit's merge (the station's, and the check's): body <paramref name="sub"/> into <paramref name="root"/> at its use's
    /// place, each of its parts matched to the flat step at that place (def and place within 1 cm), then the joining links. False and why.
    /// </summary>
    public static bool JoinAt(ConstructWorld world, Construct root, (ConstructWorld World, Construct Body) sub, JoinPlan plan, ConstructBuild.Blueprint flat, UseTag tag, int[] subPartOf, int[] partOf, out string why)
    {
        why = "";
        var b = sub.Body;
        Construct moved;
        if (sub.World != world)
        {
            // (the check's own world: copied over first, as it stands)
            var order = new int[b.PartCount];
            for (var i = 0; i < order.Length; i++)
                order[i] = i;
            moved = world.CopyPiece(b, order, Vector3.Zero, Quaternion.Identity, out why) ?? throw new ConstructPartException(why);
            CopyTable(b, moved);
        }
        else
            moved = b;
        if (world.CanMerge(root, moved) is { } no)
        {
            why = no;
            return false;
        }
        var off = root.PartCount;
        var n = moved.PartCount;
        plan.Other = moved;
        var m0 = Stopwatch.GetTimestamp();
        if (!MergeExact(world, root, plan))
        {
            why = "the merge refused";
            return false;
        }
        // Each merged part to its flat step by its place.
        var want = new Dictionary<(string, int, int, int), int>();
        for (var s = 0; s < tag.Count; s++)
            if (subPartOf.Length > s && subPartOf[s] >= 0)
            {
                var k = tag.First + s;
                var p = root.Parts[off + subPartOf[s]];
                want[(root.DefOf(off + subPartOf[s]).Name, (int)MathF.Round(p.Pos.X * 100), (int)MathF.Round(p.Pos.Y * 100), (int)MathF.Round(p.Pos.Z * 100))] = k;
                partOf[k] = off + subPartOf[s];
            }
        for (var s = 0; s < tag.Count; s++)
            if (partOf[tag.First + s] >= 0)
                LinkOutside(world, root, flat, tag.First + s, partOf, tag.First, tag.Count);
        root.Dirty = true;
        root.Rebuild(); // (once, after the joining links: the exact merge leaves it to the caller)
        LastMergeMs = (Stopwatch.GetTimestamp() - m0) * 1000.0 / Stopwatch.Frequency;
        return true;
    }

    /// <summary>
    /// <see cref="ConstructWorld.Merge"/> without its auto-linking of every meeting socket (a use joins by the blueprint's own links):
    /// the plan's turn and place are exact, its link none.
    /// </summary>
    public static bool MergeExact(ConstructWorld world, Construct a, JoinPlan plan) => world.MergeAt(a, plan);

    public static double LastMergeMs; // the check's: the last merge alone

    /// <summary>A flat step's "also" links that end outside its use: laid after the big fit (the use's own were laid by its own build).</summary>
    public static void LinkOutside(ConstructWorld world, Construct c, ConstructBuild.Blueprint bp, int k, int[] partOf, int first, int count)
    {
        if (bp.StepAlso[k] is not { } also)
            return;
        foreach (var (on, socket, own) in also)
        {
            if (on >= first && on < first + count || on < 0 || on >= partOf.Length || partOf[on] < 0)
                continue;
            var theirs = ConstructMerge.SocketIndex(c, partOf[on], socket);
            var mine = Array.FindIndex(c.DefOf(partOf[k]).Sockets, s => s.Name == own);
            if (theirs < 0 || mine < 0 || !world.AddLink(c, partOf[on], theirs, partOf[k], mine))
                Console.WriteLine($"[subbp] {bp.Name}: step {k}'s link to step {on} ({own} to {socket}) couldn't be made at the join");
        }
    }

    /// <summary>
    /// --sub-blueprint NAME --tree-size: a full-size tree at the user's limits (depth 8, 128 uses a blueprint, 1,024 in the tree) made
    /// of NAME, flattened and costed as a station costs it, each slice timed: the proof that it stays inside the tick.
    /// </summary>
    private static int TreeSize(string name)
    {
        string text;
        try
        {
            text = File.ReadAllText(PlayerBlueprints.PathOf(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"sub-blueprint: {ex.Message}");
            return 1;
        }
        var leaf = ConstructBuild.ParseBlueprint(text, name);
        if (leaf.Uses.Count > 0)
        {
            Console.WriteLine("tree size: give a blueprint without uses: it is the leaf");
            return 1;
        }
        // Level 0 the leaf; each level up uses the one below N times along a line, N chosen so the tree reaches 1,024 uses at depth 8.
        var packed = new Dictionary<string, Dictionary<int, string>>();
        var leafId = "a000000000000000";
        packed[leafId] = new() { [1] = SubBlueprints.WithId(text, leafId, 1) };
        var probe = ConstructBuild.Build(new ConstructWorld(), leaf, Vector3.Zero, Quaternion.Identity, out _)!;
        ConstructStation.PartBounds(probe, out var lo, out var size);
        var spacing = MathF.Ceiling(MathF.Max(size.X, MathF.Max(size.Y, size.Z)) + 1);
        int[] fan = [5, 5, 4, 4, 2, 1, 1, 1]; // 1,005 uses in all (800 leaves), 8 deep: at the user's limits
        var below = leafId;
        var belowText = packed[leafId][1];
        var width = new[] { 1, 1, 1 }; // (each level along the next axis: the tree stays within reach)
        string topText = "";
        for (var level = 0; level < fan.Length; level++)
        {
            var id = $"b{level:x1}00000000000000";
            var sb = new StringBuilder();
            sb.Append($"{{\"format\": \"{ConstructBuild.BlueprintFormat}\", \"name\": \"Tree {level + 1}\", \"id\": \"{id}\", \"version\": 1, \"steps\": [{{\"part\": \"{leaf.Steps[0].Part}\"}}], \"uses\": [");
            var axis = level % 3;
            for (var u = 0; u < fan[level]; u++)
            {
                var d = spacing * width[axis] * (u + 1);
                var at = axis == 0 ? new Vector3(d, 0, 0) : axis == 1 ? new Vector3(0, d, 0) : new Vector3(0, 0, d);
                sb.Append(u > 0 ? ", " : "").Append(FormattableString.Invariant($"{{\"id\": \"{below}\", \"name\": \"Tree {level}\", \"version\": 1, \"turn\": 0, \"at\": [{at.X}, {at.Y}, {at.Z}], \"after\": 0}}"));
            }
            sb.Append("]}");
            width[axis] *= fan[level] + 1;
            topText = sb.ToString();
            packed[id] = new() { [1] = topText };
            below = id;
        }
        var src = new Source { Packed = packed, Library = false };
        var t0 = Stopwatch.GetTimestamp();
        ConstructBuild.Blueprint flat;
        try
        {
            flat = Flatten(ConstructBuild.ParseBlueprint(topText, "tree.json"), src);
        }
        catch (ConstructPartException ex)
        {
            Console.WriteLine($"tree size: {ex.Message}");
            return 1;
        }
        var flatMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"tree size: depth {fan.Length}, {flat.Map!.Tags.Count} uses, {flat.Steps.Count} flat steps; resolved and flattened in {flatMs:0.0} ms off the game thread");
        // The station's costing: the flat steps on a scratch world, CostBudgetMs a tick (twice: the first run carries the JIT).
        var ok = true;
        for (var pass = 1; pass <= 2; pass++)
        {
            var run = new ConstructBuild.BuildRun { StepsOnly = true };
            using var steps = ConstructBuild.BuildSteps(new ConstructWorld(), flat, Vector3.Zero, Quaternion.Identity, run).GetEnumerator();
            double worst = 0, total = 0;
            var slices = 0;
            var more = true;
            while (more)
            {
                var s0 = Stopwatch.GetTimestamp();
                var until = s0 + (long)(ConstructStation.CostBudgetMs * Stopwatch.Frequency / 1000);
                while (more && Stopwatch.GetTimestamp() < until)
                    more = steps.MoveNext();
                var ms = (Stopwatch.GetTimestamp() - s0) * 1000.0 / Stopwatch.Frequency;
                worst = Math.Max(worst, ms);
                total += ms;
                slices++;
            }
            Console.WriteLine($"costing, run {pass}: {(run.Body != null ? "built" : "REFUSED: " + run.Why)} in {slices} slices, {total:0.0} ms in all, worst slice {worst:0.00} ms (budget {ConstructStation.CostBudgetMs} ms a tick)");
            ok = run.Body != null && (pass == 1 || worst < 8);
        }
        // The big fit at this size: the top use's own body merged into its parent (the station's weld), timed.
        var reference = ConstructBuild.Build(new ConstructWorld(), flat, Vector3.Zero, Quaternion.Identity, out _);
        if (reference != null)
            for (var pass = 0; pass < 2; pass++)
                JoinCheck(flat, reference); // (twice: the first carries the JIT)
        return ok ? 0 : 1;
    }
}
