using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>What the performance key shows: nothing, the full window (a menu: the cursor is free), or the corner overlay.</summary>
public enum PerfView : byte
{
    Off,
    Window,
    Overlay,
}

/// <summary>
/// The performance window (F9 by default, rebindable; in every mode, the lobby and release builds): a frame-time graph
/// of the last ~10 s with 16.7 / 33.3 ms guides, min / avg / max / 1% low and the worst frames marked; a stacked CPU
/// graph of the frame's main areas; the server tick graph against its 8 ms budget; the CPU tree (<see cref="Perf"/>:
/// average, self and peak per area over the last second, sortable); the GPU passes (<see cref="PerfGpu"/>); and
/// counters (draw calls, triangles, instances, entities, GC, memory, network, ping, snapshots). Pause freezes the
/// graphs; Copy puts a text snapshot on the clipboard, Log writes it to the log; "Corner overlay" swaps to a small
/// overlay that leaves the mouse to the game. F9 cycles window, overlay, off; Esc closes the window. Drawn with ImGui's
/// own draw list (no ImPlot), allocation-free per frame; closed, it costs one check and the scopes are off.
/// </summary>
public sealed partial class Ui
{
    public PerfView PerfMode;

    /// <summary>The full perf window is up (it counts as a menu for the cursor).</summary>
    public bool PerfWindowOpen => PerfMode == PerfView.Window;

    private readonly PerfStats _perfStats = new();
    private double _perfStatsAt = -10;
    private int _perfSort; // 0: tree, 1: by average, 2: by peak
    private readonly int[] _perfOrder = new int[Perf.Count];
    private readonly float[] _perfKeys = new float[Perf.Count];
    private string _perfMsg = "";
    private double _perfMsgAt = -10;
    private bool _perfWasOn;
    private int _perfBind = int.MaxValue;
    private string _perfKeyHint = "";

    // Network and memory figures, sampled once a second.
    private double _perfNetAt = -10;
    private long _perfIn0, _perfOut0, _perfSnapB0, _perfSnapN0;
    private float _perfIn, _perfOut, _perfSnapSize, _perfSnapRate;
    private long _perfWorkingSet;
    private object? _perfNetOf;

    // The roots' colours in the stacked graph and its legend: events, input, game, audio, render, ui, swap, loading, limiter.
    private static readonly (byte R, byte G, byte B)[] PerfRootRgb =
        [(120, 130, 150), (90, 170, 230), (240, 150, 60), (170, 120, 230), (90, 200, 120), (230, 210, 90), (110, 110, 120), (230, 80, 80), (70, 90, 110)];

    private static readonly ImColor8[] PerfRootColours = PerfRootRgb.Select(c => new ImColor8(c.R, c.G, c.B, 255)).ToArray();
    private static readonly float4[] PerfRootText = PerfRootRgb.Select(c => new float4(c.R / 255f, c.G / 255f, c.B / 255f, 1f)).ToArray();

    private static readonly PerfArea[] PerfRoots = [PerfArea.Events, PerfArea.Input, PerfArea.Game, PerfArea.Audio, PerfArea.Render, PerfArea.Ui, PerfArea.Swap, PerfArea.Loading, PerfArea.Limiter];

    /// <summary>
    /// Draws the window or the overlay (call every frame: it also turns the timing on and off with the view).
    /// <paramref name="game"/>: the session, for its counters (null in the lobby).
    /// </summary>
    public void DrawPerf(Game? game, double now, int bind)
    {
        var on = PerfMode != PerfView.Off;
        if (on != _perfWasOn)
        {
            Perf.SetOn(on);
            Perf.Paused = false;
            _perfWasOn = on;
            _perfStatsAt = -10;
            _perfNetAt = -10;
        }
        if (!on)
            return; // closed: one check a frame, the scopes are off
        if (now - _perfStatsAt >= 0.25 || _perfStatsAt > now)
        {
            _perfStatsAt = now;
            _perfStats.Update();
        }
        SamplePerfNet(game, now);
        _perfOverlayGame = game;
        if (PerfMode == PerfView.Overlay)
            DrawPerfOverlay();
        else
            DrawPerfWindow(game, now, bind);
    }

