using System.Globalization;

namespace CallOfSlop.VehicleKit;

/// <summary>The four layers of a kit-3 vehicle's variety (docs/vehiclekit/VEHICLEKIT.md, "Layered variety").</summary>
public enum VehicleLayer
{
    Chassis, // wheelbase, track, axles and their spacing, wheels, suspension, ride height
    Body,    // the outline: modules, section lengths and heights, rake, paint
    Kit,     // guns, stores, furniture, dressing, loot
    Wear,    // age and damage: rust, dirt, scorch, patches, missing plates
}

/// <summary>
/// A seed's layer seeds. Each layer rolls from its own root (<see cref="Dice.Routed"/>), so one layer can be kept while another rerolls: the same truck with different
/// wear is the same seed with <see cref="Wear"/> set, the same body on a different chassis the same seed with <see cref="Chassis"/> set. A layer left null takes the
/// vehicle's seed, so a seed with no overrides is one vehicle, as it always was. Kit 3 only: kit 1 and 2 have no layers.
/// </summary>
public readonly record struct VehicleLayers(int? Chassis = null, int? Body = null, int? Kit = null, int? Wear = null)
{
    /// <summary>The command line's <c>--layers chassis=5,wear=9</c>: overrides every vehicle made takes (null: none).</summary>
    public static VehicleLayers? Force;

    /// <summary>The command line's <c>--vary LAYER [--base S]</c>: the seed every tool loops over becomes that layer's seed on the base seed (default 1).</summary>
    public static (VehicleLayer Layer, int Base)? Vary;

    public bool Any => Chassis != null || Body != null || Kit != null || Wear != null;

    public int? this[VehicleLayer l] => l switch { VehicleLayer.Chassis => Chassis, VehicleLayer.Body => Body, VehicleLayer.Kit => Kit, _ => Wear };

    /// <summary>The seed a layer rolls from on a vehicle of <paramref name="seed"/>.</summary>
    public int SeedOf(VehicleLayer l, int seed) => this[l] ?? seed;

    public VehicleLayers With(VehicleLayer l, int? s) => l switch
    {
        VehicleLayer.Chassis => this with { Chassis = s },
        VehicleLayer.Body => this with { Body = s },
        VehicleLayer.Kit => this with { Kit = s },
        _ => this with { Wear = s },
    };

    /// <summary>Every layer's seed, as the kit line and the log give them: "chassis 12 body 7 kit 7 wear 99".</summary>
    public string Label(int seed)
    {
        var me = this;
        return string.Join(" ", All.Select(l => $"{Name(l)} {me.SeedOf(l, seed)}"));
    }

    /// <summary>What a blueprint's name adds for the overridden layers: " [chassis 12, wear 99]"; empty without any. The last word is then no seed, so the
    /// name never reads as a committed seed's.</summary>
    public string Suffix
    {
        get
        {
            var me = this;
            return Any ? " [" + string.Join(", ", All.Where(l => me[l] != null).Select(l => $"{Name(l)} {me[l]}")) + "]" : "";
        }
    }

    public static readonly VehicleLayer[] All = [VehicleLayer.Chassis, VehicleLayer.Body, VehicleLayer.Kit, VehicleLayer.Wear];

    public static string Name(VehicleLayer l) => l.ToString().ToLowerInvariant();

    public static bool TryLayer(string name, out VehicleLayer layer)
    {
        foreach (var l in All)
            if (Name(l).Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                layer = l;
                return true;
            }
        layer = default;
        return false;
    }

    /// <summary>"chassis=5,wear=9" (or "chassis 5, wear 9"): the overrides, or why not.</summary>
    public static bool TryParse(string text, out VehicleLayers layers, out string why)
    {
        layers = default;
        why = "";
        foreach (var one in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = one.Split(['=', ' ', ':'], StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length != 2 || !TryLayer(bits[0], out var l) || !int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) || s < 1)
            {
                why = $"\"{one}\" isn't layer=seed (chassis, body, kit or wear; a seed is 1 or more)";
                return false;
            }
            layers = layers.With(l, s);
        }
        return true;
    }

    /// <summary>
    /// Which layer a root fork of a vehicle's dice belongs to, by its topic: the names <see cref="Vehicle"/>'s own methods fork by ("deck bed", "weapon 2", "paint"...)
    /// and the grammar's ("chassis", "grammar kit"...). Anything not named here is the body's: the body is everything a vehicle looks like before its kit and its age.
    /// </summary>
    public static VehicleLayer LayerOf(string topic)
    {
        static bool Is(string topic, string name) => topic.StartsWith(name, StringComparison.Ordinal) && (topic.Length == name.Length || topic[name.Length] == ' ');
        if (Is(topic, "chassis") || Is(topic, "gear") || Is(topic, "axles") || Is(topic, "drive"))
            return VehicleLayer.Chassis;
        if (Is(topic, "wear") || Is(topic, "crooked"))
            return VehicleLayer.Wear;
        if (Is(topic, "grammar kit") || Is(topic, "weapon") || Is(topic, "rounds") || Is(topic, "ram") || Is(topic, "show") || Is(topic, "kit") || Is(topic, "piper")
            || Is(topic, "furnish") || Is(topic, "end chest") || Is(topic, "cargo") || Is(topic, "crew") || Is(topic, "panniers"))
            return VehicleLayer.Kit;
        return VehicleLayer.Body;
    }
}

