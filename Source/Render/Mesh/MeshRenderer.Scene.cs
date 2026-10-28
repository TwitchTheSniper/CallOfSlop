using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Scene-graph meshes (<see cref="IMeshDrawer"/>): <see cref="DrawList.Submit"/> queues every mesh node with
/// <see cref="DrawMesh"/> and calls <see cref="Flush"/> at the end, which draws the queue with the same shader, pool and
/// lighting as the map's meshes: grouped by mesh (stable order), one glDrawElementsInstanced per mesh, full detail, no
/// culling (scene rigs are few and near). The queue's arrays only grow, so nothing allocates per frame.
/// </summary>
public sealed unsafe partial class MeshRenderer
{
    private MeshAsset[] _dynAsset = new MeshAsset[32];
    private float[] _dynData = new float[32 * FloatsPerInstance];
    private float[] _dynSorted = new float[32 * FloatsPerInstance];
    private int[] _dynOrder = new int[32];
    private int _dynCount;
    private int _dynCapacityGpu;

    /// <summary>Mesh instances drawn by the last <see cref="Flush"/> and the draw calls it took.</summary>
    public (int Instances, int Draws) LastSceneFlush { get; private set; }

    public void DrawMesh(int meshId, in Matrix4x4 world, Vector3 color, float alpha, int mode, Material material)
    {
        if (SceneMeshes.Get(meshId) is not { } asset)
            return;
        if (_dynCount == _dynAsset.Length)
        {
            Array.Resize(ref _dynAsset, _dynCount * 2);
            Array.Resize(ref _dynData, _dynCount * 2 * FloatsPerInstance);
            Array.Resize(ref _dynSorted, _dynCount * 2 * FloatsPerInstance);
            Array.Resize(ref _dynOrder, _dynCount * 2);
        }
        _dynAsset[_dynCount] = asset;
        var d = _dynData.AsSpan(_dynCount * FloatsPerInstance, FloatsPerInstance);
        ref readonly var m = ref world;
        d[0] = m.M11; d[1] = m.M21; d[2] = m.M31; d[3] = m.M41;
        d[4] = m.M12; d[5] = m.M22; d[6] = m.M32; d[7] = m.M42;
        d[8] = m.M13; d[9] = m.M23; d[10] = m.M33; d[11] = m.M43;
        d[12] = color.X; d[13] = color.Y; d[14] = color.Z;
        d[15] = mode == 1 ? (float)material : -1f; // procedural surface texture only in textured mode, as for boxes
        _dynCount++;
    }

    public void Flush(Renderer gfx)
    {
        var count = _dynCount;
        _dynCount = 0;
        LastSceneFlush = (0, 0);
        if (count == 0 || _broken || (!_ready && !Init()))
            return;
        if (_modelTexGeneration != MeshTextures.Generation)
            UploadModelTextures();

        // Group by mesh (insertion sort on the id keeps equal meshes in submission order; queues are short).
        for (var i = 0; i < count; i++)
            _dynOrder[i] = i;
        for (var i = 1; i < count; i++)
        {
            var k = _dynOrder[i];
            var id = _dynAsset[k].Id;
            var j = i - 1;
            while (j >= 0 && _dynAsset[_dynOrder[j]].Id > id)
            {
                _dynOrder[j + 1] = _dynOrder[j];
                j--;
            }
            _dynOrder[j + 1] = k;
        }
        for (var i = 0; i < count; i++)
            Array.Copy(_dynData, _dynOrder[i] * FloatsPerInstance, _dynSorted, i * FloatsPerInstance, FloatsPerInstance);

        _vao.Bind();
        for (var i = 0; i < count; i++)
            _pool.Add(_dynAsset[i]); // packs a mesh the first time it's seen
        _pool.Sync(_vbo, _ibo);

        var vp = gfx.ViewProj;
        var cam = gfx.CameraPosition;
        var fog = gfx.FogColor;
        var fogRange = gfx.FogRange;
        _program.Use();
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_locFogRange, fogRange.X, fogRange.Y);
        Native.glActiveTexture(GL_TEXTURE1);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _modelTex);
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        _dynCapacityGpu = Math.Max(_dynCapacityGpu, count);
        Native.glBufferData(GL_ARRAY_BUFFER, (nint)_dynCapacityGpu * FloatsPerInstance * sizeof(float), null, GL_STREAM_DRAW); // orphan
        fixed (float* src = _dynSorted)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, (nint)count * FloatsPerInstance * sizeof(float), src);

        var draws = 0;
        const int stride = FloatsPerInstance * sizeof(float);
        for (var start = 0; start < count;)
        {
            var asset = _dynAsset[_dynOrder[start]];
            var end = start + 1;
            while (end < count && ReferenceEquals(_dynAsset[_dynOrder[end]], asset))
                end++;
            var range = _pool.Add(asset)[0];
            if (asset.DoubleSided)
                Native.glDisable(GlCapability.CullFace);
            else
            {
                Enable(GlCapability.CullFace);
                Native.glCullFace(GlFace.Back);
            }
            var offset = (nint)start * stride;
            for (uint a = 0; a < 4; a++)
                Native.glVertexAttribPointer(5 + a, 4, GL_FLOAT, false, stride, (void*)(offset + a * 4 * sizeof(float)));
            PerfCounters.Draw(range.IndexCount, end - start); // the perf window's counters (Source/Perf/PerfCounters.cs)
            Native.glDrawElementsInstanced(GL_TRIANGLES, range.IndexCount, GL_UNSIGNED_INT, (void*)((nint)range.FirstIndex * sizeof(uint)), end - start);
            draws++;
            start = end;
        }
        Native.glDisable(GlCapability.CullFace);
        gfx.Rebind();
        LastSceneFlush = (count, draws);
    }
}
