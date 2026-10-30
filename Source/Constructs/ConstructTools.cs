using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>One box of a held tool's model (tool space: origin at the strong hand's grip, -Z forward, +Y up, metres).</summary>
public readonly record struct ToolBox(Vector3 Center, Vector3 Size, Vector3 Color, bool Spin, bool Glow);

/// <summary>Where a hand holds a tool (tool space) and the shape it makes.</summary>
public readonly record struct ToolHand(Vector3 At, Quaternion Rot, HandShape Shape);

/// <summary>How a tool moves while it is used (a timed edit running, a wire being run).</summary>
public enum ToolMotion : byte
{
    None,
    Ratchet, // cranks back and forth about an axis through the grip (the wrench)
    Jitter, // a small fast shake (the welder)
    Push, // a short jab forward and back (probes, the drill)
    Sweep, // a slow side-to-side sweep (the scanner)
}

/// <summary>A Sandbox tool's held model: data from Constructs/Tools/*.json (docs/sandbox/CONSTRUCTS.md, "Tools in hand").</summary>
public sealed class ToolModel
{
    public string Name = "", Key = "", Description = "", File = "";

    /// <summary>The tool this model is for, or null for one that doesn't exist in the game yet (hoist, configure tool, scanner, drill).</summary>
    public ConstructTool? Tool;

    public ToolBox[] Boxes = [];

    /// <summary>The model turned in the hands (pitch, yaw, roll), applied about the grip before the animation.</summary>
    public Quaternion Hold = Quaternion.Identity;

    public ToolHand Grip;
    public ToolHand? Support; // the off hand (null: a one-handed tool; the off hand rests)
    public Vector3 Tip; // where sparks, light and beams come from
    public Vector3 SpinAxis = Vector3.UnitZ, SpinPivot; // the "spin" boxes turn about this
    public ToolMotion Motion;
    public Vector3 MotionAxis = Vector3.UnitX;
    public float Amount, Rate; // the motion's size (radians or metres) and rate (Hz)
    public float Spin, IdleSpin; // radians a second, used and idle
    public Vector3? Sparks, Light, Beam;
    public float BeamLength = 2.5f;
    public int SpinBoxes, GlowBoxes;
}

/// <summary>A tool model file that can't be used: the reason names the file and the problem.</summary>
public sealed class ToolModelException(string message) : Exception(message);

/// <summary>
/// The held models of the Sandbox tools: every JSON file in Constructs/Tools, loaded once (data first: a new look is a new
/// file). A bad file is refused with a reason (<see cref="Refused"/>; <c>--check-construct-parts</c> prints them) and
/// the tool is then drawn with a plain grey block. Models are built once; drawing them allocates nothing (ToolRender.cs).
/// </summary>
public static class ConstructTools
{
    public const string Format = "call-of-slop-construct-tool/1";

    public const int MaxBoxes = 24;

    /// <summary>Every tool key a model may name: the tools in the game, then the ones still to come (the tool wheel's diagonals).</summary>
    public static readonly string[] Keys = ["wrench", "welder", "linker", "hoist", "configure", "scanner", "drill"];

    private static readonly List<string> _refused = new();
    private static readonly Lazy<ToolModel[]> _all = new(() => LoadFolder(Folder, _refused), LazyThreadSafetyMode.ExecutionAndPublication);
    private static ToolModel?[]? _byTool;

    public static string Folder => Path.Combine(AppFolders.Data, "Constructs", "Tools");

    public static IReadOnlyList<ToolModel> All => _all.Value;

    public static IReadOnlyList<string> Refused
    {
        get
        {
            _ = All;
            return _refused;
        }
    }

    /// <summary>The model of a tool in the game (null for none, or when its file was refused).</summary>
    public static ToolModel? For(ConstructTool tool)
    {
        var by = _byTool;
        if (by == null)
        {
            by = new ToolModel?[(int)ConstructTool.Configure + 1];
            foreach (var m in All)
                if (m.Tool is { } t)
                    by[(int)t] = m;
            _byTool = by;
        }
        return (uint)tool < (uint)by.Length ? by[(int)tool] : null;
    }

