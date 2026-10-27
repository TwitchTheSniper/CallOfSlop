using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The decal system (Source/Decals): the decal library and its file format (good and bad files), the atlas painter,
    /// the Decal / DecalScatter ops baked on the "Decal Test" map (surfaces, kerb wrap, a mesh, terrain round a hole, owners
    /// hidden when their box changes, determinism, fingerprint unchanged), the bullet-impact pool's rules (capacities,
    /// per-player cap, oldest-first fade without pops, pellet merging, removal with a box, no allocations), impact
    /// placement from a tracer, the setting, and the costs. All CPU-side: nothing here needs a GPU.
    /// </summary>
    private static void CheckDecals()
    {
        CheckDecalLibrary();
        CheckDecalFileErrors();
        CheckDecalAtlas();
        CheckDecalBake();
        CheckImpactPool();
        CheckImpactPlacement();
        CheckImpactVariety(); // impact variants, surfaces, the Decal Impacts range (SelfTestDecalImpacts.cs)
        CheckDecalSettings();
        // The section's results as one GitHub Actions notice (the workflow prints selftest.log; "::notice" lines become annotations).
        Line("::notice title=Decals (results)::" + string.Join("%0A", DecalNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        DecalNotes.Clear();
    }

    private static readonly List<string> DecalNotes = new();

    /// <summary>A result line for the log and the section's notice.</summary>
    private static void DecalLine(string s)
    {
        Line(s);
        DecalNotes.Add(s);
    }

    private static void CheckDecalLibrary()
    {
        var all = Decals.All;
        foreach (var e in Decals.LoadErrors)
            Fail("decal file problem: " + e);
        var cats = all.Select(d => d.Category).Distinct().ToList();
        DecalLine($"decal library: {all.Length} decals ({all.Count(d => d.IsImpact)} impacts, {all.Count(d => d.Blend == DecalBlend.Multiply)} multiply) in {cats.Count} categories [{string.Join(", ", cats)}] from {Decals.Folder}");
        if (all.Length < 15)
            Fail($"only {all.Length} decals loaded (the starter set has 17)");
        for (var i = 0; i < all.Length; i++)
            if (all[i].Index != i)
                Fail($"decal {all[i].Name} has index {all[i].Index}, expected {i} (its atlas layer)");
        foreach (var m in DecalFile.ImpactMaterialNames)
        {
            var set = Decals.ImpactsFor(m);
            if (set.Length == 0)
                Fail($"no impact decal for {m} surfaces (nor a Default one)");
        }
        if (!ReferenceEquals(Decals.ImpactsFor("Concrete"), Decals.ImpactsFor("Concrete")))
            Fail("ImpactsFor isn't cached (it would allocate per shot)");
        if (Decals.ImpactsFor("Brick").Any(d => !d.ImpactMaterials.Contains("Brick")))
            Fail("the Brick impacts include a decal that isn't for brick");
        foreach (var d in all.Where(d => d.IsImpact))
            if (d.Blend != DecalBlend.Alpha || MathF.Max(d.Size.X, d.Size.Y) > 1f)
                Fail($"impact decal {d.Name}: must be blend alpha and at most 1 m");
        if (Decals.Find("oil stain") is not { Blend: DecalBlend.Multiply, Surfaces: DecalSurfaces.Floor })
            Fail("Oil Stain should be found case-insensitively, multiply, floors only");

        // The template is a complete, valid decal file (comments and all), and the loader skips its folder.
        var template = Path.Combine(Decals.Folder, "_Template", "Template.json");
        if (!File.Exists(template))
            Fail("Decals/_Template/Template.json is missing");
        else
        {
            try
            {
                var t = DecalFile.Load(File.ReadAllText(template), "_Template/Template.json", "Misc", Path.GetDirectoryName(template));
                if (t.Layers.Count < 3 || all.Any(d => d.Name == t.Name))
                    Fail("the template should have 3 layers and not be in the library");
            }
            catch (DecalFileException ex)
            {
                Fail("the decal template doesn't load: " + ex.Message);
            }
        }
    }

    private static void CheckDecalFileErrors()
    {
        const string ok = "\"format\": \"call-of-slop-decal/1\", \"name\": \"T\"";
        (string Json, string Expect)[] cases =
        [
            ("{ \"name\": \"T\", \"layers\": [ { \"shape\": \"circle\" } ], \"colour\": 1 }", "unknown key"),
            ("{ \"format\": \"call-of-slop-decal/2\", \"name\": \"T\", \"layers\": [ { \"shape\": \"circle\" } ] }", "different version"),
            ("{ " + ok + " }", "needs \"layers\""),
            ("{ \"format\": \"call-of-slop-decal/1\", \"layers\": [ { \"shape\": \"circle\" } ] }", "missing \"name\""),
            ("{ " + ok + ", \"layers\": [ { \"shape\": \"hexagon\" } ] }", "unknown shape"),
            ("{ " + ok + ", \"layers\": [ { \"radius\": 0.2 } ] }", "needs \"shape\""),
            ("{ " + ok + ", \"layers\": [ { \"shape\": \"circle\", \"radius\": 3 } ] }", "outside"),
            ("{ " + ok + ", \"layers\": [ { \"shape\": \"circle\", \"color\": \"plaid\" } ] }", "colours are"),
            ("{ " + ok + ", \"blend\": \"screen\", \"layers\": [ { \"shape\": \"circle\" } ] }", "\"blend\" is"),
            ("{ " + ok + ", \"surfaces\": [\"roof\"], \"layers\": [ { \"shape\": \"circle\" } ] }", "unknown surface"),
            ("{ " + ok + ", \"size\": 40, \"layers\": [ { \"shape\": \"circle\" } ] }", "outside"),
            ("{ " + ok + ", \"impact\": { \"materials\": [\"Lava\"] }, \"layers\": [ { \"shape\": \"circle\" } ] }", "unknown material"),
            ("{ " + ok + ", \"impact\": { \"materials\": [\"Metal\"] }, \"blend\": \"multiply\", \"layers\": [ { \"shape\": \"circle\" } ] }", "impact decals use"),
            ("{ " + ok + ", \"impact\": { \"materials\": [\"Metal\"] }, \"size\": 2, \"layers\": [ { \"shape\": \"circle\" } ] }", "at most 1 m"),
            ("{ " + ok + ", \"image\": \"../x.png\", \"layers\": [ { \"shape\": \"circle\" } ] }", "\"image\" is"),
            ("[ 1, 2 ]", "one JSON object"),
            ("{ nope", "not valid JSON"),
        ];
        var bad = 0;
        foreach (var (json, expect) in cases)
        {
            try
            {
                DecalFile.Load(json, "test.json", "Misc", Path.GetTempPath());
                Fail($"decal file should be rejected ({expect}): {json}");
            }
            catch (DecalFileException ex)
            {
                if (!ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
                    Fail($"decal file error should mention \"{expect}\", got: {ex.Message}");
                bad++;
            }
        }
        // Every shape kind parses and paints something.
        var shapes = string.Join(", ", Enum.GetNames<DecalShapeKind>().Select(n => $"{{ \"shape\": \"{n.ToLowerInvariant()}\", \"color\": \"black\", \"to\": [0.9, 0.9] }}"));
        var every = DecalFile.Load("{ " + ok + ", \"layers\": [ " + shapes + " ] }", "every.json");
        var px = new Vector4[DecalAtlas.LayerSize * DecalAtlas.LayerSize];
        var layer = new byte[DecalAtlas.BytesPerLayer];
        foreach (var s in every.Layers)
        {
            var one = new DecalDef { Name = s.Kind.ToString() };
            one.Layers.Add(s);
            DecalAtlas.Paint(one, px, layer);
            var covered = 0;
            for (var i = 3; i < layer.Length; i += 4)
                if (layer[i] > 128)
                    covered++;
            if (covered < 20)
                Fail($"decal shape {s.Kind} with default settings paints almost nothing ({covered} pixels)");
        }
        DecalLine($"decal files: {bad}/{cases.Length} broken files rejected with readable messages; all {every.Layers.Count} shape kinds paint");
    }

    private static void CheckDecalAtlas()
    {
        var a = DecalAtlas.BuildLibrary(out var ms);
        var b = DecalAtlas.BuildLibrary(out _);
        var all = Decals.All;
        if (a.Length != Math.Max(1, all.Length) * DecalAtlas.BytesPerLayer)
            Fail($"decal atlas is {a.Length} bytes for {all.Length} decals");
        if (!a.AsSpan().SequenceEqual(b))
            Fail("the decal atlas isn't deterministic");
        var empty = new List<string>();
        for (var l = 0; l < all.Length; l++)
        {
            long alpha = 0;
            var span = a.AsSpan(l * DecalAtlas.BytesPerLayer, DecalAtlas.BytesPerLayer);
            for (var i = 3; i < span.Length; i += 4)
                alpha += span[i];
            // The edge row stays clear enough that clamped sampling doesn't smear a border round the decal.
            if (alpha < 255 * 60)
                empty.Add(all[l].Name);
        }
        if (empty.Count > 0)
            Fail($"decal(s) painted (nearly) empty: {string.Join(", ", empty)}");
        DecalLine($"decal atlas: {all.Length} layers of {DecalAtlas.LayerSize} px ({a.Length / 1024} KB before mipmaps) painted in {ms:0.0} ms, deterministic");
        if (ms > 2000)
            Fail($"painting the decal atlas took {ms:0} ms (startup budget 2000 ms)");
    }

    private static void CheckDecalBake()
    {
        var map = Maps.Find("Decal Test");
        if (map == null)
        {
            Fail("the \"Decal Test\" map (Maps/Dev/DecalTest.json) didn't load");
            return;
        }
        if (map.Pack?.Name != "Dev")
            Fail("Decal Test isn't in the Dev pack");
        var sw = Stopwatch.StartNew();
        var mesh = DecalBaker.Bake(map);
        var bakeMs = sw.Elapsed.TotalMilliseconds;
        var again = DecalBaker.Bake(map);
        DecalLine($"Decal Test: {map.DecalRequests.Count} decal ops asking for {mesh.Requested} decals: {mesh.Placed} placed, {mesh.Missed} missed; "
             + $"{mesh.Triangles} triangles in {mesh.Chunks.Length} chunks, baked in {bakeMs:0.0} ms");
        if (mesh.Placed < mesh.Requested * 0.85 || mesh.Triangles < 200)
            Fail($"Decal Test: only {mesh.Placed}/{mesh.Requested} decals placed, {mesh.Triangles} triangles");
        if (mesh.VertexCount != again.VertexCount || !mesh.Vertices.AsSpan(0, mesh.VertexCount * DecalMesh.FloatsPerVertex).SequenceEqual(again.Vertices.AsSpan(0, again.VertexCount * DecalMesh.FloatsPerVertex)))
            Fail("decal baking isn't deterministic");
        if (bakeMs > 1500)
            Fail($"baking the Decal Test map's decals took {bakeMs:0} ms");

        // Chunks: multiply ones first, ranges contiguous and inside the buffer, bounds holding their vertices.
        var seenAlpha = false;
        var next = 0;
        foreach (var c in mesh.Chunks)
        {
            if (c.Blend == DecalBlend.Alpha)
                seenAlpha = true;
            else if (seenAlpha)
                Fail("a multiply decal chunk comes after an alpha one (multiply decals draw first)");
            if (c.First != next || c.Count <= 0 || c.Count % 3 != 0 || c.FadeEnd <= 0)
                Fail($"decal chunk range {c.First}+{c.Count} (expected to start at {next})");
            next = c.First + c.Count;
        }
        if (next != mesh.VertexCount)
            Fail($"decal chunks cover {next} of {mesh.VertexCount} vertices");

        // Vertices: finite, uvs in range, owners are decal surfaces, terrain vertices never over the hole.
        const int f = DecalMesh.FloatsPerVertex;
        var v = mesh.Vertices;
        int owned = 0, onTerrain = 0, overHole = 0, onMesh = 0, walls = 0;
        var terrain = map.Terrain;
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var k = i * f;
            var p = new Vector3(v[k], v[k + 1], v[k + 2]);
            var n = new Vector3(v[k + 3], v[k + 4], v[k + 5]);
            if (!float.IsFinite(p.X + p.Y + p.Z + n.X + n.Y + n.Z + v[k + 14]) || v[k + 6] is < 0 or > 1 || v[k + 7] is < 0 or > 1 || MathF.Abs(n.Length() - 1) > 0.01f)
            {
                Fail($"decal vertex {i} is malformed (position {p}, normal {n})");
                break;
            }
            var owner = (int)v[k + 9];
            if (owner >= 0)
            {
                owned++;
                if (!map.IsDecalSurface(owner))
                    Fail($"a decal lies on box {owner}, which isn't a decal surface");
            }
            else if (terrain != null && MathF.Abs(p.Y - terrain.Sample(p.X, p.Z)) < 0.05f && n.Y > 0.5f && MathF.Abs(p.X + 20f) > 2.5f)
                onTerrain++;
            if (owner < 0 && MathF.Abs(p.X + 20f) < 1.7f && MathF.Abs(p.Z - 8f) < 1.7f && p.Y > map.GroundHeight(-20, 8) + 0.3f)
                onMesh++;
            if (MathF.Abs(n.Y) < 0.3f)
                walls++;
        }
        if (onTerrain < 30 || onMesh < 3 || walls < 30)
            Fail($"Decal Test: decals missing from the terrain ({onTerrain}), the mesh ({onMesh}) or the walls ({walls})");

        // Hole cells really get no triangles: every terrain decal triangle's centre is over solid ground.
        for (var i = 0; i + 2 < mesh.VertexCount; i += 3)
        {
            if (v[i * f + 9] >= 0 || terrain == null)
                continue;
            var c = (new Vector3(v[i * f], v[i * f + 1], v[i * f + 2]) + new Vector3(v[(i + 1) * f], v[(i + 1) * f + 1], v[(i + 1) * f + 2])
                     + new Vector3(v[(i + 2) * f], v[(i + 2) * f + 1], v[(i + 2) * f + 2])) / 3f;
            if (terrain.IsHole(c.X, c.Z) && MathF.Abs(c.Y - terrain.Sample(c.X, c.Z)) > 0.3f)
                overHole++;
        }
        DecalLine($"Decal Test vertices: {owned} on boxes, {onTerrain} on the terrain ({overHole} triangles over the hole), {onMesh} on the mesh tank, {walls} on walls");
        if (overHole > 0)
            Fail($"{overHole} decal triangles lie over the terrain hole");

        // Cosmetic: no boxes added, the fingerprint is the same without the decal ops, and the ops survive a file round trip.
        var json = MapFile.ToJson(map);
        var copy = MapFile.Load(json, "Decal Test (round trip)", map.PackDir ?? "");
        if (copy.DecalRequests.Count != map.DecalRequests.Count || copy.DecalCount != map.DecalCount)
            Fail("Decal Test: decal ops changed in a map file round trip");
        // The renderer's path: the first call starts a worker-thread bake and returns null; later calls get the same mesh.
        var asyncCopy = MapFile.Load(json, "Decal Test (async)", map.PackDir ?? "");
        var waited = Stopwatch.StartNew();
        var firstCall = asyncCopy.DecalMeshIfReady();
        var ready = firstCall;
        while (ready == null && waited.Elapsed.TotalSeconds < 20)
        {
            Thread.Sleep(2);
            ready = asyncCopy.DecalMeshIfReady();
        }
        DecalLine($"background bake: first call {(firstCall == null ? "returned nothing (baking on a worker thread)" : "already had the mesh")}, ready after {waited.Elapsed.TotalMilliseconds:0} ms");
        if (ready == null || ready.VertexCount != mesh.VertexCount || !ReferenceEquals(ready, asyncCopy.DecalMeshIfReady()))
            Fail($"the background decal bake gave {ready?.VertexCount.ToString() ?? "nothing"} vertices (expected {mesh.VertexCount}), or isn't kept");
        var root = JsonNode.Parse(json)!.AsObject();
        var ops = root["ops"]!.AsArray();
        for (var i = ops.Count - 1; i >= 0; i--)
            if (ops[i]!["op"]!.GetValue<string>() is "Decal" or "DecalScatter")
                ops.RemoveAt(i);
        var bare = MapFile.Load(root.ToJsonString(), "Decal Test (no decals)", map.PackDir ?? "");
        if (bare.ContentHash != map.ContentHash || bare.Solid.Count != map.Solid.Count)
            Fail("decal ops change the map's fingerprint or boxes (they must be cosmetic)");

        // Owners: a decal on a box hides when that box changes and shows again when it's back.
        var cm = copy.DecalMesh;
        var alive = new byte[((cm.Owners.Length + 1023) / 1024) * 1024];
        DecalEvents.Alive(copy, cm, alive);
        var someOwner = -1;
        for (var i = 0; i < cm.VertexCount && someOwner < 0; i++)
            if (cm.Vertices[i * f + 9] >= 0)
                someOwner = (int)cm.Vertices[i * f + 9];
        if (someOwner < 0 || alive[someOwner] != 255)
            Fail("Decal Test: no owned decal, or its box isn't alive after baking");
        else
        {
            var was = copy.Solid[someOwner];
            var version = DecalEvents.Version;
            copy.Solid[someOwner] = was with { Center = was.Center + new Vector3(0, 0, 1) };
            DecalEvents.SolidChanged(copy, someOwner);
            DecalEvents.Alive(copy, cm, alive);
            var hidden = alive[someOwner] == 0;
            copy.Solid[someOwner] = was;
            DecalEvents.BoxesRestored(copy);
            DecalEvents.Alive(copy, cm, alive);
            if (!hidden || alive[someOwner] != 255 || DecalEvents.Version != version + 2)
                Fail("a decal's box moving should hide its decals, and coming back show them again");
        }

        // Readable errors for bad ops.
        (string Op, string Expect)[] badOps =
        [
            ("{ \"op\": \"Decal\", \"decal\": \"No Such Decal\", \"x\": 0, \"z\": 0 }", "unknown decal"),
            ("{ \"op\": \"Decal\", \"decal\": \"Oil Stain\", \"x\": 0, \"z\": 0, \"onto\": \"sideways\" }", "\"onto\" is"),
            ("{ \"op\": \"DecalScatter\", \"decals\": [\"Oil Stain\"], \"x0\": 0, \"z0\": 0, \"x1\": 5, \"z1\": 5, \"count\": 5000 }", "count is"),
            ("{ \"op\": \"Decal\", \"decal\": \"Oil Stain\", \"x\": 0, \"z\": 0, \"tint\": \"plaid\" }", "\"tint\" is"),
        ];
        foreach (var (op, expect) in badOps)
        {
            var file = "{ \"format\": \"call-of-slop-map/1\", \"name\": \"Bad Decals\", \"halfX\": 10, \"halfZ\": 10, \"ops\": [ { \"op\": \"Ground\", \"color\": [0.5, 0.5, 0.5] }, "
                       + "{ \"op\": \"Spawn\", \"x\": 0, \"z\": 0 }, " + op + " ] }";
            try
            {
                MapFile.Load(file, "bad-decals.json");
                Fail($"map op should be rejected ({expect}): {op}");
            }
            catch (MapFileException ex)
            {
                if (!ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
                    Fail($"bad decal op error should mention \"{expect}\", got: {ex.Message}");
            }
        }

        // A plain flat map: a floor decal lies exactly on the ground box, a wall decal on the wall, nothing at all over empty space.
        var flat = MapFile.Load("{ \"format\": \"call-of-slop-map/1\", \"name\": \"Flat Decals\", \"halfX\": 10, \"halfZ\": 10, \"ops\": [ "
                                + "{ \"op\": \"Ground\", \"color\": [0.5, 0.5, 0.5] }, { \"op\": \"Spawn\", \"x\": 0, \"z\": 0 }, "
                                + "{ \"op\": \"Block\", \"x0\": -2, \"y0\": 0, \"z0\": -6, \"x1\": 2, \"y1\": 3, \"z1\": -5, \"color\": [0.6, 0.6, 0.6] }, "
                                + "{ \"op\": \"Decal\", \"decal\": \"Manhole Cover\", \"x\": 3, \"z\": 3, \"rotation\": 0 }, "
                                + "{ \"op\": \"Decal\", \"decal\": \"Torn Poster\", \"x\": 0, \"z\": -3, \"y\": 1.5, \"onto\": \"wall-z\" }, "
                                + "{ \"op\": \"Decal\", \"decal\": \"Torn Poster\", \"x\": 0, \"z\": 5, \"y\": 1.5, \"onto\": \"wall+z\", \"reach\": 2 }, "
                                + "{ \"op\": \"Block\", \"x0\": 4, \"y0\": 0, \"z0\": -3, \"x1\": 9, \"y1\": 0.3, \"z1\": 3, \"color\": [0.6, 0.6, 0.6] }, "
                                + "{ \"op\": \"Decal\", \"decal\": \"Oil Stain\", \"x\": 4.3, \"z\": -0.5, \"y\": 0.3, \"size\": 3.2 } ] }", "flat-decals.json");
        var fm = flat.DecalMesh;
        var floorOk = true;
        var wallOk = true;
        int kerbTop = 0, kerbGround = 0, kerbOther = 0;
        for (var i = 0; i < fm.VertexCount; i++)
        {
            var k = i * f;
            var p = new Vector3(fm.Vertices[k], fm.Vertices[k + 1], fm.Vertices[k + 2]);
            if (fm.Vertices[k + 4] < 0.9f)
                wallOk &= MathF.Abs(p.Z - (-5 + DecalBaker.SurfaceLift)) < 0.001f && fm.Vertices[k + 5] > 0.99f;
            else if (p.Z > 2.4f)
                floorOk &= MathF.Abs(p.Y - DecalBaker.SurfaceLift) < 0.001f && MathF.Abs(p.X - 3) <= 0.46f && MathF.Abs(p.Z - 3) <= 0.46f;
            else if (MathF.Abs(p.Y - (0.3f + DecalBaker.SurfaceLift)) < 0.001f)
                kerbTop++;
            else if (MathF.Abs(p.Y - DecalBaker.SurfaceLift) < 0.001f)
                kerbGround++;
            else
                kerbOther++;
        }
        DecalLine($"flat check: {fm.Placed} placed, {fm.Missed} missed (the poster facing open space), {fm.Triangles} triangles; "
             + $"the stain over the 0.3 m kerb: {kerbTop} vertices on top, {kerbGround} on the ground, {kerbOther} elsewhere");
        if (fm.Placed != 3 || fm.Missed != 1 || !floorOk || !wallOk)
            Fail($"flat decals: placed {fm.Placed}, missed {fm.Missed}, floor on the ground {floorOk}, poster on the wall {wallOk}");
        if (kerbTop < 3 || kerbGround < 3 || kerbOther > 0)
            Fail($"a floor decal over a kerb should wrap onto both levels ({kerbTop} top, {kerbGround} ground, {kerbOther} elsewhere)");
    }

    private static void CheckImpactPool()
    {
        foreach (var (q, cap) in new[] { (ImpactDecalQuality.High, 1024), (ImpactDecalQuality.Medium, 512), (ImpactDecalQuality.Low, 128), (ImpactDecalQuality.Off, 0) })
            if (ImpactPool.CapacityFor(q) != cap)
                Fail($"impact capacity for {q} should be {cap}");
        var map = Maps.Find("Decal Test") ?? Maps.All[0];
        var other = Maps.All.First(m => m != map);
        var pool = new ImpactPool();
        pool.Resize(1024);
        var now = 100.0;

        // Per player: one player firing 200 shots keeps at most 64 marks.
        for (var i = 0; i < 200; i++)
            pool.Add(map, new Vector3(i * 0.5f, 1, 0), Vector3.UnitZ, 0.15f, 0, 0, 3, -1, now += 0.05, false);
        var mine = pool.Showing(3);
        if (mine > ImpactPool.PerPlayer || mine < ImpactPool.PerPlayer - 1)
            Fail($"one player's impacts: {mine} showing (the cap is {ImpactPool.PerPlayer})");

        // Everyone: 40 players, 1600 shots over 16 s. The ring never holds more than its capacity, the oldest fade first and
        // (fading a reserve ahead) nothing still showing is ever overwritten.
        pool.Clear(map, now);
        for (var i = 0; i < 1600; i++)
            pool.Add(map, new Vector3(i % 40, 1 + i / 40 * 0.2f, 0), Vector3.UnitZ, 0.15f, 0, 0, (byte)(i % 40), -1, now += 0.01, false);
        var showing = pool.Showing();
        DecalLine($"impact pool (1024): 1600 shots by 40 players -> {showing} showing, {pool.Count} in the ring, popped {pool.Popped}, player-capped {pool.PlayerFaded}");
        if (pool.Count > pool.Capacity || showing > pool.Capacity - pool.Reserve + 1 || showing < pool.Capacity - pool.Reserve - 70)
            Fail($"impact ring: {pool.Count} slots used, {showing} showing (capacity {pool.Capacity}, reserve {pool.Reserve})");
        if (pool.Popped != 0)
            Fail($"{pool.Popped} impacts were overwritten while still showing (they should fade first)");

        // Pellets: under pressure, 8 pellets of one shot landing together merge into one or two marks.
        var before = pool.Count;
        var merged = pool.Merged;
        now += 0.2;
        for (var i = 0; i < 8; i++)
            pool.Add(map, new Vector3(50 + i * 0.02f, 1, 0), Vector3.UnitZ, 0.1f, 0, 0, 7, -1, now, true);
        var used = pool.Count - before;
        if (pool.Merged - merged < 6 || used > 2)
            Fail($"8 close pellets under pressure took {used} slots ({pool.Merged - merged} merged)");
        // Without pressure pellets keep their own marks.
        pool.Clear(map, now);
        for (var i = 0; i < 8; i++)
            pool.Add(map, new Vector3(i * 0.02f, 1, 0), Vector3.UnitZ, 0.1f, 0, 0, 7, -1, now, true);
        if (pool.Count != 8 || pool.Merged != 0)
            Fail($"pellets without pressure should each get a mark ({pool.Count} slots, {pool.Merged} merged)");

        // A box changing takes its marks with it; other boxes keep theirs. A new map clears everything.
        pool.Clear(map, now);
        for (var i = 0; i < 10; i++)
            pool.Add(map, new Vector3(i, 1, 0), Vector3.UnitZ, 0.15f, 0, 0, 1, i < 4 ? 5 : 6, now += 0.01, false);
        pool.OnSolidChanged(map, 5);
        pool.OnSolidChanged(other, 6); // another map's box: nothing happens
        if (pool.Showing() != 6 || pool.Removed != 4)
            Fail($"after box 5 broke: {pool.Showing()} impacts showing (expected 6), {pool.Removed} removed");
        pool.Add(other, Vector3.Zero, Vector3.UnitY, 0.1f, 0, 0, 1, -1, now, false);
        if (pool.Count != 1 || pool.Map != other)
            Fail("a new map should clear the impacts");

        // Allocation-free and cheap per shot.
        pool.Clear(map, now);
        // Warm up with the same argument shapes as the measured loop, so tiered JIT / on-stack replacement settles first.
        for (var i = 0; i < 20000; i++)
            pool.Add(map, new Vector3(i % 97, 1, i % 13), Vector3.UnitZ, 0.15f, i * 0.1f, i % 6, (byte)(i % 32), i % 9, now += 0.001, i % 5 == 0);
        const int adds = 50000;
        void Adds()
        {
            for (var i = 0; i < adds; i++)
                pool.Add(map, new Vector3(i % 97, 1, i % 13), Vector3.UnitZ, 0.15f, i * 0.1f, i % 6, (byte)(i % 32), i % 9, now += 0.001, i % 5 == 0);
        }
        // The least of a few identical rounds (SelfTestCpuRatio.cs): a real per-call allocation shows in every round, a one-off runtime allocation in one at most.
        var bytes = SteadyAllocBytes(Adds, warmups: 0);
        var sw = Stopwatch.StartNew(); // the time: one more round on its own
        Adds();
        var ns = sw.Elapsed.TotalMilliseconds * 1e6 / adds;
        DecalLine($"impact pool cost: {ns:0} ns per add, {bytes} bytes allocated over {adds} adds");
        // A real per-add allocation would be tens of bytes x 50000 adds; a few KB once is the runtime (JIT tier-up), not the pool.
        if (bytes > 16 * 1024)
            Fail($"adding impacts allocated {bytes} bytes over {adds} adds (must be allocation-free)");
        if (ns > 20000)
            Fail($"adding an impact takes {ns:0} ns");
        var uploads = pool.DirtyMax - pool.DirtyMin + 1;
        if (uploads <= 0)
            Fail("adding impacts should mark slots for upload");
        pool.ClearDirty();
        pool.Add(map, Vector3.One, Vector3.UnitY, 0.1f, 0, 0, 2, -1, now += 0.01, false);
        if (pool.DirtyOverflow || pool.DirtyCount is < 1 or > 3)
            Fail($"one new impact marked {pool.DirtyCount} slots for upload (expected 1 to 3: it, and the ones it fades)");
    }

    private static void CheckImpactPlacement()
    {
        var map = Maps.Find("Decal Test");
        if (map == null)
            return;
        var previous = World.Map;
        var quality = ImpactDecals.Quality;
        try
        {
            World.SetMap(map);
            ImpactDecals.Configure(ImpactDecalQuality.High);
            var pool = ImpactDecals.Pool;
            pool.Clear(map, 10);

            // A shot at the concrete back wall from the slab: one concrete mark on the wall, facing the shooter.
            var eye = new Vector3(-5, map.GroundHeight(-5, 0) + 1.5f, 0);
            var dir = Vector3.Normalize(new Vector3(0.1f, 0, -1));
            if (!World.Query.Raycast(eye, dir, 50, QueryFilter.Shots, out var hit) || hit.Kind != ShapeKind.Box)
            {
                Fail("Decal Test: a shot at the back wall hits no box");
                return;
            }
            var to = eye + dir * hit.Distance;
            var sw = Stopwatch.StartNew();
            var placed = ImpactDecals.OnTracer(map, eye, to, 4, 10.0, false);
            var us = sw.Elapsed.TotalMilliseconds * 1000;
            var k = 0;
            var layer = (int)pool.Instances[k + 8];
            var def = layer >= 0 && layer < Decals.All.Length ? Decals.All[layer] : null;
            var wallMaterial = ImpactDecals.SurfaceOf(map, hit);
            if (!placed || pool.Count != 1 || def == null || !Decals.ImpactsFor(wallMaterial).Contains(def) || pool.Instances[k + 6] < 0.99f)
                Fail($"a tracer ending on the {wallMaterial} back wall should leave one {wallMaterial} impact facing +z (placed {placed}, {pool.Count} in pool, decal {def?.Name})");
            // The same tracer on another client gives exactly the same mark.
            var first = pool.Instances.AsSpan(0, ImpactPool.FloatsPerInstance).ToArray();
            pool.Clear(map, 10);
            ImpactDecals.OnTracer(map, eye, to, 4, 10.0, false);
            if (!pool.Instances.AsSpan(0, ImpactPool.FloatsPerInstance).SequenceEqual(first))
                Fail("the same tracer gave a different impact mark (impacts must be deterministic)");
            // Misses (into the sky, at the range limit) and tracers ending in mid-air leave nothing.
            var count = pool.Count;
            ImpactDecals.OnTracer(map, eye, eye + Vector3.Normalize(new Vector3(0, 1, 0.2f)) * World.Range, 4, 10.1, false);
            ImpactDecals.OnTracer(map, eye, eye + dir * (hit.Distance * 0.5f), 4, 10.2, false);
            if (pool.Count != count)
                Fail("a miss or a tracer ending in the air left an impact");
            // Terrain: a shot into the ground off the slab picks a terrain impact.
            var feet = new Vector3(25, map.GroundHeight(25, 10) + 1.6f, 10);
            var down = Vector3.Normalize(new Vector3(0.2f, -1, 0.1f));
            if (World.Query.Raycast(feet, down, 10, QueryFilter.Shots, out var th) && th.Kind == ShapeKind.Terrain)
            {
                ImpactDecals.OnTracer(map, feet, feet + down * th.Distance, 4, 10.3, false);
                var tl = (int)pool.Instances[(pool.HighWater - 1) * ImpactPool.FloatsPerInstance + 8];
                if (pool.Count != count + 1 || !Decals.ImpactsFor(ImpactDecals.SurfaceOf(map, th)).Contains(Decals.All[tl]))
                    Fail("a shot into the terrain should leave a terrain impact");
            }
            else
                Fail("Decal Test: the terrain test shot doesn't hit the terrain");
            DecalLine($"impact placement: {wallMaterial} wall -> {def?.Name}, terrain -> {Decals.All[(int)pool.Instances[(pool.HighWater - 1) * ImpactPool.FloatsPerInstance + 8]].Name}; "
                 + $"OnTracer {us:0.0} us (first call), deterministic");

            // Fitting on a box face: a mark near an edge slides inwards, a narrow face shrinks it, a sliver refuses it.
            var box = new Box(new Vector3(0, 1, 0), new Vector3(1, 1, 0.05f));
            var pos = new Vector3(0.98f, 1.5f, 0.05f);
            var size = 0.2f;
            var fit = ImpactDecals.FitOnFace(box, ref pos, Vector3.UnitZ, ref size);
            var rail = new Box(new Vector3(0, 1, 0), new Vector3(1, 0.02f, 0.02f));
            var p2 = new Vector3(0, 1, 0.02f);
            var s2 = 0.2f;
            var sliver = ImpactDecals.FitOnFace(rail, ref p2, Vector3.UnitZ, ref s2);
            var narrow = new Box(new Vector3(0, 1, 0), new Vector3(1, 0.07f, 0.1f));
            var p3 = new Vector3(0, 1, 0.1f);
            var s3 = 0.2f;
            var shrunk = ImpactDecals.FitOnFace(narrow, ref p3, Vector3.UnitZ, ref s3);
            if (!fit || MathF.Abs(pos.X - 0.9f) > 1e-4f || size != 0.2f || sliver || !shrunk || MathF.Abs(s3 - 0.14f) > 1e-4f)
                Fail($"impact face fitting: slide {fit} to x {pos.X}, sliver refused {!sliver}, narrow face size {s3}");
        }
        finally
        {
            ImpactDecals.Configure(quality);
            World.SetMap(previous);
        }
    }

    private static void CheckDecalSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-decals-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(tmp, "fov=90\n");
            var oldSave = Settings.Load(tmp);
            new Settings { ImpactDecals = ImpactDecalQuality.Low }.Save(tmp);
            var low = Settings.Load(tmp);
            if (oldSave.ImpactDecals != ImpactDecalQuality.High || low.ImpactDecals != ImpactDecalQuality.Low)
                Fail("Bullet impacts setting: old settings files should default to High, and Low should save and load");
        }
        finally
        {
            File.Delete(tmp);
        }
        if (ImpactDecals.Quality != ImpactDecalQuality.Off || ImpactDecals.Pool.Capacity != 0)
            Fail("impact decals should be off until the renderer applies the setting (servers and the self-test pay nothing)");
    }
}
