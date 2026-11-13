using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Piece Rack: Constructs/Parts/PieceRacks (docs/sandbox/constructs/production.md, "Conveyors"). A low steel cradle on two
/// runners, posts and an amber top rail down each side, that takes whole pieces off a conveyor at its Piece in (its -z end) and
/// holds them by kind (<c>storage.pieces</c>: the largest section and length and how many). What it holds is drawn lying on its
/// slats inside its posts, packed by volume into its hold (<c>storage.pieces.hold</c>, written here from the frame: ConstructRackPack.cs).
/// </summary>
public sealed class PieceRackParts : IPartGenerator
{
    public string Name => "piece_rack_parts";

    private const string Out = "Constructs/Parts/PieceRacks";
    private const string Frame = "#35556b", Bar = MachineLook.Base; // (the family's body colour takes paint; the rest is MachineLook's tones)
    // The hold: inside the posts and rails, 3 mm over the slats, past the sill and short of the far posts.
    private const double Side = 0.115, Bed = 0.133, EndIn = 0.074, EndOut = 0.02, Top = 0.01;

    /// <summary>A rack nx by nz small cells (0.25 m) on the ground, ny high, holding pieces up to section by length, count of them.</summary>
    private static Part Rack(string name, int nx, int ny, int nz, double section, double length, int count, string desc)
    {
        var c = Small;
        var p = new Part(name, c, "Extrusion", desc, role: "storage", tool: "welder", color: Frame, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        const double runner = 0.15, high = 0.12, post = 0.08, rail = 0.06;
        // Two runners along its length, and cross bars between them every 1.5 m (into the runners, under their tops).
        p.Add(Box([x0, y0, z0], [x0 + runner, y0 + high, z1]));
        p.Add(Box([x1 - runner, y0, z0], [x1, y0 + high, z1]));
        var bays = Math.Max(2, (int)Math.Round((z1 - z0) / 1.5));
        for (var k = 0; k <= bays; k++)
        {
            var z = z0 + 0.06 + (z1 - z0 - 0.18) * k / bays;
            p.Add(Box([x0 + runner - 0.005, y0 + 0.01, z], [x1 - runner + 0.005, y0 + 0.11, z + 0.12], Bar));
            // Posts each side, from inside the runner to inside the top rail.
            p.Add(Box([x0 + 0.02, y0 + high - 0.005, z], [x0 + 0.02 + post, y1 - rail + 0.005, z + post]));
            p.Add(Box([x1 - 0.02 - post, y0 + high - 0.005, z], [x1 - 0.02, y1 - rail + 0.005, z + post]));
        }
        // Slats the pieces lie on, along its length between the cross bars' tops and the runners' tops.
        for (var k = 1; k < 4; k++)
        {
            var x = x0 + runner + (x1 - x0 - 2 * runner) * k / 4;
            p.Add(MachineLook.D([x - 0.03, y0 + 0.105, z0 + 0.04], [x + 0.03, y0 + 0.13, z1 - 0.04], MachineLook.Steel, "steel"));
        }
        // The amber top rails: what bites a shin in a factory.
        p.Add(MachineLook.D([x0 + 0.01, y1 - rail, z0 + 0.02], [x0 + 0.03 + post, y1, z1 - 0.02], MachineLook.Accent));
        p.Add(MachineLook.D([x1 - 0.03 - post, y1 - rail, z0 + 0.02], [x1 - 0.01, y1, z1 - 0.02], MachineLook.Accent));
        // Fill marks along its length: a dark mark across each rail at every quarter, and a steel load plate on the first post each side.
        for (var k = 1; k < 4; k++)
        {
            var z = z0 + (z1 - z0) * k / 4;
            p.Add(MachineLook.D([x0 + 0.004, y1 - rail - 0.004, z - 0.02], [x0 + 0.032 + post, y1 + 0.004, z + 0.02], MachineLook.Ink));
            p.Add(MachineLook.D([x1 - 0.032 - post, y1 - rail - 0.004, z - 0.02], [x1 - 0.004, y1 + 0.004, z + 0.02], MachineLook.Ink));
        }
        var plateY = y0 + high + (y1 - rail - y0 - high) * 0.55;
        p.Add(MachineLook.D([x0 + 0.006, plateY, z0 + 0.045], [x0 + 0.022, plateY + 0.14, z0 + 0.155], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([x1 - 0.022, plateY, z0 + 0.045], [x1 - 0.006, plateY + 0.14, z0 + 0.155], MachineLook.Steel, "steel"));
        // The way in: an end sill at -z with a pale mark where the conveyor meets it.
        p.Add(Box([x0 + runner - 0.005, y0 + 0.005, z0], [x1 - runner + 0.005, y0 + 0.1, z0 + 0.05], Bar));
        // (the in side's white band across the sill, hazard paint over it: look.md "Machine look")
        p.Add(MachineLook.D([x0 + runner + 0.01, y0 + 0.02, z0 - 0.006], [x1 - runner - 0.01, y0 + 0.078, z0 + 0.008], MachineLook.In));
        MachineLook.Hazard(p, [x0 + runner + 0.01, y0 + 0.082, z0 - 0.006], [x1 - runner - 0.01, y0 + 0.098, z0 + 0.008]);
        MachineLook.Arrow(p, 1, y0 + high - 0.002, 1, 2, 1, z0 + 0.3, x0 + runner / 2, 0.3, runner * 0.46, MachineLook.In);
        MachineLook.Arrow(p, 1, y0 + high - 0.002, 1, 2, 1, z0 + 0.3, x1 - runner / 2, 0.3, runner * 0.46, MachineLook.In);
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        var wide = nx * c.Cell;
        var deep = nz * c.Cell;
        p.Stats(wide * deep * 30, 400 + 40 * wide * deep, 4);
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, section)), ("length", length), ("count", count),
            ("hold", Py.L(Py.L(Py.Round(x0 + Side, 3), Py.Round(y0 + Bed, 3), Py.Round(z0 + EndIn, 3)), Py.L(Py.Round(x1 - Side, 3), Py.Round(y1 - Top, 3), Py.Round(z1 - EndOut, 3)))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round((x0 + x1) / 2, 3), Py.Round(y0 + 0.05, 3), z0)), ("face", "-z"),
                ("opening", Py.L(section, section))))));
        return p;
    }

    /// <summary>
    /// The Lay-down Pad (docs/sandbox/design/arms-and-cranes.md 3.5, lifters phase 4): an open steel deck, no bars, where pieces
    /// too wide for a rack wait and where Loader Arms pick and put. A Piece Rack by its <c>storage.pieces</c> block (pieces up to
    /// section by up, length, packed by ConstructRackPack in its hold over the deck); four corner posts mark how high it stacks.
    /// </summary>
    private static Part Pad(string name, int nx, int ny, int nz, double section, double up, double length, int count, string desc)
    {
        var c = Small;
        var p = new Part(name, c, "Extrusion", desc, role: "storage", tool: "welder", color: Frame, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        const double deck = 0.1, post = 0.12, kerb = 0.1;
        var top = y0 + deck;
        // The deck: one plate, and a raised kerb round its edge in hazard paint (set 4 mm inside the deck's sides, its foot in the deck).
        p.Add(Box([x0, y0, z0], [x1, top, z1]));
        MachineLook.Hazard(p, [x0 + 0.004, top - 0.005, z0 + 0.004 + kerb], [x0 + 0.004 + kerb, top + 0.035, z1 - 0.004 - kerb]);
        MachineLook.Hazard(p, [x1 - 0.004 - kerb, top - 0.005, z0 + 0.004 + kerb], [x1 - 0.004, top + 0.035, z1 - 0.004 - kerb]);
        // (the -z end's kerb is the in side's white band, hazard over its top: look.md "Machine look"; the +z end's is hazard)
        p.Add(MachineLook.D([x0 + 0.004, top - 0.005, z0 + 0.004], [x1 - 0.004, top + 0.03, z0 + 0.004 + kerb], MachineLook.In));
        MachineLook.Hazard(p, [x0 + 0.01, top + 0.03, z0 + 0.008], [x1 - 0.01, top + 0.042, z0 + kerb]);
        MachineLook.Hazard(p, [x0 + 0.004, top - 0.005, z1 - 0.004 - kerb], [x1 - 0.004, top + 0.035, z1 - 0.004]);
        // Corner posts to the stacking height, each with an amber cap and a dark mark at every metre.
        foreach (var (px, pz) in new[] { (x0 + 0.02, z0 + 0.02), (x1 - 0.02 - post, z0 + 0.02), (x0 + 0.02, z1 - 0.02 - post), (x1 - 0.02 - post, z1 - 0.02 - post) })
        {
            p.Add(Box([px, top + 0.03, pz], [px + post, y1 - 0.1, pz + post], Bar));
            p.Add(MachineLook.D([px - 0.006, y1 - 0.1 - 0.005, pz - 0.006], [px + post + 0.006, y1, pz + post + 0.006], MachineLook.Accent));
            for (var m = 1.0; m < y1 - y0 - 0.3; m += 1.0)
                p.Add(MachineLook.D([px - 0.004, y0 + m - 0.02, pz - 0.004], [px + post + 0.004, y0 + m + 0.02, pz + post + 0.004], MachineLook.Ink));
        }
        // Deck seams every 2 m across: thin dark strips laid on it.
        for (var z = z0 + 2.0; z < z1 - 0.5; z += 2.0)
            p.Add(MachineLook.D([x0 + kerb + 0.02, top - 0.002, z - 0.015], [x1 - kerb - 0.02, top + 0.006, z + 0.015], MachineLook.Ink));
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        var wide = nx * c.Cell;
        var deep = nz * c.Cell;
        p.Stats(wide * deep * 40, 300 + 30 * wide * deep, 4);
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, up)), ("length", length), ("count", count),
            ("hold", Py.L(Py.L(Py.Round(x0 + kerb + 0.01, 3), Py.Round(top + 0.045, 3), Py.Round(z0 + kerb + 0.01, 3)), Py.L(Py.Round(x1 - kerb - 0.01, 3), Py.Round(y1 - 0.01, 3), Py.Round(z1 - kerb - 0.01, 3)))), ("open", true))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round((x0 + x1) / 2, 3), Py.Round(top + 0.05, 3), z0)), ("face", "-z"),
                ("opening", Py.L(Math.Min(section, 2.0), Math.Min(up, 2.0)))))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Rack("Piece Rack", 6, 4, 26, 1.0, 6.0, 2000, "Takes whole pieces off a conveyor at its Piece in and holds them: up to 1 x 1 m and 6 m long, as many as fill it. F takes the one you look at.")),
        ("", Rack("Long Piece Rack", 12, 6, 52, 2.0, 12.0, 6000, "Takes whole pieces off a conveyor: up to 2 x 2 m and 12 m long, as many as fill it. F takes the one you look at.")),
        ("", Pad("Lay-down Pad", 16, 8, 32, 3.6, 1.8, 7.6, 4000, "An open deck where pieces wait: up to 3.6 m across, 1.8 m high and 7.6 m long, stacked to 2 m. Loader Arms in reach pick from it and put on it; a conveyor may end at it.")),
        ("", Pad("Large Lay-down Pad", 32, 16, 64, 7.6, 3.8, 15.6, 16000, "An open deck for large pieces: up to 7.6 m across, 3.8 m high and 15.6 m long, stacked to 4 m. Loader Arms in reach pick from it and put on it.")),
    ]);
}
