using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// One prop being edited in the dev prop editor (Source/UI/UiDevTools.cs). The edits are made to the prop FILE's JSON
/// (so every key the editor doesn't know about, a breakable block, lights, scatter hints, stays exactly as it was), and
/// each edit is checked by loading that text with the real prop loader (<see cref="PropFile.Load"/>): a bad edit is
/// refused with the loader's reason and undone. A good one is copied into the loaded prop in place
/// (<see cref="Props.CopyInto"/>), so every copy of it on the map changes at once. The placed prop's position, turn and
/// scale are edited on the map's prop list (render only: its collision boxes stay where the map built them).
///
/// Nothing touches a shipped file: <see cref="DevEditPackage.Write"/> saves the result as a change package, and
/// <see cref="Revert"/> (also run when the map changes) puts the loaded prop and the map back as they were.
/// </summary>
public sealed class PropEditSession
{
    /// <summary>A state the undo stack can go back to: the prop file's text and the placed prop's position, turn and scale.</summary>
    private readonly record struct State(string Json, Vector3 Pos, float YawDeg, float Scale);

    public readonly Map Map;

    /// <summary>The placed prop (index in <see cref="Map.PropInstances"/>).</summary>
    public readonly int Instance;

    /// <summary>The loaded prop being changed (shared by every copy of it).</summary>
    public readonly PropDef Def;

    /// <summary>The prop file, as a path from the game folder (Props/Animated/Anemometer.json).</summary>
    public readonly string PropPath;

    /// <summary>The prop file's text as it was read.</summary>
    public readonly string OriginalText;

    /// <summary>The placed prop as the map built it.</summary>
    public readonly PropInstance OriginalPlacement;

    private readonly PropDef _snapshot;
    private readonly Dictionary<int, PropInstance> _movedOthers = new(); // other copies whose bounds an edit changed
    private readonly JsonObject _original;
    private JsonObject _json;
    private string _goodText;
    private readonly List<State> _undo = new(), _redo = new();
    private string _lastKey = "";
    private double _lastAt = -10;

    /// <summary>The live placement (render only).</summary>
    public Vector3 Position { get; private set; }

    public float YawDeg { get; private set; }

    public float Scale { get; private set; }

    /// <summary>The last refused edit's reason (from the prop loader), or "".</summary>
    public string Error { get; private set; } = "";

    /// <summary>Maps loaded part index to (entry in the file's "parts" list, or -1 for a part copied in by a "use" entry; the used prop's name).</summary>
    private (int Entry, string? Used)[] _partEntry = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Has anything changed from the file and the map as loaded?</summary>
    public bool Changed => PropChanged || PlacementChanged;

    public bool PropChanged => !JsonNode.DeepEquals(_json, _original);

    public bool PlacementChanged => Position != OriginalPlacement.Position || MathF.Abs(YawDeg - OriginalPlacement.Yaw * 180f / MathF.PI) > 1e-3f || Scale != OriginalPlacement.Scale;

    /// <summary>The file's JSON as it stands (do not change it directly: use the edit methods).</summary>
    public JsonObject Json => _json;

    private PropEditSession(Map map, int instance, string path, string text, JsonObject json)
    {
        Map = map;
        Instance = instance;
        OriginalPlacement = map.PropInstances[instance];
        Def = OriginalPlacement.Def;
        PropPath = path;
        OriginalText = text;
        _original = (JsonObject)json.DeepClone();
        _json = json;
        _goodText = Format(json);
        _snapshot = Props.Snapshot(Def);
        Position = OriginalPlacement.Position;
        YawDeg = OriginalPlacement.Yaw * 180f / MathF.PI;
        Scale = OriginalPlacement.Scale;
        _partEntry = PartEntries(json);
    }

