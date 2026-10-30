using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// How many of each part a player has, for the inventory's count badges. The Sandbox salvage work adds the real counts
/// (Game.SandboxInventory, through Game.InventoryCounts, set when the window opens in a match); with no source every badge shows "inf".
/// </summary>
public interface ISandboxPartCounts
{
    /// <summary>How many of part <paramref name="partId"/> player <paramref name="playerId"/> holds, or a negative number for no limit.</summary>
    int PartCount(Game game, byte playerId, byte partId);
}

/// <summary>
/// Sandbox: the inventory (the inventory key, I by default; docs/sandbox/CONSTRUCTS.md, "The inventory"). Tabs for
/// parts, blueprints and tools; on the left a search box and a category tree (SandboxCatalogue.cs: groups by part role,
/// made from the data, so new roles land in a sensible group by themselves); on the right a scrolling grid of square 3D
/// tiles (SandboxTiles.cs: each thing drawn from its boxes into a texture atlas, once) under big section headers, a tile
/// size slider, a count badge per part ("inf" while parts are infinite), and a tooltip with the name, tool, mass, health,
/// armour and description. Clicking a part (or Enter on the highlighted one; the arrow keys move it) asks the server for it
/// by id and takes out the tool it needs, exactly as with a part from the rack; a blueprint is built in front of you; a
/// tool comes out. A recent row keeps the last few parts. Only in Sandbox, and only on maps whose sandbox section doesn't
/// turn it off (<see cref="SandboxMap.PartsMenu"/>). Drawn every frame while open without allocating: every label is made
/// once, and the atlas is made over a few frames by <see cref="SandboxTiles.Pump"/>.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The Sandbox inventory is open over the match (the mouse is free for it).</summary>
    public bool SandboxPartsOpen { get; private set; }

    /// <summary>Keep the window open after picking a part, blueprint or tool (off: it closes, back to building).</summary>
    public bool SandboxPartsKeepOpen;

    /// <summary>The tiles' size (pixels at scale 1; the window's slider).</summary>
    public float SandboxTileSize = 112;

    public const float SandboxTileMin = 56, SandboxTileMax = 160;

    /// <summary>Where the count badges come from (null: every badge shows "inf"). The merger wires Game.SandboxInventory in here.</summary>
    public ISandboxPartCounts? SandboxCounts;

    /// <summary>The inventory's tabs.</summary>
    public enum SbTab
    {
        Parts,
        Blueprints,
        Tools,
    }

    /// <summary>What a tile shows: a part, a blueprint or a tool, its texts made once.</summary>
    private sealed class SbItem
    {
        public int Slot, Index;
        public string Id = "", Name = "", Line1 = "", Line2 = "", Description = "", Search = "";
        public ConstructPartDef? Part;
        public ConstructTool Tool;
        public bool Usable = true;
        public ImColor8 Color, Color2, ToolColor;
    }

    private SandboxCatalogue? _sbCat;
    private SbItem[][] _sbItems = [[], [], []]; // per tab, by part id / blueprint index / tool index
    private SbTab _sbTab;
    private int _sbTabRequest = -1;
    private int _sbGroupSel = -1, _sbSectionSel = -1; // the tree's selection (-1: all)
    private int[] _sbVisItem = [], _sbVisSection = [];
    private int _sbVisibleCount, _sbSel = -1, _sbFilterVersion, _sbFilterBuilt = -1;
    private ImInputString? _sbSearchInput;
    private string _sbSearch = "";
    private readonly byte[] _sbRecent = [ConstructParts.None, ConstructParts.None, ConstructParts.None, ConstructParts.None, ConstructParts.None, ConstructParts.None, ConstructParts.None, ConstructParts.None];
    private bool _sbFocusSearch, _sbScrollToSel;
    private int _sbColumns = 1;

    /// <summary>The tiles the current tab lists now (self-test).</summary>
    public int SandboxPartsListed => _sbVisibleCount;

    /// <summary>The highlighted part on the Parts tab (self-test), or null.</summary>
    public ConstructPartDef? SandboxPartsHighlighted => _sbTab == SbTab.Parts && _sbSel >= 0 && _sbSel < _sbItems[0].Length ? _sbItems[0][_sbSel].Part : null;

    /// <summary>The highlighted item's index in the current tab (a part id, a blueprint or tool index), or -1.</summary>
    public int SandboxInventoryHighlight => _sbSel;

    /// <summary>The recent row, newest first (self-test).</summary>
    public ReadOnlySpan<byte> SandboxPartsRecent => _sbRecent;

    /// <summary>The categories the window shows (self-test).</summary>
    public SandboxCatalogue? SandboxInventoryCatalogue => _sbCat;

    public SbTab SandboxInventoryTab => _sbTab;

    /// <summary>Tiles drawn last frame with their picture, and as a plain swatch (the picture not made yet).</summary>
    public int SandboxTilesWithPicture { get; private set; }

    public int SandboxTilesPlain { get; private set; }

    public void OpenSandboxParts(bool focusSearch = false) // (not by default: while the box has the keyboard, the game's keys, I included, are off)
    {
        SandboxPartsOpen = true;
        _sbFocusSearch = focusSearch;
        SbBuildRows();
    }

    public void CloseSandboxParts() => SandboxPartsOpen = false;

    /// <summary>The search text (the self-test types it in; the window's box does too).</summary>
    public void SetSandboxPartsSearch(string text)
    {
        _sbSearchInput ??= new ImInputString(33);
        ImGuiSafe.SetInput(_sbSearchInput, text);
        _sbSearch = text.Trim();
        _sbFilterVersion++;
    }

    /// <summary>Show a tab (the self-test and the smoke pictures; a click on the tab does it too).</summary>
    public void SetSandboxInventoryTab(SbTab tab)
    {
        _sbTabRequest = (int)tab;
        SbSwitchTab(tab);
    }

    /// <summary>Select a tree node: a group (by index; -1 all) and one of its sections (by index in the tab's tree; -1 the whole group).</summary>
    public void SetSandboxInventoryNode(int group, int section)
    {
        _sbGroupSel = group;
        _sbSectionSel = section;
        _sbFilterVersion++;
    }

    private void SbSwitchTab(SbTab tab)
    {
        if (tab == _sbTab)
            return;
        _sbTab = tab;
        _sbGroupSel = _sbSectionSel = -1;
        _sbSel = -1;
        _sbFilterVersion++;
    }

    /// <summary>Every tile's text, made once (again only when the loaded parts, blueprints or tools change).</summary>
    private void SbBuildRows()
    {
        if (_sbCat == null || _sbCat.Key != SandboxTiles.Key())
            SbMakeRows();
    }

    private void SbMakeRows()
    {
        var cat = SandboxCatalogue.Make();
        var parts = ConstructParts.All;
        var partItems = new SbItem[parts.Count];
        foreach (var d in parts)
        {
            var c = d.Boxes.Length > 0 ? d.Boxes[0].Color : new System.Numerics.Vector3(0.6f);
            var c2 = d.Boxes.Length > 1 ? d.Boxes[^1].Color : c;
            partItems[d.Id] = new SbItem
            {
                Slot = SandboxTiles.SlotOf(SandboxTiles.Kind.Part, d.Id),
                Index = d.Id,
                Id = $"##sbtile_p{d.Id}",
                Name = d.Name,
                Part = d,
                Tool = d.Tool,
                Line1 = d.Tool switch
                {
                    ConstructTool.Welder => "Fitted with the welder (4)",
                    ConstructTool.Wrench => "Fitted with the wrench (3)",
                    ConstructTool.Linker => "Set up with the linking tool",
                    _ => "No tool needed",
                },
                Line2 = $"{d.Mass:0.#} kg   {d.Health:0} hp   armour {d.Armour:0.#}   fit {d.AttachSeconds:0.#} s",
                Description = $"{cat.PartGroup[d.Id]} > {cat.PartSection[d.Id]}. {d.Description}",
                Search = $"{d.Name} {cat.PartGroup[d.Id]} {cat.PartSection[d.Id]} {d.Role} {d.Description}",
                Color = Col8(c),
                Color2 = Col8(c2),
                ToolColor = ToolCol(d.Tool),
            };
        }
        var bps = ConstructBuild.Blueprints;
        var bpItems = new SbItem[bps.Count];
        for (var i = 0; i < bps.Count; i++)
        {
            var bp = bps[i];
            bpItems[i] = new SbItem
            {
                Slot = SandboxTiles.SlotOf(SandboxTiles.Kind.Blueprint, i),
                Index = i,
                Id = $"##sbtile_b{i}",
                Name = bp.Name,
                Line1 = $"Blueprint: {bp.Steps.Count} parts, {bp.Wires.Count} wires",
                Line2 = "Built whole, standing in front of you",
                Description = bp.Description.Length > 0 ? bp.Description : "A ready-made construct.",
                Search = $"{bp.Name} {cat.BlueprintSection[i]} {bp.Description}",
                Color = new ImColor8(120, 126, 136, 255),
                Color2 = new ImColor8(80, 86, 96, 255),
                ToolColor = new ImColor8(0, 0, 0, 0),
            };
        }
        var tools = ConstructTools.All;
        var toolItems = new SbItem[tools.Count];
        for (var i = 0; i < tools.Count; i++)
        {
            var m = tools[i];
            var c = m.Boxes.Length > 0 ? m.Boxes[0].Color : new System.Numerics.Vector3(0.5f);
            toolItems[i] = new SbItem
            {
                Slot = SandboxTiles.SlotOf(SandboxTiles.Kind.Tool, i),
                Index = i,
                Id = $"##sbtile_t{i}",
                Name = m.Name,
                Tool = m.Tool ?? ConstructTool.None,
                Usable = m.Tool != null,
                Line1 = m.Tool switch
                {
                    ConstructTool.Wrench => "Slot 3: bolted parts",
                    ConstructTool.Welder => "Slot 4: welded parts",
                    ConstructTool.Linker => "Wires and logic settings",
                    null => "Still to come",
                    _ => "",
                },
                Line2 = "",
                Description = m.Description,
                Search = $"{m.Name} {m.Key} {m.Description}",
                Color = Col8(c),
                Color2 = Col8(c),
                ToolColor = ToolCol(m.Tool ?? ConstructTool.None),
            };
        }
        _sbItems = [partItems, bpItems, toolItems];
        var most = Math.Max(partItems.Length, Math.Max(bpItems.Length, toolItems.Length));
        _sbVisItem = new int[most];
        _sbVisSection = new int[most];
        _sbCat = cat;
        _sbAllParts = $"All parts ({cat.Parts.Items})##sball0";
        _sbAllBlueprints = $"All blueprints ({cat.Blueprints.Items})##sball1";
        _sbAllTools = $"All tools ({cat.Tools.Items})##sball2";
        _sbGroupSel = _sbSectionSel = -1;
        _sbFilterVersion++;
        return;

        static ImColor8 Col8(System.Numerics.Vector3 v) =>
            new((byte)Math.Clamp(v.X * 255, 0, 255), (byte)Math.Clamp(v.Y * 255, 0, 255), (byte)Math.Clamp(v.Z * 255, 0, 255), 255);

        static ImColor8 ToolCol(ConstructTool t) => t switch
        {
            ConstructTool.Welder => new ImColor8(240, 150, 40, 255),
            ConstructTool.Wrench => new ImColor8(110, 200, 255, 255),
            ConstructTool.Linker => new ImColor8(120, 230, 140, 255),
            _ => new ImColor8(0, 0, 0, 0),
        };
    }

    private SandboxCatalogue.Tree SbTree => _sbTab switch
    {
        SbTab.Parts => _sbCat!.Parts,
        SbTab.Blueprints => _sbCat!.Blueprints,
        _ => _sbCat!.Tools,
    };

    /// <summary>The visible tiles (only when the search, tab or tree node changed): the highlight stays on its item, or moves to the first match.</summary>
    private void SbFilter()
    {
        if (_sbFilterBuilt == _sbFilterVersion)
            return;
        _sbFilterBuilt = _sbFilterVersion;
        _sbVisibleCount = 0;
        var tree = SbTree;
        var items = _sbItems[(int)_sbTab];
        var q = _sbSearch.AsSpan();
        var keep = false;
        for (var s = 0; s < tree.Sections.Length; s++)
        {
            var sec = tree.Sections[s];
            if (_sbSectionSel >= 0 ? s != _sbSectionSel : _sbGroupSel >= 0 && sec.Group != _sbGroupSel)
                continue;
            foreach (var i in sec.Items)
            {
                if (q.Length > 0 && !items[i].Search.AsSpan().Contains(q, StringComparison.OrdinalIgnoreCase))
                    continue;
                _sbVisItem[_sbVisibleCount] = i;
                _sbVisSection[_sbVisibleCount++] = s;
                keep |= i == _sbSel;
            }
        }
        if (!keep)
            _sbSel = _sbVisibleCount > 0 ? _sbVisItem[0] : -1;
    }

    /// <summary>
    /// A part picked (a click, Enter, the recent row, or the self-test): the server is asked for it by id, the tool it
    /// needs comes out, it goes to the front of the recent row, and the window closes unless it is kept open.
    /// </summary>
    public void SandboxPartsPick(Game game, ConstructPartDef def)
    {
        if (game.Local is { } me && def.Tool != ConstructTool.None && game.SandboxHands.Hand(me.Id).Tool != def.Tool)
            game.SandboxSend(CActKind.Tool, part: (byte)def.Tool);
        game.SandboxTakePart(def.Id);
        var at = Array.IndexOf(_sbRecent, def.Id);
        if (at < 0)
            at = _sbRecent.Length - 1;
        for (var i = at; i > 0; i--)
            _sbRecent[i] = _sbRecent[i - 1];
        _sbRecent[0] = def.Id;
        if (!SandboxPartsKeepOpen)
            CloseSandboxParts();
    }

    /// <summary>A blueprint tile (or the self-test): the server builds it in front of you.</summary>
    public void SandboxPartsSpawn(Game game, int blueprint)
    {
        game.SandboxSend(CActKind.Blueprint, part: (byte)blueprint);
        if (!SandboxPartsKeepOpen)
            CloseSandboxParts();
    }

    /// <summary>A tool tile: that tool comes out (again: put away).</summary>
    public void SandboxPartsTool(Game game, ConstructTool tool)
    {
        if (game.Local is { } me)
            game.SandboxSend(CActKind.Tool, part: (byte)(game.SandboxHands.Hand(me.Id).Tool == tool ? ConstructTool.None : tool));
        if (!SandboxPartsKeepOpen)
            CloseSandboxParts();
    }

    /// <summary>Takes the highlighted tile of the current tab, as Enter does (self-test). False when nothing is highlighted.</summary>
    public bool SandboxInventoryTake(Game game)
    {
        SbFilter();
        var items = _sbItems[(int)_sbTab];
        if (_sbSel < 0 || _sbSel >= items.Length)
            return false;
        SbActivate(game, items[_sbSel]);
        return true;
    }

    private void SbActivate(Game game, SbItem it)
    {
        switch (_sbTab)
        {
            case SbTab.Parts when it.Part != null:
                SandboxPartsPick(game, it.Part);
                break;
            case SbTab.Blueprints:
                SandboxPartsSpawn(game, it.Index);
                break;
            case SbTab.Tools when it.Usable:
                SandboxPartsTool(game, it.Tool);
                break;
        }
    }

    private static readonly ImColor8 SbCard = new(26, 29, 35, 240), SbCardHot = new(40, 46, 57, 245), SbCardSel = new(30, 43, 60, 245),
        SbOutline = new(110, 200, 255, 255), SbHotOutline = new(240, 150, 40, 255), SbText = new(222, 224, 230, 255), SbDim = new(150, 154, 164, 255),
        SbBadgeBg = new(14, 16, 20, 220);

    /// <summary>The window (every frame while open). It closes itself outside Sandbox or where the map turns it off.</summary>
    public void DrawSandboxParts(Game game, Settings settings)
    {
        if (!game.SandboxPartsMenu || game.Local is not { } me)
        {
            CloseSandboxParts();
            return;
        }
        SbBuildRows();
        SandboxTiles.Want(); // (the atlas is made over the next frames if it isn't yet)
        _sbSearchInput ??= new ImInputString(33);
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(1500 * _scale, vp.WorkSize.X - 48 * _scale);
        var h = MathF.Min(vp.WorkSize.Y * 0.86f, vp.WorkSize.Y - 32 * _scale);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X * 0.5f, vp.WorkPos.Y + vp.WorkSize.Y * 0.5f), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);
        var open = true;
        SbItem? picked = null;
        if (ImGui.Begin("Inventory###sbparts"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            // The tabs.
            if (ImGui.BeginTabBar("##sbtabs"u8))
            {
                SbTabItem("Parts"u8, SbTab.Parts);
                SbTabItem("Blueprints"u8, SbTab.Blueprints);
                SbTabItem("Tools"u8, SbTab.Tools);
                ImGui.EndTabBar();
            }
            _sbTabRequest = -1;
            SbFilter();
            var paneW = 240 * _scale;
            // Left: the search box, the tree, the options.
            if (ImGui.BeginChild("##sbleft"u8, new float2(paneW, 0), ImGuiChildFlags.Borders))
            {
                if (_sbFocusSearch)
                {
                    ImGui.SetKeyboardFocusHere();
                    _sbFocusSearch = false;
                }
                ImGui.SetNextItemWidth(-1);
                try
                {
                    if (ImGui.InputTextWithHint("##sbsearch"u8, "Search..."u8, _sbSearchInput))
                    {
                        _sbSearch = ImGuiSafe.Text(_sbSearchInput).Trim();
                        _sbFilterVersion++;
                    }
                }
                catch (Exception ex)
                {
                    ImGuiSafe.FieldFailed("sandbox inventory search", ex);
                }
                ImGui.Spacing();
                var treeH = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 5.2f;
                if (ImGui.BeginChild("##sbtree"u8, new float2(0, MathF.Max(60 * _scale, treeH)), ImGuiChildFlags.None))
                    SbDrawTree();
                ImGui.EndChild();
                ImGui.Separator();
                ImGui.Checkbox("Keep open after picking"u8, ref SandboxPartsKeepOpen);
                // Free placement (a part set down on a surface as a new construct): the world grid, and creative's anchor.
                ImGui.Checkbox("Snap new constructs to the grid"u8, ref game.SandboxPlaceGrid);
                if (game.SandboxInfiniteParts)
                    ImGui.Checkbox("Anchor new constructs to the ground"u8, ref game.SandboxPlaceAnchor);
                else
                    ImGui.TextColored(Dim, "New constructs: free (a foundation anchors)"u8);
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderFloat("##sbtilesize"u8, ref SandboxTileSize, SandboxTileMin, SandboxTileMax, "Tile size %.0f"u8);
                ImGuiSafe.TextColored(Dim, $"[{Keybinds.Name(settings.Bind(GameAction.SandboxInventory))}] or Esc closes");
            }
            ImGui.EndChild();
            ImGui.SameLine();
            // Right: what's in your hands and the recent row, then the grid.
            ImGui.BeginGroup();
            ref var hand = ref game.SandboxHands.Hand(me.Id);
            var held = ConstructParts.Get(hand.Held);
            var items = _sbItems[(int)_sbTab];
            ImGui.AlignTextToFramePadding();
            ImGuiSafe.TextColored(Dim, $"In your hands: {(held != null ? held.Name : "nothing")}   |   {_sbVisibleCount} of {items.Length} shown   |   Recent:");
            var mini = ImGui.GetFrameHeight() * 1.25f;
            var any = false;
            for (var i = 0; i < _sbRecent.Length; i++)
                if (ConstructParts.Get(_sbRecent[i]) is { } rd && rd.Id < _sbItems[0].Length)
                {
                    ImGui.SameLine();
                    var it = _sbItems[0][rd.Id];
                    var at = ImGui.GetCursorScreenPos();
                    if (ImGui.InvisibleButton(SbRecentIds[i], new float2(mini, mini)))
                        picked = it;
                    var hot = ImGui.IsItemHovered();
                    SbDrawTile(ImGui.GetWindowDrawList(), it, at, mini, false, hot, false);
                    if (hot)
                        SbTooltip(game, me.Id, it);
                    any = true;
                }
            if (!any)
            {
                ImGui.SameLine();
                ImGui.TextColored(Dim, "(nothing yet)"u8);
            }
            if (picked != null && _sbTab != SbTab.Parts) // (the recent row holds parts whatever the tab)
            {
                if (picked.Part != null)
                    SandboxPartsPick(game, picked.Part);
                picked = null;
            }
            var gridPicked = SbDrawGrid(game, me.Id);
            picked ??= gridPicked;
            ImGui.EndGroup();
            if (picked != null)
                SbActivate(game, picked);
        }
        ImGui.End();
        if (!open)
            CloseSandboxParts();
    }

    private static readonly string[] SbRecentIds = ["##sbrecent0", "##sbrecent1", "##sbrecent2", "##sbrecent3", "##sbrecent4", "##sbrecent5", "##sbrecent6", "##sbrecent7"];

    private void SbTabItem(ImString label, SbTab tab)
    {
        var flags = _sbTabRequest == (int)tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (!ImGui.BeginTabItem(label, flags))
            return;
        if (_sbTabRequest < 0)
            SbSwitchTab(tab);
        ImGui.EndTabItem();
    }

    /// <summary>The category tree: "All", then each group with its sections (a group of one section is a single row).</summary>
    private void SbDrawTree()
    {
        var tree = SbTree;
        const ImGuiTreeNodeFlags leaf = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
        ImGui.TreeNodeEx(SbAllLabel(tree), leaf | (_sbGroupSel < 0 && _sbSectionSel < 0 ? ImGuiTreeNodeFlags.Selected : 0));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            SetSandboxInventoryNode(-1, -1);
        for (var g = 0; g < tree.Groups.Length; g++)
        {
            var grp = tree.Groups[g];
            var single = grp.Sections.Length == 1;
            var sel = _sbGroupSel == g && _sbSectionSel < 0;
            if (single)
            {
                ImGui.TreeNodeEx(grp.Label, leaf | (sel ? ImGuiTreeNodeFlags.Selected : 0));
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    SetSandboxInventoryNode(g, -1);
                continue;
            }
            var open = ImGui.TreeNodeEx(grp.Label, ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth
                                                  | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.DrawLinesToNodes | (sel ? ImGuiTreeNodeFlags.Selected : 0));
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
                SetSandboxInventoryNode(g, -1);
            if (!open)
                continue;
            foreach (var s in grp.Sections)
            {
                ImGui.TreeNodeEx(tree.Sections[s].Label, leaf | (_sbSectionSel == s ? ImGuiTreeNodeFlags.Selected : 0));
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    SetSandboxInventoryNode(g, s);
            }
            ImGui.TreePop();
        }
    }

    private string _sbAllParts = "", _sbAllBlueprints = "", _sbAllTools = "";

    private string SbAllLabel(SandboxCatalogue.Tree tree) => tree == _sbCat!.Parts ? _sbAllParts : tree == _sbCat.Blueprints ? _sbAllBlueprints : _sbAllTools;

    /// <summary>The grid: big group headers, section headers, rows of tiles; the arrow keys move the highlight, Enter takes it. Returns a clicked tile.</summary>
    private SbItem? SbDrawGrid(Game game, byte me)
    {
        SbItem? picked = null;
        var items = _sbItems[(int)_sbTab];
        // The keys: Left/Right one tile, Up/Down a row, Enter takes it.
        if (_sbVisibleCount > 0)
        {
            var pos = 0;
            for (var i = 0; i < _sbVisibleCount; i++)
                if (_sbVisItem[i] == _sbSel)
                    pos = i;
            var to = pos;
            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow, true))
                to = pos + 1;
            if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, true))
                to = pos - 1;
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true))
                to = pos + _sbColumns;
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true))
                to = pos - _sbColumns;
            if (to != pos)
            {
                _sbSel = _sbVisItem[Math.Clamp(to, 0, _sbVisibleCount - 1)];
                _sbScrollToSel = true;
            }
            if ((ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)) && _sbSel >= 0)
                picked = items[_sbSel];
        }
        var withPic = 0;
        var plain = 0;
        if (ImGui.BeginChild("##sbgrid"u8, new float2(0, 0), ImGuiChildFlags.Borders))
        {
            var dl = ImGui.GetWindowDrawList();
            var tile = SandboxTileSize * _scale;
            var gap = 8 * _scale;
            var availW = ImGui.GetContentRegionAvail().X;
            var cols = Math.Max(1, (int)((availW + gap) / (tile + gap)));
            _sbColumns = cols;
            var nameH = ImGui.GetFontSize() * 0.85f + 6 * _scale;
            var rowH = tile + nameH + gap;
            var clipTop = ImGui.GetWindowPos().Y;
            var clipBottom = clipTop + ImGui.GetWindowSize().Y;
            var tree = SbTree;
            var v = 0;
            var byGroup = _sbSectionSel < 0; // all or a group: a block per group, its tiles flowing; a section: its own block
            while (v < _sbVisibleCount)
            {
                var s = _sbVisSection[v];
                var sec = tree.Sections[s];
                var grp = tree.Groups[sec.Group];
                // The block's header: big, with the group's name small above a section's.
                if (v > 0)
                    ImGui.Dummy(new float2(1, 10 * _scale));
                var sectionBlock = !byGroup && grp.Sections.Length > 1; // (a group of one section is shown by the group's name)
                if (sectionBlock)
                {
                    ImGui.PushFont(default, 15);
                    ImGuiSafe.TextColored(new float4(0.95f, 0.6f, 0.2f, 1f), grp.Name);
                    ImGui.PopFont();
                }
                ImGui.PushFont(default, 34);
                ImGuiSafe.TextColored(new float4(0.93f, 0.94f, 0.96f, 1f), sectionBlock ? sec.Name : grp.Name);
                ImGui.PopFont();
                // The block's tiles, row by row (rows out of view: only their space).
                var end = v;
                while (end < _sbVisibleCount && (byGroup ? tree.Sections[_sbVisSection[end]].Group == sec.Group : _sbVisSection[end] == s))
                    end++;
                for (var r0 = v; r0 < end; r0 += cols)
                {
                    var rowAt = ImGui.GetCursorScreenPos();
                    var r1 = Math.Min(end, r0 + cols);
                    var selInRow = false;
                    for (var k = r0; k < r1; k++)
                        selInRow |= _sbVisItem[k] == _sbSel;
                    if (rowAt.Y + rowH < clipTop || rowAt.Y > clipBottom)
                    {
                        ImGui.Dummy(new float2(availW, rowH - ImGui.GetStyle().ItemSpacing.Y));
                        if (selInRow && _sbScrollToSel)
                        {
                            ImGui.SetScrollHereY(0.5f);
                            _sbScrollToSel = false;
                        }
                        continue;
                    }
                    for (var k = r0; k < r1; k++)
                    {
                        var it = items[_sbVisItem[k]];
                        var at = new float2(rowAt.X + (k - r0) * (tile + gap), rowAt.Y);
                        ImGui.SetCursorScreenPos(at);
                        if (ImGui.InvisibleButton(it.Id, new float2(tile, tile + nameH)))
                        {
                            _sbSel = it.Index;
                            picked = it;
                        }
                        var hot = ImGui.IsItemHovered();
                        if (SbDrawTile(dl, it, at, tile, it.Index == _sbSel, hot, true, nameH))
                            withPic++;
                        else
                            plain++;
                        if (_sbTab == SbTab.Parts && it.Part != null)
                            SbBadge(dl, game, me, it, at, tile);
                        if (hot)
                            SbTooltip(game, me, it);
                    }
                    ImGui.SetCursorScreenPos(rowAt);
                    ImGui.Dummy(new float2(availW, rowH - ImGui.GetStyle().ItemSpacing.Y));
                    if (selInRow && _sbScrollToSel)
                    {
                        ImGui.SetScrollHereY(0.5f);
                        _sbScrollToSel = false;
                    }
                }
                v = end;
            }
            if (_sbVisibleCount == 0)
                ImGui.TextColored(Dim, "Nothing matches the search."u8);
        }
        ImGui.EndChild();
        SandboxTilesWithPicture = withPic;
        SandboxTilesPlain = plain;
        return picked;
    }

    /// <summary>One tile: its card, its picture (or a plain swatch until the picture is made), and its name under it. True when the picture was drawn.</summary>
    private bool SbDrawTile(ImDrawListPtr dl, SbItem it, float2 at, float size, bool selected, bool hot, bool named, float nameH = 0)
    {
        var p1 = new float2(at.X + size, at.Y + size + nameH);
        var r = 5 * _scale;
        dl.AddRectFilled(at, p1, selected ? SbCardSel : hot ? SbCardHot : SbCard, r);
        var pad = size * 0.06f;
        var i0 = new float2(at.X + pad, at.Y + pad);
        var i1 = new float2(at.X + size - pad, at.Y + size - pad);
        var pic = SandboxTiles.TryGet(it.Slot, out var tex, out var uv0, out var uv1);
        var tint = it.Usable ? new ImColor8(255, 255, 255, 255) : new ImColor8(140, 140, 150, 150);
        if (pic)
            dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, i0, i1, new float2(uv0.X, uv0.Y), new float2(uv1.X, uv1.Y), tint);
        else
        {
            // The plain swatch: the main colour, the last box's colour as a corner.
            var c0 = new float2(at.X + size * 0.3f, at.Y + size * 0.3f);
            var c1 = new float2(at.X + size * 0.7f, at.Y + size * 0.7f);
            dl.AddRectFilled(c0, c1, it.Color, 3 * _scale);
            dl.AddRectFilled(new float2(c0.X + (c1.X - c0.X) * 0.55f, c0.Y + (c1.Y - c0.Y) * 0.55f), c1, it.Color2, 3 * _scale);
        }
        if (it.Tool != ConstructTool.None)
            dl.AddCircleFilled(new float2(at.X + 9 * _scale, at.Y + 9 * _scale), 3.5f * _scale, it.ToolColor);
        if (named)
        {
            var clip = new float4(at.X + 4 * _scale, at.Y, p1.X - 4 * _scale, p1.Y);
            ImGui.PushFont(default, 15);
            DlText(dl, new float2(at.X + 6 * _scale, at.Y + size - 2 * _scale), it.Usable ? SbText : SbDim, it.Name, clip);
            ImGui.PopFont();
        }
        if (selected)
            dl.AddRect(at, p1, SbOutline, r, ImDrawFlags.None, 2f * _scale);
        else if (hot)
            dl.AddRect(at, p1, SbHotOutline, r, ImDrawFlags.None, 1.5f * _scale);
        return pic;
    }

    /// <summary>The count badge (top right): how many the player has, or "inf" while parts are infinite (no count source).</summary>
    private void SbBadge(ImDrawListPtr dl, Game game, byte me, SbItem it, float2 at, float size)
    {
        var n = SandboxCounts?.PartCount(game, me, it.Part!.Id) ?? -1;
        ImGui.PushFont(default, 14);
        var fs = ImGui.GetFontSize();
        var w = (n < 0 ? 3 : n >= 100 ? 3 : n >= 10 ? 2 : 1) * fs * 0.6f + 8 * _scale;
        var b1 = new float2(at.X + size - 4 * _scale, at.Y + 4 * _scale + fs + 2 * _scale);
        var b0 = new float2(b1.X - w, at.Y + 4 * _scale);
        dl.AddRectFilled(b0, b1, n == 0 ? new ImColor8(90, 30, 30, 230) : SbBadgeBg, 4 * _scale);
        var tp = new float2(b0.X + 4 * _scale, b0.Y + 1 * _scale);
        if (n < 0)
            DlText(dl, tp, SbDim, "inf"u8);
        else if (n > 999)
            DlText(dl, tp, SbText, "999"u8);
        else
            DlText(dl, tp, n == 0 ? new ImColor8(255, 140, 140, 255) : SbText, $"{n}");
        ImGui.PopFont();
    }

    /// <summary>The tooltip: name, tool, numbers, description (and the count on the Parts tab).</summary>
    private void SbTooltip(Game game, byte me, SbItem it)
    {
        if (!ImGui.BeginTooltip())
            return;
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24);
        ImGui.PushFont(default, 20);
        ImGui.Text(it.Name);
        ImGui.PopFont();
        if (it.Line1.Length > 0)
            ImGuiSafe.TextColored(it.Tool == ConstructTool.Welder ? Orange : Dim, it.Line1);
        if (it.Line2.Length > 0)
            ImGui.Text(it.Line2);
        if (it.Part != null)
        {
            var n = SandboxCounts?.PartCount(game, me, it.Part.Id) ?? -1;
            if (n < 0)
                ImGui.TextColored(Dim, "You have: unlimited (infinite parts)"u8);
            else
                ImGuiSafe.TextColored(Dim, $"You have: {n}");
        }
        ImGuiSafe.TextDisabled(it.Description);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }
}
