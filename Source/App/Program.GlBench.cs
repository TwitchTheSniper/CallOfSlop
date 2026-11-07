using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// <c>--gl-bench ["Map" ...] [--frames N] [--horde N] [--bench-size WxH] [--bench-shots]</c>: render timings for the OpenGL 4.6 experiments
/// (docs/render/OPENGL46.md "Measuring"). Opens the window, and for each map draws a few fixed views (the map-select
/// overview and two spawns, like <c>--light-shots</c>) at one fixed clock, with a synthetic zombie horde of N bodies
/// (posed by the real animation code, drawn through the real instanced batch) in front of the camera on Zombies maps.
/// Each view is drawn with every experiment on (whatever its default), with each one off in turn and with all off (the 4.1 path's code), the
/// configurations taking turns frame by frame, and logs per configuration the median
/// CPU time to submit the frame, the median frame time with glFinish (CPU + GPU, no overlap) and the median GPU time
/// (timestamp queries). Run it once as is and once with <c>--gl41</c> for the 4.1 path. Uses the smoke test's
/// throwaway save copy; no debug context (it would skew the numbers); vsync off.
/// </summary>
public static unsafe partial class Program
{
    private static bool _glBench;

    // Pass timings (--gl-bench only): DrawArena calls BenchPass(k) after pass k (0 before the first); CPU timestamps and
    // GPU timestamp queries, read after the bench's glFinish. One bool test per call when not benchmarking.
    private static readonly string[] BenchPassNames = { "", "buildings", "terrain", "props", "meshes", "foliage", "decals", "sky", "horde", "constructs", "particles", "post" };
    private static bool _passOn;
    private static readonly long[] PassCpu = new long[12];
    private static int _passMarks;
    private static GlGpuStamps? _passGpu;

    private static void BenchPass(int k)
    {
        if (!_passOn)
            return;
        PassCpu[k] = Stopwatch.GetTimestamp();
        _passGpu?.Stamp(k);
        _passMarks |= 1 << k;
    }

    private static readonly string[] BenchDefaultMaps = { "Downtown", "Ashgrave Cathedral", "Horde Test" };

