using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--preview-shot NAME|FILE [--out OUT.png] [--views iso,back,side,top,front,...] [--cut Y] [--size N] [--links]</c>: the preview's model drawn
/// straight to a PNG by a small software rasteriser, with no window, browser or GPU: a sheet of views (by default the four corners at
/// three quarters, so every side of a part is seen), each tile fitted to the model. For an agent's own look at a part it is
/// designing in seconds: <c>--gen-parts NAME</c> (which copies what it writes into the build's data too), then this; no rebuild, no
/// gallery. Flat light from a sun and a fill, glass at a third, glow full bright; a design aid like the gallery, not the renderer.
/// </summary>
public static partial class ConstructPreview
{
    private static (List<Vector3> Pos, List<Vector3> Nrm, List<ushort> TriLook, List<(string Hex, float Glass, bool Glow)> Looks) _last;
    private static bool _shotLinks; // --links: the construct's links drawn too, bright by type (data yellow, power red, pipe blue, chute orange, the rest white)

    /// <summary>Each link segment as a square tube (at least 3 cm across, so a data cable shows) added to the shot's triangles, glowing in its type's colour.</summary>
    private static void LinkTris(Construct c)
    {
        var (pos, nrm, triLook, looks) = _last;
        Span<ushort> lookOf = stackalloc ushort[(int)RouteType.COUNT];
        for (var t = 0; t < (int)RouteType.COUNT; t++)
        {
            lookOf[t] = (ushort)looks.Count;
            looks.Add(((RouteType)t switch { RouteType.Data => "#f2d21b", RouteType.Power => "#e0342a", RouteType.Pipe => "#2f7fe0", RouteType.Chute => "#ff8a1c", _ => "#f4f4f4" }, 0f, true));
        }
        for (var r = 0; r < c.RouteCount; r++)
        {
            var type = c.Routes[r].Type;
            var rad = MathF.Max(ConstructRoutes.Radius(type), 0.015f);
            var a = ConstructRoutes.RestPoint(c, r, -1);
            for (var i = 0; i <= c.Routes[r].Count; i++)
            {
                var b = ConstructRoutes.RestPoint(c, r, i);
                var d = b - a;
                if (d.LengthSquared() > 1e-8f)
                {
                    var f = Vector3.Normalize(d);
                    var u = Vector3.Normalize(Vector3.Cross(f, MathF.Abs(f.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX)) * rad;
                    var v = Vector3.Cross(f, u);
                    for (var k = 0; k < 4; k++)
                    {
                        var (p0, p1) = k switch { 0 => (u + v, u - v), 1 => (u - v, -u - v), 2 => (-u - v, -u + v), _ => (-u + v, u + v) };
                        var n = Vector3.Normalize(p0 + p1);
                        Quad(a + p0, a + p1, b + p1, b + p0, n, lookOf[(int)type]);
                    }
                }
                a = b;
            }
        }
        _tris = triLook.Count;

        void Quad(Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3, Vector3 n, ushort look)
        {
            pos.Add(q0); pos.Add(q1); pos.Add(q2); nrm.Add(n); nrm.Add(n); nrm.Add(n); triLook.Add(look);
            pos.Add(q0); pos.Add(q2); pos.Add(q3); nrm.Add(n); nrm.Add(n); nrm.Add(n); triLook.Add(look);
        }
    }

    private static readonly Dictionary<string, Vector3> ShotViews = new(StringComparer.OrdinalIgnoreCase)
    {
        ["iso"] = new(1, 0.75f, 1.15f), // (front right, high: a part's front is +z, as furniture's back socket is at -z)
        ["iso2"] = new(-1, 0.75f, 1.15f),
        ["back"] = new(-1, 0.75f, -1.15f),
        ["back2"] = new(1, 0.75f, -1.15f),
        ["front"] = new(0, 0.15f, 1),
        ["bow"] = new(0, 0.15f, -1), // (a ship's bow is -z)
        ["side"] = new(-1, 0.05f, 0),
        ["right"] = new(1, 0.05f, 0),
        ["top"] = new(0, 1, 0),
        ["under"] = new(0, -1, 0),
    };

    private static int Shot(string what, string[] args)
    {
        var views = (Flag(args, "--views") ?? "iso,iso2,back,back2").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var v in views)
            if (!ShotViews.ContainsKey(v))
            {
                Console.WriteLine($"--preview-shot: no view \"{v}\"; views: {string.Join(", ", ShotViews.Keys)}");
                return 2;
            }
        var size = int.TryParse(Flag(args, "--size"), out var sz) ? Math.Clamp(sz, 64, 2048) : 600;
        float? cut = float.TryParse(Flag(args, "--cut"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cy) ? cy : null;
        var outFile = Flag(args, "--out") ?? Path.Combine(AppContext.BaseDirectory, "preview", Safe(Path.GetFileNameWithoutExtension(what)) + ".png");
        _shotLinks = args.Any(a => a.Equals("--links", StringComparison.OrdinalIgnoreCase));
        if (Export(what, out var bp, out var parts, out _) is null)
            return 1;
        var cols = views.Length == 1 ? 1 : 2;
        var rows = (views.Length + cols - 1) / cols;
        int w = size * cols, h = size * rows;
        var rgb = new byte[w * h * 3];
        for (var i = 0; i < rgb.Length; i += 3)
            (rgb[i], rgb[i + 1], rgb[i + 2]) = ((byte)213, (byte)218, (byte)222);
        for (var k = 0; k < views.Length; k++)
            DrawTile(rgb, w, (k % cols) * size, (k / cols) * size, size, ShotViews[views[k]], cut);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        MapKit.PngWriter.Write(outFile, w, h, rgb);
        Console.WriteLine($"shot {bp.Name}: {parts} parts, {_tris} triangles, views {string.Join(" ", views)} -> {Path.GetFullPath(outFile)}");
        return 0;
    }

    /// <summary>One view into a size-square tile at (ox, oy): orthographic, fitted to the model's bounds, z-buffered, glass last.</summary>
    private static void DrawTile(byte[] rgb, int stride, int ox, int oy, int size, Vector3 eye, float? cut)
    {
        var (pos, nrm, triLook, looks) = _last;
        if (pos.Count == 0)
            return;
        var d = Vector3.Normalize(eye);
        var up = MathF.Abs(d.Y) > 0.99f ? new Vector3(0, 0, -MathF.Sign(d.Y)) : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(up, d));
        var upv = Vector3.Cross(d, right);
        // Fit: the projected bounds of what is drawn, with a margin.
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        foreach (var v in pos)
        {
            if (cut is { } c0 && v.Y > c0)
                continue;
            float x = Vector3.Dot(v, right), y = Vector3.Dot(v, upv);
            (x0, x1, y0, y1) = (MathF.Min(x0, x), MathF.Max(x1, x), MathF.Min(y0, y), MathF.Max(y1, y));
        }
        if (x0 > x1)
            return;
        var scale = size * 0.9f / MathF.Max(0.01f, MathF.Max(x1 - x0, y1 - y0));
        float cx = (x0 + x1) / 2, cyy = (y0 + y1) / 2;
        var depth = new float[size * size];
        Array.Fill(depth, float.MinValue);
        var colours = looks.Select(l => new Vector3(Convert.ToInt32(l.Hex[1..3], 16), Convert.ToInt32(l.Hex[3..5], 16), Convert.ToInt32(l.Hex[5..7], 16)) / 255f).ToArray();
        var sun = Vector3.Normalize(new Vector3(0.5f, 1, 0.7f));
        var fill = Vector3.Normalize(new Vector3(-0.8f, 0.3f, -0.6f));
        for (var pass = 0; pass < 2; pass++) // (solids, then glass over them without writing depth)
            for (var t = 0; t < triLook.Count; t++)
            {
                var L = looks[triLook[t]];
                if ((L.Glass > 0) != (pass == 1))
                    continue;
                Vector3 a = pos[t * 3], b = pos[t * 3 + 1], c = pos[t * 3 + 2];
                if (cut is { } ct && a.Y > ct && b.Y > ct && c.Y > ct)
                    continue;
                var n = Vector3.Normalize(nrm[t * 3] + nrm[t * 3 + 1] + nrm[t * 3 + 2]);
                if (Vector3.Dot(n, d) < 0)
                    n = -n; // (double-sided, as the gallery draws)
                var col = colours[triLook[t]];
                var lit = L.Glow ? col * 1.3f : col * (0.38f + 0.62f * MathF.Max(0, Vector3.Dot(n, sun)) + 0.18f * MathF.Max(0, Vector3.Dot(n, fill)));
                Vector3 P(Vector3 v) => new((Vector3.Dot(v, right) - cx) * scale + size / 2f, size / 2f - (Vector3.Dot(v, upv) - cyy) * scale, Vector3.Dot(v, d));
                Raster(P(a), P(b), P(c), Vector3.Clamp(lit, Vector3.Zero, Vector3.One), pass == 1 ? 0.35f : 1f, rgb, stride, ox, oy, size, depth, pass == 0);
            }
    }

    private static void Raster(Vector3 a, Vector3 b, Vector3 c, Vector3 col, float alpha, byte[] rgb, int stride, int ox, int oy, int size, float[] depth, bool writeDepth)
    {
        var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(area) < 1e-6f)
            return;
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)))), maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)))), maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                var w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
                var w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
                var w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0)
                    continue;
                var z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                var di = y * size + x;
                if (z < depth[di] - 1e-4f)
                    continue;
                if (writeDepth)
                    depth[di] = z;
                var o = ((oy + y) * stride + ox + x) * 3;
                rgb[o] = (byte)(rgb[o] * (1 - alpha) + col.X * 255 * alpha);
                rgb[o + 1] = (byte)(rgb[o + 1] * (1 - alpha) + col.Y * 255 * alpha);
                rgb[o + 2] = (byte)(rgb[o + 2] * (1 - alpha) + col.Z * 255 * alpha);
            }
    }
}
