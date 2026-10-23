using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace CallOfSlop.MapKit;

/// <summary>
/// A top-down plan of a built map as a PNG: the outline, every solid box shaded by its top height (hidden colliders
/// bluer), props (amber), waypoints (cyan dots), spawns (blue and red by team), hills (yellow rings), flags (white
/// squares) and any lanes the generator hands in (orange lines). The PNG is written by <see cref="PngWriter"/>: no
/// image library, just zlib from the runtime.
/// </summary>
public static class Plan
{
    public static void Draw(Map m, string pngPath, IEnumerable<IReadOnlyList<(double X, double Z)>>? lanes = null, double scale = 4)
    {
        var (hx, hz) = (m.HalfX + 6, m.HalfZ + 6);
        int w = Math.Clamp((int)(2 * hx * scale), 64, 4096), h = Math.Clamp((int)(2 * hz * scale), 64, 4096);
        var img = new Canvas(w, h, (24, 26, 34));
        (int X, int Y) P(double x, double z) => ((int)Math.Round(w / 2.0 + x * scale), (int)Math.Round(h / 2.0 + z * scale));

        if (m.Boundary is { } b)
        {
            img.FillPolygon(b.Outline.Select(p => P(p.X, p.Y)).ToList(), (44, 48, 60));
            img.Polygon(b.Outline.Select(p => P(p.X, p.Y)).ToList(), (120, 130, 160));
        }
        var order = Enumerable.Range(0, m.Solid.Count).OrderBy(i => m.Solid[i].Center.Y + m.Solid[i].Half.Y).ToList();
        var lowest = m.Solid.Count == 0 ? 0f : m.Solid.Min(s => s.Center.Y - s.Half.Y);
        foreach (var i in order)
        {
            var s = m.Solid[i];
            if (s.Half.X > 400 || s.Half.Z > 400)
                continue; // the ground slab
            var top = s.Center.Y + s.Half.Y;
            var c = (int)Math.Clamp(90 + (top - lowest) * 6, 60, 240);
            var col = m.IsHiddenBox(i) ? (c * 2 / 3, c * 2 / 3, c) : (c, c, c - 10);
            var a = P(s.Center.X - s.Half.X, s.Center.Z - s.Half.Z);
            var e = P(s.Center.X + s.Half.X, s.Center.Z + s.Half.Z);
            img.FillRect(a.X, a.Y, Math.Max(e.X, a.X + 1), Math.Max(e.Y, a.Y + 1), col);
        }
        foreach (var i in m.SolidMeshes)
        {
            var bb = m.MeshPlacements[i].Bounds;
            var a = P(bb.Min.X, bb.Min.Z);
            var e = P(bb.Max.X, bb.Max.Z);
            img.Rect(a.X, a.Y, e.X, e.Y, (150, 140, 120));
        }
        if (lanes != null)
            foreach (var lane in lanes)
                for (var i = 0; i + 1 < lane.Count; i++)
                    img.Line(P(lane[i].X, lane[i].Z), P(lane[i + 1].X, lane[i + 1].Z), (255, 140, 40), 2);
        foreach (var p in m.PropInstances)
        {
            var q = P(p.Position.X, p.Position.Z);
            img.FillRect(q.X - 2, q.Y - 2, q.X + 2, q.Y + 2, (255, 200, 60));
        }
        foreach (var wp in m.Waypoints)
        {
            var q = P(wp.X, wp.Z);
            img.FillRect(q.X - 1, q.Y - 1, q.X + 1, q.Y + 1, (80, 200, 255));
        }
        var o = m.Objectives;
        for (var t = 0; t < 2; t++)
            foreach (var sp in o.TeamSpawns[t])
            {
                var q = P(sp.X, sp.Z);
                img.FillCircle(q.X, q.Y, 4, t == 0 ? (80, 160, 255) : (255, 90, 80));
            }
        foreach (var hill in o.Hills)
        {
            var q = P(hill.Center.X, hill.Center.Z);
            img.Circle(q.X, q.Y, (int)Math.Round(hill.Radius * scale), (255, 240, 80), 2);
        }
        foreach (var f in o.Flags)
        {
            var q = P(f.X, f.Z);
            img.Rect(q.X - 5, q.Y - 5, q.X + 5, q.Y + 5, (255, 255, 255), 2);
        }
        PngWriter.Write(pngPath, img.Width, img.Height, img.Rgb);
    }

    /// <summary>A plain RGB raster with the few drawing calls the plan needs.</summary>
    public sealed class Canvas
    {
        public readonly int Width, Height;
        public readonly byte[] Rgb;

        public Canvas(int w, int h, (int R, int G, int B) back)
        {
            Width = w;
            Height = h;
            Rgb = new byte[w * h * 3];
            for (var i = 0; i < w * h; i++)
                Set(i % w, i / w, back);
        }

