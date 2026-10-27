using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

public sealed class WeaponFileException(string message) : Exception(message);

/// <summary>
/// Weapon files. A weapon is a self-contained JSON file in the Weapons folder holding every stat of a
/// <see cref="WeaponDef"/> (damage, fire rate, magazine, handling, look and sound recipe). The game loads
/// every *.json in Weapons (and its subfolders) at startup, so new weapons are added by dropping a file in.
/// See Weapons/README.md for the authoring guide.
/// </summary>
public static class WeaponFile
{
    public const string Format = "call-of-slop-weapon/1";

    /// <summary>The stat fields a weapon file holds, in declaration order (everything except per-copy attachment state).</summary>
    public static readonly FieldInfo[] Fields = typeof(WeaponDef).GetFields(BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.Name is not (nameof(WeaponDef.Id) or nameof(WeaponDef.Optic) or nameof(WeaponDef.Suppressor) or nameof(WeaponDef.Loudness) or nameof(WeaponDef.Kit)
            or nameof(WeaponDef.BulletSize) or nameof(WeaponDef.BulletSpeed))) // part and attachment stats only
        .Where(f => !f.IsDefined(typeof(RecoilKeyAttribute))) // these live in the "recoil" block (see Recoil.cs)
        .Where(f => !f.IsDefined(typeof(LauncherKeyAttribute))) // and these in a launcher's "launcher" block (LauncherStats.cs)
        .OrderBy(f => f.MetadataToken)
        .ToArray();

    /// <summary>The fields every weapon file must give (the rest have defaults).</summary>
    public static IEnumerable<FieldInfo> RequiredFields => Fields.Where(f => f.IsDefined(typeof(RequiredMemberAttribute)));

    /// <summary>Allowed range for each number. A file outside it is rejected with a message (see Weapons/README.md).</summary>
    public static readonly Dictionary<string, (float Min, float Max)> Ranges = new()
    {
        [nameof(WeaponDef.Damage)] = (1, 1000),
        [nameof(WeaponDef.HeadMult)] = (0.5f, 10),
        [nameof(WeaponDef.Pellets)] = (1, 50),
        [nameof(WeaponDef.RangeM)] = (0.5f, 1000),
        [nameof(WeaponDef.MinDamageMult)] = (0, 1),
        [nameof(WeaponDef.Rpm)] = (1, 3000),
        [nameof(WeaponDef.BurstCount)] = (1, 20),
        [nameof(WeaponDef.BurstDelay)] = (0, 5),
        [nameof(WeaponDef.Mag)] = (1, 1000),
        [nameof(WeaponDef.Reload)] = (0.1f, 20),
        [nameof(WeaponDef.HipSpreadDeg)] = (0.01f, 45),
        [nameof(WeaponDef.AdsSpreadMult)] = (0, 2),
        [nameof(WeaponDef.RecoilDeg)] = (0, 20),
        [nameof(WeaponDef.Mobility)] = (0.3f, 1.5f),
        [nameof(WeaponDef.AdsZoom)] = (1, 10),
        [nameof(WeaponDef.EquipTime)] = (0.05f, 5),
        [nameof(WeaponDef.AdsTime)] = (0.05f, 2),
        [nameof(WeaponDef.Length)] = (0.1f, 1.5f),
        [nameof(WeaponDef.Bulk)] = (0.3f, 3),
        [nameof(WeaponDef.Caliber)] = (0, 1),
        [nameof(WeaponDef.Bright)] = (0, 1),
        [nameof(WeaponDef.Tail)] = (0, 1),
        [nameof(WeaponDef.Penetration)] = (-1, 500),
        [nameof(WeaponDef.PenetrationMult)] = (0, 5),
    };

    /// <summary>The JSON key for a field: its name with a lower-case first letter ("RangeM" -> "rangeM").</summary>
    public static string Key(FieldInfo f) => char.ToLowerInvariant(f.Name[0]) + f.Name[1..];

    // ---- Writing --------------------------------------------------------------------------

    /// <summary>The weapon as a weapon file (every field written out, in a fixed order).</summary>
    public static string ToJson(WeaponDef w)
    {
        var sb = new StringBuilder("{\n");
        void Add(string k, string v) => sb.Append("  \"").Append(k).Append("\": ").Append(v).Append(",\n");
        Add("format", Str(Format));
        Add("name", Str(w.Name));
        if (w.Author.Length > 0)
            Add("author", Str(w.Author));
        Add("order", w.Order.ToString(CultureInfo.InvariantCulture));
        if (w.UnlockLevel is { } ul)
            Add("unlockLevel", ul.ToString(CultureInfo.InvariantCulture));
        if (w.DefaultOptic is { } dop)
            Add("defaultOptic", Str(dop)); // only weapons that name one, so every other weapon's text (and fingerprint) is unchanged
        foreach (var f in Fields)
        {
            if (f.Name != nameof(WeaponDef.Name))
                Add(Key(f), Fmt(f.GetValue(w)));
            if (f.Name == nameof(WeaponDef.RecoilDeg))
                Add("recoil", Recoil.BlockJson(w));
        }
        if (w.IsLauncher)
            Add("launcher", LauncherStats.BlockJson(w)); // launchers only, so every other weapon's text (and fingerprint) is unchanged
        if (w.Model != null)
            Add("model", w.Model.ToJson());
        if (w.Voice != null)
            Add("voice", w.Voice.ToJson());
        sb.Length -= 2;
        sb.Append("\n}\n");
        return sb.ToString();
    }

