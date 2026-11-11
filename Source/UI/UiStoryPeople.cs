using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Story window's People tab (docs/story/STORY.md "People"): everyone with a mind of their own, where they are and
/// what they are doing; for the one picked, their needs, how each goal scores and why, the plan tree with the step under
/// way, what they did lately and what they know (and who told them). A cast can be added to the live world from here, and
/// the people's bodies in it turned on or off (GameStoryBodies.cs).
/// </summary>
public sealed partial class Ui
{
    private int _stPerson = -1; // the entity picked
    private int _stCast;
    private string _stCastNote = "";

    private static readonly float4 StoryFailed = new(0.85f, 0.4f, 0.4f, 1f), StoryPlain = new(0.9f, 0.9f, 0.92f, 1f), StoryBodied = new(0.55f, 0.85f, 0.55f, 1f);

    private void DrawStoryPeople(Game game, StoryEngine s)
    {
        var ag = s.Agents;
        var p = s.Rules.People;
        var w = s.World;
        if (!p.Any)
        {
            ImGuiSafe.TextWrapped("No people: Story/People has no goals or actions (or was refused: see Data, under Engine).");
            return;
        }
        var all = StoryEngineTabs;
        if (all)
            ImGuiSafe.TextColored(Dim, $"{ag.Count} people{(ag.Deaths > 0 ? $" ({ag.Deaths} died this session)" : "")}. {ag.Thinks} thinks, {ag.PlansMade} plans ({ag.PlanFails} found none), {ag.StepsDone} steps. A think takes {ag.ThinkMs / Math.Max(1, ag.Thinks) * 1000:0} us on average (last {ag.LastThinkMs * 1000:0} us, worst {ag.WorstThinkMs:0.00} ms); {ag.PerTick} may think a tick.");
        if (p.Casts.Length > 0)
        {
            _stCast = Math.Clamp(_stCast, 0, p.Casts.Length - 1);
            ImGui.SetNextItemWidth(160 * _scale);
            if (ImGui.BeginCombo("##stcast"u8, p.Casts[_stCast].Name))
            {
                for (var i = 0; i < p.Casts.Length; i++)
                    if (ImGui.Selectable($"{p.Casts[i].Name}##stc{i}", i == _stCast))
                        _stCast = i;
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip($"{p.Casts[_stCast].About} ({p.Casts[_stCast].File}: {p.Casts[_stCast].People.Length} people in {p.Casts[_stCast].Places.Length} places)");
            ImGui.SameLine();
            if (ImGui.Button("Add cast"u8))
            {
                var n = game.StoryAddCast(p.Casts[_stCast]);
                _stCastNote = n > 0 ? $"{n} came (their world place is this map)." : "They are here already.";
            }
            ImGui.SameLine();
        }
        ImGui.BeginDisabled(ag.Count == 0);
        if (ImGui.Button("Skip 10 min"u8))
            s.Advance(600); // (a second at a time: they live through it)
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip("Moves the story clock on ten minutes at once (the game's clock doesn't move).");
        ImGui.SameLine();
        ImGui.BeginDisabled(game.Local is not { Alive: true });
        if (ImGui.Button("Call craft"u8) && game.Local is { } me)
            game.StandInTest(me.Position);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip("A placeholder shuttle, hover hauler and courier flyer come in near you, stand for ten seconds and go.");
        if (all)
        {
            ImGui.SameLine();
            var bodies = game.StoryBodiesOn;
            if (ImGui.Checkbox("Bodies"u8, ref bodies))
                game.StoryBodiesOn = bodies;
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip($"The people in this world walk about it ({game.StoryBodyCount} now, {Game.StoryBodiesMax} at most; {game.StoryBodiesMade} came and {game.StoryBodiesGone} went this session). They go where each step happens, a step with someone waits until they have met ({ag.MeetWaits} waits, {ag.Unreached} never met), and a body killed is the person killed, for good. Off: the story's clock alone.");
            ImGui.SameLine();
            // STAND-IN: the placeholder craft (Source/StandIns), until the vehicle and ship redo.
            var craft = game.StandInsOn;
            if (ImGui.Checkbox("Craft"u8, ref craft))
                game.StandInsOn = craft;
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip($"Placeholder craft (yellow and black, only pictures) carry the people on their roads: a shuttle for a long road, a hover hauler for a short one, and a courier flyer comes for a sealed Requisition Crate. {game.StandInCount} about now, {game.StandInsMade} this session. They stand in for the real vehicles and ships until those are redone.");
            ImGui.SameLine();
            var talk = s.Talk.On;
            if (ImGui.Checkbox("Talk"u8, ref talk))
                s.Talk.On = talk;
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip($"The people say things (Story/Lines, the response rules): {s.Talk.Lines} lines so far, {game.StoryLinesHeard} heard by players near the speaker, {game.StoryBroadcasts} radio broadcasts that every player heard, {s.Talk.Quiet} times a rule kept quiet. Off: nobody says anything (the radio still puts news about in the story).");
        }
        if (_stCastNote.Length > 0)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, _stCastNote);
        }
        if (ag.Count == 0)
        {
            ImGuiSafe.TextWrapped("Nobody yet. Add a cast (Story/Casts) or play a scenario with one (Data, under Engine), like Frontier.");
            return;
        }
        if (ag.Of(_stPerson) == null)
            _stPerson = ag.Of(_stSel) != null ? _stSel : ag.All[0].Entity;

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##stpeople"u8, new float2(avail.X * 0.42f, avail.Y), ImGuiChildFlags.Borders))
        {
            if (ImGui.BeginTable("##stpeopletab"u8, 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Person"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Doing"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Goal"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var a in ag.All)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var bodied = game.StoryBodyOf(a.Entity) != null;
                    ImGui.PushStyleColor(ImGuiCol.Text, a.Dead ? Dim : bodied ? StoryBodied : StoryPlain);
                    if (ImGui.Selectable($"{w.NameOf(a.Entity)}{(a.Dead ? " (dead)" : "")}##stp{a.Entity}", a.Entity == _stPerson, ImGuiSelectableFlags.SpanAllColumns))
                        _stPerson = _stSel = a.Entity;
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered() && bodied)
                        ImGuiSafe.SetItemTooltip("Has a body in this world (green).");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(a.Busy ? StoryPlain : Dim, $"{DoingText(s, a)}, {WhereText(s, a)}");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(a.Goal >= 0 ? Orange : Dim, a.Goal >= 0 ? $"{p.Goals[a.Goal].Name}{(a.GoalTarget >= 0 ? $": {w.NameOf(a.GoalTarget)}" : "")}" : "none");
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##stperson"u8, new float2(0, avail.Y), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
        {
            if (ag.Of(_stPerson) is { } a)
                DrawStoryPerson(game, s, a);
        }
        ImGui.EndChild();
    }

    private static string WhereText(StoryEngine s, StoryAgent a) =>
        a.Dead ? "nowhere" : s.Agents.Where(a.Entity) is var at and >= 0 ? $"at {s.World.NameOf(at)}" : a.Busy && a.DoingFromPlace >= 0 ? $"on the road to {s.World.NameOf(a.Doing.Place)}" : "nowhere";

    private static string DoingText(StoryEngine s, StoryAgent a)
    {
        if (a.Dead)
            return a.DiedAt >= 0 ? $"dead since {StoryRules.Clock(a.DiedAt)}" : "dead";
        if (a.Meeting)
            return $"{s.Agents.StepText(a.Doing)} (on the way to them)";
        if (!a.Busy)
            return "waiting";
        var span = a.DoingUntil - a.DoingFrom;
        var done = span > 0 ? Math.Clamp((s.Time - a.DoingFrom) / span, 0, 1) : 1;
        return $"{s.Agents.StepText(a.Doing)} ({done * 100:0}%)";
    }

    private void DrawStoryPerson(Game game, StoryEngine s, StoryAgent a)
    {
        var ag = s.Agents;
        var p = s.Rules.People;
        var w = s.World;
        var e = w[a.Entity];
        var now = s.Time;
        var here = ag.Where(a.Entity);
        ImGuiSafe.TextColored(Orange, e.Name);
        ImGui.SameLine();
        var all = StoryEngineTabs;
        ImGuiSafe.TextColored(Dim, !all ? $"  {WhereText(s, a)}" : $"  {WhereText(s, a)}; {a.Steps} steps ({a.StepFails} failed), {a.Plans} plans ({a.PlanFails} found none){(a.Scripted ? "; from a scenario" : "")}");
        if (a.Dead)
            ImGuiSafe.TextColored(StoryFailed, $"{DoingText(s, a)}: they do nothing more, and nobody has them in mind.");
        else if (a.Busy)
        {
            var span = a.DoingUntil - a.DoingFrom;
            ImGui.ProgressBar(span > 0 ? (float)Math.Clamp((now - a.DoingFrom) / span, 0, 1) : 1, new float2(-1, 0), $"{ag.StepText(a.Doing)}: {StoryRules.Span(Math.Max(0, a.DoingUntil - now))} to go");
            if (a.Meeting)
                ImGuiSafe.TextColored(Dim, $"Its time is up, but their bodies haven't met yet: they wait {StoryRules.Span(Math.Max(0, a.DoingUntil + StoryAgents.MeetWait - now))} more at most.");
        }
        else
            ImGuiSafe.TextColored(Dim, $"Waiting; thinks again in {StoryRules.Span(Math.Max(0, a.WakeAt - now))}.");
        if (game.StoryBodyOf(a.Entity) is { } body)
        {
            var to = game.StoryBodyGoalOf(a.Entity) ?? body.Position;
            var far = MathF.Sqrt((to.X - body.Position.X) * (to.X - body.Position.X) + (to.Z - body.Position.Z) * (to.Z - body.Position.Z));
            if (all || !body.Alive || far > 1.5f)
                ImGuiSafe.TextColored(StoryBodied, !body.Alive ? (all ? $"Body: player {body.Id}, lying dead" : "Their body lies dead.")
                    : all ? $"Body: player {body.Id} at {body.Position.X:0}, {body.Position.Z:0}{(far > 1.5f ? $", going to {to.X:0}, {to.Z:0} ({far:0} m)" : ", there")}"
                    : $"Walking there: {far:0} m to go.");
            if (body.Alive && game.StorySightOf(a.Entity) is { } react)
                ImGuiSafe.TextColored(StoryBodied, $"Sight: {react}."); // (GameStorySight.cs)
            if (body.Alive && game.StoryMoodOf(a.Entity, Brutal.GlfwApi.Glfw.Time) is { } md)
            {
                var (mood, left) = md;
                ImGuiSafe.TextColored(StoryNudged, $"Mood: {mood.Name}, {mood.Label}; {left:0} s left: {MoodRules.Effects(mood)}."); // (GameStoryMoods.cs)
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetItemTooltip(mood.About.Length > 0 ? mood.About : "A mood from Story/Moods.");
            }
        }
        else if (!a.Dead)
            ImGuiSafe.TextColored(Dim, game.StoryBodiesOn ? "No body: not in this world now." : "No body: bodies are off.");
        if (a.Why != AgentWhy.None && (all || a.Why != AgentWhy.Idle))
            ImGuiSafe.TextColored(a.Why == AgentWhy.Idle ? Dim : StoryFailed, $"Last: {ag.WhyText(a)}");
        if (s.Drama.Of(a, now) is var di and >= 0)
        {
            ref readonly var dir = ref s.Drama.Directions[di];
            ImGuiSafe.TextColored(dir.Followed ? StoryDone : StoryNudged, $"Nudged: {s.Drama.Words(dir)}; {StoryRules.Span(Math.Max(0, dir.Until - now))} left{(dir.Followed ? " (did it)" : "")}");
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip("A stage direction from the drama manager (Drama tab): their goals that lead to it score that much more.");
        }

        // Needs as bars, then the other qualities (traits, wealth) on a line.
        var barW = Math.Max(80 * _scale, (ImGui.GetContentRegionAvail().X - 12 * _scale) / Math.Max(1, p.Needs.Length) - 8 * _scale);
        for (var n = 0; n < p.Needs.Length; n++)
        {
            if (n > 0)
                ImGui.SameLine();
            var v = ag.NeedNow(a, n, now);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, v < 25 ? new float4(0.8f, 0.3f, 0.25f, 1f) : v < 55 ? new float4(0.8f, 0.6f, 0.2f, 1f) : new float4(0.3f, 0.6f, 0.35f, 1f));
            ImGui.ProgressBar(v / 100f, new float2(barW, 0), $"{p.Needs[n].Name} {v:0}");
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip($"{p.Needs[n].Name}: {p.Needs[n].About}; falls {p.Needs[n].FallsPerSecond * 3600:0.#} an hour");
        }
        var traits = new List<string>();
        for (var i = 0; i < e.QualityCount; i++)
        {
            var q = e.Qualities[i].Quality;
            if (Array.FindIndex(p.Needs, n => n.Quality == q) < 0)
                traits.Add($"{s.Rules.Qualities[q].Name} {e.Qualities[i].Value}");
        }
        if (traits.Count > 0)
            ImGuiSafe.TextWrapped(string.Join(", ", traits));
        // How they feel about others: a line per relation (pairs are sorted by relation).
        for (var i = 0; i < e.PairCount;)
        {
            var rel = e.Pairs[i].Rel;
            var j = i;
            while (j < e.PairCount && e.Pairs[j].Rel == rel)
                j++;
            if (rel != p.In && rel != p.Home && rel != p.WorksAt && rel != p.Road)
            {
                var parts = new List<string>(j - i);
                for (var k = i; k < j; k++)
                    parts.Add(e.Pairs[k].Value != 1 ? $"{w.NameOf(e.Pairs[k].Target)} {e.Pairs[k].Value}" : w.NameOf(e.Pairs[k].Target));
                ImGui.PushStyleColor(ImGuiCol.Text, StoryRelColour(rel));
                ImGuiSafe.TextWrapped($"{s.Rules.Relations[rel].Name}: {string.Join(", ", parts)}");
                ImGui.PopStyleColor();
            }
            i = j;
        }

        if (all && ImGui.CollapsingHeader("Goals: utility, best first"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            Span<int> order = stackalloc int[p.Goals.Length];
            for (var g = 0; g < order.Length; g++)
                order[g] = g;
            order.Sort((x, y) => a.Scores[y].CompareTo(a.Scores[x]));
            if (ImGui.BeginTable("##stgoals"u8, 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Goal"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Score"u8, ImGuiTableColumnFlags.WidthFixed, 110 * _scale);
                ImGui.TableSetupColumn("For"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var g in order)
                {
                    var goal = p.Goals[g];
                    var score = a.Scores[g];
                    var t = a.ScoreTargets[g];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var current = g == a.Goal;
                    ImGuiSafe.TextColored(current ? Orange : score >= StoryAgents.MinScore ? StoryPlain : Dim, $"{(current ? "> " : "")}{goal.Name}");
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetItemTooltip(GoalTip(s, a, goal, t, here, now));
                    ImGui.TableNextColumn();
                    ImGui.ProgressBar(Math.Clamp(score / Math.Max(0.01f, goal.Weight), 0, 1), new float2(-1, 0), $"{score:0.00}");
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetItemTooltip(GoalTip(s, a, goal, t, here, now));
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(Dim, t >= 0 ? w.NameOf(t) : goal.Pick != null ? "nobody" : "");
                }
                ImGui.EndTable();
            }
            ImGuiSafe.TextColored(Dim, $"As of their last think. Each goal's score is its weight times every consideration (hover one); the one under way counts {StoryAgents.KeepBonus:0.#}x; below {StoryAgents.MinScore:0.##} nothing is worth doing.");
        }

        if (all && ImGui.CollapsingHeader("Plan"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (a.TreeCount == 0 || a.Goal < 0)
                ImGuiSafe.TextColored(Dim, "No plan.");
            else
            {
                ImGuiSafe.TextColored(Orange, $"{p.Goals[a.Goal].Name}{(a.GoalTarget >= 0 ? $" ({w.NameOf(a.GoalTarget)})" : "")}, score {a.GoalScore:0.00}");
                for (var k = 0; k < a.TreeCount; k++)
                {
                    ref readonly var node = ref a.Tree[k];
                    ImGui.Indent((node.Depth + 1) * 14 * _scale);
                    if (node.Action)
                    {
                        var step = -1;
                        for (var i = 0; i < a.PlanCount; i++)
                            if (a.Plan[i].Node == k)
                                step = i;
                        var state = step < 0 ? 0 : step < a.PlanNext ? 1 : step == a.PlanNext && a.Busy ? 2 : 3;
                        var text = step >= 0 ? ag.StepText(a.Plan[step]) : p.Actions[node.Index].Name;
                        StoryStage($"{(state == 1 ? "done: " : state == 2 ? "now: " : "")}{text}##stpl{k}", state == 1 ? StoryDone : state == 2 ? StoryWaiting : StoryLater,
                            $"action {p.Actions[node.Index].Name}: {(p.Actions[node.Index].If?.Text ?? "always")}");
                    }
                    else
                    {
                        var task = p.Tasks[node.Index];
                        var args = task.Params.Length == 0 ? "" : $"({string.Join(", ", new[] { node.A, node.B }.Take(task.Params.Length).Select(x => w.NameOf(x)))})";
                        ImGuiSafe.TextColored(StoryPlain, $"{task.Name}{args}: {task.Methods[node.Method].Name}");
                        if (ImGui.IsItemHovered())
                            ImGuiSafe.SetItemTooltip($"{task.About}\nmethod {task.Methods[node.Method].Name}: if {task.Methods[node.Method].If?.Text ?? "always"}");
                    }
                    ImGui.Unindent((node.Depth + 1) * 14 * _scale);
                }
            }
        }

        if (ImGui.CollapsingHeader("Lately, newest first"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (var k = 0; k < a.HistoryCount; k++)
            {
                ref readonly var h = ref a.History[(a.HistoryNext - 1 - k + StoryAgent.HistorySize) % StoryAgent.HistorySize];
                var act = p.Actions[h.Action];
                ImGuiSafe.TextColored(Dim, StoryRules.Clock(h.At));
                ImGui.SameLine();
                ImGuiSafe.TextColored(h.Failed ? StoryFailed : StoryPlain, $"{act.Name}{(h.Target >= 0 ? $" {w.NameOf(h.Target)}" : "")}{(h.Place >= 0 ? $" at {w.NameOf(h.Place)}" : "")}{(h.Failed ? " (failed)" : "")}");
            }
            if (a.HistoryCount == 0)
                ImGuiSafe.TextColored(Dim, "Nothing yet.");
        }

        if (ImGui.CollapsingHeader("What they said lately"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var any = false;
            foreach (var t in s.Talk.SaidBy(a.Entity))
            {
                any = true;
                ImGuiSafe.TextColored(Dim, StoryRules.Clock(t.At));
                ImGui.SameLine();
                ImGuiSafe.TextColored(t.Radio ? StoryDone : StoryPlain, $"{(t.Radio ? "[RADIO] " : t.To >= 0 ? $"to {w.NameOf(t.To)}: " : "")}\"{t.Text}\"");
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetItemTooltip(t.Play >= 0 ? $"the play {s.Rules.Plays.Plays[t.Play].Name}, beat {t.Rule + 1}: {s.Rules.Plays.Plays[t.Play].Beats[t.Rule].Text}"
                        : $"{s.Rules.Lines.Concepts[t.Concept]}: the rule {s.Rules.Lines.Rules[t.Rule].Text} ({s.Rules.Lines.Rules[t.Rule].File}, {s.Rules.Lines.Rules[t.Rule].Weight} criteria)");
            }
            if (!any)
                ImGuiSafe.TextColored(Dim, "Nothing lately. People speak when a step plays out with their body, a player comes up to them, they are hurt, or they see something happen; the radio goes out to everyone.");
        }

        if (ImGui.CollapsingHeader("What they know, most interesting first"u8, all ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
        {
            Span<int> order = stackalloc int[a.KnownCount];
            for (var k = 0; k < order.Length; k++)
                order[k] = k;
            order.Sort((x, y) => StoryAgents.Faded(a.Known[y], now).CompareTo(StoryAgents.Faded(a.Known[x], now)));
            foreach (var k in order)
            {
                ref readonly var it = ref a.Known[k];
                var ev = new StoryEvent { Id = it.Event, Verb = it.Verb, Actor = it.Actor, Target = it.Target, Owner = it.Owner, Place = it.Place, Item = -1 };
                ImGuiSafe.TextColored(Dim, $"{StoryAgents.Faded(it, now),3:0}");
                ImGui.SameLine();
                ImGuiSafe.TextColored(StoryPlain, s.Sentence(ev));
                ImGui.SameLine();
                ImGuiSafe.TextColored(Dim, it.Source switch
                {
                    KnowSource.Told => $"(told by {w.NameOf(it.From)}, {StoryRules.Clock(it.At)})",
                    KnowSource.Did => $"(did it, {StoryRules.Clock(it.At)})",
                    KnowSource.Radio => $"(on the radio, {StoryRules.Clock(it.At)})",
                    KnowSource.Road => $"(heard it on the road, {StoryRules.Clock(it.At)})",
                    _ => $"(saw it, {StoryRules.Clock(it.At)})",
                });
            }
            if (a.KnownCount == 0)
                ImGuiSafe.TextColored(Dim, "Nothing worth passing on.");
            ImGuiSafe.TextColored(Dim, $"The number is how interesting it still is (it halves every {StoryRules.Span(StoryAgents.NewsHalfLife)}); at most {StoryAgent.MaxKnown} are kept.");
        }
    }

    /// <summary>A goal's score taken apart: the weight and each consideration's factor.</summary>
    private static string GoalTip(StoryEngine s, StoryAgent a, GoalDef goal, int t, int here, double now)
    {
        var lines = new List<string> { $"{goal.Name}: {goal.About}", $"weight {goal.Weight:0.##}" };
        foreach (var c in goal.Score)
            lines.Add($"x {s.Agents.Factor(a, goal, c, t, here, now):0.00}  {c.Text}");
        if (s.Drama.GoalFactor(a, goal.Id, t, now) is var df and not 1 && s.Drama.Of(a, now) is var di and >= 0)
            lines.Add($"x {df:0.00}  nudged: {s.Drama.Words(s.Drama.Directions[di])}");
        lines.Add("x 0.92 to 1.08  (a little each way, so people differ)");
        if (goal.If != null)
            lines.Add($"if {goal.If.Text}");
        if (goal.Pick != null)
            lines.Add($"pick {goal.Pick.Text}{(t >= 0 ? $": {s.World.NameOf(t)}" : "")}");
        lines.Add($"do {goal.Do.Text}");
        return string.Join("\n", lines);
    }
}
