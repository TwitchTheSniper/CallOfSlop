using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Limpet, the Zero Day's boarding pod (Karl, 2026-11-17, docs/sandbox/constructs/limpet.md): a plain armoured box for eight
/// boarders and no pilot, flown from the ship's flight controller. It rides under the Zero Day's keel, its roof receiver on the
/// ship's docking hatch; it flies to a ship on RCS alone (a one-way trip), puts its nose on a door or a hull plate, its four mag
/// locks grip, and either its seal skirt closes round the door or the breaching cutter ringing its nose door burns through the
/// hull; then the nose door, a sliding bulkhead in two leaves, parts and the boarders go in.
///
/// Pieces welded like the Zero Day's: the floor, a side each (wall, chamfers, the RCS outriggers), the roof (the receiver's hole),
/// the nose (the bulkhead, its door frame and the seal skirt) and the stern. The pod's own frame: x across from its centreline,
/// y up from its belly, z from its middle back (forward is -z). Its rooms' layout is the 2D plan Karl approved
/// (/mnt/project-files/limpet/limpet-layout-v3.png): seats four a side facing the aisle, the ladder up to the receiver centred
/// at the aft end, the flight computer to starboard aft and the batteries to port aft.
/// </summary>
public sealed partial class TorpedoFrigateParts
{
    public const string LimpetName = "Limpet";
    private const string PSection = "Limpet";
    private const string POut = "Constructs/Parts/Limpet";

    // The section (half): belly at 0, roof at PH; walls at PW with 0.3 m chamfers along the four long edges.
    private const double PW = 1.5, PH = 2.35, PCh = 0.3, PWallIn = 1.37, PFt = 0.25, PCeil = 2.23;
    private const double PZ0 = -2.8, PZ1 = 2.8, PIn = 2.68; // the nose's and the stern's faces, the rooms' ends (inner faces)
    private const double PDoorW = 0.55, PDoorY0 = 0.26, PDoorY1 = 2.18; // the nose door's opening (half width) and its sill and head
    private const double PSkirtZ = -3.0, PGasketZ = -3.15; // the seal skirt's armour face and its gasket's face
    private const double PCutY = 1.19, PCutHang = 1.06; // the cutter ring's middle (its outer edge 0.08 to 2.30) and its socket over that, on the block over the door
    private static readonly Rect PHole = new(-0.55, 1.5, 0.55, 2.6); // the receiver's hole through the roof (x, z)
    private static readonly double[] PSeatZ = [-1.55, -0.69, 0.17, 1.03];

    // Docked under the Zero Day's keel (its frame): the pod's roof just under the keel's docking hatch, its receiver on the centreline.
    private const double LimpetRoofY = -1.7, LimpetBaseY = LimpetRoofY - PH, LimpetRecvZ = 31.4;
    private const double PHatchZ = 1.95; // the docking hatch's middle under the ceiling (its shoe 0.69 m aft of it, clear of the stern; its lid over the hole)
    private const double LimpetLadderRise = Ft0 - (LimpetBaseY + PFt + 0.013); // (from the pod's deck up to the lower deck's floor)
    private const double LimpetOriginZ = LimpetRecvZ - 2.05; // (the pod's middle: its receiver is 2.05 m aft of it)
    private const double LimpetClampX = Cx - 2.1, LimpetClampZ = 29.3; // the Docking Clamp under the keel to port of the pod

    /// <summary>The Limpet's Docking Clamp, upside down under the keel beside the pod: holding from the start, its box round the pod.</summary>
    private static PyDict LimpetClampConfig()
    {
        double cx = LimpetClampX, cy = -1.58 - 0.5, cz = LimpetClampZ; // (the clamp's middle: half a metre under the keel's underside)
        double bx = Cx, by = LimpetBaseY + PH / 2, bz = LimpetOriginZ; // (the box's middle: the pod's)
        // (the clamp hangs upside down, turned half a turn about x: its frame's y and z are the ship's -y and -z)
        return new PyDict
        {
            ["startOn"] = true,
            ["sizeX"] = 4.4, ["sizeY"] = 2.8, ["sizeZ"] = 6.8,
            ["atX"] = Py.Round(bx - cx, 2), ["atY"] = Py.Round(-(by - cy), 2), ["atZ"] = Py.Round(-(bz - cz), 2),
        };
    }

    private static Part PBig(string name, string desc, string role = "frame", string color = Skin) =>
        new(name, Small, PSection, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);

