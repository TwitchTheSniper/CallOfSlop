using System.Numerics;

namespace CallOfSlop;

/// <summary>The shape of a gravity zone.</summary>
public enum GravityZoneShape : byte
{
    Box,
    Sphere,
}

/// <summary>
/// One region of a <see cref="ZonedGravity"/> field: a box (<see cref="Min"/>..<see cref="Max"/>) or a sphere
/// (<see cref="Center"/>, <see cref="Radius"/>) and the field inside it (a <see cref="UniformGravity"/> in any direction and
/// strength, or a <see cref="PointGravity"/> for a rock you walk round).
/// </summary>
public sealed class GravityZone
{
    public GravityZoneShape Shape { get; }

    public Vector3 Min { get; }

    public Vector3 Max { get; }

    public Vector3 Center { get; }

    public float Radius { get; }

    public IGravityField Field { get; }

    /// <summary>A label for messages (the map file's "name", or "zone N").</summary>
    public string Name { get; }

    public GravityZone(GravityZoneShape shape, Vector3 min, Vector3 max, Vector3 center, float radius, IGravityField field, string name)
    {
        Shape = shape;
        Min = min;
        Max = max;
        Center = center;
        Radius = radius;
        Field = field;
        Name = name;
    }

    public static GravityZone Box(Vector3 min, Vector3 max, IGravityField field, string name = "") =>
        new(GravityZoneShape.Box, Vector3.Min(min, max), Vector3.Max(min, max), (min + max) * 0.5f, 0f, field, name);

    public static GravityZone Sphere(Vector3 center, float radius, IGravityField field, string name = "") =>
        new(GravityZoneShape.Sphere, center - new Vector3(radius), center + new Vector3(radius), center, radius, field, name);

    /// <summary>Is a position inside the zone (its faces and surface count as inside)?</summary>
    public bool Contains(Vector3 p)
    {
        if (Shape == GravityZoneShape.Box)
            return p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
        return Vector3.DistanceSquared(p, Center) <= Radius * Radius;
    }

    /// <summary>Is the zone's up world +Y everywhere inside it (a low-gravity zone, say)?</summary>
    public bool StraightDown => Field is UniformGravity { StraightDown: true };
}

/// <summary>
/// Several gravity regions on one map (the map file's <c>"gravity": { "type": "zones", ... }</c>): the field at a position
/// is the first zone containing it, else the <see cref="Default"/> field. A pure function of position, so every PC agrees;
/// a lookup is a handful of box and sphere tests with no allocation.
/// </summary>
public sealed class ZonedGravity : IGravityField
{
    public IGravityField Default { get; }

    public GravityZone[] Zones { get; }

    public ZonedGravity(IGravityField defaultField, GravityZone[] zones)
    {
        Default = defaultField;
        Zones = zones;
    }

    /// <summary>The index of the zone a position is in, or -1 for the default field.</summary>
    public int ZoneAt(Vector3 p)
    {
        var zones = Zones;
        for (var i = 0; i < zones.Length; i++)
            if (zones[i].Contains(p))
                return i;
        return -1;
    }

    /// <summary>The field at a position.</summary>
    public IGravityField FieldAt(Vector3 p)
    {
        var zones = Zones;
        for (var i = 0; i < zones.Length; i++)
            if (zones[i].Contains(p))
                return zones[i].Field;
        return Default;
    }

    /// <summary>Is up exactly world +Y at a position?</summary>
    public bool PlainUpAt(Vector3 p) => FieldAt(p) is UniformGravity { StraightDown: true };

    public Vector3 GravityAt(Vector3 position) => FieldAt(position).GravityAt(position);

    public Vector3 UpAt(Vector3 position) => FieldAt(position).UpAt(position);

    public Vector3 AxisAt(Vector3 position) => FieldAt(position).AxisAt(position);

    public float HeightOf(Vector3 position) => FieldAt(position).HeightOf(position);

    /// <summary>Fingerprint data: the default field and every zone's shape, bounds and field.</summary>
    public void Mix(Action<float> mix)
    {
        mix(3f); // the kind
        Default.Mix(mix);
        mix(Zones.Length);
        foreach (var z in Zones)
        {
            mix(z.Shape == GravityZoneShape.Box ? 10f : 11f);
            mix(z.Min.X); mix(z.Min.Y); mix(z.Min.Z);
            mix(z.Max.X); mix(z.Max.Y); mix(z.Max.Z);
            mix(z.Radius);
            z.Field.Mix(mix);
        }
    }
}
