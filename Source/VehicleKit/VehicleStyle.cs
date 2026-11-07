namespace CallOfSlop.VehicleKit;

/// <summary>
/// Seeded choices. The same seed gives the same choices on every machine: no <c>Random</c>, no <c>HashCode</c>, no maths that
/// varies with the platform. Each feature takes its own <see cref="Fork"/> by name, so a new roll in the paint doesn't change the chassis.
/// </summary>
public sealed class Dice(uint seed)
{
    private uint _s = seed * 2654435761u + 12345u;

    public static Dice Of(int seed) => new((uint)seed);

    /// <summary>A separate stream for a topic ("paint", "panels"), fixed by the seed and the topic's name.</summary>
    public Dice Fork(string topic)
    {
        var h = 2166136261u ^ seed;
        foreach (var ch in topic)
            h = (h ^ ch) * 16777619u;
        return new Dice(h);
    }

    /// <summary>The next value in [0, 1).</summary>
    public double Next()
    {
        _s = unchecked(_s * 1664525u + 1013904223u);
        var x = _s ^ (_s >> 15); // (the low bits of a congruential generator are poor; a few hundred rolls never see the period)
        return x / 4294967296.0;
    }

    public double Between(double a, double b) => a + (b - a) * Next();

    /// <summary>A whole number from <paramref name="lo"/> up to and including <paramref name="hi"/>.</summary>
    public int Int(int lo, int hi) => lo + (int)Math.Floor(Next() * (hi - lo + 1));

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];

    public bool Chance(double p) => Next() < p;

    /// <summary>The list in a seeded order.</summary>
    public List<T> Shuffled<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Int(0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}

/// <summary>
/// One gun a faction may mount, described by its parts in the weapon builder's own text: a receiver with its parts by builder socket, as in
/// <c>Parts/Builds</c> ("Whaler Receiver{barrel:Harpoon Tube,feed:Harpoon Breech,ammo:Barbed Head,action:Harpoon Trigger}", a barrel may hold "{muzzle:...}").
/// The kit fits them as parts on a construct and the game's build rules judge the gun: what they refuse throws with the reason.
/// <paramref name="Box"/>: what feeds it, bolted to its feed (null: nothing; a flame gun drinks the construct's fuel).
/// </summary>
public sealed record WeaponOption(string Name, string Build, string? Box = "Ammo Box")
{
    /// <summary>The build's receiver and its parts by socket; a value may carry its own braces.</summary>
    public static (string Part, Dictionary<string, string> Children) Parse(string text)
    {
        var open = text.IndexOf('{');
        var children = new Dictionary<string, string>();
        if (open < 0)
            return (text.Trim(), children);
        var body = text[(open + 1)..text.LastIndexOf('}')];
        var (depth, start) = (0, 0);
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length && body[i] == '{')
                depth++;
            else if (i < body.Length && body[i] == '}')
                depth--;
            if (i == body.Length || body[i] == ',' && depth == 0)
            {
                var one = body[start..i];
                var colon = one.IndexOf(':');
                if (colon < 0)
                    throw new VehicleKitException($"a gun build \"{text}\": \"{one}\" isn't socket:part");
                children[one[..colon].Trim()] = one[(colon + 1)..].Trim();
                start = i + 1;
            }
        }
        return (text[..open].Trim(), children);
    }
}

/// <summary>
/// A faction's look and what its seeds may vary: palette, part preferences and limits. A generator asks the style, never names a
/// colour or a preferred part itself, so a second faction is a second style over the same generator (docs/vehiclekit/VEHICLEKIT.md).
/// A slot's list is the choices; a name given twice is twice as likely.
/// </summary>
public sealed class FactionStyle
{
    public required string Name { get; init; }

    /// <summary>The group its blueprints show under in the inventory's Blueprints tab.</summary>
    public required string Group { get; init; }

    /// <summary>Colours by role ("body", "accent", "war"...).</summary>
    public required Dictionary<string, string[]> Palette { get; init; }

    /// <summary>Part choices by slot ("wheel", "engine", "sheet", "trophy"...).</summary>
    public required Dictionary<string, string[]> Parts { get; init; }

    /// <summary>Running gear a seed may roll, for a light vehicle and a heavy one (<see cref="Vehicle.PickGear"/>).</summary>
    public Gear[] LightGear { get; init; } = [];

    public Gear[] HeavyGear { get; init; } = [];

    /// <summary>The guns a seed may mount.</summary>
    public required WeaponOption[] Weapons { get; init; }

    /// <summary>The loot table its containers roll from (Constructs/Loot.json).</summary>
    public string LootTable { get; init; } = "";

    /// <summary>
    /// Seeds that ship by shape ("Box Truck"), for a shape whose own seeds fail in this style: a seed's dice fall differently
    /// over another style's lists. A shape not named ships its own seeds.
    /// </summary>
    public Dictionary<string, int[]> Seeds { get; init; } = [];

    /// <summary>Shapes only some styles build (<see cref="VehicleRegistry.Extras"/>), by name ("Double Decker"): this style ships them too.</summary>
    public string[] Extra { get; init; } = [];

    /// <summary>
    /// Shape grammar weights (kit 2: <see cref="ShapeGrammar"/>, docs/vehiclekit/VEHICLEKIT.md "Shape grammar"), by family ("nose", "cab", "body", "rear", "under", "flank", "sides")
    /// and module name ("long hood", "caged", "room", "porch"...): how much likelier this faction makes a module than the default. A family or a module not named
    /// keeps its default, which comes from the style's own slots (a nose from its "hood" list, a cab from its "heavyCab" list), so a style that names none works unchanged.
    /// 0 rules a module out; a module whose parts the style doesn't list is never made, whatever its weight.
    /// </summary>
    public Dictionary<string, Dictionary<string, double>> Modules { get; init; } = [];

    /// <summary>A module's weight in this style: its default times the style's own factor (<see cref="Modules"/>).</summary>
    public double ModuleWeight(string family, string module, double fallback) =>
        Modules.TryGetValue(family, out var f) && f.TryGetValue(module, out var w) ? fallback * w : fallback;

    /// <summary>Limits on variation by name ("crookedPlates", "tiltDegrees"...).</summary>
    public required Dictionary<string, double> Limits { get; init; }

    /// <summary>
    /// What a run picks, slot by slot, while someone listens (<see cref="VehicleDiversity"/>'s trait spread sets it round one generation); null otherwise.
    /// Per thread, so a run on another thread is never mixed in. Colours are noted as "colour ROLE".
    /// </summary>
    [ThreadStatic] public static List<(string Slot, string Value)>? Trace;

    public string Colour(Dice d, string role) =>
        Palette.TryGetValue(role, out var c) ? Noted("colour " + role, d.Pick(c)) : throw new VehicleKitException($"style {Name} has no \"{role}\" colours");

    public string Part(Dice d, string slot) =>
        Parts.TryGetValue(slot, out var c) ? Noted(slot, d.Pick(c)) : throw new VehicleKitException($"style {Name} has no \"{slot}\" parts");

    /// <summary>Notes a pick in <see cref="Trace"/> when one is being kept.</summary>
    public static string Noted(string slot, string value)
    {
        Trace?.Add((slot, value));
        return value;
    }

    public double Limit(string name) =>
        Limits.TryGetValue(name, out var v) ? v : throw new VehicleKitException($"style {Name} has no limit \"{name}\"");

    public IReadOnlyList<string> Choices(string slot) =>
        Parts.TryGetValue(slot, out var c) ? c : throw new VehicleKitException($"style {Name} has no \"{slot}\" parts");
}
