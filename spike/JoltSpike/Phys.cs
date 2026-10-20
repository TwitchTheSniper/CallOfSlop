using Brutal.JoltApi;
using Brutal.Numerics;

namespace JoltSpike;

/// <summary>Object layers. The game would use the same split (see the design doc, "Physics").</summary>
public static class Layers
{
    public const ushort Static = 0; // map geometry and terrain, never moves
    public const ushort Moving = 1; // kinematic movers (platforms, doors), dynamic props, grenades
    public const ushort Hitbox = 2; // player hit capsules (only in the lag-compensation experiment)
    public const ushort Debris = 3; // cosmetic rigid bodies: collide with the world, nothing queries them
    public const ushort Count = 4;

    public const byte BpStatic = 0;
    public const byte BpMoving = 1;
    public const byte BpQuery = 2;
    public const byte BpCount = 3;
}

/// <summary>One Jolt PhysicsSystem plus the filters, allocator and query helpers the spike needs.</summary>
public sealed class Phys
{
    static bool _joltReady;

    public readonly PhysicsSystem System;
    public readonly BodyInterface Bodies;
    public readonly NarrowPhaseQuery Query;
    public readonly TempAllocator Temp;
    readonly BroadPhaseLayerInterfaceTable _bpl;
    readonly ObjectLayerPairFilterTable _olp;
    readonly ObjectVsBroadPhaseLayerFilterTable _ovb;

    // Filters for "what a player collides with / what a bullet hits": static + moving, never hitboxes or debris.
    public readonly BroadPhaseLayerFilter WorldBp;
    public readonly ObjectLayerFilter WorldObj;
    public readonly BroadPhaseLayerFilter HitboxBp;
    public readonly ObjectLayerFilter HitboxObj;
    public readonly BodyFilter AnyBody;
    public readonly ShapeFilter AnyShape;
    public readonly RayCastSettings RaySettings;

    /// <summary>Global Jolt start-up: allocator, factory, type registration. Once per process.</summary>
    public static void InitJolt()
    {
        if (_joltReady)
            return;
        Jolt.RegisterDefaultAllocator();
        Factory.Instance = Factory.Create();
        Jolt.RegisterTypes();
        _joltReady = true;
    }

    public Phys(uint maxBodies = 65536)
    {
        InitJolt();
        _bpl = BroadPhaseLayerInterfaceTable.Create(Layers.Count, Layers.BpCount);
        _bpl.MapObjectToBroadPhaseLayer(Layers.Static, Layers.BpStatic);
        _bpl.MapObjectToBroadPhaseLayer(Layers.Moving, Layers.BpMoving);
        _bpl.MapObjectToBroadPhaseLayer(Layers.Hitbox, Layers.BpQuery);
        _bpl.MapObjectToBroadPhaseLayer(Layers.Debris, Layers.BpMoving);

        _olp = ObjectLayerPairFilterTable.Create(Layers.Count);
        _olp.EnableCollision(Layers.Moving, Layers.Static);
        _olp.EnableCollision(Layers.Moving, Layers.Moving);
        _olp.EnableCollision(Layers.Debris, Layers.Static);
        // Hitboxes collide with nothing in the simulation; they are only ever found by explicit queries.

        _ovb = ObjectVsBroadPhaseLayerFilterTable.Create(_bpl, Layers.BpCount, _olp, Layers.Count);

        System = PhysicsSystem.Create();
        System.Init(maxBodies, 0, 65536, 10240, _bpl, _ovb, _olp);
        System.SetGravity(new float3(0, -18f, 0));
        Bodies = System.GetBodyInterface();
        Query = System.GetNarrowPhaseQuery();
        Temp = TempAllocatorImpl.Create(16 * 1024 * 1024);

        // A "Moving" object collides with Static + Moving: exactly what a character or a bullet should see.
        WorldBp = DefaultBroadPhaseLayerFilter.Create(_ovb, Layers.Moving);
        WorldObj = DefaultObjectLayerFilter.Create(_olp, Layers.Moving);
        HitboxBp = SpecifiedBroadPhaseLayerFilter.Create(Layers.BpQuery);
        HitboxObj = SpecifiedObjectLayerFilter.Create(Layers.Hitbox);
        AnyBody = BodyFilter.Create();
        AnyShape = ShapeFilter.Create();
        RaySettings = RayCastSettings.Create();
        RaySettings.SetBackFaceMode(BackFaceMode.CollideWithBackFaces); // also hit triangle back faces (thin meshes)
    }

