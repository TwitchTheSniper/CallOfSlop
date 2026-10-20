using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A mesh or model placed on a map by a Mesh or Model op. <see cref="Transform"/> takes it from its own space (standing
/// on y = 0) into the world; <see cref="Color"/> tints it and <see cref="Layer"/> is the procedural texture for
/// untextured surfaces (<see cref="MeshVertex.NoLayer"/>: plain colour).
/// </summary>
public sealed record MeshPlacement(string Source, MeshAsset? Mesh, ModelAsset? Model, Matrix4x4 Transform, Vector4 Color, float Layer)
{
    /// <summary>The placement's world bounding box.</summary>
    public MeshBounds Bounds => (Mesh?.Bounds ?? Model!.Bounds).Transform(Transform);

    /// <summary>Collision data in world space (exact triangles, box and convex points). Not used by gameplay yet.</summary>
    public MeshCollision BuildCollision() => Mesh != null
        ? MeshCollision.From(Mesh.Lods[0], Transform)
        : MeshCollision.From(Model!.Parts.Select(p => (Model.Meshes[p.Mesh].Lods[0], p.Transform * Transform)).ToList());
}

/// <summary>
/// Meshes on a map: procedural primitives (cylinders, spheres, ramps...) and glTF models, drawn instanced by
/// <see cref="MeshRenderer"/>. They are <b>cosmetic</b> for now: no collision, no effect on movement, bullets, bots or
/// the map fingerprint (so clients and servers never disagree because of them). Put a Block or Prop under or inside
/// one where players need something solid. Collision data is ready (<see cref="MeshPlacement.BuildCollision"/>) for
/// when the physics work can take it.
/// </summary>
public sealed partial class Map
{
    /// <summary>The most Mesh and Model placements one map may have.</summary>
    public const int MaxMeshPlacements = 20000;

    /// <summary>Every Mesh and Model op's result, in op order.</summary>
    public readonly List<MeshPlacement> MeshPlacements = new();

    /// <summary>
    /// A procedural mesh standing at (x, z) (on the ground or terrain, raised by <paramref name="y"/>). <paramref name="mesh"/>
    /// is box, roundedbox, cylinder, cone, sphere, icosphere, capsule, torus, wedge (ramp) or tube (pipe); width, height,
    /// depth, radius, inner, segments and detail shape it (see Maps/README.md), yaw/pitch/roll turn it (degrees) and
    /// scale grows it. <paramref name="material"/> picks a surface texture (Concrete, Brick, Wood, Metal, Tiles, Dirt,
    /// Grid) like the boxes'; empty is plain colour. Cosmetic: players and bullets pass through.
    /// </summary>
    [MapOp]
    public void Mesh(string mesh, float x, float z, Vector3 color, float y = 0, float width = 1, float height = 1, float depth = 1, float radius = 0,
        float inner = 0, int segments = 0, int detail = 0, float yaw = 0, float pitch = 0, float roll = 0, float scale = 1, string material = "")
    {
        using var __ = Op([mesh, x, z, color, y, width, height, depth, radius, inner, segments, detail, yaw, pitch, roll, scale, material]);
        var spec = new PrimitiveSpec(PrimitiveSpec.ParseKind(mesh), width, height, depth, radius, inner, segments, detail);
        var asset = MeshLibrary.Primitive(spec);
        AddMeshPlacement(new MeshPlacement(mesh, asset, null, PlacementTransform(x, y, z, yaw, pitch, roll, scale), new Vector4(color, 1), ParseMeshMaterial(material)));
    }

    /// <summary>
    /// A glTF model (.glb or .gltf, path relative to the game folder, e.g. "Models/barrel.glb") standing at (x, z), turned
    /// and scaled like <see cref="Mesh"/>. Drawn with its own materials. Cosmetic: players and bullets pass through.
    /// </summary>
    [MapOp]
    public void Model(string file, float x, float z, float y = 0, float yaw = 0, float pitch = 0, float roll = 0, float scale = 1)
    {
        using var __ = Op([file, x, z, y, yaw, pitch, roll, scale]);
        ModelAsset model;
        try
        {
            model = MeshLibrary.Model(file);
        }
        catch (ModelException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
        AddMeshPlacement(new MeshPlacement(file, null, model, PlacementTransform(x, y, z, yaw, pitch, roll, scale), Vector4.One, MeshVertex.NoLayer));
    }

    private Matrix4x4 PlacementTransform(float x, float y, float z, float yaw, float pitch, float roll, float scale)
    {
        if (scale is < 0.01f or > 100f || !float.IsFinite(scale))
            throw new InvalidOperationException("scale is 0.01 to 100");
        if (!float.IsFinite(x + y + z + yaw + pitch + roll))
            throw new InvalidOperationException("position and angles must be numbers");
        const float deg = MathF.PI / 180f;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromYawPitchRoll(yaw * deg, pitch * deg, roll * deg)
               * Matrix4x4.CreateTranslation(x, PointGround(x, z) + y, z);
    }

    private void AddMeshPlacement(MeshPlacement p)
    {
        if (MeshPlacements.Count >= MaxMeshPlacements)
            throw new InvalidOperationException($"too many meshes (the limit is {MaxMeshPlacements} per map)");
        MeshPlacements.Add(p);
    }

    /// <summary>"" (plain colour) or a surface texture name; throws with the list otherwise.</summary>
    public static float ParseMeshMaterial(string material)
    {
        if (string.IsNullOrWhiteSpace(material) || material.Equals("none", StringComparison.OrdinalIgnoreCase))
            return MeshVertex.NoLayer;
        if (Enum.TryParse<Material>(material.Trim(), true, out var m) && m != Material.Count && Enum.IsDefined(m))
            return (float)m;
        throw new InvalidOperationException($"unknown material \"{material}\" (use {string.Join(", ", Enum.GetNames<Material>().Where(n => n != nameof(Material.Count)))} or leave it out)");
    }
}
