using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>The crowd tier's numbers (docs/zombies/MASS-HORDE.md). Code defaults for the spike; a rules section later.</summary>
public sealed class CrowdTuning
{
    /// <summary>A particle thinks (reads the flow field for its heading) every this many ticks, spread per particle.</summary>
    public int ThinkMin = 8, ThinkMax = 16;

    /// <summary>
    /// Hysteresis (flat metres to the nearest player): a particle nearer than <see cref="PromoteRange"/> becomes a full
    /// zombie (the individual tier); an individual further than <see cref="DemoteRange"/> for <see cref="DemoteSeconds"/>
    /// goes back into the crowd.
    /// </summary>
    public float PromoteRange = 22f, DemoteRange = 34f, DemoteSeconds = 2f;

    /// <summary>Most promotions and demotions a tick (the tick's cost stays flat when a wave arrives).</summary>
    public int PromotePerTick = 6, DemotePerTick = 4;

    /// <summary>The most individuals at once (the rest of the crowd waits at the promote range). At most <see cref="Game.MaxZombies"/>.</summary>
    public int IndividualCap = 300;

    /// <summary>Walking speed (m/s) and the spread either way (each particle its own, from its index).</summary>
    public float Speed = 1.5f, SpeedSpread = 0.35f;

    /// <summary>Separation: particles closer than this (m) push apart, this hard (m/s at contact), counting this many neighbours at most.</summary>
    public float SeparationRadius = 0.7f, SeparationStrength = 2.5f;

    public int SeparationMax = 8;

    /// <summary>The flow field's cell (m) and how often (ticks) it is built again (sooner when a player moves to another cell, at most every 10 ticks).</summary>
    public float FlowCell = 2f;

    public int FlowRefreshTicks = 30;
}

/// <summary>
/// The crowd tier of the mass horde (docs/zombies/MASS-HORDE.md, the spike): thousands of zombies as particles with only
/// a position and a velocity, in flat arrays (no object per zombie). One flow field over the map's floor plan (a grid
/// of <see cref="CrowdTuning.FlowCell"/> m cells, a Dijkstra from every player's cell at once) gives every particle its
/// heading; each reads it every 8-16 ticks and walks it every tick, pushed apart from its neighbours (and from the
/// individuals) through a uniform grid rebuilt each tick by a counting sort. No hit boxes, no animation state, no
/// network state: near the players the game promotes particles to full zombies (<see cref="Game.CrowdTick"/>), and
/// sends far, healthy ones back. Single floor (one height; the spike's dev map is flat). No allocation per tick once
/// the arrays have grown.
/// </summary>
public sealed class HordeCrowd
{
    public CrowdTuning Tuning = new();

    // ---- The particles (structure of arrays) ----------------------------------------------------------

    /// <summary>Positions, velocities, headings (unit, from the flow field), speeds and separation pushes, by particle index.</summary>
    public float[] X = [], Z = [], VX = [], VZ = [], DX = [], DZ = [], Spd = [], PX = [], PZ = [];

    /// <summary>The tick each particle thinks next.</summary>
    public long[] Next = [];

    /// <summary>Inside the promote range but not promoted yet (the individual cap): it holds there.</summary>
    public bool[] Held = [];

    /// <summary>Particles alive in the crowd (indices 0..Count-1).</summary>
    public int Count;

    /// <summary>The floor's height (the spike is one floor).</summary>
    public float Y;

    // ---- Statistics (self-test, log) -------------------------------------------------------------------

    public long Ticks, Thinks, Promoted, Demoted, FlowBuilds, Blocked;
    public double FlowMs, TickMs;

    /// <summary>Particles inside the promote range this tick (waiting or about to be promoted).</summary>
    public int Near;

    // ---- The flow field --------------------------------------------------------------------------------

    private float _minX, _minZ, _cell = 2f, _halfX, _halfZ;
    private int _cols, _rows;
    private bool[] _walk = [];
    private float[] _cost = [], _fdx = [], _fdz = [];
    private int[] _heapCell = new int[1024];
    private float[] _heapKey = new float[1024];
    private int _heapCount;
    private long _flowTick = -1_000_000;
    private readonly int[] _targetCell = new int[HordeController<Zombie>.MaxTargets];
    private Map? _setupMap;

    // ---- The separation grid ---------------------------------------------------------------------------

    private float _sMinX, _sMinZ, _sCell = 0.7f;
    private int _sCols, _sRows;
    private int[] _sStart = [], _sFill = [], _sItem = [];
    private float[] _sX = [], _sZ = [];

