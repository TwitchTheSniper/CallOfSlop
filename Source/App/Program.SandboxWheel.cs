using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox tool wheel on this PC (SandboxToolWheel.cs; docs/sandbox/CONSTRUCTS.md, "The tool wheel"): hold the tool
/// wheel key, flick the mouse to a tool (further out onto its outer ring for one of its options), let go. While it is open the view stands still (the mouse moves the wheel's
/// pointer instead) and the mouse buttons don't reach the gun or the tools; walking carries on.
/// </summary>
public static partial class Program
{
    private static readonly SandboxToolWheel _toolWheel = new();

    /// <summary>
    /// A match starts or the mode switches (Game.TakeMatchReset, board item ms01): this PC's mode UI goes with it: the quick
    /// inventory, the Gun Workbench window, the tool wheel and the recent-parts wheel close, and the Sandbox hint lines clear.
    /// Settings, keybinds and the loadout screen's choices stay.
    /// </summary>
    private static void ResetLocalUiForMatch()
    {
        Ui.CloseInventory();
        if (Ui.WorkbenchOpen)
            Ui.CloseBench();
        if (_toolWheel.Menu.IsOpen)
            _toolWheel.Menu.Close();
        if (_snapWheel.Menu.IsOpen)
            _snapWheel.Menu.Close();
        _game!.SandboxWeldHint = "";
    }

    /// <summary>The tool wheel is open this frame (the view is frozen).</summary>
    private static bool ToolWheelOpen => _toolWheel.Menu.IsOpen;

    /// <summary>Every frame before the mouse look: feeds the wheel the key and the mouse; returns true while it is open.</summary>
    private static bool ToolWheelInput(double2 delta)
    {
        if (_game is not { SandboxOn: true } g)
        {
            if (_toolWheel.Menu.IsOpen)
                _toolWheel.Menu.Close();
            return false;
        }
        g.SandboxWheelKey = Keybinds.NameOf(_settings, GameAction.ToolWheel); // (for the HUD's key line; each name is made once)
        if (!_toolWheel.Menu.IsOpen && Pressed(GameAction.ToolWheel))
            _toolWheel.SetKeys(_settings); // (the hints follow rebinding; remade only when the binds changed)
        var blocked = !_mouseCaptured || MenusOpen || Ui.ChatOpen;
        if (_toolWheel.OptionsOf(SandboxToolWheel.Slot.Linker) == null)
            _toolWheel.SetOptions(SandboxToolWheel.Slot.Linker, _linkWheel); // (the linking tool's outer ring: link types, edit modes)
        if (_toolWheel.OptionsOf(SandboxToolWheel.Slot.Welder) == null)
            _toolWheel.SetOptions(SandboxToolWheel.Slot.Welder, _weldModes); // (the welder's: socket or freeform weld, board item fw01)
        if (_toolWheel.OptionsOf(SandboxToolWheel.Slot.Paint) == null)
            _toolWheel.SetOptions(SandboxToolWheel.Slot.Paint, _paintWheel); // (the paint tool's: modes, swatches, custom, recent colours; board item pp01)
        var now = Environment.TickCount64;
        var dt = Math.Clamp((now - _lkWheelLast) / 1000f, 0f, 0.1f);
        _lkWheelLast = now;
        // The linking tool's port picker (an output or input with several ports) has the mouse while it is open.
        if (!_toolWheel.Menu.IsOpen && g.Local is { } who && g.SandboxHands.Hand(who.Id).Tool == ConstructTool.Linker && !g.SandboxHands.Hand(who.Id).Seated
            && LinkWheelInput(g, new Vector2((float)delta.X, (float)delta.Y), blocked, dt))
        {
            _down[(int)GameAction.Aim] &= _lkPicker.IsOpen; // (the picker takes RMB as its cancel)
            if (!_lkPicker.IsOpen)
                _down[(int)GameAction.Fire] = false;
            return true;
        }
        if (!_toolWheel.Menu.IsOpen && SnapWheelStep(g, delta, blocked, dt))
        {
            _down[(int)GameAction.Fire] = false; // (the recent-parts wheel has the mouse)
            _down[(int)GameAction.Aim] = false;
            return true;
        }
        var open = _toolWheel.Step(g, Down(GameAction.ToolWheel), new Vector2((float)delta.X, (float)delta.Y), blocked, dt);
        if (_toolWheel.Tapped)
        {
            _toolWheel.Tapped = false;
            SnapTapped();
        }
        if (open)
        {
            // The mouse belongs to the wheel: no shooting, aiming, fitting or taking off meanwhile.
            _down[(int)GameAction.Fire] = false;
            _down[(int)GameAction.Aim] = false;
        }
        return open;
    }

    /// <summary>The open tool wheel over the HUD.</summary>
    private static void DrawToolWheelInMatch()
    {
        _toolWheel.Draw(Ui.Scale);
        _snapWheel.Draw(Ui.Scale);
        DrawLinkWheelInMatch();
    }

    private static long _lkWheelLast;
}
