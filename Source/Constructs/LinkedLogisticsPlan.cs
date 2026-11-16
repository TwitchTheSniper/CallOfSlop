using System.Text;

namespace CallOfSlop;

/// <summary>
/// An end's report of its network (docs/sandbox/design/linked-logistics.md 4.1): the free stock of the network round its Cargo Dock (or,
/// with none, its first Crafting Terminal), and every ten seconds what its machines make (the terminal's own catalogue), with each maker's
/// pace and how many its stock of inputs makes, for the estimate while its world is closed. Copies, fixed arrays.
/// </summary>
public static class LinkReports
{
    private static readonly CHolding[] Hold = new CHolding[CStore.MaxKinds + 1];
    private static readonly List<CraftRow> Rows = new(256);
    private static readonly ushort[] MKind = new ushort[LinkReport.MaxKinds];

    /// <summary>The part a body's network is seen from: its first working Cargo Dock, else its first Crafting Terminal (not a station), or -1.</summary>
    public static int Terminal(Construct c, bool dockOnly = false)
    {
        var term = -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.LinkKind == LinkKind.Dock && !c.Parts[i].Broken)
                return i;
            if (!dockOnly && term < 0 && d.IsCraftTerminal && !d.IsBuildStation && !c.Parts[i].Broken)
                term = i;
        }
        return term;
    }

    public static void Build(ConstructWorld world, Construct c, LinkReport r)
    {
        var t = Terminal(c);
        var h = 17L;
        LinkSub.Report(world, c, r);
        for (var k = 0; k < r.StationN; k++)
            h = h * 31 + r.Stations[k] + (r.StationIdle[k] ? 7 : 0);
        r.HasDock = t >= 0 && c.DefOf(t).LinkKind == LinkKind.Dock;
        r.DockPart = r.HasDock ? (ushort)t : ushort.MaxValue;
        r.HasDispatcher = false;
        for (var i = 0; i < c.PartCount && !r.HasDispatcher; i++)
            r.HasDispatcher = c.DefOf(i).LinkKind == LinkKind.Dispatcher && !c.Parts[i].Broken;
        r.Powered = t >= 0 && ConstructRecycler.Powered(world, c, t);
        if (t < 0)
        {
            if (r.N != 0)
                r.Version++;
            r.N = 0;
            r.Made = LinkLedger.Now;
            return;
        }
        var net = ConstructCrafting.NetOf(world, c, t);
        // The makes, every MakesEvery reports (the catalogue allocates its rows; once in ten seconds an end).
        var keepMakes = r.MakesAge < LinkLedger.MakesEvery;
        r.MakesAge = keepMakes ? r.MakesAge + 1 : 0;
        var mn = 0;
        Span<float> mRate = stackalloc float[LinkReport.MaxKinds];
        Span<int> mBatch = stackalloc int[LinkReport.MaxKinds];
        if (keepMakes)
            for (var k = 0; k < r.N && mn < MKind.Length; k++)
                if (r.Makes[k])
                    (MKind[mn], mRate[mn], mBatch[mn++]) = (r.Kind[k], r.Rate[k], r.Batches[k]);
        if (!keepMakes)
        {
            ConstructCrafting.Catalogue(world, c, t, Rows);
            foreach (var row in Rows)
            {
                if (mn >= MKind.Length || row.Make == 0 || row.Def is not { } d || !Linkable((ushort)d.Id))
                    continue;
                var rate = 1f / 30;
                var batches = 1000;
                if (row.Recipe is { } rec)
                {
                    rate = rec.Count / MathF.Max(0.5f, rec.Seconds);
                    foreach (var (rp, rn) in rec.Parts)
                        batches = Math.Min(batches, ConstructCrafting.Stock(world, c, net, (ushort)rp.Id) / Math.Max(1, rn) * rec.Count);
                }
                (MKind[mn], mRate[mn], mBatch[mn++]) = ((ushort)d.Id, rate, batches);
            }
        }
        // The stock: what the network's stores hold less what is claimed.
        var n = 0;
        Span<ushort> kinds = stackalloc ushort[LinkReport.MaxKinds];
        Span<int> free = stackalloc int[LinkReport.MaxKinds];
        for (var i = 0; i < net.StoreN; i++)
        {
            if (world.StoreOf(c, net.Stores[i]) is not { } st)
                continue;
            var hn = world.Holdings(c, net.Stores[i], Hold);
            for (var k = 0; k < hn; k++)
            {
                var ho = Hold[k];
                if (!ho.IsPart || !Linkable(ho.Id))
                    continue;
                var f = ho.Count - world.Reserve.Reserved(st.Handle, ho.Id);
                if (f <= 0)
                    continue;
                var at = kinds[..n].IndexOf(ho.Id);
                if (at < 0 && n < kinds.Length)
                    (kinds[n], free[n], at) = (ho.Id, 0, n++);
                if (at >= 0)
                    free[at] += f;
            }
        }
        var old = r.N;
        r.N = 0;
        for (var k = 0; k < n; k++)
        {
            var m = MKind.AsSpan(0, mn).IndexOf(kinds[k]);
            Put(r, kinds[k], free[k], m >= 0, m >= 0 ? mRate[m] : 0, m >= 0 ? mBatch[m] : 0);
        }
        for (var m = 0; m < mn; m++)
            if (kinds[..n].IndexOf(MKind[m]) < 0)
                Put(r, MKind[m], 0, true, mRate[m], mBatch[m]);
        for (var k = 0; k < r.N; k++)
            h = h * 31 + r.Kind[k] * 7 + r.Free[k] + (r.Makes[k] ? 1 : 0);
        h += r.HasDock ? 3 : 0;
        h += r.Powered ? 5 : 0;
        if (h != r.Hash || old != r.N)
        {
            r.Hash = h;
            r.Version++;
        }
        r.Made = LinkLedger.Now;
    }

    private static void Put(LinkReport r, ushort kind, int free, bool makes, float rate, int batches)
    {
        if (r.N >= LinkReport.MaxKinds)
            return;
        var k = r.N++;
        (r.Kind[k], r.Free[k], r.Makes[k], r.Rate[k], r.Batches[k]) = (kind, free, makes, rate, batches);
    }

    /// <summary>A kind that travels over a link: a catalogue part (kinds go by name in saves; a run-time piece's id is this machine's own).</summary>
    public static bool Linkable(ushort kind) => kind != ConstructParts.None && !Extrusion.IsRuntimeId(kind) && ConstructParts.Get(kind) is { } d && ReferenceEquals(ConstructParts.Find(d.Name), d);
}

