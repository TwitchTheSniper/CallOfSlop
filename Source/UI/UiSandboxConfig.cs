using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the configure tool's settings panel (docs/sandbox/CONSTRUCTS.md, "The configure tool"). Fire on a part with
/// the configure tool opens it, beside the part on the screen (a plain window: no dim behind it; the player can drag it,
/// and later panels open where it was left until snapped back): a header (the part's tile, name, construct and
/// health), the settings grouped as the part file's schema groups them (sliders with numeric entry, dropdowns, toggles,
/// colour pickers with swatches, a key-capture field), each with a tooltip and a reset; Reset, Copy and Paste between
/// parts of a kind; the shift-selected parts of the same kind changed together; and the part's wiring (hover a row to
/// highlight the part at the other end, click to open its settings). Changes are sent as they are made (at most ten a
/// second while a slider is dragged) and the server's values flow back in; the world previews (ConstructRenderConfig.cs)
/// follow the panel at once. Drawn without allocating while open: every text is made once (the wiring and health lines
/// again only when they change).
/// </summary>
public sealed partial class Ui
{
    public bool SandboxConfigOpen { get; private set; }

    /// <summary>The setting slot waiting for a key press (an input reader's key), or -1. Program.cs routes the press here.</summary>
    public int SandboxConfigCapture = -1;

    private ushort _cfBody;
    private int _cfPart = -1;
    private ConstructPartDef? _cfDef;
    private readonly float[] _cfVals = new float[ConstructConfig.MaxSlots];
    private readonly double[] _cfHoldUntil = new double[ConstructConfig.MaxSlots]; // (sent: the server's echo is awaited until then)
    private int _cfPending; // bit per slot: changed, not sent yet
    private double _cfSentAt;
    private readonly byte[] _cfSel = new byte[ConstructEdits.MaxConfigParts];
    private int _cfSelCount;
    private bool _cfAll = true; // change every selected part
    private readonly byte[] _cfSendParts = new byte[ConstructEdits.MaxConfigParts];
    private readonly byte[] _cfSendSlots = new byte[ConstructConfig.MaxSlots];
    private readonly float[] _cfSendVals = new float[ConstructConfig.MaxSlots];
    private ushort _cfClipDef = ConstructParts.None;
    private readonly float[] _cfClip = new float[ConstructConfig.MaxSlots];
    private string _cfTitle = "", _cfWho = "", _cfHealth = "", _cfSelText = "", _cfClipText = "";
    private int _cfHealthKey = -1, _cfSelKey = -1, _cfWireKey = -1;
    private string[] _cfWireRows = [];
    private int[] _cfWireIndex = [], _cfWireOther = [];
    private string[] _cfGroups = [];
    private int _cfOpenRequest = -1; // a wiring row clicked: open that part next frame
    private float2 _cfPos, _cfSize; // where the window is on the screen, and its size (last frame)
    private bool _cfMoved; // the player dragged it: every panel opens there this session, until snapped back
    private bool _cfPlace; // put the window at _cfPos next frame (a panel opening where the player left the last one)
    private bool _cfSeen; // the window has been drawn since this panel opened (so a change of position is a drag)

    /// <summary>The window's place and size on the screen (last frame), and whether the player has moved it (self-test).</summary>
    public (float2 Pos, float2 Size, bool Moved) SandboxConfigWindow => (_cfPos, _cfSize, _cfMoved);

    /// <summary>Back beside the part (the Snap back button, or a double-click on the title bar).</summary>
    public void SandboxConfigSnapBack() => _cfMoved = false;

    /// <summary>
    /// How dark the scene is drawn behind the UI while the mouse is free (Program.cs's HUD overlay): the menus dim it
    /// (<paramref name="menuOpen"/>: the pause menu, a workbench, the mixer...), a free mouse alone dims it a little, and
    /// the Sandbox's non-modal panels (this settings panel, the parts inventory) don't dim it at all: the world, the part's
    /// outline and the previews stay at full brightness behind them.
    /// </summary>
    public float SceneDim(bool mouseCaptured, bool menuOpen)
    {
        if (mouseCaptured)
            return 0;
        if (menuOpen || MenuOpen || PauseOpen)
            return 0.55f;
        return SandboxConfigOpen || SandboxPartsOpen ? 0 : 0.35f;
    }

