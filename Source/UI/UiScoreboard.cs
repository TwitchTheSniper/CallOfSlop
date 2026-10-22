using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The scoreboard for big lobbies (more than <see cref="BigScoreboardFrom"/> players, up to 32): two columns of
/// compact rows. Team modes put Blue on the left and Red on the right; free-for-all and Gun Game split the ranking
/// (1-16 left, 17-32 right). Smaller games keep the full-width scoreboard with the weapon column. No scoreboard shows
/// health (only your own HUD does); every one has a Level column (ScoreboardModel.cs).
/// </summary>
public sealed partial class Ui
{
    /// <summary>Above this many players the scoreboard switches to two compact columns.</summary>
    public const int BigScoreboardFrom = 12;

    /// <summary>The full-size scoreboard's width (before UI scale; capped by the HUD area on narrow screens).</summary>
    private const float ScoreboardWidth = 680;

    // Reused every frame the scoreboard is open (no allocation): the ranking and the big scoreboard's two columns.
    private readonly List<Player> _scoreRanked = new();
    private readonly List<Player> _scoreLeft = new();
    private readonly List<Player> _scoreRight = new();

    /// <summary>Sets up the scoreboard's columns from <see cref="ScoreboardModel.Headers"/> (the widths fit 16:9 to 32:9 HUD areas).</summary>
    private void SetupScoreColumns(string[] heads, bool compact)
    {
        for (var i = 0; i < heads.Length; i++)
        {
            var h = heads[i];
            if (h == "Player" || h == "Weapon")
                ImGui.TableSetupColumn(h, ImGuiTableColumnFlags.WidthStretch);
            else
                ImGui.TableSetupColumn(h, ImGuiTableColumnFlags.WidthFixed, _scale * (compact
                    ? i == 0 ? 26 : i == 2 ? 76 : i == 3 ? 48 : i == 4 ? 36 : 40
                    : h == ScoreboardModel.LevelHeader ? 90 : i == heads.Length - 1 ? 60 : 70));
        }
        ImGui.TableHeadersRow();
    }

    /// <summary>
    /// A player's Level cell: the hat they wear (the lobby's little front view) and their rank in gold ("P2 L13"), or a dim
    /// "BOT" for bots.
    /// </summary>
    private void DrawLevelCell(Game game, Player p)
    {
        var cell = ScoreboardModel.Level(game, p);
        var icon = ImGui.GetTextLineHeight();
        if (cell.Hat != HatId.None)
        {
            DrawHatIcon(ImGui.GetWindowDrawList(), cell.Hat, ImGui.GetCursorScreenPos(), icon);
            ImGui.Dummy(new float2(icon, icon));
            ImGui.SameLine(0, 3 * _scale);
        }
        ImGuiSafe.TextColored(cell.Bot ? Dim : Gold, cell.Label);
    }

    private void DrawBigScoreboard(Game game, float2 size, float2 pos)
    {
        var width = MathF.Min(1080 * _scale, size.X - 16 * _scale);
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.12f), ImGuiCond.Always, new float2(0.5f, 0));
        ImGui.SetNextWindowSize(new float2(width, 0));
        if (ImGui.Begin("Scoreboard"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize))
        {
            var bots = ScoreboardModel.BotCount(game);
            var mode = game.Module;
            ImGuiSafe.TextColored(Orange, $"SCOREBOARD - {ScoreboardModel.UpperName(mode)}");
            ImGui.SameLine();
            if (game.IsServer && bots > 0)
                ImGuiSafe.TextColored(Dim, $"   {game.Players.Count} / {World.Map.MaxPlayers} players on {World.Map.Name} ({bots} bots)");
            else
                ImGuiSafe.TextColored(Dim, $"   {game.Players.Count} / {World.Map.MaxPlayers} players on {World.Map.Name}");
            ScoreboardModel.Rank(game, _scoreRanked);
            ScoreboardModel.Split(game, _scoreRanked, _scoreLeft, _scoreRight);
            var heads = ScoreboardModel.Headers(mode, true); // #, Player, Level, the mode's short columns, status (no HP)
            if (ImGui.BeginTable("##bigscores"u8, 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("##l"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##r"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow();
                for (var side = 0; side < 2; side++)
                {
                    ImGui.TableNextColumn();
                    var list = side == 0 ? _scoreLeft : _scoreRight;
                    if (game.IsTeamMode)
                        ImGuiSafe.TextColored(TeamRgba((byte)side), $"{ScoreboardModel.TeamUpper[side]} TEAM   {game.TeamScore[side]}   ({list.Count} players)");
                    var rankBase = game.IsTeamMode || side == 0 ? 0 : _scoreLeft.Count;
                    if (ImGui.BeginTable(side == 0 ? "##bl"u8 : "##br"u8, heads.Length, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
                    {
                        SetupScoreColumns(heads, true);
                        for (var i = 0; i < list.Count; i++)
                        {
                            var p = list[i];
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGuiSafe.TextColored(Dim, ScoreboardModel.Num(rankBase + i + 1));
                            ImGui.TableNextColumn();
                            var carrier = mode.ScoreboardTag(game, p); // " [FLAG]"
                            ImGuiSafe.TextColored(Rgba(p.Color), $"{game.NameOf(p.Id)}{(p.Id == game.LocalId ? " (you)" : "")}{carrier}");
                            ImGui.TableNextColumn();
                            DrawLevelCell(game, p);
                            var (cell1, cell2) = mode.ScoreboardCells(game, p);
                            ImGui.TableNextColumn();
                            ImGui.Text(cell1);
                            ImGui.TableNextColumn();
                            ImGui.Text(cell2);
                            ImGui.TableNextColumn();
                            ImGuiSafe.TextColored(Red, ScoreboardModel.Status(p, true));
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
