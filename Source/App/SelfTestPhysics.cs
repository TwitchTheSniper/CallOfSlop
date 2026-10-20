using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Physics" (Source/Physics): the BVH against brute force on every map (rays with normals and shape ids,
/// box and capsule overlaps, sphere casts, box lists), oriented boxes and meshes, determinism (two worlds, a shuffled world,
/// 32 bodies x 600 ticks twice, replays from mid-stream state), the swept mover's behaviour (steps, stairs, ramps, thin walls,
/// ceilings, mantling, terrain, movers) next to the classic mover's numbers, and costs. The old-vs-new parity of the classic
/// movement and every query is the "Collision parity" section (SelfTestNav.cs).
/// </summary>
public static partial class SelfTest
{
    /// <summary>One-line results gathered for the CI summary (lines starting "[physics]").</summary>
    private static readonly List<string> PhysicsSummary = new();

    private static void CheckPhysics()
    {
        var previous = World.Map;
        var previousMover = World.Mover;
        var sw = Stopwatch.StartNew();
        try
        {
            PhysicsBvhVsBruteForce();
            PhysicsShapes();
            PhysicsDeterminism();
            SweptBehaviour();
            SweptTerrain();
            SweptOnMovers();
            PhysicsCosts();
            SweptBotMatch();
        }
        finally
        {
            World.Mover = previousMover;
            MoverPhysics.Clock = 0;
            World.SetMap(previous);
        }
        PhysicsSummary.Add($"physics section took {sw.Elapsed.TotalSeconds:0.0} s");
        foreach (var s in PhysicsSummary)
            Line("[physics] " + s);
    }

