using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Tab inventory's "Made parts" category (docs/sandbox/design/inventory-run-time-parts.md phase 2): the made parts the local
/// player's counted inventory holds (extrusions, pressed parts, formed and cut panels...: up to <see cref="SandboxInventory.MaxMadeKinds"/>
/// kinds), sorted by kind then size, each by its real name and count. A click sets one down in front of the player as a body (made parts
/// are never hand items: the user's answer 1), asked for by its key. The rows are made again only when the inventory changes, so an
/// open window allocates nothing frame to frame. Plain rows for now: their pictures are phase 4.
/// </summary>
public sealed partial class Ui
{
    private bool _sbMadeSel; // the "Made parts" node is selected (the Parts tab)
    private SandboxInventory? _sbMadeInv;
    private int _sbMadeVersion = -1, _sbMadeN;
    private readonly ushort[] _sbMadeIds = new ushort[SandboxInventory.MaxMadeKinds];
    private readonly string[] _sbMadeRow = new string[SandboxInventory.MaxMadeKinds];
    private readonly string?[] _sbMadeHead = new string?[SandboxInventory.MaxMadeKinds]; // a kind's heading before its first row, else null
    private string _sbMadeLabel = "Made parts (0)##sbmade";
    private readonly int[] _sbMadeOrder = new int[SandboxInventory.MaxMadeKinds];

    /// <summary>The "Made parts" category is the one shown (self-test, tools).</summary>
    public bool SandboxInventoryMadeSelected => _sbMadeSel;

    /// <summary>Selects the "Made parts" category (on), or goes back to all parts (off).</summary>
    public void SetSandboxInventoryMade(bool on)
    {
        _sbMadeSel = on;
        _sbFilterVersion++;
    }

    /// <summary>The rows as shown, in order: (part id, text). For tools and checks; allocates.</summary>
    public List<(ushort Id, string Text)> SandboxInventoryMadeRows(Game game)
    {
        var list = new List<(ushort, string)>();
        if (game.Local is { } me)
        {
            SbMadeRows(game.SandboxInventory(me.Id));
            for (var k = 0; k < _sbMadeN; k++)
                list.Add((_sbMadeIds[k], _sbMadeRow[k]));
        }
        return list;
    }

    /// <summary>The rows again when the inventory changed: sorted by kind, then size, then name.</summary>
    private void SbMadeRows(SandboxInventory inv)
    {
        if (ReferenceEquals(inv, _sbMadeInv) && inv.Version == _sbMadeVersion)
            return;
        _sbMadeInv = inv;
        _sbMadeVersion = inv.Version;
        var n = 0;
        for (var k = 0; k < inv.MadeN; k++)
            if (ConstructParts.Get(inv.MadeDef[k]) != null)
                _sbMadeOrder[n++] = k;
        Array.Sort(_sbMadeOrder, 0, n, Comparer<int>.Create((a, b) =>
        {
            var da = ConstructParts.Def(inv.MadeDef[a]);
            var db = ConstructParts.Def(inv.MadeDef[b]);
            var c = PartRef.KindOrder(da).CompareTo(PartRef.KindOrder(db));
            if (c == 0)
                c = PartRef.SizeOf(da).CompareTo(PartRef.SizeOf(db));
            return c != 0 ? c : string.CompareOrdinal(PartRef.Listed(da), PartRef.Listed(db));
        }));
        var total = 0;
        string? lastKind = null;
        for (var i = 0; i < n; i++)
        {
            var k = _sbMadeOrder[i];
            var d = ConstructParts.Def(inv.MadeDef[k]);
            _sbMadeIds[i] = d.Id;
            _sbMadeRow[i] = $"{PartRef.Listed(d)}   x{inv.MadeCount[k]}##sbmade{i}";
            var kind = PartRef.KindName(d);
            _sbMadeHead[i] = kind == lastKind ? null : kind;
            lastKind = kind;
            total += inv.MadeCount[k];
        }
        _sbMadeN = n;
        _sbMadeLabel = $"Made parts ({total})##sbmade";
    }

    /// <summary>The tree's "Made parts" node, under the categories (the Parts tab).</summary>
    private void SbDrawMadeNode(Game game, bool searching)
    {
        if (game.Local is not { } me)
            return;
        SbMadeRows(game.SandboxInventory(me.Id));
        const ImGuiTreeNodeFlags leaf = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
        ImGui.Separator();
        var empty = _sbMadeN == 0;
        if (empty)
            ImGui.BeginDisabled();
        ImGui.TreeNodeEx(_sbMadeLabel, leaf | (_sbMadeSel && !searching ? ImGuiTreeNodeFlags.Selected : 0));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            SetSandboxInventoryMade(true);
        if (empty)
            ImGui.EndDisabled();
    }

    /// <summary>The right side while "Made parts" is selected: a heading per kind, a row per made part; a click sets one down in front of you.</summary>
    private void SbDrawMade(Game game, byte me)
    {
        SbMadeRows(game.SandboxInventory(me));
        if (!ImGui.BeginChild("##sbmadelist"u8, new float2(0, 0), ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();
            return;
        }
        if (_sbMadeN == 0)
            ImGui.TextColored(Dim, "No made parts. Take a piece an Extruder, Press or former made apart with the remove tool to count it here."u8);
        for (var i = 0; i < _sbMadeN; i++)
        {
            if (_sbMadeHead[i] is { } head)
            {
                if (i > 0)
                    ImGui.Spacing();
                ImGuiSafe.TextColored(Dim, head);
            }
            if (ImGui.Selectable(_sbMadeRow[i]) && ConstructParts.Get(_sbMadeIds[i]) is { } d)
            {
                SbSpawned(game, PartRef.Listed(d)); // (a body in front of you: the window stays open)
                game.SandboxTakePart(d.Id);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Set it down in front of you (a made part is moved with the physics gun)"u8);
        }
        ImGui.EndChild();
    }
}
