using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static void CheckCompoundCollision()
    {
        CollisionCost();
    }

    /// <summary>Wall and busiest-tick time and allocation of <paramref name="ticks"/> ticks of a scene, after a warm-up.</summary>
    private static void CostRun(string name, ConstructWorld w, IWorldQuery q, int warm, int ticks, Action? each = null)
    {
        for (var t = 0; t < warm; t++)
        {
            each?.Invoke();
            w.Step(q, World.TickDt);
        }
        var sw = new Stopwatch();
        double sum = 0, worst = 0;
        long bytes = 0;
        var contacts = 0;
        var sim = 0;
        for (var t = 0; t < ticks; t++)
        {
            each?.Invoke();
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            w.Step(q, World.TickDt);
            var ms = sw.Elapsed.TotalMilliseconds;
            bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            sum += ms;
            worst = Math.Max(worst, ms);
            contacts = Math.Max(contacts, ConstructPhysics.LastContacts);
            sim = Math.Max(sim, ConstructPhysics.LastSimulated);
        }
        Line($"collision cost, {name}: {sum / ticks:0.000} ms a tick (worst {worst:0.000}), up to {contacts} contacts, {sim} simulated, {w.Bodies.Count(b => !b.Asleep)} awake, {bytes} bytes in {ticks} ticks");
        if (bytes > 0)
            Fail($"compound collision: {name} allocated {bytes} bytes in {ticks} ticks");
    }

    /// <summary>A hull of <paramref name="parts"/> blocks in a flat 12-wide sheet, sliding.</summary>
    private static Construct CostHull(ConstructWorld w, Vector3 at, int parts, ConstructPartDef block)
    {
        var size = block.BoundsMax - block.BoundsMin;
        var c = w.Create(at, Quaternion.Identity)!;
        for (var i = 0; i < parts; i++)
            w.AddPart(c, block, new Vector3(i % 12 * size.X, 0, i / 12 * size.Z), 0);
        c.Rebuild();
        return c;
    }

    /// <summary>Rolling ground, half-metre cells.</summary>
    private static PhysicsWorld CostGround()
    {
        const int Count = 241;
        var h = new float[Count * Count];
        for (var j = 0; j < Count; j++)
            for (var i = 0; i < Count; i++)
                h[j * Count + i] = 0.4f * MathF.Sin(i * 0.35f) * MathF.Cos(j * 0.25f);
        return new PhysicsWorld(new List<Box>(), new HeightfieldCollision(new TerrainData(Count, Count, 0.5f, -60, -60, h, new byte[h.Length], new TerrainSurface())), 59, 59);
    }

    private static void CollisionCost()
    {
        var q = new JointTestWorld();
        var block = ConstructParts.Need("Frame Block");
        var size = block.BoundsMax - block.BoundsMin;
        // Warm the code paths once.
        var warm = new ConstructWorld();
        for (var i = 0; i < 40; i++)
            ConstructBuild.PlaceNew(warm, block, new Vector3(i % 6 * 0.3f, 1f + i / 6 * 0.3f, 0), 0);
        CostHull(warm, new Vector3(0, 3, 4), Construct.MaxParts, block).Vel = new Vector3(2, 0, 0);
        for (var t = 0; t < 120; t++)
            warm.Step(q, World.TickDt);
        // Many awake bodies: 300 blocks falling, spread out.
        var rng = new Random(7);
        var fall = new ConstructWorld();
        for (var i = 0; i < 300; i++)
            ConstructBuild.PlaceNew(fall, block, new Vector3(i % 17 * 2.5f, 30f + (float)rng.NextDouble() * 2, i / 17 * 2.5f), (float)rng.NextDouble() * 6);
        CostRun("300 falling blocks", fall, q, 8, 30);
        // A pile: 200 blocks dropped into one heap (bodies touching bodies, while it settles).
        var pile = new ConstructWorld();
        rng = new Random(11);
        for (var i = 0; i < 200; i++)
            ConstructBuild.PlaceNew(pile, block, new Vector3(i % 8 * size.X * 1.1f, 0.3f + i / 64 * size.Y * 1.2f, i / 8 % 8 * size.Z * 1.1f), (float)rng.NextDouble() * 0.3f);
        CostRun("a pile of 200", pile, q, 15, 60);
        // The biggest hull a construct holds, kept sliding on the floor (the per-construct cap is 96 parts, not 200).
        var hull = new ConstructWorld();
        var slide = CostHull(hull, new Vector3(0, -block.BoundsMin.Y + 0.02f, 0), Construct.MaxParts, block);
        CostRun($"one {Construct.MaxParts}-part hull sliding", hull, q, 20, 60, () =>
        {
            slide.Vel = new Vector3(3, slide.Vel.Y, 0);
            slide.Wake();
        });
        // The same hull and a few dozen blocks on rolling ground of half-metre cells (the worst of the ground's triangles).
        var rolling = CostGround();
        var land = new ConstructWorld();
        var track = CostHull(land, new Vector3(0, 1.6f, 0), Construct.MaxParts, block);
        for (var i = 0; i < 40; i++)
            ConstructBuild.PlaceNew(land, block, new Vector3(-20 + i % 8 * 2, 1.5f, 10 + i / 8 * 2), 0);
        CostRun($"one {Construct.MaxParts}-part hull and 40 blocks on half-metre ground", land, rolling, 30, 60, () =>
        {
            track.Vel = new Vector3(3, track.Vel.Y, 0);
            track.Wake();
        });
        // Eight of them stacked, the top ones kept sliding over each other.
        var many = new ConstructWorld();
        var hulls = new List<Construct>();
        for (var i = 0; i < 8; i++)
            hulls.Add(CostHull(many, new Vector3(i * 0.2f, 0.3f + i * size.Y * 1.5f, i * 0.2f), Construct.MaxParts, block));
        CostRun($"eight {Construct.MaxParts}-part hulls stacked", many, q, 30, 60, () =>
        {
            for (var i = 1; i < hulls.Count; i += 2)
            {
                hulls[i].Vel = new Vector3(1, hulls[i].Vel.Y, 0);
                hulls[i].Wake();
            }
        });
    }
}