    internal static string Str(string s) => JsonSerializer.Serialize(s);

    internal static string Num(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    internal static string Fmt(object? v) => v switch
    {
        null => "null",
        float f => Num(f),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => Str(s),
        Enum e => Str(e.ToString()),
        Vector3 c => $"[{Num(c.X)}, {Num(c.Y)}, {Num(c.Z)}]",
        string[] list => "[" + string.Join(", ", list.Select(Str)) + "]",
        _ => throw new WeaponFileException($"can't write a {v.GetType().Name}"),
    };

    // ---- Reading --------------------------------------------------------------------------

    /// <summary>Builds a weapon from a weapon file. Throws <see cref="WeaponFileException"/> with a readable message if the file is wrong.</summary>
    public static WeaponDef Load(string json, string source)
    {
        using var doc = Parse(json, source);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{source}: a weapon file is one JSON object {{ \"name\": ..., \"class\": ..., ... }}");
        var w = WeaponDef.Blank();
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonElement? recoil = null;
        JsonElement? model = null;
        JsonElement? launcher = null;
        foreach (var p in root.EnumerateObject())
        {
            var where = $"{source} (\"{p.Name}\")";
            if (!given.Add(p.Name))
                throw new WeaponFileException($"{where}: given twice");
            switch (p.Name.ToLowerInvariant())
            {
                case "format":
                    CheckFormat(p.Value, "call-of-slop-weapon/", Format, where);
                    break;
                case "recoil":
                    recoil = p.Value; // read after the loop, over the defaults of the weapon's class
                    break;
                case "model":
                    model = p.Value; // read after the loop: its sizes scale with the weapon's length and bulk
                    break;
                case "launcher":
                    launcher = p.Value; // read after the loop: only a weapon of class Launcher may have one
                    break;
                case "voice":
                    w.Voice = VoiceSpec.Read(p.Value, where);
                    break;
                case "order":
                    w.Order = Int(p.Value, where);
                    break;
                case "unlocklevel":
                    w.UnlockLevel = Int(p.Value, where) is var lvl and >= 1 ? lvl : throw new WeaponFileException($"{where}: must be 1 or more");
                    break;
                case "author":
                    w.Author = Text(p.Value, where);
                    break;
                case "defaultoptic":
                    w.DefaultOptic = Text(p.Value, where) is { Length: > 0 } dop ? dop : throw new WeaponFileException($"{where}: must name an optic attachment");
                    break;
                default:
                    var f = Fields.FirstOrDefault(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                            ?? throw new WeaponFileException($"{source}: unknown field \"{p.Name}\". Known: format, name, author, order, unlockLevel, defaultOptic, recoil, launcher, model, voice, {string.Join(", ", Fields.Where(x => x.Name != nameof(WeaponDef.Name)).Select(Key))}");
                    f.SetValue(w, FromJson(p.Value, f.FieldType, where));
                    break;
            }
        }
        var missing = RequiredFields.Where(f => !given.Contains(Key(f))).Select(Key).ToArray();
        if (missing.Length > 0)
            throw new WeaponFileException($"{source}: missing {string.Join(", ", missing.Select(m => $"\"{m}\""))} (every weapon needs {string.Join(", ", RequiredFields.Select(Key))})");
        Recoil.ApplyClassDefaults(w);
        if (recoil is { } r)
            Recoil.ReadBlock(w, r, $"{source} (\"recoil\")");
        if (launcher is { } lb)
        {
            if (!w.IsLauncher)
                throw new WeaponFileException($"{source} (\"launcher\"): only a weapon of class \"Launcher\" has a launcher block");
            LauncherStats.ReadBlock(w, lb, $"{source} (\"launcher\")");
        }
        if (model is { } m)
            w.Model = GunSpec.Read(m, w, $"{source} (\"model\")");
        Validate(w, source);
        w.SourceFile = source;
        return w;
    }

    /// <summary>Checks names and number ranges. Throws <see cref="WeaponFileException"/> if something is out of range.</summary>
    public static void Validate(WeaponDef w, string source)
    {
        CheckName(w.Name, source, "=");
        foreach (var (field, (min, max)) in Ranges)
        {
            var f = typeof(WeaponDef).GetField(field)!;
            var v = System.Convert.ToSingle(f.GetValue(w), CultureInfo.InvariantCulture);
            if (!(v >= min && v <= max))
                throw new WeaponFileException($"{source}: \"{Key(f)}\" is {Num(v)}, but must be between {Num(min)} and {Num(max)}");
        }
        if (w.Accent.X is < 0 or > 1 || w.Accent.Y is < 0 or > 1 || w.Accent.Z is < 0 or > 1)
            throw new WeaponFileException($"{source}: \"accent\" colour values must be between 0 and 1");
        Recoil.Validate(w, source);
        if (w.IsLauncher)
            LauncherStats.Validate(w, source);
    }

    /// <summary>Names end up in save files and loadout.txt ("name=value" lines), so a few characters are off limits.</summary>
    internal static void CheckName(string name, string source, string forbidden)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 40 || name.Any(c => char.IsControl(c) || forbidden.Contains(c)))
            throw new WeaponFileException($"{source}: \"name\" must be 1-40 characters with no leading or trailing spaces{(forbidden.Length > 0 ? $" and no {string.Join(" or ", forbidden.Select(c => $"'{c}'"))}" : "")} (got {Str(name)})");
    }

    internal static JsonDocument Parse(string json, string source)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new WeaponFileException($"{source}: not valid JSON ({ex.Message})");
        }
    }

    internal static void CheckFormat(JsonElement e, string prefix, string current, string where)
    {
        if (e.ValueKind != JsonValueKind.String || !e.GetString()!.StartsWith(prefix, StringComparison.Ordinal))
            throw new WeaponFileException($"{where}: should be \"{current}\"");
    }

    internal static int Int(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i : throw new WeaponFileException($"{where}: expected a whole number, got {e}");

    internal static float Number(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number ? e.GetSingle() : throw new WeaponFileException($"{where}: expected a number, got {e}");

    internal static string Text(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new WeaponFileException($"{where}: expected text in quotes, got {e}");

    internal static string[] TextList(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => Text(x, where)).ToArray() : throw new WeaponFileException($"{where}: expected a list like [\"a\", \"b\"]");

    internal static T EnumValue<T>(JsonElement e, string where) where T : struct, Enum =>
        e.ValueKind == JsonValueKind.String && Enum.TryParse<T>(e.GetString(), true, out var v) && Enum.IsDefined(v) && !char.IsDigit(e.GetString()![0])
            ? v
            : throw new WeaponFileException($"{where}: {e} isn't one of {string.Join(", ", Enum.GetNames<T>())}");

    /// <summary>Reads a JSON value as a field of type <paramref name="t"/> (number, whole number, text, true/false, enum name or colour).</summary>
    internal static object FromJson(JsonElement e, Type t, string where)
    {
        if (t == typeof(float))
            return Number(e, where);
        if (t == typeof(int))
            return Int(e, where);
        if (t == typeof(string))
            return Text(e, where);
        if (t == typeof(bool))
            return e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw new WeaponFileException($"{where}: expected true or false, got {e}");
        if (t.IsEnum)
        {
            if (e.ValueKind == JsonValueKind.String && Enum.TryParse(t, e.GetString(), true, out var v) && Enum.IsDefined(t, v!) && !char.IsDigit(e.GetString()![0]))
                return v!;
            throw new WeaponFileException($"{where}: {e} isn't one of {string.Join(", ", Enum.GetNames(t))}");
        }
        if (t == typeof(Vector3))
        {
            if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3)
                return new Vector3(Number(e[0], where), Number(e[1], where), Number(e[2], where));
            if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: 7 } hex && hex[0] == '#'
                && int.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
            throw new WeaponFileException($"{where}: colours are [r, g, b] with values 0-1, or \"#rrggbb\"");
        }
        throw new WeaponFileException($"{where}: unsupported field type {t.Name}");
    }
}