    /// <summary>Keeps a shape alive for as long as the process runs (Jolt shapes are ref counted).</summary>
    public static Shape Keep(Shape s)
    {
        s.AddRef();
        return s;
    }

    /// <summary>Binding problems found at run time and the workaround used (reported by the spike).</summary>
    public static readonly List<string> BindingNotes = new();

    static void Note(string s)
    {
        if (!BindingNotes.Contains(s))
        {
            BindingNotes.Add(s);
            Report.Log("BINDING " + s);
        }
    }

    // ---- Shape construction with fallbacks --------------------------------------------------------------
    // Ahwoo.BRUTAL.Jolt 2026.10.2 ships natives that lack (or number differently) some overloaded exports the
    // managed binding calls, so every constructor here tries the direct call and falls back to an equivalent
    // that works, recording what happened in BindingNotes (reported by the spike, see the design doc).

    static bool Missing(Exception e) => e is EntryPointNotFoundException or JoltException;

    public static Shape Box(float3 half, float convexRadius = 0.05f)
    {
        try { return BoxShape.Create(half, convexRadius); }
        catch (Exception e) when (Missing(e))
        {
            Note($"BoxShape.Create(half, r): {e.GetType().Name}; used BoxShapeSettings");
            return BoxShapeSettings.Create(half, convexRadius).CreateShape();
        }
    }

    public static Shape Capsule(float halfHeight, float radius)
    {
        try { return CapsuleShape.Create(halfHeight, radius); }
        catch (Exception e) when (Missing(e))
        {
            Note($"CapsuleShape.Create(h, r): {e.GetType().Name}; used CapsuleShapeSettings");
            return CapsuleShapeSettings.Create(halfHeight, radius).CreateShape();
        }
    }

    public static Shape Cylinder(float halfHeight, float radius, float convexRadius = 0.05f)
    {
        try { return CylinderShape.Create(halfHeight, radius, convexRadius); }
        catch (Exception e) when (Missing(e))
        {
            Note($"CylinderShape.Create(h, r): {e.GetType().Name}; used CylinderShapeSettings");
            return CylinderShapeSettings.Create(halfHeight, radius, convexRadius).CreateShape();
        }
    }

    public static Shape Sphere(float radius)
    {
        try { return SphereShape.Create(radius); }
        catch (Exception e) when (Missing(e))
        {
            Note($"SphereShape.Create(r): {e.GetType().Name}; used SphereShapeSettings");
            return SphereShapeSettings.Create(radius).CreateShape();
        }
    }

    public static Shape Offset(float3 pos, floatQuat rot, Shape inner)
    {
        try { return RotatedTranslatedShape.Create(pos, rot, inner); }
        catch (Exception e) when (Missing(e))
        {
            try
            {
                var shape = RotatedTranslatedShapeSettings.Create(pos, rot, inner).CreateShape();
                Note($"RotatedTranslatedShape.Create: {e.GetType().Name}; used RotatedTranslatedShapeSettings");
                return shape;
            }
            catch (Exception e2) when (Missing(e2))
            {
                Note($"RotatedTranslatedShape(+Settings).Create: {e2.GetType().Name}; used a one-child StaticCompoundShape");
                var c = StaticCompoundShapeSettings.Create();
                c.AddShape(pos, rot, inner);
                return c.CreateShape();
            }
        }
    }

    public static Shape Tri(float3 a, float3 b, float3 c)
    {
        try { return TriangleShape.Create(a, b, c, 0f); }
        catch (Exception e) when (Missing(e))
        {
            Note($"TriangleShape.Create(v1, v2, v3): {e.GetType().Name}; used TriangleShapeSettings");
            return TriangleShapeSettings.Create(a, b, c, 0f).CreateShape();
        }
    }

