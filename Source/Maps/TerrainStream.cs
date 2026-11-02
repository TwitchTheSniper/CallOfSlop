using System.Collections.Concurrent;
using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// A streamed terrain's heights (<see cref="TerrainSpec.Stream"/>): the grid is never built whole. It is cut into tiles of
/// <see cref="Tile"/> x <see cref="Tile"/> samples, and a tile is worked out from the map's features
/// (<see cref="TerrainGen.GenerateBlock"/>) the first time anything reads a sample in it: a player walking, a bullet, the
/// renderer. Tiles nobody has read for a while are dropped once more than <see cref="Budget"/> are held, and made again
/// (the same numbers: they're a function of position alone) when needed. Players, the camera and the server ask for the
/// tiles round them a little ahead (<see cref="Prefetch"/>), on a worker, so walking never waits for one.
/// Thread-safe: tiles are published whole with one reference swap, and a dropped tile stays valid for whoever still holds it.
/// </summary>
public sealed class TerrainStream
{
    /// <summary>Samples along a tile's side (a power of two: sample (i, j) is in tile (i / Tile, j / Tile)).</summary>
    public const int Tile = 64;

    private const int TileShift = 6, TileMask = Tile - 1;

    /// <summary>Tiles held before the least recently read ones are dropped (20 KB each).</summary>
    public const int Budget = 1536;

    public readonly TerrainSpec Spec;
    public readonly MapBoundary? Boundary;
    public readonly double Cell, Ox, Oz;
    public readonly int Nx, Nz, TilesX, TilesZ;

    private sealed class TileData(float[] h, byte[] m)
    {
        public readonly float[] H = h;
        public readonly byte[] M = m;
    }

    /// <summary>The tiles and the work on them: runtime only (a baked map makes its own on first use).</summary>
    private sealed class Runtime(int tiles)
    {
        public readonly TileData?[] Tiles = new TileData?[tiles];
        public readonly int[] Used = new int[tiles];
        public int Clock, Held, Made;
        public readonly object TrimLock = new();
        public readonly ConcurrentQueue<int> Queue = new();
        public readonly byte[] Queued = new byte[tiles];
        public int WorkerRunning;
    }

    [NotBaked]
    private Runtime? _rt;

    private Runtime Rt => Volatile.Read(ref _rt) ?? MakeRuntime();

    private Runtime MakeRuntime()
    {
        Interlocked.CompareExchange(ref _rt, new Runtime(TilesX * TilesZ), null);
        return _rt!;
    }

    /// <summary>Lowest and highest ground, from a coarse pass at load (with room for what the coarse pass misses).</summary>
    public readonly float MinHeight, MaxHeight;

    private TerrainStream(TerrainSpec spec, double cell, double ox, double oz, int nx, int nz, MapBoundary? boundary)
    {
        Spec = spec;
        Boundary = boundary;
        Cell = cell;
        Ox = ox;
        Oz = oz;
        Nx = nx;
        Nz = nz;
        TilesX = (nx + Tile - 1) / Tile;
        TilesZ = (nz + Tile - 1) / Tile;
        (MinHeight, MaxHeight) = CoarseBounds();
    }

    /// <summary>A streamed terrain for a map (the whole grid's size and origin, as <see cref="TerrainGen.Generate"/> works them out).</summary>
    public static TerrainData Create(TerrainSpec spec, double cell, double ox, double oz, int nx, int nz, MapBoundary? boundary) =>
        new(new TerrainStream(spec, cell, ox, oz, nx, nz, boundary));

    /// <summary>Tiles held right now, and made since the map loaded (the self-test and the perf window).</summary>
    public (int Held, int Made) Stats => _rt is { } rt ? (Volatile.Read(ref rt.Held), Volatile.Read(ref rt.Made)) : (0, 0);

    /// <summary>Height of sample (i, j) (inside the grid).</summary>
    public float Height(int i, int j) => TileAt(i >> TileShift, j >> TileShift).H[((j & TileMask) << TileShift) | (i & TileMask)];

