using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The inventory's Builds tab (board item dev-sandbox-weapon-parts-wp01, part 2; GameSandboxBench.cs): every standard
/// build (Parts/Builds: whole, legal guns, melee weapons and grenades of every tier) as a button; a click spawns it
/// complete into your hands, the way a build made at the Gun Workbench arrives. Creative worlds only (a survival world
/// builds from parts at a bench). The list is made once per set of loaded builds.
/// </summary>
public sealed partial class Ui
{
    private string[] _sbBuildLabels = [];
    private StandardBuild[] _sbBuildList = [];
    private string _sbBuildSearch = "";
    private string[] _sbBuildWords = [], _sbBuildKeys = [];
    private ImInputString? _sbBuildSearchInput;

    private void SbDrawBuilds(Game game, Player me)
    {
        if (!game.SandboxInfiniteParts)
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "No standard builds in a survival world: build one at a Gun Workbench from your parts."u8);
            return;
        }
        var all = StandardBuilds.All;
        if (!ReferenceEquals(_sbBuildList, all) || _sbBuildLabels.Length != all.Length)
        {
            _sbBuildList = all;
            _sbBuildLabels = all.Select(b => $"{b.Name}  ({SbBuildKind(b)}, {b.Tier})##sbb{b.Name}").ToArray();
            _sbBuildKeys = all.Select(b => $"{b.Name} {SbBuildKind(b)} {b.Tier}").ToArray();
        }
        ImGui.Spacing();
        ImGui.TextColored(Dim, "Click: into your hands. Endless ammo."u8);
        _sbBuildSearchInput ??= new ImInputString(33);
        SbFocusSearchBox();
        ImGui.SetNextItemWidth(260 * _scale);
        if (ImGui.InputTextWithHint(_sbBuildLabel, "Search builds..."u8, _sbBuildSearchInput))
            SbTyped(_sbBuildSearchInput);
        ImGui.SameLine();
        if (ImGui.Button("x##sbbclear"u8))
        {
            ClearSandboxPartsSearch();
            _sbFocusSearch = true;
        }
        ImGui.Separator();
        if (ImGui.BeginChild("##sbbuilds"u8, new float2(0, 0), ImGuiChildFlags.None))
        {
            // The search covers every build, best matches first: name starts with it, name has it, kind or tier has it.
            for (var rank = 0; rank <= (_sbBuildWords.Length > 0 ? 2 : 0); rank++)
                for (var i = 0; i < all.Length; i++)
                {
                    if (SbRank(all[i].Name, _sbBuildKeys[i], _sbBuildKeys[i], _sbBuildWords) != rank)
                        continue;
                    if (ImGui.Button(_sbBuildLabels[i]))
                        SbSpawnBuild(game, all[i]);
                    if (ImGui.IsItemHovered() && all[i].Flavor.Length > 0)
                        ImGuiSafe.SetTooltip(all[i].Flavor);
                }
        }
        ImGui.EndChild();
    }

    private static string SbBuildKind(StandardBuild b) => b.Build().Kind switch { BuildKind.Melee => "melee", BuildKind.Grenade => "grenade", _ => "gun" };

    /// <summary>The builds the Builds tab lists for the current search (self-test).</summary>
    public int SandboxBuildsListed()
    {
        var n = 0;
        for (var i = 0; i < _sbBuildKeys.Length; i++)
            if (SbRank(_sbBuildList[i].Name, _sbBuildKeys[i], _sbBuildKeys[i], _sbBuildWords) >= 0)
                n++;
        return n;
    }

    /// <summary>Asks for a standard build complete in the hands (the server checks creative and the build again).</summary>
    public void SbSpawnBuild(Game game, StandardBuild b)
    {
        game.SandboxSendBench(Game.BenchSpawn, 0, b.Build().ToText());
        if (!SandboxPartsKeepOpen)
            CloseSandboxParts();
    }
}
