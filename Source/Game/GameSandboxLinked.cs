using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Sandbox: linked logistics on the Game's side (docs/sandbox/design/linked-logistics.md): a player's action on a link part
/// (<see cref="CActKind.Link"/>: a name, a code, a route from the command view, a Dispatcher's rules), and the space legs of routes (a
/// Route Computer whose next stop is on another world, or that is out in space: the command view's Orbit order to a low orbit there,
/// where the stop is settled on the ledger: LinkedLogisticsRoutes.cs).
/// </summary>
public sealed partial class Game
{
    /// <summary>Server: CActKind.Link. Owner rules as the configure tool's; a route is set from anywhere (the command view).</summary>
    private string? SandboxLinkAct(ref PacketReader r, Player p)
    {
        var body = r.U16();
        var part = r.U16();
        var op = (LinkOp)r.U8();
        var a = r.U32();
        var b = r.U32();
        var text = r.Text(64);
        if (op > LinkOp.CancelLine)
            return "a bad action";
        if (Constructs.Get(body) is not { } c)
            return "that construct is gone";
        if (SandboxConfigRefusal(c, p.Id) is { } why)
            return why;
        var said = LinkedLogistics.Act(Constructs, c, part, op, a, b, text);
        if (said == null)
            Constructs.LinkChanged(c);
        return said;
    }

    /// <summary>Client (or the host itself): an action on a link part. The server says why not.</summary>
    public void SandboxLink(ushort body, int part, LinkOp op, uint a = 0, uint b = 0, string text = "")
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Link).U16(body).U16((ushort)part).U8((byte)op).U32(a).U32(b).Str(text);
        SandboxCraftSend(w);
    }

    private int _linkTick;

    /// <summary>
    /// Host, every tick with the nav orders (once a second does the work): a route ship bound for another world, or out in space, flies
    /// an Orbit order to a low orbit round its stop's world; on the circle its route settles the stop; its order ends when it leaves for
    /// a stop it can fly to. Counts against the nav orders only while it flies such a leg.
    /// </summary>
    private void ServerTickLinkRoutes(SpaceSession s)
    {
        if (++_linkTick < World.TickRate)
            return;
        _linkTick = 0;
        foreach (var (bid, part) in Constructs.LinkState.Parts)
        {
            if (Constructs.Get(bid) is not { } c || part >= c.PartCount || c.DefOf(part).LinkKind != LinkKind.Route || c.Parts[part].Link?.Route is not { } r)
                continue;
            var o = NavOf(c.Id);
            var wants = r is { Suspended: false, Phase: RoutePhase.Orbit or RoutePhase.Settling } && r.N > 0;
            if (!wants)
            {
                if (o is { Kind: NavKind.Orbit, By: 255 })
                    NavEnd(o, c, "its route's leg through space is over");
                r.OrderSet = false;
                continue;
            }
            var stop = r.Stops[Math.Min(r.At, r.N - 1)];
            var sys = c.SpaceAway != 0 ? Galaxy.System(c.SpaceAway - 1) : s.System;
            var target = -1;
            for (var k = 0; k < sys.Count && target < 0; k++)
                if (sys.Bodies[k].Map is { } m && m.Equals(stop.World, StringComparison.OrdinalIgnoreCase))
                    target = k;
            if (target < 0)
            {
                r.Why = $"{LinkLedger.Title(stop.World)} isn't in this star system: the route waits";
                continue;
            }
            if (o == null && r.OrderSet && Constructs.HumanDriver(c))
            {
                // (its order ended because its pilot took the controls, GameNav: hands on the controls win; RESUME takes it up again)
                r.OrderSet = false;
                r.Suspended = true;
                r.Why = "suspended: a pilot took the controls - RESUME on its card";
                Console.WriteLine($"[linked] {c.DisplayName()}'s route suspended: a pilot took the controls");
                continue;
            }
            if (o == null || o.Kind != NavKind.Orbit || o.Body != target)
            {
                // (a new leg goes through the command view's own path: every trip state reset, so a hand-over or a trip between planets plans afresh)
                if (NavApply(s, c, NavKind.Orbit, target, default, NavMinOrbit(sys, target), 255, $"{c.DisplayName()}'s route", null, out o) is { } why)
                {
                    r.Why = $"through space to {stop.Name}: {why}";
                    continue;
                }
                r.OrderSet = true;
                Console.WriteLine($"[linked] {c.DisplayName()}: through space to {sys.Bodies[target].Name} for {stop.Name}");
            }
            r.InOrbit = o!.Phase == NavPhase.Orbiting || o.Phase == NavPhase.Insert && o.Distance < 0.05 * o.Radius;
            if (r.Phase == RoutePhase.Orbit)
                r.Why = o.Phase switch
                {
                    NavPhase.NoDrive => "stalled: nothing it has can push it (fuel, power)",
                    NavPhase.Short => FormattableString.Invariant($"no trip in reach: it needs about {o.DvNeed:0} m/s of dV"),
                    NavPhase.Wait => "in orbit, waiting for its launch window",
                    NavPhase.Climb => $"climbing out for {LinkLedger.Title(stop.World)}",
                    NavPhase.Depart or NavPhase.Coast or NavPhase.Correct or NavPhase.Brake => $"between planets ({o.Phase.ToString().ToLowerInvariant()}) to {LinkLedger.Title(stop.World)}",
                    _ => $"through space to {LinkLedger.Title(stop.World)} ({stop.Name})",
                };
        }
    }
}
