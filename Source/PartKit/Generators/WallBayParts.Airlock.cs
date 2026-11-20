using CallOfSlop.PartKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The hull airlock, first drafted for the Animation gallery (Karl, 2026-11-20: "Let's try adding an airlock to the outer hull"): a one-person
/// chamber through the 1.5 m void between the lining and the hull. A pressure door fills the lining's opening (the bay's 0.9 x 1.9 m),
/// held by the twist latch on its jamb, and swings into the room on its left hinge; the outer hatch is an armoured plate that slides
/// aside along rails on the hull's outside. Inside: tread plate, ribs, handrails, roof lamps, a cycle panel and lamps, a tether reel,
/// a foot bar and grab bars at the outer sill; in the void beside it, the tank and pump the chamber's air goes back into.
///
/// Each door is its own event door hung on the chamber (no insert: the chamber is the whole fill), so in the game each opens with its
/// own press; the game has no pressurised rooms yet, so the cycle (inner door shut, pump down, outer hatch open) is the gallery's
/// timing only. The doors' Lock input could interlock them later. Built while the 1.5 m depth is set.
/// </summary>
public sealed partial class WallBayParts
{
    // The chamber's inner half width and wall, its roof's underside; where the slice ends on the right (wide enough for the open
    // hatch); the inner door's tab height.
    private const double AirCi = 0.55, AirT = 0.03, AirRoof = 2.10, AirHullX = 1.62, InnerY = 0.5;
    private const string HatchFace = "#3b4249", Tank = "#6b7b8a", Motor = "#3d5a40";

    // Outside the hull's inner face (HullZ): the armour's outer face, the hatch ring's outer face, the hatch's inner and outer faces,
    // the rails' front.
    private static double ArmO => HullZ - 0.12;
    private static double RingO => HullZ - 0.16;
    private static double HatchIn => HullZ - 0.18;
    private static double HatchOut => HullZ - 0.26;
    private static double RailO => HullZ - 0.23;

