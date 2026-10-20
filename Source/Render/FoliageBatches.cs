using System.Numerics;

namespace CallOfSlop;

/// <summary>A run of same-type plants in the instance buffer that share one square of the map (culled as a unit).</summary>
public readonly record struct FoliageChunk(FoliageType Type, int Start, int Count, Vector3 Min, Vector3 Max);

/// <summary>
/// The GPU-ready form of a map's foliage at one quality level, built once per map (no GL here, so the self-test
/// can check it). Plants are sorted by type, then by 16 m chunk in a snake order across the map, so neighbouring
/// visible chunks sit next to each other in the buffer and merge into one instanced draw.
///
/// Instance layout (8 floats): position (3), scale (1), colour (3), yaw as a fraction of a turn (1).
/// </summary>
public sealed class FoliageBatches
{
    public const int FloatsPerInstance = 8;

    /// <summary>Side of a culling chunk in metres.</summary>
    public const float ChunkSize = 16f;

    public float[] Instances = [];
    public int Count;
    public FoliageChunk[] Chunks = [];

    /// <summary>Per type: the first chunk and how many chunks (types are contiguous in <see cref="Chunks"/>).</summary>
    public readonly int[] TypeChunkStart = new int[FoliageMeshes.TypeCount];

    public readonly int[] TypeChunkCount = new int[FoliageMeshes.TypeCount];

    /// <summary>Per type: how many plants were kept at this quality.</summary>
    public readonly int[] TypeCount = new int[FoliageMeshes.TypeCount];

    public static FoliageBatches Build(IReadOnlyList<FoliageInstance> all, float halfX, float halfZ, FoliageQuality quality)
    {
        var b = new FoliageBatches();
        if (quality == FoliageQuality.Off || all.Count == 0)
            return b;
        var ncx = Math.Max(1, (int)MathF.Ceiling(halfX * 2 / ChunkSize) + 1);
        var ncz = Math.Max(1, (int)MathF.Ceiling(halfZ * 2 / ChunkSize) + 1);
        var keys = new List<long>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            var f = all[i];
            if (!Foliage.Kept(f, quality))
                continue;
            var cx = Math.Clamp((int)MathF.Floor((f.Position.X + halfX) / ChunkSize), 0, ncx - 1);
            var cz = Math.Clamp((int)MathF.Floor((f.Position.Z + halfZ) / ChunkSize), 0, ncz - 1);
            var snake = cz % 2 == 0 ? cx : ncx - 1 - cx;
            long cell = ((int)f.Type * ncz + cz) * ncx + snake;
            keys.Add((cell << 32) | (uint)i);
        }
        keys.Sort();

        b.Count = keys.Count;
        b.Instances = new float[keys.Count * FloatsPerInstance];
        var chunks = new List<FoliageChunk>();
        var start = 0;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var k = 0; k < keys.Count; k++)
        {
            var f = all[(int)(keys[k] & 0xFFFFFFFF)];
            var o = k * FloatsPerInstance;
            b.Instances[o] = f.Position.X;
            b.Instances[o + 1] = f.Position.Y;
            b.Instances[o + 2] = f.Position.Z;
            b.Instances[o + 3] = f.Scale;
            b.Instances[o + 4] = f.Color.X;
            b.Instances[o + 5] = f.Color.Y;
            b.Instances[o + 6] = f.Color.Z;
            b.Instances[o + 7] = f.Yaw01;
            b.TypeCount[(int)f.Type]++;

            // Bounds: the mesh's reach at this plant's scale, plus room for the wind to bend it.
            var (reach, height) = FoliageMeshes.Extent[(int)f.Type];
            var r = reach * f.Scale + 0.15f * f.Scale;
            min = Vector3.Min(min, new Vector3(f.Position.X - r, f.Position.Y, f.Position.Z - r));
            max = Vector3.Max(max, new Vector3(f.Position.X + r, f.Position.Y + height * f.Scale + 0.1f, f.Position.Z + r));

            var last = k == keys.Count - 1 || keys[k + 1] >> 32 != keys[k] >> 32;
            if (last)
            {
                chunks.Add(new FoliageChunk(f.Type, start, k + 1 - start, min, max));
                start = k + 1;
                min = new Vector3(float.MaxValue);
                max = new Vector3(float.MinValue);
            }
        }
        b.Chunks = chunks.ToArray();
        for (var t = 0; t < FoliageMeshes.TypeCount; t++)
        {
            var first = Array.FindIndex(b.Chunks, c => (int)c.Type == t);
            b.TypeChunkStart[t] = Math.Max(first, 0);
            b.TypeChunkCount[t] = first < 0 ? 0 : b.Chunks.Count(c => (int)c.Type == t);
        }
        return b;
    }
}
