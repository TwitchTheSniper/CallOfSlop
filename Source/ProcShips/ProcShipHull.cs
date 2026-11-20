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
    /// The hull for a spec: its family by role, its length, beam and heights by class and seed, the midbody section by family, then
    /// stations for the bow and stern from the family's shape keys, each nudged by the dice; a barge's open well and the nacelles last.
    /// Every choice is written into <paramref name="traits"/>.
    /// </summary>
    public static ProcHullShape Make(ProcShipSpec spec, ProcRole role, ProcDice dice, List<(string Key, string Value)> traits, int grow = 0)
    {
        var style = spec.Hull ?? dice.For("hull.style").Weighted<ProcHull>(role switch
        {
            ProcRole.Gunship => [(ProcHull.Chisel, 0.4), (ProcHull.Dart, 0.32), (ProcHull.Block, 0.1), (ProcHull.Stepped, 0.18)],
            ProcRole.Freighter => [(ProcHull.Chisel, 0.12), (ProcHull.Dart, 0.05), (ProcHull.Block, 0.23), (ProcHull.Barge, 0.42), (ProcHull.Stepped, 0.18)],
            ProcRole.Miner => [(ProcHull.Chisel, 0.25), (ProcHull.Dart, 0.05), (ProcHull.Block, 0.35), (ProcHull.Barge, 0.2), (ProcHull.Stepped, 0.15)],
            ProcRole.Tender => [(ProcHull.Chisel, 0.3), (ProcHull.Dart, 0.15), (ProcHull.Block, 0.3), (ProcHull.Barge, 0.1), (ProcHull.Stepped, 0.15)],
            _ => [(ProcHull.Chisel, 0.2), (ProcHull.Dart, 0.25), (ProcHull.Block, 0.15), (ProcHull.Stepped, 0.4)],
        });
        var size = dice.For("hull.size");
        var oneDeck = role is ProcRole.Freighter or ProcRole.Miner or ProcRole.Tender ? 0.3 : 0.55; // (a hold or a hangar wants two decks)
        var (len, beam, decks) = spec.Class switch
        {
            ProcClass.Corvette => (Q(size.Range(30, 40), 0.5), size.Range(3.9, 4.8), size.Chance(oneDeck) ? 1 : 2),
            ProcClass.Frigate => (Q(size.Range(44, 60), 0.5), size.Range(5.1, 6.5), size.Chance(0.85) ? 2 : 3),
            _ => (Q(size.Range(64, 84), 0.5), size.Range(6.4, 8.0), size.Chance(0.55) ? 2 : 3),
        };
        var dk = dice.For("hull.decks");
        if (role == ProcRole.Liner)
            decks = Math.Min(decks + 1, spec.Class switch { ProcClass.Corvette => 3, ProcClass.Frigate => 4, _ => 5 }); // (a liner's extra deck of cabins)
        if (style == ProcHull.Stepped)
            decks = spec.Class switch { ProcClass.Corvette => dk.Int(2, 3), ProcClass.Frigate => dk.Int(3, 4), _ => dk.Int(4, 5) };
        else if (style == ProcHull.Barge)
            decks = spec.Class switch { ProcClass.Corvette => 1, ProcClass.Frigate => dk.Chance(0.55) ? 1 : 2, _ => 2 };
        if (spec.Decks is { } fixedDecks)
            decks = fixedDecks;
        if (spec.Class == ProcClass.Corvette && decks >= 2)
            len = Q(len + 2.5 * (decks - 1), 0.5); // (a small hull of decks over decks: room for the lobby amidships under its taper)
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
        if (decks == 3)
            beam *= 1.08;
        else if (decks >= 4)
            beam *= 1.08 + 0.04 * (decks - 3); // (a tall hull: wide enough for its upper decks' lobbies)
        else if (decks == 2 && spec.Class == ProcClass.Corvette)
            beam *= 1.06; // (a small hull of two decks: room for the lobby's ladder on the upper one)
        beam = Q(beam);
        var h = dice.For("hull.heights");
        var keelDepth = Q(spec.Class switch { ProcClass.Corvette => h.Range(0.95, 1.2), ProcClass.Frigate => h.Range(1.1, 1.45), _ => h.Range(1.3, 1.7) });
        var spineGap = Q(style == ProcHull.Barge ? h.Range(2.8, 3.4) : h.Range(0.65, 1.05)); // (a barge's: its well's depth)
        var roof = Storey * decks + Slab;
        var keelY = -keelDepth;
        var topY = roof + spineGap;
        var beamY = Q(roof * (style == ProcHull.Barge ? h.Range(0.3, 0.5) : h.Range(0.36, 0.56)));
        var f = dice.For("hull.section");
        // The midbody's half profile: fractions of the beam for the keel's edge, the chines and the spine's edge, by family.
        var (f0, f1, f3, f4) = style switch
        {
            ProcHull.Chisel => (f.Range(0.42, 0.52), f.Range(0.86, 0.93), f.Range(0.8, 0.88), f.Range(0.4, 0.5)),
            ProcHull.Dart => (f.Range(0.27, 0.36), f.Range(0.72, 0.82), f.Range(0.7, 0.8), f.Range(0.27, 0.36)),
            ProcHull.Barge => (f.Range(0.72, 0.82), f.Range(0.95, 0.98), f.Range(0.97, 1.0), f.Range(0.86, 0.92)),
            _ => (f.Range(0.64, 0.74), f.Range(0.94, 0.98), f.Range(0.92, 0.97), f.Range(0.62, 0.74)),
        };
        var boxy = style is ProcHull.Block or ProcHull.Stepped or ProcHull.Barge;
        var y1 = Q(keelY * f.Range(boxy ? 0.55 : 0.05, boxy ? 0.8 : 0.4));
        var y3 = style == ProcHull.Barge
            ? Q(roof + spineGap * f.Range(0.55, 0.75)) // (the bulwarks stand straight up from the beam, their top flared in)
            : Q(roof + spineGap * f.Range(decks > 1 ? -0.1 : -0.35, decks > 1 ? 0.4 : 0.3)); // (decks over decks: the upper chine near the roof, so the top deck keeps a width)
        Pt[] mid = [new(Q(beam * f0), keelY), new(Q(beam * f1), y1), new(beam, beamY), new(Q(beam * f3), y3), new(Q(beam * f4), topY)];

        // Shape keys along the length: (z as a share of the length, width scale, the section's bottom and top as shares of the
        // midbody's height from the keel); the family's, each nudged.
        var k = dice.For("hull.keys");
        double J(double v, double by) => v + k.Range(-by, by);
        var keys = new List<(double Z, double W, double Bot, double Top)>();
        string bow, stern;
        var aftF = 1.0; // (a stepped hull's low deck aft of its tower: the stern keys squashed to it)
        var towerAft = false;
        switch (style)
        {
            case ProcHull.Chisel:
                {
                    var low = k.Range(0.22, 0.36); // (how low the chisel's tip sits)
                    keys.Add((0, J(0.09, 0.02), low, low + J(0.24, 0.05)));
                    keys.Add((J(0.05, 0.01), J(0.58, 0.04), J(0.12, 0.03), J(0.84, 0.04)));
                    keys.Add((J(0.18, 0.02), J(0.86, 0.03), J(0.04, 0.02), J(0.97, 0.02)));
                    keys.Add((J(0.29, 0.02), 1, 0, 1));
                    bow = low < 0.29 ? "low chisel" : "chisel";
                    break;
                }
            case ProcHull.Dart:
                {
                    var mid0 = J(0.42, 0.04);
                    keys.Add((0, J(0.035, 0.01), J(0.46, 0.04), J(0.56, 0.04)));
                    keys.Add((mid0 * 0.26, J(0.3, 0.04), J(0.3, 0.04), J(0.74, 0.04)));
                    keys.Add((mid0 * 0.55, J(0.6, 0.04), J(0.13, 0.03), J(0.89, 0.03)));
                    keys.Add((mid0 * 0.82, J(0.88, 0.03), J(0.03, 0.02), J(0.98, 0.01)));
                    keys.Add((mid0, 1, 0, 1));
                    bow = mid0 > 0.42 ? "long dart" : "dart";
                    break;
                }
            case ProcHull.Barge:
                {
                    var rake = k.Range(0.3, 0.48); // (how high the raked bow's foot sits)
                    keys.Add((0, J(0.8, 0.04), rake, J(0.985, 0.01)));
                    keys.Add((J(0.07, 0.015), J(0.95, 0.02), J(0.1, 0.03), 1));
                    keys.Add((J(0.14, 0.02), 1, 0, 1));
                    bow = rake > 0.4 ? "high raked barge" : "raked barge";
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
                        keys.Add((0, face, J(0.14, 0.03) * lowF, J(0.86, 0.03) * lowF));
                        keys.Add((J(0.05, 0.01), J(0.9, 0.03), J(0.04, 0.02) * lowF, J(0.97, 0.02) * lowF));
                        keys.Add((J(0.12, 0.015), 1, 0, lowF));
                        keys.Add(((len - tower - ramp) / len, 1, 0, lowF));
                        keys.Add(((len - tower) / len, 1, 0, 1));
                        bow = "low block, tower aft";
                    }
                    else
                    {
                        keys.Add((0, face, J(0.13, 0.03), J(0.88, 0.03)));
                        keys.Add((J(0.045, 0.01), J(0.9, 0.03), J(0.04, 0.02), J(0.97, 0.02)));
                        keys.Add((J(0.11, 0.015), 1, 0, 1));
                        keys.Add((tower / len, 1, 0, 1));
                        keys.Add(((tower + ramp) / len, 1, 0, lowF));
                        aftF = lowF;
                        bow = "block, tower fore";
                    }
                    break;
                }
            default:
                {
                    var face = k.Range(0.6, 0.74); // (the blunt bow's face)
                    keys.Add((0, face, J(0.13, 0.03), J(0.88, 0.03)));
                    keys.Add((J(0.045, 0.01), J(0.9, 0.03), J(0.04, 0.02), J(0.97, 0.02)));
                    keys.Add((J(0.11, 0.015), 1, 0, 1));
                    bow = face > 0.67 ? "blunt block" : "chamfered block";
                    break;
                }
        }
        var aft = dice.For("hull.stern");
        var sternKind = style switch
        {
            ProcHull.Dart => aft.Weighted(("pinched", 0.6), ("straight", 0.4)),
            ProcHull.Barge => "square",
            ProcHull.Stepped => aft.Weighted(("straight", 0.6), ("pinched", 0.4)),
            _ => aft.Weighted(("swell", 0.45), ("straight", 0.3), ("pinched", 0.25)),
        };
        var midEnd = style switch { ProcHull.Dart => J(0.84, 0.03), ProcHull.Block => J(0.9, 0.02), ProcHull.Barge or ProcHull.Stepped => J(0.92, 0.015), _ => J(0.8, 0.03) };
        keys.Add((midEnd, 1, 0, aftF));
        switch (sternKind)
        {
            case "swell":
                keys.Add((J(0.9, 0.015), J(1.045, 0.015), 0.0, 1.0));
                keys.Add((1, J(1.02, 0.02), J(0.1, 0.03), J(0.99, 0.01)));
                break;
            case "pinched":
                keys.Add((1, J(0.84, 0.03), J(0.16, 0.04) * aftF, J(0.95, 0.02) * aftF));
                break;
            case "square":
                keys.Add((1, J(0.985, 0.01), J(0.05, 0.02), 1));
                break;
            default:
                keys.Add((1, J(0.97, 0.02), J(0.08, 0.03) * aftF, J(0.99, 0.01) * aftF));
                break;
        }
        stern = sternKind;
        // Stations from the keys, at least a metre apart, on a 5 cm grid.
        var span = topY - keelY;
        var stations = new List<Station>();
        foreach (var (zf, w, bot, top) in keys)
        {
            var z = Q(Math.Clamp(zf, 0, 1) * len, 0.5);
            if (stations.Count > 0 && z < stations[^1].Z + 1.0)
                z = stations[^1].Z + 1.0;
            var yb = keelY + bot * span;
            var yt = keelY + top * span;
            var p = new Pt[5];
            for (var i = 0; i < 5; i++)
            {
                var t = (mid[i].Y - keelY) / span;
                p[i] = new Pt(Q(Math.Max(0.06, mid[i].H * w), 0.01), Q(yb + t * (yt - yb), 0.01));
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
            MidZ0 = midZ0, MidZ1 = midZ1, Mid = mid, Role = role, TowerAft = towerAft,
        };
        traits.Add(("Class", spec.Class.ToString().ToLowerInvariant()));
        traits.Add(("Role", role.ToString().ToLowerInvariant()));
        traits.Add(("Hull", style.ToString().ToLowerInvariant()));
        traits.Add(("Bow", bow));
        traits.Add(("Stern", stern));
        traits.Add(("Length", $"{M(len)} m"));
        traits.Add(("Beam", $"{M(beam * 2)} m"));
        traits.Add(("Decks", decks.ToString(CultureInfo.InvariantCulture)));
        traits.Add(("Height", $"{M(topY - keelY)} m"));
        if (grow > 0)
            traits.Add(("Grown", $"{8 * grow}% longer and {7 * grow}% wider than its seed's, to fit its rooms"));

        // A barge's open well: the top of the midbody open between bulwarks down to the roof, where containers stand.
        if (style == ProcHull.Barge)
        {
            var wl = dice.For("hull.well");
            double w0 = Q(midZ0 + wl.Range(0.5, 2.5), 0.5), w1 = Q(midZ1 - wl.Range(0.5, 2.5), 0.5);
            if (w1 - w0 >= 6)
            {
                shape.WellZ0 = w0;
                shape.WellZ1 = w1;
                shape.WellHalf = Q(mid[4].H - wl.Range(0.45, 0.7), 0.05);
            }
            traits.Add(("Well", shape.HasWell ? $"{M(w1 - w0)} m long, {M(shape.WellHalf * 2)} m wide, {M(spineGap)} m deep" : "none"));
        }

        // Engine nacelles, one each side on a pylon, by role and family: past the beam, along the aft part.
        var n = dice.For("hull.nacelles");
        var roll = n.Chance(role switch { ProcRole.Gunship => 0.35, ProcRole.Freighter => 0.22, ProcRole.Miner => 0.15, ProcRole.Tender => 0.45, _ => 0.3 }
                            * style switch { ProcHull.Chisel or ProcHull.Dart => 1.3, ProcHull.Barge => 0.5, ProcHull.Stepped => 0.8, _ => 1.0 });
        if (spec.Nacelles || roll)
        {
            var es = spec.Class == ProcClass.Corvette ? 2 : 3;
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
    }
}
