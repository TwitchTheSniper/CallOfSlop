namespace CallOfSlop;

/// <summary>
/// One stock target (smart logistics phase 3, design section 6): store <see cref="Store"/> on body <see cref="Body"/> keeps its
/// <c>storeKeep</c> setting's count of <see cref="Kind"/>. Server only; saved in the body's LOGI block (its fourth part).
/// </summary>
public struct StockTarget
{
    public ushort Body, Store; // the body, and the rack's store handle
    public ushort Kind; // the part kind it keeps (a run-time piece's id is this machine's own)
    public ushort Entry; // its open chain entry (0: none)
    public ushort NextStep; // the plan step from which it may open the next one (5 s after the last closed)
}

/// <summary>
/// Stock targets and the Logistics settings (design sections 6 and 7). A plain Piece Rack's <c>storeKeep</c> setting (the quick
/// "Keep what it holds") makes a target of each kind it holds when the setting is first seen above 0, at most <see cref="PerStore"/>;
/// a target is a standing order owned by its store: when it holds no more than its low mark (keep less a quarter, at least one) and
/// nothing is coming, it asks one maker for the rest (room allowing), as a chained order's link (<see cref="ChainFlags.Stock"/>) that
/// ranks below every order, so a refill never starves a build. It never takes stock from other stores: it only makes. Setting the
/// count to 0 ends its targets (an unstarted order leaves its maker). Checked with the crafting step (2 Hz), only while targets exist.
/// </summary>
public static class StockTargets
{
    public const int MaxTargets = 256, PerStore = 8, MaxKeep = 200;
    public const int ScanTicks = 2 * World.TickRate; // ticks between looks for racks whose setting changed (2 s; ConstructCrafting.Step)
    private const int CooldownSteps = 10; // plan steps after a refill closes before the next may open (5 s)
    public const int TakesAnything = 0, TakesHeld = 1, TakesNothing = 2;
    public const int SpillAuto = 0;

    private static readonly string[] TakesNames = ["Anything", "Only what it holds now", "Nothing (orders and what it keeps)"];
    private static readonly string[] SpillNames = ["Auto (storage)", "Ahead", "Left", "Right"];
    private static readonly string[] PriorityNames = ["Low", "Normal", "High"];

    /// <summary>A store a target or a filter can be set on: a plain Piece Rack (not a terminal's tray).</summary>
    public static bool IsStore(ConstructPartDef d) => d.IsPieceRack && !d.IsCraftTerminal;

