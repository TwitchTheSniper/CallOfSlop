using CallOfSlop.PartKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Ship systems in the in-wall bays, first drafted for the Animation gallery (Karl, 2026-11-20: "look at my conversation around subsystems
/// and their related components and see if you can incorporate these into the in-wall system"; the brainstorm in the project chat
/// listed each system's parts, the sub-parts that wear and what it uses up). Every system fill follows the brainstorm's rule: the
/// module has two service slots, a part that wears in the upper <b>cassette</b> and a consumable in the lower <b>drawer</b>, with a
/// wear bar and a supply bar over them (wear lowers output rather than stopping the module, and the bays hold the spares). Twist the
/// latch and the drawer runs out; the cassette runs out with a press.
///
/// One fill for each system the bays didn't have: 0.5 m, breakers and fuses (power), a flight computer, a damage control locker (hull
/// and doors); 1 m, a coolant pump (heat), RCS valves and propellant (propulsion), a radar emitter and nav cores (sensors), a PDC feeder
/// and ammo (weapons); 1.5 m, reactor fuel and control rods (power). Life support and water were there already (the CO2 scrubber, the
/// oxygen generator, the water reclaimer); the scrubber's fan and the reclaimer's pump became their parts that wear.
/// </summary>
public sealed partial class WallBayParts
{
    // The supply drawer's head; the wear cassette's sill and head; the status strip from over the cassette to the opening's head.
    private const double SupY1 = 0.91, WearY0 = 0.92, WearY1 = 1.68;
    private const string CopperC = "#b87333", BrassC = "#b59a4a", Pcb = "#2f6b3a", Teal = "#4ad0c8", Orange = "#f07020", Lime = "#b8e04a",
        Dark = "#2f3338", Carton = "#c8b88a", BottleWhite = "#e8e6e0", JugTeal = "#3fb8b0", CanLid = "#4f5644", Foam = "#3a3d40";

    /// <summary>A ship system's insert: the module's back block and rails, the wear bar (lit to <paramref name="wear"/> of six) and supply
    /// bar (to <paramref name="supply"/>) over the cassette, and the system's colour.</summary>
    private static FittingLookParts.Kit SysInsert(string name, string desc, string tag, int wear, int supply)
    {
        var k = Insert(name + Sfx, desc);
        Tab(k, "supply", Y0);
        Tab(k, "wear", WearY0);
        Tab(k, "latch", LatchY);
        k.Paint(-W, WearY1 + 0.01, -0.04, W, Y1, -0.012, Wall, "wall panelling");
        Solid(k, -W, WearY1 + 0.01, -0.04, W, Y1, -0.012);
        k.Box(-0.4, 1.74, -0.014, 0.4, 1.94, 0.0, Frame, "plastic");
        for (var i = 0; i < 6; i++)
        {
            k.Glow(-0.36 + i * 0.06, 1.85, -0.002, -0.31 + i * 0.06, 1.9, 0.004, i < wear ? (i < 3 ? Green : Amber) : Frame);
            k.Glow(-0.36 + i * 0.06, 1.78, -0.002, -0.31 + i * 0.06, 1.83, 0.004, i < supply ? Glow : Frame);
        }
        k.Glow(0.06, 1.78, -0.002, 0.36, 1.9, 0.004, tag);
        k.Box(-W, SupY1, -D, W, WearY0, -0.04, Recess, "dirty steel");
        k.Box(-W, WearY1, -D, W, WearY1 + 0.01, -0.04, Recess, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.42, Y0, -D, sx * W, Y0 + 0.04, -0.04, Steel, "steel");
            k.Box(sx * 0.42, WearY0, -D, sx * W, WearY0 + 0.04, -0.04, Steel, "steel");
        }
        k.Box(-0.42, Y0 + 0.05, -D + 0.015, 0.42, WearY1 - 0.02, -D + 0.06, Frame, "dirty steel");
        return k;
    }

    /// <summary>A ship system's supply drawer on its tray (press it and it runs out); returns the tray's top and back for its load.</summary>
    private static (FittingLookParts.Kit K, double T, double Zb) SysDrawer(string name, string desc, string tag)
    {
        var k = Mover(name + Sfx, desc, "supply", [0, Y0, 0]);
        k.Paint(-0.44, Y0 + 0.008, -0.012, 0.44, SupY1 - 0.008, 0.018, Wall, "wall panelling");
        k.Box(0.3, 0.4, 0.015, 0.38, 0.72, 0.04, Frame, "rubber"); // (a pull grip by the latch)
        k.Box(-0.38, SupY1 - 0.14, 0.015, -0.04, SupY1 - 0.08, 0.026, Frame, "plastic");
        k.Glow(-0.36, SupY1 - 0.13, 0.024, -0.06, SupY1 - 0.09, 0.03, tag);
        double t = Y0 + 0.03, zb = -D + 0.11;
        k.Box(-0.42, t, zb, 0.42, t + 0.02, -0.012, Steel, "tread plate");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.42, t, zb, sx * 0.435, t + 0.03, -0.02, Steel, "steel");
        Solid(k, -0.44, Y0, -0.012, 0.44, SupY1, 0.018);
        Solid(k, -0.42, t, zb, 0.42, t + 0.02, -0.012);
        Slide(k.P, D - 0.08, 2.0);
        return (k, t + 0.02, zb);
    }

    /// <summary>A ship system's wear cassette on its tray: two handles, its label and wear lamps on the face; returns the tray's top and back.</summary>
    private static (FittingLookParts.Kit K, double T, double Zb) SysCassette(string name, string desc, string tag)
    {
        var k = Mover(name + Sfx, desc, "wear", [0, WearY0, 0]);
        k.Paint(-0.44, WearY0 + 0.008, -0.012, 0.44, WearY1 - 0.008, 0.018, Wall, "wall panelling");
        foreach (var sx in new[] { -0.32, 0.32 })
        {
            Rod(k, sx - 0.02, WearY0 + 0.2, 0.045, sx + 0.02, WearY0 + 0.5, 0.075, "+y", Frame, "rubber");
            foreach (var y in new[] { WearY0 + 0.22, WearY0 + 0.455 })
                k.Box(sx - 0.012, y, 0.009, sx + 0.012, y + 0.025, 0.05, Steel, "steel");
        }
        k.Box(-0.2, WearY1 - 0.14, 0.015, 0.2, WearY1 - 0.06, 0.026, Frame, "plastic");
        k.Glow(-0.18, WearY1 - 0.13, 0.024, -0.01, WearY1 - 0.07, 0.03, tag);
        for (var i = 0; i < 3; i++)
            k.Glow(0.02 + i * 0.06, WearY1 - 0.125, 0.024, 0.06 + i * 0.06, WearY1 - 0.075, 0.03, i < 2 ? Green : Amber);
        double t = WearY0 + 0.03, zb = -D + 0.11;
        k.Box(-0.42, t, zb, 0.42, t + 0.02, -0.012, Steel, "tread plate");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.42, t, zb, sx * 0.435, t + 0.03, -0.02, Steel, "steel");
        Solid(k, -0.44, WearY0, -0.012, 0.44, WearY1, 0.018);
        Solid(k, -0.42, t, zb, 0.42, t + 0.02, -0.012);
        Slide(k.P, D - 0.1, 2.4);
        return (k, t + 0.02, zb);
    }

    /// <summary>A shelf half way up a drawer on four posts, its top at y 0.5, for a second row of stock.</summary>
    private static void Tier(FittingLookParts.Kit k, double t, double zb)
    {
        k.Box(-0.4, 0.48, zb + 0.02, 0.4, 0.5, -0.03, Steel, "steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var z in new[] { zb + 0.035, -0.045 })
                k.Box(sx * 0.4 - 0.012, t, z - 0.012, sx * 0.4 + 0.012, 0.48, z + 0.012, Steel, "steel");
    }

    // ---- 0.5 m: power distribution. The cassette's face is the breaker panel (one tripped, red); fuses in the drawer.

    private static Part[] Breakers()
    {
        var i = SysInsert("Breaker Panel Insert", $"An in-wall breaker panel ({Py.G(D)} m deep): the power bus behind, its breaker cassette (the part that wears) over a fuse drawer (the consumable), wear and supply bars over them.", Amber, 4, 5);
        foreach (var x in new[] { -0.2, 0.0, 0.2 })
            i.Box(x - 0.02, Y0 + 0.08, -D + 0.06, x + 0.02, WearY1 - 0.05, -D + 0.075, CopperC, "copper");
        Rod(i, 0.28, 1.5, -D + 0.02, 0.34, Y1 + 0.25, -D + 0.08, "+y", Frame, "rubber");

        var (c, t, _) = SysCassette("Breaker Cassette", "A breaker panel's cassette: the part that wears. Its face is the panel, three rows of breakers, one tripped red; it runs out on its tray with the breakers' bodies and the bus stabs behind.", Amber);
        for (var r = 0; r < 3; r++)
            for (var j = 0; j < 7; j++)
            {
                double x = -0.24 + j * 0.08, y = 0.98 + r * 0.17;
                var trip = r == 1 && j == 4;
                c.Box(x - 0.028, y, 0.015, x + 0.028, y + 0.12, 0.034, Dark, "plastic");
                c.Box(x - 0.01, y + (trip ? 0.02 : 0.045), 0.03, x + 0.01, y + (trip ? 0.05 : 0.075), 0.048, trip ? Red : MedWhite, "plastic");
            }
        c.Box(-0.3, t, -0.3, 0.3, t + 0.5, -0.03, Dark, "plastic");
        c.Box(-0.3, t + 0.1, -0.33, 0.3, t + 0.16, -0.3, CopperC, "copper");
        foreach (var x in new[] { -0.2, 0.0, 0.2 })
            c.Box(x - 0.015, t + 0.11, -0.37, x + 0.015, t + 0.15, -0.33, CopperC, "copper");

        var (d, s, zb) = SysDrawer("Fuse Drawer", "A breaker panel's fuse drawer: the consumable. Press it and it runs out with cartridge fuses in a rack and cartons of spares on the shelf over them.", Amber);
        d.Box(-0.38, s, -0.36, 0.38, s + 0.05, -0.04, Dark, "plastic");
        foreach (var x in new[] { -0.27, -0.09, 0.09, 0.27 })
            foreach (var z in new[] { -0.08, -0.16, -0.24, -0.32 })
            {
                Rod(d, x - 0.05, s + 0.045, z - 0.015, x + 0.05, s + 0.075, z + 0.015, "+x", MedWhite, "plastic", 10);
                foreach (var e in new[] { -1.0, 1.0 })
                    Rod(d, x + e * 0.045, s + 0.043, z - 0.017, x + e * 0.065, s + 0.077, z + 0.017, "+x", Steel, "chrome", 10);
            }
        Tier(d, s, zb);
        foreach (var x in new[] { -0.25, 0.0, 0.25 })
        {
            d.Box(x - 0.1, 0.5, -0.33, x + 0.1, 0.66, -0.08, Carton, "matte paint");
            d.Box(x - 0.07, 0.56, -0.083, x + 0.07, 0.6, -0.072, Amber, "plastic");
        }
        return [i.P.Stats(40, 500, 6, 2.0), c.P.Stats(25, 300, 4, 1.5), d.P.Stats(20, 300, 4, 1.5)];
    }

    // ---- 0.5 m: the flight computer. A card cage with its fan (the part that wears) over a drawer of memory modules.

    private static Part[] Computer()
    {
        var i = SysInsert("Flight Computer Insert", $"An in-wall flight computer ({Py.G(D)} m deep): its card cage (the part that wears) over a drawer of memory modules, the data trunk behind.", Green, 5, 4);
        foreach (var x in new[] { -0.29, -0.21 })
            Rod(i, x - 0.03, 1.5, -D + 0.02, x + 0.03, Y1 + 0.25, -D + 0.08, "+y", PipeBlue, "rubber");

        var (c, t, _) = SysCassette("Computer Card Cage", "A flight computer's card cage: the part that wears. It runs out with nine cards standing in their guide and the cage's fan behind them.", Green);
        foreach (var sx in new[] { -1.0, 1.0 })
            c.Box(sx * 0.314, t, -0.36, sx * 0.326, t + 0.4, -0.04, Steel, "aluminium");
        foreach (var (z0, z1) in new[] { (-0.36, -0.34), (-0.06, -0.04) })
            c.Box(-0.326, t + 0.4, z0, 0.326, t + 0.42, z1, Steel, "aluminium");
        c.Box(-0.3, t, -0.34, 0.3, t + 0.025, -0.05, Dark, "plastic");
        for (var j = 0; j < 9; j++)
        {
            var x = -0.24 + j * 0.06;
            c.Box(x - 0.006, t + 0.02, -0.33, x + 0.006, t + 0.37, -0.06, Pcb, "plastic");
            c.Box(x - 0.008, t + 0.04, -0.06, x + 0.008, t + 0.35, -0.045, BrassC, "brass");
            c.Box(x + 0.006, t + 0.15, -0.25, x + 0.018, t + 0.26, -0.13, Dark, "plastic");
        }
        c.Box(-0.14, t + 0.06, -0.37, 0.14, t + 0.34, -0.355, Dark, "plastic");
        Rod(c, -0.12, t + 0.08, -0.355, 0.12, t + 0.32, -0.338, "+z", Frame, "plastic", 20);
        Rod(c, -0.03, t + 0.17, -0.343, 0.03, t + 0.23, -0.332, "+z", Steel, "steel");

        var (d, s, zb) = SysDrawer("Memory Module Drawer", "A flight computer's spares drawer: press it and it runs out with memory modules standing in foam and spare cards bagged on the shelf.", Green);
        d.Box(-0.38, s, -0.36, 0.38, s + 0.08, -0.04, Foam, "rubber");
        foreach (var x in new[] { -0.24, -0.08, 0.08, 0.24 })
            foreach (var z in new[] { -0.08, -0.13, -0.18, -0.23, -0.28, -0.33 })
                d.Box(x - 0.06, s + 0.04, z - 0.006, x + 0.06, s + 0.13, z + 0.006, Pcb, "plastic");
        Tier(d, s, zb);
        foreach (var (x0, x1) in new[] { (-0.35, -0.03), (0.03, 0.35) })
            d.Box(x0, 0.5, -0.34, x1, 0.54, -0.06, "#8a9096", "plastic");
        return [i.P.Stats(30, 400, 5, 2.0), c.P.Stats(15, 250, 3, 1.5), d.P.Stats(10, 250, 3, 1.5)];
    }

    // ---- 0.5 m: hull and doors. A damage control locker: spare door actuators (the part that wears) over patch plates and foam.

    private static Part[] DamageControl()
    {
        var i = SysInsert("Damage Control Insert", $"An in-wall damage control locker ({Py.G(D)} m deep): spare door actuators over a drawer of hull patch plates and sealant foam.", Hazard, 6, 4);

        var (c, t, _) = SysCassette("Actuator Spares Rack", "A damage control locker's spares rack: two spare door actuators in their cradles, for the door that wears out.", Hazard);
        foreach (var z0 in new[] { -0.12, -0.28 })
        {
            foreach (var x in new[] { -0.25, 0.0 })
                c.Box(x - 0.03, t, z0 - 0.06, x + 0.03, t + 0.05, z0 + 0.06, Frame, "rubber");
            Rod(c, -0.34, t + 0.02, z0 - 0.045, 0.1, t + 0.11, z0 + 0.045, "+x", Steel, "painted steel", 16);
            Rod(c, 0.097, t + 0.05, z0 - 0.015, 0.3, t + 0.08, z0 + 0.015, "+x", Steel, "chrome");
            c.Box(-0.37, t + 0.045, z0 - 0.02, -0.337, t + 0.085, z0 + 0.02, Dark, "dirty steel");
            c.Box(0.297, t + 0.045, z0 - 0.02, 0.33, t + 0.085, z0 + 0.02, Dark, "dirty steel");
        }

        var (d, s, _) = SysDrawer("Patch and Foam Drawer", "A damage control locker's drawer: the consumables. Press it and it runs out with hull patch plates standing in a rack and cans of sealant foam.", Hazard);
        d.Box(-0.4, s, -0.3, -0.02, s + 0.04, -0.04, Dark, "rubber");
        foreach (var z in new[] { -0.07, -0.11, -0.15, -0.19, -0.23 })
        {
            d.Box(-0.38, s + 0.02, z - 0.006, -0.04, s + 0.52, z + 0.006, Steel, "plated steel");
            d.Box(-0.383, s + 0.5, z - 0.008, -0.037, s + 0.54, z + 0.008, Hazard, "hazard paint");
        }
        foreach (var x in new[] { 0.1, 0.22, 0.34 })
            foreach (var z in new[] { -0.1, -0.24 })
            {
                Rod(d, x - 0.045, s, z - 0.045, x + 0.045, s + 0.28, z + 0.045, "+y", Hazard, "gloss paint", 16);
                Rod(d, x - 0.032, s + 0.277, z - 0.032, x + 0.032, s + 0.33, z + 0.032, "+y", Red, "plastic");
                d.Box(x - 0.008, s + 0.33, z - 0.03, x + 0.008, s + 0.35, z + 0.008, Frame, "plastic");
            }
        return [i.P.Stats(30, 400, 5, 2.0), c.P.Stats(30, 300, 4, 1.5), d.P.Stats(40, 300, 4, 1.5)];
    }

    // ---- 1 m: heat. The coolant pump on its cassette (the part that wears) over a drawer of coolant jugs.

    private static Part[] Coolant()
    {
        var i = SysInsert("Coolant Pump Insert", $"An in-wall coolant pump ({Py.G(D)} m deep): the loop's pipes up into the void, the couplers the pump's hoses plug into, the pump's cassette (the part that wears) over a drawer of coolant (the consumable).", Teal, 4, 3);
        foreach (var x in new[] { -0.3, 0.3 })
            Rod(i, x - 0.03, 1.5, -D + 0.025, x + 0.03, Y1 + 0.25, -D + 0.085, "+y", x < 0 ? Teal : PipeBlue, "painted steel");
        foreach (var x in new[] { -0.22, 0.22 })
            Rod(i, x - 0.035, 1.07, -D + 0.06, x + 0.035, 1.14, -D + 0.1, "+z", BrassC, "brass");

        var (c, t, _) = SysCassette("Coolant Pump Cassette", "A coolant pump's cassette: the part that wears. It runs out with the pump and its motor on a base plate, the hoses back to the loop's couplers.", Teal);
        c.Box(-0.2, t, -0.7, 0.2, t + 0.03, -0.12, Dark, "dirty steel");
        Rod(c, -0.12, t + 0.03, -0.62, 0.12, t + 0.27, -0.32, "+z", Motor, "painted steel", 20);
        Rod(c, -0.1, t + 0.05, -0.66, 0.1, t + 0.25, -0.617, "+z", Frame, "dirty steel", 20);
        Rod(c, -0.15, t + 0.002, -0.323, 0.15, t + 0.298, -0.2, "+z", Steel, "painted steel", 20);
        Rod(c, -0.05, t + 0.1, -0.203, 0.05, t + 0.2, -0.08, "+z", Teal, "painted steel");
        Rod(c, 0.06, t + 0.295, -0.3, 0.14, t + 0.42, -0.22, "+y", Teal, "painted steel");
        foreach (var x in new[] { -0.22, 0.22 })
        {
            Rod(c, x - 0.03, t + 0.1, -0.862, x + 0.03, t + 0.16, -0.4, "+z", Frame, "rubber");
            Rod(c, x - 0.035, t + 0.095, -0.889, x + 0.035, t + 0.165, -0.858, "+z", BrassC, "brass");
        }

        var (d, s, _) = SysDrawer("Coolant Jug Drawer", "A coolant pump's drawer: the consumable. Press it and it runs out with jugs of coolant to top the loop up.", Teal);
        foreach (var x in new[] { -0.27, -0.09, 0.09, 0.27 })
            foreach (var z in new[] { -0.16, -0.42, -0.68 })
            {
                d.Box(x - 0.08, s, z - 0.1, x + 0.08, s + 0.34, z + 0.1, JugTeal, "plastic");
                Rod(d, x + 0.005, s + 0.337, z + 0.015, x + 0.055, s + 0.37, z + 0.065, "+y", Frame, "plastic");
                d.Box(x - 0.06, s + 0.337, z - 0.07, x - 0.02, s + 0.38, z - 0.02, JugTeal, "plastic");
            }
        return [i.P.Stats(60, 500, 6, 2.5), c.P.Stats(45, 300, 4, 2.0), d.P.Stats(50, 300, 4, 1.5)];
    }

    // ---- 1 m: propulsion. The RCS valve block with its seals (the part that wears) over a drawer of propellant bottles.

    private static Part[] Rcs()
    {
        var i = SysInsert("RCS Feed Insert", $"An in-wall RCS feed ({Py.G(D)} m deep): the feed lines up to the thrusters, the couplers the valve block plugs into, the block's cassette (the part that wears) over a drawer of propellant bottles (the consumable).", Orange, 3, 4);
        foreach (var x in new[] { -0.3, 0.3 })
            Rod(i, x - 0.03, 1.5, -D + 0.025, x + 0.03, Y1 + 0.25, -D + 0.085, "+y", Orange, "painted steel");
        foreach (var x in new[] { -0.2, 0.2 })
            Rod(i, x - 0.03, 1.015, -D + 0.06, x + 0.03, 1.075, -D + 0.1, "+z", BrassC, "brass");

        var (c, t, _) = SysCassette("RCS Valve Block", "An RCS feed's valve block: the part that wears. It runs out with four valves on the manifold, the seals on its face and the ports back to the feed lines.", Orange);
        c.Box(-0.3, t, -0.62, 0.3, t + 0.16, -0.2, Steel, "aluminium");
        foreach (var x in new[] { -0.21, -0.07, 0.07, 0.21 })
        {
            Rod(c, x - 0.03, t + 0.157, -0.43, x + 0.03, t + 0.26, -0.37, "+y", BrassC, "brass");
            c.Box(x - 0.055, t + 0.26, -0.412, x + 0.055, t + 0.282, -0.388, Red, "plastic");
        }
        foreach (var x in new[] { -0.15, 0.15 })
            Rod(c, x - 0.06, t + 0.02, -0.203, x + 0.06, t + 0.14, -0.185, "+z", Frame, "rubber", 16);
        foreach (var x in new[] { -0.2, 0.2 })
        {
            Rod(c, x - 0.025, t + 0.05, -0.86, x + 0.025, t + 0.1, -0.617, "+z", Orange, "painted steel");
            Rod(c, x - 0.03, t + 0.045, -0.889, x + 0.03, t + 0.105, -0.857, "+z", BrassC, "brass");
        }

        var (d, s, zb) = SysDrawer("Propellant Bottle Drawer", "An RCS feed's drawer: the consumable. Press it and it runs out with propellant bottles in cradles, two rows deep.", Orange);
        void Bottles(double y)
        {
            foreach (var z in new[] { -0.66, -0.24 })
                d.Box(-0.38, y, z - 0.03, 0.38, y + 0.06, z + 0.03, Frame, "rubber");
            foreach (var x in new[] { -0.25, 0.0, 0.25 })
            {
                Rod(d, x - 0.1, y + 0.02, -0.8, x + 0.1, y + 0.22, -0.1, "+z", BottleWhite, "gloss paint", 20);
                foreach (var z in new[] { -0.52, -0.4 })
                    Rod(d, x - 0.104, y + 0.016, z, x + 0.104, y + 0.224, z + 0.06, "+z", Orange, "gloss paint", 20);
                Rod(d, x - 0.03, y + 0.09, -0.103, x + 0.03, y + 0.15, -0.04, "+z", BrassC, "brass");
            }
        }
        Bottles(s);
        Tier(d, s, zb);
        Bottles(0.5);
        return [i.P.Stats(50, 500, 6, 2.5), c.P.Stats(40, 300, 4, 2.0), d.P.Stats(70, 300, 4, 1.5)];
    }

    // ---- 1 m: sensors and comms. The radar's emitter tube (the part that wears) over a drawer of nav chart data cores.

    private static Part[] Sensors()
    {
        var i = SysInsert("Sensor Rack Insert", $"An in-wall sensor rack ({Py.G(D)} m deep): the waveguide up to the antenna, the flange the emitter tube seats into, its cassette (the part that wears) over a drawer of nav chart data cores.", Glow, 3, 5);
        i.Box(0.14, 1.5, -D + 0.02, 0.26, Y1 + 0.25, -D + 0.08, CopperC, "copper");
        i.Box(-0.11, 1.02, -D + 0.06, 0.11, 1.2, -D + 0.1, BrassC, "brass");

        var (c, t, _) = SysCassette("Emitter Tube Cassette", "A sensor rack's emitter cassette: the part that wears. It runs out with the radar's emitter tube in its cradles: a finned copper body, a glass end that glows while it works, the flange at the back.", Glow);
        foreach (var z in new[] { -0.7, -0.28 })
            c.Box(-0.12, t, z - 0.02, 0.12, t + 0.08, z + 0.02, Frame, "rubber");
        Rod(c, -0.07, t + 0.07, -0.78, 0.07, t + 0.21, -0.22, "+z", CopperC, "copper", 20);
        foreach (var z in new[] { -0.6, -0.55, -0.5, -0.45, -0.4, -0.35 })
            Rod(c, -0.12, t + 0.02, z, 0.12, t + 0.26, z + 0.012, "+z", Steel, "aluminium", 20);
        Rod(c, -0.055, t + 0.085, -0.223, 0.055, t + 0.195, -0.1, "+z", "#9fd0e0", "glass", 16);
        c.Glow(-0.025, t + 0.115, -0.103, 0.025, t + 0.165, -0.09, Glow);
        c.Box(-0.1, t + 0.05, -0.83, 0.1, t + 0.23, -0.777, BrassC, "brass");

        var (d, s, _) = SysDrawer("Nav Core Drawer", "A sensor rack's drawer: press it and it runs out with the nav chart data cores standing in their rack, their tops lit (one amber: a chart past its date).", Glow);
        d.Box(-0.36, s, -0.52, 0.36, s + 0.08, -0.06, Dark, "plastic");
        var n = 0;
        foreach (var z in new[] { -0.14, -0.29, -0.44 })
            foreach (var x in new[] { -0.28, -0.14, 0.0, 0.14, 0.28 })
            {
                d.Box(x - 0.035, s + 0.04, z - 0.05, x + 0.035, s + 0.24, z + 0.05, Trim, "plastic");
                d.Glow(x - 0.025, s + 0.235, z - 0.04, x + 0.025, s + 0.25, z + 0.04, n == 7 ? Amber : n == 11 ? Green : Glow);
                n++;
            }
        return [i.P.Stats(40, 500, 6, 2.5), c.P.Stats(30, 250, 3, 2.0), d.P.Stats(20, 250, 3, 1.5)];
    }

    // ---- 1 m: weapons. A PDC's belt feeder (the part that wears) over a drawer of ammo cans.

    private static Part[] Pdc()
    {
        var i = SysInsert("PDC Feed Insert", $"An in-wall PDC feed ({Py.G(D)} m deep): the belt chute up to the mount, the feeder's cassette (the part that wears) over a drawer of ammo cans (the consumable).", Red, 5, 3);
        i.Box(-0.13, 1.45, -D + 0.02, 0.13, Y1 + 0.25, -D + 0.16, Steel, "dirty steel");

        var (c, t, _) = SysCassette("Belt Feeder Cassette", "A PDC feed's feeder cassette: the part that wears. It runs out with the feeder, its sprocket on top and a length of belt coming out of it.", Red);
        c.Box(-0.22, t, -0.62, 0.22, t + 0.3, -0.24, Olive, "matte paint");
        Rod(c, -0.2, t + 0.22, -0.52, 0.2, t + 0.38, -0.36, "+x", Steel, "dirty steel", 16);
        c.Box(-0.05, t + 0.238, -0.245, 0.05, t + 0.252, -0.05, Frame, "dirty steel");
        foreach (var z in new[] { -0.215, -0.185, -0.155, -0.125, -0.095, -0.065 })
        {
            Rod(c, -0.06, t + 0.25, z - 0.012, 0.06, t + 0.274, z + 0.012, "+x", BrassC, "brass", 10);
            Rod(c, 0.057, t + 0.254, z - 0.008, 0.085, t + 0.27, z + 0.008, "+x", CopperC, "copper", 10);
        }

        var (d, s, _) = SysDrawer("Ammo Can Drawer", "A PDC feed's drawer: the consumable. Press it and it runs out with six cans of linked rounds.", Red);
        foreach (var x in new[] { -0.27, 0.0, 0.27 })
            foreach (var z in new[] { -0.24, -0.64 })
            {
                d.Box(x - 0.12, s, z - 0.17, x + 0.12, s + 0.28, z + 0.17, Olive, "matte paint");
                d.Box(x - 0.125, s + 0.28, z - 0.175, x + 0.125, s + 0.3, z + 0.175, CanLid, "matte paint");
                d.Box(x - 0.05, s + 0.3, z - 0.012, x + 0.05, s + 0.33, z + 0.012, Frame, "dirty steel");
                d.Box(x - 0.09, s + 0.12, z + 0.167, x + 0.09, s + 0.16, z + 0.178, Hazard, "hazard paint");
            }
        return [i.P.Stats(50, 500, 6, 2.5), c.P.Stats(40, 300, 4, 2.0), d.P.Stats(90, 300, 4, 1.5)];
    }

    // ---- 1.5 m: power. The reactor's fuel: a control rod pack (the part that wears) over a shielded drawer of fuel rods.

    private static Part[] ReactorFuel()
    {
        var i = SysInsert("Reactor Fuel Insert", $"An in-wall reactor fuel loader ({Py.G(D)} m deep): two heavy conduits to the reactor, the control rod cassette (the part that wears) over a shielded drawer of fuel rods (the consumable).", Lime, 4, 2);
        foreach (var x in new[] { -0.28, 0.28 })
            Rod(i, x - 0.06, 1.45, -D + 0.02, x + 0.06, Y1 + 0.25, -D + 0.14, "+y", Steel, "dirty steel", 16);

        var (c, t, _) = SysCassette("Control Rod Cassette", "A reactor's control rod cassette: the part that wears. It runs 1.4 m out with six control rods between their end plates and spacer grids.", Lime);
        foreach (var z0 in new[] { -1.32, -0.14 })
            c.Box(-0.18, t, z0, 0.18, t + 0.24, z0 + 0.03, Steel, "plated steel");
        foreach (var z in new[] { -0.93, -0.53 })
            c.Box(-0.17, t + 0.03, z - 0.008, 0.17, t + 0.22, z + 0.008, Steel, "steel");
        foreach (var x in new[] { -0.11, 0.0, 0.11 })
            foreach (var y in new[] { t + 0.06, t + 0.15 })
                Rod(c, x - 0.025, y, -1.293, x + 0.025, y + 0.05, -0.137, "+z", Dark, "dirty steel");
        c.Box(-0.06, t + 0.237, -0.135, 0.06, t + 0.32, -0.115, Hazard, "hazard paint");

        var (d, s, _) = SysDrawer("Fuel Rod Drawer", "A reactor's fuel drawer: the consumable. Behind a shield plate; press it and it runs out with fuel rods in two rows, their tips green (one red: spent).", Lime);
        d.Box(-0.42, Y0 + 0.04, 0.015, 0.26, SupY1 - 0.16, 0.035, Armour2, "plated steel");
        foreach (var z in new[] { -1.0, -0.6, -0.3 })
            d.Box(-0.36, s, z - 0.02, 0.36, s + 0.17, z + 0.02, Dark, "plated steel");
        foreach (var x in new[] { -0.27, -0.09, 0.09, 0.27 })
            foreach (var y in new[] { s + 0.04, s + 0.13 })
            {
                Rod(d, x - 0.03, y, -1.2, x + 0.03, y + 0.06, -0.15, "+z", Steel, "chrome");
                Rod(d, x - 0.032, y - 0.002, -0.153, x + 0.032, y + 0.062, -0.12, "+z", x == 0.09 && y > s + 0.1 ? Red : Lime, "gloss paint");
            }
        return [i.P.Stats(120, 900, 12, 3.0), c.P.Stats(80, 500, 8, 2.5), d.P.Stats(120, 600, 10, 2.5)];
    }
}
