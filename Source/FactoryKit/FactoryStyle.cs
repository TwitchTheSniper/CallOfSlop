namespace CallOfSlop.FactoryKit;

/// <summary>
/// A faction's look and habits for buildings (factory-generator.md 3.6): the vehicle kit's <c>FactionStyle</c> for factories. A power's style names both,
/// so its trucks and sheds match (<see cref="VehicleStyle"/>). A shape never names a colour or a favourite part: it asks the style.
/// </summary>
public sealed class FactoryStyle
{
    public string Name = "";

    /// <summary>The vehicle kit's style of the same power (its trucks).</summary>
    public string VehicleStyle = "";

    /// <summary>Arrangement and shell weights (3.4, 3.5): the seed picks one by weight.</summary>
    public Dictionary<string, double> Arrangements = new(), Shells = new();

    /// <summary>The machine tier when the brief gives none, and the chance of one tier lower in a bank's second machine (an old one kept running).</summary>
    public int Tier = 1;

    public double OlderMachine;

    /// <summary>Spare capacity the seed picks within (0 to 0.5).</summary>
    public double SpareMin, SpareMax;

    /// <summary>Colours by role (look.md: three colours plus hazard). Each a list: the paint seed picks one.</summary>
    public Dictionary<string, string[]> Palette = new();

    /// <summary>Floor tints by zone (concrete tinted: the floor carries the area, the machines their own colours).</summary>
    public Dictionary<char, string[]> ZoneFloor = new();

    public string LampColour = "#ffd27a";

    /// <summary>The signature seen at 50 m (buildings.md rule 3): "mast", "stack" or "roof".</summary>
    public string Signature = "mast";

    /// <summary>Cell margin round a machine (metres), the seed picks within: a tidy style keeps it even, a sprawling one loose.</summary>
    public float MarginMin = 0.75f, MarginMax = 1.5f;

    /// <summary>The perimeter piece for a fenced zone (power), standing on its <c>b0_0</c> socket.</summary>
    public string Fence = "Sandbag Wall 2 m";

    /// <summary>Shipped seeds per function (the graded best, factory-generator.md 5).</summary>
    public Dictionary<string, int[]> Seeds = new();

    public string Colour(VehicleKit.Dice d, string role) => Palette.TryGetValue(role, out var c) ? d.Pick(c) : throw new FactoryKitException($"style {Name} has no colour for {role}");

    public string Floor(VehicleKit.Dice d, char zone) => ZoneFloor.TryGetValue(zone, out var c) ? d.Pick(c) : Colour(d, "floor");

    public string Pick(VehicleKit.Dice d, Dictionary<string, double> weights)
    {
        var total = weights.Values.Sum();
        var x = d.Next() * total;
        foreach (var (k, w) in weights.OrderBy(p => p.Key, StringComparer.Ordinal))
            if ((x -= w) < 0)
                return k;
        return weights.Keys.Order(StringComparer.Ordinal).Last();
    }
}

/// <summary>The factory styles. Militia first (the vehicle kit's test style, so its trucks match: the user, 2026-11-17 answer 5).</summary>
public static class FactoryStyles
{
    public static IReadOnlyList<FactoryStyle> All => [Militia];

    public static FactoryStyle Find(string name) =>
        All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new FactoryKitException($"no factory style \"{name}\" ({string.Join(", ", All.Select(s => s.Name))})");

    /// <summary>
    /// Militia: olive and dark steel, sandbagged power, a radio mast as the signature, open yards or open-sided sheds over the refiner banks (factory_shell's portal frames;
    /// halls with cladding come later). Workshop machines, now and then an older plain one in a bank.
    /// </summary>
    public static readonly FactoryStyle Militia = new()
    {
        Name = "Militia",
        VehicleStyle = "Militia",
        Arrangements = new() { ["rows"] = 1 },
        Shells = new() { ["yard"] = 0.4, ["shed"] = 0.6 }, // (a shed: portal frames and a roof over the refiner banks, open sided)
        Tier = 2,
        OlderMachine = 0.35,
        SpareMin = 0,
        SpareMax = 0.25,
        Palette = new()
        {
            ["machine"] = ["#4f5a3a", "#5c6644", "#47523a"],
            ["frame"] = ["#26282a", "#2f3236"],
            ["accent"] = ["#c8c4b0"],
            ["hazard"] = ["#d8a020"],
            ["control"] = ["#e0b020"],
            ["board"] = ["#23272b"],
            ["post"] = ["#5a5f52", "#666b5c"],
            ["power"] = ["#8a3a28", "#7a4a2a"],
            ["stock"] = ["#8a7a4a", "#7f7046"],
            ["floor"] = ["#80796a", "#7a7466"],
            ["aisle"] = ["#56603f", "#4c5638"],
            ["lane"] = ["#55534e", "#5d5a54"],
            ["roof"] = ["#5e6a52", "#6b6f5e", "#5a5f58"],
        },
        ZoneFloor = new()
        {
            ['I'] = ["#7d6a52", "#806c50"],
            ['P'] = ["#7f7a62", "#827c66"],
            ['S'] = ["#7a7563", "#77735f"],
            ['O'] = ["#6f7458", "#6c7155"],
            ['W'] = ["#7a5d4e", "#76594a"],
            ['C'] = ["#706f66", "#6c6b62"],
        },
        LampColour = "#ffcf7a",
        Signature = "mast",
        MarginMin = 0.75f,
        MarginMax = 1.75f,
        Fence = "Sandbag Wall 2 m",
        Seeds = new() { ["extraction"] = [1, 2, 3], ["refinery"] = [1, 2, 3] },
    };
}
