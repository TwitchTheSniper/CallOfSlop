using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Faction window's rank bars (docs/story/STORY.md "Ranks"; the promotions: Game/GameStoryRanks.cs): with each story
/// faction, your tier, a bar from where it began to the next one, and what the next one brings (its uniform, technologies
/// and jobs). From the standings every peer has (FactionTable.PlayerStanding); the words are worked out again only when
/// the table changes, not every frame.
/// </summary>
public sealed partial class Ui
{
    private sealed class RankRow
    {
        public string Head = "", Bar = "", Next = "";
        public float Fill;
        public bool Top, Low;
    }

    private RankRow[] _rankRows = [];
    private int _rankRowsVersion = -1;
    private byte _rankRowsFor;
    private readonly sbyte[] _rankRowsHad = new sbyte[FactionTable.MaxStory];

    /// <summary>Draws a bar per story faction from the local player's own standings; false when the host hasn't sent any (the old lines are drawn).</summary>
    private bool DrawRankBars(Game game)
    {
        var t = game.FactionTable;
        var me = game.LocalId;
        if (!t.PlayerStory[me] || t.StoryNames.Length == 0)
            return false;
        if (_rankRowsVersion != t.Version || _rankRowsFor != me)
        {
            _rankRowsVersion = t.Version;
            _rankRowsFor = me;
            var same = _rankRows.Length > 0;
            for (var f = 0; f < FactionTable.MaxStory; f++)
            {
                same &= _rankRowsHad[f] == t.PlayerStanding[me * FactionTable.MaxStory + f];
                _rankRowsHad[f] = t.PlayerStanding[me * FactionTable.MaxStory + f];
            }
            if (!same) // (the table changes for anyone's standing: the words only for ours)
                _rankRows = RankRows(game, t, me);
        }
        if (_rankRows.Length == 0)
            return false;
        var w = ImGui.GetContentRegionAvail().X;
        foreach (var row in _rankRows)
        {
            ImGuiSafe.TextColored(row.Low ? new float4(1f, 0.45f, 0.4f, 1f) : Orange, row.Head);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, row.Top ? new float4(0.85f, 0.7f, 0.25f, 1f) : row.Low ? new float4(0.75f, 0.3f, 0.25f, 1f) : new float4(0.45f, 0.75f, 0.45f, 1f));
            ImGui.ProgressBar(row.Fill, new float2(w, 16 * _scale), row.Bar);
            ImGui.PopStyleColor();
            if (row.Next.Length > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                ImGuiSafe.TextWrapped(row.Next);
                ImGui.PopStyleColor();
            }
        }
        return true;
    }

    private static RankRow[] RankRows(Game game, FactionTable t, byte me)
    {
        var rules = Game.StoryData.Factions;
        var uniforms = game.Players.TryGetValue(me, out var p) && Uniforms.Get(p.Uniform) is { Tiers.Length: > 0 }; // (issued one by standing: this world issues them)
        var rows = new List<RankRow>(t.StoryNames.Length);
        for (var f = 0; f < t.StoryNames.Length && f < FactionTable.MaxStory; f++)
        {
            if (rules.Find(t.StoryNames[f]) is not { } def || def.Tiers.Length == 0)
                continue;
            int v = t.PlayerStanding[me * FactionTable.MaxStory + f];
            var tier = def.TierOf(v);
            var name = def.Tiers[tier].Name;
            var lo = tier == 0 ? -FactionRules.MaxStanding : def.Tiers[tier - 1].Below;
            var top = tier == def.Tiers.Length - 1;
            var hi = top ? FactionRules.MaxStanding : def.Tiers[tier].Below;
            var row = new RankRow
            {
                Head = $"{def.Name}: {name} ({v:+0;-0;0})",
                Top = top,
                Low = tier < def.TierOf(0),
                Fill = hi > lo ? Math.Clamp((v - lo) / (float)(hi - lo), 0f, 1f) : 1f,
            };
            if (top)
                row.Bar = "Top rank";
            else
            {
                var next = def.Tiers[tier + 1].Name;
                row.Bar = $"{v - lo} / {hi - lo} to {next}";
                var brings = Game.RankUnlockLine(def, tier, tier + 1, uniforms, "Uniform", "Teaches");
                row.Next = brings.Length > 0 ? $"  {next} at {hi:+0;-0;0}. {brings}" : $"  {next} at {hi:+0;-0;0}.";
            }
            rows.Add(row);
        }
        return rows.ToArray();
    }
}
