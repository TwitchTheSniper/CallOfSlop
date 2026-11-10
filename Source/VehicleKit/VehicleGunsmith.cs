namespace CallOfSlop.VehicleKit;

/// <summary>
/// The gunsmith (docs/sandbox/VEHICLE-WEAPONS.md): big guns made for one vehicle, as weapon-builder text the kit fits through the game's own rules (<see cref="Vehicle.FitGun"/>).
/// The vehicle's mass sets the ceiling: a gun's mass (its parts' masses times N squared, as a merged receiver's are) at most <see cref="GunMassShare"/> of the vehicle's, and its
/// recoil impulse at most <see cref="KickPerKg"/> times the vehicle's mass, so a shot moves it at most that many m/s. The biggest merge N that passes both is the ceiling;
/// the role's tier picks within it (tier 1 two steps under, tier 2 one, tier 3 and 4 at it). Phase 1 classes: heavy cannons (artillery receivers, shells with big blasts
/// and craters) and rapid heavy guns (autocannons, rotary and flak blocks, chain guns). The guns join the style's list for that vehicle (<see cref="Vehicle.Arsenal"/>),
/// so every existing recipe and style gun can still be drawn.
/// </summary>
public static class VehicleGunsmith
{
    /// <summary>Off: <c>--no-gunsmith</c>, the style's list alone, to measure what the gunsmith changes.</summary>
    public static bool On = true;

    /// <summary>A gun's mass at most this share of the vehicle's (the mount's weight rating).</summary>
    public const float GunMassShare = 0.12f;

    /// <summary>A shot's recoil impulse (N s) at most this times the vehicle's mass (kg): the most a shot moves it, m/s (the mount's recoil rating).</summary>
    public const float KickPerKg = 0.8f;

    /// <summary>The biggest merge the gunsmith makes in phase 1 (the game merges to 6x6: giant bodies come later).</summary>
    public const int MaxMerge = 4;

    /// <summary>The share of a vehicle's draws the gunsmith's guns take: a fighting role's, any other role's, a vehicle with no role's.</summary>
    public const double CombatShare = 0.5, OtherShare = 0.25, PlainShare = 0.35;

    public enum Kind
    {
        HeavyCannon,
        RapidHeavy,
        MissileBattery,
    }

    /// <summary>One design: its build at N = 1, a name, its class's least tier and job tags. A missile battery's build is its spotting gun, and its sizes are
    /// <see cref="Racks"/> (size N the Nth rack, every station holding <see cref="Missile"/>) in place of N x N merges.</summary>
    private sealed record Design(Kind Kind, string Name, string Build, int Tier, string[] Tags, bool Shell, string[]? Racks = null, string? Missile = null);

    /// <summary>A battery's missile: a radar seeker (the unanchored body nearest its axis as it leaves, moving or not: a parked truck too), impact fuse, HE, fins, a solid motor.</summary>
    private const string RadarMissile = "Ordnance:M25:Radar Seeker+Impact Fuse+Warhead HE+Tail Fins+Solid Motor";

    private static readonly Design[] Designs =
    [
        new(Kind.HeavyCannon, "Field Gun", "Field Gun Receiver{barrel:Long Gun Tube,feed:Ready Rack,ammo:High Explosive Shell,action:Sliding Breech}", 1, ["heavy", "anti-armour"], true),
        new(Kind.HeavyCannon, "Howitzer", "Howitzer Receiver{barrel:Howitzer Tube,feed:Shell Hoist,ammo:High Explosive Shell,action:Interrupted Breech}", 2, ["heavy", "anti-armour"], true),
        new(Kind.RapidHeavy, "Autocannon", "Hammerfall Autocannon Receiver{barrel:Cannon Sleeve,feed:Heavy Box,action:Autocannon Drive}", 1, ["heavy", "anti-armour", "aa"], false),
        new(Kind.RapidHeavy, "Rotary Cannon", "Hammerfall Autocannon Receiver{barrel:Ladder Splitter{barrel_1:Stub Barrel,barrel_2:Stub Barrel,barrel_3:Stub Barrel,barrel_4:Stub Barrel,barrel_5:Stub Barrel,barrel_6:Stub Barrel},feed:Heavy Box,action:Autocannon Drive}", 3, ["heavy", "suppress", "aa"], false),
        new(Kind.RapidHeavy, "Flak Block", "Thumper Chain Gun Receiver{barrel:Cross Splitter{barrel_1:Standard Barrel,barrel_2:Standard Barrel,barrel_3:Standard Barrel,barrel_4:Standard Barrel},feed:Belt Box,action:Chain Drive Motor}", 2, ["heavy", "aa", "suppress"], false),
        new(Kind.RapidHeavy, "Chain Cannon", "Thumper Chain Gun Receiver{barrel:Big Iron Barrel,feed:Belt Box,action:Chain Drive Motor}", 1, ["heavy", "suppress"], false),
        new(Kind.RapidHeavy, "Bunker Buster", "Bunker Buster Receiver{barrel:Cannon Sleeve,feed:Heavy Box,action:Autocannon Drive}", 3, ["heavy", "anti-armour"], false),
        new(Kind.MissileBattery, "Missile Battery", "Thumper Chain Gun Receiver{barrel:Big Iron Barrel,feed:Belt Box,action:Chain Drive Motor}", 1, ["heavy", "anti-armour", "aa"], false,
            ["Launch Rack"], RadarMissile), // (the 1x2, 2x2 and 3x2 grids on a half-metre stand-off build and fire, but in the bench their missiles met the ground: VEHICLE-WEAPONS.md, next steps)
    ];

