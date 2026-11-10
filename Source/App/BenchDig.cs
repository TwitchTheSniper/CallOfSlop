using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-dig [--map NAME]</c>: what changing the ground costs (docs/sandbox/TERRAIN-DIGGING.md, "What it costs"). A command-line
/// benchmark, not a self-test section: it fails nothing. On a planetoid and on a flat streamed map (or the one named), with no Game: a work
/// site is dug with the ground drill's own ops (scoops along a line, fills, flatten pads, a ramp, craters), each kind timed per op; then what
/// the site costs afterwards, which is what a player feels: a drawing chunk's mesh made again (it is after every op that touches it), the
/// collision shapes gathered for a player and for a vehicle standing on it, and height queries and rays over it. Runs twice (the first
/// warms the JIT); read the last.
/// </summary>
public static partial class SelfTest
{
    public static int BenchDig(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var names = Arg("--map") is { } one ? [one] : new[] { "Planetoid", "Streamed Terrain" };
        foreach (var name in names)
        {
            if (Maps.Find(name) is not { } map || (map.Planetoid == null && map.Terrain == null))
            {
                Console.WriteLine($"[dig bench] no map \"{name}\" with ground to dig");
                return 1;
            }
            for (var run = 1; run <= 2; run++)
                BenchDigOn(map, run == 2);
        }
        return 0;
    }

    /// <summary>Counts what a mesh would hold, as the renderers' sinks do (a list of vertices and indices).</summary>
    private struct BenchDigSink : IDigFaceSink
    {
        public List<Vector4> Verts;
        public List<uint> Idx;

        public void Smooth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd) =>
            Quad(a, b, c, d, Vector3.Normalize(Vector3.Cross(c - a, d - b)), new Vector3(ca.X, ca.Y, ca.Z), ca.W);

