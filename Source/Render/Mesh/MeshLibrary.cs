using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One drawable mesh with its distance LODs. <see cref="Lods"/>[0] is full detail; later entries have fewer triangles
/// (primitives: rebuilt with fewer segments; models: simplified by meshoptimizer over the same vertices).
/// CPU data only: the renderer uploads it into the <see cref="MeshPool"/> once.
/// </summary>
public sealed class MeshAsset
{
    private static int _nextId;
    private MeshCollision? _collision;

    public readonly int Id;
    public readonly string Key;
    public readonly MeshData[] Lods;

    /// <summary>Draw both sides (glTF doubleSided materials); everything else is drawn with back faces culled.</summary>
    public readonly bool DoubleSided;

    /// <summary>Where the LODs came from: "segments", "meshoptimizer" or "none".</summary>
    public readonly string LodSource;

    /// <summary>The (resolved) shape of a library primitive, null for any other mesh (a baked map stores just this, MapBake.cs).</summary>
    public PrimitiveSpec? Spec { get; init; }

    public MeshAsset(string key, MeshData[] lods, bool doubleSided, string lodSource)
    {
        Id = Interlocked.Increment(ref _nextId);
        Key = key;
        Lods = lods;
        DoubleSided = doubleSided;
        LodSource = lodSource;
    }

    public MeshBounds Bounds => Lods[0].Bounds;

    /// <summary>Collision data (exact triangles, box and simplified convex points), built on first use.</summary>
    public MeshCollision Collision => _collision ??= MeshCollision.From(Lods[0]);
}

/// <summary>A node of a model's hierarchy, as written in the file. <see cref="Parent"/> and <see cref="Mesh"/> are indices (-1: none).</summary>
public readonly record struct ModelNode(string Name, int Parent, int Mesh, Vector3 Translation, Quaternion Rotation, Vector3 Scale, Matrix4x4 Local, Matrix4x4 World);

/// <summary>A model material as the game uses it: an sRGB base colour and an imported texture layer (-1: none).</summary>
public readonly record struct ModelMaterial(string Name, Vector4 BaseColor, int TextureLayer, bool DoubleSided);

/// <summary>Something to draw: mesh <see cref="Mesh"/> of the model at <see cref="Transform"/> (model space), from node <see cref="Node"/>.</summary>
public readonly record struct ModelPart(int Node, int Mesh, Matrix4x4 Transform);

/// <summary>
/// A loaded glTF model: its meshes (with LODs), materials and node tree. <see cref="Nodes"/> lists parents before their
/// children, so a scene graph can build its nodes in one pass (create, then attach to <see cref="ModelNode.Parent"/>).
/// </summary>
public sealed class ModelAsset
{
    public required string File { get; init; }
    public required MeshAsset[] Meshes { get; init; }
    public required string[] MeshNames { get; init; }
    public required ModelMaterial[] Materials { get; init; }
    public required ModelNode[] Nodes { get; init; }
    public required ModelPart[] Parts { get; init; }
    public required MeshBounds Bounds { get; init; }
    public required string[] Warnings { get; init; }
    public required int Textures { get; init; }
    public double LoadMs { get; internal set; }

    public int Triangles => Parts.Sum(p => Meshes[p.Mesh].Lods[0].TriangleCount);

    /// <summary>One collision soup for the whole model (every part transformed into model space).</summary>
    public MeshCollision BuildCollision() => MeshCollision.From(Parts.Select(p => (Meshes[p.Mesh].Lods[0], p.Transform)).ToList());

