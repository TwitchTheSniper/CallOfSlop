using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Crafting Terminal: Constructs/Parts/Crafting (docs/sandbox/constructs/production.md, "Crafting terminal"). A desk with a
/// screen at standing height at its front left, a sorter cabinet with a chute hopper behind the desk, and along its right a low
/// tray that parts ride into from the back and are taken from at the front. The tray is a Piece Rack's hold
/// (<c>storage.pieces</c> with <c>crafting</c>: ConstructCrafting.cs), so it packs by volume and F takes the part looked at.
/// The look: shape says function (desk, sorter, tray), flow marked (pale sill and chevrons at Piece in, a hopper at Chute in),
/// three tones (steel blue body, dark frame, pale work top) and amber only on what is touched (the tray's rails, the keys).
/// </summary>
public sealed class CraftingTerminalParts : IPartGenerator
{
    public string Name => "crafting_terminal_parts";

    private const string Out = "Constructs/Parts/Crafting";
    private const string Body = "#35556b", Dark = "#2c2e30", Amber = "#d9a21c", Pale = "#e8e2d0", Slat = "#7d8388", Screen = "#10161c", Key = "#c9a21c";

    private static Part Terminal()
    {
        var c = Small;
        const int nx = 8, ny = 7, nz = 10; // 2.0 m wide, 1.75 m high, 2.5 m deep
        var p = new Part("Crafting Terminal", c, "Extrusion", "Lists what its linked machines and stores can make or hold. Order a part on its screen: it is fetched or made and arrives in the tray. Needs power.",
            role: "storage", tool: "welder", color: Body, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), z0 = c.Lo(), z1 = c.Hi(nz);
        var xm = x0 + 0.92; // the desk and sorter to its left, the tray to its right
        // ---- The desk, at the front left: a work top at 1 m, the screen's housing standing on its back.
        double dz0 = z1 - 0.55, dz1 = z1 - 0.03;
        p.Add(Box([x0 + 0.02, y0, dz0], [xm - 0.02, y0 + 1.0, dz1]));
        p.Add(Box([x0 + 0.04, y0 + 0.99, dz0 + 0.01], [xm - 0.04, y0 + 1.66, dz0 + 0.16]));
        p.Add(Box([x0 + 0.06, y0 + 1.0, dz0 + 0.22], [xm - 0.06, y0 + 1.012, dz1 - 0.03], Pale, detail: true)); // the work top
        p.Add(Box([x0 + 0.07, y0 + 1.06, dz0 + 0.154], [xm - 0.07, y0 + 1.6, dz0 + 0.166], Screen, detail: true)); // what its panel stands on
        for (var k = 0; k < 3; k++) // the keys: what a hand goes to
            p.Add(Box([x0 + 0.14 + k * 0.22, y0 + 1.012, dz1 - 0.2], [x0 + 0.3 + k * 0.22, y0 + 1.03, dz1 - 0.08], Key, detail: true));
        p.Add(Box([x0 + 0.02, y0 + 0.08, dz1 - 0.008], [xm - 0.02, y0 + 0.2, dz1 + 0.008], Dark, detail: true)); // a kick plate
        // ---- The sorter, behind the desk: a low cabinet with the chute's hopper on top and a slide into the tray.
        p.Add(Box([x0 + 0.05, y0, z0 + 0.05], [xm - 0.05, y0 + 0.7, dz0 - 0.01], Dark));
        p.Add(Box([x0 + 0.2, y0 + 0.695, z0 + 0.3], [xm - 0.2, y0 + 0.95, z0 + 0.82])); // the hopper
        p.Add(Box([x0 + 0.26, y0 + 0.945, z0 + 0.36], [xm - 0.26, y0 + 0.965, z0 + 0.76], Pale, detail: true)); // its mouth, marked: the way in
        p.Add(Box([xm - 0.055, y0 + 0.3, z0 + 0.4], [xm + 0.1, y0 + 0.62, z0 + 0.75], Slat, detail: true)); // the slide
        // ---- The tray, along the right: runners, slats, low amber rails; parts ride in at the back and are taken at the front.
        const double runner = 0.12, high = 0.12, post = 0.07, rail = 0.06, top = 0.62;
        p.Add(Box([xm, y0, z0], [xm + runner, y0 + high, z1]));
        p.Add(Box([x1 - runner, y0, z0], [x1, y0 + high, z1]));
        for (var k = 0; k <= 2; k++)
        {
            var z = z0 + 0.06 + (z1 - z0 - 0.18) * k / 2;
            p.Add(Box([xm + runner - 0.005, y0 + 0.01, z], [x1 - runner + 0.005, y0 + 0.11, z + 0.12], Dark));
            p.Add(Box([xm + 0.02, y0 + high - 0.005, z], [xm + 0.02 + post, y0 + top - rail + 0.005, z + post]));
            p.Add(Box([x1 - 0.02 - post, y0 + high - 0.005, z], [x1 - 0.02, y0 + top - rail + 0.005, z + post]));
        }
        for (var k = 1; k < 4; k++)
        {
            var x = xm + runner + (x1 - xm - 2 * runner) * k / 4;
            p.Add(Box([x - 0.03, y0 + 0.105, z0 + 0.04], [x + 0.03, y0 + 0.13, z1 - 0.04], Slat, detail: true));
        }
        p.Add(Box([xm + 0.01, y0 + top - rail, z0 + 0.02], [xm + 0.03 + post, y0 + top, z1 - 0.02], Amber, detail: true));
        p.Add(Box([x1 - 0.03 - post, y0 + top - rail, z0 + 0.02], [x1 - 0.01, y0 + top, z1 - 0.02], Amber, detail: true));
        // The way in: a sill at the back with a pale mark, and two pale chevrons on the first slats' line pointing on.
        var mid = (xm + x1) / 2;
        p.Add(Box([xm + runner - 0.005, y0 + 0.005, z0], [x1 - runner + 0.005, y0 + 0.1, z0 + 0.05], Dark));
        p.Add(Box([mid - 0.2, y0 + 0.02, z0 - 0.004], [mid + 0.2, y0 + 0.08, z0 + 0.01], Pale, detail: true));
        // The way out is a hand's: a pale lip across the front.
        p.Add(Box([xm + runner + 0.01, y0 + 0.02, z1 - 0.045], [x1 - runner - 0.01, y0 + 0.125, z1 - 0.005], Pale, detail: true));
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        p.Stats(180, 500, 3, seconds: 5.0);
        p.Set(("power", 80), ("onBreak", "disable"));
        p.Set(("panels", Py.L(Kw(("source", "craft"), ("at", Py.L(Py.Round((x0 + xm) / 2, 3), Py.Round(y0 + 1.33, 3), Py.Round(dz0 + 0.169, 3))), ("size", Py.L(0.74, 0.5)), ("face", "+z"), ("up", "+y"), ("rows", 8), ("range", 12)))));
        const double section = 0.8, length = 2.3;
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, section)), ("length", length), ("count", 400), ("crafting", true),
            ("hold", Py.L(Py.L(Py.Round(xm + 0.1, 3), Py.Round(y0 + 0.133, 3), Py.Round(z0 + 0.074, 3)), Py.L(Py.Round(x1 - 0.1, 3), Py.Round(y0 + top - 0.01, 3), Py.Round(z1 - 0.06, 3)))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round(mid, 3), Py.Round(y0 + 0.05, 3), z0)), ("face", "-z"), ("opening", Py.L(section, section))),
            Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(Py.Round((x0 + xm) / 2, 3), Py.Round(y0 + 0.95, 3), Py.Round(z0 + 0.56, 3))), ("face", "+y")))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Terminal())]);
}
