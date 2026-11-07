using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a bulk material makes when it is extruded (one row of Constructs/Extrusion.json).</summary>
public sealed record ExtrusionMaterial(string Material, float UnitsPerM3, float HealthPerM3, float ArmourPerCm, float MaxArmour, string Class);

/// <summary>An airfoil section (a row of the file's "airfoils"): its cells on the Extruder's grid, leading edge to the left, and how it flies.</summary>
public sealed record AirfoilRow(string Id, string Label, ulong Mask, float Cl0, float StallDeg, float Cd0);

/// <summary>What a Fan Former makes (a row of the file's "fanKinds"): per square metre of disc and by tip speed, so any size follows.</summary>
/// <summary>What an Airframe Former cuts (a row of the file's "airframes"): a lifting surface's numbers, and a control surface's when <see cref="Control"/> is above 0.</summary>
public sealed record AirframeKind(string Id, string Label, float Cl0, float StallDeg, float Cd0, float Aspect, float Sweep, float Control, float Effect, float Sign);

public sealed record FanKind(string Id, string Label, float KwPerM2, float MotorPerM2, float TipSpeed, float TopSpeed, float Duct, float Spool, float ChordShare, bool Heli);

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
    public ExtrusionMaterial[] FanRows = [];
    public AirfoilRow[] Airfoils = [];
    public FanKind[] FanKinds = [];
    public AirframeKind[] Airframes = [];
    public ExtrusionMaterial[] WingRows = [];
    public string Problem = "";

    /// <summary>
    /// The material painter's choice (the file's "painter": PartMaterials.cs): the bulk materials it offers, each a row of
    /// "materials" (whose numbers make the part's), and the roles of the parts it may remake. Empty: the tool offers nothing.
    /// </summary>
    public string[] PainterMaterials = [];
    public PartRole[] PainterRoles = [];

    private readonly Dictionary<string, ExtrusionMaterial> _by = new();
    private readonly Dictionary<string, WheelMaterial> _wheels = new();
    private readonly Dictionary<string, ExtrusionMaterial> _fans = new();
    private readonly Dictionary<string, ExtrusionMaterial> _wings = new();

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
            var fans = new List<ExtrusionMaterial>();
            var foils = new List<AirfoilRow>();
            var kinds = new List<FanKind>();
            var wings = new List<ExtrusionMaterial>();
            var frames = new List<AirframeKind>();
            JsonElement? painter = null;
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
                    case "fans":
                        foreach (var row in p.Value.EnumerateArray())
                            fans.Add(Row(row, fans.Count + 1));
                        break;
                    case "airfoils":
                        foreach (var row in p.Value.EnumerateArray())
                            foils.Add(Foil(row, foils.Count + 1));
                        break;
                    case "fanKinds":
                        foreach (var row in p.Value.EnumerateArray())
                            kinds.Add(Kind(row, kinds.Count + 1));
                        break;
                    case "wings":
                        foreach (var row in p.Value.EnumerateArray())
                            wings.Add(Row(row, wings.Count + 1));
                        break;
                    case "airframes":
                        foreach (var row in p.Value.EnumerateArray())
                            frames.Add(Frame(row, frames.Count + 1));
                        break;
                    case "painter":
                        painter = p.Value.Clone();
                        break;
                    default: throw new FormatException($"unknown key \"{p.Name}\"");
                }
            if (rows.Count == 0)
                throw new FormatException("\"materials\" is missing");
            if (rows.Select(r => r.Material).Distinct().Count() != rows.Count)
                throw new FormatException("a material is listed twice");
            if (wheels.Select(r => r.Material).Distinct().Count() != wheels.Count)
                throw new FormatException("a wheel material is listed twice");
            var t = new ExtrusionTable().Finish(rows.ToArray(), wheels.Count > 0 ? wheels.ToArray() : BuiltInWheels);
            t.FanRows = fans.ToArray();
            t.Airfoils = foils.ToArray();
            t.FanKinds = kinds.ToArray();
            t.WingRows = wings.ToArray();
            t.Airframes = frames.ToArray();
            foreach (var r in fans)
                t._fans[r.Material] = r;
            foreach (var r in wings)
                t._wings[r.Material] = r;
            if (painter is { } pt)
                t.Painter(pt);
            return t;
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

    /// <summary>"painter": {"materials": [names of rows of "materials"], "roles": [part roles]}.</summary>
    private void Painter(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new FormatException("\"painter\" is an object");
        var mats = new List<string>();
        var roles = new List<PartRole>();
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "about": break;
                case "materials":
                    foreach (var m in p.Value.EnumerateArray())
                    {
                        var name = m.GetString() ?? "";
                        if (!_by.ContainsKey(name))
                            throw new FormatException($"painter: \"{name}\" isn't a row of \"materials\"");
                        if (name.Length is 0 or > PartMaterials.MaxName || name.Any(ch => ch is < ' ' or > '~' or '(' or ')'))
                            throw new FormatException($"painter: \"{name}\" is 1 to {PartMaterials.MaxName} plain characters, no brackets");
                        if (!mats.Contains(name))
                            mats.Add(name);
                    }
                    break;
                case "roles":
                    foreach (var r in p.Value.EnumerateArray())
                        roles.Add(Enum.TryParse<PartRole>(r.GetString(), true, out var role) ? role : throw new FormatException($"painter: \"{r.GetString()}\" isn't a part role"));
                    break;
                default: throw new FormatException($"painter: unknown key \"{p.Name}\"");
            }
        if (mats.Count > PartMaterials.MaxMaterials)
            throw new FormatException($"painter: at most {PartMaterials.MaxMaterials} materials");
        PainterMaterials = mats.ToArray();
        PainterRoles = roles.ToArray();
    }

    /// <summary>The row of "materials" for a bulk material by name, or null.</summary>
    public ExtrusionMaterial? FindByName(string material) => _by.TryGetValue(material, out var r) ? r : null;

    private static AirfoilRow Foil(JsonElement e, int n)
    {
        string id = "", label = "";
        float cl0 = 0, stall = 15, cd0 = 0.01f;
        var mask = 0ul;
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "id": id = p.Value.GetString() ?? ""; break;
                case "label": label = p.Value.GetString() ?? ""; break;
                case "cl0": cl0 = Num(p.Value, p.Name, n, -1, 2); break;
                case "stall": stall = Num(p.Value, p.Name, n, 3, 45); break;
                case "cd0": cd0 = Num(p.Value, p.Name, n, 0.001f, 2); break;
                case "cells":
                    var rows = p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                    if (rows.Length != Extrusion.Grid || rows.Any(r => r.Length != Extrusion.Grid))
                        throw new FormatException($"airfoil {n}: \"cells\" is {Extrusion.Grid} rows of {Extrusion.Grid}");
                    mask = Extrusion.Cells(rows);
                    break;
                default: throw new FormatException($"airfoil {n}: unknown key \"{p.Name}\"");
            }
        if (id.Length == 0 || mask == 0 || Extrusion.Refusal(mask) is { } why)
            throw new FormatException($"airfoil {n} needs an \"id\" and good \"cells\"");
        return new AirfoilRow(id, label, mask, cl0, stall, cd0);
    }

    private static FanKind Kind(JsonElement e, int n)
    {
        string id = "", label = "";
        float kw = 0, motor = 0, tip = 0, top = 100, duct = 1, spool = 1.5f, chord = 0.1f;
        var heli = false;
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "id": id = p.Value.GetString() ?? ""; break;
                case "label": label = p.Value.GetString() ?? ""; break;
                case "kwPerM2": kw = Num(p.Value, p.Name, n, 0.1f, 100000); break;
                case "motorPerM2": motor = Num(p.Value, p.Name, n, 0, 100000); break;
                case "tipSpeed": tip = Num(p.Value, p.Name, n, 10, 500); break;
                case "topSpeed": top = Num(p.Value, p.Name, n, 1, 1000); break;
                case "duct": duct = Num(p.Value, p.Name, n, 1, 3); break;
                case "spool": spool = Num(p.Value, p.Name, n, 0.05f, 60); break;
                case "chordShare": chord = Num(p.Value, p.Name, n, 0.02f, 0.5f); break;
                case "heli": heli = p.Value.GetBoolean(); break;
                default: throw new FormatException($"fan kind {n}: unknown key \"{p.Name}\"");
            }
        if (id.Length == 0 || kw <= 0 || tip <= 0)
            throw new FormatException($"fan kind {n} needs \"id\", \"kwPerM2\" and \"tipSpeed\"");
        return new FanKind(id, label, kw, motor, tip, top, duct, spool, chord, heli);
    }

    private static AirframeKind Frame(JsonElement e, int n)
    {
        string id = "", label = "";
        float cl0 = 0, stall = 15, cd0 = 0.02f, aspect = 6, sweep = 0, control = 0, effect = 1, sign = 1;
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "id": id = p.Value.GetString() ?? ""; break;
                case "label": label = p.Value.GetString() ?? ""; break;
                case "cl0": cl0 = Num(p.Value, p.Name, n, -1, 2); break;
                case "stall": stall = Num(p.Value, p.Name, n, 3, 45); break;
                case "cd0": cd0 = Num(p.Value, p.Name, n, 0.001f, 2); break;
                case "aspect": aspect = Num(p.Value, p.Name, n, 0.2f, 40); break;
                case "sweep": sweep = Num(p.Value, p.Name, n, -75, 75); break;
                case "control": control = Num(p.Value, p.Name, n, 1, 80); break;
                case "effect": effect = Num(p.Value, p.Name, n, 0, 1); break;
                case "sign": sign = Num(p.Value, p.Name, n, -1, 1) < 0 ? -1 : 1; break;
                default: throw new FormatException($"airframe {n}: unknown key \"{p.Name}\"");
            }
        if (id.Length == 0 || id.Contains(' ') || label.Length == 0)
            throw new FormatException($"airframe {n} needs an \"id\" (one word) and a \"label\"");
        return new AirframeKind(id, label, cl0, stall, cd0, aspect, sweep, control, effect, sign);
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

    /// <summary>The fan row for bulk material <paramref name="material"/>, or null when a Fan Former can't use it.</summary>
    public ExtrusionMaterial? FindFan(byte material) => material != 0 && _fans.TryGetValue(RecyclingTable.Current.NameOf(material), out var r) ? r : null;

    /// <summary>The row for bulk material <paramref name="material"/> as a wing's skin, or null when an Airframe Former can't use it.</summary>
    public ExtrusionMaterial? FindWing(byte material) => material != 0 && _wings.TryGetValue(RecyclingTable.Current.NameOf(material), out var r) ? r : null;

    /// <summary>What an Airframe Former takes, in words (the look line).</summary>
    public string TakesWings()
    {
        var names = RecyclingTable.Current;
        return string.Join(", ", WingRows.Where(r => names.IdOf(r.Material) != 0).Select(r => r.Material));
    }

    /// <summary>What a Fan Former takes, in words (the look line).</summary>
    public string TakesFans()
    {
        var names = RecyclingTable.Current;
        return string.Join(", ", FanRows.Where(r => names.IdOf(r.Material) != 0).Select(r => r.Material));
    }

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
