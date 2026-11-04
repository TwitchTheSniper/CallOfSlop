using System.Numerics;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox frame's safety net and its self-test seam (board item dev-crash-sweep-cs01). The whole per-frame Sandbox input
/// (the tool wheel, the physics gun's mouse, the socket weld's mouse, third person, the parts menu key, <see cref="SandboxInput"/>)
/// runs behind one guard: an exception that still gets out is logged once ("[crash]" with the stack) and this PC's tool state
/// is reset, so a bad frame never takes the game down. The host's own Sandbox actions run behind the same kind of guard
/// (<see cref="Game.SandboxActTrips"/>), as a client's packets do behind the packet safety net.
/// The seam (<see cref="SweepFrame"/>, <see cref="SweepDraw"/>) runs exactly that input with scripted keys, outside an ImGui frame,
/// and the match's Sandbox ImGui pass (HUD, panels, wheels) for a caller that opens a headless ImGui frame around it.
/// </summary>
public static unsafe partial class Program
{
    internal static bool SandboxGuardRethrow; // the self-test: an exception reaches the test instead of being logged and recovered
    internal static int SandboxGuardTrips; // how many frames the guard had to recover (the self-test wants none)
    private static bool _sbGuardLogged, _sbDrawGuardLogged;

    // Modifier keys, read straight from the window (none headless; the self-test sets them).
    internal static bool TestShift, TestCtrl, TestZ;
    private static bool KeyHeld(GlfwKey k) => _window != null && _window.GetKey(k) == GlfwKeyAction.Press;
    private static bool ShiftHeld => TestShift || KeyHeld(GlfwKey.LeftShift) || KeyHeld(GlfwKey.RightShift);
    private static bool CtrlHeld => TestCtrl || KeyHeld(GlfwKey.LeftControl) || KeyHeld(GlfwKey.RightControl);
    private static bool ZHeld => TestZ || KeyHeld(GlfwKey.Z);

    /// <summary>The framebuffer size for screen-space picks (the window's; headless, a 1920 x 1080 stand-in).</summary>
    private static Vector2 ScreenSize => _window != null ? new Vector2(_window.FramebufferSize.X, _window.FramebufferSize.Y) : new Vector2(1920, 1080);

    /// <summary>The Sandbox part of the frame's input, behind the guard. Returns what <see cref="SandboxInput"/> returns (false after a recovered frame).</summary>
    private static bool SandboxInputGuarded(double now)
    {
        try
        {
            CrashLog.Phase = "sandbox input";
            var on = SandboxInput(now);
            CrashLog.Phase = "input";
            return on;
        }
        catch (Exception ex) when (!SandboxGuardRethrow)
        {
            SandboxGuardTrips++;
            if (!_sbGuardLogged)
            {
                _sbGuardLogged = true;
                CrashLog.Report("sandbox input frame exception (recovered, the tools were reset)", ex);
            }
            SandboxLocalReset();
            return _session is { SandboxOn: true };
        }
    }

    /// <summary>
    /// This PC's Sandbox tool state goes (a recovered frame, or the self-test between cases): the link being made, the physics
    /// gun's hold, the paint stroke, the socket weld, the cargo drag, the remover's selection, the wheels and the panels.
    /// The server's state (hands, edits) is not touched.
    /// </summary>
    internal static void SandboxLocalReset()
    {
        try
        {
            LinkReset();
            _linkWheel.ResetMode();
            _lkMarkN = 0;
            PhysGunIdle();
            PaintIdle();
            SculptIdle();
            _swFlow.Cancel();
            _pwFlow.Cancel();
            SocketWeldRender.Clear();
            CargoReset();
            if (_session is { } g)
                _remover.Cancel(g);
            if (_toolWheel.Menu.IsOpen)
                _toolWheel.Menu.Close();
            if (_snapWheel.Menu.IsOpen)
                _snapWheel.Menu.Close();
            Ui.CloseSandboxParts();
            Ui.CloseSandboxConfig();
            Ui.CloseSandboxPaintPicker();
            if (Ui.WorkbenchOpen)
                Ui.CloseBench();
            ConstructRender.ClearAim();
            ConstructRender.VolumeHotBody = null;
            _sbFiring = _sbRemoving = false;
            _sbVariant = -1;
            _sbLookKey = -1;
            _sbGroundKey = -1;
            _sbStoreKey = -1;
        }
        catch (Exception ex) when (!SandboxGuardRethrow)
        {
            CrashLog.Report("sandbox reset exception", ex);
        }
    }

