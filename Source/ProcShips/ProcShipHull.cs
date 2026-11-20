using System.Globalization;

namespace CallOfSlop.ProcShips;

/// <summary>
/// An engine nacelle outboard on side <see cref="Side"/>: a chamfered square pod from Z0 to Z1 centred <see cref="X"/> off the
/// centreline at height <see cref="Y"/>, <see cref="S"/> half across with its corners chamfered <see cref="C"/>, its engine plate on
/// its tail. The hull places it; the plan sets its pylon (PZ0 to PZ1, PT half thick, from PIn off the centreline) once the airlocks
/// are placed, and may shorten its nose to keep it clear of them.
/// </summary>
public sealed class ProcNacelle
{
    public required int Side { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public double Z0 { get; set; }
    public required double Z1 { get; init; }
    public required double S { get; init; }
    public required double C { get; init; }
    public required int Engine { get; init; } // (its engine plate's size in metres)
    public double PZ0, PZ1, PT, PIn;

    /// <summary>The half size of the solid box inside it: inside the octagon at its narrowest past the nose (the tail's 0.95).</summary>
    public double Core => S - C / 2 - 0.1;

    /// <summary>Where the full section starts behind the nose.</summary>
    public double Body => Z0 + 2.6;

    /// <summary>The section's scale along it: a tapered nose, full along its body, a slight pinch at the tail.</summary>
    public (double Z, double K)[] Rings => [(Z0, 0.55), (Z0 + 1.2, 0.86), (Body, 1.0), (Z1 - 0.8, 1.0), (Z1, 0.95)];
}

/// <summary>
/// A procedural ship's outer hull: a lofted, faceted skin through stations from the bow's tip (z 0) to the stern's transom (z L), each
/// station a half profile of five points mirrored about the centreline (the Zero Day's: P0 the keel's edge, P1 the lower chine, P2
/// the beam, P3 the upper chine, P4 the spine's edge). Ship metres: x across (0 the centreline, port -x), y up (0 the underside of
/// the lowest deck's floor), z aft from the bow's tip, so forward is -z as in the game.
/// </summary>
public sealed class ProcHullShape
{
    public readonly record struct Pt(double H, double Y);

    public readonly record struct Station(double Z, Pt[] P);

    public const double Storey = 3.25; // floor to floor (a 0.25 m slab and 3 m clear: the Zero Day's)

    /// <summary>How many times a hull too small for its rooms is made again a size up (ProcShip.Make).</summary>
    public const int MostGrown = 3;
    public const double Slab = 0.25;

    public required Station[] Stations { get; init; }
    public required double Length { get; init; }
    public required double Beam { get; init; } // the half beam at the beam line
    public required double KeelY { get; init; }
    public required double TopY { get; init; }
    public required double BeamY { get; init; }
    public required int Decks { get; init; }
    public required ProcHull Style { get; init; }
    public required double MidZ0 { get; init; } // the constant midbody, where the section doesn't change
    public required double MidZ1 { get; init; }
    public required Pt[] Mid { get; init; }
    public required ProcRole Role { get; init; }

    /// <summary>The bow's kind (what it is for).</summary>
    public required ProcBow Bow { get; init; }

    /// <summary>Where the hull's skin starts: its first station (z 0, the bow's tip).</summary>
    public double HullZ0 => Stations[0].Z;

    /// <summary>A spinal railgun's barrel along the centreline of the lowest deck (a lancer's): its axis' height, half width and half height; NaN: none.</summary>
    public double BarrelY { get; private set; } = double.NaN;

    public const double BarrelHalf = 0.5, BarrelHalfH = 0.55;

    public bool Spinal => !double.IsNaN(BarrelY);

    /// <summary>Where the barrel's muzzle is: the lance's tip.</summary>
    public double MuzzleZ => HullZ0;

    /// <summary>A cage hull's well is a hollow shell of frames (its bulwarks open between posts), where an ice hauler's chunks ride.</summary>
    public bool Cage => Style == ProcHull.Cage;

    /// <summary>Where the bow's own shape ends (the first full section behind a lance, hammer, mouth, dome or jaw; 0 for the family's bow).</summary>
    public double BowZ1 { get; private set; }

    /// <summary>A hammer bow's bar: its tips' z range (0 to HammerZ1, the bar's aft face), and the bar's half span (0: no hammer).</summary>
    public double HammerZ1 { get; private set; }

    public double HammerHalf { get; private set; }

    /// <summary>The engine nacelles (none, or one each side).</summary>
    public List<ProcNacelle> Nacelles { get; } = [];

    /// <summary>A barge's open cargo well: the top of the hull open from WellZ0 to WellZ1, WellHalf either side of the centreline, down to the roof (0: no well).</summary>
    public double WellZ0 { get; private set; }

    public double WellZ1 { get; private set; }

    public double WellHalf { get; private set; }

    public bool HasWell => WellHalf > 0;

    /// <summary>The well's half width at z (0 outside it).</summary>
    public double Well(double z) => HasWell && z > WellZ0 + 1e-6 && z < WellZ1 - 1e-6 ? WellHalf : 0;

    /// <summary>A stepped hull's tower: true when it stands over the aft part (the long deck forward of it).</summary>
    public bool TowerAft { get; private set; }

    /// <summary>The top of the roof slab over the top deck.</summary>
    public double Roof => Storey * Decks + Slab;

