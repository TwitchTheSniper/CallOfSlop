using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A city laid on a planetoid (docs/planets/CITY.md): a map's top-level <c>"city"</c> block. The plan (streets, blocks,
/// buildings) is worked out from the block's numbers and its seed on every PC alike, so nothing of it is sent or saved; the
/// geometry and the colliders are made from the plan (<see cref="CityGeometry"/>) and drawn by <c>CityRenderer</c>.
/// <para>
/// Coordinates: the plan works in metres on a flat sheet laid on the ground at the city's centre (x towards its "east"
/// axis, z towards its "north"), mapped onto the sphere by true distance and bearing from the centre (azimuthal
/// equidistant, like the planetoid's placed features). Buildings stand upright in their own frame at their middle.
/// </para>
/// </summary>
public sealed class CitySpec
{
    public float Lat = 90, Lon;

    /// <summary>The city's reach from its centre (m): blocks whose middle lies inside are built.</summary>
    public float Radius = 600;

    /// <summary>The ground's height there (m over the planetoid's radius): the city's flatten feature must make it level.</summary>
    public float Height;

    public int Seed = 1;

    /// <summary>A block's length along x and along z between building lines (m), before the jitter.</summary>
    public float BlockX = 70, BlockZ = 96;

    /// <summary>A street's and an avenue's whole width, building line to building line (m), and every how many blocks an avenue runs.</summary>
    public float Street = 16, Avenue = 26;

    public int AvenueEvery = 3;

    /// <summary>The tallest and lowest buildings' usual heights (m): the tallest stand in the middle.</summary>
    public float Tallest = 240, Lowest = 12;

    /// <summary>Where the heights have fallen to the lowest (m from the centre; 0: the radius).</summary>
    public float CoreFalloff;

    /// <summary>The elevated railway's ring (m from the centre; 0: none).</summary>
    public float Loop;

    /// <summary>A metro under the central avenue (docs/planets/CITY.md "The metro"), and its platforms' depth (m).</summary>
    public bool Metro;

    public float MetroDepth = 10;

    public static CitySpec Parse(JsonElement e, string source)
    {
        var s = new CitySpec();
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{source}: city: an object");
        foreach (var p in e.EnumerateObject())
        {
            float F() => p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetSingle() : throw new MapFileException($"{source}: city: \"{p.Name}\" is a number");
            switch (p.Name)
            {
                case "lat": s.Lat = F(); break;
                case "lon": s.Lon = F(); break;
                case "radius": s.Radius = F(); break;
                case "height": s.Height = F(); break;
                case "seed": s.Seed = (int)F(); break;
                case "blockX": s.BlockX = F(); break;
                case "blockZ": s.BlockZ = F(); break;
                case "street": s.Street = F(); break;
                case "avenue": s.Avenue = F(); break;
                case "avenueEvery": s.AvenueEvery = Math.Max(1, (int)F()); break;
                case "tallest": s.Tallest = F(); break;
                case "lowest": s.Lowest = F(); break;
                case "coreFalloff": s.CoreFalloff = F(); break;
                case "loop": s.Loop = F(); break;
                case "metro": s.Metro = F() != 0; break;
                case "metroDepth": s.MetroDepth = Math.Clamp(F(), 7, 30); break;
                default: throw new MapFileException($"{source}: city: unknown key \"{p.Name}\"");
            }
        }
        if (s.Radius is < 50 or > 3000)
            throw new MapFileException($"{source}: city: radius is 50 to 3000 m");
        if (s.BlockX < 24 || s.BlockZ < 24 || s.Street < 10 || s.Avenue < s.Street)
            throw new MapFileException($"{source}: city: blocks are 24 m or more, streets 10 m or more, avenues no narrower than streets");
        if (s.Tallest < s.Lowest || s.Lowest < 4 || s.Tallest > 600)
            throw new MapFileException($"{source}: city: heights run from 4 m (lowest) to 600 m (tallest)");
        return s;
    }
}

/// <summary>The facade or surface a face is drawn with (CityRenderer's shader reads it from the vertex).</summary>
public enum CityStyle : byte
{
    Glass, Grid, Brick, Ribbon, Plain, Roof, Road, Crossing, Pavement, Kerb, Court, Lamp, Metal, Neon, Beacon, Rail, Stairs, Leaves, TrainSide, TrainEnd, MetroWall, Pad, Water,
}

/// <summary>A building: its middle on the plan, its turn, and the seed its looks are drawn from. Its masses stand in its frame.</summary>
public struct CityBuilding
{
    public Vector2 At;
    public float Yaw;
    public uint Seed;
    public byte Style;
    public bool Shops;
    public float FloorHeight;
    public float Lit;
    public uint Wall;
}

/// <summary>One upright prism of a building, in the building's frame (metres over its ground).</summary>
public struct CityMass
{
    public int Building;
    public Vector2 At, Half;
    public float Bottom, Top;

    /// <summary>0 a box, 1 a box with its corners cut by <see cref="Cut"/>, 2 round (16 sides).</summary>
    public byte Shape;

    public float Cut;
    public byte Style;
    public uint Color;

    /// <summary>Drawn only near (the roof's clutter, parapets, cornices): the far level of detail leaves it out.</summary>
    public bool Detail;

    /// <summary>Stands in the physics world.</summary>
    public bool Solid;
}

/// <summary>A run of street between two crossings, along x (<see cref="AlongX"/>) or z.</summary>
public struct CityStreet
{
    public Vector2 A, B;
    public float Half, Carriage;
    public bool AlongX, Avenue;
}

/// <summary>A crossing of two streets: the square where their carriageways meet.</summary>
public struct CityCrossing
{
    public Vector2 At;
    public float HalfX, HalfZ, CarX, CarZ;
}

