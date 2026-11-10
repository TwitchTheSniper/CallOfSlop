using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A planetoid (docs/planets/PLANETS.md): a whole world kilometres across, with ground all the way round and gravity
/// towards its centre. Its surface is a <b>cube-sphere</b>: six faces of a cube, each a grid of <see cref="N"/> x
/// <see cref="N"/> cells, pushed out onto a sphere (equiangular, so the cells are nearly square everywhere) and raised
/// along "up" (away from the centre) by the map's terrain features. Nothing is built whole: a vertex's height is a
/// function of its direction alone (<see cref="HeightAt"/>), so any part of the world can be worked out on its own, the
/// same on every peer, and the vertices on a face's edge are bit for bit the neighbouring face's.
/// <para>
/// Collision: two triangles per cell (split along the cell's (0,0)-(1,1) diagonal, as the drawing splits them), handed to
/// the capsule mover and shape casts by <see cref="Gather"/>; rays march along the ray and settle on the triangles
/// (<see cref="Raycast"/>). Vertices are cached in small tiles (<see cref="Tile"/>) made on first use and dropped when
/// unused. Drawing: <see cref="PlanetRenderer"/> (CDLOD per face).
/// </para>
/// Directions use the world's axes: the faces are +X, -X, +Y, -Y, +Z, -Z in that order.
/// </summary>
public sealed class Planetoid
{
    /// <summary>The centre (world metres).</summary>
    public double Cx, Cy, Cz;

    /// <summary>The radius of the ground at height 0 (metres).</summary>
    public double Radius;

    /// <summary>Cells along a face's side (a power of two, at least 64).</summary>
    public int N;

    /// <summary>Height of the ground before any feature (metres).</summary>
    public double Base;

    /// <summary>Gravity at the surface (m/s2).</summary>
    public float Strength;

    /// <summary>Colours of the ground (grass, dirt, rock, sand, snow, slope rules): as a flat terrain's.</summary>
    public TerrainSurface Surface = new();

    /// <summary>The terrain features (noise, mountain, flatten, crater), in order.</summary>
    public List<TerrainFeature> Features = new();

    /// <summary>Lowest and highest the ground can be (metres above <see cref="Radius"/>), worked out from the features' own limits.</summary>
    public float MinHeight, MaxHeight;

    // ---- A hollow planetoid (PLANETS.md "Hollow planetoids"): a crust round a glowing cavity ----------------------------

    /// <summary>How thick the crust is under the base height (metres; 0: solid all the way down).</summary>
    public double Crust;

    /// <summary>The cavity's light: its walls' glowing seams and the core's colour.</summary>
    public Vector3 Glow = new(1f, 0.55f, 0.2f);

    /// <summary>The radius of the glowing core at the centre (metres; 0: none). A light only: nothing touches it.</summary>
    public float CoreRadius;

    /// <summary>Is there a cavity inside?</summary>
    public bool Hollow => Crust > 0;

    /// <summary>The cavity's roof as an altitude (metres above <see cref="Radius"/>): everything below it is air. -infinity when solid.</summary>
    public float Inner => Crust > 0 ? (float)(Base - Crust) : float.NegativeInfinity;

    /// <summary>The cavity's radius (0 when solid).</summary>
    public double InnerRadius => Crust > 0 ? Radius + Base - Crust : 0;

    /// <summary>How far inside the cavity the pull turns round under a hole dug through the crust (metres).</summary>
    public const float HoleLip = 2.1f;

    /// <summary>
    /// Where the pull turns round along a point's line from the centre (<paramref name="rel"/>, <paramref name="d"/> long):
    /// the cavity's wall, or <see cref="HoleLip"/> inside it under a column dug right through. Someone who falls through
    /// a hole comes to rest just inside the cavity, clear of the hole's rim whichever way they stand, and walks off over the
    /// roof round it, dropping onto it; turned at the wall itself they would hang level with the rim and catch on it.
    /// </summary>
    public float TurnRadius(Vector3 rel, float d)
    {
        var inner = (float)InnerRadius;
        if (d > inner - HoleLip && d < inner + HoleLip && _edits is { OpenToCavity: true } e && e.TryColumn(rel.X / d, rel.Y / d, rel.Z / d, out var spans) && spans[1] <= Inner)
            return inner - HoleLip;
        return inner;
    }

    public Vector3 Center => new((float)Cx, (float)Cy, (float)Cz);

    /// <summary>A cell's side along the ground at the middle of a face (metres): a quarter of the circumference over <see cref="N"/>.</summary>
    public double Cell => Radius * Math.PI / 2 / N;

    /// <summary>The level-of-detail levels: level 0 is <see cref="PageCells"/> cells across, the top one a whole face.</summary>
    public int Levels => (int)Math.Round(Math.Log2(N / (double)PageCells)) + 1;

    public const int PageCells = 64;

    /// <summary>Cells along a cached tile's side (its vertices: one more).</summary>
    public const int Tile = 32;

    private const int TileVerts = Tile + 1;

    /// <summary>Cached tiles kept before the least recently used are dropped (17 KB each).</summary>
    public const int Budget = 1024;

    public const double Quantum = TerrainGen.Quantum;

    /// <summary>Feature types and their keys (lat / lon in degrees; lat 90 is the +Y pole, lon 0 faces +Z, 90 faces +X).</summary>
    private static readonly Dictionary<string, (string[] Keys, string[] Required)> Schema = new(StringComparer.OrdinalIgnoreCase)
    {
        ["noise"] = (["amplitude", "scale", "octaves", "persistence", "lacunarity", "seed", "ridged", "lat", "lon", "radius", "edge"], ["amplitude", "scale"]),
        ["mountain"] = (["lat", "lon", "radius", "height", "seed", "ridges", "erosion", "warp"], ["lat", "lon", "radius", "height"]),
        ["flatten"] = (["lat", "lon", "radius", "height", "edge"], ["lat", "lon", "radius"]),
        ["crater"] = (["lat", "lon", "radius", "depth", "rim"], ["lat", "lon", "radius", "depth"]),
        ["rift"] = (["lat", "lon", "bearing", "length", "width", "depth", "floor", "wander", "seed"], ["lat", "lon", "length", "width", "depth"]),
        ["terrace"] = (["step", "sharp", "lat", "lon", "radius", "edge"], ["step"]),
    };

    public static IEnumerable<string> FeatureTypes => Schema.Keys;

    // ---- Faces ----------------------------------------------------------------------------------------------------

    // Each face: its outward normal and the axes its grid's i and j run along, chosen so v x u = n (the same handedness as
    // the flat terrain's x and z: the drawing's index list and the collision's winding are then the flat ones). Every axis
    // is a unit vector along a world axis, so a cube point's components are exactly 1, -1 or a warped coordinate.
    private static readonly (int N, int U, int V)[] FaceAxes =
    [
        // (axis index * 2 + (negative ? 1 : 0)) for normal, u, v: 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z
        (0, 2, 5), // +X: u +Y, v -Z
        (1, 2, 4), // -X: u +Y, v +Z
        (2, 5, 0), // +Y: u -Z, v +X
        (3, 4, 0), // -Y: u +Z, v +X
        (4, 2, 0), // +Z: u +Y, v +X
        (5, 2, 1), // -Z: u +Y, v -X
    ];

