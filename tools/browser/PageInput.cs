using CefSharp;
using CefSharp.OffScreen;

namespace SlopBrowser;

/// <summary>The game's mouse and, on a Browser Screen, keyboard, from the shared block to CEF. Nothing here records or logs a key.</summary>
internal static class PageInput
{
    private const int Move = 1, Down = 2, Up = 3, Wheel = 4, Leave = 5, KeyDown = 6, KeyUp = 7, Char = 8;

    /// <summary>Runs until <paramref name="closing"/>. <paramref name="touched"/> hears a first click or wheel. Keys count only with <paramref name="keys"/>: a YouTube page never takes one.</summary>
    public static void Run(SharedBlock block, ChromiumWebBrowser web, bool keys, Func<bool> closing, Action touched)
    {
        var pressed = false;
        while (!closing())
        {
            if (!block.NextMouse(out var kind, out var x, out var y, out var data))
            {
                Thread.Sleep(8);
                continue;
            }
            if (!web.IsBrowserInitialized || web.GetBrowserHost() is not { } host)
                continue;
            var e = new MouseEvent(x, y, pressed ? CefEventFlags.LeftMouseButton : CefEventFlags.None);
            switch (kind)
            {
                case Move:
                    host.SendMouseMoveEvent(e, false);
                    break;
                case Leave:
                    host.SendMouseMoveEvent(e, true);
                    break;
                case Down:
                    touched();
                    pressed = true;
                    host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.LeftMouseButton), MouseButtonType.Left, false, 1);
                    break;
                case Up:
                    pressed = false;
                    host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.None), MouseButtonType.Left, true, 1);
                    break;
                case Wheel:
                    touched();
                    host.SendMouseWheelEvent(e, 0, data);
                    break;
                case KeyDown or KeyUp or Char when keys:
                    host.SendKeyEvent(new KeyEvent
                    {
                        Type = kind == KeyDown ? KeyEventType.RawKeyDown : kind == KeyUp ? KeyEventType.KeyUp : KeyEventType.Char,
                        WindowsKeyCode = x,
                        NativeKeyCode = 0,
                        Modifiers = (y & 1) != 0 ? CefEventFlags.ShiftDown | Ctrl(y) | Alt(y) : Ctrl(y) | Alt(y),
                    });
                    break;
            }
        }
    }

    private static CefEventFlags Ctrl(int mods) => (mods & 2) != 0 ? CefEventFlags.ControlDown : CefEventFlags.None;

    private static CefEventFlags Alt(int mods) => (mods & 4) != 0 ? CefEventFlags.AltDown : CefEventFlags.None;
}