        public void Set(int x, int y, (int R, int G, int B) c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;
            var i = (y * Width + x) * 3;
            Rgb[i] = (byte)Math.Clamp(c.R, 0, 255);
            Rgb[i + 1] = (byte)Math.Clamp(c.G, 0, 255);
            Rgb[i + 2] = (byte)Math.Clamp(c.B, 0, 255);
        }

        public void FillRect(int x0, int y0, int x1, int y1, (int, int, int) c)
        {
            for (var y = Math.Max(0, Math.Min(y0, y1)); y <= Math.Min(Height - 1, Math.Max(y0, y1)); y++)
                for (var x = Math.Max(0, Math.Min(x0, x1)); x <= Math.Min(Width - 1, Math.Max(x0, x1)); x++)
                    Set(x, y, c);
        }

        public void Rect(int x0, int y0, int x1, int y1, (int, int, int) c, int width = 1)
        {
            for (var k = 0; k < width; k++)
            {
                Line((x0 + k, y0 + k), (x1 - k, y0 + k), c);
                Line((x1 - k, y0 + k), (x1 - k, y1 - k), c);
                Line((x1 - k, y1 - k), (x0 + k, y1 - k), c);
                Line((x0 + k, y1 - k), (x0 + k, y0 + k), c);
            }
        }

        public void Line((int X, int Y) a, (int X, int Y) b, (int, int, int) c, int width = 1)
        {
            int dx = Math.Abs(b.X - a.X), dy = -Math.Abs(b.Y - a.Y), sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1, err = dx + dy;
            var (x, y) = a;
            while (true)
            {
                if (width <= 1) Set(x, y, c);
                else FillRect(x - width / 2, y - width / 2, x + width / 2, y + width / 2, c);
                if (x == b.X && y == b.Y)
                    break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }

        public void Polygon(IReadOnlyList<(int X, int Y)> pts, (int, int, int) c)
        {
            for (var i = 0; i < pts.Count; i++)
                Line(pts[i], pts[(i + 1) % pts.Count], c);
        }

        public void FillPolygon(IReadOnlyList<(int X, int Y)> pts, (int, int, int) c)
        {
            if (pts.Count < 3)
                return;
            int y0 = Math.Max(0, pts.Min(p => p.Y)), y1 = Math.Min(Height - 1, pts.Max(p => p.Y));
            var xs = new List<int>();
            for (var y = y0; y <= y1; y++)
            {
                xs.Clear();
                for (var i = 0; i < pts.Count; i++)
                {
                    var (ax, ay) = pts[i];
                    var (bx, by) = pts[(i + 1) % pts.Count];
                    if (ay > y != by > y)
                        xs.Add(ax + (y - ay) * (bx - ax) / (by - ay));
                }
                xs.Sort();
                for (var i = 0; i + 1 < xs.Count; i += 2)
                    for (var x = Math.Max(0, xs[i]); x <= Math.Min(Width - 1, xs[i + 1]); x++)
                        Set(x, y, c);
            }
        }

        public void FillCircle(int cx, int cy, int r, (int, int, int) c)
        {
            for (var y = -r; y <= r; y++)
                for (var x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r)
                        Set(cx + x, cy + y, c);
        }

        public void Circle(int cx, int cy, int r, (int, int, int) c, int width = 1)
        {
            var n = Math.Max(12, r * 2);
            var pts = new List<(int, int)>();
            for (var i = 0; i < n; i++)
                pts.Add(((int)Math.Round(cx + r * Math.Cos(2 * Math.PI * i / n)), (int)Math.Round(cy + r * Math.Sin(2 * Math.PI * i / n))));
            for (var i = 0; i < n; i++)
                Line(pts[i], pts[(i + 1) % n], c, width);
        }
    }
}

/// <summary>A minimal PNG encoder (8-bit RGB, one zlib IDAT), enough for plans and self-test pictures.</summary>
public static class PngWriter
{
    public static void Write(string path, int width, int height, byte[] rgb) => File.WriteAllBytes(path, Encode(width, height, rgb));

    public static byte[] Encode(int width, int height, byte[] rgb)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type: RGB
        Chunk(ms, "IHDR", ihdr);
        var raw = new byte[(width * 3 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (width * 3 + 1)] = 0; // filter: none
            Buffer.BlockCopy(rgb, y * width * 3, raw, y * (width * 3 + 1) + 1, width * 3);
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true))
            zs.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, data));
        s.Write(crc);
    }

    private static uint[]? _table;

    private static uint Crc32(byte[] a, byte[] b)
    {
        if (_table == null)
        {
            _table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                _table[n] = c;
            }
        }
        var crc = 0xFFFFFFFFu;
        foreach (var x in a)
            crc = _table[(crc ^ x) & 0xFF] ^ (crc >> 8);
        foreach (var x in b)
            crc = _table[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
