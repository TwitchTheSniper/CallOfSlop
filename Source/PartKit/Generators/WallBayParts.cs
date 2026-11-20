using CallOfSlop.PartKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// In-wall bays, a DRAFT for the Animation gallery (Karl, 2026-11-20: "adding in-wall storage/utility. It means we can use the space
/// between the interior wall and exterior hull ... press on the cabinet/panel and whatever is in it should pull out in ways that is
/// customisable but only with a few options. This can be for example chairs, storage, certain ship components, future components").
///
/// One standard <b>Wall Bay</b> (a 0.9 x 1.9 m opening in a ship's interior lining, 0.5 m deep into the void before the hull) takes
/// one <b>insert</b> (the fixed carcass of a fill), and the insert hangs its moving pieces. Every moving piece is an event door, so a
/// press opens it the way any door opens, and it uses one of four motions only: <b>flip down</b> (a seat), <b>flip up</b> (a cover),
/// <b>swing aside</b> (a small locker door) and <b>slide out</b> (a drawer, a rack, a sled). The four fills drafted here:
/// a jump seat with a helmet locker over it, a stack of four drawers, a pull-out rack of spares and bottles, and a component sled
/// (a cover that flips up and a sled that runs out with a power cell on it, plugged in at the back).
///
/// Shown with a slice of the ship round it: lining either side, the deck, the hull's ribs and skin behind with lagging and the pipes
/// and cable tray that run through the void, so the cutaways show where the bay sits. The look is the Zero Day's interior
/// (TorpedoFrigateParts.cs: pale lining over a blue lower band, dark trim, hazard yellow at what moves, small lamps).
///
/// Frame, shared by every part: the deck at y 0, x across the bay (centre 0), the lining's face at z 0, the room +z, the hull behind
/// (-z). Group Style (unlisted): nothing here shows in the Parts tab. Writes Constructs/Parts/WallBays and one blueprint a fill.
/// </summary>
public sealed class WallBayParts : IPartGenerator
{
    public string Name => "wall_bays";

    private const string Out = "Constructs/Parts/WallBays";
    private const string BpOut = "Constructs/Blueprints";
    private const string Section = "In-wall bays";
    private const string Group = StyleSheetParts.Group;

    // The Zero Day's interior paint.
    private const string Wall = "#c4c8ca", WallLow = "#2c5aa0", Trim = "#2a2e33", Deck = "#4a4f55", Frame = "#1f2226", Armour2 = "#30353b",
        Skin2 = "#a9a59a", Hazard = "#e0b020", Glow = "#5ac8ff", Green = "#5ad070", Amber = "#f0a830", Red = "#e0402c", PipeRed = "#a8402c",
        PipeBlue = "#2f6a9a", PipeYellow = "#c8a030", MedWhite = "#dfe4e2", Lag = "#8d8a7e", Pad = "#4e5a66", Strap = "#c87a20", Steel = "#7c838c",
        Olive = "#5d6450", Recess = "#262a2f";

    // The opening: half width, sill and head, depth to the back plate. The lining's thickness, the slice's half width, the hull's inner face.
    private const double W = 0.45, Y0 = 0.10, Y1 = 2.00, D = 0.50, Lt = 0.05, Sw = 0.95, Top = 2.30, HullZ = -0.72;

    private static FittingLookParts.Kit K(string name, string role, string desc, string color = Wall)
    {
        var p = new Part(name, Small, Section, desc, role: role, tool: "wrench", material: "steel", color: color, group: Group);
        p.Set(("finish", "painted steel"), ("collider", "boxes")); // (the hidden boxes collide; the looks don't)
        return new FittingLookParts.Kit(p);
    }

