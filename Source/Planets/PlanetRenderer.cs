using System.Collections.Concurrent;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws a <see cref="Planetoid"/>'s ground: the streamed terrain's CDLOD (StreamedTerrainRenderer.cs, Strugar 2010) on
/// each of the six cube faces, bent onto the sphere. A face is a quadtree of nodes of <see cref="Planetoid.PageCells"/>
/// cells at their level's spacing (the top level is the whole face); a node near enough the camera is split, so detail
/// falls off with distance. A node's page (65 x 65 texels) holds each vertex's position relative to the page's origin,
/// its normal and colour, worked out on a worker thread from the planetoid's features; the vertex shader only adds the
/// origin, so far-away pages lose nothing to float precision. Vertices morph into the next level's grid near the end of
/// their range (positions, normals and colours together), so levels meet without cracks or pops; skirts hang down along
/// "up" to hide what's left while a page is coming. Nodes on the far side of the planet are skipped.
/// <para>
/// The sky light and fog lean on the local "up" (away from the centre), not on world +Y: the shader corrects
/// <c>ApplyLightAt</c>'s sky/ground ambient and colours the haze with the sky gradient seen from where the camera stands.
/// </para>
/// </summary>
public sealed unsafe class PlanetRenderer
{
    private const int PageCells = Planetoid.PageCells;
    private const int PageVerts = PageCells + 1;
    private const int Quarter = PageCells / 2;
    private const int QuarterVerts = Quarter + 1;
    private const int Apron = PageVerts + 2;

    /// <summary>A level is drawn within this many of its node widths of the camera.</summary>
    public const float RangeScale = 2.4f;

    /// <summary>Pages kept on the GPU (two 65 x 65 RGBA32F layers each: 135 KB).</summary>
    public const int MaxPages = 512;

    private const int MaxLevels = 16;
    private const int MaxInstances = 4096;
    private const int FloatsPerInstance = 8;
    private const int UploadsPerFrame = 8;
    private const int MaxPending = 64;
    private const int PosUnit = 10, NrmUnit = 11; // texture units (0: the box texture array, 12-14: the light grid)

    private const string VertexSrc = """
        #version 410 core
        layout(location = 0) in vec4 iA; // page origin (world x, y, z); page layer
        layout(location = 1) in vec4 iB; // quarter's first texel x, z; level; spacing (metres)
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        uniform vec3 uCenter;
        uniform vec3 uTexOrigin;
        uniform vec2 uMorph[16];        // per level: morph start, end (metres)
        uniform sampler2DArray uPos;    // per texel: position from the page origin (x, y, z), rockiness
        uniform sampler2DArray uNrm;    // per texel: normal (x, y, z), colour (r + g 256 + b 65536)
        out vec3 vWorld;
        out vec3 vTex;
        out vec3 vNormal;
        out vec3 vUp;
        out vec4 vColor;
        const int Q = 32;
        const int N = Q + 1;

        void Fetch(ivec2 t, int layer, out vec3 p, out vec3 n, out vec4 c)
        {
            vec4 a = texelFetch(uPos, ivec3(t, layer), 0);
            vec4 b = texelFetch(uNrm, ivec3(t, layer), 0);
            p = a.xyz;
            n = b.xyz;
            uint q = uint(b.w);
            c = vec4(float(q & 255u) / 255.0, float((q >> 8) & 255u) / 255.0, float(q >> 16) / 255.0, a.w);
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
            vec3 p; vec3 n; vec4 c;
            Fetch(tex, layer, p, n, c);
            // Morph into the next level's grid near the end of this level's range: odd vertices slide onto their even
            // neighbour (as on the flat streamed terrain), so at the range's end the grid is the parent's exactly.
            vec2 mr = uMorph[int(iB.z)];
            float m = clamp((distance(uCam, iA.xyz + p) - mr.x) / (mr.y - mr.x), 0.0, 1.0);
            ivec2 odd = tex & 1;
            if (m > 0.0 && odd.x + odd.y > 0)
            {
                vec2 f = vec2(odd) * m;
                vec3 pb, pc, pd; vec3 nb, nc, nd; vec4 cb, cc, cd;
                Fetch(tex - ivec2(odd.x, 0), layer, pb, nb, cb);
                Fetch(tex - ivec2(0, odd.y), layer, pc, nc, cc);
                Fetch(tex - odd, layer, pd, nd, cd);
                p = mix(mix(p, pb, f.x), mix(pc, pd, f.x), f.y);
                n = mix(mix(n, nb, f.x), mix(nc, nd, f.x), f.y);
                c = mix(mix(c, cb, f.x), mix(cc, cd, f.x), f.y);
            }
            vec3 world = iA.xyz + p;
            vec3 up = normalize(world - uCenter);
            if (skirt)
                world -= up * (iB.w * 3.0 + 1.0); // hangs down from the edge: covers a crack while a neighbour's page is coming
            vWorld = world;
            vTex = world - uTexOrigin; // (small numbers for the texture lookups: the origin moves in whole texture repeats)
            vNormal = n;
            vUp = up;
            vColor = c;
            gl_Position = uViewProj * vec4(world, 1.0);
        }
        """;