    /// <summary>The panel's construct and part (self-test), or (0, -1).</summary>
    public (ushort Body, int Part) SandboxConfigTarget => SandboxConfigOpen ? (_cfBody, _cfPart) : ((ushort)0, -1);

    /// <summary>The parts the panel changes besides its own (shift-selected, same kind).</summary>
    public ReadOnlySpan<byte> SandboxConfigSelection => _cfSel.AsSpan(0, _cfSelCount);

    /// <summary>The value the panel shows in a slot (self-test).</summary>
    public float SandboxConfigValue(int slot) => _cfVals[slot];

    private static readonly ImColor8 CfAccent = new(220, 130, 240, 255);
    private static readonly float4 CfAccentF = new(0.86f, 0.51f, 0.94f, 1f);

    /// <summary>Swatches under a colour picker.</summary>
    private static readonly float4[] CfSwatches =
    [
        new(1f, 0.93f, 0.6f, 1f), new(1f, 1f, 1f, 1f), new(1f, 0.25f, 0.2f, 1f), new(1f, 0.6f, 0.15f, 1f),
        new(1f, 0.95f, 0.2f, 1f), new(0.3f, 1f, 0.35f, 1f), new(0.3f, 0.6f, 1f, 1f), new(0.8f, 0.4f, 1f, 1f),
    ];

    private static readonly string[] CfSwatchIds = ["##cfsw0", "##cfsw1", "##cfsw2", "##cfsw3", "##cfsw4", "##cfsw5", "##cfsw6", "##cfsw7"];
    private static readonly string[] CfWireIds = MakeIds("##cfw", 64);

    private static string[] MakeIds(string stem, int n)
    {
        var a = new string[n];
        for (var i = 0; i < n; i++)
            a[i] = stem + i;
        return a;
    }

    /// <summary>
    /// Shift-click with the configure tool: adds the part to the selection (or takes it out). A part of another kind or
    /// construct starts a new selection. Returns how many are selected.
    /// </summary>
    public int SandboxConfigSelect(Construct c, int part)
    {
        if (_cfSelCount > 0 && (_cfBody != c.Id || c.Parts[_cfSel[0]].Def != c.Parts[part].Def || _cfSel[0] >= c.PartCount))
            _cfSelCount = 0;
        _cfBody = c.Id;
        for (var k = 0; k < _cfSelCount; k++)
            if (_cfSel[k] == part)
            {
                _cfSel[k] = _cfSel[--_cfSelCount];
                return _cfSelCount;
            }
        if (_cfSelCount < _cfSel.Length)
            _cfSel[_cfSelCount++] = (byte)part;
        return _cfSelCount;
    }

    public void SandboxConfigClearSelection() => _cfSelCount = 0;

    /// <summary>Opens the panel on a part (the selection stays when it is of the same kind on the same construct; else it is cleared).</summary>
    public void OpenSandboxConfig(Construct c, int part)
    {
        var d = c.DefOf(part);
        if (_cfSelCount > 0 && (_cfBody != c.Id || _cfSel[0] >= c.PartCount || c.Parts[_cfSel[0]].Def != c.Parts[part].Def))
            _cfSelCount = 0;
        SandboxConfigOpen = true;
        SandboxConfigCapture = -1;
        _cfBody = c.Id;
        _cfPart = part;
        _cfDef = d;
        _cfPlace = _cfMoved;
        _cfSeen = false;
        _cfPending = 0;
        Array.Clear(_cfHoldUntil);
        for (var s = 0; s < d.Config.Length; s++)
            _cfVals[s] = ConstructConfig.At(c.Parts[part], d, s);
        _cfTitle = d.Name;
        _cfHealthKey = _cfSelKey = _cfWireKey = -1;
        var groups = new List<string>();
        foreach (var e in d.Config)
            if (!groups.Contains(e.Group))
                groups.Add(e.Group);
        _cfGroups = groups.ToArray();
        _cfClipText = _cfClipDef == d.Id ? $"Paste ({d.Name})" : "Paste";
        SbBuildRows(); // (the part's tile)
    }