/// <summary>
/// What a style's chassis may be, kit 3 (<see cref="VehicleChassis"/>). The corners themselves come from the style's own <see cref="FactionStyle.HeavyGear"/> recipes,
/// mixed front with rear and with wheels and springs swapped within their class; these are the ranges and the odds around them.
/// </summary>
public sealed record ChassisRange
{
    /// <summary>How far in from the body's end the rear axle stands, in quarter metres (kit 2: 1.25 to 2.25 in halves).</summary>
    public float InsetMin = 1.25f, InsetMax = 2.25f;

    /// <summary>Front axle stations and their weights: -1.675 under the hood's back (kit 1 and 2), -1.925 forward under the engine, -0.925 set back beside the cab.</summary>
    public (float Z, double W)[] FrontAxles = [(-1.675f, 1)];

    /// <summary>The front axle's wing (track): 0.5 m of beam out each side (kit 2) or 0.75.</summary>
    public (float Wing, double W)[] FrontWings = [(0.5f, 1)];

    /// <summary>
    /// Weights of the axle layouts: one rear axle, a free-rolling tag behind it, a free-rolling pusher ahead of it, a driven tandem (kit 2: 7, 1, 0, 1). The three
    /// second axles weigh more under a long body and less under a short one (<see cref="VehicleChassis"/>: from 0.3 times at 4 m of body to twice at 9 m).
    /// </summary>
    public double Single = 7, Tag = 1, Pusher, Tandem = 1;

    /// <summary>A second axle's spacing from the driven one (kit 2: 1.25).</summary>
    public float[] Spacings = [1.25f];

    /// <summary>The chance the front and the rear corners come from different recipes of the style (kit 2: never).</summary>
    public double Mix;

    /// <summary>The chance a corner's wheel or spring is swapped for another of its class (kit 2: never).</summary>
    public double WheelSwap, SpringSwap;

    /// <summary>The chance a tag or pusher axle is a dead axle on Rigid Mounts.</summary>
    public double Rigid;
}

/// <summary>
/// What a style's bodies may be, kit 3: the grammar's recipes give each shape its segments and their length ranges; these skew and shape them per style. The defaults are
/// kit 2's own rolls (lengths uniform over the recipe's range, bay heights tall 3 : low 2 : narrow 1, a height change at 45 % of the bounds, no extra rake).
/// </summary>
public sealed record BodyRange
{
    /// <summary>-1 to 1: how a segment's length leans in its recipe's range (0 uniform; 1 strongly long; -1 strongly short).</summary>
    public double LengthBias;

