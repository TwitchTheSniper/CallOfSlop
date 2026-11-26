using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The inventory's Designs tab (creative worlds; DesignParts.cs, GameSandboxDesigns.cs): the shipped ship designs (Constructs/Designs), the
/// player's own (their ShipDesigns folder, kept from the Ship Designer) and the one open in the designer. A click makes it ready on the host
/// and its ghost comes out to be placed by aiming, as a blueprint's does. The list is read again each time the tab opens or Refresh is pressed.
/// </summary>
public sealed partial class Ui
{
    private List<ShipDesignStore.Listed>? _sbDesigns;
    private string[] _sbDesignLabels = [];
    private bool _sbDesignsShown;

    private void SbDrawDesigns(Game game, Player me)
    {
        if (!game.SandboxInfiniteParts)
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "Ship designs spawn only in a creative world."u8);
            return;
        }
        if (_sbDesigns == null || !_sbDesignsShown)
        {
            _sbDesigns = ShipDesignStore.List();
            _sbDesignLabels = _sbDesigns.Select((d, i) => $"{d.Name}  ({(d.Shipped ? "shipped" : d.Current ? "open in the Ship Designer" : "yours")})##sbd{i}").ToArray();
        }
        _sbDesignsShown = true;
        ImGui.Spacing();
        ImGui.TextColored(Dim, "Click: it is made ready, then place it by aiming (as a blueprint). Keep your own from the Ship Designer with \"Keep in my designs\"."u8);
        if (ImGui.Button("Refresh##sbdrefresh"u8))
            _sbDesigns = null;
        SbDrawSpawnNote(game);
        ImGui.Separator();
        if (ImGui.BeginChild("##sbdesigns"u8, new float2(0, 0), ImGuiChildFlags.None) && _sbDesigns != null)
        {
            if (_sbDesigns.Count == 0)
                ImGui.TextColored(Dim, "No designs: make one in the Ship Designer (main menu) and keep it."u8);
            for (var i = 0; i < _sbDesigns.Count; i++)
                if (ImGui.Button(_sbDesignLabels[i]))
                {
                    game.SandboxSay("");
                    game.SandboxPrepareDesign(_sbDesigns[i].Json, _sbDesigns[i].Name);
                    CloseSandboxParts();
                    break;
                }
        }
        ImGui.EndChild();
    }
}