/// <summary>A block between building lines: built on, or the square.</summary>
public struct CityBlock
{
    public Vector2 Min, Max;
    public byte Kind; // 0 built, 1 the square
}

/// <summary>The elevated railway: its line on the plan (a closed loop, a point every few metres), its deck's height and the
/// stations (distances along the line).</summary>
public sealed class CityRail
{
    public Vector2[] Path = [];
    public float Height = 9.5f;
    /// <summary>The stations' middles on the plan (each on the line).</summary>
    public Vector2[] Stations = [];

    /// <summary>The line's whole length (m).</summary>
    public float Length;
}

/// <summary>A landing pad on a tower's roof: its middle on the plan, its deck's height over the ground and its radius.</summary>
public struct CityPad
{
    public Vector2 At;
    public float Height, Radius;
}

/// <summary>The whole plan of a city: what <see cref="CityGeometry"/> makes its meshes and colliders from.</summary>
public sealed class CityPlan
{
    public readonly CitySpec Spec;
    public CityBuilding[] Buildings = [];
    public CityMass[] Masses = [];
    public CityStreet[] Streets = [];
    public CityCrossing[] Crossings = [];
    public CityBlock[] Blocks = [];

    /// <summary>The elevated railway: a closed line over the avenues (null: none).</summary>
    public CityRail? Rail;

    /// <summary>The metro under the central avenue (null: none).</summary>
    public CityMetro? Metro;

    /// <summary>The towers' rooftop pads (the air taxis' stops).</summary>
    public CityPad[] Pads = [];

    /// <summary>The square in the middle (no traffic): its rectangle on the plan.</summary>
    public Vector2 SquareMin, SquareMax;

    /// <summary>The planetoid's centre and the ground's radius under the city (m).</summary>
    public Vector3 PlanetCenter;

    public double GroundRadius;

    /// <summary>The city's centre direction and its sheet's axes (unit vectors from the planet's centre).</summary>
    public Vector3 Up, East, North;

    public CityPlan(CitySpec spec) => Spec = spec;

    // ---- Mapping the plan onto the sphere ----------------------------------------------------------------------------

    /// <summary>The direction from the planet's centre to a point of the plan (true distance and bearing from the city's centre).</summary>
    public Vector3 DirOf(Vector2 p)
    {
        var d = p.Length();
        if (d < 1e-4f)
            return Up;
        var th = d / GroundRadius;
        var (s, c) = Math.SinCos(th);
        var t = (East * p.X + North * p.Y) / d;
        return Vector3.Normalize(Up * (float)c + t * (float)s);
    }

    /// <summary>A point of the plan, <paramref name="above"/> m over the city's ground.</summary>
    public Vector3 Ground(Vector2 p, float above = 0) => PlanetCenter + DirOf(p) * (float)(GroundRadius + above);

    /// <summary>A building's frame: its origin on the ground at its middle, its east, up and north axes (yaw turns the first and last).</summary>
    public Matrix4x4 FrameOf(in CityBuilding b, float sink)
    {
        var up = DirOf(b.At);
        var e = Vector3.Normalize(East - up * Vector3.Dot(East, up));
        var n = Vector3.Cross(e, up);
        var (sy, cy) = MathF.SinCos(b.Yaw);
        var ex = e * cy + n * sy;
        var nz = Vector3.Cross(ex, up);
        var o = PlanetCenter + up * (float)(GroundRadius - sink);
        return new Matrix4x4(ex.X, ex.Y, ex.Z, 0, up.X, up.Y, up.Z, 0, nz.X, nz.Y, nz.Z, 0, o.X, o.Y, o.Z, 1);
    }

    // ---- Making the plan ---------------------------------------------------------------------------------------------

    /// <summary>The plan of <paramref name="spec"/> on <paramref name="pl"/>: the same on every PC.</summary>
    public static CityPlan Make(CitySpec spec, Planetoid pl)
    {
        var plan = new CityPlan(spec) { PlanetCenter = pl.Center, GroundRadius = pl.Radius + spec.Height };
        double lat = spec.Lat * Math.PI / 180, lon = spec.Lon * Math.PI / 180;
        plan.Up = Vector3.Normalize(new Vector3((float)(Math.Cos(lat) * Math.Sin(lon)), (float)Math.Sin(lat), (float)(Math.Cos(lat) * Math.Cos(lon))));
        // The sheet's x axis: world X turned into the ground's plane (at a pole), else the local east.
        var x = MathF.Abs(plan.Up.Y) > 0.99f ? Vector3.UnitX : Vector3.Cross(Vector3.UnitY, plan.Up);
        plan.East = Vector3.Normalize(x - plan.Up * Vector3.Dot(x, plan.Up));
        plan.North = Vector3.Cross(plan.East, plan.Up);
        new Planner(plan).Run();
        return plan;
    }

    /// <summary>A small deterministic generator (xorshift): the same numbers on every machine and .NET.</summary>
    public struct Dice(uint seed)
    {
        private uint _s = seed * 2654435761u ^ 0x9E3779B9u | 1;

        public uint Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return _s;
        }

        public float Unit() => (Next() >> 8) / 16777216f;

        public float Range(float a, float b) => a + (b - a) * Unit();

        public bool Chance(float p) => Unit() < p;

