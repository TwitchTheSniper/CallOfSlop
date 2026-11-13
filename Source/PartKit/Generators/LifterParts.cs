using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Lifters: Constructs/Parts/Lifters (docs/sandbox/design/arms-and-cranes.md; ConstructLifters.cs). The Loader Arm in the machine
/// tiers' five tiers (section 3.2): 1 Scrap, 2 Workshop, 3 Industrial, 4 Military, 5 Plasma, each grown by the tiers' size (x1 to
/// x2.5) with its reach, links and plinth. The part file holds only what stands still: a plinth with hazard paint all round (an arm
/// has no fixed sides), the tier's dressing and the fixed half of the turntable with the slewing ring on top. The turret, shoulder,
/// upper arm, elbow, forearm, wrist and gripper are not boxes of the part: the renderer poses them each frame by two-link inverse
/// kinematics from the hand (<c>lifter.base</c> is the shoulder, <c>lifter.arm</c> the two links, <c>lifter.ring</c> the ring's top
/// the turret stands on, <c>lifter.look</c> their colours: LifterRender.cs).
/// </summary>
public sealed class LifterParts : IPartGenerator
{
    public string Name => "lifter_parts";

    private const string Out = "Constructs/Parts/Lifters";
    private const string Rust = "#6b4a35", Rust2 = "#7a4432", Timber = "#9a7442", Dark = "#2f2a26", Iron = "#8b857a", Pin = "#f07818";
    public const double TurretY = 0.66; // the slewing ring's top over the part's floor at tier 1 (grown by the tier's size)

    private static readonly string[] Tiers = ["", "Scrap", "Workshop", "Industrial", "Military", "Plasma"];
    private static readonly string[] Prefix = ["", "", "Workshop ", "Industrial ", "Military ", "Plasma "];
    private static readonly double[] Size = [0, 1.0, 1.25, 1.5, 2.0, 2.5]; // the machine tiers' sizes (MachineTiers.Size)
    private static readonly int[] Cells = [0, 6, 8, 9, 12, 15]; // the plinth's side in 0.25 m cells: 6 x Size, rounded

    /// <summary>The design's table (section 3.2): reach min and max, up, payload kg, piece across and long, hand speed, handling s, power W.</summary>
    private static readonly (double R0, double R1, double Up, int Payload, double Across, double Long, double Speed, double Handle, int Power)[] Table =
    [
        default,
        (1.0, 4.0, 3.0, 500, 1.0, 4.0, 3.0, 0.8, 600),
        (1.2, 5.0, 4.0, 1000, 1.25, 5.0, 4.5, 0.6, 1100),
        (1.5, 6.0, 5.0, 2000, 1.5, 6.0, 6.0, 0.45, 1900),
        (2.0, 8.0, 6.0, 4000, 2.0, 8.0, 9.0, 0.3, 3400),
        (2.5, 10.0, 8.0, 8000, 2.5, 10.0, 12.0, 0.2, 5800),
    ];

    private static readonly double[] MassK = [0, 1.0, 1.5, 2.25, 4.0, 6.25], HealthK = [0, 1.0, 1.5, 2.25, 4.0, 6.25];
    private static readonly int[] Armour = [0, 3, 4, 6, 9, 12];

    /// <summary>The drawn half's colours a tier: turret, counterweight, upper arm, forearm, joints, pins (what moves: the accent).</summary>
    private static readonly string[][] Look =
    [
        [],
        ["#7a4432", "#2f2a26", "#7a4432", "#6b4a35", "#8b857a", Pin], // a rusty derrick arm
        ["#2f5f8a", "#2a2e33", "#3f6f9c", "#2f5f8a", "#d8c690", Pin], // blue cast iron, cream trim
        ["#f0c21a", "#5e6266", "#f0c21a", "#c9a21c", "#74777a", Pin], // a yellow hydraulic arm on grey
        ["#4b5436", "#3e4530", "#59603f", "#4b5436", "#6a6f60", "#d9d5c0"], // olive armour, pale stencil on what moves
        ["#1a1f2e", "#10131c", "#2c3448", "#242b3e", "#39445c", "#35e0ff"], // dark shells, a glowing ring joint
    ];

