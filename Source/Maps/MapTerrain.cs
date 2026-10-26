using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// How an op's boxes are placed on terrain (the "anchor" key on any op in a map file). Maps without terrain ignore it.
/// </summary>
public enum OpAnchor : byte
{
    /// <summary>The op's default: <see cref="Ground"/> for most ops, <see cref="Drape"/> for CoverLine, RuinWall and Rocks.</summary>
    Auto,

    /// <summary>Build as on flat ground, then raise the whole op (rigidly) to the lowest ground under its footprint.</summary>
    Ground,

    /// <summary>Raise each box separately to the lowest ground under that box (long walls, scattered pieces).</summary>
    Drape,

    /// <summary>Y values are absolute world heights (bridges over valleys, floating platforms).</summary>
    World,
}

/// <summary>Terrain on a map: the definition, the generated heightfield, ground queries and placing ops on the ground.</summary>
public sealed partial class Map
{
    /// <summary>The map file's "terrain" object (null: a flat map, the ground is y = 0).</summary>
    public TerrainSpec? TerrainSpec { get; private set; }

    /// <summary>The generated heightfield (null without terrain).</summary>
    public TerrainData? Terrain { get; private set; }

    /// <summary>Ground collision built from <see cref="Terrain"/>. Swappable (e.g. for a physics engine's heightfield).</summary>
    public ITerrainCollision? TerrainCollision { get; set; }

    public bool HasTerrain => TerrainCollision != null;

    /// <summary>
    /// Gives the map terrain. Must come before any op, because ops are placed on the ground as they run.
    /// </summary>
    public void SetTerrain(TerrainSpec? spec)
    {
        if (Solid.Count > 0 || Spawns.Count > 0 || Waypoints.Count > 0)
            throw new InvalidOperationException("SetTerrain must be called before any op");
        TerrainSpec = spec;
        Terrain = spec == null ? null : TerrainGen.Generate(spec, HalfX, HalfZ, Boundary);
        TerrainCollision = Terrain == null ? null : new HeightfieldCollision(Terrain);
    }

    /// <summary>Ground height at (x, z): the terrain, or 0 on flat maps.</summary>
    public float GroundHeight(float x, float z) => TerrainCollision?.HeightAt(x, z) ?? 0f;

    /// <summary>
    /// Ground under a point at height <paramref name="y"/>: the same as <see cref="GroundHeight(float, float)"/> except in a
    /// cave (TerrainVoids.cs), where it's the cave's floor for a point inside it.
    /// </summary>
    public float GroundHeight(float x, float z, float y) => TerrainCollision?.GroundBelow(x, z, y) ?? 0f;

    /// <summary>Does the terrain have holes or caves?</summary>
    public bool HasTerrainVoids => Terrain?.Voids != null;

    /// <summary>Is (x, z) over a terrain hole (no ground surface there)?</summary>
    public bool IsTerrainHole(float x, float z) => Terrain?.IsHole(x, z) ?? false;

    /// <summary>Is a terrain hole within <paramref name="radius"/> of (x, z)? (Foliage and clutter keep away from them.)</summary>
    public bool TerrainHoleNear(float x, float z, float radius) => Terrain?.HoleNear(x, z, radius) ?? false;

    /// <summary>Ground surface normal at (x, z) (straight up on flat maps).</summary>
    public Vector3 GroundNormal(float x, float z) => TerrainCollision?.NormalAt(x, z) ?? Vector3.UnitY;

    /// <summary>Ground slope at (x, z) in degrees.</summary>
    public float GroundSlope(float x, float z) => MathF.Acos(Math.Clamp(GroundNormal(x, z).Y, -1f, 1f)) * (180f / MathF.PI);

    /// <summary>Is the ground at (x, z) too steep to walk up (steeper than <see cref="World.MaxSlopeDeg"/>)?</summary>
    public bool IsSteep(float x, float z) => HasTerrain && GroundNormal(x, z).Y < World.MinWalkNormalY;

    /// <summary>Lowest and highest ground (0 and 0 on flat maps).</summary>
    public float LowestGround => Terrain?.MinHeight ?? 0f;

    public float HighestGround => Terrain?.MaxHeight ?? 0f;

    /// <summary>Standing on the terrain itself (not on a box) where it's too steep to stand: a bad spot for spawns and waypoints.</summary>
    private bool OnSteepGround(Vector3 feet) =>
        HasTerrain && MathF.Abs(feet.Y - GroundHeight(feet.X, feet.Z)) < 0.15f && IsSteep(feet.X, feet.Z);

    private bool BadSpot(Vector3 feet) => IsBlocked(feet) || OnSteepGround(feet);

    /// <summary>The lowest ground at 9 points of a footprint (corners, edge midpoints and centre).</summary>
    public float LowestGroundUnder(float x0, float z0, float x1, float z1)
    {
        var lo = float.MaxValue;
        for (var a = 0; a <= 2; a++)
        for (var b = 0; b <= 2; b++)
            lo = MathF.Min(lo, GroundHeight(x0 + (x1 - x0) * a * 0.5f, z0 + (z1 - z0) * b * 0.5f));
        return lo;
    }

