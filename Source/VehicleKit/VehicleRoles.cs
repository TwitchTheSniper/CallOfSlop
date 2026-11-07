using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>How heavy a gun is, from its receiver's bore and action (<see cref="VehicleRoles.ClassOf"/>): a carbine or a pump gun is light, a machine gun medium, a harpoon or a flame gun heavy.</summary>
public enum GunClass
{
    None,
    Light,
    Medium,
    Heavy,
}

/// <summary>
/// What a role asks of a built vehicle, in what can be measured (docs/vehiclekit/VEHICLEKIT.md, "Roles and tiers"): crew seats, a top speed band, an armour rating,
/// how many guns and how heavy, loot, length, fuel carried, and the equipment the role is for. <see cref="VehicleRole.Needs"/> holds tier 2's; <see cref="RoleAsk"/> moves them by tier.
/// </summary>
public sealed record RoleNeeds
{
    public int SeatsMin = 1, SeatsMax = 99;
    public int GunsMin, GunsMax = 99;
    public GunClass ClassMin = GunClass.None, ClassMax = GunClass.Heavy;

    /// <summary>The cheap top speed estimate's band, m/s (<see cref="RoleFit.Speed"/>).</summary>
    public float SpeedMin, SpeedMax = 999;

    /// <summary>The least armour rating: the mean armour of the body's panels by area (<see cref="RoleFit.Armour"/>): sheet steel 6, hull bays 8, tread plate 10, plate steel 20, armour plate 40.</summary>
    public float ArmourMin;

    /// <summary>Loot containers plus loose cargo pieces aboard.</summary>
    public int LootMin, LootMax = 99;

    public float LengthMin, LengthMax = 99;

    /// <summary>Litres of fuel carried in all its tanks.</summary>
    public float FuelMin;

    /// <summary>A walk-in room; an open deck (a bed or a porch); a tow hitch; a winch; the fuel a weak point in the open; this many signals (the style's masts, else its trophies).</summary>
    public bool Room, Deck, Tow, Winch, FuelOpen;

    public int SignalsMin;
}

/// <summary>
/// A role: what a vehicle is for (docs/vehiclekit/VEHICLEKIT.md, "Roles and tiers"). The style decides how it looks; the role decides what it must do, as needs the built
/// vehicle is measured against (<see cref="Needs"/>, tier 2's), the shapes that can meet them (<see cref="Shapes"/>), and how it leans the grammar's choices: module
/// weights by family (<see cref="Modules"/>; body segments under "body"), segment lengths, the chassis (fast: one axle; slow: more), plates' armour, guns and loot.
/// </summary>
public sealed record VehicleRole(string Name, string Purpose)
{
    /// <summary>Shapes that can meet it, with weights; a style's own shapes only (its extras included).</summary>
    public (string Shape, double W)[] Shapes { get; init; } = [];

    public RoleNeeds Needs { get; init; } = new();

    /// <summary>Factors on the grammar's module weights, by family and module (body: by segment kind; a pattern weighs the product of its segments').</summary>
    public Dictionary<string, Dictionary<string, double>> Modules { get; init; } = [];

    /// <summary>-1 to 1: how segment lengths lean (added to the style's).</summary>
    public double LengthBias { get; init; }

    /// <summary>The chance an open bed carries a gun (kit 3: a half).</summary>
    public double BedGun { get; init; } = 0.5;

    /// <summary>0 to 1: how much its plates lean to the heavier materials (and the armoured cab, walled sides).</summary>
    public double Armour { get; init; }

    /// <summary>-1 slow and heavy (more axles) to 1 fast and light (one axle).</summary>
    public double Fast { get; init; }

    /// <summary>Loot tier at tier 1 (a tier adds one, to 4): 1 and 2 the style's own table, 3 and 4 its richer ones ("bandit 3").</summary>
    public int LootBase { get; init; } = 1;

    /// <summary>Extra chests it tries to stand in its room.</summary>
    public int ExtraChests { get; init; }

    /// <summary>A fighting role: its guns grow with the tier.</summary>
    public bool Combat { get; init; }
}

/// <summary>A role at a tier for a style: the needs as that tier moves them, and the leanings the grammar reads (<see cref="ShapeGrammar"/>, <see cref="FactionStyle.Weigh"/>).</summary>
public sealed class RoleAsk
{
    public readonly VehicleRole Role;
    public readonly int Tier;
    public readonly FactionStyle Style;
    public readonly RoleNeeds Needs;

    /// <summary>The shape it was asked as (set by whoever picked it: <see cref="VehicleRoles.ShapeFor"/>), for the kit line.</summary>
    public string Shape = "";

    public RoleAsk(VehicleRole role, int tier, FactionStyle style)
    {
        Role = role;
        Tier = Math.Clamp(tier, 1, VehicleRoles.MaxTier);
        Style = style;
        Needs = NeedsAt(role, Tier, style);
        FrameAt = Tier;
    }

    /// <summary>
    /// A family's mark (VehicleFamilies.cs, docs/vehiclekit/VEHICLEKIT.md "Families and convoys"): asked at <paramref name="tier"/>, its frame (body and chassis
    /// leans: lengths, the axles, cabs, bed sides, plate materials, running gear) at <paramref name="frameTier"/>, the family's base tier, so Mk1, Mk2 and Mk3 share
    /// one body on one chassis; the kit, appliqué and loot follow the mark's tier and the model's variant.
    /// </summary>
    public RoleAsk(VehicleRole role, int tier, FactionStyle style, VehicleModel model, int frameTier) : this(role, tier, style)
    {
        Model = model;
        FrameAt = Math.Clamp(frameTier, 1, VehicleRoles.MaxTier);
        if (model.Variant == VehicleFamilies.GunTruck)
            Needs.GunsMax += 1; // (a gun truck carries one more than its role's mark would)
    }

    /// <summary>The tier the frame leans by: <see cref="Tier"/>, except for a family's later marks (the family's base tier).</summary>
    public readonly int FrameAt;

    /// <summary>The family model this vehicle is a mark of (VehicleFamilies.cs), or null for every other role vehicle.</summary>
    public readonly VehicleModel? Model;

    /// <summary>A convoy's shared paint (<see cref="FactionStyle.Scheme"/>, VehicleConvoys.cs): set while it is made; null for every other vehicle.</summary>
    public Dictionary<string, string[]>? Scheme { get; init; }

    /// <summary>A family member: made from its first plan that builds, never planned again for its role, so every mark keeps one body (a miss is reported).</summary>
    public bool Family => Model != null;

    /// <summary>The model's variant ("" for none).</summary>
    public string Variant => Model?.Variant ?? "";

    /// <summary>A rare named unique (<see cref="VehicleModel.IsNamed"/>): the best loot, an extra chest, one more mark.</summary>
    public bool Named => Model?.IsNamed == true;

    public string Label => $"{Role.Name}, tier {Tier}";

    /// <summary>What a blueprint's name adds: " [raider, tier 2]", so it never reads as a committed seed's.</summary>
    public string Suffix => $" [{Label}]";

    /// <summary>The loot table tier: the role's base plus one a tier, 1 to 4; a named unique's is the best.</summary>
    public int LootTier => Named ? 4 : Math.Clamp(Role.LootBase + Tier - 1 + (Variant == VehicleFamilies.TankerConversion ? 1 : 0), 1, 4);

    /// <summary>How far the plates (and cabs, bed sides) lean to armour, with the frame's tier (<see cref="FrameAt"/>: the tier, but for a family's later marks).</summary>
    public double ArmourLean => Math.Clamp(Role.Armour + 0.15 * (FrameAt - 2), 0, 1);

    /// <summary>
    /// How far the kit leans to armour (appliqué, loaded flanks): <see cref="ArmourLean"/> for every vehicle but a family's, whose Mk2 and Mk3 take appliqué at
    /// the mark's tier (at least 0.3 and 0.5, so a raider's Mk2 is the same truck plated) and whose up-armoured variant more.
    /// </summary>
    public double KitArmourLean
    {
        get
        {
            if (!Family)
                return ArmourLean;
            var a = Math.Clamp(Role.Armour + 0.15 * (Tier - 2), 0, 1);
            var mark = Model!.Mark;
            if (mark >= 2)
                a = Math.Max(a, 0.3 + 0.2 * (mark - 2));
            if (Variant == VehicleFamilies.UpArmoured)
                a = Math.Max(a, 0.5) + 0.3;
            return Math.Clamp(a, 0, 1);
        }
    }