    public static double FloorLo(int deck) => Storey * deck;

    public static double FloorTop(int deck) => Storey * deck + Slab;

    /// <summary>A deck's ceiling (the underside of the slab over it).</summary>
    public static double Ceil(int deck) => Storey * (deck + 1);

    /// <summary>The half profile at z, blended in straight lines between stations.</summary>
    public Pt[] At(double z)
    {
        var st = Stations;
        if (z <= st[0].Z)
            return st[0].P;
        for (var i = 0; i < st.Length - 1; i++)
            if (z <= st[i + 1].Z)
            {
                var t = (z - st[i].Z) / (st[i + 1].Z - st[i].Z);
                var o = new Pt[5];
                for (var k = 0; k < 5; k++)
                    o[k] = new Pt(st[i].P[k].H + (st[i + 1].P[k].H - st[i].P[k].H) * t, st[i].P[k].Y + (st[i + 1].P[k].Y - st[i].P[k].Y) * t);
                return o;
            }
        return st[^1].P;
    }

    /// <summary>The skin's half width at height y and z: 0 above or below the section.</summary>
    public double SkinHalf(double z, double y)
    {
        if (z < Stations[0].Z - 1e-6)
            return 0; // (forward of the bow's tip)
        var q = At(z);
        if (y < q[0].Y || y > q[4].Y)
            return 0;
        for (var i = 0; i < 4; i++)
            if (y <= q[i + 1].Y)
                return q[i + 1].Y - q[i].Y < 1e-9 ? Math.Max(q[i].H, q[i + 1].H) : q[i].H + (q[i + 1].H - q[i].H) * (y - q[i].Y) / (q[i + 1].Y - q[i].Y);
        return q[4].H;
    }

    /// <summary>The narrowest the skin is over a box of heights and lengths (it is convex, so at the box's corners).</summary>
    public double MinHalf(double z0, double z1, double y0, double y1)
    {
        var m = double.MaxValue;
        for (var z = z0; z <= z1 + 1e-9; z += Math.Max(0.25, (z1 - z0) / 8))
            m = Math.Min(m, Math.Min(SkinHalf(Math.Min(z, z1), y0), SkinHalf(Math.Min(z, z1), y1)));
        return Math.Min(m, Math.Min(SkinHalf(z1, y0), SkinHalf(z1, y1)));
    }

    /// <summary>A facet's two edge points at z, ship metres: k 0 the keel (port to starboard), 1..4 the side facets P[k-1] to P[k] on side s, 5 the spine (starboard to port).</summary>
    public (double[] A, double[] B) Edge(int k, int s, double z)
    {
        var q = At(z);
        double[] V(Pt p, int side) => [side * p.H, p.Y, z];
        return k switch
        {
            0 => (V(q[0], -1), V(q[0], 1)),
            5 => (V(q[4], 1), V(q[4], -1)),
            _ => (V(q[k - 1], s), V(q[k], s)),
        };
    }

    /// <summary>A point on facet k (side s) between za and zb: u along z, v across from edge A to edge B.</summary>
    public double[] OnFacet(int k, int s, double za, double zb, double u, double v)
    {
        var (a0, b0) = Edge(k, s, za);
        var (a1, b1) = Edge(k, s, zb);
        return Lerp(Lerp(a0, b0, v), Lerp(a1, b1, v), u);
    }

    public static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    // ---- making one

    private static double Q(double v, double step = 0.05) => Math.Round(v / step) * step;

    /// <summary>
    /// A shape key along the length: z as a share of the length, the width's scale, the section's bottom and top as shares of the
    /// midbody's height from the keel, and (when given) each profile point's own width scale (HK) and height share between the
    /// bottom and the top (YK; else the midbody's own).
    /// </summary>
    private readonly record struct Key(double Z, double W, double Bot, double Top, double[]? HK = null, double[]? YK = null);

    /// <summary>The families a role's hull is, weighted.</summary>
    private static (ProcHull, double)[] Styles(ProcRole role) => role switch
    {
        ProcRole.Gunship => [(ProcHull.Chisel, 0.4), (ProcHull.Dart, 0.32), (ProcHull.Block, 0.1), (ProcHull.Stepped, 0.18)],
        ProcRole.Freighter => [(ProcHull.Chisel, 0.12), (ProcHull.Dart, 0.05), (ProcHull.Block, 0.23), (ProcHull.Barge, 0.42), (ProcHull.Stepped, 0.18)],
        ProcRole.Miner => [(ProcHull.Chisel, 0.25), (ProcHull.Dart, 0.05), (ProcHull.Block, 0.35), (ProcHull.Barge, 0.2), (ProcHull.Stepped, 0.15)],
        ProcRole.Tender => [(ProcHull.Chisel, 0.3), (ProcHull.Dart, 0.15), (ProcHull.Block, 0.3), (ProcHull.Barge, 0.1), (ProcHull.Stepped, 0.15)],
        ProcRole.Liner => [(ProcHull.Chisel, 0.2), (ProcHull.Dart, 0.25), (ProcHull.Block, 0.15), (ProcHull.Stepped, 0.4)],
        ProcRole.Lancer => [(ProcHull.Chisel, 0.35), (ProcHull.Dart, 0.45), (ProcHull.Block, 0.1), (ProcHull.Kite, 0.1)],
        ProcRole.Carrier => [(ProcHull.Chisel, 0.2), (ProcHull.Dart, 0.05), (ProcHull.Block, 0.45), (ProcHull.Stepped, 0.3)],
        ProcRole.Comms => [(ProcHull.Chisel, 0.3), (ProcHull.Dart, 0.3), (ProcHull.Block, 0.2), (ProcHull.Stepped, 0.1), (ProcHull.Kite, 0.1)],
        ProcRole.Stealth => [(ProcHull.Kite, 0.85), (ProcHull.Dart, 0.15)],
        ProcRole.Salvager => [(ProcHull.Chisel, 0.2), (ProcHull.Block, 0.35), (ProcHull.Barge, 0.25), (ProcHull.Stepped, 0.2)],
        _ => [(ProcHull.Cage, 0.75), (ProcHull.Barge, 0.25)],
    };

