using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>What the foliage no-go rules did on one map (logged, and printed by the self-test).</summary>
public sealed class FoliageNoGoStats
{
    /// <summary>Scattered plants dropped because they were indoors: under a roof or inside something (rule "cover").</summary>
    public int Indoors;

    /// <summary>Scattered plants dropped by the surface allowance (pavement, floors, tiles).</summary>
    public int Surface;

    /// <summary>Scattered plants dropped by explicit exclude zones.</summary>
    public int Excluded;

    /// <summary>Scattered plants dropped on paths that allow some growth ("density" above 0).</summary>
    public int PathThinned;

    /// <summary>Plants kept on partly allowed ground (busted pavement, thinned zones and paths).</summary>
    public int Sparse;

    /// <summary>Plants placed on top of a roof or box (include zones with "onTop").</summary>
    public int OnTop;

    /// <summary>Drops per surface name (the surface allowance's share of <see cref="Surface"/>).</summary>
    public readonly SortedDictionary<string, int> BySurface = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Time to build the query world (BVH over boxes, props and cosmetic meshes), and for the whole planting.</summary>
    public double BuildMs, PlantMs;

    /// <summary>Rays cast (cover and surface probes).</summary>
    public long Rays;

    public override string ToString() =>
        $"{Indoors} indoors, {Surface} on pavement/floors, {Excluded} in exclude zones, {PathThinned} on busted paths; {Sparse} sparse, {OnTop} on top; "
        + $"{Rays} rays, query world {BuildMs:0.0} ms, planting {PlantMs:0.0} ms";
}

public sealed partial class Map
{
    /// <summary>What the no-go rules did when this map's foliage was planted (null without foliage).</summary>
    public FoliageNoGoStats? FoliageStats;

    /// <summary>
    /// Self-test hook: plant foliage exactly as before the no-go rules existed (no automatic rules, no zones, no surface
    /// table, every path a full no-go), to report the "before" counts.
    /// </summary>
    [ThreadStatic]
    internal static bool LegacyFoliage;

    /// <summary>How far above a plant's base something counts as a roof (metres).</summary>
    public const float FoliageRoofReach = 20f;

    /// <summary>
    /// The automatic foliage no-go rules for one planting pass (see Maps/README.md, "Foliage", "No-go zones"). One query world
    /// (<see cref="PhysicsWorld"/>: a BVH over the map's boxes and prop colliders as the ops built them, plus collision
    /// triangles of the cosmetic meshes and models) answers two vertical rays per plant:
    /// <list type="bullet">
    /// <item><b>Cover:</b> a ray straight up from just above the plant's base, up to <see cref="FoliageRoofReach"/> m. Anything
    /// it hits is a roof, a ceiling, a floor above, a bridge deck or an awning, or the plant is inside a box or a closed mesh
    /// (a ray from inside a box hits it at once; from inside a mesh it hits the mesh's top). Tree trunks aren't in the query
    /// world, and leafy mesh shapes (green cones, spheres, icospheres, capsules: mesh-built trees) are left out, so trees
    /// never count as roofs.</item>
    /// <item><b>Surface:</b> a short ray down from just above the base finds a box or mesh lying on the ground there
    /// (pavement, a tiled market floor, a deck). Its material's allowance (<see cref="Foliage.SurfaceDefaults"/>, overridden
    /// by the map's "surfaces") thins or removes the plant. Without such a surface it's the terrain's surface type, or
    /// "Ground" on a flat map.</item>
    /// </list>
    /// Things lying on the ground (their top at most <see cref="FoliageNoGo.LieHeight"/> above the base) are surfaces, not cover.
    /// Everything is a pure function of the map (fixed BVH build, closest-hit queries with index tie breaks), so every PC
    /// places the same plants. Boxes are axis-aligned and the rays vertical, so box tests are exact comparisons.
    /// </summary>
    private sealed class FoliageNoGo
    {
        private readonly Map _map;
        private readonly PhysicsWorld _world;
        private readonly QueryFilter _cover, _surface;
        private readonly float[] _boxAllow;
        private readonly string[] _boxSurface;
        private readonly List<float> _meshAllow = new();
        private readonly List<string> _meshSurface = new();
        private readonly float[] _terrainAllow = new float[4];
        private readonly float _groundAllow;
        public readonly FoliageNoGoStats Stats;

        /// <summary>
        /// Things whose top is at most this far above a plant's base lie on the ground (a paving slab, a floor mesh draped
        /// over gentle terrain): they're surfaces (rule "surface"), not cover. The cover ray starts here.
        /// </summary>
        public const float LieHeight = 0.12f;

