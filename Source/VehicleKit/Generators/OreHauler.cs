using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// The ore hauler of the factory kit's ore loop (docs/sandbox/design/factory-generator.md, phase 1): a heavy chassis with a side-tipping bed. The bed is a
/// Tipper Body (a scoop whose mouth is up, ScoopParts) on a Motor Hinge along the frame's left edge; a TIP switch behind the cab swings it out past its
/// spill angle, and the bulk pours over the left rim into whatever store is under it (ConstructScoops' tipping). Driving east along a factory's road, left is the yard side: it stops beside a refinery's tipping hopper and tips into it,
/// and it loads under an extraction post's gated hopper.
/// </summary>
public sealed class OreHauler(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Ore Hauler";
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    /// <summary>How far the hinge swings the bed: 20 degrees past the Tipper Body's 55-degree spill, where it runs.</summary>
    public const float TipDegrees = 75;

    public override void Build(Vehicle v)
    {
        v.Description = "An ore hauler: a side-tipping bed on a Motor Hinge along the left rail. Load it under a hopper, drive to the tipping bay, stop with the bay on your left and flip the TIP switch behind the cab.";
        var d = v.Dice.Fork("shape");
        Chassis(v, 4.75f, 2.0f, "leaf", "hydraulic", "dually", "six");
        // The bed's middle along the frame, two metres of it, behind the cab; a sill under each end and one under the hinge.
        // (the hinge's sill carries wings out to the wheels' line: never over an axle's springs)
        var want = MathF.Min(v.TailZ - 2.5f, 1.75f + (float)d.Between(0, 0.5));
        var mid = new[] { 0f, -0.25f, 0.25f, -0.5f, 0.5f, -0.75f, 0.75f, -1f, 1f }.Select(o => v.RailZ(want + o)).First(z => MathF.Abs(z - v.RearAxleZ) > 0.7f && MathF.Abs(z - (v.RearAxleZ + 1.25f)) > 0.7f);
        v.Sill(v.RailZ(mid - 0.75f));
        v.Sill(v.RailZ(mid), wing: 0.75f);
        v.Sill(v.RailZ(mid + 0.75f));
        const float side = -1f; // (left: the hinge on the left rail's wing, the bed swinging out over it)
        var top = Vehicle.SillTop(1);
        // Motor Hinge 3x3 Medium: own x (base to leaf) up, its axis (own y) along the frame.
        var hingeAt = new Vector3(side * 0.875f, top + 0.375f, mid);
        var hinge = v.Put("Motor Hinge 3x3 Medium", hingeAt, new Orient(Vector3.UnitX, Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ), 0.2f, null, "base");
        // The Tipper Body (a scoop with its mouth up) on the hinge's leaf by its hingeL socket: centred on the frame, the pivot under its left wall.
        var bed = v.Put("Tipper Body", new Vector3(0, top + 0.75f + 0.3f, mid), Orient.Forward, 0.2f, hinge, "hingeL");
        v.Config(hinge, ("min", -(double)TipDegrees), ("max", 0.0));
        // The TIP switch, wired to the hinge: on, the bed swings out to the left.
        var tip = v.Put("Switch", new Vector3(-side * 1.225f, Vehicle.Half * 2, mid), Orient.Any, 0.2f); // (on the end of the hinge sill's right wing: reached from the ground)
        v.Wire(tip, hinge);
        v.PaintAll([bed], "body", d.Fork("paint"));
        v.PaintAll([hinge], "frame", d.Fork("paint"));
        v.Paint(tip, "#d02a1e");
        // The fuel drum on a sill behind the bed, clear of its swing (the first row behind it that takes a sill).
        var tail = 0f;
        foreach (var o in new[] { 1.625f, 1.875f, 2.125f })
        {
            tail = v.RailZ(mid + o);
            try
            {
                v.Sill(tail);
                break;
            }
            catch (VehicleKitException) when (o < 2f)
            {
            }
        }
        v.FuelUnderFloor(v.DrumOn(new Vector3(0, Vehicle.SillTop(1), tail)), 1);
        v.Hood();
        v.Trim("hoodsides");
    }
}
