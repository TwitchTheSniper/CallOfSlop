using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.StationLook;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Space stations, built procedurally from the station kit (SpaceStationKit.cs, the look in SpaceStationLook.cs; notes in
/// docs/sandbox/constructs/station.md). A station is a plan: a list of module calls up a vertical spine, each with a few numbers (a
/// block's half-size and heights, an arm's direction and reach, the ship each berth takes). The kit turns each call into welded pieces in
/// the station's frame; then every touching pair of pieces gets a socket pair (<see cref="Connect"/>), the pieces are placed out from
/// the first, and the docking clamps and hatches are hung on their sockets.
///
/// The first plan is Caledon Highport (Karl, 2026-11-18: "think large, with docking spaces for ships ... Expanse vibes"; the layout
/// sheets in the project's station/highport folder): a 310 m tower with gravity down the spine, four docking tiers at their own heights
/// (pads for the Shuttle and Limpet on top, Zero Day berths on arms north and south, Magpie berths on arms east and west, Dray aprons
/// with a container lift), the commons and the ops crown above the docks, the warehouse, refineries, tank farm, engineering, reactor
/// and radiators below.
/// </summary>
public sealed partial class SpaceStationParts : IPartGenerator
{
    public string Name => "space_station";

    private const string Out = "Constructs/Parts/SpaceStations";
    private const string BpOut = "Constructs/Blueprints";
    public const string Group = "Station";

    /// <summary>The ships the berths are sized for (metres across, long, high: measured from their blueprints, 2026-11-18).</summary>
    private static readonly (double W, double L, double H) ZeroDay = (17.1, 53.3, 14.7), Magpie = (18.3, 34.9, 11.1), Dray = (14.7, 61.3, 11.7);

    // ---- Caledon Highport

    public const string HighportName = "Caledon Highport";

    private static Kit Highport()
    {
        var k = new Kit("Highport", HighportName);
        var N = new ArmFrame(0, -1);
        var S = new ArmFrame(0, 1);
        var E = new ArmFrame(1, 0);
        var W = new ArmFrame(-1, 0);

        // the base: station-keeping drives, the reactor under its shield plug, engineering and its radiators
        Thrusters(k, "Thrust Block", 11, -2, 4);
        Reactor(k, "Reactor", 8, 4, 20, 10, 22);
        Block(k, "Engineering", 15, 22, 40, new BlockOpts(Windows: false, Seed: 11, Job: "engineering", Roof: (p, y) => { Pdc(p, 12, y, 12); Pdc(p, -12, y, -12); },
            Rooms: new()
            {
                [0] = [new(-12, 6, 12, 12, "Power room", 's'), new(-12, -12, 0, -6, "Workshop", 'n'), new(0, -12, 12, -6, "Spares", 'n'), new(-12, -6, -6, 6, "Life support", 'e'), new(6, -6, 12, 6, "Air plant", 'w')],
                [1] = [new(-12, 6, 12, 12, "Switch room", 's'), new(-12, -12, 12, -6, "Water plant", 'n'), new(-12, -6, -6, 6, "Pumps", 'e'), new(6, -6, 12, 6, "Pumps", 'w')],
                [2] = [new(-12, 6, 0, 12, "Engineers' mess", 's'), new(0, 6, 12, 12, "Bunks", 's'), new(-12, -12, 12, -6, "Machine shop", 'n')],
                [3] = [new(-12, 6, 12, 12, "Control room", 's'), new(-12, -12, 12, -6, "Stores", 'n')],
            }));
        foreach (var (fr, nm) in new[] { (E, "E"), (W, "W"), (N, "N"), (S, "S") })
            Radiator(k, $"Radiator {nm}", fr, 15, 78, 30, 14, 7, 3);

        Spine(k, "Spine 1", 40, 48, 1);
        TankFarm(k, "Tank", 13, 48, 72, 21, 7.2, ["Reaction mass", "Reaction mass", "Water", "Reaction mass", "Reaction mass", "Oxygen", "Reaction mass", "Nitrogen"]);
        Spine(k, "Spine 2", 72, 80, 2);

        // freight: the warehouse, its refineries and the two Dray aprons with their container tunnels
        Block(k, "Warehouse", 25, 80, 110, new BlockOpts(Windows: false, Decks: false, Seed: 21, Job: "the warehouse", FloorExtra: Containers,
            Roof: (p, y) => { Pdc(p, 21, y, 21); Pdc(p, -21, y, -21); }));
        const string refDesc = "A space refinery against the warehouse's {0} wall: an open truss cage of process plant. Ore comes down the chute from the miners' arm into the hopper, through the crusher drum to the smelter; gas goes to the columns and spheres, plate and ingots down the duct into the warehouse.";
        Refinery(k, "Refinery E", string.Format(refDesc, "east"), 1, 25, 84, 104, 18, 9);
        Refinery(k, "Refinery W", string.Format(refDesc, "west"), -1, 25, 84, 104, 18, 9);
        foreach (var (fr, nm) in new[] { (N, "N"), (S, "S") })
            Apron(k, $"Dray Apron {nm}", fr, 25, 95, 110, (48, 72), 86, 60.5, Dray.W, Dray.L, Dray.H, 40);
        Spine(k, "Spine 3", 110, 133, 3);

        // the miner tier: a collar with arms east and west, two Magpie berths on each, ore conveyors and chutes down to the refineries
        Block(k, "Miner Collar", 13, 133, 149, new BlockOpts(Seed: 41, Job: "the miners' collar",
            Rooms: new()
            {
                [0] = [new(-10, 6, 10, 10, "Ore weighing", 's'), new(-10, -10, 10, -6, "Conveyor control", 'n')],
                [1] = [new(6, -4, 10, 4, "Airlock E", 'w'), new(-10, -4, -6, 4, "Airlock W", 'e'), new(-10, 6, 10, 10, "Assay", 's'), new(-10, -10, 10, -6, "Suit room", 'n')],
                [2] = [new(-10, 6, 10, 10, "Miners' bunks", 's'), new(-10, -10, 10, -6, "Mess", 'n')],
            }));
        foreach (var (fr, nm) in new[] { (E, "E"), (W, "W") })
        {
            const double yb = 136.6, mid = 65, bottom = 131.5;
            Arm(k, $"Miner Arm {nm}", fr, 13, 105, yb, [(mid - Magpie.L / 2, mid + Magpie.L / 2)], true, 50);
            foreach (var side in new[] { -1, 1 })
            {
                var sn = side < 0 ? "L" : "R";
                Cradle(k, $"Miner Cradle {nm}{sn} 1", fr, side, mid - 13, yb, bottom, Magpie.W, Magpie.L, Magpie.H, false);
                Cradle(k, $"Miner Cradle {nm}{sn} 2", fr, side, mid, yb, bottom - 4, Magpie.W, Magpie.L, Magpie.H, true);
                Cradle(k, $"Miner Cradle {nm}{sn} 3", fr, side, mid + 13, yb, bottom, Magpie.W, Magpie.L, Magpie.H, false);
            }
            Gantry(k, $"Miner Gantry {nm}", fr, mid - 1, yb, ArmHalf + 3.5 + Magpie.W + 3, bottom + Magpie.H + 3);
            OreChute(k, $"Ore Chute {nm}", fr, 27, 30, 104, yb);
        }
        Spine(k, "Spine 4", 149, 168, 4);

        // the frigate tier: a collar with arms north and south, two Zero Day berths on each, two repair gantries an arm
        Block(k, "Frigate Collar", 15, 168, 190, new BlockOpts(Seed: 61, Job: "the frigates' collar",
            Rooms: new()
            {
                [0] = [new(-12, 6, 12, 12, "Ammunition store", 's'), new(-12, -12, 12, -6, "Fuel control", 'n')],
                [1] = [new(-12, 6, 0, 12, "Crew lounge", 's'), new(0, 6, 12, 12, "Briefing", 's'), new(-12, -12, 12, -6, "Bunks", 'n')],
                [2] = [new(-12, 8, -3, 12, "Airlock N", 's'), new(3, 8, 12, 12, "Dockmaster", 's'), new(-12, -12, -3, -8, "Airlock S", 'n'), new(3, -12, 12, -8, "Medbay", 'n'), new(-12, -8, -8, 8, "Suit room", 'e'), new(8, -8, 12, 8, "Feed control", 'w')],
                [3] = [new(-12, 6, 12, 12, "Repair control", 's'), new(-12, -12, 12, -6, "Spares", 'n')],
                [4] = [new(-12, 6, 12, 12, "Armoury", 's'), new(-12, -12, 12, -6, "Security", 'n')],
            }));
        foreach (var (fr, nm) in new[] { (N, "N"), (S, "S") })
        {
            const double yb = 175.6, mid = 80, bottom = 172;
            Arm(k, $"Frigate Arm {nm}", fr, 15, 130, yb, [(mid - ZeroDay.L / 2, mid + ZeroDay.L / 2)], false, 70);
            foreach (var side in new[] { -1, 1 })
            {
                var sn = side < 0 ? "L" : "R";
                Cradle(k, $"Frigate Cradle {nm}{sn} 1", fr, side, mid - 18, yb, bottom, ZeroDay.W, ZeroDay.L, ZeroDay.H, false);
                Cradle(k, $"Frigate Cradle {nm}{sn} 2", fr, side, mid, yb, bottom - 4, ZeroDay.W, ZeroDay.L, ZeroDay.H, true);
                Cradle(k, $"Frigate Cradle {nm}{sn} 3", fr, side, mid + 18, yb, bottom, ZeroDay.W, ZeroDay.L, ZeroDay.H, false);
            }
            var span = ArmHalf + 3.5 + ZeroDay.W + 3;
            Gantry(k, $"Frigate Gantry {nm}1", fr, mid - 15, yb, span, bottom + ZeroDay.H + 3);
            Gantry(k, $"Frigate Gantry {nm}2", fr, mid + 9, yb, span, bottom + ZeroDay.H + 3);
        }
        Spine(k, "Spine 5", 190, 200, 5);

        // the commons: a hex concourse on the spine, the hub tower over it and six towers round it, varied in size, height, setback and
        // look, with landing pads for small craft on the towers' roofs and sky bridges to the hub
        HexBlock(k, "Concourse", 0, 0, 52, 200, 205, new HexOpts("the concourse", 81, Decks: false, Fit: "plaza", Greebles: false, WinCol: LampCol));
        const double hubR = 20, hubTop = 249;
        HexBlock(k, "Commons Hub", 0, 0, hubR, 205, hubTop, new HexOpts("the commons hub", 83, Rooms: HubRooms, Keep: 6, Antenna: false));
        var towers = new (string Mod, string Job, double R, double Top, double UpR, double UpTop, string Fit, string Win, string BandC, char Style)[]
        {
            ("Hostel", "the hostel tower", 12, 236, 0, 0, "bunks", LampCol, StationLook.Band, 's'),
            ("Hydro", "the hydroponics tower", 10, 222, 0, 0, "hydro", "#b8f5a8", Container4, 'b'),
            ("Market", "the market tower", 13.5, 222, 10, 244, "stalls", Window, Hazard, 's'),
            ("Clinic", "the clinic tower", 11, 228, 0, 0, "beds", "#d8f0ff", Container2, 'v'),
            ("Exchange", "the exchange tower", 13, 228, 9.5, 240, "desks", Window, Container1, 's'),
            ("Canteen", "the canteen tower", 10.5, 218, 0, 0, "tables", "#ff7ab8", Frame, 'b'),
        };
        for (var i = 0; i < towers.Length; i++)
        {
            var t = towers[i];
            var th = (30 + 60 * i) * Math.PI / 180;
            double dx = Math.Cos(th), dz = Math.Sin(th), d = hubR * Cos30 + 3.5 + t.R * Cos30;
            double cx = Math.Round(d * dx, 2), cz = Math.Round(d * dz, 2);
            var seed = 120 + i * 10;
            double PadFor(double r) => Math.Min(6, Math.Round(0.634 * r - 0.5, 1));
            var lowTop = t.Top;
            HexBlock(k, t.Mod, cx, cz, t.R, 205, lowTop, new HexOpts(t.Job, seed, Fit: t.Fit, WinCol: t.Win, BandCol: t.BandC, WinStyle: t.Style, CoreHalf: 2.2,
                PadR: t.UpR > 0 ? 0 : PadFor(t.R), Keep: t.UpR > 0 ? t.UpR + 0.5 : 0, Antenna: t.UpR == 0 && i % 2 == 0));
            if (t.UpR > 0)
                HexBlock(k, t.Mod + " Top", cx, cz, t.UpR, lowTop, t.UpTop, new HexOpts(t.Job + "'s upper storeys", seed + 5, Fit: t.Fit, WinCol: t.Win, BandCol: t.BandC,
                    WinStyle: t.Style == 's' ? 'v' : 's', CoreHalf: 2.2, PadR: PadFor(t.UpR), Antenna: true));
            var walls = k.Pieces.First(q => (string)q.D["name"]! == $"Highport {t.Mod} Walls");
            var by = Math.Min(lowTop, hubTop) - 5.5;
            SkyBridge(walls, (hubR * Cos30 - 0.1) * dx, (hubR * Cos30 - 0.1) * dz, cx - (t.R * Cos30 - 0.1) * dx, cz - (t.R * Cos30 - 0.1) * dz, by);
        }
        Spine(k, "Spine 6", hubTop, 263, 6);
        Block(k, "Crown", 12, 263, 287, new BlockOpts(Seed: 101, Job: "the ops crown", Keep: 2.5, Roof: (p, y) => { Pdc(p, 9.5, y, 9.5); Pdc(p, -9.5, y, -9.5); },
            Rooms: new()
            {
                [0] = [new(-9, 6, 9, 9, "Barracks", 's'), new(-9, -9, 9, -6, "Barracks", 'n')],
                [1] = [new(-9, 6, 9, 9, "Armoury", 's'), new(-9, -9, 9, -6, "PDC magazine", 'n')],
                [2] = [new(-9, 6, 9, 9, "Radar room", 's'), new(-9, -9, 9, -6, "Comms", 'n')],
                [3] = [new(-9, 6, 9, 9, "Bridge", 's'), new(-9, -9, 0, -6, "Traffic control", 'n'), new(0, -9, 9, -6, "Captain", 'n'), new(-9, -6, -6, 6, "Briefing", 'e'), new(6, -6, 9, 6, "Plot", 'w')],
                [4] = [new(-9, 6, 9, 9, "Mast access", 's')],
            }));
        Band(k, "Crown Band", 12, 2, 275.2, 279.4);
        Mast(k, "Mast", 287, 313);
        return k;
    }

    /// <summary>The commons hub's rooms a level: north, south, west and east of the lift core.</summary>
    private static string[] HubRooms(int level) => level switch
    {
        0 => ["Customs hall", "Arrivals lounge", "Quarantine", "Cargo inspection"],
        1 => ["Market hall", "Traders", "Exchange", "Bonded store"],
        2 => ["Market gallery", "Brokers", "Stalls", "Contracts"],
        3 => ["Canteen", "Galley", "Bar", "Cold store"],
        4 => ["Clinic", "Wards", "Pharmacy", "Chapel"],
        5 => ["Station security", "Cells", "Armoury", "Records"],
        6 => ["Admin", "Harbour office", "Meeting room", "Archive"],
        7 or 8 => ["Bunks", "Bunks", "Washroom", "Washroom"],
        _ => ["Harbourmaster", "Traffic office", "Comms", "Plant room"],
    };

    /// <summary>The warehouse floor's stock: rows of 20-foot containers two high, and the overhead crane's rails along the walls.</summary>
    private static void Containers(Part p, double f)
    {
        string[] cols = [Container1, Container2, Container3, Container4, Plate2];
        var n = 0;
        foreach (var z in new[] { -17.0, -12.5, 10.0, 14.5 })
            for (var x = -21.0; x + 6.06 < 21.5; x += 7)
                for (var tier = 0; tier < 2; tier++)
                {
                    if (Rnd(77, n) < 0.2 && tier == 1)
                    {
                        n++;
                        continue;
                    }
                    var y = f + tier * 2.62;
                    Detail(p, x, y + 0.01, z, x + 6.06, y + 2.6, z + 2.44, cols[(int)(Rnd(78, n) * cols.Length)], Worn);
                    n++;
                }
        foreach (var x in new[] { -21.5, 21.5 })
            Detail(p, x - 0.4, 105.5, -22, x + 0.4, 106.2, 22, Frame, Steel);
        Detail(p, -21.9, 106.2, -3, 21.9, 107.4, -1, Hazard, HazardFin);
        Detail(p, -1.5, 104.0, -3.2, 1.5, 106.2, -0.8, Frame, Dirty);
    }

    // ---- the base's pieces

    private static void Thrusters(Kit k, string name, double h, double y0, double y1)
    {
        var p = k.Piece(name, "The thrust block under the reactor: four station-keeping drives that hold the station in its orbit.", Frame);
        Solid(p, -h, y0, -h, h, y1, h, Plate2, Plated);
        foreach (var (sx, sz) in Corners)
        {
            double x = sx * 6, z = sz * 6;
            Cyl(p, 'y', x, 0, z, 1.7, y0 - 2.5, y0, Frame, Soot);
            Shaped(p, x - 3, y0 - 10, z - 3, x + 3, y0 - 2.5, z + 3, Truss, Soot, "tube", "+y", ("inner", 0.86), ("ends", "open"));
            Lamp(p, x - 1.2, y0 - 9.2, z - 1.2, x + 1.2, y0 - 8.9, z + 1.2, Glow);
            Detail(p, x - 3.1, y0 - 3.2, z - 3.1, x + 3.1, y0 - 2.6, z + 3.1, Hazard, HazardFin);
        }
        foreach (var (sx, sz) in Corners)
            Lamp(p, sx * (h - 0.4) - 0.2, y1 - 0.4, sz * (h + 0.01) - (sz < 0 ? 0.1 : 0), sx * (h - 0.4) + 0.2, y1 - 0.1, sz * (h + 0.01) + (sz < 0 ? 0 : 0.1), sx < 0 ? NavRed : NavGreen);
        Finish(p);
    }

    private static void Reactor(Kit k, string name, double h, double y0, double y1, double plugH, double plugTop)
    {
        var p = k.Piece(name, "The reactor, sealed under a shield plug below engineering: coolant risers up its faces, hazard bands, its vents glowing.", Frame);
        Solid(p, -h, y0, -h, h, y1, h, "#5a4a44", Painted);
        Solid(p, -plugH, y1, -plugH, plugH, plugTop, plugH, Plate2, Plated);
        foreach (var s in new[] { -1, 1 })
        {
            for (var u = -h + 2; u < h - 1; u += 4)
            {
                Cyl(p, 'y', u, 0, s * (h + 0.5), 0.45, y0 + 0.5, y1 - 0.2, Tank, Rusty);
                Cyl(p, 'y', s * (h + 0.5), 0, u, 0.45, y0 + 0.5, y1 - 0.2, Tank, Rusty);
            }
            Detail(p, -h, y0 + 7, s < 0 ? -h - 0.03 : h, h, y0 + 8.2, s < 0 ? -h : h + 0.03, Hazard, HazardFin);
            Detail(p, s < 0 ? -h - 0.03 : h, y0 + 7, -h, s < 0 ? -h : h + 0.03, y0 + 8.2, h, Hazard, HazardFin);
            Detail(p, -h + 1.5, y0 + 2, s < 0 ? -h - 0.025 : h, h - 1.5, y0 + 2.6, s < 0 ? -h : h + 0.025, Glow, glow: true);
            Detail(p, s < 0 ? -h - 0.025 : h, y0 + 2, -h + 1.5, s < 0 ? -h : h + 0.025, y0 + 2.6, h - 1.5, Glow, glow: true);
        }
        Detail(p, -plugH, plugTop - 0.5, -plugH - 0.03, plugH, plugTop - 0.1, -plugH, Hazard, HazardFin);
        Detail(p, -plugH, plugTop - 0.5, plugH, plugH, plugTop - 0.1, plugH + 0.03, Hazard, HazardFin);
        Finish(p, 800);
    }

    /// <summary>An ore chute from a refinery's roof up to a miners' arm's underside, at along a0 to a1 (a square duct with bands).</summary>
    private static void OreChute(Kit k, string name, ArmFrame fr, double a0, double a1, double y0, double y1)
    {
        var p = k.Piece(name, "An ore chute from the miners' arm down to a refinery: a square duct with bands, inspection hatches and a lamp.", Tank);
        fr.Solid(p, a0, a1, -1.5, 1.5, y0, y1, Tank, Rusty);
        for (var y = y0 + 3; y < y1 - 1; y += 5)
            fr.Detail(p, a0 - 0.15, a1 + 0.15, -1.65, 1.65, y, y + 0.4, Frame, Dirty);
        fr.Detail(p, a0 + 0.6, a1 - 0.6, 1.5, 1.53, y0 + 8, y0 + 9.4, Hazard, HazardFin);
        fr.Detail(p, a0 + 1, a1 - 1, 1.5, 1.56, y1 - 3, y1 - 2.6, LampCol, glow: true);
        Finish(p, 200);
    }

    // ---- welding the pieces together

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    /// <summary>A facing socket pair wherever two pieces' solid boxes touch face to face (one pair a pair of pieces, on their biggest
    /// contact); throws where two pieces' solids overlap, which the game would refuse.</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var bounds = solid.Select(l => (Mn: Enumerable.Range(0, 3).Select(a => l.Min(b => b.Mn[a])).ToArray(), Mx: Enumerable.Range(0, 3).Select(a => l.Max(b => b.Mx[a])).ToArray())).ToList();
        var count = new int[parts.Count];
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var (bi, bj) = (bounds[i], bounds[j]);
                if (Enumerable.Range(0, 3).Any(a => bi.Mn[a] > bj.Mx[a] + 0.002 || bj.Mn[a] > bi.Mx[a] + 0.002))
                    continue;
                (double Area, double[] At, int Axis, bool IPlus) best = (0, [], 0, false);
                foreach (var A in solid[i])
                    foreach (var B in solid[j])
                    {
                        if (Enumerable.Range(0, 3).All(a => Math.Min(A.Mx[a], B.Mx[a]) - Math.Max(A.Mn[a], B.Mn[a]) > 0.01))
                            throw new InvalidOperationException($"space_station: {parts[i].D["name"]} and {parts[j].D["name"]} overlap at [{string.Join(", ", A.Mn.Select(v => v.ToString("0.##")))}]");
                        for (var ax = 0; ax < 3; ax++)
                        {
                            bool iPlus = Math.Abs(A.Mx[ax] - B.Mn[ax]) < 0.001, iMinus = Math.Abs(A.Mn[ax] - B.Mx[ax]) < 0.001;
                            if (!iPlus && !iMinus)
                                continue;
                            var at = new double[3];
                            var area = 1.0;
                            var ok = true;
                            for (var a = 0; a < 3 && ok; a++)
                            {
                                if (a == ax)
                                {
                                    at[a] = iPlus ? A.Mx[a] : A.Mn[a];
                                    continue;
                                }
                                double lo = Math.Max(A.Mn[a], B.Mn[a]), hi = Math.Min(A.Mx[a], B.Mx[a]);
                                if (hi - lo < 0.03)
                                    ok = false;
                                at[a] = Math.Round((lo + hi) / 2, 2);
                                area *= hi - lo;
                            }
                            if (ok && area > best.Area)
                                best = (area, at, ax, iPlus);
                        }
                    }
                if (best.Area <= 0)
                    continue;
                var axis = "xyz"[best.Axis];
                Sock(parts[i], $"j{count[i]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "+" : "-") + axis);
                Sock(parts[j], $"j{count[j]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "-" : "+") + axis);
            }
    }

    private static bool Meet(Part a, Part b)
    {
        foreach (var s in a.Sockets)
            foreach (var t in b.Sockets)
            {
                var (pa, pb) = ((List<object?>)s["at"]!, (List<object?>)t["at"]!);
                if (Enumerable.Range(0, 3).All(i => Math.Abs(Convert.ToDouble(pa[i]) - Convert.ToDouble(pb[i])) < 0.004))
                {
                    var (da, db) = ((string)s["dir"]!, (string)t["dir"]!);
                    if (da[1] == db[1] && da[0] != db[0])
                        return true;
                }
            }
        return false;
    }

    /// <summary>The order to place the pieces in: out from the first over the socket pairs they meet at.</summary>
    private static List<string> PlaceOrder(List<Part> parts, string first)
    {
        var names = parts.Select(q => (string)q.D["name"]!).ToList();
        var seen = new HashSet<int> { names.IndexOf(first) };
        var order = new List<string> { first };
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            for (var j = 0; j < parts.Count; j++)
                if (!seen.Contains(j) && Meet(parts[i], parts[j]))
                {
                    seen.Add(j);
                    order.Add(names[j]);
                    queue.Enqueue(j);
                }
        }
        if (order.Count != parts.Count)
            throw new InvalidOperationException("space_station: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    /// <summary>
    /// Moves a piece's origin from the station's to the middle of its own boxes (whole metres, so cells stay aligned): the game refuses
    /// a box more than 200 m from its part's origin, and a station is taller than that. Returns where the origin went (station frame),
    /// which is where the blueprint puts the piece.
    /// </summary>
    private static double[] Recentre(Part p)
    {
        var all = p.Boxes.Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList();
        double[] o = [.. Enumerable.Range(0, 3).Select(a => Math.Round((all.Min(b => b.Mn[a]) + all.Max(b => b.Mx[a])) / 2))];
        List<object?> Shift(List<object?> v) => [.. v.Select((x, a) => (object?)R(Convert.ToDouble(x) - o[a]))];
        foreach (var b in p.Boxes)
        {
            b["min"] = Shift((List<object?>)b["min"]!);
            b["max"] = Shift((List<object?>)b["max"]!);
            if (b.Get("points") is List<object?> pts)
                b["points"] = pts.Select(q => (object?)Shift((List<object?>)q!)).ToList();
        }
        foreach (var s in p.Sockets)
            s["at"] = Shift((List<object?>)s["at"]!);
        return o;
    }

    private static PyDict Blueprint(string name, string desc, Kit kit, List<string> order, Dictionary<string, double[]> origins, Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint(name, desc, defs, maxSteps: 800);
        var placed = new Dictionary<string, int>();
        foreach (var piece in order)
            placed[piece] = placed.Count == 0 ? bp.First(piece) : bp.Put(piece, Sub3(origins[piece], origins[order[0]]), 0);
        foreach (var h in kit.Hangs)
        {
            var step = bp.HangTurned(h.Part, placed[h.Piece], h.Sock, h.By, BuildingParts.R0);
            if (h.Config != null)
                bp.Steps[step]["config"] = h.Config;
            if (h.Paint != null)
                bp.Steps[step]["paint"] = h.Paint;
            if (h.Finish != null)
                bp.Steps[step]["finish"] = h.Finish;
        }
        return bp.Json();
    }

    private static double[] Sub3(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    public void Make(PartOutput o)
    {
        var kit = Highport();
        Connect(kit.Pieces);
        var order = PlaceOrder(kit.Pieces, "Highport Thrust Block");
        var origins = kit.Pieces.ToDictionary(p => (string)p.D["name"]!, Recentre);
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in kit.Pieces)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/Highport/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/CaledonHighport.json", PyJson.Dumps(Blueprint(HighportName,
            "Caledon Highport, the first space station: a 310 m tower with gravity down its spine. A hex commons of seven towers with landing pads for small craft on their roofs, Zero Day berths on arms north and south, Magpie berths on arms east and west with ore chutes to the refineries, Dray aprons with a container lift into the warehouse; the ops crown above them, the tank farm, engineering, the reactor and its radiators below.",
            kit, order, origins, defs), 2) + "\n");
    }
}