    private static readonly string FragmentSrc = "#version 410 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vTex;
        in vec3 vNormal;
        in vec3 vUp;
        in vec4 vColor;
        uniform sampler2DArray uTex;
        uniform float uGroundLayer;
        uniform float uRockLayer;
        uniform mat3 uToLocal; // world -> the camera's local frame (y = up there)
        uniform float uDip;    // sin of how far the limb is below level (the sky's horizon drops to it: SkyRenderer)
        out vec4 FragColor;

        vec3 Amb(float y)
        {
            float skyLum = max(dot(uFog, vec3(0.3333)), 0.05);
            vec3 sky = mix(uAmbSky, uFog / skyLum * dot(uAmbSky, vec3(0.3333)), uSkyTint);
            return mix(uAmbGround, sky, y * 0.5 + 0.5);
        }

        // SkyGradient with "up" the camera's: dl is the direction in the local frame, dw the same in the world.
        vec3 GradientLocal(vec3 dl, vec3 dw)
        {
            float y = (dl.y + uDip) / (1.0 + uDip);
            vec3 c = y >= 0.0 ? mix(uSkyHorizon, uSkyZenith, sqrt(min(y, 1.0)))
                              : mix(uSkyHorizon, uSkyGround, smoothstep(0.0, 0.3, -y));
            float s = max(dot(dw, uSkyHazeDir), 0.0);
            float h = uSkyHaze.w * pow(s, 6.0) * (1.0 - 0.6 * min(abs(y), 1.0));
            return mix(c, uSkyHaze.rgb, clamp(h, 0.0, 1.0));
        }

        float Tri(vec3 p, vec3 w, float layer)
        {
            return texture(uTex, vec3(p.zy, layer)).r * w.x + texture(uTex, vec3(p.xz, layer)).r * w.y + texture(uTex, vec3(p.xy, layer)).r * w.z;
        }

