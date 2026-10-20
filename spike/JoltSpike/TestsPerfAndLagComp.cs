// The game project (CallOfSlop.csproj) compiles every .cs under the repo, so the spike's code only exists
// when JOLT_SPIKE is defined (JoltSpike.csproj defines it).
#if JOLT_SPIKE
using Brutal.JoltApi;
using Brutal.Numerics;
using static JoltSpike.Report;

namespace JoltSpike;

/// <summary>Section d (ray and build performance) and e (lag compensation options).</summary>
public static class TestsPerfAndLagComp
{
    // ---------------------------------------------------------------- d
    public static void Perf()
    {
        var w = new TestWorld();
        w.SetMovers(0);
        const int N = 1000;
        var rng = new Rng(4242);
        var rays = new RRayCast[N];
        var origins = new float3[N];
        var dirs = new float3[N];
        for (var i = 0; i < N; i++)
        {
            origins[i] = new float3(rng.Range(-50, 120), rng.Range(0.5f, 3f), rng.Range(-60, 60));
            var d = new float3(rng.Range(-1, 1), rng.Range(-0.2f, 0.2f), rng.Range(-1, 1));
            dirs[i] = d / d.Length();
            rays[i] = new RRayCast { Origin = origins[i], Direction = dirs[i] * 200f };
        }

        w.P.Raycast(origins[0], dirs[0], 200, out _);
        if (!Phys.RaysAvailable)
        {
            Check("Jolt ray casts available", false, "no native ray cast export; only the C# baseline is measured");
            var tb = Clock.Now;
            for (var i = 0; i < N; i++)
                BruteForce(w, origins[i], dirs[i], 200);
            var tbUs = Clock.UsSince(tb);
            Metric("1000 rays, today's C# loop over every AABB, us total / per ray", $"{F(tbUs, 0)} / {F(tbUs / N, 2)} over {w.Aabbs.Count} boxes");
            BvhRays(w, origins, dirs);
            BuildTimes();
            return;
        }
        // Warm up (JIT, caches), then best of 5.
        double single = double.MaxValue, batch = double.MaxValue, brute = double.MaxValue;
        var dist = new float[N];
        var results = new RayCastResult[N];
        var hits = 0u;
        for (var rep = 0; rep < 6; rep++)
        {
            var t0 = Clock.Now;
            for (var i = 0; i < N; i++)
                dist[i] = w.P.Raycast(origins[i], dirs[i], 200, out _);
            if (rep > 0) single = Math.Min(single, Clock.UsSince(t0));

            t0 = Clock.Now;
            hits = w.P.Query.CastRaysClosest(rays, w.P.RaySettings, results, w.P.WorldBp, w.P.WorldObj, w.P.AnyBody, w.P.AnyShape);
            if (rep > 0) batch = Math.Min(batch, Clock.UsSince(t0));

            t0 = Clock.Now;
            for (var i = 0; i < N; i++)
                BruteForce(w, origins[i], dirs[i], 200);
            if (rep > 0) brute = Math.Min(brute, Clock.UsSince(t0));
        }
        var hitCount = dist.Count(x => x < 200);
        Metric("1000 rays, single CastRayClosest, us total / per ray", $"{F(single, 0)} / {F(single / N, 2)}");
        Metric("1000 rays, batched CastRaysClosest, us total / per ray", $"{F(batch, 0)} / {F(batch / N, 2)} (returned {hits})");
        Metric("1000 rays, today's C# loop over every AABB (boxes only), us total / per ray", $"{F(brute, 0)} / {F(brute / N, 2)} over {w.Aabbs.Count} boxes");
        Check("rays hit things", hitCount > N / 2, $"{hitCount}/{N} rays hit within 200 m");
        BvhRays(w, origins, dirs);
        BuildTimes(origins, dirs);
    }