        public bool Shaded => true;


        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 color, float rock)
        {
            var b0 = (uint)Verts.Count;
            Verts.Add(new Vector4(a, n.X));
            Verts.Add(new Vector4(b, n.Y));
            Verts.Add(new Vector4(c, n.Z));
            Verts.Add(new Vector4(d, rock + color.X));
            Idx.Add(b0);
            Idx.Add(b0 + 1);
            Idx.Add(b0 + 2);
            Idx.Add(b0);
            Idx.Add(b0 + 2);
            Idx.Add(b0 + 3);
        }
    }

    private static void BenchDigOn(Map map, bool print)
    {
        map.RestoreProps();
        World.SetMap(map);
        var pl = map.Planetoid;
        var t = map.Terrain;
        IDigGround ground = pl != null ? pl.Edits : t!.Edits;
        ground.Clear();
        var phys = World.Physics;
        var spawn = map.Spawns.Count > 0 ? map.Spawns[0] : Vector3.Zero;
        Vector3 UpAt(Vector3 p) => pl != null ? Vector3.Normalize(p - pl.Center) : Vector3.UnitY;
        var up = UpAt(spawn);
        var east = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var north = Vector3.Cross(east, up);
        Vector3 OnGround(Vector3 p)
        {
            var u = UpAt(p);
            var from = p + u * 60f;
            var d = phys.Raycast(from, -u, 400f, QueryFilter.All);
            return from - u * d;
        }
        var site = OnGround(spawn + east * 40f + north * 40f);
        void Line(string what, string numbers)
        {
            if (print)
                Console.WriteLine($"[dig bench] {map.Name,-16} {what,-34} {numbers}");
        }
        (double Mean, double Worst) Time(int n, Func<int, DigOp> op)
        {
            double sum = 0, worst = 0;
            for (var k = 0; k < n; k++)
            {
                var o = op(k).Quantised;
                var t0 = Stopwatch.GetTimestamp();
                ground.Apply(o);
                var us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
                sum += us;
                worst = Math.Max(worst, us);
            }
            return (sum / n, worst);
        }
        string Us((double Mean, double Worst) r) => $"{r.Mean,8:0.0} us an op, worst {r.Worst,8:0.0}";

        // The site: a drill held along a 30 m line and boring down, fills beside it, pads, a ramp, then craters.
        Line("scoops 0.8 m, new ground", Us(Time(120, k => new DigOp(DigKind.Scoop, OnGround(site + east * (k * 0.25f)) - up * 0.2f, 0.8f))));
        Line("scoops 0.8 m, boring down", Us(Time(120, k => new DigOp(DigKind.Scoop, site + east * (k % 8 * 0.3f) - up * (0.3f + k * 0.03f), 0.8f))));
        Line("fills 0.8 m", Us(Time(120, k => new DigOp(DigKind.FillDirt, OnGround(site + north * 6f + east * (k * 0.25f)) + up * 0.3f, 0.8f))));
        var pad = OnGround(site + north * 14f);
        Line("flatten pads 1.5 m", Us(Time(60, k => new DigOp(DigKind.GradeDirt, pad + east * k, 1.5f, To: pad + east * k))));
        var r0 = OnGround(site + north * 22f);
        Line("ramp 30 m", Us(Time(4, k => new DigOp(DigKind.GradeDirt, r0 + north * (k * 3.5f), 1.5f, To: r0 + north * (k * 3.5f) + east * 30f + up * 6f))));
        Line("clean holes 2 m", Us(Time(40, k => new DigOp(DigKind.SmoothDirt, OnGround(site + east * (k * 0.7f)), 2f))));
        Line("craters 6 m", Us(Time(12, k => new DigOp(DigKind.Crater, OnGround(site - north * 14f + east * (k * 5f)), 6f))));
        Line("crater 30 m", Us(Time(1, _ => new DigOp(DigKind.Crater, OnGround(site - north * 60f), 30f))));

        // What the site costs afterwards. A chunk's mesh, made as the renderers make it.
        var verts = new List<Vector4>(1 << 16);
        var idx = new List<uint>(1 << 16);
        var sink = new BenchDigSink { Verts = verts, Idx = idx };
        double meshWorst = 0, meshSum = 0;
        int chunks = 0, tris = 0, cells = 0;
        for (var pass = 0; pass < 3; pass++)
        {
            chunks = tris = 0;
            meshSum = meshWorst = 0;
            if (pl != null)
            {
                var e = pl.Edits;
                cells = e.TouchedCells;
                foreach (var key in e.Chunks)
                {
                    e.ChunkOf(key, out var face, out var i0, out var j0);
                    pl.DirOf(face, i0 + 16.0, j0 + 16.0, out var x, out var y, out var z);
                    verts.Clear();
                    idx.Clear();
                    var t0 = Stopwatch.GetTimestamp();
                    for (var j = j0; j < Math.Min(j0 + PlanetEdits.ChunkCells, pl.N); j++)
                    for (var i = i0; i < Math.Min(i0 + PlanetEdits.ChunkCells, pl.N); i++)
                        e.CellFaces(face, i, j, pl.Cx + x * pl.Radius, pl.Cy + y * pl.Radius, pl.Cz + z * pl.Radius, ref sink);
                    Count(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                }
            }
            else
            {
                var e = t!.Edits;
                cells = e.TouchedCells;
                for (var c = 0; c < e.ChunkVersion.Length; c++)
                {
                    if (e.ChunkVersion[c] == 0)
                        continue;
                    int cx = c % e.ChunksX, cz = c / e.ChunksX;
                    int i0 = cx * TerrainEdits.ChunkCells, i1 = Math.Min(i0 + TerrainEdits.ChunkCells, t.CountX - 1), j0 = cz * TerrainEdits.ChunkCells, j1 = Math.Min(j0 + TerrainEdits.ChunkCells, t.CountZ - 1);
                    verts.Clear();
                    idx.Clear();
                    var t0 = Stopwatch.GetTimestamp();
                    for (var j = j0; j < j1; j++)
                    for (var i = i0; i < i1; i++)
                        e.CellFaces(i, j, ref sink, smooth: true);
                    Count(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                }
            }
        }
        void Count(double ms)
        {
            if (idx.Count == 0)
                return;
            chunks++;
            tris += idx.Count / 3;
            meshSum += ms;
            meshWorst = Math.Max(meshWorst, ms);
        }
        Line("chunk mesh made again", $"{meshSum / Math.Max(chunks, 1),8:0.00} ms a chunk, worst {meshWorst,8:0.00}; {chunks} chunks, {tris} triangles, {cells} dug cells");

        // Collision shapes for a player and for a vehicle on the site, heights and rays over it.
        var shapes = new List<ContactShape>(4096);
        (double Us, int Shapes) Gather(Vector3 half)
        {
            const int n = 400;
            var found = 0;
            var t0 = Stopwatch.GetTimestamp();
            for (var k = 0; k < n; k++)
            {
                var p = site + east * (k % 40 * 0.7f) + north * (k / 40 * 0.9f - 2f) + up * 0.5f;
                shapes.Clear();
                phys.Gather(p - half, p + half, QueryFilter.All, shapes);
                found += shapes.Count;
            }
            return (Stopwatch.GetElapsedTime(t0).TotalMicroseconds / n, found / n);
        }
        Gather(new Vector3(1f));
        var g = Gather(new Vector3(0.6f, 1.2f, 0.6f));
        Line("gather, a player's box", $"{g.Us,8:0.0} us, {g.Shapes} shapes");
        g = Gather(new Vector3(3.5f, 2f, 3.5f));
        Line("gather, a vehicle's box", $"{g.Us,8:0.0} us, {g.Shapes} shapes");
        {
            const int n = 20000;
            var sum = 0f;
            var t0 = Stopwatch.GetTimestamp();
            for (var k = 0; k < n; k++)
            {
                var p = site + east * (k % 200 * 0.14f) + north * (k / 200 * 0.05f - 2f) + up * 2f;
                sum += pl != null ? pl.Altitude(p) : p.Y - t!.GroundBelow(p.X, p.Z, p.Y);
            }
            Line("height under a point", $"{Stopwatch.GetElapsedTime(t0).TotalNanoseconds / n,8:0} ns   ({sum / n:0.000} m mean)");
            sum = 0;
            t0 = Stopwatch.GetTimestamp();
            const int rays = 4000;
            for (var k = 0; k < rays; k++)
            {
                var p = site + east * (k % 100 * 0.28f) + north * (k / 100 * 0.1f - 2f) + up * 3f;
                sum += phys.Raycast(p, Vector3.Normalize(-up + east * 0.8f), 30f, QueryFilter.All);
            }
            Line("ray down onto the site", $"{Stopwatch.GetElapsedTime(t0).TotalMicroseconds / rays,8:0.00} us   ({sum / rays:0.000} m mean)");
        }
        Line("log", $"{ground.Ops.Count} ops");
        ground.Clear();
        map.RestoreProps();
    }
}

// TEMP (remove before the PR): a dug site for pictures, COS_DIG_DEMO=1 with --light-shots.
public static partial class SelfTest
{
    public static void DigDemo(Map map)
    {
        var pl = map.Planetoid;
        var t = map.Terrain;
        IDigGround ground = pl != null ? pl.Edits : t!.Edits;
        var phys = World.Physics;
        var spawn = map.Spawns.Count > 0 ? map.Spawns[0] : Vector3.Zero;
        Vector3 UpAt(Vector3 p) => pl != null ? Vector3.Normalize(p - pl.Center) : Vector3.UnitY;
        var up = UpAt(spawn);
        var east = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var north = Vector3.Cross(east, up);
        Vector3 OnGround(Vector3 p)
        {
            var u = UpAt(p);
            var from = p + u * 60f;
            var d = phys.Raycast(from, -u, 400f, QueryFilter.All);
            return from - u * d;
        }
        var site = OnGround(spawn + east * 40f + north * 40f);
        var ops = new List<DigOp>();
        void Do(DigOp op) => ops.Add(op.Quantised);
        for (var k = 0; k < 24; k++)
            Do(new DigOp(DigKind.Scoop, OnGround(site + east * (k * 0.3f)) - up * 0.1f, 0.8f)); // a drill swept along a line
        for (var k = 0; k < 30; k++)
            Do(new DigOp(DigKind.Scoop, OnGround(site + north * 5f) + east * (k % 6 * 0.4f) - up * (0.2f + k * 0.05f), 0.8f)); // held: boring down
        for (var k = 0; k < 14; k++)
            Do(new DigOp(DigKind.FillDirt, OnGround(site + north * 9f + east * (k * 0.5f)) + up * 0.2f, 0.8f)); // a bank laid along a line
        for (var k = 0; k < 6; k++)
            Do(new DigOp(DigKind.FillDirt, OnGround(site + north * 9f + east * (2f + k * 0.5f)) + up * 0.9f, 0.8f)); // and heaped
        var pad = OnGround(site + north * 14f) + up * 0.6f;
        for (var k = 0; k < 6; k++)
            Do(new DigOp(DigKind.GradeDirt, pad + east * k, 1.5f, To: pad + east * k));
        var r0 = OnGround(site + north * 19f);
        Do(new DigOp(DigKind.GradeDirt, r0, 1.5f, To: r0 + east * 14f + up * 4f));
        Do(new DigOp(DigKind.Crater, OnGround(site - north * 8f + east * 4f), 6f));
        Do(new DigOp(DigKind.Crater, OnGround(site - north * 8f + east * 16f), 3f));
        foreach (var op in ops)
            ground.Apply(op);
        var eye = site + up * 9f - east * 6f + north * 5f;
        Console.WriteLine(FormattableString.Invariant($"[dig demo] site {site.X:0.0},{site.Y:0.0},{site.Z:0.0} east {east} north {north} up {up}; {ground.Ops.Count} ops"));
    }
}
