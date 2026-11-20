using System.Globalization;

namespace CallOfSlop.ProcShips;

/// <summary>A length of a deck's room block: from Z0 to Z1 the block's outer face is W from the centreline (a stepped plan, as the Zero Day's).</summary>
public readonly record struct ProcRun(double Z0, double Z1, double W);

/// <summary>A doorway in a wall run: from A along the run, W wide, open from the floor's top to Head.</summary>
public readonly record struct ProcGap(double A, double W, double Head = ProcShipPlan.DoorH);

/// <summary>A wall 0.25 m thick, from its deck's floor top to its ceiling: centred on C, from A0 to A1 along x (AlongX) or z, with its doorways.</summary>
public sealed class ProcWall(int deck, bool alongX, double a0, double a1, double c, string group)
{
    public readonly int Deck = deck;
    public readonly bool AlongX = alongX;
    public readonly double A0 = a0, A1 = a1, C = c;
    public readonly string Group = group;
    public readonly List<ProcGap> Gaps = [];
}

/// <summary>A door hung in a doorway: on its wall's centre line at the doorway's start (it slides back into the wall before A).</summary>
public sealed record ProcDoor(int Deck, string Group, bool AlongX, double A, double C, double Floor, string Kind);

/// <summary>A room's clear floor (for the gallery's labels and the sockets), on deck Deck; one that spans decks (a hold, a hangar, an atrium) is open up to deck DeckTop's ceiling.</summary>
public sealed record ProcRoom(int Deck, string Name, string Kind, double X0, double Z0, double X1, double Z1, int DeckTop = -1)
{
    /// <summary>The highest deck it is open through.</summary>
    public int Top => DeckTop < 0 ? Deck : DeckTop;
}

/// <summary>
/// A room over decks D0 to D1 at one end of the block, aft of Z (Aft) or fore of it: the slabs between its decks are open over it and
/// its walls stand deck over deck, so a hold, a hangar, a drive hall, a torpedo bay or an atrium is two decks tall or more.
/// </summary>
public sealed record ProcSpan(string Name, string Kind, int D0, int D1, double Z, bool Aft);

/// <summary>A hole in slab level Slab (0 the lowest deck's floor, Decks the roof), with a rail round it on the deck above (open at its aft edge).</summary>
public sealed record ProcHole(int Slab, double X0, double Z0, double X1, double Z1, bool Rail);

/// <summary>
/// A ladder standing on deck Deck's floor, its bottom socket at X, Z: it climbs Rise to its landing (the floor or the spine behind
/// its top); its rails stand to Top over its foot. Climbed from the fore, it lands aft (Turned: climbed from the aft, landing fore).
/// </summary>
public sealed record ProcLadder(int Deck, double X, double Z, bool Turned, double Rise, double Top, string Why);

/// <summary>
/// A docking hatch through the keel (belly) or the spine (dorsal): its opening, the sill it lies on (its top at SillTop) and the
/// shaft's lining from the deck to it. The hatch slides aft into a slot under the lining, 1.5 m.
/// </summary>
public sealed record ProcHatch(string Kind, int Deck, double X0, double Z0, double X1, double Z1, double SillTop, double Y0, double Y1);

/// <summary>A side airlock: a housing out through the void and the skin from the lobby's outer wall (XIn) to past the skin (XOut), its doorways from A.</summary>
public sealed record ProcLock(int Side, int Deck, double A, double XIn, double XOut, double Z0, double Z1, double Y0, double Y1);

/// <summary>
/// A lancer's spinal railgun (generator version 3): its barrel along the lowest deck's centreline from the muzzle (Z0: the lance's
/// tip, or the fork's root face between its rails) to the breech block (BreechZ0 to Z1) in engineering, its axis Y, HX half wide
/// and HY half high; the walls it passes have an arch for it, and it stands on saddles on the floor.
/// </summary>
public sealed record ProcBarrel(double Z0, double Z1, double BreechZ0, double Y, double HX, double HY);

/// <summary>
/// A mouth bow's opening (generator version 3): the bow's face open from X -HalfW to HalfW and from Y0 (deck D0's floor's underside)
/// to Y1, a tunnel lined through the void from the bow (z 0) back to the room block's fore wall at Z1, which is open there too: the
/// way into the hangar or hold behind it (decks D0 to D1).
/// </summary>
public sealed record ProcMouth(double HalfW, double Y0, double Y1, double Z1, int D0, int D1);

/// <summary>
/// A Module Socket plate W x L (module-sockets.md): where it goes (transom, spine, flank, floor), its kind, style and keying, the turn
/// it is fitted with, its plate's box (ship metres), the socket its b00.00 goes on and the pad under it (none on a floor).
/// </summary>
public sealed record ProcSocket(string Name, string Where, int Kind, int W, int L, int Style, double Above, bool Keyed, int Rot,
    double[] Min, double[] Max, double[] Sock, string Dir, double[]? PadMin, double[]? PadMax, int Deck, string Use);

/// <summary>
/// The interior and its ways out (docs/procships/PROCSHIPS.md): each deck's room block (stepped to stay a seeded gap inside the
/// skin), its rooms by the ship's role (a fore room, a corridor with side rooms or one hall, a lobby amidships, an aft room; at
/// either end a room that spans decks), walls and doors, the ladders between the lobbies, the side airlocks, the docking hatches,
/// the nacelles' pylons and the module sockets. Ship metres as <see cref="ProcHullShape"/>.
/// </summary>
public sealed class ProcShipPlan
{
    public const double Wall = 0.25, Half = 0.125, DoorW = 1.5, DoorH = 2.25, Margin = 0.06, Grid = 0.5;
    public const double SideMinRoom = 1.5; // (a side room's clear depth)
    public const double MinRoomLen = 3.0;
    public const double DoorSlide = 1.5; // (a Bulkhead Door's leaf slides 1.48 m into the wall before its doorway)

    public required ProcHullShape Hull { get; init; }
    public required ProcRole Role { get; init; }

    /// <summary>What the ship spent its points on (its reactors and drive) and so what is left for its payload.</summary>
    public required ProcFit Kit { get; set; }
    public double Gap, Ch, AftGap, Zl, Ll;
    public int LockDeck;

    /// <summary>The rooms that span decks at the block's ends (null: none).</summary>
    public ProcSpan? AftSpan, ForeSpan;

    /// <summary>Engineering: the aft room of this deck (-1: a hall amidships, or the aft span is the drive hall).</summary>
    public int EngDeck;

    /// <summary>Middle parts that are one hall, not a corridor with side rooms: by deck and part ('f' fore of the lobby, 'a' aft), its name and kind.</summary>
    public readonly Dictionary<(int Deck, char Part), (string Name, string Kind)> Halls = [];

    /// <summary>The halls made (middle parts that are one room).</summary>
    public readonly List<ProcRoom> HallRooms = [];
    public List<ProcRun>[] Runs = [];
    public double[] Front = [], Back = [];
    public readonly List<ProcWall> Walls = [];
    public readonly List<ProcDoor> Doors = [];
    public readonly List<ProcRoom> Rooms = [];
    public readonly List<ProcHole> Holes = [];
    public readonly List<ProcLadder> Ladders = [];
    public readonly List<ProcHatch> Hatches = [];
    public readonly List<ProcLock> Locks = [];
    public readonly List<ProcSocket> Sockets = [];
    public readonly List<(int Deck, double X0, double Z0, double X1, double Z1)> Pillars = []; // (the dorsal ladder's wall)
    public string Accent = "#2c5aa0";

    /// <summary>A lancer's spinal railgun (null: none).</summary>
    public ProcBarrel? Barrel;

    /// <summary>A mouth bow's opening (null: none, or no room behind it).</summary>
    public ProcMouth? Mouth;

    /// <summary>The arch a spinal barrel passes the walls through: half its width, and its head over the floor's top.</summary>
    public const double ArchHalf = ProcHullShape.BarrelHalf + 0.12;

    public static double ArchHead => ProcHullShape.BarrelAxis + ProcHullShape.BarrelHalfH - FloorTop(0) + 0.08;

    /// <summary>The gun deck's walls between its rooms are wide arches (the barrel and its walkways through them), this high over the floor.</summary>
    public const double GalleryHead = 2.6;

    /// <summary>The breech block's length (aft of it the barrel ends) and its half width and half height.</summary>
    public const double BreechLen = 1.6, BreechHalf = 0.8, BreechHalfH = 0.7;

    private static double Q(double v, double step = Grid) => Math.Round(v / step) * step;

    private static double Down(double v, double step) => Math.Floor(v / step + 1e-9) * step;

    public int Decks => Hull.Decks;

    public static double FloorLo(int d) => ProcHullShape.FloorLo(d);

    public static double FloorTop(int d) => ProcHullShape.FloorTop(d);

    public static double Ceil(int d) => ProcHullShape.Ceil(d);

    /// <summary>The block's outer half width on deck d at z (0: no block there).</summary>
    public double W(int d, double z)
    {
        foreach (var r in Runs[d])
            if (z >= r.Z0 - 1e-6 && z <= r.Z1 + 1e-6)
                return r.W;
        return 0;
    }

    /// <summary>The narrowest the block is on deck d between za and zb (0 if it isn't there all along).</summary>
    public double WMin(int d, double za, double zb)
    {
        var m = double.MaxValue;
        var covered = za;
        foreach (var r in Runs[d])
            if (r.Z1 > za + 1e-6 && r.Z0 < zb - 1e-6)
            {
                if (r.Z0 > covered + 1e-6)
                    return 0;
                m = Math.Min(m, r.W);
                covered = Math.Max(covered, r.Z1);
            }
        return covered < zb - 1e-6 || m == double.MaxValue ? 0 : m;
    }

    // ---- making one

