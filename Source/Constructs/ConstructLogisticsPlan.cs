namespace CallOfSlop;

/// <summary>
/// One link of a chained order (docs/sandbox/constructs/production.md "Smart logistics", phase 2): machine <see cref="Maker"/> was asked
/// for <see cref="Asked"/> of <see cref="Kind"/> for machine <see cref="Dest"/>, because terminal <see cref="Root"/>'s order
/// <see cref="RootId"/> (at depth 1) or link <see cref="Parent"/> (deeper) asked <see cref="Dest"/> for a part whose recipe needs it.
/// Part of it may come from stock instead (claims owned by <see cref="Dest"/>'s store, id <see cref="Id"/>). Server only; saved in a
/// body's LOGI block.
/// </summary>
public struct ChainEntry
{
    public ushort Id; // never 0: a rider's Tag when it rides to Dest, and a claim's OwnerId
    public ushort Body;
    public ushort Maker; // the part asked to make it (NoPart: nobody, or stock covers it: Reason says which)
    public ushort Kind; // the part's id
    public ushort Asked, Made; // wanted at Dest (from stock and made); arrived at Dest
    public ushort Dest; // the machine that needs it (an Assembler)
    public ushort Root, RootId; // the terminal (or Building Station) and its order
    public ushort Parent; // the link whose maker is Dest (0: the root's own machine)
    public byte Depth; // 1: an input of the root's machine; 2: an input of that input's maker; ... at most MaxDepth
    public byte Class; // the root's priority class (Normal today; stock targets will be below Low, phase 3)
    public uint Stamp; // the root's age stamp (smaller: older)
    public byte Flags; // ChainFlags
    public ushort Born; // the plan step it was made in (its own inputs are asked from the next step: one level a step)
    public string Reason; // why nobody makes it ("" while a maker or stock is on it)
}

[Flags]
public enum ChainFlags : byte
{
    None = 0,
    Loop = 1, // its kind is one an ancestor (or the root) is made of: never asked
    Deep = 2, // deeper than MaxDepth: never asked
    Expanded = 4, // its own inputs were asked of others (cleared when its maker changes)
    Blocked = Loop | Deep,
}

/// <summary>
/// Orders pull their inputs (phase 2): when a terminal's order is queued at an Assembler whose recipe needs parts, each part the
/// Assembler doesn't hold or have coming becomes a <see cref="ChainEntry"/>. An entry takes free stock on the network first (claimed for
/// the Assembler and sent by conveyor, one piece a step), and the rest is asked of another machine (a <see cref="WorkOrder"/> marked with
/// its <see cref="WorkOrder.Chain"/>). The next step, that maker's own recipe inputs are asked the same way, one level a step, at most
/// <see cref="MaxDepth"/> levels below the root, never a kind an ancestor is made of (a recipe loop). Entries are kept in rank order
/// (priority class, then the root's age), so the oldest root's chain claims stock and takes made pieces first. The made part rides to
/// its destination's Piece in (<see cref="ConstructLogistics.AtSource"/>); a link nobody makes waits and names why in the order's row.
/// Loose material is not ordered (the user, Q3: kept by levels); a chained machine's material comes by its chutes as today.
/// </summary>
public static class ConstructLogisticsPlan
{
    public const int MaxEntries = 256, MaxDepth = 6, MaxLines = 6;
    private const int MaxInputs = 24; // (inputs a machine asks for: more than any recipe lists)
    public const ushort NoPart = ushort.MaxValue;
    public const byte ClassLow = 0, ClassNormal = 1, ClassHigh = 2; // (stock targets, phase 3, rank below Low)

    // ---- Asking ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server: terminal <paramref name="t"/>'s order <paramref name="o"/> was just queued for <paramref name="ask"/> at machine
    /// <paramref name="m"/>: the parts its recipe needs that <paramref name="m"/> doesn't hold or already has coming are asked for.
    /// </summary>
    public static void Asked(ConstructWorld world, Construct c, CraftNet net, int t, in CraftOrder o, int m, int ask)
    {
        if (o.What.IsPiece || o.What.Name != null && !o.What.IsPressed)
            return;
        var kind = o.What.IsPressed ? o.Def : o.What.Part; // (a pressed part's kind is its run-time def here: Place set it)
        if (kind == 0 || kind == ConstructParts.None)
            return;
        var root = new ChainEntry { Root = (ushort)t, RootId = o.Id, Class = ClassNormal, Stamp = o.Stamp, Depth = 0, Kind = kind };
        Need(world, c, net, m, kind, ask, root);
    }

