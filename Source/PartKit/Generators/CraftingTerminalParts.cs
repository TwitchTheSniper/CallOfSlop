using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Crafting Terminal: Constructs/Parts/Crafting (docs/sandbox/constructs/production.md, "Crafting terminal"). A desk with a
/// screen at standing height at its front left, a sorter cabinet with a chute hopper behind the desk, and along its right a low
/// tray that parts ride into from the back and are taken from at the front. The tray is a Piece Rack's hold
/// (<c>storage.pieces</c> with <c>crafting</c>: ConstructCrafting.cs), so it packs by volume and F takes the part looked at.
/// The look: shape says function (desk, sorter, tray), flow marked (pale sill and chevrons at Piece in, a hopper at Chute in),
/// the machine look's three tones (look.md "Machine look": a dark base, the body colour, the accent only on the rails and keys a hand works), white at the ways in, orange at the way out.
/// </summary>
public sealed class CraftingTerminalParts : IPartGenerator
{
    public string Name => "crafting_terminal_parts";

    private const string Out = "Constructs/Parts/Crafting";
    private const string Body = "#2f8f8a", Screen = "#10161c"; // (the family's body colour: it takes paint; the rest is MachineLook's tones)

    private static Part Terminal()
    {
        var c = Small;
        const int nx = 8, ny = 8, nz = 10; // 2.0 m wide, 2.0 m high, 2.5 m deep
        var p = new Part("Crafting Terminal", c, "Extrusion", "Lists what its linked machines and stores can make or hold. Order a part on its screen: it is fetched or made and arrives in the tray. Needs power.",
            role: "storage", tool: "welder", color: Body, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), z0 = c.Lo(), z1 = c.Hi(nz);
        var xm = x0 + 0.92; // the desk and sorter to its left, the tray to its right
        // ---- The desk, at the front left (the control face, on the aisle): a work top at 1.15 m, the screen's middle at 1.5 m.
        double dz0 = z1 - 0.55, dz1 = z1 - 0.03;
        p.Add(Box([x0 + 0.02, y0, dz0], [xm - 0.02, y0 + MachineLook.HandY, dz1]));
        p.Add(Box([x0 + 0.04, y0 + MachineLook.HandY - 0.01, dz0 + 0.01], [xm - 0.04, y0 + 1.86, dz0 + 0.16], MachineLook.Base)); // the screen's housing
        p.Add(MachineLook.D([x0 + 0.06, y0 + MachineLook.HandY, dz0 + 0.22], [xm - 0.06, y0 + MachineLook.HandY + 0.014, dz1 - 0.03], MachineLook.Steel, "steel")); // the work top
        p.Add(MachineLook.D([x0 + 0.07, y0 + 1.22, dz0 + 0.154], [xm - 0.07, y0 + 1.78, dz0 + 0.168], Screen)); // what its panel stands on
        for (var k = 0; k < 3; k++) // three keys and a stop: what a hand goes to
            p.Add(MachineLook.D([x0 + 0.12 + k * 0.17, y0 + MachineLook.HandY + 0.014, dz1 - 0.2], [x0 + 0.25 + k * 0.17, y0 + MachineLook.HandY + 0.034, dz1 - 0.08], MachineLook.Accent));
        p.Add(MachineLook.D([x0 + 0.66, y0 + MachineLook.HandY + 0.014, dz1 - 0.2], [x0 + 0.79, y0 + MachineLook.HandY + 0.034, dz1 - 0.08], MachineLook.Stop));
        p.Add(MachineLook.D([x0 + 0.02, y0, dz1 - 0.008], [xm - 0.02, y0 + 0.16, dz1 + 0.008], MachineLook.Base)); // a kick plate
        // ---- The sorter, behind the desk: a dark cabinet that takes things in (the chute's hopper on top, a second conveyor
        //      at its side) and slides them into the tray.
        p.Add(Box([x0 + 0.02, y0, z0 + 0.05], [xm - 0.05, y0 + 0.7, dz0 - 0.01], MachineLook.Base));
        p.Add(Box([x0 + 0.2, y0 + 0.695, z0 + 0.3], [xm - 0.2, y0 + 0.95, z0 + 0.82])); // the hopper
        p.Add(MachineLook.D([x0 + 0.24, y0 + 0.945, z0 + 0.34], [xm - 0.24, y0 + 0.965, z0 + 0.78], MachineLook.In)); // its mouth: the in side's white
        p.Add(MachineLook.D([xm - 0.055, y0 + 0.3, z0 + 0.4], [xm + 0.1, y0 + 0.62, z0 + 0.75], MachineLook.Steel, "steel")); // the slide
        // The second way in, on the sorter's side: a white band round the mouth and a hazard sill.
        p.Add(MachineLook.D([x0 + 0.006, y0 + 0.02, z0 + 0.6], [x0 + 0.022, y0 + 0.5, z0 + 1.2], MachineLook.In));
        MachineLook.Hazard(p, [x0 + 0.002, y0 + 0.0, z0 + 0.62], [x0 + 0.024, y0 + 0.018, z0 + 1.18]);
        // ---- The tray, along the right: runners, slats, accent rails; parts ride in at the back and are taken at the front.
        const double runner = 0.12, high = 0.12, post = 0.07, rail = 0.06, top = 0.62;
        p.Add(Box([xm, y0, z0], [xm + runner, y0 + high, z1], MachineLook.Base));
        p.Add(Box([x1 - runner, y0, z0], [x1, y0 + high, z1], MachineLook.Base));
        for (var k = 0; k <= 2; k++)
        {
            var z = z0 + 0.06 + (z1 - z0 - 0.18) * k / 2;
            p.Add(Box([xm + runner - 0.005, y0 + 0.01, z], [x1 - runner + 0.005, y0 + 0.11, z + 0.12], MachineLook.Base));
            p.Add(Box([xm + 0.02, y0 + high - 0.005, z], [xm + 0.02 + post, y0 + top - rail + 0.005, z + post]));
            p.Add(Box([x1 - 0.02 - post, y0 + high - 0.005, z], [x1 - 0.02, y0 + top - rail + 0.005, z + post]));
        }
        for (var k = 1; k < 4; k++)
        {
            var x = xm + runner + (x1 - xm - 2 * runner) * k / 4;
            p.Add(MachineLook.D([x - 0.03, y0 + 0.105, z0 + 0.04], [x + 0.03, y0 + 0.13, z1 - 0.04], MachineLook.Steel, "steel"));
        }
        p.Add(MachineLook.D([xm + 0.01, y0 + top - rail, z0 + 0.02], [xm + 0.03 + post, y0 + top, z1 - 0.02], MachineLook.Accent));
        p.Add(MachineLook.D([x1 - 0.03 - post, y0 + top - rail, z0 + 0.02], [x1 - 0.01, y0 + top, z1 - 0.02], MachineLook.Accent));
        // The way in at the back: a sill in hazard paint under a white band.
        var mid = (xm + x1) / 2;
        p.Add(Box([xm + runner - 0.005, y0 + 0.005, z0], [x1 - runner + 0.005, y0 + 0.1, z0 + 0.05], MachineLook.Base));
        p.Add(MachineLook.D([mid - 0.3, y0 + 0.02, z0 - 0.006], [mid + 0.3, y0 + 0.08, z0 + 0.008], MachineLook.In));
        MachineLook.Hazard(p, [mid - 0.3, y0 + 0.082, z0 - 0.006], [mid + 0.3, y0 + 0.098, z0 + 0.008]);
        // The way out is a hand's: an orange lip across the front.
        p.Add(MachineLook.D([xm + runner + 0.01, y0 + 0.02, z1 - 0.045], [x1 - runner - 0.01, y0 + 0.125, z1 - 0.005], MachineLook.Out));
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        p.Stats(180, 500, 3, seconds: 5.0);
        p.Set(("power", 80), ("onBreak", "disable"));
        p.Set(("panels", Py.L(Kw(("source", "craft"), ("at", Py.L(Py.Round((x0 + xm) / 2, 3), Py.Round(y0 + MachineLook.EyeY, 3), Py.Round(dz0 + 0.171, 3))), ("size", Py.L(0.74, 0.5)), ("face", "+z"), ("up", "+y"), ("rows", 8), ("range", 12)))));
        const double section = 0.8, length = 2.3;
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, section)), ("length", length), ("count", 400), ("crafting", true),
            ("hold", Py.L(Py.L(Py.Round(xm + 0.1, 3), Py.Round(y0 + 0.133, 3), Py.Round(z0 + 0.074, 3)), Py.L(Py.Round(x1 - 0.1, 3), Py.Round(y0 + top - 0.01, 3), Py.Round(z1 - 0.06, 3)))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round(mid, 3), Py.Round(y0 + 0.05, 3), z0)), ("face", "-z"), ("opening", Py.L(section, section))),
            Kw(("name", "Piece in 2"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round(x0 + 0.02, 3), Py.Round(y0 + 0.05, 3), Py.Round(z0 + 0.9, 3))), ("face", "-x"), ("opening", Py.L(0.5, 0.5))),
            Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(Py.Round((x0 + xm) / 2, 3), Py.Round(y0 + 0.95, 3), Py.Round(z0 + 0.56, 3))), ("face", "+y")))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Terminal())]);
}
