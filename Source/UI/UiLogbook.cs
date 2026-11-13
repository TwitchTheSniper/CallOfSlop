using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Logbook (F1 in Sandbox by default; docs/story/FRAGMENTS.md): the old records' fragments this player holds, each with where it
/// came from, a "?" for each one still out there, and the leads they have heard of, a "?" until what the lead points to is found.
/// Its second page, Paths, is UiLogbookPaths.cs; its third, Technologies, UiTechnologies.cs.
/// It reads what the host sent (Game.FragmentsHeld, Game.FragmentLeads) and the texts every peer has (Story/Fragments). Closed, it
/// costs one bool check.
/// </summary>
public sealed partial class Ui
{
    public bool LogbookOpen;

    public void DrawLogbook(Game? game, int bind)
    {
        if (!LogbookOpen)
            return;
        ImGui.SetNextWindowSize(new float2(760 * _scale, 640 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.96f);
        if (ImGui.Begin("Logbook###logbook"u8, ref LogbookOpen))
        {
            if (game is not { SandboxOn: true })
                ImGuiSafe.TextWrapped("The Logbook is a Sandbox thing: start or join a Sandbox game.");
            else if (ImGui.BeginTabBar("##logbooktabs"u8))
            {
                if (ImGui.BeginTabItem("Records"u8))
                {
                    DrawLogbookBody(game);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Paths"u8))
                {
                    DrawLogbookPaths(game); // (what they did and chose, and where they stand: UiLogbookPaths.cs)
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Technologies"u8))
                {
                    DrawLogbookTechnologies(game); // (what the factions taught them, what is still locked, their tool upgrades: UiTechnologies.cs)
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            ImGui.Separator();
            ImGuiSafe.TextColored(Dim, $"{Keybinds.Name(bind)} or Esc closes this window.");
        }
        ImGui.End();
    }

    private static void DrawLogbookBody(Game game)
    {
        var script = FragmentScript.Current;
        if (script.Problem.Length > 0)
        {
            ImGuiSafe.TextColored(Red, $"No records: {script.Problem}");
            return;
        }
        var total = script.Fragments.Length;
        ImGuiSafe.TextWrapped($"Old records: {game.FragmentsHeld.Count} of {total} found.");
        ImGui.Spacing();
        foreach (var (f, source) in game.FragmentsHeld)
        {
            var frag = script.Fragments[f];
            if (!ImGui.CollapsingHeader($"{frag.Title}##logfrag{f}", ImGuiTreeNodeFlags.DefaultOpen))
                continue;
            if (frag.Kind.Length > 0)
                ImGuiSafe.TextColored(Dim, frag.Kind);
            foreach (var para in frag.Text)
            {
                ImGuiSafe.TextWrapped(para);
                ImGui.Spacing();
            }
            if (source.Length > 0)
                ImGuiSafe.TextColored(Dim, $"Found: {source}");
            ImGui.Spacing();
        }
        for (var f = 0; f < total; f++)
        {
            var have = false;
            foreach (var (h, _) in game.FragmentsHeld)
                have |= h == f;
            if (!have)
                ImGuiSafe.TextColored(Dim, script.Fragments[f].Hint.Length > 0 ? $"?  {script.Fragments[f].Hint}" : "?  A record not yet found.");
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGuiSafe.TextWrapped("Leads");
        if (game.FragmentLeads.Count == 0)
            ImGuiSafe.TextColored(Dim, "Nothing yet. Listen to people, and look around.");
        foreach (var (name, found) in game.FragmentLeads)
        {
            if (found)
                ImGuiSafe.TextColored(Green, $"{name}: found");
            else
                ImGuiSafe.TextColored(Orange, $"{name}  ?");
        }
    }
}