    private void SamplePerfNet(Game? game, double now)
    {
        if (now - _perfNetAt < 1 && _perfNetAt <= now && ReferenceEquals(_perfNetOf, game))
            return;
        var dt = now - _perfNetAt;
        var fresh = !ReferenceEquals(_perfNetOf, game) || dt > 5 || dt <= 0;
        _perfNetAt = now;
        _perfNetOf = game;
        _perfWorkingSet = Environment.WorkingSet; // (once a second)
        if (game == null)
        {
            _perfIn = _perfOut = _perfSnapSize = _perfSnapRate = 0;
            return;
        }
        long inB = game.Net.BytesReceived, outB = game.Net.BytesSent, sb = game.SnapBytes, sn = game.SnapPackets;
        if (!fresh)
        {
            _perfIn = (float)((inB - _perfIn0) / dt);
            _perfOut = (float)((outB - _perfOut0) / dt);
            var parts = sn - _perfSnapN0;
            _perfSnapRate = (float)(parts / dt);
            _perfSnapSize = parts > 0 ? (float)(sb - _perfSnapB0) / parts : 0;
        }
        (_perfIn0, _perfOut0, _perfSnapB0, _perfSnapN0) = (inB, outB, sb, sn);
    }

    private void PerfMessage(string text, double now)
    {
        _perfMsg = text;
        _perfMsgAt = now;
    }

