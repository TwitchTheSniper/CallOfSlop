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
/// it refines a whole load as one job at <see cref="CreativeSpeed"/> times the speed, at least 1.5 s, unpowered. A wire into Run holds it off (<see cref="ConstructGatherer.HeldOff"/>).
/// Server, at 10 Hz, looking only at constructs that have a machine; nothing allocates.
/// </summary>
public static class ConstructRefining
{
    public const int Every = 6; // ticks between steps (10 Hz)
    public const int Milli = 1000; // a store's JobTicks counts thousandths of a tick here, so a machine's speed adds up exactly
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
        var size = RefineOf(c, part);
        var state = StateOf(table, d, size, st, out var recipe);
        if (state != RefineState.Ready)
            st.JobTicks = 0;
        if (held || state != RefineState.Ready || !ConstructRecycler.Powered(world, c, part))
        {
            ConstructGatherer.SetBusy(world, st, false);
            return; // (a batch in hand waits, and it draws nothing meanwhile)
        }
        if (world.CreativeRecycling)
        {
            Creative(world, size, c, part, st, recipe!);
            return;
        }
        ConstructGatherer.SetBusy(world, st, true);
        st.JobTicks += size.Work; // (a squares Core's work is its rate's: RefineOf)
        var done = st.JobTicks / (recipe!.Ticks * Milli); // (a fast machine finishes several batches in a step)
        if (done == 0)
            return;
        st.JobTicks -= done * recipe.Ticks * Milli;
        var fits = Batches(size, st, recipe);
        if (done > fits)
            st.JobTicks = 0; // (nothing carries over a batch it couldn't make)
        Make(world, c, part, st, recipe, Math.Min(done, fits));
    }

    /// <summary>Creative refining runs this many times survival's speed (proposed).</summary>
    public const int CreativeSpeed = 8;

    /// <summary>A creative load takes at least this long (steps; 1.5 s), so its lamp shows Running and a Start is seen (proposed).</summary>
    public const int CreativeMinSteps = 15;

    /// <summary>
    /// Creative: the whole load that fits is one job, worked at <see cref="CreativeSpeed"/> times the survival rate and never in under
    /// <see cref="CreativeMinSteps"/> steps, then made at once. Unpowered (creative needs no power); Busy while it works.
    /// </summary>
    private static void Creative(ConstructWorld world, in RefineSize size, Construct c, int part, CStore st, RefineRecipe recipe)
    {
        var fits = Batches(size, st, recipe);
        if (fits <= 0)
        {
            st.JobTicks = 0;
            ConstructGatherer.SetBusy(world, st, false);
            return;
        }
        ConstructGatherer.SetBusy(world, st, true);
        st.JobTicks += size.Work;
        var need = Math.Max((long)fits * recipe.Ticks * Milli / CreativeSpeed, (long)CreativeMinSteps * size.Work);
        if (st.JobTicks < need)
            return;
        st.JobTicks = 0;
        Make(world, c, part, st, recipe, fits);
    }

    private static void Make(ConstructWorld world, Construct c, int part, CStore st, RefineRecipe recipe, int batches)
    {
        if (recipe.Mix.Length == 0)
            world.UseInput(st, batches * recipe.UnitsIn);
        else
            foreach (var (m, units) in recipe.Mix)
                world.AddInput(st, m, -batches * units);
        world.Yield(CYieldKind.Refined, c, recipe.Output, world.Deposit(c, part, batches * recipe.UnitsOut, recipe.Output));
        ProductionLines.Made(world, c, part, batches); // (a batch is a piece to its console's Made count)
    }

    /// <summary>How many batches the input holds and the output has room for (none while a bin holds more than it takes now: a block taken away).</summary>
    private static int Batches(in RefineSize size, CStore st, RefineRecipe recipe)
    {
        var n = Math.Max(0, size.Out - st.Amount) / recipe.UnitsOut;
        if (recipe.Mix.Length == 0)
            return Math.Min(n, st.InAmount / recipe.UnitsIn);
        foreach (var (m, units) in recipe.Mix)
            n = Math.Min(n, st.MixOf(m) / units);
        return n;
    }

    /// <summary>Does a mixed machine take this material, alongside what it holds? Some recipe of its tier must use it and all of what is there; with a set recipe, that one. Allocates nothing.</summary>
    public static bool MixTakes(RefiningTable table, ConstructPartDef d, CStore st, byte material, RefineRecipe? set = null)
    {
        if (set != null)
            return set.Uses(material) && UsesAll(set, st);
        foreach (var r in table.MixesOf(d.RefineMachine))
            if (r.MinTier <= d.Tier && r.Uses(material) && UsesAll(r, st))
                return true;
        return false;
    }

    /// <summary>
    /// A machine's set recipe (its "recipe" setting, docs/sandbox/constructs/production.md "Production lines"), or null: Auto, what is in the
    /// input picks it. A set recipe only filters what goes in (a chute or F): what is already in the input is worked as ever, so changing it never
    /// strands a load the player can't take back out.
    /// </summary>
    public static RefineRecipe? SetOf(in CPart p, ConstructPartDef d)
    {
        if (d.RefineChoices.Length == 0)
            return null;
        var v = (int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0);
        return v >= 1 && v <= d.RefineChoices.Length ? d.RefineChoices[v - 1] : null;
    }

    /// <summary>As the parts load: each machine with a "recipe" setting gets its tier's recipes as the choices (Auto first).</summary>
    public static ConstructPartDef[] Attach(ConstructPartDef[] parts)
    {
        var table = RefiningTable.Current;
        foreach (var d in parts)
        {
            if (!d.IsRefiner || d.CfgSlot[(int)CfgKey.Recipe] < 0)
                continue;
            d.RefineChoices = table.Recipes.Where(r => r.Machine == d.RefineMachine && r.MinTier <= d.Tier).ToArray();
            d.Config[d.CfgSlot[(int)CfgKey.Recipe]].Options = ["Auto", .. d.RefineChoices.Select(Label)];
        }
        return parts;
    }

    /// <summary>A recipe as a choice: "iron ore to metal", "steel (2 metal + 1 stone)".</summary>
    private static string Label(RefineRecipe r) => r.Mix.Length > 0 ? $"{r.OutputName} ({r.Batch})" : $"{r.InputName} to {r.OutputName}";

    private static bool UsesAll(RefineRecipe r, CStore st)
    {
        for (var k = 0; k < st.MixN; k++)
            if (!r.Uses(st.MixMat![k]))
                return false;
        return true;
    }

    /// <summary>The recipe a mixed input picks: one that uses all of what is there, first one with a whole batch. Null: none does.</summary>
    internal static RefineRecipe? MixRecipe(RefiningTable table, ConstructPartDef d, CStore st)
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
    public static RefineState StateOf(RefiningTable table, Construct c, int part, CStore st, out RefineRecipe? recipe) =>
        StateOf(table, c.DefOf(part), RefineOf(c, part), st, out recipe);

    private static RefineState StateOf(RefiningTable table, ConstructPartDef d, in RefineSize size, CStore st, out RefineRecipe? recipe)
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
        return size.Out - st.Amount < recipe.UnitsOut ? RefineState.OutputFull : RefineState.Ready;
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
    public static byte TipMaterial(SandboxInventory inv, Construct c, int part, CStore st, RefineRecipe? set = null)
    {
        var d = c.DefOf(part);
        var inCap = RefineOf(c, part).In;
        if (d.RefineMix)
        {
            for (byte m = 1; m < SandboxInventory.MaxMaterials; m++)
                if (inv.Materials[m] > 0 && st.MixOf(m) < inCap && MixTakes(RefiningTable.Current, d, st, m, set))
                    return m;
            return 0;
        }
        if (st.InAmount >= inCap)
            return 0;
        if (st.InAmount > 0) // (Feed's rule keeps a material the machine's tier can't work out of the input)
            return inv.Materials[st.InMaterial] > 0 && (set == null || set.Input == st.InMaterial) ? st.InMaterial : (byte)0;
        if (set != null)
            return inv.Materials[set.Input] > 0 ? set.Input : (byte)0;
        for (byte m = 1; m < SandboxInventory.MaxMaterials; m++)
            if (inv.Materials[m] > 0 && RefiningTable.Current.Find(d.RefineMachine, m) is { } r && r.MinTier <= d.Tier)
                return m;
        return 0;
    }

    /// <summary>What the machine is doing, or why not. Read by the look line on either side.</summary>
    public static string Status(Construct c, int part, CStore st, RefineRecipe? set = null)
    {
        var names = RecyclingTable.Current;
        var table = RefiningTable.Current;
        var d = c.DefOf(part);
        var state = StateOf(table, d, RefineOf(c, part), st, out var r);
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
        if (set != null)
            doing += $" - set to {set.OutputName}";
        if (st.Refused == 0)
            return doing;
        if (set != null && !set.Uses(st.Refused))
            return $"{doing} - chute refused {names.NameOf(st.Refused)}: set to {set.OutputName}";
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
    public static string Look(Construct c, int part, CStore st, bool near, SandboxInventory inv, RefineRecipe? set = null)
    {
        var names = RecyclingTable.Current;
        var d = c.DefOf(part);
        var size = RefineOf(c, part);
        var tip = TipMaterial(inv, c, part, st, set);
        var act = !near ? ""
            : tip != 0 ? $" - {SandboxKeys.Use}: tip in {Math.Min((int)inv.Materials[tip], size.In - (d.RefineMix ? st.MixOf(tip) : st.InAmount))} {names.NameOf(tip)}"
            : st.Amount > 0 ? $" - {SandboxKeys.Use}: take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {names.NameOf(st.Material)}"
            : "";
        var output = st.Amount > 0 ? " " + names.NameOf(st.Material) : "";
        var sized = d.BlockSquares ? $" - {SizeText(c, part)}" : ""; // (a squares Core: its furnaces and rate, what the next size needs)
        if (d.RefineMix)
            return $"{d.Name}: in {(st.MixN == 0 ? $"0/{size.In}" : $"{MixHolds(st)} (of {size.In} each)")}, out {st.Amount}/{size.Out}{output}{Status(c, part, st, set)}{sized}{act}";
        var input = st.InAmount > 0 ? " " + names.NameOf(st.InMaterial) : "";
        return $"{d.Name}: in {st.InAmount}/{size.In}{input}, out {st.Amount}/{size.Out}{output}{Status(c, part, st, set)}{sized}{act}";
    }
    // ---- A machine's size: a squares Core grows with its blocks (docs/sandbox/constructs/production.md "Refiners as block machines") ----

    /// <summary>
    /// A production machine's numbers as it stands (<see cref="RefineOf"/>): four times its rate, its work a 10 Hz step, both bins, its
    /// chute load (survival's: <see cref="ChuteLoadOf"/> adds creative's) and its power shares. A part file's figures, for any machine but a
    /// squares Core (the Smelter), whose rate is the sum of its squares' (MachineBlocks.Squares).
    /// </summary>
    public readonly struct RefineSize(int r4, int work, int outBin, int inBin, int load, int shares)
    {
        public readonly int R4 = r4, Work = work, Out = outBin, In = inBin, Load = load, Shares = shares;
        public float Rate => R4 / 4f;
    }

    /// <summary>
    /// What every reader of a production machine's rate, bins, chute load or power goes through, server and client alike (a reader that
    /// kept the part file's figure would have a batch take its whole input while the bin kept only what fits). A squares Core: rate
    /// r = R4 / 4 (its own hearth 1, each whole cell of an n x n square 1 + n/4), work and chute load x r, bins x r up to its file's
    /// capacity, power one share for the Core and each whole cell. Allocates nothing.
    /// </summary>
    public static RefineSize RefineOf(Construct c, int part)
    {
        var d = c.DefOf(part);
        if (!d.BlockSquares || d.RefineOutBin == 0)
            return new RefineSize(4, d.RefineWork, d.BulkCapacity, d.InCapacity, d.ChuteLoad, 1);
        int r4 = 4, shares = 1;
        if (MachineBlocks.TryOf(c, part, out var asm) && asm.Core == part)
            (r4, shares) = (Math.Max(4, (int)asm.R4), Math.Max(1, (int)asm.Shares));
        return Sized(d, r4, shares);
    }

    /// <summary>A squares Core's numbers at four times rate <paramref name="r4"/> (the FactoryKit sizes one from a plan this way).</summary>
    public static RefineSize Sized(ConstructPartDef d, int r4, int shares) => new(r4, (int)Math.Min(int.MaxValue, (long)d.RefineWork * r4 / 4),
        (int)Math.Min(d.BulkCapacity, (long)d.RefineOutBin * r4 / 4), (int)Math.Min(d.InCapacity, (long)d.RefineInBin * r4 / 4),
        (int)Math.Min(ConstructStorage.MaxBulk, ((long)ConstructRoutes.ChuteLoad * r4 + 3) / 4), shares);

    /// <summary>Four times the rate of a Core with these squares' sides (all whole): 4 + each n^2 (4 + n). 4 + 9 x 7 = 67 for a 3 x 3 (x16.75).</summary>
    public static int R4Of(ReadOnlySpan<int> sides)
    {
        var r4 = 4;
        foreach (var n in sides)
            r4 += n * n * (4 + n);
        return r4;
    }

    /// <summary>A bin's chute load as it is now: a squares Core's grows with its rate, and in creative (infinite parts on) eight times that, so one chute keeps up with its machine.</summary>
    public static int ChuteLoadOf(ConstructWorld world, Construct c, int part)
    {
        var d = c.DefOf(part);
        if (!d.BlockSquares || !d.IsRefiner)
            return d.ChuteLoad;
        var load = RefineOf(c, part).Load;
        return world.CreativeRecycling ? Math.Min(ConstructStorage.MaxBulk, load * CreativeSpeed) : load;
    }

    /// <summary>A squares Core's draw while it works: its part file's power a share (the Core and each whole block in its squares).</summary>
    public static float PowerOf(Construct c, int part) => c.DefOf(part).PowerDraw * RefineOf(c, part).Shares;

    /// <summary>A squares Core's machine as its look line and screen say it: "furnaces 3x3, 2x2, 1x1: speed x24", what does nothing, and what the next size needs.</summary>
    public static string SizeText(Construct c, int part)
    {
        var d = c.DefOf(part);
        var noun = d.RefineMachine switch { "smelter" => "furnaces", "foundry" => "crucibles", "sawmill" => "mills", _ => "squares" };
        return MachineBlocks.TryOf(c, part, out var asm) && asm.Core == part ? MachineBlocks.SquaresText(c, asm, noun) : "speed x1: weld its blocks on its +x side";
    }

    /// <summary>
    /// A saved recipe choice of a Smelter saved at tier 2 to 5 (it loads as the Smelter Core, ConstructParts.Renamed): its index named a recipe
    /// of that tier's list, which the gates cut (iron, copper 2, gold 3, sand, dirt, stone, levite 4); the same recipe's place in the Core's
    /// list now (the old tier 1 recipes stay first, so a tier 1 choice keeps its number). 0 (Auto), or anything unknown, stays as it is.
    /// </summary>
    public static float SavedRecipe(ConstructPartDef d, int tier, float v)
    {
        string?[] old = ["iron ore", tier >= 2 ? "copper ore" : null, tier >= 3 ? "gold ore" : null, "sand", "dirt", "stone", tier >= 4 ? "levite ore" : null];
        var k = (int)v;
        if (d.RefineMachine != "smelter" || k < 1 || k != v)
            return v;
        foreach (var name in old)
        {
            if (name == null)
                continue;
            if (--k > 0)
                continue;
            var at = Array.FindIndex(d.RefineChoices, r => r.Mix.Length == 0 && r.InputName == name);
            return at >= 0 ? at + 1 : 0;
        }
        return v;
    }

    /// <summary>The tier a saved part's name gave a line converted to a squares Core ("Workshop Smelter" 2 to "Plasma Smelter" 5), else 0.</summary>
    public static int SavedTier(string name) => name.ToLowerInvariant() switch
    {
        "workshop smelter" => 2,
        "industrial smelter" => 3,
        "military smelter" => 4,
        "plasma smelter" => 5,
        _ => 0,
    };
}
