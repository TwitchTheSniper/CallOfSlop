using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A Zombies area: a rectangle of the map (any height), or on a map with floors over each other (the Cathedral) a
/// rectangle between two heights, plus any number of more boxes ("y" and "more" in the map file: an area is the union
/// of its boxes). Areas are unlocked by opening the doors between them. Where areas' boxes overlap the first listed wins.
/// </summary>
public sealed class ZArea
{
    public required string Name;

    /// <summary>The first box (its rectangle; <see cref="Y0"/>..<see cref="Y1"/> its heights, infinite without "y").</summary>
    public float X0, Z0, X1, Z1;

    public float Y0 = float.NegativeInfinity, Y1 = float.PositiveInfinity;

    /// <summary>More boxes of the same area ("more": [[x0, z0, x1, z1, y0, y1], ...]: a stairwell, a landing); usually none.</summary>
    public Box[] More = [];

    public bool Contains(Vector3 p)
    {
        if (p.X >= X0 && p.X <= X1 && p.Z >= Z0 && p.Z <= Z1 && p.Y >= Y0 && p.Y <= Y1)
            return true;
        for (var i = 0; i < More.Length; i++)
            if (p.X >= More[i].Min.X && p.X <= More[i].Max.X && p.Z >= More[i].Min.Z && p.Z <= More[i].Max.Z && p.Y >= More[i].Min.Y && p.Y <= More[i].Max.Y)
                return true;
        return false;
    }

    /// <summary>Has the area a height band (a floor of a building with floors over each other)?</summary>
    public bool Banded => !float.IsNegativeInfinity(Y0) || !float.IsPositiveInfinity(Y1);

    public Vector3 Center => new((X0 + X1) / 2, Banded ? Y0 + 0.5f : 0, (Z0 + Z1) / 2);

    /// <summary>Distance from <paramref name="p"/> to the area (0 inside): flat for an area without heights, else including the height.</summary>
    public float Distance(Vector3 p)
    {
        var d = Dist(p, X0, Z0, X1, Z1, Y0, Y1);
        for (var i = 0; i < More.Length; i++)
            d = MathF.Min(d, Dist(p, More[i].Min.X, More[i].Min.Z, More[i].Max.X, More[i].Max.Z, More[i].Min.Y, More[i].Max.Y));
        return d;
    }

