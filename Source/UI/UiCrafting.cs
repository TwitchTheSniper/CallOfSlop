using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Crafting Terminal's panel (docs/sandbox/constructs/production.md "Crafting terminal"): what the terminal's network can make
/// or holds, as tiles like the recipe picker's (its tiles, pictures and resource icons are reused), under three tabs: Make, In stock
/// and Orders. A click selects a tile; the bar under the grid sets a count and orders it (<see cref="CActKind.CraftOrder"/>: the
/// server decides and says why not). A dim tile is one a machine exists for but lacks inputs: its reason is on the tile and on hover.
/// The list is worked out from what this peer already hears (stores, links, queues, claims), at most twice a second while open and
/// only when something changed; a frame of it allocates nothing of its own.
/// </summary>
public sealed partial class Ui
{
    public bool CraftingOpen { get; private set; }

    private ushort _crBody;
    private int _crPart = -1, _crTab, _crSel = -1, _crCount = 1, _crStamp, _crOrdersKey;
    private double _crBuiltAt;
    private bool _crBuilt;
    private readonly List<CraftRow> _crRows = new(128);
    private RpTile[] _crTiles = [];
    private int[] _crVisible = [];
    private int _crVisibleCount, _crMake, _crStock;
    private ImInputString? _crSearchInput;
    private string _crSearch = "", _crSelText = "";
    private readonly string[] _crOrderText = new string[ConstructCrafting.MaxOrders];

    /// <summary>The tiles drawn last frame (smoke log).</summary>
    public int CraftingTiles { get; private set; }

    /// <summary>The terminal the panel is open on.</summary>
    public (ushort Body, int Part) CraftingTarget => (_crBody, _crPart);

    public void OpenCrafting(ushort body, int part)
    {
        if (_crBody != body || _crPart != part)
        {
            _crSel = -1;
            _crTab = 0;
            _crSearch = "";
            if (_crSearchInput != null)
                ImGuiSafe.SetInput(_crSearchInput, "");
        }
        _crBody = body;
        _crPart = part;
        _crBuilt = false;
        _crOrdersKey = 0;
        CraftingOpen = true;
    }

    public void CloseCrafting() => CraftingOpen = false;

    /// <summary>Smoke and probes: which tab shows (0 Make, 1 In stock, 2 Orders).</summary>
    public void CraftingTab(int tab) => (_crTab, _crBuilt) = (Math.Clamp(tab, 0, 2), false);

