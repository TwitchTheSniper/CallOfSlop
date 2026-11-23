using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where a planetoid's plants give way to bases (Sean, 2026-11-23: "trees removed and foliage removed when a foundation is
/// placed, or an object is anchored, so trees don't pop through buildings"): the box round every anchored construct
/// standing on a planet, from a construct world (each game's own: the host's and a joined client's each keep theirs).
/// The drawing folds away the plants whose foot is in one (FoliageRendererPlanet.cs, building the cells round a change
/// again), and the construct contacts skip the trunks in one (PlanetFlora.GatherTrunks). Remade at most twice a second;
/// nothing is allocated once its lists have grown.
/// </summary>
public sealed class PlanetClearings
{
    /// <summary>The anchored bodies' boxes (world space; the rest frame's bounds at the body's pose).</summary>
    public readonly List<Obb> Boxes = new();

    /// <summary>Changes each time <see cref="Boxes"/> does.</summary>
    public int Version;

    private readonly List<Obb> _next = new();
    private long _at = long.MinValue;

    /// <summary>How far sideways a tree's foot must be from a base to stay (its canopy's reach), and other plants'.</summary>
    public const float TreeMargin = 3f, PlantMargin = 0.5f;

    /// <summary>The longest a clearing goes without being remade (seconds).</summary>
    private const double Every = 0.5;

    /// <summary>Remakes the boxes from a construct world's anchored bodies near the planet's ground, at most every half second.</summary>
    public void Update(ConstructWorld world, Planetoid planet)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now - _at < Every * System.Diagnostics.Stopwatch.Frequency)
            return;
        _at = now;
        _next.Clear();
        foreach (var c in world.Bodies)
        {
            if (!c.Anchored || c.PartCount == 0 || c.LocalMax.X < c.LocalMin.X)
                continue;
            var mid = (c.LocalMin + c.LocalMax) * 0.5f;
            var half = (c.LocalMax - c.LocalMin) * 0.5f;
            var centre = c.Pos + Vector3.Transform(mid, c.Rot);
            if (planet.RoughAltitude(centre) > half.Length() + 20)
                continue; // (anchored up in the sky or in space: over no plants)
            _next.Add(new Obb(centre, half, Vector3.Transform(Vector3.UnitX, c.Rot), Vector3.Transform(Vector3.UnitY, c.Rot), Vector3.Transform(Vector3.UnitZ, c.Rot)));
        }
        if (Same(_next, Boxes))
            return;
        Boxes.Clear();
        Boxes.AddRange(_next);
        Version++;
    }

    private static bool Same(List<Obb> a, List<Obb> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
            if (Vector3.DistanceSquared(a[i].Center, b[i].Center) > 0.01f || Vector3.DistanceSquared(a[i].Half, b[i].Half) > 0.01f
                || Vector3.Dot(a[i].AxisX, b[i].AxisX) < 0.9999f || Vector3.Dot(a[i].AxisY, b[i].AxisY) < 0.9999f)
                return false;
        return true;
    }

    /// <summary>
    /// Is a plant's foot inside a box (sideways within <paramref name="margin"/> of it, from 3 m under its bottom to its top)?
    /// The box's own "up" is the axis nearest the planet's up at the foot.
    /// </summary>
    public static bool Covers(in Obb b, Vector3 foot, Vector3 up, float margin)
    {
        var l = b.ToLocal(foot);
        float ax = MathF.Abs(Vector3.Dot(b.AxisX, up)), ay = MathF.Abs(Vector3.Dot(b.AxisY, up)), az = MathF.Abs(Vector3.Dot(b.AxisZ, up));
        // (which of the box's axes stands along up, and which way it points)
        var k = ax > ay ? (ax > az ? 0 : 2) : (ay > az ? 1 : 2);
        float lu = k == 0 ? l.X : k == 1 ? l.Y : l.Z, hu = k == 0 ? b.Half.X : k == 1 ? b.Half.Y : b.Half.Z;
        var axis = k == 0 ? b.AxisX : k == 1 ? b.AxisY : b.AxisZ;
        if (Vector3.Dot(axis, up) < 0)
            lu = -lu;
        if (lu < -hu - 3 || lu > hu + margin)
            return false;
        return (k == 0 || MathF.Abs(l.X) <= b.Half.X + margin) && (k == 1 || MathF.Abs(l.Y) <= b.Half.Y + margin) && (k == 2 || MathF.Abs(l.Z) <= b.Half.Z + margin);
    }

    /// <summary>Is a foot inside any box (all of them: for a few points at a time)?</summary>
    public bool AnyCovers(Vector3 foot, Vector3 up, float margin)
    {
        foreach (var b in Boxes)
            if (Covers(b, foot, up, margin))
                return true;
        return false;
    }
}