    private static float Dist(Vector3 p, float x0, float z0, float x1, float z1, float y0, float y1)
    {
        var dx = MathF.Max(0, MathF.Max(x0 - p.X, p.X - x1));
        var dz = MathF.Max(0, MathF.Max(z0 - p.Z, p.Z - z1));
        var dy = MathF.Max(0, MathF.Max(y0 - p.Y, p.Y - y1)); // (0 without a band: -inf..inf)
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>A buyable door or pile of debris between two areas: a solid box until it's bought, then it's gone.</summary>
public sealed class ZDoor
{
    public required string Name;
    public bool Debris;
    public int Cost;
    public bool NeedsPower;
    public Box Box;

    /// <summary>The two areas it joins.</summary>
    public int AreaA, AreaB;

    /// <summary>Where you stand on each side (on the ground, just clear of the door): zombies walk from one to the other once it's open.</summary>
    public Vector3 SideA, SideB;

    /// <summary>Its box in <see cref="Map.Solid"/>.</summary>
    public int SolidIndex = -1;

    public Vector3 SideIn(int area) => area == AreaA ? SideA : SideB;

    public Vector3 SideOut(int area) => area == AreaA ? SideB : SideA;

    public int Other(int area) => area == AreaA ? AreaB : AreaA;
}

/// <summary>A boarded window (barrier): zombies come to its outside, tear the boards off and climb in; players put boards back.</summary>
public sealed class ZWindow
{
    public int Area;

    /// <summary>The opening. It's an invisible wall for players and zombies (bullets pass): zombies only get in by climbing.</summary>
    public Box Opening;

    public Vector3 Outside, Inside;
    public int Boards = 6;
    public int SolidIndex = -1;

    /// <summary>Facing into the area (from outside to inside).</summary>
    public float Yaw => MathF.Atan2(Inside.X - Outside.X, -(Inside.Z - Outside.Z));
}

/// <summary>Where zombies come from: behind a window (they walk to it) or out of the ground in an area.</summary>
public sealed class ZSpawner
{
    public int Area;
    public Vector3 At;

    /// <summary>The window they come in through, or -1 (they rise out of the ground right there).</summary>
    public int Window = -1;
}

/// <summary>A chalk outline on a wall: buys a standard build tagged "wall" (or ammo for it).</summary>
public sealed class ZWallBuy
{
    public required string Build;
    public Vector3 At;
    public float Yaw;
    public int Cost, AmmoCost;
    public int Area;
}

/// <summary>A placed thing that isn't more than a spot: a mystery box spot, a workbench, a perk machine, the slot upgrade, the power switch, a salvage pile.</summary>
public sealed class ZSpot
{
    public Vector3 At;
    public float Yaw;
    public int Area;

    /// <summary>Perk machines: the perk (a key in the rules file); salvage: unused.</summary>
    public string Perk = "";

    /// <summary>
    /// Perk machines: the prop it stands as (and is drawn as without machine props): the perk's own machine
    /// (<see cref="ZombiesMap.MachineProp.PerkFor"/>, "Jugg Perk Machine") if the props have it, else the plain
    /// "Perk Machine" (a modded perk without a machine of its own). Worked out when the map is read.
    /// </summary>
    public string PropName = "";

    /// <summary>Price (slot upgrade, slot machines); 0 = the rules' default. Salvage piles ignore it (they gave that much scrap before 2026-10-27; now they give parts).</summary>
    public int Cost;

    /// <summary>Doesn't work until the power is on.</summary>
    public bool NeedsPower;

    /// <summary>
    /// Slot machines: the part category it pays (a <see cref="PartCategory"/> as a number, from "category" in the map
    /// file), or -1 for the classic machine that pays any part (ZombiesSlots.cs, "per-category machines").
    /// </summary>
    public int SlotCategory = -1; // a PartCategory, or SlotCategoryGrenade (any grenade part)

    /// <summary>Casino tables (ZombiesCasino.cs): the game (a <see cref="CasinoGame"/> as a number, from "game"); -1 for anything else.</summary>
    public int Game = -1;

    /// <summary>Casino tables: the table's own smallest and largest bet ("min" / "max"); 0 = the rules' limits for its game.</summary>
    public int BetMin, BetMax;
}

/// <summary>
/// A map's "zombies" section (docs/zombies/ZOMBIES-MODE.md, "Map format"): the areas (with the start area), the doors
/// and debris between them, windows with boards, zombie spawners, wall buys, mystery box spots, workbenches, perk
/// machines, the weapon-slot upgrade, the power switch and salvage piles. A map with this section can be played in
/// the Zombies mode. Doors and window openings become boxes in <see cref="Map.Solid"/> (drawn by the Zombies renderer,
/// not as map boxes): doors are solid until bought, window openings block bodies but not bullets.
/// </summary>
public sealed class ZombiesMap
{
    public readonly List<ZArea> Areas = new();
    public int StartArea;
    public readonly List<ZDoor> Doors = new();
    public readonly List<ZWindow> Windows = new();
    public readonly List<ZSpawner> Spawners = new();
    public readonly List<ZWallBuy> WallBuys = new();
    public readonly List<ZSpot> BoxSpots = new();
    public int BoxStart;
    public int BoxCost;
    public readonly List<ZSpot> Benches = new();
    public readonly List<ZSpot> PerkMachines = new();
    public ZSpot? SlotUpgrade;
    public ZSpot? Power;
    public readonly List<ZSpot> Salvage = new();

    /// <summary>Slot machines (ZombiesSlots.cs): pull for a loose part, points back or nothing. A spot's cost overrides the rules' price.</summary>
    public readonly List<ZSpot> SlotMachines = new();

    /// <summary>Casino tables (ZombiesCasino.cs): blackjack, roulette, high-low and dice, betting points ("casino" in the map file).</summary>
    public readonly List<ZSpot> CasinoTables = new();

    /// <summary>
    /// Where the trader sets up his stall ("traderSpots"; ZombiesTrader.cs): one is picked at random each visit, an open
    /// area's first. A map without any uses its box spots and workbenches (the trader stands in front of them).
    /// </summary>
    public readonly List<ZSpot> TraderSpots = new();

    /// <summary>
    /// "machineProps": true: every machine stands as a solid prop from <c>Props/Zombies</c> (<see cref="MachineProp"/>):
    /// perk machines, workbenches, the power switch, the slot upgrade, and a crate at every mystery box spot (the box is
    /// drawn over the crate at its spot). Players, zombies and bullets can't pass through them. Off (Zombies Test): the
    /// machines are drawn but have no collision.
    /// </summary>
    public bool MachineProps;

    /// <summary>
    /// "testKit": true: a test map (Dev/HordeTest.json). The host's "Test kit" lobby setting works here only: everyone
    /// starts with every perk, the slot upgrade and strong late builds (Source/Game/ZombiesStartRound.cs).
    /// </summary>
    public bool TestKit;

    /// <summary>
    /// "crowd": N (a mass-horde test map: Dev/MassHordeTest.json; 0, the default: off). Each round is N zombies, nearly all
    /// of them crowd particles (Source/Game/Horde/HordeCrowd.cs) that become full zombies near the players and go back to
    /// the crowd far from them. The spike of the mass horde (docs/zombies/MASS-HORDE.md).
    /// </summary>
    public int Crowd;

    /// <summary>The most a map's <see cref="Crowd"/> can ask for.</summary>
    public const int MaxCrowd = 32000;

    /// <summary>The prop each kind of machine stands as with <see cref="MachineProps"/>.</summary>
    public static class MachineProp
    {
        public const string Perk = "Perk Machine", BoxSpot = "Mystery Box Crate", Bench = "Zombies Workbench", Power = "Power Switch Box", Slots = "Weapon Locker", SlotMachine = "Reel Cabinet";

        /// <summary>
        /// The perk machine prop of perk <paramref name="key"/> (2026-10-27: every perk has its own machine,
        /// Props/Zombies/PerkMachines, written by tools/zombies/make_perk_machines.py): "Jugg Perk Machine". Props are
        /// found by name ignoring case. A map whose props lack it uses the plain <see cref="Perk"/> prop.
        /// </summary>
        public static string PerkFor(string key) => key.Length == 0 ? Perk : $"{char.ToUpperInvariant(key[0])}{key[1..]} {Perk}";

        private static readonly string[] CategoryCabinets = Enum.GetValues<PartCategory>().Select(c => $"{PartDef.BuiltInCategoryName(c)} {SlotMachine}").ToArray();

        /// <summary>The prop a slot machine of part category <paramref name="category"/> stands as ("Barrel Reel Cabinet"; -1: the classic "Reel Cabinet").</summary>
        public static string SlotMachineFor(int category) => category == SlotCategoryGrenade ? GrenadeCabinet
            : category >= 0 && category < CategoryCabinets.Length ? CategoryCabinets[category] : SlotMachine;

        /// <summary>The grenade parts machine's cabinet (Props/Zombies/SlotMachines, tools/zombies/make_slot_cabinets.py).</summary>
        public const string GrenadeCabinet = "Grenade Parts Reel Cabinet";

        /// <summary>The prop each casino game's table stands as (Props/Zombies/Casino, written by tools/zombies/make_casino_tables.py), by <see cref="CasinoGame"/>.</summary>
        public static readonly string[] CasinoTables = ["Blackjack Table", "Roulette Table", "High-Low Table", "Dice Table"];

        public static string CasinoTableFor(int game) => CasinoTables[Math.Clamp(game, 0, CasinoTables.Length - 1)];
    }

    /// <summary>The section as it was in the file (written back unchanged).</summary>
    public string Raw = "{}";

    /// <summary>A slot machine's category for the grenade parts machine (any casing, fuse, payload or grenade mod; "category": "grenade" in a map).</summary>
    public const int SlotCategoryGrenade = 64;

    /// <summary>
    /// The categories slot machines come in (the part categories, except that the four grenade ones share the grenade
    /// parts machine, <see cref="SlotCategoryGrenade"/>): maps with category machines have one of each.
    /// </summary>
    public static readonly int[] SlotCategories = Enum.GetValues<PartCategory>()
        .Where(c => c is not (PartCategory.Casing or PartCategory.Fuse or PartCategory.Payload or PartCategory.GrenadeMod))
        .Select(c => (int)c).Append(SlotCategoryGrenade).ToArray();

    public const int MaxDoors = 32, MaxWindows = 32, MaxAreas = 32, MaxBoards = 12, MaxSlotMachines = 24, MaxCasinoTables = 8, MaxTraderSpots = 32;

    /// <summary>The area <paramref name="p"/> is in (the nearest one if it's in none).</summary>
    public int AreaAt(Vector3 p)
    {
        var best = StartArea;
        var bestD = float.MaxValue;
        for (var i = 0; i < Areas.Count; i++)
        {
            var d = Areas[i].Distance(p);
            if (d <= 0)
                return i;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Is <paramref name="p"/> inside any area (not outside, where zombies come from)?</summary>
    public bool Inside(Vector3 p)
    {
        for (var i = 0; i < Areas.Count; i++) // (a loop: Any with a lambda allocated a closure per call, every zombie every tick)
            if (Areas[i].Contains(p))
                return true;
        return false;
    }

    public int AreaIndex(string name) => Areas.FindIndex(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // ---- Reading ----------------------------------------------------------------------------------------

    private static readonly string[] Keys = ["start", "areas", "doors", "windows", "spawners", "wallBuys", "box", "workbenches", "perks", "slotUpgrade", "power", "salvage", "slots", "casino", "traderSpots", "machineProps", "testKit", "crowd"];

    /// <summary>Reads the section and adds its door and window boxes to <paramref name="map"/>.</summary>
    public static ZombiesMap Read(JsonElement e, Map map, string source)
    {
        var where = $"{source}: zombies";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: expected an object");
        var z = new ZombiesMap { Raw = e.GetRawText() };
        foreach (var p in e.EnumerateObject())
            if (!Keys.Contains(p.Name))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (it takes: {string.Join(", ", Keys)})");

        if (Get(e, "areas") is not { ValueKind: JsonValueKind.Array } areas || areas.GetArrayLength() == 0)
            throw new MapFileException($"{where}: needs \"areas\": [ {{ \"name\": ..., \"rect\": [x0, z0, x1, z1] }} ]");
        foreach (var a in areas.EnumerateArray())
        {
            Only(a, $"{where}.areas", "name", "rect", "y", "more");
            var r = Nums(Need(a, "rect", $"{where}.areas"), 4, $"{where}.areas.rect");
            var area = new ZArea
            {
                Name = Text(Need(a, "name", $"{where}.areas"), $"{where}.areas.name"),
                X0 = MathF.Min(r[0], r[2]), Z0 = MathF.Min(r[1], r[3]), X1 = MathF.Max(r[0], r[2]), Z1 = MathF.Max(r[1], r[3]),
            };
            if (Get(a, "y") is { } ye)
            {
                // A floor of a building with floors over each other: the area is only between these heights.
                var y = Nums(ye, 2, $"{where}.areas.y");
                (area.Y0, area.Y1) = (MathF.Min(y[0], y[1]), MathF.Max(y[0], y[1]));
                if (area.Y1 - area.Y0 < 2)
                    throw new MapFileException($"{where}: area '{area.Name}': \"y\" is [bottom, top], at least 2 m apart");
            }
            if (Get(a, "more") is { } more)
                area.More = List(more, $"{where}.areas.more").Select(m =>
                {
                    var b = Nums(m, 6, $"{where}.areas.more (each [x0, z0, x1, z1, y0, y1])");
                    var lo = new Vector3(MathF.Min(b[0], b[2]), MathF.Min(b[4], b[5]), MathF.Min(b[1], b[3]));
                    var hi = new Vector3(MathF.Max(b[0], b[2]), MathF.Max(b[4], b[5]), MathF.Max(b[1], b[3]));
                    if (hi.X - lo.X < 0.5f || hi.Z - lo.Z < 0.5f || hi.Y - lo.Y < 2)
                        throw new MapFileException($"{where}: area '{area.Name}': a \"more\" box is smaller than 0.5 x 0.5 m or under 2 m tall");
                    return new Box((lo + hi) / 2, (hi - lo) / 2);
                }).ToArray();
            if (z.AreaIndex(area.Name) >= 0)
                throw new MapFileException($"{where}: two areas called '{area.Name}'");
            if (area.X1 - area.X0 < 1 || area.Z1 - area.Z0 < 1)
                throw new MapFileException($"{where}: area '{area.Name}' is smaller than 1 x 1 m");
            z.Areas.Add(area);
        }
        if (z.Areas.Count > MaxAreas)
            throw new MapFileException($"{where}: at most {MaxAreas} areas");
        z.StartArea = Get(e, "start") is { } st ? AreaRef(z, st, $"{where}.start") : 0;

        if (Get(e, "doors") is { } doors)
            foreach (var d in List(doors, $"{where}.doors"))
            {
                var w = $"{where}.doors";
                Only(d, w, "name", "kind", "cost", "box", "areas", "power");
                var b = Nums(Need(d, "box", w), 6, $"{w}.box");
                var box = BoxOf(b);
                var names = Need(d, "areas", w);
                if (names.ValueKind != JsonValueKind.Array || names.GetArrayLength() != 2)
                    throw new MapFileException($"{w}: \"areas\" is the two area names the door joins");
                var kind = Get(d, "kind") is { } k ? Text(k, $"{w}.kind") : "door";
                if (kind is not ("door" or "debris"))
                    throw new MapFileException($"{w}.kind: \"door\" or \"debris\"");
                var door = new ZDoor
                {
                    Name = Get(d, "name") is { } n ? Text(n, $"{w}.name") : $"Door {z.Doors.Count + 1}",
                    Debris = kind == "debris",
                    Cost = Get(d, "cost") is { } c ? Whole(c, $"{w}.cost", 0, 1000000) : 750,
                    NeedsPower = Get(d, "power") is { ValueKind: JsonValueKind.True },
                    Box = box,
                    AreaA = AreaRef(z, names[0], $"{w}.areas"),
                    AreaB = AreaRef(z, names[1], $"{w}.areas"),
                };
                if (door.AreaA == door.AreaB)
                    throw new MapFileException($"{w}: door '{door.Name}' joins an area to itself");
                // The two sides: across the door's thin axis, a little clear of it, on the ground.
                var thinX = box.Half.X <= box.Half.Z;
                var off = (thinX ? box.Half.X : box.Half.Z) + 0.75f;
                var axis = thinX ? Vector3.UnitX : Vector3.UnitZ;
                var s1 = DoorSide(map, box, box.Center + axis * off);
                var s2 = DoorSide(map, box, box.Center - axis * off);
                var a1 = z.Areas[door.AreaA];
                (door.SideA, door.SideB) = a1.Distance(s1) <= a1.Distance(s2) ? (s1, s2) : (s2, s1);
                z.Doors.Add(door);
            }
        if (z.Doors.Count > MaxDoors)
            throw new MapFileException($"{where}: at most {MaxDoors} doors");

        if (Get(e, "windows") is { } windows)
            foreach (var wi in List(windows, $"{where}.windows"))
            {
                var w = $"{where}.windows";
                Only(wi, w, "area", "box", "outside", "inside", "boards");
                var win = new ZWindow
                {
                    Area = AreaRef(z, Need(wi, "area", w), $"{w}.area"),
                    Opening = BoxOf(Nums(Need(wi, "box", w), 6, $"{w}.box")),
                    Outside = Point(map, Need(wi, "outside", w), $"{w}.outside"),
                    Inside = Point(map, Need(wi, "inside", w), $"{w}.inside"),
                    Boards = Get(wi, "boards") is { } bo ? Whole(bo, $"{w}.boards", 1, MaxBoards) : 6,
                };
                if (!z.Areas[win.Area].Contains(win.Inside))
                    throw new MapFileException($"{w}: the window's \"inside\" point isn't in its area '{z.Areas[win.Area].Name}'");
                z.Windows.Add(win);
            }
        if (z.Windows.Count > MaxWindows)
            throw new MapFileException($"{where}: at most {MaxWindows} windows");

        if (Get(e, "spawners") is { } spawners)
            foreach (var s in List(spawners, $"{where}.spawners"))
            {
                var w = $"{where}.spawners";
                Only(s, w, "at", "window", "area");
                var sp = new ZSpawner { At = Point(map, Need(s, "at", w), $"{w}.at") };
                if (Get(s, "window") is { } wn)
                {
                    sp.Window = Whole(wn, $"{w}.window", 0, z.Windows.Count - 1);
                    sp.Area = z.Windows[sp.Window].Area;
                }
                else if (Get(s, "area") is { } an)
                    sp.Area = AreaRef(z, an, $"{w}.area");
                else
                    sp.Area = z.AreaAt(sp.At);
                z.Spawners.Add(sp);
            }
        if (z.Spawners.Count == 0)
            throw new MapFileException($"{where}: needs at least one entry in \"spawners\"");
        if (!z.Spawners.Any(s => s.Area == z.StartArea))
            throw new MapFileException($"{where}: no spawner in the start area '{z.Areas[z.StartArea].Name}' (round 1 would have nowhere to come from)");

        if (Get(e, "wallBuys") is { } walls)
            foreach (var wb in List(walls, $"{where}.wallBuys"))
            {
                var w = $"{where}.wallBuys";
                Only(wb, w, "build", "at", "yaw", "cost", "ammoCost");
                var buy = new ZWallBuy
                {
                    Build = Text(Need(wb, "build", w), $"{w}.build"),
                    At = Point(map, Need(wb, "at", w), $"{w}.at", 1.3f),
                    Yaw = Get(wb, "yaw") is { } y ? Num(y, $"{w}.yaw") * MathF.PI / 180f : 0,
                    Cost = Get(wb, "cost") is { } c ? Whole(c, $"{w}.cost", 0, 1000000) : 0,
                };
                buy.AmmoCost = Get(wb, "ammoCost") is { } ac ? Whole(ac, $"{w}.ammoCost", 0, 1000000) : 0;
                buy.Area = z.AreaAt(buy.At);
                z.WallBuys.Add(buy);
            }

        if (Get(e, "box") is { } bx)
        {
            var w = $"{where}.box";
            Only(bx, w, "spots", "start", "cost");
            foreach (var s in List(Need(bx, "spots", w), $"{w}.spots"))
                z.BoxSpots.Add(Spot(map, z, s, $"{w}.spots"));
            if (z.BoxSpots.Count == 0)
                throw new MapFileException($"{w}: needs at least one spot");
            z.BoxStart = Get(bx, "start") is { } bs ? Whole(bs, $"{w}.start", 0, z.BoxSpots.Count - 1) : 0;
            z.BoxCost = Get(bx, "cost") is { } bc ? Whole(bc, $"{w}.cost", 0, 1000000) : 0;
        }
        if (Get(e, "workbenches") is { } benches)
            foreach (var s in List(benches, $"{where}.workbenches"))
                z.Benches.Add(Spot(map, z, s, $"{where}.workbenches"));
        if (Get(e, "perks") is { } perks)
            foreach (var s in List(perks, $"{where}.perks"))
            {
                var spot = Spot(map, z, s, $"{where}.perks", "perk");
                if (spot.Perk.Length == 0)
                    throw new MapFileException($"{where}.perks: each machine needs \"perk\" (a perk key from Parts/Tables/ZombiesRules.json)");
                var own = MachineProp.PerkFor(spot.Perk);
                spot.PropName = map.PropScope.Find(own) != null ? own : MachineProp.Perk;
                z.PerkMachines.Add(spot);
            }
        if (Get(e, "slotUpgrade") is { } su)
            z.SlotUpgrade = Spot(map, z, su, $"{where}.slotUpgrade");
        if (Get(e, "power") is { } pw)
            z.Power = Spot(map, z, pw, $"{where}.power");
        if (Get(e, "salvage") is { } sv)
            foreach (var s in List(sv, $"{where}.salvage"))
                z.Salvage.Add(Spot(map, z, s, $"{where}.salvage"));
        if (Get(e, "slots") is { } sm)
            foreach (var s in List(sm, $"{where}.slots"))
            {
                var spot = Spot(map, z, s, $"{where}.slots", "category");
                if (spot.Perk.Length > 0)
                {
                    // A per-category machine: it pays parts of this category only ("Barrel", "Optic", "Melee Mod"...), or
                    // "Grenade" / "Grenade Parts": any grenade part (casings, fuses, payloads, grenade mods).
                    if (spot.Perk.Equals("grenade", StringComparison.OrdinalIgnoreCase) || spot.Perk.Equals("grenade parts", StringComparison.OrdinalIgnoreCase))
                    {
                        spot.SlotCategory = SlotCategoryGrenade;
                        spot.Perk = "";
                        z.SlotMachines.Add(spot);
                        continue;
                    }
                    var cat = Enum.GetValues<PartCategory>().Where(c => c.ToString().Equals(spot.Perk, StringComparison.OrdinalIgnoreCase)
                                                                          || PartDef.BuiltInCategoryName(c).Equals(spot.Perk, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (cat.Length != 1)
                        throw new MapFileException($"{where}.slots: unknown part category \"{spot.Perk}\" (one of {string.Join(", ", Enum.GetValues<PartCategory>())})");
                    spot.SlotCategory = (int)cat[0];
                    spot.Perk = "";
                }
                z.SlotMachines.Add(spot);
            }
        if (z.SlotMachines.Count > MaxSlotMachines)
            throw new MapFileException($"{where}: at most {MaxSlotMachines} slot machines");
        if (Get(e, "casino") is { } cas)
            foreach (var t in List(cas, $"{where}.casino"))
            {
                var w = $"{where}.casino";
                Only(t, w, "game", "at", "yaw", "power", "min", "max");
                var game = Text(Need(t, "game", w), $"{w}.game");
                var k = Array.FindIndex(CasinoGames.Keys, g => g.Equals(game, StringComparison.OrdinalIgnoreCase));
                if (k < 0)
                    throw new MapFileException($"{w}: unknown game \"{game}\" (one of {string.Join(", ", CasinoGames.Keys)})");
                var spot = new ZSpot
                {
                    At = Point(map, Need(t, "at", w), $"{w}.at"),
                    Yaw = Get(t, "yaw") is { } y ? Num(y, $"{w}.yaw") * MathF.PI / 180f : 0,
                    NeedsPower = Get(t, "power") is { ValueKind: JsonValueKind.True },
                    Game = k,
                    BetMin = Get(t, "min") is { } mn ? Whole(mn, $"{w}.min", 1, 1000000) : 0,
                    BetMax = Get(t, "max") is { } mx ? Whole(mx, $"{w}.max", 1, 1000000) : 0,
                };
                if (spot.BetMin > 0 && spot.BetMax > 0 && spot.BetMax < spot.BetMin)
                    throw new MapFileException($"{w}: max is below min");
                spot.Area = z.AreaAt(spot.At);
                z.CasinoTables.Add(spot);
            }
        if (z.CasinoTables.Count > MaxCasinoTables)
            throw new MapFileException($"{where}: at most {MaxCasinoTables} casino tables");
        if (Get(e, "traderSpots") is { } ts)
            foreach (var s in List(ts, $"{where}.traderSpots"))
                z.TraderSpots.Add(Spot(map, z, s, $"{where}.traderSpots"));
        if (z.TraderSpots.Count > MaxTraderSpots)
            throw new MapFileException($"{where}: at most {MaxTraderSpots} trader spots");
        if (Get(e, "machineProps") is { } mp)
            z.MachineProps = mp.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new MapFileException($"{where}.machineProps: true or false"),
            };
        if (Get(e, "testKit") is { } tk)
            z.TestKit = tk.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new MapFileException($"{where}.testKit: true or false"),
            };
        if (Get(e, "crowd") is { } cr)
        {
            if (cr.ValueKind != JsonValueKind.Number || !cr.TryGetInt32(out var n) || n < 0 || n > MaxCrowd)
                throw new MapFileException($"{where}.crowd: a whole number of zombies from 0 to {MaxCrowd}");
            z.Crowd = n;
        }
        if (z.MachineProps)
            foreach (var (name, _) in z.Machines())
                if (map.PropScope.Find(name) == null)
                    throw new MapFileException($"{where}.machineProps: the prop \"{name}\" is missing (Props/Zombies)");

        // The boxes, last (after every check, so a refused section adds nothing).
        foreach (var d in z.Doors)
            d.SolidIndex = map.AddZombieBox(d.Box, d.Debris ? new Vector3(0.38f, 0.33f, 0.28f) : new Vector3(0.45f, 0.3f, 0.18f), clip: false);
        foreach (var win in z.Windows)
            win.SolidIndex = map.AddZombieBox(win.Opening, new Vector3(0.4f, 0.3f, 0.2f), clip: true);
        if (z.MachineProps)
            foreach (var (name, spot) in z.Machines())
                map.AddMachineProp(name, spot);
        return z;
    }

    /// <summary>Every machine and the prop it stands as with <see cref="MachineProps"/>, in a fixed order (the box spots, perk machines, workbenches, the slot upgrade, the power switch, the slot machines).</summary>
    public IEnumerable<(string Prop, ZSpot Spot)> Machines()
    {
        foreach (var s in BoxSpots)
            yield return (MachineProp.BoxSpot, s);
        foreach (var s in PerkMachines)
            yield return (s.PropName.Length > 0 ? s.PropName : MachineProp.Perk, s);
        foreach (var s in Benches)
            yield return (MachineProp.Bench, s);
        if (SlotUpgrade is { } su)
            yield return (MachineProp.Slots, su);
        if (Power is { } pw)
            yield return (MachineProp.Power, pw);
        foreach (var s in SlotMachines)
            yield return (MachineProp.SlotMachineFor(s.SlotCategory), s); // (last: maps without them place the same props as before)
        foreach (var t in CasinoTables)
            yield return (MachineProp.CasinoTableFor(t.Game), t); // (after the slot machines: maps without tables place the same props as before)
    }

    private static ZSpot Spot(Map map, ZombiesMap z, JsonElement s, string w, string extra = "")
    {
        Only(s, w, extra.Length > 0 ? new[] { "at", "yaw", "power", "cost", extra } : new[] { "at", "yaw", "power", "cost" });
        var spot = new ZSpot
        {
            At = Point(map, Need(s, "at", w), $"{w}.at"),
            Yaw = Get(s, "yaw") is { } y ? Num(y, $"{w}.yaw") * MathF.PI / 180f : 0,
            NeedsPower = Get(s, "power") is { ValueKind: JsonValueKind.True },
            Cost = Get(s, "cost") is { } c ? Whole(c, $"{w}.cost", 0, 1000000) : 0,
        };
        if (extra.Length > 0 && Get(s, extra) is { } ex)
            spot.Perk = Text(ex, $"{w}.{extra}");
        spot.Area = z.AreaAt(spot.At);
        return spot;
    }

    private static Box BoxOf(float[] b)
    {
        var min = new Vector3(MathF.Min(b[0], b[3]), MathF.Min(b[1], b[4]), MathF.Min(b[2], b[5]));
        var max = new Vector3(MathF.Max(b[0], b[3]), MathF.Max(b[1], b[4]), MathF.Max(b[2], b[5]));
        if (max.X - min.X < 0.02f || max.Y - min.Y < 0.02f || max.Z - min.Z < 0.02f)
            throw new MapFileException($"a zombies box [{string.Join(", ", b)}] has no size");
        return new Box((min + max) / 2, (max - min) / 2);
    }

    private static Vector3 Ground(Map map, Vector3 p) => p with { Y = map.HasTerrain ? map.GroundHeight(p.X, p.Z) : 0 };

    /// <summary>
    /// A door's side on the floor it stands on: the ground, or for a door on an upper floor or underground (its bottom
    /// more than 0.3 m from the ground: the Cathedral's galleries and crypt) the door's own bottom.
    /// </summary>
    private static Vector3 DoorSide(Map map, Box box, Vector3 p)
    {
        var g = Ground(map, p);
        return MathF.Abs(box.Min.Y - g.Y) > 0.3f ? p with { Y = box.Min.Y } : g;
    }

    /// <summary>[x, z] (on the ground, plus <paramref name="lift"/>) or [x, y, z].</summary>
    private static Vector3 Point(Map map, JsonElement e, string w, float lift = 0)
    {
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2)
        {
            var p = Ground(map, new Vector3(Num(e[0], w), 0, Num(e[1], w)));
            return p with { Y = p.Y + lift };
        }
        var v = Nums(e, 3, w);
        return new Vector3(v[0], v[1], v[2]);
    }

    private static int AreaRef(ZombiesMap z, JsonElement e, string w)
    {
        var name = Text(e, w);
        var i = z.AreaIndex(name);
        return i >= 0 ? i : throw new MapFileException($"{w}: no area called '{name}' (areas: {string.Join(", ", z.Areas.Select(a => a.Name))})");
    }

    private static IEnumerable<JsonElement> List(JsonElement e, string w) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : throw new MapFileException($"{w}: expected a list");

    private static void Only(JsonElement e, string w, params string[] keys)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{w}: expected an object");
        foreach (var p in e.EnumerateObject())
            if (!keys.Contains(p.Name))
                throw new MapFileException($"{w}: unknown key \"{p.Name}\" (it takes: {string.Join(", ", keys)})");
    }

    private static JsonElement? Get(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject())
            if (p.Name == name)
                return p.Value;
        return null;
    }

    private static JsonElement Need(JsonElement e, string name, string w) =>
        Get(e, name) ?? throw new MapFileException($"{w}: missing \"{name}\"");

    private static float Num(JsonElement e, string w) =>
        e.ValueKind == JsonValueKind.Number ? e.GetSingle() : throw new MapFileException($"{w}: expected a number, got {e}");

    private static int Whole(JsonElement e, string w, int min, int max) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) && i >= min && i <= max
            ? i : throw new MapFileException($"{w}: expected a whole number from {min} to {max}, got {e}");

    private static string Text(JsonElement e, string w) =>
        e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s ? s : throw new MapFileException($"{w}: expected text, got {e}");

    private static float[] Nums(JsonElement e, int n, string w)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != n)
            throw new MapFileException($"{w}: expected a list of {n} numbers");
        return e.EnumerateArray().Select(x => Num(x, w)).ToArray();
    }
}