    /// <summary>How triangle soups are built when MeshShape is unavailable (picked by ProbeTriangles).</summary>
    public static int TriMode = -1; // 0 = compound of TriangleShapeSettings, 1 = compound of TriangleShapes, 2 = oriented thin boxes
    public static readonly string[] TriModeNames = ["compound of TriangleShapeSettings", "compound of TriangleShape", "thin oriented boxes"];
    public static string ProbeReport = "";

    /// <summary>Fallback for meshes/hulls/terrain: a static compound of single triangles (or thin boxes).</summary>
    public static Shape TriangleCompound(float3[] verts, IndexedTriangle[] tris)
    {
        if (TriMode < 0)
            ProbeTriangles();
        var c = StaticCompoundShapeSettings.Create();
        foreach (var t in tris)
        {
            var a = verts[t.Idx[0]];
            var b = verts[t.Idx[1]];
            var d = verts[t.Idx[2]];
            AddTriangle(c, a, b, d, TriMode);
        }
        return c.CreateShape();
    }

    static void AddTriangle(StaticCompoundShapeSettings c, float3 a, float3 b, float3 d, int mode)
    {
        if (mode == 0)
            c.AddShape(float3.Zero, floatQuat.Identity, (ShapeSettings)TriangleShapeSettings.Create(a, b, d, 0f));
        else if (mode == 1)
            c.AddShape(float3.Zero, floatQuat.Identity, Tri(a, b, d));
        else
        {
            // A thin box in the triangle's plane covering the triangle (overlaps neighbours a little; fine for terrain).
            var n = float3.Cross(b - a, d - a);
            var len = n.Length();
            if (len < 1e-8f)
                return;
            n /= len;
            var center = (a + b + d) / 3f;
            var e1 = b - a;
            e1 /= e1.Length();
            var e2 = float3.Cross(n, e1);
            var r = MathF.Max((a - center).Length(), MathF.Max((b - center).Length(), (d - center).Length()));
            var rot = QuatFromBasis(e1, n, e2);
            c.AddShape(center - n * 0.05f, rot, (ShapeSettings)BoxShapeSettings.Create(new float3(r, 0.05f, r * 0.75f), 0.01f));
        }
    }

    /// <summary>Rotation whose X, Y, Z axes are the given orthonormal vectors.</summary>
    static floatQuat QuatFromBasis(float3 x, float3 y, float3 z)
    {
        float m00 = x.X, m11 = y.Y, m22 = z.Z, tr = m00 + m11 + m22;
        if (tr > 0)
        {
            var s = MathF.Sqrt(tr + 1) * 2;
            return floatQuat.Normalize(new floatQuat((y.Z - z.Y) / s, (z.X - x.Z) / s, (x.Y - y.X) / s, 0.25f * s));
        }
        if (m00 > m11 && m00 > m22)
        {
            var s = MathF.Sqrt(1 + m00 - m11 - m22) * 2;
            return floatQuat.Normalize(new floatQuat(0.25f * s, (y.X + x.Y) / s, (z.X + x.Z) / s, (y.Z - z.Y) / s));
        }
        if (m11 > m22)
        {
            var s = MathF.Sqrt(1 + m11 - m00 - m22) * 2;
            return floatQuat.Normalize(new floatQuat((y.X + x.Y) / s, 0.25f * s, (z.Y + y.Z) / s, (z.X - x.Z) / s));
        }
        var s2 = MathF.Sqrt(1 + m22 - m00 - m11) * 2;
        return floatQuat.Normalize(new floatQuat((z.X + x.Z) / s2, (z.Y + y.Z) / s2, 0.25f * s2, (x.Y - y.X) / s2));
    }