    private static Part AirlockChamber()
    {
        var k = K("Airlock Chamber", "frame", $"A hull airlock: a one-person chamber {Py.G(D)} m through the void, from a pressure door in the interior lining to a hatch in the hull that slides aside outside. Its air pumps back into a tank in the void before the outer hatch opens. It comes as a wall section: its own deck, lining either side and the hull behind.");
        double hz = HullZ, ao = ArmO, ro = RingO;
        // The deck (room side) and the void's floor grating, wider on the right where the open hatch lies; the lining; the sill.
        k.Paint(-Sw, -0.06, -0.005, AirHullX, 0, 0.62, Deck, "painted deck plate");
        Solid(k, -Sw, -0.06, -0.005, AirHullX, 0, 0.62);
        k.Box(-Sw, -0.06, hz + 0.01, AirHullX, -0.012, -0.012, Frame, "painted grating");
        Lining(k, AirHullX);
        Solid(k, -W, 0, -Lt, W, Y0, 0);
        // The room side: a beacon over the door, a cycle panel above the latch (lamps red, amber, green; a screen; the cycle button).
        k.Box(-0.07, 2.12, -0.003, 0.07, 2.22, 0.04, Frame, "plastic");
        k.Glow(-0.05, 2.135, 0.037, 0.05, 2.205, 0.062, Amber);
        k.Box(0.6, 1.42, -0.003, 0.72, 1.7, 0.03, Frame, "plastic");
        k.Glow(0.615, 1.62, 0.027, 0.64, 1.66, 0.038, Red);
        k.Glow(0.648, 1.62, 0.027, 0.673, 1.66, 0.038, Amber);
        k.Glow(0.681, 1.62, 0.027, 0.706, 1.66, 0.038, Green);
        k.Glow(0.615, 1.545, 0.027, 0.705, 1.6, 0.036, Glow);
        k.Box(0.63, 1.45, 0.027, 0.69, 1.52, 0.042, Hazard, "hazard paint");

        // The chamber: tread-plate floor level with the sill, walls and roof through the void to the hull, pale over a blue band.
        k.Box(-(AirCi + 0.006), 0.04, hz + 0.003, AirCi + 0.006, Y0, -0.055, Steel, "tread plate");
        k.Paint(-(AirCi + 0.006), AirRoof, hz + 0.003, AirCi + 0.006, AirRoof + 0.04, -0.055, Wall, "wall panelling");
        foreach (var s in new[] { -1.0, 1.0 })
        {
            k.Paint(s * AirCi, -0.015, hz, s * (AirCi + AirT), AirRoof + 0.045, -0.052, Wall, "wall panelling");
            k.Paint(s * (AirCi - 0.009), Y0 - 0.002, hz + 0.04, s * (AirCi + 0.003), 0.38, -0.06, WallLow, "painted steel");
            Solid(k, s * AirCi, -0.015, hz, s * (AirCi + AirT), AirRoof + 0.045, -0.052);
            // A handrail along each wall on three brackets; a grab bar by the outer sill.
            Rod(k, s * (AirCi - 0.08), 1.03, -1.5, s * (AirCi - 0.05), 1.06, -0.25, "+z", Steel, "steel");
            foreach (var z in new[] { -0.4, -0.9, -1.4 })
                k.Box(s * (AirCi - 0.065), 1.035, z - 0.012, s * (AirCi + 0.003), 1.055, z + 0.012, Steel, "steel");
            Rod(k, s * (AirCi - 0.055), 0.55, hz + 0.075, s * (AirCi - 0.025), 1.65, hz + 0.105, "+y", Hazard, "hazard paint");
            foreach (var y in new[] { 0.6, 1.6 })
                k.Box(s * (AirCi - 0.045), y - 0.012, hz + 0.078, s * (AirCi + 0.003), y + 0.012, hz + 0.102, Steel, "steel");
        }
        Solid(k, -AirCi, 0.04, hz, AirCi, Y0, -0.052);
        Solid(k, -AirCi, AirRoof, hz, AirCi, AirRoof + 0.04, -0.052);
        // Two ribs round the chamber (walls and roof), a lamp in each third of the roof, a beacon over the outer hatch.
        foreach (var zc in new[] { -0.62, -1.18 })
        {
            foreach (var s in new[] { -1.0, 1.0 })
                k.Box(s * (AirCi - 0.03), 0.096, zc - 0.03, s * (AirCi + 0.004), 2.08, zc + 0.03, Frame, "dirty steel");
            k.Box(-(AirCi + 0.002), 2.065, zc - 0.027, AirCi + 0.002, AirRoof + 0.003, zc + 0.027, Frame, "dirty steel");
        }
        foreach (var zc in new[] { -0.34, -0.9, -1.46 })
        {
            k.Box(-0.15, 2.07, zc - 0.08, 0.15, AirRoof + 0.003, zc + 0.08, Frame, "plastic");
            k.Glow(-0.13, 2.062, zc - 0.06, 0.13, 2.073, zc + 0.06, "#e8f0ff");
        }
        k.Box(-0.06, 2.05, hz + 0.06, 0.06, AirRoof + 0.003, hz + 0.14, Frame, "plastic");
        k.Glow(-0.045, 2.038, hz + 0.075, 0.045, 2.055, hz + 0.125, Amber);
        // The cycle panel inside, on the right wall by the inner door; a tether reel on the left by the hull; a floor vent, a foot bar.
        k.Box(AirCi - 0.025, 1.22, -0.36, AirCi + 0.003, 1.62, -0.14, Frame, "plastic");
        k.Glow(AirCi - 0.033, 1.5, -0.33, AirCi - 0.022, 1.55, -0.29, Red);
        k.Glow(AirCi - 0.033, 1.5, -0.27, AirCi - 0.022, 1.55, -0.23, Amber);
        k.Glow(AirCi - 0.033, 1.5, -0.21, AirCi - 0.022, 1.55, -0.17, Green);
        k.Box(AirCi - 0.036, 1.3, -0.29, AirCi - 0.022, 1.4, -0.21, Hazard, "hazard paint");
        Rod(k, -(AirCi + 0.003), 1.55, -1.52, -(AirCi - 0.06), 1.71, -1.36, "+x", Hazard, "hazard paint", 16);
        Rod(k, -(AirCi - 0.057), 1.6, -1.47, -(AirCi - 0.075), 1.66, -1.41, "+x", Frame, "dirty steel");
        k.Box(-0.25, 0.093, -1.35, 0.25, 0.104, -1.05, Frame, "painted grating");
        Rod(k, -0.4, 0.14, hz + 0.16, 0.4, 0.17, hz + 0.19, "+x", Steel, "steel");
        foreach (var sx in new[] { -0.38, 0.38 })
            k.Box(sx - 0.012, 0.095, hz + 0.163, sx + 0.012, 0.155, hz + 0.187, Steel, "steel");

        // The hull: skin and armour round an opening the inner one's size, a dark ring through them, hazard stripes on the outer sill
        // and on the ring's outer face (seen when the hatch is aside).
        void HullPiece(double x0, double y0, double x1, double y1)
        {
            k.Box(x0, y0, hz - 0.06, x1, y1, hz, Skin2, "painted plating");
            k.Box(x0, y0, ao, x1, y1, hz - 0.06, Armour2, "plated steel");
            Solid(k, x0, y0, ao, x1, y1, hz);
        }
        HullPiece(-Sw, -0.06, -W, Top + 0.15);
        HullPiece(W, -0.06, AirHullX, Top + 0.15);
        HullPiece(-W, -0.06, W, Y0);
        HullPiece(-W, Y1, W, Top + 0.15);
        void Ring(double x0, double y0, double x1, double y1)
        {
            k.Box(x0, y0, ro, x1, y1, hz + 0.04, Frame, "dirty steel");
            Solid(k, x0, y0, ro, x1, y1, hz + 0.04);
        }
        Ring(-(W + 0.08), Y0 - 0.05, -(W - 0.01), Y1 + 0.05);
        Ring(W - 0.01, Y0 - 0.05, W + 0.08, Y1 + 0.05);
        Ring(-(W - 0.01), Y0 - 0.05, W - 0.01, Y0 + 0.01);
        Ring(-(W - 0.01), Y1 - 0.01, W - 0.01, Y1 + 0.05);
        for (var i = 0; i < 9; i++)
            k.Box(-0.42 + i * 0.1, Y0 + 0.007, ro + 0.02, -0.37 + i * 0.1, Y0 + 0.018, hz + 0.02, Hazard, "hazard paint");
        foreach (var s in new[] { -1.0, 1.0 })
            for (var i = 0; i < 10; i++)
                k.Box(s * (W - 0.005), 0.1 + i * 0.2, ro - 0.008, s * (W + 0.075), 0.2 + i * 0.2, ro + 0.003, Hazard, "hazard paint");
        // The hatch's rails along the outside, top and bottom, out to the right where it lies open; its hang tab on the top rail.
        k.Box(-0.62, Y1 + 0.08, RailO, AirHullX - 0.02, Y1 + 0.14, ao + 0.003, Steel, "steel");
        k.Box(-0.62, -0.02, RailO, AirHullX - 0.02, 0.03, ao + 0.003, Steel, "steel");
        Solid(k, -0.5, Y1 + 0.08, HatchIn, -0.48, Y1 + 0.1, RingO);
        k.P.Sock("outer", [-0.49, Y1 + 0.09, HatchIn], "-z");
        // Outside, left of the hatch: two EVA handholds and the outer lamps (red over green) on a dark plate.
        foreach (var y in new[] { 0.75, 1.45 })
            k.Box(-0.795, y - 0.012, ao - 0.04, -0.775, y + 0.012, ao + 0.003, Steel, "steel");
        Rod(k, -0.8, 0.7, ao - 0.06, -0.77, 1.5, ao - 0.03, "+y", Hazard, "hazard paint");
        k.Box(-0.66, 1.58, ao - 0.008, -0.56, 1.82, ao + 0.003, Frame, "plastic");
        k.Glow(-0.64, 1.72, ao - 0.012, -0.58, 1.78, ao - 0.001, Red);
        k.Glow(-0.64, 1.62, ao - 0.012, -0.58, 1.68, ao - 0.001, Green);

        // The void either side: ribs, lagging on the skin, the pipes and cable tray over the chamber's roof; on the right, the
        // air plant: a tank on its stand, the pump with its motor, the line through the rib into the chamber's wall.
        foreach (var (x0, x1) in new[] { (-0.76, -0.66), (0.66, 0.76), (1.4, 1.5) })
        {
            k.Box(x0, -0.06, hz, x1, Top + 0.1, -Lt - 0.004, Armour2, "plated steel");
            Solid(k, x0, -0.06, hz, x1, Top + 0.1, -Lt);
        }
        foreach (var (x0, x1) in new[] { (-(Sw - 0.01), -0.78), (0.78, 1.38), (1.52, AirHullX - 0.01) })
            k.Box(x0, 0.0, hz + 0.002, x1, Top + 0.1, hz + 0.07, Lag, "lagging");
        Rod(k, -Sw, 2.19, hz + 0.06, AirHullX, 2.27, hz + 0.14, "+x", PipeRed, "painted steel");
        Rod(k, -Sw, 2.19, hz + 0.17, AirHullX, 2.27, hz + 0.25, "+x", PipeBlue, "painted steel");
        k.Box(-Sw, Top, -0.4, AirHullX, Top + 0.02, -0.16, Steel, "steel");
        k.Box(-Sw, Top + 0.02, -0.38, AirHullX, Top + 0.1, -0.18, Frame, "rubber");
        Rod(k, 0.86, 0.35, -1.25, 1.26, 1.85, -0.85, "+y", Tank, "gloss paint", 16);
        Rod(k, 0.9, 1.847, -1.21, 1.22, 1.9, -0.89, "+y", Frame, "dirty steel", 16);
        k.Box(0.9, -0.015, -1.21, 1.22, 0.353, -0.89, Frame, "dirty steel");
        k.Box(0.86, -0.015, -0.62, 1.12, 0.28, -0.36, Steel, "painted steel");
        Rod(k, 1.117, 0.07, -0.56, 1.3, 0.23, -0.42, "+x", Motor, "painted steel");
        Rod(k, AirCi + AirT - 0.003, 0.16, -0.52, 0.863, 0.22, -0.46, "+x", PipeYellow, "painted steel");
        Rod(k, 0.98, 0.1, -0.893, 1.04, 0.16, -0.617, "+z", PipeYellow, "painted steel");

        k.P.Sock("deck", [0, -0.06, 0.2], "-y");
        k.P.Sock("hull", [-0.7, 1.0, ao], "-z");
        Tab(k, "inner", InnerY);
        Tab(k, "latch", LatchY);
        return k.P.Stats(380, 3000, 30, 10.0);
    }