    /// <summary>The section's outline at the full width (convex, for a cap), x scaled by side or taken whole.</summary>
    private static (double X, double Y)[] PSection8 =>
        [(-PW + PCh, 0), (PW - PCh, 0), (PW, PCh), (PW, PH - PCh), (PW - PCh, PH), (-PW + PCh, PH), (-PW, PH - PCh), (-PW, PCh)];

    private static double[][] PRing((double X, double Y)[] pts, double z) => [.. pts.Select(q => P(q.X, q.Y, z))];

    private static void PPrism(Part p, (double X, double Y)[] pts, double z0, double z1, string color, string finish, bool glow = false) =>
        Prism(p, PRing(pts, z0), PRing(pts, z1), color, finish, glow);

    // ---- the floor

    private static Part PFloor()
    {
        var p = PBig("Limpet Floor", "The Limpet's floor: the belly plate, the deck the boarders' seats stand on, the flight computer's console and the batteries' footing.", color: Armour);
        Hidden(p, -PWallIn, 0, -PIn + 0.003, PWallIn, PFt, PIn - 0.003);
        Detail(p, -PW + PCh - 0.003, 0.003, -PIn + 0.006, PW - PCh + 0.003, PFt - 0.003, PIn - 0.006, Armour, "plated steel");
        Detail(p, -PWallIn + 0.003, PFt - 0.003, -PIn + 0.006, PWallIn - 0.003, PFt + 0.013, PIn - 0.006, Deck, "painted deck plate");
        Detail(p, -0.55, PFt + 0.013, -2.48, 0.55, PFt + 0.029, 1.42, Grate, "painted grating");
        Detail(p, -0.55, PFt + 0.013, -PIn + 0.01, 0.55, PFt + 0.029, -2.5, Hazard, "hazard paint"); // (the threshold inside the nose door)
        Detail(p, -0.6, PFt + 0.013, PHole.Z0 - 0.15, 0.6, PFt + 0.029, PHole.Z0 - 0.04, Hazard, "hazard paint"); // (the foot of the ladder)
        // The belly's armour: four slabs under the plate, stopped short of the chamfers.
        foreach (var (z0, z1) in new[] { (-2.6, -1.35), (-1.3, -0.05), (0.05, 1.3), (1.35, 2.6) })
            Detail(p, -1.05, -0.035, z0, 1.05, -0.003, z1, Armour2, "plated steel");
        // The flight computer's console, starboard aft: a pedestal with a screen to the aisle.
        Solid(p, 0.82, PFt + 0.013, 1.98, 1.3, 0.98, 2.62, Console, "plastic");
        Detail(p, 0.79, 0.62, 2.05, 0.818, 0.9, 2.55, Screen, glow: true);
        Detail(p, 0.79, 0.5, 2.1, 0.818, 0.55, 2.2, "#40e070", glow: true);
        Detail(p, 0.79, 0.5, 2.25, 0.818, 0.55, 2.35, Amber, glow: true);
        Detail(p, 0.83, 0.98, 1.99, 1.29, 0.996, 2.61, Hazard, "hazard paint");
        // Sockets: the eight seats (four a side, backs to the walls), the computer, the batteries, the ladder.
        for (var i = 0; i < PSeatZ.Length; i++)
        {
            Sock(p, $"seatL{i}", -0.94, PFt, PSeatZ[i], "+y");
            Sock(p, $"seatR{i}", 0.94, PFt, PSeatZ[i], "+y");
        }
        Sock(p, "computer", 1.06, 0.98, 2.3, "+y");
        Sock(p, "battery0", -1.05, PFt, 2.1, "+y");
        Sock(p, "battery1", -1.05, PFt, 2.4, "+y");
        Sock(p, "ladder", 0, PFt, PIn - 0.1, "+y");
        return p.Stats(900, 6000, 40, 12.0);
    }

    // ---- a side: the wall, its chamfers, the RCS outriggers

