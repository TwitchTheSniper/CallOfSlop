using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Story window's Scenes and Roster tabs (docs/story/STORY.md "Plays", "Places" and "Roster"): the plays under way
/// (who plays whom, the beat they are at) and lately, each place's state and why, and the legacy roster of everyone the
/// story has known. Plays can be turned off, and one tried at once when it can be cast.
/// </summary>
public sealed partial class Ui
{
    private List<RosterEntry>? _stRoster;
    private double _stRosterAt = double.MinValue;
    private int _stRosterShow; // 0 everyone, 1 people, 2 players, 3 the dead
    private string _stPlayNote = "";
    private static readonly string[] StoryRosterShows = ["Everyone", "People", "Players", "The dead"];

    private void DrawStoryScenes(StoryEngine s)
    {
        var pl = s.Plays;
        var rules = s.Rules;
        var w = s.World;
        var now = s.Time;
        var on = pl.On;
        if (ImGui.Checkbox("Plays"u8, ref on))
            pl.On = on;
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip("Plays are short scenes cast from the world (Story/Plays): when their query finds people free and together, they act them out in beats of lines and actions. Off, none starts (one under way finishes).");
        ImGui.SameLine();
        ImGui.BeginDisabled(pl.RunningCount == 0);
        if (ImGui.Button("Stop them"u8))
            pl.StopAll(now);
        ImGui.EndDisabled();
        DimWrapped($"{pl.Started} started, {pl.Finished} played out, {pl.BrokenOff} broken off; {rules.Plays.Plays.Length} plays in the data. A look for one every {StoryPlays.LookEvery:0} s ({pl.Looks} so far), {StoryPlays.MaxRunning} at once at most, one a place.");
        if (s.Agents.Count == 0)
            ImGuiSafe.TextWrapped("Nobody to act them: plays cast the people (People > Add cast).");

        if (ImGui.CollapsingHeader("Under way"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (pl.RunningCount == 0)
                ImGuiSafe.TextColored(Dim, "None now.");
            for (var i = 0; i < pl.RunningCount; i++)
            {
                ref readonly var r = ref pl.Running[i];
                var p = rules.Plays.Plays[r.Play];
                ImGuiSafe.TextColored(Orange, $"{p.Name} at {w.NameOf(r.Place)}, beat {Math.Min(r.Beat + 1, p.Beats.Length)}/{p.Beats.Length}{(r.WaitSince >= 0 ? $" (waiting {now - r.WaitSince:0} s for them to meet)" : "")}");
                if (ImGui.IsItemHovered())
                {
                    var at = r.Beat;
                    ImGuiSafe.SetItemTooltip($"{p.About}\n{string.Join("\n", p.Beats.Select((b, k) => $"{(k == at ? "> " : "  ")}{b.Text}"))}");
                }
                ImGui.Indent();
                ImGuiSafe.TextColored(StoryPlain, pl.CastText(p, r.Cast));
                ImGui.Unindent();
            }
        }

        if (ImGui.CollapsingHeader("Lately"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (pl.HistoryCount == 0)
                ImGuiSafe.TextColored(Dim, "No play yet.");
            for (var k = 0; k < pl.HistoryCount; k++)
            {
                ref readonly var d = ref pl.History[(pl.HistoryNext - 1 - k + StoryPlays.HistorySize) % StoryPlays.HistorySize];
                var p = rules.Plays.Plays[d.Play];
                ImGuiSafe.TextColored(d.Broken == null ? StoryDone : StoryFailed, $"{StoryRules.Clock(d.StartedAt)}  {p.Name} at {w.NameOf(d.Place)}: {(d.Broken == null ? "played out" : $"broken off at beat {d.Beats + 1}, {d.Broken}")} ({d.Said} lines, {StoryRules.Span(d.EndedAt - d.StartedAt)})");
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetItemTooltip($"{p.About}\n{pl.CastText(p, d.Cast)}");
            }
        }

        if (ImGui.CollapsingHeader("Try one now"u8, ImGuiTreeNodeFlags.None))
        {
            DimWrapped("Starts the play now if it can be cast (its every and the quiet after big news aside).");
            foreach (var p in rules.Plays.Plays)
            {
                if (ImGui.Selectable($"{p.Name}##stplay{p.Id}", false))
                    _stPlayNote = pl.TryNow(p.Id, now) ? $"{p.Name}: begun." : $"{p.Name}: nobody fits now (or {StoryPlays.MaxRunning} are under way).";
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetItemTooltip($"{p.About}\ncast: {p.Cast.Text}{(p.Extra != null ? $"\nextra: {p.Extra.Text}" : "")}");
            }
            if (_stPlayNote.Length > 0)
                ImGuiSafe.TextColored(Dim, _stPlayNote);
        }

        if (ImGui.CollapsingHeader("Places"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var states = rules.Places.States;
            DimWrapped($"Each place is in the first state (Story/Places) whose terms hold, looked at every {StoryPlaces.LookEvery:0} s; it carries the state's quality and a change is a Turn event there. {s.Places.Turns} changes this session.");
            if (ImGui.BeginTable("##stplaces"u8, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Place"u8, ImGuiTableColumnFlags.WidthStretch, 0.8f);
                ImGui.TableSetupColumn("State"u8, ImGuiTableColumnFlags.WidthStretch, 0.7f);
                ImGui.TableSetupColumn("Since"u8, ImGuiTableColumnFlags.WidthFixed, 80 * _scale);
                ImGui.TableSetupColumn("Here now"u8, ImGuiTableColumnFlags.WidthStretch, 0.6f);
                ImGui.TableHeadersRow();
                for (var i = 0; i < w.Count; i++)
                {
                    if (w[i].Kind != StoryKind.Place || w[i].Gone)
                        continue;
                    var st = s.Places.StateOf(i);
                    var (people, players) = s.Places.Count(i);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(StoryPlain, w.NameOf(i));
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(st >= 0 ? Orange : Dim, st >= 0 ? states[st].Name : "as it was");
                    if (st >= 0 && ImGui.IsItemHovered())
                        ImGuiSafe.SetItemTooltip($"{states[st].About}\nwhen: {states[st].Text}\nat least {StoryRules.Span(states[st].Hold)}");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(Dim, st >= 0 ? StoryRules.Span(Math.Max(0, now - s.Places.SinceOf(i))) : "");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(Dim, $"{people} people, {players} players");
                }
                ImGui.EndTable();
            }
        }
    }

    private void DrawStoryRoster(StoryEngine s)
    {
        var w = s.World;
        if (_stRoster == null || s.Time - _stRosterAt > 5 || s.Time < _stRosterAt)
        {
            _stRoster = StoryRoster.Make(s); // (made when looked at, five seconds of story apart: it reads the whole chronicle)
            _stRosterAt = s.Time;
        }
        ImGui.SetNextItemWidth(130 * _scale);
        if (ImGui.BeginCombo("##strostershow"u8, StoryRosterShows[_stRosterShow]))
        {
            for (var i = 0; i < StoryRosterShows.Length; i++)
                if (ImGui.Selectable(StoryRosterShows[i], i == _stRosterShow))
                    _stRosterShow = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        DimWrapped($"{_stRoster.Count} names, {_stRoster.Count(r => r.Dead)} dead. Renown: a story they are in counts 5, a deed as much as it is news. Their line is the response rules' Remembered (Story/Lines/Roster.json).");
        if (!ImGui.BeginTable("##stroster"u8, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
            return;
        ImGui.TableSetupColumn("Name"u8, ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Now"u8, ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Renown"u8, ImGuiTableColumnFlags.WidthFixed, 60 * _scale);
        ImGui.TableSetupColumn("Did"u8, ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("In the stories"u8, ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Remembered"u8, ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableHeadersRow();
        foreach (var r in _stRoster)
        {
            if (_stRosterShow == 1 && r.Kind != StoryKind.Person || _stRosterShow == 2 && r.Kind != StoryKind.Player || _stRosterShow == 3 && !r.Dead)
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, r.Dead ? Dim : r.Kind == StoryKind.Player ? Orange : StoryPlain);
            if (ImGui.Selectable($"{r.Name}{(r.Dead ? " (dead)" : "")}##stros{r.Entity}", r.Entity == _stPerson, ImGuiSelectableFlags.SpanAllColumns))
                _stPerson = _stSel = r.Entity;
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip($"{(r.Kind == StoryKind.Player ? "a player" : "a person")}{(r.Home >= 0 ? $", lives at {w.NameOf(r.Home)}" : "")}{(r.Works >= 0 ? $", works at {w.NameOf(r.Works)}" : "")}; in the story since {StoryRules.Clock(r.Born)}\n{r.DeedCount} deeds: {StoryRoster.DeedText(s, r, 12)}\n{r.StoryCount} stories: {StoryRoster.StoryText(s, r, 12)}\n(click: their page on the People tab, and picked on the World tab)");
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(r.Dead ? StoryFailed : Dim, $"{StoryRoster.Status(s, r)}{(r.Dead && r.DiedAt >= 0 ? $", {StoryRules.Clock(r.DiedAt)}" : "")}");
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(StoryPlain, $"{r.Renown}");
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(StoryPlain, StoryRoster.DeedText(s, r));
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(StoryPlain, StoryRoster.StoryText(s, r));
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(Dim, r.Epitaph);
        }
        ImGui.EndTable();
    }
}
