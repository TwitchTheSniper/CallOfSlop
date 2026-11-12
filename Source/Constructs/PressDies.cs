using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a bay 2 amount or a scaled number is measured by: the blank's length (m), its size across (m), its largest face (m2), its volume (m3) or its mass (kg).</summary>
public enum PressMeasure : byte { Length, Across, Area, Volume, Mass }

/// <summary>
/// A die's feature: a box in the blank's unit frame (x across its width, y its height, z its length, each 0 to 1), so it stretches with
/// the blank. Drawn only: a pressed part collides as its blank. <see cref="Proud"/> names the axes on which the builder moves a face
/// outward, not inward, when it would lie on another box's face (Pressed.cs: the z-fighting gap); <see cref="Clamp"/> the axes whose size
/// is held to <see cref="MinCm"/>..<see cref="MaxCm"/>.
/// </summary>
public sealed class PressFeature
{
    public string Tag = "";
    public Vector3 Lo, Hi;
    public byte Proud, Clamp; // axis bits: 1 x, 2 y, 4 z
    public float MinCm, MaxCm = float.MaxValue;
    public byte Look; // 0 the blank's colour, 1 bare, 2 dark, 3 accent
    public int[] Blanks = []; // the sections it is on (empty: every one the die takes)

    public bool On(int section) => Blanks.Length == 0 || Array.IndexOf(Blanks, section) >= 0;
}

/// <summary>A socket a die gives its parts, at a unit point on the blank's bounds.</summary>
public sealed class PressSocketSpec
{
    public string Name = "";
    public Vector3 At;
    public byte Dir; // 0..5: +x -x +y -y +z -z
}

/// <summary>An amount by formula (D4): <c>clamp(ceil(base + rate x measure), 1, max)</c> of a part or a bulk material.</summary>
public sealed class PressAmount
{
    public ConstructPartDef? Part;
    public byte Material; // (when there is no part)
    public float Base, Rate;
    public PressMeasure By;
    public int Max = 9999;

    public int For(float measure) => Math.Clamp((int)MathF.Ceiling(Base + Rate * measure - 1e-4f), 1, Max);

    public string What => Part?.Name ?? RecyclingTable.Current.NameOf(Material);
}

/// <summary>One number scaled by the law of docs section 5: <c>like x (measure / preset's)^power x (S / preset's S)</c>.</summary>
public sealed class PressScale
{
    public string Field = "";
    public PressMeasure By;
    public float Power;
    public bool Strength;
    public float Ref; // the like value when the like part's own is 0 ("no limit")
}

/// <summary>What a stroke makes of a blank: the behaviour of <see cref="Like"/>, sized by the blank.</summary>
public sealed class PressPattern
{
    public string Id = "", Label = "";
    public ConstructPartDef Like = null!;
    public ExtrusionKey Preset; // the blank whose part has the like part's numbers (the scale law's reference)
    public int[] Blanks = []; // its own sections (empty: the die's)
    public PressAmount? Bay2;
    public PressScale[] Scale = [];
    public PressFeature[] Features = []; // drawn after the die's own
}

/// <summary>A die of Pressing.json format /2: what blanks it takes and the patterns it presses them into.</summary>
public sealed class PressDie
{
    public string Family = "", Id = ""; // "Bracket", and its key word "bracket"
    public int[] Blanks = []; // Extrusion.Presets indices
    public byte[] Materials = [];
    public int AcrossMin, AcrossMax, LengthMin, LengthMax; // cm
    public float AspectMin, AspectMax;
    public float SecondsBase, SecondsPerKg;
    public PressFeature[] Features = [];
    public PressSocketSpec[] Sockets = [];
    public PressPattern[] Patterns = [];
    public PressPreset[] Presets = []; // this die's presets, in the file's order

    public PressPattern? Pattern(string id) => Array.Find(Patterns, p => p.Id == id);

    public bool Takes(PressPattern p, int section) => Array.IndexOf(p.Blanks.Length > 0 ? p.Blanks : Blanks, section) >= 0;

