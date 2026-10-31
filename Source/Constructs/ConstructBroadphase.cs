using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The broadphase (board item cl01): a hashed uniform grid over the bodies' bounding spheres in x and z, so ray tests,
/// keeping players out, blasts and the physics' construct-against-construct pairs look only at the bodies near them, not
/// at every body in the world. Small worlds (under <see cref="LinearBelow"/> bodies) skip it and walk the list, exactly
/// as before.
/// <para>
/// The grid is built lazily: the first query after a step (or after a body went) rebuilds it, in two passes over the
/// bodies with no allocation (counting sort into <see cref="Buckets"/> buckets; the entry array grows only when the world
/// does). A body made, or one that gained parts, since the last build is kept in a short list every query walks too, so
/// it is never missed. Each body goes in every cell its sphere (plus <see cref="GridMargin"/>, a tick's travel at the
/// speed cap) overlaps, so a query finds every body whose sphere meets its own box. Buckets are hashed, so a query also
/// meets bodies from far cells: callers keep their own sphere test, as they always had.
/// </para>
/// </summary>
public sealed partial class ConstructWorld
{
    /// <summary>Worlds with fewer bodies than this walk the list in every query (the way it was before the grid).</summary>
    public const int LinearBelow = 48;
    public const float CellSize = 8f;
    public const int Buckets = 4096; // (a power of two)
    public const float GridMargin = 1.2f; // metres: a tick at the speed cap (ConstructPhysics.MaxSpeed), and a little more
    private const int MaxCellSpan = 24; // a body (or a query) wider than this many cells goes in the always-walked list (or walks the list)

    private readonly int[] _bStart = new int[Buckets + 1];
    private readonly int[] _bFill = new int[Buckets];
    private Construct[] _entries = new Construct[1024];
    private readonly List<Construct> _gridExtra = new(256);
    private bool _gridStale = true;
    private int _gridVersion = 1;
    private int _queryStamp;
    private uint _moveCount;

    /// <summary>Grid rebuilds so far, and the bodies the last query looked at (the self-test).</summary>
    public int GridBuilds, LastCandidates;

    /// <summary>Do queries use the grid (a big enough world)?</summary>
    public bool UseGrid => Bodies.Count >= LinearBelow;

    /// <summary>The grid must be rebuilt before its next query (a body went, or the bodies moved).</summary>
    internal void GridStale() => _gridStale = true;

    /// <summary>The bodies moved (the server's step, a client's blend): the grid is rebuilt at its next query.</summary>
    public void GridMoved()
    {
        _moveCount++;
        _gridStale = true;
    }

    private void GridAdded(Construct c)
    {
        if (_gridStale || c.GridMark == _gridVersion)
            return;
        c.GridMark = _gridVersion;
        _gridExtra.Add(c);
    }

    /// <summary>A body gained a part: its sphere may grow past its cells, so queries walk it until the next build.</summary>
    private void GridGrew(Construct c) => GridAdded(c);

    private static int Hash(int cx, int cz) => (int)((uint)(cx * 73856093 ^ cz * 19349663) & (Buckets - 1));

    private static int Cell(float v) => (int)MathF.Floor(v / CellSize);

