using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The aim highlight's side of board item dev-sandbox-highlight-relevant-hr01 (Source/Constructs/SandboxAim.cs holds the
/// predicate): builds the target from the crosshair's hit and this PC's tool modes, asks <see cref="SandboxAim.CanAct"/>
/// once a frame, and sets the outline (yellow, or red with the reason when the tool would be refused). The contested
/// reason is made again only every quarter second (it builds a string).
/// </summary>
public static partial class Program
{
    private static ushort _aimContestBody = ushort.MaxValue;
    private static long _aimContestBucket = -1;
    private static string? _aimContestWhy;

    /// <summary>Decision 19 for the local player on <paramref name="c"/> (survival only), remade at most every 250 ms.</summary>
    private static string? AimContested(Game g, Construct c, byte player)
    {
        if (g.SandboxInfiniteParts || c.Owner == 255 || c.Owner == player || c.Wreck)
            return null;
        var bucket = Environment.TickCount64 >> 8;
        if (c.Id != _aimContestBody || bucket != _aimContestBucket)
        {
            _aimContestBody = c.Id;
            _aimContestBucket = bucket;
            _aimContestWhy = ConstructGrab.Defended(c, player, g.SandboxAliveAt, g.SandboxNameOf);
        }
        return _aimContestWhy;
    }

    /// <summary>The held tool's call on the crosshair's part (nothing when the tool is the physics gun: its own look runs in PhysGunInput).</summary>
    private static AimCall SandboxAimCall(Game g, Player me, in SandboxHand h, in ConstructHit hit)
    {
        if (h.Tool == ConstructTool.PhysicsGun)
            return AimCall.No;
        var c = hit.Body;
        var t = new SandboxAim.Target
        {
            World = g.Constructs,
            Body = c,
            Part = hit.Part,
            Distance = hit.Distance,
            SlotPlus1 = hit.SlotPlus1,
            Player = me.Id,
            Seated = h.Seated,
            Contested = h.Tool is ConstructTool.None ? null : AimContested(g, c, me.Id),
            ConfigWhy = h.Tool == ConstructTool.Configure ? g.SandboxConfigRefusal(c, me.Id) : null,
            LinkMode = _linkWheel.Mode,
            Routing = _lkBody != 0,
            PaintMode = _paintWheel.Mode,
            Freeform = _weldModes.Freeform,
            OnSeam = h.Tool == ConstructTool.Welder && hit.Distance <= ConstructBuild.Reach && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0
                     && c.FreeformCount > 0 && Freeform.SeamAt(c, hit.Part, hit.Point) >= 0,
        };
        return SandboxAim.CanAct(h.Tool, in t);
    }
}
