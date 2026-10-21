using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Small pictures of maps for the map select screen, rendered with the game's own renderers into offscreen textures
/// (one framebuffer object, a colour texture per map, a shared depth buffer) and cached:
/// <list type="bullet">
/// <item>The screen asks with <see cref="Get"/> while drawing; a map it hasn't seen is queued (nothing renders then).</item>
/// <item><see cref="Pump"/> runs at the start of the next frame, before the scene, and renders at most one queued map, so
/// opening the screen costs one thumbnail per frame instead of a long freeze.</item>
/// <item>Each picture is a fixed 3/4 view from above with boxes, movers, terrain, props, meshes, low foliage and the map's sky. It is made
/// once per map and kept until the map's fingerprint (<see cref="Map.ContentHash"/>) changes, e.g. after "Refresh maps"
/// with an edited file; identical maps keep their picture across reloads.</item>
/// <item>The terrain, prop, mesh and foliage renderers here are separate instances from the lobby background's, so making
/// a thumbnail never throws away the background map's GPU buffers.</item>
/// <item>If the framebuffer can't be made (or a render throws), the map is marked failed and the screen draws a coloured
/// tile with an icon instead.</item>
/// </list>
/// Logs a "[mapselect]" line per thumbnail (CPU time including a glFinish, so it is the real cost) and a summary once the
/// queue is empty.
/// </summary>
public static unsafe class MapThumbs
{
    /// <summary>Thumbnail size in pixels (16:9). Mipmapped, so smaller tiles still look smooth.</summary>
    public const int Width = 384, Height = 216;

    public enum State
    {
        Pending,
        Ready,
        Failed,
    }

    private sealed class Entry
    {
        public required string Name;
        public uint Hash;
        public uint Texture;
        public State State;
    }

    private static readonly Dictionary<Map, Entry> ByMap = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<string, Entry> ByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(Map Map, Entry Entry)> _queue = new();
    private static readonly List<uint> DeleteLater = new(); // textures of replaced pictures (freed in Pump, outside the UI frame)
    private static int _generation = -1;

    private static GlFramebuffer _fbo;
    private static GlRenderbuffer _depth;
    private static bool _glReady, _glBroken;

    private static readonly TerrainRenderer TerrainR = new();
    private static readonly PropRenderer PropR = new();
    private static readonly MeshRenderer MeshR = new();
    private static readonly FoliageRenderer FoliageR = new();

    // Stats for the log and the smoke test (since the last time the queue emptied).
    private static int _batchCount, _batchFailed;
    private static double _batchMs, _worstMs;
    private static string _worstName = "";

    /// <summary>Thumbnails rendered (and failed) since the game started.</summary>
    public static int Rendered { get; private set; }

    public static int Failed { get; private set; }

    /// <summary>Total and worst milliseconds spent rendering thumbnails since the game started.</summary>
    public static double TotalMs { get; private set; }

    public static double WorstMs { get; private set; }

    /// <summary>Maps waiting for a picture.</summary>
    public static int Pending => _queue.Count;

    /// <summary>
    /// The picture of <paramref name="map"/>: a GL texture name when <paramref name="state"/> is Ready, else 0 (Pending: it's
    /// queued; Failed: draw a fallback). Cheap: a dictionary lookup per call (the fingerprint is only computed for a map
    /// object seen for the first time).
    /// </summary>
    public static uint Get(Map map, out State state)
    {
        if (_generation != Maps.Generation)
        {
            ByMap.Clear(); // a reload made new map objects; ByName still finds pictures of unchanged maps
            _generation = Maps.Generation;
        }
        if (!ByMap.TryGetValue(map, out var e))
        {
            var hash = map.ContentHash;
            if (ByName.TryGetValue(map.Name, out var old) && old.Hash == hash)
                e = old;
            else
            {
                if (old != null)
                {
                    if (old.Texture != 0)
                        DeleteLater.Add(old.Texture);
                    old.Texture = 0;
                    old.State = State.Failed; // anything still holding the old entry stops drawing its texture
                    _queue.RemoveAll(q => q.Entry == old);
                }
                e = new Entry { Name = map.Name, Hash = hash, State = _glBroken ? State.Failed : State.Pending };
                ByName[map.Name] = e;
                if (e.State == State.Pending)
                    _queue.Add((map, e));
            }
            ByMap[map] = e;
        }
        state = e.State;
        return e.State == State.Ready ? e.Texture : 0;
    }

