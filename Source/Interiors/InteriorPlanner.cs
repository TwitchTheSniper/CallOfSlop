using System.Numerics;

namespace CallOfSlop.Interiors;

/// <summary>
/// What the planner fits an interior into, in metres in the hull's own frame: x along the spine from 0, y up from the lowest floor, z across
/// from 0. <see cref="KeepOuts"/> are boxes nothing may be built in (leg hips, turret rings, the drive). Nothing here is about vehicles, so a
/// static base gives its plot the same way. <see cref="Anchored"/> (phase 10): the shafts must stand where a part's hatches are (a hollow chassis
/// and the deck over it), so the spine, the north row and the shafts keep their classic places there.
/// </summary>
public sealed record HullVolume(Vector3 Size, IReadOnlyList<(Vector3 Min, Vector3 Max)> KeepOuts, bool BowAtMinX, bool Anchored = false);

/// <summary>A seeded random stream that is the same on every machine (SplitMix64; never System.Random or hash codes).</summary>
public sealed class InteriorRng(int seed)
{
    private ulong _s = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        var z = _s += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return ((z ^ (z >> 31)) >> 11) * (1.0 / (1UL << 53));
    }
}

/// <summary>
/// The planner (docs/sandbox/INTERIORS.md "The planner"): a hull volume and keep-outs to decks, a corridor spine, the reserved logistics lane,
/// stair and elevator shafts, programme rooms placed by adjacency, leftovers turned to storage, fuel, void or merged, doors until every room is
/// reachable, bulkheads and entry points. Deterministic for a seed. Offline only: it allocates freely.
/// </summary>
public static class InteriorPlanner
{
    private sealed record Band(int Strip, string Kind, int Z0, int Z1) // Strip 0 north of the spine, 1 south
    {
        public int Depth => Z1 - Z0 + 1;
    }