    /// <summary>The node tree as text, one node per line, indented by depth (for logs and tests).</summary>
    public string DescribeTree()
    {
        var sb = new System.Text.StringBuilder();
        var depth = new int[Nodes.Length];
        for (var i = 0; i < Nodes.Length; i++)
        {
            var n = Nodes[i];
            depth[i] = n.Parent >= 0 ? depth[n.Parent] + 1 : 0;
            sb.Append(' ', depth[i] * 2).Append(n.Name);
            if (n.Mesh >= 0)
                sb.Append(" [").Append(MeshNames[n.Mesh]).Append(']');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>
/// The cache of every mesh asset: primitives by their canonical spec, models by file. Map files ask for meshes while
/// they load (CPU only: building and optimising, no GL), so a shape used 200 times is built once and drawn instanced.
/// Thread-safe.
/// </summary>
public static class MeshLibrary
{
    private static readonly Dictionary<string, MeshAsset> Primitives = new();
    private static readonly Dictionary<string, (DateTime Stamp, ModelAsset Model)> Models = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>The folder model files are looked up in (next to the game).</summary>
    public static string ModelsFolder => Path.Combine(AppFolders.Data, "Models");

    /// <summary>LOD levels made for each primitive (full detail plus up to two simpler ones).</summary>
    public const int MaxLods = 3;

    /// <summary>How many primitive meshes and models are cached.</summary>
    public static (int Primitives, int Models) Counts
    {
        get
        {
            lock (Gate)
                return (Primitives.Count, Models.Count);
        }
    }

    /// <summary>The (cached) mesh of a primitive, with its LODs. Throws a readable message for bad parameters.</summary>
    public static MeshAsset Primitive(PrimitiveSpec spec)
    {
        var s = spec.Resolve();
        var key = s.Key;
        lock (Gate)
        {
            if (Primitives.TryGetValue(key, out var hit))
                return hit;
            var lods = new List<MeshData> { MeshOpt.Optimize(MeshPrimitives.Build(s)) };
            for (var lod = 1; lod < MaxLods; lod++)
                if (s.ForLod(lod) is { } simpler)
                    lods.Add(MeshOpt.Optimize(MeshPrimitives.Build(simpler)));
            var asset = new MeshAsset(key, lods.ToArray(), false, lods.Count > 1 ? "segments" : "none") { Spec = s };
            Primitives[key] = asset;
            return asset;
        }
    }

    /// <summary>Is <paramref name="asset"/> the library's primitive for its key (MapBake.cs)?</summary>
    public static bool IsPrimitive(MeshAsset asset)
    {
        lock (Gate)
            return Primitives.TryGetValue(asset.Key, out var hit) && ReferenceEquals(hit, asset);
    }

    /// <summary>Every model loaded so far, with its full path (a baked map refers to models by path, MapBake.cs).</summary>
    public static (string Path, ModelAsset Model)[] LoadedModels()
    {
        lock (Gate)
            return Models.Select(kv => (kv.Key, kv.Value.Model)).OrderBy(m => m.Key, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The (cached) model in <paramref name="file"/>: a path relative to the game folder ("Models/barrel.glb") or absolute.
    /// Reloaded when the file changed. Throws <see cref="ModelException"/> with a readable message. <paramref name="display"/>
    /// is the name used in logs and messages (default: <paramref name="file"/>).
    /// </summary>
    public static ModelAsset Model(string file, string? display = null)
    {
        if (string.IsNullOrWhiteSpace(file))
            throw new ModelException("no model file given");
        var path = Path.IsPathRooted(file) ? file : Path.Combine(AppFolders.Data, file.Replace('\\', '/'));
        if (!System.IO.File.Exists(path))
        {
            var near = Directory.Exists(ModelsFolder)
                ? Directory.GetFiles(ModelsFolder).Where(f => f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                    .Select(f => "Models/" + Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray()
                : [];
            throw new ModelException($"model file \"{file}\" not found{(near.Length > 0 ? $" (models: {string.Join(", ", near)})" : " (no Models folder next to the game)")}");
        }
        var stamp = System.IO.File.GetLastWriteTimeUtc(path);
        var full = Path.GetFullPath(path);
        lock (Gate)
        {
            if (Models.TryGetValue(full, out var hit) && hit.Stamp == stamp)
                return hit.Model;
        }
        var sw = Stopwatch.StartNew();
        var model = GltfModelLoader.Load(path, (display ?? file).Replace('\\', '/'));
        model.LoadMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[mesh] model {model.File}: {model.Nodes.Length} nodes, {model.Meshes.Length} meshes, {model.Triangles} triangles, "
            + $"{model.Materials.Length} materials, {model.Textures} textures, LODs {string.Join("/", model.Meshes.Select(m => m.Lods.Length))}, {model.LoadMs:0.0} ms"
            + (model.Warnings.Length > 0 ? $"; warnings: {string.Join("; ", model.Warnings)}" : ""));
        lock (Gate)
            Models[full] = (stamp, model);
        return model;
    }
}
