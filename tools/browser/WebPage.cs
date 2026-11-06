using System.Net;
using CefSharp;
using CefSharp.Handler;
using CefSharp.OffScreen;

namespace SlopBrowser;

/// <summary>
/// A Browser Screen's page (docs/sandbox/MEDIA.md, "Browser Screens"): any http or https site. It runs on rules of its own, not the YouTube page's, in a request context of its own with nothing on disk.
/// The address can come from another player, so everything here is the same for every peer's copy: only http and https, no private or local addresses, no downloads, no external programs, no popups
/// (a link that wants one opens in place), every permission denied, no dialogs or file pickers. URLs are never logged, and a query string never leaves this process in a report.
/// </summary>
internal sealed class WebPage : IDisposable
{
    private readonly SharedBlock _block;
    private readonly ChromiumWebBrowser _web;
    private readonly RequestContext _context;
    private readonly int _slot;
    private volatile bool _closing;
    private readonly AdSwitch _ads;

    public WebPage(int slot, string url, int width, int height, string block, bool ads)
    {
        _slot = slot;
        _ads = new AdSwitch(ads);
        _block = new SharedBlock(block, width, height);
        _context = new RequestContext(new RequestContextSettings { CachePath = "", PersistSessionCookies = false });
        _web = new ChromiumWebBrowser(WebPolicy.Allowed(url) ? url : "about:blank", requestContext: _context, automaticallyCreateBrowser: false)
        {
            RenderHandler = new Painter(_block),
            AudioHandler = new PageSound(_block, slot),
            RequestHandler = new Requests(new Resources(_ads)),
            LifeSpanHandler = new InPlace(),
            DisplayHandler = new Quiet(),
            DownloadHandler = new NoDownloads(),
            PermissionHandler = new NoPermissions(),
            DialogHandler = new NoFiles(),
            JsDialogHandler = new NoDialogs(),
        };
        _web.AddressChanged += (_, e) => Program.Say($"u {_slot} {WebPolicy.Shareable(e.Address)}");
        _web.TitleChanged += (_, e) => Program.Say($"t {_slot} {Program.OneLine(e.Title)}");
        _web.Size = new System.Drawing.Size(width, height);
        new Thread(() => PageInput.Run(_block, _web, keys: true, () => _closing, () => { })) { IsBackground = true, Name = "Input " + slot }.Start();
        _ = Watch();
        _web.CreateBrowser(null, new BrowserSettings { WindowlessFrameRate = 30 });
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
        while (!_closing)
        {
            await Task.Delay(250);
            if (_closing || !_web.IsBrowserInitialized)
                continue;
            try
            {
                var r = await _web.GetMainFrame().EvaluateScriptAsync(WatchScript);
                if (r.Success && r.Result is string { Length: > 0 } now && now != last)
                {
                    last = now;
                    Program.Say($"w {_slot} {now}");
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
        _web.Dispose();
        _context.Dispose();
        _block.Dispose();
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