    /// <summary>A battery's name by its size (ASCII: players read it).</summary>
    private static readonly string[] BatteryNames = ["", "Missile Launcher", "Twin Missile Launcher", "Missile Battery", "Heavy Missile Battery"];

    /// <summary>The word for a merge's size in a gun's name (ASCII: players read it).</summary>
    private static readonly string[] Sizes = ["", "", "Heavy", "Siege", "Super-Heavy", "Colossal", "Titan"];

    /// <summary>The guns the gunsmith offers a vehicle of <paramref name="mass"/> kg armed at <paramref name="tier"/>: per class, the designs its tier reaches, each at the merge
    /// the ceiling and tier give (none where even a single receiver is over the ceiling).</summary>
    public static List<WeaponOption> Offer(float mass, int tier)
    {
        var list = new List<WeaponOption>();
        foreach (var d in Designs)
        {
            if (d.Tier > Math.Max(tier, 1) + 1)
                continue; // (a design two tiers past the vehicle's isn't offered at all; one past it is, at a tenth: VehicleArmoury.TierWeight)
            var ceiling = Ceiling(d, mass);
            if (ceiling < 1)
                continue;
            var n = Math.Clamp(ceiling - (tier <= 1 ? 2 : tier == 2 ? 1 : 0), 1, ceiling);
            while (n >= 1 && !Fits(d, n))
                n--; // (a size the game's rules won't build: the next smaller)
            if (n >= 1)
                list.Add(Make(d, n, tier));
        }
        return list;
    }

    /// <summary>The biggest merge of <paramref name="d"/> a vehicle of <paramref name="mass"/> kg is rated for (0: not even one receiver).</summary>
    private static int Ceiling(Design d, float mass)
    {
        var best = 0;
        for (var n = 1; n <= MaxSize(d); n++)
            if (Rating(d, n) is var (m, k) && m <= GunMassShare * mass && k <= KickPerKg * mass)
                best = n;
        return best;
    }

    private static int MaxSize(Design d) => d.Racks?.Length ?? MaxMerge;

    /// <summary>A design's mass (kg) and recoil impulse (N s) at size <paramref name="n"/>: a gun's go with N squared; a battery's mass adds its rack and a missile a station.</summary>
    private static (float Mass, float Kick) Rating(Design d, int n)
    {
        var (m1, k1) = Rating(d);
        if (d.Racks == null)
            return (m1 * n * n, k1 * n * n);
        var rack = ConstructParts.Find(d.Racks[n - 1]);
        var missile = ConstructParts.Find(d.Missile!);
        return (m1 + (rack?.Mass ?? 0) + (rack?.Rack.Stations ?? 0) * (missile?.Mass ?? 0), k1);
    }

    private static readonly Dictionary<string, (float Mass, float Kick)> _ratings = new(StringComparer.Ordinal);

