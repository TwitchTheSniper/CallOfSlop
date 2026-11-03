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
}

/// <summary>Why a finished batch can't leave an Assembler (<see cref="CStore.Blocked"/>).</summary>
public enum AsmBlock : byte
{
    None,
    OutputBlocked, // something stands where a body leaves
    WorldFull, // no room for another construct
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

    public static void Step(ConstructWorld world, IWorldQuery query)
    {
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

    /// <summary>The recipe a part has chosen, or null.</summary>
    public static AssembleRecipe? RecipeOf(AssemblingTable table, in CPart p, ConstructPartDef d)
    {
        var k = (int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0) - 1;
        return k >= 0 && k < table.Recipes.Length ? table.Recipes[k] : null;
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
        var id = recipe == null ? 0 : Array.IndexOf(table.Recipes, recipe) + 1;
        if (st.JobAmount != id)
        {
            st.JobAmount = id; // (a new recipe starts its batch afresh)
            st.JobTicks = 0;
            SetBlocked(world, st, AsmBlock.None);
        }
        var held = ConstructGatherer.HeldOff(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        var state = StateOf(d, st, recipe);
        if (state is not (AsmState.Ready or AsmState.Blocked))
        {
            st.JobTicks = 0;
            SetBlocked(world, st, AsmBlock.None);
        }
        if (held || state is not (AsmState.Ready or AsmState.Blocked) || !ConstructRecycler.Powered(world, c, part))
        {
            ConstructGatherer.SetBusy(world, st, false);
            return;
        }
        if (state == AsmState.Blocked)
        {
            // Its batch is done and waits: it draws nothing, and looks again now and then.
            ConstructGatherer.SetBusy(world, st, false);
            if (++st.JobMaterial >= RetrySteps)
            {
                st.JobMaterial = 0;
                Finish(world, query, c, part, st, recipe!);
            }
            return;
        }
        if (world.CreativeRecycling)
        {
            ConstructGatherer.SetBusy(world, st, false);
            Finish(world, query, c, part, st, recipe!);
            return;
        }
        ConstructGatherer.SetBusy(world, st, true);
        st.JobTicks += d.RefineWork;
        var need = recipe!.Ticks * Milli;
        if (st.JobTicks < need)
            return;
        st.JobTicks -= need; // (what is left over carries into the next batch)
        Finish(world, query, c, part, st, recipe);
    }

    private static void SetBlocked(ConstructWorld world, CStore st, AsmBlock why)
    {
        if (st.Blocked == (byte)why)
            return;
        st.Blocked = (byte)why;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    /// <summary>A batch is done: its output leaves, then its inputs go. A body that can't leave keeps them and waits.</summary>
    private static void Finish(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, AssembleRecipe recipe)
    {
        var d = c.Parts[part].D;
        var dest = OutputOf(d, st, recipe);
        if (dest == AsmOut.TrayFull)
            return; // (creative, or a chute took less than the state saw: it waits)
        if (dest == AsmOut.Body)
        {
            for (var n = 0; n < recipe.Count; n++)
            {
                var why = Leave(world, query, c, part, recipe.Output);
                if (why == AsmBlock.None)
                    continue;
                SetBlocked(world, st, why);
                return;
            }
        }
        else
            for (var n = 0; n < recipe.Count; n++)
                world.TrayAdd(st, d, recipe.Output);
        SetBlocked(world, st, AsmBlock.None);
        foreach (var (m, units) in recipe.Materials)
            world.AddInput(st, m, -units);
        foreach (var (p, count) in recipe.Parts)
            world.AddInputPart(st, p.Id, -count);
    }

    /// <summary>A body leaves at the output point, on its far side and level, its near side at the point. Why not, or None.</summary>
    private static AsmBlock Leave(ConstructWorld world, IWorldQuery query, Construct c, int part, ConstructPartDef def)
    {
        if (!world.HasRoom())
            return AsmBlock.WorldFull;
        ref var p = ref c.Parts[part];
        var d = p.D;
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
            if (ConstructPlace.Fits(world, query, def, fp, feet) != null)
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

    /// <summary>Where a recipe's output goes as the tray stands.</summary>
    public static AsmOut OutputOf(ConstructPartDef d, CStore st, AssembleRecipe r)
    {
        if (r.Output.PhysicsOnly)
            return AsmOut.Body;
        var fits = false;
        var room = 0;
        for (var k = 0; k < d.Slots.Length && k < st.SlotCount; k++)
        {
            ref readonly var s = ref d.Slots[k];
            if (!ConstructStorage.Fits(s, r.Output, out _))
                continue;
            fits = true;
            room += st.Def[k] == ConstructParts.None ? s.Stack : st.Def[k] == r.Output.Id ? s.Stack - st.Count[k] : 0;
        }
        return !fits ? AsmOut.Body : room >= r.Count ? AsmOut.Tray : AsmOut.TrayFull;
    }

    /// <summary>How much of a material the input takes for a recipe (the buffer's limit).</summary>
    public static int RoomFor(ConstructPartDef d, CStore st, AssembleRecipe r, byte material) => r.UnitsOf(material) * d.AsmBatches - st.MixOf(material);

    /// <summary>How many of a part the input takes for a recipe.</summary>
    public static int RoomFor(ConstructPartDef d, CStore st, AssembleRecipe r, ushort part)
    {
        var k = st.KindIndex(part);
        return r.CountOf(part) * d.AsmBatches - (k < 0 ? 0 : st.KindCount![k]);
    }

    /// <summary>Where a machine stands with what it holds.</summary>
    public static AsmState StateOf(ConstructPartDef d, CStore st, AssembleRecipe? r)
    {
        if (r == null)
            return AsmState.NoRecipe;
        if (r.MinTier > d.Tier)
            return AsmState.TierLow;
        if (st.Blocked != 0)
            return AsmState.Blocked;
        if (MissingOf(st, r, out _, out _))
            return AsmState.Short;
        return OutputOf(d, st, r) == AsmOut.TrayFull ? AsmState.OutputFull : AsmState.Ready;
    }

    /// <summary>The first input short of a batch (the material, or the part with <paramref name="part"/> set), if any.</summary>
    private static bool MissingOf(CStore st, AssembleRecipe r, out byte material, out ConstructPartDef? part)
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
    public static string Status(ConstructPartDef d, CStore st, AssembleRecipe? r)
    {
        var names = RecyclingTable.Current;
        var doing = st.Held ? Program.HeldLook
            : st.Busy ? $" - BUILDING {r?.Name}"
            : StateOf(d, st, r) switch
            {
                AsmState.NoRecipe => " - no recipe: choose one with the configure tool",
                AsmState.TierLow => $" - blocked: the {r!.Name} needs a tier {r.MinTier} assembler, this is tier {d.Tier}",
                AsmState.Short => MissingOf(st, r!, out var m, out var p) ? $" - needs {(p != null ? $"{r!.CountOf(p.Id)} {p.Name}" : $"{r!.UnitsOf(m)} {names.NameOf(m)}")} (a batch: {r!.Batch})" : "",
                AsmState.OutputFull => " - blocked: its output tray is full",
                AsmState.Blocked => st.Blocked == (byte)AsmBlock.WorldFull ? " - blocked: the world is full of constructs" : " - blocked: something is in the way of its output point",
                _ => " - no power: it needs a battery with charge",
            };
        if (st.RefusedPart != ConstructParts.None && ConstructParts.Get(st.RefusedPart) is { } rp)
            return $"{doing} - chute refused: the {rp.Name} stays in the source, {RefusedWhy(r)}";
        return st.Refused != 0 ? $"{doing} - chute refused: {names.NameOf(st.Refused)} stays in the source, {RefusedWhy(r)}" : doing;
    }

    private static string RefusedWhy(AssembleRecipe? r) => r == null ? "choose a recipe first" : $"the {r.Name} takes {r.Batch}";

    /// <summary>The whole look line: its recipe, what it holds, what it is doing, what F would do.</summary>
    public static string Look(ConstructPartDef d, CStore st, AssembleRecipe? r, bool near, SandboxInventory inv, ConstructPartDef? held)
    {
        var names = RecyclingTable.Current;
        var tip = TipMaterial(inv, d, st, r);
        var feeds = held != null && r != null && r.MinTier <= d.Tier && RoomFor(d, st, r, held.Id) > 0;
        var item = TopItem(st);
        var act = !near ? ""
            : feeds ? $" - F: put in the {held!.Name}"
            : tip != 0 ? $" - F: tip in your {Math.Min((int)inv.Materials[tip], RoomFor(d, st, r!, tip))} {names.NameOf(tip)}"
            : item != null ? $" - F: take the {item.Name}"
            : st.MixN > 0 || st.KindN > 0 ? " - F: take back its input"
            : "";
        var name = r == null ? d.Name : $"{d.Name} ({r.Name})";
        return $"{name}: in {Holds(st)}, tray {st.Items}{Status(d, st, r)}{act}";
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
        ConstructParts.Known(b, ["min", "max", "capacity", "speed", "chuteLoad", "batches", "outAt", "outDir"], bw);
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
            if (st.Def[k] == item.Id && st.Count[k] < d.Slots[k].Stack)
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
        if (r == null || r.MinTier > d.Tier || r.UnitsOf(material) == 0)
        {
            NoteRefused(st, material);
            return 0;
        }
        var n = Math.Min(amount, ConstructAssembling.RoomFor(d, st, r, material));
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
        if (r == null || r.MinTier > d.Tier || r.CountOf(item.Id) == 0)
        {
            NoteRefusedPart(st, item.Id);
            return false;
        }
        if (ConstructAssembling.RoomFor(d, st, r, item.Id) <= 0)
            return false;
        AddInputPart(st, item.Id, 1);
        return true;
    }

    /// <summary>Would this Assembler take a part now? (For a chute looking through its source: nothing is noted.)</summary>
    public bool WantsPart(Construct c, int part, ushort item)
    {
        if (StoreOf(c, part) is not { } st)
            return false;
        var d = c.DefOf(part);
        return ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[part], d) is { } r && r.MinTier <= d.Tier && r.CountOf(item) > 0 && ConstructAssembling.RoomFor(d, st, r, item) > 0;
    }
}