    /// <summary>
    /// What a press needs to press <paramref name="ask"/> of <paramref name="product"/> (Press phase 4): its blank, one a part (a piece kind,
    /// an Extruder's), and its bay 2 kit when the kit is a part. Bulk kits are not ordered (the user, Q3): they come by chute. 0 inputs: not a press's.
    /// </summary>
    private static int PressInputs(Construct c, int m, CStore st, ushort product, int ask, Span<ushort> kinds, Span<int> counts, Span<int> held)
    {
        if (ConstructParts.Get(product) is not { } pd || ConstructPress.SizedDieOf(PressTable.Current, c, m) is not { } die)
            return 0;
        var key = Pressed.IsName(pd.Name) ? pd.Name : PressTable.Current.PresetKeyOf(pd);
        if (key == null || Pressed.SpecOf(key) is not { } spec || !ReferenceEquals(spec.Die, die) || Extrusion.Def(spec.Blank) is not { } blank)
            return 0;
        var n = 0;
        var inBays = 0;
        var md = c.DefOf(m);
        for (var b = 0; b < md.PressBays.Length; b++)
            if (b != ConstructPress.HopperBay && ConstructPress.PieceIn(c, m, b) is var i and >= 0 && (c.Parts[i].D.ExKey with { Growing = false }) == spec.Blank)
                inBays++;
        (kinds[n], counts[n], held[n]) = ((ushort)blank.Id, ask, inBays);
        n++;
        if (spec.Pattern.Bay2 is { Part: { } kit } amount && Pressed.Bay2For(spec.Pattern, spec.Blank) is var each and > 0)
        {
            (kinds[n], counts[n], held[n]) = ((ushort)kit.Id, each * ask, ConstructPress.KitHeld(st, amount));
            n++;
        }
        return n;
    }

    /// <summary>
    /// Machine <paramref name="m"/> makes <paramref name="ask"/> of <paramref name="product"/> for <paramref name="parent"/> (the root itself
    /// at depth 0): each recipe part it lacks becomes an entry under it. False when the table was full (asked again next step).
    /// </summary>
    private static bool Need(ConstructWorld world, Construct c, CraftNet net, int m, ushort product, int ask, in ChainEntry parent)
    {
        if (m >= c.PartCount || world.StoreOf(c, m) is not { } st)
            return true;
        // The inputs: an Assembler's recipe parts (whole batches), or a press's blank and part kit (Press phase 4).
        Span<ushort> kinds = stackalloc ushort[MaxInputs];
        Span<int> wants = stackalloc int[MaxInputs], inside = stackalloc int[MaxInputs];
        var inputs = 0;
        var md = c.DefOf(m);
        if (md.IsPress && !md.IsLathe)
            inputs = PressInputs(c, m, st, product, ask, kinds, wants, inside);
        else if (md.IsAssembler && AssemblingTable.Current.Of(product) is { Parts.Length: > 0 } r)
        {
            var batches = (ask + r.Count - 1) / r.Count;
            foreach (var (rp, rn) in r.Parts)
                if (inputs < kinds.Length)
                {
                    (kinds[inputs], wants[inputs], inside[inputs]) = ((ushort)rp.Id, rn * batches, ConstructCrafting.Holds(st, rp.Id));
                    inputs++;
                }
        }
        for (var j = 0; j < inputs; j++)
        {
            var part = ConstructParts.Get(kinds[j]);
            if (part == null)
                continue;
            var coming = 0;
            for (var i = 0; i < world.ChainCount; i++)
                if (world.Chains[i].Body == c.Id && world.Chains[i].Dest == m && world.Chains[i].Kind == part.Id)
                    coming += world.Chains[i].Asked - world.Chains[i].Made;
            var shortBy = wants[j] - inside[j] - coming; // (one root a machine at a time: its head order uses what it holds)
            if (shortBy <= 0)
                continue;
            if (world.ChainCount >= MaxEntries)
                return false;
            var e = new ChainEntry
            {
                Id = NextId(world), Body = c.Id, Maker = NoPart, Kind = (ushort)part.Id, Asked = (ushort)Math.Min(shortBy, WorkOrders.MaxCount),
                Dest = (ushort)m, Root = parent.Root, RootId = parent.RootId, Parent = parent.Depth == 0 ? (ushort)0 : parent.Id,
                Depth = (byte)(parent.Depth + 1), Class = parent.Class, Stamp = parent.Stamp, Born = world.ChainStepNo, Reason = "",
            };
            var name = part.Name;
            if (e.Depth > MaxDepth)
            {
                e.Flags = (byte)ChainFlags.Deep;
                e.Reason = BlockedText(e.Flags, name);
            }
            else if (Loops(world, c, parent, e.Kind))
            {
                e.Flags = (byte)ChainFlags.Loop;
                e.Reason = BlockedText(e.Flags, name);
            }
            var at = Insert(world, e);
            ref var ne = ref world.Chains[at];
            if (((ChainFlags)ne.Flags & ChainFlags.Blocked) != 0)
                continue;
            var left = ne.Asked - FromStock(world, c, net, ref ne, ne.Asked);
            if (left > 0)
                FindMaker(world, c, net, ref ne, left);
        }
        return true;
    }