    /// <summary>The chance an open bed carries a gun: the role's, every bed for a gun truck.</summary>
    public double BedGun => Variant == VehicleFamilies.GunTruck ? 1 : Role.BedGun;

    private static RoleNeeds NeedsAt(VehicleRole r, int tier, FactionStyle st)
    {
        var n = r.Needs with { };
        var best = st.Weapons.Select(VehicleRoles.ClassOf).DefaultIfEmpty(GunClass.Light).Max();
        if (r.Combat)
        {
            if (tier >= 3)
                n.GunsMax += 1;
            if (tier >= 3 && n.ClassMin < GunClass.Medium)
                n.ClassMin = GunClass.Medium;
        }
        if (tier <= 1 && n.ClassMax > GunClass.Medium)
            n.ClassMax = GunClass.Medium; // (a tier 1 crew has what anyone has: no harpoon, no flame gun)
        if (tier >= 4 && r.Combat && r.Armour >= 0.5)
            n.ClassMin = GunClass.Heavy;
        if (n.ClassMin > best)
            n.ClassMin = best; // (a style with no heavy gun fields its heaviest)
        if (n.ArmourMin > 0)
            n.ArmourMin = MathF.Max(0, n.ArmourMin + 0.5f * (tier - 2)); // (measured: a tier 4 carrier's appliqué and plates reach 12 to 14)
        return n;
    }

    /// <summary>The factor on a module's weight (1: no lean). Armour leans cabs, flanks and bed sides; the role's own table the rest.</summary>
    public double Module(string family, string name)
    {
        var f = Role.Modules.TryGetValue(family, out var m) && m.TryGetValue(name, out var w) ? w : 1;
        var a = family == "flank" ? KitArmourLean : ArmourLean; // (the flanks are the kit's: a family's later marks load them; the rest is the frame's)
        if (a > 0)
            f *= (family, name) switch
            {
                ("cab", "armoured") => 1 + 4 * a,
                ("cab", "enclosed") => 1 + a,
                ("cab", "caged") => 1 - 0.7 * a,
                ("sides", "walls") => 1 + 3 * a,
                ("sides", "open") => 1 - 0.8 * a,
                ("flank", "loaded") => 1 + 2 * a,
                ("flank", "bare") => 1 - 0.5 * a,
                _ => 1,
            };
        return f;
    }

    /// <summary>A body pattern's factor: the product of its segments' (<see cref="Module"/> under "body").</summary>
    public double Body(BodyPattern b) => b.Segments.Aggregate(1.0, (w, s) => w * Module("body", s.Kind));

    /// <summary>The length lean: the style's plus the role's plus a little a tier.</summary>
    public double LengthBias(double style) => Math.Clamp(style + Role.LengthBias + 0.15 * (FrameAt - 2), -1, 1);

    /// <summary>The gun count to roll from, within the recipe's.</summary>
    public (int Lo, int Hi) GunRange(int min, int max)
    {
        var lo = Math.Clamp(Needs.GunsMin + (Role.Combat && Tier >= 4 ? 1 : 0) + (Variant == VehicleFamilies.GunTruck ? 1 : 0), min, max); // (a tier 4 fighter rolls one more where its shape has room: a lean, not a need, as a Technical carries one)
        return (lo, Math.Clamp(Needs.GunsMax, lo, max));
    }

    /// <summary>A gun's weight for this role and tier: its class in the needs' range, heavier for a fighting role at a high tier, rarer guns at higher tiers.</summary>
    public double WeaponWeight(WeaponOption w)
    {
        var c = VehicleRoles.ClassOf(w);
        var f = c < Needs.ClassMin || c > Needs.ClassMax ? 0.1 : 1;
        if (Role.Combat && c == Needs.ClassMax)
            f *= 1 + 0.5 * Math.Max(0, Tier - 1);
        var rare = VehicleRoles.RareGun(w);
        return f * (rare ? (Tier >= 3 ? 1.5 : Tier <= 1 ? 0.4 : 1) : 1);
    }

    /// <summary>One of <paramref name="list"/>, weighted by <see cref="WeaponWeight"/>: one roll, as <see cref="Dice.Pick"/> takes.</summary>
    public WeaponOption Pick(Dice d, IReadOnlyList<WeaponOption> list)
    {
        var w = list.Select(WeaponWeight).ToArray();
        var roll = d.Next() * w.Sum();
        for (var i = 0; i < list.Count; i++)
            if ((roll -= w[i]) < 0)
                return list[i];
        return list[^1];
    }

    /// <summary>The list in a seeded order, heavier weights first more often (a weighted draw without replacement).</summary>
    public List<WeaponOption> Order(Dice d, IReadOnlyList<WeaponOption> list)
    {
        var left = list.ToList();
        var order = new List<WeaponOption>();
        while (left.Count > 0)
        {
            var pick = Pick(d, left);
            order.Add(pick);
            left.Remove(pick);
        }
        return order;
    }

    /// <summary>The style's chassis table leaned by how fast the role is: one axle for a fast one, a second for a slow, heavy one.</summary>
    public ChassisRange Chassis(ChassisRange r)
    {
        var f = Role.Fast;
        var heavier = Math.Max(0, -f) + Math.Max(0, (FrameAt - 2) * 0.2 * (Role.Armour > 0.4 ? 1 : 0));
        return r with
        {
            Single = r.Single * (1 + 2 * Math.Max(0, f)),
            Tag = r.Tag * (1 - 0.7 * Math.Max(0, f)) * (1 + heavier),
            Pusher = r.Pusher * (1 - 0.7 * Math.Max(0, f)) * (1 + heavier),
            Tandem = r.Tandem * (1 - 0.8 * Math.Max(0, f)) * (1 + 2 * heavier),
        };
    }

    // ---- The style's parts by tier and armour (FactionStyle.Weigh) ------------------------------------------------------------

    private static readonly HashSet<string> Plates = new(StringComparer.Ordinal) { "sheet", "wall", "deck", "bed", "armour" };

    private static readonly HashSet<string> Graded = new(StringComparer.Ordinal)
    {
        "engine", "gearbox", "wheel", "spring", "differential", "heavyEngine", "heavyGearbox", "heavyWheel", "heavySpring", "heavyDifferential", "cab",
    };

    /// <summary>
    /// <see cref="FactionStyle.Weigh"/> while this role's vehicle is made: the style's plate materials weigh more the more armour they have, and running gear and the light cab weigh by the tier of their recipe against this tier. Only the style's own
    /// parts: the identity stays the style's.
    /// </summary>
    public (string[] Items, double[] Weights)? Weigh(FactionStyle st, string slot, string[] list)
    {
        if (Plates.Contains(slot))
        {
            var a = ArmourLean;
            if (a <= 0)
                return null;
            // (the style's own materials only: an Armour Plate deck or wall stands thicker than any a style lists, and refused a Flatbed Fort's bunker brow beside it.
            // The armour plate goes on as appliqué instead: Vehicle.UpArmour)
            return (list, list.Select(m => Math.Pow(Math.Max(4, VehicleRoles.PlateArmour(m)) / 20.0, 1.5 * a)).ToArray());
        }
        if (!Graded.Contains(slot) || list.Distinct().Count() < 2)
            return null;
        var tiers = list.Select(VehicleRoles.PartTier).ToArray();
        var (lo, hi) = (tiers.Min(), tiers.Max());
        if (lo == hi)
            return null;
        var target = lo + (hi - lo) * (FrameAt - 1) / 3.0; // (running gear is the frame's: a family's marks keep their base's)
        return (list, tiers.Select(t => Math.Pow(2.0, -Math.Abs(t - target))).ToArray());
    }
}

/// <summary>What a built vehicle measured as, against a role's needs (<see cref="VehicleRoles.Measure"/>): every number, and each need it missed, in words.</summary>
public sealed record RoleFit(int Seats, int Guns, GunClass Best, float Speed, float Armour, int Loot, int LootTier, float Length, float Fuel, bool Room, bool Deck, bool Tow,
    int Winches, int Signals, float Mass, IReadOnlyList<string> Misses)
{
    public bool Met => Misses.Count == 0;

    public override string ToString() =>
        $"seats {Seats}, guns {Guns} ({Best.ToString().ToLowerInvariant()}), est. top speed {Speed:0} m/s, {Mass / 1000:0.0} t, armour {Armour:0.0}, loot {Loot} (tier {LootTier}), {Length:0.0} m, fuel {Fuel:0} l"
        + (Tow ? ", tow hitch" : "") + (Winches > 0 ? $", {Winches} winch" : "") + (Signals > 0 ? $", {Signals} signal{(Signals == 1 ? "" : "s")}" : "") + (Room ? ", room" : "") + (Deck ? ", deck" : "");
}