    /// <summary>
    /// What a press makes of this blank until the Pattern setting comes (the Press rework's phase 3): the pattern of the first preset whose
    /// blank it is (and <paramref name="preset"/>, that catalogue part), else the pattern whose standard blank of this section is nearest in
    /// size (a preset's or the pattern's own; the first listed of equals), else the first pattern that takes the section. Null: none takes
    /// it. Every peer picks the same; allocates nothing.
    /// </summary>
    public PressPattern? PatternFor(in ExtrusionKey blank, out ConstructPartDef? preset)
    {
        preset = null;
        var k = blank with { Growing = false };
        if (k.Shape != ExShape.Section || Extrusion.PresetOf(k.Mask) is not (var section and >= 0))
            return null;
        foreach (var p in Presets)
            if (p.Blank == k && Takes(p.Pattern, section))
            {
                preset = p.Part;
                return p.Pattern;
            }
        PressPattern? best = null;
        var dist = int.MaxValue;
        foreach (var p in Presets)
            Nearer(p.Pattern, p.Blank, k, section, ref best, ref dist);
        foreach (var p in Patterns)
            Nearer(p, p.Preset, k, section, ref best, ref dist);
        if (best != null)
            return best;
        foreach (var p in Patterns)
            if (Takes(p, section))
                return p;
        return null;
    }

    private void Nearer(PressPattern p, in ExtrusionKey standard, in ExtrusionKey k, int section, ref PressPattern? best, ref int dist)
    {
        if (standard.Mask != k.Mask || !Takes(p, section))
            return;
        var d = Math.Abs(standard.W - k.W) + Math.Abs(standard.H - k.H);
        if (d < dist)
        {
            dist = d;
            best = p;
        }
    }
}

/// <summary>A catalogue part that is a die's product of one standard blank (Pressing.json "presets").</summary>
public readonly record struct PressPreset(ExtrusionKey Blank, PressPattern Pattern, ConstructPartDef Part);

/// <summary>Reads Pressing.json /2's "strength", "dies" and "presets" (PressTable.Parse calls it; a bad entry refuses the whole file).</summary>
internal static class PressDies
{
    private static readonly string[] Dirs = ["+x", "-x", "+y", "-y", "+z", "-z"];
    private static readonly string[] Looks = ["blank", "bare", "dark", "accent"];
    private static readonly string[] Measures = ["length", "across", "area", "volume", "mass"];

    public static void Read(JsonElement root, Dictionary<string, ConstructPartDef> byName, PressTable t)
    {
        if (root.TryGetProperty("strength", out var st))
        {
            Keys(st, "strength", "of", "ref");
            if (st.GetProperty("of").GetString() != "healthPerM3")
                throw new FormatException("strength: \"of\" is \"healthPerM3\"");
            t.StrengthRef = Num(st.GetProperty("ref"), "strength.ref", 1, 1e6f);
        }
        if (root.TryGetProperty("dies", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > PressTable.MaxDies)
                throw new FormatException($"\"dies\" lists at most {PressTable.MaxDies} dies");
            var dies = new List<PressDie>();
            foreach (var e in list.EnumerateArray())
            {
                var die = DieOf(e, byName, t);
                if (dies.Exists(d => d.Id == die.Id))
                    throw new FormatException($"die \"{die.Family}\" is listed twice");
                dies.Add(die);
            }
            t.Dies = dies.ToArray();
        }
        if (root.TryGetProperty("presets", out var presets))
        {
            if (presets.ValueKind != JsonValueKind.Array || presets.GetArrayLength() > PressTable.MaxPresets)
                throw new FormatException($"\"presets\" lists at most {PressTable.MaxPresets}");
            var n = 0;
            foreach (var e in presets.EnumerateArray())
            {
                var where = $"preset {++n}";
                Keys(e, where, "part", "die", "pattern", "blank");
                var name = e.GetProperty("part").GetString() ?? "";
                var part = byName.GetValueOrDefault(name) ?? throw new FormatException($"{where}: no part named \"{name}\"");
                if (part.RuntimeMade)
                    throw new FormatException($"{where}: \"{name}\" isn't a part file's");
                var dieId = e.GetProperty("die").GetString() ?? "";
                var die = t.Die(dieId) ?? throw new FormatException($"{where}: no die \"{dieId}\"");
                var patId = e.GetProperty("pattern").GetString() ?? "";
                var pat = die.Pattern(patId) ?? throw new FormatException($"{where}: the {die.Family} die has no pattern \"{patId}\"");
                var blank = BlankOf(e.GetProperty("blank"), where, die, pat, t);
                var key = Pressed.KeyOf(die, pat, blank);
                if (!t.PresetByKey.TryAdd(key, part))
                    throw new FormatException($"{where}: {key} is already the preset of {t.PresetByKey[key].Name}");
                t.PresetKeyById.TryAdd(part.Id, key);
                die.Presets = [.. die.Presets, new PressPreset(blank, pat, part)];
            }
        }
    }