    private static string BlockedText(byte flags, string name) =>
        ((ChainFlags)flags & ChainFlags.Loop) != 0 ? $"recipe loop: the {name} needs itself" : $"waiting on {name}: chain too deep ({MaxDepth} levels)";

    /// <summary>Is <paramref name="kind"/> the root's product or the kind of <paramref name="parent"/> or any of its ancestors?</summary>
    private static bool Loops(ConstructWorld world, Construct c, in ChainEntry parent, ushort kind)
    {
        if (parent.Depth == 0)
            return parent.Kind == kind;
        var at = Find(world, parent.Id);
        for (var hop = 0; hop <= MaxDepth && at >= 0; hop++)
        {
            ref readonly var a = ref world.Chains[at];
            if (a.Kind == kind)
                return true;
            if (a.Parent == 0)
                return RootKind(c, a.Root, a.RootId) == kind;
            at = Find(world, a.Parent);
        }
        return false;
    }

    private static ushort RootKind(Construct c, ushort t, ushort id) =>
        t < c.PartCount && c.Parts[t].Craft is { } q && q.Find(id) is var k and >= 0 ? q.Items[k].What.Part : ConstructParts.None;

    /// <summary>Puts an entry after every one of better or equal rank (class, then age): the table is in rank order. Its index.</summary>
    private static int Insert(ConstructWorld world, in ChainEntry e)
    {
        var at = world.ChainCount;
        while (at > 0 && Better(e, world.Chains[at - 1]))
            at--;
        if (at < world.ChainCount)
            Array.Copy(world.Chains, at, world.Chains, at + 1, world.ChainCount - at);
        world.Chains[at] = e;
        world.ChainCount++;
        return at;
    }

    /// <summary>Does <paramref name="a"/> outrank <paramref name="b"/>: a higher class, else an older root?</summary>
    public static bool Better(in ChainEntry a, in ChainEntry b) => a.Class != b.Class ? a.Class > b.Class : a.Stamp < b.Stamp;