/// <summary>
/// Roles and tiers (kit 4; docs/vehiclekit/VEHICLEKIT.md, "Roles and tiers"): the roles, the tiers' effect, which roles each style fields and how often, the shape a
/// role picks, the measure a built vehicle is held to, and the role's equipment pass. The threat ladder: a tier is how hard a place is within a faction (a Bandit tier 4
/// is a warlord's truck, still a Bandit's), so it scales part quality, armour, guns, size and loot and nudges weights; identity holds at every tier.
/// </summary>
public static class VehicleRoles
{
    public const int MaxTier = 4;

    private static RoleNeeds N(int seatsMin = 1, int seatsMax = 99, int gunsMin = 0, int gunsMax = 99, GunClass classMin = GunClass.None, GunClass classMax = GunClass.Heavy,
        float speedMin = 0, float speedMax = 999, float armour = 0, int lootMin = 0, int lootMax = 99, float lengthMin = 0, float lengthMax = 99, float fuel = 0,
        bool room = false, bool deck = false, bool tow = false, bool winch = false, bool fuelOpen = false, int signals = 0) => new()
    {
        SeatsMin = seatsMin, SeatsMax = seatsMax, GunsMin = gunsMin, GunsMax = gunsMax, ClassMin = classMin, ClassMax = classMax, SpeedMin = speedMin, SpeedMax = speedMax,
        ArmourMin = armour, LootMin = lootMin, LootMax = lootMax, LengthMin = lengthMin, LengthMax = lengthMax, FuelMin = fuel, Room = room, Deck = deck, Tow = tow, Winch = winch,
        FuelOpen = fuelOpen, SignalsMin = signals,
    };

