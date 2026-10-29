using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Shots and blasts against the mass tier (docs/zombies/MASS-HORDE.md, "Combat"; board item dev-mass-points-mp01). A mass
/// cell is only a count, so the server stands its zombies at fixed spots: the cell's zombie number k (0 to count - 1, its
/// "slot") is a vertical capsule at <see cref="MassSpot"/> (a hash of the cell and the slot, kept off the cell's edges so
/// a capsule never reaches into the next cell). A pure function of the counts: the same on every run, never the camera.
/// <para>
/// Wounded mass zombies keep their health in a small pool of wounds (cell, slot, health), linked per cell; every other
/// mass zombie has <see cref="MassHP"/>. The slots stay packed (0 to count - 1) wherever a count changes: a zombie that
/// joins takes the top slot, the flow sends the top slots downstream (their wounds go with them), a release takes the
/// top slot (a wounded one comes out of the mass hurt), and a kill moves the top slot into the dead one's.
/// </para>
/// </summary>
public sealed partial class HordeCrowd
{
    /// <summary>Most wounded mass zombies at once (more: the hit still pays, the zombie's wound is forgotten).</summary>
    public const int WoundCap = 4096;

    private readonly int[] _wCell = new int[WoundCap], _wSlot = new int[WoundCap], _wNext = new int[WoundCap];
    private readonly float[] _wHP = new float[WoundCap];
    private int[] _wHead = [];
    private int _wFree = -1, _wUsed;

    /// <summary>Wounded mass zombies now (self-test, F9).</summary>
    public int Wounded { get; private set; }

    private void SetupWounds(int cells)
    {
        _wHead = new int[cells];
        ClearWounds();
    }

    private void ClearWounds()
    {
        Array.Fill(_wHead, -1);
        _wFree = -1;
        _wUsed = 0;
        Wounded = 0;
    }

    /// <summary>The wound of slot <paramref name="slot"/> in mass cell <paramref name="c"/> (-1: unhurt).</summary>
    private int WoundOf(int c, int slot)
    {
        for (var w = _wHead[c]; w >= 0; w = _wNext[w])
            if (_wSlot[w] == slot)
                return w;
        return -1;
    }

    private void LinkWound(int w, int c)
    {
        _wCell[w] = c;
        _wNext[w] = _wHead[c];
        _wHead[c] = w;
    }

    private void UnlinkWound(int w)
    {
        var c = _wCell[w];
        if (_wHead[c] == w)
            _wHead[c] = _wNext[w];
        else
            for (var p = _wHead[c]; p >= 0; p = _wNext[p])
                if (_wNext[p] == w)
                {
                    _wNext[p] = _wNext[w];
                    break;
                }
    }

    private void FreeWound(int w)
    {
        UnlinkWound(w);
        _wNext[w] = _wFree;
        _wFree = w;
        Wounded--;
    }

    /// <summary>A mass zombie's health now.</summary>
    public float MassHealth(int c, int slot)
    {
        if (_wHead.Length == 0 || _wHead[c] < 0)
            return MassHP;
        var w = WoundOf(c, slot);
        return w >= 0 ? _wHP[w] : MassHP;
    }

    /// <summary>Sets a mass zombie's health (below <see cref="MassHP"/>: a wound; at it: unhurt again).</summary>
    public void SetMassHealth(int c, int slot, float hp)
    {
        if (_wHead.Length == 0)
            return;
        var w = WoundOf(c, slot);
        if (hp >= MassHP)
        {
            if (w >= 0)
                FreeWound(w);
            return;
        }
        if (w < 0)
        {
            if (_wFree >= 0)
            {
                w = _wFree;
                _wFree = _wNext[w];
            }
            else if (_wUsed < WoundCap)
                w = _wUsed++;
            else
                return; // (the pool is full: this one forgets its wound)
            _wSlot[w] = slot;
            LinkWound(w, c);
            Wounded++;
        }
        _wHP[w] = hp;
    }

