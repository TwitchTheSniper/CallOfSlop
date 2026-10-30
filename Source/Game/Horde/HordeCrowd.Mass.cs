using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The mass tier (docs/zombies/MASS-HORDE.md, "Mass"): far from every player the crowd needs no particles at all. Per
/// cell of <see cref="CrowdTuning.NetCell"/> m (the network's grid) only a count of zombies and their mean velocity,
/// moved along the flow field like a fluid (a share of each cell's count flows to the neighbour downstream every tick,
/// held back where the next cell is full). Particles further than <see cref="CrowdTuning.MassRange"/> +
/// <see cref="CrowdTuning.MassHysteresis"/> from everyone merge into their cell; cells within
/// <see cref="CrowdTuning.MassRange"/> of a player let theirs out as particles again; at most
/// <see cref="CrowdTuning.MassPerTick"/> each way a tick. Only an unhurt particle merges; a mass zombie hurt by a shot or a
/// blast keeps its health in the wound pool (HordeCrowd.MassHits.cs), every other one has <see cref="MassHP"/>. <see cref="Total"/> = particles + mass, always.
/// <para>
/// Zombie types (feat/zombie-types): the mass holds one count a cell. When types reach the mass, each cell takes a count
/// per type (a second index: cell * kinds + kind) at the four places a count moves: <see cref="TickMass"/>'s merge
/// (the particle's type), flux (each type's share) and release (the type the particle gets back), and
/// <see cref="KillMass"/> (the kill's type for the credit). Nothing else reads the counts by type.
/// </para>
/// </summary>
public sealed partial class HordeCrowd
{
    /// <summary>Zombies in each mass cell, and the cells' velocities (m/s).</summary>
    public int[] MassCount = [];

    public float[] MassVX = [], MassVZ = [];

    /// <summary>Zombies in the mass tier in all.</summary>
    public int MassTotal;

    /// <summary>A mass zombie's health (the round's).</summary>
    public float MassHP = 100f;

    /// <summary>Particles and mass together: every crowd zombie.</summary>
    public int Total => Count + MassTotal;

    /// <summary>Merged into the mass and let out of it (log, self-test).</summary>
    public long Merged, Released;

    private int _mCols, _mRows;
    private float _mCell;
    private bool[] _mWalk = [];
    private int[] _mNext = [];
    private float[] _mFlux = [];
    private readonly int[] _mergeCand = new int[256];
    private readonly float[] _mergeD = new float[256];
    private int _mergeN;

    // The soft wake's release choice this tick: the cells nearest first (HordeCrowd.Mass.cs, ReleaseSoft).
    private readonly int[] _relCell = new int[256];
    private readonly float[] _relKey = new float[256], _relUX = new float[256], _relUZ = new float[256];
    private int _relN;
    private uint _mRng = 0x6A55u;

    /// <summary>
    /// Visual only: the latest merges into the mass (kind 1, with the particle's look) and releases out of it (kind 2), a
    /// ring of <see cref="NoteCap"/>, for the host's drawing (CrowdAgents.Hint / Release). <see cref="Notes"/> counts every
    /// one ever written; a reader keeps its own count and reads what it hasn't seen (at most the ring's worth).
    /// </summary>
    public const int NoteCap = 512;

    public readonly float[] NoteX = new float[NoteCap], NoteZ = new float[NoteCap];
    public readonly uint[] NoteLook = new uint[NoteCap];
    public readonly byte[] NoteKind = new byte[NoteCap];

    /// <summary>A merge's yaw, metres walked and gait as it was drawn (NaN yaw: none; the agent takes the cell's).</summary>
    public readonly float[] NoteYaw = new float[NoteCap], NoteWalk = new float[NoteCap];

    public readonly byte[] NoteGait = new byte[NoteCap];
    public long Notes;

    private void Note(byte kind, float x, float z, uint look, float yaw = float.NaN, float walk = 0, byte gait = 0)
    {
        var k = (int)(Notes & (NoteCap - 1));
        NoteKind[k] = kind;
        NoteX[k] = x;
        NoteZ[k] = z;
        NoteLook[k] = look;
        NoteYaw[k] = yaw;
        NoteWalk[k] = walk;
        NoteGait[k] = gait;
        Notes++;
    }

