using System.Numerics;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class MainframeParts
{
    // ---- The plan, in ship metres (docs/sandbox/constructs/mainframe.md "Zones")

    /// <summary>The transverse bulkheads: each zone's ends.</summary>
    private static readonly double[] Bulkheads = [30, 90, 160, 230, 300, 420, 580, 700, 790, 860, 930];

    // Each zone by its fore bulkhead: the bow's sensors, the crew's decks, the marines' decks, the forward missile field, the hangar over
    // the boarding deck, command over the Zero Day's berth, the shell vault over the yard, the aft missile field, the reactors, the engine
    // room, the drives.
    private const double ZFore = 30, ZCrew = 90, ZMarines = 160, ZMissilesF = 230, ZHangar = 300, ZCommand = 420, ZVault = 580, ZMissilesA = 700, ZReactors = 790, ZEngines = 860, ZDrives = 930;

    // Deck tops.
    private const double YardDeck = 3.9, BoardingDeck = 15, MainDeck = 30, UpperDeck = 34.5, TopDeck = 39, CitadelRoof = 43.5, HangarDeck = 52;
    private const double CitadelX = 18, CorridorX = 1.6, DoorW = 1.5, DoorH = 2.25;

    /// <summary>How far inside the skin the inner structure stops (decks, bulkheads), so nothing pokes through the plating.</summary>
    private const double Inset = 0.6;

    /// <summary>The citadel's runs: decks of rooms over the main deck under a roof, each a zone long.</summary>
    private static readonly (double Z0, double Z1, int Storeys)[] Citadels = [(ZCrew, ZMarines, 3), (ZMarines, ZMissilesF, 3), (ZHangar, ZCommand, 1), (ZCommand, ZVault, 3)];

    private static void Lay(Writer w)
    {
        w.Root(NodeName, Vector3.Zero, 0); // step 0: the body's origin
        // The structure first (decks and bulkheads), then the skin on frame nodes welded to it (each weld rebuilds the body, so
        // they come while it is small), then everything inside.
        Floors(w);
        foreach (var z in Bulkheads)
            Bulkhead(w, z, BulkheadHoles(z));
        var facets = Facets();
        var planes = facets.ToDictionary(f => (f.Band, f.F), f => f.Plane);
        Plating(w, facets);
        Closures(w);
        ArmourSlabs(w, facets);
        BeamLights(w, facets);
        BulkheadDoors(w);
        Citadel(w);
        Passages(w, planes);
        LayLifts(w);
        Bays(w, planes);
    }

    // ---- Decks

    private static readonly Look DeckLook = new(Deck, "tread plate"), CeilLook = new(Ceil, "painted steel");

    private static void Floors(Writer w)
    {
        Floor(w, MainDeck, 25, ZDrives - 2, 3.0, new Look(Deck, "tread plate"));
        foreach (var (z0, z1, n) in Citadels)
            for (var k = 1; k <= n; k++)
            {
                var y = MainDeck + k * Storey4;
                Slab(w, y, -CitadelX - 0.06, CitadelX + 0.06, z0 + 0.07, z1 - 0.07, ShaftHoles(y, -CitadelX, CitadelX, z0, z1), k == n ? CeilLook : DeckLook);
            }
        Floor(w, HangarDeck, ZHangar + 0.07, ZCommand - 0.07, 1.0, new Look(BayFloor, "tread plate"), HangarDeckHoles());
        Floor(w, BoardingDeck, ZHangar + 0.07, ZCommand - 0.07, 1.2, DeckLook, BoardingDeckHoles());
        Floor(w, YardDeck, ZVault + 0.07, ZMissilesA - 0.07, 1.0, new Look(BayFloor, "tread plate"));
        Slab(w, CitadelRoof, -40, 40, ZVault + 0.07, ZMissilesA - 0.07, [], CeilLook); // (the vault's ceiling)
        foreach (var (z0, z1, y) in MissileDecks)
            Floor(w, y, z0 + 0.07, z1 - 0.07, 1.0, new Look(Grate, "tread plate"));
    }

    /// <summary>A deck's plate at height y (its top), from z0 to z1, the hull's section there less <paramref name="inset"/> each side.</summary>
    private static List<int> Floor(Writer w, double y, double z0, double z1, double inset, Look look, List<Hole>? holes = null)
    {
        var pl = Flat(new D3(0, y, 0), PY, PZ, $"deck at {y:0.#} m");
        var poly = Outline(y, z0, z1, inset);
        holes ??= [];
        holes.AddRange(ShaftHoles(y, poly.Min(p => p.V) / 100, poly.Max(p => p.V) / 100, z0, z1));
        return Plate(w, pl, poly, holes, DeckH, -Thick(DeckH) / 2 - 0.002, look);
    }

    /// <summary>The hull's section at height y from z0 to z1 as an outline in a deck's plane (V = x, U = z, cm), less inset each side: exact at the
    /// stations and where the height crosses a chine (between those every side is one facet's straight line).</summary>
    private static List<(double V, double U)> Outline(double y, double z0, double z1, double inset)
    {
        var zs = new SortedSet<double> { z0, z1 };
        foreach (var s in Stations)
            if (s.Z > z0 && s.Z < z1)
                zs.Add(s.Z);
        for (var i = 0; i + 1 < Stations.Length; i++)
            for (var j = 0; j < 5; j++)
            {
                var (za, sa, ya) = Stations[i];
                var (zb, sb, yb) = Stations[i + 1];
                double y1 = ya + sa * Mid[j].Y, y2 = yb + sb * Mid[j].Y;
                if (Math.Abs(y2 - y1) < 1e-9 || (y - y1) * (y - y2) > 0)
                    continue;
                var z = za + (zb - za) * (y - y1) / (y2 - y1);
                if (z > z0 && z < z1)
                    zs.Add(z);
            }
        var side = zs.Select(z => (W: Math.Max(0, HalfWidth(z, y) - inset), Z: z)).ToList();
        var poly = new List<(double V, double U)>();
        foreach (var (wd, z) in side)
            poly.Add((wd * 100, z * 100));
        for (var i = side.Count - 1; i >= 0; i--)
            poly.Add((-side[i].W * 100, side[i].Z * 100));
        return Dedupe(poly);
    }

    private static List<(double V, double U)> Dedupe(List<(double V, double U)> poly)
    {
        var o = new List<(double V, double U)>();
        foreach (var p in poly)
            if (o.Count == 0 || Math.Abs(o[^1].V - p.V) > 0.5 || Math.Abs(o[^1].U - p.U) > 0.5)
                o.Add(p);
        if (o.Count > 1 && Math.Abs(o[0].V - o[^1].V) <= 0.5 && Math.Abs(o[0].U - o[^1].U) <= 0.5)
            o.RemoveAt(o.Count - 1);
        return o;
    }

    /// <summary>A convex outline moved in by d (cm) on every side.</summary>
    private static List<(double V, double U)> Shrink(IReadOnlyList<(double V, double U)> poly, double d)
    {
        var n = poly.Count;
        double area = 0;
        for (var i = 0; i < n; i++)
            area += poly[i].V * poly[(i + 1) % n].U - poly[(i + 1) % n].V * poly[i].U;
        var sign = area > 0 ? 1 : -1; // (anticlockwise: the inside is on the left of each edge)
        var lines = new List<(double PV, double PU, double DV, double DU)>();
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (poly[i], poly[(i + 1) % n]);
            double dv = b.V - a.V, du = b.U - a.U, len = Math.Sqrt(dv * dv + du * du);
            if (len < 1e-6)
                continue;
            double nv = -du / len * sign, nu = dv / len * sign; // (the inward normal)
            lines.Add((a.V + nv * d, a.U + nu * d, dv, du));
        }
        var o = new List<(double V, double U)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var (p, q) = (lines[(i + lines.Count - 1) % lines.Count], lines[i]);
            var den = p.DV * q.DU - p.DU * q.DV;
            if (Math.Abs(den) < 1e-9)
                continue;
            var t = ((q.PV - p.PV) * q.DU - (q.PU - p.PU) * q.DV) / den;
            o.Add((p.PV + p.DV * t, p.PU + p.DU * t));
        }
        return Dedupe(o);
    }

    /// <summary>A convex outline cut to the band v0..v1 and u0..u1 (any bound may be infinite).</summary>
    private static List<(double V, double U)> Clip(IReadOnlyList<(double V, double U)> poly, double v0, double v1, double u0, double u1)
    {
        var cur = poly.ToList();
        foreach (var (axis, bound, keepAbove) in new[] { (0, v0, true), (0, v1, false), (1, u0, true), (1, u1, false) })
        {
            if (double.IsInfinity(bound) || cur.Count == 0)
                continue;
            var next = new List<(double V, double U)>();
            for (var i = 0; i < cur.Count; i++)
            {
                var (a, b) = (cur[i], cur[(i + 1) % cur.Count]);
                double ka = axis == 0 ? a.V : a.U, kb = axis == 0 ? b.V : b.U;
                bool ina = keepAbove ? ka >= bound : ka <= bound, inb = keepAbove ? kb >= bound : kb <= bound;
                if (ina)
                    next.Add(a);
                if (ina != inb)
                {
                    var t = (bound - ka) / (kb - ka);
                    next.Add((a.V + (b.V - a.V) * t, a.U + (b.U - a.U) * t));
                }
            }
            cur = next;
        }
        return Dedupe(cur);
    }

    // ---- Bulkheads

    private static readonly Look BulkheadLook = new(Bay, "painted steel");

    /// <summary>A transverse bulkhead at z: the hull's section there, less the inset, in plate cut round the openings asked.</summary>
    private static List<int> Bulkhead(Writer w, double z, List<Hole> holes)
    {
        var pl = Flat(new D3(0, 0, z), PZ, PY, $"bulkhead at {z:0} m");
        var sec = Ring(z);
        var poly = Shrink(sec.Select(p => pl.Of(p)).ToList(), Inset * 100);
        return Plate(w, pl, poly, holes, BulkheadH, 0, BulkheadLook);
    }

    /// <summary>A doorway in a bulkhead's plane at x, standing on the floor at y (ship metres): U is up, V is -x.</summary>
    private static Hole BulkheadDoor(double x, double y, double wide = DoorW, double high = DoorH) => new((y + high / 2) * 100, -x * 100, high * 100, wide * 100);

    /// <summary>The doorways through a bulkhead: (x, floor) in ship metres.</summary>
    private static List<(double X, double Y)> Doorways(double z)
    {
        var o = new List<(double, double)>();
        if (z >= 60 && z <= ZEngines)
            o.Add((0, MainDeck)); // (the main corridor runs through every bulkhead aft of the bow's frames to the engine room)
        if (z > ZHangar && z < ZEngines)
        {
            o.Add((PassageIn + 1.5, MainDeck)); // (the flank passages, both sides)
            o.Add((-(PassageIn + 1.5), MainDeck));
        }
        foreach (var (z0, z1, n) in Citadels)
            if (Math.Abs(z - z1) < 0.01 && Citadels.Any(c => Math.Abs(c.Z0 - z) < 0.01 && c.Storeys == n))
                for (var k = 1; k < n; k++)
                    o.Add((0, MainDeck + k * Storey4)); // (a citadel's corridors on into the next)
        return o;
    }

    private static List<Hole> BulkheadHoles(double z) => [.. Doorways(z).Select(d => BulkheadDoor(d.X, d.Y))];

    private static void BulkheadDoors(Writer w)
    {
        foreach (var z in Bulkheads)
            foreach (var (x, y) in Doorways(z))
                w.Root(DoorPart, B(x - 0.72, y + 1.125, z), 0, DoorLook);
    }

    // ---- The skin

    /// <summary>
    /// The hull's skin, facet by facet: a flat facet on the body's frame, a slanting one on a frame node welded to the nearest bulkhead
    /// or deck; the skin in sheets the Laser Cutter cut round the openings (airlocks, bay doors, cells), a gap left where the berth opens.
    /// </summary>
    private static void Plating(Writer w, List<Facet> facets)
    {
        foreach (var f in facets)
        {
            if (!f.Flat)
            {
                var (pos, rot) = NodePose(f.Plane);
                f.Plane.Node = w.Weld(w.Nearest(pos), NodeName, pos, rot);
            }
            var look = new Look(f.F is 4 or 9 ? Skin2 : Skin, "painted plating");
            var holes = SkinHoles(f);
            if (SkinGap(f) is { } gap)
                foreach (var piece in Around(f.Poly, gap))
                    Plate(w, f.Plane, piece, holes.Where(h => Within(h, piece)).ToList(), SkinH, 0, look, 3200, 6400);
            else
                Plate(w, f.Plane, f.Poly, holes, SkinH, 0, look, 3200, 6400);
        }
    }

    /// <summary>An outline less a rectangle (V0..V1, U0..U1) inside it: the pieces either side of it and before and after it.</summary>
    private static List<List<(double V, double U)>> Around(IReadOnlyList<(double V, double U)> poly, (double V0, double V1, double U0, double U1) g)
    {
        var inf = double.PositiveInfinity;
        var o = new List<List<(double V, double U)>>
        {
            Clip(poly, -inf, g.V0, -inf, inf), Clip(poly, g.V1, inf, -inf, inf),
            Clip(poly, g.V0, g.V1, -inf, g.U0), Clip(poly, g.V0, g.V1, g.U1, inf),
        };
        return [.. o.Where(p => p.Count >= 3)];
    }

    private static bool Within(Hole h, IReadOnlyList<(double V, double U)> poly) =>
        h.V0 >= poly.Min(p => p.V) && h.V1 <= poly.Max(p => p.V) && h.U0 >= poly.Min(p => p.U) && h.U1 <= poly.Max(p => p.U);

    /// <summary>The stern's transom (a dark armoured wall with the six drives' openings, in bands round them) and the bow's cap.</summary>
    private static void Closures(Writer w)
    {
        var look = new Look(Armour2, "plated steel");
        var pl = Flat(new D3(0, 0, Stern - Thick(ArmourH) / 2 - 0.03), PZ, PY, "transom");
        var poly = Shrink(Ring(Stern).Select(p => pl.Of(p)).ToList(), 6);
        var holes = Drives.Select(d => new Hole(d.Y * 100, -d.X * 100, DriveHole * 100, DriveHole * 100, DriveHole * 100 * 0.29)).ToList();
        // (in bands across, each a column of sheets: no sheet's strip may cross a drive's opening, and the outline bends only at the sides)
        var cuts = new List<double> { double.NegativeInfinity };
        foreach (var x in Drives.Select(d => -d.X).Distinct().OrderBy(v => v))
        {
            cuts.Add((x - DriveHole / 2 - 0.5) * 100);
            cuts.Add((x + DriveHole / 2 + 0.5) * 100);
        }
        cuts.Add(double.PositiveInfinity);
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            var band = Clip(poly, cuts[i], cuts[i + 1], double.NegativeInfinity, double.PositiveInfinity);
            if (band.Count >= 3)
                Plate(w, pl, band, holes.Where(h => Within(h, band)).ToList(), ArmourH, 0, look, 3200, 6400);
        }
        var bow = Flat(new D3(0, 0, Bow + Thick(ArmourH) / 2 + 0.03), NZ, PY, "bow cap");
        Plate(w, bow, Shrink(Ring(Bow).Select(p => bow.Of(p)).ToList(), 3), [], ArmourH, 0, look);
    }

    // ---- Armour and lights

    /// <summary>
    /// The armour: dark slabs proud of the pale skin on the flanks, belts and shoulders, about 28 m long with 2 m between, staggered from
    /// facet to facet; the belts and upper belts take a second, narrower tier proud of the first (the Zero Day's steps). Each hangs on its
    /// facet's frame node, plain plate off the Extruder. None over an opening.
    /// </summary>
    private static void ArmourSlabs(Writer w, List<Facet> facets)
    {
        foreach (var f in facets)
        {
            if (f.Flat || f.F is 4 or 9 || f.Band == 0)
                continue;
            double umin = f.Poly.Min(p => p.U), umax = f.Poly.Max(p => p.U);
            var holes = SkinHoles(f);
            var twoTier = f.F is 1 or 2 or 6 or 7;
            var stagger = f.F % 2 * 1500;
            for (var u = umin + 150 + stagger - 3000; u < umax - 400; u += 3000)
            {
                double a = Math.Max(u, umin + 150), b = Math.Min(u + 2800, umax - 150);
                if (b - a < 800)
                    continue;
                if (holes.Any(h => h.U1 > a - 60 && h.U0 < b + 60) || Gapped(f, a, b))
                {
                    // (an opening in its way: the slab in the part before and after it, if long enough)
                    foreach (var (pa, pb) in Free(a, b, holes.Select(h => (h.U0 - 60, h.U1 + 60)).Concat(GapSpan(f))))
                        Slab2(w, f, pa, pb, twoTier);
                    continue;
                }
                Slab2(w, f, a, b, twoTier);
            }
        }
    }

    private static IEnumerable<(double A, double B)> GapSpan(Facet f) => SkinGap(f) is { } g ? [(g.U0 - 60, g.U1 + 60)] : [];

    private static bool Gapped(Facet f, double a, double b) => SkinGap(f) is { } g && g.U1 + 60 > a && g.U0 - 60 < b;

    /// <summary>The parts of a..b clear of the spans given, at least 8 m long.</summary>
    private static IEnumerable<(double A, double B)> Free(double a, double b, IEnumerable<(double A, double B)> spans)
    {
        var pieces = new List<(double A, double B)> { (a, b) };
        foreach (var (sa, sb) in spans)
        {
            var next = new List<(double A, double B)>();
            foreach (var (pa, pb) in pieces)
            {
                if (sb <= pa || sa >= pb)
                {
                    next.Add((pa, pb));
                    continue;
                }
                if (sa > pa)
                    next.Add((pa, sa));
                if (sb < pb)
                    next.Add((sb, pb));
            }
            pieces = next;
        }
        return pieces.Where(p => p.B - p.A >= 800);
    }

    /// <summary>One slab (and its second tier) on a facet from U a to b (cm).</summary>
    private static void Slab2(Writer w, Facet f, double a, double b, bool twoTier)
    {
        var (la, ha) = Span(f.Poly, a);
        var (lb, hb) = Span(f.Poly, b);
        double lo = Math.Max(la, lb), hi = Math.Min(ha, hb);
        var inset = Math.Max(150, (hi - lo - 3200) / 2 + 2);
        double v0 = Even(lo + inset), v1 = Even(hi - inset);
        if (v1 - v0 < 400)
            return;
        var gap = 0.05;
        var off1 = Thick(SkinH) / 2 + gap + Thick(ArmourH) / 2;
        var look = new Look(f.F % 2 == 0 ? Armour : Armour2, "plated steel");
        Sheet(w, f.Plane, new Tile { V0 = (int)v0, V1 = (int)v1, U0 = Five(a), U1 = Five(b) }, ArmourH, off1, look);
        if (!twoTier)
            return;
        double w0 = Even(v0 + 300), w1 = Even(v1 - 300), a2 = Five(a + 300), b2 = Five(b - 300);
        if (w1 - w0 < 400 || b2 - a2 < 600)
            return;
        var off2 = off1 + Thick(ArmourH) + gap;
        Sheet(w, f.Plane, new Tile { V0 = (int)w0, V1 = (int)w1, U0 = (int)a2, U1 = (int)b2 }, ArmourH, off2, new Look(Armour2, "plated steel"));
    }

    /// <summary>The link-blue light along the beam line, both sides: strips on the upper belt just over the chine, 10 m each, in the
    /// gaps between the belt's and the upper belt's armour.</summary>
    private static void BeamLights(Writer w, List<Facet> facets)
    {
        foreach (var f in facets)
        {
            if (f.F is not (2 or 6) || f.Band == 0)
                continue;
            double umin = f.Poly.Min(p => p.U), umax = f.Poly.Max(p => p.U);
            for (var u = umin + 50; u + LightLen * 100 <= umax - 50; u += LightLen * 100)
            {
                // (the chine's edge of the facet at this U: the low side for the starboard upper belt (its V runs from the beam up), the
                // high side for the port one)
                var (lo, hi) = Span(f.Poly, u + LightLen * 50);
                var v = f.F == 2 ? lo + 70 : hi - 70;
                OnPlane(w, f.Plane, LightName, v, Thick(SkinH) / 2 + 0.004, u, 0);
            }
        }
    }
}