    private static Part PSide(int s)
    {
        var name = s < 0 ? "Limpet Side Port" : "Limpet Side Starboard";
        var p = PBig(name, $"The Limpet's {(s < 0 ? "port" : "starboard")} side: the wall and its chamfers, the armour on it and the two RCS outriggers, fore and aft.", color: Skin);
        double z0 = -PIn + 0.003, z1 = PIn - 0.003;
        (double, double)[] M((double X, double Y)[] q) => [.. q.Select(v => (s * v.X, v.Y))];
        Hidden(p, s * PWallIn, 0, -PIn + 0.003, s * PW, PH, PIn - 0.003);
        PPrism(p, M([(PW - PCh, 0.003), (PW, PCh + 0.003), (PWallIn, PCh + 0.003), (PW - PCh, PFt + 0.003)]), z0, z1, Armour, "plated steel");
        Detail(p, s * PWallIn, PCh + 0.003, z0, s * PW, PH - PCh - 0.003, z1, Skin, "painted plating");
        PPrism(p, M([(PW, PH - PCh - 0.003), (PW - PCh, PH - 0.003), (PW - PCh, PCeil - 0.003), (PWallIn, PH - PCh - 0.003)]), z0, z1, Armour, "plated steel");
        // Inside: the lining, blue under the dado, pale above, a trim strip between.
        Detail(p, s * (PWallIn - 0.018), PCh + 0.01, z0 + 0.01, s * (PWallIn - 0.002), 1.0, z1 - 0.01, WallLow, "painted plating");
        Detail(p, s * (PWallIn - 0.018), 1.04, z0 + 0.01, s * (PWallIn - 0.002), PCeil - 0.06, z1 - 0.01, Wall, "painted plating");
        Detail(p, s * (PWallIn - 0.022), 1.0, z0 + 0.01, s * (PWallIn - 0.004), 1.04, z1 - 0.01, WallTrim, "painted steel");
        // Outside: two armour slabs, a blue line, hazard round the outriggers' feet.
        foreach (var (a, b) in new[] { (-1.8, -0.06), (0.06, 1.8) })
            Detail(p, s * (PW + 0.003), 0.45, a, s * (PW + 0.023), 1.9, b, Armour2, "plated steel");
        Detail(p, s * (PW + 0.003), 1.94, -1.8, s * (PW + 0.019), 1.97, 1.8, Blue, glow: true);
        foreach (var (tag, zc) in new[] { ("F", -2.2), ("B", 2.2) })
        {
            double xa = s * 1.525, xb = s * 1.8;
            Hidden(p, s * PW, 0.85, zc - 0.3, xb, 1.55, zc + 0.3);
            Solid(p, xa, 0.85, zc - 0.3, xb, 1.55, zc + 0.3, Armour, "plated steel");
            Detail(p, s * PW + s * 0.003, 0.8, zc - 0.34, s * 1.522, 1.6, zc + 0.34, Hazard, "hazard paint");
            Detail(p, xb, 1.12, zc - 0.25, xb + s * 0.018, 1.28, zc + 0.25, Frame, "dirty steel");
            Sock(p, $"rcsOut{tag}", s * 1.8, 1.2, zc, s < 0 ? "-x" : "+x");
            Sock(p, $"rcsUp{tag}", s * 1.66, 1.55, zc, "+y");
            Sock(p, $"rcsDn{tag}", s * 1.66, 0.85, zc, "-y");
            Sock(p, $"rcsEnd{tag}", s * 1.66, 1.2, tag == "F" ? zc - 0.3 : zc + 0.3, tag == "F" ? "-z" : "+z");
        }
        // Lamps high on the wall, the switches by the door (starboard), facing in.
        var inward = s < 0 ? "+x" : "-x";
        Sock(p, "lampF", s * PWallIn, 1.95, -1.1, inward);
        Sock(p, "lampB", s * PWallIn, 1.95, 0.9, inward);
        Sock(p, "switchA", s * PWallIn, 1.35, -2.3, inward);
        Sock(p, "switchB", s * PWallIn, 1.2, -2.3, inward);
        Sock(p, "switchC", s * PWallIn, 1.05, -2.3, inward);
        return p.Stats(700, 6000, 40, 10.0);
    }

    // ---- the roof and its receiver

