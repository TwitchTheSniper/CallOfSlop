using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;

namespace SlopBrowser;

/// <summary>Frames from the offscreen browser.</summary>
internal sealed class Painter(SharedBlock block) : IRenderHandler
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
internal sealed class PageSound(SharedBlock block, int slot) : IAudioHandler
{
    private const long LogEvery = 5;
    private int _channels = 2, _rate = SharedBlock.GameRate;
    private long _packets, _nextLog;
    public volatile bool Started;

    public bool GetAudioParameters(IWebBrowser chromiumWebBrowser, IBrowser browser, ref AudioParameters parameters)
    {
        Program.Log($"page {slot}: audio handler asked (hop 2): offered {parameters.SampleRate} Hz, {parameters.ChannelLayout}, {parameters.FramesPerBuffer} frames");
        parameters.SampleRate = SharedBlock.GameRate;
        parameters.ChannelLayout = ChannelLayout.LayoutStereo;
        return true;
    }

    public void OnAudioStreamStarted(IWebBrowser chromiumWebBrowser, IBrowser browser, AudioParameters parameters, int channels)
    {
        _channels = channels;
        _rate = parameters.SampleRate;
        Started = true;
        Program.Log($"page {slot}: audio stream started (hop 3): {channels} channels, {parameters.SampleRate} Hz, {parameters.ChannelLayout}");
    }

    public void OnAudioStreamPacket(IWebBrowser chromiumWebBrowser, IBrowser browser, IntPtr data, int noOfFrames, long pts)
    {
        block.WriteAudio(data, noOfFrames, _channels, _rate);
        _packets++;
        var now = Environment.TickCount64 / 1000;
        if (now < _nextLog)
            return;
        _nextLog = now + LogEvery;
        Program.Log($"page {slot}: audio packets (hop 4): {_packets}, {block.AudioFrames} frames into the ring, peak {block.AudioPeak:0.000}");
        block.AudioPeak = 0;
    }

    public void OnAudioStreamStopped(IWebBrowser chromiumWebBrowser, IBrowser browser) => Program.Log($"page {slot}: audio stream stopped after {_packets} packets");

    // (never a "fail": that closes every page, and a picture without sound is still a picture)
    public void OnAudioStreamError(IWebBrowser chromiumWebBrowser, IBrowser browser, string errorMessage) => Program.Log($"page {slot}: audio stream error: {errorMessage}");

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
