using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Why an Assembler is or isn't building (<see cref="ConstructAssembling.StateOf"/>): worked out from its store, so a client's look line says the same as the server.</summary>
public enum AsmState : byte
{
    NoRecipe, // none chosen
    TierLow, // the recipe asks for a higher tier of machine
    Short, // not enough for a batch
    OutputFull, // the tray has no room for what it makes
    Blocked, // a finished batch can't leave (CStore.Blocked)
    Ready,
    Untaught, // a story faction hasn't taught its owner this part yet (AsmBlock.Untaught)
    NoOrders, // it has a queue of work orders, empty: it waits for the next (WorkOrders.cs)
    Pressed, // (dead since Press phase 5b dropped the marker rows: nothing sets it; kept while MachineScreens.cs, another session's, names it)
    NeedsModule, // the recipe needs an upgrade module the Core hasn't got (AssembleRecipe.Needs)
    TooBig, // the part doesn't fit the bed or its clear height (AsmFit.Places)
    NeedsTool, // the recipe needs a tool block in the Core's Tool Bay that isn't there (AssembleRecipe.Tool): a chute from a Block Rack swaps it in
}

/// <summary>
/// What an Assembler on the block system can take on (MachineBlocks, family "assembler"; docs/sandbox/constructs/production.md
/// "Assembling"): the upgrade modules on its Core, its Gantry Carriages and its bed. Worked out from the parts on either side, never sent.
/// <see cref="Free"/> is a machine with no limits (a part that isn't an Assembler Core: a self-test's).
/// </summary>
public readonly struct AsmFit
{
    public const float CoreSide = 2f; // m: what a bare Core builds on its own deck, a side (an Assembly Bed Block, so survival can start a bed)
    public const float SlackM = 0.02f; // m: a part this much over a cell's side still fits it

    public readonly bool Limited; // a block Assembler (false: Free)
    public readonly byte Modules; // the modules' bits (MachineBlocks.TierPieceNames: 2 Power Coupling to 5 Plasma Conditioner)
    public readonly byte Overdrive, Buffers; // Overdrive Units, Feed Buffers
    public readonly byte Carriages; // its arms: the Core's own and one a Gantry Carriage on its bed
    public readonly byte W, L; // its bed in cells (0: a bare Core)
    public readonly float Cell; // a cell's side (m)
    public readonly float Clear; // the clear height over the bed (m)
    public readonly byte Tool; // the tool block docked in its Tool Bay (AssemblingTable.Machines: 1 thruster, 2 tailor), 0 none

    public AsmFit(byte modules, byte overdrive, byte buffers, byte carriages, byte w, byte l, float cell, float clear, byte tool = 0)
    {
        Tool = tool;
        Limited = true;
        Modules = modules;
        Overdrive = overdrive;
        Buffers = buffers;
        Carriages = carriages;
        W = w;
        L = l;
        Cell = cell;
        Clear = clear;
    }

    public static AsmFit Free => default;

    /// <summary>The build speed: x2 an Overdrive Unit.</summary>
    public int Speed => Limited ? 1 << Overdrive : 1;

    /// <summary>The draw for each working arm: x1.8 an Overdrive Unit.</summary>
    public float DrawScale => Limited ? MathF.Pow(1.8f, Overdrive) : 1f;

    /// <summary>The modules a recipe needs that this one hasn't got (bits), 0 when it has them all.</summary>
    public byte Missing(AssembleRecipe r) => Limited ? (byte)(r.Needs & ~Modules) : (byte)0;

    /// <summary>Does a recipe need a tool block this one hasn't docked?</summary>
    public bool LacksTool(AssembleRecipe r) => Limited && r.ToolId != 0 && r.ToolId != Tool;

    /// <summary>The tool block that gives a tool (AssemblingTable.Machines index).</summary>
    public static string ToolBlockName(int tool) => tool switch
    {
        1 => "Thruster Jig Block",
        2 => "Sewing Table Block",
        _ => "tool block",
    };

    /// <summary>A part's plan footprint in cells, its sides rounded up (either way round), and whether it stands under the clear height.</summary>
    public void Footprint(ConstructPartDef d, out int fx, out int fz, out bool tall)
    {
        var size = d.BoundsMax - d.BoundsMin;
        fx = Math.Max(1, (int)MathF.Ceiling((size.X - SlackM) / Cell));
        fz = Math.Max(1, (int)MathF.Ceiling((size.Z - SlackM) / Cell));
        tall = size.Y > Clear + SlackM;
    }

    /// <summary>How many of a part the bed packs side by side (its places): 0 when it doesn't fit. A bare Core has one place, for a part up to 1.5 m a side.</summary>
    public int Places(ConstructPartDef d) => Places(d, out _);

    public int Places(ConstructPartDef d, out bool turned)
    {
        turned = false;
        if (!Limited)
            return 1;
        var size = d.BoundsMax - d.BoundsMin;
        if (W == 0)
            return MathF.Max(size.X, MathF.Max(size.Y, size.Z)) <= CoreSide + SlackM ? 1 : 0;
        Footprint(d, out var fx, out var fz, out var tall);
        if (tall)
            return 0;
        int a = W / fx * (L / fz), b = W / fz * (L / fx);
        turned = b > a;
        return Math.Max(a, b);
    }

    /// <summary>Parts it builds at once: the smaller of its arms and the places (0: it can't build it).</summary>
    public int AtOnce(ConstructPartDef d) => Limited ? Math.Min(Carriages, Places(d)) : 1;

    /// <summary>Why a part doesn't fit, for the look line and the screen ("needs a 2 x 3 bed, this one is 2 x 2").</summary>
    public string TooBigWhy(ConstructPartDef d)
    {
        var size = d.BoundsMax - d.BoundsMin;
        if (W == 0)
            return $"needs a bed ({size.X:0.0} x {size.Z:0.0} x {size.Y:0.0} m, a bare Core builds up to {CoreSide:0.0} m): weld Assembly Bed Blocks to it";
        Footprint(d, out var fx, out var fz, out var tall);
        if (tall)
        {
            var masts = (int)MathF.Ceiling((size.Y - SlackM) / Cell);
            return $"needs {masts * Cell:0} m clear, this one has {Clear:0} m: stack {masts} Mast Block{(masts == 1 ? "" : "s")} on each mast column";
        }
        return $"needs a {Math.Min(fx, fz)} x {Math.Max(fx, fz)} bed, this one is {Math.Min(W, L)} x {Math.Max(W, L)}";
    }

    /// <summary>The modules a recipe needs and this one lacks, as words: "a Servo Rack and a Power Coupling".</summary>
    public static string ModuleWords(byte bits)
    {
        var s = "";
        for (var k = 2; k <= MachineBlocks.MaxTier; k++)
            if ((bits & 1 << k) != 0)
                s += (s.Length > 0 ? " and a " : "a ") + MachineBlocks.TierPieceNames[k];
        return s;
    }
}

