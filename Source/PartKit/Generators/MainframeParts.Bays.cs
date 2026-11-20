using System.Numerics;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class MainframeParts
{
    // ---- What the skin is cut for (docs/sandbox/constructs/mainframe.md "Zones")

    /// <summary>The six drives through the transom (x, y: each bell's axis) and the openings the Laser Cutter cuts for them (m across).</summary>
    private static readonly (double X, double Y)[] Drives = [(-42, 30), (0, 30), (42, 30), (-42, 74), (0, 74), (42, 74)];

    private const double DriveHole = 28;

    /// <summary>The link-blue strips along the beam line (own part) and how long each is.</summary>
    private const string LightName = "Mainframe Beam Light";

    private const double LightLen = 10;

    /// <summary>The hangar's three pads (x) in a row across its middle, the openings over them in the spine (m) and its roof doors.</summary>
    private static readonly double[] PadX = [-20, 0, 20];

    private const double PadZ = 360, RoofHoleX = 10, RoofHoleZ = 18;

    /// <summary>The Limpets' pockets in the lower flanks (z), each Limpet's middle out from the centreline, the pockets' floor, and the
    /// pods' belly (its roof 12 cm under the boarding deck, as under the Zero Day's keel).</summary>
    private static readonly double[] PocketZ = [330, 390];

    private const double LimpetX = 46, PocketFloor = 11.6, PocketHalfZ = 3.2, PodH = 2.35;

    private static double PodBase => BoardingDeck - Thick(DeckH) - 0.002 - 0.12 - PodH;

    /// <summary>The Limpet's roof receiver from its middle, along its own z (aft), and its height: the docking hatch over it.</summary>
    private const double RecvAft = 2.05;

    /// <summary>The Zero Day's berth: its walls (half width, fore and aft) and the gap in the keel under it.</summary>
    private const double BerthX = 13, BerthZ0 = 436, BerthZ1 = 524, BerthGapX = 11.5, BerthGapZ0 = 441, BerthGapZ1 = 519, BerthMid = 480;

    /// <summary>The missile fields' cell blocks: their middle across, the first rack's z, the missiles' tops; each block ten racks of eight cells.</summary>
    private static readonly (double X, double Z0, double Top)[] VlsBlocks = [(-16, 251, 94.3), (16, 251, 94.3), (-16, 731, 98.9), (16, 731, 98.9)];

    private const int VlsRacks = 10, VlsCells = 8;
    private const double VlsPitch = 3;
    private const string RackKey = "Ord Rack (1x8 grid 500x5500)";

    /// <summary>The missile decks under the cell blocks: (z0, z1, top).</summary>
    private static readonly (double Z0, double Z1, double Y)[] MissileDecks = [(244, 286, 87.5), (724, 764, 92.2)];

    /// <summary>The point-defence turrets along the spine's edges.</summary>
    private static readonly double[] PdcZ = [330, 450, 570, 690, 810];

    private const double PdcX = 29;

    /// <summary>The cell hatch's size: along the ship (its hinge on its fore edge) and across.</summary>
    private const double CellHatchL = 1.3, CellHatchW = 4.8;

    // ---- The skin's openings

    /// <summary>The openings the Laser Cutter cuts in a facet's plates (cm in its plane): airlocks in the belts, Limpet pockets in the lower
    /// flanks, the hangar's roof, the missile cells and the turrets' wells in the spine.</summary>
    private static List<Hole> SkinHoles(Facet f)
    {
        var holes = new List<Hole>();
        var pl = f.Plane;
        if (f.Band == 4 && f.F is 1 or 7)
        {
            var s = f.F == 1 ? 1 : -1;
            foreach (var z in AirlockZs())
            {
                var (v, u) = pl.Of(new D3(s * HalfWidth(z, HatchY), HatchY, z));
                holes.Add(new Hole(u, v, 130, 130, 20));
            }
        }
        if (f.Band == 4 && f.F is 0 or 8)
        {
            var s = f.F == 0 ? 1 : -1;
            foreach (var z in PocketZ)
            {
                double y0 = PocketFloor + 0.12, y1 = BoardingDeck - 0.15, za = z - PocketHalfZ + 0.35, zb = z + PocketHalfZ - 0.35;
                var pts = new[] { (y0, za), (y0, zb), (y1, za), (y1, zb) }.Select(q => pl.Of(new D3(s * HalfWidth(q.Item2, q.Item1), q.Item1, q.Item2))).ToList();
                double v0 = pts.Min(p => p.V), v1 = pts.Max(p => p.V), u0 = pts.Min(p => p.U), u1 = pts.Max(p => p.U);
                holes.Add(new Hole((u0 + u1) / 2, (v0 + v1) / 2, u1 - u0, v1 - v0, 40));
            }
        }
        if (f.F == 4)
        {
            foreach (var x in PadX)
                if (f.Band == 4)
                    holes.Add(Spot(pl, x, PadZ, RoofHoleZ * 100, RoofHoleX * 100, 150));
            foreach (var (x, z0, _) in VlsBlocks)
                if (BandOf(z0) == f.Band)
                    for (var i = 0; i < VlsRacks; i++)
                        holes.Add(Spot(pl, x, z0 + i * VlsPitch, 100, 460, 15));
            if (f.Band == 4)
                foreach (var z in PdcZ)
                    foreach (var s in (ReadOnlySpan<int>)[1, -1])
                        holes.Add(Spot(pl, s * PdcX, z, 120, 120, 20));
        }
        return holes;
    }

    /// <summary>An opening in the spine's plane over ship point (x, z): A along, B across (cm).</summary>
    private static Hole Spot(Plane pl, double x, double z, double a, double b, double c)
    {
        var (v, u) = pl.Of(new D3(x, Heights(z).Spine, z));
        return new Hole(u, v, a, b, c);
    }

    /// <summary>The piece of a facet left out where the skin opens wider than a sheet: the berth's mouth in the keel under the Zero Day.</summary>
    private static (double V0, double V1, double U0, double U1)? SkinGap(Facet f)
    {
        if (f.Band != 4 || f.F != 9)
            return null;
        var (va, ua) = f.Plane.Of(new D3(-BerthGapX, 0, BerthGapZ0));
        var (vb, ub) = f.Plane.Of(new D3(BerthGapX, 0, BerthGapZ1));
        return (Math.Min(va, vb), Math.Max(va, vb), Math.Min(ua, ub), Math.Max(ua, ub));
    }

    /// <summary>The hangar deck's own openings (its lift shafts are cut by <see cref="Floor"/>).</summary>
    private static List<Hole> HangarDeckHoles() => [];

    /// <summary>The boarding deck's hatchways down into the Limpets' roofs.</summary>
    private static List<Hole> BoardingDeckHoles()
    {
        var holes = new List<Hole>();
        foreach (var s in (ReadOnlySpan<int>)[1, -1])
            foreach (var z in PocketZ)
            {
                var x = s * (LimpetX - RecvAft);
                holes.Add(LevelHole(x - 0.65, x + 0.65, z - 0.65, z + 0.65, 15));
            }
        return holes;
    }

    // ---- The citadel's rooms

    /// <summary>
    /// The citadel: decks of rooms over the main deck, each storey a corridor down the middle with rooms either side (the crew forward, the
    /// marines aft of them, flight control and stores under the hangar, command over the berth with the bridge on its top storey).
    /// </summary>
    private static void Citadel(Writer w)
    {
        Room L = R("lift", 6.6);
        // The crew's decks (90-160 m).
        Storey(w, ZCrew, ZMarines, MainDeck, [L, R("mess", 18), R("galley", 10), R("stores", 8), R("heads", 8), R("rec", 0)], [R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("heads", 8), R("medbay", 0)]);
        Storey(w, ZCrew, ZMarines, UpperDeck, [L, R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 0)], [R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("heads", 0)]);
        Storey(w, ZCrew, ZMarines, TopDeck, [L, R("officer", 8), R("officer", 8), R("officer", 8), R("officer", 8), R("office", 12), R("rec", 0)], [R("officer", 8), R("officer", 8), R("officer", 8), R("officer", 8), R("heads", 8), R("briefing", 0)]);
        // The marines' decks (160-230 m).
        Storey(w, ZMarines, ZMissilesF, MainDeck, [L, R("armoury", 12), R("armoury", 12), R("workshop", 14), R("ready", 0)], [R("ready", 16), R("suits", 12), R("brig", 18), R("stores", 0)]);
        Storey(w, ZMarines, ZMissilesF, UpperDeck, [L, R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("heads", 0)], [R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("heads", 0)]);
        Storey(w, ZMarines, ZMissilesF, TopDeck, [L, R("mess", 16), R("galley", 10), R("heads", 8), R("rec", 0)], [R("medbay", 16), R("briefing", 14), R("office", 12), R("stores", 0)]);
        // Under the hangar (300-420 m): flight control, the boarding parties' rooms, stores; a lift at each end to port.
        Storey(w, ZHangar, ZCommand, MainDeck, [L, R("flight", 14), R("suits", 12), R("ready", 16), R("armoury", 12), R("workshop", 16), R("stores", 12), R("heads", 8), R("lifesupport", 0)],
            [L, R("workshop", 16), R("stores", 14), R("suits", 12), R("ready", 16), R("briefing", 14), R("office", 12), R("heads", 8), R("stores", 14.8), R("lift", 0)]);
        // Command over the berth (420-580 m): the CIC and servers, berthing, the bridge on top.
        Storey(w, ZCommand, ZVault, MainDeck, [L, R("cic", 24), R("servers", 12), R("office", 12), R("briefing", 14), R("stores", 12), R("heads", 8), R("lifesupport", 14), R("workshop", 0)],
            [R("servers", 12), R("office", 12), R("ready", 14), R("armoury", 12), R("medbay", 18), R("heads", 8), R("stores", 12), R("lifesupport", 14), R("stores", 0)]);
        Storey(w, ZCommand, ZVault, UpperDeck, [L, R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("heads", 8), R("mess", 18), R("galley", 10), R("rec", 0)],
            [R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("berth", 10), R("heads", 8), R("medbay", 16), R("office", 0)]);
        Storey(w, ZCommand, ZVault, TopDeck, [L, R("bridge", 24), R("office", 12), R("officer", 8), R("officer", 8), R("officer", 8), R("officer", 8), R("heads", 8), R("rec", 0)],
            [R("cic", 20), R("officer", 8), R("officer", 8), R("officer", 8), R("officer", 8), R("briefing", 14), R("office", 12), R("servers", 0)]);
    }

    // ---- The bays

    private static void Bays(Writer w, Dictionary<(int, int), Plane> planes)
    {
        Hangar(w);
        Boarding(w);
        Berth(w);
        Vault(w);
        Yard(w);
        MissileFields(w, planes);
        Reactors(w);
        EngineRoom(w);
        DriveTrains(w);
        Bunkers(w);
        Rcs(w, planes);
        Pdcs(w);
        Sensors(w, planes);
    }

    /// <summary>The turn that stands a part upright facing <paramref name="facing"/> (its +z there).</summary>
    private static byte Upright(byte facing) => TurnYZ(PY, facing);

    private static byte Opp(byte d) => (byte)(d ^ 1);

    /// <summary>
    /// The hangar over the boarding deck (300-420 m, its deck on the beam line, its roof the spine): three Shuttles on pads in a row
    /// across, each held by a Docking Clamp beside its stern as on the Zero Day's cradle, under an opening in the roof whose door has slid
    /// aft along the spine; floodlights on the bulkheads.
    /// </summary>
    private static void Hangar(Writer w)
    {
        var shuttle = CraftOf("Shuttle.json");
        foreach (var x in PadX)
        {
            Stand(w, "Bay Marking", x, HangarDeck, PadZ);
            foreach (var k in (ReadOnlySpan<int>)[-1, 1])
                Stand(w, "Lane Marking", x, HangarDeck, PadZ + k * 8.5);
            // The Shuttle on the pad, its soles on the deck; its clamp beside its stern, the box round it (its centre of mass in it).
            var at = new Vector3((float)x, (float)(HangarDeck + 0.005), (float)PadZ);
            double cx = x - 1.3, cy = HangarDeck + 0.5, cz = PadZ + 5.6;
            Stand(w, "Docking Clamp Medium", cx, cy, cz, 0, default, new PyDict
            {
                ["startOn"] = true,
                ["sizeX"] = 4.5, ["sizeY"] = 3.5, ["sizeZ"] = 10.5,
                ["atX"] = Py.Round(at.X + shuttle.Com.X - cx, 2), ["atY"] = Py.Round(at.Y + shuttle.Com.Y - cy, 2), ["atZ"] = Py.Round(at.Z + shuttle.Com.Z - cz, 2),
            });
            w.Carried.Add(("Shuttle", B(at.X, at.Y, at.Z), 0));
            // The roof door: a Hangar Door Large lying on the spine, slid aft off its opening (Turn: its x aft, its y to starboard, its face up).
            var top = Heights(PadZ).Spine + Thick(SkinH) / 2;
            w.Root("Hangar Door Large 20x12", B(x - 4, top + 0.464 + 0.012, PadZ + RoofHoleZ / 2 + 4), Turn(PZ, PX), new Look(Armour, "plated steel"));
        }
        // Floodlights on the bulkheads fore and aft, high up, shining into the hangar.
        foreach (var (z, face) in new[] { (ZHangar, PZ), (ZCommand, NZ) })
            foreach (var x in (ReadOnlySpan<double>)[-40, -14, 14, 40])
            {
                var back = z + (face == PZ ? 1 : -1) * (Thick(BulkheadH) / 2 + 0.5 + 0.012);
                w.Root("Floodlight Medium", B(x, 72, back), Upright(Opp(face)), new Look(Frame, "dirty steel"));
            }
        // Light strips along the deck's edges, every 12 m.
        for (var z = ZHangar + 8; z < ZCommand - 6; z += 12)
            foreach (var s in (ReadOnlySpan<int>)[1, -1])
                Stand(w, "Lane Marking", s * 52, HangarDeck, z, Turn(PZ, PY));
    }

    /// <summary>
    /// The boarding deck under the hangar: the boarding parties' rooms down its middle under a ceiling, and four Limpets in pockets in the
    /// lower flanks, each hanging under the deck from a clamp with a docking hatch over its roof's receiver, nose out to its opening.
    /// </summary>
    private static void Boarding(Writer w)
    {
        var ceil = BoardingDeck + Storey4;
        Slab(w, ceil, -CitadelX - 0.06, CitadelX + 0.06, ZHangar + 0.07, ZCommand - 0.07, ShaftHoles(ceil, -CitadelX, CitadelX, ZHangar, ZCommand), CeilLook);
        Storey(w, ZHangar, ZCommand, BoardingDeck, [R("lift", 6.6), R("suits", 12), R("ready", 16), R("armoury", 12), R("ready", 16), R("suits", 12), R("briefing", 14), R("medbay", 14), R("stores", 0)],
            [R("ready", 16), R("suits", 12), R("armoury", 12), R("brig", 18), R("workshop", 14), R("stores", 12), R("heads", 8), R("lifesupport", 0)]);
        var limpet = CraftOf("Limpet.json");
        foreach (var s in (ReadOnlySpan<int>)[1, -1])
            foreach (var z in PocketZ)
                Pocket(w, s, z, limpet);
    }

    /// <summary>One Limpet's pocket: walls fore, aft and inboard, a floor, all cut along the flank's slant; the Limpet itself.</summary>
    private static void Pocket(Writer w, int s, double z, Craft limpet)
    {
        var under = BoardingDeck - Thick(DeckH) - 0.012;
        double xin = LimpetX - 3.7;
        var look = new Look(Bay, "painted steel");
        foreach (var dz in (ReadOnlySpan<double>)[-PocketHalfZ, PocketHalfZ])
        {
            var zz = z + dz;
            var pl = WallAcross(zz, $"Limpet pocket at z {z:0}, side {s}");
            double b0 = HalfWidth(zz, PocketFloor) - 0.1, b1 = HalfWidth(zz, under) - 0.1;
            var poly = s > 0
                ? new List<(double V, double U)> { (xin * 100, PocketFloor * 100), (b0 * 100, PocketFloor * 100), (b1 * 100, under * 100), (xin * 100, under * 100) }
                : [(-b0 * 100, PocketFloor * 100), (-xin * 100, PocketFloor * 100), (-xin * 100, under * 100), (-b1 * 100, under * 100)];
            Plate(w, pl, poly, [], WallH, 0, look);
        }
        WallZ(w, s * (xin - 0.06), z - PocketHalfZ + 0.06, z + PocketHalfZ - 0.06, PocketFloor, under, [], look);
        double f0 = xin - 0.06, f1 = HalfWidth(z, PocketFloor) - 0.12;
        Slab(w, PocketFloor, s > 0 ? f0 : -f1, s > 0 ? f1 : -f0, z - PocketHalfZ + 0.06, z + PocketHalfZ - 0.06, [], new Look(BayFloor, "tread plate"));
        // The Limpet, nose out (its -z to the flank), its roof under the deck; the clamp upside down under the deck beside it.
        var turn = TurnYZ(PY, s > 0 ? NX : PX);
        var at = new Vector3((float)(s * LimpetX), (float)PodBase, (float)z);
        var com = at + Rot24.Apply(turn, limpet.Com);
        double cx = s * LimpetX, cy = under - 0.5 - 0.005, cz = z + 2.1;
        w.Root("Docking Clamp Medium", B(cx, cy, cz), Turn(PX, NY), default, new PyDict
        {
            ["startOn"] = true,
            ["sizeX"] = 6.8, ["sizeY"] = 2.8, ["sizeZ"] = 4.4, // (the clamp's x is the ship's; its y and z are the ship's -y and -z)
            ["atX"] = Py.Round(com.X - cx, 2), ["atY"] = Py.Round(-(com.Y - cy), 2), ["atZ"] = Py.Round(-(com.Z - cz), 2),
        });
        w.Carried.Add(("Limpet", B(at.X, at.Y, at.Z), turn));
        // The hatch on the deck over the Limpet's receiver (it opens with the Limpet's own while the clamp holds it).
        w.Root("Docking Hatch", B(s * (LimpetX - RecvAft), BoardingDeck + 0.003, z), 0, new Look(Armour, "plated steel"));
        Lamp(w, "Bulkhead Light", s * (xin + 1.2), under, z - 2.4);
    }

    /// <summary>
    /// The Zero Day's berth under the command decks: walls round it from the keel up to the main deck, catwalks along its sides at the
    /// height of the frigate's deck, floodlights, the berth's Docking Clamp Large under the main deck (holding what comes into its box) and
    /// the mouth in the keel the frigate comes up through (<see cref="SkinGap"/>).
    /// </summary>
    private static void Berth(Writer w)
    {
        var top = MainDeck - Thick(DeckH) - 0.012;
        var look = new Look(Bay, "painted steel");
        WallX(w, BerthZ0, -BerthX, BerthX, 0.06, top, [], look);
        WallX(w, BerthZ1, -BerthX, BerthX, 0.06, top, [], look);
        foreach (var s in (ReadOnlySpan<int>)[1, -1])
        {
            WallZ(w, s * BerthX, BerthZ0 + 0.06, BerthZ1 - 0.06, 0.06, top, [], look);
            for (var z = BerthZ0 + 1.0; z + 8 <= BerthZ1 - 0.5; z += 8.04)
                Stand(w, "Catwalk 8 m", s * (BerthX - 0.06 - 0.53), 12, z + 0.5, 0, new Look(Grate, "tread plate"));
            for (var z = BerthZ0 + 8; z < BerthZ1 - 4; z += 16)
                w.Root("Floodlight Medium", B(s * (BerthX - 0.06 - 0.5 - 0.012), 22, z), Upright(s > 0 ? NX : PX), new Look(Frame, "dirty steel"));
        }
        // The clamp (the frigate weighs past a Medium's 30 t): its box round where the Zero Day's centre of mass sits when berthed.
        var zd = CraftOf("ZeroDayFrigate.json");
        var (at, _) = BerthedAt(zd);
        var com = at + zd.Com;
        double cx = 0, cy = top - 2.0 - 0.005, cz = BerthMid;
        w.Root(zd.Mass > 300_000 ? "Docking Clamp Capital" : "Docking Clamp Large", B(cx, cy, cz), Turn(PX, NY), default, new PyDict
        {
            ["startOn"] = true,
            ["sizeX"] = 22, ["sizeY"] = 18, ["sizeZ"] = 64,
            ["atX"] = Py.Round(com.X - cx, 2), ["atY"] = Py.Round(-(com.Y - cy), 2), ["atZ"] = Py.Round(-(com.Z - cz), 2),
        });
    }

    /// <summary>Where a Zero Day sits in the berth: its origin (ship metres) with its centreline on the ship's and its middle at
    /// <see cref="BerthMid"/>, its lowest point a metre over the keel; turn 0 (bow forward).</summary>
    private static (Vector3 At, byte Turn) BerthedAt(Craft zd)
    {
        var mid = (zd.Min + zd.Max) * 0.5f;
        return (new Vector3(-mid.X, (float)(1.0 - zd.Min.Y), (float)(BerthMid - mid.Z)), 0);
    }

    /// <summary>The Zero Day the gallery shows in the berth (the game builds the berth empty: a carried craft can't yet carry its own
    /// Shuttle and Limpet, ConstructCargo.BuildCarry). Body metres, as a "carry" entry would say.</summary>
    public static (string Blueprint, Vector3 At, byte Turn) GalleryBerthed
    {
        get
        {
            var (at, turn) = BerthedAt(CraftOf("ZeroDayFrigate.json"));
            return ("Zero Day Frigate", B(at.X, at.Y, at.Z), turn);
        }
    }

    /// <summary>
    /// The shell vault on the main deck over the yard (580-700 m): four racks down its length, Robot Shell Docks both sides of each, every
    /// dock holding a shell.
    /// </summary>
    private static void Vault(Writer w)
    {
        var shellLook = new Look(Frame, "dirty steel");
        foreach (var x in (ReadOnlySpan<double>)[-27, -9, 9, 27])
        {
            WallZ(w, x, 592, 688, MainDeck, MainDeck + 2.9, [], new Look(Frame, "dirty steel"));
            foreach (var s in (ReadOnlySpan<int>)[1, -1])
                for (var z = 593.0; z < 687.5; z += 2.0)
                {
                    var dock = Fit(w, "Robot Shell Dock", MainDeck, s > 0 ? NX : PX, x + s * (Thick(WallH) / 2), z, shellLook, keep: ["dock"]);
                    w.OnSocket(dock, "dock", "Robot Shell", "back", Upright(Opp(Rot24.ApplyDir(w.Rot(dock), PZ))));
                }
        }
        for (var z = 600.0; z < 690; z += 12)
            foreach (var x in (ReadOnlySpan<double>)[-18, 0, 18])
                Lamp(w, "Strip Light", x, CitadelRoof - Thick(DeckH) - 0.012, z, across: true);
    }

    /// <summary>The yard on the keel deck under the vault: the ship's own production line (laser cutters, extruders, a panel former,
    /// assemblers and recyclers), to cut and mend plate as the hull is made of.</summary>
    private static void Yard(Writer w)
    {
        var y = YardDeck;
        Stand(w, "Large Laser Cutter", -17, y + 0.56, 612);
        Stand(w, "Large Laser Cutter", 17, y + 0.56, 612);
        Stand(w, "Large Extruder", -24, y, 646);
        Stand(w, "Large Extruder", 8, y, 646);
        Stand(w, "Medium Panel Former", -4, y, 676);
        Stand(w, "Medium Panel Former", 8, y, 676);
        foreach (var x in (ReadOnlySpan<double>)[-26, -22, 22, 26])
        {
            Stand(w, "Assembler", x, y, 686);
            Stand(w, "Recycler", x, y, 692);
        }
        for (var z = 592.0; z < 700; z += 12)
            foreach (var x in (ReadOnlySpan<double>)[-24, 0, 24])
                Lamp(w, "Strip Light", x, MainDeck - Thick(DeckH) - 0.012, z, across: true);
    }

    // ---- The missile fields

    /// <summary>The cell racks' turn: standing on end, their missiles' noses up (their local -z), the racks' beams aft of them.</summary>
    private static byte VlsTurn => TurnYZ(PZ, NY);

    /// <summary>
    /// The two missile fields: blocks of cells under the spine, each cell an Ordnance Rack's station with a missile on it (the Zero Day's
    /// long-range one), a row of cells under each hatch the Laser Cutter cut its opening for; the hatch swings open before a launch.
    /// </summary>
    private static void MissileFields(Writer w, Dictionary<(int, int), Plane> planes)
    {
        var rack = w.Def(RackKey);
        var missile = w.Def(TorpedoFrigateParts.Torpedo);
        var t = VlsTurn;
        var lug = Writer.SockOf(missile, "lug").At;
        Vector3 L(int i) => Rot24.Apply(t, Writer.SockOf(rack, $"lug{i}").At - lug); // (missile i's origin from the rack's, in the ship's frame)
        float mid = 0;
        for (var i = 0; i < VlsCells; i++)
            mid += L(i).X / VlsCells;
        var top = -missile.BoundsMin.Z; // (its nose over its origin, standing: its local -z up)
        string[] lugs = [.. Enumerable.Range(0, VlsCells).Select(i => $"lug{i}")];
        foreach (var (x, z0, tip) in VlsBlocks)
        {
            var band = BandOf(z0);
            var spine = planes[(band, 4)];
            for (var i = 0; i < VlsRacks; i++)
            {
                var z = z0 + i * VlsPitch;
                var o = L(0);
                var at = new Vector3((float)(x - mid), (float)(tip - top - o.Y), (float)(z - o.Z));
                var r = w.Root(RackKey, B(at.X, at.Y, at.Z), t, new Look(Frame, "dirty steel"), keep: lugs);
                for (var c = 0; c < VlsCells; c++)
                    w.OnSocket(r, $"lug{c}", TorpedoFrigateParts.Torpedo, "lug", t);
                // The hatch over the row: hinged on its fore edge, on the spine.
                var (v, u) = spine.Of(new D3(x, Heights(z).Spine, z));
                OnPlane(w, spine, CellHatch, v, Thick(SkinH) / 2 + 0.004, u - CellHatchL * 50, Turn(NZ, PX), new Look(Armour2, "plated steel"));
            }
        }
    }

    // ---- Power and drive

    /// <summary>The reactor room (790-860 m): four fusion reactors on the main deck, the engineers' consoles down the middle.</summary>
    private static void Reactors(Writer w)
    {
        foreach (var x in (ReadOnlySpan<double>)[-24, 24])
            foreach (var z in (ReadOnlySpan<double>)[808, 842])
                Stand(w, ReactorName, x, MainDeck, z);
        for (var z = 800.0; z < 852; z += 4)
            foreach (var s in (ReadOnlySpan<int>)[1, -1])
                Stand(w, z % 8 < 4 ? "Engineer's Panel" : "Power Board", s * 6, MainDeck, z, Upright(s > 0 ? PX : NX));
        for (var z = 798.0; z < 856; z += 10)
            foreach (var x in (ReadOnlySpan<double>)[-24, 0, 24])
                Lamp(w, "Strip Light", x, ReactorRoof, z, across: true);
        Slab(w, ReactorRoof + Thick(DeckH) + 0.012, -40, 40, ZReactors + 0.07, ZEngines - 0.07, [], CeilLook); // (the reactor room's ceiling, over the reactors' tops)
    }

    /// <summary>The reactor room's ceiling (its underside): clear of the reactors' copper caps, 15.6 m over the main deck.</summary>
    private const double ReactorRoof = 48;

    /// <summary>The fuel bunkers: four Capital Fuel Bunkers (4,000 kl each) stood on the keel under the reactor room, on end so their bands
    /// ring them level. With the drive mounts' header tanks and the RCS blocks' tanks they are the ship's fuel.</summary>
    private static void Bunkers(Writer w)
    {
        foreach (var z in (ReadOnlySpan<double>)[805, 835])
            foreach (var x in (ReadOnlySpan<double>)[-14, 14])
                w.Root("Capital Fuel Bunker", B(x, Heights(z).Keel + Thick(SkinH) / 2 + 8 + 0.004, z), TurnYZ(NZ, PY), new Look(Skin2, "painted plating"));
    }

    /// <summary>Where the attitude jets' blocks stand along the ship: near the bow's end of the forward missile field and aft over the drives.</summary>
    private const double RcsBowZ = 240, RcsSternZ = 955;

    /// <summary>
    /// The attitude jets: a block at each edge of the spine and of the keel, fore and aft (eight), each with a jet out to its side, one
    /// out from the hull (up on the spine, down on the keel) and one fore and one aft. Far apart along the ship they turn it about every
    /// axis; together they slide it, and the fore ones slow it without a flip.
    /// </summary>
    private static void Rcs(Writer w, Dictionary<(int, int), Plane> planes)
    {
        string[] faces = ["+x", "-x", "+y", "+z", "-z"];
        foreach (var z in (ReadOnlySpan<double>)[RcsBowZ, RcsSternZ])
            foreach (var f in (ReadOnlySpan<int>)[4, 9]) // (the spine, the keel)
                foreach (var s in (ReadOnlySpan<int>)[1, -1])
                {
                    var pl = planes[(BandOf(z), f)];
                    var (scale, _) = Scale(z);
                    var (keel, spine) = Heights(z);
                    var x = s * (scale * (f == 4 ? Mid[^1].H : Mid[0].H) - 4.2);
                    var (v, u) = pl.Of(new D3(x, f == 4 ? spine : keel, z));
                    var block = OnPlane(w, pl, RcsBlockName, v, Thick(SkinH) / 2 + 0.004, u, 0, new Look(Armour, "plated steel"), keep: faces);
                    var outward = pl.V.X * s > 0 ? "+x" : "-x"; // (the block's side away from the centreline)
                    foreach (var face in (ReadOnlySpan<string>)[outward, "+y", "+z", "-z"])
                        w.OnSocket(block, face, JetName, "mount", w.FirstTurn(block, face, JetName, "mount"), look: new Look(Frame, "dirty steel"));
                }
    }

    /// <summary>The engine room (860-930 m): capacitor banks and battery banks in rows, the drives' control desks.</summary>
    private static void EngineRoom(Writer w)
    {
        for (var z = 868.0; z < 926; z += 5)
            foreach (var x in (ReadOnlySpan<double>)[-30, -22, -14, 14, 22, 30])
                Stand(w, (int)(z / 5) % 2 == 0 ? "Capacitor Bank L" : "Ship Battery Bank", x, MainDeck, z);
        for (var z = 870.0; z < 924; z += 6)
            foreach (var s in (ReadOnlySpan<int>)[1, -1])
                Stand(w, "Engineer's Panel", s * 6, MainDeck, z, Upright(s > 0 ? PX : NX));
        for (var z = 866.0; z < 928; z += 10)
            foreach (var x in (ReadOnlySpan<double>)[-24, 0, 24])
                Lamp(w, "Strip Light", x, MainDeck + 8, z, across: true);
        Slab(w, MainDeck + 8 + 0.012 + Thick(DeckH), -40, 40, ZEngines + 0.07, ZDrives - 0.07, [], CeilLook);
    }

    /// <summary>
    /// The six drives: each one's mount bolted over its opening on the transom's inner face, its bell through the opening, and its engine on
    /// its axis forward of the mount: a Drive Core, a Fuel Pump and a Coolant Exchanger (ship-works.md 9).
    /// </summary>
    private static void DriveTrains(Writer w)
    {
        var inner = Stern - Thick(ArmourH) - 0.03 - 0.004; // (the transom's inner face, less a gap)
        foreach (var (x, y) in Drives)
        {
            var m = w.Root(DriveMountName, B(x, y, inner), 0, new Look(Armour2, "plated steel"), keep: ["bell", "engine"]);
            w.OnSocket(m, "bell", DriveBellName, "mount", 0);
            var core = w.OnSocket(m, "engine", DriveCoreName, "back", 0);
            var pump = w.OnSocket(core, "front", FuelPumpName, "back", 0);
            w.OnSocket(pump, "front", ExchangerName, "back", 0);
        }
    }

    /// <summary>Point-defence turrets along the spine's edges (the Zero Day's PDC on a turret ring and motor hinge), their rings' trunks
    /// down through wells the Laser Cutter cut.</summary>
    private static void Pdcs(Writer w)
    {
        foreach (var z in PdcZ)
            foreach (var s in (ReadOnlySpan<int>)[1, -1])
            {
                var ring = w.Root("Turret Ring S", B(s * PdcX, Heights(z).Spine + Thick(SkinH) / 2 + 0.003, z), 0, new Look(Armour, "plated steel"), keep: ["top"]);
                var hinge = w.OnSocket(ring, "top", "Motor Hinge", "base", w.FirstTurn(ring, "top", "Motor Hinge", "base"));
                w.OnSocket(hinge, "leaf", "Zero Day PDC", "bottom", w.FirstTurn(hinge, "leaf", "Zero Day PDC", "bottom"));
            }
    }

    /// <summary>Long-range sensors on the bow's spine and over the bridge.</summary>
    private static void Sensors(Writer w, Dictionary<(int, int), Plane> planes)
    {
        foreach (var z in (ReadOnlySpan<double>)[100, 125])
        {
            var pl = planes[(BandOf(z), 4)];
            var (v, u) = pl.Of(new D3(0, Heights(z).Spine, z));
            OnPlane(w, pl, "Long Range Sensor", v, Thick(SkinH) / 2 + 0.125 + 0.004, u, 0, new Look(Frame, "dirty steel"));
        }
        foreach (var x in (ReadOnlySpan<double>)[-8, 8])
            w.Root("Long Range Sensor", B(x, Heights(500).Spine + Thick(SkinH) / 2 + 0.125 + 0.003, 500), 0, new Look(Frame, "dirty steel"));
    }
}