    /// <summary>The C# fallback query (design doc "BvhWorldQuery"): same answers as today's loop, timed at 609 and 5000 boxes.</summary>
    static void BvhRays(TestWorld w, float3[] origins, float3[] dirs)
    {
        var N = origins.Length;
        var tb = Clock.Now;
        var bvh = new AabbBvh(w.Aabbs);
        var buildUs = Clock.UsSince(tb);
        var mismatch = 0;
        for (var i = 0; i < N; i++)
            if (MathF.Abs(bvh.Raycast(origins[i], dirs[i], 200) - BruteForce(w, origins[i], dirs[i], 200)) > 1e-3f)
                mismatch++;
        Check("C# BVH ray cast = today's brute-force loop", mismatch == 0, $"{mismatch}/{N} differ");
        double bvhUs = double.MaxValue, bruteUs = double.MaxValue;
        for (var rep = 0; rep < 6; rep++)
        {
            var t0 = Clock.Now;
            for (var i = 0; i < N; i++)
                bvh.Raycast(origins[i], dirs[i], 200);
            if (rep > 0) bvhUs = Math.Min(bvhUs, Clock.UsSince(t0));
            t0 = Clock.Now;
            for (var i = 0; i < N; i++)
                BruteForce(w, origins[i], dirs[i], 200);
            if (rep > 0) bruteUs = Math.Min(bruteUs, Clock.UsSince(t0));
        }
        Metric($"ray us per ray at {w.Aabbs.Count} boxes: brute force / C# BVH", $"{F(bruteUs / N, 2)} / {F(bvhUs / N, 3)} (BVH build {F(buildUs, 0)} us, {bvh.NodeCount} nodes)");
        var big = new TestWorld(randomBoxes: 5000);
        var bigBvh = new AabbBvh(big.Aabbs);
        double bigBvhUs = double.MaxValue, bigBruteUs = double.MaxValue;
        for (var rep = 0; rep < 4; rep++)
        {
            var t0 = Clock.Now;
            for (var i = 0; i < N; i++)
                bigBvh.Raycast(origins[i], dirs[i], 200);
            if (rep > 0) bigBvhUs = Math.Min(bigBvhUs, Clock.UsSince(t0));
            t0 = Clock.Now;
            for (var i = 0; i < N; i++)
                BruteForce(big, origins[i], dirs[i], 200);
            if (rep > 0) bigBruteUs = Math.Min(bigBruteUs, Clock.UsSince(t0));
        }
        Metric($"ray us per ray at {big.Aabbs.Count} boxes: brute force / C# BVH", $"{F(bigBruteUs / N, 2)} / {F(bigBvhUs / N, 3)}");
    }

    static void BuildTimes(float3[]? origins = null, float3[]? dirs = null)
    {
        // Static world build times.
        var t = Clock.Now;
        var big = new TestWorld(randomBoxes: 5000);
        Metric("build: 5000 boxes as separate bodies + terrain ms", F(Clock.MsSince(t), 1));
        t = Clock.Now;
        var compound = StaticCompoundShapeSettings.Create();
        var crng = new Rng(55);
        for (var i = 0; i < 5000; i++)
        {
            var half = new float3(crng.Range(0.3f, 3f), crng.Range(0.2f, 2.5f), crng.Range(0.3f, 3f));
            compound.AddShape(new float3(crng.Range(-250, 250), half.Y, crng.Range(-250, 250)), floatQuat.Identity, Phys.Box(half, 0.05f));
        }
        var cp = new Phys();
        cp.AddStatic(compound.CreateShape(cp.Temp), float3.Zero, floatQuat.Identity);
        cp.System.OptimizeBroadPhase();
        Metric("build: 5000 boxes as one StaticCompoundShape ms", F(Clock.MsSince(t), 1));
        if (origins != null && dirs != null)
        {
            var cmpUs = TimeRays(cp, origins, dirs);
            var bigUs = TimeRays(big.P, origins, dirs);
            Metric("1000 rays vs 5000 boxes (separate bodies / one compound) us", $"{F(bigUs, 0)} / {F(cmpUs, 0)}");
        }

        t = Clock.Now;
        var n = 1024;
        var samples = new float[n * n];
        for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
                samples[z * n + x] = TestWorld.TerrainHeight(x - 512, z - 512) + (x % 7) * 0.05f;
        var genMs = Clock.MsSince(t);
        t = Clock.Now;
        var hf = Phys.MakeHeightField(samples, n, new float3(-512, 0, -512), new float3(1, 1, 1));
        Metric("build: 1024^2 heightfield ms", hf != null ? $"{F(Clock.MsSince(t), 1)} (sample gen {F(genMs, 1)})" : "unavailable in this binding (see section a for the fallback's cost)");

        t = Clock.Now;
        var tris = new Triangle[20000];
        for (var i = 0; i < tris.Length; i++)
        {
            var gx = (i / 2) % 100;
            var gz = (i / 2) / 100;
            var p0 = new float3(gx, (gx * gz % 5) * 0.1f, gz);
            tris[i] = i % 2 == 0
                ? Phys.T(p0, p0 + new float3(0, 0, 1), p0 + new float3(1, 0, 0))
                : Phys.T(p0 + new float3(1, 0, 0), p0 + new float3(0, 0, 1), p0 + new float3(1, 0, 1));
        }
        var meshShape = Phys.MakeMesh(tris);
        Metric("build: 20k-triangle MeshShape ms", F(Clock.MsSince(t), 1));
        _ = meshShape;
    }

