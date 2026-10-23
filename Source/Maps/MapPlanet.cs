using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Planet maps: point gravity (the top-level <c>"gravity"</c> field, see <see cref="PointGravity"/>) and the ops that build
/// a small world you walk all the way round: <see cref="Planet"/> (the ball itself, solid) and the Surface* ops, which
/// place things by latitude and longitude standing on the surface (their "up" is away from the centre).
/// <para>
/// Unlike the plain <see cref="Mesh"/> op, surface meshes are <b>solid</b> by default: their exact triangles go into the
/// physics world (<see cref="SolidMeshes"/>), so players stand on them, bullets hit them and bots see round them. Solid
/// meshes count towards the fingerprint (<see cref="SolidMeshHash"/>; maps without any keep their old one).
/// </para>
/// Latitude 90 is the +Y pole ("north"), -90 the -Y pole; longitude 0 faces +Z from the centre and 90 faces +X.
/// </summary>
public sealed partial class Map
{
    /// <summary>The map's gravity field (null: the usual straight-down gravity): a planet's point field, or gravity zones.</summary>
    public IGravityField? Gravity;

    /// <summary>The map's point gravity when the whole map is a planet (null otherwise, zones included: their rocks are local).</summary>
    public PointGravity? PointGravity => Gravity as PointGravity;

    /// <summary>The map's gravity zones (GravityZones.cs), or null.</summary>
    public ZonedGravity? GravityZones => Gravity as ZonedGravity;

    /// <summary>The planet's centre and radius (from the <see cref="Planet"/> op; radius 0 = none).</summary>
    public Vector3 PlanetCenter;

    public float PlanetRadius;

    /// <summary>Indices into <see cref="MeshPlacements"/> of the meshes that collide (the planet and solid surface meshes).</summary>
    public readonly List<int> SolidMeshes = new();

    private List<IMeshCollider>? _solidColliders;

    /// <summary>The solid meshes' colliders (built once, on first use, in world space).</summary>
    public IReadOnlyList<IMeshCollider> SolidColliders
    {
        get
        {
            if (_solidColliders != null && _solidColliders.Count == SolidMeshes.Count)
                return _solidColliders;
            var list = new List<IMeshCollider>(SolidMeshes.Count);
            foreach (var i in SolidMeshes)
                list.Add(TriangleMeshCollider.FromMesh(MeshPlacements[i].BuildCollision(), Matrix4x4.Identity));
            return _solidColliders = list;
        }
    }

    private int _minGravityPercent = -1;

    /// <summary>
    /// The lowest gravity setting (percent) this map plays at (<see cref="World.ApplyGravitySetting"/>): 25 on most maps; on a
    /// planet, enough pull that a sprinting player stays on the ground with room to spare (2 x sprint speed^2 / radius, so a
    /// sprint jump still comes down within a few seconds), rounded up to 5%. Pocket Planet (r 56 m, 11 m/s2): 60%. Pure map
    /// data: every PC agrees.
    /// </summary>
    public int MinGravityPercent
    {
        get
        {
            if (_minGravityPercent >= 0)
                return _minGravityPercent;
            var min = Game.MinGravityPercent;
            if (PointGravity is { } pg && PlanetRadius > 0)
            {
                var need = 2f * World.SprintSpeed * World.SprintSpeed / PlanetRadius;
                min = Math.Clamp((int)MathF.Ceiling(100f * need / pg.Strength / 5f) * 5, Game.MinGravityPercent, 100);
            }
            return _minGravityPercent = min;
        }
    }

    /// <summary>Is up exactly world +Y at a point of this map (outside every gravity zone with an up of its own)?</summary>
    public bool PlainUpAtPoint(Vector3 p) => Gravity == null || Gravity.UpAt(p) == Vector3.UnitY;

    /// <summary>Does this map's gravity pull anywhere but straight down (or with a strength of its own)?</summary>
    public bool CurvedGravity => Gravity != null;