    private static Dictionary<string, Dictionary<string, double>> M(params (string Family, string Module, double W)[] w) =>
        w.GroupBy(x => x.Family).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Module, x => x.W));

    /// <summary>Every role, by name. Numbers are tier 2's; the speed is the cheap estimate's (<see cref="Speed"/>), not a drive's.</summary>
    public static readonly VehicleRole[] All =
    [
        new("scout", "fast, light, one or two crew, one light gun, little loot")
        {
            Shapes = [("Technical", 1)], // (a Box Truck measured 20 to 25 m/s against a Technical's 25 to 34: no grammar shape is a scout)
            Needs = N(seatsMax: 3, gunsMin: 1, gunsMax: 1, classMax: GunClass.Medium, speedMin: 27, lootMax: 3, lengthMax: 7),
            Modules = M(("body", "room", 0.5), ("body", "gap", 1.5), ("rear", "gun deck", 0.3), ("flank", "loaded", 0.3)),
            LengthBias = -0.7, BedGun = 0.2, Fast = 1, LootBase = 1,
        },
        new("raider", "fast to middling, two to four crew, guns, some loot")
        {
            Shapes = [("Technical", 3), ("Box Truck", 2), ("Gun Wagon", 2)],
            Needs = N(seatsMin: 2, seatsMax: 4, gunsMin: 1, gunsMax: 3, speedMin: 22, lootMin: 1, lengthMax: 9.5f),
            Modules = M(("body", "gun bed", 1.5), ("body", "bed", 1.2), ("body", "gap", 1.2), ("nose", "ram", 2)),
            LengthBias = -0.3, BedGun = 0.7, Fast = 0.6, Combat = true, LootBase = 1,
        },
        new("gun platform", "slow, heavy guns on an open deck")
        {
            Shapes = [("Flatbed Fort", 3), ("Gun Wagon", 3)],
            Needs = N(gunsMin: 2, classMin: GunClass.Medium, deck: true),
            Modules = M(("body", "gun bed", 3), ("body", "hutch", 2), ("body", "gun bay", 2), ("body", "room", 0.6), ("rear", "gun deck", 3), ("sides", "walls", 2)),
            LengthBias = 0.2, BedGun = 1, Armour = 0.3, Fast = -0.5, Combat = true, LootBase = 1,
        },
        new("carrier", "an APC: crew space, armour, little gun")
        {
            Shapes = [("War Bus", 3), ("Box Truck", 2), ("Double Decker", 2)],
            Needs = N(seatsMin: 3, gunsMax: 2, armour: 11, room: true),
            Modules = M(("body", "room", 2.5), ("body", "gap", 0.5), ("body", "gun bed", 0.4), ("flank", "loaded", 2)),
            LengthBias = 0.5, BedGun = 0.2, Armour = 0.7, Fast = -0.3, LootBase = 1,
        },
        new("tanker", "fuel or cargo, its weak points in the open, rich loot")
        {
            Shapes = [("Scrap Tanker", 4), ("Tanker Rig", 2)],
            Needs = N(gunsMax: 2, lootMin: 1, fuel: 400, fuelOpen: true),
            Modules = M(("body", "tank", 3), ("body", "bed", 1.5), ("rear", "porch hitch", 2)),
            LengthBias = 0.3, BedGun = 0.3, Fast = -0.4, LootBase = 2, ExtraChests = 1,
        },
        new("recovery", "a crane, winch or tow, little gun")
        {
            Shapes = [("Box Truck", 2), ("Flatbed Fort", 1), ("Gun Wagon", 1)],
            Needs = N(gunsMax: 2, tow: true, winch: true),
            Modules = M(("body", "bed", 2.5), ("body", "gun bed", 0.5), ("rear", "hitch", 6), ("rear", "porch hitch", 6), ("rear", "porch", 0.3), ("rear", "gun deck", 0.2), ("rear", "bare", 0.1)),
            BedGun = 0.2, Fast = -0.2, LootBase = 1,
        },
        new("command", "masts and radio, armour, one gun, the best loot")
        {
            Shapes = [("War Bus", 2), ("Box Truck", 2), ("Double Decker", 1)],
            Needs = N(gunsMin: 1, gunsMax: 2, armour: 11, room: true, signals: 2),
            Modules = M(("body", "room", 2), ("body", "gun bed", 0.6), ("roof", "rack", 2), ("cab", "armoured", 2)),
            LengthBias = 0.2, BedGun = 0.2, Armour = 0.5, LootBase = 3, ExtraChests = 1,
        },
        new("boss", "the largest: the most guns and armour, the best loot; rare")
        {
            Shapes = [("War Bus", 3), ("Flatbed Fort", 1), ("Double Decker", 1)],
            Needs = N(gunsMin: 3, armour: 11.5f, lengthMin: 8.5f, room: true),
            Modules = M(("body", "room", 1.5), ("body", "gun bed", 2), ("body", "gap", 0.3), ("rear", "gun deck", 2), ("flank", "loaded", 2)),
            LengthBias = 0.9, BedGun = 1, Armour = 0.8, Fast = -0.6, Combat = true, LootBase = 3, ExtraChests = 2,
        },
    ];

    public static VehicleRole? Find(string name) => All.FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || VehicleRegistry.Slug(r.Name).Equals(VehicleRegistry.Slug(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Which roles each style fields and how often (a weight; 0 or not named: never picked for it, though a request may still ask for one by name). Keyed by style
    /// name here rather than in <c>Styles.cs</c>; a style not named fields <see cref="DefaultFielded"/>.
    /// </summary>
    public static readonly Dictionary<string, Dictionary<string, double>> Fielded = new(StringComparer.Ordinal)
    {
        ["Bandit"] = new() { ["scout"] = 3, ["raider"] = 4, ["gun platform"] = 2, ["carrier"] = 1, ["tanker"] = 2, ["recovery"] = 1, ["command"] = 0.5, ["boss"] = 0.3 },
        ["IT"] = new() { ["scout"] = 2, ["raider"] = 1, ["gun platform"] = 1, ["carrier"] = 3, ["tanker"] = 1, ["recovery"] = 2, ["command"] = 2, ["boss"] = 0.3 },
        ["Scam"] = new() { ["scout"] = 3, ["raider"] = 2, ["gun platform"] = 1, ["carrier"] = 2, ["tanker"] = 1, ["recovery"] = 1, ["command"] = 1.5, ["boss"] = 0.3 },
        ["Highland"] = new() { ["scout"] = 2, ["raider"] = 4, ["gun platform"] = 2, ["carrier"] = 2, ["tanker"] = 1, ["recovery"] = 1, ["command"] = 1, ["boss"] = 0.4 },
        ["British"] = new() { ["scout"] = 1, ["raider"] = 1, ["gun platform"] = 1, ["carrier"] = 4, ["tanker"] = 1, ["recovery"] = 2, ["command"] = 2, ["boss"] = 0.3 },
        ["Militia"] = new() { ["scout"] = 2, ["raider"] = 2, ["gun platform"] = 3, ["carrier"] = 3, ["tanker"] = 1, ["recovery"] = 1, ["command"] = 2, ["boss"] = 0.4 },
    };

    public static readonly Dictionary<string, double> DefaultFielded = new() { ["scout"] = 2, ["raider"] = 2, ["gun platform"] = 2, ["carrier"] = 2, ["tanker"] = 1, ["recovery"] = 1, ["command"] = 1, ["boss"] = 0.3 };

    public static Dictionary<string, double> FieldedBy(FactionStyle st) => Fielded.GetValueOrDefault(st.Name) ?? DefaultFielded;

    /// <summary>A role the style fields, by its weights, from the seed (the server's pick when a request names no role).</summary>
    public static VehicleRole PickRole(FactionStyle st, int seed)
    {
        var table = FieldedBy(st).Where(kv => kv.Value > 0 && Find(kv.Key) != null).ToList();
        var d = Dice.Of(seed).Fork("role pick " + st.Name);
        var roll = d.Next() * table.Sum(kv => kv.Value);
        foreach (var (name, w) in table)
            if ((roll -= w) < 0)
                return Find(name)!;
        return Find(table[^1].Key)!;
    }

    /// <summary>The shapes a style can field a role in: the role's, that the style builds (its extras when it names them).</summary>
    public static List<(IVehicleGenerator G, double W)> ShapesFor(FactionStyle st, VehicleRole role)
    {
        var own = VehicleQualityCli.ShapesOf(st, false).ToList();
        return role.Shapes.Select(s => (G: own.FirstOrDefault(g => VehicleQualityCli.Parts(g).Shape == s.Shape), s.W)).Where(x => x.G != null).Select(x => (x.G!, x.W)).ToList();
    }

    /// <summary>The shape the server picks for a role, from the seed: the same seed always picks the same.</summary>
    public static IVehicleGenerator ShapeFor(FactionStyle st, VehicleRole role, int seed)
    {
        var list = ShapesFor(st, role);
        if (list.Count == 0)
            throw new VehicleKitException($"the {st.Name} style builds no shape a {role.Name} comes in");
        var d = Dice.Of(seed).Fork("role shape " + role.Name);
        var roll = d.Next() * list.Sum(x => x.W);
        foreach (var (g, w) in list)
            if ((roll -= w) < 0)
                return g;
        return list[^1].G;
    }

    // ---- Classes and tiers of parts -------------------------------------------------------------------------------------------

    /// <summary>A gun's class from its receiver's bore and action: rifle and pistol bores light, full-auto medium; shells light; fuel, harpoons and anything bigger heavy.</summary>
    public static GunClass ClassOf(WeaponOption w)
    {
        var (receiver, children) = WeaponOption.Parse(w.Build);
        var bore = Parts.Find(receiver)?.Bores is { Length: > 0 } b ? b[0] : "";
        var auto = children.TryGetValue("action", out var action) && action.Contains("Auto", StringComparison.OrdinalIgnoreCase);
        return bore switch
        {
            "pistol" or "rifle" => auto ? GunClass.Medium : GunClass.Light,
            "shell" => GunClass.Light,
            "" => GunClass.Medium,
            _ => GunClass.Heavy,
        };
    }

    /// <summary>A gun whose receiver is past common (a tier 3 and 4 gun).</summary>
    public static bool RareGun(WeaponOption w) => Parts.Find(WeaponOption.Parse(w.Build).Part) is { } p && p.Rarity > Rarity.Common;

    private static readonly Dictionary<string, int> _tiers = new(StringComparer.Ordinal);

    /// <summary>A part's tier: the least Assembler tier its recipe needs (a plate material by its 1 m size); 1 with no recipe.</summary>
    public static int PartTier(string name)
    {
        lock (_tiers)
        {
            if (_tiers.TryGetValue(name, out var t))
                return t;
            var def = ConstructParts.Find(name) ?? ConstructParts.Find(name + " 1 m");
            t = def == null ? 1 : AssemblingTable.Current.Recipes.FirstOrDefault(r => r.Output == def)?.MinTier ?? 1;
            return _tiers[name] = t;
        }
    }

    /// <summary>A plate material's armour (its 1 m plate's), for <see cref="RoleAsk.Weigh"/>.</summary>
    public static float PlateArmour(string material) => ConstructParts.Find(material + " 1 m")?.Armour ?? ConstructParts.Find(material)?.Armour ?? 6;

    /// <summary>A style's loot table at a loot tier: "bandit 3" when Constructs/Loot.json has it, else the next lower one there, else the style's own.</summary>
    public static string LootTable(string table, int tier)
    {
        for (var t = tier; t >= 3; t--)
            if (ConstructLoot.Tables.ContainsKey($"{table} {t}"))
                return $"{table} {t}";
        return table;
    }

    private static int LootTierOf(string table) => table.Length > 2 && table[^2] == ' ' && char.IsDigit(table[^1]) ? table[^1] - '0' : 2;

    // ---- The measure ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The cheap top speed estimate (m/s): the engine's peak power at 60 % through the drive line against rolling resistance (0.03) and drag (Cd 0.9 over the
    /// frontal box). It runs nothing and knows no gearing: it ranks vehicles, the drive check measures them (a Bandit Technical estimates about 37, drives 24 in 6 s).
    /// </summary>
    public static float Speed(float powerW, float massKg, float frontalM2)
    {
        if (powerW <= 0 || massKg <= 0)
            return 0;
        var (lo, hi) = (0.0, 120.0);
        for (var k = 0; k < 40; k++)
        {
            var v = (lo + hi) * 0.5;
            var need = (0.03 * massKg * 9.81 + 0.5 * 1.2 * 0.9 * frontalM2 * v * v) * v;
            if (need > 0.6 * powerW)
                hi = v;
            else
                lo = v;
        }
        return (float)lo;
    }

    /// <summary>A built vehicle against <paramref name="ask"/>'s needs (or no needs: only the numbers). <paramref name="identity"/>: its identity score, for the open fuel; scored here when null.</summary>
    public static RoleFit Measure(Vehicle v, RoleAsk? ask, VehicleIdentity.Score? identity = null) => v.MeasureRole(ask, identity);
}

public sealed partial class Vehicle
{
    /// <summary>Kit 4: the role and tier it was made for (<see cref="VehicleRoles"/>); null for every other vehicle.</summary>
    public RoleAsk? Role { get; set; }

    /// <summary>What it measured against its role when it was made (kit 4), or null.</summary>
    public RoleFit? Fit { get; set; }

    /// <summary>How many plans the role asked again for (kit 4).</summary>
    public int RolePlans { get; set; }

    internal RoleFit MeasureRole(RoleAsk? ask, VehicleIdentity.Score? identity)
    {
        var c = BuiltBody();
        int seats = 0, winches = 0;
        float fuel = 0, area = 0, armour = 0;
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            c.PartBounds(i, out var a, out var b);
            var seg = c.Parts[i].Seg;
            if (!(seg > 0 && seg <= c.JointCount && c.Joints[seg - 1].Freeform)) // (a weld's bounds are its own segment's numbers: a patch is inside the outline anyway)
            {
                lo = Vector3.Min(lo, a);
                hi = Vector3.Max(hi, b);
            }
            if (d.Role == PartRole.Seat)
                seats++;
            if (d.Role == PartRole.FuelTank)
                fuel += d.FuelCapacity;
            if (d.Name.Contains("Winch", StringComparison.Ordinal))
                winches++;
            if (d.Role == PartRole.Panel)
            {
                var e = b - a;
                var s = MathF.Max(e.X * e.Y, MathF.Max(e.Y * e.Z, e.X * e.Z)); // (a plate's face, whichever way it lies)
                area += s;
                armour += s * d.Armour;
            }
        }
        var size = hi - lo;
        var power = Engine is { } en ? en.Def.EngineTorque * en.Def.PeakRpm * MathF.Tau / 60f : 0;
        var speed = VehicleRoles.Speed(power, c.Mass + SpareMass, size.X * size.Y); // (with the cargo pass's loose pieces aboard: VehicleCargo.cs)
        var best = Turrets.Select(t => VehicleRoles.ClassOf(t.Weapon)).DefaultIfEmpty(GunClass.None).Max();
        var signalSlot = Style.Parts.ContainsKey("mast") ? "mast" : "trophy";
        var signalNames = Style.Parts.GetValueOrDefault(signalSlot) ?? [];
        var signals = Parts.Count(p => Array.IndexOf(signalNames, p.Name) >= 0);
        var loot = LootSpots + CargoPieces - SparePieces; // (its own spares and ammo aren't the role's loot: VehicleCargo.cs)
        var lootTier = LootSpots == 0 ? 0 : LootTables.Max(t => t.Length > 2 && t[^2] == ' ' && char.IsDigit(t[^1]) ? t[^1] - '0' : 2);
        var deck = Panels.TryGetValue("bed", out var bed) && bed.Count > 0 || Panels.TryGetValue("porch", out var porch) && porch.Count > 0;
        var room = Rooms.Count > 0;
        var rating = area > 0 ? armour / area : 0;
        var misses = new List<string>();
        if (ask != null)
        {
            var n = ask.Needs;
            void Need(bool ok, string why)
            {
                if (!ok)
                    misses.Add(why);
            }
            Need(seats >= n.SeatsMin, $"{seats} seats, under {n.SeatsMin}");
            Need(seats <= n.SeatsMax, $"{seats} seats, over {n.SeatsMax}");
            Need(Turrets.Count >= n.GunsMin, $"{Turrets.Count} guns, under {n.GunsMin}");
            Need(Turrets.Count <= n.GunsMax, $"{Turrets.Count} guns, over {n.GunsMax}");
            Need(Turrets.Count == 0 || best >= n.ClassMin, $"its heaviest gun is {best.ToString().ToLowerInvariant()}, under {n.ClassMin.ToString().ToLowerInvariant()}");
            Need(best <= n.ClassMax, $"a {best.ToString().ToLowerInvariant()} gun, over {n.ClassMax.ToString().ToLowerInvariant()}");
            Need(speed >= n.SpeedMin, $"est. top speed {speed:0} m/s, under {n.SpeedMin:0}");
            Need(speed <= n.SpeedMax, $"est. top speed {speed:0} m/s, over {n.SpeedMax:0}");
            Need(rating >= n.ArmourMin, $"armour {rating:0.0}, under {n.ArmourMin:0.0}");
            Need(loot >= n.LootMin, $"loot {loot}, under {n.LootMin}");
            Need(loot <= n.LootMax, $"loot {loot}, over {n.LootMax}");
            Need(size.Z >= n.LengthMin, $"{size.Z:0.0} m long, under {n.LengthMin:0.0}");
            Need(size.Z <= n.LengthMax, $"{size.Z:0.0} m long, over {n.LengthMax:0.0}");
            Need(fuel >= n.FuelMin, $"fuel {fuel:0} l, under {n.FuelMin:0}");
            Need(!n.Room || room, "no room");
            Need(!n.Deck || deck, "no open deck");
            Need(!n.Tow || Hitch != null, "no tow hitch");
            Need(!n.Winch || winches > 0, "no winch");
            Need(signals >= n.SignalsMin, $"{signals} signals, under {n.SignalsMin}");
            if (n.FuelOpen)
            {
                identity ??= VehicleIdentity.Of(this);
                Need(identity.WeakPoints.Any(w => w.Label.Contains("fuel", StringComparison.OrdinalIgnoreCase) && w.Found > 0 && w.Passed), "its fuel isn't in the open");
            }
        }
        return new RoleFit(seats, Turrets.Count, best, speed, rating, loot, lootTier, size.Z, fuel, room, deck, Hitch != null, winches, signals, c.Mass, misses);
    }

    /// <summary>
    /// Kit 4's role pass, after the dressing and the marks: what the role is for that the shape didn't bring. A recovery vehicle's winch on a free up-facing socket of
    /// its bed, porch or tail (out of every turret's sweep, under open sky); a command vehicle's signals (the style's masts, else its trophies) on the marks pass's spots;
    /// the role's extra chests in its room; then every loot container's table at the role's loot tier. Each on a fork of its own, in the kit layer.
    /// </summary>
    public Vehicle RoleKit(Room? room)
    {
        if (Role is not { } ask)
            return this;
        var n = ask.Needs;
        if (n.Winch && !Parts.Any(p => p.Name.Contains("Winch", StringComparison.Ordinal)))
            OnDeck("Cargo Winch 16 m", null, Dice.Fork("kit role winch"));
        var hull = ask.Family ? ask.ArmourLean : ask.KitArmourLean; // (a family's marks keep Mk1's hull appliqué: their own goes on low, VehicleFamilies.cs)
        if (hull >= 0.3)
            UpArmour(hull, Dice.Fork("kit role armour"));
        if (n.SignalsMin > 0)
        {
            var slot = Style.Parts.ContainsKey("mast") ? "mast" : "trophy";
            var names = Style.Parts.GetValueOrDefault(slot) ?? [];
            var d = Dice.Fork("kit role signals");
            var parts = d.Shuffled(names.Distinct()).Where(x => ConstructParts.Find(x) is { } def && Array.Exists(def.Sockets, s => s.Name == "base")).ToList();
            for (var k = 0; k < 4 && parts.Count > 0 && Parts.Count(p => Array.IndexOf(names, p.Name) >= 0) < n.SignalsMin; k++)
            {
                VehicleGenJob.Yield();
                if (PlaceMark(parts, d) is not { } placed)
                    break;
                MarksPlaced.Add($"{ask.Role.Name} signals: a {placed.Name} on the {_markHost}");
            }
        }
        room ??= Rooms.FirstOrDefault();
        var chests = ask.Role.ExtraChests + (ask.Tier >= 4 ? 1 : 0) + (ask.Named ? 1 : 0);
        for (var k = 0; k < chests && room != null; k++)
        {
            VehicleGenJob.Yield();
            if (!EndChest(room))
                break;
        }
        if (room == null && chests > 0) // (no room: the loot rides on the deck, in the open, as a tanker's rich haul should)
        {
            var cd = Dice.Fork("kit role chests");
            for (var k = 0; k < chests; k++)
            {
                VehicleGenJob.Yield();
                if (OnDeck(Style.Part(cd, "chest"), "bottom", cd) is not { } chest)
                    break;
                Loot(chest, Style.LootTable);
            }
        }
        if (ask.Family)
            FamilyKit(ask); // (a family's variant and a named unique's extra mark: VehicleFamilies.cs)
        var tier = ask.LootTier;
        RetableLoot(t => VehicleRoles.LootTable(t, tier));
        StowCargo(); // (its ammo, spares and spare guns, stowed as a crew would: VehicleCargo.cs)
        return this;
    }

    /// <summary>
    /// <paramref name="part"/> (by its socket <paramref name="by"/>, null: the build's pick) on the first free up-facing socket of the porch, the bed, the rack, a tank or
    /// the tail rails that takes it clear of the guns and under open sky: a recovery vehicle's winch, a roomless vehicle's chest. Null when none does.
    /// </summary>
    private VPart? OnDeck(string part, string? by, Dice d, bool clear = false)
    {
        var def = Def(part);
        IEnumerable<VPart> Tag(string t) => Panels.GetValueOrDefault(t) ?? [];
        foreach (var on in new[] { Tag("porch"), Tag("bed"), Tag("rack"), Tanks, TailL.Concat(TailR) })
            foreach (var p in d.Shuffled(on))
            {
                if (p.Seg != 0 && !C.Rigid(p.Seg))
                    continue;
                foreach (var socket in FreeSockets(p, Vector3.UnitY))
                {
                    C.SocketAt(p.Index, Array.FindIndex(p.Def.Sockets, k => k.Name == socket), out var at, out _);
                    if (!ClearOfGunsBox(at, def) || !OpenSkyBox(at, def))
                        continue;
                    try
                    {
                        return Onto(p, socket, part, by, clear: clear);
                    }
                    catch (VehicleKitException)
                    {
                        // (no room for it on that socket: the next)
                    }
                }
            }
        return null;
    }

    /// <summary>
    /// Appliqué armour (kit 4, a role that leans to armour): the style's heaviest armour plate, a metre square, welded flat over the sides of its rooms' hull bays, a few
    /// centimetres proud of each bay's widest point (so no face is shared and nothing is inside the room), never on the door's side of the door bay, never touching another
    /// plate (a metre apart and more), and only where it meets nothing else: panniers, sponsons, the ladder, wheels. As many as the lean asks, up to half the bays' sides.
    /// In the style's body colours, so the palette stays the style's.
    /// </summary>
    private void UpArmour(double lean, Dice d)
    {
        var material = Style.Parts.GetValueOrDefault("armour")?.OrderByDescending(VehicleRoles.PlateArmour).ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? "Plate Steel";
        if (ConstructParts.Find(material + " 1 m") is not { } def)
            return;
        var plate = def.Name;
        var e = def.BoundsMax - def.BoundsMin;
        var thick = MathF.Min(e.X, MathF.Min(e.Y, e.Z));
        var spots = new List<(VPart Bay, float Side)>();
        foreach (var room in Rooms)
            foreach (var bay in room.Bays)
                foreach (var side in new[] { -1f, 1f })
                    if (!(side > 0 && bay.Name.Contains("Door", StringComparison.Ordinal)))
                        spots.Add((bay, side));
        var want = (int)MathF.Round((float)(lean * spots.Count * 0.5));
        var placed = new List<Vector3>();
        foreach (var (bay, side) in d.Shuffled(spots))
        {
            if (placed.Count >= want)
                break;
            VehicleGenJob.Yield(); // (each try reads every part's bounds)
            C.PartBounds(bay.Index, out var mn, out var mx);
            var (lo, hi) = (FromRest(mn), FromRest(mx));
            var c = CentreOf(bay);
            var at = new Vector3((side > 0 ? hi.X : lo.X) + side * (thick * 0.5f + 0.04f), MathF.Min(c.Y, hi.Y - 0.55f), c.Z);
            if (placed.Any(p => Vector3.Distance(p, at) < 1.05f) || _welded.Any(w => Vector3.Distance(w, at) < 0.9f) || !Clear(bay, at, new Vector3(thick * 0.5f + 0.02f, 0.5f, 0.5f)))
                continue;
            var p = Weld(plate, bay, at, Orient.Facing(new Vector3(side, 0, 0)), 0, 0, 0);
            PanelList("armour").Add(p);
            _welded.Add(at);
            placed.Add(at);
            if (Style.Palette.ContainsKey("body"))
                Paint(p, Style.Colour(d, "body"));
        }
        if (placed.Count > 0)
            MarksPlaced.Add($"armour: {placed.Count} {plate} on the hull");
    }

    /// <summary>Whether a box of half size <paramref name="half"/> at <paramref name="at"/> (vehicle coordinates) meets no part but <paramref name="on"/> (welded parts judged by their spots).</summary>
    private bool Clear(VPart on, Vector3 at, Vector3 half)
    {
        var (mn, mx) = (at - half, at + half);
        for (var i = 0; i < C.PartCount; i++)
        {
            var seg = C.Parts[i].Seg;
            if (i == on.Index || seg > 0 && seg <= C.JointCount && C.Joints[seg - 1].Freeform)
                continue;
            C.PartBounds(i, out var bmn, out var bmx);
            var (qmn, qmx) = (FromRest(bmn), FromRest(bmx));
            if (qmn.X < mx.X && qmx.X > mn.X && qmn.Y < mx.Y && qmx.Y > mn.Y && qmn.Z < mx.Z && qmx.Z > mn.Z)
                return false;
        }
        return true;
    }

    /// <summary>As the marks pass's turret test, for a part of <paramref name="def"/>'s size standing on <paramref name="at"/> by its bounds: lower than every ring's top, or out of its guns' reach.</summary>
    private bool ClearOfGunsBox(Vector3 at, ConstructPartDef def)
    {
        if (Turrets.Count == 0)
            return true;
        var e = def.BoundsMax - def.BoundsMin;
        var top = at.Y + MathF.Max(e.X, MathF.Max(e.Y, e.Z));
        var reach = MathF.Max(1.2f, Turrets.Max(t => GunReach(t.Weapon))) + 0.4f + MathF.Max(e.X, e.Z) * 0.5f;
        foreach (var t in Parts)
        {
            if (t.Name != "Turntable")
                continue;
            C.PartBounds(t.Index, out var lo, out var hi);
            var ring = (lo + hi) * 0.5f;
            if (top > hi.Y - 0.05f && new Vector2(ring.X - at.X, ring.Z - at.Z).Length() < reach)
                return false;
        }
        return true;
    }

    /// <summary>Open sky over a part of <paramref name="def"/>'s size standing on <paramref name="at"/>, by its bounds (it has no "base" socket).</summary>
    private bool OpenSkyBox(Vector3 at, ConstructPartDef def)
    {
        var e = def.BoundsMax - def.BoundsMin;
        var half = MathF.Max(e.X, e.Z) * 0.5f;
        return Empty(new Vector3(at.X - half, at.Y + MathF.Max(e.X, MathF.Max(e.Y, e.Z)) + 0.02f, at.Z - half), new Vector3(at.X + half, at.Y + 30, at.Z + half));
    }
}

