using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bullet impacts as decals: <see cref="Game"/> reports every tracer it draws (each client gets the same server-confirmed
/// end points, so every client places the same marks; no extra network traffic), this finds the surface right at the
/// end point with one short ray, picks an impact decal for the surface's material and adds it to <see cref="Pool"/>.
/// Kept apart from impact particles and sounds. Off (capacity 0) until the renderer applies the player's setting, so
/// servers and the headless self-test pay nothing.
/// </summary>
public static class ImpactDecals
{
    /// <summary>The impacts of the map being played.</summary>
    public static readonly ImpactPool Pool = new();

    /// <summary>The setting the pool is sized for.</summary>
    public static ImpactDecalQuality Quality { get; private set; } = ImpactDecalQuality.Off;

    /// <summary>Tracers looked at and impacts placed since startup (for the log).</summary>
    public static long Tracers, Placed, NoSurface;

    private static readonly string[] MaterialNames = Enumerable.Range(0, (int)Material.Count).Select(i => ((Material)i).ToString()).ToArray();

    /// <summary>Applies the setting (resizes and clears the pool when it changes).</summary>
    public static void Configure(ImpactDecalQuality quality)
    {
        if (quality == Quality)
            return;
        Quality = quality;
        Pool.Resize(ImpactPool.CapacityFor(quality));
    }

    /// <summary>
    /// A tracer from <paramref name="from"/> to <paramref name="to"/> by player <paramref name="owner"/>: if it ends on a
    /// surface of <paramref name="map"/> (the map being played), an impact decal goes there. <paramref name="pellet"/>: one of
    /// several pellets of a shot. Allocation-free. Returns true if a decal was added (or merged).
    /// </summary>
    public static bool OnTracer(Map map, Vector3 from, Vector3 to, byte owner, double now, bool pellet)
    {
        if (Pool.Capacity == 0 || !ReferenceEquals(map, World.Map))
            return false;
        var d = to - from;
        var len = d.Length();
        if (len < 0.05f || len > World.Range - 0.5f)
            return false; // a miss into the sky ends at the range limit
        d /= len;
        Tracers++;
        const float back = 0.3f;
        if (!World.Query.Raycast(to - d * back, d, back * 2f, QueryFilter.Shots, out var hit) || MathF.Abs(hit.Distance - back) > 0.08f)
        {
            NoSurface++; // ended on a player, a zombie, or in the air
            return false;
        }
        return Place(map, hit, d, owner, now, pellet);
    }

    /// <summary>Puts an impact decal where a ray hit (also used by the smoke test).</summary>
    public static bool Place(Map map, in RayHit hit, Vector3 dir, byte owner, double now, bool pellet)
    {
        string material;
        var solid = -1;
        Box box = default;
        switch (hit.Kind)
        {
            case ShapeKind.Box:
                solid = hit.Shape.Index;
                if (!map.IsDecalSurface(solid))
                    return false;
                box = map.Solid[solid];
                var mats = map.Materials;
                material = solid < mats.Length ? MaterialNames[(int)mats[solid]] : "Default";
                break;
            case ShapeKind.Terrain:
                material = "Terrain";
                break;
            case ShapeKind.Mesh or ShapeKind.Obb:
                material = "Default";
                break;
            default:
                return false; // props are models (their collider is only a box), movers move, barriers are invisible
        }
        var defs = Decals.ImpactsFor(material);
        if (defs.Length == 0)
            return false;

        // Deterministic choices from the hit position (millimetres), the same on every client.
        var h = Hash(hit.Point);
        var total = 0f;
        foreach (var def in defs)
            total += def.ImpactWeight;
        var pick = (h & 0xFFFF) / 65536f * total;
        var chosen = defs[^1];
        foreach (var def in defs)
        {
            pick -= def.ImpactWeight;
            if (pick < 0)
            {
                chosen = def;
                break;
            }
        }
        var r1 = ((h >> 16) & 0xFF) / 255f;
        var r2 = ((h >> 24) & 0xFF) / 255f;
        var r3 = ((h >> 8) & 0xFF) / 255f;
        var size = MathF.Max(chosen.Size.X, chosen.Size.Y) * (1f + (r1 * 2f - 1f) * chosen.SizeVariation);
        var rotation = float.IsNaN(chosen.Rotation) ? r2 * MathF.Tau : chosen.Rotation * MathF.PI / 180f;
        var shade = 1f + (r3 * 2f - 1f) * chosen.TintVariation;

        var pos = hit.Point;
        var normal = hit.Normal;
        if (solid >= 0 && !FitOnFace(box, ref pos, normal, ref size))
            return false; // a face too small for a mark (a thin rail's edge)
        _ = dir;
        Pool.Add(map, pos, normal, size, rotation, chosen.Index, owner, solid, now, pellet, shade);
        Placed++;
        return true;
    }

    /// <summary>
    /// Keeps a square mark of <paramref name="size"/> inside the box face it hit: slides it inwards by up to its half size,
    /// shrinks it to a narrow face, and refuses a face much narrower than the mark. Boxes are axis-aligned.
    /// </summary>
    public static bool FitOnFace(in Box box, ref Vector3 pos, Vector3 normal, ref float size)
    {
        var axis = MathF.Abs(normal.X) > 0.5f ? 0 : MathF.Abs(normal.Y) > 0.5f ? 1 : 2;
        var min = box.Min;
        var max = box.Max;
        for (var a = 0; a < 3; a++)
        {
            if (a == axis)
                continue;
            var extent = max[a] - min[a];
            if (extent < size * 0.45f)
                return false;
            if (extent < size)
                size = extent;
        }
        var half = size * 0.5f;
        for (var a = 0; a < 3; a++)
        {
            if (a == axis)
                continue;
            var v = Math.Clamp(pos[a], min[a] + half, max[a] - half);
            pos = a == 0 ? pos with { X = v } : a == 1 ? pos with { Y = v } : pos with { Z = v };
        }
        return true;
    }

    /// <summary>A box changed (World.SolidChanged): impacts on it fade out.</summary>
    public static void SolidChanged(Map map, int index) => Pool.OnSolidChanged(map, index);

    private static uint Hash(Vector3 p)
    {
        var x = (uint)(int)MathF.Round(p.X * 1000f);
        var y = (uint)(int)MathF.Round(p.Y * 1000f);
        var z = (uint)(int)MathF.Round(p.Z * 1000f);
        var h = x * 73856093u ^ y * 19349663u ^ z * 83492791u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        h *= 3266489917u;
        h ^= h >> 16;
        return h;
    }
}
