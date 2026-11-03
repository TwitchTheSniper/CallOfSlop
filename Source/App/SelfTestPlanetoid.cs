using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Planetoid" self-test section (Source/Planets, the Dev map Planetoid): the cube-sphere's faces meet bit for bit,
/// directions round-trip through the grid, heights stay inside the worked-out bounds, rays land on the ground they say
/// they hit, a player stands, walks and sprints at the pole, on the equator and across a cube corner without sinking or
/// floating, the headless drawing picks pages round the camera and none behind the planet, and the fingerprint covers the
/// ground. Short scenarios only.
/// </summary>
public static partial class SelfTest
{
    private static void CheckPlanetoid()
    {
        var previous = World.Map;
        try
        {
            var map = Maps.All.FirstOrDefault(m => m.Planetoid != null);
            if (map == null)
            {
                Fail("planetoid: no map with a planetoid (Maps/Dev/Planetoid.json)");
                return;
            }
            var pl = map.Planetoid!;
            Line($"planetoid: '{map.Name}', radius {pl.Radius:0} m, {pl.N} x {pl.N} cells a face ({pl.Cell:0.00} m), {pl.Levels} levels, ground {pl.MinHeight:0} to {pl.MaxHeight:0} m, gravity {map.PointGravity?.Strength:0} m/s2");
            if ((pl.N & (pl.N - 1)) != 0 || pl.N < Planetoid.PageCells || map.PointGravity == null || map.PointGravity.Center != pl.Center)
                Fail("planetoid: a face's cells aren't a power of two, or the map has no point gravity towards the centre");
            PlanetoidSeams(pl);
            PlanetoidBounds(pl);
            World.SetMap(map);
            PlanetoidRays(pl);
            PlanetoidWalks(map, pl);
            PlanetoidDraw(pl);
            PlanetoidFingerprint(map);
        }
        finally
        {
            World.SetMap(previous);
        }
    }

    /// <summary>Every face's edge vertices are exactly the neighbouring faces' (direction and height, bit for bit).</summary>
    private static void PlanetoidSeams(Planetoid pl)
    {
        var buckets = new Dictionary<(long, long, long), List<(double X, double Y, double Z, double H)>>();
        var step = pl.N / 32;
        for (var f = 0; f < 6; f++)
        for (var k = 0; k <= 32; k++)
            foreach (var (i, j) in new[] { (k * step, 0), (k * step, pl.N), (0, k * step), (pl.N, k * step) })
            {
                pl.DirOf(f, i, j, out var x, out var y, out var z);
                var key = ((long)Math.Round(x * 1e7), (long)Math.Round(y * 1e7), (long)Math.Round(z * 1e7));
                if (!buckets.TryGetValue(key, out var list))
                    buckets[key] = list = new();
                list.Add((x, y, z, pl.HeightAt(x, y, z)));
            }
        int shared = 0, lonely = 0, differ = 0;
        foreach (var list in buckets.Values)
        {
            if (list.Count < 2)
                lonely++;
            else
                shared++;
            foreach (var v in list)
                if (v != list[0])
                    differ++;
        }
        // The tiles' vertices (what collision reads) too: the (1, 1, -1) cube corner from each of its three faces.
        var n = pl.N;
        var corner = Vector3.Normalize(new Vector3(1, 1, -1));
        Vector3? first = null;
        var seen = 0;
        var equal = true;
        for (var f = 0; f < 6; f++)
            foreach (var (i, j) in new[] { (0, 0), (0, n), (n, 0), (n, n) })
            {
                var v = pl.VertexRel(f, i, j);
                if (Vector3.Dot(Vector3.Normalize(v), corner) < 0.9999f)
                    continue;
                seen++;
                if (first is { } a)
                    equal &= v == a;
                else
                    first = v;
            }
        Line($"planetoid seams: {shared} shared edge vertices, {lonely} with no partner, {differ} differing; the (1, 1, -1) corner from {seen} faces, the same: {equal}");
        if (lonely > 0 || differ > 0 || seen != 3 || !equal)
            Fail($"planetoid: faces don't meet exactly ({lonely} edge vertices with no partner, {differ} differing, corner seen {seen}, same {equal})");

        // Directions round-trip through the grid.
        var rng = new Random(5);
        var worst = 0.0;
        for (var t = 0; t < 2000; t++)
        {
            var f = rng.Next(6);
            double fi = rng.NextDouble() * pl.N, fj = rng.NextDouble() * pl.N;
            pl.DirOf(f, fi, fj, out var x, out var y, out var z);
            pl.Locate(x, y, z, out var g, out var gi, out var gj);
            var onEdge = Math.Min(Math.Min(fi, pl.N - fi), Math.Min(fj, pl.N - fj)) < 1e-6; // (a tie on an edge may pick the neighbour)
            var err = g == f ? Math.Max(Math.Abs(gi - fi), Math.Abs(gj - fj)) : onEdge ? 0 : 1;
            worst = Math.Max(worst, err);
        }
        Line($"planetoid grid: 2000 random points round-trip within {worst:0.000000} cells");
        if (worst > 1e-6)
            Fail($"planetoid: a direction doesn't come back to its grid point ({worst} cells off)");
    }

