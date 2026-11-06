using System.Globalization;
using System.Text;
using CefSharp;
using CefSharp.Enums;
using CefSharp.Handler;
using CefSharp.OffScreen;
using CefSharp.Structs;

namespace SlopBrowser;

/// <summary>
/// One live screen: an offscreen browser on a small page that holds YouTube's iframe player. The page comes from here and not from a site:
/// its made-up address is answered by <see cref="Requests"/>. The player needs a real https page around it, since it refuses to run
/// without a Referer (error 153), which rules out a file or a data URL. The page can go nowhere else: top-level navigation is
/// refused and popups are closed. Nothing but a checked 11-character id gets into its text.
/// </summary>
internal sealed class Page : IDisposable
{
    public const string Origin = "https://callofslop.invalid", Address = Origin + "/player.html";
    private const string Report = "SLOP ";
    private readonly int _slot;
    private readonly SharedBlock _block;
    private readonly ChromiumWebBrowser _web;
    private readonly PageSound _audioHandler;
    private string _title = "";
    private string _sound = "";
    private long _playingSince, _nextProbe;
    private bool _awake;
    private volatile bool _closing;
    private bool _handsOn;

    public Page(int slot, string id, double seconds, int width, int height, string block)
    {
        _slot = slot;
        _block = new SharedBlock(block, width, height);
        var html = Html(id, seconds, width, height);
        _web = new ChromiumWebBrowser(Address, automaticallyCreateBrowser: false)
        {
            RenderHandler = new Painter(_block),
            AudioHandler = _audioHandler = new PageSound(_block, slot),
            RequestHandler = new Requests(html),
            LifeSpanHandler = new NoPopups(),
            DisplayHandler = new Console_(this),
            DialogHandler = null,
            MenuHandler = null,
        };
        _web.Size = new System.Drawing.Size(width, height);
        new Thread(() => PageInput.Run(_block, _web, keys: false, () => _closing, Hands)) { IsBackground = true, Name = "Mouse " + slot }.Start();
        _web.CreateBrowser(null, new BrowserSettings { WindowlessFrameRate = 30 }); // (settings given to the constructor only count when it creates the browser itself)
    }

    public void Run(string script)
    {
        if (_web.IsBrowserInitialized)
            _web.GetMainFrame().ExecuteJavaScriptAsync("if (window.slop) " + script);
    }

    private void Heard(string message)
    {
        if (!message.StartsWith(Report, StringComparison.Ordinal))
            return;
        var body = message[Report.Length..];
        Wake();
        if (body.StartsWith("audio ", StringComparison.Ordinal))
        {
            if (body != _sound)
            {
                _sound = body;
                Program.Log($"page {_slot}: player {body} (muted, volume)"); // (hop 1: it must come to false 100)
            }
            return;
        }
        if (body.StartsWith("title ", StringComparison.Ordinal))
        {
            var title = Program.OneLine(body[6..]);
            if (title != _title)
            {
                _title = title;
                Program.Say($"t {_slot} {title}");
            }
            return;
        }
        Program.Say($"s {_slot} {body}");
        Probe(body);
    }

    /// <summary>
    /// Playing for a while with no sound stream: says what the video element itself reports. It lives in YouTube's frame, which the page's script can't reach but the host can.
    /// </summary>
    private void Probe(string report)
    {
        if (!report.StartsWith("1 ", StringComparison.Ordinal) || _audioHandler.Started)
        {
            _playingSince = 0;
            return;
        }
        var now = Environment.TickCount64;
        if (_playingSince == 0)
            _playingSince = now;
        if (now - _playingSince < 6000 || now < _nextProbe)
            return;
        _nextProbe = now + 10000;
        if (_web.GetBrowser() is not { } browser)
            return;
        Program.Log($"page {_slot}: playing with no sound stream, report {report}");
        foreach (var frame in browser.GetAllFrames())
            if (frame.Url.Contains("/embed/", StringComparison.Ordinal))
                _ = ProbeFrame(frame);
    }

    private async Task ProbeFrame(IFrame frame)
    {
        try
        {
            const string read = "(function(){var v=document.querySelector('video');return v?'muted '+v.muted+' volume '+v.volume+' paused '+v.paused+' time '+v.currentTime.toFixed(1)+' audio bytes '+v.webkitAudioDecodedByteCount:'no video element';})()";
            var r = await frame.EvaluateScriptAsync(read);
            Program.Log($"page {_slot}: video element: {(r.Success ? r.Result : r.Message)}");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Program.Log($"page {_slot}: video element unread: {e.Message}");
        }
    }