    public void CloseSandboxConfig()
    {
        SandboxConfigOpen = false;
        SandboxConfigCapture = -1;
        _cfPending = 0;
        ConstructRender.ClearPreview();
    }

    /// <summary>A key press while capturing (Program.cs): slot set to option <paramref name="option"/>; -1 cancels.</summary>
    public void SandboxConfigCaptured(Game game, int option, double now)
    {
        var slot = SandboxConfigCapture;
        SandboxConfigCapture = -1;
        if (option < 0 || _cfDef == null || slot < 0 || slot >= _cfDef.Config.Length)
            return;
        _cfVals[slot] = option;
        _cfPending |= 1 << slot;
        SandboxConfigFlush(game, now, true);
    }

    /// <summary>Sets a slot from outside the panel's controls (the self-test), sent at once like a dropdown.</summary>
    public void SandboxConfigSet(Game game, int slot, float value, double now)
    {
        if (_cfDef == null || slot < 0 || slot >= _cfDef.Config.Length)
            return;
        _cfVals[slot] = _cfDef.Config[slot].Clamp(value);
        _cfPending |= 1 << slot;
        SandboxConfigFlush(game, now, true);
    }

    /// <summary>Every setting back to its default (the Reset button).</summary>
    public void SandboxConfigReset(Game game, double now)
    {
        if (_cfDef is not { } d)
            return;
        for (var s = 0; s < d.Config.Length; s++)
        {
            _cfVals[s] = d.Config[s].Bound ? d.DefaultSetting : d.Config[s].Default;
            _cfPending |= 1 << s;
        }
        SandboxConfigFlush(game, now, true);
    }

    public void SandboxConfigCopy()
    {
        if (_cfDef is not { } d)
            return;
        _cfClipDef = d.Id;
        Array.Copy(_cfVals, _cfClip, ConstructConfig.MaxSlots);
        _cfClipText = $"Paste ({d.Name})";
    }

    /// <summary>The copied settings onto this part (and the selection): only between parts of the same kind.</summary>
    public bool SandboxConfigPaste(Game game, double now)
    {
        if (_cfDef is not { } d || _cfClipDef != d.Id)
            return false;
        for (var s = 0; s < d.Config.Length; s++)
        {
            _cfVals[s] = _cfClip[s];
            _cfPending |= 1 << s;
        }
        SandboxConfigFlush(game, now, true);
        return true;
    }

    /// <summary>Every value onto the selected parts (Apply to all selected), whatever has changed.</summary>
    public void SandboxConfigApplyAll(Game game, double now)
    {
        if (_cfDef is not { } d)
            return;
        for (var s = 0; s < d.Config.Length; s++)
            _cfPending |= 1 << s;
        var all = _cfAll;
        _cfAll = true;
        SandboxConfigFlush(game, now, true);
        _cfAll = all;
    }

    /// <summary>Sends the changed slots (now, or when the last send was a tenth of a second ago).</summary>
    private void SandboxConfigFlush(Game game, double now, bool force)
    {
        if (_cfPending == 0 || _cfDef is not { } d || game.Constructs.Get(_cfBody) is not { } c)
            return;
        if (!force && now - _cfSentAt < 0.1)
            return;
        var n = 0;
        _cfSendParts[n++] = (byte)_cfPart;
        if (_cfAll)
            for (var k = 0; k < _cfSelCount && n < _cfSendParts.Length; k++)
                if (_cfSel[k] != _cfPart && _cfSel[k] < c.PartCount && c.Parts[_cfSel[k]].Def == d.Id)
                    _cfSendParts[n++] = _cfSel[k];
        var m = 0;
        for (var s = 0; s < d.Config.Length; s++)
            if ((_cfPending & (1 << s)) != 0)
            {
                _cfSendSlots[m] = (byte)s;
                _cfSendVals[m++] = _cfVals[s];
                _cfHoldUntil[s] = now + 0.6;
            }
        _cfPending = 0;
        _cfSentAt = now;
        game.SandboxConfigure(_cfBody, _cfSendParts.AsSpan(0, n), _cfSendSlots.AsSpan(0, m), _cfSendVals.AsSpan(0, m));
    }

