using System.Numerics;
using Brutal;
using MO = Brutal.MeshOptimizer.MeshOptimizer;

namespace CallOfSlop;

/// <summary>
/// The parts of meshoptimizer (Brutal.MeshOptimizer, a native library) the mesh pipeline uses: vertex cache and vertex
/// fetch optimisation, and simplification for model LODs. Its native wasn't export-checked when the package was chosen,
/// so the first use runs a small canary that calls every function used here on a test grid and checks the answers.
/// If anything is missing or wrong, <see cref="Available"/> is false and callers fall back: meshes stay unoptimised
/// (they still draw the same) and models get no simplified LODs (primitives make their LODs from fewer segments anyway).
/// </summary>
public static unsafe class MeshOpt
{
    private static int _state; // 0 = not checked, 1 = works, -1 = disabled
    private static readonly object Gate = new();

    /// <summary>One line describing the canary's result (for the log and the self-test).</summary>
    public static string CanaryReport { get; private set; } = "not run";

    /// <summary>True when the meshoptimizer native passed the canary (runs it on first use).</summary>
    public static bool Available
    {
        get
        {
            if (_state == 0)
                lock (Gate)
                    if (_state == 0)
                        _state = RunCanary() ? 1 : -1;
            return _state > 0;
        }
    }

    /// <summary>For tests: pretend the native is missing (true) or re-run the canary (false).</summary>
    public static void ForceDisabled(bool disabled)
    {
        lock (Gate)
            _state = disabled ? -1 : 0;
    }