        public int Pick(int n) => (int)(Next() % (uint)n);
    }

    private sealed class Planner(CityPlan plan)
    {
        private readonly CitySpec _s = plan.Spec;
        private readonly List<CityBuilding> _buildings = new();
        private readonly List<CityMass> _masses = new();
        private readonly List<CityStreet> _streets = new();
        private readonly List<CityCrossing> _crossings = new();
        private readonly List<CityBlock> _blocks = new();
        private readonly List<CityPad> _pads = new();
        private bool _market;
        private Dice _dice = new((uint)plan.Spec.Seed);

        private record struct Line(float Pos, float Half, float Car, bool Avenue);

        /// <summary>The street lines across one axis: an avenue through the middle, then outwards both ways.</summary>
        private List<Line> Lines(float block, uint salt)
        {
            var d = new Dice((uint)_s.Seed * 31 + salt);
            var lines = new List<Line> { Mk(0, true) };
            foreach (var sign in new[] { 1, -1 })
            {
                var pos = 0f;
                var half = lines[0].Half;
                for (var k = 1; pos < _s.Radius + block; k++)
                {
                    var l = Mk(0, k % _s.AvenueEvery == 0);
                    pos += half + block * d.Range(0.85f, 1.15f) + l.Half;
                    lines.Add(l with { Pos = sign * pos });
                    half = l.Half;
                }
            }
            lines.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            return lines;

            Line Mk(float p, bool av)
            {
                var w = av ? _s.Avenue : _s.Street;
                var pave = av ? 4.5f : 3.5f;
                return new Line(p, w * 0.5f, w * 0.5f - pave, av);
            }
        }

        public void Run()
        {
            var xs = Lines(_s.BlockX, 1);
            var zs = Lines(_s.BlockZ, 2);
            var r2 = _s.Radius * _s.Radius;
            int ix0 = xs.FindIndex(l => l.Pos == 0), iz0 = zs.FindIndex(l => l.Pos == 0);
            // The square: the four blocks round the middle and the crossing between them.
            plan.SquareMin = new Vector2(xs[ix0 - 1].Pos + xs[ix0 - 1].Half, zs[iz0 - 1].Pos + zs[iz0 - 1].Half);
            plan.SquareMax = new Vector2(xs[ix0 + 1].Pos - xs[ix0 + 1].Half, zs[iz0 + 1].Pos - zs[iz0 + 1].Half);
            bool InSquare(Vector2 p) => p.X > plan.SquareMin.X && p.X < plan.SquareMax.X && p.Y > plan.SquareMin.Y && p.Y < plan.SquareMax.Y;

            var built = new bool[xs.Count + 1, zs.Count + 1];
            for (var i = 0; i + 1 < xs.Count; i++)
                for (var j = 0; j + 1 < zs.Count; j++)
                {
                    var min = new Vector2(xs[i].Pos + xs[i].Half, zs[j].Pos + zs[j].Half);
                    var max = new Vector2(xs[i + 1].Pos - xs[i + 1].Half, zs[j + 1].Pos - zs[j + 1].Half);
                    var mid = (min + max) * 0.5f;
                    if (mid.LengthSquared() > r2)
                        continue;
                    built[i, j] = true;
                    var square = InSquare(mid);
                    // The market hall: the block over the avenue from the square's north side, east of the middle.
                    var market = !square && !_market && min.X > 0 && min.X < 20 && min.Y > plan.SquareMax.Y && min.Y < plan.SquareMax.Y + 30;
                    _market |= market;
                    _blocks.Add(new CityBlock { Min = min, Max = max, Kind = (byte)(square ? 1 : market ? 2 : 0) });
                    if (!square && !market)
                        Block(min, max);
                }
            bool Built(int i, int j) => i >= 0 && j >= 0 && i < xs.Count && j < zs.Count && built[i, j];
            // Streets along x (on the z lines) between crossings, and along z (on the x lines), where a block is built beside them.
            for (var j = 0; j < zs.Count; j++)
                for (var i = 0; i + 1 < xs.Count; i++)
                    if (Built(i, j) || Built(i, j - 1))
                    {
                        var st = new CityStreet
                        {
                            A = new Vector2(xs[i].Pos + xs[i].Half, zs[j].Pos), B = new Vector2(xs[i + 1].Pos - xs[i + 1].Half, zs[j].Pos),
                            Half = zs[j].Half, Carriage = zs[j].Car, AlongX = true, Avenue = zs[j].Avenue,
                        };
                        if (!InSquare((st.A + st.B) * 0.5f))
                            _streets.Add(st);
                    }
            for (var i = 0; i < xs.Count; i++)
                for (var j = 0; j + 1 < zs.Count; j++)
                    if (Built(i, j) || Built(i - 1, j))
                    {
                        var st = new CityStreet
                        {
                            A = new Vector2(xs[i].Pos, zs[j].Pos + zs[j].Half), B = new Vector2(xs[i].Pos, zs[j + 1].Pos - zs[j + 1].Half),
                            Half = xs[i].Half, Carriage = xs[i].Car, AlongX = false, Avenue = xs[i].Avenue,
                        };
                        if (!InSquare((st.A + st.B) * 0.5f))
                            _streets.Add(st);
                    }
            for (var i = 0; i < xs.Count; i++)
                for (var j = 0; j < zs.Count; j++)
                {
                    if (!(Built(i, j) || Built(i - 1, j) || Built(i, j - 1) || Built(i - 1, j - 1)))
                        continue;
                    var at = new Vector2(xs[i].Pos, zs[j].Pos);
                    if (InSquare(at))
                        continue;
                    _crossings.Add(new CityCrossing { At = at, HalfX = xs[i].Half, HalfZ = zs[j].Half, CarX = xs[i].Car, CarZ = zs[j].Car });
                }
            if (_s.Loop > 0)
                plan.Rail = Rail(xs, zs);
            if (_s.Metro)
                plan.Metro = Metro(zs, plan.Rail);
            plan.Pads = _pads.ToArray();
            plan.Buildings = _buildings.ToArray();
            plan.Masses = _masses.ToArray();
            plan.Streets = _streets.ToArray();
            plan.Crossings = _crossings.ToArray();
            plan.Blocks = _blocks.ToArray();
        }

        /// <summary>The elevated loop: over the avenues nearest the spec's ring on each side, its corners turned in the crossings.</summary>
        private CityRail? Rail(List<Line> xs, List<Line> zs)
        {
            float Pick(List<Line> lines, int sign)
            {
                var best = float.NaN;
                foreach (var l in lines)
                    if (l.Avenue && MathF.Sign(l.Pos) == sign && (float.IsNaN(best) || MathF.Abs(MathF.Abs(l.Pos) - _s.Loop) < MathF.Abs(MathF.Abs(best) - _s.Loop)))
                        best = l.Pos;
                return best;
            }
            float x0 = Pick(xs, -1), x1 = Pick(xs, 1), z0 = Pick(zs, -1), z1 = Pick(zs, 1);
            if (float.IsNaN(x0) || float.IsNaN(x1) || float.IsNaN(z0) || float.IsNaN(z1))
                return null;
            const float R = 13f, Step = 4f;
            var pts = new List<Vector2>();
            // Round the rectangle anticlockwise from the middle of its south side: straights cut into 4 m steps, quarter turns.
            Vector2[] corners = [new(x1, z0), new(x1, z1), new(x0, z1), new(x0, z0)];
            Vector2[] dirs = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];
            var at = new Vector2((x0 + x1) * 0.5f, z0);
            var length = 0f;
            for (var k = 0; k < 4; k++)
            {
                var d = dirs[k];
                var end = corners[k] - d * R;
                var run = Vector2.Distance(at, end);
                var n = Math.Max(1, (int)MathF.Ceiling(run / Step));
                for (var i = 0; i < n; i++)
                    pts.Add(Vector2.Lerp(at, end, i / (float)n));
                length += run;
                var next = dirs[(k + 1) % 4];
                var centre = end + next * R;
                for (var i = 0; i < 6; i++)
                {
                    var a = i / 6f * MathF.PI * 0.5f;
                    var p = centre - next * R * MathF.Cos(a) + d * R * MathF.Sin(a);
                    pts.Add(p);
                }
                length += MathF.PI * 0.5f * R;
                at = corners[k] + next * R;
            }
            // Back to the start along the south side.
            var home = new Vector2((x0 + x1) * 0.5f, z0);
            var back = Vector2.Distance(at, home);
            var m = Math.Max(1, (int)MathF.Ceiling(back / Step));
            for (var i = 0; i < m; i++)
                pts.Add(Vector2.Lerp(at, home, i / (float)m));
            length += back;
            // A station in the middle of each side.
            float mx = (x0 + x1) * 0.5f, mz = (z0 + z1) * 0.5f;
            Vector2[] stations = [new(mx, z0), new(x1, mz), new(mx, z1), new(x0, mz)];
            return new CityRail { Path = pts.ToArray(), Length = length, Stations = stations };
        }

        /// <summary>The metro: under the avenue through the centre, a station under the square, one under each crossing of the
        /// elevated loop (an interchange), and one near each end of the city where there is room.</summary>
        private CityMetro Metro(List<Line> zs, CityRail? rail)
        {
            var stations = new List<float> { 0 };
            if (rail != null)
                foreach (var st in rail.Stations)
                    if (MathF.Abs(st.X) < 40)
                        stations.Add(st.Y);
            if (stations.Count == 1)
                stations.AddRange([-_s.Radius * 0.5f, _s.Radius * 0.5f]);
            float lo = stations.Min(), hi = stations.Max();
            // One more beyond each end, at a crossing (a hall wants a street over its whole length) inside the city.
            foreach (var sign in new[] { -1, 1 })
            {
                var from = sign < 0 ? lo : hi;
                var best = float.NaN;
                foreach (var l in zs)
                    if (MathF.Sign(l.Pos - from) == sign && MathF.Abs(l.Pos - from) > 180 && MathF.Abs(l.Pos) < _s.Radius - 70 && (float.IsNaN(best) || MathF.Abs(l.Pos - from) < MathF.Abs(best - from)))
                        best = l.Pos;
                if (!float.IsNaN(best))
                    stations.Add(best);
            }
            stations.Sort();
            return new CityMetro
            {
                X = 0, Stations = stations.ToArray(), Depth = _s.MetroDepth,
                Z0 = stations[0] - CityMetro.HallZ - 120, Z1 = stations[^1] + CityMetro.HallZ + 120,
            };
        }

        /// <summary>How tall the buildings stand round <paramref name="p"/>: the tallest in the middle, falling away to the edge.</summary>
        private float HeightAt(Vector2 p)
        {
            var reach = _s.CoreFalloff > 0 ? _s.CoreFalloff : _s.Radius;
            var t = 1 - Math.Clamp(p.Length() / reach, 0, 1);
            t = t * t * (3 - 2 * t);
            return _s.Lowest + (_s.Tallest - _s.Lowest) * MathF.Pow(t, 1.5f);
        }

        /// <summary>A built block cut into lots, a building on each.</summary>
        private void Block(Vector2 min, Vector2 max)
        {
            var size = max - min;
            // Along the longer side 2 to 4 lots, across it 1 or 2 (a deep block has a back row).
            var longX = size.X >= size.Y;
            float lenLong = longX ? size.X : size.Y, lenShort = longX ? size.Y : size.X;
            var nLong = Math.Clamp((int)MathF.Round(lenLong / _dice.Range(22, 40)), 1, 5);
            var nShort = lenShort > 56 ? 2 : 1;
            var cuts = new float[nLong + 1];
            cuts[nLong] = 1;
            for (var k = 1; k < nLong; k++)
                cuts[k] = (k + _dice.Range(-0.25f, 0.25f)) / nLong;
            var tall = HeightAt((min + max) * 0.5f);
            // One landmark in a block now and then near the middle.
            var landmark = _dice.Chance(0.3f * tall / _s.Tallest) ? _dice.Pick(nLong) : -1;
            for (var a = 0; a < nLong; a++)
                for (var b = 0; b < nShort; b++)
                {
                    float l0 = cuts[a] * lenLong, l1 = cuts[a + 1] * lenLong;
                    float s0 = b * lenShort / nShort, s1 = (b + 1) * lenShort / nShort;
                    var lo = longX ? new Vector2(min.X + l0, min.Y + s0) : new Vector2(min.X + s0, min.Y + l0);
                    var hi = longX ? new Vector2(min.X + l1, min.Y + s1) : new Vector2(min.X + s1, min.Y + l1);
                    // Which sides face a street (the building's front), so the tower sets back from the right edges.
                    // The lot's front: the block's side it stands on (a back row faces the other way).
                    var front = longX ? new Vector2(0, b == 0 ? -1 : 1) : new Vector2(b == 0 ? -1 : 1, 0);
                    Lot(lo, hi, tall * _dice.Range(0.55f, 1.2f) * (a == landmark && b == 0 ? 1.6f : 1f), front);
                }
        }

        /// <summary>0xRRGGBB to the vertex colour's RGBA8 (red in the lowest byte).</summary>
        public static uint Rgb(uint hex) => 0xff000000u | (hex >> 16 & 255) | (hex & 0xff00) | (hex & 255) << 16;

        private static readonly uint[] GlassMullions = [Rgb(0x42403c), Rgb(0x979390), Rgb(0x22262a), Rgb(0x445a6d), Rgb(0xc2bcb7), Rgb(0x6b5236)];
        private static readonly uint[] GridWalls = [Rgb(0x8c8a84), Rgb(0xb4ad9e), Rgb(0x9c8f7c), Rgb(0x7e8084), Rgb(0xc9c0ae), Rgb(0x8a7d6c), Rgb(0xa8a49a)];
        private static readonly uint[] BrickWalls = [Rgb(0x8a4a3c), Rgb(0x6e3a2f), Rgb(0x96654e), Rgb(0x4a3a3a), Rgb(0xa8866f), Rgb(0x7a4a30), Rgb(0xb09060)];
        private static readonly uint[] RibbonWalls = [Rgb(0xd6dcdc), Rgb(0x5f5c63), Rgb(0xd0cbc4), Rgb(0x9f9b8d)];

        private void Lot(Vector2 lo, Vector2 hi, float height, Vector2 front)
        {
            const float Gap = 0.6f; // between neighbours: an alley's shadow line, never a shared face
            lo += new Vector2(Gap);
            hi -= new Vector2(Gap);
            var size = hi - lo;
            if (size.X < 8 || size.Y < 8)
                return;
            var d = new Dice(_dice.Next());
            var b = new CityBuilding
            {
                At = (lo + hi) * 0.5f, Yaw = 0, Seed = d.Next(), Shops = d.Chance(0.8f),
                FloorHeight = d.Range(3.3f, 3.9f), Lit = d.Range(0.18f, 0.6f),
            };
            var half = size * 0.5f;
            var id = _buildings.Count;
            height = MathF.Max(height, 9);
            if (height > 70)
            {
                b.Style = (byte)(d.Unit() switch { < 0.6f => CityStyle.Glass, < 0.85f => CityStyle.Grid, _ => CityStyle.Ribbon });
                b.Wall = b.Style switch
                {
                    (byte)CityStyle.Glass => GlassMullions[d.Pick(GlassMullions.Length)],
                    (byte)CityStyle.Grid => GridWalls[d.Pick(GridWalls.Length)],
                    _ => RibbonWalls[d.Pick(RibbonWalls.Length)],
                };
                Tower(id, ref b, half, height, ref d);
            }
            else
            {
                b.Style = (byte)(height < 30 ? (d.Chance(0.6f) ? CityStyle.Brick : CityStyle.Grid) : d.Unit() switch { < 0.4f => CityStyle.Brick, < 0.75f => CityStyle.Grid, _ => CityStyle.Ribbon });
                b.Wall = b.Style switch
                {
                    (byte)CityStyle.Brick => BrickWalls[d.Pick(BrickWalls.Length)],
                    (byte)CityStyle.Grid => GridWalls[d.Pick(GridWalls.Length)],
                    _ => RibbonWalls[d.Pick(RibbonWalls.Length)],
                };
                MidRise(id, ref b, half, height, ref d);
                Billboard(id, half, front, ref d);
            }
            Entrance(id, half, front, b.Shops, ref d);
            if (b.Shops && d.Chance(0.85f))
            {
                Blade(id, half, front, ref d, 1);
                if (d.Chance(0.4f))
                    Blade(id, half, front, ref d, -1);
            }
            _buildings.Add(b);
        }

        private void Add(int id, Vector2 at, Vector2 half, float bottom, float top, CityStyle style, uint color, byte shape = 0, float cut = 0, bool detail = false, bool solid = true)
            => _masses.Add(new CityMass
            {
                Building = id, At = at, Half = half, Bottom = bottom, Top = top, Style = (byte)style, Color = color, Shape = shape, Cut = cut,
                Detail = detail, Solid = solid,
            });

        /// <summary>Snaps a height to whole floors over a ground floor of shops.</summary>
        private static float Floors(float h, float floor) => 4.8f + MathF.Max(1, MathF.Round((h - 4.8f) / floor)) * floor;

        private void Tower(int id, ref CityBuilding b, Vector2 half, float height, ref Dice d)
        {
            // A podium filling the lot (shops and a lobby, three to five floors), then the shaft set back from it, stepping in
            // once or twice on the way up, and a crown of plant rooms.
            var podium = Floors(d.Range(10, 18), b.FloorHeight);
            var podStyle = d.Chance(0.5f) ? CityStyle.Grid : (CityStyle)b.Style;
            var podColor = podStyle == CityStyle.Grid && b.Style != (byte)CityStyle.Grid ? GridWalls[d.Pick(GridWalls.Length)] : b.Wall;
            Add(id, Vector2.Zero, half, 0, podium, podStyle, podColor);
            Cornice(id, Vector2.Zero, half, podium, podColor);
            var shaftHalf = half - new Vector2(d.Range(2.5f, 6f), d.Range(2.5f, 6f));
            shaftHalf = Vector2.Max(shaftHalf, new Vector2(6));
            // Square-ish lots take a round or cut-cornered shaft now and then: fewer boxes on the skyline.
            var ratio = shaftHalf.X / shaftHalf.Y;
            byte shape = 0;
            if (ratio is > 0.75f and < 1.33f && d.Chance(0.25f))
            {
                shape = 2;
                shaftHalf = new Vector2(MathF.Min(shaftHalf.X, shaftHalf.Y));
            }
            else if (d.Chance(0.35f))
                shape = 1;
            var top = Floors(height, b.FloorHeight);
            var steps = top > 120 ? d.Pick(3) + 1 : d.Pick(2);
            var bottom = podium;
            var h = shaftHalf;
            for (var k = 0; k <= steps; k++)
            {
                var upTo = k == steps ? top : Floors(podium + (top - podium) * ((k + 1f) / (steps + 1) + d.Range(-0.08f, 0.08f)), b.FloorHeight);
                upTo = MathF.Min(upTo, top);
                if (upTo <= bottom + b.FloorHeight)
                    continue;
                var cut = shape == 1 ? MathF.Min(h.X, h.Y) * 0.3f : 0;
                Add(id, Vector2.Zero, h, bottom - 0.05f, upTo, (CityStyle)b.Style, b.Wall, shape, cut);
                if (upTo < top)
                    Cornice(id, Vector2.Zero, h, upTo, b.Wall, shape, cut);
                bottom = upTo;
                h = Vector2.Max(h * d.Range(0.72f, 0.88f), new Vector2(5));
            }
            // The crown: a plant room set in, a parapet, an aerial with a lamp on the tallest.
            var crown = h * 0.75f;
            var roofColor = Rgb(0x5a5e63);
            var crownTop = bottom + d.Range(4, 7);
            Add(id, Vector2.Zero, crown, bottom - 0.05f, crownTop, CityStyle.Plain, roofColor, shape, shape == 1 ? MathF.Min(crown.X, crown.Y) * 0.3f : 0);
            // The skyline: towers end differently. A third keep the flat plant room (and maybe a pad), the rest step in,
            // rise to a pyramid or a spire, or wear a lit crown.
            var kind = top > 90 ? d.Pick(4) : 0;
            if (kind == 1)
            {
                // Stepped: two shrinking tiers over the plant room.
                var t2 = crown * 0.7f;
                Add(id, Vector2.Zero, t2, crownTop - 0.05f, crownTop + 5, CityStyle.Plain, Rgb(0x6a6e73), shape, shape == 1 ? MathF.Min(t2.X, t2.Y) * 0.3f : 0);
                Add(id, Vector2.Zero, t2 * 0.6f, crownTop + 4.95f, crownTop + 9, CityStyle.Plain, Rgb(0x7a7e83), shape, shape == 1 ? MathF.Min(t2.X, t2.Y) * 0.18f : 0);
                crownTop += 1000; // (no pad: the top tier is too small for one)
            }
            else if (kind == 2 && shape == 0)
            {
                // A pyramid roof, dark metal, a lamp at its point.
                var ph = MathF.Min(crown.X, crown.Y) * d.Range(0.9f, 1.6f);
                Add(id, Vector2.Zero, crown, crownTop - 0.05f, crownTop + ph, CityStyle.Roof, Rgb(0x3a4248), 3, 0.08f);
                Add(id, Vector2.Zero, new Vector2(0.5f), crownTop + ph - 0.2f, crownTop + ph + 0.6f, CityStyle.Beacon, Rgb(0xff2020), solid: false);
                crownTop += 1000; // (no pad on a point)
            }
            else if (kind == 3)
            {
                // A lit crown: a band of light round the top, the tower's name at night.
                Add(id, Vector2.Zero, crown + new Vector2(0.15f), crownTop - 1.6f, crownTop - 0.2f, CityStyle.Lamp, d.Chance(0.5f) ? Rgb(0x8ad0ff) : Rgb(0xffd49a), shape,
                    shape == 1 ? MathF.Min(crown.X, crown.Y) * 0.3f + 0.1f : 0, detail: false, solid: false);
            }
            // A landing pad on the crown of a tower tall and broad enough (the air taxis come and go from these).
            var padR = MathF.Min(crown.X, crown.Y) - 0.3f;
            if (top > 80 && padR >= 5.5f && crownTop < top + 900 && d.Chance(0.8f))
            {
                padR = MathF.Min(padR, 11f);
                Add(id, Vector2.Zero, new Vector2(padR), crownTop - 0.05f, crownTop + 0.35f, CityStyle.Pad, Rgb(0x3a3d40), 2);
                _pads.Add(new CityPad { At = b.At, Height = crownTop + 0.35f - 0.3f, Radius = padR });
            }
            RoofClutter(id, h, bottom, ref d, false);
            if (top > 150 && d.Chance(0.3f))
            {
                var mast = d.Range(14, 40);
                Add(id, Vector2.Zero, new Vector2(0.35f), bottom, bottom + 7 + mast, CityStyle.Metal, Rgb(0x9a9a9a), detail: true, solid: false);
                Add(id, Vector2.Zero, new Vector2(0.6f), bottom + 7 + mast, bottom + 7.8f + mast, CityStyle.Beacon, Rgb(0xff2020), detail: false, solid: false);
            }
        }

        private void MidRise(int id, ref CityBuilding b, Vector2 half, float height, ref Dice d)
        {
            var top = Floors(height, b.FloorHeight);
            // Some step back a few metres above a cornice line (a setback storey), most rise straight to a heavy cornice.
            if (top > 26 && d.Chance(0.35f))
            {
                var at = Floors(top * d.Range(0.55f, 0.8f), b.FloorHeight);
                Add(id, Vector2.Zero, half, 0, at, (CityStyle)b.Style, b.Wall);
                Cornice(id, Vector2.Zero, half, at, b.Wall);
                var inner = half - new Vector2(d.Range(2, 4));
                Add(id, Vector2.Zero, inner, at - 0.05f, top, (CityStyle)b.Style, b.Wall);
                Cornice(id, Vector2.Zero, inner, top, b.Wall);
                RoofClutter(id, inner, top, ref d, true);
            }
            else
            {
                Add(id, Vector2.Zero, half, 0, top, (CityStyle)b.Style, b.Wall);
                Cornice(id, Vector2.Zero, half, top, b.Wall);
                if (b.Style == (byte)CityStyle.Brick && d.Chance(0.5f))
                {
                    // An old brick block: a hipped slate roof over its cornice instead of a flat one.
                    var rh = MathF.Min(half.X, half.Y) * d.Range(0.5f, 0.8f);
                    Add(id, Vector2.Zero, half + new Vector2(0.2f), top + 0.1f, top + rh, CityStyle.Roof, Rgb(0x3e4247), 3, 0.25f);
                }
                else
                    RoofClutter(id, half, top, ref d, true);
            }
        }

        /// <summary>The way in, marked (buildings.md rule 4): a canopy over the door in the middle of the front, a lamp under it;
        /// a lobby without shops gets a deeper canopy on two posts.</summary>
        private void Entrance(int id, Vector2 half, Vector2 front, bool shops, ref Dice d)
        {
            var along = new Vector2(MathF.Abs(front.Y), MathF.Abs(front.X));
            var reach = front.X != 0 ? half.X : half.Y;
            var deep = shops ? 1.1f : 1.3f; // (posts stay by the wall: the pavement's walkers pass outside them)
            var at = front * (reach + deep * 0.5f);
            var hs = front.X != 0 ? new Vector2(deep * 0.5f, 2.2f) : new Vector2(2.2f, deep * 0.5f);
            var colour = shops ? Rgb(0x2c2f33) : Rgb(d.Pick(3) switch { 0 => 0x3a3d42, 1 => 0x6b2a2a, _ => 0x2a4a3a });
            Add(id, at, hs, 4.1f, 4.35f, CityStyle.Plain, colour, detail: true, solid: false);
            var lampAt = front * (reach + 0.25f);
            var ls = front.X != 0 ? new Vector2(0.15f, 0.35f) : new Vector2(0.35f, 0.15f);
            Add(id, lampAt, ls, 3.85f, 4.08f, CityStyle.Lamp, Rgb(0xffd49a), detail: true, solid: false);
            if (!shops)
                foreach (var sgn in new[] { -1f, 1f })
                    Add(id, front * (reach + deep - 0.15f) + along * sgn * 2.0f, new Vector2(0.08f), 0.2f, 4.1f, CityStyle.Metal, Rgb(0x2c2f33), detail: true, solid: true);
        }

        /// <summary>A blade sign: a lit panel standing out from the front wall near a corner, over the shops.</summary>
        private void Blade(int id, Vector2 half, Vector2 front, ref Dice d, int end)
        {
            var colour = Rgb(d.Pick(5) switch { 0 => 0xff3060, 1 => 0x30d0ff, 2 => 0xffd030, 3 => 0x60ff80, _ => 0xc050ff });
            var along = new Vector2(MathF.Abs(front.Y), MathF.Abs(front.X)); // (along the front wall)
            var reach = front.X != 0 ? half.X : half.Y;
            var side = front.X != 0 ? half.Y : half.X;
            var at = front * (reach + 0.75f) + along * (side - 1.6f) * end;
            var hs = front.X != 0 ? new Vector2(0.65f, 0.12f) : new Vector2(0.12f, 0.65f);
            var h = d.Range(6f, 11f);
            Add(id, at, hs, 5.4f, 5.4f + h, CityStyle.Neon, colour, detail: true, solid: false);
        }

        /// <summary>A billboard on a mid-rise's roof, facing its street: a lit face on two steel legs.</summary>
        private void Billboard(int id, Vector2 half, Vector2 front, ref Dice d)
        {
            if (!d.Chance(0.22f) || half.X < 7 || half.Y < 7)
                return;
            if (_masses.Any(m => m.Building == id && m.Shape == 3))
                return; // (a hipped roof: nowhere flat to stand a billboard)
            var top = _masses.Where(m => m.Building == id).Max(m => m.Top);
            var colour = Rgb(d.Pick(4) switch { 0 => 0xffffff, 1 => 0xffe0b0, 2 => 0xb0e0ff, _ => 0xffb0d0 });
            var reach = (front.X != 0 ? half.X : half.Y) - 2.5f;
            var w = MathF.Min((front.X != 0 ? half.Y : half.X) - 2, 7);
            var at = front * reach;
            var hs = front.X != 0 ? new Vector2(0.12f, w) : new Vector2(w, 0.12f);
            Add(id, at, hs, top + 2.2f, top + 6.2f, CityStyle.Neon, colour, detail: true, solid: false);
            var along = new Vector2(MathF.Abs(front.Y), MathF.Abs(front.X));
            foreach (var sgn in new[] { -1f, 1f })
                Add(id, at - front * 0.3f + along * sgn * w * 0.7f, new Vector2(0.12f), top, top + 2.2f, CityStyle.Metal, Rgb(0x3a3a3a), detail: true, solid: false);
        }

        /// <summary>A band round a mass's top, standing out from its walls; with a parapet over a roof people can reach.</summary>
        private void Cornice(int id, Vector2 at, Vector2 half, float top, uint color, byte shape = 0, float cut = 0)
        {
            var c = Darken(color, 0.85f);
            Add(id, at, half + new Vector2(0.35f), top - 0.7f, top + 0.15f, CityStyle.Plain, c, shape, cut > 0 ? cut + 0.15f : 0, detail: true, solid: false);
        }

        private static uint Darken(uint c, float k)
        {
            uint Ch(int sh) => (uint)Math.Clamp((int)(((c >> sh) & 255) * k), 0, 255) << sh;
            return (c & 0xff000000u) | Ch(0) | Ch(8) | Ch(16);
        }

        /// <summary>What stands on a flat roof: a parapet, plant boxes, a lift's motor room, a water tank on the older blocks.</summary>
        private void RoofClutter(int id, Vector2 half, float top, ref Dice d, bool tank)
        {
            uint Parapet = Rgb(0x6b6e72), Plant = Rgb(0x8a8d90), TankColor = Rgb(0x5c4a3b);
            const float T = 0.3f, H = 1.1f;
            // The parapet: four thin walls just inside the cornice.
            Add(id, new Vector2(0, half.Y - T), new Vector2(half.X, T), top, top + H, CityStyle.Plain, Parapet, detail: true);
            Add(id, new Vector2(0, -half.Y + T), new Vector2(half.X, T), top, top + H, CityStyle.Plain, Parapet, detail: true);
            Add(id, new Vector2(half.X - T, 0), new Vector2(T, half.Y - 2 * T - 0.004f), top, top + H - 0.004f, CityStyle.Plain, Parapet, detail: true);
            Add(id, new Vector2(-half.X + T, 0), new Vector2(T, half.Y - 2 * T - 0.004f), top, top + H - 0.004f, CityStyle.Plain, Parapet, detail: true);
            var n = 1 + d.Pick(3);
            for (var k = 0; k < n; k++)
            {
                var hs = new Vector2(d.Range(1.2f, 3.5f), d.Range(1.2f, 3.5f));
                var room = half - hs - new Vector2(1.2f);
                if (room.X <= 0 || room.Y <= 0)
                    break;
                var at = new Vector2(d.Range(-room.X, room.X), d.Range(-room.Y, room.Y));
                Add(id, at, hs, top - 0.05f, top + d.Range(1.4f, 3.2f), CityStyle.Plain, Plant, detail: true);
            }
            // The roof's own kit, one or two of: a stair house with its door, a dish on a post, a mast with a red lamp,
            // a row of humming plant units (neighbours differ: buildings.md rule 2).
            var kit = d.Next();
            if ((kit & 1) != 0 && half.X > 4 && half.Y > 4)
            {
                var at = new Vector2(d.Range(-half.X + 3, half.X - 3), d.Range(-half.Y + 3, half.Y - 3));
                Add(id, at, new Vector2(1.6f, 1.3f), top - 0.05f, top + 2.8f, CityStyle.Plain, Rgb(0x7e7a72), detail: true);
                Add(id, at + new Vector2(0, 1.32f), new Vector2(0.5f, 0.02f), top + 0.05f, top + 2.1f, CityStyle.Metal, Rgb(0x2a3a4a), detail: true, solid: false);
            }
            if ((kit & 6) == 2 && half.X > 5 && half.Y > 5)
            {
                var at = new Vector2(d.Range(-half.X + 2.5f, half.X - 2.5f), d.Range(-half.Y + 2.5f, half.Y - 2.5f));
                Add(id, at, new Vector2(0.08f), top, top + 1.6f, CityStyle.Metal, Rgb(0x4a4a4a), detail: true, solid: false);
                Add(id, at, new Vector2(0.9f), top + 1.5f, top + 1.75f, CityStyle.Plain, Rgb(0xd8d8d2), 3, 0.45f, detail: true, solid: false);
            }
            else if ((kit & 6) == 4)
            {
                var at = new Vector2(d.Range(-half.X + 1.5f, half.X - 1.5f), d.Range(-half.Y + 1.5f, half.Y - 1.5f));
                var mh = d.Range(6, 12);
                Add(id, at, new Vector2(0.1f), top, top + mh, CityStyle.Metal, Rgb(0x9a9a9a), detail: true, solid: false);
                Add(id, at, new Vector2(0.22f), top + mh, top + mh + 0.35f, CityStyle.Beacon, Rgb(0xff2020), detail: false, solid: false);
            }
            if ((kit & 8) != 0 && half.X > 7)
                for (var k = 0; k < 3; k++)
                    Add(id, new Vector2(-half.X + 2.5f + k * 2.2f, -half.Y + 2.2f), new Vector2(0.9f, 0.7f), top - 0.05f, top + 1.1f, CityStyle.Plain, Rgb(0x9aa0a4), detail: true);
            if (tank && d.Chance(0.6f) && half.X > 5 && half.Y > 5)
            {
                var at = new Vector2(d.Range(-half.X + 4, half.X - 4), d.Range(-half.Y + 4, half.Y - 4));
                var r = d.Range(1.6f, 2.4f);
                for (var leg = 0; leg < 4; leg++)
                    Add(id, at + new Vector2(leg % 2 == 0 ? -r * 0.6f : r * 0.6f, leg < 2 ? -r * 0.6f : r * 0.6f), new Vector2(0.12f), top, top + 2.6f, CityStyle.Metal, Rgb(0x4a4a4a), detail: true, solid: false);
                Add(id, at, new Vector2(r), top + 2.6f, top + 2.6f + r * 1.8f, CityStyle.Plain, TankColor, 2, detail: true);
            }
        }
    }
}
