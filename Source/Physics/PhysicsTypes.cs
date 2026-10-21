using System.Numerics;

namespace CallOfSlop;

/// <summary>Which player movement code the server runs (and every client predicts with). Sent in the Welcome and the server info.</summary>
public enum PhysicsMode : byte
{
    /// <summary>The original box-by-box movement (<see cref="ClassicMover"/>). The default.</summary>
    Classic,

    /// <summary>The capsule character controller: sweep and slide, step-up, ground snap (<see cref="SweptMover"/>).</summary>
    Swept,
}

/// <summary>What kind of collision shape a query hit.</summary>
public enum ShapeKind : byte
{
    None,

    /// <summary>A map box (<see cref="Map.Solid"/>; <see cref="ShapeId.Index"/> is its index there).</summary>
    Box,

    /// <summary>A prop's collider box (also in <see cref="Map.Solid"/>, same index space as <see cref="Box"/>).</summary>
    Prop,

    /// <summary>A static oriented box added to the physics world (<see cref="PhysicsWorld.AddObb"/>).</summary>
    Obb,

    /// <summary>A triangle of a static mesh collider (<see cref="ShapeId.Index"/> = mesh, <see cref="ShapeId.Sub"/> = triangle).</summary>
    Mesh,

    /// <summary>The terrain heightfield (<see cref="ShapeId.Index"/> = cell index for contacts, 0 for ray hits).</summary>
    Terrain,

    /// <summary>A mover box at its pose (<see cref="ShapeId.Index"/> = mover, <see cref="ShapeId.Sub"/> = part).</summary>
    Mover,

    /// <summary>A mover's whole sweep as a pillar (bot navigation only; <see cref="ShapeId.Index"/> = mover).</summary>
    MoverSweep,

    /// <summary>Reserved for capsule colliders (characters are capsules in the swept mover, but aren't in the world).</summary>
    Capsule,

    /// <summary>A piece of a map boundary's invisible barrier (<see cref="Map.BoundaryBarriers"/>; <see cref="ShapeId.Index"/> = segment).</summary>
    Boundary,
}

/// <summary>Identifies one collision shape: its kind, an index in that kind's list and a sub-part (triangle, mover part).</summary>
public readonly record struct ShapeId(ShapeKind Kind, int Index, int Sub = 0)
{
    public static readonly ShapeId None = default;

    public override string ToString() => Kind == ShapeKind.None ? "none" : $"{Kind} {Index}" + (Sub != 0 ? $".{Sub}" : "");
}

/// <summary>Layers a query can include. Combine with |.</summary>
[Flags]
public enum QueryLayers : ushort
{
    None = 0,
    Boxes = 1,
    Props = 2,
    Terrain = 4,
    Movers = 8,
    MoverSweeps = 16,
    Obbs = 32,
    Meshes = 64,

    /// <summary>A map boundary's invisible barrier (<see cref="Map.BoundaryBarriers"/>). Not in <see cref="Static"/>: the classic body check stays exactly as it was, and movement is held inside by the boundary clamp.</summary>
    Boundary = 128,

    /// <summary>Everything static except the terrain.</summary>
    StaticShapes = Boxes | Props | Obbs | Meshes,

    /// <summary>Everything that never moves.</summary>
    Static = StaticShapes | Terrain,

    /// <summary>Everything solid as it is now: static geometry and movers at their pose. Bullets, line of sight, grenades.</summary>
    World = Static | Movers | Boundary,

    /// <summary>Bot navigation: static geometry with every mover's whole sweep as a pillar (never where a mover happens to be).</summary>
    Nav = Static | MoverSweeps | Boundary,

    /// <summary>Everything that lives in the physics world's BVH.</summary>
    Tree = StaticShapes | Boundary,
}

/// <summary>
/// What a query looks at: layers, the tick movers are posed at, and shapes to ignore. Value type, no allocation
/// (the ignore list is an optional array the caller keeps).
/// </summary>
public readonly struct QueryFilter
{
    public readonly QueryLayers Layers;

    /// <summary>Server tick for mover poses; NaN means <see cref="MoverPhysics.Clock"/> at the time of the query.</summary>
    public readonly double Tick;

    /// <summary>Shapes to skip (mover entries match every part of that mover). Null = none.</summary>
    public readonly ShapeId[]? Ignore;

    public QueryFilter(QueryLayers layers, double tick = double.NaN, ShapeId[]? ignore = null)
    {
        Layers = layers;
        Tick = tick;
        Ignore = ignore;
    }

    /// <summary>Static geometry, terrain and movers at the current clock (what <see cref="World.RaycastWorld"/> sees).</summary>
    public static QueryFilter All => new(QueryLayers.World);

    /// <summary>Static geometry and terrain, no movers.</summary>
    public static QueryFilter StaticOnly => new(QueryLayers.Static);

    /// <summary>Map and prop boxes only (no terrain, no movers): <see cref="World.RaycastBoxes"/>.</summary>
    public static QueryFilter BoxesOnly => new(QueryLayers.StaticShapes);

    /// <summary>Bot navigation: static geometry plus mover sweep pillars.</summary>
    public static QueryFilter Navigation => new(QueryLayers.Nav);

    public QueryFilter AtTick(double tick) => new(Layers, tick, Ignore);

    public QueryFilter Ignoring(params ShapeId[] ids) => new(Layers, Tick, ids);

    public QueryFilter With(QueryLayers layers) => new(Layers | layers, Tick, Ignore);

    public QueryFilter Without(QueryLayers layers) => new(Layers & ~layers, Tick, Ignore);

    public bool Has(QueryLayers l) => (Layers & l) != 0;

    /// <summary>The tick movers are posed at for this query.</summary>
    public double MoverTick => double.IsNaN(Tick) ? MoverPhysics.Clock : Tick;

    /// <summary>Is this shape on the ignore list?</summary>
    public bool Ignores(ShapeKind kind, int index)
    {
        if (Ignore == null)
            return false;
        foreach (var id in Ignore)
            if (id.Kind == kind && id.Index == index)
                return true;
        return false;
    }
}