    /// <summary>Individuals' positions this tick (they push particles; set by the game before <see cref="Tick"/>).</summary>
    public float[] BlockX = new float[Game.MaxZombies], BlockZ = new float[Game.MaxZombies];

    public int BlockCount;

    // ---- Promotion candidates --------------------------------------------------------------------------

    /// <summary>This tick's nearest particles inside the promote range (indices, nearest first), at most <see cref="CrowdTuning.PromotePerTick"/>.</summary>
    public readonly int[] Candidates = new int[64];

    public int CandidateCount;
    private readonly float[] _candD = new float[64];

    /// <summary>The flow grid's size (cells) and the bytes the crowd's arrays hold (self-test: memory).</summary>
    public int FlowCells => _cols * _rows;

    public long Bytes => (long)X.Length * (9 * 4 + 8 + 1 + 4 + 8) + (long)_walk.Length * (1 + 12) + (long)_sStart.Length * 8;

    /// <summary>Sizes everything for the map (once per map: the walkable cells, by probing the floor) and for <paramref name="capacity"/> particles.</summary>
    public void Setup(Map map, float floorY, int capacity)
    {
        Reserve(capacity);
        if (ReferenceEquals(_setupMap, map) && Y == floorY)
            return;
        _setupMap = map;
        Y = floorY;
        _halfX = map.HalfX;
        _halfZ = map.HalfZ;
        _cell = MathF.Max(0.5f, Tuning.FlowCell);
        _minX = -map.HalfX;
        _minZ = -map.HalfZ;
        _cols = Math.Clamp((int)MathF.Ceiling(map.HalfX * 2 / _cell), 1, 1024);
        _rows = Math.Clamp((int)MathF.Ceiling(map.HalfZ * 2 / _cell), 1, 1024);
        var n = _cols * _rows;
        _walk = new bool[n];
        _cost = new float[n];
        _fdx = new float[n];
        _fdz = new float[n];
        for (var r = 0; r < _rows; r++)
            for (var c = 0; c < _cols; c++)
            {
                var p = new Vector3(_minX + (c + 0.5f) * _cell, floorY + 0.05f, _minZ + (r + 0.5f) * _cell);
                _walk[r * _cols + c] = !World.Blocked(p, 1.7f);
            }
        _sCell = MathF.Max(0.4f, Tuning.SeparationRadius);
        _sMinX = -map.HalfX - _sCell;
        _sMinZ = -map.HalfZ - _sCell;
        _sCols = Math.Clamp((int)MathF.Ceiling((map.HalfX * 2 + 2 * _sCell) / _sCell), 1, 2048);
        _sRows = Math.Clamp((int)MathF.Ceiling((map.HalfZ * 2 + 2 * _sCell) / _sCell), 1, 2048);
        _sStart = new int[_sCols * _sRows + 1];
        _sFill = new int[_sCols * _sRows];
        _flowTick = -1_000_000;
        Array.Fill(_targetCell, -1);
    }

    /// <summary>Set up for a map (<see cref="Setup"/>): before that there is nothing to tick.</summary>
    public bool Ready => _sStart.Length > 0;

    /// <summary>Grows the particle arrays to <paramref name="n"/> (allocates only when it grows).</summary>
    public void Reserve(int n)
    {
        if (X.Length >= n)
            return;
        Array.Resize(ref X, n);
        Array.Resize(ref Z, n);
        Array.Resize(ref VX, n);
        Array.Resize(ref VZ, n);
        Array.Resize(ref DX, n);
        Array.Resize(ref DZ, n);
        Array.Resize(ref Spd, n);
        Array.Resize(ref PX, n);
        Array.Resize(ref PZ, n);
        Array.Resize(ref Next, n);
        Array.Resize(ref Held, n);
        var m = n + Game.MaxZombies;
        _sItem = new int[m];
        _sX = new float[m];
        _sZ = new float[m];
    }

    /// <summary>Every particle gone (a new run or round).</summary>
    public void Clear()
    {
        Count = 0;
        CandidateCount = 0;
        Near = 0;
    }

    /// <summary>Is the flow cell at (x, z) walkable (outside the map: no)?</summary>
    public bool Walkable(float x, float z)
    {
        if (_walk.Length == 0)
            return true;
        var c = (int)((x - _minX) / _cell);
        var r = (int)((z - _minZ) / _cell);
        return c >= 0 && r >= 0 && c < _cols && r < _rows && _walk[r * _cols + c];
    }