    private static bool RunCanary()
    {
        try
        {
            // A 6x6 quad grid (49 vertices, 72 triangles) in the shared vertex format.
            var grid = Grid(6);
            var idx = (uint[])grid.Indices.Clone();
            var cached = new uint[idx.Length];
            fixed (uint* src = idx, dst = cached)
                MO.OptimizeVertexCache((int*)dst, (int*)src, idx.Length, grid.Vertices.Length);
            if (!SameTriangles(idx, cached))
                return Report(false, "optimizeVertexCache changed the triangle set");

            var verts = (MeshVertex[])grid.Vertices.Clone();
            var outVerts = new MeshVertex[verts.Length];
            ulong unique;
            fixed (MeshVertex* vs = verts, vd = outVerts)
            fixed (uint* ix = cached)
                unique = MO.OptimizeVertexFetch((void*)vd, (int*)ix, cached.Length, (void*)vs, verts.Length, new ByteSize64(MeshVertex.SizeInBytes)).Value;
            if (unique != (ulong)verts.Length || !SamePositions(grid, outVerts, cached))
                return Report(false, $"optimizeVertexFetch gave {unique} vertices or moved positions");

            var shadow = new uint[idx.Length];
            fixed (uint* s = shadow, i = idx)
            fixed (MeshVertex* v = grid.Vertices)
                MO.GenerateShadowIndexBuffer((int*)s, (int*)i, idx.Length, (void*)v, grid.Vertices.Length, new ByteSize64(12), new ByteSize64(MeshVertex.SizeInBytes));
            if (!SameTriangles(idx, shadow))
                return Report(false, "generateShadowIndexBuffer changed a grid with no duplicates");

            float scale;
            fixed (MeshVertex* v = grid.Vertices)
                scale = MO.SimplifyScale((float*)v, grid.Vertices.Length, new ByteSize64(MeshVertex.SizeInBytes));
            if (MathF.Abs(scale - 6f) > 0.01f) // the grid's largest extent
                return Report(false, $"simplifyScale gave {scale} for a 6 m grid");

            var simple = new uint[idx.Length];
            ulong count;
            float error;
            fixed (uint* d = simple, i = idx)
            fixed (MeshVertex* v = grid.Vertices)
                count = MO.Simplify((int*)d, (int*)i, idx.Length, (float*)v, grid.Vertices.Length, new ByteSize64(MeshVertex.SizeInBytes), idx.Length / 4, 0.05f, 0, &error).Value;
            // A flat grid simplifies down to very few triangles without any error.
            if (count == 0 || count % 3 != 0 || count >= (ulong)idx.Length || simple.Take((int)count).Any(x => x >= grid.Vertices.Length))
                return Report(false, $"simplify gave {count} indices from {idx.Length}");
            return Report(true, $"ok (optimizeVertexCache, optimizeVertexFetch, generateShadowIndexBuffer, simplifyScale {scale:0.00}, simplify {idx.Length / 3} -> {count / 3} triangles)");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeLoadException or System.Runtime.InteropServices.MarshalDirectiveException)
        {
            return Report(false, $"native not usable: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Report(false, $"canary threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool Report(bool ok, string text)
    {
        CanaryReport = (ok ? "" : "FALLBACK: ") + text;
        Console.WriteLine($"[mesh] meshoptimizer canary: {CanaryReport}");
        return ok;
    }

    /// <summary>An n x n quad grid on the xz plane (for the canary and tests).</summary>
    public static MeshData Grid(int n)
    {
        var b = new MeshBuilder();
        for (var z = 0; z <= n; z++)
            for (var x = 0; x <= n; x++)
                b.Vertex(new Vector3(x, 0, z), Vector3.UnitY, new Vector2(x, z));
        for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                var a = z * (n + 1) + x;
                b.Quad(a, a + 1, a + n + 2, a + n + 1);
            }
        return b.Build();
    }

    private static bool SameTriangles(uint[] a, uint[] b)
    {
        static IEnumerable<(uint, uint, uint)> Canon(uint[] t)
        {
            for (var i = 0; i + 2 < t.Length; i += 3)
            {
                uint x = t[i], y = t[i + 1], z = t[i + 2];
                // rotate so the smallest index comes first (keeps the winding)
                yield return x <= y && x <= z ? (x, y, z) : y <= x && y <= z ? (y, z, x) : (z, x, y);
            }
        }
        return a.Length == b.Length && Canon(a).Order().SequenceEqual(Canon(b).Order());
    }

    private static bool SamePositions(MeshData before, MeshVertex[] after, uint[] remappedIndices)
    {
        // Every triangle of the remapped mesh must still cover the same grid squares: compare position sums.
        Vector3 s0 = default, s1 = default;
        foreach (var i in before.Indices)
            s0 += before.Vertices[i].Position;
        foreach (var i in remappedIndices)
            s1 += after[i].Position;
        return Vector3.Distance(s0, s1) < 1e-3f;
    }

    // ---- What the pipeline calls ---------------------------------------------------------------------------------

    /// <summary>
    /// Reorders the triangles for the GPU's vertex cache and the vertices for fetch locality (same triangles, same
    /// look). Returns the mesh unchanged when meshoptimizer isn't available.
    /// </summary>
    public static MeshData Optimize(MeshData m)
    {
        if (m.Indices.Length < 3 || !Available)
            return m;
        try
        {
            var cached = new uint[m.Indices.Length];
            fixed (uint* src = m.Indices, dst = cached)
                MO.OptimizeVertexCache((int*)dst, (int*)src, cached.Length, m.Vertices.Length);
            var verts = new MeshVertex[m.Vertices.Length];
            ulong unique;
            fixed (MeshVertex* vs = m.Vertices, vd = verts)
            fixed (uint* ix = cached)
                unique = MO.OptimizeVertexFetch((void*)vd, (int*)ix, cached.Length, (void*)vs, m.Vertices.Length, new ByteSize64(MeshVertex.SizeInBytes)).Value;
            if (unique < (ulong)verts.Length)
                Array.Resize(ref verts, (int)unique); // unused vertices dropped
            return new MeshData(verts, cached);
        }
        catch (Exception ex)
        {
            Disable(ex);
            return m;
        }
    }

    /// <summary>
    /// A simplified index buffer over the same vertices with about <paramref name="ratio"/> of the triangles, or null if
    /// meshoptimizer isn't available or couldn't reduce it by at least 20%. Seams (uv and hard-normal splits) are welded
    /// for the simplifier first (shadow index buffer), so they don't lock the mesh.
    /// </summary>
    public static uint[]? Simplify(MeshData m, float ratio, float maxError)
    {
        if (m.Indices.Length < 3 * 16 || !Available)
            return null;
        try
        {
            var shadow = new uint[m.Indices.Length];
            fixed (uint* s = shadow, i = m.Indices)
            fixed (MeshVertex* v = m.Vertices)
                MO.GenerateShadowIndexBuffer((int*)s, (int*)i, shadow.Length, (void*)v, m.Vertices.Length, new ByteSize64(12), new ByteSize64(MeshVertex.SizeInBytes));
            var dst = new uint[m.Indices.Length];
            var target = Math.Max(3, (long)(m.Indices.Length * ratio) / 3 * 3);
            ulong count;
            float err;
            fixed (uint* d = dst, s = shadow)
            fixed (MeshVertex* v = m.Vertices)
                count = MO.Simplify((int*)d, (int*)s, shadow.Length, (float*)v, m.Vertices.Length, new ByteSize64(MeshVertex.SizeInBytes), target, maxError, 0, &err).Value;
            if (count < 3 || count > (ulong)(m.Indices.Length * 0.8f))
                return null;
            var result = new uint[count];
            fixed (uint* src = dst, r = result)
                MO.OptimizeVertexCache((int*)r, (int*)src, (long)count, m.Vertices.Length);
            return result;
        }
        catch (Exception ex)
        {
            Disable(ex);
            return null;
        }
    }

    private static void Disable(Exception ex)
    {
        lock (Gate)
            _state = -1;
        CanaryReport = $"FALLBACK: disabled after {ex.GetType().Name}: {ex.Message}";
        Console.WriteLine($"[mesh] meshoptimizer {CanaryReport}");
    }
}
