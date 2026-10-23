using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Foliage cover for bot sight (part of the concealment system, MapConcealment.cs): grass, bushes, shrubs and tree
/// canopies hide people from bots. Plants stay looks-only for movement and bullets; only the bots' line of sight
/// (<see cref="Map.Concealed"/>) reads this. The map's plants (every one, at full density, so the result doesn't depend
/// on anyone's Foliage setting) are rasterised once into a 1 m grid on a worker thread. Each cell keeps three layers,
/// each a height band and an opacity per metre: grass (mean blade top), brush (bushes and shrubs, their highest top)
/// and canopy (tree crowns, from the lowest leaves to the highest). A sight line walks the cells it crosses (a 2D DDA)
/// and adds up, per layer, the length it runs inside the band times the opacity. Tree trunks are solid boxes already.
/// </summary>
public sealed partial class Map
{
    /// <summary>
    /// Metres of fully dense foliage a sight line has to cross before it counts as hidden (bush leaves are about 1 per
    /// metre, so a bush 1 m thick hides; thin grass is a fraction, so it takes a few metres of it).
    /// </summary>
    public const float FoliageCoverDepth = 1.0f;

    /// <summary>Grid cell size (metres).</summary>
    public const float FoliageCoverCell = 1f;

    // Plant shapes (scale 1; see FoliageMeshes.cs), as cover.
    internal const float GrassTop = 0.6f; // blade tips (5 blades of 0.5-0.62 m)
    internal const float GrassArea = 0.126f; // m2 of solid-looking cover per clump (a 0.2 m radius tuft)
    internal const float BushTop = 0.9f, BushRadius = 0.6f, BushOpacity = 1f; // a leaf ball ~1.2 m wide, ~0.96 m tall
    internal const float ShrubTop = 0.65f, ShrubRadius = 0.6f, ShrubOpacity = 0.55f; // fronds: see-through-ish
    internal const float TreeLow = 0.45f, TreeRadius = 0.27f, TreeOpacity = 0.75f; // broadleaf crown, per unit of height
    internal const float PineLow = 0.22f, PineRadius = 0.18f, PineOpacity = 0.85f; // stacked cones (an average radius)

    /// <summary>Self-test switch: bots ignore foliage (to measure what the cover costs). Never set in the game.</summary>
    public static bool FoliageCoverDisabled;

    private FoliageCover? _foliageCover;
    private Task<FoliageCover>? _foliageCoverTask;
    private readonly object _foliageCoverLock = new();

    /// <summary>One layer of a cell: cover from <see cref="Base"/> to <see cref="Top"/> (world y) at <see cref="Opacity"/> per metre.</summary>
    public struct CoverLayer
    {
        public float Base, Top, Opacity;
    }

    /// <summary>One grid cell's cover.</summary>
    public struct CoverCell
    {
        public CoverLayer Grass, Brush, Canopy;
    }

    /// <summary>The built grid (read-only once built, so queries need no lock).</summary>
    public sealed class FoliageCover
    {
        public float X0, Z0;
        public int Nx, Nz;
        public float MinY = float.MaxValue, MaxY = float.MinValue;
        public CoverCell[] Cells = [];
        public double BuildMs;
        public int Plants;

        /// <summary>How many cells have any cover (self-test statistics).</summary>
        public int CoveredCells;

        public ref CoverCell At(int x, int z) => ref Cells[z * Nx + x];

        /// <summary>The cell under (x, z), or -1 outside the grid.</summary>
        public int IndexOf(float x, float z)
        {
            var ix = (int)MathF.Floor((x - X0) / FoliageCoverCell);
            var iz = (int)MathF.Floor((z - Z0) / FoliageCoverCell);
            return (uint)ix < (uint)Nx && (uint)iz < (uint)Nz ? iz * Nx + ix : -1;
        }