    /// <summary>A design's mass (kg, its construct parts') and recoil impulse (N s) at N = 1; both go with N squared (ReceiverMerge.json, "partMass" and "recoil").</summary>
    private static (float Mass, float Kick) Rating(Design d)
    {
        lock (_ratings)
        {
            if (_ratings.TryGetValue(d.Build, out var r))
                return r;
            var mass = 0f;
            var receiver = "";
            void Walk(string text)
            {
                var (part, children) = WeaponOption.Parse(text);
                if (receiver.Length == 0)
                    receiver = part;
                if (!part.Equals("High Explosive Shell", StringComparison.Ordinal)) // (ammo isn't fitted on a mounted gun: what feeds it chooses the rounds)
                    mass += Def(part)?.Mass ?? 0f;
                foreach (var c in children.Values)
                    Walk(c);
            }
            Walk(d.Build);
            var rules = ConstructWeaponParts.Current;
            var kick = d.Shell ? rules.ShellKick : Def(receiver)?.GunKick is > 0 and var k ? k : rules.ReceiverKick;
            return _ratings[d.Build] = (mass, kick);
        }

        static ConstructPartDef? Def(string builderName) =>
            ConstructParts.All.FirstOrDefault(p => p.GunPartName == builderName || p.GunReceiver == builderName);
    }

    private static readonly Dictionary<(string, int), bool> _fits = new();

    /// <summary>Does <paramref name="d"/> at merge <paramref name="n"/> build as one gun that fires on a test turret through the game's rules (as <c>--check-armoury</c> fits a recipe)? Once a process.</summary>
    private static bool Fits(Design d, int n)
    {
        lock (_fits)
        {
            if (_fits.TryGetValue((d.Name, n), out var ok))
                return ok;
            var w = Make(d, n, 4);
            string? why = null;
            try
            {
                var v = new Vehicle("Gunsmith Stand", Styles.Militia, 1);
                var root = v.Root("Plate Steel 1 m", new System.Numerics.Vector3(0, 2f, 0), Orient.Facing(System.Numerics.Vector3.UnitY));
                v.Turret(root, w);
                var c = v.Live;
                ok = false;
                for (var g = 0; g < c.GunCount; g++)
                    if (c.Guns[g].Part == v.Gun!.Index && c.Guns[g].Def != null && c.Guns[g].Problem.Length == 0 && (n == 1 || d.Racks != null || c.Guns[g].MergeN == n))
                        ok = true;
                if (ok && d.Racks != null)
                {
                    // (a battery: its rack holds a missile on every station, as the game counts what it holds)
                    c.Rebuild();
                    var held = -1;
                    for (var k = 0; k < c.RackCount; k++)
                        held = Math.Max(held, c.Racks[k].Held);
                    ok = ConstructParts.Find(d.Racks[n - 1]) is { } rd && held == rd.Rack.Stations;
                    if (!ok)
                        why = $"its rack holds {held} missiles";
                }
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                ok = false;
                why = ex.Message;
            }
            if (!ok)
                Console.WriteLine($"[vehicles] gunsmith: {d.Name} won't build as {(d.Racks == null ? $"a {n}x{n}" : $"its {d.Racks[n - 1]}")}{(why == null ? "" : ": " + why)}: left out");
            return _fits[(d.Name, n)] = ok;
        }
    }

    /// <summary>A missile battery of size <paramref name="n"/> (1 to 4) on the gunsmith's test stand, as blueprint text (<c>--bench-vehicle-guns --battery N</c>).</summary>
    public static string BatteryStand(int n)
    {
        var d = Designs.First(x => x.Kind == Kind.MissileBattery);
        n = Math.Clamp(n, 1, d.Racks!.Length);
        var v = new Vehicle("Missile Stand", Styles.Militia, n);
        var root = v.Root("Plate Steel 1 m", new System.Numerics.Vector3(0, 2f, 0), Orient.Facing(System.Numerics.Vector3.UnitY));
        v.Turret(root, Make(d, n, 4));
        return v.Text();
    }