    /// <summary>The Logistics group of settings, for the parts that read them (after every other setting).</summary>
    public static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        void Add(CfgKey k, CfgType type, string label, float min, float max, float dflt, string tip, string[]? options = null)
        {
            if (list.Count < ConstructConfig.MaxSlots && !list.Exists(e => e.Id == k))
                list.Add(new CConfigDef { Key = ConstructConfig.KeyName(k), Id = k, Type = type, Label = label, Group = "Logistics", Min = min, Max = max, Step = 1, Default = dflt,
                    Options = options ?? [], Tooltip = tip });
        }
        if (d.IsRouter && d.IsConveyorJunction)
        {
            Add(CfgKey.RouterSpill, CfgType.Choice, "Unordered pieces go", 0, 3, 0, "Where a piece no order wants goes: Auto sends it to storage by the network; a way pins it to that lane.", SpillNames);
            Add(CfgKey.RouterHold, CfgType.Toggle, "Hold pieces", 0, 1, 0, "On: a piece that can't reach its order waits here instead of going to storage (a line that must never mix).");
        }
        if (IsStore(d))
        {
            Add(CfgKey.StoreKeep, CfgType.Number, "Keep what it holds", 0, MaxKeep, 0, "Keeps this many of each kind it holds now (up to 8 kinds): the network makes more when it runs low. 0: off.");
            Add(CfgKey.StorePriority, CfgType.Number, "Store priority", 1, 5, 3, "Pieces for storage go to the store with the highest priority first, then the nearest.");
            Add(CfgKey.StoreTakes, CfgType.Choice, "Takes", 0, 2, 0, "What it takes as storage. Orders and what it keeps always come in.", TakesNames);
        }
        if (d.IsCraftTerminal)
            Add(CfgKey.FeedPriority, CfgType.Choice, "Order priority", 0, 2, 1, "Its orders' class: High orders take shared parts and machines first, then the oldest order.", PriorityNames);
    }

    public static int Keep(Construct c, int part) => (int)ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.StoreKeep, 0);

    public static int Priority(Construct c, int part) => (int)ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.StorePriority, 3);

    public static int Takes(Construct c, int part) => (int)ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.StoreTakes, TakesAnything);

    public static int Spill(Construct c, int part) => (int)ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.RouterSpill, SpillAuto);

    public static bool Hold(Construct c, int part) => ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.RouterHold, 0) >= 0.5f;

    /// <summary>A terminal's (or station's) order class (Low 0, Normal 1, High 2).</summary>
    public static byte FeedClass(Construct c, int part) =>
        (byte)Math.Clamp((int)ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.FeedPriority, ConstructLogisticsPlan.ClassNormal), 0, 2);

    /// <summary>The low mark: a target opens a refill at or below it (keep 20: 15; keep 6: 4).</summary>
    public static int Low(int keep) => keep - Math.Max(1, (keep + 3) / 4);

    /// <summary>Server: does store <paramref name="handle"/> on body <paramref name="c"/> keep kind <paramref name="kind"/>?</summary>
    public static bool Keeps(ConstructWorld world, Construct c, ushort handle, ushort kind)
    {
        for (var i = 0; i < world.StockTargetN; i++)
            if (world.StockTargets[i].Store == handle && world.StockTargets[i].Kind == kind && world.StockTargets[i].Body == c.Id)
                return true;
        return false;
    }

    /// <summary>
    /// Server, storage choice: may a piece of <paramref name="d"/> go to rack <paramref name="rack"/> as storage or overflow? Its filter
    /// (design 7.1), except that a store always takes what it keeps (7.2).
    /// </summary>
    public static bool Accepts(ConstructWorld world, Construct c, int rack, CStore st, ConstructPartDef d)
    {
        var takes = Takes(c, rack);
        if (takes == TakesAnything)
            return true;
        if (world.StockTargetN > 0 && Keeps(world, c, st.Handle, (ushort)d.Id))
            return true;
        return takes == TakesHeld && ConstructCrafting.Holds(st, (ushort)d.Id) > 0;
    }

    // ---- The step ---------------------------------------------------------------------------------------------------------

    /// <summary>Server, with the crafting step (2 Hz), after the terminals: each target's refill, then their links.</summary>
    public static void Step(ConstructWorld world)
    {
        var step = world.ChainStepNo;
        for (var i = 0; i < world.StockTargetN; i++)
        {
            ref var t = ref world.StockTargets[i];
            var c = world.Get(t.Body);
            var part = c == null ? -1 : ConstructLogistics.PartOfStore(c, t.Store);
            if (c == null || part < 0)
                continue; // (Scan removes it)
            if (t.Entry != 0 && !ConstructLogisticsPlan.Open(world, c, part, t.Entry))
            {
                t.Entry = 0; // (its refill closed: the next no sooner than 5 s on)
                t.NextStep = (ushort)(step + CooldownSteps);
            }
            if (t.Entry == 0 && ConstructLogisticsPlan.Refill(world, c, part, t.Kind, out var open))
                t.Entry = open.Id; // (a loaded world's refill: its link came back from the save, not its target's mark)
            if (t.Entry != 0 || (short)(step - t.NextStep) < 0 || world.StoreOf(c, part) is not { } st || ConstructParts.Get(t.Kind) is not { } d)
                continue;
            var keep = Keep(c, part);
            var have = ConstructCrafting.Holds(st, t.Kind);
            var riding = Riding(c, st.Handle, t.Kind, out var ridingAll);
            if (keep <= 0 || have + riding > Low(keep))
                continue;
            var room = c.DefOf(part).RackCount - st.ItemCount - ridingAll;
            var want = Math.Min(keep - have - riding, room);
            if (want <= 0 || ConstructConveyor.RackCheck(c, part, st, d) != 0 && have == 0 && st.ItemCount > 0)
                continue;
            t.Entry = ConstructLogisticsPlan.AskStock(world, c, part, t.Kind, want);
        }
        // Their links are served after every order (Serve per store, once: a store's targets are kept together).
        for (var i = 0; i < world.StockTargetN; i++)
        {
            ref readonly var t = ref world.StockTargets[i];
            if (i > 0 && world.StockTargets[i - 1].Body == t.Body && world.StockTargets[i - 1].Store == t.Store)
                continue;
            if (world.Get(t.Body) is { } c && ConstructLogistics.PartOfStore(c, t.Store) is var part and >= 0)
                ConstructLogisticsPlan.Serve(world, c, part);
        }
    }

    /// <summary>Pieces of <paramref name="kind"/> riding to store <paramref name="handle"/>; <paramref name="all"/>: of every kind.</summary>
    private static int Riding(Construct c, ushort handle, ushort kind, out int all)
    {
        var n = 0;
        all = 0;
        for (var i = 0; i < c.ConveyedCount; i++)
            if (c.Conveyed[i].Dest == handle)
            {
                all++;
                if (c.Conveyed[i].Def == kind)
                    n++;
            }
        return n;
    }

    /// <summary>Racks whose keep setting went above 0 get a target a kind they hold; targets whose rack went, or whose setting is 0, end.</summary>
    public static void Scan(ConstructWorld world)
    {
        for (var i = world.StockTargetN - 1; i >= 0; i--)
        {
            ref readonly var t = ref world.StockTargets[i];
            var c = world.Get(t.Body);
            var part = c == null ? -1 : ConstructLogistics.PartOfStore(c, t.Store);
            if (c != null && part >= 0 && IsStore(c.DefOf(part)) && Keep(c, part) > 0)
                continue;
            RemoveAt(world, i);
        }
        foreach (var c in world.Bodies)
            for (var p = 0; p < c.PartCount; p++)
            {
                if (!c.Parts[p].D.IsPieceRack || c.Parts[p].D.IsCraftTerminal || c.Parts[p].Store == 0 || Keep(c, p) <= 0)
                    continue;
                var handle = c.Parts[p].Store;
                if (Has(world, c.Id, handle) || world.StoreOf(c, p) is not { KindN: > 0 } st)
                    continue; // (an empty rack set to keep waits for its first piece)
                for (var k = 0; k < st.KindN && k < PerStore; k++)
                    Add(world, c.Id, handle, st.KindDef![k]);
            }
    }

    private static bool Has(ConstructWorld world, ushort body, ushort handle)
    {
        for (var i = 0; i < world.StockTargetN; i++)
            if (world.StockTargets[i].Body == body && world.StockTargets[i].Store == handle)
                return true;
        return false;
    }

    /// <summary>A target, kept beside its store's others (the step serves a store once).</summary>
    public static void Add(ConstructWorld world, ushort body, ushort handle, ushort kind)
    {
        if (world.StockTargetN >= MaxTargets || kind == 0 || kind == ConstructParts.None)
            return;
        var at = world.StockTargetN;
        for (var i = 0; i < world.StockTargetN; i++)
        {
            if (world.StockTargets[i].Body == body && world.StockTargets[i].Store == handle)
            {
                if (world.StockTargets[i].Kind == kind)
                    return;
                at = i + 1;
            }
        }
        if (at < world.StockTargetN)
            Array.Copy(world.StockTargets, at, world.StockTargets, at + 1, world.StockTargetN - at);
        world.StockTargets[at] = new StockTarget { Body = body, Store = handle, Kind = kind, NextStep = world.ChainStepNo };
        world.StockTargetN++;
    }

    private static void RemoveAt(ConstructWorld world, int i)
    {
        world.StockTargetN--;
        if (i < world.StockTargetN)
            Array.Copy(world.StockTargets, i + 1, world.StockTargets, i, world.StockTargetN - i);
        world.StockTargets[world.StockTargetN] = default; // (its open link ends at the next plan step: its root is gone)
    }

    // ---- Saves (the LOGI block's fourth part) ---------------------------------------------------------------------------

    /// <summary>A body's targets as (rack part, kind), for a save.</summary>
    public static (ushort Part, ushort Kind)[]? Snapshot(ConstructWorld world, Construct c)
    {
        List<(ushort, ushort)>? list = null;
        for (var i = 0; i < world.StockTargetN; i++)
            if (world.StockTargets[i].Body == c.Id && ConstructLogistics.PartOfStore(c, world.StockTargets[i].Store) is var p and >= 0)
                (list ??= []).Add(((ushort)p, world.StockTargets[i].Kind));
        return list?.ToArray();
    }

    /// <summary>A loaded body's targets into this world (by its parts' store handles here).</summary>
    public static void Apply(ConstructWorld world, Construct c, (ushort Part, ushort Kind)[] list)
    {
        foreach (var (p, kind) in list)
            if (p < c.PartCount && IsStore(c.DefOf(p)) && c.Parts[p].Store != 0)
                Add(world, c.Id, c.Parts[p].Store, kind);
    }

    // ---- What the player reads (either peer: from the settings) ---------------------------------------------------------

    /// <summary>A rack's Logistics line, or "": "keeps 6 of each kind it holds; takes only what it holds; priority 5".</summary>
    public static string Look(Construct c, int part)
    {
        var d = c.DefOf(part);
        if (!IsStore(d) || d.CfgSlot[(int)CfgKey.StoreKeep] < 0)
            return "";
        var keep = Keep(c, part);
        var takes = Takes(c, part);
        var prio = Priority(c, part);
        if (keep == 0 && takes == TakesAnything && prio == 3)
            return "";
        var parts = new List<string>(3);
        if (keep > 0)
            parts.Add($"keeps {keep} of each kind it holds (refills at {Low(keep)})");
        if (takes == TakesHeld)
            parts.Add("takes only what it holds");
        else if (takes == TakesNothing)
            parts.Add("takes nothing as storage");
        if (prio != 3)
            parts.Add($"store priority {prio}");
        return string.Join("; ", parts);
    }

    /// <summary>What changes a rack's Logistics line (the look line's cache key).</summary>
    public static long LookKey(Construct c, int part) =>
        IsStore(c.DefOf(part)) && c.DefOf(part).CfgSlot[(int)CfgKey.StoreKeep] >= 0 ? Keep(c, part) * 64L + Takes(c, part) * 8L + Priority(c, part) : 0;
}

public sealed partial class ConstructWorld
{
    public readonly StockTarget[] StockTargets = new StockTarget[global::CallOfSlop.StockTargets.MaxTargets]; // (server) stores' kept kinds, a store's together
    public int StockTargetN;
    public int StockScan;
}
