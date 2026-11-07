using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The tool wheel's screen (docs/sandbox/CONSTRUCTS.md, "Your tool wheel"): which tools sit on the wheel and in what order. In
/// Settings > Tool wheel, and in a match from the wheel itself (the Reload key while it is open). A picture of the wheel beside two
/// lists: on the wheel, clockwise from the top (take off, move earlier or later), and off it, each with the other ways to its tool.
/// Taking a tool off or adding one turns the wheel to keep Hands at the bottom if that is where it was. The tool picked on the list
/// shows its outer ring, in order (Settings.ToolWheelOrder), to move about or put back to the default. Every change is saved at
/// once (Settings.ToolWheel) and the wheel in the match picks it up the next time its key goes down.
/// </summary>
public sealed partial class Ui
{
    public bool ToolWheelEditorOpen { get; private set; }

    private readonly List<int> _twOn = new(), _twOff = new();
    private readonly SandboxToolWheel _twPicture = new();
    private int _twSeenStamp = -1, _twSel = -1;
    private string[] _twReach = [];
    private int _twOptSel = -1, _twOptSlot = -1;
    private const int TwOptRows = 8; // options shown before the list scrolls (the paint tool has many)

    public void OpenToolWheelEditor()
    {
        ToolWheelEditorOpen = true;
        _twSeenStamp = -1;
    }

    public void CloseToolWheelEditor() => ToolWheelEditorOpen = false;

    private static string TwName(int slot) => ((SandboxToolWheel.Slot)slot).ToString();

    /// <summary>The ways to a tool besides the wheel, for the off list. Every tool has its Tools tab tile; two have keys too.</summary>
    private static string TwReach(int slot, Settings s) => (SandboxToolWheel.Slot)slot switch
    {
        SandboxToolWheel.Slot.Welder => $"key {Keybinds.NameOf(s, GameAction.Streak1)}, or Tab > Tools",
        SandboxToolWheel.Slot.Linker => $"key {Keybinds.NameOf(s, GameAction.Streak2)}, or Tab > Tools",
        _ => "Tab > Tools",
    };

