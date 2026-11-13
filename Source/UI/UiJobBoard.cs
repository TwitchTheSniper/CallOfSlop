using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The job board (docs/story/STORY.md "Jobs", "The job board"): every story faction's work in one place, opened by the use
/// key on the Ministry Work Board in the colony yard (a lore item with <c>"opens": "jobs"</c>: Program.JobBoard.cs). A tab a
/// faction: your standing with it and a bar to its next tier (what that tier teaches), then each job: what it asks, what it
/// pays and how far that takes you towards the next tier, why it can't be taken yet, or how far along yours is, with Take
/// and Drop. The Faction window (F2) draws the same rows under "Jobs for the world's factions". The rows are what the
/// server sent this player (<see cref="Game.JobsView"/>, per party, so per player in co-op); tiers and pay come from the
/// faction files every peer has. The words are worked out again only when the view changes; closed, one bool check.
/// </summary>
public sealed partial class Ui
{
    public bool JobBoardOpen;
    public LoreDef? JobBoardLore; // the board's words, shown over the tabs (null: opened without a board)

    /// <summary>One faction's lines, made when the view changes (no strings made a frame).</summary>
    private sealed class JobBoardFaction
    {
        public string Tab = "", Head = "", Next = "", Teaches = "";
        public float Bar; // 0..1 through the current tier towards the next
        public string[] Title = [], About = [], Asks = [], Pays = [], Note = [], Take = [], Drop = [];
    }

    private JobBoardFaction[] _jb = [];
    private bool _jbPick; // the board was just opened: select its own faction's tab once
    private int _jbVersion = -1;
    private static readonly float4 JobHeld = new(0.45f, 0.9f, 0.45f, 1f), JobOpen = new(1f, 1f, 1f, 1f);

    /// <summary>Opens the board (with the words of the board looked at, or none), on that board's own faction's tab.</summary>
    public void OpenJobBoard(LoreDef? board)
    {
        JobBoardOpen = true;
        JobBoardLore = board;
        _jbPick = true;
    }

