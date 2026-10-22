using System.Numerics;

namespace CallOfSlop;

/// <summary>How a decal mixes with the surface under it.</summary>
public enum DecalBlend : byte
{
    /// <summary>Painted on: the decal's colour, lit like the surface, over it by its alpha (paint, posters, bullet holes).</summary>
    Alpha,

    /// <summary>Darkens: the surface's colour times the decal's colour, by its alpha (dirt, oil, scorch, water stains).</summary>
    Multiply,
}

/// <summary>Which way a surface may face for a decal to land on it (by the surface normal).</summary>
[Flags]
public enum DecalSurfaces : byte
{
    None = 0,

    /// <summary>Faces up (normal y above 0.7): floors, roads, the terrain, table tops.</summary>
    Floor = 1,

    /// <summary>Faces sideways: walls, crates' sides.</summary>
    Wall = 2,

    /// <summary>Faces down (normal y below -0.7).</summary>
    Ceiling = 4,

    All = Floor | Wall | Ceiling,
}

/// <summary>The procedural shapes a decal's layers are drawn with (see Decals/README.md).</summary>
public enum DecalShapeKind : byte
{
    Circle,
    Ring,
    Rect,
    Splat,
    Cracks,
    Speckle,
    Line,
    Stripes,
}

/// <summary>
/// One layer of a decal's picture: a shape drawn into the decal's square (0..1 on both axes, y up, centre 0.5, 0.5).
/// Layers are drawn in order, each over the ones before (or cutting them away with <see cref="Erase"/>).
/// </summary>
public sealed class DecalShape
{
    public DecalShapeKind Kind;

    /// <summary>Centre (circle, ring, rect, splat, speckle, stripes), start (line) or origin (cracks).</summary>
    public Vector2 At = new(0.5f, 0.5f);

    /// <summary>The line's end.</summary>
    public Vector2 To = new(0.5f, 0.5f);

    /// <summary>Radius (circle, ring, splat; speckle: the area the dots fall in).</summary>
    public float Radius = 0.3f;

    /// <summary>Stroke width (ring, line, cracks; speckle: the largest dot's radius).</summary>
    public float Width = 0.04f;

    /// <summary>Width and height (rect, stripes).</summary>
    public Vector2 Size = new(0.5f, 0.5f);

    /// <summary>Turn in degrees (rect, stripes).</summary>
    public float Rotation;

    /// <summary>Lobes (splat), cracks, dots (speckle), droplets round a splat.</summary>
    public int Count = 6;

    /// <summary>Crack length (in the square's units).</summary>
    public float Length = 0.4f;

    /// <summary>How ragged the edge (splat) or how crooked the cracks are, 0..1.</summary>
    public float Jag = 0.35f;

    /// <summary>Random seed for the shape's irregularity.</summary>
    public int Seed;

    /// <summary>Stripe period and the painted part of it (0..1) for stripes.</summary>
    public float Period = 0.2f, Duty = 0.5f;

    /// <summary>Colour (rgb) and opacity (a).</summary>
    public Vector4 Color = new(0.1f, 0.1f, 0.1f, 1f);

    /// <summary>Edge softness in the square's units (0 = crisp, still anti-aliased).</summary>
    public float Softness;

    /// <summary>Grunge: 0..1 of the coverage taken away by value noise of <see cref="NoiseScale"/> cells per square.</summary>
    public float Noise, NoiseScale = 8f;

    /// <summary>Cuts the layers below away instead of painting (holes in a stain, a stencil).</summary>
    public bool Erase;
}

/// <summary>
/// One decal from the Decals folder: a flat picture projected onto surfaces. Built from a decal file (<see cref="DecalFile"/>):
/// procedural layers and/or a small PNG, a size in metres, variation rules and where it may land. Map ops place it
/// (Decal, DecalScatter); a decal with "impact" materials is also a bullet-impact mark for those surfaces.
/// </summary>
public sealed class DecalDef
{
    public string Name = "";
    public string Category = "Misc";
    public string Description = "";
    public string Author = "";

    /// <summary>The file it was loaded from (relative to the Decals folder).</summary>
    public string SourceFile = "";

    /// <summary>Width and height in metres (before a placement's own size or variation).</summary>
    public Vector2 Size = new(1f, 1f);

    /// <summary>Each placement's size varies by up to this fraction either way (0..0.9).</summary>
    public float SizeVariation;

    /// <summary>Fixed turn in degrees, or NaN for a random turn per placement.</summary>
    public float Rotation = float.NaN;

    /// <summary>Each placement's brightness varies by up to this fraction either way (0..0.5).</summary>
    public float TintVariation;

    public DecalBlend Blend = DecalBlend.Alpha;

    /// <summary>The surfaces it may land on.</summary>
    public DecalSurfaces Surfaces = DecalSurfaces.All;

    /// <summary>Overall opacity (0..1).</summary>
    public float Opacity = 1f;

    /// <summary>Fades out between 75% and 100% of this distance (metres); 0 picks one from the size.</summary>
    public float FadeDistance;

    /// <summary>The picture's layers, drawn in order (over <see cref="ImagePath"/> if there is one).</summary>
    public readonly List<DecalShape> Layers = new();

    /// <summary>Full path of the optional PNG under the layers (null: none).</summary>
    public string? ImagePath;

    /// <summary>The surface materials this is a bullet impact for (Concrete, Brick, Wood, Metal, Tiles, Dirt, Grid, Terrain, Default); empty: not an impact.</summary>
    public string[] ImpactMaterials = [];

    /// <summary>How likely this impact is picked among the others for the same material (1 = normal).</summary>
    public float ImpactWeight = 1f;

    /// <summary>Position in <see cref="Decals.All"/>, which is also its layer in the decal atlas.</summary>
    public int Index;

    /// <summary>Is this a bullet-impact decal?</summary>
    public bool IsImpact => ImpactMaterials.Length > 0;

    /// <summary>The distance it fades out by: <see cref="FadeDistance"/>, or 30 m plus 25 m per metre of size (at most 150 m).</summary>
    public float FadeEnd => FadeDistance > 0 ? FadeDistance : MathF.Min(30f + 25f * MathF.Max(Size.X, Size.Y), 150f);

    public override string ToString() => $"{Name} ({Category})";
}