    private void TwLoad(Settings s)
    {
        if (_twSeenStamp == s.ToolWheelStamp)
            return;
        _twSeenStamp = s.ToolWheelStamp;
        _twOn.Clear();
        _twOn.AddRange(SandboxToolWheel.Resolve(s.ToolWheel, s.ToolWheelOff));
        _twOff.Clear();
        for (var i = 0; i < (int)SandboxToolWheel.Slot.COUNT; i++)
            if (!_twOn.Contains(i))
                _twOff.Add(i);
        _twSel = Math.Min(_twSel, _twOn.Count - 1);
        _twReach = _twOff.Select(o => TwReach(o, s)).ToArray();
        _twPicture.SetOptions(SandboxToolWheel.Slot.Hands, null); // (no outer ring on the picture)
        _twPicture.SetLayout(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_twOn));
        _twPicture.Menu.Help = "";
        _twPicture.Menu.Open();
        _twPicture.Menu.Preselect(_twSel);
    }

    /// <summary>Saves the lists; the default wheel is saved as the default, so it follows the game's later changes.</summary>
    private void TwSave(Settings s)
    {
        var dflt = _twOn.SequenceEqual(SandboxToolWheel.DefaultLayout());
        s.ToolWheel = dflt ? null : _twOn.Select(TwName).ToList();
        s.ToolWheelOff = dflt ? new List<string>() : _twOff.Select(TwName).ToList();
        s.ToolWheelStamp++;
        if (!TwNoSaveTest)
            s.Save();
    }

    internal bool TwNoSaveTest; // (the self-test changes a Settings of its own and writes nothing)

    /// <summary>The screen's body (the Settings tab's, and the window's in a match).</summary>
    public void DrawToolWheelEditor(Settings s)
    {
        TwLoad(s);
        var scale = 0.75f * _scale;
        var side = 2f * (RadialMenu.OuterEdge * scale + 34f * scale);
        ImGui.TextColored(Dim, "Pick what sits on the tool wheel. A tool off the wheel stays in the Tab inventory's Tools tab."u8);
        // The picture: click a segment to select it.
        var at = ImGui.GetCursorScreenPos();
        var centre = new float2(at.X + side * 0.5f, at.Y + side * 0.5f);
        ImGui.InvisibleButton("##twpicture"u8, new float2(side, side));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            var m = ImGui.GetMousePos();
            var seg = RadialMenu.SegmentAt(new Vector2(m.X - centre.X, m.Y - centre.Y), _twOn.Count, 10f);
            if (seg >= 0)
            {
                _twSel = seg;
                _twPicture.Menu.Preselect(seg);
            }
        }
        if (_twOn.Count > 0)
            _twPicture.Menu.Draw(ImGui.GetWindowDrawList(), centre, scale, SandboxToolWheel.Icons);
        else
            ImGuiSafe.TextColored(Dim, SandboxToolWheel.EmptyText);
        ImGui.SameLine();
        ImGui.BeginGroup();
        // A button only notes what it asks for; the lists change after the group is drawn (TwApply), so nothing drawn
        // later in the frame reads a list that a button has just shortened (the crash: taking off the last tool on the list).
        var act = TwAct.None;
        var actAt = -1;
        ImGui.TextColored(Orange, "On the wheel, clockwise from the top"u8);
        var rows = Math.Min(_twOn.Count, _twPicture.Menu.Items.Length);
        for (var i = 0; i < rows; i++)
            if (ImGui.Selectable($"{_twPicture.Menu.Items[i].Label}##twon{i}", i == _twSel))
            {
                _twSel = i;
                _twPicture.Menu.Preselect(i);
            }
        var has = _twSel >= 0 && _twSel < _twOn.Count;
        ImGui.BeginDisabled(!has);
        if (ImGui.Button("Take off"u8) && has)
            act = TwAct.TakeOff;
        ImGui.SameLine();
        if (ImGui.Button("Move earlier"u8) && has)
            act = TwAct.Earlier;
        ImGui.SameLine();
        if (ImGui.Button("Move later"u8) && has)
            act = TwAct.Later;
        ImGui.EndDisabled();
        if (has)
            TwRingOrder(s, _twOn[_twSel]);
        ImGui.Spacing();
        ImGui.TextColored(Orange, "Off the wheel"u8);
        if (_twOff.Count == 0)
            ImGui.TextColored(Dim, "Every tool is on it"u8);
        for (var i = 0; i < _twOff.Count; i++)
        {
            if (ImGui.SmallButton($"Add##twoff{i}"))
            {
                act = TwAct.Add;
                actAt = i;
            }
            ImGui.SameLine();
            ImGui.Text(_twPicture.ItemLabel(_twOff[i]));
            ImGui.SameLine();
            if (i < _twReach.Length)
                ImGuiSafe.TextColored(Dim, _twReach[i]);
        }
        ImGui.Spacing();
        if (ImGui.Button("Default wheel"u8))
            act = TwAct.Default;
        ImGui.EndGroup();
        TwApply(s, act, actAt);
    }

    internal enum TwAct { None, TakeOff, Earlier, Later, Add, Default }

    /// <summary>
    /// One change to the wheel's lists, outside any drawing: <paramref name="act"/> on the selected tool, or (Add) on entry
    /// <paramref name="offAt"/> of the off list. Anything out of range does nothing. Saves and moves the stamp, so the lists,
    /// the picture and the reach text are made again on the next frame.
    /// </summary>
    internal void TwApply(Settings s, TwAct act, int offAt = -1)
    {
        var has = _twSel >= 0 && _twSel < _twOn.Count;
        switch (act)
        {
            case TwAct.TakeOff when has:
            {
                var south = SandboxToolWheel.HandsSouth(_twOn);
                _twOn.RemoveAt(_twSel);
                if (south)
                    SandboxToolWheel.TurnHandsSouth(_twOn);
                _twSel = Math.Min(_twSel, _twOn.Count - 1);
                break;
            }
            case TwAct.Earlier or TwAct.Later when has && _twOn.Count > 1:
            {
                var to = (_twSel + (act == TwAct.Earlier ? -1 : 1) + _twOn.Count) % _twOn.Count;
                (_twOn[_twSel], _twOn[to]) = (_twOn[to], _twOn[_twSel]);
                _twSel = to;
                break;
            }
            case TwAct.Add when offAt >= 0 && offAt < _twOff.Count && !_twOn.Contains(_twOff[offAt]):
            {
                var south = SandboxToolWheel.HandsSouth(_twOn);
                var tool = _twOff[offAt];
                _twOn.Add(tool);
                if (south || tool == (int)SandboxToolWheel.Slot.Hands && _twOn.Count == 1)
                    SandboxToolWheel.TurnHandsSouth(_twOn);
                break;
            }
            case TwAct.Default:
                _twOn.Clear();
                _twOn.AddRange(SandboxToolWheel.DefaultLayout());
                _twSel = Math.Min(_twSel, _twOn.Count - 1);
                break;
            default:
                return;
        }
        _twOff.Clear();
        for (var i = 0; i < (int)SandboxToolWheel.Slot.COUNT; i++)
            if (!_twOn.Contains(i))
                _twOff.Add(i);
        TwSave(s); // (the stamp moved: the lists, the picture and the reach text are made again next frame)
    }

    /// <summary>For the self-test: the lists as the screen holds them, and a pick on the on list.</summary>
    internal IReadOnlyList<int> TwOnTest => _twOn;
    internal IReadOnlyList<int> TwOffTest => _twOff;
    internal void TwSelectTest(int i) => _twSel = i;
    internal void TwLoadTest(Settings s) => TwLoad(s);

    /// <summary>The picked tool's outer ring, in order, and the buttons that change it. A player's order is saved by label; the default order isn't saved.</summary>
    private void TwRingOrder(Settings s, int slot)
    {
        if (Program.ToolRingOf(slot) is not { Length: > 1 } options)
            return;
        if (slot != _twOptSlot)
        {
            _twOptSlot = slot;
            _twOptSel = -1;
        }
        var key = ((SandboxToolWheel.Slot)slot).ToString();
        var order = SandboxToolWheel.OrderOf(options, s.ToolWheelOrder.GetValueOrDefault(key));
        ImGui.Spacing();
        ImGuiSafe.TextColored(Orange, $"{_twPicture.ItemLabel(slot)}'s outer ring, in order");
        var rowHeight = ImGui.GetTextLineHeightWithSpacing();
        ImGui.BeginChild("##twring"u8, new float2(0, Math.Min(order.Length, TwOptRows) * rowHeight + 4 * _scale));
        for (var i = 0; i < order.Length; i++)
            if (ImGui.Selectable($"{options[order[i]].Label}##twopt{i}", i == _twOptSel))
                _twOptSel = i;
        ImGui.EndChild();
        var changed = false;
        var sel = _twOptSel;
        ImGui.BeginDisabled(sel <= 0 || sel >= order.Length);
        if (ImGui.Button("Move earlier##twopt"u8))
        {
            (order[sel], order[sel - 1]) = (order[sel - 1], order[sel]);
            _twOptSel = sel - 1;
            changed = true;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(sel < 0 || sel >= order.Length - 1);
        if (ImGui.Button("Move later##twopt"u8))
        {
            (order[sel], order[sel + 1]) = (order[sel + 1], order[sel]);
            _twOptSel = sel + 1;
            changed = true;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        var isDefault = !s.ToolWheelOrder.ContainsKey(key);
        ImGui.BeginDisabled(isDefault);
        if (ImGui.Button("Default order##twopt"u8))
        {
            s.ToolWheelOrder.Remove(key);
            _twOptSel = -1;
            s.ToolWheelStamp++;
            s.Save();
        }
        ImGui.EndDisabled();
        if (!changed)
            return;
        if (order.SequenceEqual(Enumerable.Range(0, order.Length)))
            s.ToolWheelOrder.Remove(key);
        else
            s.ToolWheelOrder[key] = order.Select(k => options[k].Label).ToList();
        s.ToolWheelStamp++;
        s.Save();
    }

    /// <summary>The screen as a window over the match. False: it should close.</summary>
    public bool DrawToolWheelEditorWindow(Settings s)
    {
        var open = true;
        ImGui.SetNextWindowSize(new float2(780 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.9f);
        var vp = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowPos(new float2(vp.X * 0.5f - 390 * _scale, vp.Y * 0.15f), ImGuiCond.Appearing);
        if (ImGui.Begin("Tool wheel###sbtoolwheel"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            DrawToolWheelEditor(s);
            if (ImGui.Button("Close"u8))
                open = false;
            ImGui.SameLine();
            ImGui.TextColored(Dim, "Esc closes"u8);
        }
        ImGui.End();
        return open;
    }
}
