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
    private string _title = "";

    public Page(int slot, string id, double seconds, int width, int height, string block)
    {
        _slot = slot;
        _block = new SharedBlock(block, width, height);
        var html = Html(id, seconds, width, height);
        _web = new ChromiumWebBrowser(Address, new BrowserSettings { WindowlessFrameRate = 30 }, automaticallyCreateBrowser: false)
        {
            RenderHandler = new Painter(_block),
            AudioHandler = new Sound(_block),
            RequestHandler = new Requests(html),
            LifeSpanHandler = new NoPopups(),
            DisplayHandler = new Console_(this),
            DialogHandler = null,
            MenuHandler = null,
        };
        _web.Size = new System.Drawing.Size(width, height);
        _web.CreateBrowser();
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
    }

    public void Dispose()
    {
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
          var d = player.getVideoData && player.getVideoData();
          if (d && d.title) say("title " + d.title);
        }
        window.slop = {
          play: function () { if (player && player.playVideo) player.playVideo(); },
          pause: function () { if (player && player.pauseVideo) player.pauseVideo(); },
          seek: function (s) { if (player && player.seekTo) player.seekTo(s, true); }
        };
        function onYouTubeIframeAPIReady() {
          player = new YT.Player("p", {
            host: "https://www.youtube-nocookie.com", width: {{width}}, height: {{height}}, videoId: "{{id}}",
            playerVars: { autoplay: 1, start: {{(long)seconds}}, playsinline: 1, enablejsapi: 1, origin: "{{Origin}}" },
            events: {
              onReady: function (e) { e.target.playVideo(); report(); },
              onStateChange: report,
              onError: function (e) { error = e.data; report(); }
            }
          });
        }
        setInterval(report, 1000);
        </script>
        <script src="https://www.youtube.com/iframe_api"></script>
        </body></html>
        """);

    /// <summary>Frames from the offscreen browser.</summary>
    private sealed class Painter(SharedBlock block) : IRenderHandler
    {
        public ScreenInfo? GetScreenInfo() => new ScreenInfo { DeviceScaleFactor = 1 };
        public Rect GetViewRect() => new(0, 0, block.Width, block.Height);
        public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
        {
            screenX = viewX;
            screenY = viewY;
            return false;
        }
        public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, AcceleratedPaintInfo acceleratedPaintInfo) { }
        public void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
        {
            if (type == PaintElementType.View)
                block.WriteFrame(buffer, width, height);
        }
        public void OnCursorChange(IntPtr cursor, CursorType type, CursorInfo customCursorInfo) { }
        public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y) => false;
        public void UpdateDragCursor(DragOperationsMask operation) { }
        public void OnPopupShow(bool show) { }
        public void OnPopupSize(Rect rect) { }
        public void OnImeCompositionRangeChanged(CefSharp.Structs.Range selectedRange, Rect[] characterBounds) { }
        public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode) { }
        public void Dispose() { }
    }

    /// <summary>The sound: with this handler CEF sends a page's audio here, not to the speakers.</summary>
    private sealed class Sound(SharedBlock block) : IAudioHandler
    {
        private int _channels;
        public bool GetAudioParameters(IWebBrowser chromiumWebBrowser, IBrowser browser, ref AudioParameters parameters)
        {
            parameters.SampleRate = 44100; // (the game's rate: Audio.SampleRate)
            parameters.ChannelLayout = ChannelLayout.LayoutStereo;
            return true;
        }
        public void OnAudioStreamStarted(IWebBrowser chromiumWebBrowser, IBrowser browser, AudioParameters parameters, int channels) => _channels = channels;
        public void OnAudioStreamPacket(IWebBrowser chromiumWebBrowser, IBrowser browser, IntPtr data, int noOfFrames, long pts) => block.WriteAudio(data, noOfFrames, _channels);
        public void OnAudioStreamStopped(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
        public void OnAudioStreamError(IWebBrowser chromiumWebBrowser, IBrowser browser, string errorMessage) => Program.Say("fail audio: " + Program.OneLine(errorMessage));
        public void Dispose() { }
    }

    /// <summary>Answers the page's own address, and keeps the top frame on it.</summary>
    private sealed class Requests(string html) : RequestHandler
    {
        private readonly byte[] _body = Encoding.UTF8.GetBytes(html);

        protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect) =>
            frame.IsMain && !request.Url.StartsWith(Address, StringComparison.Ordinal); // (true cancels)

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
