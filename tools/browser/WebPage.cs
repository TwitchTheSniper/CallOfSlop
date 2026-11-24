using System.Net;
using CefSharp;
using CefSharp.Handler;
using CefSharp.OffScreen;

namespace SlopBrowser;

/// <summary>
/// A Browser Screen's page (docs/sandbox/MEDIA.md, "Browser Screens"): any http or https site. It runs on rules of its own, not the YouTube page's, in one of the Browser Screens' two request
/// contexts (<see cref="Program"/>'s <c>WebContext</c>: the player's own screens', or other players'), in memory and shared by the pages of this run that use it, so a sign-in lasts until the
/// game closes and nothing is put on disk.
/// The address can come from another player, so everything here is the same for every peer's copy: only http and https, no private or local addresses, no downloads, no external programs, no popups
/// (a link that wants one opens in place), every permission denied, no dialogs or file pickers. URLs are never logged, and a query string never leaves this process in a report.
/// Out of range, a page goes on the shelf instead of closing (<see cref="Shelve"/>): hidden and unheard, but alive, so it comes back as it was.
/// </summary>
internal sealed class WebPage : IDisposable
{
    private readonly PageSink _sink;
    private readonly Painter _painter;
    private readonly ChromiumWebBrowser _web;
    private volatile bool _closing;
    private volatile string _address = "", _title = ""; // (said again when the page comes off the shelf)
    private volatile int _seen; // goes up when the page comes off the shelf: its watch says where it is again
    private readonly AdSwitch _ads;

    public int Width => _sink.Width;

    public int Height => _sink.Height;

    /// <summary>In the player's own request context, not other players'.</summary>
    public readonly bool Mine;

    public WebPage(int slot, string url, int width, int height, string block, bool ads, bool mine, RequestContext context)
    {
        Mine = mine;
        _ads = new AdSwitch(ads);
        _sink = new PageSink(new SharedBlock(block, width, height), slot);
        _web = new ChromiumWebBrowser(WebPolicy.Allowed(url) ? url : "about:blank", requestContext: context, automaticallyCreateBrowser: false)
        {
            RenderHandler = _painter = new Painter(_sink),
            AudioHandler = new PageSound(_sink),
            RequestHandler = new Requests(new Resources(_ads)),
            LifeSpanHandler = new InPlace(),
            DisplayHandler = new Quiet(),
            DownloadHandler = new NoDownloads(),
            PermissionHandler = new NoPermissions(),
            DialogHandler = new NoFiles(),
            JsDialogHandler = new NoDialogs(),
        };
        _web.AddressChanged += (_, e) =>
        {
            _address = e.Address;
            _sink.Say("u", WebPolicy.Shareable(e.Address));
        };
        _web.TitleChanged += (_, e) =>
        {
            _title = Program.OneLine(e.Title);
            _sink.Say("t", _title);
        };
        _web.LoadError += (_, e) =>
        {
            if (e.Frame.IsMain && e.ErrorCode is not (CefErrorCode.Aborted or CefErrorCode.BlockedByClient))
                _sink.Say("pfail", $"the page didn't load ({e.ErrorCode})"); // (never the address)
        };
        _web.FrameLoadEnd += (_, e) =>
        {
            if (e.Frame.IsMain && e.HttpStatusCode > 0)
                _sink.Say("pok", ""); // (a page that loaded after one that didn't: its panel's fault goes)
        };
        _web.Size = new System.Drawing.Size(width, height);
        new Thread(() => PageInput.Run(_sink, _web, keys: true, () => _closing, () => { })) { IsBackground = true, Name = "Input " + slot }.Start();
        _ = Watch();
        _web.CreateBrowser(null, new BrowserSettings { WindowlessFrameRate = 30 });
    }

    /// <summary>
    /// Its screen went out of range: off the screen and onto the shelf. No picture, sound, input or report, and the block goes back to the game, but the page itself runs on as it was,
    /// signed in, at its place, its video going (unheard), until it comes back (<see cref="Return"/>) or is dropped.
    /// </summary>
    public void Shelve()
    {
        _sink.Detach()?.Dispose();
        if (_web.IsBrowserInitialized)
            _web.GetBrowserHost()?.WasHidden(true); // (no paints while nobody sees it)
    }

