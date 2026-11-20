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

/// <summary>A room's clear floor (for the gallery's labels and the sockets), on deck Deck.</summary>
public sealed record ProcRoom(int Deck, string Name, string Kind, double X0, double Z0, double X1, double Z1);

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
/// A Module Socket plate W x L (module-sockets.md): where it goes (transom, spine, flank, floor), its kind, style and keying, the turn
/// it is fitted with, its plate's box (ship metres), the socket its b00.00 goes on and the pad under it (none on a floor).
/// </summary>
public sealed record ProcSocket(string Name, string Where, int Kind, int W, int L, int Style, double Above, bool Keyed, int Rot,
    double[] Min, double[] Max, double[] Sock, string Dir, double[]? PadMin, double[]? PadMax, int Deck);

/// <summary>
/// The interior and its ways out (docs/procships/PROCSHIPS.md): each deck's room block (stepped to stay a seeded gap inside the
/// skin), its rooms (a fore room, a corridor with side rooms, a lobby amidships, an aft room), walls and doors, the ladders between
/// the lobbies, the side airlocks, the docking hatches and the module sockets. Ship metres as <see cref="ProcHullShape"/>.
/// </summary>
public sealed class ProcShipPlan
{
    public const double Wall = 0.25, Half = 0.125, DoorW = 1.5, DoorH = 2.25, Margin = 0.06, Grid = 0.5;
    public const double SideMinRoom = 1.5; // (a side room's clear depth)
    public const double MinRoomLen = 3.0;
    public const double DoorSlide = 1.5; // (a Bulkhead Door's leaf slides 1.48 m into the wall before its doorway)

    public required ProcHullShape Hull { get; init; }
    public double Gap, Ch, AftGap, Zl, Ll;
    public int LockDeck;
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

