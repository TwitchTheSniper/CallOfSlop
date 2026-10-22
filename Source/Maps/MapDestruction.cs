using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>One destructible structure: a building (its shell, chunked) or a <see cref="Map.DestructibleBox"/>.</summary>
public sealed class DestructibleStructure
{
    public int Index;

    /// <summary>The <see cref="Map.PlacedBuildings"/> index, or -1 for a <see cref="Map.DestructibleBox"/>.</summary>
    public int Building = -1;

    /// <summary>Its chunks are <see cref="MapDestruction"/> ids [ChunkStart .. ChunkStart + ChunkCount), in Solid order.</summary>
    public int ChunkStart, ChunkCount;

    /// <summary>The Solid range its chunks live in (a building: exactly its shell).</summary>
    public int SolidStart, SolidCount;

    public Box Bounds;

    /// <summary>The ground it stands on (chunks reaching down to here hold everything else up).</summary>
    public float BaseY;

    /// <summary>Chunks broken or fallen so far this match.</summary>
    public int BrokenCount;

    /// <summary>Goes up whenever one of its chunks breaks, falls or comes back (the renderer refreshes the building).</summary>
    public int Version;

    /// <summary>The longest support path (m) as built: how far its farthest chunk is from the ground along what holds it.</summary>
    public float MaxSpanAsBuilt;
}

/// <summary>
/// The destructible chunks of a map (Buildings/README.md, "Destruction"). When a map with destruction loads, every shell box
/// of its destructible buildings is cut into chunks (wall panels about 1 x 1.25 m that keep the door and window openings,
/// floor and roof slabs about 2 x 2 m), and those chunks are the building's ordinary shell boxes in <see cref="Map.Solid"/>:
/// collision, bullets, decals, the building renderer and the nav build all treat them like any wall. A broken chunk's box
/// is parked under the world (like a broken prop's), so it stops blocking anything, its decals hide and the renderer drops it.
///
/// <para><b>Support.</b> Chunks touching each other form a graph per structure. Support flows up from the chunks on the
/// ground (free when a chunk rests on the one under it) and sideways or downwards at the distance between the chunks'
/// centres. A chunk farther from the ground along its best path than its reach (its material's "span", or what it needed as
/// built plus a margin) falls. <see cref="Collapse"/> runs that flood (Dijkstra, ties by index, so the same on every PC)
/// over one structure after something in it broke.</para>
///
/// <para>Runtime state (health, broken flags) is per match (<see cref="Map.RestoreDestruction"/>) and never touches the
/// map's fingerprint: broken chunks count as built.</para>
/// </summary>
public sealed class MapDestruction
{
    public const byte FlagAnchor = 1, FlagFixed = 2, FlagSkin = 4, FlagFloor = 8, FlagStatic = 16;

    public readonly List<DestructibleStructure> Structures = new();
    public MaterialDestruction Table = MaterialDestruction.Current;

    public int Count;
    public int[] Solid = [];
    public byte[] Mat = [];
    public byte[] Flags = [];
    public float[] MaxHp = [];
    public float[] Hp = [];
    public bool[] Broken = [];
    public int[] Owner = [];
    public int[] Host = [];
    public float[] Allowed = [];
    public float[] BuiltDist = [];
    public Box[] Original = [];

    /// <summary>Support graph (directed, CSR): the edges out of chunk c are [AdjStart[c] .. AdjStart[c+1]).</summary>
    public int[] AdjStart = [0];

    public int[] AdjTo = [];
    public float[] AdjCost = [];

    /// <summary>Skins (2 cm linings) glued to each host chunk: [SkinStart[c] .. SkinStart[c+1]) in <see cref="SkinList"/>.</summary>
    public int[] SkinStart = [0];

    public int[] SkinList = [];

    /// <summary>The chunk id of each Solid box (-1: not a chunk).</summary>
    public int[] ChunkOfSolid = [];

    /// <summary>Goes up on every change (any structure).</summary>
    public int Version;

    public double BuildMs;
    public int EdgeCount => AdjTo.Length;

    // Scratch for the flood (sized once; no allocation per collapse).
    private float[] _dist = [];
    private int[] _heapNode = [];
    private float[] _heapKey = [];
    private int _heapCount;

    public bool IsChunk(int solid, out int chunk)
    {
        chunk = (uint)solid < (uint)ChunkOfSolid.Length ? ChunkOfSolid[solid] : -1;
        return chunk >= 0;
    }

    public bool IsBroken(int c) => Broken[c];

