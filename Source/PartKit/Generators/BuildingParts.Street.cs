namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// What the Ministry puts on its roofs (docs/sandbox/constructs/buildings.md, "The street"): the things that give each block
/// of the penal colony its own outline against the sky, one bold piece a building. The Tannoy Mast is the voice the inmates
/// hear all day; the Roof Sign Frame holds a slogan board where the whole street reads it; the Roof Water Tank stands on legs
/// over a roof; the Loading Hoist reaches out over a dock from the lid; the Air Raid Siren calls the guards out. Steel, on a
/// roof slab's top sockets or the lid's front, a handful of boxes each.
/// </summary>
public sealed partial class BuildingParts
{
    private const string StreetSteel = "#5b625f", StreetHorn = "#4a5450", StreetHazard = "#c29a2a", StreetTank = "#6c776d";

    /// <summary>A pole on a concrete foot with four horn loudspeakers at its head, one each way.</summary>
    private static BPart TannoyMast()
    {
        var p = new BPart("Tannoy Mast", SectionSite, "A pole of horn loudspeakers: the Ministry's voice, at all hours.", tool: "wrench", color: StreetSteel);
        p.CBox(0, 0, 0, 2, 1, 2, MinWeathered); // the foot
        p.FBox(0.19, 0.25, 0.19, 0.31, 3.9, 0.31); // the pole (tall: from the street it is the tallest thing on the Records Office)
        p.FBox(0.16, 3.9, 0.16, 0.34, 3.96, 0.34, detail: true); // (its cap)
        p.FBox(-0.1, 3.3, 0.2, 0.6, 3.36, 0.3, detail: true); // (the cross-arm the cables run along)
        // The horns: a throat out from the pole and a wide mouth, each way, a little apart in height so no two share a plane.
        foreach (var (dx, dz, y) in new[] { (0, -1, 3.6), (0, 1, 3.62), (-1, 0, 3.64), (1, 0, 3.66) })
        {
            double cx = 0.25 + dx * 0.05, cz = 0.25 + dz * 0.05;
            double tx = 0.25 + dx * 0.22, tz = 0.25 + dz * 0.22, mx = 0.25 + dx * 0.36, mz = 0.25 + dz * 0.36;
            double w = 0.05, m = 0.13;
            p.FBox(Math.Min(cx, tx) - (dx == 0 ? w : 0), y - w, Math.Min(cz, tz) - (dz == 0 ? w : 0), Math.Max(cx, tx) + (dx == 0 ? w : 0), y + w, Math.Max(cz, tz) + (dz == 0 ? w : 0), StreetHorn, detail: true);
            p.FBox(Math.Min(tx, mx) - (dx == 0 ? m : 0), y - m, Math.Min(tz, mz) - (dz == 0 ? m : 0), Math.Max(tx, mx) + (dx == 0 ? m : 0), y + m, Math.Max(tz, mz) + (dz == 0 ? m : 0), StreetHorn, detail: true);
        }
        p.CSock("b00", (0, 0, 0), "-y", must: true);
        p.CSock("b11", (1, 0, 1), "-y", must: true);
        return p.Stats(70, 220, 6, 3.0);
    }

    /// <summary>A frame for a slogan on a roof's edge: two posts and a dark backing 3 m wide, a hood with two lamp sockets under
    /// it, and the board on its front ("sign"). It stands by its middle foot ("b") on the lid's top, its front (-z) to the street.</summary>
    private static BPart RoofSignFrame()
    {
        var p = new BPart("Roof Sign Frame 3 m", SectionSite, "A frame for a slogan where the whole street reads it.", tool: "wrench", color: StreetSteel);
        p.FBox(0.0, 0.0, 0.1, 0.12, 1.72, 0.25); // the posts
        p.FBox(2.88, 0.0, 0.1, 3.0, 1.72, 0.25);
        p.FBox(1.44, 0.0, 0.1, 1.56, 0.7, 0.25); // the middle foot
        p.FBox(-0.05, 0.7, 0.05, 3.05, 1.6, 0.1, MinSlate); // the backing
        p.FBox(-0.06, 1.72, -0.25, 3.06, 1.78, 0.27); // the hood
        p.Sock("b", [1.5 - H, 0 - H, 0.175 - H], "-y");
        p.Sock("sign", [1.5 - H, 1.15 - H, 0.05 - H], "-z");
        p.Sock("lamp0", [0.5 - H, 1.72 - H, -0.15 - H], "-y");
        p.Sock("lamp1", [2.5 - H, 1.72 - H, -0.15 - H], "-y");
        return p.Stats(120, 300, 6, 4.0);
    }

