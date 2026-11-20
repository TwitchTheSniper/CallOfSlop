using CallOfSlop.PartKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// In-wall bays, a DRAFT for the Animation gallery (Karl, 2026-11-20: "adding in-wall storage/utility. It means we can use the space
/// between the interior wall and exterior hull ... press on the cabinet/panel and whatever is in it should pull out in ways that is
/// customisable but only with a few options. This can be for example chairs, storage, certain ship components, future components").
///
/// One standard <b>Wall Bay</b> (a 0.9 x 1.9 m opening in a ship's interior lining) comes in three depths into the void before the
/// hull, 0.5 m, 1 m and 1.5 m, and takes one <b>insert</b> (the fixed carcass of a fill), and the insert hangs its moving pieces. Every
/// moving piece is an event door, so a press opens it the way any door opens, and it uses one of four motions only: <b>flip down</b>
/// (a seat, a bed, a desk), <b>flip up</b> (a cover), <b>swing aside</b> (a small locker door) and <b>slide out</b> (a drawer, a rack,
/// a sled). Each depth has its own fills. 0.5 m: a jump seat with a helmet locker, four drawers, a pull-out rack, a component sled, a
/// Murphy bed, a fold-down desk. 1 m: a suit rack, deep shelves, a long component sled, a toilet, a consumables rack, and two ship
/// systems that take consumables, a CO2 scrubber (canisters) and a water reclaimer (filters). 1.5 m: a spacewalk pack sled, a
/// stretcher drawer, deeper shelves, a heavy module sled and an oxygen generator (candles). The second way to open a bay is the
/// <b>twist latch</b>, a big handle on the jamb that turns a quarter, then the fill runs out (the toilet, the stores, the systems).
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
    private const double W = 0.45, Y0 = 0.10, Y1 = 2.00, Lt = 0.05, Sw = 0.95, Top = 2.30;

    // The depth being built (Karl, 2026-11-20: "make deeper ones as well ... 3x the depth ... all different depths, different
    // functions"): 0.5 m (the standard bay), 1 m and 1.5 m, each with its own fills; the hull's inner face 0.22 m behind the back
    // plate. Sfx names a deeper bay's parts (" 1m", " 1.5m"); a part only one depth has is built while that depth is set.
    private static double D = 0.50, HullZ = -0.72;
    private static string Sfx = "";

    private static void Depth(double d) => (D, HullZ, Sfx) = (d, -d - 0.22, d == 0.5 ? "" : $" {Py.G(d)}m");

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
        var takes = D < 0.75 ? "a jump seat, drawers, a rack, a component sled, a Murphy bed or a desk"
            : D < 1.25 ? "a suit rack, deep shelves, a long component sled, a toilet, a consumables rack, a CO2 scrubber or a water reclaimer"
            : "a spacewalk pack sled, a stretcher, deeper shelves, a module sled or an oxygen generator";
        var k = K("Wall Bay" + Sfx, "frame", $"An in-wall bay (draft): a 0.9 x 1.9 m opening in a ship's interior lining, {Py.G(D)} m deep into the void before the hull. It takes one insert: {takes}. Shown with the lining, deck and hull round it.");
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
        Rod(k, -Sw, 2.13, HullZ + 0.06, Sw, 2.21, HullZ + 0.14, "+x", PipeRed, "painted steel");
        Rod(k, -Sw, 2.13, HullZ + 0.17, Sw, 2.21, HullZ + 0.25, "+x", PipeBlue, "painted steel");
        Rod(k, -Sw, 0.16, HullZ + 0.04, Sw, 0.22, HullZ + 0.10, "+x", PipeYellow, "painted steel");
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
        var k = Insert("Drawer Stack Insert" + Sfx, $"An in-wall bay's drawer insert ({Py.G(D)} m deep, draft): the dividers and runners for four drawers.");
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
        var k = Mover($"Stow Drawer {i + 1}{Sfx}", $"A drawer in an in-wall bay ({Py.G(D)} m deep, draft): press its face and it runs out on its runners.", $"drawer{i}", [0, DrawerY(i), 0]);
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
        // What's in it, a different load in each: one load a half metre of depth (a deep drawer holds two, one behind the other).
        double f = b + 0.02;
        for (var off = 0.0; off > -D + 0.45; off -= 0.46)
        {
            void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin) => k.Box(x0, y0, z0 + off, x1, y1, z1 + off, col, fin);
            var back = off < 0;
            switch (i)
            {
                case 0: // boots and an extinguisher, lying down; behind them a coil of line and a hard case
                    if (back)
                    {
                        Rod(k, -0.36, f, -0.4 + off, -0.06, f + 0.1, -0.1 + off, "+y", Strap, "fabric");
                        B(0.04, f, -0.42, 0.36, f + 0.16, -0.08, Frame, "plastic");
                        break;
                    }
                    Rod(k, -0.36, f, -0.4 + off, -0.2, f + 0.16, -0.06 + off, "+z", Red, "gloss paint");
                    B(-0.12, f, -0.36, 0.1, f + 0.14, -0.08, Frame, "rubber");
                    B(0.14, f, -0.36, 0.36, f + 0.14, -0.08, Frame, "rubber");
                    break;
                case 1: // tool tray: foam with tools in their cut-outs
                    B(-0.38, f, -0.44, 0.38, f + 0.08, -0.03, "#3a3d33", "rubber");
                    B(-0.32, f + 0.08, -0.38, -0.27, f + 0.1, -0.08, Steel, "chrome");
                    B(-0.2, f + 0.08, -0.36, -0.1, f + 0.11, -0.1, back ? Hazard : Red, "plastic");
                    B(0.0, f + 0.08, -0.4, 0.04, f + 0.1, -0.06, Steel, "chrome");
                    B(0.12, f + 0.08, -0.3, 0.32, f + 0.12, -0.14, back ? Red : Hazard, "plastic");
                    break;
                case 2: // spare parts bins
                    for (var c = 0; c < 3; c++)
                        for (var r = 0; r < 2; r++)
                        {
                            double x = -0.37 + c * 0.25, z = -0.44 + r * 0.2;
                            B(x, f, z, x + 0.23, f + 0.18, z + 0.18, (r == 0) != back ? PipeBlue : Olive, "plastic");
                        }
                    break;
                default: // ration packs in two stacks
                    for (var r = 0; r < 3; r++)
                    {
                        B(-0.36, f + r * 0.08, -0.42, -0.04, f + r * 0.08 + 0.07, -0.1, Olive, "plastic");
                        B(0.04, f + r * 0.08, -0.42, 0.36, f + r * 0.08 + 0.07, -0.1, r == 1 ? Amber : Olive, "plastic");
                    }
                    break;
            }
        }
        Solid(k, -0.44, y0, -0.012, 0.44, y1, 0.018);
        Solid(k, -0.42, b, zb, 0.42, b + 0.02, -0.012);
        Slide(k.P, D - 0.08, Py.Round(0.9 + (D - 0.5) * 1.2, 2));
        return k.P.Stats(20, 300, 4, 1.5);
    }

    // ---- 3. Pull-out rack: the whole face runs out with a two-sided rack of shelves behind it

    private static Part RackInsert(bool stores = false)
    {
        var k = Insert((stores ? "Consumables Rack Insert" : "Pull-out Rack Insert") + Sfx, $"An in-wall bay's {(stores ? "consumables" : "pull-out")} rack insert ({Py.G(D)} m deep, draft): the top and bottom runners a tall rack runs out on{(stores ? ", and the twist latch's socket" : "")}.");
        Tab(k, "rack", Y0);
        if (stores)
            Tab(k, "latch", LatchY);
        k.Box(-0.06, Y0, -D, 0.06, Y0 + 0.03, -0.02, Steel, "steel");
        k.Box(-0.06, Y1 - 0.03, -D, 0.06, Y1, -0.02, Steel, "steel");
        return k.P.Stats(20, 400, 6, 3.0);
    }

    private static Part Rack(bool stores = false)
    {
        var k = stores
            ? Mover("Consumables Rack" + Sfx, $"A consumables rack in an in-wall bay ({Py.G(D)} m deep, draft): twist the latch and the rack runs out with the ship's stock on it: water pouches, rations, scrubber canisters, oxygen candles and filter cartridges.", "rack", [0, Y0, 0])
            : Mover("Pull-out Rack" + Sfx, $"A pull-out rack in an in-wall bay ({Py.G(D)} m deep, draft): the bay's face runs out of the wall with shelves behind it, reached from both sides: oxygen bottles, a med kit, filters, rations.", "rack", [0, Y0, 0]);
        k.Paint(-0.44, Y0 + 0.01, -0.012, 0.44, Y1 - 0.01, 0.018, Wall, "wall panelling");
        k.Paint(-0.44, Y0 + 0.01, -0.012, 0.44, 0.38, 0.022, WallLow, "painted steel");
        if (stores)
            k.Box(0.3, 0.85, 0.015, 0.38, 1.25, 0.04, Frame, "rubber"); // (a pull grip by the latch)
        else
            k.Box(-0.06, 1.0, 0.015, 0.06, 1.3, 0.03, Hazard, "hazard paint"); // (the press pad)
        k.Box(-0.22, 1.62, 0.015, 0.22, 1.7, 0.026, Frame, "plastic");
        k.Glow(-0.2, 1.635, 0.024, 0.2, 1.685, 0.03, stores ? Amber : Glow);
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
        // Loads: O2 bottles (white, green shoulder) on the bottom shelf, a med kit and filters, spare cells, rations; a deep rack again behind.
        for (var off = 0.0; off > -D + 0.45; off -= 0.47)
        {
            void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin) => k.Box(x0, y0, z0 + off, x1, y1, z1 + off, col, fin);
            if (stores)
            {
                StoresLoad(k, off);
                continue;
            }
            foreach (var s in new[] { -1.0, 1.0 })
                foreach (var z in new[] { -0.4, -0.22 })
                {
                    Rod(k, s * 0.33, 0.2, z + off, s * 0.17, 0.52, z + 0.15 + off, "+y", MedWhite, "gloss paint");
                    Rod(k, s * 0.31, 0.52, z + 0.02 + off, s * 0.19, 0.57, z + 0.13 + off, "+y", Green, "gloss paint");
                }
            B(-0.36, 0.62, -0.42, -0.06, 0.84, -0.06, MedWhite, "plastic");
            B(-0.25, 0.7, -0.062, -0.17, 0.76, -0.055, Red, "plastic");
            B(-0.23, 0.68, -0.062, -0.19, 0.78, -0.054, Red, "plastic");
            for (var c = 0; c < 3; c++)
                Rod(k, 0.06 + c * 0.11, 0.62, -0.36 + off, 0.15 + c * 0.11, 0.8, -0.27 + off, "+y", Olive, "plastic");
            for (var c = 0; c < 2; c++)
            {
                B(-0.37 + c * 0.17, 1.04, -0.4, -0.23 + c * 0.17, 1.3, -0.12, Frame, "plastic");
                k.Glow(-0.33 + c * 0.17, 1.24, -0.122 + off, -0.27 + c * 0.17, 1.27, -0.115 + off, Green);
            }
            B(0.05, 1.04, -0.44, 0.37, 1.16, -0.1, Olive, "plastic");
            B(0.05, 1.16, -0.44, 0.37, 1.28, -0.1, Amber, "plastic");
            B(-0.37, 1.46, -0.42, 0.37, 1.6, -0.06, Olive, "fabric"); // (folded blankets)
        }
        Solid(k, -0.44, Y0, -0.012, 0.44, Y1, 0.018);
        Slide(k.P, D - 0.04, Py.Round(1.4 + (D - 0.5) * 1.2, 2));
        return k.P.Stats(45, 400, 4, 2.0);
    }

    // ---- 4. Component sled: a cover flips up, then a sled runs out with a power cell on it, plugged in at the back

    private const double CovY0 = 0.70, CovY1 = 1.50;

    private static Part SledInsert()
    {
        var k = Insert("Component Bay Insert" + Sfx, $"An in-wall bay's component insert ({Py.G(D)} m deep, draft): a service panel under the sled, a gauge panel over it, the sled's rails and the bay's power plug at the back.");
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
        var k = Mover("Component Bay Cover" + Sfx, "The cover of an in-wall component bay (draft): a hinged panel that flips up on its top edge.", "cover", [0, CovY1, 0]);
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
        var k = Mover("Component Sled" + Sfx, $"A sled in an in-wall component bay ({Py.G(D)} m deep, draft): it runs out on the bay's rails with what it carries (here a power cell, a long one in the deep bay) and unplugs as it goes.", "sled", [0, CovY0, 0], Steel);
        double y = CovY0 + 0.01;
        // The tray and its front handle.
        k.Box(-0.42, y, -D + 0.06, 0.42, y + 0.03, -0.02, Steel, "tread plate");
        foreach (var s in new[] { -1.0, 1.0 })
            k.Box(s * 0.395, y + 0.03, -D + 0.06, s * 0.42, y + 0.07, -0.02, Steel, "steel");
        k.Paint(-0.42, y, -0.03, 0.42, y + 0.12, -0.005, Trim, "painted steel");
        Rod(k, -0.2, y + 0.05, -0.006, 0.2, y + 0.09, 0.03, "+x", Hazard, "plastic");
        double c0 = y + 0.03, c1 = c0 + 0.5;
        if (D > 1.2)
        {
            Coolant(k, c0, c1);
            Solid(k, -0.42, y, -D + 0.06, 0.42, y + 0.03, -0.02);
            Solid(k, -0.36, c0, -D + 0.08, 0.36, c1, -0.06);
            Slide(k.P, D - 0.05, Py.Round(1.2 + (D - 0.5) * 1.2, 2));
            return k.P.Stats(220, 900, 8, 3.0);
        }
        // The power cell: a cased block with cell bands, terminals, a carry handle and a status lamp.
        k.Paint(-0.34, c0, -D + 0.08, 0.34, c1, -0.05, Olive, "painted steel");
        for (var i = 0; i < 4; i++)
            k.Box(-0.345, c0 + 0.06 + i * 0.11, -D + 0.075, 0.345, c0 + 0.08 + i * 0.11, -0.045, Frame, "dirty steel");
        for (var z = -0.5; z > -D + 0.1; z -= 0.45) // (a long cell: cooling fins down its sides between the bands' blocks)
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.34, c0 + 0.1, z - 0.2, sx * 0.36, c1 - 0.1, z, Steel, "steel");
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
        Slide(k.P, D - 0.05, Py.Round(1.2 + (D - 0.5) * 1.2, 2));
        return k.P.Stats(60, 500, 6, 2.0);
    }

    /// <summary>The 1.5 m bay's heavy module: a coolant exchanger as long as the bay, fins down its sides, its pipes to the bay's plug, a fan on top.</summary>
    private static void Coolant(FittingLookParts.Kit k, double c0, double c1)
    {
        const string Body = "#4d6170";
        double zb = -D + 0.08;
        k.Paint(-0.36, c0, zb, 0.36, c1, -0.06, Body, "painted steel");
        for (var z = -0.12; z > zb + 0.06; z -= 0.08) // (fins down both sides)
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.36, c0 + 0.05, z - 0.03, sx * 0.38, c1 - 0.05, z, Frame, "dirty steel");
        foreach (var (z, col) in new[] { (zb + 0.25, PipeBlue), (zb + 0.55, PipeRed) })
        {
            Rod(k, -0.2, c1, z - 0.06, 0.2, c1 + 0.12, z + 0.06, "+x", col, "painted steel"); // (a header across the top)
            Rod(k, -0.05, c0 + 0.1, zb - 0.02, 0.05, c0 + 0.2, z, "+z", col, "painted steel"); // (its run to the plug at the back)
        }
        Rod(k, -0.22, c1, -0.6, 0.22, c1 + 0.06, -0.16, "+y", Frame, "plastic", 20); // (the fan's shroud)
        Rod(k, -0.18, c1 + 0.04, -0.56, 0.18, c1 + 0.075, -0.2, "+y", Steel, "steel", 20);
        k.Box(-0.3, c1 - 0.14, -0.062, 0.3, c1 - 0.06, -0.052, Hazard, "hazard paint");
        k.Box(-0.34, c0 + 0.04, -0.064, -0.14, c0 + 0.16, -0.05, Frame, "plastic");
        k.Glow(-0.32, c0 + 0.07, -0.056, -0.16, c0 + 0.13, -0.046, Glow);
        k.Glow(0.2, c0 + 0.08, -0.06, 0.32, c0 + 0.13, -0.05, Green);
        foreach (var x in new[] { -0.25, 0.25 }) // (front lifting handles)
            k.Box(x - 0.06, c1 - 0.04, -0.06, x + 0.06, c1 - 0.01, -0.0, Steel, "steel");
    }

    // ---- 0.5 m: a Murphy bed (the whole face flips down on its foot, a mattress on its back)

    private static Part BunkInsert()
    {
        var k = Insert("Murphy Bed Insert", "An in-wall bay's Murphy bed insert (draft): a dark berth behind a fold-down bed, with a reading lamp, a shelf and a net pocket.");
        Tab(k, "bunk", Y0);
        k.Paint(-W, Y0, -D + 0.015, W, Y1, -D + 0.04, Pad, "fabric"); // (padded back of the berth)
        k.Box(-0.4, 1.32, -D + 0.04, 0.4, 1.345, -0.26, Recess, "dirty steel"); // (a shelf)
        k.Box(-0.34, 1.345, -D + 0.08, -0.22, 1.5, -0.34, PipeBlue, "plastic"); // (books)
        k.Box(-0.2, 1.345, -D + 0.08, -0.12, 1.48, -0.34, Olive, "plastic");
        Rod(k, 0.12, 1.345, -0.42, 0.2, 1.45, -0.34, "+y", MedWhite, "plastic"); // (a mug)
        k.Box(-0.3, 1.7, -D + 0.04, 0.3, 1.76, -D + 0.1, Frame, "plastic");
        k.Glow(-0.28, 1.705, -D + 0.098, 0.28, 1.755, -D + 0.106, "#ffd9a0"); // (the reading lamp, warm)
        k.Box(-0.36, 0.5, -D + 0.04, 0.36, 0.95, -D + 0.07, Strap, "fabric"); // (a net pocket)
        return k.P.Stats(30, 400, 4, 3.0);
    }

    private static Part Bunk()
    {
        const double H = Y0 + 0.02;
        var k = Mover("Murphy Bed", "A Murphy bed in an in-wall bay (draft): the bay's whole face flips down on its foot to a made bed. Press the bar at the top.", "bunk", [0, Y0, 0]);
        k.Paint(-0.44, H, 0.0, 0.44, Y1 - 0.02, 0.025, Wall, "wall panelling");
        k.Paint(-0.44, H, 0.02, 0.44, 0.38, 0.03, WallLow, "painted steel");
        k.Box(-0.2, Y1 - 0.12, 0.022, 0.2, Y1 - 0.08, 0.036, Hazard, "hazard paint"); // (the press bar)
        k.Glow(0.26, Y1 - 0.12, 0.024, 0.34, Y1 - 0.08, 0.034, Green);
        foreach (var x in new[] { -0.36, 0.36 }) // (its two legs, folded flat on the face; they would swing down to stand it)
            k.Box(x - 0.025, 1.2, 0.024, x + 0.025, Y1 - 0.2, 0.05, Steel, "steel");
        // The back: a frame, the mattress, the pillow at the wall end (the foot of the panel), a blanket at the far end, two straps.
        k.Box(-0.42, H + 0.02, -0.02, 0.42, Y1 - 0.04, 0.0, Frame, "dirty steel");
        k.Paint(-0.4, H + 0.04, -0.16, 0.4, Y1 - 0.06, -0.02, Pad, "fabric");
        k.Paint(-0.32, H + 0.08, -0.22, 0.32, H + 0.4, -0.16, MedWhite, "fabric");
        k.Paint(-0.41, 1.05, -0.185, 0.41, Y1 - 0.07, -0.16, Olive, "fabric");
        foreach (var y in new[] { 0.75, 1.45 })
            k.Box(-0.42, y, -0.2, 0.42, y + 0.05, -0.185, Strap, "fabric");
        Solid(k, -0.44, H, -0.16, 0.44, Y1 - 0.02, 0.025);
        Swing(k.P, "+x", 90, V(0, H, 0.025), 1.6);
        return k.P.Stats(45, 400, 4, 2.0);
    }

    // ---- 0.5 m: a fold-down desk (a waist-high panel flips down to a worktop, a pegboard and a screen behind)

    private const double DeskY0 = 0.76, DeskY1 = 1.36;

    private static Part DeskInsert()
    {
        var k = Insert("Desk Insert", "An in-wall bay's desk insert (draft): a cabinet under a fold-down worktop, a tool pegboard behind it and a screen over it.");
        Tab(k, "desk", DeskY0);
        // Under: a cabinet face with vents.
        k.Paint(-W, Y0, -0.04, W, DeskY0 - 0.01, -0.012, Wall, "wall panelling");
        k.Paint(-W, Y0, -0.04, W, 0.38, -0.008, WallLow, "painted steel");
        for (var i = 0; i < 3; i++)
            k.Box(-0.3, 0.5 + i * 0.05, -0.014, 0.3, 0.52 + i * 0.05, -0.006, Frame, "dirty steel");
        Solid(k, -W, Y0, -0.04, W, DeskY0 - 0.01, -0.012);
        // Behind the worktop: a pegboard with tools on it, a little lamp.
        k.Box(-W, DeskY0, -0.3, W, DeskY1, -0.28, Trim, "plastic");
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 8; c++)
                k.Box(-0.38 + c * 0.1, DeskY0 + 0.1 + r * 0.12, -0.282, -0.37 + c * 0.1, DeskY0 + 0.11 + r * 0.12, -0.272, Frame, "dirty steel");
        k.Box(-0.34, DeskY0 + 0.14, -0.28, -0.3, DeskY0 + 0.44, -0.25, Steel, "chrome"); // (a spanner)
        k.Box(-0.22, DeskY0 + 0.16, -0.28, -0.18, DeskY0 + 0.4, -0.25, Red, "plastic"); // (a driver)
        k.Box(-0.06, DeskY0 + 0.2, -0.28, 0.12, DeskY0 + 0.42, -0.24, Hazard, "plastic"); // (a meter)
        k.Box(0.2, DeskY0 + 0.12, -0.28, 0.36, DeskY0 + 0.3, -0.22, Frame, "plastic"); // (a parts box)
        k.Glow(-0.3, DeskY1 - 0.04, -0.27, 0.3, DeskY1 - 0.02, -0.12, "#ffd9a0");
        k.Box(-W, DeskY0 - 0.01, -0.3, W, DeskY0, -0.04, Recess, "dirty steel");
        k.Box(-W, DeskY1, -0.3, W, DeskY1 + 0.01, -0.04, Recess, "dirty steel");
        // Over: a panel with a screen and two switches.
        k.Paint(-W, DeskY1 + 0.01, -0.04, W, Y1, -0.012, Wall, "wall panelling");
        k.Box(-0.36, 1.48, -0.014, 0.2, 1.86, 0.0, Frame, "plastic");
        k.Glow(-0.34, 1.5, -0.002, 0.18, 1.84, 0.004, Glow);
        foreach (var y in new[] { 1.55, 1.72 })
        {
            k.Box(0.27, y, -0.014, 0.37, y + 0.1, 0.0, Trim, "plastic");
            k.Glow(0.3, y + 0.03, -0.002, 0.34, y + 0.07, 0.006, y < 1.6 ? Green : Amber);
        }
        Solid(k, -W, DeskY1 + 0.01, -0.04, W, Y1, -0.012);
        return k.P.Stats(35, 500, 6, 3.0);
    }

    private static Part Desk()
    {
        var k = Mover("Fold-down Desk", "A fold-down desk in an in-wall bay (draft): a waist-high panel that flips down on its foot to a worktop with a keyboard.", "desk", [0, DeskY0, 0]);
        k.Paint(-0.44, DeskY0 + 0.005, 0.0, 0.44, DeskY1 - 0.005, 0.025, Wall, "wall panelling");
        k.Box(-0.16, DeskY1 - 0.08, 0.022, 0.16, DeskY1 - 0.045, 0.036, Hazard, "hazard paint");
        k.Paint(-0.43, DeskY0 + 0.01, -0.045, 0.43, DeskY1 - 0.01, 0.0, "#59605a", "plastic"); // (the worktop)
        k.Box(-0.22, DeskY0 + 0.18, -0.062, 0.18, DeskY0 + 0.34, -0.045, Frame, "plastic"); // (the keyboard)
        k.Box(0.24, DeskY0 + 0.2, -0.058, 0.32, DeskY0 + 0.3, -0.045, Frame, "plastic"); // (a pad)
        k.Glow(-0.43, DeskY1 - 0.03, -0.05, 0.43, DeskY1 - 0.015, -0.04, Glow); // (a lit front edge, open)
        Solid(k, -0.44, DeskY0, -0.045, 0.44, DeskY1, 0.025);
        Swing(k.P, "+x", 90, V(0, DeskY0, 0.025), 1.0);
        return k.P.Stats(14, 300, 4, 1.5);
    }

    // ---- 1 m: a suit rack (a pressure suit hangs side-on on a hanger that runs right out of the wall)

    private static Part SuitInsert()
    {
        var k = Insert("Suit Rack Insert" + Sfx, $"An in-wall bay's suit rack insert ({Py.G(D)} m deep, draft): the top rail a suit's hanger runs on, the floor guide and a charge point.");
        Tab(k, "suit", Y0);
        k.Box(-0.05, Y1 - 0.04, -D, 0.05, Y1, -0.03, Steel, "steel");
        k.Box(-0.06, Y0, -D, 0.06, Y0 + 0.02, -0.03, Steel, "steel");
        k.Box(0.2, 1.3, -D + 0.015, 0.36, 1.46, -D + 0.05, Frame, "plastic");
        k.Glow(0.24, 1.34, -D + 0.048, 0.32, 1.42, -D + 0.056, Green);
        return k.P.Stats(25, 400, 6, 3.0);
    }

    private static Part SuitRack()
    {
        const string Suit = "#d8d4c8", Band = Strap;
        var k = Mover("Suit Rack" + Sfx, $"A suit rack in an in-wall bay ({Py.G(D)} m deep, draft): press the face and a pressure suit on its hanger runs right out of the wall, side-on, with two spare bottles at its feet.", "suit", [0, Y0, 0]);
        k.Paint(-0.44, Y0 + 0.01, -0.012, 0.44, Y1 - 0.01, 0.018, Wall, "wall panelling");
        k.Paint(-0.44, Y0 + 0.01, -0.012, 0.44, 0.38, 0.022, WallLow, "painted steel");
        k.Box(-0.06, 1.0, 0.015, 0.06, 1.3, 0.03, Hazard, "hazard paint");
        k.Box(-0.2, 1.6, 0.015, 0.2, 1.7, 0.026, Frame, "plastic");
        k.Glow(-0.18, 1.615, 0.024, 0.18, 1.685, 0.03, Glow);
        double zf = -D + 0.05, zc = -D / 2;
        // The hanger: a bar along the top rail, a hook down to the suit's shoulders; a tray along the floor.
        k.Box(-0.02, Y1 - 0.08, zf, 0.02, Y1 - 0.04, -0.012, Steel, "steel");
        k.Box(-0.015, 1.84, zc - 0.015, 0.015, Y1 - 0.08, zc + 0.015, Steel, "steel");
        k.Box(-0.22, Y0 + 0.01, zf, 0.22, Y0 + 0.04, -0.012, Steel, "tread plate");
        // The suit, side-on (its chest to +x): boots, legs, torso, arms, gloves, the cube helmet with its visor, the pack behind.
        foreach (var dz in new[] { -0.1, 0.1 })
        {
            k.Box(-0.09, Y0 + 0.04, zc + dz - 0.07, 0.15, Y0 + 0.16, zc + dz + 0.07, Frame, "rubber");
            k.Paint(-0.08, Y0 + 0.16, zc + dz - 0.065, 0.08, 0.98, zc + dz + 0.065, Suit, "fabric");
            k.Box(-0.085, 0.55, zc + dz - 0.07, 0.085, 0.6, zc + dz + 0.07, Band, "fabric"); // (knee band)
        }
        k.Paint(-0.13, 0.96, zc - 0.22, 0.13, 1.5, zc + 0.22, Suit, "fabric");
        k.Box(-0.135, 1.2, zc - 0.225, 0.135, 1.26, zc + 0.225, Band, "fabric");
        k.Box(0.12, 1.28, zc - 0.1, 0.14, 1.42, zc + 0.1, Frame, "plastic"); // (chest panel)
        k.Glow(0.135, 1.32, zc - 0.06, 0.145, 1.36, zc + 0.06, Green);
        foreach (var sz in new[] { -1.0, 1.0 })
        {
            k.Paint(-0.06, 0.86, zc + sz * 0.22, 0.07, 1.46, zc + sz * 0.29, Suit, "fabric");
            k.Box(-0.06, 0.8, zc + sz * 0.22, 0.07, 0.88, zc + sz * 0.29, Frame, "rubber");
        }
        k.Paint(-0.13, 1.5, zc - 0.14, 0.13, 1.8, zc + 0.14, Suit, "gloss paint");
        k.Glass(0.13, 1.56, zc - 0.11, 0.142, 1.74, zc + 0.11);
        k.Box(-0.3, 1.0, zc - 0.17, -0.13, 1.5, zc + 0.17, Olive, "painted steel"); // (the life-support pack)
        // Spare bottles on the tray, behind the suit.
        foreach (var dz in new[] { -0.12, 0.0 })
            Rod(k, -0.06, Y0 + 0.04, zf + 0.03 + dz + 0.12, 0.06, Y0 + 0.5, zf + 0.15 + dz + 0.12, "+y", MedWhite, "gloss paint");
        Solid(k, -0.44, Y0, -0.012, 0.44, Y1, 0.018);
        Solid(k, -0.22, Y0 + 0.01, zf, 0.22, Y0 + 0.04, -0.012);
        Slide(k.P, D - 0.04, 2.0);
        return k.P.Stats(70, 400, 4, 2.0);
    }

    // ---- 1.5 m: a stretcher drawer (a waist-high tray as long as the bay, a med cabinet over it)

    private const double StrY0 = 0.78, StrY1 = 1.06;

    private static Part StretcherInsert()
    {
        var k = Insert("Stretcher Insert" + Sfx, $"An in-wall bay's stretcher insert ({Py.G(D)} m deep, draft): a cabinet under a long stretcher drawer, its runners, and a med cabinet over it.");
        Tab(k, "stretcher", StrY0);
        k.Paint(-W, Y0, -0.04, W, StrY0 - 0.01, -0.012, Wall, "wall panelling");
        k.Paint(-W, Y0, -0.04, W, 0.38, -0.008, WallLow, "painted steel");
        Solid(k, -W, Y0, -0.04, W, StrY0 - 0.01, -0.012);
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.42, StrY0 + 0.02, -D + 0.02, sx * W, StrY0 + 0.05, -0.04, Steel, "steel");
        k.Box(-W, StrY0 - 0.01, -D, W, StrY0, -0.04, Recess, "dirty steel");
        // The med cabinet over it: white, a red cross that glows, a green status lamp.
        k.Paint(-W, StrY1 + 0.01, -0.04, W, Y1, -0.012, MedWhite, "gloss paint");
        // (the cross's two bars a centimetre and more thick, their faces 2 mm apart: grown thin boxes were trimmed under 1 cm and refused)
        k.Glow(-0.04, 1.42, -0.03, 0.04, 1.7, -0.002, Red);
        k.Glow(-0.14, 1.52, -0.026, 0.14, 1.6, -0.004, Red);
        k.Glow(0.3, 1.82, -0.026, 0.36, 1.86, -0.004, Green);
        k.Box(-0.4, StrY1 + 0.06, -0.016, 0.4, StrY1 + 0.09, -0.006, Trim, "plastic");
        Solid(k, -W, StrY1 + 0.01, -0.04, W, Y1, -0.012);
        k.Box(-W, StrY1, -D, W, StrY1 + 0.01, -0.04, Recess, "dirty steel");
        return k.P.Stats(40, 500, 6, 3.0);
    }

    private static Part Stretcher()
    {
        var k = Mover("Stretcher Drawer" + Sfx, $"A stretcher drawer in an in-wall bay ({Py.G(D)} m deep, draft): a waist-high tray as long as the bay runs out with a padded stretcher on it.", "stretcher", [0, StrY0, 0]);
        k.Paint(-0.44, StrY0 + 0.008, -0.012, 0.44, StrY1 - 0.008, 0.018, MedWhite, "gloss paint");
        k.Box(-0.2, StrY1 - 0.08, 0.015, 0.2, StrY1 - 0.045, 0.03, Hazard, "hazard paint");
        k.Glow(0.26, StrY1 - 0.08, 0.016, 0.38, StrY1 - 0.045, 0.026, Red);
        double zb = -D + 0.05, t = StrY0 + 0.03;
        k.Box(-0.42, t, zb, 0.42, t + 0.03, -0.012, Steel, "steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.4, t + 0.03, zb, sx * 0.42, t + 0.09, -0.012, Steel, "steel");
            k.Box(sx * 0.42, t, zb, sx * 0.435, t + 0.03, -0.02, Steel, "steel");
        }
        k.Paint(-0.32, t + 0.03, zb + 0.04, 0.32, t + 0.1, -0.06, "#3d6a6a", "fabric"); // (the pad)
        k.Paint(-0.26, t + 0.1, zb + 0.06, 0.26, t + 0.16, zb + 0.32, MedWhite, "fabric"); // (the pillow, at the far end)
        foreach (var z in new[] { -0.45, -0.85 })
            k.Box(-0.34, t + 0.1, z, 0.34, t + 0.115, z + 0.06, Strap, "fabric");
        k.Box(0.12, t + 0.03, -0.3, 0.36, t + 0.13, -0.08, MedWhite, "plastic"); // (a trauma kit at the near end)
        k.Box(0.21, t + 0.13, -0.22, 0.27, t + 0.14, -0.16, Red, "plastic");
        Solid(k, -0.44, StrY0, -0.012, 0.44, StrY1, 0.018);
        Solid(k, -0.42, t, zb, 0.42, t + 0.03, -0.012);
        Slide(k.P, D - 0.08, 2.2);
        return k.P.Stats(40, 300, 4, 2.0);
    }

    // ---- 1.5 m: a spacewalk pack bay (an MMU on a stand rides a floor sled right out into the room, backed up to be put on)

    private static Part MmuInsert()
    {
        var k = Insert("MMU Bay Insert" + Sfx, $"An in-wall bay's MMU insert ({Py.G(D)} m deep, draft): the floor rails a spacewalk pack's sled runs on, a fuel line and charge point at the back, a lamp, and the hinge of the door in front.");
        Tab(k, "mmu", Y0);
        Tab(k, "door", 1.0);
        foreach (var sx in new[] { -0.3, 0.3 })
            k.Box(sx - 0.03, Y0, -D, sx + 0.03, Y0 + 0.02, -0.03, Steel, "steel");
        k.Box(-0.2, 1.0, -D + 0.015, 0.2, 1.3, -D + 0.07, Frame, "plastic");
        k.Glow(-0.16, 1.24, -D + 0.068, -0.04, 1.28, -D + 0.076, Green);
        k.Glow(0.04, 1.24, -D + 0.068, 0.16, 1.28, -D + 0.076, Amber);
        Rod(k, 0.1, 1.0, -D + 0.03, 0.16, 1.06, -D + 0.3, "+z", PipeYellow, "rubber"); // (the fuel line, coiled short)
        k.Glow(-0.3, Y1 - 0.04, -D + 0.1, 0.3, Y1 - 0.02, -0.2, "#e8f0ff");
        return k.P.Stats(30, 500, 6, 3.0);
    }

    // (the face is a door that swings aside first, so nothing stands between the pack and the person backing into it)
    private static Part MmuDoor()
    {
        var k = Mover("MMU Bay Door" + Sfx, $"The door of a spacewalk pack bay ({Py.G(D)} m deep, draft): a full-height lining panel that swings aside on its left edge, then the pack's sled runs out.", "door", [0, 1.0, 0]);
        k.Paint(-0.43, Y0 + 0.01, -0.012, 0.43, Y1 - 0.01, 0.018, Wall, "wall panelling");
        k.Paint(-0.43, Y0 + 0.01, -0.012, 0.43, 0.38, 0.022, WallLow, "painted steel");
        k.Box(0.3, 0.9, 0.015, 0.36, 1.3, 0.028, Frame, "plastic"); // (the pull)
        k.Box(-0.06, 1.0, 0.015, 0.06, 1.3, 0.03, Hazard, "hazard paint"); // (the press pad)
        k.Box(-0.22, 1.6, 0.015, 0.22, 1.7, 0.026, Frame, "plastic");
        k.Glow(-0.2, 1.615, 0.024, 0.2, 1.685, 0.03, Amber);
        foreach (var y in new[] { 0.3, 1.0, 1.7 })
            Rod(k, -0.45, y, 0.0, -0.42, y + 0.08, 0.03, "+y", Steel, "steel"); // (hinge knuckles on the left edge)
        Solid(k, -0.43, Y0 + 0.01, -0.012, 0.43, Y1 - 0.01, 0.018);
        Swing(k.P, "+y", -90, V(-0.435, 0, 0.018), 1.0);
        return k.P.Stats(16, 300, 4, 1.5);
    }

    private static Part MmuSled()
    {
        const string Pack = "#c9c3b0";
        var k = Mover("MMU Sled" + Sfx, $"A spacewalk pack bay's sled ({Py.G(D)} m deep, draft): with the door aside, the MMU on its stand rides 1.2 m out into the room on a floor sled, its back to the wall, so you back into it from the room and buckle on.", "mmu", [0, Y0, 0]);
        double zb = -D + 0.05, f = Y0 + 0.02;
        // The sled: a plate on the rails with a hazard edge at its front, a post up to the pack's cradle.
        k.Box(-0.36, f, zb, 0.36, f + 0.03, -0.012, Steel, "tread plate");
        for (var i = 0; i < 6; i++)
            k.Box(-0.34 + i * 0.12, f + 0.03, -0.1, -0.28 + i * 0.12, f + 0.038, -0.03, i % 2 == 0 ? Hazard : Frame, "hazard paint");
        k.Box(-0.05, f + 0.03, -0.95, 0.05, 0.92, -0.85, Steel, "steel");
        // The pack (its back to the wall side, -z; a person stands at +z): the body, two fuel bottles, the arms forward with hand controls,
        // a thruster block at each corner.
        k.Paint(-0.3, 0.92, -0.98, 0.3, 1.62, -0.72, Pack, "painted plating");
        foreach (var sx in new[] { -0.17, 0.17 })
            Rod(k, sx - 0.08, 0.98, -1.08, sx + 0.08, 1.56, -0.98, "+y", MedWhite, "gloss paint");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.3, 1.02, -0.9, sx * 0.36, 1.08, -0.4, Pack, "painted plating"); // (the arm)
            k.Box(sx * 0.29, 1.0, -0.44, sx * 0.37, 1.16, -0.36, Frame, "plastic"); // (its hand control)
            foreach (var y in new[] { 0.94, 1.56 })
            {
                k.Box(sx * 0.3, y - 0.05, -1.0, sx * 0.38, y + 0.05, -0.9, Frame, "dirty steel");
                Rod(k, sx * 0.38, y - 0.02, -0.97, sx * 0.4, y + 0.02, -0.93, "+x", Steel, "chrome");
            }
        }
        k.Box(-0.2, 1.3, -0.72, 0.2, 1.36, -0.7, Strap, "fabric"); // (the harness across its front)
        k.Box(-0.2, 1.0, -0.72, 0.2, 1.06, -0.7, Strap, "fabric");
        k.Glow(0.12, 1.5, -0.72, 0.24, 1.56, -0.71, Green);
        Solid(k, -0.36, f, zb, 0.36, f + 0.03, -0.012);
        Slide(k.P, 1.2, 2.6);
        return k.P.Stats(160, 500, 6, 2.0);
    }

    // ---- the twist latch (Karl, 2026-11-20: "a big handle you twist on the side to interact and pull it out"): the second way a bay
    // opens. A big handle on the bay's right jamb, up at rest; twist it a quarter turn outward and the fill lets go, then pull it out.
    // It is its own event door on the insert (a door can't ride another door's leaf in the game yet: Construct.Doors.cs), so it stays
    // on the jamb while the fill runs out; one part, built at LatchY, hung at whatever height its insert's "latch" tab is.

    private const double LatchY = 1.0;

    private static Part TwistLatch()
    {
        var k = Mover("Twist Latch", "A big twist handle on an in-wall bay's jamb (draft): twist it a quarter turn outward to let the fill go, then pull the fill out.", "latch", [0, LatchY, 0], Hazard);
        // (on the bezel, its boss and grip clear of the fill's face, which runs out past it to x 0.44)
        double x = W + 0.06, y = LatchY + 0.01;
        Rod(k, x - 0.065, y - 0.065, 0.021, x + 0.065, y + 0.065, 0.045, "+z", Frame, "dirty steel", 20); // (the boss on the jamb)
        Rod(k, x - 0.045, y - 0.045, 0.045, x + 0.045, y + 0.045, 0.085, "+z", Steel, "steel", 16); // (the hub)
        k.Box(x - 0.028, y + 0.03, 0.05, x + 0.028, y + 0.3, 0.082, Hazard, "hazard paint"); // (the arm, up)
        Rod(k, x - 0.062, y + 0.27, 0.042, x + 0.062, y + 0.33, 0.1, "+x", Frame, "rubber"); // (the grip across its end)
        k.Glow(x - 0.012, y - 0.012, 0.084, x + 0.012, y + 0.012, 0.09, Green);
        Swing(k.P, "+z", -90, V(x, y, 0.06), 0.6);
        return k.P.Stats(4, 200, 3, 1.0);
    }

    /// <summary>The consumables rack's load, one rack-deep at <paramref name="off"/>: water pouches, rations, scrubber canisters, O2 candles and filters.</summary>
    private static void StoresLoad(FittingLookParts.Kit k, double off)
    {
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin) => k.Box(x0, y0, z0 + off, x1, y1, z1 + off, col, fin);
        foreach (var s in new[] { -1.0, 1.0 })
        {
            // Bottom shelf: water pouches, stacked two high.
            for (var r = 0; r < 2; r++)
                for (var c = 0; c < 2; c++)
                    B(s * (0.05 + c * 0.17), 0.2 + r * 0.1, -0.42, s * (0.2 + c * 0.17), 0.29 + r * 0.1, -0.08, r == 0 ? PipeBlue : "#4a86b8", "plastic");
            // Second: ration packs.
            for (var r = 0; r < 3; r++)
                B(s * 0.05, 0.62 + r * 0.075, -0.42, s * 0.37, 0.69 + r * 0.075, -0.08, r == 1 ? Amber : Olive, "plastic");
            // Third: scrubber canisters, white with a green band, standing.
            foreach (var z in new[] { -0.36, -0.18 })
            {
                Rod(k, s * 0.08, 1.04, z - 0.06 + off, s * 0.2, 1.36, z + 0.06 + off, "+y", MedWhite, "gloss paint");
                Rod(k, s * 0.075, 1.24, z - 0.065 + off, s * 0.205, 1.28, z + 0.065 + off, "+y", Green, "gloss paint");
                Rod(k, s * 0.24, 1.04, z - 0.06 + off, s * 0.36, 1.36, z + 0.06 + off, "+y", MedWhite, "gloss paint");
                Rod(k, s * 0.235, 1.24, z - 0.065 + off, s * 0.365, 1.28, z + 0.065 + off, "+y", Green, "gloss paint");
            }
        }
        // Top shelf: oxygen candles lying in a tray (red, a white band) on one side, filter cartridges standing on the other.
        for (var c = 0; c < 3; c++)
        {
            Rod(k, -0.36 + c * 0.1, 1.46, -0.42 + off, -0.28 + c * 0.1, 1.54, -0.1 + off, "+z", Red, "gloss paint");
            B(-0.365 + c * 0.1, 1.455, -0.3, -0.275 + c * 0.1, 1.545, -0.27, MedWhite, "plastic");
        }
        foreach (var x in new[] { 0.1, 0.27 })
            foreach (var z in new[] { -0.36, -0.18 })
            {
                Rod(k, x - 0.06, 1.46, z - 0.06 + off, x + 0.06, 1.74, z + 0.06 + off, "+y", MedWhite, "plastic");
                Rod(k, x - 0.062, 1.74, z - 0.062 + off, x + 0.062, 1.78, z + 0.062 + off, "+y", PipeBlue, "plastic");
            }
    }

    // ---- 1 m: a toilet (twist the side handle and the pan runs out of the wall; you sit with your back to the wall)

    private const double PanY1 = 0.46;

    private static Part ToiletInsert()
    {
        var k = Insert("Toilet Insert" + Sfx, $"An in-wall bay's toilet insert ({Py.G(D)} m deep, draft): the waste tank and vacuum fan at the back, a padded back rest, a grab bar, the urine funnel on its hose and a wipes dispenser over the pan.");
        Tab(k, "pan", Y0);
        Tab(k, "latch", 0.3);
        // At the back, under the panel: the waste tank, its vacuum fan and the pipe up into the void.
        k.Paint(-0.36, Y0 + 0.02, -D + 0.015, 0.36, 0.5, -D + 0.34, Olive, "painted steel");
        k.Box(-0.37, 0.3, -D + 0.015, 0.37, 0.34, -D + 0.345, Frame, "dirty steel");
        Rod(k, -0.12, 0.5, -D + 0.06, 0.12, 0.62, -D + 0.3, "+y", Frame, "dirty steel", 16);
        Rod(k, -0.04, 0.62, -D + 0.14, 0.04, Y1, -D + 0.22, "+y", Steel, "steel");
        Rod(k, -0.05, 0.36, -D + 0.34, 0.05, 0.46, -0.62, "+z", Frame, "rubber"); // (the flexible hose to the pan)
        // The panel over the pan and what is on it.
        k.Paint(-W, PanY1 + 0.04, -0.06, W, Y1, -0.04, Wall, "wall panelling");
        Solid(k, -W, PanY1 + 0.04, -0.06, W, Y1, -0.04);
        k.Box(-W, PanY1, -D, W, PanY1 + 0.04, -0.06, Recess, "dirty steel");
        k.Paint(-0.3, 0.62, -0.04, 0.3, 1.05, 0.04, Pad, "fabric"); // (the back rest)
        Rod(k, -0.38, 1.12, 0.06, 0.38, 1.16, 0.1, "+x", Steel, "chrome"); // (the grab bar)
        foreach (var x in new[] { -0.34, 0.34 })
            k.Box(x - 0.02, 1.12, -0.04, x + 0.02, 1.16, 0.07, Steel, "steel");
        Rod(k, 0.2, 1.3, -0.04, 0.4, 1.5, 0.02, "+z", Frame, "rubber", 16); // (the funnel's hose, coiled on its hook)
        Rod(k, 0.26, 1.5, -0.02, 0.34, 1.64, 0.06, "+y", Hazard, "plastic", 16); // (the funnel)
        k.Box(-0.38, 1.3, -0.04, -0.18, 1.46, 0.04, MedWhite, "plastic"); // (the wipes dispenser)
        k.Box(-0.34, 1.34, 0.036, -0.22, 1.36, 0.046, Frame, "plastic");
        k.Glow(-0.24, 1.42, 0.04, -0.2, 1.44, 0.046, Green);
        k.Box(-0.2, 1.78, -0.044, 0.2, 1.92, -0.03, Frame, "plastic"); // (the extract fan's grille)
        for (var i = 0; i < 4; i++)
            k.Box(-0.18, 1.8 + i * 0.03, -0.032, 0.18, 1.81 + i * 0.03, -0.024, Steel, "steel");
        k.Glow(-0.3, Y1 - 0.04, -0.03, 0.3, Y1 - 0.02, 0.02, "#e8f0ff");
        return k.P.Stats(80, 500, 6, 3.0);
    }

    private static Part ToiletPan()
    {
        var k = Mover("Toilet Pan" + Sfx, $"An in-wall toilet's pan ({Py.G(D)} m deep, draft): twist the latch and it runs out of the wall on its runners. Its face is a lining panel with a foot bar; the seat is on top.", "pan", [0, Y0, 0]);
        k.Paint(-0.44, Y0 + 0.01, -0.012, 0.44, PanY1, 0.018, Wall, "wall panelling");
        k.Paint(-0.44, Y0 + 0.01, -0.012, 0.44, 0.3, 0.022, WallLow, "painted steel");
        Rod(k, -0.3, Y0 + 0.05, 0.022, 0.3, Y0 + 0.09, 0.062, "+x", Steel, "chrome"); // (the foot bar)
        k.Box(-0.42, PanY1 - 0.03, -0.012, -0.3, PanY1 - 0.005, 0.024, Frame, "rubber"); // (a pull grip on the top edge)
        // The pan behind the face: a white body, the padded seat ring on top, the bowl's dark mouth.
        const double Zc = -0.3;
        k.Paint(-0.22, Y0 + 0.02, -0.58, 0.22, PanY1 - 0.03, -0.012, MedWhite, "gloss paint");
        Rod(k, -0.21, PanY1 - 0.03, Zc - 0.23, 0.21, PanY1, Zc + 0.19, "+y", Pad, "rubber", 24);
        Rod(k, -0.12, PanY1 - 0.025, Zc - 0.15, 0.12, PanY1 + 0.006, Zc + 0.11, "+y", Frame, "rubber", 24);
        k.Box(-0.24, Y0 + 0.02, -0.6, 0.24, Y0 + 0.08, -0.56, Steel, "steel"); // (its back plate, where the hose meets it)
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.22, Y0 + 0.03, -0.58, sx * 0.24, Y0 + 0.06, -0.02, Steel, "steel"); // (runners)
        Solid(k, -0.44, Y0, -0.012, 0.44, PanY1, 0.018);
        Solid(k, -0.22, Y0 + 0.02, -0.58, 0.22, PanY1 - 0.03, -0.012);
        Slide(k.P, 0.6, 1.4);
        return k.P.Stats(35, 300, 4, 2.0);
    }

    // ---- ship systems that take consumables: each holds its stock on a tray you pull out to change it

    // 1 m: a CO2 scrubber. A tray of absorbent canisters (one spent, its band red), the fan under it, the manifold and duct behind.
    private const double ScrY0 = 0.62, ScrY1 = 1.40;

    private static Part ScrubberInsert()
    {
        var k = Insert("CO2 Scrubber Insert" + Sfx, $"An in-wall CO2 scrubber ({Py.G(D)} m deep, draft): the fan under the canister tray, the manifold and duct behind it, a CO2 readout over it.");
        Tab(k, "tray", ScrY0);
        Tab(k, "latch", LatchY);
        // Under: the fan's grille and the fan behind it.
        k.Paint(-W, Y0, -0.04, W, ScrY0 - 0.01, -0.012, Wall, "wall panelling");
        k.Paint(-W, Y0, -0.04, W, 0.3, -0.008, WallLow, "painted steel");
        Rod(k, -0.2, 0.18, -0.014, 0.2, 0.58, 0.0, "+z", Frame, "dirty steel", 24);
        foreach (var y in new[] { 0.28, 0.38, 0.48 })
            k.Box(-0.18, y - 0.008, -0.002, 0.18, y + 0.008, 0.006, Steel, "steel");
        Rod(k, -0.17, 0.21, -0.3, 0.17, 0.55, -0.2, "+z", Steel, "steel", 20);
        Solid(k, -W, Y0, -0.04, W, ScrY0 - 0.01, -0.012);
        // Over: a CO2 bar gauge, a screen, an amber warning lamp.
        k.Paint(-W, ScrY1 + 0.01, -0.04, W, Y1, -0.012, Wall, "wall panelling");
        k.Box(-0.38, 1.5, -0.014, -0.28, 1.9, 0.0, Frame, "plastic");
        for (var i = 0; i < 6; i++)
            k.Glow(-0.36, 1.52 + i * 0.06, -0.002, -0.3, 1.56 + i * 0.06, 0.004, i < 4 ? Green : i == 4 ? Amber : Frame);
        k.Box(-0.2, 1.52, -0.014, 0.22, 1.86, 0.0, Frame, "plastic");
        k.Glow(-0.18, 1.54, -0.002, 0.2, 1.84, 0.004, Glow);
        k.Glow(0.3, 1.78, -0.012, 0.38, 1.86, -0.004, Amber);
        Solid(k, -W, ScrY1 + 0.01, -0.04, W, Y1, -0.012);
        // The bay: rails, the manifold the canisters seat into, the duct up into the void.
        k.Box(-W, ScrY0 - 0.01, -D, W, ScrY0, -0.04, Recess, "dirty steel");
        k.Box(-W, ScrY1, -D, W, ScrY1 + 0.01, -0.04, Recess, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.42, ScrY0, -D, sx * W, ScrY0 + 0.04, -0.04, Steel, "steel");
        k.Box(-0.42, ScrY0 + 0.02, -D + 0.015, 0.42, ScrY1 - 0.02, -D + 0.1, Frame, "dirty steel");
        foreach (var x in new[] { -0.3, -0.1, 0.1, 0.3 })
            Rod(k, x - 0.05, ScrY0 + 0.6, -D + 0.1, x + 0.05, ScrY0 + 0.7, -D + 0.14, "+z", Steel, "steel");
        Rod(k, -0.1, ScrY1 + 0.01, -D + 0.03, 0.1, Y1 + 0.2, -D + 0.23, "+y", Steel, "steel", 16);
        return k.P.Stats(90, 700, 8, 3.0);
    }

    private static Part ScrubberTray()
    {
        var k = Mover("Scrubber Tray" + Sfx, $"A CO2 scrubber's canister tray ({Py.G(D)} m deep, draft): twist the latch and it runs out with its absorbent canisters; a spent one shows a red band, a fresh one green. Swap them from the stores.", "tray", [0, ScrY0, 0]);
        k.Paint(-0.44, ScrY0 + 0.008, -0.012, 0.44, ScrY1 - 0.008, 0.018, Wall, "wall panelling");
        k.Box(0.3, 0.85, 0.015, 0.38, 1.25, 0.04, Frame, "rubber"); // (a pull grip by the latch)
        for (var i = 0; i < 5; i++)
            k.Box(-0.36, ScrY0 + 0.1 + i * 0.05, 0.015, 0.2, ScrY0 + 0.12 + i * 0.05, 0.024, Frame, "dirty steel"); // (vent slots)
        k.Box(-0.36, ScrY1 - 0.14, 0.015, -0.04, ScrY1 - 0.08, 0.026, Frame, "plastic");
        k.Glow(-0.34, ScrY1 - 0.13, 0.024, -0.06, ScrY1 - 0.09, 0.03, Green);
        double t = ScrY0 + 0.03, zb = -D + 0.12;
        k.Box(-0.42, t, zb, 0.42, t + 0.02, -0.012, Steel, "tread plate");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.42, t, zb, sx * 0.435, t + 0.03, -0.02, Steel, "steel");
        var n = 0;
        for (var zr = -0.28; zr > -D + 0.15; zr -= 0.4)
            foreach (var x in new[] { -0.3, -0.1, 0.1, 0.3 })
            {
                var spent = n++ == 5;
                Rod(k, x - 0.08, t + 0.02, zr - 0.08, x + 0.08, t + 0.6, zr + 0.08, "+y", spent ? "#d6c99c" : MedWhite, "gloss paint", 16);
                Rod(k, x - 0.084, t + 0.4, zr - 0.084, x + 0.084, t + 0.46, zr + 0.084, "+y", spent ? Red : Green, "gloss paint", 16);
                Rod(k, x - 0.06, t + 0.6, zr - 0.06, x + 0.06, t + 0.65, zr + 0.06, "+y", Frame, "plastic", 16);
            }
        Solid(k, -0.44, ScrY0, -0.012, 0.44, ScrY1, 0.018);
        Solid(k, -0.42, t, zb, 0.42, t + 0.02, -0.012);
        Slide(k.P, D - 0.08, 2.0);
        return k.P.Stats(70, 400, 4, 2.0);
    }

    // 1 m: a water reclaimer. A drawer of filter cartridges (pre-filter, carbon, membrane, with spares behind), the pump under it,
    // the tank over it in the void with its level in a lit column on the panel.
    private const double FilY0 = 0.46, FilY1 = 1.22;

    private static Part WaterInsert()
    {
        var k = Insert("Water Reclaimer Insert" + Sfx, $"An in-wall water reclaimer ({Py.G(D)} m deep, draft): the pump under the filter drawer, the tank over it with its level on the panel, the manifold behind.");
        Tab(k, "filters", FilY0);
        Tab(k, "latch", 0.84 - 0.01);
        // Under: the pump panel; the pump behind it.
        k.Paint(-W, Y0, -0.04, W, FilY0 - 0.01, -0.012, Wall, "wall panelling");
        k.Paint(-W, Y0, -0.04, W, 0.3, -0.008, WallLow, "painted steel");
        for (var i = 0; i < 3; i++)
            k.Box(-0.3, 0.32 + i * 0.03, -0.014, 0.3, 0.335 + i * 0.03, -0.006, Frame, "dirty steel");
        Rod(k, -0.3, 0.16, -0.4, 0.2, 0.34, -0.2, "+x", PipeBlue, "painted steel", 16);
        Solid(k, -W, Y0, -0.04, W, FilY0 - 0.01, -0.012);
        // Over: the level column, two dials, a screen.
        k.Paint(-W, FilY1 + 0.01, -0.04, W, Y1, -0.012, Wall, "wall panelling");
        k.Box(-0.38, 1.3, -0.014, -0.26, 1.92, 0.0, Frame, "plastic");
        k.Glow(-0.36, 1.32, -0.002, -0.28, 1.7, 0.004, "#4aa8e8");
        foreach (var x in new[] { -0.1, 0.1 })
        {
            Rod(k, x - 0.075, 1.72, -0.014, x + 0.075, 1.87, 0.01, "+z", Trim, "plastic", 16);
            Rod(k, x - 0.06, 1.735, 0.0, x + 0.06, 1.855, 0.014, "+z", MedWhite, "plastic", 16);
        }
        k.Box(-0.16, 1.34, -0.014, 0.36, 1.62, 0.0, Frame, "plastic");
        k.Glow(-0.14, 1.36, -0.002, 0.34, 1.6, 0.004, Glow);
        Solid(k, -W, FilY1 + 0.01, -0.04, W, Y1, -0.012);
        // The bay: rails, the manifold the cartridges seat into; the tank above in the void, its pipes down.
        k.Box(-W, FilY0 - 0.01, -D, W, FilY0, -0.04, Recess, "dirty steel");
        k.Box(-W, FilY1, -D, W, FilY1 + 0.01, -0.04, Recess, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.42, FilY0, -D, sx * W, FilY0 + 0.04, -0.04, Steel, "steel");
        k.Box(-0.42, FilY0 + 0.02, -D + 0.015, 0.42, FilY1 - 0.02, -D + 0.09, Frame, "dirty steel");
        Rod(k, -0.42, 1.3, -D + 0.04, 0.42, 1.9, -D + 0.6, "+x", "#3f6f8f", "painted steel", 20);
        foreach (var x in new[] { -0.25, 0.25 })
            Rod(k, x - 0.03, FilY1 - 0.02, -D + 0.03, x + 0.03, 1.32, -D + 0.09, "+y", PipeBlue, "painted steel");
        return k.P.Stats(120, 700, 8, 3.0);
    }

    private static Part FilterDrawer()
    {
        var k = Mover("Filter Drawer" + Sfx, $"A water reclaimer's filter drawer ({Py.G(D)} m deep, draft): twist the latch and it runs out with its cartridges standing in a row (pre-filter, carbon, membrane) and a spare set behind.", "filters", [0, FilY0, 0]);
        k.Paint(-0.44, FilY0 + 0.008, -0.012, 0.44, FilY1 - 0.008, 0.018, Wall, "wall panelling");
        k.Box(0.3, 0.66, 0.015, 0.38, 1.0, 0.04, Frame, "rubber");
        k.Box(-0.36, FilY1 - 0.14, 0.015, -0.04, FilY1 - 0.08, 0.026, Frame, "plastic");
        k.Glow(-0.34, FilY1 - 0.13, 0.024, -0.06, FilY1 - 0.09, 0.03, "#4aa8e8");
        double t = FilY0 + 0.03, zb = -D + 0.11;
        k.Box(-0.42, t, zb, 0.42, t + 0.02, -0.012, Steel, "tread plate");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.42, t, zb, sx * 0.435, t + 0.03, -0.02, Steel, "steel");
        string[] stage = [Amber, "#3a3d40", MedWhite];
        for (var zr = -0.3; zr > -D + 0.15; zr -= 0.4)
            for (var c = 0; c < 3; c++)
            {
                double x = -0.25 + c * 0.25;
                Rod(k, x - 0.07, t + 0.02, zr - 0.07, x + 0.07, t + 0.58, zr + 0.07, "+y", stage[c], "plastic", 16);
                Rod(k, x - 0.074, t + 0.58, zr - 0.074, x + 0.074, t + 0.64, zr + 0.074, "+y", PipeBlue, "plastic", 16);
                Rod(k, x - 0.074, t + 0.02, zr - 0.074, x + 0.074, t + 0.06, zr + 0.074, "+y", PipeBlue, "plastic", 16);
            }
        Solid(k, -0.44, FilY0, -0.012, 0.44, FilY1, 0.018);
        Solid(k, -0.42, t, zb, 0.42, t + 0.02, -0.012);
        Slide(k.P, D - 0.08, 2.0);
        return k.P.Stats(50, 400, 4, 2.0);
    }

    // 1.5 m: an oxygen generator. A furnace that burns oxygen candles: its breech runs out of the chamber with the candle in it, a
    // drawer of spare candles under it, the chamber, its fins and the oxygen line behind.
    private const double BrY0 = 0.62, BrY1 = 1.2;

    private static Part OxygenInsert()
    {
        var k = Insert("O2 Generator Insert" + Sfx, $"An in-wall oxygen generator ({Py.G(D)} m deep, draft): the candle furnace's chamber at the back with its fins and oxygen line, the breech's rails, two pressure dials and a heat warning over it.");
        Tab(k, "breech", BrY0);
        Tab(k, "spares", Y0);
        Tab(k, "latch", 0.9);
        // The chamber at the back: a finned cylinder along the bay, its mouth ring, the oxygen line up into the void with a filter on it.
        double c0 = -D + 0.05, c1 = -D + 0.75, cy0 = BrY0 + 0.04, cy1 = BrY0 + 0.5;
        Rod(k, -0.23, cy0, c0, 0.23, cy1, c1, "+z", "#4b4a46", "dirty steel", 24);
        for (var z = c0 + 0.08; z < c1 - 0.05; z += 0.11)
            Rod(k, -0.27, cy0 - 0.04, z, 0.27, cy1 + 0.04, z + 0.03, "+z", Frame, "dirty steel", 24);
        Rod(k, -0.26, cy0 - 0.03, c1, 0.26, cy1 + 0.03, c1 + 0.05, "+z", Steel, "steel", 24);
        Rod(k, -0.04, cy1, -D + 0.3, 0.04, Y1 + 0.2, -D + 0.38, "+y", Green, "painted steel");
        Rod(k, -0.09, 1.45, -D + 0.25, 0.09, 1.7, -D + 0.43, "+y", MedWhite, "painted steel", 16);
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.42, BrY0, -D, sx * W, BrY0 + 0.04, -0.04, Steel, "steel");
        k.Box(-W, BrY0 - 0.01, -D, W, BrY0, -0.04, Recess, "dirty steel");
        k.Box(-W, BrY1, -D, W, BrY1 + 0.01, -0.04, Recess, "dirty steel");
        // Over: two pressure dials, a status lamp, a heat warning band, a small screen.
        k.Paint(-W, BrY1 + 0.01, -0.04, W, Y1, -0.012, Wall, "wall panelling");
        foreach (var x in new[] { -0.22, 0.02 })
        {
            Rod(k, x - 0.1, 1.5, -0.014, x + 0.1, 1.7, 0.01, "+z", Trim, "plastic", 20);
            Rod(k, x - 0.082, 1.518, 0.0, x + 0.082, 1.682, 0.014, "+z", MedWhite, "plastic", 20);
        }
        for (var i = 0; i < 6; i++)
            k.Box(-0.42 + i * 0.145, BrY1 + 0.04, -0.014, -0.35 + i * 0.145, BrY1 + 0.09, -0.004, i % 2 == 0 ? Hazard : Frame, "hazard paint");
        k.Box(0.18, 1.5, -0.014, 0.38, 1.7, 0.0, Frame, "plastic");
        k.Glow(0.2, 1.52, -0.002, 0.36, 1.68, 0.004, Glow);
        k.Glow(0.3, 1.8, -0.012, 0.36, 1.86, -0.004, Green);
        Solid(k, -W, BrY1 + 0.01, -0.04, W, Y1, -0.012);
        return k.P.Stats(160, 900, 10, 4.0);
    }

    private static Part CandleBreech()
    {
        var k = Mover("Candle Breech" + Sfx, $"An oxygen generator's breech ({Py.G(D)} m deep, draft): twist the latch and the breech runs out of the furnace with its candle in the cradle; a spent candle comes out, a fresh one goes in.", "breech", [0, BrY0, 0], Steel);
        k.Paint(-0.44, BrY0 + 0.008, -0.012, 0.44, BrY1 - 0.008, 0.018, Trim, "painted steel");
        Rod(k, -0.24, BrY0 + 0.05, 0.004, 0.24, BrY0 + 0.53, 0.05, "+z", Steel, "plated steel", 24); // (the breech door)
        Rod(k, -0.19, BrY0 + 0.1, 0.03, 0.19, BrY0 + 0.48, 0.062, "+z", Hazard, "hazard paint", 24);
        Rod(k, -0.05, BrY0 + 0.24, 0.05, 0.05, BrY0 + 0.34, 0.07, "+z", Amber, "plastic", 12); // (the sight window, hot)
        k.Box(0.3, 0.72, 0.015, 0.38, 1.1, 0.04, Frame, "rubber");
        double t = BrY0 + 0.03, zb = -D + 0.1;
        k.Box(-0.2, t, zb, 0.2, t + 0.02, -0.012, Steel, "tread plate");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.2, t, zb, sx * 0.42, t + 0.02, zb + 0.6, Steel, "steel"); // (the carriage's wings on the rails)
            k.Box(sx * 0.42, t, zb, sx * 0.435, t + 0.03, zb + 0.6, Steel, "steel");
        }
        // The candle in its cradle, deep in the chamber at rest.
        double cz0 = -D + 0.2, cz1 = -D + 0.6;
        foreach (var z in new[] { cz0 + 0.05, cz1 - 0.08 })
            k.Box(-0.1, t + 0.02, z, 0.1, t + 0.07, z + 0.03, Frame, "dirty steel");
        Rod(k, -0.07, t + 0.05, cz0, 0.07, t + 0.19, cz1, "+z", Red, "gloss paint", 16);
        Rod(k, -0.072, t + 0.048, cz0 + 0.12, 0.072, t + 0.192, cz0 + 0.17, "+z", MedWhite, "gloss paint", 16);
        Rod(k, -0.05, t + 0.07, cz1, 0.05, t + 0.17, cz1 + 0.03, "+z", Frame, "plastic", 16);
        Solid(k, -0.44, BrY0, -0.012, 0.44, BrY1, 0.018);
        Solid(k, -0.2, t, zb, 0.2, t + 0.02, -0.012);
        Slide(k.P, D - 0.35, 2.2);
        return k.P.Stats(90, 600, 6, 2.0);
    }

    private static Part CandleDrawer()
    {
        var k = Mover("Candle Drawer" + Sfx, $"The spare candle drawer under an oxygen generator ({Py.G(D)} m deep, draft): press it and it runs out with fresh oxygen candles in two layers.", "spares", [0, Y0, 0]);
        double y0 = Y0 + 0.008, y1 = BrY0 - 0.018;
        k.Paint(-0.44, y0, -0.012, 0.44, y1, 0.018, WallLow, "painted steel");
        k.Box(-0.2, y1 - 0.07, 0.015, 0.2, y1 - 0.035, 0.03, Hazard, "hazard paint");
        k.Box(0.26, y1 - 0.075, 0.015, 0.4, y1 - 0.03, 0.026, Frame, "plastic");
        k.Glow(0.28, y1 - 0.066, 0.024, 0.36, y1 - 0.04, 0.03, Red);
        double b = y0 + 0.02, zb = -D + 0.06;
        k.Box(-0.42, b, zb, 0.42, b + 0.02, -0.012, Recess, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.40, b + 0.02, zb, sx * 0.42, y1 - 0.06, -0.012, Recess, "dirty steel");
            k.Box(sx * 0.42, b + 0.02, zb, sx * 0.435, b + 0.05, -0.02, Steel, "steel");
        }
        k.Box(-0.40, b + 0.02, zb, 0.40, y1 - 0.06, zb + 0.02, Recess, "dirty steel");
        for (var zr = -0.3; zr > -D + 0.3; zr -= 0.45)
            for (var layer = 0; layer < 2; layer++)
                for (var c = 0; c < 6; c++)
                {
                    double x = -0.31 + c * 0.124, y = b + 0.02 + layer * 0.1;
                    Rod(k, x - 0.05, y, zr - 0.18, x + 0.05, y + 0.1, zr + 0.18, "+z", Red, "gloss paint", 12);
                    k.Box(x - 0.052, y - 0.002, zr - 0.06, x + 0.052, y + 0.102, zr - 0.03, MedWhite, "plastic");
                }
        Solid(k, -0.44, y0, -0.012, 0.44, y1, 0.018);
        Solid(k, -0.42, b, zb, 0.42, b + 0.02, -0.012);
        Slide(k.P, D - 0.1, 2.0);
        return k.P.Stats(60, 300, 4, 1.5);
    }

    // ---- assembled, one blueprint a fill (for the gallery)

    public void Make(PartOutput o)
    {
        o.Own(Out);
        var parts = new List<Part>();
        var fills = new List<(string File, string Name, string Desc, string Bay, string Insert, (string Part, string Sock)[] Movers)>();
        void Fill(string file, string name, string desc, string insert, params (string, string)[] movers) => fills.Add((file, name, desc, "Wall Bay" + Sfx, insert, movers));

        // 0.5 m: the standard bay. A seat, drawers, a rack, a component sled, a Murphy bed and a desk; the twist latch, any depth.
        Depth(0.5);
        parts.AddRange([Bay(), SeatInsert(), SeatFlap(), HelmetDoor(), DrawerInsert(), RackInsert(), Rack(), SledInsert(), SledCover(), Sled(), BunkInsert(), Bunk(), DeskInsert(), Desk(), TwistLatch()]);
        parts.AddRange(Enumerable.Range(0, Drawers).Select(Drawer));
        Fill("WallBayJumpSeat", "Wall Bay Jump Seat", "An in-wall bay (draft) with a jump seat that flips down and a helmet locker over it.", "Jump Seat Insert", ("Jump Seat", "seat"), ("Helmet Locker Door", "helmet"));
        Fill("WallBayDrawers", "Wall Bay Drawers", "An in-wall bay (draft) with four deep drawers.", "Drawer Stack Insert", [.. Enumerable.Range(0, Drawers).Select(i => ($"Stow Drawer {i + 1}", $"drawer{i}"))]);
        Fill("WallBayRack", "Wall Bay Rack", "An in-wall bay (draft) with a rack that pulls out of the wall.", "Pull-out Rack Insert", ("Pull-out Rack", "rack"));
        Fill("WallBayComponent", "Wall Bay Component Sled", "An in-wall bay (draft) whose cover flips up and whose sled runs out with a power cell on it.", "Component Bay Insert", ("Component Bay Cover", "cover"), ("Component Sled", "sled"));
        Fill("WallBayMurphyBed", "Wall Bay Murphy Bed", "An in-wall bay (draft) whose whole face flips down to a Murphy bed.", "Murphy Bed Insert", ("Murphy Bed", "bunk"));
        Fill("WallBayDesk", "Wall Bay Desk", "An in-wall bay (draft) with a desk that flips down from the wall.", "Desk Insert", ("Fold-down Desk", "desk"));

        // 1 m: a suit rack, deep shelves, a long component sled, a toilet, a consumables rack; two ship systems, a CO2 scrubber and a water reclaimer.
        Depth(1.0);
        parts.AddRange([Bay(), SuitInsert(), SuitRack(), RackInsert(), Rack(), SledInsert(), SledCover(), Sled(), ToiletInsert(), ToiletPan(), RackInsert(true), Rack(true),
            ScrubberInsert(), ScrubberTray(), WaterInsert(), FilterDrawer()]);
        Fill("WallBay1mSuit", "Wall Bay 1m Suit Rack", "A 1 m in-wall bay (draft) whose suit rack runs out with a pressure suit on it.", "Suit Rack Insert" + Sfx, ("Suit Rack" + Sfx, "suit"));
        Fill("WallBay1mRack", "Wall Bay 1m Rack", "A 1 m in-wall bay (draft) with deep shelves, two loads deep, that pull out of the wall.", "Pull-out Rack Insert" + Sfx, ("Pull-out Rack" + Sfx, "rack"));
        Fill("WallBay1mComponent", "Wall Bay 1m Component Sled", "A 1 m in-wall bay (draft) whose sled runs out with a long power cell on it.", "Component Bay Insert" + Sfx, ("Component Bay Cover" + Sfx, "cover"), ("Component Sled" + Sfx, "sled"));
        Fill("WallBay1mToilet", "Wall Bay 1m Toilet", "A 1 m in-wall bay (draft): twist the side handle and a toilet pan runs out of the wall.", "Toilet Insert" + Sfx, ("Twist Latch", "latch"), ("Toilet Pan" + Sfx, "pan"));
        Fill("WallBay1mStores", "Wall Bay 1m Consumables", "A 1 m in-wall bay (draft): twist the side handle and a rack of the ship's consumables runs out.", "Consumables Rack Insert" + Sfx, ("Twist Latch", "latch"), ("Consumables Rack" + Sfx, "rack"));
        Fill("WallBay1mScrubber", "Wall Bay 1m CO2 Scrubber", "A CO2 scrubber in a 1 m in-wall bay (draft): twist the side handle and its tray of canisters runs out to be changed.", "CO2 Scrubber Insert" + Sfx, ("Twist Latch", "latch"), ("Scrubber Tray" + Sfx, "tray"));
        Fill("WallBay1mWater", "Wall Bay 1m Water Reclaimer", "A water reclaimer in a 1 m in-wall bay (draft): twist the side handle and its filter drawer runs out to be changed.", "Water Reclaimer Insert" + Sfx, ("Twist Latch", "latch"), ("Filter Drawer" + Sfx, "filters"));

        // 1.5 m: a spacewalk pack bay, a stretcher drawer, deeper shelves, a heavy module sled; an oxygen generator that burns candles.
        Depth(1.5);
        parts.AddRange([Bay(), MmuInsert(), MmuDoor(), MmuSled(), StretcherInsert(), Stretcher(), RackInsert(), Rack(), SledInsert(), SledCover(), Sled(), OxygenInsert(), CandleBreech(), CandleDrawer()]);
        Fill("WallBay15mMmu", "Wall Bay 1.5m MMU", "A 1.5 m in-wall bay (draft): its door swings aside and a sled brings a spacewalk pack out into the room.", "MMU Bay Insert" + Sfx, ("MMU Bay Door" + Sfx, "door"), ("MMU Sled" + Sfx, "mmu"));
        Fill("WallBay15mStretcher", "Wall Bay 1.5m Stretcher", "A 1.5 m in-wall bay (draft) with a stretcher drawer under a med cabinet.", "Stretcher Insert" + Sfx, ("Stretcher Drawer" + Sfx, "stretcher"));
        Fill("WallBay15mRack", "Wall Bay 1.5m Rack", "A 1.5 m in-wall bay (draft) with deeper shelves, three loads deep, that pull out of the wall.", "Pull-out Rack Insert" + Sfx, ("Pull-out Rack" + Sfx, "rack"));
        Fill("WallBay15mModule", "Wall Bay 1.5m Module Sled", "A 1.5 m in-wall bay (draft) whose sled runs out with a coolant exchanger module on it.", "Component Bay Insert" + Sfx, ("Component Bay Cover" + Sfx, "cover"), ("Component Sled" + Sfx, "sled"));
        Fill("WallBay15mOxygen", "Wall Bay 1.5m O2 Generator", "An oxygen generator in a 1.5 m in-wall bay (draft): twist the side handle and the breech runs out with its candle; spare candles in the drawer under it.", "O2 Generator Insert" + Sfx, ("Twist Latch", "latch"), ("Candle Breech" + Sfx, "breech"), ("Candle Drawer" + Sfx, "spares"));

        var defs = new Dictionary<string, PyDict>();
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        foreach (var f in fills)
        {
            var bp = new BuildingParts.Blueprint(f.Name, f.Desc, defs);
            var bay = bp.First(f.Bay, 2); // (turned half round: the galleries look from -z, and the room is the bay's +z)
            var ins = bp.Hang(f.Insert, bay, "insert", "bay");
            foreach (var (m, sock) in f.Movers)
                bp.Hang(m, ins, sock, "hang");
            o.File($"{BpOut}/{f.File}.json", PyJson.Dumps(bp.Json(), 2) + "\n");
        }
    }
}
