using Brutal.ImGuiApi;
using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The noclip key and its HUD note (creative Sandbox; Game.ToggleNoclip, docs/sandbox/CONSTRUCTS.md "Noclip"). V is both
/// Melee and Noclip by default (<see cref="Keybinds.Overlap"/>): where noclip is on offer the key flies and doesn't swing,
/// everywhere else it swings as it always did.
/// </summary>
public static partial class Program
{
    /// <summary>The melee key, unless noclip has it (creative Sandbox, the two on one key: a melee binding noclip doesn't share still swings; the one-shot actions of the main input).</summary>
    private static void MeleeInput(Game g, double now)
    {
        if (g.NoclipCreative ? PressedNotOn(GameAction.Melee, GameAction.Noclip) : Pressed(GameAction.Melee))
            g.TryMelee(now);
    }

    /// <summary>What the movement keys mean in flight (noclip, or a worn MMU in vacuum): Crouch is held for down, not toggled, and nothing is left toggled on landing.</summary>
    private static Buttons NoclipButtons(Game g, Buttons b)
    {
        if (g.Local is not { Sim.Noclip: true } && !g.MmuSteering)
            return b;
        if (g.MmuSteering && _mouseCaptured && !SpectateOn)
        {
            // A worn MMU flies as noclip does (Karl, 2026-11-21): Jump is up and Crouch is down, both held. The walker's
            // crouch toggle left the pack burning down after a tap, and Jump while "crouched" only stood them up, so the
            // up and down jets seemed to fire the wrong way or not at all.
            _holdJumpUntilRelease = false;
            if (Down(GameAction.Jump))
                b |= Buttons.Jump;
        }
        _crouchToggle = _proneToggle = false;
        _stanceKeyAt = -1;
        b &= ~(Buttons.Crouch | Buttons.Prone);
        return Down(GameAction.CrouchHold) || Down(GameAction.CrouchToggle) || Down(GameAction.Prone) ? b | Buttons.Crouch : b;
    }

    /// <summary>How far (m) the Board inside key reaches along the look.</summary>
    private const float NoclipBoardReach = 2000f;

    /// <summary>The Board inside key: the construct under the crosshair (or the one whose frame they fly in), and where the look met it.</summary>
    private static void NoclipBoardInput(Game g, Player me)
    {
        SandboxAimRay(g, me, out var eye, out var dir);
        Construct? c = null;
        var at = Vector3.Zero;
        if (g.Constructs.Raycast(eye, dir, NoclipBoardReach, out var hit) && hit.Body is { PartCount: > 0 } aimed)
            (c, at) = (aimed, aimed.ToLocal(hit.Point));
        else if (me.Sim.RideBody != 0 && g.Constructs.Get(me.Sim.RideBody) is { } ridden)
            (c, at) = (ridden, ridden.ToLocal(me.Position));
        if (c == null)
        {
            AudioSystem.Effect(Sfx.HitTick, 0.5f);
            return;
        }
        g.NoclipBoardAsk(c.Id, at);
    }

    private static readonly float4 NoclipColour = new(1f, 0.82f, 0.3f, 0.95f);

    /// <summary>The note under the crosshair's top while flying (from the Sandbox ImGui pass).</summary>
    private static string _boardHint = "";
    private static int _boardHintKey = int.MaxValue; // (no key has this code: the first call makes the note)

    /// <summary>The note under NOCLIP: the Board inside key (made again only when the key changes: no string a frame).</summary>
    private static string NoclipBoardHint()
    {
        var key = _settings.Bind(GameAction.NoclipBoard);
        if (key != _boardHintKey)
            (_boardHintKey, _boardHint) = (key, $"{Keybinds.Name(key)}: board the ship you aim at, inside");
        return _boardHint;
    }

    private static void DrawNoclipHud()
    {
        if (_game is not { SandboxOn: true, Local: { Alive: true, Sim.Noclip: true } } || _wsNoHands || Ui.CommandOpen || SpectateOn) // (the window tool's --no-hands: a scene picture shows no flying note)
            return;
        var size = ImGui.GetIO().DisplaySize;
        var fast = _down[(int)GameAction.Sprint];
        var text = fast ? "NOCLIP - FAST"u8 : "NOCLIP"u8;
        var s = Ui.Scale;
        var draw = ImGui.GetForegroundDrawList();
        draw.AddText(new float2(size.X * 0.5f - (fast ? 62 : 30) * s, 64 * s), ImGui.GetColorU32(NoclipColour), text);
        if (_settings.Bind(GameAction.NoclipBoard) != Keybinds.Unbound)
        {
            var hint = NoclipBoardHint();
            draw.AddText(new float2(size.X * 0.5f - hint.Length * 3.5f * s, 82 * s), ImGui.GetColorU32(NoclipColour with { W = 0.7f }), hint);
        }
    }
}
