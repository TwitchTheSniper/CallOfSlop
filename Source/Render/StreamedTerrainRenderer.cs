using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws streamed terrain (TerrainStream.cs: maps tens of kilometres across) with continuous distance-dependent level of
/// detail (CDLOD, Strugar 2010). The ground is a quadtree of nodes, each <see cref="PageCells"/> x <see cref="PageCells"/>
/// cells at its level's spacing (level 0: the map's own cells; each level up doubles them). Every frame the tree is walked
/// from the top: a node near enough to the camera for the next level down is split, so the detail falls off with distance
/// (level 0 within about <see cref="RangeScale"/> node widths, and so on up). Each node's heights, normals and colours (a
/// "page") are worked out on a worker thread by the map's own terrain features at that spacing and kept on the GPU in a
/// texture array (least recently drawn dropped first); a node whose page isn't ready yet is drawn by its parent.
/// <para>
/// Drawing is one instanced call: every visible quarter-node is an instance of one 32 x 32-cell grid (no vertex buffer:
/// the shader reads its page). Vertices morph smoothly into the next level's grid as they near the end of their level's
/// range, so detail changes never pop and neighbouring levels meet without cracks; skirts hide the cracks left while a
/// page is still coming. Dug cells (TerrainEdits.cs) are cut out of level-0 nodes by a mask; their blocks are drawn by
/// <see cref="TerrainRenderer"/> as on any map.
/// </para>
/// </summary>
public sealed unsafe class StreamedTerrainRenderer
{
    /// <summary>Cells along a node's side (at its level's spacing).</summary>
    public const int PageCells = 64;

    private const int PageVerts = PageCells + 1;
    private const int Quarter = PageCells / 2;
    private const int QuarterVerts = Quarter + 1;

    /// <summary>A level is drawn within this many of its node widths of the camera.</summary>
    public const float RangeScale = 2.4f;

    /// <summary>Pages kept on the GPU (65 x 65 texels of 16 bytes each: 66 KB apiece).</summary>
    public const int MaxPages = 512;

    private const int MaxLevels = 16;
    private const int MaxInstances = 4096;
    private const int FloatsPerInstance = 8;
    private const int UploadsPerFrame = 8;
    private const int MaxPending = 64;
    private const int PageUnit = 10, DugUnit = 11; // texture units (0: the box texture array, 12-14: the light grid)

