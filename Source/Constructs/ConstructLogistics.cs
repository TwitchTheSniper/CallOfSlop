using System.Numerics;

namespace CallOfSlop;

/// <summary>What a riding piece is doing on a smart network (<see cref="CRider.Flags"/>; on the wire and in a save: a byte).</summary>
[Flags]
public enum LogiFlags : byte
{
    None = 0,
    Storage = 1, // it rides to a store as overflow or unordered stock (else, with a Dest, to an order)
    Overflow = 2, // it had an order's destination and lost it (it is not offered to an order again: no ping-pong)
    Stuck = 4, // it waits at a Router: no way to its destination and no storage with room
    Fetched = 8, // a terminal fetched it from a store for an order (it counts as that order's Sent, not its Asked)
    ReasonMask = 0xF0, // the last failure's reason (LogiReason), bits 4-7
}

/// <summary>Why a piece lost its destination or waits (bits 4-7 of <see cref="CRider.Flags"/>; each peer makes the text).</summary>
public enum LogiReason : byte { None, NoWay, Narrow, DestFull, Closed, Loop, NoStorage }

/// <summary>A construct's logistics network map (<see cref="ConstructLogistics"/>): built on either peer from its links, kept until they change.</summary>
public sealed class LogiMap
{
    public const int MaxNodes = 512, MaxRouters = 32, MaxDests = 128;
    public const byte None = 255;

    public int Stamp = int.MinValue, SizeStamp, PartCount, RouteCount; // what it was built from
    public ulong LinkHash; // ...and its links (ConstructLogistics.LinkHash): a rebuild that changed none keeps the map
    public short[] NodeOf = []; // part -> node, -1
    public readonly ushort[] NodePart = new ushort[MaxNodes];
    public readonly short[] NodeNet = new short[MaxNodes]; // its group (the smallest node index joined to it)
    public readonly bool[] Through = new bool[MaxNodes]; // a way may pass it: a Router, or a junction with one way out (a Join)
    public readonly sbyte[] RouterOfNode = new sbyte[MaxNodes];
    public readonly short[] DestOfNode = new short[MaxNodes];
    public int NodeN;
    public readonly float[] EdgeLen = new float[Construct.MaxRoutes]; // each conveyor's length (walked once here, never per step)

    public readonly ushort[] Routers = new ushort[MaxRouters]; // node of each Router
    public int RouterN;
    public readonly ushort[] Dests = new ushort[MaxDests]; // node of each destination (a Piece Rack's hold or a terminal's tray)
    public readonly bool[] DestStore = new bool[MaxDests]; // a plain Piece Rack: it takes overflow (a tray does not)
    public int DestN;
    public readonly bool[] NetHasRouter = new bool[MaxNodes]; // by group

    // Next hops, a column a destination, made the first time a piece is sent there after a rebuild (server, and a client's terminal list).
    public readonly bool[] Ready = new bool[MaxDests];
    public readonly byte[] Hop = new byte[MaxRouters * MaxDests]; // the Router's out conveyor toward the destination (None: no way)
    public readonly float[] Dist = new float[MaxRouters * MaxDests]; // metres, plus 2 m a junction crossed
    public readonly byte[] Narrow = new byte[MaxRouters * MaxDests]; // the narrowest conveyor on that way, in 5 cm steps

    // What each Router shows (either peer: the server as it sends a piece, a client from the riders it hears).
    public readonly byte[] ShowWay = new byte[MaxRouters], ShowFrom = new byte[MaxRouters], ShowState = new byte[MaxRouters];
    public readonly double[] ShowAt = new double[MaxRouters];
    public readonly ushort[] ShowDef = new ushort[MaxRouters], ShowDest = new ushort[MaxRouters], ShowTag = new ushort[MaxRouters];
    public readonly bool[] ShowStuck = new bool[MaxRouters];
    public readonly byte[] ShowReason = new byte[MaxRouters];
    public readonly ushort[] ShowStuckDef = new ushort[MaxRouters];
    public int ShowVersion; // goes up when any Router's show changes (a look line's key)
    public bool AnyOut; // server: a Router's Alarm or Moving output is set

    public int RouterSlot(int part) => (uint)part < (uint)NodeOf.Length && NodeOf[part] >= 0 ? RouterOfNode[NodeOf[part]] : -1;
    public int DestSlot(int part) => (uint)part < (uint)NodeOf.Length && NodeOf[part] >= 0 ? DestOfNode[NodeOf[part]] : -1;
    public int NetOf(int part) => (uint)part < (uint)NodeOf.Length && NodeOf[part] >= 0 ? NodeNet[NodeOf[part]] : -1;
}

public sealed partial class Construct
{
    public LogiMap? Logi; // its logistics map (ConstructLogistics.Map), made the first time a conveyor asks
    public int LogiStamp; // goes up when something the map follows changes without a rebuild (a conveyor's width)
}

