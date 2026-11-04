using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The tool wheel's screen (docs/sandbox/CONSTRUCTS.md, "Your tool wheel"): which tools sit on the wheel and in what order. In
/// Settings > Tool wheel, and in a match from the wheel itself (the Reload key while it is open). A picture of the wheel beside two
/// lists: on the wheel, clockwise from the top (take off, move earlier or later), and off it, each with the other ways to its tool.
/// Taking a tool off or adding one turns the wheel to keep Hands at the bottom if that is where it was. Every change is saved at
/// once (Settings.ToolWheel) and the wheel in the match picks it up the next time its key goes down.
/// </summary>
public sealed partial class Ui
{
    public bool ToolWheelEditorOpen { get; private set; }

    private readonly List<int> _twOn = new(), _twOff = new();
    private readonly SandboxToolWheel _twPicture = new();
    private int _twSeenStamp = -1, _twSel = -1;
    private string[] _twReach = [];

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
        s.Save();
    }

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
        var changed = false;
        ImGui.TextColored(Orange, "On the wheel, clockwise from the top"u8);
        for (var i = 0; i < _twOn.Count; i++)
            if (ImGui.Selectable($"{_twPicture.Menu.Items[i].Label}##twon{i}", i == _twSel))
            {
                _twSel = i;
                _twPicture.Menu.Preselect(i);
            }
        var has = _twSel >= 0 && _twSel < _twOn.Count;
        ImGui.BeginDisabled(!has);
        if (ImGui.Button("Take off"u8) && has)
        {
            var south = SandboxToolWheel.HandsSouth(_twOn);
            _twOn.RemoveAt(_twSel);
            if (south)
                SandboxToolWheel.TurnHandsSouth(_twOn);
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Move earlier"u8) && has && _twOn.Count > 1)
        {
            var to = (_twSel - 1 + _twOn.Count) % _twOn.Count;
            (_twOn[_twSel], _twOn[to]) = (_twOn[to], _twOn[_twSel]);
            _twSel = to;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Move later"u8) && has && _twOn.Count > 1)
        {
            var to = (_twSel + 1) % _twOn.Count;
            (_twOn[_twSel], _twOn[to]) = (_twOn[to], _twOn[_twSel]);
            _twSel = to;
            changed = true;
        }
        ImGui.EndDisabled();
        ImGui.Spacing();
        ImGui.TextColored(Orange, "Off the wheel"u8);
        if (_twOff.Count == 0)
            ImGui.TextColored(Dim, "Every tool is on it"u8);
        for (var i = 0; i < _twOff.Count; i++)
        {
            if (ImGui.SmallButton($"Add##twoff{i}"))
            {
                var south = SandboxToolWheel.HandsSouth(_twOn);
                _twOn.Add(_twOff[i]);
                if (south || _twOff[i] == (int)SandboxToolWheel.Slot.Hands && _twOn.Count == 1)
                    SandboxToolWheel.TurnHandsSouth(_twOn);
                changed = true;
            }
            ImGui.SameLine();
            ImGui.Text(_twPicture.ItemLabel(_twOff[i]));
            ImGui.SameLine();
            if (i < _twReach.Length)
                ImGuiSafe.TextColored(Dim, _twReach[i]);
        }
        ImGui.Spacing();
        if (ImGui.Button("Default wheel"u8))
        {
            _twOn.Clear();
            _twOn.AddRange(SandboxToolWheel.DefaultLayout());
            _twOff.Clear();
            changed = true;
        }
        ImGui.EndGroup();
        if (!changed)
            return;
        _twOff.Clear();
        for (var i = 0; i < (int)SandboxToolWheel.Slot.COUNT; i++)
            if (!_twOn.Contains(i))
                _twOff.Add(i);
        TwSave(s); // (the stamp moved: the lists, the picture and the reach text are made again next frame)
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