    /// <summary>Heights at random places stay inside the bounds the features promise (rays and the drawing rely on them).</summary>
    private static void PlanetoidBounds(Planetoid pl)
    {
        var rng = new Random(11);
        double lo = double.MaxValue, hi = double.MinValue;
        var sw = Stopwatch.StartNew();
        for (var t = 0; t < 20000; t++)
        {
            double x = rng.NextDouble() * 2 - 1, y = rng.NextDouble() * 2 - 1, z = rng.NextDouble() * 2 - 1;
            var len = Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-3)
                continue;
            var h = pl.HeightAt(x / len, y / len, z / len);
            lo = Math.Min(lo, h);
            hi = Math.Max(hi, h);
        }
        var us = sw.Elapsed.TotalMilliseconds * 1000 / 20000;
        Line($"planetoid heights: 20000 samples from {lo:0.0} to {hi:0.0} m (bounds {pl.MinHeight:0} to {pl.MaxHeight:0}), {us:0.0} us a sample");
        if (lo < pl.MinHeight || hi > pl.MaxHeight)
            Fail($"planetoid: ground outside its bounds ({lo:0.0} to {hi:0.0}, bounds {pl.MinHeight} to {pl.MaxHeight})");
    }

    private static Vector3 PlanetoidDir(double lat, double lon)
    {
        var d = Map.SurfaceDir((float)lat, (float)lon);
        return Vector3.Normalize(d);
    }

    /// <summary>Rays straight down and across the ground land on it; points under it are inside.</summary>
    private static void PlanetoidRays(Planetoid pl)
    {
        var rng = new Random(3);
        var phys = World.Physics;
        var ground = new QueryFilter(QueryLayers.Terrain);
        float worstDown = 0, worstAcross = 0;
        int across = 0, inside = 0, places = 0;
        var sw = Stopwatch.StartNew();
        for (var t = 0; t < 120; t++)
        {
            // Near the pole (where play starts), then anywhere.
            var dir = t < 60 ? PlanetoidDir(90 - rng.NextDouble() * 20, rng.NextDouble() * 360) : PlanetoidDir(rng.NextDouble() * 180 - 90, rng.NextDouble() * 360);
            var g = pl.Center + dir * pl.SurfaceRadius(dir, out _);
            var from = g + dir * 50f;
            if (!phys.Raycast(from, -dir, 200, ground, out var hit) || hit.Shape.Kind != ShapeKind.Terrain)
            {
                Fail($"planetoid: a ray straight down at {g} missed the ground");
                continue;
            }
            places++;
            worstDown = MathF.Max(worstDown, MathF.Abs(hit.Distance - 50f));
            if (Vector3.Dot(hit.Normal, dir) < 0.2f)
                Fail($"planetoid: a ray's hit normal points into the ground ({hit.Normal} at {hit.Point})");
            // Across: from eye height, level, in a random direction along the ground.
            var side = Vector3.Normalize(Vector3.Cross(dir, new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f)));
            if (phys.Raycast(g + dir * 1.7f, side, 2000, ground, out var h2))
            {
                across++;
                worstAcross = MathF.Max(worstAcross, MathF.Abs(pl.Altitude(h2.Point)));
            }
            if (phys.PointInside(g - dir * 0.5f, 0.1f, ground))
                inside++;
        }
        var ms = sw.Elapsed.TotalMilliseconds;
        Line($"planetoid rays: {places} straight down off by at most {worstDown * 1000:0.0} mm; {across} of 120 level rays hit the ground, landing within {worstAcross * 1000:0.0} mm of it; {inside} of {places} points under the ground inside; {ms:0} ms (tiles made {pl.TileStats.Made})");
        if (worstDown > 0.01f || worstAcross > 0.02f || inside != places)
            Fail($"planetoid: rays don't land on the ground (down {worstDown:0.000} m, across {worstAcross:0.000} m off; {inside} of {places} underground points inside)");
    }

    /// <summary>A player stands and sprints at the pole, on the equator and over a cube corner, staying on the ground.</summary>
    private static void PlanetoidWalks(Map map, Planetoid pl)
    {
        var profile = string.Join(", ", new[] { 0, 50, 100, 150, 200, 300, 400 }.Select(m =>
        {
            var d = PlanetoidDir(90 - m / pl.Radius * 180 / Math.PI, 0);
            return $"{pl.HeightAt(d.X, d.Y, d.Z):0.0}";
        }));
        Line($"planetoid ground from the pole south, every 50-100 m: {profile}");
        var spots = new (string Name, Vector3 Dir)[]
        {
            ("the pole", PlanetoidDir(89.5, 0)), // (beside the pole's own pillar and cover ring)
            ("the equator", PlanetoidDir(0, 90)),
            ("a cube corner", Vector3.Normalize(new Vector3(1, 1, -1))),
            ("a face edge", Vector3.Normalize(new Vector3(-1, 0.2f, -1))),
        };
        var rng = new Random(9);
        var walkTicks = 0;
        var walkSw = Stopwatch.StartNew();
        foreach (var (name, dir) in spots)
            for (var w = 0; w < 4; w++)
            {
                var start = pl.Center + dir * (pl.SurfaceRadius(dir, out _) + 0.6f);
                var s = new MoveState { Position = start, Grounded = false };
                for (var i = 0; i < 40; i++)
                    World.Simulate(ref s, new InputCmd { SpeedPct = 100 });
                if (!s.Grounded || MathF.Abs(pl.Altitude(s.Position)) > 0.1f)
                {
                    Fail($"planetoid: a player doesn't stand at {name} (grounded {s.Grounded}, {pl.Altitude(s.Position):0.000} m off the ground)");
                    break;
                }
                var yaw = (float)(rng.NextDouble() * MathF.Tau);
                var pitch = 0f;
                var origin = s.Position;
                var prev = s.Position;
                float lowest = float.MaxValue, highest = float.MinValue;
                var groundedTicks = 0;
                const int ticks = 300;
                for (var t = 0; t < ticks; t++)
                {
                    GravityFrames.Transport(ref yaw, ref pitch, prev, s.Position);
                    prev = s.Position;
                    var b = Buttons.Forward | Buttons.Sprint | (t == 150 ? Buttons.Jump : 0);
                    World.Simulate(ref s, new InputCmd { Buttons = b, Yaw = yaw, Pitch = pitch, SpeedPct = 100 });
                    walkTicks++;
                    var alt = pl.Altitude(s.Position);
                    lowest = MathF.Min(lowest, alt);
                    if (s.Grounded)
                    {
                        groundedTicks++;
                        highest = MathF.Max(highest, alt);
                    }
                }
                var went = Vector3.Distance(origin, s.Position);
                if (w == 0)
                    Line($"planetoid walk at {name}: {went:0.0} m in {ticks / 60f:0} s, grounded {groundedTicks} of {ticks} ticks, feet {lowest:+0.000;-0.000} to {highest:+0.000;-0.000} m from the ground while standing");
                if (went < 20 || lowest < -0.1f || highest > 0.3f || groundedTicks < ticks / 2)
                {
                    Fail($"planetoid: walking at {name} went wrong ({went:0.0} m, lowest {lowest:0.000} m, highest standing {highest:0.000} m, grounded {groundedTicks} of {ticks})");
                    break;
                }
            }
        Line($"planetoid walks: {walkTicks} moves, {walkSw.Elapsed.TotalMilliseconds * 1000 / Math.Max(walkTicks, 1):0} us each on average (new ground's tiles included; {pl.TileStats.Held} tiles held)");
        // The map's own spawns stand on the ground.
        var bad = map.Spawns.Count(sp => MathF.Abs(pl.Altitude(sp)) > 0.6f || World.Physics.Blocked(sp));
        Line($"planetoid spawns: {map.Spawns.Count}, {bad} off the ground or blocked");
        if (bad > 0 || map.Spawns.Count == 0)
            Fail($"planetoid: {bad} of {map.Spawns.Count} spawns are off the ground or blocked");
    }

    /// <summary>The headless drawing: pages round the camera, levels getting coarser with distance, nothing behind the planet.</summary>
    private static void PlanetoidDraw(Planetoid pl)
    {
        var r = new PlanetRenderer(headless: true);
        r.Reset(pl);
        var sw = Stopwatch.StartNew();
        foreach (var (name, dir) in new[] { ("the pole", Vector3.UnitY), ("the far pole", -Vector3.UnitY) })
        {
            var eye = pl.Center + dir * (pl.SurfaceRadius(dir, out _) + 1.7f);
            PlanetRenderer.LocalAxes(pl, eye, out var right, out var up, out var south);
            var view = Matrix4x4.CreateLookAt(eye, eye - south * 100f, up);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, 30000f);
            Span<Vector4> planes = stackalloc Vector4[6];
            TerrainRenderer.FrustumPlanes(view * proj, planes);
            for (var k = 0; k < 400; k++)
            {
                r.Update(eye, planes);
                if (r.Settled)
                    break;
                Thread.Sleep(5);
            }
            r.Update(eye, planes);
            var inst = r.Instances;
            int count = inst.Length / 8, behind = 0, level0 = 0;
            var nearest0 = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var o = new Vector3(inst[i * 8], inst[i * 8 + 1], inst[i * 8 + 2]);
                if (Vector3.Dot(Vector3.Normalize(o - pl.Center), dir) < -0.2f)
                    behind++;
                if (inst[i * 8 + 6] == 0)
                {
                    level0++;
                    nearest0 = MathF.Min(nearest0, Vector3.Distance(o, eye));
                }
            }
            var st = r.Stats;
            var t0 = Stopwatch.GetTimestamp();
            for (var k = 0; k < 20; k++)
                r.Update(eye, planes);
            var pick = Stopwatch.GetElapsedTime(t0).TotalMilliseconds / 20;
            Line($"planetoid drawing from {name}: {count} instances ({level0} at level 0, nearest page centre {nearest0:0} m), {st.Pages} pages held, {st.Made} made, {behind} behind the planet, {pick:0.000} ms to pick");
            if (count == 0 || level0 == 0 || behind > 0 || nearest0 > 150 || st.Pages > PlanetRenderer.MaxPages)
                Fail($"planetoid: the drawing from {name} is wrong ({count} instances, {level0} at level 0 nearest {nearest0:0} m, {behind} behind the planet)");
        }
        r.Reset(null);
        Line($"planetoid drawing: {sw.Elapsed.TotalMilliseconds:0} ms in all");
    }

    /// <summary>The fingerprint covers the ground: a different seed gives a different one.</summary>
    private static void PlanetoidFingerprint(Map map)
    {
        if (map.SourceFile is not { } file || !File.Exists(file))
            return;
        var json = File.ReadAllText(file);
        var other = json.Replace("\"seed\": 11", "\"seed\": 12");
        if (other == json)
        {
            Fail("planetoid: the fingerprint check couldn't find the noise seed to change");
            return;
        }
        var a = MapFile.Load(json, file);
        var b = MapFile.Load(other, file);
        Line($"planetoid fingerprint: {a.ContentHash:x8}, another seed {b.ContentHash:x8}");
        if (a.ContentHash == b.ContentHash)
            Fail("planetoid: the map's fingerprint doesn't change with its ground");
    }
}
