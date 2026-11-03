using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The recycling table (Constructs/Recycling.json; docs/sandbox/CONSTRUCTS.md, "Salvage haul", decisions 14 and 31): which
/// bulk materials there are (their index is the wire id, 1-based: 0 is "none"), what a wrecked part gives back by its
/// class and mass, and how long the Recycler takes. A bad file gives the built-in defaults with <see cref="Problem"/>.
/// </summary>
public sealed class RecyclingTable
{
    public const string Format = "call-of-slop-recycling/1";
    public const int MaxMaterials = 63; // (the inventory holds 64 slots; id 0 is "none"; ids are one byte on the wire, so this is only arrays)
    public const float MinKgPerUnit = 0.01f, MaxKgPerUnit = 100f;

    public float BaseSeconds = 2f, SecondsPerKg = 0.04f, MaxSeconds = 15f;
    public string[] Materials = ["metal", "wood", "rubber", "circuits", "stone", "glass", "cloth", "sand"];
    public Vector3[] Colours = [];
    public readonly Dictionary<string, (string Gives, float PerKg)> Classes = new()
    {
        ["steel"] = ("metal", 0.5f), ["armour"] = ("metal", 0.6f), ["scrap"] = ("metal", 0.5f), ["wood"] = ("wood", 0.5f), ["rubber"] = ("rubber", 0.5f),
        ["concrete"] = ("stone", 0.25f), ["glass"] = ("glass", 0.5f), ["cloth"] = ("cloth", 0.5f), ["sand"] = ("sand", 0.5f), ["electronics"] = ("circuits", 1f),
    };
    public readonly Dictionary<string, string> Roles = new() { ["logic"] = "electronics", ["battery"] = "electronics", ["alternator"] = "electronics" };
    public float DefaultKgPerUnit = 2f;
    public float UnitsPerCubicMetre = 400f; // a Storage Volume's bulk (ConstructStoreVolume.cs)
    public float ItemPackShare = 0.7f; // ...and the share of its box parts can use: the rest is the gaps between them
    public readonly Dictionary<string, float> KgPerUnit = new()
    {
        ["metal"] = 2f, ["wood"] = 1f, ["rubber"] = 1.5f, ["circuits"] = 1f, ["stone"] = 3f, ["glass"] = 2.5f, ["cloth"] = 0.5f, ["sand"] = 2f, ["iron ore"] = 4f, ["copper"] = 2.5f, ["gold"] = 5f, ["planks"] = 0.5f,
    };
    private float[] _weights = []; // by material id - 1 (the load step reads it)
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Recycling.json");

    private static RecyclingTable? _current;

    /// <summary>The table in use (loaded once; the defaults when the file is missing or refused).</summary>
    public static RecyclingTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (the self-test; null: load the file again).</summary>
    public static void Use(RecyclingTable? t) => _current = t;

