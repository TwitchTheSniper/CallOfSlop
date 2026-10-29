using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What the players can see, for waking and sleeping the mass horde (docs/zombies/MASS-HORDE.md, "Waking and sleeping").
/// Server side, from each standing player's eye and look (yaw and pitch), never from a client's drawing: a point is seen
/// by a player when it is inside their view (<see cref="CrowdTuning.SightHalfAngle"/> either side,
/// <see cref="CrowdTuning.SightHalfAngleV"/> up and down, a body's width of slack) and its cell is not behind the ground
/// from their eye. The ground test runs per cell of the mass grid (4 m) near each player, every few ticks
/// (<see cref="Occlude"/>): a line from the eye to the top of a body standing in the cell's middle, checked against the
/// terrain every 2 m (the terrain's own samples); a flat map hides nothing. No allocation once set up.
/// </summary>
public sealed class CrowdSight
{
    /// <summary>At most this many views (the players standing; the horde controller's targets).</summary>
    public const int MaxViews = 8;

    /// <summary>The views this tick.</summary>
    public int Views;

    private readonly Vector3[] _eye = new Vector3[MaxViews], _f = new Vector3[MaxViews], _r = new Vector3[MaxViews], _u = new Vector3[MaxViews];
    private float _tanH = 1.88f, _tanV = 0.9f, _weight = 2.5f;

    // Per cell of the grid: bit v set = hidden from view v by the ground, valid where _stamp is the current generation.
    private byte[] _hidden = [];
    private int[] _stamp = [];
    private int _gen;
    private int _cols, _rows;
    private float _minX, _minZ, _cell = 4f;
    private TerrainData? _terrain;

    /// <summary>Cells tested against the ground in the last <see cref="Occlude"/>, and how many were hidden (self-test, F9).</summary>
    public int Tested, Hidden;

    /// <summary>The grid (the mass tier's) and the ground (null: flat, nothing hidden).</summary>
    public void Setup(TerrainData? terrain, float minX, float minZ, float cell, int cols, int rows)
    {
        _terrain = terrain;
        _minX = minX;
        _minZ = minZ;
        _cell = cell;
        _cols = cols;
        _rows = rows;
        if (_hidden.Length != cols * rows)
        {
            _hidden = new byte[cols * rows];
            _stamp = new int[cols * rows];
        }
        else
            Array.Clear(_stamp);
        _gen = 1;
        Views = 0;
    }

    /// <summary>A new tick's views (none yet), with the tuning's angles and the unseen weight.</summary>
    public void Begin(CrowdTuning t)
    {
        Views = 0;
        _tanH = MathF.Tan(Math.Clamp(t.SightHalfAngle, 1f, 89.5f) * MathF.PI / 180f);
        _tanV = MathF.Tan(Math.Clamp(t.SightHalfAngleV, 1f, 89.5f) * MathF.PI / 180f);
        _weight = MathF.Max(1f, t.UnseenWeight);
    }

    /// <summary>Adds a player's view: their eye and look.</summary>
    public void AddView(Vector3 eye, float yaw, float pitch)
    {
        if (Views >= MaxViews)
            return;
        var v = Views++;
        _eye[v] = eye;
        var f = World.Forward(yaw, pitch);
        var r = World.FlatRight(yaw);
        _f[v] = f;
        _r[v] = r;
        _u[v] = Vector3.Cross(r, f);
    }

    /// <summary>
    /// The ground test for every cell within <paramref name="range"/> m (flat) of each view: which are hidden from which
    /// view. Cells further out count as not hidden.
    /// </summary>
    public void Occlude(float range)
    {
        _gen++;
        Tested = Hidden = 0;
        if (_terrain == null || _cols == 0)
            return;
        var span = (int)MathF.Ceiling(range / _cell);
        for (var v = 0; v < Views; v++)
        {
            var e = _eye[v];
            var ec = (int)((e.X - _minX) / _cell);
            var er = (int)((e.Z - _minZ) / _cell);
            var bit = (byte)(1 << v);
            for (var r = Math.Max(0, er - span); r <= Math.Min(_rows - 1, er + span); r++)
                for (var c = Math.Max(0, ec - span); c <= Math.Min(_cols - 1, ec + span); c++)
                {
                    var k = r * _cols + c;
                    if (_stamp[k] != _gen)
                    {
                        _stamp[k] = _gen;
                        _hidden[k] = 0;
                    }
                    float x = _minX + (c + 0.5f) * _cell, z = _minZ + (r + 0.5f) * _cell;
                    Tested++;
                    if (GroundBetween(e, x, z))
                    {
                        _hidden[k] |= bit;
                        Hidden++;
                    }
                }
        }
    }

    /// <summary>Does the ground rise above the line from <paramref name="e"/> to the top of a body standing at (x, z)?</summary>
    private bool GroundBetween(Vector3 e, float x, float z)
    {
        var t = _terrain!;
        var top = t.Sample(x, z) + HordeCrowd.BodyHeight;
        float dx = x - e.X, dz = z - e.Z;
        var len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 3f)
            return false;
        var steps = (int)(len / 2f);
        for (var s = 1; s < steps; s++)
        {
            var f = s * 2f / len;
            if (f > 1f - 1.5f / len)
                break; // (the body's own ground)
            if (t.Sample(e.X + dx * f, e.Z + dz * f) > e.Y + (top - e.Y) * f + 0.05f)
                return true;
        }
        return false;
    }

    /// <summary>Is (x, y, z) (a body's middle) inside view <paramref name="v"/> and not behind the ground from it?</summary>
    public bool InView(int v, float x, float y, float z)
    {
        var d = new Vector3(x, y, z) - _eye[v];
        var fz = Vector3.Dot(d, _f[v]);
        if (fz <= 0f)
            return false;
        if (MathF.Abs(Vector3.Dot(d, _r[v])) > fz * _tanH + BodySlack || MathF.Abs(Vector3.Dot(d, _u[v])) > fz * _tanV + BodySlack)
            return false;
        return !GroundHides(v, x, z);
    }

    /// <summary>Does the ground hide the cell at (x, z) from view <paramref name="v"/> (as of the last <see cref="Occlude"/>)?</summary>
    public bool GroundHides(int v, float x, float z)
    {
        if (_terrain == null)
            return false;
        int c = (int)((x - _minX) / _cell), r = (int)((z - _minZ) / _cell);
        if ((uint)c >= (uint)_cols || (uint)r >= (uint)_rows)
            return false;
        var k = r * _cols + c;
        return _stamp[k] == _gen && (_hidden[k] & (1 << v)) != 0;
    }

    /// <summary>The slack round a body (m): one whose edge is in view counts as seen.</summary>
    public const float BodySlack = 0.6f;

    /// <summary>
    /// How near a zombie at (x, z), its middle at height <paramref name="y"/>, counts: over the views, the least flat
    /// distance, times the unseen weight from a view that doesn't see it. <paramref name="seen"/>: any view sees it;
    /// <paramref name="nearest"/>: the plain nearest distance. With no views: infinity.
    /// </summary>
    public float Score(float x, float y, float z, out bool seen, out float nearest)
    {
        seen = false;
        nearest = float.MaxValue;
        var best = float.MaxValue;
        for (var v = 0; v < Views; v++)
        {
            float dx = x - _eye[v].X, dz = z - _eye[v].Z;
            var d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < nearest)
                nearest = d;
            var see = InView(v, x, y, z);
            seen |= see;
            var s = see ? d : d * _weight;
            if (s < best)
                best = s;
        }
        return best;
    }
}