    /// <summary>Fingerprint of the gravity field and the solid meshes (0 for ordinary maps, which keep their fingerprint).</summary>
    public uint PlanetHash
    {
        get
        {
            if (Gravity == null && SolidMeshes.Count == 0)
                return 0;
            var h = 2166136261u;
            void Mix(float f)
            {
                var bits = BitConverter.SingleToUInt32Bits(MathF.Round(f * 1000f) / 1000f);
                for (var i = 0; i < 4; i++)
                {
                    h ^= (bits >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }
            }
            Gravity?.Mix(Mix);
            foreach (var i in SolidMeshes)
            {
                var p = MeshPlacements[i];
                var m = p.Transform;
                Mix(m.M11); Mix(m.M12); Mix(m.M13); Mix(m.M21); Mix(m.M22); Mix(m.M23);
                Mix(m.M31); Mix(m.M32); Mix(m.M33); Mix(m.M41); Mix(m.M42); Mix(m.M43);
                foreach (var c in p.Source)
                    Mix(c);
                var b = p.Mesh?.Bounds;
                if (b is { } bb)
                {
                    Mix(bb.Min.X); Mix(bb.Min.Y); Mix(bb.Min.Z); Mix(bb.Max.X); Mix(bb.Max.Y); Mix(bb.Max.Z);
                }
            }
            return h == 0 ? 1 : h;
        }
    }

    /// <summary>How far above a sphere of <paramref name="radius"/> round the centre the highest solid mesh reaches, plus a metre.</summary>
    public float MaxHeightAbovePlanet(float radius)
    {
        var top = 2f;
        var c = PointGravity?.Center ?? PlanetCenter;
        foreach (var i in SolidMeshes)
        {
            var b = MeshPlacements[i].Bounds;
            if (b.Size.Length() > radius) // the planet itself
                continue;
            top = MathF.Max(top, Vector3.Distance(b.Center, c) + b.Size.Length() * 0.5f - radius);
        }
        return MathF.Min(top + 1f, 60f);
    }

    /// <summary>The direction from the planet's centre for a latitude and longitude (degrees).</summary>
    public static Vector3 SurfaceDir(float lat, float lon)
    {
        const float deg = MathF.PI / 180f;
        var (sa, ca) = MathF.SinCos(lat * deg);
        var (so, co) = MathF.SinCos(lon * deg);
        return new Vector3(ca * so, sa, ca * co);
    }

    /// <summary>A point <paramref name="above"/> metres over the planet's surface at a latitude and longitude.</summary>
    public Vector3 SurfacePoint(float lat, float lon, float above = 0)
    {
        RequirePlanet();
        return PlanetCenter + SurfaceDir(lat, lon) * (PlanetRadius + above);
    }

    private void RequirePlanet()
    {
        if (PlanetRadius <= 0)
            throw new InvalidOperationException("place the Planet op first");
    }

    /// <summary>
    /// The geographic frame at a point on (or over) the planet: up away from the centre, north towards latitude 90 (the +Y
    /// pole) and right = east. Surface ops' headings are measured from this north. At the poles themselves north is -Z.
    /// </summary>
    public void GeoFrame(Vector3 at, out Vector3 right, out Vector3 up, out Vector3 north)
    {
        var d = at - PlanetCenter;
        up = d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : Vector3.UnitY;
        var n = Vector3.UnitY - up * up.Y;
        if (n.LengthSquared() < 1e-8f)
            n = -Vector3.UnitZ - up * Vector3.Dot(-Vector3.UnitZ, up);
        north = Vector3.Normalize(n);
        right = Vector3.Cross(north, up);
    }

    /// <summary>
    /// The transform that stands a shape built upright (y up, -z north) on the surface at <paramref name="at"/>, turned by
    /// <paramref name="heading"/> degrees about the local up (0: its -z faces geographic north, <see cref="GeoFrame"/>).
    /// </summary>
    private Matrix4x4 SurfaceTransform(Vector3 at, float heading, float pitch, float roll, float scale)
    {
        if (scale is < 0.01f or > 100f || !float.IsFinite(scale))
            throw new InvalidOperationException("scale is 0.01 to 100");
        if (!float.IsFinite(at.X + at.Y + at.Z + heading + pitch + roll))
            throw new InvalidOperationException("position and angles must be numbers");
        const float deg = MathF.PI / 180f;
        GeoFrame(at, out var r, out var u, out var n);
        var basis = new Matrix4x4(r.X, r.Y, r.Z, 0, u.X, u.Y, u.Z, 0, -n.X, -n.Y, -n.Z, 0, 0, 0, 0, 1);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromYawPitchRoll(-heading * deg, pitch * deg, roll * deg) * basis * Matrix4x4.CreateTranslation(at);
    }

    private void AddSolidMesh(MeshPlacement p, bool solid)
    {
        AddMeshPlacement(p);
        if (solid)
            SolidMeshes.Add(MeshPlacements.Count - 1);
    }

    /// <summary>
    /// The planet: a solid geodesic ball of <paramref name="radius"/> metres centred on (x, y, z), <paramref name="detail"/>
    /// subdivisions (5: about 20,000 triangles, 2 m across each at 55 m), textured with <paramref name="material"/>.
    /// Give the map a <c>"gravity": { "type": "point" }</c> with the same centre so things fall onto it.
    /// </summary>
    [MapOp]
    public void Planet(float radius, Vector3 color, float x = 0, float y = 0, float z = 0, int detail = 5, string material = "Dirt")
    {
        using var __ = Op([radius, color, x, y, z, detail, material]);
        if (radius is < 5f or > 400f || !float.IsFinite(radius))
            throw new InvalidOperationException("radius is 5 to 400 m");
        if (PlanetRadius > 0)
            throw new InvalidOperationException("a map has one Planet");
        PlanetCenter = new Vector3(x, y, z);
        PlanetRadius = radius;
        var spec = new PrimitiveSpec(PrimitiveKind.IcoSphere, radius * 2, radius * 2, radius * 2, Detail: Math.Clamp(detail, 0, 5));
        var asset = MeshLibrary.Primitive(spec);
        var at = Matrix4x4.CreateTranslation(PlanetCenter - new Vector3(0, radius, 0)); // primitives stand on their lowest point
        AddSolidMesh(new MeshPlacement("planet", asset, null, at, new Vector4(color, 1), ParseMeshMaterial(material)), true);
    }

    /// <summary>
    /// A procedural mesh standing on the planet at latitude/longitude (degrees), raised by <paramref name="y"/> (negative sinks
    /// it in), turned by <paramref name="heading"/> about the local up, then tipped by pitch and roll like the Mesh op.
    /// Solid unless <paramref name="solid"/> is false.
    /// </summary>
    [MapOp]
    public void SurfaceMesh(string mesh, float lat, float lon, Vector3 color, float y = 0, float width = 1, float height = 1, float depth = 1, float radius = 0,
        float inner = 0, int segments = 0, int detail = 0, float heading = 0, float pitch = 0, float roll = 0, float scale = 1, string material = "", bool solid = true)
    {
        using var __ = Op([mesh, lat, lon, color, y, width, height, depth, radius, inner, segments, detail, heading, pitch, roll, scale, material, solid]);
        var spec = new PrimitiveSpec(PrimitiveSpec.ParseKind(mesh), width, height, depth, radius, inner, segments, detail);
        PlaceSurface(mesh, spec, SurfacePoint(lat, lon, y), color, heading, pitch, roll, scale, material, solid);
    }

    private void PlaceSurface(string source, PrimitiveSpec spec, Vector3 at, Vector3 color, float heading, float pitch, float roll, float scale, string material, bool solid)
    {
        var asset = MeshLibrary.Primitive(spec);
        AddSolidMesh(new MeshPlacement(source, asset, null, SurfaceTransform(at, heading, pitch, roll, scale), new Vector4(color, 1), ParseMeshMaterial(material)), solid);
    }

    /// <summary>
    /// A solid box w x h x d standing on the surface (sunk <paramref name="sink"/> metres so its flat bottom never floats
    /// over the curve), turned by heading. Walls, cover, steps and platforms.
    /// </summary>
    [MapOp]
    public void SurfaceBlock(float lat, float lon, float w, float h, float d, Vector3 color, float y = 0, float heading = 0, float sink = 0.3f, string material = "")
    {
        using var __ = Op([lat, lon, w, h, d, color, y, heading, sink, material]);
        var spec = new PrimitiveSpec(PrimitiveKind.Box, w, h + sink, d);
        PlaceSurface("block", spec, SurfacePoint(lat, lon, y - sink), color, heading, 0, 0, 1, material, true);
    }

    /// <summary>
    /// A crater: a ring of <paramref name="count"/> rock slabs round (lat, lon), <paramref name="radius"/> metres out, each
    /// standing on the surface where it is and leaning outwards, up to <paramref name="rim"/> metres tall.
    /// </summary>
    [MapOp]
    public void Crater(float lat, float lon, float radius, Vector3 color, float rim = 1.4f, int count = 0, int seed = 1, string material = "Dirt")
    {
        using var __ = Op([lat, lon, radius, color, rim, count, seed, material]);
        RequirePlanet();
        if (radius is < 1f or > 60f)
            throw new InvalidOperationException("radius is 1 to 60 m");
        var n = count > 0 ? Math.Clamp(count, 3, 80) : Math.Clamp((int)MathF.Round(MathF.Tau * radius / 2.2f), 6, 60);
        var rng = new Random(seed);
        var centre = SurfaceDir(lat, lon);
        GeoFrame(PlanetCenter + centre * PlanetRadius, out var r0, out _, out var n0);
        var seg = MathF.Tau * radius / n;
        for (var i = 0; i < n; i++)
        {
            var a = i * MathF.Tau / n;
            var tangent = r0 * MathF.Cos(a) + n0 * MathF.Sin(a); // outwards from the centre, across the ground
            var ang = radius / PlanetRadius; // along the surface
            var dir = Vector3.Normalize(centre * MathF.Cos(ang) + tangent * MathF.Sin(ang));
            var at = PlanetCenter + dir * (PlanetRadius - 0.3f);
            var hgt = rim * (0.6f + 0.5f * (float)rng.NextDouble());
            var w = seg * (1.05f + 0.25f * (float)rng.NextDouble());
            var d = 1.2f + 0.8f * (float)rng.NextDouble();
            // Heading: the slab's long side runs round the ring (across the outward direction).
            GeoFrame(at, out var r, out _, out var north);
            var outward = Vector3.Normalize(tangent - dir * Vector3.Dot(tangent, dir));
            var heading = MathF.Atan2(Vector3.Dot(outward, r), Vector3.Dot(outward, north)) * 180f / MathF.PI;
            var shade = color * (0.85f + 0.25f * (float)rng.NextDouble());
            var spec = new PrimitiveSpec(PrimitiveKind.Box, w, hgt + 0.3f, d);
            PlaceSurface("crater rim", spec, at, shade, heading, -12f - 10f * (float)rng.NextDouble(), (float)(rng.NextDouble() - 0.5) * 8f, 1, material, true);
        }
    }

    /// <summary>
    /// <paramref name="count"/> shapes scattered over the planet between two latitudes (rocks, boulders), each a random size
    /// from <paramref name="minSize"/> to <paramref name="maxSize"/> (metres across), keeping <paramref name="clear"/> metres
    /// from every spawn. Deterministic for a seed.
    /// </summary>
    [MapOp]
    public void SurfaceScatter(string mesh, int count, Vector3 color, float minSize = 0.8f, float maxSize = 2.5f, int seed = 1, float latMin = -90, float latMax = 90,
        float clear = 4, string material = "", bool solid = true)
    {
        using var __ = Op([mesh, count, color, minSize, maxSize, seed, latMin, latMax, clear, material, solid]);
        RequirePlanet();
        var kind = PrimitiveSpec.ParseKind(mesh);
        var rng = new Random(seed);
        var placed = 0;
        for (var tries = 0; placed < Math.Clamp(count, 0, 2000) && tries < count * 20; tries++)
        {
            // Uniform over the band: sin(latitude) uniform.
            var s0 = MathF.Sin(latMin * MathF.PI / 180f);
            var s1 = MathF.Sin(latMax * MathF.PI / 180f);
            var lat = MathF.Asin(s0 + (s1 - s0) * (float)rng.NextDouble()) * 180f / MathF.PI;
            var lon = (float)rng.NextDouble() * 360f - 180f;
            var size = minSize + (maxSize - minSize) * (float)rng.NextDouble();
            var at = SurfacePoint(lat, lon, -size * 0.25f);
            if (Spawns.Any(sp => Vector3.Distance(sp, at) < clear + size) || Waypoints.Any(wp => Vector3.Distance(wp, at) < size * 0.6f + 0.6f))
                continue;
            var spec = new PrimitiveSpec(kind, size, size * (0.5f + 0.4f * (float)rng.NextDouble()), size * (0.7f + 0.5f * (float)rng.NextDouble()), Detail: kind == PrimitiveKind.IcoSphere ? 1 : 0);
            var shade = color * (0.8f + 0.35f * (float)rng.NextDouble());
            PlaceSurface(mesh, spec, at, shade, (float)rng.NextDouble() * 360f, (float)(rng.NextDouble() - 0.5) * 20f, (float)(rng.NextDouble() - 0.5) * 20f, 1, material, solid);
            placed++;
        }
    }

    /// <summary>A spawn point on the surface at latitude/longitude (a hair above the ground).</summary>
    [MapOp]
    public void SurfaceSpawn(float lat, float lon)
    {
        using var __ = Op([lat, lon]);
        Spawns.Add(SurfacePoint(lat, lon, 0.05f));
        Waypoints.Add(SurfacePoint(lat, lon, 0.05f));
    }

    /// <summary>A waypoint (where bots roam) on the surface.</summary>
    [MapOp]
    public void SurfaceWaypoint(float lat, float lon)
    {
        using var __ = Op([lat, lon]);
        Waypoints.Add(SurfacePoint(lat, lon, 0.05f));
    }

    /// <summary>
    /// Waypoints spread evenly over the whole planet about <paramref name="spacing"/> metres apart (a Fibonacci sphere).
    /// Ones that end up inside something solid are dropped by the bots' graph.
    /// </summary>
    [MapOp]
    public void PlanetWaypoints(float spacing = 8)
    {
        using var __ = Op([spacing]);
        RequirePlanet();
        foreach (var d in FibonacciSphere(PlanetRadius, spacing))
            Waypoints.Add(PlanetCenter + d * (PlanetRadius + 0.05f));
    }

    /// <summary>Unit directions spread evenly over a sphere of <paramref name="radius"/>, about <paramref name="spacing"/> apart.</summary>
    public static IEnumerable<Vector3> FibonacciSphere(float radius, float spacing)
    {
        spacing = MathF.Max(spacing, 1f);
        var count = Math.Clamp((int)(4 * MathF.PI * radius * radius / (spacing * spacing)), 4, 20000);
        var golden = MathF.PI * (3f - MathF.Sqrt(5f));
        for (var i = 0; i < count; i++)
        {
            var y = 1f - 2f * (i + 0.5f) / count;
            var r = MathF.Sqrt(MathF.Max(0, 1 - y * y));
            var (s, c) = MathF.SinCos(golden * i);
            yield return new Vector3(c * r, y, s * r);
        }
    }
}