    /// <summary>A steel water tank, 2 by 2 m and 1.5 m deep, on four legs 1.75 m high, banded, with a ladder up one side.</summary>
    private static BPart RoofWaterTank()
    {
        var p = new BPart("Roof Water Tank", SectionSite, "A tank on legs over a roof: the building's water, frozen till noon.", tool: "welder", color: StreetTank);
        foreach (var (x, z) in new[] { (0, 0), (7, 0), (0, 7), (7, 7) })
            p.CBox(x, 0, z, x + 1, 7, z + 1, StreetSteel); // the legs (high: the sky shows under it)
        p.CBox(0, 7, 0, 8, 13, 8); // the tank
        foreach (var y in new[] { 2.15, 2.75 }) // (its bands)
            p.FBox(-0.015, y, -0.015, 2.015, y + 0.06, 2.015, StreetSteel, detail: true);
        p.FBox(0.1, 3.25, 0.1, 1.9, 3.3, 1.9, StreetSteel, detail: true); // (the hatch's curb)
        foreach (var x in new[] { 0.7, 1.25 }) // the ladder's rails, up the front (-z)
            p.FBox(x, 0.0, -0.2, x + 0.04, 3.35, -0.16, StreetSteel, detail: true);
        foreach (var y in new[] { 0.6, 1.4, 2.2, 3.0 })
            p.FBox(0.74, y, -0.19, 1.25, y + 0.03, -0.17, StreetSteel, detail: true);
        foreach (var (x, z) in new[] { (0, 0), (7, 0), (0, 7), (7, 7) })
            p.CSock($"b{x}{z}", (x, 0, z), "-y", must: true);
        return p.Stats(900, 1200, 10, 6.0);
    }

    /// <summary>A hoist beam out over a dock: a plate on the lid's front (its back is +z), a yellow beam 3 m out under a tie, the trolley,
    /// the chain block and its hook.</summary>
    private static BPart LoadingHoist()
    {
        var p = new BPart("Loading Hoist", SectionSite, "A beam and a chain block out over the dock: crates up, crates down.", tool: "wrench", color: StreetHazard);
        p.FBox(0.0, 0.0, 0.2, 0.5, 0.75, 0.25, StreetSteel); // the plate
        p.FBox(0.15, 0.55, -2.8, 0.35, 0.75, 0.2); // the beam, well out past the lid
        p.FBox(0.13, 0.75, -0.6, 0.37, 1.35, 0.2); // the king post over the plate
        p.FBox(0.2, 1.3, -2.75, 0.3, 1.36, -0.6, StreetSteel, detail: true); // (the tie from its head to the beam's end)
        p.FBox(0.2, 0.75, -2.75, 0.3, 1.3, -2.69, StreetSteel, detail: true);
        p.FBox(0.1, 0.4, -2.5, 0.4, 0.55, -2.2, StreetSteel, detail: true); // the trolley
        p.FBox(0.23, -1.0, -2.38, 0.27, 0.4, -2.32, "#2c2e30", detail: true); // the chain
        p.FBox(0.15, -1.3, -2.45, 0.35, -1.0, -2.25, StreetHazard, detail: true); // the block
        p.FBox(0.22, -1.55, -2.37, 0.28, -1.3, -2.33, "#2c2e30", detail: true); // the hook's shank
        p.FBox(0.22, -1.58, -2.37, 0.36, -1.52, -2.33, "#2c2e30", detail: true); // and its bill
        p.Sock("back", [0.25 - H, 0.375 - H, 0.25 - H], "+z");
        return p.Stats(150, 400, 8, 4.0);
    }

    /// <summary>An air raid siren on a short mast: a louvred drum with a horn each way. The barracks' call to turn out.</summary>
    private static BPart AirRaidSiren()
    {
        var p = new BPart("Air Raid Siren", SectionSite, "A louvred drum on a mast: when it winds up, the guards turn out.", tool: "wrench", color: "#8a9196");
        p.CBox(0, 0, 0, 2, 1, 2, MinWeathered); // the foot
        p.FBox(0.19, 0.25, 0.19, 0.31, 3.0, 0.31, StreetSteel); // the mast, clear of the lid in profile
        p.FBox(-0.05, 3.0, -0.05, 0.55, 3.5, 0.55); // the drum
        p.FBox(-0.065, 3.1, -0.065, 0.565, 3.4, 0.565, "#5d6366", detail: true); // (its louvres)
        p.FBox(0.08, 3.12, -0.45, 0.42, 3.38, -0.05, detail: true); // the horns
        p.FBox(0.08, 3.13, 0.55, 0.42, 3.39, 0.95, detail: true);
        p.FBox(0.12, 3.5, 0.12, 0.38, 3.62, 0.38, StreetSteel, detail: true); // (the motor's cap)
        p.CSock("b00", (0, 0, 0), "-y", must: true);
        p.CSock("b11", (1, 0, 1), "-y", must: true);
        return p.Stats(110, 300, 6, 3.0);
    }

    private static IEnumerable<(string, BPart)> StreetParts()
    {
        const string folder = "96-Site";
        yield return (folder, TannoyMast());
        yield return (folder, RoofSignFrame());
        yield return (folder, RoofWaterTank());
        yield return (folder, LoadingHoist());
        yield return (folder, AirRaidSiren());
    }
}