    private static void RunGlBench(string[] args)
    {
        var names = args.SkipWhile(a => !a.Equals("--gl-bench", StringComparison.OrdinalIgnoreCase)).Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
        if (names.Count == 0)
            names.AddRange(BenchDefaultMaps);
        var frames = Math.Clamp(FlagAfter(args, "--frames", 60), 5, 2000);
        var horde = Math.Clamp(FlagAfter(args, "--horde", 300), 0, 5000);
        var shots = args.Any(a => a.Equals("--bench-shots", StringComparison.OrdinalIgnoreCase)); // smoke_bench_*.bmp: the configurations must look the same
        Glfw.SwapInterval(0);
        if (args.SkipWhile(a => !a.Equals("--bench-size", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()?.Split('x') is [var bw, var bh]
            && int.TryParse(bw, out var w) && int.TryParse(bh, out var h))
        {
            _window.Size = new int2(Math.Max(w, 64), Math.Max(h, 64)); // small: the pixels cost little and draw submission shows
            for (var i = 0; i < 5; i++)
                Glfw.PollEvents();
        }
        Console.WriteLine($"[glbench] {RenderCaps.Describe()}");
        ReadBenchVideo(args);
        BuildBenchConstructs(args);
        Console.WriteLine($"[glbench] compute self-check: {GlCompute.SelfCheck()}"); // the compute hook works (4.6 path) or is off (4.1)
        Console.WriteLine($"[glbench] {frames} frames a view and configuration, horde {horde} on Zombies maps; times are medians in ms");
        var configs = new List<string> { "all on" };
        if (RenderCaps.Gl46)
            configs.AddRange(RenderCaps.Experiments.Select(e => "no " + e.Name).Append("all off"));
        if (_benchWorld != null)
            configs.Add("no merge"); // constructs box by box, as before merged meshes (docs/render/MERGED-MESHES.md)
        var gpu = GlGpuStamps.Create(2);
        _passGpu = GlGpuStamps.Create(BenchPassNames.Length);
        _passOn = true;
        var np = BenchPassNames.Length;
        var passCpu = new double[configs.Count * np];
        var passGpu = new double[configs.Count * np];
        var passN = new int[configs.Count];
        var cpu = new double[frames * configs.Count];
        var full = new double[frames * configs.Count];
        var gpuMs = new double[frames * configs.Count];
        var totals = new Dictionary<string, (double Cpu, double Full, double Gpu, int Views)>();
        var pictureFailures = 0;
        foreach (var name in names)
        {
            var map = Maps.Find(name);
            if (map == null)
            {
                Console.WriteLine($"[glbench] no map \"{name}\"");
                continue;
            }
            World.SetMap(map);
            var zombies = map.Zombies != null ? horde : 0;
            var views = new List<(string Name, Vector3 Eye, Vector3 Target, float Fov)>();
            var (oe, ot, ofov, _) = MapThumbs.Camera(map);
            views.Add(("overview", oe, ot, ofov));
            for (var i = 0; i < Math.Min(2, map.Spawns.Count); i++)
            {
                var s = map.Spawns[i * (map.Spawns.Count - 1)];
                views.Add(($"spawn{i}", s + new Vector3(0, 1.7f, 0), new Vector3(0, 1.5f, 0), 1.2f));
            }
            foreach (var view in views)
            {
                byte[]? reference = null;
                // The configurations take turns frame by frame (A B C A B C ...), so load from anything else on the
                // machine falls on all of them alike instead of on whichever ran during a busy second.
                var nc = configs.Count;
                var n = new int[nc];
                var propDraws = new long[nc];
                var propRuns = new long[nc];
                var drawCalls = new long[nc];
                var mergedCalls = new long[nc];
                var dLoop = new double[nc];
                var dFlush = new double[nc];
                Gfx.BatchBoxes = Gfx.BatchDraws = 0;
                for (var f = -10 * nc; f < frames * nc && !_window.ShouldClose; f++) // 10 warm-up frames each (render data is built on the first)
                {
                    var c = (f % nc + nc) % nc;
                    foreach (var (e, _) in RenderCaps.Experiments)
                        RenderCaps.Force(e, !(configs[c] == "all off" || configs[c] == "no " + e));
                    ConstructRender.MergeOn = configs[c] != "no merge";
                    Glfw.PollEvents();
                    Native.glFinish();
                    _passMarks = 0;
                    ConstructRender.LoopMs = ConstructRender.FlushMs = 0;
                    var t0 = Stopwatch.GetTimestamp();
                    gpu?.Stamp(0);
                    BenchFrame(map, view.Eye, view.Target, view.Fov, zombies, f / nc);
                    gpu?.Stamp(1);
                    var t1 = Stopwatch.GetTimestamp();
                    Native.glFinish();
                    var t2 = Stopwatch.GetTimestamp();
                    if (shots && f >= (frames - 1) * nc)
                    {
                        // The last frame of each configuration: a picture, and compared pixel by pixel with "all on" (the
                        // same data drawn another way should give the same picture: a 1-level difference is the
                        // driver's MSAA resolve or blend order, more is a bug).
                        _shotFull = _benchFull;
                        SaveScreenshot($"bench_{Slug(map.Name)}_{view.Name}_{Slug(configs[c])}");
                        var px = ReadFrame();
                        if (c == 0)
                            reference = px;
                        else if (reference != null && reference.Length == px.Length)
                        {
                            int differ = 0, maxDelta = 0;
                            for (var i = 0; i < px.Length; i += 4)
                            {
                                var d = Math.Max(Math.Max(Math.Abs(px[i] - reference[i]), Math.Abs(px[i + 1] - reference[i + 1])), Math.Abs(px[i + 2] - reference[i + 2]));
                                if (d > 0)
                                    differ++;
                                maxDelta = Math.Max(maxDelta, d);
                            }
                            var total = px.Length / 4;
                            var bad = maxDelta > 2 || differ > total / 100;
                            Console.WriteLine($"[glbench] picture {map.Name} {view.Name} {configs[c]} vs all on: {differ} of {total} pixels differ, max channel delta {maxDelta}{(bad ? " PICTURES DIFFER" : "")}");
                            if (bad)
                                pictureFailures++;
                        }
                    }
                    _window.SwapBuffers();
                    if (f < 0)
                        continue;
                    if ((_passMarks & 255) == 255)
                    {
                        for (var p = 1; p < np; p++)
                        {
                            if ((_passMarks & (1 << p)) == 0)
                                continue;
                            var prev = p - 1;
                            while ((_passMarks & (1 << prev)) == 0)
                                prev--;
                            passCpu[c * np + p] += Stopwatch.GetElapsedTime(PassCpu[prev], PassCpu[p]).TotalMilliseconds;
                            if (_passGpu != null && _passGpu.Ready(p))
                                passGpu[c * np + p] += (_passGpu.Read(p) - (double)_passGpu.Read(prev)) / 1e6;
                        }
                        passN[c]++;
                    }
                    drawCalls[c] += _benchConstructDraws; // (the constructs pass's draw calls)
                    mergedCalls[c] += _benchMergedDraws;
                    dLoop[c] += ConstructRender.LoopMs;
                    dFlush[c] += ConstructRender.FlushMs;
                    propDraws[c] += PropGfx.LastDraws; // this frame's prop draw calls and visible runs
                    propRuns[c] += PropGfx.LastRuns;
                    var k = c * frames + n[c]++;
                    cpu[k] = Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
                    full[k] = Stopwatch.GetElapsedTime(t0, t2).TotalMilliseconds;
                    gpuMs[k] = gpu != null && gpu.Ready(1) ? (gpu.Read(1) - (double)gpu.Read(0)) / 1e6 : -1;
                }
                var boxes = Gfx.BatchBoxes / Math.Max((frames + 10) * nc, 1);
                for (var c = 0; c < nc; c++)
                {
                    var (mc, mf, mg) = (Median(cpu, c * frames, n[c]), Median(full, c * frames, n[c]), Median(gpuMs, c * frames, n[c]));
                    var pd = propDraws[c] / Math.Max(n[c], 1.0);
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"[glbench] {map.Name,-22} {view.Name,-8} {configs[c],-14} cpu {mc,7:0.000}  frame {mf,8:0.000}  gpu {(mg >= 0 ? mg.ToString("0.000", CultureInfo.InvariantCulture) : "n/a"),8}  ({n[c]} frames, {drawCalls[c] / Math.Max(n[c], 1.0):0} construct draw calls ({mergedCalls[c] / Math.Max(n[c], 1.0):0} merged), bodies {dLoop[c] / Math.Max(n[c], 1):0.000} ms + flush {dFlush[c] / Math.Max(n[c], 1):0.000} ms{(zombies > 0 ? $", {boxes} batched boxes a frame" : "")}{(pd > 0 ? $", props {pd:0.0} draw calls for {propRuns[c] / Math.Max(n[c], 1.0):0.0} runs" : "")})"));
                    var key = $"{map.Name}|{configs[c]}";
                    totals.TryGetValue(key, out var acc);
                    totals[key] = (acc.Cpu + mc, acc.Full + mf, acc.Gpu + Math.Max(mg, 0), acc.Views + 1);
                }
            }
            BenchSkyAlone(map, frames, shots);
            for (var c = 0; c < configs.Count; c++)
            {
                var parts = new List<string>();
                for (var p = 1; p < np; p++)
                    if (passCpu[c * np + p] > 0)
                        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{BenchPassNames[p]} {passCpu[c * np + p] / Math.Max(passN[c], 1):0.00}/{passGpu[c * np + p] / Math.Max(passN[c], 1):0.00}"));
                Console.WriteLine($"[glbench] passes {map.Name,-22} {configs[c],-14} cpu/gpu ms (mean): {string.Join(", ", parts)}");
                Array.Clear(passCpu, c * np, np);
                Array.Clear(passGpu, c * np, np);
                passN[c] = 0;
            }
        }
        _passOn = false;
        ConstructRender.MergeOn = true;
        if (_benchWorld != null)
        {
            var (mb, mc, ms, mbytes) = ConstructRender.MergedStats();
            Console.WriteLine($"[glbench] merged meshes: {mb} bodies, {mc} chunks, {ms} box slots, {mbytes / 1024} KB on the GPU ({_benchWorld.Bodies.Sum(c => c.PartCount)} parts now); most allocated by the constructs pass in a steady frame: {_benchConstructAlloc} B");
        }
        foreach (var (e, _) in RenderCaps.Experiments)
            RenderCaps.Force(e, null);
        foreach (var (key, t) in totals)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[glbench] summary {key.Replace('|', ' '),-40} cpu {t.Cpu / t.Views,7:0.000}  frame {t.Full / t.Views,8:0.000}  gpu {t.Gpu / t.Views,8:0.000}  (mean of {t.Views} views)"));
        Console.WriteLine($"[glbench] done{(shots ? $", {pictureFailures} picture(s) differ from all on" : "")}, GL error 0x{GlDebug.Error():X}");
    }

