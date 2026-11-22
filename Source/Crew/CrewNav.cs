using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One body's walk graph in its rest frame (docs/sandbox/design/bot-brain.md section 5, phase 4): its solid boxes laid on a grid of
/// <see cref="CrewNav.G"/> m columns, each surface in a column (a deck, a floor, a stair's step) a node where a standing person fits (head
/// room to <see cref="World.PlayerHeight"/>, <see cref="CrewNav.Radius"/> clear round), joined to the four next columns' surfaces a step up
/// or down, and a ladder's foot to its landing. Door leaves are left out (a walker opens them). Built off the tick in slices
/// (<see cref="CrewNav.Step"/>), for the <see cref="Construct.RebuildStamp"/> it was started on, then its connected parts are labelled
/// (<see cref="Comp"/>), so a station cut off by a breach is known unreachable. Pooled: its arrays are kept for the next body.
/// </summary>
public sealed class CrewNavGraph
{
    public ushort Body;
    public int Stamp = -1, PartsSeen = -1;
    public ulong Shape; // its parts' places when built (CrewNav.ShapeOf): a rebuild that moved nothing (a breakdown, a repair) keeps the graph
    public int Build; // a number per build (station nodes are kept against it)
    public bool Ready;
    public long UsedAt; // the tick it was last asked for (the cache drops the oldest)
    internal int Phase, At; // the build's phase and its place in it
    public long BuildTicks; // ticks the last build took
    public double BuildMs, WorstSliceMs; // CPU it took, all slices; its worst slice, and that slice's phase
    public int WorstPhase;

    // The solid boxes in the rest frame.
    internal Vector3[] BMin = new Vector3[256], BMax = new Vector3[256];
    internal bool[] BWalk = new bool[256]; // (a stair, a ramp, a floor: stepped up onto from the disc's edge; anything else only a lip)
    internal int[] BPart = new int[256]; // (each box's part)
    internal int BoxCount;
    // 1 m cells over the grid: the boxes near each.
    internal int[] CellStart = [], CellItems = [];
    internal int Cx, Cz;
    // The grid.
    public float X0, Z0;
    public int Nx, Nz;
    internal int[] ColStart = [];
    /// <summary>Each node: its surface's height (rest frame), its column, its connected part.</summary>
    public float[] Top = new float[1024];
    public int[] ColOf = new int[1024];
    /// <summary>Each surface: 1 a standing body fits, 2 at a drop's edge (left out: a body's centre that slips over the edge falls), 0 no room.</summary>
    public byte[] Flag = new byte[1024];
    public bool Usable(int n) => Flag[n] == 1;
    public int[] Comp = new int[1024];
    /// <summary>Spots in each connected part (a pocket of a few between a couch's arms is no place to start a walk).</summary>
    public int[] CompSize = new int[64];
    public int NodeCount, Comps;
    // Ladders: foot node, top node, the ladder part.
    internal int[] LadA = new int[16], LadB = new int[16];
    internal short[] LadPart = new short[16];
    internal int LadCount;
    internal int[] Queue = new int[1024];
    internal int QHead, QTail;

    public Vector3 NodePos(int n) => new(X0 + (ColOf[n] / Nz + 0.5f) * CrewNav.G, Top[n], Z0 + (ColOf[n] % Nz + 0.5f) * CrewNav.G);
}

/// <summary>
/// CrewNav: the walk graphs of crewed bodies near people, their searches and the crew bodies walking them (bot-brain.md section 5). Server
/// only, a fixed cost per tick: one graph built at a time (<see cref="ColsPerTick"/> columns a tick), searches sliced (<see cref="ExpandPerTick"/>
/// nodes a tick, one at a time), at most <see cref="MaxGraphs"/> graphs kept and <see cref="MaxWalkers"/> walkers; nothing allocated once
/// its arrays have grown to the biggest body seen.
/// </summary>
public sealed class CrewNav
{
    public const float G = 0.2f, Radius = World.PlayerRadius, Reach = 1.6f;
    public const int MaxGraphs = 16, MaxWalkers = CrewWorld.MaxBodiedCap, MaxPts = 48, PartsPerTick = 40, ColsPerTick = 80, EdgesPerTick = 160, CompPerTick = 400, ExpandPerTick = 1200, MaxExpand = 60000;
    /// <summary>A walker making no headway this long (ticks) searches again; twice, it gives up.</summary>
    public const int StuckTicks = 3 * World.TickRate;

    /// <summary>A tick's building stops after this much CPU (ms), at the end of a slice (the slices above are each a few tens of microseconds).</summary>
    public const double BuildBudgetMs = 0.35, SearchBudgetMs = 0.3;

    private readonly CrewNavGraph?[] _graphs = new CrewNavGraph?[MaxGraphs];
    private readonly Stack<CrewNavGraph> _pool = new();
    private int _building = -1; // the slot being built
    private int _builds;
    public long Tick;
    public double WorstMs, CpuMs;
    public readonly List<string> Events = new(16);

    // ---- Graphs ---------------------------------------------------------------------------------------------------------------------

    /// <summary>The graph of <paramref name="c"/> if it is built for the body as it is now; else null, and one is queued (built in slices).</summary>
    public CrewNavGraph? Want(Construct c)
    {
        var free = -1;
        var oldest = -1;
        for (var k = 0; k < MaxGraphs; k++)
        {
            var g = _graphs[k];
            if (g == null)
            {
                if (free < 0)
                    free = k;
                continue;
            }
            if (g.Body == c.Id)
            {
                g.UsedAt = Tick;
                if (g.Stamp == c.RebuildStamp && g.PartsSeen == c.PartCount)
                    return g.Ready ? g : null;
                if (g.Ready && g.PartsSeen == c.PartCount && ShapeOf(c) == g.Shape)
                {
                    g.Stamp = c.RebuildStamp; // (rebuilt, but nothing moved or went: the same walk graph)
                    return g;
                }
                if (_building < 0 || _building == k)
                    Start(k, c); // (rebuilt: build it again; the old one is no use meanwhile)
                return null;
            }
            if (k != _building && (oldest < 0 || g.UsedAt < _graphs[oldest]!.UsedAt))
                oldest = k;
        }
        if (_building >= 0)
            return null; // (one build at a time: asked again next look)
        var slot = free >= 0 ? free : oldest >= 0 && _graphs[oldest]!.UsedAt < Tick - 10 * World.TickRate ? oldest : -1;
        if (slot < 0)
            return null;
        if (_graphs[slot] == null)
            _graphs[slot] = _pool.Count > 0 ? _pool.Pop() : new CrewNavGraph();
        Start(slot, c);
        return null;
    }