/// <summary>Which weapons an attachment fits. Every rule that's given must pass; an empty rule set fits everything.</summary>
public sealed class AttachmentFits
{
    public WeaponClass[] Classes = [];
    public WeaponClass[] NotClasses = [];
    public SoundStyle[] Sounds = [];
    public SoundStyle[] NotSounds = [];
    public int MinMag;
    public int MaxMag; // 0 = no limit
    public string[] OnlyWeapons = [];
    public string[] NotWeapons = [];

    /// <summary>
    /// Launchers only take attachments made for them: a rule set has to name the Launcher class (or the weapon) to fit
    /// one, so the existing "everything but pistols" attachments never end up on a rocket tube.
    /// </summary>
    public bool Test(WeaponDef w) =>
        (!w.IsLauncher || Classes.Contains(WeaponClass.Launcher) || OnlyWeapons.Contains(w.Name, StringComparer.OrdinalIgnoreCase))
        && (Classes.Length == 0 || Classes.Contains(w.Class)) && !NotClasses.Contains(w.Class)
        && (Sounds.Length == 0 || Sounds.Contains(w.Sound)) && !NotSounds.Contains(w.Sound)
        && w.Mag >= MinMag && (MaxMag == 0 || w.Mag <= MaxMag)
        && (OnlyWeapons.Length == 0 || OnlyWeapons.Contains(w.Name, StringComparer.OrdinalIgnoreCase))
        && !NotWeapons.Contains(w.Name, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One stat change made by an attachment: set a value, add to it, multiply it, or "grow" a whole number
/// (multiply and round, but always at least +1, the way magazines grow), then optionally clamp it.
/// </summary>
public sealed class AttachmentEffect
{
    public required FieldInfo Field;
    public required string Op; // set, add, mul or grow
    public required object Value; // the field's type for "set", a float for the others
    public float? Min, Max;

    // Typed field accessors, compiled once per field when the file loads, so applying a kit mid-match
    // never goes through reflection.
    private Func<WeaponDef, float>? _getF;
    private Action<WeaponDef, float>? _setF;
    private Func<WeaponDef, int>? _getI;
    private Action<WeaponDef, int>? _setI;
    private Action<WeaponDef, object>? _set;

    private static readonly Dictionary<FieldInfo, Delegate[]> Accessors = new();

    /// <summary>Builds the accessors this effect needs (call once after creating it).</summary>
    public AttachmentEffect Prepare()
    {
        Delegate[]? acc;
        lock (Accessors)
        {
            if (!Accessors.TryGetValue(Field, out acc))
            {
                var w = Expression.Parameter(typeof(WeaponDef), "w");
                var field = Expression.Field(w, Field);
                var obj = Expression.Parameter(typeof(object), "v");
                var setObj = Expression.Lambda<Action<WeaponDef, object>>(Expression.Assign(field, Expression.Convert(obj, Field.FieldType)), w, obj).Compile();
                acc = [setObj];
                if (Field.FieldType == typeof(float) || Field.FieldType == typeof(int))
                {
                    var v = Expression.Parameter(Field.FieldType, "x");
                    var getter = Expression.Lambda(Expression.GetFuncType(typeof(WeaponDef), Field.FieldType), field, w).Compile();
                    var setter = Expression.Lambda(Expression.GetActionType(typeof(WeaponDef), Field.FieldType), Expression.Assign(field, v), w, v).Compile();
                    acc = [setObj, getter, setter];
                }
                Accessors[Field] = acc;
            }
        }
        _set = (Action<WeaponDef, object>)acc[0];
        if (acc.Length > 1 && Field.FieldType == typeof(float))
        {
            _getF = (Func<WeaponDef, float>)acc[1];
            _setF = (Action<WeaponDef, float>)acc[2];
        }
        else if (acc.Length > 1)
        {
            _getI = (Func<WeaponDef, int>)acc[1];
            _setI = (Action<WeaponDef, int>)acc[2];
        }
        return this;
    }

    public void ApplyTo(WeaponDef w)
    {
        if (_set == null)
            Prepare();
        if (Op == "set")
        {
            _set!(w, Value);
        }
        else if (_getF != null)
        {
            var cur = _getF(w);
            var x = (float)Value;
            var r = Op == "add" ? cur + x : cur * x;
            if (Min is { } mn)
                r = MathF.Max(mn, r);
            if (Max is { } mx)
                r = MathF.Min(mx, r);
            _setF!(w, r);
        }
        else
        {
            var cur = _getI!(w);
            var x = (float)Value;
            var r = Op switch
            {
                "add" => cur + (int)x,
                "mul" => (int)MathF.Round(cur * x),
                _ => Math.Max(cur + 1, (int)MathF.Round(cur * x)), // grow
            };
            if (Min is { } mn)
                r = Math.Max((int)mn, r);
            if (Max is { } mx)
                r = Math.Min((int)mx, r);
            _setI!(w, r);
        }
    }
}

/// <summary>
/// Attachment files: one JSON file per attachment in the Attachments folder, holding its slot, text,
/// which weapons it fits and the stat changes it makes. See Attachments/README.md.
/// </summary>
public static class AttachmentFile
{
    public const string Format = "call-of-slop-attachment/1";

    /// <summary>The weapon stats an attachment may change.</summary>
    public static readonly FieldInfo[] EffectFields = typeof(WeaponDef).GetFields(BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.Name is not (nameof(WeaponDef.Id) or nameof(WeaponDef.Name) or nameof(WeaponDef.Class) or nameof(WeaponDef.Blurb) or nameof(WeaponDef.Kit)))
        .OrderBy(f => f.MetadataToken)
        .ToArray();

    private static readonly string[] Ops = ["set", "add", "mul", "grow"];

    /// <summary>Builds an attachment from an attachment file. Throws <see cref="WeaponFileException"/> with a readable message if the file is wrong.</summary>
    public static AttachmentDef Load(string json, string source)
    {
        using var doc = WeaponFile.Parse(json, source);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{source}: an attachment file is one JSON object {{ \"name\": ..., \"slot\": ..., ... }}");
        string? name = null;
        AttachSlot? slot = null;
        var a = new AttachmentDef { Slot = AttachSlot.Optic, Name = "" };
        var fits = new AttachmentFits();
        var effects = new List<AttachmentEffect>();
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonElement? sight = null;
        foreach (var p in root.EnumerateObject())
        {
            var where = $"{source} (\"{p.Name}\")";
            if (!given.Add(p.Name))
                throw new WeaponFileException($"{where}: given twice");
            switch (p.Name.ToLowerInvariant())
            {
                case "format": WeaponFile.CheckFormat(p.Value, "call-of-slop-attachment/", Format, where); break;
                case "sight": sight = p.Value.Clone(); break;
                case "name": name = WeaponFile.Text(p.Value, where); break;
                case "slot": slot = WeaponFile.EnumValue<AttachSlot>(p.Value, where); break;
                case "order": a.Order = WeaponFile.Int(p.Value, where); break;
                case "unlockorder": a.UnlockOrder = WeaponFile.Int(p.Value, where); break;
                case "author": a.Author = WeaponFile.Text(p.Value, where); break;
                case "blurb": a.Blurb = WeaponFile.Text(p.Value, where); break;
                case "pros": a.Pros = WeaponFile.TextList(p.Value, where); break;
                case "cons": a.Cons = WeaponFile.TextList(p.Value, where); break;
                case "fits": fits = ReadFits(p.Value, where); break;
                case "model":
                    var look = WeaponFile.Text(p.Value, where);
                    a.Look = GunBuilder.Looks.FirstOrDefault(known => known.Equals(look, StringComparison.OrdinalIgnoreCase))
                             ?? throw new WeaponFileException($"{where}: unknown model \"{look}\". Known: {string.Join(", ", GunBuilder.Looks)}");
                    break;
                case "meleereach":
                    a.MeleeReach = WeaponFile.Number(p.Value, where);
                    if (a.MeleeReach is < 0 or > 2)
                        throw new WeaponFileException($"{where}: melee reach is extra metres from 0 to 2, got {a.MeleeReach}");
                    break;
                case "effects":
                    if (p.Value.ValueKind != JsonValueKind.Array)
                        throw new WeaponFileException($"{where}: expected a list of effects like [ {{ \"stat\": \"recoilDeg\", \"mul\": 0.8 }} ]");
                    var i = 0;
                    foreach (var e in p.Value.EnumerateArray())
                        effects.Add(ReadEffect(e, $"{source}, effect #{++i}"));
                    break;
                default:
                    throw new WeaponFileException($"{source}: unknown field \"{p.Name}\". Known: format, name, slot, order, unlockOrder, author, blurb, pros, cons, fits, model, meleeReach, effects, sight");
            }
        }
        if (name == null || slot == null)
            throw new WeaponFileException($"{source}: missing {(name == null ? "\"name\"" : "\"slot\"")} (every attachment needs a name and a slot: {string.Join(", ", Enum.GetNames<AttachSlot>())})");
        WeaponFile.CheckName(name, source, "=,");
        a.Name = name;
        a.Slot = slot.Value;
        a.Rule = fits;
        a.Effects = effects.ToArray();
        a.Fits = fits.Test;
        if (sight is { } sb)
        {
            // How the optic looks when aiming, on top of the defaults for the optic kind and zoom its effects set.
            var kind = a.Effects.FirstOrDefault(e => e.Field.Name == nameof(WeaponDef.Optic) && e.Op == "set")?.Value as OpticKind?;
            if (a.Slot != AttachSlot.Optic || kind is null or OpticKind.Iron)
                throw new WeaponFileException($"{source} (\"sight\"): only an optic has a sight block, and it needs an effect that sets \"optic\" (RedDot, Acog, Scope8...)");
            var zoom = a.Effects.FirstOrDefault(e => e.Field.Name == nameof(WeaponDef.AdsZoom) && e.Op == "set")?.Value is float z ? z : 1.5f;
            a.Sight = Sights.Read(sb, kind.Value, zoom, $"{source} (\"sight\")");
        }
        var list = a.Effects;
        var spec = a.Sight;
        a.Apply = w =>
        {
            foreach (var e in list)
                e.ApplyTo(w);
            if (spec != null)
                w.Sight = spec;
        };
        a.SourceFile = source;
        return a;
    }

    private static AttachmentFits ReadFits(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected {{ ... }} with any of classes, notClasses, sounds, notSounds, minMag, maxMag, weapons, notWeapons");
        var f = new AttachmentFits();
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where}.{p.Name}";
            switch (p.Name.ToLowerInvariant())
            {
                case "classes": f.Classes = EnumList<WeaponClass>(p.Value, w); break;
                case "notclasses": f.NotClasses = EnumList<WeaponClass>(p.Value, w); break;
                case "sounds": f.Sounds = EnumList<SoundStyle>(p.Value, w); break;
                case "notsounds": f.NotSounds = EnumList<SoundStyle>(p.Value, w); break;
                case "minmag": f.MinMag = WeaponFile.Int(p.Value, w); break;
                case "maxmag": f.MaxMag = WeaponFile.Int(p.Value, w); break;
                case "weapons": f.OnlyWeapons = WeaponFile.TextList(p.Value, w); break;
                case "notweapons": f.NotWeapons = WeaponFile.TextList(p.Value, w); break;
                default:
                    throw new WeaponFileException($"{where}: unknown rule \"{p.Name}\". Known: classes, notClasses, sounds, notSounds, minMag, maxMag, weapons, notWeapons");
            }
        }
        return f;
    }