    static double TimeRays(Phys p, float3[] origins, float3[] dirs)
    {
        var best = double.MaxValue;
        for (var rep = 0; rep < 4; rep++)
        {
            var t0 = Clock.Now;
            for (var i = 0; i < origins.Length; i++)
                p.Raycast(origins[i], dirs[i], 200, out _);
            if (rep > 0) best = Math.Min(best, Clock.UsSince(t0));
        }
        return best;
    }

    /// <summary>Today's World.RaycastWorld: slab test against every box.</summary>
    static float BruteForce(TestWorld w, float3 o, float3 d, float maxDist)
    {
        var best = maxDist;
        foreach (var (min, max) in w.Aabbs)
        {
            float tMin = 0, tMax = best;
            var miss = false;
            for (var axis = 0; axis < 3 && !miss; axis++)
            {
                var oo = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
                var dd = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
                var lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
                var hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
                if (MathF.Abs(dd) < 1e-6f)
                {
                    if (oo < lo || oo > hi) miss = true;
                    continue;
                }
                var inv = 1f / dd;
                var t1 = (lo - oo) * inv;
                var t2 = (hi - oo) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = MathF.Max(tMin, t1);
                tMax = MathF.Min(tMax, t2);
                if (tMin > tMax) miss = true;
            }
            if (!miss && tMin < best)
                best = tMin;
        }
        return best;
    }

    // ---------------------------------------------------------------- e
    const int Players = 8, History = 128;

    /// <summary>Ray vs capsule (segment a-b, radius r). Returns the distance along the unit ray or +inf.</summary>
    public static float RayCapsule(float3 o, float3 d, float3 a, float3 b, float r, float maxDist)
    {
        var best = float.PositiveInfinity;
        // Infinite cylinder around a-b, clipped to the segment.
        var ab = b - a;
        var ao = o - a;
        var abab = float3.Dot(ab, ab);
        var abd = float3.Dot(ab, d);
        var abao = float3.Dot(ab, ao);
        var qa = abab - abd * abd;
        var qb = abab * float3.Dot(ao, d) - abao * abd;
        var qc = abab * float3.Dot(ao, ao) - abao * abao - r * r * abab;
        if (qa > 1e-8f)
        {
            var disc = qb * qb - qa * qc;
            if (disc >= 0)
            {
                var t = (-qb - MathF.Sqrt(disc)) / qa;
                var y = abao + t * abd;
                if (t >= 0 && y > 0 && y < abab)
                    best = t;
            }
        }
        // End spheres.
        best = MathF.Min(best, RaySphere(o, d, a, r));
        best = MathF.Min(best, RaySphere(o, d, b, r));
        return best <= maxDist ? best : float.PositiveInfinity;
    }

    static float RaySphere(float3 o, float3 d, float3 c, float r)
    {
        var oc = o - c;
        var bq = float3.Dot(oc, d);
        var cq = float3.Dot(oc, oc) - r * r;
        var disc = bq * bq - cq;
        if (disc < 0)
            return float.PositiveInfinity;
        var t = -bq - MathF.Sqrt(disc);
        return t >= 0 ? t : float.PositiveInfinity;
    }

