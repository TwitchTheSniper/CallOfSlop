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
    private const string Rust = "#6b4a35", Rust2 = "#7a4432", Timber = "#9a7442", Dark = "#2f2a26", Iron = "#8b857a", Pin = "#f07818", Screen = "#10161c";
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

    /// <summary>
    /// The Module Fitter S (docs/sandbox/design/module-sockets.md 7.1): a Military Loader Arm's frame and size with the fitter's numbers
    /// (reach 1.5 to 8 m, 20 t, a module up to 3x3 and 6 m tall, 8 kW) and <c>lifter.fitter</c>: it carries modules to sockets of its
    /// construct and welds them on, or cuts their seam and sets them on a stand (ConstructModuleFitters.cs). Written by module_sockets.
    /// </summary>
    public static Part ModuleFitter() => Arm(4, fitter: true);

    private static readonly (double R0, double R1, double Up, int Payload, double Across, double Long, double Speed, double Handle, int Power) FitterRow =
        (1.5, 8.0, 6.0, 20000, 3.3, 6.5, 3.0, 1.5, 8000);

    private static Part Arm(int t, bool fitter = false)
    {
        var c = Small;
        var s = Size[t];
        var n = Cells[t];
        var tb = fitter ? FitterRow : Table[t];
        var pal = TierStyle.Palette[t];
        var name = fitter ? "Module Fitter S" : Prefix[t] + "Loader Arm";
        var desc = fitter
            ? $"A heavy arm with a gripper, a welding torch and a cutting head: F on a Module Socket in its reach fits a module lying in its reach (it carries it, seats it and welds it on), or cuts a fitted module's seam and sets it on a free Module Stand. Reaches {tb.R0:0.#} to {tb.R1:0.#} m, carries {tb.Payload / 1000.0:0.#} t, modules up to 3x3. Needs {tb.Power / 1000.0:0.#} kW while it works."
            : $"Stands beside a Building Station and fits parts from its hold onto the build in the bay: one more pair of hands. Reaches {tb.R0:0.#} to {tb.R1:0.#} m round its base, carries {(tb.Payload >= 1000 ? $"{tb.Payload / 1000.0:0.#} t" : $"{tb.Payload} kg")}. Needs power while it moves.";
        var p = new Part(name, c, fitter ? "Module fitters" : "Lifters", desc, role: "frame", tool: "welder", color: t == 1 ? Rust : pal["body"], group: fitter ? "Ship Modules" : "Production");
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
        // ---- Its screen (the user, 2026-11-13: "a small screen now"): a console on the plinth in front of the drum on the +z side,
        // clear of every tier's dressing (the drum's bands end 0.02 m out, Industrial's kick rail starts 0.12 m in from the edge).
        double sy0 = y0 + plinth - 0.004, sy1 = y0 + plinth + 0.5, sz0 = z1 - 0.21, sz1 = z1 - 0.13;
        p.Add(MachineLook.D([cx - 0.4, sy0, sz0], [cx + 0.4, sy1, sz1], t == 1 ? Iron : pal["frame"], "steel"));
        p.Add(MachineLook.D([cx - 0.38, sy1 - 0.33, sz1 - 0.004], [cx + 0.38, sy1 - 0.03, sz1 + 0.004], Screen)); // (the glass behind the panel's card)
        p.Set(("panels", Py.L(Kw(("source", "lifter"), ("at", Py.L(Py.Round(cx, 3), Py.Round(sy1 - 0.18, 3), Py.Round(sz1 + 0.007, 3))), ("size", Py.L(0.74, 0.28)),
            ("face", "+z"), ("up", "+y"), ("rows", 5), ("range", 10)))));
        BlockSockets(p, n, n, n, faces: "y", skip: ["+y"]);
        p.Stats(380 * MassK[t], 600 * HealthK[t], Armour[t], seconds: Py.Round(6.0 * (1 + 0.25 * (t - 1)), 2));
        if (t > 1)
            p.Set(("tier", t));
        p.Set(("power", tb.Power), ("onBreak", "disable"), ("collider", "boxes")); // (as the machines: a tier's round details are looks and never become a hull)
        var lk = Look[t];
        p.Set(("lifter", Kw(("kind", "arm"), ("base", Py.L(Py.Round(cx, 3), Py.Round(y0 + 1.32 * s, 3), Py.Round(cz, 3))), ("ring", Py.Round(y0 + ring, 3)),
            ("reach", Py.L(tb.R0, tb.R1)), ("up", tb.Up), ("down", Py.Round(1.32 * s + 0.3, 2)) /* (lifters phase 4: to 0.3 m under its own floor, so a rack or pad on the same ground is in reach; was 1.0 * s) */, ("payload", tb.Payload), ("piece", Py.L(tb.Across, tb.Long)),
            ("speed", tb.Speed), ("handle", tb.Handle), ("arm", Py.L(Py.Round(2.3 * s, 3), Py.Round(2.2 * s, 3))),
            ("look", Kw(("turret", lk[0]), ("weight", lk[1]), ("upper", lk[2]), ("fore", lk[3]), ("joint", lk[4]), ("pin", lk[5]))))));
        if (fitter)
            ((PyDict)p.D.Get("lifter")!)["fitter"] = true;
        p.Set(("config", Py.L(
            Kw(("key", "liftMode"), ("type", "choice"), ("label", "Works"), ("group", "Lifter"), ("options", Py.L("Auto", "Station only", "Carry only", "Off")), ("default", 0),
                ("tooltip", "Auto: every job in its reach. Station only: fits parts at Building Stations. Carry only: carries between machines (to come). Off: stands still.")),
            Kw(("key", "carryHeight"), ("label", "Carry height"), ("group", "Lifter"), ("unit", "m"), ("min", 0.0), ("max", Py.Round(tb.Up, 1)), ("step", 0.1), ("default", 0.4),
                ("tooltip", "How high it lifts a piece over the higher end of its move before it turns. Never past what its arm reaches.")))));
        return p;
    }

    // ---- The Bridge Crane (arms-and-cranes.md 3.4, phase 6): Crane Rail sections and the Crane Bridge, in four size classes (the Small
    // for small container ships' holds, Sean 2026-11-22: a 4 m span on 0.25 m runway girders).

    /// <summary>The design's table: span, rail section, hook drop, payload kg, piece across and long, travel and hoist m/s, handling s, power W.</summary>
    private static (double Span, int Rail, double Drop, int Payload, double Across, double Long, double Travel, double Hoist, double Handle, int Power) Crane(Cls c) => c.Key switch
    {
        "small" => (4, 1, 4, 5_000, 3.6, 8, 2.5, 1.25, 2.0, 2000),
        "medium" => (8, 4, 8, 30_000, 7.2, 16, 2.0, 1.0, 3.0, 6000),
        "large" => (32, 16, 24, 300_000, 28.8, 64, 1.5, 0.75, 5.0, 30_000),
        _ => (64, 32, 48, 3_000_000, 28.8, 128, 1.0, 0.5, 8.0, 120_000),
    };

    /// <summary>A crane class's word in names: the Small class has none of its own ("" in PartScale), so its cranes say "Small".</summary>
    private static string CraneName(Cls c) => c.Name == "" ? "Small" : c.Name;

    private const string CraneBase = "#22262b", CraneSteel = "#98a1a9", CraneYellow = "#f0c21a", CraneWeb = "#5e6266";

    /// <summary>
    /// A Crane Rail section: a runway girder along +z, one cell across, an I-beam (bottom flange, web, top flange) with a steel rail
    /// on top, whose top is the cell's top: the line a bridge's trucks run on. Sections in line end to end make a run (ConstructCraneRails.cs).
    /// </summary>
    private static Part Rail(Cls c)
    {
        var cr = Crane(c);
        var n = c.N(cr.Rail);
        var k = c.Cell;
        var cn = CraneName(c);
        var p = new Part($"Crane Rail {cn} {cr.Rail} m", c, "Bridge cranes",
            $"A {cr.Rail} m section of crane runway. Lay two runs of them {cr.Span:0} m apart, in line end to end, on columns or walls; a {cn} Crane Bridge bolted to one runs over both.",
            role: "frame", color: CraneBase, group: cn);
        double z0 = c.Lo(), z1 = c.Hi(n), h = 0.5 * k;
        var cell = Box([-h, -h, z0], [h, h, z1], CraneBase)!;
        cell["hidden"] = true; // (the collider and the sockets' faces: the I-beam inside it is drawn)
        p.Add(cell);
        p.Add(Box([-0.42 * k, -h + 0.004 * k, z0 + 0.004 * k], [0.42 * k, -0.36 * k, z1 - 0.004 * k], CraneBase)); // (the bottom flange)
        p.Add(Box([-0.08 * k, -0.365 * k, z0 + 0.008 * k], [0.08 * k, 0.385 * k, z1 - 0.008 * k], CraneWeb)); // (the web)
        p.Add(Box([-0.36 * k, 0.38 * k, z0 + 0.004 * k], [0.36 * k, 0.455 * k, z1 - 0.004 * k], CraneBase)); // (the top flange)
        p.Add(MachineLook.D([-0.09 * k, 0.452 * k, z0 + 0.002 * k], [0.09 * k, h, z1 - 0.002 * k], CraneSteel, "steel")); // (the rail its trucks run on)
        // Hazard paint on the bottom flange's edges: a runway is overhead work.
        p.Add(MachineLook.D([-0.425 * k, -0.47 * k, z0 + 0.05 * k], [-0.416 * k, -0.39 * k, z1 - 0.05 * k], CraneYellow, "hazard paint"));
        p.Add(MachineLook.D([0.416 * k, -0.47 * k, z0 + 0.05 * k], [0.425 * k, -0.39 * k, z1 - 0.05 * k], CraneYellow, "hazard paint"));
        BlockSockets(p, 1, 1, n);
        var mass = cr.Rail * c.KgM2 * 1.6 * k; // (a girder a cell deep: about 130 kg a metre at Medium)
        p.Stats(mass, cr.Rail * c.HealthM2 * k * 2, c.Armour, seconds: Py.Round(c.Seconds * 1.5, 1));
        p.Set(("craneRail", true), ("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// The Crane Bridge: the lifter part (kind "bridge"). What stands still is its drive and power cabinet, a cell bolted to the outer
    /// side of a rail by its +x socket; the bridge (end trucks, double girder), the trolley, the cable and the hook are drawn from the
    /// hook's place (LifterRender.DrawCrane). Its frame: +x across the span through the near rail, +z along the rails; <c>base</c> is the
    /// near rail's top line beside it.
    /// </summary>
    private static Part Bridge(Cls c)
    {
        var cr = Crane(c);
        var k = c.Cell;
        var carries = cr.Payload >= 1000 ? $"{cr.Payload / 1000:0} t" : $"{cr.Payload} kg";
        var cn = CraneName(c);
        var p = new Part($"{cn} Crane Bridge", c, "Bridge cranes",
            $"An overhead crane over a {cr.Span:0} m span: bolt it to the outer side of a run of {cn} Crane Rails with a second run {cr.Span:0} m across. Carries {carries} between machines, racks and pads under it, and fits parts at a Building Station. Needs power while it moves.",
            role: "frame", color: CraneYellow, group: "Production");
        double lo = c.Lo(), hi = c.Hi(1);
        var whole = Box([lo, lo, lo], [hi, hi, hi], CraneBase)!;
        whole["hidden"] = true; // (the collider and the sockets' faces)
        p.Add(whole);
        // The cabinet: a dark skirt, the yellow body, a steel door, hazard bands round its foot, a lamp mast with the festoon's arm.
        p.Add(Box([lo + 0.02 * k, lo, lo + 0.02 * k], [hi - 0.03 * k, lo + 0.12 * k, hi - 0.02 * k], CraneBase));
        p.Add(Box([lo + 0.06 * k, lo + 0.116 * k, lo + 0.06 * k], [hi - 0.06 * k, hi - 0.1 * k, hi - 0.06 * k], CraneYellow));
        var t = Math.Max(0.004 * k, 0.003); // (half a skin's thickness: at Small 0.004 a cell is a 2 mm sliver Box drops, so 3 mm, grown to a detail's 16 mm)
        p.Add(MachineLook.D([lo + 0.2 * k, lo + 0.18 * k, lo + 0.06 * k - t], [hi - 0.2 * k, hi - 0.2 * k, lo + 0.06 * k + t], CraneSteel, "steel")); // (its door, -z)
        MachineLook.Hazard(p, [lo + 0.02 * k - t, lo + 0.03 * k, lo + 0.1 * k], [lo + 0.02 * k + t, lo + 0.1 * k, hi - 0.1 * k]);
        MachineLook.Hazard(p, [lo + 0.1 * k, lo + 0.03 * k, hi - 0.02 * k - t], [hi - 0.1 * k, lo + 0.1 * k, hi - 0.02 * k + t]);
        p.Add(MachineLook.D([lo + 0.1 * k, hi - 0.104 * k, lo + 0.1 * k], [hi - 0.1 * k, hi - 0.04 * k, hi - 0.1 * k], CraneBase)); // (its lid)
        p.Add(MachineLook.D([lo + 0.12 * k, hi - Math.Max(0.044 * k, 0.016), lo + 0.4 * k], [lo + 0.2 * k, hi, lo + 0.48 * k], MachineLook.Accent)); // (the beacon's foot; never under a detail's 16 mm)
        // Its screen on the -x face (away from the rail: the aisle side), the middle about 1.5 m up at Medium.
        double sx = lo + 0.056 * k, sy = lo + 0.62 * k; // (at Small the screen grows to a detail's 16 mm, so its panel sits 11 mm out)
        p.Add(MachineLook.D([sx - t, sy - 0.16 * k, lo + 0.16 * k], [sx + t, sy + 0.16 * k, hi - 0.16 * k], Screen));
        p.Set(("panels", Py.L(Kw(("source", "lifter"), ("at", Py.L(Py.Round(sx - (k < 1 ? 0.011 : 0.007 * k), 3), Py.Round(sy, 3), 0.0)), ("size", Py.L(Py.Round(0.6 * k, 3), Py.Round(0.28 * k, 3))),
            ("face", "-x"), ("up", "+y"), ("rows", 5), ("range", Py.Round(10 * k, 1))))));
        BlockSockets(p, 1, 1, 1, faces: "xyz", skip: ["+y"]);
        p.Stats(c.KgM2 * 6 * k * k, c.HealthM2 * 6 * k * k, c.Armour, seconds: Py.Round(c.Seconds * 2, 1));
        p.Set(("power", cr.Power), ("onBreak", "disable"), ("collider", "boxes"));
        var margin = 0.7 * k;
        p.Set(("lifter", Kw(("kind", "bridge"), ("base", Py.L(Py.Round(k, 3), Py.Round(hi, 3), 0.0)), ("span", cr.Span), ("depth", Py.Round(0.6 * k, 3)),
            ("reach", Py.L(Py.Round(margin, 3), Py.Round(cr.Span - margin, 3))), ("up", 0.0), ("down", cr.Drop), ("payload", cr.Payload), ("piece", Py.L(cr.Across, cr.Long)),
            ("speed", cr.Travel), ("hoist", cr.Hoist), ("handle", cr.Handle))));
        p.Set(("config", Py.L(
            Kw(("key", "liftMode"), ("type", "choice"), ("label", "Works"), ("group", "Lifter"), ("options", Py.L("Auto", "Station only", "Carry only", "Off")), ("default", 0),
                ("tooltip", "Auto: every job under it. Station only: fits parts at Building Stations. Carry only: carries between machines, racks and pads. Off: stands still.")),
            Kw(("key", "carryHeight"), ("label", "Carry height"), ("group", "Lifter"), ("unit", "m"), ("min", 0.0), ("max", Py.Round(cr.Drop, 1)), ("step", 0.1), ("default", Py.Round(cr.Drop, 1)),
                ("tooltip", "How high it hoists a piece over the higher end of its move before it travels. Never past the hook's highest point.")))));
        return p;
    }

    // ---- The Hall Crane (design page drawing 24): Crane Rail Blocks on mast columns and a Crane Core per bridge. --------------------

    /// <summary>
    /// A Crane Rail Block: 2 m of runway girder, one 2 m cell across and along and 1 m deep, for the top of a mast column (the shared Mast
    /// Block). An I-beam (sole plate, bottom flange, web, top flange) with the steel rail on top, whose top is the block's top: the line a
    /// Hall Crane's end trucks run on. Plain for the triangle count: hazard paint on the bottom flange, a conductor bar for the bridge's power on its
    /// +x side, an amber lamp on its -x side that flashes while a bridge passes (ConstructLifters.RailLit). Blocks in line end to end make a
    /// run (ConstructCraneRails.cs); two parallel runs up to 32 m apart make a crane.
    /// </summary>
    private static Part HallRail()
    {
        var p = new Part("Crane Rail Block", Small, "Bridge cranes",
            "2 m of crane runway for the top of a Mast Block column. Lay two parallel runs of them, end to end, up to 32 m apart at one height; a Crane Core bolted beside one run drives a bridge over both. Each block in the runs adds 5 t to what it lifts.",
            role: "frame", color: CraneBase, group: "Production");
        const double e = 0.997, top = 1.0;
        p.Add(Box([-1, 0, -1], [1, top, 1], hidden: true)); // (the collider and the sockets' faces)
        p.Add(Box([-0.62, 0.003, -e], [0.62, 0.05, e], CraneBase)); // (the sole plate on the mast)
        p.Add(Box([-0.46, 0.046, -e + 0.003], [0.46, 0.13, e - 0.003], CraneBase)); // (the bottom flange)
        p.Add(Box([-0.065, 0.126, -e + 0.006], [0.065, 0.824, e - 0.006], CraneWeb)); // (the web)
        p.Add(Box([-0.4, 0.82, -e + 0.003], [0.4, 0.9, e - 0.003], CraneBase)); // (the top flange)
        p.Add(MachineLook.D([-0.085, 0.896, -0.999], [0.085, top, 0.999], CraneSteel, "steel")); // (the rail: its top is the run's line)
        // Kept plain for the triangle count (Sean, 2026-11-23): no stiffeners, splice plates, bolts or rail clips; only details that
        // run the block's whole length (hazard paint, the conductor bar), so a run of blocks reads as one long girder.
        // Hazard paint on the bottom flange's edges: overhead work.
        p.Add(MachineLook.D([-0.472, 0.06, -0.92], [-0.458, 0.12, 0.92], CraneYellow, "hazard paint"));
        p.Add(MachineLook.D([0.458, 0.06, -0.92], [0.472, 0.12, 0.92], CraneYellow, "hazard paint"));
        // The conductor bar on the +x side: one long bracket off the web, a dark carrier and its copper face (the bridge's collector runs on it).
        p.Add(MachineLook.D([0.06, 0.6, -0.97], [0.333, 0.64, 0.97], CraneBase));
        p.Add(MachineLook.D([0.33, 0.57, -0.98], [0.37, 0.65, 0.98], CraneBase));
        p.Add(MachineLook.D([0.37, 0.59, -0.97], [0.384, 0.63, 0.97], "#b87333", "copper"));
        // The lamp on the -x side under the top flange: a dark housing and its amber lens, which flashes while a bridge passes.
        p.Add(MachineLook.D([-0.24, 0.62, -0.09], [-0.069, 0.78, 0.09], CraneBase));
        p.Add(MachineLook.Work(MachineLook.D([-0.27, 0.64, -0.07], [-0.236, 0.76, 0.07], MachineLook.Accent), "glow"));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+z", [0, 0.5, 1], "+z");
        p.Sock("-z", [0, 0.5, -1], "-z");
        p.Sock("+x", [1, 0.5, 0], "+x");
        p.Sock("-x", [-1, 0.5, 0], "-x");
        p.Stats(1400, 3000, 10, 12);
        p.Set(("craneRail", "hall"), ("centreSockets", false), ("collider", "boxes")); // ("hall": a Crane Core's rails, never a Small Crane Bridge's)
        return p;
    }

    /// <summary>
    /// The Crane Core: one Hall Crane bridge's drive, power and controls, a 2 m cabinet bolted beside a Crane Rail Block by its +x socket
    /// (the rail's foot at its middle height, the rail's top level with its own). Everything else of the crane (end trucks, girders, the
    /// trolley, the cab under the girder, the hook) is drawn from the hook's place (LifterRender.DrawCrane). Its span, lift and drop come
    /// from its rails ("hall": ConstructCraneRails.HallWalk). More Cores on the same rails make more bridges, each with its own queue.
    /// </summary>
    private static Part HallCore()
    {
        var p = new Part("Crane Core", Small, "Bridge cranes",
            "A Hall Crane's bridge: bolt it beside a run of Crane Rail Blocks with a second run up to 32 m across. Carries 20 t plus 5 t a rail block between machines, pads and roller runs under it. The rails must clear every machine under them. Needs power while it moves.",
            role: "frame", color: CraneYellow, group: "Production");
        const double e = 0.997, h = 2.0;
        p.Add(Box([-1, 0, -1], [1, h, 1], hidden: true));
        // The cabinet: a dark plinth, the yellow body in two stacked sections with a dark band between, a lid with a lip.
        p.Add(Box([-e, 0.003, -e], [e, 0.14, e], CraneBase));
        p.Add(Box([-0.94, 0.136, -0.94], [0.94, 1.02, 0.94], CraneYellow));
        p.Add(Box([-0.955, 1.016, -0.955], [0.955, 1.07, 0.955], CraneBase));
        p.Add(Box([-0.94, 1.066, -0.94], [0.94, 1.84, 0.94], CraneYellow));
        p.Add(Box([-0.97, 1.836, -0.97], [0.97, 1.9, 0.97], CraneBase));
        MachineLook.Hazard(p, [-0.985, 0.03, -0.985], [0.985, 0.11, -0.95]);
        MachineLook.Hazard(p, [-0.985, 0.03, 0.95], [0.985, 0.11, 0.985]);
        // The -x face (the aisle): the screen over a door, its handle and hinges; the button shelf under the screen.
        p.Add(MachineLook.D([-0.952, 0.2, -0.7], [-0.94, 0.98, 0.7], CraneSteel, "steel")); // (the lower door)
        p.Add(MachineLook.D([-0.972, 0.5, 0.5], [-0.952, 0.7, 0.56], CraneBase)); // (its handle)
        foreach (var y in new[] { 0.3, 0.88 })
            p.Add(MachineLook.D([-0.968, y - 0.04, -0.68], [-0.952, y + 0.04, -0.62], CraneBase)); // (its hinges)
        double sx = -1.004, sy = 1.48;
        p.Add(MachineLook.D([sx - 0.006, sy - 0.24, -0.5], [sx + 0.06, sy + 0.24, 0.5], CraneBase)); // (the screen's bezel)
        p.Add(MachineLook.D([sx - 0.012, sy - 0.2, -0.45], [sx - 0.004, sy + 0.2, 0.45], Screen));
        p.Set(("panels", Py.L(Kw(("source", "lifter"), ("at", Py.L(Py.Round(sx - 0.015, 3), sy, 0.0)), ("size", Py.L(0.9, 0.4)),
            ("face", "-x"), ("up", "+y"), ("rows", 6), ("range", 14.0)))));
        p.Add(MachineLook.D([-1.06, 1.12, -0.45], [-0.94, 1.17, 0.45], CraneBase)); // (the button shelf)
        foreach (var z in new[] { -0.3, -0.1, 0.1 })
            p.Add(MachineLook.D([-1.05, 1.17, z - 0.04], [-1.0, 1.2, z + 0.04], MachineLook.Accent));
        p.Add(MachineLook.D([-1.05, 1.17, 0.27], [-0.99, 1.215, 0.37], MachineLook.Stop));
        // The -z face: KEEP CLEAR (a pale plate with dark lettering bars inside a hazard border) and louvres.
        p.Add(MachineLook.D([-0.6, 1.3, -0.956], [0.6, 1.7, -0.94], CraneBase));
        MachineLook.Hazard(p, [-0.58, 1.32, -0.968], [0.58, 1.68, -0.954]);
        p.Add(MachineLook.D([-0.5, 1.38, -0.98], [0.5, 1.62, -0.966], MachineLook.In));
        foreach (var (y, w) in new[] { (1.55, 0.36), (1.45, 0.42) })
            p.Add(MachineLook.D([-w, y - 0.03, -0.992], [w, y + 0.03, -0.978], MachineLook.Ink));
        foreach (var y in new[] { 0.3, 0.42, 0.54, 0.66, 0.78 })
            p.Add(MachineLook.D([-0.5, y, -0.96], [0.5, y + 0.05, -0.94], CraneBase));
        // The +z face: the main isolator (a grey box, a red handle) and louvres.
        p.Add(MachineLook.D([-0.3, 1.25, 0.94], [0.3, 1.7, 1.0], "#8d9399"));
        p.Add(MachineLook.D([-0.06, 1.38, 1.0], [0.06, 1.6, 1.05], MachineLook.Stop));
        foreach (var y in new[] { 0.3, 0.42, 0.54, 0.66, 0.78 })
            p.Add(MachineLook.D([-0.5, y, 0.94], [0.5, y + 0.05, 0.96], CraneBase));
        // The +x face against the rail: the drive's conduit up to the rail's conductor bar, and a bolted flange where it bolts on.
        p.Add(MachineLook.D([0.94, 1.1, -0.5], [0.985, 1.9, 0.5], CraneBase));
        foreach (var (y, z) in new[] { (1.2, -0.4), (1.2, 0.4), (1.8, -0.4), (1.8, 0.4) })
            p.Add(MachineLook.D([0.985, y - 0.025, z - 0.025], [0.997, y + 0.025, z + 0.025], CraneSteel, "steel"));
        // On the lid: the drive motor and gearbox, the festoon's cable drum, the beacon. Everything stays under the rail's top line (2 m).
        p.Add(MachineLook.D([-0.7, 1.9, -0.6], [0.1, 1.98, 0.2], CraneBase)); // (the drive's bed)
        p.Add(MachineLook.D([-0.6, 1.9, -0.5], [-0.1, 1.995, 0.1], "#3b4148", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+z")))); // (the motor)
        p.Add(MachineLook.D([0.2, 1.9, -0.7], [0.8, 1.995, -0.1], CraneSteel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 14), ("front", "+x")))); // (the cable drum)
        p.Add(MachineLook.D([0.3, 1.9, 0.4], [0.5, 1.95, 0.6], CraneBase)); // (the beacon's foot)
        p.Add(MachineLook.Work(MachineLook.D([0.33, 1.95, 0.43], [0.47, 1.995, 0.57], MachineLook.Accent), "glow"));
        p.Add(MachineLook.D([-0.85, 0.2, 0.94], [-0.55, 0.36, 1.0], MachineLook.In)); // (the maker's plate)
        p.Add(MachineLook.D([-0.82, 0.25, 1.0], [-0.58, 0.31, 1.012], MachineLook.Ink));
        p.Sock("+x", [1, 1.5, 0], "+x"); // (onto a Crane Rail Block's -x: the rail's foot at this cabinet's middle height)
        p.Sock("-x", [-1, 1, 0], "-x");
        p.Sock("+z", [0, 1, 1], "+z");
        p.Sock("-z", [0, 1, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(2200, 3600, 10, 16);
        p.Set(("power", 12000), ("onBreak", "disable"), ("centreSockets", false), ("collider", "boxes"));
        p.Set(("lifter", Kw(("kind", "bridge"), ("hall", true), ("base", Py.L(2.0, 2.0, 0.0)), ("span", 32.0), ("depth", 1.6),
            ("reach", Py.L(1.0, 31.0)), ("up", 0.0), ("down", 8.0), ("payload", 20000), ("piece", Py.L(30.0, 64.0)),
            ("speed", 1.5), ("hoist", 0.75), ("handle", 3.0))));
        p.Set(("config", Py.L(
            Kw(("key", "liftMode"), ("type", "choice"), ("label", "Works"), ("group", "Lifter"), ("options", Py.L("Auto", "Station only", "Carry only", "Off")), ("default", 0),
                ("tooltip", "Auto: every job under it. Station only: fits parts at Building Stations. Carry only: carries between machines, pads and roller runs. Off: stands still.")),
            Kw(("key", "carryHeight"), ("label", "Carry height"), ("group", "Lifter"), ("unit", "m"), ("min", 0.0), ("max", 32.0), ("step", 0.1), ("default", 1.5),
                ("tooltip", "How high it hoists a piece over the higher end of its move before it travels. Never past the hook's highest point.")))));
        return p;
    }

    // ---- The Lifter Cab (arms-and-cranes.md 3.7, phase 8): a seat that steers one lifter by hand. ----------------------------------

    /// <summary>
    /// The Lifter Cab: a small enclosed chair on a swivel with a window, one cell wide and two deep (0.5 by 0.5 by 1 m; its seat 0.3 m up).
    /// Stood on the same construct as a lifter, it steers the nearest one ("lifterCab": ConstructLifterCab.cs): the move keys move the
    /// hook, Space and Ctrl raise and lower it, F grabs and sets down, E leaves.
    /// </summary>
    private static Part Cab()
    {
        var c = Small;
        var p = new Part("Lifter Cab", c, "Lifters",
            "A cab that steers the nearest Loader Arm or Bridge Crane on its construct by hand: move keys move the hook, Space and Ctrl raise and lower it, F grabs a loose piece or a point's piece under it and sets it down, E gets out. While it is manned the lifter does nothing else.",
            role: "seat", tool: "wrench", color: CraneYellow, group: "Production");
        // 0.75 m wide (x), 1.5 m high, 1 m deep (z; the window is its -z face): three, six and four cells.
        double x0 = c.Lo(), x1 = c.Hi(3), y0 = c.Lo(), y1 = c.Hi(6), z0 = c.Lo(), z1 = c.Hi(4);
        var whole = Box([x0, y0, z0], [x1, y1, z1], CraneBase)!;
        whole["hidden"] = true; // (the collider and the sockets' faces)
        p.Add(whole);
        var fl = y0 + 0.16; // (the floor's top)
        p.Add(Box([x0 + 0.02, y0, z0 + 0.02], [x1 - 0.02, y0 + 0.08, z1 - 0.02], CraneBase)); // (the swivel's foot)
        p.Add(MachineLook.D([x0 + 0.16, y0 + 0.08, z0 + 0.3], [x1 - 0.16, y0 + 0.12, z1 - 0.3], CraneSteel, "steel")); // (the swivel ring)
        p.Add(Box([x0 + 0.03, y0 + 0.118, z0 + 0.03], [x1 - 0.03, fl, z1 - 0.03], CraneYellow)); // (the floor)
        p.Add(Box([x0 + 0.03, fl - 0.002, z1 - 0.1], [x1 - 0.03, y1 - 0.06, z1 - 0.03], CraneYellow)); // (the back wall)
        p.Add(Box([x0 + 0.03, fl - 0.002, z0 + 0.03], [x0 + 0.08, fl + 0.5, z1 - 0.102], CraneYellow)); // (the side walls' lower halves)
        p.Add(Box([x1 - 0.08, fl - 0.002, z0 + 0.03], [x1 - 0.03, fl + 0.5, z1 - 0.102], CraneYellow));
        p.Add(Box([x0 + 0.03, fl - 0.002, z0 + 0.03], [x1 - 0.03, fl + 0.3, z0 + 0.08], CraneYellow)); // (the front's sill)
        p.Add(Box([x0 + 0.03, y1 - 0.062, z0 + 0.03], [x1 - 0.03, y1 - 0.01, z1 - 0.03], CraneBase)); // (the roof)
        foreach (var (a, b) in new[] { (x0 + 0.03, z0 + 0.03), (x1 - 0.08, z0 + 0.03) }) // (the front posts)
            p.Add(Box([a, fl + 0.298, b], [a + 0.05, y1 - 0.06, b + 0.05], CraneYellow));
        p.Add(Glass(MachineLook.D([x0 + 0.04, fl + 0.497, z0 + 0.09], [x0 + 0.07, y1 - 0.07, z1 - 0.11], "#9fc4d6"))); // (the side windows)
        p.Add(Glass(MachineLook.D([x1 - 0.07, fl + 0.497, z0 + 0.09], [x1 - 0.04, y1 - 0.07, z1 - 0.11], "#9fc4d6")));
        p.Add(Glass(MachineLook.D([x0 + 0.09, fl + 0.297, z0 + 0.04], [x1 - 0.09, y1 - 0.07, z0 + 0.07], "#9fc4d6"))); // (the front window, -z)
        p.Add(MachineLook.D([x0 + 0.17, fl - 0.003, z1 - 0.5], [x1 - 0.17, fl + 0.4, z1 - 0.12], "#2f343a")); // (the chair's seat)
        p.Add(MachineLook.D([x0 + 0.17, fl + 0.397, z1 - 0.22], [x1 - 0.17, fl + 0.95, z1 - 0.104], "#2f343a")); // (its back)
        p.Add(MachineLook.D([x0 + 0.12, fl - 0.003, z0 + 0.1], [x1 - 0.12, fl + 0.55, z0 + 0.3], CraneSteel, "steel")); // (the control desk)
        p.Add(MachineLook.D([x0 + 0.2, fl + 0.547, z0 + 0.2], [x0 + 0.23, fl + 0.7, z0 + 0.23], MachineLook.Accent)); // (the joysticks)
        p.Add(MachineLook.D([x1 - 0.23, fl + 0.547, z0 + 0.2], [x1 - 0.2, fl + 0.7, z0 + 0.23], MachineLook.Accent));
        MachineLook.Hazard(p, [x0 + 0.006, y0 + 0.01, z0 + 0.06], [x0 + 0.02, y0 + 0.07, z1 - 0.06]);
        MachineLook.Hazard(p, [x1 - 0.02, y0 + 0.01, z0 + 0.06], [x1 - 0.006, y0 + 0.07, z1 - 0.06]);
        BlockSockets(p, 3, 6, 4, faces: "y", skip: ["+y"]);
        p.Stats(60, 160, c.Armour, seconds: Py.Round(c.Seconds, 1));
        p.Set(("seat", Kw(("at", Py.L(Py.Round((x0 + x1) / 2, 3), Py.Round(fl + 0.4, 3), Py.Round(z1 - 0.32, 3))))), ("lifterCab", true), ("collider", "boxes"));
        return p;
    }

    private static PyDict? Glass(PyDict? b)
    {
        if (b != null)
            b["glass"] = true;
        return b;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, Enumerable.Range(1, 5).Select(t => ("", Arm(t))).Append(("", Cab()))
        .Concat(new[] { Medium, Large, Capital }.SelectMany(c => new[] { ("Cranes", Rail(c)), ("Cranes", Bridge(c)) })) // (the classed cranes stay: the Medium for the Dray's hold, the Large and Capital rows because saved Assemblers keep recipe numbers; the hall's is the Hall Crane: drawing 24)
        .Concat(new[] { ("HallCranes", HallRail()), ("HallCranes", HallCore()) })
        .Concat(new[] { ("SmallCranes", Rail(Small)), ("SmallCranes", Bridge(Small)) })); // (a folder of their own, so their Assembling rows go at the end: Assembling.AfterCloche)
}