/// <summary>Why a finished batch can't leave an Assembler (<see cref="CStore.Blocked"/>).</summary>
public enum AsmBlock : byte
{
    None,
    OutputBlocked, // something stands where a body leaves
    WorldFull, // no room for another construct
    Untaught, // not a finished batch: the recipe's part is a technology its owner hasn't been taught (ConstructWorld.Taught)
    ConveyorSmall, // the part is too wide for the conveyor out of its Piece out, and no tray slot takes it
    ConveyorFull, // no room at the start of that conveyor
    ConveyorCut, // that conveyor is cut
    BedFull, // a block Assembler's bed holds what it made before, every place: it waits for a crane or an arm to lift them off
    COUNT,
}

/// <summary>Where a recipe's output goes.</summary>
public enum AsmOut : byte
{
    Tray, // a slot has room
    TrayFull, // it fits the tray, which is full
    Body, // physics-only, or too big for the tray: it leaves as a body at the output point
}

/// <summary>
/// Assembling (docs/sandbox/CONSTRUCTS.md "Assembling"): a machine that builds parts from a recipe chosen with the configure
/// tool (Constructs/Assembling.json). Its input is mixed: materials in <see cref="CStore.MixMat"/>, parts in the store's
/// kinds, as much as <see cref="ConstructPartDef.AsmBatches"/> batches of each. Chutes, F and held parts feed it. What it
/// builds sits in its slots (the output tray, read by chutes, F and beacons as any slots) or, when physics-only or too big for
/// a slot, leaves as a body at its output point. The input is used when a batch is done, so a batch in hand loses nothing, and
/// a body that can't leave waits with its inputs. Power, Run and creative are the production machines'
/// (<see cref="ConstructRefining"/>). Server, at 10 Hz, looking only at constructs that have one; nothing allocates.
/// </summary>
public static class ConstructAssembling
{
    private const int Milli = 1000;
    private const int RetrySteps = 5; // how often a blocked output looks again (10 Hz steps)

    /// <summary>An Assembler's limits as it stands (<see cref="AsmFit"/>): an Assembler Core's machine, else none.</summary>
    public static AsmFit FitOf(Construct? c, int part)
    {
        if (c == null || (uint)part >= (uint)c.PartCount)
            return AsmFit.Free;
        var d = c.Parts[part].D;
        if (d.BlockRole != BlockRole.Core || !MachineBlocks.TryOf(c, part, out var asm) || MachineBlocks.Families[asm.Family] != "assembler")
            return AsmFit.Free; // (a Panel Former is an Assembler too: its own rules, ConstructPanelFormer)
        var carriages = (byte)Math.Min(MachineBlocks.MaxCarriages, 1 + asm.Carriages); // (the Core's own arm, and one a Gantry Carriage)
        var clear = d.BlockCell * Math.Max(1, (int)asm.Masts); // (2 m a mast block by the shortest corner column, 2 m with none: the Building Station's rule)
        return new AsmFit(asm.TierPieces, asm.Overdrive, asm.Buffers, carriages, asm.W, asm.L, d.BlockCell, clear, asm.Tool);
    }

    /// <summary>Would this Assembler build the recipe as it stands: its modules and its bed (inputs aside)?</summary>
    public static bool Takes(Construct c, int part, AssembleRecipe r)
    {
        var f = FitOf(c, part);
        return f.Missing(r) == 0 && f.AtOnce(r.Output) > 0;
    }

    /// <summary>What an Assembler Core draws while it builds (W): its own draw for each working arm, x1.8 an Overdrive Unit.</summary>
    public static float PowerOf(Construct c, int part)
    {
        var d = c.Parts[part].D;
        var f = FitOf(c, part);
        if (!f.Limited)
            return d.PowerDraw;
        var r = RecipeOf(AssemblingTable.Current, c.Parts[part], d);
        return d.PowerDraw * Math.Max(1, r == null ? 1 : f.AtOnce(r.Output)) * f.DrawScale;
    }

