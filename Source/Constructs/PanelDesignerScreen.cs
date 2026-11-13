namespace CallOfSlop;

/// <summary>
/// The Panel Designer's own screen (docs/sandbox/constructs/production.md "Panel Designer"). The desk is a Crafting Terminal with
/// <c>designer</c> set and its part file names the terminal's panel source (<c>craft</c>); that source comes here first, so the desk
/// says what it is and that F opens the designer, rather than reading as a terminal ("order a part"). Either peer; allocates nothing.
/// </summary>
public static class PanelDesignerScreen
{
    /// <summary>The <c>craft</c> panel source: a Panel Designer's screen, else the Crafting Terminal's (ConstructCrafting.FillPanel).</summary>
    public static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        var d = c.DefOf(part);
        if (!d.IsPanelDesigner)
        {
            ConstructCrafting.FillPanel(world, c, part, panel, card);
            return;
        }
        if (world.StoreOf(c, part) is not { } st)
            return;
        if (!ConstructRecycler.Powered(world, c, part))
        {
            card.State = PanelState.Fault;
            card.Add(0, d.Name);
            card.Add(1, c.Parts[part].Broken ? "Wrecked" : "No power");
            return;
        }
        var q = c.Parts[part].Craft;
        card.State = q is { Count: > 0 } ? PanelState.Working : PanelState.Ok;
        card.Add(0, "PANEL DESIGNER");
        card.Add(2, "Use it to design");
        card.Add(3, "a body panel");
        card.Add(5, "Orders: ").Add(5, q?.Count ?? 0);
        card.Add(6, "Tray: ").Add(6, st.ItemCount).Add(6, st.ItemCount == 1 ? " panel" : " panels");
    }
}