    /// <summary>Weights of a room's section heights: tall, low and narrow.</summary>
    public double Tall = 3, Low = 2, Narrow = 1;

    /// <summary>The chance the roofline changes at each bound between sections.</summary>
    public double StepChance = 0.45;

    /// <summary>The chance a room's brow, and again its tail, is forced low or narrow: a raked end.</summary>
    public double Rake;
}

/// <summary>How worn a style's vehicles are, kit 3 (<see cref="VehicleWear"/>). Each seed's wear also rolls an age that scales these.</summary>
public sealed record WearProfile
{
    /// <summary>The share of painted panels gone over to the style's rust.</summary>
    public double Rust = 0.15;

    /// <summary>The share of low panels (the lower 1.2 m) dark with road dirt (the style's frame colour).</summary>
    public double Dirt = 0.3;

    /// <summary>The chance each gun has scorched the panels near it, and the hood near the exhaust.</summary>
    public double Scorch = 0.3;

    /// <summary>Extra patch repairs welded over panels, on top of the style's crooked plates.</summary>
    public int PatchesMin, PatchesMax = 2;

    /// <summary>The share of an open deck's edge cells (beds, porches, a plated hood, a rack) rusted out: no plate there.</summary>
    public double Missing = 0.05;

    /// <summary>The age a seed rolls, as a factor on everything above.</summary>
    public double AgeMin = 0.4, AgeMax = 1.2;
}

/// <summary>
/// Kit 3's tables by style name (docs/vehiclekit/VEHICLEKIT.md, "Layered variety"). Kept here rather than in <c>Styles.cs</c>, so a style written there needs none of
/// this: a style not named gets the defaults, which vary everything moderately.
/// </summary>
public static class LayerTables
{
    private static readonly ChassisRange DefaultChassis = new()
    {
        InsetMin = 1.25f, InsetMax = 2.25f, FrontAxles = [(-1.675f, 3), (-1.925f, 1)], FrontWings = [(0.5f, 3), (0.75f, 1)],
        Single = 5, Tag = 1.2, Pusher = 0.5, Tandem = 1, Spacings = [1.25f, 1.5f], Mix = 0.5, WheelSwap = 0.25, SpringSwap = 0.25, Rigid = 0.25,
    };

    public static readonly Dictionary<string, ChassisRange> Chassis = new(StringComparer.Ordinal)
    {
        // Scrap built: anything under anything, long and short, the odd dead axle.
        ["Bandit"] = DefaultChassis with { InsetMin = 1f, InsetMax = 2.5f, FrontAxles = [(-1.675f, 3), (-1.925f, 1), (-0.925f, 1)], FrontWings = [(0.5f, 2), (0.75f, 1)],
            Single = 4, Tag = 1.5, Pusher = 0.8, Tandem = 1.2, Mix = 0.75, WheelSwap = 0.45, SpringSwap = 0.35, Rigid = 0.4 },
        // Fleet vans: matched corners, a forward cab now and then, an extra axle only on the long ones.
        ["IT"] = DefaultChassis with { InsetMin = 1f, InsetMax = 2.25f, FrontAxles = [(-1.675f, 4), (-1.925f, 1), (-0.925f, 1.5)], FrontWings = [(0.5f, 3), (0.75f, 1)],
            Single = 5, Tag = 1.5, Pusher = 0.4, Tandem = 0.8, Mix = 0.4, WheelSwap = 0.1, SpringSwap = 0.25, Rigid = 0 },
        ["Scam"] = DefaultChassis with { InsetMin = 1f, InsetMax = 2.25f, FrontAxles = [(-1.675f, 4), (-1.925f, 1), (-0.925f, 1)], FrontWings = [(0.5f, 3), (0.75f, 1)],
            Single = 5, Tag = 1, Pusher = 0.4, Tandem = 0.8, Mix = 0.4, WheelSwap = 0.15, SpringSwap = 0.2, Rigid = 0.1 },
        // Farm and hill: wide fronts, short wheelbases, tandems for the mud.
        ["Highland"] = DefaultChassis with { InsetMin = 1f, InsetMax = 2.25f, FrontAxles = [(-1.675f, 3), (-1.925f, 1)], FrontWings = [(0.5f, 2), (0.75f, 1)],
            Single = 4, Tag = 1, Pusher = 0.4, Tandem = 1.5, Mix = 0.5, WheelSwap = 0.25, SpringSwap = 0.3, Rigid = 0.2 },
        // Buses: long overhangs, axles set back under the driver, tag axles.
        ["British"] = DefaultChassis with { InsetMin = 1.25f, InsetMax = 2.5f, FrontAxles = [(-1.675f, 3), (-0.925f, 2)], FrontWings = [(0.5f, 3), (0.75f, 1)],
            Single = 4, Tag = 1.5, Pusher = 0.3, Tandem = 0.6, Mix = 0.35, WheelSwap = 0.1, SpringSwap = 0.25, Rigid = 0.1 },
    };

