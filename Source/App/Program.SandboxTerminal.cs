using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Smart Terminal's list (docs/sandbox/CONSTRUCTS.md "The Smart Terminal"): no window and no cursor. Look at the terminal
/// within reach and its network's rows stand beside it in the world; the wheel picks, the use key brings the item to the
/// hands, reload sends it to the tray and Chute out. Everything here reads <see cref="TerminalView"/> and sends
/// <see cref="CActKind.TerminalPull"/>, so a display panel can take this file's place.
/// </summary>
public static partial class Program
{
    private const int TerminalRowsShown = 9;

    private static double _tmScroll;
    private static bool _tmWheelOk, _tmDraw;
    private static Vector3 _tmAt;
    private static TerminalView? _tmView;
    private static ushort _tmId = ushort.MaxValue; // the picked row, by what it is: an index goes stale when the stock changes
    private static bool _tmMaterial;
    private static int _tmSel;
    private static long _tmKey = -1;
    private static string _tmText = "";

    private static bool TerminalScroll(double y)
    {
        if (!_tmWheelOk)
            return false;
        _tmScroll += y;
        return true;
    }

    private static void TerminalIdle()
    {
        _tmWheelOk = _tmDraw = false;
        _tmScroll = 0;
        _tmView = null;
    }

    /// <summary>One frame of a terminal under the crosshair. True when it is one: the use key is its, not the part's.</summary>
    private static bool TerminalFrame(Game g, Player me, in SandboxHand h, bool aimed, in ConstructHit hit, bool near)
    {
        TerminalIdleKeepScroll(aimed && near);
        if (!aimed || !near || hit.SlotPlus1 != 0 || hit.RoutePlus1 != 0 || hit.Part < 0 || hit.Part >= hit.Body.PartCount || !hit.Body.DefOf(hit.Part).IsTerminal || h.Seated || h.GrabBody != 0)
            return false;
        var view = g.SandboxTerminal(me.Id);
        if (view.Handle == 0 || view.Handle != hit.Body.Parts[hit.Part].Store || !view.Stock.On)
            return true; // (the store's look line says why)
        var rows = view.Rows;
        if (rows.Count == 0)
        {
            g.SandboxLookText = SandboxKeys.Line(hit.Body.DefOf(hit.Part).Name, "empty network");
            return true;
        }
        _tmWheelOk = true;
        _tmSel = 0;
        for (var k = 0; k < rows.Count; k++)
            if (rows[k].Id == _tmId && rows[k].Material == _tmMaterial)
                _tmSel = k;
        var steps = (int)_tmScroll;
        _tmScroll -= steps;
        _tmSel = ((_tmSel - steps) % rows.Count + rows.Count) % rows.Count;
        var row = rows[_tmSel];
        _tmId = row.Id;
        _tmMaterial = row.Material;
        _tmView = view;
        _tmDraw = true;
        _tmAt = ConstructTerminals.At(hit.Body, hit.Part) + Vector3.UnitY * 0.5f;
        var key = view.Stock.Version * 8192L + _tmSel + ((long)SandboxKeys.Stamp << 44);
        if (key != _tmKey)
        {
            _tmKey = key;
            _tmText = SandboxKeys.Line(hit.Body.DefOf(hit.Part).Name, $"{view.Stock.Stores} stores, {row.Text}", null, $"Wheel: pick - {SandboxKeys.Use}: to hands - {SandboxKeys.Reload}: to chute");
        }
        g.SandboxLookText = _tmText;
        if (Pressed(GameAction.Interact))
            g.SandboxTerminalPull(hit.Body.Id, hit.Part, row, hands: true);
        else if (Pressed(GameAction.Reload) && h.Held == ConstructParts.None)
            g.SandboxTerminalPull(hit.Body.Id, hit.Part, row, hands: false);
        return true;
    }

    private static void TerminalIdleKeepScroll(bool keep)
    {
        _tmWheelOk = _tmDraw = false;
        _tmView = null;
        if (!keep)
            _tmScroll = 0;
    }

    /// <summary>The ImGui pass: the rows around the picked one, standing by the terminal. Input found them; nothing here touches the construct.</summary>
    private static void DrawTerminalInMatch()
    {
        if (!_tmDraw || _tmView is not { } view || !Hud.CanDraw())
            return;
        var cam = Hud.Camera;
        if (!cam.Valid)
            return;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        if (!Hud.Project(cam.ViewProj, _tmAt, area.X, area.Y, out var s, out _))
            return;
        var rows = view.Rows;
        var px = ImGui.GetFontSize();
        var shown = Math.Min(TerminalRowsShown, rows.Count);
        var top = Math.Clamp(_tmSel - shown / 2, 0, Math.Max(0, rows.Count - shown));
        var list = ImGui.GetBackgroundDrawList();
        var x = org.X + Math.Clamp(s.X + 24f * Ui.Scale, 8f, MathF.Max(8f, area.X - 260f * Ui.Scale));
        var y = org.Y + Math.Clamp(s.Y - shown * px * 0.6f, 8f, MathF.Max(8f, area.Y - shown * px * 1.2f - 8f));
        for (var k = 0; k < shown; k++)
        {
            var i = top + k;
            if (i >= rows.Count)
                break;
            var picked = i == _tmSel;
            Hud.ShadowText(list, new float2(x, y + k * px * 1.2f), px, (picked ? "> " : "  ") + rows[i].Text, picked ? new Vector3(0.4f, 1f, 0.5f) : new Vector3(0.82f, 0.88f, 0.92f), 1f);
        }
    }
}