    public static void LagComp()
    {
        var w = new TestWorld();
        w.SetMovers(0);
        var rng = new Rng(31337);
        w.P.Raycast(new float3(0, 50, 0), new float3(0, -1, 0), 100, out _);
        var joltRays = Phys.RaysAvailable;
        // Ring buffers of feet positions per player, like Player.History (random walks in the open arena).
        var hist = new float3[Players, History];
        for (var p = 0; p < Players; p++)
        {
            var pos = new float3(rng.Range(-40, 30), 0, rng.Range(-40, 40));
            for (var t = 0; t < History; t++)
            {
                pos += new float3(rng.Range(-0.15f, 0.15f), 0, rng.Range(-0.15f, 0.15f));
                hist[p, t] = pos;
            }
        }

        // Option B: hitbox capsules as kinematic bodies on a query-only layer, moved to the historic pose per shot.
        var capsule = Phys.Keep(Phys.Offset(new float3(0, 0.9f, 0), floatQuat.Identity, Phys.Keep(Phys.Capsule(0.5f, 0.4f))));
        var bodies = new uint[Players];
        for (var p = 0; p < Players; p++)
            bodies[p] = w.P.AddKinematic(capsule, hist[p, History - 1], floatQuat.Identity, Layers.Hitbox);

        const int Shots = 1000;
        var shots = new (float3 O, float3 D, double Tick)[Shots];
        for (var i = 0; i < Shots; i++)
        {
            var target = rng.Int(Players);
            var tick = History - 1 - rng.Range(0, 24); // up to 400 ms back, fractional
            var aim = Interp(hist, target, tick) + new float3(rng.Range(-0.6f, 0.6f), rng.Range(0.2f, 1.9f), rng.Range(-0.6f, 0.6f));
            var o = new float3(rng.Range(-40, 30), 1.6f, rng.Range(-40, 40));
            var d = aim - o;
            shots[i] = (o, d / d.Length(), tick);
        }

        int agree = 0, hitsA = 0;
        double usA = double.MaxValue, usB = double.MaxValue;
        var resA = new (int P, float T)[Shots];
        var resB = new (int P, float T)[Shots];
        var ray = default(RRayCast);
        for (var rep = 0; rep < 4; rep++)
        {
            // Option A: world ray via Jolt, then the 8 historic capsules by hand (no Jolt state touched).
            var t0 = Clock.Now;
            for (var i = 0; i < Shots; i++)
            {
                var (o, d, tick) = shots[i];
                var best = joltRays ? w.P.Raycast(o, d, 200, out _) : 200f;
                var who = -1;
                for (var p = 0; p < Players; p++)
                {
                    var feet = Interp(hist, p, tick);
                    var tHit = RayCapsule(o, d, feet + new float3(0, 0.4f, 0), feet + new float3(0, 1.4f, 0), 0.4f, best);
                    if (tHit < best)
                    {
                        best = tHit;
                        who = p;
                    }
                }
                resA[i] = (who, best);
            }
            if (rep > 0) usA = Math.Min(usA, Clock.UsSince(t0));

            // Option B: move hitbox bodies to the historic pose, one filtered ray, move them back.
            if (!joltRays)
                continue;
            t0 = Clock.Now;
            for (var i = 0; i < Shots; i++)
            {
                var (o, d, tick) = shots[i];
                var best = w.P.Raycast(o, d, 200, out _);
                for (var p = 0; p < Players; p++)
                    w.P.Bodies.SetPosition(bodies[p], Interp(hist, p, tick), Activation.DontActivate);
                ray.Origin = o;
                ray.Direction = d * best;
                var who = -1;
                if (w.P.Query.CastRayClosest(in ray, w.P.RaySettings, out var hit, w.P.HitboxBp, w.P.HitboxObj, w.P.AnyBody, w.P.AnyShape))
                {
                    who = Array.IndexOf(bodies, hit.BodyID);
                    best *= hit.Fraction;
                }
                for (var p = 0; p < Players; p++)
                    w.P.Bodies.SetPosition(bodies[p], hist[p, History - 1], Activation.DontActivate);
                resB[i] = (who, best);
            }
            if (rep > 0) usB = Math.Min(usB, Clock.UsSince(t0));
        }
        for (var i = 0; i < Shots; i++)
        {
            if (resA[i].P >= 0) hitsA++;
            if (resA[i].P == resB[i].P && (resA[i].P < 0 || MathF.Abs(resA[i].T - resB[i].T) < 0.01f))
                agree++;
        }
        Metric("lag comp A (manual historic capsules) us per shot", F(usA / Shots, 2));
        if (joltRays)
        {
            Metric("lag comp B (Jolt hitbox layer, move bodies per shot) us per shot", F(usB / Shots, 2));
            Check("A and B agree on who was hit and where", agree >= Shots * 0.99, $"{agree}/{Shots} agree, {hitsA} player hits");
        }
        else
            Metric("lag comp A note", $"world ray skipped (no Jolt ray casts), capsules only; {hitsA}/{Shots} shots hit a player; option B not testable");

        // Our C# ray-capsule against Jolt's own RayCapsule helper (local space, capsule centred at the origin).
        var mismatch = 0;
        for (var i = 0; i < 1000; i++)
        {
            var o = new float3(rng.Range(-3, 3), rng.Range(-3, 3), rng.Range(-3, 3));
            var d = new float3(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1));
            d /= d.Length();
            var mine = RayCapsule(o, d, new float3(0, -0.5f, 0), new float3(0, 0.5f, 0), 0.4f, 100);
            var jolt = Jolt.RayCapsule(o, d * 100, 0.5f, 0.4f); // fraction of the 100 m ray
            var joltT = jolt <= 1 ? jolt * 100 : float.PositiveInfinity;
            var startsInside = float.IsInfinity(mine) != float.IsInfinity(joltT) && joltT < 1e-4f;
            if (!startsInside && (float.IsInfinity(mine) != float.IsInfinity(joltT) || (!float.IsInfinity(mine) && MathF.Abs(mine - joltT) > 1e-3f)))
                mismatch++;
        }
        Check("C# ray-capsule matches Jolt.RayCapsule", mismatch <= 5, $"{mismatch}/1000 differ");
    }

    static float3 Interp(float3[,] hist, int p, double tick)
    {
        var t0 = (int)Math.Floor(tick);
        var t1 = Math.Min(t0 + 1, History - 1);
        var f = (float)(tick - t0);
        return hist[p, t0] + (hist[p, t1] - hist[p, t0]) * f;
    }
}
#endif
