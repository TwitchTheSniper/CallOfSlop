using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// HUD and menus for game modes and ready-up: the warmup panel (who's ready, READY / FORCE START), the mode score in
/// the match bar, the objective panel, the team scoreboard, the results line, the loadout footer's READY button and
/// the lobby's mode picker. What a mode shows comes from its module (GameModeModule's HUD hooks); this draws it.
/// </summary>
public sealed partial class Ui
{
    private Settings? _hudSettings; // for key names in the match bar


    private static float4 TeamRgba(byte team) => Rgba(GameModes.TeamColor(team));

    // ---- Lobby ------------------------------------------------------------------------------

    /// <summary>Compact game mode picker (single player and Host screens), with the rules underneath.</summary>
    private void ModePicker(Settings s)
    {
        var map = s.MapRandom ? null : Maps.Find(s.MapName);
        var mode = GameModeRegistry.Get(s.Mode);
        ImGui.SetNextItemWidth(260 * _scale);
        if (ImGui.BeginCombo("Game mode"u8, mode.Name))
        {
            foreach (var m in GameModeRegistry.ForMap(map)) // Zombies only on maps made for it; Dev-only modes on dev-pack maps
                if (ImGui.Selectable(m.Name, m.Id == s.Mode))
                    s.Mode = m.Id;
            ImGui.EndCombo();
        }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 620 * _scale);
        if (!mode.Fits(map))
            ImGuiSafe.TextColored(Orange, mode.MapWarning);
        else
            ImGuiSafe.TextColored(Dim, mode.HasTimeLimit ? $"{mode.Rules()} {Game.MatchSeconds / 60:0} minutes max." : mode.Rules());
        ImGui.PopTextWrapPos();
        ScoreLimitPicker(s);
    }

    /// <summary>Score limit: Auto (scales with the player count, ScoreLimits.cs) or Fixed (the classic limits).</summary>
    private void ScoreLimitPicker(Settings s)
    {
        if (!ScoreLimits.HasLimit(s.Mode))
            return;
        ImGui.SetNextItemWidth(260 * _scale);
        if (ImGui.BeginCombo("Score limit"u8, ScoreLimits.ChoiceName(s.ScoreLimit)))
        {
            foreach (var c in new[] { ScoreLimitMode.Auto, ScoreLimitMode.Fixed })
                if (ImGui.Selectable(ScoreLimits.ChoiceName(c), c == s.ScoreLimit))
                    s.ScoreLimit = c;
            ImGui.EndCombo();
        }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 620 * _scale);
        ImGuiSafe.TextColored(Dim, ScoreLimits.Describe(s.Mode, s.ScoreLimit));
        ImGui.PopTextWrapPos();
    }

    /// <summary>Host screen: auto-start after the first person readies up.</summary>
    private void AutoStartPicker(Settings s)
    {
        var on = s.AutoStart > 0;
        if (ImGui.Checkbox("Auto-start the match once someone is ready"u8, ref on))
            s.AutoStart = on ? 30 : 0;
        if (s.AutoStart > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140 * _scale);
            ImGui.SliderInt("seconds##autostart"u8, ref s.AutoStart, 5, 120);
        }
    }

    // ---- Loadout footer ------------------------------------------------------------------------

    /// <summary>The READY toggle in the loadout footer (warmup only). Returns true if it was drawn.</summary>
    private bool ReadyButton(Game game, float2 size)
    {
        if (!game.IsWarmup || game.Local is not { } me)
            return false;
        var ready = me.Ready;
        ImGui.PushStyleColor(ImGuiCol.Button, ready ? new float4(0.2f, 0.55f, 0.25f, 1f) : new float4(0.75f, 0.42f, 0.12f, 1f));
        if (ImGui.Button(ready ? "READY!###ready"u8 : "READY UP###ready"u8, size))
            game.ToggleReady();
        ImGui.PopStyleColor();
        ImGui.SameLine();
        return true;
    }

    // ---- HUD -----------------------------------------------------------------------------------

    /// <summary>Everything mode-related on the HUD (called every frame while a match is on).</summary>
    private void DrawModeHud(Game game, double now, float2 pos, float2 size, Settings settings)
    {
        if (game.IsWarmup)
            DrawReadyPanel(game, pos, size, settings);
        if (game.Phase != MatchPhase.Playing || game.Local is not { } me)
            return;
        var lines = new List<HudLine>();
        System.Numerics.Vector3? pointAt = null;
        game.Module.HudLines(game, me, lines, ref pointAt); // the mode's objective lines (the hill, the flags, the Gun Game ladder...)
        float? arrow = null;
        string? arrowText = null;
        if (pointAt is { } target)
            (arrow, arrowText) = Pointer(game, me, target);
        if (lines.Count == 0)
            return;

        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + 108 * _scale), ImGuiCond.Always, new float2(0.5f, 0));
        ImGui.SetNextWindowBgAlpha(0.35f);
        if (ImGui.Begin("##modehud"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize
                                       | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
        {
            foreach (var (text, color) in lines)
                ImGuiSafe.TextColored(new float4(color.X, color.Y, color.Z, color.W), text);
            if (arrow is { } rel)
            {
                // Arrow pointing at the objective relative to where you're looking, then the distance.
                var s = 11 * _scale;
                var start = ImGui.GetCursorScreenPos();
                var c = start + new float2(s, s);
                float2 Rot(float x, float y) => c + new float2(x * MathF.Cos(rel) - y * MathF.Sin(rel), x * MathF.Sin(rel) + y * MathF.Cos(rel));
                ImDrawListExtensions.AddTriangleFilled(ImGui.GetWindowDrawList(), Rot(0, -s), Rot(s * 0.7f, s * 0.8f), Rot(-s * 0.7f, s * 0.8f),
                    new ImColor8(240, 150, 40, 255));
                ImGui.Dummy(new float2(s * 2, s * 2));
                ImGui.SameLine();
                ImGui.Text(arrowText ?? "");
            }
        }
        ImGui.End();
    }

    /// <summary>Screen angle (0 = straight ahead, positive = to the right) and distance text to a point.</summary>
    private static (float? Angle, string Text) Pointer(Game game, Player me, System.Numerics.Vector3 target)
    {
        var d = target - me.RenderPosition;
        var yaw = MathF.Atan2(d.X, -d.Z);
        var rel = World.WrapAngle(yaw - game.LookYaw);
        var dist = new System.Numerics.Vector2(d.X, d.Z).Length();
        var height = d.Y > 2f ? " (up)" : d.Y < -2f ? " (down)" : "";
        return (rel, $"{dist:0} m{height}");
    }

    /// <summary>Warmup: who's ready, the READY toggle and (host) FORCE START. Clickable while the mouse is free.</summary>
    private void DrawReadyPanel(Game game, float2 pos, float2 size, Settings settings)
    {
        ImGui.SetNextWindowPos(new float2(pos.X + 24 * _scale, pos.Y + size.Y * 0.3f), ImGuiCond.Always, new float2(0, 0));
        ImGui.SetNextWindowBgAlpha(0.6f);
        if (ImGui.Begin("##ready"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
                                     | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove))
        {
            var (ready, humans) = game.ReadyCount;
            ImGui.TextColored(Orange, "WARMUP"u8);
            ImGuiSafe.TextColored(Dim, $"{ready}/{humans} ready");
            if (game.IsTeamMode && game.Local is { Team: < 2 } mine)
                ImGuiSafe.TextColored(TeamRgba(mine.Team), game.ManualTeams ? $"You: {GameModes.TeamNames[mine.Team]} - TEAMS to switch" : $"You: {GameModes.TeamNames[mine.Team]}"); // TEAMS window: UiTeams.cs
            foreach (var p in game.Players.Values.OrderBy(p => p.Team).ThenBy(p => p.Bot != null).ThenBy(p => p.Id))
            {
                var ok = p.Ready || p.Bot != null;
                ImGui.TextColored(ok ? Green : Red, ok ? "[x]"u8 : "[ ]"u8);
                ImGui.SameLine();
                ImGuiSafe.TextColored(Rgba(p.Color), p.Id == game.LocalId ? $"{game.NameOf(p.Id)} (you)" : game.NameOf(p.Id));
            }
            var key = Keybinds.Name(settings.Bind(GameAction.Ready));
            if (game.Local is { } me && ImGui.Button(me.Ready ? $"NOT READY ({key})###rdy" : $"READY ({key})###rdy", new float2(180 * _scale, 0)))
                game.ToggleReady();
            if (game.IsServer && !game.Net.IsOffline && ImGui.Button("FORCE START"u8, new float2(180 * _scale, 0)))
                game.ForceStart();
            if (game.AutoStartIn > 0)
                ImGuiSafe.TextColored(Orange, $"Starting in {Math.Ceiling(game.AutoStartIn):0} s");
        }
        ImGui.End();
    }

    /// <summary>The match bar during the warmup.</summary>
    private void DrawWarmupBar(Game game, Settings? settings)
    {
        var (ready, humans) = game.ReadyCount;
        ImGui.PushFont(default, 32);
        ImGui.TextColored(Orange, "WAITING FOR PLAYERS"u8);
        ImGui.PopFont();
        var key = settings != null ? Keybinds.Name(settings.Bind(GameAction.Ready)) : "F3";
        ImGuiSafe.TextColored(Dim, game.Net.IsOffline
            ? $"Pick your loadout, then READY or DEPLOY ({key}) to start."
            : $"{ready}/{humans} ready - press {key} or click READY. The match starts when everyone is ready.");
        ImGuiSafe.TextColored(Dim, game.ModeGoalText);
    }

    /// <summary>The match bar while playing a mode with its own bar (team scores, Gun Game's ladder, Zombies' round). Returns false for the plain free-for-all bar.</summary>
    private bool DrawModeScoreBar(Game game, string clock, int t)
    {
        if (DrawZombiesBar(game))
            return true; // the round instead of a clock (UiZombies.cs)
        var mode = game.Module;
        var solo = mode.TeamBased ? null : mode.ScoreBar(game);
        if (!mode.TeamBased && solo == null)
            return false;
        ImGui.PushFont(default, 24);
        ImGuiSafe.TextColored(t <= 60 ? Red : new float4(1, 1, 1, 1), clock);
        ImGui.SameLine(0, 24 * _scale);
        if (mode.TeamBased)
        {
            ImGuiSafe.TextColored(TeamRgba(0), $"BLUE {game.TeamScore[0]}");
            ImGui.SameLine();
            ImGui.TextColored(Dim, "-"u8);
            ImGui.SameLine();
            ImGuiSafe.TextColored(TeamRgba(1), $"{game.TeamScore[1]} RED");
            ImGui.PopFont();
            var team = game.Local?.Team ?? Game.NoTeam;
            var limit = $"first to {game.ScoreLimit} {mode.ScoreUnit}";
            ImGuiSafe.TextColored(team < 2 ? TeamRgba(team) : Dim, team < 2 ? $"{mode.Name} - you're on {GameModes.TeamNames[team]} - {limit}" : $"{mode.Name} - {limit}");
        }
        else
        {
            var (big, sub) = solo!.Value;
            ImGuiSafe.TextColored(Orange, big);
            ImGui.PopFont();
            ImGuiSafe.TextColored(Dim, sub);
        }
        return true;
    }

    /// <summary>Scoreboard for team modes (grouped by team) and modes with their own columns (Gun Game: by ladder step). False: use the normal one.</summary>
    private bool DrawModeScoreboard(Game game, float2 size, float2 pos)
    {
        var mode = game.Module;
        if (mode is FreeForAllModule)
            return false;
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.3f), ImGuiCond.Always, new float2(0.5f, 0));
        ImGui.SetNextWindowSize(new float2(MathF.Min(620 * _scale, size.X - 16 * _scale), 0));
        if (ImGui.Begin("Scoreboard"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGuiSafe.TextColored(Orange, $"SCOREBOARD - {mode.Name.ToUpperInvariant()}");
            var (col1, col2) = mode.ScoreboardColumns;
            if (ImGui.BeginTable("##modescores"u8, 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Player"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(col1, ImGuiTableColumnFlags.WidthFixed, 70 * _scale);
                ImGui.TableSetupColumn(col2, ImGuiTableColumnFlags.WidthFixed, 70 * _scale);
                ImGui.TableSetupColumn("Weapon"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Status"u8, ImGuiTableColumnFlags.WidthFixed, 80 * _scale);
                ImGui.TableHeadersRow();
                var groups = mode.TeamBased ? new byte[] { 0, 1 } : new[] { Game.NoTeam };
                foreach (var team in groups)
                {
                    if (team < 2)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGuiSafe.TextColored(TeamRgba(team), $"{GameModes.TeamNames[team].ToUpperInvariant()} TEAM   {game.TeamScore[team]}");
                    }
                    var members = game.Players.Values.Where(p => !mode.TeamBased || p.Team == team)
                        .OrderByDescending(mode.RankScore).ThenByDescending(p => p.Kills).ThenBy(p => p.Deaths);
                    foreach (var p in members)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        var tag = mode.ScoreboardTag(game, p);
                        var carrier = (tag.Length > 0 ? " " + tag : "")
                                      + (mode.TeamBased && p.Bot == null && p.TeamChoice < 2 ? " *" : ""); // * picked their team (UiTeams.cs)
                        ImGuiSafe.TextColored(Rgba(p.Color), (p.Id == game.LocalId ? $"{game.NameOf(p.Id)} (you)" : game.NameOf(p.Id)) + carrier);
                        var (cell1, cell2) = mode.ScoreboardCells(game, p);
                        ImGui.TableNextColumn();
                        ImGui.Text(cell1);
                        ImGui.TableNextColumn();
                        ImGui.Text(cell2);
                        ImGui.TableNextColumn();
                        ImGui.Text(p.WeaponDef.Name);
                        ImGui.TableNextColumn();
                        ImGuiSafe.TextColored(p.Alive ? Green : Red, p.Alive ? $"{p.Health} HP" : "dead");
                    }
                }
                ImGui.EndTable();
            }
            if (game.ManualTeams)
                ImGui.TextColored(Dim, "* picked their team. Switch sides: Esc > TEAMS, or TEAMS on the loadout screen."u8);
        }
        ImGui.End();
        return true;
    }

    /// <summary>Results screen: the mode and, for team modes, the final team score (other modes: their own line, if any).</summary>
    private void DrawModeResultsLine(Game game)
    {
        var mode = game.Module;
        if (mode.TeamBased)
        {
            ImGuiSafe.TextColored(Dim, $"{mode.Name}:");
            ImGui.SameLine();
            ImGuiSafe.TextColored(TeamRgba(0), $"Blue {game.TeamScore[0]}");
            ImGui.SameLine();
            ImGui.TextColored(Dim, "-"u8);
            ImGui.SameLine();
            ImGuiSafe.TextColored(TeamRgba(1), $"{game.TeamScore[1]} Red");
            if (game.Local is { Team: < 2 } me)
            {
                ImGui.SameLine();
                ImGuiSafe.TextColored(Dim, $"  (you were {GameModes.TeamNames[me.Team]})");
            }
        }
        else if (mode.ResultsLine(game) is { } line)
            ImGuiSafe.TextColored(Dim, line);
    }
}