    public static ProcShipPlan Make(ProcShipSpec spec, ProcHullShape h, ProcDice dice, List<(string Key, string Value)> traits)
    {
        var p = new ProcShipPlan { Hull = h };
        var r = dice.For("rooms.layout");
        p.Gap = Q(r.Range(0.5, 0.9), 0.05);
        p.Ch = r.Chance(0.55) ? 0.75 : 1.0;
        p.AftGap = Q(r.Range(1.0, 1.6), 0.5);
        var look = dice.For("look.accent");
        p.Accent = look.Pick("#2c5aa0", "#a8402c", "#2f7a5a", "#c08a20", "#5a3c8c", "#2a8a9a", "#8c2c4c");
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
        p.Zl = Math.Clamp(Q(lo + (hi - lo) * lob.Range(0.25, 0.75)), Math.Ceiling(lo / Grid) * Grid, Math.Floor(hi / Grid) * Grid);
        if (p.Zl < lo - 1e-6 || p.Zl > hi + 1e-6)
            p.Zl = Math.Ceiling(lo / Grid) * Grid;
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
        for (var d = 0; d < h.Decks; d++)
            p.Layout(d, dice, sides, belly && d == 0, dorsal && d == h.Decks - 1);
        p.Fit(spec, dice);
        traits.Add(("Void gap", $"{p.Gap.ToString("0.0#", CultureInfo.InvariantCulture)} m"));
        traits.Add(("Corridor", $"{(p.Ch * 2).ToString("0.#", CultureInfo.InvariantCulture)} m"));
        traits.Add(("Rooms", p.Rooms.Count(q => q.Kind != "corridor").ToString(CultureInfo.InvariantCulture)));
        traits.Add(("Side locks", sides.Length == 2 ? "both sides" : sides[0] < 0 ? "port" : "starboard"));
        traits.Add(("Belly hatch", belly ? "yes" : "no"));
        traits.Add(("Dorsal hatch", dorsal ? "yes" : "no"));
        traits.Add(("Accent", p.Accent));
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

    private void Layout(int d, ProcDice dice, int[] lockSides, bool belly, bool dorsal)
    {
        var h = Hull;
        var r = dice.For($"rooms.deck{d}");
        double F = Front[d], B = Back[d];
        var sideMin = Ch + Wall + SideMinRoom + Wall;
        var top = d == Decks - 1;
        if (_foreEnd.Length != Decks)
            (_foreEnd, _aftStart) = (new double[Decks], new double[Decks]);
        // The fore room: the narrow runs at the front, at least 3.5 to 5 m long; it reaches the lobby if what is left is too short for side rooms.
        var fEnd = Q(F + r.Range(3.5, 5.0));
        foreach (var rr in Runs[d])
            if (rr.W < sideMin && rr.Z1 <= Zl + 1e-6)
                fEnd = Math.Max(fEnd, rr.Z1);
        if (Zl - fEnd < MinRoomLen + Wall + 0.25)
            fEnd = Zl;
        // The aft room: engineering on the lowest deck (5.5 to 7.5 m, the reactor's), a hold above (4.5 to 7 m); it takes the narrow runs at the back.
        var aStart = Q(B - (d == 0 ? r.Range(5.5, 7.5) : r.Range(4.5, 7.0)));
        foreach (var rr in Runs[d])
            if (rr.W < sideMin && rr.Z0 >= Zl + Ll - 1e-6)
                aStart = Math.Min(aStart, rr.Z0);
        if (aStart - (Zl + Ll) < MinRoomLen + Wall + 0.25 || aStart > B - 3.0)
            aStart = Zl + Ll;
        // The transverse walls, full width, fore to aft (each with a doorway on the centreline but the ends).
        var tw = new List<(double Lo, double Hi, bool Door, string Group)> { (F, F + Wall, false, $"d{d}.fore") };
        if (fEnd < Zl - 1e-6)
        {
            var (lo, hi) = Span(d, fEnd, true);
            tw.Add((lo, hi, true, $"d{d}.fore"));
        }
        tw.Add((Zl, Zl + Wall, true, $"d{d}.lobby"));
        tw.Add((Zl + Ll - Wall, Zl + Ll, true, $"d{d}.lobby"));
        if (aStart > Zl + Ll + 1e-6)
        {
            var (lo, hi) = Span(d, aStart, false);
            tw.Add((lo, hi, true, $"d{d}.aft"));
        }
        tw.Add((B - Wall, B, false, $"d{d}.aft"));
        _foreEnd[d] = tw[1].Lo;
        _aftStart[d] = tw[^2].Hi;
        foreach (var (lo, hi, door, group) in tw)
        {
            var ext = WMax(d, lo, hi);
            var w = new ProcWall(d, true, -ext, ext, (lo + hi) / 2, group);
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
        var fw = WMinOr(d, F + Wall, foreHi) - Wall;
        Rooms.Add(new ProcRoom(d, top ? "Bridge" : d == 0 && Decks > 1 ? "Fore store" : "Fore compartment", top ? "bridge" : "fore", -fw, F + Wall, fw, foreHi));
        var aftLo = tw[^2].Hi;
        var aw = WMinOr(d, aftLo, B - Wall) - Wall;
        Rooms.Add(new ProcRoom(d, d == 0 ? "Engineering" : "Hold", d == 0 ? "engineering" : "hold", -aw, aftLo, aw, B - Wall));
        var lw = W(d, Zl + Ll / 2) - Wall;
        Rooms.Add(new ProcRoom(d, d == LockDeck ? "Airlock lobby" : "Lobby", "lobby", -lw, Zl + Wall, lw, Zl + Ll - Wall));

        // The middle parts: a corridor down the centreline with side rooms either side, fore and aft of the lobby.
        var cuts = new Dictionary<int, List<(double Lo, double Hi)>> { [-1] = [], [1] = [] }; // (per side: what the outer walls stop at)
        foreach (var (lo, hi, _, _) in tw)
        {
            cuts[-1].Add((lo, hi));
            cuts[1].Add((lo, hi));
        }
        var names = dice.For($"rooms.names{d}");
        string[] pool = d == 0 && Decks > 1 ? ["Workshop", "Store", "Armoury", "Machine room", "Magazine", "Locker room", "Pump room"]
            : top ? ["Cabin", "Crew quarters", "Captain's cabin", "Ops room", "Comms room", "Office", "Med bay"]
            : ["Mess", "Galley", "Med bay", "Cabin", "Crew quarters", "Gym", "Store"];
        var named = names.Int(0, pool.Length - 1);
        foreach (var (p0, p1, grp) in new[] { (tw[1].Hi, Zl, "f"), (Zl + Ll, tw[^2].Lo, "a") })
        {
            if (p1 - p0 < MinRoomLen - 1e-6)
                continue;
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
                    Locks.Add(new ProcLock(s, d, a, w, Math.Ceiling((most + 0.14) * 100) / 100, z0, z1, y0, y1));
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
    /// The sockets (module-sockets.md): engine sockets across the transom (keyed), turret sockets along the spine, a reactor socket in
    /// engineering, a cargo socket in each hold, and on a frigate or a destroyer sometimes a flank socket each side at the beam.
    /// </summary>
    private void Fit(ProcShipSpec spec, ProcDice dice)
    {
        var h = Hull;
        var e = dice.For("sockets.engines");
        // Engines: keyed plates across the transom facing aft, a row (two rows on a destroyer of four or more), the first layout that fits.
        (int Size, int N)[] options = spec.Class switch
        {
            ProcClass.Corvette => [(2, e.Pick(2, 2, 3)), (2, 2), (2, 1)],
            ProcClass.Frigate => e.Chance(0.6) ? [(2, e.Pick(3, 4, 4)), (2, 2), (2, 1)] : [(3, 2), (2, 3), (2, 2), (2, 1)],
            _ => e.Chance(0.7) ? [(3, e.Pick(3, 4)), (3, 2), (2, 4), (2, 2)] : [(2, e.Pick(4, 6)), (2, 4), (2, 2)],
        };
        var rot = PartKit.Generators.BuildingParts.Turn("+x", "+z");
        var ring = h.Stations[^1].P;
        foreach (var (size, n) in options)
        {
            var rows = n >= 4 && spec.Class == ProcClass.Destroyer ? 2 : 1;
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
            var i = 0;
            foreach (var (x0, topY) in plates)
            {
                double[] padMin = [x0 - 0.05, topY - size - 0.05, h.Length - 0.45], padMax = [x0 + size + 0.05, topY + 0.05, h.Length + 0.04];
                Add($"engine{i++}", "transom", 1, size, size, 0, 0, true, rot, [x0, topY - size, padMax[2]], padMin, padMax, -1);
            }
            break;
        }
        // Turrets: along the spine's flat in the midbody, clear of the dorsal hatch and the slot its lid slides into.
        var t = dice.For("sockets.turrets");
        var tn = spec.Class switch { ProcClass.Corvette => t.Pick(1, 2, 2), ProcClass.Frigate => t.Pick(2, 3, 3), _ => t.Pick(3, 4, 4) };
        var flat = h.Mid[4].H;
        var ts = spec.Class == ProcClass.Destroyer && flat >= 1.75 ? 3 : 2;
        if (flat >= ts / 2.0 + 0.12)
        {
            var keep = Hatches.Where(x => x.Kind == "dorsal").Select(x => (x.Z0 - 0.6, x.Z1 + 1.8)).ToList();
            double z0 = h.MidZ0 + 0.5, z1 = h.MidZ1 - 0.5;
            var spots = new List<double>();
            var step = (z1 - z0 - ts) / Math.Max(1, tn - 1);
            for (var k = 0; k < tn; k++)
            {
                var za = Q(tn == 1 ? (z0 + z1 - ts) / 2 : z0 + k * step, 0.25);
                foreach (var (a, b) in keep)
                    if (za < b && za + ts > a)
                        za = za + ts / 2.0 < (a + b) / 2 ? Q(a - ts - 0.25, 0.25) : Q(b + 0.25, 0.25);
                if (za < z0 - 1e-6 || za + ts > z1 + 1e-6 || spots.Any(o => Math.Abs(o - za) < ts + 1.0))
                    continue;
                spots.Add(za);
            }
            var i = 0;
            foreach (var za in spots.OrderBy(v => v))
            {
                var x0 = -ts / 2.0;
                var topY = h.TopY;
                var padMin = new[] { x0 - 0.05, topY - 0.3, za - 0.05 };
                var padMax = new[] { x0 + ts + 0.05, topY + 0.04, za + ts + 0.05 };
                Add($"turret{i++}", "spine", 3, ts, ts, 0, 0, false, 0, [x0, padMax[1], za], padMin, padMax, -1);
            }
        }
        // The reactor: on engineering's floor, in its aft part, room to stand over it.
        var eng = Rooms.First(q => q.Kind == "engineering");
        var rs = eng.X1 - eng.X0 >= 3.6 && eng.Z1 - eng.Z0 >= 4.5 ? 3 : 2;
        if (eng.X1 - eng.X0 >= rs + 0.4 && eng.Z1 - eng.Z0 >= rs + 1.5)
        {
            var za = Q(eng.Z1 - 0.4 - rs, 0.05);
            Add("reactor", "floor", 2, rs, rs, 0, 2.5, false, 0, [-rs / 2.0, FloorTop(0), za], null, null, 0);
        }
        // Cargo: a plate on each hold's floor.
        foreach (var hold in Rooms.Where(q => q.Kind == "hold"))
        {
            var hw = hold.X1 - hold.X0;
            var (cw, cl) = hw >= 3.6 && hold.Z1 - hold.Z0 >= 4.5 ? (3, 3) : hw >= 2.6 ? (2, 3) : (2, 2);
            if (hw < cw + 0.4 || hold.Z1 - hold.Z0 < cl + 1.5)
                continue;
            var za = Q(hold.Z1 - 0.4 - cl, 0.05);
            Add($"hold{hold.Deck}", "floor", 0, cw, cl, 0, 2.5, false, 0, [-cw / 2.0, FloorTop(hold.Deck), za], null, null, hold.Deck);
        }
        // Flank sockets: a 2 x 3 m plate each side on the beam line, fore of the lobby, standing out of the skin on a pad.
        var f = dice.For("sockets.flank");
        if (spec.Class != ProcClass.Corvette && f.Chance(0.55))
        {
            var za = Q(Zl - 1.5 - 3.0, 0.25);
            var zb = za + 3.0;
            if (za > h.MidZ0 + 0.25 && !Locks.Any(l => l.Z0 < zb + 0.5 && l.Z1 > za - 0.5))
            {
                double y0 = Q(h.BeamY - 1.0, 0.05), y1 = y0 + 2.0;
                foreach (var s in new[] { -1, 1 })
                {
                    var most = Math.Max(h.SkinHalf(za, h.BeamY), h.SkinHalf(zb, h.BeamY));
                    var xo = Math.Ceiling((most + 0.1) * 100) / 100;
                    var xi = Math.Floor((Math.Min(h.SkinHalf(za, y0), h.SkinHalf(za, y1)) - 0.7) * 100) / 100;
                    var deckW = Enumerable.Range(0, Decks).Where(d => FloorLo(d) < y1 && (d == Decks - 1 ? Hull.Roof : FloorLo(d + 1)) > y0).Select(d => WMax(d, za - 0.1, zb + 0.1)).DefaultIfEmpty(0).Max();
                    xi = Math.Max(xi, deckW + 0.05);
                    if (xo - xi < 0.3)
                        continue;
                    var rr = s > 0 ? PartKit.Generators.BuildingParts.Turn("-y", "+x") : PartKit.Generators.BuildingParts.Turn("+y", "-x");
                    var padMin = new[] { s > 0 ? xi : -xo - 0.04, y0 - 0.05, za - 0.05 };
                    var padMax = new[] { s > 0 ? xo + 0.04 : -xi, y1 + 0.05, zb + 0.05 };
                    var face = s > 0 ? padMax[0] : padMin[0];
                    Add($"flank{(s < 0 ? "P" : "S")}", "flank", 0, 2, 3, 0, 0, false, rr, [face, y0, za], padMin, padMax, -1);
                }
            }
        }
    }

    /// <summary>
    /// A socket plate W x L turned <paramref name="rot"/>, its box's corner at <paramref name="corner"/>: the plate's own back face (y -0.5
    /// in its frame) on the surface. Works out where its b00.00 goes and which way that socket faces.
    /// </summary>
    private void Add(string name, string where, int kind, int w, int l, int style, double above, bool keyed, int rot, double[] corner, double[]? padMin, double[]? padMax, int deck)
    {
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
            [origin[0] + mn[0], origin[1] + mn[1], origin[2] + mn[2]], [origin[0] + mx[0], origin[1] + mx[1], origin[2] + mx[2]], at, dir, padMin, padMax, deck));
    }
}

public sealed class ProcShipException(string message) : Exception(message);
