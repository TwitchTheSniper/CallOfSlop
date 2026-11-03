using System.Numerics;

namespace CallOfSlop;

/// <summary>A map that is a planetoid (Source/Planets/Planetoid.cs): the top-level "planetoid" object.</summary>
public sealed partial class Map
{
    /// <summary>The map's planetoid (null: none).</summary>
    public Planetoid? Planetoid;

    /// <summary>
    /// Makes this map a planetoid: its ground, point gravity towards its centre (unless the map gives a "gravity" of its
    /// own) and the planet centre and radius the Surface ops place things by. Before any op.
    /// </summary>
    public void SetPlanetoid(Planetoid p)
    {
        if (Solid.Count > 0 || Spawns.Count > 0)
            throw new InvalidOperationException("SetPlanetoid must be called before any op");
        Planetoid = p;
        Gravity ??= new PointGravity(p.Center, p.Strength, -Vector3.UnitZ);
        PlanetCenter = p.Center;
        PlanetRadius = (float)p.Radius;
    }

    /// <summary>How far a planetoid map is drawn: to the horizon seen from the highest ground, and on to ground as high behind it.</summary>
    private float PlanetoidFarPlane
    {
        get
        {
            var pl = Planetoid!;
            var h = Math.Max(pl.MaxHeight, 200.0);
            return (float)Math.Min(2 * Math.Sqrt(2 * pl.Radius * h + h * h) + 2000, 40000);
        }
    }
}