    private static Vector3 RandomDir(Random rng, int i) => (i % 6) switch
    {
        0 => -Vector3.UnitY,
        1 => rng.Next(4) switch { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitZ },
        2 => Vector3.Normalize(new Vector3((float)rng.NextDouble() * 2 - 1, 0, (float)rng.NextDouble() * 2 - 1) + new Vector3(1e-4f, 0, 0)),
        _ => Vector3.Normalize(new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1) + new Vector3(0, 1e-4f, 0)),
    };

    private static float MapTop(Map m)
    {
        var top = 4f;
        foreach (var b in m.Solid)
            if (b.Center.Y > -500)
                top = MathF.Max(top, b.Max.Y + 2f);
        if (m.TerrainCollision is { } terrain)
            top = MathF.Max(top, terrain.Data.MaxHeight + 2f);
        return top;
    }

    // ---- BVH against brute force -----------------------------------------------------------------------------

    private static void PhysicsBvhVsBruteForce()
    {
        var rng = new Random(21);
        int rays = 0, rayBad = 0, boxes = 0, boxBad = 0, caps = 0, capBad = 0, casts = 0, castBad = 0, lists = 0, listBad = 0;
        var nodes = 0;
        var depth = 0;
        double buildMs = 0;
        foreach (var m in Maps.All)
        {
            World.SetMap(m);
            var phys = World.Physics;
            nodes = Math.Max(nodes, phys.Tree.NodeCount);
            depth = Math.Max(depth, phys.Tree.Depth);
            buildMs = Math.Max(buildMs, phys.BuildMs);
            var solid = phys.Boxes;
            var all = new List<ContactShape>();
            for (var i = 0; i < solid.Length; i++)
                if (phys.IsEnabled(i))
                    all.Add(ContactShape.FromObb(Obb.FromBox(solid[i]), new ShapeId(phys.IsProp(i) ? ShapeKind.Prop : ShapeKind.Box, i)));
            var allSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(all);
            var top = MapTop(m);
            var bad = 0;
            Vector3 RandomPoint() => new(Rand(-m.HalfX - 4, m.HalfX + 4), Rand(-0.5f, top), Rand(-m.HalfZ - 4, m.HalfZ + 4));

            // Rays: distance, shape and normal must be exactly the brute-force answer.
            for (var k = 0; k < 300; k++)
            {
                var o = RandomPoint();
                var dir = RandomDir(rng, k);
                var len = k % 3 == 0 ? Rand(0.05f, 3f) : Rand(1f, 160f);
                var best = len;
                var bi = -1;
                for (var i = 0; i < solid.Length; i++)
                    if (phys.IsEnabled(i) && solid[i].Raycast(o, dir, best) is { } t && t < best)
                    {
                        best = t;
                        bi = i;
                    }
                var hit = phys.Raycast(o, dir, len, QueryFilter.BoxesOnly, out var h);
                rays++;
                var ok = hit == (bi >= 0) && (!hit || (h.Distance == best && h.Shape.Index == bi
                    && h.Normal == Geometry.BoxEntryNormal(solid[bi].Min, solid[bi].Max, o, dir, best) && h.Point == o + dir * best));
                if (!ok)
                {
                    rayBad++;
                    if (bad++ < 3)
                        Line($"  {m.Name}: ray {o} {dir} len {len:0.00}: brute force {best} box {bi}, BVH {(hit ? $"{h.Distance} {h.Shape} n {h.Normal}" : "miss")}");
                }
            }

            // Oriented boxes (yaw-only and fully turned) against the boxes.
            for (var k = 0; k < 80; k++)
            {
                var c = RandomPoint();
                var half = new Vector3(Rand(0.1f, 3f), Rand(0.1f, 2f), Rand(0.1f, 3f));
                var rot = k % 2 == 0 ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, Rand(-3, 3))
                    : Quaternion.Normalize(new Quaternion(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1), Rand(-1, 1)));
                var obb = new Obb(c, half, rot);
                var want = false;
                for (var i = 0; i < solid.Length && !want; i++)
                    want = phys.IsEnabled(i) && Geometry.ObbOverlap(obb, Obb.FromBox(solid[i]));
                boxes++;
                if (phys.OverlapBox(obb, new QueryFilter(QueryLayers.StaticShapes)) != want)
                {
                    boxBad++;
                    if (bad++ < 3)
                        Line($"  {m.Name}: box at {c} half {half}: brute force {want}, BVH {!want}");
                }
            }

            // Capsule overlaps and sphere casts: the BVH's gathered shapes against every box.
            for (var k = 0; k < 60; k++)
            {
                var feet = RandomPoint();
                var cap = SweptMover.CapsuleAt(feet, (Stance)(k % 3), Vector3.UnitY);
                var want = CapsuleContact.Overlaps(allSpan, cap.A, cap.B, cap.Radius);
                caps++;
                if (phys.OverlapCapsule(cap, new QueryFilter(QueryLayers.StaticShapes)) != want)
                {
                    capBad++;
                    if (bad++ < 3)
                        Line($"  {m.Name}: capsule at {feet}: brute force {want}, BVH {!want}");
                }
                var o = RandomPoint();
                var dir = RandomDir(rng, k);
                var len = Rand(0.5f, 25f);
                var r = Rand(0.05f, 0.5f);
                var bHit = CapsuleContact.Cast(allSpan, o, o, r, dir, len, out var toi, out var n, out _, out var idx);
                var got = phys.SphereCast(o, r, dir, len, new QueryFilter(QueryLayers.StaticShapes), out var sh);
                casts++;
                if (bHit != got || (got && (sh.Distance != toi || sh.Shape != all[idx].Id || sh.Normal != n)))
                {
                    castBad++;
                    if (bad++ < 3)
                        Line($"  {m.Name}: sphere cast {o} {dir} r {r:0.00}: brute force {(bHit ? $"{toi} {all[idx].Id}" : "miss")}, BVH {(got ? $"{sh.Distance} {sh.Shape}" : "miss")}");
                }
            }

            // Box lists for the classic mover: every box touching the rectangle, sorted, no repeats.
            for (var k = 0; k < 60; k++)
            {
                var p = RandomPoint();
                float w = Rand(0, 3), d = Rand(0, 3);
                var got = phys.CollectBoxes(p.X, p.Z, p.X + w, p.Z + d).ToArray();
                lists++;
                var sorted = true;
                for (var i = 1; i < got.Length; i++)
                    sorted &= got[i] > got[i - 1];
                var missing = 0;
                for (var i = 0; i < solid.Length; i++)
                {
                    var b = solid[i];
                    if (phys.IsEnabled(i) && b.Max.X >= p.X && b.Min.X <= p.X + w && b.Max.Z >= p.Z && b.Min.Z <= p.Z + d && Array.BinarySearch(got, i) < 0)
                        missing++;
                }
                if (!sorted || missing > 0)
                {
                    listBad++;
                    if (bad++ < 3)
                        Line($"  {m.Name}: box list at {p}: sorted {sorted}, {missing} missing");
                }
            }
            if (bad > 0)
                Fail($"{m.Name}: the BVH answered {bad} queries differently from brute force");
        }
        Line($"BVH vs brute force on {Maps.All.Length} maps: rays {rays - rayBad}/{rays}, box overlaps {boxes - boxBad}/{boxes}, capsule overlaps {caps - capBad}/{caps}, "
            + $"sphere casts {casts - castBad}/{casts}, box lists {lists - listBad}/{lists} identical; largest tree {nodes} nodes, depth {depth}, slowest build {buildMs:0.00} ms");
        PhysicsSummary.Add($"bvh=brute: rays {rays - rayBad}/{rays}, obb {boxes - boxBad}/{boxes}, capsule {caps - capBad}/{caps}, casts {casts - castBad}/{casts}, lists {lists - listBad}/{lists}; max {nodes} nodes depth {depth} build {buildMs:0.00} ms");
        return;

        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    }

    // ---- Oriented boxes and meshes ------------------------------------------------------------------------------

    private static void PhysicsShapes()
    {
        var rng = new Random(5);
        var worst = 0f;
        var worstCast = 0f;
        var disagree = 0;
        var tests = 0;
        for (var k = 0; k < 6; k++)
        {
            var rot = k < 3 ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f + k) : Quaternion.Normalize(new Quaternion(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1), Rand(-1, 1)));
            var obb = new Obb(new Vector3(Rand(-3, 3), Rand(0, 3), Rand(-3, 3)), new Vector3(Rand(0.3f, 2), Rand(0.3f, 2), Rand(0.3f, 2)), rot);
            var asBox = new PhysicsWorld(new List<Box>(), null, 50, 50);
            asBox.AddObb(obb);
            var asMesh = new PhysicsWorld(new List<Box>(), null, 50, 50);
            asMesh.AddMesh(TriangleMeshCollider.FromObb(obb));
            for (var i = 0; i < 200; i++)
            {
                // From outside, towards the box (mostly): the same distance and normal both ways.
                var o = obb.Center + Vector3.Normalize(new Vector3(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1))) * 8;
                var dir = Vector3.Normalize(obb.Center + new Vector3(Rand(-1.5f, 1.5f), Rand(-1.5f, 1.5f), Rand(-1.5f, 1.5f)) - o);
                var a = asBox.Raycast(o, dir, 20, QueryFilter.All, out var ha);
                var b = asMesh.Raycast(o, dir, 20, QueryFilter.All, out var hb);
                tests++;
                if (a != b)
                {
                    disagree++;
                    continue;
                }
                if (a)
                {
                    worst = MathF.Max(worst, MathF.Abs(ha.Distance - hb.Distance));
                    if (Vector3.Dot(ha.Normal, hb.Normal) < 0.999f && MathF.Abs(ha.Distance - hb.Distance) < 1e-4f)
                        disagree++; // an edge hit can pick either face; only count clear face hits
                }
                var r = Rand(0.1f, 0.6f);
                var ca = asBox.SphereCast(o, r, dir, 20, QueryFilter.All, out var sa);
                var cb = asMesh.SphereCast(o, r, dir, 20, QueryFilter.All, out var sb);
                if (ca != cb)
                    disagree++;
                else if (ca)
                    worstCast = MathF.Max(worstCast, MathF.Abs(sa.Distance - sb.Distance));
            }
        }
        Line($"oriented boxes vs the same boxes as triangle meshes: {tests} rays, {disagree} disagreements, distances within {worst:0e0} m (rays) and {worstCast:0e0} m (sphere casts)");
        if (disagree > tests / 100 || worst > 1e-3f || worstCast > 5e-3f)
            Fail($"oriented box and mesh colliders disagree ({disagree} of {tests}, ray {worst} m, cast {worstCast} m)");

        // A ray straight down onto a 30 degree ramp hits it where the geometry says, with the ramp's normal.
        var ramp = new Obb(new Vector3(0, 0, 0), new Vector3(2, 0.25f, 4), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 30 * MathF.PI / 180));
        var w = new PhysicsWorld(new List<Box>(), null, 50, 50);
        w.AddObb(ramp);
        var down = w.Raycast(new Vector3(0, 10, 1), -Vector3.UnitY, 20, QueryFilter.All, out var rh);
        var surface = ramp.ToWorld(new Vector3(0, 0.25f, 0));
        var wantY = surface.Y - MathF.Tan(30 * MathF.PI / 180) * (1 - surface.Z); // on the top face's plane, above (0, 1)
        if (!down || MathF.Abs(rh.Point.Y - wantY) > 1e-3f || MathF.Abs(rh.Normal.Y - MathF.Cos(30 * MathF.PI / 180)) > 1e-4f)
            Fail($"ray onto a 30 degree ramp: hit {down} at y {rh.Point.Y:0.0000} (want {wantY:0.0000}), normal {rh.Normal}");
        if (!w.PointInside(ramp.Center, 0.01f, QueryFilter.All) || w.PointInside(ramp.Center + Vector3.UnitY * 3, 0.01f, QueryFilter.All))
            Fail("point tests against an oriented box are wrong");
        return;

        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    }

    // ---- Determinism --------------------------------------------------------------------------------------------

    private static ulong Fnv(ulong h, float v) => (h ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211UL;

    private static ulong HashState(ulong h, in MoveState s)
    {
        h = Fnv(h, s.Position.X);
        h = Fnv(h, s.Position.Y);
        h = Fnv(h, s.Position.Z);
        h = Fnv(h, s.Velocity.X);
        h = Fnv(h, s.Velocity.Y);
        h = Fnv(h, s.Velocity.Z);
        return (h ^ (s.Grounded ? 1UL : 0) ^ ((ulong)s.Stance << 1) ^ ((ulong)s.MantleTicks << 3)) * 1099511628211UL;
    }

    /// <summary>A stream of held-key input like a player's: a direction for half a second, sometimes sprint, crouch or jump.</summary>
    private static InputCmd[] InputStream(int seed, int ticks, uint firstTick = 0)
    {
        var rng = new Random(seed);
        var cmds = new InputCmd[ticks];
        var yaw = (float)(rng.NextDouble() * MathF.Tau);
        var held = Buttons.Forward;
        for (var i = 0; i < ticks; i++)
        {
            if (i % 30 == 0)
                held = (Buttons)rng.Next(16) | (rng.Next(2) == 0 ? Buttons.Sprint : Buttons.None) | (rng.Next(8) == 0 ? Buttons.Crouch : Buttons.None)
                    | (rng.Next(25) == 0 ? Buttons.Prone : Buttons.None);
            yaw += (float)(rng.NextDouble() - 0.5) * 0.15f;
            cmds[i] = new InputCmd
            {
                Buttons = held | (rng.Next(20) == 0 ? Buttons.Jump : Buttons.None),
                Yaw = yaw,
                SpeedPct = 100,
                Tick = firstTick == 0 ? 0 : firstTick + (uint)i,
            };
        }
        return cmds;
    }

    /// <summary>32 bodies from the map's spawns, each with its own input stream, all ticked together; returns a hash of every state.</summary>
    private static ulong RunBodies(Map m, InputCmd[][] streams, MoveState[]? keep = null, int keepAt = -1)
    {
        var n = streams.Length;
        var states = new MoveState[n];
        for (var i = 0; i < n; i++)
        {
            var sp = m.Spawns[i % m.Spawns.Count];
            states[i] = new MoveState { Position = sp + new Vector3(0.3f * (i / m.Spawns.Count), 0, 0), Grounded = true };
        }
        var h = 14695981039346656037UL;
        for (var t = 0; t < streams[0].Length; t++)
        {
            for (var i = 0; i < n; i++)
            {
                if (keep != null && t == keepAt)
                    keep[i] = states[i];
                World.Simulate(ref states[i], streams[i][t]);
                h = HashState(h, states[i]);
            }
        }
        return h;
    }

    private static void PhysicsDeterminism()
    {
        var rng = new Random(9);
        var rayDiff = 0;
        var normalDiff = 0;
        var shuffledDiff = 0;
        var shuffledNormalDiff = 0;
        var total = 0;
        foreach (var name in new[] { "Proto Box", "Downtown", "Physics Test", "Galleria" }.Concat(Maps.All.Where(m => m.HasTerrain).Take(2).Select(m => m.Name)))
        {
            if (Maps.Find(name) is not { } m)
                continue;
            World.SetMap(m);
            var a = World.Physics;
            var b = new PhysicsWorld(m);
            var order = Enumerable.Range(0, m.Solid.Count).OrderBy(_ => rng.Next()).ToArray();
            var shuffled = new PhysicsWorld(order.Select(i => m.Solid[i]).ToList(), m.TerrainCollision, m.HalfX, m.HalfZ);
            var top = MapTop(m);
            for (var k = 0; k < 400; k++)
            {
                var o = new Vector3(Rand(-m.HalfX, m.HalfX), Rand(0, top), Rand(-m.HalfZ, m.HalfZ));
                var dir = RandomDir(rng, k);
                var len = Rand(1, 150);
                var filter = QueryFilter.StaticOnly;
                a.Raycast(o, dir, len, filter, out var ha);
                b.Raycast(o, dir, len, filter, out var hb);
                shuffled.Raycast(o, dir, len, filter, out var hs);
                total++;
                if (ha.Distance != hb.Distance || ha.Point != hb.Point || ha.Shape != hb.Shape)
                    rayDiff++;
                if (ha.Normal != hb.Normal)
                    normalDiff++;
                if (ha.Distance != hs.Distance || ha.Point != hs.Point)
                    shuffledDiff++;
                else if (ha.Normal != hs.Normal)
                    shuffledNormalDiff++; // the same distance on two boxes (a corner): either face is right
            }
        }
        Line($"determinism: {total} rays in two separately built worlds: {rayDiff} differ ({normalDiff} normals); in a world built from shuffled boxes: "
            + $"{shuffledDiff} distances differ, {shuffledNormalDiff} normals differ (ties between boxes at the same distance)");
        if (rayDiff > 0 || normalDiff > 0 || shuffledDiff > 0)
            Fail($"ray casts are not deterministic ({rayDiff} / {normalDiff} / {shuffledDiff} differences)");
        if (shuffledNormalDiff > total / 200)
            Fail($"too many normals depend on the box order ({shuffledNormalDiff} of {total})");

        // Movement: 32 bodies x 600 ticks of input on two maps, twice (the second time on a rebuilt world), both movers.
        var lines = new List<string>();
        foreach (var name in new[] { "Downtown", "Physics Test" }.Concat(Maps.All.Where(m => m.HasTerrain).Take(1).Select(m => m.Name)))
        {
            if (Maps.Find(name) is not { } m)
                continue;
            var streams = Enumerable.Range(0, 32).Select(i => InputStream(100 + i, 600, m.Movers.Count > 0 ? 300_000u : 0)).ToArray();
            foreach (var mover in new IMover[] { ClassicMover.Instance, SweptMover.Instance })
            {
                World.Mover = mover;
                World.SetMap(m);
                var keep = new MoveState[32];
                var h1 = RunBodies(m, streams, keep, 300);
                World.SetMap(m); // a fresh physics world
                var h2 = RunBodies(m, streams);
                // Replay from the snapshot state at tick 300 (all a client has when it reconciles): body 0 alone.
                var s = keep[0];
                var replay = s;
                World.SetMap(m);
                for (var t = 300; t < 600; t++)
                    World.Simulate(ref replay, streams[0][t]);
                var s2 = keep[0];
                for (var t = 300; t < 600; t++)
                {
                    World.Simulate(ref s2, streams[0][t]);
                    // interleave another body's ticks, as the server does
                    var other = keep[1];
                    World.Simulate(ref other, streams[1][t]);
                }
                var replayOk = HashState(0, replay) == HashState(0, s2);
                lines.Add($"{name} {mover.Mode}: {(h1 == h2 ? "same" : "DIFFERENT")} ({h1:x16}), replay {(replayOk ? "same" : "DIFFERENT")}");
                if (h1 != h2)
                    Fail($"{name}: {mover.Mode} movement differs between two runs ({h1:x16} vs {h2:x16})");
                if (!replayOk)
                    Fail($"{name}: {mover.Mode} movement replayed from tick 300 state differs (hidden state in the mover?)");
            }
        }
        World.Mover = ClassicMover.Instance;
        Line("determinism, 32 bodies x 600 ticks twice: " + string.Join("; ", lines));
        PhysicsSummary.Add($"determinism: rays {rayDiff}/{normalDiff}/{shuffledDiff} diffs of {total}; " + string.Join("; ", lines));
        return;

        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    }
}
