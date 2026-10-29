using System.Diagnostics.Tracing;
using Brutal.GlfwApi;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// <c>--alloc-frames "Map" [--stress-size N] [--frames N] [--warmup N] [--views closed,window,overlay] [--alloc-host |
/// --alloc-join IP] [--alloc-types]</c>: a frame-counted allocation report of the real main loop (so a slow software
/// GL box measures the same as a fast PC). It plays the map like <c>--perf-shot</c> (single player by default; with
/// <c>--alloc-host</c> it hosts, with <c>--alloc-join</c> it joins a host), waits for the warm-up frames, then for each
/// perf view (the F9 window closed, the full window, the corner overlay) runs N frames and logs the main thread's bytes
/// allocated per frame, broken down by perf area (<see cref="Perf.TrackAllocs"/>: each area's own bytes, outside its
/// children), plus all threads and the GC counts. <c>--alloc-types</c> also listens to the runtime's allocation-tick
/// events (one every ~100 KB) and lists the types they name. The timing scopes stay on in every view (that is how the
/// areas are charged; they don't allocate), with the GPU pass timer off while the window is closed, as in play.
/// Lines start with <c>[alloc-frames]</c>; then it quits. A host started with <c>--alloc-host</c> keeps playing until
/// its own frames are done, so give it more frames than the joiner.
/// </summary>
public static unsafe partial class Program
{
    private static bool _allocFrames;
    private static int _afFrames = 300, _afWarmup = 300, _afSettle = 30;
    private static PerfView[] _afViews = [PerfView.Off, PerfView.Window, PerfView.Overlay];
    private static int _afStep, _afView, _afCount;
    private static long _afStartMain, _afStartAll, _afPrev, _afMax;
    private static int _afGc0, _afGc1, _afGc2, _afAllocFrames;
    private static PerfGpu? _afGpu;
    private static AllocTypeListener? _afTypes;
    private static bool _afHost, _afWarmupOnly, _afStay, _afSpectate;
    private static double _afSuicideAt = -10;
    private static int _afHumans = 1;

    private static int Humans(Game game)
    {
        var n = 0;
        foreach (var p in game.Players.Values)
            if (p.Bot == null)
                n++;
        return n;
    }

    private static void StartAllocFrames(string[] args)
    {
        _allocFrames = true;
        _settings.Bots = 0;
        _settings.MapRandom = false;
        string? Arg(string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        if (Arg("--alloc-frames") is { } name && !name.StartsWith("--") && Maps.Find(name) is { } map)
        {
            _settings.MapName = map.Name;
            if (map.Zombies != null)
                _settings.Mode = GameMode.Zombies;
        }
        if (Arg("--stress-size") is { } sz && int.TryParse(sz, out var n))
            _settings.ZombieStressSize = n;
        if (Arg("--frames") is { } f && int.TryParse(f, out var fn))
            _afFrames = Math.Clamp(fn, 1, 100000);
        if (Arg("--warmup") is { } w && int.TryParse(w, out var wn))
            _afWarmup = Math.Max(wn, 0);
        if (Arg("--views") is { } v)
            _afViews = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.ToLowerInvariant() switch { "window" => PerfView.Window, "overlay" => PerfView.Overlay, _ => PerfView.Off }).ToArray();
        if (args.Any(a => a.Equals("--alloc-types", StringComparison.OrdinalIgnoreCase)))
            _afTypes = new AllocTypeListener();
        _afWarmupOnly = args.Any(a => a.Equals("--alloc-warmup", StringComparison.OrdinalIgnoreCase));
        if (Arg("--alloc-humans") is { } hm && int.TryParse(hm, out var hn))
            _afHumans = Math.Max(1, hn); // a host: wait until this many people are in before warming up
        _afSpectate = args.Any(a => a.Equals("--alloc-spectate", StringComparison.OrdinalIgnoreCase));
        _afStay = args.Any(a => a.Equals("--alloc-stay", StringComparison.OrdinalIgnoreCase));
        _afHost = args.Any(a => a.Equals("--alloc-host", StringComparison.OrdinalIgnoreCase));
        if (Arg("--alloc-join") is { } ip)
        {
            _settings.JoinAddress = Settings.CleanAddress(ip);
            StartSession(LobbyAction.Join);
        }
        else
            StartSession(_afHost ? LobbyAction.Host : LobbyAction.SinglePlayer);
        Console.WriteLine($"[alloc-frames] {_settings.MapName} ({_settings.Mode}), stress size {_settings.ZombieStressSize}, {(_afHost ? "host" : _session?.IsServer == false ? "client" : "single player")}, "
                          + $"warm-up {_afWarmup} frames, {_afFrames} frames per view: {string.Join(", ", _afViews)}");
    }