    private static void Axis(int a, out double x, out double y, out double z)
    {
        var s = (a & 1) == 0 ? 1.0 : -1.0;
        x = a >> 1 == 0 ? s : 0;
        y = a >> 1 == 1 ? s : 0;
        z = a >> 1 == 2 ? s : 0;
    }

    /// <summary>A face's normal and grid axes as vectors.</summary>
    public static void FaceBasis(int face, out Vector3 n, out Vector3 u, out Vector3 v)
    {
        var (fn, fu, fv) = FaceAxes[face];
        Axis(fn, out var x, out var y, out var z);
        n = new Vector3((float)x, (float)y, (float)z);
        Axis(fu, out x, out y, out z);
        u = new Vector3((float)x, (float)y, (float)z);
        Axis(fv, out x, out y, out z);
        v = new Vector3((float)x, (float)y, (float)z);
    }

    /// <summary>Equiangular warp of a face coordinate in -1..1 (exactly odd, and exactly 0 and +-1 at the middle and edges).</summary>
    public static double Warp(double a)
    {
        if (a == 0)
            return 0;
        var m = Math.Abs(a);
        var w = m == 1 ? 1 : Math.Tan(m * (Math.PI / 4));
        return a < 0 ? -w : w;
    }

    internal static double Unwarp(double w) => Math.Atan(w) * (4 / Math.PI);

    /// <summary>
    /// The unit direction of grid point (<paramref name="fi"/>, <paramref name="fj"/>) on <paramref name="face"/> (0..N
    /// along each side; outside that range it carries on over the cube face's plane, for the drawing's aprons).
    /// </summary>
    public void DirOf(int face, double fi, double fj, out double x, out double y, out double z)
    {
        var wa = Warp(2 * fi / N - 1);
        var wb = Warp(2 * fj / N - 1);
        var (fn, fu, fv) = FaceAxes[face];
        double px = 0, py = 0, pz = 0;
        Add(fn, 1, ref px, ref py, ref pz);
        Add(fu, wa, ref px, ref py, ref pz);
        Add(fv, wb, ref px, ref py, ref pz);
        var len = Math.Sqrt(px * px + py * py + pz * pz);
        x = px / len;
        y = py / len;
        z = pz / len;

        static void Add(int a, double w, ref double px, ref double py, ref double pz)
        {
            var v = (a & 1) == 0 ? w : -w;
            switch (a >> 1)
            {
                case 0: px = v; break;
                case 1: py = v; break;
                default: pz = v; break;
            }
        }
    }

    /// <summary>Which face a direction is on, and where on its grid (continuous, 0..N).</summary>
    public void Locate(double x, double y, double z, out int face, out double fi, out double fj)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
        if (ax >= ay && ax >= az)
            face = x >= 0 ? 0 : 1;
        else if (ay >= az)
            face = y >= 0 ? 2 : 3;
        else
            face = z >= 0 ? 4 : 5;
        var (fn, fu, fv) = FaceAxes[face];
        var dn = Comp(fn, x, y, z);
        fi = (Unwarp(Comp(fu, x, y, z) / dn) + 1) * 0.5 * N;
        fj = (Unwarp(Comp(fv, x, y, z) / dn) + 1) * 0.5 * N;