    private void CraftingBuild(ConstructWorld world, Construct c, int part, double now)
    {
        var stamp = ConstructCrafting.Stamp(world, c, part) * 31 + SandboxTiles.Key();
        if (_crBuilt && (stamp == _crStamp || now - _crBuiltAt < 0.5))
            return;
        var selected = _crSel >= 0 && _crSel < _crRows.Count ? _crRows[_crSel].What : default;
        var had = _crSel >= 0 && _crSel < _crRows.Count;
        _crBuilt = true;
        _crStamp = stamp;
        _crBuiltAt = now;
        ConstructCrafting.Catalogue(world, c, part, _crRows);
        _crRows.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        var names = RecyclingTable.Current;
        _crTiles = new RpTile[_crRows.Count];
        _crMake = _crStock = 0;
        _crSel = -1;
        for (var i = 0; i < _crRows.Count; i++)
        {
            var row = _crRows[i];
            if (had && row.What.Part == selected.Part && (!row.What.IsPiece || row.What.Key == selected.Key))
                _crSel = i;
            _crMake += row.Make > 0 ? 1 : 0;
            _crStock += row.Stock > 0 ? 1 : 0;
            var usable = row.Stock > 0 || row.Make is 1 or 2;
            var mat = row.What.IsPiece ? row.What.Key.Material : (byte)0;
            var t = new RpTile
            {
                Value = usable ? i : -1,
                Name = row.Name,
                Out = row.Stock > 0 ? $"x{row.Stock}" : "",
                Slot = row.What.IsPiece ? SandboxTiles.ResourceSlot(mat) : SandboxTiles.SlotOf(SandboxTiles.Kind.Part, row.What.Part),
                Swatch = row.What.IsPiece ? Col8(names.ColourOf(mat)) : Col8(row.Def is { Boxes.Length: > 0 } d ? d.Boxes[0].Color : new System.Numerics.Vector3(0.5f)),
                Search = row.Name.ToLowerInvariant(),
            };
            t.Time = row.Stock > 0 ? (row.Make is 1 or 2 ? $"in stock {row.Stock}, can make" : $"in stock {row.Stock}")
                : row.Make == 1 ? "can make" : row.Make == 2 ? "can make: chain" : Short(row.Missing, 24);
            var maker = row.Machine >= 0 && row.Machine < c.PartCount ? c.DefOf(row.Machine).Name : "";
            t.Tip = row.Make switch
            {
                1 => $"{row.Name}: the {maker} makes it now{(row.Recipe != null ? $" ({row.Recipe.Batch})" : "")}.{(row.Stock > 0 ? $" {row.Stock} in stock come first." : "")}",
                2 => $"{row.Name}: the {maker} can make it once it is fed: {row.Missing}.",
                3 => $"{row.Name}: the {maker} could make it, but {row.Missing}.{(row.Stock > 0 ? $" {row.Stock} in stock can be fetched." : "")}",
                _ => $"{row.Name}: {row.Stock} in linked stores, not reserved. No linked machine makes it.",
            };
            if (row.Recipe is { } r)
            {
                var n = r.Materials.Length + r.Parts.Length;
                t.InSlot = new int[n];
                t.InText = new string[n];
                t.InSwatch = new ImColor8[n];
                var k = 0;
                foreach (var (m, units) in r.Materials)
                    (t.InSlot[k], t.InText[k], t.InSwatch[k++]) = (SandboxTiles.ResourceSlot(m), $"{units}", Col8(names.ColourOf(m)));
                foreach (var (p, count) in r.Parts)
                    (t.InSlot[k], t.InText[k], t.InSwatch[k++]) = (SandboxTiles.SlotOf(SandboxTiles.Kind.Part, p.Id), $"{count}", Col8(p.Boxes.Length > 0 ? p.Boxes[0].Color : new System.Numerics.Vector3(0.5f)));
            }
            _crTiles[i] = t;
        }
        if (_crVisible.Length < _crTiles.Length)
            _crVisible = new int[Math.Max(64, _crTiles.Length)];
        _crSelText = _crSel >= 0 ? _crRows[_crSel].Name : "";
        CraftingFilter();
    }

    private static string Short(string s, int n) => s.Length <= n ? s : s[..(n - 2)] + "..";