/// <summary>Kit 4's making of a role's vehicle (<see cref="ShapeGrammar.Make"/> for a grammar shape, <see cref="VehicleRegistry.Make"/> for the rest): the plans it takes, its log line.</summary>
public static class VehicleRoleMake
{
    /// <summary>Plans a role may take in all: the first, and up to two more for a role it missed or an identity under the style's mark (bounded, as the identity gate is).</summary>
    public const int Plans = 3;

    /// <summary>Plans that missed their role, to measure what it costs.</summary>
    public static int Replanned;

    /// <summary>How a plan ranks: fewest role misses, then fewest marks missing, then the identity score.</summary>
    public static (int, int, int, double) Rank(Vehicle v, VehicleIdentity.Score s) => !VehicleIdentity.GateOn
        ? (v.Fit?.Misses.Count ?? 0, 0, 0, 0) // (--no-identity-gate: the role only)
        : (v.Fit?.Misses.Count ?? 0, s.Missing.Count, s.Total >= VehicleIdentity.SignatureOf(v.Style).Threshold ? 0 : 1, -s.Total);

    /// <summary>The kit line's notes and the description's role: called on the plan kept.</summary>
    public static void Note(Vehicle v, RoleAsk ask, string shape)
    {
        v.Role = ask;
        v.KitNotes.Clear();
        v.KitNotes.Add($"role {ask.Role.Name}");
        v.KitNotes.Add($"tier {ask.Tier}");
        v.KitNotes.Add($"shape {shape}");
        v.KitNotes.Add(v.Fit is { Met: true } ? "role met" : $"role missed ({string.Join("; ", v.Fit?.Misses ?? [])})");
        if (v.RolePlans > 0)
            v.KitNotes.Add($"role plans {v.RolePlans + 1}");
        if (v.Fit != null)
            v.KitNotes.Add($"fit {v.Fit}");
        if (v.CargoNote is { Length: > 0 } cargo)
            v.KitNotes.Add(cargo);
        var article = "aeiou".Contains(ask.Role.Name[0]) ? "An" : "A";
        if (!v.Description.StartsWith(article + " " + ask.Role.Name, StringComparison.Ordinal))
            v.Description = $"{article} {ask.Role.Name} (tier {ask.Tier}). {v.Description}";
        if (ask.Model is { } model)
            VehicleFamilies.Note(v, ask, model, shape); // (a family's mark: its model's name and key)
    }