    private static Part PRoof()
    {
        var p = PBig("Limpet Roof", "The Limpet's roof: the ceiling and its light, and the receiver: the hole the ladder climbs through into the Zero Day when the pod is docked under its keel.", color: Skin);
        var h = PHole;
        foreach (var r in Minus([new Rect(-PWallIn, -PIn + 0.003, PWallIn, PIn - 0.003)], h))
            Hidden(p, r.X0, PCeil, r.Z0, r.X1, PH, r.Z1);
        foreach (var r in Minus([new Rect(-PW + PCh - 0.003, -PIn + 0.006, PW - PCh + 0.003, PIn - 0.006)], h))
            Detail(p, r.X0, PCeil + 0.003, r.Z0, r.X1, PH - 0.003, r.Z1, Skin, "painted plating");
        foreach (var r in Minus([new Rect(-PWallIn + 0.02, -PIn + 0.02, PWallIn - 0.02, PIn - 0.02)], new Rect(h.X0 - 0.003, h.Z0 - 0.003, h.X1 + 0.003, h.Z1 + 0.003)))
            Detail(p, r.X0, PCeil - 0.013, r.Z0, r.X1, PCeil + 0.003, r.Z1, Ceil, "painted plating");
        Detail(p, -0.08, PCeil - 0.033, -2.3, 0.08, PCeil - 0.013, 1.3, "#d8f0ff", glow: true);
        // The receiver: an armoured collar round the hole, hazard on its lip, the hole lined with steel.
        foreach (var (x0, z0, x1, z1) in new[] { (h.X0 - 0.14, h.Z0 - 0.14, h.X1 + 0.14, h.Z0), (h.X0 - 0.14, h.Z1, h.X1 + 0.14, h.Z1 + 0.14), (h.X0 - 0.14, h.Z0 + 0.003, h.X0, h.Z1 - 0.003), (h.X1, h.Z0 + 0.003, h.X1 + 0.14, h.Z1 - 0.003) })
        {
            Solid(p, x0, PH, z0, x1, PH + 0.035, z1, Armour2, "plated steel");
            Detail(p, x0 + 0.01, PH + 0.035, z0 + 0.01, x1 - 0.01, PH + 0.051, z1 - 0.01, Hazard, "hazard paint");
        }
        foreach (var (x0, z0, x1, z1) in new[] { (h.X0, h.Z0, h.X1, h.Z0 + 0.016), (h.X0, h.Z1 - 0.016, h.X1, h.Z1), (h.X0, h.Z0 + 0.019, h.X0 + 0.016, h.Z1 - 0.019), (h.X1 - 0.016, h.Z0 + 0.019, h.X1, h.Z1 - 0.019) })
            Detail(p, x0, PCeil - 0.01, z0, x1, PH - 0.004, z1, Frame, "dirty steel");
        // The Docking Hatch hangs under the ceiling over the hole (its shoe on a pad aft of it), and slides forward along the ceiling to open:
        // it pairs with the one in the Zero Day's keel over the shaft (ConstructDoors.StepDocks).
        Hidden(p, -0.32, PCeil - 0.005, PHatchZ + DockHatchHang - 0.04, 0.32, PCeil + 0.05, PIn - 0.003); // (5 cm at least, or Hidden drops it)
        Sock(p, "hatch", 0, PCeil - 0.005, PHatchZ + DockHatchHang, "-y");
        return p.Stats(600, 5000, 35, 10.0);
    }

    // ---- the nose: the bulkhead, the door's frame and the seal skirt

