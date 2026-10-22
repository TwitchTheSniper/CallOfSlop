using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Concealment: cosmetic props marked <c>"concealment": true</c> (cornfields, tall crops) hide people from bots, and so
/// do the map's plants (grass, bushes, shrubs, tree canopies: FoliageCover.cs). They stay looks-only for movement,
/// bullets and grenades; only the bots' line of sight checks them (Bots.cs, BotFoliage.cs). A sight line is hidden when
/// it runs through at least <see cref="ConcealDepth"/> metres of concealing props, <see cref="FoliageCoverDepth"/> metres
/// of dense foliage, or a mix of both adding up to as much (each counted as a share of its own threshold), so someone a
/// step inside a cornfield or a bush is hidden from the road, while a bot right next to you (closer than
/// <see cref="ConcealSeeRange"/>) still sees you. The props' world bounds go in a coarse grid built on first use, so a
/// query only tests the few props near the line. Maps without concealing props never build it and answer false at once.
/// </summary>
public sealed partial class Map
{
    /// <summary>Metres of concealing props a sight line has to cross before it counts as hidden.</summary>
    public const float ConcealDepth = 1.2f;

    /// <summary>Within this distance a bot sees through concealment anyway (you can make someone out an arm's length away).</summary>
    public const float ConcealSeeRange = 2.5f;

    private const float ConcealCell = 8f;

    private ConcealGrid? _conceal;

    private sealed class ConcealGrid
    {
        public Box[] Boxes = [];
        public int[][] Cells = [];
        public int Nx, Nz;
        public float X0, Z0;
        public Vector3 Min, Max;
        public int[] Stamp = [];
        public int Mark;
    }

    /// <summary>How many concealing props the map has (0 on maps without any).</summary>
    public int ConcealingProps => Conceal().Boxes.Length;

    /// <summary>
    /// Is the sight line from <paramref name="from"/> to <paramref name="to"/> hidden by concealing props (tall crops) or
    /// foliage? True when it crosses at least <see cref="ConcealDepth"/> metres of the props, <see cref="FoliageCoverDepth"/>
    /// metres of dense foliage, or a mix adding up to as much, and the two points are further apart than
    /// <see cref="ConcealSeeRange"/>. Server side only (bots); no allocation.
    /// </summary>
    public bool Concealed(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var dist = d.Length();
        if (dist < ConcealSeeRange)
            return false;
        // Foliage first (FoliageCover.cs); whatever share it leaves is what the props have to make up.
        var share = FoliageInstances.Length == 0 || FoliageCoverDisabled ? 0f : FoliageCoverGrid()!.Depth(from, d / dist, dist, FoliageCoverDepth) / FoliageCoverDepth;
        if (share >= 1f)
            return true;
        var need = ConcealDepth * (1f - share);
        var g = Conceal();
        if (g.Boxes.Length == 0)
            return false;
        // Quick reject: the segment's bounds against all the concealing props' bounds.
        Vector3 lo = Vector3.Min(from, to), hi = Vector3.Max(from, to);
        if (hi.X < g.Min.X || lo.X > g.Max.X || hi.Y < g.Min.Y || lo.Y > g.Max.Y || hi.Z < g.Min.Z || lo.Z > g.Max.Z)
            return false;
        var dir = d / dist;
        lock (g)
        {
            if (++g.Mark == int.MaxValue)
            {
                Array.Clear(g.Stamp);
                g.Mark = 1;
            }
            var depth = 0f;
            // Walk the segment in half-cell steps and test each new cell's props once.
            var steps = Math.Max(1, (int)MathF.Ceiling(dist / (ConcealCell * 0.5f)));
            int lastCx = int.MinValue, lastCz = int.MinValue;
            for (var s = 0; s <= steps; s++)
            {
                var p = from + d * (s / (float)steps);
                var cx = Math.Clamp((int)MathF.Floor((p.X - g.X0) / ConcealCell), 0, g.Nx - 1);
                var cz = Math.Clamp((int)MathF.Floor((p.Z - g.Z0) / ConcealCell), 0, g.Nz - 1);
                if (cx == lastCx && cz == lastCz)
                    continue;
                lastCx = cx;
                lastCz = cz;
                // the cell and its neighbours, so a line that cuts a cell corner between two samples isn't missed
                for (var oz = -1; oz <= 1; oz++)
                    for (var ox = -1; ox <= 1; ox++)
                    {
                        int ix = cx + ox, iz = cz + oz;
                        if ((uint)ix >= (uint)g.Nx || (uint)iz >= (uint)g.Nz)
                            continue;
                        foreach (var i in g.Cells[iz * g.Nx + ix])
                        {
                            if (g.Stamp[i] == g.Mark)
                                continue;
                            g.Stamp[i] = g.Mark;
                            depth += Inside(g.Boxes[i], from, dir, dist);
                            if (depth >= need)
                                return true;
                        }
                    }
            }
            return false;
        }
    }

    /// <summary>The length of the segment (origin, unit dir, length) inside the box.</summary>
    private static float Inside(in Box b, Vector3 o, Vector3 dir, float len)
    {
        float t0 = 0f, t1 = len;
        Vector3 min = b.Min, max = b.Max;
        for (var axis = 0; axis < 3; axis++)
        {
            float oa = o[axis], da = dir[axis];
            if (MathF.Abs(da) < 1e-6f)
            {
                if (oa < min[axis] || oa > max[axis])
                    return 0f;
                continue;
            }
            var inv = 1f / da;
            float a = (min[axis] - oa) * inv, c = (max[axis] - oa) * inv;
            if (a > c)
                (a, c) = (c, a);
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, c);
            if (t0 >= t1)
                return 0f;
        }
        return t1 - t0;
    }

    private ConcealGrid Conceal()
    {
        if (_conceal is { } ready)
            return ready;
        var boxes = new List<Box>();
        foreach (var p in PropInstances)
            if (p.Def.Concealment)
                boxes.Add(p.Bounds);
        var g = new ConcealGrid { Boxes = boxes.ToArray() };
        if (boxes.Count > 0)
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var b in boxes)
            {
                min = Vector3.Min(min, b.Min);
                max = Vector3.Max(max, b.Max);
            }
            g.Min = min;
            g.Max = max;
            g.X0 = min.X;
            g.Z0 = min.Z;
            g.Nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / ConcealCell));
            g.Nz = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / ConcealCell));
            var lists = new List<int>[g.Nx * g.Nz];
            for (var i = 0; i < lists.Length; i++)
                lists[i] = new List<int>();
            for (var i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                int x0 = Math.Clamp((int)MathF.Floor((b.Min.X - g.X0) / ConcealCell), 0, g.Nx - 1), x1 = Math.Clamp((int)MathF.Floor((b.Max.X - g.X0) / ConcealCell), 0, g.Nx - 1);
                int z0 = Math.Clamp((int)MathF.Floor((b.Min.Z - g.Z0) / ConcealCell), 0, g.Nz - 1), z1 = Math.Clamp((int)MathF.Floor((b.Max.Z - g.Z0) / ConcealCell), 0, g.Nz - 1);
                for (var z = z0; z <= z1; z++)
                    for (var x = x0; x <= x1; x++)
                        lists[z * g.Nx + x].Add(i);
            }
            g.Cells = lists.Select(l => l.ToArray()).ToArray();
            g.Stamp = new int[boxes.Count];
        }
        _conceal = g;
        return g;
    }
}