    private static Part AirlockInnerDoor()
    {
        var k = Mover("Airlock Inner Door", "An airlock's inner pressure door: twist the latch on its jamb and it swings into the room on its left hinge; a porthole shows the chamber.", "inner", [0, InnerY, 0]);
        const double Z0 = -0.04, Z1 = 0.012;
        // The slab round the porthole (four pieces), its glass, the ring round it; the blue band and a hazard kick strip; a pull on the
        // latch side, hinge knuckles on the left, a lit label at the top.
        k.Paint(-0.43, Y0 + 0.01, Z0, 0.43, 1.3, Z1, Wall, "wall panelling");
        k.Paint(-0.43, 1.68, Z0, 0.43, Y1 - 0.01, Z1, Wall, "wall panelling");
        k.Paint(-0.43, 1.3, Z0, -0.15, 1.68, Z1, Wall, "wall panelling");
        k.Paint(0.15, 1.3, Z0, 0.43, 1.68, Z1, Wall, "wall panelling");
        k.Glass(-0.153, 1.297, -0.02, 0.153, 1.683, -0.009);
        k.Box(-0.19, 1.26, 0.009, 0.19, 1.31, 0.026, Frame, "plastic");
        k.Box(-0.19, 1.67, 0.009, 0.19, 1.72, 0.026, Frame, "plastic");
        k.Box(-0.19, 1.31, 0.009, -0.14, 1.67, 0.026, Frame, "plastic");
        k.Box(0.14, 1.31, 0.009, 0.19, 1.67, 0.026, Frame, "plastic");
        k.Paint(-0.425, Y0 + 0.015, 0.008, 0.425, 0.38, 0.02, WallLow, "painted steel");
        for (var i = 0; i < 7; i++)
            k.Box(-0.42 + i * 0.12, 0.4, 0.008, -0.3 + i * 0.12, 0.46, 0.019, i % 2 == 0 ? Hazard : Frame, "hazard paint");
        Rod(k, 0.29, 0.86, 0.045, 0.33, 1.24, 0.075, "+y", Frame, "rubber");
        foreach (var y in new[] { 0.88, 1.195 })
            k.Box(0.298, y, 0.009, 0.322, y + 0.025, 0.05, Steel, "steel");
        foreach (var y in new[] { 0.3, 1.0, 1.7 })
            Rod(k, -0.45, y, 0.005, -0.42, y + 0.08, 0.035, "+y", Steel, "steel");
        k.Box(-0.2, 1.8, 0.009, 0.2, 1.88, 0.022, Frame, "plastic");
        k.Glow(-0.18, 1.815, 0.02, 0.18, 1.865, 0.026, Amber);
        Solid(k, -0.43, Y0 + 0.01, Z0, 0.43, Y1 - 0.01, Z1);
        Swing(k.P, "+y", -90, V(-0.435, 0, 0.02), 1.2);
        return k.P.Stats(60, 800, 12, 2.0);
    }

