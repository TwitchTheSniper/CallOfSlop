using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The city's life drawn (docs/planets/CITY.md "Life", <see cref="CityLife"/>): people as one instanced figure, walking
/// round the blocks and the square wholly on the GPU (each one's round and start are static; the vertex shader puts them
/// where the clock says), the people at the near stations posed on the CPU from the timetable, and the trains as cars of
/// one small mesh drawn with the city's own shader.
/// </summary>
public sealed unsafe partial class CityRenderer
{
    /// <summary>People are drawn within this many metres.</summary>
    public static float PeopleRange = 260f;

    private const string FigureVs = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in float aBone;
        layout(location = 3) in float aPart;
        layout(location = 4) in vec4 iA;
        layout(location = 5) in vec4 iB;
        uniform mat4 uViewProjRel;
        uniform vec3 uOrigin;
        uniform vec3 uPlanet;
        uniform vec3 uUp;
        uniform vec3 uEast;
        uniform vec3 uNorth;
        uniform float uR;
        uniform float uClock;
        uniform float uRange;
        uniform vec3 uCam;
        out vec3 vWorld;
        out vec3 vNormal;
        flat out vec3 vColor;
        flat out vec3 vGlow;

        uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x;
        }
        vec3 Pick(uint h, int part)
        {
            float f = float(h & 255u) / 255.0;
            if (part == 0) // trousers and skirts
                return f < 0.3 ? vec3(0.12, 0.13, 0.17) : f < 0.55 ? vec3(0.22, 0.26, 0.38) : f < 0.75 ? vec3(0.35, 0.3, 0.24) : f < 0.9 ? vec3(0.4, 0.4, 0.42) : vec3(0.5, 0.18, 0.15);
            if (part == 1) // coats and shirts
                return f < 0.2 ? vec3(0.62, 0.15, 0.13) : f < 0.35 ? vec3(0.2, 0.32, 0.5) : f < 0.5 ? vec3(0.8, 0.78, 0.72) : f < 0.62 ? vec3(0.2, 0.42, 0.3) : f < 0.75 ? vec3(0.55, 0.42, 0.25) : f < 0.88 ? vec3(0.15, 0.15, 0.16) : vec3(0.75, 0.6, 0.2);
            if (part == 2) // skin
                return f < 0.25 ? vec3(0.95, 0.78, 0.66) : f < 0.5 ? vec3(0.82, 0.62, 0.48) : f < 0.75 ? vec3(0.58, 0.4, 0.28) : vec3(0.36, 0.24, 0.17);
            if (part == 3) // hair
                return f < 0.35 ? vec3(0.08, 0.06, 0.05) : f < 0.6 ? vec3(0.3, 0.2, 0.12) : f < 0.8 ? vec3(0.65, 0.5, 0.3) : vec3(0.55, 0.55, 0.55);
            return vec3(0.08, 0.07, 0.07);
        }
        vec3 PlanDir(vec2 p)
        {
            float d = length(p);
            if (d < 1e-3)
                return uUp;
            float th = d / uR;
            return normalize(cos(th) * uUp + sin(th) * (uEast * p.x + uNorth * p.y) / d);
        }

        void main()
        {
            int kind = int(iB.w + 0.5);
            vec2 p;
            vec2 f;
            float h;
            float walked;
            float moving;
            if (kind == 0)
            {
                // Round a rectangle of the plan, as far as the clock has taken them.
                vec2 lo = iA.xy;
                vec2 hi = iA.zw;
                vec2 sz = hi - lo;
                float per = 2.0 * (sz.x + sz.y);
                walked = uClock * abs(iB.x) + iB.y;
                float u = mod(walked, per);
                if (iB.x < 0.0)
                    u = per - u;
                if (u < sz.x) { p = lo + vec2(u, 0.0); f = vec2(1.0, 0.0); }
                else if (u < sz.x + sz.y) { p = vec2(hi.x, lo.y + u - sz.x); f = vec2(0.0, 1.0); }
                else if (u < 2.0 * sz.x + sz.y) { p = vec2(hi.x - (u - sz.x - sz.y), hi.y); f = vec2(-1.0, 0.0); }
                else { p = vec2(lo.x, hi.y - (u - 2.0 * sz.x - sz.y)); f = vec2(0.0, -1.0); }
                if (iB.x < 0.0)
                    f = -f;
                h = 0.18;
                moving = 1.0;
            }
            else if (kind == 2)
            {
                // A car along its lane: as far as the clock has taken it, coming in again at the start.
                vec2 a = iA.xy;
                vec2 b = iA.zw;
                float len = max(length(b - a), 1.0);
                f = (b - a) / len;
                walked = uClock * iB.x + iB.y;
                p = a + f * mod(walked, len);
                h = 0.03;
                moving = 0.0;
            }
            else
            {
                p = iA.xy;
                h = iA.z;
                f = vec2(cos(iA.w), sin(iA.w));
                walked = iB.y;
                moving = iB.x;
            }
            uint seed = uint(iB.z + 0.5);
            vec3 up = PlanDir(p);
            vec3 ex = normalize(uEast - up * dot(uEast, up));
            vec3 nz = cross(ex, up);
            vec3 fw = normalize(ex * f.x + nz * f.y);
            vec3 right = cross(fw, up);
            // Legs and arms swing about the hips and the shoulders; one standing shifts their weight now and then.
            float ph = walked / 1.45 * 6.2831853;
            float swing = sin(ph) * 0.45 * moving;
            int bone = int(aBone + 0.5);
            vec3 lp = aPos;
            vec3 ln = aNormal;
            float pivot = bone >= 3 ? 1.42 : 0.9;
            float a = bone == 1 ? swing : bone == 2 ? -swing : bone == 3 ? -swing * 0.7 : bone == 4 ? swing * 0.7 : 0.0;
            if (bone > 0)
            {
                float c = cos(a), s = sin(a);
                vec2 yz = vec2(lp.y - pivot, lp.z);
                lp.yz = vec2(c * yz.x - s * yz.y + pivot, s * yz.x + c * yz.y);
                ln.yz = vec2(c * ln.y - s * ln.z, s * ln.y + c * ln.z);
            }
            float scale = 0.92 + 0.16 * float((Hash(seed * 3u) >> 8) & 255u) / 255.0;
            lp *= scale;
            lp.y += abs(sin(ph)) * 0.035 * moving; // the bob of a step
            vec3 base = uPlanet + up * (uR + h);
            if (kind != 1 && distance(base, uCam) > uRange * (kind == 2 ? 1.6 : 1.0))
            {
                gl_Position = vec4(0.0, 0.0, -2.0, 1.0); // (out of range: clipped away)
                vWorld = base;
                vNormal = up;
                vColor = vec3(0.0);
                vGlow = vec3(0.0);
                return;
            }
            vWorld = base + right * lp.x + up * lp.y + fw * lp.z;
            vNormal = right * ln.x + up * ln.y + fw * ln.z;
            int part = int(aPart + 0.5);
            vColor = Pick(Hash(seed * 7u + uint(part) * 1013u), part);
            // A car's lamps: white at the front, red behind.
            vGlow = part == 5 ? vec3(1.0, 0.95, 0.8) * 1.6 : part == 6 ? vec3(1.0, 0.12, 0.08) * 1.2 : vec3(0.0);
            gl_Position = uViewProjRel * vec4(vWorld - uOrigin, 1.0);
        }
        """;

    private static readonly string FigureFs = "#version 410 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        flat in vec3 vColor;
        flat in vec3 vGlow;
        uniform vec3 uPlanet;
        uniform float uR;
        out vec4 FragColor;

        void main()
        {
            vec3 n = normalize(vNormal);
            vec3 col;
            if (length(vWorld - uPlanet) < uR - 2.5) // (in the metro: its strip lights)
                col = vColor * vec3(0.9, 0.95, 1.0) * (0.6 + 0.3 * max(dot(n, normalize(vWorld - uPlanet)), 0.0));
            else
                col = ApplyLightAt(vColor, n, 0.3, vWorld) + vColor * vec3(1.0, 0.75, 0.5) * 0.3; // (the lamps)
            col += vGlow;
            FragColor = vec4(ApplyFog(col, vWorld), 1.0);
        }
        """;

    private GlProgram _fig;
    private GlVertexArray _figVao;
    private GlBuffer _figVbo, _figIbo, _walkVbo, _poseVbo, _carMeshVbo, _carMeshIbo, _carInstVbo;
    private GlVertexArray _carFigVao;
    private int _carMeshIndices;
    private int _figIndices, _poseCap;
    private int _fVp, _fOrigin, _fPlanet, _fUp, _fEast, _fNorth, _fR, _fClock, _fCam, _fFog, _fFogRange, _fRange;
    private CityLife? _life;
    private Vector3[] _walkerMids = [];
    private float[] _poses = new float[8 * 256];
    private int _poseCount;
    private bool _lifeReady;

    // The train: one car's mesh in the city's vertex format, drawn with the city's program turned by uRot.
    private GlVertexArray _carVao;
    private GlBuffer _carVbo, _carIbo;
    private int _carIndices, _locRot, _metroFirst, _metroIndices, _taxiFirst, _taxiIndices;

    /// <summary>People and trains drawn in the last frame.</summary>
    public int DrawnPeople, DrawnCars;

    private static int Loc(GlProgram p, ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* b = nameZ)
            return Native.glGetUniformLocation(p, b);
    }

    private bool InitLife()
    {
        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(FigureVs)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FigureFs)).Compile();
        _fig = CreateProgram();
        Native.glAttachShader(_fig, vs);
        Native.glAttachShader(_fig, fs);
        Native.glLinkProgram(_fig);
        int linked;
        Native.glGetProgramiv(_fig, GL_LINK_STATUS, &linked);
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(_fig, 2048, &len, log);
            throw new InvalidOperationException("figure shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
        }
        vs.Dispose();
        fs.Dispose();
        Lighting.Upload(_fig);
        _fig.Use();
        _fVp = Loc(_fig, "uViewProjRel\0"u8);
        _fOrigin = Loc(_fig, "uOrigin\0"u8);
        _fPlanet = Loc(_fig, "uPlanet\0"u8);
        _fUp = Loc(_fig, "uUp\0"u8);
        _fEast = Loc(_fig, "uEast\0"u8);
        _fNorth = Loc(_fig, "uNorth\0"u8);
        _fR = Loc(_fig, "uR\0"u8);
        _fClock = Loc(_fig, "uClock\0"u8);
        _fCam = Loc(_fig, "uCam\0"u8);
        _fRange = Loc(_fig, "uRange\0"u8);
        _fFog = Loc(_fig, "uFog\0"u8);
        _fFogRange = Loc(_fig, "uFogRange\0"u8);
        // The figure: (position, normal, bone, part) a vertex.
        var (verts, idx) = FigureMesh();
        _figVao = GenVertexArray();
        _figVao.Bind();
        _figVbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _figVbo);
        fixed (float* p = verts)
            Native.glBufferData(GL_ARRAY_BUFFER, (nint)verts.Length * 4, p, GL_STATIC_DRAW);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 32, (void*)0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 32, (void*)12);
        Native.glVertexAttribPointer(2, 1, GL_FLOAT, false, 32, (void*)24);
        Native.glVertexAttribPointer(3, 1, GL_FLOAT, false, 32, (void*)28);
        for (uint a = 0; a < 4; a++)
            EnableVertexAttribArray(a);
        _figIbo = GenBuffer();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _figIbo);
        fixed (ushort* p = idx)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, (nint)idx.Length * 2, p, GL_STATIC_DRAW);
        _figIndices = idx.Length;
        _walkVbo = GenBuffer();
        _poseVbo = GenBuffer();
        for (uint a = 4; a < 6; a++)
        {
            EnableVertexAttribArray(a);
            Native.glVertexAttribDivisor(a, 1);
        }
        // The street car: the figure's vertex format (bone 0: nothing swings), drawn by the same program as kind 2.
        var (sv, si) = StreetCarMesh();
        _carFigVao = GenVertexArray();
        _carFigVao.Bind();
        _carMeshVbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _carMeshVbo);
        fixed (float* p = sv)
            Native.glBufferData(GL_ARRAY_BUFFER, (nint)sv.Length * 4, p, GL_STATIC_DRAW);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 32, (void*)0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 32, (void*)12);
        Native.glVertexAttribPointer(2, 1, GL_FLOAT, false, 32, (void*)24);
        Native.glVertexAttribPointer(3, 1, GL_FLOAT, false, 32, (void*)28);
        for (uint a = 0; a < 4; a++)
            EnableVertexAttribArray(a);
        _carMeshIbo = GenBuffer();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _carMeshIbo);
        fixed (ushort* p = si)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, (nint)si.Length * 2, p, GL_STATIC_DRAW);
        _carMeshIndices = si.Length;
        _carInstVbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _carInstVbo);
        for (uint a = 4; a < 6; a++)
        {
            Native.glVertexAttribPointer(a, 4, GL_FLOAT, false, 32, (void*)(nint)((a - 4) * 16));
            EnableVertexAttribArray(a);
            Native.glVertexAttribDivisor(a, 1);
        }
        // The car.
        var (cv, ci) = CarMesh(0);
        var (mv, mi) = CarMesh(1);
        var (tv, ti) = TaxiMesh();
        mv = [.. mv, .. tv];
        _taxiFirst = mi.Length;
        mi = [.. mi, .. ti.Select(i => i + (uint)(mv.Length - tv.Length))];
        _taxiIndices = ti.Length;
        _metroFirst = ci.Length;
        _metroIndices = _taxiFirst;
        _taxiFirst += _metroFirst;
        var baseV = (uint)cv.Length;
        cv = [.. cv, .. mv];
        ci = [.. ci, .. mi.Select(i => i + baseV)];
        _carVao = GenVertexArray();
        _carVao.Bind();
        _carVbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _carVbo);
        fixed (CityVertex* p = cv)
            Native.glBufferData(GL_ARRAY_BUFFER, (nint)cv.Length * CityVertex.SizeInBytes, p, GL_STATIC_DRAW);
        const int stride = CityVertex.SizeInBytes;
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)12);
        Native.glVertexAttribPointer(2, 2, GL_FLOAT, false, stride, (void*)24);
        Native.glVertexAttribPointer(3, 4, GL_UNSIGNED_BYTE, true, stride, (void*)32);
        Native.glVertexAttribPointer(4, 4, GL_UNSIGNED_BYTE, false, stride, (void*)36);
        for (uint a = 0; a < 5; a++)
            EnableVertexAttribArray(a);
        _carIbo = GenBuffer();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _carIbo);
        fixed (uint* p = ci)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, (nint)ci.Length * 4, p, GL_STATIC_DRAW);
        _carIndices = _metroFirst;
        _lifeReady = true;
        return true;
    }

    private void UploadLife(CityLife life)
    {
        _figVao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _walkVbo);
        fixed (Vector4* p = life.Walkers)
            Native.glBufferData(GL_ARRAY_BUFFER, (nint)Math.Max(life.Walkers.Length, 2) * 16, life.Walkers.Length > 0 ? p : null, GL_STATIC_DRAW);
        _life = life;
        _walkerMids = life.WalkerChunks.Select(w => life.Plan.Ground(w.Mid)).ToArray();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _carInstVbo);
        fixed (Vector4* p = life.StreetCars)
            Native.glBufferData(GL_ARRAY_BUFFER, (nint)Math.Max(life.StreetCars.Length, 2) * 16, life.StreetCars.Length > 0 ? p : null, GL_STATIC_DRAW);
        Console.WriteLine($"[city] life: {life.Walkers.Length / 2} people walking, {life.StreetCars.Length / 2} cars, trains every {life.Period / CityLife.TrainsPerTrack:0} s each way round a {life.Length:0} m loop");
    }

    /// <summary>The people and the trains, after the city's own chunks (the city program still bound).</summary>
    private void DrawLife(Map map, Renderer gfx, CityGeometry geo, double clock)
    {
        if (map.City is not { } plan)
            return;
        if (!_lifeReady && !InitLife())
            return;
        if (_life == null || _life.Plan != plan)
            UploadLife(new CityLife(plan));
        var life = _life!;
        var cam = gfx.CameraPosition;
        var origin = gfx.Origin;
        DrawTrains(plan, life, clock, origin);
        DrawMetroTrains(plan, life, clock, origin);
        DrawTaxis(plan, life, clock, origin);
        // People: the walkers of the near chunks straight from their buffer, then the stations' people posed here.
        PoseStations(plan, life, clock, cam);
        PoseMetro(plan, life, clock, cam);
        _fig.Use();
        var vp = gfx.ViewProjRelative;
        Native.glUniformMatrix4fv(_fVp, 1, false, &vp.M11);
        Native.glUniform3f(_fOrigin, origin.X, origin.Y, origin.Z);
        Native.glUniform3f(_fPlanet, plan.PlanetCenter.X, plan.PlanetCenter.Y, plan.PlanetCenter.Z);
        Native.glUniform3f(_fUp, plan.Up.X, plan.Up.Y, plan.Up.Z);
        Native.glUniform3f(_fEast, plan.East.X, plan.East.Y, plan.East.Z);
        Native.glUniform3f(_fNorth, plan.North.X, plan.North.Y, plan.North.Z);
        Native.glUniform1f(_fR, (float)plan.GroundRadius);
        Native.glUniform1f(_fClock, (float)(clock % 100000));
        Native.glUniform3f(_fCam, cam.X, cam.Y, cam.Z);
        var fog = gfx.FogColor;
        var fr = gfx.FogRange;
        Native.glUniform3f(_fFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_fFogRange, fr.X, fr.Y);
        _figVao.Bind();
        DrawnPeople = 0;
        Native.glBindBuffer(GL_ARRAY_BUFFER, _walkVbo);
        // The near chunks' walkers, a run of neighbouring chunks a call; the vertex shader drops those past the range.
        Native.glUniform1f(_fRange, PeopleRange);
        var range2 = (PeopleRange + CityGeometry.ChunkSize) * (PeopleRange + CityGeometry.ChunkSize);
        int runFirst = 0, runCount = 0;
        for (var c = 0; c <= life.WalkerChunks.Length; c++)
        {
            var near = c < life.WalkerChunks.Length && Vector3.DistanceSquared(_walkerMids[c], cam) < range2;
            if (near && runCount > 0 && life.WalkerChunks[c].First == runFirst + runCount)
            {
                runCount += life.WalkerChunks[c].Count;
                continue;
            }
            if (runCount > 0)
            {
                for (uint a = 0; a < 2; a++)
                    Native.glVertexAttribPointer(4 + a, 4, GL_FLOAT, false, 32, (void*)((nint)runFirst * 32 + a * 16));
                Native.glDrawElementsInstanced(GL_TRIANGLES, _figIndices, GL_UNSIGNED_SHORT, null, runCount);
                DrawnPeople += runCount;
                runCount = 0;
            }
            if (near)
                (runFirst, runCount) = (life.WalkerChunks[c].First, life.WalkerChunks[c].Count);
        }
        // The traffic: every car in one call (the vertex shader drops those far off).
        if (life.StreetCars.Length > 0)
        {
            _carFigVao.Bind();
            Native.glDrawElementsInstanced(GL_TRIANGLES, _carMeshIndices, GL_UNSIGNED_SHORT, null, life.StreetCars.Length / 2);
            DrawnPeople += 0;
            _figVao.Bind();
        }
        if (_poseCount > 0)
        {
            Native.glBindBuffer(GL_ARRAY_BUFFER, _poseVbo);
            if (_poseCount > _poseCap)
            {
                _poseCap = _poses.Length / 8;
                Native.glBufferData(GL_ARRAY_BUFFER, (nint)_poseCap * 32, null, GL_STREAM_DRAW);
            }
            Native.glBufferData(GL_ARRAY_BUFFER, (nint)_poseCap * 32, null, GL_STREAM_DRAW); // (orphaned)
            fixed (float* p = _poses)
                Native.glBufferSubData(GL_ARRAY_BUFFER, 0, (nint)_poseCount * 32, p);
            for (uint a = 0; a < 2; a++)
                Native.glVertexAttribPointer(4 + a, 4, GL_FLOAT, false, 32, (void*)(nint)(a * 16));
            Native.glDrawElementsInstanced(GL_TRIANGLES, _figIndices, GL_UNSIGNED_SHORT, null, _poseCount);
            DrawnPeople += _poseCount;
        }
    }

    // ---- Trains -------------------------------------------------------------------------------------------------------

    private void DrawTrains(CityPlan plan, CityLife life, double clock, Vector3 origin)
    {
        DrawnCars = 0;
        if (life.Rail is not { } rail || life.Period <= 0)
            return;
        // (the city program is bound: its uniforms are this frame's)
        _carVao.Bind();
        for (var dir = -1; dir <= 1; dir += 2)
            for (var k = 0; k < CityLife.TrainsPerTrack; k++)
            {
                var (s, _, _) = life.Train(clock, dir, k);
                for (var c = 0; c < CityLife.Cars; c++)
                {
                    var mid = s + dir * ((CityLife.Cars - 1) * 0.5f - c) * CityLife.CarLength;
                    Vector3 Bogie(float at)
                    {
                        var (p, d) = life.At(at);
                        var left = new Vector2(-d.Y, d.X);
                        return plan.Ground(p + left * CityLife.Track * dir, rail.Height + 0.03f);
                    }
                    var front = Bogie(mid + dir * 5.5f);
                    var back = Bogie(mid - dir * 5.5f);
                    var centre = (front + back) * 0.5f;
                    var fwd = Vector3.Normalize(front - back);
                    var up0 = Vector3.Normalize(centre - plan.PlanetCenter);
                    var right = Vector3.Normalize(Vector3.Cross(fwd, up0));
                    var up = Vector3.Cross(right, fwd);
                    // (columns: the car's x along, y up, z to its right)
                    var rot = new Matrix4x4(fwd.X, fwd.Y, fwd.Z, 0, up.X, up.Y, up.Z, 0, right.X, right.Y, right.Z, 0, 0, 0, 0, 1);
                    float* m = stackalloc float[9] { fwd.X, fwd.Y, fwd.Z, up.X, up.Y, up.Z, right.X, right.Y, right.Z };
                    Native.glUniformMatrix3fv(_locRot, 1, false, m);
                    var rel = centre - origin;
                    Native.glUniform3f(_locChunkRel, rel.X, rel.Y, rel.Z);
                    Native.glUniform3f(_locChunkWorld, centre.X, centre.Y, centre.Z);
                    Native.glDrawElements(GL_TRIANGLES, _carIndices, GL_UNSIGNED_INT, null);
                    DrawnCars++;
                    _ = rot;
                }
            }
        float* id = stackalloc float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        Native.glUniformMatrix3fv(_locRot, 1, false, id);
    }

    // ---- The metro's trains and people --------------------------------------------------------------------------------

    private void Car(Vector3 front, Vector3 back, CityPlan plan, Vector3 origin, int first, int count)
    {
        var centre = (front + back) * 0.5f;
        var fwd = Vector3.Normalize(front - back);
        var up0 = Vector3.Normalize(centre - plan.PlanetCenter);
        var right = Vector3.Normalize(Vector3.Cross(fwd, up0));
        var up = Vector3.Cross(right, fwd);
        float* m = stackalloc float[9] { fwd.X, fwd.Y, fwd.Z, up.X, up.Y, up.Z, right.X, right.Y, right.Z };
        Native.glUniformMatrix3fv(_locRot, 1, false, m);
        var rel = centre - origin;
        Native.glUniform3f(_locChunkRel, rel.X, rel.Y, rel.Z);
        Native.glUniform3f(_locChunkWorld, centre.X, centre.Y, centre.Z);
        Native.glDrawElements(GL_TRIANGLES, count, GL_UNSIGNED_INT, (void*)((nint)first * 4));
        DrawnCars++;
    }

    private void DrawMetroTrains(CityPlan plan, CityLife life, double clock, Vector3 origin)
    {
        if (plan.Metro is not { } m || life.MetroHalf <= 0)
            return;
        _carVao.Bind();
        var y = -m.Depth - CityMetro.Bed + 0.03f;
        for (var k = 0; k < 2; k++)
        {
            var (z, dir, _) = life.MetroTrain(clock, k);
            var x = m.X + CityMetro.Track * dir;
            for (var c = 0; c < CityLife.Cars; c++)
            {
                var mid = z + dir * ((CityLife.Cars - 1) * 0.5f - c) * CityLife.CarLength;
                Car(plan.Ground(new Vector2(x, mid + dir * 5.5f), y), plan.Ground(new Vector2(x, mid - dir * 5.5f), y), plan, origin, _metroFirst, _metroIndices);
            }
        }
        float* id = stackalloc float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        Native.glUniformMatrix3fv(_locRot, 1, false, id);
    }

    private void DrawTaxis(CityPlan plan, CityLife life, double clock, Vector3 origin)
    {
        if (life.Taxis == 0)
            return;
        _carVao.Bind();
        for (var k = 0; k < life.Taxis; k++)
        {
            var (p, h, d) = life.Taxi(clock, k);
            Car(plan.Ground(p + d * 1.5f, h), plan.Ground(p - d * 1.5f, h), plan, origin, _taxiFirst, _taxiIndices);
        }
        float* id = stackalloc float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        Native.glUniformMatrix3fv(_locRot, 1, false, id);
    }

    /// <summary>An air taxi (x along, y up, z to its right; its skids at 0): a yellow body with a band of lit windows, a dark
    /// canopy, skids, a beacon on top and the red and green lamps on its sides.</summary>
    private static (CityVertex[] Verts, uint[] Idx) TaxiMesh()
    {
        var v = new List<CityVertex>();
        var ix = new List<uint>();
        void Box(Vector3 lo, Vector3 hi, uint col, uint par)
        {
            Vector3 c = (lo + hi) * 0.5f, h = (hi - lo) * 0.5f;
            Span<Vector3> n = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
            foreach (var nn in n)
            {
                var u = MathF.Abs(nn.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
                var w = Vector3.Cross(nn, u);
                var first = (uint)v.Count;
                foreach (var (su, sw) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var p = c + (nn + u * su + w * sw) * h;
                    // (a side face's own metres: along x and up y, so the window band lands where a train's does)
                    v.Add(new CityVertex { Position = p, Normal = nn, Uv = new Vector2(p.X + 5.0f, p.Y + 1.0f), Color = col, Params = par });
                }
                ix.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            }
        }
        uint yellow = CityPlanColours.Rgb(0xe8b020), dark = CityPlanColours.Rgb(0x202326);
        var metal = CityGeometry.Pack(CityStyle.Metal, 0, 0, 0);
        Box(new Vector3(-2.6f, 0.5f, -1.1f), new Vector3(2.6f, 1.75f, 1.1f), yellow, CityGeometry.Pack(CityStyle.TrainSide, 2, 0, 0));
        Box(new Vector3(-1.4f, 1.75f, -0.9f), new Vector3(1.8f, 2.25f, 0.9f), dark, CityGeometry.Pack(CityStyle.TrainEnd, 0, 0, 0));
        foreach (var sz in new[] { -0.8f, 0.8f })
        {
            Box(new Vector3(-2.2f, 0f, sz - 0.06f), new Vector3(2.2f, 0.1f, sz + 0.06f), dark, metal);
            foreach (var sx in new[] { -1.4f, 1.4f })
                Box(new Vector3(sx - 0.05f, 0.1f, sz - 0.05f), new Vector3(sx + 0.05f, 0.5f, sz + 0.05f), dark, metal);
        }
        Box(new Vector3(-0.15f, 2.25f, -0.15f), new Vector3(0.15f, 2.45f, 0.15f), CityPlanColours.Rgb(0xff3020), CityGeometry.Pack(CityStyle.Beacon, 0, 0, 0));
        Box(new Vector3(-0.2f, 1.0f, -1.2f), new Vector3(0.2f, 1.2f, -1.1f), CityPlanColours.Rgb(0xff2020), CityGeometry.Pack(CityStyle.Lamp, 0, 0, 0));
        Box(new Vector3(-0.2f, 1.0f, 1.1f), new Vector3(0.2f, 1.2f, 1.2f), CityPlanColours.Rgb(0x20ff40), CityGeometry.Pack(CityStyle.Lamp, 0, 0, 0));
        Box(new Vector3(2.6f, 0.8f, -0.6f), new Vector3(2.7f, 1.0f, 0.6f), CityPlanColours.Rgb(0xfff2d0), CityGeometry.Pack(CityStyle.Lamp, 0, 0, 0));
        return (v.ToArray(), ix.ToArray());
    }

    private void PoseMetro(CityPlan plan, CityLife life, double clock, Vector3 cam)
    {
        if (plan.Metro is not { } m || life.MetroHalf <= 0)
            return;
        var top = -m.Depth;
        var foot = CityGeometry.RoadLift + CityGeometry.Kerb;
        var run = m.StairRun;
        Span<(Vector2, float)> pts = stackalloc (Vector2, float)[5];
        for (var st = 0; st < m.Stations.Length; st++)
        {
            var z0 = m.Stations[st];
            if (Vector3.DistanceSquared(plan.Ground(new Vector2(m.X, z0), top), cam) > PeopleRange * PeopleRange)
                continue;
            for (var dir = -1; dir <= 1; dir += 2)
            {
                var side = (float)dir; // (trains going +z use the track and the platform on the +x side)
                var (lastLeave, nextArrive, standing, arrived) = life.MetroTimes(clock, dir, st);
                var interval = standing ? (lastLeave, arrived) : (lastLeave, nextArrive);
                var salt = (uint)(st * 2 + (dir + 1) / 2) * 104729u + 77u;
                for (var i = 0; i < Waiting; i++)
                {
                    var seed = Hash(salt + (uint)i + (uint)(long)Math.Floor(interval.Item1));
                    var spotZ = z0 + (((seed >> 12) & 1023) / 1023f - 0.5f) * 2 * (CityMetro.HallZ - 6);
                    var spotX = m.X + side * (5.0f + (seed & 1023) / 1023f * 3.4f);
                    var spot = (new Vector2(spotX, spotZ), top);
                    var span = interval.Item2 - interval.Item1;
                    var reach = interval.Item1 + span * (i + 0.5) / Waiting * 0.85;
                    var setOff = reach - 30;
                    if (clock < setOff)
                        continue;
                    if (standing && clock >= arrived + 1.5 + i * 0.4)
                    {
                        var doorZ = NearestDoor(spotZ, z0, dir);
                        pts[0] = spot;
                        pts[1] = (new Vector2(m.X + side * 4.2f, doorZ), top);
                        if (Walk(pts[..2], (float)(clock - arrived - 1.5 - i * 0.4), 1.4f, out var a, out var h, out var d, out var w))
                            Pose(a, h, d, w, true, seed);
                        continue;
                    }
                    if (clock < reach)
                    {
                        var (fs, ft, ff) = Flight(m, z0, side, spotZ, run);
                        pts[0] = (fs, foot);
                        pts[1] = (ft, foot);
                        pts[2] = (ff, top);
                        pts[3] = (ff + new Vector2(-side * 1.6f, 0), top);
                        pts[4] = spot;
                        var total = 5 + run + 1.6f + Vector2.Distance(pts[3].Item1, spot.Item1);
                        if (Walk(pts, (float)(clock - setOff) * total / 30f, 1f, out var a, out var h, out var d, out var w))
                            Pose(a, h, d, w, true, seed);
                        continue;
                    }
                    Pose(spot.Item1, top, new Vector2(-side, 0), (float)clock * 0.02f, false, seed);
                }
                var lastArrive = standing ? arrived : lastLeave - CityLife.Dwell;
                for (var j = 0; j < Alighting; j++)
                {
                    var seed = Hash(salt + 5000u + (uint)j + (uint)(long)Math.Floor(lastArrive));
                    var t = (float)(clock - lastArrive - 2.0 - j * 0.7);
                    if (t < 0)
                        continue;
                    var car = (int)(Hash(seed) % CityLife.Cars);
                    var doorZ = z0 + dir * ((CityLife.Cars - 1) * 0.5f - car) * CityLife.CarLength + ((Hash(seed >> 3) & 1) == 0 ? -4f : 4f);
                    var (fs, ft, ff) = Flight(m, z0, side, doorZ, run);
                    pts[0] = (new Vector2(m.X + side * 3.9f, doorZ), top);
                    pts[1] = (new Vector2(m.X + side * (6.2f + (j % 3) * 0.5f), doorZ), top);
                    pts[2] = (ff + new Vector2(-side * 1.6f, 0), top);
                    pts[3] = (ff, top);
                    pts[4] = (ft, foot);
                    if (Walk(pts, t, 1.3f, out var a, out var h, out var d, out var w))
                        Pose(a, h, d, w, true, seed);
                    _ = fs;
                }
            }
        }
    }

    /// <summary>The flight nearer a point of the platform on one side: a point on the street behind its top, its top, its foot.</summary>
    private static (Vector2 Street, Vector2 Top, Vector2 Foot) Flight(CityMetro m, float z0, float side, float z, float run)
    {
        var sz = z >= z0 ? 1f : -1f;
        var t = new Vector2(m.X + side * CityMetro.StairX, z0 + sz * CityMetro.StairTop);
        return (t + new Vector2(0, sz * 5), t, t + new Vector2(0, -sz * run));
    }

    /// <summary>The metro door (4 m either side of each car's middle) nearest <paramref name="z"/>, for a train standing at z0.</summary>
    private static float NearestDoor(float z, float z0, int dir)
    {
        var best = z0;
        for (var c = 0; c < CityLife.Cars * 2; c++)
        {
            var at = z0 + dir * ((CityLife.Cars - 1) * 0.5f - c / 2) * CityLife.CarLength + (c % 2 == 0 ? -4f : 4f);
            if (c == 0 || MathF.Abs(at - z) < MathF.Abs(best - z))
                best = at;
        }
        return best;
    }

    // ---- People at the stations ---------------------------------------------------------------------------------------

    private const int Waiting = 10, Alighting = 8;

    private void Pose(Vector2 p, float h, Vector2 dir, float walked, bool moving, uint seed)
    {
        if (_poseCount * 8 + 8 > _poses.Length)
            Array.Resize(ref _poses, _poses.Length * 2);
        var o = _poseCount++ * 8;
        _poses[o] = p.X;
        _poses[o + 1] = p.Y;
        _poses[o + 2] = h;
        _poses[o + 3] = MathF.Atan2(dir.Y, dir.X);
        _poses[o + 4] = moving ? 1 : 0;
        _poses[o + 5] = walked;
        _poses[o + 6] = seed & 0xffff;
        _poses[o + 7] = 1;
    }

    /// <summary>A walk along waypoints (plan point, height) at a walking pace: where one is <paramref name="t"/> s after setting out,
    /// or false once arrived (the last point is where they go out of sight).</summary>
    private static bool Walk(ReadOnlySpan<(Vector2 P, float H)> pts, float t, float pace, out Vector2 at, out float h, out Vector2 dir, out float walked)
    {
        walked = t * pace;
        var left = walked;
        for (var i = 0; i + 1 < pts.Length; i++)
        {
            var seg = Vector2.Distance(pts[i].P, pts[i + 1].P);
            if (left <= seg)
            {
                var f = seg > 1e-3f ? left / seg : 1;
                at = Vector2.Lerp(pts[i].P, pts[i + 1].P, f);
                h = pts[i].H + (pts[i + 1].H - pts[i].H) * f;
                dir = seg > 1e-3f ? (pts[i + 1].P - pts[i].P) / seg : Vector2.UnitX;
                return true;
            }
            left -= seg;
        }
        at = pts[^1].P;
        h = pts[^1].H;
        dir = Vector2.UnitX;
        return false;
    }

    private void PoseStations(CityPlan plan, CityLife life, double clock, Vector3 cam)
    {
        _poseCount = 0;
        if (life.Rail is not { } rail || life.Period <= 0)
            return;
        var top = rail.Height + 1.0f;
        var foot = CityGeometry.RoadLift + CityGeometry.Kerb;
        var run = (top - foot) / MathF.Tan(33f * MathF.PI / 180);
        var platHalf = CityGeometry.StationHalf * 4f;
        Span<(Vector2, float)> pts = stackalloc (Vector2, float)[5];
        for (var st = 0; st < life.StationAt.Length; st++)
        {
            var s0 = life.StationAt[st];
            var (centre, _) = life.At(s0);
            if (Vector3.DistanceSquared(plan.Ground(centre), cam) > PeopleRange * PeopleRange)
                continue;
            for (var dir = -1; dir <= 1; dir += 2)
            {
                // Platform points: along the line from the station's middle, across from the line (on this platform's side).
                Vector2 P(float along, float across)
                {
                    var (p, d) = life.At(s0 + along);
                    return p + new Vector2(-d.Y, d.X) * across * dir;
                }
                var (lastLeave, nextArrive, standing, arrived) = life.StationTimes(clock, dir, st);
                var interval = standing ? (lastLeave, arrived) : (lastLeave, nextArrive);
                var stairTop = (P(platHalf, 9.6f), top);
                var stairFoot = (P(platHalf + run, 9.6f), foot);
                var street = (P(platHalf + run + 12, 9.6f), foot);
                var salt = (uint)(st * 2 + (dir + 1) / 2) * 7919u;
                // Waiting: each comes up the stairs some time after the last train left and stands at their spot; when a
                // train stands here they walk to its nearest door and are gone.
                for (var i = 0; i < Waiting; i++)
                {
                    var seed = Hash(salt + (uint)i + (uint)(long)Math.Floor(interval.Item1));
                    var spot = (P(Range(seed, -platHalf + 4, platHalf - 4), Range(seed >> 8, 5.2f, 9.5f)), top);
                    var span = interval.Item2 - interval.Item1;
                    if (double.IsInfinity(span))
                        continue;
                    var reach = interval.Item1 + span * (i + 0.5) / Waiting * 0.85;
                    var setOff = reach - 28;
                    if (clock < setOff)
                        continue;
                    // The door (4 m either side of each car's middle) nearest the spot along the platform.
                    var spotAlong = Range(seed, -platHalf + 4, platHalf - 4);
                    var doorAt = 0f;
                    for (var c = 0; c < CityLife.Cars * 2; c++)
                    {
                        var at = -((CityLife.Cars - 1) * 0.5f - c / 2) * CityLife.CarLength + (c % 2 == 0 ? -4f : 4f);
                        if (MathF.Abs(at - spotAlong) < MathF.Abs(doorAt - spotAlong) || c == 0)
                            doorAt = at;
                    }
                    if (standing && clock >= arrived + 1.5 + i * 0.4)
                    {
                        pts[0] = spot;
                        pts[1] = (P(doorAt, 4.4f), top);
                        if (Walk(pts[..2], (float)(clock - arrived - 1.5 - i * 0.4), 1.4f, out var a, out var h, out var d, out var w))
                            Pose(a, h, d, w, true, seed);
                        continue;
                    }
                    if (clock < reach)
                    {
                        pts[0] = street;
                        pts[1] = stairFoot;
                        pts[2] = stairTop;
                        pts[3] = spot;
                        var total = Vector2.Distance(street.Item1, stairFoot.Item1) + Vector2.Distance(stairFoot.Item1, stairTop.Item1) + Vector2.Distance(stairTop.Item1, spot.Item1);
                        var t = (float)(clock - setOff) * total / 28f;
                        if (Walk(pts[..4], t, 1f, out var a, out var h, out var d, out var w))
                            Pose(a, h, d, w, true, seed);
                        continue;
                    }
                    // Standing, facing the track, now and then shifting.
                    Pose(spot.Item1, top, P(0, -1) - P(0, 0), (float)clock * 0.02f, false, seed);
                }
                // Getting off: out of the doors, along to the stairs, down and away along the street.
                var lastArrive = standing ? arrived : lastLeave - CityLife.Dwell;
                if (double.IsInfinity(lastArrive))
                    continue;
                for (var j = 0; j < Alighting; j++)
                {
                    var seed = Hash(salt + 1000u + (uint)j + (uint)(long)Math.Floor(lastArrive));
                    var t = (float)(clock - lastArrive - 2.0 - j * 0.7);
                    if (t < 0)
                        continue;
                    var car = (int)(Hash(seed) % CityLife.Cars);
                    var carMid = -((CityLife.Cars - 1) * 0.5f - car) * CityLife.CarLength;
                    var door = carMid + ((Hash(seed >> 3) & 1) == 0 ? -4f : 4f);
                    pts[0] = (P(door, 3.9f), top);
                    pts[1] = (P(door, 6.5f + (j % 3) * 0.8f), top);
                    pts[2] = stairTop;
                    pts[3] = stairFoot;
                    pts[4] = street;
                    if (Walk(pts, t, 1.3f, out var a, out var h, out var d, out var w))
                        Pose(a, h, d, w, true, seed);
                }
            }
        }

        static float Range(uint h, float a, float b) => a + (b - a) * (h & 1023) / 1023f;
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    // ---- Meshes -------------------------------------------------------------------------------------------------------

    /// <summary>A person of boxes (x to the right, y up, z forward; feet at 0): legs (bones 1, 2) from the hips at 0.9 m,
    /// arms (3, 4) from the shoulders at 1.42 m. Parts: 0 trousers, 1 coat, 2 skin, 3 hair, 4 shoes.</summary>
    private static (float[] Verts, ushort[] Idx) FigureMesh()
    {
        var v = new List<float>();
        var ix = new List<ushort>();
        void Box(Vector3 lo, Vector3 hi, int bone, int part)
        {
            Span<Vector3> n = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
            foreach (var nn in n)
            {
                var u = MathF.Abs(nn.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
                var w = Vector3.Cross(nn, u);
                var c = (lo + hi) * 0.5f;
                var half = (hi - lo) * 0.5f;
                var first = (ushort)(v.Count / 8);
                foreach (var (su, sw) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var p = c + (nn + u * su + w * sw) * half;
                    v.AddRange([p.X, p.Y, p.Z, nn.X, nn.Y, nn.Z, bone, part]);
                }
                // (u x w = n: the corners above go round anticlockwise seen from outside)
                ix.AddRange([first, (ushort)(first + 1), (ushort)(first + 2), first, (ushort)(first + 2), (ushort)(first + 3)]);
            }
        }
        foreach (var (sx, bone) in new[] { (-1f, 1), (1f, 2) })
        {
            Box(new Vector3(sx * 0.1f - 0.07f, 0.08f, -0.075f), new Vector3(sx * 0.1f + 0.07f, 0.92f, 0.075f), bone, 0);
            Box(new Vector3(sx * 0.1f - 0.065f, 0f, -0.08f), new Vector3(sx * 0.1f + 0.065f, 0.08f, 0.16f), bone, 4);
            Box(new Vector3(sx * 0.25f - 0.055f, 0.8f, -0.06f), new Vector3(sx * 0.25f + 0.055f, 1.42f, 0.06f), bone + 2, 1);
            Box(new Vector3(sx * 0.25f - 0.045f, 0.7f, -0.045f), new Vector3(sx * 0.25f + 0.045f, 0.8f, 0.045f), bone + 2, 2);
        }
        Box(new Vector3(-0.19f, 0.86f, -0.11f), new Vector3(0.19f, 1.0f, 0.11f), 0, 0);
        Box(new Vector3(-0.2f, 1.0f, -0.12f), new Vector3(0.2f, 1.46f, 0.12f), 0, 1);
        Box(new Vector3(-0.05f, 1.46f, -0.05f), new Vector3(0.05f, 1.52f, 0.05f), 0, 2);
        Box(new Vector3(-0.1f, 1.52f, -0.1f), new Vector3(0.1f, 1.74f, 0.11f), 0, 2);
        Box(new Vector3(-0.105f, 1.66f, -0.115f), new Vector3(0.105f, 1.78f, 0.06f), 0, 3);
        return (v.ToArray(), ix.ToArray());
    }

    /// <summary>A street car (x to its right, y up, z forward; its wheels on 0), in the figure's format: body (part 1, its
    /// paint), glass and tyres (4), headlamps (5) and tail lamps (6).</summary>
    private static (float[] Verts, ushort[] Idx) StreetCarMesh()
    {
        var v = new List<float>();
        var ix = new List<ushort>();
        void Box(Vector3 lo, Vector3 hi, int part)
        {
            Span<Vector3> n = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
            foreach (var nn in n)
            {
                var u = MathF.Abs(nn.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
                var w = Vector3.Cross(nn, u);
                var c = (lo + hi) * 0.5f;
                var half = (hi - lo) * 0.5f;
                var first = (ushort)(v.Count / 8);
                foreach (var (su, sw) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var p = c + (nn + u * su + w * sw) * half;
                    v.AddRange([p.X, p.Y, p.Z, nn.X, nn.Y, nn.Z, 0, part]);
                }
                ix.AddRange([first, (ushort)(first + 1), (ushort)(first + 2), first, (ushort)(first + 2), (ushort)(first + 3)]);
            }
        }
        Box(new Vector3(-0.88f, 0.32f, -2.15f), new Vector3(0.88f, 0.95f, 2.15f), 1);
        Box(new Vector3(-0.8f, 0.95f, -1.05f), new Vector3(0.8f, 1.4f, 1.0f), 4);
        Box(new Vector3(-0.78f, 1.4f, -0.95f), new Vector3(0.78f, 1.46f, 0.9f), 1);
        foreach (var sx in new[] { -0.82f, 0.82f })
            foreach (var sz in new[] { -1.35f, 1.35f })
                Box(new Vector3(sx - 0.12f, 0f, sz - 0.32f), new Vector3(sx + 0.12f, 0.62f, sz + 0.32f), 4);
        foreach (var sx in new[] { -0.6f, 0.6f })
        {
            Box(new Vector3(sx - 0.18f, 0.62f, 2.14f), new Vector3(sx + 0.18f, 0.78f, 2.2f), 5);
            Box(new Vector3(sx - 0.18f, 0.66f, -2.2f), new Vector3(sx + 0.18f, 0.8f, -2.14f), 6);
        }
        return (v.ToArray(), ix.ToArray());
    }

    /// <summary>A railway car (x along, y up from the rail, z to its right): body with its windows and doors (drawn in the
    /// shader), roof, underframe and bogies, a lamp at each end.</summary>
    private static (CityVertex[] Verts, uint[] Idx) CarMesh(uint livery)
    {
        var v = new List<CityVertex>();
        var ix = new List<uint>();
        const float L = CityLife.CarLength * 0.5f - 0.3f;
        uint cream = CityPlanColours.Rgb(0xd8cfb8), roof = CityPlanColours.Rgb(0x5b5f63), dark = CityPlanColours.Rgb(0x26282b), lamp = CityPlanColours.Rgb(0xfff2d0);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, uint col, uint par)
        {
            var first = (uint)v.Count;
            v.Add(new CityVertex { Position = a, Normal = n, Uv = ua, Color = col, Params = par });
            v.Add(new CityVertex { Position = b, Normal = n, Uv = ub, Color = col, Params = par });
            v.Add(new CityVertex { Position = c, Normal = n, Uv = uc, Color = col, Params = par });
            v.Add(new CityVertex { Position = d, Normal = n, Uv = ud, Color = col, Params = par });
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0)
                ix.AddRange([first, first + 2, first + 1, first, first + 3, first + 2]);
            else
                ix.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }
        void Box(Vector3 lo, Vector3 hi, uint col, uint par)
        {
            Vector3 c = (lo + hi) * 0.5f, h = (hi - lo) * 0.5f;
            Span<Vector3> n = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
            foreach (var nn in n)
            {
                var u = MathF.Abs(nn.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
                var w = Vector3.Cross(nn, u);
                Quad(c + (nn - u - w) * h, c + (nn + u - w) * h, c + (nn + u + w) * h, c + (nn - u + w) * h, nn, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, col, par);
            }
        }
        // The body's section (z, y), swept along x: sides, the roof's shoulders, the roof.
        ReadOnlySpan<Vector2> sec = [new(1.45f, 0.5f), new(1.45f, 3.1f), new(1.15f, 3.55f), new(-1.15f, 3.55f), new(-1.45f, 3.1f), new(-1.45f, 0.5f)];
        var side = CityGeometry.Pack(CityStyle.TrainSide, livery, 0, 0);
        var plain = CityGeometry.Pack(CityStyle.Plain, 13, 0, 0);
        for (var j = 0; j + 1 < sec.Length; j++)
        {
            Vector2 a = sec[j], b = sec[j + 1];
            var e = b - a;
            var n2 = Vector2.Normalize(new Vector2(e.Y, -e.X));
            if (Vector2.Dot(n2, (a + b) * 0.5f - new Vector2(0, 2)) < 0)
                n2 = -n2;
            var n = new Vector3(0, n2.Y, n2.X);
            var isSide = MathF.Abs(e.X) < 0.01f;
            var col = isSide ? cream : roof;
            var par = isSide ? side : plain;
            Quad(new Vector3(-L, a.Y, a.X), new Vector3(L, a.Y, a.X), new Vector3(L, b.Y, b.X), new Vector3(-L, b.Y, b.X), n,
                new Vector2(-L, a.Y), new Vector2(L, a.Y), new Vector2(L, b.Y), new Vector2(-L, b.Y), col, par);
        }
        // The ends: a fan of the section, with a dark windscreen drawn by the end style.
        var endPar = CityGeometry.Pack(CityStyle.TrainEnd, 0, 0, 0);
        foreach (var sx in new[] { -1f, 1f })
        {
            var n = new Vector3(sx, 0, 0);
            var mid = new Vector3(sx * L, 2.0f, 0);
            for (var j = 0; j < sec.Length; j++)
            {
                Vector2 a = sec[j], b = sec[(j + 1) % sec.Length];
                var first = (uint)v.Count;
                v.Add(new CityVertex { Position = mid, Normal = n, Uv = new Vector2(0, 2.0f), Color = cream, Params = endPar });
                v.Add(new CityVertex { Position = new Vector3(sx * L, a.Y, a.X), Normal = n, Uv = new Vector2(a.X, a.Y), Color = cream, Params = endPar });
                v.Add(new CityVertex { Position = new Vector3(sx * L, b.Y, b.X), Normal = n, Uv = new Vector2(b.X, b.Y), Color = cream, Params = endPar });
                var p0 = v[(int)first].Position;
                if (Vector3.Dot(Vector3.Cross(v[(int)first + 1].Position - p0, v[(int)first + 2].Position - p0), n) < 0)
                    ix.AddRange([first, first + 2, first + 1]);
                else
                    ix.AddRange([first, first + 1, first + 2]);
            }
            // Lamps low on each end.
            foreach (var sz in new[] { -0.9f, 0.9f })
                Box(new Vector3(sx * L - 0.05f + sx * 0.06f, 0.8f, sz - 0.15f), new Vector3(sx * L + 0.05f + sx * 0.06f, 1.0f, sz + 0.15f), lamp, CityGeometry.Pack(CityStyle.Lamp, 0, 0, 0));
        }
        // Underframe and bogies.
        var metal = CityGeometry.Pack(CityStyle.Metal, 0, 0, 0);
        Box(new Vector3(-L + 0.4f, 0.32f, -1.25f), new Vector3(L - 0.4f, 0.5f, 1.25f), dark, metal);
        foreach (var bx in new[] { -5.5f, 5.5f })
            Box(new Vector3(bx - 1.3f, 0.06f, -1.1f), new Vector3(bx + 1.3f, 0.6f, 1.1f), dark, metal);
        return (v.ToArray(), ix.ToArray());
    }
}
