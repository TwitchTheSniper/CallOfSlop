using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Lifters phase 5 (docs/sandbox/design/arms-and-cranes.md 3.5): a Lay-down Pad takes in a loose piece set down on its deck. A
/// one-part body at rest whose middle is over the pad's hold (put there by hand, the physics gun, or dropped there by a machine's
/// out) goes into the pad's hold as a conveyor's delivery would, about once a second. Server only. The world keeps only the bodies
/// that have a pad (<see cref="ConstructWorld.PadBodies"/>, noted when a body's parts change), never a record per body.
/// </summary>
public static partial class ConstructLifters
{
    public const int PadTakesAStep = 4; // pieces the world's pads take in one look

    /// <summary>A body whose parts changed: on the pads list while it has a Lay-down Pad (server).</summary>
    private static void NotePads(ConstructWorld world, Construct c)
    {
        c.PadStamp = c.RebuildStamp;
        c.PadPartCount = c.PartCount;
        var has = false;
        for (var i = 0; i < c.PartCount && !has; i++)
            has = c.DefOf(i).IsLayDownPad;
        var at = world.PadBodies.IndexOf(c.Id);
        if (has && at < 0)
            world.PadBodies.Add(c.Id);
        else if (!has && at >= 0)
            world.PadBodies.RemoveAt(at);
    }

    /// <summary>Server, once a second after the scan: each pad takes in the loose pieces resting on it. Allocates nothing.</summary>
    private static void PadsTakeIn(ConstructWorld world)
    {
        var taken = 0;
        for (var k = world.PadBodies.Count - 1; k >= 0 && taken < PadTakesAStep; k--)
        {
            if (world.Get(world.PadBodies[k]) is not { } c)
            {
                world.PadBodies.RemoveAt(k);
                continue;
            }
            for (var i = 0; i < c.PartCount && taken < PadTakesAStep; i++)
                if (c.DefOf(i).IsLayDownPad && !c.Parts[i].Broken)
                    taken += PadTakeIn(world, c, i, PadTakesAStep - taken);
        }
    }

    /// <summary>One pad: up to <paramref name="most"/> loose pieces over its hold go in. How many did.</summary>
    private static int PadTakeIn(ConstructWorld world, Construct c, int part, int most)
    {
        if (world.StoreOf(c, part) is not { } st)
            return 0;
        var d = c.DefOf(part);
        Freeform.PartWorld(c, part, out var pp, out var pr);
        var inv = Quaternion.Conjugate(pr);
        var reach = c.Radius + Vector3.Distance(c.Pos, pp) + 2f; // (a cheap first cut: the pad's body's sphere)
        var n = 0;
        var bodies = world.Bodies;
        for (var bi = bodies.Count - 1; bi >= 0 && n < most; bi--)
        {
            var lo = bodies[bi];
            if (lo.PartCount != 1 || lo == c || lo.Anchored || lo.HeldBy != 0 || lo.GrabbedBy != 255 || lo.CarriedBy != 0 || lo.Parts[0].Broken)
                continue;
            if (Vector3.DistanceSquared(lo.Pos, c.Pos) > reach * reach || lo.Vel.LengthSquared() > 0.25f)
                continue;
            var local = Vector3.Transform(lo.WorldCom - pp, inv);
            if (local.X < d.RackHoldMin.X || local.X > d.RackHoldMax.X || local.Z < d.RackHoldMin.Z || local.Z > d.RackHoldMax.Z
                || local.Y < d.RackHoldMin.Y - 0.25f || local.Y > d.RackHoldMax.Y)
                continue;
            var pd = lo.DefOf(0);
            // Not a part that holds anything (its contents would be lost: a rack holds kinds only), nor a damaged one (it would come
            // out whole), nor what the pad or its stock target refuses.
            if (lo.Parts[0].Store != 0 || pd.IsPieceRack || lo.Parts[0].Health < pd.Health * 0.999f)
                continue;
            if (ConstructConveyor.RackCheck(c, part, st, pd) != 0 || !StockTargets.Accepts(world, c, part, st, pd))
                continue;
            world.Remove(lo);
            if (!world.RackPut(c, part, pd))
                continue; // (checked above: not reached)
            if (world.StockTargetN > 0)
                ConstructLogisticsPlan.Arrived(world, c, part, 0, pd); // (a stock target's refill counts it, as a conveyor's delivery does)
            world.CraftChanged(c);
            Console.WriteLine($"[lifter] pad body {c.Id} part {part}: took in a loose {pd.Name}");
            n++;
        }
        return n;
    }
}

public sealed partial class ConstructWorld
{
    public readonly List<ushort> PadBodies = new(8); // server: the bodies that have a Lay-down Pad (ConstructLifterPads.cs)
}
