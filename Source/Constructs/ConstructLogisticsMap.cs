using System.Numerics;

namespace CallOfSlop;

/// <summary>What a map node is drawn as (<see cref="NetPlan"/>).</summary>
public enum PlanNode : byte { Machine, Store, Terminal, Router, Splitter, Join, Other }

/// <summary>
/// The network board's plan map (smart logistics design 9.2; <see cref="PanelDraw.Map"/>): a conveyor network seen from above, flattened
/// on the two axes it is widest on, in metres of the body frame. The static layer (conveyor lines, nodes, tags) is made again only when
/// the logistics map's links change; the dynamic layer (states, fills, piece dots, the stuck ring) at each board fill. One plan per body
/// and network, kept and reused (made the first time a big board shows that network): a fill allocates nothing. Each viewer's own.
/// </summary>
public sealed class NetPlan
{
    public const int MaxLines = 1024, MaxNodes = LogiMap.MaxNodes, MaxDots = 256, MaxPoints = 2048;

    // Static: every conveyor's polyline (flattened points), its line runs, the nodes.
    public ulong LinkHash = ulong.MaxValue;
    public int MapStamp = int.MinValue, Net = -1;
    public int Axis0, Axis1; // the body axes (0 x, 1 y, 2 z) drawn across and up
    public Vector2 Min, Max; // metres, the plan's bounds
    public readonly Vector2[] Points = new Vector2[MaxPoints];
    public int PointN;
    public readonly short[] RouteFirst = new short[Construct.MaxRoutes], RouteCount = new short[Construct.MaxRoutes]; // a conveyor's points (count 0: not drawn)
    public readonly float[] RouteLen = new float[Construct.MaxRoutes], RouteTrue = new float[Construct.MaxRoutes]; // flattened and true metres
    public readonly ushort[] LineRoute = new ushort[MaxLines]; // each drawn conveyor
    public readonly bool[] LineCut = new bool[MaxLines];
    public int LineN;
    public readonly Vector2[] NodeAt = new Vector2[MaxNodes];
    public readonly PlanNode[] NodeKind = new PlanNode[MaxNodes];
    public readonly ushort[] NodePart = new ushort[MaxNodes];
    public readonly char[] NodeTag = new char[MaxNodes * 2];
    public int NodeN;

    // Dynamic, made each fill.
    public readonly Vector3[] NodeColour = new Vector3[MaxNodes];
    public readonly float[] NodeFill = new float[MaxNodes]; // a store's fill, 0..1 (-1: none)
    public readonly Vector2[] Dots = new Vector2[MaxDots];
    public readonly byte[] DotKind = new byte[MaxDots]; // 0 storage, 1 an order's, 2 stuck
    public int DotN;
    public int Ring = -1; // the plan node a red ring blinks round (the first stuck Router), or -1
    public bool RingOn; // the blink's phase
    public int Hash; // what the dynamic layer shows, folded (the card is laid out again only when it changes)

    private static readonly Dictionary<long, NetPlan> Plans = new();

    /// <summary>The plan of body <paramref name="c"/>'s network <paramref name="net"/>, made up to date (<paramref name="world"/>: stores, states).</summary>
    public static NetPlan Of(ConstructWorld world, Construct c, LogiMap m, int net)
    {
        var key = (long)c.Id << 20 | (uint)(net & 0xFFFFF);
        if (!Plans.TryGetValue(key, out var p))
        {
            if (Plans.Count > 64)
                Plans.Clear(); // (old bodies' plans: a few at most are in view)
            Plans[key] = p = new NetPlan();
        }
        if (p.LinkHash != m.LinkHash || p.MapStamp != m.Stamp || p.Net != net)
            p.Static(c, m, net);
        p.Dynamic(world, c, m, net);
        return p;
    }

    private Vector2 Flat(Vector3 v) => new(Axis0 == 0 ? v.X : Axis0 == 1 ? v.Y : v.Z, Axis1 == 0 ? v.X : Axis1 == 1 ? v.Y : -v.Z);

