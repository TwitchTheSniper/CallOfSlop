using System.Text.Json;

namespace CallOfSlop;

/// <summary>What one bay of a stamping recipe takes.</summary>
public sealed class PressBaySpec
{
    public string Label = ""; // "Strip", "Rod": on the panel
    public int[] Sections = []; // Extrusion.Presets indices, or RoundSection / PipeSection
    public byte[] Materials = [];
    public int MinCm, MaxCm; // across the piece
    public int UseCm; // a stroke's length, or
    public float UseAcross; // ...this many times its size across

    public const int RoundSection = 100, PipeSection = 101;

    /// <summary>The section a key is, in <see cref="Sections"/>' terms (-1: a custom grid or a wheel).</summary>
    public static int SectionOf(in ExtrusionKey k) => k.Shape switch
    {
        ExShape.Round => RoundSection,
        ExShape.Pipe => PipeSection,
        ExShape.Section => Extrusion.PresetOf(k.Mask),
        _ => -1,
    };

    public bool Takes(in ExtrusionKey k) => Array.IndexOf(Materials, k.Material) >= 0 && Array.IndexOf(Sections, SectionOf(k)) >= 0 && Across(k) >= MinCm && Across(k) <= MaxCm;

    /// <summary>A piece's size across: what sets a part's size. Its smaller side, so a flat strip is sized by its width only when the section says so.</summary>
    public static int Across(in ExtrusionKey k) => Math.Min((int)k.W, k.H);

    /// <summary>The length a stroke takes off this piece, on the 5 cm step.</summary>
    public int StrokeCm(in ExtrusionKey k)
    {
        var cm = UseAcross > 0 ? (int)MathF.Ceiling(Across(k) * UseAcross) : UseCm;
        return Math.Max(Extrusion.StepCm, (cm + Extrusion.StepCm - 1) / Extrusion.StepCm * Extrusion.StepCm);
    }
}

/// <summary>One line of Pressing.json: with this die (or chosen on a lathe) and these pieces in its bays, a stroke makes this.</summary>
public sealed class PressRow
{
    public string Die = ""; // the die family ("" on a lathe's list)
    public ConstructPartDef Output = null!;
    public int Count = 1;
    public float Seconds = 1;
    public PressBaySpec[] Bays = [];
    public int Ticks => (int)MathF.Ceiling(Seconds * World.TickRate - 0.001f);
}

/// <summary>
/// The stamping table (Constructs/Pressing.json, format call-of-slop-pressing/1, written by tools/constructs/press_parts.py;
/// docs/sandbox/CONSTRUCTS.md "The Press"). <see cref="Rows"/> are a press's: flat, one line per die family, material and size
/// band, so a die with a dozen lines is a dozen rows and the first that its bays satisfy runs. <see cref="Lathe"/> are a Die
/// Lathe's, chosen as a setting. A file that is refused gives no recipes and <see cref="Problem"/>.
/// </summary>
public sealed class PressTable
{
    public const string Format = "call-of-slop-pressing/1";
    public const int MaxRows = 2000, MaxBays = 3;
    public const string NoRecipe = "(no die chosen)";

    public PressRow[] Rows = [];
    public PressRow[] Lathe = [];
    public string[] LatheChoices = [NoRecipe];
    public string Problem = "";

    private readonly Dictionary<string, (int First, int Count)> _families = new(StringComparer.OrdinalIgnoreCase);

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Pressing.json");

    private static PressTable? _current;

    public static PressTable Current
    {
        get
        {
            if (_current == null)
                _ = ConstructParts.All;
            return _current ?? new PressTable { Problem = "the parts haven't loaded" };
        }
    }

    /// <summary>Sets the table in use (null: read the file again, against the parts as loaded).</summary>
    public static void Use(PressTable? t)
    {
        _current = t ?? Load(FilePath, ConstructParts.All);
        Fill(ConstructParts.All, _current);
    }

    /// <summary>Called as the parts load: reads the table and fills every Die Lathe's choice.</summary>
    public static ConstructPartDef[] Attach(ConstructPartDef[] parts)
    {
        _current = Load(FilePath, parts);
        Fill(parts, _current);
        return parts;
    }

    private static void Fill(IReadOnlyList<ConstructPartDef> parts, PressTable t)
    {
        foreach (var d in parts)
        {
            if (d.IsLathe && d.CfgSlot[(int)CfgKey.Recipe] >= 0)
                d.Config[d.CfgSlot[(int)CfgKey.Recipe]].Options = t.LatheChoices;
            if (d.DieFamily.Length > 0)
                d.DieRows = t._families.GetValueOrDefault(d.DieFamily);
        }
    }