    /// <summary>Surface type of sample (i, j).</summary>
    public byte Material(int i, int j) => TileAt(i >> TileShift, j >> TileShift).M[((j & TileMask) << TileShift) | (i & TileMask)];

    /// <summary>The four corners of cell (i, j): (i, j), (i + 1, j), (i, j + 1), (i + 1, j + 1).</summary>
    public void Corners(int i, int j, out float h00, out float h10, out float h01, out float h11)
    {
        int li = i & TileMask, lj = j & TileMask;
        if (li < TileMask && lj < TileMask)
        {
            var h = TileAt(i >> TileShift, j >> TileShift).H;
            var k = (lj << TileShift) | li;
            h00 = h[k];
            h10 = h[k + 1];
            h01 = h[k + Tile];
            h11 = h[k + Tile + 1];
            return;
        }
        h00 = Height(i, j);
        h10 = Height(i + 1, j);
        h01 = Height(i, j + 1);
        h11 = Height(i + 1, j + 1);
    }

    /// <summary>Is tile (ti, tj) held now (no work to read it)?</summary>
    public bool Has(int ti, int tj) => Volatile.Read(ref Rt.Tiles[tj * TilesX + ti]) != null;

    private TileData TileAt(int ti, int tj)
    {
        var rt = Rt;
        var k = tj * TilesX + ti;
        var t = Volatile.Read(ref rt.Tiles[k]) ?? Make(rt, k);
        rt.Used[k] = rt.Clock; // (a benign race: only "recently read" matters)
        return t;
    }

    /// <summary>Works tile <paramref name="k"/> out (or takes the copy another thread just made) and drops old tiles over budget.</summary>
    private TileData Make(Runtime rt, int k)
    {
        int ti = k % TilesX, tj = k / TilesX;
        const int n = Tile + 2; // a one-sample apron, so slopes (and so materials) at the tile's edge match the whole grid's
        var h = new float[n * n];
        var m = new byte[n * n];
        TerrainGen.GenerateBlock(Spec, Boundary, Ox + (ti * Tile - 1) * Cell, Oz + (tj * Tile - 1) * Cell, n, n, Cell, h, m);
        var th = new float[Tile * Tile];
        var tm = new byte[Tile * Tile];
        for (var j = 0; j < Tile; j++)
        {
            Array.Copy(h, (j + 1) * n + 1, th, j * Tile, Tile);
            Array.Copy(m, (j + 1) * n + 1, tm, j * Tile, Tile);
        }
        var made = new TileData(th, tm);
        var won = Interlocked.CompareExchange(ref rt.Tiles[k], made, null);
        if (won != null)
            return won;
        rt.Used[k] = Interlocked.Increment(ref rt.Clock);
        Interlocked.Increment(ref rt.Made);
        if (Interlocked.Increment(ref rt.Held) > Budget)
            Trim(rt);
        return made;
    }

    /// <summary>Drops the least recently read tiles down to three quarters of the budget.</summary>
    private static void Trim(Runtime rt)
    {
        if (!Monitor.TryEnter(rt.TrimLock))
            return; // someone else is trimming
        try
        {
            var live = new List<(int Used, int K)>(rt.Held);
            for (var k = 0; k < rt.Tiles.Length; k++)
                if (Volatile.Read(ref rt.Tiles[k]) != null)
                    live.Add((rt.Used[k], k));
            live.Sort();
            var drop = live.Count - Budget * 3 / 4;
            for (var d = 0; d < drop; d++)
                if (Interlocked.Exchange(ref rt.Tiles[live[d].K], null) != null)
                    Interlocked.Decrement(ref rt.Held);
        }
        finally
        {
            Monitor.Exit(rt.TrimLock);
        }
    }