    public static readonly Dictionary<string, BodyRange> Body = new(StringComparer.Ordinal)
    {
        ["Bandit"] = new() { LengthBias = 0, Tall = 3, Low = 2, Narrow = 2, StepChance = 0.55, Rake = 0.3 },
        ["IT"] = new() { LengthBias = 0.15, Tall = 3, Low = 2, Narrow = 0.5, StepChance = 0.4, Rake = 0.25 },      // (tidy: low, not narrow; raked like a coach)
        ["Scam"] = new() { LengthBias = 0.1, Tall = 3, Low = 2, Narrow = 1, StepChance = 0.4, Rake = 0.2 },
        ["Highland"] = new() { LengthBias = 0.1, Tall = 3, Low = 2, Narrow = 1, StepChance = 0.45, Rake = 0.2 },
        ["British"] = new() { LengthBias = 0.2, Tall = 3.5, Low = 1.5, Narrow = 1, StepChance = 0.45, Rake = 0.25 },
    };

    public static readonly Dictionary<string, WearProfile> Wear = new(StringComparer.Ordinal)
    {
        ["Bandit"] = new() { Rust = 0.45, Dirt = 0.35, Scorch = 0.6, PatchesMin = 2, PatchesMax = 5, Missing = 0.12, AgeMin = 0.6, AgeMax = 1.3 },
        ["IT"] = new() { Rust = 0.03, Dirt = 0.1, Scorch = 0.1, PatchesMin = 0, PatchesMax = 1, Missing = 0, AgeMin = 0.2, AgeMax = 0.8 },
        ["Scam"] = new() { Rust = 0.08, Dirt = 0.2, Scorch = 0.15, PatchesMin = 0, PatchesMax = 1, Missing = 0.02, AgeMin = 0.3, AgeMax = 1 },
        ["Highland"] = new() { Rust = 0.12, Dirt = 0.35, Scorch = 0.1, PatchesMin = 1, PatchesMax = 3, Missing = 0.03, AgeMin = 0.5, AgeMax = 1.1 }, // (well loved: mud and mended, not neglected)
        ["British"] = new() { Rust = 0.1, Dirt = 0.2, Scorch = 0.05, PatchesMin = 0, PatchesMax = 2, Missing = 0.02, AgeMin = 0.3, AgeMax = 1 },
    };

    public static ChassisRange ChassisOf(FactionStyle st) => Chassis.GetValueOrDefault(st.Name) ?? DefaultChassis;

    public static BodyRange BodyOf(FactionStyle st) => Body.GetValueOrDefault(st.Name) ?? new BodyRange();

    public static WearProfile WearOf(FactionStyle st) => Wear.GetValueOrDefault(st.Name) ?? new WearProfile();
}
