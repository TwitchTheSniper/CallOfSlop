using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The network board (docs/sandbox/design/smart-logistics.md section 9, phase 4; production.md "Smart logistics"): a display wired to a
/// Conveyor Router's <b>Data out</b> shows that Router's conveyor network, laid out by the rows it has, as every monitor is
/// (<see cref="ProductionLines.MonitorRows"/>): one line (tiny: the counts, or the stuck spot in red); a title, the stuck spots, the open
/// orders and the stores by fill (small); and from 7 rows the counts, then the stuck spots with their reasons, each open order with its
/// chain lines and where each is held up, the machines with their state and the stores with their fill and what they keep (medium and
/// large). Any Router of the network gives the same board. A list longer than the panel moves on by itself, as a monitor list does.
/// <para>
/// Everything comes from what every peer holds (the logistics map, the riders, the Routers' shown choice and stuck piece, the stores, the
/// machines' states and the terminals' orders with their chain lines), so a client's board says what the host's does. Filled with the
/// panels (4 Hz, in view); nothing here allocates once each kind's name has been seen (<see cref="Names"/>).
/// </para>
/// <para>The map of design 9.2 (<c>PanelDraw.Map</c>) is not built yet: a large panel shows the medium list (phase 4 notes).</para>
/// </summary>
public static class ConstructLogisticsBoard
{
    private const int MaxRows = 160; // rows of the list before it scrolls (more are not listed)

    private enum Row : byte { Heading, Stuck, Order, Chain, Machine, Store, Router }

    private struct Entry
    {
        public Row Kind;
        public int Part; // the Router, terminal, machine or store
        public int Index; // a Router's number on the network, an order's index, a chain line's index; a heading's text
        public int Order; // a chain line's order index
    }

    private static readonly Entry[] Rows = new Entry[MaxRows];
    private static readonly string[] Headings = ["Stuck", "Orders", "Machines", "Stores", "Routers"];

    /// <summary>The network's counts, worked out once a fill.</summary>
    private struct Counts
    {
        public int Moving, Orders, Stuck, Machines, Running, Stores, Routers;
        public float Fill; // stores' mean fill, 0..1 (-1: no store)
        public int FirstStuck; // the first stuck Router's part, or -1
        public int FirstStuckNo;
    }

    /// <summary>Fills a monitor's card from Router <paramref name="router"/>'s network (ProductionLines.FillMonitor).</summary>
    public static void Fill(ConstructWorld world, Construct c, int router, PanelCard card)
    {
        var m = ConstructLogistics.Map(c);
        var node = (uint)router < (uint)m.NodeOf.Length ? m.NodeOf[router] : (short)-1;
        if (node < 0)
        {
            card.State = PanelState.Idle;
            card.Add(0, "Network: no conveyors");
            return;
        }
        var net = m.NodeNet[node];
        var k = CountUp(world, c, m, net);
        card.State = k.Stuck > 0 ? PanelState.Fault : k.Moving > 0 || k.Running > 0 ? PanelState.Working : PanelState.Idle;
        var cols = card.Cols;
        if (card.Rows == 1)
        {
            if (k.FirstStuck >= 0)
                StuckLine(c, m, k.FirstStuck, k.FirstStuckNo, card, 0, true);
            else
                Summary(card, 0, k);
            return;
        }
        var medium = card.Rows >= 7;
        // The title: the stuck spot first, in red, else the counts.
        if (k.FirstStuck >= 0)
        {
            card.Add(0, "NETWORK - STUCK at Router ").Add(0, k.FirstStuckNo);
            if (k.Stuck > 1)
                card.Add(0, " +").Add(0, k.Stuck - 1).Add(0, " more");
            card.Tint(0, 0, card.Length(0), StuckRed).Clip(0, cols);
        }
        else
        {
            Summary(card, 0, k);
            card.Clip(0, cols);
        }
        if (card.Rows >= MapRows && card.PanelSize.X >= MapWidth)
        {
            // A big sign: the plan map (design 9.2; NetPlan, drawn by PanelRender.MapPlot) between the title and a two-line caption.
            card.Draw = PanelDraw.Map;
            card.Plan = NetPlan.Of(world, c, m, net);
            int l1 = card.Rows - 2, l2 = card.Rows - 1;
            if (k.FirstStuck >= 0)
                StuckLine(c, m, k.FirstStuck, k.FirstStuckNo, card, l1, true);
            else
                card.Add(l1, "All moving");
            card.Clip(l1, cols);
            card.Add(l2, k.Moving).Add(l2, " moving  ").Add(l2, k.Orders).Add(l2, k.Orders == 1 ? " order" : " orders");
            if (k.Fill >= 0)
                card.Add(l2, "  storage ").Add(l2, (int)MathF.Round(k.Fill * 100)).Add(l2, '%');
            card.Clip(l2, cols).Dim(l2);
            return;
        }
        var top = 1;
        if (medium)
        {
            card.Add(1, "Moving ").Add(1, k.Moving).Add(1, "  Orders ").Add(1, k.Orders).Add(1, "  Stuck ").Add(1, k.Stuck)
                .Add(1, "  Running ").Add(1, k.Running).Add(1, '/').Add(1, k.Machines).Clip(1, cols).Dim(1);
            top = 2;
        }
        var n = List(world, c, m, net, medium);
        var fit = card.Rows - top;
        if (n == 0)
        {
            card.Add(top, "Nothing on this network yet");
            return;
        }
        var first = ProductionLines.AutoFirst(n, fit, ConstructRender.Now);
        for (var r = first; r < n && r - first < fit; r++)
            Draw(world, c, m, Rows[r], card, top + r - first, cols, medium);
        card.Bar = k.Fill;
    }