/// <summary>The Zombies boxes in a map: doors (solid until bought) and window openings (block bodies, not bullets).</summary>
public sealed partial class Map
{
    /// <summary>The map's "zombies" section, or null: only maps with one can be played in the Zombies mode.</summary>
    public ZombiesMap? Zombies;

    private bool[] _zombieBox = [];
    private bool[] _clipBox = [];
    private readonly Dictionary<int, Box> _zombieOriginal = new();

    /// <summary>Is <see cref="Solid"/>[i] a Zombies door or window box? (Drawn by the Zombies renderer, not as a map box.)</summary>
    public bool IsZombieBox(int i) => (uint)i < (uint)_zombieBox.Length && _zombieBox[i];

    /// <summary>Is <see cref="Solid"/>[i] a window opening: it blocks bodies (players, zombies, the nav graph) but not bullets?</summary>
    public bool IsClipBox(int i) => (uint)i < (uint)_clipBox.Length && _clipBox[i] || IsBuildingGlass(i); // (and building window glass, MapBuildings.cs)

    /// <summary>Adds a Zombies box to <see cref="Solid"/> (not recorded as an op: the "zombies" section writes itself back).</summary>
    internal int AddZombieBox(Box b, Vector3 color, bool clip)
    {
        var i = Solid.Count;
        Solid.Add(b);
        Colors.Add(color);
        Array.Resize(ref _zombieBox, Solid.Count);
        Array.Resize(ref _clipBox, Solid.Count);
        _zombieBox[i] = true;
        _clipBox[i] = clip;
        _zombieOriginal[i] = b;
        return i;
    }