    /// <summary>
    /// Adds a particle at (x, z) with velocity (vx, vz) and returns its index (-1: full). Its speed comes from
    /// <paramref name="seed"/> (a stable per-zombie number), so a zombie keeps its pace across demotion and promotion.
    /// </summary>
    public int Add(float x, float z, float vx, float vz, uint seed, long tick)
    {
        if (Count >= X.Length)
            return -1;
        var i = Count++;
        X[i] = x;
        Z[i] = z;
        VX[i] = vx;
        VZ[i] = vz;
        var l = MathF.Sqrt(vx * vx + vz * vz);
        DX[i] = l > 1e-3f ? vx / l : 0;
        DZ[i] = l > 1e-3f ? vz / l : 0;
        PX[i] = PZ[i] = 0;
        var h = Hash(seed);
        Spd[i] = Tuning.Speed * (1 + Tuning.SpeedSpread * ((h & 0xFFFF) / 32767.5f - 1));
        Next[i] = tick + 1 + (h >> 16) % (uint)Math.Max(1, Tuning.ThinkMax);
        Held[i] = false;
        return i;
    }

    /// <summary>Removes particle <paramref name="i"/> (the last one takes its index).</summary>
    public void RemoveAt(int i)
    {
        var last = --Count;
        if (i == last)
            return;
        X[i] = X[last];
        Z[i] = Z[last];
        VX[i] = VX[last];
        VZ[i] = VZ[last];
        DX[i] = DX[last];
        DZ[i] = DZ[last];
        Spd[i] = Spd[last];
        PX[i] = PX[last];
        PZ[i] = PZ[last];
        Next[i] = Next[last];
        Held[i] = Held[last];
    }

