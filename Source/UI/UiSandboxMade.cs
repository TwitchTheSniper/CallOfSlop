using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Tab inventory's "Made parts" category (docs/sandbox/design/inventory-run-time-parts.md phase 2): the made parts the local
/// player's counted inventory holds (extrusions, pressed parts, formed and cut panels...: up to <see cref="SandboxInventory.MaxMadeKinds"/>
/// kinds), and (inventories phase 4) those on the racks of the beacon network they stand in, sorted by kind then size, each by its real
/// name and counts. A click sets one down in front of the player as a body (made parts are never hand items: the user's answer 1), asked
/// for by its key: out of the count first, else off a rack on the network. With the Line Layer or the Replacer on, a click picks it for the
/// aid instead. A search lists the made parts whose name or key has every word. The rows are made again only when the inventory or the
/// network's list changes, so an open window allocates nothing frame to frame. Each row has its picture, drawn on demand (SandboxTiles.Made.cs).
/// </summary>
public sealed partial class Ui
{
    private const int SbMadeMax = SandboxInventory.MaxMadeKinds + BeaconView.MaxMade; // (the count's kinds, and the network's not in it)

    private bool _sbMadeSel; // the "Made parts" node is selected (the Parts tab)
    private SandboxInventory? _sbMadeInv;
    private BeaconView? _sbMadeNet;
    private int _sbMadeVersion = -1, _sbMadeNetVersion = -1, _sbMadeN;
    private bool _sbMadeNetOn;
    private readonly ushort[] _sbMadeIds = new ushort[SbMadeMax];
    private readonly string[] _sbMadeRow = new string[SbMadeMax];
    private readonly string[] _sbMadeTip = new string[SbMadeMax]; // the tooltip: what a click does, and the part's key
    private readonly string[] _sbMadeFind = new string[SbMadeMax]; // what a search reads: the shown name and the key
    private readonly string?[] _sbMadeHead = new string?[SbMadeMax]; // a kind's heading before its first row, else null
    private string _sbMadeLabel = "Made parts (0)##sbmade";
    private readonly int[] _sbMadeOrder = new int[SbMadeMax];
    private readonly ushort[] _sbMadeSrc = new ushort[SbMadeMax]; // (the rebuild's scratch: each kind once)
    private readonly int[] _sbMadeOwn = new int[SbMadeMax], _sbMadeOnNet = new int[SbMadeMax];

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
            SbMadeRows(game, me.Id);
            for (var k = 0; k < _sbMadeN; k++)
                list.Add((_sbMadeIds[k], _sbMadeRow[k]));
        }
        return list;
    }

    /// <summary>The rows' tooltips as shown, in order (tools and checks).</summary>
    public string SandboxInventoryMadeTip(int row) => row >= 0 && row < _sbMadeN ? _sbMadeTip[row] : "";

    /// <summary>The rows again when the inventory or the network's made list changed: sorted by kind, then size, then name.</summary>
    private void SbMadeRows(Game game, byte me)
    {
        var inv = game.SandboxInv.Of(me);
        var on = game.SandboxBeaconOn;
        var net = game.SandboxBeacon(me);
        if (ReferenceEquals(inv, _sbMadeInv) && inv.Version == _sbMadeVersion && on == _sbMadeNetOn && (!on || ReferenceEquals(net, _sbMadeNet) && net.Version == _sbMadeNetVersion))
            return;
        SbMadeRebuild(inv, on ? net : null); // (its own method: the sort's closure would otherwise be made every frame, before this return)
        _sbMadeNetOn = on;
        _sbMadeNet = net;
        _sbMadeNetVersion = net.Version;
    }

    private void SbMadeRebuild(SandboxInventory inv, BeaconView? net)
    {
        _sbMadeInv = inv;
        _sbMadeVersion = inv.Version;
        var n = 0;
        for (var k = 0; k < inv.MadeN && n < SbMadeMax; k++)
            if (ConstructParts.Get(inv.MadeDef[k]) != null)
            {
                _sbMadeSrc[n] = inv.MadeDef[k];
                _sbMadeOwn[n] = inv.MadeCount[k];
                _sbMadeOnNet[n] = net?.CountOf(inv.MadeDef[k]) ?? 0;
                _sbMadeOrder[n] = n;
                n++;
            }
        if (net != null)
            for (var k = 0; k < net.MadeN && n < SbMadeMax; k++)
                if (net.MadeCount[k] > 0 && inv.Count(net.MadeDef[k]) == 0 && ConstructParts.Get(net.MadeDef[k]) != null)
                {
                    _sbMadeSrc[n] = net.MadeDef[k];
                    _sbMadeOwn[n] = 0;
                    _sbMadeOnNet[n] = net.MadeCount[k];
                    _sbMadeOrder[n] = n;
                    n++;
                }
        var src = _sbMadeSrc;
        Array.Sort(_sbMadeOrder, 0, n, Comparer<int>.Create((a, b) =>
        {
            var da = ConstructParts.Def(src[a]);
            var db = ConstructParts.Def(src[b]);
            var c = PartRef.KindOrder(da).CompareTo(PartRef.KindOrder(db));
            if (c == 0)
                c = PartRef.SizeOf(da).CompareTo(PartRef.SizeOf(db));
            if (c == 0)
                c = string.CompareOrdinal(PartRef.Listed(da), PartRef.Listed(db));
            return c != 0 ? c : string.CompareOrdinal(da.Name, db.Name); // (two of one label: by key, so the order holds)
        }));
        var total = 0;
        string? lastKind = null;
        for (var i = 0; i < n; i++)
        {
            var k = _sbMadeOrder[i];
            var d = ConstructParts.Def(_sbMadeSrc[k]);
            int own = _sbMadeOwn[k], onNet = _sbMadeOnNet[k];
            _sbMadeIds[i] = d.Id;
            var counts = own > 0 && onNet > 0 ? $"x{own}, {onNet} on the network" : own > 0 ? $"x{own}" : $"{onNet} on the network";
            var listed = PartRef.Listed(d);
            _sbMadeRow[i] = $"{listed}   {counts}##sbmade{i}";
            _sbMadeTip[i] = $"{(own > 0 ? "Set it down in front of you" : "Take one off a rack on the network: it is set down in front of you")} (a made part is moved with the physics gun).\nWith the Line Layer or the Replacer on: lay or fit this one.\nKey: {d.Name}";
            _sbMadeFind[i] = listed + " " + d.Name;
            var kind = PartRef.KindName(d);
            _sbMadeHead[i] = kind == lastKind ? null : kind;
            lastKind = kind;
            total += own + onNet;
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
        SbMadeRows(game, me.Id);
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

    /// <summary>Does made row <paramref name="i"/> have every word of the search (its shown name or its key)? Allocates nothing.</summary>
    private bool SbMadeMatches(int i)
    {
        foreach (var w in _sbWords)
            if (_sbMadeFind[i].IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        return true;
    }

    /// <summary>Made parts that match the search, at the end of the grid (the Parts tab): how many rows were drawn.</summary>
    private int SbDrawMadeHits(Game game, byte me)
    {
        SbMadeRows(game, me);
        var shown = 0;
        for (var i = 0; i < _sbMadeN; i++)
        {
            if (!SbMadeMatches(i))
                continue;
            if (shown++ == 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(Dim, "Made parts"u8);
            }
            SbDrawMadeRow(game, i);
        }
        SandboxMadeSearchHits = shown;
        SandboxMadeIcons = _sbMadeIcons;
        SandboxMadeSwatches = _sbMadeSwatches;
        _sbMadeIcons = _sbMadeSwatches = 0;
        return shown;
    }

    /// <summary>Made rows the search found last frame (self-test, tools).</summary>
    public int SandboxMadeSearchHits;

    /// <summary>One made row: its picture on the left (drawn on demand: SandboxTiles.Made.cs; a swatch until it is), the row beside it.</summary>
    private void SbDrawMadeRow(Game game, int i)
    {
        var at = ImGui.GetCursorScreenPos();
        var side = SbMadeIcon * _scale;
        ImGui.SetCursorScreenPos(new float2(at.X + side + 6 * _scale, at.Y));
        var clicked = ImGui.Selectable(_sbMadeRow[i], false, ImGuiSelectableFlags.None, new float2(0, side));
        var hovered = ImGui.IsItemHovered();
        SbDrawMadeIcon(ImGui.GetWindowDrawList(), _sbMadeIds[i], at, side);
        if (hovered)
            ImGuiSafe.SetTooltip(_sbMadeTip[i]);
        if (clicked && ConstructParts.Get(_sbMadeIds[i]) is { } d)
            SandboxMadePick(game, d);
    }

    /// <summary>A made row's click: picked for the Line Layer or the Replacer when one is on, else set down in front of the player.</summary>
    public void SandboxMadePick(Game game, ConstructPartDef d)
    {
        if (Program.AidPick(game, d))
        {
            CloseSandboxParts(); // (a Line Layer's or Replacer's part: picked, not set down; Program.SandboxAids.cs)
            return;
        }
        SbSpawned(game, PartRef.Listed(d)); // (a body in front of you: the window stays open)
        game.SandboxTakePart(d.Id);
    }

    /// <summary>The right side while "Made parts" is selected: a heading per kind, a row per made part.</summary>
    private void SbDrawMade(Game game, byte me)
    {
        SbMadeRows(game, me);
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
            SbDrawMadeRow(game, i);
        }
        ImGui.EndChild();
        SandboxMadeIcons = _sbMadeIcons;
        SandboxMadeSwatches = _sbMadeSwatches;
        _sbMadeIcons = _sbMadeSwatches = 0;
    }
}
