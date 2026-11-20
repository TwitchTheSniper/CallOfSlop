using static CallOfSlop.PartKit.PartKitMath;
using K = CallOfSlop.PartKit.Generators.FittingLookParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The commoners' style, drafts for a copy of the Part Style Sheet (Karl, 2026-11-20: "winnebago vibe. Think space commoner,
/// they're just trying to get by, it looks as though it's way past its expiry date and from an older generation"). One draft
/// of each of 25 kinds of ship equipment (the sheet's step 4, "the set"), for feedback one by one. Not merged; nothing here is in
/// the Parts tab (group StyleCommoner is unlisted, Constructs/Categories.json). Shown by --preview-gallery DIR --group StyleCommoner.
///
/// The look: an old camper's cream and tan with faded brown, orange and mustard stripes, wood-grain laminate, dull chrome,
/// tired vinyl, grey tape over the cracks, patches riveted on in another colour, rust where water sat. Lamps are warm
/// incandescent and few. Modules keep the standard Module Plug (FittingLookParts.Reactor, module-sockets.md): 1x1, the
/// magazine 2x2. Free-standing pieces: front +z, the wall behind -z, the floor at y 0.
/// </summary>
public sealed partial class CommonerStyleParts : IPartGenerator
{
    public string Name => "style_commoner";

    private const string Out = "Constructs/Parts/StylesCommoner";
    internal const string Group = "StyleCommoner";
    private const string Section = "Commoner style";

    // The camper palette, all faded.
    private const string Cream = "#d6c9a8", Beige = "#b5a37f", Tan = "#8c7654", Brown = "#5b3f28", Orange = "#b25a2c", Mustard = "#c2962f";
    private const string Avocado = "#6d763c", Rust = "#7a4a2c", Wood = "#7b5634", WoodDark = "#5a3d24", Tape = "#8b8c88", Chrome = "#a3a199";
    private const string Rubber = "#2a2826", Grime = "#3b3631", Vinyl = "#6b4a33", Olive = "#565b3a", Teal = "#4f7c76", BottleGreen = "#3e5a3a";
    private const string Bulb = "#ffc978", PilotRed = "#d9533b", PilotGreen = "#9fd46a", Phosphor = "#8fd88a", Ceramic = "#e4ddcc";

    // ---- Shapes

    /// <summary>A free-standing piece: a hidden solid (what collides), a floor socket and a wall socket.</summary>
    private static K.Kit Free(string name, string desc, double[] mn, double[] mx, string role = "frame", bool wall = true, string material = "steel")
    {
        var p = new Part(name, Small, Section, desc, role: role, tool: "wrench", material: material, color: Cream, group: Group);
        p.Set(("finish", "worn paint"), ("collider", "boxes"));
        p.Add(Box(mn, mx, Cream, hidden: true));
        p.Sock("bottom", [R(0.5 * (mn[0] + mx[0])), mn[1], R(0.5 * (mn[2] + mx[2]))], "-y");
        if (wall)
            p.Sock("wall", [R(0.5 * (mn[0] + mx[0])), R(mn[1] + Math.Min(mx[1] - mn[1], 0.25) / 2), mn[2]], "-z");
        p.Stats(40, 200, 2, 2.0);
        return new K.Kit(p);
    }

    private static double R(double v) => Py.Round(v, 4);

    /// <summary>A frustum along an axis (0 x, 1 y, 2 z) from t0 (radius r0) to t1 (radius r1), centred on (a, b) in the other two axes.</summary>
    private static IEnumerable<double[]> Frustum(int axis, double a, double b, double t0, double r0, double t1, double r1, int sides = 10, double turn = 0.5)
    {
        foreach (var (t, r) in new[] { (t0, r0), (t1, r1) })
            for (var i = 0; i < sides; i++)
            {
                var ang = (i + turn) * 2 * Math.PI / sides;
                double u = a + r * Math.Cos(ang), v = b + r * Math.Sin(ang);
                yield return axis switch { 0 => [t, u, v], 1 => [u, t, v], _ => [u, v, t] };
            }
    }

    private static void Tube(K.Kit k, int axis, double a, double b, double t0, double t1, double r, string col, string finish, int sides = 8) =>
        k.Hull(Frustum(axis, a, b, t0, r, t1, r, sides), col, finish);