        static double Comp(int a, double x, double y, double z)
        {
            var c = (a >> 1) switch { 0 => x, 1 => y, _ => z };
            return (a & 1) == 0 ? c : -c;
        }
    }

    // ---- Heights --------------------------------------------------------------------------------------------------

    private sealed class Placed
    {
        public required TerrainFeature F;
        public double Dx, Dy, Dz; // centre direction (features placed by lat / lon)
        public double Ex, Ey, Ez, Nx, Ny, Nz; // east and north there
        public double CosReach = -2; // dot(dir, centre) above this: inside the feature's reach
        public TerrainGen.Mountain? Mountain;
        public bool Local; // placed by lat / lon
        // The feature's numbers, read once (HeightAt runs for every vertex: a lookup by name there cost more than the noise):
        // noise: A amplitude, B scale, C persistence, D lacunarity, I octaves, J seed, Flag ridged, R radius, E edge (R 0: everywhere)
        // flatten: R radius, E edge, A height; crater: R radius, A depth, B rim
        // rift: A depth, B half length, C half width, D floor share, E wander, G wander's wavelength, Sin / Cos of the bearing, J seed
        // terrace: A step, B the riser's share of a step, R radius, E edge (R 0: everywhere)
        public double A, B, C, D, E, G, R, Sin, Cos;
        public int I, J;
        public bool Flag;
    }

    [NotBaked]
    private Placed[]? _placed;

    private Placed[] PlacedFeatures => _placed ??= Place();

    private Placed[] Place()
    {
        var list = new Placed[Features.Count];
        for (var k = 0; k < Features.Count; k++)
        {
            var f = Features[k];
            var p = new Placed { F = f, Local = f.Has("lat") };
            switch (f.Type)
            {
                case "noise":
                    p.A = f.Num("amplitude", 0); p.B = f.Num("scale", 100); p.C = f.Num("persistence", 0.5); p.D = f.Num("lacunarity", 2);
                    p.I = (int)f.Num("octaves", 4); p.J = (int)f.Num("seed", 1); p.Flag = f.Get("ridged") is true;
                    p.R = p.Local ? f.Num("radius", 1) : 0; p.E = Math.Max(f.Num("edge", p.R * 0.5), 0.01);
                    break;
                case "flatten":
                    p.R = f.Num("radius", 1); p.E = Math.Max(f.Num("edge", 20), 0.01); p.A = f.Num("height", 0);
                    break;
                case "crater":
                    p.R = f.Num("radius", 1); p.A = f.Num("depth", 1); p.B = f.Num("rim", p.A * 0.25);
                    break;
                case "rift":
                    p.A = f.Num("depth", 1); p.B = f.Num("length", 1) * 0.5; p.C = f.Num("width", 1) * 0.5;
                    p.D = Math.Clamp(f.Num("floor", 0.3), 0, 0.95); p.E = Math.Abs(f.Num("wander", 0)); p.G = Math.Max(p.C * 8, 1);
                    p.Sin = Math.Sin(f.Num("bearing", 0) * Math.PI / 180); p.Cos = Math.Cos(f.Num("bearing", 0) * Math.PI / 180);
                    p.J = (int)f.Num("seed", 1);
                    break;
                case "terrace":
                    p.A = f.Num("step", 1); p.B = 1 - Math.Clamp(f.Num("sharp", 0.8), 0, 0.98);
                    p.R = p.Local ? f.Num("radius", 1) : 0; p.E = Math.Max(f.Num("edge", p.R * 0.5), 0.01);
                    break;
            }
            if (p.Local)
            {
                double lat = f.Num("lat", 0) * Math.PI / 180, lon = f.Num("lon", 0) * Math.PI / 180;
                p.Dx = Math.Cos(lat) * Math.Sin(lon);
                p.Dy = Math.Sin(lat);
                p.Dz = Math.Cos(lat) * Math.Cos(lon);
                // East: up x north-pole axis (along increasing longitude); north = east x up... any fixed tangent frame works
                // (the feature's local x, z), as long as it's smooth over the feature: at a pole, pick -Z for north.
                double ex = p.Dz, ey = 0, ez = -p.Dx;
                var el = Math.Sqrt(ex * ex + ez * ez);
                if (el < 1e-9)
                {
                    ex = 1;
                    ez = 0;
                    el = 1;
                }
                p.Ex = ex / el; p.Ey = ey / el; p.Ez = ez / el;
                // north = up x east
                p.Nx = p.Dy * p.Ez - p.Dz * p.Ey;
                p.Ny = p.Dz * p.Ex - p.Dx * p.Ez;
                p.Nz = p.Dx * p.Ey - p.Dy * p.Ex;
                var reach = ReachOf(f);
                p.CosReach = Math.Cos(Math.Min(reach / Radius, Math.PI));
                if (f.Type == "mountain")
                    p.Mountain = new TerrainGen.Mountain(new TerrainFeature { Type = "mountain" }
                        .With("x", 0.0).With("z", 0.0).With("radius", f.Num("radius", 1)).With("height", f.Num("height", 0))
                        .With("seed", f.Num("seed", 1)).With("ridges", f.Num("ridges", 0.6)).With("erosion", f.Num("erosion", 0.6)).With("warp", f.Num("warp", 0.35)));
            }
            list[k] = p;
        }
        return list;
    }

    /// <summary>The ground's height (metres above <see cref="Radius"/>) in unit direction (x, y, z): the features in order, rounded to <see cref="Quantum"/>.</summary>
    public double HeightAt(double x, double y, double z)
    {
        var h = Base;
        foreach (var p in PlacedFeatures)
        {
            var f = p.F;
            if (!p.Local)
            {
                if (f.Type == "noise")
                    h += Noise(p, x, y, z);
                else if (f.Type == "terrace")
                    h = Terrace(p, h);
                continue;
            }
            var c = x * p.Dx + y * p.Dy + z * p.Dz;
            if (c <= p.CosReach)
                continue;
            // Local ground coordinates round the feature's centre (azimuthal equidistant: true distance along the
            // ground from the centre, true bearing), metres.
            double tx = x - c * p.Dx, ty = y - c * p.Dy, tz = z - c * p.Dz;
            var s = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            var ang = Math.Atan2(s, c);
            double lx = 0, lz = 0;
            if (s > 1e-12)
            {
                var k = ang * Radius / s;
                lx = (tx * p.Ex + ty * p.Ey + tz * p.Ez) * k;
                lz = -(tx * p.Nx + ty * p.Ny + tz * p.Nz) * k;
            }
            var d = ang * Radius;
            switch (f.Type)
            {
                case "noise": // (a patch of noise: dunes in one basin, rough ground on one side)
                    if (d < p.R + p.E)
                        h += Noise(p, x, y, z) * (d <= p.R ? 1 : 1 - TerrainGen.Smoothstep((d - p.R) / p.E));
                    break;
                case "terrace":
                    if (d < p.R + p.E)
                        h += (Terrace(p, h) - h) * (d <= p.R ? 1 : 1 - TerrainGen.Smoothstep((d - p.R) / p.E));
                    break;
                default:
                    h = Apply(p, h, lx, lz, d);
                    break;
            }
        }
        return Math.Round(h / Quantum) * Quantum;
    }

    private double Noise(Placed p, double x, double y, double z)
    {
        var n = Fbm3(x * Radius, y * Radius, z * Radius, p.B, p.I, p.C, p.D, p.J);
        if (p.Flag)
            n = 1 - 2 * Math.Abs(n);
        return p.A * n;
    }

    /// <summary>
    /// The height stepped into terraces <c>step</c> high: flat treads with a riser over the last <c>1 - sharp</c> of each
    /// step (mesas, benches, a layered crust). It never leaves the step the height was in.
    /// </summary>
    private static double Terrace(Placed p, double h)
    {
        var q = h / p.A;
        var floor = Math.Floor(q);
        var t = (q - floor - (1 - p.B)) / p.B;
        return (floor + (t <= 0 ? 0 : TerrainGen.Smoothstep(t))) * p.A;
    }

    private static double Apply(Placed p, double h, double lx, double lz, double d)
    {
        switch (p.F.Type)
        {
            case "mountain":
                return h + p.Mountain!.HeightAt(lx, lz);
            case "flatten":
            {
                var t = d <= p.R ? 1 : 1 - TerrainGen.Smoothstep((d - p.R) / p.E);
                return h + (p.A - h) * t;
            }
            case "crater":
            {
                var t = d / p.R;
                if (t < 1)
                    return h - p.A * (1 - t * t) + p.B * TerrainGen.Smoothstep(t);
                return t < 2 ? h + p.B * (1 - TerrainGen.Smoothstep(t - 1)) : h;
            }
            case "rift":
            {
                // A trench along a bearing (0: north, 90: east): a flat floor, sides rising to the ground either side, ends
                // that taper out over its width, and a line that wanders. A negative depth is a wall or a dyke.
                double north = -lz, along = lx * p.Sin + north * p.Cos, across = lx * p.Cos - north * p.Sin;
                var end = (p.B - Math.Abs(along)) / (p.C * 2);
                if (end <= 0)
                    return h;
                if (p.E > 0)
                    across += p.E * Noise3(along / p.G, 0.5, 0.5, p.J);
                var t = Math.Abs(across) / p.C;
                if (t >= 1)
                    return h;
                var side = t <= p.D ? 1 : 1 - TerrainGen.Smoothstep((t - p.D) / (1 - p.D));
                return h - p.A * side * (end >= 1 ? 1 : TerrainGen.Smoothstep(end));
            }
            default:
                return h;
        }
    }

    /// <summary>How far along the ground from its centre a feature placed by lat / lon can change the height (metres).</summary>
    private double ReachOf(TerrainFeature f) => f.Type switch
    {
        "mountain" => f.Num("radius", 1) * (1 + f.Num("warp", 0.35) * 0.35) + 10,
        "flatten" => f.Num("radius", 1) + f.Num("edge", 20) + 2,
        "crater" => f.Num("radius", 1) * 2 + 2,
        "rift" => Math.Sqrt(f.Num("length", 1) * f.Num("length", 1) * 0.25 + Math.Pow(f.Num("width", 1) * 0.5 + Math.Abs(f.Num("wander", 0)), 2)) + 2,
        "noise" or "terrace" => f.Num("radius", 1) + Math.Max(f.Num("edge", f.Num("radius", 1) * 0.5), 0.01) + 2,
        _ => Radius * 4,
    };

    /// <summary>The most a feature can lower and raise the ground it reaches, from its own limits.</summary>
    private static (double Down, double Up) Swing(TerrainFeature f)
    {
        switch (f.Type)
        {
            case "noise":
                var a = Math.Abs(f.Num("amplitude", 0));
                return (a, a);
            case "mountain":
                var mh = Math.Abs(f.Num("height", 0));
                var ridges = f.Num("ridges", 0.6);
                return (mh * (0.7 * ridges + 0.1), mh * (1 + 1.5 * ridges));
            case "crater":
                return (f.Num("depth", 0), Math.Abs(f.Num("rim", f.Num("depth", 0) * 0.25)));
            case "rift":
                return (Math.Max(f.Num("depth", 0), 0), Math.Max(-f.Num("depth", 0), 0));
            case "terrace": // (a height stays within its own step: a step either way covers it)
                var step = Math.Abs(f.Num("step", 0));
                return (step, step);
            default:
                return (0, 0);
        }
    }

    /// <summary>
    /// Lowest and highest the features can make the ground, from their own limits (not a sampling: always safe). Features
    /// that are everywhere add up. Features placed by lat / lon add up only where they can meet: the bound is the worst
    /// group of them that all reach one another (eight mountains spread round a world bound it as its worst cluster,
    /// not as eight stacked: the far plane, the drawing's culling and every ray's shell come from these two numbers).
    /// A flatten sets the ground to its height, give or take what comes after it.
    /// </summary>
    public void WorkOutBounds()
    {
        double everyDown = 0, everyUp = 0;
        var local = new List<(double X, double Y, double Z, double Reach, double Down, double Up)>();
        foreach (var f in Features)
        {
            if (f.Type == "flatten")
                continue;
            var (down, up) = Swing(f);
            if (!f.Has("lat"))
            {
                everyDown += down;
                everyUp += up;
                continue;
            }
            double lat = f.Num("lat", 0) * Math.PI / 180, lon = f.Num("lon", 0) * Math.PI / 180;
            local.Add((Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat), Math.Cos(lat) * Math.Cos(lon), ReachOf(f), down, up));
        }
        double localDown = 0, localUp = 0;
        foreach (var a in local)
        {
            double down = 0, up = 0;
            foreach (var b in local) // (a itself too: its angle to itself is 0)
                if (Math.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y + a.Z * b.Z, -1, 1)) * Radius < a.Reach + b.Reach)
                {
                    down += b.Down;
                    up += b.Up;
                }
            localDown = Math.Max(localDown, down);
            localUp = Math.Max(localUp, up);
        }
        double lo = Base - everyDown - localDown, hi = Base + everyUp + localUp;
        for (var k = 0; k < Features.Count; k++)
        {
            if (Features[k].Type != "flatten")
                continue;
            double afterDown = 0, afterUp = 0;
            for (var j = k + 1; j < Features.Count; j++)
            {
                var (down, up) = Swing(Features[j]);
                afterDown += down;
                afterUp += up;
            }
            var t = Features[k].Num("height", 0);
            lo = Math.Min(lo, t - afterDown);
            hi = Math.Max(hi, t + afterUp);
        }
        MinHeight = (float)Math.Floor(lo - 1);
        MaxHeight = (float)Math.Ceiling(hi + 1);
    }

    // ---- 3D value noise (fBm), a function of position alone --------------------------------------------------------

    private static double Hash3(int ix, int iy, int iz, int seed)
    {
        unchecked
        {
            var h = (uint)(ix * 374761393 + iy * 668265263 + iz * 1274126177 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFFFF) / (double)0xFFFFFF * 2.0 - 1.0;
        }
    }

    /// <summary>Smooth value noise in -1..1 with one lattice point per unit.</summary>
    public static double Noise3(double x, double y, double z, int seed)
    {
        double fx0 = Math.Floor(x), fy0 = Math.Floor(y), fz0 = Math.Floor(z);
        int ix = (int)fx0, iy = (int)fy0, iz = (int)fz0;
        double fx = x - fx0, fy = y - fy0, fz = z - fz0;
        double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy), sz = fz * fz * (3 - 2 * fz);
        var a0 = Lerp(Hash3(ix, iy, iz, seed), Hash3(ix + 1, iy, iz, seed), sx);
        var a1 = Lerp(Hash3(ix, iy + 1, iz, seed), Hash3(ix + 1, iy + 1, iz, seed), sx);
        var b0 = Lerp(Hash3(ix, iy, iz + 1, seed), Hash3(ix + 1, iy, iz + 1, seed), sx);
        var b1 = Lerp(Hash3(ix, iy + 1, iz + 1, seed), Hash3(ix + 1, iy + 1, iz + 1, seed), sx);
        return Lerp(Lerp(a0, a1, sy), Lerp(b0, b1, sy), sz);

        static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }

    public static double Fbm3(double x, double y, double z, double scale, int octaves, double persistence, double lacunarity, int seed)
    {
        double sum = 0, ampSum = 0, amp = 1, freq = 1 / scale;
        for (var o = 0; o < octaves; o++)
        {
            sum += amp * Noise3(x * freq, y * freq, z * freq, seed + o * 1013);
            ampSum += amp;
            amp *= persistence;
            freq *= lacunarity;
        }
        return ampSum > 0 ? sum / ampSum : 0;
    }

    // ---- Dug ground (PlanetEdits.cs): runtime changes, never baked or fingerprinted -------------------------------

    [NotBaked]
    private PlanetEdits? _edits;

    /// <summary>Dug ground (craters, scoops, fills), made on first use.</summary>
    public PlanetEdits Edits => _edits ??= new PlanetEdits(this);

    /// <summary>The dug ground if anything has asked for it.</summary>
    public PlanetEdits? EditsIfMade => _edits;

    private PlanetEdits? Dug => _edits is { Any: true } e ? e : null;

    /// <summary>The highest solid ground can be right now (metres above <see cref="Radius"/>): fills can stand over the features' own.</summary>
    public float HighestNow => MaxHeight + (_edits?.MaxRise ?? 0);

    /// <summary>The lowest solid ground can be right now: digging goes down to bedrock.</summary>
    public float LowestNow => _edits is { Any: true } e ? MathF.Min(MinHeight, e.Bedrock) : MinHeight;

    // ---- Cached vertices --------------------------------------------------------------------------------------------

    /// <summary>A tile's vertices: positions relative to the centre (x, y, z) per vertex, row by row.</summary>
    private sealed class TileData(float[] p)
    {
        public readonly float[] P = p;
    }

    private sealed class Runtime(int tiles)
    {
        public readonly TileData?[] Tiles = new TileData?[tiles];
        public readonly int[] Used = new int[tiles];
        public int Clock, Held, Made;
        public readonly object TrimLock = new();
    }

    [NotBaked]
    private Runtime? _rt;

    private Runtime Rt => Volatile.Read(ref _rt) ?? MakeRuntime();

    private Runtime MakeRuntime()
    {
        Interlocked.CompareExchange(ref _rt, new Runtime(6 * TilesPerSide * TilesPerSide), null);
        return _rt!;
    }

    private int TilesPerSide => N / Tile;

    /// <summary>Tiles held and made since load (the self-test).</summary>
    public (int Held, int Made) TileStats => _rt is { } r ? (r.Held, r.Made) : default;

    /// <summary>Vertex (<paramref name="i"/>, <paramref name="j"/>) of <paramref name="face"/> (0..N), relative to the centre.</summary>
    public Vector3 VertexRel(int face, int i, int j)
    {
        var t = TilesPerSide;
        int ti = Math.Min(i / Tile, t - 1), tj = Math.Min(j / Tile, t - 1);
        var k = (face * t + tj) * t + ti;
        var rt = Rt;
        var tile = Volatile.Read(ref rt.Tiles[k]) ?? MakeTile(rt, face, ti, tj, k);
        rt.Used[k] = ++rt.Clock;
        var o = ((j - tj * Tile) * TileVerts + (i - ti * Tile)) * 3;
        return new Vector3(tile.P[o], tile.P[o + 1], tile.P[o + 2]);
    }

    /// <summary>A grid vertex in world space.</summary>
    public Vector3 Vertex(int face, int i, int j) => Center + VertexRel(face, i, j);

    private TileData MakeTile(Runtime rt, int face, int ti, int tj, int k)
    {
        var p = new float[TileVerts * TileVerts * 3];
        var o = 0;
        for (var j = 0; j < TileVerts; j++)
        for (var i = 0; i < TileVerts; i++)
        {
            DirOf(face, ti * Tile + i, tj * Tile + j, out var x, out var y, out var z);
            var r = Radius + HeightAt(x, y, z);
            p[o++] = (float)(x * r);
            p[o++] = (float)(y * r);
            p[o++] = (float)(z * r);
        }
        var tile = new TileData(p);
        if (Interlocked.CompareExchange(ref rt.Tiles[k], tile, null) is { } other)
            return other;
        Interlocked.Increment(ref rt.Made);
        if (Interlocked.Increment(ref rt.Held) > Budget)
            Trim(rt);
        return tile;
    }

    private static void Trim(Runtime rt)
    {
        lock (rt.TrimLock)
        {
            if (rt.Held <= Budget)
                return;
            // Drop the older half of what's held (a rare sweep: tiles are 64 m across and players walk).
            var cut = rt.Clock - Budget * 64;
            var drop = 0;
            for (var pass = 0; pass < 3 && rt.Held - drop > Budget * 3 / 4; pass++, cut += Budget * 64)
                for (var k = 0; k < rt.Tiles.Length; k++)
                    if (rt.Tiles[k] != null && rt.Used[k] < cut)
                    {
                        Volatile.Write(ref rt.Tiles[k], null);
                        drop++;
                    }
            Interlocked.Add(ref rt.Held, -drop);
        }
    }

    // ---- The surface as triangles ----------------------------------------------------------------------------------

    /// <summary>
    /// The ground's distance from the centre straight along unit direction <paramref name="dir"/>: where that line meets the
    /// cell's triangle (exactly the collision surface). Also gives that triangle's outward normal.
    /// </summary>
    public float SurfaceRadius(Vector3 dir, out Vector3 normal)
    {
        Locate(dir.X, dir.Y, dir.Z, out var f, out var fi, out var fj);
        int i = Math.Clamp((int)Math.Floor(fi), 0, N - 1), j = Math.Clamp((int)Math.Floor(fj), 0, N - 1);
        var a = VertexRel(f, i, j);
        var e = VertexRel(f, i + 1, j + 1);
        // The diagonal a-e splits the cell; which side of the plane through the centre, a and e is the direction on?
        var side = Vector3.Cross(a, e);
        Vector3 b, c;
        if (Vector3.Dot(side, dir) * Vector3.Dot(side, VertexRel(f, i, j + 1)) >= 0)
        {
            b = VertexRel(f, i, j + 1);
            c = e; // (a, (i, j+1), e): front faces out
        }
        else
        {
            b = e;
            c = VertexRel(f, i + 1, j); // (a, e, (i+1, j))
        }
        var n = Vector3.Cross(b - a, c - a);
        var len = n.Length();
        normal = len > 1e-12f ? n / len : dir;
        var dn = Vector3.Dot(normal, dir);
        return dn > 1e-6f ? Vector3.Dot(normal, a) / dn : a.Length();
    }

    /// <summary>How far above the ground a world point is (negative: under it), straight along "up".</summary>
    public float Altitude(Vector3 p)
    {
        var rel = p - Center;
        var d = rel.Length();
        if (d < 1e-3f)
            return -(float)Radius;
        var dir = rel / d;
        if (Crust > 0 && d - (float)Radius < Inner)
            return Inner - (d - (float)Radius); // in the cavity: how far from its walls, the inside of the crust (gravity holds things to them)
        if (Dug is { } e && e.TryColumn(dir.X, dir.Y, dir.Z, out var spans))
        {
            // Dug ground: over the span the point is in (negative, inside it), else over the highest span below it.
            var a = d - (float)Radius;
            return a - TerrainEdits.GroundBelow(spans, a);
        }
        return d - SurfaceRadius(dir, out _);
    }

    /// <summary>
    /// <see cref="Altitude"/> from the features' smooth height straight under the point instead of the triangles (within a
    /// few centimetres of it on the planetoid's 2 m cells), with no cached tiles: for marching rays far from the ground.
    /// </summary>
    public float RoughAltitude(Vector3 p)
    {
        double x = p.X - Cx, y = p.Y - Cy, z = p.Z - Cz;
        var d = Math.Sqrt(x * x + y * y + z * z);
        if (d < 1e-3)
            return -(float)Radius;
        var rise = _edits?.MaxRise ?? 0; // (a fill can stand over the features' own ground)
        if (Crust > 0 && d < InnerRadius)
            return (float)(InnerRadius - d); // (the cavity)
        if (d > Radius + MaxHeight + rise)
            return (float)(d - Radius - MaxHeight - rise);
        return (float)(d - Radius - HeightAt(x / d, y / d, z / d) - rise);
    }

    /// <summary>The ground's normal under a world point.</summary>
    public Vector3 NormalUnder(Vector3 p)
    {
        var rel = p - Center;
        var d = rel.Length();
        var dir = d > 1e-3f ? rel / d : Vector3.UnitY;
        if (Crust > 0 && d - (float)Radius < Inner)
            return -dir; // the cavity's walls face the centre
        if (Dug is { } e && e.TryColumn(dir.X, dir.Y, dir.Z, out _))
            return dir; // (dug tops are level: "up" there)
        SurfaceRadius(dir, out var n);
        return n;
    }

    /// <summary>The ground point straight under (or over) a world point.</summary>
    public Vector3 GroundUnder(Vector3 p)
    {
        var rel = p - Center;
        var d = rel.Length();
        var dir = d > 1e-3f ? rel / d : Vector3.UnitY;
        if (Crust > 0 && d - (float)Radius < Inner)
            return Center + dir * (float)InnerRadius; // the cavity: its wall straight "under" (outwards)
        if (Dug is { } e && e.TryColumn(dir.X, dir.Y, dir.Z, out var spans))
            return Center + dir * ((float)Radius + TerrainEdits.GroundBelow(spans, d - (float)Radius));
        return Center + dir * SurfaceRadius(dir, out _);
    }

    /// <summary>
    /// Every ground triangle whose bounds reach min..max, front faces out (up), as contact shapes for capsule queries
    /// (<see cref="ShapeKind.Terrain"/>, index = the cell, sub 0 or 1). Deterministic order: by face, then row, then cell.
    /// </summary>
    public void Gather(Vector3 min, Vector3 max, List<ContactShape> into)
    {
        var c = Center;
        var mid = (min + max) * 0.5f - c;
        var half = (max - min).Length() * 0.5f;
        var dm = mid.Length();
        if (dm - half > Radius + HighestNow + 1 || dm + half < Radius + (Crust > 0 ? Inner : LowestNow) - 1)
            return; // all above the highest ground, or all deep inside the planet (or in a hollow one's cavity)
        var hollow = Crust > 0 && dm - half < InnerRadius + 1; // (the cavity's walls too)
        var inner = (float)InnerRadius;
        Span<Vector3> corners = stackalloc Vector3[8];
        for (var k = 0; k < 8; k++)
            corners[k] = new Vector3((k & 1) == 0 ? min.X : max.X, (k & 2) == 0 ? min.Y : max.Y, (k & 4) == 0 ? min.Z : max.Z) - c;
        for (var f = 0; f < 6; f++)
        {
            FaceBasis(f, out var fn, out var fu, out var fv);
            double lo0 = double.MaxValue, hi0 = double.MinValue, lo1 = double.MaxValue, hi1 = double.MinValue;
            var any = false;
            foreach (var p in corners)
            {
                var dn = Vector3.Dot(p, fn);
                if (dn <= 0.1f * p.Length())
                    continue;
                double gi = (Unwarp(Vector3.Dot(p, fu) / dn) + 1) * 0.5 * N, gj = (Unwarp(Vector3.Dot(p, fv) / dn) + 1) * 0.5 * N;
                lo0 = Math.Min(lo0, gi);
                hi0 = Math.Max(hi0, gi);
                lo1 = Math.Min(lo1, gj);
                hi1 = Math.Max(hi1, gj);
                any = true;
            }
            if (!any || hi0 < -1 || lo0 > N + 1 || hi1 < -1 || lo1 > N + 1)
                continue;
            int i0 = Math.Max(0, (int)Math.Floor(lo0) - 1), i1 = Math.Min(N - 1, (int)Math.Floor(hi0) + 1);
            int j0 = Math.Max(0, (int)Math.Floor(lo1) - 1), j1 = Math.Min(N - 1, (int)Math.Floor(hi1) + 1);
            var dug = Dug;
            for (var j = j0; j <= j1; j++)
            for (var i = i0; i <= i1; i++)
            {
                if (dug != null && dug.Touched(f, i, j))
                {
                    // A dug cell: its columns' faces, two-sided (PlanetEdits.cs).
                    var sink = new DugContacts { Into = into, Min = min, Max = max, Cell = (f * N + j) * N + i };
                    dug.CellFaces(f, i, j, 0, 0, 0, ref sink);
                    continue;
                }
                Vector3 ra = VertexRel(f, i, j), rb = VertexRel(f, i + 1, j), rd = VertexRel(f, i, j + 1), re = VertexRel(f, i + 1, j + 1);
                var cell = (f * N + j) * N + i;
                if (hollow)
                {
                    // The cavity's wall under this cell: the same grid on the inner sphere, front faces towards the centre.
                    Vector3 ia = Vector3.Normalize(ra) * inner + c, ib = Vector3.Normalize(rb) * inner + c, id = Vector3.Normalize(rd) * inner + c, ie = Vector3.Normalize(re) * inner + c;
                    var imin = Vector3.Min(Vector3.Min(ia, ib), Vector3.Min(id, ie));
                    var imax = Vector3.Max(Vector3.Max(ia, ib), Vector3.Max(id, ie));
                    if (!(imin.X > max.X || imax.X < min.X || imin.Y > max.Y || imax.Y < min.Y || imin.Z > max.Z || imax.Z < min.Z))
                    {
                        into.Add(ContactShape.FromTriangle(ia, ie, id, new ShapeId(ShapeKind.Terrain, cell, 2), true));
                        into.Add(ContactShape.FromTriangle(ia, ib, ie, new ShapeId(ShapeKind.Terrain, cell, 3), true));
                    }
                }
                Vector3 a = ra + c, b = rb + c, d = rd + c, e = re + c;
                var tmin = Vector3.Min(Vector3.Min(a, b), Vector3.Min(d, e));
                var tmax = Vector3.Max(Vector3.Max(a, b), Vector3.Max(d, e));
                if (tmin.X > max.X || tmax.X < min.X || tmin.Y > max.Y || tmax.Y < min.Y || tmin.Z > max.Z || tmax.Z < min.Z)
                    continue;
                into.Add(ContactShape.FromTriangle(a, d, e, new ShapeId(ShapeKind.Terrain, cell, 0), true));
                into.Add(ContactShape.FromTriangle(a, e, b, new ShapeId(ShapeKind.Terrain, cell, 1), true));
            }
        }
    }

    private struct DugContacts : IDigFaceSink
    {
        public List<ContactShape> Into;
        public Vector3 Min, Max;
        public int Cell, Sub;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 color, float rock)
        {
            var lo = Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d));
            var hi = Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d));
            if (hi.X < Min.X || lo.X > Max.X || hi.Y < Min.Y || lo.Y > Max.Y || hi.Z < Min.Z || lo.Z > Max.Z)
                return;
            Into.Add(ContactShape.FromTriangle(a, b, c, new ShapeId(ShapeKind.Terrain, Cell, Sub++)));
            Into.Add(ContactShape.FromTriangle(a, c, d, new ShapeId(ShapeKind.Terrain, Cell, Sub++)));
        }
    }

    /// <summary>
    /// Distance along a unit ray to the ground, or <paramref name="maxDist"/> if it doesn't reach it. Marches by half the
    /// height above the ground (at least 25 cm), then halves the last step down onto the triangles. A ray starting under
    /// the ground hits at 0.
    /// </summary>
    public float Raycast(Vector3 o, Vector3 d, float maxDist, out Vector3 normal)
    {
        normal = -d;
        var rel = o - Center;
        var rmax = (float)(Radius + HighestNow + 1);
        var b = Vector3.Dot(rel, d);
        var cc = rel.LengthSquared() - rmax * rmax;
        var disc = b * b - cc;
        if (disc <= 0)
            return maxDist;
        var sq = MathF.Sqrt(disc);
        float t0 = -b - sq, t1 = -b + sq;
        if (t1 < 0)
            return maxDist;
        var t = MathF.Max(t0, 0);
        var end = MathF.Min(t1, maxDist);
        // Past where the ray enters the lowest possible ground there's nothing to look for.
        var rmin = (float)(Radius + LowestNow - 1);
        var cin = rel.LengthSquared() - rmin * rmin;
        var din = b * b - cin;
        if (din > 0 && Crust <= 0) // (a hollow planetoid: a ray can go down a shaft, across the cavity and on to its far wall)
        {
            var tin = -b - MathF.Sqrt(din);
            if (tin >= 0)
                end = MathF.Min(end, tin + 0.01f);
        }
        if (t > end)
            return maxDist;
        var alt = Altitude(o + d * t);
        if (alt <= 0)
        {
            if (t <= 0)
                return 0;
            return Settle(o, d, t, t, out normal); // (can't happen above the highest ground; kept safe)
        }
        var dug = Dug;
        var rough = dug == null ? 0f : RoughAltitude(o + d * t);
        for (var steps = 0; steps < 100000; steps++)
        {
            var prev = t;
            var step = MathF.Max(alt * 0.5f, 0.25f);
            // Next to dug ground the height over what's under the point says nothing about a wall beside it (dug walls
            // stand straight up): step by the height over the ground as built (the features', less what fills may have
            // added), and finely in or by a hole, and look along each step for the columns it crosses (PlanetEdits.SegmentHit),
            // so a wall, a lip or a rim's corner isn't stepped over.
            var byDug = false;
            if (dug != null && rough < 3f)
            {
                var q = o + d * t - Center;
                if (dug.NearDug(q.X, q.Y, q.Z))
                {
                    step = MathF.Max(MathF.Min(alt, rough) * 0.5f, DugStep);
                    byDug = true;
                }
            }
            t = MathF.Min(t + step, end);
            if (byDug)
            {
                var u = dug!.SegmentHit(o + d * prev - Center, o + d * t - Center);
                if (u >= 0)
                {
                    var th = prev + (t - prev) * u;
                    if (Altitude(o + d * th) <= 0)
                        return Settle(o, d, prev, th, out normal); // (untouched ground came first, in the same step)
                    normal = dug.HitNormal(o + d * (th + 1e-3f), d);
                    return th;
                }
            }
            // Well clear of the ground the features' own smooth height is near enough (no cached tiles made for a long
            // ray's whole path); close to it, the triangles decide.
            alt = rough = RoughAltitude(o + d * t);
            if (alt < 1f)
                alt = Altitude(o + d * t);
            if (alt <= 0)
                return Settle(o, d, prev, t, out normal);
            if (t >= end)
                break;
        }
        return maxDist;
    }

    /// <summary>The longest step a ray takes next to dug ground (thinner slivers than this can be stepped over).</summary>
    public const float DugStep = 0.05f;

    private float Settle(Vector3 o, Vector3 d, float above, float below, out Vector3 normal)
    {
        for (var k = 0; k < 24 && below - above > 1e-4f; k++)
        {
            var m = (above + below) * 0.5f;
            if (Altitude(o + d * m) <= 0)
                below = m;
            else
                above = m;
        }
        var at = o + d * below;
        var rel = at - Center;
        if (Dug is { } e && rel.LengthSquared() > 1e-6f && e.TryColumn(rel.X, rel.Y, rel.Z, out _))
            normal = e.HitNormal(at, d); // (a dug column: its top, underside or wall)
        else
            normal = NormalUnder(at);
        if (Vector3.Dot(normal, d) > 0)
            normal = -normal;
        return above;
    }

    // ---- Map file ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The map's top-level <c>"planetoid"</c> object: <c>radius</c> (metres, 500 to 16000), <c>cell</c> (about this many
    /// metres per cell; rounded so a face has a power-of-two number of them), <c>strength</c> (gravity at the surface,
    /// default 11), <c>base</c>, <c>surface</c> (as a terrain's) and <c>features</c>. The centre is straight under the
    /// world's origin, so the +Y pole (latitude 90) is at (0, 0, 0) plus its height: spawns go there.
    /// </summary>
    public static Planetoid Parse(JsonElement e, string source)
    {
        var where = $"{source}: planetoid";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} should be an object");
        var p = new Planetoid { Radius = 10000, Strength = 11 };
        var cell = 2.0;
        foreach (var kv in e.EnumerateObject())
        {
            switch (kv.Name.ToLowerInvariant())
            {
                case "radius": p.Radius = Range(Num(kv.Value, where, "radius"), 500, 16000, where, "radius"); break;
                case "cell": cell = Range(Num(kv.Value, where, "cell"), 0.5, 16, where, "cell"); break;
                case "strength": p.Strength = (float)Range(Num(kv.Value, where, "strength"), 1, 60, where, "strength"); break;
                case "base": p.Base = Num(kv.Value, where, "base"); break;
                case "surface": p.Surface = TerrainSpec.ParseSurface(kv.Value, where); break;
                case "cavity": ParseCavity(p, kv.Value, where + ".cavity"); break;
                case "features":
                    if (kv.Value.ValueKind != JsonValueKind.Array)
                        throw new MapFileException($"{where}.features should be a list");
                    var k = 0;
                    foreach (var fe in kv.Value.EnumerateArray())
                        p.Features.Add(ParseFeature(fe, $"{where}.features[{k++}]"));
                    break;
                default:
                    throw new MapFileException($"{where}: no key \"{kv.Name}\" (it takes: radius, cell, strength, base, surface, cavity, features)");
            }
        }
        var arc = p.Radius * Math.PI / 2;
        var cells = (int)Math.Clamp(Math.Pow(2, Math.Round(Math.Log2(arc / cell))), PageCells, 16384);
        p.N = cells;
        p.Cx = 0;
        p.Cy = 0; // the centre is the World's origin: float precision is the same all round (PLANETS.md "Origin")
        p.Cz = 0;
        p.WorkOutBounds();
        if (p.Crust > 0)
        {
            if (p.Radius > 4000)
                throw new MapFileException($"{where}.cavity: a hollow planetoid is at most 4000 m in radius (its cavity's drawing keeps a bit for every cell)");
            if (p.Crust > p.Radius * 0.8)
                throw new MapFileException($"{where}.cavity: \"crust\" is at most 80% of the radius");
            if (p.MinHeight - p.Inner < MinCrust)
                throw new MapFileException($"{where}.cavity: the crust is only {p.MinHeight - p.Inner:0.#} m thick where the ground can be lowest (its features reach down to {p.MinHeight:0.#} m); it needs at least {MinCrust} m");
            if (p.InnerRadius < p.CoreRadius + 50)
                throw new MapFileException($"{where}.cavity: the cavity ({p.InnerRadius:0} m across the middle) needs room round its core");
        }
        return p;
    }

    /// <summary>The thinnest a hollow planetoid's crust may be where its features dig deepest (metres).</summary>
    public const float MinCrust = 8;

    /// <summary><c>"cavity": { "crust": 180, "glow": "#ff8c33", "core": 40 }</c>: a hollow planetoid (PLANETS.md "Hollow planetoids").</summary>
    private static void ParseCavity(Planetoid p, JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} should be an object like {{ \"crust\": 180 }}");
        foreach (var kv in e.EnumerateObject())
        {
            switch (kv.Name.ToLowerInvariant())
            {
                case "crust": p.Crust = Range(Num(kv.Value, where, "crust"), 10, 2000, where, "crust"); break;
                case "glow": p.Glow = MapFile.ToColour(kv.Value, ".glow", where); break;
                case "core": p.CoreRadius = (float)Range(Num(kv.Value, where, "core"), 0, 500, where, "core"); break;
                default:
                    throw new MapFileException($"{where}: no key \"{kv.Name}\" (it takes: crust, glow, core)");
            }
        }
        if (p.Crust <= 0)
            throw new MapFileException($"{where}: needs \"crust\"");
    }

    private static TerrainFeature ParseFeature(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: should be an object like {{ \"type\": \"noise\", ... }}");
        string? type = null;
        foreach (var kv in e.EnumerateObject())
            if (kv.Name.Equals("type", StringComparison.OrdinalIgnoreCase) && kv.Value.ValueKind == JsonValueKind.String)
                type = kv.Value.GetString()!.ToLowerInvariant();
        if (type == null || !Schema.TryGetValue(type, out var schema))
            throw new MapFileException($"{where}: unknown or missing \"type\". Known: {string.Join(", ", Schema.Keys)}");
        where += $" ({type})";
        var f = new TerrainFeature { Type = type };
        foreach (var kv in e.EnumerateObject())
        {
            if (kv.Name.Equals("type", StringComparison.OrdinalIgnoreCase))
                continue;
            var key = schema.Keys.FirstOrDefault(k => k.Equals(kv.Name, StringComparison.OrdinalIgnoreCase))
                      ?? throw new MapFileException($"{where}: no parameter \"{kv.Name}\" (it takes: {string.Join(", ", schema.Keys)})");
            f.Params.Add(new(key, kv.Value.ValueKind switch
            {
                JsonValueKind.Number => kv.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new MapFileException($"{where}.{key} should be a number"),
            }));
        }
        foreach (var r in schema.Required)
            if (!f.Has(r))
                throw new MapFileException($"{where}: needs \"{r}\"");
        if (f.Has("lat") && Math.Abs(f.Num("lat", 0)) > 90)
            throw new MapFileException($"{where}: \"lat\" is -90 to 90");
        if (f.Has("radius") && f.Num("radius", 1) <= 0 || f.Has("scale") && f.Num("scale", 1) <= 0)
            throw new MapFileException($"{where}: \"radius\" and \"scale\" are more than 0");
        if (f.Has("octaves") && f.Num("octaves", 1) is < 1 or > 10)
            throw new MapFileException($"{where}: \"octaves\" is 1 to 10");
        if (f.Has("lat") != f.Has("lon") || type is "noise" or "terrace" && f.Has("lat") != f.Has("radius"))
            throw new MapFileException($"{where}: a place is \"lat\", \"lon\" and \"radius\" together");
        if (type == "terrace" && f.Num("step", 1) < 0.5)
            throw new MapFileException($"{where}: \"step\" is at least 0.5");
        if (type == "rift" && (f.Num("length", 1) <= 0 || f.Num("width", 1) < 4 || f.Num("width", 1) > f.Num("length", 1)))
            throw new MapFileException($"{where}: \"width\" is 4 m to the \"length\"");
        return f;
    }

    private static double Num(JsonElement e, string where, string key) =>
        e.ValueKind == JsonValueKind.Number ? e.GetDouble() : throw new MapFileException($"{where}.{key} should be a number");

    private static double Range(double v, double lo, double hi, string where, string key) =>
        v >= lo && v <= hi ? v : throw new MapFileException($"{where}.{key} is {lo} to {hi}, got {v}");

    /// <summary>The "planetoid" object as map-file JSON (reads back to the same planetoid).</summary>
    public string ToJson(string indent)
    {
        var i2 = indent + "  ";
        var cell = Radius * Math.PI / 2 / N;
        return "{\n"
               + $"{i2}\"radius\": {Radius.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},\n"
               + $"{i2}\"cell\": {cell.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},\n"
               + $"{i2}\"strength\": {Strength.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},\n"
               + (Base != 0 ? $"{i2}\"base\": {Base.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},\n" : "")
               + $"{i2}\"surface\": {TerrainSpec.SurfaceJson(Surface)},\n"
               + (Crust > 0 ? $"{i2}\"cavity\": {{ \"crust\": {Js(Crust)}, \"glow\": [{Js(Glow.X)}, {Js(Glow.Y)}, {Js(Glow.Z)}], \"core\": {Js(CoreRadius)} }},\n" : "")
               + TerrainSpec.FeaturesJson(Features, i2) + $"\n{indent}}}";

        static string Js(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Fingerprint data: every input the ground is made from.</summary>
    public void Mix(Action<float> mix)
    {
        mix((float)Radius);
        mix(N);
        mix((float)Base);
        mix(Strength);
        foreach (var f in Features)
        {
            mix(f.Type.Length);
            foreach (var ch in f.Type)
                mix(ch);
            foreach (var kv in f.Params)
                mix(kv.Value is double d ? (float)d : kv.Value is true ? 1 : 0);
        }
        var s = Surface;
        foreach (var v in new[] { s.Grass, s.Dirt, s.Rock, s.Sand, s.Snow })
        {
            mix(v.X); mix(v.Y); mix(v.Z);
        }
        mix((float)s.DirtSlope);
        mix((float)s.RockSlope);
        mix((float)(s.SandBelow ?? -1e6));
        mix((float)(s.SnowAbove ?? 1e6));
        if (Crust > 0) // (solid planetoids keep the fingerprint they had)
        {
            mix((float)Crust);
            mix(CoreRadius);
            mix(Glow.X); mix(Glow.Y); mix(Glow.Z);
        }
    }
}