    private static Part PNose()
    {
        var p = PBig("Limpet Nose", "The Limpet's nose: the armoured bulkhead with the door in it, and round the door the seal skirt it presses on a ship's hull or door, with its rubber gasket; the breaching cutter and the four mag locks fit on it.", color: Armour);
        double za = PZ0 + 0.003, zb = -PIn - 0.003;
        // The bulkhead in four blocks round the door (each convex).
        (double, double)[] left = [(-PW, PCh), (-PW + PCh, 0), (-PDoorW, 0), (-PDoorW, PH), (-PW + PCh, PH), (-PW, PH - PCh)];
        PPrism(p, left, za, zb, Armour, "plated steel");
        PPrism(p, [.. left.Select(q => (-q.Item1, q.Item2)).Reverse()], za, zb, Armour, "plated steel");
        Detail(p, -PDoorW + 0.003, PDoorY1, za, PDoorW - 0.003, PH, zb, Armour, "plated steel");
        Detail(p, -PDoorW + 0.003, 0, za, PDoorW - 0.003, PDoorY0, zb, Armour, "plated steel");
        foreach (var r in Minus([new Rect(-PW, 0, PW, PH)], new Rect(-PDoorW, PDoorY0, PDoorW, PDoorY1)))
            Hidden(p, r.X0, r.Z0, PZ0, r.X1, r.Z1, -PIn + 0.003); // (Rect's z is y here: the blocks round the door)
        // Inside: a hazard frame round the door.
        Detail(p, -PDoorW - 0.1, PDoorY1, zb, PDoorW + 0.1, PDoorY1 + 0.08, zb + 0.016, Hazard, "hazard paint");
        foreach (var x in new[] { -PDoorW - 0.1, PDoorW })
            Detail(p, x, PDoorY0, zb, x + 0.1, PDoorY1 - 0.003, zb + 0.016, Hazard, "hazard paint");
        Detail(p, PDoorW + 0.12, 1.75, zb, PDoorW + 0.2, 1.85, zb + 0.02, Red, glow: true);
        Detail(p, PDoorW + 0.12, 1.6, zb, PDoorW + 0.2, 1.7, zb + 0.02, "#40e070", glow: true);
        // The skirt: an armoured collar round the cutter's ring, hazard on its face, the gasket on its inner edge.
        double sa = PSkirtZ, sb = PZ0 - 0.003;
        foreach (var sd in new[] { -1, 1 })
        {
            Solid(p, sd * 0.76, 0.05, sa, sd * 1.42, 2.3, sb, Armour2, "plated steel");
            Detail(p, sd * 0.98, 0.08, sa - 0.016, sd * 1.39, 2.27, sa, Hazard, "hazard paint");
            Detail(p, sd * 0.78, 0.07, PGasketZ, sd * 0.95, 2.29, sa, "#1c1e20", "rubber");
        }
        Detail(p, -0.76 + 0.003, 2.31, sa, 0.76 - 0.003, 2.345, sb, Armour2, "plated steel");
        Detail(p, -0.76 + 0.003, 0.015, sa, 0.76 - 0.003, 0.07, sb, Armour2, "plated steel");
        Detail(p, -0.76 + 0.003, 2.315, PGasketZ, 0.76 - 0.003, 2.342, sa, "#1c1e20", "rubber");
        Detail(p, -0.76 + 0.003, 0.018, PGasketZ, 0.76 - 0.003, 0.066, sa, "#1c1e20", "rubber");
        // Sockets: the door's two leaves (in the opening, their backs flush with the inner face), the cutter, the mag locks.
        Sock(p, "doorL", -PDoorW, 1.22, -2.74, "+x"); // (on the door's jambs: each leaf hangs by its outer edge)
        Sock(p, "doorR", PDoorW, 1.22, -2.74, "-x");
        Sock(p, "cutter", 0, PCutY + PCutHang, PZ0, "-z"); // (on the block over the door)
        foreach (var (n, x, y) in new[] { ("lockTL", -1.15, 1.95), ("lockTR", 1.15, 1.95), ("lockBL", -1.15, 0.45), ("lockBR", 1.15, 0.45) })
            Sock(p, n, x, y, PSkirtZ, "-z");
        return p.Stats(900, 9000, 60, 12.0);
    }

    // ---- the stern

    private static Part PStern()
    {
        var p = PBig("Limpet Stern", "The Limpet's stern: a plain armoured bulkhead, the aft wall the ladder stands against. There is no drive: the pod flies on its RCS alone, one way.", color: Armour);
        double za = PIn + 0.003, zb = PZ1 - 0.003;
        PPrism(p, PSection8, za, zb, Armour, "plated steel");
        Hidden(p, -PW, 0, PIn + 0.003, PW, PH, PZ1);
        Detail(p, -PWallIn + 0.02, PCh, za - 0.016, PWallIn - 0.02, 1.0, za, WallLow, "painted plating");
        Detail(p, -PWallIn + 0.02, 1.04, za - 0.016, PWallIn - 0.02, PCeil - 0.06, za, Wall, "painted plating");
        // Outside: slabs, a hazard band, the tail lamps.
        foreach (var sd in new[] { -1, 1 })
            Detail(p, sd * 0.12, 0.4, zb, sd * 1.2, 2.0, zb + 0.022, Armour2, "plated steel");
        Detail(p, -1.2, 2.06, zb, 1.2, 2.12, zb + 0.016, Hazard, "hazard paint");
        foreach (var sd in new[] { -1, 1 })
            Detail(p, sd * 1.25, 1.7, zb, sd * 1.38, 1.9, zb + 0.02, Red, glow: true);
        return p.Stats(500, 5000, 40, 8.0);
    }

    // ---- fittings