    /// <param name="sealed">No doors to the outside (a hull inside another, as a chassis void: its way in is the stairs and the lift).</param>
    public static InteriorPlan Plan(InteriorData data, SizeClass cls, HullVolume hull, int seed, bool @sealed = false)
    {
        var rules = cls.Deck;
        var rng = new InteriorRng(seed);
        var p = new InteriorPlan
        {
            Class = cls, Seed = seed, BowAtMinX = hull.BowAtMinX, Sealed = @sealed,
            W = (int)MathF.Floor(hull.Size.X / rules.GridCell + 1e-4f),
            D = (int)MathF.Floor(hull.Size.Z / rules.GridCell + 1e-4f),
            Decks = Math.Clamp((int)MathF.Floor(hull.Size.Y / rules.Storey + 1e-4f), 1, rules.Decks),
        };
        if (p.W < 3 || p.D < rules.Corridor + 2)
        {
            p.Refused.Add($"the hull is {p.W}x{p.D} cells: too small for a corridor and rooms");
            p.Cells = [new int[Math.Max(p.W, 1), Math.Max(p.D, 1)]];
            return p;
        }
        p.Cells = new int[p.Decks][,];
        for (var k = 0; k < p.Decks; k++)
        {
            p.Cells[k] = new int[p.W, p.D];
            for (var x = 0; x < p.W; x++)
                for (var z = 0; z < p.D; z++)
                    p.Cells[k][x, z] = InteriorPlan.Free;
        }
        // keep-outs: any cell a keep-out box reaches into, on the decks its height reaches
        foreach (var (lo, hi) in hull.KeepOuts)
            for (var k = 0; k < p.Decks; k++)
            {
                if (hi.Y <= k * rules.Storey || lo.Y >= (k + 1) * rules.Storey)
                    continue;
                for (var x = 0; x < p.W; x++)
                    for (var z = 0; z < p.D; z++)
                        if (hi.X > x * rules.GridCell && lo.X < (x + 1) * rules.GridCell && hi.Z > z * rules.GridCell && lo.Z < (z + 1) * rules.GridCell)
                            p.Cells[k][x, z] = InteriorPlan.KeepOut;
            }
        // (a deck over a keep-out has no slab there: the builder lays none on what can't hold one, so the cell is out on the decks above too)
        for (var k = 1; k < p.Decks; k++)
            for (var x = 0; x < p.W; x++)
                for (var z = 0; z < p.D; z++)
                    if (p.Cells[k - 1][x, z] == InteriorPlan.KeepOut)
                        p.Cells[k][x, z] = InteriorPlan.KeepOut;
        // the spine, on every deck: off the centreline by a seeded cell or two (phase 10: a mirrored plan feels wrong to move through), keeping
        // each strip at least a row deep plus one. Drawn from the seed and the hull's size alone, so a chassis void and the deck above it,
        // planned from one seed on one grid, put their spines, bands and shafts in the same cells.
        var arng = new InteriorRng(seed * 7919 + p.D * 31 + p.W);
        p.SpineZ0 = (p.D - rules.Corridor) / 2;
        {
            var minStrip = rules.RowDepth + 1;
            var offs = new List<int>();
            foreach (var o in new[] { -2, -1, 1, 2 })
            {
                var z0 = p.SpineZ0 + o;
                if (z0 >= minStrip && p.D - (z0 + rules.Corridor) >= minStrip + (rules.ReserveRoute ? 1 : 0))
                    offs.Add(o);
            }
            if (offs.Count > 0 && p.D >= 8 && !hull.Anchored)
                p.SpineZ0 += offs[(int)(arng.Next() * offs.Count) % offs.Count];
        }
        p.SpineZ1 = p.SpineZ0 + rules.Corridor - 1;
        // (phase 10) each strip's inner row its own depth, a cell either side of the class's, so the rooms either side differ in depth
        p.RowNorth = Math.Max(2, rules.RowDepth + (int)(arng.Next() * 3) - 1);
        p.RowSouth = Math.Max(2, rules.RowDepth + (int)(arng.Next() * 3) - 1);
        if (hull.Anchored)
            p.RowNorth = rules.RowDepth; // (the hatches are a class row deep)
        if (p.RowSouth == p.RowNorth)
            p.RowSouth = p.RowNorth == 2 ? 3 : p.RowNorth - 1;

        // (phase 10) the shafts off their even spacing: the lift off the middle, the stairs at seeded points in their halves
        var liftAt = 0.35f + (float)arng.Next() * 0.3f;
        var stairAt = new[] { 0.15f + (float)arng.Next() * 0.15f, 0.65f + (float)arng.Next() * 0.2f };
        if (hull.Anchored)
            (liftAt, stairAt) = (0.5f, new[] { 0.25f, 0.75f }); // (the hollow chassis's hatches: tools/constructs/interior_parts.py HATCHES)
        for (var k = 0; k < p.Decks; k++)
            for (var x = 0; x < p.W; x++)
                for (var z = p.SpineZ0; z <= p.SpineZ1; z++)
                    Claim(p, k, x, z, InteriorPlan.Corridor);
        // the reserved logistics lane beside it on the lowest deck (the train track's later; kept clear now)
        var laneRow = -1;
        if (rules.ReserveRoute && p.SpineZ1 + 3 < p.D)
        {
            laneRow = p.SpineZ1 + 1;
            for (var x = 0; x < p.W; x++)
                Claim(p, 0, x, laneRow, InteriorPlan.Lane);
            p.ReservedRoute = new GridRect(0, laneRow, p.W - 1, laneRow);
        }
        var bands = new List<Band>[p.Decks];
        for (var k = 0; k < p.Decks; k++)
            bands[k] = Bands(p, rules, k == 0 && laneRow >= 0 ? laneRow + 1 : p.SpineZ1 + 1);

        // shafts: the same rectangle on every deck, in the north inner band (the lane doesn't move it), each inside one section
        var north = bands[0].Where(b => b.Strip == 0 && b.Kind != "outer").OrderBy(b => b.Kind == "inner" ? 0 : 1).First();
        if (p.Decks > 1)
        {
            var stairCount = p.W >= 2 * rules.Section ? 2 : 1;
            for (var i = 0; i < stairCount; i++)
                PlaceShaft(p, data.Rooms["stairs"], "stairs", rules.Stair.W, north, (int)(p.W * (stairCount == 1 ? 0.5f : stairAt[i])));
            PlaceShaft(p, data.Rooms["elevator"], "elevator", rules.Elevator.W, north, (int)(p.W * liftAt));
        }

        // phase 9: the halls first (big volumes through several storeys), then the people-sized rooms round them
        var programme = cls.Programme.ToList();
        Halls(p, data, cls, bands, programme, rng);
        // the programme: which deck, then where on it
        var wants = programme.Select(id => data.Rooms[id]).ToList();
        var order = wants.Select((t, i) => (t, i)).OrderByDescending(e => e.t.Critical).ThenByDescending(e => e.t.MinW * e.t.MinD).ThenByDescending(e => e.t.Want).ThenBy(e => e.i).Select(e => e.t).ToList();
        var deckArea = new int[p.Decks];
        foreach (var t in order)
        {
            var deck = t.Deck switch { DeckWant.Lower => 0, DeckWant.Upper => p.Decks - 1, _ => Array.IndexOf(deckArea, deckArea.Min()) };
            if (!PlaceRoom(p, data, t, deck, bands[deck], rules, rng) && !(p.Decks > 1 && PlaceRoom(p, data, t, deck == 0 ? p.Decks - 1 : 0, bands[deck == 0 ? p.Decks - 1 : 0], rules, rng)))
                p.Refused.Add($"no room for the {t.Name} ({t.MinW}x{t.MinD} cells at least) on any deck");
            else
                deckArea[p.Rooms[^1].Deck] += p.Rooms[^1].Area;
        }
        for (var k = 0; k < p.Decks; k++)
            Leftovers(p, data, k, bands[k], rng);
        Doors(p, data, bands);
        Bulkheads(p, laneRow);
        Reach(p);
        var marks = "ABCDFGHIJKLMNOPQRTUVWXYZabcdghijklmnopqrstuvwyz0123456789";
        var n = 0;
        foreach (var r in p.Rooms)
            r.Mark = r.Shaft == "stairs" ? 'S' : r.Shaft == "elevator" ? 'E' : r.Type == "dead" ? 'x' : r.Type == "fuel" ? 'f' : r.Type == "spare" ? '-' : r.Type == "storage" && r.Origin == "leftover" ? 's' : marks[n++ % marks.Length];
        return p;
    }