    /// <summary>
    /// The soft wake (<see cref="CrowdTuning.SoftWake"/>): mass cell <paramref name="c"/>'s own wake radius, the mass range
    /// +- half the band, fixed per cell (a hash of the cell), so the wake edge is ragged rather than a circle sweeping
    /// over a blob. Its merge radius is this plus <see cref="CrowdTuning.MassHysteresis"/>.
    /// </summary>
    public float WakeRadius(int c)
    {
        if (!Tuning.SoftWake || Tuning.MassBand <= 0)
            return Tuning.MassRange;
        var u = Hash((uint)c * 0x9E3779B1u ^ 0x57A6E5u) / 4294967295f;
        return Tuning.MassRange + (u - 0.5f) * Tuning.MassBand;
    }

    /// <summary>Visual only: how much of particle <paramref name="i"/>'s hand-off offset is left (1 at the hand-off, eased to 0).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public float HandWeight(int i)
    {
        var left = HandLeft[i];
        if (left <= 0)
            return 0;
        var t = MathF.Min(1f, left / MathF.Max(HandDur[i], 1e-3f));
        return t * t * (3f - 2f * t); // (smooth at both ends: no kick as it starts or stops)
    }

    /// <summary>Visual only: where particle <paramref name="i"/> is drawn (its position plus what is left of its hand-off offset).</summary>
    public void DrawnAt(int i, out float x, out float z)
    {
        var w = HandWeight(i);
        x = X[i] + HandX[i] * w;
        z = Z[i] + HandZ[i] * w;
    }

    /// <summary>Visual only: the yaw particle <paramref name="i"/> is drawn facing (its walk's, turned from the hand-off's while that lasts).</summary>
    public float DrawnYaw(int i)
    {
        float vx = VX[i], vz = VZ[i];
        var yaw = vx * vx + vz * vz > 0.04f ? MathF.Atan2(vx, -vz) : MathF.Atan2(DX[i], -DZ[i]);
        var w = HandWeight(i);
        if (w <= 0)
            return yaw;
        var dy = HandYaw[i] - yaw;
        dy -= MathF.Floor((dy + MathF.PI) / MathF.Tau) * MathF.Tau;
        return yaw + dy * w;
    }

    public int MassCols => _mCols;

    public int MassRows => _mRows;

    public float MassCell => _mCell;

    /// <summary>Sizes the mass grid for the map (with <see cref="Setup"/>): its cells walkable where any flow cell in them is.</summary>
    private void SetupMass()
    {
        _mCell = MathF.Max(1f, Tuning.NetCell);
        (_mCols, _mRows) = DensitySize(_mCell);
        var n = _mCols * _mRows;
        MassCount = new int[n];
        MassVX = new float[n];
        MassVZ = new float[n];
        _mNext = new int[n];
        _mFlux = new float[n];
        _mWalk = new bool[n];
        SetupWounds(n); // (the mass's wounded: HordeCrowd.MassHits.cs)
        Sight.Setup(_terrain, MinX, MinZ, _mCell, _mCols, _mRows); // (what the players see: CrowdSight.cs)
        for (var r = 0; r < _mRows; r++)
            for (var c = 0; c < _mCols; c++)
            {
                var any = false;
                for (var sz = 0.25f; sz < 1f && !any; sz += 0.5f)
                    for (var sx = 0.25f; sx < 1f && !any; sx += 0.5f)
                        any = Walkable(MinX + (c + sx) * _mCell, MinZ + (r + sz) * _mCell);
                _mWalk[r * _mCols + c] = any;
            }
        MassTotal = 0;
    }

    private void ClearMass()
    {
        Array.Clear(MassCount);
        Array.Clear(MassVX);
        Array.Clear(MassVZ);
        Array.Clear(_mFlux);
        ClearWounds();
        MassTotal = 0;
        _mergeN = 0;
    }