    /// <summary>A leaf of the nose door: half a sliding bulkhead that parts sideways into the nose's armour. It hangs by its outer
    /// edge on the door's jamb (its socket's frame: x across from the jamb, inwards for the port leaf; y up from 1.22; z back).</summary>
    private static Part PDoorLeaf(int s)
    {
        var name = s < 0 ? "Limpet Door Leaf L" : "Limpet Door Leaf R";
        var p = new Part(name, Small, PSection, "A leaf of the Limpet's nose door: half of a sliding bulkhead that parts " + (s < 0 ? "to port" : "to starboard") + " into the nose's armour. Both leaves open together on the door switch, or when the pod has sealed on a door or cut through a hull.", role: "door", tool: "welder", material: "steel", color: Armour2, group: Group);
        var w = PDoorW - 0.0025; // (its meeting edge 2.5 mm off the middle)
        double x0 = s < 0 ? 0 : -w, x1 = s < 0 ? w : 0;
        double y0 = PDoorY0 + 0.003 - 1.22, y1 = PDoorY1 - 0.003 - 1.22;
        p.Add(Box([x0, y0, -0.045], [x1, y1, 0.045], Armour2, shape: Kw(("finish", "plated steel"))));
        foreach (var y in new[] { -0.6, 0.0, 0.6 })
            p.Add(Box([x0 + 0.03, y - 0.03, -0.061], [x1 - 0.03, y + 0.03, -0.045], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        double m0 = s < 0 ? x1 - 0.1 : x0, m1 = s < 0 ? x1 : x0 + 0.1; // (hazard on the meeting edge, both faces)
        p.Add(Box([m0, y0 + 0.05, -0.061], [m1, y1 - 0.05, -0.045], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([m0, y0 + 0.05, 0.045], [m1, y1 - 0.05, 0.061], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("hang", [0, 0, 0], s < 0 ? "-x" : "+x");
        p.Set(("door", Kw(("kind", "slide"), ("axis", s < 0 ? "-x" : "+x"), ("travel", 0.56), ("seconds", 1.6), ("hang", "hang"))));
        return p.Stats(300, 4000, 50, 4.0);
    }

    /// <summary>The breaching cutter: a ring of thermal lances round the nose door, inside the seal skirt. Its face (its -y) points
    /// out of the nose; in its own frame x is across and z down the door.</summary>
    private static Part PCutter()
    {
        var p = new Part("Limpet Breaching Cutter", Small, PSection, "The Limpet's breaching cutter: a ring of thermal lances round its nose door, inside the seal skirt. Wired to the cutter switch: while the pod's mag locks grip a hull and it is on, it burns through what is in front of the door, part after part, until the way is open.", role: "logic", tool: "wrench", material: "steel", color: Frame, group: Group);
        double ox = 0.72, ix = 0.6, oz0 = -1.11, iz0 = -1.01, iz1 = 0.99, oz1 = 1.11, y0 = -0.29, y1 = 0.0;
        foreach (var (x0, z0, x1, z1) in new[] { (-ox, oz0, ox, iz0), (-ox, iz1, ox, oz1), (-ox, iz0 + 0.003, -ix, iz1 - 0.003), (ix, iz0 + 0.003, ox, iz1 - 0.003) })
            p.Add(Box([x0, y0, z0], [x1, y1, z1], Armour2, shape: Kw(("finish", "plated steel"))));
        // The lances along the ring's inner edge, their glow (lit while it burns), hazard on the ring's face.
        for (var k = 0; k < 6; k++)
        {
            var z = iz0 + 0.12 + k * (iz1 - iz0 - 0.24) / 5;
            foreach (var x in new[] { -0.66, 0.66 })
            {
                p.Add(Box([x - 0.03, y0 - 0.04, z - 0.03], [x + 0.03, y0, z + 0.03], "#3a3d40", detail: true, shape: Kw(("finish", "soot"))));
                p.Add(Box([x - 0.02, y0 - 0.056, z - 0.02], [x + 0.02, y0 - 0.04, z + 0.02], "#ff7020", detail: true, shape: Kw(("glow", true))));
            }
        }
        foreach (var x in new[] { -0.4, 0.0, 0.4 })
            foreach (var z in new[] { -1.06, 1.05 })
            {
                p.Add(Box([x - 0.03, y0 - 0.04, z - 0.025], [x + 0.03, y0, z + 0.025], "#3a3d40", detail: true, shape: Kw(("finish", "soot"))));
                p.Add(Box([x - 0.02, y0 - 0.056, z - 0.017], [x + 0.02, y0 - 0.04, z + 0.017], "#ff7020", detail: true, shape: Kw(("glow", true))));
            }
        foreach (var (x0, x1) in new[] { (-ox + 0.01, -ix - 0.01), (ix + 0.01, ox - 0.01) })
            p.Add(Box([x0, y0 - 0.016, iz0 + 0.3], [x1, y0, iz1 - 0.3], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("top", [0, 0, -PCutHang], "+y"); // (on the ring's top bar: the nose's block over the door)
        p.Set(("logic", Kw(("kind", "lamp"))), ("breach", Kw(("kind", "cutter"), ("reach", 2.0), ("rate", 900))), ("power", 2500),
            ("config", Py.L(Kw(("key", "colour"), ("type", "colour"), ("label", "Colour"), ("group", "Light"), ("default", "#ff6a20"), ("tooltip", "The colour its lances glow while it burns.")))));
        return p.Stats(320, 3500, 35, 6.0);
    }

    /// <summary>A mag lock on the nose's skirt: a magnetic pad (its -y, out of the nose) that grips the hull the pod puts its nose on.</summary>
    private static Part PMagLock()
    {
        var p = new Part("Limpet Mag Lock", Small, PSection, "A mag lock on the Limpet's nose: a magnetic pad on a short ram. Wired to the lock switch: while it is on and a hull is within half a metre of its pad, the pod grips that hull and rides on it.", role: "logic", tool: "wrench", material: "steel", color: Frame, group: Group);
        p.Add(Box([-0.16, -0.13, -0.16], [0.16, 0, 0.16], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.18, -0.155, -0.18], [0.18, -0.13, 0.18], "#202326", detail: true, shape: Kw(("finish", "rubber"))));
        p.Add(Box([-0.165, -0.1, -0.165], [0.165, -0.06, 0.165], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.05, -0.05, 0.16], [0.05, -0.02, 0.176], "#ff8030", detail: true, shape: Kw(("glow", true))));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("logic", Kw(("kind", "lamp"))), ("breach", Kw(("kind", "grapple"), ("reach", 0.6))), ("power", 300),
            ("config", Py.L(Kw(("key", "colour"), ("type", "colour"), ("label", "Colour"), ("group", "Light"), ("default", "#ff9040"), ("tooltip", "The colour its lamp glows while it grips.")))));
        return p.Stats(90, 1500, 25, 3.0);
    }

    /// <summary>The ladder from the Limpet's floor up through its receiver and the Zero Day's docking hatch into its lower deck: its
    /// rails stop under the roof, so nothing of it stands out of the pod.</summary>
    private static Part PLadder()
    {
        var rise = LimpetLadderRise;
        var top = PCeil - PFt - 0.09; // (under the docking hatch hung on the ceiling)
        var p = new Part("Limpet Ladder", Small, PSection, "The ladder at the Limpet's aft wall, up through its receiver and, docked, on through the Zero Day's docking hatch into its lower deck: F climbs it.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        p.Add(Box([-0.3, 0, -0.19], [0.3, top, -0.005], hidden: true));
        foreach (var x in new[] { -0.25, 0.25 })
        {
            p.Add(Box([x - 0.03, 0.0, -0.18], [x + 0.03, top, -0.12], "#9aa0a4", detail: true, shape: Kw(("finish", "painted steel"))));
            for (var y = 0.5; y < top - 0.2; y += 1.0)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.01], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        for (var y = 0.3; y < top - 0.05; y += 0.3)
            p.Add(Box([-0.22, y - 0.02, -0.165], [0.22, y + 0.02, -0.135], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, -0.095], "-y");
        p.Set(("ladder", Py.Round(rise, 3)));
        return p.Stats(50 + rise * 12, 400, 6, 3.0);
    }

    // ---- assembly

    private static (List<Part> Pieces, List<string> Order) LimpetStructure()
    {
        List<Part> pieces = [PFloor(), PSide(-1), PSide(1), PRoof(), PNose(), PStern()];
        Connect(pieces);
        return (pieces, PlaceOrder(pieces, "Limpet Floor"));
    }

    private static List<(Part Part, string Folder)> LimpetParts(List<Part> pieces) =>
    [
        .. pieces.Select(x => (x, POut)),
        (PDoorLeaf(-1), POut), (PDoorLeaf(1), POut), (PCutter(), POut), (PMagLock(), POut), (PLadder(), POut),
    ];

    private static PyDict LimpetBlueprint(Dictionary<string, PyDict> defs, List<string> order)
    {
        var bp = new BuildingParts.Blueprint(LimpetName, "The Zero Day's boarding pod: eight boarders and no pilot, flown from the ship's flight controller on RCS alone. It puts its nose on a ship, its mag locks grip, and it seals on a door or burns through the hull with the cutter round its nose door; then the door parts. Starboard switches by the door: the mag locks (top), the cutter, the door.", defs, maxSteps: 120);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var st = new Dictionary<string, int>();
        foreach (var name in order)
            st[name] = st.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        int floor = st["Limpet Floor"], port = st["Limpet Side Port"], stbd = st["Limpet Side Starboard"], nose = st["Limpet Nose"];
        // RCS only: a quad on each outrigger (out, up, down and fore or aft).
        foreach (var side in new[] { port, stbd })
            foreach (var tag in new[] { "F", "B" })
                foreach (var r in new[] { "rcsOut", "rcsUp", "rcsDn", "rcsEnd" })
                    bp.Hang("RCS Thruster", side, r + tag, "base");
        // The nose: the cutter ring and the four mag locks face out of it (their -y forward), the door's two leaves in the opening.
        var forward = BuildingParts.Turn("+x", "+z");
        var cutter = bp.HangTurned("Limpet Breaching Cutter", nose, "cutter", "top", forward);
        var locks = new[] { "lockTL", "lockTR", "lockBL", "lockBR" }.Select(n => bp.HangTurned("Limpet Mag Lock", nose, n, "top", forward)).ToArray();
        var leaves = new[] { ("Limpet Door Leaf L", "doorL"), ("Limpet Door Leaf R", "doorR") }.Select(x => bp.Hang(x.Item1, nose, x.Item2, "hang")).ToArray();
        // The switches by the door, starboard: the locks (top), the cutter, the door.
        var lockSwitch = bp.Hang("Bakelite Switch", stbd, "switchA", "back");
        foreach (var l in locks)
            bp.Wires.Add((lockSwitch, l));
        bp.Wires.Add((bp.Hang("Bakelite Switch", stbd, "switchB", "back"), cutter));
        var doorSwitch = bp.Hang("Bakelite Switch", stbd, "switchC", "back");
        foreach (var l in leaves)
            bp.Wires.Add((doorSwitch, l));
        // The eight seats, backs to the walls, facing the aisle (a couch faces its -z: RY270 turns that to +x, RY90 to -x).
        for (var i = 0; i < PSeatZ.Length; i++)
        {
            Paint(bp, bp.HangTurned("Flight Couch", floor, $"seatL{i}", "bottom", BuildingParts.RY270), "#2c3e58", "fabric");
            Paint(bp, bp.HangTurned("Flight Couch", floor, $"seatR{i}", "bottom", BuildingParts.RY90), "#2c3e58", "fabric");
        }
        // The flight computer (no pilot: the ship that carries it flies it), the batteries, the ladder up to the receiver.
        var fc = bp.Hang("Flight Stabilizer", floor, "computer", "-y");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight();
        foreach (var n in new[] { "battery0", "battery1" })
            bp.Hang("Battery", floor, n, "-y");
        bp.Hang("Limpet Ladder", floor, "ladder", "bottom");
        // The Docking Hatch under the receiver, upside down (it slides forward, its shoe aft): it opens with the keel's while the clamp holds the pod.
        bp.HangTurned("Docking Hatch", st["Limpet Roof"], "hatch", "base", BuildingParts.Turn("+x", "-y"));
        // Lights on the lamp switch by the door (on from the start).
        var lightSwitch = bp.Hang("Bakelite Switch", port, "switchA", "back");
        bp.Steps[lightSwitch]["config"] = new PyDict { ["startOn"] = true };
        foreach (var side in new[] { port, stbd })
            foreach (var l in new[] { "lampF", "lampB" })
            {
                var lamp = bp.Hang("Bulkhead Light", side, l, "back");
                bp.Steps[lamp]["config"] = new PyDict { ["colour"] = l == "lampF" ? "#ffb070" : "#e8f4ff" };
                bp.Wires.Add((lightSwitch, lamp));
            }
        return bp.Json();
    }
}