        public FoliageNoGo(Map map, FoliageSpec spec, FoliageNoGoStats stats)
        {
            var sw = Stopwatch.StartNew();
            _map = map;
            Stats = stats;
            _world = new PhysicsWorld(map);
            const QueryLayers layers = QueryLayers.Boxes | QueryLayers.Props | QueryLayers.Meshes;
            _cover = new QueryFilter(layers);
            _surface = map.GroundIndex >= 0 ? new QueryFilter(layers).Ignoring(new ShapeId(ShapeKind.Box, map.GroundIndex)) : new QueryFilter(layers);

            float Allow(string name) => spec.Surfaces.TryGetValue(name, out var v) ? v : Foliage.SurfaceDefaults.TryGetValue(name, out var d) ? d : 1f;
            var mats = map.Materials;
            _boxAllow = new float[map.Solid.Count];
            _boxSurface = new string[map.Solid.Count];
            for (var i = 0; i < _boxAllow.Length; i++)
            {
                _boxSurface[i] = mats[i].ToString();
                _boxAllow[i] = Allow(_boxSurface[i]);
            }
            foreach (var p in map.MeshPlacements)
            {
                if (IsCanopy(p))
                    continue; // mesh-built trees: plants grow under them
                if (p.Source is "boundary floor" or "boundary far ground")
                    continue; // the drawn ground of a map with an outline (MapEdges.cs): it is the ground
                MeshCollision col;
                try
                {
                    col = p.BuildCollision();
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
                {
                    continue; // no usable triangles (an asset without geometry): it can't cover anything
                }
                if (col.TriangleCount == 0)
                    continue;
                _world.AddMesh(TriangleMeshCollider.FromMesh(col, Matrix4x4.Identity));
                var name = p.Mesh != null && p.Layer >= 0 && p.Layer < (float)Material.Count ? ((Material)(int)p.Layer).ToString() : "Plain";
                _meshSurface.Add(name);
                _meshAllow.Add(Allow(name));
            }
            foreach (var t in Enum.GetValues<TerrainMaterial>())
                _terrainAllow[(int)t] = Allow(t.ToString());
            _groundAllow = Allow("Ground");
            _ = _world.Tree; // build the BVH now, so BuildMs covers it
            Stats.BuildMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>A leafy mesh shape (cone, sphere, icosphere, capsule in a green colour): a mesh-built tree's canopy.</summary>
        private static bool IsCanopy(MeshPlacement p)
        {
            if (p.Mesh == null)
                return false;
            var kind = p.Source.ToLowerInvariant();
            if (kind is not ("cone" or "sphere" or "icosphere" or "capsule"))
                return false;
            var c = p.Color;
            return c.Y > c.X + 0.04f && c.Y > c.Z + 0.04f;
        }

        /// <summary>Is there anything over (x, z) within the roof reach above <paramref name="y"/> (or is the point inside something)?</summary>
        public bool Covered(float x, float z, float y)
        {
            Stats.Rays++;
            return _world.Raycast(new Vector3(x, y + LieHeight, z), Vector3.UnitY, FoliageRoofReach, _cover) < FoliageRoofReach;
        }

        /// <summary>Like <see cref="Covered(float, float, float)"/>, also at four points <paramref name="r"/> away (a bush's footprint).</summary>
        public bool Covered(float x, float z, float y, float r)
        {
            if (Covered(x, z, y))
                return true;
            if (r <= 0f)
                return false;
            return Covered(x + r, z, y) || Covered(x - r, z, y) || Covered(x, z + r, y) || Covered(x, z - r, y);
        }

        /// <summary>The allowance of the surface the plant stands on (0 to 1) and its name.</summary>
        public float Allowance(float x, float z, float y, out string surface)
        {
            Stats.Rays++;
            // From just above the cover ray's start down to 40 cm below the base: a slab or floor mesh lying on (or sunk
            // into) the ground. Anything taller already counts as cover.
            if (_world.Raycast(new Vector3(x, y + LieHeight + 0.01f, z), -Vector3.UnitY, LieHeight + 0.41f, _surface, out var hit))
            {
                switch (hit.Shape.Kind)
                {
                    case ShapeKind.Box or ShapeKind.Prop when hit.Shape.Index < _boxAllow.Length:
                        surface = _boxSurface[hit.Shape.Index];
                        return _boxAllow[hit.Shape.Index];
                    case ShapeKind.Mesh when hit.Shape.Index < _meshAllow.Count:
                        surface = _meshSurface[hit.Shape.Index];
                        return _meshAllow[hit.Shape.Index];
                }
            }
            if (_map.Terrain is { } t)
            {
                t.Locate(x, z, out var i, out var j, out var tx, out var tz);
                if (tx >= 0.5f)
                    i++;
                if (tz >= 0.5f)
                    j++;
                i = Math.Clamp(i, 0, t.CountX - 1);
                j = Math.Clamp(j, 0, t.CountZ - 1);
                var m = Math.Min((int)t.MaterialIds[j * t.CountX + i], 3);
                surface = ((TerrainMaterial)m).ToString();
                return _terrainAllow[m];
            }
            surface = "Ground";
            return _groundAllow;
        }

        /// <summary>
        /// The top of whatever solid or mesh is at (x, z) (a roof, a planter box), for include zones with "onTop". Null if
        /// there's nothing above the ground there, or its top is too steep to grow on.
        /// </summary>
        public float? TopAt(float x, float z, float ground)
        {
            Stats.Rays++;
            const float from = 60f;
            if (!_world.Raycast(new Vector3(x, ground + from, z), -Vector3.UnitY, from - 0.05f, _surface, out var hit))
                return null;
            if (hit.Normal.Y < World.MinWalkNormalY)
                return null;
            return hit.Point.Y;
        }
    }
}
