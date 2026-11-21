using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Sun shadows from ships and vehicles (Settings > Video > Sun shadows; Karl, 2026-11-21, phase 5 of the ship looks from the Ship Render
/// Bench). Before the world is drawn, the nearest constructs' boxes (each body's parts at rest, made once per rebuild as one instanced
/// list) are drawn from the key light into one depth map that covers <see cref="Reach"/> metres round the camera. Every scene shader
/// reads it through <c>Lighting.Glsl</c>'s <c>SunShadow</c> (the LitFrame block's uShadowVP, uShadowC, uShadowQ; texture unit
/// <see cref="Unit"/>): the sun on terrain, props, buildings and the ships themselves is dimmed where a hull stands between, with a
/// soft edge of 12 taps, fading out at the map's edge. The baked light grid already shades the static world; this adds what moves.
/// Moving parts (doors, turrets) cast at their rest pose. Cost: one depth pass of instanced boxes (at most <see cref="MaxBodies"/>
/// bodies) and 12 texture reads per lit pixel; off, nothing runs and the shaders skip it.
/// </summary>
public static unsafe class SunShadows
{
    public const int Unit = 20, MaxBodies = 12;

    /// <summary>The setting; <see cref="ForcedOff"/>: --no-sun-shadows.</summary>
    public static bool On = true, ForcedOff;

    /// <summary>The map's size in texels (Video: Sun shadows Medium 2048, High 4096).</summary>
    public static int Size = 2048;

    /// <summary>Half the side of the square the map covers round the camera (m), and the key light's multiplier (Video: Key light).</summary>
    public static float Reach = 48f, KeyLight = 1f;

    /// <summary>Bodies drawn into the map last frame (F9, checks).</summary>
    public static int LastBodies, LastBoxes;

    /// <summary>True while the map holds this frame's shadows (the LitFrame block turns them on).</summary>
    public static bool Live { get; private set; }

    /// <summary>World to the map (u, v, depth as stored), relative to <see cref="Centre"/>, for the LitFrame block.</summary>
    public static Matrix4x4 Lookup { get; private set; } = Matrix4x4.Identity;

    public static Vector3 Centre { get; private set; }

    /// <summary>The map's texel in uv, and the comparison's sign (stored depth runs the other way with reversed depth).</summary>
    public static float TexelUv => 1f / Size;

    public static float DepthSign => RenderCaps.ReversedDepth ? -1f : 1f;

    /// <summary>The depth bias in stored units (about 4 cm) and the normal offset in metres (about two texels).</summary>
    public static float DepthBias => 0.04f / Depth;

    public static float NormalOffset => 2.2f * Reach * 2f / Size;

    private const float Depth = 600f; // (the light's depth range: 300 m above the centre to 300 m below)

    private sealed class Caster
    {
        public GlBuffer Vbo, Shapes;
        public int Count, ShapeVerts, Stamp = -1, Parts = -1, Doors = -1, Used;
        public float Radius;
    }

    private struct Pick
    {
        public Construct Body;
        public float Dist2;
    }

    private static readonly Dictionary<int, Caster> Casters = new();
    private static readonly List<int> Gone = new();
    private static readonly Pick[] Picks = new Pick[MaxBodies];
    private static float[] _boxes = new float[6 * 256];
    private static float[] _shapes = new float[6 * 1024];
    private static bool _ready, _broken;
    private static int _frame, _size;
    private static GlProgram _prog;
    private static int _lMvp;
    private static GlVertexArray _vao, _shapeVao;
    private static GlBuffer _cube, _cubeIdx;
    private static GlFramebuffer _fbo;
    private static uint _tex;

    /// <summary>
    /// The frame's map: the bodies of <paramref name="world"/> within reach of <paramref name="cam"/>, posed as ConstructRender
    /// will draw them (<paramref name="alpha"/> between ticks when <paramref name="blendTicks"/>). Call after the camera is set and
    /// before the world is drawn; leaves the main target bound with its viewport.
    /// </summary>
    public static void Render(Renderer gfx, ConstructWorld? world, Vector3 cam, Vector3 fwd, float alpha, bool blendTicks)
    {
        Live = false;
        LastBodies = LastBoxes = 0;
        _frame++;
        if ((_frame & 255) == 0)
            DropOld();
        var sun = Lighting.Current.SunDir;
        // (no key light: night, or the star behind the ground or eclipsed; any direction otherwise, since the star lights a ship
        // in orbit from below too and a planetoid's "up" isn't world +Y)
        if (!On || ForcedOff || _broken || world == null || world.Bodies.Count == 0 || Lighting.Current.SunColor.LengthSquared() < 1e-6f || CameraFeeds.InPass)
            return;
        var n = PickNear(world, cam, Reach, alpha, blendTicks);
        if (n == 0 || !Init())
        {
            Clear(n);
            return;
        }
        // The light's square: centred a little ahead of the camera, snapped to whole texels in the light's frame so it doesn't shimmer as you move.
        var up = MathF.Abs(sun.Y) > 0.98f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(up, sun));
        var lup = Vector3.Cross(sun, right);
        var flat = new Vector3(fwd.X, 0f, fwd.Z);
        var ahead = cam + (flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) * (Reach * 0.35f) : Vector3.Zero);
        var texel = Reach * 2f / Size;
        double cr = Math.Round(Vector3.Dot(ahead, right) / (double)texel) * texel, cu = Math.Round(Vector3.Dot(ahead, lup) / (double)texel) * texel, cs = Vector3.Dot(ahead, sun);
        var centre = new Vector3((float)(right.X * cr + lup.X * cu + sun.X * cs), (float)(right.Y * cr + lup.Y * cu + sun.Y * cs), (float)(right.Z * cr + lup.Z * cu + sun.Z * cs));
        var view = Matrix4x4.CreateLookAt(sun * (Depth * 0.5f), Vector3.Zero, up);
        var ortho = Matrix4x4.CreateOrthographicOffCenter(-Reach, Reach, -Reach, Reach, 0f, Depth); // (z 0 at the eye, 1 at the far end)
        var toClip = Matrix4x4.Identity;
        var toMap = Matrix4x4.Identity;
        (toMap.M11, toMap.M41, toMap.M22, toMap.M42) = (0.5f, 0.5f, 0.5f, 0.5f);
        if (RenderCaps.ReversedDepth)
            (toClip.M33, toClip.M43, toMap.M33, toMap.M43) = (-1f, 1f, -1f, 1f); // (clip 0..1 with the near end at 1, as the game draws)
        else
            (toClip.M33, toClip.M43) = (2f, -1f); // (GL's -1..1 clip; stored 0..1)
        var lightClip = view * ortho * toClip;
        Lookup = view * ortho * toMap;
        Centre = centre;

        var vp = stackalloc int[4];
        Native.glGetIntegerv(GL_VIEWPORT, vp);
        var blend = Native.glIsEnabled((GlEnum)(uint)GlCapability.Blend);
        var cull = Native.glIsEnabled((GlEnum)(uint)GlCapability.CullFace);
        var scissor = Native.glIsEnabled((GlEnum)(uint)GlCapability.ScissorTest);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        Native.glViewport(0, 0, Size, Size);
        Native.glColorMask(false, false, false, false);
        Native.glDepthMask(true);
        Native.glEnable(GlCapability.DepthTest);
        Native.glDisable(GlCapability.Blend);
        Native.glDisable(GlCapability.CullFace);
        Native.glDisable(GlCapability.ScissorTest);
        Native.glClear(GL_DEPTH_BUFFER_BIT); // (the game's clear depth: the far end either way)
        _prog.Use();
        Native.glBindVertexArray(_vao);
        int bodies = 0, boxes = 0;
        DrawPicked(n, lightClip, centre, alpha, blendTicks, ref bodies, ref boxes);
        (LastBodies, LastBoxes) = (bodies, boxes);
        Native.glBindVertexArray(default);
        Native.glBindBuffer(GL_ARRAY_BUFFER, default);
        Native.glColorMask(true, true, true, true);
        if (blend)
            Native.glEnable(GlCapability.Blend);
        if (cull)
            Native.glEnable(GlCapability.CullFace);
        if (scissor)
            Native.glEnable(GlCapability.ScissorTest);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, Renderer.MainFbo);
        Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D, _tex);
        Native.glActiveTexture(GL_TEXTURE0);
        gfx.Rebind(); // (the box renderer's program and arrays: its unbatched draws trust them)
        Live = LastBodies > 0;
        Clear(n);
    }

    /// <summary>
    /// Fills <see cref="Picks"/> with the nearest bodies (at most <see cref="MaxBodies"/>) whose sphere comes within 1.5 x
    /// <paramref name="reach"/> of <paramref name="cam"/>, as ConstructRender draws them; returns how many. <see cref="RoomShade"/> shares it.
    /// </summary>
    internal static int PickNear(ConstructWorld world, Vector3 cam, float reach, float alpha, bool blendTicks)
    {
        // The nearest bodies whose sphere reaches the square (a ship 40 m off still shades the ground at your feet).
        var n = 0;
        foreach (var c in world.Bodies)
        {
            if (c.PartCount == 0 || (ConstructRender.AwayOnly != 0 ? c.Id != ConstructRender.AwayOnly : c.SpaceAway != 0)) // (as ConstructRender draws them)
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var d2 = Vector3.DistanceSquared(pos, cam);
            var r = reach * 1.5f + c.Radius;
            if (d2 > r * r)
                continue;
            var k = n;
            if (n == MaxBodies)
            {
                k = 0;
                for (var i = 1; i < n; i++)
                    if (Picks[i].Dist2 > Picks[k].Dist2)
                        k = i;
                if (Picks[k].Dist2 <= d2)
                    continue;
            }
            else
                n++;
            Picks[k] = new Pick { Body = c, Dist2 = d2 };
        }
        return n;
    }

    /// <summary>Draws the picked bodies' boxes into the bound depth target with <paramref name="lightClip"/> (positions less <paramref name="centre"/>); the depth program and arrays must be bound.</summary>
    internal static void DrawPicked(int n, Matrix4x4 lightClip, Vector3 centre, float alpha, bool blendTicks, ref int bodies, ref int boxes)
    {
        for (var k = 0; k < n; k++)
        {
            var c = Picks[k].Body;
            var cast = CasterOf(c);
            if (cast.Count == 0)
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            var mvp = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos - centre) * lightClip;
            Native.glUniformMatrix4fv(_lMvp, 1, false, &mvp.M11);
            Native.glBindBuffer(GL_ARRAY_BUFFER, cast.Vbo);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 24, (void*)0);
            Native.glVertexAttribPointer(2, 3, GL_FLOAT, false, 24, (void*)12);
            Native.glDrawElementsInstanced(GL_TRIANGLES, 36, GL_UNSIGNED_SHORT, null, cast.Count);
            DrawShapes(cast.Shapes, cast.ShapeVerts);
            bodies++;
            boxes += cast.Count;
        }
    }

    /// <summary>Binds the depth program and its arrays (<see cref="RoomShade"/>'s passes); false when it can't be made.</summary>
    internal static bool BindDepthProgram()
    {
        if (!InitProgram())
            return false;
        _prog.Use();
        Native.glBindVertexArray(_vao);
        return true;
    }

    /// <summary>The unit cube's arrays with the caster attributes (<see cref="LampShadows"/>' own program draws with them); false when they can't be made.</summary>
    internal static bool BindCubeArrays()
    {
        if (!InitProgram())
            return false;
        Native.glBindVertexArray(_vao);
        return true;
    }

    /// <summary>Lets go of the picked bodies (no references kept past the frame).</summary>
    internal static void ClearPicks(int n) => Clear(n);

    /// <summary>Turns the map off for the passes after the world (the gun, menus): the next <see cref="Render"/> turns it on again.</summary>
    public static void EndWorld() => Live = false;

    private static void Clear(int n)
    {
        for (var k = 0; k < n; k++)
            Picks[k].Body = null!;
    }

    private const string Vs = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 iCentre;
        layout(location = 2) in vec3 iHalf;
        uniform mat4 uMvp;
        void main() { gl_Position = uMvp * vec4(iCentre + aPos * iHalf, 1.0); }
        """;

    private const string Fs = """
        #version 410 core
        out vec4 FragColor;
        void main() { FragColor = vec4(1.0); }
        """;

    private static bool _progReady, _progBroken;

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready && Resize();
        if (!InitProgram())
        {
            _broken = true;
            return false;
        }
        try
        {
            GlFramebuffer fb;
            Native.glGenFramebuffers(1, &fb);
            _fbo = fb;
            _ready = true;
            if (!Resize())
                return false;
            Console.WriteLine($"[shadows] sun shadows ready: {Size} x {Size}, {Reach * 2:0} m square, texture unit {Unit}");
        }
        catch (Exception ex)
        {
            _broken = true;
            _ready = false;
            Console.WriteLine($"[shadows] sun shadows off: {ex.Message}");
        }
        return _ready;
    }

    /// <summary>The depth program and the unit cube's arrays, shared by the sun's map and <see cref="RoomShade"/>'s.</summary>
    private static bool InitProgram()
    {
        if (_progReady || _progBroken)
            return _progReady;
        try
        {
            var v = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(Vs)).Compile();
            var f = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(Fs)).Compile();
            _prog = CreateProgram();
            Native.glAttachShader(_prog, v);
            Native.glAttachShader(_prog, f);
            Native.glLinkProgram(_prog);
            GlDebug.Label(GL_PROGRAM, _prog.Id, "SunShadows");
            int linked;
            Native.glGetProgramiv(_prog, GL_LINK_STATUS, &linked);
            if (linked == 0)
                throw new InvalidOperationException("the depth program did not link");
            v.Dispose();
            f.Dispose();
            fixed (byte* nm = "uMvp\0"u8)
                _lMvp = Native.glGetUniformLocation(_prog, nm);

            _vao = GenVertexArray();
            Native.glBindVertexArray(_vao);
            var corners = stackalloc float[24];
            for (var i = 0; i < 8; i++)
            {
                corners[i * 3] = (i & 1) == 0 ? -1f : 1f;
                corners[i * 3 + 1] = (i & 2) == 0 ? -1f : 1f;
                corners[i * 3 + 2] = (i & 4) == 0 ? -1f : 1f;
            }
            var idx = stackalloc ushort[36] { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 };
            _cube = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _cube);
            Native.glBufferData(GL_ARRAY_BUFFER, 24 * sizeof(float), corners, GL_STATIC_DRAW);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 12, (void*)0);
            EnableVertexAttribArray(0);
            _cubeIdx = GenBuffer();
            Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _cubeIdx);
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, 36 * sizeof(ushort), idx, GL_STATIC_DRAW);
            EnableVertexAttribArray(1);
            EnableVertexAttribArray(2);
            Native.glVertexAttribDivisor(1, 1);
            Native.glVertexAttribDivisor(2, 1);
            Native.glBindVertexArray(default);
            Native.glBindBuffer(GL_ARRAY_BUFFER, default);
            GlDebug.Label(GL_VERTEX_ARRAY, _vao.Id, "SunShadows");
            _shapeVao = GenVertexArray(); // (shaped boxes' triangles: DrawShapes points its three attributes at a body's list, no divisors)
            Native.glBindVertexArray(_shapeVao);
            EnableVertexAttribArray(0);
            EnableVertexAttribArray(1);
            EnableVertexAttribArray(2);
            Native.glBindVertexArray(default);
            _progReady = true;
        }
        catch (Exception ex)
        {
            _progBroken = true;
            Console.WriteLine($"[shadows] the depth program failed: {ex.Message}");
        }
        return _progReady;
    }

    /// <summary>The depth map at <see cref="Size"/> (made again when the setting changes).</summary>
    private static bool Resize()
    {
        if (_size == Size && _tex != 0)
            return true;
        if (_tex != 0)
        {
            var old = new GlTexture(_tex);
            Native.glDeleteTextures(1, &old);
        }
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        _tex = tex.Id;
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D, _tex);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_DEPTH_COMPONENT32F, Size, Size, 0, GL_DEPTH_COMPONENT, GL_FLOAT, null);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, _tex, 0);
        var status = Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, Renderer.MainFbo);
        _size = Size;
        if (status != GL_FRAMEBUFFER_COMPLETE)
        {
            Console.WriteLine($"[shadows] sun shadows off: the depth map's framebuffer is incomplete (0x{status:x})");
            _broken = true;
            _ready = false;
            return false;
        }
        return true;
    }

    /// <summary>
    /// The body's boxes at rest in its own frame (axis-aligned there: parts turn in quarter turns), made again on a rebuild and
    /// whenever a door starts moving or settles (<see cref="Construct.DoorPoseStamp"/>): a settled door's parts sit at its pose
    /// (shut or open), and a moving door is left out, so an open door lets the sun, the sky and lamps through.
    /// </summary>
    internal static (GlBuffer Vbo, int Count, GlBuffer Shapes, int ShapeVerts) CasterBoxes(Construct c)
    {
        var k = CasterOf(c);
        return (k.Vbo, k.Count, k.Shapes, k.ShapeVerts);
    }

    /// <summary>
    /// Draws a body's shaped boxes as their own triangles (<see cref="CasterOf"/>) with whichever depth program is in use: each
    /// vertex comes in as the caster's centre with no half size, so <c>iCentre + aPos * iHalf</c> is the point itself. Leaves the
    /// unit cube's arrays bound again (the caller sets the body's matrix first).
    /// </summary>
    internal static void DrawShapes(GlBuffer vbo, int verts)
    {
        if (verts == 0)
            return;
        Native.glBindVertexArray(_shapeVao);
        Native.glBindBuffer(GL_ARRAY_BUFFER, vbo);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 24, (void*)12); // (zeros: no corner)
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 24, (void*)0);
        Native.glVertexAttribPointer(2, 3, GL_FLOAT, false, 24, (void*)12); // (zeros: no half size)
        Native.glDrawArrays(GL_TRIANGLES, 0, verts);
        Native.glBindVertexArray(_vao);
    }

    /// <summary>A shaped box smaller than this (its largest half size, m) still casts as its box: a bolt's or a knob's shadow doesn't show the difference.</summary>
    private const float ShapeMinHalf = 0.12f;

    private static Caster CasterOf(Construct c)
    {
        if (!Casters.TryGetValue(c.Id, out var k))
        {
            Casters[c.Id] = k = new Caster();
            k.Vbo = GenBuffer();
            GlDebug.Label(GL_BUFFER, k.Vbo.Id, "SunShadows.caster");
        }
        k.Used = _frame;
        if (k.Stamp == c.RebuildStamp && k.Parts == c.PartCount && k.Doors == c.DoorPoseStamp)
            return k;
        k.Stamp = c.RebuildStamp;
        k.Parts = c.PartCount;
        k.Doors = c.DoorPoseStamp;
        var n = 0;
        var sv = 0;
        var doors = c.DoorMovingCount > 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var part = ref c.Parts[i];
            if (doors && c.MovingDoorOf(i) is var dk and >= 0 && ConstructDoors.IsMoving(c, c.Doors[dk].Part))
                continue; // (a door on its way: out until it settles, when its parts are at the new pose)
            foreach (var b in c.DefOf(i).Boxes)
            {
                if (b.Hidden || b.Glass > 0f)
                    continue;
                if (b.Shape != 0 && MathF.Max(b.Half.X, MathF.Max(b.Half.Y, b.Half.Z)) >= ShapeMinHalf && PartShapes.Mesh(b.Shape) is { Length: > 0 } mesh)
                {
                    // A shape (a sloped hull plate, a wedge, a cylinder) casts as itself, not as the box it fills: a sloped plate's
                    // box is a solid block round it, which hid the plate's own face from the sky and the sun (Karl, 2026-11-21: the
                    // Zero Day's sides went black outside, the dark interiors taking them for a shut room).
                    var verts = mesh.Length / PartShapes.FloatsPerVertex;
                    if ((sv + verts) * 6 > _shapes.Length)
                        Array.Resize(ref _shapes, Math.Max(_shapes.Length * 2, (sv + verts) * 6));
                    for (var v = 0; v < verts; v++)
                    {
                        var m = v * PartShapes.FloatsPerVertex;
                        var at = part.Pos + Rot24.Apply(part.Rot, b.ShapePoint(new Vector3(mesh[m], mesh[m + 1], mesh[m + 2])));
                        var so = (sv + v) * 6;
                        (_shapes[so], _shapes[so + 1], _shapes[so + 2], _shapes[so + 3], _shapes[so + 4], _shapes[so + 5]) = (at.X, at.Y, at.Z, 0f, 0f, 0f);
                    }
                    sv += verts;
                    continue;
                }
                if ((n + 1) * 6 > _boxes.Length)
                    Array.Resize(ref _boxes, _boxes.Length * 2); // (grows to the biggest body once)
                var centre = part.Pos + Rot24.Apply(part.Rot, b.Center);
                var half = Rot24.ApplyHalf(part.Rot, b.Half);
                var o = n * 6;
                (_boxes[o], _boxes[o + 1], _boxes[o + 2], _boxes[o + 3], _boxes[o + 4], _boxes[o + 5]) = (centre.X, centre.Y, centre.Z, half.X, half.Y, half.Z);
                n++;
            }
        }
        k.Count = n;
        Native.glBindBuffer(GL_ARRAY_BUFFER, k.Vbo);
        fixed (float* p = _boxes)
            Native.glBufferData(GL_ARRAY_BUFFER, Math.Max(n, 1) * 24, p, GL_STATIC_DRAW);
        k.ShapeVerts = sv;
        if (sv > 0)
        {
            if (k.Shapes.Id == 0)
            {
                k.Shapes = GenBuffer();
                Native.glBindBuffer(GL_ARRAY_BUFFER, k.Shapes);
                GlDebug.Label(GL_BUFFER, k.Shapes.Id, "SunShadows.shapes");
            }
            Native.glBindBuffer(GL_ARRAY_BUFFER, k.Shapes);
            fixed (float* p = _shapes)
                Native.glBufferData(GL_ARRAY_BUFFER, sv * 24, p, GL_STATIC_DRAW);
        }
        Native.glBindBuffer(GL_ARRAY_BUFFER, default);
        return k;
    }

    private static void DropOld()
    {
        Gone.Clear();
        foreach (var (id, k) in Casters)
            if (_frame - k.Used > 600)
            {
                var b = k.Vbo;
                Native.glDeleteBuffers(1, &b);
                if (k.Shapes.Id != 0)
                {
                    var sb = k.Shapes;
                    Native.glDeleteBuffers(1, &sb);
                }
                Gone.Add(id);
            }
        foreach (var id in Gone)
            Casters.Remove(id);
    }
}