    public DestructionMaterial MaterialOf(int c) => Table[Mat[c]];

    /// <summary>The chunk damage is applied to: a skin's host (a lining breaks with the wall behind it).</summary>
    public int Target(int c) => (Flags[c] & FlagSkin) != 0 && Host[c] >= 0 ? Host[c] : c;

    // ---- Build (map load) ------------------------------------------------------------------------------------

    /// <summary>Builds the support graph and every chunk's reach from the chunks the loader recorded. Map load only.</summary>
    internal void Build(Map map, List<(int Solid, byte Mat, byte Flags, int Structure)> chunks)
    {
        var sw = Stopwatch.StartNew();
        var n = chunks.Count;
        Count = n;
        Solid = new int[n];
        Mat = new byte[n];
        Flags = new byte[n];
        MaxHp = new float[n];
        Hp = new float[n];
        Broken = new bool[n];
        Owner = new int[n];
        Host = new int[n];
        Allowed = new float[n];
        BuiltDist = new float[n];
        Original = new Box[n];
        ChunkOfSolid = new int[map.Solid.Count];
        Array.Fill(ChunkOfSolid, -1);
        for (var c = 0; c < n; c++)
        {
            var (solid, mat, flags, s) = chunks[c];
            Solid[c] = solid;
            Mat[c] = mat;
            Flags[c] = flags;
            Owner[c] = s;
            Host[c] = -1;
            Original[c] = map.Solid[solid];
            ChunkOfSolid[solid] = c;
            var h = Original[c].Half * 2;
            var volume = h.X * h.Y * h.Z;
            MaxHp[c] = (flags & FlagFixed) != 0 ? float.PositiveInfinity : Table[mat].Hp * Math.Clamp(MathF.Sqrt(volume / 0.25f), 0.5f, 2f);
            Hp[c] = MaxHp[c];
        }

        // Neighbours: chunks of the same structure whose boxes touch face to face (within 3 cm).
        var edges = new List<(int A, int B, float Cost)>(n * 6);
        var skins = new List<(int Skin, int Host)>();
        var cells = new Dictionary<long, List<int>>();
        const float eps = 0.03f, cell = 1.0f;
        static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);
        foreach (var st in Structures)
        {
            cells.Clear();
            for (var c = st.ChunkStart; c < st.ChunkStart + st.ChunkCount; c++)
            {
                var b = Original[c];
                var lo = b.Min - new Vector3(eps);
                var hi = b.Max + new Vector3(eps);
                for (var x = (int)MathF.Floor(lo.X / cell); x <= (int)MathF.Floor(hi.X / cell); x++)
                for (var y = (int)MathF.Floor(lo.Y / cell); y <= (int)MathF.Floor(hi.Y / cell); y++)
                for (var z = (int)MathF.Floor(lo.Z / cell); z <= (int)MathF.Floor(hi.Z / cell); z++)
                {
                    var k = Key(x, y, z);
                    if (!cells.TryGetValue(k, out var list))
                        cells[k] = list = new List<int>(4);
                    list.Add(c);
                }
            }
            var bestHost = new Dictionary<int, (int Host, float Area)>();
            var seen = new HashSet<long>();
            foreach (var list in cells.Values)
                for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                {
                    int a = Math.Min(list[i], list[j]), b = Math.Max(list[i], list[j]);
                    if (a == b || !seen.Add(((long)a << 32) | (uint)b))
                        continue;
                    var ba = Original[a];
                    var bb = Original[b];
                    var ox = MathF.Min(ba.Max.X, bb.Max.X) - MathF.Max(ba.Min.X, bb.Min.X);
                    var oy = MathF.Min(ba.Max.Y, bb.Max.Y) - MathF.Max(ba.Min.Y, bb.Min.Y);
                    var oz = MathF.Min(ba.Max.Z, bb.Max.Z) - MathF.Max(ba.Min.Z, bb.Min.Z);
                    if (ox < -eps || oy < -eps || oz < -eps)
                        continue;
                    var positive = (ox > eps ? 1 : 0) + (oy > eps ? 1 : 0) + (oz > eps ? 1 : 0);
                    if (positive < 2)
                        continue; // only an edge or a corner touching
                    var skinA = (Flags[a] & FlagSkin) != 0;
                    var skinB = (Flags[b] & FlagSkin) != 0;
                    if (skinA || skinB)
                    {
                        if (skinA && skinB)
                            continue;
                        var area = MathF.Max(0, ox) * MathF.Max(0, oy) + MathF.Max(0, oy) * MathF.Max(0, oz) + MathF.Max(0, ox) * MathF.Max(0, oz);
                        var (skin, host) = skinA ? (a, b) : (b, a);
                        if (!bestHost.TryGetValue(skin, out var cur) || area > cur.Area + 1e-5f || (MathF.Abs(area - cur.Area) <= 1e-5f && host < cur.Host))
                            bestHost[skin] = (host, area);
                        continue;
                    }
                    var dist = Vector3.Distance(ba.Center, bb.Center);
                    edges.Add((a, b, Cost(bb, ba, ox, oz, dist))); // b holds a up (stored as the edge b -> a, see below)
                    edges.Add((b, a, Cost(ba, bb, ox, oz, dist)));
                }
            foreach (var (skin, (host, _)) in bestHost)
            {
                Host[skin] = host;
                skins.Add((skin, host));
            }
        }

