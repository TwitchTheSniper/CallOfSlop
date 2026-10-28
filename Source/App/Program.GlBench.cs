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
    private static readonly string[] BenchPassNames = { "", "buildings", "terrain", "props", "meshes", "foliage", "decals", "sky", "horde" };
    private static bool _passOn;
    private static readonly long[] PassCpu = new long[9];
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
        Console.WriteLine($"[glbench] compute self-check: {GlCompute.SelfCheck()}"); // the compute hook works (4.6 path) or is off (4.1)
        Console.WriteLine($"[glbench] {frames} frames a view and configuration, horde {horde} on Zombies maps; times are medians in ms");
        var configs = new List<string> { "all on" };
        if (RenderCaps.Gl46)
            configs.AddRange(RenderCaps.Experiments.Select(e => "no " + e.Name).Append("all off"));
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
                Gfx.BatchBoxes = Gfx.BatchDraws = 0;
                for (var f = -10 * nc; f < frames * nc && !_window.ShouldClose; f++) // 10 warm-up frames each (render data is built on the first)
                {
                    var c = (f % nc + nc) % nc;
                    foreach (var (e, _) in RenderCaps.Experiments)
                        RenderCaps.Force(e, !(configs[c] == "all off" || configs[c] == "no " + e));
                    Glfw.PollEvents();
                    Native.glFinish();
                    _passMarks = 0;
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
                    if (_passMarks == (1 << np) - 1 || (zombies == 0 && _passMarks == (1 << (np - 1)) - 1))
                    {
                        for (var p = 1; p < np && (_passMarks & (1 << p)) != 0; p++)
                        {
                            passCpu[c * np + p] += Stopwatch.GetElapsedTime(PassCpu[p - 1], PassCpu[p]).TotalMilliseconds;
                            if (_passGpu != null && _passGpu.Ready(p))
                                passGpu[c * np + p] += (_passGpu.Read(p) - (double)_passGpu.Read(p - 1)) / 1e6;
                        }
                        passN[c]++;
                    }
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
                        $"[glbench] {map.Name,-22} {view.Name,-8} {configs[c],-14} cpu {mc,7:0.000}  frame {mf,8:0.000}  gpu {(mg >= 0 ? mg.ToString("0.000", CultureInfo.InvariantCulture) : "n/a"),8}  ({n[c]} frames{(zombies > 0 ? $", {boxes} batched boxes a frame" : "")}{(pd > 0 ? $", props {pd:0.0} draw calls for {propRuns[c] / Math.Max(n[c], 1.0):0.0} runs" : "")})"));
                    var key = $"{map.Name}|{configs[c]}";
                    totals.TryGetValue(key, out var acc);
                    totals[key] = (acc.Cpu + mc, acc.Full + mf, acc.Gpu + Math.Max(mg, 0), acc.Views + 1);
                }
            }
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
        foreach (var (e, _) in RenderCaps.Experiments)
            RenderCaps.Force(e, null);
        foreach (var (key, t) in totals)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[glbench] summary {key.Replace('|', ' '),-40} cpu {t.Cpu / t.Views,7:0.000}  frame {t.Full / t.Views,8:0.000}  gpu {t.Gpu / t.Views,8:0.000}  (mean of {t.Views} views)"));
        Console.WriteLine($"[glbench] done{(shots ? $", {pictureFailures} picture(s) differ from all on" : "")}, GL error 0x{GlDebug.Error():X}");
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
        DrawArena();
        if (zombies > 0)
        {
            DrawBenchHorde(eye, target, zombies, (float)(frame / 60.0));
            BenchPass(8);
        }
        Gfx.SetFog(map.Sky, 0, 0);
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