    private void CraftingFilter()
    {
        _crVisibleCount = 0;
        var words = _crSearch.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < _crTiles.Length && i < _crRows.Count; i++)
        {
            if (_crTab == 0 ? _crRows[i].Make == 0 : _crRows[i].Stock == 0)
                continue;
            var match = true;
            foreach (var w in words)
                match &= _crTiles[i].Search.Contains(w, StringComparison.Ordinal);
            if (match)
                _crVisible[_crVisibleCount++] = i;
        }
    }

    /// <summary>The order lines' texts, made again only when the orders changed.</summary>
    private void CraftingOrders(Construct c, CraftList? q)
    {
        var key = 17;
        if (q != null)
            for (var k = 0; k < q.Count; k++)
                key = key * 31 + q.Items[k].Id * 7919 + q.Items[k].Got * 131 + (int)q.Items[k].State * 17 + q.Items[k].Where + (q.Items[k].Note?.Length ?? 0);
        if (key == _crOrdersKey)
            return;
        _crOrdersKey = key;
        for (var k = 0; q != null && k < q.Count; k++)
        {
            ref readonly var o = ref q.Items[k];
            _crOrderText[k] = $"{o.Got}/{o.What.Count}  {ConstructCrafting.NameOf(o.What)}  -  {ConstructCrafting.StateText(c, o)}";
        }
    }

    private static readonly float4 CrTabOn = new(0.18f, 0.32f, 0.5f, 1f), CrGood = new(0.45f, 0.86f, 0.55f, 1f), CrWarn = new(1f, 0.72f, 0.3f, 1f);

    /// <summary>The panel (between NewFrame and Render). True while it stays open. An order or a cancel goes to the server after the drawing.</summary>
    public bool DrawCrafting(Game game, Construct c, int part, double now)
    {
        if (!CraftingOpen)
            return false;
        SandboxTiles.WantFew();
        var world = game.Constructs;
        CraftingBuild(world, c, part, now);
        var q = c.Parts[part].Craft;
        CraftingOrders(c, q);
        var open = true;
        var order = false;
        var cancel = 0;
        var tab = _crTab;
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(940 * _scale, vp.WorkSize.X * 0.9f);
        var h = MathF.Min(640 * _scale, vp.WorkSize.Y * 0.88f);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + (vp.WorkSize.X - w) * 0.5f, vp.WorkPos.Y + (vp.WorkSize.Y - h) * 0.5f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.95f);
        if (ImGui.Begin("Crafting Terminal###craft"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            for (var t = 0; t < 3; t++)
            {
                if (t > 0)
                    ImGui.SameLine();
                var sel = t == _crTab;
                if (sel)
                    ImGui.PushStyleColor(ImGuiCol.Button, CrTabOn);
                if (t == 0 ? ImGui.Button("Make"u8, new float2(110 * _scale, 0)) : t == 1 ? ImGui.Button("In stock"u8, new float2(110 * _scale, 0)) : ImGui.Button("Orders"u8, new float2(110 * _scale, 0)))
                    tab = t;
                if (sel)
                    ImGui.PopStyleColor();
            }
            ImGui.SameLine();
            if (_crTab < 2)
            {
                _crSearchInput ??= new ImInputString(33);
                ImGui.SetNextItemWidth(220 * _scale);
                if (ImGui.InputTextWithHint("##crsearch"u8, "Search..."u8, _crSearchInput))
                {
                    _crSearch = ImGuiSafe.Text(_crSearchInput);
                    CraftingFilter();
                }
                ImGui.SameLine();
            }
            ImGui.TextColored(Dim, _crTab == 0 ? "What the linked machines make. Dim: inputs missing."u8 : _crTab == 1 ? "On linked shelves and racks, not reserved."u8 : "This terminal's requests."u8);
            ImGui.Separator();
            var foot = ImGui.GetFrameHeightWithSpacing() * (_crTab < 2 ? 2 : 1) + 4 * _scale;
            if (ImGui.BeginChild("##crgrid"u8, new float2(0, -foot), ImGuiChildFlags.None))
            {
                if (_crTab < 2)
                    CraftingGrid();
                else
                    cancel = CraftingOrderList(q);
            }
            ImGui.EndChild();
            if (_crTab < 2)
            {
                if (_crSel >= 0 && _crSel < _crRows.Count)
                {
                    var row = _crRows[_crSel];
                    ImGuiSafe.TextColored(row.Stock > 0 || row.Make == 1 ? CrGood : CrWarn, _crSelText);
                    ImGui.SameLine();
                    if (ImGui.Button("1"u8, new float2(34 * _scale, 0)))
                        _crCount = 1;
                    ImGui.SameLine();
                    if (ImGui.Button("5"u8, new float2(34 * _scale, 0)))
                        _crCount = 5;
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(110 * _scale);
                    ImGui.InputInt("##crcount"u8, ref _crCount, 1, 10);
                    _crCount = Math.Clamp(_crCount, 1, WorkOrders.MaxCount);
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Button, CrTabOn);
                    order = ImGui.Button("Order"u8, new float2(120 * _scale, 0));
                    ImGui.PopStyleColor();
                }
                else if (_crVisibleCount == 0)
                    ImGui.TextColored(Dim, _crTab == 0 ? "Nothing to make: link machines to the terminal (conveyors, chutes, or a Line Console's wire into Orders)."u8 : "Nothing in linked stores."u8);
                else
                    ImGui.TextColored(Dim, "Click a part, set how many, then Order. It arrives in the tray."u8);
            }
            if (ImGui.Button("Close"u8))
                open = false;
        }
        ImGui.End();
        // After the drawing: the tab, the order, the cancel.
        if (tab != _crTab)
        {
            _crTab = tab;
            CraftingFilter();
        }
        if (order && _crSel >= 0 && _crSel < _crRows.Count)
        {
            game.SandboxCraftOrder(_crBody, _crPart, _crRows[_crSel].What with { Count = (ushort)_crCount });
            _crTab = 2;
        }
        if (cancel != 0)
            game.SandboxCraftCancel(_crBody, _crPart, (ushort)cancel);
        if (!open)
            CloseCrafting();
        return open;
    }

    /// <summary>The Orders tab: a line an order with its state and a Cancel button. The id of the one cancelled this frame, else 0.</summary>
    private int CraftingOrderList(CraftList? q)
    {
        if (q is not { Count: > 0 })
        {
            ImGui.TextColored(Dim, "No open orders. Order a part on the Make or In stock tab."u8);
            return 0;
        }
        var cancel = 0;
        for (var k = 0; k < q.Count; k++)
        {
            ref readonly var o = ref q.Items[k];
            ImGui.PushID(o.Id);
            if (ImGui.Button("Cancel"u8, new float2(80 * _scale, 0)))
                cancel = o.Id;
            ImGui.PopID();
            ImGui.SameLine();
            var col = o.State switch { CraftState.InTray => CrGood, CraftState.Held or CraftState.Collect => CrWarn, _ => new float4(0.9f, 0.9f, 0.93f, 1f) };
            ImGuiSafe.TextColored(col, _crOrderText[k] ?? "");
        }
        return cancel;
    }

    /// <summary>The tiles in rows (the recipe picker's tile); a click on a usable one selects it.</summary>
    private void CraftingGrid()
    {
        var dl = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 8 * _scale;
        var tw = 150 * _scale;
        var cols = Math.Max(1, (int)((avail + gap) / (tw + gap)));
        tw = (avail - gap * (cols - 1)) / cols;
        var icon = MathF.Min(tw * 0.62f, 96 * _scale);
        var th = icon + 74 * _scale;
        var rows = (_crVisibleCount + cols - 1) / cols;
        var rowH = th + gap;
        var top = ImGui.GetCursorPosY();
        var first = Math.Clamp((int)((ImGui.GetScrollY() - top) / rowH), 0, rows);
        var last = Math.Clamp((int)((ImGui.GetScrollY() + ImGui.GetWindowHeight() - top) / rowH) + 1, first, rows);
        ImGui.SetCursorPosY(top + first * rowH);
        var tiles = 0;
        var pick = -1;
        for (var row = first; row < last; row++)
        {
            var rowAt = ImGui.GetCursorScreenPos();
            for (var col = 0; col < cols; col++)
            {
                var v = row * cols + col;
                if (v >= _crVisibleCount)
                    break;
                var i = _crVisible[v];
                var t = _crTiles[i];
                var at = new float2(rowAt.X + col * (tw + gap), rowAt.Y);
                ImGui.SetCursorScreenPos(at);
                ImGui.PushID(i);
                var clicked = ImGui.InvisibleButton("##ct"u8, new float2(tw, th));
                ImGui.PopID();
                var hot = ImGui.IsItemHovered();
                if (clicked && t.Value >= 0)
                    pick = i;
                if (hot && t.Tip.Length > 0)
                    ImGuiSafe.WrappedTooltip(t.Tip, 420 * _scale);
                RecipeTile(dl, t, at, tw, th, icon, i == _crSel, hot);
                tiles++;
            }
            ImGui.SetCursorScreenPos(rowAt);
            ImGui.Dummy(new float2(avail, rowH - ImGui.GetStyle().ItemSpacing.Y));
        }
        ImGui.SetCursorPosY(top + rows * rowH);
        ImGui.Dummy(new float2(1, 1));
        CraftingTiles = tiles;
        if (pick >= 0)
        {
            _crSel = pick; // (a selection only: nothing in the lists changes)
            _crSelText = _crRows[pick].Name;
        }
    }
}