        // CSR: edges out of each chunk. Entry (A, B, cost) is the edge B -> A (B holds A up at that cost).
        AdjStart = new int[n + 1];
        foreach (var e in edges)
            AdjStart[e.B + 1]++;
        for (var c = 0; c < n; c++)
            AdjStart[c + 1] += AdjStart[c];
        AdjTo = new int[edges.Count];
        AdjCost = new float[edges.Count];
        var fill = (int[])AdjStart.Clone();
        edges.Sort((x, y) => x.B != y.B ? x.B.CompareTo(y.B) : x.A.CompareTo(y.A)); // a fixed order: the flood is the same everywhere
        foreach (var e in edges)
        {
            var at = fill[e.B]++;
            AdjTo[at] = e.A;
            AdjCost[at] = e.Cost;
        }
        SkinStart = new int[n + 1];
        foreach (var (_, host) in skins)
            SkinStart[host + 1]++;
        for (var c = 0; c < n; c++)
            SkinStart[c + 1] += SkinStart[c];
        SkinList = new int[skins.Count];
        var sfill = (int[])SkinStart.Clone();
        skins.Sort((x, y) => x.Skin.CompareTo(y.Skin));
        foreach (var (skin, host) in skins)
            SkinList[sfill[host]++] = skin;

        // Reach as built: every chunk gets max(its material's span, its support distance as built + the margin). A chunk
        // the flood can't reach from the ground at all (a detached bit of trim) holds itself up.
        var maxChunks = 0;
        foreach (var st in Structures)
            maxChunks = Math.Max(maxChunks, st.ChunkCount);
        _dist = new float[maxChunks];
        var heap = Math.Max(16, edges.Count + n);
        _heapNode = new int[heap];
        _heapKey = new float[heap];
        Array.Fill(Allowed, float.PositiveInfinity);
        foreach (var st in Structures)
        {
            Flood(st, initial: true);
            var most = 0f;
            for (var c = st.ChunkStart; c < st.ChunkStart + st.ChunkCount; c++)
            {
                var d = _dist[c - st.ChunkStart];
                BuiltDist[c] = d;
                if (float.IsPositiveInfinity(d))
                {
                    if ((Flags[c] & FlagSkin) == 0)
                        Flags[c] |= FlagStatic | FlagAnchor;
                    Allowed[c] = float.PositiveInfinity;
                    continue;
                }
                most = MathF.Max(most, d);
                Allowed[c] = MathF.Max(Table[Mat[c]].Span, d + Table.Rules.SpanMargin);
            }
            st.MaxSpanAsBuilt = most;
        }
        BuildMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Hanging from a chunk above costs this much more than reaching sideways (per metre).</summary>
    public const float HangCost = 1.5f;

    /// <summary>
    /// What it costs <paramref name="holder"/> to hold <paramref name="held"/> up: nothing from right under it, the distance
    /// between their centres sideways, <see cref="HangCost"/> times that hanging from above.
    /// </summary>
    private static float Cost(in Box holder, in Box held, float ox, float oz, float dist) =>
        Supports(holder, held, ox, oz) ? 0f : holder.Center.Y > held.Center.Y + 0.05f ? dist * HangCost : dist;

    /// <summary>Does <paramref name="below"/> hold <paramref name="above"/> up from under it (overlapping footprints, lower bottom)?</summary>
    private static bool Supports(in Box below, in Box above, float ox, float oz) =>
        ox > 0.01f && oz > 0.01f && below.Min.Y < above.Min.Y - 0.01f && below.Max.Y <= above.Max.Y + 0.01f;

