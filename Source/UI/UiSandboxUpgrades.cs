using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Tab inventory's Upgrades tab (docs/sandbox/TOOL-UPGRADES.md): each tool stat, what the local player has of it and the
/// module that raises it next (an Assembler builds it; F on it, lying loose, fits it). The lines are made again only when a
/// level changes.
/// </summary>
public sealed partial class Ui
{
    private readonly string[] _upLine = new string[(int)ToolStat.COUNT];
    private readonly string[] _upNext = new string[(int)ToolStat.COUNT];
    private readonly int[] _upSeen = new int[(int)ToolStat.COUNT];

    private void SbDrawUpgrades(Game game, Player me)
    {
        var table = ToolUpgrades.Current;
        var creative = game.SandboxInfiniteParts;
        ImGuiSafe.TextColored(Dim, creative
            ? "Creative: every tool is at its best."
            : "Your own tool upgrades: an Assembler builds the modules (some only once a faction has taught them); F on one lying loose fits it.");
        if (table.Problem.Length > 0)
            ImGuiSafe.TextColored(Dim, $"ToolUpgrades.json was refused ({table.Problem}): the built-in table is in use.");
        ImGui.Separator();
        for (var s = 0; s < (int)ToolStat.COUNT; s++)
        {
            var stat = (ToolStat)s;
            var st = table.Of(stat);
            var level = game.SandboxHands.Level(me.Id, stat);
            var seen = level * 2 + (creative ? 1 : 0) + 1;
            if (_upSeen[s] != seen || _upLine[s] == null)
            {
                _upSeen[s] = seen;
                var name = ToolUpgrades.ToolName(st.Tool);
                var tool = char.ToUpperInvariant(name[0]) + name[1..];
                var now = table.Show(stat, table.Value(stat, level, creative));
                var mk = st.Function || creative ? "" : $" (Mk {Math.Min(level, st.Levels.Length) + 1} of {st.Levels.Length + 1})";
                _upLine[s] = $"{tool}: {st.Name}: {now}{mk}";
                _upNext[s] = creative ? "" : level >= st.Levels.Length ? "at its best" : $"next: {st.Levels[level].Part}, {table.Show(stat, st.Levels[level].Value)}";
            }
            ImGui.Text(_upLine[s]);
            if (ImGui.IsItemHovered() && st.About.Length > 0)
                ImGuiSafe.SetTooltip(st.About);
            if (_upNext[s].Length > 0)
            {
                ImGui.SameLine(420 * _scale);
                ImGuiSafe.TextColored(Dim, _upNext[s]);
            }
        }
    }
}