    private void DrawPerfWindow(Game? game, double now, int bind)
    {
        var s = _perfStats;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + 24 * _scale, vp.WorkPos.Y + 40 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(MathF.Min(760 * _scale, vp.WorkSize.X * 0.9f), MathF.Min(860 * _scale, vp.WorkSize.Y * 0.9f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.94f);
        var open = true;
        if (ImGui.Begin("Performance###perfwindow"u8, ref open))
        {
            ImGui.Checkbox("Pause"u8, ref Perf.Paused);
            ImGui.SameLine();
            if (ImGui.Button("Copy snapshot"u8))
            {
                try
                {
                    SafeClipboard.Sink(s.Report(Perf.Gpu, PerfExtra(game)));
                    PerfMessage("copied a text snapshot to the clipboard", now);
                }
                catch (Exception ex)
                {
                    PerfMessage($"copy failed: {ex.Message}", now);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Write to log"u8))
            {
                foreach (var line in s.Report(Perf.Gpu, PerfExtra(game)).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Console.WriteLine("[perf] " + line);
                PerfMessage("wrote a snapshot to the log", now);
            }
            ImGui.SameLine();
            if (ImGui.Button("Corner overlay"u8))
                PerfMode = PerfView.Overlay;
            ImGui.SameLine();
            if (bind != _perfBind)
                (_perfBind, _perfKeyHint) = (bind, $"{Keybinds.Name(bind)}: window, overlay, off"); // (made once: the key's name allocates)
            ImGuiSafe.TextColored(Dim, _perfKeyHint);
            if (now - _perfMsgAt < 4)
                ImGuiSafe.TextColored(Green, _perfMsg);

            // ---- Frame time ----
            ImGui.Text($"{s.Fps:0} fps   avg {s.AvgMs:0.00} ms   min {s.MinMs:0.00}   max {s.MaxMs:0.00}   1% low {s.Low1Ms:0.00} ms ({s.Low1Fps:0} fps)   {s.Frames} frames");
            if (FrameLimiter.ActiveFps > 0)
                ImGuiSafe.TextColored(PerfCapText, $"Max FPS cap {FrameLimiter.ActiveFps} ({1000f / FrameLimiter.ActiveFps:0.0} ms, the blue line): the limiter waits {s.AreaAvg[(int)PerfArea.Limiter]:0.00} ms a frame on average (not part of Swap)");
            if (s.WorstBack[0] >= 0)
                ImGuiSafe.TextColored(Dim, $"worst: {s.WorstMs[0]:0.0} ms {s.WorstBack[0] / 60f:0.0} s ago, {s.WorstMs[1]:0.0} ms, {s.WorstMs[2]:0.0} ms (red marks)");
            var scale = s.MaxMs > 66 ? 100f : s.MaxMs > 36 ? 70f : 40f;
            var w = ImGui.GetContentRegionAvail().X;
            DrawFrameGraph(new float2(w, 110 * _scale), scale, true);
            ImGuiSafe.TextColored(Dim, $"CPU by area per frame (same scale; the gap to the white frame line is time outside every scope)");
            DrawCpuStack(new float2(w, 90 * _scale), scale);
            DrawPerfLegend();
            if (s.TicksPerSecond > 0)
            {
                ImGui.Text($"Server ticks: {s.TicksPerSecond:0}/s, avg {s.TickAvgMs:0.000} ms, worst {s.TickWorstMs:0.000} ms of the 8 ms budget");
                DrawTickGraph(new float2(w, 60 * _scale));
            }

            // ---- CPU tree ----
            if (ImGui.CollapsingHeader("CPU (main thread, ms per frame over the last second)"u8, ImGuiTreeNodeFlags.DefaultOpen))
                DrawCpuTable(s);
            if (ImGui.CollapsingHeader("GPU passes"u8, ImGuiTreeNodeFlags.DefaultOpen))
                DrawGpuTable(s);
            if (ImGui.CollapsingHeader("Counters"u8, ImGuiTreeNodeFlags.DefaultOpen))
                DrawPerfCounters(game, s);
            if (Perf.Misnested > 0)
                ImGuiSafe.TextColored(Red, $"{Perf.Misnested} scope(s) ended out of order (a bug in the timing code)");
        }
        ImGui.End();
        if (!open && PerfMode == PerfView.Window)
            PerfMode = PerfView.Off;
    }

    private void DrawFrameGraph(float2 size, float scale, bool marks)
    {
        var dl = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + size;
        dl.AddRectFilled(p0, p1, new ImColor8(18, 20, 24, 230));
        var n = Perf.Filled;
        var col = size.X / Perf.History;
        for (var b = 0; b < n; b++)
        {
            var f = Perf.FrameMs(b);
            if (f <= 0)
                continue;
            var x = p1.X - (b + 1) * col;
            var h = MathF.Min(f / scale, 1f) * size.Y;
            var c = f <= 16.7f ? new ImColor8(80, 200, 110, 255) : f <= 33.3f ? new ImColor8(230, 200, 70, 255) : new ImColor8(235, 80, 70, 255);
            dl.AddRectFilled(new float2(x, p1.Y - h), new float2(x + MathF.Max(col, 1f), p1.Y), c);
        }
        GuideLine(dl, p0, p1, 16.7f, scale, new ImColor8(140, 220, 140, 160));
        GuideLine(dl, p0, p1, 33.3f, scale, new ImColor8(230, 140, 90, 160));
        CapLine(dl, p0, p1, scale);
        if (marks)
        {
            var s = _perfStats;
            for (var k = 0; k < PerfStats.Worst; k++)
            {
                if (s.WorstBack[k] < 0)
                    continue;
                var x = p1.X - (s.WorstBack[k] + 0.5f) * col;
                dl.AddRectFilled(new float2(x - 2 * _scale, p0.Y), new float2(x + 2 * _scale, p0.Y + 6 * _scale), new ImColor8(255, 60, 60, 255));
            }
        }
        dl.AddText(p0 + new float2(4, 2), new ImColor8(200, 200, 210, 200), $"{scale:0} ms, 10 s");
        ImGui.Dummy(size);
    }

    private void GuideLine(ImDrawListPtr dl, float2 p0, float2 p1, float ms, float scale, ImColor8 c)
    {
        if (ms > scale)
            return;
        var y = p1.Y - ms / scale * (p1.Y - p0.Y);
        dl.AddLine(new float2(p0.X, y), new float2(p1.X, y), c, 1f);
        var at = new float2(p0.X + 4 * _scale, y - 14 * _scale); // (at the old end of the graph, left, where the bars are oldest)
        dl.AddText(at + new float2(1, 1), new ImColor8(0, 0, 0, 220), $"{ms:0.0} ms");
        dl.AddText(at, c, $"{ms:0.0} ms");
    }

    private static readonly float4 PerfCapText = new(0.45f, 0.75f, 1f, 1f);

    /// <summary>The Max FPS cap's frame time as a blue guide line, labelled at the new (right) end of the graph.</summary>
    private void CapLine(ImDrawListPtr dl, float2 p0, float2 p1, float scale)
    {
        var fps = FrameLimiter.ActiveFps;
        if (fps <= 0)
            return;
        var ms = 1000f / fps;
        if (ms > scale)
            return;
        var c = new ImColor8(110, 180, 255, 220);
        var y = p1.Y - ms / scale * (p1.Y - p0.Y);
        dl.AddLine(new float2(p0.X, y), new float2(p1.X, y), c, 1.5f);
        var at = new float2(p1.X - 90 * _scale, y - 14 * _scale);
        dl.AddText(at + new float2(1, 1), new ImColor8(0, 0, 0, 220), $"cap {fps} fps");
        dl.AddText(at, c, $"cap {fps} fps");
    }

    private void DrawCpuStack(float2 size, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + size;
        dl.AddRectFilled(p0, p1, new ImColor8(18, 20, 24, 230));
        var n = Perf.Filled;
        var col = size.X / Perf.History;
        var wcol = MathF.Max(col, 1f);
        for (var b = 0; b < n; b++)
        {
            var x = p1.X - (b + 1) * col;
            var y = p1.Y;
            for (var r = 0; r < PerfRoots.Length; r++)
            {
                var v = Perf.AreaMs(b, PerfRoots[r]);
                if (v <= 0)
                    continue;
                var h = v / scale * size.Y;
                var top = MathF.Max(p0.Y, y - h);
                dl.AddRectFilled(new float2(x, top), new float2(x + wcol, y), PerfRootColours[r]);
                y = top;
                if (y <= p0.Y)
                    break;
            }
            var f = Perf.FrameMs(b);
            if (f > 0)
            {
                var fy = p1.Y - MathF.Min(f / scale, 1f) * size.Y;
                dl.AddRectFilled(new float2(x, fy), new float2(x + wcol, fy + 1), new ImColor8(235, 235, 240, 200));
            }
        }
        GuideLine(dl, p0, p1, 16.7f, scale, new ImColor8(140, 220, 140, 160));
        CapLine(dl, p0, p1, scale);
        ImGui.Dummy(size);
    }

    private void DrawPerfLegend()
    {
        var s = _perfStats;
        for (var r = 0; r < PerfRoots.Length; r++)
        {
            if (r > 0)
                ImGui.SameLine();
            ImGuiSafe.TextColored(PerfRootText[r], $"{Perf.Names[(int)PerfRoots[r]]} {s.AreaAvg[(int)PerfRoots[r]]:0.00}");
        }
    }

    private void DrawTickGraph(float2 size)
    {
        const float scale = 16f;
        var dl = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + size;
        dl.AddRectFilled(p0, p1, new ImColor8(18, 20, 24, 230));
        var n = Perf.Filled;
        var col = size.X / Perf.History;
        for (var b = 0; b < n; b++)
        {
            var t = Perf.TickWorstMs(b);
            if (t <= 0)
                continue;
            var x = p1.X - (b + 1) * col;
            var h = MathF.Min(t / scale, 1f) * size.Y;
            dl.AddRectFilled(new float2(x, p1.Y - h), new float2(x + MathF.Max(col, 1f), p1.Y), t > 8f ? new ImColor8(235, 80, 70, 255) : new ImColor8(240, 150, 60, 255));
        }
        GuideLine(dl, p0, p1, 8f, scale, new ImColor8(235, 90, 80, 200));
        dl.AddText(p0 + new float2(4, 2), new ImColor8(200, 200, 210, 200), "worst tick per frame, 16 ms"u8);
        ImGui.Dummy(size);
    }

    private void DrawCpuTable(PerfStats s)
    {
        ImGui.RadioButton("Tree"u8, ref _perfSort, 0);
        ImGui.SameLine();
        ImGui.RadioButton("By average"u8, ref _perfSort, 1);
        ImGui.SameLine();
        ImGui.RadioButton("By peak"u8, ref _perfSort, 2);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"music thread {s.MusicMsPerSecond:0.0} ms/s");
        for (var a = 0; a < Perf.Count; a++)
        {
            _perfOrder[a] = a;
            _perfKeys[a] = _perfSort == 1 ? -s.AreaSelf[a] : _perfSort == 2 ? -s.AreaMax[a] : a;
        }
        if (_perfSort != 0)
            Array.Sort(_perfKeys, _perfOrder);
        var frame = MathF.Max(s.AvgMs, 0.001f);
        if (!ImGui.BeginTable("##perfcpu"u8, 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            return;
        ImGui.TableSetupColumn("Area"u8, ImGuiTableColumnFlags.WidthStretch, 0, default);
        ImGui.TableSetupColumn("avg"u8, ImGuiTableColumnFlags.WidthFixed, 60 * _scale, default);
        ImGui.TableSetupColumn("self"u8, ImGuiTableColumnFlags.WidthFixed, 60 * _scale, default);
        ImGui.TableSetupColumn("peak"u8, ImGuiTableColumnFlags.WidthFixed, 60 * _scale, default);
        ImGui.TableSetupColumn("share of frame (self)"u8, ImGuiTableColumnFlags.WidthFixed, 150 * _scale, default);
        ImGui.TableHeadersRow();
        for (var i = 0; i < Perf.Count; i++)
        {
            var a = _perfOrder[i];
            if (s.AreaMax[a] <= 0 && Perf.Parent[a] >= 0)
                continue; // a child area that didn't run: hidden
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var indent = _perfSort == 0 ? Perf.Depth[a] * 14 * _scale : 0;
            if (indent > 0)
                ImGui.Indent(indent);
            if (s.AreaMax[a] <= 0)
                ImGuiSafe.TextColored(Dim, Perf.Names[a]);
            else
                ImGui.Text(Perf.Names[a]);
            if (indent > 0)
                ImGui.Unindent(indent);
            ImGui.TableNextColumn();
            ImGui.Text($"{s.AreaAvg[a]:0.000}");
            ImGui.TableNextColumn();
            ImGui.Text($"{s.AreaSelf[a]:0.000}");
            ImGui.TableNextColumn();
            if (a == (int)PerfArea.ServerTick && s.TickWorstMs > 8f)
                ImGuiSafe.TextColored(Red, $"{s.AreaMax[a]:0.000}");
            else
                ImGui.Text($"{s.AreaMax[a]:0.000}");
            ImGui.TableNextColumn();
            var bar = new float2(-1, ImGui.GetTextLineHeight());
            ImGui.ProgressBar(Math.Clamp(s.AreaSelf[a] / frame, 0f, 1f), bar, ImString.Empty);
        }
        ImGui.EndTable();
    }

    private void DrawGpuTable(PerfStats s)
    {
        var gpu = Perf.Gpu;
        if (gpu == null || !gpu.Tried)
        {
            ImGuiSafe.TextColored(Dim, "GPU timer: starting");
            return;
        }
        if (!gpu.Available)
        {
            ImGuiSafe.TextColored(Dim, "GPU timer: unavailable on this driver (no timestamp queries)");
            return;
        }
        var total = MathF.Max(gpu.TotalAvgMs(), 0.001f);
        ImGui.Text($"GPU frame {gpu.TotalAvgMs():0.000} ms in the passes (smoothed; read back a few frames late, never waited for)");
        ImGuiSafe.TextColored(Dim, $"+ {gpu.GapAvgMs:0.000} ms between passes (the GPU waiting for the CPU, or untimed work; billed to no pass)");
        if (gpu.OutOfOrder > 0)
            ImGuiSafe.TextColored(Red, $"{gpu.OutOfOrder} pass mark(s) out of order (skipped)");
        if (!ImGui.BeginTable("##perfgpu"u8, 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            return;
        ImGui.TableSetupColumn("Pass"u8, ImGuiTableColumnFlags.WidthStretch, 0, default);
        ImGui.TableSetupColumn("ms"u8, ImGuiTableColumnFlags.WidthFixed, 60 * _scale, default);
        ImGui.TableSetupColumn("share"u8, ImGuiTableColumnFlags.WidthFixed, 150 * _scale, default);
        ImGui.TableHeadersRow();
        for (var p = 0; p < PerfGpu.Count; p++)
        {
            if (gpu.Samples[p] == 0)
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(PerfGpu.Names[p]);
            ImGui.TableNextColumn();
            ImGui.Text($"{gpu.AvgMs[p]:0.000}");
            ImGui.TableNextColumn();
            ImGui.ProgressBar(Math.Clamp(gpu.AvgMs[p] / total, 0f, 1f), new float2(-1, ImGui.GetTextLineHeight()), ImString.Empty);
        }
        ImGui.EndTable();
    }

    private void DrawPerfCounters(Game? game, PerfStats s)
    {
        ImGui.Text($"Draw calls {s.Draws:0}   triangles {s.Triangles / 1000f:0.0}k   instances {s.Instances:0}   (per frame, the 3D renderers; ImGui not counted)");
        ImGui.Text($"Allocated per frame: {s.AllocBytes:0} B main thread, {s.AllocAllBytes:0} B all threads");
        ImGui.Text($"GC: frames with a collection in the graph: gen0 {s.Gc0Frames}, gen1 {s.Gc1Frames}, gen2 {s.Gc2Frames}; since opened {PerfCounters.Gen0}/{PerfCounters.Gen1}/{PerfCounters.Gen2}");
        ImGui.Text($"Memory: managed heap {PerfCounters.HeapBytes / 1048576f:0.0} MB, working set {_perfWorkingSet / 1048576f:0} MB");
        if (game == null)
        {
            ImGuiSafe.TextColored(Dim, "Not in a session: no entities or network.");
            return;
        }
        CountEntities(game, out var people, out var bots, out var walk, out var run, out var sprint, out var soldiers);
        var map = World.Map;
        ImGui.Text($"Players {people}, bots {bots}; zombies {walk + run + sprint} (walk {walk}, run {run}, sprint {sprint}); co-op soldiers {soldiers}");
        if (game.CrowdOn)
            DrawPerfCrowd(game, walk + run + sprint);
        ImGui.Text($"Props {map.PropInstances.Count} ({map.BrokenProps.Count} broken), decals {map.DecalRequests.Count}, ragdolls {Ragdolls.LiveCount}, projectiles {game.Projectiles.Count}");
        ImGui.Text($"Particles: tracers {game.Tracers.Count}, blasts {game.Blasts.Count}, smoke clouds {game.Smokes.Count}");
        if (game.Net.IsOffline)
            ImGuiSafe.TextColored(Dim, "Offline: no network traffic.");
        else
        {
            if (game.IsServer)
                ImGui.Text($"Net: in {_perfIn / 1024f:0.0} KB/s, out {_perfOut / 1024f:0.0} KB/s; snapshots sent {_perfSnapRate:0}/s, avg {_perfSnapSize:0} B");
            else
                ImGui.Text($"Net: in {_perfIn / 1024f:0.0} KB/s, out {_perfOut / 1024f:0.0} KB/s; snapshots in {_perfSnapRate:0}/s, avg {_perfSnapSize:0} B; ping {game.Net.PingTo(game.Net.ServerGuid)} ms");
        }
    }

    private static void CountEntities(Game game, out int people, out int bots, out int walk, out int run, out int sprint, out int soldiers)
    {
        people = bots = walk = run = sprint = 0;
        foreach (var p in game.Players.Values)
            if (p.Bot != null)
                bots++;
            else
                people++;
        foreach (var z in game.Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            switch (z.Speed)
            {
                case ZSpeed.Walk:
                    walk++;
                    break;
                case ZSpeed.Run:
                    run++;
                    break;
                default:
                    sprint++;
                    break;
            }
        }
        soldiers = game.AliveEnemies;
    }

    /// <summary>The game's counters for a text snapshot (allocates: only on Copy / Log).</summary>
    private string PerfExtra(Game? game)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var map = World.Map;
        var sb = new System.Text.StringBuilder();
        sb.Append(inv, $"map {map.Name}, memory: working set {_perfWorkingSet / 1048576.0:0} MB\n");
        if (game == null)
            return sb.Append("not in a session\n").ToString();
        CountEntities(game, out var people, out var bots, out var walk, out var run, out var sprint, out var soldiers);
        sb.Append(inv, $"mode {game.Mode}, {(game.IsServer ? game.Net.IsOffline ? "offline" : "host" : "client")}; players {people}, bots {bots}, zombies {walk + run + sprint} (walk {walk}, run {run}, sprint {sprint}), co-op soldiers {soldiers}\n");
        sb.Append(inv, $"props {map.PropInstances.Count} ({map.BrokenProps.Count} broken), decals {map.DecalRequests.Count}, ragdolls {Ragdolls.LiveCount}, projectiles {game.Projectiles.Count}, tracers {game.Tracers.Count}, blasts {game.Blasts.Count}, smokes {game.Smokes.Count}\n");
        if (!game.Net.IsOffline)
            sb.Append(inv, $"net in {_perfIn / 1024:0.0} KB/s, out {_perfOut / 1024:0.0} KB/s, snapshots {_perfSnapRate:0}/s avg {_perfSnapSize:0} B{(game.IsServer ? "" : $", ping {game.Net.PingTo(game.Net.ServerGuid)} ms")}\n");
        return sb.ToString();
    }

    /// <summary>The session the overlay was last drawn for (its crowd line; set by <see cref="DrawPerf"/>).</summary>
    private Game? _perfOverlayGame;

    /// <summary>A mass horde map's tiers (Source/Game/ZombiesCrowd*.cs): the counts, the crowd's tick, its bytes, what is drawn.</summary>
    private static void DrawPerfCrowd(Game game, int individuals)
    {
        var c = game.Crowd;
        if (game.IsServer)
        {
            ImGui.Text($"Horde tiers: {c.Total + individuals} live = individuals {individuals}, crowd particles {c.Count} ({c.Near} near, held or promoting), mass {c.MassTotal}; killed {game.CrowdRoundKills} of {game.CrowdRoundTotal}");
            ImGui.Text($"Crowd tick {game.CrowdTickAvgMs:0.00} ms avg, {game.CrowdTickWorstMs:0.00} worst (last second); flow field worst step {c.FlowWorstMs:0.00} ms; crowd net {game.CrowdBytesPerClient / 1024f:0.0} KB/s per client ({game.CrowdClients} clients)");
        }
        else
            ImGui.Text($"Horde tiers: individuals {individuals}, crowd {game.CrowdSeen.Total} (the server's picture, {game.CrowdSeen.Cols} x {game.CrowdSeen.Rows} cells)");
        ImGui.Text($"Crowd drawn: {Program.CrowdDrawnNear}/{Program.CrowdDrawnMid}/{Program.CrowdDrawnFar} near/mid/far instances, {Program.CrowdDrawnCulled} culled, prep {Program.CrowdPrepMs:0.00} ms");
        if (game.StressOn)
        {
            ImGui.Text($"Horde stress: {StressPhaseNames[(int)game.StressState]} {game.StressPhaseSeconds:0} s, wave {game.StressWave} of {game.StressWaveSize}, {game.StressNearShare * 100:0}% in the blob at the rally point");
            if (game.StressMergeSeconds >= 0)
            {
                ImGui.SameLine();
                ImGui.Text($"(merged after {game.StressMergeSeconds:0} s)");
            }
        }
    }

    private static readonly string[] StressPhaseNames = ["off", "staging", "descending", "advancing"];

    private static void DrawPerfCrowdLine(Game game)
    {
        var c = game.Crowd;
        if (game.IsServer)
            ImGui.Text($"horde {c.Total + game.StressIndividuals} (ind {game.StressIndividuals} crowd {c.Count} mass {c.MassTotal})  crowd {game.CrowdTickAvgMs:0.00} ms  {game.CrowdBytesPerClient / 1024f:0.0} KB/s/client");
        else
            ImGui.Text($"horde: crowd {game.CrowdSeen.Total}  drawn {Program.CrowdDrawnNear + Program.CrowdDrawnMid + Program.CrowdDrawnFar}");
    }

    private void DrawPerfOverlay()
    {
        var s = _perfStats;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + 10 * _scale, vp.WorkPos.Y + 96 * _scale), ImGuiCond.Always); // (under the HUD's top-left lines)
        ImGui.SetNextWindowBgAlpha(0.55f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin("##perfoverlay"u8, flags))
        {
            ImGui.Text($"{s.Fps:0} fps  avg {s.AvgMs:0.0}  max {s.MaxMs:0.0}  1% low {s.Low1Ms:0.0} ms");
            DrawFrameGraph(new float2(240 * _scale, 44 * _scale), s.MaxMs > 36 ? 70f : 40f, false);
            // The three busiest roots (by average).
            int a1 = -1, a2 = -1, a3 = -1;
            for (var r = 0; r < PerfRoots.Length; r++)
            {
                var a = (int)PerfRoots[r];
                var v = s.AreaAvg[a];
                if (a1 < 0 || v > s.AreaAvg[a1])
                    (a1, a2, a3) = (a, a1, a2);
                else if (a2 < 0 || v > s.AreaAvg[a2])
                    (a2, a3) = (a, a2);
                else if (a3 < 0 || v > s.AreaAvg[a3])
                    a3 = a;
            }
            ImGui.Text($"{Perf.Names[a1]} {s.AreaAvg[a1]:0.00}  {Perf.Names[a2]} {s.AreaAvg[a2]:0.00}  {Perf.Names[a3]} {s.AreaAvg[a3]:0.00}");
            var gpu = Perf.Gpu is { Available: true } g ? g.TotalAvgMs() : -1f;
            if (s.TicksPerSecond > 0)
                ImGui.Text($"GPU {gpu:0.00} ms  tick {s.TickAvgMs:0.00} (worst {s.TickWorstMs:0.0}/8)  draws {s.Draws:0}");
            else
                ImGui.Text($"GPU {gpu:0.00} ms  draws {s.Draws:0}  alloc {s.AllocBytes:0} B/frame");
            if (_perfOverlayGame is { CrowdOn: true } cg)
                DrawPerfCrowdLine(cg);
        }
        ImGui.End();
    }
}
