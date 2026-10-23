using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The scoreboard for big lobbies (more than <see cref="BigScoreboardFrom"/> players, up to 32): two columns of
/// compact rows. Team modes put Blue on the left and Red on the right; free-for-all and Gun Game split the ranking
/// (1-16 left, 17-32 right). Smaller games keep the full-width scoreboard with the weapon column.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Above this many players the scoreboard switches to two compact columns.</summary>
    public const int BigScoreboardFrom = 12;

    /// <summary>Splits players into the scoreboard's two columns (team modes: by team; otherwise the ranking in halves).</summary>
    public static (List<Player> Left, List<Player> Right) ScoreColumns(Game game)
    {
        var ranked = game.Players.Values.OrderByDescending(game.Module.RankScore).ThenByDescending(p => p.Kills).ThenBy(p => p.Deaths).ThenBy(p => p.Id).ToList();
        if (game.IsTeamMode)
            return (ranked.Where(p => p.Team == 0).ToList(), ranked.Where(p => p.Team != 0).ToList());
        var half = (ranked.Count + 1) / 2;
        return (ranked.Take(half).ToList(), ranked.Skip(half).ToList());
    }

    private void DrawBigScoreboard(Game game, float2 size, float2 pos)
    {
        var width = MathF.Min(1080 * _scale, size.X - 16 * _scale);
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.12f), ImGuiCond.Always, new float2(0.5f, 0));
        ImGui.SetNextWindowSize(new float2(width, 0));
        if (ImGui.Begin("Scoreboard"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize))
        {
            var bots = game.Players.Values.Count(p => p.Bot != null);
            var mode = game.Module;
            ImGuiSafe.TextColored(Orange, $"SCOREBOARD - {mode.Name.ToUpperInvariant()}");
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, $"   {game.Players.Count} / {World.Map.MaxPlayers} players on {World.Map.Name}" + (game.IsServer && bots > 0 ? $" ({bots} bots)" : ""));
            var (left, right) = ScoreColumns(game);
            var (col1, col2) = mode.ScoreboardColumnsShort;
            if (ImGui.BeginTable("##bigscores"u8, 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("##l"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##r"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow();
                for (var side = 0; side < 2; side++)
                {
                    ImGui.TableNextColumn();
                    var list = side == 0 ? left : right;
                    if (game.IsTeamMode)
                        ImGuiSafe.TextColored(TeamRgba((byte)side), $"{GameModes.TeamNames[side].ToUpperInvariant()} TEAM   {game.TeamScore[side]}   ({list.Count} players)");
                    var rankBase = game.IsTeamMode || side == 0 ? 0 : left.Count;
                    if (ImGui.BeginTable(side == 0 ? "##bl"u8 : "##br"u8, 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
                    {
                        ImGui.TableSetupColumn("#"u8, ImGuiTableColumnFlags.WidthFixed, 26 * _scale);
                        ImGui.TableSetupColumn("Player"u8, ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn(col1, ImGuiTableColumnFlags.WidthFixed, 48 * _scale);
                        ImGui.TableSetupColumn(col2, ImGuiTableColumnFlags.WidthFixed, 36 * _scale);
                        ImGui.TableSetupColumn(""u8, ImGuiTableColumnFlags.WidthFixed, 56 * _scale);
                        ImGui.TableHeadersRow();
                        for (var i = 0; i < list.Count; i++)
                        {
                            var p = list[i];
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGuiSafe.TextColored(Dim, $"{rankBase + i + 1}");
                            ImGui.TableNextColumn();
                            var carrier = mode.ScoreboardTag(game, p); // " [FLAG]"
                            ImGuiSafe.TextColored(Rgba(p.Color), (p.Id == game.LocalId ? $"{game.NameOf(p.Id)} (you)" : game.NameOf(p.Id)) + game.RankTag(p) + carrier + game.VoiceTag(p, Brutal.GlfwApi.Glfw.Time)); // talking / muted (GameVoice.cs)
                            var (cell1, cell2) = mode.ScoreboardCells(game, p);
                            ImGui.TableNextColumn();
                            ImGui.Text(cell1);
                            ImGui.TableNextColumn();
                            ImGui.Text(cell2);
                            ImGui.TableNextColumn();
                            ImGuiSafe.TextColored(p.Alive ? Green : Red, p.Alive ? $"{p.Health}" : "dead");
                        }
                        ImGui.EndTable();
                    }
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }
}
