using System.Globalization;
using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The performance window's recording (F9): <b>Start recording</b> keeps every frame from now on (the benchmark's
/// recorder, <see cref="PerfBench"/>, with room for <see cref="CaptureFrames"/> frames) and swaps to the corner overlay, so
/// the player can go and do the thing they want measured; the perf key (F9) then stops it and opens the window again
/// (<b>Stop recording</b> there does the same). The report covers exactly the recorded frames: frame time percentiles,
/// the worst frames with when they came and what was slow in them, a second-by-second timeline, the CPU areas' mean and
/// peak, the GPU passes, the ticks (optionally traced by section past a threshold), allocations, and which constructs
/// stayed awake and why (<see cref="PerfCaptureBodies"/>). It is copied to the clipboard and written to
/// <c>perf-recordings/</c> beside the log. Nothing here allocates per frame while recording (four body samples a second
/// reuse one dictionary); the report allocates once, when the recording stops.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Most frames one recording keeps (about two minutes at 300 fps); it stops itself when full.</summary>
    public const int CaptureFrames = 36000;

    private const string CaptureLabel = "F9 recording";

    private bool _capOn;
    private double _capStart, _capNow;
    private bool _capTrace;
    private float _capTraceMs = 4f;
    private string? _capReport, _capPath;
    private readonly PerfCaptureBodies _capBodies = new();
    private int _capBind = int.MaxValue;
    private string _capKey = "";

    /// <summary>The F9 window's recording is running (the perf key stops it: Program.Perf.cs).</summary>
    public bool CaptureRecording => _capOn;

    /// <summary>Set by the perf key while recording: the recording stops in the next <see cref="DrawPerf"/>.</summary>
    public bool CaptureStopAsked;

    /// <summary>Every frame, before the view's own drawing: the body samples, and a stop that was asked for or a full buffer.</summary>
    private void PerfCaptureFrame(Game? game, double now, int bind)
    {
        if (bind != _capBind)
            (_capBind, _capKey) = (bind, Keybinds.Name(bind)); // (made once: the key's name allocates)
        if (!_capOn)
        {
            CaptureStopAsked = false;
            return;
        }
        _capNow = now;
        _capBodies.Frame(game, now);
        if (CaptureStopAsked || PerfBench.Full || !PerfBench.Recording)
            StopCapture(game, now);
    }

    /// <summary>Starts the recording (the window's button, and the window tool's `rec start`).</summary>
    public void StartCapture(Game? game, double now)
    {
        PerfBench.TraceFrame = false;
        PerfBench.TraceTicksMs = _capTrace ? Math.Clamp(_capTraceMs, 0.5f, 50f) : 0;
        PerfBench.Begin(CaptureLabel, CaptureFrames);
        _capBodies.Begin(game, now);
        _capOn = true;
        _capStart = _capNow = now;
        CaptureStopAsked = false;
        PerfMode = PerfView.Overlay; // (the cursor goes back to the game: go and do the thing; the perf key stops)
    }

    private void StopCapture(Game? game, double now)
    {
        _capOn = false;
        CaptureStopAsked = false;
        var full = PerfBench.Full;
        var spot = PerfBench.End(keepOn: PerfMode != PerfView.Off);
        PerfBench.Spots.Remove(spot); // (a benchmark run's list is for its spots)
        PerfBench.TraceTicksMs = 0;
        try
        {
            _capReport = CaptureReport(spot, game, now - _capStart, full);
        }
        catch (Exception ex)
        {
            _capReport = null;
            Console.WriteLine($"[perf] the recording's report failed: {ex}");
            PerfMessage($"the recording's report failed: {ex.Message}", now);
            return;
        }
        _capPath = null;
        var said = new StringBuilder();
        try
        {
            var dir = Path.Combine(AppFolders.Logs, "perf-recordings");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"recording-{PerfBench.Stamp()}.txt");
            File.WriteAllText(path, _capReport);
            _capPath = path;
            Console.WriteLine($"[perf] recording of {spot.Frames} frames written to {path}");
            said.Append("saved to ").Append(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[perf] the recording could not be written: {ex.Message}");
            said.Append("not saved: ").Append(ex.Message);
        }
        try
        {
            SafeClipboard.Sink(_capReport);
            said.Insert(0, "recording copied to the clipboard, ");
        }
        catch (Exception ex)
        {
            said.Insert(0, $"copy failed ({ex.Message}), ");
        }
        PerfMessage(said.ToString(), now);
    }

    /// <summary>The window's recording row: Start (and its tick trace), or Stop while recording, and the last report.</summary>
    private void DrawCaptureControls(Game? game, double now)
    {
        if (_capOn)
        {
            if (ImGui.Button("Stop recording"u8))
                CaptureStopAsked = true; // (stops in the next frame's PerfCaptureFrame, outside the drawing)
            ImGui.SameLine();
            ImGuiSafe.TextColored(Red, $"REC {now - _capStart:0.0} s, {PerfBench.Kept} frames ({_capKey} stops it)");
            return;
        }
        if (PerfBench.Recording)
        {
            ImGui.TextColored(Dim, "Recording: the benchmark is measuring"u8);
            return;
        }
        if (ImGui.Button("Start recording"u8))
            StartCapture(game, now);
        ImGui.SameLine();
        ImGui.Checkbox("Trace ticks over"u8, ref _capTrace);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110 * _scale);
        ImGui.SliderFloat("##captrace"u8, ref _capTraceMs, 1f, 8f, "%.1f ms"u8);
        if (_capReport != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Copy last recording"u8))
            {
                try
                {
                    SafeClipboard.Sink(_capReport);
                    PerfMessage("copied the last recording to the clipboard", now);
                }
                catch (Exception ex)
                {
                    PerfMessage($"copy failed: {ex.Message}", now);
                }
            }
        }
        ImGuiSafe.TextColored(Dim, $"Start, then do the thing to measure: the overlay shows REC, and {_capKey} stops the recording and reopens this window.");
    }

    /// <summary>The overlay's recording line.</summary>
    private void DrawCaptureOverlayLine()
    {
        if (_capOn)
            ImGuiSafe.TextColored(Red, $"REC {_capNow - _capStart:0.0} s, {PerfBench.Kept} frames - {_capKey} stops");
    }

    /// <summary>The recording as text (allocates: once, when it stops).</summary>
    private string CaptureReport(PerfBench.Spot s, Game? game, double seconds, bool full)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var n = PerfBench.Kept;
        sb.Append(c, $"Call of Slop perf recording, build {BuildInfo.Version}, {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n");
        sb.Append(c, $"recorded {seconds:0.0} s: {s.Frames} frames kept{(full ? $" (FULL at {CaptureFrames} frames: it stopped itself)" : "")}\n");
        if (n == 0)
            return sb.Append("no frames were kept\n").ToString();
        sb.Append(c, $"frame time: mean {s.Mean:0.00} ms ({s.Fps:0} fps), median {s.P50:0.00}, 95th {s.P95:0.00}, 99th {s.P99:0.00}, worst {s.Max:0.00}; 1% low {s.Low1Fps:0} fps\n");
        var ticksPerSecond = seconds > 0 ? s.TicksPerFrame * s.Frames / seconds : 0;
        sb.Append(c, $"server ticks: {ticksPerSecond:0}/s, mean {s.TickMean:0.000} ms, worst {s.TickWorst:0.000} ms (budget 8 ms)\n");
        sb.Append(c, $"allocated per frame: {s.AllocBytes:0} B main thread, {s.AllocAllBytes:0} B all threads; GC: gen0 {s.Gc0}, gen1 {s.Gc1}, gen2 {s.Gc2} ({s.GcFrames} frames with one)\n");
        sb.Append(c, $"draw calls {s.Draws:0}, triangles {s.Triangles:0}, instances {s.Instances:0} per frame\n");
        if (s.OtherCores >= 0.05)
            sb.Append(c, $"NOT ALONE: {s.Others} used {s.OtherCores:0.0} cores while recording\n");

        sb.Append("worst frames (seconds into the recording; the deepest areas over 0.1 ms):\n");
        foreach (var (index, ms, top) in s.WorstFrames)
        {
            sb.Append(c, $"  {ms:0.00} ms at {PerfBench.SecondsAt(index):0.00} s{(PerfBench.TicksAt(index) > 0 ? $", {PerfBench.TicksAt(index)} tick(s), longest {PerfBench.TickWorstAt(index):0.00} ms" : ", no tick")}:");
            foreach (var (a, v) in top)
                sb.Append(c, $" {Perf.Names[a]} {v:0.00};");
            sb.Append('\n');
        }
        if (s.SlowTicks.Length > 0)
        {
            sb.Append(c, $"ticks over {_capTraceMs:0.0} ms, by section ({s.SlowTicks.Length}{(s.SlowTicks.Length >= 64 ? ", the first 64" : "")}):\n");
            foreach (var t in s.SlowTicks)
                sb.Append("  ").Append(t).Append('\n');
        }
        else if (_capTrace)
            sb.Append(c, $"no tick took {_capTraceMs:0.0} ms or more\n");

        CaptureTimeline(sb, s, seconds);

        sb.Append("CPU per frame over the recording (mean / peak ms):\n");
        for (var a = 0; a < Perf.Count; a++)
            if (s.AreaMean[a] >= 0.0005 || s.AreaMax[a] >= 0.05)
                sb.Append(c, $"  {new string(' ', Perf.Depth[a] * 2)}{Perf.Names[a]}: {s.AreaMean[a]:0.000} / {s.AreaMax[a]:0.000}\n");
        if (s.GpuFrames > 0)
        {
            sb.Append(c, $"GPU per pass over {s.GpuFrames} frames read (mean ms), total {s.GpuTotal:0.000}, plus {s.GpuGap:0.000} between passes:\n");
            for (var p = 0; p < PerfGpu.Count; p++)
                if (s.GpuMean[p] >= 0.0005)
                    sb.Append(c, $"  {PerfGpu.Names[p]}: {s.GpuMean[p]:0.000}\n");
        }
        else
            sb.Append("GPU timer: no readings\n");
        _capBodies.Report(sb, (int)Math.Round(s.TicksPerFrame * s.Frames));
        sb.Append(PerfExtra(game));
        return sb.ToString();
    }

    /// <summary>One line per second (or per a few seconds on a long recording): when in it things went slow.</summary>
    private static void CaptureTimeline(StringBuilder sb, PerfBench.Spot s, double seconds)
    {
        var c = CultureInfo.InvariantCulture;
        var n = PerfBench.Kept;
        var bucket = Math.Max(1, (int)Math.Ceiling(seconds / 120)); // (at most about 120 lines)
        sb.Append(c, $"timeline, {bucket} s a line (frames, mean fps, worst frame, worst tick, and the slowest area of the worst frame):\n");
        double at = 0, sum = 0;
        int frames = 0, worstAt = -1, line = 0;
        float worst = 0, tick = 0;
        for (var i = 0; i <= n; i++)
        {
            var here = i < n ? (int)(at / bucket) : int.MaxValue; // (the line frame i starts in)
            if (here != line && frames > 0)
            {
                var slow = "";
                float slowMs = 0;
                for (var a = 0; a < Perf.Count; a++)
                {
                    // (the deepest area whose children don't explain it, as in the worst frames' list)
                    var v = PerfBench.AreaAt(worstAt, a);
                    if (v <= slowMs)
                        continue;
                    var kids = 0f;
                    for (var k = 0; k < Perf.Count; k++)
                        if (Perf.Parent[k] == a)
                            kids += PerfBench.AreaAt(worstAt, k);
                    if (kids < v * 0.8f)
                        (slow, slowMs) = (Perf.Names[a], v);
                }
                sb.Append(c, $"  {line * bucket,5} s: {frames,5} frames, {(sum > 0 ? 1000 * frames / sum : 0),5:0} fps, worst {worst,6:0.00} ms, tick {tick,5:0.00} ms{(slowMs > 0 ? $"; {slow} {slowMs:0.00}" : "")}\n");
                (sum, frames, worstAt, worst, tick) = (0, 0, -1, 0, 0);
            }
            if (i == n)
                break;
            line = here;
            var f = PerfBench.FrameAt(i);
            at += f / 1000.0;
            sum += f;
            frames++;
            tick = MathF.Max(tick, PerfBench.TickWorstAt(i));
            if (f > worst || worstAt < 0)
                (worst, worstAt) = (f, i);
        }
    }
}