    // ---- The flood --------------------------------------------------------------------------------------------

    /// <summary>
    /// Support distances over one structure's unbroken chunks into <c>_dist</c> (index: chunk - ChunkStart). With
    /// <paramref name="initial"/> false, a chunk past its reach holds nothing up (it's falling).
    /// </summary>
    private void Flood(DestructibleStructure st, bool initial)
    {
        var start = st.ChunkStart;
        var count = st.ChunkCount;
        Array.Fill(_dist, float.PositiveInfinity, 0, count);
        _heapCount = 0;
        for (var c = start; c < start + count; c++)
            if (!Broken[c] && (Flags[c] & (FlagAnchor | FlagSkin)) == FlagAnchor)
            {
                _dist[c - start] = 0;
                Push(c, 0);
            }
        while (_heapCount > 0)
        {
            var (u, du) = Pop();
            if (du > _dist[u - start])
                continue; // a stale entry
            if (!initial && du > Allowed[u])
                continue; // falls: holds nothing up
            for (var e = AdjStart[u]; e < AdjStart[u + 1]; e++)
            {
                var v = AdjTo[e];
                if (Broken[v])
                    continue;
                var nd = du + AdjCost[e];
                if (nd < _dist[v - start])
                {
                    _dist[v - start] = nd;
                    Push(v, nd);
                }
            }
        }
    }

    private void Push(int node, float key)
    {
        if (_heapCount == _heapNode.Length)
        {
            Array.Resize(ref _heapNode, _heapCount * 2);
            Array.Resize(ref _heapKey, _heapCount * 2);
        }
        var i = _heapCount++;
        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (Less(_heapKey[parent], _heapNode[parent], key, node))
                break;
            _heapNode[i] = _heapNode[parent];
            _heapKey[i] = _heapKey[parent];
            i = parent;
        }
        _heapNode[i] = node;
        _heapKey[i] = key;
    }

    private static bool Less(float ka, int na, float kb, int nb) => ka < kb || (ka == kb && na <= nb);

    private (int Node, float Key) Pop()
    {
        var top = (_heapNode[0], _heapKey[0]);
        var lastNode = _heapNode[--_heapCount];
        var lastKey = _heapKey[_heapCount];
        var i = 0;
        while (true)
        {
            var c = i * 2 + 1;
            if (c >= _heapCount)
                break;
            if (c + 1 < _heapCount && Less(_heapKey[c + 1], _heapNode[c + 1], _heapKey[c], _heapNode[c]))
                c++;
            if (Less(lastKey, lastNode, _heapKey[c], _heapNode[c]))
                break;
            _heapNode[i] = _heapNode[c];
            _heapKey[i] = _heapKey[c];
            i = c;
        }
        if (_heapCount > 0)
        {
            _heapNode[i] = lastNode;
            _heapKey[i] = lastKey;
        }
        return top;
    }

    /// <summary>
    /// Runs the support flood over structure <paramref name="s"/> and adds every unbroken chunk that lost its support to
    /// <paramref name="falling"/> (not broken yet: <see cref="Map.CollapseStructure"/> does that). Returns how many.
    /// </summary>
    public int Unsupported(int s, List<int> falling)
    {
        var st = Structures[s];
        Flood(st, initial: false);
        var n = 0;
        for (var c = st.ChunkStart; c < st.ChunkStart + st.ChunkCount; c++)
        {
            if (Broken[c] || (Flags[c] & (FlagSkin | FlagAnchor | FlagFixed)) != 0)
                continue;
            if (_dist[c - st.ChunkStart] > Allowed[c])
            {
                falling.Add(c);
                n++;
            }
        }
        return n;
    }

    /// <summary>The support distance of chunk <paramref name="c"/> from the last flood over its structure (self-test).</summary>
    public float LastDistance(int c) => _dist[c - Structures[Owner[c]].ChunkStart];
}

/// <summary>Cuts a building's shell boxes into chunks (Buildings/README.md, "Destruction"). Pure: the same boxes give the same chunks.</summary>
public static class StructureChunker
{
    /// <summary>How a box is cut.</summary>
    public enum Shape : byte
    {
        Single,
        Wall,
        Slab,
        Column,
        Block,
    }

