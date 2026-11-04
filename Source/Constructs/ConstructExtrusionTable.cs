using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a bulk material makes when it is extruded (one row of Constructs/Extrusion.json).</summary>
public sealed record ExtrusionMaterial(string Material, float UnitsPerM3, float HealthPerM3, float ArmourPerCm, float MaxArmour, string Class);

/// <summary>What a bulk material makes on a Wheel Former (one row of the file's "wheels"): as a material's row, and how it grips and rolls.</summary>
public sealed record WheelMaterial(string Material, float UnitsPerM3, float HealthPerM3, float ArmourPerCm, float MaxArmour, string Class, float Grip, float Offroad, float Side, float Roll, bool Flat);

/// <summary>
/// The extrusion table (Constructs/Extrusion.json; docs/sandbox/CONSTRUCTS.md, "Extrusion"): which bulk materials an Extruder
/// takes and what a cubic metre of each gives. A row may name a material Recycling.json doesn't have yet: it simply isn't
/// usable. A bad file gives the built-in table with <see cref="Problem"/> (<c>--check-construct-parts</c> prints it).
/// </summary>
public sealed class ExtrusionTable
{
    public const string Format = "call-of-slop-extrusion/1";
    public const int MaxRows = 32;

    public ExtrusionMaterial[] Rows = [];
    public WheelMaterial[] WheelRows = [];
    public string Problem = "";

    private readonly Dictionary<string, ExtrusionMaterial> _by = new();
    private readonly Dictionary<string, WheelMaterial> _wheels = new();

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Extrusion.json");

    private static ExtrusionTable? _current;

    public static ExtrusionTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (the self-test; null: load the file again).</summary>
    public static void Use(ExtrusionTable? t) => _current = t;

    private static readonly ExtrusionMaterial[] BuiltIn =
    [
        new("metal", 200, 3200, 4, 68, "steel"), new("wood", 140, 1800, 0.2f, 3, "wood"), new("planks", 280, 1800, 0.2f, 3, "wood"), new("stone", 250, 2400, 1.5f, 40, "concrete"),
    ];

    private static readonly WheelMaterial[] BuiltInWheels =
    [
        new("rubber", 120, 1800, 1, 12, "rubber", 1, 1, 1, 0.012f, true), new("metal", 100, 3200, 1, 20, "steel", 0.7f, 1.15f, 1, 0.02f, false),
    ];

    private ExtrusionTable Finish(ExtrusionMaterial[] rows, WheelMaterial[] wheels)
    {
        Rows = rows;
        WheelRows = wheels;
        foreach (var r in rows)
            _by[r.Material] = r;
        foreach (var r in wheels)
            _wheels[r.Material] = r;
        return this;
    }

