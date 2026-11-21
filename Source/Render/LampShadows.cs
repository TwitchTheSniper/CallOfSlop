using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Lamps cast shadows (Settings > Video > Lamp shadows; Karl, 2026-11-21: after the dark interiors, a lamp still lit the room
/// beyond its wall). The lamps <see cref="ConstructLamps"/> lights each frame (the nearest few) each get a slot: six depth maps
/// round the lamp, one per face of a cube, holding how far the light gets in each direction past the boxes of its own body and
/// of any body near it (the same instanced lists as <see cref="SunShadows"/>). <c>Lighting.Glsl</c>'s <c>LampShadow</c> reads
/// them for that dynamic light (its colour's alpha names the slot): a wall, a floor or a shut door between a lamp and a surface
/// keeps the lamp's light off it.
/// <para>
/// The cost is in the drawing of the maps, so they are kept: a slot's faces are drawn in its body's own frame, so a ship that
/// flies, turns or rolls keeps them, and drawn again only when the body is rebuilt, a door on it starts or stops moving
/// (<see cref="Construct.DoorPoseStamp"/>), another body near the lamp moves against it, or the lamp is new to the slot. At most
/// <see cref="Budget"/> slots are drawn a frame, nearest first; a lamp whose slot has never been drawn lights unshadowed until it
/// is. Read in the shader: four filtered compares a lamp a pixel. Off: nothing runs and the shader skips it.
/// </para>
/// Lamps on a moving segment (a hinge, a turret) are left unshadowed. A box that holds the lamp's point (its own housing), or a
/// small box within <see cref="SelfClear"/> of it, casts nothing: the lamp's own fixture doesn't shade its light.
/// </summary>
public static unsafe class LampShadows
{
    public const int Unit = 22;

    /// <summary>Most slots (one lamp each): every dynamic light could be a lamp.</summary>
    public const int MaxSlots = LightGrid.MaxDynamic;

    /// <summary>The setting (Video: Lamp shadows Low off, Medium, High); <see cref="ForcedOff"/>: --no-lamp-shadows.</summary>
    public static bool On = true, ForcedOff;

    /// <summary>Each face's size in texels and the slots in use (Medium 256 and 8, High 512 and 12).</summary>
    public static int Size = 256, Slots = 8;

    /// <summary>Slots drawn again at most a frame (six depth passes each).</summary>
    public static int Budget = 3;

    /// <summary>How far the maps hold distances (m): past <see cref="ConstructLamps.DarkRange"/>, so a lamp's range can change without drawing again.</summary>
    public const float Store = 16f;

    /// <summary>How near the lamp's point a small box casts nothing (m): the lamp's own fixture.</summary>
    public const float SelfClear = 0.16f;

    /// <summary>The cube faces are drawn a little wider than a quarter turn (the shader reads them the same), so the soft compare at a face's edge has texels on both sides.</summary>
    public const float Guard = 0.925926f; // (1 / 1.08; Lighting.Glsl's LampShadow says the same number)

    /// <summary>Other bodies drawn into one slot at most (a docked craft, a crate, a ship alongside).</summary>
    private const int MaxOthers = 6;

    /// <summary>True while some slot holds a drawn map (the LitFrame block turns them on).</summary>
    public static bool Live { get; private set; }

    /// <summary>Lamps shadowed, slots drawn and their boxes, last frame (F9, checks).</summary>
    public static int LastShadowed, LastDrawn, LastBoxes;

    /// <summary>Each slot's world-to-lamp turn as three rows (the body's axes in world space), for the LitFrame block.</summary>
    public static readonly Vector4[] Rows = new Vector4[MaxSlots * 3];

    /// <summary>The normal offset a metre from the lamp (m: about three texels there), the soft compare's step (uv) and the depth bias (stored units, signed).</summary>
    public static float NormalPerMetre => 3f * 2f / (Size * Guard);

    public static float TexelUv => 1f / Size;

    public static float SignedBias => (RenderCaps.ReversedDepth ? -1f : 1f) * 0.02f / Store;

    private sealed class Slot
    {
        public Construct? Body;
        public int BodyId, Part, Sub = -1, Stamp, Parts, Doors, Used = -1, Drawn = -1;
        public Vector3 Local;
        public int Others;
        public readonly int[] OtherId = new int[MaxOthers], OtherStamp = new int[MaxOthers], OtherDoors = new int[MaxOthers];
        public readonly Matrix4x4[] OtherRel = new Matrix4x4[MaxOthers];
    }

    private struct Want
    {
        public Construct Body;
        public int Part, Sub, Light;
        public Vector3 Local, Pos;
        public Quaternion Rot;
    }

    private static readonly Slot[] All = new Slot[MaxSlots];
    private static readonly Want[] Wants = new Want[MaxSlots];
    private static readonly Construct?[] Near = new Construct?[MaxOthers];
    private static readonly Matrix4x4[] NearRel = new Matrix4x4[MaxOthers];
    private static int _wants, _frame, _size, _layers;
    private static bool _ready, _broken;
    private static GlProgram _prog;
    private static int _lModel, _lFace, _lSelf, _lInv, _lRev, _lClear;
    private static GlFramebuffer _fbo;
    private static uint _tex;

    /// <summary>
    /// <see cref="ConstructLamps"/>: lamp <paramref name="sub"/> of part <paramref name="part"/> (its point <paramref name="local"/> in the body's
    /// frame) is dynamic light <paramref name="light"/> this frame, the body posed at <paramref name="pos"/>, <paramref name="rot"/>.
    /// </summary>
    public static void Offer(int light, Construct c, int part, int sub, Vector3 local, Vector3 pos, Quaternion rot)
    {
        if (!On || ForcedOff || _broken || _wants >= Math.Min(Slots, MaxSlots))
            return;
        Wants[_wants++] = new Want { Body = c, Part = part, Sub = sub, Light = light, Local = local, Pos = pos, Rot = rot };
    }

    /// <summary>
    /// The frame's maps for the lamps offered since the last call (after the frame's lights and before the world is drawn); names
    /// each shadowed light's slot in the dynamic lights. Leaves the main target bound with its viewport.
    /// </summary>
    public static void Render(Renderer gfx, ConstructWorld? world)
    {
        _frame++;
        Live = false;
        LastShadowed = LastDrawn = LastBoxes = 0;
        var n = _wants;
        _wants = 0;
        if (CameraFeeds.InPass)
        {
            Forget(n);
            return;
        }
        if (n == 0 || !On || ForcedOff || _broken || world == null || !Init())
        {
            Forget(n);
            return;
        }
        // Each wanted lamp keeps its slot from last frame, else takes the one used longest ago.
        Span<int> slotOf = stackalloc int[MaxSlots];
        for (var w = 0; w < n; w++)
        {
            ref var want = ref Wants[w];
            slotOf[w] = -1;
            for (var s = 0; s < Slots; s++)
                if (All[s] is { } sl && sl.BodyId == want.Body.Id && sl.Part == want.Part && sl.Sub == want.Sub && sl.Used != _frame && sl.Local == want.Local)
                {
                    slotOf[w] = s;
                    break;
                }
            if (slotOf[w] >= 0)
                All[slotOf[w]].Used = _frame;
        }
        for (var w = 0; w < n; w++)
        {
            if (slotOf[w] >= 0)
                continue;
            var best = -1;
            for (var s = 0; s < Slots; s++)
            {
                All[s] ??= new Slot();
                if (All[s].Used == _frame)
                    continue;
                if (best < 0 || All[s].Used < All[best].Used)
                    best = s;
            }
            if (best < 0)
                continue;
            var sl = All[best];
            ref var want = ref Wants[w];
            (sl.BodyId, sl.Part, sl.Sub, sl.Local, sl.Used, sl.Drawn) = (want.Body.Id, want.Part, want.Sub, want.Local, _frame, -1);
            slotOf[w] = best;
        }

        var vp = stackalloc int[4];
        var bound = false;
        var blend = false;
        var cull = false;
        var scissor = false;
        var drawn = 0;
        for (var w = 0; w < n; w++)
        {
            var s = slotOf[w];
            if (s < 0)
                continue;
            ref var want = ref Wants[w];
            var sl = All[s];
            var others = Gather(world, want.Body, want.Local, want.Pos, want.Rot);
            if (drawn < Budget && Stale(sl, want.Body, others))
            {
                if (!bound)
                {
                    Native.glGetIntegerv(GL_VIEWPORT, vp);
                    blend = Native.glIsEnabled((GlEnum)(uint)GlCapability.Blend);
                    cull = Native.glIsEnabled((GlEnum)(uint)GlCapability.CullFace);
                    scissor = Native.glIsEnabled((GlEnum)(uint)GlCapability.ScissorTest);
                    if (!SunShadows.BindCubeArrays())
                        break;
                    _prog.Use();
                    Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
                    Native.glViewport(0, 0, Size, Size);
                    Native.glColorMask(false, false, false, false);
                    Native.glDepthMask(true);
                    Native.glEnable(GlCapability.DepthTest);
                    Native.glDisable(GlCapability.Blend);
                    Native.glDisable(GlCapability.CullFace);
                    Native.glDisable(GlCapability.ScissorTest);
                    Native.glUniform1f(_lInv, 1f / Store);
                    Native.glUniform1f(_lRev, RenderCaps.ReversedDepth ? 1f : 0f);
                    Native.glUniform1f(_lClear, SelfClear);
                    bound = true;
                }
                Draw(s, sl, want.Body, want.Local, others);
                drawn++;
            }
            if (sl.Drawn < 0)
                continue; // (never drawn: unshadowed until its turn comes)
            var r0 = Vector3.Transform(Vector3.UnitX, want.Rot);
            var r1 = Vector3.Transform(Vector3.UnitY, want.Rot);
            var r2 = Vector3.Transform(Vector3.UnitZ, want.Rot);
            Rows[s * 3] = new Vector4(r0, 0f);
            Rows[s * 3 + 1] = new Vector4(r1, 0f);
            Rows[s * 3 + 2] = new Vector4(r2, 0f);
            LightGrid.SetDynamicSlot(want.Light, s);
            LastShadowed++;
        }
        if (bound)
        {
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
            gfx.Rebind();
        }
        LastDrawn = drawn;
        if (LastShadowed > 0)
        {
            Native.glActiveTexture(GL_TEXTURE0 + Unit);
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex);
            Native.glActiveTexture(GL_TEXTURE0);
        }
        Live = LastShadowed > 0;
        Forget(n);
        for (var k = 0; k < MaxOthers; k++)
            Near[k] = null;
    }

    /// <summary>Menus, pictures and the gun's pass: off until the next <see cref="Render"/>.</summary>
    public static void EndWorld() => Live = false;

    private static void Forget(int n)
    {
        for (var w = 0; w < n; w++)
            Wants[w].Body = null!;
    }

    /// <summary>The other bodies whose sphere reaches the lamp's (into <see cref="Near"/>, with each one's pose in the lamp's body frame); how many.</summary>
    private static int Gather(ConstructWorld world, Construct own, Vector3 local, Vector3 pos, Quaternion rot)
    {
        var at = pos + Vector3.Transform(local, rot);
        var inv = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(rot));
        var n = 0;
        foreach (var c in world.Bodies)
        {
            if (c == own || c.PartCount == 0 || (ConstructRender.AwayOnly != 0 ? c.Id != ConstructRender.AwayOnly : c.SpaceAway != 0))
                continue;
            var r = Store + c.Radius + 2f;
            if (Vector3.DistanceSquared(c.Pos, at) > r * r)
                continue;
            if (n == MaxOthers)
                break;
            Near[n] = c;
            NearRel[n] = Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.Pos - pos) * inv; // (its frame to the lamp body's)
            n++;
        }
        return n;
    }

    /// <summary>Whether slot <paramref name="sl"/> must be drawn again: new, its body rebuilt or a door moved, or the bodies near it changed or moved.</summary>
    private static bool Stale(Slot sl, Construct own, int others)
    {
        if (sl.Drawn < 0 || sl.Stamp != own.RebuildStamp || sl.Parts != own.PartCount || sl.Doors != own.DoorPoseStamp || sl.Others != others)
            return true;
        for (var k = 0; k < others; k++)
        {
            var c = Near[k]!;
            if (sl.OtherId[k] != c.Id || sl.OtherStamp[k] != c.RebuildStamp || sl.OtherDoors[k] != c.DoorPoseStamp || !Close(sl.OtherRel[k], NearRel[k]))
                return true;
        }
        return false;
    }

    /// <summary>Two poses within about a millimetre (the turn's terms and the offset).</summary>
    private static bool Close(in Matrix4x4 a, in Matrix4x4 b)
    {
        var d = MathF.Abs(a.M11 - b.M11) + MathF.Abs(a.M12 - b.M12) + MathF.Abs(a.M13 - b.M13)
              + MathF.Abs(a.M21 - b.M21) + MathF.Abs(a.M22 - b.M22) + MathF.Abs(a.M23 - b.M23)
              + MathF.Abs(a.M31 - b.M31) + MathF.Abs(a.M32 - b.M32) + MathF.Abs(a.M33 - b.M33);
        var t = MathF.Abs(a.M41 - b.M41) + MathF.Abs(a.M42 - b.M42) + MathF.Abs(a.M43 - b.M43);
        return d < 1e-4f && t < 2e-3f;
    }

    /// <summary>Draws slot <paramref name="s"/>'s six faces: its own body's boxes and the near ones', round the lamp, in the body's frame.</summary>
    private static void Draw(int s, Slot sl, Construct own, Vector3 local, int others)
    {
        (sl.Stamp, sl.Parts, sl.Doors, sl.Others, sl.Drawn) = (own.RebuildStamp, own.PartCount, own.DoorPoseStamp, others, _frame);
        for (var k = 0; k < others; k++)
            (sl.OtherId[k], sl.OtherStamp[k], sl.OtherDoors[k], sl.OtherRel[k]) = (Near[k]!.Id, Near[k]!.RebuildStamp, Near[k]!.DoorPoseStamp, NearRel[k]);
        var toLamp = Matrix4x4.CreateTranslation(-local);
        for (var f = 0; f < 6; f++)
        {
            Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, _tex, 0, s * 6 + f);
            Native.glClear(GL_DEPTH_BUFFER_BIT);
            Native.glUniform1i(_lFace, f);
            DrawBody(own, toLamp, true);
            for (var k = 0; k < others; k++)
            {
                var m = NearRel[k] * toLamp;
                DrawBody(Near[k]!, m, false);
            }
        }
    }

    private static void DrawBody(Construct c, Matrix4x4 model, bool self)
    {
        var (vbo, count) = SunShadows.CasterBoxes(c);
        if (count == 0)
            return;
        Native.glUniformMatrix4fv(_lModel, 1, false, &model.M11);
        Native.glUniform1f(_lSelf, self ? 1f : 0f);
        Native.glBindBuffer(GL_ARRAY_BUFFER, vbo);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 24, (void*)0);
        Native.glVertexAttribPointer(2, 3, GL_FLOAT, false, 24, (void*)12);
        Native.glDrawElementsInstanced(GL_TRIANGLES, 36, GL_UNSIGNED_SHORT, null, count);
        LastBoxes += count;
    }

    // Face f: axis f / 2, + for even f. The map's u, v are the other two axes (x: y, z; y: z, x; z: x, y) over the distance along
    // the axis, times Guard; Lighting.Glsl's LampShadow reads them the same way. Depth: the distance from the lamp over Store
    // (one minus that with reversed depth, so the game's own depth test and clear value keep the nearest).
    private const string Vs = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 iCentre;
        layout(location = 2) in vec3 iHalf;
        uniform mat4 uModel;
        uniform int uFace;
        uniform float uSelf;
        uniform float uClear;
        out vec3 vRel;
        void main()
        {
            vec3 c = (uModel * vec4(iCentre, 1.0)).xyz;
            if (uSelf > 0.5) // (the lamp's own fixture: a box holding its point, or a small one beside it; its body's frame turns nothing)
            {
                vec3 out3 = max(abs(c) - iHalf, vec3(0.0));
                if (all(lessThanEqual(abs(c), iHalf + 0.02)) || (max(max(iHalf.x, iHalf.y), iHalf.z) <= 0.25 && length(out3) < uClear))
                {
                    vRel = vec3(0.0);
                    gl_Position = vec4(2.0, 2.0, 2.0, 1.0); // (outside the clip box: nothing drawn)
                    return;
                }
            }
            vec3 r = (uModel * vec4(iCentre + aPos * iHalf, 1.0)).xyz;
            vRel = r;
            int a = uFace / 2;
            float s = (uFace & 1) == 0 ? 1.0 : -1.0;
            vec2 uv = a == 0 ? r.yz : a == 1 ? r.zx : r.xy;
            float w = s * (a == 0 ? r.x : a == 1 ? r.y : r.z);
            gl_Position = vec4(uv * 0.925926, 0.5 * w, w); // (Guard)
        }
        """;

    private const string Fs = """
        #version 410 core
        in vec3 vRel;
        uniform float uInv;
        uniform float uRev;
        out vec4 FragColor;
        void main()
        {
            float d = clamp(length(vRel) * uInv, 0.0, 1.0);
            gl_FragDepth = uRev > 0.5 ? 1.0 - d : d;
            FragColor = vec4(1.0);
        }
        """;

    private static bool Init()
    {
        if (_broken)
            return false;
        if (!_ready)
        {
            try
            {
                var v = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(Vs)).Compile();
                var f = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(Fs)).Compile();
                _prog = CreateProgram();
                Native.glAttachShader(_prog, v);
                Native.glAttachShader(_prog, f);
                Native.glLinkProgram(_prog);
                int linked;
                Native.glGetProgramiv(_prog, GL_LINK_STATUS, &linked);
                if (linked == 0)
                    throw new InvalidOperationException("the lamp depth program did not link");
                GlDebug.Label(GL_PROGRAM, _prog.Id, "LampShadows");
                v.Dispose();
                f.Dispose();
                _lModel = Loc("uModel\0"u8);
                _lFace = Loc("uFace\0"u8);
                _lSelf = Loc("uSelf\0"u8);
                _lInv = Loc("uInv\0"u8);
                _lRev = Loc("uRev\0"u8);
                _lClear = Loc("uClear\0"u8);
                GlFramebuffer fb;
                Native.glGenFramebuffers(1, &fb);
                _fbo = fb;
                _ready = true;
            }
            catch (Exception ex)
            {
                _broken = true;
                Console.WriteLine($"[shadows] lamp shadows off: {ex.Message}");
                return false;
            }
        }
        return Resize();
    }

    private static int Loc(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_prog, p);
    }

    /// <summary>The maps at <see cref="Size"/> for <see cref="Slots"/> lamps (made again when the setting changes; every slot is drawn again).</summary>
    private static bool Resize()
    {
        var layers = Math.Clamp(Slots, 1, MaxSlots) * 6;
        if (_size == Size && _layers == layers && _tex != 0)
            return true;
        if (_tex != 0)
        {
            var old = new GlTexture(_tex);
            Native.glDeleteTextures(1, &old);
        }
        for (var s = 0; s < MaxSlots; s++)
            All[s] = new Slot();
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        _tex = tex.Id;
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex);
        Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_DEPTH_COMPONENT16, Size, Size, layers, 0, GL_DEPTH_COMPONENT, GL_UNSIGNED_SHORT, null);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_COMPARE_MODE, (int)GL_COMPARE_REF_TO_TEXTURE);
        // (a point is lit while it is no farther than the map holds, the way stored depth runs)
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_COMPARE_FUNC, (int)(RenderCaps.ReversedDepth ? GL_GEQUAL : GL_LEQUAL));
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, _tex, 0, 0);
        var status = Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, Renderer.MainFbo);
        (_size, _layers) = (Size, layers);
        if (status != GL_FRAMEBUFFER_COMPLETE)
        {
            _broken = true;
            Console.WriteLine($"[shadows] lamp shadows off: the maps' framebuffer is incomplete (0x{status:x})");
            return false;
        }
        Console.WriteLine($"[shadows] lamp shadows ready: {layers / 6} lamps, faces of {Size} x {Size}, texture unit {Unit}");
        return true;
    }
}