    /// <summary>Once per main-loop pass, after <see cref="Perf.EndFrame"/> (outside every scope).</summary>
    private static void AllocFramesTick()
    {
        var now = Glfw.Time;
        var game = _session;
        if (game != null)
        {
            if (game.InLobby && game.IsServer)
                game.DebugLobby(false, now); // a host: straight past the lobby room
            if (Ui.MenuOpen)
                Ui.AutoDeploy(game, now);
            else if (game.IsWarmup && !_afWarmupOnly && game.Local is { Ready: false })
                game.ToggleReady(); // into the match (the warmup is measured with --alloc-warmup)
            else if (game.Phase == MatchPhase.Playing && game.Local is not { Alive: true } && !game.RespawnRequested && !_afSpectate)
                game.RequestRespawn();
            if (game.IsServer)
                foreach (var p in game.Players.Values)
                    p.ImmuneUntil = now + 0.5; // a host: everyone (a joiner measuring mustn't be killed)
            else if (game.Local is { } me)
                me.ImmuneUntil = now + 0.5;
            if (_afSpectate && game.Phase == MatchPhase.Playing && game.Local is { Alive: true } && _afStep == 0 && now - _afSuicideAt > 2)
            {
                _afSuicideAt = now;
                game.RequestSuicide(); // --alloc-spectate: measure the dead (spectating) frames instead
            }
        }
        switch (_afStep)
        {
            case 0: // waiting for the match and a live player
                if (game is { Local: { } lp } && lp.Alive != _afSpectate && game.Phase == (_afWarmupOnly ? MatchPhase.Warmup : MatchPhase.Playing) && Humans(game) >= _afHumans)
                {
                    _afStep = 1;
                    _afCount = 0;
                }
                else if (++_afCount % 300 == 0)
                    Console.WriteLine($"[alloc-frames] waiting: {_afCount} frames, phase {game?.Phase}, player {(game?.Local is { } p ? p.Alive ? "alive" : "dead" : "none")}, menu {Ui.MenuOpen}");
                break;
            case 1 when ++_afCount >= _afWarmup:
                _afView = 0;
                BeginAllocView();
                break;
            case 2 when ++_afCount >= _afSettle: // the view has settled: count from here
                _afStep = 3;
                _afCount = 0;
                _afMax = 0;
                _afAllocFrames = 0;
                (_afGc0, _afGc1, _afGc2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
                _afStartAll = GC.GetTotalAllocatedBytes(true);
                _afTypes?.Reset();
                Perf.ResetAllocs();
                AllocProfile.Reset(); // the game's own finer sections (AllocProfile marks in Game.Update and the server tick)
                AllocProfile.On = true;
                _afStartMain = _afPrev = GC.GetAllocatedBytesForCurrentThread();
                break;
            case 3:
            {
                var a = GC.GetAllocatedBytesForCurrentThread();
                var d = a - _afPrev;
                if (d > 0)
                    _afAllocFrames++;
                if (d > _afMax)
                    _afMax = d;
                _afPrev = a;
                if (++_afCount >= _afFrames)
                {
                    Perf.FlushAllocs();
                    ReportAllocView(a);
                    if (++_afView < _afViews.Length)
                        BeginAllocView();
                    else
                    {
                        _afStep = 4;
                        AllocProfile.On = Perf.TrackAllocs = false;
                        Console.WriteLine("[alloc-frames] done");
                        _quit = !_afStay; // --alloc-stay: a host keeps hosting (for a joiner still measuring) until it is closed
                    }
                }
                break;
            }
        }
        if (_afStep is 2 or 3)
        {
            Perf.SetOn(true); // the scopes charge the areas in every view (DrawPerf turns them off when the window closes)
            Perf.TrackAllocs = true;
        }
        Perf.FlushAllocs(); // this hook's own bytes (between frames) are not the frame's
        if (_afStep == 3)
            _afPrev = GC.GetAllocatedBytesForCurrentThread();
    }

    private static void BeginAllocView()
    {
        var view = _afViews[_afView];
        _afGpu ??= Perf.Gpu;
        Perf.Gpu = view == PerfView.Off ? null : _afGpu; // closed: no GPU pass timer, as in play
        SetPerfMode(view);
        _afStep = 2;
        _afCount = 0;
    }

    private static void ReportAllocView(long mainNow)
    {
        var frames = Math.Max(_afCount, 1);
        var main = (mainNow - _afStartMain) / (double)frames;
        var all = (GC.GetTotalAllocatedBytes(true) - _afStartAll) / (double)frames;
        var view = _afViews[_afView];
        var sb = new StringBuilder();
        sb.Append($"[alloc-frames] view {view}: {main:0.0} B/frame main thread (max {_afMax} B, {_afAllocFrames} of {_afCount} frames allocated), {all:0.0} B/frame all threads, ");
        sb.Append($"GC gen0 {GC.CollectionCount(0) - _afGc0} gen1 {GC.CollectionCount(1) - _afGc1} gen2 {GC.CollectionCount(2) - _afGc2}; by area (own bytes per frame):");
        var order = Enumerable.Range(0, Perf.Count + 1).Where(i => Perf.AllocSelf[i] != 0).OrderByDescending(i => Perf.AllocSelf[i]);
        var any = false;
        foreach (var i in order)
        {
            sb.Append(any ? ", " : " ").Append(i == Perf.Count ? "(outside scopes)" : Perf.Names[i]).Append(' ').Append((Perf.AllocSelf[i] / (double)frames).ToString("0.0"));
            any = true;
        }
        if (!any)
            sb.Append(" none");
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"[alloc-frames] view {view}: the game's sections (bytes per frame): {AllocProfile.Top(30, frames)}");
        AllocProfile.Reset();
        if (_afTypes != null)
            Console.WriteLine($"[alloc-frames] view {view}: allocation ticks by type (all threads, ~100 KB each): {_afTypes.Report()}");
    }

