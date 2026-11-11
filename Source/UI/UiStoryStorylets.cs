using Brutal.ImGuiApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Story window's Story tab (docs/story/STORY.md "Storylets"; host only): for a player, every storylet with why
/// it isn't playing now, clause by clause, the one under way with its cast and the ends it waits for,
/// what ended lately, and the storylet qualities and facts they hold. The buttons are for trying things: nudge a quality,
/// start a storylet now (its needs aside), end one by any of its ends, or forget what a player has had.
/// </summary>
public sealed partial class Ui
{
    private int _stSlPlayer = -1;
    private string _stSlNote = "";

    private void DrawStoryStorylets(StoryEngine s)
    {
        var sl = s.Storylets;
        var sr = s.Rules.Storylets;
        var w = s.World;
        var now = s.Time;
        var on = sl.On;
        if (StoryEngineTabs && ImGui.Checkbox("Storylets"u8, ref on))
            sl.On = on;
        if (StoryEngineTabs && ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip("Storylets are authored beats (Story/Storylets) that wait for the world to be ready. Per player: an urgent one first, then the A and B plots in turn, with a rest between. Off, none starts (one under way still ends).");
        if (!on)
            ImGuiSafe.TextColored(StoryFailed, StoryEngineTabs ? "Off: no storylet starts." : "Storylets are off: none starts (the switch is under Engine).");
        if (StoryEngineTabs)
            DimWrapped($"{sl.Started} begun, {sl.Ended} ended, {sl.Dropped} given up; {sr.Storylets.Length} storylets in the data. A rest of {StoryRules.Span(StoryStorylets.Rest)} after each.");
        if (sr.Storylets.Length == 0)
        {
            ImGuiSafe.TextWrapped("No storylets loaded (Story/Storylets/*.json).");
            return;
        }

        // Whose.
        if (!w.Valid(_stSlPlayer) || w[_stSlPlayer].Kind != StoryKind.Player)
        {
            _stSlPlayer = -1;
            for (var i = 0; i < w.Count && _stSlPlayer < 0; i++)
                if (w[i].Kind == StoryKind.Player && !w[i].Gone)
                    _stSlPlayer = i;
        }
        if (ImGui.BeginCombo("Player##stslpl"u8, _stSlPlayer >= 0 ? w[_stSlPlayer].Name : "nobody"))
        {
            for (var i = 0; i < w.Count; i++)
                if (w[i].Kind == StoryKind.Player && ImGui.Selectable($"{w[i].Name}{(w[i].Gone ? " (away)" : "")}##stslp{i}", i == _stSlPlayer))
                    _stSlPlayer = i;
            ImGui.EndCombo();
        }
        var me = _stSlPlayer;
        if (me < 0)
        {
            ImGuiSafe.TextColored(Dim, "No player in the story yet.");
            return;
        }
        ImGui.SameLine();
        if (ImGui.Button("Forget what they had"u8))
        {
            sl.Reset(me);
            _stSlNote = $"{w.NameOf(me)}: every storylet can come again.";
        }
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip("Clears which storylets they have had and the one under way (their qualities and facts stay).");
        if (_stSlNote.Length > 0)
            ImGuiSafe.TextColored(Orange, _stSlNote);

        // Under way.
        if (ImGui.CollapsingHeader("Playing now"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var any = false;
            foreach (var r in sl.Runs)
            {
                if (r.Player != me)
                    continue;
                any = true;
                var d = sr.Storylets[r.Def];
                var waited = now - r.WaitFrom - r.Paused;
                ImGuiSafe.TextColored(Orange, $"{d.Title} [{d.Bucket}]: {r.Phase switch { StoryletPhase.Beats => "its beats are playing", StoryletPhase.Paused => "paused for an urgent one", _ => $"waiting for an end ({StoryRules.Span(Math.Max(0, waited))})" }}");
                ImGui.Indent();
                if (d.CastVars > 1)
                    ImGuiSafe.TextColored(StoryPlain, sl.CastText(d, r.Binds));
                foreach (var e in d.Ends)
                {
                    var left = e.After > 0 ? $" (in {StoryRules.Span(Math.Max(0, e.After - waited))})" : e.Within > 0 ? $" (for {StoryRules.Span(Math.Max(0, e.Within - (now - r.WaitFrom)))})" : "";
                    if (ImGui.SmallButton($"End##stslend{r.Serial}_{e.Index}"))
                        _stSlNote = sl.Force(me, r.Def, e.Index, now) ? $"{d.Title}: ended by {e.Id}." : $"{d.Title}: not under way.";
                    ImGui.SameLine();
                    ImGuiSafe.TextColored(StoryPlain, $"{e.Id}: {e.Text}{left}");
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetItemTooltip(EndTip(s, d, e));
                }
                ImGui.Unindent();
            }
            if (!any)
                ImGuiSafe.TextColored(Dim, "None for them now.");
        }

        // Every storylet, and why not.
        if (ImGui.CollapsingHeader("Every storylet, and why it isn't playing"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            DimWrapped(StoryEngineTabs ? "Ready ones play in turn (urgent first, then A and B taking turns, the highest priority first). Hover one for what it needs; Start plays it now if it can be cast, its needs aside."
                : "Green is ready, red says what it waits for, grey has been. Hover one for what it needs; Start plays it now.");
            foreach (var d in sr.Storylets)
            {
                var why = sl.WhyNot(me, d.Index, now);
                if (ImGui.SmallButton($"Start##stslgo{d.Index}"))
                    _stSlNote = sl.StartNow(me, d.Index, now) is { } no ? $"{d.Title}: {no}." : $"{d.Title}: begun.";
                ImGui.SameLine();
                ImGuiSafe.TextColored(why == null ? StoryDone : why.StartsWith("had") ? Dim : why.StartsWith("under") || why.StartsWith("paused") ? Orange : StoryFailed,
                    $"[{(d.Bucket == StoryletBucket.Urgent ? "!" : d.Bucket.ToString())}] {d.Title}: {why ?? "ready"}");
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetItemTooltip(StoryletTip(s, d));
            }
        }

        // Their qualities, to nudge.
        if (ImGui.CollapsingHeader("Their qualities and facts (- and + change one)"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var q in sr.Qualities)
                QualityRow(s, me, q.Id, q.Name, q.Min, q.Max, q.Menace ? "menace" : "progress");
            foreach (var name in (ReadOnlySpan<string>)["Inducted", "Contraband"])
                if (s.Rules.Quality(name) is { } qd)
                    QualityRow(s, me, qd.Id, qd.Name, 0, 1, "");
            foreach (var name in (ReadOnlySpan<string>)["Course", "Lead", "Visited"])
            {
                if (s.Rules.Relation(name) is not { } rd)
                    continue;
                var (a, b) = w.Range(me, rd.Id);
                var held = new List<string>();
                for (var k = a; k < b; k++)
                    held.Add(w[me].Pairs[k].Value == 1 ? w.NameOf(w[me].Pairs[k].Target) : $"{w.NameOf(w[me].Pairs[k].Target)} {w[me].Pairs[k].Value}");
                ImGuiSafe.TextColored(held.Count > 0 ? StoryPlain : Dim, $"{name}: {(held.Count > 0 ? string.Join(", ", held) : "none")}");
            }
        }

        if (ImGui.CollapsingHeader("Ended lately"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (sl.HistoryCount == 0)
                ImGuiSafe.TextColored(Dim, "None ended yet.");
            for (var k = 0; k < sl.HistoryCount; k++)
            {
                ref readonly var h = ref sl.History[(sl.HistoryNext - 1 - k + StoryStorylets.HistorySize) % StoryStorylets.HistorySize];
                var d = sr.Storylets[h.Def];
                ImGuiSafe.TextColored(h.Dropped == null ? StoryDone : StoryFailed,
                    $"{StoryRules.Clock(h.EndedAt)}  {w.NameOf(h.Player)}: {d.Title}: {(h.Dropped != null ? $"given up, {h.Dropped}" : h.End >= 0 ? $"ended by {d.Ends[h.End].Id}" : "ended")} ({StoryRules.Span(h.EndedAt - h.StartedAt)})");
            }
        }
    }

    private void QualityRow(StoryEngine s, int me, ushort id, string name, int min, int max, string kind)
    {
        var v = s.World.Quality(me, id);
        if (ImGui.SmallButton($"-##stslq{id}") && v > min)
            s.World.SetQuality(me, id, v - 1);
        ImGui.SameLine();
        if (ImGui.SmallButton($"+##stslq{id}") && v < max)
            s.World.SetQuality(me, id, v + 1);
        ImGui.SameLine();
        ImGuiSafe.TextColored(StoryPlain, $"{name} {v}{(kind.Length > 0 ? $"  ({kind}, {min} to {max})" : "")}");
    }

    private static string StoryletTip(StoryEngine s, StoryletDef d)
    {
        var lines = new List<string> { $"{d.Id} ({d.File}){(d.Final ? ", final" : "")}{(d.Group ? ", for the whole group" : "")}{(d.Again > 0 ? $", again after {StoryRules.Span(d.Again)}" : ", once")}" };
        if (d.About.Length > 0)
            lines.Add(d.About);
        if (d.Needs.Length > 0)
            lines.Add($"needs: {string.Join("   OR   ", d.Needs.Select(n => n.Text))}");
        if (d.Cast != null)
            lines.Add($"cast: {d.Cast.Text}{(d.PreferText.Length > 0 ? $" (prefer {d.PreferText})" : "")}");
        if (d.Play >= 0)
            lines.AddRange(s.Rules.Plays.Plays[d.Play].Beats.Select(b => "  " + b.Text));
        if (d.JobText.Length > 0)
            lines.Add($"job: {d.JobText}");
        if (d.Hook.Length > 0)
            lines.Add($"hook: {d.Hook}");
        foreach (var e in d.Ends)
            lines.Add($"end {e.Id}: {e.Text}");
        return string.Join("\n", lines);
    }

    private static string EndTip(StoryEngine s, StoryletDef d, StoryletEnd e)
    {
        var lines = new List<string>();
        if (e.Then.Length > 0)
            lines.Add($"then: {e.Then.Length} change(s)");
        if (e.Learn >= 0)
            lines.Add($"learns {d.Named[e.Learn - d.CastVars]}");
        if (e.Hook.Length > 0)
            lines.Add($"hook: {e.Hook}");
        if (e.Next.Length > 0)
            lines.Add($"leads to {e.Next}");
        lines.AddRange(e.Say.Select(l => $"\"{l}\""));
        return lines.Count > 0 ? string.Join("\n", lines) : "changes nothing";
    }
}
