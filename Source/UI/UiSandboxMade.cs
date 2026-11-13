using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Tab inventory's "Made parts" category (docs/sandbox/design/inventory-run-time-parts.md phase 2): the made parts the local
/// player's counted inventory holds (extrusions, pressed parts, formed and cut panels...: up to <see cref="SandboxInventory.MaxMadeKinds"/>
/// kinds), sorted by kind then size, each by its real name and count. A click sets one down in front of the player as a body (made parts
/// are never hand items: the user's answer 1), asked for by its key. The rows are made again only when the inventory changes, so an
/// open window allocates nothing frame to frame. Each row has its picture, drawn on demand (SandboxTiles.Made.cs).
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
        SbMadeRebuild(inv); // (its own method: the sort's closure would otherwise be made every frame, before this return)
    }

    private void SbMadeRebuild(SandboxInventory inv)
    {
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

    /// <summary>A made part's row picture's side (px at scale 1).</summary>
    private const float SbMadeIcon = 40;

    /// <summary>Made-part pictures drawn last frame, and swatches in their place (self-test, tools).</summary>
    public int SandboxMadeIcons, SandboxMadeSwatches;

    private int _sbMadeIcons, _sbMadeSwatches;

    /// <summary>A part's picture in a square (a made part's is asked for and drawn over the next frames), or its colour as a swatch until then.</summary>
    private void SbDrawMadeIcon(ImDrawListPtr dl, ushort id, float2 at, float side)
    {
        var b = new float2(at.X + side, at.Y + side);
        dl.AddRectFilled(at, b, SbCard, 4 * _scale);
        var pad = side * 0.06f;
        if (SandboxTiles.TryGetPart(id, out var tex, out var uv0, out var uv1))
        {
            dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, new float2(at.X + pad, at.Y + pad), new float2(b.X - pad, b.Y - pad), new float2(uv0.X, uv0.Y), new float2(uv1.X, uv1.Y), new ImColor8(255, 255, 255, 255));
            _sbMadeIcons++;
            return;
        }
        if (ConstructParts.Get(id) is { Boxes.Length: > 0 } d)
        {
            var c = d.Boxes[0].Color;
            dl.AddRectFilled(new float2(at.X + side * 0.3f, at.Y + side * 0.3f), new float2(b.X - side * 0.3f, b.Y - side * 0.3f), new ImColor8((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), 255), 3 * _scale);
        }
        _sbMadeSwatches++;
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
            // Its picture on the left (drawn on demand: SandboxTiles.Made.cs; a swatch until it is), the row beside it.
            var at = ImGui.GetCursorScreenPos();
            var side = SbMadeIcon * _scale;
            ImGui.SetCursorScreenPos(new float2(at.X + side + 6 * _scale, at.Y));
            var clicked = ImGui.Selectable(_sbMadeRow[i], false, ImGuiSelectableFlags.None, new float2(0, side));
            var hovered = ImGui.IsItemHovered();
            SbDrawMadeIcon(ImGui.GetWindowDrawList(), _sbMadeIds[i], at, side);
            if (clicked && ConstructParts.Get(_sbMadeIds[i]) is { } d)
            {
                SbSpawned(game, PartRef.Listed(d)); // (a body in front of you: the window stays open)
                game.SandboxTakePart(d.Id);
            }
            if (hovered)
                ImGui.SetTooltip("Set it down in front of you (a made part is moved with the physics gun)"u8);
        }
        ImGui.EndChild();
        SandboxMadeIcons = _sbMadeIcons;
        SandboxMadeSwatches = _sbMadeSwatches;
        _sbMadeIcons = _sbMadeSwatches = 0;
    }
}
