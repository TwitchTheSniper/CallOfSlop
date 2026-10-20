using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The collision world of one map: every static collider (map boxes, prop collider boxes, extra oriented boxes and triangle
/// meshes) in one <see cref="Bvh"/>, the terrain heightfield (through <see cref="ITerrainCollision"/>) and the map's movers
/// (posed per tick through <see cref="MoverPhysics"/>). Implements <see cref="IWorldQuery"/>.
///
/// <para><b>Same answers as before.</b> Ray casts visit boxes, then the terrain, then movers, with the same box test and the
/// same rules (<see cref="Box.Raycast"/>, the terrain only when nothing was hit at distance 0) as the old scans, so
/// distances match them bit for bit; the classic movement walks <see cref="CollectBoxes(float, float, float, float)"/> in map
/// order. The "Collision parity" self-test compares everything against the old code on every map.</para>
///
/// <para><b>Deterministic.</b> The BVH build is a pure function of the input; queries pick the closest hit and break ties by
/// the lowest index; gathered shape lists come out sorted.</para>
///
/// <para><b>Dynamic parts.</b> A broken prop's boxes are switched off (<see cref="UpdateBox"/>); restored props switch back on
/// (<see cref="RefreshBoxes"/>). Movers are posed at the query's tick (<see cref="QueryFilter.Tick"/>).</para>
/// </summary>
public sealed class PhysicsWorld : IWorldQuery
{
    private readonly Map? _map;
    private readonly Box[] _boxes;
    private readonly bool[] _isProp;
    private readonly bool[] _enabled;
    private readonly int _boxCount;
    private readonly List<Obb> _obbs = new();
    private readonly List<IMeshCollider> _meshes = new();
    private readonly Box[] _sweeps;
    private Bvh _tree = null!;
    private bool _dirty = true;
    private int[] _collect = new int[64];
    private readonly List<ContactShape> _gather = new();
    private readonly List<int> _sortScratch = new();

    public ITerrainCollision? Terrain { get; }

    /// <summary>Playable half extents (the arena clamp).</summary>
    public float HalfX { get; }

    public float HalfZ { get; }

    /// <summary>Gravity for bodies in this world (the swept mover reads "up" from it).</summary>
    public IGravityField Gravity { get; set; } = UniformGravity.Down;

    /// <summary>How long the last BVH build took (ms).</summary>
    public double BuildMs { get; private set; }

    /// <summary>The map this world was built from (null for a hand-built test world).</summary>
    public Map? Map => _map;

    /// <summary>The map's boxes, in map order (indices from <see cref="CollectBoxes(float, float, float, float)"/> point here).</summary>
    public ReadOnlySpan<Box> Boxes => _boxes;

    public int BoxCount => _boxCount;
    public int ObbCount => _obbs.Count;
    public int MeshCount => _meshes.Count;

    /// <summary>Static items in the BVH (boxes, oriented boxes, meshes).</summary>
    public int StaticCount => _boxCount + _obbs.Count + _meshes.Count;

    public Bvh Tree
    {
        get
        {
            EnsureTree();
            return _tree;
        }
    }

    public bool HasMovers => _map is { } m && m.Movers.Count > 0;

    public PhysicsWorld(Map map)
        : this(map.Solid, map.TerrainCollision, map.HalfX, map.HalfZ, map)
    {
    }

    /// <summary>A world from a list of boxes (self-tests: shuffled copies, hand-built scenes). No movers, no props.</summary>
    public PhysicsWorld(IReadOnlyList<Box> boxes, ITerrainCollision? terrain, float halfX, float halfZ)
        : this(boxes, terrain, halfX, halfZ, null)
    {
    }

    private PhysicsWorld(IReadOnlyList<Box> boxes, ITerrainCollision? terrain, float halfX, float halfZ, Map? map)
    {
        _map = map;
        _boxCount = boxes.Count;
        _boxes = new Box[_boxCount];
        _isProp = new bool[_boxCount];
        _enabled = new bool[_boxCount];
        for (var i = 0; i < _boxCount; i++)
        {
            _boxes[i] = boxes[i];
            _isProp[i] = map != null && map.IsPropCollider(i);
            _enabled[i] = !Parked(boxes[i]);
        }
        Terrain = terrain;
        HalfX = halfX;
        HalfZ = halfZ;
        _sweeps = map != null ? MoverPhysics.SweepPillars(map) : [];
        EnsureTree();
    }

    /// <summary>Is this a snapshot of <paramref name="map"/> as it is now? (Same guard the collision grid used.)</summary>
    public bool IsFor(Map map) => ReferenceEquals(map, _map) && map.Solid.Count == _boxCount && ReferenceEquals(map.TerrainCollision, Terrain);

    /// <summary>A broken prop's collider box is parked far under the world: such boxes are switched off.</summary>
    private static bool Parked(in Box b) => b.Center.Y < -500f;

    private void EnsureTree()
    {
        if (!_dirty)
            return;
        var sw = Stopwatch.StartNew();
        var n = StaticCount;
        var mins = new Vector3[n];
        var maxs = new Vector3[n];
        for (var i = 0; i < n; i++)
            ItemBounds(i, out mins[i], out maxs[i]);
        _tree = new Bvh(mins, maxs);
        _dirty = false;
        BuildMs = sw.Elapsed.TotalMilliseconds;
    }

    private void ItemBounds(int item, out Vector3 min, out Vector3 max)
    {
        if (item < _boxCount)
        {
            min = _boxes[item].Min;
            max = _boxes[item].Max;
        }
        else if (item < _boxCount + _obbs.Count)
            _obbs[item - _boxCount].Bounds(out min, out max);
        else
        {
            var m = _meshes[item - _boxCount - _obbs.Count];
            min = m.Min;
            max = m.Max;
        }
    }

    // ---- Changes ----------------------------------------------------------------------------------------------

    /// <summary>Box <paramref name="index"/> changed (a prop broke: its box is parked under the world and switched off).</summary>
    public void UpdateBox(int index, Box box)
    {
        _boxes[index] = box;
        var on = !Parked(box);
        var wasOn = _enabled[index];
        _enabled[index] = on;
        if (on)
            Refit(); // a box that really moved: node bounds follow it
        else if (!wasOn)
            return;
    }

    /// <summary>Reads every box from the map again (broken props restored) and switches them back on.</summary>
    public void RefreshBoxes()
    {
        if (_map == null)
            return;
        for (var i = 0; i < _boxCount && i < _map.Solid.Count; i++)
        {
            _boxes[i] = _map.Solid[i];
            _enabled[i] = !Parked(_boxes[i]);
        }
        Refit();
    }

    private void Refit()
    {
        if (_dirty)
            return; // rebuilt on next use anyway
        var n = StaticCount;
        var mins = new Vector3[n];
        var maxs = new Vector3[n];
        for (var i = 0; i < n; i++)
            ItemBounds(i, out mins[i], out maxs[i]);
        _tree.Refit(mins, maxs);
    }

    /// <summary>Adds a static oriented box (ramps, turned props). Returns its index (<see cref="ShapeKind.Obb"/> ids).</summary>
    public int AddObb(in Obb box)
    {
        _obbs.Add(box);
        _dirty = true;
        return _obbs.Count - 1;
    }

    /// <summary>Adds a static triangle mesh (from the mesh pipeline). Returns its index (<see cref="ShapeKind.Mesh"/> ids).</summary>
    public int AddMesh(IMeshCollider mesh)
    {
        _meshes.Add(mesh);
        _dirty = true;
        return _meshes.Count - 1;
    }

    public bool IsProp(int box) => _isProp[box];

    public bool IsEnabled(int box) => _enabled[box];

    public Obb GetObb(int index) => _obbs[index];

    public IMeshCollider GetMesh(int index) => _meshes[index];

    private static bool Ignored(ShapeId[]? ignore, ShapeKind kind, int index)
    {
        if (ignore == null)
            return false;
        foreach (var id in ignore)
            if (id.Kind == kind && id.Index == index)
                return true;
        return false;
    }

    private bool BoxWanted(int i, QueryLayers layers, ShapeId[]? ignore)
    {
        if (!_enabled[i])
            return false;
        var prop = _isProp[i];
        if ((layers & (prop ? QueryLayers.Props : QueryLayers.Boxes)) == 0)
            return false;
        return ignore == null || !Ignored(ignore, prop ? ShapeKind.Prop : ShapeKind.Box, i);
    }

    // ---- Rays -------------------------------------------------------------------------------------------------

    private struct RayVisitor : IBvhRayVisitor
    {
        public PhysicsWorld W;
        public Vector3 O, D;
        public QueryLayers Layers;
        public ShapeId[]? Ignore;
        public int Item, Sub;

        public float Visit(int item, float best)
        {
            var w = W;
            float? hit;
            var sub = 0;
            if (item < w._boxCount)
            {
                if (!w.BoxWanted(item, Layers, Ignore))
                    return best;
                hit = w._boxes[item].Raycast(O, D, best);
            }
            else if (item < w._boxCount + w._obbs.Count)
            {
                var k = item - w._boxCount;
                if ((Layers & QueryLayers.Obbs) == 0 || Ignored(Ignore, ShapeKind.Obb, k))
                    return best;
                hit = Geometry.RayObb(w._obbs[k], O, D, best);
            }
            else
            {
                var k = item - w._boxCount - w._obbs.Count;
                if ((Layers & QueryLayers.Meshes) == 0 || Ignored(Ignore, ShapeKind.Mesh, k))
                    return best;
                var mesh = w._meshes[k];
                var tv = new TriangleRayVisitor { Mesh = mesh, O = O, D = D, Tri = -1 };
                var t = mesh.Tree.Raycast(O, D, best, ref tv);
                hit = tv.Tri >= 0 ? t : null;
                sub = tv.Tri;
            }
            if (hit is { } v && (v < best || (v == best && Item >= 0 && item < Item)))
            {
                Item = item;
                Sub = sub;
                return v;
            }
            return best;
        }
    }

    private struct TriangleRayVisitor : IBvhRayVisitor
    {
        public IMeshCollider Mesh;
        public Vector3 O, D;
        public int Tri;

        public float Visit(int item, float best)
        {
            Mesh.Triangle(item, out var a, out var b, out var c);
            if (Geometry.RayTriangle(O, D, a, b, c, best) is { } t && (t < best || (t == best && Tri >= 0 && item < Tri)))
            {
                Tri = item;
                return t;
            }
            return best;
        }
    }

    public float Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter) => Cast(origin, dir, maxDist, filter, out _, false);

    public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
    {
        Cast(origin, dir, maxDist, filter, out hit, true);
        return hit.Shape.Kind != ShapeKind.None;
    }

    private float Cast(Vector3 o, Vector3 d, float maxDist, in QueryFilter filter, out RayHit hit, bool details)
    {
        hit = default;
        var best = maxDist;
        var kind = ShapeKind.None;
        int index = 0, sub = 0;
        if ((filter.Layers & QueryLayers.StaticShapes) != 0 && StaticCount > 0)
        {
            EnsureTree();
            var v = new RayVisitor { W = this, O = o, D = d, Layers = filter.Layers, Ignore = filter.Ignore, Item = -1 };
            best = _tree.Raycast(o, d, best, ref v);
            if (v.Item >= 0)
            {
                if (v.Item < _boxCount)
                {
                    kind = _isProp[v.Item] ? ShapeKind.Prop : ShapeKind.Box;
                    index = v.Item;
                }
                else if (v.Item < _boxCount + _obbs.Count)
                {
                    kind = ShapeKind.Obb;
                    index = v.Item - _boxCount;
                }
                else
                {
                    kind = ShapeKind.Mesh;
                    index = v.Item - _boxCount - _obbs.Count;
                    sub = v.Sub;
                }
            }
        }
        if ((filter.Layers & QueryLayers.Terrain) != 0 && Terrain != null && best > 0 && !filter.Ignores(ShapeKind.Terrain, 0))
        {
            var t = Terrain.Raycast(o, d, best);
            if (t < best)
            {
                best = t;
                kind = ShapeKind.Terrain;
                index = 0;
                sub = 0;
            }
        }
        if ((filter.Layers & QueryLayers.Movers) != 0 && HasMovers && best > 0)
        {
            var boxes = MoverPhysics.At(_map!, filter.MoverTick);
            for (var i = 0; i < boxes.Length; i++)
            {
                if (filter.Ignore != null && Ignored(filter.Ignore, ShapeKind.Mover, boxes[i].Mover))
                    continue;
                if (MoverPhysics.Raycast(boxes[i], o, d, best) is { } t && t < best)
                {
                    best = t;
                    kind = ShapeKind.Mover;
                    index = boxes[i].Mover;
                    sub = i;
                }
            }
        }
        if ((filter.Layers & QueryLayers.MoverSweeps) != 0 && _sweeps.Length > 0 && best > 0)
        {
            for (var i = 0; i < _sweeps.Length; i++)
                if (_sweeps[i].Raycast(o, d, best) is { } t && t < best)
                {
                    best = t;
                    kind = ShapeKind.MoverSweep;
                    index = i;
                    sub = 0;
                }
        }
        if (details && kind != ShapeKind.None)
        {
            hit.Distance = best;
            hit.Point = o + d * best;
            hit.Shape = new ShapeId(kind, index, sub);
            hit.Normal = kind switch
            {
                ShapeKind.Box or ShapeKind.Prop => Geometry.BoxEntryNormal(_boxes[index].Min, _boxes[index].Max, o, d, best),
                ShapeKind.Obb => Geometry.ObbEntryNormal(_obbs[index], o, d, best),
                ShapeKind.Mesh => TriangleHitNormal(_meshes[index], sub, d),
                ShapeKind.Terrain => best <= 0 ? -d : Terrain!.NormalAt(hit.Point.X, hit.Point.Z),
                ShapeKind.Mover => MoverNormal(filter.MoverTick, sub, o, d, best),
                _ => Geometry.BoxEntryNormal(_sweeps[index].Min, _sweeps[index].Max, o, d, best),
            };
        }
        return best;
    }

    private static Vector3 TriangleHitNormal(IMeshCollider mesh, int tri, Vector3 dir)
    {
        mesh.Triangle(tri, out var a, out var b, out var c);
        var n = Geometry.TriangleNormal(a, b, c);
        return Vector3.Dot(n, dir) > 0 ? -n : n;
    }

    private Vector3 MoverNormal(double tick, int part, Vector3 o, Vector3 d, float t)
    {
        var b = MoverPhysics.At(_map!, tick)[part];
        return Geometry.ObbEntryNormal(Obb.FromYaw(b.Center, b.Half, b.Cos, b.Sin), o, d, t);
    }

    // ---- Shape casts --------------------------------------------------------------------------------------------

    public bool SphereCast(Vector3 origin, float radius, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit) =>
        CapsuleCast(new Capsule(origin, origin, radius), dir, maxDist, filter, out hit);

    public bool CapsuleCast(in Capsule capsule, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
    {
        hit = default;
        var end = dir * MathF.Max(0, maxDist);
        var lo = Vector3.Min(capsule.Min, capsule.Min + end) - new Vector3(CapsuleContact.Skin);
        var hi = Vector3.Max(capsule.Max, capsule.Max + end) + new Vector3(CapsuleContact.Skin);
        Gather(lo, hi, filter, _gather);
        var shapes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_gather);
        if (!CapsuleContact.Cast(shapes, capsule.A, capsule.B, capsule.Radius, dir, maxDist, out var toi, out var n, out var p, out var i))
            return false;
        hit = new RayHit { Distance = toi, Normal = n, Point = p, Shape = shapes[i].Id };
        return true;
    }

    // ---- Overlaps -------------------------------------------------------------------------------------------------

    private struct BodyVisitor : IBvhOverlapVisitor
    {
        public PhysicsWorld W;
        public Vector3 Feet;
        public float Height;
        public QueryLayers Layers;
        public ShapeId[]? Ignore;

        public bool Visit(int item)
        {
            var w = W;
            const float r = World.PlayerRadius;
            if (item < w._boxCount)
            {
                if (!w.BoxWanted(item, Layers, Ignore))
                    return false;
                var b = w._boxes[item];
                // Exactly the classic body test (World.BlockedByBoxes before the physics module).
                return Feet.Y < b.Max.Y - 0.001f && Feet.Y + Height > b.Min.Y + 0.001f
                    && Feet.X + r > b.Min.X && Feet.X - r < b.Max.X && Feet.Z + r > b.Min.Z && Feet.Z - r < b.Max.Z;
            }
            var body = new Obb(Feet + new Vector3(0, Height / 2, 0), new Vector3(r, Height / 2 - 0.001f, r), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
            return w.StaticItemOverlapsObb(item, body, Layers, Ignore);
        }
    }

    private bool StaticItemOverlapsObb(int item, in Obb box, QueryLayers layers, ShapeId[]? ignore)
    {
        if (item < _boxCount)
            return BoxWanted(item, layers, ignore) && Geometry.ObbOverlap(box, Obb.FromBox(_boxes[item]));
        if (item < _boxCount + _obbs.Count)
        {
            var k = item - _boxCount;
            return (layers & QueryLayers.Obbs) != 0 && !Ignored(ignore, ShapeKind.Obb, k) && Geometry.ObbOverlap(box, _obbs[k]);
        }
        var m = item - _boxCount - _obbs.Count;
        if ((layers & QueryLayers.Meshes) == 0 || Ignored(ignore, ShapeKind.Mesh, m))
            return false;
        var mesh = _meshes[m];
        box.Bounds(out var lo, out var hi);
        var v = new TriangleObbVisitor { Mesh = mesh, Box = box };
        return mesh.Tree.Overlap(lo, hi, ref v);
    }

    private struct TriangleObbVisitor : IBvhOverlapVisitor
    {
        public IMeshCollider Mesh;
        public Obb Box;

        public bool Visit(int item)
        {
            Mesh.Triangle(item, out var a, out var b, out var c);
            return Geometry.ObbTriangleOverlap(Box, a, b, c);
        }
    }

    public bool OverlapBody(Vector3 feet, float height, in QueryFilter filter)
    {
        if ((filter.Layers & QueryLayers.Terrain) != 0 && Terrain != null && Terrain.HeightAt(feet.X, feet.Z) > feet.Y + 0.05f)
            return true;
        if ((filter.Layers & QueryLayers.StaticShapes) != 0 && StaticCount > 0)
        {
            EnsureTree();
            const float r = World.PlayerRadius;
            var v = new BodyVisitor { W = this, Feet = feet, Height = height, Layers = filter.Layers, Ignore = filter.Ignore };
            if (_tree.Overlap(new Vector3(feet.X - r, feet.Y, feet.Z - r), new Vector3(feet.X + r, feet.Y + height, feet.Z + r), ref v))
                return true;
        }
        if ((filter.Layers & QueryLayers.Movers) != 0 && HasMovers)
        {
            foreach (ref readonly var b in MoverPhysics.At(_map!, filter.MoverTick))
                if (feet.Y < b.Top - 0.001f && feet.Y + height > b.Bottom + 0.001f && !Ignored(filter.Ignore, ShapeKind.Mover, b.Mover)
                    && MoverPhysics.CircleOverlap(b, feet.X, feet.Z, World.PlayerRadius, out _))
                    return true;
        }
        if ((filter.Layers & QueryLayers.MoverSweeps) != 0)
            foreach (var s in _sweeps)
                if (feet.Y < s.Max.Y - 0.001f && feet.Y + height > s.Min.Y + 0.001f && feet.X + World.PlayerRadius > s.Min.X && feet.X - World.PlayerRadius < s.Max.X
                    && feet.Z + World.PlayerRadius > s.Min.Z && feet.Z - World.PlayerRadius < s.Max.Z)
                    return true;
        return false;
    }

    /// <summary>The classic body check against static geometry and the terrain (what <see cref="World.Blocked"/> answers).</summary>
    public bool Blocked(Vector3 feet, float height = World.PlayerHeight) => OverlapBody(feet, height, new QueryFilter(QueryLayers.Static));

    /// <summary>The classic body check against boxes only (terrain ignored): <see cref="World.BlockedByBoxes"/>.</summary>
    public bool BlockedByBoxes(Vector3 feet, float height = World.PlayerHeight) => OverlapBody(feet, height, new QueryFilter(QueryLayers.StaticShapes));

    private struct PointVisitor : IBvhOverlapVisitor
    {
        public PhysicsWorld W;
        public Vector3 P;
        public float R;
        public QueryLayers Layers;
        public ShapeId[]? Ignore;

        public bool Visit(int item)
        {
            var w = W;
            var p = P;
            var r = R;
            if (item < w._boxCount)
            {
                if (!w.BoxWanted(item, Layers, Ignore))
                    return false;
                var b = w._boxes[item];
                // Exactly the grenade test from before the physics module.
                return p.X > b.Min.X - r && p.X < b.Max.X + r && p.Y > b.Min.Y - r && p.Y < b.Max.Y + r && p.Z > b.Min.Z - r && p.Z < b.Max.Z + r;
            }
            if (item < w._boxCount + w._obbs.Count)
            {
                var k = item - w._boxCount;
                if ((Layers & QueryLayers.Obbs) == 0 || Ignored(Ignore, ShapeKind.Obb, k))
                    return false;
                var o = w._obbs[k];
                var l = Vector3.Abs(o.ToLocal(p));
                return l.X < o.Half.X + r && l.Y < o.Half.Y + r && l.Z < o.Half.Z + r;
            }
            var m = item - w._boxCount - w._obbs.Count;
            if ((Layers & QueryLayers.Meshes) == 0 || Ignored(Ignore, ShapeKind.Mesh, m))
                return false;
            var mesh = w._meshes[m];
            var tv = new TrianglePointVisitor { Mesh = mesh, P = p, R2 = r * r };
            return mesh.Tree.Overlap(p - new Vector3(r), p + new Vector3(r), ref tv);
        }
    }

    private struct TrianglePointVisitor : IBvhOverlapVisitor
    {
        public IMeshCollider Mesh;
        public Vector3 P;
        public float R2;

        public bool Visit(int item)
        {
            Mesh.Triangle(item, out var a, out var b, out var c);
            return Vector3.DistanceSquared(P, Geometry.ClosestPointTriangle(P, a, b, c)) < R2;
        }
    }

    public bool PointInside(Vector3 point, float radius, in QueryFilter filter)
    {
        if ((filter.Layers & QueryLayers.StaticShapes) != 0 && StaticCount > 0)
        {
            EnsureTree();
            var v = new PointVisitor { W = this, P = point, R = radius, Layers = filter.Layers, Ignore = filter.Ignore };
            if (_tree.Overlap(point - new Vector3(radius), point + new Vector3(radius), ref v))
                return true;
        }
        if ((filter.Layers & QueryLayers.Terrain) != 0 && Terrain != null && point.Y - radius < Terrain.HeightAt(point.X, point.Z))
            return true;
        if ((filter.Layers & QueryLayers.Movers) != 0 && HasMovers)
        {
            foreach (ref readonly var b in MoverPhysics.At(_map!, filter.MoverTick))
            {
                if (filter.Ignore != null && Ignored(filter.Ignore, ShapeKind.Mover, b.Mover))
                    continue;
                // Exactly MoverPhysics.PointInside.
                var d = point - b.Center;
                float lx = d.X * b.Cos + d.Z * b.Sin, lz = -d.X * b.Sin + d.Z * b.Cos;
                if (MathF.Abs(lx) < b.Half.X + radius && MathF.Abs(d.Y) < b.Half.Y + radius && MathF.Abs(lz) < b.Half.Z + radius)
                    return true;
            }
        }
        return false;
    }

    private struct ObbVisitor : IBvhOverlapVisitor
    {
        public PhysicsWorld W;
        public Obb Box;
        public QueryLayers Layers;
        public ShapeId[]? Ignore;

        public bool Visit(int item) => W.StaticItemOverlapsObb(item, Box, Layers, Ignore);
    }

    public bool OverlapBox(in Obb box, in QueryFilter filter)
    {
        box.Bounds(out var lo, out var hi);
        if ((filter.Layers & QueryLayers.StaticShapes) != 0 && StaticCount > 0)
        {
            EnsureTree();
            var v = new ObbVisitor { W = this, Box = box, Layers = filter.Layers, Ignore = filter.Ignore };
            if (_tree.Overlap(lo, hi, ref v))
                return true;
        }
        if ((filter.Layers & QueryLayers.Terrain) != 0 && Terrain != null)
        {
            if (Terrain.HeightAt(box.Center.X, box.Center.Z) > box.Center.Y)
                return true; // the box's middle is underground
            _gather.Clear();
            AddTerrain(lo, hi, _gather);
            foreach (var t in _gather)
                if (Geometry.ObbTriangleOverlap(box, t.A, t.B, t.C))
                    return true;
        }
        if ((filter.Layers & QueryLayers.Movers) != 0 && HasMovers)
            foreach (ref readonly var b in MoverPhysics.At(_map!, filter.MoverTick))
                if (!Ignored(filter.Ignore, ShapeKind.Mover, b.Mover) && Geometry.ObbOverlap(box, Obb.FromYaw(b.Center, b.Half, b.Cos, b.Sin)))
                    return true;
        if ((filter.Layers & QueryLayers.MoverSweeps) != 0)
            foreach (var s in _sweeps)
                if (Geometry.ObbOverlap(box, Obb.FromBox(s)))
                    return true;
        return false;
    }

    public bool OverlapCapsule(in Capsule capsule, in QueryFilter filter)
    {
        if ((filter.Layers & QueryLayers.Terrain) != 0 && Terrain != null)
        {
            var low = capsule.A.Y < capsule.B.Y ? capsule.A : capsule.B;
            if (Terrain.HeightAt(low.X, low.Z) > low.Y)
                return true; // the core's lower end is underground
        }
        Gather(capsule.Min, capsule.Max, filter, _gather);
        return CapsuleContact.Overlaps(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_gather), capsule.A, capsule.B, capsule.Radius);
    }

    // ---- Lists for movement code ------------------------------------------------------------------------------

    private struct CollectVisitor : IBvhOverlapVisitor
    {
        public PhysicsWorld W;
        public int Count;

        public bool Visit(int item)
        {
            var w = W;
            if (item >= w._boxCount || !w._enabled[item])
                return false;
            if (Count == w._collect.Length)
                Array.Resize(ref w._collect, Count * 2);
            w._collect[Count++] = item;
            return false;
        }
    }

    /// <summary>
    /// Every enabled map box whose footprint could touch the rectangle x0..x1, z0..z1 (any height), as indices into
    /// <see cref="Boxes"/> in map order with no repeats: a superset of the boxes really touching it, so a loop over these
    /// visits the boxes that matter in the same order as a loop over the whole map. The answer lives in a buffer the next
    /// call reuses: finish with it first.
    /// </summary>
    public ReadOnlySpan<int> CollectBoxes(float x0, float z0, float x1, float z1) =>
        CollectBoxes(new Vector3(x0, -1e30f, z0), new Vector3(x1, 1e30f, z1));

    /// <summary>Every enabled map box whose bounds could touch min..max, in map order (see the 2D overload).</summary>
    public ReadOnlySpan<int> CollectBoxes(Vector3 min, Vector3 max)
    {
        if (_boxCount == 0)
            return default;
        EnsureTree();
        var v = new CollectVisitor { W = this };
        _tree.Overlap(min, max, ref v);
        var span = _collect.AsSpan(0, v.Count);
        span.Sort();
        return span;
    }

    /// <summary>
    /// Tops of the boxes whose footprint strictly contains (x, z), in map order, lower than <paramref name="below"/> (bot
    /// navigation's column probe). Returns how many were written.
    /// </summary>
    public int TopsAt(float x, float z, float below, Span<float> into)
    {
        var n = 0;
        foreach (var i in CollectBoxes(x, z, x, z))
        {
            if (n >= into.Length)
                break;
            var b = _boxes[i];
            if (x > b.Min.X && x < b.Max.X && z > b.Min.Z && z < b.Max.Z && b.Max.Y < below)
                into[n++] = b.Max.Y;
        }
        return n;
    }

    private struct GatherVisitor : IBvhOverlapVisitor
    {
        public List<int> Items;

        public bool Visit(int item)
        {
            Items.Add(item);
            return false;
        }
    }

    /// <summary>
    /// Every convex piece (boxes, mesh and terrain triangles, posed mover boxes) whose bounds reach min..max, for capsule
    /// queries. Deterministic order: static items by index, then terrain cells row by row, then movers.
    /// </summary>
    public void Gather(Vector3 min, Vector3 max, in QueryFilter filter, List<ContactShape> into)
    {
        into.Clear();
        var layers = filter.Layers;
        if ((layers & QueryLayers.StaticShapes) != 0 && StaticCount > 0)
        {
            EnsureTree();
            _sortScratch.Clear();
            var v = new GatherVisitor { Items = _sortScratch };
            _tree.Overlap(min, max, ref v);
            _sortScratch.Sort();
            foreach (var item in _sortScratch)
            {
                if (item < _boxCount)
                {
                    if (BoxWanted(item, layers, filter.Ignore))
                        into.Add(ContactShape.FromObb(Obb.FromBox(_boxes[item]), new ShapeId(_isProp[item] ? ShapeKind.Prop : ShapeKind.Box, item)));
                }
                else if (item < _boxCount + _obbs.Count)
                {
                    var k = item - _boxCount;
                    if ((layers & QueryLayers.Obbs) != 0 && !Ignored(filter.Ignore, ShapeKind.Obb, k))
                        into.Add(ContactShape.FromObb(_obbs[k], new ShapeId(ShapeKind.Obb, k)));
                }
                else
                {
                    var k = item - _boxCount - _obbs.Count;
                    if ((layers & QueryLayers.Meshes) != 0 && !Ignored(filter.Ignore, ShapeKind.Mesh, k))
                        AddMeshTriangles(k, min, max, into);
                }
            }
        }
        if ((layers & QueryLayers.Terrain) != 0 && Terrain != null && !filter.Ignores(ShapeKind.Terrain, 0))
            AddTerrain(min, max, into);
        if ((layers & QueryLayers.Movers) != 0 && HasMovers)
        {
            var boxes = MoverPhysics.At(_map!, filter.MoverTick);
            for (var i = 0; i < boxes.Length; i++)
            {
                ref readonly var b = ref boxes[i];
                if (Ignored(filter.Ignore, ShapeKind.Mover, b.Mover))
                    continue;
                var s = ContactShape.FromObb(Obb.FromYaw(b.Center, b.Half, b.Cos, b.Sin), new ShapeId(ShapeKind.Mover, b.Mover, i));
                if (!(s.Min.X > max.X || s.Max.X < min.X || s.Min.Y > max.Y || s.Max.Y < min.Y || s.Min.Z > max.Z || s.Max.Z < min.Z))
                    into.Add(s);
            }
        }
        if ((layers & QueryLayers.MoverSweeps) != 0)
            for (var i = 0; i < _sweeps.Length; i++)
            {
                var s = _sweeps[i];
                if (!(s.Min.X > max.X || s.Max.X < min.X || s.Min.Y > max.Y || s.Max.Y < min.Y || s.Min.Z > max.Z || s.Max.Z < min.Z))
                    into.Add(ContactShape.FromObb(Obb.FromBox(s), new ShapeId(ShapeKind.MoverSweep, i)));
            }
    }

    private void AddMeshTriangles(int meshIndex, Vector3 min, Vector3 max, List<ContactShape> into)
    {
        var mesh = _meshes[meshIndex];
        var tris = new List<int>();
        var v = new GatherVisitor { Items = tris };
        mesh.Tree.Overlap(min, max, ref v);
        tris.Sort();
        foreach (var t in tris)
        {
            mesh.Triangle(t, out var a, out var b, out var c);
            into.Add(ContactShape.FromTriangle(a, b, c, new ShapeId(ShapeKind.Mesh, meshIndex, t)));
        }
    }

    /// <summary>The terrain's triangles under min..max (two per cell, split like the renderer splits them), front faces up.</summary>
    private void AddTerrain(Vector3 min, Vector3 max, List<ContactShape> into)
    {
        var d = Terrain!.Data;
        var i0 = Math.Clamp((int)MathF.Floor((min.X - d.OriginX) / d.Cell), 0, d.CountX - 2);
        var i1 = Math.Clamp((int)MathF.Floor((max.X - d.OriginX) / d.Cell), 0, d.CountX - 2);
        var j0 = Math.Clamp((int)MathF.Floor((min.Z - d.OriginZ) / d.Cell), 0, d.CountZ - 2);
        var j1 = Math.Clamp((int)MathF.Floor((max.Z - d.OriginZ) / d.Cell), 0, d.CountZ - 2);
        if (max.Y < d.MinHeight || min.Y > d.MaxHeight + 1)
            return;
        for (var j = j0; j <= j1; j++)
            for (var i = i0; i <= i1; i++)
            {
                var k = j * d.CountX + i;
                float ha = d.Heights[k], hb = d.Heights[k + 1], hc = d.Heights[k + d.CountX], hd = d.Heights[k + d.CountX + 1];
                var lo = MathF.Min(MathF.Min(ha, hb), MathF.Min(hc, hd));
                var hi = MathF.Max(MathF.Max(ha, hb), MathF.Max(hc, hd));
                if (hi < min.Y || lo > max.Y)
                    continue;
                float x0 = d.OriginX + i * d.Cell, x1 = x0 + d.Cell, z0 = d.OriginZ + j * d.Cell, z1 = z0 + d.Cell;
                var a = new Vector3(x0, ha, z0);
                var b = new Vector3(x1, hb, z0);
                var c = new Vector3(x0, hc, z1);
                var e = new Vector3(x1, hd, z1);
                if (MathF.Abs(ha - hd) <= MathF.Abs(hb - hc))
                {
                    into.Add(ContactShape.FromTriangle(a, c, e, new ShapeId(ShapeKind.Terrain, k, 0), true));
                    into.Add(ContactShape.FromTriangle(a, e, b, new ShapeId(ShapeKind.Terrain, k, 1), true));
                }
                else
                {
                    into.Add(ContactShape.FromTriangle(a, c, b, new ShapeId(ShapeKind.Terrain, k, 0), true));
                    into.Add(ContactShape.FromTriangle(b, c, e, new ShapeId(ShapeKind.Terrain, k, 1), true));
                }
            }
    }

    /// <summary>One line for the logs: sizes and build time.</summary>
    public string Describe()
    {
        EnsureTree();
        return $"{_boxCount} boxes ({_isProp.Count(p => p)} prop colliders), {_obbs.Count} oriented boxes, {_meshes.Count} meshes, "
            + $"terrain {(Terrain != null ? "yes" : "no")}, {(_map?.Movers.Count ?? 0)} movers; BVH {_tree.NodeCount} nodes, depth {_tree.Depth}, built in {BuildMs:0.00} ms";
    }
}