    /// <summary>
    /// With no ray casts, the only way to know whether a shape really collides is to drop a character on it.
    /// Tries each way of building a triangle floor (plus control cases) and picks the first that holds a player.
    /// </summary>
    public static void ProbeTriangles()
    {
        var p = new Phys(1024);
        var results = new List<string>();
        var slot = 0;
        // Terrain-like patches: cells x cells quads, optional slope, vertices offset by 'shift' with the body at -shift.
        float Patch(int mode, int cells, float slope, float shift)
        {
            var c = StaticCompoundShapeSettings.Create();
            for (var z = 0; z < cells; z++)
                for (var x = 0; x < cells; x++)
                {
                    float3 V(int xx, int zz) => new(xx + shift, (xx + zz) * slope, zz + shift);
                    AddTriangle(c, V(x, z), V(x, z + 1), V(x + 1, z), mode);
                    AddTriangle(c, V(x + 1, z), V(x, z + 1), V(x + 1, z + 1), mode);
                }
            var baseX = (slot++) * 80f;
            p.AddStatic(c.CreateShape(), new float3(baseX - shift, 20, -shift), floatQuat.Identity);
            p.System.OptimizeBroadPhase();
            var m = new Mover(p, new float3(baseX + cells / 2f, 22 + cells * slope, cells / 2f));
            for (var i = 0; i < 90; i++)
                m.Step(default);
            return m.Ch.GetPosition().Y - (20 + cells * slope);
        }
        var heldBy = new bool[3];
        for (var mode = 0; mode < 3; mode++)
        {
            float y;
            try { y = Patch(mode, 16, 0.05f, 0); }
            catch (Exception e) { results.Add($"{TriModeNames[mode]}: {e.GetType().Name} {e.Message}"); continue; }
            heldBy[mode] = MathF.Abs(y) < 0.2f;
            results.Add($"{TriModeNames[mode]} {Report.F(y)}");
        }
        TriMode = Array.IndexOf(heldBy, true);
        if (TriMode < 0)
            TriMode = 2;
        // Which property of a real terrain chunk matters? (mode chosen above)
        results.Add($"[{TriModeNames[TriMode]}] 32x32 sloped {Report.F(Patch(TriMode, 32, 0.05f, 0))}");
        results.Add($"16x16 flat {Report.F(Patch(TriMode, 16, 0f, 0))}");
        results.Add($"16x16 sloped, verts offset 100 m {Report.F(Patch(TriMode, 16, 0.05f, 100))}");
        results.Add($"16x16 flat, verts offset 100 m {Report.F(Patch(TriMode, 16, 0f, 100))}");
        ProbeReport = $"drop test on triangle patches (feet y above surface after 1.5 s, ~0 = held): {string.Join(", ", results)} -> using {TriModeNames[TriMode]}";
        Note("triangle fallback: " + ProbeReport);
    }

    /// <summary>Convex hull from points; falls back to the given surface triangles as a triangle compound.</summary>
    public static Shape MakeHull(ReadOnlySpan<float3> points, float convexRadius, float3[] fbVerts, IndexedTriangle[] fbTris)
    {
        try
        {
            return ConvexHullShapeSettings.Create(points, convexRadius).CreateShape();
        }
        catch (Exception e) when (Missing(e))
        {
            Note($"ConvexHullShapeSettings.Create(points): {e.GetType().Name} '{e.Message}'; NO WORKING HULL CONSTRUCTOR, used a triangle compound of the surface");
            return TriangleCompound(fbVerts, fbTris);
        }
    }

    /// <summary>Triangle mesh from indexed triangles, with fallbacks.</summary>
    public static Shape MakeMesh(float3[] verts, IndexedTriangle[] tris)
    {
        try
        {
            return MeshShapeSettings.Create(verts, tris, ReadOnlySpan<PhysicsMaterial>.Empty).CreateShape();
        }
        catch (Exception e) when (Missing(e))
        {
            try
            {
                var flat = new Triangle[tris.Length];
                for (var i = 0; i < tris.Length; i++)
                    flat[i] = Triangle.Create(verts[tris[i].Idx[0]], verts[tris[i].Idx[1]], verts[tris[i].Idx[2]]);
                var shape = MeshShapeSettings.Create(flat, ReadOnlySpan<PhysicsMaterial>.Empty).CreateShape();
                Note($"MeshShapeSettings.Create(verts, tris): {e.GetType().Name}; Create(triangles) worked");
                return shape;
            }
            catch (Exception e2) when (Missing(e2))
            {
                Note($"MeshShapeSettings.Create: {e.GetType().Name} / {e2.GetType().Name} '{e2.Message}'; NO WORKING MESH CONSTRUCTOR, used a triangle compound");
                return TriangleCompound(verts, tris);
            }
        }
    }

