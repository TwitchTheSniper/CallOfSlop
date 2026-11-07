using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>What a piece of a vehicle's cargo is for (docs/vehiclekit/VEHICLEKIT.md, "Cargo and spares").</summary>
public enum StowKind : byte
{
    Ammo, // an Ammo Box or Ammo Crate: rounds for its guns (any type fits any gun; the magazine is what a gun is choosy about)
    Magazine, // a spare magazine of the kind its own guns take
    GunPart, // a spare gun's parts (receiver, barrel, action, magazine), or a spare barrel
    Wheel, // a spare wheel of its own size
    Spring, // a spare suspension of its own kind
    Plate, // plates of its own materials and sizes, for patching
    Frame, // a few of its own frame beams
    Fuel, // fuel for its engine, or a flame gun's
    Battery, // a spare battery for an electric drive
}

/// <summary>One line of a vehicle's manifest: what, how many, where (a container's slot, or loose in a frozen Cargo Volume on a deck) and what it serves.</summary>
public sealed record Stowed(StowKind Kind, string Part, int Count, string Where, string For, bool Loose);

/// <summary>How much of each kind a role carries at tier 2, a gun's share per gun; fractions are a chance of one more.</summary>
public sealed record SpareProfile(double Mags, double Boxes, double Crates, double Wheels, double Springs, double Plates, double Beams, double Fuel, double Guns, double Barrels);

/// <summary>
/// The tables the cargo pass reads (additive: Styles.cs and the generators are untouched). <see cref="ByRole"/>: what each role carries; <see cref="ByStyle"/>: how much more a style
/// carries than the table, of ammo and of spares (the Bandits scavenge everything, the IT Department travels light). A style not named takes 1 and 1.
/// </summary>
public static class VehicleCargoTables
{
    public static readonly Dictionary<string, SpareProfile> ByRole = new()
    {
        //                         mags boxes crates wheels springs plates beams fuel guns barrels
        ["scout"] = new(1, 0.5, 0, 0.5, 0, 0, 0.3, 0.5, 0, 0.3),
        ["raider"] = new(1.5, 1, 0.3, 1, 0.3, 1, 0.5, 1, 0.3, 0.5),
        ["gun platform"] = new(2, 2, 1, 1, 0.5, 2, 1, 1, 1, 1),
        ["carrier"] = new(1.5, 2, 0.3, 1, 0.5, 3, 1, 1, 0.5, 0.5),
        ["tanker"] = new(1, 1, 0.5, 2, 1, 2, 2, 2, 0.3, 0.5),
        ["recovery"] = new(1, 1, 0, 2, 2, 4, 4, 1, 0.3, 1),
        ["command"] = new(2, 2, 0.5, 1, 0.5, 2, 1, 1, 1, 1),
        ["boss"] = new(3, 3, 1, 2, 1, 3, 2, 2, 2, 1),
    };

    public static readonly Dictionary<string, (double Ammo, double Spares)> ByStyle = new()
    {
        ["Bandit"] = (1, 1.3),
        ["IT"] = (0.8, 0.8),
        ["Scam"] = (0.8, 0.9),
        ["Highland"] = (1.1, 1),
        ["British"] = (1, 1.1),
    };

    /// <summary>A tier's factor on every amount: 0.7 at tier 1, 1 at tier 2, then 0.3 a tier more.</summary>
    public static double TierFactor(int tier) => tier <= 1 ? 0.7 : 1 + 0.3 * (tier - 2);

    /// <summary>Loose pieces a vehicle carries at most, and their mass at most as a share of the vehicle's (it must still drive, and meet its role's speed).</summary>
    public const int MaxLoose = 18;

    public const float MaxMassShare = 0.12f;

    /// <summary>The heaviest piece a roof rack takes (kg): a rack is high, and weight there tips a vehicle.</summary>
    public const float RackMaxMass = 16f;
}

/// <summary>
/// Kit 4's cargo pass (roadmap phase 9, "Cargo and spares"): what a faction vehicle carries for itself, stowed the way its crew would. Ammo for its own guns (magazines of
/// the kinds they take, Ammo Boxes, a loose Ammo Crate on a gun deck), spare parts chosen from its own build (its wheels, springs, plates and beams, fuel for its engine, a
/// battery for an electric drive) and spare guns by their parts. Items go into free slots of its storage, as a player would set them there (the loot rolls into what is
/// left); physics-only pieces, and what no slot takes, ride loose in a frozen Cargo Volume on an open deck, laid out in rows, plates and wheels stacked, clear of what is
/// built and of the guns' sweep. Everything is a real part: shot, spilled, stolen, salvaged. A stored box doesn't cook off; a loose one does.
/// </summary>
public sealed partial class Vehicle
{
    /// <summary>Off: no cargo pass (<c>--no-cargo</c>), to measure what it changes.</summary>
    public static bool CargoOn = true;

    /// <summary>The manifest (empty without a role).</summary>
    public List<Stowed> Stowage { get; } = new();

    /// <summary>Loose pieces the cargo pass added, and their mass (the role's loot need counts neither; its speed counts the mass).</summary>
    public int SparePieces { get; private set; }

    public float SpareMass { get; private set; }

    /// <summary>The kit line's note: "cargo 4 ammo (1 loose), 3 magazine, 2 wheel, ...; 118 kg loose".</summary>
    public string CargoNote
    {
        get
        {
            if (Stowage.Count == 0)
                return "";
            var parts = Stowage.GroupBy(s => s.Kind).OrderBy(g => g.Key).Select(g =>
            {
                var n = g.Sum(s => s.Count);
                var loose = g.Where(s => s.Loose).Sum(s => s.Count);
                return $"{n} {KindWord(g.Key)}{(loose > 0 && loose < n ? $" ({loose} loose)" : loose == n ? " loose" : "")}";
            });
            return $"cargo {string.Join(", ", parts)}; {SpareMass:0} kg loose";
        }
    }

