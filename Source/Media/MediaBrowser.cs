using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace CallOfSlop;

/// <summary>What a page's player last said.</summary>
public sealed class MediaReport
{
    public const int Ended = 0, Playing = 1, Paused = 2, Buffering = 3, Cued = 5, NotLoaded = -2;

    public volatile int State = NotLoaded;
    public volatile int Error;
    public volatile float Time, Duration;
    public volatile string Title = "";
    public volatile string Address = ""; // a web page's, secrets out
    public volatile bool HasMedia, InnerPaused; // its main video or audio
    public volatile float Scroll, InnerTime;
    public volatile string Failure = ""; // the browser's own word on why this page can't show anything ("pfail"), or ""
}

/// <summary>
/// The browser process from the game's side (docs/sandbox/MEDIA.md). CEF lives in its own exe, Browser/SlopBrowser.exe next to the game, started
/// by the first page and let go when none is left, so a game that never plays a link never starts it, and its crash can't be the game's.
/// Nothing here waits on it: commands queue for a thread that writes them, reports come in on another. Whatever goes wrong ends up in
/// <see cref="Problem"/>, which the panels and the look line show. The self-tests leave <see cref="Allowed"/> off and never start one.
/// </summary>
public static class MediaBrowser
{
    private const int MaxRestarts = 2;
    private const double RestartAfter = 10, ReadyWithin = 30;

    /// <summary>
    /// The command lines' version, which the helper says with "ready" (tools/browser/Program.cs <c>Version</c>: raise both together whenever a line changes). A helper built
    /// from older source used to skip the commands it didn't know, and every screen said "Loading" for ever; now it is refused and the panels say how to rebuild it.
    /// </summary>
    public const int HelperVersion = 2;

    /// <summary>What a player is told to do about a missing or old helper.</summary>
    public const string Rebuild = "run tools\\BuildBrowser.bat (or Start.bat) to build it";

    private static readonly MediaReport[] Reports = NewReports();
    private static readonly MediaBlock?[] Blocks = new MediaBlock?[MediaDirector.MaxPages];
    private static BlockingCollection<string>? _lines; // (one per process: a new one never hears what was said to the last)
    private static Process? _process;
    private static int _restarts;
    private static double _failedAt = double.NegativeInfinity;
    private static volatile string? _problem;
    private static volatile string? _stale; // the helper is from other source: no restart helps, so it stays all session
    private static volatile bool _ready;
    private static double _startedAt;
    private static int _blockNumber;

    /// <summary>Set by the windowed game; the self-tests and any tool leave it off.</summary>
    public static bool Allowed;