    /// <summary>Converts a triangle soup into indexed form (3 vertices per triangle; fine for a spike).</summary>
    public static Shape MakeMesh(IReadOnlyList<Triangle> soup)
    {
        var verts = new float3[soup.Count * 3];
        var tris = new IndexedTriangle[soup.Count];
        for (var i = 0; i < soup.Count; i++)
        {
            verts[i * 3] = soup[i].V[0];
            verts[i * 3 + 1] = soup[i].V[1];
            verts[i * 3 + 2] = soup[i].V[2];
            tris[i] = IndexedTriangle.Create((uint)(i * 3), (uint)(i * 3 + 1), (uint)(i * 3 + 2), 0);
        }
        return MakeMesh(verts, tris);
    }

    /// <summary>Heightfield n x n; returns null when the binding can't build one (caller falls back).</summary>
    public static unsafe Shape? MakeHeightField(float[] samples, int n, float3 offset, float3 scale)
    {
        try
        {
            HeightFieldShapeSettings hs;
            fixed (float* p = samples)
                hs = HeightFieldShapeSettings.Create(p, offset, scale, (uint)n, default, ReadOnlySpan<PhysicsMaterial>.Empty);
            return hs.CreateShape();
        }
        catch (Exception e) when (Missing(e))
        {
            Note($"HeightFieldShapeSettings.Create(samples, ...): {e.GetType().Name} '{e.Message}'; NO WORKING HEIGHTFIELD, terrain built from triangle compounds");
            return null;
        }
    }

    /// <summary>Terrain as heightfield, or as 32x32-cell chunks of triangle compounds when that's unavailable.</summary>
    public List<uint> AddTerrain(float[] samples, int n, float3 offset, out bool heightfield)
    {
        var ids = new List<uint>();
        var hf = MakeHeightField(samples, n, offset, new float3(1, 1, 1));
        heightfield = hf != null;
        if (hf is { } shape)
        {
            ids.Add(AddStatic(shape, float3.Zero, floatQuat.Identity));
            return ids;
        }
        const int Chunk = 16;
        for (var cz = 0; cz < n - 1; cz += Chunk)
            for (var cx = 0; cx < n - 1; cx += Chunk)
            {
                var verts = new List<float3>();
                var tris = new List<IndexedTriangle>();
                for (var z = cz; z < Math.Min(cz + Chunk, n - 1); z++)
                    for (var x = cx; x < Math.Min(cx + Chunk, n - 1); x++)
                    {
                        var b = (uint)verts.Count;
                        verts.Add(offset + new float3(x, samples[z * n + x], z));
                        verts.Add(offset + new float3(x + 1, samples[z * n + x + 1], z));
                        verts.Add(offset + new float3(x, samples[(z + 1) * n + x], z + 1));
                        verts.Add(offset + new float3(x + 1, samples[(z + 1) * n + x + 1], z + 1));
                        tris.Add(IndexedTriangle.Create(b, b + 2, b + 1, 0)); // counter-clockwise seen from above
                        tris.Add(IndexedTriangle.Create(b + 1, b + 2, b + 3, 0));
                    }
                ids.Add(AddStatic(MakeMesh(verts.ToArray(), tris.ToArray()), float3.Zero, floatQuat.Identity));
            }
        return ids;
    }

    public uint AddStatic(Shape shape, float3 pos, floatQuat rot)
    {
        var bcs = BodyCreationSettings.Create(shape, pos, rot, MotionType.Static, Layers.Static);
        var id = Bodies.CreateAndAddBody(bcs, Activation.DontActivate);
        bcs.Dispose();
        return id;
    }

    public uint AddKinematic(Shape shape, float3 pos, floatQuat rot, ushort layer = Layers.Moving)
    {
        var bcs = BodyCreationSettings.Create(shape, pos, rot, MotionType.Kinematic, layer);
        var id = Bodies.CreateAndAddBody(bcs, Activation.Activate);
        bcs.Dispose();
        return id;
    }

    public uint AddBox(float3 center, float3 half, floatQuat rot) =>
        AddStatic(Box(half, MathF.Min(0.05f, MathF.Min(half.X, MathF.Min(half.Y, half.Z)) * 0.5f)), center, rot);