    /// <summary>The panel, every frame while open. Returns false when it closed itself (the part or construct went).</summary>
    public bool DrawSandboxConfig(Game game, double now)
    {
        if (!SandboxConfigOpen || _cfDef is not { } d || game.Constructs.Get(_cfBody) is not { } c || _cfPart >= c.PartCount || c.Parts[_cfPart].Def != d.Id)
        {
            CloseSandboxConfig();
            return false;
        }
        ref var part = ref c.Parts[_cfPart];
        // The server's values flow in, except where the panel has its own still on the way.
        for (var s = 0; s < d.Config.Length; s++)
            if ((_cfPending & (1 << s)) == 0)
            {
                var server = ConstructConfig.At(part, d, s);
                if (now >= _cfHoldUntil[s] || server == _cfVals[s])
                {
                    _cfVals[s] = server;
                    _cfHoldUntil[s] = 0;
                }
            }
        // Texts that change: health, the selection, the wiring (made only when they do).
        var hk = (int)MathF.Ceiling(part.Health) * 2 + (part.Broken ? 1 : 0);
        if (hk != _cfHealthKey)
        {
            _cfHealthKey = hk;
            _cfHealth = part.Broken ? "Wrecked" : $"Health {part.Health:0} / {d.Health:0}";
            _cfWho = $"Construct {c.Id}{(c.Owner == game.LocalId ? " - yours" : c.Owner == 255 ? " - nobody's" : game.Players.TryGetValue(c.Owner, out var op) ? $" - {op.Name}'s" : "")}   part {_cfPart}";
        }
        var sk = _cfSelCount * 2 + (_cfAll ? 1 : 0);
        if (sk != _cfSelKey)
        {
            _cfSelKey = sk;
            _cfSelText = _cfSelCount > 1 ? $"Change all {_cfSelCount} selected {d.Name}s" : "";
        }
        SandboxConfigWiring(c);
        // Previews in the world.
        ConstructRender.PreviewBody = c;
        ConstructRender.PreviewPart = _cfPart;
        Array.Copy(_cfVals, ConstructRender.PreviewCfg, ConstructConfig.MaxSlots);
        ConstructRender.PreviewSelectedCount = _cfSelCount;
        Array.Copy(_cfSel, ConstructRender.PreviewSelected, _cfSelCount);
        ConstructRender.PreviewHighlightWire = -1;

        // A plain movable window (no dim behind it: Ui.SceneDim). Until the player drags it, it sits beside the part on the
        // screen, following it, but not while a mouse button is down (that may be the start of a drag); once dragged it
        // stays where it was left, for every panel this session, until snapped back (the button, or a double-click on the
        // title bar).
        var vp = ImGui.GetMainViewport();
        var w = 430 * _scale;
        if (!_cfMoved && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            ImGui.SetNextWindowPos(SandboxConfigAnchor(c, w), ImGuiCond.Always);
        else if (_cfMoved && _cfPlace)
            ImGui.SetNextWindowPos(_cfPos, ImGuiCond.Always);
        _cfPlace = false;
        ImGui.SetNextWindowSize(new float2(w, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.82f); // (slightly see-through: the world and the previews show behind it)
        var open = true;
        var labelW = 150 * _scale;
        var shown = ImGui.Begin("Settings###sbconfig"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize);
        {
            // Kept fully on the screen; a position the panel didn't set itself is the player's drag.
            var wp = ImGui.GetWindowPos();
            var ws = ImGui.GetWindowSize();
            var min = vp.WorkPos;
            var max = new float2(MathF.Max(min.X, vp.WorkPos.X + vp.WorkSize.X - ws.X), MathF.Max(min.Y, vp.WorkPos.Y + vp.WorkSize.Y - ws.Y));
            var cp = new float2(Math.Clamp(wp.X, min.X, max.X), Math.Clamp(wp.Y, min.Y, max.Y));
            if (cp.X != wp.X || cp.Y != wp.Y)
                ImGui.SetWindowPos(cp, ImGuiCond.Always);
            if (_cfSeen && (wp.X != _cfPos.X || wp.Y != _cfPos.Y) && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                _cfMoved = true;
            _cfPos = cp;
            _cfSize = ws;
            _cfSeen = true;
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && ImGui.IsMouseHoveringRect(cp, new float2(cp.X + ws.X, cp.Y + ImGui.GetFrameHeight())))
                SandboxConfigSnapBack();
        }
        if (shown)
        {
            // Header: the tile, the name, the construct, the health.
            var icon = ImGui.GetFrameHeight() * 2.3f;
            var at = ImGui.GetCursorScreenPos();
            if (d.Id < _sbItems[0].Length)
                SbDrawTile(ImGui.GetWindowDrawList(), _sbItems[0][d.Id], at, icon, false, false, false);
            ImGui.Dummy(new float2(icon, icon));
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGuiSafe.TextColored(CfAccentF, _cfTitle);
            ImGuiSafe.TextColored(Dim, _cfWho);
            var hp = Math.Clamp(part.Health / MathF.Max(1, d.Health), 0, 1);
            ImGui.ProgressBar(hp, new float2(-1, ImGui.GetTextLineHeight()), _cfHealth);
            ImGui.EndGroup();
            if (_cfSelText.Length > 0)
                ImGui.Checkbox(_cfSelText, ref _cfAll);

            // The settings, group by group.
            foreach (var group in _cfGroups)
            {
                ImGui.SeparatorText(group);
                for (var s = 0; s < d.Config.Length; s++)
                {
                    var e = d.Config[s];
                    if (!ReferenceEquals(e.Group, group) && e.Group != group)
                        continue;
                    SandboxConfigRow(game, d, e, s, labelW, now);
                }
            }

            // Wiring.
            if (_cfWireRows.Length > 0)
            {
                ImGui.SeparatorText("Wiring"u8);
                for (var k = 0; k < _cfWireRows.Length && k < CfWireIds.Length; k++)
                {
                    ImGui.PushID(CfWireIds[k]);
                    if (ImGui.Selectable(_cfWireRows[k], false))
                        _cfOpenRequest = _cfWireOther[k];
                    if (ImGui.IsItemHovered())
                    {
                        ConstructRender.PreviewHighlightWire = _cfWireIndex[k];
                        ImGui.SetTooltip("Click: that part's settings"u8);
                    }
                    ImGui.PopID();
                }
            }
            else if (d.HasOutput || d.MaxInputs > 0)
            {
                ImGui.SeparatorText("Wiring"u8);
                ImGui.TextColored(Dim, "No wires (the linking tool wires parts)"u8);
            }

            // Reset, copy, paste, apply, close.
            ImGui.Separator();
            if (ImGui.Button("Reset"u8))
                SandboxConfigReset(game, now);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Every setting back to its default"u8);
            ImGui.SameLine();
            if (ImGui.Button("Copy"u8))
                SandboxConfigCopy();
            ImGui.SameLine();
            ImGui.BeginDisabled(_cfClipDef != d.Id);
            if (ImGui.Button(_cfClipText))
                SandboxConfigPaste(game, now);
            ImGui.EndDisabled();
            if (_cfSelCount > 1)
            {
                ImGui.SameLine();
                if (ImGui.Button("Apply to selected"u8))
                    SandboxConfigApplyAll(game, now);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Every setting shown here onto all the selected parts"u8);
            }
            if (_cfMoved)
            {
                ImGui.SameLine();
                if (ImGui.Button("Snap back"u8))
                    SandboxConfigSnapBack();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Back beside the part (or double-click the title bar)"u8);
            }
            ImGui.SameLine();
            if (ImGui.Button("Close"u8))
                open = false;
            ImGui.TextColored(Dim, "Esc closes. Drag to move. Shift+LMB with the tool selects more of a kind."u8);
        }
        ImGui.End();
        SandboxConfigFlush(game, now, !ImGui.IsAnyItemActive());
        if (_cfOpenRequest >= 0)
        {
            var other = _cfOpenRequest;
            _cfOpenRequest = -1;
            if (other < c.PartCount && c.DefOf(other).Configurable)
                OpenSandboxConfig(c, other);
        }
        if (!open)
        {
            SandboxConfigFlush(game, now, true);
            CloseSandboxConfig();
            return false;
        }
        return true;
    }

    /// <summary>Beside the part on the screen, on whichever side keeps it clear of the part (else on the right edge).</summary>
    private float2 SandboxConfigAnchor(Construct c, float w)
    {
        var vp = ImGui.GetMainViewport();
        var pos = new float2(vp.WorkPos.X + vp.WorkSize.X - w - 24 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.18f);
        var cam = Hud.Camera;
        if (cam.Valid && Hud.Project(cam.ViewProj, ConstructRender.PartCentre(c, _cfPart), vp.WorkSize.X, vp.WorkSize.Y, out var sp, out _))
        {
            var x = sp.X + 70 * _scale;
            if (x + w > vp.WorkSize.X - 12 * _scale)
                x = sp.X - 70 * _scale - w; // (the other side when it would run off)
            pos = new float2(vp.WorkPos.X + Math.Clamp(x, 12 * _scale, MathF.Max(12 * _scale, vp.WorkSize.X - w - 12 * _scale)),
                vp.WorkPos.Y + Math.Clamp(sp.Y - 120 * _scale, 12 * _scale, vp.WorkSize.Y * 0.45f));
        }
        return pos;
    }

    private void SandboxConfigRow(Game game, ConstructPartDef d, CConfigDef e, int s, float labelW, double now)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(e.Label);
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip(e.TooltipText);
        ImGui.SameLine(labelW);
        var resetW = ImGui.GetFrameHeight();
        var fieldW = ImGui.GetContentRegionAvail().X - resetW - ImGui.GetStyle().ItemSpacing.X;
        var v = _cfVals[s];
        var changed = false;
        var discrete = false;
        switch (e.Type)
        {
            case CfgType.Number:
            {
                var inputW = 74 * _scale;
                ImGui.SetNextItemWidth(fieldW - inputW - ImGui.GetStyle().ItemSpacing.X);
                if (ImGui.SliderFloat(e.UiId, ref v, e.Min, e.Max, e.Format, ImGuiSliderFlags.AlwaysClamp))
                    changed = true;
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetTooltip(e.TooltipText);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(inputW);
                if (ImGui.InputFloat(e.InputId, ref v, 0, 0, e.Format, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    changed = true;
                    discrete = true;
                }
                break;
            }
            case CfgType.Toggle:
            {
                var on = v >= 0.5f;
                if (ImGui.Checkbox(e.UiId, ref on))
                {
                    v = on ? 1 : 0;
                    changed = discrete = true;
                }
                ImGui.SameLine();
                ImGui.TextColored(Dim, on ? "on"u8 : "off"u8);
                break;
            }
            case CfgType.Choice:
            {
                ImGui.SetNextItemWidth(fieldW);
                var idx = (int)Math.Clamp(v, 0, Math.Max(0, e.Options.Length - 1));
                if (e.Options.Length > 0 && ImGui.BeginCombo(e.UiId, e.Options[idx], ImGuiComboFlags.None))
                {
                    for (var k = 0; k < e.Options.Length; k++)
                        if (ImGui.Selectable(e.Options[k], k == idx))
                        {
                            v = k;
                            changed = discrete = true;
                        }
                    ImGui.EndCombo();
                }
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetTooltip(e.TooltipText);
                break;
            }
            case CfgType.Key:
            {
                var idx = (int)Math.Clamp(v, 0, Math.Max(0, e.Options.Length - 1));
                var capturing = SandboxConfigCapture == s;
                ImGui.PushStyleColor(ImGuiCol.Button, capturing ? CfAccent : new ImColor8(48, 52, 62, 255));
                if (ImGui.Button(capturing ? "Press a key... (Esc: cancel)"u8 : "Press to bind"u8, new float2(fieldW * 0.55f, 0)))
                    SandboxConfigCapture = capturing ? -1 : s;
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.SetNextItemWidth(fieldW * 0.45f - ImGui.GetStyle().ItemSpacing.X);
                if (e.Options.Length > 0 && ImGui.BeginCombo(e.CaptureId, e.Options[idx], ImGuiComboFlags.None))
                {
                    for (var k = 0; k < e.Options.Length; k++)
                        if (ImGui.Selectable(e.Options[k], k == idx))
                        {
                            v = k;
                            changed = discrete = true;
                        }
                    ImGui.EndCombo();
                }
                break;
            }
            case CfgType.Colour:
            {
                var rgb = ConstructConfig.Rgb(v);
                var col = new float3(rgb.X, rgb.Y, rgb.Z);
                ImGui.SetNextItemWidth(fieldW);
                if (ImGui.ColorEdit3(e.UiId, ref col, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                {
                    v = ConstructConfig.Pack(new System.Numerics.Vector3(col.X, col.Y, col.Z));
                    changed = true;
                }
                // Swatches.
                ImGui.SameLine();
                var sw = ImGui.GetFrameHeight() * 0.8f;
                for (var k = 0; k < CfSwatches.Length; k++)
                {
                    if (k > 0)
                        ImGui.SameLine(0, 3 * _scale);
                    if (ImGui.ColorButton(CfSwatchIds[k], CfSwatches[k], ImGuiColorEditFlags.NoTooltip, new float2(sw, sw)))
                    {
                        v = ConstructConfig.Pack(new System.Numerics.Vector3(CfSwatches[k].X, CfSwatches[k].Y, CfSwatches[k].Z));
                        changed = discrete = true;
                    }
                }
                break;
            }
        }
        // The row's reset (shown while it is off its default).
        var def = e.Bound ? d.DefaultSetting : e.Default;
        ImGui.SameLine();
        ImGui.BeginDisabled(v == def);
        if (ImGui.Button(e.ResetId, new float2(resetW, 0)))
        {
            v = def;
            changed = discrete = true;
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiSafe.SetTooltip(e.DefaultText);
        if (changed)
        {
            _cfVals[s] = e.Clamp(v);
            _cfPending |= 1 << s;
            if (discrete)
                SandboxConfigFlush(game, now, true);
        }
    }

    /// <summary>The wiring rows (made again only when the construct's wires change).</summary>
    private void SandboxConfigWiring(Construct c)
    {
        var key = c.WireCount * 977 + _cfPart * 31 + (int)c.Id;
        for (var k = 0; k < c.WireCount; k++)
            key = key * 31 + c.Wires[k].From * 257 + c.Wires[k].To;
        if (key == _cfWireKey)
            return;
        _cfWireKey = key;
        var rows = new List<string>();
        var idx = new List<int>();
        var other = new List<int>();
        for (var k = 0; k < c.WireCount; k++)
        {
            ref var w = ref c.Wires[k];
            if (w.From >= c.PartCount || w.To >= c.PartCount || (w.From != _cfPart && w.To != _cfPart))
                continue;
            var into = w.To == _cfPart;
            var o = into ? w.From : w.To;
            rows.Add(o == _cfPart ? "Loop: into itself" : $"{(into ? "In from" : "Out to")}: {c.DefOf(o).Name} (part {o}){(c.DefOf(o).Configurable ? "" : " - no settings")}");
            idx.Add(k);
            other.Add(o);
        }
        _cfWireRows = rows.ToArray();
        _cfWireIndex = idx.ToArray();
        _cfWireOther = other.ToArray();
    }
}
