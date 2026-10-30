using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// World links and hoses in the game (board item wl01; docs/sandbox/CONSTRUCTS.md, "World links", "Hoses"): the
/// linking tool's world links (CAct.WorldRoute, CAct.WorldRouteEdit), F on a hose part (taking, plugging, unplugging,
/// hanging a hose back; the Drop key lets go), the list sent to every client when it changes (CBodyKind.WorldLinks) and
/// to a joiner, and the hints (a link tore off, a hose popped out) for the players near.
/// </summary>
public sealed partial class Game
{
    public const float SandboxHintReach = 25f; // a hint goes to the players this close to where it happened

    /// <summary>Server: a world link action from a player. Null, or why it was refused.</summary>
    private string? SandboxWorldLinkAct(Player p, CActKind kind, ref PacketReader r)
    {
        if (kind == CActKind.WorldRouteEdit)
        {
            var op = r.U8();
            var link = r.U8();
            var fromBody = r.U16();
            var from = r.U8();
            var toBody = r.U16();
            var to = r.U8();
            return op != (byte)RouteEditOp.Delete ? "a world link can only be deleted" : SandboxHands.WorldRouteDelete(Constructs, p.Id, p.Eye, link, fromBody, from, toBody, to);
        }
        var ts = r.U8();
        var fb = r.U16();
        var fp = r.U8();
        var fport = r.U8();
        var tb = r.U16();
        var tp = r.U8();
        var tport = r.U8();
        int count = r.U8();
        if (count > Construct.MaxNodesPerRoute)
            return "too many nodes";
        Span<WNode> nodes = stackalloc WNode[Construct.MaxNodesPerRoute];
        for (var i = 0; i < count; i++)
            nodes[i] = new WNode { Body = r.U16(), Part = r.U8(), Pos = r.Vec() };
        return SandboxHands.WorldRoute(Constructs, World.Query, p.Id, p.Eye, (RouteType)(ts & 15), (byte)(ts >> 4), fb, fp, fport, nodes[..count], tb, tp, tport);
    }

    /// <summary>Server: F on a part: a hose part's own action (true), else not handled here (false: the ordinary Use).</summary>
    private bool SandboxHoseUse(Player p, ushort body, byte part, out string? why)
    {
        why = null;
        if (Constructs.Get(body) is not { } c || part >= c.PartCount || c.DefOf(part).Role != PartRole.Hose)
            return false;
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Seated)
        {
            why = "get out of the seat first";
            return true;
        }
        if (Vector3.Distance(p.Eye, ConstructWorld.HosePoint(c, part)) > ConstructBuild.UseReach + 0.5f)
        {
            why = "too far away";
            return true;
        }
        why = Constructs.HoseUse(p.Id, c, part, h.Held == ConstructParts.None);
        return true;
    }

    /// <summary>Server: where a player's hands are (a held hose's receiver; null: not alive).</summary>
    private Vector3? SandboxHandOf(byte id) => Players.TryGetValue(id, out var pl) && pl.Alive ? ConstructWorld.HandPoint(pl.Eye, pl.Yaw) : null;

    /// <summary>Server, after the structures: the world links and hoses when they changed; the hints as notices for the players near.</summary>
    private void SandboxWorldLinksFlush()
    {
        if (Constructs.WorldLinksDirty)
        {
            Constructs.WorldLinksDirty = false;
            ConstructNet.WriteWorldLinks(SbW, Constructs);
            Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
        }
        foreach (var (at, text) in Constructs.Hints)
            foreach (var pl in Players.Values)
                if (pl.Alive && Vector3.Distance(pl.Position, at) <= SandboxHintReach)
                    SandboxHands.Notices.Add((pl.Id, text));
        Constructs.Hints.Clear();
    }

    /// <summary>Client (or host): ask for a world link the linking tool laid out (the server checks it all again).</summary>
    public void SandboxSendWorldRoute(RouteType type, byte style, ushort fromBody, int from, byte fromPort, ReadOnlySpan<WNode> nodes, ushort toBody, int to, byte toPort)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.WorldRoute).U8((byte)((byte)type | style << 4)).U16(fromBody).U8((byte)from).U8(fromPort).U16(toBody).U8((byte)to).U8(toPort).U8((byte)nodes.Length);
        foreach (var n in nodes)
            w.U16(n.Body).U8(n.Part).Vec(n.Pos);
        SandboxSendBuilt(w);
    }

    /// <summary>Client (or host): delete world link <paramref name="link"/> (its ends as this PC has them).</summary>
    public void SandboxSendWorldRouteDelete(int link)
    {
        if (link < 0 || link >= Constructs.WorldLinkCount)
            return;
        ref var l = ref Constructs.WorldLinks[link];
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.WorldRouteEdit).U8((byte)RouteEditOp.Delete).U8((byte)link).U16(l.BodyFrom).U8(l.From).U16(l.BodyTo).U8(l.To);
        SandboxSendBuilt(w);
    }
}