    public uint AddBox(float3 center, float3 half) => AddBox(center, half, floatQuat.Identity);

    /// <summary>0 = CastRayClosest, 1 = CastRay(ref hit), 2 = no ray cast export in this package.</summary>
    public static int RayPath;
    public static bool RaysAvailable => RayPath < 2;

    /// <summary>Closest hit along a ray against world geometry. Returns the distance, maxDist on a miss, NaN if unsupported.</summary>
    public float Raycast(float3 origin, float3 dir, float maxDist, out uint body)
    {
        body = uint.MaxValue;
        var ray = new RRayCast { Origin = origin, Direction = dir * maxDist };
        if (RayPath == 0)
        {
            try
            {
                if (Query.CastRayClosest(in ray, RaySettings, out var hit, WorldBp, WorldObj, AnyBody, AnyShape))
                {
                    body = hit.BodyID;
                    return hit.Fraction * maxDist;
                }
                return maxDist;
            }
            catch (EntryPointNotFoundException)
            {
                Note("NarrowPhaseQuery.CastRayClosest has no native export");
                RayPath = 1;
            }
        }
        if (RayPath == 1)
        {
            try
            {
                var hit = new RayCastResult { BodyID = uint.MaxValue, Fraction = 1.0001f };
                if (Query.CastRay(in ray, ref hit, WorldBp, WorldObj, AnyBody))
                {
                    body = hit.BodyID;
                    return hit.Fraction * maxDist;
                }
                return maxDist;
            }
            catch (EntryPointNotFoundException)
            {
                Note("NarrowPhaseQuery.CastRay has no native export either: NO JOLT RAY CASTS in this package");
                RayPath = 2;
            }
        }
        return float.NaN;
    }

    /// <summary>Runs a setter that may be missing from the natives; records it if so.</summary>
    public static void Try(Action a, string what)
    {
        try { a(); }
        catch (EntryPointNotFoundException) { Note($"{what}: no native export (skipped)"); }
    }
}

/// <summary>Deterministic helpers: no MathF.Sin/Cos in anything that feeds the simulation (CRTs differ per OS).</summary>
public static class DMath
{
    public static float Sin(float x)
    {
        double a = x;
        const double tau = 6.283185307179586;
        a -= Math.Floor((a + Math.PI) / tau) * tau; // [-pi, pi)
        double a2 = a * a, term = a, sum = a;
        for (var n = 1; n <= 9; n++)
        {
            term *= -a2 / ((2 * n) * (2 * n + 1));
            sum += term;
        }
        return (float)sum;
    }

    public static float Cos(float x) => Sin(x + 1.5707963267948966f);

    public static floatQuat AxisAngle(float3 axis, float angle)
    {
        var s = Sin(angle * 0.5f);
        return new floatQuat(axis.X * s, axis.Y * s, axis.Z * s, Cos(angle * 0.5f));
    }
}

/// <summary>Small deterministic PRNG (xorshift32) so inputs and maps are identical on every machine.</summary>
public struct Rng(uint seed)
{
    uint _s = seed == 0 ? 1u : seed;

    public uint Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 17;
        _s ^= _s << 5;
        return _s;
    }

    public float Float() => (Next() >> 8) * (1f / 16777216f); // [0,1)
    public float Range(float lo, float hi) => lo + (hi - lo) * Float();
    public int Int(int n) => (int)(Next() % (uint)n);
}

/// <summary>FNV-1a over raw float bits: two runs match only if every bit matches.</summary>
public struct Hasher
{
    public ulong Value;

    public Hasher() => Value = 0xcbf29ce484222325UL;

    public void Add(uint v)
    {
        for (var i = 0; i < 4; i++)
        {
            Value ^= (v >> (i * 8)) & 0xff;
            Value *= 0x100000001b3UL;
        }
    }

    public void Add(float f) => Add(BitConverter.SingleToUInt32Bits(f));

    public void Add(float3 v)
    {
        Add(v.X);
        Add(v.Y);
        Add(v.Z);
    }

    public override readonly string ToString() => Value.ToString("x16");
}