    /// <summary>What a part is as cargo (null: loot or anything else): rounds, a magazine, a gun's part, a wheel, a spring, a plate, a beam, fuel, a battery.</summary>
    public static StowKind? StowKindOf(ConstructPartDef d) =>
        d.Role == PartRole.AmmoBox ? StowKind.Ammo
        : ConstructAmmo.IsMagazine(d) ? StowKind.Magazine
        : d.Role is PartRole.Gun or PartRole.GunPart ? StowKind.GunPart
        : d.Role == PartRole.Wheel ? StowKind.Wheel
        : d.Role == PartRole.Suspension ? StowKind.Spring
        : d.Role == PartRole.Panel ? StowKind.Plate
        : d.Role == PartRole.Frame ? StowKind.Frame
        : d.Role == PartRole.FuelTank ? StowKind.Fuel
        : d.Role == PartRole.Battery ? StowKind.Battery
        : null;

    public static string KindWord(StowKind k) => k switch
    {
        StowKind.GunPart => "gun part",
        _ => k.ToString().ToLowerInvariant(),
    };

    private sealed record Want(StowKind Kind, string Part, int Count, string For, Vector3 Near);

    /// <summary>The cargo pass: after the role pass (<see cref="RoleKit"/>), on forks of the kit layer ("kit cargo"...). Nothing without a role.</summary>
    internal void StowCargo()
    {
        if (Role is not { } ask || !CargoOn)
            return;
        var profile = VehicleCargoTables.ByRole.GetValueOrDefault(ask.Role.Name);
        if (profile == null)
            return;
        var d = Dice.Fork("kit cargo");
        var (ammoK, sparesK) = VehicleCargoTables.ByStyle.GetValueOrDefault(Style.Name, (1, 1));
        var tierK = VehicleCargoTables.TierFactor(ask.Tier);
        int Roll(double x)
        {
            x = Math.Max(0, x);
            var whole = (int)Math.Floor(x);
            return whole + (d.Chance(x - whole) ? 1 : 0);
        }
        var wants = new List<Want>();
        var back = new Vector3(0, FloorY, TailZ); // (spares stand towards the back, where a crew reaches them; ammo near its gun)

        // Ammo for its own guns: magazines of the kinds they take, boxes, and on a gun deck a crate.
        var magsOf = new Dictionary<string, (int Guns, Vector3 Near)>();
        var ammoNear = Turrets.Count > 0 ? CentreOf(Turrets[0].Gun) : back;
        var flame = 0;
        foreach (var (gun, _, w) in Turrets)
        {
            var (_, children) = WeaponOption.Parse(w.Build);
            var at = CentreOf(gun);
            if (children.TryGetValue("feed", out var feed) && TryGunDef(WeaponOption.Parse(feed).Part) is { } mag)
                magsOf[mag.Name] = (magsOf.GetValueOrDefault(mag.Name).Guns + 1, at);
            if (w.Box == null)
                flame++; // (a flame gun drinks the construct's fuel: its ammo is fuel)
        }
        foreach (var (mag, (taking, near)) in magsOf.OrderBy(m => m.Key, StringComparer.Ordinal))
            if (Roll(profile.Mags * taking * ammoK * tierK) is > 0 and var n)
                wants.Add(new Want(StowKind.Magazine, mag, n, $"its {GunsTaking(mag)}", near));
        var boxed = Turrets.Count - flame;
        if (boxed > 0)
        {
            if (Roll(profile.Boxes * boxed * ammoK * tierK) is > 0 and var n)
                wants.Add(new Want(StowKind.Ammo, "Ammo Box", n, "its guns", ammoNear));
            if (Roll(profile.Crates * boxed * ammoK * tierK) is > 0 and var c)
                wants.Add(new Want(StowKind.Ammo, "Ammo Crate", Math.Min(c, 3), "its guns", ammoNear));
        }
        if (flame > 0 && Roll(profile.Boxes * flame * ammoK * tierK) is > 0 and var cans)
            wants.Add(new Want(StowKind.Fuel, "Small Fuel Tank", cans, "its flame gun", ammoNear));

        // Spare guns by their parts, and spare barrels: the guns it carries, the commonest first.
        var guns = Turrets.Select(t => t.Weapon).GroupBy(w => w.Build).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.First()).ToList();
        if (guns.Count > 0)
        {
            for (int k = 0, n = Roll(profile.Guns * tierK); k < n; k++)
            {
                var w = guns[k % guns.Count];
                foreach (var part in GunParts(w.Build))
                    wants.Add(new Want(StowKind.GunPart, part, 1, $"a spare {w.Name}", ammoNear));
            }
            if (Roll(profile.Barrels * tierK) is > 0 and var nb && GunParts(guns[0].Build, barrelOnly: true).FirstOrDefault() is { } barrel)
                wants.Add(new Want(StowKind.GunPart, barrel, nb, $"its {guns[0].Name}", ammoNear));
        }

