namespace CallOfSlop.VehicleKit;

/// <summary>
/// Seeded choices. The same seed gives the same choices on every machine: no <c>Random</c>, no <c>HashCode</c>, no maths that
/// varies with the platform. Each feature takes its own <see cref="Fork"/> by name, so a new roll in the paint doesn't change the chassis.
/// </summary>
public sealed class Dice(uint seed)
{
    private uint _s = seed * 2654435761u + 12345u;

    public static Dice Of(int seed) => new((uint)seed);

    /// <summary>
    /// Kit 3's layers (<see cref="VehicleLayers"/>): a topic the route names is forked from that layer's own root instead, so a vehicle's chassis, body, kit and
    /// wear can each take their own seed. A layer root with this one's seed forks exactly as this would. Null: every topic forks from here (kit 1 and 2).
    /// </summary>
    private Func<string, Dice?>? _route;

    /// <summary>The same dice with a <see cref="_route"/> for its forks.</summary>
    public Dice Routed(Func<string, Dice?> route) => new(seed) { _route = route };

    /// <summary>A separate stream for a topic ("paint", "panels"), fixed by the seed and the topic's name.</summary>
    public Dice Fork(string topic)
    {
        if (_route?.Invoke(topic) is { } layer && layer != this)
            return layer.Fork(topic);
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
    /// <summary>The receivers merged into one (docs/sandbox/CONSTRUCTS.md, "Merged receivers"): 1 a single receiver, 2 a 2x2 square whose gun parts are twice the size, 3 a 3x3.</summary>
    public int Merge { get; init; } = 1;

    /// <summary>How it is mounted (<see cref="VehicleArmoury.Mounts"/>): "pintle" (open, the default), "shield" (a gun shield in front of the gunner), "twin" (two guns on one cradle).</summary>
    public string Mount { get; init; } = "pintle";

    /// <summary>The armoury's recipe it came from ("" for a style's own gun), its class when the recipe states one, its least tier and its job tags (aa, anti-armour, close...).</summary>
    public string Recipe { get; init; } = "";

    public GunClass? Class { get; init; }

    public int Tier { get; init; } = 1;

    public string[] Tags { get; init; } = [];

    /// <summary>A missile battery's (the gunsmith's, docs/sandbox/VEHICLE-WEAPONS.md): the Ordnance Rack fitted on the turret beside the gun, and the designed missile's key on each of its stations.
    /// The gun spots and aims; the rack lets go on the Turret Module's fire. Null on every other gun.</summary>
    public string? Rack { get; init; }

    public string? Ordnance { get; init; }

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

    /// <summary>
    /// The guns a seed may mount: the style's own list (<see cref="OwnWeapons"/>) and the vehicle armoury's recipes for this faction, each as many times as its weight
    /// (Constructs/VehicleArmoury.json, <see cref="VehicleArmoury"/>; <c>--no-armoury</c>: the own list only, as before the armoury).
    /// </summary>
    public required WeaponOption[] Weapons
    {
        get => VehicleArmoury.On ? _armed ??= VehicleArmoury.Arm(this, _weapons) : _weapons;
        init => _weapons = value;
    }

    private readonly WeaponOption[] _weapons = [];
    private WeaponOption[]? _armed; // (made once; the same on every thread, so a race only makes it twice)

    /// <summary>The style's own guns, without the armoury's.</summary>
    public WeaponOption[] OwnWeapons => _weapons;

    /// <summary>The loot table its containers roll from (Constructs/Loot.json).</summary>
    public string LootTable { get; init; } = "";

    /// <summary>
    /// All-wheel drive by style (drivetrain-and-engines.md "All-wheel drive and round wheels"): 0, every shipped style, leaves it to the vehicle and the Engine Kit;
    /// 1 drives every axle on every ground vehicle the style makes; N (2 to 5) only when an engine wish of tech tier N or more is current. Off for every shipped style.
    /// </summary>
    public int AllWheelDriveTier { get; init; }

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

    /// <summary>
    /// What every vehicle of the style must read as (<see cref="VehicleIdentity"/>, docs/vehiclekit/VEHICLEKIT.md "Identity and weak points"): its marks, characteristic parts and modules,
    /// silhouette range and designed weak points. Null: <see cref="VehicleIdentity.Signatures"/>' entry under the style's name, else one derived from the style's own slots.
    /// </summary>
    public StyleSignature? Signature { get; init; }

    /// <summary>Limits on variation by name ("crookedPlates", "tiltDegrees"...).</summary>
    public required Dictionary<string, double> Limits { get; init; }

    /// <summary>
    /// What a run picks, slot by slot, while someone listens (<see cref="VehicleDiversity"/>'s trait spread sets it round one generation); null otherwise.
    /// Per thread, so a run on another thread is never mixed in. Colours are noted as "colour ROLE".
    /// </summary>
    [ThreadStatic] public static List<(string Slot, string Value)>? Trace;

    public string Colour(Dice d, string role) =>
        Palette.TryGetValue(role, out var c)
            ? Noted("colour " + role, d.Pick(VehicleContext.Colours(this, role, Scheme != null && Scheme.TryGetValue(role, out var own) && own.Length > 0 ? own : c))) // (a convoy's scheme: the same one roll, over its share of the palette; a spawn context's: the share that suits the ground, VehicleContext.cs)
            : throw new VehicleKitException($"style {Name} has no \"{role}\" colours");

    /// <summary>
    /// A convoy's shared paint scheme (VehicleConvoys.cs, docs/vehiclekit/VEHICLEKIT.md "Families and convoys"): while one of its vehicles is made on this thread,
    /// the colours each palette role may take, a share of the style's own. Per thread, as <see cref="Weigh"/>; null whenever no convoy is being made, so every
    /// other vehicle paints exactly as before.
    /// </summary>
    [ThreadStatic] public static Dictionary<string, string[]>? Scheme;

    public string Part(Dice d, string slot)
    {
        if (!Parts.TryGetValue(slot, out var c))
            throw new VehicleKitException($"style {Name} has no \"{slot}\" parts");
        var w = Weigh?.Invoke(this, slot, c);
        if (SpawnContext.Current is { Matters: true } ctx && VehicleContext.Weigh(ctx, this, slot, w?.Items ?? c, w?.Weights) is { } cw)
            w = cw; // (a spawn context's lean, on top of a role's: VehicleContext.cs; a neutral context leaves the pick as it was)
        if (w is { } ww && ww.Items.Length > 0) // (kit 4's role and tier: the same one roll, over weighted choices)
        {
            var roll = d.Next() * ww.Weights.Sum();
            for (var i = 0; i < ww.Items.Length; i++)
                if ((roll -= ww.Weights[i]) < 0 || i == ww.Items.Length - 1)
                    return Noted(slot, ww.Items[i]);
        }
        return Noted(slot, d.Pick(c));
    }

    /// <summary>
    /// Kit 4 (VehicleRoles.cs): while a role's vehicle is made on this thread, the choices of a slot and their weights instead of the slot's list as it is (null: as
    /// it is). A role that wants armour weighs its plates by their armour, a tier weighs parts by the tier of their recipe. Per thread, as <see cref="Trace"/>; null
    /// whenever no role is being made, so every other vehicle picks exactly as before.
    /// </summary>
    [ThreadStatic] public static Func<FactionStyle, string, string[], (string[] Items, double[] Weights)?>? Weigh;

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
