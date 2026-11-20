using System.Globalization;

namespace CallOfSlop.ProcShips;

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
    /// The hull for a spec: its length, beam and heights by class and seed, the midbody section by hull family, then stations for the
    /// bow and stern from the family's shape keys, each nudged by the dice. Every choice is written into <paramref name="traits"/>.
    /// </summary>
    public static ProcHullShape Make(ProcShipSpec spec, ProcDice dice, List<(string Key, string Value)> traits)
    {
        var style = spec.Hull ?? dice.For("hull.style").Weighted((ProcHull.Chisel, 0.45), (ProcHull.Dart, 0.3), (ProcHull.Block, 0.25));
        var size = dice.For("hull.size");
        var (len, beam, decks) = spec.Class switch
        {
            ProcClass.Corvette => (Q(size.Range(30, 40), 0.5), size.Range(3.9, 4.8), size.Chance(0.55) ? 1 : 2),
            ProcClass.Frigate => (Q(size.Range(44, 60), 0.5), size.Range(5.1, 6.5), size.Chance(0.85) ? 2 : 3),
            _ => (Q(size.Range(64, 84), 0.5), size.Range(6.4, 8.0), size.Chance(0.55) ? 2 : 3),
        };
        if (spec.Decks is { } fixedDecks)
            decks = fixedDecks;
        if (style == ProcHull.Dart)
            beam *= 1.06; // (its section is slimmer: a wider beam keeps rooms in it)
        if (decks == 3)
            beam *= 1.08;
        else if (decks == 2 && spec.Class == ProcClass.Corvette)
            beam *= 1.06; // (a small hull of two decks: room for the lobby's ladder on the upper one)
        beam = Q(beam);
        var h = dice.For("hull.heights");
        var keelDepth = Q(spec.Class switch { ProcClass.Corvette => h.Range(0.95, 1.2), ProcClass.Frigate => h.Range(1.1, 1.45), _ => h.Range(1.3, 1.7) });
        var spineGap = Q(h.Range(0.65, 1.05));
        var roof = Storey * decks + Slab;
        var keelY = -keelDepth;
        var topY = roof + spineGap;
        var beamY = Q(roof * h.Range(0.36, 0.56));
        var f = dice.For("hull.section");
        // The midbody's half profile: fractions of the beam for the keel's edge, the chines and the spine's edge, by family.
        var (f0, f1, f3, f4) = style switch
        {
            ProcHull.Chisel => (f.Range(0.42, 0.52), f.Range(0.86, 0.93), f.Range(0.8, 0.88), f.Range(0.4, 0.5)),
            ProcHull.Dart => (f.Range(0.27, 0.36), f.Range(0.72, 0.82), f.Range(0.7, 0.8), f.Range(0.27, 0.36)),
            _ => (f.Range(0.64, 0.74), f.Range(0.94, 0.98), f.Range(0.92, 0.97), f.Range(0.62, 0.74)),
        };
        var y1 = Q(keelY * f.Range(style == ProcHull.Block ? 0.55 : 0.05, style == ProcHull.Block ? 0.8 : 0.4));
        var y3 = Q(roof + spineGap * f.Range(decks > 1 ? -0.1 : -0.35, decks > 1 ? 0.4 : 0.3)); // (decks over decks: the upper chine near the roof, so the top deck keeps a width)
        Pt[] mid = [new(Q(beam * f0), keelY), new(Q(beam * f1), y1), new(beam, beamY), new(Q(beam * f3), y3), new(Q(beam * f4), topY)];

        // Shape keys along the length: (z as a share of the length, width scale, the section's bottom and top as shares of the
        // midbody's height from the keel); the family's, each nudged.
        var k = dice.For("hull.keys");
        double J(double v, double by) => v + k.Range(-by, by);
        var keys = new List<(double Z, double W, double Bot, double Top)>();
        string bow, stern;
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
        var sternKind = style == ProcHull.Dart ? aft.Weighted(("pinched", 0.6), ("straight", 0.4)) : aft.Weighted(("swell", 0.45), ("straight", 0.3), ("pinched", 0.25));
        var midEnd = style switch { ProcHull.Dart => J(0.84, 0.03), ProcHull.Block => J(0.9, 0.02), _ => J(0.8, 0.03) };
        keys.Add((midEnd, 1, 0, 1));
        switch (sternKind)
        {
            case "swell":
                keys.Add((J(0.9, 0.015), J(1.045, 0.015), 0.0, 1.0));
                keys.Add((1, J(1.02, 0.02), J(0.1, 0.03), J(0.99, 0.01)));
                break;
            case "pinched":
                keys.Add((1, J(0.84, 0.03), J(0.16, 0.04), J(0.95, 0.02)));
                break;
            default:
                keys.Add((1, J(0.97, 0.02), J(0.08, 0.03), J(0.99, 0.01)));
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
        traits.Add(("Class", spec.Class.ToString().ToLowerInvariant()));
        traits.Add(("Hull", style.ToString().ToLowerInvariant()));
        traits.Add(("Bow", bow));
        traits.Add(("Stern", stern));
        traits.Add(("Length", $"{len.ToString("0.#", CultureInfo.InvariantCulture)} m"));
        traits.Add(("Beam", $"{(beam * 2).ToString("0.#", CultureInfo.InvariantCulture)} m"));
        traits.Add(("Decks", decks.ToString(CultureInfo.InvariantCulture)));
        traits.Add(("Height", $"{(topY - keelY).ToString("0.#", CultureInfo.InvariantCulture)} m"));
        return new ProcHullShape
        {
            Stations = [.. stations], Length = len, Beam = beam, KeelY = keelY, TopY = topY, BeamY = beamY, Decks = decks, Style = style,
            MidZ0 = midZ0, MidZ1 = midZ1, Mid = mid,
        };

        static bool Same(Pt[] a, Pt[] b) => a.Zip(b).All(q => Math.Abs(q.First.H - q.Second.H) < 1e-6 && Math.Abs(q.First.Y - q.Second.Y) < 1e-6);
    }
}