    /// <summary>The graph of body <paramref name="body"/> if it has one ready (any stamp: a walker on its way keeps it).</summary>
    public CrewNavGraph? Of(ushort body)
    {
        for (var k = 0; k < MaxGraphs; k++)
            if (_graphs[k] is { } g && g.Body == body && g.Ready)
                return g;
        return null;
    }

    private void Start(int slot, Construct c)
    {
        var g = _graphs[slot]!;
        g.Body = c.Id;
        g.Stamp = c.RebuildStamp;
        g.PartsSeen = c.PartCount;
        g.Shape = ShapeOf(c);
        g.Ready = false;
        g.Phase = 0;
        g.At = 0;
        g.BoxCount = 0;
        g.NodeCount = 0;
        g.LadCount = 0;
        g.Build = ++_builds;
        g.UsedAt = Tick;
        g.BuildTicks = 0;
        g.BuildMs = g.WorstSliceMs = 0;
        _building = slot;
    }

    /// <summary>A hash of where a body's parts are and which are whole (a part's health only counts as there or gone): its walk graph's key.</summary>
    public static ulong ShapeOf(Construct c)
    {
        var h = 14695981039346656037UL;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            h = (h ^ p.Tag) * 1099511628211UL;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(p.Pos.X)) * 1099511628211UL;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(p.Pos.Y)) * 1099511628211UL;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(p.Pos.Z)) * 1099511628211UL;
            h = (h ^ (uint)(p.Rot | p.Seg << 8 | (p.Health > 0 ? 1 << 16 : 0))) * 1099511628211UL;
        }
        return h;
    }

    /// <summary>Forget every graph and walker (a world's end).</summary>
    public void Clear()
    {
        for (var k = 0; k < MaxGraphs; k++)
            if (_graphs[k] is { } g)
            {
                _pool.Push(g);
                _graphs[k] = null;
            }
        _building = -1;
        _search = -1;
        for (var i = 0; i < MaxWalkers; i++)
            WMember[i] = -1;
    }

    /// <summary>A body that went: its graph goes back to the pool.</summary>
    public void Drop(ushort body)
    {
        for (var k = 0; k < MaxGraphs; k++)
            if (_graphs[k] is { } g && g.Body == body)
            {
                if (_building == k)
                    _building = -1;
                _pool.Push(g);
                _graphs[k] = null;
            }
    }

    /// <summary>A tick (server): a slice of the graph being built, then a slice of the search under way.</summary>
    public void Step(ConstructWorld w)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Tick++;
        if (_building >= 0 && _graphs[_building] is { } g)
        {
            var c = w.Get(g.Body);
            if (c == null || c.Wreck || c.PartCount != g.PartsSeen || c.RebuildStamp != g.Stamp && ShapeOf(c) != g.Shape)
            {
                if (c == null || c.Wreck)
                    Drop(g.Body);
                else
                    Start(_building, c); // (changed while building: again)
            }
            else
            {
                var b0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var phase = g.Phase;
                var sliceMs = 0.0;
                while (!g.Ready && sliceMs < BuildBudgetMs)
                {
                    BuildSlice(c, g);
                    sliceMs = (System.Diagnostics.Stopwatch.GetTimestamp() - b0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                }
                g.BuildTicks++;
                g.BuildMs += sliceMs;
                if (sliceMs > g.WorstSliceMs)
                    (g.WorstSliceMs, g.WorstPhase) = (sliceMs, phase);
                if (g.Ready)
                {
                    _building = -1;
                    Events.Add($"construct {c.Id}: its walk graph is built ({g.NodeCount} spots, {g.Comps} parts, {g.LadCount} ladders, {g.BoxCount} boxes; {g.BuildTicks} ticks, {g.BuildMs:0.0} ms in all, worst slice {g.WorstSliceMs:0.00} ms in phase {g.WorstPhase})");
                }
            }
        }
        SearchSlice();
        var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        CpuMs += ms;
        if (Tick > 10)
            WorstMs = Math.Max(WorstMs, ms);
    }

    private static void Grow<T>(ref T[] a, int need)
    {
        if (a.Length < need)
            Array.Resize(ref a, Math.Max(need, a.Length * 2));
    }

    private void BuildSlice(Construct c, CrewNavGraph g)
    {
        switch (g.Phase)
        {
            case 0: // the solid boxes, a slice of parts at a time
            {
                var end = Math.Min(c.PartCount, g.At + PartsPerTick);
                for (var i = g.At; i < end; i++)
                {
                    if (c.Parts[i].Health <= 0 || c.DefOf(i).Role == PartRole.Door && Upright(c, i))
                        continue; // (an upright door's leaf: a walker opens it; a hatch in a deck is walked on)
                    var n = c.SolidCount(i);
                    var walk = c.DefOf(i).Walk != WalkKind.None;
                    if (n == 0)
                        continue;
                    c.SegPose(c.Parts[i].Seg, out var sq, out var st);
                    var m = Matrix4x4.CreateFromQuaternion(sq);
                    for (var k = 0; k < n; k++)
                    {
                        c.SolidBox(i, k, out var centre, out var half);
                        var rc = st + Vector3.Transform(centre, sq);
                        var rh = new Vector3(
                            MathF.Abs(m.M11) * half.X + MathF.Abs(m.M21) * half.Y + MathF.Abs(m.M31) * half.Z,
                            MathF.Abs(m.M12) * half.X + MathF.Abs(m.M22) * half.Y + MathF.Abs(m.M32) * half.Z,
                            MathF.Abs(m.M13) * half.X + MathF.Abs(m.M23) * half.Y + MathF.Abs(m.M33) * half.Z);
                        Grow(ref g.BMin, g.BoxCount + 1);
                        Grow(ref g.BMax, g.BoxCount + 1);
                        Grow(ref g.BWalk, g.BoxCount + 1);
                        g.BWalk[g.BoxCount] = walk;
                        Grow(ref g.BPart, g.BoxCount + 1);
                        g.BPart[g.BoxCount] = i;
                        g.BMin[g.BoxCount] = rc - rh;
                        g.BMax[g.BoxCount++] = rc + rh;
                    }
                }
                g.At = end;
                if (end >= c.PartCount)
                {
                    g.Phase = 1;
                    g.At = 0;
                }
                return;
            }
            case 1: // the cells, and the grid's size
            {
                var lo = new Vector3(float.MaxValue);
                var hi = new Vector3(float.MinValue);
                for (var b = 0; b < g.BoxCount; b++)
                {
                    lo = Vector3.Min(lo, g.BMin[b]);
                    hi = Vector3.Max(hi, g.BMax[b]);
                }
                if (g.BoxCount == 0)
                {
                    g.Nx = g.Nz = 0;
                    g.Ready = true;
                    return;
                }
                g.X0 = lo.X - G;
                g.Z0 = lo.Z - G;
                g.Nx = (int)MathF.Ceiling((hi.X - lo.X) / G) + 2;
                g.Nz = (int)MathF.Ceiling((hi.Z - lo.Z) / G) + 2;
                g.Cx = (int)MathF.Ceiling(g.Nx * G) + 1;
                g.Cz = (int)MathF.Ceiling(g.Nz * G) + 1;
                var cells = g.Cx * g.Cz;
                Grow(ref g.CellStart, cells + 1);
                Array.Clear(g.CellStart, 0, cells + 1);
                var total = 0;
                for (var pass = 0; pass < 2; pass++)
                {
                    for (var b = 0; b < g.BoxCount; b++)
                    {
                        var x0 = Math.Max(0, (int)(g.BMin[b].X - g.X0 - Radius));
                        var x1 = Math.Min(g.Cx - 1, (int)(g.BMax[b].X - g.X0 + Radius));
                        var z0 = Math.Max(0, (int)(g.BMin[b].Z - g.Z0 - Radius));
                        var z1 = Math.Min(g.Cz - 1, (int)(g.BMax[b].Z - g.Z0 + Radius));
                        for (var ix = x0; ix <= x1; ix++)
                            for (var iz = z0; iz <= z1; iz++)
                            {
                                if (pass == 0)
                                {
                                    g.CellStart[ix * g.Cz + iz + 1]++;
                                    total++;
                                }
                                else
                                    g.CellItems[g.CellStart[ix * g.Cz + iz]++] = b;
                            }
                    }
                    if (pass == 0)
                    {
                        for (var k = 0; k < cells; k++)
                            g.CellStart[k + 1] += g.CellStart[k];
                        Grow(ref g.CellItems, total);
                    }
                    else
                    {
                        for (var k = cells; k > 0; k--)
                            g.CellStart[k] = g.CellStart[k - 1]; // (filling moved each start to the next's; back one)
                        g.CellStart[0] = 0;
                    }
                }
                Grow(ref g.ColStart, g.Nx * g.Nz + 1);
                g.ColStart[0] = 0;
                g.Phase = 2;
                g.At = 0;
                return;
            }
            case 2: // the columns' surfaces
            {
                var cols = g.Nx * g.Nz;
                var end = Math.Min(cols, g.At + ColsPerTick);
                Span<float> tops = stackalloc float[32];
                for (var col = g.At; col < end; col++)
                {
                    var x = g.X0 + (col / g.Nz + 0.5f) * G;
                    var z = g.Z0 + (col % g.Nz + 0.5f) * G;
                    var cell = CellOf(g, x, z);
                    var n = 0;
                    for (var k = g.CellStart[cell]; k < g.CellStart[cell + 1] && n < tops.Length; k++)
                    {
                        var b = g.CellItems[k];
                        if (x < g.BMin[b].X || x > g.BMax[b].X || z < g.BMin[b].Z || z > g.BMax[b].Z)
                            continue;
                        // Insertion by height.
                        var t = g.BMax[b].Y;
                        var j = n++;
                        while (j > 0 && tops[j - 1] > t)
                        {
                            tops[j] = tops[j - 1];
                            j--;
                        }
                        tops[j] = t;
                    }
                    var last = float.MinValue;
                    for (var k = 0; k < n; k++)
                    {
                        if (tops[k] - last < 0.02f)
                            continue;
                        last = tops[k];
                        var node = g.NodeCount++;
                        Grow(ref g.Top, node + 1);
                        Grow(ref g.ColOf, node + 1);
                        Grow(ref g.Flag, node + 1);
                        g.Top[node] = tops[k];
                        g.ColOf[node] = col;
                        g.Flag[node] = Blocked(g, x, z, tops[k]) ? (byte)0 : (byte)1;
                    }
                    g.ColStart[col + 1] = g.NodeCount;
                }
                g.At = end;
                if (end >= cols)
                {
                    g.Phase = 5;
                    g.At = 0;
                }
                return;
            }
            case 5: // drops' edges: a surface with a column round it that has nothing a step up or down (fit or not: a wall isn't a drop)
            {
                var end = Math.Min(g.NodeCount, g.At + EdgesPerTick);
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var most = (long)(BuildBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);
                for (var n = g.At; n < end; n++)
                {
                    if ((n & 7) == 7 && System.Diagnostics.Stopwatch.GetTimestamp() - t0 > most)
                    {
                        end = n; // (a slice of a big body's many boxes runs long: the rest next tick, Hexkey Orbital phase 6)
                        break;
                    }
                    if (g.Flag[n] != 1)
                        continue;
                    var col = g.ColOf[n];
                    int ix = col / g.Nz, iz = col % g.Nz;
                    for (var dx = -Edge; dx <= Edge && g.Flag[n] == 1; dx++)
                        for (var dz = -Edge; dz <= Edge; dz++)
                        {
                            if (dx == 0 && dz == 0 || dx * dx + dz * dz > Edge * Edge)
                                continue;
                            int jx = ix + dx, jz = iz + dz;
                            if (jx < 0 || jz < 0 || jx >= g.Nx || jz >= g.Nz)
                            {
                                g.Flag[n] = 2;
                                break;
                            }
                            if (!Solid(g, g.X0 + (jx + 0.5f) * G, g.Z0 + (jz + 0.5f) * G, g.Top[n] - World.StepHeight, g.Top[n] + World.PlayerHeight))
                            {
                                g.Flag[n] = 2;
                                break;
                            }
                        }
                }
                g.At = end;
                if (end >= g.NodeCount)
                {
                    g.Phase = 3;
                    g.At = 0;
                }
                return;
            }
            case 3: // ladders: the foot joined to the landing behind the top, as ConstructRide.ClimbLadder climbs
            {
                for (var i = 0; i < c.PartCount; i++)
                {
                    ref var lp = ref c.Parts[i];
                    var d = lp.D;
                    if (d.LadderTop <= 0 || lp.Broken)
                        continue;
                    c.SegPose(lp.Seg, out var sq, out var st);
                    var cx = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f;
                    var stand = World.PlayerRadius + 0.12f;
                    var foot = st + Vector3.Transform(lp.Pos + Rot24.Apply(lp.Rot, new Vector3(cx, d.LadderFoot, d.BoundsMin.Z - stand)), sq);
                    var top = st + Vector3.Transform(lp.Pos + Rot24.Apply(lp.Rot, new Vector3(cx, d.LadderTop, d.BoundsMax.Z + stand)), sq);
                    var a = NearestNode(g, foot, 0.6f, 0.8f);
                    var b = NearestNode(g, top, 0.6f, 0.8f);
                    if (a < 0 || b < 0 || g.Top[b] - g.Top[a] < World.StepHeight)
                        continue; // (no landing found above its foot: not a way up)
                    Grow(ref g.LadA, g.LadCount + 1);
                    Grow(ref g.LadB, g.LadCount + 1);
                    Grow(ref g.LadPart, g.LadCount + 1);
                    g.LadA[g.LadCount] = a;
                    g.LadB[g.LadCount] = b;
                    g.LadPart[g.LadCount++] = (short)i;
                }
                Grow(ref g.Comp, g.NodeCount);
                Grow(ref g.Queue, g.NodeCount);
                Array.Fill(g.Comp, -1, 0, g.NodeCount);
                g.Comps = 0;
                g.QHead = g.QTail = 0;
                g.Phase = 4;
                g.At = 0;
                return;
            }
            case 4: // connected parts, a slice of nodes at a time
            {
                Span<int> nb = stackalloc int[16];
                var budget = CompPerTick;
                while (budget > 0)
                {
                    if (g.QHead == g.QTail)
                    {
                        while (g.At < g.NodeCount && (g.Comp[g.At] >= 0 || !g.Usable(g.At)))
                            g.At++;
                        if (g.At >= g.NodeCount)
                        {
                            g.Ready = true;
                            return;
                        }
                        Grow(ref g.CompSize, g.Comps + 1);
                        g.CompSize[g.Comps] = 1;
                        g.Comp[g.At] = g.Comps++;
                        g.QHead = g.QTail = 0;
                        g.Queue[g.QTail++] = g.At;
                    }
                    var n = g.Queue[g.QHead++];
                    budget--;
                    var k = Neighbours(g, n, nb, out _);
                    for (var j = 0; j < k; j++)
                        if (g.Comp[nb[j]] < 0)
                        {
                            g.Comp[nb[j]] = g.Comp[n];
                            g.CompSize[g.Comp[n]]++;
                            g.Queue[g.QTail++] = nb[j];
                        }
                }
                return;
            }
        }
    }

    /// <summary>A part that isn't a stair, a ramp or a floor is stepped onto only this far above the feet (BlockPlayer steps onto it only with the feet's centre over it).</summary>
    public const float Lip = 0.06f;

    /// <summary>A connected part of fewer spots than this (about half a square metre) is a pocket, not a place to stand.</summary>
    public const int MinPart = 16;

    /// <summary>Columns kept clear of a drop's edge (0.4 m: a body's centre that drifts over an edge it isn't stepping onto falls).</summary>
    public const int Edge = 2;

    /// <summary>A door whose leaf stands up (a walker opens it); a flat one is a hatch in a deck.</summary>
    public static bool Upright(Construct c, int part)
    {
        c.PartBounds(part, out var mn, out var mx);
        c.SegPose(c.Parts[part].Seg, out var sq, out _);
        var size = Vector3.Abs(Vector3.Transform(mx - mn, sq));
        return size.Y > 0.35f;
    }

    private static int CellOf(CrewNavGraph g, float x, float z) =>
        Math.Clamp((int)(x - g.X0), 0, g.Cx - 1) * g.Cz + Math.Clamp((int)(z - g.Z0), 0, g.Cz - 1);

    /// <summary>Something solid at (x, z) between heights <paramref name="lo"/> and <paramref name="hi"/>: a floor or a wall (no drop there).</summary>
    private static bool Solid(CrewNavGraph g, float x, float z, float lo, float hi)
    {
        var cell = CellOf(g, x, z);
        for (var k = g.CellStart[cell]; k < g.CellStart[cell + 1]; k++)
        {
            var b = g.CellItems[k];
            if (x >= g.BMin[b].X && x <= g.BMax[b].X && z >= g.BMin[b].Z && z <= g.BMax[b].Z && g.BMax[b].Y >= lo && g.BMin[b].Y <= hi)
                return true;
        }
        return false;
    }

    /// <summary>A standing body at (x, z) on <paramref name="top"/> hits a box above a step or under its head.</summary>
    private static bool Blocked(CrewNavGraph g, float x, float z, float top)
    {
        var cell = CellOf(g, x, z);
        for (var k = g.CellStart[cell]; k < g.CellStart[cell + 1]; k++)
        {
            var b = g.CellItems[k];
            if (g.BMax[b].Y <= top + (g.BWalk[b] ? World.StepHeight : Lip) + 1e-3f || g.BMin[b].Y >= top + World.PlayerHeight)
                continue;
            var dx = MathF.Max(0, MathF.Max(g.BMin[b].X - x, x - g.BMax[b].X));
            var dz = MathF.Max(0, MathF.Max(g.BMin[b].Z - z, z - g.BMax[b].Z));
            if (dx * dx + dz * dz < Radius * Radius)
                return true;
        }
        return false;
    }

    /// <summary>The node in column <paramref name="col"/> a body at <paramref name="top"/> walks on (the nearest in height, a step up or down at most), -1: none.</summary>
    private static int StepTo(CrewNavGraph g, int col, float top)
    {
        var best = -1;
        var bestDy = World.StepHeight + 1e-3f;
        for (var k = g.ColStart[col]; k < g.ColStart[col + 1]; k++)
            if (g.Flag[k] == 1 && MathF.Abs(g.Top[k] - top) is var dy && dy <= bestDy)
                (best, bestDy) = (k, dy);
        return best;
    }

    /// <summary>
    /// Where a body goes from node <paramref name="n"/>: every surface a step up or down in the four next columns (at most three a column: the
    /// edges are the same both ways), and a ladder's other end (<paramref name="ladder"/>: their bits, by index).
    /// </summary>
    public static int Neighbours(CrewNavGraph g, int n, Span<int> nb, out int ladder)
    {
        var col = g.ColOf[n];
        int ix = col / g.Nz, iz = col % g.Nz;
        var k = 0;
        ladder = 0;
        var top = g.Top[n];
        for (var dir = 0; dir < 4; dir++)
        {
            int o;
            if (dir == 0)
                o = ix > 0 ? col - g.Nz : -1;
            else if (dir == 1)
                o = ix < g.Nx - 1 ? col + g.Nz : -1;
            else if (dir == 2)
                o = iz > 0 ? col - 1 : -1;
            else
                o = iz < g.Nz - 1 ? col + 1 : -1;
            if (o < 0)
                continue;
            var per = 0;
            for (var j = g.ColStart[o]; j < g.ColStart[o + 1] && per < 3 && k < nb.Length; j++)
                if (g.Flag[j] == 1 && MathF.Abs(g.Top[j] - top) <= World.StepHeight + 1e-3f)
                {
                    nb[k++] = j;
                    per++;
                }
        }
        for (var l = 0; l < g.LadCount && k < nb.Length; l++)
        {
            var o = g.LadA[l] == n ? g.LadB[l] : g.LadB[l] == n ? g.LadA[l] : -1;
            if (o < 0)
                continue;
            ladder |= 1 << k;
            nb[k++] = o;
        }
        return k;
    }

    /// <summary>How far over a spot's head (m) a ceiling or a roof of the body makes the spot indoors.</summary>
    public const float CoverReach = 6f;

    /// <summary>
    /// Node <paramref name="n"/> is indoors: something of the body over its head within <see cref="CoverReach"/> (a deck's ceiling, a cab's roof).
    /// A spot on top of the hull has nothing over it (Karl, 2026-11-22: crew bodied on a freighter's spine and its cab roof in orbit).
    /// </summary>
    public static bool Covered(CrewNavGraph g, int n)
    {
        var p = g.NodePos(n);
        return Solid(g, p.X, p.Z, g.Top[n] + World.PlayerHeight, g.Top[n] + CoverReach);
    }

    /// <summary>
    /// The node nearest <paramref name="rest"/> (rest frame) within <paramref name="across"/> m across and <paramref name="below"/> m below or 0.6 m above; -1: none.
    /// <paramref name="inComp"/> (0 or more): only a node in that connected part; <paramref name="indoors"/>: only a <see cref="Covered"/> one.
    /// </summary>
    public static int NearestNode(CrewNavGraph g, Vector3 rest, float across = Reach, float below = 1.5f, int inComp = -1, bool indoors = false)
    {
        if (g.Nx == 0)
            return -1;
        var r = (int)MathF.Ceiling(across / G);
        var cx = (int)((rest.X - g.X0) / G);
        var cz = (int)((rest.Z - g.Z0) / G);
        var best = -1;
        var bestD = float.MaxValue;
        for (var ix = Math.Max(0, cx - r); ix <= Math.Min(g.Nx - 1, cx + r); ix++)
            for (var iz = Math.Max(0, cz - r); iz <= Math.Min(g.Nz - 1, cz + r); iz++)
            {
                var col = ix * g.Nz + iz;
                for (var k = g.ColStart[col]; k < g.ColStart[col + 1]; k++)
                {
                    if (g.Flag[k] != 1)
                        continue;
                    var dy = g.Top[k] - rest.Y;
                    if (dy < -below || dy > 0.6f)
                        continue;
                    if (inComp >= 0 && g.Comp[k] != inComp || indoors && !Covered(g, k))
                        continue; // (outside: on the hull, or cut off from the helm's decks)
                    var p = g.NodePos(k);
                    var d = (p.X - rest.X) * (p.X - rest.X) + (p.Z - rest.Z) * (p.Z - rest.Z) + dy * dy * 0.5f;
                    if (d > across * across)
                        continue;
                    if (g.Ready && g.CompSize[g.Comp[k]] < MinPart)
                        d += 100; // (a pocket: only if there is nothing else)
                    if (d < bestD)
                        (best, bestD) = (k, d);
                }
            }
        return best;
    }

    // ---- Searches (one at a time, sliced) --------------------------------------------------------------------------------------------

    private float[] _gCost = new float[1024];
    private int[] _came = new int[1024], _seen = new int[1024], _heap = new int[1024];
    private float[] _heapKey = new float[1024];
    private int _heapCount, _seenId, _search = -1, _expanded;
    private CrewNavGraph? _sg;
    private int _sGoal;
    private int _queueHead, _queueCount;
    private readonly int[] _queue = new int[MaxWalkers];

    private void Push(int n, float key)
    {
        var i = _heapCount++;
        while (i > 0)
        {
            var parent = (i - 1) >> 1;
            if (_heapKey[parent] <= key)
                break;
            _heap[i] = _heap[parent];
            _heapKey[i] = _heapKey[parent];
            i = parent;
        }
        _heap[i] = n;
        _heapKey[i] = key;
    }

    private int Pop()
    {
        var top = _heap[0];
        var n = _heap[--_heapCount];
        var key = _heapKey[_heapCount];
        var i = 0;
        while (true)
        {
            var l = 2 * i + 1;
            if (l >= _heapCount)
                break;
            var r = l + 1;
            var m = r < _heapCount && _heapKey[r] < _heapKey[l] ? r : l;
            if (_heapKey[m] >= key)
                break;
            _heap[i] = _heap[m];
            _heapKey[i] = _heapKey[m];
            i = m;
        }
        if (_heapCount > 0)
        {
            _heap[i] = n;
            _heapKey[i] = key;
        }
        return top;
    }

    private void SearchSlice()
    {
        var budget = ExpandPerTick;
        Span<int> nb = stackalloc int[16];
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var limit = (long)(SearchBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000);
        while (budget > 0)
        {
            if ((budget & 63) == 0 && System.Diagnostics.Stopwatch.GetTimestamp() - t0 > limit)
                return; // (the rest next tick)
            if (_search < 0)
            {
                if (_queueCount == 0)
                    return;
                var w = _queue[_queueHead];
                _queueHead = (_queueHead + 1) % MaxWalkers;
                _queueCount--;
                if (WMember[w] < 0 || WPhase[w] != 0)
                    continue;
                BeginSearch(w);
                continue;
            }
            var walker = _search;
            var g = _sg!;
            if (_heapCount == 0 || _expanded > MaxExpand)
            {
                Fail(walker, _heapCount == 0 ? "no way there" : "too far to search");
                _search = -1;
                continue;
            }
            var n = Pop();
            if (n == _sGoal)
            {
                Found(walker, g);
                _search = -1;
                continue;
            }
            _expanded++;
            budget--;
            var k = Neighbours(g, n, nb, out var lad);
            var gp = g.NodePos(_sGoal);
            for (var j = 0; j < k; j++)
            {
                var o = nb[j];
                var step = (lad & (1 << j)) != 0 ? 1 + MathF.Abs(g.Top[o] - g.Top[n]) : G + MathF.Abs(g.Top[o] - g.Top[n]) * 0.5f;
                var cost = _gCost[n] + step;
                if (_seen[o] == _seenId && _gCost[o] <= cost)
                    continue;
                _seen[o] = _seenId;
                _gCost[o] = cost;
                _came[o] = n;
                var op = g.NodePos(o);
                Push(o, cost + MathF.Abs(op.X - gp.X) + MathF.Abs(op.Z - gp.Z) + MathF.Abs(op.Y - gp.Y));
            }
        }
    }

    private void BeginSearch(int w)
    {
        var g = Of(WBody[w]);
        if (g == null || WFrom[w] < 0 || WGoal[w] < 0 || WFrom[w] >= g.NodeCount || WGoal[w] >= g.NodeCount || g.Build != WBuild[w])
        {
            Fail(w, "its walk graph changed");
            return;
        }
        if (g.Comp[WFrom[w]] != g.Comp[WGoal[w]])
        {
            Fail(w, "no way there");
            return;
        }
        var need = g.NodeCount;
        if (_gCost.Length < need)
        {
            Array.Resize(ref _gCost, need);
            Array.Resize(ref _came, need);
            Array.Resize(ref _seen, need);
        }
        if (_heap.Length < need * 4)
        {
            Array.Resize(ref _heap, need * 4);
            Array.Resize(ref _heapKey, need * 4);
        }
        _seenId++;
        _sg = g;
        _search = w;
        _sGoal = WGoal[w];
        _expanded = 0;
        _heapCount = 0;
        _seen[WFrom[w]] = _seenId;
        _gCost[WFrom[w]] = 0;
        _came[WFrom[w]] = -1;
        Push(WFrom[w], 0);
    }

    private void Fail(int w, string why)
    {
        WPhase[w] = 3;
        Events.Add($"construct {WBody[w]}: crew member {WMember[w]} finds no path ({why})");
    }

    /// <summary>The way found: its turning points, pulled straight where a body can walk straight (never past a ladder's end).</summary>
    private void Found(int w, CrewNavGraph g)
    {
        // The nodes from the goal back, into the scratch heap (reversed in place).
        var n = 0;
        for (var at = _sGoal; at >= 0 && n < _heap.Length; at = _came[at])
            _heap[n++] = at;
        Array.Reverse(_heap, 0, n);
        var path = _heap.AsSpan(0, n);
        var count = 0;
        var baseIx = w * MaxPts;
        var anchor = 0;
        WLength[w] = 0;
        while (anchor < n - 1 && count < MaxPts)
        {
            var next = anchor + 1;
            var ladder = IsLadder(g, path[anchor], path[next], out var ladPart);
            if (!ladder)
                for (var j = Math.Min(n - 1, anchor + 40); j > anchor + 1; j--)
                {
                    if (Crosses(g, path, anchor, j) || !Straight(g, path[anchor], path[j]))
                        continue;
                    next = j;
                    break;
                }
            WPts[baseIx + count] = g.NodePos(path[next]);
            WLadder[baseIx + count] = ladder ? ladPart : (short)-1;
            WLength[w] += Vector3.Distance(g.NodePos(path[anchor]), g.NodePos(path[next]));
            count++;
            anchor = next;
        }
        WCount[w] = count;
        WAt[w] = 0;
        WPhase[w] = 1;
        WStuck[w] = 0;
        WBest[w] = float.MaxValue;
    }

    /// <summary>A ladder between path nodes <paramref name="j"/> to <paramref name="k"/>: the pull stops at it.</summary>
    private static bool Crosses(CrewNavGraph g, Span<int> path, int j, int k)
    {
        for (var i = j; i < k; i++)
            if (IsLadder(g, path[i], path[i + 1], out _))
                return true;
        return false;
    }

    private static bool IsLadder(CrewNavGraph g, int a, int b, out short part)
    {
        for (var l = 0; l < g.LadCount; l++)
            if (g.LadA[l] == a && g.LadB[l] == b || g.LadA[l] == b && g.LadB[l] == a)
            {
                part = g.LadPart[l];
                return true;
            }
        part = -1;
        return false;
    }

    /// <summary>A body walks straight from node <paramref name="a"/> to <paramref name="b"/>: every column on the line has a surface a step from the last.</summary>
    private static bool Straight(CrewNavGraph g, int a, int b)
    {
        var pa = g.NodePos(a);
        var pb = g.NodePos(b);
        var len = new Vector2(pb.X - pa.X, pb.Z - pa.Z).Length();
        var steps = (int)MathF.Ceiling(len / (G * 0.5f));
        var top = pa.Y;
        for (var s = 1; s <= steps; s++)
        {
            var f = s / (float)steps;
            var ix = (int)((pa.X + (pb.X - pa.X) * f - g.X0) / G);
            var iz = (int)((pa.Z + (pb.Z - pa.Z) * f - g.Z0) / G);
            if (ix < 0 || iz < 0 || ix >= g.Nx || iz >= g.Nz)
                return false;
            var o = StepTo(g, ix * g.Nz + iz, top);
            if (o < 0)
                return false;
            top = g.Top[o];
        }
        return MathF.Abs(top - pb.Y) < 0.05f;
    }

    // ---- Walkers (crew bodies on their way: bot-brain.md section 5) ------------------------------------------------------------------

    /// <summary>Per walker: its member (-1: free), body, the graph build its nodes are of, from and to nodes, phase (0 searching, 1 walking, 2 there, 3 failed).</summary>
    public readonly int[] WMember = new int[MaxWalkers], WFrom = new int[MaxWalkers], WGoal = new int[MaxWalkers], WBuild = new int[MaxWalkers], WCount = new int[MaxWalkers], WAt = new int[MaxWalkers], WStuck = new int[MaxWalkers], WTries = new int[MaxWalkers];
    public readonly ushort[] WBody = new ushort[MaxWalkers];
    public readonly byte[] WPhase = new byte[MaxWalkers];
    public readonly float[] WLength = new float[MaxWalkers], WBest = new float[MaxWalkers];
    public readonly Vector3[] WPts = new Vector3[MaxWalkers * MaxPts];
    public readonly short[] WLadder = new short[MaxWalkers * MaxPts];
    public readonly short[] WDoor = new short[MaxWalkers]; // (the door it opened and hasn't passed 2.5 m yet: shut behind it; -1: none)
    public readonly long[] WDoorAt = new long[MaxWalkers], WStart = new long[MaxWalkers];

    public CrewNav()
    {
        Array.Fill(WMember, -1);
    }

    /// <summary>Walkers on their way now.</summary>
    public int Walking
    {
        get
        {
            var n = 0;
            for (var i = 0; i < MaxWalkers; i++)
                n += WMember[i] >= 0 ? 1 : 0;
            return n;
        }
    }

    /// <summary>The walker of member <paramref name="m"/> (-1: none).</summary>
    public int WalkerOf(int m)
    {
        if (m < 0)
            return -1;
        for (var i = 0; i < MaxWalkers; i++)
            if (WMember[i] == m)
                return i;
        return -1;
    }

    /// <summary>Member <paramref name="m"/> walks <paramref name="g"/> from node <paramref name="from"/> to <paramref name="to"/> (queued for a search). -1: no walker free.</summary>
    public int Walk(CrewNavGraph g, int m, int from, int to)
    {
        var w = WalkerOf(m);
        if (w < 0)
            for (var i = 0; i < MaxWalkers; i++)
                if (WMember[i] < 0)
                {
                    w = i;
                    break;
                }
        if (w < 0 || _queueCount >= MaxWalkers)
            return -1;
        if (_search == w)
            _search = -1;
        WMember[w] = m;
        WBody[w] = g.Body;
        WBuild[w] = g.Build;
        WFrom[w] = from;
        WGoal[w] = to;
        WPhase[w] = 0;
        WCount[w] = 0;
        WAt[w] = 0;
        WStuck[w] = 0;
        WDoorAt[w] = 0;
        WDoor[w] = -1;
        WStart[w] = Tick;
        _queue[(_queueHead + _queueCount) % MaxWalkers] = w;
        _queueCount++;
        return w;
    }

    /// <summary>The walker goes (there, given up, or its member gone).</summary>
    public void End(int w)
    {
        if (w < 0)
            return;
        if (_search == w)
            _search = -1;
        WMember[w] = -1;
        WTries[w] = 0;
    }

    /// <summary>
    /// The keys a walking body presses this tick to follow its path (the arena bots' follower, cut down: head for the next turning point in
    /// the body's rest frame turned to the world each tick; a ladder's point climbs it; a closed door on the way is opened). Returns its
    /// phase: 1 walking, 2 there, 3 given up (stuck twice, or a locked door in the way).
    /// </summary>
    public byte Steer(int w, Construct c, ConstructWorld world, ref MoveState s, out InputCmd cmd, bool leaveDoors = false)
    {
        cmd = new InputCmd { SpeedPct = 100, RideBody = s.RideBody };
        if (WPhase[w] != 1)
            return WPhase[w];
        if (WAt[w] >= WCount[w])
            return There(w);
        var feet = c.ToLocal(s.Position);
        var at = WPts[w * MaxPts + WAt[w]];
        var flat = new Vector2(at.X - feet.X, at.Z - feet.Z);
        var d = flat.Length();
        var last = WAt[w] == WCount[w] - 1;
        if (d < (last ? 0.35f : 0.5f) && MathF.Abs(at.Y - feet.Y) < 1.2f)
        {
            WAt[w]++;
            WBest[w] = float.MaxValue;
            WStuck[w] = 0;
            if (WAt[w] >= WCount[w])
                return There(w);
            at = WPts[w * MaxPts + WAt[w]];
            flat = new Vector2(at.X - feet.X, at.Z - feet.Z);
            d = flat.Length();
        }
        // A ladder: climb it from its end (the game's own climb).
        if (WLadder[w * MaxPts + WAt[w]] is var lad and >= 0 && s.RideClimb == 0 && d < 1.6f)
        {
            ConstructRide.OpenWay(world, c, lad); // (a ladder in a shaft: the crew opens its hatches and lowers its sections, then climbs once they rest open)
            if (ConstructRide.ClimbLadder(ref s, c, lad, World.HeightOf(Stance.Stand), frame: true) == null)
            {
                WAt[w]++;
                return 1;
            }
        }
        if (s.RideClimb > 0)
            return 1;
        // Headway: a better distance to the point than seen, or the stuck clock runs.
        if (d < WBest[w] - 0.05f)
        {
            WBest[w] = d;
            WStuck[w] = 0;
        }
        else if (++WStuck[w] > StuckTicks)
        {
            Events.Add($"construct {WBody[w]}: a crew body stuck {Vector3.Distance(feet, at):0.0} m short of its turn {WAt[w] + 1} of {WCount[w]} gave up the walk");
            return WPhase[w] = 3;
        }
        // Doors, twice a second: one it opened and has left 2.5 m behind is shut again (unless told to leave them: a boarding, a fight); a
        // closed one on the leg it walks now (within 0.9 m of the line, in front of it, within 1.8 m) is opened as F opens it.
        if (Tick >= WDoorAt[w] && c.DoorCount > 0)
        {
            WDoorAt[w] = Tick + World.TickRate / 2;
            if (WDoor[w] >= 0 && WDoor[w] < c.PartCount && c.DoorOf(WDoor[w]) is var dk and >= 0)
            {
                var oc = c.ToLocal(ConstructRender.PartCentre(c, WDoor[w]));
                if (Vector2.Distance(new Vector2(oc.X, oc.Z), new Vector2(feet.X, feet.Z)) > 2.5f)
                {
                    if (!leaveDoors && ConstructDoors.TargetOpen(c, WDoor[w]) && !ConstructDoors.Locked(c, WDoor[w]))
                        ConstructDoors.Command(world, c, dk, false);
                    WDoor[w] = -1;
                }
            }
            var leg = new Vector2(at.X - feet.X, at.Z - feet.Z);
            var legLen = MathF.Max(leg.Length(), 1e-3f);
            var along = leg / legLen;
            for (var k = 0; k < c.DoorCount; k++)
            {
                int part = c.Doors[k].Part;
                if (part >= c.PartCount || ConstructDoors.TargetOpen(c, part) || !Upright(c, part))
                    continue; // (open already, or a hatch underfoot: left shut)
                var dc = c.ToLocal(ConstructRender.PartCentre(c, part));
                if (dc.Y < feet.Y + 0.2f || dc.Y > feet.Y + 2.2f)
                    continue;
                var rel = new Vector2(dc.X - feet.X, dc.Z - feet.Z);
                var ahead = Vector2.Dot(rel, along);
                var off = MathF.Abs(rel.X * along.Y - rel.Y * along.X);
                if (ahead < -0.3f || ahead > MathF.Min(1.8f, legLen + 0.6f) || off > 0.9f)
                    continue; // (not on its way)
                if (ConstructDoors.Locked(c, part))
                {
                    if (WStuck[w] > World.TickRate)
                        return WPhase[w] = 3; // (locked: no way through)
                    continue;
                }
                ConstructDoors.Use(world, c, part);
                WDoor[w] = (short)part;
            }
        }
        var localYaw = MathF.Atan2(flat.X, -flat.Y);
        var dir = Vector3.Transform(new Vector3(flat.X, 0, flat.Y), c.Rot);
        cmd.Yaw = MathF.Atan2(dir.X, -dir.Z);
        cmd.RideYaw = localYaw;
        cmd.Buttons = Buttons.Forward;
        cmd.SpeedPct = 75; // (a walk, not a run: crew inside a ship)
        if (d < 1.2f)
            cmd.SpeedPct = 50; // (round a corner, or onto its spot, without running past it)
        return 1;
    }

    private byte There(int w)
    {
        if (WCount[w] > 0)
            Events.Add($"construct {WBody[w]}: a crew body walked its way ({WLength[w]:0.0} m, {WCount[w]} legs) in {(Tick - WStart[w]) / (float)World.TickRate:0.0} s");
        return WPhase[w] = 2;
    }

    /// <summary>A tool's line: graphs kept, walkers, cost.</summary>
    public string Describe()
    {
        var n = 0;
        var nodes = 0;
        for (var k = 0; k < MaxGraphs; k++)
            if (_graphs[k] is { Ready: true } g)
            {
                n++;
                nodes += g.NodeCount;
            }
        return $"walk graphs {n} ({nodes} spots), {Walking} walking, worst tick {WorstMs:0.00} ms";
    }
}
