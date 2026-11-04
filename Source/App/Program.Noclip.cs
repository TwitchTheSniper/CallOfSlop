using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The noclip key and its HUD note (creative Sandbox; Game.ToggleNoclip, docs/sandbox/CONSTRUCTS.md "Noclip"). V is both
/// Melee and Noclip by default (<see cref="Keybinds.Overlap"/>): where noclip is on offer the key flies and doesn't swing,
/// everywhere else it swings as it always did.
/// </summary>
public static partial class Program
{
    /// <summary>Does the melee key belong to noclip here (creative Sandbox, the two on one key)?</summary>
    private static bool NoclipHasMeleeKey(Game g) => g.NoclipCreative && _settings.Bind(GameAction.Noclip) == _settings.Bind(GameAction.Melee);

    /// <summary>The melee key, unless noclip has it (the one-shot actions of the main input).</summary>
    private static void MeleeInput(Game g, double now)
    {
        if (Pressed(GameAction.Melee) && !NoclipHasMeleeKey(g))
            g.TryMelee(now);
    }

    /// <summary>What the movement keys mean in flight: Crouch is held for down, not toggled, and nothing is left toggled on landing.</summary>
    private static Buttons NoclipButtons(Game g, Buttons b)
    {
        if (g.Local is not { Sim.Noclip: true })
            return b;
        _crouchToggle = _proneToggle = false;
        _stanceKeyAt = -1;
        b &= ~(Buttons.Crouch | Buttons.Prone);
        return Down(GameAction.CrouchHold) || Down(GameAction.CrouchToggle) || Down(GameAction.Prone) ? b | Buttons.Crouch : b;
    }

    private static readonly float4 NoclipColour = new(1f, 0.82f, 0.3f, 0.95f);

    /// <summary>The note under the crosshair's top while flying (from the Sandbox ImGui pass).</summary>
    private static void DrawNoclipHud()
    {
        if (_game is not { SandboxOn: true, Local: { Alive: true, Sim.Noclip: true } })
            return;
        var size = ImGui.GetIO().DisplaySize;
        var fast = _down[(int)GameAction.Sprint];
        var text = fast ? "NOCLIP - FAST"u8 : "NOCLIP"u8;
        var s = Ui.Scale;
        ImGui.GetForegroundDrawList().AddText(new float2(size.X * 0.5f - (fast ? 62 : 30) * s, 64 * s), ImGui.GetColorU32(NoclipColour), text);
    }
}