    private static T[] EnumList<T>(JsonElement e, string where) where T : struct, Enum =>
        e.ValueKind == JsonValueKind.Array
            ? e.EnumerateArray().Select(x => WeaponFile.EnumValue<T>(x, where)).ToArray()
            : throw new WeaponFileException($"{where}: expected a list like [\"{Enum.GetNames<T>()[0]}\"]");

    private static AttachmentEffect ReadEffect(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected {{ \"stat\": ..., \"set\"/\"add\"/\"mul\"/\"grow\": ... }}");
        FieldInfo? field = null;
        string? op = null;
        JsonElement value = default;
        float? min = null, max = null;
        foreach (var p in e.EnumerateObject())
        {
            var key = p.Name.ToLowerInvariant();
            if (key == "stat")
            {
                var s = WeaponFile.Text(p.Value, where);
                field = EffectFields.FirstOrDefault(f => f.Name.Equals(s, StringComparison.OrdinalIgnoreCase))
                        ?? throw new WeaponFileException($"{where}: unknown stat \"{s}\". Attachments can change: {string.Join(", ", EffectFields.Select(WeaponFile.Key))}");
            }
            else if (Ops.Contains(key))
            {
                if (op != null)
                    throw new WeaponFileException($"{where}: give only one of set, add, mul or grow per effect (add another effect for a second change)");
                op = key;
                value = p.Value;
            }
            else if (key == "min")
                min = WeaponFile.Number(p.Value, where);
            else if (key == "max")
                max = WeaponFile.Number(p.Value, where);
            else
                throw new WeaponFileException($"{where}: unknown key \"{p.Name}\" (an effect has stat, one of set/add/mul/grow, and optional min/max)");
        }
        if (field == null || op == null)
            throw new WeaponFileException($"{where}: an effect needs \"stat\" and one of \"set\", \"add\", \"mul\" or \"grow\"");
        var numeric = field.FieldType == typeof(float) || field.FieldType == typeof(int);
        object v;
        if (op == "set")
            v = WeaponFile.FromJson(value, field.FieldType, $"{where} ({WeaponFile.Key(field)})");
        else if (!numeric)
            throw new WeaponFileException($"{where}: \"{WeaponFile.Key(field)}\" isn't a number, so it can only be \"set\"");
        else if (op == "grow" && field.FieldType != typeof(int))
            throw new WeaponFileException($"{where}: \"grow\" is for whole numbers like \"mag\"; use \"mul\" for \"{WeaponFile.Key(field)}\"");
        else
        {
            var f = WeaponFile.Number(value, where);
            if (op == "add" && field.FieldType == typeof(int) && f != MathF.Floor(f))
                throw new WeaponFileException($"{where}: \"{WeaponFile.Key(field)}\" is a whole number, so \"add\" must be too");
            v = f;
        }
        if ((min != null || max != null) && (!numeric || op == "set"))
            throw new WeaponFileException($"{where}: min/max only go with add, mul or grow on a number");
        return new AttachmentEffect { Field = field, Op = op, Value = v, Min = min, Max = max }.Prepare();
    }

    /// <summary>The attachment as an attachment file (only for attachments loaded from files: code-built ones have no rules to write).</summary>
    public static string ToJson(AttachmentDef a)
    {
        var sb = new StringBuilder("{\n");
        void Add(string k, string v) => sb.Append("  \"").Append(k).Append("\": ").Append(v).Append(",\n");
        string Num(float f) => WeaponFile.Num(f);
        Add("format", WeaponFile.Str(Format));
        Add("name", WeaponFile.Str(a.Name));
        if (a.Author.Length > 0)
            Add("author", WeaponFile.Str(a.Author));
        Add("slot", WeaponFile.Str(a.Slot.ToString()));
        Add("order", a.Order.ToString(CultureInfo.InvariantCulture));
        Add("unlockOrder", a.UnlockOrder.ToString(CultureInfo.InvariantCulture));
        Add("blurb", WeaponFile.Str(a.Blurb));
        Add("pros", WeaponFile.Fmt(a.Pros));
        Add("cons", WeaponFile.Fmt(a.Cons));
        var r = a.Rule ?? new AttachmentFits();
        var rules = new List<string>();
        void Rule<T>(string k, T[] list) where T : notnull
        {
            if (list.Length > 0)
                rules.Add($"\"{k}\": [{string.Join(", ", list.Select(x => WeaponFile.Str(x.ToString()!)))}]");
        }
        Rule("classes", r.Classes);
        Rule("notClasses", r.NotClasses);
        Rule("sounds", r.Sounds);
        Rule("notSounds", r.NotSounds);
        if (r.MinMag != 0)
            rules.Add($"\"minMag\": {r.MinMag}");
        if (r.MaxMag != 0)
            rules.Add($"\"maxMag\": {r.MaxMag}");
        Rule("weapons", r.OnlyWeapons);
        Rule("notWeapons", r.NotWeapons);
        Add("fits", rules.Count == 0 ? "{}" : "{ " + string.Join(", ", rules) + " }");
        if (a.Look != null)
            Add("model", WeaponFile.Str(a.Look));
        if (a.MeleeReach != 0)
            Add("meleeReach", Num(a.MeleeReach));
        var effects = a.Effects.Select(e =>
        {
            var parts = new List<string> { $"\"stat\": {WeaponFile.Str(WeaponFile.Key(e.Field))}", $"\"{e.Op}\": {(e.Op == "set" ? WeaponFile.Fmt(e.Value) : Num((float)e.Value))}" };
            if (e.Min is { } mn)
                parts.Add($"\"min\": {Num(mn)}");
            if (e.Max is { } mx)
                parts.Add($"\"max\": {Num(mx)}");
            return "    { " + string.Join(", ", parts) + " }";
        }).ToArray();
        sb.Append("  \"effects\": ").Append(effects.Length == 0 ? "[]" : "[\n" + string.Join(",\n", effects) + "\n  ]");
        if (a.Sight != null)
            sb.Append(",\n  \"sight\": ").Append(Sights.ToJson(a.Sight));
        sb.Append("\n}\n");
        return sb.ToString();
    }
}

/// <summary>
/// The loaded weapon set as a whole: fingerprints (host and clients must match), load problems for the
/// lobby, and the modders' command-line helpers.
/// </summary>
public static class WeaponSet
{
    private static uint? _weaponsHash, _attachmentsHash;

