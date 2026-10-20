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

    private static float SmoothStep(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