    public static ProcShipPlan Make(ProcShipSpec spec, ProcRole role, ProcFit fit, ProcHullShape h, ProcDice dice, List<(string Key, string Value)> traits)
    {
        var p = new ProcShipPlan { Hull = h, Role = role, Kit = fit };
        var r = dice.For("rooms.layout");
        p.Gap = Q(r.Range(0.5, 0.9), 0.05);
        p.Ch = r.Chance(0.55) ? 0.75 : 1.0;
        p.AftGap = Q(r.Range(1.0, 1.6), 0.5);
        var look = dice.For("look.accent");
        p.Accent = look.Pick("#2c5aa0", "#a8402c", "#2f7a5a", "#c08a20", "#5a3c8c", "#2a8a9a", "#8c2c4c");
        if (role == ProcRole.Stealth)
            p.Accent = dice.For("look.stealth").Pick("#5c2a30", "#2f3f52", "#3d4a2e", "#4a3a5c"); // (dim: nothing bright to see)
        if (h.Spinal)
        {
            // The gun deck: the barrel runs down the lowest deck's middle, so it has halls either side of the lobby, no corridor.
            p.Halls[(0, 'f')] = ("Spinal gallery", "gun");
            p.Halls[(0, 'a')] = ("Capacitor gallery", "gun");
        }
        p.Runs = new List<ProcRun>[h.Decks];
        p.Front = new double[h.Decks];
        p.Back = new double[h.Decks];
        for (var d = 0; d < h.Decks; d++)
        {
            p.Runs[d] = p.MakeRuns(d);
            if (p.Runs[d].Count == 0)
                throw new ProcShipException($"deck {d + 1} has no room inside the hull");
            p.Front[d] = p.Runs[d][0].Z0;
            p.Back[d] = p.Runs[d][^1].Z1;
        }
        // The lock deck: the one the beam line crosses (its housings stick out where the hull is widest), else the nearest.
        p.LockDeck = Enumerable.Range(0, h.Decks).OrderBy(d => h.BeamY < FloorTop(d) ? FloorTop(d) - h.BeamY : h.BeamY > Ceil(d) ? h.BeamY - Ceil(d) : 0).First();
        // The lobby: one z range amidships on every deck, inside the constant midbody, a lobby on each deck stacked over the others.
        var lob = dice.For("rooms.lobby");
        p.Ll = Q(lob.Range(5.5, 6.5));
        double lo = h.MidZ0 + 1.0, hi = h.MidZ1 - 1.0 - p.Ll;
        for (var d = 0; d < h.Decks; d++)
        {
            lo = Math.Max(lo, p.Front[d] + 4.0);
            hi = Math.Min(hi, p.Back[d] - 4.5 - p.Ll);
        }
        if (hi < lo)
            throw new ProcShipException($"no room amidships for the lobby ({h.MidZ0:0.#} to {h.MidZ1:0.#} m)");
        // Clear of the nacelles where it can be: the side airlocks (amidships in the lobby) a metre or more fore of their noses.
        var lockAft = p.Ll / 2 + DoorW / 2 + 0.2 + 1.0;
        if (h.Nacelles.Count > 0 && h.Nacelles.Min(q => q.Z0) - lockAft >= lo)
            hi = Math.Min(hi, h.Nacelles.Min(q => q.Z0) - lockAft);
        var zlU = lob.Range(0.25, 0.75);
        double gLo = Math.Ceiling(lo / Grid) * Grid, gHi = Math.Floor(hi / Grid) * Grid;
        p.Zl = gLo <= gHi ? Math.Clamp(Q(lo + (hi - lo) * zlU), gLo, gHi) : gLo; // (a range with no grid step in it: the step after it)
        if (p.Zl < lo - 1e-6 || p.Zl > hi + 1e-6)
            p.Zl = Math.Ceiling(lo / Grid) * Grid;
        p.Nacelles(dice, lockAft, traits);
        // The ways out, by class: side airlocks (both sides on a frigate or a destroyer), a belly hatch and a dorsal hatch.
        var locks = dice.For("locks");
        var sides = spec.Class == ProcClass.Corvette && !locks.Chance(0.3) ? new[] { locks.Chance(0.5) ? -1 : 1 } : new[] { -1, 1 };
        var belly = locks.Chance(spec.Class switch { ProcClass.Corvette => 0.7, ProcClass.Frigate => 0.85, _ => 1.0 });
        var dorsal = locks.Chance(spec.Class switch { ProcClass.Corvette => 0.35, ProcClass.Frigate => 0.6, _ => 1.0 });
        belly &= h.Mid[0].H >= 0.85; // (the shaft's lining stands inside the keel's flat)
        dorsal &= h.Mid[4].H >= 0.85;
        if (!belly && !dorsal)
            belly = h.Mid[0].H >= 0.85;
        if (h.Decks == 1 && belly && dorsal)
            dorsal = false; // (one lobby: one shaft through its middle)
        if (h.HasWell)
            dorsal = false; // (the well is its way out on top: the lobby's ladder climbs into it)
        if (h.Spinal)
        {
            belly = false; // (the barrel runs under the lobby's middle)
            dorsal = h.Mid[4].H >= 0.85;
        }
        if (role == ProcRole.Stealth && (belly || dorsal))
            sides = []; // (a stealth ship keeps its flanks clean: a hatch through the keel or the spine will do)
        p.ChooseSpans(dice, traits);
        foreach (var sp in new[] { p.AftSpan, p.ForeSpan })
            if (sp != null)
                p.SpanHoles(sp);
        p.MakeMouth();
        for (var d = 0; d < h.Decks; d++)
            p.Layout(d, dice, sides, belly && d == 0, dorsal && d == h.Decks - 1);
        p.Fit(spec, dice, traits);
        p.MakeBarrel();
        traits.Add(("Void gap", $"{p.Gap.ToString("0.0#", CultureInfo.InvariantCulture)} m"));
        traits.Add(("Corridor", $"{(p.Ch * 2).ToString("0.#", CultureInfo.InvariantCulture)} m"));
        traits.Add(("Rooms", p.Rooms.Count(q => q.Kind != "corridor").ToString(CultureInfo.InvariantCulture)));
        var tall = p.Rooms.Where(q => q.Top > q.Deck).Select(q => $"{q.Name} over decks {q.Deck + 1}-{q.Top + 1}").ToList();
        traits.Add(("Tall rooms", tall.Count == 0 ? "none" : string.Join(", ", tall)));
        traits.Add(("Halls", p.HallRooms.Count == 0 ? "none" : string.Join(", ", p.HallRooms.Select(q => $"{q.Name} (deck {q.Deck + 1})"))));
        traits.Add(("Engineering", p.Rooms.FirstOrDefault(q => q.Kind == "engineering") is { } eng ? $"{eng.Name}, deck {eng.Deck + 1}{(eng.Top > eng.Deck ? $"-{eng.Top + 1}" : "")}" : "none"));
        traits.Add(("Side locks", sides.Length == 2 ? "both sides" : sides.Length == 0 ? "none" : sides[0] < 0 ? "port" : "starboard"));
        traits.Add(("Belly hatch", belly ? "yes" : "no"));
        traits.Add(("Dorsal hatch", dorsal ? "yes" : "no"));
        traits.Add(("Accent", p.Accent));
        traits.Add(("Fit", p.Kit.Words));
        traits.Add(("Payload", p.Kit.Budget));
        if (p.Barrel is { } bar)
            traits.Add(("Railgun", $"a spinal barrel {(bar.Z1 - bar.Z0).ToString("0.#", CultureInfo.InvariantCulture)} m long down deck 1, its breech in engineering"));
        if (h.Bow == ProcBow.Mouth)
            traits.Add(("Mouth", p.Mouth is { } m ? $"{(2 * m.HalfW).ToString("0.#", CultureInfo.InvariantCulture)} m wide, {(m.Y1 - FloorTop(m.D0)).ToString("0.#", CultureInfo.InvariantCulture)} m high, into the {p.Rooms.First(q => q.Deck == m.D0 && q.Z0 <= p.Front[m.D0] + Wall + 1e-6).Name.ToLowerInvariant()}" : "closed (no room for an opening)"));
        return p;
    }

    /// <summary>
    /// A deck's runs: on a half-metre grid, the widest the block may be (its outer face the gap inside the skin at its floor's
    /// underside and its ceiling's top) in quarter metres, never wider than toward the midbody, then runs under 3 m merged into
    /// their narrower neighbour, and the ends where it is under 1.5 m dropped.
    /// </summary>
    private List<ProcRun> MakeRuns(int d)
    {
        var h = Hull;
        double y0 = FloorLo(d), y1 = d == h.Decks - 1 ? h.Roof : FloorLo(d + 1);
        var zEnd = h.Length - AftGap;
        var n = (int)Math.Floor(zEnd / Grid + 1e-9);
        var cell = new double[n];
        for (var i = 0; i < n; i++)
        {
            double za = i * Grid, zb = za + Grid;
            var a = Math.Min(Math.Min(h.SkinHalf(za, y0), h.SkinHalf(za, y1)), Math.Min(h.SkinHalf(zb, y0), h.SkinHalf(zb, y1)));
            cell[i] = Down(a - Gap - Margin, 0.25);
        }
        // Never wider than toward the widest cell (a block that steps out and back in would be a room inside a room).
        var top = Array.IndexOf(cell, cell.Max());
        for (var i = top - 1; i >= 0; i--)
            cell[i] = Math.Min(cell[i], cell[i + 1]);
        for (var i = top + 1; i < n; i++)
            cell[i] = Math.Min(cell[i], cell[i - 1]);
        const double least = 1.5;
        var runs = new List<ProcRun>();
        for (var i = 0; i < n; i++)
        {
            if (cell[i] < least)
                continue;
            if (runs.Count > 0 && Math.Abs(runs[^1].W - cell[i]) < 1e-9 && Math.Abs(runs[^1].Z1 - i * Grid) < 1e-9)
                runs[^1] = runs[^1] with { Z1 = (i + 1) * Grid };
            else
                runs.Add(new ProcRun(i * Grid, (i + 1) * Grid, cell[i]));
        }
        // Only the longest stretch of cells wide enough (a pinched stern could leave a scrap behind a gap).
        var stretches = new List<List<ProcRun>>();
        foreach (var rr in runs)
            if (stretches.Count > 0 && Math.Abs(stretches[^1][^1].Z1 - rr.Z0) < 1e-9)
                stretches[^1].Add(rr);
            else
                stretches.Add([rr]);
        runs = stretches.Count == 0 ? [] : stretches.OrderByDescending(s => s[^1].Z1 - s[0].Z0).First();
        // Steps of half a metre at least, then, out from the widest run (the midbody) toward each end, a run under 3 m merged into
        // its outer neighbour (narrower: the merged run stays inside the skin); one left short at an end is dropped.
        for (var i = 0; i < runs.Count; i++)
            runs[i] = runs[i] with { W = Down(runs[i].W, 0.5) < least ? Down(runs[i].W, 0.25) : Down(runs[i].W, 0.5) };
        runs = Merge(runs);
        if (runs.Count == 0)
            return runs;
        var widest = 0;
        for (var i = 1; i < runs.Count; i++)
            if (runs[i].W > runs[widest].W + 1e-9 || Math.Abs(runs[i].W - runs[widest].W) < 1e-9 && runs[i].Z1 - runs[i].Z0 > runs[widest].Z1 - runs[widest].Z0)
                widest = i;
        var fore = runs.Take(widest).Reverse().ToList(); // (nearest the midbody first)
        var aft = runs.Skip(widest + 1).ToList();
        Outward(fore);
        Outward(aft);
        fore.Reverse();
        runs = Merge([.. fore, runs[widest], .. aft]);
        return runs;

        static void Outward(List<ProcRun> side)
        {
            for (var i = 0; i < side.Count;)
            {
                if (side[i].Z1 - side[i].Z0 >= MinRoomLen - 1e-6)
                {
                    i++;
                    continue;
                }
                if (i + 1 < side.Count)
                {
                    var o = side[i + 1];
                    side[i + 1] = new ProcRun(Math.Min(o.Z0, side[i].Z0), Math.Max(o.Z1, side[i].Z1), o.W);
                }
                side.RemoveAt(i);
            }
        }

        static List<ProcRun> Merge(List<ProcRun> rs)
        {
            var o = new List<ProcRun>();
            foreach (var x in rs)
                if (o.Count > 0 && Math.Abs(o[^1].W - x.W) < 1e-9)
                    o[^1] = o[^1] with { Z1 = x.Z1 };
                else
                    o.Add(x);
            return o;
        }
    }

    /// <summary>A transverse wall's z range at a boundary between runs: on the wider side, so it closes the step (the narrower run's outer wall ends on its face).</summary>
    private (double Lo, double Hi) AtBoundary(int d, double zb)
    {
        double wf = W(d, zb - 0.01), wa = W(d, zb + 0.01);
        return wa >= wf ? (zb, zb + Wall) : (zb - Wall, zb);
    }

    private bool IsBoundary(int d, double z) => Runs[d].Skip(1).Any(r => Math.Abs(r.Z0 - z) < 1e-6);

    /// <summary>A transverse wall's span for one meant to stand at about z: at a run boundary within 0.3 m, on its wider side; else centred.</summary>
    private (double Lo, double Hi) Span(int d, double z, bool foreFace)
    {
        foreach (var rr in Runs[d].Skip(1))
            if (Math.Abs(rr.Z0 - z) < 0.3)
                return AtBoundary(d, rr.Z0);
        return foreFace ? (z, z + Wall) : (z - Wall, z);
    }

    /// <summary>The widest of the runs a z range touches on deck d.</summary>
    public double WMax(int d, double za, double zb) => Runs[d].Where(r => r.Z1 > za + 1e-6 && r.Z0 < zb - 1e-6).Select(r => r.W).DefaultIfEmpty(0).Max();

    private double[] _foreEnd = [], _aftStart = [];

    /// <summary>Where deck d's fore room ends (its aft wall's fore face) and its aft room starts (its fore wall's aft face).</summary>
    public double ForeEnd(int d) => _foreEnd[d];

    public double AftStart(int d) => _aftStart[d];

    /// <summary>
    /// The nacelles' pylons, now the lobby is placed: a nacelle whose nose would hang by an airlock is shortened (dropped when that
    /// leaves it under 8 m); its pylon a third of the way along it, from inside the skin (past the rooms) out to its core.
    /// </summary>
    private void Nacelles(ProcDice dice, double lockAft, List<(string Key, string Value)> traits)
    {
        var h = Hull;
        if (h.Nacelles.Count == 0)
            return;
        var clearZ = Math.Ceiling((Zl + lockAft) / Grid - 1e-9) * Grid;
        if (h.Nacelles[0].Z0 < clearZ)
        {
            if (h.Nacelles[0].Z1 - clearZ < 8.0)
            {
                h.Nacelles.Clear();
                var i = traits.FindIndex(t => t.Key == "Nacelles");
                traits[i] = ("Nacelles", "none (no room for them clear of the airlocks)");
                return;
            }
            foreach (var q in h.Nacelles)
                q.Z0 = clearZ;
        }
        var pl = dice.For("hull.pylons");
        var n = h.Nacelles[0];
        var pz0 = Q(Math.Max(n.Body + 0.2, n.Z0 + (n.Z1 - n.Z0) * pl.Range(0.3, 0.42)));
        var pz1 = Math.Min(pz0 + Q(pl.Range(2.0, 3.5)), Math.Floor(Math.Min(n.Z1 - 1.0, h.Length - 1.0) / Grid) * Grid);
        if (pz1 - pz0 < 1.5)
            pz0 = pz1 - 1.5;
        var pt = Math.Round(pl.Range(0.3, 0.42), 2);
        var skin = double.MaxValue;
        foreach (var z in new[] { pz0, (pz0 + pz1) / 2, pz1 })
            foreach (var y in new[] { n.Y - pt, n.Y + pt })
                skin = Math.Min(skin, h.SkinHalf(z, y));
        var pin = Math.Floor((skin - 0.25) * 100) / 100;
        var rooms = Enumerable.Range(0, Decks).Where(d => FloorLo(d) < n.Y + pt + 0.3 && (d == Decks - 1 ? h.Roof : FloorLo(d + 1)) > n.Y - pt - 0.3)
            .Select(d => WMax(d, pz0 - 0.1, pz1 + 0.1)).DefaultIfEmpty(0).Max();
        if (pin < rooms + 0.1)
            throw new ProcShipException($"no room for the nacelles' pylons between the rooms ({rooms:0.##} m) and the skin ({skin:0.##} m)");
        foreach (var q in h.Nacelles)
            (q.PZ0, q.PZ1, q.PT, q.PIn) = (pz0, pz1, pt, pin);
    }

    /// <summary>
    /// The rooms that span decks, by role: aft, a cargo hold, an ore hold, a hangar or a drive hall over two decks or three (and
    /// where engineering goes then: the deck over a hold, else a hall amidships); fore, a torpedo bay or an atrium under the bridge,
    /// or behind a mouth bow the hangar or hold it opens into. A heavy drive's hall may take the aft hold's place (its trade-off);
    /// a drive hall is long enough for the reactors.
    /// </summary>
    private void ChooseSpans(ProcDice dice, List<(string Key, string Value)> traits)
    {
        var sp = dice.For("rooms.spans");
        var sideMin = Ch + Wall + SideMinRoom + Wall;
        var (aName, aKind, aChance) = Role switch
        {
            ProcRole.Freighter => ("Cargo hold", "hold", 0.85),
            ProcRole.Miner => ("Ore hold", "hold", 0.75),
            ProcRole.Tender => ("Hangar", "hangar", 0.85),
            ProcRole.Carrier => ("Hangar", "hangar", 0.95),
            ProcRole.Salvager => ("Salvage hold", "hold", 0.8),
            ProcRole.IceHauler => ("Ice hold", "hold", 0.5),
            ProcRole.Gunship => ("Drive hall", "engineering", 0.5),
            ProcRole.Lancer => ("Drive hall", "engineering", 0.45),
            _ => ("Drive hall", "engineering", 0.3),
        };
        double aRoll = sp.U(), aLow = sp.U(), aTall = sp.U(), aLen = sp.Range(0.2, 0.32);
        if (aKind != "engineering" && Kit.Drive == ProcDrive.Heavy && dice.For("rooms.heavy").Chance(0.5))
        {
            traits.Add(("Trade-off", $"the heavy drive's hall took the {aName.ToLowerInvariant()}'s place"));
            (aName, aKind) = ("Drive hall", "engineering");
        }
        EngDeck = 0;
        if (Decks >= 2 && aRoll < aChance)
        {
            int d0 = 0, d1 = 1;
            if (aKind != "engineering")
            {
                d0 = Decks >= 3 && aLow < 0.4 ? 1 : 0; // (over the upper decks: engineering keeps the lowest deck's aft room)
                d1 = Math.Min(Decks - 1, d0 + (Decks - d0 >= 3 && aTall < 0.45 ? 2 : 1));
            }
            var engHall = aKind != "engineering" && d0 == 0 && d1 == Decks - 1;
            double back = double.MaxValue, narrow = double.MaxValue;
            for (var d = d0; d <= d1; d++)
            {
                back = Math.Min(back, Back[d]);
                foreach (var rr in Runs[d])
                    if (rr.W < sideMin && rr.Z0 >= Zl + Ll - 1e-6)
                        narrow = Math.Min(narrow, rr.Z0);
            }
            var zs = Math.Min(Q(back - Math.Max(Math.Max(5.0, aLen * Hull.Length), aKind == "engineering" ? EngNeed(0) : 0)), narrow);
            var least = Zl + Ll + (engHall ? 5.0 : 0);
            if (zs < least)
                zs = least;
            else if (!engHall && zs - (Zl + Ll) < MinRoomLen + Wall + 0.25)
                zs = Zl + Ll;
            if (back - zs >= 4.5 && zs <= narrow + 1e-6)
            {
                AftSpan = new ProcSpan(aName, aKind, d0, d1, zs, true);
                if (aKind == "engineering")
                    EngDeck = -1;
                else if (d0 == 0 && engHall)
                {
                    Halls[(0, 'a')] = ("Engineering", "engineering");
                    EngDeck = -1;
                }
                else if (d0 == 0)
                    EngDeck = d1 + 1;
            }
        }
        var mouth = Hull.Bow == ProcBow.Mouth;
        var (fName, fKind, fChance) = (Role, mouth) switch
        {
            (ProcRole.Carrier, true) => ("Bow hangar", "hangar", 1.0),
            (ProcRole.Tender, true) => ("Repair bay", "hangar", 1.0),
            (_, true) => ("Bow hold", "hold", 1.0),
            (ProcRole.Gunship, _) => ("Torpedo bay", "torpedo", 0.6),
            (ProcRole.Liner, _) => ("Atrium", "atrium", 0.7),
            _ => ("", "", 0.0),
        };
        double fRoll = sp.U(), fLen = sp.Range(0.12, 0.2);
        if (mouth || Decks >= 3 && fRoll < fChance)
        {
            // (a mouth's: the lowest deck, and the ones over it under the top two; the bridge stays on the top deck, over it)
            int d0 = 0, d1 = mouth ? Math.Max(0, Decks - 2) : Decks - 2;
            double front = 0, narrow = 0;
            for (var d = d0; d <= d1; d++)
            {
                front = Math.Max(front, Front[d]);
                foreach (var rr in Runs[d])
                    if (rr.W < sideMin && rr.Z1 <= Zl + 1e-6)
                        narrow = Math.Max(narrow, rr.Z1);
            }
            var ze = Math.Max(Q(front + Math.Max(mouth ? 7.0 : 5.0, (mouth ? fLen + 0.1 : fLen) * Hull.Length)), narrow);
            if (Zl - ze < MinRoomLen + Wall + 0.25)
                ze = Zl;
            if (ze - front >= 4.5)
                ForeSpan = new ProcSpan(fName, fKind, d0, d1, ze, false);
        }
    }

    /// <summary>
    /// How long engineering on deck d must be for the reactors (and, on a lancer's gun deck, the breech fore of them): a plate's side
    /// (two and a gap when twins can't stand side by side) and room to stand fore and aft of it.
    /// </summary>
    private double EngNeed(int d)
    {
        var rs = Kit.ReactorSize;
        var across = 2 * (WMinOr(d, Math.Max(Front[d], Back[d] - 8), Back[d] - Wall) - Wall) >= 2 * rs + 0.9;
        var plates = Kit.Reactors == 2 && !across ? 2 * rs + 0.5 : rs;
        return plates + 0.4 + 1.5 + Wall + 0.6 + (Hull.Spinal && d == 0 ? BreechLen + 0.6 : 0); // (0.6: the half-metre grid and a wall snapped to a step)
    }

    /// <summary>
    /// A mouth bow's opening: as wide as the room behind it at the block's front and the bow's face over its heights allow (a 2.5 m
    /// opening at the least), from the lowest floor to under the ceiling of the top deck it opens; the slabs between those decks
    /// open over the fore wall, where it is open too.
    /// </summary>
    private void MakeMouth()
    {
        var h = Hull;
        if (h.Bow != ProcBow.Mouth)
            return;
        int d0 = ForeSpan?.D0 ?? 0, d1 = ForeSpan?.D1 ?? 0;
        var z1 = Enumerable.Range(d0, d1 - d0 + 1).Max(d => Front[d]);
        if (Enumerable.Range(d0, d1 - d0 + 1).Any(d => Front[d] < z1 - 1e-6))
            return; // (a block whose decks start at different places: no straight tunnel)
        double y0 = FloorLo(d0), y1 = Ceil(d1) - 0.3;
        var room = Enumerable.Range(d0, d1 - d0 + 1).Min(d => W(d, z1 + 0.1)) - Wall - 0.1;
        var skin = double.MaxValue;
        for (var y = y0; y <= y1 + 0.15 + 1e-9; y += 0.25)
            foreach (var z in new[] { 0.01, z1 / 2, z1 })
                skin = Math.Min(skin, h.SkinHalf(z, Math.Min(y, y1 + 0.15)));
        var half = Math.Floor(Math.Min(room, skin - 0.4) * 20) / 20;
        if (half < 1.25)
            return;
        Mouth = new ProcMouth(half, y0, y1, z1, d0, d1);
        for (var j = d0 + 1; j <= d1; j++)
            Holes.Add(new ProcHole(j, -half, z1, half, z1 + Wall, false));
    }

    /// <summary>
    /// A span's holes in the slabs between its decks: between the run boundaries of the decks under and over each, as wide as both
    /// rooms are; the slab stays as a beam across under each step's wall on the deck over it.
    /// </summary>
    private void SpanHoles(ProcSpan sp)
    {
        for (var j = sp.D0 + 1; j <= sp.D1; j++)
        {
            var z0 = sp.Aft ? sp.Z : Math.Max(Front[j - 1], Front[j]) + Wall;
            var z1 = sp.Aft ? Math.Min(Back[j - 1], Back[j]) - Wall : sp.Z;
            var keep = Runs[j].Skip(1).Select(rr => AtBoundary(j, rr.Z0)).ToList();
            var stops = new SortedSet<double> { z0, z1 };
            foreach (var dd in new[] { j - 1, j })
                foreach (var rr in Runs[dd])
                    foreach (var z in new[] { rr.Z0, rr.Z1 })
                        if (z > z0 + 1e-6 && z < z1 - 1e-6)
                            stops.Add(z);
            foreach (var (lo, hi) in keep)
                foreach (var z in new[] { lo, hi })
                    if (z > z0 + 1e-6 && z < z1 - 1e-6)
                        stops.Add(z);
            var list = stops.ToList();
            for (var i = 0; i + 1 < list.Count; i++)
            {
                double a = list[i], b = list[i + 1], zm = (a + b) / 2;
                if (b - a < 0.05 || keep.Any(k => zm > k.Lo && zm < k.Hi))
                    continue;
                var w = Math.Min(W(j - 1, zm), W(j, zm)) - Wall;
                if (w > 0.3)
                    Holes.Add(new ProcHole(j, -w, a, w, b, false));
            }
        }
    }

    /// <summary>A span's room: from its boundary to the block's end on every deck it spans, as wide as the narrowest of them.</summary>
    private ProcRoom SpanRoom(ProcSpan sp, double edge)
    {
        var decks = Enumerable.Range(sp.D0, sp.D1 - sp.D0 + 1).ToList();
        double z0 = sp.Aft ? edge : decks.Max(dd => Front[dd]) + Wall, z1 = sp.Aft ? decks.Min(dd => Back[dd]) - Wall : edge;
        var w = decks.Min(dd => WMinOr(dd, z0, z1)) - Wall;
        return new ProcRoom(sp.D0, sp.Name, sp.Kind, -w, z0, w, z1, sp.D1);
    }

    private (string Name, string Kind) ForeName(int d) => (Role, d == 0 && Decks > 1) switch
    {
        (ProcRole.Gunship, true) => ("Forward magazine", "magazine"),
        (ProcRole.Gunship or ProcRole.Comms or ProcRole.Stealth, false) => ("Sensor room", "fore"),
        (ProcRole.Lancer, true) => ("Spinal gallery", "gun"),
        (ProcRole.Miner, true) => ("Drill store", "fore"),
        (ProcRole.Carrier, true) => ("Ordnance store", "fore"),
        (ProcRole.Liner, false) => ("Observation lounge", "fore"),
        (_, true) => ("Fore store", "fore"),
        _ => ("Fore compartment", "fore"),
    };

    /// <summary>
    /// A deck's fore room, by what the bow is for: the bridge on the top deck (under a glazed dome, an observation dome, the bridge
    /// a deck down: never glass on a bridge), the hangar or hold a mouth opens into, the cutter room behind a jaw, the sensor room
    /// behind a hammer.
    /// </summary>
    private (string Name, string Kind) ForeRoom(int d, bool top)
    {
        var bow = Hull.Bow;
        if (bow == ProcBow.Dome && Decks >= 2 && (top || d == Decks - 2))
            return top ? ("Observation dome", "lounge") : ("Bridge", "bridge");
        if (top)
            return ("Bridge", "bridge");
        if (d == 0 && bow == ProcBow.Mouth)
            return Role switch { ProcRole.Carrier => ("Bow hangar", "hangar"), ProcRole.Tender => ("Repair bay", "hangar"), _ => ("Bow hold", "hold") };
        if (d == 0 && bow == ProcBow.Jaw)
            return ("Cutter room", "works");
        if (d == 0 && bow == ProcBow.Hammer)
            return ("Sensor room", "fore");
        return ForeName(d);
    }

    private (string Name, string Kind) AftName() => Role switch
    {
        ProcRole.Gunship or ProcRole.Lancer => ("Aft magazine", "magazine"),
        ProcRole.Liner => ("Luggage hold", "hold"),
        ProcRole.Carrier => ("Craft store", "hold"),
        ProcRole.Stealth => ("Heat sink room", "works"),
        _ => ("Hold", "hold"),
    };

    /// <summary>A middle part as one hall, by role (and on a stepped hull's long low deck, most often): null for a corridor with side rooms.</summary>
    private (string Name, string Kind)? HallFor(int d, char part, double len, double u)
    {
        if (Halls.TryGetValue((d, part), out var set))
            return set;
        if (Hull.Style == ProcHull.Stepped && d == 0 && len >= 12 && part == (Hull.TowerAft ? 'f' : 'a'))
            return u < 0.7 ? Role switch
            {
                ProcRole.Freighter => ("Cargo deck", "hold"),
                ProcRole.Miner => ("Ore deck", "hold"),
                ProcRole.Gunship => ("Vehicle deck", "hangar"),
                ProcRole.Tender or ProcRole.Carrier => ("Hangar deck", "hangar"),
                ProcRole.Salvager => ("Salvage deck", "hold"),
                ProcRole.IceHauler => ("Ice deck", "hold"),
                _ => ("Promenade", "hall"),
            } : null;
        var top = d == Decks - 1;
        var (name, kind, chance) = (Role, d == 0 && Decks > 1, part) switch
        {
            (ProcRole.Freighter, true, 'a') => ("Cargo hall", "hold", 0.4),
            (ProcRole.Freighter, true, _) => ("Cargo hall", "hold", 0.25),
            (ProcRole.Miner, true, 'a') => ("Refinery", "works", 0.5),
            (ProcRole.Miner, true, _) => ("Drill bay", "drill", 0.35),
            (ProcRole.Tender, true, _) => ("Workshop", "works", 0.35),
            (ProcRole.Gunship, true, 'f') => ("Magazine", "magazine", 0.25),
            (ProcRole.Liner, _, 'f') when d < Decks - 1 => ("Lounge", "hall", 0.3),
            (ProcRole.Liner, _, 'a') when d < Decks - 1 => ("Mess hall", "hall", 0.35),
            (ProcRole.Carrier, true, 'a') => ("Hangar deck", "hangar", 0.45),
            (ProcRole.Carrier, _, 'f') when top => ("Flight control", "cic", 0.35),
            (ProcRole.Comms, _, 'f') when top => ("CIC", "cic", 0.6),
            (ProcRole.Comms, true, 'a') => ("Server hall", "works", 0.3),
            (ProcRole.Stealth, _, 'a') when d == 0 => ("Weapons bay", "magazine", 0.6),
            (ProcRole.Salvager, true, 'a') => ("Breaker's hall", "works", 0.45),
            (ProcRole.IceHauler, true, 'a') => ("Melt plant", "works", 0.5),
            _ => ("", "", 0.0),
        };
        return u < chance ? (name, kind) : null;
    }

    /// <summary>The side rooms' names by role, for the lowest deck (stores, works), the top deck (command, cabins) and the ones between (crew).</summary>
    private string[] Pool(int d)
    {
        var lower = d == 0 && Decks > 1;
        var top = d == Decks - 1;
        return Role switch
        {
            ProcRole.Gunship => lower ? ["Magazine", "Armoury", "Machine room", "Store", "Locker room", "Pump room"]
                : top ? ["Fire control", "Ops room", "Captain's cabin", "Comms room", "Crew quarters"]
                : ["Mess", "Galley", "Med bay", "Crew quarters", "Gym", "Armoury"],
            ProcRole.Freighter => lower ? ["Cargo store", "Store", "Pump room", "Machine room", "Locker room"]
                : top ? ["Cabin", "Captain's cabin", "Comms room", "Office", "Purser's office"]
                : ["Mess", "Galley", "Cabin", "Crew quarters", "Store"],
            ProcRole.Miner => lower ? ["Ore store", "Drill store", "Machine room", "Workshop", "Locker room", "Pump room"]
                : top ? ["Bunk room", "Captain's cabin", "Survey room", "Comms room"]
                : ["Mess", "Galley", "Bunk room", "Med bay", "Assay lab"],
            ProcRole.Tender => lower ? ["Workshop", "Spares store", "Machine room", "Fuel store", "Locker room"]
                : top ? ["Flight control", "Ops room", "Captain's cabin", "Comms room", "Crew quarters"]
                : ["Mess", "Galley", "Crew quarters", "Med bay", "Workshop"],
            ProcRole.Lancer => lower ? ["Capacitor room", "Magazine", "Machine room", "Coolant room", "Locker room"]
                : top ? ["Fire control", "Ops room", "Captain's cabin", "Comms room", "Crew quarters"]
                : ["Mess", "Galley", "Med bay", "Crew quarters", "Gym", "Armoury"],
            ProcRole.Carrier => lower ? ["Ordnance store", "Fuel store", "Spares store", "Machine room", "Locker room"]
                : top ? ["Ops room", "Briefing room", "Captain's cabin", "Comms room", "Crew quarters"]
                : ["Pilots' quarters", "Ready room", "Mess", "Galley", "Med bay", "Crew quarters"],
            ProcRole.Comms => lower ? ["Server room", "Signals store", "Machine room", "Cable store", "Locker room"]
                : top ? ["Signals room", "Crypto room", "Captain's cabin", "Comms room", "Analysts' room"]
                : ["Mess", "Galley", "Crew quarters", "Analysts' room", "Med bay"],
            ProcRole.Stealth => lower ? ["Magazine", "Machine room", "Heat sink room", "Store", "Locker room"]
                : top ? ["Fire control", "Captain's cabin", "Sensor room", "Crew quarters"]
                : ["Mess", "Crew quarters", "Med bay", "Store"],
            ProcRole.Salvager => lower ? ["Scrap store", "Cutting shop", "Machine room", "Parts store", "Locker room"]
                : top ? ["Salvage control", "Captain's cabin", "Survey room", "Comms room", "Bunk room"]
                : ["Mess", "Galley", "Bunk room", "Workshop", "Med bay"],
            ProcRole.IceHauler => lower ? ["Melt tank room", "Pump room", "Machine room", "Water store", "Locker room"]
                : top ? ["Cargo office", "Captain's cabin", "Survey room", "Comms room", "Bunk room"]
                : ["Mess", "Galley", "Bunk room", "Med bay", "Lab"],
            _ => lower ? ["Store", "Laundry", "Machine room", "Galley store", "Luggage room"]
                : top ? ["Suite", "Cabin", "Captain's cabin", "Purser's office", "Lounge"]
                : ["Cabin", "Suite", "Cabin", "Bar", "Galley", "Med bay"],
        };
    }

    private void Layout(int d, ProcDice dice, int[] lockSides, bool belly, bool dorsal)
    {
        var h = Hull;
        var r = dice.For($"rooms.deck{d}");
        double F = Front[d], B = Back[d];
        var sideMin = Ch + Wall + SideMinRoom + Wall;
        var top = d == Decks - 1;
        var fs = ForeSpan is { } fsp && d >= fsp.D0 && d <= fsp.D1 ? fsp : null;
        var asp = AftSpan is { } asq && d >= asq.D0 && d <= asq.D1 ? asq : null;
        bool upperFore = fs != null && d > fs.D0, upperAft = asp != null && d > asp.D0; // (a span's upper levels: no doorway into it)
        if (_foreEnd.Length != Decks)
            (_foreEnd, _aftStart) = (new double[Decks], new double[Decks]);
        // The fore room: the narrow runs at the front, at least 3.5 to 5 m long; it reaches the lobby if what is left is too short
        // for side rooms. On a fore span's decks it ends at the span's boundary.
        var fEnd = Q(F + r.Range(3.5, 5.0));
        foreach (var rr in Runs[d])
            if (rr.W < sideMin && rr.Z1 <= Zl + 1e-6)
                fEnd = Math.Max(fEnd, rr.Z1);
        if (Zl - fEnd < MinRoomLen + Wall + 0.25 || d == 0 && h.Spinal)
            fEnd = Zl; // (the gun deck's spinal gallery runs from the bow to the lobby)
        if (fs != null)
            fEnd = fs.Z;
        // The aft room: engineering on the lowest deck (5.5 to 7.5 m, the reactor's), a hold above (4.5 to 7 m); it takes the narrow
        // runs at the back. On an aft span's decks it starts at the span's boundary.
        var aStart = Q(B - Math.Max(d == 0 ? r.Range(5.5, 7.5) : r.Range(4.5, 7.0), d == EngDeck && asp == null ? EngNeed(d) : 0));
        foreach (var rr in Runs[d])
            if (rr.W < sideMin && rr.Z0 >= Zl + Ll - 1e-6)
                aStart = Math.Min(aStart, rr.Z0);
        if (aStart - (Zl + Ll) < MinRoomLen + Wall + 0.25 || aStart > B - 3.0)
            aStart = Zl + Ll;
        if (asp != null)
            aStart = asp.Z;
        // The transverse walls, full width, fore to aft (each with a doorway on the centreline but the ends and a span's upper levels).
        var tw = new List<(double Lo, double Hi, bool Door, string Group)> { (F, F + Wall, false, $"d{d}.fore") };
        if (fEnd < Zl - 1e-6)
        {
            var (lo, hi) = fs != null ? (fEnd, fEnd + Wall) : Span(d, fEnd, true);
            tw.Add((lo, hi, !upperFore, $"d{d}.fore"));
        }
        tw.Add((Zl, Zl + Wall, !(upperFore && fEnd >= Zl - 1e-6), $"d{d}.lobby"));
        tw.Add((Zl + Ll - Wall, Zl + Ll, !(upperAft && aStart <= Zl + Ll + 1e-6), $"d{d}.lobby"));
        if (aStart > Zl + Ll + 1e-6)
        {
            var (lo, hi) = asp != null ? (aStart - Wall, aStart) : Span(d, aStart, false);
            tw.Add((lo, hi, !upperAft, $"d{d}.aft"));
        }
        tw.Add((B - Wall, B, false, $"d{d}.aft"));
        _foreEnd[d] = tw[1].Lo;
        _aftStart[d] = tw[^2].Hi;
        var spinal = d == 0 && h.Spinal;
        foreach (var (lo, hi, door0, group) in tw)
        {
            var ext = WMax(d, lo, hi);
            var w = new ProcWall(d, true, -ext, ext, (lo + hi) / 2, group);
            var door = door0;
            if (spinal && hi < B - 1e-6)
            {
                // The gun deck: the block's fore wall has the barrel's arch; the walls between its rooms are wide arches, open from
                // the bow to engineering round the barrel (no doors: a door beside the barrel needs a wider deck than most have).
                if (lo == F)
                    w.Gaps.Add(new ProcGap(-ArchHalf, 2 * ArchHalf, ArchHead));
                else
                    w.Gaps.Add(new ProcGap(Math.Round((-ext + 0.35) * 20) / 20, Math.Round((2 * ext - 0.7) * 20) / 20, GalleryHead));
                door = false;
            }
            if (Mouth is { } mo && lo == F && d >= mo.D0 && d <= mo.D1)
                w.Gaps.Add(new ProcGap(-mo.HalfW, 2 * mo.HalfW, Math.Min(mo.Y1, Ceil(d)) - FloorTop(d))); // (the mouth's opening, the wall's full height but under the top deck's lintel)
            if (door)
            {
                // On the centreline, or moved to starboard where the wall is narrow: the leaf slides 1.48 m to port into the wall.
                var a = Math.Max(-DoorW / 2, Math.Ceiling((-ext + DoorSlide) * 20) / 20);
                if (a + DoorW > ext - 0.05)
                    throw new ProcShipException($"deck {d + 1}: the wall at {lo:0.##} m is {2 * ext:0.##} m wide, too narrow for a door and the wall it slides into");
                w.Gaps.Add(new ProcGap(a, DoorW));
                Doors.Add(new ProcDoor(d, group, true, a, (lo + hi) / 2, FloorTop(d), "door"));
            }
            Walls.Add(w);
        }
        var foreHi = tw[1].Lo;
        if (fs == null)
        {
            var fw = WMinOr(d, F + Wall, foreHi) - Wall;
            var (fn, fk) = ForeRoom(d, top);
            Rooms.Add(new ProcRoom(d, fn, fk, -fw, F + Wall, fw, foreHi));
        }
        else if (d == fs.D0)
            Rooms.Add(SpanRoom(fs, foreHi));
        var aftLo = tw[^2].Hi;
        if (asp == null)
        {
            var aw = WMinOr(d, aftLo, B - Wall) - Wall;
            var (an, ak) = d == EngDeck ? ("Engineering", "engineering") : AftName();
            Rooms.Add(new ProcRoom(d, an, ak, -aw, aftLo, aw, B - Wall));
        }
        else if (d == asp.D0)
            Rooms.Add(SpanRoom(asp, aftLo));
        var lw = W(d, Zl + Ll / 2) - Wall;
        Rooms.Add(new ProcRoom(d, d == LockDeck ? "Airlock lobby" : "Lobby", "lobby", -lw, Zl + Wall, lw, Zl + Ll - Wall));

        // The middle parts: one hall, or a corridor down the centreline with side rooms either side, fore and aft of the lobby.
        var cuts = new Dictionary<int, List<(double Lo, double Hi)>> { [-1] = [], [1] = [] }; // (per side: what the outer walls stop at)
        foreach (var (lo, hi, _, _) in tw)
        {
            cuts[-1].Add((lo, hi));
            cuts[1].Add((lo, hi));
        }
        var names = dice.For($"rooms.names{d}");
        var pool = Pool(d);
        var named = names.Int(0, pool.Length - 1);
        var hd = dice.For($"rooms.halls{d}");
        foreach (var (p0, p1, grp) in new[] { (tw[1].Hi, Zl, "f"), (Zl + Ll, tw[^2].Lo, "a") })
        {
            var hu = hd.U();
            if (p1 - p0 < MinRoomLen - 1e-6)
                continue;
            if (HallFor(d, grp[0], p1 - p0, hu) is { } hall)
            {
                var hw = WMinOr(d, p0, p1) - Wall;
                var room = new ProcRoom(d, hall.Name, hall.Kind, -hw, p0, hw, p1);
                Rooms.Add(room);
                HallRooms.Add(room);
                continue;
            }
            var n = Math.Max(1, (int)Math.Round((p1 - p0) / r.Range(3.0, 5.0)));
            while (n > 1 && (p1 - p0) / n < MinRoomLen)
                n--;
            var centres = Enumerable.Range(1, n - 1).Select(k => Q(p0 + (p1 - p0) * k / n)).ToList();
            foreach (var s in new[] { -1, 1 })
            {
                var group = $"d{d}.{grp}{(s < 0 ? "p" : "s")}";
                var corridor = new ProcWall(d, false, p0, p1, s * (Ch + Half), group);
                Walls.Add(corridor);
                var faces = new List<(double Lo, double Hi)> { (p0 - Wall, p0) };
                foreach (var zc in centres)
                {
                    var (lo, hi) = Span(d, zc - Half, true);
                    if (lo < faces[^1].Hi + MinRoomLen - 1e-6 || hi > p1 - MinRoomLen + 1e-6)
                        continue; // (every side room at least 3 m long: room for its door and the wall the door slides into)
                    var ext = WMax(d, lo, hi);
                    Walls.Add(new ProcWall(d, true, s < 0 ? -ext : Ch + Wall, s < 0 ? -(Ch + Wall) : ext, (lo + hi) / 2, group));
                    cuts[s].Add((lo, hi));
                    faces.Add((lo, hi));
                }
                faces.Add((p1, p1 + Wall));
                for (var k = 0; k + 1 < faces.Count; k++)
                {
                    double a0 = faces[k].Hi, a1 = faces[k + 1].Lo;
                    var wmin = WMinOr(d, a0, a1);
                    Rooms.Add(new ProcRoom(d, pool[named++ % pool.Length], "side", s < 0 ? -(wmin - Wall) : Ch + Wall, a0, s < 0 ? -(Ch + Wall) : wmin - Wall, a1));
                    if (a1 - a0 < DoorW + DoorSlide)
                        continue;
                    var a = Math.Round(Math.Max(a0 + DoorSlide, a1 - 0.2 - DoorW) * 100) / 100;
                    corridor.Gaps.Add(new ProcGap(a, DoorW));
                    Doors.Add(new ProcDoor(d, group, false, a, s * (Ch + Half), FloorTop(d), "door"));
                }
            }
            Rooms.Add(new ProcRoom(d, "Corridor", "corridor", -Ch, p0, Ch, p1));
        }
        // Stubs at run boundaries no wall closes, then the outer walls between the cuts.
        foreach (var s in new[] { -1, 1 })
        {
            foreach (var rr in Runs[d].Skip(1))
            {
                var (lo, hi) = AtBoundary(d, rr.Z0);
                if (cuts[s].Any(c => c.Lo < hi - 1e-6 && c.Hi > lo + 1e-6))
                    continue;
                double wf = W(d, rr.Z0 - 0.01), wa = W(d, rr.Z0 + 0.01);
                double wn = Math.Min(wf, wa), ww = Math.Max(wf, wa);
                Walls.Add(new ProcWall(d, true, s < 0 ? -ww : wn - Wall, s < 0 ? -(wn - Wall) : ww, (lo + hi) / 2, GroupAt(d, s, (lo + hi) / 2)));
                cuts[s].Add((lo, hi));
            }
            var cs = cuts[s].OrderBy(c => c.Lo).ToList();
            for (var k = 0; k + 1 < cs.Count; k++)
            {
                double a0 = cs[k].Hi, a1 = cs[k + 1].Lo;
                if (a1 - a0 < 0.02)
                    continue;
                var w = W(d, (a0 + a1) / 2);
                var wall = new ProcWall(d, false, a0, a1, s * (w - Half), GroupAt(d, s, (a0 + a1) / 2));
                // The side airlock's inner door, in the lobby's outer wall on the lock deck; its housing out through the void and the skin.
                if (d == LockDeck && lockSides.Contains(s) && a0 < Zl + Ll / 2 && a1 > Zl + Ll / 2)
                {
                    var a = Math.Round((Zl + Ll / 2 - DoorW / 2) * 100) / 100;
                    wall.Gaps.Add(new ProcGap(a, DoorW));
                    Doors.Add(new ProcDoor(d, wall.Group, false, a, s * (w - Half), FloorTop(d), "lock inner"));
                    double y0 = FloorLo(d), y1 = FloorTop(d) + 2.6, z0 = a - 1.6, z1 = a + DoorW + 0.2;
                    var most = 0.0;
                    foreach (var zz in new[] { z0, z1 })
                        for (var k2 = 0; k2 <= 8; k2++)
                            most = Math.Max(most, h.SkinHalf(zz, y0 + (y1 - y0) * k2 / 8));
                    Locks.Add(new ProcLock(s, d, a, w, Math.Ceiling((most + (Role == ProcRole.Stealth ? 0.03 : 0.14)) * 100) / 100, z0, z1, y0, y1)); // (a stealth ship's housing all but flush)
                }
                Walls.Add(wall);
            }
        }
        // The lobby's ladder up to the next deck's lobby: through a hole by its fore wall beside the doorway (the sides taking turns),
        // standing at the hole's aft edge, so it is climbed from the fore and lands aft on the deck over it (the Zero Day's).
        if (!top)
        {
            var side = d % 2 == 0 ? 1 : -1;
            var wl = Math.Min(W(d, Zl + Ll / 2), W(d + 1, Zl + Ll / 2));
            var room = wl - Wall - 0.695; // (the hole's rail on the deck over it clear of the lobby's outer wall)
            if (room < 1.52) // (and clear of the doorway's jamb)
                throw new ProcShipException($"the lobby on decks {d + 1} and {d + 2} is {2 * wl:0.##} m wide: too narrow for its ladder beside the doorway");
            var xl = side * Math.Min(1.75, Math.Floor(room * 20) / 20);
            double hz0 = Zl + Wall, hz1 = hz0 + 1.25;
            Ladders.Add(new ProcLadder(d, xl, hz1 - 0.095, false, ProcHullShape.Storey, ProcHullShape.Storey + 1.0, $"the lobby, deck {d + 1} to {d + 2}"));
            Holes.Add(new ProcHole(d + 1, xl - 0.625, hz0, xl + 0.625, hz1, true));
        }
        else if (h.Well(Zl + Ll / 2) > 0)
        {
            // A barge's top lobby: the same ladder up through the roof into the open well (the way to the containers from inside).
            var side = d % 2 == 0 ? 1 : -1;
            var room = Math.Min(W(d, Zl + Ll / 2), h.WellHalf) - Wall - 0.695;
            double hz0 = Zl + Wall, hz1 = hz0 + 1.25;
            if (room >= 1.52 && hz0 > h.WellZ0 + 0.3 && hz1 < h.WellZ1 - 1.5)
            {
                var xl = side * Math.Min(1.75, Math.Floor(room * 20) / 20);
                Ladders.Add(new ProcLadder(d, xl, hz1 - 0.095, false, ProcHullShape.Storey, ProcHullShape.Storey + 1.0, $"the lobby, deck {d + 1} up into the well"));
                Holes.Add(new ProcHole(Decks, xl - 0.625, hz0, xl + 0.625, hz1, true));
            }
        }
        var zc0 = Zl + Ll / 2;
        if (belly)
        {
            // Down through the keel under the lobby's middle: a 1.3 m opening (the Docking Hatch's), its sill on the keel's flat.
            var z0 = Math.Round((zc0 - 0.65) * 20) / 20;
            Hatches.Add(new ProcHatch("belly", d, -0.65, z0, 0.65, z0 + 1.3, h.KeelY + 0.13, h.KeelY, 0));
            Holes.Add(new ProcHole(0, -0.65, z0, 0.65, z0 + 1.3, true));
        }
        if (dorsal)
        {
            // Up through the roof and the spine over the lobby's middle; a wall across its fore edge carries the ladder up to it.
            var zd = Math.Round((zc0 - 0.6) * 20) / 20;
            var sill = h.TopY - 0.09;
            Hatches.Add(new ProcHatch("dorsal", d, -0.65, zd + 0.22, 0.65, zd + 1.52, sill, h.Roof, h.TopY));
            Holes.Add(new ProcHole(Decks, -0.65, zd, 0.65, zd + 1.55, false));
            Pillars.Add((d, -0.75, zd - Wall, 0.75, zd));
            // Its ladder against the pillar, turned: climbed from the aft, it lands fore of the opening on the spine; its rails stop
            // under the sill (the lid rests over them and slides aft).
            var rise = Math.Round((h.TopY - FloorTop(d)) * 100) / 100;
            Ladders.Add(new ProcLadder(d, 0, zd + 0.09, true, rise, Math.Floor((sill - 0.11 - FloorTop(d)) * 100) / 100, "the dorsal hatch"));
        }
    }

    private double WMinOr(int d, double za, double zb)
    {
        var w = WMin(d, za, zb);
        return w > 0 ? w : W(d, (za + zb) / 2);
    }

    /// <summary>Which wall piece a wall on side s at z belongs to: the zone it stands in (the fore room, a side of a middle part, the lobby, the aft room).</summary>
    private string GroupAt(int d, int s, double z)
    {
        if (z <= _foreEnd[d] + 1e-6)
            return $"d{d}.fore";
        if (z >= _aftStart[d] - 1e-6)
            return $"d{d}.aft";
        if (z >= Zl - 1e-6 && z <= Zl + Ll + 1e-6)
            return $"d{d}.lobby";
        return $"d{d}.{(z < Zl ? "f" : "a")}{(s < 0 ? "p" : "s")}";
    }

    // ---- the module sockets

    /// <summary>
    /// The sockets (module-sockets.md), by role and fit: engine plates across the transom (keyed; a heavy drive's a size up, a light
    /// drive's fewer; fewer with nacelles, which carry one on each tail), the reactors in engineering (as big as the fit's, twins side
    /// by side or one fore of the other), then the payload, capped by the points the fit left: turrets along the spine, cargo plates
    /// in the holds, craft pads in the hangars, containers or ice chunks in a well; and each role's own: a comms ship's dish, masts and
    /// sensors, a salvager's clamps or barge berth and arms, a stealth ship's keel launch bays (no turrets), a lancer's capacitors, a
    /// carrier's launch bays, a miner's drill, a jaw's cutters.
    /// </summary>
    private void Fit(ProcShipSpec spec, ProcDice dice, List<(string Key, string Value)> traits)
    {
        var h = Hull;
        var cls = spec.Class;
        var e = dice.For("sockets.engines");
        var nac = h.Nacelles.Count > 0;
        // The reactors first (the fit as fitted sets every count after them): on engineering's floor (wherever engineering is), in
        // its aft part, room to stand round them; twins side by side where engineering is wide enough, else one fore of the other; a
        // size down (or one) until they fit, which is noted.
        if (Rooms.FirstOrDefault(q => q.Kind == "engineering") is { } eng)
        {
            double ew = eng.X1 - eng.X0, el = eng.Z1 - eng.Z0;
            var aftM = HallRooms.Contains(eng) ? 1.2 : 0.4;
            var foreM = 1.5 + (h.Spinal && eng.Deck == 0 ? BreechLen + 0.6 : 0); // (a lancer's breech stands in its fore part)
            var placed = "";
            foreach (var rs in ProcFit.ReactorSizes.Select(r => r.Side).Where(v => v <= Kit.ReactorSize).Reverse())
            {
                var n = Kit.Reactors;
                var across = n == 2 && ew >= 2 * rs + 0.5 + 0.4;
                if (n == 2 && !across && el < 2 * rs + 0.5 + aftM + foreM)
                    n = 1;
                if (ew < rs + 0.4 || el < (n == 2 && !across ? 2 * rs + 0.5 : rs) + aftM + foreM)
                    continue;
                var za = Q(eng.Z1 - aftM - rs, 0.05);
                var above = eng.Top > eng.Deck ? 4.5 : 2.5;
                for (var k = 0; k < n; k++)
                {
                    var x0 = n == 2 && across ? (k == 0 ? -rs - 0.25 : 0.25) : -rs / 2.0;
                    var zk = n == 2 && !across && k == 1 ? Q(za - rs - 0.5, 0.05) : za;
                    Add(n == 2 ? $"reactor{k}" : "reactor", "floor", 2, rs, rs, 0, above, false, 0, [x0, FloorTop(eng.Deck), zk], null, null, eng.Deck, "reactor");
                }
                placed = $"{n} x {rs} m in the {eng.Name.ToLowerInvariant()}{(n == 2 ? across ? ", side by side" : ", one fore of the other" : "")}"
                    + (rs < Kit.ReactorSize || n < Kit.Reactors ? $" (cut from the fit's {Kit.Reactors} x {Kit.ReactorSize} m to fit a room {ew:0.#} x {el:0.#} m: its points go to the payload)" : "");
                Kit = Kit with { ReactorSize = rs, Reactors = n }; // (what it saved is payload: the counts below are taken from the fit as fitted)
                break;
            }
            traits.Add(("Reactors", placed == "" ? $"none (the {eng.Name.ToLowerInvariant()} is too small)" : placed));
        }

        // Engines: keyed plates across the transom facing aft, a row (two rows on a destroyer of four or more), the first layout that fits.
        (int Size, int N)[] options = cls switch
        {
            ProcClass.Corvette => nac ? [(2, 1)] : [(2, e.Pick(2, 2, 3)), (2, 2), (2, 1)],
            ProcClass.Frigate => nac ? [(2, e.Pick(1, 2)), (2, 1)] : e.Chance(0.6) ? [(2, e.Pick(3, 4, 4)), (2, 2), (2, 1)] : [(3, 2), (2, 3), (2, 2), (2, 1)],
            _ => nac ? [(2, 2), (2, 1)] : e.Chance(0.7) ? [(3, e.Pick(3, 4)), (3, 2), (2, 4), (2, 2)] : [(2, e.Pick(4, 6)), (2, 4), (2, 2)],
        };
        if (Kit.Drive == ProcDrive.Heavy)
            options = [.. options.Select(o => (Math.Min(4, o.Size + 1), o.N)), .. options]; // (a heavy drive: each plate a size up where the transom takes it)
        else if (Kit.Drive == ProcDrive.Light)
            options = [.. options.Select(o => (o.Size, Math.Max(1, o.N - 1)))];
        var rot = PartKit.Generators.BuildingParts.Turn("+x", "+z");
        var ring = h.Stations[^1].P;
        var ei = 0;
        foreach (var (size, n) in options)
        {
            var rows = n >= 4 && cls == ProcClass.Destroyer ? 2 : 1;
            var perRow = (int)Math.Ceiling(n / (double)rows);
            var span = perRow * size + (perRow - 1) * 0.5;
            var midY = Math.Round((ring[0].Y + ring[4].Y) / 2 * 4) / 4;
            double[] tops = rows == 1 ? [midY + size / 2.0] : [midY + size + 0.25, midY - 0.25];
            var plates = new List<(double X0, double Top)>();
            for (var row = 0; row < rows; row++)
                for (var k = 0; k < perRow && plates.Count < n; k++)
                    plates.Add((Math.Round((-span / 2 + k * (size + 0.5)) * 20) / 20, tops[row]));
            if (plates.Any(q => new[] { (q.X0, q.Top), (q.X0 + size, q.Top), (q.X0, q.Top - size), (q.X0 + size, q.Top - size) }.Any(c => Math.Abs(c.Item1) + 0.12 > h.SkinHalf(h.Length, c.Item2))))
                continue;
            foreach (var (x0, topY) in plates)
            {
                double[] padMin = [x0 - 0.05, topY - size - 0.05, h.Length - 0.45], padMax = [x0 + size + 0.05, topY + 0.05, h.Length + 0.04];
                Add($"engine{ei++}", "transom", 1, size, size, 0, 0, true, rot, [x0, topY - size, padMax[2]], padMin, padMax, -1, "engine");
            }
            traits.Add(("Engines", $"{n} x {size} m on the transom{(nac ? $", {h.Nacelles[0].Engine} m on each nacelle" : "")}"));
            break;
        }
        // The nacelles' main engines: a keyed plate on each tail, on a pad standing out of it.
        foreach (var q in h.Nacelles)
        {
            var es = q.Engine;
            double x0 = q.Side * q.X - es / 2.0, yb = q.Y - es / 2.0;
            double[] padMin = [x0 - 0.05, yb - 0.05, q.Z1 - 0.45], padMax = [x0 + es + 0.05, yb + es + 0.05, q.Z1 + 0.04];
            Add($"engine{ei++}", "nacelle", 1, es, es, 0, 0, true, rot, [x0, yb, padMax[2]], padMin, padMax, -1, "engine");
        }

        // The spine: what is kept clear on it (the dorsal hatch and its lid's slot, the well), then a comms ship's dish and a
        // salvager's barge berth amidships, a comms ship's masts, then the turrets between.
        var flat = h.Mid[4].H;
        var keep = Hatches.Where(x => x.Kind == "dorsal").Select(x => (x.Z0 - 0.6, x.Z1 + 1.8)).ToList();
        if (h.HasWell)
            keep.Add((h.WellZ0 - 0.6, h.WellZ1 + 0.6));
        double sz0 = h.MidZ0 + 0.5, sz1 = h.MidZ1 - 0.5;
        bool Free(double za, double len) => za >= sz0 - 1e-6 && za + len <= sz1 + 1e-6 && !keep.Any(kp => za < kp.Item2 && za + len > kp.Item1);
        double? Spot(double len, double at)
        {
            // The nearest free stretch of the spine to "at" (a share of the midbody), on a quarter-metre grid.
            var want = Q(sz0 + (sz1 - sz0 - len) * at, 0.25);
            for (var k = 0; k <= 80; k++)
                foreach (var za in new[] { want + k * 0.25, want - k * 0.25 })
                    if (Free(za, len))
                        return za;
            return null;
        }
        void SpinePlate(string name, int w, int l, double za, string use, int kind)
        {
            var x0 = -w / 2.0;
            var padMin = new[] { x0 - 0.05, h.TopY - 0.3, za - 0.05 };
            var padMax = new[] { x0 + w + 0.05, h.TopY + 0.04, za + l + 0.05 };
            Add(name, "spine", kind, w, l, 0, 0, false, 0, [x0, padMax[1], za], padMin, padMax, -1, use);
            keep.Add((za - 1.0, za + l + 1.0));
        }
        if (Role == ProcRole.Comms)
        {
            var ds = new[] { cls == ProcClass.Destroyer ? 4 : 3, 3, 2 }.First(v => v / 2.0 + 0.12 <= flat || v == 2);
            if (flat >= ds / 2.0 + 0.12 && Spot(ds, 0.5) is { } dz)
                SpinePlate("dish", ds, ds, dz, "dish", 0);
            // Masts: a post up from the spine with a 1 x 1 plate on its head, fore and aft of the dish.
            var mn = dice.For("sockets.masts").Int(1, cls == ProcClass.Corvette ? 2 : 3);
            for (var k = 0; k < mn; k++)
                if (Spot(1, k % 2 == 0 ? 0.15 + 0.1 * k : 0.85 - 0.1 * k) is { } mz)
                {
                    var tall = Q(1.6 + 0.6 * ((k + spec.Seed) % 3), 0.1);
                    var padMin = new[] { -0.55, h.TopY + tall, mz - 0.05 };
                    var padMax = new[] { 0.55, h.TopY + tall + 0.1, mz + 1.05 };
                    Add($"mast{k}", "mast", 0, 1, 1, 0, 0, false, 0, [-0.5, padMax[1], mz], padMin, padMax, -1, "antenna");
                    keep.Add((mz - 0.8, mz + 1.8));
                }
        }
        if (Role == ProcRole.Salvager && h.Bow != ProcBow.Fork)
        {
            // No fork to hold a wreck: a berth on the spine for a small salvage barge (and a hold for the materials, aft).
            foreach (var (bw, bl) in cls == ProcClass.Corvette ? new[] { (3, 3), (2, 3) } : [(4, 6), (3, 3)])
                if (flat >= bw / 2.0 + 0.12 && Spot(bl, 0.7) is { } bz)
                {
                    SpinePlate("berth", bw, bl, bz, "barge berth", 0);
                    break;
                }
        }
        // Turrets: along the spine's flat, as many as the class has for its role, scaled by the payload (none on a stealth ship).
        var t = dice.For("sockets.turrets");
        var tn = cls switch { ProcClass.Corvette => t.Pick(1, 2, 2), ProcClass.Frigate => t.Pick(2, 3, 3), _ => t.Pick(3, 4, 4) };
        tn = Math.Max(1, tn + (Role == ProcRole.Gunship ? 1 : -1));
        tn = Role switch
        {
            ProcRole.Stealth => 0,
            ProcRole.Comms => Math.Min(1, tn),
            _ => Kit.Cap(cls, tn, ProcShipSpec.Warship(Role) ? 1 : 0),
        };
        var ts = cls == ProcClass.Destroyer && flat >= 1.75 || Role == ProcRole.Gunship && cls == ProcClass.Frigate && flat >= 1.75 ? 3 : 2;
        if (tn > 0 && flat >= ts / 2.0 + 0.12)
        {
            var spots = new List<double>();
            var step = (sz1 - sz0 - ts) / Math.Max(1, tn - 1);
            for (var k = 0; k < tn; k++)
            {
                var za = Q(tn == 1 ? (sz0 + sz1 - ts) / 2 : sz0 + k * step, 0.25);
                foreach (var (a, b) in keep)
                    if (za < b && za + ts > a)
                        za = za + ts / 2.0 < (a + b) / 2 ? Q(a - ts - 0.25, 0.25) : Q(b + 0.25, 0.25);
                if (!Free(za, ts) || spots.Any(o => Math.Abs(o - za) < ts + 1.0))
                    continue;
                spots.Add(za);
            }
            var i = 0;
            foreach (var za in spots.OrderBy(v => v))
            {
                var x0 = -ts / 2.0;
                var padMin = new[] { x0 - 0.05, h.TopY - 0.3, za - 0.05 };
                var padMax = new[] { x0 + ts + 0.05, h.TopY + 0.04, za + ts + 0.05 };
                Add($"turret{i++}", "spine", 3, ts, ts, 0, 0, false, 0, [x0, padMax[1], za], padMin, padMax, -1, "turret");
            }
        }

        // Cargo: a grid of plates in a freighter's holds and in a tall hold, one plate in any other hold; craft pads in the hangars.
        var ci = 0;
        foreach (var hold in Rooms.Where(q => q.Kind == "hold").ToList())
        {
            var above = Math.Round(Ceil(hold.Top) - FloorTop(hold.Deck) - 0.5, 2);
            var most = Role == ProcRole.Freighter ? 12 : hold.Top > hold.Deck ? 6 : 1;
            CargoGrid(hold, ref ci, above, Kit.Cap(cls, most, 1), HallRooms.Contains(hold), "cargo");
        }
        var pi = 0;
        foreach (var bay in Rooms.Where(q => q.Kind == "hangar").ToList())
        {
            var above = Math.Round(Ceil(bay.Top) - FloorTop(bay.Deck) - 0.5, 2);
            var most = Role == ProcRole.Carrier ? cls switch { ProcClass.Corvette => 1, ProcClass.Frigate => 2, _ => 4 } : 1;
            CargoGrid(bay, ref pi, above, Kit.Cap(cls, most, 1), HallRooms.Contains(bay) || bay.Z0 < Front[bay.Deck] + Wall + 1e-6, "craft", "pad");
        }
        // A well: plates on its floor (the roof) in rows, clear of the ladder up into it and where it lands people: a barge's
        // containers, an ice hauler's chunks (4 m plates where the shell is wide enough).
        if (h.HasWell)
        {
            var ice = Role == ProcRole.IceHauler;
            var cs = ice && 2 * h.WellHalf >= 2 * 4 + 0.5 + 0.6 ? 4 : 3;
            var keepOut = Holes.Where(q => q.Slab == Decks).Select(q => (X0: q.X0 - 0.3, Z0: q.Z0 - 0.3, X1: q.X1 + 0.3, Z1: q.Z1 + 1.5)).ToList();
            var nx = (int)Math.Floor((2 * h.WellHalf - 0.6 + 0.5) / (cs + 0.5));
            var total = nx * cs + (nx - 1) * 0.5;
            var cap = Kit.Cap(cls, 12, 2);
            var n = 0;
            for (var za = h.WellZ0 + 0.4; za + cs <= h.WellZ1 - 0.4 + 1e-6 && n < cap; za += cs + 0.5)
                for (var i = 0; i < nx && n < cap; i++)
                {
                    var xa = Math.Round((-total / 2 + i * (cs + 0.5)) * 20) / 20;
                    if (keepOut.Any(k => xa < k.X1 && xa + cs > k.X0 && za < k.Z1 && za + cs > k.Z0))
                        continue;
                    Add($"{(ice ? "chunk" : "container")}{n++}", "well", 0, cs, cs, 0, ice ? 3.0 : 2.4, false, 0, [xa, h.Roof, Q(za, 0.05)], null, null, Decks, ice ? "ice chunk" : "container");
                }
        }
        // Under the keel: a miner's drill, or a stealth ship's launch bays (flush), facing down, clear of the belly hatch.
        var keelN = Role switch
        {
            ProcRole.Miner => 1,
            ProcRole.Stealth => Kit.Cap(cls, cls switch { ProcClass.Corvette => 1, ProcClass.Frigate => 2, _ => 3 }, 1),
            _ => 0,
        };
        if (keelN > 0)
        {
            var ds = Role == ProcRole.Miner && cls == ProcClass.Destroyer && h.Mid[0].H >= 1.75 ? 3 : 2;
            if (h.Mid[0].H >= ds / 2.0 + 0.15)
            {
                var spots = new List<double> { Q(h.MidZ0 + 0.5, 0.25) };
                spots.AddRange(Hatches.Where(q => q.Kind == "belly").Select(q => Q(q.Z1 + 2.0, 0.25)));
                for (var k = 1; k < 8; k++)
                    spots.Add(Q(spots[^1] + ds + 0.75, 0.25));
                var placed = new List<double>();
                foreach (var z0 in spots)
                {
                    if (placed.Count >= keelN)
                        break;
                    if (z0 + ds > h.MidZ1 - 0.5 || Hatches.Any(q => q.Kind == "belly" && q.Z0 - 0.4 < z0 + ds + 0.1 && q.Z1 + 1.9 > z0 - 0.1) || placed.Any(o => Math.Abs(o - z0) < ds + 0.5))
                        continue;
                    var x0 = -ds / 2.0;
                    var padMin = new[] { x0 - 0.05, h.KeelY - 0.04, z0 - 0.05 };
                    var padMax = new[] { x0 + ds + 0.05, h.KeelY + 0.45, z0 + ds + 0.05 };
                    var miner = Role == ProcRole.Miner;
                    Add(miner ? "drill" : $"bay{placed.Count}", "keel", miner ? 0 : 3, ds, ds, miner ? 0 : 1, 0, false, PartKit.Generators.BuildingParts.Turn("+x", "-y"), [x0, padMin[1], z0], padMin, padMax, -1, miner ? "drill" : "launch bay");
                    placed.Add(z0);
                }
            }
        }
        // The bow's own: cutters across a jaw's lip, sensors on a hammer's tips, clamps on a fork's prongs (a salvage or repair berth).
        if (h.Bow == ProcBow.Jaw)
        {
            var st = h.Stations[0];
            double y0 = Q(st.P[0].Y + 0.15, 0.05), lipTop = st.P[4].Y - 0.1;
            var cl = (int)Math.Floor(Math.Min(2, lipTop - y0) + 1e-9);
            if (cl >= 1)
            {
                var room = double.MaxValue;
                for (var y = y0; y <= y0 + cl + 1e-9; y += 0.25)
                    room = Math.Min(room, h.SkinHalf(0.01, y));
                var n = Math.Min((int)Math.Floor((2 * room - 0.4 + 0.5) / 2.5), Kit.Cap(cls, cls == ProcClass.Corvette ? 2 : 3, 1));
                var total = n * 2 + (n - 1) * 0.5;
                for (var k = 0; k < n; k++)
                {
                    var x0 = Math.Round((-total / 2 + k * 2.5) * 20) / 20;
                    var padMin = new[] { x0 - 0.05, y0 - 0.05, -0.4 };
                    var padMax = new[] { x0 + 2.05, y0 + cl + 0.05, 0.04 };
                    // (a lip under 2 m takes a 1 x 2 plate on its side, its long side across)
                    var (cw, ch, ct) = cl >= 2 ? (2, 2, PartKit.Generators.BuildingParts.Turn("+x", "-z")) : (1, 2, PartKit.Generators.BuildingParts.Turn("+y", "-z"));
                    Add($"cutter{k}", "bow", 0, cw, ch, 0, 0, false, ct, [x0, y0, padMin[2]], padMin, padMax, -1, "cutter");
                }
            }
        }
        if (h.Bow == ProcBow.Hammer && h.HammerHalf > 0)
        {
            var st = h.Stations[0];
            double y0 = Q(h.BeamY - 1.0, 0.05), y1 = y0 + 2, za = Q(Math.Max(0.3, (h.HammerZ1 - 2) / 2), 0.05), zb = za + 2;
            if (zb <= h.HammerZ1 - 0.2 && y0 > st.P[0].Y + 0.2 && y1 < st.P[4].Y - 0.2)
                foreach (var sd in new[] { -1, 1 })
                    SidePlate($"sensor{(sd < 0 ? "P" : "S")}", "tip", sd, 2, 2, y0, za, "sensor", 0, 0.0);
        }
        foreach (var pr in h.Prongs.Where(_ => Role is ProcRole.Salvager or ProcRole.Tender))
        {
            // Clamps on the inner face, facing the slot: one, or two on a long prong.
            var n = pr.Z1 - pr.Body >= 6.0 ? 2 : 1;
            double y0 = Q(pr.Y - 1.0, 0.05), face = pr.Inner - pr.Side * 0.3;
            if (pr.SY - pr.C < 1.05)
                continue;
            for (var k = 0; k < n; k++)
            {
                var za = Q(pr.Body + 0.4 + k * ((pr.Z1 - pr.Body - 2.8) / Math.Max(1, n - 1)), 0.05);
                double[] padMin = pr.Side > 0 ? [face, y0 - 0.05, za - 0.05] : [pr.Inner - 0.04, y0 - 0.05, za - 0.05];
                double[] padMax = pr.Side > 0 ? [pr.Inner + 0.04, y0 + 2.05, za + 2.05] : [face, y0 + 2.05, za + 2.05];
                var rr = pr.Side > 0 ? PartKit.Generators.BuildingParts.Turn("+y", "-x") : PartKit.Generators.BuildingParts.Turn("-y", "+x");
                Add($"clamp{(pr.Side < 0 ? "P" : "S")}{k}", "prong", 0, 2, 2, 0, 0, false, rr, [face, y0, za], padMin, padMax, -1, "clamp");
            }
        }

        // The flanks: plates each side on the beam line standing out of the skin on pads: a salvager's arms, a carrier's launch bays
        // (a row aft of the lobby), a comms ship's sensors (fore), else the 2 x 3 m flank plates (most gunships, some others), by the payload.
        var f = dice.For("sockets.flank");
        var flankRoll = f.U();
        var (fw, fl, fuse, fore) = Role switch
        {
            ProcRole.Salvager => (2, 2, "arm", true),
            ProcRole.Carrier => (3, 3, "launch bay", false),
            ProcRole.Comms when h.Bow != ProcBow.Hammer => (2, 2, "sensor", true),
            _ => (2, 3, "flank", true),
        };
        var flankChance = fuse switch
        {
            "flank" => (Role == ProcRole.Gunship || cls != ProcClass.Corvette ? Role == ProcRole.Gunship ? 0.8 : 0.4 : 0) * Math.Min(1, Kit.Scale(cls)),
            "launch bay" => cls == ProcClass.Corvette ? 0 : 1,
            _ => 1,
        };
        if (Role is not ProcRole.Stealth && flankRoll < flankChance)
        {
            // (a carrier's launch bays in a row aft along the midbody, as many a side as its payload keeps: a destroyer's up to three)
            var count = fuse == "launch bay" ? Kit.Cap(cls, cls == ProcClass.Destroyer ? 3 : 2, 1) : 1;
            double y0 = Q(h.BeamY - fw / 2.0, 0.05);
            var tag = fuse == "flank" ? "flank" : fuse.Split(' ')[^1];
            var placed = 0;
            for (var za = fore ? Q(Zl - 1.5 - fl, 0.25) : Q(Zl + Ll + 1.5, 0.25); placed < count; za = fore ? za - fl - 1.5 : za + fl + 1.5)
            {
                var zb = za + fl;
                if (za < h.MidZ0 + 0.25 || zb > h.MidZ1 - 0.25)
                    break;
                if (Locks.Any(l => l.Z0 < zb + 0.5 && l.Z1 > za - 0.5) || h.Nacelles.Any(q => q.PZ0 < zb + 0.5 && q.PZ1 > za - 0.5))
                    continue;
                foreach (var sd in new[] { -1, 1 })
                    SidePlate($"{tag}{(sd < 0 ? "P" : "S")}{(count > 1 ? placed.ToString(CultureInfo.InvariantCulture) : "")}", "flank", sd, fw, fl, y0, za, fuse, 0, 0.0);
                placed++;
            }
        }

        // A lancer's capacitors: 2 x 2 plates down the port side of the capacitor gallery (or the spinal gallery), clear of the
        // barrel and its walkway; the railgun fires from them (Capacitor Banks, ship-works.md 8).
        if (h.Spinal)
        {
            var gal = Rooms.FirstOrDefault(q => q.Deck == 0 && q.Name == "Capacitor gallery") ?? Rooms.FirstOrDefault(q => q.Deck == 0 && q.Name == "Spinal gallery");
            var want = Kit.Cap(cls, cls switch { ProcClass.Corvette => 2, ProcClass.Frigate => 3, _ => 5 }, 1);
            if (gal != null && -gal.X0 - 0.2 - 2 >= ArchHalf + 0.25)
            {
                var x0 = Math.Round((gal.X0 + 0.2) * 20) / 20;
                var n = 0;
                for (var za = Q(gal.Z0 + 1.2, 0.05); za + 2 <= gal.Z1 - 1.2 + 1e-6 && n < want; za = Q(za + 2.5, 0.05))
                    Add($"capacitor{n++}", "floor", 0, 2, 2, 0, 2.0, false, 0, [x0, FloorTop(0), za], null, null, 0, "capacitor");
            }
        }
    }

    /// <summary>
    /// A plate standing out of the skin on side s on a pad (the flanks, a hammer's tips): W metres up from y0, L along from za; the pad
    /// from inside the skin (clear of the rooms there) out past its widest point there.
    /// </summary>
    private void SidePlate(string name, string where, int s, int w, int l, double y0, double za, string use, int kind, double extra)
    {
        var h = Hull;
        double zb = za + l, y1 = y0 + w;
        var most = 0.0;
        foreach (var z in new[] { za, (za + zb) / 2, zb })
            foreach (var y in new[] { y0, (y0 + y1) / 2, y1, h.BeamY })
                if (y >= y0 - 1e-9 && y <= y1 + 1e-9 || y == h.BeamY && h.BeamY > y0 && h.BeamY < y1)
                    most = Math.Max(most, h.SkinHalf(z, y));
        var xo = Math.Ceiling((most + 0.1 + extra) * 100) / 100;
        var xi = Math.Floor((Math.Min(Math.Min(h.SkinHalf(za, y0), h.SkinHalf(za, y1)), Math.Min(h.SkinHalf(zb, y0), h.SkinHalf(zb, y1))) - 0.7) * 100) / 100;
        var deckW = Enumerable.Range(0, Decks).Where(d => FloorLo(d) < y1 && (d == Decks - 1 ? h.Roof : FloorLo(d + 1)) > y0).Select(d => WMax(d, za - 0.1, zb + 0.1)).DefaultIfEmpty(0).Max();
        xi = Math.Max(xi, deckW + 0.05);
        if (xo - xi < 0.3)
            return;
        var rr = s > 0 ? PartKit.Generators.BuildingParts.Turn("-y", "+x") : PartKit.Generators.BuildingParts.Turn("+y", "-x");
        var padMin = new[] { s > 0 ? xi : -xo - 0.04, y0 - 0.05, za - 0.05 };
        var padMax = new[] { s > 0 ? xo + 0.04 : -xi, y1 + 0.05, zb + 0.05 };
        var face = s > 0 ? padMax[0] : padMin[0];
        Add(name, where, kind, w, l, 0, 0, false, rr, [face, y0, za], padMin, padMax, -1, use);
    }

    /// <summary>
    /// Plates on a room's floor: rows from its aft end forward (a hall's, or one entered from the bow, leave a walkway at both ends),
    /// as many across as fit, at most <paramref name="most"/>; the plate 3 x 3 m where the room is big enough, else 2 x 3 or 2 x 2.
    /// </summary>
    private void CargoGrid(ProcRoom hold, ref int ci, double above, int most, bool hall, string use, string prefix = "cargo")
    {
        var hw = hold.X1 - hold.X0;
        var hl = hold.Z1 - hold.Z0;
        var (cw, cl) = hw >= 3.6 && hl >= 4.5 ? (3, 3) : hw >= 2.6 ? (2, 3) : (2, 2);
        var aftMargin = hall ? 1.2 : 0.4;
        if (hw < cw + 0.4 || hl < cl + aftMargin + 1.1)
            return;
        var nx = Math.Max(1, (int)Math.Floor((hw - 0.8 + 0.5) / (cw + 0.5)));
        var rows = new List<double>();
        for (var za = Q(hold.Z1 - aftMargin - cl, 0.05); rows.Count == 0 || za >= hold.Z0 + 1.2 - 1e-6; za = Q(za - cl - 0.5, 0.05))
            rows.Add(za);
        while (rows.Count > 1 && rows.Count * nx > most)
            rows.RemoveAt(rows.Count - 1);
        while (nx > 1 && rows.Count * nx > most)
            nx--;
        var total = nx * cw + (nx - 1) * 0.5;
        foreach (var za in rows)
            for (var i = 0; i < nx; i++)
            {
                var x0 = Math.Round((-total / 2 + i * (cw + 0.5)) * 20) / 20;
                Add($"{prefix}{ci++}", "floor", 0, cw, cl, 0, above, false, 0, [x0, FloorTop(hold.Deck), za], null, null, hold.Deck, use);
            }
    }

    /// <summary>
    /// A lancer's barrel, once engineering and its reactors are placed: from the muzzle (a lance's barrel 0.35 m proud of its tip)
    /// along the lowest deck's centreline to the breech, which ends 0.6 m short of the nearest reactor plate (or 2.2 m into engineering).
    /// </summary>
    private void MakeBarrel()
    {
        var h = Hull;
        if (!h.Spinal)
            return;
        var eng = Rooms.FirstOrDefault(q => q.Kind == "engineering" && q.Deck == 0)
            ?? throw new ProcShipException("a lancer's railgun needs engineering on its lowest deck (its breech)");
        var z1 = eng.Z0 + BreechLen + 0.6;
        foreach (var r in Sockets.Where(q => q.Use == "reactor" && q.Deck == 0 && q.Min[0] < BreechHalf + 0.1 && q.Max[0] > -BreechHalf - 0.1))
            z1 = Math.Min(z1, r.Min[2] - 0.6);
        if (z1 - eng.Z0 < BreechLen * 0.6)
            throw new ProcShipException($"no room for the railgun's breech in the {eng.Name.ToLowerInvariant()} ({eng.Z1 - eng.Z0:0.#} m long)");
        z1 = Q(z1, 0.05);
        var z0 = h.Bow == ProcBow.Lance ? h.MuzzleZ - 0.35 : h.MuzzleZ;
        Barrel = new ProcBarrel(z0, z1, Q(z1 - BreechLen, 0.05), h.BarrelY, ProcHullShape.BarrelHalf, ProcHullShape.BarrelHalfH);
    }

    /// <summary>
    /// A socket plate W x L turned <paramref name="rot"/>, its box's corner at <paramref name="corner"/>: the plate's own back face (y -0.5
    /// in its frame) on the surface. Works out where its b00.00 goes and which way that socket faces.
    /// </summary>
    private void Add(string name, string where, int kind, int w, int l, int style, double above, bool keyed, int rot, double[] corner, double[]? padMin, double[]? padMax, int deck, string use)

    {
        // (the room over a plate is at most the table's share of its long side: a tall hangar's craft pad keeps to it)
        var tbl = ModuleSocketTable.Current;
        above = Math.Min(above, Math.Floor(tbl.MostAbove(l) / tbl.AboveStep + 1e-6) * tbl.AboveStep);
        // The plate's box in its own frame (ModuleSocketParts): x -0.5..w-0.5, y -0.5..-0.399, z -0.5..l-0.5; b00.00 at (-0.25, -0.5, -0.25) facing -y.
        double[] lo = [-0.5, -0.5, -0.5], hi = [w - 0.5, -0.399, l - 0.5];
        var a = PartKit.Generators.BuildingParts.Apply(rot, lo);
        var b = PartKit.Generators.BuildingParts.Apply(rot, hi);
        double[] mn = [Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2])];
        double[] mx = [Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2])];
        // The back face's axis: where the plate's -y goes; the corner given is on the surface, the box out from it.
        var backDir = PartKit.Generators.BuildingParts.ApplyDir(rot, 3);
        var ax = backDir >> 1;
        var outward = (backDir & 1) == 1; // (the back faces -axis: the plate stands out toward +axis)
        var origin = new double[3];
        for (var k = 0; k < 3; k++)
            origin[k] = k == ax ? corner[k] - (outward ? mn[k] : mx[k]) : corner[k] - mn[k];
        var sock = PartKit.Generators.BuildingParts.Apply(rot, [-0.25, -0.5, -0.25]);
        double[] at = [Math.Round((origin[0] + sock[0]) * 100) / 100, Math.Round((origin[1] + sock[1]) * 100) / 100, Math.Round((origin[2] + sock[2]) * 100) / 100];
        var dir = PartKit.Generators.BuildingParts.Dirs[backDir ^ 1];
        Sockets.Add(new ProcSocket(name, where, kind, w, l, style, above, keyed, rot,
            [origin[0] + mn[0], origin[1] + mn[1], origin[2] + mn[2]], [origin[0] + mx[0], origin[1] + mx[1], origin[2] + mx[2]], at, dir, padMin, padMax, deck, use));
    }
}

public sealed class ProcShipException(string message) : Exception(message);