    public static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352d;
        x ^= x >> 15;
        x *= 0x846ca68b;
        x ^= x >> 16;
        return x;
    }

    /// <summary>
    /// One tick: the flow field when it is due, each particle's think when due, separation (half the particles a tick,
    /// alternating), the walk with sliding along blocked cells, and this tick's promotion candidates (the nearest inside
    /// the promote range). <paramref name="tx"/>/<paramref name="tz"/>: the players standing (at most 8).
    /// <paramref name="canPromote"/>: how many the game can take this tick (0: the cap is full, everyone near holds).
    /// </summary>
    public void Tick(long tick, ReadOnlySpan<float> tx, ReadOnlySpan<float> tz, int canPromote)
    {
        var t0 = Stopwatch.GetTimestamp();
        Ticks++;
        var targets = Math.Min(tx.Length, _targetCell.Length);
        // The flow field: due, or a player has moved to another cell.
        var moved = false;
        for (var t = 0; t < targets; t++)
            if (CellOf(tx[t], tz[t]) != _targetCell[t])
                moved = true;
        for (var t = targets; t < _targetCell.Length; t++)
            if (_targetCell[t] != -1)
                moved = true;
        if (targets > 0 && (tick - _flowTick >= Tuning.FlowRefreshTicks || (moved && tick - _flowTick >= 10)))
            BuildFlow(tick, tx[..targets], tz[..targets]);

        BuildSeparation();

        var dt = World.TickDt;
        var pr2 = Tuning.PromoteRange * Tuning.PromoteRange;
        var want = Math.Min(Math.Min(canPromote, Tuning.PromotePerTick), Candidates.Length);
        CandidateCount = 0;
        Near = 0;
        var parity = (int)(tick & 1);
        var sepR = Tuning.SeparationRadius;
        var sepS = Tuning.SeparationStrength;
        var thinkSpan = (uint)Math.Max(1, Tuning.ThinkMax - Tuning.ThinkMin + 1);
        for (var i = 0; i < Count; i++)
        {
            var x = X[i];
            var z = Z[i];
            // The nearest player (flat).
            var best = float.MaxValue;
            var bt = -1;
            for (var t = 0; t < targets; t++)
            {
                var ddx = tx[t] - x;
                var ddz = tz[t] - z;
                var d = ddx * ddx + ddz * ddz;
                if (d < best)
                {
                    best = d;
                    bt = t;
                }
            }
            if (tick >= Next[i])
            {
                Thinks++;
                Next[i] = tick + Tuning.ThinkMin + Hash((uint)i * 2654435761u ^ (uint)tick) % thinkSpan;
                Think(i, x, z, bt >= 0 ? tx[bt] : x, bt >= 0 ? tz[bt] : z, best);
            }
            if (best < pr2)
            {
                Near++;
                if (want > 0)
                    Offer(i, best, want);
                Held[i] = true;
            }
            else
                Held[i] = false;
            if ((i & 1) == parity)
                Separate(i, x, z, sepR, sepS);
            var spd = Held[i] ? 0f : Spd[i];
            var wx = DX[i] * spd + PX[i];
            var wz = DZ[i] * spd + PZ[i];
            // Ease towards the wanted velocity (a quarter a tick: about 0.07 s to turn).
            var vx = VX[i] + (wx - VX[i]) * 0.25f;
            var vz = VZ[i] + (wz - VZ[i]) * 0.25f;
            var nx = x + vx * dt;
            var nz = z + vz * dt;
            if (!Walkable(nx, nz))
            {
                if (Walkable(nx, z))
                {
                    nz = z;
                    vz = 0;
                }
                else if (Walkable(x, nz))
                {
                    nx = x;
                    vx = 0;
                }
                else
                {
                    nx = x;
                    nz = z;
                    vx = vz = 0;
                    Blocked++;
                }
            }
            X[i] = Math.Clamp(nx, -_halfX + 0.3f, _halfX - 0.3f);
            Z[i] = Math.Clamp(nz, -_halfZ + 0.3f, _halfZ - 0.3f);
            VX[i] = vx;
            VZ[i] = vz;
        }
        TickMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>Keeps the <paramref name="want"/> nearest candidates (insertion, nearest first; ties by index).</summary>
    private void Offer(int i, float d, int want)
    {
        var n = CandidateCount;
        if (n == want && d >= _candD[n - 1])
            return;
        var k = n < want ? n++ : n - 1;
        while (k > 0 && _candD[k - 1] > d)
        {
            _candD[k] = _candD[k - 1];
            Candidates[k] = Candidates[k - 1];
            k--;
        }
        _candD[k] = d;
        Candidates[k] = i;
        CandidateCount = n;
    }

    /// <summary>A particle's think: its heading from the flow field (near the player, or off the field: straight at them).</summary>
    private void Think(int i, float x, float z, float px, float pz, float d2)
    {
        var c = CellOf(x, z);
        float hx, hz;
        // The flow field when the way round is longer than the straight line could be on the grid (octile distance is at
        // most 8% over the straight one, plus a cell): something is in the way. Otherwise straight at the player (no
        // lanes along the grid's eight directions in the open).
        if (c >= 0 && _cost.Length > c && !float.IsPositiveInfinity(_cost[c]) && d2 > _cell * _cell * 4 && (_fdx[c] != 0 || _fdz[c] != 0)
            && _cost[c] * _cell > MathF.Sqrt(d2) * 1.09f + _cell)
        {
            hx = _fdx[c];
            hz = _fdz[c];
        }
        else
        {
            hx = px - x;
            hz = pz - z;
            var l = MathF.Sqrt(hx * hx + hz * hz);
            if (l < 1e-3f)
                hx = hz = 0;
            else
            {
                hx /= l;
                hz /= l;
            }
        }
        DX[i] = hx;
        DZ[i] = hz;
    }

    private int CellOf(float x, float z)
    {
        var c = (int)((x - _minX) / _cell);
        var r = (int)((z - _minZ) / _cell);
        return c < 0 || r < 0 || c >= _cols || r >= _rows ? -1 : r * _cols + c;
    }

    // ---- Flow field -------------------------------------------------------------------------------------

    private void BuildFlow(long tick, ReadOnlySpan<float> tx, ReadOnlySpan<float> tz)
    {
        var t0 = Stopwatch.GetTimestamp();
        _flowTick = tick;
        FlowBuilds++;
        var n = _cols * _rows;
        Array.Fill(_cost, float.PositiveInfinity, 0, n);
        Array.Fill(_targetCell, -1);
        _heapCount = 0;
        for (var t = 0; t < tx.Length; t++)
        {
            var c = CellOf(tx[t], tz[t]);
            _targetCell[t] = c;
            if (c < 0 || _cost[c] == 0)
                continue;
            _cost[c] = 0;
            Push(c, 0);
        }
        var diag = 1.41421356f;
        while (_heapCount > 0)
        {
            var (v, key) = Pop();
            if (key > _cost[v])
                continue;
            var vc = v % _cols;
            var vr = v / _cols;
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0)
                        continue;
                    int uc = vc + dc, ur = vr + dr;
                    if (uc < 0 || ur < 0 || uc >= _cols || ur >= _rows)
                        continue;
                    var u = ur * _cols + uc;
                    if (!_walk[u])
                        continue;
                    if (dr != 0 && dc != 0 && (!_walk[vr * _cols + uc] || !_walk[ur * _cols + vc]))
                        continue; // no cutting a corner
                    var k = key + (dr != 0 && dc != 0 ? diag : 1f);
                    if (k >= _cost[u])
                        continue;
                    _cost[u] = k;
                    Push(u, k);
                }
        }
        // Each cell's heading: towards its cheapest neighbour.
        for (var r = 0; r < _rows; r++)
            for (var c = 0; c < _cols; c++)
            {
                var i = r * _cols + c;
                _fdx[i] = _fdz[i] = 0;
                var own = _cost[i];
                if (float.IsPositiveInfinity(own) || own == 0)
                    continue;
                var best = own;
                int bc = 0, br = 0;
                for (var dr = -1; dr <= 1; dr++)
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        int uc = c + dc, ur = r + dr;
                        if ((dr == 0 && dc == 0) || uc < 0 || ur < 0 || uc >= _cols || ur >= _rows)
                            continue;
                        if (dr != 0 && dc != 0 && (!_walk[r * _cols + uc] || !_walk[ur * _cols + c]))
                            continue;
                        var k = _cost[ur * _cols + uc];
                        if (k < best)
                        {
                            best = k;
                            bc = dc;
                            br = dr;
                        }
                    }
                // In the open (all four neighbours reachable): down the cost's slope (central differences), so the
                // crowd fans out straight at the players instead of filing along the grid's eight directions; next to
                // a wall: to the cheapest neighbour.
                if (c > 0 && r > 0 && c < _cols - 1 && r < _rows - 1)
                {
                    float cl = _cost[i - 1], cr = _cost[i + 1], cu = _cost[i - _cols], cd = _cost[i + _cols];
                    if (!float.IsPositiveInfinity(cl) && !float.IsPositiveInfinity(cr) && !float.IsPositiveInfinity(cu) && !float.IsPositiveInfinity(cd))
                    {
                        var gx = cl - cr;
                        var gz = cu - cd;
                        var gl = MathF.Sqrt(gx * gx + gz * gz);
                        if (gl > 1e-4f && gx * bc + gz * br > 0)
                        {
                            _fdx[i] = gx / gl;
                            _fdz[i] = gz / gl;
                            continue;
                        }
                    }
                }
                var l = bc != 0 && br != 0 ? 0.70710678f : 1f;
                _fdx[i] = bc * l;
                _fdz[i] = br * l;
            }
        FlowMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    private void Push(int cell, float key)
    {
        if (_heapCount == _heapCell.Length)
        {
            Array.Resize(ref _heapCell, _heapCell.Length * 2);
            Array.Resize(ref _heapKey, _heapKey.Length * 2);
        }
        var i = _heapCount++;
        while (i > 0)
        {
            var p = (i - 1) >> 1;
            if (_heapKey[p] <= key)
                break;
            _heapCell[i] = _heapCell[p];
            _heapKey[i] = _heapKey[p];
            i = p;
        }
        _heapCell[i] = cell;
        _heapKey[i] = key;
    }

    private (int Cell, float Key) Pop()
    {
        var top = (_heapCell[0], _heapKey[0]);
        var last = --_heapCount;
        if (last > 0)
        {
            var cell = _heapCell[last];
            var key = _heapKey[last];
            var i = 0;
            while (true)
            {
                var l = i * 2 + 1;
                if (l >= last)
                    break;
                var r = l + 1;
                var m = r < last && _heapKey[r] < _heapKey[l] ? r : l;
                if (_heapKey[m] >= key)
                    break;
                _heapCell[i] = _heapCell[m];
                _heapKey[i] = _heapKey[m];
                i = m;
            }
            _heapCell[i] = cell;
            _heapKey[i] = key;
        }
        return top;
    }

    /// <summary>The flow field's cost (cells) at (x, z) (self-test; infinity: unreachable or no field yet).</summary>
    public float FlowCost(float x, float z)
    {
        var c = CellOf(x, z);
        return c < 0 || c >= _cost.Length ? float.PositiveInfinity : _cost[c];
    }

    // ---- Separation -------------------------------------------------------------------------------------

    private int SCell(float x, float z)
    {
        var c = Math.Clamp((int)((x - _sMinX) / _sCell), 0, _sCols - 1);
        var r = Math.Clamp((int)((z - _sMinZ) / _sCell), 0, _sRows - 1);
        return r * _sCols + c;
    }

    /// <summary>The counting sort: particles then individuals into the separation grid (items past <see cref="Count"/> are individuals).</summary>
    private void BuildSeparation()
    {
        var cells = _sCols * _sRows;
        var total = Count + Math.Min(BlockCount, BlockX.Length);
        Array.Clear(_sStart, 0, cells + 1);
        for (var i = 0; i < total; i++)
        {
            var (x, z) = i < Count ? (X[i], Z[i]) : (BlockX[i - Count], BlockZ[i - Count]);
            _sStart[SCell(x, z) + 1]++;
        }
        for (var c = 0; c < cells; c++)
            _sStart[c + 1] += _sStart[c];
        Array.Copy(_sStart, _sFill, cells);
        for (var i = 0; i < total; i++)
        {
            var (x, z) = i < Count ? (X[i], Z[i]) : (BlockX[i - Count], BlockZ[i - Count]);
            var k = _sFill[SCell(x, z)]++;
            _sItem[k] = i;
            _sX[k] = x;
            _sZ[k] = z;
        }
    }

    private void Separate(int i, float x, float z, float r, float strength)
    {
        var c0 = Math.Clamp((int)((x - r - _sMinX) / _sCell), 0, _sCols - 1);
        var c1 = Math.Clamp((int)((x + r - _sMinX) / _sCell), 0, _sCols - 1);
        var r0 = Math.Clamp((int)((z - r - _sMinZ) / _sCell), 0, _sRows - 1);
        var r1 = Math.Clamp((int)((z + r - _sMinZ) / _sCell), 0, _sRows - 1);
        float px = 0, pz = 0;
        var seen = 0;
        var r2 = r * r;
        var max = Tuning.SeparationMax;
        for (var cz = r0; cz <= r1 && seen < max; cz++)
            for (var cx = c0; cx <= c1 && seen < max; cx++)
            {
                var cell = cz * _sCols + cx;
                for (var k = _sStart[cell]; k < _sStart[cell + 1]; k++)
                {
                    if (_sItem[k] == i)
                        continue;
                    var dx = x - _sX[k];
                    var dz = z - _sZ[k];
                    var l2 = dx * dx + dz * dz;
                    if (l2 > r2)
                        continue;
                    if (l2 < 1e-6f)
                    {
                        // Exactly on top of each other: split by index.
                        dx = (i & 1) == 0 ? 0.01f : -0.01f;
                        dz = (i & 2) == 0 ? 0.01f : -0.01f;
                        l2 = 2e-4f;
                    }
                    var l = MathF.Sqrt(l2);
                    var f = (r - l) / r * strength / l;
                    // An individual pushes twice as hard (bodies with collision; particles give way).
                    if (_sItem[k] >= Count)
                        f *= 2;
                    px += dx * f;
                    pz += dz * f;
                    if (++seen >= max)
                        break;
                }
            }
        PX[i] = px;
        PZ[i] = pz;
    }

    // ---- The network sketch -----------------------------------------------------------------------------

    /// <summary>
    /// Counts particles per square cell of <paramref name="cell"/> m into <paramref name="into"/> (cols x rows of the map;
    /// sized by the caller from <see cref="DensitySize"/>). The network sketch sends these as per-cell deltas.
    /// </summary>
    public void Density(float cell, Span<ushort> into)
    {
        var (cols, rows) = DensitySize(cell);
        into[..(cols * rows)].Clear();
        for (var i = 0; i < Count; i++)
        {
            var c = Math.Clamp((int)((X[i] + _halfX) / cell), 0, cols - 1);
            var r = Math.Clamp((int)((Z[i] + _halfZ) / cell), 0, rows - 1);
            ref var v = ref into[r * cols + c];
            if (v < ushort.MaxValue)
                v++;
        }
    }

    public (int Cols, int Rows) DensitySize(float cell) => (Math.Max(1, (int)MathF.Ceiling(_halfX * 2 / cell)), Math.Max(1, (int)MathF.Ceiling(_halfZ * 2 / cell)));

    /// <summary>Order-dependent digest of every particle (self-test: determinism).</summary>
    public ulong Digest()
    {
        var h = 1469598103934665603UL;
        for (var i = 0; i < Count; i++)
        {
            h = (h ^ BitConverter.SingleToUInt32Bits(X[i])) * 1099511628211UL;
            h = (h ^ BitConverter.SingleToUInt32Bits(Z[i])) * 1099511628211UL;
        }
        return h;
    }
}
