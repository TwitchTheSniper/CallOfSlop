namespace CallOfSlop;

/// <summary>
/// A Conveyor Router's own screen (panel source <c>router</c>; production.md "Smart logistics", phase 4): what it is doing on the top
/// line in its state's colour (sending, holding a stuck piece, idle, no power), then its last piece and where it went or the piece it
/// holds, then why (the order, storage, the stuck reason), then its two settings. Two buttons under the lines change them at the
/// screen: <b>Spill</b> steps <c>routerSpill</c> (Auto, Ahead, Left, Right) and <b>Hold</b> turns <c>routerHold</c> on and off, under
/// the configure tool's rule (CActKind.PanelTouch): a joined client's finger goes to the host, and the setting comes back with the body.
/// Everything shown is what every peer holds (the Router's shown choice and stuck piece: ConstructLogistics.Refresh); nothing here allocates.
/// </summary>
public static class RouterScreen
{
    private static readonly string[] SpillWords = ["Auto", "Ahead", "Left", "Right"];
    private static readonly string[] SpillLabels = ["Spill: Auto", "Spill: Ahead", "Spill: Left", "Spill: Right"];

    public static string SpillWord(int spill) => SpillWords[Math.Clamp(spill, 0, SpillWords.Length - 1)];

    public static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        LogicDisplays.Ink(p, d, card);
        var spill = StockTargets.Spill(c, part);
        var hold = StockTargets.Hold(c, part);
        // The buttons say what they are set to (the first two of the panel's buttons: Spill, then Hold).
        if (card.TouchLabels.Length > 0)
            card.TouchLabels[0] = SpillLabels[Math.Clamp(spill, 0, SpillLabels.Length - 1)];
        if (card.TouchLabels.Length > 1)
            card.TouchLabels[1] = hold ? "Hold: On" : "Hold: Off";
        var cols = card.Cols;
        var m = ConstructLogistics.Map(c);
        var s = m.RouterSlot(part);
        if (p.Broken || !ConstructRecycler.Powered(world, c, part))
        {
            card.State = PanelState.Fault;
            card.Add(0, p.Broken ? "Wrecked" : "No power");
            card.Add(1, "Deals in turn");
        }
        else if (s < 0)
        {
            card.State = PanelState.Idle;
            card.Add(0, "Not on a conveyor");
        }
        else if (m.ShowStuck[s])
        {
            card.State = PanelState.Fault;
            card.Add(0, "STUCK");
            card.Add(1, "Holds ").Add(1, ConstructLogisticsBoard.Names.Piece(m.ShowStuckDef[s]));
            card.Add(2, ConstructLogistics.ReasonText((LogiReason)m.ShowReason[s]));
        }
        else if (m.ShowWay[s] == 0)
        {
            card.State = PanelState.Idle;
            card.Add(0, "Waiting for pieces");
        }
        else
        {
            card.State = PanelState.Working;
            card.Add(0, "Sent ").Add(0, ConstructLogisticsBoard.Names.Piece(m.ShowDef[s]));
            card.Add(1, m.ShowWay[s] switch { 1 => "Ahead", 2 => "Left", _ => "Right" }).Add(1, " to ");
            var to = Store(c, m.ShowDest[s]);
            if (m.ShowState[s] == 2)
                card.Add(1, "storage");
            else
                card.Add(1, "order ").Add(1, m.ShowTag[s]);
            if (to >= 0)
                card.Add(2, c.DefOf(to).Name);
        }
        card.Add(3, "Spill ").Add(3, SpillWord(spill)).Add(3, hold ? ", holds" : ", no hold");
        card.Bar = -1;
        for (var l = 0; l < PanelCard.MaxLines; l++)
            card.Clip(l, cols);
    }

    /// <summary>The part a store handle belongs to (a destination's), or -1; the last one asked is kept (a screen asks each fill).</summary>
    private static int Store(Construct c, ushort handle)
    {
        if (handle == 0)
            return -1;
        if (_lastBody == c && _lastHandle == handle && _lastPart < c.PartCount && c.Parts[_lastPart].Store == handle)
            return _lastPart;
        var part = ConstructLogistics.PartOfStore(c, handle);
        (_lastBody, _lastHandle, _lastPart) = (c, handle, part);
        return part;
    }

    private static Construct? _lastBody;
    private static ushort _lastHandle;
    private static int _lastPart = -1;

    /// <summary>Touch "router.spill": the next way a piece with no order goes (Auto, Ahead, Left, Right).</summary>
    internal static string? NextSpill(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!d.IsRouter || d.CfgSlot[(int)CfgKey.RouterSpill] < 0)
            return $"the {d.Name} has no spill setting";
        ConstructConfig.Put(ref p, d, CfgKey.RouterSpill, (StockTargets.Spill(c, part) + 1) % SpillWords.Length);
        c.Wake();
        return null;
    }

    /// <summary>Touch "router.hold": a piece with no way to its order waits here, or goes to storage.</summary>
    internal static string? ToggleHold(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!d.IsRouter || d.CfgSlot[(int)CfgKey.RouterHold] < 0)
            return $"the {d.Name} has no hold setting";
        ConstructConfig.Put(ref p, d, CfgKey.RouterHold, StockTargets.Hold(c, part) ? 0 : 1);
        c.Wake();
        return null;
    }
}
