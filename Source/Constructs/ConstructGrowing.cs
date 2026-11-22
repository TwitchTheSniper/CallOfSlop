namespace CallOfSlop;

/// <summary>Why a grow bed isn't growing (<see cref="CStore.Blocked"/>, on the wire: the look line).</summary>
public enum GrowStall : byte
{
    None, // growing
    NoWater, // not a batch's water in the construct's tanks
    Full, // its bin holds all it can
    NoPower,
}

/// <summary>
/// Hydroponics (docs/sandbox/constructs/production.md "Grow beds"): a storage part with <c>storage.grow</c> (the Cloche's grow beds,
/// racks and seed beds) grows its bin's material, a batch of <see cref="ConstructPartDef.GrowUnits"/> every
/// <see cref="ConstructPartDef.GrowTicks"/>, while it has power and the construct's Fluid Tanks hold water. Each batch drinks
/// <see cref="ConstructPartDef.GrowLitres"/> of water off whatever tanks hold it, as the batch is made (a bed is plumbed into the craft's
/// water, so no pipe is laid to it). A wire into Run holds it off, as for every machine (<see cref="ConstructGatherer.HeldOff"/>); it draws
/// power only while it grows. Chutes and F take its crop out, and nothing goes in. Server, at 10 Hz, looking only at constructs that have
/// a grow bed; nothing allocates.
/// </summary>
public static class ConstructGrowing
{
    public const int Every = 6; // ticks between steps (10 Hz)

    public static void Step(ConstructWorld world)
    {
        if (++world.GrowTicks < Every)
            return;
        world.GrowTicks = 0;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasGrower)
                continue;
            if (c.Dirty)
                c.Rebuild();
            var water = -1f; // (read once a body a step, at its first bed)
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsGrower && world.StoreOf(c, i) is { } st)
                    Work(world, c, i, st, ref water);
        }
    }

    private static void Work(ConstructWorld world, Construct c, int part, CStore st, ref float water)
    {
        var d = c.Parts[part].D;
        var held = ConstructGatherer.HeldOff(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        if (water < 0)
            water = ConstructFluid.Aboard(c, Fluid.Water);
        var stall = ConstructStorage.Capacity(c, part) - st.Amount < d.GrowUnits ? GrowStall.Full
            : water < d.GrowLitres ? GrowStall.NoWater
            : !ConstructRecycler.Powered(world, c, part) ? GrowStall.NoPower
            : GrowStall.None;
        SetStall(world, st, held ? GrowStall.None : stall);
        if (held || stall != GrowStall.None)
        {
            ConstructGatherer.SetBusy(world, st, false);
            return; // (a crop half grown waits: nothing is lost while the water or the power is out)
        }
        ConstructGatherer.SetBusy(world, st, true);
        st.JobTicks += Every;
        if (st.JobTicks < d.GrowTicks)
            return;
        st.JobTicks = 0;
        water -= ConstructFluid.Use(c, Fluid.Water, d.GrowLitres);
        world.DepositGrown(c, part, d.GrowUnits, RecyclingTable.Current.IdOf(d.BulkMaterial));
        ProductionLines.Made(world, c, part, 1);
    }

    private static void SetStall(ConstructWorld world, CStore st, GrowStall stall)
    {
        if (st.Blocked == (byte)stall)
            return;
        st.Blocked = (byte)stall;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    /// <summary>What it is doing, or why not, from what the clients have too (its store's flags and the tanks' contents).</summary>
    public static string Why(Construct c, int part, CStore st)
    {
        var d = c.DefOf(part);
        if (st.Held)
            return Program.HeldLook.TrimStart(' ', '-');
        return (GrowStall)st.Blocked switch
        {
            GrowStall.NoWater => $"no water: fill a Fluid Tank aboard with water ({d.GrowLitres:0.#} l a crop)",
            GrowStall.Full => "full: take its crop (chute or F)",
            GrowStall.NoPower => "no power: needs a charged battery",
            _ => st.Busy ? $"growing {d.BulkMaterial}" : $"waiting to grow {d.BulkMaterial}",
        };
    }

    /// <summary>The bin's look line after its count: "- growing produce, 4 every 2 min, 3.2 l water each; 18400 l water aboard".</summary>
    public static string Look(Construct c, int part, CStore st)
    {
        var d = c.DefOf(part);
        var secs = d.GrowTicks / (float)World.TickRate;
        var every = secs >= 120 ? $"{secs / 60:0.#} min" : $"{secs:0} s";
        return $" - {Why(c, part, st)} ({d.GrowUnits} every {every}, {d.GrowLitres:0.#} l water each); {ConstructFluid.Aboard(c, Fluid.Water):0} l water aboard";
    }
}