    /// <summary>A strip of grey tape over a crack: flat on a face whose normal is +z at z, from (x0, y0) to (x1, y1), 45 mm wide.</summary>
    private static void TapeZ(K.Kit k, double z, double x0, double y0, double x1, double y1, double w = 0.045)
    {
        double dx = x1 - x0, dy = y1 - y0, l = Math.Sqrt(dx * dx + dy * dy), nx = -dy / l * w / 2, ny = dx / l * w / 2;
        k.Hull([[x0 + nx, y0 + ny, z], [x0 - nx, y0 - ny, z], [x1 + nx, y1 + ny, z], [x1 - nx, y1 - ny, z],
                [x0 + nx, y0 + ny, z + 0.011], [x0 - nx, y0 - ny, z + 0.011], [x1 + nx, y1 + ny, z + 0.011], [x1 - nx, y1 - ny, z + 0.011]], Tape, "fabric");
    }

    /// <summary>A row of rivet heads along x at height y on the +z face at z.</summary>
    private static void RivetsX(K.Kit k, double x0, double x1, double y, double z, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var x = x0 + (x1 - x0) * i / Math.Max(1, n - 1);
            k.Box(x - 0.008, y - 0.008, z, x + 0.008, y + 0.008, z + 0.011, Chrome, "steel");
        }
    }

    /// <summary>A riveted patch plate on the +z face at z, in another colour than what it covers.</summary>
    private static void PatchZ(K.Kit k, double x0, double y0, double x1, double y1, double z, string col)
    {
        k.Box(x0, y0, z, x1, y1, z + 0.012, col, "rusty paint");
        RivetsX(k, x0 + 0.02, x1 - 0.02, y0 + 0.02, z + 0.012, 3);
        RivetsX(k, x0 + 0.02, x1 - 0.02, y1 - 0.02, z + 0.012, 3);
    }

    /// <summary>A round dial gauge facing +z: rim, face and a needle.</summary>
    private static void DialZ(K.Kit k, double x, double y, double z, double r, double needle = 0.6)
    {
        k.Hull(Frustum(2, x, y, z, r, z + 0.03, r, 10), Chrome, "chrome");
        k.Hull(Frustum(2, x, y, z + 0.03, r * 0.82, z + 0.036, r * 0.82, 10), Ceramic, "plastic");
        var a = Math.PI * (1.25 - 1.5 * needle);
        double ex = x + Math.Cos(a) * r * 0.7, ey = y + Math.Sin(a) * r * 0.7;
        k.Hull([[x - 0.004, y, z + 0.036], [x + 0.004, y, z + 0.036], [ex, ey, z + 0.036], [x - 0.004, y, z + 0.048], [x + 0.004, y, z + 0.048], [ex, ey, z + 0.048]], Brown, "plastic");
    }

    /// <summary>A small warm pilot lamp in a chrome bezel facing +z.</summary>
    private static void PilotZ(K.Kit k, double x, double y, double z, string col, double r = 0.016)
    {
        k.Hull(Frustum(2, x, y, z, r * 1.5, z + 0.014, r * 1.5, 8), Chrome, "chrome");
        k.Hull(Frustum(2, x, y, z + 0.014, r, z + 0.026, r * 0.8, 8), col, "", glow: true);
    }

    /// <summary>A toggle switch facing +z: a plate and a lever thrown up or down.</summary>
    private static void ToggleZ(K.Kit k, double x, double y, double z, bool up)
    {
        k.Box(x - 0.016, y - 0.024, z, x + 0.016, y + 0.024, z + 0.011, Chrome, "steel");
        var t = up ? 0.035 : -0.035;
        k.Hull([[x - 0.006, y, z + 0.011], [x + 0.006, y, z + 0.011], [x - 0.006, y + 0.012 * Math.Sign(t), z + 0.011], [x + 0.006, y + 0.012 * Math.Sign(t), z + 0.011],
                [x - 0.004, y + t, z + 0.045], [x + 0.004, y + t, z + 0.045], [x - 0.004, y + t * 0.8, z + 0.05], [x + 0.004, y + t * 0.8, z + 0.05]], Chrome, "chrome");
    }

    /// <summary>The camper's stripes: three bands across a +z face at z, brown, orange and mustard, from y upward.</summary>
    private static void StripesZ(K.Kit k, double x0, double x1, double y, double z, double band = 0.035)
    {
        string[] cols = [Brown, Orange, Mustard];
        for (var i = 0; i < 3; i++)
            k.Box(x0, y + i * (band + 0.006), z, x1, y + i * (band + 0.006) + band, z + 0.011, cols[i], "worn paint");
    }

    private static void StripesX(K.Kit k, double x, double z0, double z1, double y, double sx, double band = 0.035)
    {
        string[] cols = [Brown, Orange, Mustard];
        for (var i = 0; i < 3; i++)
            k.Box(Math.Min(x, x + sx * 0.011), y + i * (band + 0.006), z0, Math.Max(x, x + sx * 0.011), y + i * (band + 0.006) + band, z1, cols[i], "worn paint");
    }

    /// <summary>A rust run under a seam: a thin streak down a +z face.</summary>
    private static void StreakZ(K.Kit k, double x, double y0, double y1, double z, double w = 0.03) =>
        k.Hull([[x - w / 2, y1, z], [x + w / 2, y1, z], [x - w * 0.15, y0, z], [x + w * 0.15, y0, z],
                [x - w / 2, y1, z + 0.011], [x + w / 2, y1, z + 0.011], [x - w * 0.15, y0, z + 0.011], [x + w * 0.15, y0, z + 0.011]], Rust, "rust");

    /// <summary>20. The ladder: a steel ladder, its rungs worn bright, one replaced in another colour, grip tape, hooks at the top.</summary>
    private static Part Ladder()
    {
        var k = Free("Commoner Ladder", "Commoner ladder (draft): a steel ladder off something else, olive paint worn to bare metal where hands and boots go, one rung replaced with a length of pipe, grip tape wound round the stiles and two hooks over the hatch rim at the top.", [-0.25, 0, -0.25], [0.25, 2.5, -0.05]);
        foreach (var sx in new[] { -0.22, 0.22 })
        {
            k.Box(sx - 0.025, 0.0, -0.17, sx + 0.025, 2.45, -0.1, Olive, "worn paint");
            k.Box(sx - 0.03, 1.0, -0.175, sx + 0.03, 1.3, -0.095, Rubber, "fabric"); // (grip tape)
            k.Box(sx - 0.025, 0.15, -0.25, sx + 0.025, 0.2, -0.17, Grime, "steel"); // (stand-off brackets)
            k.Box(sx - 0.025, 2.0, -0.25, sx + 0.025, 2.05, -0.17, Grime, "steel");
            k.Hull([[sx - 0.02, 2.45, -0.17], [sx + 0.02, 2.45, -0.17], [sx - 0.02, 2.45, -0.1], [sx + 0.02, 2.45, -0.1], [sx - 0.02, 2.5, -0.22], [sx + 0.02, 2.5, -0.22], [sx - 0.02, 2.53, -0.18], [sx + 0.02, 2.53, -0.18]], Grime, "steel"); // (the hook)
        }
        for (var i = 0; i < 8; i++)
        {
            var y = 0.25 + i * 0.29;
            if (i == 3)
                Tube(k, 0, y, -0.135, -0.198, 0.198, 0.017, "#8a8f94", "steel", 6); // (the replaced rung: a pipe)
            else
            {
                k.Box(-0.196, y - 0.016, -0.16, 0.196, y + 0.016, -0.11, Olive, "worn paint");
                k.Box(-0.12, y + 0.016, -0.155, 0.12, y + 0.027, -0.115, "#8f9086", "steel"); // (worn bright on top)
            }
        }
        return k.P;
    }

    /// <summary>21. The door: a camper-style interior door, aluminium frame, wood-grain panel, a louvred lower half, a lever, a scuffed kick plate.</summary>

    public void Make(PartOutput o)
    {
        o.Own(Out);
        var all = new List<Part> { Ladder() }; // (Uncategorised: liked, but too nice for the commoner look)
        all.AddRange(Set2()); // (round 2: the parts Karl kept)
        all.AddRange(Set3()); // (round 3: usable gameplay items)
        foreach (var p in all)
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
