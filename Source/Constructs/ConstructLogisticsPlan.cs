namespace CallOfSlop;

/// <summary>
/// One link of a chained order (docs/sandbox/constructs/production.md "Smart logistics", phase 2): machine <see cref="Maker"/> was asked
/// for <see cref="Asked"/> of <see cref="Kind"/> for machine <see cref="Dest"/>, because terminal <see cref="Root"/>'s order
/// <see cref="RootId"/> asked <see cref="Dest"/> for a part whose recipe needs it. Server only; saved in a body's LOGI block.
/// </summary>
public struct ChainEntry
{
    public ushort Id; // never 0: a rider's Tag when it rides to Dest
    public ushort Body;
    public ushort Maker; // the part asked to make it (NoPart: nobody yet: Reason says why)
    public ushort Kind; // the part's id
    public ushort Asked, Made; // asked of the maker; arrived at Dest
    public ushort Dest; // the machine that needs it (an Assembler)
    public ushort Root, RootId; // the terminal and its order
    public string Reason; // why nobody makes it ("" while a maker is on it)
}

/// <summary>
/// Orders pull their inputs, one level (phase 2): when a terminal's order is queued at an Assembler whose recipe needs parts, each part
/// the Assembler doesn't hold is asked of another machine on the network (a <see cref="ChainEntry"/> and a <see cref="WorkOrder"/>
/// marked with its <see cref="WorkOrder.Chain"/>). The made part rides to the Assembler's Piece in (its destination is the entry:
/// <see cref="ConstructLogistics.AtSource"/>); a link nobody makes waits and names why in the order's row. A 2 Hz step (with the
/// terminals') keeps entries honest: a maker that went or dropped the order is replaced, a cancelled or finished root's entries go.
/// Loose material is not ordered (the user, Q3: kept by levels); a chained machine's material comes by its chutes as today.
/// </summary>
public static class ConstructLogisticsPlan
{
    public const int MaxEntries = 256;
    public const ushort NoPart = ushort.MaxValue;

    // ---- Asking ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server: terminal <paramref name="t"/>'s order <paramref name="o"/> was just queued for <paramref name="ask"/> at machine
    /// <paramref name="m"/>: the parts its recipe needs that <paramref name="m"/> doesn't hold or already has coming are asked of others.
    /// </summary>
    public static void Asked(ConstructWorld world, Construct c, CraftNet net, int t, in CraftOrder o, int m, int ask)
    {
        var d = c.DefOf(m);
        if (!d.IsAssembler || o.What.IsPiece || o.What.Name != null || AssemblingTable.Current.Of(o.What.Part) is not { Parts.Length: > 0 } r
            || world.StoreOf(c, m) is not { } st)
            return;
        var batches = (ask + r.Count - 1) / r.Count;
        foreach (var (part, n) in r.Parts)
        {
            var coming = 0;
            for (var i = 0; i < world.ChainCount; i++)
                if (world.Chains[i].Body == c.Id && world.Chains[i].Dest == m && world.Chains[i].Kind == part.Id)
                    coming += world.Chains[i].Asked - world.Chains[i].Made;
            var shortBy = n * batches - ConstructCrafting.Holds(st, part.Id) - coming; // (one root a machine at a time: its head order uses what it holds)
            if (shortBy <= 0 || world.ChainCount >= MaxEntries)
                continue;
            ref var e = ref world.Chains[world.ChainCount++];
            e = new ChainEntry
            {
                Id = NextId(world), Body = c.Id, Maker = NoPart, Kind = (ushort)part.Id, Asked = (ushort)Math.Min(shortBy, WorkOrders.MaxCount),
                Dest = (ushort)m, Root = (ushort)t, RootId = o.Id, Reason = "",
            };
            FindMaker(world, c, net, ref e, 0);
        }
    }

    private static ushort NextId(ConstructWorld world)
    {
        for (var tries = 0; tries < 65535; tries++)
        {
            var id = world.NextChainId++;
            if (world.NextChainId == 0)
                world.NextChainId = 1;
            if (id == 0)
                continue;
            var used = false;
            for (var i = 0; i < world.ChainCount && !used; i++)
                used = world.Chains[i].Id == id;
            if (!used)
                return id;
        }
        return 1;
    }

    /// <summary>A machine on the terminal's network (not the one that needs it) that takes the order: queued there, marked with the entry.</summary>
    private static void FindMaker(ConstructWorld world, Construct c, CraftNet net, ref ChainEntry e, int riding)
    {
        var what = new WorkOrder { Part = e.Kind, Count = (ushort)Math.Max(1, e.Asked - e.Made - riding), Chain = e.Id };
        string? refusal = null;
        for (var i = 0; i < net.MachineN; i++)
        {
            var mk = net.Machines[i];
            if (mk == e.Dest || !c.DefOf(mk).IsAssembler)
                continue;
            var why = WorkOrders.Queue(world, c, mk, what);
            if (why == null)
            {
                e.Maker = (ushort)mk;
                e.Reason = "";
                return;
            }
            refusal ??= why;
        }
        var name = ConstructParts.Get(e.Kind)?.Name ?? "part";
        var reason = refusal != null ? $"waiting on {name}: {refusal}" : $"waiting on {name}: nothing on the network makes it";
        if (e.Reason != reason)
            e.Reason = reason; // (made only when it changes)
    }

