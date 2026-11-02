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