        public static FoliageCover Build(FoliageInstance[] plants)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var g = new FoliageCover { Plants = plants.Length };
            if (plants.Length == 0)
                return g;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var f in plants)
            {
                var r = Reach(f);
                x0 = MathF.Min(x0, f.Position.X - r);
                z0 = MathF.Min(z0, f.Position.Z - r);
                x1 = MathF.Max(x1, f.Position.X + r);
                z1 = MathF.Max(z1, f.Position.Z + r);
            }
            g.X0 = MathF.Floor(x0) - 1;
            g.Z0 = MathF.Floor(z0) - 1;
            g.Nx = Math.Max(1, (int)MathF.Ceiling((x1 - g.X0) / FoliageCoverCell) + 1);
            g.Nz = Math.Max(1, (int)MathF.Ceiling((z1 - g.Z0) / FoliageCoverCell) + 1);
            var cells = g.Cells = new CoverCell[g.Nx * g.Nz];
            var grassWeight = new float[cells.Length]; // for the mean blade top
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i].Grass.Base = cells[i].Brush.Base = cells[i].Canopy.Base = float.MaxValue;
                cells[i].Grass.Top = cells[i].Brush.Top = cells[i].Canopy.Top = float.MinValue;
            }

            foreach (var f in plants)
            {
                var p = f.Position;
                var s = f.Scale;
                switch (f.Type)
                {
                    case FoliageType.Grass:
                    {
                        var i = g.IndexOf(p.X, p.Z);
                        if (i < 0)
                            break;
                        ref var l = ref cells[i].Grass;
                        var w = GrassArea * s * s;
                        l.Opacity += w / (FoliageCoverCell * FoliageCoverCell);
                        l.Base = MathF.Min(l.Base, p.Y - 0.05f);
                        // Top accumulates a weighted sum here and becomes the mean below.
                        if (grassWeight[i] == 0)
                            l.Top = 0;
                        l.Top += (p.Y + GrassTop * s) * w;
                        grassWeight[i] += w;
                        break;
                    }
                    case FoliageType.Bush:
                        Disc(g, p.X, p.Z, BushRadius * s, BushOpacity, p.Y - 0.05f, p.Y + BushTop * s, layer: 1);
                        break;
                    case FoliageType.Shrub:
                        Disc(g, p.X, p.Z, ShrubRadius * s, ShrubOpacity, p.Y - 0.05f, p.Y + ShrubTop * s, layer: 1);
                        break;
                    case FoliageType.Tree:
                        Disc(g, p.X, p.Z, TreeRadius * s, TreeOpacity, p.Y + TreeLow * s, p.Y + s, layer: 2);
                        break;
                    case FoliageType.Pine:
                        Disc(g, p.X, p.Z, PineRadius * s, PineOpacity, p.Y + PineLow * s, p.Y + s, layer: 2);
                        break;
                }
            }

            for (var i = 0; i < cells.Length; i++)
            {
                ref var c = ref cells[i];
                if (grassWeight[i] > 0)
                    c.Grass.Top /= grassWeight[i];
                var any = false;
                any |= Finish(g, ref c.Grass);
                any |= Finish(g, ref c.Brush);
                any |= Finish(g, ref c.Canopy);
                if (any)
                    g.CoveredCells++;
            }
            g.BuildMs = clock.Elapsed.TotalMilliseconds;
            return g;
        }

        private static float Reach(in FoliageInstance f) => f.Type switch
        {
            FoliageType.Bush => BushRadius * f.Scale,
            FoliageType.Shrub => ShrubRadius * f.Scale,
            FoliageType.Tree => TreeRadius * f.Scale,
            FoliageType.Pine => PineRadius * f.Scale,
            _ => 0.3f,
        };

        /// <summary>Clamps a layer's opacity to 1 and records the grid's height range; false for an empty layer.</summary>
        private static bool Finish(FoliageCover g, ref CoverLayer l)
        {
            if (l.Opacity <= 0 || l.Top <= l.Base)
            {
                l = default;
                return false;
            }
            l.Opacity = MathF.Min(1f, l.Opacity);
            g.MinY = MathF.Min(g.MinY, l.Base);
            g.MaxY = MathF.Max(g.MaxY, l.Top);
            return true;
        }

        /// <summary>Adds a disc of cover (centre, radius, opacity, band) to the brush (1) or canopy (2) layer, by 4 x 4 samples per cell.</summary>
        private static void Disc(FoliageCover g, float cx, float cz, float r, float opacity, float y0, float y1, int layer)
        {
            const int sub = 4;
            const float step = FoliageCoverCell / sub;
            int ix0 = (int)MathF.Floor((cx - r - g.X0) / FoliageCoverCell), ix1 = (int)MathF.Floor((cx + r - g.X0) / FoliageCoverCell);
            int iz0 = (int)MathF.Floor((cz - r - g.Z0) / FoliageCoverCell), iz1 = (int)MathF.Floor((cz + r - g.Z0) / FoliageCoverCell);
            var r2 = r * r;
            var total = 0;
            for (var iz = Math.Max(0, iz0); iz <= Math.Min(g.Nz - 1, iz1); iz++)
                for (var ix = Math.Max(0, ix0); ix <= Math.Min(g.Nx - 1, ix1); ix++)
                {
                    var hits = 0;
                    for (var sz = 0; sz < sub; sz++)
                        for (var sx = 0; sx < sub; sx++)
                        {
                            var dx = g.X0 + ix * FoliageCoverCell + (sx + 0.5f) * step - cx;
                            var dz = g.Z0 + iz * FoliageCoverCell + (sz + 0.5f) * step - cz;
                            if (dx * dx + dz * dz <= r2)
                                hits++;
                        }
                    if (hits == 0)
                        continue;
                    total += hits;
                    Add(ref Layer(ref g.At(ix, iz), layer), opacity * hits / (sub * sub), y0, y1);
                }
            if (total == 0)
            {
                // Smaller than a sample spacing: its own cell gets its area's worth.
                var i = g.IndexOf(cx, cz);
                if (i >= 0)
                    Add(ref Layer(ref g.Cells[i], layer), opacity * MathF.PI * r2 / (FoliageCoverCell * FoliageCoverCell), y0, y1);
            }
        }

        private static ref CoverLayer Layer(ref CoverCell c, int layer)
        {
            if (layer == 1)
                return ref c.Brush;
            return ref c.Canopy;
        }

        private static void Add(ref CoverLayer l, float opacity, float y0, float y1)
        {
            l.Opacity += opacity;
            l.Base = MathF.Min(l.Base, y0);
            l.Top = MathF.Max(l.Top, y1);
        }

        /// <summary>
        /// Metres-of-dense-foliage along the segment (unit <paramref name="dir"/>, length <paramref name="dist"/>), stopping
        /// early once it reaches <paramref name="limit"/>. Walks the 1 m cells the segment crosses; no allocation.
        /// </summary>
        public float Depth(Vector3 from, Vector3 dir, float dist, float limit)
        {
            if (Cells.Length == 0)
                return 0f;
            var to = from + dir * dist;
            if (MathF.Min(from.Y, to.Y) > MaxY || MathF.Max(from.Y, to.Y) < MinY)
                return 0f;
            // Clip the segment (parameter t in metres) to the grid's rectangle.
            float t0 = 0f, t1 = dist;
            float gx1 = X0 + Nx * FoliageCoverCell, gz1 = Z0 + Nz * FoliageCoverCell;
            if (!Clip(from.X, dir.X, X0, gx1, ref t0, ref t1) || !Clip(from.Z, dir.Z, Z0, gz1, ref t0, ref t1))
                return 0f;
            var start = from + dir * t0;
            var ix = Math.Clamp((int)MathF.Floor((start.X - X0) / FoliageCoverCell), 0, Nx - 1);
            var iz = Math.Clamp((int)MathF.Floor((start.Z - Z0) / FoliageCoverCell), 0, Nz - 1);
            int stepX = dir.X > 0 ? 1 : -1, stepZ = dir.Z > 0 ? 1 : -1;
            var adx = MathF.Abs(dir.X);
            var adz = MathF.Abs(dir.Z);
            var dtX = adx > 1e-7f ? FoliageCoverCell / adx : float.PositiveInfinity;
            var dtZ = adz > 1e-7f ? FoliageCoverCell / adz : float.PositiveInfinity;
            var nextX = adx > 1e-7f ? ((X0 + (ix + (stepX > 0 ? 1 : 0)) * FoliageCoverCell) - from.X) / dir.X : float.PositiveInfinity;
            var nextZ = adz > 1e-7f ? ((Z0 + (iz + (stepZ > 0 ? 1 : 0)) * FoliageCoverCell) - from.Z) / dir.Z : float.PositiveInfinity;
            var depth = 0f;
            var t = t0;
            for (var guard = 0; guard < Nx + Nz + 4 && t < t1; guard++)
            {
                var tEnd = MathF.Min(t1, MathF.Min(nextX, nextZ));
                if (tEnd > t)
                {
                    ref readonly var c = ref Cells[iz * Nx + ix];
                    if (c.Grass.Opacity > 0)
                        depth += Band(c.Grass, from.Y, dir.Y, t, tEnd);
                    if (c.Brush.Opacity > 0)
                        depth += Band(c.Brush, from.Y, dir.Y, t, tEnd);
                    if (c.Canopy.Opacity > 0)
                        depth += Band(c.Canopy, from.Y, dir.Y, t, tEnd);
                    if (depth >= limit)
                        return depth;
                }
                t = tEnd;
                if (nextX < nextZ)
                {
                    ix += stepX;
                    nextX += dtX;
                    if ((uint)ix >= (uint)Nx)
                        break;
                }
                else
                {
                    iz += stepZ;
                    nextZ += dtZ;
                    if ((uint)iz >= (uint)Nz)
                        break;
                }
            }
            return depth;
        }

        /// <summary>Opacity times the length of [ta, tb] (metres along the ray) during which the ray's height is inside the layer's band.</summary>
        private static float Band(in CoverLayer l, float y0, float dy, float ta, float tb)
        {
            if (MathF.Abs(dy) < 1e-6f)
                return y0 >= l.Base && y0 <= l.Top ? (tb - ta) * l.Opacity : 0f;
            float a = (l.Base - y0) / dy, b = (l.Top - y0) / dy;
            if (a > b)
                (a, b) = (b, a);
            var len = MathF.Min(b, tb) - MathF.Max(a, ta);
            return len > 0 ? len * l.Opacity : 0f;
        }

        private static bool Clip(float o, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (MathF.Abs(d) < 1e-7f)
                return o >= lo && o <= hi;
            float a = (lo - o) / d, b = (hi - o) / d;
            if (a > b)
                (a, b) = (b, a);
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            return t0 < t1;
        }
    }

    /// <summary>
    /// Starts building the foliage cover grid on a worker thread (the server calls it once bots are ticking; it returns at
    /// once if the grid is built, building, or the map has no plants). A query before it's done waits for it, so bots
    /// always see the same thing on every run.
    /// </summary>
    public void PrepareFoliageCover()
    {
        if (Volatile.Read(ref _foliageCover) != null || FoliageInstances.Length == 0)
            return;
        lock (_foliageCoverLock)
        {
            if (_foliageCover == null && _foliageCoverTask == null)
            {
                var plants = FoliageInstances;
                _foliageCoverTask = Task.Run(() => FoliageCover.Build(plants));
            }
        }
    }

    /// <summary>The foliage cover grid (built, or waited for), or null on a map without plants.</summary>
    public FoliageCover? FoliageCoverGrid()
    {
        var ready = Volatile.Read(ref _foliageCover);
        if (ready != null)
            return ready;
        if (FoliageInstances.Length == 0)
            return null;
        PrepareFoliageCover();
        lock (_foliageCoverLock)
        {
            if (_foliageCover == null)
            {
                var built = _foliageCoverTask!.GetAwaiter().GetResult();
                _foliageCoverTask = null;
                Volatile.Write(ref _foliageCover, built);
                Console.WriteLine($"Map '{Name}': foliage cover for bots: {built.Nx} x {built.Nz} cells ({built.CoveredCells} covered) from {built.Plants} plants in {built.BuildMs:0.0} ms");
            }
            return _foliageCover;
        }
    }

    /// <summary>The plants changed (they were placed again): the next query builds the grid again.</summary>
    private void ResetFoliageCover()
    {
        lock (_foliageCoverLock)
        {
            _foliageCover = null;
            _foliageCoverTask = null;
        }
    }

    /// <summary>
    /// Metres of dense foliage on the sight line from <paramref name="from"/> to <paramref name="to"/> (0 on a map without
    /// plants), counted up to <paramref name="limit"/>. Server side only (bots); no allocation.
    /// </summary>
    public float FoliageDepth(Vector3 from, Vector3 to, float limit = float.MaxValue)
    {
        if (FoliageInstances.Length == 0 || FoliageCoverDisabled)
            return 0f;
        var d = to - from;
        var dist = d.Length();
        if (dist < 1e-4f)
            return 0f;
        return FoliageCoverGrid()!.Depth(from, d / dist, dist, limit);
    }

    /// <summary>The foliage cover at (x, z) at height y: the summed opacity of the layers whose band holds y (0 = open).</summary>
    public float FoliageCoverAt(float x, float z, float y)
    {
        if (FoliageInstances.Length == 0)
            return 0f;
        var g = FoliageCoverGrid()!;
        var i = g.IndexOf(x, z);
        if (i < 0)
            return 0f;
        ref readonly var c = ref g.Cells[i];
        var o = 0f;
        if (c.Grass.Opacity > 0 && y >= c.Grass.Base && y <= c.Grass.Top)
            o += c.Grass.Opacity;
        if (c.Brush.Opacity > 0 && y >= c.Brush.Base && y <= c.Brush.Top)
            o += c.Brush.Opacity;
        if (c.Canopy.Opacity > 0 && y >= c.Canopy.Base && y <= c.Canopy.Top)
            o += c.Canopy.Opacity;
        return o;
    }
}