    /// <summary>FNV-1a over every loaded weapon, written out canonically (so line endings and spacing don't matter).</summary>
    /// Always the weapons as their files have them (<see cref="WeaponTweaks.FileDef"/>): live tweaks never change it.
    public static uint WeaponsHash => _weaponsHash ??= Fnv(Weapons.All.Select(w => WeaponFile.ToJson(WeaponTweaks.FileDef(w))));

    /// <summary>Forgets the fingerprints (the weapon tweak window just saved weapon files: they are the new baseline).</summary>
    internal static void ResetHashes() => _weaponsHash = null;

    /// <summary>FNV-1a over every loaded attachment, in slot and index order.</summary>
    public static uint AttachmentsHash => _attachmentsHash ??= Fnv(Attachments.BySlot.SelectMany(s => s).Select(a => a.Rule != null ? AttachmentFile.ToJson(a) : a.Name));

    /// <summary>One number for the whole set (shown in the log and by --check-weapon).</summary>
    public static uint Fingerprint => Fnv([WeaponsHash.ToString("x8"), AttachmentsHash.ToString("x8")]);

    public static uint Fnv(IEnumerable<string> parts)
    {
        var h = 2166136261u;
        foreach (var part in parts)
        {
            foreach (var b in Encoding.UTF8.GetBytes(part))
            {
                h ^= b;
                h *= 16777619u;
            }
            h ^= 0xFF; // separator, so ["ab", "c"] and ["a", "bc"] differ
            h *= 16777619u;
        }
        return h;
    }

