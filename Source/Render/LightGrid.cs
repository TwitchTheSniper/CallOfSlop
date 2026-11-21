using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>Settings > Video > Lighting: what lit maps (docs/render/LIGHTING.md) draw.</summary>
public enum LightingQuality : byte
{
    /// <summary>The classic light on every map (the baked grid is ignored).</summary>
    Off = 0,

    /// <summary>The baked light grid.</summary>
    Baked = 1,

    /// <summary>The baked grid plus up to four dynamic lights (muzzle flashes, blasts).</summary>
    Full = 2,
}

/// <summary>
/// The GPU side of baked lighting (docs/render/LIGHTING.md): the drawn map's <see cref="LightBake"/> as three 3D textures on
/// units 12-14, and the small "LitFrame" uniform block every scene shader reads (<see cref="Lighting.Glsl"/>'s ApplyLightAt).
/// One block for all programs, rewritten at most three times a frame (world, first-person gun, off for menus and previews),
/// so switching the light costs one buffer update whatever the number of shaders or lights: there is no per-light loop at
/// draw time. A map without a bake (every unlit map) draws in mode 0, the classic light, exactly as before.
/// </summary>
public static unsafe class LightGrid
{
    public const int UnitA = 12, UnitB = 13, UnitC = 14;

    /// <summary>The uniform block binding point the LitFrame block uses in every program.</summary>
    public const uint Binding = 7;

    /// <summary>
    /// The most dynamic lights (muzzle flashes, blasts, construct lamps: ConstructLamps.cs) a frame takes; more are dropped.
    /// The shader's arrays take their size from <see cref="MaxDynamicGlsl"/>, which must say the same number.
    /// </summary>
    public const int MaxDynamic = 12;

    /// <summary>Is the shader's block the same size as <see cref="FrameBlock"/> (its arrays sized <see cref="MaxDynamic"/>)? Checked when the block is made.</summary>
    public static bool ShaderAgrees => Lighting.Glsl.Contains($"uDynPos[{MaxDynamic}]", StringComparison.Ordinal) && Lighting.Glsl.Contains($"uDynCol[{MaxDynamic}]", StringComparison.Ordinal);

    /// <summary>The dark interiors' lookups (RoomShade.cs), one mat4 each, as the std140 array lays them out.</summary>
    [System.Runtime.CompilerServices.InlineArray(RoomShade.Layers)]
    public struct RoomArray
    {
        private Matrix4x4 _first;
    }

    /// <summary>The block's dynamic lights, one vec4 each, as the std140 array lays them out.</summary>
    [System.Runtime.CompilerServices.InlineArray(MaxDynamic)]
    public struct DynArray
    {
        private Vector4 _first;
    }

    /// <summary>Settings > Video > Baked lighting (off: every map draws with the classic light).</summary>
    public static bool Enabled = true;

    /// <summary>
    /// --light-shots --light-debug only: 3 draws the grid's light alone on white (no sun, sky or texture), 4 the grid's
    /// sky and sun visibility (grey: sky, yellow: sun). 0 (always in the game): the normal light.
    /// </summary>
    public static float DebugView;

    /// <summary>Settings > Video > Dynamic lights (muzzle flashes and blasts light lit maps, at most <see cref="MaxDynamic"/>).</summary>
    public static bool DynamicEnabled = true;