    private const string VertexSrc = """
        #version 410 core
        layout(location = 0) in vec4 iA; // quarter's world origin x, z; spacing; page layer
        layout(location = 1) in vec4 iB; // quarter's first texel in the page x, z; level; dug mask on
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        uniform vec2 uMorph[16];   // per level: morph start, end (metres)
        uniform sampler2DArray uPages; // per texel: height, normal x (+ 4 x rockiness 0-255), normal z, colour (r + g 256 + b 65536)
        out vec3 vWorld;
        out vec3 vNormal;
        out vec4 vColor;
        flat out vec4 vPage;
        flat out float vDug;
        const int Q = 32;
        const int N = Q + 1;

        void Fetch(ivec2 t, int layer, out float h, out vec3 n, out vec4 c)
        {
            vec4 v = texelFetch(uPages, ivec3(t, layer), 0);
            float k = floor((v.y + 2.0) * 0.25);
            float nx = v.y - 4.0 * k;
            h = v.x;
            n = vec3(nx, sqrt(max(1.0 - nx * nx - v.z * v.z, 0.0)), v.z);
            uint p = uint(v.w);
            c = vec4(float(p & 255u), float((p >> 8) & 255u), float(p >> 16), k) / 255.0;
        }

        void main()
        {
            int id = gl_VertexID;
            bool skirt = id >= N * N;
            ivec2 g;
            if (!skirt)
                g = ivec2(id % N, id / N);
            else
            {
                int k = id - N * N;
                int side = k / N;
                int t = k - side * N;
                g = side == 0 ? ivec2(t, 0) : side == 1 ? ivec2(Q, t) : side == 2 ? ivec2(t, Q) : ivec2(0, t);
            }
            ivec2 tex = g + ivec2(iB.xy);
            int layer = int(iA.w);
            float s = iA.z;
            vec2 xz = iA.xy + vec2(g) * s;
            float h; vec3 n; vec4 c;
            Fetch(tex, layer, h, n, c);
            // Morph into the next level's grid near the end of this level's range: odd vertices slide onto their even
            // neighbour (heights, normals and colours follow), so at the range's end the grid is the parent's exactly.
            vec2 mr = uMorph[int(iB.z)];
            float m = clamp((distance(uCam, vec3(xz.x, h, xz.y)) - mr.x) / (mr.y - mr.x), 0.0, 1.0);
            ivec2 odd = tex & 1;
            if (m > 0.0 && odd.x + odd.y > 0)
            {
                vec2 f = vec2(odd) * m;
                float hb, hc, hd; vec3 nb, nc, nd; vec4 cb, cc, cd;
                Fetch(tex - ivec2(odd.x, 0), layer, hb, nb, cb);
                Fetch(tex - ivec2(0, odd.y), layer, hc, nc, cc);
                Fetch(tex - odd, layer, hd, nd, cd);
                h = mix(mix(h, hb, f.x), mix(hc, hd, f.x), f.y);
                n = mix(mix(n, nb, f.x), mix(nc, nd, f.x), f.y);
                c = mix(mix(c, cb, f.x), mix(cc, cd, f.x), f.y);
                xz -= f * s;
            }
            if (skirt)
                h -= s * 3.0 + 1.0; // hangs down from the edge: covers a crack while a neighbour's page is coming
            vWorld = vec3(xz.x, h, xz.y);
            vNormal = n;
            vColor = c;
            vPage = vec4(iA.xy - iB.xy * s, s, iA.w);
            vDug = iB.w;
            gl_Position = uViewProj * vec4(vWorld, 1.0);
        }
        """;

    // The same look as TerrainRenderer's shader (sun, ambient, fog from Lighting.Glsl), plus the dug-cell mask.
    private static readonly string FragmentSrc = "#version 410 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        flat in vec4 vPage;
        flat in float vDug;
        uniform sampler2DArray uTex;
        uniform sampler2DArray uDug;
        uniform float uGroundLayer;
        uniform float uRockLayer;
        out vec4 FragColor;