    private int MassCellOf(float x, float z) =>
        Math.Clamp((int)((z - MinZ) / _mCell), 0, _mRows - 1) * _mCols + Math.Clamp((int)((x - MinX) / _mCell), 0, _mCols - 1);

    /// <summary>Puts one zombie in the mass at (x, z) (seeding, merging).</summary>
    public void AddMass(float x, float z)
    {
        if (MassCount.Length == 0)
            return;
        MassCount[MassCellOf(x, z)]++;
        MassTotal++;
    }

    /// <summary>
    /// In the particle loop: a far, unhurt, walking particle may merge this tick. The old way: the first few found, from a
    /// turning start. The soft wake: the furthest (<paramref name="d2"/>, its squared distance) kept, so the far side of
    /// the crowd goes back first and the edge isn't eaten in index order.
    /// </summary>
    private void OfferMerge(int i, float d2 = 0)
    {
        var want = Math.Min(_mergeCand.Length, Tuning.MassPerTick);
        if (!Tuning.SoftWake)
        {
            if (_mergeN < want)
                _mergeCand[_mergeN++] = i;
            return;
        }
        var n = _mergeN;
        if (n == want && d2 <= _mergeD[n - 1])
            return;
        var k = n < want ? n++ : n - 1;
        while (k > 0 && _mergeD[k - 1] < d2)
        {
            _mergeD[k] = _mergeD[k - 1];
            _mergeCand[k] = _mergeCand[k - 1];
            k--;
        }
        _mergeD[k] = d2;
        _mergeCand[k] = i;
        _mergeN = n;
    }

