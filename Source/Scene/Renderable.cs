using System.Numerics;

namespace CallOfSlop;

/// <summary>What a <see cref="Renderable"/> draws.</summary>
public enum RenderKind : byte
{
    None,

    /// <summary>The renderer's unit cube, transformed by the node's world matrix (every box in the game today).</summary>
    Box,

    /// <summary>A mesh from the mesh pipeline, by id (drawn through an <see cref="IMeshDrawer"/>).</summary>
    Mesh,
}

/// <summary>Render state a draw needs. <see cref="DrawList.Submit"/> switches state only when it changes.</summary>
[Flags]
public enum RenderFlags : byte
{
    None = 0,

    /// <summary>Alpha blended (glass, glow, fading optic housings, muzzle flashes).</summary>
    Blend = 1,

    /// <summary>Don't write depth (soft transparent things).</summary>
    NoDepthWrite = 2,
}

/// <summary>
/// What a scene node draws: a box today, a mesh id from the mesh pipeline later, with the same parameters
/// <see cref="Renderer.Draw"/> takes (colour, alpha, shading mode, surface material) plus render state flags.
/// A plain struct stored in the node, so flattening needs no virtual calls or allocations.
/// </summary>
public struct Renderable
{
    public RenderKind Kind;

    /// <summary>Mesh id for <see cref="RenderKind.Mesh"/> (the mesh pipeline's handle; unused for boxes).</summary>
    public int MeshId;

    public Vector3 Color;
    public float Alpha;

    /// <summary>Renderer shading mode: 0 = lit, 1 = lit + world texture, 2 = unlit.</summary>
    public byte Mode;

    public Material Material;
    public RenderFlags Flags;

    /// <summary>A unit-cube box (scale it with the node's transform).</summary>
    public static Renderable Box(Vector3 color, float alpha = 1f, int mode = 0, Material material = Material.Grid, RenderFlags flags = RenderFlags.None) =>
        new() { Kind = RenderKind.Box, MeshId = -1, Color = color, Alpha = alpha, Mode = (byte)mode, Material = material, Flags = flags };

    /// <summary>A mesh from the mesh pipeline.</summary>
    public static Renderable Mesh(int meshId, Vector3 color, float alpha = 1f, int mode = 0, Material material = Material.Grid, RenderFlags flags = RenderFlags.None) =>
        new() { Kind = RenderKind.Mesh, MeshId = meshId, Color = color, Alpha = alpha, Mode = (byte)mode, Material = material, Flags = flags };
}

/// <summary>
/// A custom drawable a node can carry (<see cref="SceneNode.Custom"/>) when a single <see cref="Renderable"/> isn't enough,
/// e.g. a multi-primitive glTF mesh or a particle effect: it appends its own items to the draw list.
/// </summary>
public interface IRenderable
{
    /// <summary>Appends draw items for <paramref name="node"/> (its world matrix is up to date).</summary>
    void Emit(DrawList list, SceneNode node);
}

/// <summary>
/// Draws meshes for <see cref="DrawList.Submit"/>: the mesh pipeline implements this. Boxes keep going through the
/// existing <see cref="Renderer"/>; without a mesh drawer, mesh items are skipped (and counted).
/// </summary>
public interface IMeshDrawer
{
    void DrawMesh(int meshId, in Matrix4x4 world, Vector3 color, float alpha, int mode, Material material);
}

/// <summary>
/// Receives what a <see cref="Renderer"/> would draw instead of calling OpenGL (set <see cref="Renderer.Recorder"/>).
/// The self-test uses it to compare the scene-graph draws with the old immediate-mode code, draw by draw, headlessly.
/// </summary>
public interface IDrawRecorder
{
    void Draw(in Matrix4x4 model, Vector3 color, float alpha, int mode, Material material);
    void SetBlend(bool on);
    void SetDepthWrite(bool on);
}
