using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Site works (docs/sandbox/constructs/buildings.md "The site"): what concrete buildings stand on and what joins them on
/// frozen ground. Road slabs, heating mains on trestles, a portal over a road, a stack, and the furniture of a place run
/// by notice: gate piers, a flag, notice boards. (The penal colony's site blueprints that used them went with the colony,
/// 2026-11-17; the parts stay as generic concrete pieces.)
/// </summary>
public sealed partial class BuildingParts
{
    private const string SectionSite = "Site works";
    private const string SiteLagging = "#c9c4b4", SiteBand = "#4b5157", SiteRoad = "#8a877f", SiteJoint = "#5f5c56";

    /// <summary>A road slab: 0.25 m of concrete on the ground, 4 m wide and <paramref name="nz"/> cells deep, a tarred joint
    /// across it. A footing: slabs laid edge to edge make a street, and columns, posts and a flag's base stand on its top.</summary>
    private static BPart RoadSlab(int nz)
    {
        const int nx = 16;
        double W = nx * C, D = nz * C;
        var p = new BPart($"Concrete Road Slab 4x{F(D)} m", SectionSite, "A slab of road on levelled ground: a street, a square, a path.", role: "foundation", tool: "welder", material: "concrete", color: SiteRoad);
        p.CBox(0, 0, 0, nx, 1, nz);
        p.FBox(W / 2 - 0.02, C, 0.04, W / 2 + 0.02, C + 0.006, D - 0.04, SiteJoint, detail: true);
        if (nz == 16)
            p.FBox(0.04, C, D / 2 - 0.02, W - 0.04, C + 0.006, D / 2 + 0.02, SiteJoint, detail: true);
        foreach (var x in Mods(nx))
            foreach (var z in Mods(nz))
                p.CSock($"t{x}_{z}", (x, 0, z), "+y");
        foreach (var z in Mods(nz))
        {
            p.CSock($"l{z}", (0, 0, z), "-x");
            p.CSock($"r{z}", (nx - 1, 0, z), "+x");
        }
        foreach (var x in Mods(nx))
        {
            p.CSock($"f{x}", (x, 0, 0), "-z");
            p.CSock($"k{x}", (x, 0, nz - 1), "+z");
        }
        p.Extra["walk"] = "floor";
        return p.Stats(1800 * W * D * C, 200 + 500 * W * D * C, 25, nz == 16 ? 5.0 : 3.5);
    }

    // The heating mains: a flow and a return pipe, lagged, 0.5 m apart, their middles 1 m up.
    private const double MainW = 1.25, PipeR = 0.17, PipeA = 0.375, PipeB = 0.875, PipeY = 1.0;