    /// <summary>
    /// Starts editing placed prop <paramref name="instance"/> of <paramref name="map"/>, reading its file from the game
    /// folder (<paramref name="dataFolder"/>, default <see cref="AppFolders.Data"/>). Null with <paramref name="why"/> when it can't be edited.
    /// </summary>
    public static PropEditSession? Open(Map map, int instance, out string why, string? dataFolder = null)
    {
        why = "";
        if ((uint)instance >= (uint)map.PropInstances.Count)
        {
            why = "no such prop on this map";
            return null;
        }
        var def = map.PropInstances[instance].Def;
        if (def.FlagId != null)
        {
            why = $"this is a flag design variant of \"{def.Name}\"; flag cloth comes from the flag pool (Props/Flags), not the prop file";
            return null;
        }
        var path = LookAt.PropPath(def);
        if (def.SourceFile == null)
        {
            why = $"\"{def.Name}\" is built in code, not loaded from a file";
            return null;
        }
        var full = Path.Combine(dataFolder ?? AppFolders.Data, path.Replace('/', Path.DirectorySeparatorChar));
        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            why = $"can't read {path}: {ex.Message}";
            return null;
        }
        JsonObject json;
        try
        {
            json = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new JsonException("not an object");
        }
        catch (JsonException ex)
        {
            why = $"{path} is not valid JSON: {ex.Message}";
            return null;
        }
        var s = new PropEditSession(map, instance, path, text, json);
        // The file must load to the prop the map holds (same number of parts), or part numbers would not line up.
        var check = s.TryLoad(s._goodText);
        if (check.Def == null || check.Def.Parts.Count != def.Parts.Count)
        {
            why = check.Def == null ? $"{path} doesn't load on its own: {check.Error}" : $"{path} has changed since the game loaded it (reload the props first)";
            return null;
        }
        return s;
    }

    // ---- Reading parts ----------------------------------------------------------------------------

    private JsonArray PartsArray => _json["parts"] as JsonArray ?? throw new InvalidOperationException("the prop file has no \"parts\" list");

    /// <summary>The file entry a loaded part comes from, or null with the reason (a part copied in by a "use" entry).</summary>
    public JsonObject? PartJson(int part, out string why)
    {
        why = "";
        if ((uint)part >= (uint)_partEntry.Length)
        {
            why = "no such part";
            return null;
        }
        var (entry, used) = _partEntry[part];
        if (entry < 0)
        {
            why = $"part #{part + 1} comes from the prefab's \"use\": \"{used}\"; edit that prop (or its \"use\" entry's pos / yaw / scale in the file)";
            return null;
        }
        return PartsArray[entry] as JsonObject;
    }

    /// <summary>Is the loaded part one of the file's own boxes (not copied in from a used prop)?</summary>
    public bool PartEditable(int part) => (uint)part < (uint)_partEntry.Length && _partEntry[part].Entry >= 0;

    /// <summary>For each loaded part: the "parts" entry it comes from (a "use" entry copies all the used prop's parts in).</summary>
    private (int, string?)[] PartEntries(JsonObject json)
    {
        var list = new List<(int, string?)>();
        if (json["parts"] is not JsonArray parts)
            return [];
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is JsonObject o && UseName(o) is { } use)
            {
                var used = Resolve(use);
                for (var k = 0; k < (used?.Parts.Count ?? 0); k++)
                    list.Add((-1, use));
            }
            else
                list.Add((i, null));
        }
        return list.ToArray();
    }

    private static string? UseName(JsonObject o)
    {
        foreach (var (k, v) in o)
            if (k.Equals("use", StringComparison.OrdinalIgnoreCase))
                return v?.GetValue<string>();
        return null;
    }

    private PropDef? Resolve(string name) => Map.PropScope.Find(name);

    // ---- Editing -----------------------------------------------------------------------------------

    /// <summary>
    /// Remembers the state before an edit for undo. Edits with the same <paramref name="key"/> within half a second share one
    /// undo step (dragging a field). <paramref name="now"/>: seconds (any clock); null always starts a new step.
    /// </summary>
    private void Remember(string key, double? now)
    {
        var t = now ?? double.NaN;
        if (now == null || key != _lastKey || t - _lastAt > 0.5)
            _undo.Add(new State(_goodText, Position, YawDeg, Scale));
        if (_undo.Count > 200)
            _undo.RemoveAt(0);
        _redo.Clear();
        _lastKey = key;
        _lastAt = t;
    }

    /// <summary>Sets a part's "size", "offset" or "rotation". True when the loader took it.</summary>
    public bool SetPartVector(int part, string key, Vector3 v, double? now = null) =>
        EditPart(part, key + part, o =>
        {
            if (key == "rotation")
                foreach (var k in (string[])["yaw", "pitch", "roll"])
                    RemoveKey(o, k); // the rotation now says all three
            o[key] = Vec(v);
        }, now);

    /// <summary>Sets a part's colour ([r, g, b], 0-1).</summary>
    public bool SetPartColor(int part, Vector3 rgb, double? now = null) =>
        EditPart(part, "color" + part, o =>
        {
            RemoveKey(o, "colour");
            o["color"] = Vec(Vector3.Clamp(rgb, Vector3.Zero, Vector3.One));
        }, now);

    /// <summary>Sets a part's "material" (a name from <see cref="PropFile.Materials"/>), or removes it (null: a plain colour).</summary>
    public bool SetPartMaterial(int part, string? material, double? now = null) =>
        EditPart(part, "material" + part, o =>
        {
            RemoveKey(o, "material");
            if (material != null)
                o["material"] = material;
        }, now);

    /// <summary>
    /// Sets a key of a part's "anim" block (the axis: "x", "-z" or [x, y, z]; the pivot: [x, y, z]; rpm, angle...). With
    /// <paramref name="sameAnim"/>, every part whose anim block was the same as this one's changes too (an anemometer's
    /// eleven spinning parts in one go).
    /// </summary>
    public bool SetAnim(int part, string key, JsonNode? value, bool sameAnim, double? now = null)
    {
        if (PartJson(part, out var why) is not { } target)
            return Refuse(why);
        if (target["anim"] is not JsonObject anim)
            return Refuse($"part #{part + 1} has no \"anim\" block");
        var before = anim.DeepClone();
        var targets = new List<JsonObject>();
        if (sameAnim)
        {
            foreach (var p in PartsArray)
                if (p is JsonObject o && o["anim"] is JsonObject a && JsonNode.DeepEquals(a, before))
                    targets.Add(a);
        }
        else
            targets.Add(anim);
        Remember($"anim.{key}{part}{sameAnim}", now);
        foreach (var a in targets)
        {
            RemoveKey(a, key);
            if (value != null)
                a[key] = value.DeepClone();
        }
        return Apply();
    }

    /// <summary>Adds a new grey box (0.2 m cube) on top of the model's bounds; returns its part index, or -1.</summary>
    public int AddPart()
    {
        Remember("add", null);
        var b = Def.Bounds;
        PartsArray.Add(new JsonObject
        {
            ["size"] = Vec(new Vector3(0.2f)),
            ["offset"] = Vec(new Vector3(b.Center.X, b.Max.Y + 0.1f, b.Center.Z)),
            ["color"] = "grey",
        });
        return Apply() ? _partEntry.Length - 1 : -1;
    }

    /// <summary>Copies a part (the copy goes straight after it in the file); returns the copy's part index, or -1.</summary>
    public int DuplicatePart(int part)
    {
        if (PartJson(part, out var why) is not { } o)
        {
            Refuse(why);
            return -1;
        }
        Remember("dup", null);
        var entry = _partEntry[part].Entry;
        PartsArray.Insert(entry + 1, o.DeepClone());
        return Apply() ? part + 1 : -1;
    }

    /// <summary>Removes a part (a prop keeps at least one).</summary>
    public bool DeletePart(int part)
    {
        if (PartJson(part, out var why) is null)
            return Refuse(why);
        if (PartsArray.Count <= 1)
            return Refuse("a prop needs at least one part");
        Remember("del", null);
        PartsArray.RemoveAt(_partEntry[part].Entry);
        return Apply();
    }

    /// <summary>Moves, turns and scales the placed prop (render only; scale 0.1 to 10 as the map op allows).</summary>
    public bool SetPlacement(Vector3 pos, float yawDeg, float scale, double? now = null)
    {
        if (scale is < 0.1f or > 10f || !float.IsFinite(pos.X + pos.Y + pos.Z + yawDeg))
            return Refuse("scale is 0.1 to 10 (as the PlaceProp op allows)");
        Remember("place", now);
        Position = pos;
        YawDeg = yawDeg;
        Scale = scale;
        ApplyPlacement();
        Error = "";
        return true;
    }

    private bool EditPart(int part, string key, Action<JsonObject> edit, double? now = null)
    {
        if (PartJson(part, out var why) is not { } o)
            return Refuse(why);
        Remember(key, now);
        edit(o);
        return Apply();
    }

    private bool Refuse(string why)
    {
        Error = why;
        return false;
    }

    private static void RemoveKey(JsonObject o, string key)
    {
        foreach (var k in o.Select(p => p.Key).Where(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList())
            o.Remove(k);
    }

    // ---- Undo / redo ---------------------------------------------------------------------------------

    public bool Undo() => Step(_undo, _redo);

    public bool Redo() => Step(_redo, _undo);

    private bool Step(List<State> from, List<State> to)
    {
        if (from.Count == 0)
            return false;
        to.Add(new State(_goodText, Position, YawDeg, Scale));
        var s = from[^1];
        from.RemoveAt(from.Count - 1);
        _json = (JsonObject)JsonNode.Parse(s.Json)!;
        Position = s.Pos;
        YawDeg = s.YawDeg;
        Scale = s.Scale;
        _lastKey = "";
        Apply();
        ApplyPlacement();
        return true;
    }

    // ---- Applying ---------------------------------------------------------------------------------------

    /// <summary>Loads the JSON as it stands; a good file goes live, a bad one is refused and the JSON goes back to the last good text.</summary>
    private bool Apply()
    {
        var text = Format(_json);
        var (def, error) = TryLoad(text);
        if (def == null)
        {
            Error = error;
            _json = (JsonObject)JsonNode.Parse(_goodText)!;
            if (_undo.Count > 0 && _undo[^1].Json == _goodText && _undo[^1].Pos == Position)
                _undo.RemoveAt(_undo.Count - 1); // the refused edit leaves no undo step
            _lastKey = "";
            return false;
        }
        if (!def.Name.Equals(Def.Name, StringComparison.OrdinalIgnoreCase))
        {
            Error = $"the prop's \"name\" must stay \"{Def.Name}\" (maps find it by name)";
            _json = (JsonObject)JsonNode.Parse(_goodText)!;
            return false;
        }
        _goodText = text;
        _partEntry = PartEntries(_json);
        Error = "";
        Props.CopyInto(def, Def);
        RefreshBounds();
        return true;
    }

    /// <summary>Loads prop text with the real loader (prefabs resolved through the map's props). Def null: refused, with the reason.</summary>
    public (PropDef? Def, string Error) TryLoad(string text)
    {
        try
        {
            var rel = Def.SourceFile ?? PropPath;
            return (PropFile.Load(text, rel, Def.Category, n => Resolve(n)), "");
        }
        catch (PropFileException ex)
        {
            return (null, ex.Message);
        }
    }

    private void ApplyPlacement()
    {
        var inst = Map.PropInstances[Instance];
        var yaw = YawDeg * MathF.PI / 180f;
        Map.PropInstances[Instance] = inst with { Position = Position, Yaw = yaw, Scale = Scale, Bounds = Map.WorldBounds(Def.Bounds, Position, yaw, Scale) };
        Props.MarkEdited();
    }

    /// <summary>The model's bounds changed: every copy of the prop on the map gets new culling bounds.</summary>
    private void RefreshBounds()
    {
        var list = Map.PropInstances;
        for (var i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (!ReferenceEquals(p.Def, Def))
                continue;
            if (i != Instance)
                _movedOthers.TryAdd(i, p);
            list[i] = p with { Bounds = Map.WorldBounds(Def.Bounds, p.Position, p.Yaw, p.Scale) };
        }
    }

    /// <summary>Puts the loaded prop and the map back as they were before the session (the session stays usable from there).</summary>
    public void Revert()
    {
        Props.CopyInto(_snapshot, Def);
        Map.PropInstances[Instance] = OriginalPlacement;
        foreach (var (i, p) in _movedOthers)
            if (i < Map.PropInstances.Count)
                Map.PropInstances[i] = p;
        _movedOthers.Clear();
        _json = (JsonObject)_original.DeepClone();
        _goodText = Format(_json);
        _partEntry = PartEntries(_json);
        Position = OriginalPlacement.Position;
        YawDeg = OriginalPlacement.Yaw * 180f / MathF.PI;
        Scale = OriginalPlacement.Scale;
        _undo.Clear();
        _redo.Clear();
        Error = "";
        Props.MarkEdited();
    }

    // ---- What changed -------------------------------------------------------------------------------------

    /// <summary>Every changed value of the prop file, as (path, before, after): "parts[3].anim.axis", "y", "z" (null: not there).</summary>
    public List<(string Path, JsonNode? From, JsonNode? To)> PropChanges()
    {
        var list = new List<(string, JsonNode?, JsonNode?)>();
        Diff("", _original, _json, list);
        return list;
    }

    private static void Diff(string path, JsonNode? a, JsonNode? b, List<(string, JsonNode?, JsonNode?)> into)
    {
        if (JsonNode.DeepEquals(a, b))
            return;
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var (k, v) in oa)
                Diff(path.Length == 0 ? k : path + "." + k, v, ob.TryGetPropertyValue(k, out var bv) ? bv : null, into);
            foreach (var (k, v) in ob)
                if (!oa.ContainsKey(k))
                    into.Add((path.Length == 0 ? k : path + "." + k, null, v?.DeepClone()));
            return;
        }
        // Lists of objects (parts) compare entry by entry; entries past the shorter list are added or removed.
        if (a is JsonArray la && b is JsonArray lb && (la.Any(x => x is JsonObject) || lb.Any(x => x is JsonObject)))
        {
            for (var i = 0; i < Math.Max(la.Count, lb.Count); i++)
                Diff($"{path}[{i}]", i < la.Count ? la[i] : null, i < lb.Count ? lb[i] : null, into);
            return;
        }
        into.Add((path, a?.DeepClone(), b?.DeepClone()));
    }

    /// <summary>
    /// The map op change for a moved placed prop: (op number, the op as the map file has it, the op after) when the prop was
    /// placed by a PlaceProp ("Prop") op and its map file can be read; null otherwise (with the reason).
    /// </summary>
    public (int OpNumber, JsonObject Before, JsonObject After)? MapOpPatch(out string why, string? dataFolder = null)
    {
        why = "";
        if (!PlacementChanged)
        {
            why = "the placed prop hasn't moved";
            return null;
        }
        var fileOp = Map.FileOpOfProp(Instance);
        if (fileOp < 0 || Map.FileOpNames[fileOp] != nameof(Map.PlaceProp))
        {
            why = fileOp < 0 ? "this prop wasn't placed by a map op (Clutter or generated): the package gives its new place in world coordinates"
                : $"this prop comes from map op #{Map.FileOpMarks[fileOp].OpNumber} {Map.FileOpNames[fileOp]}, not a Prop op: the package gives its new place in world coordinates";
            return null;
        }
        var opNumber = Map.FileOpMarks[fileOp].OpNumber;
        if (Map.SourceFile == null)
        {
            why = "the map was built in code";
            return null;
        }
        if (_opBefore != null)
            return Patched(opNumber, _opBefore);
        JsonObject before;
        try
        {
            var text = File.ReadAllText(Path.Combine(dataFolder ?? AppFolders.Data, "Maps", Map.SourceFile));
            var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            before = (root?["ops"] as JsonArray)?[opNumber - 1] as JsonObject ?? throw new JsonException($"no op #{opNumber}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException)
        {
            why = $"can't read the map file {LookAt.MapPath(Map)}: {ex.Message}";
            return null;
        }
        _opBefore = before;
        return Patched(opNumber, before);
    }

    private JsonObject? _opBefore; // the map file's op, read once

    private (int, JsonObject, JsonObject) Patched(int opNumber, JsonObject before)
    {
        var after = (JsonObject)before.DeepClone();
        var p = OriginalPlacement;
        if (Position.X != p.Position.X)
            after["x"] = Round(Position.X, 3);
        if (Position.Z != p.Position.Z)
            after["z"] = Round(Position.Z, 3);
        if (Position.Y != p.Position.Y)
            after["y"] = Round((before["y"]?.GetValue<double>() ?? 0) + (Position.Y - p.Position.Y), 3);
        if (MathF.Abs(YawDeg - p.Yaw * 180f / MathF.PI) > 1e-3f)
            after["yaw"] = Round(YawDeg, 2);
        if (Scale != p.Scale)
            after["scale"] = Round(Scale, 3);
        return (opNumber, (JsonObject)before.DeepClone(), after);
    }

    // ---- JSON helpers ----------------------------------------------------------------------------------------

    public static JsonArray Vec(Vector3 v) => new(Round(v.X, 4), Round(v.Y, 4), Round(v.Z, 4));

    private static JsonNode Round(double v, int places)
    {
        var r = Math.Round(v, places);
        return r == Math.Floor(r) && Math.Abs(r) < 1e9 ? JsonValue.Create((long)r) : JsonValue.Create(r);
    }

    /// <summary>
    /// Writes prop JSON the way the shipped files look: top-level keys on their own lines, and each entry of a list of objects
    /// (parts, collider boxes, lights) on one line, so a diff against the original shows only the changed parts.
    /// </summary>
    public static string Format(JsonObject root)
    {
        var compact = new JsonSerializerOptions { WriteIndented = false };
        var sb = new StringBuilder("{\n");
        var first = true;
        foreach (var (k, v) in root)
        {
            if (!first)
                sb.Append(",\n");
            first = false;
            sb.Append("  ").Append(JsonSerializer.Serialize(k)).Append(": ");
            if (v is JsonArray arr && arr.Count > 0 && arr.All(x => x is JsonObject))
            {
                sb.Append("[\n");
                for (var i = 0; i < arr.Count; i++)
                    sb.Append("    ").Append(Spaced(arr[i]!.ToJsonString(compact))).Append(i < arr.Count - 1 ? ",\n" : "\n");
                sb.Append("  ]");
            }
            else
                sb.Append(Spaced(v?.ToJsonString(compact) ?? "null"));
        }
        return sb.Append("\n}\n").ToString();
    }

    /// <summary>Compact JSON with a space after each comma and colon outside strings ({"size": [0.1, 0.2, 0.3]}), as the shipped files have it.</summary>
    private static string Spaced(string json)
    {
        var sb = new StringBuilder(json.Length + json.Length / 4);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            sb.Append(c);
            if (inString)
            {
                if (c == '\\' && i + 1 < json.Length)
                    sb.Append(json[++i]);
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
                inString = true;
            else if (c is ',' or ':')
                sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>A short name for the package folder: letters and digits of the prop's name.</summary>
    public string Slug => new string(Def.Name.Where(char.IsAsciiLetterOrDigit).ToArray()) is { Length: > 0 } s ? s : "Prop";

    internal static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
