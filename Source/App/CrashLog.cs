using System.IO.MemoryMappedFiles;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Crash logging (board item dev-link-tool-mp-crash-lc01). The game has no console, so a crash used to vanish from
/// callofslop.log without a trace. Every unhandled exception (any thread), every unobserved task exception and the main
/// loop's own catch write a "[crash]" block (the exception with its full stack, the build, the frame phase breadcrumb) to
/// callofslop.log (flushed) and to crash.txt beside it. Fail-fast errors (a stack overflow, an access violation in native
/// GL or ImGui code) cannot be caught: for those, <see cref="Phase"/>, <see cref="Detail"/> and the frame number are also
/// written, as they change, into a small memory-mapped file (<see cref="Breadcrumb"/>, breadcrumb.txt in the log folder)
/// overwritten in place with no allocation. The pages belong to the operating system, so they reach the file even when the
/// process dies; the next start logs what it said when the last run did not exit cleanly (lc01, reopened). Release builds
/// also write a crash dump (CrashDumps.cs).
/// </summary>
public static class CrashLog
{
    private static string _phase = "start", _detail = "";

    /// <summary>The frame phase now (a string literal: assigning it allocates nothing; written to the breadcrumb file when it changes).</summary>
    public static string Phase
    {
        get => _phase;
        set
        {
            if (ReferenceEquals(_phase, value))
                return;
            _phase = value;
            _crumb?.SetPhase(value);
        }
    }

    /// <summary>A second breadcrumb: the last thing the phase was doing (a literal too).</summary>
    public static string Detail
    {
        get => _detail;
        set
        {
            if (ReferenceEquals(_detail, value))
                return;
            _detail = value;
            _crumb?.SetDetail(value);
        }
    }

    /// <summary>The main loop's frame number (written to the breadcrumb each frame).</summary>
    public static long Frame { get; private set; }

    /// <summary>A new frame starts (the main loop, once a frame).</summary>
    public static void NextFrame()
    {
        Frame++;
        _crumb?.SetFrame(Frame);
    }

    private static Breadcrumb? _crumb;

