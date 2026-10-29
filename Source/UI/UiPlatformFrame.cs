using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// ImGui's per-frame platform update without the GLFW backend's monitor scan. Brutal's <c>GlfwBackend.NewFrame</c>
/// calls <c>UpdateMonitors</c> on every frame (imgui_impl_glfw only does it when a monitor changes): for each monitor
/// <c>glfwGetMonitorPos</c> and <c>glfwGetVideoMode</c>, which on Windows are two <c>EnumDisplaySettings</c> calls into
/// the display driver and on X11 XRandR round trips to the server. That was most of the UI's time on a fast frame and its
/// spikes (2026-10-29, perf/imgui-spike: the F9 window showed UI 0.41 ms average, 4.7 ms peak at 600 fps; under xvfb
/// the backend's NewFrame was 0.21 ms median and 2.5 ms at worst, of a 0.25 ms "ImGui new frame"). Its mouse update
/// also sets GLFW_MOUSE_PASSTHROUGH on the window every frame (a SetWindowLongW on Windows). The game has no ImGui
/// multi-viewports, so the monitor list is only filled once (by the backend's own first NewFrame) and the rest is done
/// here: display size and scale, the frame's delta time, the mouse fallback and the cursor shape (the backend's own
/// method). Gamepad navigation is off in the game, so the backend's gamepad update would return at once. Input events
/// still come from the backend's GLFW callbacks.
/// <para><c>--imgui-backend-frame</c> keeps the backend's NewFrame every frame (to compare).</para>
/// </summary>
public sealed partial class Ui
{
    /// <summary>Use the backend's own NewFrame every frame (<c>--imgui-backend-frame</c>; the old path, for comparisons).</summary>
    public static bool BackendNewFrame;

    private double _uiTime = -1;
    private int _platformFrames;

    /// <summary>The platform half of <see cref="BeginFrame"/>: the backend's NewFrame on the first frame, then the lean one.</summary>
    private void PlatformNewFrame()
    {
        if (BackendNewFrame || _platformFrames++ == 0 || _window == null || !LeanFrameReady())
        {
            _platform!.NewFrame(); // (fills the monitor list and the backend's clock)
            _uiTime = Glfw.Time;
            return;
        }
        var io = ImGui.GetIO();
        var window = _window;
        var size = window.Size;
        io.DisplaySize = new float2(size.X, size.Y);
        if (size.X > 0 && size.Y > 0)
        {
            var fb = window.FramebufferSize;
            io.DisplayFramebufferScale = new float2(fb.X / (float)size.X, fb.Y / (float)size.Y);
        }
        var now = Glfw.Time;
        io.DeltaTime = _uiTime > 0 && now > _uiTime ? (float)(now - _uiTime) : 1f / 60f;
        _uiTime = now;

        // The mouse: the backend's callbacks give the position while the cursor is over the window (or captured); like
        // imgui_impl_glfw, a focused window also reports it when the cursor is outside (a drag past the edge).
        if (window.GetAttribute(GlfwWindowAttribute.Focused) != 0)
        {
            if (io.WantSetMousePos)
                window.CursorPos = new double2(io.MousePos.X, io.MousePos.Y);
            var c = window.CursorPos;
            if (c.X < 0 || c.Y < 0 || c.X >= size.X || c.Y >= size.Y)
                io.AddMousePosEvent((float)c.X, (float)c.Y);
        }
        _mouseCursor!(); // the cursor shape (the backend's own, private, method: bound once)
    }

    private Action? _mouseCursor;
    private bool _leanChecked;

    /// <summary>
    /// Whether the lean frame can stand in for the backend's: its cursor update binds (a private method of this
    /// package version; if a future one renames it, the backend's NewFrame is used, as before), and no gamepad
    /// navigation or multi-viewports are on (the backend's gamepad and monitor updates are only needed for those).
    /// </summary>
    private bool LeanFrameReady()
    {
        if (!_leanChecked)
        {
            _leanChecked = true;
            var m = typeof(Brutal.ImGuiApi.Backends.GlfwBackend).GetMethod("UpdateMouseCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, Type.EmptyTypes);
            if (m != null && m.ReturnType == typeof(void))
                _mouseCursor = (Action)Delegate.CreateDelegate(typeof(Action), _platform, m);
            else
                Console.WriteLine("[ui] the GLFW backend has no UpdateMouseCursor: using its own NewFrame every frame");
        }
        var flags = ImGui.GetIO().ConfigFlags;
        return _mouseCursor != null && (flags & (ImGuiConfigFlags.NavEnableGamepad | ImGuiConfigFlags.ViewportsEnable)) == 0;
    }
}
