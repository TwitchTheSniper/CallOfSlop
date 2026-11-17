using System.Globalization;
using System.Text.Json;

namespace CallOfSlop.FactoryKit;

/// <summary>A size class (Constructs/Factories/Sizes.json, factory-generator.md 2.1): the most floor, parts and machines a building of it may have.</summary>
public sealed record FactorySize(string Name, float Length, float Depth, int Parts, int MinMachines, int MaxMachines);

/// <summary>A role a function needs and the zone it goes in (factory-generator.md 3.1, 3.3).</summary>
public sealed record FactoryRole(string Role, string Zone);

/// <summary>
/// A function file (<c>Constructs/Factories/Functions/*.json</c>, format <see cref="Format"/>): what a kind of building does, as data. Its roles,
/// its default target (material an hour), size and spare, how long intake and output stores hold, the power margin and the dock kind.
/// </summary>
public sealed class FactoryFunction
{
    public const string Format = "call-of-slop-factory-function/1";

    public string Name = "", About = "", Size = "works", Dock = "road";
    public Dictionary<string, double> Target = new();
    public double Spare, IntakeMinutes = 30, OutputMinutes = 60, PowerMargin = 0.25;
    public List<FactoryRole> Roles = new();

    public bool Has(string role) => Roles.Any(r => r.Role == role);
}

/// <summary>
/// A factory brief (factory-generator.md 2): what to build. Everything defaults from the function and its size, so a function and a style are enough.
/// Readable from JSON (<see cref="FactoryData.ReadBrief"/>) so a faction controller or a test can write one.
/// </summary>
public sealed record FactoryBrief
{
    public string Function { get; init; } = "refinery";
    public string Style { get; init; } = "Militia";
    public string? Size { get; init; }
    public int Tier { get; init; }
    public int Seed { get; init; } = 1;
    public Dictionary<string, double>? Target { get; init; }
    public double? Spare { get; init; }
    public int Defence { get; init; }
    public string? Dock { get; init; }

    public string Key => $"{Style} {Function} {Seed}";
}

/// <summary>The kit's data files under Constructs/Factories, read once.</summary>
public static class FactoryData
{
    public static string Folder => Path.Combine(AppFolders.Data, "Constructs", "Factories");

    private static List<FactorySize>? _sizes;

    public static IReadOnlyList<FactorySize> Sizes => _sizes ??= ReadSizes(Path.Combine(Folder, "Sizes.json"));

    public static FactorySize SizeOf(string name) =>
        Sizes.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new FactoryKitException($"no factory size \"{name}\" ({string.Join(", ", Sizes.Select(s => s.Name))})");

    private static readonly Dictionary<string, FactoryFunction> _functions = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> FunctionNames =>
        Directory.Exists(Path.Combine(Folder, "Functions")) ? Directory.GetFiles(Path.Combine(Folder, "Functions"), "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal).ToList() : [];

    public static FactoryFunction Function(string name)
    {
        if (_functions.TryGetValue(name, out var f))
            return f;
        var path = Path.Combine(Folder, "Functions", name.ToLowerInvariant() + ".json");
        if (!File.Exists(path))
            throw new FactoryKitException($"no factory function \"{name}\" ({string.Join(", ", FunctionNames)})");
        return _functions[name] = ReadFunction(File.ReadAllText(path), Path.GetFileName(path));
    }

    public static FactoryFunction ReadFunction(string json, string file)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var r = doc.RootElement;
        if (Str(r, "format") != FactoryFunction.Format)
            throw new FactoryKitException($"{file}: format must be \"{FactoryFunction.Format}\"");
        var f = new FactoryFunction
        {
            Name = Str(r, "name") ?? throw new FactoryKitException($"{file}: no name"),
            About = Str(r, "about") ?? "",
            Size = Str(r, "size") ?? "works",
            Dock = Str(r, "dock") ?? "road",
            Spare = Num(r, "spare", 0),
            IntakeMinutes = Num(r, "intakeMinutes", 30),
            OutputMinutes = Num(r, "outputMinutes", 60),
            PowerMargin = Num(r, "powerMargin", 0.25),
        };
        if (r.TryGetProperty("target", out var t))
            foreach (var p in t.EnumerateObject())
                f.Target[p.Name] = p.Value.GetDouble() > 0 ? p.Value.GetDouble() : throw new FactoryKitException($"{file}: target {p.Name} must be over 0");
        if (r.TryGetProperty("roles", out var roles))
            foreach (var e in roles.EnumerateArray())
                f.Roles.Add(new FactoryRole(Str(e, "role") ?? throw new FactoryKitException($"{file}: a role with no name"), Str(e, "zone") ?? "process"));
        foreach (var role in f.Roles)
            if (!FactoryPlan.ZoneLetters.ContainsKey(role.Zone))
                throw new FactoryKitException($"{file}: role {role.Role}: no zone \"{role.Zone}\" ({string.Join(", ", FactoryPlan.ZoneLetters.Keys)})");
        return f;
    }

    private static List<FactorySize> ReadSizes(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var list = new List<FactorySize>();
        foreach (var e in doc.RootElement.GetProperty("sizes").EnumerateArray())
        {
            var m = e.GetProperty("machines");
            list.Add(new FactorySize(Str(e, "name")!, (float)Num(e, "length", 32), (float)Num(e, "depth", 32), (int)Num(e, "parts", 1200), m[0].GetInt32(), m[1].GetInt32()));
        }
        return list;
    }

    /// <summary>A brief from JSON: <c>{"function": "refinery", "style": "Militia", "seed": 3, "target": {"steel": 7200}, ...}</c>.</summary>
    public static FactoryBrief ReadBrief(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var r = doc.RootElement;
        Dictionary<string, double>? target = null;
        if (r.TryGetProperty("target", out var t))
            target = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
        return new FactoryBrief
        {
            Function = Str(r, "function") ?? "refinery",
            Style = Str(r, "style") ?? "Militia",
            Size = Str(r, "size"),
            Tier = (int)Num(r, "tier", 0),
            Seed = (int)Num(r, "seed", 1),
            Target = target,
            Spare = r.TryGetProperty("spare", out var s) ? s.GetDouble() : null,
            Defence = (int)Num(r, "defence", 0),
            Dock = Str(r, "dock"),
        };
    }

    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string k, double def) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;

    public static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed class FactoryKitException(string message) : Exception(message);
