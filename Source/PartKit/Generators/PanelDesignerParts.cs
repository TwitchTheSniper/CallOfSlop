using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Panel Designer: Constructs/Parts/PanelDesigner/PanelDesigner.json (docs/sandbox/constructs/production.md, "Panel Designer"). A
/// drafting desk with a large screen at standing height at its front left, a raked drawing board under the screen, a model plinth
/// beside the desk, a plan chest behind it, and along its right a wide flat tray formed panels ride into from the back. It is a
/// Crafting Terminal (<c>storage.pieces</c> with <c>crafting</c>) with <c>designer</c> set: its network, orders and tray are the
/// terminal's (ConstructCrafting.cs); F opens the designer's panel (UiPanelDesigner.cs).
/// The look (look.md "Machine look"): a dark base, the body colour, the accent only on the keys, the rule and the tray's rails;
/// white at the way in, orange at the way out.
/// </summary>
public sealed class PanelDesignerParts : IPartGenerator
{
    public string Name => "panel_designer_parts";

    private const string Out = "Constructs/Parts/PanelDesigner";
    private const string Body = "#3f6f9a", Screen = "#10161c", Paper = "#dfe6ea"; // (a drawing office's blue: it takes paint)

    private static Part Designer()
    {
        var c = Small;
        const int nx = 14, ny = 8, nz = 10; // 3.5 m wide, 2.0 m high, 2.5 m deep
        var p = new Part("Panel Designer", c, "Extrusion", "Design a body panel on its screen: pick a preset, set its size, crown and rake, and order it. A linked Panel Former forms it. Saved designs are shared with the world. Needs power.",
            role: "storage", tool: "welder", color: Body, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), z0 = c.Lo(), z1 = c.Hi(nz);
        var xm = x0 + 1.5; // the desk, plinth and plan chest to its left, the tray to its right
        var xd = x0 + 1.04; // the desk's right side; the plinth stands between it and the tray
        // ---- The desk, at the front left: a wide screen at standing height over a raked drawing board.
        double dz0 = z1 - 0.62, dz1 = z1 - 0.03;
        p.Add(Box([x0 + 0.02, y0, dz0], [xd - 0.02, y0 + MachineLook.HandY - 0.2, dz1]));
        p.Add(Box([x0 + 0.04, y0 + MachineLook.HandY - 0.21, dz0 + 0.01], [xd - 0.04, y0 + 1.92, dz0 + 0.16], MachineLook.Base)); // the screen's housing
        p.Add(MachineLook.D([x0 + 0.07, y0 + 1.16, dz0 + 0.154], [xd - 0.07, y0 + 1.86, dz0 + 0.168], Screen)); // what its panel stands on
        // The drawing board: three steps down towards the hand, each a little lower, a sheet and a rule on the lowest.
        for (var k = 0; k < 3; k++)
        {
            var za = dz0 + 0.16 + k * 0.14;
            var top = y0 + MachineLook.HandY - 0.04 - k * 0.07;
            p.Add(MachineLook.D([x0 + 0.05 + k * 0.004, y0 + MachineLook.HandY - 0.2, za], [xd - 0.05 - k * 0.004, top, za + 0.15], MachineLook.Steel, "steel"));
            if (k > 0)
                p.Add(MachineLook.D([x0 + 0.14, top, za + 0.02], [xd - 0.3, top + 0.006, za + 0.13], Paper));
        }
        p.Add(MachineLook.D([x0 + 0.1, y0 + MachineLook.HandY - 0.174, dz0 + 0.45], [xd - 0.1, y0 + MachineLook.HandY - 0.154, dz0 + 0.48], MachineLook.Accent)); // the rule
        for (var k = 0; k < 2; k++) // two keys and a stop at the board's right
            p.Add(MachineLook.D([xd - 0.26, y0 + MachineLook.HandY - 0.11 - k * 0.07, dz0 + 0.33 + k * 0.14], [xd - 0.12, y0 + MachineLook.HandY - 0.09 - k * 0.07, dz0 + 0.42 + k * 0.14], k == 0 ? MachineLook.Accent : MachineLook.Stop));
        p.Add(MachineLook.D([x0 + 0.02, y0, dz1 - 0.008], [xd - 0.02, y0 + 0.16, dz1 + 0.008], MachineLook.Base)); // a kick plate
        // ---- The plinth, beside the desk: a dark post with a pale top the model of the last design stands on.
        double pz0 = z1 - 0.5, pz1 = z1 - 0.1;
        p.Add(Box([xd + 0.03, y0, pz0], [xm - 0.07, y0 + 0.9, pz1], MachineLook.Base));
        p.Add(MachineLook.D([xd + 0.015, y0 + 0.9, pz0 - 0.015], [xm - 0.055, y0 + 0.93, pz1 + 0.015], Paper));
        p.Add(MachineLook.D([xd + 0.024, y0 + 0.86, pz0 - 0.006], [xm - 0.064, y0 + 0.9, pz1 + 0.006], MachineLook.Accent));
        // ---- The plan chest, behind the desk: a low dark cabinet of wide drawers, steel pulls.
        p.Add(Box([x0 + 0.02, y0, z0 + 0.05], [xm - 0.07, y0 + 0.78, dz0 - 0.03], MachineLook.Base));
        for (var k = 0; k < 4; k++)
        {
            var y = y0 + 0.08 + k * 0.17;
            p.Add(MachineLook.D([xm - 0.076, y, z0 + 0.12], [xm - 0.062, y + 0.13, dz0 - 0.1], Body, paintable: true));
            p.Add(MachineLook.D([xm - 0.066, y + 0.05, z0 + 0.6], [xm - 0.048, y + 0.08, dz0 - 0.58], MachineLook.Steel, "steel"));
        }
        p.Add(MachineLook.D([x0 + 0.1, y0 + 0.78, z0 + 0.14], [xm - 0.16, y0 + 0.794, dz0 - 0.12], MachineLook.Steel, "steel")); // its top: a place for drawings
        // ---- The tray, along the right: wide and flat for panels; runners, slats, accent rails.
        const double runner = 0.12, high = 0.12, post = 0.07, rail = 0.06, top2 = 0.78;
        p.Add(Box([xm, y0, z0], [xm + runner, y0 + high, z1], MachineLook.Base));
        p.Add(Box([x1 - runner, y0, z0], [x1, y0 + high, z1], MachineLook.Base));
        for (var k = 0; k <= 2; k++)
        {
            var z = z0 + 0.06 + (z1 - z0 - 0.18) * k / 2;
            p.Add(Box([xm + runner - 0.005, y0 + 0.01, z], [x1 - runner + 0.005, y0 + 0.11, z + 0.12], MachineLook.Base));
            p.Add(Box([xm + 0.02, y0 + high - 0.005, z], [xm + 0.02 + post, y0 + top2 - rail + 0.005, z + post]));
            p.Add(Box([x1 - 0.02 - post, y0 + high - 0.005, z], [x1 - 0.02, y0 + top2 - rail + 0.005, z + post]));
        }
        for (var k = 1; k < 6; k++)
        {
            var x = xm + runner + (x1 - xm - 2 * runner) * k / 6;
            p.Add(MachineLook.D([x - 0.03, y0 + 0.105, z0 + 0.04], [x + 0.03, y0 + 0.13, z1 - 0.04], MachineLook.Steel, "steel"));
        }
        p.Add(MachineLook.D([xm + 0.01, y0 + top2 - rail, z0 + 0.02], [xm + 0.03 + post, y0 + top2, z1 - 0.02], MachineLook.Accent));
        p.Add(MachineLook.D([x1 - 0.03 - post, y0 + top2 - rail, z0 + 0.02], [x1 - 0.01, y0 + top2, z1 - 0.02], MachineLook.Accent));
        // The way in at the back: a sill in hazard paint under a white band.
        var mid = (xm + x1) / 2;
        p.Add(Box([xm + runner - 0.005, y0 + 0.005, z0], [x1 - runner + 0.005, y0 + 0.1, z0 + 0.05], MachineLook.Base));
        p.Add(MachineLook.D([mid - 0.6, y0 + 0.02, z0 - 0.006], [mid + 0.6, y0 + 0.08, z0 + 0.008], MachineLook.In));
        MachineLook.Hazard(p, [mid - 0.6, y0 + 0.082, z0 - 0.006], [mid + 0.6, y0 + 0.098, z0 + 0.008]);
        // The way out is a hand's: an orange lip across the front.
        p.Add(MachineLook.D([xm + runner + 0.01, y0 + 0.02, z1 - 0.045], [x1 - runner - 0.01, y0 + 0.125, z1 - 0.005], MachineLook.Out));
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        p.Stats(230, 600, 3, seconds: 5.0);
        p.Set(("power", 120), ("onBreak", "disable"));
        p.Set(("panels", Py.L(Kw(("source", "craft"), ("at", Py.L(Py.Round((x0 + xd) / 2, 3), Py.Round(y0 + MachineLook.EyeY + 0.01, 3), Py.Round(dz0 + 0.171, 3))), ("size", Py.L(0.86, 0.66)), ("face", "+z"), ("up", "+y"), ("rows", 9), ("range", 12)))));
        const double across = 1.85, up = 0.6, length = 2.3;
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(across, up)), ("length", length), ("count", 200), ("crafting", true), ("designer", true), ("plinth", Py.L(Py.Round((xd + 0.03 + xm - 0.07) / 2, 3), Py.Round(y0 + 0.935, 3), Py.Round((pz0 + pz1) / 2, 3), 0.34)),
            ("hold", Py.L(Py.L(Py.Round(xm + 0.07, 3), Py.Round(y0 + 0.133, 3), Py.Round(z0 + 0.074, 3)), Py.L(Py.Round(x1 - 0.07, 3), Py.Round(y0 + top2 - 0.01, 3), Py.Round(z1 - 0.06, 3)))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round(mid, 3), Py.Round(y0 + 0.05, 3), z0)), ("face", "-z"), ("opening", Py.L(across, up))))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Designer())]);
}
