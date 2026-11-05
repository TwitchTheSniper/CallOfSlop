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
/// One gun a faction may mount, as data: what mounts on the hinge, by which of its sockets, what feeds it. The kit builds any
/// option the same way, so a new weapon is a new line in a style's <see cref="FactionStyle.Weapons"/>. <paramref name="Feed"/> null: the gun feeds itself.
/// </summary>
public sealed record WeaponOption(string Gun, string? Feed = "Ammo Box", string Mount = "bottom", string FeedAt = "left", string FeedBy = "right");

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

    /// <summary>The guns a seed may mount.</summary>
    public required WeaponOption[] Weapons { get; init; }

    /// <summary>Limits on variation by name ("crookedPlates", "tiltDegrees"...).</summary>
    public required Dictionary<string, double> Limits { get; init; }

    public string Colour(Dice d, string role) =>
        Palette.TryGetValue(role, out var c) ? d.Pick(c) : throw new VehicleKitException($"style {Name} has no \"{role}\" colours");

    public string Part(Dice d, string slot) =>
        Parts.TryGetValue(slot, out var c) ? d.Pick(c) : throw new VehicleKitException($"style {Name} has no \"{slot}\" parts");

    public double Limit(string name) =>
        Limits.TryGetValue(name, out var v) ? v : throw new VehicleKitException($"style {Name} has no limit \"{name}\"");

    public IReadOnlyList<string> Choices(string slot) =>
        Parts.TryGetValue(slot, out var c) ? c : throw new VehicleKitException($"style {Name} has no \"{slot}\" parts");
}
