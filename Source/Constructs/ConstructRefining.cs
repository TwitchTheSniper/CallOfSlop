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
            Make(world, c, part, st, recipe!, Batches(d, st, recipe!));
            return;
        }
        ConstructGatherer.SetBusy(world, st, true);
        st.JobTicks += d.RefineWork;
        var done = st.JobTicks / (recipe!.Ticks * Milli); // (a fast machine finishes several batches in a step)
        if (done == 0)
            return;
        st.JobTicks -= done * recipe.Ticks * Milli;
        var fits = Batches(d, st, recipe);
        if (done > fits)
            st.JobTicks = 0; // (nothing carries over a batch it couldn't make)
        Make(world, c, part, st, recipe, Math.Min(done, fits));
    }

    private static void Make(ConstructWorld world, Construct c, int part, CStore st, RefineRecipe recipe, int batches)
    {
        if (recipe.Mix.Length == 0)
            world.UseInput(st, batches * recipe.UnitsIn);
        else
            foreach (var (m, units) in recipe.Mix)
                world.AddInput(st, m, -batches * units);
        world.Yield(CYieldKind.Refined, c, recipe.Output, world.Deposit(c, part, batches * recipe.UnitsOut, recipe.Output));
    }

    /// <summary>How many batches the input holds and the output has room for.</summary>
    private static int Batches(ConstructPartDef d, CStore st, RefineRecipe recipe)
    {
        var n = (d.BulkCapacity - st.Amount) / recipe.UnitsOut;
        if (recipe.Mix.Length == 0)
            return Math.Min(n, st.InAmount / recipe.UnitsIn);
        foreach (var (m, units) in recipe.Mix)
            n = Math.Min(n, st.MixOf(m) / units);
        return n;
    }

    /// <summary>Does a mixed machine take this material, alongside what it holds? Some recipe of its tier must use it and all of what is there. Allocates nothing.</summary>
    public static bool MixTakes(RefiningTable table, ConstructPartDef d, CStore st, byte material)
    {
        foreach (var r in table.MixesOf(d.RefineMachine))
            if (r.MinTier <= d.Tier && r.Uses(material) && UsesAll(r, st))
                return true;
        return false;
    }

    private static bool UsesAll(RefineRecipe r, CStore st)
    {
        for (var k = 0; k < st.MixN; k++)
            if (!r.Uses(st.MixMat![k]))
                return false;
        return true;
    }

    /// <summary>The recipe a mixed input picks: one that uses all of what is there, first one with a whole batch. Null: none does.</summary>
    private static RefineRecipe? MixRecipe(RefiningTable table, ConstructPartDef d, CStore st)
    {
        RefineRecipe? first = null;
        foreach (var r in table.MixesOf(d.RefineMachine))
        {
            if (!UsesAll(r, st))
                continue;
            var whole = true;
            foreach (var (m, units) in r.Mix)
                whole &= st.MixOf(m) >= units;
            if (whole)
                return r;
            first ??= r;
        }
        return first;
    }

    /// <summary>Where a machine stands with what it holds, and the recipe its input picks.</summary>
    public static RefineState StateOf(RefiningTable table, ConstructPartDef d, CStore st, out RefineRecipe? recipe)
    {
        if (d.RefineMix)
        {
            recipe = MixRecipe(table, d, st);
            if (st.MixN == 0)
                return RefineState.Empty;
        }
        else
        {
            recipe = table.Find(d.RefineMachine, st.InMaterial);
            if (st.InAmount <= 0)
                return RefineState.Empty;
        }
        if (recipe == null)
            return RefineState.NoRecipe;
        if (recipe.MinTier > d.Tier)
            return RefineState.TierLow;
        if (Short(st, recipe))
            return RefineState.Short;
        if (st.Amount > 0 && st.Material != recipe.Output)
            return RefineState.OutputMixed;
        return d.BulkCapacity - st.Amount < recipe.UnitsOut ? RefineState.OutputFull : RefineState.Ready;
    }

    private static bool Short(CStore st, RefineRecipe r)
    {
        if (r.Mix.Length == 0)
            return st.InAmount < r.UnitsIn;
        foreach (var (m, units) in r.Mix)
            if (st.MixOf(m) < units)
                return true;
        return false;
    }

    /// <summary>The material a player would tip into this machine's input: what it holds already, else the first one they carry that it takes (0: none, or it is full).</summary>
    public static byte TipMaterial(SandboxInventory inv, ConstructPartDef d, CStore st)
    {
        if (d.RefineMix)
        {
            for (byte m = 1; m < SandboxInventory.MaxMaterials; m++)
                if (inv.Materials[m] > 0 && st.MixOf(m) < d.InCapacity && MixTakes(RefiningTable.Current, d, st, m))
                    return m;
            return 0;
        }
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
            : st.Busy ? $", refining {r?.InputName} to {r?.OutputName}"
            : state switch
            {
                RefineState.Empty => $" - takes {table.TakesLine(d.RefineMachine, d.Tier)}",
                RefineState.NoRecipe => $" - can't refine {names.NameOf(st.InMaterial)}",
                RefineState.TierLow => $" - {r!.InputName} needs a tier {r.MinTier} {d.RefineMachine}",
                RefineState.Short => r!.Mix.Length > 0 ? $" - needs {r.Batch} a batch" : $" - needs {r.UnitsIn} {r.InputName} a batch",
                RefineState.OutputMixed => $" - output holds {names.NameOf(st.Material)}: empty it to make {r!.OutputName}",
                RefineState.OutputFull => " - output full",
                _ => " - no power: needs a charged battery",
            };
        if (st.Refused == 0)
            return doing;
        if (d.RefineMix)
            return $"{doing} - chute refused {names.NameOf(st.Refused)}: {MixRefusal(table, d, st)}";
        var refused = table.Find(d.RefineMachine, st.Refused);
        var why = refused == null ? $"takes {table.Takes(d.RefineMachine, d.Tier)}"
            : refused.MinTier > d.Tier ? $"needs a tier {refused.MinTier} {d.RefineMachine}"
            : $"input holds {names.NameOf(st.InMaterial)}";
        return $"{doing} - chute refused {names.NameOf(st.Refused)}: {why}";
    }

    private static string MixRefusal(RefiningTable table, ConstructPartDef d, CStore st)
    {
        RefineRecipe? higher = null, used = null;
        foreach (var r in table.MixesOf(d.RefineMachine))
            if (r.Uses(st.Refused))
            {
                used ??= r;
                if (r.MinTier > d.Tier && UsesAll(r, st))
                    higher ??= r;
            }
        return higher != null ? $"needs a tier {higher.MinTier} {d.RefineMachine}"
            : used != null ? $"no recipe mixes it with {MixHolds(st)}"
            : $"takes {table.Takes(d.RefineMachine, d.Tier)}";
    }

    /// <summary>"40 metal + 20 stone", "empty".</summary>
    private static string MixHolds(CStore st)
    {
        var names = RecyclingTable.Current;
        if (st.MixN == 0)
            return "empty";
        var s = $"{st.MixAmount![0]} {names.NameOf(st.MixMat![0])}";
        for (var k = 1; k < st.MixN; k++)
            s += $" + {st.MixAmount[k]} {names.NameOf(st.MixMat[k])}";
        return s;
    }

    /// <summary>The whole look line: both bins, what it is doing, what F would do.</summary>
    public static string Look(ConstructPartDef d, CStore st, bool near, SandboxInventory inv)
    {
        var names = RecyclingTable.Current;
        var tip = TipMaterial(inv, d, st);
        var act = !near ? ""
            : tip != 0 ? $" - {SandboxKeys.Use}: tip in {Math.Min((int)inv.Materials[tip], d.InCapacity - (d.RefineMix ? st.MixOf(tip) : st.InAmount))} {names.NameOf(tip)}"
            : st.Amount > 0 ? $" - {SandboxKeys.Use}: take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {names.NameOf(st.Material)}"
            : "";
        var output = st.Amount > 0 ? " " + names.NameOf(st.Material) : "";
        if (d.RefineMix)
            return $"{d.Name}: in {(st.MixN == 0 ? $"0/{d.InCapacity}" : $"{MixHolds(st)} (of {d.InCapacity} each)")}, out {st.Amount}/{d.BulkCapacity}{output}{Status(d, st)}{act}";
        var input = st.InAmount > 0 ? " " + names.NameOf(st.InMaterial) : "";
        return $"{d.Name}: in {st.InAmount}/{d.InCapacity}{input}, out {st.Amount}/{d.BulkCapacity}{output}{Status(d, st)}{act}";
    }
}
