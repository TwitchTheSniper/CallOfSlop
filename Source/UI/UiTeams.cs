using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Team selection UI (rules in Source/Game/TeamPick.cs): Join Blue / Join Red / Auto with both rosters, in the TEAMS
/// window beside the loadout screen (warmup, respawn screen, join in progress), on the pause menu's TEAMS page, and the
/// host's "Team selection" and "Max team size difference" settings on the Host / Single player screens.
/// </summary>
public sealed partial class Ui
{
    private bool _pauseTeams; // the pause menu shows the TEAMS page
    private bool _teamsWindow; // the TEAMS window beside the loadout screen is open
    private bool _teamsAutoOpened; // opened once on its own per warmup

    // ---- Lobby (host settings) -----------------------------------------------------------------

    /// <summary>Host / Single player screens: the team rules, shown for team modes only.</summary>
    private void TeamRulesPicker(Settings s)
    {
        if (!GameModes.IsTeam(s.Mode))
            return;
        var sel = (int)s.TeamSelect;
        ImGui.TextColored(Dim, "Team selection:"u8);
        ImGui.SameLine();
        ImGui.RadioButton("Manual##teamsel"u8, ref sel, (int)TeamSelection.Manual);
        ImGui.SameLine();
        ImGui.RadioButton("Auto only##teamsel"u8, ref sel, (int)TeamSelection.AutoOnly);
        s.TeamSelect = (TeamSelection)sel;
        if (s.TeamSelect == TeamSelection.Manual)
        {
            ImGui.SetNextItemWidth(160 * _scale);
            ImGui.SliderInt("Max team size difference"u8, ref s.TeamMaxDiff, 0, TeamRules.MaxDiffLimit);
            s.TeamMaxDiff = Math.Clamp(s.TeamMaxDiff, 0, TeamRules.MaxDiffLimit);
            ImGuiSafe.TextColored(Dim, $"Players pick Blue, Red or Auto, so friends can play together. A side may get up to {s.TeamMaxDiff} bigger; bots and Auto players even it out.");
        }
        else
            ImGui.TextColored(Dim, "The game splits the teams and keeps them even."u8);
        TeamBalancePicker(ref s.TeamBalance, ref s.HumansTeam, "host");
    }