    /// <summary>
    /// After the particle loop: merge this tick's far particles (highest index first), move the mass along the flow
    /// field, and let particles out of cells near a player. <paramref name="tx"/>/<paramref name="tz"/>: the players;
    /// <paramref name="fx"/>/<paramref name="fz"/>: what the flow leads to (the players, or the goal). Held: no flow.
    /// </summary>
    private void TickMass(long tick, ReadOnlySpan<float> tx, ReadOnlySpan<float> tz, ReadOnlySpan<float> fx, ReadOnlySpan<float> fz)
    {
        if (!Tuning.Mass || MassCount.Length == 0)
        {
            _mergeN = 0;
            return;
        }
        // Merge (removed from the highest index, so the others keep theirs: the soft wake's were kept furthest first).
        if (Tuning.SoftWake && _mergeN > 1)
            Array.Sort(_mergeCand, 0, _mergeN);
        for (var k = _mergeN - 1; k >= 0; k--)
        {
            var i = _mergeCand[k];
            if (i >= Count)
                continue;
            var c = MassCellOf(X[i], Z[i]);
            // The cell's velocity: a running mean of what came in.
            var w = 1f / (MassCount[c] + 1);
            MassVX[c] += (VX[i] - MassVX[c]) * w;
            MassVZ[c] += (VZ[i] - MassVZ[c]) * w;
            MassCount[c]++;
            MassTotal++;
            Merged++;
            if (Tuning.SoftWake)
            {
                // The drawing hands it to an agent exactly as it was drawn: place, yaw, walk and look (no fade).
                DrawnAt(i, out var dx, out var dz);
                Note(1, dx, dz, DrawLook[i], DrawnYaw(i), Walk[i], Cls[i]);
            }
            else
                Note(1, X[i], Z[i], Look[i]);
            RemoveAt(i);
        }
        _mergeN = 0;
        if (MassTotal == 0)
            return;

        if (Hold)
            goto Release;
        // Flow: each cell sends a share of its zombies one cell downstream (the flow field's heading at its centre), as far
        // as the next cell has room. Worked out from this tick's counts into the next (no order bias).
        var n = _mCols * _mRows;
        Array.Copy(MassCount, _mNext, n);
        var dt = World.TickDt;
        var cap = Tuning.MassCapacity;
        // The round's mean pace (CrowdPace: the individuals' speed mix). At the fastest a rules file allows (12 m/s) a cell
        // sends a twentieth of its count a tick, and only ever one cell on: nothing jumps a cell.
        var speed = Pace.Mean;
        for (var c = 0; c < n; c++)
        {
            var m = MassCount[c];
            if (m == 0)
            {
                _mFlux[c] = 0;
                continue;
            }
            var cx = MinX + (c % _mCols + 0.5f) * _mCell;
            var cz = MinZ + (c / _mCols + 0.5f) * _mCell;
            // Heading: the flow field, else straight at the nearest player.
            float hx = 0, hz = 0;
            var fc = CellOf(cx, cz);
            if (fc >= 0 && !float.IsPositiveInfinity(_cost[fc]) && (_fdx[fc] != 0 || _fdz[fc] != 0))
            {
                hx = _fdx[fc];
                hz = _fdz[fc];
            }
            else
            {
                var best = float.MaxValue;
                for (var t = 0; t < fx.Length; t++)
                {
                    var d = (fx[t] - cx) * (fx[t] - cx) + (fz[t] - cz) * (fz[t] - cz);
                    if (d < best && d > 1e-4f)
                    {
                        best = d;
                        var l = MathF.Sqrt(d);
                        hx = (fx[t] - cx) / l;
                        hz = (fz[t] - cz) / l;
                    }
                }
                if (best < _mCell * _mCell * 0.5f)
                    hx = hz = 0; // (the target is in this cell: they're there)
            }
            MassVX[c] = hx * speed;
            MassVZ[c] = hz * speed;
            int dc = MathF.Abs(hx) > 0.38f ? Math.Sign(hx) : 0, dr = MathF.Abs(hz) > 0.38f ? Math.Sign(hz) : 0;
            if (dc == 0 && dr == 0)
                continue;
            // A cell's zombies cross it in cell / speed seconds: that share of them a tick.
            _mFlux[c] += m * speed * dt / (_mCell * (dc != 0 && dr != 0 ? 1.41421356f : 1f));
            if ((int)_mFlux[c] <= 0)
                continue;
            // Downstream: the heading's neighbour; when that one is full (or a wall), the neighbour 45 degrees either side
            // (the side nearer the heading first) if it is further down the field, so a horde spreads round a jam and
            // packs like a fluid instead of queueing in single file behind it.
            var u = MassDownstream(c, dc, dr, cap);
            if (u < 0)
            {
                var ring = RingIndex(dc, dr);
                int ra = (ring + 1) & 7, rb = (ring + 7) & 7;
                if (RingX[ra] * hx + RingZ[ra] * hz < RingX[rb] * hx + RingZ[rb] * hz)
                    (ra, rb) = (rb, ra);
                var own = fc >= 0 ? _cost[fc] : float.PositiveInfinity;
                u = MassSideways(c, RingX[ra], RingZ[ra], cap, own);
                if (u < 0)
                    u = MassSideways(c, RingX[rb], RingZ[rb], cap, own);
            }
            var move = u < 0 ? 0 : Math.Min((int)_mFlux[c], Math.Min(_mNext[c], Math.Max(0, cap - _mNext[u])));
            if (move <= 0)
            {
                _mFlux[c] = MathF.Min(_mFlux[c], 1.5f); // (held back: no backlog building up)
                continue;
            }
            _mFlux[c] -= move;
            if (_wHead[c] >= 0)
                MoveWounds(c, _mNext[c], move, u, _mNext[u]); // (the top slots go downstream, wounds and all)
            _mNext[c] -= move;
            _mNext[u] += move;
        }
        Array.Copy(_mNext, MassCount, n);

        Release:
        if (Tuning.SoftWake)
        {
            ReleaseSoft(tick, tx, tz);
            return;
        }
        // Release: cells within the mass range of a player let zombies out as particles (nearest cells first would cost a
        // sort; in index order from a turning start instead, a few a tick).
        n = _mCols * _mRows;
        var r2 = Tuning.MassRange * Tuning.MassRange;
        var left = Tuning.MassPerTick;
        var start = (int)(tick % n);
        for (var j = 0; j < n && left > 0; j++)
        {
            var c = (start + j) % n;
            if (MassCount[c] == 0)
                continue;
            var cx = MinX + (c % _mCols + 0.5f) * _mCell;
            var cz = MinZ + (c / _mCols + 0.5f) * _mCell;
            var near = false;
            for (var t = 0; t < tx.Length && !near; t++)
                near = (tx[t] - cx) * (tx[t] - cx) + (tz[t] - cz) * (tz[t] - cz) < r2;
            if (!near)
                continue;
            while (MassCount[c] > 0 && left > 0 && Count < X.Length)
            {
                // A free spot in the cell (a few tries; the cell is walkable somewhere).
                float x = cx, z = cz;
                for (var tries = 0; tries < 6; tries++)
                {
                    _mRng = Hash(_mRng + 0x9E3779B9u);
                    x = cx + ((_mRng & 0xFFFF) / 65535f - 0.5f) * _mCell * 0.9f;
                    z = cz + ((_mRng >> 16) / 65535f - 0.5f) * _mCell * 0.9f;
                    if (Walkable(x, z) && !Packed(x, z))
                        break;
                }
                if (!Walkable(x, z) || Packed(x, z))
                    break; // (no room in the cell: they stay in the mass for now)
                var p = Add(x, z, MassVX[c], MassVZ[c], _mRng, tick, TakeTop(c, MassCount[c])); // (a wounded one comes out hurt)
                if (p >= 0)
                    Born[p] = tick;
                Note(2, x, z, 0);
                MassCount[c]--;
                MassTotal--;
                Released++;
                left--;
            }
        }
    }