    /// <summary>
    /// The class's halls (phase 9, INTERIORS.md "Phase 9"): each a room through several storeys, placed before the other rooms, as long as its
    /// share of the hull asks (the longest that fits, down to its minimum), across a whole strip on one side of the spine (or the strip's outer
    /// band where the shafts stand in the inner one). Its floor room is on the lowest storey; each storey above holds a gallery room over the same
    /// cells (out to the spine on that deck), whose floor the builder lays only round its walls and its stair column. A hall of a programme room
    /// takes that room's place in <paramref name="programme"/>. Each hall goes on the side with less hall floor so far, for a lopsided plan.
    /// </summary>
    private static void Halls(InteriorPlan p, InteriorData data, SizeClass cls, List<Band>[] bands, List<string> programme, InteriorRng rng)
    {
        var area = new int[2];
        foreach (var h in cls.Halls.Where(h => h.Where == "any" || (h.Where == "chassis") == p.Sealed))
        {
            var storeys = Math.Min(h.Storeys, p.Decks);
            if (storeys < 2)
                continue;
            var t = data.Rooms[h.Room];
            var len = Math.Clamp((int)Math.Round(h.Share * p.W), h.MinW, p.W);
            (GridRect[] Rects, int K0, Band Band, double Score)? best = null;
            for (var k0 = 0; k0 + storeys <= p.Decks; k0++)
                foreach (var band in bands[k0].Where(b => b.Kind is "full" or "outer"))
                {
                    if (band.Depth < Math.Max(t.MinD, 3))
                        continue;
                    var found = false;
                    for (var w = len; w >= h.MinW && !found; w--)
                        for (var x0 = 0; x0 + w <= p.W; x0++)
                        {
                            var rects = new GridRect[storeys];
                            var ok = true;
                            for (var j = 0; j < storeys && ok; j++)
                            {
                                // (a storey above reaches out to its own deck's strip edge: the lane is on the lowest deck only)
                                var up = band.Kind == "full" ? bands[k0 + j].FirstOrDefault(b => b.Strip == band.Strip && b.Kind == "full") : band;
                                if (up == null)
                                    ok = false;
                                else
                                {
                                    rects[j] = new GridRect(x0, Math.Min(band.Z0, up.Z0), x0 + w - 1, Math.Max(band.Z1, up.Z1));
                                    ok = FreeRect(p, k0 + j, j == 0 ? new GridRect(x0, band.Z0, x0 + w - 1, band.Z1) : rects[j]);
                                    if (j == 0)
                                        rects[0] = new GridRect(x0, band.Z0, x0 + w - 1, band.Z1);
                                }
                            }
                            if (!ok || p.SectionOf(x0 + w - 1) - p.SectionOf(x0) > 1)
                                continue;
                            found = true;
                            // (on the open deck the bow section is the crew's: the bridge stands there, by the legs its helm drives)
                            var bowSec = p.SectionOf(p.BowAtMinX ? 0 : p.W - 1);
                            var crew = !p.Sealed && (p.SectionOf(x0) == bowSec || p.SectionOf(x0 + w - 1) == bowSec) ? 40 : 0;
                            // (deep first, then the side with less hall so far, then the hull's ends: halls at the ends leave the middle to the crew)
                            var sc = w * band.Depth * 0.5 + band.Depth * 2 - area[band.Strip] * 0.3 + (band.Kind == "full" ? 3 : 0) + Math.Abs(x0 + w * 0.5 - p.W * 0.5) * 0.1 + rng.Next() * 2 - crew;
                            if (best == null || sc > best.Value.Score)
                                best = (rects, k0, band, sc);
                        }
                }
            if (best is not { } pick)
            {
                p.Notes.Add($"no place for a {t.Name} hall ({h.MinW} cells long and {storeys} storeys at least)");
                continue;
            }
            var origin = programme.Remove(h.Room) ? "programme" : "hall";
            var floor = Add(p, t, pick.K0, pick.Rects[0], origin, pick.Band);
            floor.Hall = floor.Id;
            floor.Storeys = storeys;
            floor.Set = data.Sets.ContainsKey(h.Room + "Hall") ? h.Room + "Hall" : null;
            var g = data.Rooms["gallery"];
            for (var j = 1; j < storeys; j++)
            {
                var up = Add(p, g, pick.K0 + j, pick.Rects[j], "gallery", pick.Band);
                up.Name = $"{t.Name} gallery";
                up.Hall = floor.Id;
            }
            area[pick.Band.Strip] += pick.Rects[0].Area;
            p.Notes.Add($"hall: {t.Name} {pick.Rects[0]} on deck {pick.K0}, {storeys} storeys ({pick.Rects[0].W * 4} x {pick.Rects[0].D * 4} x {storeys * 3} m)");
        }
    }

    private static void Claim(InteriorPlan p, int k, int x, int z, int what)
    {
        if (p.Cells[k][x, z] == InteriorPlan.Free)
            p.Cells[k][x, z] = what;
    }

    /// <summary>The strips either side of the spine, each split into an inner band (on the corridor) and an outer band (on the hull side) when both are deep enough.</summary>
    private static List<Band> Bands(InteriorPlan p, DeckRules rules, int southStart)
    {
        var list = new List<Band>();
        void Strip(int strip, int z0, int z1, bool innerAtHigh)
        {
            var depth = z1 - z0 + 1;
            if (depth < 1)
                return;
            list.Add(new Band(strip, "full", z0, z1));
            var row = strip == 0 ? p.RowNorth : p.RowSouth; // (phase 10: each strip its own)
            if (row <= 0)
                row = rules.RowDepth;
            if (depth - row < 2)
                row = Math.Max(2, depth - 2); // (keep an outer band of two where the strip allows)
            if (depth - row < 2)
                return; // (an outer band of one cell would be all void: the strip stays one band)
            if (innerAtHigh)
            {
                list.Add(new Band(strip, "inner", z1 - row + 1, z1));
                list.Add(new Band(strip, "outer", z0, z1 - row));
            }
            else
            {
                list.Add(new Band(strip, "inner", z0, z0 + row - 1));
                list.Add(new Band(strip, "outer", z0 + row, z1));
            }
        }
        Strip(0, 0, p.SpineZ0 - 1, true);
        Strip(1, southStart, p.D - 1, false);
        // (a strip with no split: its full band is also its inner band)
        foreach (var s in new[] { 0, 1 })
            if (list.Count(b => b.Strip == s) == 1)
                list.Add(list.First(b => b.Strip == s) with { Kind = "inner" });
        return list;
    }

    private static bool FreeRect(InteriorPlan p, int k, GridRect r)
    {
        if (r.X0 < 0 || r.X1 >= p.W || r.Z0 < 0 || r.Z1 >= p.D)
            return false;
        for (var x = r.X0; x <= r.X1; x++)
            for (var z = r.Z0; z <= r.Z1; z++)
                if (p.Cells[k][x, z] != InteriorPlan.Free)
                    return false;
        return true;
    }

    private static PlanRoom Add(InteriorPlan p, RoomType t, int k, GridRect r, string origin, Band band)
    {
        var room = new PlanRoom
        {
            Id = p.Rooms.Count, Type = t.Id, Name = t.Name, Deck = k, Rect = r, Layout = t.Layout, Section = p.SectionOf(r.X0),
            Critical = t.Critical, System = t.System, Origin = origin, Inner = band.Kind != "outer", Outer = band.Kind != "inner",
        };
        p.Rooms.Add(room);
        for (var x = r.X0; x <= r.X1; x++)
            for (var z = r.Z0; z <= r.Z1; z++)
                p.Cells[k][x, z] = room.Id;
        return room;
    }