    private static MediaReport[] NewReports()
    {
        var a = new MediaReport[MediaDirector.MaxPages];
        for (var i = 0; i < a.Length; i++)
            a[i] = new MediaReport();
        return a;
    }

    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "Browser", OperatingSystem.IsWindows() ? "SlopBrowser.exe" : "SlopBrowser");

    public static bool Running => _process != null;

    /// <summary>The helper said "ready" with this game's version.</summary>
    public static bool Ready => _ready;

    public static MediaReport Report(int page) => Reports[page];

    public static MediaBlock? Block(int page) => Blocks[page];

    /// <summary>Why links can't play on this PC, or null.</summary>
    public static string? Problem
    {
        get
        {
            if (!Allowed)
                return "links are off here";
            if (_stale != null)
                return _stale;
            if (_problem != null)
                return _problem;
            return File.Exists(ExePath) ? null : OperatingSystem.IsWindows() ? "this build has no Browser folder: " + Rebuild : "links need the Windows game for now";
        }
    }

    /// <summary>Opens a page in slot <paramref name="page"/>. False: no browser, and <see cref="Problem"/> says why.</summary>
    public static bool Open(int page, string videoId, float seconds, int width, int height, bool ads)
    {
        if (!MediaEntry.IsId(videoId) || Problem != null || !Start() || !Prepare(page, width, height))
            return false;
        Send(string.Create(CultureInfo.InvariantCulture, $"open {page} {videoId} {seconds:0.##} {width} {height} {Blocks[page]!.Name} {(ads ? 1 : 0)}"));
        return true;
    }

    private static bool Prepare(int page, int width, int height)
    {
        try
        {
            if (Blocks[page] is not { } b || b.Width != width || b.Height != height)
            {
                Blocks[page]?.Dispose();
                b = Blocks[page] = new MediaBlock($"SlopMedia-{Environment.ProcessId}-{_blockNumber++}", width, height);
            }
            b.Reset();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail($"no shared memory ({e.Message})");
            return false;
        }
        var r = Reports[page];
        r.State = MediaReport.NotLoaded;
        r.Error = 0;
        r.Time = r.Duration = 0;
        r.Title = "";
        r.Address = "";
        r.HasMedia = r.InnerPaused = false;
        r.Scroll = r.InnerTime = 0;
        r.Failure = "";
        return true;
    }

    /// <summary>Opens a Browser Screen's page. The address has been through <see cref="MediaEntry.Clean"/>. Nothing here logs it.</summary>
    public static bool OpenWeb(int page, string url, int width, int height, bool ads)
    {
        if (Problem != null || !Start() || !Prepare(page, width, height))
            return false;
        Send($"web {page} {url} {width} {height} {Blocks[page]!.Name} {(ads ? 1 : 0)}");
        return true;
    }

    /// <summary>Ad blocking on this page, this PC only: it reloads.</summary>
    public static void Ads(int page, bool on) => Send($"ads {page} {(on ? 1 : 0)}");

    public static void WebScroll(int page, float share) => Send(string.Create(CultureInfo.InvariantCulture, $"wscroll {page} {share:0.#####}"));

    public static void WebInner(int page, bool play) => Send(play ? $"wplay {page}" : $"wpause {page}");

    public static void WebSeek(int page, float seconds) => Send(string.Create(CultureInfo.InvariantCulture, $"wseek {page} {seconds:0.##}"));

    public static void Go(int page, string url) => Send($"go {page} {url}");

    /// <summary>"back", "forward" or "reload".</summary>
    public static void Web(int page, string command) => Send($"{command} {page}");

    public static void Play(int page) => Send($"play {page}");

    public static void Pause(int page) => Send($"pause {page}");

    public static void Seek(int page, float seconds) => Send(string.Create(CultureInfo.InvariantCulture, $"seek {page} {seconds:0.##}"));

    public static void Close(int page) => Send($"close {page}");

    /// <summary>No pages left: the browser process goes, with what it held.</summary>
    public static void Quit()
    {
        if (_process == null)
            return;
        Send("quit");
        _lines?.CompleteAdding();
        _process = null;
        _lines = null;
        _ready = false;
    }

    /// <summary>A helper that went quiet or is from other source: it goes now, not when it next reads.</summary>
    private static void Kill()
    {
        var process = _process;
        Quit();
        try
        {
            process?.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Console.WriteLine($"[media] the browser didn't stop: {e.Message}");
        }
    }

    /// <summary>The game is closing. Its end of stdin closing would do it too.</summary>
    public static void Shutdown() => Quit();

    private static void Send(string line) => _lines?.TryAdd(line); // (a browser that stopped reading loses commands, and the game never waits for it)

    private static bool Start()
    {
        if (_process != null)
            return true;
        if (_restarts > MaxRestarts)
            return false;
        try
        {
            var info = new ProcessStartInfo(ExePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(ExePath)!,
            };
            info.ArgumentList.Add("--log-dir");
            info.ArgumentList.Add(AppFolders.Logs);
            var process = Process.Start(info) ?? throw new InvalidOperationException("it did not start");
            var lines = new BlockingCollection<string>(64);
            _process = process;
            _lines = lines;
            _problem = null;
            _ready = false;
            _startedAt = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            new Thread(() => Writer(process, lines)) { IsBackground = true, Name = "Browser in" }.Start();
            new Thread(() => Reader(process)) { IsBackground = true, Name = "Browser out" }.Start();
            Console.WriteLine("[media] browser started");
            return true;
        }
        catch (Exception e)
        {
            Fail($"the browser didn't start ({e.Message})");
            return false;
        }
    }

    private static void Fail(string why)
    {
        Console.WriteLine($"[media] {why}");
        _problem = why;
        _failedAt = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        _restarts++;
    }

    /// <summary>Each scan: after a pause a failed browser gets another go, a few times a session.</summary>
    public static void Tick()
    {
        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (_process != null && _stale != null)
            Kill(); // (said by the reader's thread: this is the main one)
        else if (_process != null && !_ready && now - _startedAt > ReadyWithin)
        {
            Kill();
            Fail($"the browser didn't start in {ReadyWithin:0} s (see slopbrowser.log beside callofslop.log)");
        }
        if (_problem != null && _restarts <= MaxRestarts && now - _failedAt > RestartAfter)
            _problem = null;
    }

    private static void Writer(Process process, BlockingCollection<string> lines)
    {
        try
        {
            var input = process.StandardInput;
            input.AutoFlush = true;
            foreach (var line in lines.GetConsumingEnumerable())
            {
                if (process.HasExited)
                    break;
                input.WriteLine(line);
                if (line == "quit")
                {
                    input.Close();
                    return;
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // (it went: the reader says so)
        }
    }

    private static void Reader(Process process)
    {
        try
        {
            while (process.StandardOutput.ReadLine() is { } line)
                Heard(line);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
        }
        if (ReferenceEquals(_process, process))
        {
            _lines?.CompleteAdding();
            _lines = null;
            _process = null;
            Fail("the browser closed");
        }
    }

    private static void Heard(string line)
    {
        var a = line.Split(' ', 6);
        switch (a[0])
        {
            case "ready":
                if (a.Length >= 2 && int.TryParse(a[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version == HelperVersion)
                {
                    _ready = true;
                    Console.WriteLine($"[media] browser ready (version {version})");
                }
                else
                {
                    _stale = "the Browser folder is out of date: " + Rebuild;
                    Console.WriteLine($"[media] browser refused: it says version {(a.Length >= 2 ? MediaEntry.Printable(a[1]) : "none")}, this game needs {HelperVersion} ({ExePath} is from older source: {Rebuild})");
                }
                break;
            case "pfail" when a.Length >= 3 && int.TryParse(a[1], out var page5) && (uint)page5 < Reports.Length:
                var why = MediaEntry.Printable(line[(line.IndexOf(' ', 6) + 1)..], 120);
                Reports[page5].Failure = why;
                Console.WriteLine($"[media] page {page5}: {why}");
                break;
            case "pok" when a.Length >= 2 && int.TryParse(a[1], out var page6) && (uint)page6 < Reports.Length:
                Reports[page6].Failure = "";
                break;
            case "log":
                Console.WriteLine($"[media] browser: {line[Math.Min(4, line.Length)..]}");
                break;
            case "fail":
                Console.WriteLine($"[media] browser: {line}");
                _problem = "browser: " + MediaEntry.Printable(line[Math.Min(5, line.Length)..]);
                break;
            case "s" when a.Length >= 6 && int.TryParse(a[1], out var page) && (uint)page < Reports.Length:
                var r = Reports[page];
                r.Time = Num(a[3]);
                r.Duration = Num(a[4]);
                r.Error = int.TryParse(a[5], out var error) ? error : 0;
                r.State = int.TryParse(a[2], out var state) ? state : MediaReport.NotLoaded;
                break;
            case "w" when a.Length >= 6 && int.TryParse(a[1], out var page4) && (uint)page4 < Reports.Length:
                var w = Reports[page4];
                w.Scroll = Num(a[2]);
                w.InnerTime = Num(a[5]);
                w.InnerPaused = a[4] == "1";
                w.HasMedia = a[3] == "1";
                break;
            case "u" when a.Length >= 3 && int.TryParse(a[1], out var page3) && (uint)page3 < Reports.Length:
                Reports[page3].Address = a[2]; // (never logged)
                break;
            case "t" when a.Length >= 3 && int.TryParse(a[1], out var page2) && (uint)page2 < Reports.Length:
                Reports[page2].Title = MediaEntry.Printable(line[(line.IndexOf(' ', 2) + 1)..]);
                break;
        }
    }

    private static float Num(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : 0;
}