    /// <summary>
    /// Opens the breadcrumb file in the log folder (the windowed game only) and logs the previous run's breadcrumb when that run
    /// didn't exit cleanly. A second copy of the game on the same PC uses breadcrumb-2.txt (and so on).
    /// </summary>
    public static void OpenBreadcrumb(string folder)
    {
        try
        {
            _crumb = Breadcrumb.Open(folder, BuildInfo.Label, out var previous);
            if (_crumb == null)
                return;
            _crumb.SetPhase(_phase);
            _crumb.SetDetail(_detail);
            Console.WriteLine($"[log] breadcrumb file: {_crumb.Path}");
            if (previous != null)
            {
                Console.WriteLine($"[crash] the last run did not exit cleanly; its breadcrumb said: {previous}");
                foreach (var dump in CrashDumps.Recent(folder))
                    Console.WriteLine($"[crash] crash dump: {dump}");
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) => _crumb?.Close(clean: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[crash] no breadcrumb file: {ex.Message}");
        }
    }

    /// <summary>The self-test: uses <paramref name="b"/> (or none) as the breadcrumb the setters write to; returns the one before.</summary>
    internal static Breadcrumb? UseBreadcrumb(Breadcrumb? b)
    {
        var old = _crumb;
        _crumb = b;
        return old;
    }

    private static int _reported;
    private static readonly object _lock = new();

    /// <summary>Hooks the process-wide handlers. Call once, early in Main.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report("unhandled exception" + (e.IsTerminating ? " (terminating)" : ""), e.ExceptionObject as Exception, e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report("unobserved task exception", e.Exception, e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Writes a "[crash]" block to the log and to crash.txt. Safe to call from any thread; never throws.</summary>
    public static string Report(string what, Exception? ex, object? raw = null)
    {
        var text = Format(what, ex, raw);
        try
        {
            lock (_lock)
            {
                Console.WriteLine(text);
                Console.Out.Flush();
                _reported++;
                _crumb?.MarkCrashed();
                File.WriteAllText(AppFolders.LogPath("crash.txt"), text);
            }
        }
        catch
        {
            // (a crash report must not crash)
        }
        return text;
    }

    /// <summary>How many reports were written (the self-test).</summary>
    public static int Reported => _reported;

    internal static string Format(string what, Exception? ex, object? raw)
    {
        var sb = new StringBuilder();
        sb.Append("[crash] ").Append(what).Append(" at ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine();
        sb.Append("[crash] build ").Append(BuildInfo.Label).Append(", protocol ").Append(ServerInfo.ProtocolVersion).Append(", ").Append(Environment.OSVersion).AppendLine();
        sb.Append("[crash] frame phase: ").Append(Phase).Append(Detail.Length > 0 ? " / " + Detail : "").Append(", frame ").Append(Frame).Append(", thread ").Append(Environment.CurrentManagedThreadId).AppendLine();
        sb.Append("[crash] ").Append(ex?.ToString() ?? raw?.ToString() ?? "(no exception object)").AppendLine();
        return sb.ToString();
    }
}

/// <summary>
/// The breadcrumb file (lc01, reopened): a fixed 1 KB text file, memory-mapped, whose fields are overwritten in place
/// (ASCII, padded with spaces) when the phase, the detail or the frame changes. No allocation, no system call per write: the
/// mapped pages are the file's, and the operating system writes them out even after the process is killed or fails fast.
/// The "state" line says "running" until the game exits cleanly ("exited"), so the next start knows whether the last run
/// crashed and logs what it was doing.
/// </summary>
public sealed unsafe class Breadcrumb
{
    public const int Size = 1024;
    private const int FieldWidth = 60;

    // Line layout (each line ends in '\n'; the values start after the labels).
    private const string Header = "Call of Slop breadcrumb (CrashLog.cs): what the game was doing; overwritten in place\n";
    private const string StateLabel = "state: ", FrameLabel = "frame: ", PhaseLabel = "phase: ", DetailLabel = "detail: ", BuildLabel = "build: ", PidLabel = "pid: ", StartLabel = "started: ";
    public const string Running = "running", Exited = "exited", Crashed = "crashed";

    private readonly FileStream _file, _lock;
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _p;
    private readonly int _state, _frame, _phase, _detail;

    /// <summary>The file's path.</summary>
    public string Path { get; }

    private Breadcrumb(string path, FileStream file, FileStream lockFile, string build)
    {
        Path = path;
        _file = file;
        _lock = lockFile;
        _file.SetLength(Size);
        _map = MemoryMappedFile.CreateFromFile(_file, null, Size, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
        _view = _map.CreateViewAccessor(0, Size, MemoryMappedFileAccess.ReadWrite);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _p);
        _p += _view.PointerOffset;
        // The fixed text, written once; then the value fields.
        var at = 0;
        new Span<byte>(_p, Size).Fill((byte)' ');
        at = Put(at, Header);
        at = Line(at, BuildLabel, build, out _);
        at = Line(at, PidLabel, Environment.ProcessId.ToString(), out _);
        at = Line(at, StartLabel, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), out _);
        at = Line(at, StateLabel, Running, out _state);
        at = Line(at, FrameLabel, "0", out _frame);
        at = Line(at, PhaseLabel, "", out _phase);
        at = Line(at, DetailLabel, "", out _detail);
        _p[Size - 1] = (byte)'\n';
    }

    private int Put(int at, string s)
    {
        foreach (var ch in s)
            _p[at++] = (byte)(ch < 128 ? ch : '?');
        return at;
    }

    private int Line(int at, string label, string value, out int valueAt)
    {
        at = Put(at, label);
        valueAt = at;
        Field(valueAt, value);
        at += FieldWidth;
        _p[at++] = (byte)'\n';
        return at;
    }

    /// <summary>Overwrites a value field: ASCII, cut to the width, the rest spaces. No allocation.</summary>
    private void Field(int at, string? value)
    {
        if (_p == null)
            return;
        var n = value == null ? 0 : Math.Min(value.Length, FieldWidth);
        for (var i = 0; i < n; i++)
        {
            var ch = value![i];
            _p[at + i] = (byte)(ch is >= ' ' and < (char)127 ? ch : '?');
        }
        for (var i = n; i < FieldWidth; i++)
            _p[at + i] = (byte)' ';
    }

    public void SetPhase(string value) => Field(_phase, value);

    public void SetDetail(string value) => Field(_detail, value);

    /// <summary>The frame number as decimal digits, in place.</summary>
    public void SetFrame(long frame)
    {
        if (_p == null)
            return;
        Span<byte> digits = stackalloc byte[20];
        var n = 0;
        var v = (ulong)Math.Max(0, frame);
        do
        {
            digits[n++] = (byte)('0' + (int)(v % 10));
            v /= 10;
        } while (v > 0);
        for (var i = 0; i < FieldWidth; i++)
            _p[_frame + i] = i < n ? digits[n - 1 - i] : (byte)' ';
    }

    /// <summary>Closes the file: "exited" when the game is quitting normally, otherwise the state is left as it is (the self-test).</summary>
    public void Close(bool clean)
    {
        if (_p == null)
            return;
        if (clean)
            Field(_state, Exited);
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _p = null;
        _view.Flush();
        _view.Dispose();
        _map.Dispose();
        _file.Dispose();
        _lock.Dispose();
    }

    /// <summary>Marks the run as crashed (a managed crash report was written) without closing.</summary>
    public void MarkCrashed() => Field(_state, Crashed);

    /// <summary>
    /// Opens breadcrumb.txt in <paramref name="folder"/> (breadcrumb-2.txt... when another copy of the game has it open),
    /// after reading what the last run left there: <paramref name="previous"/> is its summary when that run didn't exit
    /// cleanly, else null. Null when no file could be opened.
    /// </summary>
    public static Breadcrumb? Open(string folder, string build, out string? previous)
    {
        previous = null;
        Directory.CreateDirectory(folder);
        for (var slot = 1; slot <= 4; slot++)
        {
            var path = System.IO.Path.Combine(folder, slot == 1 ? "breadcrumb.txt" : $"breadcrumb-{slot}.txt");
            FileStream lk, fs;
            try
            {
                // (the lock file is held exclusively while the game runs: a second copy takes the next slot; the breadcrumb itself
                // stays readable by anyone, as a share mode that keeps writers out is only enforced on Windows)
                lk = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                continue; // another copy of the game is using it
            }
            try
            {
                fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                previous = Summary(ReadAll(fs));
                return new Breadcrumb(path, fs, lk, build);
            }
            catch
            {
                lk.Dispose();
                throw;
            }
        }
        return null;
    }

    private static string ReadAll(FileStream fs)
    {
        if (fs.Length == 0)
            return "";
        var buf = new byte[Math.Min(fs.Length, 4096)];
        fs.Position = 0;
        var n = fs.Read(buf, 0, buf.Length);
        fs.Position = 0;
        return Encoding.ASCII.GetString(buf, 0, n);
    }

    /// <summary>A breadcrumb file's text as one line, when its run didn't exit cleanly ("running" or "crashed"); null otherwise (exited, empty, not one).</summary>
    public static string? Summary(string text)
    {
        if (!text.StartsWith("Call of Slop breadcrumb", StringComparison.Ordinal))
            return null;
        string Value(string label)
        {
            foreach (var line in text.Split('\n'))
                if (line.StartsWith(label, StringComparison.Ordinal))
                    return line[label.Length..].Trim();
            return "";
        }
        var state = Value(StateLabel);
        if (state is not (Running or Crashed))
            return null;
        var detail = Value(DetailLabel);
        return $"build {Value(BuildLabel)}, pid {Value(PidLabel)}, started {Value(StartLabel)}, {state}, frame {Value(FrameLabel)}, phase {Value(PhaseLabel)}{(detail.Length > 0 ? " / " + detail : "")}";
    }

    /// <summary>Reads a breadcrumb file another process may have open (the self-test, and a tester looking at it).</summary>
    public static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ReadAll(fs);
    }
}