    private static PyDict Cyl(string axis, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", axis));

    /// <summary>The two pipes along x from 0 to <paramref name="L"/> m at height <paramref name="y"/>: what is hit (hidden boxes)
    /// and what is seen (lagging, a clamp band at each end), and a socket on each pipe's two ends ("la", "lb", "ra", "rb").</summary>
    private static void Pipes(BPart p, double L, double y, bool bands = true)
    {
        foreach (var (z, n) in new[] { (PipeA, "a"), (PipeB, "b") })
        {
            p.FBox(0, y - PipeR, z - PipeR, L, y + PipeR, z + PipeR, hidden: true);
            p.FBox(0, y - PipeR, z - PipeR, L, y + PipeR, z + PipeR, SiteLagging, detail: true, shape: Cyl("+x"));
            foreach (var x in bands ? new[] { 0.06, L - 0.14 } : [])
                p.FBox(x, y - PipeR - 0.025, z - PipeR - 0.025, x + 0.08, y + PipeR + 0.025, z + PipeR + 0.025, SiteBand, detail: true, shape: Cyl("+x"));
            p.Sock($"l{n}", [-H, y - H, z - H], "-x");
            p.Sock($"r{n}", [L - H, y - H, z - H], "+x");
        }
    }

    /// <summary>A length of the mains on its concrete trestle (a footing, two legs and a head under the pipes): stands on the
    /// ground, and the next length goes on its pipes' ends.</summary>
    private static BPart HeatingMain(int n)
    {
        var L = n * C;
        if (n < 8)
        {
            // A make-up length (1 m, 0.5 m): the pipes alone, on the end of a length that has a trestle.
            var s = new BPart($"Heating Main {F(L)} m", SectionSite, "A short make-up length of lagged heating pipe.", role: "frame", tool: "welder", material: "steel", color: SiteBand);
            Pipes(s, L, PipeY, bands: n >= 4);
            return s.Stats(120 * L, 150, 15, 2.0);
        }
        var p = new BPart($"Heating Main {F(L)} m", SectionSite, "Lagged heating pipes on a trestle, above the frozen ground.", role: "foundation", tool: "welder", material: "concrete", color: MinWeathered);
        var m = L / 2;
        p.FBox(m - 0.3, 0, 0, m + 0.3, 0.15, MainW); // the footing
        p.FBox(m - 0.125, 0.15, 0.1, m + 0.125, 0.63, 0.35);
        p.FBox(m - 0.125, 0.15, 0.9, m + 0.125, 0.63, 1.15);
        p.FBox(m - 0.15, 0.63, 0, m + 0.15, PipeY - PipeR, MainW); // the head
        Pipes(p, L, PipeY);
        return p.Stats(120 * L + 400, 300 + 80 * L, 20, 3.0 + 0.5 * L);
    }

    /// <summary>The pipes alone, for where the mains are carried over a road between two risers.</summary>
    private static BPart HeatingSpan()
    {
        const double L = 4.0;
        var p = new BPart("Heating Main Span 4 m", SectionSite, "Lagged heating pipes carried between two risers.", role: "frame", tool: "welder", material: "steel", color: SiteBand);
        Pipes(p, L, PipeR + 0.05);
        // A steel strap round both pipes at the middle (what is seen to hold them together).
        p.FBox(L / 2 - 0.05, 0.02, PipeA - PipeR - 0.04, L / 2 + 0.05, 2 * PipeR + 0.08, PipeB + PipeR + 0.04, SiteBand, detail: true);
        return p.Stats(120 * L, 200, 15, 4.0);
    }

    /// <summary>A riser: a concrete pylon that takes the mains in low on one face and gives them out high on the other, so two of
    /// them and a span carry the pipes over a road. Low sockets "la", "lb" (-x), high "ha", "hb" (+x), 3.25 m up.</summary>
    private static BPart HeatingRiser()
    {
        const double T = 0.5, Top = 3.6, High = 3.25;
        var p = new BPart("Heating Main Riser", SectionSite, "A pylon that lifts the heating mains over a road.", role: "foundation", tool: "welder", material: "concrete", color: MinWeathered);
        p.FBox(0, 0, 0, T, Top, MainW);
        p.FBox(-0.05, Top, -0.05, T + 0.05, Top + 0.1, MainW + 0.05, BrutDark, detail: true); // the cap
        foreach (var (z, n) in new[] { (PipeA, "a"), (PipeB, "b") })
        {
            p.FBox(-0.06, PipeY - PipeR - 0.03, z - PipeR - 0.03, 0, PipeY + PipeR + 0.03, z + PipeR + 0.03, SiteBand, detail: true, shape: Cyl("+x"));
            p.FBox(T, High - PipeR - 0.03, z - PipeR - 0.03, T + 0.06, High + PipeR + 0.03, z + PipeR + 0.03, SiteBand, detail: true, shape: Cyl("+x"));
            p.Sock($"l{n}", [-H, PipeY - H, z - H], "-x");
            p.Sock($"h{n}", [T - H, High - H, z - H], "+x");
        }
        return p.Stats(1800 * T * Top * MainW, 1500, 25, 6.0);
    }

    /// <summary>A valve chamber: a concrete box where the mains end or turn, a hand wheel on its lid. A notice goes on "f".</summary>
    private static BPart ValveChamber()
    {
        var p = new BPart("Heating Valve Chamber", SectionSite, "Where the heating mains turn or end: a valve under a concrete lid.", role: "foundation", tool: "welder", material: "concrete", color: MinWeathered);
        p.CBox(0, 0, 0, 6, 5, 6);
        p.FBox(-0.04, 1.25, -0.04, 1.54, 1.31, 1.54, BrutDark, detail: true); // the lid
        p.FBox(0.71, 1.31, 0.71, 0.79, 1.47, 0.79, SiteBand, detail: true); // the spindle
        p.FBox(0.53, 1.45, 0.53, 0.97, 1.5, 0.97, "#8a2f25", detail: true, shape: Cyl("+y", 16)); // the wheel
        p.Sock("f", [0.75 - H, 0.75 - H, -H], "-z");
        p.Sock("k", [0.75 - H, 0.75 - H, 1.5 - H], "+z");
        p.Sock("l", [-H, 0.75 - H, 0.75 - H], "-x");
        p.Sock("r", [1.5 - H, 0.75 - H, 0.75 - H], "+x");
        return p.Stats(1800 * 1.5 * 1.25 * 1.5 * 0.5, 1200, 25, 5.0);
    }

    /// <summary>A length of the boiler house's stack: 1 m square, 2.75 m tall; they stand on a pad and on each other.</summary>
    private static BPart Chimney()
    {
        var p = MatPart("Concrete Chimney 2.75 m", "concrete", SectionSite, "A length of a boiler's stack: stand them on each other.");
        p.CBox(0, 0, 0, 4, WallH, 4);
        p.FBox(-0.03, WallH * C - 0.2, -0.03, 1.03, WallH * C - 0.08, 1.03, BrutDark, detail: true); // (the pour line under each lift's top)
        foreach (var (x, z) in new[] { (0, 0), (3, 0), (0, 3), (3, 3) })
        {
            p.CSock($"b{x}{z}", (x, 0, z), "-y", must: true);
            p.CSock($"t{x}{z}", (x, WallH - 1, z), "+y", must: true);
        }
        p.CSock("f", (1, 5, 0), "-z", must: true);
        p.CSock("k", (1, 5, 3), "+z", must: true);
        return MatStats(p, "concrete", 4 * 1.0 * WallH * C, secondsScale: 0.8);
    }

    /// <summary>A gate pier: 1 m square and 3.25 m tall under a cap. A lamp, a name and a warning on its front (-z), the battery behind.</summary>
    private static BPart GatePier()
    {
        var p = new BPart("Concrete Gate Pier", SectionSite, "A pier beside the way in: a lamp, a name and a warning.", role: "foundation", tool: "welder", material: "concrete", color: MinWeathered);
        p.CBox(0, 0, 0, 4, 13, 4);
        p.FBox(-0.1, 3.25, -0.1, 1.1, 3.45, 1.1, BrutDark, detail: true); // the cap
        p.Sock("lamp", [0.5 - H, 2.75 - H, -H], "-z");
        p.Sock("sign", [0.5 - H, 2.1 - H, -H], "-z");
        p.Sock("sign2", [0.5 - H, 1.5 - H, -H], "-z");
        p.Sock("back", [0.5 - H, 2.1 - H, 1.0 - H], "+z");
        return p.Stats(1800 * 3.25, 2500, 25, 6.0);
    }

    /// <summary>The flag's footing (1 m square, 0.5 m tall; on a road slab's top sockets), a length of mast, and the top length
    /// with the Ministry's flag frozen stiff on it.</summary>
    private static BPart FlagBase()
    {
        var p = MatPart("Flagpole Base", "concrete", SectionSite, "The footing of a flagpole.");
        p.CBox(0, 0, 0, 4, 2, 4);
        foreach (var (x, z) in new[] { (0, 0), (3, 0), (0, 3), (3, 3) })
            p.CSock($"b{x}{z}", (x, 0, z), "-y", must: true);
        p.CSock("mast", (1, 1, 1), "+y", must: true);
        return MatStats(p, "concrete", 4.0, secondsScale: 0.5);
    }

    private static BPart FlagMast(bool top)
    {
        const string Paint = "#d9dcd6";
        var p = new BPart(top ? "Flagpole Top 3.75 m" : "Flagpole Mast 3.75 m", SectionSite, top ? "The top of a flagpole, and the flag." : "A length of flagpole.", role: "frame", tool: "wrench", material: "steel", color: Paint);
        p.CBox(0, 0, 0, 1, 15, 1);
        p.CSock("b", (0, 0, 0), "-y", must: true);
        p.CSock("t", (0, 14, 0), "+y", must: true);
        if (top)
        {
            p.FBox(0.25, 2.55, 0.105, 1.85, 3.55, 0.145, MinGreen, detail: true); // the flag, frozen stiff
            p.FBox(0.25, 2.95, 0.1, 1.85, 3.15, 0.15, "#d8d2bf", detail: true); // (its cream band)
            p.FBox(-0.04, 3.75, -0.04, 0.29, 3.83, 0.29, SiteBand, detail: true); // the truck
        }
        return p.Stats(90, 300, 10, 3.0);
    }

    /// <summary>A notice board: two concrete posts, a board between them under a hood; two notices on its front (-z).</summary>
    private static BPart NoticeBoard()
    {
        var p = MatPart("Concrete Notice Board 2 m", "concrete", SectionSite, "Two posts and a board under a hood: where orders are posted.");
        p.CBox(0, 0, 0, 1, 9, 1);
        p.CBox(7, 0, 0, 8, 9, 1);
        p.CBox(1, 4, 0, 7, 9, 1);
        p.CBox(0, 9, -1, 8, 10, 2); // the hood
        p.FBox(0.3, 1.05, -0.012, 1.7, 2.2, 0, MinGreen, detail: true); // (the board's painted field)
        p.CSock("b0", (0, 0, 0), "-y", must: true);
        p.CSock("b7", (7, 0, 0), "-y", must: true);
        p.Sock("n0", [0.75 - H, 1.9 - H, -H], "-z");
        p.Sock("n1", [1.25 - H, 1.35 - H, -H], "-z");
        return MatStats(p, "concrete", 2.0 * 1.5 + 1.5, secondsScale: 0.7);
    }

    /// <summary>The boiler house's boiler: a riveted drum 1.5 m across and 3 m long on two concrete saddles, its fire doors and
    /// its gauge on the front (-z). Furniture: it heats nothing but the story. Stands on a pad's top socket ("b").</summary>
    private static BPart LancashireBoiler()
    {
        const string Iron = "#3d3835", Plate = "#2e2b29";
        var p = new BPart("Lancashire Boiler", SectionSite, "An old drum boiler on its saddles. Older than the colony.", role: "frame", tool: "welder", material: "steel", color: Iron);
        p.CBox(0, 0, 0, 6, 7, 12, hidden: true);
        p.FBox(0.1, 0, 0.4, 1.4, 0.45, 0.8, MinWeathered); // the saddles
        p.FBox(0.1, 0, 2.2, 1.4, 0.45, 2.6, MinWeathered);
        p.FBox(0, 0.25, 0.05, 1.5, 1.75, 3.0, Iron, detail: true, shape: Cyl("+z", 20)); // the drum
        p.FBox(-0.03, 0.22, 0, 1.53, 1.78, 0.08, Plate, detail: true, shape: Cyl("+z", 20)); // the front plate
        foreach (var z in new[] { 1.0, 2.0 })
            p.FBox(-0.02, 0.23, z, 1.52, 1.77, z + 0.07, Plate, detail: true, shape: Cyl("+z", 20)); // (the riveted seams)
        foreach (var x in new[] { 0.3, 0.85 })
            p.FBox(x, 0.6, -0.04, x + 0.35, 0.95, 0, "#6e2a22", detail: true); // the fire doors
        p.FBox(0.62, 1.3, -0.05, 0.88, 1.56, 0, "#d8d2bf", detail: true, shape: Cyl("+z", 16)); // the gauge
        p.FBox(0.6, 1.75, 1.3, 0.9, 2.3, 1.6, Plate, detail: true, shape: Cyl("+y", 12)); // the steam dome's neck
        p.CSock("b", (5, 0, 5), "-y", must: true);
        p.CSock("t", (3, 6, 6), "+y", must: true);
        return p.Stats(6000, 4000, 30, 8.0);
    }

    /// <summary>A length of the outer fence: a precast concrete panel 4 m long and 2.5 m tall between two posts, three raised
    /// fields cast in its face each side, and over it two strands of wire on cranked arms. A footing: lengths go end to end.</summary>
    private static BPart FencePanel()
    {
        const double L = 4.0, Hm = 2.5;
        var p = new BPart("Concrete Fence Panel 4 m", SectionSite, "A precast panel of the outer fence, wire over it.", role: "foundation", tool: "welder", material: "concrete", color: MinConcrete);
        p.CBox(0, 0, 0, 16, 10, 1);
        foreach (var x in new[] { 0.0, L - 0.3 })
            p.FBox(x, 0, -0.06, x + 0.3, Hm + 0.1, C + 0.06, MinWeathered); // the posts
        for (var k = 0; k < 3; k++)
        {
            var x = 0.45 + k * 1.07;
            p.FBox(x, 0.45, -0.03, x + 0.95, 2.1, 0, BrutDark, detail: true); // (the cast fields, outside and in)
            p.FBox(x, 0.45, C, x + 0.95, 2.1, C + 0.03, BrutDark, detail: true);
        }
        foreach (var x in new[] { 0.11, L - 0.19 })
            p.FBox(x, Hm + 0.1, -0.2, x + 0.08, Hm + 0.5, C + 0.2, SiteBand, detail: true); // the arms
        foreach (var z in new[] { -0.19, C + 0.15 })
            p.FBox(0.11, Hm + 0.44, z, L - 0.11, Hm + 0.48, z + 0.04, "#2e2b29", detail: true); // the wire
        p.CSock("l", (0, 5, 0), "-x", must: true);
        p.CSock("r", (15, 5, 0), "+x", must: true);
        p.CSock("f", (8, 6, 0), "-z", must: true);
        p.CSock("k", (8, 6, 0), "+z", must: true);
        return p.Stats(1800 * L * Hm * 0.15, 3000, 30, 6.0);
    }

    /// <summary>A leaf of a sliding gate on its kerb: a steel frame of bars 4 m long and 2.4 m tall, in the Ministry's green.
    /// Two end to end close an 8 m way; parked along the inside of the fence they leave it open.</summary>
    private static BPart SlidingGate()
    {
        const double L = 4.0;
        var p = new BPart("Sliding Gate Leaf 4 m", SectionSite, "A leaf of a sliding gate on its kerb.", role: "foundation", tool: "welder", material: "steel", color: MinGreen);
        p.FBox(0, 0, 0, L, 0.12, C, MinWeathered); // the kerb and its rail
        p.FBox(0, 0.12, 0.06, L, 2.4, 0.19, hidden: true);
        foreach (var y in new[] { 0.2, 2.3 })
            p.FBox(0, y, 0.06, L, y + 0.1, 0.19, MinGreen, detail: true);
        for (var k = 0; k < 9; k++)
            p.FBox(k * 0.4875, 0.3, 0.09, k * 0.4875 + 0.1, 2.3, 0.16, MinGreen, detail: true);
        p.Sock("l", [-H, 0.06 - H, C / 2 - H], "-x");
        p.Sock("r", [L - H, 0.06 - H, C / 2 - H], "+x");
        return p.Stats(450, 1500, 25, 5.0);
    }

    private static IEnumerable<(string, BPart)> SiteParts()
    {
        const string folder = "96-Site";
        yield return (folder, RoadSlab(16));
        yield return (folder, RoadSlab(8));
        yield return (folder, HeatingMain(16));
        yield return (folder, HeatingMain(8));
        yield return (folder, HeatingMain(4));
        yield return (folder, HeatingMain(2));
        yield return (folder, FencePanel());
        yield return (folder, SlidingGate());
        yield return (folder, HeatingSpan());
        yield return (folder, HeatingRiser());
        yield return (folder, ValveChamber());
        yield return (folder, Chimney());
        yield return (folder, GatePier());
        yield return (folder, FlagBase());
        yield return (folder, FlagMast(false));
        yield return (folder, FlagMast(true));
        yield return (folder, NoticeBoard());
        yield return (folder, LancashireBoiler());
    }
}
