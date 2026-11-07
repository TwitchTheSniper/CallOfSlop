namespace CallOfSlop;

/// <summary>
/// The fuse tool on this PC (ConstructFuse.cs; docs/sandbox/CONSTRUCTS.md, "Fusing"). LMB fuses: the selector's selection when
/// it is on the aimed construct (the parts picked with the Selector; it stays drawn while this tool is out), else the aimed
/// part's whole rigid piece. RMB un-fuses the unit under the crosshair. The aimed unit is outlined. Each click is one request
/// to the server (ConstructEdits.Fuse), which checks the tool, the reach and the contested-construct rule.
/// </summary>
public static partial class Program
{
    private static long _fuLookKey = long.MinValue;
    private static string _fuLook = "";

    /// <summary>Every frame with the fuse tool out and the mouse captured (from SandboxInput, with the crosshair's hit).</summary>
    private static void SandboxFuseInput(Game g, Player me, bool aimed, in ConstructHit hit)
    {
        var sel = Selector.Selection;
        sel.Check(g.Constructs);
        ConstructRender.PartSel = sel.Count > 0 ? sel : null;
        var inReach = aimed && hit.Distance <= ConstructBuild.Reach && hit.RoutePlus1 == 0 && hit.Part >= 0 && hit.Part < hit.Body.PartCount;
        if (!inReach)
        {
            _fuLookKey = long.MinValue;
            return;
        }
        var c = hit.Body;
        var unit = c.UnitOf(hit.Part);
        var size = unit >= 0 ? c.Unit(unit).Count : 0;
        var useSel = ReferenceEquals(sel.Body, c) && sel.Count >= 2;
        if (unit >= 0)
        {
            ConstructRender.FuseShow = c;
            ConstructRender.FuseShowUnit = unit;
            ConstructRender.FuseShowStamp = c.RebuildStamp;
        }
        var key = ((long)c.Id << 40) ^ ((long)hit.Part << 20) ^ ((long)size << 2) ^ (useSel ? (long)sel.Count << 48 : 0) ^ c.RebuildStamp;
        if (key != _fuLookKey)
        {
            _fuLookKey = key;
            var state = size > 0 ? $"fused, {size} parts" : null;
            var fuse = useSel ? $"{SandboxKeys.Fire}: fuse the {sel.Count} selected" : $"{SandboxKeys.Fire}: fuse this piece";
            _fuLook = SandboxKeys.Line(c.DefOf(hit.Part).Name, state, null, size > 0 ? $"{fuse}, {SandboxKeys.Aim}: un-fuse" : fuse);
        }
        g.SandboxLookText = _fuLook;
        if (_swallowClick)
            return;
        if (Pressed(GameAction.Aim))
        {
            if (size == 0)
            {
                g.SandboxSay("that part isn't fused");
                return;
            }
            g.SandboxFuse(c.Id, FuseMode.Unfuse, hit.Part, default);
            return;
        }
        if (!Pressed(GameAction.Fire))
            return;
        if (useSel)
            g.SandboxFuse(c.Id, FuseMode.Parts, hit.Part, sel.Items);
        else
            g.SandboxFuse(c.Id, FuseMode.Piece, hit.Part, default);
    }

    /// <summary>Every frame (from SandboxInput): nothing is outlined when the fuse tool isn't out.</summary>
    private static void FuseIdle() => _fuLookKey = long.MinValue;
}