    private static Part Arm(int t)
    {
        var c = Small;
        var s = Size[t];
        var n = Cells[t];
        var tb = Table[t];
        var pal = TierStyle.Palette[t];
        var name = Prefix[t] + "Loader Arm";
        var desc = $"Stands beside a Building Station and fits parts from its hold onto the build in the bay: one more pair of hands. Reaches {tb.R0:0.#} to {tb.R1:0.#} m round its base, carries {(tb.Payload >= 1000 ? $"{tb.Payload / 1000.0:0.#} t" : $"{tb.Payload} kg")}. Needs power while it moves.";
        var p = new Part(name, c, "Lifters", desc, role: "frame", tool: "welder", color: t == 1 ? Rust : pal["body"], group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(n), y0 = c.Lo(), z0 = c.Lo(), z1 = c.Hi(n);
        double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        var plinth = 0.34 * s;
        var ring = TurretY * s; // the ring's top over the floor
        var drum = 0.5 * s; // the drum's half side
        var baseCol = t == 1 ? Dark : pal["dark"];
        // ---- The plinth: a skirt in the tier's dark base, hazard paint all round (no fixed sides), the drum and the slewing ring.
        p.Add(Box([x0 + 0.02, y0, z0 + 0.02], [x1 - 0.02, y0 + plinth, z1 - 0.02], t == 3 ? pal["frame"] : baseCol));
        double hy0 = y0 + plinth * 0.65, hy1 = y0 + plinth * 0.88;
        MachineLook.Hazard(p, [x0 + 0.014, hy0, z0 + 0.1], [x0 + 0.026, hy1, z1 - 0.1]);
        MachineLook.Hazard(p, [x1 - 0.026, hy0, z0 + 0.1], [x1 - 0.014, hy1, z1 - 0.1]);
        MachineLook.Hazard(p, [x0 + 0.1, hy0, z0 + 0.014], [x1 - 0.1, hy1, z0 + 0.026]);
        MachineLook.Hazard(p, [x0 + 0.1, hy0, z1 - 0.026], [x1 - 0.1, hy1, z1 - 0.014]);
        var drumCol = t == 1 ? Rust2 : pal["body"];
        var steel = t == 1 ? Iron : pal["edge"];
        if (t == 5)
        {
            // Plasma: a rounded dark drum, a glowing ring at its waist.
            var solid = Box([cx - drum, y0 + plinth - 0.005, cz - drum], [cx + drum, y0 + ring * 0.9, cz + drum], drumCol)!;
            solid["hidden"] = true; // (the collider; the round drum is drawn)
            p.Add(solid);
            p.Add(MachineLook.D([cx - drum, y0 + plinth - 0.004, cz - drum], [cx + drum, y0 + ring * 0.9 - 0.001, cz + drum], drumCol, paintable: true, shape: Kw(("shape", "cylinder"), ("segments", 24), ("front", "+y"))));
            p.Add(MachineLook.D([cx - drum - 0.03, y0 + ring * 0.62, cz - drum - 0.03], [cx + drum + 0.03, y0 + ring * 0.68, cz + drum + 0.03], pal["light"], shape: Kw(("shape", "cylinder"), ("segments", 24), ("front", "+y"))));
        }
        else
            p.Add(Box([cx - drum, y0 + plinth - 0.005, cz - drum], [cx + drum, y0 + ring * 0.9, cz + drum], drumCol));
        var ringHalf = drum * 0.88;
        p.Add(MachineLook.D([cx - ringHalf, y0 + ring * 0.9 - 0.005, cz - ringHalf], [cx + ringHalf, y0 + ring, cz + ringHalf], steel, "steel",
            shape: t == 5 ? Kw(("shape", "cylinder"), ("segments", 24), ("front", "+y")) : null)); // the slewing ring
        // ---- The tier's dressing (tier by build: more guarding, cladding and piping up the ladder; the same silhouette).
        switch (t)
        {
            case 1:
                // Scrap: four crooked timber props, a chain drum with its brake band on the drum's side.
                for (var sx = 0; sx < 2; sx++)
                    for (var sz = 0; sz < 2; sz++)
                    {
                        double px = sx == 0 ? x0 + 0.05 : x1 - 0.19, pz = sz == 0 ? z0 + 0.05 : z1 - 0.19;
                        var h = 0.82 + 0.06 * (sx + 2 * sz) % 0.15; // (crooked: no two the same height)
                        p.Add(MachineLook.D([px, y0 + plinth - 0.005, pz], [px + 0.14, y0 + h, pz + 0.14], Timber, "wood"));
                    }
                p.Add(MachineLook.D([cx - 0.56, y0 + 0.39, cz - 0.2], [cx - 0.495, y0 + 0.55, cz + 0.2], Iron, "steel")); // the chain drum
                p.Add(MachineLook.D([cx - 0.575, y0 + 0.43, cz - 0.24], [cx - 0.555, y0 + 0.51, cz + 0.24], MachineLook.Accent)); // its brake band
                break;
            case 2:
                // Workshop: a cream trim band round the drum, rivets on the skirt, a tidy motor box on one side.
                p.Add(MachineLook.D([cx - drum - 0.02, y0 + ring * 0.7, cz - drum - 0.02], [cx + drum + 0.02, y0 + ring * 0.76, cz + drum + 0.02], pal["trim"]));
                for (var sx = 0; sx < 2; sx++)
                    for (var sz = 0; sz < 2; sz++)
                    {
                        double px = sx == 0 ? x0 + 0.12 : x1 - 0.2, pz = sz == 0 ? z0 + 0.12 : z1 - 0.2;
                        p.Add(MachineLook.D([px, y0 + plinth - 0.004, pz], [px + 0.08, y0 + plinth + 0.03, pz + 0.08], pal["rivet"], "steel"));
                    }
                p.Add(MachineLook.D([cx + drum - 0.004, y0 + plinth + 0.04, cz - 0.22], [cx + drum + 0.26, y0 + plinth + 0.34, cz + 0.22], pal["motor"]));
                p.Add(MachineLook.D([cx + drum + 0.26 - 0.004, y0 + plinth + 0.12, cz - 0.08], [cx + drum + 0.3, y0 + plinth + 0.26, cz + 0.08], steel, "steel"));
                break;
            case 3:
                // Industrial: a hydraulic power pack beside the drum, its pipes up the drum's side, a hazard-striped kick rail.
                p.Add(MachineLook.D([cx - drum - 0.5, y0 + plinth - 0.004, cz - 0.35], [cx - drum - 0.06, y0 + plinth + 0.5, cz + 0.35], pal["frame"]));
                p.Add(MachineLook.D([cx - drum - 0.5, y0 + plinth + 0.5 - 0.004, cz - 0.3], [cx - drum - 0.1, y0 + plinth + 0.56, cz + 0.3], pal["accent"]));
                for (var k = -1; k <= 1; k += 2)
                {
                    p.Add(MachineLook.D([cx - drum - 0.07, y0 + plinth + 0.2, cz + k * 0.18 - 0.04], [cx - drum + 0.004, y0 + ring * 0.86, cz + k * 0.18 + 0.04], pal["pipe"], "steel"));
                }
                p.Add(MachineLook.D([x0 + 0.04, y0 + plinth - 0.004, z1 - 0.12], [x1 - 0.04, y0 + plinth + 0.08, z1 - 0.06], pal["stripe"], "hazard paint"));
                p.Add(MachineLook.D([x0 + 0.04, y0 + plinth - 0.004, z0 + 0.06], [x1 - 0.04, y0 + plinth + 0.08, z0 + 0.12], pal["stripe"], "hazard paint"));
                break;
            case 4:
                // Military: armour skirts proud of the plinth on every side, a pale stencil band on the drum, sandbags at two corners.
                var a = 0.05;
                p.Add(MachineLook.D([x0 - 0.004, y0 + 0.04, z0 + 0.3], [x0 + a, y0 + plinth * 0.6, z1 - 0.3], pal["plate"]));
                p.Add(MachineLook.D([x1 - a, y0 + 0.04, z0 + 0.3], [x1 + 0.004, y0 + plinth * 0.6, z1 - 0.3], pal["plate"]));
                p.Add(MachineLook.D([x0 + 0.3, y0 + 0.04, z0 - 0.004], [x1 - 0.3, y0 + plinth * 0.6, z0 + a], pal["plate"]));
                p.Add(MachineLook.D([x0 + 0.3, y0 + 0.04, z1 - a], [x1 - 0.3, y0 + plinth * 0.6, z1 + 0.004], pal["plate"]));
                p.Add(MachineLook.D([cx - drum - 0.02, y0 + ring * 0.6, cz - drum - 0.02], [cx + drum + 0.02, y0 + ring * 0.68, cz + drum + 0.02], pal["accent"]));
                for (var k = 0; k < 2; k++)
                {
                    double px = k == 0 ? x0 + 0.08 : x1 - 0.68, pz = k == 0 ? z0 + 0.08 : z1 - 0.48;
                    p.Add(MachineLook.D([px, y0 + plinth - 0.004, pz], [px + 0.6, y0 + plinth + 0.22, pz + 0.4], pal["sand"], "fabric"));
                    p.Add(MachineLook.D([px + 0.08, y0 + plinth + 0.216, pz + 0.05], [px + 0.52, y0 + plinth + 0.4, pz + 0.35], pal["sand"], "fabric"));
                }
                break;
            case 5:
                // Plasma: four glowing pylons at the plinth's corners and a violet core lamp on the drum's flank.
                for (var sx = 0; sx < 2; sx++)
                    for (var sz = 0; sz < 2; sz++)
                    {
                        double px = sx == 0 ? x0 + 0.12 : x1 - 0.32, pz = sz == 0 ? z0 + 0.12 : z1 - 0.32;
                        p.Add(MachineLook.D([px, y0 + plinth - 0.004, pz], [px + 0.2, y0 + plinth + 0.7, pz + 0.2], pal["plate2"]));
                        p.Add(MachineLook.D([px + 0.03, y0 + plinth + 0.7 - 0.004, pz + 0.03], [px + 0.17, y0 + plinth + 0.82, pz + 0.17], pal["edge"]));
                    }
                p.Add(MachineLook.D([cx - 0.14, y0 + ring * 0.4, cz - drum - 0.04], [cx + 0.14, y0 + ring * 0.55, cz - drum + 0.004], pal["heat"]));
                break;
        }
        BlockSockets(p, n, n, n, faces: "y", skip: ["+y"]);
        p.Stats(380 * MassK[t], 600 * HealthK[t], Armour[t], seconds: Py.Round(6.0 * (1 + 0.25 * (t - 1)), 2));
        if (t > 1)
            p.Set(("tier", t));
        p.Set(("power", tb.Power), ("onBreak", "disable"));
        var lk = Look[t];
        p.Set(("lifter", Kw(("kind", "arm"), ("base", Py.L(Py.Round(cx, 3), Py.Round(y0 + 1.32 * s, 3), Py.Round(cz, 3))), ("ring", Py.Round(y0 + ring, 3)),
            ("reach", Py.L(tb.R0, tb.R1)), ("up", tb.Up), ("down", Py.Round(1.0 * s, 2)), ("payload", tb.Payload), ("piece", Py.L(tb.Across, tb.Long)),
            ("speed", tb.Speed), ("handle", tb.Handle), ("arm", Py.L(Py.Round(2.3 * s, 3), Py.Round(2.2 * s, 3))),
            ("look", Kw(("turret", lk[0]), ("weight", lk[1]), ("upper", lk[2]), ("fore", lk[3]), ("joint", lk[4]), ("pin", lk[5]))))));
        p.Set(("config", Py.L(
            Kw(("key", "liftMode"), ("type", "choice"), ("label", "Works"), ("group", "Lifter"), ("options", Py.L("Auto", "Station only", "Carry only", "Off")), ("default", 0),
                ("tooltip", "Auto: every job in its reach. Station only: fits parts at Building Stations. Carry only: carries between machines (to come). Off: stands still.")),
            Kw(("key", "carryHeight"), ("label", "Carry height"), ("group", "Lifter"), ("unit", "m"), ("min", 0.0), ("max", Py.Round(tb.Up, 1)), ("step", 0.1), ("default", 0.4),
                ("tooltip", "How high it lifts a piece over the higher end of its move before it turns. Never past what its arm reaches.")))));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, Enumerable.Range(1, 5).Select(t => ("", Arm(t))));
}