        void main()
        {
            vec3 n = normalize(vNormal);
            vec3 up = normalize(vUp);
            vec3 w = abs(n);
            w /= (w.x + w.y + w.z);
            float ground = Tri(vTex * 0.5, w, uGroundLayer);
            float rock = vColor.a > 0.01 ? Tri(vTex * 0.25, w, uRockLayer) : 0.0;
            vec3 col = vColor.rgb * mix(ground, rock, vColor.a) * 1.25;
            vec3 lit = ApplyLightAt(col, n, 0.0, vWorld);
            lit += col * (Amb(dot(n, up)) - Amb(n.y)); // the sky is above "up" here, not above world +Y
            if (uFogRange.y > uFogRange.x)
            {
                vec3 v = vWorld - uCam;
                float dist = length(v);
                float f = clamp((dist - uFogRange.x) / (uFogRange.y - uFogRange.x), 0.0, 1.0) * uFogMax;
                vec3 d = v / max(dist, 1e-4);
                vec3 fogCol = uSkyFog > 0.5 ? GradientLocal(uToLocal * d, d) : uFog;
                lit = mix(lit, fogCol, f);
            }
            FragColor = vec4(lit, 1.0);
        }
        """;

    // ---- What's drawn from ------------------------------------------------------------------------------------------

    private sealed class Source(Planetoid p, int gen)
    {
        public readonly Planetoid Planet = p;
        public readonly int Gen = gen;
    }

    private Source? _src;
    private int _gen;
    private int _levels;
    private readonly float[] _range = new float[MaxLevels];
    private readonly Vector2[] _morph = new Vector2[MaxLevels];
    private double _cell;

    // ---- Pages ------------------------------------------------------------------------------------------------------

    private readonly Dictionary<long, int> _pageOf = new(MaxPages * 2);
    private readonly long[] _pageKey = new long[MaxPages];
    private readonly int[] _pageUsed = new int[MaxPages];
    private readonly Vector3[] _pageOrigin = new Vector3[MaxPages];
    private readonly Vector3[] _pageMin = new Vector3[MaxPages * 5], _pageMax = new Vector3[MaxPages * 5]; // the page, then its four quarters
    private int _pagesHeld;
    private readonly HashSet<long> _pending = new(MaxPending * 2);

    private readonly record struct Request(Source Src, long Key, int Face, int Level, int Nx, int Nz);

    private sealed class Result
    {
        public int Gen;
        public long Key;
        public readonly float[] Pos = new float[PageVerts * PageVerts * 4];
        public readonly float[] Nrm = new float[PageVerts * PageVerts * 4];
        public Vector3 Origin;
        public readonly Vector3[] Min = new Vector3[5], Max = new Vector3[5];
    }

    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly ConcurrentQueue<Result> _results = new();
    private readonly ConcurrentQueue<Result> _free = new();
    private readonly SemaphoreSlim _wake = new(0);
    private Thread? _worker;
    private volatile bool _stop;

    // The worker's own scratch (only it touches it; Reset's own pages use another).
    private double[] _wp = new double[Apron * Apron * 3];
    private double[] _wh = new double[Apron * Apron];

    // ---- Per frame ----------------------------------------------------------------------------------------------------

    private readonly float[] _inst = new float[MaxInstances * FloatsPerInstance];
    private int _instances;
    private int _frame;
    private Vector3 _cam, _camUp;
    private Vector4[] _planes = new Vector4[6];
    private int _made;

    /// <summary>Triangles in one instance: the 32 x 32 grid and its four skirts (both faces).</summary>
    public const int TrianglesPerInstance = Quarter * Quarter * 2 + 4 * Quarter * 4;

    /// <summary>Instances (quarter-nodes) drawn last frame, pages held, pages made since the planetoid was set, triangles.</summary>
    public (int Instances, int Pages, int Made, int Triangles) Stats => (_instances, _pagesHeld, _made, _instances * TrianglesPerInstance);

    /// <summary>Has the worker nothing left to do (the self-test waits on it)?</summary>
    public bool Settled => _requests.IsEmpty && _results.IsEmpty && _pending.Count == 0;

    /// <summary>The instances picked last <see cref="Update"/>: (origin x, y, z, page, texel x, z, level, spacing) each (the self-test).</summary>
    public ReadOnlySpan<float> Instances => _inst.AsSpan(0, _instances * FloatsPerInstance);

    /// <summary>The level count and level 0's range.</summary>
    public (int Levels, float Range0) Layout => (_levels, _range[0]);

    // ---- GL -----------------------------------------------------------------------------------------------------------

    private readonly bool _headless;
    private bool _glReady;
    private GlProgram _program;
    private int _locViewProj, _locCam, _locCenter, _locTexOrigin, _locMorph, _locPos, _locNrm, _locTex, _locGround, _locRock, _locFog, _locFogRange, _locToLocal, _locDip;
    private GlVertexArray _vao;
    private GlBuffer _ibo, _instVbo;
    private uint _posTex, _nrmTex;
    private int _indexCount;

    /// <param name="headless">No GL at all: the selection and the pages only (the self-test).</param>
    public PlanetRenderer(bool headless = false) => _headless = headless;

    /// <summary>The planetoid being drawn (null: none).</summary>
    public Planetoid? Planet => _src?.Planet;

    /// <summary>Starts drawing <paramref name="p"/> (or nothing): every page is dropped, the worker's old work thrown away.</summary>
    public void Reset(Planetoid? p)
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
            _free.Enqueue(r);
        for (var k = 0; k < MaxPages; k++)
        {
            _pageKey[k] = -1;
            _pageUsed[k] = int.MinValue;
        }
        _src = p == null ? null : new Source(p, _gen);
        if (_src == null)
            return;
        _levels = Math.Min(p!.Levels, MaxLevels);
        _cell = p.Cell;
        for (var l = 0; l < _levels; l++)
        {
            _range[l] = (float)(RangeScale * (PageCells << l) * _cell);
            _morph[l] = new Vector2(_range[l] * 0.6f, _range[l] * 0.9f);
        }
        // The top two levels now, on this thread (30 pages): there's always ground, from the first frame.
        var wp = new double[Apron * Apron * 3];
        var wh = new double[Apron * Apron];
        for (var l = _levels - 1; l >= Math.Max(_levels - 2, 0); l--)
        {
            var per = 1 << (_levels - 1 - l);
            for (var f = 0; f < 6; f++)
            for (var nz = 0; nz < per; nz++)
            for (var nx = 0; nx < per; nx++)
            {
                var req = new Request(_src, KeyOf(f, l, nx, nz), f, l, nx, nz);
                var res = _free.TryDequeue(out var d) ? d : new Result();
                MakePage(req, res, wp, wh);
                _pending.Add(req.Key);
                _results.Enqueue(res);
            }
        }
        if (_worker == null)
        {
            _worker = new Thread(Work) { IsBackground = true, Name = "Planet pages", Priority = ThreadPriority.BelowNormal };
            _worker.Start();
        }
    }

    private void Work()
    {
        while (!_stop)
        {
            _wake.Wait();
            while (_requests.TryDequeue(out var r))
            {
                if (r.Src.Gen != Volatile.Read(ref _gen))
                    continue;
                var res = _free.TryDequeue(out var d) ? d : new Result();
                MakePage(r, res, _wp, _wh);
                _results.Enqueue(res);
            }
        }
    }

    private static long KeyOf(int face, int level, int nx, int nz) => ((long)face << 56) | ((long)level << 48) | ((long)nz << 24) | (uint)nx;

    /// <summary>A node's page: positions from the planetoid's own features at the level's spacing, then normals and colours.</summary>
    private static void MakePage(Request r, Result dst, double[] wp, double[] wh)
    {
        var pl = r.Src.Planet;
        var s = 1 << r.Level;
        int i0 = r.Nx * PageCells * s, j0 = r.Nz * PageCells * s;
        for (var jj = 0; jj < Apron; jj++)
        for (var ii = 0; ii < Apron; ii++)
        {
            pl.DirOf(r.Face, i0 + (ii - 1) * s, j0 + (jj - 1) * s, out var x, out var y, out var z);
            var h = pl.HeightAt(x, y, z);
            var rad = pl.Radius + h;
            var k = jj * Apron + ii;
            wh[k] = h;
            wp[k * 3] = x * rad;
            wp[k * 3 + 1] = y * rad;
            wp[k * 3 + 2] = z * rad;
        }
        pl.DirOf(r.Face, i0 + Quarter * s, j0 + Quarter * s, out var ox, out var oy, out var oz);
        double orx = ox * pl.Radius, ory = oy * pl.Radius, orz = oz * pl.Radius; // the origin, from the centre
        dst.Gen = r.Src.Gen;
        dst.Key = r.Key;
        dst.Origin = new Vector3((float)(pl.Cx + orx), (float)(pl.Cy + ory), (float)(pl.Cz + orz));
        for (var q = 0; q < 5; q++)
        {
            dst.Min[q] = new Vector3(float.MaxValue);
            dst.Max[q] = new Vector3(float.MinValue);
        }
        var surf = pl.Surface;
        var o = 0;
        for (var j = 0; j < PageVerts; j++)
        for (var i = 0; i < PageVerts; i++)
        {
            var k = (j + 1) * Apron + i + 1;
            double px = wp[k * 3], py = wp[k * 3 + 1], pz = wp[k * 3 + 2];
            // Normal: across the grid's j and i directions (v x u is "out", the faces' handedness).
            var du = new Vector3((float)(wp[(k + 1) * 3] - wp[(k - 1) * 3]), (float)(wp[(k + 1) * 3 + 1] - wp[(k - 1) * 3 + 1]), (float)(wp[(k + 1) * 3 + 2] - wp[(k - 1) * 3 + 2]));
            var dv = new Vector3((float)(wp[(k + Apron) * 3] - wp[(k - Apron) * 3]), (float)(wp[(k + Apron) * 3 + 1] - wp[(k - Apron) * 3 + 1]), (float)(wp[(k + Apron) * 3 + 2] - wp[(k - Apron) * 3 + 2]));
            var n = Vector3.Normalize(Vector3.Cross(dv, du));
            var len = Math.Sqrt(px * px + py * py + pz * pz);
            var up = new Vector3((float)(px / len), (float)(py / len), (float)(pz / len));
            var c = Math.Clamp(Vector3.Dot(n, up), -1f, 1f);
            var h = (float)wh[k];
            var local = new Vector3(MathF.Sqrt(MathF.Max(0, 1 - c * c)), c, 0);
            var slope = MathF.Acos(c) * (180f / MathF.PI);
            var rule = slope >= surf.RockSlope ? TerrainMaterial.Rock
                : slope >= surf.DirtSlope ? TerrainMaterial.Dirt
                : surf.SandBelow is { } sb && h <= sb ? TerrainMaterial.Sand
                : TerrainMaterial.Grass;
            var (col, rock) = TerrainData.ColourOf(surf, local, h, (byte)rule, (float)(px + py * 0.5), (float)(pz - py * 0.5));
            var rel = new Vector3((float)(px - orx), (float)(py - ory), (float)(pz - orz));
            dst.Pos[o] = rel.X;
            dst.Pos[o + 1] = rel.Y;
            dst.Pos[o + 2] = rel.Z;
            dst.Pos[o + 3] = Math.Clamp(rock, 0f, 1f);
            dst.Nrm[o] = n.X;
            dst.Nrm[o + 1] = n.Y;
            dst.Nrm[o + 2] = n.Z;
            dst.Nrm[o + 3] = B(col.X) + B(col.Y) * 256f + B(col.Z) * 65536f;
            o += 4;
            var w = dst.Origin + rel;
            Grow(dst, 0, w);
            // A vertex on a quarter's edge belongs to both quarters.
            for (var q = 0; q < 4; q++)
            {
                int qi = (q & 1) * Quarter, qj = (q >> 1) * Quarter;
                if (i >= qi && i <= qi + Quarter && j >= qj && j <= qj + Quarter)
                    Grow(dst, 1 + q, w);
            }
        }
        // Skirts hang below the edges.
        var pad = new Vector3((float)(pl.Cell * s * 3 + 1));
        for (var q = 0; q < 5; q++)
        {
            dst.Min[q] -= pad;
            dst.Max[q] += pad;
        }

        static float B(float v) => Math.Clamp(MathF.Round(v * 255f), 0f, 255f);

        static void Grow(Result d, int q, Vector3 w)
        {
            d.Min[q] = Vector3.Min(d.Min[q], w);
            d.Max[q] = Vector3.Max(d.Max[q], w);
        }
    }

    // ---- Each frame -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Takes finished pages (a few per frame onto the GPU), then picks what to draw from <paramref name="cam"/> inside
    /// <paramref name="planes"/> (TerrainRenderer.FrustumPlanes), asking for pages that are missing.
    /// </summary>
    public void Update(Vector3 cam, ReadOnlySpan<Vector4> planes)
    {
        _instances = 0;
        if (_src is not { } src)
            return;
        _frame++;
        _cam = cam;
        var rel = cam - src.Planet.Center;
        _camUp = rel.LengthSquared() > 1e-6f ? Vector3.Normalize(rel) : Vector3.UnitY;
        planes.CopyTo(_planes);
        TakeResults(src, UploadsPerFrame);
        for (var f = 0; f < 6; f++)
            Select(f, _levels - 1, 0, 0);
        if (_requests.Count > 0)
            _wake.Release();
    }

    private bool Select(int face, int level, int nx, int nz)
    {
        var src = _src!;
        var key = KeyOf(face, level, nx, nz);
        var page = _pageOf.TryGetValue(key, out var p) ? p : -1;
        Vector3 min, max;
        if (page >= 0)
        {
            min = _pageMin[page * 5];
            max = _pageMax[page * 5];
        }
        else
            Estimate(src.Planet, face, level, nx, nz, out min, out max);
        if (!SphereTouchesBox(_cam, _range[level], min, max))
            return false;
        if (!TerrainRenderer.Visible(_planes, min, max) || BehindPlanet(src.Planet, min, max))
            return true; // out of view: nothing, and no page wanted for it
        if (page < 0)
        {
            Ask(src, key, face, level, nx, nz);
            return false;
        }
        _pageUsed[page] = _frame;
        if (level == 0 || !SphereTouchesBox(_cam, _range[level - 1], min, max))
        {
            for (var q = 0; q < 4; q++)
                AddQuarter(page, level, q);
            return true;
        }
        for (var q = 0; q < 4; q++)
            if (!Select(face, level - 1, nx * 2 + (q & 1), nz * 2 + (q >> 1)))
                AddQuarter(page, level, q);
        return true;
    }

    /// <summary>A node's bounds before its page is made: nine directions over it at the lowest and highest the ground can be.</summary>
    private static void Estimate(Planetoid pl, int face, int level, int nx, int nz, out Vector3 min, out Vector3 max)
    {
        var size = PageCells << level;
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        double r0 = pl.Radius + pl.MinHeight, r1 = pl.Radius + pl.MaxHeight;
        for (var b = 0; b <= 2; b++)
        for (var a = 0; a <= 2; a++)
        {
            pl.DirOf(face, (nx + a * 0.5) * size, (nz + b * 0.5) * size, out var x, out var y, out var z);
            var lo = new Vector3((float)(pl.Cx + x * r0), (float)(pl.Cy + y * r0), (float)(pl.Cz + z * r0));
            var hi = new Vector3((float)(pl.Cx + x * r1), (float)(pl.Cy + y * r1), (float)(pl.Cz + z * r1));
            min = Vector3.Min(min, Vector3.Min(lo, hi));
            max = Vector3.Max(max, Vector3.Max(lo, hi));
        }
        // (a face's middle bulges out past its corners and edge midpoints: pad by the sagitta of the node's arc)
        var arc = size * pl.Cell;
        var sag = (float)(arc * arc / (8 * pl.Radius) + pl.Cell * (1 << level) * 3 + 1);
        min -= new Vector3(sag);
        max += new Vector3(sag);
    }

    /// <summary>Is the box wholly on the far side of the planet from the camera (past the plane through the centre)?</summary>
    private bool BehindPlanet(Planetoid pl, Vector3 min, Vector3 max)
    {
        var c = (min + max) * 0.5f - pl.Center;
        var r = (max - min).Length() * 0.5f;
        return Vector3.Dot(c, _camUp) + r < 0;
    }

    private void AddQuarter(int page, int level, int q)
    {
        if (_instances == MaxInstances)
            return;
        var min = _pageMin[page * 5 + 1 + q];
        var max = _pageMax[page * 5 + 1 + q];
        if (!TerrainRenderer.Visible(_planes, min, max))
            return;
        var o = _instances++ * FloatsPerInstance;
        var origin = _pageOrigin[page];
        _inst[o] = origin.X;
        _inst[o + 1] = origin.Y;
        _inst[o + 2] = origin.Z;
        _inst[o + 3] = page;
        _inst[o + 4] = (q & 1) * Quarter;
        _inst[o + 5] = (q >> 1) * Quarter;
        _inst[o + 6] = level;
        _inst[o + 7] = (float)(_cell * (1 << level));
    }

    private void Ask(Source src, long key, int face, int level, int nx, int nz)
    {
        if (_pending.Count >= MaxPending || !_pending.Add(key))
            return;
        _requests.Enqueue(new Request(src, key, face, level, nx, nz));
    }

    private static bool SphereTouchesBox(Vector3 c, float r, Vector3 min, Vector3 max)
    {
        var d = Vector3.Max(Vector3.Max(min - c, c - max), Vector3.Zero);
        return d.LengthSquared() <= r * r;
    }

    private void TakeResults(Source src, int most)
    {
        for (var n = 0; n < most && _results.TryDequeue(out var r); n++)
        {
            if (r.Gen != src.Gen)
            {
                _free.Enqueue(r);
                n--;
                continue;
            }
            _pending.Remove(r.Key);
            var layer = FreeLayer();
            if (layer < 0)
            {
                _free.Enqueue(r);
                continue;
            }
            if (_pageKey[layer] >= 0)
                _pageOf.Remove(_pageKey[layer]);
            else
                _pagesHeld++;
            _pageKey[layer] = r.Key;
            _pageOf[r.Key] = layer;
            _pageOrigin[layer] = r.Origin;
            for (var q = 0; q < 5; q++)
            {
                _pageMin[layer * 5 + q] = r.Min[q];
                _pageMax[layer * 5 + q] = r.Max[q];
            }
            _pageUsed[layer] = _frame;
            _made++;
            if (!_headless && _glReady)
            {
                Native.glActiveTexture(GL_TEXTURE0 + PosUnit);
                Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _posTex);
                fixed (float* p = r.Pos)
                    Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, layer, PageVerts, PageVerts, 1, GL_RGBA, GL_FLOAT, p);
                Native.glActiveTexture(GL_TEXTURE0 + NrmUnit);
                Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _nrmTex);
                fixed (float* p = r.Nrm)
                    Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, layer, PageVerts, PageVerts, 1, GL_RGBA, GL_FLOAT, p);
                Native.glActiveTexture(GL_TEXTURE0);
            }
            _free.Enqueue(r);
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

    // ---- Drawing ------------------------------------------------------------------------------------------------------

    /// <summary>Picks and draws the ground for <paramref name="gfx"/>'s camera (fog and lights as the box renderer has them).</summary>
    public void Draw(Renderer gfx, ReadOnlySpan<Vector4> planes)
    {
        if (_src is not { } src)
            return;
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
        var c = src.Planet.Center;
        Native.glUniform3f(_locCenter, c.X, c.Y, c.Z);
        var to = new Vector3(MathF.Floor(cam.X / 64f) * 64f, MathF.Floor(cam.Y / 64f) * 64f, MathF.Floor(cam.Z / 64f) * 64f);
        Native.glUniform3f(_locTexOrigin, to.X, to.Y, to.Z);
        fixed (Vector2* m = _morph)
            Native.glUniform2fv(_locMorph, MaxLevels, (float*)m);
        var toLocal = LocalFrame(src.Planet, cam);
        Native.glUniformMatrix3fv(_locToLocal, 1, false, &toLocal.M11);
        Native.glUniform1f(_locDip, HorizonDip(src.Planet, cam));
        Native.glUniform1i(_locTex, 0);
        Native.glUniform1i(_locPos, PosUnit);
        Native.glUniform1i(_locNrm, NrmUnit);
        Native.glUniform1f(_locGround, (float)Material.Dirt);
        Native.glUniform1f(_locRock, (float)Material.Concrete);
        var fog = gfx.FogColor;
        Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_locFogRange, gfx.FogRange.X, gfx.FogRange.Y);
        Native.glActiveTexture(GL_TEXTURE0 + PosUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _posTex);
        Native.glActiveTexture(GL_TEXTURE0 + NrmUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _nrmTex);
        Native.glActiveTexture(GL_TEXTURE0);
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        fixed (float* p = _inst)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, (nint)_instances * FloatsPerInstance * sizeof(float), p);
        PerfCounters.Draw(_indexCount * _instances);
        Native.glDrawElementsInstanced(GL_TRIANGLES, _indexCount, GL_UNSIGNED_INT, (void*)0, _instances);
    }

    /// <summary>
    /// World -> the local frame at <paramref name="at"/> (y up away from the centre, -z towards the gravity's north), as
    /// the 3 x 3 matrix GLSL reads column by column: its columns are the local axes' world components' rows.
    /// </summary>
    public static Matrix3x3 LocalFrame(Planetoid pl, Vector3 at)
    {
        LocalAxes(pl, at, out var right, out var up, out var south);
        // GLSL mat3 * v: row r of the result is dot(row r, v); column-major upload: element (col, row).
        return new Matrix3x3
        {
            M11 = right.X, M12 = up.X, M13 = south.X,
            M21 = right.Y, M22 = up.Y, M23 = south.Y,
            M31 = right.Z, M32 = up.Z, M33 = south.Z,
        };
    }

    /// <summary>
    /// How far below level the planetoid's limb is from <paramref name="cam"/>, as the sine of the angle (0 at the ground,
    /// about 0.37 from 700 m up a 10 km planetoid): the sky's horizon is drawn there, not at level.
    /// </summary>
    public static float HorizonDip(Planetoid pl, Vector3 cam)
    {
        var d = (cam - pl.Center).Length();
        var r = pl.Radius + pl.Base;
        return d <= r ? 0f : (float)Math.Sqrt(Math.Max(0, 1 - r * r / ((double)d * d)));
    }

    /// <summary>The local axes at a point over the planetoid: right, up (away from the centre) and south (+z).</summary>
    public static void LocalAxes(Planetoid pl, Vector3 at, out Vector3 right, out Vector3 up, out Vector3 south)
    {
        var rel = at - pl.Center;
        up = rel.LengthSquared() > 1e-6f ? Vector3.Normalize(rel) : Vector3.UnitY;
        var axis = World.Map.PointGravity?.Axis ?? -Vector3.UnitZ;
        var north = axis - up * Vector3.Dot(axis, up);
        if (north.LengthSquared() < 1e-8f)
            north = Vector3.Cross(up, Vector3.UnitX);
        north = Vector3.Normalize(north);
        south = -north;
        right = Vector3.Cross(up, south); // x = y cross z
    }

    private void InitGl()
    {
        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
        _program = CreateProgram();
        Native.glAttachShader(_program, vs);
        Native.glAttachShader(_program, fs);
        Native.glLinkProgram(_program);
        GlDebug.Label(GL_PROGRAM, _program.Id, "PlanetRenderer");
        int linked;
        Native.glGetProgramiv(_program, GL_LINK_STATUS, &linked);
        vs.Dispose();
        fs.Dispose();
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(_program, 2048, &len, log);
            throw new InvalidOperationException("planet shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
        }
        _locViewProj = Uniform("uViewProj\0"u8);
        _locCam = Uniform("uCam\0"u8);
        _locCenter = Uniform("uCenter\0"u8);
        _locTexOrigin = Uniform("uTexOrigin\0"u8);
        _locMorph = Uniform("uMorph\0"u8);
        _locPos = Uniform("uPos\0"u8);
        _locNrm = Uniform("uNrm\0"u8);
        _locTex = Uniform("uTex\0"u8);
        _locGround = Uniform("uGroundLayer\0"u8);
        _locRock = Uniform("uRockLayer\0"u8);
        _locFog = Uniform("uFog\0"u8);
        _locFogRange = Uniform("uFogRange\0"u8);
        _locToLocal = Uniform("uToLocal\0"u8);
        _locDip = Uniform("uDip\0"u8);
        Lighting.Upload(_program);

        var idx = new List<uint>(Quarter * Quarter * 6 + 4 * Quarter * 12);
        const uint nv = QuarterVerts;
        for (uint z = 0; z < Quarter; z++)
        for (uint x = 0; x < Quarter; x++)
        {
            uint a = z * nv + x, b = a + 1, c = a + nv, d = c + 1;
            idx.AddRange([a, c, d, a, d, b]); // the collision's split and winding (Planetoid.Gather)
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
        _posTex = MakeArray(PosUnit);
        _nrmTex = MakeArray(NrmUnit);
        Native.glActiveTexture(GL_TEXTURE0);
        _glReady = true;
        // The pages made before GL was ready (Reset's own) are uploaded as the frames take them, so nothing is lost.
        Console.WriteLine($"[planet] drawing ready: {MaxPages} pages of {PageVerts} x {PageVerts} ({MaxPages * PageVerts * PageVerts * 32 / (1024 * 1024)} MB), "
                          + $"{_indexCount / 3} triangles an instance, GL error 0x{(uint)GlDebug.Error():X}");

        static uint Edge(uint side, uint k) => side switch
        {
            0 => k,
            1 => k * nv + Quarter,
            2 => Quarter * nv + k,
            _ => k * nv,
        };

        static uint MakeArray(int unit)
        {
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            Native.glActiveTexture(GL_TEXTURE0 + (uint)unit);
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, tex.Id);
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA32F, PageVerts, PageVerts, MaxPages, 0, GL_RGBA, GL_FLOAT, null);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            return tex.Id;
        }
    }

    private int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }
}

/// <summary>A 3 x 3 matrix laid out for <c>glUniformMatrix3fv</c> (nine floats, M11 first).</summary>
public struct Matrix3x3
{
    public float M11, M12, M13, M21, M22, M23, M31, M32, M33;
}