    private void Static(Construct c, LogiMap m, int net)
    {
        (LinkHash, MapStamp, Net) = (m.LinkHash, m.Stamp, net);
        PointN = LineN = NodeN = 0;
        // The axes: the two the network spreads widest on (a floor plan: x and z, unless it is a tower or a wall).
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var nd = 0; nd < m.NodeN; nd++)
            if (m.NodeNet[nd] == net && m.NodePart[nd] < c.PartCount)
            {
                lo = Vector3.Min(lo, c.Parts[m.NodePart[nd]].Pos);
                hi = Vector3.Max(hi, c.Parts[m.NodePart[nd]].Pos);
            }
        var ext = hi - lo;
        (Axis0, Axis1) = ext.Y <= ext.X && ext.Y <= ext.Z ? (0, 2) : ext.X <= ext.Z ? (2, 1) : (0, 1);
        Min = new Vector2(float.MaxValue);
        Max = new Vector2(float.MinValue);
        Array.Clear(RouteCount);
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type != RouteType.Conveyor || r.From >= c.PartCount || m.NetOf(r.From) != net && m.NetOf(r.To) != net)
                continue;
            if (PointN + r.Count + 2 > MaxPoints || LineN >= MaxLines)
                break; // (a network too big to draw whole: the rest of its conveyors are left off)
            RouteFirst[k] = (short)PointN;
            var len = 0f;
            for (var i = -1; i <= r.Count; i++)
            {
                var at = Flat(ConstructRoutes.Point(c, k, i, out _));
                if (i >= 0)
                    len += Vector2.Distance(Points[PointN - 1], at);
                Points[PointN++] = at;
                Min = Vector2.Min(Min, at);
                Max = Vector2.Max(Max, at);
            }
            RouteCount[k] = (short)(r.Count + 2);
            RouteLen[k] = len;
            RouteTrue[k] = m.EdgeLen[k] > 1e-4f ? m.EdgeLen[k] : len;
            LineRoute[LineN] = (ushort)k;
            LineCut[LineN++] = r.FirstCut <= r.Count;
        }
        for (var nd = 0; nd < m.NodeN && NodeN < MaxNodes; nd++)
        {
            if (m.NodeNet[nd] != net || m.NodePart[nd] >= c.PartCount)
                continue;
            int part = m.NodePart[nd];
            var d = c.DefOf(part);
            var kind = m.RouterOfNode[nd] >= 0 ? PlanNode.Router
                : d.IsConveyorJunction ? (Outs(c, part) > 1 ? PlanNode.Splitter : PlanNode.Join)
                : d.IsCraftTerminal ? PlanNode.Terminal
                : StockTargets.IsStore(d) ? PlanNode.Store
                : d.Monitor == MonitorKind.Machine ? PlanNode.Machine : PlanNode.Other;
            var at = Flat(c.Parts[part].Pos);
            NodeAt[NodeN] = at;
            NodeKind[NodeN] = kind;
            NodePart[NodeN] = (ushort)part;
            Tag(d.Name, NodeTag.AsSpan(NodeN * 2, 2));
            Min = Vector2.Min(Min, at);
            Max = Vector2.Max(Max, at);
            NodeN++;
        }
        if (NodeN == 0 && PointN == 0)
            Min = Max = Vector2.Zero;
    }

    private static int Outs(Construct c, int part)
    {
        var n = 0;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].Parent == 255 && c.Routes[k].From == part)
                n++;
        return n;
    }

    /// <summary>A machine's two-letter tag: the first letters of its first two words (Panel Former PF), else its first two (EX, AS, PR).</summary>
    private static void Tag(string name, Span<char> tag)
    {
        var space = name.IndexOf(' ');
        tag[0] = char.ToUpperInvariant(name.Length > 0 ? name[0] : '?');
        tag[1] = char.ToUpperInvariant(space > 0 && space + 1 < name.Length ? name[space + 1] : name.Length > 1 ? name[1] : ' ');
    }

    private void Dynamic(ConstructWorld world, Construct c, LogiMap m, int net)
    {
        var h = 2166136261u;
        void Mix(uint v) => h = (h ^ v) * 16777619u;
        Ring = -1;
        for (var i = 0; i < NodeN; i++)
        {
            int part = NodePart[i];
            NodeFill[i] = -1;
            var colour = new Vector3(0.45f, 0.55f, 0.62f);
            if (part < c.PartCount)
            {
                var d = c.DefOf(part);
                switch (NodeKind[i])
                {
                    case PlanNode.Machine when world.StoreOf(c, part) is { } st:
                    {
                        var s = ProductionLines.Of(world, c, part, st, ConstructRender.Now);
                        ProductionLines.Look(s, out colour, out _, out _);
                        if (s == LineState.NoPower)
                            colour = ProductionLines.NoPowerGrey;
                        Mix((uint)s);
                        break;
                    }
                    case PlanNode.Store when world.StoreOf(c, part) is { } st:
                        ConstructRackPack.Ensure(d, st);
                        NodeFill[i] = Math.Clamp(ConstructRackPack.Fill(d, st), 0, 1);
                        Mix((uint)(NodeFill[i] * 40));
                        break;
                    case PlanNode.Router when m.RouterSlot(part) is var rs and >= 0:
                        if (m.ShowStuck[rs])
                        {
                            colour = new Vector3(1f, 0.25f, 0.2f);
                            if (Ring < 0)
                                Ring = i;
                        }
                        else
                            colour = new Vector3(0.35f, 0.72f, 1f);
                        Mix((uint)rs | (m.ShowStuck[rs] ? 64u : 0));
                        break;
                }
            }
            NodeColour[i] = colour;
        }
        RingOn = Ring >= 0 && (int)(ConstructRender.Now * 6) % 2 == 0; // (3 Hz)
        Mix((uint)(Ring + 1) | (RingOn ? 1u << 16 : 0));
        // The pieces: a dot at each rider's place along its conveyor (every n-th past the most).
        DotN = 0;
        var step = c.ConveyedCount > MaxDots ? (c.ConveyedCount + MaxDots - 1) / MaxDots : 1;
        for (var i = 0; i < c.ConveyedCount && DotN < MaxDots; i += step)
        {
            ref readonly var r = ref c.Conveyed[i];
            if (r.Route >= c.RouteCount || RouteCount[r.Route] < 2)
                continue;
            Dots[DotN] = Along(r.Route, r.At);
            var f = (LogiFlags)r.Flags;
            DotKind[DotN] = (byte)((f & LogiFlags.Stuck) != 0 ? 2 : r.Dest != 0 && (f & LogiFlags.Storage) == 0 ? 1 : 0);
            Mix((uint)(Dots[DotN].X * 20) ^ (uint)(Dots[DotN].Y * 20) << 12 ^ (uint)DotKind[DotN] << 28);
            DotN++;
        }
        Mix((uint)DotN);
        Hash = (int)h;
    }

    /// <summary>A place <paramref name="s"/> metres along a conveyor's flattened line (the line's length scaled to the true one).</summary>
    private Vector2 Along(int route, float s)
    {
        int first = RouteFirst[route], n = RouteCount[route];
        var share = Math.Clamp(s, 0, RouteTrue[route]) * RouteLen[route] / MathF.Max(1e-4f, RouteTrue[route]); // (a climb is shorter seen from above)
        for (var i = 1; i < n; i++)
        {
            var a = Points[first + i - 1];
            var b = Points[first + i];
            var len = Vector2.Distance(a, b);
            if (share <= len || i == n - 1)
                return len > 1e-4f ? Vector2.Lerp(a, b, Math.Clamp(share / len, 0, 1)) : b;
            share -= len;
        }
        return Points[first];
    }
}