    private static PressDie DieOf(JsonElement e, Dictionary<string, ConstructPartDef> byName, PressTable t)
    {
        Keys(e, "a die", "die", "blanks", "materials", "across", "length", "aspect", "seconds", "features", "sockets", "patterns");
        var family = e.GetProperty("die").GetString() ?? "";
        if (family.Length is 0 or > 16 || !family.All(char.IsAsciiLetter))
            throw new FormatException($"die \"{family}\": a family is 1 to 16 letters");
        var where = $"die {family}";
        var d = new PressDie { Family = family, Id = family.ToLowerInvariant() };
        d.Blanks = Sections(e.GetProperty("blanks"), where);
        var materials = new List<byte>();
        foreach (var m in e.GetProperty("materials").EnumerateArray())
        {
            var id = m.GetString() ?? "";
            var k = RecyclingTable.Current.IdOf(id);
            if (k == 0 || ExtrusionTable.Current.Find(k) == null)
                throw new FormatException($"{where}: \"{id}\" can't be extruded (Extrusion.json)");
            materials.Add(k);
        }
        if (materials.Count == 0)
            throw new FormatException($"{where}: it lists materials");
        d.Materials = materials.ToArray();
        (d.AcrossMin, d.AcrossMax) = Range(e.GetProperty("across"), where + ".across", Extrusion.MinSectionCm, Pressed.PressMaxCm[^1]);
        (d.LengthMin, d.LengthMax) = Range(e.GetProperty("length"), where + ".length", Extrusion.StepCm, Extrusion.MaxLengthCm);
        var asp = e.GetProperty("aspect");
        if (asp.ValueKind != JsonValueKind.Array || asp.GetArrayLength() != 2)
            throw new FormatException($"{where}: \"aspect\" is [least, most]");
        d.AspectMin = Num(asp[0], where + ".aspect", 0.01f, 1000);
        d.AspectMax = Num(asp[1], where + ".aspect", d.AspectMin, 1000);
        var sec = e.GetProperty("seconds");
        Keys(sec, where + ".seconds", "base", "perKg");
        d.SecondsBase = Num(sec.GetProperty("base"), where + ".seconds.base", 0.1f, 600);
        d.SecondsPerKg = sec.TryGetProperty("perKg", out var pk) ? Num(pk, where + ".seconds.perKg", 0, 100) : 0;
        d.Features = e.TryGetProperty("features", out var fs) ? Features(fs, where, d.Blanks) : [];
        if (e.TryGetProperty("sockets", out var ss))
        {
            var socks = new List<PressSocketSpec>();
            foreach (var s in ss.EnumerateArray())
            {
                Keys(s, where + " socket", "name", "at", "dir");
                var name = s.GetProperty("name").GetString() ?? "";
                if (name.Length is 0 or > 16 || name.Any(c => c <= 32 || c > 126) || socks.Exists(x => x.Name == name))
                    throw new FormatException($"{where}: a socket's name is 1 to 16 plain characters, once");
                var dir = Array.IndexOf(Dirs, s.GetProperty("dir").GetString());
                if (dir < 0)
                    throw new FormatException($"{where}: socket {name}'s \"dir\" is one of {string.Join(", ", Dirs)}");
                socks.Add(new PressSocketSpec { Name = name, At = Unit(s.GetProperty("at"), $"{where} socket {name}", 0, 1), Dir = (byte)dir });
            }
            if (socks.Count > PressTable.MaxSockets)
                throw new FormatException($"{where}: at most {PressTable.MaxSockets} sockets");
            d.Sockets = socks.ToArray();
        }
        var pats = new List<PressPattern>();
        foreach (var p in e.GetProperty("patterns").EnumerateArray())
        {
            var pat = PatternOf(p, where, d, byName, t);
            if (pats.Exists(x => x.Id == pat.Id))
                throw new FormatException($"{where}: pattern \"{pat.Id}\" is listed twice");
            pats.Add(pat);
            d.Patterns = pats.ToArray(); // (the preset blank below checks against the die as read so far)
        }
        if (pats.Count is 0 or > PressTable.MaxPatterns)
            throw new FormatException($"{where}: 1 to {PressTable.MaxPatterns} patterns");
        return d;
    }

