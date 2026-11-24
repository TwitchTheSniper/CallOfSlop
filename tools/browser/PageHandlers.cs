using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;

namespace SlopBrowser;

/// <summary>Frames from the offscreen browser, into the page's <see cref="PageSink"/>. The first one, and a size the game's block can't take, say so in the game's log.</summary>
internal sealed class Painter(PageSink sink) : IRenderHandler
{
    private bool _painted;
    private int _wrongSize;
    private bool _failed; // this painter said pfail: its first right-size frame takes it back

    /// <summary>Back on a screen from the shelf: its first picture there is logged too.</summary>
    public void Again() => _painted = false;

    public ScreenInfo? GetScreenInfo() => new ScreenInfo { DeviceScaleFactor = 1 };
    public Rect GetViewRect() => new(0, 0, sink.Width, sink.Height);
    public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
    {
        screenX = viewX;
        screenY = viewY;
        return false;
    }
    public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, AcceleratedPaintInfo acceleratedPaintInfo) { }
    public void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
    {
        if (type != PaintElementType.View)
            return;
        if (width != sink.Width || height != sink.Height)
        {
            // (a display scale that isn't 1, say; a paint or two while a size settles is no fault, so the panel hears of it only if it goes on)
            if (++_wrongSize == 1)
                Program.Log($"page {sink.Slot}: the browser paints {width} x {height}, not the screen's {sink.Width} x {sink.Height}");
            else if (_wrongSize == 10 && !_painted)
            {
                _failed = true;
                sink.Say("pfail", $"the browser paints {width} x {height}, not the screen's {sink.Width} x {sink.Height}");
            }
            return;
        }
        if (!sink.Paint(buffer, width, height))
            return; // (on the shelf)
        if (_failed)
        {
            _failed = false;
            sink.Say("pok", "");
        }
        if (!_painted)
        {
            _painted = true;
            Program.Log($"page {sink.Slot}: first picture, {width} x {height}");
        }
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
internal sealed class PageSound(PageSink sink) : IAudioHandler
{
    private const long LogEvery = 5;
    private int _channels = 2, _rate = SharedBlock.GameRate;
    private long _packets, _nextLog;
    public volatile bool Started;

    public bool GetAudioParameters(IWebBrowser chromiumWebBrowser, IBrowser browser, ref AudioParameters parameters)
    {
        Program.Log($"page {sink.Slot}: audio handler asked (hop 2): offered {parameters.SampleRate} Hz, {parameters.ChannelLayout}, {parameters.FramesPerBuffer} frames");
        parameters.SampleRate = SharedBlock.GameRate;
        parameters.ChannelLayout = ChannelLayout.LayoutStereo;
        return true;
    }

    public void OnAudioStreamStarted(IWebBrowser chromiumWebBrowser, IBrowser browser, AudioParameters parameters, int channels)
    {
        _channels = channels;
        _rate = parameters.SampleRate;
        Started = true;
        Program.Log($"page {sink.Slot}: audio stream started (hop 3): {channels} channels, {parameters.SampleRate} Hz, {parameters.ChannelLayout}");
    }

    public void OnAudioStreamPacket(IWebBrowser chromiumWebBrowser, IBrowser browser, IntPtr data, int noOfFrames, long pts)
    {
        sink.Audio(data, noOfFrames, _channels, _rate); // (dropped on the shelf)
        _packets++;
        var now = Environment.TickCount64 / 1000;
        if (now < _nextLog || sink.Slot < 0)
            return;
        _nextLog = now + LogEvery;
        var (frames, peak) = sink.AudioLevels();
        Program.Log($"page {sink.Slot}: audio packets (hop 4): {_packets}, {frames} frames into the ring, peak {peak:0.000}");
    }

    public void OnAudioStreamStopped(IWebBrowser chromiumWebBrowser, IBrowser browser) => Program.Log($"page {sink.Slot}: audio stream stopped after {_packets} packets");

    // (never a "fail": that closes every page, and a picture without sound is still a picture)
    public void OnAudioStreamError(IWebBrowser chromiumWebBrowser, IBrowser browser, string errorMessage) => Program.Log($"page {sink.Slot}: audio stream error: {errorMessage}");

    public void Dispose() { }
}

/// <summary>The ad list on a page's requests, with its switch off a pass-through. It cancels or lets go, so it adds no way out of a page's rules.</summary>
internal sealed class AdResources(AdSwitch ads) : CefSharp.Handler.ResourceRequestHandler
{
    protected override CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
    {
        if (!ads.On || !AdFilter.Blocks(request.Url))
            return CefReturnValue.Continue;
        Interlocked.Increment(ref ads.Refused);
        return CefReturnValue.Cancel;
    }
}