    /// <summary>Off the shelf onto a screen in <paramref name="slot"/>: says where it is and what it's called, and paints a whole frame at once.</summary>
    public void Return(int slot, string block, bool ads)
    {
        _painter.Again();
        _seen++;
        // (said as it goes on the screen, under the sink's lock: an address the page moves to meanwhile is said after it, never before)
        _sink.Attach(new SharedBlock(block, _sink.Width, _sink.Height), slot, () =>
        {
            if (_address.Length > 0)
                Program.Say($"u {slot} {WebPolicy.Shareable(_address)}");
            if (_title.Length > 0)
                Program.Say($"t {slot} {_title}");
        });
        if (_web.IsBrowserInitialized && _web.GetBrowserHost() is { } host)
        {
            host.WasHidden(false);
            host.Invalidate(PaintElementType.View);
        }
        Ads(ads);
    }

    private const string FindScroller = """
        function scroller() {
          var d = document, s = d.scrollingElement || d.documentElement;
          if (s && s.scrollHeight - s.clientHeight > 2) return s;
          var best = null, most = 2;
          d.querySelectorAll('main, [role=main], #content, #app, #root, #__next, main > *, #app > *, #root > *').forEach(function (e) {
            var o = getComputedStyle(e).overflowY, m = e.scrollHeight - e.clientHeight;
            if ((o == 'auto' || o == 'scroll') && m > most) { best = e; most = m; }
          });
          return best || s;
        }
        function medium() {
          var best = null, most = -1;
          document.querySelectorAll('video, audio').forEach(function (e) {
            if (!(e.duration > 0)) return;
            var r = e.getBoundingClientRect(), a = (e.paused ? 0 : 1e7) + r.width * r.height;
            if (a > most) { best = e; most = a; }
          });
          return best;
        }
        """;

    private const string WatchScript = "(function () {" + FindScroller + """
          var s = scroller(), m = s ? s.scrollHeight - s.clientHeight : 0, v = medium();
          return (m > 2 ? s.scrollTop / m : 0).toFixed(5) + ' ' + (v ? 1 : 0) + ' ' + (v && v.paused ? 1 : 0) + ' ' + (v ? v.currentTime.toFixed(2) : 0);
        })()
        """;

    /// <summary>
    /// Four times a second: where the page's main scroller is, and its main video or audio. Reading these needs only the top frame, so a player inside another site's frame isn't seen. No text of the page's goes anywhere.
    /// </summary>
    private async Task Watch()
    {
        var last = "";
        var seen = _seen;
        while (!_closing)
        {
            await Task.Delay(250);
            if (_closing || !_web.IsBrowserInitialized || _sink.Slot < 0) // (nobody asks about a shelved page)
                continue;
            if (seen != _seen)
            {
                seen = _seen;
                last = ""; // (the game forgot it when its screen took it back)
            }
            try
            {
                var r = await _web.GetMainFrame().EvaluateScriptAsync(WatchScript);
                if (r.Success && r.Result is string { Length: > 0 } now && now != last)
                {
                    last = now;
                    _sink.Say("w", now);
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // (the page is between documents)
            }
        }
    }

    private void InTop(string script)
    {
        if (_web.IsBrowserInitialized)
            _web.GetMainFrame().ExecuteJavaScriptAsync("(function () {" + FindScroller + script + "})()");
    }

    public void Scroll(double share) =>
        InTop(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"var s = scroller(); if (s) s.scrollTop = {share:0.#####} * (s.scrollHeight - s.clientHeight);"));

    public void Play() => InTop("var v = medium(); if (v) v.play();");

    public void Pause() => InTop("var v = medium(); if (v) v.pause();");