    /// <summary>
    /// The soft wake's release (docs/zombies/MASS-HORDE.md, "Wake waves"). Every mass cell inside its own wake radius
    /// (<see cref="WakeRadius"/>) of a player is a candidate, keyed by its distance to the nearest player, plus
    /// <see cref="CrowdTuning.MassInterior"/> if the cell one step towards that player still holds mass (the inside of a
    /// blob, hidden behind its edge), plus up to 2 m of jitter a tick (so neighbouring cells take turns). The
    /// <see cref="CrowdTuning.MassPerTick"/> lowest keys let out at most <see cref="CrowdTuning.MassPerCell"/> each, in key
    /// order, until the tick's budget is spent: the edge facing the players wakes first, a cell drains over several ticks,
    /// and the inside follows as the mass flows into the cells the edge left. Only the window of cells round the players
    /// is looked at; no allocation.
    /// </summary>
    private void ReleaseSoft(long tick, ReadOnlySpan<float> tx, ReadOnlySpan<float> tz)
    {
        if (tx.Length == 0 || MassTotal == 0)
            return;
        var rmax = Tuning.MassRange + MathF.Max(0f, Tuning.MassBand) * 0.5f;
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        for (var t = 0; t < tx.Length; t++)
        {
            x0 = MathF.Min(x0, tx[t] - rmax);
            x1 = MathF.Max(x1, tx[t] + rmax);
            z0 = MathF.Min(z0, tz[t] - rmax);
            z1 = MathF.Max(z1, tz[t] + rmax);
        }
        int c0 = Math.Max(0, (int)((x0 - MinX) / _mCell)), c1 = Math.Min(_mCols - 1, (int)((x1 - MinX) / _mCell));
        int r0 = Math.Max(0, (int)((z0 - MinZ) / _mCell)), r1 = Math.Min(_mRows - 1, (int)((z1 - MinZ) / _mCell));
        var want = Math.Min(_relCell.Length, Math.Max(1, Tuning.MassPerTick));
        var rmax2 = rmax * rmax;
        var interior = MathF.Max(0f, Tuning.MassInterior);
        _relN = 0;
        for (var r = r0; r <= r1; r++)
            for (var col = c0; col <= c1; col++)
            {
                var c = r * _mCols + col;
                if (MassCount[c] == 0)
                    continue;
                var cx = MinX + (col + 0.5f) * _mCell;
                var cz = MinZ + (r + 0.5f) * _mCell;
                var best = float.MaxValue;
                float bx = 0, bz = 0;
                for (var t = 0; t < tx.Length; t++)
                {
                    float ddx = tx[t] - cx, ddz = tz[t] - cz;
                    var d2 = ddx * ddx + ddz * ddz;
                    if (d2 < best)
                        (best, bx, bz) = (d2, ddx, ddz);
                }
                if (best >= rmax2)
                    continue;
                var d = MathF.Sqrt(best);
                if (d >= WakeRadius(c))
                    continue;
                // The edge facing the player: the cell one step towards them is off the grid, a wall, or holds no mass.
                var front = true;
                if (d > _mCell)
                {
                    int dc = MathF.Abs(bx) > 0.38f * d ? Math.Sign(bx) : 0, dr = MathF.Abs(bz) > 0.38f * d ? Math.Sign(bz) : 0;
                    int ux = col + dc, uz = r + dr;
                    if ((uint)ux < (uint)_mCols && (uint)uz < (uint)_mRows)
                    {
                        var u = uz * _mCols + ux;
                        front = !_mWalk[u] || MassCount[u] == 0;
                    }
                }
                var key = d + (front ? 0f : interior) + (Hash((uint)c * 0x85EBCA77u ^ (uint)tick * 0xC2B2AE3Du) & 1023) * (2f / 1023f);
                // Keep the want lowest keys (insertion, lowest first).
                var n = _relN;
                if (n == want && key >= _relKey[n - 1])
                    continue;
                var k = n < want ? n++ : n - 1;
                while (k > 0 && _relKey[k - 1] > key)
                {
                    _relKey[k] = _relKey[k - 1];
                    _relCell[k] = _relCell[k - 1];
                    _relUX[k] = _relUX[k - 1];
                    _relUZ[k] = _relUZ[k - 1];
                    k--;
                }
                _relKey[k] = key;
                _relCell[k] = c;
                _relUX[k] = d > 1e-3f ? bx / d : 0;
                _relUZ[k] = d > 1e-3f ? bz / d : 0;
                _relN = n;
            }
        var left = Tuning.MassPerTick;
        var per = Math.Max(1, Tuning.MassPerCell);
        for (var k = 0; k < _relN && left > 0; k++)
        {
            var c = _relCell[k];
            float cx = MinX + (c % _mCols + 0.5f) * _mCell, cz = MinZ + (c / _mCols + 0.5f) * _mCell;
            // (Out of the cell's back half, away from the player: where the drawn crowd, trailing the counts a little at a
            // moving front, stands; the hand-over then has least to make up.)
            cx -= _relUX[k] * _mCell * 0.2f;
            cz -= _relUZ[k] * _mCell * 0.2f;
            for (var j = 0; j < per && MassCount[c] > 0 && left > 0 && Count < X.Length; j++)
            {
                if (!FreeSpot(cx, cz, out var x, out var z))
                    break; // (no room in the cell: they stay in the mass for now)
                var p = Add(x, z, MassVX[c], MassVZ[c], _mRng, tick, TakeTop(c, MassCount[c])); // (a wounded one comes out hurt)
                if (p >= 0)
                {
                    Born[p] = tick;
                    HandLeft[p] = -1; // (the drawing hands the nearest agent's place, yaw and walk to it: no rising)
                    HandPending = true;
                }
                Note(2, x, z, (uint)c); // (the cell it came out of, for the self-test)
                MassCount[c]--;
                MassTotal--;
                Released++;
                ReleasedSoft++;
                left--;
            }
        }
    }

