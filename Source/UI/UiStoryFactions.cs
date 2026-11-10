using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Story window's Factions tab (docs/story/STORY.md "Factions"): each faction of Story/Factions, what it wants, its
/// members and what it claims, and its standing with every player and group it has dealings with: the tier, the number,
/// and the deeds it remembers of them.
/// </summary>
public sealed partial class Ui
{
    private static readonly float4 StoryGood = new(0.55f, 0.85f, 0.55f, 1f), StoryBad = new(0.9f, 0.45f, 0.4f, 1f);

    private void DrawStoryFactions(StoryEngine s)
    {
        var fs = s.Factions;
        DimWrapped($"{fs.Count} factions in the data (Story/Factions), {fs.Deeds} deeds counted this session. A deed is something a player does to a faction's own (a member, a construct it claims, the faction itself): it remembers it, and its standing with them and their group moves. Standing is its Likes or Grudge.");
        if (fs.Count == 0)
            return;
        for (var f = 0; f < fs.Count; f++)
        {
            var def = fs.Def(f);
            var id = fs.EntityOf(f);
            // Every faction draws the same labels ("Deeds it counts"), so each gets its own ID scope.
            ImGui.PushID(f);
            if (ImGui.CollapsingHeader(def.Name, ImGuiTreeNodeFlags.DefaultOpen) && id >= 0)
                DrawStoryFaction(s, f, def, id);
            ImGui.PopID();
        }
    }

    /// <summary>One faction's part of the Factions tab, inside its header and ID scope.</summary>
    private void DrawStoryFaction(StoryEngine s, int f, FactionDef def, int id)
    {
        var fs = s.Factions;
        var rules = s.Rules;
        var w = s.World;
        var fr = rules.Factions;
        ImGui.Indent();
        DimWrapped($"{AsSentence(def.About)}Home: {(def.Home.Length > 0 ? def.Home : "none")}. Claims constructs named {(def.Claims.Length > 0 ? string.Join(", ", def.Claims.Select(c => $"\"{c}...\"")) : "nothing")}.");
        if (def.Wants.Length == 0)
            ImGuiSafe.TextColored(Dim, "Wants: none (its file has no \"wants\").");
        else
        {
            ImGuiSafe.TextColored(StoryPlain, "Wants: " + string.Join(", ", def.Wants.Select(x => $"{x.Name} {w.Quality(id, x.Quality)}")));
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip(string.Join("\n", def.Wants.Select(x => $"{x.Name}: {x.About} ({x.PerHour:+0.##;-0.##;0} an hour)")));
        }
        var members = 0;
        var claimed = 0;
        if (fr.MemberOf >= 0)
        {
            var (a, b) = w.HeldRange(id, fr.MemberOf);
            members = b - a;
        }
        if (fr.Claims >= 0)
        {
            var (a, b) = w.Range(id, fr.Claims);
            claimed = b - a;
        }
        ImGuiSafe.TextColored(StoryPlain, $"{members} members, {claimed} constructs claimed");
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip(string.Join("\n", def.Members.Select(m => $"{m.Name}: {m.About}")));

        // Everyone it has dealings with: whoever it holds any relation towards that is a player or a group.
        var parties = new List<int>();
        var e = w[id];
        for (var k = 0; k < e.PairCount; k++)
        {
            var t = e.Pairs[k].Target;
            if (w.Valid(t) && (w[t].Kind == StoryKind.Player || w[t].Kind == StoryKind.Faction && fs.FactionOf(t) < 0) && !parties.Contains(t))
                parties.Add(t);
        }
        if (parties.Count == 0)
            ImGuiSafe.TextColored(Dim, "No dealings with anyone yet.");
        foreach (var p in parties.OrderBy(p => fs.Standing(f, p)))
        {
            var st = fs.Standing(f, p);
            var memory = new List<string>();
            for (var k = 0; k < e.PairCount; k++)
                if (e.Pairs[k].Target == p && e.Pairs[k].Rel != fr.Likes && e.Pairs[k].Rel != fr.Grudge)
                    memory.Add($"{rules.Relations[e.Pairs[k].Rel].Name} {e.Pairs[k].Value}");
            var group = w[p].Kind == StoryKind.Faction ? " (group)" : fs.Party(p) != p ? $" (in {w.NameOf(fs.Party(p))})" : "";
            ImGuiSafe.TextColored(st < 0 ? StoryBad : st > 0 ? StoryGood : StoryPlain, $"{w.NameOf(p)}{group}: {def.TierName(st)} ({st:+0;-0;0}){(fs.Notorious(p) ? ", NOTORIOUS: hunted by every faction" : "")}{(memory.Count > 0 ? "  " + string.Join(", ", memory) : "")}");
        }
        if (ImGui.TreeNode("Deeds it counts"u8))
        {
            foreach (var d in def.Deeds)
            {
                ImGuiSafe.TextColored(StoryPlain, $"{d.Name}: {d.Text}");
                if (d.About.Length > 0 && ImGui.IsItemHovered())
                    ImGuiSafe.SetItemTooltip(d.About);
            }
            ImGuiSafe.TextColored(Dim, "Tiers: " + string.Join(", ", def.Tiers.Select((t, k) => k == def.Tiers.Length - 1 ? $"{t.Name} above" : $"{t.Name} below {t.Below}")));
            ImGui.TreePop();
        }
        if (def.Scripture.Length > 0 && ImGui.TreeNode("Scripture"u8))
        {
            foreach (var v in def.Scripture)
            {
                ImGuiSafe.TextColored(StoryPlain, v.Fragment.Length > 0 ? $"{v.Name} (fragment: {v.Fragment})" : v.Name);
                if (v.Bends.Length > 0 && ImGui.IsItemHovered())
                    ImGuiSafe.SetItemTooltip("How it bends the fragment: " + v.Bends);
                DimWrapped(v.Text);
            }
            ImGui.TreePop();
        }
        ImGui.Unindent();
    }

    /// <summary>A faction's "about" as the start of a sentence: a capital first letter and a full stop, then a space.</summary>
    private static string AsSentence(string about)
    {
        about = about.Trim();
        if (about.Length == 0)
            return "";
        about = char.ToUpperInvariant(about[0]) + about[1..];
        return about[^1] is '.' or '!' or '?' ? about + " " : about + ". ";
    }
}