    /// <summary>The runtime's GC allocation-tick events (about one per 100 KB allocated), counted by type name.</summary>
    private sealed class AllocTypeListener : EventListener
    {
        private readonly Dictionary<string, (int Ticks, long Bytes)> _types = new();
        private readonly object _lock = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1); // GC keyword: GCAllocationTick at verbose
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick") || e.PayloadNames == null || e.Payload == null)
                return;
            var name = "?";
            long bytes = 0;
            for (var i = 0; i < e.PayloadNames.Count; i++)
                switch (e.PayloadNames[i])
                {
                    case "TypeName":
                        name = e.Payload[i] as string ?? "?";
                        break;
                    case "AllocationAmount64":
                        bytes = Convert.ToInt64(e.Payload[i]);
                        break;
                }
            var thread = ThreadName(e.OSThreadId);
            lock (_lock)
            {
                _types.TryGetValue(name, out var t);
                _types[name] = (t.Ticks + 1, t.Bytes + bytes);
                _threads.TryGetValue(thread, out var n);
                _threads[thread] = n + 1;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _types.Clear();
                _threads.Clear();
            }
        }

        private readonly Dictionary<string, int> _threads = new();

        /// <summary>The OS thread's name (Linux: /proc/self/task/ID/comm, which .NET sets from Thread.Name), else its id.</summary>
        private static string ThreadName(long osId)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                    return $"{File.ReadAllText($"/proc/self/task/{osId}/comm").Trim()} ({osId})";
            }
            catch (IOException)
            {
            }
            return $"thread {osId}";
        }

        public string Report()
        {
            lock (_lock)
                return _types.Count == 0 ? "none" : string.Join(", ", _types.OrderByDescending(kv => kv.Value.Bytes).Take(25).Select(kv => $"{kv.Key} x{kv.Value.Ticks}"))
                                                    + "; by thread: " + string.Join(", ", _threads.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}"));
        }
    }
}