    // ---- The step (2 Hz, with the terminals) -----------------------------------------------------------------------------

    /// <summary>Server: every entry kept honest; entries whose root went or that are complete leave the table.</summary>
    public static void Step(ConstructWorld world)
    {
        for (var i = world.ChainCount - 1; i >= 0; i--)
        {
            ref var e = ref world.Chains[i];
            var c = world.Get(e.Body);
            var gone = c == null || e.Root >= c.PartCount || e.Dest >= c.PartCount || e.Made >= e.Asked || c.Parts[e.Root].Craft is not { } q || RootClosed(q, e.RootId);
            if (gone)
            {
                if (c != null)
                    Unqueue(world, c, e);
                Remove(world, i);
                continue;
            }
            var riding = Riding(c!, e);
            if (e.Maker != NoPart && (e.Maker >= c!.PartCount || !QueuedAt(c, e.Maker, e.Id)) && riding + e.Made < e.Asked)
                e.Maker = NoPart; // (its maker went, or the order left its queue: the player pressed Clear; asked again below)
            if (e.Maker == NoPart && c!.Parts[e.Root].D.IsCraftTerminal)
                FindMaker(world, c, ConstructCrafting.NetOf(world, c, e.Root), ref e, riding);
        }
    }

    private static bool RootClosed(CraftList q, ushort id) => q.Find(id) is var k && (k < 0 || q.Items[k].Cancelled || q.Items[k].Done);

    private static bool QueuedAt(Construct c, int maker, ushort id)
    {
        if (c.Parts[maker].Orders is not { Bill: false } q)
            return false;
        for (var k = 0; k < q.Count; k++)
            if (q.Items[k].Chain == id)
                return true;
        return false;
    }

    private static int Riding(Construct c, in ChainEntry e)
    {
        var h = c.Parts[e.Dest].Store;
        var n = 0;
        for (var i = 0; i < c.ConveyedCount; i++)
            if (c.Conveyed[i].Dest == h && c.Conveyed[i].Tag == e.Id)
                n++;
        return n;
    }

    /// <summary>An entry's work order leaves its maker's queue if nothing of it is made yet (a started one finishes its piece).</summary>
    private static void Unqueue(ConstructWorld world, Construct c, in ChainEntry e)
    {
        if (e.Maker >= c.PartCount || c.Parts[e.Maker].Orders is not { Bill: false } q)
            return;
        for (var k = q.Count - 1; k >= 0; k--)
            if (q.Items[k].Chain == e.Id && q.Items[k].Made == 0 && k > 0) // (the head may be on the die: it finishes, and its piece goes to storage)
            {
                q.RemoveAt(k);
                world.OrdersChanged(c);
            }
    }

    private static void Remove(ConstructWorld world, int i)
    {
        world.ChainCount--;
        if (i < world.ChainCount)
            Array.Copy(world.Chains, i + 1, world.Chains, i, world.ChainCount - i);
        world.Chains[world.ChainCount] = default;
    }

    /// <summary>Server: terminal <paramref name="t"/>'s order cancelled: its entries go, and their unstarted orders leave their makers.</summary>
    public static void Cancelled(ConstructWorld world, Construct c, int t, ushort id)
    {
        for (var i = world.ChainCount - 1; i >= 0; i--)
            if (world.Chains[i].Body == c.Id && world.Chains[i].Root == t && world.Chains[i].RootId == id)
            {
                Unqueue(world, c, world.Chains[i]);
                Remove(world, i);
            }
    }

    // ---- Riding and arriving ---------------------------------------------------------------------------------------------

    /// <summary>The entry a piece of kind <paramref name="d"/> could serve at machine <paramref name="dest"/> (from <paramref name="maker"/> first): its id, or 0.</summary>
    public static ushort WantAt(ConstructWorld world, Construct c, int dest, ConstructPartDef d, int maker)
    {
        ushort any = 0;
        for (var i = 0; i < world.ChainCount; i++)
        {
            ref readonly var e = ref world.Chains[i];
            if (e.Body != c.Id || e.Dest != dest || e.Kind != d.Id || e.Made + Riding(c, e) >= e.Asked)
                continue;
            if (maker < 0 || e.Maker == maker)
                return e.Id;
            if (any == 0)
                any = e.Id;
        }
        return any;
    }

