using System.Numerics;
using CallOfSlop.MapKit;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A vehicle's plan: its parts' boxes in their paint, seen from the left, from above, from the front and from three quarters (nearest
/// box on top, shaded by which face it shows). Like the map kit's plan, for a generator's author to look at: <c>--gen-vehicle NAME --plan</c>.
/// </summary>
public static class VehiclePlan
{
    private readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Vector3 Rgb);

    public static void Draw(string text, string file, string png)
    {
        var bp = ConstructBuild.ParseBlueprint(text, file);
        var w = new ConstructWorld();
        var c = ConstructBuild.Build(w, bp, Vector3.Zero, Quaternion.Identity, out var why) ?? throw new VehicleKitException($"it doesn't build: {why}");
        var faces = new List<Face>();
        foreach (var body in w.Bodies)
            AddBody(body, Quaternion.Inverse(c.Rot), c.Pos, faces);
        const int cell = 520;
        var (width, height) = (cell * 2, cell * 2);
        var rgb = new byte[width * height * 3];
        Array.Fill(rgb, (byte)225);
        var depth = new float[width * height];
        Array.Fill(depth, float.MaxValue);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var f in faces)
            foreach (var p in new[] { f.A, f.B, f.C, f.D })
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        var mid = (min + max) * 0.5f;
        var views = new (Vector3 Right, Vector3 Up, Vector3 Back, int X, int Y)[]
        {
            (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, 0, 0), // from the left, nose to the right
            (-Vector3.UnitZ, -Vector3.UnitX, -Vector3.UnitY, cell, 0), // from above
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 0, cell), // from the front
            (Vector3.Normalize(new Vector3(0.7f, 0, 0.7f)), Vector3.Normalize(new Vector3(0.35f, 0.87f, -0.35f)), Vector3.Normalize(new Vector3(0.6f, -0.45f, -0.6f)), cell, cell), // three quarters: from behind on the left, above (the bed)
        };
        var scale = cell * 0.9f / MathF.Max(max.Z - min.Z, MathF.Max(max.X - min.X, max.Y - min.Y) * 1.15f);
        foreach (var v in views)
            foreach (var f in faces)
                Quad(f, v, mid, scale, cell, width, height, rgb, depth);
        PngWriter.Write(png, width, height, rgb);
    }

    private static void AddBody(Construct b, Quaternion toPlan, Vector3 planPos, List<Face> faces)
    {
        for (var i = 0; i < b.PartCount; i++)
        {
            var d = b.DefOf(i);
            b.RestPose(b.Parts[i].Seg, out var q, out var t);
            var rot = q * Rot24.Quat(b.Parts[i].Rot);
            foreach (var box in d.Boxes)
            {
                if (box.Hidden && box.Shape == 0)
                    continue;
                var colour = ConstructPaint.IsSet(b.Parts[i].Paint) && box.Paintable ? ConstructPaint.ToVector(b.Parts[i].Paint) : box.Color;
                var centre = t + Vector3.Transform(b.Parts[i].Pos + Rot24.Apply(b.Parts[i].Rot, box.Center), q);
                var world = b.Pos + Vector3.Transform(centre, b.Rot);
                if (box.Shape != 0)
                {
                    AddMesh(box, Vector3.Transform(world - planPos, toPlan), toPlan * b.Rot * rot, colour, faces);
                    continue;
                }
                AddBox(Vector3.Transform(world - planPos, toPlan), box.Half, toPlan * b.Rot * rot, colour, faces);
            }
        }
    }

    /// <summary>A shaped box's real triangles (its mesh is in unit-cube space, 6 floats a vertex), so a slope reads as a slope.</summary>
    private static void AddMesh(in CPartBox box, Vector3 c, Quaternion r, Vector3 rgb, List<Face> faces)
    {
        var mesh = PartShapes.Mesh(box.Shape);
        var m = box.Model(0, Vector3.Zero);
        Vector3 P(int v) => c + Vector3.Transform(Vector3.Transform(new Vector3(mesh[v * 6], mesh[v * 6 + 1], mesh[v * 6 + 2]), m), r);
        for (var v = 0; v + 2 < mesh.Length / 6; v += 3)
            faces.Add(new Face(P(v), P(v + 1), P(v + 2), P(v + 2), rgb));
    }

    private static void AddBox(Vector3 c, Vector3 h, Quaternion r, Vector3 rgb, List<Face> faces)
    {
        Vector3 P(float x, float y, float z) => c + Vector3.Transform(new Vector3(x * h.X, y * h.Y, z * h.Z), r);
        for (var sign = -1; sign <= 1; sign += 2)
        {
            faces.Add(new Face(P(sign, -1, -1), P(sign, 1, -1), P(sign, 1, 1), P(sign, -1, 1), rgb));
            faces.Add(new Face(P(-1, sign, -1), P(1, sign, -1), P(1, sign, 1), P(-1, sign, 1), rgb));
            faces.Add(new Face(P(-1, -1, sign), P(1, -1, sign), P(1, 1, sign), P(-1, 1, sign), rgb));
        }
    }

    private static void Quad(in Face f, (Vector3 Right, Vector3 Up, Vector3 Back, int X, int Y) v, Vector3 mid, float scale, int cell, int width, int height, byte[] rgb, float[] depth)
    {
        Vector3 S(Vector3 p)
        {
            var q = p - mid;
            return new Vector3(v.X + cell * 0.5f + Vector3.Dot(q, v.Right) * scale, v.Y + cell * 0.5f - Vector3.Dot(q, v.Up) * scale, Vector3.Dot(q, v.Back));
        }
        var n = Vector3.Normalize(Vector3.Cross(f.B - f.A, f.C - f.A));
        var shade = 0.55f + 0.45f * MathF.Abs(Vector3.Dot(n, Vector3.Normalize(new Vector3(0.3f, 0.8f, 0.5f))));
        var (a, b, c, d) = (S(f.A), S(f.B), S(f.C), S(f.D));
        Tri(a, b, c, f.Rgb * shade, v, cell, width, rgb, depth);
        Tri(a, c, d, f.Rgb * shade, v, cell, width, rgb, depth);
    }

    private static void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 col, (Vector3 Right, Vector3 Up, Vector3 Back, int X, int Y) v, int cell, int width, byte[] rgb, float[] depth)
    {
        int x0 = Math.Max(v.X, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)))), x1 = Math.Min(v.X + cell - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int y0 = Math.Max(v.Y, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)))), y1 = Math.Min(v.Y + cell - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(area) < 1e-4f)
            return;
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var (px, py) = (x + 0.5f, y + 0.5f);
            var w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
            var w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
            var w2 = 1 - w0 - w1;
            if (w0 < 0 || w1 < 0 || w2 < 0)
                continue;
            var z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
            var k = y * width + x;
            if (z >= depth[k])
                continue;
            depth[k] = z;
            rgb[k * 3] = (byte)Math.Clamp(col.X * 255, 0, 255);
            rgb[k * 3 + 1] = (byte)Math.Clamp(col.Y * 255, 0, 255);
            rgb[k * 3 + 2] = (byte)Math.Clamp(col.Z * 255, 0, 255);
        }
    }
}