    private static void PlaceShaft(InteriorPlan p, RoomType t, string shaft, int w, Band band, int want)
    {
        var sec = p.Class.Deck.Section;
        for (var d = 0; d < p.W; d++)
            foreach (var x0 in new[] { want + d, want - d })
            {
                var r = new GridRect(x0, band.Z0, x0 + w - 1, band.Z1);
                if (x0 < 0 || r.X1 >= p.W || r.X0 / sec != r.X1 / sec || r.X0 == 0 || r.X1 == p.W - 1)
                    continue;
                if (Enumerable.Range(0, p.Decks).All(k => FreeRect(p, k, r)))
                {
                    for (var k = 0; k < p.Decks; k++)
                        Add(p, t, k, r, "system", band).Shaft = shaft;
                    return;
                }
            }
        p.Refused.Add($"no place for a {t.Name} through every deck");
    }

    /// <summary>Tries every band deep enough and every free place in one section; the best by end, band, wanted and avoided neighbours and packing.</summary>
    private static bool PlaceRoom(InteriorPlan p, InteriorData data, RoomType t, int k, List<Band> bands, DeckRules rules, InteriorRng rng)
    {
        var best = double.MinValue;
        GridRect? pick = null;
        Band? pickBand = null;
        var grow = t.MinW >= 2 ? rules.RoomGrow : 0;
        foreach (var band in bands)
        {
            if (band.Depth < t.MinD)
                continue;
            // (a free room's rectangle takes about 60% of what it wants: its wing, beside it in the next band, the rest)
            var share = t.Layout == RoomLayout.Free ? 0.6 : 1.0;
            var target = Math.Max(t.MinW + grow, (int)Math.Ceiling(share * t.Want * (1 + 0.25 * rules.RoomGrow) / band.Depth));
            for (var w = Math.Min(target, rules.Section); w >= t.MinW; w--)
            {
                for (var x0 = 0; x0 + w <= p.W; x0++)
                {
                    var r = new GridRect(x0, band.Z0, x0 + w - 1, band.Z1);
                    if (p.SectionOf(r.X0) != p.SectionOf(r.X1) || !FreeRect(p, k, r))
                        continue;
                    var s = Score(p, data, t, k, r, band, w < t.MinW + grow) + rng.Next() * 0.5;
                    if (s > best)
                    {
                        best = s;
                        pick = r;
                        pickBand = band;
                    }
                }
                if (pick != null)
                    break; // (the widest that fits anywhere wins; narrower only when nothing fits)
            }
        }
        if (pick is not { } at)
            return false;
        var room = Add(p, t, k, at, "programme", pickBand!);
        if (t.Layout == RoomLayout.Free)
            Wing(p, t, room, bands, rules);
        return true;
    }

    /// <summary>
    /// A free room's wing (phase 2): a rectangle in the other band of its strip (inner and outer), along the room's edge on that side, flush with
    /// the room's bow or stern end (its end want; else whichever fits), at least 2 cells wide and narrower than the room, so the outline is an L
    /// that follows the hull side rather than a bigger box. It takes what the rectangle left of the room's want (at least 2 cells wide).
    /// </summary>
    private static void Wing(InteriorPlan p, RoomType t, PlanRoom r, List<Band> bands, DeckRules rules)
    {
        var band = bands.FirstOrDefault(b => b.Kind != "full" && (b.Z0 == r.Rect.Z1 + 1 || b.Z1 == r.Rect.Z0 - 1));
        if (band == null)
        {
            p.Notes.Add($"the {r.Name} is free but its band has no other band beside it: a rectangle");
            return;
        }
        var want = t.Want * (1 + 0.25 * rules.RoomGrow) - r.Rect.Area;
        var w = Math.Min(r.Rect.W - 1, Math.Max(2, (int)Math.Ceiling(want / band.Depth)));
        var bowX = p.BowAtMinX ? 0 : p.W - 1;
        var atMin = t.End switch { EndWant.Bow => bowX == 0, EndWant.Stern => bowX != 0, _ => true };
        for (; w >= 2; w--)
            foreach (var min in new[] { atMin, !atMin })
            {
                var x0 = min ? r.Rect.X0 : r.Rect.X1 - w + 1;
                var g = new GridRect(x0, band.Z0, x0 + w - 1, band.Z1);
                if (p.SectionOf(g.X0) != r.Section || p.SectionOf(g.X1) != r.Section || !FreeRect(p, r.Deck, g))
                    continue;
                r.Wings.Add(g);
                for (var x = g.X0; x <= g.X1; x++)
                    for (var z = g.Z0; z <= g.Z1; z++)
                        p.Cells[r.Deck][x, z] = r.Id;
                return;
            }
        p.Notes.Add($"the {r.Name} is free but no wing fits beside it: a rectangle");
    }

    private static double Score(InteriorPlan p, InteriorData data, RoomType t, int k, GridRect r, Band band, bool tight)
    {
        var s = 0.0;
        var (cx, cz) = r.Centre;
        var bowX = p.BowAtMinX ? 0 : p.W - 1;
        if (t.End == EndWant.Bow)
            s -= 2 * MathF.Abs(cx - bowX);
        else if (t.End == EndWant.Stern)
            s -= 2 * MathF.Abs(cx - (p.W - 1 - bowX));
        s += (t.Row, band.Kind) switch
        {
            (RowWant.Inner, "inner") => 6,
            (RowWant.Outer, "outer") => 6,
            (RowWant.Outer, "full") => 4,
            (RowWant.Any, "full") => -4,
            (_, "full") => -6,
            (RowWant.Inner, "outer") => -8,
            (RowWant.Outer, "inner") => -8,
            _ => 0,
        };
        if (tight)
            s -= 3;
        foreach (var o in p.Rooms)
        {
            var near = o.Deck == k && Touches(o.Rect, r);
            var dist = MathF.Abs(o.Rect.Centre.X - cx) + MathF.Abs(o.Rect.Centre.Z - cz) + (o.Deck == k ? 0 : 6);
            if (t.Wants.Contains(o.Type) || data.Rooms[o.Type].Wants.Contains(t.Id))
                s += (near ? 8 : 0) - 0.5 * Math.Min(dist, 30);
            if (t.Avoids.Contains(o.Type) || data.Rooms[o.Type].Avoids.Contains(t.Id))
                s += (near ? -8 : 0) + 0.5 * Math.Min(dist, 20);
        }
        // packing: hug a neighbour or a section's edge, so the gaps left are whole
        bool Edge(int x) => x < 0 || x >= p.W || p.SectionOf(x) != p.SectionOf(r.X0) || Enumerable.Range(r.Z0, r.D).Any(z => p.Cells[k][x, z] != InteriorPlan.Free);
        s += (Edge(r.X0 - 1) ? 3 : 0) + (Edge(r.X1 + 1) ? 3 : 0);
        return s;
    }