    private static WeaponOption Make(Design d, int n, int tier)
    {
        if (d.Racks != null)
            return new WeaponOption(BatteryNames[Math.Min(n, BatteryNames.Length - 1)], d.Build)
            {
                Mount = "rack", Rack = d.Racks[n - 1], Ordnance = d.Missile, Recipe = $"gunsmith:{d.Kind}:{d.Name}", Class = n >= 2 ? GunClass.Heavy : GunClass.Medium,
                Tier = Math.Min(d.Tier, Math.Max(1, tier)), Tags = n >= 3 && !d.Tags.Contains("boss") ? [.. d.Tags, "boss"] : d.Tags,
            };
        var name = n > 1 ? $"{Sizes[Math.Min(n, Sizes.Length - 1)]} {d.Name}" : d.Name;
        // (a shell gun's crew stand behind a shield; a rapid gun is a pintle's: a shield on every turret filled a bus's side box past the Silhouette rule)
        var cls = d.Kind == Kind.RapidHeavy && n == 1 && d.Tier <= 1 ? GunClass.Medium : GunClass.Heavy; // (a single autocannon or chain gun is a crew's gun; merged, or a shell gun, a cannon)
        var tags = n >= 3 && !d.Tags.Contains("boss") ? [.. d.Tags, "boss"] : d.Tags;
        return new WeaponOption(name, d.Build) { Merge = n, Mount = d.Shell ? "shield" : "pintle", Recipe = $"gunsmith:{d.Kind}:{d.Name}", Class = cls, Tier = Math.Min(d.Tier, Math.Max(1, tier)), Tags = tags };
    }

    /// <summary>
    /// <paramref name="own"/> (the style's list) with the gunsmith's guns for a vehicle of <paramref name="mass"/> kg at <paramref name="tier"/>, each repeated so together they take
    /// about <paramref name="share"/> of the draws (the list's weights are counts, as a style's are).
    /// </summary>
    public static WeaponOption[] Arm(IReadOnlyList<WeaponOption> own, float mass, int tier, double share)
    {
        if (!On || own.Count == 0)
            return own as WeaponOption[] ?? own.ToArray();
        var made = Offer(mass, tier);
        if (made.Count == 0)
            return own as WeaponOption[] ?? own.ToArray();
        // Each class takes an equal part of the share, split among its designs: two shell guns weigh as much as five rapid ones.
        var total = share / (1 - share) * own.Count;
        var classes = made.Select(w => KindOf(w)).Distinct().Count();
        var list = new List<WeaponOption>(own);
        foreach (var w in made)
        {
            var kind = KindOf(w);
            var copies = Math.Max(1, (int)Math.Round(total / classes / made.Count(x => KindOf(x) == kind)));
            for (var k = 0; k < copies; k++)
                list.Add(w);
        }
        return list.ToArray();
    }

    private static string KindOf(WeaponOption w) => w.Recipe.Split(':') is { Length: >= 2 } p ? p[1] : "";

    /// <summary><c>--gunsmith-report</c>: what the gunsmith offers vehicles of a range of masses at each tier, with each gun's estimated mass and recoil.</summary>
    public static int Report()
    {
        Console.WriteLine($"gunsmith: mass share {GunMassShare}, recoil {KickPerKg} m/s a shot at most, merges to {MaxMerge}x{MaxMerge}");
        foreach (var mass in (float[])[800, 1500, 3000, 6000, 12000, 24000])
            for (var tier = 1; tier <= 4; tier++)
            {
                var offer = Offer(mass, tier);
                Console.WriteLine($"  {mass,6:0} kg tier {tier}: {(offer.Count == 0 ? "nothing" : string.Join("; ", offer.Select(w => { var d = Designs.First(d => w.Recipe.EndsWith(":" + d.Name, StringComparison.Ordinal)); var n = d.Racks == null ? w.Merge : Array.IndexOf(d.Racks, w.Rack) + 1; var (m, k) = Rating(d, n); return d.Racks == null ? $"{w.Name} {w.Merge}x{w.Merge} ({m:0} kg, {k:0} N s)" : $"{w.Name} ({w.Rack}, {m:0} kg)"; })))}");
            }
        return 0;
    }
}

public sealed partial class Vehicle
{
    /// <summary>The guns this vehicle draws from: its style's, plus the gunsmith's made for its mass now and its role's tier (<see cref="VehicleGunsmith"/>).</summary>
    public WeaponOption[] Arsenal
    {
        get
        {
            if (!VehicleGunsmith.On)
                return Style.Weapons;
            var mass = 0f;
            for (var i = 0; i < C.PartCount; i++)
                mass += C.PartMass(i);
            var share = Role is { } r ? r.Role.Combat ? VehicleGunsmith.CombatShare : VehicleGunsmith.OtherShare : VehicleGunsmith.PlainShare;
            return VehicleGunsmith.Arm(Style.Weapons, mass, Role?.Tier ?? VehicleArmoury.PlainTier, share);
        }
    }
}