    /// <summary>
    /// "Team balance: Auto / Off / Humans vs Bots" (TeamBalance.cs), on the Host / Single player screens and in the
    /// lobby room. Returns true when it changed.
    /// </summary>
    private bool TeamBalancePicker(ref TeamBalance balance, ref byte humansTeam, string id)
    {
        var changed = false;
        ImGui.SetNextItemWidth(200 * _scale);
        if (ImGui.BeginCombo($"Team balance##{id}", Game.TeamBalanceName(balance)))
        {
            foreach (var b in TeamBalanceChoices)
                if (ImGui.Selectable(Game.TeamBalanceName(b), b == balance))
                {
                    changed |= b != balance;
                    balance = b;
                }
            ImGui.EndCombo();
        }
        if (balance == TeamBalance.HumansVsBots)
        {
            ImGui.SameLine();
            var t = (int)humansTeam;
            ImGui.RadioButton($"People on Blue##{id}", ref t, 0);
            ImGui.SameLine();
            ImGui.RadioButton($"People on Red##{id}", ref t, 1);
            changed |= t != humansTeam;
            humansTeam = (byte)t;
        }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 620 * _scale);
        ImGuiSafe.TextColored(Dim, balance switch
        {
            TeamBalance.Off => "Off: nobody is moved to even the teams and picks have no size limit, so friends can stack one side. Bots stay where they were put.",
            TeamBalance.HumansVsBots => "Every person on one side, every bot on the other. The bot slider sets how many bots you face.",
            _ => "Auto: bots (and players on Auto) move to keep the sides even.",
        });
        ImGui.PopTextWrapPos();
        return changed;
    }

    private static readonly TeamBalance[] TeamBalanceChoices = [TeamBalance.Auto, TeamBalance.Off, TeamBalance.HumansVsBots];

    // ---- In a match ----------------------------------------------------------------------------

    /// <summary>Loadout footer: the TEAMS button (team modes). Opens or closes the TEAMS window.</summary>
    private void TeamsButton(Game game, float2 size)
    {
        if (!game.IsTeamMode)
            return;
        if (ImGui.Button(_teamsWindow ? "HIDE TEAMS###teams"u8 : "TEAMS###teams"u8, size))
            _teamsWindow = !_teamsWindow;
        ImGui.SameLine();
    }

    /// <summary>The TEAMS window to the right of the loadout screen (opens by itself once per warmup when players can pick).</summary>
    private void DrawTeamsWindow(Game game, double now)
    {
        if (!game.IsTeamMode)
            return;
        if (game.IsWarmup && game.ManualTeams && !_teamsAutoOpened)
        {
            _teamsWindow = true;
            _teamsAutoOpened = true;
        }
        else if (!game.IsWarmup)
            _teamsAutoOpened = false;
        if (!_teamsWindow)
            return;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X - 16 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.12f), ImGuiCond.Always, new float2(1, 0));
        ImGui.SetNextWindowSize(new float2(MathF.Min(440 * _scale, vp.WorkSize.X - 32 * _scale), 0));
        ImGui.SetNextWindowBgAlpha(0.95f);
        if (ImGui.Begin("TEAMS###teamswin"u8, ref _teamsWindow, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                                                        | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize))
            TeamPicker(game, now);
        ImGui.End();
    }

    /// <summary>The pause menu's TEAMS page: the picker, the host's rules, BACK.</summary>
    private void DrawPauseTeams(Game game, Settings s)
    {
        Title("Teams");
        TeamPicker(game, Brutal.GlfwApi.Glfw.Time);
        if (game.IsServer && game.IsTeamMode)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(Orange, "HOST"u8);
            var sel = (int)game.TeamSelection;
            var diff = game.MaxTeamDiff;
            ImGui.TextColored(Dim, "Team selection:"u8);
            ImGui.SameLine();
            ImGui.RadioButton("Manual##pteamsel"u8, ref sel, (int)TeamSelection.Manual);
            ImGui.SameLine();
            ImGui.RadioButton("Auto only##pteamsel"u8, ref sel, (int)TeamSelection.AutoOnly);
            ImGui.SetNextItemWidth(160 * _scale);
            ImGui.SliderInt("Max team size difference##p"u8, ref diff, 0, TeamRules.MaxDiffLimit);
            if (sel != (int)game.TeamSelection || diff != game.MaxTeamDiff)
            {
                game.SetTeamRules((TeamSelection)sel, diff);
                s.TeamSelect = game.TeamSelection; // remembered for the next game you host (saved with SAVE in settings)
                s.TeamMaxDiff = game.MaxTeamDiff;
            }
            var balance = game.TeamBalance;
            var humansTeam = game.HumansTeam;
            if (TeamBalancePicker(ref balance, ref humansTeam, "pause"))
            {
                game.SetTeamBalance(balance, humansTeam);
                s.TeamBalance = game.TeamBalance;
                s.HumansTeam = game.HumansTeam;
            }
        }
        ImGui.Spacing();
        if (BigButton("BACK"))
            _pauseTeams = false;
    }

    /// <summary>
    /// Join Blue / Join Red / Auto (greyed out with the reason when not allowed), the last answer from the server, and
    /// both rosters, with "Join" next to a person on the other side (to play with a friend).
    /// </summary>
    private void TeamPicker(Game game, double now)
    {
        if (!game.IsTeamMode || game.Local is not { } me)
        {
            ImGui.TextColored(Dim, "There are no teams in this mode."u8);
            return;
        }
        ImGuiSafe.TextColored(me.Team < 2 ? TeamRgba(me.Team) : Dim, me.Team < 2
            ? $"You're on {GameModes.TeamNames[me.Team]} ({(me.TeamChoice < 2 ? "your pick" : "Auto")})"
            : "You're not on a team yet");
        ImGuiSafe.TextColored(Dim, game.TeamSelection == TeamSelection.AutoOnly
            ? "The host set team selection to Auto only: the game keeps the teams even."
            : $"Sides can differ by up to {game.MaxTeamDiff}. Switch before the match or while dead (every {TeamRules.SwitchCooldown:0} s).");

        var btn = new float2(130 * _scale, 34 * _scale);
        string? blocked = null;
        for (byte t = 0; t < 2; t++)
        {
            var why = game.TeamRefusal(me, t, now);
            if (why != null && me.TeamChoice != t)
                blocked ??= why;
            ImGui.BeginDisabled(why != null);
            var tc = TeamRgba(t);
            ImGui.PushStyleColor(ImGuiCol.Button, new float4(tc.X * 0.6f, tc.Y * 0.6f, tc.Z * 0.6f, 1f));
            if (ImGui.Button(me.TeamChoice == t ? $"ON {GameModes.TeamNames[t].ToUpperInvariant()}###join{t}" : $"JOIN {GameModes.TeamNames[t].ToUpperInvariant()}###join{t}", btn))
                game.RequestTeam(t, now);
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            ImGui.SameLine();
        }
        ImGui.BeginDisabled(me.TeamChoice > 1);
        if (ImGui.Button(me.TeamChoice > 1 ? "ON AUTO###auto"u8 : "AUTO###auto"u8, btn))
            game.RequestTeam(Game.NoTeam, now);
        ImGui.EndDisabled();
        if (blocked != null)
            ImGuiSafe.TextColored(Orange, blocked);
        if (now - game.TeamNoticeAt < 5 && game.TeamNotice.Length > 0)
            ImGuiSafe.TextColored(new float4(1, 1, 1, 1), game.TeamNotice);

        ImGui.Spacing();
        if (!ImGui.BeginTable("##teamroster"u8, 2, ImGuiTableFlags.BordersInnerV))
            return;
        for (byte t = 0; t < 2; t++)
            ImGui.TableSetupColumn(GameModes.TeamNames[t], ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();
        for (byte t = 0; t < 2; t++)
        {
            ImGui.TableNextColumn();
            var members = game.Players.Values.Where(p => p.Team == t).OrderBy(p => p.Bot != null).ThenBy(p => p.Id).ToList();
            ImGuiSafe.TextColored(TeamRgba(t), $"{GameModes.TeamNames[t].ToUpperInvariant()} ({members.Count})");
            foreach (var p in members)
            {
                var label = p.Id == game.LocalId ? $"{game.NameOf(p.Id)} (you)" : game.NameOf(p.Id);
                if (p.Bot == null && p.TeamChoice < 2)
                    label += " *";
                ImGuiSafe.TextColored(p.Bot != null ? Dim : Rgba(p.Color), label);
                // Play with a friend: join the side a person on the other team is on.
                if (p.Bot == null && p.Id != game.LocalId && p.Team != me.Team && game.TeamRefusal(me, p.Team, now) == null)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Join##friend{p.Id}"))
                        game.RequestTeam(p.Team, now);
                }
            }
        }
        ImGui.EndTable();
        ImGui.TextColored(Dim, "* picked their team (never moved by autobalance)"u8);
    }

    /// <summary>Smoke test: open or close the TEAMS window.</summary>
    public void SmokeShowTeams(bool open) => _teamsWindow = open;
}