    public void DrawJobBoard(Game? game, int closeBind)
    {
        if (!JobBoardOpen)
            return;
        ImGui.SetNextWindowSize(new float2(620 * _scale, 600 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.96f);
        if (ImGui.Begin("Work Allocation###jobboard"u8, ref JobBoardOpen))
        {
            if (JobBoardLore is { } lore)
            {
                ImGuiSafe.TextColored(Orange, lore.Title);
                for (var k = 0; k < lore.Text.Length; k++)
                    ImGuiSafe.TextWrapped(lore.Text[k]);
                ImGui.Separator();
            }
            if (game is not { SandboxOn: true } || game.JobsView.Length == 0)
                ImGuiSafe.TextColored(Dim, "Nothing is posted: the factions' work is a story world's.");
            else if (ImGui.BeginTabBar("##jbtabs"u8))
            {
                JobBoardBuild(game);
                for (var f = 0; f < _jb.Length; f++)
                {
                    var pick = _jbPick && JobBoardLore is { OpensOn.Length: > 0 } l && l.OpensOn.Equals(game.JobsView[f].Name, StringComparison.OrdinalIgnoreCase);
                    if (!ImGui.BeginTabItem(_jb[f].Tab, pick ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                        continue;
                    ImGui.BeginChild("##jbrows"u8, new float2(0, -ImGui.GetFrameHeightWithSpacing()));
                    DrawJobFaction(game, f, false);
                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
                _jbPick = false;
            }
            ImGuiSafe.TextColored(Dim, closeBind == 0 ? "Esc closes the board." : $"{Keybinds.Name(closeBind)} or Esc closes the board.");
        }
        ImGui.End();
    }

    /// <summary>The words for every faction's rows, made again when the server's view has changed.</summary>
    private void JobBoardBuild(Game game)
    {
        if (_jbVersion == game.JobsVersion && _jb.Length == game.JobsView.Length)
            return;
        _jbVersion = game.JobsVersion;
        var view = game.JobsView;
        _jb = new JobBoardFaction[view.Length];
        for (var f = 0; f < view.Length; f++)
            _jb[f] = JobBoardWords(view[f], f);
    }

    private static JobBoardFaction JobBoardWords(JobFaction jf, int f)
    {
        var def = Game.StoryData.Factions.Find(jf.Name);
        var b = new JobBoardFaction
        {
            Tab = $"{jf.Name}{(Array.Exists(jf.Jobs, r => r.Held) ? " *" : "")}###jbt{f}",
            Head = $"{jf.Name}: you are {(jf.Tier.Length > 0 ? jf.Tier : "unknown to them")} ({jf.Standing:+0;-0;0} standing)",
        };
        // The next tier: how far there is to go, and what it teaches.
        var toGo = 0;
        var nextName = "";
        if (def is { Tiers.Length: > 0 } && def.TierOf(jf.Standing) is var t and >= 0 && t < def.Tiers.Length - 1)
        {
            var from = t > 0 ? def.Tiers[t - 1].Below : -FactionRules.MaxStanding;
            var at = def.Tiers[t].Below;
            toGo = Math.Max(1, at - jf.Standing);
            nextName = def.Tiers[t + 1].Name;
            b.Bar = Math.Clamp((jf.Standing - from) / (float)Math.Max(1, at - from), 0f, 1f);
            b.Next = $"{nextName} at {at:+0;-0;0}: {toGo} standing to go";
            var techs = Array.FindAll(def.Teaches, x => x.Tier == t + 1);
            if (techs.Length > 0)
                b.Teaches = $"{nextName} teaches: {string.Join(", ", Array.ConvertAll(techs, x => x.Name))}";
        }
        else if (def is { Tiers.Length: > 0 })
        {
            b.Bar = 1f;
            b.Next = "The top of their ladder.";
        }
        var n = jf.Jobs.Length;
        (b.Title, b.About, b.Asks, b.Pays, b.Note, b.Take, b.Drop) = (new string[n], new string[n], new string[n], new string[n], new string[n], new string[n], new string[n]);
        var same = def != null && def.Jobs.Length == n; // (the host's faction file is this peer's: the rows line up with its jobs)
        for (var j = 0; j < n; j++)
        {
            var row = jf.Jobs[j];
            var pay = same && def!.Jobs[j].Name == row.Name ? def.Jobs[j].Pay.Standing : 0;
            b.Title[j] = $"{(row.Held ? "[yours] " : "")}{row.Name}{(row.Ticket.Length > 0 ? $" ({row.Ticket})" : "")}";
            b.About[j] = row.About;
            b.Asks[j] = $"Asks: {row.What}.";
            b.Pays[j] = pay > 0 && nextName.Length > 0 && (row.Open || row.Held)
                ? pay >= toGo ? $"Pays {row.Pay}: enough for {nextName}." : $"Pays {row.Pay}: {pay} of the {toGo} to {nextName}."
                : $"Pays {row.Pay}.";
            b.Note[j] = row.Note.Length == 0 ? "" : row.Held ? $"So far: {row.Note}" : $"Not yet: {row.Note}";
            b.Take[j] = $"Take this job##jbt{f}_{j}";
            b.Drop[j] = $"Drop this job##jbd{f}_{j}";
        }
        return b;
    }

    /// <summary>One faction's standing, bar and jobs, with Take and Drop (the board's tab, or a block of the Faction window).</summary>
    private void DrawJobFaction(Game game, int f, bool compact)
    {
        if (f >= _jb.Length || f >= game.JobsView.Length)
            return;
        var b = _jb[f];
        var jf = game.JobsView[f];
        ImGuiSafe.TextColored(Orange, b.Head);
        var ranked = compact && game.FactionTable.PlayerStory[game.LocalId]; // (the Faction window has its rank bars above: UiRanks.cs)
        if (b.Next.Length > 0 && !ranked)
        {
            ImGui.ProgressBar(b.Bar, new float2(compact ? 220 * _scale : -1, 8 * _scale), ""u8);
            ImGuiSafe.TextColored(Dim, b.Next);
        }
        if (b.Teaches.Length > 0 && !ranked)
            ImGuiSafe.TextColored(Dim, b.Teaches);
        for (var j = 0; j < jf.Jobs.Length && j < b.Title.Length; j++)
        {
            var row = jf.Jobs[j];
            ImGui.Spacing();
            ImGuiSafe.TextColored(row.Held ? JobHeld : row.Open ? JobOpen : Dim, b.Title[j]);
            ImGui.Indent();
            if (b.About[j].Length > 0)
                ImGuiSafe.TextWrapped(b.About[j]);
            ImGuiSafe.TextWrapped(b.Asks[j]);
            ImGuiSafe.TextWrapped(b.Pays[j]);
            if (b.Note[j].Length > 0)
                ImGuiSafe.TextColored(row.Held ? Orange : Dim, b.Note[j]);
            if (row.Held)
            {
                if (ImGui.Button(b.Drop[j]))
                    game.JobAsk(f, -1);
            }
            else if (row.Open && ImGui.Button(b.Take[j]))
                game.JobAsk(f, j);
            ImGui.Unindent();
        }
    }
}