        void main()
        {
            if (vDug > 0.5)
            {
                ivec2 cell = ivec2(floor((vWorld.xz - vPage.xy) / vPage.z));
                if (cell.x >= 0 && cell.y >= 0 && cell.x < 64 && cell.y < 64 && texelFetch(uDug, ivec3(cell, int(vPage.w)), 0).r > 0.5)
                    discard; // dug: drawn as blocks (TerrainRendererDug.cs)
            }
            vec3 n = normalize(vNormal);
            float ground = texture(uTex, vec3(vWorld.xz * 0.5, uGroundLayer)).r;
            float rock = 0.0;
            if (vColor.a > 0.01)
            {
                vec3 w = abs(n);
                w /= (w.x + w.y + w.z);
                rock = texture(uTex, vec3(vWorld.zy * 0.25, uRockLayer)).r * w.x
                     + texture(uTex, vec3(vWorld.xz * 0.25, uRockLayer)).r * w.y
                     + texture(uTex, vec3(vWorld.xy * 0.25, uRockLayer)).r * w.z;
            }
            vec3 col = vColor.rgb * mix(ground, rock, vColor.a) * 1.25;
            FragColor = vec4(ApplyFog(ApplyLightAt(col, n, 0.0, vWorld), vWorld), 1.0);
        }
        """;

    // ---- What's drawn from: the terrain, its levels and ranges -------------------------------------------------------

    /// <summary>The terrain the pages come from, and a number that changes with it (stale worker results are dropped).</summary>
    private sealed class Source(TerrainData t, int gen)
    {
        public readonly TerrainData Terrain = t;
        public readonly TerrainStream Stream = t.Stream!;
        public readonly int Gen = gen;
    }

    private Source? _src;
    private int _gen;
    private int _levels;
    private readonly int[] _nodesX = new int[MaxLevels], _nodesZ = new int[MaxLevels];
    private readonly float[] _range = new float[MaxLevels];
    private readonly Vector2[] _morph = new Vector2[MaxLevels];
    private float _gridMaxX, _gridMaxZ;

    // ---- Pages ------------------------------------------------------------------------------------------------------

    private readonly Dictionary<long, int> _pageOf = new(MaxPages * 2);
    private readonly long[] _pageKey = new long[MaxPages];
    private readonly int[] _pageUsed = new int[MaxPages];
    private readonly float[] _pageMin = new float[MaxPages], _pageMax = new float[MaxPages];
    private readonly int[] _pageDugStamp = new int[MaxPages];
    private readonly bool[] _pageDug = new bool[MaxPages];
    private int _pagesHeld;
    private readonly HashSet<long> _pending = new(MaxPending * 2);

    private readonly record struct Request(Source Src, long Key, int Level, int Nx, int Nz);

    private readonly record struct Result(int Gen, long Key, float[] Data, float Min, float Max);

    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly ConcurrentQueue<Result> _results = new();
    private readonly ConcurrentQueue<float[]> _free = new();
    private readonly SemaphoreSlim _wake = new(0);
    private Thread? _worker;
    private volatile bool _stop;

    // The worker's own scratch (only it touches these).
    private float[] _wh = [];
    private byte[] _wm = [];

    // ---- Per frame ----------------------------------------------------------------------------------------------------

    private readonly float[] _inst = new float[MaxInstances * FloatsPerInstance];
    private int _instances;
    private int _frame;
    private Vector3 _cam;
    private Vector4[] _planes = new Vector4[6];
    private readonly byte[] _dugScratch = new byte[PageCells * PageCells];

    /// <summary>Instances (quarter-nodes) drawn last frame, pages held, pages made since the map loaded, and the deepest level drawn.</summary>
    public (int Instances, int Pages, int Made, int Triangles) Stats => (_instances, _pagesHeld, _made, _instances * TrianglesPerInstance);

    private int _made;

    /// <summary>Triangles in one instance: the 32 x 32 grid and its four skirts (both faces).</summary>
    public const int TrianglesPerInstance = Quarter * Quarter * 2 + 4 * Quarter * 4;

    // ---- GL -----------------------------------------------------------------------------------------------------------

    private readonly bool _headless;
    private bool _glReady;
    private GlProgram _program;
    private int _locViewProj, _locCam, _locMorph, _locPages, _locDug, _locTex, _locGround, _locRock, _locFog, _locFogRange;
    private GlVertexArray _vao;
    private GlBuffer _ibo, _instVbo;
    private uint _pageTex, _dugTex;
    private int _indexCount;

    /// <param name="headless">No GL at all: the selection and the pages only (the self-test).</param>
    public StreamedTerrainRenderer(bool headless = false) => _headless = headless;

    /// <summary>Starts drawing <paramref name="t"/> (or nothing): every page is dropped, and the worker's work for the old terrain is thrown away.</summary>
    public void Reset(TerrainData? t)
    {
        _gen++;
        _pageOf.Clear();
        _pending.Clear();
        _pagesHeld = 0;
        _made = 0;
        _instances = 0;
        while (_requests.TryDequeue(out _))
        {
        }
        while (_results.TryDequeue(out var r))
            _free.Enqueue(r.Data);
        for (var p = 0; p < MaxPages; p++)
        {
            _pageKey[p] = -1;
            _pageUsed[p] = int.MinValue;
            _pageDugStamp[p] = -1;
            _pageDug[p] = false;
        }
        _src = t is { IsStreamed: true } ? new Source(t, _gen) : null;
        if (_src == null)
            return;
        var cells = Math.Max(t.CountX, t.CountZ) - 1;
        _levels = 1;
        while (_levels < MaxLevels && (PageCells << (_levels - 1)) < cells)
            _levels++;
        for (var l = 0; l < _levels; l++)
        {
            var size = PageCells << l;
            _nodesX[l] = (t.CountX - 1 + size - 1) / size;
            _nodesZ[l] = (t.CountZ - 1 + size - 1) / size;
            _range[l] = RangeScale * size * t.Cell;
            _morph[l] = new Vector2(_range[l] * 0.6f, _range[l] * 0.9f); // fully morphed well inside the range: neighbours a level up see the parent grid
        }
        _gridMaxX = t.OriginX + (t.CountX - 1) * t.Cell;
        _gridMaxZ = t.OriginZ + (t.CountZ - 1) * t.Cell;
        // The top two levels now, on this thread (five pages, a few milliseconds): there's always ground to fall back on,
        // from the first frame and in a one-frame picture (the map-select thumbnail).
        float[] h = new float[(PageVerts + 2) * (PageVerts + 2)];
        byte[] mats = new byte[h.Length];
        for (var l = _levels - 1; l >= Math.Max(_levels - 2, 0); l--)
            for (var nz = 0; nz < _nodesZ[l]; nz++)
            for (var nx = 0; nx < _nodesX[l]; nx++)
            {
                var r = new Request(_src, KeyOf(l, nx, nz), l, nx, nz);
                var data = _free.TryDequeue(out var d) ? d : new float[PageVerts * PageVerts * 4];
                var (min, max) = MakePage(r, data, h, mats);
                _pending.Add(r.Key);
                _results.Enqueue(new Result(_gen, r.Key, data, min, max));
            }
        if (_worker == null)
        {
            _worker = new Thread(Work) { IsBackground = true, Name = "Terrain pages", Priority = ThreadPriority.BelowNormal };
            _worker.Start();
        }
    }

    /// <summary>The level count and level 0's range (the self-test).</summary>
    public (int Levels, float Range0) Layout => (_levels, _range[0]);

    // ---- The worker ---------------------------------------------------------------------------------------------------

    private void Work()
    {
        while (!_stop)
        {
            _wake.Wait();
            while (_requests.TryDequeue(out var r))
            {
                if (r.Src.Gen != Volatile.Read(ref _gen))
                    continue;
                var data = _free.TryDequeue(out var d) ? d : new float[PageVerts * PageVerts * 4];
                if (_wh.Length == 0)
                {
                    _wh = new float[(PageVerts + 2) * (PageVerts + 2)];
                    _wm = new byte[_wh.Length];
                }
                var (min, max) = MakePage(r, data, _wh, _wm);
                _results.Enqueue(new Result(r.Src.Gen, r.Key, data, min, max));
            }
        }
    }

    /// <summary>A node's page: heights by the terrain's own features at the level's spacing, then normals and colours.</summary>
    private static (float Min, float Max) MakePage(Request r, float[] dst, float[] wh, byte[] wm)
    {
        var t = r.Src.Terrain;
        var st = r.Src.Stream;
        var s = st.Cell * (1 << r.Level);
        var size = PageCells << r.Level;
        double ox = st.Ox + (double)r.Nx * size * st.Cell, oz = st.Oz + (double)r.Nz * size * st.Cell;
        const int n = PageVerts + 2; // a one-sample apron for the normals
        TerrainGen.GenerateBlock(st.Spec, st.Boundary, ox - s, oz - s, n, n, s, wh, wm);
        float min = float.MaxValue, max = float.MinValue;
        var inv = 1f / (2f * (float)s);
        var o = 0;
        for (var j = 0; j < PageVerts; j++)
        for (var i = 0; i < PageVerts; i++)
        {
            var k = (j + 1) * n + i + 1;
            var h = wh[k];
            min = MathF.Min(min, h);
            max = MathF.Max(max, h);
            var nrm = Vector3.Normalize(new Vector3(-(wh[k + 1] - wh[k - 1]) * inv, 1, -(wh[k + n] - wh[k - n]) * inv));
            var (col, rock) = TerrainData.ColourOf(t.Surface, nrm, h, wm[k], (float)(ox + i * s), (float)(oz + j * s));
            dst[o++] = h;
            dst[o++] = nrm.X + 4f * MathF.Round(Math.Clamp(rock, 0f, 1f) * 255f);
            dst[o++] = nrm.Z;
            dst[o++] = B(col.X) + B(col.Y) * 256f + B(col.Z) * 65536f;
        }
        return (min, max);

        static float B(float v) => Math.Clamp(MathF.Round(v * 255f), 0f, 255f);
    }

    // ---- Each frame -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Takes the pages the worker finished (a few per frame onto the GPU), then picks what to draw from
    /// <paramref name="cam"/> inside <paramref name="planes"/> (TerrainRenderer.FrustumPlanes), asking for the pages that
    /// are missing. Allocation-free once warm.
    /// </summary>
    public void Update(Vector3 cam, ReadOnlySpan<Vector4> planes)
    {
        _instances = 0;
        if (_src is not { } src)
            return;
        _frame++;
        _cam = cam;
        planes.CopyTo(_planes);
        TakeResults(src, UploadsPerFrame);
        UpdateDugMasks(src.Terrain);
        for (var nz = 0; nz < _nodesZ[_levels - 1]; nz++)
        for (var nx = 0; nx < _nodesX[_levels - 1]; nx++)
            Select(_levels - 1, nx, nz);
        if (_requests.Count > 0)
            _wake.Release();
    }

    private static long KeyOf(int level, int nx, int nz) => ((long)level << 48) | ((long)nz << 24) | (uint)nx;

    /// <summary>
    /// Draws node (<paramref name="nx"/>, <paramref name="nz"/>) of <paramref name="level"/> or its children. False when
    /// it can't (out of its level's range, or its page isn't ready): the parent draws that quarter itself.
    /// </summary>
    private bool Select(int level, int nx, int nz)
    {
        var src = _src!;
        var t = src.Terrain;
        var size = (PageCells << level) * t.Cell;
        float x0 = t.OriginX + nx * size, z0 = t.OriginZ + nz * size;
        if (x0 >= _gridMaxX || z0 >= _gridMaxZ)
            return true; // past the terrain's edge: nothing to draw
        var key = KeyOf(level, nx, nz);
        var page = _pageOf.TryGetValue(key, out var p) ? p : -1;
        var min = new Vector3(x0, page >= 0 ? _pageMin[page] : src.Stream.MinHeight, z0);
        var max = new Vector3(x0 + size, page >= 0 ? _pageMax[page] : src.Stream.MaxHeight, z0 + size);
        if (!SphereTouchesBox(_cam, _range[level], min, max))
            return false;
        if (!TerrainRenderer.Visible(_planes, min, max))
            return true; // out of view: nothing, and no page wanted for it
        if (page < 0)
        {
            Ask(src, key, level, nx, nz);
            return false;
        }
        _pageUsed[page] = _frame;
        if (level == 0 || !SphereTouchesBox(_cam, _range[level - 1], min, max))
        {
            for (var q = 0; q < 4; q++)
                AddQuarter(page, level, x0, z0, size, q);
            return true;
        }
        for (var q = 0; q < 4; q++)
            if (!Select(level - 1, nx * 2 + (q & 1), nz * 2 + (q >> 1)))
                AddQuarter(page, level, x0, z0, size, q);
        return true;
    }

    private void AddQuarter(int page, int level, float x0, float z0, float size, int q)
    {
        var half = size * 0.5f;
        float qx = x0 + (q & 1) * half, qz = z0 + (q >> 1) * half;
        if (qx >= _gridMaxX || qz >= _gridMaxZ || _instances == MaxInstances)
            return;
        var min = new Vector3(qx, _pageMin[page] - 4f, qz); // (skirts hang a little below)
        var max = new Vector3(qx + half, _pageMax[page], qz + half);
        if (!TerrainRenderer.Visible(_planes, min, max))
            return;
        var o = _instances++ * FloatsPerInstance;
        _inst[o] = qx;
        _inst[o + 1] = qz;
        _inst[o + 2] = (float)(_src!.Terrain.Cell * (1 << level));
        _inst[o + 3] = page;
        _inst[o + 4] = (q & 1) * Quarter;
        _inst[o + 5] = (q >> 1) * Quarter;
        _inst[o + 6] = level;
        _inst[o + 7] = level == 0 && _pageDug[page] ? 1f : 0f;
    }

    private void Ask(Source src, long key, int level, int nx, int nz)
    {
        if (_pending.Count >= MaxPending || !_pending.Add(key))
            return;
        _requests.Enqueue(new Request(src, key, level, nx, nz));
    }

    private static bool SphereTouchesBox(Vector3 c, float r, Vector3 min, Vector3 max)
    {
        var d = Vector3.Max(Vector3.Max(min - c, c - max), Vector3.Zero);
        return d.LengthSquared() <= r * r;
    }

    /// <summary>Finished pages onto the GPU (a few a frame), each into a free layer or the least recently drawn one's.</summary>
    private void TakeResults(Source src, int most)
    {
        for (var n = 0; n < most && _results.TryDequeue(out var r); n++)
        {
            if (r.Gen != src.Gen)
            {
                _free.Enqueue(r.Data);
                n--;
                continue;
            }
            _pending.Remove(r.Key);
            var layer = FreeLayer();
            if (layer < 0)
            {
                _free.Enqueue(r.Data); // everything was drawn this frame: try again later
                continue;
            }
            if (_pageKey[layer] >= 0)
                _pageOf.Remove(_pageKey[layer]);
            else
                _pagesHeld++;
            _pageKey[layer] = r.Key;
            _pageOf[r.Key] = layer;
            _pageMin[layer] = r.Min;
            _pageMax[layer] = r.Max;
            _pageUsed[layer] = _frame;
            _pageDugStamp[layer] = -1;
            _pageDug[layer] = false;
            _made++;
            if (!_headless && _glReady)
            {
                Native.glActiveTexture(GL_TEXTURE0 + PageUnit);
                Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _pageTex);
                fixed (float* p = r.Data)
                    Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, layer, PageVerts, PageVerts, 1, GL_RGBA, GL_FLOAT, p);
                Native.glActiveTexture(GL_TEXTURE0);
            }
            _free.Enqueue(r.Data);
        }
    }

    private int FreeLayer()
    {
        int best = -1, oldest = int.MaxValue;
        for (var p = 0; p < MaxPages; p++)
        {
            if (_pageKey[p] < 0)
                return p;
            if (_pageUsed[p] < oldest && _pageUsed[p] < _frame)
            {
                oldest = _pageUsed[p];
                best = p;
            }
        }
        return best;
    }

    /// <summary>Level-0 pages over dug ground get a mask of their dug cells (redone when those cells change).</summary>
    private void UpdateDugMasks(TerrainData t)
    {
        if (t.EditsIfMade is not { } e || e.TouchedCells == 0 && !_anyDugMask)
            return;
        var any = false;
        for (var p = 0; p < MaxPages; p++)
        {
            var key = _pageKey[p];
            if (key < 0 || key >> 48 != 0)
                continue;
            int nx = (int)(key & 0xFFFFFF), nz = (int)((key >> 24) & 0xFFFFFF);
            // The page's 64 cells are 2 x 2 of the dug-drawing chunks (TerrainEdits.ChunkCells = 32).
            int cx = nx * 2, cz = nz * 2;
            var stamp = 0;
            for (var b = 0; b < 2; b++)
            for (var a = 0; a < 2; a++)
                if (cx + a < e.ChunksX && cz + b < e.ChunksZ)
                    stamp = stamp * 31 + e.ChunkVersion[(cz + b) * e.ChunksX + cx + a];
            if (stamp == _pageDugStamp[p])
            {
                any |= _pageDug[p];
                continue;
            }
            _pageDugStamp[p] = stamp;
            var dug = false;
            int i0 = nx * PageCells, j0 = nz * PageCells;
            for (var j = 0; j < PageCells; j++)
            for (var i = 0; i < PageCells; i++)
            {
                var d = e.Touched(i0 + i, j0 + j);
                _dugScratch[j * PageCells + i] = d ? (byte)255 : (byte)0;
                dug |= d;
            }
            _pageDug[p] = dug;
            any |= dug;
            if (dug && !_headless && _glReady)
            {
                Native.glActiveTexture(GL_TEXTURE0 + DugUnit);
                Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _dugTex);
                fixed (byte* bp = _dugScratch)
                    Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, p, PageCells, PageCells, 1, GL_RED, GL_UNSIGNED_BYTE, bp);
                Native.glActiveTexture(GL_TEXTURE0);
            }
        }
        _anyDugMask = any;
    }

    private bool _anyDugMask;

    /// <summary>The instances picked last <see cref="Update"/>: (origin x, z, spacing, page, texel x, z, level, dug) each (the self-test).</summary>
    public ReadOnlySpan<float> Instances => _inst.AsSpan(0, _instances * FloatsPerInstance);

    /// <summary>Has the worker nothing left to do (the self-test waits on it)?</summary>
    public bool Settled => _requests.IsEmpty && _results.IsEmpty && _pending.Count == 0;

    // ---- Drawing ------------------------------------------------------------------------------------------------------

    /// <summary>Picks and draws the terrain for <paramref name="gfx"/>'s camera (fog and camera as the box renderer has them).</summary>
    public void Draw(Renderer gfx, ReadOnlySpan<Vector4> planes)
    {
        if (!_glReady)
            InitGl();
        Update(gfx.CameraPosition, planes);
        if (_instances == 0)
            return;
        var vp = gfx.ViewProj;
        _program.Use();
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        var cam = gfx.CameraPosition;
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        fixed (Vector2* m = _morph)
            Native.glUniform2fv(_locMorph, MaxLevels, (float*)m);
        Native.glUniform1i(_locTex, 0);
        Native.glUniform1i(_locPages, PageUnit);
        Native.glUniform1i(_locDug, DugUnit);
        Native.glUniform1f(_locGround, (float)Material.Dirt);
        Native.glUniform1f(_locRock, (float)Material.Concrete);
        var fog = gfx.FogColor;
        Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_locFogRange, gfx.FogRange.X, gfx.FogRange.Y);
        Native.glActiveTexture(GL_TEXTURE0 + PageUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _pageTex);
        Native.glActiveTexture(GL_TEXTURE0 + DugUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _dugTex);
        Native.glActiveTexture(GL_TEXTURE0);
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        fixed (float* p = _inst)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, (nint)_instances * FloatsPerInstance * sizeof(float), p);
        PerfCounters.Draw(_indexCount * _instances);
        Native.glDrawElementsInstanced(GL_TRIANGLES, _indexCount, GL_UNSIGNED_INT, (void*)0, _instances);
    }

    private void InitGl()
    {
        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
        _program = CreateProgram();
        Native.glAttachShader(_program, vs);
        Native.glAttachShader(_program, fs);
        Native.glLinkProgram(_program);
        GlDebug.Label(GL_PROGRAM, _program.Id, "StreamedTerrainRenderer");
        int linked;
        Native.glGetProgramiv(_program, GL_LINK_STATUS, &linked);
        vs.Dispose();
        fs.Dispose();
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(_program, 2048, &len, log);
            throw new InvalidOperationException("streamed terrain shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
        }
        _locViewProj = Uniform("uViewProj\0"u8);
        _locCam = Uniform("uCam\0"u8);
        _locMorph = Uniform("uMorph\0"u8);
        _locPages = Uniform("uPages\0"u8);
        _locDug = Uniform("uDug\0"u8);
        _locTex = Uniform("uTex\0"u8);
        _locGround = Uniform("uGroundLayer\0"u8);
        _locRock = Uniform("uRockLayer\0"u8);
        _locFog = Uniform("uFog\0"u8);
        _locFogRange = Uniform("uFogRange\0"u8);
        Lighting.Upload(_program);

        // One quarter-node grid (33 x 33 vertices, numbered row by row) and its four skirts, as indices only.
        var idx = new List<uint>(Quarter * Quarter * 6 + 4 * Quarter * 12);
        const uint nv = QuarterVerts;
        for (uint z = 0; z < Quarter; z++)
        for (uint x = 0; x < Quarter; x++)
        {
            uint a = z * nv + x, b = a + 1, c = a + nv, d = c + 1;
            idx.AddRange([a, c, d, a, d, b]); // the same winding as TerrainRenderer's cells
        }
        for (uint side = 0; side < 4; side++)
        for (uint k = 0; k < Quarter; k++)
        {
            uint e0 = Edge(side, k), e1 = Edge(side, k + 1);
            uint s0 = nv * nv + side * nv + k, s1 = s0 + 1;
            idx.AddRange([e0, s0, s1, e0, s1, e1, e0, s1, s0, e0, e1, s1]); // both faces
        }
        _indexCount = idx.Count;
        _vao = GenVertexArray();
        _vao.Bind();
        _ibo = GenBuffer();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ibo);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(idx);
        fixed (uint* p = span)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, span.Length * sizeof(uint), p, GL_STATIC_DRAW);
        _instVbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        Native.glBufferData(GL_ARRAY_BUFFER, MaxInstances * FloatsPerInstance * sizeof(float), null, GL_DYNAMIC_DRAW);
        for (uint a = 0; a < 2; a++)
        {
            Native.glVertexAttribPointer(a, 4, GL_FLOAT, false, FloatsPerInstance * sizeof(float), (void*)(a * 4 * sizeof(float)));
            EnableVertexAttribArray(a);
            Native.glVertexAttribDivisor(a, 1);
        }

        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        _pageTex = tex.Id;
        Native.glActiveTexture(GL_TEXTURE0 + PageUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _pageTex);
        Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA32F, PageVerts, PageVerts, MaxPages, 0, GL_RGBA, GL_FLOAT, null);
        Nearest();
        GlTexture dug = default;
        Native.glGenTextures(1, &dug);
        _dugTex = dug.Id;
        Native.glActiveTexture(GL_TEXTURE0 + DugUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _dugTex);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_R8, PageCells, PageCells, MaxPages, 0, GL_RED, GL_UNSIGNED_BYTE, null);
        Nearest();
        Native.glActiveTexture(GL_TEXTURE0);
        _glReady = true;
        Console.WriteLine($"[terrain] streamed terrain drawing ready: {MaxPages} pages of {PageVerts} x {PageVerts} ({MaxPages * PageVerts * PageVerts * 16 / (1024 * 1024)} MB), "
                          + $"{_indexCount / 3} triangles an instance, GL error 0x{(uint)GlDebug.Error():X}");

        static uint Edge(uint side, uint k) => side switch
        {
            0 => k,
            1 => k * nv + Quarter,
            2 => Quarter * nv + k,
            _ => k * nv,
        };

        static void Nearest()
        {
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        }
    }

    private int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }
}