    /// <summary>
    /// A kit-1 shape's vehicle for a role (the Technical, the Tanker Rig, the Double Decker: no grammar to lean): built by its generator with the role's part weights,
    /// its marks and the role pass, measured, and if it misses the role, tried again from seeds derived from it (<see cref="VehicleSpawnKit.Reroll"/>), up to
    /// <see cref="Plans"/> in all; the best is kept. The seed that made it is in its name.
    /// </summary>
    public static Vehicle Kit1(IVehicleGenerator g, int seed, RoleAsk ask)
    {
        Vehicle? best = null;
        (int, int, int, double) bestRank = default;
        Exception? last = null;
        var shape = VehicleQualityCli.Parts(g).Shape;
        var tried = 0;
        for (var k = 0; k < Plans; k++)
        {
            tried++;
            var s = k == 0 ? seed : VehicleSpawnKit.Reroll(seed, 100 + k);
            VehicleGenJob.Yield();
            var v = new Vehicle(VehicleRegistry.BlueprintName(g, s) + ask.Suffix, g.Name, g.Style, s) { Role = ask };
            var (weigh, scheme) = (FactionStyle.Weigh, FactionStyle.Scheme);
            (FactionStyle.Weigh, FactionStyle.Scheme) = (ask.Weigh, ask.Scheme);
            try
            {
                g.Build(v);
                v.GuaranteeMarks();
                v.RoleKit(null);
                var least = (g.Checks ?? new VehicleCheckOptions()).MinStability + 0.02f; // (a kit-1 generator has no stability gate of its own: a role's parts can stand it taller)
                if (v.Stability() is var stable && stable < least)
                    throw new VehicleKitException($"stability {stable:0.00}: under the check's {least - 0.02f:0.00}");
                v.DriveGate();
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                last = ex;
                continue;
            }
            finally
            {
                (FactionStyle.Weigh, FactionStyle.Scheme) = (weigh, scheme);
            }
            v.Kit = VehicleKitVersion.Roles;
            var id = VehicleIdentity.Of(v);
            v.Identity = id;
            v.Fit = VehicleRoles.Measure(v, ask, id);
            var rank = Rank(v, id);
            if (best == null || rank.CompareTo(bestRank) < 0)
                (best, bestRank) = (v, rank);
            if (rank.Item1 == 0 && rank.Item2 == 0 && rank.Item3 == 0 || ask.Family) // (a family's mark keeps the seed that built: its marks share it)
                break;
            if (k + 1 < Plans)
            {
                Interlocked.Increment(ref Replanned);
                Console.WriteLine($"[vehicles] {v.Name}: misses the {ask.Role.Name} ({(v.Fit.Met ? $"identity {id.Total:0.00}" : string.Join("; ", v.Fit.Misses))}); trying seed {VehicleSpawnKit.Reroll(seed, 101 + k)}");
            }
        }
        if (best == null)
            throw last ?? new VehicleKitException($"no {g.Name} could be made for a {ask.Role.Name}");
        best.RolePlans = tried - 1;
        Note(best, ask, shape);
        return best;
    }
}