    /// <summary>
    /// Renders at most one queued thumbnail (call at the start of a frame, before the scene is drawn: it changes the
    /// framebuffer, viewport and shader state, and restores the framebuffer and viewport).
    /// </summary>
    public static void Pump(Renderer gfx, double now)
    {
        if (DeleteLater.Count > 0)
        {
            foreach (var t in DeleteLater)
            {
                var tex = new GlTexture(t);
                Native.glDeleteTextures(1, &tex);
            }
            DeleteLater.Clear();
        }
        if (_queue.Count == 0)
            return;
        var (map, entry) = _queue[0];
        _queue.RemoveAt(0);
        if (!_glReady && !_glBroken)
            InitGl();
        if (_glBroken)
        {
            Fail(entry);
            while (_queue.Count > 0)
            {
                Fail(_queue[0].Entry);
                _queue.RemoveAt(0);
            }
            return;
        }

        var sw = Stopwatch.StartNew();
        int prevFbo;
        Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prevFbo);
        var vp = stackalloc int[4];
        Native.glGetIntegerv(GL_VIEWPORT, vp);
        uint texture = 0;
        try
        {
            texture = MakeTexture();
            Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
            Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
            var status = (uint)Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
            if (status != GL_FRAMEBUFFER_COMPLETE)
                throw new InvalidOperationException($"framebuffer incomplete (0x{status:X})");
            RenderMap(gfx, map, now);
            Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prevFbo));
            Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
            Native.glBindTexture(GL_TEXTURE_2D, texture);
            Native.glGenerateMipmap(GL_TEXTURE_2D);
            Native.glBindTexture(GL_TEXTURE_2D, 0);
            Native.glFinish(); // so the time below is what the picture really cost (once per map, menus only)
            var err = (uint)Native.glGetError();
            if (err != 0)
                Console.WriteLine($"[mapselect] GL error 0x{err:X} after the thumbnail of '{map.Name}' (picture kept)");
            if (entry.State == State.Pending) // not replaced meanwhile
            {
                entry.Texture = texture;
                entry.State = State.Ready;
                texture = 0;
            }
            var ms = sw.Elapsed.TotalMilliseconds;
            Rendered++;
            TotalMs += ms;
            WorstMs = Math.Max(WorstMs, ms);
            _batchCount++;
            _batchMs += ms;
            if (ms > _worstMs)
                (_worstMs, _worstName) = (ms, map.Name);
            Console.WriteLine($"[mapselect] thumbnail '{map.Name}' {Width}x{Height}: {ms:0.0} ms ({map.Solid.Count} boxes"
                + $"{(map.HasTerrain ? ", terrain" : "")}{(map.FoliageInstances.Length > 0 ? $", {map.FoliageInstances.Length} plants at low" : "")}), {_queue.Count} left");
        }
        catch (Exception ex)
        {
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prevFbo));
            Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
            Console.WriteLine($"[mapselect] thumbnail '{map.Name}' failed: {ex.Message} (showing a plain tile)");
            Fail(entry);
        }
        finally
        {
            if (texture != 0)
            {
                var tex = new GlTexture(texture);
                Native.glDeleteTextures(1, &tex);
            }
            gfx.Rebind();
        }
        if (_queue.Count == 0 && _batchCount + _batchFailed > 0)
        {
            Console.WriteLine($"[mapselect] {_batchCount} thumbnail(s) in {_batchMs:0.0} ms (avg {_batchMs / Math.Max(1, _batchCount):0.0} ms, worst {_worstMs:0.0} ms '{_worstName}')"
                + $"{(_batchFailed > 0 ? $", {_batchFailed} failed" : "")}; one per frame, cached until a map file changes");
            _batchCount = _batchFailed = 0;
            _batchMs = _worstMs = 0;
            _worstName = "";
        }
    }

    private static void Fail(Entry e)
    {
        if (e.State == State.Pending)
            e.State = State.Failed;
        Failed++;
        _batchFailed++;
    }

    private static void InitGl()
    {
        try
        {
            GlFramebuffer fbo;
            Native.glGenFramebuffers(1, &fbo);
            GlRenderbuffer depth;
            Native.glGenRenderbuffers(1, &depth);
            Native.glBindRenderbuffer(GL_RENDERBUFFER, depth);
            Native.glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH_COMPONENT24, Width, Height);
            Native.glBindRenderbuffer(GL_RENDERBUFFER, new GlRenderbuffer(0));
            int prev;
            Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prev);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, fbo);
            Native.glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, depth);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prev));
            _fbo = fbo;
            _depth = depth;
            var err = (uint)Native.glGetError();
            if (fbo.Id == 0 || depth.Id == 0 || err != 0)
                throw new InvalidOperationException($"framebuffer {fbo.Id}, depth buffer {depth.Id}, GL error 0x{err:X}");
            _glReady = true;
            Console.WriteLine($"[mapselect] thumbnail framebuffer ready ({Width}x{Height}, depth 24)");
        }
        catch (Exception ex)
        {
            _glBroken = true;
            Console.WriteLine($"[mapselect] no thumbnail framebuffer ({ex.Message}): the map select screen shows plain tiles");
        }
    }

    private static uint MakeTexture()
    {
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, Width, Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glBindTexture(GL_TEXTURE_2D, 0);
        if (tex.Id == 0)
            throw new InvalidOperationException("glGenTextures gave no texture");
        return tex.Id;
    }

    /// <summary>The camera for a map's picture: high 3/4 view from the south-east corner, framing the whole floor.</summary>
    public static (Vector3 Eye, Vector3 Target, float FovY, float Far) Camera(Map map)
    {
        var target = new Vector3(0, map.HasTerrain ? map.GroundHeight(0, 0) : 0f, 0);
        var radius = MathF.Sqrt(map.HalfX * map.HalfX + map.HalfZ * map.HalfZ);
        const float fovY = 0.7f;
        const float pitch = 0.88f; // ~50 degrees down
        const float yaw = 0.62f;
        var dist = radius * 0.8f / MathF.Tan(fovY / 2);
        var dir = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        return (target + dir * dist, target, fovY, dist + radius * 3f);
    }

    private static void RenderMap(Renderer gfx, Map map, double now)
    {
        gfx.BeginFrame(Width, Height, map.Sky); // viewport, clear and the box shader
        SkyRenderer.Use(map); // the map's own sun and sky fog (the scene puts its map's light back next frame)
        var (eye, target, fovY, far) = Camera(map);
        gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(fovY, Width / (float)Height, MathF.Max(0.1f, far * 0.002f), far));
        var dist = Vector3.Distance(eye, target);
        gfx.SetFog(map.Sky, dist * 1.2f, dist * 3f); // just a hint of haze on the far side of big maps
        for (var i = 0; i < map.Solid.Count; i++)
            if (!map.IsPropCollider(i))
                gfx.DrawBox(map.Solid[i], map.Colors[i], mode: 1, material: map.Materials[i]);
        MoverRender.DrawImmediate(gfx, map, 0); // movers at their start pose
        TerrainR.Draw(gfx, map);
        PropR.Draw(map, gfx, now);
        MeshR.Draw(map, gfx, now);
        FoliageR.Draw(map, gfx, FoliageQuality.Low, now);
        SkyRenderer.Draw(gfx, map, SkyQuality.High, now, timed: false); // the sky behind the far side (maps with a "skybox")
        gfx.SetFog(map.Sky, 0, 0);
    }
}