    public static void Step(ConstructWorld world, IWorldQuery query)
    {
        if (world.ToolSwaps.Count > 0)
            AssemblerToolBay.Deliver(world); // (a tool block a chute took from a rack last tick docks now)
        if (++world.AssembleTicks < ConstructRefining.Every)
            return;
        world.AssembleTicks = 0;
        var table = AssemblingTable.Current;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasAssembler)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsAssembler && world.StoreOf(c, i) is { } st)
                    Work(world, query, table, c, i, st);
        }
    }

    /// <summary>
    /// The recipe a part works now, or null: with a queue of work orders (WorkOrders.cs) its head order's (none when the queue is empty, so
    /// a finished queue makes nothing more), else the one chosen with the configure tool.
    /// </summary>
    public static AssembleRecipe? RecipeOf(AssemblingTable table, in CPart p, ConstructPartDef d)
    {
        if (p.Orders is { Bill: false } q)
            return q.Count > 0 && table.Of(q.Head.Part) is { } r && r.Machine == d.AsmMachine ? r : null;
        var k = (int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0) - 1;
        var list = table.ByMachine[d.AsmMachine];
        return k >= 0 && k < list.Length && !BakeKits.Has(list[k].Output) ? list[k] : null; // (a part made from a kit is baked, never assembled: BakeKits.cs; its row stays for saved settings)
    }

    private static void Work(ConstructWorld world, IWorldQuery query, AssemblingTable table, Construct c, int part, CStore st)
    {
        if ((st.Refused != 0 || st.RefusedPart != ConstructParts.None) && ++st.RefusedAge >= 40)
        {
            st.Refused = 0;
            st.RefusedPart = ConstructParts.None;
            st.RefusedAge = 0;
            st.Version++;
            world.StoreDirty.Add(st.Handle);
        }
        var d = c.Parts[part].D;
        var recipe = RecipeOf(table, c.Parts[part], d);
        var id = recipe?.Choice ?? 0;
        if (st.JobAmount != id)
        {
            st.JobAmount = id; // (a new recipe starts its batch afresh)
            st.JobTicks = 0;
            SetBlocked(world, st, AsmBlock.None);
        }
        if (recipe != null && world.Taught is { } taught && !world.CreativeRecycling && !taught(c.Owner, recipe.Output))
        {
            // A technology nobody has taught its owner: it waits, and says so (a faction's teaching ends the wait).
            st.JobTicks = 0;
            SetBlocked(world, st, AsmBlock.Untaught);
            ConstructGatherer.SetBusy(world, st, false);
            MachineScreens.SetProgress(world, st, 0);
            return;
        }
        if (st.Blocked == (byte)AsmBlock.Untaught)
            SetBlocked(world, st, AsmBlock.None);
        var held = ConstructGatherer.HeldOff(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        var fit = FitOf(c, part);
        var state = StateOf(d, st, recipe, fit);
        if (state is not (AsmState.Ready or AsmState.Blocked))
        {
            st.JobTicks = 0;
            SetBlocked(world, st, AsmBlock.None);
        }
        if (held || state is not (AsmState.Ready or AsmState.Blocked) || !ConstructRecycler.Powered(world, c, part))
        {
            ConstructGatherer.SetBusy(world, st, false);
            MachineScreens.SetProgress(world, st, 0);
            return;
        }
        if (state == AsmState.Blocked)
        {
            // Its batch is done and waits: it draws nothing, and looks again now and then.
            ConstructGatherer.SetBusy(world, st, false);
            if (++st.JobMaterial >= RetrySteps)
            {
                st.JobMaterial = 0;
                Finish(world, query, c, part, st, recipe!, 0);
            }
            return;
        }
        if (world.CreativeRecycling)
        {
            ConstructGatherer.SetBusy(world, st, false);
            Wave(world, query, c, part, st, recipe!, fit);
            return;
        }
        ConstructGatherer.SetBusy(world, st, true);
        st.JobTicks += d.RefineWork * fit.Speed; // (x2 an Overdrive Unit)
        var need = recipe!.Ticks * Milli;
        MachineScreens.SetProgress(world, st, Math.Min(99, (int)((long)st.JobTicks * 100 / need)));
        if (st.JobTicks < need)
            return;
        st.JobTicks -= need; // (what is left over carries into the next batch)
        Wave(world, query, c, part, st, recipe, fit);
    }

    /// <summary>
    /// A wave is done: each arm that had its batch in builds one part, at its own place on the bed (one recipe at a time: every carriage
    /// builds the head order's part). A wave stops at the first part that can't leave, which waits with its inputs.
    /// </summary>
    private static void Wave(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, AssembleRecipe recipe, in AsmFit fit)
    {
        var n = Math.Max(1, fit.AtOnce(recipe.Output));
        for (var k = 0; k < n; k++)
        {
            if (k > 0 && (MissingOf(st, recipe, out _, out _) || OutputOf(c.Parts[part].D, st, recipe) == AsmOut.TrayFull))
                return; // (the hopper held fewer batches than arms: the rest wait for the next wave)
            if (k > 0 && c.Parts[part].Orders is { Bill: false } q && (q.Count == 0 || q.Head.Part != recipe.Output.Id))
                return; // (the order is filled: the next one's recipe starts afresh)
            if (!Finish(world, query, c, part, st, recipe, k))
                return;
        }
    }

    private static string? _leaveWhy; // (the last place's refusal in Leave: the log line when an output first can't leave)
    private static ConstructWorld? _leaveWorld;
    private static int _leavePart;

    /// <summary>Why the last place refused, naming the part in the way when it was a construct's.</summary>
    private static string LeaveWhy()
    {
        var why = _leaveWhy ?? "no place on its bed fits it";
        if (why.StartsWith("another construct", StringComparison.Ordinal) && _leaveWorld?.Get(ConstructPlace.LastInWayBody) is { } b && ConstructPlace.LastInWayPart >= 0 && ConstructPlace.LastInWayPart < b.PartCount)
            why += $" (body {b.Id} part {ConstructPlace.LastInWayPart}: {b.DefOf(ConstructPlace.LastInWayPart).Name})";
        return why;
    }

    private static void SetBlocked(ConstructWorld world, CStore st, AsmBlock why)
    {
        if (st.Blocked == (byte)why)
            return;
        st.Blocked = (byte)why;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    /// <summary>A batch is done: its output leaves, then its inputs go. A body that can't leave keeps them and waits (false).</summary>
    private static bool Finish(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, AssembleRecipe recipe, int place)
    {
        var d = c.Parts[part].D;
        var dest = OutputOf(d, st, recipe);
        var conveyor = ConstructConveyor.Out(c, part);
        var rode = false;
        if (conveyor >= 0)
        {
            // A Piece out conveyor takes what it makes, whole batches only; a part too wide for it goes to the tray when a slot takes it.
            rode = ConstructConveyor.PutMade(world, c, conveyor, recipe.Output, recipe.Count, out var full);
            if (!rode && !(full == ExtrudeStall.ConveyorSmall && dest != AsmOut.Body))
            {
                SetBlocked(world, st, full switch { ExtrudeStall.ConveyorSmall => AsmBlock.ConveyorSmall, ExtrudeStall.ConveyorCut => AsmBlock.ConveyorCut, _ => AsmBlock.ConveyorFull });
                return false;
            }
        }
        if (!rode && dest == AsmOut.TrayFull)
            return false; // (creative, or a chute took less than the state saw: it waits)
        if (rode)
        {
            // (on its way to a rack)
        }
        else if (dest == AsmOut.Body)
        {
            for (var n = 0; n < recipe.Count; n++)
            {
                var why = Leave(world, query, c, part, recipe.Output, place);
                if (why == AsmBlock.None)
                    continue;
                if (st.Blocked != (byte)why && why is AsmBlock.OutputBlocked or AsmBlock.BedFull)
                    Console.WriteLine($"[assembler] body {c.Id} part {part}: its {recipe.Output.Name} can't leave: {LeaveWhy()}");
                SetBlocked(world, st, why);
                return false;
            }
        }
        else
            for (var n = 0; n < recipe.Count; n++)
                world.TrayAdd(st, d, recipe.Output);
        SetBlocked(world, st, AsmBlock.None);
        world.Note(CNoteKind.Made, c.Id, c.Owner, (ushort)recipe.Output.Id, (byte)d.Tier); // (the story's milestones: ConstructWorldNotes.cs)
        ProductionLines.Made(world, c, part, recipe.Count); // (its console's Made count)
        foreach (var (m, units) in recipe.Materials)
            world.AddInput(st, m, -units);
        foreach (var (p, count) in recipe.Parts)
            world.AddInputPart(st, p.Id, -count);
        if (c.Parts[part].Orders is { Bill: false, Count: > 0 } q && q.Head.Part == recipe.Output.Id)
            WorkOrders.Made(world, c, q, recipe.Count); // (its head order: at its count the next order's recipe starts)
        return true;
    }

    /// <summary>
    /// A body leaves at the output point, on its far side and level, its near side at the point. Why not, or None. An Assembler with a bed
    /// sets it on the bed instead, at the place its arm built it (<paramref name="place"/>, or the next free one), for a crane or an arm to lift off.
    /// </summary>
    internal static AsmBlock Leave(ConstructWorld world, IWorldQuery query, Construct c, int part, ConstructPartDef def, int place = 0)
    {
        if (!world.HasRoom())
            return AsmBlock.WorldFull;
        (_leaveWorld, _leaveWhy, _leavePart) = (world, null, part);
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (FitOf(c, part) is { Limited: true, W: > 0 } fit && MachineBlocks.TryOf(c, part, out var asm))
            return LeaveOnBed(world, query, c, part, def, place, fit, asm);
        c.WorldFrame(p.Seg, out var q, out var t);
        var at = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, d.AsmOutAt), q);
        var dir = Vector3.Transform(Rot24.Apply(p.Rot, d.AsmOutDir), q) with { Y = 0 };
        if (dir.LengthSquared() < 0.01f)
            dir = Vector3.UnitZ;
        dir = Vector3.Normalize(dir);
        var rot = ConstructGrab.YawQ(MathF.Atan2(dir.X, -dir.Z));
        var mid = (def.BoundsMin + def.BoundsMax) * 0.5f;
        var half = (def.BoundsMax - def.BoundsMin) * 0.5f;
        var deep = MathF.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitX, rot), dir)) * half.X + MathF.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, rot), dir)) * half.Z;
        Span<float> lifts = [0.05f, 0.4f, 1f]; // (the ground may rise a little in front of it)
        var feet = world.PlayerSpots.AsSpan(0, world.PlayerSpotCount);
        foreach (var lift in lifts)
        {
            var centre = at + dir * (deep + 0.05f) + new Vector3(0, half.Y + lift, 0);
            var fp = new FreePlacement { Pos = centre - Vector3.Transform(mid, rot), Rot = rot };
            if ((_leaveWhy = ConstructPlace.Fits(world, query, def, fp, feet)) != null)
                continue;
            if (ConstructBuild.PlaceNew(world, def, fp) is not { } made)
                return AsmBlock.WorldFull;
            made.Owner = c.Owner;
            made.Wake();
            world.Events.Add($"construct {c.Id}: a {d.Name} built a {def.Name} (construct {made.Id})");
            return AsmBlock.None;
        }
        return AsmBlock.OutputBlocked;
    }

    /// <summary>A body set down on an Assembler's bed, at a place of the packing (<see cref="AsmFit.Places"/>): the first free one from <paramref name="place"/>.</summary>
    private static AsmBlock LeaveOnBed(ConstructWorld world, IWorldQuery query, Construct c, int part, ConstructPartDef def, int place, in AsmFit fit, in CMachineAsm asm)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var places = fit.Places(def, out var turned);
        if (places == 0)
            return AsmBlock.OutputBlocked;
        fit.Footprint(def, out var fx, out var fz, out _);
        if (turned)
            (fx, fz) = (fz, fx);
        var across = Math.Max(1, fit.W / fx);
        c.WorldFrame(p.Seg, out var q, out var t);
        var coreQ = q * Rot24.Quat(p.Rot);
        var mid = (def.BoundsMin + def.BoundsMax) * 0.5f;
        var half = (def.BoundsMax - def.BoundsMin) * 0.5f;
        var rot = turned ? coreQ * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) : coreQ;
        var bedTop = d.BlockOrigin is { } org ? org.Y : 0f; // (the Core's cell middle is the bed layer's: its top is as far over it as the foot is under)
        var feet = world.PlayerSpots.AsSpan(0, world.PlayerSpotCount);
        var own = MachineBlocks.AsmOf(c, part);
        var madeOnly = true; // (every place refused for a loose piece lying on the bed: what it made before, waiting to be lifted)
        for (var k = 0; k < places; k++)
        {
            var at = (place + k) % places;
            int i = at % across, j = at / across;
            // The place's middle in the Core's cell frame (a cell's middle at whole cells), over the bed's top.
            var x = (asm.X0 - 0.5f + (i + 0.5f) * fx) * fit.Cell;
            var z = (asm.Z0 - 0.5f + (j + 0.5f) * fz) * fit.Cell;
            var local = (d.BlockOrigin ?? Vector3.Zero) + new Vector3(x, bedTop + 0.03f, z);
            var spot = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, local), q);
            var up = Vector3.Transform(Vector3.UnitY, coreQ);
            var centre = spot + up * half.Y;
            var fp = new FreePlacement { Pos = centre - Vector3.Transform(mid, rot), Rot = rot };
            if ((_leaveWhy = ConstructPlace.Fits(world, query, def, fp, feet, null, c, own)) != null)
            {
                madeOnly &= MadeHere(world, c);
                continue;
            }
            if (ConstructBuild.PlaceNew(world, def, fp) is not { } made)
                return AsmBlock.WorldFull;
            made.Owner = c.Owner;
            made.Wake();
            world.Events.Add($"construct {c.Id}: a {d.Name} built a {def.Name} on its bed (construct {made.Id})");
            return AsmBlock.None;
        }
        return madeOnly ? AsmBlock.BedFull : AsmBlock.OutputBlocked;
    }

    /// <summary>Was the last place refused for a loose piece this machine's recipes make (one it made before, lying on the bed)?</summary>
    private static bool MadeHere(ConstructWorld world, Construct c) =>
        _leaveWhy != null && _leaveWhy.StartsWith("another construct", StringComparison.Ordinal) && ConstructPlace.LastInWayBody != c.Id
        && world.Get(ConstructPlace.LastInWayBody) is { PartCount: 1, Anchored: false } lo
        && AssemblingTable.Current.Of((ushort)lo.DefOf(0).Id) is { } r && r.Output == lo.DefOf(0) && r.Machine == c.DefOf(_leavePart).AsmMachine;

    private const int ArmDamage = 1; // per 60 Hz game tick in a working place (60 a second): its arm swinging and its weld (GameMachineHazards)
    private static int _family = -2;
    private const int MostHazards = 60;

    /// <summary>
    /// Server, 10 Hz (from ConstructBakeOven.Step, after the ovens' boxes): a hazard box over the bed of each Assembler building on one, from
    /// the bed's top to 1.9 m over it (its arms reach any place: a part goes to the first free one). Machines never stop for people: the kerb's
    /// stripes are the warning. At most <see cref="MostHazards"/> in the shared list. Allocates nothing.
    /// </summary>
    public static void AddHazards(ConstructWorld world)
    {
        if (_family == -2)
            _family = MachineBlocks.Families.IndexOf("assembler");
        if (_family < 0)
            return;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.Family != _family || asm.W == 0 || asm.Core >= c.PartCount || world.Hazards.Count >= MostHazards)
                    continue;
                ref readonly var p = ref c.Parts[asm.Core];
                if (p.Broken || p.D.BlockRole != BlockRole.Core || world.StoreOf(c, asm.Core) is not { } st || !st.Busy || st.Held || st.Blocked != 0
                    || !ConstructRecycler.Powered(world, c, asm.Core) || !Matrix4x4.Invert(ConstructBakeOven.CoreFrame(c, asm.Core), out var toCore))
                    continue;
                var o = p.D.BlockOrigin ?? Vector3.Zero;
                var cell = p.D.BlockCell;
                var bedTop = o.Y * 2f;
                var min = new Vector3(o.X + (asm.X0 - 0.5f) * cell, bedTop, o.Z + (asm.Z0 - 0.5f) * cell);
                world.Hazards.Add(new MachineHazard { ToLocal = toCore, Min = min, Max = min + new Vector3(asm.W * cell, 1.9f, asm.L * cell), Damage = ArmDamage });
            }
        }
    }

    /// <summary>Where a recipe's output goes as the tray stands.</summary>
    public static AsmOut OutputOf(ConstructPartDef d, CStore st, AssembleRecipe r) => OutputOf(d, st, r.Output, r.Count);

    /// <summary>The same for any machine with a tray (a press).</summary>
    public static AsmOut OutputOf(ConstructPartDef d, CStore st, ConstructPartDef output, int count)
    {
        if (output.PhysicsOnly)
            return AsmOut.Body;
        var fits = false;
        var room = 0;
        for (var k = 0; k < d.Slots.Length && k < st.SlotCount; k++)
        {
            ref readonly var s = ref d.Slots[k];
            if (!ConstructStorage.Fits(s, output, out _))
                continue;
            fits = true;
            room += st.Def[k] == ConstructParts.None ? s.Stack : st.Def[k] == output.Id ? s.Stack - st.Count[k] : 0;
        }
        return !fits ? AsmOut.Body : room >= count ? AsmOut.Tray : AsmOut.TrayFull;
    }

    /// <summary>How much of a material the input takes for a recipe (the buffer's limit).</summary>
    public static int RoomFor(ConstructPartDef d, CStore st, AssembleRecipe r, byte material) => RoomFor(d, st, r, material, AsmFit.Free);

    /// <summary>The same on a block Assembler: its batches a carriage (x2 a Feed Buffer).</summary>
    public static int RoomFor(ConstructPartDef d, CStore st, AssembleRecipe r, byte material, in AsmFit fit) =>
        RoomFor(d, st, r, material, fit, int.MaxValue);

    /// <summary>
    /// The same, at most <paramref name="batches"/> batches held (<see cref="OrderedBatches"/>: what its orders still ask for). An Assembler
    /// on orders filled its hopper with 2,800 rubber for one Zero Day Keel that takes 208 (Sean, 2026-11-22): its buffer was four batches a
    /// carriage, whatever the orders wanted.
    /// </summary>
    public static int RoomFor(ConstructPartDef d, CStore st, AssembleRecipe r, byte material, in AsmFit fit, int batches) =>
        (int)Math.Min(ConstructStorage.MaxBulk, (long)r.UnitsOf(material) * Math.Min(BufferBatches(d, r, fit), batches)) - st.MixOf(material);

    /// <summary>The batches its hopper holds of a recipe: four (the part's) a carriage (x2 a Feed Buffer).</summary>
    public static int BufferBatches(ConstructPartDef d, AssembleRecipe r, in AsmFit fit) =>
        d.AsmBatches * (fit.Limited ? fit.Carriages << fit.Buffers : 1);

    /// <summary>
    /// How many of a part the input takes as it stands: on its own recipe its buffer for that recipe; on orders, what every queued order
    /// that uses the part still asks for (each up to its buffer), less what it holds. Parts are pooled in one hopper, so a later order's
    /// Bearing counts against the head's too: a cap per order would refuse it at the front of the one conveyor in, with the head's own
    /// parts riding behind it (the 2026-11-14 Copper Coil stall).
    /// </summary>
    public static int PartRoom(in CPart p, ConstructPartDef d, CStore st, AssembleRecipe r, ushort part, in AsmFit fit)
    {
        if (p.Orders is not { Bill: false } q)
            return RoomFor(d, st, r, part, fit);
        var table = AssemblingTable.Current;
        long want = 0;
        for (var k = 0; k < q.Count; k++)
        {
            if (q.Items[k].IsPiece || q.Items[k].Left == 0)
                continue;
            var rk = k == 0 ? r : table.Of(q.Items[k].Part);
            if (rk == null || rk.Machine != d.AsmMachine || rk.CountOf(part) == 0)
                continue;
            var per = Math.Max(1, rk.Count);
            want += (long)rk.CountOf(part) * Math.Min(BufferBatches(d, rk, fit), (q.Items[k].Left + per - 1) / per);
        }
        var held = st.KindIndex(part) is var i and >= 0 ? st.KindCount![i] : 0;
        return (int)Math.Min(ConstructStorage.MaxBulk, want) - held;
    }

    /// <summary>
    /// How many batches of a recipe its queued orders still ask for: int.MaxValue for a machine on its own recipe (no queue). The head
    /// order is the recipe's (<see cref="RecipeOf"/>), and a later order for the same part adds to it.
    /// </summary>
    public static int OrderedBatches(in CPart p, AssembleRecipe r)
    {
        if (p.Orders is not { Bill: false } q)
            return int.MaxValue;
        var left = 0;
        for (var k = 0; k < q.Count; k++)
            if (!q.Items[k].IsPiece && q.Items[k].Part == r.Output.Id)
                left += q.Items[k].Left;
        var per = Math.Max(1, r.Count);
        return (left + per - 1) / per;
    }

    /// <summary>How many of a part the input takes for a recipe.</summary>
    public static int RoomFor(ConstructPartDef d, CStore st, AssembleRecipe r, ushort part) => RoomFor(d, st, r, part, AsmFit.Free);

    public static int RoomFor(ConstructPartDef d, CStore st, AssembleRecipe r, ushort part, in AsmFit fit)
    {
        var k = st.KindIndex(part);
        return (int)Math.Min(ConstructStorage.MaxBulk, (long)r.CountOf(part) * BufferBatches(d, r, fit)) - (k < 0 ? 0 : st.KindCount![k]);
    }

    /// <summary>Where a machine stands with what it holds.</summary>
    /// <summary>The same for a machine as it stands: a queue of work orders run dry waits for orders (<see cref="AsmState.NoOrders"/>).</summary>
    public static AsmState StateOf(in CPart p, ConstructPartDef d, CStore st, AssembleRecipe? r) => StateOf(p, d, st, r, AsmFit.Free);

    public static AsmState StateOf(in CPart p, ConstructPartDef d, CStore st, AssembleRecipe? r, in AsmFit fit) =>
        r == null && p.Orders is { Bill: false } ? AsmState.NoOrders : StateOf(d, st, r, fit);

    public static AsmState StateOf(ConstructPartDef d, CStore st, AssembleRecipe? r) => StateOf(d, st, r, AsmFit.Free);

    public static AsmState StateOf(ConstructPartDef d, CStore st, AssembleRecipe? r, in AsmFit fit)
    {
        if (r == null)
            return AsmState.NoRecipe;
        if (st.Blocked == (byte)AsmBlock.Untaught)
            return AsmState.Untaught;
        if (r.MinTier > d.Tier)
            return AsmState.TierLow;
        if (fit.Missing(r) != 0)
            return AsmState.NeedsModule;
        if (fit.LacksTool(r))
            return AsmState.NeedsTool;
        if (fit.AtOnce(r.Output) == 0)
            return AsmState.TooBig;
        if (st.Blocked != 0)
            return AsmState.Blocked;
        if (MissingOf(st, r, out _, out _))
            return AsmState.Short;
        return OutputOf(d, st, r) == AsmOut.TrayFull ? AsmState.OutputFull : AsmState.Ready;
    }

    /// <summary>The first input short of a batch (the material, or the part with <paramref name="part"/> set), if any.</summary>
    public static bool MissingOf(CStore st, AssembleRecipe r, out byte material, out ConstructPartDef? part)
    {
        material = 0;
        part = null;
        foreach (var (m, units) in r.Materials)
            if (st.MixOf(m) < units)
            {
                material = m;
                return true;
            }
        foreach (var (p, count) in r.Parts)
        {
            var k = st.KindIndex(p.Id);
            if ((k < 0 ? 0 : st.KindCount![k]) < count)
            {
                part = p;
                return true;
            }
        }
        return false;
    }

    /// <summary>The first material a player carries that this machine takes now (0: none).</summary>
    public static byte TipMaterial(SandboxInventory inv, ConstructPartDef d, CStore st, AssembleRecipe? r)
    {
        if (r == null || r.MinTier > d.Tier)
            return 0;
        foreach (var (m, _) in r.Materials)
            if (inv.Materials[m] > 0 && RoomFor(d, st, r, m) > 0)
                return m;
        return 0;
    }

    /// <summary>What the machine is doing, or why not. Read by the look line on either side.</summary>
    /// <summary>Who teaches a part, as words for the look line (the game sets it from the faction table, which every PC has): null when it can't say.</summary>
    public static Func<string, string?>? TaughtBy;

    public static string Status(ConstructPartDef d, CStore st, AssembleRecipe? r, bool queued = false) => Status(d, st, r, AsmFit.Free, queued);

    public static string Status(ConstructPartDef d, CStore st, AssembleRecipe? r, in AsmFit fit, bool queued = false, string? toolIn = null)
    {
        var names = RecyclingTable.Current;
        var doing = st.Held ? Program.HeldLook
            : st.Busy && st.Blocked == 0 ? Building(fit, r)
            : (queued && r == null ? AsmState.NoOrders : StateOf(d, st, r, fit)) switch
            {
                AsmState.NoRecipe => " - no recipe set: Recipe on its screen picks one",
                AsmState.TierLow => $" - needs a tier {r!.MinTier} assembler",
                AsmState.NeedsModule => $" - the {r!.Name} needs {AsmFit.ModuleWords(fit.Missing(r))} on the Core",
                AsmState.TooBig => $" - the {r!.Name} {fit.TooBigWhy(r.Output)}",
                AsmState.NeedsTool => toolIn != null
                    ? $" - the {r!.Name} needs the {AsmFit.ToolBlockName(r.ToolId)} in its Tool Bay: one is on the {toolIn} (chute it in from there, or dock it on a free side of the Core)"
                    : $" - the {r!.Name} needs the {AsmFit.ToolBlockName(r.ToolId)} in its Tool Bay: dock one on a free side of the Core, or chute one in from a Block Rack",
                AsmState.Short => MissingOf(st, r!, out var m, out var p) ? $" - needs {(p != null ? $"{r!.CountOf(p.Id)} {p.Name}" : $"{r!.UnitsOf(m)} {names.NameOf(m)}")}" : "",
                AsmState.Untaught => TaughtBy?.Invoke(r!.Output.Name) is { } who ? $" - not taught: {who}" : " - not taught: a faction must teach it first (F2)",
                AsmState.OutputFull => " - tray full",
                AsmState.NoOrders => " - waiting for orders (Clear on its screen: back to its recipe)",
                AsmState.Blocked => BlockedWhy(st),
                _ => " - no power: needs a charged battery",
            };
        if (st.RefusedPart != ConstructParts.None && ConstructParts.Get(st.RefusedPart) is { } rp)
            return $"{doing} - chute refused the {rp.Name}: {RefusedWhy(r, queued)}";
        return st.Refused != 0 ? $"{doing} - chute refused {names.NameOf(st.Refused)}: {RefusedWhy(r, queued)}" : doing;
    }

    /// <summary>", building", or on a block Assembler how many at once and what limits it: ", building 4 at once: fit more carriages (16 places)".</summary>
    private static string Building(in AsmFit fit, AssembleRecipe? r)
    {
        if (!fit.Limited || r == null)
            return ", building";
        var places = fit.Places(r.Output);
        var n = Math.Min(fit.Carriages, places);
        return n <= 1 && places <= 1 ? ", building" : n < places ? $", building {n} at once: fit more carriages ({places} places)" : $", building {n} at once";
    }

    private static string RefusedWhy(AssembleRecipe? r, bool queued) => r == null ? queued ? "no order needs it" : "pick a recipe first" : $"takes {r.Batch}";

    /// <summary>Why a finished batch waits, for the look line.</summary>
    public static string BlockedWhy(CStore st) => (AsmBlock)st.Blocked switch
    {
        AsmBlock.WorldFull => " - the world is full of constructs",
        AsmBlock.ConveyorSmall => " - refused: the part is too wide for its conveyor: widen it",
        AsmBlock.ConveyorFull => " - blocked: its conveyor is full",
        AsmBlock.ConveyorCut => " - its conveyor is cut",
        AsmBlock.BedFull => " - its bed is full of what it made: a crane or an arm must lift them off (to a station or a rack with room)",
        _ => " - something is in the way of its output",
    };

    /// <summary>A few words for why a finished batch waits (the screen and the console).</summary>
    public static string BlockedWord(CStore st) => (AsmBlock)st.Blocked switch
    {
        AsmBlock.WorldFull => "world is full",
        AsmBlock.ConveyorSmall => "conveyor too small",
        AsmBlock.ConveyorFull => "conveyor full",
        AsmBlock.ConveyorCut => "conveyor cut",
        AsmBlock.BedFull => "bed full",
        _ => "output blocked",
    };

    /// <summary>The whole look line: its recipe, what it holds, what it is doing, what F would do.</summary>
    public static string Look(ConstructPartDef d, CStore st, AssembleRecipe? r, bool near, SandboxInventory inv, ConstructPartDef? held, bool queued = false) =>
        Look(d, st, r, AsmFit.Free, near, inv, held, queued);

    public static string Look(ConstructPartDef d, CStore st, AssembleRecipe? r, in AsmFit fit, bool near, SandboxInventory inv, ConstructPartDef? held, bool queued = false, string? toolIn = null)
    {
        var names = RecyclingTable.Current;
        var tip = TipMaterial(inv, d, st, r);
        var feeds = held != null && r != null && r.MinTier <= d.Tier && RoomFor(d, st, r, held.Id, fit) > 0;
        var item = TopItem(st);
        var act = !near ? ""
            : feeds ? $" - {SandboxKeys.Use}: put it in"
            : tip != 0 ? $" - {SandboxKeys.Use}: tip in {Math.Min((int)inv.Materials[tip], RoomFor(d, st, r!, tip))} {names.NameOf(tip)}"
            : item != null ? $" - {SandboxKeys.Use}: take the {item.Name}"
            : st.MixN > 0 || st.KindN > 0 ? $" - {SandboxKeys.Use}: take back its input"
            : "";
        var name = r == null ? d.Name : $"{d.Name} ({r.Name})";
        var holds = st.MixN == 0 && st.KindN == 0 ? "" : $"in {Holds(st)}";
        var tray = st.Items > 0 ? $"tray {st.Items}" : "";
        var state = holds.Length > 0 && tray.Length > 0 ? $"{holds}, {tray}" : holds + tray;
        return $"{name}{(state.Length > 0 ? ": " + state : "")}{Status(d, st, r, fit, queued, toolIn)}{act}";
    }

    /// <summary>The first part in the tray, or null.</summary>
    public static ConstructPartDef? TopItem(CStore st)
    {
        for (var k = 0; k < st.SlotCount; k++)
            if (ConstructParts.Get(st.Def[k]) is { } d)
                return d;
        return null;
    }

    private static string Holds(CStore st)
    {
        if (st.MixN == 0 && st.KindN == 0)
            return "nothing";
        var names = RecyclingTable.Current;
        var s = "";
        for (var k = 0; k < st.MixN; k++)
            s += $"{(s.Length > 0 ? " + " : "")}{st.MixAmount![k]} {names.NameOf(st.MixMat![k])}";
        for (var k = 0; k < st.KindN; k++)
            s += $"{(s.Length > 0 ? " + " : "")}{st.KindCount![k]} {ConstructParts.Get(st.KindDef![k])?.Name}";
        return s;
    }

    /// <summary>A part file's <c>"assemble"</c> block: the input's fill box and scale, its speed, its buffer and the output point.</summary>
    internal static void ParseBlock(ConstructPartDef d, JsonElement b, string file, int slots)
    {
        var bw = $"{file}, storage.assemble";
        if (b.ValueKind != JsonValueKind.Object || d.BulkCapacity != 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner || slots == 0)
            throw new ConstructPartException($"{file}: storage \"assemble\" is an object (min, max, capacity, outAt, outDir) on a part with \"slots\", its output tray, and no \"bulk\"");
        ConstructParts.Known(b, ["min", "max", "capacity", "speed", "chuteLoad", "batches", "outAt", "outDir", "machine"], bw);
        d.InFillMin = ConstructParts.Vec(ConstructParts.Need(b, "min", bw), file, "storage.assemble.min");
        d.InFillMax = ConstructParts.Vec(ConstructParts.Need(b, "max", bw), file, "storage.assemble.max");
        if (d.InFillMax.X - d.InFillMin.X < 0.05f || d.InFillMax.Y - d.InFillMin.Y < 0.05f || d.InFillMax.Z - d.InFillMin.Z < 0.05f)
            throw new ConstructPartException($"{file}: storage.assemble's fill box is smaller than 5 cm a side (or inside out)");
        var cap = ConstructParts.Num(ConstructParts.Need(b, "capacity", bw), file, "storage.assemble.capacity");
        ConstructParts.Range(cap, 1, ConstructStorage.MaxBulk, file, "storage.assemble.capacity");
        d.InCapacity = (int)cap;
        var speed = ConstructParts.Opt(b, "speed") is { } sp ? ConstructParts.Num(sp, file, "storage.assemble.speed") : 1f;
        ConstructParts.Range(speed, 0.1f, 100, file, "storage.assemble.speed");
        d.RefineWork = (int)MathF.Round(speed * ConstructRefining.BaseWork);
        d.ChuteLoad = ConstructParts.Opt(b, "chuteLoad") is { } cl ? (int)ConstructParts.Num(cl, file, "storage.assemble.chuteLoad") : ConstructRoutes.ChuteLoad;
        ConstructParts.Range(d.ChuteLoad, ConstructRoutes.ChuteLoad, ConstructStorage.MaxBulk, file, "storage.assemble.chuteLoad");
        var batches = ConstructParts.Opt(b, "batches") is { } bt ? ConstructParts.Num(bt, file, "storage.assemble.batches") : 4f;
        ConstructParts.Range(batches, 1, 64, file, "storage.assemble.batches");
        d.AsmBatches = (int)batches;
        d.AsmOutAt = ConstructParts.Vec(ConstructParts.Need(b, "outAt", bw), file, "storage.assemble.outAt");
        var dir = ConstructParts.Str(ConstructParts.Need(b, "outDir", bw), file, "storage.assemble.outDir");
        d.AsmOutDir = dir switch
        {
            "+x" => Vector3.UnitX,
            "-x" => -Vector3.UnitX,
            "+z" => Vector3.UnitZ,
            "-z" => -Vector3.UnitZ,
            _ => throw new ConstructPartException($"{file}: storage.assemble.outDir is level: \"+x\", \"-x\", \"+z\" or \"-z\""),
        };
        if (ConstructParts.Opt(b, "machine") is { } mc)
            d.AsmMachine = AssemblingTable.MachineOf(ConstructParts.Str(mc, file, "storage.assemble.machine"), file);
        d.IsAssembler = true;
        d.DataIn = [ConstructGatherer.RunPort];
    }
}

