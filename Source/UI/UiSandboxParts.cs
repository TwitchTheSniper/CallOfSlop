using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the debug parts menu, "Parts (debug)" (the inventory key, I by default; docs/sandbox/CONSTRUCTS.md). A
/// debugging aid for the test yard, not how the final game hands out parts: every loaded construct part, grouped by role,
/// with a search box, each with its colour, the tool it needs, its mass, health, armour and description. Clicking one
/// (or Enter on the highlighted one; Up and Down move it) asks the server for it by id (the rack's request with a part
/// id, GameSandbox.cs) and takes out the tool it needs, so the ghost sockets, fitting and dropping work exactly as with
/// a part from the rack. A recent row keeps the last few picks, and the blueprint buttons build a whole blueprint in front
/// of you. Only in Sandbox, and only on maps whose sandbox section doesn't turn it off (<see cref="SandboxMap.PartsMenu"/>).
/// Drawn every frame while open without allocating: every label is made once.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The Sandbox parts menu is open over the match (the mouse is free for it).</summary>
    public bool SandboxPartsOpen { get; private set; }

    /// <summary>Keep the window open after picking a part or a blueprint (off: it closes, back to building).</summary>
    public bool SandboxPartsKeepOpen;

    private sealed class SbPartRow
    {
        public ConstructPartDef Def = null!;
        public string Label = "", Stats = "", Tool = "";
        public ImColor8 Color, Color2;
    }

    private SbPartRow[] _sbRows = [];
    private string[] _sbGroupLabels = [];
    private int[] _sbVisible = [];
    private int _sbVisibleCount, _sbSel = -1, _sbFilterVersion = -1, _sbFilterBuilt = -2;
    private ImInputString? _sbSearchInput;
    private string _sbSearch = "";
    private readonly byte[] _sbRecent = [ConstructParts.None, ConstructParts.None, ConstructParts.None, ConstructParts.None, ConstructParts.None, ConstructParts.None];
    private string[] _sbBlueprintLabels = [];
    private bool _sbFocusSearch, _sbScrollToSel;

    /// <summary>The rows the last frame listed (self-test).</summary>
    public int SandboxPartsListed => _sbVisibleCount;

    /// <summary>The highlighted row's part (self-test), or null.</summary>
    public ConstructPartDef? SandboxPartsHighlighted => _sbSel >= 0 && _sbSel < _sbRows.Length ? _sbRows[_sbSel].Def : null;

    /// <summary>The recent row, newest first (self-test).</summary>
    public ReadOnlySpan<byte> SandboxPartsRecent => _sbRecent;

    private static readonly string[] SbRoleNames =
    [
        "Frames", "Panels", "Seats", "Wheels", "Suspension", "Engines", "Fuel tanks", "Fuel lines", "Hinges", "Doors", "Foundations",
        "Joints", "Transmissions", "Differentials", "Logic",
    ];

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

    /// <summary>Every row's text, made once (the parts are loaded at start-up and never change in a run).</summary>
    private void SbBuildRows()
    {
        if (_sbRows.Length != ConstructParts.All.Count || _sbBlueprintLabels.Length != ConstructBuild.Blueprints.Count)
            SbMakeRows(ConstructParts.All); // (its own method: its lambdas' closure would otherwise be made on every call)
    }

    private void SbMakeRows(IReadOnlyList<ConstructPartDef> all)
    {
        // Grouped by role (the enum's order), then by name.
        var rows = all.OrderBy(d => d.Role).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(d =>
        {
            var c = d.Boxes.Length > 0 ? d.Boxes[0].Color : new System.Numerics.Vector3(0.6f);
            var c2 = d.Boxes.Length > 1 ? d.Boxes[^1].Color : c;
            var tool = d.Tool switch { ConstructTool.Welder => "welder (4)", ConstructTool.Wrench => "wrench (3)", _ => "-" };
            return new SbPartRow
            {
                Def = d,
                Label = $"{d.Name}##sbpart{d.Id}",
                Tool = tool,
                Stats = $"{d.Mass:0.#} kg   {d.Health:0} hp   armour {d.Armour:0.#}   fit {d.AttachSeconds:0.#} s",
                Color = Col8(c),
                Color2 = Col8(c2),
            };
        }).ToArray();
        _sbRows = rows;
        _sbGroupLabels = SbRoleNames.Select((n, i) => $"{n} ({rows.Count(r => (int)r.Def.Role == i)})").ToArray();
        _sbVisible = new int[rows.Length];
        _sbBlueprintLabels = ConstructBuild.Blueprints.Select((b, i) => $"{b.Name}##sbbp{i}").ToArray();
        _sbFilterVersion++;
        return;

        static ImColor8 Col8(System.Numerics.Vector3 v) =>
            new((byte)Math.Clamp(v.X * 255, 0, 255), (byte)Math.Clamp(v.Y * 255, 0, 255), (byte)Math.Clamp(v.Z * 255, 0, 255), 255);
    }

    /// <summary>Does this row match the search (name, role or description; no allocation)?</summary>
    private bool SbMatches(SbPartRow r)
    {
        if (_sbSearch.Length == 0)
            return true;
        var q = _sbSearch.AsSpan();
        var role = (int)r.Def.Role < SbRoleNames.Length ? SbRoleNames[(int)r.Def.Role] : "";
        return r.Def.Name.AsSpan().Contains(q, StringComparison.OrdinalIgnoreCase) || role.AsSpan().Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.Def.Description.AsSpan().Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The visible rows (only when the search changed): the highlight stays on its part, or moves to the first match.</summary>
    private void SbFilter()
    {
        if (_sbFilterBuilt == _sbFilterVersion)
            return;
        _sbFilterBuilt = _sbFilterVersion;
        _sbVisibleCount = 0;
        var keep = false;
        for (var i = 0; i < _sbRows.Length; i++)
            if (SbMatches(_sbRows[i]))
            {
                _sbVisible[_sbVisibleCount++] = i;
                keep |= i == _sbSel;
            }
        if (!keep)
            _sbSel = _sbVisibleCount > 0 ? _sbVisible[0] : -1;
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

    /// <summary>A blueprint button (or the self-test): the server builds it in front of you.</summary>
    public void SandboxPartsSpawn(Game game, int blueprint)
    {
        game.SandboxSend(CActKind.Blueprint, part: (byte)blueprint);
        if (!SandboxPartsKeepOpen)
            CloseSandboxParts();
    }

    /// <summary>The window (every frame while open). It closes itself outside Sandbox or where the map turns it off.</summary>
    public void DrawSandboxParts(Game game, Settings settings)
    {
        if (!game.SandboxPartsMenu || game.Local is not { } me)
        {
            CloseSandboxParts();
            return;
        }
        SbBuildRows();
        _sbSearchInput ??= new ImInputString(33);
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(640 * _scale, vp.WorkSize.X - 32);
        var h = vp.WorkSize.Y * 0.82f;
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + 24 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.5f), ImGuiCond.Always, new float2(0, 0.5f));
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.92f);
        var open = true;
        if (ImGui.Begin("Parts (debug)###sbparts"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ref var hand = ref game.SandboxHands.Hand(me.Id);
            ImGui.PushTextWrapPos(0);
            ImGuiSafe.TextColored(Orange, "DEBUG TOOL: any part into your hands, any blueprint in front of you. The final game won't hand parts out like this."u8);
            ImGuiSafe.TextColored(Dim, $"Click a part (or Up/Down and Enter): it goes in your hands and its tool comes out. [{Keybinds.Name(settings.Bind(GameAction.Inventory))}] or Esc closes (Esc while typing a search).");
            ImGui.PopTextWrapPos();
            // The search box (typing refilters; Enter takes the highlighted part).
            if (_sbFocusSearch)
            {
                ImGui.SetKeyboardFocusHere();
                _sbFocusSearch = false;
            }
            ImGui.SetNextItemWidth(260 * _scale);
            try
            {
                if (ImGui.InputTextWithHint("##sbsearch"u8, "Search parts..."u8, _sbSearchInput))
                {
                    _sbSearch = ImGuiSafe.Text(_sbSearchInput).Trim();
                    _sbFilterVersion++;
                }
            }
            catch (Exception ex)
            {
                ImGuiSafe.FieldFailed("sandbox part search", ex);
            }
            ImGui.SameLine();
            ImGui.Checkbox("Keep open after picking"u8, ref SandboxPartsKeepOpen);
            SbFilter();
            // The keys: Up and Down move the highlight through the visible rows, Enter takes it.
            var picked = (ConstructPartDef?)null;
            if (_sbVisibleCount > 0)
            {
                var pos = 0;
                for (var i = 0; i < _sbVisibleCount; i++)
                    if (_sbVisible[i] == _sbSel)
                        pos = i;
                if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true))
                {
                    _sbSel = _sbVisible[Math.Min(pos + 1, _sbVisibleCount - 1)];
                    _sbScrollToSel = true;
                }
                if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true))
                {
                    _sbSel = _sbVisible[Math.Max(pos - 1, 0)];
                    _sbScrollToSel = true;
                }
                if ((ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)) && _sbSel >= 0)
                    picked = _sbRows[_sbSel].Def;
            }
            // What is in your hands now, and the recent row.
            var held = ConstructParts.Get(hand.Held);
            ImGuiSafe.TextColored(Dim, $"In your hands: {(held != null ? held.Name : "nothing")}   ({_sbVisibleCount} of {_sbRows.Length} parts shown)");
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Dim, "Recent:"u8);
            var any = false;
            for (var i = 0; i < _sbRecent.Length; i++)
                if (ConstructParts.Get(_sbRecent[i]) is { } rd)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"{rd.Name}##sbrecent{i}"))
                        picked = rd;
                    any = true;
                }
            if (!any)
            {
                ImGui.SameLine();
                ImGui.TextColored(Dim, "(nothing yet)"u8);
            }
            // Blueprints: built whole in front of you.
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Dim, "Spawn a blueprint:"u8);
            var spawn = -1;
            for (var i = 0; i < _sbBlueprintLabels.Length; i++)
            {
                ImGui.SameLine();
                if (ImGui.GetContentRegionAvail().X < 90 * _scale)
                    ImGui.NewLine();
                if (ImGui.SmallButton(_sbBlueprintLabels[i]))
                    spawn = i;
            }
            ImGui.Separator();
            // The list, grouped by role.
            if (ImGui.BeginChild("##sbpartlist"u8, new float2(0, 0), ImGuiChildFlags.None))
            {
                var dl = ImGui.GetWindowDrawList();
                var sw = 18 * _scale;
                var group = -1;
                for (var v = 0; v < _sbVisibleCount; v++)
                {
                    var r = _sbRows[_sbVisible[v]];
                    var role = (int)r.Def.Role;
                    if (role != group)
                    {
                        group = role;
                        ImGui.SeparatorText(role < _sbGroupLabels.Length ? _sbGroupLabels[role] : "Other");
                    }
                    // The swatch: the part's main colour, its last box's colour as a corner.
                    var at = ImGui.GetCursorScreenPos();
                    var y = at.Y + 2 * _scale;
                    dl.AddRectFilled(new float2(at.X, y), new float2(at.X + sw, y + sw), r.Color);
                    dl.AddRectFilled(new float2(at.X + sw * 0.55f, y + sw * 0.55f), new float2(at.X + sw, y + sw), r.Color2);
                    dl.AddRect(new float2(at.X, y), new float2(at.X + sw, y + sw), new ImColor8(20, 20, 20, 255));
                    ImGui.Dummy(new float2(sw, sw));
                    ImGui.SameLine();
                    var sel = _sbVisible[v] == _sbSel;
                    ImGui.BeginGroup();
                    if (ImGui.Selectable(r.Label, sel, ImGuiSelectableFlags.None, new float2(180 * _scale, 0)))
                    {
                        _sbSel = _sbVisible[v];
                        picked = r.Def;
                    }
                    if (sel && _sbScrollToSel)
                    {
                        ImGui.SetScrollHereY(0.5f);
                        _sbScrollToSel = false;
                    }
                    ImGui.SameLine();
                    ImGuiSafe.TextColored(r.Def.Tool == ConstructTool.Welder ? Orange : Dim, r.Tool);
                    ImGui.SameLine();
                    ImGui.Text(r.Stats);
                    ImGui.PushTextWrapPos(0);
                    ImGuiSafe.TextDisabled(r.Def.Description);
                    ImGui.PopTextWrapPos();
                    ImGui.EndGroup();
                }
                if (_sbVisibleCount == 0)
                    ImGui.TextColored(Dim, "No part matches the search."u8);
            }
            ImGui.EndChild();
            if (picked != null)
                SandboxPartsPick(game, picked);
            else if (spawn >= 0)
                SandboxPartsSpawn(game, spawn);
        }
        ImGui.End();
        if (!open)
            CloseSandboxParts();
    }
}
