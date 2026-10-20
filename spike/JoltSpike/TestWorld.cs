using Brutal.JoltApi;
using Brutal.Numerics;

namespace JoltSpike;

/// <summary>
/// The spike's test map: a heightfield, a strip of movement fixtures (stairs, ramps, ceilings, ledges,
/// moving platforms), a few non-box shapes and ~600 random boxes like a big Call of Slop map.
/// Everything is generated from a fixed seed so every machine builds the identical world.
/// </summary>
public sealed class TestWorld
{
    public readonly Phys P;
    public readonly List<(float3 Min, float3 Max)> Aabbs = new(); // axis-aligned boxes, for the brute-force baseline
    public uint MovingPlatform, RotatingPlatform;
    public uint CylinderBody, HullBody, MeshBody;
    public List<uint> TerrainBodies = new();
    public bool HeightField;
    public int BodyCount;

    // Fixture lanes (z) - each movement test runs in its own lane.
    public const float LaneStairs = -20, LaneStep55 = -14, LaneStep70 = -8, LaneRamp30 = 8, LaneRamp50 = 14;
    public const float LaneCeiling = 20, LaneLedge = 26, LaneWall = 32, LaneMover = 40, LaneSpinner = 52, LaneShapes = -30;
    public static readonly float3 MoverStart = new(0, 0.5f, LaneMover);
    public static readonly float3 SpinnerCenter = new(4, 0.5f, LaneSpinner);
    public const float MoverSpeed = 2f, SpinnerOmega = 1f;

    public const int TerrainSamples = 256; // 1 m spacing -> 256 x 256 m

    public TestWorld(int randomBoxes = 600, bool shuffled = false, int terrainSamples = TerrainSamples)
    {
        P = new Phys();
        var adds = new List<Action>();

        // ---- Terrain: flat in the middle (where the fixtures are), hills outside ----
        adds.Add(() => AddTerrain(terrainSamples));

        // ---- Movement fixtures ----
        for (var i = 0; i < 4; i++) // 0.4 m stairs, 1 m deep each, the top one 6 m long; top at 1.6 m
        {
            var h = 0.4f * (i + 1);
            var len = i < 3 ? 1f : 6f;
            var c = new float3(3 + i + len / 2, h / 2, LaneStairs);
            var half = new float3(len / 2, h / 2, 2);
            adds.Add(() => AddAabb(c, half));
        }
        adds.Add(() => AddAabb(new float3(7, 0.275f, LaneStep55), new float3(4, 0.275f, 2))); // one 0.55 m step, x 3..11
        adds.Add(() => AddAabb(new float3(7, 0.35f, LaneStep70), new float3(4, 0.35f, 2))); // 0.70 m: too tall to step
        adds.Add(() => AddRamp(LaneRamp30, 30));
        adds.Add(() => AddRamp(LaneRamp50, 50));
        adds.Add(() => AddAabb(new float3(3, 1.4f + 0.25f, LaneCeiling), new float3(3, 0.25f, 3))); // ceiling 1.4 m up
        adds.Add(() => AddAabb(new float3(4, 0.75f, LaneLedge), new float3(1, 0.75f, 2))); // 1.5 m ledge
        adds.Add(() => AddAabb(new float3(4, 1.5f, LaneWall), new float3(1, 1.5f, 2))); // 3 m wall
        adds.Add(() =>
        {
            MovingPlatform = P.AddKinematic(Phys.Keep(Phys.Box(new float3(2, 0.25f, 2))), MoverStart, floatQuat.Identity);
            RotatingPlatform = P.AddKinematic(Phys.Keep(Phys.Box(new float3(4, 0.25f, 4))), SpinnerCenter, floatQuat.Identity);
        });

        // ---- Non-box shapes: cylinder, convex hull, triangle mesh ----
        adds.Add(() =>
        {
            CylinderBody = P.AddStatic(Phys.Cylinder(1.5f, 0.6f), new float3(0, 1.5f, LaneShapes), floatQuat.Identity); // 3 m pillar
            // A pyramid as a convex hull (stand-in for a rock or a sandbag pile): apex 2 m up.
            ReadOnlySpan<float3> pyr = [new(-1.5f, 0, -1.5f), new(1.5f, 0, -1.5f), new(1.5f, 0, 1.5f), new(-1.5f, 0, 1.5f), new(0, 2, 0)];
            float3[] pv = [pyr[0], pyr[1], pyr[2], pyr[3], pyr[4]];
            IndexedTriangle[] pt = [IndexedTriangle.Create(0, 1, 2, 0), IndexedTriangle.Create(0, 2, 3, 0), // base (faces down)
                IndexedTriangle.Create(0, 4, 1, 0), IndexedTriangle.Create(1, 4, 2, 0), IndexedTriangle.Create(2, 4, 3, 0), IndexedTriangle.Create(3, 4, 0, 0)];
            HullBody = P.AddStatic(Phys.MakeHull(pyr, 0.05f, pv, pt), new float3(6, 0, LaneShapes), floatQuat.Identity);
            // An arch as a triangle mesh: two legs and a top beam, 4 m wide, 3 m tall (top surface at y = 3).
            var tris = new List<Triangle>();
            AddMeshBox(tris, new float3(-1.75f, 1.25f, 0), new float3(0.25f, 1.25f, 0.5f));
            AddMeshBox(tris, new float3(1.75f, 1.25f, 0), new float3(0.25f, 1.25f, 0.5f));
            AddMeshBox(tris, new float3(0, 2.75f, 0), new float3(2f, 0.25f, 0.5f));
            MeshBody = P.AddStatic(Phys.MakeMesh(tris), new float3(14, 0, LaneShapes), floatQuat.Identity);
        });

        // ---- Random boxes like a big map: houses, crates, cover, some stacked ----
        var rng = new Rng(1234);
        for (var i = 0; i < randomBoxes; i++)
        {
            var half = new float3(rng.Range(0.3f, 3f), rng.Range(0.2f, 2.5f), rng.Range(0.3f, 3f));
            var c = new float3(rng.Range(40, 120), half.Y + (rng.Int(5) == 0 ? rng.Range(0, 3) : 0), rng.Range(-60, 60));
            adds.Add(() => AddAabb(c, half));
        }

        if (shuffled)
        {
            var srng = new Rng(999);
            for (var i = adds.Count - 1; i > 0; i--)
            {
                var j = srng.Int(i + 1);
                (adds[i], adds[j]) = (adds[j], adds[i]);
            }
        }
        foreach (var a in adds)
            a();
        P.System.OptimizeBroadPhase();
        BodyCount = (int)P.System.GetNumBodies();
    }

