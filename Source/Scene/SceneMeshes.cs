using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Mesh handles for scene nodes: a <see cref="Renderable"/> of kind <see cref="RenderKind.Mesh"/> stores a
/// <see cref="MeshAsset.Id"/>, and this registry turns it back into the asset when the draw list is submitted
/// (<see cref="MeshRenderer"/> implements <see cref="IMeshDrawer"/> and draws them instanced). Lookups are an array index.
/// </summary>
public static class SceneMeshes
{
    private static MeshAsset?[] _byId = new MeshAsset?[64];
    private static readonly object Gate = new();

    /// <summary>Makes <paramref name="asset"/> drawable from a scene node and returns its handle (its id).</summary>
    public static int Register(MeshAsset asset)
    {
        lock (Gate)
        {
            if (asset.Id >= _byId.Length)
                Array.Resize(ref _byId, Math.Max(_byId.Length * 2, asset.Id + 1));
            _byId[asset.Id] = asset;
        }
        return asset.Id;
    }

    /// <summary>The asset behind a handle, or null if it was never registered.</summary>
    public static MeshAsset? Get(int id)
    {
        var a = _byId;
        return (uint)id < (uint)a.Length ? a[id] : null;
    }

    /// <summary>A renderable that draws <paramref name="asset"/> (registered on the way).</summary>
    public static Renderable Of(MeshAsset asset, Vector3 tint, int mode = 0, Material material = Material.Grid) =>
        Renderable.Mesh(Register(asset), tint, 1f, mode, material);

    /// <summary>
    /// Builds a loaded glTF model's node tree (<see cref="ModelAsset.Nodes"/>: parents before children) under
    /// <paramref name="root"/>: one <see cref="SceneNode"/> per glTF node with its name and local transform (TRS, or the
    /// node's matrix when it has shear the TRS can't hold), and a mesh renderable on the nodes that have a mesh.
    /// Empties named like sockets ("muzzle", "grip", "seat_0"...) keep their names, so <see cref="SceneNode.FindSocket"/>
    /// can be used after marking them, or <see cref="SceneNode.Find"/> as they are. Returns the nodes by glTF order.
    /// </summary>
    public static SceneNode[] BuildModel(ModelAsset model, SceneNode root, Vector3 tint)
    {
        var b = new SceneBuilder();
        foreach (var n in model.Nodes)
        {
            var r = n.Mesh >= 0 ? Of(model.Meshes[n.Mesh], tint) : default;
            // TRS when it reproduces the node's matrix, otherwise the matrix itself.
            var trs = Matrix4x4.CreateScale(n.Scale) * Matrix4x4.CreateFromQuaternion(n.Rotation) * Matrix4x4.CreateTranslation(n.Translation);
            if (Close(trs, n.Local))
                b.Add(n.Name, n.Parent, n.Translation, n.Rotation, n.Scale, r);
            else
                b.Add(n.Name, n.Parent, n.Local, r);
        }
        return b.Build(root);
    }

    private static bool Close(in Matrix4x4 a, in Matrix4x4 b)
    {
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 4; c++)
                if (MathF.Abs(a[r, c] - b[r, c]) > 1e-5f * MathF.Max(1f, MathF.Abs(b[r, c])))
                    return false;
        return true;
    }
}
