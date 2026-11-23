using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--check-winding</c>: does every part shape (and every box of every part) draw its triangles wound outwards, counter-clockwise
/// seen from outside, as back-face culling of the construct pass needs (2026-11-23, the 500 fps work: construct boxes were drawn
/// with culling off). For each shape it counts the triangles whose winding disagrees with their vertex normal, those with no area,
/// and the edges used once only (an open mesh: its inside shows when culled). It also counts part boxes with a mirrored or flat
/// size. Prints one line a shape with a problem and a total. Exit code: the shapes with wrong winding.
/// </summary>
public static class WindingCheck
{
    public static int Cli()
    {
        _ = ConstructParts.All; // (loads every part file, and with them their shapes)
        const int fpv = PartShapes.FloatsPerVertex;
        int bad = 0, open = 0, shapes = 0;
        for (var id = 1; id < PartShapes.Count; id++)
        {
            var m = PartShapes.Mesh(id);
            if (m.Length == 0)
                continue;
            shapes++;
            int wrong = 0, flat = 0, tris = m.Length / (fpv * 3);
            var edges = new Dictionary<(long, long), int>();
            for (var t = 0; t < tris; t++)
            {
                var o = t * 3 * fpv;
                var a = new Vector3(m[o], m[o + 1], m[o + 2]);
                var b = new Vector3(m[o + fpv], m[o + fpv + 1], m[o + fpv + 2]);
                var c = new Vector3(m[o + 2 * fpv], m[o + 2 * fpv + 1], m[o + 2 * fpv + 2]);
                var n = (new Vector3(m[o + 3], m[o + 4], m[o + 5]) + new Vector3(m[o + fpv + 3], m[o + fpv + 4], m[o + fpv + 5]) + new Vector3(m[o + 2 * fpv + 3], m[o + 2 * fpv + 4], m[o + 2 * fpv + 5]));
                var g = Vector3.Cross(b - a, c - a);
                if (g.LengthSquared() < 1e-14f)
                {
                    flat++;
                    continue;
                }
                if (Vector3.Dot(g, n) < 0)
                    wrong++;
                Edge(edges, a, b);
                Edge(edges, b, c);
                Edge(edges, c, a);
            }
            var once = edges.Values.Count(v => v == 1);
            if (once > 0)
                open++;
            if (wrong > 0)
                bad++;
            if (wrong > 0 || once > 0)
                Console.WriteLine($"shape {id} {PartShapes.KindOf(id)}: {tris} triangles, {wrong} wound inwards, {flat} flat, {once} open edges");
        }
        var mirrored = 0;
        foreach (var d in ConstructParts.All)
            foreach (var box in d.Boxes)
                if (box.Half.X <= 0 || box.Half.Y <= 0 || box.Half.Z <= 0)
                    mirrored++;
        Console.WriteLine($"winding: {shapes} shapes, {bad} with triangles wound inwards, {open} open (edges used once); {mirrored} part boxes with a size of 0 or below");
        return bad;
    }

    private static long Key(Vector3 p) => (long)MathF.Round(p.X * 1e4f) * 73856093L ^ (long)MathF.Round(p.Y * 1e4f) * 19349663L ^ (long)MathF.Round(p.Z * 1e4f) * 83492791L;

    private static void Edge(Dictionary<(long, long), int> edges, Vector3 a, Vector3 b)
    {
        var (ka, kb) = (Key(a), Key(b));
        var k = ka < kb ? (ka, kb) : (kb, ka);
        edges[k] = edges.GetValueOrDefault(k) + 1;
    }
}