    public void Seek(double seconds) =>
        InTop(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"var v = medium(); if (v) v.currentTime = {seconds:0.##};"));

    /// <summary>Turns ad blocking on or off, and reloads.</summary>
    public void Ads(bool on)
    {
        if (_ads.On == on)
            return;
        _ads.On = on;
        Reload();
    }

    public void Go(string url)
    {
        if (WebPolicy.Allowed(url) && _web.IsBrowserInitialized)
            _web.GetMainFrame().LoadUrl(url);
    }

    public void Back()
    {
        if (_web.IsBrowserInitialized)
            _web.GetBrowser()?.GoBack();
    }

    public void Forward()
    {
        if (_web.IsBrowserInitialized)
            _web.GetBrowser()?.GoForward();
    }

    public void Reload()
    {
        if (_web.IsBrowserInitialized)
            _web.GetBrowser()?.Reload();
    }

    public void Dispose()
    {
        _closing = true;
        var block = _sink.Detach(); // (a paint or a packet in flight finishes first, and none comes after)
        _web.Dispose();
        block?.Dispose(); // (the request context is the run's: the next page signs in with it)
    }

    private sealed class Requests(IResourceRequestHandler resources) : RequestHandler
    {
        protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect) =>
            !(WebPolicy.Allowed(request.Url) || request.Url == "about:blank" || !frame.IsMain && request.Url == "about:srcdoc"); // (true cancels)

        protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling) =>
            resources;

        protected override bool GetAuthCredentials(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, bool isProxy, string host, int port, string realm, string scheme, IAuthCallback callback) => false;
    }

    /// <summary>What a page loads, as well as where it goes: a public site can't reach into the viewer's own network either.</summary>
    private sealed class Resources(AdSwitch ads) : ResourceRequestHandler
    {
        protected override CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback) =>
            WebPolicy.Resource(request.Url) && !(ads.On && AdFilter.Blocks(request.Url)) ? CefReturnValue.Continue : CefReturnValue.Cancel;
    }

    /// <summary>A popup opens in place, if a click asked for it. Anything else is dropped.</summary>
    private sealed class InPlace : LifeSpanHandler
    {
        protected override bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
        {
            newBrowser = null;
            if (userGesture && WebPolicy.Allowed(targetUrl))
                browser.MainFrame.LoadUrl(targetUrl);
            return true;
        }
    }

    private sealed class Quiet : DisplayHandler
    {
        protected override bool OnConsoleMessage(IWebBrowser chromiumWebBrowser, ConsoleMessageEventArgs e) => true; // (a page's console can hold anything: it goes nowhere)
    }

    private sealed class NoDownloads : DownloadHandler
    {
        protected override bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod) => false;
    }

    private sealed class NoPermissions : PermissionHandler
    {
        protected override bool OnRequestMediaAccessPermission(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string requestingOrigin, MediaAccessPermissionType requestedPermissions, IMediaAccessCallback callback)
        {
            callback.Cancel();
            return true;
        }

        protected override bool OnShowPermissionPrompt(IWebBrowser chromiumWebBrowser, IBrowser browser, ulong promptId, string requestingOrigin, PermissionRequestType requestedPermissions, IPermissionPromptCallback callback)
        {
            callback.Continue(PermissionRequestResult.Deny);
            return true;
        }
    }

    private sealed class NoFiles : IDialogHandler
    {
        public bool OnFileDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, CefFileDialogMode mode, string title, string defaultFilePath, IReadOnlyCollection<string> acceptFilters, IReadOnlyCollection<string> acceptExtensions, IReadOnlyCollection<string> acceptDescriptions, IFileDialogCallback callback)
        {
            callback.Cancel();
            return true;
        }
    }

    private sealed class NoDialogs : IJsDialogHandler
    {
        public bool OnJSDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, CefJsDialogType dialogType, string messageText, string defaultPromptText, IJsDialogCallback callback, ref bool suppressMessage)
        {
            suppressMessage = true;
            return false;
        }

        public bool OnBeforeUnloadDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, string messageText, bool isReload, IJsDialogCallback callback)
        {
            callback.Continue(true);
            return true;
        }

        public void OnResetDialogState(IWebBrowser chromiumWebBrowser, IBrowser browser) { }

        public void OnDialogClosed(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
    }
}

/// <summary>What a Browser Screen may touch. The game checks the same before an address gets this far (Source/Media/MediaEntry.cs); keep the two alike.</summary>
internal static class WebPolicy
{
    public static bool Allowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.UserInfo.Length == 0 && !Private(u);

    /// <summary>A resource: the web, and what a page makes for itself. Never a file, an internal page or the viewer's network.</summary>
    public static bool Resource(string url) =>
        url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) || url == "about:blank" || Allowed(url) || Socket(url);

    private static bool Socket(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "ws" or "wss" && !Private(u);

    /// <summary>An address as it may be told to the game and other players: no user name or password in it, and no parameter that looks like a secret.</summary>
    public static string Shareable(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.UserInfo.Length == 0 ? CallOfSlop.WebScrub.Clean(u.AbsoluteUri) : "";

    private static bool Private(Uri u)
    {
        var host = u.IdnHost.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || !host.Contains('.') && !host.Contains(':') || host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal)
            || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal) || host.EndsWith(".home.arpa", StringComparison.Ordinal))
            return true;
        return IPAddress.TryParse(host, out var ip) && PrivateIp(ip);
    }

    private static bool PrivateIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 4)
            return b[0] is 0 or 10 or 127 || b[0] == 100 && b[1] >= 64 && b[1] < 128 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] >= 16 && b[1] < 32 || b[0] == 192 && b[1] == 168 || b[0] >= 224;
        return IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6None) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (b[0] & 0xFE) == 0xFC || b[0] == 0xFF;
    }
}