    /// <summary>
    /// Client side of the Welcome check: null when our weapons and attachments match the host's, otherwise a
    /// message saying which folder differs and what to do about it.
    /// </summary>
    public static string? Mismatch(uint hostWeapons, byte hostWeaponCount, uint hostAttachments, byte hostAttachmentCount)
    {
        var problems = new List<string>();
        var myWeapons = Weapons.All.Length;
        var myAttachments = Attachments.BySlot.Sum(s => s.Length);
        if (hostWeapons != WeaponsHash)
            problems.Add(hostWeaponCount != myWeapons
                ? $"your Weapons folder has {myWeapons} weapons but the host's has {hostWeaponCount}"
                : $"your Weapons folder has the same {myWeapons} weapons as the host's, but with different stats, names or order");
        if (hostAttachments != AttachmentsHash)
            problems.Add(hostAttachmentCount != myAttachments
                ? $"your Attachments folder has {myAttachments} attachments but the host's has {hostAttachmentCount}"
                : $"your Attachments folder has the same {myAttachments} attachments as the host's, but they're set up differently");
        if (problems.Count == 0)
            return null;
        return $"Can't join: {string.Join("; ", problems)}. Everyone in a game needs identical Weapons and Attachments folders: copy the host's over yours. "
               + $"(weapon set {hostWeapons:x8}/{hostAttachments:x8} on the host, {WeaponsHash:x8}/{AttachmentsHash:x8} here)";
    }