    /// <summary>The match's Sandbox ImGui pass (the HUD's look lines, the parts menu, the settings panel, the colour picker, the wheels, the physics gun's HUD), behind a guard.</summary>
    private static void DrawSandboxUiGuarded(double now)
    {
        try
        {
            DrawSandboxPartsInMatch();
            DrawSandboxConfigInMatch(now);
            DrawSandboxPaintInMatch();
            DrawToolWheelInMatch();
            DrawPhysGunHud();
            DrawNoclipHud();
        }
        catch (Exception ex) when (!SandboxGuardRethrow)
        {
            SandboxGuardTrips++;
            if (!_sbDrawGuardLogged)
            {
                _sbDrawGuardLogged = true;
                CrashLog.Report("sandbox UI exception (recovered, the panels were closed)", ex);
            }
            SandboxLocalReset();
        }
    }

    // ---- Self-test seam ("Interaction sweep") -------------------------------------------------------------

    /// <summary>
    /// One frame of this PC's Sandbox input for game <paramref name="g"/> as the player's loop runs it (outside any ImGui frame):
    /// the keys in <paramref name="down"/> are held this frame (pressed if they weren't last frame), the mouse moved by
    /// <paramref name="mouse"/>, the wheel turned by <paramref name="scroll"/> notches. Restores the session afterwards.
    /// </summary>
    internal static void SweepFrame(Game g, double now, ReadOnlySpan<GameAction> down, double2 mouse = default, double scroll = 0)
    {
        var keepSession = _session;
        var keepCaptured = _mouseCaptured;
        _session = g;
        _mouseCaptured = true;
        try
        {
            Array.Copy(_down, _prev, _down.Length);
            Array.Clear(_down);
            foreach (var a in down)
                _down[(int)a] = true;
            _swallowClick = false;
            if (g.Local is { Alive: true } me && ThirdPerson.Active)
            {
                var eye = me.RenderEye;
                var fwd = g.LookDir(g.LookYaw, g.LookPitch);
                ThirdPersonCamera(me, ref eye, ref fwd); // (the render pass's camera, which the aim ray starts from)
            }
            ToolWheelInput(mouse);
            PhysGunMouse(mouse);
            SocketWeldMouse(mouse);
            PointWeldMouse(mouse);
            ThirdPersonInput();
            if (Math.Abs(scroll) > 0.1)
                ScrollInput(scroll);
            SandboxMenuInput();
            SandboxInputGuarded(now);
        }
        finally
        {
            _session = keepSession;
            _mouseCaptured = keepCaptured;
        }
    }

    /// <summary>The match's Sandbox ImGui pass for game <paramref name="g"/> (the caller has called ImGui.NewFrame): the HUD, then the Sandbox panels and wheels.</summary>
    internal static void SweepDraw(Game g, double now)
    {
        var keepSession = _session;
        _session = g;
        try
        {
            Ui.DrawHud(g, now, "", _settings);
            DrawSandboxUiGuarded(now);
            DrawBenchInMatch(now);
        }
        finally
        {
            _session = keepSession;
        }
    }

    /// <summary>All keys up (the frame after a sweep case), and this PC's tool state gone.</summary>
    internal static void SweepRelease(Game g)
    {
        Array.Clear(_down);
        Array.Clear(_prev);
        var keepSession = _session;
        _session = g;
        try
        {
            SandboxLocalReset();
            Ui.MenuOpen = Ui.PauseOpen = false; // (the loadout screen is open on launch: the match's input runs with no menu up)
            ThirdPerson.FootOn = false;
            ThirdPerson.Active = ThirdPerson.HaveCam = false;
            _weldModes.Freeform = false;
        }
        finally
        {
            _session = keepSession;
        }
    }

    internal static SandboxWeldModes SweepWeldModes => _weldModes;
    internal static bool SweepMenusOpen => MenusOpen;
}
