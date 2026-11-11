using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Piece Rack: Constructs/Parts/PieceRacks (docs/sandbox/constructs/production.md, "Conveyors"). A low steel cradle on two
/// runners, posts and an amber top rail down each side, that takes whole pieces off a conveyor at its Piece in (its -z end) and
/// holds them by kind (<c>storage.pieces</c>: the largest section and length and how many). What it holds is drawn lying on its
/// slats inside its posts (ConstructConveyor.RackLayout: keep its margins in step with the runners, posts, slats and sill here).
/// </summary>
public sealed class PieceRackParts : IPartGenerator
{
    public string Name => "piece_rack_parts";

    private const string Out = "Constructs/Parts/PieceRacks";
    private const string Frame = "#35556b", Bar = "#2c2e30", Rail = "#d9a21c", Slat = "#7d8388", Mark = "#e8e2d0";

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
            p.Add(Box([x - 0.03, y0 + 0.105, z0 + 0.04], [x + 0.03, y0 + 0.13, z1 - 0.04], Slat, detail: true));
        }
        // The amber top rails: what bites a shin in a factory.
        p.Add(Box([x0 + 0.01, y1 - rail, z0 + 0.02], [x0 + 0.03 + post, y1, z1 - 0.02], Rail, detail: true));
        p.Add(Box([x1 - 0.03 - post, y1 - rail, z0 + 0.02], [x1 - 0.01, y1, z1 - 0.02], Rail, detail: true));
        // The way in: an end sill at -z with a pale mark where the conveyor meets it.
        p.Add(Box([x0 + runner - 0.005, y0 + 0.005, z0], [x1 - runner + 0.005, y0 + 0.1, z0 + 0.05], Bar));
        p.Add(Box([-0.2 + (x0 + x1) / 2, y0 + 0.02, z0 - 0.004], [0.2 + (x0 + x1) / 2, y0 + 0.08, z0 + 0.01], Mark, detail: true));
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        var wide = nx * c.Cell;
        var deep = nz * c.Cell;
        p.Stats(wide * deep * 30, 400 + 40 * wide * deep, 4);
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, section)), ("length", length), ("count", count))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round((x0 + x1) / 2, 3), Py.Round(y0 + 0.05, 3), z0)), ("face", "-z"),
                ("opening", Py.L(section, section))))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Rack("Piece Rack", 6, 4, 26, 1.0, 6.0, 40, "Takes whole pieces off a conveyor at its Piece in and holds them: up to 1 x 1 m, 6 m long, 40 of them. F takes one off.")),
        ("", Rack("Long Piece Rack", 12, 6, 52, 2.0, 12.0, 60, "Takes whole pieces off a conveyor: up to 2 x 2 m, 12 m long, 60 of them. F takes one off.")),
    ]);
}
