using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Terrain horizon culling for the crowd (docs/zombies/MASS-HORDE.md, "Rendering"): from the camera, the terrain's
/// horizon round the compass, as a profile per direction: for each of <see cref="Bins"/> directions and each of
/// <see cref="Steps"/> distances, the steepest rise (height over distance) of the ground seen so far along that
/// direction. A crowd cell is hidden when, in every direction it spans, the ground nearer than it rises above its top.
/// Conservative: the profiles are built from points on the ground itself (so whatever they hide really is behind
/// ground), a cell's top is its highest ground plus a body, and the test allows for the camera having moved since the
/// profiles were built (<see cref="Rebuild"/>, at most <see cref="MoveTolerance"/> m ago). Directions are binned by a
/// "diamond angle" (no trigonometry per test). CPU only, the same on both GL paths; nothing allocates once built.
/// </summary>
public sealed class CrowdHorizon
{
    public const int Bins = 512;

    /// <summary>The camera may move this far (m) before the profiles are built again.</summary>
    public const float MoveTolerance = 0.3f;

    private float[] _dist = [];
    private float[] _prof = [];
    private readonly float[] _dirX = new float[Bins], _dirZ = new float[Bins];
    private Vector3 _at = new(float.NaN);
    private TerrainData? _terrain;

    public int Steps => _dist.Length;

    /// <summary>Times the profiles were built (log, self-test).</summary>
    public int Builds;

    /// <summary>The camera the profiles are from.</summary>
    public Vector3 At => _at;

    public CrowdHorizon()
    {
        for (var b = 0; b < Bins; b++)
        {
            // The middle of bin b, as a direction (the inverse of DiamondAngle).
            var p = (b + 0.5f) * 4f / Bins;
            float x, z;
            if (p < 1)
                (x, z) = (1 - p, p);
            else if (p < 2)
                (x, z) = (1 - p, 2 - p);
            else if (p < 3)
                (x, z) = (p - 3, 2 - p);
            else
                (x, z) = (p - 3, p - 4);
            var l = MathF.Sqrt(x * x + z * z);
            _dirX[b] = x / l;
            _dirZ[b] = z / l;
        }
    }

    /// <summary>A direction's place round the compass, 0-4 (monotonic in the angle; at most 1 per radian).</summary>
    public static float DiamondAngle(float x, float z)
    {
        if (z >= 0)
            return x >= 0 ? z / (x + z + 1e-20f) : 1 - x / (-x + z + 1e-20f);
        return x < 0 ? 2 - z / (-x - z + 1e-20f) : 3 + x / (x - z + 1e-20f);
    }

    /// <summary>Does the camera need new profiles (moved, another terrain, or none yet)?</summary>
    public bool Stale(TerrainData terrain, Vector3 cam) =>
        !ReferenceEquals(terrain, _terrain) || !(Vector3.DistanceSquared(cam, _at) <= MoveTolerance * MoveTolerance);

    /// <summary>Builds the profiles from <paramref name="cam"/> out to <paramref name="range"/> m (all at once; see <see cref="Begin"/>).</summary>
    public void Rebuild(TerrainData terrain, Vector3 cam, float range)
    {
        Begin(terrain, cam, range);
        BuildBins(0, Bins);
    }

    /// <summary>Starts new profiles from <paramref name="cam"/>; then <see cref="BuildBins"/> over every bin (in parallel stripes if wanted).</summary>
    public void Begin(TerrainData terrain, Vector3 cam, float range)
    {
        if (!ReferenceEquals(terrain, _terrain) || _dist.Length == 0 || _dist[^1] < range)
        {
            // Distances: every 2 m near the camera, then 4% further each step (allocates only when the range grows).
            var n = 0;
            for (var d = 2f; ; d += MathF.Max(2f, d * 0.04f))
            {
                n++;
                if (d >= range)
                    break;
            }
            _dist = new float[n];
            n = 0;
            for (var d = 2f; n < _dist.Length; d += MathF.Max(2f, d * 0.04f))
                _dist[n++] = d;
            _prof = new float[Bins * _dist.Length];
        }
        _terrain = terrain;
        _at = cam;
        Builds++;
    }

    /// <summary>The profiles of bins [<paramref name="from"/>, <paramref name="to"/>).</summary>
    public void BuildBins(int from, int to)
    {
        var terrain = _terrain!;
        var cam = _at;
        var steps = _dist.Length;
        float minX = terrain.OriginX, minZ = terrain.OriginZ, maxX = terrain.MaxX, maxZ = terrain.MaxZ;
        for (var b = from; b < to; b++)
        {
            var best = float.NegativeInfinity;
            var o = b * steps;
            for (var j = 0; j < steps; j++)
            {
                var d = _dist[j];
                float x = cam.X + _dirX[b] * d, z = cam.Z + _dirZ[b] * d;
                if (x >= minX && x <= maxX && z >= minZ && z <= maxZ)
                    best = MathF.Max(best, (terrain.Sample(x, z) - cam.Y) / d);
                _prof[o + j] = best;
            }
        }
    }

    /// <summary>
    /// Is a cell at flat distance <paramref name="d"/> from the camera (its middle at (<paramref name="dx"/>,
    /// <paramref name="dz"/>) from the camera, its half-diagonal <paramref name="r"/>, its highest point
    /// <paramref name="top"/>) hidden behind the ground in every direction it spans?
    /// </summary>
    public bool Hidden(float dx, float dz, float d, float r, float top)
    {
        if (_dist.Length == 0)
            return false;
        var near = d - r - 1f; // (the ground nearer than the cell's nearest corner)
        if (near < 8f)
            return false;
        // The highest the cell could be seen at, from wherever the camera is now (it may have moved a little).
        var num = top - _at.Y + MoveTolerance;
        var rise = MathF.Max(num / near, num / (d + r)) + MoveTolerance / near;
        var j = Step(near);
        if (j < 0)
            return false;
        var p = DiamondAngle(dx, dz);
        var half = (r + MoveTolerance) / MathF.Max(d - r, 1f) + 1e-3f; // (radians, and a diamond angle grows at most as fast)
        var b0 = (int)MathF.Floor((p - half) * (Bins / 4f));
        var b1 = (int)MathF.Floor((p + half) * (Bins / 4f));
        if (b1 - b0 >= Bins / 4)
            return false;
        var steps = _dist.Length;
        // Every direction the cell spans lies between two of these rays: all must rise above it.
        for (var b = b0 - 1; b <= b1 + 1; b++)
        {
            var w = (b % Bins + Bins) % Bins;
            if (_prof[w * steps + j] <= rise)
                return false;
        }
        return true;
    }

    /// <summary>The last step at or nearer than <paramref name="d"/> (-1: none).</summary>
    private int Step(float d)
    {
        int lo = 0, hi = _dist.Length - 1;
        if (d < _dist[0])
            return -1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (_dist[mid] <= d)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }
}