/// <summary>
/// The smart logistics network (docs/sandbox/constructs/production.md "Smart logistics", phase 1). A network is one group of parts
/// on one construct joined by intact conveyors and chutes. A <b>Conveyor Router</b> (a junction part with <c>"router": true</c>) sends
/// each piece the way to its destination: a terminal's tray whose order asked a machine for it, else a plain Piece Rack on its network
/// with room (overflow). A piece with neither waits at the Router, stuck (a red lamp, its Alarm output). A line without a Router, a
/// plain Splitter and an unpowered Router behave exactly as before (ConstructConveyor.Pass).
/// <para>
/// The map (<see cref="LogiMap"/>) is built from the links when they change (the body's <see cref="Construct.RebuildStamp"/>, or
/// <see cref="Construct.LogiStamp"/> for a width) and its next-hop columns lazily, one a destination, at most <see cref="ColumnsPerStep"/>
/// a conveyor step. A Router decides once per lead piece (one array read and one <c>Takes</c>); nothing runs for a piece between
/// junctions. Wants are read from the terminals' orders (an order's Asked less what already rides to it): no table is kept.
/// </para>
/// </summary>
public static class ConstructLogistics
{
    public const int ColumnsPerStep = 4; // new next-hop columns a conveyor step (a piece whose column isn't made waits a step)
    public const int MaxHops = 32; // junctions crossed before a piece is called lost in a loop
    public const int FullSteps = 50; // conveyor steps (5 s) a piece waits for its way before it goes to storage instead
    public const int RetrySteps = 20; // a stuck piece looks again every 2 s
    public const float JunctionCost = 2f; // metres a way is charged for each junction it crosses
    public static readonly string[] RouterOut = ["Alarm", "Moving"]; // a Router's outputs: its Out and Out1 (Refresh)

    private static int _columnsLeft = ColumnsPerStep;

    /// <summary>Server, each conveyor step: the column budget starts again.</summary>
    public static void NewStep() => _columnsLeft = ColumnsPerStep;

    // ---- The map ---------------------------------------------------------------------------------------------------------

    /// <summary>The body's map, built again if its links changed. Either peer; allocates only when the body grows past its arrays.</summary>
    public static LogiMap Map(Construct c)
    {
        var m = c.Logi ??= new LogiMap();
        if (m.Stamp == c.RebuildStamp && m.SizeStamp == c.LogiStamp && m.PartCount == c.PartCount && m.RouteCount == c.RouteCount)
            return m;
        Build(c, m);
        return m;
    }

    /// <summary>Is a conveyor whole and from a part (not a branch)?</summary>
    private static bool Usable(Construct c, in CRoute r) =>
        r.Type == RouteType.Conveyor && r.Parent == 255 && r.From < c.PartCount && r.To < c.PartCount && r.FirstCut > r.Count;

    private static int NodeFor(Construct c, LogiMap m, int part)
    {
        if (m.NodeOf[part] >= 0)
            return m.NodeOf[part];
        if (m.NodeN >= LogiMap.MaxNodes)
            return -1;
        var n = m.NodeN++;
        m.NodeOf[part] = (short)n;
        m.NodePart[n] = (ushort)part;
        m.NodeNet[n] = (short)n;
        m.Through[n] = false;
        m.RouterOfNode[n] = -1;
        m.DestOfNode[n] = -1;
        return n;
    }

    private static int Root(LogiMap m, int n)
    {
        while (m.NodeNet[n] != n)
            n = m.NodeNet[n] = m.NodeNet[m.NodeNet[n]];
        return n;
    }

    private static void Union(LogiMap m, int a, int b)
    {
        if (a < 0 || b < 0)
            return;
        a = Root(m, a);
        b = Root(m, b);
        if (a != b)
            m.NodeNet[Math.Max(a, b)] = (short)Math.Min(a, b);
    }