        // Spares from its own build: what it has most of, and loses first.
        var wheels = Wheels.GroupBy(w => w.Name).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).ToList();
        if (wheels.Count > 0 && Roll(profile.Wheels * sparesK * tierK) is > 0 and var nw)
        {
            wants.Add(new Want(StowKind.Wheel, wheels[0], nw, "its wheels", back));
            if (wheels.Count > 1 && nw > 1)
                wants[^1] = wants[^1] with { Count = nw - nw / 2 };
            if (wheels.Count > 1 && nw > 1)
                wants.Add(new Want(StowKind.Wheel, wheels[1], nw / 2, "its wheels", back));
        }
        if (Commonest(p => p.Def.Role == PartRole.Suspension) is { } spring && Roll(profile.Springs * sparesK * tierK) is > 0 and var ns)
            wants.Add(new Want(StowKind.Spring, spring, ns, "its suspension", back));
        var plates = Ranked(p => p.Def.Role == PartRole.Panel && IsPatchPlate(p.Name) && !p.Name.EndsWith(" 0.25 m", StringComparison.Ordinal)); // (a patch big enough to cover a hole)
        if (plates.Count == 0)
            plates = Ranked(p => p.Def.Role == PartRole.Panel && IsPatchPlate(p.Name));
        if (plates.Count > 0 && Roll(profile.Plates * sparesK * tierK) is > 0 and var np)
        {
            wants.Add(new Want(StowKind.Plate, plates[0], np - np / 3, "patching its plates", back));
            if (np / 3 > 0)
                wants.Add(new Want(StowKind.Plate, plates.Count > 1 ? plates[1] : plates[0], np / 3, "patching its plates", back));
        }
        if (Commonest(p => p.Def.Role == PartRole.Frame && p.Name.StartsWith(Beam + " ", StringComparison.Ordinal) && p.Def.BoundsMax.Z - p.Def.BoundsMin.Z <= 1.05f) is { } beam
            && Roll(profile.Beams * sparesK * tierK) is > 0 and var nf)
            wants.Add(new Want(StowKind.Frame, beam, nf, "its frame", back));
        if (Electric)
        {
            if (Roll(profile.Fuel * 0.5 * tierK) > 0)
                wants.Add(new Want(StowKind.Battery, "Battery Pack", 1, "its electric drive", back));
        }
        else if (Engine != null && Roll(profile.Fuel * sparesK * tierK) is > 0 and var nfuel)
        {
            var drums = Tanks.Any(t => t.Name == "Fuel Drum") && nfuel > 1;
            wants.Add(new Want(StowKind.Fuel, "Small Fuel Tank", drums ? nfuel - 1 : nfuel, "its engine", back));
            if (drums)
                wants.Add(new Want(StowKind.Fuel, "Fuel Drum", 1, "its engine", back));
        }
        VehicleGenJob.Yield();
        Stow(wants.OrderBy(w => Priority(w)).ToList(), d.Fork("kit cargo layout")); // (what matters most is stowed first: rounds, then the rest as a crew would pack)
    }

    /// <summary>The order things are stowed in when room runs short: rounds, magazines, fuel, a spare gun, wheels, springs, plates, beams, a battery.</summary>
    private static int Priority(Want w) => w.Kind switch
    {
        StowKind.Ammo => w.Part == "Ammo Box" ? 0 : 2,
        StowKind.Magazine => 1,
        StowKind.Fuel => 3,
        StowKind.Wheel => 4,
        StowKind.GunPart => 5,
        StowKind.Spring => 6,
        StowKind.Plate => 7,
        StowKind.Frame => 8,
        _ => 9,
    };

    /// <summary>What the guns that take magazine <paramref name="mag"/> are called ("Scrap MG and Whaler").</summary>
    private string GunsTaking(string mag)
    {
        var names = Turrets.Where(t => WeaponOption.Parse(t.Weapon.Build).Children.TryGetValue("feed", out var f) && TryGunDef(WeaponOption.Parse(f).Part)?.Name == mag).Select(t => t.Weapon.Name).Distinct().ToList();
        return string.Join(" and ", names);
    }

    /// <summary>A gun's construct parts from its build text: receiver, barrel, muzzle, action, magazine (not its ammo part, which does nothing on a mounted gun).</summary>
    private static List<string> GunParts(string build, bool barrelOnly = false)
    {
        var (receiver, children) = WeaponOption.Parse(build);
        var list = new List<string>();
        if (!barrelOnly && TryGunDef(receiver) is { } r)
            list.Add(r.Name);
        foreach (var (socket, value) in children.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (socket.Equals("ammo", StringComparison.OrdinalIgnoreCase) || barrelOnly && !socket.Equals("barrel", StringComparison.OrdinalIgnoreCase))
                continue;
            var (part, sub) = WeaponOption.Parse(value);
            if (TryGunDef(part) is { } p)
                list.Add(p.Name);
            if (!barrelOnly && sub.TryGetValue("muzzle", out var muzzle) && TryGunDef(muzzle) is { } m)
                list.Add(m.Name);
        }
        return list;
    }

    private static ConstructPartDef? TryGunDef(string builderName) =>
        ConstructParts.All.FirstOrDefault(x => x.GunPartName == builderName || x.GunReceiver == builderName);

    /// <summary>A plate a crew patches with: a plain plate of a material and a size, 1 m or under.</summary>
    private static bool IsPatchPlate(string name) =>
        (name.StartsWith("Plate Steel ", StringComparison.Ordinal) || name.StartsWith("Sheet Steel ", StringComparison.Ordinal) || name.StartsWith("Tread Plate ", StringComparison.Ordinal)
         || name.StartsWith("Wood Planks ", StringComparison.Ordinal) || name.StartsWith("Armour Plate ", StringComparison.Ordinal))
        && (name.EndsWith(" 0.5 m", StringComparison.Ordinal) || name.EndsWith(" 1 m", StringComparison.Ordinal) || name.EndsWith(" 0.25 m", StringComparison.Ordinal));

    private List<string> Ranked(Func<VPart, bool> which) =>
        Parts.Where(which).GroupBy(p => p.Name).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).ToList();

    private string? Commonest(Func<VPart, bool> which) => Ranked(which).FirstOrDefault();

    // ---- Stowing ------------------------------------------------------------------------------------------------------------------

    /// <summary>A slot's state as the pass fills it: what kind it holds and how many (its container's loot rolls into what is left).</summary>
    private sealed class SlotUse
    {
        public VPart Container = null!;
        public int Slot;
        public ConstructPartDef? Kind;
        public int Count, Room;
        public bool Loot;
    }

    private void Stow(List<Want> wants, Dice d)
    {
        // The slots: every storage part's, but a loot container keeps at least half its slots for its loot.
        var slots = new List<SlotUse>();
        var lootSteps = LootSteps.ToHashSet();
        foreach (var p in Parts)
        {
            var cd = p.Def;
            if (cd.Role != PartRole.Storage || cd.Slots.Length == 0 || cd.IsAssembler || cd.IsTerminal || cd.IsPress || cd.IsVolumeStore || p.Seg != 0 && !C.Rigid(p.Seg))
                continue;
            for (var s = 0; s < cd.Slots.Length; s++)
                slots.Add(new SlotUse { Container = p, Slot = s, Loot = lootSteps.Contains(p.Step) });
        }
        var spareSlots = new Dictionary<int, int>(); // container step: slots the pass may take
        foreach (var g in slots.GroupBy(s => s.Container.Step))
            spareSlots[g.Key] = g.First().Loot ? g.Count() / 2 : g.Count();
        var loose = new List<(Want W, int Count)>();
        foreach (var w in wants)
        {
            var def = Def(w.Part);
            var left = w.Count;
            // A loose Ammo Crate is the point: it cooks off where it stands. Everything else goes in a slot when one takes it.
            if (!def.PhysicsOnly && !(w.Kind == StowKind.Ammo && w.Part == "Ammo Crate"))
                foreach (var s in slots.OrderBy(s => Vector3.DistanceSquared(CentreOf(s.Container), w.Near)).ThenBy(s => s.Container.Step).ThenBy(s => s.Slot))
                {
                    if (left == 0)
                        break;
                    if (s.Kind != null && s.Kind != def || s.Kind != null && s.Count >= s.Room)
                        continue;
                    if (s.Kind == null)
                    {
                        var room = spareSlots[s.Container.Step] <= 0 ? 0 : SlotRoom(s.Container.Def, s.Slot, def);
                        if (room <= 0)
                            continue;
                        spareSlots[s.Container.Step]--;
                        (s.Kind, s.Room) = (def, room);
                    }
                    var n = Math.Min(left, s.Room - s.Count);
                    s.Count += n;
                    left -= n;
                    Store(s.Container, s.Slot, w.Part, n);
                    Stowage.Add(new Stowed(w.Kind, w.Part, n, $"{s.Container.Name} slot {s.Slot}", w.For, false));
                }
            if (left > 0)
                loose.Add((w, left));
        }
        if (loose.Count > 0)
            Lay(loose, d);
    }

    /// <summary>How many of <paramref name="d"/> slot <paramref name="slot"/> of container <paramref name="cd"/> takes (0: none), by the game's own slot rule.</summary>
    private static int SlotRoom(ConstructPartDef cd, int slot, ConstructPartDef d)
    {
        if (d.PhysicsOnly || cd.StoreMaxMass > 0 && d.Mass > cd.StoreMaxMass)
            return 0;
        ref readonly var s = ref cd.Slots[slot];
        if (!ConstructStorage.Fits(s, d, out var rot))
            return 0;
        return s.Rack || s.Stack > 1 ? ConstructStorage.StackOf(s, d, rot) : 1;
    }

    // ---- Laying it out on a deck -------------------------------------------------------------------------------------------------

    private const float CargoGap = 0.03f, Grid = 0.125f;

    /// <summary>An open deck: panels whose tops stand level together (vehicle coordinates), the rects they cover and how high the pass may stack on it.</summary>
    private sealed class DeckSpot
    {
        public string Tag = "";
        public float Top;
        public List<(float X0, float X1, float Z0, float Z1)> Rects = new();
        public List<VPart> Panels = new();
        public float X0 = float.MaxValue, X1 = float.MinValue, Z0 = float.MaxValue, Z1 = float.MinValue;
        public float MaxMass = float.MaxValue;
        public List<(Vector3 Min, Vector3 Max)> Laid = new();
        public VPart? Volume;
        public bool NoVolume; // (no socket took its control box: nothing goes on it)
        public bool Supports(float x, float z) => Rects.Any(r => x >= r.X0 && x <= r.X1 && z >= r.Z0 && z <= r.Z1);
    }

    /// <summary>The loose pieces on the open decks (bed, deck, porch, then a roof rack for light ones), each deck with a frozen Cargo Volume round what it carries.</summary>
    private void Lay(List<(Want W, int Count)> loose, Dice d)
    {
        var decks = new List<DeckSpot>();
        foreach (var tag in (string[])["bed", "deck", "porch", "rack"])
        {
            if (Panels.GetValueOrDefault(tag) is not { Count: > 0 } panels)
                continue;
            foreach (var p in panels)
            {
                if (p.Seg != 0 && !C.Rigid(p.Seg) || p.Def.Role != PartRole.Panel)
                    continue;
                C.PartBounds(p.Index, out var mn, out var mx);
                var (lo, hi) = (FromRest(mn), FromRest(mx));
                if (hi.X - lo.X < 0.2f || hi.Z - lo.Z < 0.2f || hi.Y - lo.Y > 0.2f)
                    continue; // (a plate lying flat, not an edge)
                var spot = decks.FirstOrDefault(s => s.Tag == tag && MathF.Abs(s.Top - hi.Y) < 0.015f);
                if (spot == null)
                    decks.Add(spot = new DeckSpot { Tag = tag, Top = hi.Y, MaxMass = tag == "rack" ? VehicleCargoTables.RackMaxMass : float.MaxValue });
                spot.Top = MathF.Max(spot.Top, hi.Y);
                spot.Rects.Add((lo.X, hi.X, lo.Z, hi.Z));
                spot.Panels.Add(p);
                (spot.X0, spot.X1, spot.Z0, spot.Z1) = (MathF.Min(spot.X0, lo.X), MathF.Max(spot.X1, hi.X), MathF.Min(spot.Z0, lo.Z), MathF.Max(spot.Z1, hi.Z));
            }
        }
        if (decks.Count == 0)
        {
            KitCargoMisses.Add($"{loose.Sum(l => l.Count)} pieces: no open deck to carry them on");
            return;
        }
        var rings = TurretSweeps();
        _cargoBefore = CargoBoxes.Count;
        var massCap = VehicleCargoTables.MaxMassShare * C.Mass;
        foreach (var (w, count) in loose)
        {
            var def = Def(w.Part);
            var turns = TurnsFor(w.Kind, def);
            var stack = w.Kind switch { StowKind.Plate => 4, StowKind.Frame => 3, StowKind.Wheel => 2, _ => 1 };
            var placed = 0;
            for (var k = 0; k < count; k++)
            {
                if (SparePieces >= VehicleCargoTables.MaxLoose || SpareMass + def.Mass > massCap)
                {
                    KitCargoMisses.Add($"{count - k} {w.Part}: over the vehicle's cargo limit ({VehicleCargoTables.MaxLoose} pieces, {massCap:0} kg)");
                    break;
                }
                VehicleGenJob.Yield(); // (each try reads the deck's parts)
                if (PlaceLoose(decks, def, turns, stack, w.Near, rings, d) is not { } spot)
                {
                    KitCargoMisses.Add($"{count - k} {w.Part}: no room on its decks");
                    break;
                }
                placed++;
                _ = spot;
            }
            if (placed > 0)
                Stowage.Add(new Stowed(w.Kind, w.Part, placed, "loose on the " + string.Join("/", decks.Where(s => s.Laid.Count > 0).Select(s => s.Tag).Distinct()), w.For, true));
        }
        foreach (var spot in decks)
            if (spot.Laid.Count > 0)
                Enclose(spot);
    }

    /// <summary>Why pieces were left behind (the deck was full, or it had none): for the report.</summary>
    public List<string> KitCargoMisses { get; } = new();

    /// <summary>The turns a piece is laid in: flat (its thinnest way up: plates, beams, wheels, a gun's parts), else upright (boxes, tanks, drums), turned about its up either way.</summary>
    private static List<byte> TurnsFor(StowKind kind, ConstructPartDef def)
    {
        var flat = kind is StowKind.Plate or StowKind.Frame or StowKind.Wheel or StowKind.Spring or StowKind.GunPart or StowKind.Magazine;
        var list = new List<byte>();
        var least = float.MaxValue;
        for (byte t = 0; t < Rot24.Count; t++)
        {
            var (lo, hi) = TurnedBounds(def, t);
            least = MathF.Min(least, hi.Y - lo.Y);
        }
        for (byte t = 0; t < Rot24.Count; t++)
        {
            var (lo, hi) = TurnedBounds(def, t);
            var up = Rot24.Apply(t, Vector3.UnitY);
            if (flat ? hi.Y - lo.Y <= least + 0.001f : up.Y > 0.99f)
                list.Add(t);
        }
        return list;
    }

    /// <summary>Each turret's ring: its axis, its top and how far round it what it carries reaches (a piece within that stays under the ring's top).</summary>
    private List<(Vector2 Axis, float Top, float Reach)> TurretSweeps()
    {
        var list = new List<(Vector2, float, float)>();
        foreach (var t in Parts)
        {
            if (t.Name != "Turntable")
                continue;
            C.PartBounds(t.Index, out var mn, out var mx);
            var (lo, hi) = (FromRest(mn), FromRest(mx));
            var axis = new Vector2((lo.X + hi.X) * 0.5f, (lo.Z + hi.Z) * 0.5f);
            var reach = 1.2f;
            for (var i = 0; i < C.PartCount; i++)
            {
                var seg = C.Parts[i].Seg;
                if (seg == 0 || C.Rigid(seg))
                    continue;
                C.PartBounds(i, out var pmn, out var pmx);
                var (plo, phi) = (FromRest(pmn), FromRest(pmx));
                if (plo.Y < hi.Y - 0.05f)
                    continue; // (a wheel, a steering pivot: below the ring)
                var r = new Vector2(MathF.Max(MathF.Abs(plo.X - axis.X), MathF.Abs(phi.X - axis.X)), MathF.Max(MathF.Abs(plo.Z - axis.Y), MathF.Abs(phi.Z - axis.Y))).Length();
                if (r < 4f)
                    reach = MathF.Max(reach, r);
            }
            list.Add((axis, hi.Y, reach + 0.1f));
        }
        return list;
    }

    /// <summary>One piece on the first deck that takes it, nearest <paramref name="near"/>: on a pile of its own kind when it stacks, else on the deck's grid. Null: no room.</summary>
    private DeckSpot? PlaceLoose(List<DeckSpot> decks, ConstructPartDef def, List<byte> turns, int stack, Vector3 near, List<(Vector2 Axis, float Top, float Reach)> rings, Dice d)
    {
        foreach (var spot in decks)
        {
            if (def.Mass > spot.MaxMass || spot.NoVolume)
                continue;
            var parts = NearParts(spot);
            // On a pile of its own kind first: a stack of plates, a pair of wheels.
            if (stack > 1)
                foreach (var pile in _piles.Where(p => p.Def == def && p.Spot == spot && p.Boxes.Count < stack).ToList())
                {
                    var (n, turn) = (pile.Boxes.Count, pile.Turn);
                    var (lo, hi) = TurnedBounds(def, turn);
                    var shift = (n % 2 == 0 ? 0 : 1) * 0.004f; // (every other one a few millimetres off the one below: no side lies flush with the next one's)
                    var at = pile.At + new Vector3(shift, n * (hi.Y - lo.Y + 0.004f), shift);
                    var box = (at + lo, at + hi);
                    if (Fits(spot, box, parts, rings, ignore: pile.Boxes))
                    {
                        Cargo(def.Name, at, turn);
                        spot.Laid.Add(box);
                        pile.Boxes.Add(box);
                        SparePieces++;
                        SpareMass += def.Mass;
                        return spot;
                    }
                }
            var best = Search(spot, def, turns, near, parts, rings);
            if (best == null)
                continue;
            if (spot.Volume == null) // (the deck's first piece: its volume's control box goes on first, and the spot is looked for again round it)
            {
                if (!PlaceVolume(spot))
                    continue;
                parts = NearParts(spot);
                if ((best = Search(spot, def, turns, near, parts, rings)) == null)
                    continue;
            }
            var (at0, turn0, box0) = best.Value;
            Cargo(def.Name, at0, turn0);
            spot.Laid.Add(box0);
            _piles.Add(new Pile { Def = def, Spot = spot, At = at0, Turn = turn0, Boxes = [box0] });
            SparePieces++;
            SpareMass += def.Mass;
            return spot;
        }
        return null;
    }

    /// <summary>The free spot on the deck's grid nearest <paramref name="near"/> that takes the piece in one of its turns: its origin, turn and box. Null: none.</summary>
    private static (Vector3 At, byte Turn, (Vector3 Min, Vector3 Max) Box)? Search(DeckSpot spot, ConstructPartDef def, List<byte> turns, Vector3 near, List<(Vector3 Min, Vector3 Max)> parts,
        List<(Vector2 Axis, float Top, float Reach)> rings)
    {
        var best = (Score: float.MaxValue, At: Vector3.Zero, Turn: (byte)0, Box: (Vector3.Zero, Vector3.Zero));
        foreach (var turn in turns)
        {
            var (lo, hi) = TurnedBounds(def, turn);
            var size = hi - lo;
            if (size.X > spot.X1 - spot.X0 || size.Z > spot.Z1 - spot.Z0)
                continue;
            for (var x = spot.X0 + CargoGap; x + size.X <= spot.X1 - CargoGap + 0.001f; x += Grid)
            {
                VehicleGenJob.Yield();
                for (var z = spot.Z0 + CargoGap; z + size.Z <= spot.Z1 - CargoGap + 0.001f; z += Grid)
                {
                    var min = new Vector3(x, spot.Top + 0.005f, z);
                    var centre = min + size * 0.5f;
                    var score = new Vector2(centre.X - near.X, centre.Z - near.Z).LengthSquared();
                    if (score >= best.Score)
                        continue;
                    var box = (min, min + size);
                    if (!Fits(spot, box, parts, rings))
                        continue;
                    best = (score, min - lo, turn, box);
                }
            }
        }
        return best.Score == float.MaxValue ? null : (best.At, best.Turn, best.Box);
    }

    private sealed class Pile
    {
        public ConstructPartDef Def = null!;
        public DeckSpot Spot = null!;
        public Vector3 At;
        public byte Turn;
        public List<(Vector3 Min, Vector3 Max)> Boxes = new();
    }

    private readonly List<Pile> _piles = new();

    private int _cargoBefore;

    /// <summary>The solid boxes standing over a deck (vehicle coordinates), the deck's own plates left out.</summary>
    private List<(Vector3 Min, Vector3 Max)> NearParts(DeckSpot spot)
    {
        var list = new List<(Vector3, Vector3)>();
        var own = spot.Panels.Select(p => p.Index).ToHashSet();
        for (var i = 0; i < C.PartCount; i++)
        {
            if (own.Contains(i))
                continue;
            C.PartBounds(i, out var mn, out var mx);
            var (lo, hi) = (FromRest(mn), FromRest(mx));
            if (hi.X < spot.X0 - 0.1f || lo.X > spot.X1 + 0.1f || hi.Z < spot.Z0 - 0.1f || lo.Z > spot.Z1 + 0.1f || hi.Y < spot.Top - 0.01f || lo.Y > spot.Top + 2.5f)
                continue;
            for (var k = 0; k < C.DefOf(i).SolidBoxes; k++)
            {
                C.BoxOf(i, k, out var oc, out var oh);
                var (bc, bh) = (FromRest(oc), oh);
                list.Add((bc - bh, bc + bh));
            }
            for (var k = C.DefOf(i).SolidBoxes; k < C.DefOf(i).Boxes.Length; k++) // (what is drawn too: a piece mustn't stand in a detail, nor share its face)
            {
                C.BoxOf(i, k, out var oc, out var oh);
                var (bc, bh) = (FromRest(oc), oh);
                list.Add((bc - bh, bc + bh));
            }
        }
        for (var k = 0; k < _cargoBefore && k < CargoBoxes.Count; k++)
            list.Add(CargoBoxes[k]); // (cargo the shape laid before this pass: crates on a technical's bed; this pass's own are the deck's Laid)
        return list;
    }

    /// <summary>Does a piece's box stand there: on the deck's plates at every corner, clear of every part and piece by a margin, and under any turret's ring within its reach?</summary>
    private static bool Fits(DeckSpot spot, (Vector3 Min, Vector3 Max) box, List<(Vector3 Min, Vector3 Max)> parts, List<(Vector2 Axis, float Top, float Reach)> rings, List<(Vector3 Min, Vector3 Max)>? ignore = null)
    {
        var (mn, mx) = box;
        const float In = 0.02f;
        if (!spot.Supports(mn.X + In, mn.Z + In) || !spot.Supports(mx.X - In, mn.Z + In) || !spot.Supports(mn.X + In, mx.Z - In) || !spot.Supports(mx.X - In, mx.Z - In)
            || !spot.Supports((mn.X + mx.X) * 0.5f, (mn.Z + mx.Z) * 0.5f))
            return false;
        foreach (var (axis, top, reach) in rings)
        {
            var dx = MathF.Max(MathF.Max(mn.X - axis.X, axis.X - mx.X), 0);
            var dz = MathF.Max(MathF.Max(mn.Z - axis.Y, axis.Y - mx.Z), 0);
            if (dx * dx + dz * dz < reach * reach && mx.Y > top - 0.03f)
                return false;
        }
        var (gmn, gmx) = (mn - new Vector3(CargoGap * 0.5f), mx + new Vector3(CargoGap * 0.5f));
        foreach (var (pmn, pmx) in parts)
            if (pmn.X < gmx.X && pmx.X > gmn.X && pmn.Y < gmx.Y && pmx.Y > gmn.Y && pmn.Z < gmx.Z && pmx.Z > gmn.Z)
                return false;
        foreach (var (lmn, lmx) in spot.Laid)
        {
            if (ignore != null && ignore.Contains((lmn, lmx)))
                continue;
            if (lmn.X < gmx.X && lmx.X > gmn.X && lmn.Y < gmx.Y && lmx.Y > gmn.Y && lmn.Z < gmx.Z && lmx.Z > gmn.Z)
                return false;
        }
        return true;
    }

    /// <summary>
    /// A deck's Cargo Volume, before anything is laid on it: its control box on a free socket on the side of one of the deck's plates (out of the way of what is laid
    /// and of a player on the deck), else on top at the deck's edge. False (and the deck carries nothing) when no socket takes it.
    /// </summary>
    private bool PlaceVolume(DeckSpot spot)
    {
        var middle = new Vector3((spot.X0 + spot.X1) * 0.5f, spot.Top, (spot.Z0 + spot.Z1) * 0.5f);
        foreach (var dir in (Vector3[])[new(-1, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, 0, -1), Vector3.UnitY])
            foreach (var p in spot.Panels.OrderByDescending(p => Vector3.DistanceSquared(CentreOf(p), middle)).ThenBy(p => p.Step))
                foreach (var socket in FreeSockets(p, dir))
                {
                    VehicleGenJob.Yield();
                    try
                    {
                        var volume = Onto(p, socket, "Cargo Volume", "-y", clear: true);
                        if (volume.Seg != 0 && !C.Rigid(volume.Seg))
                            continue;
                        spot.Volume = volume;
                        return true;
                    }
                    catch (VehicleKitException)
                    {
                        // (something there: the next)
                    }
                }
        spot.NoVolume = true;
        KitCargoMisses.Add($"the {spot.Tag}: nowhere for a Cargo Volume's control box");
        return false;
    }

    /// <summary>A deck's frozen Cargo Volume set round what was laid there, with a margin, on the configure tool's 10 cm steps.</summary>
    private void Enclose(DeckSpot spot)
    {
        if (spot.Volume is not { } volume)
            return;
        var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var (mn, mx) in spot.Laid)
            (lo, hi) = (Vector3.Min(lo, mn), Vector3.Max(hi, mx));
        lo -= new Vector3(0.15f, 0.05f, 0.15f);
        hi += new Vector3(0.15f, 0.15f, 0.15f);
        // The box in the volume part's own frame (it may be turned on its socket), on 10 cm steps; the part's middle stays where it is.
        var vc = CentreOf(volume);
        var inv = Quaternion.Conjugate(Rot24.Quat(C.Parts[volume.Index].Rot));
        var size = Vector3.Abs(Vector3.Transform(hi - lo, inv));
        var off = Vector3.Transform((lo + hi) * 0.5f - vc, inv);
        static double Step(float v) => Math.Round(Math.Clamp(Math.Ceiling(v * 10 - 0.001) / 10, ConstructCargo.MinSide, ConstructCargo.MaxSide), 1);
        Config(volume, ("startOn", true), ("sizeX", Step(size.X)), ("sizeY", Step(size.Y)), ("sizeZ", Step(size.Z)),
            ("atX", Math.Round(off.X, 1)), ("atY", Math.Round(off.Y, 1)), ("atZ", Math.Round(off.Z, 1)));
    }
}