    private static GlGpuStamps? _skyStamps;

    /// <summary>
    /// The sky on its own: a full screen of sky (the camera looking just above the horizon, nothing else drawn) at High
    /// and at Low, each draw between two glFinish calls, timed with GPU timestamps and with the CPU clock (Mesa llvmpipe
    /// writes its timestamps when it bins a draw, not when it shades it, so on llvmpipe only the clock means anything).
    /// It is the sky shader's own cost at this size with every pixel sky; in play only the pixels the world leaves
    /// uncovered pay. Maps with a "skybox" only.
    /// </summary>
    private static void BenchSkyAlone(Map map, int frames, bool shots)
    {
        if (map.Skybox == null)
            return;
        _skyStamps ??= GlGpuStamps.Create(4);
        if (_skyStamps == null)
            return;
        var fb = _window.FramebufferSize;
        var (eye, _, fov, _) = MapThumbs.Camera(map);
        var fwd = Vector3.Normalize(new Vector3(0.3f, 0.35f, -1f));
        var gpu = new double[2 * frames];
        var wall = new double[2 * frames];
        var n = 0;
        for (var f = -5; f < frames && !_window.ShouldClose; f++)
        {
            Glfw.PollEvents();
            var t = ShotTime + Math.Max(f, 0) / 60.0;
            Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
            Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, eye + fwd, Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(fov, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
            for (var k = 0; k < 2; k++)
            {
                Native.glFinish();
                var c0 = Stopwatch.GetTimestamp();
                _skyStamps.Stamp(2 * k);
                SkyRenderer.Draw(Gfx, map, k == 0 ? SkyQuality.High : SkyQuality.Low, t, timed: false);
                _skyStamps.Stamp(2 * k + 1);
                Native.glFinish();
                if (f >= 0)
                {
                    wall[k * frames + n] = Stopwatch.GetElapsedTime(c0).TotalMilliseconds;
                    gpu[k * frames + n] = (_skyStamps.Read(2 * k + 1) - (double)_skyStamps.Read(2 * k)) / 1e6;
                }
                if (k == 0 && shots && f == frames - 1)
                    SaveScreenshot($"bench_{Slug(map.Name)}_sky_alone");
            }
            if (f >= 0)
                n++;
            _window.SwapBuffers();
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[glbench] sky alone {map.Name,-22} {fb.X}x{fb.Y}: High {Median(gpu, 0, n):0.000} ms GPU, {Median(wall, 0, n):0.000} ms clock; Low {Median(gpu, frames, n):0.000} ms GPU, {Median(wall, frames, n):0.000} ms clock (full screen of sky, median of {n})"));
    }

    /// <summary>The finished frame's RGBA pixels (full size).</summary>
    private static byte[] ReadFrame()
    {
        var fb = _window.FramebufferSize;
        var pixels = new byte[fb.X * fb.Y * 4];
        Native.glPixelStorei(GL_PACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            Native.glReadPixels(0, 0, fb.X, fb.Y, GL_RGBA, GL_UNSIGNED_BYTE, p);
        return pixels;
    }

    /// <summary>One bench frame: the arena at the fixed clock, plus the synthetic horde.</summary>
    private static void BenchFrame(Map map, Vector3 eye, Vector3 target, float fov, int zombies, int frame)
    {
        var fb = _window.FramebufferSize;
        Glfw.SetTime(ShotTime + frame / 60.0); // a steady 60 fps clock: wind, flicker and the horde move as in play
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(fov, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        ApplyVideoLevels();
        DrawArena();
        if (zombies > 0)
        {
            DrawBenchHorde(eye, target, zombies, (float)(frame / 60.0));
            BenchPass(8);
        }
        DrawBenchConstructs(eye, target, frame);
        DrawBenchFx(eye, target, frame);
        Gfx.SetFog(map.Sky, 0, 0);
    }

    private static int _benchFx;
    private static bool _benchFull;
    private static float _benchDist = 24f;

    /// <summary>
    /// <c>--bench-fx N</c>: about N particles alive (smoke columns and flames in front of the camera, a blast now and then) through the
    /// real pool and post effects; <c>--bench-mat L</c>, <c>--bench-particles L</c>, <c>--bench-post L</c>: the Video settings (0, 1, 2).
    /// </summary>
    private static void ReadBenchVideo(string[] args)
    {
        _settings.Materials = (VideoLevel)Math.Clamp(FlagAfter(args, "--bench-mat", (int)_settings.Materials), 0, 2);
        _settings.Particles = (VideoLevel)Math.Clamp(FlagAfter(args, "--bench-particles", (int)_settings.Particles), 0, 2);
        _settings.PostEffects = (VideoLevel)Math.Clamp(FlagAfter(args, "--bench-post", (int)_settings.PostEffects), 0, 2);
        _benchFx = Math.Clamp(FlagAfter(args, "--bench-fx", 0), 0, 500000);
        _benchDist = Math.Clamp(FlagAfter(args, "--bench-dist", 24), 3, 400);
        _benchFull = args.Any(a => a.Equals("--bench-full", StringComparison.OrdinalIgnoreCase));
        _benchEdit = args.Any(a => a.Equals("--bench-edit", StringComparison.OrdinalIgnoreCase)) ? Math.Clamp(FlagAfter(args, "--frames", 60), 5, 2000) - 12 : 0;
    }

    private static void DrawBenchFx(Vector3 eye, Vector3 target, int frame)
    {
        ApplyVideoLevels();
        if (_benchFx > 0)
        {
            var fwd = Vector3.Normalize(target - eye);
            var side = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
            var at = eye + fwd * _benchDist * 0.8f;
            var fps = 60f;
            var n = Math.Min(_benchFx / (int)(2.5f * fps), Fx.MaxBirths - 512);
            for (var i = 0; i < n; i++)
            {
                var a = i * 2.399963f;
                var r = 0.2f + 4f * ((i * 37 % 100) / 100f);
                var from = at + side * (MathF.Cos(a) * r) + new Vector3(0, -1.2f, 0) + fwd * (MathF.Sin(a) * r);
                Fx.Emit(i % 3 == 0 ? FxKind.Flame : i % 3 == 1 ? FxKind.Smoke : FxKind.Spark, from, new Vector3(MathF.Sin(a) * 0.4f, 2.5f, MathF.Cos(a) * 0.4f), i % 3 == 2 ? 0.03f : 0.35f, 2.5f);
            }
            Fx.Flame(FxKind.Rocket, at + side * 6f, -Vector3.UnitY, Vector3.Zero, 0.45f, 4f, 1f, 1f / 60f);
            Fx.Flame(FxKind.Flame, at - side * 6f, Vector3.UnitY, Vector3.Zero, 0.3f, 2f, 0.8f, 1f / 60f);
            PostFx.AddHaze(at + side * 6f + new Vector3(0, -3f, 0), 4f, 0.9f);
            PostFx.AddHaze(at - side * 6f + new Vector3(0, 2f, 0), 2.6f, 0.8f);
            if (frame % 60 == 0)
                Fx.Blast(at + fwd * 6f, 5f, default);
        }
        var tp = Stopwatch.GetTimestamp();
        Gfx.MarkGlow = false;
        FxGpu.Step(1f / 60f, (float)(frame / 60.0));
        var l = Lighting.Current;
        FxGpu.Draw(Gfx, (l.AmbSky + l.AmbGround) * 0.5f + l.SunColor * MathF.Max(l.SunDir.Y, 0f) * 0.55f);
        BenchPass(10);
        var fb = _window.FramebufferSize;
        PostFx.ApplyAo(Gfx, fb.X, fb.Y, Gfx.Proj);
        PostFx.Apply(Gfx, fb.X, fb.Y, frame / 60.0);
        BenchPass(11);
    }

    private static ConstructWorld? _benchWorld;
    private static int _benchConstructDraws, _benchMergedDraws;
    private static long _benchConstructAlloc;
    private static readonly Dictionary<Construct, Vector3> _benchHome = new();

    /// <summary>
    /// <c>--bench-bp "A,B" [--bench-copies N]</c>: N copies of each named blueprint on a grid in front of the camera, drawn by the
    /// real ConstructRender: what materials and part batches cost.
    /// </summary>
    private static void BuildBenchConstructs(string[] args)
    {
        var names = args.SkipWhile(a => !a.Equals("--bench-bp", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        if (names == null && FlagAfter(args, "--bench-capital", 0) <= 0)
            return;
        names ??= "";
        var copies = Math.Clamp(FlagAfter(args, "--bench-copies", 1), 1, 400);
        _benchWorld = new ConstructWorld();
        var cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(copies)));
        var made = 0;
        var parts = 0;
        foreach (var name in names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ConstructBuild.FindBlueprint(name) is not { } bp)
            {
                Console.WriteLine($"[glbench] no blueprint \"{name}\"");
                continue;
            }
            for (var i = 0; i < copies; i++)
                if (ConstructBuild.Build(_benchWorld, bp, new Vector3((i % cols - (cols - 1) * 0.5f + made / copies * cols) * 16f, 2f, -(i / cols) * 16f), Quaternion.Identity, out var why) is { } c)
                {
                    c.Rebuild();
                    var wear = FlagAfter(args, "--bench-wear", 0) / 100f;
                    for (var k = 0; k < c.PartCount && wear > 0; k++)
                        c.Parts[k].Health = c.Parts[k].D.Health * (1f - wear * ((k % 4 + 1) / 4f));
                    parts += c.PartCount;
                    made++;
                }
                else
                    Console.WriteLine($"[glbench] {name} didn't build: {why}");
        }
        if (FlagAfter(args, "--bench-capital", 0) is var cap and > 0)
        {
            // A capital hull (the self-test's: decks and plates, 300 m long) beside the blueprints.
            if (SelfTest.CapHull(_benchWorld, new Vector3(0, 2f, -40f), Math.Clamp(cap, 100, 20000), out var why) is { } hull)
            {
                hull.Rebuild();
                parts += hull.PartCount;
                made++;
            }
            else
                Console.WriteLine($"[glbench] the capital hull didn't build: {why}");
        }
        var boxes = ConstructParts.All.Sum(d => d.Boxes.Length);
        Console.WriteLine($"[glbench] constructs: {made} bodies, {parts} parts");
        Console.WriteLine($"[glbench] materials: {ConstructParts.All.Count} part types, {boxes} boxes at {System.Runtime.CompilerServices.Unsafe.SizeOf<CPartBox>()} B a box, {BoxLooks.Count} looks at {System.Runtime.CompilerServices.Unsafe.SizeOf<BoxLooks.Row>()} B a look, {Surfaces.File.Rows.Length} materials ({SurfaceGpu.Bytes} B on the GPU); merged mesh slot {(Renderer.MergeXformFloats + Renderer.MergeLookFloats) * sizeof(float)} B");
    }

    private static int _benchEdit, _benchEdited = -1;

    /// <summary>
    /// <c>--bench-edit</c>: the constructs change as they are drawn, the same way in every configuration (frame by frame, until
    /// 12 frames before the end, so the last pictures compare): a part painted and one damaged every frame, one taken off
    /// every 8th (merged meshes: looks rewritten alone, a chunk rebuilt; docs/render/MERGED-MESHES.md).
    /// </summary>
    private static void BenchEdit(int frame)
    {
        if (_benchWorld == null || frame < 0 || frame >= _benchEdit || frame == _benchEdited)
            return;
        _benchEdited = frame; // (once a frame, whichever configuration draws it first)
        var k = 0;
        foreach (var c in _benchWorld.Bodies)
        {
            k++;
            if (c.PartCount < 4)
                continue;
            c.Parts[(frame * 7 + k) % c.PartCount].Paint = 0x01000000u | (uint)(frame * 0x3A71F + k * 0x1F3) & 0xFFFFFF;
            ref var hurt = ref c.Parts[(frame * 13 + k) % c.PartCount];
            hurt.Health = MathF.Max(1, hurt.D.Health * (0.2f + 0.08f * (frame % 10)));
        }
        if (frame % 8 == 7)
        {
            var bodies = _benchWorld.Bodies.ToList();
            foreach (var c in bodies)
                if (c.PartCount > 8)
                {
                    _benchWorld.RemovePart(c, (frame * 31) % c.PartCount);
                    c.Rebuild();
                }
        }
    }

    private static void DrawBenchConstructs(Vector3 eye, Vector3 target, int frame)
    {
        if (_benchWorld == null)
            return;
        BenchEdit(frame);
        var fwd = target - eye;
        fwd.Y = 0;
        fwd = fwd.LengthSquared() > 1e-4f ? Vector3.Normalize(fwd) : new Vector3(0, 0, -1);
        var at = eye + fwd * _benchDist - new Vector3(0, 1.7f, 0);
        var yaw = MathF.Atan2(-fwd.X, -fwd.Z);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        foreach (var c in _benchWorld.Bodies)
        {
            if (!_benchHome.TryGetValue(c, out var home))
                _benchHome[c] = home = c.Pos;
            c.Pos = c.PrevPos = at + Vector3.Transform(home, turn);
            c.Rot = c.PrevRot = turn;
        }
        ConstructRender.Now = Glfw.Time;
        var draws0 = PerfCounters.Draws;
        Gfx.MergedDraws = 0;
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        ConstructRender.Draw(Gfx, _benchWorld, 1f, blendTicks: false);
        if (frame > 2 && (frame < 0 || frame >= _benchEdit))
            _benchConstructAlloc = Math.Max(_benchConstructAlloc, GC.GetAllocatedBytesForCurrentThread() - alloc0); // (steady frames: edits allocate, by design)
        ConstructRender.DrawGlass(Gfx, eye);
        _benchConstructDraws = PerfCounters.Draws - draws0;
        _benchMergedDraws = Gfx.MergedDraws;
        Gfx.SetBlend(false);
        Gfx.SetDepthWrite(true);
        BenchPass(9);
    }

    /// <summary>
    /// <paramref name="count"/> zombies walking on a grid in front of the camera (ground at the camera's feet), posed by
    /// <see cref="ZombieAnim.Evaluate"/> and drawn in one batch like the real horde (Program.Zombies.cs).
    /// </summary>
    private static void DrawBenchHorde(Vector3 eye, Vector3 target, int count, float t)
    {
        ZombieAnim.SetFrustum(Gfx.ViewProj);
        var fwd = target - eye;
        fwd.Y = 0;
        fwd = fwd.LengthSquared() > 1e-4f ? Vector3.Normalize(fwd) : new Vector3(0, 0, -1);
        var side = new Vector3(-fwd.Z, 0, fwd.X);
        var feet = eye - new Vector3(0, 1.7f, 0);
        var cols = Math.Max(1, (int)MathF.Sqrt(count));
        var yaw = MathF.Atan2(-fwd.X, -fwd.Z) + MathF.PI; // facing the camera
        Gfx.BeginBatch();
        for (var i = 0; i < count; i++)
        {
            int row = i / cols, col = i % cols;
            var pos = feet + fwd * (4f + row * 1.4f) + side * ((col - cols * 0.5f) * 1.2f);
            var a = new ZombieAnimInput
            {
                Pos = pos, Yaw = yaw, Mode = ZAnimMode.Locomotion, Speed = (ZSpeed)(i % 3), Id = (ushort)i,
                Gait = t * 6f + i * 0.37f, Move = 1f, Time = t + i, SinceState = 5f, Attack = -1f, Stagger = -1f, TearSeconds = 1f,
            };
            if (!ZombieAnim.Visible(a.Pos + new Vector3(0, 0.9f, 0), 1.7f))
                continue;
            ZombieAnim.Evaluate(a, out var pose);
            var dist = Vector3.Distance(eye, pos);
            ZombieAnim.Draw(Gfx, pose, ZShirt, ZSkin, ZPants, BodyModel.WantsDetail(pos), dist > ZombieAnim.LodRange, true);
        }
        Gfx.EndBatch();
    }

    private static string Slug(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());

    private static int FlagAfter(string[] args, string name, int fallback)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
    }

    private static double Median(double[] values, int start, int n)
    {
        if (n == 0)
            return -1;
        var copy = values[start..(start + n)];
        Array.Sort(copy);
        return copy[n / 2];
    }
}
