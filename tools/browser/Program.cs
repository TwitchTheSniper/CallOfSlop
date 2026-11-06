using System.Globalization;
using System.Text.RegularExpressions;
using CefSharp;
using CefSharp.OffScreen;

namespace SlopBrowser;

/// <summary>
/// The game's browser (docs/sandbox/MEDIA.md): a process of its own running CEF offscreen, one page per live screen, each page YouTube's
/// official iframe player. The game starts it when the first screen goes live and closes its input when the last one stops.
/// Pictures and sound go to the game through one shared memory block a page; commands come in on stdin, reports go out on stdout.
/// Stdin closing, which is also what the game crashing looks like, ends the process.
/// <code>
/// in:  open SLOT ID SECONDS WIDTH HEIGHT MAP ADS | web SLOT URL WIDTH HEIGHT MAP ADS | ads SLOT ON | go SLOT URL | back SLOT | forward SLOT | reload SLOT | play SLOT | pause SLOT | seek SLOT SECONDS | close SLOT | quit
/// out: ready | fail REASON | log TEXT | s SLOT STATE TIME DURATION ERROR | t SLOT TITLE | u SLOT ADDRESS (no query)
/// </code>
/// </summary>
internal static class Program
{
    private const int MaxSlots = 4; // (the game's MediaDirector.MaxPages)
    private static readonly Regex VideoId = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly IDisposable?[] Pages = new IDisposable?[MaxSlots]; // (a Page, YouTube's locked player; or a WebPage, a Browser Screen's own rules)
    private static readonly object Out = new();
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "SlopBrowser-" + Environment.ProcessId);

    private static int Main(string[] args)
    {
        // (CefSharp's own subprocess exe sits beside this one and runs Chromium's renderers, so this process is only ever the host.)
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            Directory.CreateDirectory(Root);
            var settings = new CefSettings
            {
                WindowlessRenderingEnabled = true,
                RootCachePath = Root, // (its own for each run: a second game on this PC gets a second browser)
                CachePath = "", // (in memory: nothing of a browsing session is left on disk)
                LogSeverity = LogSeverity.Warning,
                LogFile = LogFile(args),
                PersistSessionCookies = false,
                Locale = "en-US",
            };
            settings.EnableAudio(); // (CefSharp's offscreen settings start with --mute-audio: no output at all, so nothing is audible and CEF's audio handler is never asked)
            settings.CefCommandLineArgs.Add("autoplay-policy", "no-user-gesture-required"); // (a screen starts by itself: there is no page to click)
            settings.CefCommandLineArgs.Add("disable-gpu"); // (software: never a second user of the game's GPU, and the frames are copied anyway)
            settings.CefCommandLineArgs.Add("disable-gpu-compositing");
            settings.CefCommandLineArgs.Add("disable-extensions");
            settings.CefCommandLineArgs.Add("disable-pdf-extension");
            settings.CefCommandLineArgs.Add("disable-background-networking");
            // A page here has no window, so nothing may treat it as a tab in the background.
            settings.CefCommandLineArgs.Add("disable-renderer-backgrounding");
            settings.CefCommandLineArgs.Add("disable-background-timer-throttling");
            settings.CefCommandLineArgs.Add("disable-backgrounding-occluded-windows");
            settings.CefCommandLineArgs.Add("disable-background-media-suspend");
            Log("mute-audio is " + (settings.CefCommandLineArgs.ContainsKey("mute-audio") ? "ON: no sound will come" : "off"));
            if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
            {
                Say("fail cef did not start");
                return 2;
            }
        }
        catch (Exception e)
        {
            Say("fail " + OneLine(e.Message));
            return 2;
        }

        Log("ad list: " + AdFilter.Load(Path.Combine(AppContext.BaseDirectory, "adhosts.txt")));
        Say("ready");
        try
        {
            while (Console.In.ReadLine() is { } line)
                if (!Command(line))
                    break;
        }
        catch (Exception e)
        {
            Say("fail " + OneLine(e.Message));
        }

        // A page that won't close must not outlive the game.
        new Thread(() =>
        {
            Thread.Sleep(5000);
            Environment.Exit(0);
        }) { IsBackground = true }.Start();
        for (var i = 0; i < Pages.Length; i++)
            CloseSlot(i);
        Cef.Shutdown();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return 0;
    }

    private static bool Command(string line)
    {
        var a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0)
            return true;
        if (a[0] == "quit")
            return false;
        if (a.Length < 2 || !int.TryParse(a[1], NumberStyles.None, CultureInfo.InvariantCulture, out var slot) || slot >= MaxSlots)
            return true;
        switch (a[0])
        {
            case "ads" when a.Length == 3:
                (Pages[slot] as Page)?.Ads(a[2] == "1");
                (Pages[slot] as WebPage)?.Ads(a[2] == "1");
                break;
            case "open" when a.Length == 8 && VideoId.IsMatch(a[2]):
                CloseSlot(slot);
                Pages[slot] = new Page(slot, a[2], Num(a[3]), (int)Num(a[4]), (int)Num(a[5]), a[6], a[7] == "1");
                break;
            case "web" when a.Length == 7:
                CloseSlot(slot);
                Pages[slot] = new WebPage(slot, a[2], (int)Num(a[3]), (int)Num(a[4]), a[5], a[6] == "1");
                break;
            case "go" when a.Length == 3:
                (Pages[slot] as WebPage)?.Go(a[2]);
                break;
            case "back":
                (Pages[slot] as WebPage)?.Back();
                break;
            case "forward":
                (Pages[slot] as WebPage)?.Forward();
                break;
            case "reload":
                (Pages[slot] as WebPage)?.Reload();
                break;
            case "play":
                (Pages[slot] as Page)?.Run("slop.play()");
                break;
            case "pause":
                (Pages[slot] as Page)?.Run("slop.pause()");
                break;
            case "seek" when a.Length == 3:
                (Pages[slot] as Page)?.Run(string.Create(CultureInfo.InvariantCulture, $"slop.seek({Num(a[2]):0.###})"));
                break;
            case "close":
                CloseSlot(slot);
                break;
        }
        return true;
    }

    private static void CloseSlot(int slot)
    {
        Pages[slot]?.Dispose();
        Pages[slot] = null;
    }

    private static double Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Max(0, v) : 0;

    public static void Say(string line)
    {
        lock (Out)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    /// <summary>A line for the game's log.</summary>
    public static void Log(string text) => Say("log " + OneLine(text));

    /// <summary>CEF's own log, beside the game's when the game says where that is.</summary>
    private static string LogFile(string[] args)
    {
        var at = Array.IndexOf(args, "--log-dir");
        return Path.Combine(at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetTempPath(), "slopbrowser.log");
    }

    public static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ');
}
