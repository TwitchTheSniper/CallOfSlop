using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Gathering modules (decision 14; docs/sandbox/CONSTRUCTS.md "Gathering"): a Saw Module cuts the trees of a forest within
/// its work box, and a Drill Module whose bit reaches the ground taps the underground deposit below it and digs a hole
/// under its bit (<see cref="DrillShaft"/>). What they gather lands in the part's own bin, and a chute carries it on. They need the electrical system in survival, like the Recycler
/// (<see cref="ConstructRecycler.Powered"/>), and draw power only while they work. Server, at 10 Hz; nothing allocates in
/// the step except a part-cut tree's first entry.
/// </summary>
public static class ConstructGatherer
{
    public const int Every = 6; // ticks between steps (10 Hz)
    public const float GroundSlack = 0.25f; // how far above the ground a bit still counts as touching it
    private const int Milli = 1000; // a store's JobTicks counts thousandths of a unit here
    private const int RegrowPerStep = 32; // most trees grown back in a step (the rest follow in the next)
    private const float RegrowRetrySeconds = 10; // how soon a tree blocked by what stands on its stump tries again
    private static readonly List<Construct> _near = new(32);

    /// <summary>The work box's centre, lowest point and flat reach in the world.</summary>
    public static void WorkBox(Construct c, int part, out Vector3 centre, out float bottom, out float reach)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        c.WorldFrame(p.Seg, out var q, out var t);
        var a = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, d.WorkMin), q);
        var b = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, d.WorkMax), q);
        centre = (a + b) * 0.5f;
        bottom = MathF.Min(a.Y, b.Y);
        var half = Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z)) * 0.5f;
        reach = half;
    }

    public static void Step(ConstructWorld world)
    {
        if (world.Resources is not { } field || ++world.GatherTicks < Every)
            return;
        world.GatherTicks = 0;
        Regrow(world, field);
        world.ScoopsThisStep = 0;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasGatherer)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsGatherer && world.StoreOf(c, i) is { } st)
                    SetBusy(world, st, ConstructRecycler.Powered(world, c, i) && Work(world, field, c, i, st));
        }
    }

    /// <summary>Felled trees whose time has come stand again, unless something stands on the stump: then it waits.</summary>
    private static void Regrow(ConstructWorld world, ResourceField field)
    {
        field.RegrowTick(Every);
        var done = 0;
        while (done++ < RegrowPerStep && field.NextRegrown(out var tree))
        {
            ResourceField.UnpackKey(tree, out var ix, out var iz);
            if (!field.TreeAt(ix, iz, out var x, out var z, out _))
                continue; // (the map's own boxes changed it: nothing to grow)
            if (SpotTaken(world, field, tree, x, z))
            {
                field.PutOff(tree, RegrowRetrySeconds);
                continue;
            }
            field.Grow(tree);
            world.GrownTrees.Add(tree);
        }
    }

    /// <summary>Would a tree growing here stand in a player or a construct?</summary>
    private static bool SpotTaken(ConstructWorld world, ResourceField field, uint tree, float x, float z)
    {
        var ground = field.Ground(x, z);
        var at = new Vector3(x, ground, z);
        var trunk = new Trunk(tree, x, z, ResourceField.MaxTrunkRadius, ResourceField.MaxTreeHeight);
        for (var k = 0; k < world.PlayerSpotCount; k++)
        {
            var d = world.PlayerSpots[k] - at;
            if (d.X * d.X + d.Z * d.Z < 4f && MathF.Abs(d.Y) < ResourceField.MaxTreeHeight)
                return true;
        }
        var near = world.Near(at + new Vector3(0, ResourceField.MaxTreeHeight / 2, 0), ResourceField.MaxTreeHeight / 2 + 1, _near);
        for (var k = 0; k < near.Count; k++)
            if (ConstructPhysics.TouchesTrunk(near[k], trunk, ground))
                return true;
        return false;
    }

    private static void SetBusy(ConstructWorld world, CStore st, bool busy)
    {
        if (st.Busy == busy)
            return;
        st.Busy = busy;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    private static bool Work(ConstructWorld world, ResourceField field, Construct c, int part, CStore st)
    {
        var d = c.Parts[part].D;
        if (st.Amount >= d.BulkCapacity)
            return false;
        WorkBox(c, part, out var at, out var bottom, out var reach);
        var ground = field.Ground(at.X, at.Z);
        var units = 0;
        if (d.GatherSaw)
        {
            if (MathF.Abs(bottom - ground) > ResourceField.SawHeight || !field.NearestTree(at, reach, out var tree))
                return false;
            var wood = RecyclingTable.Current.IdOf(field.WoodMaterial);
            if (st.Amount > 0 && st.Material != wood)
                return false;
            units = Accrue(st, d.GatherRate);
            if (units == 0)
                return true;
            var got = field.Cut(tree, units, out var felled);
            if (felled)
                world.FelledTrees.Add(tree);
            world.Deposit(c, part, got, wood);
            return true;
        }
        if (!DrillShaft.Touches(field, at, bottom) || field.KindAt(at.X, at.Z, false) == null)
            return false;
        var want = RecyclingTable.Current.IdOf(field.KindAt(at.X, at.Z, false)!.Material);
        if (st.Amount > 0 && st.Material != want)
            return false;
        units = Accrue(st, d.GatherRate);
        if (units == 0)
            return true;
        var tapped = field.Tap(at.X, at.Z, units, out var material);
        if (tapped == 0)
            return false;
        world.Deposit(c, part, tapped, material);
        DrillShaft.Dig(world, field, c, st, at, tapped);
        return true;
    }

    /// <summary>A step's share of the rate (units a second), kept in thousandths so a slow rate still adds up.</summary>
    private static int Accrue(CStore st, float rate)
    {
        st.JobTicks += (int)(rate * Every / World.TickRate * Milli);
        var units = st.JobTicks / Milli;
        st.JobTicks -= units * Milli;
        return units;
    }
}
