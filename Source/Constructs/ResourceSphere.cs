using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Raw resources on a planetoid (docs/planets/WORLDS.md "Ore"; docs/sandbox/CONSTRUCTS.md "Gathering"). A round world has
/// no (x, z), so its deposits lie on the six faces of the cube round it: a point's direction from the centre picks the
/// face (its largest axis) and two angles across it, in metres along the ground (the angle times the radius), and each
/// face is a flat field of its own, generated as a flat map's is. Deposits stop at a face's edge. Underground kinds only:
/// trees stand along +Y. Everything here is asked by a world point; on a flat map the same calls read its (x, z).
/// </summary>
public sealed partial class ResourceField
{
    /// <summary>The round world this field lies on (null: a flat map).</summary>
    public Planetoid? Planet => _planet;

    /// <summary>How far round the camera a planetoid's outcrops are drawn (m): the ground curves away long before a flat map's 800.</summary>
    public const float SphereDrawRadius = 400f;

    private readonly List<Vector3> _dirs = new(); // a planetoid: each deposit's direction from the centre

    private static readonly Vector3[] FaceN = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
    private static readonly Vector3[] FaceU = [Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX];
    private static readonly Vector3[] FaceV = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY];

    /// <summary>Half a cube face's width along the ground (m): an eighth of the way round.</summary>
    private static float FaceHalf(Planetoid p) => (float)(p.Radius * Math.PI / 4);

    /// <summary>A world point's place in the field: its face and the metres across it (a flat map: face 0 and its x, z).</summary>
    private void Locate(Vector3 p, out int face, out float x, out float z)
    {
        if (_planet is not { } pl)
        {
            (face, x, z) = (0, p.X, p.Z);
            return;
        }
        LocateDir(p - pl.Center, out face, out x, out z);
    }

    /// <summary>A direction from a planetoid's centre (any length): its face and the metres across it.</summary>
    private void LocateDir(Vector3 d, out int face, out float x, out float z)
    {
        var pl = _planet!;
        float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y), az = MathF.Abs(d.Z);
        face = ax >= ay && ax >= az ? (d.X >= 0 ? 0 : 1) : ay >= az ? (d.Y >= 0 ? 2 : 3) : d.Z >= 0 ? 4 : 5;
        var n = MathF.Max(MathF.Max(ax, ay), MathF.Max(az, 1e-6f));
        var r = (float)pl.Radius;
        x = MathF.Atan(Vector3.Dot(d, FaceU[face]) / n) * r;
        z = MathF.Atan(Vector3.Dot(d, FaceV[face]) / n) * r;
    }

    /// <summary>
    /// A planetoid's ground colour at a point (from its centre) with the deposit under it showing (Sean, 2026-11-22: each kind
    /// its own earth): the ground leans towards the strongest underground deposit's "ground" colour, most in its middle,
    /// rock faces less. Asked by the planet renderer's page worker: it reads only what doesn't change after the field is made.
    /// </summary>
    public Vector3 TintGround(double px, double py, double pz, Vector3 col, float rock)
    {
        var share = _table.PlanetGround;
        if (_planet == null || share <= 0)
            return col;
        LocateDir(new Vector3((float)px, (float)py, (float)pz), out var f, out var x, out var z);
        var i = At(x, z, false, false, out var s, f);
        if (i < 0 || s <= EdgeThin)
            return col;
        var w = share * MathF.Min(1, (s - EdgeThin) / GroundFade) * (1 - 0.5f * Math.Clamp(rock, 0f, 1f));
        // Keep the ground's own light and dark (slopes, snow, sand): only its hue and some of its brightness move.
        var g = _table.Kinds[_deposits[i].Kind].GroundTint;
        var lum = (col.X + col.Y + col.Z) / 3;
        var glum = MathF.Max((g.X + g.Y + g.Z) / 3, 1e-3f);
        var target = g * MathF.Sqrt(lum / glum);
        return Vector3.Lerp(col, Vector3.Lerp(target, g, 0.5f), w);
    }

    private const float GroundFade = 0.3f; // strength over which a deposit's ground colour fades in from its edge

    /// <summary>The direction from a planetoid's centre of a place on one of its faces.</summary>
    private Vector3 DirOf(int face, float x, float z)
    {
        var r = (float)_planet!.Radius;
        return Vector3.Normalize(FaceN[face] + FaceU[face] * MathF.Tan(x / r) + FaceV[face] * MathF.Tan(z / r));
    }

    private void SphereReady()
    {
        _dirs.Capacity = _deposits.Count;
        foreach (var d in _deposits)
            _dirs.Add(DirOf(d.Face, d.X, d.Z));
    }

    /// <summary>The resource layer under a world point, or null.</summary>
    public ResourceKind? KindAt(Vector3 p, bool surface)
    {
        Locate(p, out var f, out var x, out var z);
        var i = At(x, z, surface, false, out _, f);
        return i < 0 ? null : _table.Kinds[_deposits[i].Kind];
    }

    /// <summary>Server: the underground kind a drill at a world point would tap: the strongest deposit with anything left, or null.</summary>
    public ResourceKind? TappableAt(Vector3 p)
    {
        Locate(p, out var f, out var x, out var z);
        var i = At(x, z, false, true, out _, f);
        return i < 0 ? null : _table.Kinds[_deposits[i].Kind];
    }

    /// <summary>Server: takes up to <paramref name="want"/> units from the underground deposit under a world point.</summary>
    public int Tap(Vector3 p, int want, out byte material)
    {
        material = 0;
        Locate(p, out var f, out var x, out var z);
        var key = SpotKey(p);
        return Spend(key, TapDeposit(At(x, z, false, true, out _, f), Math.Min(want, SpotLeft(key)), ref material));
    }

    /// <summary>Server: how much the underground deposit a drill at a world point would tap holds.</summary>
    public int LeftAt(Vector3 p)
    {
        Locate(p, out var f, out var x, out var z);
        var i = At(x, z, false, true, out _, f);
        return i < 0 ? 0 : _deposits[i].Left;
    }

    /// <summary>The nearest underground deposit a drill of <paramref name="tier"/> can work, from a world point ("nearest deposit: iron vein, 84 m north east").</summary>
    public string NearestUnderground(Vector3 p, int tier = ConstructParts.MaxTier)
    {
        if (_planet is not { } pl)
            return NearestUnderground(p.X, p.Z, tier);
        var rel = p - pl.Center;
        if (rel.LengthSquared() < 1)
            return "no deposits known here";
        var up = Vector3.Normalize(rel);
        var best = -2f;
        var bi = -1;
        for (var i = 0; i < _deposits.Count; i++)
        {
            var dot = Vector3.Dot(_dirs[i], up);
            if (dot <= best)
                continue;
            var d = _deposits[i];
            if (d.Left > 0 && _table.Kinds[d.Kind].MinTier <= tier)
                (best, bi) = (dot, i);
        }
        if (bi < 0)
            return "no deposits known here";
        var metres = MathF.Acos(Math.Clamp(best, -1f, 1f)) * (float)pl.Radius;
        // Which way: north is towards the +Y pole along the ground, east to its right seen from above.
        var north = Vector3.UnitY - up * up.Y;
        var words = "";
        if (north.LengthSquared() > 1e-4f && metres > 10)
        {
            north = Vector3.Normalize(north);
            var east = Vector3.Cross(up, north); // (the game is left-handed seen from above: +Z north on the flat maps is "north" there too)
            var way = _dirs[bi] - up * best;
            float n = Vector3.Dot(way, north), e = Vector3.Dot(way, east);
            var len = MathF.Sqrt(n * n + e * e);
            if (len > 1e-6f)
                words = ((n > 0.38f * len ? "north" : n < -0.38f * len ? "south" : "") + " " + (e > 0.38f * len ? "east" : e < -0.38f * len ? "west" : "")).Trim();
        }
        return $"nearest deposit: {_table.Kinds[_deposits[bi].Kind].Name}, {metres:0} m {words}".TrimEnd();
    }

    /// <summary>A planetoid: the nearest underground deposit's middle on the ground and its kind, or null (the window shot's "deposit").</summary>
    public ResourceKind? NearestMiddle(Vector3 p, out Vector3 at)
    {
        at = default;
        if (_planet is not { } pl || (p - pl.Center).LengthSquared() < 1)
            return null;
        var up = Vector3.Normalize(p - pl.Center);
        var (best, bi) = (-2f, -1);
        for (var i = 0; i < _deposits.Count; i++)
            if (Vector3.Dot(_dirs[i], up) is var dot && dot > best && _deposits[i].Left > 0)
                (best, bi) = (dot, i);
        if (bi < 0)
            return null;
        var dir = _dirs[bi];
        at = pl.Center + dir * (float)(pl.Radius + pl.HeightAt(dir.X, dir.Y, dir.Z));
        return _table.Kinds[_deposits[bi].Kind];
    }

    /// <summary>A planetoid's outcrops near the camera: each deposit's streak of rocks, set on the round ground.</summary>
    private void AddSphereOutcrops(Vector3 cam, List<FoliageInstance> into)
    {
        var pl = _planet!;
        var rel = cam - pl.Center;
        if (rel.LengthSquared() < 1)
            return;
        var up = Vector3.Normalize(rel);
        var r = (float)pl.Radius;
        var left = MaxDrawnOutcrops;
        for (var di = 0; di < _deposits.Count && left > 0; di++)
        {
            var d = _deposits[di];
            var kind = _table.Kinds[d.Kind];
            if (kind.OutcropSpacing <= 0)
                continue;
            var reach = (SphereDrawRadius + d.Rx * 1.25f) / r; // (as an angle)
            if (Vector3.Dot(_dirs[di], up) < MathF.Cos(MathF.Min(reach, MathF.PI)))
                continue;
            AddStreak(di, d, kind, ref left, into);
        }
    }

    /// <summary>
    /// A rock's place on a planetoid. The rock's mesh stands along +Y with its middle 0.45 of its size over its foot
    /// (FoliageMeshes.cs), so the foot goes where that middle lies as far out of the ground as on a flat map.
    /// </summary>
    private Vector3 SphereSpot(int face, float x, float z, float size, float lift = 0)
    {
        var pl = _planet!;
        var dir = DirOf(face, x, z);
        var ground = pl.Center + dir * (float)(pl.Radius + pl.HeightAt(dir.X, dir.Y, dir.Z));
        return ground + dir * ((0.45f - Sink) * size + lift) - new Vector3(0, 0.45f * size, 0);
    }

    /// <summary>How many deposits of each kind the world holds ("iron vein 2104, stone seam 1980"), for the world checker.</summary>
    public string Summary()
    {
        var n = new int[_table.Kinds.Count];
        foreach (var d in _deposits)
            n[d.Kind]++;
        var sb = new System.Text.StringBuilder();
        for (var k = 0; k < n.Length; k++)
            if (n[k] > 0)
                sb.Append(sb.Length > 0 ? ", " : "").Append(_table.Kinds[k].Name).Append(' ').Append(n[k]);
        return sb.Length > 0 ? sb.ToString() : "none";
    }
}
