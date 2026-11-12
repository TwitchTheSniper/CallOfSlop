namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Brutalist pieces (docs/sandbox/constructs/buildings.md, "The Ministry of Slop: Brutalism"): what turns a concrete room
/// into a building that reads at 50 m. Fins and stepped buttresses give a front its rhythm and its weight, a fascia round the
/// roof slab gives it a heavy lid, a canopy marks the way in, and slot and ribbon walls keep the windows small, deep and
/// high, as a cold place wants them. All plain concrete on the building module, so they go on the walls' and slabs' own
/// sockets: a fin, a buttress and a canopy by their "back" on a wall's face socket, a fascia by its back on a slab's edge.
/// </summary>
public sealed partial class BuildingParts
{
    private const string BrutDark = "#7f7c75";

    /// <summary>A vertical fin, 0.25 m thick and 0.75 m deep, a storey tall: stands out from a wall's face (its back is +z).</summary>
    private static BPart Fin()
    {
        var p = MatPart("Concrete Fin 2.75 m", "concrete", SectionFrame, "A fin standing out of a wall: shade, shelter and rhythm.");
        p.CBox(0, 0, 0, 1, WallH, 3);
        foreach (var y in Thirds(WallH))
            p.CSock($"back{y}", (0, y, 2), "+z", must: true);
        p.CSock("b", (0, 0, 1), "-y", must: true);
        p.CSock("t", (0, WallH - 1, 1), "+y", must: true);
        p.CSock("front", (0, WallH / 2, 0), "-z", must: true);
        return MatStats(p, "concrete", 0.75 * WallH * C, secondsScale: 0.6);
    }

    /// <summary>A stepped buttress: 1.25 m deep at the foot, 0.75 m at the waist, 0.25 m at the head (its back is +z).</summary>
    private static BPart Buttress()
    {
        var p = MatPart("Concrete Buttress 2.75 m", "concrete", SectionFrame, "A stepped pier against a wall: the weight shows.");
        p.CBox(0, 0, 0, 1, 4, 5);
        p.CBox(0, 4, 2, 1, 8, 5);
        p.CBox(0, 8, 4, 1, WallH, 5);
        foreach (var y in Thirds(WallH))
            p.CSock($"back{y}", (0, y, 4), "+z", must: true);
        p.CSock("b", (0, 0, 2), "-y", must: true);
        p.CSock("t", (0, WallH - 1, 4), "+y", must: true);
        return MatStats(p, "concrete", (1.25 + 0.75 + 0.25) * 1.0, secondsScale: 0.7);
    }

    /// <summary>A canopy over a door: a 2 by 1.25 m slab with an upstand along its front, hung by its back edge (+z) on a wall's
    /// top face sockets; a lamp goes under it ("lamp") and a sign on its front ("sign").</summary>
    private static BPart Canopy()
    {
        var p = MatPart("Concrete Canopy 2 m", "concrete", SectionFrame, "A slab over a doorway: keeps the snow off whoever is knocking.");
        p.CBox(0, 0, 1, 8, 1, 5); // the slab
        p.CBox(0, 0, 0, 8, 2, 1); // the upstand along the front
        p.FBox(0, 0.5, -0.015, 2.0, 0.56, 0, BrutDark, detail: true); // (a drip line under the upstand's top)
        foreach (var x in new[] { 0, 7 })
        {
            p.CSock($"back{x}", (x, 0, 4), "+z", must: true);
            p.CSock($"t{x}", (x, 0, 3), "+y", must: true);
        }
        p.CSock("lamp", (3, 0, 2), "-y", must: true);
        p.CSock("sign", (3, 1, 0), "-z", must: true);
        p.CSock("l", (0, 0, 2), "-x", must: true);
        p.CSock("r", (7, 0, 2), "+x", must: true);
        return MatStats(p, "concrete", 2.0 * 1.25 + 2.0 * 0.5, secondsScale: 0.7);
    }

    /// <summary>The roof's heavy edge: a band 0.75 m tall and 0.5 m deep on a roof slab's edge socket, from the slab's underside
    /// up, so the lid overhangs the wall and stands up round the roof as its parapet. Its back is +z. The wall's top row under it
    /// stays free for a canopy, a lamp or a sign.</summary>
    private static BPart Fascia(int n)
    {
        var p = MatPart($"Concrete Fascia {F(n * C)} m", "concrete", SectionFrame, "A deep band round a flat roof's edge: the lid.");
        p.CBox(0, 0, 0, n, 3, 2);
        p.FBox(0, 0.06, -0.015, n * C, 0.12, 0, BrutDark, detail: true); // (the drip groove)
        foreach (var x in Mods(n))
        {
            p.CSock($"back{x}", (x, 0, 1), "+z", must: true);
            p.CSock($"t{x}", (x, 2, 0), "+y", must: true);
            p.CSock($"f{x}", (x, 1, 0), "-z", must: true);
        }
        p.CSock("l", (0, 1, 0), "-x", must: true);
        p.CSock("r", (n - 1, 1, 0), "+x", must: true);
        return MatStats(p, "concrete", n * C * 0.75 * 2, secondsScale: 0.5 + 0.3 * n / 16);
    }