    private static PressPattern PatternOf(JsonElement e, string die, PressDie d, Dictionary<string, ConstructPartDef> byName, PressTable t)
    {
        Keys(e, die + " pattern", "id", "like", "label", "preset", "blanks", "bay2", "scale", "features");
        var id = e.GetProperty("id").GetString() ?? "";
        if (id.Length is 0 or > 16 || !id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            throw new FormatException($"{die}: a pattern's id is 1 to 16 of a-z, 0-9 and -");
        var where = $"{die} pattern {id}";
        var like = e.GetProperty("like").GetString() ?? "";
        var p = new PressPattern { Id = id, Like = byName.GetValueOrDefault(like) ?? throw new FormatException($"{where}: no part named \"{like}\"") };
        if (p.Like.RuntimeMade)
            throw new FormatException($"{where}: \"{like}\" isn't a part file's");
        p.Label = e.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : like;
        if (p.Label.Length is 0 or > 24 || p.Label.Any(c => c < 32 || c > 126))
            throw new FormatException($"{where}: a label is 1 to 24 plain characters");
        if (e.TryGetProperty("blanks", out var bl))
        {
            p.Blanks = Sections(bl, where);
            if (p.Blanks.Any(s => Array.IndexOf(d.Blanks, s) < 0))
                throw new FormatException($"{where}: a pattern's blanks are some of its die's");
        }
        if (e.TryGetProperty("bay2", out var b2))
        {
            Keys(b2, where + ".bay2", "part", "material", "base", "rate", "by", "max");
            var a = new PressAmount();
            if (b2.TryGetProperty("part", out var bp))
                a.Part = byName.GetValueOrDefault(bp.GetString() ?? "") ?? throw new FormatException($"{where}: bay 2's part \"{bp.GetString()}\" doesn't exist");
            else if (b2.TryGetProperty("material", out var bm) && RecyclingTable.Current.IdOf(bm.GetString() ?? "") is var m and not 0)
                a.Material = m;
            else
                throw new FormatException($"{where}: bay 2 names a \"part\" or a bulk \"material\"");
            a.Base = Num(b2.GetProperty("base"), where + ".bay2.base", 0, 1000);
            a.Rate = b2.TryGetProperty("rate", out var r) ? Num(r, where + ".bay2.rate", 0, 1000) : 0;
            a.By = MeasureOf(b2, where + ".bay2");
            a.Max = b2.TryGetProperty("max", out var mx) ? (int)Num(mx, where + ".bay2.max", 1, 9999) : 9999;
            p.Bay2 = a;
        }
        if (e.TryGetProperty("scale", out var sc))
        {
            var rows = new List<PressScale>();
            foreach (var s in sc.EnumerateArray())
            {
                Keys(s, where + ".scale", "field", "by", "power", "strength", "ref");
                var row = new PressScale { Field = s.GetProperty("field").GetString() ?? "", By = MeasureOf(s, where + ".scale"), Power = Num(s.GetProperty("power"), where + ".scale.power", -4, 4) };
                if (!Pressed.Scalable(row.Field))
                    throw new FormatException($"{where}: \"{row.Field}\" isn't a number the press scales ({string.Join(", ", Pressed.ScaledFields)})");
                row.Strength = s.TryGetProperty("strength", out var sg) && sg.ValueKind == JsonValueKind.True;
                row.Ref = s.TryGetProperty("ref", out var rf) ? Num(rf, where + ".scale.ref", 0, 1e9f) : 0;
                rows.Add(row);
            }
            p.Scale = rows.ToArray();
        }
        p.Features = e.TryGetProperty("features", out var fs) ? Features(fs, where, d.Blanks) : [];
        if (d.Features.Length + p.Features.Length > PressTable.MaxFeatures)
            throw new FormatException($"{where}: at most {PressTable.MaxFeatures} features with its die's");
        d.Patterns = [.. d.Patterns, p];
        p.Preset = BlankOf(e.GetProperty("preset"), where, d, p, t);
        d.Patterns = d.Patterns[..^1];
        return p;
    }

    private static ExtrusionKey BlankOf(JsonElement e, string where, PressDie die, PressPattern p, PressTable t)
    {
        var text = e.GetString() ?? "";
        if (Pressed.ParseBlank(text) is not { } k)
            throw new FormatException($"{where}: \"{text}\" isn't a blank as an Extruder names it (\"solid 50x50x75 steel\")");
        if (Pressed.Refusal(die, p, k) is { } why)
            throw new FormatException($"{where}: {text}: {why}");
        return k;
    }

    private static PressFeature[] Features(JsonElement list, string where, int[] dieBlanks)
    {
        var fs = new List<PressFeature>();
        foreach (var e in list.EnumerateArray())
        {
            Keys(e, where + " feature", "tag", "box", "proud", "clamp", "minCm", "maxCm", "look", "blanks");
            var f = new PressFeature { Tag = e.TryGetProperty("tag", out var tg) ? tg.GetString() ?? "" : "" };
            var at = $"{where} feature {fs.Count + 1}{(f.Tag.Length > 0 ? " (" + f.Tag + ")" : "")}";
            var box = e.GetProperty("box");
            if (box.ValueKind != JsonValueKind.Array || box.GetArrayLength() != 6)
                throw new FormatException($"{at}: \"box\" is [x0, y0, z0, x1, y1, z1] in the blank's unit frame");
            f.Lo = new Vector3(Num(box[0], at, -1, 2), Num(box[1], at, -1, 2), Num(box[2], at, -1, 2));
            f.Hi = new Vector3(Num(box[3], at, -1, 2), Num(box[4], at, -1, 2), Num(box[5], at, -1, 2));
            if (f.Hi.X <= f.Lo.X || f.Hi.Y <= f.Lo.Y || f.Hi.Z <= f.Lo.Z)
                throw new FormatException($"{at}: a box's max is above its min on every axis");
            f.Proud = Axes(e, "proud", at);
            f.Clamp = Axes(e, "clamp", at);
            f.MinCm = e.TryGetProperty("minCm", out var mn) ? Num(mn, at + ".minCm", 0.2f, 1000) : 0;
            f.MaxCm = e.TryGetProperty("maxCm", out var mx) ? Num(mx, at + ".maxCm", MathF.Max(f.MinCm, 0.2f), 10000) : float.MaxValue;
            var look = e.TryGetProperty("look", out var lk) ? Array.IndexOf(Looks, lk.GetString()) : 0;
            if (look < 0)
                throw new FormatException($"{at}: \"look\" is one of {string.Join(", ", Looks)}");
            f.Look = (byte)look;
            if (e.TryGetProperty("blanks", out var bl))
            {
                f.Blanks = Sections(bl, at);
                if (f.Blanks.Any(s => Array.IndexOf(dieBlanks, s) < 0))
                    throw new FormatException($"{at}: a feature's blanks are some of its die's");
            }
            fs.Add(f);
        }
        if (fs.Count > PressTable.MaxFeatures)
            throw new FormatException($"{where}: at most {PressTable.MaxFeatures} features");
        return fs.ToArray();
    }

    private static byte Axes(JsonElement e, string key, string where)
    {
        if (!e.TryGetProperty(key, out var v))
            return 0;
        var s = v.GetString() ?? "";
        byte bits = 0;
        foreach (var c in s)
            bits |= c switch { 'x' => (byte)1, 'y' => (byte)2, 'z' => (byte)4, _ => throw new FormatException($"{where}: \"{key}\" names axes, as \"xy\"") };
        return bits;
    }

    private static int[] Sections(JsonElement list, string where)
    {
        var sections = new List<int>();
        foreach (var e in list.EnumerateArray())
        {
            var id = e.GetString() ?? "";
            if (id is "round" or "pipe")
                throw new FormatException($"{where}: a {id} blank waits for lathe rings (the Press rework's phase 3)");
            var k = Array.FindIndex(Extrusion.Presets, p => p.Id == id);
            if (k < 0)
                throw new FormatException($"{where}: \"{id}\" isn't a section");
            if (!sections.Contains(k))
                sections.Add(k);
        }
        if (sections.Count == 0)
            throw new FormatException($"{where}: it lists blanks");
        return sections.ToArray();
    }

    private static PressMeasure MeasureOf(JsonElement e, string where)
    {
        var k = Array.IndexOf(Measures, e.GetProperty("by").GetString());
        return k >= 0 ? (PressMeasure)k : throw new FormatException($"{where}: \"by\" is one of {string.Join(", ", Measures)}");
    }

    private static (int, int) Range(JsonElement e, string where, int lo, int hi)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
            throw new FormatException($"{where}: [least, most] in cm");
        var a = (int)Num(e[0], where, lo, hi);
        var b = (int)Num(e[1], where, a, hi);
        return (a, b);
    }

    private static Vector3 Unit(JsonElement e, string where, float lo, float hi) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3
            ? new Vector3(Num(e[0], where, lo, hi), Num(e[1], where, lo, hi), Num(e[2], where, lo, hi))
            : throw new FormatException($"{where}: [x, y, z] in the blank's unit frame");

    private static float Num(JsonElement e, string where, float lo, float hi)
    {
        if (e.ValueKind != JsonValueKind.Number || !float.IsFinite(e.GetSingle()) || e.GetSingle() < lo || e.GetSingle() > hi)
            throw new FormatException($"{where}: a number from {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");
        return e.GetSingle();
    }

    private static void Keys(JsonElement e, string where, params string[] allowed)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{where}: an object");
        foreach (var p in e.EnumerateObject())
            if (Array.IndexOf(allowed, p.Name) < 0)
                throw new FormatException($"{where}: unknown key \"{p.Name}\"");
    }
}