    public static ExtrusionTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ExtrusionTable { Problem = $"{Path.GetFileName(path)}: {ex.Message}" }.Finish(BuiltIn, BuiltInWheels);
        }
    }

    public static ExtrusionTable Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            var rows = new List<ExtrusionMaterial>();
            var wheels = new List<WheelMaterial>();
            foreach (var p in root.EnumerateObject())
                switch (p.Name)
                {
                    case "format" or "about": break;
                    case "materials":
                        if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() is 0 or > MaxRows)
                            throw new FormatException($"\"materials\" lists 1 to {MaxRows} rows");
                        foreach (var row in p.Value.EnumerateArray())
                            rows.Add(Row(row, rows.Count + 1));
                        break;
                    case "wheels":
                        if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() is 0 or > MaxRows)
                            throw new FormatException($"\"wheels\" lists 1 to {MaxRows} rows");
                        foreach (var row in p.Value.EnumerateArray())
                            wheels.Add(WheelRow(row, wheels.Count + 1));
                        break;
                    default: throw new FormatException($"unknown key \"{p.Name}\"");
                }
            if (rows.Count == 0)
                throw new FormatException("\"materials\" is missing");
            if (rows.Select(r => r.Material).Distinct().Count() != rows.Count)
                throw new FormatException("a material is listed twice");
            if (wheels.Select(r => r.Material).Distinct().Count() != wheels.Count)
                throw new FormatException("a wheel material is listed twice");
            return new ExtrusionTable().Finish(rows.ToArray(), wheels.Count > 0 ? wheels.ToArray() : BuiltInWheels);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new ExtrusionTable { Problem = ex.Message }.Finish(BuiltIn, BuiltInWheels);
        }
    }

    private static ExtrusionMaterial Row(JsonElement e, int n)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new FormatException($"row {n} is an object");
        string material = "", cls = "";
        float units = 0, health = 0, armour = 0, max = 0;
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "material": material = p.Value.GetString() ?? ""; break;
                case "class": cls = p.Value.GetString() ?? ""; break;
                case "unitsPerM3": units = Num(p.Value, p.Name, n, 1, 100000); break;
                case "healthPerM3": health = Num(p.Value, p.Name, n, 1, 1000000); break;
                case "armourPerCm": armour = Num(p.Value, p.Name, n, 0, 1000); break;
                case "maxArmour": max = Num(p.Value, p.Name, n, 0, 1000); break;
                default: throw new FormatException($"row {n}: unknown key \"{p.Name}\"");
            }
        if (material.Length == 0 || units <= 0 || health <= 0)
            throw new FormatException($"row {n} needs \"material\", \"unitsPerM3\" and \"healthPerM3\"");
        if (!RecyclingTable.Current.Classes.ContainsKey(cls))
            throw new FormatException($"row {n}: \"class\" \"{cls}\" isn't a class in Recycling.json");
        return new ExtrusionMaterial(material, units, health, armour, max, cls);
    }

    private static WheelMaterial WheelRow(JsonElement e, int n)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new FormatException($"wheel row {n} is an object");
        string material = "", cls = "";
        float units = 0, health = 0, armour = 0, max = 0, grip = 1, offroad = 1, side = 1, roll = 0.01f;
        var flat = false;
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "material": material = p.Value.GetString() ?? ""; break;
                case "class": cls = p.Value.GetString() ?? ""; break;
                case "unitsPerM3": units = Num(p.Value, p.Name, n, 1, 100000); break;
                case "healthPerM3": health = Num(p.Value, p.Name, n, 1, 1000000); break;
                case "armourPerCm": armour = Num(p.Value, p.Name, n, 0, 1000); break;
                case "maxArmour": max = Num(p.Value, p.Name, n, 0, 1000); break;
                case "grip": grip = Num(p.Value, p.Name, n, 0.05f, 4); break;
                case "offroad": offroad = Num(p.Value, p.Name, n, 0.1f, 3); break;
                case "side": side = Num(p.Value, p.Name, n, 0.01f, 2); break;
                case "roll": roll = Num(p.Value, p.Name, n, 0, 0.5f); break;
                case "flat": flat = p.Value.GetBoolean(); break;
                default: throw new FormatException($"wheel row {n}: unknown key \"{p.Name}\"");
            }
        if (material.Length == 0 || units <= 0 || health <= 0)
            throw new FormatException($"wheel row {n} needs \"material\", \"unitsPerM3\" and \"healthPerM3\"");
        if (!RecyclingTable.Current.Classes.ContainsKey(cls))
            throw new FormatException($"wheel row {n}: \"class\" \"{cls}\" isn't a class in Recycling.json");
        return new WheelMaterial(material, units, health, armour, max, cls, grip, offroad, side, roll, flat);
    }

    private static float Num(JsonElement e, string what, int n, float min, float max)
    {
        if (e.ValueKind != JsonValueKind.Number || e.GetSingle() < min || e.GetSingle() > max)
            throw new FormatException($"row {n}: \"{what}\" is a number from {min} to {max}");
        return e.GetSingle();
    }

    /// <summary>The row for bulk material <paramref name="material"/> (a RecyclingTable id), or null when it can't be extruded.</summary>
    public ExtrusionMaterial? Find(byte material) => material != 0 && _by.TryGetValue(RecyclingTable.Current.NameOf(material), out var r) ? r : null;

    /// <summary>The wheel row for bulk material <paramref name="material"/>, or null when a Wheel Former can't use it.</summary>
    public WheelMaterial? FindWheel(byte material) => material != 0 && _wheels.TryGetValue(RecyclingTable.Current.NameOf(material), out var r) ? r : null;

    /// <summary>What a Wheel Former takes, in words (the look line).</summary>
    public string TakesWheels()
    {
        var names = RecyclingTable.Current;
        return string.Join(", ", WheelRows.Where(r => names.IdOf(r.Material) != 0).Select(r => r.Material));
    }

    /// <summary>What an Extruder takes, in words (the look line).</summary>
    public string Takes()
    {
        var names = RecyclingTable.Current;
        return string.Join(", ", Rows.Where(r => names.IdOf(r.Material) != 0).Select(r => r.Material));
    }
}
