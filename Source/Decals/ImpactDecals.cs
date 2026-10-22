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
    /// <param name="scale">Mark size for the calibre (<see cref="CalibreScale"/>; 1: as drawn).</param>
    public static bool OnTracer(Map map, Vector3 from, Vector3 to, byte owner, double now, bool pellet, float scale = 1f)
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
        return Place(map, hit, d, owner, now, pellet, scale);
    }

    /// <summary>Puts an impact decal where a ray hit (also used by the smoke test). <paramref name="material"/>: an impact
    /// material name to use instead of the surface's own (the explosive scorch).</summary>
    public static bool Place(Map map, in RayHit hit, Vector3 dir, byte owner, double now, bool pellet, float scale = 1f, string? material = null)
    {
        var solid = -1;
        Box box = default;
        switch (hit.Kind)
        {
            case ShapeKind.Box:
                solid = hit.Shape.Index;
                if (!map.IsDecalSurface(solid))
                    return false;
                box = map.Solid[solid];
                break;
            case ShapeKind.Terrain or ShapeKind.Mesh or ShapeKind.Obb:
                break;
            default:
                return false; // props are models (their collider is only a box), movers move, barriers are invisible
        }
        material ??= SurfaceOf(map, hit);
        var defs = Decals.ImpactsFor(material);
        if (defs.Length == 0)
            return false;

        // Deterministic choices from the hit position (millimetres), the same on every client.
        var h = Hash(hit.Point);
        var chosen = PickVariant(defs, h);
        var r1 = ((h >> 16) & 0xFF) / 255f;
        var r2 = ((h >> 24) & 0xFF) / 255f;
        var r3 = ((h * 0x9E3779B1u) >> 24) / 255f; // remixed: the low 16 bits already picked the variant
        var size = MathF.Min(MathF.Max(chosen.Size.X, chosen.Size.Y) * (1f + (r1 * 2f - 1f) * chosen.SizeVariation) * scale, MaxSize);
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

    /// <summary>The biggest an impact mark gets (metres), whatever its calibre.</summary>
    public const float MaxSize = 1.2f;

    /// <summary>
    /// How big a weapon's marks are drawn (from its data, so the same on every client): shotgun pellets small (they merge
    /// under pressure), single shots from 0.7 (light pistols) up to 1.35 (heavy rifles, slugs) by <see cref="WeaponDef.Caliber"/>.
    /// </summary>
    public static float CalibreScale(WeaponDef def) => def.Pellets > 1 ? 0.8f : 0.7f + 0.65f * Math.Clamp(def.Caliber, 0f, 1f);

    /// <summary>
    /// A grenade, launcher round or strike went off at <paramref name="pos"/> (every client gets the same point from the
    /// server): a scorch ("Explosive" impact decals) on the nearest surface under or beside it, within a metre and a half.
    /// Allocation-free. Returns true if a scorch was placed.
    /// </summary>
    public static bool OnBlast(Map map, Vector3 pos, byte owner, double now)
    {
        if (Pool.Capacity == 0 || !ReferenceEquals(map, World.Map) || Decals.ImpactsFor("Explosive").Length == 0)
            return false;
        var origin = pos + new Vector3(0, 0.3f, 0);
        RayHit best = default;
        var found = false;
        var bestDir = -Vector3.UnitY;
        for (var i = 0; i < BlastRays.Length; i++)
        {
            var d = BlastRays[i];
            if (World.Query.Raycast(origin, d, 1.8f, QueryFilter.Shots, out var hit) && (!found || hit.Distance < best.Distance - 0.05f))
            {
                best = hit;
                bestDir = d;
                found = true;
            }
        }
        return found && Place(map, best, bestDir, owner, now, false, 1f, "Explosive");
    }

    // Down first (most blasts are on the ground), then sideways (an impact round against a wall), then up (a ceiling).
    private static readonly Vector3[] BlastRays = [-Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY];

    /// <summary>
    /// The impact material of the surface a ray hit (see Decals/README.md, "Bullet impacts"): a box's texture (a building
    /// part's own texture, a door's material), refined by its colour and facing (dark concrete floors are asphalt, pale
    /// concrete walls plaster, earthy boxes grass, sand or snow); the terrain's surface where it was hit (grass, dirt, sand,
    /// rock, snow); "Default" for anything else. Only map data goes in, so every client gets the same answer. No allocation.
    /// </summary>
    public static string SurfaceOf(Map map, in RayHit hit)
    {
        switch (hit.Kind)
        {
            case ShapeKind.Box:
            {
                var i = hit.Shape.Index;
                if ((uint)i >= (uint)map.Solid.Count)
                    return "Default";
                var mats = map.Materials;
                var m = i < mats.Length ? mats[i] : Material.Concrete;
                if (map.BuildingLooks.TryGetValue(i, out var look))
                {
                    if (look.Layer >= 0 && look.Layer < (int)Material.Count)
                        m = (Material)look.Layer;
                    else if (map.IsBuildingDoorBox(i))
                        foreach (var door in map.BuildingDoors)
                            if (door.SolidIndex == i)
                            {
                                m = door.Plan.Material == DoorMaterial.Metal ? Material.Metal : Material.Wood;
                                break;
                            }
                }
                return Refine(m, i < map.Colors.Count ? map.Colors[i] : new Vector3(0.5f), hit.Normal, i == map.GroundIndex);
            }
            case ShapeKind.Terrain:
                if (map.Terrain is not { } t)
                    return "Dirt";
                return SurfaceProbe.OfTerrain(t, hit.Point.X, hit.Point.Z) switch
                {
                    FootSurface.Rock => "Rock",
                    FootSurface.Dirt => "Dirt",
                    FootSurface.Sand => "Sand",
                    FootSurface.Snow => "Snow",
                    _ => "Grass",
                };
            default:
                return "Default";
        }
    }

    /// <summary>A box texture refined by the box's colour and the face hit (see <see cref="SurfaceOf"/>).</summary>
    public static string Refine(Material m, Vector3 c, Vector3 normal, bool ground)
    {
        var luma = c.X * 0.3f + c.Y * 0.59f + c.Z * 0.11f;
        var spread = MathF.Max(c.X, MathF.Max(c.Y, c.Z)) - MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        var floor = normal.Y > 0.7f;
        var wall = MathF.Abs(normal.Y) < 0.5f;
        switch (m)
        {
            case Material.Concrete:
                if (floor && spread < 0.08f && (luma < 0.34f || ground && luma < 0.45f))
                    return "Asphalt"; // dark grey ground: roads, car parks, yards
                if (wall && luma > 0.68f)
                    return "Plaster"; // pale walls: rendered, painted, drywall
                return "Concrete";
            case Material.Dirt:
                if (SurfaceProbe.IsSnowy(c))
                    return "Snow";
                if (c.Y > c.X + 0.04f && c.Y > c.Z + 0.04f)
                    return "Grass";
                if (c.X > c.Z + 0.12f && c.Y > c.Z + 0.04f && c.X > 0.55f)
                    return "Sand";
                return "Dirt";
            default:
                return MaterialNames[(int)m];
        }
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

    /// <summary>
    /// The variant a hit gets: one of <paramref name="defs"/> (non-empty, in library order, so the same on every client)
    /// weighted by <see cref="DecalDef.ImpactWeight"/>, from the low 16 bits of the hit's <see cref="Hash"/>. No allocation.
    /// </summary>
    public static DecalDef PickVariant(DecalDef[] defs, uint h)
    {
        var total = 0f;
        foreach (var def in defs)
            total += def.ImpactWeight;
        var pick = (h & 0xFFFF) / 65536f * total;
        foreach (var def in defs)
        {
            pick -= def.ImpactWeight;
            if (pick < 0)
                return def;
        }
        return defs[^1];
    }

    /// <summary>A hit position (to the millimetre) hashed: the seed of every per-hit choice.</summary>
    public static uint Hash(Vector3 p)
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
