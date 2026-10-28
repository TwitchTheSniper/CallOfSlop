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

    /// <summary>The most dynamic lights (muzzle flashes, blasts) a frame takes; more are dropped.</summary>
    public const int MaxDynamic = 4;

    /// <summary>Settings > Video > Baked lighting (off: every map draws with the classic light).</summary>
    public static bool Enabled = true;

    /// <summary>Settings > Video > Dynamic lights (muzzle flashes and blasts light lit maps, at most <see cref="MaxDynamic"/>).</summary>
    public static bool DynamicEnabled = true;

    /// <summary>The LitFrame block as std140 lays it out: 6 vec4s, then 4 positions and 4 colours.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FrameBlock
    {
        public Vector4 Mode;    // x: 0 off, 1 grid, 2 probe; y: time; z: normal offset (m); w: dynamic lights
        public Vector4 Origin;  // grid min corner
        public Vector4 Scale;   // 1 / grid size
        public Vector4 ProbeA;  // probe: light rgb, flicker share
        public Vector4 ProbeB;  // probe: direction (view space), ambient
        public Vector4 ProbeC;  // probe: sun, phase
        public Vector4 Dyn0, Dyn1, Dyn2, Dyn3;     // dynamic lights: position, range
        public Vector4 DynC0, DynC1, DynC2, DynC3; // colour * intensity
    }

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
            var zeroC = stackalloc byte[2];
            Upload(_texA, UnitA, 1, 1, 1, GL_RGBA16F, GL_RGBA, GL_HALF_FLOAT, zeroA);
            Upload(_texB, UnitB, 1, 1, 1, GL_RGBA8_SNORM, GL_RGBA, GL_BYTE, zeroB);
            Upload(_texC, UnitC, 1, 1, 1, GL_RG8, GL_RG, GL_UNSIGNED_BYTE, zeroC);
            Native.glActiveTexture(GL_TEXTURE0);
            _lastMode = 0f;
            _ready = true;
            Console.WriteLine($"[lighting] baked light ready (uniform block binding {Binding}, texture units {UnitA}-{UnitC}), GL error 0x{(uint)Native.glGetError():x}");
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
            Upload(_texC, UnitC, bake.NX, bake.NY, bake.NZ, GL_RG8, GL_RG, GL_UNSIGNED_BYTE, c);
        Native.glActiveTexture(GL_TEXTURE0);
        _uploaded = bake;
        GpuBytes = bake.Bytes;
        Uploads++;
        Console.WriteLine($"[lighting] light grid {bake.NX} x {bake.NY} x {bake.NZ} at {bake.Cell:0.##} m, {bake.Sources.Length} lights, {bake.Bytes / 1024} KB uploaded");
    }

    // ---- Dynamic lights ---------------------------------------------------------------------------------------------

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
        _dynCount = 0;
        Send();
    }

    /// <summary>The first-person gun (drawn in camera space): the grid sampled once at the eye, its direction turned into view space (mode 2).</summary>
    public static void BeginViewModel(Map map, Vector3 eye, Matrix4x4 view, double time)
    {
        var bake = Enabled ? map.LightBake : null;
        if (!Init())
            return;
        FillProbe(ref _block, bake, eye, view, time);
        Send();
    }

    /// <summary>Menus, gun pictures and thumbnails: the classic light (mode 0).</summary>
    public static void Off()
    {
        if (!_ready || _lastMode == 0f)
            return;
        _block.Mode = Vector4.Zero;
        Send();
    }

    private static void Send()
    {
        Native.glBindBuffer(GL_UNIFORM_BUFFER, _ubo);
        fixed (FrameBlock* p = &_block)
            Native.glBufferSubData(GL_UNIFORM_BUFFER, 0, sizeof(FrameBlock), p);
        Native.glBindBuffer(GL_UNIFORM_BUFFER, default);
        _lastMode = _block.Mode.X;
    }

    /// <summary>How far along its normal a pixel samples the grid (a little over half a cell, so a wall reads the air in front of it).</summary>
    public static float NormalOffset(LightBake bake) => bake.Cell * 0.6f;

    /// <summary>Fills the world pass's block (no GL: the self-test runs it; no allocation).</summary>
    public static void Fill(ref FrameBlock b, LightBake? bake, double time, int dynamic, Vector4[] dynPos, Vector4[] dynCol)
    {
        if (bake == null)
        {
            b.Mode = Vector4.Zero;
            return;
        }
        var n = Math.Min(dynamic, MaxDynamic);
        b.Mode = new Vector4(1f, (float)(time % 3600.0), NormalOffset(bake), n);
        b.Origin = new Vector4(bake.Origin, 0f);
        b.Scale = new Vector4(1f / (bake.NX * bake.Cell), 1f / (bake.NY * bake.Cell), 1f / (bake.NZ * bake.Cell), 0f);
        b.Dyn0 = n > 0 ? dynPos[0] : default;
        b.Dyn1 = n > 1 ? dynPos[1] : default;
        b.Dyn2 = n > 2 ? dynPos[2] : default;
        b.Dyn3 = n > 3 ? dynPos[3] : default;
        b.DynC0 = n > 0 ? dynCol[0] : default;
        b.DynC1 = n > 1 ? dynCol[1] : default;
        b.DynC2 = n > 2 ? dynCol[2] : default;
        b.DynC3 = n > 3 ? dynCol[3] : default;
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
        b.Mode = new Vector4(2f, (float)(time % 3600.0), 0f, 0f);
        b.ProbeA = light;
        b.ProbeB = new Vector4(d, dir.W);
        b.ProbeC = new Vector4(sun, 0f, 0f);
    }
}