    private static Part AirlockOuterHatch()
    {
        var k = K("Airlock Outer Hatch", "door", "An airlock's outer hull hatch: an armoured plate on rollers that slides aside along rails on the outside of the hull.", Armour2);
        double z0 = HatchOut, z1 = HatchIn;
        // (its hang socket on the +z face of a small tab at the top rail, meeting the chamber's tab there)
        Solid(k, -0.5, Y1 + 0.08, z1 - 0.02, -0.48, Y1 + 0.1, z1);
        k.P.Sock("hang", [-0.49, Y1 + 0.09, z1], "+z");
        // The plate, a raised face on it, hazard stripes down both edges, an EVA grab handle; hangers up to the top rail, shoes down to
        // the bottom one.
        k.Box(-0.49, Y0 - 0.04, z0, 0.49, Y1 + 0.04, z1, Armour2, "plated steel");
        k.Box(-0.38, 0.18, z0 - 0.012, 0.38, 1.92, z0 + 0.003, HatchFace, "plated steel");
        foreach (var s in new[] { -1.0, 1.0 })
            for (var i = 0; i < 10; i++)
                k.Box(s * 0.42, 0.1 + i * 0.2, z0 - 0.011, s * 0.47, 0.2 + i * 0.2, z0 + 0.003, Hazard, "hazard paint");
        Rod(k, 0.2, 0.85, z0 - 0.055, 0.24, 1.35, z0 - 0.025, "+y", Hazard, "hazard paint");
        foreach (var y in new[] { 0.87, 1.305 })
            k.Box(0.208, y, z0 - 0.04, 0.232, y + 0.025, z0 - 0.009, Steel, "steel");
        foreach (var sx in new[] { -0.32, 0.32 })
        {
            k.Box(sx - 0.05, Y1 + 0.035, z1 - 0.035, sx + 0.05, Y1 + 0.075, z1 - 0.01, Frame, "dirty steel");
            k.Box(sx - 0.04, Y0 - 0.065, z1 - 0.035, sx + 0.04, Y0 - 0.035, z1 - 0.01, Frame, "dirty steel");
        }
        Solid(k, -0.49, Y0 - 0.04, z0, 0.49, Y1 + 0.04, z1);
        Slide(k.P, 1.05, 2.0, "+x");
        return k.P.Stats(140, 1500, 25, 3.0);
    }
}
