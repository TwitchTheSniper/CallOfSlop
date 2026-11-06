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
    private const double RestartAfter = 10;

    private static readonly MediaReport[] Reports = NewReports();
    private static readonly MediaBlock?[] Blocks = new MediaBlock?[MediaDirector.MaxPages];
    private static readonly BlockingCollection<string> Lines = new(64);
    private static Process? _process;
    private static int _restarts, _ready;
    private static double _failedAt = double.NegativeInfinity;
    private static string? _problem;
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

    public static MediaReport Report(int page) => Reports[page];

    public static MediaBlock? Block(int page) => Blocks[page];

    /// <summary>Why links can't play on this PC, or null.</summary>
    public static string? Problem
    {
        get
        {
            if (!Allowed)
                return "links are off here";
            if (_problem != null)
                return _problem;
            return File.Exists(ExePath) ? null : OperatingSystem.IsWindows() ? "this build has no browser files (Browser folder)" : "links need the Windows game for now";
        }
    }

    /// <summary>Opens a page in slot <paramref name="page"/>. False: no browser, and <see cref="Problem"/> says why.</summary>
    public static bool Open(int page, string videoId, float seconds, int width, int height)
    {
        if (!MediaEntry.IsId(videoId) || Problem != null || !Start())
            return false;
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
        Send(string.Create(CultureInfo.InvariantCulture, $"open {page} {videoId} {seconds:0.##} {width} {height} {Blocks[page]!.Name}"));
        return true;
    }

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
        _process = null;
        Volatile.Write(ref _ready, 0);
    }

    /// <summary>The game is closing. Its end of stdin closing would do it too.</summary>
    public static void Shutdown()
    {
        Quit();
        Lines.CompleteAdding();
    }

    private static void Send(string line)
    {
        if (_process == null || Lines.IsAddingCompleted)
            return;
        Lines.TryAdd(line); // (a browser that stopped reading loses commands, and the game never waits for it)
    }

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
            var process = Process.Start(info) ?? throw new InvalidOperationException("it did not start");
            _process = process;
            _problem = null;
            new Thread(() => Writer(process)) { IsBackground = true, Name = "Browser in" }.Start();
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
        if (_problem != null && _restarts <= MaxRestarts && Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency - _failedAt > RestartAfter)
            _problem = null;
    }

    private static void Writer(Process process)
    {
        try
        {
            var input = process.StandardInput;
            input.AutoFlush = true;
            foreach (var line in Lines.GetConsumingEnumerable())
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
            _process = null;
            Volatile.Write(ref _ready, 0);
            Fail("the browser closed");
        }
    }

    private static void Heard(string line)
    {
        var a = line.Split(' ', 6);
        switch (a[0])
        {
            case "ready":
                Volatile.Write(ref _ready, 1);
                _restarts = 0;
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
            case "t" when a.Length >= 3 && int.TryParse(a[1], out var page2) && (uint)page2 < Reports.Length:
                Reports[page2].Title = MediaEntry.Printable(line[(line.IndexOf(' ', 2) + 1)..]);
                break;
        }
    }

    private static float Num(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : 0;
}
