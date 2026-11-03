namespace CallOfSlop;

/// <summary>Why a production machine is or isn't making a batch (<see cref="ConstructRefining.StateOf"/>): worked out from its store, so a client's look line says the same as the server.</summary>
public enum RefineState : byte
{
    Empty, // nothing in its input
    NoRecipe, // its input holds what no recipe takes (the table changed)
    TierLow, // its input holds what asks for a higher tier of machine (the table changed)
    Short, // not enough for a batch
    OutputMixed, // its output holds another material
    OutputFull,
    Ready,
}

/// <summary>
/// Refining (decision 14; docs/sandbox/CONSTRUCTS.md "Refining"): a production machine (Smelter, Sawmill; any part with
/// <c>storage.refine</c>) has an input bin and an output bin. Chutes and F put material into the input and take it from the
/// output; what is in the input picks the recipe (Constructs/Refining.json). The input is used when a batch is done, so a
/// batch in hand loses nothing if the power goes or the machine breaks. It needs the electrical system in survival, like the
/// Recycler (<see cref="ConstructRecycler.Powered"/>), and draws power only while it works; in creative (infinite parts on)
/// it refines what it holds in one step, unpowered. A wire into Run holds it off (<see cref="ConstructGatherer.HeldOff"/>).
/// Server, at 10 Hz, looking only at constructs that have a machine; nothing allocates.
/// </summary>
public static class ConstructRefining
{
    public const int Every = 6; // ticks between steps (10 Hz)
    private const int Milli = 1000; // a store's JobTicks counts thousandths of a tick here, so a machine's speed adds up exactly
    public const int BaseWork = Every * Milli; // a step's work at speed 1
    private const int RefusalSteps = 40; // how long the look line remembers a refused chute (a two-way junction's branch tries every other second)

