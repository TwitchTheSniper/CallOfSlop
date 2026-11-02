using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Terrain streaming" self-test section: streamed terrain (TerrainStream.cs, <c>"stream": true</c>). A small map
/// built both ways gives the same heights, materials, ground queries, rays and normals; on the 25 km Dev map, tiles are
/// made on first read and dropped over budget (and made again the same), prefetching fills them on a worker, reads of
/// held tiles don't allocate, a tile is quick to make, and the map loads quickly and survives its bake.
/// </summary>
public static partial class SelfTest
{
    private static string StreamLabJson(bool stream) => $$"""
        {
          "format": "call-of-slop-map/1",
          "name": "Stream Lab",
          "halfX": 90,
          "halfZ": 70,
          "terrain": {
            "cell": 1.5,
            "margin": 10,
            "stream": {{(stream ? "true" : "false")}},
            "surface": { "sandBelow": -2, "snowAbove": 18 },
            "features": [
              { "type": "noise", "amplitude": 3, "scale": 40, "octaves": 4, "seed": 3 },
              { "type": "hill", "x": 30, "z": -20, "radius": 30, "height": 18 },
              { "type": "cliff", "points": [[-80, 10], [-20, 10]], "height": 8, "face": 2, "back": 30, "side": "right", "taper": 20 },
              { "type": "valley", "points": [[-10, 50], [40, 40]], "width": 20, "depth": 5 },
              { "type": "ramp", "points": [[0, 0, 2], [20, -10, 9]], "width": 4, "edge": 2, "material": "dirt" },
              { "type": "flatten", "x": -40, "z": -40, "radius": 10, "height": 1, "edge": 5 },
              { "type": "mountain", "x": -40, "z": 35, "radius": 40, "height": 30, "seed": 2, "erosion": 0.8 }
            ]
          },
          "ops": [
            { "op": "Ground", "color": [0.36, 0.5, 0.25] },
            { "op": "RingSpawns", "count": 8, "frac": 0.75 }
          ]
        }
        """;

    private static void CheckTerrainStream()
    {
        var previous = World.Map;
        try
        {
            StreamMatchesWhole();
            StreamBigMap();
            StreamLod();
            StreamPlay();
            StreamDig();
            StreamBombs();
            StreamIsland();
        }
        finally
        {
            World.SetMap(previous);
        }
    }