    /// <summary>The LitFrame block as std140 lays it out: 6 vec4s, then the dynamic lights, the materials setting, the sun shadow map and the key light.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FrameBlock
    {
        public Vector4 Mode;    // x: 0 off, 1 grid, 2 probe; y: time; z: normal offset (m); w: dynamic lights
        public Vector4 Origin;  // grid min corner; w: the normal offset up and down (cells can be taller than wide)
        public Vector4 Scale;   // 1 / grid size
        public Vector4 ProbeA;  // probe: light rgb, flicker share
        public Vector4 ProbeB;  // probe: direction (view space), ambient
        public Vector4 ProbeC;  // probe: sun, flicker phasor x, y, pace (as stored: LightFlicker)
        public DynArray Dyn;  // dynamic lights: position, range
        public DynArray DynC; // colour * intensity
        public Vector4 Surf;    // x: the materials setting (Surfaces.cs); z: edge bevels on; w: material contrast on
        public Matrix4x4 ShadowVP; // world (less ShadowC) to the sun shadow map's u, v and stored depth (SunShadows.cs)
        public Vector4 ShadowC;   // xyz: the map's centre; w: the normal offset (m)
        public Vector4 ShadowQ;   // x: 1 while the map is live; y: the soft edge's radius (uv); z: the depth sign; w: the depth bias
        public Vector4 Key;       // x: the key light's multiplier (Video: Key light)
        public RoomArray RoomVP;  // world (less RoomC) to each dark-interiors map's u, v and stored depth (RoomShade.cs)
        public Vector4 RoomC;     // xyz: the maps' centre (the camera); w: the normal offset (m)
        public Vector4 RoomQ;     // x: 1 while the maps are live; y: the ambient a shut room keeps; z: 1 in the gun's pass (read at the eye); w: the signed depth bias
    }

    /// <summary>Settings > Video > Materials: 0 Low (the classic light), 1 Medium, 2 High.</summary>
    public static float SurfaceLevel = 1f;

    /// <summary>Settings > Video > Edge bevels (Materials Medium and High): box edges drawn as thin chamfers in <c>SurfaceLit</c>. <see cref="BevelsForcedOff"/>: --no-bevels, for a before picture.</summary>
    public static bool Bevels = true, BevelsForcedOff;

    /// <summary>Settings > Video > Material contrast (Materials Medium and High): paint at a satin roughness, dark framing and rubber rough, in <c>SurfaceLit</c>. <see cref="ContrastForcedOff"/>: --no-contrast.</summary>
    public static bool Contrast = true, ContrastForcedOff;

    /// <summary>The world is being drawn for the bloom: props mark what glows in the back buffer's alpha (PostFx.cs). Menus and pictures never do.</summary>
    public static bool MarkGlow;

    private static float _lastMark;
    private static bool _shadowSent, _roomSent;

    private static FrameBlock _block;
    private static readonly Vector4[] DynPos = new Vector4[MaxDynamic], DynCol = new Vector4[MaxDynamic];
    private static int _dynCount;

    private static bool _ready, _broken;
    private static GlBuffer _ubo;
    private static uint _texA, _texB, _texC;
    private static LightBake? _uploaded;
    private static float _lastMode = -1f;

    /// <summary>GPU bytes the current map's light textures take (0: none).</summary>
    public static long GpuBytes { get; private set; }

    /// <summary>Uploads since start (the log and the smoke tool).</summary>
    public static int Uploads { get; private set; }

    /// <summary>Called by <see cref="Lighting.Upload"/> for each scene program: its LitFrame block and the three samplers.</summary>
    public static void Register(GlProgram program)
    {
        if (!Init())
            return;
        uint index;
        fixed (byte* n = "LitFrame\0"u8)
            index = Native.glGetUniformBlockIndex(program, n);
        if (index != GL_INVALID_INDEX)
            Native.glUniformBlockBinding(program, index, Binding);
        SetSampler(program, "uLitA\0"u8, UnitA);
        SetSampler(program, "uLitB\0"u8, UnitB);
        SetSampler(program, "uLitC\0"u8, UnitC);
        SetSampler(program, "uSunShadow\0"u8, SunShadows.Unit);
        SetSampler(program, "uRoomShade\0"u8, RoomShade.Unit);
    }