    /// <summary>What kind of box this is (from its proportions).</summary>
    public static Shape Classify(in Box b)
    {
        var s = b.Half * 2;
        var thin = MathF.Min(s.X, s.Z);
        var longest = MathF.Max(s.X, s.Z);
        if (s.Y <= 0.45f && s.X >= 0.6f && s.Z >= 0.6f)
            return Shape.Slab;
        if (thin <= 0.7f && s.Y >= 0.5f && longest >= 0.6f)
            return Shape.Wall;
        if (thin <= 0.7f && s.Y > 1.6f)
            return Shape.Column;
        if (s.X > 0.7f && s.Y > 0.7f && s.Z > 0.7f && (s.X > 2.4f || s.Y > 2.4f || s.Z > 2.4f))
            return Shape.Block;
        return Shape.Single;
    }

    /// <summary>
    /// Cuts <paramref name="b"/> into pieces (added to <paramref name="into"/>, in a fixed order). Cuts sit on a grid fixed in
    /// the world (x and z from 0, y from <paramref name="baseY"/>) so a wall and its lining are cut in the same places; a cut
    /// closer than <see cref="DestructionRules.MinPiece"/> to an end is skipped. <paramref name="scale"/> grows every cell
    /// (the per-building budget). Returns the piece count (1: the box itself).
    /// </summary>
    public static int Split(in Box b, DestructionRules r, float baseY, float scale, List<Box> into)
    {
        Span<float> xs = stackalloc float[66];
        Span<float> ys = stackalloc float[66];
        Span<float> zs = stackalloc float[66];
        var shape = Classify(b);
        var min = b.Min;
        var max = b.Max;
        var s = b.Half * 2;
        for (var grow = 1f; ; grow *= 1.5f)
        {
            var k = scale * grow;
            int nx = 1, ny = 1, nz = 1;
            xs[0] = min.X;
            ys[0] = min.Y;
            zs[0] = min.Z;
            switch (shape)
            {
                case Shape.Wall when s.X >= s.Z:
                    nx = Cuts(min.X, max.X, r.WallCell * k, 0, r.MinPiece, xs);
                    ny = Cuts(min.Y, max.Y, r.WallRow * k, baseY, r.MinPiece, ys);
                    break;
                case Shape.Wall:
                    nz = Cuts(min.Z, max.Z, r.WallCell * k, 0, r.MinPiece, zs);
                    ny = Cuts(min.Y, max.Y, r.WallRow * k, baseY, r.MinPiece, ys);
                    break;
                case Shape.Slab:
                    nx = Cuts(min.X, max.X, r.SlabCell * k, 0, r.MinPiece, xs);
                    nz = Cuts(min.Z, max.Z, r.SlabCell * k, 0, r.MinPiece, zs);
                    break;
                case Shape.Column:
                    ny = Cuts(min.Y, max.Y, r.WallRow * k, baseY, r.MinPiece, ys);
                    break;
                case Shape.Block:
                    nx = Cuts(min.X, max.X, r.SlabCell * k, 0, r.MinPiece, xs);
                    ny = Cuts(min.Y, max.Y, r.SlabCell * k, baseY, r.MinPiece, ys);
                    nz = Cuts(min.Z, max.Z, r.SlabCell * k, 0, r.MinPiece, zs);
                    break;
            }
            if ((nx * ny * nz > r.MaxPerBox || nx > 64 || ny > 64 || nz > 64) && grow < 64)
                continue;
            xs[nx] = max.X;
            ys[ny] = max.Y;
            zs[nz] = max.Z;
            for (var iy = 0; iy < ny; iy++)
            for (var ix = 0; ix < nx; ix++)
            for (var iz = 0; iz < nz; iz++)
            {
                var lo = new Vector3(xs[ix], ys[iy], zs[iz]);
                var hi = new Vector3(xs[ix + 1], ys[iy + 1], zs[iz + 1]);
                into.Add(nx * ny * nz == 1 ? b : new Box((lo + hi) / 2, (hi - lo) / 2));
            }
            return nx * ny * nz;
        }
    }

    /// <summary>Cut positions on a grid of <paramref name="cell"/> from <paramref name="origin"/> strictly inside (lo, hi); writes lo then the cuts. Returns the piece count.</summary>
    private static int Cuts(float lo, float hi, float cell, float origin, float minPiece, Span<float> into)
    {
        into[0] = lo;
        var n = 1;
        if (hi - lo < cell + minPiece * 0.5f)
            return 1;
        var k = (int)MathF.Ceiling((lo + minPiece - origin) / cell);
        for (; n < into.Length - 1; k++)
        {
            var x = origin + k * cell;
            if (x > hi - minPiece)
                break;
            if (x - into[n - 1] < minPiece)
                continue;
            into[n++] = x;
        }
        return n;
    }
}
