using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The mass tier (docs/zombies/MASS-HORDE.md, "Mass"): far from every player the crowd needs no particles at all. Per
/// cell of <see cref="CrowdTuning.NetCell"/> m (the network's grid) only a count of zombies and their mean velocity,
/// moved along the flow field like a fluid (a share of each cell's count flows to the neighbour downstream every tick,
/// held back where the next cell is full). Particles further than <see cref="CrowdTuning.MassRange"/> +
/// <see cref="CrowdTuning.MassHysteresis"/> from everyone merge into their cell; cells within
/// <see cref="CrowdTuning.MassRange"/> of a player let theirs out as particles again; at most
/// <see cref="CrowdTuning.MassPerTick"/> each way a tick. Mass zombies are unhurt (only an unhurt particle merges) and all
/// have <see cref="MassHP"/>. <see cref="Total"/> = particles + mass, always.
/// <para>
/// Zombie types (feat/zombie-types): the mass holds one count a cell. When types reach the mass, each cell takes a count
/// per type (a second index: cell * kinds + kind) at the four places a count moves: <see cref="TickMass"/>'s merge
/// (the particle's type), flux (each type's share) and release (the type the particle gets back), and
/// <see cref="BlastMass"/> (kills per type for the credit). Nothing else reads the counts by type.
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
    private int _mergeN;
    private uint _mRng = 0x6A55u;

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

    /// <summary>In the particle loop: a far, unhurt, walking particle may merge this tick (the first few found, from a turning start).</summary>
    private void OfferMerge(int i)
    {
        if (_mergeN < Math.Min(_mergeCand.Length, Tuning.MassPerTick))
            _mergeCand[_mergeN++] = i;
    }

    /// <summary>
    /// After the particle loop: merge this tick's far particles (highest index first), move the mass along the flow
    /// field, and let particles out of cells near a player. <paramref name="tx"/>/<paramref name="tz"/>: the players.
    /// </summary>
    private void TickMass(long tick, ReadOnlySpan<float> tx, ReadOnlySpan<float> tz)
    {
        if (!Tuning.Mass || MassCount.Length == 0)
        {
            _mergeN = 0;
            return;
        }
        // Merge (the candidates were found in index order: remove from the highest).
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
            RemoveAt(i);
        }
        _mergeN = 0;
        if (MassTotal == 0)
            return;

        // Flow: each cell sends a share of its zombies one cell downstream (the flow field's heading at its centre), as far
        // as the next cell has room. Worked out from this tick's counts into the next (no order bias).
        var n = _mCols * _mRows;
        Array.Copy(MassCount, _mNext, n);
        var dt = World.TickDt;
        var cap = Tuning.MassCapacity;
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
                for (var t = 0; t < tx.Length; t++)
                {
                    var d = (tx[t] - cx) * (tx[t] - cx) + (tz[t] - cz) * (tz[t] - cz);
                    if (d < best && d > 1e-4f)
                    {
                        best = d;
                        var l = MathF.Sqrt(d);
                        hx = (tx[t] - cx) / l;
                        hz = (tz[t] - cz) / l;
                    }
                }
            }
            MassVX[c] = hx * Tuning.Speed;
            MassVZ[c] = hz * Tuning.Speed;
            int dc = MathF.Abs(hx) > 0.38f ? Math.Sign(hx) : 0, dr = MathF.Abs(hz) > 0.38f ? Math.Sign(hz) : 0;
            if (dc == 0 && dr == 0)
                continue;
            int ux = c % _mCols + dc, uz = c / _mCols + dr;
            if (ux < 0 || uz < 0 || ux >= _mCols || uz >= _mRows)
                continue;
            var u = uz * _mCols + ux;
            if (!_mWalk[u])
                continue;
            // A cell's zombies cross it in cell / speed seconds: that share of them a tick.
            _mFlux[c] += m * Tuning.Speed * dt / (_mCell * (dc != 0 && dr != 0 ? 1.41421356f : 1f));
            var move = Math.Min((int)_mFlux[c], Math.Min(_mNext[c], Math.Max(0, cap - _mNext[u])));
            if (move <= 0)
            {
                _mFlux[c] = MathF.Min(_mFlux[c], 1.5f); // (held back: no backlog building up)
                continue;
            }
            _mFlux[c] -= move;
            _mNext[c] -= move;
            _mNext[u] += move;
        }
        Array.Copy(_mNext, MassCount, n);

        // Release: cells within the mass range of a player let zombies out as particles (nearest cells first would cost a
        // sort; in index order from a turning start instead, a few a tick).
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
                    if (Walkable(x, z))
                        break;
                }
                if (!Walkable(x, z))
                    break;
                Add(x, z, MassVX[c], MassVZ[c], _mRng, tick, MassHP);
                MassCount[c]--;
                MassTotal--;
                Released++;
                left--;
            }
        }
    }

    /// <summary>
    /// Server: a blast over the mass: in each mass cell whose middle the blast's damage there would kill a mass zombie,
    /// the share of the cell the blast covers dies (at least one if the middle is inside the radius). Writes the kills
    /// per cell into <paramref name="killedAt"/> (cell, count) pairs; returns how many pairs.
    /// </summary>
    public int BlastMass(Vector3 center, float radius, float maxDamage, Span<(int Cell, int Count)> killedAt)
    {
        if (MassTotal == 0 || MassCount.Length == 0)
            return 0;
        var reach = radius + _mCell * 0.71f;
        var c0 = Math.Clamp((int)((center.X - reach - MinX) / _mCell), 0, _mCols - 1);
        var c1 = Math.Clamp((int)((center.X + reach - MinX) / _mCell), 0, _mCols - 1);
        var r0 = Math.Clamp((int)((center.Z - reach - MinZ) / _mCell), 0, _mRows - 1);
        var r1 = Math.Clamp((int)((center.Z + reach - MinZ) / _mCell), 0, _mRows - 1);
        var cover = MathF.Min(1f, MathF.PI * radius * radius / (_mCell * _mCell));
        var n = 0;
        for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
            {
                var k = r * _mCols + c;
                if (MassCount[k] == 0 || n == killedAt.Length)
                    continue;
                var cx = MinX + (c + 0.5f) * _mCell;
                var cz = MinZ + (r + 0.5f) * _mCell;
                var dist = new Vector3(cx - center.X, Y + 1.1f - center.Y, cz - center.Z).Length();
                var inside = dist <= radius;
                var dmg = maxDamage * MathF.Pow(MathF.Max(0f, 1f - MathF.Max(0f, dist - _mCell * 0.5f) / radius), 1.4f);
                if (dmg < MassHP)
                    continue;
                var dead = Math.Min(MassCount[k], Math.Max(inside ? 1 : 0, (int)(MassCount[k] * cover * (inside ? 1f : 0.5f))));
                if (dead <= 0)
                    continue;
                MassCount[k] -= dead;
                MassTotal -= dead;
                killedAt[n++] = (k, dead);
            }
        return n;
    }

    /// <summary>A mass cell's middle (world, on the floor).</summary>
    public Vector3 MassCentre(int cell) => new(MinX + (cell % _mCols + 0.5f) * _mCell, Y, MinZ + (cell / _mCols + 0.5f) * _mCell);
}
