using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Recycler (board item sh01, decision 31; docs/sandbox/CONSTRUCTS.md "Salvage haul"): a welder part with an intake
/// box and a bin for its output. A wrecked part (a single-part body whose part is Broken: what taking a wreck off leaves,
/// or debris that broke off) left in the intake, once the physics gun lets go of it, is taken in and processed over time
/// (<see cref="RecyclingTable.SecondsFor"/>), then the part's material and mass (<see cref="RecyclingTable.YieldOf"/>) land
/// in its bin; a chute from the bin carries them on to a bulk store. It needs the electrical system in survival (a
/// working battery's links reach it, the battery has charge) and draws power only while it works; in creative (infinite
/// parts on) it works instantly and needs none. Intact parts are never taken in. Server, every tick; nothing allocates
/// except the log line when a part goes in or comes out.
/// </summary>
public static class ConstructRecycler
{
    public const int ScanEvery = 6; // ticks between looks at the intakes (10 Hz)

    /// <summary>Is the recycler part working (not wrecked, and powered unless creative)?</summary>
    public static bool Powered(ConstructWorld world, Construct c, int part) =>
        !c.Parts[part].Broken && (world.CreativeRecycling || c.HasBattery && c.PowerOn && c.Parts[part].Powered);

    /// <summary>Can this body be fed to a recycler (every part wrecked, one part, free)?</summary>
    public static bool Feedable(Construct b) =>
        b.PartCount == 1 && b.Parts[0].Broken && b.GrabbedBy == 255 && b.CarriedBy == 0 && b.Driver == 255 && !b.Anchored && b.CargoCount == 0;

    /// <summary>Is a world point inside the intake box of recycler part <paramref name="part"/>?</summary>
    public static bool InIntake(Construct c, int part, Vector3 world)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        var local = Vector3.Transform(world - t, Quaternion.Conjugate(q));
        // (the box in the part's frame, turned by the part's Rot24)
        var a = p.Pos + Rot24.Apply(p.Rot, p.D.IntakeMin);
        var b = p.Pos + Rot24.Apply(p.Rot, p.D.IntakeMax);
        var lo = Vector3.Min(a, b);
        var hi = Vector3.Max(a, b);
        return local.X >= lo.X && local.X <= hi.X && local.Y >= lo.Y && local.Y <= hi.Y && local.Z >= lo.Z && local.Z <= hi.Z;
    }

    public static void Step(ConstructWorld world)
    {
        var scan = ++world.RecyclerTicks >= ScanEvery;
        if (scan)
            world.RecyclerTicks = 0;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasRecycler)
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                if (!c.Parts[i].D.IsRecycler || world.StoreOf(c, i) is not { } st)
                    continue;
                if (st.JobAmount > 0)
                {
                    if (Powered(world, c, i) && st.JobTicks > 0)
                        st.JobTicks--;
                    if (st.JobTicks <= 0 && Powered(world, c, i))
                        Finish(world, c, i, st);
                    continue;
                }
                if (scan && Powered(world, c, i))
                    TakeIn(world, c, i, st);
            }
        }
    }

    private static void Finish(ConstructWorld world, Construct c, int part, CStore st)
    {
        var put = world.Deposit(c, part, st.JobAmount, st.JobMaterial);
        st.JobAmount -= put;
        if (st.JobAmount > 0)
            return; // (the output is full: it waits until a chute or a player empties it)
        st.JobMaterial = 0;
        st.Busy = false;
        world.StoreDirty.Add(st.Handle);
        st.Version++;
        c.Wake();
    }

    private static void TakeIn(ConstructWorld world, Construct c, int part, CStore st)
    {
        var bodies = world.Bodies;
        var table = RecyclingTable.Current;
        var d = c.Parts[part].D;
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var b = bodies[bi];
            if (b == c || !Feedable(b))
                continue;
            if (b.Dirty)
                b.Rebuild();
            if (!InIntake(c, part, b.WorldCom))
                continue;
            var (mat, amount) = table.YieldOf(b.Parts[0]);
            if (mat == 0 || amount <= 0)
            {
                world.Events.Add($"recycler {c.Id}: a wrecked {b.Parts[0].D.Name} has nothing to give back");
                continue;
            }
            if (st.Amount > 0 && st.Material != mat || d.BulkCapacity - st.Amount < amount)
                continue; // (its output holds another material, or hasn't room: it stays in the intake until it does)
            var mass = b.Parts[0].D.Mass;
            var name = b.Parts[0].D.Name;
            world.Remove(b);
            if (world.CreativeRecycling)
            {
                world.Deposit(c, part, amount, mat);
                world.Events.Add($"recycler {c.Id}: {name} -> {amount} {table.NameOf(mat)} (instant)");
            }
            else
            {
                st.JobAmount = amount;
                st.JobMaterial = mat;
                st.JobTicks = Math.Max(1, (int)MathF.Ceiling(table.SecondsFor(mass) * World.TickRate));
                st.Busy = true;
                world.StoreDirty.Add(st.Handle);
                st.Version++;
                world.Events.Add($"recycler {c.Id}: took in {name} ({mass:0} kg): {amount} {table.NameOf(mat)} in {st.JobTicks / (float)World.TickRate:0.0} s");
            }
            c.Wake();
            return; // (one part at a time)
        }
    }
}