    private static bool Touches(GridRect a, GridRect b) =>
        (a.X1 + 1 == b.X0 || b.X1 + 1 == a.X0) && a.Z0 <= b.Z1 && b.Z0 <= a.Z1 ||
        (a.Z1 + 1 == b.Z0 || b.Z1 + 1 == a.Z0) && a.X0 <= b.X1 && b.X0 <= a.X1;

    /// <summary>
    /// What no room took: a run of free columns in a band joins a neighbour of the same band and section when it is narrow (merged), else
    /// becomes storage (fuel when one cell wide on the lowest deck), else void.
    /// </summary>
    private static void Leftovers(InteriorPlan p, InteriorData data, int k, List<Band> bands, InteriorRng rng)
    {
        foreach (var band in bands.Where(b => b.Kind != "full").Concat(bands.Where(b => b.Kind == "full")))
        {
            var x = 0;
            while (x < p.W)
            {
                if (!ColumnFree(p, k, x, band))
                {
                    x++;
                    continue;
                }
                var a = x;
                while (x + 1 < p.W && ColumnFree(p, k, x + 1, band) && p.SectionOf(x + 1) == p.SectionOf(a))
                    x++;
                var b = x++;
                var run = new GridRect(a, band.Z0, b, band.Z1);
                PlanRoom? Mergeable(int at) =>
                    at >= 0 && at < p.W && p.Cells[k][at, band.Z0] is var id && id >= 0 && p.Rooms[id] is { System: false, Critical: false, Hall: -1 } n &&
                    n.Rect.Z0 == band.Z0 && n.Rect.Z1 == band.Z1 && n.Section == p.SectionOf(a) && n.Rect.W + run.W <= p.Class.Deck.Section ? n : null;
                // (a free room's wing in this band takes a narrow run beside it the same way: the outline stays an L or T)
                (PlanRoom R, int Wing)? WingBeside(int at) =>
                    at >= 0 && at < p.W && p.Cells[k][at, band.Z0] is var id && id >= 0 && p.Rooms[id] is { System: false, Critical: false, Hall: -1 } n &&
                    n.Wings.FindIndex(w => w.Z0 == band.Z0 && w.Z1 == band.Z1 && (w.X1 + 1 == a || w.X0 - 1 == b)) is var wi && wi >= 0 &&
                    n.Section == p.SectionOf(a) ? (n, wi) : null;
                if (run.W <= 2 && (WingBeside(a - 1) ?? WingBeside(b + 1)) is { } hit)
                {
                    var (wr, wi2) = hit;
                    var wg = wr.Wings[wi2];
                    wr.Wings[wi2] = new GridRect(Math.Min(wg.X0, a), band.Z0, Math.Max(wg.X1, b), band.Z1);
                    wr.Origin = "merged";
                    for (var c = a; c <= b; c++)
                        for (var z = band.Z0; z <= band.Z1; z++)
                            p.Cells[k][c, z] = wr.Id;
                    continue;
                }
                if (run.W <= (p.Sealed ? 3 : 2) && (Mergeable(a - 1) ?? Mergeable(b + 1)) is { } into)
                {
                    into.Rect = new GridRect(Math.Min(into.Rect.X0, a), band.Z0, Math.Max(into.Rect.X1, b), band.Z1);
                    into.Origin = "merged";
                    for (var c = a; c <= b; c++)
                        for (var z = band.Z0; z <= band.Z1; z++)
                            p.Cells[k][c, z] = into.Id;
                    continue;
                }
                var kind = band.Depth < 2 ? "dead" : run.W >= data.Rooms["spare"].MinW ? "spare" : k == 0 && run.W == 1 ? "fuel" : run.W >= 2 || k == 0 ? "storage" : "dead";
                if (kind is "spare" or "storage" && run.W >= 2 && p.Class.Fill.Count > 0) // (phase 8: a narrow run is a room of the mix too, when one fits; else storage as before)
                    Generate(p, data, k, run, band, rng, kind);
                else
                    Add(p, data.Rooms[kind], k, run, "leftover", band);
            }
        }
        // (anything else still free: void)
        for (var x = 0; x < p.W; x++)
            for (var z = 0; z < p.D; z++)
                if (p.Cells[k][x, z] == InteriorPlan.Free)
                    Add(p, data.Rooms["dead"], k, new GridRect(x, z, x, z), "leftover", new Band(z < p.SpineZ0 ? 0 : 1, "full", z, z));
    }