/// <summary>The closest hit of a ray or shape cast.</summary>
public struct RayHit
{
    /// <summary>Distance travelled along the (unit) direction until contact.</summary>
    public float Distance;

    /// <summary>The contact point (for a ray: origin + dir * distance; for a shape cast: on the surface hit).</summary>
    public Vector3 Point;

    /// <summary>Unit surface normal at the contact, pointing back towards the caster.</summary>
    public Vector3 Normal;

    public ShapeId Shape;

    public readonly ShapeKind Kind => Shape.Kind;
}

/// <summary>A capsule: a core segment from <see cref="A"/> to <see cref="B"/> grown by <see cref="Radius"/>. A = B is a sphere.</summary>
public readonly record struct Capsule(Vector3 A, Vector3 B, float Radius)
{
    public Capsule Offset(Vector3 by) => new(A + by, B + by, Radius);

    public Vector3 Min => Vector3.Min(A, B) - new Vector3(Radius);
    public Vector3 Max => Vector3.Max(A, B) + new Vector3(Radius);
}

/// <summary>
/// An oriented box: centre, half size and the world directions of its local axes (orthonormal). Yaw-only boxes (movers,
/// turned props) and fully rotated ones (ramps) use the same code; an axis-aligned box is the identity rotation.
/// </summary>
public readonly struct Obb
{
    public readonly Vector3 Center, Half;
    public readonly Vector3 AxisX, AxisY, AxisZ;

    public Obb(Vector3 center, Vector3 half, Vector3 axisX, Vector3 axisY, Vector3 axisZ)
    {
        Center = center;
        Half = half;
        AxisX = axisX;
        AxisY = axisY;
        AxisZ = axisZ;
    }

    /// <summary>A box turned by any rotation.</summary>
    public Obb(Vector3 center, Vector3 half, Quaternion rotation)
        : this(center, half, Vector3.Transform(Vector3.UnitX, rotation), Vector3.Transform(Vector3.UnitY, rotation), Vector3.Transform(Vector3.UnitZ, rotation))
    {
    }

    /// <summary>An axis-aligned box as an OBB (identity axes, so local coordinates are exact copies of the world ones).</summary>
    public static Obb FromBox(in Box b) => new(b.Center, b.Half, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    /// <summary>A box turned about the vertical by <paramref name="cos"/>/<paramref name="sin"/> of its yaw (the mover convention, see <see cref="MoverPose"/>).</summary>
    public static Obb FromYaw(Vector3 center, Vector3 half, float cos, float sin) =>
        new(center, half, new Vector3(cos, 0, sin), Vector3.UnitY, new Vector3(-sin, 0, cos));

    public static Obb FromYaw(Vector3 center, Vector3 half, float yaw)
    {
        var (s, c) = MathF.SinCos(yaw);
        return FromYaw(center, half, c, s);
    }

    /// <summary>A world point in box coordinates (centre at the origin).</summary>
    public Vector3 ToLocal(Vector3 p)
    {
        var d = p - Center;
        return new Vector3(Vector3.Dot(d, AxisX), Vector3.Dot(d, AxisY), Vector3.Dot(d, AxisZ));
    }

    /// <summary>A world direction in box coordinates.</summary>
    public Vector3 DirToLocal(Vector3 v) => new(Vector3.Dot(v, AxisX), Vector3.Dot(v, AxisY), Vector3.Dot(v, AxisZ));

    /// <summary>A box-coordinate direction in world space.</summary>
    public Vector3 DirToWorld(Vector3 l) => AxisX * l.X + AxisY * l.Y + AxisZ * l.Z;

    public Vector3 ToWorld(Vector3 l) => Center + DirToWorld(l);

    /// <summary>World-space bounds.</summary>
    public void Bounds(out Vector3 min, out Vector3 max)
    {
        var e = Vector3.Abs(AxisX) * Half.X + Vector3.Abs(AxisY) * Half.Y + Vector3.Abs(AxisZ) * Half.Z;
        min = Center - e;
        max = Center + e;
    }
}

/// <summary>
/// A per-body gravity source. Today one uniform field (straight down, <see cref="World.Gravity"/>); a later gravity system can
/// return a different vector per zone, and the swept mover takes "up" as the opposite of whatever this returns.
/// </summary>
public interface IGravityField
{
    /// <summary>Gravity acceleration (m/s²) at a world position.</summary>
    Vector3 GravityAt(Vector3 position);
}

/// <summary>The same gravity everywhere.</summary>
public sealed class UniformGravity(Vector3 gravity) : IGravityField
{
    public Vector3 Gravity { get; } = gravity;

    public Vector3 GravityAt(Vector3 position) => Gravity;

    /// <summary>The game's standard gravity: <see cref="World.Gravity"/> straight down.</summary>
    public static readonly UniformGravity Down = new(new Vector3(0, -World.Gravity, 0));
}