    /// <summary>Deterministic terrain height at a sample (no CRT trig).</summary>
    public static float TerrainHeight(float x, float z)
    {
        var edge = MathF.Max(MathF.Abs(x), MathF.Abs(z));
        if (edge < 60)
            return 0;
        var t = MathF.Min(1, (edge - 60) / 20);
        return t * (2.5f + 2.5f * DMath.Sin(x * 0.07f) * DMath.Cos(z * 0.05f));
    }

    void AddTerrain(int n)
    {
        var samples = new float[n * n];
        var half = n / 2f;
        for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
                samples[z * n + x] = TerrainHeight(x - half, z - half);
        TerrainBodies = P.AddTerrain(samples, n, new float3(-half, 0, -half), out HeightField);
    }

    void AddAabb(float3 c, float3 half)
    {
        P.AddBox(c, half);
        Aabbs.Add((c - half, c + half));
    }

    void AddRamp(float lane, float degrees)
    {
        // A 12 m long plank tilted around Z, its lower end buried in the ground near x = 2.
        var angle = degrees * MathF.PI / 180f;
        var rot = DMath.AxisAngle(new float3(0, 0, 1), angle);
        var half = new float3(6, 0.25f, 2);
        var center = new float3(2 + 6 * DMath.Cos(angle), 6 * DMath.Sin(angle) - 0.25f, lane);
        P.AddBox(center, half, rot);
    }

    static void AddMeshBox(List<Triangle> tris, float3 c, float3 h)
    {
        Span<float3> v = stackalloc float3[8];
        for (var i = 0; i < 8; i++)
            v[i] = c + new float3((i & 1) != 0 ? h.X : -h.X, (i & 2) != 0 ? h.Y : -h.Y, (i & 4) != 0 ? h.Z : -h.Z);
        // 6 faces, 2 triangles each, wound counter-clockwise seen from outside (Jolt's front face).
        // Faces as quads a,b,c,d. Vertex i has x = bit 0, y = bit 1, z = bit 2.
        ReadOnlySpan<int> f = [0, 4, 6, 2, /* -x */ 1, 3, 7, 5, /* +x */ 0, 1, 5, 4, /* -y */ 2, 6, 7, 3, /* +y */ 0, 2, 3, 1, /* -z */ 4, 5, 7, 6 /* +z */];
        for (var q = 0; q < 6; q++)
        {
            int a = f[q * 4], b = f[q * 4 + 1], cc = f[q * 4 + 2], d = f[q * 4 + 3];
            tris.Add(Triangle.Create(v[a], v[b], v[cc]));
            tris.Add(Triangle.Create(v[a], v[cc], v[d]));
        }
    }

    /// <summary>Platform poses are pure functions of the tick, so replays and other machines see the same thing.</summary>
    public void SetMovers(int tick)
    {
        var t = tick * Sim.Dt;
        var phase = t % 8f; // back and forth along X: 4 s out, 4 s back
        var vx = phase < 4 ? MoverSpeed : -MoverSpeed;
        var x = phase < 4 ? phase * MoverSpeed : (8 - phase) * MoverSpeed;
        P.Bodies.SetPositionRotationAndVelocity(MovingPlatform, MoverStart + new float3(x, 0, 0), floatQuat.Identity, new float3(vx, 0, 0), float3.Zero);
        var ang = t * SpinnerOmega;
        P.Bodies.SetPositionRotationAndVelocity(RotatingPlatform, SpinnerCenter, DMath.AxisAngle(new float3(0, 1, 0), ang), float3.Zero, new float3(0, SpinnerOmega, 0));
    }
}
