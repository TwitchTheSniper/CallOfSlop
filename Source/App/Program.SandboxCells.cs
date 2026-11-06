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
}