    /// <summary>
    /// A leftover run (phase 3, phase 8): a generated room from the class's weighted programme (<see cref="SizeClass.FillMix"/>), the type chosen by
    /// what fits (its minimum size; a type holding fuel stays on the lowest deck), its place (deck, row) and neighbours (wanted and avoided rooms
    /// beside it), how far its share of the generated rooms is under its weight's (and never over its cap), the crew's count (a mess or medical
    /// bay per so many quarters), with a seeded jitter. A run big enough for two (twice the type's want and minimum width) is split in two, each
    /// chosen again. Nothing fits: <paramref name="fallback"/> (spare space, or storage for a narrow run).
    /// </summary>
    private static void Generate(InteriorPlan p, InteriorData data, int k, GridRect run, Band band, InteriorRng rng, string fallback = "spare")
    {
        RoomType? pick = null;
        var best = double.MinValue;
        var mix = p.Class.FillMix.Count > 0 ? p.Class.FillMix : p.Class.Fill.Select(f => new FillEntry(f, 1, 1, "any", 0)).ToList();
        mix = mix.Where(f => p.Sealed ? f.Where != "deck" : f.Where != "chassis").GroupBy(f => f.Room).Select(g => g.First()).ToList();
        var weight = mix.Sum(f => f.Weight);
        var made = p.Rooms.Count(o => o.Origin == "generated");
        var quarters = p.Rooms.Count(o => o.Type == "quarters");
        foreach (var capped in new[] { true, false }) // (every type over its cap: the best of them, rather than spare space)
        {
            if (pick != null)
                break;
            foreach (var f in mix)
            {
                var t = data.Rooms[f.Room];
                if (run.W < t.MinW || run.D < t.MinD)
                    continue;
                var fuel = t.Modules != null && data.Sets[t.Modules].Items.Any(i => i.Part == "Fuel Tank");
                if (fuel && k != 0 || t.Critical && (band.Kind == "inner" || p.Sealed)) // (a critical room keeps one door, so never in front of an outer band whose rooms may need a way through it, nor in a sealed hull, whose rooms have no outside doors)
                    continue;
                var have = p.Rooms.Count(o => o.Type == f.Room && o.Origin == "generated");
                if (capped && made >= 4 && (have + 1.0) / (made + 1) > f.Cap)
                    continue;
                double s = (t.Deck, k == 0, k == p.Decks - 1) switch { (DeckWant.Lower, true, _) => 2, (DeckWant.Upper, _, true) => 2, (DeckWant.Any, _, _) => 0, _ => -4 };
                s += (t.Row, band.Kind) switch { (RowWant.Inner, "inner") => 2, (RowWant.Outer, "outer") => 2, (RowWant.Inner, "outer") => -2, (RowWant.Outer, "inner") => -2, _ => 0 };
                foreach (var o in p.Rooms.Where(o => o.Deck == k && !o.System && Touches(o.Rect, run)))
                {
                    if (t.Wants.Contains(o.Type) || data.Rooms[o.Type].Wants.Contains(t.Id))
                        s += 4;
                    if (t.Avoids.Contains(o.Type) || data.Rooms[o.Type].Avoids.Contains(t.Id))
                        s -= 6;
                }
                s += 20 * (f.Weight / weight - (double)have / Math.Max(1, made)); // (under its share: wanted more)
                if (f.PerQuarters > 0 && quarters / f.PerQuarters - p.Rooms.Count(o => o.Type == f.Room) is var short1 && short1 > 0)
                    s += 3 * Math.Min(short1, 2);
                s += 3 * rng.Next();
                if (s > best)
                {
                    best = s;
                    pick = t;
                }
        }
        }
        if (pick == null)
        {
            Add(p, data.Rooms[fallback], k, run, "leftover", band);
            return;
        }
        if (run.Area >= 2 * pick.Want && run.W >= 2 * Math.Max(pick.MinW, 2) && !p.Sealed) // (a sealed hull keeps its rooms whole: fewer walls, CONSTRUCTS.md's link limit)
        {
            var half = run.W / 2;
            Generate(p, data, k, new GridRect(run.X0, run.Z0, run.X0 + half - 1, run.Z1), band, rng, fallback);
            Generate(p, data, k, new GridRect(run.X0 + half, run.Z0, run.X1, run.Z1), band, rng, fallback);
            return;
        }
        Add(p, pick, k, run, "generated", band);
    }

    private static bool ColumnFree(InteriorPlan p, int k, int x, Band band)
    {
        for (var z = band.Z0; z <= band.Z1; z++)
            if (p.Cells[k][x, z] != InteriorPlan.Free)
                return false;
        return true;
    }

    /// <summary>The middle piece slot of a side <paramref name="len"/> cells long (-1: the door corner, for a one-cell side).</summary>
    private static int MidSlot(int len) => len >= 2 ? (len - 2) / 2 : -1;

    /// <summary>A door between two rooms through a shared edge where both have a middle piece (at least two cells in common), in one section.</summary>
    private static PlanDoor? Between(InteriorPlan p, PlanRoom a, PlanRoom b, string why, bool anySection = false)
    {
        if (a.Deck != b.Deck || a.Section != b.Section && !anySection)
            return null;
        var (ra, rb) = (a.Rect, b.Rect);
        Side side;
        int lo, hi, start;
        if (ra.Z1 + 1 == rb.Z0 || rb.Z1 + 1 == ra.Z0)
        {
            side = ra.Z1 + 1 == rb.Z0 ? Side.South : Side.North;
            (lo, hi, start) = (Math.Max(ra.X0, rb.X0), Math.Min(ra.X1, rb.X1), ra.X0);
        }
        else if (ra.X1 + 1 == rb.X0 || rb.X1 + 1 == ra.X0)
        {
            side = ra.X1 + 1 == rb.X0 ? Side.East : Side.West;
            (lo, hi, start) = (Math.Max(ra.Z0, rb.Z0), Math.Min(ra.Z1, rb.Z1), ra.Z0);
        }
        else
            return null;
        if (hi - lo + 1 < 2)
            return null;
        // (never through a hall's west wall: its stairs climb that column, and a doorway there opened onto a flight: phase 9's walk-through)
        if (a.Hall >= 0 && side == Side.West || b.Hall >= 0 && side == Side.East)
            return null;
        return new PlanDoor { Deck = a.Deck, Room = a.Id, Other = b.Id, Side = side, Slot = lo - start + (hi - lo - 1) / 2, Why = why };
    }

