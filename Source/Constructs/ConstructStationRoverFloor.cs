using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The ground a station's rovers keep off (Sean 2026-11-28, "rovers should avoid driving on already placed parts when possible"): the
/// bay in 1 m cells, each marked where a rover of a class standing there would be over a part already fitted (each fitted step's
/// footprint grown by the class's half width and a margin). A drive goes the cheapest way over the cells (A*, a fitted cell costing
/// <see cref="Penalty"/> times a free one), so a rover goes round what is built when there is room and over it only when there is none
/// (a large rover in a narrow bay), then the way is pulled straight between corners it can see. Its stand moves off what is built to
/// the nearest spot that still reaches (<see cref="StationRovers.FreeStand"/>). Made again when a step is fitted or the bay changes;
/// routes are kept for the same ends until then, so a trip's tries (its waits) don't search again. Server only. Nothing here allocates
/// once made for a bay.
/// </summary>
public sealed class RoverFloor
{
    public const float Cell = 1f;
    public const float Penalty = 25f; // a metre over a fitted part costs this many metres of free floor
    public const int MaxWay = 6; // waypoints a route keeps: a carry from the dock with two full routes takes 36 of RoverPlan.MaxBase's 40 keys
    private const int CacheSize = 24;

    public int W, H; // cells across (x) and along (z)
    public float Hx, Hz; // the bay's half size
    public int Fitted = -1; // the job's fitted count it was made for
    public Vector4[]? Feet; // the job's step footprints it was made from
    private readonly byte[][] _cover = new byte[4][]; // per class (1..3): 1 where its rover would stand over a fitted part
    private float[] _top = []; // per cell: the highest top of a fitted part over it (bay floor up; a carry's height over its path)
    public Vector2 KLo, KHi; // the fitted parts' footprint (the rovers' keep-out ring grows with the build: Sean 2026-11-28)
    public bool Any; // anything fitted
    private float[] _g = [], _hf = [];
    private int[] _from = [], _seen = [], _heap = [];
    private int _stamp;
    private readonly (int Cls, Vector2 A, Vector2 B, int N, Vector2[] Way)[] _cache = new (int, Vector2, Vector2, int, Vector2[])[CacheSize];
    private int _cacheNext;

    public RoverFloor()
    {
        for (var k = 0; k < CacheSize; k++)
            _cache[k] = (0, default, default, -1, new Vector2[MaxWay]);
    }

    /// <summary>Is this floor still the one for the job's fitted steps in a bay this size?</summary>
    public bool Fresh(StationJob j, float hx, float hz) => Fitted == j.Fitted && ReferenceEquals(Feet, j.StepFoot) && Hx == hx && Hz == hz;

    /// <summary>Mark the cells again from the job's fitted steps (a fit, a new job, a bay resized).</summary>
    public void Make(StationJob j, float hx, float hz)
    {
        (Hx, Hz, Fitted, Feet) = (hx, hz, j.Fitted, j.StepFoot);
        int w = Math.Max(1, (int)MathF.Ceiling(hx * 2 / Cell)), h = Math.Max(1, (int)MathF.Ceiling(hz * 2 / Cell));
        if (w * h != W * H || _g.Length != w * h)
        {
            var n = w * h;
            for (var c = 1; c < 4; c++)
                _cover[c] = new byte[n];
            _top = new float[n];
            _g = new float[n];
            _from = new int[n];
            _seen = new int[n];
            _heap = new int[n * 8 + 16];
            _hf = new float[n * 8 + 16];
            _stamp = 0;
        }
        (W, H) = (w, h);
        for (var c = 1; c < 4; c++)
            Array.Clear(_cover[c]);
        Array.Fill(_top, float.MinValue);
        (KLo, KHi, Any) = (new Vector2(float.MaxValue), new Vector2(float.MinValue), false);
        for (var k = 0; k < _cache.Length; k++)
            _cache[k].N = -1;
        var feet = j.StepFoot;
        for (var k = 0; k < feet.Length && k < j.PartOf.Length; k++)
        {
            if (j.PartOf[k] < 0)
                continue;
            var f = feet[k];
            if (f.Z <= f.X && f.W <= f.Y)
                continue;
            (KLo, KHi, Any) = (Vector2.Min(KLo, new Vector2(f.X, f.Y)), Vector2.Max(KHi, new Vector2(f.Z, f.W)), true);
            if (k < j.StepHi.Length)
                Top(f.X, f.Y, f.Z, f.W, j.StepHi[k]);
            for (var c = 1; c < 4; c++)
            {
                var m = StationRovers.Classes[c].HalfWid + 0.1f;
                Mark(_cover[c], f.X - m, f.Y - m, f.Z + m, f.W + m);
            }
        }
    }