    private static void Solid(FittingLookParts.Kit k, double x0, double y0, double z0, double x1, double y1, double z1) =>
        k.P.Add(Box([Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)], hidden: true));

    /// <summary>A cylinder along <paramref name="axis"/> (+x, +y or +z) through the box's middle.</summary>
    private static void Rod(FittingLookParts.Kit k, double x0, double y0, double z0, double x1, double y1, double z1, string axis, string col, string finish, int seg = 12) =>
        k.P.Add(Box([Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)], col, detail: true,
            shape: Kw(("shape", "cylinder"), ("segments", seg), ("front", axis), ("finish", finish), ("paintable", false))));

    private static List<object?> V(double x, double y, double z) => [Py.Round(x, 4), Py.Round(y, 4), Py.Round(z, 4)];

    // ---- the bay and its slice of ship

    private static Part Bay()
    {
        var k = K("Wall Bay", "frame", "An in-wall bay (draft): a 0.9 x 1.9 m opening in a ship's interior lining, 0.5 m deep into the void before the hull. It takes one insert: a jump seat, drawers, a pull-out rack or a component sled. Shown with the lining, deck and hull round it.");
        // The deck: plate on the room side, grating over the void's floor.
        k.Paint(-Sw, -0.06, -0.005, Sw, 0, 0.62, Deck, "painted deck plate");
        Solid(k, -Sw, -0.06, -0.005, Sw, 0, 0.62);
        k.Box(-Sw, -0.06, HullZ + 0.01, Sw, -0.012, -0.012, Frame, "painted grating");
        // The lining either side: pale panels over the blue lower band, a dark seam between panels; the header over the opening.
        foreach (var s in new[] { -1.0, 1.0 })
        {
            double a = s * (W + 0.08), b = s * Sw;
            k.Paint(a, 0.38, -Lt, b, Top, 0, Wall, "wall panelling");
            k.Paint(a, 0, -Lt, b, 0.38, 0.006, WallLow, "painted steel");
            k.Box(a + s * 0.20, 0.4, -0.004, a + s * 0.215, Top - 0.02, 0.003, Trim, "plastic"); // (a panel seam)
            Solid(k, a, 0, -Lt, b, Top, 0);
        }
        k.Paint(-(W + 0.08), Y1 + 0.08, -Lt, W + 0.08, Top, 0, Wall, "wall panelling");
        Solid(k, -(W + 0.08), Y1 + 0.08, -Lt, W + 0.08, Top, 0);
        // The bezel: dark trim round the opening, 20 mm proud; a hazard edge on the sill; a lit label and the latch lamp on the head.
        k.Box(-(W + 0.08), 0.02, -Lt, -W, Y1 + 0.08, 0.02, Trim, "plastic");
        k.Box(W, 0.02, -Lt, W + 0.08, Y1 + 0.08, 0.02, Trim, "plastic");
        k.Box(-W, 0.02, -Lt, W, Y0, 0.02, Trim, "plastic");
        k.Box(-W, Y1, -Lt, W, Y1 + 0.08, 0.02, Trim, "plastic");
        for (var i = 0; i < 9; i++)
            k.Box(-W + 0.02 + i * 0.1, 0.035, 0.019, -W + 0.07 + i * 0.1, 0.085, 0.026, Hazard, "hazard paint");
        k.Box(-0.16, Y1 + 0.02, 0.019, 0.16, Y1 + 0.06, 0.027, Frame, "plastic");
        k.Glow(-0.14, Y1 + 0.03, 0.026, 0.1, Y1 + 0.05, 0.031, Glow);
        k.Glow(0.12, Y1 + 0.03, 0.026, 0.14, Y1 + 0.05, 0.032, Green);
        // The recess: dark lining, sides, floor and roof, the back plate.
        k.Box(-W, Y0, -D - 0.03, W, Y1, -D, Recess, "dirty steel");
        foreach (var s in new[] { -1.0, 1.0 })
            k.Box(s * W, Y0 - 0.03, -D - 0.03, s * (W + 0.03), Y1 + 0.03, -Lt - 0.002, Recess, "dirty steel");
        k.Box(-W, Y0 - 0.03, -D, W, Y0, -Lt - 0.002, Recess, "dirty steel");
        k.Box(-W, Y1, -D, W, Y1 + 0.03, -Lt - 0.002, Recess, "dirty steel");
        Solid(k, -W - 0.03, Y0 - 0.03, -D - 0.03, W + 0.03, Y1 + 0.03, -D);
        // The void behind: two ribs, the hull's skin with lagging on it, armour outside.
        foreach (var s in new[] { -1.0, 1.0 })
        {
            k.Box(s * 0.66, -0.06, HullZ, s * 0.76, Top + 0.1, -Lt - 0.004, Armour2, "plated steel");
            k.Box(s * 0.68, 0.3, HullZ + 0.12, s * 0.74, Top - 0.2, -0.16, Frame, "dirty steel"); // (the rib's flange)
            Solid(k, s * 0.66, -0.06, HullZ, s * 0.76, Top + 0.1, -Lt);
        }
        k.Box(-Sw, -0.06, HullZ - 0.06, Sw, Top + 0.15, HullZ, Skin2, "painted plating");
        k.Box(-Sw, -0.06, HullZ - 0.12, Sw, Top + 0.15, HullZ - 0.06, Armour2, "plated steel");
        foreach (var s in new[] { -1.0, 1.0 })
            k.Box(s * 0.78, 0.0, HullZ + 0.002, s * (Sw - 0.01), Top + 0.1, HullZ + 0.07, Lag, "lagging");
        Solid(k, -Sw, -0.06, HullZ - 0.12, Sw, Top + 0.15, HullZ);
        // Runs through the void over the bay: a red and a blue pipe, a yellow one low, a cable tray with its bundle.
        Rod(k, -Sw, 2.13, -0.66, Sw, 2.21, -0.58, "+x", PipeRed, "painted steel");
        Rod(k, -Sw, 2.13, -0.55, Sw, 2.21, -0.47, "+x", PipeBlue, "painted steel");
        Rod(k, -Sw, 0.16, -0.68, Sw, 0.22, -0.62, "+x", PipeYellow, "painted steel");
        k.Box(-Sw, 2.08, -0.40, Sw, 2.1, -0.16, Steel, "steel");
        k.Box(-Sw, 2.1, -0.38, Sw, 2.18, -0.18, Frame, "rubber");
        p_Sockets(k.P);
        return k.P.Stats(160, 1500, 20, 6.0);
    }

    private static void p_Sockets(Part p)
    {
        p.Sock("deck", [0, -0.06, 0.2], "-y");
        p.Sock("hull", [0, 1.0, HullZ - 0.12], "-z");
        p.Sock("insert", [0, 1.0, -D], "+z");
    }

    /// <summary>An insert: the fixed carcass of one fill, hung on the bay's back plate.</summary>
    private static FittingLookParts.Kit Insert(string name, string desc)
    {
        var k = K(name, "frame", desc, Recess);
        Solid(k, -W, Y0, -D, W, Y1, -D + 0.015); // (the back plate it bolts to the bay by)
        k.P.Sock("bay", [0, 1.0, -D], "-z");
        return k;
    }

    /// <summary>A moving piece: an event door on its insert. Slide: travel out along +z; swing: about axis by angle at pivot.</summary>
    private static FittingLookParts.Kit Mover(string name, string desc, string sock, double[] at, string color = Wall)
    {
        // (its hang socket on the -z face of a small solid tab at the lining's plane by the bay's right jamb, meeting its insert's tab there)
        var k = K(name, "door", desc, color);
        Solid(k, 0.435, at[1], 0, 0.455, at[1] + 0.02, 0.02);
        k.P.Sock("hang", [0.445, at[1] + 0.01, 0], "-z");
        return k;
    }

    /// <summary>An insert's socket for a moving piece at height y: on the +z face of a small solid tab at the lining's plane.</summary>
    private static void Tab(FittingLookParts.Kit k, string name, double y)
    {
        Solid(k, 0.435, y, -0.02, 0.455, y + 0.02, 0);
        k.P.Sock(name, [0.445, y + 0.01, 0], "+z");
    }

    private static void Slide(Part p, double travel, double seconds) =>
        p.Set(("door", Kw(("kind", "slide"), ("axis", "+z"), ("travel", travel), ("seconds", seconds), ("hang", "hang"))));

    private static void Swing(Part p, string axis, double angle, List<object?> pivot, double seconds) =>
        p.Set(("door", Kw(("kind", "swing"), ("axis", axis), ("pivot", pivot), ("angle", angle), ("seconds", seconds), ("hang", "hang"))));

    // ---- 1. Jump seat: the seat flips down from the wall, a padded back and harness above it, a helmet locker on top that swings aside

    private const double SeatY0 = 0.47, SeatY1 = 0.93, LockY0 = 1.63;

    private static Part SeatInsert()
    {
        var k = Insert("Jump Seat Insert", "An in-wall bay's jump seat insert (draft): a padded back with a four-point harness, a kick panel, the seat's hinge, and a helmet locker over it.");
        Tab(k, "seat", SeatY0);
        Tab(k, "helmet", LockY0);
        // The kick panel under the seat: lining, vents.
        k.Paint(-W, Y0, -0.04, W, SeatY0 - 0.01, -0.012, Wall, "wall panelling");
        for (var i = 0; i < 4; i++)
            k.Box(-0.3, 0.18 + i * 0.05, -0.014, 0.3, 0.2 + i * 0.05, -0.006, Frame, "dirty steel");
        // Behind the seat: a shallow dark recess the cushion sits in, two hinge knuckles at its foot.
        k.Box(-W, SeatY0, -0.16, W, SeatY1 + 0.01, -0.13, Recess, "dirty steel");
        foreach (var x in new[] { -0.3, 0.3 })
            Rod(k, x - 0.06, SeatY0 - 0.02, 0.0, x + 0.06, SeatY0 + 0.02, 0.04, "+x", Steel, "steel");
        Solid(k, -W, Y0, -0.16, W, SeatY0, -0.012);
        // The back: a panel across the opening, the pad on it in two bolsters, the harness over it.
        k.Box(-W, SeatY1 + 0.01, -0.06, W, LockY0 - 0.02, -0.04, Recess, "dirty steel");
        k.Paint(-0.36, SeatY1 + 0.04, -0.04, -0.01, LockY0 - 0.05, 0.03, Pad, "fabric");
        k.Paint(0.01, SeatY1 + 0.04, -0.04, 0.36, LockY0 - 0.05, 0.03, Pad, "fabric");
        k.Paint(-0.16, LockY0 - 0.2, -0.04, 0.16, LockY0 - 0.03, 0.05, Pad, "fabric"); // (the head pad)
        foreach (var x in new[] { -0.17, 0.17 })
            k.Box(x - 0.025, SeatY1 + 0.1, 0.03, x + 0.025, LockY0 - 0.2, 0.042, Strap, "fabric");
        k.Box(-0.05, SeatY1 + 0.16, 0.035, 0.05, SeatY1 + 0.26, 0.05, Steel, "chrome"); // (the buckle)
        k.Box(-0.2, SeatY1 + 0.19, 0.03, 0.2, SeatY1 + 0.23, 0.04, Strap, "fabric");
        Solid(k, -W, SeatY1, -0.06, W, LockY0 - 0.02, 0.03);
        // The helmet locker: a dark box, a helmet with its visor, a mask on a hook.
        k.Box(-W, LockY0, -D, W, LockY0 + 0.012, -0.03, Recess, "dirty steel");
        k.Paint(-0.3, LockY0 + 0.012, -0.36, 0.0, LockY0 + 0.27, -0.08, MedWhite, "gloss paint");
        k.Glow(-0.27, LockY0 + 0.1, -0.082, -0.03, LockY0 + 0.19, -0.072, "#3a6a80");
        k.Box(0.1, LockY0 + 0.08, -0.3, 0.26, LockY0 + 0.22, -0.18, Frame, "rubber");
        Rod(k, 0.16, LockY0 + 0.22, -0.32, 0.2, LockY0 + 0.3, -0.28, "+y", Steel, "steel");
        return k.P.Stats(40, 600, 6, 3.0);
    }

    private static Part SeatFlap()
    {
        var k = Mover("Jump Seat", "An in-wall jump seat (draft): a lining panel that flips down on its foot to a cushioned seat. Press it and it drops.", "seat", [0, SeatY0, 0]);
        // Shut: the panel's face flush with the lining, the cushion behind it. Open, the cushion faces up.
        k.Paint(-0.43, SeatY0 + 0.005, 0.0, 0.43, SeatY1, 0.025, Wall, "wall panelling");
        k.Paint(-0.43, SeatY0 + 0.005, 0.02, 0.43, SeatY0 + 0.06, 0.03, WallLow, "painted steel"); // (the blue edge: the seat's front lip open)
        k.Box(-0.08, SeatY1 - 0.07, 0.022, 0.08, SeatY1 - 0.03, 0.034, Hazard, "hazard paint"); // (the press pad)
        k.Glow(-0.05, SeatY1 - 0.06, 0.03, 0.05, SeatY1 - 0.04, 0.038, Green);
        k.Box(-0.4, SeatY0 + 0.02, -0.02, 0.4, SeatY1 - 0.02, 0.0, Frame, "dirty steel");
        k.Paint(-0.37, SeatY0 + 0.04, -0.11, 0.37, SeatY1 - 0.04, -0.02, Pad, "fabric");
        Solid(k, -0.43, SeatY0, -0.11, 0.43, SeatY1, 0.025);
        Swing(k.P, "+x", 90, V(0, SeatY0, 0.025), 0.9);
        return k.P.Stats(18, 300, 4, 2.0);
    }

    private static Part HelmetDoor()
    {
        var k = Mover("Helmet Locker Door", "The helmet locker's door over an in-wall jump seat (draft): it swings aside on its left edge.", "helmet", [0, LockY0, 0]);
        k.Paint(-0.43, LockY0 + 0.01, -0.012, 0.43, Y1 - 0.01, 0.018, Wall, "wall panelling");
        k.Box(0.3, LockY0 + 0.1, 0.015, 0.36, Y1 - 0.1, 0.028, Frame, "plastic"); // (the pull)
        k.Box(-0.34, LockY0 + 0.25, 0.015, -0.1, LockY0 + 0.3, 0.024, Trim, "plastic");
        k.Glow(-0.32, LockY0 + 0.26, 0.022, -0.12, LockY0 + 0.29, 0.028, Glow);
        Rod(k, -0.45, LockY0 + 0.05, 0.0, -0.42, LockY0 + 0.12, 0.03, "+y", Steel, "steel");
        Rod(k, -0.45, Y1 - 0.12, 0.0, -0.42, Y1 - 0.05, 0.03, "+y", Steel, "steel");
        Solid(k, -0.43, LockY0, -0.012, 0.43, Y1, 0.018);
        Swing(k.P, "+y", -90, V(-0.435, 0, 0.018), 0.8);
        return k.P.Stats(8, 200, 3, 1.5);
    }

    // ---- 2. Drawers: four deep drawers on runners

    private const int Drawers = 4;
    private static double DrawerY(int i) => Y0 + i * (Y1 - Y0) / Drawers;

    private static Part DrawerInsert()
    {
        var k = Insert("Drawer Stack Insert", "An in-wall bay's drawer insert (draft): the dividers and runners for four deep drawers.");
        for (var i = 0; i < Drawers; i++)
            Tab(k, $"drawer{i}", DrawerY(i));
        for (var i = 1; i < Drawers; i++)
            k.Box(-W, DrawerY(i) - 0.006, -D, W, DrawerY(i) + 0.006, -0.03, Recess, "dirty steel");
        foreach (var s in new[] { -1.0, 1.0 })
            for (var i = 0; i < Drawers; i++)
                k.Box(s * (W - 0.02), DrawerY(i) + 0.03, -D + 0.02, s * W, DrawerY(i) + 0.06, -0.04, Steel, "steel");
        return k.P.Stats(30, 500, 6, 3.0);
    }

    private static Part Drawer(int i)
    {
        double y0 = DrawerY(i) + 0.008, y1 = DrawerY(i + 1) - 0.008;
        var k = Mover($"Stow Drawer {i + 1}", "A deep drawer in an in-wall bay (draft): press its face and it runs out on its runners.", $"drawer{i}", [0, DrawerY(i), 0]);
        // The face: lining, a press bar across the top in hazard yellow, a lit tag.
        k.Paint(-0.44, y0, -0.012, 0.44, y1, 0.018, i == 0 ? WallLow : Wall, "wall panelling");
        k.Box(-0.2, y1 - 0.07, 0.015, 0.2, y1 - 0.035, 0.03, Hazard, "hazard paint");
        k.Box(0.26, y1 - 0.075, 0.015, 0.4, y1 - 0.03, 0.026, Frame, "plastic");
        k.Glow(0.28, y1 - 0.066, 0.024, 0.36, y1 - 0.04, 0.03, i % 2 == 0 ? Glow : Amber);
        // The box: sides, bottom and back in dark steel, open on top.
        double b = y0 + 0.02, h = y1 - 0.06, zb = -D + 0.04;
        k.Box(-0.42, b, zb, 0.42, b + 0.02, -0.012, Recess, "dirty steel");
        foreach (var s in new[] { -1.0, 1.0 })
        {
            k.Box(s * 0.40, b + 0.02, zb, s * 0.42, h, -0.012, Recess, "dirty steel");
            k.Box(s * 0.42, b + 0.02, zb, s * 0.435, b + 0.05, -0.02, Steel, "steel"); // (its runner)
        }
        k.Box(-0.40, b + 0.02, zb, 0.40, h, zb + 0.02, Recess, "dirty steel");
        // What's in it, a different load in each.
        double f = b + 0.02;
        switch (i)
        {
            case 0: // boots and an extinguisher, lying down
                Rod(k, -0.36, f, -0.4, -0.2, f + 0.16, -0.06, "+z", Red, "gloss paint");
                k.Box(-0.12, f, -0.36, 0.1, f + 0.14, -0.08, Frame, "rubber");
                k.Box(0.14, f, -0.36, 0.36, f + 0.14, -0.08, Frame, "rubber");
                break;
            case 1: // tool tray: foam with tools in their cut-outs
                k.Box(-0.38, f, -0.44, 0.38, f + 0.08, -0.03, "#3a3d33", "rubber");
                k.Box(-0.32, f + 0.08, -0.38, -0.27, f + 0.1, -0.08, Steel, "chrome");
                k.Box(-0.2, f + 0.08, -0.36, -0.1, f + 0.11, -0.1, Red, "plastic");
                k.Box(0.0, f + 0.08, -0.4, 0.04, f + 0.1, -0.06, Steel, "chrome");
                k.Box(0.12, f + 0.08, -0.3, 0.32, f + 0.12, -0.14, Hazard, "plastic");
                break;
            case 2: // spare parts bins
                for (var c = 0; c < 3; c++)
                    for (var r = 0; r < 2; r++)
                    {
                        double x = -0.37 + c * 0.25, z = -0.44 + r * 0.2;
                        k.Box(x, f, z, x + 0.23, f + 0.18, z + 0.18, r == 0 ? PipeBlue : Olive, "plastic");
                    }
                break;
            default: // ration packs in two stacks
                for (var r = 0; r < 3; r++)
                {
                    k.Box(-0.36, f + r * 0.08, -0.42, -0.04, f + r * 0.08 + 0.07, -0.1, Olive, "plastic");
                    k.Box(0.04, f + r * 0.08, -0.42, 0.36, f + r * 0.08 + 0.07, -0.1, r == 1 ? Amber : Olive, "plastic");
                }
                break;
        }
        Solid(k, -0.44, y0, -0.012, 0.44, y1, 0.018);
        Solid(k, -0.42, b, zb, 0.42, b + 0.02, -0.012);
        Slide(k.P, 0.42, 0.9);
        return k.P.Stats(20, 300, 4, 1.5);
    }

    // ---- 3. Pull-out rack: the whole face runs out with a two-sided rack of shelves behind it

    private static Part RackInsert()
    {
        var k = Insert("Pull-out Rack Insert", "An in-wall bay's pull-out rack insert (draft): the top and bottom runners a tall rack runs out on.");
        Tab(k, "rack", Y0);
        k.Box(-0.06, Y0, -D, 0.06, Y0 + 0.03, -0.02, Steel, "steel");
        k.Box(-0.06, Y1 - 0.03, -D, 0.06, Y1, -0.02, Steel, "steel");
        return k.P.Stats(20, 400, 6, 3.0);
    }

    private static Part Rack()
    {
        var k = Mover("Pull-out Rack", "A pull-out rack in an in-wall bay (draft): the bay's face runs out of the wall with shelves behind it, reached from both sides: oxygen bottles, a med kit, filters, rations.", "rack", [0, Y0, 0]);
        k.Paint(-0.44, Y0 + 0.01, -0.012, 0.44, Y1 - 0.01, 0.018, Wall, "wall panelling");
        k.Paint(-0.44, Y0 + 0.01, -0.012, 0.44, 0.38, 0.022, WallLow, "painted steel");
        k.Box(-0.06, 1.0, 0.015, 0.06, 1.3, 0.03, Hazard, "hazard paint"); // (the press pad)
        k.Box(-0.22, 1.62, 0.015, 0.22, 1.7, 0.026, Frame, "plastic");
        k.Glow(-0.2, 1.635, 0.024, 0.2, 1.685, 0.03, Glow);
        // The rack: a spine down the middle, shelves across.
        double zb = -D + 0.03;
        k.Box(-0.015, Y0 + 0.03, zb, 0.015, Y1 - 0.03, -0.012, Recess, "dirty steel");
        k.Box(-0.03, Y0 + 0.01, zb, 0.03, Y0 + 0.03, -0.012, Steel, "steel");
        k.Box(-0.03, Y1 - 0.03, zb, 0.03, Y1 - 0.01, -0.012, Steel, "steel");
        double[] shelves = [0.2, 0.62, 1.04, 1.46];
        foreach (var y in shelves)
        {
            k.Box(-0.4, y - 0.015, zb, 0.4, y, -0.012, Recess, "dirty steel");
            foreach (var s in new[] { -1.0, 1.0 })
                k.Box(s * 0.39, y, zb, s * 0.4, y + 0.06, -0.012, Steel, "steel"); // (a lip so nothing slides off)
        }
        // Loads: O2 bottles (white, green shoulder) on the bottom shelf, a med kit and filters, spare cells, rations.
        foreach (var s in new[] { -1.0, 1.0 })
            foreach (var z in new[] { -0.4, -0.22 })
            {
                Rod(k, s * 0.33, 0.2, z, s * 0.17, 0.52, z + 0.15, "+y", MedWhite, "gloss paint");
                Rod(k, s * 0.31, 0.52, z + 0.02, s * 0.19, 0.57, z + 0.13, "+y", Green, "gloss paint");
            }
        k.Box(-0.36, 0.62, -0.42, -0.06, 0.84, -0.06, MedWhite, "plastic");
        k.Box(-0.25, 0.7, -0.062, -0.17, 0.76, -0.055, Red, "plastic");
        k.Box(-0.23, 0.68, -0.062, -0.19, 0.78, -0.054, Red, "plastic");
        for (var c = 0; c < 3; c++)
            Rod(k, 0.06 + c * 0.11, 0.62, -0.36, 0.15 + c * 0.11, 0.8, -0.27, "+y", Olive, "plastic");
        for (var c = 0; c < 2; c++)
        {
            k.Box(-0.37 + c * 0.17, 1.04, -0.4, -0.23 + c * 0.17, 1.3, -0.12, Frame, "plastic");
            k.Glow(-0.33 + c * 0.17, 1.24, -0.122, -0.27 + c * 0.17, 1.27, -0.115, Green);
        }
        k.Box(0.05, 1.04, -0.44, 0.37, 1.16, -0.1, Olive, "plastic");
        k.Box(0.05, 1.16, -0.44, 0.37, 1.28, -0.1, Amber, "plastic");
        k.Box(-0.37, 1.46, -0.42, 0.37, 1.6, -0.06, Olive, "fabric"); // (folded blankets)
        Solid(k, -0.44, Y0, -0.012, 0.44, Y1, 0.018);
        Slide(k.P, 0.46, 1.4);
        return k.P.Stats(45, 400, 4, 2.0);
    }

    // ---- 4. Component sled: a cover flips up, then a sled runs out with a power cell on it, plugged in at the back

    private const double CovY0 = 0.70, CovY1 = 1.50;

    private static Part SledInsert()
    {
        var k = Insert("Component Bay Insert", "An in-wall bay's component insert (draft): a service panel under the sled, a gauge panel over it, the sled's rails and the bay's power plug at the back.");
        Tab(k, "cover", CovY1);
        Tab(k, "sled", CovY0);
        // Under: a service panel with a breaker and vents.
        k.Paint(-W, Y0, -0.04, W, CovY0 - 0.01, -0.012, Wall, "wall panelling");
        k.Paint(-W, Y0, -0.04, W, 0.38, -0.008, WallLow, "painted steel");
        for (var i = 0; i < 3; i++)
            k.Box(-0.3, 0.44 + i * 0.05, -0.014, 0.3, 0.46 + i * 0.05, -0.006, Frame, "dirty steel");
        k.Box(0.24, 0.12, -0.014, 0.4, 0.3, 0.0, Frame, "plastic");
        k.Box(0.29, 0.18, -0.002, 0.35, 0.26, 0.02, Red, "plastic");
        // Over: gauges and a small screen.
        k.Paint(-W, CovY1 + 0.01, -0.04, W, Y1, -0.012, Wall, "wall panelling");
        k.Box(-0.38, 1.62, -0.014, -0.02, 1.9, 0.0, Frame, "plastic");
        k.Glow(-0.36, 1.64, -0.002, -0.04, 1.88, 0.004, Glow);
        foreach (var x in new[] { 0.12, 0.3 })
        {
            Rod(k, x - 0.07, 1.68, -0.014, x + 0.07, 1.82, 0.01, "+z", Trim, "plastic", 16);
            Rod(k, x - 0.055, 1.695, 0.0, x + 0.055, 1.805, 0.014, "+z", MedWhite, "plastic", 16);
        }
        Solid(k, -W, Y0, -0.04, W, CovY0 - 0.01, -0.012);
        Solid(k, -W, CovY1 + 0.01, -0.04, W, Y1, -0.012);
        // The bay: dark, rails either side, the plug at the back with its lamp and the cable down into the void.
        k.Box(-W, CovY0 - 0.01, -D, W, CovY0, -0.04, Recess, "dirty steel");
        foreach (var s in new[] { -1.0, 1.0 })
            k.Box(s * 0.40, CovY0, -D, s * 0.44, CovY0 + 0.05, -0.04, Steel, "steel");
        k.Box(-0.12, 0.84, -D, 0.12, 1.06, -D + 0.05, Frame, "plastic");
        k.Glow(0.14, 1.0, -D + 0.002, 0.18, 1.04, -D + 0.02, Green);
        Rod(k, -0.03, CovY0 - 0.6, -D + 0.01, 0.03, 0.84, -D + 0.05, "+y", Frame, "rubber");
        return k.P.Stats(40, 600, 6, 3.0);
    }

    private static Part SledCover()
    {
        var k = Mover("Component Bay Cover", "The cover of an in-wall component bay (draft): a hinged panel that flips up on its top edge.", "cover", [0, CovY1, 0]);
        k.Paint(-0.44, CovY0 + 0.01, -0.012, 0.44, CovY1 - 0.005, 0.018, Wall, "wall panelling");
        for (var i = 0; i < 6; i++) // (a hazard band at the bottom edge: it swings up, and out over your head)
            k.Box(-0.42 + i * 0.145, CovY0 + 0.03, 0.015, -0.35 + i * 0.145, CovY0 + 0.07, 0.024, i % 2 == 0 ? Hazard : Frame, "hazard paint");
        k.Box(-0.16, CovY0 + 0.1, 0.015, 0.16, CovY0 + 0.15, 0.03, Trim, "plastic"); // (the press bar)
        k.Box(-0.3, CovY1 - 0.18, 0.015, 0.3, CovY1 - 0.1, 0.024, Frame, "plastic");
        k.Glow(-0.28, CovY1 - 0.165, 0.022, 0.1, CovY1 - 0.115, 0.028, Amber);
        Rod(k, -0.4, CovY1 - 0.025, 0.004, 0.4, CovY1 + 0.005, 0.034, "+x", Steel, "steel");
        Solid(k, -0.44, CovY0, -0.012, 0.44, CovY1, 0.018);
        Swing(k.P, "+x", -90, V(0, CovY1, 0.02), 1.0);
        return k.P.Stats(12, 300, 4, 1.5);
    }

    private static Part Sled()
    {
        var k = Mover("Component Sled", "A sled in an in-wall component bay (draft): it runs out on the bay's rails with what it carries (here a power cell) and unplugs as it goes.", "sled", [0, CovY0, 0], Steel);
        double y = CovY0 + 0.01;
        // The tray and its front handle.
        k.Box(-0.42, y, -D + 0.06, 0.42, y + 0.03, -0.02, Steel, "tread plate");
        foreach (var s in new[] { -1.0, 1.0 })
            k.Box(s * 0.395, y + 0.03, -D + 0.06, s * 0.42, y + 0.07, -0.02, Steel, "steel");
        k.Paint(-0.42, y, -0.03, 0.42, y + 0.12, -0.005, Trim, "painted steel");
        Rod(k, -0.2, y + 0.05, -0.006, 0.2, y + 0.09, 0.03, "+x", Hazard, "plastic");
        // The power cell: a cased block with cell bands, terminals, a carry handle and a status lamp.
        double c0 = y + 0.03, c1 = c0 + 0.5;
        k.Paint(-0.34, c0, -D + 0.08, 0.34, c1, -0.05, Olive, "painted steel");
        for (var i = 0; i < 4; i++)
            k.Box(-0.345, c0 + 0.06 + i * 0.11, -D + 0.075, 0.345, c0 + 0.08 + i * 0.11, -0.045, Frame, "dirty steel");
        k.Box(-0.3, c1, -0.3, 0.3, c1 + 0.02, -0.1, Frame, "plastic");
        foreach (var x in new[] { -0.2, 0.2 })
        {
            Rod(k, x - 0.035, c1 + 0.02, -0.22, x + 0.035, c1 + 0.06, -0.15, "+y", x < 0 ? Red : Frame, "plastic");
            k.Box(x - 0.02, c1, -0.42, x + 0.02, c1 + 0.08, -0.38, Steel, "steel"); // (the carry handle's posts)
        }
        k.Box(-0.22, c1 + 0.08, -0.42, 0.22, c1 + 0.1, -0.38, Frame, "rubber");
        k.Box(-0.12, c0 + 0.12, -D + 0.05, 0.12, c0 + 0.32, -D + 0.08, Frame, "plastic"); // (its plug, mating the bay's)
        k.Box(-0.3, c1 - 0.12, -0.052, 0.3, c1 - 0.04, -0.044, Hazard, "hazard paint");
        k.Glow(0.18, c0 + 0.08, -0.05, 0.3, c0 + 0.13, -0.043, Green);
        Solid(k, -0.42, y, -D + 0.06, 0.42, y + 0.03, -0.02);
        Solid(k, -0.34, c0, -D + 0.08, 0.34, c1, -0.05);
        Slide(k.P, 0.45, 1.2);
        return k.P.Stats(60, 500, 6, 2.0);
    }

    // ---- assembled, one blueprint a fill (for the gallery)

    private static readonly (string File, string Name, string Desc, string Insert, string[] Movers)[] Fills =
    [
        ("WallBayJumpSeat", "Wall Bay Jump Seat", "An in-wall bay (draft) with a jump seat that flips down and a helmet locker over it.", "Jump Seat Insert", ["Jump Seat", "Helmet Locker Door"]),
        ("WallBayDrawers", "Wall Bay Drawers", "An in-wall bay (draft) with four deep drawers.", "Drawer Stack Insert", [.. Enumerable.Range(1, Drawers).Select(i => $"Stow Drawer {i}")]),
        ("WallBayRack", "Wall Bay Rack", "An in-wall bay (draft) with a rack that pulls out of the wall.", "Pull-out Rack Insert", ["Pull-out Rack"]),
        ("WallBayComponent", "Wall Bay Component Sled", "An in-wall bay (draft) whose cover flips up and whose sled runs out with a power cell on it.", "Component Bay Insert", ["Component Bay Cover", "Component Sled"]),
    ];

    public void Make(PartOutput o)
    {
        o.Own(Out);
        var parts = new List<Part> { Bay(), SeatInsert(), SeatFlap(), HelmetDoor(), DrawerInsert(), RackInsert(), Rack(), SledInsert(), SledCover(), Sled() };
        parts.AddRange(Enumerable.Range(0, Drawers).Select(Drawer));
        var defs = new Dictionary<string, PyDict>();
        var insertSock = new Dictionary<string, string>
        {
            ["Jump Seat"] = "seat", ["Helmet Locker Door"] = "helmet", ["Pull-out Rack"] = "rack", ["Component Bay Cover"] = "cover", ["Component Sled"] = "sled",
        };
        for (var i = 0; i < Drawers; i++)
            insertSock[$"Stow Drawer {i + 1}"] = $"drawer{i}";
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        foreach (var f in Fills)
        {
            var bp = new BuildingParts.Blueprint(f.Name, f.Desc, defs);
            var bay = bp.First("Wall Bay", 2); // (turned half round: the galleries look from -z, and the room is the bay's +z)
            var ins = bp.Hang(f.Insert, bay, "insert", "bay");
            foreach (var m in f.Movers)
                bp.Hang(m, ins, insertSock[m], "hang");
            o.File($"{BpOut}/{f.File}.json", PyJson.Dumps(bp.Json(), 2) + "\n");
        }
    }
}

internal static class WallBayKitExt
{
    public static T Also<T>(this T t, Action<T> a)
    {
        a(t);
        return t;
    }
}
