using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Gathering modules (decision 14; docs/sandbox/CONSTRUCTS.md "Gathering"): a Saw Module cuts the trees of a forest within
/// its work box, and a Drill Module whose bit reaches the ground taps the underground deposit below it and digs a hole
/// under its bit (<see cref="DrillShaft"/>). What they gather lands in the part's own bin, and a chute carries it on (a drill's bin is a
/// small buffer: it stops when that is full). They need the electrical system in survival, like the Recycler
/// (<see cref="ConstructRecycler.Powered"/>), and draw power only while they work; a drill with a drive shaft linked runs from
/// the shaft instead (<see cref="ConstructDrillDrive"/>). Server, at 10 Hz; nothing allocates in the step except a part-cut tree's first entry.
/// </summary>
public static class ConstructGatherer
{
    public const int Every = 6; // ticks between steps (10 Hz)
    public const float GroundSlack = 0.25f; // how far above the ground a bit still counts as touching it
    private const int Milli = 1000; // a store's JobTicks counts thousandths of a unit here
    private const int RegrowPerStep = 32; // most trees grown back in a step (the rest follow in the next)
    private const float RegrowRetrySeconds = 10; // how soon a tree blocked by what stands on its stump tries again
    private static readonly List<Construct> _near = new(32);
    public const string RunPort = "Run"; // the data input of a gathering module, a Recycler or a production machine

    /// <summary>
    /// Does a wire hold this module, Recycler or production machine off? Nothing wired: it runs. Wired: only while the signal into Run is on. A
    /// cut cable is still wired, and carries nothing. The part's own input, never whether its construct is a vehicle.
    /// </summary>
    public static bool HeldOff(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        return p.InCount > 0 ? p.In < 0.5f : c.RoutesInto(part, RouteType.Data) > 0;
    }

    /// <summary>Marks a store held off or not (on the wire: the look line says so).</summary>
    public static void SetHeld(ConstructWorld world, CStore st, bool held)
    {
        if (st.Held == held)
            return;
        st.Held = held;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

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
        if (++world.GatherTicks < Every)
            return;
        world.GatherTicks = 0;
        if (world.Resources is { } growing)
            Regrow(world, growing);
        world.ScoopsThisStep = 0;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasGatherer)
                continue;
            if (c.Dirty)
                c.Rebuild();
            var load = 0f;
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsGatherer && world.StoreOf(c, i) is { } st)
                {
                    var held = HeldOff(c, i);
                    SetHeld(world, st, held);
                    var shaft = ConstructDrillDrive.Update(world, c, i, st); // (a drill with a drive shaft linked: the share of its rate; else -1)
                    var can = shaft < 0 ? ConstructRecycler.Powered(world, c, i) : shaft > 0 && !c.Parts[i].Broken;
                    var busy = !held && world.Resources is { } field && can && Work(world, field, c, i, st, shaft < 0 ? 1f : shaft);
                    SetBusy(world, st, busy);
                    if (busy && shaft > 0)
                        load += ConstructDrillDrive.Load(c, i);
                }
            c.ShaftLoad = load;
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

    internal static void SetBusy(ConstructWorld world, CStore st, bool busy)
    {
        if (st.Busy == busy)
            return;
        st.Busy = busy;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    private static bool Work(ConstructWorld world, ResourceField field, Construct c, int part, CStore st, float share)
    {
        var d = c.Parts[part].D;
        if (st.Amount >= d.BulkCapacity)
            return false;
        WorkBox(c, part, out var at, out var bottom, out var reach);
        var ground = field.Ground(at.X, at.Z);
        var units = 0;
        if (d.GatherSaw)
        {
            if (d.Tier < field.WoodMinTier || MathF.Abs(bottom - ground) > ResourceField.SawHeight || !field.NearestTree(at, reach, out var tree))
                return false;
            var wood = RecyclingTable.Current.IdOf(field.WoodMaterial);
            if (st.Amount > 0 && st.Material != wood)
                return false;
            units = Math.Min(Accrue(st, d.GatherRate * share), d.BulkCapacity - st.Amount); // (a fast saw's step never takes more than the bin has room for)
            if (units == 0)
                return true;
            var got = field.Cut(tree, units, out var felled);
            if (felled)
                world.FelledTrees.Add(tree);
            world.Yield(CYieldKind.Gathered, c, wood, world.Deposit(c, part, got, wood));
            return true;
        }
        if (!DrillShaft.Touches(field, at, bottom) || field.TappableAt(at.X, at.Z) is not { } kind || d.Tier < kind.MinTier)
            return false;
        var want = RecyclingTable.Current.IdOf(kind.Material);
        if (st.Amount > 0 && st.Material != want)
            return false;
        units = Math.Min(Accrue(st, d.GatherRate * share), d.BulkCapacity - st.Amount); // (a fast drill's step never takes more than the bin has room for)
        if (units == 0)
            return true;
        if (kind.Deep && !DrillShaft.Bored(world, field, at))
        {
            DrillShaft.Dig(world, field, c, st, at, units); // (the rock over a deep deposit: it gives nothing)
            return true;
        }
        var tapped = field.Tap(at.X, at.Z, units, out var material);
        if (tapped == 0)
            return false;
        world.Yield(CYieldKind.Gathered, c, material, world.Deposit(c, part, tapped, material));
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
