using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The mesh pipeline (Source/Render/Mesh): native canaries for every BRUTAL native it uses, every primitive's
    /// counts, closedness, normals, volume and bounds, LODs, determinism, meshoptimizer and its fallback, the glTF test
    /// models (node tree, materials, textures), readable errors for broken files and bad map ops, MeshPool packing, the
    /// collision data and the "Mesh Test" map (including its file round trip). All CPU-side: nothing here needs a GPU.
    /// </summary>
    private static void CheckMeshes()
    {
        CheckMeshCanaries();
        CheckPrimitives();
        CheckMeshOptimizer();
        CheckGltfModels();
        CheckBadModels();
        CheckMeshPool();
        CheckMeshMap();
    }

    private static void CheckMeshCanaries()
    {
        // meshoptimizer (native "meshoptimizer"): every entry point the pipeline calls, checked on a test grid.
        var mo = MeshOpt.Available;
        Line($"canary meshoptimizer: {(mo ? "PASS" : "FALLBACK")} - {MeshOpt.CanaryReport}");
        // Brutal.Texture (native "stb"): a PNG decoded by stb and by the built-in reader must match.
        var stb = MeshTextures.StbAvailable;
        Line($"canary Brutal.Texture/stb: {(stb ? "PASS" : "FALLBACK")} - {MeshTextures.CanaryReport}");
        Fail($"TEMP canaries: meshoptimizer {MeshOpt.CanaryReport} | stb {MeshTextures.CanaryReport}"); // TEMP: visible in CI annotations
        // Brutal.Gltf is managed (System.Text.Json); check its schema parser on a tiny document.
        try
        {
            var g = Brutal.GltfApi.GltfUtility.DeserializeModelJson("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"name\":\"a\",\"translation\":[1,2,3]}]}");
            var ok = g.Nodes?.Length == 1 && g.Nodes[0].Name == "a" && g.Nodes[0].Translation.Y == 2 && g.Asset.Version == "2.0";
            Line($"canary Brutal.Gltf (managed): {(ok ? "PASS" : "FAIL")} - schema parse of a 1-node document");
            if (!ok)
                Fail("Brutal.Gltf parsed a minimal document wrongly");
        }
        catch (Exception ex)
        {
            Fail($"Brutal.Gltf can't parse a minimal document: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- Primitives ---------------------------------------------------------------------------------------------------

    private readonly record struct PrimitiveCase(PrimitiveSpec Spec, int Vertices, int Triangles, float Volume, Vector3 Min, Vector3 Max, bool Convex, int Lods, float Tolerance);

    private static IEnumerable<PrimitiveCase> PrimitiveCases()
    {
        const float pi = MathF.PI;
        yield return new(new(PrimitiveKind.Box, 1.2f, 0.8f, 2f), 24, 12, 1.2f * 0.8f * 2f, new(-0.6f, 0, -1), new(0.6f, 0.8f, 1), true, 1, 0.001f);
        yield return new(new(PrimitiveKind.Wedge, 2f, 1f, 3f), 18, 8, 2f * 1f * 3f / 2, new(-1, 0, -1.5f), new(1, 1, 1.5f), true, 1, 0.001f);
        yield return new(new(PrimitiveKind.Cylinder, 1f, 2f, 1f, Segments: 24), 6 * 25, 4 * 24, pi * 0.25f * 2f, new(-0.5f, 0, -0.5f), new(0.5f, 2, 0.5f), true, 3, 0.03f);
        yield return new(new(PrimitiveKind.Cone, 1f, 1.5f, 1f, Segments: 16), 4 * 17, 2 * 16, pi * 0.25f * 1.5f / 3, new(-0.5f, 0, -0.5f), new(0.5f, 1.5f, 0.5f), true, 3, 0.05f);
        yield return new(new(PrimitiveKind.Sphere, 1f, 1f, 1f, Segments: 24, Detail: 12), 13 * 25, 2 * 24 * 11, pi / 6, new(-0.5f, 0, -0.5f), new(0.5f, 1, 0.5f), true, 3, 0.06f);
        yield return new(new(PrimitiveKind.Capsule, 0.6f, 1.8f, 0.6f, Segments: 16, Detail: 4), 2 * 5 * 17, 4 * 4 * 16, pi * 0.09f * 1.2f + 4f / 3 * pi * 0.027f, new(-0.3f, 0, -0.3f), new(0.3f, 1.8f, 0.3f), true, 2, 0.06f);
        yield return new(new(PrimitiveKind.Torus, Radius: 0.5f, Inner: 0.15f, Segments: 24, Detail: 12), 13 * 25, 2 * 24 * 12, 2 * pi * pi * 0.5f * 0.0225f, new(-0.65f, 0, -0.65f), new(0.65f, 0.3f, 0.65f), false, 3, 0.08f);
        yield return new(new(PrimitiveKind.Tube, 1f, 2f, 1f, Radius: 0.5f, Inner: 0.4f, Segments: 24), 8 * 25, 8 * 24, pi * (0.25f - 0.16f) * 2f, new(-0.5f, 0, -0.5f), new(0.5f, 2, 0.5f), false, 3, 0.03f);
        yield return new(new(PrimitiveKind.RoundedBox, 1f, 1f, 2f, Radius: 0.2f, Segments: 3), 6 * 64, 12 * 49, 1f * 1f * 2f - (8 - 4f / 3 * pi) * 0.008f - (4 - pi) * 0.04f * (0.6f + 0.6f + 1.6f), new(-0.5f, 0, -1), new(0.5f, 1, 1), true, 2, 0.01f);
        yield return new(new(PrimitiveKind.IcoSphere, 1f, 1f, 1f, Detail: 2), -1, 20 * 16, pi / 6, new(-0.5f, 0, -0.5f), new(0.5f, 1, 0.5f), true, 3, 0.07f);
        // scaled (ellipsoid / elliptic cylinder): normals must still be right
        yield return new(new(PrimitiveKind.Sphere, 2f, 1f, 0.5f, Segments: 32, Detail: 16), 17 * 33, 2 * 32 * 15, pi / 6 * 2f * 1f * 0.5f, new(-1, 0, -0.25f), new(1, 1, 0.25f), true, 3, 0.04f);
    }

    private static void CheckPrimitives()
    {
        var sw = Stopwatch.StartNew();
        var lines = new List<string>();
        foreach (var c in PrimitiveCases())
        {
            var name = c.Spec.Resolve().Key;
            MeshData mesh;
            try
            {
                mesh = MeshPrimitives.Build(c.Spec);
            }
            catch (Exception ex)
            {
                Fail($"{name}: didn't build: {ex.Message}");
                continue;
            }
            if (c.Vertices >= 0 && mesh.Vertices.Length != c.Vertices)
                Fail($"{name}: {mesh.Vertices.Length} vertices, expected {c.Vertices}");
            if (mesh.TriangleCount != c.Triangles)
                Fail($"{name}: {mesh.TriangleCount} triangles, expected {c.Triangles}");
            CheckMeshSanity(name, mesh, c.Convex);
            var col = MeshCollision.From(mesh);
            if (!col.IsClosed(out var bad))
                Fail($"{name}: not closed ({bad} open or badly wound edges)");
            var vol = col.SignedVolume();
            if (MathF.Abs(vol - c.Volume) > c.Volume * c.Tolerance)
                Fail($"{name}: volume {vol:0.0000} m3, expected {c.Volume:0.0000} (+-{c.Tolerance:P0})");
            var tol = MathF.Max(c.Tolerance, 0.02f) * (c.Max - c.Min).Length();
            if (Vector3.Distance(mesh.Bounds.Min, c.Min) > tol || Vector3.Distance(mesh.Bounds.Max, c.Max) > tol)
                Fail($"{name}: bounds {mesh.Bounds}, expected {new MeshBounds(c.Min, c.Max)}");
            if (col.TriangleCount != mesh.TriangleCount || col.HullPoints.Length < 4 || col.Box.Half.X <= 0
                || col.HullPoints.Any(p => p.X < col.Bounds.Min.X - 1e-4f || p.Y < col.Bounds.Min.Y - 1e-4f || p.Z > col.Bounds.Max.Z + 1e-4f))
                Fail($"{name}: collision data wrong ({col.TriangleCount} triangles, {col.HullPoints.Length} hull points)");
            // Deterministic: building again gives the same bytes.
            if (MeshPrimitives.Build(c.Spec).Hash() != mesh.Hash())
                Fail($"{name}: two builds differ (not deterministic)");
            // LODs: as many as expected, each with fewer triangles, all closed.
            var asset = MeshLibrary.Primitive(c.Spec);
            if (asset.Lods.Length != c.Lods)
                Fail($"{name}: {asset.Lods.Length} LODs, expected {c.Lods}");
            for (var l = 1; l < asset.Lods.Length; l++)
            {
                if (asset.Lods[l].TriangleCount >= asset.Lods[l - 1].TriangleCount)
                    Fail($"{name}: LOD {l} has {asset.Lods[l].TriangleCount} triangles, not fewer than LOD {l - 1} ({asset.Lods[l - 1].TriangleCount})");
                if (!MeshCollision.From(asset.Lods[l]).IsClosed(out _))
                    Fail($"{name}: LOD {l} isn't closed");
            }
            if (!ReferenceEquals(MeshLibrary.Primitive(c.Spec), asset))
                Fail($"{name}: the library built it twice instead of sharing one mesh");
            lines.Add($"{c.Spec.Kind} {mesh.Vertices.Length}v/{mesh.TriangleCount}t vol {vol:0.000} LODs {string.Join("/", asset.Lods.Select(x => x.TriangleCount))} hull {col.HullPoints.Length} hash {mesh.Hash():x16}");
        }
        foreach (var l in lines)
            Line("  " + l);
        // Every name resolves with its defaults; bad values give readable errors.
        foreach (var n in PrimitiveSpec.Names.Keys)
            try
            {
                MeshLibrary.Primitive(new PrimitiveSpec(PrimitiveSpec.ParseKind(n)));
            }
            catch (Exception ex)
            {
                Fail($"primitive \"{n}\" with defaults: {ex.Message}");
            }
        foreach (var (spec, expect) in new (Func<PrimitiveSpec>, string)[]
                 {
                     (() => new PrimitiveSpec(PrimitiveSpec.ParseKind("cilinder")), "did you mean \"cylinder\""),
                     (() => new PrimitiveSpec(PrimitiveKind.Cylinder, Segments: 2), "segments must be 3 to 128"),
                     (() => new PrimitiveSpec(PrimitiveKind.Torus, Radius: 0.2f, Inner: 0.3f), "less than radius"),
                     (() => new PrimitiveSpec(PrimitiveKind.Capsule, Radius: 1f, Height: 1f), "less than twice"),
                     (() => new PrimitiveSpec(PrimitiveKind.Box, -1f), "width must be"),
                 })
            try
            {
                MeshPrimitives.Build(spec());
                Fail($"a bad primitive built without complaint (expected '{expect}')");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(expect, StringComparison.Ordinal))
            {
            }
            catch (Exception ex)
            {
                Fail($"bad primitive gave the wrong error (expected '{expect}'): {ex.Message}");
            }
        Line($"{lines.Count} primitive cases built, checked and LOD'd in {sw.Elapsed.TotalMilliseconds:0} ms; library holds {MeshLibrary.Counts.Primitives} primitive meshes");
    }

    /// <summary>Unit normals, every triangle facing the way its normals point, and (convex shapes) normals pointing away from the middle.</summary>
    private static void CheckMeshSanity(string name, MeshData m, bool convex)
    {
        var centre = m.Bounds.Center;
        int badLen = 0, badFace = 0, badOut = 0, badIndex = 0;
        foreach (var v in m.Vertices)
        {
            if (MathF.Abs(v.Normal.Length() - 1) > 1e-3f || !float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z + v.Uv.X + v.Uv.Y))
                badLen++;
            if (convex && Vector3.Dot(v.Normal, v.Position - centre) < -1e-4f)
                badOut++;
        }
        for (var i = 0; i + 2 < m.Indices.Length; i += 3)
        {
            if (m.Indices[i] >= m.Vertices.Length || m.Indices[i + 1] >= m.Vertices.Length || m.Indices[i + 2] >= m.Vertices.Length)
            {
                badIndex++;
                continue;
            }
            var a = m.Vertices[m.Indices[i]];
            var b = m.Vertices[m.Indices[i + 1]];
            var c = m.Vertices[m.Indices[i + 2]];
            var fn = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (Vector3.Dot(fn, a.Normal + b.Normal + c.Normal) <= 0)
                badFace++;
        }
        if (badLen + badFace + badOut + badIndex > 0)
            Fail($"{name}: {badLen} bad normals/positions, {badFace} triangles facing against their normals, {badOut} normals pointing inwards, {badIndex} bad indices");
    }

    // ---- meshoptimizer --------------------------------------------------------------------------------------------------

    private static void CheckMeshOptimizer()
    {
        var spec = new PrimitiveSpec(PrimitiveKind.Sphere, Segments: 64, Detail: 32);
        var raw = MeshPrimitives.Build(spec);
        var opt = MeshOpt.Optimize(raw);
        var rawCol = MeshCollision.From(raw);
        var optCol = MeshCollision.From(opt);
        if (opt.TriangleCount != raw.TriangleCount || MathF.Abs(optCol.SignedVolume() - rawCol.SignedVolume()) > 1e-4f || !optCol.IsClosed(out _))
            Fail($"meshoptimizer changed the sphere ({raw.TriangleCount} -> {opt.TriangleCount} triangles)");
        if (MeshOpt.Optimize(raw).Hash() != opt.Hash())
            Fail("meshoptimizer output isn't deterministic");
        var acmrRaw = Acmr(raw.Indices, 16);
        var acmrOpt = Acmr(opt.Indices, 16);
        Line($"vertex cache (16-entry FIFO) misses per triangle: {acmrRaw:0.000} before, {acmrOpt:0.000} after ({(MeshOpt.Available ? "meshoptimizer" : "fallback: unchanged")})");
        if (MeshOpt.Available && acmrOpt > acmrRaw + 0.01)
            Fail($"meshoptimizer made the vertex cache use worse ({acmrRaw:0.000} -> {acmrOpt:0.000})");
        var simple = MeshOpt.Simplify(opt, 0.25f, 0.05f);
        if (MeshOpt.Available)
        {
            if (simple == null)
                Fail("meshoptimizer is available but couldn't simplify a 64x32 sphere");
            else
            {
                var lod = new MeshData(opt.Vertices, simple);
                var v = MeshCollision.From(lod).SignedVolume();
                Line($"simplified sphere: {opt.TriangleCount} -> {lod.TriangleCount} triangles, volume {rawCol.SignedVolume():0.0000} -> {v:0.0000}");
                if (MathF.Abs(v - rawCol.SignedVolume()) > rawCol.SignedVolume() * 0.1f)
                    Fail($"simplified sphere lost too much volume ({v:0.0000})");
            }
        }
        // The fallback: with the native "missing", meshes pass through untouched and there are no simplified LODs.
        MeshOpt.ForceDisabled(true);
        try
        {
            if (!ReferenceEquals(MeshOpt.Optimize(raw), raw) || MeshOpt.Simplify(raw, 0.5f, 0.05f) != null)
                Fail("meshoptimizer fallback didn't pass meshes through");
            var fallbackAsset = MeshLibrary.Primitive(new PrimitiveSpec(PrimitiveKind.Cylinder, Segments: 40));
            if (fallbackAsset.Lods.Length != 3 || !MeshCollision.From(fallbackAsset.Lods[0]).IsClosed(out _))
                Fail("primitive LODs without meshoptimizer are wrong");
        }
        finally
        {
            MeshOpt.ForceDisabled(false);
        }
    }

    /// <summary>Average cache misses per triangle with a FIFO vertex cache (lower is better; 0.5 is ideal for big grids).</summary>
    private static double Acmr(uint[] indices, int cacheSize)
    {
        var cache = new Queue<uint>();
        var set = new HashSet<uint>();
        var misses = 0;
        foreach (var i in indices)
        {
            if (set.Contains(i))
                continue;
            misses++;
            cache.Enqueue(i);
            set.Add(i);
            if (cache.Count > cacheSize)
                set.Remove(cache.Dequeue());
        }
        return indices.Length == 0 ? 0 : misses / (indices.Length / 3.0);
    }

    // ---- glTF ------------------------------------------------------------------------------------------------------------

    private static void CheckGltfModels()
    {
        if (!Directory.Exists(MeshLibrary.ModelsFolder))
        {
            Fail("the Models folder wasn't copied next to the game");
            return;
        }
        ModelAsset? Load(string file)
        {
            try
            {
                var m = MeshLibrary.Model(file);
                Line($"{file}: {m.Nodes.Length} nodes, {m.Meshes.Length} meshes, {m.Parts.Length} parts, {m.Triangles} triangles, {m.Materials.Length} materials "
                    + $"({string.Join(", ", m.Materials.Select(x => $"{x.Name} {x.BaseColor.X:0.00},{x.BaseColor.Y:0.00},{x.BaseColor.Z:0.00}{(x.TextureLayer >= 0 ? $" tex{x.TextureLayer}" : "")}"))}), "
                    + $"bounds {m.Bounds}, LODs {string.Join("/", m.Meshes.Select(x => $"{x.LodSource}:{string.Join(",", x.Lods.Select(l => l.TriangleCount))}"))}, {m.LoadMs:0.0} ms"
                    + (m.Warnings.Length > 0 ? $"; warnings: {string.Join("; ", m.Warnings)}" : ""));
                Line("    tree: " + m.DescribeTree().TrimEnd().Replace("\n", " | "));
                if (!ReferenceEquals(MeshLibrary.Model(file), m))
                    Fail($"{file}: loaded twice instead of cached");
                for (var i = 0; i < m.Nodes.Length; i++)
                    if (m.Nodes[i].Parent >= i)
                        Fail($"{file}: node {m.Nodes[i].Name} comes before its parent");
                foreach (var mesh in m.Meshes)
                    CheckMeshSanity($"{file} {mesh.Key}", mesh.Lods[0], false);
                return m;
            }
            catch (ModelException ex)
            {
                Fail($"{file}: {ex.Message}");
                return null;
            }
        }

        static bool Near(Vector3 a, Vector3 b, float tol) => Vector3.Distance(a, b) <= tol;

        if (Load("Models/barrel.glb") is { } barrel)
        {
            var names = string.Join(",", barrel.Nodes.Select(n => n.Name));
            if (names != "Barrel,Body,HoopLow,HoopHigh" || barrel.Meshes.Length != 2 || barrel.Parts.Length != 3)
                Fail($"barrel.glb: nodes {names}, {barrel.Meshes.Length} meshes, {barrel.Parts.Length} parts (expected Barrel,Body,HoopLow,HoopHigh, 2 meshes shared by 3 parts)");
            if (barrel.Materials.Length != 2 || !Near(new Vector3(barrel.Materials[0].BaseColor.X, barrel.Materials[0].BaseColor.Y, barrel.Materials[0].BaseColor.Z), new Vector3(0.62f, 0.14f, 0.10f), 0.01f))
                Fail("barrel.glb: the Paint material's colour didn't come back as sRGB (0.62, 0.14, 0.10)");
            if (!Near(barrel.Bounds.Size, new Vector3(0.612f, 0.88f, 0.612f), 0.02f))
                Fail($"barrel.glb: bounds {barrel.Bounds}");
            if (!Near(barrel.Nodes[3].World.Translation, new Vector3(0, 0.63f, 0), 1e-4f))
                Fail($"barrel.glb: HoopHigh is at {barrel.Nodes[3].World.Translation}");
            var col = barrel.BuildCollision();
            if (!col.IsClosed(out var bad))
                Fail($"barrel.glb: collision soup not closed ({bad} edges)");
            Line($"    barrel collision: {col.TriangleCount} triangles, {col.Positions.Length} welded points, volume {col.SignedVolume():0.0000} m3, {col.HullPoints.Length} hull points");
        }
        if (Load("Models/traffic_cone.glb") is { } cone)
        {
            if (cone.Nodes.Length != 1 || cone.Meshes.Length != 1 || cone.Materials.Length != 2)
                Fail($"traffic_cone.glb: {cone.Nodes.Length} nodes, {cone.Meshes.Length} meshes, {cone.Materials.Length} materials (expected 1, 1, 2)");
            if (cone.Textures != 1 || cone.Materials[0].TextureLayer < 0)
                Fail("traffic_cone.glb: its embedded PNG texture didn't load");
            else
            {
                // The texture's top rows are orange, a band near the middle is white.
                var layer = MeshTextures.Snapshot()[cone.Materials[0].TextureLayer];
                int Px(int x, int y, int c) => layer[(y * MeshTextures.Size + x) * 4 + c];
                var top = (Px(128, 4, 0), Px(128, 4, 1), Px(128, 4, 2));
                var band = (Px(128, 90, 0), Px(128, 90, 1), Px(128, 90, 2));
                if (top.Item1 < 200 || top.Item2 > 140 || band.Item1 < 220 || band.Item3 < 220)
                    Fail($"traffic_cone.glb: texture texels wrong (top {top}, band {band})");
            }
            if (!cone.BuildCollision().IsClosed(out var bad))
                Fail($"traffic_cone.glb: collision soup not closed ({bad} edges)");
            var v = cone.Meshes[0].Lods[0].Vertices;
            if (!v.Any(x => x.Layer >= MeshVertex.ImportedLayerBase) || !v.Any(x => x.Layer < 0))
                Fail("traffic_cone.glb: textured and untextured parts weren't told apart");
        }
        if (Load("Models/lamp_post.gltf") is { } lamp)
        {
            var names = string.Join(",", lamp.Nodes.Select(n => n.Name));
            if (names != "LampPost,Base,Pole,Arm,Head")
                Fail($"lamp_post.gltf: nodes {names} (expected LampPost,Base,Pole,Arm,Head, parents first)");
            else
            {
                var parents = string.Join(",", lamp.Nodes.Select(n => n.Parent));
                if (parents != "-1,0,0,2,3")
                    Fail($"lamp_post.gltf: parents {parents}, expected -1,0,0,2,3");
                // Head = matrix (0.9, -0.03, 0) inside Arm (3.0 up, turned 10 deg about z) inside Pole (0.3 up).
                var a = 10f * MathF.PI / 180f;
                var expect = new Vector3(0.9f * MathF.Cos(a) + 0.03f * MathF.Sin(a), 3.3f + 0.9f * MathF.Sin(a) - 0.03f * MathF.Cos(a), 0);
                if (!Near(lamp.Nodes[4].World.Translation, expect, 1e-3f))
                    Fail($"lamp_post.gltf: Head is at {lamp.Nodes[4].World.Translation}, expected {expect} (node matrix or TRS order wrong)");
                if (!Near(lamp.Nodes[3].Translation, new Vector3(0, 3, 0), 1e-5f) || MathF.Abs(lamp.Nodes[3].Rotation.Z - MathF.Sin(a / 2)) > 1e-4f)
                    Fail("lamp_post.gltf: Arm's TRS didn't come through");
                if (!Near(lamp.Nodes[4].Translation, new Vector3(0.9f, -0.03f, 0), 1e-5f))
                    Fail("lamp_post.gltf: Head's matrix wasn't decomposed into TRS");
            }
            if (lamp.Bounds.Max.Y < 3.4f || lamp.Bounds.Max.Y > 3.6f || lamp.Bounds.Max.X < 1.0f)
                Fail($"lamp_post.gltf: bounds {lamp.Bounds}");
            Line($"    lamp collision: {lamp.BuildCollision().TriangleCount} triangles");
        }
    }

    /// <summary>A tiny one-triangle glTF (buffer as a data URI); <paramref name="edit"/> breaks it in one place.</summary>
    private static byte[] TriangleGltf(Func<string, string>? edit = null, bool longBuffer = true)
    {
        var bin = new byte[44];
        float[] pos = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        Buffer.BlockCopy(pos, 0, bin, 0, 36);
        ushort[] idx = [0, 1, 2];
        Buffer.BlockCopy(idx, 0, bin, 36, 6);
        var uri = "data:application/octet-stream;base64," + Convert.ToBase64String(longBuffer ? bin : bin[..12]);
        var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"name\":\"Tri\",\"mesh\":0}],"
                   + "\"meshes\":[{\"name\":\"T\",\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}],"
                   + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},"
                   + "{\"bufferView\":1,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}],"
                   + "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":6}],"
                   + "\"buffers\":[{\"byteLength\":44,\"uri\":\"" + uri + "\"}]}";
        return Encoding.UTF8.GetBytes(edit != null ? edit(json) : json);
    }

    private static void CheckBadModels()
    {
        // The good one first, so the bad cases below each break exactly one thing.
        try
        {
            var ok = GltfModelLoader.Load(TriangleGltf(), "tri.gltf", null);
            if (ok.Triangles != 1 || ok.Nodes.Length != 1 || ok.Parts.Length != 1)
                Fail($"the one-triangle glTF loaded as {ok.Triangles} triangles, {ok.Nodes.Length} nodes");
            if (ok.Meshes[0].Lods[0].Vertices.Any(v => MathF.Abs(v.Normal.Z) < 0.99f))
                Fail("a glTF without normals didn't get flat normals");
        }
        catch (ModelException ex)
        {
            Fail("the one-triangle test glTF didn't load: " + ex.Message);
        }

        var barrel = File.Exists(Path.Combine(MeshLibrary.ModelsFolder, "barrel.glb")) ? File.ReadAllBytes(Path.Combine(MeshLibrary.ModelsFolder, "barrel.glb")) : null;
        var cases = new List<(string Name, byte[] Data, string Expect)>
        {
            ("text", Encoding.UTF8.GetBytes("hello, I am not a model"), "neither glTF JSON nor"),
            ("broken json", Encoding.UTF8.GetBytes("{ \"asset\": { \"version\": "), "not valid glTF JSON"),
            ("version 1", TriangleGltf(j => j.Replace("\"version\":\"2.0\"", "\"version\":\"1.0\"")), "only 2.x"),
            ("no nodes", TriangleGltf(j => j.Replace("\"nodes\":[{\"name\":\"Tri\",\"mesh\":0}],", "").Replace("\"scenes\":[{\"nodes\":[0]}],", "")), "has no nodes"),
            ("child out of range", TriangleGltf(j => j.Replace("\"mesh\":0}", "\"mesh\":0,\"children\":[7]}")), "node index 7 is out of range"),
            ("cycle", TriangleGltf(j => j.Replace("\"nodes\":[{\"name\":\"Tri\",\"mesh\":0}]", "\"nodes\":[{\"name\":\"Tri\",\"mesh\":0,\"children\":[1]},{\"name\":\"Loop\",\"children\":[0]}]")), "appears twice"),
            ("no position", TriangleGltf(j => j.Replace("\"POSITION\":0", "\"NORMAL\":0")), "no POSITION"),
            ("accessor out of range", TriangleGltf(j => j.Replace("\"POSITION\":0", "\"POSITION\":5")), "accessor 5 is out of range"),
            ("short buffer", TriangleGltf(longBuffer: false), "is 12 bytes but should be 44"),
            ("view past buffer", TriangleGltf(j => j.Replace("\"byteOffset\":36,\"byteLength\":6", "\"byteOffset\":40,\"byteLength\":6")), "runs past the end"),
            ("external buffer", TriangleGltf(j => System.Text.RegularExpressions.Regex.Replace(j, "\"uri\":\"[^\"]*\"", "\"uri\":\"missing.bin\"")), "no folder to look in"),
            ("bad index", TriangleGltf(j => j.Replace("\"count\":3,\"type\":\"VEC3\"", "\"count\":2,\"type\":\"VEC3\"")), "past the last vertex"),
            ("draco", TriangleGltf(j => j.Replace("{\"asset\"", "{\"extensionsRequired\":[\"KHR_draco_mesh_compression\"],\"asset\"")), "KHR_draco_mesh_compression"),
            ("wrong type", TriangleGltf(j => j.Replace("\"type\":\"VEC3\"", "\"type\":\"VEC2\"")), "expected VEC3"),
        };
        if (barrel != null)
        {
            cases.Add(("truncated glb", barrel[..(barrel.Length - 100)], "header says"));
            var v1 = (byte[])barrel.Clone();
            v1[4] = 1;
            cases.Add(("glb version 1", v1, "version 1"));
        }
        var shown = 0;
        foreach (var (name, data, expect) in cases)
        {
            try
            {
                GltfModelLoader.Load(data, $"bad-{name.Replace(' ', '-')}.gltf", null);
                Fail($"broken model '{name}' loaded without complaint (expected '{expect}')");
            }
            catch (ModelException ex) when (ex.Message.Contains(expect, StringComparison.Ordinal))
            {
                if (shown++ < 4)
                    Line($"  readable error ({name}): {ex.Message}");
            }
            catch (Exception ex)
            {
                Fail($"broken model '{name}' gave the wrong error (expected '{expect}'): {ex.GetType().Name}: {ex.Message}");
            }
        }
        try
        {
            MeshLibrary.Model("Models/no_such_model.glb");
            Fail("a missing model file loaded");
        }
        catch (ModelException ex) when (ex.Message.Contains("not found", StringComparison.Ordinal) && ex.Message.Contains("Models/barrel.glb", StringComparison.Ordinal))
        {
        }
        catch (Exception ex)
        {
            Fail($"a missing model gave the wrong error: {ex.Message}");
        }
        Line($"{cases.Count + 1} broken model files rejected with readable messages");
    }

    // ---- MeshPool ---------------------------------------------------------------------------------------------------------

    private static void CheckMeshPool()
    {
        var pool = new MeshPool();
        var assets = new List<MeshAsset>
        {
            MeshLibrary.Primitive(new PrimitiveSpec(PrimitiveKind.Cylinder, Segments: 24)),
            MeshLibrary.Primitive(new PrimitiveSpec(PrimitiveKind.Box)),
        };
        // A model-style asset whose LODs share one vertex array.
        var grid = MeshOpt.Grid(8);
        var half = grid.Indices[..(grid.Indices.Length / 2)];
        assets.Add(new MeshAsset("shared-lods", [grid, new MeshData(grid.Vertices, half)], false, "test"));
        // Enough spheres to force the arrays to grow past their first capacity.
        for (var i = 0; i < 40; i++)
            assets.Add(MeshLibrary.Primitive(new PrimitiveSpec(PrimitiveKind.Sphere, 1f + i * 0.01f, Segments: 48, Detail: 24)));
        var ranges = assets.Select(pool.Add).ToList();
        if (!ReferenceEquals(pool.Add(assets[0]), ranges[0]) || pool.MeshCount != assets.Count)
            Fail("MeshPool packed the same mesh twice");
        var shared = ranges[2];
        if (shared[0].BaseVertex != shared[1].BaseVertex || shared[1].IndexCount != half.Length)
            Fail("MeshPool didn't share the vertices of LODs that share them");
        var verts = pool.Vertices;
        var idx = pool.Indices;
        var bad = 0;
        var nextIndex = 0;
        for (var a = 0; a < assets.Count; a++)
            for (var l = 0; l < assets[a].Lods.Length; l++)
            {
                var r = ranges[a][l];
                var src = assets[a].Lods[l];
                if (r.FirstIndex != nextIndex || r.IndexCount != src.Indices.Length || r.VertexCount != src.Vertices.Length)
                    bad++;
                nextIndex += r.IndexCount;
                for (var i = 0; i < r.IndexCount; i++)
                    if (idx[r.FirstIndex + i] != src.Indices[i] + r.BaseVertex || idx[r.FirstIndex + i] >= r.BaseVertex + r.VertexCount)
                    {
                        bad++;
                        break;
                    }
                if (!System.Runtime.InteropServices.MemoryMarshal.AsBytes(verts.Slice(r.BaseVertex, r.VertexCount)).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(src.Vertices.AsSpan())))
                    bad++;
            }
        if (bad > 0 || nextIndex != pool.IndexCount)
            Fail($"MeshPool packing wrong in {bad} place(s)");
        Line($"MeshPool: {pool.MeshCount} meshes packed into {pool.VertexCount} vertices + {pool.IndexCount} indices ({pool.Bytes / 1024} KB), ranges and offsets verified");
    }

    // ---- The Mesh Test map --------------------------------------------------------------------------------------------------

    private static void CheckMeshMap()
    {
        var map = Maps.Find("Mesh Test");
        if (map == null)
        {
            Fail("the \"Mesh Test\" map (Maps/Dev/MeshTest.json) didn't load");
            return;
        }
        var ops = map.Ops.Count(o => o.Name is nameof(Map.Mesh) or nameof(Map.Model));
        var models = map.MeshPlacements.Count(p => p.Model != null);
        var parts = map.MeshPlacements.Sum(p => p.Model?.Parts.Length ?? 1);
        var meshes = map.MeshPlacements.Select(p => p.Mesh?.Id ?? 0).Where(id => id != 0).Distinct().Count();
        Line($"Mesh Test: {map.MeshPlacements.Count} placements ({models} models) from {ops} ops, {parts} parts, {meshes} distinct primitive meshes, pack {map.Pack?.Name}");
        if (map.MeshPlacements.Count != ops || models < 10 || parts < 200)
            Fail($"Mesh Test: {map.MeshPlacements.Count} placements for {ops} ops ({models} models, {parts} parts)");
        if (map.Pack?.Name != "Dev")
            Fail("Mesh Test isn't in the Dev pack");
        foreach (var p in map.MeshPlacements)
        {
            var b = p.Bounds;
            if (!float.IsFinite(b.Min.X + b.Max.Y + b.Min.Z) || b.Min.Y < -0.05f || MathF.Abs(b.Center.X) > map.HalfX || MathF.Abs(b.Center.Z) > map.HalfZ)
            {
                Fail($"Mesh Test: {p.Source} placed outside the map or under the ground ({b})");
                break;
            }
        }
        var placementCol = map.MeshPlacements.First(p => p.Model != null).BuildCollision();
        if (placementCol.TriangleCount == 0 || placementCol.Bounds.Min.Y < -0.01f)
            Fail("a placed model's world collision data is wrong");
        // Cosmetic: meshes add no boxes and don't change the fingerprint of a map.
        var json = MapFile.ToJson(map);
        var copy = MapFile.Load(json, "Mesh Test (round trip)");
        var same = copy.MeshPlacements.Count == map.MeshPlacements.Count
                   && copy.MeshPlacements.Zip(map.MeshPlacements).All(t => t.First.Source == t.Second.Source && t.First.Transform == t.Second.Transform
                                                                            && t.First.Color == t.Second.Color && t.First.Layer == t.Second.Layer
                                                                            && ReferenceEquals(t.First.Mesh, t.Second.Mesh) && ReferenceEquals(t.First.Model, t.Second.Model));
        if (!same)
            Fail("Mesh Test: the map file round trip changed its meshes");
        var noMeshes = MapFile.Load(System.Text.RegularExpressions.Regex.Replace(json, "\\s*\\{ \"op\": \"(Mesh|Model)\"[^\\n]*\\},?\\n", "\n").Replace(",\n  ]", "\n  ]"), "Mesh Test without meshes");
        if (noMeshes.ContentHash != copy.ContentHash || noMeshes.Solid.Count != copy.Solid.Count || noMeshes.MeshPlacements.Count != 0)
            Fail("Mesh and Model ops changed the map's boxes or fingerprint (they must be cosmetic)");

        // Bad ops give readable errors.
        const string head = "{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ { \"op\": \"Spawn\", \"x\": 1, \"z\": 1 }, ";
        foreach (var (op, expect) in new[]
                 {
                     ("{ \"op\": \"Mesh\", \"mesh\": \"cilinder\", \"x\": 0, \"z\": 0, \"color\": [1, 1, 1] }", "did you mean \"cylinder\""),
                     ("{ \"op\": \"Mesh\", \"mesh\": \"cone\", \"x\": 0, \"z\": 0, \"color\": [1, 1, 1], \"material\": \"Marble\" }", "unknown material"),
                     ("{ \"op\": \"Mesh\", \"mesh\": \"cone\", \"x\": 0, \"z\": 0 }", "needs \"color\""),
                     ("{ \"op\": \"Mesh\", \"mesh\": \"cone\", \"x\": 0, \"z\": 0, \"color\": [1, 1, 1], \"segment\": 8 }", "no parameter \"segment\""),
                     ("{ \"op\": \"Model\", \"file\": \"Models/nope.glb\", \"x\": 0, \"z\": 0 }", "not found"),
                     ("{ \"op\": \"Model\", \"file\": \"Models/barrel.glb\", \"x\": 0, \"z\": 0, \"scale\": 0 }", "scale is 0.01 to 100"),
                 })
        {
            try
            {
                MapFile.Load(head + op + " ] }", "bad.json");
                Fail($"a bad mesh op loaded without complaint (expected '{expect}')");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.Ordinal))
            {
            }
            catch (Exception ex)
            {
                Fail($"bad mesh op gave the wrong error (expected '{expect}'): {ex.Message}");
            }
        }
    }
}