    public static PressTable Load(string path, IReadOnlyList<ConstructPartDef> parts)
    {
        try
        {
            return Parse(File.ReadAllText(path), parts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PressTable { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    /// <summary>Reads the table against <paramref name="parts"/>; a bad file gives no recipes and the reason.</summary>
    public static PressTable Parse(string json, IReadOnlyList<ConstructPartDef> parts)
    {
        try
        {
            var byName = new Dictionary<string, ConstructPartDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in parts)
                byName[d.Name] = d;
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "rows" or "lathe"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            var t = new PressTable
            {
                Rows = RowsOf(root, "rows", byName, true),
                Lathe = RowsOf(root, "lathe", byName, false),
            };
            t.LatheChoices = [NoRecipe, .. t.Lathe.Select(r => r.Output.Name)];
            for (var k = 0; k < t.Rows.Length; k++)
            {
                var fam = t.Rows[k].Die;
                if (t._families.TryGetValue(fam, out var have))
                {
                    if (have.First + have.Count != k)
                        throw new FormatException($"rows of die \"{fam}\" must stand together");
                    t._families[fam] = (have.First, have.Count + 1);
                }
                else
                    t._families[fam] = (k, 1);
            }
            return t;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return new PressTable { Problem = $"{Path.GetFileName(FilePath)}: {ex.Message}" };
        }
    }

    private static PressRow[] RowsOf(JsonElement root, string key, Dictionary<string, ConstructPartDef> byName, bool die)
    {
        if (!root.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaxRows)
            throw new FormatException($"\"{key}\" lists 1 to {MaxRows} recipes");
        var rows = new List<PressRow>();
        foreach (var e in list.EnumerateArray())
        {
            var where = $"{key} {rows.Count + 1}";
            var r = new PressRow { Die = die ? e.GetProperty("die").GetString() ?? "" : "" };
            var name = e.GetProperty("output").GetString() ?? "";
            r.Output = byName.GetValueOrDefault(name) ?? throw new FormatException($"{where}: no part named \"{name}\"");
            r.Count = e.TryGetProperty("count", out var n) ? n.GetInt32() : 1;
            r.Seconds = e.GetProperty("seconds").GetSingle();
            if (r.Count is < 1 or > 12 || r.Seconds is < 0.1f or > 600)
                throw new FormatException($"{where}: count 1 to 12, seconds 0.1 to 600");
            var bays = new List<PressBaySpec>();
            foreach (var b in e.GetProperty("bays").EnumerateArray())
                bays.Add(BayOf(b, $"{where}, bay {bays.Count + 1}"));
            if (bays.Count is 0 or > MaxBays)
                throw new FormatException($"{where}: 1 to {MaxBays} bays");
            r.Bays = bays.ToArray();
            rows.Add(r);
        }
        return rows.ToArray();
    }

    private static PressBaySpec BayOf(JsonElement b, string where)
    {
        var s = new PressBaySpec { Label = b.GetProperty("label").GetString() ?? "" };
        if (s.Label.Length is 0 or > 12 || s.Label.Any(c => c < 32 || c > 126))
            throw new FormatException($"{where}: a label is 1 to 12 plain characters");
        var sections = new List<int>();
        foreach (var e in b.GetProperty("sections").EnumerateArray())
        {
            var id = e.GetString() ?? "";
            var k = id.ToLowerInvariant() switch
            {
                "round" => PressBaySpec.RoundSection,
                "pipe" => PressBaySpec.PipeSection,
                _ => Array.FindIndex(Extrusion.Presets, p => p.Id == id),
            };
            if (k < 0)
                throw new FormatException($"{where}: \"{id}\" isn't a section");
            sections.Add(k);
        }
        var materials = new List<byte>();
        foreach (var e in b.GetProperty("materials").EnumerateArray())
        {
            var id = e.GetString() ?? "";
            var m = RecyclingTable.Current.IdOf(id);
            if (m == 0 || ExtrusionTable.Current.Find(m) == null)
                throw new FormatException($"{where}: \"{id}\" can't be extruded (Extrusion.json)");
            materials.Add(m);
        }
        if (sections.Count == 0 || materials.Count == 0)
            throw new FormatException($"{where}: it lists sections and materials");
        s.Sections = sections.ToArray();
        s.Materials = materials.ToArray();
        s.MinCm = (int)MathF.Round(b.GetProperty("minAcross").GetSingle() * 100);
        s.MaxCm = (int)MathF.Round(b.GetProperty("maxAcross").GetSingle() * 100);
        s.UseCm = b.TryGetProperty("use", out var u) ? (int)MathF.Round(u.GetSingle() * 100) : 0;
        s.UseAcross = b.TryGetProperty("useAcross", out var a) ? a.GetSingle() : 0;
        if (s.MinCm > s.MaxCm || s.UseCm <= 0 && s.UseAcross <= 0)
            throw new FormatException($"{where}: minAcross up to maxAcross, and \"use\" (m a stroke) or \"useAcross\" (times its size across)");
        return s;
    }
}