    /// <summary>The model for a tool key ("wrench", ..., "drill"), or null.</summary>
    public static ToolModel? Find(string key)
    {
        foreach (var m in All)
            if (m.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return m;
        return null;
    }

    /// <summary>Loads every tool file of a folder (sorted by file name), refusing bad ones into <paramref name="refused"/>.</summary>
    public static ToolModel[] LoadFolder(string folder, List<string> refused)
    {
        var list = new List<ToolModel>();
        if (!Directory.Exists(folder))
        {
            refused.Add($"{folder}: the construct tools folder is missing");
            return [];
        }
        foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), StringComparer.Ordinal))
        {
            try
            {
                var m = Parse(File.ReadAllText(file), Path.GetFileName(file));
                foreach (var o in list)
                    if (o.Key == m.Key)
                        throw new ToolModelException($"{m.File}: {o.File} is already the {m.Key}'s model");
                    else if (o.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))
                        throw new ToolModelException($"{m.File}: another file already has the name \"{m.Name}\"");
                list.Add(m);
            }
            catch (Exception ex) when (ex is ToolModelException or JsonException or IOException)
            {
                refused.Add(ex is ToolModelException ? ex.Message : $"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return list.ToArray();
    }

    // ---- Parsing ---------------------------------------------------------------------------------------------

    private static readonly string[] TopKeys = ["format", "name", "tool", "description", "hold", "boxes", "grip", "support", "tip", "spin", "use"];
    private static readonly string[] BoxKeys = ["size", "at", "min", "max", "color", "spin", "glow"];
    private static readonly string[] HandKeys = ["at", "rot", "shape"];
    private static readonly string[] UseKeys = ["motion", "axis", "amount", "rate", "spin", "idleSpin", "sparks", "light", "beam", "beamLength"];

    /// <summary>Parses one tool file's text. Throws <see cref="ToolModelException"/> with the reason for anything wrong.</summary>
    public static ToolModel Parse(string json, string file)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ToolModelException($"{file}: a tool file is one JSON object");
        Known(root, TopKeys, file);
        if (Opt(root, "format") is { } fmt && fmt.GetString() != Format)
            throw new ToolModelException($"{file}: \"format\" must be \"{Format}\"");
        var m = new ToolModel { File = file };
        m.Name = Str(Need(root, "name", file), file, "name");
        if (m.Name.Length is 0 or > 32 || m.Name.Any(c => c < 32 || c > 126))
            throw new ToolModelException($"{file}: \"name\" must be 1-32 plain ASCII characters");
        m.Description = Opt(root, "description") is { } desc ? Str(desc, file, "description") : "";
        if (m.Description.Any(c => c < 32 || c > 126))
            throw new ToolModelException($"{file}: \"description\" must be plain ASCII");
        m.Key = Str(Need(root, "tool", file), file, "tool").ToLowerInvariant();
        var ki = Array.IndexOf(Keys, m.Key);
        if (ki < 0)
            throw new ToolModelException($"{file}: \"tool\" is one of {string.Join(", ", Keys)}, not \"{m.Key}\"");
        m.Tool = ki < 3 ? (ConstructTool)(ki + 1) : Keys[ki] == "configure" ? ConstructTool.Configure : null;
        if (Opt(root, "hold") is { } hold)
            m.Hold = Turn(Vec(hold, file, "hold"));

        // Boxes
        if (Opt(root, "boxes") is not { ValueKind: JsonValueKind.Array } boxes || boxes.GetArrayLength() == 0)
            throw new ToolModelException($"{file}: \"boxes\" must list at least one box");
        if (boxes.GetArrayLength() > MaxBoxes)
            throw new ToolModelException($"{file}: at most {MaxBoxes} boxes a tool");
        var list = new List<ToolBox>();
        var bi = 0;
        foreach (var b in boxes.EnumerateArray())
        {
            var where = $"boxes[{bi++}]";
            if (b.ValueKind != JsonValueKind.Object)
                throw new ToolModelException($"{file}: {where} must be an object");
            Known(b, BoxKeys, $"{file}, {where}");
            Vector3 min, max;
            if (Opt(b, "min") is { } mn && Opt(b, "max") is { } mx)
            {
                min = Vec(mn, file, where + ".min");
                max = Vec(mx, file, where + ".max");
            }
            else if (Opt(b, "size") is { } sz)
            {
                var size = Vec(sz, file, where + ".size");
                var at = Opt(b, "at") is { } a ? Vec(a, file, where + ".at") : Vector3.Zero;
                min = at - size * 0.5f;
                max = at + size * 0.5f;
            }
            else
                throw new ToolModelException($"{file}: {where} needs \"size\" (and \"at\") or \"min\" and \"max\"");
            var s = max - min;
            if (!(s.X >= 0.002f && s.Y >= 0.002f && s.Z >= 0.002f) || s.X > 0.6f || s.Y > 0.6f || s.Z > 0.6f)
                throw new ToolModelException($"{file}: {where} must be 2 mm to 60 cm along each side");
            if (Vector3.Max(Vector3.Abs(min), Vector3.Abs(max)).Length() > 0.6f)
                throw new ToolModelException($"{file}: {where} is more than 60 cm from the grip");
            var colour = Opt(b, "color") is { } col ? Colour(col, file, where + ".color") : new Vector3(0.55f);
            list.Add(new ToolBox((min + max) * 0.5f, s, colour, Bool(b, "spin", file, where), Bool(b, "glow", file, where)));
        }
        m.Boxes = list.ToArray();
        m.SpinBoxes = m.Boxes.Count(x => x.Spin);
        m.GlowBoxes = m.Boxes.Count(x => x.Glow);
        if (m.Boxes.All(x => x.Glow))
            throw new ToolModelException($"{file}: at least one box must not glow");
        if (Floating(m.Boxes) is { } floating)
            throw new ToolModelException($"{file}: boxes[{floating}] doesn't touch the rest of the tool (a tool is one piece)");

        // Hands, tip, spin
        m.Grip = Hand(Need(root, "grip", file), file, "grip");
        if (m.Grip.At.Length() > 0.15f)
            throw new ToolModelException($"{file}: \"grip\" must be within 15 cm of the origin (the origin is where the strong hand holds it)");
        if (Opt(root, "support") is { } sup)
        {
            m.Support = Hand(sup, file, "support");
            if (m.Support.Value.At.Length() > 0.4f)
                throw new ToolModelException($"{file}: \"support\" must be within 40 cm of the grip");
        }
        m.Tip = Vec(Need(root, "tip", file), file, "tip");
        if (m.Tip.Length() > 0.6f)
            throw new ToolModelException($"{file}: \"tip\" must be within 60 cm of the grip");
        if (Opt(root, "spin") is { } spin)
        {
            Known(spin, ["axis", "pivot"], $"{file}, spin");
            m.SpinAxis = Axis(Need(spin, "axis", file + ", spin"), file, "spin.axis");
            m.SpinPivot = Opt(spin, "pivot") is { } pv ? Vec(pv, file, "spin.pivot") : Vector3.Zero;
        }
        else if (m.SpinBoxes > 0)
            throw new ToolModelException($"{file}: \"spin\" boxes need a \"spin\" section (its axis and pivot)");

        // Use
        if (Opt(root, "use") is { } use)
        {
            Known(use, UseKeys, $"{file}, use");
            m.Motion = Opt(use, "motion") is { } mo ? EnumOf<ToolMotion>(Str(mo, file, "use.motion"), file, "use.motion") : ToolMotion.None;
            m.MotionAxis = Opt(use, "axis") is { } ax ? Axis(ax, file, "use.axis") : Vector3.UnitX;
            m.Amount = Opt(use, "amount") is { } am ? Num(am, file, "use.amount") : 0f;
            Range(m.Amount, 0f, 1.5f, file, "use.amount");
            m.Rate = Opt(use, "rate") is { } rt ? Num(rt, file, "use.rate") : 2f;
            Range(m.Rate, 0f, 40f, file, "use.rate");
            m.Spin = Opt(use, "spin") is { } sp ? Num(sp, file, "use.spin") : 0f;
            m.IdleSpin = Opt(use, "idleSpin") is { } isp ? Num(isp, file, "use.idleSpin") : 0f;
            Range(m.Spin, -100f, 100f, file, "use.spin");
            Range(m.IdleSpin, -100f, 100f, file, "use.idleSpin");
            m.Sparks = Opt(use, "sparks") is { } spk ? Colour(spk, file, "use.sparks") : null;
            m.Light = Opt(use, "light") is { } lt ? Colour(lt, file, "use.light") : null;
            m.Beam = Opt(use, "beam") is { } bm ? Colour(bm, file, "use.beam") : null;
            m.BeamLength = Opt(use, "beamLength") is { } bl ? Num(bl, file, "use.beamLength") : 2.5f;
            Range(m.BeamLength, 0.1f, 8f, file, "use.beamLength");
            if ((m.Spin != 0 || m.IdleSpin != 0) && m.SpinBoxes == 0)
                throw new ToolModelException($"{file}: \"use.spin\" turns the \"spin\" boxes, but no box has \"spin\": true");
        }
        return m;
    }

    /// <summary>The first box not joined to box 0 through boxes that touch (1 mm slack), or null: the model is one piece.</summary>
    public static int? Floating(ToolBox[] boxes)
    {
        Span<bool> joined = stackalloc bool[boxes.Length];
        joined[0] = true;
        for (var grew = true; grew;)
        {
            grew = false;
            for (var i = 0; i < boxes.Length; i++)
            {
                if (joined[i])
                    continue;
                for (var j = 0; j < boxes.Length; j++)
                    if (joined[j] && Touch(boxes[i], boxes[j]))
                    {
                        joined[i] = grew = true;
                        break;
                    }
            }
        }
        for (var i = 0; i < boxes.Length; i++)
            if (!joined[i])
                return i;
        return null;
    }

    private static bool Touch(in ToolBox a, in ToolBox b)
    {
        var gap = Vector3.Abs(a.Center - b.Center) - (a.Size + b.Size) * 0.5f;
        return gap.X <= 0.001f && gap.Y <= 0.001f && gap.Z <= 0.001f;
    }

    private static ToolHand Hand(JsonElement e, string file, string what)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ToolModelException($"{file}: \"{what}\" must be an object (\"at\", \"rot\", \"shape\")");
        Known(e, HandKeys, $"{file}, {what}");
        var at = Vec(Need(e, "at", $"{file}, {what}"), file, what + ".at");
        var rot = Opt(e, "rot") is { } r ? Turn(Vec(r, file, what + ".rot")) : Quaternion.Identity;
        var shape = Opt(e, "shape") is { } s ? EnumOf<HandShape>(Str(s, file, what + ".shape"), file, what + ".shape") : HandShape.Wrap;
        return new ToolHand(at, rot, shape);
    }

    /// <summary>Degrees (pitch about X, yaw about Y, roll about Z, applied in that order) to a rotation.</summary>
    private static Quaternion Turn(Vector3 deg)
    {
        var k = MathF.PI / 180f;
        return Quaternion.Concatenate(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, deg.X * k), Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg.Y * k)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, deg.Z * k));
    }

    private static Vector3 Axis(JsonElement e, string file, string what)
    {
        var v = Vec(e, file, what);
        return v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : throw new ToolModelException($"{file}: \"{what}\" must not be [0, 0, 0]");
    }

    private static void Known(JsonElement obj, string[] keys, string where)
    {
        foreach (var p in obj.EnumerateObject())
            if (!keys.Any(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ToolModelException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", keys)})");
    }

    private static JsonElement? Opt(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.Null ? null : p.Value;
        return null;
    }

    private static JsonElement Need(JsonElement obj, string name, string where) =>
        Opt(obj, name) ?? throw new ToolModelException($"{where}: missing \"{name}\"");

    private static string Str(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ToolModelException($"{file}: \"{what}\" must be text");

    private static bool Bool(JsonElement obj, string name, string file, string where) =>
        Opt(obj, name) is not { } e ? false
        : e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean()
        : throw new ToolModelException($"{file}: {where}.{name} must be true or false");

    private static float Num(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new ToolModelException($"{file}: \"{what}\" must be a number");

    private static void Range(float v, float lo, float hi, string file, string what)
    {
        if (v < lo || v > hi)
            throw new ToolModelException($"{file}: \"{what}\" is {v.ToString(CultureInfo.InvariantCulture)}, it must be {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");
    }

    private static Vector3 Vec(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3
            ? new Vector3(Num(e[0], file, what), Num(e[1], file, what), Num(e[2], file, what))
            : throw new ToolModelException($"{file}: \"{what}\" must be [x, y, z]");

    private static Vector3 Colour(JsonElement e, string file, string what)
    {
        try
        {
            return MapFile.ToColour(e, $"\"{what}\"", file + ": ");
        }
        catch (MapFileException ex)
        {
            throw new ToolModelException(ex.Message);
        }
    }

    private static T EnumOf<T>(string s, string file, string what) where T : struct, Enum =>
        Enum.TryParse<T>(s, true, out var v) && Enum.IsDefined(v) && !int.TryParse(s, out _)
            ? v
            : throw new ToolModelException($"{file}: \"{what}\" is one of {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}, not \"{s}\"");
}