    /// <summary>
    /// The bows a role's ship has, weighted, each for a use: a lancer's lance carries its spinal railgun's muzzle, a carrier's mouth
    /// opens into its hangar, a freighter's into a fore hold and a tender's into its repair bay, a comms ship's hammer holds sensors at
    /// its tips, a liner's dome is its observation lounge, a miner's, an ice hauler's or a salvager's jaw carries cutters. (The fork,
    /// two prongs either side of a berth, was taken out in version 4: Karl, 2026-11-20.)
    /// </summary>
    private static (ProcBow, double)[] Bows(ProcRole role) => role switch
    {
        ProcRole.Gunship => [(ProcBow.Family, 0.85), (ProcBow.Hammer, 0.15)],
        ProcRole.Freighter => [(ProcBow.Family, 0.65), (ProcBow.Mouth, 0.35)],
        ProcRole.Miner => [(ProcBow.Family, 0.45), (ProcBow.Jaw, 0.55)],
        ProcRole.Tender => [(ProcBow.Family, 0.6), (ProcBow.Mouth, 0.4)],
        ProcRole.Liner => [(ProcBow.Family, 0.45), (ProcBow.Dome, 0.55)],
        ProcRole.Lancer => [(ProcBow.Lance, 1.0)],
        ProcRole.Carrier => [(ProcBow.Mouth, 0.8), (ProcBow.Family, 0.2)],
        ProcRole.Comms => [(ProcBow.Hammer, 0.7), (ProcBow.Family, 0.2), (ProcBow.Dome, 0.1)],
        ProcRole.Stealth => [(ProcBow.Family, 1.0)],
        ProcRole.Salvager => [(ProcBow.Family, 0.55), (ProcBow.Jaw, 0.45)],
        _ => [(ProcBow.Jaw, 0.5), (ProcBow.Family, 0.5)],
    };

    /// <summary>A spinal railgun's axis: its barrel's middle over the lowest deck's floor (the barrel from 0.15 m over the floor).</summary>
    public const double BarrelAxis = Slab + 0.15 + BarrelHalfH;

