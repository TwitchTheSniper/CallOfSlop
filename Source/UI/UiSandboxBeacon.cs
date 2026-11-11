using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Tab inventory on an Inventory Beacon's network (ConstructBeacons.cs; docs/sandbox/CONSTRUCTS.md "The Inventory Beacon"):
/// on a survival map, where the map's menu is off, the same window with real counts: the Parts tab groups what the network
/// holds as creative groups it (what it doesn't hold is hidden, or greyed with the box unticked), a click takes one as a
/// click does in creative, and the Materials tab takes loads from its bins. Nothing the window holds is trusted: the
/// server walks the real stores and says why when the last one has gone.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Hide what the network doesn't hold (off: grey). Per session.</summary>
    public bool SandboxNetHideMissing = true;

    private BeaconView? _sbNet; // the window is on this network (null: the map's own menu)
    private SandboxInventory? _sbNetInv;
    private int _sbNetVersion = -1;
    private readonly int[] _sbMatSeen = new int[SandboxInventory.MaxMaterials];
    private readonly string[] _sbMatLabel = new string[SandboxInventory.MaxMaterials];
    private readonly string[] _sbMatButton = MakeMatButtons();

    private static string[] MakeMatButtons()
    {
        var a = new string[SandboxInventory.MaxMaterials];
        for (var k = 0; k < a.Length; k++)
            a[k] = $"Take a load##sbmat{k}";
        return a;
    }

    /// <summary>Each frame the window is open: which network it shows; the list is filtered again when the counts change.</summary>
    private void SbNetFrame(Game game, byte me)
    {
        var net = game.SandboxPartsMenu ? null : game.SandboxBeacon(me);
        if (!ReferenceEquals(net, _sbNet))
        {
            _sbNet = net;
            _sbNetVersion = -1;
        }
        _sbNetInv = net != null ? game.SandboxInventory(me) : null;
        if (net != null && net.Version != _sbNetVersion)
        {
            _sbNetVersion = net.Version;
            _sbFilterVersion++;
        }
        if (net != null && _sbTab is SbTab.Blueprints or SbTab.Spawn or SbTab.Builds or SbTab.Engine)
            SetSandboxInventoryTab(SbTab.Parts);
        else if (net == null && _sbTab == SbTab.Materials)
            SetSandboxInventoryTab(SbTab.Parts);
    }

    /// <summary>The self-test: the window as it stands for this player (the frame's own step, without a frame).</summary>
    public void SandboxInventorySync(Game game) => SbNetFrame(game, game.LocalId);

    private int SbNetHas(int id) => _sbNet == null ? 0 : _sbNet.Counts[id] + (_sbNetInv?.Parts[id] ?? 0);

    /// <summary>Is this tile a part the network doesn't hold (grey)?</summary>
    private bool SbNetMissing(SbItem it) => _sbNet != null && _sbTab == SbTab.Parts && it.Part != null && SbNetHas(it.Index) == 0;

    /// <summary>Does the Parts tab leave this part out?</summary>
    private bool SbNetHides(int item) => _sbNet != null && SandboxNetHideMissing && _sbTab == SbTab.Parts && SbNetHas(item) == 0;

    /// <summary>The beacon's line and the hide box, under the window's own options.</summary>
    private void SbNetOptions()
    {
        if (ImGui.Checkbox("Hide what it doesn't hold"u8, ref SandboxNetHideMissing))
            _sbFilterVersion++;
        ImGuiSafe.TextColored(Dim, _sbNet!.Line);
    }

    /// <summary>The Materials tab: a row a material the network holds. A click fills what you carry up to a load.</summary>
    private void SbDrawMaterials(Game game)
    {
        if (_sbNet is not { } net)
            return;
        ImGuiSafe.TextColored(Dim, net.Line);
        ImGui.Separator();
        var names = RecyclingTable.Current;
        var any = false;
        for (var m = 1; m < net.Materials.Length && m <= names.Materials.Length; m++)
        {
            var amount = net.Materials[m];
            if (amount <= 0)
                continue;
            any = true;
            if (_sbMatLabel[m] == null || _sbMatSeen[m] != amount)
            {
                _sbMatSeen[m] = amount;
                _sbMatLabel[m] = $"{names.NameOf((byte)m)}: {amount}";
            }
            ImGui.AlignTextToFramePadding();
            ImGui.Text(_sbMatLabel[m]);
            ImGui.SameLine(280 * _scale);
            if (ImGui.Button(_sbMatButton[m]))
                game.SandboxTakeMaterial((byte)m);
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetTooltip($"Fills what you carry up to {ConstructStorage.TakeLoad}.");
        }
        if (!any)
            ImGui.TextColored(Dim, "No bulk materials on the network."u8);
    }
}