/// <summary>
/// The planner's remote steps (docs/sandbox/design/linked-logistics.md 4.2; the user's answer 4: a pair is two-way): a root's order or a
/// chained link that its own network can't fill is posted to a linked network that holds or makes the kind, after every local answer.
/// There it is an order at that network's Cargo Dock (a terminal-like owner whose tray is the staging hold: its claims, its makers and
/// its chains are the ordinary ones), and what comes into the dock waits for a ship. Goods never teleport.
/// </summary>
public static class LinkPlan
{
    public const ushort LinkOwner = 0xFFF0; // claims a Cargo Dock's hold keeps for a line's ready parts (owner: never a store handle)
    public const ushort ChainOwner = 0xFFFF; // a line posted for a chained link (OwnerId: the link's id)
    public const int PostsAStep = 8; // new remote lines a world a second

    private static int _posts;
    private static double _postsAt;

    /// <summary>Does a linked network offer this kind to this body (a terminal there may take an order nothing local makes)?</summary>
    public static bool Offers(Construct c, ushort kind)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Link is { Offers.Length: > 0 } lp && c.DefOf(i).IsLinkEnd)
                foreach (var o in lp.Offers)
                    if (o.Kind == kind && (o.Free > 0 || o.Makes))
                        return true;
        return false;
    }

    /// <summary>
    /// Server: what is left of an order after stock and local makers goes to a linked network: the supplier on this planetoid first, then
    /// the one with most free stock, then any that makes it; never one that refused this order lately; never split. How many were posted.
    /// </summary>
    public static int AskRemote(ConstructWorld world, Construct c, ushort owner, ushort ownerId, bool chain, ushort kind, int ask)
    {
        if (ask <= 0 || !LinkReports.Linkable(kind) || !Budget())
            return 0;
        var key = LinkedLogistics.WorldKey();
        LinkEnd? best = null, from = null;
        var bestScore = int.MinValue;
        Span<uint> ps = stackalloc uint[16];
        foreach (var e in LinkLedger.Ends.Values)
        {
            if (e.World != key || e.Body != c.Id || e.Kind == LinkKind.None)
                continue;
            var n = LinkLedger.Partners(e, ps);
            for (var i = 0; i < n; i++)
            {
                var p = LinkLedger.Ends[ps[i]];
                var k = p.Report.IndexOf(kind);
                if (k < 0 || p.Report.Free[k] <= 0 && !p.Report.Makes[k] || RefusedLately(key, c.Id, owner, ownerId, kind, p.Id))
                    continue;
                var score = (p.World == key ? 1_000_000 : 0) + Math.Min(p.Report.Free[k], 999_999) + (p.Report.Makes[k] ? 1 : 0);
                if (score > bestScore)
                    (best, from, bestScore) = (p, e, score);
            }
        }
        if (best == null || from == null || LinkLedger.NewLine(from, best, kind, ask) is not { } line)
            return 0;
        (line.Owner, line.OwnerId, line.Chain) = (owner, ownerId, chain);
        _posts++;
        Console.WriteLine($"[linked] line {line.Id}: {line.Count} {ConstructParts.Get(kind)?.Name} for construct {c.Id} asked of {best.Name} ({LinkLedger.Title(best.World)})");
        return line.Count;
    }

    private static bool Budget()
    {
        var now = LinkLedger.Now;
        if (now - _postsAt >= 1)
            (_posts, _postsAt) = (0, now);
        return _posts < PostsAStep;
    }

    private static bool RefusedLately(string world, ushort body, ushort owner, ushort id, ushort kind, uint supplier)
    {
        foreach (var l in LinkLedger.Lines)
            if (l.State == LinkLineState.Failed && l.SupplierEnd == supplier && l.Kind == kind && l.CWorld == world && l.CBody == body && l.Owner == owner && l.OwnerId == id)
                return true;
        return false;
    }

    /// <summary>A chained link nobody on the network makes: posted remotely for what isn't posted already. Its reason, or null when nothing links.</summary>
    public static string? AskChain(ConstructWorld world, Construct c, ushort chainId, ushort kind, int count)
    {
        var key = LinkedLogistics.WorldKey();
        var posted = LinkLedger.Posted(key, c.Id, ChainOwner, chainId, true);
        if (posted < count)
            posted += AskRemote(world, c, ChainOwner, chainId, true, kind, count - posted);
        return posted > 0 ? Note(c, ChainOwner, chainId, true) : null;
    }

    /// <summary>Server: an order cancelled at its terminal: its remote lines end (what is loaded still arrives, as stock).</summary>
    public static void Cancelled(Construct c, ushort owner, ushort id)
    {
        var key = LinkedLogistics.WorldKey();
        foreach (var l in LinkLedger.Lines)
            if (l.State is LinkLineState.Posted or LinkLineState.Open && l.CWorld == key && l.CBody == c.Id && l.Owner == owner && l.OwnerId == id && !l.Chain)
            {
                LinkLedger.CancelLine(l.Id, "its order was cancelled");
                l.LostPending = 0; // (no order to take it off)
            }
    }

    // ---- The consumer's side -----------------------------------------------------------------------------------------------

    /// <summary>Server, once a second: lines this world posted that lost goods (a ship destroyed, a broken link) come off their orders, which ask again.</summary>
    public static void Consume(ConstructWorld world, string key)
    {
        foreach (var l in LinkLedger.Lines)
        {
            if (l.CWorld != key || l.LostPending == 0)
                continue;
            var n = l.LostPending;
            l.LostPending = 0;
            if (l.Use != 0)
            {
                LinkSub.Lost(world, l);
                continue;
            }
            if (l.Chain || world.Get(l.CBody) is not { } c)
                continue; // (a chained link asks again on its next try: its need is worked out from what came)
            for (var t = 0; t < c.PartCount; t++)
                if (c.Parts[t].Store == l.Owner && c.Parts[t].Craft is { } q && q.Find(l.OwnerId) is var k and >= 0)
                {
                    ref var o = ref q.Items[k];
                    o.Asked = (ushort)Math.Max(0, o.Asked - n);
                    world.CraftChanged(c);
                    break;
                }
        }
    }

    private static readonly StringBuilder Sb = new(128);

    /// <summary>A root order's remote state in a few words ("12 Steel Beam at Bluestone Works: 8 ready, 4 being made - needs a ship"), or null with none.</summary>
    public static string? Note(Construct c, ushort owner, ushort id, bool chain = false)
    {
        var key = LinkedLogistics.WorldKey();
        LinkLine? first = null;
        int ready = 0, aboard = 0, making = 0, taken = 0, lost = 0;
        var stalled = false;
        var asleep = false;
        foreach (var l in LinkLedger.Lines)
        {
            if (l.CWorld != key || l.CBody != c.Id || l.Owner != owner || l.OwnerId != id || l.Chain != chain || l.State == LinkLineState.Closed && l.Taken == 0)
                continue;
            first ??= l;
            ready += l.Ready + (int)l.Est;
            aboard += Math.Max(0, l.Loaded - l.Delivered - l.Lost - l.Taken);
            making += Math.Max(0, l.Count - l.Loaded - l.Ready - (int)l.Est);
            taken += l.Taken;
            lost += l.Lost;
            stalled |= l.StalledFrom >= 0 && LinkLedger.Now - l.StalledFrom > LinkLedger.StallAfter;
            asleep |= LinkLedger.Asleep(l.SWorld);
        }
        if (first == null)
            return null;
        var where = LinkLedger.Ends.TryGetValue(first.SupplierEnd, out var s) ? s.Name : LinkLedger.Title(first.SWorld);
        var sb = Sb.Clear().Append(ConstructParts.Get(first.Kind)?.Name ?? "part").Append(" at ").Append(where).Append(": ");
        if (first.State == LinkLineState.Failed)
            return sb.Append(first.Note).ToString();
        if (aboard > 0)
            sb.Append(aboard).Append(stalled ? " STALLED on " : " coming on ").Append(first.Ship.Length > 0 ? first.Ship : "a ship").Append(ready + making > 0 ? "; " : "");
        if (ready > 0)
            sb.Append(ready).Append(" ready").Append(making > 0 ? ", " : "");
        if (making > 0)
            sb.Append(making).Append(asleep ? " estimated (nobody there)" : " being made");
        if (ready > 0 && aboard == 0)
            sb.Append(" - needs a ship");
        if (taken > 0)
            sb.Append(" - ").Append(taken).Append(" taken from the ship's hold: order again to replace them");
        if (lost > 0)
            sb.Append(" (").Append(lost).Append(" lost with a ship: ordered again)");
        if (s is { Report.HasDock: false })
            sb.Append(" - no Cargo Dock there");
        return sb.ToString();
    }

    // ---- The supplier's side -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Server, once a second: lines posted to this world's ends become orders at the supplier's Cargo Dock; what lands in the dock's hold
    /// for them is ready (claimed for the line, so no local order takes it), and a broken link's unstarted order is cancelled.
    /// </summary>
    public static void Supply(ConstructWorld world, string key)
    {
        foreach (var l in LinkLedger.Lines)
        {
            if (l.SWorld != key || world.Get(l.SBody) is not { } c)
                continue;
            if (l.Use != 0)
            {
                LinkSub.Supply(world, c, l);
                continue;
            }
            if (l.CancelAtDock)
            {
                l.CancelAtDock = false;
                if (l.DockPart < c.PartCount && c.Parts[l.DockPart].Craft is { } cq && cq.Find(l.DockOrder) >= 0)
                    ConstructCrafting.Cancel(world, c, l.DockPart, l.DockOrder);
                continue;
            }
            if (l.State == LinkLineState.Posted)
            {
                var dock = LinkReports.Terminal(c, dockOnly: true);
                if (dock < 0)
                {
                    l.Note = $"no Cargo Dock at {LinkedLogistics.NameOf(c, 0)}: nothing is sent";
                    continue;
                }
                var want = l.ToLoad - l.Ready - (int)l.Est;
                if (want <= 0)
                {
                    l.State = LinkLineState.Open;
                    continue;
                }
                if (ConstructCrafting.Place(world, c, dock, new WorkOrder { Part = l.Kind, Count = (ushort)want }) is { } why)
                {
                    l.State = LinkLineState.Failed;
                    l.Note = why;
                    l.ClosedAt = LinkLedger.Now;
                    l.LostPending += (ushort)l.Outstanding; // (its order asks again: never this supplier, RefusedLately)
                    Console.WriteLine($"[linked] line {l.Id} refused at {LinkedLogistics.NameOf(c, dock)}: {why}");
                    continue;
                }
                var q = c.Parts[dock].Craft!;
                (l.DockPart, l.DockOrder, l.SeenGot, l.State, l.Reclaim) = ((ushort)dock, q.Items[q.Count - 1].Id, 0, LinkLineState.Open, false);
                l.Note = $"ordered at {LinkedLogistics.NameOf(c, dock)}";
                continue;
            }
            if (l.State != LinkLineState.Open || l.DockPart >= c.PartCount || world.StoreOf(c, l.DockPart) is not { } tray)
                continue;
            if (l.Reclaim)
            {
                // (after a load: claims aren't saved, the ready parts are claimed again)
                l.Reclaim = false;
                var have = world.Reserve.Held(LinkOwner, l.Id);
                if (have < l.Ready)
                    world.Reserve.Add(tray, l.Kind, Math.Min(l.Ready - have, ConstructCrafting.Available(world, tray, l.Kind)), LinkOwner, l.Id);
            }
            if (c.Parts[l.DockPart].Craft is { } dq && dq.Find(l.DockOrder) is var k and >= 0)
            {
                ref var o = ref dq.Items[k];
                if (o.Got > l.SeenGot)
                {
                    var add = Math.Min(o.Got - l.SeenGot, l.ToLoad - l.Ready);
                    l.SeenGot = o.Got;
                    if (add > 0)
                    {
                        l.Ready += (ushort)add;
                        world.Reserve.Add(tray, l.Kind, add, LinkOwner, l.Id);
                        l.Note = $"{l.Ready} ready at {LinkedLogistics.NameOf(c, l.DockPart)}: needs a ship";
                    }
                }
            }
        }
    }
}