    /// <summary>
    /// The hull for a spec: its family and bow by role, its length, beam and heights by class and seed, the midbody section by
    /// family, then stations for the bow and stern from the family's shape keys (the bow's own keys in place of the family's when it
    /// has one), each nudged by the dice; a barge's or a cage's well, the spinal barrel and the nacelles last.
    /// Every choice is written into <paramref name="traits"/>.
    /// </summary>
    public static ProcHullShape Make(ProcShipSpec spec, ProcRole role, ProcFit fit, ProcDice dice, List<(string Key, string Value)> traits, int grow = 0)
    {
        var style = spec.Hull ?? dice.For("hull.style").Weighted(Styles(role));
        var bow = spec.Bow ?? dice.For("hull.bow").Weighted(Bows(role));
        var size = dice.For("hull.size");
        var oneDeck = role is ProcRole.Freighter or ProcRole.Miner or ProcRole.Tender or ProcRole.Salvager ? 0.3 : role == ProcRole.Stealth ? 0.75 : 0.55; // (a hold or a hangar wants two decks)
        var (len, beam, decks) = spec.Class switch
        {
            ProcClass.Corvette => (Q(size.Range(30, 40), 0.5), size.Range(3.9, 4.8), size.Chance(oneDeck) ? 1 : 2),
            ProcClass.Frigate => (Q(size.Range(44, 60), 0.5), size.Range(5.1, 6.5), size.Chance(0.85) ? 2 : 3),
            _ => (Q(size.Range(64, 84), 0.5), size.Range(6.4, 8.0), size.Chance(0.55) ? 2 : 3),
        };
        var dk = dice.For("hull.decks");
        if (role == ProcRole.Liner)
            decks = Math.Min(decks + 1, spec.Class switch { ProcClass.Corvette => 3, ProcClass.Frigate => 4, _ => 5 }); // (a liner's extra deck of cabins)
        if (role is ProcRole.Lancer or ProcRole.Carrier)
            decks = Math.Max(decks, spec.Class == ProcClass.Destroyer && role == ProcRole.Carrier ? 3 : 2); // (a gun deck under the crew's; a hangar under the bridge)
        if (style == ProcHull.Stepped)
            decks = spec.Class switch { ProcClass.Corvette => dk.Int(2, 3), ProcClass.Frigate => dk.Int(3, 4), _ => dk.Int(4, 5) };
        else if (style is ProcHull.Barge or ProcHull.Cage)
            decks = spec.Class switch { ProcClass.Corvette => 1, ProcClass.Frigate => dk.Chance(0.55) ? 1 : 2, _ => 2 };
        else if (style == ProcHull.Kite)
            decks = spec.Class switch { ProcClass.Corvette => 1, ProcClass.Frigate => dk.Chance(0.5) ? 1 : 2, _ => 2 }; // (low: little to see)
        if (role == ProcRole.Lancer && style != ProcHull.Stepped)
            decks = Math.Max(decks, 2);
        if (spec.Decks is { } fixedDecks)
            decks = fixedDecks;
        if (role == ProcRole.Lancer && decks < 2)
            throw new ProcShipException("a lancer needs two decks or more (its railgun runs along the lowest)");
        // What a bow needs: a mouth opens under the bridge's deck; a kite keeps its blade (or a lance); a well's hull no dome or hammer.
        if (bow == ProcBow.Mouth && decks < 2)
            bow = ProcBow.Family;
        if (style == ProcHull.Kite && bow is not (ProcBow.Family or ProcBow.Lance))
            bow = ProcBow.Family;
        if (style is ProcHull.Barge or ProcHull.Cage && bow is ProcBow.Dome or ProcBow.Hammer)
            bow = ProcBow.Family;
        if (spec.Class == ProcClass.Corvette && decks >= 2)
            len = Q(len + 2.5 * (decks - 1), 0.5); // (a small hull of decks over decks: room for the lobby amidships under its taper)
        if (bow == ProcBow.Lance && spec.Class == ProcClass.Corvette)
            len = Q(len + 5, 0.5); // (the lance before the rooms start)
        if (grow > 0)
        {
            len = Q(len * (1 + 0.08 * grow), 0.5);
            beam *= 1 + 0.07 * grow;
        }
        if (style == ProcHull.Stepped && decks < 2)
            throw new ProcShipException("a stepped hull needs two decks or more (a tall block over one long deck)");
        if (style == ProcHull.Dart)
            beam *= 1.06; // (its section is slimmer: a wider beam keeps rooms in it)
        if (style == ProcHull.Barge)
            beam *= 1.35;
        if (style == ProcHull.Cage)
            beam *= 1.4;
        if (style == ProcHull.Kite)
            beam *= 1.22; // (flat and wide: its rooms sit in the thick middle)
        if (decks == 3)
            beam *= 1.08;
        else if (decks >= 4)
            beam *= 1.08 + 0.04 * (decks - 3); // (a tall hull: wide enough for its upper decks' lobbies)
        else if (decks == 2 && spec.Class == ProcClass.Corvette)
            beam *= 1.06; // (a small hull of two decks: room for the lobby's ladder on the upper one)
        beam = Q(beam);
        var h = dice.For("hull.heights");
        var keelDepth = Q(spec.Class switch { ProcClass.Corvette => h.Range(0.95, 1.2), ProcClass.Frigate => h.Range(1.1, 1.45), _ => h.Range(1.3, 1.7) });
        var spineGap = Q(style switch
        {
            ProcHull.Barge => h.Range(2.8, 3.4), // (its well's depth)
            ProcHull.Cage => spec.Class switch { ProcClass.Corvette => h.Range(3.6, 4.4), ProcClass.Frigate => h.Range(4.4, 5.6), _ => h.Range(5.2, 6.6) }, // (the cage's: chunks stacked in it)
            ProcHull.Kite => h.Range(0.55, 0.8),
            _ => h.Range(0.65, 1.05),
        });
        var roof = Storey * decks + Slab;
        var keelY = -keelDepth;
        var topY = roof + spineGap;
        var beamY = Q(roof * style switch { ProcHull.Barge => h.Range(0.3, 0.5), ProcHull.Cage => h.Range(0.3, 0.45), ProcHull.Kite => h.Range(0.44, 0.56), _ => h.Range(0.36, 0.56) });
        var f = dice.For("hull.section");
        // The midbody's half profile: fractions of the beam for the keel's edge, the chines and the spine's edge, by family.
        var (f0, f1, f3, f4) = style switch
        {
            ProcHull.Chisel => (f.Range(0.42, 0.52), f.Range(0.86, 0.93), f.Range(0.8, 0.88), f.Range(0.4, 0.5)),
            ProcHull.Dart => (f.Range(0.27, 0.36), f.Range(0.72, 0.82), f.Range(0.7, 0.8), f.Range(0.27, 0.36)),
            ProcHull.Barge => (f.Range(0.72, 0.82), f.Range(0.95, 0.98), f.Range(0.97, 1.0), f.Range(0.86, 0.92)),
            ProcHull.Cage => (f.Range(0.72, 0.8), f.Range(0.95, 0.98), f.Range(0.97, 1.0), f.Range(0.9, 0.95)),
            ProcHull.Kite => (f.Range(0.26, 0.34), f.Range(0.86, 0.92), f.Range(0.84, 0.9), f.Range(0.26, 0.34) + (decks >= 2 ? 0.14 : 0)), // (decks over decks: a broader ridge)
            _ => (f.Range(0.64, 0.74), f.Range(0.94, 0.98), f.Range(0.92, 0.97), f.Range(0.62, 0.74)),
        };
        var boxy = style is ProcHull.Block or ProcHull.Stepped or ProcHull.Barge or ProcHull.Cage;
        var y1 = style == ProcHull.Kite
            ? Q(beamY - (beamY - keelY) * f.Range(0.22, 0.34), 0.05) // (the kite's chines close over and under its beam: one sharp edge round it)
            : Q(keelY * f.Range(boxy ? 0.55 : 0.05, boxy ? 0.8 : 0.4));
        var y3 = style switch
        {
            ProcHull.Barge => Q(roof + spineGap * f.Range(0.55, 0.75)), // (the bulwarks stand straight up from the beam, their top flared in)
            ProcHull.Cage => Q(roof + f.Range(0.35, 0.5)), // (the shell's frames stand from just over the roof)
            ProcHull.Kite => Q(beamY + (topY - beamY) * (f.Range(0.22, 0.34) + (decks >= 2 ? 0.36 : 0)), 0.05), // (decks over decks: the upper chine near the roof, so the top deck keeps a width)
            _ => Q(roof + spineGap * f.Range(decks > 1 ? -0.1 : -0.35, decks > 1 ? 0.4 : 0.3)), // (decks over decks: the upper chine near the roof, so the top deck keeps a width)
        };
        Pt[] mid = [new(Q(beam * f0), keelY), new(Q(beam * f1), y1), new(beam, beamY), new(Q(beam * f3), y3), new(Q(beam * f4), topY)];
        var span = topY - keelY;

        // The family's shape keys along the length, each nudged.
        var k = dice.For("hull.keys");
        double J(double v, double by) => v + k.Range(-by, by);
        var keys = new List<Key>();
        string bowText, stern;
        var aftF = 1.0; // (a stepped hull's low deck aft of its tower: the stern keys squashed to it)
        var towerAft = false;
        switch (style)
        {
            case ProcHull.Chisel:
                {
                    var low = k.Range(0.22, 0.36); // (how low the chisel's tip sits)
                    keys.Add(new(0, J(0.09, 0.02), low, low + J(0.24, 0.05)));
                    keys.Add(new(J(0.05, 0.01), J(0.58, 0.04), J(0.12, 0.03), J(0.84, 0.04)));
                    keys.Add(new(J(0.18, 0.02), J(0.86, 0.03), J(0.04, 0.02), J(0.97, 0.02)));
                    keys.Add(new(J(0.29, 0.02), 1, 0, 1));
                    bowText = low < 0.29 ? "low chisel" : "chisel";
                    break;
                }
            case ProcHull.Dart:
                {
                    var mid0 = J(0.42, 0.04);
                    keys.Add(new(0, J(0.035, 0.01), J(0.46, 0.04), J(0.56, 0.04)));
                    keys.Add(new(mid0 * 0.26, J(0.3, 0.04), J(0.3, 0.04), J(0.74, 0.04)));
                    keys.Add(new(mid0 * 0.55, J(0.6, 0.04), J(0.13, 0.03), J(0.89, 0.03)));
                    keys.Add(new(mid0 * 0.82, J(0.88, 0.03), J(0.03, 0.02), J(0.98, 0.01)));
                    keys.Add(new(mid0, 1, 0, 1));
                    bowText = mid0 > 0.42 ? "long dart" : "dart";
                    break;
                }
            case ProcHull.Barge:
            case ProcHull.Cage:
                {
                    var rake = k.Range(0.3, 0.48); // (how high the raked bow's foot sits)
                    keys.Add(new(0, J(0.8, 0.04), rake, J(0.985, 0.01)));
                    keys.Add(new(J(0.07, 0.015), J(0.95, 0.02), J(0.1, 0.03), 1));
                    keys.Add(new(J(0.14, 0.02), 1, 0, 1));
                    bowText = rake > 0.4 ? $"high raked {style.ToString().ToLowerInvariant()}" : $"raked {style.ToString().ToLowerInvariant()}";
                    break;
                }
            case ProcHull.Kite:
                {
                    // A flat blade: the tip on the beam's chine line, the planform a long straight taper (the kite's stealth: nothing square to a radar).
                    var sb = (beamY - keelY) / span;
                    var lk = J(0.38, 0.03);
                    keys.Add(new(0, J(0.04, 0.01), sb - 0.03, sb + 0.03, HK: [0.3, 0.9, 1, 0.9, 0.3]));
                    keys.Add(new(lk * 0.36, J(0.38, 0.03), sb * 0.55, sb + (1 - sb) * 0.45));
                    keys.Add(new(lk * 0.7, J(0.74, 0.03), sb * 0.18, sb + (1 - sb) * 0.82));
                    keys.Add(new(lk, 1, 0, 1));
                    bowText = "kite blade";
                    break;
                }
            case ProcHull.Stepped:
                {
                    // A tall block of every deck over part of the length, one long low deck (the lowest) over the rest: the low
                    // part's section is the midbody's squashed to the lowest deck and its spine (lowF of the height).
                    var lowTop = FloorTop(1) + Q(k.Range(0.6, 0.95));
                    var lowF = (lowTop - keelY) / (topY - keelY);
                    towerAft = k.Chance(0.65);
                    var tower = Q(Math.Clamp(k.Range(0.42, 0.58) * len, Math.Min(19, len - 10), len - 10), 0.5);
                    var ramp = Q(k.Range(1.5, 3.0), 0.5);
                    var face = k.Range(0.58, 0.72);
                    if (towerAft)
                    {
                        keys.Add(new(0, face, J(0.14, 0.03) * lowF, J(0.86, 0.03) * lowF));
                        keys.Add(new(J(0.05, 0.01), J(0.9, 0.03), J(0.04, 0.02) * lowF, J(0.97, 0.02) * lowF));
                        keys.Add(new(J(0.12, 0.015), 1, 0, lowF));
                        keys.Add(new((len - tower - ramp) / len, 1, 0, lowF));
                        keys.Add(new((len - tower) / len, 1, 0, 1));
                        bowText = "low block, tower aft";
                    }
                    else
                    {
                        keys.Add(new(0, face, J(0.13, 0.03), J(0.88, 0.03)));
                        keys.Add(new(J(0.045, 0.01), J(0.9, 0.03), J(0.04, 0.02), J(0.97, 0.02)));
                        keys.Add(new(J(0.11, 0.015), 1, 0, 1));
                        keys.Add(new(tower / len, 1, 0, 1));
                        keys.Add(new((tower + ramp) / len, 1, 0, lowF));
                        aftF = lowF;
                        bowText = "block, tower fore";
                    }
                    break;
                }
            default:
                {
                    var face = k.Range(0.6, 0.74); // (the blunt bow's face)
                    keys.Add(new(0, face, J(0.13, 0.03), J(0.88, 0.03)));
                    keys.Add(new(J(0.045, 0.01), J(0.9, 0.03), J(0.04, 0.02), J(0.97, 0.02)));
                    keys.Add(new(J(0.11, 0.015), 1, 0, 1));
                    bowText = face > 0.67 ? "blunt block" : "chamfered block";
                    break;
                }
        }
        // The bow's own keys in place of the family's bow: up to the first full key (its section the midbody's, or a stepped
        // hull's low deck's), which they end on.
        var bs = dice.For("hull.bowshape");
        var ownKeys = 0;
        if (bow != ProcBow.Family)
        {
            var full = keys.FindIndex(q => q.W == 1 && q.Bot == 0);
            var foreTop = keys[full].Top;
            var axis = (BarrelAxis - keelY) / span; // (the barrel's axis as a share of the height)
            var sb = Math.Min((beamY - keelY) / span, foreTop * 0.85);
            var own = new List<Key>();
            switch (bow)
            {
                case ProcBow.Lance:
                    {
                        // A long cone to a muzzle 2 m across round the spinal barrel's axis (a lance on another role: the same cone, a sensor spike in it).
                        var lb = bs.Range(0.36, 0.46);
                        const double r0 = 1.05;
                        double a0 = Math.Max(0, axis - r0 / span), a1 = Math.Min(foreTop, axis + r0 / span), w0 = r0 / beam;
                        double[] round = [0.55, 0.95, 1.0, 0.95, 0.55];
                        double[] oct = [0, 0.2, 0.5, 0.8, 1];
                        foreach (var u in new[] { 0.0, 0.2, 0.45, 0.72, 1.0 })
                        {
                            double g = Math.Pow(u, 0.8), g2 = Math.Pow(u, 0.7), keep = Math.Pow(1 - u, 1.5);
                            var w = w0 + (1 - w0) * g;
                            var hk = new double[5];
                            var yk = new double[5];
                            for (var i = 0; i < 5; i++)
                            {
                                hk[i] = 1 + (r0 * round[i] / (mid[i].H * w0) - 1) * keep;
                                yk[i] = (mid[i].Y - keelY) / span + (oct[i] - (mid[i].Y - keelY) / span) * keep;
                            }
                            own.Add(new(u * lb, w, a0 * (1 - g2), a1 + (foreTop - a1) * g2, HK: hk, YK: yk));
                        }
                        bowText = $"lance, a {M(2 * r0)} m muzzle";
                        break;
                    }
                case ProcBow.Hammer:
                    {
                        // A wide flat bar across the bow (sensors at its tips), a neck behind it, then the hull.
                        double hd = Q(bs.Range(3.2, 4.8), 0.5) / len, neck = hd + Q(bs.Range(2.0, 3.2), 0.5) / len;
                        var end = Math.Max(neck + 0.12, bs.Range(0.28, 0.36));
                        double wh = bs.Range(1.28, 1.5), wn = bs.Range(0.52, 0.64), hb = bs.Range(0.15, 0.2);
                        double b0 = Math.Max(0.02, sb - hb), b1 = Math.Min(foreTop - 0.02, sb + hb);
                        own.Add(new(0, wh, b0, b1));
                        own.Add(new(hd, wh, b0, b1));
                        own.Add(new(neck, wn, 0.1 * foreTop, 0.9 * foreTop));
                        own.Add(new(end, 1, 0, foreTop));
                        bowText = $"hammer, a {M(2 * wh * beam)} m bar";
                        break;
                    }
                case ProcBow.Mouth:
                    own.Add(new(0, bs.Range(0.84, 0.9), 0.04 * foreTop, 0.96 * foreTop));
                    own.Add(new(bs.Range(0.03, 0.04), 0.955, 0.015 * foreTop, 0.985 * foreTop));
                    own.Add(new(bs.Range(0.07, 0.09), 1, 0, foreTop));
                    bowText = "mouth";
                    break;
                case ProcBow.Dome:
                    {
                        var ld = bs.Range(0.16, 0.24);
                        const double wa = 0.1;
                        double[] round = [0.55, 0.95, 1.0, 0.95, 0.55];
                        double[] oct = [0, 0.2, 0.5, 0.8, 1];
                        foreach (var u in new[] { 0.0, 0.18, 0.4, 0.65, 1.0 })
                        {
                            var c = Math.Sqrt(1 - (1 - u) * (1 - u));
                            var keep = Math.Pow(1 - u, 1.5);
                            var hk = new double[5];
                            var yk = new double[5];
                            for (var i = 0; i < 5; i++)
                            {
                                hk[i] = 1 + (round[i] / (mid[i].H / beam) - 1) * keep;
                                yk[i] = (mid[i].Y - keelY) / span + (oct[i] - (mid[i].Y - keelY) / span) * keep;
                            }
                            own.Add(new(u * ld, wa + (1 - wa) * c, (sb - 0.05) * (1 - c), sb + 0.05 + (foreTop - sb - 0.05) * c, HK: hk, YK: yk));
                        }
                        bowText = "glazed dome";
                        break;
                    }
                default:
                    {
                        // The jaw: a low wide lip (cutters on its face), the face raked back from it to the spine.
                        var lip = bs.Range(0.24, 0.32);
                        own.Add(new(0, bs.Range(0.84, 0.9), 0, lip * foreTop));
                        own.Add(new(bs.Range(0.045, 0.055), 0.93, 0, 0.62 * foreTop));
                        own.Add(new(bs.Range(0.09, 0.11), 0.975, 0, 0.92 * foreTop));
                        own.Add(new(bs.Range(0.14, 0.17), 1, 0, foreTop));
                        bowText = $"jaw, a {M(lip * foreTop * span)} m lip";
                        break;
                    }
            }
            var ownEnd = own[^1].Z;
            var rest = keys.Skip(full + 1).ToList();
            if (rest.Count > 0 && rest[0].Z <= ownEnd + 0.03)
                throw new ProcShipException($"the {BowWordOf(bow)} bow is too long for a {style.ToString().ToLowerInvariant()} hull's steps");
            keys = [.. own, .. rest];
            ownKeys = own.Count;
        }
        var aft = dice.For("hull.stern");
        var sternKind = style switch
        {
            ProcHull.Dart => aft.Weighted(("pinched", 0.6), ("straight", 0.4)),
            ProcHull.Barge or ProcHull.Cage => "square",
            ProcHull.Stepped => aft.Weighted(("straight", 0.6), ("pinched", 0.4)),
            ProcHull.Kite => aft.Weighted(("pinched", 0.5), ("straight", 0.5)),
            _ => aft.Weighted(("swell", 0.45), ("straight", 0.3), ("pinched", 0.25)),
        };
        var midEnd = style switch { ProcHull.Dart or ProcHull.Kite => J(0.84, 0.03), ProcHull.Block => J(0.9, 0.02), ProcHull.Barge or ProcHull.Cage or ProcHull.Stepped => J(0.92, 0.015), _ => J(0.8, 0.03) };
        keys.Add(new(midEnd, 1, 0, aftF));
        switch (sternKind)
        {
            case "swell":
                keys.Add(new(J(0.9, 0.015), J(1.045, 0.015), 0.0, 1.0));
                keys.Add(new(1, J(1.02, 0.02), J(0.1, 0.03), J(0.99, 0.01)));
                break;
            case "pinched":
                keys.Add(new(1, J(0.84, 0.03), J(0.16, 0.04) * aftF, J(0.95, 0.02) * aftF));
                break;
            case "square":
                keys.Add(new(1, J(0.985, 0.01), J(0.05, 0.02), 1));
                break;
            default:
                keys.Add(new(1, J(0.97, 0.02), J(0.08, 0.03) * aftF, J(0.99, 0.01) * aftF));
                break;
        }
        stern = sternKind;
        // Stations from the keys, at least a metre apart, on a 5 cm grid.
        var stations = new List<Station>();
        foreach (var key in keys)
        {
            var z = Q(Math.Clamp(key.Z, 0, 1) * len, 0.5);
            if (stations.Count > 0 && z < stations[^1].Z + 1.0)
                z = stations[^1].Z + 1.0;
            var yb = keelY + key.Bot * span;
            var yt = keelY + key.Top * span;
            var p = new Pt[5];
            for (var i = 0; i < 5; i++)
            {
                var t = key.YK?[i] ?? (mid[i].Y - keelY) / span;
                p[i] = new Pt(Q(Math.Max(0.06, mid[i].H * key.W * (key.HK?[i] ?? 1)), 0.01), Q(yb + t * (yt - yb), 0.01));
            }
            stations.Add(new Station(z, p));
        }
        if (stations[^1].Z != len)
            len = stations[^1].Z; // (a pushed last station sets the length)
        double midZ0 = 0, midZ1 = 0;
        for (var i = 0; i + 1 < stations.Count; i++)
            if (Same(stations[i].P, mid) && Same(stations[i + 1].P, mid) && stations[i + 1].Z - stations[i].Z > midZ1 - midZ0)
                (midZ0, midZ1) = (stations[i].Z, stations[i + 1].Z);
        var shape = new ProcHullShape
        {
            Stations = [.. stations], Length = len, Beam = beam, KeelY = keelY, TopY = topY, BeamY = beamY, Decks = decks, Style = style,
            MidZ0 = midZ0, MidZ1 = midZ1, Mid = mid, Role = role, TowerAft = towerAft, Bow = bow,
        };
        if (ownKeys > 0)
            shape.BowZ1 = stations[ownKeys - 1].Z;
        if (bow == ProcBow.Hammer)
        {
            shape.HammerZ1 = stations[1].Z;
            shape.HammerHalf = stations[0].P[2].H;
        }
        traits.Add(("Class", spec.Class.ToString().ToLowerInvariant()));
        traits.Add(("Role", ProcShipSpec.RoleWord(role)));
        traits.Add(("Hull", style.ToString().ToLowerInvariant()));
        traits.Add(("Bow", bowText));
        traits.Add(("Stern", stern));
        traits.Add(("Length", $"{M(len)} m"));
        traits.Add(("Beam", $"{M(beam * 2)} m"));
        traits.Add(("Decks", decks.ToString(CultureInfo.InvariantCulture)));
        traits.Add(("Height", $"{M(topY - keelY)} m"));
        if (grow > 0)
            traits.Add(("Grown", $"{8 * grow}% longer and {7 * grow}% wider than its seed's, to fit its rooms"));

        // A barge's open well, or a cage's hollow shell: the top of the midbody open between bulwarks (a cage's: frames) down to the roof.
        if (style is ProcHull.Barge or ProcHull.Cage)
        {
            var wl = dice.For("hull.well");
            double w0 = Q(midZ0 + wl.Range(0.5, 2.5), 0.5), w1 = Q(midZ1 - wl.Range(0.5, 2.5), 0.5);
            if (w1 - w0 >= 6)
            {
                shape.WellZ0 = w0;
                shape.WellZ1 = w1;
                shape.WellHalf = Q(mid[4].H - (style == ProcHull.Cage ? wl.Range(0.3, 0.4) : wl.Range(0.45, 0.7)), 0.05);
            }
            traits.Add((style == ProcHull.Cage ? "Cage" : "Well", shape.HasWell ? $"{M(w1 - w0)} m long, {M(shape.WellHalf * 2)} m wide, {M(spineGap)} m deep" : "none"));
        }

        // A lancer's spinal railgun: its barrel along the lowest deck's centreline from the muzzle (the lance's tip) to engineering.
        if (role == ProcRole.Lancer)
            shape.BarrelY = BarrelAxis;

        // Engine nacelles, one each side on a pylon, by role and family: past the beam, along the aft part.
        var n = dice.For("hull.nacelles");
        var roll = n.Chance(role switch
        {
            ProcRole.Gunship => 0.35, ProcRole.Freighter => 0.22, ProcRole.Miner => 0.15, ProcRole.Tender => 0.45, ProcRole.Lancer => 0.25, ProcRole.Carrier => 0.4,
            ProcRole.Comms => 0.3, ProcRole.Stealth => 0, ProcRole.Salvager => 0.25, ProcRole.IceHauler => 0.3, _ => 0.3,
        } * style switch { ProcHull.Chisel or ProcHull.Dart => 1.3, ProcHull.Barge => 0.5, ProcHull.Cage => 0.6, ProcHull.Stepped => 0.8, ProcHull.Kite => 0, _ => 1.0 });
        if (spec.Nacelles || roll)
        {
            var es = (spec.Class == ProcClass.Corvette ? 2 : 3) + (fit.Drive == ProcDrive.Heavy ? 1 : 0); // (a heavy drive's plate a size up)
            var s = es / 2.0 + 0.4; // (the engine plate's pad inside its tail's octagon, chamfers and all)
            var c = Q(s * 0.24, 0.05);
            var ln = Q(Math.Clamp(n.Range(0.32, 0.46) * len, 9, 26), 0.5);
            var z1 = Q(len + n.Range(-1.5, 1.5), 0.5);
            // Its height: about the beam, its pylon between the lowest floor and the roof (the low deck's, aft of a stepped hull's tower).
            var yTop = style == ProcHull.Stepped && !towerAft ? FloorTop(1) : roof;
            var y = Q(Math.Clamp(beamY + n.Range(-0.6, 1.4), 0.6, yTop - 0.6), 0.05);
            var clear = n.Range(0.9, 2.2);
            var most = 0.0;
            for (var z = Math.Max(0, z1 - ln); z <= Math.Min(z1, len) + 1e-9; z += 0.5)
                foreach (var yy in new[] { y - s, y, y + s, beamY })
                    if (yy >= y - s - 1e-9 && yy <= y + s + 1e-9)
                        most = Math.Max(most, shape.SkinHalf(Math.Min(z, len), yy));
            var x = Q(most + clear + s, 0.05);
            foreach (var side in new[] { -1, 1 })
                shape.Nacelles.Add(new ProcNacelle { Side = side, X = x, Y = y, Z0 = z1 - ln, Z1 = z1, S = s, C = c, Engine = es });
            traits.Add(("Nacelles", $"{M(ln)} m, {M(s * 2)} m across, {M(x - s - most)} m off the skin"));
        }
        else
            traits.Add(("Nacelles", "none"));
        return shape;

        static bool Same(Pt[] a, Pt[] b) => a.Zip(b).All(q => Math.Abs(q.First.H - q.Second.H) < 1e-6 && Math.Abs(q.First.Y - q.Second.Y) < 1e-6);
        static string M(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        static string BowWordOf(ProcBow b) => ProcShipSpec.BowWord(b);
    }
}