    /// <summary>The anchor an op really uses (resolves <see cref="OpAnchor.Auto"/> per op).</summary>
    public static OpAnchor EffectiveAnchor(string op, OpAnchor anchor)
    {
        if (anchor != OpAnchor.Auto)
            return anchor;
        return op switch
        {
            nameof(Ground) or nameof(Perimeter) => OpAnchor.World, // they handle terrain themselves
            nameof(CoverLine) or nameof(RuinWall) or nameof(Rocks) => OpAnchor.Drape,
            _ => OpAnchor.Ground,
        };
    }

    // The anchor of the top-level op running right now (Spawn and Waypoint use it).
    private OpAnchor _topAnchor;
    private string _topName = "";

    /// <summary>Height to add to a spawn or waypoint at (x, z) made by the current op.</summary>
    private float PointGround(float x, float z) =>
        HasTerrain && EffectiveAnchor(_topName, _topAnchor) != OpAnchor.World ? GroundHeight(x, z) : 0f;

    /// <summary>Runs when a top-level op finishes: lifts the boxes it made onto the terrain.</summary>
    private void PlaceOnTerrain(string op, int firstBox, OpAnchor anchor)
    {
        if (!HasTerrain || Solid.Count == firstBox || op is nameof(PlaceProp) or nameof(Clutter) or nameof(PlaceBuilding))
            return; // (props and buildings stand themselves on the ground, see MapProps.cs and MapBuildings.cs)
        InvalidatePlaceSpace(firstBox); // the boxes from firstBox on are lifted below (a nested PlaceProp may have filed them)
        switch (EffectiveAnchor(op, anchor))
        {
            case OpAnchor.Ground:
            {
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                for (var i = firstBox; i < Solid.Count; i++)
                {
                    x0 = MathF.Min(x0, Solid[i].Min.X);
                    z0 = MathF.Min(z0, Solid[i].Min.Z);
                    x1 = MathF.Max(x1, Solid[i].Max.X);
                    z1 = MathF.Max(z1, Solid[i].Max.Z);
                }
                var dy = LowestGroundUnder(x0, z0, x1, z1);
                for (var i = firstBox; i < Solid.Count; i++)
                    Solid[i] = Solid[i] with { Center = Solid[i].Center + new Vector3(0, dy, 0) };
                break;
            }
            case OpAnchor.Drape:
                for (var i = firstBox; i < Solid.Count; i++)
                {
                    var b = Solid[i];
                    var dy = LowestGroundUnder(b.Min.X, b.Min.Z, b.Max.X, b.Max.Z);
                    Solid[i] = b with { Center = b.Center + new Vector3(0, dy, 0) };
                }
                break;
        }
    }

    /// <summary>Perimeter walls on terrain: from 1 m below the lowest ground along each edge to <paramref name="height"/> above the highest.</summary>
    private void TerrainPerimeter(float height, Vector3 color, float t)
    {
        (float Lo, float Hi) Span(float ax, float az, float bx, float bz)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            var n = Math.Max(2, (int)(MathF.Max(MathF.Abs(bx - ax), MathF.Abs(bz - az)) / Terrain!.Cell) + 1);
            for (var k = 0; k <= n; k++)
            {
                var g = GroundHeight(ax + (bx - ax) * k / n, az + (bz - az) * k / n);
                lo = MathF.Min(lo, g);
                hi = MathF.Max(hi, g);
            }
            return (lo - 1, hi + height);
        }
        var (nLo, nHi) = Span(-HalfX - t, -HalfZ - t / 2, HalfX + t, -HalfZ - t / 2);
        var (sLo, sHi) = Span(-HalfX - t, HalfZ + t / 2, HalfX + t, HalfZ + t / 2);
        var (wLo, wHi) = Span(-HalfX - t / 2, -HalfZ, -HalfX - t / 2, HalfZ);
        var (eLo, eHi) = Span(HalfX + t / 2, -HalfZ, HalfX + t / 2, HalfZ);
        Block(-HalfX - t, nLo, -HalfZ - t, HalfX + t, nHi, -HalfZ, color);
        Block(-HalfX - t, sLo, HalfZ, HalfX + t, sHi, HalfZ + t, color);
        Block(-HalfX - t, wLo, -HalfZ, -HalfX, wHi, HalfZ, color);
        Block(HalfX, eLo, -HalfZ, HalfX + t, eHi, HalfZ, color);
    }

    /// <summary>Lifts spawns and waypoints that ended up under the terrain onto it (part of <see cref="Validate"/>).</summary>
    private void SnapToGround(List<Vector3> points)
    {
        if (!HasTerrain)
            return;
        for (var i = 0; i < points.Count; i++)
        {
            var g = GroundHeight(points[i].X, points[i].Z, points[i].Y); // (a point in a cave stays on its floor)
            if (points[i].Y < g)
                points[i] = points[i] with { Y = g };
        }
    }
}
