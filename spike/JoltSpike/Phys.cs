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

    /// <summary>Convex hull from points, robust against the 2026.10.2 overload mix-up (see the design doc).</summary>
    public static Shape MakeHull(ReadOnlySpan<float3> points, float convexRadius)
    {
        var s = ConvexHullShapeSettings.Create(points, convexRadius);
        try
        {
            return s.CreateShape();
        }
        catch (JoltException e)
        {
            Note($"ConvexHullShapeSettings.Create(points) -> '{e.Message}'; fixed with SetPoints()");
            s.SetPoints(points);
            s.SetMaxConvexRadius(convexRadius);
            s.ClearCachedResult();
            return s.CreateShape();
        }
    }

    /// <summary>Triangle mesh from indexed triangles, with a setter-based fallback.</summary>
    public static Shape MakeMesh(float3[] verts, IndexedTriangle[] tris)
    {
        try
        {
            return MeshShapeSettings.Create(verts, tris, ReadOnlySpan<PhysicsMaterial>.Empty).CreateShape();
        }
        catch (Exception e) when (e is JoltException or EntryPointNotFoundException)
        {
            Note($"MeshShapeSettings.Create(verts, tris) -> {e.GetType().Name} '{e.Message}'; fixed with setters");
            var flat = new Triangle[tris.Length];
            for (var i = 0; i < tris.Length; i++)
                flat[i] = Triangle.Create(verts[tris[i].Idx[0]], verts[tris[i].Idx[1]], verts[tris[i].Idx[2]]);
            var s = MeshShapeSettings.Create(flat, ReadOnlySpan<PhysicsMaterial>.Empty);
            s.SetTriangleVertices(verts);
            s.SetIndexedTriangles(tris);
            s.Sanitize();
            s.ClearCachedResult();
            return s.CreateShape();
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

    /// <summary>Heightfield n x n with 1 sample per scale unit. The parameterless Create() is missing from the 2026.10.2 natives.</summary>
    public static unsafe Shape MakeHeightField(float[] samples, int n, float3 offset, float3 scale)
    {
        HeightFieldShapeSettings hs;
        try
        {
            hs = HeightFieldShapeSettings.Create();
        }
        catch (EntryPointNotFoundException)
        {
            Note("HeightFieldShapeSettings.Create() has no native export (HeightFieldShapeSettings_Create_2); used the full Create(samples, ...) + setters");
            fixed (float* p = samples)
                hs = HeightFieldShapeSettings.Create(p, offset, scale, (uint)n, default, ReadOnlySpan<PhysicsMaterial>.Empty);
        }
        hs.SetOffset(offset);
        hs.SetScale(scale);
        hs.SetSampleCount((uint)n);
        hs.SetHeightSamples(samples);
        hs.ClearCachedResult();
        return hs.CreateShape();
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
        AddStatic(BoxShape.Create(half, MathF.Min(0.05f, MathF.Min(half.X, MathF.Min(half.Y, half.Z)) * 0.5f)), center, rot);

    public uint AddBox(float3 center, float3 half) => AddBox(center, half, floatQuat.Identity);

    /// <summary>Closest hit along a ray against world geometry. Returns the distance or maxDist.</summary>
    public float Raycast(float3 origin, float3 dir, float maxDist, out uint body)
    {
        var ray = new RRayCast { Origin = origin, Direction = dir * maxDist };
        if (Query.CastRayClosest(in ray, RaySettings, out var hit, WorldBp, WorldObj, AnyBody, AnyShape))
        {
            body = hit.BodyID;
            return hit.Fraction * maxDist;
        }
        body = uint.MaxValue;
        return maxDist;
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
