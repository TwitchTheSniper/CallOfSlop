using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Building Station: Constructs/Parts/BuildingStation (docs/sandbox/constructs/production.md, "Building station"). A control
/// pedestal with the screen at standing height at its front left, an intake cabinet with a chute hopper behind it, a low hold
/// along its right that parts ride into from the back, and a mast with a boom that reaches over the hold towards the bay on
/// its +x side. The hold is a Piece Rack's (<c>storage.pieces</c> with <c>crafting</c> and <c>station</c>: ConstructStation.cs),
/// so it packs by volume and its orders are the Crafting Terminal's. The bay is three settings (bayWidth, bayDepth, bayHeight).
/// The look (look.md "Machine look"): mast and boom say function, white at the ways in, orange along the bay side, three tones.
/// </summary>
public sealed class BuildStationParts : IPartGenerator
{
    public string Name => "build_station_parts";

    private const string Out = "Constructs/Parts/BuildingStation";
    private const string Body = "#3f7d3a", Screen = "#10161c"; // (the family's body colour: it takes paint; the rest is MachineLook's tones)

    private static Part Terminal()
    {
        var c = Small;
        const int nx = 12, ny = 8, nz = 20; // 3.0 m wide, 2.0 m high, 5.0 m deep (the hold packs a kind a column along it)
        var p = new Part("Building Station", c, "Extrusion", "Builds a blueprint in the bay beside it, part by part, from what its linked stores and machines send and what you bring by hand. Pick a blueprint on its screen. Needs power.",
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
        // The fluid inlet beside it: a steel flange with a white ring, what a pipe from a tank is laid to (the build's tanks are filled from it).
        p.Add(MachineLook.D([x0 + 0.006, y0 + 0.24, z0 + 1.44], [x0 + 0.03, y0 + 0.46, z0 + 1.66], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([x0 + 0.002, y0 + 0.3, z0 + 1.5], [x0 + 0.008, y0 + 0.4, z0 + 1.6], MachineLook.In));
        // ---- The tray, along the right: runners, slats, accent rails; parts ride in at the back and are taken at the front.
        const double runner = 0.12, high = 0.12, post = 0.07, rail = 0.06, top = 1.4;
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
        // ---- The mast and its boom: over the hold, pointing at the bay (the +x side), with a steel fitting head at its end.
        p.Add(Box([x0 + 0.06, y0 + 0.69, z0 + 0.08], [x0 + 0.28, y0 + 1.84, z0 + 0.3], MachineLook.Base));
        p.Add(Box([x0 + 0.05, y0 + 1.83, z0 + 0.07], [x1 - 0.04, y0 + 1.985, z0 + 0.31]));
        p.Add(MachineLook.D([x1 - 0.34, y0 + 1.58, z0 + 0.11], [x1 - 0.1, y0 + 1.835, z0 + 0.27], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([x1 - 0.27, y0 + 1.5, z0 + 0.15], [x1 - 0.17, y0 + 1.585, z0 + 0.23], MachineLook.Accent));
        MachineLook.Hazard(p, [x1 - 0.6, y0 + 1.826, z0 + 0.064], [x1 - 0.036, y0 + 1.99, z0 + 0.082]);
        // The bay side: an orange band along the hold's outer runner, and a lamp over the screen.
        p.Add(MachineLook.D([x1 - 0.008, y0 + 0.02, z0 + 0.3], [x1 + 0.008, y0 + 0.1, z1 - 0.3], MachineLook.Out));
        p.Add(MachineLook.D([x0 + 0.38, y0 + 1.86, dz0 + 0.04], [x0 + 0.54, y0 + 1.94, dz0 + 0.13], MachineLook.Accent));
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        p.Stats(420, 800, 3, seconds: 8.0);
        p.Set(("power", 150), ("onBreak", "disable"));
        p.Set(("panels", Py.L(Kw(("source", "station"), ("at", Py.L(Py.Round((x0 + xm) / 2, 3), Py.Round(y0 + MachineLook.EyeY, 3), Py.Round(dz0 + 0.171, 3))), ("size", Py.L(0.74, 0.5)), ("face", "+z"), ("up", "+y"), ("rows", 8), ("range", 12)))));
        const double section = 1.8, length = 4.8;
        static PyDict Bay(string key, string label, double def, string tip) => Kw(("key", key), ("label", label), ("group", "Bay"), ("min", 2), ("max", 64), ("step", 0.5), ("default", def), ("unit", "m"), ("tooltip", tip));
        p.Set(("config", Py.L(Bay("bayWidth", "Bay width", 6, "The bay's size across, away from the station."), Bay("bayDepth", "Bay depth", 8, "The bay's size along the station."), Bay("bayHeight", "Bay height", 4, "The bay's height. A blueprint is built only if it fits the bay."))));
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, 1.25)), ("length", length), ("count", 400), ("crafting", true), ("station", true),
            ("hold", Py.L(Py.L(Py.Round(xm + 0.1, 3), Py.Round(y0 + 0.133, 3), Py.Round(z0 + 0.074, 3)), Py.L(Py.Round(x1 - 0.1, 3), Py.Round(y0 + top - 0.01, 3), Py.Round(z1 - 0.06, 3)))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round(mid, 3), Py.Round(y0 + 0.05, 3), z0)), ("face", "-z"), ("opening", Py.L(section, section))),
            Kw(("name", "Piece in 2"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round(x0 + 0.02, 3), Py.Round(y0 + 0.05, 3), Py.Round(z0 + 0.9, 3))), ("face", "-x"), ("opening", Py.L(0.5, 0.5))),
            Kw(("name", "Fluid in"), ("type", "pipe"), ("dir", "in"), ("at", Py.L(Py.Round(x0 + 0.02, 3), Py.Round(y0 + 0.35, 3), Py.Round(z0 + 1.55, 3))), ("face", "-x")),
            Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(Py.Round((x0 + xm) / 2, 3), Py.Round(y0 + 0.95, 3), Py.Round(z0 + 0.56, 3))), ("face", "+y")))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Terminal())]);
}
