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
/// Browser Screens share one request context for the life of the process, in memory, so a sign-in lasts until the game closes (Sean, 2026-11-24: not on disk). A Browser Screen out of range
/// is parked on the shelf under a name the game gives it (SHELF: letters and digits) rather than closed, and a <c>web</c> naming it takes it back as it was; "-" names none.
/// <code>
/// in:  open SLOT ID SECONDS WIDTH HEIGHT MAP ADS | web SLOT URL WIDTH HEIGHT MAP ADS SHELF | park SLOT SHELF | drop SHELF | ads SLOT ON | go SLOT URL | wscroll SLOT SHARE | wplay SLOT | wpause SLOT | wseek SLOT SECONDS | back SLOT | forward SLOT | reload SLOT | play SLOT | pause SLOT | seek SLOT SECONDS | close SLOT | quit
/// out: ready VERSION | fail REASON | pfail SLOT REASON | pok SLOT | log TEXT | s SLOT STATE TIME DURATION ERROR | t SLOT TITLE | u SLOT ADDRESS (secrets out) | w SLOT SCROLL HASMEDIA PAUSED TIME
/// </code>
/// </summary>
internal static class Program
{
    private const int MaxSlots = 4; // (the game's MediaDirector.MaxPages)

    /// <summary>
    /// The command lines' version, said with "ready". The game refuses a helper whose number isn't its own <c>MediaBrowser.HelperVersion</c> and says on the panels
    /// that the Browser folder is out of date: an old helper used to skip a command it didn't know, and every screen said "Loading" for ever. Raise both together whenever a line changes.
    /// </summary>
    public const int Version = 3;

    /// <summary>Shelved pages at most. The game keeps fewer (<c>MediaDirector.MaxShelved</c>) and drops its own; this only bounds a game that forgot to.</summary>
    private const int MaxShelf = 8;

    private static readonly Regex VideoId = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShelfName = new("^[A-Za-z0-9]{1,24}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly IDisposable?[] Pages = new IDisposable?[MaxSlots]; // (a Page, YouTube's locked player; or a WebPage, a Browser Screen's own rules)
    private static readonly List<(string Name, WebPage Page)> Shelf = []; // oldest first
    private static RequestContext? _webContext;
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
        Say("ready " + Version.ToString(CultureInfo.InvariantCulture));
        try
        {
            while (Console.In.ReadLine() is { } line)
                if (!Safe(line))
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
        while (Shelf.Count > 0)
            DropAt(0);
        _webContext?.Dispose();
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

    /// <summary>One command. A page that can't be made fails on its own panel (<c>pfail</c>), and the others go on.</summary>
    private static bool Safe(string line)
    {
        try
        {
            return Command(line);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var a = line.Split(' ', 3);
            if (a.Length >= 2 && int.TryParse(a[1], NumberStyles.None, CultureInfo.InvariantCulture, out var slot) && slot < MaxSlots)
            {
                CloseQuietly(slot);
                Say($"pfail {slot} {a[0]} failed: {OneLine(e.Message)}");
            }
            else
                Log($"{a[0]} failed: {OneLine(e.Message)}");
            return true;
        }
    }

    private static bool Command(string line)
    {
        var a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0)
            return true;
        if (a[0] == "quit")
            return false;
        if (a[0] == "drop")
        {
            if (a.Length == 2)
                Drop(a[1]);
            return true;
        }
        if (a.Length < 2 || !int.TryParse(a[1], NumberStyles.None, CultureInfo.InvariantCulture, out var slot) || slot >= MaxSlots)
        {
            Log($"unknown command: {a[0]} ({a.Length} fields)"); // (never the line: a web one holds an address)
            return true;
        }
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
            case "web" when a.Length == 8:
                CloseSlot(slot);
                Web(slot, a[2], (int)Num(a[3]), (int)Num(a[4]), a[5], a[6] == "1", a[7]);
                break;
            case "park" when a.Length == 3 && ShelfName.IsMatch(a[2]):
                Park(slot, a[2]);
                break;
            case "go" when a.Length == 3:
                (Pages[slot] as WebPage)?.Go(a[2]);
                break;
            case "wscroll" when a.Length == 3:
                (Pages[slot] as WebPage)?.Scroll(Math.Min(1, Num(a[2])));
                break;
            case "wplay":
                (Pages[slot] as WebPage)?.Play();
                break;
            case "wpause":
                (Pages[slot] as WebPage)?.Pause();
                break;
            case "wseek" when a.Length == 3:
                (Pages[slot] as WebPage)?.Seek(Num(a[2]));
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
            case "open" or "web":
                Say($"pfail {slot} the game's {a[0]} command has {a.Length} fields, not the browser's: rebuild the Browser folder"); // (never silent: a mismatch once left every screen on "Loading")
                break;
            default:
                Log($"unknown command: {a[0]} ({a.Length} fields)");
                break;
        }
        return true;
    }

    /// <summary>The Browser Screens' request context: one for every page of this run, in memory, so a sign-in on one screen holds on the rest and after a page closes, until the game does.</summary>
    private static RequestContext WebContext => _webContext ??= new RequestContext(new RequestContextSettings { CachePath = "", PersistSessionCookies = false });

    /// <summary>A Browser Screen's page into the slot: the one shelved as <paramref name="shelf"/> if there is one of this size, else a new one at <paramref name="url"/>.</summary>
    private static void Web(int slot, string url, int width, int height, string block, bool ads, string shelf)
    {
        var at = Shelf.FindIndex(s => s.Name == shelf);
        if (at >= 0)
        {
            var kept = Shelf[at].Page;
            Shelf.RemoveAt(at);
            if (kept.Width == width && kept.Height == height)
            {
                Pages[slot] = kept; // (first: if taking it back fails, closing the slot closes it)
                kept.Return(slot, block, ads);
                Log($"page {slot}: back from the shelf ({Shelf.Count} left on it)");
                return;
            }
            kept.Dispose();
            Log($"page {slot}: the shelved page was another size, so a new one opens");
        }
        Pages[slot] = new WebPage(slot, url, width, height, block, ads, WebContext);
    }

    /// <summary>The slot's Browser Screen page onto the shelf, as <paramref name="name"/>. A YouTube page closes: it comes back on the server's clock anyway.</summary>
    private static void Park(int slot, string name)
    {
        if (Pages[slot] is not WebPage page)
        {
            CloseSlot(slot);
            return;
        }
        Pages[slot] = null;
        Drop(name);
        page.Shelve();
        Shelf.Add((name, page));
        while (Shelf.Count > MaxShelf)
            DropAt(0);
        Log($"page {slot}: shelved ({Shelf.Count} on the shelf)");
    }

    private static void Drop(string name)
    {
        var at = Shelf.FindIndex(s => s.Name == name);
        if (at >= 0)
            DropAt(at);
    }

    private static void DropAt(int at)
    {
        var page = Shelf[at].Page;
        Shelf.RemoveAt(at);
        try
        {
            page.Dispose();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log($"a shelved page didn't close: {OneLine(e.Message)}");
        }
    }

    private static void CloseQuietly(int slot)
    {
        try
        {
            CloseSlot(slot);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Pages[slot] = null;
            Log($"page {slot}: close failed: {OneLine(e.Message)}");
        }
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
