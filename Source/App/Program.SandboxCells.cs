namespace CallOfSlop;

public static partial class Program
{
    /// <summary>The attach tool at a holed lift cell: the look line says so, and Fire patches it (ConstructLift.cs).</summary>
    private static void SandboxCellPatch(Game g, ref SandboxHand h, bool aimed, in ConstructHit hit)
    {
        if (!aimed || h.Tool != ConstructTool.Welder || h.Held != ConstructParts.None || ConnectorModeOn(in h) || hit.RoutePlus1 != 0 || hit.Part < 0 || hit.Part >= hit.Body.PartCount)
            return;
        var d = hit.Body.DefOf(hit.Part);
        if (!d.IsEnvelope || ConstructConfig.Get(hit.Body.Parts[hit.Part], d, CfgKey.Hole, 0) <= 0)
            return;
        g.SandboxLookText = $"{d.Name}: {ConstructFluid.Problem(hit.Body, hit.Part)} - {SandboxKeys.Fire}: patch";
        if (Pressed(GameAction.Fire) && !_swallowClick)
            g.SandboxSendPatchCell(hit.Body.Id, hit.Part);
    }

    /// <summary>
    /// The attach tool at a part that is groaning or has broken down (Game/GameSandboxBreakdowns.cs): the look line says so,
    /// and Fire sees to it or repairs it. True: it is such a part, and the tool does nothing else this frame.
    /// </summary>
    private static bool SandboxBreakdownLook(Game g, ref SandboxHand h, bool aimed, in ConstructHit hit)
    {
        if (!aimed || h.Seated || h.Tool != ConstructTool.Welder || h.Held != ConstructParts.None || ConnectorModeOn(in h) || hit.RoutePlus1 != 0
            || g.BreakdownLook(hit.Body, hit.Part, out _) is not { } line)
            return false;
        g.SandboxLookText = line;
        if (Pressed(GameAction.Fire) && !_swallowClick)
            g.SandboxSendMend(hit.Body.Id, hit.Part);
        return true; // (the line stays: nothing else the attach tool does here overwrites it)
    }

    /// <summary>
    /// The attach tool at a standing Robot Shell with wear (Game/GameShellWear.cs): the look line says what is worn, and Fire fits a
    /// spare part from the inventory or services the worst work. True: the tool does nothing else this frame. A spare held in the
    /// hands is fitted too (so the tool may hold one here).
    /// </summary>
    private static bool SandboxShellWearLook(Game g, ref SandboxHand h, bool aimed, in ConstructHit hit)
    {
        if (!aimed || h.Seated || h.Tool != ConstructTool.Welder || ConnectorModeOn(in h) || hit.RoutePlus1 != 0
            || g.ShellWearLook(hit.Body, hit.Part) is not { } line)
            return false;
        if (h.Held != ConstructParts.None && !Game.IsShellSpare(h.Held))
            return false;
        g.SandboxLookText = line;
        if (Pressed(GameAction.Fire) && !_swallowClick)
            g.SandboxSendMend(hit.Body.Id, hit.Part);
        return true;
    }
}
