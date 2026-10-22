using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Every prop model in the library baked into one vertex and index buffer (no GL here, so the self-test can check it).
/// Built once per library load; each prop type is a range of the index buffer that the renderer instances.
///
/// Vertex layout (12 floats): position (3, prop frame), normal (3), colour (3, with a little baked ground shadow),
/// texture coordinates (2, metres along the part's own faces / 2, like the map boxes) and the texture layer (1, -1 for a
/// plain colour).
/// </summary>
public sealed class PropMeshes
{
    public const int FloatsPerVertex = 12;

    /// <summary>A range of the shared index buffer.</summary>
    public readonly record struct Range(int FirstIndex, int IndexCount);

    public float[] Vertices = [];
    public uint[] Indices = [];

    /// <summary>[def index]: where each prop's mesh is in <see cref="Indices"/>.</summary>
    public Range[] Ranges = [];

    /// <summary>The library the meshes were built from (instances of props not in it are skipped).</summary>
    public PropDef[] Defs = [];

    public int Triangles => Indices.Length / 3;

    private static readonly (Vector3 N, Vector3 U, Vector3 V)[] Faces =
    [
        (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),
        (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
        (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
        (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
        (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),
        (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
    ];

    public static PropMeshes Build(PropDef[] defs)
    {
        var v = new List<float>(defs.Length * 600);
        var idx = new List<uint>(defs.Length * 300);
        var ranges = new Range[defs.Length];
        for (var d = 0; d < defs.Length; d++)
        {
            var first = idx.Count;
            foreach (var part in defs[d].Parts)
                AddPart(part, v, idx);
            ranges[d] = new Range(first, idx.Count - first);
        }
        return new PropMeshes { Vertices = v.ToArray(), Indices = idx.ToArray(), Ranges = ranges, Defs = defs };
    }

    private static void AddPart(PropPart part, List<float> v, List<uint> idx)
    {
        if (part.Corners is { Length: 8 })
        {
            AddCornerPart(part, v, idx);
            return;
        }
        var q = part.Rotation;
        var h = part.Size / 2;
        foreach (var (n, u, w) in Faces)
        {
            var baseVertex = (uint)(v.Count / FloatsPerVertex);
            var nh = n * h;
            var uh = u * h;
            var wh = w * h;
            // Faces stretch along their own axes: texture 2 m per repeat (as on map boxes) times the part's texScale.
            float uLen = MathF.Abs(uh.X + uh.Y + uh.Z), wLen = MathF.Abs(wh.X + wh.Y + wh.Z);
            var normal = Vector3.Transform(n, q);
            for (var k = 0; k < 4; k++)
            {
                float su = k is 0 or 3 ? -1 : 1, sw = k < 2 ? -1 : 1;
                var local = nh + uh * su + wh * sw;
                var pos = part.Offset + Vector3.Transform(local, q);
                // Baked contact shadow: darker near the ground, and faces pointing down darker still.
                var ao = 0.72f + 0.28f * SmoothStep(0f, 0.45f, pos.Y);
                if (normal.Y < -0.5f)
                    ao *= 0.7f;
                var col = part.Color * ao;
                v.AddRange([pos.X, pos.Y, pos.Z, normal.X, normal.Y, normal.Z, col.X, col.Y, col.Z,
                    su * uLen * 0.5f * part.TexScale, sw * wLen * 0.5f * part.TexScale, part.Layer]);
            }
            idx.AddRange([baseVertex, baseVertex + 1, baseVertex + 2, baseVertex, baseVertex + 2, baseVertex + 3]);
        }
    }

    /// <summary>
    /// A part given by its 8 corners (<see cref="PropPart.Corners"/>, flag cloth pieces): the same 24 vertices and 36
    /// indices as a box, in the same face order, with each face's normal worked out from its corners.
    /// </summary>
    private static void AddCornerPart(PropPart part, List<float> v, List<uint> idx)
    {
        var cs = part.Corners!;
        var centre = Vector3.Zero;
        foreach (var c in cs)
            centre += c / 8f;
        Span<Vector3> quad = stackalloc Vector3[4];
        foreach (var (n, u, w) in Faces)
        {
            var baseVertex = (uint)(v.Count / FloatsPerVertex);
            for (var k = 0; k < 4; k++)
            {
                float su = k is 0 or 3 ? -1 : 1, sw = k < 2 ? -1 : 1;
                var s = n + u * su + w * sw; // each component is -1 or +1: which corner this is
                quad[k] = cs[(s.X > 0 ? 1 : 0) | (s.Y > 0 ? 2 : 0) | (s.Z > 0 ? 4 : 0)];
            }
            var normal = Vector3.Cross(quad[1] - quad[0], quad[3] - quad[0]);
            if (normal.LengthSquared() < 1e-14f)
                normal = Vector3.Cross(quad[2] - quad[1], quad[3] - quad[1]);
            normal = normal.LengthSquared() > 1e-14f ? Vector3.Normalize(normal) : n;
            if (Vector3.Dot(normal, (quad[0] + quad[1] + quad[2] + quad[3]) / 4 - centre) < 0)
                normal = -normal;
            float uLen = Vector3.Distance(quad[0], quad[1]), wLen = Vector3.Distance(quad[1], quad[2]);
            for (var k = 0; k < 4; k++)
            {
                float su = k is 0 or 3 ? -1 : 1, sw = k < 2 ? -1 : 1;
                var pos = quad[k];
                var ao = 0.72f + 0.28f * SmoothStep(0f, 0.45f, pos.Y);
                if (normal.Y < -0.5f)
                    ao *= 0.7f;
                var col = part.Color * ao;
                v.AddRange([pos.X, pos.Y, pos.Z, normal.X, normal.Y, normal.Z, col.X, col.Y, col.Z,
                    su * uLen * 0.5f * part.TexScale, sw * wLen * 0.5f * part.TexScale, part.Layer]);
            }
            idx.AddRange([baseVertex, baseVertex + 1, baseVertex + 2, baseVertex, baseVertex + 2, baseVertex + 3]);
        }
    }

    private static float SmoothStep(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