    /// <summary>The flow moved the top <paramref name="move"/> of <paramref name="from"/>'s <paramref name="fromCount"/> slots to the top of <paramref name="to"/>'s <paramref name="toCount"/>.</summary>
    private void MoveWounds(int from, int fromCount, int move, int to, int toCount)
    {
        var first = fromCount - move;
        for (var w = _wHead[from]; w >= 0;)
        {
            var next = _wNext[w];
            if (_wSlot[w] >= first)
            {
                UnlinkWound(w);
                _wSlot[w] = toCount + _wSlot[w] - first;
                LinkWound(w, to);
            }
            w = next;
        }
    }

    /// <summary>The top slot of cell <paramref name="c"/> (of <paramref name="count"/>) leaves the mass: its health (and its wound is gone).</summary>
    private float TakeTop(int c, int count)
    {
        if (_wHead.Length == 0 || _wHead[c] < 0)
            return MassHP;
        var w = WoundOf(c, count - 1);
        if (w < 0)
            return MassHP;
        var hp = _wHP[w];
        FreeWound(w);
        return hp;
    }

    /// <summary>
    /// Server: mass zombie <paramref name="slot"/> of cell <paramref name="c"/> is killed: the cell's count drops by one
    /// and its top slot takes the dead one's place (with its wound).
    /// </summary>
    public void KillMass(int c, int slot)
    {
        var top = MassCount[c] - 1;
        if (slot < 0 || slot > top)
            return;
        if (_wHead[c] >= 0)
        {
            var w = WoundOf(c, slot);
            if (w >= 0)
                FreeWound(w);
            if (slot != top && (w = WoundOf(c, top)) >= 0)
                _wSlot[w] = slot;
        }
        MassCount[c]--;
        MassTotal--;
    }

    /// <summary>The mass cell (x, z) is in (clamped to the grid).</summary>
    public int MassCellAt(float x, float z) => MassCellOf(x, z);

    /// <summary>Where slot <paramref name="slot"/> of mass cell <paramref name="c"/> stands (flat), off the cell's edges.</summary>
    public void MassSpot(int c, int slot, out float x, out float z)
    {
        var h = Hash((uint)c * 0x9E3779B1u ^ (uint)slot * 0x85EBCA77u ^ 0x3A55u);
        x = MinX + (c % _mCols + MassInset + (h & 0xFFFF) / 65536f * (1f - 2 * MassInset)) * _mCell;
        z = MinZ + (c / _mCols + MassInset + (h >> 16) / 65536f * (1f - 2 * MassInset)) * _mCell;
    }

    /// <summary>The share of a mass cell kept clear at each edge: more than a body's radius (a capsule stays in its cell).</summary>
    private float MassInset => MathF.Min(0.25f, (BodyRadius + 0.02f) / MathF.Max(1f, _mCell));

    /// <summary>The ground under slot <paramref name="slot"/> of mass cell <paramref name="c"/>, and its spot.</summary>
    public Vector3 MassFeet(int c, int slot)
    {
        MassSpot(c, slot, out var x, out var z);
        return new Vector3(x, GroundAt(x, z), z);
    }