    private static void SetSampler(GlProgram program, ReadOnlySpan<byte> nameZ, int unit)
    {
        fixed (byte* p = nameZ)
        {
            var loc = Native.glGetUniformLocation(program, p);
            if (loc >= 0)
                Native.glUniform1i(loc, unit);
        }
    }

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready;
        if (!ShaderAgrees)
            throw new InvalidOperationException($"LightGrid.MaxDynamic is {MaxDynamic} but Lighting.Glsl's uDynPos/uDynCol arrays are another size: change both");
        try
        {
            _ubo = GenBuffer();
            Native.glBindBuffer(GL_UNIFORM_BUFFER, _ubo);
            _block = default;
            fixed (FrameBlock* p = &_block)
                Native.glBufferData(GL_UNIFORM_BUFFER, sizeof(FrameBlock), p, GL_DYNAMIC_DRAW);
            Native.glBindBufferBase(GL_UNIFORM_BUFFER, Binding, _ubo);
            Native.glBindBuffer(GL_UNIFORM_BUFFER, default);
            // 1x1x1 stand-ins so the samplers always have a complete 3D texture (they are only read in modes 1 and 2).
            _texA = MakeTexture(UnitA);
            _texB = MakeTexture(UnitB);
            _texC = MakeTexture(UnitC);
            var zeroA = stackalloc ushort[4];
            var zeroB = stackalloc sbyte[4];
            var zeroC = stackalloc byte[4] { 0, 128, 128, 0 }; // (128: no flicker)
            Upload(_texA, UnitA, 1, 1, 1, GL_RGBA16F, GL_RGBA, GL_HALF_FLOAT, zeroA);
            Upload(_texB, UnitB, 1, 1, 1, GL_RGBA8_SNORM, GL_RGBA, GL_BYTE, zeroB);
            Upload(_texC, UnitC, 1, 1, 1, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, zeroC);
            Native.glActiveTexture(GL_TEXTURE0);
            _lastMode = 0f;
            _ready = true;
            Console.WriteLine($"[lighting] baked light ready (uniform block binding {Binding}, texture units {UnitA}-{UnitC}), GL error 0x{GlDebug.Error():x}");
        }
        catch (Exception ex)
        {
            _broken = true;
            Console.WriteLine($"[lighting] baked light unavailable: {ex.Message}");
        }
        return _ready;
    }

    private static uint MakeTexture(int unit)
    {
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        Native.glActiveTexture((uint)(GL_TEXTURE0 + unit));
        Native.glBindTexture(GL_TEXTURE_3D, tex.Id);
        Native.glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_3D, GL_TEXTURE_WRAP_R, (int)GL_CLAMP_TO_EDGE);
        return tex.Id;
    }

    private static void Upload(uint tex, int unit, int nx, int ny, int nz, uint internalFormat, uint format, uint type, void* data)
    {
        Native.glActiveTexture((uint)(GL_TEXTURE0 + unit));
        Native.glBindTexture(GL_TEXTURE_3D, tex);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        Native.glTexImage3D(GL_TEXTURE_3D, 0, (int)internalFormat, nx, ny, nz, 0, format, type, data);
    }

    /// <summary>Makes <paramref name="bake"/> the grid on the texture units (uploaded once per map; a no-op while it already is).</summary>
    private static void Bind(LightBake bake)
    {
        if (ReferenceEquals(bake, _uploaded))
            return;
        fixed (ushort* a = bake.Light)
            Upload(_texA, UnitA, bake.NX, bake.NY, bake.NZ, GL_RGBA16F, GL_RGBA, GL_HALF_FLOAT, a);
        fixed (sbyte* b = bake.Dir)
            Upload(_texB, UnitB, bake.NX, bake.NY, bake.NZ, GL_RGBA8_SNORM, GL_RGBA, GL_BYTE, b);
        fixed (byte* c = bake.Sun)
            Upload(_texC, UnitC, bake.NX, bake.NY, bake.NZ, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, c);
        Native.glActiveTexture(GL_TEXTURE0);
        _uploaded = bake;
        GpuBytes = bake.Bytes;
        Uploads++;
        Console.WriteLine($"[lighting] light grid {bake.NX} x {bake.NY} x {bake.NZ} at {bake.Cell:0.##} m{(bake.CellY != bake.Cell ? $" ({bake.CellY:0.##} m tall)" : "")}, {bake.Sources.Length} lights, {bake.Bytes / 1024} KB uploaded");
    }

    // ---- Dynamic lights ---------------------------------------------------------------------------------------------

    /// <summary>Dynamic lights this frame still has room for (phase 7b: lamps on constructs take what is left: ConstructLamps.cs).</summary>
    public static int DynamicFree => MaxDynamic - _dynCount;

    /// <summary>Adds a light for this frame only (a muzzle flash, a blast); at most <see cref="MaxDynamic"/>, the rest are dropped.</summary>
    public static void AddDynamic(Vector3 pos, Vector3 color, float range)
    {
        if (_dynCount >= MaxDynamic || range <= 0f)
            return;
        DynPos[_dynCount] = new Vector4(pos, range);
        DynCol[_dynCount] = new Vector4(color, 0f);
        _dynCount++;
    }

    // ---- Passes ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The world pass: the drawn map's grid at every pixel (mode 1), or the classic light when the map has none, the
    /// setting is off or the GPU path failed. Takes this frame's dynamic lights and clears the list for the next frame.
    /// </summary>
    public static void BeginWorld(Map map, double time)
    {
        var bake = Enabled ? map.LightBake : null;
        if (!Init())
            return;
        if (bake != null)
            Bind(bake);
        Fill(ref _block, bake, time, DynamicEnabled ? _dynCount : 0, DynPos, DynCol);
        if (DebugView > 0 && bake != null)
            _block.Mode.X = DebugView;
        _dynCount = 0;
        Send();
    }

    /// <summary>The first-person gun (drawn in camera space): the grid sampled once at the eye, its direction turned into view space (mode 2).</summary>
    public static void BeginViewModel(Map map, Vector3 eye, Matrix4x4 view, double time)
    {
        var bake = Enabled ? map.LightBake : null;
        if (!Init())
            return;
        SunShadows.EndWorld(); // (camera space: the world's shadow map means nothing here, and an unlit map's probe is mode 0)
        RoomShade.BeginProbe(); // (the dark interiors' maps are read at the eye: the gun is as dark as the room)
        FillProbe(ref _block, bake, eye, view, time);
        Send();
    }

    /// <summary>Menus, gun pictures and thumbnails: the classic light (mode 0).</summary>
    public static void Off()
    {
        SunShadows.EndWorld();
        RoomShade.EndWorld();
        if (!_ready || (_lastMode == 0f && _lastMark == 0f && !_shadowSent && !_roomSent))
            return;
        _block.Mode = Vector4.Zero;
        MarkGlow = false;
        Send();
    }

    /// <summary>
    /// The material swatches (SurfaceThumbs): the classic light like any picture, but with the materials drawn at Medium at least
    /// whatever the setting, so a swatch shows its pattern; <c>false</c> puts the setting back. No GL work when the block can't be made.
    /// </summary>
    public static void Swatches(bool on)
    {
        if (!Init())
            return;
        _block.Mode = Vector4.Zero;
        MarkGlow = false;
        var keep = SurfaceLevel;
        if (on)
            SurfaceLevel = MathF.Max(keep, 1f);
        Send();
        SurfaceLevel = keep;
    }

    private static void Send()
    {
        _block.Surf.X = SurfaceLevel;
        _block.Surf.Z = Bevels ? 1f : 0f;
        _block.Surf.W = Contrast ? 1f : 0f;
        _block.Surf.Y = _lastMark = MarkGlow ? 1f : 0f;
        _block.Key = new Vector4(SunShadows.KeyLight, 0f, 0f, 0f);
        if (SunShadows.Live && _block.Mode.X is < 1.5f or > 2.5f) // (not the first-person gun's probe pass, drawn in its own space)
        {
            _block.ShadowVP = SunShadows.Lookup;
            _block.ShadowC = new Vector4(SunShadows.Centre, SunShadows.NormalOffset);
            _block.ShadowQ = new Vector4(1f, SunShadows.TexelUv * 1.6f, SunShadows.DepthSign, SunShadows.DepthBias);
        }
        else
            _block.ShadowQ = Vector4.Zero;
        _shadowSent = _block.ShadowQ.X > 0f;
        if (RoomShade.Live)
        {
            for (var i = 0; i < RoomShade.Layers; i++)
                _block.RoomVP[i] = RoomShade.Lookup[i];
            _block.RoomC = new Vector4(RoomShade.Centre, RoomShade.NormalOffset);
            _block.RoomQ = new Vector4(1f, RoomShade.Floor, RoomShade.Probe ? 1f : 0f, RoomShade.SignedBias);
        }
        else
            _block.RoomQ = Vector4.Zero;
        _roomSent = _block.RoomQ.X > 0f;
        Native.glBindBuffer(GL_UNIFORM_BUFFER, _ubo);
        fixed (FrameBlock* p = &_block)
            Native.glBufferSubData(GL_UNIFORM_BUFFER, 0, sizeof(FrameBlock), p);
        Native.glBindBuffer(GL_UNIFORM_BUFFER, default);
        _lastMode = _block.Mode.X;
    }

    /// <summary>How far along its normal a pixel samples the grid (a little over half a cell, so a wall reads the air in front of it).</summary>
    public static float NormalOffset(LightBake bake) => bake.Cell * LightBake.NormalNudge;

    /// <summary>The same up and down (taller cells: a floor reads the air a little over half a cell above it).</summary>
    public static float NormalOffsetY(LightBake bake) => bake.CellY * LightBake.NormalNudge;

    /// <summary>Fills the world pass's block (no GL: the self-test runs it; no allocation).</summary>
    public static void Fill(ref FrameBlock b, LightBake? bake, double time, int dynamic, Vector4[] dynPos, Vector4[] dynCol)
    {
        var n = Math.Min(dynamic, MaxDynamic);
        if (bake == null)
        {
            // An unlit map: the classic light, plus this frame's dynamic lights when there are any (mode 5: phase 7b's lamps on constructs, ConstructLamps.cs).
            b.Mode = n > 0 ? new Vector4(5f, PropAnim.ShaderTime(time), 0f, n) : Vector4.Zero;
            if (n == 0)
                return;
        }
        else
        {
            b.Mode = new Vector4(1f, PropAnim.ShaderTime(time), NormalOffset(bake), n);
            b.Origin = new Vector4(bake.Origin, NormalOffsetY(bake));
            b.Scale = new Vector4(1f / (bake.NX * bake.Cell), 1f / (bake.NY * bake.CellY), 1f / (bake.NZ * bake.Cell), 0f);
        }
        for (var i = 0; i < MaxDynamic; i++)
        {
            b.Dyn[i] = i < n ? dynPos[i] : default;
            b.DynC[i] = i < n ? dynCol[i] : default;
        }
    }

    /// <summary>Fills the first-person gun's block: one CPU sample of the grid at <paramref name="eye"/> (no GL, no allocation).</summary>
    public static void FillProbe(ref FrameBlock b, LightBake? bake, Vector3 eye, Matrix4x4 view, double time)
    {
        if (bake == null)
        {
            b.Mode = Vector4.Zero;
            return;
        }
        bake.Sample(eye, out var light, out var dir, out var sun);
        var d = Vector3.TransformNormal(new Vector3(dir.X, dir.Y, dir.Z), view); // world to camera space, as the gun's normals
        b.Mode = new Vector4(2f, PropAnim.ShaderTime(time), 0f, 0f);
        b.ProbeA = light;
        b.ProbeB = new Vector4(d, dir.W);
        b.ProbeC = sun; // (the gun flickers with the room)
    }
}