    /// <summary>Released by the soft wake (self-test).</summary>
    public long ReleasedSoft;

    /// <summary>A free spot in the mass cell round (<paramref name="cx"/>, <paramref name="cz"/>): a few tries; false if none.</summary>
    private bool FreeSpot(float cx, float cz, out float x, out float z)
    {
        x = cx;
        z = cz;
        for (var tries = 0; tries < 6; tries++)
        {
            _mRng = Hash(_mRng + 0x9E3779B9u);
            x = cx + ((_mRng & 0xFFFF) / 65535f - 0.5f) * _mCell * 0.9f;
            z = cz + ((_mRng >> 16) / 65535f - 0.5f) * _mCell * 0.9f;
            if (Walkable(x, z) && !Packed(x, z))
                return true;
        }
        return false;
    }

    // The eight directions round the compass, in order (a turn of 45 degrees is the next one).
    private static readonly int[] RingX = [1, 1, 0, -1, -1, -1, 0, 1], RingZ = [0, 1, 1, 1, 0, -1, -1, -1];

    private static int RingIndex(int dc, int dr)
    {
        for (var k = 0; k < 8; k++)
            if (RingX[k] == dc && RingZ[k] == dr)
                return k;
        return 0;
    }

    /// <summary>The mass cell (dc, dr) from <paramref name="c"/> if it is walkable and has room this tick (-1: not).</summary>
    private int MassDownstream(int c, int dc, int dr, int cap)
    {
        int ux = c % _mCols + dc, uz = c / _mCols + dr;
        if (ux < 0 || uz < 0 || ux >= _mCols || uz >= _mRows)
            return -1;
        var u = uz * _mCols + ux;
        return _mWalk[u] && _mNext[u] < cap ? u : -1;
    }