    /// <summary>The same small map built whole and streamed: every sample, query and ray agrees.</summary>
    private static void StreamMatchesWhole()
    {
        var whole = MapFile.Load(StreamLabJson(false), "Dev/StreamLab.json");
        whole.Validate();
        var streamed = MapFile.Load(StreamLabJson(true), "Dev/StreamLab.json");
        streamed.Validate();
        TerrainData w = whole.Terrain!, s = streamed.Terrain!;
        if (!s.IsStreamed || w.IsStreamed)
        {
            Fail("terrain streaming: \"stream\" didn't pick the streamed terrain");
            return;
        }
        if (w.CountX != s.CountX || w.CountZ != s.CountZ || w.OriginX != s.OriginX || w.OriginZ != s.OriginZ)
        {
            Fail($"terrain streaming: grids differ ({w.CountX} x {w.CountZ} at {w.OriginX}, {w.OriginZ} vs {s.CountX} x {s.CountZ} at {s.OriginX}, {s.OriginZ})");
            return;
        }
        int heights = 0, materials = 0, colours = 0;
        for (var j = 0; j < w.CountZ; j++)
        for (var i = 0; i < w.CountX; i++)
        {
            if (w.Height(i, j) != s.Height(i, j))
                heights++;
            // Materials follow slopes, which use the neighbours: the outer ring of the whole grid sees fewer of them.
            if (i > 0 && j > 0 && i < w.CountX - 1 && j < w.CountZ - 1)
            {
                if (w.Material(i, j) != s.Material(i, j))
                    materials++;
                if (w.VertexColor(i, j) != s.VertexColor(i, j))
                    colours++;
            }
        }
        if (heights + materials + colours > 0)
            Fail($"terrain streaming: of {w.CountX * w.CountZ} samples, {heights} heights, {materials} materials and {colours} colours differ from the whole grid's");
        if (s.MinHeight > w.MinHeight || s.MaxHeight < w.MaxHeight)
            Fail($"terrain streaming: the streamed bounds {s.MinHeight:0.0}..{s.MaxHeight:0.0} don't cover the real ones {w.MinHeight:0.0}..{w.MaxHeight:0.0}");

        World.SetMap(whole);
        var wc = whole.TerrainCollision!;
        var sc = streamed.TerrainCollision!;
        var rng = new Random(7);
        int queries = 0, rays = 0;
        for (var k = 0; k < 400; k++)
        {
            float x = (float)(rng.NextDouble() * 170 - 85), z = (float)(rng.NextDouble() * 130 - 65);
            if (w.Sample(x, z) != s.Sample(x, z) || w.SampleNormal(x, z) != s.SampleNormal(x, z) || w.GroundBelow(x, z, 40) != s.GroundBelow(x, z, 40))
                queries++;
            var from = new Vector3(x, 40, z);
            var dir = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, -1f, (float)rng.NextDouble() - 0.5f));
            if (MathF.Abs(wc.Raycast(from, dir, 120) - sc.Raycast(from, dir, 120)) > 1e-3f) // (the wider streamed bounds clip the ray elsewhere: last-bit differences)
                rays++;
        }
        if (queries + rays > 0)
            Fail($"terrain streaming: {queries} of 400 ground queries and {rays} of 400 rays differ from the whole grid's");
        var (held, made) = s.Stream!.Stats;
        Line($"  stream lab: {s.CountX} x {s.CountZ} samples in {s.Stream.TilesX} x {s.Stream.TilesZ} tiles ({made} made, {held} held), all samples, 400 queries and 400 rays match the whole grid");
    }

    /// <summary>The 25 km Dev map: load, tiles made and dropped, prefetching, allocation-free reads, the bake.</summary>
    private static void StreamBigMap()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Maps", "Dev", "StreamedTerrain.json");
        var sw = Stopwatch.StartNew();
        var m = MapFile.Load(File.ReadAllText(file), "Dev/StreamedTerrain.json");
        m.Validate();
        var loadMs = sw.Elapsed.TotalMilliseconds;
        var t = m.Terrain!;
        var st = t.Stream;
        if (st == null)
        {
            Fail("terrain streaming: the Streamed Terrain map isn't streamed");
            return;
        }
        if (loadMs > 3000)
            Fail($"terrain streaming: the 25 km map took {loadMs:0} ms to load (want under 3 s)");
        st.Clear();

        // A tile is quick to make.
        sw.Restart();
        var h = t.Sample(1234, -2345);
        var tileMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var peakH = t.Sample(4200, -3600); // the mountain's summit: its tiles are the dearest (crests, gullies)
        var mountainMs = sw.Elapsed.TotalMilliseconds;
        if (tileMs > 25 || mountainMs > 25)
            Fail($"terrain streaming: a tile took {tileMs:0.0} ms to make, one on the mountain {mountainMs:0.0} ms (want under 25)");
        if (peakH < 1200 || peakH > 2400)
            Fail($"terrain streaming: the mountain's summit area is {peakH:0} m high (want about 1.8 km)");

        // Held tiles: reads don't allocate.
        var sum = 0f;
        for (var k = 0; k < 64; k++)
            sum += t.Sample(1234 + k, -2345 + k * 0.5f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 5000; k++)
            sum += t.Sample(1234 + (k & 63), -2345 + (k & 63) * 0.5f) + t.SampleNormal(1234 + (k & 31), -2345).Y;
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        if (alloc > 0)
            Fail($"terrain streaming: reading held tiles allocated {alloc} B");

        // Walk a line across more tiles than the budget holds: the oldest go, and come back the same.
        var walk = TerrainStream.Budget + 200;
        var step = TerrainStream.Tile * (float)t.Cell;
        var first = (X: -12000f, Z: -12000f);
        var firstH = t.Sample(first.X, first.Z);
        var peak = 0;
        for (var k = 0; k < walk; k++)
        {
            var x = -12000f + k % 180 * step;
            var z = -12000f + k / 180 * step * 2;
            sum += t.Sample(x, z);
            peak = Math.Max(peak, st.Stats.Held);
        }
        var again = t.Sample(first.X, first.Z);
        if (peak > TerrainStream.Budget || again != firstH)
            Fail($"terrain streaming: walking {walk} tiles held up to {peak} (budget {TerrainStream.Budget}), and the first point came back {again:0.000} m, was {firstH:0.000}");

        // Prefetch: the tiles round a point get made on a worker.
        st.Clear();
        st.Prefetch(-500, 800, 300);
        var deadline = Stopwatch.StartNew();
        while (!st.Idle && deadline.ElapsedMilliseconds < 5000)
            Thread.Sleep(5);
        var ti = (int)((-500 - st.Ox) / st.Cell) / TerrainStream.Tile;
        var tj = (int)((800 - st.Oz) / st.Cell) / TerrainStream.Tile;
        if (!st.Idle || !st.Has(ti, tj) || st.Stats.Held < 9)
            Fail($"terrain streaming: a prefetch didn't fill the tiles round the point ({st.Stats.Held} held, idle {st.Idle}, {deadline.ElapsedMilliseconds} ms)");
        var prefetchMs = deadline.Elapsed.TotalMilliseconds;

        // The bake: the map reads back the same, and so does its ground.
        var key = new byte[32];
        var bakeFile = MapBake.Write(m, "Dev/StreamedTerrain.json", key, "", "", MapBake.IndexFor(null));
        var back = MapBake.Read(bakeFile, "Dev/StreamedTerrain.json", key, m.PackDir, out _, out _, out var why);
        if (back == null)
            Fail($"terrain streaming: the map bake didn't read back ({why})");
        else if (MapBake.Difference(m, back) is { } diff)
            Fail($"terrain streaming: the map read back from its bake differs: {diff}");
        else if (back.Terrain is not { IsStreamed: true } bt || bt.Sample(1234, -2345) != h || bt.Hash != t.Hash)
            Fail("terrain streaming: the baked map's ground isn't the same streamed ground");
        Line($"  streamed terrain: {t.CountX} x {t.CountZ} samples ({st.TilesX * st.TilesZ} tiles), loaded in {loadMs:0} ms, a tile in {tileMs:0.0} ms (on the mountain {mountainMs:0.0} ms, {peakH:0} m up), "
             + $"{walk} tiles walked with at most {peak} held, a 600 m prefetch in {prefetchMs:0} ms, bake {bakeFile.Length / 1024} KB (sum {sum:0})");
        st.Clear();
    }

    /// <summary>The streamed renderer's choice of what to draw (no GL): levels by distance, no overlaps, settles, no allocations.</summary>
    private static void StreamLod()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Maps", "Dev", "StreamedTerrain.json");
        var m = MapFile.Load(File.ReadAllText(file), "Dev/StreamedTerrain.json");
        m.Validate();
        var t = m.Terrain!;
        var r = new StreamedTerrainRenderer(headless: true);
        r.Reset(t);
        Span<Vector4> planes = stackalloc Vector4[6];
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, Map.StreamedFarPlane);
        foreach (var (x, z, yaw) in new[] { (0f, 0f, 0.3f), (3000f, -2500f, 2.0f), (-11000f, 9000f, -0.8f) })
        {
            var eye = new Vector3(x, t.Sample(x, z) + 1.7f, z);
            var fwd = new Vector3(MathF.Sin(yaw), -0.05f, -MathF.Cos(yaw));
            TerrainRenderer.FrustumPlanes(Matrix4x4.CreateLookAt(eye, eye + fwd, Vector3.UnitY) * proj, planes);
            var sw = Stopwatch.StartNew();
            var updates = 0;
            do
            {
                r.Update(eye, planes);
                updates++;
                if (!r.Settled)
                    Thread.Sleep(1);
            } while (!r.Settled && sw.ElapsedMilliseconds < 20000);
            r.Update(eye, planes);
            var settleMs = sw.Elapsed.TotalMilliseconds;
            var inst = r.Instances;
            var n = inst.Length / 8;
            int levelHere = -1, maxLevel = 0, overlaps = 0;
            var farthest = 0f;
            for (var a = 0; a < n; a++)
            {
                float ax = inst[a * 8], az = inst[a * 8 + 1], aw = inst[a * 8 + 2] * 32;
                var level = (int)inst[a * 8 + 6];
                maxLevel = Math.Max(maxLevel, level);
                if (eye.X >= ax && eye.X < ax + aw && eye.Z >= az && eye.Z < az + aw)
                    levelHere = level;
                farthest = MathF.Max(farthest, new Vector2(ax + aw / 2 - eye.X, az + aw / 2 - eye.Z).Length());
                for (var b = a + 1; b < n; b++)
                {
                    float bx = inst[b * 8], bz = inst[b * 8 + 1], bw = inst[b * 8 + 2] * 32;
                    if (ax < bx + bw - 0.01f && bx < ax + aw - 0.01f && az < bz + bw - 0.01f && bz < az + aw - 0.01f)
                        overlaps++;
                }
            }
            if (!r.Settled || levelHere != 0 || maxLevel < 4 || overlaps > 0 || farthest < 8000)
                Fail($"terrain streaming: drawing from ({x:0}, {z:0}): settled {r.Settled} after {updates} updates, level {levelHere} under the camera (want 0), "
                     + $"coarsest level {maxLevel} (want 4 or more), {overlaps} overlapping pieces, farthest piece {farthest:0} m (want 8 km or more)");
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 20; k++)
                r.Update(eye, planes);
            var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            if (alloc > 0)
                Fail($"terrain streaming: picking what to draw allocated {alloc} B in 20 frames");
            var pick = Stopwatch.StartNew();
            for (var k = 0; k < 20; k++)
                r.Update(eye, planes);
            var st = r.Stats;
            Line($"  drawing from ({x:0}, {z:0}): {n} pieces ({st.Triangles / 1000}k triangles), levels 0-{maxLevel}, out to {farthest / 1000:0.0} km, "
                 + $"{st.Pages} pages held ({st.Made} made, settled in {settleMs:0} ms), picking {pick.Elapsed.TotalMilliseconds / 20:0.000} ms");
            if (st.Pages > StreamedTerrainRenderer.MaxPages)
                Fail($"terrain streaming: {st.Pages} pages held, more than the {StreamedTerrainRenderer.MaxPages} there's room for");
        }
        r.Reset(null);
    }

    /// <summary>An offline match on the 25 km map: sprint across new ground, two bots steering straight (no nav graph).</summary>
    private static void StreamPlay()
    {
        var map = Maps.Find("Streamed Terrain");
        if (map == null)
        {
            Fail("terrain streaming: no Streamed Terrain map in the list");
            return;
        }
        var id = (byte)Array.IndexOf(Maps.All, map);
        var st = map.Terrain!.Stream!;
        st.Clear();
        var madeBefore = st.Stats.Made;
        var now = 1000.0;
        using var g = ModeGame(2, GameMode.TeamDeathmatch, id);
        if (SpawnLocalSafely(g, ref now) is { } why)
        {
            Fail($"terrain streaming: {why}");
            return;
        }
        var me = g.Local!;
        var start = me.Position;
        me.ImmuneUntil = now + 60; // (the bots shoot: this is about the ground, not the fight)
        g.LookYaw = 1.1f;
        g.HeldButtons = Buttons.Forward | Buttons.Sprint;
        const double dt = 1.0 / World.TickRate;
        var worst = 0.0;
        float below = 0f, above = 0f;
        for (var k = 0; k < 8 * World.TickRate; k++)
        {
            var t0 = Stopwatch.GetTimestamp();
            now += dt;
            g.Update(now, dt);
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            worst = Math.Max(worst, ms);
            if (me.Alive && me.Sim.Grounded)
            {
                var d = me.Position.Y - map.Terrain.Sample(me.Position.X, me.Position.Z);
                (below, above) = (MathF.Max(below, -d), MathF.Max(above, d));
            }
        }
        g.HeldButtons = Buttons.None;
        var moved = Vector2.Distance(new Vector2(start.X, start.Z), new Vector2(me.Position.X, me.Position.Z));
        var made = st.Stats.Made - madeBefore;
        var tile = (I: (int)((me.Position.X - st.Ox) / st.Cell) / TerrainStream.Tile, J: (int)((me.Position.Z - st.Oz) / st.Cell) / TerrainStream.Tile);
        if (!me.Alive || moved < 40 || below > 0.02f || made == 0 || !st.Has(tile.I, tile.J))
            Fail($"terrain streaming: the sprint went {moved:0} m (alive {me.Alive}), feet up to {below:0.000} m under the ground, {made} tiles made, the tile underfoot held {st.Has(tile.I, tile.J)}");
        Line($"  a match on it: sprinted {moved:0} m, {made} tiles made (prefetched round the players), grounded feet never under the ground (up to {above:0.00} m over it on slopes), "
             + $"slowest tick {worst:0.0} ms (wall clock)");
        g.HeldButtons = Buttons.None;
    }

    /// <summary>Dug ground on the streamed map: a crater 11 km out, rays into it, the drawing's dug mask, and Sandbox starting there.</summary>
    private static void StreamDig()
    {
        var map = Maps.Find("Streamed Terrain")!;
        if (!GameModes.ForMap(map).Contains(GameMode.Sandbox))
            Fail("terrain streaming: Sandbox (digging) isn't offered on Streamed Terrain");
        World.SetMap(map);
        var t = map.Terrain!;
        var e = t.Edits;
        e.Clear();
        const float x = 8000.3f, z = 8000.3f;
        var h0 = t.Sample(x, z);
        e.Apply(new DigOp(DigKind.Crater, new Vector3(x, h0, z), 3f).Quantised);
        var floor = t.Sample(x, z);
        var down = map.TerrainCollision!.Raycast(new Vector3(x, h0 + 10, z), -Vector3.UnitY, 30);
        if (floor > h0 - 1.2f || MathF.Abs(h0 + 10 - down - floor) > 0.06f || e.TouchedCells == 0)
            Fail($"terrain streaming: a crater 11 km out: floor {floor - h0:0.00} m (want below -1.2), a ray down stopped at {h0 + 10 - down - h0:0.00} m, {e.TouchedCells} cells dug");

        // The drawing: the level-0 piece over the crater carries the dug mask.
        var r = new StreamedTerrainRenderer(headless: true);
        r.Reset(t);
        Span<Vector4> planes = stackalloc Vector4[6];
        var eye = new Vector3(x, h0 + 20, z + 25);
        TerrainRenderer.FrustumPlanes(Matrix4x4.CreateLookAt(eye, new Vector3(x, h0, z), Vector3.UnitY)
                                      * Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, Map.StreamedFarPlane), planes);
        var sw = Stopwatch.StartNew();
        do
        {
            r.Update(eye, planes);
            Thread.Sleep(1);
        } while (!r.Settled && sw.ElapsedMilliseconds < 20000);
        r.Update(eye, planes);
        var masked = false;
        var inst = r.Instances;
        for (var k = 0; k < inst.Length; k += 8)
        {
            float ax = inst[k], az = inst[k + 1], w = inst[k + 2] * 32;
            if (x >= ax && x < ax + w && z >= az && z < az + w)
                masked = inst[k + 6] == 0 && inst[k + 7] > 0.5f;
        }
        if (!masked)
            Fail("terrain streaming: the level-0 piece over the crater has no dug mask (the surface would cover the crater)");
        r.Reset(null);
        e.Clear();
        if (t.Sample(x, z) != h0)
            Fail("terrain streaming: the ground didn't come back when the digging was cleared");

        // Sandbox starts on it: the player spawns and the starting buggies stand on the streamed ground.
        var id = (byte)Array.IndexOf(Maps.All, map);
        var now = 1000.0;
        using var g = ModeGame(0, GameMode.Sandbox, id);
        if (SpawnLocalSafely(g, ref now) is { } why)
            Fail($"terrain streaming: Sandbox on Streamed Terrain: {why}");
        else
            Line($"  dug a crater 11 km out ({floor - h0:0.00} m deep, {e.Log.Count} ops after clearing), the drawing masks it, Sandbox starts on the map");
    }

    /// <summary>
    /// The floating island on Streamed Terrain (TerrainIslands.cs): made with the map, the hole under it the same shape,
    /// solid to stand on and to rays, roofed from below, dug like any ground, back after a restart, a village on top, and
    /// cheap enough to draw and build.
    /// </summary>
    private static void StreamIsland()
    {
        var map = Maps.Find("Streamed Terrain")!;
        World.SetMap(map);
        var t = map.Terrain!;
        var e = t.Edits;
        if (e.Islands.Count != 1)
        {
            Fail($"terrain streaming: Streamed Terrain should have one floating island (has {e.Islands.Count})");
            return;
        }
        var isl = e.Islands[0];
        float x = (float)isl.X, z = (float)isl.Z, top = (float)isl.Top;
        var ground = t.BaseGroundTop(x, z);
        var onTop = t.GroundBelow(x, z, top + 30);
        var under = t.CeilingAbove(x, z, ground + 2);
        var floor = t.GroundBelow(x, z, ground + 2);
        var thick = top - under;
        var hole = ground - floor;
        var down = World.Query.Raycast(new Vector3(x + 3, top + 40, z + 3), -Vector3.UnitY, 100, QueryFilter.All);
        var up = World.Query.Raycast(new Vector3(x + 3, ground + 1, z + 3), Vector3.UnitY, 200, QueryFilter.All);
        Line($"  island: top {onTop:0.0} m (want {top:0}), {thick:0.0} m thick at the middle over a hole {hole:0.0} m deep in ground at {ground:0.0}; a ray down lands at {top + 40 - down:0.0}, one up hits {ground + 1 + up:0.0}");
        if (MathF.Abs(onTop - top) > 0.01f || thick < 20 || MathF.Abs(hole - thick) > 0.6f)
            Fail($"terrain streaming: the island isn't the hole's shape lifted to {top}: top {onTop:0.00}, thick {thick:0.00}, hole {hole:0.00}");
        if (MathF.Abs(top + 40 - down - top) > 0.1f || up > 200 || MathF.Abs(ground + 1 + up - t.CeilingAbove(x + 3, z + 3, t.BaseGroundTop(x + 3, z + 3) + 2)) > 0.6f)
            Fail($"terrain streaming: rays miss the island (down lands at {top + 40 - down:0.00}, up hits {ground + 1 + up:0.00}, its underside {under:0.00})");
        // Outside its outline the ground is the map's own.
        var away = x + (float)isl.Reach + 6;
        if (e.TryColumn(away, z, out _) || MathF.Abs(t.GroundBelow(away, z, 500) - t.BaseGroundTop(away, z)) > 0.01f)
            Fail("terrain streaming: the ground beside the island was changed");

        // Faces: the island's cells, counted (the renderer draws these as blocks).
        var count = new DigFaceCount();
        int i0 = (int)((x - isl.Reach - t.OriginX) / t.Cell), i1 = (int)((x + isl.Reach - t.OriginX) / t.Cell);
        int j0 = (int)((z - isl.Reach - t.OriginZ) / t.Cell), j1 = (int)((z + isl.Reach - t.OriginZ) / t.Cell);
        var sw = Stopwatch.StartNew();
        for (var j = j0; j <= j1; j++)
        for (var i = i0; i <= i1; i++)
            e.CellFaces(i, j, ref count);
        var facesMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        e.Clear(); // (a new match: the dug ground goes, the island comes back)
        var buildMs = sw.Elapsed.TotalMilliseconds;
        var back = t.GroundBelow(x, z, top + 30);
        Line($"  island: {e.TouchedCells} cells, {count.N * 2} triangles, faces in {facesMs:0} ms, rebuilt in {buildMs:0} ms; after a restart its top is at {back:0.0}");
        if (MathF.Abs(back - top) > 0.01f)
            Fail("terrain streaming: the island didn't come back after the map was restored");
        if (count.N * 2 > 450_000 || buildMs > 250)
            Fail($"terrain streaming: the island is too dear ({count.N * 2} triangles, built in {buildMs:0} ms; want 450k and 250 ms)");

        // Dug like any ground: a crater on its top.
        var at = new Vector3(x + 20, top, z);
        e.Apply(new DigOp(DigKind.Crater, at, 3f));
        var pit = t.GroundBelow(at.X, at.Z, top + 30);
        if (pit > top - 1f)
            Fail($"terrain streaming: a crater on the island dug nothing (ground {pit:0.00})");
        e.Clear();

        // The village stands on it.
        var houses = map.PlacedBuildings.Count(b => b.Plan.Footprint.Any(r => MathF.Abs((r.X0 + r.X1) / 2 - x) < isl.R && MathF.Abs((r.Z0 + r.Z1) / 2 - z) < isl.R));
        var props = map.PropInstances.Count(p => MathF.Abs(p.Position.Y - top) < 0.5f && Vector2.Distance(new(p.Position.X, p.Position.Z), new(x, z)) < isl.R);
        Line($"  island village: {houses} buildings, {props} props on top");
        if (houses < 3 || props < 5)
            Fail($"terrain streaming: the island's village has {houses} buildings and {props} props (want 3 and 5)");
    }

    /// <summary>
    /// The bombs near spawn on Streamed Terrain, in Sandbox: the Aerial Bomb and the Small Nuke go off, dig their own
    /// craters (5 m and 30 m) and the worst tick, with the blast, its props, players, constructs and the dig, stays cheap.
    /// </summary>
    private static void StreamBombs()
    {
        var map = Maps.Find("Streamed Terrain")!;
        var t = map.Terrain!;
        t.Edits.Clear();
        var id = (byte)Array.IndexOf(Maps.All, map);
        var now = 1000.0;
        using var g = ModeGame(0, GameMode.Sandbox, id);
        if (SpawnLocalSafely(g, ref now) is { } why)
        {
            Fail($"terrain streaming: bombs: {why}");
            return;
        }
        int bomb = -1, nuke = -1;
        for (var i = 0; i < map.PropInstances.Count; i++)
            if (map.PropInstances[i].Def.Name == "Aerial Bomb" && bomb < 0)
                bomb = i;
            else if (map.PropInstances[i].Def.Name == "Small Nuke")
                nuke = i;
        if (bomb < 0 || nuke < 0)
        {
            Fail($"terrain streaming: Streamed Terrain should hold an Aerial Bomb and a Small Nuke (found {bomb}, {nuke})");
            return;
        }
        foreach (var (i, want, name) in new[] { (bomb, 5f, "Aerial Bomb"), (nuke, 30f, "Small Nuke") })
        {
            var at = map.PropInstances[i].Position;
            var h0 = t.Sample(at.X, at.Z);
            var ring = t.Sample(at.X + want * 0.5f, at.Z);
            var ring0 = ring;
            var cells = t.Edits.TouchedCells;
            var shown = g.Blasts.Count;
            var sw = Stopwatch.StartNew();
            g.ServerBreakProp(i, at, PropDamage.Explosion, null, now);
            var blastMs = sw.Elapsed.TotalMilliseconds;
            var balls = g.Blasts.Count - shown;
            var worst = 0.0;
            for (var k = 0; k < 30; k++) // the half second after: props it lit, constructs falling in, the dig's wake
            {
                sw.Restart();
                now += 1.0 / World.TickRate;
                g.Update(now, 1.0 / World.TickRate);
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            var floor = t.Sample(at.X, at.Z);
            ring = t.Sample(at.X + want * 0.5f, at.Z);
            var dug = t.Edits.TouchedCells - cells;
            if (floor > h0 - want * 0.25f || ring >= ring0 - 0.05f || dug == 0)
                Fail($"terrain streaming: the {name}'s crater: {floor - h0:0.0} m deep at the middle (want below {-want * 0.25f:0.0}), {ring - ring0:0.00} m at half its radius, {dug} cells dug");
            if (balls < (want > 10 ? 16 : 1))
                Fail($"terrain streaming: the {name} showed {balls} fireballs");
            // (the blast tick is one-off work, like a grenade's; 8 ms is the steady tick budget, so allow a generous one-off)
            if (blastMs > 60 || worst > 25)
                Fail($"terrain streaming: the {name} took {blastMs:0.0} ms to go off and the worst tick after was {worst:0.0} ms (want 60 and 25)");
            Line($"  {name}: went off in {blastMs:0.0} ms, worst tick after {worst:0.0} ms, crater {h0 - floor:0.0} m deep, {dug} cells dug, {balls} fireballs");
        }
        t.Edits.Clear();

        // The same bombs as construct parts (the rack and the parts menu spawn them): set down loose, shot to pieces, each
        // goes off after its fuse with its prop's blast (fireballs and crater), not at once, and sets off a second one.
        foreach (var (name, want) in new[] { ("Aerial Bomb", 5f), ("Small Nuke", 30f) })
        {
            if (ConstructParts.Find(name) is not { Explosive: { } ex } def)
            {
                Fail($"terrain streaming: the construct part {name} is missing or isn't explosive");
                continue;
            }
            var spot = name == "Aerial Bomb" ? new Vector2(-300, -300) : new Vector2(300, 300);
            var gy = t.Sample(spot.X, spot.Y);
            var a = ConstructBuild.PlaceNew(g.Constructs, def, new Vector3(spot.X, gy, spot.Y), 0);
            var b = ConstructBuild.PlaceNew(g.Constructs, def, new Vector3(spot.X + 3f, gy, spot.Y), 0);
            if (a == null || b == null)
            {
                Fail($"terrain streaming: couldn't set down two {name} parts");
                continue;
            }
            var bid = b.Id;
            var h0 = t.Sample(spot.X, spot.Y);
            var went0 = g.PropDetonations;
            g.Constructs.DamagePart(a, 0, def.Health * 10, out _);
            var fuseTicks = (int)MathF.Round(ex.ExplodeFuse * World.TickRate);
            int early = 0, wentAt = -1;
            for (var k = 0; k < fuseTicks + 30 && wentAt < 0; k++)
            {
                now += 1.0 / World.TickRate;
                g.Update(now, 1.0 / World.TickRate);
                if (g.PropDetonations > went0)
                    wentAt = k + 1;
                else if (t.Sample(spot.X, spot.Y) < h0 - 0.05f)
                    early++;
            }
            for (var k = 0; k < (int)(ex.ExplodeFuse * World.TickRate) + 30; k++) // the second one's fuse
            {
                now += 1.0 / World.TickRate;
                g.Update(now, 1.0 / World.TickRate);
            }
            var floor = t.Sample(spot.X, spot.Y);
            var chained = g.Constructs.Get(bid) is not { } bc || bc.Parts[0].Broken || bc.PartCount == 0;
            Line($"  {name} part: went off {wentAt} ticks after it broke (fuse {fuseTicks}), crater {h0 - floor:0.0} m deep, {g.PropDetonations - went0} blasts, the one beside it went too {chained}");
            if (wentAt < fuseTicks - 1 || wentAt > fuseTicks + 2 || early > 0)
                Fail($"terrain streaming: the {name} part went off {wentAt} ticks after breaking (want its fuse, {fuseTicks})");
            if (floor > h0 - want * 0.25f)
                Fail($"terrain streaming: the {name} part's crater is {h0 - floor:0.0} m deep (want over {want * 0.25f:0.0})");
            if (!chained)
                Fail($"terrain streaming: the {name} part didn't set off the one 3 m away");
            t.Edits.Clear();
        }
    }
}