/// <summary>
/// <c>--cargo "Style role" [--tier T] [--seeds N] [--from S] [--no-cargo]</c>, or <c>--cargo all [--tiers 1,4] [--seeds N] [--style S] [--role R]</c>: what each kit-4 vehicle carries
/// for itself (counts by kind, slots and loose, the loose mass), its role, its drive check and identity, and the cost; then the totals. <c>--no-cargo</c> makes the same seeds
/// without the pass, to compare.
/// </summary>
public static class VehicleCargoCli
{
    private static string? Flag(string[] args, string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));

    public static int Run(string what, string[] args)
    {
        if (args.Any(a => a.Equals("--no-cargo", StringComparison.OrdinalIgnoreCase)))
            Vehicle.CargoOn = false;
        var seeds = VehicleQualityCli.SeedsFrom(args, 2);
        var tiers = Flag(args, "--tier") is { } one && int.TryParse(one, out var t1) ? [t1]
            : (Flag(args, "--tiers") ?? "1,2,3,4").Split(',').Select(x => int.TryParse(x, out var t) ? t : 2).ToArray();
        var cells = new List<(FactionStyle St, VehicleRole Role)>();
        if (what.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var styleOnly = Flag(args, "--style");
            var roleOnly = Flag(args, "--role");
            foreach (var st in Styles.All.Where(s => styleOnly == null || s.Name.Equals(styleOnly, StringComparison.OrdinalIgnoreCase)))
                foreach (var role in VehicleRoles.All.Where(r => roleOnly == null || r.Name.Equals(roleOnly, StringComparison.OrdinalIgnoreCase)))
                    cells.Add((st, role));
        }
        else
        {
            var st = Styles.All.FirstOrDefault(s => what.StartsWith(s.Name + " ", StringComparison.OrdinalIgnoreCase));
            var role = st == null ? null : VehicleRoles.Find(what[(st.Name.Length + 1)..]);
            if (st == null || role == null)
            {
                Console.WriteLine($"--cargo \"{what}\": a style and a role (\"Bandit raider\"), or all");
                return 1;
            }
            cells.Add((st, role));
        }
        Console.WriteLine($"cargo pass {(Vehicle.CargoOn ? "on" : "OFF")}: {cells.Count} cells x tiers {string.Join(",", tiers)} x {seeds.Count} seeds");
        var kinds = Enum.GetValues<StowKind>();
        var total = new Dictionary<StowKind, (int All, int Loose)>();
        int made = 0, failed = 0, met = 0, gated = 0, withCargo = 0;
        double ms = 0, worst = 0, mass = 0, share = 0;
        var misses = new Dictionary<string, int>();
        var byRole = new Dictionary<string, (int N, int Met, int Pieces, double Kg)>();
        var gate0 = VehicleDriveCheck.Caught;
        foreach (var (st, role) in cells)
            foreach (var tier in tiers)
                foreach (var seed in seeds)
                {
                    var o = VehicleRolesCli.Make(st, role, tier, seed);
                    ms += o.Ms;
                    worst = Math.Max(worst, o.Ms);
                    if (o.V is not { } v)
                    {
                        failed++;
                        Console.WriteLine($"{st.Name} {role.Name} tier {tier} seed {seed}: FAILED {o.Error}");
                        continue;
                    }
                    made++;
                    var ok = v.Fit is { Met: true };
                    met += ok ? 1 : 0;
                    var pieces = v.Stowage.Sum(x => x.Count);
                    withCargo += pieces > 0 ? 1 : 0;
                    foreach (var x in v.Stowage)
                    {
                        var (a, l) = total.GetValueOrDefault(x.Kind);
                        total[x.Kind] = (a + x.Count, l + (x.Loose ? x.Count : 0));
                    }
                    mass += v.SpareMass;
                    share += v.Fit is { } fit && fit.Mass > 0 ? v.SpareMass / fit.Mass : 0;
                    foreach (var m in v.KitCargoMisses)
                    {
                        var key = System.Text.RegularExpressions.Regex.Replace(m, "[0-9.]+", "#");
                        misses[key] = misses.GetValueOrDefault(key) + 1;
                    }
                    var (rn, rm, rp, rk) = byRole.GetValueOrDefault(role.Name);
                    byRole[role.Name] = (rn + 1, rm + (ok ? 1 : 0), rp + pieces, rk + v.SpareMass);
                    var drive = v.DriveCheck is { } dc ? $"pulse {dc.Moved:0.0} m" : "unchecked";
                    Console.WriteLine($"{v.Name} ({o.Shape}): {(ok ? "role met" : "role MISSED (" + string.Join("; ", v.Fit?.Misses ?? []) + ")")}, identity {v.Identity?.Total:0.00}, {drive}, {o.Ms:0} ms; "
                        + (v.Stowage.Count == 0 ? "carries nothing of its own" : v.CargoNote));
                    if (args.Any(a => a.Equals("--list", StringComparison.OrdinalIgnoreCase)))
                        foreach (var x in v.Stowage)
                            Console.WriteLine($"    {Vehicle.KindWord(x.Kind)}: {x.Count} {x.Part}, {x.Where}, for {x.For}");
                    if (Flag(args, "--write") is { } dir)
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(Path.Combine(dir, $"{st.Name}_{role.Name.Replace(' ', '_')}_{tier}_{seed}.json"), v.Text());
                    }
                    if (v.KitCargoMisses.Count > 0)
                        Console.WriteLine($"    left behind: {string.Join("; ", v.KitCargoMisses)}");
                }
        Console.WriteLine();
        foreach (var (role, (n, m, p, kg)) in byRole)
            Console.WriteLine($"role {role}: met {m}/{n}, {(double)p / Math.Max(1, n):0.0} pieces a vehicle, {kg / Math.Max(1, n):0} kg loose a vehicle");
        Console.WriteLine("carried, all / loose: " + string.Join(", ", kinds.Select(k => $"{Vehicle.KindWord(k)} {total.GetValueOrDefault(k).All}/{total.GetValueOrDefault(k).Loose}")));
        foreach (var (m, n) in misses.OrderByDescending(x => x.Value).Take(8))
            Console.WriteLine($"    {n}x left behind: {m}");
        Console.WriteLine($"all: {made} made, {failed} failed, {met}/{made} met their role, {withCargo} carry something, {mass / Math.Max(1, made):0} kg loose a vehicle ({share / Math.Max(1, made):P1} of its mass), "
            + $"{ms / Math.Max(1, made + failed):0} ms a vehicle, worst {worst:0}; drive check refused {VehicleDriveCheck.Caught - gate0} plans");
        return failed;
    }
}