    /// <summary>A hash of what the map follows from: each conveyor's and chute's ends (their parts' kinds and stores), width and cut.</summary>
    private static ulong LinkHash(Construct c)
    {
        var h = 14695981039346656037UL ^ (ulong)c.RouteCount;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type is not (RouteType.Conveyor or RouteType.Chute))
                continue;
            int a = r.Type == RouteType.Chute ? r.Source : r.From, b = r.To;
            h = (h ^ (ulong)k ^ (ulong)r.Type << 8 ^ (ulong)a << 16 ^ (ulong)b << 32) * 1099511628211UL;
            h = (h ^ r.ConvW ^ (r.FirstCut > r.Count ? 1UL << 20 : 0) ^ (r.FedEnd ? 1UL << 21 : 0) ^ (ulong)r.Parent << 24) * 1099511628211UL;
            if (a < c.PartCount && b < c.PartCount)
                h = (h ^ c.Parts[a].Def ^ (ulong)c.Parts[b].Def << 16 ^ (ulong)c.Parts[a].Store << 32 ^ (ulong)c.Parts[b].Store << 48) * 1099511628211UL;
        }
        return h;
    }

    private static void Build(Construct c, LogiMap m)
    {
        m.Stamp = c.RebuildStamp;
        m.SizeStamp = c.LogiStamp;
        m.PartCount = c.PartCount;
        m.RouteCount = c.RouteCount;
        // A rebuild that changed no link (a machine's finished piece left the body, a part elsewhere was placed): the map, its columns
        // and what the Routers show stay as they are.
        var hash = LinkHash(c);
        if (hash == m.LinkHash && m.NodeOf.Length >= c.PartCount)
        {
            for (var n = 0; n < m.NodeN; n++)
                if (m.NodePart[n] >= c.PartCount)
                    goto full;
            return;
        }
        full:
        m.LinkHash = hash;
        // What each Router showed, by its part, carried over to the new slots (a Router whose index moved starts dark).
        Span<ushort> wasPart = stackalloc ushort[LogiMap.MaxRouters];
        var was = m.RouterN;
        for (var s = 0; s < was; s++)
            wasPart[s] = m.NodePart[m.Routers[s]];
        Span<byte> wasWay = stackalloc byte[LogiMap.MaxRouters], wasState = stackalloc byte[LogiMap.MaxRouters];
        Span<ushort> wasDef = stackalloc ushort[LogiMap.MaxRouters], wasDest = stackalloc ushort[LogiMap.MaxRouters], wasTag = stackalloc ushort[LogiMap.MaxRouters];
        m.ShowWay.AsSpan(0, was).CopyTo(wasWay);
        m.ShowState.AsSpan(0, was).CopyTo(wasState);
        m.ShowDef.AsSpan(0, was).CopyTo(wasDef);
        m.ShowDest.AsSpan(0, was).CopyTo(wasDest);
        m.ShowTag.AsSpan(0, was).CopyTo(wasTag);
        if (m.NodeOf.Length < c.PartCount)
            m.NodeOf = new short[Math.Max(c.PartCount, m.NodeOf.Length * 2)];
        m.NodeOf.AsSpan(0, c.PartCount).Fill(-1);
        m.NodeN = m.RouterN = m.DestN = 0;
        Span<byte> outs = stackalloc byte[LogiMap.MaxNodes];
        outs.Clear();
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            m.EdgeLen[k] = 0;
            if (r.Type == RouteType.Conveyor && r.Parent == 255 && r.From < c.PartCount && r.To < c.PartCount)
            {
                var a = NodeFor(c, m, r.From);
                var b = NodeFor(c, m, r.To);
                m.EdgeLen[k] = MathF.Max(1e-3f, ConstructConveyor.Length(c, k));
                if (a >= 0 && outs[a] < 255)
                    outs[a]++;
                if (r.FirstCut > r.Count)
                    Union(m, a, b);
            }
            else if (r.Type == RouteType.Chute && r.FedEnd && r.Source < c.PartCount && r.To < c.PartCount)
                Union(m, NodeFor(c, m, r.Source), NodeFor(c, m, r.To));
        }
        m.NetHasRouter.AsSpan(0, m.NodeN).Clear();
        for (var n = 0; n < m.NodeN; n++)
        {
            m.NodeNet[n] = (short)Root(m, n);
            var d = c.DefOf(m.NodePart[n]);
            if (d.IsRouter && d.IsConveyorJunction && m.RouterN < LogiMap.MaxRouters)
            {
                m.RouterOfNode[n] = (sbyte)m.RouterN;
                m.Routers[m.RouterN++] = (ushort)n;
                m.Through[n] = true;
            }
            else if (d.IsConveyorJunction)
                m.Through[n] = outs[n] == 1; // (a Join; a Splitter deals in turn, so a way through it is no way)
            else if ((d.IsPieceRack || d.IsAssembler || d.IsPanelFormer || d.IsPress && !d.IsLathe) && m.DestN < LogiMap.MaxDests && c.Parts[m.NodePart[n]].Store != 0)
            {
                // A rack or a tray; an Assembler's Piece in (a chained order's inputs); a Panel Former's (a chained panel order's sheet); a press's bays.
                m.DestOfNode[n] = (short)m.DestN;
                m.DestStore[m.DestN] = d.IsPieceRack && !d.IsCraftTerminal;
                m.Dests[m.DestN++] = (ushort)n;
            }
        }
        for (var s = 0; s < m.RouterN; s++)
            m.NetHasRouter[m.NodeNet[m.Routers[s]]] = true;
        m.Ready.AsSpan().Clear();
        m.ShowWay.AsSpan().Clear();
        m.ShowState.AsSpan().Clear();
        m.ShowStuck.AsSpan().Clear();
        for (var s = 0; s < m.RouterN; s++)
            for (var o = 0; o < was; o++)
                if (wasPart[o] == m.NodePart[m.Routers[s]])
                    (m.ShowWay[s], m.ShowState[s], m.ShowDef[s], m.ShowDest[s], m.ShowTag[s]) = (wasWay[o], wasState[o], wasDef[o], wasDest[o], wasTag[o]);
        m.ShowVersion++;
    }

    /// <summary>
    /// The next-hop column of destination slot <paramref name="ds"/>: one shortest-way pass back from it over the intact conveyors,
    /// through Routers and Joins only. False when this step's budget is spent (unless <paramref name="force"/>). Allocates nothing.
    /// </summary>
    public static bool Column(Construct c, LogiMap m, int ds, bool force = false)
    {
        if (m.Ready[ds])
            return true;
        if (!force)
        {
            if (_columnsLeft <= 0)
                return false;
            _columnsLeft--;
        }
        var dn = m.Dests[ds];
        Span<float> dist = stackalloc float[m.NodeN];
        Span<byte> narrow = stackalloc byte[m.NodeN];
        dist.Fill(float.MaxValue);
        narrow.Fill(255);
        dist[dn] = 0;
        // Relax every conveyor until nothing improves (a few passes on a real factory; at most one per node).
        for (var pass = 0; pass < m.NodeN; pass++)
        {
            var changed = false;
            for (var k = 0; k < c.RouteCount; k++)
            {
                ref readonly var r = ref c.Routes[k];
                if (!Usable(c, r))
                    continue;
                int u = m.NodeOf[r.From], v = m.NodeOf[r.To];
                if (u < 0 || v < 0 || dist[v] == float.MaxValue || v != dn && !m.Through[v])
                    continue;
                var cost = dist[v] + m.EdgeLen[k] + (v == dn ? 0 : JunctionCost);
                if (cost < dist[u])
                {
                    dist[u] = cost;
                    narrow[u] = (byte)Math.Min(v == dn ? 255 : narrow[v], r.ConvW / ConstructConveyor.StepCm);
                    changed = true;
                }
            }
            if (!changed)
                break;
        }
        for (var s = 0; s < m.RouterN; s++)
        {
            var at = s * LogiMap.MaxDests + ds;
            m.Hop[at] = LogiMap.None;
            m.Dist[at] = float.MaxValue;
            m.Narrow[at] = 0;
            int rn = m.Routers[s];
            for (var k = 0; k < c.RouteCount; k++)
            {
                ref readonly var r = ref c.Routes[k];
                if (!Usable(c, r) || m.NodeOf[r.From] != rn)
                    continue;
                int v = m.NodeOf[r.To];
                if (v < 0 || dist[v] == float.MaxValue || v != dn && !m.Through[v] || v == rn)
                    continue;
                var cost = dist[v] + m.EdgeLen[k] + (v == dn ? 0 : JunctionCost);
                if (cost < m.Dist[at])
                {
                    m.Dist[at] = cost;
                    m.Hop[at] = (byte)k;
                    m.Narrow[at] = (byte)Math.Min(v == dn ? 255 : narrow[v], r.ConvW / ConstructConveyor.StepCm);
                }
            }
        }
        m.Ready[ds] = true;
        return true;
    }

    /// <summary>Does Router <paramref name="router"/> have a way to part <paramref name="dest"/> (a terminal's fetch through it)? Either peer.</summary>
    public static bool Reaches(Construct c, int router, int dest)
    {
        var m = Map(c);
        var s = m.RouterSlot(router);
        var ds = m.DestSlot(dest);
        if (s < 0 || ds < 0)
            return false;
        Column(c, m, ds, force: true);
        return m.Hop[s * LogiMap.MaxDests + ds] != LogiMap.None;
    }

    // ---- Wants (from the terminals' orders) ------------------------------------------------------------------------------

    /// <summary>Riders bound for store <paramref name="handle"/> (for order <paramref name="tag"/>, or any when 0) that have <paramref name="all"/> of these flags and none of <paramref name="none"/>.</summary>
    private static int Riding(Construct c, ushort handle, ushort tag, LogiFlags all, LogiFlags none)
    {
        var n = 0;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref readonly var r = ref c.Conveyed[i];
            var f = (LogiFlags)r.Flags;
            if (r.Dest == handle && (tag == 0 || r.Tag == tag) && (f & all) == all && (f & none) == 0)
                n++;
        }
        return n;
    }

    /// <summary>The order a piece of kind <paramref name="d"/> could serve at terminal destination <paramref name="ds"/>: its id, or 0. Prefers one asked of <paramref name="maker"/>.</summary>
    private static ushort WantAt(ConstructWorld world, Construct c, LogiMap m, int ds, ConstructPartDef d, int maker)
    {
        int t = m.NodePart[m.Dests[ds]];
        if (c.DefOf(t) is { IsAssembler: true } or { IsPanelFormer: true } or { IsPress: true, IsLathe: false })
            return ConstructLogisticsPlan.WantAt(world, c, t, d, maker); // (a chained order's input)
        if (c.Parts[t].Craft is not { Count: > 0 } q || world.StoreOf(c, t) is not { } tray)
            return 0;
        ushort any = 0;
        for (var k = 0; k < q.Count; k++)
        {
            ref var o = ref q.Items[k];
            if (o.Done || o.Cancelled || o.Asked == 0 || ConstructCrafting.KindOf(ref o) != d.Id)
                continue;
            if (o.Asked - Riding(c, tray.Handle, o.Id, LogiFlags.None, LogiFlags.Storage | LogiFlags.Fetched) <= 0)
                continue;
            if (maker < 0 || o.Where == maker)
                return o.Id;
            if (any == 0)
                any = o.Id;
        }
        return any;
    }

    /// <summary>
    /// Server: a machine's piece onto its conveyor (before it rides): when its network has a Router, the oldest order that asked this
    /// machine (else any order) for its kind becomes its destination. Else it gets one at its first Router (storage, as overflow).
    /// </summary>
    public static void AtSource(ConstructWorld world, Construct c, int machine, ref CRider r, ConstructPartDef d)
    {
        int into = -1;
        uint stamp = 0;
        var link = world.ChainCount > 0 ? ConstructLogisticsPlan.FromMaker(world, c, machine, d, out into, out stamp) : (ushort)0;
        if (link > 0 && !OlderAsked(c, machine, d, stamp))
        {
            (r.Dest, r.Tag, r.Flags) = (c.Parts[into].Store, link, 0); // (made for a chained order: to the machine that needs it, Router or not)
            return;
        }
        var m = world.CraftOpen.Contains(c.Id) ? Map(c) : null;
        var net = m?.NetOf(machine) ?? -1;
        for (var ds = 0; m != null && net >= 0 && m.NetHasRouter[net] && ds < m.DestN; ds++)
        {
            if (m.DestStore[ds] || m.NodeNet[m.Dests[ds]] != net || WantAt(world, c, m, ds, d, machine) is not (var id and > 0))
                continue;
            r.Dest = c.Parts[m.NodePart[m.Dests[ds]]].Store;
            r.Tag = id;
            r.Flags = 0;
            return;
        }
        if (link > 0)
            (r.Dest, r.Tag, r.Flags) = (c.Parts[into].Store, link, 0); // (the older terminal order had no way to be served here: the chain takes it)
    }

    /// <summary>Did a terminal order older than <paramref name="stamp"/> ask machine <paramref name="machine"/> for this kind (it goes first: class, then age)?</summary>
    private static bool OlderAsked(Construct c, int machine, ConstructPartDef d, uint stamp)
    {
        for (var t = 0; t < c.PartCount; t++)
        {
            if (c.Parts[t].Craft is not { Count: > 0 } q)
                continue;
            for (var k = 0; k < q.Count; k++)
            {
                ref var o = ref q.Items[k];
                if (o.Asked > 0 && o.Where == machine && o.Stamp < stamp && !o.Done && !o.Cancelled && ConstructCrafting.KindOf(ref o) == d.Id)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Does the way from Router slot <paramref name="s"/> to destination <paramref name="ds"/> exist and take a piece this wide?</summary>
    private static LogiReason WayFrom(Construct c, LogiMap m, int s, int ds, ConstructPartDef d)
    {
        Column(c, m, ds, force: true);
        var at = s * LogiMap.MaxDests + ds;
        if (m.Hop[at] == LogiMap.None)
            return LogiReason.NoWay;
        ConstructConveyor.Size(d, out var across, out var up, out _);
        return ConstructConveyor.Needs(across, up) > m.Narrow[at] * ConstructConveyor.StepCm / 100f + 0.005f ? LogiReason.Narrow : LogiReason.None;
    }

    /// <summary>
    /// Server: a new destination for the lead piece at Router slot <paramref name="s"/>: an order that wants it (unless it lost one
    /// already), else the nearest plain Piece Rack on its network with room for it (less what already rides there). False: none.
    /// </summary>
    private static bool Assign(ConstructWorld world, Construct c, LogiMap m, int s, ref CRider r, ConstructPartDef d, int skip)
    {
        var net = m.NodeNet[m.Routers[s]];
        if (((LogiFlags)r.Flags & LogiFlags.Overflow) == 0)
            for (var ds = 0; ds < m.DestN; ds++)
            {
                if (m.DestStore[ds] || ds == skip || m.NodeNet[m.Dests[ds]] != net || WantAt(world, c, m, ds, d, -1) is not (var id and > 0))
                    continue;
                if (WayFrom(c, m, s, ds, d) != LogiReason.None)
                    continue;
                r.Dest = c.Parts[m.NodePart[m.Dests[ds]]].Store;
                r.Tag = id;
                r.Flags = (byte)((LogiFlags)r.Flags & LogiFlags.ReasonMask);
                return true;
            }
        var best = -1;
        var bestDist = float.MaxValue;
        for (var ds = 0; ds < m.DestN; ds++)
        {
            if (!m.DestStore[ds] || ds == skip || m.NodeNet[m.Dests[ds]] != net)
                continue;
            int rack = m.NodePart[m.Dests[ds]];
            if (world.StoreOf(c, rack) is not { } st || ConstructConveyor.RackCheck(c, rack, st, d) != 0 || !StockTargets.Accepts(world, c, rack, st, d))
                continue; // (its Takes filter holds for overflow too; a store always takes what it keeps)
            if (st.ItemCount + Riding(c, st.Handle, 0, LogiFlags.Storage, LogiFlags.None) >= c.DefOf(rack).RackCount)
                continue;
            if (WayFrom(c, m, s, ds, d) != LogiReason.None)
                continue;
            var dist = m.Dist[s * LogiMap.MaxDests + ds] - StockTargets.Priority(c, rack) * 1e4f; // (its store priority first, then the shorter way)
            if (world.StockTargetN > 0 && StockTargets.Keeps(world, c, st.Handle, (ushort)d.Id))
                dist -= 1e5f; // (a store that keeps this kind before any other)
            if (dist < bestDist)
            {
                bestDist = dist;
                best = ds;
            }
        }
        if (best < 0)
            return false;
        r.Dest = c.Parts[m.NodePart[m.Dests[best]]].Store;
        r.Tag = 0;
        r.Flags = (byte)(((LogiFlags)r.Flags & (LogiFlags.Overflow | LogiFlags.ReasonMask)) | LogiFlags.Storage);
        return true;
    }

    private static void SetReason(ref CRider r, LogiReason why) => r.Flags = (byte)(((LogiFlags)r.Flags & ~LogiFlags.ReasonMask) | (LogiFlags)((int)why << 4));

    public static LogiReason ReasonOf(in CRider r) => (LogiReason)((r.Flags >> 4) & 15);

    /// <summary>Is the piece's destination still open: an order that still lists it (and a tray with room), or a rack with room?</summary>
    private static LogiReason Open(ConstructWorld world, Construct c, LogiMap m, int ds, in CRider r, ConstructPartDef d)
    {
        int part = m.NodePart[m.Dests[ds]];
        if (world.StoreOf(c, part) is not { } st)
            return LogiReason.Closed;
        if (c.DefOf(part) is { IsPieceRack: false } md)
            return (md.IsAssembler || md.IsPanelFormer || md.IsPress) && ConstructLogisticsPlan.Open(world, c, part, r.Tag) ? LogiReason.None : LogiReason.Closed; // (a machine's input waits at its door)
        if (((LogiFlags)r.Flags & LogiFlags.Storage) == 0 && (r.Tag == 0 || c.Parts[part].Craft is not { } q || q.Find(r.Tag) < 0) && !(c.Parts[part].Craft == null && ConstructLogisticsPlan.Open(world, c, part, r.Tag)))
            return LogiReason.Closed; // (a plain rack: its stock target's refill, phase 3)
        return ConstructConveyor.RackCheck(c, part, st, d) != 0 ? LogiReason.DestFull : LogiReason.None;
    }

    /// <summary>
    /// Server: the lead piece <paramref name="lead"/> of conveyor <paramref name="k"/> at a powered Router (slot <paramref name="s"/>):
    /// onto the way to its destination, given one first if it has none or its destination closed; to storage when its way is cut, too
    /// narrow or full for 5 s; held, stuck, when there is nowhere. True when it crossed; <paramref name="changed"/>: its flags changed.
    /// </summary>
    public static bool Pass(ConstructWorld world, Construct c, LogiMap m, int s, int k, int lead, ref bool changed)
    {
        ref var r = ref c.Conveyed[lead];
        var d = ConstructParts.Def(r.Def);
        var flags0 = r.Flags;
        if (((LogiFlags)r.Flags & LogiFlags.Stuck) != 0)
        {
            if (r.Wait < 255)
                r.Wait++;
            if (r.Wait % RetrySteps != 0)
                return false;
        }
        var ds = -1;
        if (r.Dest != 0)
            for (var i = 0; i < m.DestN; i++)
                if (c.Parts[m.NodePart[m.Dests[i]]].Store == r.Dest)
                    ds = i;
        var why = ds < 0 ? (r.Dest != 0 ? LogiReason.Closed : LogiReason.None) : m.NodeNet[m.Dests[ds]] != m.NodeNet[m.Routers[s]] ? LogiReason.NoWay : Open(world, c, m, ds, r, d);
        if (r.Hops >= MaxHops)
        {
            why = LogiReason.Loop;
            r.Hops = 0;
        }
        int router = m.NodePart[m.Routers[s]];
        var spill = StockTargets.Spill(c, router); // (phase 3: routerSpill pins where unordered pieces go)
        var ordered = r.Dest != 0 && ((LogiFlags)r.Flags & LogiFlags.Storage) == 0;
        if (ordered && why == LogiReason.NoWay && StockTargets.Hold(c, router))
            return Held(c, ref r, why, flags0, ref changed); // (routerHold: an order's piece with no way waits here)
        if (ds < 0 || why != LogiReason.None)
        {
            if (!Reassign(world, c, m, s, ref r, d, ds, why))
                return spill != StockTargets.SpillAuto ? Spill(c, m, s, k, lead, spill, flags0, ref changed) : Stuck(c, m, s, ref r, flags0, ref changed);
            ds = -1;
            for (var i = 0; i < m.DestN; i++)
                if (c.Parts[m.NodePart[m.Dests[i]]].Store == r.Dest)
                    ds = i;
            if (ds < 0)
                return Stuck(c, m, s, ref r, flags0, ref changed);
        }
        if (spill != StockTargets.SpillAuto && ((LogiFlags)r.Flags & LogiFlags.Storage) != 0)
            return Spill(c, m, s, k, lead, spill, flags0, ref changed); // (the pin wins for pieces going to storage; ordered pieces follow their order)
        if (!Column(c, m, ds))
        {
            changed |= r.Flags != flags0;
            return false; // (its column is made next step)
        }
        var way = WayFrom(c, m, s, ds, d);
        if (way != LogiReason.None)
        {
            if (((LogiFlags)r.Flags & LogiFlags.Storage) == 0 && StockTargets.Hold(c, router))
                return Held(c, ref r, way, flags0, ref changed);
            if (!Reassign(world, c, m, s, ref r, d, ds, way))
                return Stuck(c, m, s, ref r, flags0, ref changed);
            changed |= r.Flags != flags0;
            return false; // (the new way next step)
        }
        int o = m.Hop[s * LogiMap.MaxDests + ds];
        var wasHeld = ((LogiFlags)r.Flags & LogiFlags.Stuck) != 0;
        r.Flags &= unchecked((byte)~LogiFlags.Stuck);
        if (!ConstructConveyor.PassTo(c, k, lead, o))
        {
            if (wasHeld)
                r.Flags |= (byte)LogiFlags.Stuck;
            if (r.Wait < 255)
                r.Wait++;
            if (r.Wait >= FullSteps && ((LogiFlags)r.Flags & LogiFlags.Storage) == 0)
            {
                r.Wait = 0;
                if (StockTargets.Hold(c, router))
                    return Held(c, ref r, LogiReason.DestFull, flags0, ref changed);
                if (!Reassign(world, c, m, s, ref r, d, ds, LogiReason.DestFull))
                    return Stuck(c, m, s, ref r, flags0, ref changed);
            }
            changed |= r.Flags != flags0;
            return false;
        }
        Shown(c, m, s, o, in c.Conveyed[LastOn(c, o)]);
        return true;
    }

    /// <summary>routerHold: an order's piece that can't get on waits at the Router with its order kept, marked stuck (tried again every 2 s).</summary>
    private static bool Held(Construct c, ref CRider r, LogiReason why, byte flags0, ref bool changed)
    {
        if (((LogiFlags)r.Flags & LogiFlags.Stuck) == 0)
            r.Wait = 0;
        SetReason(ref r, why);
        r.Flags |= (byte)LogiFlags.Stuck;
        changed |= r.Flags != flags0;
        return false;
    }

    /// <summary>routerSpill pinned: a piece for storage (or with nowhere to go) leaves by way <paramref name="spill"/> (1 ahead, 2 and 3 the sides); else waits.</summary>
    private static bool Spill(Construct c, LogiMap m, int s, int k, int lead, int spill, byte flags0, ref bool changed)
    {
        int router = m.NodePart[m.Routers[s]];
        ref var r = ref c.Conveyed[lead];
        r.Flags = (byte)(((LogiFlags)r.Flags & (LogiFlags.Overflow | LogiFlags.ReasonMask | LogiFlags.Storage)) | LogiFlags.Storage);
        if (r.Dest != 0 && PartOfStore(c, r.Dest) is var dp and >= 0 && m.DestSlot(dp) is var dsl and >= 0 && !m.DestStore[dsl])
            r.Dest = 0; // (a store keeps its destination: the next Router sends it on; anything else is dropped here)
        for (var o = 0; o < c.RouteCount; o++)
        {
            if (c.Routes[o].Type != RouteType.Conveyor || c.Routes[o].From != router || WayOf(c, o) != spill)
                continue;
            if (!ConstructConveyor.PassTo(c, k, lead, o))
            {
                if (r.Wait < 255)
                    r.Wait++;
                changed |= r.Flags != flags0;
                return false;
            }
            Shown(c, m, s, o, in c.Conveyed[LastOn(c, o)]);
            return true;
        }
        changed |= r.Flags != flags0;
        return false; // (no conveyor on that way: it waits, as a piece with nowhere to go)
    }

    /// <summary>A new destination after <paramref name="why"/>: an order's piece goes to storage as overflow; a stored one to another store.</summary>
    private static bool Reassign(ConstructWorld world, Construct c, LogiMap m, int s, ref CRider r, ConstructPartDef d, int lost, LogiReason why)
    {
        var hadOrder = r.Dest != 0 && ((LogiFlags)r.Flags & LogiFlags.Storage) == 0;
        if (hadOrder && why is not (LogiReason.None or LogiReason.Closed) && lost >= 0 && c.Parts[m.NodePart[m.Dests[lost]]].Craft is { } q && q.Find(r.Tag) is var k and >= 0)
            q.Items[k].Lost = (byte)why; // (its order's row says why a part went to storage)
        SetReason(ref r, why);
        if (hadOrder && why != LogiReason.None)
            r.Flags |= (byte)LogiFlags.Overflow;
        r.Dest = 0;
        r.Tag = 0;
        r.Flags &= unchecked((byte)~(LogiFlags.Storage | LogiFlags.Fetched));
        return Assign(world, c, m, s, ref r, d, why == LogiReason.None ? -1 : lost);
    }

    private static bool Stuck(Construct c, LogiMap m, int s, ref CRider r, byte flags0, ref bool changed)
    {
        if (((LogiFlags)r.Flags & LogiFlags.Stuck) == 0)
            r.Wait = 0;
        if (ReasonOf(r) is LogiReason.None or LogiReason.Closed)
            SetReason(ref r, LogiReason.NoStorage);
        r.Flags |= (byte)LogiFlags.Stuck;
        r.Dest = 0;
        r.Tag = 0;
        changed |= r.Flags != flags0;
        return false;
    }

    private static int LastOn(Construct c, int route)
    {
        for (var i = c.ConveyedCount - 1; i >= 0; i--)
            if (c.Conveyed[i].Route == route)
                return i;
        return 0;
    }

    // ---- What a Router shows ---------------------------------------------------------------------------------------------

    /// <summary>Which of a Router's ways (1 ahead, 2 and 3 the sides, in its port order) conveyor <paramref name="route"/> leaves by; 0 none.</summary>
    public static byte WayOf(Construct c, int route)
    {
        ref readonly var r = ref c.Routes[route];
        if (r.From >= c.PartCount)
            return 0;
        var n = 0;
        foreach (var np in c.DefOf(r.From).NamedPorts)
            if (np.Type == RouteType.Conveyor && np.Out)
            {
                n++;
                if (np.Byte == r.FromPort)
                    return (byte)Math.Min(n, 3);
            }
        return 0;
    }

    /// <summary>A piece crossed Router slot <paramref name="s"/> onto conveyor <paramref name="o"/>: its lamps and fence follow.</summary>
    private static void Shown(Construct c, LogiMap m, int s, int o, in CRider r)
    {
        var way = WayOf(c, o);
        var state = (byte)(((LogiFlags)r.Flags & LogiFlags.Storage) != 0 || r.Dest == 0 ? 2 : 1);
        if (m.ShowWay[s] != way)
        {
            m.ShowFrom[s] = m.ShowWay[s];
            m.ShowAt[s] = ConstructConveyor.Clock;
        }
        m.ShowWay[s] = way;
        m.ShowState[s] = state;
        m.ShowDef[s] = r.Def;
        m.ShowDest[s] = r.Dest;
        m.ShowTag[s] = r.Tag;
        m.ShowVersion++;
    }

    /// <summary>
    /// Either peer, after the riders changed: each Router's last choice from the riders still in it (a client hears no choice), and
    /// whether the lead piece at its input is stuck. The server also sets its Alarm (out 0) and Moving (out 1) outputs.
    /// </summary>
    public static void Refresh(Construct c, bool server)
    {
        var m = Map(c);
        if (m.RouterN == 0)
            return;
        Span<bool> stuck = stackalloc bool[LogiMap.MaxRouters];
        Span<ushort> stuckDef = stackalloc ushort[LogiMap.MaxRouters];
        Span<byte> reason = stackalloc byte[LogiMap.MaxRouters];
        Span<int> moving = stackalloc int[LogiMap.MaxNodes];
        stuck.Clear();
        moving[..m.NodeN].Clear();
        var prev = -1;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref readonly var r = ref c.Conveyed[i];
            if (r.Route >= c.RouteCount)
                continue;
            ref readonly var rt = ref c.Routes[r.Route];
            if (rt.To >= c.PartCount || rt.From >= c.PartCount)
                continue;
            var from = m.NodeOf[rt.From];
            if (from >= 0)
                moving[m.NodeNet[from]]++;
            var lead = r.Route != prev;
            prev = r.Route;
            var s = m.RouterSlot(rt.To);
            if (lead && s >= 0 && ((LogiFlags)r.Flags & LogiFlags.Stuck) != 0)
            {
                stuck[s] = true;
                stuckDef[s] = r.Def;
                reason[s] = (byte)ReasonOf(r);
            }
            // A piece still in a Router (its back on the input): the way it took is the Router's choice (what a client draws).
            if (!server && r.ViaPlus1 != 0 && m.RouterSlot(rt.From) is var via and >= 0)
            {
                var way = WayOf(c, r.Route);
                var state = (byte)(((LogiFlags)r.Flags & LogiFlags.Storage) != 0 || r.Dest == 0 ? 2 : 1);
                if (m.ShowWay[via] != way || m.ShowState[via] != state || m.ShowDef[via] != r.Def || m.ShowDest[via] != r.Dest)
                {
                    if (m.ShowWay[via] != way)
                    {
                        m.ShowFrom[via] = m.ShowWay[via];
                        m.ShowAt[via] = ConstructConveyor.Clock;
                    }
                    (m.ShowWay[via], m.ShowState[via], m.ShowDef[via], m.ShowDest[via], m.ShowTag[via]) = (way, state, r.Def, r.Dest, r.Tag);
                    m.ShowVersion++;
                }
            }
        }
        var any = false;
        for (var s = 0; s < m.RouterN; s++)
        {
            if (m.ShowStuck[s] != stuck[s] || stuck[s] && (m.ShowStuckDef[s] != stuckDef[s] || m.ShowReason[s] != reason[s]))
            {
                (m.ShowStuck[s], m.ShowStuckDef[s], m.ShowReason[s]) = (stuck[s], stuckDef[s], reason[s]);
                m.ShowVersion++;
            }
            if (!server)
                continue;
            ref var p = ref c.Parts[m.NodePart[m.Routers[s]]];
            var alarm = stuck[s] ? 1f : 0f;
            float move = moving[m.NodeNet[m.Routers[s]]];
            if (p.Out != alarm || p.Out1 != move)
            {
                p.Out = p.OutNext = alarm;
                p.Out1 = move;
                c.Wake();
            }
            any |= alarm != 0 || move != 0;
        }
        if (server)
            m.AnyOut = any;
    }

    /// <summary>Draw: a Router's shown way (0 none), its state (1 order, 2 storage), whether its input is stuck, and its fence's turn now (radians about its up).</summary>
    public static bool ShowOf(Construct c, int part, out byte way, out byte state, out bool stuck, out float fence)
    {
        way = state = 0;
        stuck = false;
        fence = 0;
        if (c.Logi is not { } m || m.Stamp != c.RebuildStamp)
            return false;
        var s = m.RouterSlot(part);
        if (s < 0)
            return false;
        (way, state, stuck) = (m.ShowWay[s], m.ShowState[s], m.ShowStuck[s]);
        var to = Angle(way);
        var t = (float)Math.Clamp((ConstructConveyor.Clock - m.ShowAt[s]) / 0.3, 0, 1);
        fence = Angle(m.ShowFrom[s]) + (to - Angle(m.ShowFrom[s])) * (t * t * (3 - 2 * t));
        return true;
    }

    private static float Angle(byte way) => way switch { 2 => MathF.PI / 4, 3 => -MathF.PI / 4, _ => 0 };

    // ---- Text (either peer, from what it heard) --------------------------------------------------------------------------

    /// <summary>The part a store handle belongs to on this body, or -1 (a look line: rare).</summary>
    public static int PartOfStore(Construct c, ushort handle)
    {
        if (handle == 0)
            return -1;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Store == handle)
                return i;
        return -1;
    }

    public static string ReasonText(LogiReason why) => why switch
    {
        LogiReason.NoWay => "no way to it: a conveyor on the way is cut or missing",
        LogiReason.Narrow => "the way to it is too narrow for the piece",
        LogiReason.DestFull => "its destination is full",
        LogiReason.Closed => "its order closed",
        LogiReason.Loop => "going round: a Splitter keeps sending it back",
        LogiReason.NoStorage => "no Piece Rack on the network has room for it",
        _ => "",
    };

    /// <summary>"-> Crafting Terminal (order 3)", "-> storage: Piece Rack (overflow: its order closed)", "stuck: ..." for a rider; null when it has nothing.</summary>
    public static string? RiderNote(Construct c, in CRider r)
    {
        var f = (LogiFlags)r.Flags;
        if ((f & LogiFlags.Stuck) != 0)
            return "stuck: " + ReasonText(ReasonOf(r));
        if (r.Dest == 0)
            return null;
        var part = PartOfStore(c, r.Dest);
        var name = part >= 0 ? c.DefOf(part).Name : "a store";
        if ((f & LogiFlags.Storage) != 0)
            return $"-> storage: {name}" + ((f & LogiFlags.Overflow) != 0 && ReasonOf(r) != LogiReason.None ? $" ({ReasonText(ReasonOf(r))})" : "");
        return $"-> {name} (order {r.Tag})";
    }

    /// <summary>A Router's look line: its last piece and where it went, the piece it holds and why, or its ways and destinations.</summary>
    public static string Look(ConstructWorld world, Construct c, int part)
    {
        var d = c.DefOf(part);
        var m = Map(c);
        var s = m.RouterSlot(part);
        if (!ConstructRecycler.Powered(world, c, part))
            return $"{d.Name}: no power - deals in turn like a Splitter";
        if (s < 0)
            return $"{d.Name}: not on a conveyor";
        if (m.ShowStuck[s])
            return $"{d.Name}: holding {PieceName(m.ShowStuckDef[s])} - stuck: {ReasonText((LogiReason)m.ShowReason[s])}";
        var net = m.NodeNet[m.Routers[s]];
        int ways = 0, dests = 0, stores = 0;
        for (var k = 0; k < c.RouteCount; k++)
            if (Usable(c, c.Routes[k]) && c.Routes[k].From == part)
                ways++;
        for (var ds = 0; ds < m.DestN; ds++)
            if (m.NodeNet[m.Dests[ds]] == net)
            {
                dests++;
                if (m.DestStore[ds])
                    stores++;
            }
        var idle = $"{ways} way{(ways == 1 ? "" : "s")} out, {dests} destination{(dests == 1 ? "" : "s")} ({stores} for storage)";
        if (m.ShowWay[s] == 0)
            return $"{d.Name}: {idle}";
        var to = PartOfStore(c, m.ShowDest[s]);
        var where = to < 0 ? "storage" : m.ShowState[s] == 2 ? $"storage: {c.DefOf(to).Name}" : $"{c.DefOf(to).Name} (order {m.ShowTag[s]})";
        return $"{d.Name}: last piece {PieceName(m.ShowDef[s])} -> {where} - {idle}";
    }

    private static string PieceName(ushort def) => ConstructParts.Get(def) is { } pd ? pd.IsExtrusion ? Extrusion.Label(pd.ExKey) : pd.Name : "a piece";
}

