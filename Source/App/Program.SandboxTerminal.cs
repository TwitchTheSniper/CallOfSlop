namespace CallOfSlop;

/// <summary>
/// The Smart Terminal's keys (docs/sandbox/CONSTRUCTS.md "The Smart Terminal"): no window and no cursor. The list is on the
/// terminal's own screen (<see cref="ConstructTerminals.FillPanel"/>). Look at it within reach: the wheel picks, the use key
/// brings the item to the hands, reload sends it to the tray and Chute out. The pick is kept in <see cref="TerminalView"/>.
/// </summary>
public static partial class Program
{
    private static double _tmScroll;
    private static bool _tmWheelOk;
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
        _tmWheelOk = false;
        _tmScroll = 0;
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
        var steps = (int)_tmScroll;
        _tmScroll -= steps;
        var was = view.Picked;
        var picked = view.Step(-steps);
        if (picked != was)
            PanelRender.Touch++; // (the screen follows the wheel at once)
        var row = rows[picked];
        var key = view.Stock.Version * 8192L + picked + ((long)SandboxKeys.Stamp << 44);
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
        _tmWheelOk = false;
        if (!keep)
            _tmScroll = 0;
    }
}