    private void Mark(byte[] cover, float x0, float z0, float x1, float z1)
    {
        // (every cell the grown footprint touches: a stand anywhere in a marked cell may be over it, so none is missed by rounding)
        int i0 = Math.Max(0, (int)MathF.Floor((x0 + Hx) / Cell)), i1 = Math.Min(W - 1, (int)MathF.Ceiling((x1 + Hx) / Cell) - 1);
        int k0 = Math.Max(0, (int)MathF.Floor((z0 + Hz) / Cell)), k1 = Math.Min(H - 1, (int)MathF.Ceiling((z1 + Hz) / Cell) - 1);
        for (var k = k0; k <= k1; k++)
            for (var i = i0; i <= i1; i++)
                cover[k * W + i] = 1;
    }

    private void Top(float x0, float z0, float x1, float z1, float top)
    {
        int i0 = Math.Max(0, (int)MathF.Floor((x0 + Hx) / Cell)), i1 = Math.Min(W - 1, (int)MathF.Ceiling((x1 + Hx) / Cell) - 1);
        int k0 = Math.Max(0, (int)MathF.Floor((z0 + Hz) / Cell)), k1 = Math.Min(H - 1, (int)MathF.Ceiling((z1 + Hz) / Cell) - 1);
        for (var k = k0; k <= k1; k++)
            for (var i = i0; i <= i1; i++)
                _top[k * W + i] = MathF.Max(_top[k * W + i], top);
    }

    /// <summary>The highest top of a fitted part within <paramref name="r"/> of <paramref name="p"/> (a square of cells; float.MinValue: none).</summary>
    public float TopNear(Vector2 p, float r)
    {
        if (W == 0)
            return float.MinValue;
        int i0 = Math.Max(0, (int)MathF.Floor((p.X - r + Hx) / Cell)), i1 = Math.Min(W - 1, (int)MathF.Floor((p.X + r + Hx) / Cell));
        int k0 = Math.Max(0, (int)MathF.Floor((p.Y - r + Hz) / Cell)), k1 = Math.Min(H - 1, (int)MathF.Floor((p.Y + r + Hz) / Cell));
        var best = float.MinValue;
        for (var k = k0; k <= k1; k++)
            for (var i = i0; i <= i1; i++)
                best = MathF.Max(best, _top[k * W + i]);
        return best;
    }

    private int CellOf(Vector2 p)
    {
        var i = Math.Clamp((int)((p.X + Hx) / Cell), 0, W - 1);
        var k = Math.Clamp((int)((p.Y + Hz) / Cell), 0, H - 1);
        return k * W + i;
    }

    private Vector2 Mid(int c) => new(-Hx + (c % W + 0.5f) * Cell, -Hz + (c / W + 0.5f) * Cell);

    /// <summary>Would a rover of class <paramref name="cls"/> standing at <paramref name="p"/> be over a fitted part?</summary>
    public bool Covered(int cls, Vector2 p) => cls is >= 1 and <= 3 && W > 0 && _cover[cls][CellOf(p)] != 0;

    /// <summary>How many of the half-metre steps along a to b are over fitted parts for class <paramref name="cls"/>.</summary>
    public int Over(int cls, Vector2 a, Vector2 b)
    {
        var cover = _cover[cls];
        var len = Vector2.Distance(a, b);
        var steps = Math.Max(1, (int)(len / (Cell * 0.5f)));
        var n = 0;
        for (var s = 0; s <= steps; s++)
            if (cover[CellOf(Vector2.Lerp(a, b, s / (float)steps))] != 0)
                n++;
        return n;
    }

    /// <summary>
    /// The way from <paramref name="a"/> to <paramref name="b"/> for class <paramref name="cls"/> into <paramref name="way"/> (b last, a
    /// left out): straight when nothing fitted is in the way, else the cheapest way over the cells pulled straight between corners.
    /// </summary>
    public int Route(int cls, Vector2 a, Vector2 b, Span<Vector2> way)
    {
        if (cls is < 1 or > 3 || W == 0 || way.Length == 0)
        {
            way[0] = b;
            return 1;
        }
        if (Over(cls, a, b) == 0)
        {
            way[0] = b;
            return 1;
        }
        for (var k = 0; k < _cache.Length; k++)
        {
            ref var e = ref _cache[k];
            if (e.N > 0 && e.Cls == cls && Vector2.DistanceSquared(e.A, a) < 1e-4f && Vector2.DistanceSquared(e.B, b) < 1e-4f)
            {
                var m = Math.Min(e.N, way.Length);
                e.Way.AsSpan(0, m).CopyTo(way);
                way[m - 1] = b;
                return m;
            }
        }
        var n = Search(cls, a, b, way);
        ref var slot = ref _cache[_cacheNext];
        _cacheNext = (_cacheNext + 1) % _cache.Length;
        (slot.Cls, slot.A, slot.B, slot.N) = (cls, a, b, n);
        way[..n].CopyTo(slot.Way);
        return n;
    }