/// <summary>The command line for roles: <c>--role "Style Role" --tier N [--seeds N]</c> and the matrix in <c>--kit-report --roles</c>.</summary>
public static class VehicleRolesCli
{
    private static string? Flag(string[] args, string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));

    private static (FactionStyle? St, VehicleRole? Role) Parse(string text)
    {
        foreach (var st in Styles.All)
            foreach (var prefix in new[] { st.Name, st.Group })
                if (text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                    return (st, VehicleRoles.Find(text[(prefix.Length + 1)..]));
        return (null, null);
    }

    /// <summary>One vehicle for a style, role, tier and seed: what was built, how it met the role, its identity, what it cost.</summary>
    public sealed record Outcome(int Seed, string Shape, Vehicle? V, string Error, double Ms, int Plans);

    public static Outcome Make(FactionStyle st, VehicleRole role, int tier, int seed)
    {
        var clock = Stopwatch.StartNew();
        IVehicleGenerator? g = null;
        try
        {
            g = VehicleRoles.ShapeFor(st, role, seed);
            var ask = new RoleAsk(role, tier, st) { Shape = VehicleQualityCli.Parts(g).Shape };
            var v = VehicleRegistry.Make(g, seed, role: ask);
            if (v.Fit == null) // (--kit 3 or below: made without its role, measured against it all the same)
            {
                v.Identity ??= VehicleIdentity.Of(v);
                v.Fit = VehicleRoles.Measure(v, ask, v.Identity);
            }
            v.Identity ??= VehicleIdentity.Of(v);
            return new Outcome(seed, ask.Shape, v, "", clock.Elapsed.TotalMilliseconds, v.RolePlans + 1);
        }
        catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
        {
            return new Outcome(seed, g == null ? "" : VehicleQualityCli.Parts(g).Shape, null, ex.Message, clock.Elapsed.TotalMilliseconds, 0);
        }
    }

    private static void Problems()
    {
        foreach (var p in ConstructLoot.Problems)
            Console.WriteLine($"loot: {p}");
    }

    /// <summary>
    /// <c>--role "Bandit Raider" --tier 2 [--seeds N] [--from S] [--seed-list A,B] [--check] [--sliced]</c>: each seed's shape, fit, identity and what the role pass placed, then the hit
    /// rate; the first seed is made again (the same text), with <c>--sliced</c> as the dynamic spawn makes it (the same hash), and <c>--check</c> runs the full check on each
    /// (seconds a seed). Exit code: how many missed, failed or differed.
    /// </summary>
    public static int Role(string text, string[] args)
    {
        var (st, role) = Parse(text);
        if (st == null || role == null)
        {
            Console.WriteLine($"--role \"{text}\": a style and a role, \"Bandit raider\". Roles: {string.Join(", ", VehicleRoles.All.Select(r => r.Name))}");
            return 1;
        }
        var tier = int.TryParse(Flag(args, "--tier"), out var t) ? Math.Clamp(t, 1, VehicleRoles.MaxTier) : 2;
        var seeds = VehicleQualityCli.SeedsFrom(args, 3);
        Problems();
        var ask = new RoleAsk(role, tier, st);
        Console.WriteLine($"{st.Name} {role.Name} ({role.Purpose}), tier {tier}: shapes {string.Join(", ", VehicleRoles.ShapesFor(st, role).Select(x => $"{VehicleQualityCli.Parts(x.G).Shape} {x.W}"))}");
        Console.WriteLine($"  needs: {Describe(ask.Needs)}; loot tier {ask.LootTier}, armour lean {ask.ArmourLean:0.00}");
        var bad = 0;
        var check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        var checkedBad = 0;
        var outs = new List<Outcome>();
        foreach (var seed in seeds)
        {
            var o = Make(st, role, tier, seed);
            outs.Add(o);
            if (o.V is not { } v)
            {
                bad++;
                Console.WriteLine($"  seed {seed} ({o.Shape}): doesn't generate: {o.Error}");
                continue;
            }
            bad += v.Fit!.Met ? 0 : 1;
            Console.WriteLine($"  seed {seed}: {v.Name}, kit {v.Kit}, {o.Plans} plan(s), {o.Ms:0} ms: {(v.Fit.Met ? "MET" : "MISSED " + string.Join("; ", v.Fit.Misses))}");
            Console.WriteLine($"    {v.Fit}");
            Console.WriteLine($"    identity {v.Identity}");
            Console.WriteLine($"    {v.Description}");
            if (v.MarksPlaced.Count > 0)
                Console.WriteLine($"    placed: {string.Join("; ", v.MarksPlaced)}");
            if (check) // (the full check, as --sweep-vehicle runs it: drive, turn, guns, coplanar faces, rooms)
            {
                var g = VehicleRoles.ShapeFor(st, role, seed);
                var r = VehicleChecks.Check(v.Text(), VehicleRegistry.Slug(v.Name) + ".json", g.Checks, v.Rooms);
                checkedBad += r.Passed ? 0 : 1;
                Console.WriteLine(r.Passed ? "    check: passed" : $"    check: FAILED {string.Join("; ", r.Failures)}");
            }
        }
        Summary(outs);
        if (check)
            Console.WriteLine($"  the full check: {outs.Count(o => o.V != null) - checkedBad}/{outs.Count(o => o.V != null)} passed");
        bad += checkedBad;
        if (outs.FirstOrDefault(o => o.V != null) is { } first)
            bad += Again(st, role, tier, first, args.Any(a => a.Equals("--sliced", StringComparison.OrdinalIgnoreCase)));
        return bad;
    }

    /// <summary>
    /// The first seed made again, which must write the same text (as <c>--gen-vehicle</c> proves a seed), and with <paramref name="sliced"/> once more as the game's
    /// dynamic spawn makes it (<see cref="VehicleGenJob"/>, 2.5 ms slices on its own thread), whose hash must be the direct run's. 1 when they differ.
    /// </summary>
    private static int Again(FactionStyle st, VehicleRole role, int tier, Outcome first, bool sliced)
    {
        var text = first.V!.Text();
        var again = Make(st, role, tier, first.Seed);
        var same = again.V?.Text() == text;
        Console.WriteLine($"  seed {first.Seed} made again: {(same ? "the same text" : "DIFFERENT TEXT")}");
        if (!sliced)
            return same ? 0 : 1;
        var g = VehicleRoles.ShapeFor(st, role, first.Seed);
        var job = new VehicleGenJob(g, first.Seed, default, new RoleAsk(role, tier, st) { Shape = first.Shape });
        while (!job.Step(2.5))
        {
        }
        var hash = VehicleGenJob.Fnv(text);
        Console.WriteLine($"  seed {first.Seed} as a dynamic spawn makes it: {job.Name}, {(job.Blueprint == null ? "FAILED: " + job.Error : job.Hash == hash ? "the same hash" : "A DIFFERENT HASH")} over {job.Slices} slices; kit {job.Kit}: {job.RoleNotes}");
        return same && job.Hash == hash ? 0 : 1;
    }

    private static string Describe(RoleNeeds n)
    {
        var bits = new List<string>();
        void Range(string what, double lo, double hi, double none, string unit = "")
        {
            if (lo > 0 && hi < none)
                bits.Add($"{what} {lo:0.#}-{hi:0.#}{unit}");
            else if (lo > 0)
                bits.Add($"{what} {lo:0.#}+{unit}");
            else if (hi < none)
                bits.Add($"{what} up to {hi:0.#}{unit}");
        }
        Range("seats", n.SeatsMin, n.SeatsMax, 99);
        Range("guns", n.GunsMin, n.GunsMax, 99);
        if (n.ClassMin > GunClass.None || n.ClassMax < GunClass.Heavy)
            bits.Add($"gun class {n.ClassMin.ToString().ToLowerInvariant()} to {n.ClassMax.ToString().ToLowerInvariant()}");
        Range("est. top speed", n.SpeedMin, n.SpeedMax, 999, " m/s");
        Range("armour", n.ArmourMin, 999, 999);
        Range("loot", n.LootMin, n.LootMax, 99);
        Range("length", n.LengthMin, n.LengthMax, 99, " m");
        Range("fuel", n.FuelMin, 1e9, 1e9, " l");
        Range("signals", n.SignalsMin, 99, 99);
        foreach (var (on, what) in new[] { (n.Room, "a room"), (n.Deck, "an open deck"), (n.Tow, "a tow hitch"), (n.Winch, "a winch"), (n.FuelOpen, "its fuel in the open") })
            if (on)
                bits.Add(what);
        return string.Join(", ", bits);
    }

    private static void Summary(IReadOnlyList<Outcome> outs)
    {
        var made = outs.Where(o => o.V != null).ToList();
        if (made.Count == 0)
            return;
        Console.WriteLine($"  {made.Count(o => o.V!.Fit!.Met)}/{outs.Count} met the role, {outs.Count - made.Count} failed to generate; identity mean {made.Average(o => o.V!.Identity!.Total):0.00}, {made.Count(o => o.V!.Identity!.Missing.Count == 0)}/{made.Count} with every mark; {outs.Average(o => o.Ms):0} ms mean, {outs.Max(o => o.Ms):0} worst, {made.Average(o => (double)o.Plans):0.0} plans");
        foreach (var g in made.SelectMany(o => o.V!.Fit!.Misses).GroupBy(m => VehicleQualityCli.Reason(m)).OrderByDescending(g => g.Count()).Take(4))
            Console.WriteLine($"    {g.Count()}x {g.Key}");
    }

    /// <summary>
    /// <c>--kit-report --roles [--seeds N] [--style S] [--role R] [--tier N]</c>: every style, role and tier at N seeds (default 3): the role-hit rate, identity, failures and
    /// generation cost, a row each, then by role and by tier. Exit code: 0.
    /// </summary>
    public static int Matrix(string[] args)
    {
        var seeds = VehicleQualityCli.SeedsFrom(args, 3);
        var styleOnly = Flag(args, "--style");
        var roleOnly = Flag(args, "--role") ?? (args.SkipWhile(a => !a.Equals("--roles", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--") && VehicleRoles.Find(a) != null));
        var tiers = int.TryParse(Flag(args, "--tier"), out var one) ? [Math.Clamp(one, 1, VehicleRoles.MaxTier)] : Enumerable.Range(1, VehicleRoles.MaxTier).ToArray();
        var raw = args.Any(a => a.Equals("--raw", StringComparison.OrdinalIgnoreCase));
        Problems();
        Make(Styles.Bandit, VehicleRoles.All[0], 2, 1); // (a throwaway: the JIT's first generation isn't counted)
        var rows = new List<(string Style, string Role, int Tier, List<Outcome> O)>();
        Console.WriteLine($"role report: {seeds.Count} seed(s) a cell ({seeds.FirstOrDefault()} to {seeds.LastOrDefault()}), kit {VehicleKitVersion.Force?.ToString() ?? "4"}, generation only");
        foreach (var st in Styles.All.Where(s => styleOnly == null || s.Name.Equals(styleOnly, StringComparison.OrdinalIgnoreCase)))
            foreach (var role in VehicleRoles.All.Where(r => roleOnly == null || r.Name.Equals(roleOnly, StringComparison.OrdinalIgnoreCase)))
                foreach (var tier in tiers)
                {
                    var outs = seeds.Select(s => Make(st, role, tier, s)).ToList();
                    rows.Add((st.Name, role.Name, tier, outs));
                    var made = outs.Where(o => o.V != null).ToList();
                    if (raw)
                        foreach (var o in made)
                            Console.WriteLine($"    raw {st.Name}|{role.Name}|{tier}|{o.Seed}|{o.Shape}|{o.V!.Fit!.Seats}|{o.V.Fit.Guns}|{o.V.Fit.Best}|{o.V.Fit.Speed:0.0}|{o.V.Fit.Armour:0.0}|{o.V.Fit.Loot}|{o.V.Fit.LootTier}|{o.V.Fit.Length:0.0}|{o.V.Fit.Fuel:0}|{o.V.Fit.Mass:0}|{o.V.Identity!.Total:0.00}|{o.Plans}|{o.Ms:0}|{string.Join("; ", o.V.Fit.Misses)}");
                    Console.WriteLine($"  {st.Name} {role.Name} tier {tier}: met {made.Count(o => o.V!.Fit!.Met)}/{outs.Count}{(made.Count < outs.Count ? $", {outs.Count - made.Count} failed ({VehicleQualityCli.Reason(outs.First(o => o.V == null).Error)})" : "")}"
                        + $"{string.Concat(made.Where(o => !o.V!.Fit!.Met).Take(2).Select(o => $"; {o.Seed} {o.Shape}: {string.Join(", ", o.V!.Fit!.Misses)}"))}");
                }
        Console.WriteLine();
        Console.WriteLine($"{"Style",-10} {"Role",-13} {"tier",4} {"met",5} {"fail",4} {"ident",6} {"marks",5} {"plans",5} {"ms mean",8} {"ms worst",8}  shapes");
        foreach (var (style, role, tier, outs) in rows)
        {
            var made = outs.Where(o => o.V != null).ToList();
            Console.WriteLine($"{style,-10} {role,-13} {tier,4} {$"{made.Count(o => o.V!.Fit!.Met)}/{outs.Count}",5} {outs.Count - made.Count,4} {(made.Count == 0 ? "-" : made.Average(o => o.V!.Identity!.Total).ToString("0.00")),6} {$"{made.Count(o => o.V!.Identity!.Missing.Count == 0)}/{made.Count}",5} {(made.Count == 0 ? 0 : made.Average(o => (double)o.Plans)),5:0.0} {outs.Average(o => o.Ms),8:0} {outs.Max(o => o.Ms),8:0}  {string.Join(" ", made.GroupBy(o => o.Shape).Select(g => $"{g.Key}x{g.Count()}"))}");
        }
        void By(string what, Func<(string Style, string Role, int Tier, List<Outcome> O), string> key)
        {
            Console.WriteLine();
            foreach (var g in rows.GroupBy(key))
            {
                var outs = g.SelectMany(r => r.O).ToList();
                var made = outs.Where(o => o.V != null).ToList();
                Console.WriteLine($"{what} {g.Key}: met {made.Count(o => o.V!.Fit!.Met)}/{outs.Count} ({(outs.Count == 0 ? 0 : made.Count(o => o.V!.Fit!.Met) / (double)outs.Count):P0}), failed {outs.Count - made.Count}, identity {(made.Count == 0 ? 0 : made.Average(o => o.V!.Identity!.Total)):0.00}, every mark {made.Count(o => o.V!.Identity!.Missing.Count == 0)}/{made.Count}, {outs.Average(o => o.Ms):0} ms mean");
                foreach (var m in made.SelectMany(o => o.V!.Fit!.Misses).GroupBy(VehicleQualityCli.Reason).OrderByDescending(m => m.Count()).Take(3))
                    Console.WriteLine($"    {m.Count()}x {m.Key}");
            }
        }
        By("role", r => r.Role);
        By("tier", r => r.Tier.ToString());
        By("style", r => r.Style);
        var all = rows.SelectMany(r => r.O).ToList();
        var ok = all.Where(o => o.V != null).ToList();
        Console.WriteLine();
        Console.WriteLine($"all: {ok.Count(o => o.V!.Fit!.Met)}/{all.Count} met their role, {all.Count - ok.Count} failed to generate, identity mean {(ok.Count == 0 ? 0 : ok.Average(o => o.V!.Identity!.Total)):0.00}, {all.Average(o => o.Ms):0} ms a vehicle, worst {all.Max(o => o.Ms):0}; plans re-made for a role {VehicleRoleMake.Replanned}");
        return 0;
    }
}