    private static void Doors(InteriorPlan p, InteriorData data, List<Band>[] bands)
    {
        foreach (var r in p.Rooms.Where(r => r.Type != "dead"))
        {
            var north = r.Rect.Z1 < p.SpineZ0;
            // on the corridor (or the lane): every inner room
            var spineSide = north ? Side.South : Side.North;
            var edge = north ? r.Rect.Z1 + 1 : r.Rect.Z0 - 1;
            if (edge >= 0 && edge < p.D && p.Cells[r.Deck][r.Rect.X0, edge] is InteriorPlan.Corridor or InteriorPlan.Lane)
                p.Doors.Add(new PlanDoor { Deck = r.Deck, Room = r.Id, Other = PlanDoor.Corridor, Side = spineSide, Slot = MidSlot(r.Rect.W), Why = r.Hall == r.Id && !r.Critical ? "open bay" : "corridor", Wide = r.Hall == r.Id && !r.Critical });
            // the hangar's ramp and outside doors on the lowest deck
            var hullSide = north ? Side.North : Side.South;
            var onHull = north ? r.Rect.Z0 == 0 : r.Rect.Z1 == p.D - 1;
            if (r.Deck == 0 && onHull && data.Rooms[r.Type].Exterior && r.Rect.W >= 2 && !p.Sealed)
            {
                var wide = r.Type == "hangar";
                p.Doors.Add(new PlanDoor { Deck = 0, Room = r.Id, Other = PlanDoor.Outside, Side = hullSide, Slot = MidSlot(r.Rect.W), Wide = wide, Why = wide ? "ramp" : "loading door" });
                p.Entries.Add(new PlanEntry(0, r.Rect.X0 + Math.Max(0, MidSlot(r.Rect.W)), north ? 0 : p.D - 1, wide ? "hangar ramp" : $"{r.Name} loading door"));
            }
        }
        // rooms that want each other and touch: a door between them (never into a critical room, never across a bulkhead)
        foreach (var a in p.Rooms)
            foreach (var b in p.Rooms)
                if (a.Id < b.Id && !a.Critical && !b.Critical && a.Shaft == null && b.Shaft == null && a.Type != "dead" && b.Type != "dead" &&
                    (data.Rooms[a.Type].Wants.Contains(b.Type) || data.Rooms[b.Type].Wants.Contains(a.Type)) && Between(p, a, b, "wanted") is { } d)
                    p.Doors.Add(d);
        // outer rooms with no way in: through a neighbour on the corridor, else outside on the lowest deck
        for (var pass = 0; pass < (p.Sealed ? 4 : 3); pass++)
        {
            var reached = Reached(p);
            foreach (var r in p.Rooms.Where(r => r.Type != "dead" && !reached.Contains(r.Id)))
            {
                // (from the second pass, through a bulkhead line too: a room with no other way in gets a door in the bulkhead)
                // (a hall, critical or not, has a way in on every storey already: a room behind it may open off it)
                var via = p.Rooms.Where(o => reached.Contains(o.Id) && (!o.Critical || o.Hall >= 0) && o.Shaft == null && o.Type != "dead")
                    .Select(o => Between(p, r, o, "way in")).OfType<PlanDoor>().FirstOrDefault() ??
                    (pass > 0 ? p.Rooms.Where(o => reached.Contains(o.Id) && (!o.Critical || o.Hall >= 0) && o.Shaft == null && o.Type != "dead")
                        .Select(o => Between(p, r, o, "way in through a bulkhead", anySection: true)).OfType<PlanDoor>().FirstOrDefault() : null) ??
                    // (a sealed hull has no outside: last, a door onto a shaft's landing)
                    // (stairs first; never through a lift shaft's west wall, where its cars hang: the builder puts a lift shaft's north
                    // and south doorways in their piece's east half, clear of the cars' column: phase 6's walk-through)
                    (p.Sealed && pass > 1 ? p.Rooms.Where(o => reached.Contains(o.Id) && o.Shaft != null).OrderBy(o => o.Shaft == "stairs" ? 0 : 1)
                        .Select(o => Between(p, r, o, "way in from a shaft's landing", anySection: true))
                        .OfType<PlanDoor>().FirstOrDefault(d => p.Rooms[d.Other].Shaft != "elevator" || d.Side != Side.East) : null);
                if (via != null)
                {
                    p.Doors.Add(via);
                    continue;
                }
                var north = r.Rect.Z1 < p.SpineZ0;
                if (r.Deck == 0 && (north ? r.Rect.Z0 == 0 : r.Rect.Z1 == p.D - 1) && pass == 2 && !p.Sealed)
                {
                    p.Doors.Add(new PlanDoor { Deck = 0, Room = r.Id, Other = PlanDoor.Outside, Side = north ? Side.North : Side.South, Slot = MidSlot(r.Rect.W), Why = "way in from outside" });
                    p.Entries.Add(new PlanEntry(0, r.Rect.X0, north ? 0 : p.D - 1, $"{r.Name} outside door"));
                }
            }
        }
        // (phase 10) a generated room still shut in (behind a critical room and a hall's stair wall, say) becomes void rather than a room nobody reaches
        var still = Reached(p);
        foreach (var r in p.Rooms.Where(r => r.Type != "dead" && r.Origin is "generated" or "leftover" && r.Shaft == null && !still.Contains(r.Id)))
        {
            p.Notes.Add($"the {r.Name} ({r.Rect} deck {r.Deck}) has no way in: void");
            r.Type = "dead";
            r.Name = data.Rooms["dead"].Name;
            r.Set = null;
        }
    }

    private static void Bulkheads(InteriorPlan p, int laneRow)
    {
        for (var k = 0; k < p.Decks; k++)
            for (var x = p.Class.Deck.Section; x < p.W; x += p.Class.Deck.Section)
                p.Bulkheads.Add(new PlanBulkhead(k, x, p.SpineZ0, k == 0 && laneRow >= 0 ? laneRow : p.SpineZ1));
        p.Entries.Insert(0, new PlanEntry(0, 0, p.SpineZ0, "spine end"));
        p.Entries.Insert(1, new PlanEntry(0, p.W - 1, p.SpineZ0, "spine end"));
    }