    private int Search(int cls, Vector2 a, Vector2 b, Span<Vector2> way)
    {
        var cover = _cover[cls];
        int s = CellOf(a), goal = CellOf(b);
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen);
            _stamp = 1;
        }
        var hn = 0;
        _g[s] = 0;
        _from[s] = -1;
        _seen[s] = _stamp;
        Push(ref hn, s, Heur(s, goal));
        var found = false;
        while (hn > 0)
        {
            var u = Pop(ref hn, out var fu);
            if (u == goal)
            {
                found = true;
                break;
            }
            if (fu > _g[u] + Heur(u, goal) + 1e-3f)
                continue; // (a stale entry: a cheaper one was pushed since)
            int ui = u % W, uk = u / W;
            for (var dk = -1; dk <= 1; dk++)
                for (var di = -1; di <= 1; di++)
                {
                    if (di == 0 && dk == 0)
                        continue;
                    int vi = ui + di, vk = uk + dk;
                    if (vi < 0 || vk < 0 || vi >= W || vk >= H)
                        continue;
                    var v = vk * W + vi;
                    var step = di != 0 && dk != 0 ? 1.4142f : 1f;
                    var cost = _g[u] + step * Cell * (cover[v] != 0 ? Penalty : 1f);
                    if (_seen[v] == _stamp && cost >= _g[v])
                        continue;
                    _seen[v] = _stamp;
                    _g[v] = cost;
                    _from[v] = u;
                    if (hn < _heap.Length)
                        Push(ref hn, v, cost + Heur(v, goal));
                }
        }
        if (!found)
        {
            way[0] = b;
            return 1;
        }
        // The cells back from the goal, then pulled straight: from each corner, on to the furthest cell of the path it reaches with no
        // more steps over fitted parts than the path itself takes there (counted from the path's cells, two half-metre steps a cell, so
        // a corner costs one look along the line per cell of the path, never a walk of the path for each).
        Span<int> path = stackalloc int[Math.Min(W * H, 2048)];
        Span<int> cum = stackalloc int[path.Length + 1];
        var pn = 0;
        for (var c = goal; c >= 0 && pn < path.Length; c = _from[c])
            path[pn++] = c;
        path[..pn].Reverse();
        cum[0] = 0;
        for (var q = 0; q < pn; q++)
            cum[q + 1] = cum[q] + (cover[path[q]] != 0 ? 2 : 0);
        var n = 0;
        var from = a;
        var at = 0;
        while (n < way.Length - 1)
        {
            if (Over(cls, from, b) <= cum[pn] - cum[at] + 1)
                break;
            var best = at + 1;
            for (var q = pn - 2; q > at; q--)
                if (Over(cls, from, Mid(path[q])) <= cum[q + 1] - cum[at] + 1)
                {
                    best = q;
                    break;
                }
            if (best >= pn - 1)
                break;
            from = Mid(path[best]);
            way[n++] = from;
            at = best;
        }
        way[n++] = b;
        return n;
    }

    private float Heur(int u, int goal)
    {
        float dx = MathF.Abs(u % W - goal % W), dz = MathF.Abs(u / W - goal / W);
        return (MathF.Max(dx, dz) + 0.4142f * MathF.Min(dx, dz)) * Cell;
    }

    private void Push(ref int n, int v, float f)
    {
        var i = n++;
        _heap[i] = v;
        _hf[i] = f;
        while (i > 0)
        {
            var p = (i - 1) >> 1;
            if (_hf[p] <= _hf[i])
                break;
            (_heap[p], _heap[i]) = (_heap[i], _heap[p]);
            (_hf[p], _hf[i]) = (_hf[i], _hf[p]);
            i = p;
        }
    }

    private int Pop(ref int n, out float f)
    {
        var top = _heap[0];
        f = _hf[0];
        n--;
        _heap[0] = _heap[n];
        _hf[0] = _hf[n];
        var i = 0;
        while (true)
        {
            int l = i * 2 + 1, r = l + 1, m = i;
            if (l < n && _hf[l] < _hf[m])
                m = l;
            if (r < n && _hf[r] < _hf[m])
                m = r;
            if (m == i)
                break;
            (_heap[m], _heap[i]) = (_heap[i], _heap[m]);
            (_hf[m], _hf[i]) = (_hf[i], _hf[m]);
            i = m;
        }
        return top;
    }
}
