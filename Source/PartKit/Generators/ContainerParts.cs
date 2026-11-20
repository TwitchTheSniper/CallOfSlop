using CallOfSlop.PartKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Standard cargo containers, a draft for the Animation gallery (Karl, 2026-11-20: "Cargo containers follow the sizes of socket items. So a
/// container ... will hold 3 2 x 2 x 2 parts ... a hatch spanning one side which can open and see the parts. If the hatch is closed, the
/// parts aren't rendered"). One standard any power's ships carry; loaded and unloaded whole (a crane's hook, a robot shell) and emptied by
/// NPCs once ashore.
///
/// <b>The rule.</b> A container of size N (1, 2 or 3 m: the module socket standard's whole metres) holds three N x N x N slots in a row.
/// Outside it is N + 0.5 m across and high and 3N + 0.5 m long (2 m: 2.5 x 2.5 x 6.5 m), so 0.25 m a side for the frame, the
/// clearance and the hatch. Each slot is a <b>Container Cradle</b> N x N on the floor: a Module Stand that lies flat (a module on it is
/// stored, nothing couples), so a container carries modules (a plug with anything welded on) the way a rack does, and a Module Fitter
/// or a hand takes them off it the same way.
///
/// <b>The hatch</b> is the whole +x long side, one leaf hinged along its top edge: it swings out and up 90 degrees to a flat awning over
/// the slots, never higher than the roof, so containers stacked or racked side by side still open, and a shell or a fitter reaches every
/// slot from the side. Not yet in the game: the contents not drawn while it is shut (the plan is a body flag set by the hatch's door
/// state, read by the renderer: parts on a cradle of a shut container are skipped).
///
/// Frame, shared by every part: origin the roof's middle (a crane's hook takes it there, as the Dray's container), y down to -H at the
/// feet, x across (+x the hatch side), z along. Writes Constructs/Parts/Containers (group Style: unlisted and no recipe while a draft) and
/// a loaded draft blueprint per size beside its timing file in tools/preview/anim.
/// </summary>
public sealed class ContainerParts : IPartGenerator
{
    public string Name => "containers";

    private const string Out = "Constructs/Parts/Containers";
    private const string AnimOut = "tools/preview/anim";
    private const string Section = "Cargo containers";
    private const string Group = StyleSheetParts.Group; // (unlisted while a draft: Constructs/Categories.json)

    // Paint: the Traders' corporate blue shell (paintable), a dark frame, lanyard yellow at what moves, a pale lit inside.
    private const string Shell = "#2a64a8", ShellRib = "#245893", FrameC = "#2b2f35", Casting = "#1e2125", Hazard = "#f2c400", HazardDark = "#1b1714",
        Inside = "#c9ccc8", Floor = "#5c6168", Paper = "#e6e2d6", Lamp = "#ffe2a0", Green = "#5ad070", Steel = "#7c838c",
        CradleC = "#3d444c", CradleRim = "#d8a320";

    private const double F = 0.15; // the frame: corner posts, rails, the floor's depth
    private const double Gap = 0.05; // between two slots along the container

    private static int N;
    private static double H, W2, L2, FloorY; // its height, half width, half length, the floor's top

    private static void Size(int n) => (N, H, W2, L2, FloorY) = (n, n + 0.5, (n + 0.5) / 2, (3 * n + 0.5) / 2, -(n + 0.5) + F);

    public static string ContainerName(int n) => $"Standard Container {n}m";
    public static string HatchName(int n) => $"Standard Container {n}m Hatch";
    public static string CradleName(int n) => $"Container Cradle {n}x{n}";

    /// <summary>A slot's middle along z.</summary>
    private static double SlotZ(int k) => (k - 1) * (N + Gap);

    private static FittingLookParts.Kit K(string name, string role, string desc, Cls cls, string color = Shell)
    {
        var p = new Part(name, cls, Section, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);
        p.Set(("finish", "painted plating"), ("collider", "boxes")); // (the hidden boxes collide; the looks don't)
        return new FittingLookParts.Kit(p);
    }

    private static void Solid(FittingLookParts.Kit k, double x0, double y0, double z0, double x1, double y1, double z1) =>
        k.P.Add(Box([Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)], hidden: true));

    private static void Rod(FittingLookParts.Kit k, double x0, double y0, double z0, double x1, double y1, double z1, string axis, string col, string finish, int seg = 10) =>
        k.P.Add(Box([Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)], col, detail: true,
            shape: Kw(("shape", "cylinder"), ("segments", seg), ("front", axis), ("finish", finish), ("paintable", false))));

    /// <summary>A flat bar from (a1, b1) to (a2, b2) in a plane: <paramref name="plane"/> "x" (the bar in y-z at x from c0 to c1), "y" (x-z) or "z" (x-y).</summary>
    private static void Brace(FittingLookParts.Kit k, string plane, double c0, double c1, double a1, double b1, double a2, double b2, double width, string col, string finish)
    {
        double da = a2 - a1, db = b2 - b1, len = Math.Sqrt(da * da + db * db), na = -db / len * width / 2, nb = da / len * width / 2;
        var corners = new[] { (a1 + na, b1 + nb), (a1 - na, b1 - nb), (a2 + na, b2 + nb), (a2 - na, b2 - nb) };
        double[] P(double a, double b, double c) => plane switch { "x" => [c, a, b], "y" => [a, c, b], _ => [a, b, c] };
        k.Hull(corners.Select(q => P(q.Item1, q.Item2, c0)).Concat(corners.Select(q => P(q.Item1, q.Item2, c1))), col, finish);
    }

    // ---- the container: frame, floor, roof, the back side and the two ends; the hatch hangs on its +x side

    private static Part Container()
    {
        var k = K(ContainerName(N), "frame",
            $"A standard {N} m cargo container: three {N}x{N}x{N} m slots in a row, each a cradle a module stands on, and a hatch the whole length of each side that swings up to an awning. A crane's hook takes it by its roof; it stands on its four corner castings, and clamps at its feet lock it onto a container under it.",
            Medium);
        double w = W2, l = L2, y0 = -H;
        // Solids: the floor, the roof and the ends (both long sides are open: each hatch's own leaf closes its side).
        Solid(k, -w, y0, -l, w, FloorY, l);
        Solid(k, -w, -F + 0.03, -l, w, 0, l);
        Solid(k, -w, FloorY, -l, w, -F + 0.03, -l + 0.1);
        Solid(k, -w, FloorY, l - 0.1, w, -F + 0.03, l);
        // The frame: corner posts, then the top and bottom rails along each side and across each end, dark steel; the castings at the
        // eight corners stand 3 mm proud (twist-lock pockets on their outer faces).
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                k.Box(sx * (w - F), y0 + 0.17, sz * (l - F), sx * (w - 0.004), -0.17, sz * (l - 0.004), FrameC, "dirty steel");
        foreach (var sx in new[] { -1, 1 })
        {
            k.Box(sx * (w - F), y0 + 0.004, -l + 0.17, sx * (w - 0.004), y0 + F + 0.02, l - 0.17, FrameC, "dirty steel"); // (bottom side rail)
            k.Box(sx * (w - F), -F, -l + 0.17, sx * (w - 0.004), -0.004, l - 0.17, FrameC, "dirty steel"); // (top side rail)
        }
        foreach (var sz in new[] { -1, 1 })
        {
            k.Box(-w + 0.17, y0 + 0.004, sz * (l - F), w - 0.17, y0 + F + 0.02, sz * (l - 0.004), FrameC, "dirty steel");
            k.Box(-w + 0.17, -F, sz * (l - F), w - 0.17, -0.004, sz * (l - 0.004), FrameC, "dirty steel");
        }
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                foreach (var (ya, yb) in new[] { (y0, y0 + 0.17), (-0.17, 0.0) })
                {
                    k.Box(sx * (w - 0.17), ya, sz * (l - 0.17), sx * (w + 0.003), yb, sz * (l + 0.003), Casting, "steel");
                    // (the pocket on the side face and the end face: dark slots a twist lock goes into)
                    var ym = (ya + yb) / 2;
                    k.Box(sx * (w + 0.003), ym - 0.035, sz * (l - 0.12), sx * (w + 0.009), ym + 0.035, sz * (l - 0.05), HazardDark, "steel");
                }
        // Each end: a recessed panel with an X brace across it, and on the +z end the data plate and the slot lamps' repeater.
        foreach (var sz in new[] { -1, 1 })
        {
            k.Paint(-w + F, y0 + F + 0.02, sz * (l - 0.1), w - F, -F, sz * (l - 0.07), Shell);
            double za = sz * (l - 0.07), zb = sz * (l - 0.035);
            Brace(k, "z", Math.Min(za, zb), Math.Max(za, zb), -w + F + 0.05, y0 + F + 0.07, w - F - 0.05, -F - 0.05, 0.1, FrameC, "dirty steel");
            Brace(k, "z", Math.Min(za, zb) + sz * 0.003, Math.Max(za, zb) + sz * 0.003, -w + F + 0.05, -F - 0.05, w - F - 0.05, y0 + F + 0.07, 0.1, FrameC, "dirty steel"); // (3 mm prouder: the bars cross)
        }
        double ze = l - 0.035;
        k.Paint(-w + F + 0.06, -F - 0.42, ze, -w + F + 0.5, -F - 0.12, ze + 0.012, Paper); // (the data plate: owner, size, mass)
        for (var i = 0; i < 4; i++)
            k.Box(-w + F + 0.1, -F - 0.38 + i * 0.065, ze + 0.012, -w + F + 0.1 + (i == 0 ? 0.32 : 0.22), -F - 0.36 + i * 0.065, ze + 0.018, HazardDark, "plastic");
        // The roof: plating between the rails, an X brace either half, two lifting eyes and the hook's pad in the middle.
        k.Paint(-w + F, -F + 0.03, -l + F, w - F, -0.03, l - F, Shell);
        foreach (var sz in new[] { -1, 1 })
        {
            double z0 = sz > 0 ? 0.35 : -l + F + 0.05, z1 = sz > 0 ? l - F - 0.05 : -0.35;
            Brace(k, "y", -0.03, -0.012, -w + F + 0.05, z0, w - F - 0.05, z1, 0.09, FrameC, "dirty steel");
            Brace(k, "y", -0.03, -0.009, -w + F + 0.05, z1, w - F - 0.05, z0, 0.09, FrameC, "dirty steel");
        }
        k.Box(-0.3, -0.03, -0.3, 0.3, -0.008, 0.3, Hazard, "hazard paint"); // (the hook's pad)
        k.Box(-0.22, -0.008, -0.22, 0.22, -0.002, 0.22, HazardDark, "steel");
        foreach (var sz in new[] { -1, 1 })
        {
            k.Box(-0.08, -0.03, sz * 0.5 - 0.06, 0.08, -0.004, sz * 0.5 + 0.06, FrameC, "steel"); // (a lifting eye, sunk in the roof either side of the hook's pad: nothing stands above the roof, so a container stacks on it)
        }
        // Each hatch side's sill: hazard stripes the hatch shuts onto.
        var stripes = (int)Math.Round((2 * l - 2 * F) / 0.4);
        foreach (var sx in new[] { -1, 1 })
            for (var i = 0; i < stripes; i++)
            {
                double z = -l + F + i * (2 * l - 2 * F) / stripes, z1 = z + (2 * l - 2 * F) / stripes - 0.004;
                k.Box(sx * (w - F + 0.004), y0 + F + 0.02 - 0.06, z, sx * (w - 0.004), y0 + F + 0.02 - 0.012, z1, i % 2 == 0 ? Hazard : HazardDark, "hazard paint");
            }
        // Inside: the floor (tread plate), the ends' lining, a lamp strip under the roof per slot.
        k.Box(-w + F, FloorY - 0.02, -l + 0.1, w - F, FloorY, l - 0.1, Floor, "tread plate");
        foreach (var sz in new[] { -1, 1 })
            k.Paint(-w + F, FloorY, sz * (l - 0.12), w - F, -F + 0.03, sz * (l - 0.1), Inside, "wall panelling");
        for (var s = 0; s < 3; s++)
        {
            var z = SlotZ(s);
            k.Glow(-0.25 * N, -F + 0.012, z - 0.06, 0.25 * N, -F + 0.03, z + 0.06, Lamp);
            k.Box(-0.25 * N - 0.03, -F + 0.02, z - 0.08, 0.25 * N + 0.03, -F + 0.03, z + 0.08, FrameC, "steel");
            // The slot's floor marks: an L of hazard paint at each corner of its footprint, a tie-down ring each side of it.
            double h = N / 2.0 + 0.025;
            foreach (var cx in new[] { -1, 1 })
                foreach (var cz in new[] { -1, 1 })
                {
                    k.Box(cx * h - (cx > 0 ? 0.2 : 0), FloorY - 0.004, z + cz * h - (cz > 0 ? 0.03 : 0), cx * h + (cx > 0 ? 0 : 0.2), FloorY + 0.012, z + cz * h + (cz > 0 ? 0 : 0.03), Hazard, "hazard paint");
                    k.Box(cx * h - (cx > 0 ? 0.03 : 0), FloorY - 0.007, z + cz * h - (cz > 0 ? 0.2 : 0), cx * h + (cx > 0 ? 0 : 0.03), FloorY + 0.009, z + cz * h + (cz > 0 ? 0 : 0.2), Hazard, "hazard paint");
                }
            // The slot's status lamps on the post line over each hatch: green when its cradle holds a module (lit here: the draft is loaded).
            foreach (var sx in new[] { -1, 1 })
                k.Glow(sx * (w - F + 0.02), -F - 0.06, z - 0.05, sx * (w - F + 0.04), -F - 0.02, z + 0.05, Green);
        }
        // Each hatch's latch keepers on its sill, and its hinge's fixed knuckles under the top rail.
        var knuckles = 2 * N + 2;
        foreach (var sx in new[] { -1, 1 })
        {
            foreach (var zf in new[] { -0.33, 0.33 })
                k.Box(sx * (w - F + 0.01), y0 + F + 0.02, zf * 2 * l - 0.08, sx * (w - 0.06), y0 + F + 0.06, zf * 2 * l + 0.08, Steel, "steel");
            for (var i = 0; i < knuckles; i++)
            {
                var z = -l + F + 0.2 + i * (2 * l - 2 * F - 0.4) / (knuckles - 1);
                Rod(k, sx * (w - 0.06), -F - 0.05, z - 0.06, sx * (w - 0.01), -F, z + 0.06, "+z", FrameC, "steel", 8);
            }
            // The hatch's hang: a small solid tab under the top rail, its socket on the outer face.
            Solid(k, sx * (w - 0.11), -F - 0.06, -0.05, sx * (w - 0.09), -F - 0.02, 0.05);
        }
        k.P.Sock("hatch", [w - 0.09, -F - 0.04, 0], "+x");
        k.P.Sock("hatch.1", [-(w - 0.09), -F - 0.04, 0], "-x");
        // The corner clamps' hangs: a tab on the end face of each foot casting (the clamp flips down over the casting under it).
        var c = 0;
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double cx = sx * (w - 0.085);
                Solid(k, cx - 0.06, y0 + 0.075, sz * (l - 0.01), cx + 0.06, y0 + 0.095, sz * (l + ClampOff));
                k.P.Sock($"clamp.{c++}", [Py.Round(cx, 3), y0 + 0.085, Py.Round(sz * (l + ClampOff), 4)], sz > 0 ? "+z" : "-z");
            }
        // The cradles' sockets on the floor, the hook's on the roof, its feet under it and under each casting.
        for (var s = 0; s < 3; s++)
            k.P.Sock($"slot{s}", [0, FloorY, SlotZ(s)], "+y");
        k.P.Sock("top", [0, 0, 0], "+y");
        k.P.Sock("-y", [0, y0, 0], "-y");
        var f = 0;
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                k.P.Sock($"foot.{f++}", [Py.Round(sx * (w - 0.085), 3), y0, Py.Round(sz * (l - 0.085), 3)], "-y");
        f = 0; // (a container stacks on another foot to head, the same corner numbers)
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                k.P.Sock($"head.{f++}", [Py.Round(sx * (w - 0.085), 3), 0, Py.Round(sz * (l - 0.085), 3)], "+y");
        // Mass: about a third of a tonne a metre of length at 2 m (a 20 ft steel box is 2.2 t): the shell's area at 25 kg a square metre.
        var area = 2 * (2 * w * 2 * l) + 2 * (2 * w * H) + 2 * (H * 2 * l);
        return k.P.Stats(Math.Round(area * 25), 400 + area * 60, 14, 6.0 + N * 2);
    }

    // ---- the hatch: one leaf the whole length of the +x side, hinged on its top edge

    private static Part Hatch()
    {
        var k = K(HatchName(N), "door",
            $"A hatch of a standard {N} m container (one each side): one leaf the whole length of the side, hinged at the top. It swings out and up to an awning over the slots, so a shell, a crane or a fitter reaches every module inside.",
            Medium);
        double w = W2, l = L2, x0 = w - 0.07, x1 = w - 0.03, yb = -H + F + 0.025, yt = -F - 0.005, za = -l + F + 0.005, zb = l - F - 0.005;
        Solid(k, x0, yb, za, x1, yt, zb);
        // The leaf: plating, a raised rim, horizontal stiffeners, the Traders' loading chevrons in the middle, a hazard band at the foot.
        k.Paint(x0, yb, za, x1 - 0.004, yt, zb, Shell);
        k.Paint(x1 - 0.004, yb, za, x1 + 0.012, yb + 0.06, zb, ShellRib);
        k.Paint(x1 - 0.004, yt - 0.06, za, x1 + 0.012, yt, zb, ShellRib);
        k.Paint(x1 - 0.004, yb + 0.06, za, x1 + 0.012, yt - 0.06, za + 0.06, ShellRib);
        k.Paint(x1 - 0.004, yb + 0.06, zb - 0.06, x1 + 0.012, yt - 0.06, zb, ShellRib);
        foreach (var t in new[] { 0.33, 0.67 })
        {
            var y = yb + (yt - yb) * t;
            k.Paint(x1 - 0.004, y - 0.035, za + 0.06, x1 + 0.03, y + 0.035, zb - 0.06, ShellRib);
        }
        // (two slot dividers' posts, so each slot reads as a bay: they stand where the slots meet)
        foreach (var s in new[] { 0, 1 })
        {
            var z = (SlotZ(s) + SlotZ(s + 1)) / 2;
            k.Paint(x1 - 0.004, yb + 0.06, z - 0.035, x1 + 0.03, yt - 0.06, z + 0.035, ShellRib);
        }
        var bands = (int)Math.Round((zb - za) / 0.3);
        for (var i = 0; i < bands; i++)
        {
            double z = za + 0.06 + i * (zb - za - 0.12) / bands, z1 = z + (zb - za - 0.12) / bands - 0.004;
            k.Box(x1 + 0.012, yb + 0.065, z, x1 + 0.02, yb + 0.12, z1, i % 2 == 0 ? Hazard : HazardDark, "hazard paint");
        }
        // Chevrons on each slot's panel (a big "<  >": open here), lanyard yellow, and a slot number plate (pips: 1, 2, 3).
        for (var s = 0; s < 3; s++)
        {
            double zc = SlotZ(s), yc = yb + (yt - yb) * 0.5, a = 0.12 * N, b = 0.1 * N;
            foreach (var d in new[] { -1, 1 })
            {
                Brace(k, "x", x1 + 0.012, x1 + 0.02, yc, zc + d * (a * 0.4 + b), yc + b, zc + d * (a * 0.4), 0.05 * N, Hazard, "hazard paint");
                Brace(k, "x", x1 + 0.015, x1 + 0.026, yc, zc + d * (a * 0.4 + b), yc - b, zc + d * (a * 0.4), 0.05 * N, Hazard, "hazard paint"); // (3 mm prouder: the arms cross)
            }
            k.Paint(x1 + 0.012, yt - 0.06 - 0.16, zc - 0.16, x1 + 0.02, yt - 0.08, zc + 0.16, Paper);
            for (var p = 0; p <= s; p++)
                k.Box(x1 + 0.02, yt - 0.18, zc - 0.1 + p * 0.08, x1 + 0.026, yt - 0.12, zc - 0.06 + p * 0.08, HazardDark, "plastic");
        }
        // Two lever latches at the foot (they meet the keepers on the sill) and a grab handle each end.
        foreach (var zf in new[] { -0.33, 0.33 })
        {
            var z = zf * 2 * l;
            k.Box(x1 + 0.012, yb + 0.13, z - 0.1, x1 + 0.05, yb + 0.2, z + 0.1, Steel, "steel");
            k.Box(x1 + 0.05, yb + 0.15, z - 0.03, x1 + 0.09, yb + 0.42, z + 0.03, Hazard, "plastic");
        }
        foreach (var zf in new[] { -1, 1 })
            Rod(k, x1 + 0.03, yb + 0.5, zf * (zb - 0.18) - 0.02, x1 + 0.06, yt - 0.5, zf * (zb - 0.18) + 0.02, "+y", Steel, "steel", 8);
        // The leaf's hinge knuckles, between the frame's, and a gas strut either end, folded against the leaf.
        var knuckles = 2 * N + 1;
        for (var i = 0; i < knuckles; i++)
        {
            var z = -l + F + 0.2 + (i + 0.5) * (2 * l - 2 * F - 0.4) / (2 * N + 1);
            Rod(k, w - 0.06, yt - 0.05, z - 0.07, w - 0.01, yt, z + 0.07, "+z", FrameC, "steel", 8);
        }
        foreach (var zf in new[] { -1, 1 })
            Rod(k, x0 - 0.04, yb + 0.25, zf * (zb - 0.06) - 0.02, x0 - 0.005, yt - 0.1, zf * (zb - 0.06) + 0.02, "+y", Steel, "steel", 8);
        // (the lit strip along its inner top edge: it lights the slots once the leaf is up)
        k.Glow(x0 - 0.012, yt - 0.04, za + 0.2, x0 - 0.002, yt - 0.02, zb - 0.2, Lamp);
        // Its hang: a tab against the container's, at the top in the middle.
        Solid(k, w - 0.09, -F - 0.06, -0.05, w - 0.07, -F - 0.02, 0.05);
        k.P.Sock("hang", [w - 0.09, -F - 0.04, 0], "-x");
        k.P.Set(("door", Kw(("kind", "swing"), ("axis", "+z"), ("pivot", Py.L(Py.Round(w - 0.035, 4), Py.Round(yt, 4), 0.0)), ("angle", 90), ("seconds", 1.5 + N * 0.75), ("hang", "hang"))));
        var area = (zb - za) * (yt - yb);
        return k.P.Stats(Math.Round(area * 22), 200 + area * 50, 10, 4.0);
    }

    // ---- a corner clamp: a jaw on the end face of a foot casting that flips down over the top casting of the container under it

    private const double ClampOff = 0.006; // the clamp's back stands this far off the casting's end face

    public const string ClampName = "Container Corner Clamp";

    private static Part Clamp()
    {
        // (its own frame: the hang socket at the origin's height on the casting's end face, facing back into it (-z); y 0 is the container's
        // feet, so the jaw stands on the foot casting while open and hangs over the casting under it once it has flipped down)
        var k = K(ClampName, "door",
            "A standard container's corner clamp: a steel jaw on each foot casting. Set on another container, it flips down over the casting under it and locks the two together; flip it up again to lift the top one off.",
            Medium, FrameC);
        const double hx = 0.06, y0 = 0.004, y1 = 0.165, t = 0.028, sy = 0.085;
        Solid(k, -hx, y0, 0, hx, y1, t);
        k.Box(-hx, y0, 0, hx, y1, t - 0.004, FrameC, "dirty steel");
        k.Box(-hx + 0.012, y0 + 0.012, t - 0.004, hx - 0.012, y1 - 0.012, t + 0.008, Hazard, "hazard paint"); // (the face: yellow, so a locked stack shows it)
        k.Box(-0.025, y1 - 0.06, t + 0.008, 0.025, y1 - 0.02, t + 0.05, Steel, "steel"); // (the lever)
        foreach (var bx in new[] { -0.035, 0.035 })
            k.Box(bx - 0.012, y0 + 0.02, t + 0.008, bx + 0.012, y0 + 0.044, t + 0.02, HazardDark, "steel"); // (the bolt heads by the hinge)
        Rod(k, -hx - 0.012, -0.012, 0.0, hx + 0.012, 0.012, 0.024, "+x", Steel, "steel", 8); // (the hinge pin along its foot)
        k.P.Sock("hang", [0, sy, 0], "-z");
        k.P.Set(("door", Kw(("kind", "swing"), ("axis", "+x"), ("pivot", Py.L(0.0, 0.0, Py.Round(t / 2, 4))), ("angle", 180), ("seconds", 1.2), ("hang", "hang"))));
        return k.P.Stats(14, 300, 20, 2.0);
    }

    // ---- a cradle: a flat Module Stand on the container's floor

    private static Part Cradle()
    {
        var k = K(CradleName(N), "frame",
            $"A {N}x{N} m cradle on a cargo container's floor: a flat module stand, so a module set on it is stored there (nothing couples) and travels with the container.",
            Medium, CradleC);
        double x0 = -0.5, x1 = N - 0.5, top = 0.06, face = top - 0.003, c = (N - 1) * 0.5;
        k.Paint(x0, 0, x0, x1, face - 0.016, x1, CradleC, "painted steel");
        k.Paint(x0 + 0.12, face - 0.016, x0 + 0.12, x1 - 0.12, face, x1 - 0.12, CradleC, "painted steel");
        k.Paint(x0 + 0.004, face - 0.016, x0 + 0.004, x1 - 0.004, face, x0 + 0.12, CradleRim, "hazard paint");
        k.Paint(x0 + 0.004, face - 0.016, x1 - 0.12, x1 - 0.004, face, x1 - 0.004, CradleRim, "hazard paint");
        k.Paint(x0 + 0.004, face - 0.016, x0 + 0.12, x0 + 0.12, face, x1 - 0.12, CradleRim, "hazard paint");
        k.Paint(x1 - 0.12, face - 0.016, x0 + 0.12, x1 - 0.004, face, x1 - 0.12, CradleRim, "hazard paint");
        // The locating pins at its corners (they meet a plug's lugs) and the clamps either side that hold a module down in flight.
        foreach (var px in new[] { x0 + 0.05, x1 - 0.05 })
            foreach (var pz in new[] { x0 + 0.05, x1 - 0.05 })
                Rod(k, px - 0.02, face - 0.01, pz - 0.02, px + 0.02, face + 0.006, pz + 0.02, "+y", Steel, "steel", 8);
        foreach (var sx in new[] { -1, 1 })
        {
            var xx = sx < 0 ? x0 : x1;
            k.Box(xx - sx * 0.004 - (sx > 0 ? 0.05 : 0), 0, c - 0.15, xx - sx * 0.004 + (sx < 0 ? 0.05 : 0), face + 0.004, c + 0.15, FrameC, "steel");
        }
        Solid(k, c - 0.1, face - 0.009, c - 0.1, c + 0.1, top, c + 0.1); // (the module socket's solid face)
        Solid(k, x0, 0, x0, x1, face - 0.016, x1);
        k.P.Sock("module", [c, top, c], "+y", kind: "module");
        k.P.Sock("base", [c, 0, c], "-y");
        k.P.Set(("moduleSocket", Kw(("w", N), ("l", N), ("stand", true))));
        return k.P.Stats(N * N * 45, 120 + N * N * 180, 20, 3.0 + N);
    }

    // ---- the draft blueprints: a container, its hatch and cradles, and three stock modules in it

    /// <summary>What each size carries in the gallery: per slot, a plug's parts (name, plug socket, own socket).</summary>
    private static readonly Dictionary<int, (string Part, string On, string By)[][]> Loads = new()
    {
        [1] = [[("Fuel Tank", "s0.0", "-y")], [("Battery", "s0.0", "-y")], [("RCS Thruster", "s0.0", "base")]],
        [2] = [[("RCS Thruster", "s0.0", "base"), ("RCS Thruster", "s1.0", "base"), ("RCS Thruster", "s0.1", "base"), ("RCS Thruster", "s1.1", "base")],
            [("Fuel Tank", "s0.0", "-y"), ("Fuel Tank", "s1.0", "-y"), ("Fuel Tank", "s0.1", "-y"), ("Fuel Tank", "s1.1", "-y")],
            [("Battery", "s0.0", "-y"), ("Battery", "s1.0", "-y"), ("Battery", "s0.1", "-y"), ("Battery", "s1.1", "-y")]],
        [3] = [[("Magpie Power Plant", "s1.1", "bottom")], [], [("Magpie Power Plant", "s1.1", "bottom")]],
    };

    /// <summary>One container's steps (its first step's index is the list's count): the container (on <paramref name="under"/>'s head
    /// sockets when stacked), its two hatches, its clamps (when it has any), its cradles and the stock modules on them.</summary>
    private static void Steps(List<string> steps, int? under, bool clamps)
    {
        var c = steps.Count;
        steps.Add(under is { } u ? $"    {{ \"part\": \"{ContainerName(N)}\", \"on\": {u}, \"socket\": \"head.0\", \"by\": \"foot.0\" }}" : $"    {{ \"part\": \"{ContainerName(N)}\" }}");
        foreach (var h in new[] { "hatch", "hatch.1" })
            steps.Add($"    {{ \"part\": \"{HatchName(N)}\", \"on\": {c}, \"socket\": \"{h}\", \"by\": \"hang\" }}");
        if (clamps)
            for (var k = 0; k < 4; k++)
                steps.Add($"    {{ \"part\": \"{ClampName}\", \"on\": {c}, \"socket\": \"clamp.{k}\", \"by\": \"hang\" }}");
        var cradle = steps.Count;
        for (var s = 0; s < 3; s++)
            steps.Add($"    {{ \"part\": \"{CradleName(N)}\", \"on\": {c}, \"socket\": \"slot{s}\", \"by\": \"base\" }}");
        for (var s = 0; s < 3; s++)
        {
            var parts = Loads[N][s];
            if (parts.Length == 0)
                continue;
            var plug = steps.Count;
            steps.Add($"    {{ \"part\": \"Module Plug {N}x{N}\", \"on\": {cradle + s}, \"socket\": \"module\", \"by\": \"module\" }}");
            foreach (var (part, on, by) in parts)
                steps.Add($"    {{ \"part\": \"{part}\", \"on\": {plug}, \"socket\": \"{on}\", \"by\": \"{by}\" }}");
        }
    }

    private static string Blueprint(bool stacked)
    {
        var steps = new List<string>();
        Steps(steps, null, !stacked); // (stacked: the bottom one stands on the ground, so its clamps would only flip into the floor)
        if (stacked)
            Steps(steps, 0, true);
        return "{\n" +
            "  \"format\": \"call-of-slop-construct-blueprint/1\",\n" +
            $"  \"name\": \"{ContainerName(N)} {(stacked ? "Stacked" : "Loaded")}\",\n" +
            $"  \"description\": \"{(stacked ? $"Two standard {N} m cargo containers, one clamped on the other" : $"A standard {N} m cargo container")}, with stock modules in the cradles. A draft for the Animation gallery, written by the part kit (containers).\",\n" +
            "  \"steps\": [\n" + string.Join(",\n", steps) + "\n  ]\n" +
            "}\n";
    }

    private static string Timing(bool stacked) =>
        "{\n" +
        $"  // Standard cargo containers (ContainerParts.cs, --gen-parts containers): the corner clamps flip down, then both side hatches swing up to awnings.\n" +
        $"  \"blueprint\": \"container-{N}m-{(stacked ? "stacked" : "loaded")}.blueprint.json\",\n" +
        $"  \"title\": \"Standard container {N} m: {(stacked ? "stacked and clamped" : "side hatches")}\",\n" +
        "  \"shelf\": \"Cargo containers\",\n" +
        $"  \"description\": \"{(stacked ? $"Two {N} m containers, one on the other: the top one's corner clamps flip down over the castings under it and lock the stack, then all four hatches swing up. " : "")}{Py.G(N + 0.5)} x {Py.G(N + 0.5)} x {Py.G(3 * N + 0.5)} m outside, three {N}x{N}x{N} m slots, each a cradle a module stands on. Each long side is one hatch hinged at the top: it swings out and up to an awning, never above the roof, so stacked or racked containers still open. A clamp on each foot casting locks it onto a container below. In the game the contents won't be drawn while the hatches are shut.\",\n" +
        "  \"seconds\": " + Py.G(Math.Round(1.6 + 1.5 + N * 0.75 + 0.4, 1)) + ",\n" +
        "  \"steps\": [\n" +
        $"    {{ \"part\": \"{ClampName}\", \"at\": 0.2, \"label\": \"{(stacked ? "Clamps lock the stack" : "Clamps flip down (onto a container below)")}\" }},\n" +
        $"    {{ \"part\": \"{HatchName(N)}\", \"at\": 1.6, \"label\": \"Hatches swing up\" }}\n" +
        "  ]\n" +
        "}\n";

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var n in new[] { 1, 2, 3 })
        {
            Size(n);
            parts.Add(("", Container()));
            parts.Add(("", Hatch()));
            parts.Add(("", Cradle()));
        }
        parts.Add(("", Clamp()));
        WriteParts(o, Out, parts);
        foreach (var n in new[] { 1, 2, 3 })
        {
            Size(n);
            o.File($"{AnimOut}/container-{n}m-loaded.blueprint.json", Blueprint(false));
            o.File($"{AnimOut}/container-{n}m.json", Timing(false));
            if (n == 2) // (the stack: the 2 m size, the one the freighters carry)
            {
                o.File($"{AnimOut}/container-{n}m-stacked.blueprint.json", Blueprint(true));
                o.File($"{AnimOut}/container-{n}m-stacked.json", Timing(true));
            }
        }
    }
}