    /// <summary><see cref="MassDownstream"/>, and only if the flow field there is cheaper than <paramref name="own"/> (sideways, but still downhill).</summary>
    private int MassSideways(int c, int dc, int dr, int cap, float own)
    {
        var u = MassDownstream(c, dc, dr, cap);
        if (u < 0 || float.IsPositiveInfinity(own))
            return -1;
        var fc = CellOf(MinX + (u % _mCols + 0.5f) * _mCell, MinZ + (u / _mCols + 0.5f) * _mCell);
        return fc >= 0 && _cost[fc] < own ? u : -1;
    }

    /// <summary>A mass cell's middle (world, on the ground).</summary>
    public Vector3 MassCentre(int cell)
    {
        float x = MinX + (cell % _mCols + 0.5f) * _mCell, z = MinZ + (cell / _mCols + 0.5f) * _mCell;
        return new Vector3(x, GroundAt(x, z), z);
    }

    /// <summary>
    /// Puts <paramref name="n"/> zombies in the mass round (<paramref name="x"/>, <paramref name="z"/>): walkable cells
    /// nearest first, each filled to <paramref name="perCell"/>, out to <paramref name="maxRadius"/> m. Returns how many
    /// went in (a staging area: Source/Game/ZombiesCrowdStress.cs). Allocates nothing; the rings are walked cell by cell.
    /// </summary>
    public int SeedMass(float x, float z, int n, int perCell, float maxRadius)
    {
        if (MassCount.Length == 0 || n <= 0 || perCell <= 0)
            return 0;
        var cc = Math.Clamp((int)((x - MinX) / _mCell), 0, _mCols - 1);
        var cr = Math.Clamp((int)((z - MinZ) / _mCell), 0, _mRows - 1);
        var rings = (int)MathF.Ceiling(maxRadius / _mCell);
        var put = 0;
        // Square rings round the middle cell, each ring's cells kept only within the radius (a round blob).
        for (var ring = 0; ring <= rings && put < n; ring++)
            for (var dr = -ring; dr <= ring && put < n; dr++)
                for (var dc = -ring; dc <= ring && put < n; dc++)
                {
                    if (Math.Max(Math.Abs(dc), Math.Abs(dr)) != ring)
                        continue;
                    int c = cc + dc, r = cr + dr;
                    if (c < 0 || r < 0 || c >= _mCols || r >= _mRows || (dc * dc + dr * dr) * _mCell * _mCell > maxRadius * maxRadius + _mCell * _mCell)
                        continue;
                    var k = r * _mCols + c;
                    if (!_mWalk[k] || MassCount[k] >= perCell)
                        continue;
                    var add = Math.Min(perCell - MassCount[k], n - put);
                    MassCount[k] += add;
                    MassTotal += add;
                    put += add;
                }
        return put;
    }
}