/// <summary>The world's side of an Assembler (ConstructAssembling.cs): what goes into its input and tray.</summary>
public sealed partial class ConstructWorld
{
    public int AssembleTicks; // ticks since the last assembling step

    /// <summary>Server: a part goes on the tray (the caller saw room).</summary>
    internal void TrayAdd(CStore st, ConstructPartDef d, ConstructPartDef item)
    {
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] == item.Id && st.Count[k] < ConstructStorage.StackOf(d.Slots[k], item, st.Rot[k]))
            {
                st.Count[k]++;
                Changed(st);
                return;
            }
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] == ConstructParts.None && ConstructStorage.Fits(d.Slots[k], item, out var rot))
            {
                st.Def[k] = item.Id;
                st.Rot[k] = rot;
                st.Count[k] = 1;
                st.Health[k] = 1f;
                Changed(st);
                return;
            }
    }

    /// <summary>Server: an Assembler's input gains (or, negative, loses) parts of one kind.</summary>
    internal void AddInputPart(CStore st, ushort part, int n)
    {
        st.AddKind(part, n);
        LoadChanged = true;
        Changed(st);
    }

    /// <summary>Server: a material for an Assembler (a chute, or F); how much it took. One its recipe doesn't use stays in the source.</summary>
    public int FeedAssembler(Construct c, int part, int amount, byte material)
    {
        if (StoreOf(c, part) is not { } st || !c.DefOf(part).IsAssembler || amount <= 0 || material == 0)
            return 0;
        var d = c.DefOf(part);
        var r = ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[part], d);
        if (r == null || r.MinTier > d.Tier || r.UnitsOf(material) == 0 || !ConstructAssembling.Takes(c, part, r))
        {
            NoteRefused(st, material);
            return 0;
        }
        var n = Math.Min(amount, ConstructAssembling.RoomFor(d, st, r, material, ConstructAssembling.FitOf(c, part), ConstructAssembling.OrderedBatches(c.Parts[part], r)));
        if (n <= 0)
            return 0;
        AddInput(st, material, n);
        return n;
    }

    /// <summary>Server: a part for an Assembler (a chute, F, held). True when it went in. One its recipe doesn't use stays in the source.</summary>
    public bool FeedPart(Construct c, int part, ConstructPartDef item)
    {
        if (StoreOf(c, part) is not { } st || !c.DefOf(part).IsAssembler)
            return false;
        var d = c.DefOf(part);
        var r = ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[part], d);
        if (r == null || r.MinTier > d.Tier || r.CountOf(item.Id) == 0 || !ConstructAssembling.Takes(c, part, r))
        {
            NoteRefusedPart(st, item.Id);
            return false;
        }
        if (ConstructAssembling.PartRoom(c.Parts[part], d, st, r, item.Id, ConstructAssembling.FitOf(c, part)) <= 0)
            return false;
        AddInputPart(st, item.Id, 1);
        return true;
    }

    /// <summary>
    /// Server, a conveyor's delivery: a part one of its queued orders (not the head's) uses, into its input while that order's recipe has
    /// room. Parts ride to an Assembler first in, first out on its one Piece in, so a part for a later order at the front (a Copper Coil
    /// for a Servo Motor) refused while the head (a Differential) waited for what rode behind it (its Gear Sets) stopped the machine for
    /// good: the Factory Showcase, 2026-11-14. Input parts are kept across recipes, so nothing is lost.
    /// </summary>
    public bool FeedQueuedPart(Construct c, int part, ConstructPartDef item)
    {
        if (StoreOf(c, part) is not { } st || c.DefOf(part) is not { IsAssembler: true } d || c.Parts[part].Orders is not { Bill: false, Count: > 1 } q)
            return false;
        var table = AssemblingTable.Current;
        for (var k = 1; k < q.Count; k++)
            if (!q.Items[k].IsPiece && table.Of(q.Items[k].Part) is { } r && r.Machine == d.AsmMachine && r.MinTier <= d.Tier && r.CountOf(item.Id) > 0
                && ConstructAssembling.Takes(c, part, r) && ConstructAssembling.PartRoom(c.Parts[part], d, st, r, item.Id, ConstructAssembling.FitOf(c, part)) > 0)
            {
                AddInputPart(st, item.Id, 1);
                return true;
            }
        return false;
    }

    /// <summary>
    /// Does any order queued at this Assembler (its head too) use <paramref name="item"/>? A Router sends it a part tagged for a closed
    /// order while one still does (pooled inputs, 2026-11-14: Bearings made for one order arrived after another had used its own, and
    /// were set down loose beside the Router).
    /// </summary>
    public bool UsedByQueue(Construct c, int part, ushort item)
    {
        if (c.DefOf(part) is not { IsAssembler: true } d || c.Parts[part].Orders is not { Bill: false, Count: > 0 } q)
            return false;
        var table = AssemblingTable.Current;
        for (var k = 0; k < q.Count; k++)
            if (!q.Items[k].IsPiece && table.Of(q.Items[k].Part) is { } r && r.Machine == d.AsmMachine && r.CountOf(item) > 0)
                return true;
        return false;
    }

    /// <summary>Would this Assembler take a part now? (For a chute looking through its source: nothing is noted.)</summary>
    /// <summary>
    /// Does this Assembler use <paramref name="item"/>: its recipe setting or an order queued at it? A piece rolling across its bed that it
    /// doesn't goes on to the bed's out run (ConstructConveyor.Deliver); one it does waits at its bed for room. Allocates nothing.
    /// </summary>
    public bool Keeps(Construct c, int part, ushort item)
    {
        var d = c.DefOf(part);
        return d.IsAssembler && (UsedByQueue(c, part, item) || ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[part], d) is { } r && r.CountOf(item) > 0);
    }

    public bool WantsPart(Construct c, int part, ushort item)
    {
        if (StoreOf(c, part) is not { } st)
            return false;
        var d = c.DefOf(part);
        return ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[part], d) is { } r && r.MinTier <= d.Tier && r.CountOf(item) > 0 && ConstructAssembling.Takes(c, part, r)
            && ConstructAssembling.PartRoom(c.Parts[part], d, st, r, item, ConstructAssembling.FitOf(c, part)) > 0;
    }
}