    /// <summary>
    /// Server: the nearest mass zombie a ray from <paramref name="o"/> along <paramref name="d"/> (unit) hits further
    /// than <paramref name="after"/> and nearer than <paramref name="dist"/> (shortened to the hit). Walks the mass cells
    /// along the ray's flat shadow in order (a 2D DDA) and stops at the first cell with a hit: a capsule never leaves its
    /// cell, so no later cell can hold a nearer one. Allocates nothing.
    /// </summary>
    public bool MassRayHit(Vector3 o, Vector3 d, ref float dist, float after, out int cell, out int slot, out bool head)
    {
        cell = slot = -1;
        head = false;
        if (MassTotal == 0 || MassCount.Length == 0)
            return false;
        var flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        if (flat < 1e-4f)
            return false;
        // The flat ray in cell units.
        float gx = (o.X - MinX) / _mCell, gz = (o.Z - MinZ) / _mCell;
        float ux = d.X / _mCell, uz = d.Z / _mCell; // (cells per metre of ray)
        var cx = (int)MathF.Floor(gx);
        var cz = (int)MathF.Floor(gz);
        int sx = ux > 0 ? 1 : -1, sz = uz > 0 ? 1 : -1;
        var tdx = MathF.Abs(ux) > 1e-9f ? 1f / MathF.Abs(ux) : float.PositiveInfinity;
        var tdz = MathF.Abs(uz) > 1e-9f ? 1f / MathF.Abs(uz) : float.PositiveInfinity;
        var tx = MathF.Abs(ux) > 1e-9f ? ((ux > 0 ? cx + 1 - gx : gx - cx) * tdx) : float.PositiveInfinity;
        var tz = MathF.Abs(uz) > 1e-9f ? ((uz > 0 ? cz + 1 - gz : gz - cz) * tdz) : float.PositiveInfinity;
        var t0 = 0f;
        var best = dist;
        var r2 = BodyRadius * BodyRadius;
        var a = flat * flat;
        for (var guard = 0; guard < 4096 && t0 < best; guard++)
        {
            if ((uint)cx < (uint)_mCols && (uint)cz < (uint)_mRows)
            {
                var c = cz * _mCols + cx;
                var m = MassCount[c];
                for (var k = 0; k < m; k++)
                {
                    MassSpot(c, k, out var px, out var pz);
                    // The vertical cylinder: the ray's flat closest approach, backed up to the surface.
                    float qx = o.X - px, qz = o.Z - pz;
                    var b = qx * d.X + qz * d.Z;
                    var cc = qx * qx + qz * qz - r2;
                    var disc = b * b - a * cc;
                    if (disc < 0)
                        continue;
                    var sq = MathF.Sqrt(disc);
                    var th = (-b - sq) / a;
                    if (th <= after || th >= best)
                        continue;
                    var feet = GroundAt(px, pz);
                    var y = o.Y + d.Y * th - feet;
                    if (y < 0 || y > BodyHeight)
                        continue;
                    best = th;
                    cell = c;
                    slot = k;
                    head = y > HeadFrom;
                }
                if (cell >= 0)
                    break;
            }
            else if ((cx < 0 && sx < 0) || (cz < 0 && sz < 0) || (cx >= _mCols && sx > 0) || (cz >= _mRows && sz > 0))
                break; // (off the grid and leaving it)
            // The next cell along the ray.
            if (tx < tz)
            {
                t0 = tx;
                tx += tdx;
                cx += sx;
            }
            else
            {
                t0 = tz;
                tz += tdz;
                cz += sz;
            }
        }
        if (cell < 0)
            return false;
        dist = best;
        return true;
    }

    /// <summary>The mass cells a blast of <paramref name="radius"/> round <paramref name="center"/> may reach (inclusive).</summary>
    public void MassCellsNear(Vector3 center, float radius, out int c0, out int c1, out int r0, out int r1)
    {
        c0 = Math.Clamp((int)MathF.Floor((center.X - radius - MinX) / _mCell), 0, Math.Max(0, _mCols - 1));
        c1 = Math.Clamp((int)MathF.Floor((center.X + radius - MinX) / _mCell), 0, Math.Max(0, _mCols - 1));
        r0 = Math.Clamp((int)MathF.Floor((center.Z - radius - MinZ) / _mCell), 0, Math.Max(0, _mRows - 1));
        r1 = Math.Clamp((int)MathF.Floor((center.Z + radius - MinZ) / _mCell), 0, Math.Max(0, _mRows - 1));
    }

    /// <summary>Self-test: every wound's slot is inside its cell's count, and the pool's count matches its lists.</summary>
    public bool WoundsConsistent()
    {
        var n = 0;
        for (var c = 0; c < _wHead.Length; c++)
            for (var w = _wHead[c]; w >= 0; w = _wNext[w])
            {
                if (_wCell[w] != c || _wSlot[w] < 0 || _wSlot[w] >= MassCount[c] || _wHP[w] >= MassHP || _wHP[w] <= 0)
                    return false;
                for (var v = _wNext[w]; v >= 0; v = _wNext[v])
                    if (_wSlot[v] == _wSlot[w])
                        return false;
                n++;
            }
        return n == Wounded;
    }
}
