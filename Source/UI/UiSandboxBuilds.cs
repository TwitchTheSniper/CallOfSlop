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
        }
        ImGui.Spacing();
        ImGui.TextColored(Dim, "A click puts the whole build in your hands (the gun in slot 1, a melee weapon or grenade in its own). Endless ammo."u8);
        _sbBuildSearchInput ??= new ImInputString(33);
        ImGui.SetNextItemWidth(260 * _scale);
        if (ImGui.InputTextWithHint("##sbbsearch"u8, "Search builds..."u8, _sbBuildSearchInput))
            _sbBuildSearch = ImGuiSafe.Text(_sbBuildSearchInput).Trim();
        ImGui.Separator();
        if (ImGui.BeginChild("##sbbuilds"u8, new float2(0, 0), ImGuiChildFlags.None))
        {
            for (var i = 0; i < all.Length; i++)
            {
                if (_sbBuildSearch.Length > 0 && all[i].Name.IndexOf(_sbBuildSearch, StringComparison.OrdinalIgnoreCase) < 0)
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

    /// <summary>Asks for a standard build complete in the hands (the server checks creative and the build again).</summary>
    public void SbSpawnBuild(Game game, StandardBuild b)
    {
        game.SandboxSendBench(Game.BenchSpawn, 0, b.Build().ToText());
        if (!SandboxPartsKeepOpen)
            CloseSandboxParts();
    }
}
