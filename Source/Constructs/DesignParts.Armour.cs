using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Stand-off armour plates in the game (DesignArmour, Karl 2026-11-27): the plate and its rams' rods (its leaf) ride a slow, strong piston
/// out of the rams' barrels (the sockets, which stay on the hull). F on the plate (<see cref="ArmourUse"/>) has the rams push it out by their
/// short stroke over <see cref="DesignArmour.RamSeconds"/>; F again once it is out lets it go: its arms come out of their sockets and it
/// becomes a body of its own (ConstructWorld.DetachPart), moving as the ship moved, so it drifts on beside a ship in space and falls in gravity.
/// </summary>
public static partial class DesignParts
{
    /// <summary>The plate's element and its piston (or the plate welded on, when the body's joints are all used).</summary>
    private static void ArmourLeaf(in PieceAt a, DesignMesh m)
    {
        var (p, e, els) = (a.P, a.E, a.Els);
        if (m.Leaves?.Find(l => l.Leaf == DesignArmour.PlateLeaf) is not { } l || l.To <= l.From)
            return;
        var plate = Leaf($"{e.Id}.plate", p.Kind, m, l.From, l.To, a.X, a.Y, a.Z);
        (plate.Fn, plate.Door, plate.Kind) = ("", null, "armour");
        plate.Kg = MathF.Max(50f, plate.Area * PieceKgPerM2);
        plate.Role = p.ArmButton > 0 ? p.ArmButton.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""; // (the console button that works it)
        var joints = 0;
        foreach (var x in els)
            if (x.Joint != null)
                joints++;
        if (joints + 1 > Construct.MaxJoints)
        {
            Console.WriteLine($"[design] the {p.Kind} {e.Id} has no rams that move: the body's {Construct.MaxJoints} joints are all used (F lets it go at once)");
            els.Add(plate);
            Weld(els, a.At, els.Count - 1);
            return;
        }
        var mid = (plate.BoxMin + plate.BoxMax) * 0.5f;
        var travel = (float)DesignArmour.RamTravel;
        var ram = new Element
        {
            Id = $"{e.Id}.ram", Kind = "armour", Name = "Armour plate rams", Kg = 40f, NoCollider = true, BoxMin = mid - new Vector3(0.05f), BoxMax = mid + new Vector3(0.05f),
            // Strong enough to push the plate out against a full g many times over, at a slow, heavy pace.
            Joint = new JointSpec { Type = JointType.Piston, Axis = NearestDir(a.Y), Min = 0, Max = travel, Speed = travel / (float)DesignArmour.RamSeconds, Strength = MathF.Max(LiftStrength, plate.Kg * 9.81f * 4f), Pivot = mid },
        };
        els.Add(ram);
        Weld(els, a.At, els.Count - 1);
        plate.Seg = (byte)(joints + 1);
        els.Add(plate);
        Weld(els, els.Count - 2, els.Count - 1);
    }

    /// <summary>A utilities console's buttons (DesignFurnishings.UtilitiesConsole): each drawn button its own small element, its number its Role.</summary>
    private static void ConsoleButtons(in PieceAt a, DesignMesh m)
    {
        var (e, els) = (a.E, a.Els);
        for (var k = 0; k < DesignArmour.ConsoleButtons; k++)
        {
            if (m.Leaves?.Find(l => l.Leaf == DesignArmour.ButtonLeaf + k) is not { } l || l.To <= l.From)
                continue;
            var b = Leaf($"{e.Id}.btn{k + 1}", $"Utilities console button {k + 1}", m, l.From, l.To, a.X, a.Y, a.Z);
            (b.Fn, b.Door, b.Kind, b.Role, b.Kg) = ("", null, "armourbtn", (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 1f);
            els.Add(b);
            Weld(els, a.At, els.Count - 1);
        }
    }

    /// <summary>Whether a design part is a utilities console's button.</summary>
    public static bool IsArmourButton(ConstructPartDef d) => ElementOf(d) is { Kind: "armourbtn" };

    /// <summary>
    /// F on a utilities console's button: every plate aboard set to its number is worked together. While any is still in, those that are in
    /// have their rams push them out; once all are out, they are let go. Null when something moved, a reason when not. Not per tick.
    /// </summary>
    public static string? ArmourButtonUse(ConstructWorld world, Construct c, int part)
    {
        if (ElementOf(c.DefOf(part)) is not { } btn || btn.Role.Length == 0)
            return "the button isn't wired to anything";
        var plates = new List<int>();
        for (var i = 0; i < c.PartCount; i++)
            if (ElementOf(c.DefOf(i)) is { Kind: "armour" } pe && pe.Id.EndsWith(".plate", StringComparison.Ordinal) && pe.Role == btn.Role)
                plates.Add(i);
        if (plates.Count == 0)
            return $"no armour plate is set to button {btn.Role}";
        bool In(int i) { var s = c.Parts[i].Seg; return s > 0 && s <= c.JointCount && c.Joints[s - 1].Angle < c.Joints[s - 1].Max - 0.01f; }
        if (plates.Any(In))
        {
            var pushed = false;
            foreach (var i in plates)
                if (In(i) && !c.Joints[c.Parts[i].Seg - 1].Open)
                    pushed |= ArmourUse(world, c, i) == null;
            return pushed ? null : "its plates' rams are still pushing them out";
        }
        // All out: let them go, the last part first, so letting one go doesn't move the others' places in the body.
        string? why = null;
        for (var q = plates.Count - 1; q >= 0; q--)
            why = ArmourUse(world, c, plates[q]) ?? why;
        return why;
    }

    /// <summary>Whether a design part is a stand-off armour plate (its plate, which F works).</summary>
    public static bool IsArmour(ConstructPartDef d) => ElementOf(d) is { Kind: "armour" } e && e.Id.EndsWith(".plate", StringComparison.Ordinal);

    /// <summary>
    /// F on an armour plate (ConstructEdits.Use): its rams push it out slowly by their stroke; once out, F lets it go (its arms leave their
    /// sockets and it drifts or falls as a body of its own). Null when it moved or went, a reason when not. Not per tick.
    /// </summary>
    public static string? ArmourUse(ConstructWorld world, Construct c, int part)
    {
        var seg = c.Parts[part].Seg;
        if (seg == 0 || seg > c.JointCount)
            return world.DetachPart(c, part); // (no rams that move: let go at once)
        ref var jt = ref c.Joints[seg - 1];
        if (jt.Locked || jt.Wired)
            return "its wires run its rams";
        if (!jt.Open)
        {
            jt.Open = true;
            c.Wake();
            Console.WriteLine($"[design] construct {c.Id}: an armour plate's rams push it out ({jt.Max:0.00} m over {DesignArmour.RamSeconds:0} s)");
            return null;
        }
        if (jt.Angle < jt.Max - 0.01f)
            return "its rams are still pushing it out";
        jt.Open = false; // (the rams' sockets settle back once it is gone)
        return world.DetachPart(c, part);
    }
}