    /// <summary>
    /// Asks for the tiles within <paramref name="radius"/> of (x, z) on a worker, so a later read finds them ready. Cheap
    /// when they're all held (a few array reads); never blocks.
    /// </summary>
    public void Prefetch(float x, float z, float radius)
    {
        int ti0 = TileOf(x - radius, Ox, TilesX), ti1 = TileOf(x + radius, Ox, TilesX);
        int tj0 = TileOf(z - radius, Oz, TilesZ), tj1 = TileOf(z + radius, Oz, TilesZ);
        var any = false;
        var rt = Rt;
        for (var tj = tj0; tj <= tj1; tj++)
        for (var ti = ti0; ti <= ti1; ti++)
        {
            var k = tj * TilesX + ti;
            if (Volatile.Read(ref rt.Tiles[k]) != null)
            {
                rt.Used[k] = rt.Clock;
                continue;
            }
            if (Interlocked.Exchange(ref rt.Queued[k], 1) == 0)
            {
                rt.Queue.Enqueue(k);
                any = true;
            }
        }
        if (any && Interlocked.CompareExchange(ref rt.WorkerRunning, 1, 0) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(_ => Work(rt), null);
    }

    private int TileOf(float v, double origin, int tiles) => Math.Clamp((int)Math.Floor((v - origin) / Cell) >> TileShift, 0, tiles - 1);

    private void Work(Runtime rt)
    {
        while (true)
        {
            while (rt.Queue.TryDequeue(out var k))
            {
                if (Volatile.Read(ref rt.Tiles[k]) == null)
                    Make(rt, k);
                Volatile.Write(ref rt.Queued[k], 0);
            }
            Volatile.Write(ref rt.WorkerRunning, 0);
            if (rt.Queue.IsEmpty || Interlocked.CompareExchange(ref rt.WorkerRunning, 1, 0) != 0)
                return;
        }
    }

    /// <summary>Is the prefetch worker idle with nothing queued (the self-test waits on it)?</summary>
    public bool Idle => _rt is not { } rt || (rt.Queue.IsEmpty && Volatile.Read(ref rt.WorkerRunning) == 0);

    /// <summary>Drops every tile (a map reload, the self-test).</summary>
    public void Clear()
    {
        if (_rt is not { } rt)
            return;
        lock (rt.TrimLock)
            for (var k = 0; k < rt.Tiles.Length; k++)
                if (Interlocked.Exchange(ref rt.Tiles[k], null) != null)
                    Interlocked.Decrement(ref rt.Held);
    }

    /// <summary>
    /// The height range: the features worked out every <see cref="Tile"/> samples over the whole map, plus a margin for
    /// what that misses between the coarse samples (a tenth of the range and 40 m). Callers use it as a bound (rays skip
    /// what's above it, bodies are never put below it), so it errs wide.
    /// </summary>
    private (float Min, float Max) CoarseBounds()
    {
        var sw = Stopwatch.StartNew();
        int cx = (Nx - 1) / Tile + 2, cz = (Nz - 1) / Tile + 2;
        var h = new float[cx * cz];
        var m = new byte[h.Length];
        TerrainGen.GenerateBlock(Spec, Boundary, Ox, Oz, cx, cz, Cell * Tile, h, m);
        float lo = h.Min(), hi = h.Max();
        var pad = (hi - lo) * 0.25f + 60f; // (coarse samples miss narrow peaks: a 40 m mountain can stand 45 m above them)
        if (sw.ElapsedMilliseconds > 200)
            Console.WriteLine($"[terrain] streamed terrain bounds took {sw.ElapsedMilliseconds} ms ({cx} x {cz} samples)");
        return (lo - pad, hi + pad);
    }

    /// <summary>A fingerprint of everything the heights come from: the grid and the terrain's own definition.</summary>
    public uint Hash()
    {
        var h = 2166136261u;
        foreach (var c in $"{Nx}x{Nz}@{Cell}:{Ox},{Oz}|{Spec.ToJson("")}")
        {
            h ^= c;
            h *= 16777619u;
        }
        return h;
    }
}