    /// <summary>A one-line summary of weapon and attachment files that didn't load (null if all is well). Shown in the lobby.</summary>
    public static string? ErrorSummary
    {
        get
        {
            _ = Weapons.All;
            _ = Attachments.BySlot;
            var errors = Weapons.LoadErrors.Concat(Attachments.LoadErrors).ToList();
            return errors.Count == 0
                ? null
                : $"{errors.Count} weapon/attachment file(s) didn't load: {errors[0]}{(errors.Count > 1 ? " (more in callofslop.log)" : "")}";
        }
    }

    /// <summary>
    /// "CallOfSlop.exe --check-weapon FILE": loads one weapon (or attachment) file and reports problems and key numbers.
    /// Exit code 0 = it loads; 1 = it doesn't (the reason is printed).
    /// </summary>
    public static int Check(string file)
    {
        try
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            if (IsAttachmentFile(text))
                return CheckAttachment(text, file);
            var w = WeaponFile.Load(text, name);
            Console.WriteLine($"OK: '{w.Name}' ({w.ClassName}, {w.Slot} slot, {w.ModeName}): {w.Damage}{(w.Pellets > 1 ? $" x {w.Pellets} pellets" : "")} damage, {w.Rpm} rpm, "
                              + $"{w.Dps:0} DPS, {w.ShotsToKill(false)} body / {w.ShotsToKill(true)} head shots to kill, mag {w.Mag}, reload {w.Reload:0.0#} s, "
                              + $"full damage to {w.RangeM:0.#} m, fingerprint {Fnv([WeaponFile.ToJson(w)]):x8}");
            var fits = Attachments.BySlot.SelectMany(s => s).Where(a => a.Fits(w)).Select(a => a.Name).ToArray();
            Console.WriteLine($"attachments that fit ({fits.Length}): {(fits.Length == 0 ? "none" : string.Join(", ", fits))}");
            if (w.Order == 1000)
                Console.WriteLine("note: no \"order\", so it's listed after the shipped weapons (they use 1-50)");
            Console.WriteLine(w.UnlockLevel is { } ul
                ? $"note: unlocks at level {Math.Clamp(ul, 1, Progress.MaxLevel)}{(ul > Progress.MaxLevel ? $" (max level is {Progress.MaxLevel})" : "")}"
                : Progress.HasBuiltInUnlock(w.Name) ? "note: unlocks at its built-in level" : $"note: no \"unlockLevel\", so it unlocks at the max level ({Progress.MaxLevel})");
            var startOptic = Attachments.ResolveDefaultOptic(w);
            if (w.DefaultOptic != null && !string.Equals(startOptic?.Name, w.DefaultOptic, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"note: \"defaultOptic\" names '{w.DefaultOptic}', which isn't an optic that fits it");
            if (startOptic != null)
                Console.WriteLine($"note: starts with the {startOptic.Name}; its iron sights unlock half-way through its attachment XP");
            if (Weapons.Find(w.Name) is { SourceFile: { } other } && !SameFile(Path.Combine(Weapons.Folder, other), file))
                Console.WriteLine($"note: '{w.Name}' is already in the Weapons folder ({other}); names must be unique, so only one of them would load");
            return 0;
        }
        catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    /// <summary>An attachment file has a "slot" (or says so in "format"); anything else is treated as a weapon file.</summary>
    private static bool IsAttachmentFile(string text)
    {
        try
        {
            using var doc = WeaponFile.Parse(text, "");
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.EnumerateObject().Any(p =>
                p.Name.Equals("slot", StringComparison.OrdinalIgnoreCase)
                || (p.Name.Equals("format", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String
                    && p.Value.GetString()!.StartsWith("call-of-slop-attachment/", StringComparison.Ordinal)));
        }
        catch (WeaponFileException)
        {
            return false; // not valid JSON: the weapon loader reports it properly
        }
    }

    private static int CheckAttachment(string text, string file)
    {
        var a = AttachmentFile.Load(text, Path.GetFileName(file));
        var fits = Weapons.All.Where(a.Fits).Select(w => w.Name).ToArray();
        Console.WriteLine($"OK: '{a.Name}' ({Attachments.SlotName(a.Slot)} slot, {a.Effects.Length} effect(s)) fits {fits.Length} of {Weapons.All.Length} weapons: {(fits.Length == 0 ? "none" : string.Join(", ", fits))}");
        if (Attachments.Find(a.Name) is { SourceFile: { } other } && !SameFile(Path.Combine(Attachments.Folder, other), file))
            Console.WriteLine($"note: '{a.Name}' is already in the Attachments folder ({other}); names must be unique");
        if (a.Effects.Length == 0)
            Console.WriteLine("note: no effects, so it's purely cosmetic");
        return 0;
    }

    private static bool SameFile(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>"CallOfSlop.exe --export-weapons DIR": writes the loaded weapons and attachments back out as files.</summary>
    public static int Export(string dir)
    {
        foreach (var w in Weapons.All)
        {
            var folder = Path.Combine(dir, "Weapons", w.Class.ToString());
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"{w.Order:00}-{Slug(w.Name)}.json"), WeaponFile.ToJson(w));
        }
        foreach (var a in Attachments.BySlot.SelectMany(s => s).Where(a => a.Rule != null))
        {
            var folder = Path.Combine(dir, "Attachments", a.Slot.ToString());
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"{a.Index:00}-{Slug(a.Name)}.json"), AttachmentFile.ToJson(a));
        }
        Console.WriteLine($"[weapons] wrote {Weapons.All.Length} weapon file(s) and {Attachments.BySlot.Sum(s => s.Length)} attachment file(s) to {Path.GetFullPath(dir)}");
        return 0;
    }

    private static string Slug(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());
}
