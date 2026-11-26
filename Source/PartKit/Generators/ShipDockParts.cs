using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Ship Docking Station (Karl, 2026-11-26): Constructs/Parts/ShipDock/ShipDockingStation.json, a placeholder for everything a player
/// will do with a ship at a dock. For now one thing: a spawned ship design docked at it (a design body within <c>range</c> of its pad's
/// middle and slow, GameSandboxDock.cs) opens in the Ship Designer from its console, and the edit is built into that ship where it lies.
/// The look (look.md): an 8 m square pad in tread plate with hazard paint round its edge and a white square in its middle where a ship
/// sets down, four glowing corner lamps, and at its front right a console desk (a dark base, steel top, a raked screen, accent keys and
/// a red stop, a status lamp over it) where the use key works.
/// </summary>
public sealed class ShipDockParts : IPartGenerator
{
    public string Name => "ship_dock_parts";

    private const string Out = "Constructs/Parts/ShipDock";
    private const string Deck = "#5a636b", Screen = "#10161c", Lamp = "#ffd36a", ScreenGlow = "#3ea8d8";

    /// <summary>How far (m) a design ship's bounding sphere may be from the pad's middle and still be docked.</summary>
    public const double Range = 30;

    private static Part Station()
    {
        var c = Medium;
        const int n = 8; // 8 m square
        var p = new Part("Ship Docking Station", c, "Launch and landing",
            "A landing pad with a console. A ship design set down on it or held close and still is docked: use the console to open it in the Ship Designer and rebuild it as you edit. Only its owner or the host can.",
            role: "frame", tool: "welder", color: Deck);
        double x0 = c.Lo(), x1 = c.Hi(n), y0 = c.Lo(), z0 = c.Lo(), z1 = c.Hi(n);
        var top = y0 + 0.3;
        // ---- The pad: a slab in tread plate, its edge in hazard paint, a white square where a ship's keel sets down.
        p.Add(Box([x0, y0, z0], [x1, top, z1], Deck, shape: Kw(("finish", "tread plate"))));
        const double edge = 0.22, t = 0.014;
        MachineLook.Hazard(p, [x0 + 0.02, top - 0.004, z0 + 0.02], [x1 - 0.02, top + t, z0 + 0.02 + edge]);
        MachineLook.Hazard(p, [x0 + 0.02, top - 0.004, z1 - 0.02 - edge], [x1 - 0.02, top + t, z1 - 0.02]);
        MachineLook.Hazard(p, [x0 + 0.02, top - 0.006, z0 + 0.03 + edge], [x0 + 0.02 + edge, top + t - 0.003, z1 - 0.03 - edge]);
        MachineLook.Hazard(p, [x1 - 0.02 - edge, top - 0.006, z0 + 0.03 + edge], [x1 - 0.02, top + t - 0.003, z1 - 0.03 - edge]);
        double mx = (x0 + x1) / 2, mz = (z0 + z1) / 2, s = 1.6, w = 0.12;
        p.Add(MachineLook.D([mx - s, top - 0.004, mz - s], [mx + s, top + t, mz - s + w], MachineLook.In));
        p.Add(MachineLook.D([mx - s, top - 0.004, mz + s - w], [mx + s, top + t, mz + s], MachineLook.In));
        p.Add(MachineLook.D([mx - s, top - 0.006, mz - s + w + 0.01], [mx - s + w, top + t - 0.003, mz + s - w - 0.01], MachineLook.In));
        p.Add(MachineLook.D([mx + s - w, top - 0.006, mz - s + w + 0.01], [mx + s, top + t - 0.003, mz + s - w - 0.01], MachineLook.In));
        p.Add(MachineLook.D([mx - 0.3, top - 0.005, mz - 0.3], [mx + 0.3, top + t - 0.002, mz + 0.3], MachineLook.Accent)); // the middle mark
        // ---- Four corner lamps: a dark post with a glowing cap.
        foreach (var (lx, lz) in new[] { (x0 + 0.35, z0 + 0.35), (x1 - 0.35, z0 + 0.35), (x0 + 0.35, z1 - 0.35) })
        {
            p.Add(MachineLook.D([lx - 0.09, top - 0.004, lz - 0.09], [lx + 0.09, top + 0.24, lz + 0.09], MachineLook.Base));
            p.Add(Box([lx - 0.07, top + 0.236, lz - 0.07], [lx + 0.07, top + 0.34, lz + 0.07], Lamp, detail: true, shape: Kw(("glow", true), ("paintable", false))));
        }
        // ---- The console, at the front right corner: a desk facing the pad (its user stands on the pad, looking +z).
        double cx0 = x1 - 2.1, cx1 = x1 - 0.35, cz0 = z1 - 0.95, cz1 = z1 - 0.4;
        p.Add(Box([cx0, top - 0.004, cz0], [cx1, top + 0.95, cz1], MachineLook.Base));
        p.Add(MachineLook.D([cx0 - 0.02, top + 0.946, cz0 - 0.06], [cx1 + 0.02, top + 0.976, cz1 + 0.02], MachineLook.Steel, "steel")); // the desk top
        p.Add(MachineLook.D([cx0 + 0.04, top + 0.03, cz0 - 0.012], [cx1 - 0.04, top + 0.9, cz0 + 0.004], Deck, paintable: true)); // its front panel takes paint
        // The screen: a housing standing at the back of the desk, its face towards the pad, a lit panel on it and a lamp over it.
        p.Add(Box([cx0 + 0.1, top + 0.97, cz1 - 0.16], [cx1 - 0.1, top + 1.75, cz1 - 0.02], MachineLook.Base));
        p.Add(Box([cx0 + 0.16, top + 1.04, cz1 - 0.172], [cx1 - 0.16, top + 1.68, cz1 - 0.158], ScreenGlow, detail: true, shape: Kw(("glow", true), ("paintable", false))));
        p.Add(MachineLook.D([cx0 + 0.13, top + 1.01, cz1 - 0.166], [cx1 - 0.13, top + 1.71, cz1 - 0.16], Screen));
        p.Add(Box([(cx0 + cx1) / 2 - 0.08, top + 1.746, cz1 - 0.12], [(cx0 + cx1) / 2 + 0.08, top + 1.84, cz1 - 0.04], Lamp, detail: true, shape: Kw(("glow", true), ("paintable", false))));
        // Keys on the desk top: three accent keys and the red stop.
        for (var k = 0; k < 4; k++)
        {
            var kx = cx0 + 0.3 + k * 0.32;
            p.Add(MachineLook.D([kx, top + 0.972, cz0 + 0.08], [kx + 0.2, top + 1.0, cz0 + 0.22], k == 3 ? MachineLook.Stop : MachineLook.Accent));
        }
        BlockSockets(p, n, 1, n, faces: "y", skip: ["+y"], step: 2);
        p.Stats(2400, 4000, 24, seconds: 6.0);
        p.Set(("shipDock", Kw(("pad", Py.L(Py.Round(mx, 3), Py.Round(top, 3), Py.Round(mz, 3))), ("range", Range))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Station())]);
}