    /// <summary>
    /// A Zombies machine's solid prop (the section's "machineProps"; not recorded as an op: the section writes itself back
    /// and places it again). Machine yaws turn the other way from prop yaws, so the prop's front (its -Z side) faces the
    /// way the machine does. It counts towards the prop fingerprint like any placed prop.
    /// </summary>
    internal void AddMachineProp(string name, ZSpot spot)
    {
        var def = PropScope.Find(name) ?? throw new MapFileException($"zombies.machineProps: no prop \"{name}\"");
        AddProp(def, spot.At, -spot.Yaw, 1f, null);
    }

    /// <summary>Is door <paramref name="door"/> open (its box parked out of the world)?</summary>
    public bool IsDoorOpen(int door) => Zombies is { } z && (uint)door < (uint)z.Doors.Count && Solid[z.Doors[door].SolidIndex].Center.Y < -500f;

    /// <summary>Opens (or closes) a door: its box is parked far under the world (the way broken props are) or put back.</summary>
    public void SetDoorOpen(int door, bool open)
    {
        if (Zombies is not { } z || (uint)door >= (uint)z.Doors.Count || IsDoorOpen(door) == open)
            return;
        var k = z.Doors[door].SolidIndex;
        var b = _zombieOriginal[k];
        Solid[k] = open ? new Box(new Vector3(b.Center.X, -1000f, b.Center.Z), new Vector3(0.001f)) : b;
        if (open)
            World.SolidChanged(this, k);
        else
            World.InvalidateGrid(this); // a box growing back: the collision world reads the boxes again
    }

    /// <summary>Closes every door (a new run, a new session on this map).</summary>
    public void CloseAllDoors()
    {
        if (Zombies is not { } z)
            return;
        var any = false;
        for (var d = 0; d < z.Doors.Count; d++)
        {
            if (!IsDoorOpen(d))
                continue;
            var k = z.Doors[d].SolidIndex;
            Solid[k] = _zombieOriginal[k];
            any = true;
        }
        if (any)
            World.InvalidateGrid(this);
    }

    /// <summary>Opens every door (the map played in a mode other than Zombies: all its areas are one map).</summary>
    public void OpenAllDoors()
    {
        if (Zombies is not { } z)
            return;
        for (var d = 0; d < z.Doors.Count; d++)
            SetDoorOpen(d, true);
    }

    /// <summary>The box the fingerprint uses for a Zombies box: the closed door (opening it never changes the fingerprint).</summary>
    internal bool ZombieHashBox(int i, out Box b) => _zombieOriginal.TryGetValue(i, out b);
}