    private static int Find(ConstructWorld world, ushort id)
    {
        for (var i = 0; i < world.ChainCount; i++)
            if (world.Chains[i].Id == id)
                return i;
        return -1;
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
            if (Find(world, id) < 0)
                return id;
        }
        return 1;
    }

    /// <summary>
    /// Stock before making: free stock of the entry's kind in the network's stores with a conveyor way to its destination is claimed for
    /// it (up to <paramref name="want"/>); the step sends it one piece at a time. How many were claimed.
    /// </summary>
    private static int FromStock(ConstructWorld world, Construct c, CraftNet net, ref ChainEntry e, int want)
    {
        var got = 0;
        var owner = c.Parts[e.Dest].Store;
        for (var s = 0; s < net.StoreN && got < want; s++)
        {
            if (world.StoreOf(c, net.Stores[s]) is not { } st || ConstructCrafting.Available(world, st, e.Kind) is not (var free and > 0))
                continue;
            if (ConstructCrafting.RouteTo(c, net.Stores[s], e.Dest) < 0)
                continue; // (no conveyor way to it: a chute into it brings what it wants by itself, as before)
            var take = Math.Min(want - got, free);
            if (world.Reserve.Add(st, e.Kind, take, owner, e.Id))
                got += take;
        }
        if (got > 0)
        {
            e.Reason = "";
            world.CraftChanged(c);
        }
        return got;
    }

    /// <summary>One claimed piece leaves its store for the entry's destination (one a step an entry, as a terminal's fetch).</summary>
    private static void SendOne(ConstructWorld world, Construct c, CraftNet net, in ChainEntry e)
    {
        var res = world.Reserve;
        var owner = c.Parts[e.Dest].Store;
        for (var i = 0; i < res.Count; i++)
        {
            ref var r = ref res.Items[i];
            if (r.Owner != owner || r.OwnerId != e.Id)
                continue;
            var sp = -1;
            for (var j = 0; j < net.StoreN && sp < 0; j++)
                if (c.Parts[net.Stores[j]].Store == r.Store)
                    sp = net.Stores[j];
            var st = world.StoreByHandle(r.Store);
            if (sp < 0 || st == null || ConstructCrafting.Holds(st, r.Def) <= 0 || ConstructParts.Get(r.Def) is not { } d)
            {
                res.Shrink(i, r.Count, st); // (its store left the network, or the piece was taken by hand: claimed again next step)
                world.CraftChanged(c);
                return;
            }
            var via = ConstructCrafting.RouteTo(c, sp, e.Dest);
            if (via < 0 || !ConstructConveyor.PutMade(world, c, via, d, 1, out _, owner, e.Id))
                return; // (the conveyor is busy, or its way went: tried again next step)
            if (!ConstructCrafting.TakeOne(world, c, sp, st, d))
                return;
            res.Shrink(i, 1, st);
            world.CraftChanged(c);
            return;
        }
    }

    /// <summary>The work order for a chained kind: a piece by its key (a press's blank), a pressed part by its key, else a catalogue part.</summary>
    private static WorkOrder OrderOf(ushort kind, int count)
    {
        var n = (ushort)Math.Clamp(count, 1, WorkOrders.MaxCount);
        if (Extrusion.IsRuntimeId(kind) && ConstructParts.Get(kind) is { } d)
        {
            if (d.IsExtrusion && d.ExKey.W > 0)
                return new WorkOrder { Key = d.ExKey with { Growing = false }, Part = ConstructParts.None, Count = n };
            if (Pressed.IsName(d.Name) && WorkOrders.PressedOrder(d.Name, n) is { } pressed)
                return pressed;
        }
        return new WorkOrder { Part = kind, Count = n };
    }

    /// <summary>
    /// A machine on the root's network (not the one that needs it) takes <paramref name="count"/>: one already making the kind first, else
    /// the shortest queue; queued there, marked with the entry. Nobody: the entry's reason.
    /// </summary>
    private static void FindMaker(ConstructWorld world, Construct c, CraftNet net, ref ChainEntry e, int count)
    {
        var what = OrderOf(e.Kind, count) with { Chain = e.Id };
        string? refusal = null;
        var best = -1;
        var bestScore = int.MaxValue;
        for (var i = 0; i < net.MachineN; i++)
        {
            var mk = net.Machines[i];
            if (mk == e.Dest || what.IsPiece != c.DefOf(mk).IsExtruder && !c.DefOf(mk).IsPress)
                continue; // (a piece is an Extruder's or a Former's; a part an Assembler's or a Press's: Refusal says which takes it)
            var why = WorkOrders.Refusal(world, c, mk, what);
            if (why != null)
            {
                refusal ??= why;
                continue;
            }
            var score = c.Parts[mk].Orders is { Bill: false } q ? q.Count * 2 + (Making(q, e.Kind) ? 0 : 1) : 1;
            if (score < bestScore)
                (best, bestScore) = (mk, score);
        }
        if (best >= 0 && WorkOrders.Queue(world, c, best, what) is var no && no == null)
        {
            AheadOfUsers(world, c, c.Parts[best].Orders!, e);
            e.Maker = (ushort)best;
            e.Flags &= unchecked((byte)~ChainFlags.Expanded);
            e.Reason = "";
            return;
        }
        var name = ConstructParts.Get(e.Kind)?.Name ?? "part";
        var reason = refusal != null ? $"waiting on {name}: {refusal}" : $"waiting on {name}: nothing on the network makes it";
        if (e.Reason != reason)
            e.Reason = reason; // (made only when it changes)
    }

    /// <summary>
    /// The line just queued (the last) moves ahead of the first line in that queue it is an input for (the root's own line, or an
    /// ancestor link's): else a machine asked for a part and for that part's input would wait on itself. That line waits for this
    /// input, so it has no batch under way (an Assembler's progress starts only with every input in), and nothing is lost.
    /// </summary>
    private static void AheadOfUsers(ConstructWorld world, Construct c, OrderList q, in ChainEntry e)
    {
        var rootKind = RootKind(c, e.Root, e.RootId);
        for (var k = 0; k < q.Count - 1; k++)
        {
            ref readonly var l = ref q.Items[k];
            var uses = l.Chain == 0 ? l.Part == rootKind && !l.IsPiece : IsAncestor(world, e, l.Chain);
            if (!uses)
                continue;
            var mine = q.Items[q.Count - 1];
            Array.Copy(q.Items, k, q.Items, k + 1, q.Count - 1 - k);
            q.Items[k] = mine;
            world.OrdersChanged(c);
            return;
        }
    }

    private static bool IsAncestor(ConstructWorld world, in ChainEntry e, ushort id)
    {
        var p = e.Parent;
        for (var hop = 0; hop <= MaxDepth && p != 0; hop++)
        {
            if (p == id)
                return true;
            var at = Find(world, p);
            if (at < 0)
                return false;
            p = world.Chains[at].Parent;
        }
        return false;
    }

    private static bool Making(OrderList q, ushort kind)
    {
        for (var k = 0; k < q.Count; k++)
            if (q.Items[k].Part == kind && !q.Items[k].IsPiece)
                return true;
        return false;
    }

    // ---- The step (2 Hz, with the terminals) -----------------------------------------------------------------------------

    /// <summary>Server, before the terminals: every entry kept honest; entries whose root or parent went, or that are complete, leave.</summary>
    public static void Step(ConstructWorld world)
    {
        world.ChainStepNo++;
        for (var i = world.ChainCount - 1; i >= 0; i--)
        {
            if (i >= world.ChainCount)
                continue; // (a removal cascaded)
            ref var e = ref world.Chains[i];
            var c = world.Get(e.Body);
            var gone = c == null || e.Root >= c.PartCount || e.Dest >= c.PartCount || e.Made >= e.Asked || c.Parts[e.Root].Craft is not { } q || RootClosed(q, e.RootId);
            if (!gone && e.Parent != 0)
                gone = Find(world, e.Parent) is var p && (p < 0 || world.Chains[p].Maker != e.Dest); // (its parent finished, went, or changed maker)
            if (gone)
            {
                if (c != null)
                    Drop(world, c, e);
                Remove(world, i);
                continue;
            }
            var riding = Riding(c!, e);
            var held = world.Reserve.Held(c!.Parts[e.Dest].Store, e.Id);
            if (e.Maker != NoPart && (e.Maker >= c.PartCount || !QueuedAt(c, e.Maker, e.Id)) && riding + held + e.Made < e.Asked)
            {
                e.Maker = NoPart; // (its maker went, or the order left its queue: the player pressed Clear; asked again when served)
                e.Flags &= unchecked((byte)~ChainFlags.Expanded);
            }
        }
    }

    /// <summary>
    /// Server, right after root <paramref name="t"/>'s terminal step (roots are stepped oldest first): its entries, best rank first, take
    /// stock, find a maker for what stock doesn't cover, send one claimed piece, and ask their own maker's inputs (from the step after
    /// they were made: one level a step).
    /// </summary>
    public static void Serve(ConstructWorld world, Construct c, int t)
    {
        if (world.ChainCount == 0)
            return;
        CraftNet? net = null;
        for (var i = 0; i < world.ChainCount; i++)
        {
            ref var e = ref world.Chains[i];
            if (e.Body != c.Id || e.Root != t || e.Born == world.ChainStepNo || ((ChainFlags)e.Flags & ChainFlags.Blocked) != 0)
                continue;
            net ??= ConstructCrafting.NetOf(world, c, t);
            var held = world.Reserve.Held(c.Parts[e.Dest].Store, e.Id);
            if (e.Maker == NoPart)
            {
                var left = e.Asked - e.Made - Riding(c, e) - held;
                if (left > 0)
                    left -= FromStock(world, c, net, ref e, left);
                if (left > 0)
                    FindMaker(world, c, net, ref e, left);
                else if (e.Reason != "")
                    e.Reason = "";
            }
            if (held > 0 || e.Maker == NoPart)
                SendOne(world, c, net, e);
            if (e.Maker != NoPart && ((ChainFlags)e.Flags & ChainFlags.Expanded) == 0)
            {
                var asked = QueuedCount(c, e.Maker, e.Id);
                if (asked <= 0 || Need(world, c, net, e.Maker, e.Kind, asked, world.Chains[i]))
                    world.Chains[i].Flags |= (byte)ChainFlags.Expanded; // (Need may have moved later entries, never this one: children rank after it)
            }
        }
    }

    private static bool RootClosed(CraftList q, ushort id) => q.Find(id) is var k && (k < 0 || q.Items[k].Cancelled || q.Items[k].Done);

    private static bool QueuedAt(Construct c, int maker, ushort id) => QueuedCount(c, maker, id) > 0;

    /// <summary>Pieces still to make on the maker's lines marked with entry <paramref name="id"/>.</summary>
    private static int QueuedCount(Construct c, int maker, ushort id)
    {
        if (maker >= c.PartCount || c.Parts[maker].Orders is not { Bill: false } q)
            return 0;
        var n = 0;
        for (var k = 0; k < q.Count; k++)
            if (q.Items[k].Chain == id)
                n += Math.Max(1, q.Items[k].Count - q.Items[k].Made);
        return n;
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

    /// <summary>An entry leaving: its unstarted work order leaves its maker's queue (a started one finishes its piece), its claims go.</summary>
    private static void Drop(ConstructWorld world, Construct c, in ChainEntry e)
    {
        if (e.Dest < c.PartCount && world.Reserve.Held(c.Parts[e.Dest].Store, e.Id) > 0)
        {
            world.Reserve.Release(world, c.Parts[e.Dest].Store, e.Id);
            world.CraftChanged(c);
        }
        if (e.Maker >= c.PartCount || c.Parts[e.Maker].Orders is not { Bill: false } q)
            return;
        var idle = world.StoreOf(c, e.Maker) is not { JobTicks: > 0 }; // (an Assembler's batch under way: its head is started)
        for (var k = q.Count - 1; k >= 0; k--)
            if (q.Items[k].Chain == e.Id && q.Items[k].Made == 0 && (k > 0 || idle)) // (a started head finishes its batch, and its piece goes to storage)
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

    /// <summary>Server: terminal <paramref name="t"/>'s order cancelled: its entries (every level) go, their unstarted orders leave their makers.</summary>
    public static void Cancelled(ConstructWorld world, Construct c, int t, ushort id)
    {
        for (var i = world.ChainCount - 1; i >= 0; i--)
            if (world.Chains[i].Body == c.Id && world.Chains[i].Root == t && world.Chains[i].RootId == id)
            {
                Drop(world, c, world.Chains[i]);
                Remove(world, i);
            }
    }

    // ---- Riding and arriving ---------------------------------------------------------------------------------------------

    /// <summary>Still wanted from makers: asked, less arrived, riding and claimed from stock.</summary>
    private static bool Wants(ConstructWorld world, Construct c, in ChainEntry e) =>
        e.Made + Riding(c, e) + world.Reserve.Held(c.Parts[e.Dest].Store, e.Id) < e.Asked;

    /// <summary>The entry a piece of kind <paramref name="d"/> could serve at machine <paramref name="dest"/> (from <paramref name="maker"/> first): its id, or 0. Best rank first.</summary>
    public static ushort WantAt(ConstructWorld world, Construct c, int dest, ConstructPartDef d, int maker)
    {
        ushort any = 0;
        for (var i = 0; i < world.ChainCount; i++)
        {
            ref readonly var e = ref world.Chains[i];
            if (e.Body != c.Id || e.Dest != dest || e.Kind != d.Id || !Wants(world, c, e))
                continue;
            if (maker < 0 || e.Maker == maker)
                return e.Id;
            if (any == 0)
                any = e.Id;
        }
        return any;
    }

    /// <summary>
    /// A machine's piece leaving it: the best-ranked open entry whose maker it is, of that kind, else 0; where it goes; and its root's
    /// age stamp (a terminal's own older order for the same kind from the same machine goes first: <see cref="ConstructLogistics.AtSource"/>).
    /// </summary>
    public static ushort FromMaker(ConstructWorld world, Construct c, int maker, ConstructPartDef d, out int dest, out uint stamp)
    {
        dest = -1;
        stamp = 0;
        for (var i = 0; i < world.ChainCount; i++)
        {
            ref readonly var e = ref world.Chains[i];
            if (e.Body == c.Id && e.Maker == maker && e.Kind == d.Id && e.Dest < c.PartCount && Wants(world, c, e))
            {
                dest = e.Dest;
                stamp = e.Stamp;
                return e.Id;
            }
        }
        return 0;
    }

    /// <summary>Is entry <paramref name="id"/> still open for machine <paramref name="dest"/>?</summary>
    public static bool Open(ConstructWorld world, Construct c, int dest, ushort id)
    {
        var i = Find(world, id);
        return i >= 0 && world.Chains[i].Body == c.Id && world.Chains[i].Dest == dest;
    }

    /// <summary>Server: a part arrived in machine <paramref name="dest"/>: it counts for its entry (the rider's tag), else the best-ranked open one there.</summary>
    public static void Arrived(ConstructWorld world, Construct c, int dest, ushort tag, ConstructPartDef d)
    {
        var at = tag != 0 ? Find(world, tag) : -1;
        for (var i = 0; i < world.ChainCount && at < 0; i++)
            if (world.Chains[i].Body == c.Id && world.Chains[i].Dest == dest && world.Chains[i].Kind == d.Id && world.Chains[i].Made < world.Chains[i].Asked)
                at = i;
        if (at >= 0)
            world.Chains[at].Made++;
    }

    // ---- What the player reads -------------------------------------------------------------------------------------------

    /// <summary>The order's row: the first link nobody makes, else "Gear Set 2/4 from the Assembler" for the first open one; null without entries.</summary>
    public static string? Note(ConstructWorld world, Construct c, int t, ushort id)
    {
        var first = -1;
        for (var i = 0; i < world.ChainCount; i++)
        {
            ref readonly var e = ref world.Chains[i];
            if (e.Body != c.Id || e.Root != t || e.RootId != id)
                continue;
            if (e.Maker == NoPart && e.Reason != "")
                return e.Reason;
            if (first < 0 && e.Depth == 1)
                first = i;
        }
        if (first < 0)
            return null;
        ref readonly var f = ref world.Chains[first];
        return $"{ConstructParts.Get(f.Kind)?.Name} {f.Made}/{f.Asked} {(f.Maker == NoPart ? "from stock" : $"from the {c.DefOf(f.Maker).Name}")}";
    }

    /// <summary>
    /// The order's chain as lines for its Orders row (made on the server, sent in <see cref="CBodyKind.Craft"/>): its links in tree order,
    /// at most <see cref="MaxLines"/>; how many more there are.
    /// </summary>
    public static int Lines(ConstructWorld world, Construct c, int t, ushort id, Span<ChainLine> into, out int more)
    {
        more = 0;
        var n = 0;
        Walk(world, c, t, id, 0, into, ref n, ref more);
        return n;
    }

    private static void Walk(ConstructWorld world, Construct c, int t, ushort id, ushort parent, Span<ChainLine> into, ref int n, ref int more)
    {
        for (var i = 0; i < world.ChainCount; i++)
        {
            ref readonly var e = ref world.Chains[i];
            if (e.Body != c.Id || e.Root != t || e.RootId != id || e.Parent != parent || (parent == 0 && e.Depth != 1))
                continue;
            if (n >= into.Length)
            {
                more++;
                continue;
            }
            var held = world.Reserve.Held(c.Parts[e.Dest].Store, e.Id);
            into[n++] = new ChainLine
            {
                Depth = e.Depth, Kind = e.Kind, Made = e.Made, Asked = e.Asked, Maker = e.Maker, Dest = e.Dest, Stock = (ushort)Math.Min(held, ushort.MaxValue),
                Reason = e.Maker == NoPart ? e.Reason : "",
            };
            if (e.Depth < MaxDepth + 1)
                Walk(world, c, t, id, e.Id, into, ref n, ref more);
        }
    }

    /// <summary>A chain line's text, the same on both peers: "Gear Set 1/4 - Assembler making", "Gear Set 0/2 - from stock (2 claimed)".</summary>
    public static string LineText(Construct c, in ChainLine l)
    {
        var name = ConstructParts.Get(l.Kind)?.Name ?? "part";
        var pad = new string(' ', 2 * l.Depth);
        if (l.Maker == NoPart && l.Reason != "")
            return $"{pad}{name} x{l.Asked} - {l.Reason}";
        var by = l.Maker != NoPart && l.Maker < c.PartCount ? $"the {c.DefOf(l.Maker).Name} makes it" : "from stock";
        var stock = l.Stock > 0 && l.Maker != NoPart ? $", {l.Stock} from stock" : l.Stock > 0 ? $" ({l.Stock} claimed)" : "";
        return $"{pad}{name} {l.Made}/{l.Asked} - {by}{stock}";
    }

    // ---- Saves (the LOGI block's second and third parts) -----------------------------------------------------------------

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
            var e = e0 with { Body = c.Id, Reason = "", Born = 0, Depth = Math.Max((byte)1, e0.Depth), Class = e0.Class == 0 && e0.Stamp == 0 ? ClassNormal : e0.Class };
            if (world.ChainCount >= MaxEntries || e.Dest >= c.PartCount || e.Root >= c.PartCount)
                continue;
            if (((ChainFlags)e.Flags & ChainFlags.Blocked) != 0)
                e.Reason = BlockedText(e.Flags, ConstructParts.Get(e.Kind)?.Name ?? "part");
            if (e.Maker != NoPart && (e.Maker >= c.PartCount || c.Parts[e.Maker].Orders is not { Bill: false } q || !Mark(q, e)))
                e.Maker = NoPart; // (asked again at the next step)
            Insert(world, e);
            if (e.Id >= world.NextChainId)
                world.NextChainId = (ushort)(e.Id + 1 == 0 ? 1 : e.Id + 1);
            if (e.Stamp >= world.OrderStamp)
                world.OrderStamp = e.Stamp + 1;
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

/// <summary>One chain line under an order's row (both peers: the server makes them, <see cref="CBodyKind.Craft"/> carries them).</summary>
public struct ChainLine
{
    public byte Depth;
    public ushort Kind, Made, Asked, Maker, Dest, Stock;
    public string Reason;

    public readonly bool Same(in ChainLine o) =>
        Depth == o.Depth && Kind == o.Kind && Made == o.Made && Asked == o.Asked && Maker == o.Maker && Dest == o.Dest && Stock == o.Stock && Reason == o.Reason;
}

public sealed partial class ConstructWorld
{
    public readonly ChainEntry[] Chains = new ChainEntry[ConstructLogisticsPlan.MaxEntries]; // chained orders' links (server), in rank order
    public int ChainCount;
    public ushort NextChainId = 1;
    public ushort ChainStepNo = 1; // the plan's step count (an entry's Born)
    public uint OrderStamp = 1; // the next root's age stamp (server)
}
