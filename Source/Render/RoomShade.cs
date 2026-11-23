using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Dark rooms in ships and vehicles (Settings > Video > Dark interiors; Karl, 2026-11-21: a Zero Day room with no lamp on was lit
/// like day). The sky's (ambient) light reached every surface whatever stood round it, so a shut room inside a hull was as bright
/// as the deck outside. Before the world is drawn, the nearest bodies' boxes (the same instanced lists as <see cref="SunShadows"/>)
/// are drawn into <see cref="Layers"/> depth maps, one from each of the eight diagonal directions of a cube, each a
/// <see cref="Reach"/> x 2 m square round the camera. <c>Lighting.Glsl</c>'s <c>RoomShade</c> reads them (one filtered compare
/// a direction, texture unit <see cref="Unit"/>): the share of the directions round a surface's normal that see open sky scales
/// its ambient light (and so its rim light and sky reflections, through gLitAmb), and the share towards the sun scales the sun
/// when the sun's own map is off. A room shut on every side gets none and is dark but for its lamps; a deck outside keeps its
/// light; corners and spots under overhangs lose a little. The first-person gun takes the share at the eye.
/// Cost: eight depth passes of instanced boxes and eight texture reads per lit pixel; off, nothing runs and the shaders skip it.
/// </summary>
public static unsafe class RoomShade
{
    public const int Unit = 21, Layers = 8;

    /// <summary>The setting; <see cref="ForcedOff"/>: --no-dark-interiors.</summary>
    public static bool On = true, ForcedOff;

    /// <summary>Each map's size in texels.</summary>
    public const int Size = 1024;

    /// <summary>Half the side of the square each map covers round the camera (m): a texel is under 5 cm, finer than a hull plate.</summary>
    public const float Reach = 24f;

    /// <summary>The ambient a room shut on every side still keeps (so shapes are just made out).</summary>
    public const float Floor = 0.03f;

    private const float Depth = 400f; // (each map's depth range: 200 m out along its direction to 200 m beyond the centre)

    /// <summary>True while the maps hold this frame's world (the LitFrame block turns them on).</summary>
    public static bool Live { get; private set; }

    /// <summary>The first-person gun's pass: every pixel takes the share at the eye (<see cref="Centre"/>).</summary>
    public static bool Probe { get; private set; }

    /// <summary>Where the maps are centred (the camera): the shader subtracts it from a world point before the lookups.</summary>
    public static Vector3 Centre { get; private set; }

    /// <summary>World less <see cref="Centre"/> to each map's u, v and stored depth.</summary>
    public static readonly Matrix4x4[] Lookup = new Matrix4x4[Layers];

    /// <summary>Bodies and boxes drawn into the maps last frame (all eight passes).</summary>
    public static int LastBodies, LastBoxes;

    /// <summary>The direction towards the sky of map <paramref name="i"/> (the shader's RoomDir says the same).</summary>
    public static Vector3 Dir(int i) => Vector3.Normalize(new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));

    /// <summary>The normal offset (m, about two texels) and the depth bias in stored units (about 5 cm), signed for the compare.</summary>
    public static float NormalOffset => 2.2f * Reach * 2f / Size;

    public static float SignedBias => (RenderCaps.ReversedDepth ? -1f : 1f) * 0.05f / Depth;

    private static bool _ready, _broken;

    /// <summary>The maps move with the camera in steps of this many metres: between steps the still bodies' maps are kept (<see cref="ShadowMapCache"/>).</summary>
    public const float SnapStep = 1f;

    private static readonly ShadowMapCache Cache = new();
    private static int _stillBodies, _stillBoxes;
    private static GlFramebuffer _fbo;
    private static uint _tex;

    /// <summary>
    /// The frame's maps round <paramref name="cam"/>, the bodies posed as ConstructRender draws them. Call after the camera is set and
    /// before the world is drawn; leaves the main target bound with its viewport. A camera feed's pass keeps the main pass's maps
    /// (they are in world space).
    /// </summary>
    public static void Render(Renderer gfx, ConstructWorld? world, Vector3 cam, float alpha, bool blendTicks)
    {
        Probe = false;
        if (CameraFeeds.InPass)
            return;
        Live = false;
        LastBodies = LastBoxes = 0;
        if (!On || ForcedOff || _broken || world == null || world.Bodies.Count == 0)
        {
            ForgetFar();
            return;
        }
        var n = SunShadows.PickNear(world, cam, Reach, alpha, blendTicks);
        if (!Init() || !SunShadows.BindDepthProgram())
        {
            SunShadows.ClearPicks(n);
            return;
        }
        Centre = cam;
        var ortho = Matrix4x4.CreateOrthographicOffCenter(-Reach, Reach, -Reach, Reach, 0f, Depth);
        var toClip = Matrix4x4.Identity;
        var toMap = Matrix4x4.Identity;
        (toMap.M11, toMap.M41, toMap.M22, toMap.M42) = (0.5f, 0.5f, 0.5f, 0.5f);
        if (RenderCaps.ReversedDepth)
            (toClip.M33, toClip.M43, toMap.M33, toMap.M43) = (-1f, 1f, -1f, 1f);
        else
            (toClip.M33, toClip.M43) = (2f, -1f);
        var texel = Reach * 2f / Size;

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
        int bodies = 0, boxes = 0;
        if (n > 0)
        {
            // The maps are placed round the camera snapped to SnapStep (then to whole texels in each map's frame, so they don't
            // shimmer): the still bodies' maps (Cache) stay right until the camera crosses a step or a still body changes.
            var anchor = new Vector3(MathF.Round(cam.X / SnapStep) * SnapStep, MathF.Round(cam.Y / SnapStep) * SnapStep, MathF.Round(cam.Z / SnapStep) * SnapStep);
            var stillKey = SunShadows.ClassifyPicks(n, alpha, blendTicks, out var moving);
            var key = (stillKey * 31 + BitConverter.SingleToInt32Bits(anchor.X)) * 31 + BitConverter.SingleToInt32Bits(anchor.Y) ^ (long)BitConverter.SingleToInt32Bits(anchor.Z) << 24 | 1; // (never 0: no map yet)
            var redraw = Cache.Ensure(Size, Layers, true) && key != Cache.Key;
            if (redraw)
                _stillBodies = _stillBoxes = 0;
            for (var i = 0; i < Layers; i++)
            {
                var d = Dir(i);
                var up = MathF.Abs(d.Y) > 0.98f ? Vector3.UnitZ : Vector3.UnitY;
                var right = Vector3.Normalize(Vector3.Cross(up, d));
                var lup = Vector3.Cross(d, right);
                double cr = Math.Round(Vector3.Dot(anchor, right) / (double)texel) * texel, cu = Math.Round(Vector3.Dot(anchor, lup) / (double)texel) * texel, cs = Vector3.Dot(anchor, d);
                var centre = new Vector3((float)(right.X * cr + lup.X * cu + d.X * cs), (float)(right.Y * cr + lup.Y * cu + d.Y * cs), (float)(right.Z * cr + lup.Z * cu + d.Z * cs));
                var view = Matrix4x4.CreateLookAt(d * (Depth * 0.5f), Vector3.Zero, up);
                Lookup[i] = Matrix4x4.CreateTranslation(cam - centre) * view * ortho * toMap;
                if (redraw)
                {
                    Cache.TargetStill(_fbo, i);
                    Native.glClear(GL_DEPTH_BUFFER_BIT);
                    SunShadows.DrawPicked(n, view * ortho * toClip, centre, d, Reach * 1.415f, alpha, blendTicks, ref _stillBodies, ref _stillBoxes, SunShadows.Which.Still); // (only the cells that reach the map's square)
                }
                if (moving > 0 || Cache.LiveDiffers || redraw)
                {
                    Cache.CopyToLive(_tex, i);
                    if (moving > 0)
                    {
                        Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
                        Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, _tex, 0, i);
                        SunShadows.DrawPicked(n, view * ortho * toClip, centre, d, Reach * 1.415f, alpha, blendTicks, ref bodies, ref boxes, SunShadows.Which.Moving);
                    }
                }
            }
            if (redraw)
            {
                Cache.Key = key;
                SunShadows.StillDrawn++;
            }
            Cache.LiveDiffers = moving > 0;
            bodies += _stillBodies;
            boxes += _stillBoxes;
        }
        SunShadows.ClearPicks(n);
        NearLive = bodies > 0;
        RenderFar(world, cam, toClip, toMap, alpha, blendTicks);
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
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex);
        // (the compare runs the way stored depth does: a point is lit while it is no farther than what the map holds)
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_COMPARE_FUNC, (int)(RenderCaps.ReversedDepth ? GL_GEQUAL : GL_LEQUAL));
        Native.glActiveTexture(GL_TEXTURE0 + FarUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _farTex);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_COMPARE_FUNC, (int)(RenderCaps.ReversedDepth ? GL_GEQUAL : GL_LEQUAL));
        Native.glActiveTexture(GL_TEXTURE0);
        gfx.Rebind();
        (LastBodies, LastBoxes) = (bodies / Layers, boxes / Layers);
        Live = NearLive || FarLive;
    }

    /// <summary>
    /// The far ring (Sean, 2026-11-23: the works shed was darkest in a bubble round him that moved as he walked): past
    /// <see cref="Reach"/> a surface took the open sky's light, so the floor and walls 25 m off were lit like day while the floor
    /// round him was dark, and the bright ring followed him. A second set of eight maps, <see cref="FarReach"/> m round the camera
    /// on texture unit <see cref="FarUnit"/>, holds the still bodies alone (a moving one near the camera is in the near maps), coarser
    /// and 16-bit; the shader blends into it where the near maps end and fades to the sky only at its edge. It moves in steps of
    /// <see cref="FarSnap"/> and is drawn one map a frame (each map keeps its own centre), so a step or a changed body costs one
    /// depth pass a frame for eight frames, never eight at once.
    /// </summary>
    public const int FarUnit = 23, FarSize = 1024;

    /// <summary>Half the side of each far map's square (m): a texel is about 19 cm, enough for a room's ambient 20 m off.</summary>
    public const float FarReach = 96f;

    /// <summary>The far maps move with the camera in steps of this many metres.</summary>
    public const float FarSnap = 16f;

    /// <summary>True while the near maps hold this frame's world; <see cref="FarLive"/> the same for at least one far map.</summary>
    public static bool NearLive { get; private set; }

    public static bool FarLive { get; private set; }

    /// <summary>World less <see cref="Centre"/> to each far map's u, v and stored depth (a map not drawn yet maps every point off its square).</summary>
    public static readonly Matrix4x4[] FarLookup = new Matrix4x4[Layers];

    /// <summary>Far maps drawn since start (the bench and checks: one a frame while the far ring catches up).</summary>
    public static int FarDrawn { get; private set; }

    /// <summary>The far maps' normal offset (m, about two texels) and depth bias (about 15 cm), signed for the compare.</summary>
    public static float FarNormalOffset => 2.2f * FarReach * 2f / FarSize;

    public static float FarSignedBias => (RenderCaps.ReversedDepth ? -1f : 1f) * 0.15f / Depth;

    private static readonly Vector3[] FarCentre = new Vector3[Layers];
    private static readonly long[] FarKey = new long[Layers];
    private static int _farNext, _farStillBodies, _farStillBoxes;
    private static ConstructWorld? _farWorld;
    private static GlFramebuffer _farFbo;

    /// <summary>Every far map counts as not drawn (another world, or the maps were off).</summary>
    private static void ForgetFar()
    {
        Array.Clear(FarKey);
        FarLive = false;
    }
    private static uint _farTex;

    /// <summary>Keeps the far maps (<see cref="FarReach"/>) round <paramref name="cam"/>: at most one drawn again this frame. The depth program is bound.</summary>
    private static void RenderFar(ConstructWorld world, Vector3 cam, Matrix4x4 toClip, Matrix4x4 toMap, float alpha, bool blendTicks)
    {
        if (!ReferenceEquals(world, _farWorld))
        {
            ForgetFar(); // (another world: the old one's maps would shade this one's ground until each was drawn again)
            _farWorld = world;
        }
        var n = SunShadows.PickNear(world, cam, FarReach, alpha, blendTicks, true);
        var stillKey = SunShadows.ClassifyPicks(n, alpha, blendTicks, out _);
        var anchor = new Vector3(MathF.Round(cam.X / FarSnap) * FarSnap, MathF.Round(cam.Y / FarSnap) * FarSnap, MathF.Round(cam.Z / FarSnap) * FarSnap);
        var key = (stillKey * 31 + BitConverter.SingleToInt32Bits(anchor.X)) * 31 + BitConverter.SingleToInt32Bits(anchor.Y) ^ (long)BitConverter.SingleToInt32Bits(anchor.Z) << 24 | 1; // (never 0: not drawn)
        var ortho = Matrix4x4.CreateOrthographicOffCenter(-FarReach, FarReach, -FarReach, FarReach, 0f, Depth);
        var texel = FarReach * 2f / FarSize;
        // The next map whose key isn't this frame's is drawn again (round the eight in turn, so a body that keeps changing can't starve one).
        var draw = -1;
        for (var j = 0; j < Layers && n > 0; j++)
        {
            var i = (_farNext + j) % Layers;
            if (FarKey[i] != key)
            {
                draw = i;
                break;
            }
        }
        var live = false;
        for (var i = 0; i < Layers; i++)
        {
            var d = Dir(i);
            var up = MathF.Abs(d.Y) > 0.98f ? Vector3.UnitZ : Vector3.UnitY;
            var view = Matrix4x4.CreateLookAt(d * (Depth * 0.5f), Vector3.Zero, up);
            if (i == draw)
            {
                var right = Vector3.Normalize(Vector3.Cross(up, d));
                var lup = Vector3.Cross(d, right);
                double cr = Math.Round(Vector3.Dot(anchor, right) / (double)texel) * texel, cu = Math.Round(Vector3.Dot(anchor, lup) / (double)texel) * texel, cs = Vector3.Dot(anchor, d);
                var centre = new Vector3((float)(right.X * cr + lup.X * cu + d.X * cs), (float)(right.Y * cr + lup.Y * cu + d.Y * cs), (float)(right.Z * cr + lup.Z * cu + d.Z * cs));
                Native.glBindFramebuffer(GL_FRAMEBUFFER, _farFbo);
                Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, _farTex, 0, i);
                Native.glViewport(0, 0, FarSize, FarSize);
                Native.glClear(GL_DEPTH_BUFFER_BIT);
                (_farStillBodies, _farStillBoxes) = (0, 0);
                SunShadows.DrawPicked(n, view * ortho * toClip, centre, d, FarReach * 1.415f, alpha, blendTicks, ref _farStillBodies, ref _farStillBoxes, SunShadows.Which.Still);
                (FarCentre[i], FarKey[i]) = (centre, key);
                _farNext = (i + 1) % Layers;
                FarDrawn++;
            }
            if (FarKey[i] == 0 || n == 0)
            {
                FarLookup[i] = new Matrix4x4 { M41 = 5f, M42 = 5f, M43 = 5f, M44 = 1f }; // (off the square: the sky's light)
                continue;
            }
            FarLookup[i] = Matrix4x4.CreateTranslation(cam - FarCentre[i]) * view * ortho * toMap;
            live = true;
        }
        SunShadows.ClearPicks(n);
        FarLive = live;
    }

    /// <summary>The first-person gun's pass (camera space): the maps stay on, read at the eye.</summary>
    public static void BeginProbe() => Probe = Live;

    /// <summary>Menus, pictures and thumbnails: off until the next <see cref="Render"/>.</summary>
    public static void EndWorld()
    {
        Live = false;
        Probe = false;
    }

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready;
        try
        {
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            _tex = tex.Id;
            Native.glActiveTexture(GL_TEXTURE0 + Unit);
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex);
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_DEPTH_COMPONENT32F, Size, Size, Layers, 0, GL_DEPTH_COMPONENT, GL_FLOAT, null);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_COMPARE_MODE, (int)GL_COMPARE_REF_TO_TEXTURE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_COMPARE_FUNC, (int)(RenderCaps.ReversedDepth ? GL_GEQUAL : GL_LEQUAL));
            Native.glActiveTexture(GL_TEXTURE0);
            GlFramebuffer fb;
            Native.glGenFramebuffers(1, &fb);
            _fbo = fb;
            Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
            Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, _tex, 0, 0);
            var status = Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, Renderer.MainFbo);
            if (status != GL_FRAMEBUFFER_COMPLETE)
                throw new InvalidOperationException($"the maps' framebuffer is incomplete (0x{status:x})");
            GlTexture farTex = default;
            Native.glGenTextures(1, &farTex);
            _farTex = farTex.Id;
            Native.glActiveTexture(GL_TEXTURE0 + FarUnit);
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _farTex);
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_DEPTH_COMPONENT16, FarSize, FarSize, Layers, 0, GL_DEPTH_COMPONENT, GL_UNSIGNED_SHORT, null);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_COMPARE_MODE, (int)GL_COMPARE_REF_TO_TEXTURE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_COMPARE_FUNC, (int)(RenderCaps.ReversedDepth ? GL_GEQUAL : GL_LEQUAL));
            Native.glActiveTexture(GL_TEXTURE0);
            GlFramebuffer ffb;
            Native.glGenFramebuffers(1, &ffb);
            _farFbo = ffb;
            Native.glBindFramebuffer(GL_FRAMEBUFFER, _farFbo);
            Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, _farTex, 0, 0);
            var farStatus = Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, Renderer.MainFbo);
            if (farStatus != GL_FRAMEBUFFER_COMPLETE)
                throw new InvalidOperationException($"the far maps' framebuffer is incomplete (0x{farStatus:x})");
            _ready = true;
            Console.WriteLine($"[shadows] dark interiors ready: {Layers} maps of {Size} x {Size}, {Reach * 2:0} m square, texture unit {Unit}; far ring {FarSize} x {FarSize}, {FarReach * 2:0} m, unit {FarUnit}");
        }
        catch (Exception ex)
        {
            _broken = true;
            Console.WriteLine($"[shadows] dark interiors off: {ex.Message}");
        }
        return _ready;
    }
}