    private static readonly Vector3 StuckRed = new(1f, 0.25f, 0.2f), Wanting = new(1f, 0.7f, 0.2f);
    private const int MapRows = 15; // a board this tall...
    private const float MapWidth = 0.8f; // ...on a panel this wide (metres) is the plan map; else the list (design 9.1)

    /// <summary>"Network: 14 moving, 3 orders, storage 57%".</summary>
    private static void Summary(PanelCard card, int line, in Counts k)
    {
        card.Add(line, "Network: ").Add(line, k.Moving).Add(line, " moving, ").Add(line, k.Orders).Add(line, k.Orders == 1 ? " order" : " orders");
        if (k.Fill >= 0)
            card.Add(line, ", storage ").Add(line, (int)MathF.Round(k.Fill * 100)).Add(line, '%');
    }

    private static Counts CountUp(ConstructWorld world, Construct c, LogiMap m, int net)
    {
        var k = new Counts { FirstStuck = -1, Fill = -1 };
        float fill = 0;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref readonly var r = ref c.Conveyed[i];
            if (r.Route < c.RouteCount && c.Routes[r.Route].From < c.PartCount && m.NetOf(c.Routes[r.Route].From) == net)
                k.Moving++;
        }
        var routerNo = 0;
        for (var nd = 0; nd < m.NodeN; nd++)
        {
            if (m.NodeNet[nd] != net)
                continue;
            int part = m.NodePart[nd];
            if (part >= c.PartCount)
                continue;
            var d = c.DefOf(part);
            if (m.RouterOfNode[nd] is var s and >= 0)
            {
                k.Routers++;
                routerNo++;
                if (m.ShowStuck[s])
                {
                    k.Stuck++;
                    if (k.FirstStuck < 0)
                        (k.FirstStuck, k.FirstStuckNo) = (part, routerNo);
                }
            }
            else if (d.IsCraftTerminal)
            {
                if (c.Parts[part].Craft is { } q)
                    for (var o = 0; o < q.Count; o++)
                        if (!q.Items[o].Done)
                            k.Orders++;
            }
            else if (StockTargets.IsStore(d) && world.StoreOf(c, part) is { } st)
            {
                ConstructRackPack.Ensure(d, st);
                fill += ConstructRackPack.Fill(d, st);
                k.Stores++;
            }
            else if (d.Monitor == MonitorKind.Machine && world.StoreOf(c, part) is { } ms)
            {
                k.Machines++;
                if (ProductionLines.Of(world, c, part, ms, ConstructRender.Now) == LineState.Running)
                    k.Running++;
            }
        }
        if (k.Stores > 0)
            k.Fill = fill / k.Stores;
        return k;
    }

    /// <summary>The board's rows in order: stuck spots, orders (with their chain lines on a medium board), machines, stores, Routers.</summary>
    private static int List(ConstructWorld world, Construct c, LogiMap m, int net, bool medium)
    {
        var n = 0;
        void Add(Row kind, int part, int index = 0, int order = 0)
        {
            if (n < MaxRows)
                Rows[n++] = new Entry { Kind = kind, Part = part, Index = index, Order = order };
        }
        // Stuck spots.
        var mark = n;
        var routerNo = 0;
        for (var nd = 0; nd < m.NodeN; nd++)
            if (m.NodeNet[nd] == net && m.RouterOfNode[nd] is var s and >= 0)
            {
                routerNo++;
                if (m.ShowStuck[s] && m.NodePart[nd] < c.PartCount)
                    Add(Row.Stuck, m.NodePart[nd], routerNo);
            }
        // Open orders (a medium board: each with its chain lines).
        mark = Heading(mark, ref n, 1, medium);
        for (var nd = 0; nd < m.NodeN; nd++)
        {
            if (m.NodeNet[nd] != net || m.NodePart[nd] >= c.PartCount)
                continue;
            int t = m.NodePart[nd];
            if (!c.DefOf(t).IsCraftTerminal || c.Parts[t].Craft is not { } q)
                continue;
            for (var o = 0; o < q.Count; o++)
            {
                if (q.Items[o].Done)
                    continue;
                Add(Row.Order, t, o);
                if (medium && q.Items[o].Lines is { } lines)
                    for (var l = 0; l < lines.Length; l++)
                        Add(Row.Chain, t, l, o);
            }
        }
        mark = Heading(mark, ref n, 2, medium);
        // Machines (a medium board), then stores, then Routers (a medium board).
        if (medium)
            for (var nd = 0; nd < m.NodeN; nd++)
                if (m.NodeNet[nd] == net && m.NodePart[nd] < c.PartCount && m.RouterOfNode[nd] < 0 && c.DefOf(m.NodePart[nd]).Monitor == MonitorKind.Machine)
                    Add(Row.Machine, m.NodePart[nd]);
        mark = Heading(mark, ref n, 3, medium);
        for (var nd = 0; nd < m.NodeN; nd++)
            if (m.NodeNet[nd] == net && m.NodePart[nd] < c.PartCount && StockTargets.IsStore(c.DefOf(m.NodePart[nd])))
                Add(Row.Store, m.NodePart[nd]);
        mark = Heading(mark, ref n, 4, medium);
        if (medium)
        {
            routerNo = 0;
            for (var nd = 0; nd < m.NodeN; nd++)
                if (m.NodeNet[nd] == net && m.RouterOfNode[nd] >= 0 && m.NodePart[nd] < c.PartCount)
                    Add(Row.Router, m.NodePart[nd], ++routerNo);
            Heading(mark, ref n, 5, medium);
        }
        return n;
    }

    /// <summary>
    /// A medium board puts a heading over each part of the list that has rows (the rows from <paramref name="from"/> up), by moving
    /// them down one; returns where the next part starts.
    /// </summary>
    private static int Heading(int from, ref int n, int which, bool medium)
    {
        if (!medium || n == from)
            return n;
        if (n >= MaxRows)
            n = MaxRows - 1;
        Array.Copy(Rows, from, Rows, from + 1, n - from);
        Rows[from] = new Entry { Kind = Row.Heading, Index = which - 1 };
        return ++n;
    }

    private static void Draw(ConstructWorld world, Construct c, LogiMap m, in Entry e, PanelCard card, int line, int cols, bool medium)
    {
        switch (e.Kind)
        {
            case Row.Heading:
                card.Add(line, Headings[e.Index]).Dim(line);
                break;
            case Row.Stuck:
                StuckLine(c, m, e.Part, e.Index, card, line, false);
                break;
            case Row.Order:
                OrderLine(c, e.Part, e.Index, card, line, medium);
                break;
            case Row.Chain:
                ChainLine(c, e.Part, e.Order, e.Index, card, line);
                break;
            case Row.Machine:
            {
                var d = c.DefOf(e.Part);
                if (world.StoreOf(c, e.Part) is not { } st)
                    break;
                var s = ProductionLines.Of(world, c, e.Part, st, ConstructRender.Now);
                card.Add(line, ' ').Add(line, d.Name).Add(line, ' ');
                var from = card.Length(line);
                card.Add(line, ProductionLines.Word(s)).Tint(line, from, card.Length(line), LampOf(s));
                if (c.Parts[e.Part].Orders is { Bill: false } q && q.Total > 0)
                {
                    ref var h = ref q.Head;
                    card.Add(line, ' ').Add(line, h.Made).Add(line, '/').Add(line, h.Count);
                    if (q.Total > 1)
                        card.Add(line, " +").Add(line, q.Total - 1);
                }
                break;
            }
            case Row.Store:
            {
                var d = c.DefOf(e.Part);
                card.Add(line, ' ').Add(line, d.Name).Add(line, ' ');
                if (world.StoreOf(c, e.Part) is { } st)
                {
                    ConstructRackPack.Ensure(d, st);
                    var fill = ConstructRackPack.Fill(d, st);
                    var from = card.Length(line);
                    card.Add(line, (int)MathF.Round(fill * 100)).Add(line, '%');
                    if (fill >= 0.98f)
                        card.Tint(line, from, card.Length(line), Wanting);
                    card.Add(line, ", ").Add(line, st.ItemCount).Add(line, st.ItemCount == 1 ? " piece" : " pieces");
                }
                if (StockTargets.Keep(c, e.Part) is var keep and > 0)
                    card.Add(line, ", keeps ").Add(line, keep);
                break;
            }
            case Row.Router:
                RouterLine(c, m, e.Part, e.Index, card, line);
                break;
        }
        card.Clip(line, cols);
    }

    /// <summary>"STUCK Router 2: Gear Set - no Piece Rack on the network has room for it", in red.</summary>
    private static void StuckLine(Construct c, LogiMap m, int router, int no, PanelCard card, int line, bool tiny)
    {
        var s = m.RouterSlot(router);
        card.Add(line, tiny ? "STUCK at Router " : "STUCK Router ").Add(line, no).Add(line, ": ");
        if (s >= 0)
            card.Add(line, Names.Piece(m.ShowStuckDef[s])).Add(line, " - ").Add(line, ConstructLogistics.ReasonText((LogiReason)m.ShowReason[s]));
        card.Tint(line, 0, card.Length(line), StuckRed);
    }

    /// <summary>"Gear Set 1/4 queued at the Assembler" (where it is held up, in amber, when it is).</summary>
    private static void OrderLine(Construct c, int t, int k, PanelCard card, int line, bool medium)
    {
        if (c.Parts[t].Craft is not { } q || k >= q.Count)
            return;
        ref readonly var o = ref q.Items[k];
        card.Add(line, Names.Order(o.What)).Add(line, ' ').Add(line, o.Got).Add(line, '/').Add(line, o.What.Count).Add(line, ' ');
        var from = card.Length(line);
        var where = o.Where < c.PartCount ? c.DefOf(o.Where).Name : null;
        switch (o.State)
        {
            case CraftState.Fetching: card.Add(line, "fetching from ").Add(line, where ?? "a store"); break;
            case CraftState.Queued: card.Add(line, "queued at ").Add(line, where ?? "a machine"); break;
            case CraftState.Riding: card.Add(line, "riding"); break;
            case CraftState.InTray: card.Add(line, "in the tray"); break;
            case CraftState.Collect: card.Add(line, "collect at ").Add(line, where ?? "its store"); break;
            case CraftState.Held: card.Add(line, "held"); break;
            default: card.Add(line, "waiting"); break;
        }
        if (!string.IsNullOrEmpty(o.Note))
            card.Add(line, ": ").Add(line, o.Note);
        if (o.State is CraftState.Held or CraftState.Collect || !string.IsNullOrEmpty(o.Note))
            card.Tint(line, from, card.Length(line), Wanting);
        if (medium && c.DefOf(t) is var td)
            card.Add(line, " (").Add(line, td.Name).Add(line, ')');
    }

    /// <summary>A chain line under its order: "  Gear Set 0/2 - Assembler", or why nobody makes it, in amber.</summary>
    private static void ChainLine(Construct c, int t, int k, int l, PanelCard card, int line)
    {
        if (c.Parts[t].Craft is not { } q || k >= q.Count || q.Items[k].Lines is not { } lines || l >= lines.Length)
            return;
        ref readonly var cl = ref lines[l];
        for (var i = 0; i < Math.Min((int)cl.Depth, 4); i++)
            card.Add(line, "  ");
        card.Add(line, ConstructParts.Get(cl.Kind)?.Name ?? "part").Add(line, ' ').Add(line, cl.Made).Add(line, '/').Add(line, cl.Asked).Add(line, " - ");
        if (cl.Maker == ConstructLogisticsPlan.NoPart && !string.IsNullOrEmpty(cl.Reason))
        {
            var from = card.Length(line);
            card.Add(line, cl.Reason).Tint(line, from, card.Length(line), Wanting);
        }
        else if (cl.Maker != ConstructLogisticsPlan.NoPart && cl.Maker < c.PartCount)
            card.Add(line, c.DefOf(cl.Maker).Name);
        else
            card.Add(line, "from stock");
        if (cl.Stock > 0)
            card.Add(line, ", ").Add(line, cl.Stock).Add(line, " from stock");
    }

    /// <summary>" Router 2: last Gear Set -> order, spill Left, holds" (its settings shown when not the defaults).</summary>
    private static void RouterLine(Construct c, LogiMap m, int router, int no, PanelCard card, int line)
    {
        var s = m.RouterSlot(router);
        card.Add(line, " Router ").Add(line, no).Add(line, ": ");
        if (s < 0)
            return;
        if (m.ShowStuck[s])
        {
            var from = card.Length(line);
            card.Add(line, "stuck").Tint(line, from, card.Length(line), StuckRed);
        }
        else if (m.ShowWay[s] == 0)
            card.Add(line, "no piece yet");
        else
            card.Add(line, Names.Piece(m.ShowDef[s])).Add(line, m.ShowState[s] == 2 ? " -> storage" : " -> order ").Add(line, m.ShowState[s] == 2 ? "" : Names.Number(m.ShowTag[s]));
        var spill = StockTargets.Spill(c, router);
        if (spill != StockTargets.SpillAuto)
            card.Add(line, ", spill ").Add(line, RouterScreen.SpillWord(spill));
        if (StockTargets.Hold(c, router))
            card.Add(line, ", holds");
    }

    private static Vector3 LampOf(LineState s)
    {
        ProductionLines.Look(s, out var colour, out _, out _);
        return s == LineState.NoPower ? ProductionLines.NoPowerGrey : colour;
    }

    /// <summary>Names that would cost a string each fill (a piece's label, a pressed part's), made once a kind and kept.</summary>
    internal static class Names
    {
        private const int MaxKept = 512;
        private static readonly Dictionary<ushort, string> ByDef = new();
        private static readonly Dictionary<ExtrusionKey, string> ByKey = new();
        private static readonly Dictionary<string, string> ByName = new(StringComparer.Ordinal);
        private static readonly string[] Numbers = new string[1000];

        public static string Piece(ushort def)
        {
            if (ConstructParts.Get(def) is not { } pd)
                return "a piece";
            if (!pd.IsExtrusion)
                return pd.Name;
            if (!ByDef.TryGetValue(def, out var s))
            {
                if (ByDef.Count >= MaxKept)
                    ByDef.Clear();
                ByDef[def] = s = Extrusion.Label(pd.ExKey);
            }
            return s;
        }

        public static string Order(in WorkOrder o)
        {
            if (o.Name != null)
            {
                if (!ByName.TryGetValue(o.Name, out var s))
                {
                    if (ByName.Count >= MaxKept)
                        ByName.Clear();
                    ByName[o.Name] = s = ConstructCrafting.NameOf(o);
                }
                return s;
            }
            if (!o.IsPiece)
                return ConstructParts.Get(o.Part)?.Name ?? "part";
            if (!ByKey.TryGetValue(o.Key, out var p))
            {
                if (ByKey.Count >= MaxKept)
                    ByKey.Clear();
                ByKey[o.Key] = p = ConstructCrafting.NameOf(o);
            }
            return p;
        }

        public static string Number(int n) => (uint)n < (uint)Numbers.Length ? Numbers[n] ??= n.ToString() : n.ToString();
    }
}