    /// <summary>A machine's piece leaving it: the entry whose maker it is (its oldest open one of that kind), else 0; and where it goes.</summary>
    public static ushort FromMaker(ConstructWorld world, Construct c, int maker, ConstructPartDef d, out int dest)
    {
        dest = -1;
        for (var i = 0; i < world.ChainCount; i++)
        {
            ref readonly var e = ref world.Chains[i];
            if (e.Body == c.Id && e.Maker == maker && e.Kind == d.Id && e.Dest < c.PartCount && e.Made + Riding(c, e) < e.Asked)
            {
                dest = e.Dest;
                return e.Id;
            }
        }
        return 0;
    }

    /// <summary>Is entry <paramref name="id"/> still open for machine <paramref name="dest"/>?</summary>
    public static bool Open(ConstructWorld world, Construct c, int dest, ushort id)
    {
        for (var i = 0; i < world.ChainCount; i++)
            if (world.Chains[i].Id == id)
                return world.Chains[i].Body == c.Id && world.Chains[i].Dest == dest;
        return false;
    }

    /// <summary>Server: a part arrived in machine <paramref name="dest"/>: it counts for its entry (the rider's tag), else the oldest open one there.</summary>
    public static void Arrived(ConstructWorld world, Construct c, int dest, ushort tag, ConstructPartDef d)
    {
        var at = -1;
        for (var i = 0; i < world.ChainCount && at < 0; i++)
            if (world.Chains[i].Id == tag && tag != 0)
                at = i;
        for (var i = 0; i < world.ChainCount && at < 0; i++)
            if (world.Chains[i].Body == c.Id && world.Chains[i].Dest == dest && world.Chains[i].Kind == d.Id && world.Chains[i].Made < world.Chains[i].Asked)
                at = i;
        if (at >= 0)
            world.Chains[at].Made++;
    }

    // ---- What the player reads -------------------------------------------------------------------------------------------

    /// <summary>The order's row: the first link nobody makes, else "Gear Set x2: Assembler making" for the first open one; null without entries.</summary>
    public static string? Note(ConstructWorld world, Construct c, int t, ushort id)
    {
        var first = -1;
        for (var i = 0; i < world.ChainCount; i++)
        {
            ref readonly var e = ref world.Chains[i];
            if (e.Body != c.Id || e.Root != t || e.RootId != id)
                continue;
            if (e.Maker == NoPart)
                return e.Reason;
            if (first < 0)
                first = i;
        }
        if (first < 0)
            return null;
        ref readonly var f = ref world.Chains[first];
        return $"{ConstructParts.Get(f.Kind)?.Name} {f.Made}/{f.Asked} from the {c.DefOf(f.Maker).Name}";
    }

    // ---- Saves (the LOGI block's second part) ----------------------------------------------------------------------------

    public static ChainEntry[]? Snapshot(ConstructWorld world, Construct c)
    {
        var n = 0;
        for (var i = 0; i < world.ChainCount; i++)
            if (world.Chains[i].Body == c.Id)
                n++;
        if (n == 0)
            return null;
        var list = new ChainEntry[n];
        n = 0;
        for (var i = 0; i < world.ChainCount; i++)
            if (world.Chains[i].Body == c.Id)
                list[n++] = world.Chains[i];
        return list;
    }

    /// <summary>A loaded body's entries into this world (their ids kept: riders carry them), and their makers' orders marked again.</summary>
    public static void Apply(ConstructWorld world, Construct c, ChainEntry[] list)
    {
        foreach (var e0 in list)
        {
            var e = e0 with { Body = c.Id, Reason = "" };
            if (world.ChainCount >= MaxEntries || e.Dest >= c.PartCount || e.Root >= c.PartCount)
                continue;
            if (e.Maker != NoPart && (e.Maker >= c.PartCount || c.Parts[e.Maker].Orders is not { Bill: false } q || !Mark(q, e)))
                e.Maker = NoPart; // (asked again at the next step)
            world.Chains[world.ChainCount++] = e;
            if (e.Id >= world.NextChainId)
                world.NextChainId = (ushort)(e.Id + 1 == 0 ? 1 : e.Id + 1);
        }
    }

    /// <summary>The maker's first unmarked order of the entry's kind takes its mark (a loaded queue is not asked for twice).</summary>
    private static bool Mark(OrderList q, in ChainEntry e)
    {
        for (var k = 0; k < q.Count; k++)
            if (q.Items[k].Part == e.Kind && (q.Items[k].Chain == 0 || q.Items[k].Chain == e.Id))
            {
                q.Items[k].Chain = e.Id;
                return true;
            }
        return false;
    }
}

public sealed partial class ConstructWorld
{
    public readonly ChainEntry[] Chains = new ChainEntry[ConstructLogisticsPlan.MaxEntries]; // chained orders' links (server)
    public int ChainCount;
    public ushort NextChainId = 1;
}
