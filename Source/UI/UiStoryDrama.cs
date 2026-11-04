using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Story window's Drama tab (docs/story/STORY.md "Drama"): the drama manager's stage directions (who is nudged
/// towards what, for which story, how hard, for how long, and whether they did it), the stories in progress as it ranked
/// them with why, and what it did lately. It can be turned off (the people go their own way) and its strength set.
/// </summary>
public sealed partial class Ui
{
    private static readonly float4 StoryNudged = new(0.55f, 0.75f, 0.95f, 1f);

    private void DrawStoryDrama(StoryEngine s)
    {
        var d = s.Drama;
        var w = s.World;
        var now = s.Time;
        var on = d.On;
        if (ImGui.Checkbox("Nudge the people"u8, ref on))
        {
            d.On = on;
            d.Rank(now);
        }
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip("The drama manager ranks the stories in progress and nudges the person who would act next in the best few towards what the story waits for: their goals that lead there score higher, and news that fits is worth more to tell. Off, they go their own way.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160 * _scale);
        var strength = d.Strength;
        if (ImGui.SliderFloat("##stdrstrength"u8, ref strength, 0, 1.5f, "strength %.2f"u8))
            d.Strength = strength;
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip($"The most a direction multiplies a goal by is 1 + strength (now x{1 + d.Strength:0.00}); the best ranked story gets all of it, the rest half or more. Applies from the next rank.");
        ImGui.SameLine();
        if (ImGui.Button("Rank now"u8))
            d.Rank(now);
        DimWrapped($"{d.Given} directions given, {d.Followed} followed by their person, {d.MovedOn} done by someone else first, {d.Lapsed} lapsed. Ranked {d.Ranks} times (on an event, or every {StoryDrama.RankEvery:0} s), {d.TotalRankMs / Math.Max(1, d.Ranks) * 1000:0} us each on average, last {d.LastRankMs * 1000:0} us, worst {d.WorstRankMs:0.00} ms.");
        if (s.Agents.Count == 0)
        {
            ImGuiSafe.TextWrapped("Nobody to nudge: the drama manager works on the people (People > Add cast, or a scenario with a cast). Players' own stories are theirs.");
            return;
        }
        var k = d.Skipped;
        DimWrapped($"Last rank: {d.CandidateCount} could be nudged. Not: {k[(int)DramaSkip.Player]} wait for a player, {k[(int)DramaSkip.NotForPeople]} for a verb people can't take yet, {k[(int)DramaSkip.NobodyKnows]} for anyone but nobody knows, {k[(int)DramaSkip.Unwilling]} for someone with no goal that would take it, {k[(int)DramaSkip.LeftAlone]} are left alone by their data.");

        if (ImGui.CollapsingHeader("Stage directions"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (d.LiveCount == 0)
                ImGuiSafe.TextColored(Dim, d.On ? "None now." : "None: off.");
            else if (ImGui.BeginTable("##stdirs"u8, 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Person"u8, ImGuiTableColumnFlags.WidthStretch, 0.8f);
                ImGui.TableSetupColumn("Nudged to"u8, ImGuiTableColumnFlags.WidthStretch, 1.3f);
                ImGui.TableSetupColumn("For the story"u8, ImGuiTableColumnFlags.WidthStretch, 1.4f);
                ImGui.TableSetupColumn("x"u8, ImGuiTableColumnFlags.WidthFixed, 50 * _scale);
                ImGui.TableSetupColumn("Left"u8, ImGuiTableColumnFlags.WidthFixed, 110 * _scale);
                ImGui.TableHeadersRow();
                for (var i = 0; i < d.LiveCount; i++)
                {
                    ref readonly var dir = ref d.Directions[i];
                    var p = s.Rules.Patterns[dir.Pattern];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (ImGui.Selectable($"{w.NameOf(dir.Agent)}##stdir{i}", dir.Agent == _stPerson, ImGuiSelectableFlags.SpanAllColumns))
                        _stPerson = _stSel = dir.Agent;
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetItemTooltip($"{p.Name}: {p.About}\nwaits for: {p.Stages[dir.Stage].Text}\nso far: {s.Sifter.BindText(dir.Pattern, dir.Binds)}\n(click: their page on the People tab)");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(dir.Followed ? StoryDone : StoryNudged, $"{DirectionVerb(s, dir)}{(dir.Followed ? " (did it)" : "")}");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(StoryPlain, $"{p.Name} {dir.Stage}/{p.Stages.Length}: {s.Sifter.BindText(dir.Pattern, dir.Binds)}");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(StoryPlain, $"{1 + dir.Bonus:0.00}");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(Dim, StoryRules.Span(Math.Max(0, dir.Until - now)));
                }
                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader("Stories in progress, as ranked"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var n = Math.Min(d.CandidateCount, 40);
            if (n == 0)
                ImGuiSafe.TextColored(Dim, "Nothing the people could move on.");
            else if (ImGui.BeginTable("##stdrank"u8, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Story"u8, ImGuiTableColumnFlags.WidthStretch, 1.4f);
                ImGui.TableSetupColumn("Waits for"u8, ImGuiTableColumnFlags.WidthStretch, 1.2f);
                ImGui.TableSetupColumn("Who"u8, ImGuiTableColumnFlags.WidthStretch, 0.8f);
                ImGui.TableSetupColumn("Score"u8, ImGuiTableColumnFlags.WidthFixed, 90 * _scale);
                ImGui.TableHeadersRow();
                var top = d.Candidates[0].Score;
                for (var i = 0; i < n; i++)
                {
                    ref readonly var c = ref d.Candidates[i];
                    var p = s.Rules.Patterns[c.Pattern];
                    ref readonly var m = ref s.Sifter.Pool[c.Match];
                    var still = m.Live && m.Started == c.Started && m.Stage == c.Stage;
                    var directed = false;
                    for (var j = 0; j < d.LiveCount && !directed; j++)
                        directed = d.Directions[j].Agent == c.Agent && d.Directions[j].Match == c.Match;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(!still ? Dim : directed ? StoryNudged : StoryPlain, $"{p.Name} {c.Stage}/{p.Stages.Length}{(still ? $": {s.Sifter.BindText(c.Pattern, m.Binds)}" : " (moved on since)")}");
                    var tip = $"{p.Name}: {p.About}\nscore {c.Score:0.000} = drama {c.Drama:0.##} x progress {c.Progress:0.##} x new {c.Novelty:0.##} (told {s.Sifter.Completed[c.Pattern]} times) x players {c.Players:0.##} x pressing {c.Urgency:0.##} x ready {c.Would:0.##}{(c.Keep != 1 ? $" x held {c.Keep:0.##}" : "")}";
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetItemTooltip(tip);
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(Dim, p.Stages[c.Stage].Text);
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(directed ? StoryNudged : StoryPlain, w.NameOf(c.Agent));
                    ImGui.TableNextColumn();
                    ImGui.ProgressBar(top > 0 ? c.Score / top : 0, new float2(-1, 0), $"{c.Score:0.000}");
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetItemTooltip(tip);
                }
                ImGui.EndTable();
            }
            DimWrapped($"Best first, as of the last rank; blue ones hold a direction ({StoryDrama.MaxDirections} at most, one a person). Hover a story for its score taken apart.");
        }

        if (ImGui.CollapsingHeader("What it did, newest first"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var shown = 0;
            for (var i = d.NoteTotal - 1; i >= 0 && i >= d.NoteTotal - StoryDrama.NoteRing && shown < 60; i--, shown++)
            {
                ref readonly var note = ref d.Notes[i % StoryDrama.NoteRing];
                ImGuiSafe.TextColored(Dim, StoryRules.Clock(note.At));
                ImGui.SameLine();
                ImGuiSafe.TextColored(note.Kind switch
                {
                    DramaKind.Given => StoryNudged,
                    DramaKind.Followed => StoryDone,
                    DramaKind.MovedOn => StoryPlain,
                    _ => Dim,
                }, $"{w.NameOf(note.Direction.Agent)}: {DirectionVerb(s, note.Direction)} for {s.Rules.Patterns[note.Direction.Pattern].Name} {note.Kind switch
                {
                    DramaKind.Given => "(nudged)",
                    DramaKind.Followed => "(did it)",
                    DramaKind.MovedOn => "(someone else did it first)",
                    _ => "(lapsed)",
                }}");
            }
            if (shown == 0)
                ImGuiSafe.TextColored(Dim, "Nothing this session (the drama manager's notes aren't saved).");
        }
    }

    private static void DimWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Dim);
        ImGuiSafe.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>"Tell Lena (of Rook and Mara)": the verb and who, without the story.</summary>
    private static string DirectionVerb(StoryEngine s, in StageDirection d)
    {
        var w = s.World;
        var text = $"{s.Rules.Verbs[d.Verb].Name} {(d.Target >= 0 ? w.NameOf(d.Target) : "anyone")}";
        if (d.Item >= 0 || d.Owner >= 0)
            text += $" (of {(d.Item >= 0 ? w.NameOf(d.Item) : "anyone")} and {(d.Owner >= 0 ? w.NameOf(d.Owner) : "anyone")})";
        return text;
    }
}
