using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Gravity sources" (phase 5, docs/space/GRAVITY.md): gravity as a sum of sources. The exact tetrahedral field against
    /// brute force (each tetrahedron split into 512 point masses) round a box; Poisson's equation inside and out; no jump
    /// across a face; a box's inner faces cancel; a hollow shell's cavity nearly weightless; every level of detail (point,
    /// mascons, exact) within 1% of the exact field; a stitched pair: weightless in the middle of the neck, and up along a
    /// walk over the neck turns smoothly and points out of the neck; force hooks in the bubble (players, constructs and
    /// falling things) and in the ship integrator (a lumpy body changes an orbit, a round one doesn't); map files read and
    /// refuse a sum; and queries allocate nothing.
    /// </summary>
    private static void CheckGravitySources()
    {
        GravityBoxAgainstBrute();
        GravityShellAndLevels();
        GravityStitchedPair();
        GravityHooks();
        GravityMapFiles();
        GravityTwinWorlds();
    }

    /// <summary>
    /// The Twin Worlds Dev map (tools/space/make_twin_worlds.py): a walk from the west world's top over the neck to the east
    /// world's top, each metre along the ground's tangent and dropped back onto the ground along the local down, finds
    /// ground under every step, up turns smoothly, and it ends on the east world.
    /// </summary>
    private static void GravityTwinWorlds()
    {
        if (Maps.Find("Twin Worlds") is not { } map)
        {
            Fail("gravity sources: the Twin Worlds map is missing");
            return;
        }
        var old = World.Map;
        try
        {
            World.SetMap(map);
            if (map.Gravity is not SummedGravity)
            {
                Fail("gravity sources: Twin Worlds should have a summed field");
                return;
            }
            const float d = 80f;
            var p = new Vector3(-d, 60f, 0.37f); // (just off the plane of the mesh's seams)
            var prevUp = GravityFrames.UpAt(p);
            var maxTurn = 0f;
            var steps = 0;
            var lowest = float.MaxValue;
            var steepest = 0f;
            for (; steps < 400 && p.X < d; steps++)
            {
                var up = GravityFrames.UpAt(p);
                var east = Vector3.UnitX - up * Vector3.Dot(Vector3.UnitX, up);
                if (east.LengthSquared() < 1e-4f)
                {
                    Fail($"gravity sources: Twin Worlds walk: up points along the walk at {p}");
                    return;
                }
                var q = p + Vector3.Normalize(east) * 1f;
                var u2 = GravityFrames.UpAt(q);
                var hit = World.Query.Raycast(q + u2 * 3f, -u2, 8f, QueryFilter.All);
                if (hit >= 8f)
                {
                    Fail($"gravity sources: Twin Worlds walk: no ground under step {steps} at {q} (up {u2})");
                    return;
                }
                p = q + u2 * (3f - hit);
                maxTurn = MathF.Max(maxTurn, MathF.Acos(Math.Clamp(Vector3.Dot(u2, prevUp), -1f, 1f)) * 180f / MathF.PI);
                prevUp = u2;
                if (MathF.Abs(p.X) < 16f) // (the neck itself, clear of where it runs into the worlds)
                {
                    lowest = MathF.Min(lowest, MathF.Sqrt(p.Y * p.Y + p.Z * p.Z));
                    var normal = Vector3.Normalize(new Vector3(0, p.Y, p.Z)); // (the neck's side, out of its axis)
                    steepest = MathF.Max(steepest, MathF.Acos(Math.Clamp(Vector3.Dot(u2, normal), -1f, 1f)) * 180f / MathF.PI);
                }
            }
            Line($"  gravity sources: Twin Worlds walk: {steps} steps to {p}, largest turn of up {maxTurn:0.0} deg, lowest over the neck {lowest:0.0} m from its axis, steepest the neck feels {steepest:0} deg");
            if (steepest > World.MaxSlopeDeg - 10f)
                Fail($"gravity sources: Twin Worlds: the neck feels {steepest:0} degrees steep somewhere (players slide past {World.MaxSlopeDeg:0})");
            if (p.X < d - 1f || p.Y < 55f)
                Fail($"gravity sources: Twin Worlds walk should end on the east world's top (ended at {p} after {steps} steps)");
            if (maxTurn > 15f)
                Fail($"gravity sources: Twin Worlds walk: up turned {maxTurn:0.0} degrees in a 1 m step");
            if (lowest is < 13f or > 15.5f)
                Fail($"gravity sources: Twin Worlds walk: the neck's top should be about 14 m from its axis ({lowest:0.0} m)");
        }
        finally
        {
            World.SetMap(old);
        }
    }

    /// <summary>Each tetrahedron split into 8^depth point masses (the reference).</summary>
    private static Vec3d BruteTetra(GravityShapes.Mesh m, double mu, Vec3d p, int depth)
    {
        var total = GravityShapes.Mass(m);
        var g = Vec3d.Zero;
        for (var i = 0; i < m.Tetras.Count; i++)
        {
            var t = m.Tetras[i];
            var v = m.Vertices;
            var gr = mu * m.Density[i] / total; // GM per unit volume
            BruteSplit(v[t[0]], v[t[1]], v[t[2]], v[t[3]], gr, p, depth, ref g);
        }
        return g;
    }

    private static void BruteSplit(Vec3d a, Vec3d b, Vec3d c, Vec3d d, double gr, Vec3d p, int depth, ref Vec3d g)
    {
        if (depth == 0)
        {
            var vol = Math.Abs(Vec3d.Dot(b - a, Vec3d.Cross(c - a, d - a))) / 6;
            var r = p - (a + b + c + d) * 0.25;
            var r2 = r.LengthSquared;
            g -= r * (gr * vol / (r2 * Math.Sqrt(r2)));
            return;
        }
        Vec3d ab = (a + b) * 0.5, ac = (a + c) * 0.5, ad = (a + d) * 0.5, bc = (b + c) * 0.5, bd = (b + d) * 0.5, cd = (c + d) * 0.5;
        depth--;
        BruteSplit(a, ab, ac, ad, gr, p, depth, ref g);
        BruteSplit(ab, b, bc, bd, gr, p, depth, ref g);
        BruteSplit(ac, bc, c, cd, gr, p, depth, ref g);
        BruteSplit(ad, bd, cd, d, gr, p, depth, ref g);
        // The octahedron in the middle, split along ab-cd.
        BruteSplit(ab, cd, ac, ad, gr, p, depth, ref g);
        BruteSplit(ab, cd, ad, bd, gr, p, depth, ref g);
        BruteSplit(ab, cd, bd, bc, gr, p, depth, ref g);
        BruteSplit(ab, cd, bc, ac, gr, p, depth, ref g);
    }

    private static double RelErr(Vec3d a, Vec3d b) => (a - b).Length / Math.Max(b.Length, 1e-30);

    private static void GravityBoxAgainstBrute()
    {
        // A 40 x 20 x 30 m box, GM 2e6 (about 1 m/s^2 at its face), with a heavy corner tetrahedron.
        var half = new Vec3d(20, 10, 15);
        var m = GravityShapes.Box(new Vec3d(5, -3, 2), half);
        m.Density[2] = 3;
        const double mu = 2e6;
        var box = m.Source(mu, "box");
        var uniform = GravityShapes.Box(new Vec3d(5, -3, 2), half).Source(mu);
        if (uniform.FaceCount != 12)
            Fail($"gravity sources: a uniform box should have 12 boundary faces after the inner ones cancel, has {uniform.FaceCount}");

        // Outside, near and far: exact against 6 x 4,096 point masses.
        var rng = new Random(5);
        var worst = 0.0;
        for (var k = 0; k < 8; k++)
        {
            var dir = new Vec3d(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1).Normalized();
            var dist = 12 + rng.NextDouble() * 120; // from the box's surface to far away
            var p = box.Center + new Vec3d(dir.X * (half.X + dist), dir.Y * (half.Y + dist), dir.Z * (half.Z + dist));
            worst = Math.Max(worst, RelErr(box.Exact(p), BruteTetra(m, mu, p, 4)));
        }
        Line($"  gravity sources: box exact vs brute force (outside): worst {worst:g3}");
        if (worst > 2e-3)
            Fail($"gravity sources: the tetrahedral field is {worst:P2} off brute force outside a box (want under 0.2%)");

        // Poisson: div g = -4 pi G rho inside, 0 outside (finite differences of the exact field).
        var vol = 8 * half.X * half.Y * half.Z;
        var gRho = mu / vol;
        double Div(Vec3d p)
        {
            const double h = 0.01;
            return (uniform.Exact(p + new Vec3d(h, 0, 0)).X - uniform.Exact(p - new Vec3d(h, 0, 0)).X
                  + uniform.Exact(p + new Vec3d(0, h, 0)).Y - uniform.Exact(p - new Vec3d(0, h, 0)).Y
                  + uniform.Exact(p + new Vec3d(0, 0, h)).Z - uniform.Exact(p - new Vec3d(0, 0, h)).Z) / (2 * h);
        }
        var inside = Div(uniform.Center + new Vec3d(7, 3, -4));
        var outside = Div(uniform.Center + new Vec3d(0, 40, 0));
        if (Math.Abs(inside / (-4 * Math.PI * gRho) - 1) > 1e-4 || Math.Abs(outside) > 1e-6 * 4 * Math.PI * gRho)
            Fail($"gravity sources: Poisson's equation fails (div g inside {inside:g4}, want {-4 * Math.PI * gRho:g4}; outside {outside:g3}, want 0)");

        // No jump across a face (the field is continuous through the surface).
        var top = uniform.Center + new Vec3d(3, half.Y, 2);
        var jump = RelErr(uniform.Exact(top + new Vec3d(0, 1e-4, 0)), uniform.Exact(top - new Vec3d(0, 1e-4, 0)));
        if (jump > 1e-4)
            Fail($"gravity sources: the field jumps {jump:P3} across a box's face");
    }

    private static void GravityShellAndLevels()
    {
        // A hollow world: the cavity is nearly weightless (exactly so for a perfect sphere).
        var shell = GravityShapes.Shell(Vec3d.Zero, 800, 1000, 3).Source(1e7, "shell");
        var surface = shell.Exact(new Vec3d(0, 1000.5, 0)).Length;
        var cavity = 0.0;
        foreach (var p in new[] { Vec3d.Zero, new Vec3d(300, 100, 0), new Vec3d(-200, 400, 300), new Vec3d(0, -600, 0) })
            cavity = Math.Max(cavity, shell.Exact(p).Length);
        Line($"  gravity sources: hollow shell: {surface:0.000} m/s^2 on top, at most {cavity:0.0000} in the cavity ({shell.FaceCount} faces)");
        if (cavity > 0.02 * surface)
            Fail($"gravity sources: a hollow shell's cavity pulls {cavity / surface:P1} of its surface gravity (want under 2%)");

        // Levels of detail: the model's answer (point far away, mascons, exact) within 1% of the exact field everywhere.
        var ball = GravityShapes.Ball(new Vec3d(100, 0, 0), 300, 2).Source(9e5, "ball");
        var lumps = new MasconSource([new Vec3d(-500, 0, 0), new Vec3d(-420, 60, 0), new Vec3d(-470, -40, 50)], [1, 2, 1], 4e5, 20, "lumps");
        var model = new GravityModel([ball, lumps, new PointSource(new Vec3d(0, 900, 0), 3e5, 100, "pebble")]);
        var rng = new Random(11);
        var worst = 0.0;
        long far0 = GravitySource.Counters.Far, mid0 = GravitySource.Counters.Mid, ex0 = GravitySource.Counters.Exact;
        for (var k = 0; k < 400; k++)
        {
            var dir = new Vec3d(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1).Normalized();
            var p = dir * (350 + Math.Pow(rng.NextDouble(), 2) * 40_000);
            worst = Math.Max(worst, RelErr(model.Accel(p), model.Exact(p)));
        }
        long far = GravitySource.Counters.Far - far0, mid = GravitySource.Counters.Mid - mid0, ex = GravitySource.Counters.Exact - ex0;
        Line($"  gravity sources: levels of detail vs exact: worst {worst:g3} (far {far}, mascons {mid}, exact {ex}, incl. the reference)");
        if (worst > 0.01)
            Fail($"gravity sources: the cheap levels are {worst:P2} off the exact field (want under 1%)");
        if (far == 0 || ex <= 400)
            Fail("gravity sources: the levels of detail weren't all used (far, exact)");

        // No allocation per query.
        var sum = new SummedGravity(model, -Vector3.UnitZ);
        var acc = Vector3.Zero;
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 200; k++)
        {
            var p = new Vector3(k * 37 % 900 - 450, 320 + k, k * 13 % 500);
            acc += sum.GravityAt(p) + sum.UpAt(p) + new Vector3(sum.HeightOf(p));
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        if (alloc != 0 || !float.IsFinite(acc.X))
            Fail($"gravity sources: 200 summed-field queries allocated {alloc} B");
    }

    /// <summary>The stitched pair the self-test and the twin planetoid use: two round balls joined by a tetrahedral neck.</summary>
    private static GravityModel StitchedPair(double radius, double gap, double neckRadius, double surface)
    {
        var mu = surface * radius * radius;
        var d = radius + gap / 2; // centres at -d and +d on x
        var neck = GravityShapes.Neck(new Vec3d(-d + radius * 0.9, 0, 0), new Vec3d(d - radius * 0.9, 0, 0), neckRadius, 16, 6);
        var neckVol = GravityShapes.Mass(neck);
        var ballVol = 4.0 / 3 * Math.PI * radius * radius * radius;
        return new GravityModel([
            new PointSource(new Vec3d(-d, 0, 0), mu, radius, "west"),
            new PointSource(new Vec3d(d, 0, 0), mu, radius, "east"),
            neck.Source(mu * neckVol / ballVol, "neck"), // the same density as the balls
        ]);
    }

    private static void GravityStitchedPair()
    {
        const double r = 400, gap = 300, neckR = 90;
        var model = StitchedPair(r, gap, neckR, 9.81);
        var field = new SummedGravity(model, -Vector3.UnitZ);
        // Weightless in the middle of the neck (by symmetry), and the neck itself against brute force inside it.
        var mid = model.Accel(Vec3d.Zero).Length;
        if (mid > 1e-6)
            Fail($"gravity sources: the middle of a stitched pair's neck should be weightless (pulls {mid:g3} m/s^2)");
        var neck = (TetraSource)model.Sources[2];
        var neckMesh = GravityShapes.Neck(new Vec3d(-(r + gap / 2) + r * 0.9, 0, 0), new Vec3d(r + gap / 2 - r * 0.9, 0, 0), neckR, 16, 6);
        var probe = new Vec3d(40, 0, neckR + 30); // just outside the neck's side
        var e = RelErr(neck.Exact(probe), BruteTetra(neckMesh, neck.Mu, probe, 3));
        if (e > 5e-3)
            Fail($"gravity sources: the neck's field is {e:P2} off brute force beside it");

        // Walk over the neck: from the top of the west ball, along the neck's top, onto the east ball. Up turns smoothly
        // (never more than 6 degrees between steps 2 m apart) and points out of the neck at its middle.
        var maxTurn = 0.0;
        var prev = Vector3.Zero;
        var path = new List<Vector3>();
        var d = (float)(r + gap / 2);
        var y = (float)neckR + 1; // the neck's top
        var a0 = MathF.Asin(y / (float)r); // where the west ball's surface comes down to the neck's top
        for (var a = MathF.PI / 2; a > a0; a -= 2f / (float)r) // over the west ball, 2 m a step
            path.Add(new Vector3(-d + (float)r * MathF.Cos(a), (float)r * MathF.Sin(a), 0));
        for (var x = -d + (float)r * MathF.Cos(a0); x <= d - (float)r * MathF.Cos(a0); x += 2f) // along the neck
            path.Add(new Vector3(x, y, 0));
        for (var a = a0; a < MathF.PI / 2; a += 2f / (float)r) // up the east ball
            path.Add(new Vector3(d - (float)r * MathF.Cos(a), (float)r * MathF.Sin(a), 0));
        var midUp = field.UpAt(new Vector3(0, y, 0));
        foreach (var p in path)
        {
            var up = field.UpAt(p);
            if (prev != Vector3.Zero)
                maxTurn = Math.Max(maxTurn, MathF.Acos(Math.Clamp(Vector3.Dot(up, prev), -1f, 1f)) * 180 / MathF.PI);
            prev = up;
        }
        Line($"  gravity sources: stitched pair walk: {path.Count} steps, largest turn of up {maxTurn:0.00} deg, up at the neck's middle {midUp}");
        if (maxTurn > 6)
            Fail($"gravity sources: up turned {maxTurn:0.0} degrees in one step over the neck (want a smooth walk)");
        if (midUp.Y < 0.95f)
            Fail($"gravity sources: up on top of the neck's middle should point out of the neck (+Y), is {midUp}");
        if (MathF.Abs(field.HeightOf(new Vector3(-d, (float)r, 0)) - (float)r) > 0.01f)
            Fail("gravity sources: height on the west ball should be measured from its centre");
    }

    private static void GravityHooks()
    {
        // In the bubble: a hook adds to the pull of everything that falls, and changing the map clears it.
        var old = GravityFrames.Field;
        try
        {
            GravityFrames.Use(null);
            var p = new Vector3(3, 2, 1);
            var g0 = GravityFrames.GravityAt(p);
            var hook = new PointHook(new Vector3(3, 12, 1), 500, 40);
            GravityFrames.AddHook(hook);
            var g1 = GravityFrames.GravityAt(p);
            var want = hook.AccelAt(p);
            if ((g1 - g0 - want).Length() > 1e-4f || want.Y <= 0)
                Fail($"gravity sources: a hook above should pull up in the bubble (got {g1 - g0}, want {want})");
            if (hook.AccelAt(new Vector3(3, 60, 1)) != Vector3.Zero)
                Fail("gravity sources: a hook should do nothing past its range");
            GravityFrames.Use(null);
            if (GravityFrames.HookCount != 0)
                Fail("gravity sources: changing the map should clear the hooks");
        }
        finally
        {
            GravityFrames.Use(old);
        }

        // In the ship integrator: a lumpy rock (two lumps, same GM as the point rock) bends an orbit; a model that is just
        // the rock's own point mass changes nothing.
        const double mu = 1.8e9;
        var sys = RockSystem(mu);
        Vec3d Orbit(ISpaceForce? f)
        {
            var ships = new SpaceShips(sys, 1, default, f == null ? null : [f]);
            var s = ships.AddRelative(0, new Vec3d(30_000, 0, 0), new Vec3d(0, Math.Sqrt(mu / 30_000), 0), ShipFlags.NoRails);
            ships.Advance(new SpaceTime((long)(2 * Math.PI * Math.Sqrt(Math.Pow(30_000, 3) / mu) * SpaceTime.NsPerSecond)));
            return ships.PositionOf(s) - SpacePos.Origin;
        }
        var plain = Orbit(null);
        var lumpy = Orbit(new BodyGravityForce(sys, 0, new GravityModel([
            new MasconSource([new Vec3d(-1500, 0, 0), new Vec3d(1500, 0, 0)], null, mu, 100)])));
        var round = Orbit(new BodyGravityForce(sys, 0, new GravityModel([new PointSource(Vec3d.Zero, mu, 10_000)])));
        Line($"  gravity sources: one orbit at 30 km: a lumpy rock moves the ship {(lumpy - plain).Length:0} m, a round model {(round - plain).Length * 1000:0.000} mm");
        if ((lumpy - plain).Length < 10)
            Fail("gravity sources: a lumpy body's field should bend a close orbit in the ship integrator");
        if ((round - plain).Length > 1e-3)
            Fail($"gravity sources: a round body model should add nothing to the integrator ({(round - plain).Length:g3} m off)");
    }

    private static void GravityMapFiles()
    {
        const string head = """{ "name": "Gravity Sum Test", "halfX": 100, "halfZ": 100, "ops": [ { "op": "Ground", "color": [0.4, 0.4, 0.4] }, { "op": "Spawn", "x": 0, "z": 0 } ], "gravity": """;
        var good = head + """
            { "type": "sum", "axis": [0, 0, -1], "sources": [
              { "type": "point", "center": [-60, -500, 0], "radius": 400, "surface": 9 },
              { "type": "mascons", "points": [[60, 0, 0], [70, 5, 0, 2]], "soft": 3, "mu": 20000 },
              { "type": "tetra", "vertices": [[0, 0, 0], [10, 0, 0], [0, 10, 0], [0, 0, 10]], "tetras": [[0, 1, 2, 3]], "radius": 5, "surface": 0.5 },
              { "type": "uniform", "direction": [1, 0, 0], "strength": 0.2 } ] } }
            """;
        try
        {
            var map = MapFile.Load(good, "sum test", "");
            if (map.Gravity is not SummedGravity { Model.Sources.Length: 4 } s)
                Fail("gravity sources: a map's \"sum\" gravity didn't load as a summed field");
            else if (Math.Abs(s.Model.Sources[0].Exact(new Vec3d(-60, -100, 0)).Length - 9) > 1e-4)
                Fail("gravity sources: a point source's \"surface\" gravity is wrong at its radius");
        }
        catch (Exception ex)
        {
            Fail($"gravity sources: a good \"sum\" map was refused: {ex.Message}");
        }
        foreach (var (bad, why) in new[]
        {
            ("""{ "type": "sum", "sources": [] }""", "no sources"),
            ("""{ "type": "sum", "sources": [ { "type": "uniform" } ] }""", "only uniform"),
            ("""{ "type": "sum", "sources": [ { "type": "point", "surface": 9 } ] }""", "surface without radius"),
            ("""{ "type": "sum", "sources": [ { "type": "tetra", "vertices": [[0,0,0],[1,0,0],[2,0,0],[3,0,0]], "tetras": [[0,1,2,3]], "mu": 5 } ] }""", "flat tetra"),
            ("""{ "type": "sum", "sources": [ { "type": "tetra", "vertices": [[0,0,0],[1,0,0],[0,1,0],[0,0,1]], "tetras": [[0,1,2,9]], "mu": 5 } ] }""", "bad index"),
            ("""{ "type": "sum", "sources": [ { "type": "comet", "mu": 5 } ] }""", "unknown type"),
            ("""{ "type": "sum", "sources": [ { "type": "point", "mu": 5, "wobble": 1 } ] }""", "unknown key"),
        })
        {
            try
            {
                MapFile.Load(head + bad + " }", "sum test", "");
                Fail($"gravity sources: a bad sum ({why}) was accepted");
            }
            catch (MapFileException)
            {
            }
        }
    }
}
