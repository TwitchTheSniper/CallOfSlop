using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox's plain HUD (<see cref="GameModeModule.ArenaExtras"/> off): one small line bottom right for what's in your
/// hands, and on the Esc menu what the arena HUD's top-left block used to carry: the host's invite and the keys.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The tool or part in hand, or the gun and its ammo. No strings per frame: the text goes straight into ImGui's frame arena.</summary>
    private void DrawSandboxHeld(Game game, Player local, double now)
    {
        if (game.HarpoonHudLine(local) is { Length: > 0 } line)
            ImGuiSafe.TextColored(Orange, line); // (a line out or a head stuck in you: GameSandboxHarpoon.cs)
        if (game.ArtilleryHudLine(local) is { Length: > 0 } arc)
            ImGuiSafe.TextColored(Orange, arc); // (a shell gun in your seat: where it lands, GameSandboxShells.cs)
        if (game.SandboxToolCardShown)
            return; // (on the tool's screen)
        var hand = game.SandboxHandText(local);
        if (hand.Length > 0)
        {
            ImGuiSafe.TextColored(new float4(1, 1, 1, 1), hand);
            return;
        }
        var w = game.Active;
        if (Game.IsEmptyGun(w.Def))
            return;
        ImGui.Text($"{w.Def.Name}");
        ImGui.SameLine(0, 12 * _scale);
        var reload = game.ReloadProgress(now);
        var ammoCol = w.Ammo == 0 ? Red : new float4(1, 1, 1, 1);
        if (reload >= 0)
            ImGuiSafe.TextColored(Orange, $"RELOADING {reload * 100:0}%");
        else if (game.InfiniteAmmoOn(now))
            ImGuiSafe.TextColored(ammoCol, $"{w.Ammo} | INF");
        else if (w.Reserve >= 0)
            ImGuiSafe.TextColored(ammoCol, $"{w.Ammo} | {w.Reserve}");
        else
            ImGuiSafe.TextColored(ammoCol, $"{w.Ammo} / {w.Def.Mag}");
        if (reload < 0 && w.Def.FireGroups is { } fireGroups && w.Groups is { } groups && FireGroupState.FeedsOf(fireGroups) > 1)
            ImGuiSafe.TextColored(Dim, FeedLine(fireGroups, groups, now)); // each receiver's own magazine (GameFireGroups.cs)
    }

    /// <summary>The Esc menu in Sandbox: how friends join (the host) and the keys.</summary>
    private void DrawPauseSandboxInfo(Game game, Settings s, string joinInfo)
    {
        string K(GameAction a) => Keybinds.Name(s.Bind(a));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MenuButtonWidth * _scale);
        DrawPauseTutorial(game); // skip, resume or replay the induction (UiTutorial.cs)
        if (game.ShellLocal)
        {
            ImGui.Spacing();
            if (ImGui.Button("Leave the robot shell##shell"u8))
                game.ShellAskLeave(); // (wake in the chair: GameRobotShell.cs)
        }
        if (game.IsServer && !game.Net.IsOffline && joinInfo.Length > 0)
        {
            ImGui.Spacing();
            ImGuiSafe.TextColored(Dim, $"Friends join: {joinInfo}");
            if (HostInvite.Length > 0)
            {
                if (ImGui.Button("Copy invite link##pauseinvite"u8))
                    CopyInvite(CallOfSlop.InviteCode.Link(HostInvite), "Invite link copied: send it to your friends.");
                ImGui.SameLine();
                if (ImGui.Button("Copy code##pauseinvite"u8))
                    CopyInvite(HostInvite, "Invite code copied.");
            }
            if (Environment.TickCount64 / 1000.0 < _inviteCopiedUntil)
                ImGuiSafe.TextColored(Green, _inviteCopied);
        }
        ImGui.Spacing();
        ImGuiSafe.TextColored(Dim, $"Esc: menu   {K(GameAction.Loadout)}: loadout   {K(GameAction.SandboxInventory)}: inventory   {K(GameAction.SwapWeapon)}/wheel: switch   {K(GameAction.Aim)}: aim   {K(game.NoclipCreative ? GameAction.Noclip : GameAction.Melee)}: {(game.NoclipCreative ? "noclip" : "melee")}   {K(GameAction.CrouchHold)}: crouch (tap) / prone (hold)   {K(GameAction.ChatAll)}/{K(GameAction.ChatTeam)}: chat");
        ImGui.PopTextWrapPos();
    }
}