    public static RecyclingTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RecyclingTable { Problem = $"{Path.GetFileName(path)}: {ex.Message}" }.Finish();
        }
    }

    private RecyclingTable Finish()
    {
        if (Colours.Length != Materials.Length)
            Colours = Materials.Select(_ => new Vector3(0.5f)).ToArray();
        _weights = Materials.Select(m => KgPerUnit.TryGetValue(m, out var kg) ? kg : DefaultKgPerUnit).ToArray();
        return this;
    }

    public static RecyclingTable Parse(string json)
    {
        var t = new RecyclingTable();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            var colours = new Dictionary<string, Vector3>();
            var hasWeights = root.TryGetProperty("kgPerUnit", out _);
            foreach (var p in root.EnumerateObject())
                switch (p.Name)
                {
                    case "format" or "about": break;
                    case "baseSeconds": t.BaseSeconds = Num(p.Value, p.Name, 0, 120); break;
                    case "secondsPerKg": t.SecondsPerKg = Num(p.Value, p.Name, 0, 10); break;
                    case "maxSeconds": t.MaxSeconds = Num(p.Value, p.Name, 0, 600); break;
                    case "defaultKgPerUnit": t.DefaultKgPerUnit = Num(p.Value, p.Name, MinKgPerUnit, MaxKgPerUnit); break;
                    case "binUnitsPerCubicMetre": t.UnitsPerCubicMetre = Num(p.Value, p.Name, 1, 10000); break;
                    case "itemPackShare": t.ItemPackShare = Num(p.Value, p.Name, 0.1f, 1); break;
                    case "kgPerUnit":
                        if (p.Value.ValueKind != JsonValueKind.Object)
                            throw new FormatException("\"kgPerUnit\" gives each material's weight: {\"metal\": 2, ...}");
                        t.KgPerUnit.Clear();
                        foreach (var q in p.Value.EnumerateObject())
                            t.KgPerUnit[q.Name] = Num(q.Value, $"kgPerUnit.{q.Name}", MinKgPerUnit, MaxKgPerUnit);
                        break;
                    case "materials":
                        if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() is 0 or > MaxMaterials)
                            throw new FormatException($"\"materials\" lists 1 to {MaxMaterials} names");
                        t.Materials = p.Value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new FormatException("a material name is text")).ToArray();
                        if (t.Materials.Distinct().Count() != t.Materials.Length || t.Materials.Any(m => m.Length is 0 or > 12 || m.Trim() != m || m.Any(c => c != ' ' && (c < 'a' || c > 'z'))))
                            throw new FormatException("material names are 1 to 12 lower-case letters or inner spaces, each once");
                        break;
                    case "colours":
                        foreach (var q in p.Value.EnumerateObject())
                            colours[q.Name] = Hex(q.Value.GetString() ?? "");
                        break;
                    case "classes":
                        t.Classes.Clear();
                        foreach (var q in p.Value.EnumerateObject())
                        {
                            if (!q.Value.TryGetProperty("gives", out var g) || !q.Value.TryGetProperty("perKg", out var k))
                                throw new FormatException($"class \"{q.Name}\" needs \"gives\" and \"perKg\"");
                            t.Classes[q.Name] = (g.GetString() ?? "", Num(k, q.Name + ".perKg", 0.001f, 100));
                        }
                        break;
                    case "roles":
                        t.Roles.Clear();
                        foreach (var q in p.Value.EnumerateObject())
                            t.Roles[q.Name] = q.Value.GetString() ?? "";
                        break;
                    default: throw new FormatException($"unknown key \"{p.Name}\"");
                }
            foreach (var (name, (gives, _)) in t.Classes)
                if (Array.IndexOf(t.Materials, gives) < 0)
                    throw new FormatException($"class \"{name}\" gives \"{gives}\", which isn't in \"materials\"");
            foreach (var (role, cls) in t.Roles)
                if (!t.Classes.ContainsKey(cls))
                    throw new FormatException($"role \"{role}\" names class \"{cls}\", which isn't in \"classes\"");
            foreach (var name in t.KgPerUnit.Keys.Where(k => Array.IndexOf(t.Materials, k) < 0).ToArray())
            {
                if (hasWeights)
                    throw new FormatException($"\"kgPerUnit\" names \"{name}\", which isn't in \"materials\"");
                t.KgPerUnit.Remove(name); // (the built-in weights, of a file that lists fewer materials)
            }
            t.Colours = t.Materials.Select(m => colours.TryGetValue(m, out var c) ? c : new Vector3(0.5f)).ToArray();
            return t.Finish();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new RecyclingTable { Problem = ex.Message }.Finish();
        }
    }

    private static float Num(JsonElement e, string what, float min, float max)
    {
        if (e.ValueKind != JsonValueKind.Number || e.GetSingle() < min || e.GetSingle() > max)
            throw new FormatException($"\"{what}\" is a number from {min} to {max}");
        return e.GetSingle();
    }

    private static Vector3 Hex(string s)
    {
        if (s.Length != 7 || s[0] != '#' || !int.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"colour \"{s}\" is #rrggbb");
        return new Vector3((v >> 16 & 255) / 255f, (v >> 8 & 255) / 255f, (v & 255) / 255f);
    }

    /// <summary>The material id (1-based; 0 is none) for a name, or 0.</summary>
    public byte IdOf(string name)
    {
        var i = Array.IndexOf(Materials, name);
        return i < 0 ? (byte)0 : (byte)(i + 1);
    }

    public string NameOf(byte id) => id >= 1 && id <= Materials.Length ? Materials[id - 1] : "nothing";

    /// <summary>What a unit of a material weighs (kg): it counts in the mass of the body whose bin holds it.</summary>
    public float WeightOf(byte id) => id >= 1 && id <= _weights.Length ? _weights[id - 1] : DefaultKgPerUnit;

    public Vector3 ColourOf(byte id) => id >= 1 && id <= Colours.Length ? Colours[id - 1] : new Vector3(0.5f);

    /// <summary>What a wrecked part gives back: the material id and the units (at least 1), by its class and mass. 0 id: it gives nothing.</summary>
    public (byte Material, int Amount) YieldOf(in CPart p)
    {
        var d = p.D;
        if (d.Yield > 0) // a material bundle: its Setting is the material
            return p.Setting >= 1 && p.Setting <= Materials.Length ? (p.Setting, d.Yield) : ((byte)0, 0);
        var cls = Roles.TryGetValue(RoleName(d.Role), out var r) ? r : d.Material;
        if (!Classes.TryGetValue(cls, out var c) && !Classes.TryGetValue(d.Material, out c))
            return ((byte)0, 0);
        return (IdOf(c.Gives), Math.Max(1, (int)MathF.Round(d.Mass * c.PerKg)));
    }

    private static readonly string[] RoleNames = Enum.GetNames<PartRole>().Select(n => n.ToLowerInvariant()).ToArray();

    private static string RoleName(PartRole r) => RoleNames[(int)r];

    /// <summary>How long a wrecked part of this mass takes to process.</summary>
    public float SecondsFor(float mass) => MathF.Min(MaxSeconds, BaseSeconds + mass * SecondsPerKg);
}