    /// <summary>Once, when the page first speaks: CEF's own mute off, and a page in front. Sound comes only from a page that isn't muted at either end.</summary>
    private void Wake()
    {
        if (_awake)
            return;
        _awake = true;
        var host = _web.GetBrowserHost();
        if (host == null)
            return;
        Program.Log($"page {_slot}: browser muted {host.IsAudioMuted}");
        host.SetAudioMuted(false);
        host.WasHidden(false);
        host.SetFocus(true);
    }

    /// <summary>From the first click the player's volume and mute are the viewer's, and the page stops setting them.</summary>
    private void Hands()
    {
        if (_handsOn)
            return;
        _handsOn = true;
        Run("slop.hands()");
    }

    public void Dispose()
    {
        _closing = true;
        _web.Dispose();
        _block.Dispose();
    }

    private static string Html(string id, double seconds, int width, int height) => string.Create(CultureInfo.InvariantCulture, $$"""
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Call of Slop</title>
        <style>html,body{margin:0;width:100%;height:100%;background:#000;overflow:hidden}</style></head>
        <body><div id="p"></div>
        <script>
        var player, error = 0;
        function say(s) { console.log("SLOP " + s); }
        function report() {
          if (!player || !player.getPlayerState) { say("-2 0 0 " + error); return; }
          say(player.getPlayerState() + " " + player.getCurrentTime().toFixed(2) + " " + player.getDuration().toFixed(2) + " " + error);
          sound();
          var d = player.getVideoData && player.getVideoData();
          if (d && d.title) say("title " + d.title);
        }
        // The player can start muted when nobody clicked anything, and then CEF sends no sound at all (it captures only what is audible).
        var hands = false;
        function sound() {
          if (!player.isMuted) return;
          var m = player.isMuted(), v = player.getVolume();
          if (!hands && (m || v < 100)) { player.unMute(); player.setVolume(100); }
          say("audio " + m + " " + v);
        }
        window.slop = {
          hands: function () { hands = true; },
          play: function () { if (player && player.playVideo) player.playVideo(); },
          pause: function () { if (player && player.pauseVideo) player.pauseVideo(); },
          seek: function (s) { if (player && player.seekTo) player.seekTo(s, true); }
        };
        function onYouTubeIframeAPIReady() {
          player = new YT.Player("p", {
            host: "https://www.youtube-nocookie.com", width: {{width}}, height: {{height}}, videoId: "{{id}}",
            playerVars: { autoplay: 1, mute: 0, start: {{(long)seconds}}, playsinline: 1, enablejsapi: 1, origin: "{{Origin}}" },
            events: {
              onReady: function (e) { e.target.unMute(); e.target.setVolume(100); e.target.playVideo(); report(); },
              onStateChange: report,
              onError: function (e) { error = e.data; report(); }
            }
          });
        }
        setInterval(report, 500);
        </script>
        <script src="https://www.youtube.com/iframe_api"></script>
        </body></html>
        """);

    /// <summary>Answers the page's own address, and keeps the top frame on it. A click inside the player can't leave the embed either, and adverts still load: only a gesture is held to it.</summary>
    private sealed class Requests(string html) : RequestHandler
    {
        private readonly byte[] _body = Encoding.UTF8.GetBytes(html);

        protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect) =>
            frame.IsMain ? !request.Url.StartsWith(Address, StringComparison.Ordinal) : userGesture && !IsEmbed(request.Url); // (true cancels)

        private static bool IsEmbed(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host is "www.youtube-nocookie.com" or "www.youtube.com" && u.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal);

        protected override IResourceRequestHandler? GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling) =>
            request.Url.StartsWith(Address, StringComparison.Ordinal) ? new Local(_body) : null;
    }

    private sealed class Local(byte[] body) : ResourceRequestHandler
    {
        protected override IResourceHandler GetResourceHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request)
        {
            var r = new ResourceHandler("text/html", new MemoryStream(body), autoDisposeStream: true);
            r.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin"); // (the player reads the Referer)
            return r;
        }
    }

    private sealed class NoPopups : LifeSpanHandler
    {
        protected override bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
        {
            newBrowser = null;
            return true;
        }
    }

    private sealed class Console_(Page page) : DisplayHandler
    {
        protected override bool OnConsoleMessage(IWebBrowser chromiumWebBrowser, ConsoleMessageEventArgs e)
        {
            if (e.Source.StartsWith(Address, StringComparison.Ordinal)) // (not the player's own frame)
                page.Heard(e.Message);
            return true;
        }
    }
}