    /// <summary>Where two fascias meet at a roof's corner: on the end ("+x") of one, the other's end on its "+z".</summary>
    private static BPart FasciaCorner()
    {
        var p = MatPart("Concrete Fascia Corner", "concrete", SectionFrame, "Closes the lid at a roof's corner.");
        p.CBox(0, 0, 0, 2, 3, 2);
        p.CSock("+x", (1, 1, 0), "+x", must: true);
        p.CSock("+z", (0, 1, 1), "+z", must: true);
        p.CSock("t", (0, 2, 0), "+y", must: true);
        return MatStats(p, "concrete", 0.5 * 0.75 * 2, secondsScale: 0.4);
    }

    /// <summary>Two steps up to a pad's top, 2 m wide: on the pad's edge socket by its back (+z). A pad is 0.5 m of plinth.</summary>
    private static BPart EntranceSteps()
    {
        var p = MatPart("Concrete Entrance Steps 2 m", "concrete", SectionStairs, "Two steps up to a foundation's top.");
        p.CBox(0, 0, 0, 8, 1, 4);
        p.CBox(0, 1, 2, 8, 2, 4);
        foreach (var x in new[] { 0, 7 })
            p.CSock($"back{x}", (x, 1, 3), "+z", must: true);
        p.CSock("l", (0, 0, 2), "-x", must: true);
        p.CSock("r", (7, 0, 2), "+x", must: true);
        p.Extra["walk"] = "floor";
        return MatStats(p, "concrete", 2.0 * 1.0, secondsScale: 0.6);
    }

    /// <summary>A wall of tall slots, 0.25 m wide from 0.5 to 2.25 m: light and a view out, no way in.</summary>
    private static BPart SlotWall(int n)
    {
        double L = n * C, Hm = WallH * C, lo = 0.5, hi = 2.25;
        var p = MatPart($"Concrete Slot Wall {F(L)} m", "concrete", SectionOpen, $"{(n == 8 ? "Two" : "Four")} tall slots: light without a way in.");
        var rects = Rects((0, 0, L, lo), (0, hi, L, Hm));
        int[] slots = n == 8 ? [2, 5] : [2, 5, 10, 13];
        var x0 = 0.0;
        foreach (var s in slots)
        {
            rects.Add((x0, lo, s * C, hi));
            x0 = (s + 1) * C;
        }
        rects.Add((x0, lo, L, hi));
        foreach (var (a, b, c, d) in rects)
            p.FBox(a, b, 0, c, d, C);
        WallDetails(p, "concrete", n, WallH, rects);
        WallSockets(p, n, WallH);
        return MatStats(p, "concrete", L * Hm - slots.Length * 0.25 * (hi - lo), secondsScale: 0.6 + 0.4 * n / 16);
    }

    /// <summary>A wall with a ribbon of window high up (1.75 to 2.25 m), under a hood: daylight over the filing cabinets and
    /// nothing for the wind to get at.</summary>
    private static BPart RibbonWall()
    {
        const int n = 16;
        double L = n * C, Hm = WallH * C, lo = 1.75, hi = 2.25;
        var p = MatPart("Concrete Ribbon Wall 4 m", "concrete", SectionOpen, "A high strip of window under a hood.");
        var rects = Rects((0, 0, L, lo), (0, hi, L, Hm), (0, lo, C, hi), (7 * C, lo, 9 * C, hi), (L - C, lo, L, hi));
        foreach (var (a, b, c, d) in rects)
            p.FBox(a, b, 0, c, d, C);
        p.FBox(0.2, hi + 0.01, -0.12, L - 0.2, hi + 0.09, 0, BrutDark, detail: true); // (the hood)
        p.FBox(0.2, lo - 0.07, -0.05, L - 0.2, lo - 0.01, 0, BrutDark, detail: true); // (the sill)
        WallDetails(p, "concrete", n, WallH, rects);
        WallSockets(p, n, WallH);
        return MatStats(p, "concrete", L * Hm - 2 * 1.5 * (hi - lo));
    }

    private static IEnumerable<(string, BPart)> BrutalistParts()
    {
        const string folder = "95-Brutalist";
        yield return (folder, Fin());
        yield return (folder, Buttress());
        yield return (folder, Canopy());
        yield return (folder, Fascia(8));
        yield return (folder, Fascia(16));
        yield return (folder, FasciaCorner());
        yield return (folder, EntranceSteps());
        yield return (folder, SlotWall(8));
        yield return (folder, SlotWall(16));
        yield return (folder, RibbonWall());
    }
}
