using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Lifters: Constructs/Parts/Lifters (docs/sandbox/design/arms-and-cranes.md; ConstructLifters.cs). Phase 1 makes the tier 1
/// Loader Arm: a squat turret base on timber props (the Scrap look: rust, timber, mismatched plates) and a slewing ring; the
/// turret, shoulder, upper arm, elbow, forearm, wrist and gripper are not boxes of the part: the renderer poses them each frame
/// by two-link inverse kinematics from the hand (<c>lifter.base</c> is the shoulder, <c>lifter.arm</c> the two links). Hazard paint runs all round the plinth: an arm has no fixed sides.
/// </summary>
public sealed class LifterParts : IPartGenerator
{
    public string Name => "lifter_parts";

    private const string Out = "Constructs/Parts/Lifters";
    private const string Rust = "#6b4a35", Rust2 = "#7a4432", Timber = "#9a7442", Dark = "#2f2a26", Iron = "#8b857a";
    public const double TurretY = 0.66; // the slewing ring's top over the part's floor: where the drawn turret stands (LifterRender.TurretY)

    /// <summary>The tier 1 Loader Arm (the design's table, section 3.2).</summary>
    private static Part ScrapArm()
    {
        var c = Small;
        const int n = 6; // 1.5 x 1.5 m of floor, 1.5 m high
        var p = new Part("Loader Arm", c, "Lifters", "Stands beside a Building Station and fits parts from its hold onto the build in the bay: one more pair of hands. Reaches 1 to 4 m round its base, carries 500 kg. Needs power.",
            role: "frame", tool: "welder", color: Rust, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(n), y0 = c.Lo(), z0 = c.Lo(), z1 = c.Hi(n);
        double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        const double plinth = 0.34;
        // ---- The plinth: a dark skirt with hazard paint all round, and four timber props at its corners.
        p.Add(Box([x0 + 0.02, y0, z0 + 0.02], [x1 - 0.02, y0 + plinth, z1 - 0.02], Dark));
        MachineLook.Hazard(p, [x0 + 0.014, y0 + 0.22, z0 + 0.1], [x0 + 0.026, y0 + 0.3, z1 - 0.1]);
        MachineLook.Hazard(p, [x1 - 0.026, y0 + 0.22, z0 + 0.1], [x1 - 0.014, y0 + 0.3, z1 - 0.1]);
        MachineLook.Hazard(p, [x0 + 0.1, y0 + 0.22, z0 + 0.014], [x1 - 0.1, y0 + 0.3, z0 + 0.026]);
        MachineLook.Hazard(p, [x0 + 0.1, y0 + 0.22, z1 - 0.026], [x1 - 0.1, y0 + 0.3, z1 - 0.014]);
        for (var sx = 0; sx < 2; sx++)
            for (var sz = 0; sz < 2; sz++)
            {
                double px = sx == 0 ? x0 + 0.05 : x1 - 0.19, pz = sz == 0 ? z0 + 0.05 : z1 - 0.19;
                var h = 0.82 + 0.06 * (sx + 2 * sz) % 0.15; // (crooked: no two the same height)
                p.Add(MachineLook.D([px, y0 + plinth - 0.005, pz], [px + 0.14, y0 + h, pz + 0.14], Timber, "wood"));
            }
        // ---- The fixed half of the turntable: a rusty drum with a chain drum on its side, and the slewing ring on top. The turning
        //      half (the turret, the shoulder, both links, the elbow, the wrist and the gripper) is drawn by LifterRender, posed by
        //      the hand's place each frame; it stands on the ring's top at TurretY.
        p.Add(Box([cx - 0.5, y0 + plinth - 0.005, cz - 0.5], [cx + 0.5, y0 + 0.6, cz + 0.5], Rust2));
        p.Add(MachineLook.D([cx - 0.56, y0 + 0.39, cz - 0.2], [cx - 0.495, y0 + 0.55, cz + 0.2], Iron, "steel")); // the chain drum
        p.Add(MachineLook.D([cx - 0.575, y0 + 0.43, cz - 0.24], [cx - 0.555, y0 + 0.51, cz + 0.24], MachineLook.Accent)); // its brake band
        p.Add(MachineLook.D([cx - 0.44, y0 + 0.595, cz - 0.44], [cx + 0.44, y0 + TurretY, cz + 0.44], Iron, "steel")); // the slewing ring
        BlockSockets(p, n, n, n, faces: "y", skip: ["+y"]);
        p.Stats(380, 600, 3, seconds: 6.0);
        p.Set(("power", 600), ("onBreak", "disable"));
        p.Set(("lifter", Kw(("kind", "arm"), ("base", Py.L(Py.Round(cx, 3), Py.Round(y0 + 1.32, 3), Py.Round(cz, 3))),
            ("reach", Py.L(1.0, 4.0)), ("up", 3.0), ("down", 1.0), ("payload", 500), ("piece", Py.L(1.0, 4.0)), ("speed", 3.0), ("handle", 0.8), ("arm", Py.L(2.3, 2.2)))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", ScrapArm())]);
}
