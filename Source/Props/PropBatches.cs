using System.Numerics;

namespace CallOfSlop;

/// <summary>A run of same-type props in the instance buffer that share one square of the map (culled as a unit).</summary>
public readonly record struct PropChunk(int Def, int Start, int Count, Vector3 Min, Vector3 Max);

/// <summary>
/// The GPU-ready form of a map's props (no GL here, so the self-test can check it), built once per map. Props are sorted
/// by type, then by 24 m chunk in a snake order across the map, so neighbouring visible chunks sit next to each other
/// in the buffer and merge into one instanced draw.
///
/// Instance layout (8 floats): position (3), scale (1; 0 hides a broken prop), yaw in radians (1), fade distance (1;
/// 0 = never fades), a random 0..1 (1, colour variation and dither offset), unused (1).
/// </summary>
public sealed class PropBatches
{
    public const int FloatsPerInstance = 8;

    /// <summary>Side of a culling chunk in metres.</summary>
    public const float ChunkSize = 24f;

    public float[] Instances = [];
    public int Count;
    public PropChunk[] Chunks = [];

    /// <summary>Per prop type: the first chunk and how many chunks (types are contiguous in <see cref="Chunks"/>).</summary>
    public int[] TypeChunkStart = [];

    public int[] TypeChunkCount = [];

    /// <summary>[instance index on the map]: its slot in <see cref="Instances"/>, or -1 (a prop missing from the meshes).</summary>
    public int[] SlotOf = [];

    public int Solid, Cosmetic, Types;

    private float[] _scales = [];

    public static PropBatches Build(Map map, PropMeshes meshes)
    {
        var b = new PropBatches
        {
            TypeChunkStart = new int[meshes.Defs.Length],
            TypeChunkCount = new int[meshes.Defs.Length],
        };
        var all = map.PropInstances;
        b.SlotOf = new int[all.Count];
        Array.Fill(b.SlotOf, -1);
        if (all.Count == 0)
            return b;
        // By key (the name, "Pack/Name" for a pack prop): after "Reload map files" a map kept in play still points at the
        // old library's objects, and a pack prop may share its name with a global one it shadows.
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < meshes.Defs.Length; i++)
            index[meshes.Defs[i].Key] = i;
        var ncx = Math.Max(1, (int)MathF.Ceiling(map.HalfX * 2 / ChunkSize) + 1);
        var ncz = Math.Max(1, (int)MathF.Ceiling(map.HalfZ * 2 / ChunkSize) + 1);
        var keys = new List<long>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (!TryIndex(index, p.Def, out var d))
                continue;
            var cx = Math.Clamp((int)MathF.Floor((p.Position.X + map.HalfX) / ChunkSize), 0, ncx - 1);
            var cz = Math.Clamp((int)MathF.Floor((p.Position.Z + map.HalfZ) / ChunkSize), 0, ncz - 1);
            var snake = cz % 2 == 0 ? cx : ncx - 1 - cx;
            long cell = ((long)d * ncz + cz) * ncx + snake;
            keys.Add((cell << 24) | (uint)i);
        }
        keys.Sort();

        b.Count = keys.Count;
        b.Instances = new float[keys.Count * FloatsPerInstance];
        b._scales = new float[keys.Count];
        var chunks = new List<PropChunk>();
        var types = new HashSet<int>();
        var start = 0;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var k = 0; k < keys.Count; k++)
        {
            var i = (int)(keys[k] & 0xFFFFFF);
            var p = all[i];
            TryIndex(index, p.Def, out var d);
            types.Add(d);
            b.SlotOf[i] = k;
            b._scales[k] = p.Scale;
            var o = k * FloatsPerInstance;
            b.Instances[o] = p.Position.X;
            b.Instances[o + 1] = p.Position.Y;
            b.Instances[o + 2] = p.Position.Z;
            b.Instances[o + 3] = map.IsPropBroken(i) ? 0f : p.Scale;
            b.Instances[o + 4] = p.Yaw;
            b.Instances[o + 5] = p.Def.FadeDistance;
            b.Instances[o + 6] = p.Rand;
            if (p.Def.IsSolid)
                b.Solid++;
            else
                b.Cosmetic++;
            min = Vector3.Min(min, p.Bounds.Min);
            max = Vector3.Max(max, p.Bounds.Max);
            var last = k == keys.Count - 1 || keys[k + 1] >> 24 != keys[k] >> 24;
            if (last)
            {
                chunks.Add(new PropChunk(d, start, k + 1 - start, min, max));
                start = k + 1;
                min = new Vector3(float.MaxValue);
                max = new Vector3(float.MinValue);
            }
        }
        b.Chunks = chunks.ToArray();
        b.Types = types.Count;
        for (var c = 0; c < b.Chunks.Length; c++)
        {
            var d = b.Chunks[c].Def;
            if (b.TypeChunkCount[d] == 0)
                b.TypeChunkStart[d] = c;
            b.TypeChunkCount[d]++;
        }
        return b;
    }

    /// <summary>
    /// A prop's mesh: its own, or for a flag variant missing from the meshes (a library built without
    /// <see cref="FlagPool.WithVariants"/>) the prop it was built from, drawn with its own cloth.
    /// </summary>
    private static bool TryIndex(Dictionary<string, int> index, PropDef def, out int d) =>
        index.TryGetValue(def.Key, out d) || def.FlagBase is { } b && index.TryGetValue(b.Key, out d);

    /// <summary>Hides broken props (scale 0, collapsed in the vertex shader) and shows restored ones. No allocation.</summary>
    public void ApplyBroken(Map map)
    {
        for (var i = 0; i < SlotOf.Length && i < map.PropInstances.Count; i++)
        {
            var slot = SlotOf[i];
            if (slot >= 0)
                Instances[slot * FloatsPerInstance + 3] = map.IsPropBroken(i) ? 0f : _scales[slot];
        }
    }
}
