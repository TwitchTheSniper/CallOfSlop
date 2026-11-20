using Brutal.ImGuiApi;
using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Creative Sandbox's spectate camera on this PC: a free camera that leaves the body where it stands, so a player can fly
/// round a build and look at it from any side, through walls too. Nothing is sent: the body's inputs are off (no moves, no
/// tools, no shots), so the server and everyone else just see the player standing still. The camera stays within
/// <see cref="SpectateRadius"/> of the body's eye, as an offset from it, so it follows a body riding a moving ship. The
/// mouse turns it, the move keys fly it (Jump up, Crouch down, Sprint faster) and the wheel sets its speed. The key again,
/// being hit, death, a seat or leaving creative puts the view back in the body.
/// </summary>
public static partial class Program
{
    /// <summary>How far (m) the camera may go from the body's eye.</summary>
    public const float SpectateRadius = 48f;

    private const float SpectateSpeedMin = 1f, SpectateSpeedMax = 40f, SpectateSpeedDefault = 8f, SpectateSprint = 3f;

    private static bool _scOn;
    private static float _scYaw, _scPitch, _scSpeed = SpectateSpeedDefault;
    private static int _scHealth;
    private static Vector3 _scOffset; // (the camera from the body's eye)
    private static Vector3 _scCam, _scFwd;

    /// <summary>Is the spectate camera on?</summary>
    public static bool SpectateOn => _scOn;

    /// <summary>Back to the body (also from <see cref="SandboxLocalReset"/>).</summary>
    internal static void SpectateEnd() => _scOn = false;

    /// <summary>May the local player take the camera out now?</summary>
    private static bool SpectateAllowed(Game g, Player me) => g.NoclipCreative && g.Phase == MatchPhase.Playing && me.Alive && !g.SandboxSeatedHere && !GantryOn && !InLobbyRoom;

    /// <summary>Each frame, before the mouse look: the key, the reasons to end it, and the flight. The mouse is <see cref="SpectateMouse"/>'s.</summary>
    private static void SpectateInput(Game g)
    {
        if (g.Local is not { } me || !SpectateAllowed(g, me))
        {
            _scOn = false;
            return;
        }
        if (_mouseCaptured && !Ui.ChatOpen && Pressed(GameAction.SpectateCam))
        {
            if (_scOn)
                _scOn = false;
            else
            {
                _scOn = true;
                _scYaw = g.LookYaw;
                _scPitch = g.LookPitch;
                _scOffset = Vector3.Zero;
                _scHealth = me.Health;
                PhysGunIdle(); // (the physics gun's local aids go; the trigger is off while the camera is out, Program.cs)
                if (_toolWheel.Menu.IsOpen)
                    _toolWheel.Menu.Close();
                if (_snapWheel.Menu.IsOpen)
                    _snapWheel.Menu.Close();
            }
        }
        if (!_scOn)
            return;
        if (me.Health < _scHealth)
        {
            _scOn = false;
            g.SandboxSay("Spectate: you were hit");
            return;
        }
        _scHealth = me.Health;
        if (!_mouseCaptured || Ui.ChatOpen || MenusOpen)
            return;
        var fwd = g.LookDir(_scYaw, _scPitch);
        var up = GravityFrames.UpAt(me.RenderEye);
        var right = Vector3.Cross(fwd, up);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.Normalize(Vector3.Cross(g.LookDir(_scYaw, 0), up));
        var move = (Down(GameAction.MoveForward) ? fwd : default) - (Down(GameAction.MoveBack) ? fwd : default)
                   + (Down(GameAction.MoveRight) ? right : default) - (Down(GameAction.MoveLeft) ? right : default)
                   + (Down(GameAction.Jump) ? up : default) - (Down(GameAction.CrouchHold) || Down(GameAction.CrouchToggle) ? up : default);
        if (move.LengthSquared() < 1e-6f)
            return;
        var speed = _scSpeed * (Down(GameAction.Sprint) ? SpectateSprint : 1f);
        _scOffset += Vector3.Normalize(move) * speed * (float)FrameDt;
        if (_scOffset.Length() > SpectateRadius)
            _scOffset = Vector3.Normalize(_scOffset) * SpectateRadius; // (held at the edge: the camera slides round it)
    }

    /// <summary>The mouse turns the camera, not the body. True when it took the mouse (nothing else looks round).</summary>
    private static bool SpectateMouse(double2 delta)
    {
        if (!_scOn)
            return false;
        if (_mouseCaptured && !Ui.ChatOpen)
        {
            var sens = BaseSensitivity * _settings.Sensitivity;
            var dy = (float)delta.Y * (_settings.InvertY ? -1 : 1);
            _scYaw = World.WrapAngle(_scYaw + (float)delta.X * sens);
            _scPitch = Math.Clamp(_scPitch - dy * sens, -1.55f, 1.55f);
        }
        return true;
    }

    /// <summary>The wheel sets the flying speed.</summary>
    private static bool SpectateScroll(double y)
    {
        if (!_scOn)
            return false;
        _scSpeed = Math.Clamp(_scSpeed * (y > 0 ? 1.25f : 0.8f), SpectateSpeedMin, SpectateSpeedMax);
        return true;
    }

    /// <summary>The body's buttons while the camera is out: none.</summary>
    private static Buttons SpectateButtons(Buttons b) => _scOn ? default : b;

    /// <summary>In the frame: puts the camera where it flew to. True when it did (the body is drawn like anyone's).</summary>
    private static bool SpectateCamera(Player local, ref Vector3 eye, ref Vector3 fwd)
    {
        if (!_scOn || !local.Alive)
            return false;
        _scFwd = _game.LookDir(_scYaw, _scPitch);
        _scCam = local.RenderEye + _scOffset;
        eye = _scCam;
        fwd = _scFwd;
        return true;
    }

    private static string _scHint = "";
    private static int _scHintKey = int.MaxValue; // (no key has this code: the first call makes the note)

    /// <summary>The note under SPECTATE (made again only when the key changes: no string a frame).</summary>
    private static string SpectateHint()
    {
        var key = _settings.Bind(GameAction.SpectateCam);
        if (key != _scHintKey)
            (_scHintKey, _scHint) = (key, $"{Keybinds.Name(key)}: back to your body   Wheel: speed");
        return _scHint;
    }

    /// <summary>The note at the top of the screen while the camera is out (from the Sandbox ImGui pass).</summary>
    private static void DrawSpectateHud()
    {
        if (!_scOn || _game is not { SandboxOn: true } || _wsNoHands || Ui.CommandOpen)
            return;
        var size = ImGui.GetIO().DisplaySize;
        var s = Ui.Scale;
        var draw = ImGui.GetForegroundDrawList();
        var far = _scOffset.Length() > SpectateRadius - 0.5f;
        var text = far ? "SPECTATE - AT THE EDGE"u8 : "SPECTATE"u8;
        draw.AddText(new float2(size.X * 0.5f - (far ? 80 : 32) * s, 64 * s), ImGui.GetColorU32(NoclipColour), text);
        var hint = SpectateHint();
        draw.AddText(new float2(size.X * 0.5f - hint.Length * 3.5f * s, 82 * s), ImGui.GetColorU32(NoclipColour with { W = 0.7f }), hint);
    }
}