    public static void Step(ConstructWorld world)
    {
        if (++world.RefineTicks < Every)
            return;
        world.RefineTicks = 0;
        var table = RefiningTable.Current;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasRefiner)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsRefiner && world.StoreOf(c, i) is { } st)
                    Work(world, table, c, i, st);
        }
    }

    private static void Work(ConstructWorld world, RefiningTable table, Construct c, int part, CStore st)
    {
        if (st.Refused != 0 && ++st.RefusedAge >= RefusalSteps)
        {
            st.Refused = 0;
            st.RefusedAge = 0;
            st.Version++;
            world.StoreDirty.Add(st.Handle);
        }
        var d = c.Parts[part].D;
        var held = ConstructGatherer.HeldOff(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        var state = StateOf(table, d, st, out var recipe);
        if (state != RefineState.Ready)
            st.JobTicks = 0;
        if (held || state != RefineState.Ready || !ConstructRecycler.Powered(world, c, part))
        {
            ConstructGatherer.SetBusy(world, st, false);
            return; // (a batch in hand waits, and it draws nothing meanwhile)
        }
        if (world.CreativeRecycling)
        {
            ConstructGatherer.SetBusy(world, st, false);
            Make(world, c, part, st, recipe!, Math.Min(st.InAmount / recipe!.UnitsIn, (d.BulkCapacity - st.Amount) / recipe.UnitsOut));
            return;
        }
        ConstructGatherer.SetBusy(world, st, true);
        st.JobTicks += d.RefineWork;
        var done = st.JobTicks / (recipe!.Ticks * Milli); // (a fast machine finishes several batches in a step)
        if (done == 0)
            return;
        st.JobTicks -= done * recipe.Ticks * Milli;
        var fits = Math.Min(st.InAmount / recipe.UnitsIn, (d.BulkCapacity - st.Amount) / recipe.UnitsOut);
        if (done > fits)
            st.JobTicks = 0; // (nothing carries over a batch it couldn't make)
        Make(world, c, part, st, recipe, Math.Min(done, fits));
    }

    private static void Make(ConstructWorld world, Construct c, int part, CStore st, RefineRecipe recipe, int batches)
    {
        world.UseInput(st, batches * recipe.UnitsIn);
        world.Deposit(c, part, batches * recipe.UnitsOut, recipe.Output);
    }

    /// <summary>Where a machine stands with what it holds, and the recipe its input picks.</summary>
    public static RefineState StateOf(RefiningTable table, ConstructPartDef d, CStore st, out RefineRecipe? recipe)
    {
        recipe = table.Find(d.RefineMachine, st.InMaterial);
        if (st.InAmount <= 0)
            return RefineState.Empty;
        if (recipe == null)
            return RefineState.NoRecipe;
        if (recipe.MinTier > d.Tier)
            return RefineState.TierLow;
        if (st.InAmount < recipe.UnitsIn)
            return RefineState.Short;
        if (st.Amount > 0 && st.Material != recipe.Output)
            return RefineState.OutputMixed;
        return d.BulkCapacity - st.Amount < recipe.UnitsOut ? RefineState.OutputFull : RefineState.Ready;
    }

    /// <summary>The material a player would tip into this machine's input: what it holds already, else the first one they carry that it takes (0: none, or it is full).</summary>
    public static byte TipMaterial(SandboxInventory inv, ConstructPartDef d, CStore st)
    {
        if (st.InAmount >= d.InCapacity)
            return 0;
        if (st.InAmount > 0) // (Feed's rule keeps a material the machine's tier can't work out of the input)
            return inv.Materials[st.InMaterial] > 0 ? st.InMaterial : (byte)0;
        for (byte m = 1; m < SandboxInventory.MaxMaterials; m++)
            if (inv.Materials[m] > 0 && RefiningTable.Current.Find(d.RefineMachine, m) is { } r && r.MinTier <= d.Tier)
                return m;
        return 0;
    }

    /// <summary>What the machine is doing, or why not. Read by the look line on either side.</summary>
    public static string Status(ConstructPartDef d, CStore st)
    {
        var names = RecyclingTable.Current;
        var table = RefiningTable.Current;
        var state = StateOf(table, d, st, out var r);
        var doing = st.Held ? Program.HeldLook
            : st.Busy ? $" - REFINING {r?.InputName} to {r?.OutputName}"
            : state switch
            {
                RefineState.Empty => $" - takes {table.TakesLine(d.RefineMachine, d.Tier)}",
                RefineState.NoRecipe => $" - blocked: it can't refine {names.NameOf(st.InMaterial)}",
                RefineState.TierLow => $" - blocked: {r!.InputName} needs a tier {r.MinTier} {d.RefineMachine}, this is tier {d.Tier}",
                RefineState.Short => $" - needs {r!.UnitsIn} {r.InputName} a batch",
                RefineState.OutputMixed => $" - blocked: its output holds {names.NameOf(st.Material)}, empty it to make {r!.OutputName}",
                RefineState.OutputFull => " - blocked: its output is full",
                _ => " - no power: it needs a battery with charge",
            };
        if (st.Refused == 0)
            return doing;
        var refused = table.Find(d.RefineMachine, st.Refused);
        var why = refused == null ? $"it takes {table.Takes(d.RefineMachine, d.Tier)}"
            : refused.MinTier > d.Tier ? $"it needs a tier {refused.MinTier} {d.RefineMachine}, this is tier {d.Tier}"
            : $"its input holds {names.NameOf(st.InMaterial)}";
        return $"{doing} - chute refused: {names.NameOf(st.Refused)} stays in the source, {why}";
    }

    /// <summary>The whole look line: both bins, what it is doing, what F would do.</summary>
    public static string Look(ConstructPartDef d, CStore st, bool near, SandboxInventory inv)
    {
        var names = RecyclingTable.Current;
        var tip = TipMaterial(inv, d, st);
        var act = !near ? ""
            : tip != 0 ? $" - F: tip in your {Math.Min((int)inv.Materials[tip], d.InCapacity - st.InAmount)} {names.NameOf(tip)}"
            : st.Amount > 0 ? $" - F: take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {names.NameOf(st.Material)}"
            : "";
        var input = st.InAmount > 0 ? names.NameOf(st.InMaterial) : "empty";
        var output = st.Amount > 0 ? names.NameOf(st.Material) : "empty";
        return $"{d.Name}: in {st.InAmount}/{d.InCapacity} {input}, out {st.Amount}/{d.BulkCapacity} {output}{Status(d, st)}{act}";
    }
}