    private void GridBuild()
    {
        _gridStale = false;
        _gridVersion++;
        _gridExtra.Clear();
        GridBuilds++;
        Array.Clear(_bFill);
        var total = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                if (_entries.Length < total)
                    _entries = new Construct[Math.Max(total, _entries.Length * 2)]; // (only when the world grew)
                var at = 0;
                for (var b = 0; b < Buckets; b++)
                {
                    _bStart[b] = at;
                    at += _bFill[b];
                    _bFill[b] = 0;
                }
                _bStart[Buckets] = at;
            }
            for (var i = 0; i < Bodies.Count; i++)
            {
                var c = Bodies[i];
                if (c.Dirty)
                {
                    // (its sphere is out of date until it rebuilds: walked by every query instead)
                    if (pass == 0 && c.GridMark != _gridVersion)
                    {
                        c.GridMark = _gridVersion;
                        _gridExtra.Add(c);
                    }
                    continue;
                }
                var com = c.WorldCom;
                var r = c.Radius + GridMargin;
                int x0 = Cell(com.X - r), x1 = Cell(com.X + r), z0 = Cell(com.Z - r), z1 = Cell(com.Z + r);
                if (x1 - x0 >= MaxCellSpan || z1 - z0 >= MaxCellSpan || !float.IsFinite(com.X + com.Z))
                {
                    if (pass == 0 && c.GridMark != _gridVersion)
                    {
                        c.GridMark = _gridVersion;
                        _gridExtra.Add(c);
                    }
                    continue;
                }
                for (var cx = x0; cx <= x1; cx++)
                    for (var cz = z0; cz <= z1; cz++)
                    {
                        var h = Hash(cx, cz);
                        if (pass == 0)
                        {
                            _bFill[h]++;
                            total++;
                        }
                        else
                            _entries[_bStart[h] + _bFill[h]++] = c;
                    }
            }
        }
    }

    private void TakeBucket(int h, List<Construct> into, int stamp)
    {
        for (int k = _bStart[h], e = _bStart[h + 1]; k < e; k++)
        {
            var c = _entries[k];
            if (c.QueryMark == stamp)
                continue;
            c.QueryMark = stamp;
            into.Add(c);
        }
    }

    private void TakeExtra(List<Construct> into, int stamp)
    {
        foreach (var c in _gridExtra)
            if (c.QueryMark != stamp && _byId.TryGetValue(c.Id, out var live) && ReferenceEquals(live, c))
            {
                c.QueryMark = stamp;
                into.Add(c);
            }
    }

    /// <summary>
    /// The bodies whose spheres may meet the box from <paramref name="min"/> to <paramref name="max"/> (only x and z
    /// count), into <paramref name="into"/> (cleared first). In a small world: every body. Callers test each themselves.
    /// </summary>
    public List<Construct> Near(Vector3 min, Vector3 max, List<Construct> into)
    {
        into.Clear();
        int x0 = Cell(min.X), x1 = Cell(max.X), z0 = Cell(min.Z), z1 = Cell(max.Z);
        if (!UseGrid || x1 - x0 >= MaxCellSpan * 2 || z1 - z0 >= MaxCellSpan * 2 || !float.IsFinite(min.X + min.Z + max.X + max.Z))
        {
            into.AddRange(Bodies);
            LastCandidates = into.Count;
            return into;
        }
        if (_gridStale)
            GridBuild();
        var stamp = ++_queryStamp;
        for (var cx = x0; cx <= x1; cx++)
            for (var cz = z0; cz <= z1; cz++)
                TakeBucket(Hash(cx, cz), into, stamp);
        TakeExtra(into, stamp);
        LastCandidates = into.Count;
        return into;
    }

    /// <summary>The bodies whose spheres may meet a sphere (see <see cref="Near(Vector3, Vector3, List{Construct})"/>).</summary>
    public List<Construct> Near(Vector3 centre, float radius, List<Construct> into) => Near(centre - new Vector3(radius), centre + new Vector3(radius), into);

    /// <summary>
    /// The bodies whose spheres may meet a ray (to <paramref name="maxDist"/>): the cells it crosses in x and z, walked
    /// in order (a 2D grid walk). In a small world: every body.
    /// </summary>
    public List<Construct> NearRay(Vector3 origin, Vector3 dir, float maxDist, List<Construct> into)
    {
        into.Clear();
        var end = origin + dir * maxDist;
        if (!UseGrid || !float.IsFinite(origin.X + origin.Z + end.X + end.Z) || maxDist > CellSize * 1024)
        {
            into.AddRange(Bodies);
            LastCandidates = into.Count;
            return into;
        }
        if (_gridStale)
            GridBuild();
        var stamp = ++_queryStamp;
        int cx = Cell(origin.X), cz = Cell(origin.Z), ex = Cell(end.X), ez = Cell(end.Z);
        int sx = dir.X > 0 ? 1 : -1, sz = dir.Z > 0 ? 1 : -1;
        // How far along the ray (in metres) the next x and z cell edges are, and how far one cell is.
        var dx = MathF.Abs(dir.X) > 1e-6f ? CellSize / MathF.Abs(dir.X) : float.MaxValue;
        var dz = MathF.Abs(dir.Z) > 1e-6f ? CellSize / MathF.Abs(dir.Z) : float.MaxValue;
        var tx = MathF.Abs(dir.X) > 1e-6f ? ((sx > 0 ? (cx + 1) * CellSize : cx * CellSize) - origin.X) / dir.X : float.MaxValue;
        var tz = MathF.Abs(dir.Z) > 1e-6f ? ((sz > 0 ? (cz + 1) * CellSize : cz * CellSize) - origin.Z) / dir.Z : float.MaxValue;
        for (var guard = 0; guard < 4096; guard++)
        {
            TakeBucket(Hash(cx, cz), into, stamp);
            if (cx == ex && cz == ez)
                break;
            if (tx < tz)
            {
                if (tx > maxDist)
                    break;
                cx += sx;
                tx += dx;
            }
            else
            {
                if (tz > maxDist)
                    break;
                cz += sz;
                tz += dz;
            }
        }
        TakeExtra(into, stamp);
        LastCandidates = into.Count;
        return into;
    }
}
