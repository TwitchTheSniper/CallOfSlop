using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "UI frame spikes" self-test section (2026-10-29, perf/imgui-spike): ImGui frames with the match HUD and the F9
/// performance window up, run headless (a real ImGui context, its font atlas handed back as if uploaded), each frame
/// timed on the thread's CPU clock. No frame may cost much more than the median: something done every N frames (the
/// window's statistics, its once-a-second counters, a font size baked again, a list rebuilt) shows up as a spike, as the
/// developer's F9 snapshot did (UI 0.41 ms average, 4.7 ms peak). Frames that JIT compiled or collected garbage are
/// left out (warm-up, and allocations are the allocation probes' job). Timing is enforced on Windows only, like the
/// other CPU checks (SelfTestTiming.cs); elsewhere the figures are logged. Also: the UI's Perf sub-areas sit under
/// "UI (ImGui)".
/// </summary>
public static partial class SelfTest
{
    private static void CheckUiFrameSpikes()
    {
        // The UI's parts are children of the UI area, in the tree the F9 window shows.
        for (var a = PerfArea.UiNewFrame; a <= PerfArea.UiDraw; a++)
            if (Perf.Parent[(int)a] != (int)PerfArea.Ui)
                Fail($"ui frames: perf area {Perf.Names[(int)a]} isn't under {Perf.Names[(int)PerfArea.Ui]}");

        var game = ModeGame(0, GameMode.FreeForAll);
        var now = 1000.0;
        Run(game, ref now, 2);
        var settings = new Settings();
        var ui = new Ui { MenuOpen = false };
        var ctx = ImGui.CreateContext();
        var textures = 0;
        try
        {
            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
            io.DisplaySize = new float2(1920, 1080);
            io.DisplayFramebufferScale = new float2(1, 1);
            io.DeltaTime = 1f / 600;
            Perf.ResetForTest();
            ui.PerfMode = PerfView.Window;

            const int warm = 240, frames = 1200;
            var ms = new List<double>(frames);
            int jitFrames = 0, gcFrames = 0;
            for (var f = 0; f < warm + frames; f++)
            {
                now += 1.0 / 600; // a 600 fps frame: the window's stats update every 150 frames, its counters every 600
                if (f % 10 == 0)
                    game.Update(now, 1.0 / 60); // the match moves on (the HUD's numbers change)
                var jit0 = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
                var gc0 = GC.CollectionCount(0);
                var t0 = ThreadCpu.NowMs();
                using (Perf.Measure(PerfArea.Ui))
                {
                    ImGui.NewFrame();
                    ui.DrawHud(game, now, "", settings);
                    ui.DrawPerf(game, now, Keybinds.Key(Brutal.GlfwApi.GlfwKey.F9));
                    ImGui.Render();
                    textures += FakeTextureUploads(ImGui.GetDrawData());
                    ImString.SharedStorage.Reset();
                }
                var cpu = ThreadCpu.NowMs() - t0;
                Perf.EndFrame(1000f / 600);
                if (f < warm)
                    continue;
                if (System.Runtime.JitInfo.GetCompilationTime(currentThread: true) != jit0)
                    jitFrames++;
                else if (GC.CollectionCount(0) != gc0)
                    gcFrames++;
                else
                    ms.Add(cpu);
            }
            ms.Sort();
            if (ms.Count < frames / 2)
            {
                Fail($"ui frames: only {ms.Count} of {frames} frames without JIT or GC ({jitFrames} JIT, {gcFrames} GC)");
                return;
            }
            var median = ms[ms.Count / 2];
            var p99 = ms[(int)(ms.Count * 0.99)];
            var worst = ms[^1];
            // A spike: over four times the median and over half a millisecond more (a busy machine's odd slow frame on
            // a 0.1 ms median is not one). The worst of 1200 frames, against the window's own 4.7 ms on a 0.4 ms average.
            var limit = Math.Max(median * 4, median + 0.5);
            Line($"UI frames ({ThreadCpu.Kind}, HUD + F9 window, {ms.Count} frames, {jitFrames} JIT and {gcFrames} GC frames left out): "
                 + $"median {median:0.000} ms, p99 {p99:0.000}, worst {worst:0.000} (limit {limit:0.000}); font atlas uploads {textures}");
            if (worst > limit && TickCpu.Available)
                Fail($"ui frames: the worst UI frame took {worst:0.000} ms of CPU against a {median:0.000} ms median (limit {limit:0.000}): something runs every few frames");
        }
        finally
        {
            ui.PerfMode = PerfView.Off;
            ImGui.DestroyContext(ctx);
            Perf.SetOn(false);
            Perf.ResetForTest();
        }
    }

    /// <summary>A headless renderer's half of ImGui 1.92's texture protocol: every texture made, updated or freed at once. Returns how many it saw.</summary>
    private static int FakeTextureUploads(ImDrawDataPtr dd)
    {
        if (dd.Textures.IsNull())
            return 0;
        var n = 0;
        ref var texs = ref dd.Textures.Value;
        for (var i = 0; i < texs.Count; i++)
        {
            var t = texs[i];
            switch (t.Status)
            {
                case ImTextureStatus.WantCreate:
                    t.SetTexID(new ImTextureID(1000 + i));
                    t.SetStatus(ImTextureStatus.OK);
                    n++;
                    break;
                case ImTextureStatus.WantUpdates:
                    t.SetStatus(ImTextureStatus.OK);
                    n++;
                    break;
                case ImTextureStatus.WantDestroy:
                    t.SetTexID(ImTextureID.Invalid);
                    t.SetStatus(ImTextureStatus.Destroyed);
                    break;
            }
        }
        return n;
    }
}