    /// <summary>Rooms a walker reaches from outside: the lowest deck's spine ends are open, doors join rooms, stairs and the freight lift join decks.</summary>
    private static HashSet<int> Reached(InteriorPlan p)
    {
        var seen = new HashSet<int>();
        var corridor = new bool[p.Decks];
        var q = new Queue<int>(); // (room ids; -1 - deck for a deck's corridor)
        corridor[0] = true;
        q.Enqueue(-1);
        foreach (var d in p.Doors.Where(d => d.Other == PlanDoor.Outside))
            if (seen.Add(d.Room))
                q.Enqueue(d.Room);
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            if (n < 0)
            {
                var k = -1 - n;
                foreach (var d in p.Doors.Where(d => d.Deck == k && d.Other == PlanDoor.Corridor))
                    if (seen.Add(d.Room))
                        q.Enqueue(d.Room);
                continue;
            }
            var r = p.Rooms[n];
            foreach (var d in p.Doors)
            {
                var other = d.Room == n ? d.Other : d.Other == n ? d.Room : int.MinValue;
                if (other == PlanDoor.Corridor && !corridor[r.Deck])
                {
                    corridor[r.Deck] = true;
                    q.Enqueue(-1 - r.Deck);
                }
                else if (other >= 0 && seen.Add(other))
                    q.Enqueue(other);
            }
            if (r.Hall >= 0) // (a hall's storeys: its own stairs join them)
                foreach (var o in p.Rooms.Where(o => o.Hall == r.Hall))
                    if (seen.Add(o.Id))
                        q.Enqueue(o.Id);
            if (r.Shaft != null) // (stairs, and the freight lift: the builder runs one between two decks)
                foreach (var o in p.Rooms.Where(o => o.Shaft == r.Shaft && o.Rect == r.Rect && Math.Abs(o.Deck - r.Deck) == 1))
                    if (seen.Add(o.Id))
                        q.Enqueue(o.Id);
        }
        return seen;
    }

    private static void Reach(InteriorPlan p)
    {
        var reached = Reached(p);
        foreach (var r in p.Rooms.Where(r => r.Type != "dead" && !reached.Contains(r.Id)))
            p.Refused.Add($"the {r.Name} ({r.Rect}, deck {r.Deck}) can't be reached");
        foreach (var r in p.Rooms.Where(r => r.Critical))
            if (p.DoorsOf(r.Id).Count() != 1)
                p.Notes.Add($"the {r.Name} has {p.DoorsOf(r.Id).Count()} doors (a critical room wants one)");
    }

    /// <summary>
    /// The share of room cells (every deck; shafts, void and spare left out) whose mirror cell across the hull's centreline (z to D-1-z) holds a
    /// room of the same type (phase 9: a plan that repeats port and starboard scores high).
    /// </summary>
    public static double Symmetry(InteriorPlan p)
    {
        int all = 0, same = 0;
        for (var k = 0; k < p.Decks; k++)
            for (var x = 0; x < p.W; x++)
                for (var z = 0; z < p.D; z++)
                {
                    if (p.Cells[k][x, z] is not (>= 0 and var id) || p.Rooms[id] is { System: true } or { Type: "dead" or "spare" })
                        continue;
                    all++;
                    var m = p.Cells[k][x, p.D - 1 - z];
                    if (m >= 0 && TypeOf(p.Rooms[m]) == TypeOf(p.Rooms[id]))
                        same++;
                }
        return all == 0 ? 0 : same / (double)all;
        string TypeOf(PlanRoom r) => r.Hall >= 0 ? p.Rooms[r.Hall].Type : r.Type;
    }

    /// <summary>The plan's check lines: rooms, minimum sizes, reachability, what was refused.</summary>
    public static List<string> Check(InteriorPlan p, InteriorData data, out int failures)
    {
        var lines = new List<string>();
        failures = p.Refused.Count;
        var programme = p.Rooms.Where(r => r.Origin is "programme" or "merged").ToList();
        var small = programme.Where(r => r.Rect.W < data.Rooms[r.Type].MinW || r.Rect.D < data.Rooms[r.Type].MinD).ToList();
        failures += small.Count;
        lines.Add($"rooms: {programme.Count} of {p.Class.Programme.Count} asked for placed, {p.Rooms.Count(r => r.Shaft != null) / Math.Max(1, p.Decks)} shafts through {p.Decks} deck(s), {p.Rooms.Count(r => r.Origin == "leftover")} leftovers ({string.Join(", ", p.Rooms.Where(r => r.Origin == "leftover").GroupBy(r => r.Type).Select(g => $"{g.Count()} {g.Key}"))}), {p.Rooms.Count(r => r.Origin == "merged")} merged");
        lines.Add(small.Count == 0 ? "minimum sizes: all met" : $"minimum sizes: FAIL {string.Join(", ", small.Select(r => r.ToString()))}");
        var reached = Reached(p);
        var open = p.Rooms.Where(r => r.Type != "dead").ToList();
        lines.Add($"reachable: {open.Count(r => reached.Contains(r.Id))} of {open.Count} rooms from outside{(open.All(r => reached.Contains(r.Id)) ? "" : " FAIL")}");
        lines.Add($"doors {p.Doors.Count}, bulkheads {p.Bulkheads.Count}, entry points {p.Entries.Count}, critical rooms {string.Join(", ", p.Rooms.Where(r => r.Critical).Select(r => $"{r.Name} ({p.DoorsOf(r.Id).Count()} door)"))}");
        var halls = p.Rooms.Where(r => r.Hall >= 0).Sum(r => r.Area);
        var smallRooms = p.Rooms.Where(r => r.Hall < 0 && !r.System && r.Type is not ("dead" or "spare")).Sum(r => r.Area);
        var share = smallRooms / (double)Math.Max(1, smallRooms + halls);
        lines.Add($"floor: halls {halls} cells a storey summed, small rooms {smallRooms} ({share:P0} of the room floor; target at most {p.Class.SmallShare:P0}{(share > p.Class.SmallShare + 1e-9 ? ", OVER" : "")})");
        foreach (var h in p.Rooms.Where(r => r.Hall == r.Id))
            lines.Add($"hall: {h.Name} {h.Rect.W * 4}x{h.Rect.D * 4} m, {h.Storeys} storeys ({h.Storeys * 3} m) from deck {h.Deck}");
        lines.Add($"symmetry: {Symmetry(p):P0} of room cells have a room of the same kind across the centreline");
        foreach (var r in p.Refused)
            lines.Add($"FAIL {r}");
        foreach (var n in p.Notes)
            lines.Add($"note: {n}");
        return lines;
    }
}
