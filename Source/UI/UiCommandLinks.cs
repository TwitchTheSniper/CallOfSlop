using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view's logistics layer (docs/space/COMMAND.md "The logistics layer"; the user, 2026-11-16): the linked logistics networks
/// (docs/sandbox/design/linked-logistics.md) over the map, a layer the LOGISTICS button turns on and off. Only what is linked now is
/// drawn: a factory (a body) with a link end that a partner answers, a line to each partner (to its body in this world, or towards its
/// world for a Relay's link across the system), and a route ship's leg to the stop it flies to. Unpaired ends, ends nobody answers and
/// bodies of other players outside the local player's faction are not drawn. Everything comes from the parts' own state
/// (<see cref="LinkPart"/>: the host's own, a client's from CBodyKind.Link with <see cref="LinkFar"/>), gathered four times a second
/// into fixed arrays; a frame only projects and draws. Off, it costs one bool check a frame.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Is the logistics layer shown (kept while the view is closed and opened again).</summary>
    public bool CommandLinksOn;

    public const int CmdMaxLinkNodes = 64, CmdMaxLinkEdges = 128, CmdMaxLinkLegs = 32;

    /// <summary>A factory of a linked network: one body with at least one end a partner answers.</summary>
    private struct CmdLinkNode
    {
        public ushort Body;
        public byte Ends, Linked; // its link ends that are linked now, and the partners answering them
        public bool Relay, Dock, Waiting;
        public SpacePos Pos;
        public float2 At;
        public bool Seen; // its position is known this frame
        public CmdLabel Name, Sub;
    }

    /// <summary>A link between two ends: node A to node B (both here), to a body here that isn't a node, or towards another world.</summary>
    private struct CmdLinkEdge
    {
        public short A, B; // B -1: not a node
        public ushort Body; // B -1, same world: the partner's body (0: not seen here)
        public int Planet; // another world: its body in the star system (-1: none)
        public bool Asleep;
        public CmdLabel Label;
    }

    /// <summary>A route ship's leg: from the ship to the stop it flies to.</summary>
    private struct CmdLinkLeg
    {
        public ushort Ship, Body; // the stop's body when it is in this world (0: another world)
        public int Planet;
        public CmdLabel Label;
    }

    private readonly CmdLinkNode[] _cmdLinkNodes = new CmdLinkNode[CmdMaxLinkNodes];
    private readonly CmdLinkEdge[] _cmdLinkEdges = new CmdLinkEdge[CmdMaxLinkEdges];
    private readonly CmdLinkLeg[] _cmdLinkLegs = new CmdLinkLeg[CmdMaxLinkLegs];
    private int _cmdLinkNodeN, _cmdLinkEdgeN, _cmdLinkLegN;
    private double _cmdLinksAt = -1;
    private CmdLabel _cmdLinksNone;

    /// <summary>What the layer holds now (the window tool's <c>linked layer</c>): factories, links, route legs.</summary>
    public (int Nodes, int Edges, int Legs) CommandLinkCounts => (_cmdLinkNodeN, _cmdLinkEdgeN, _cmdLinkLegN);

    /// <summary>The name and second line of the layer's n-th factory (the window tool's print).</summary>
    public string CommandLinkNode(int n) => n < _cmdLinkNodeN ? $"{_cmdLinkNodes[n].Name.Text} [{_cmdLinkNodes[n].Sub.Text}] body {_cmdLinkNodes[n].Body}" : "";

    /// <summary>May the local player see this body's links: their own, a faction mate's, or nobody's (the map's).</summary>
    private static bool CmdLinkVisible(Game g, Construct c) =>
        c.Owner == 255 || g.Local is { } me && Factions.Shares(c.Owner, me.Id);

    /// <summary>The layer's factories, links and legs from the parts' state (four times a second; strings only when a label changes).</summary>
    private void CmdLinksGather(Game g, StarSystem sys, double now)
    {
        if (now - _cmdLinksAt < 0.25 && _cmdLinksAt >= 0)
            return;
        _cmdLinksAt = now;
        _cmdLinkNodeN = _cmdLinkEdgeN = _cmdLinkLegN = 0;
        var here = LinkedLogistics.WorldKey();
        foreach (var c in g.Constructs.Bodies)
        {
            if (LinkedLogistics.PartCount(c) == 0 || c.Wreck || !CmdLinkVisible(g, c))
                continue;
            var node = -1;
            long key = c.Id;
            for (var i = 0; i < c.PartCount; i++)
            {
                if (c.Parts[i].Link is not { } lp)
                    continue;
                var d = c.DefOf(i);
                if (d.LinkKind == LinkKind.Route)
                {
                    CmdLinkRouteLeg(c, lp, sys, here);
                    continue;
                }
                if (!d.IsLinkEnd || lp.Linked == 0 || lp.Far.Length == 0)
                    continue;
                if (node < 0)
                {
                    if (_cmdLinkNodeN >= CmdMaxLinkNodes)
                        break;
                    node = _cmdLinkNodeN++;
                    ref var fresh = ref _cmdLinkNodes[node];
                    (fresh.Body, fresh.Ends, fresh.Linked, fresh.Relay, fresh.Dock, fresh.Waiting) = (c.Id, 0, 0, false, false, false);
                }
                ref var n = ref _cmdLinkNodes[node];
                n.Ends++;
                n.Linked = (byte)Math.Min(255, n.Linked + lp.Linked);
                n.Relay |= d.LinkKind == LinkKind.Relay;
                n.Waiting |= lp.Waiting;
                key = key * 31 + lp.Version;
                for (var k = 0; k < lp.Far.Length && _cmdLinkEdgeN < CmdMaxLinkEdges; k++)
                    CmdLinkEdgeAdd(g, sys, here, (short)node, lp.Far[k]);
            }
            if (node < 0)
                continue;
            ref var nd = ref _cmdLinkNodes[node];
            for (var i = 0; i < c.PartCount && !nd.Dock; i++)
                nd.Dock = c.DefOf(i).LinkKind == LinkKind.Dock;
            key = key * 4 + (nd.Dock ? 1 : 0) + (nd.Waiting ? 2 : 0);
            if (nd.Name.Stale(key))
            {
                var first = -1;
                for (var i = 0; i < c.PartCount && first < 0; i++)
                    if (c.Parts[i].Link is { Linked: > 0 } && c.DefOf(i).IsLinkEnd)
                        first = i;
                var fl = c.Parts[first].Link!;
                var code = LinkedLogistics.Code(c.DefOf(first).LinkKind == LinkKind.Receiver ? fl.Pair : fl.Id);
                nd.Name.Text = LinkedLogistics.NameOf(c, first).ToUpperInvariant();
                nd.Sub.Text = $"{code}  //  {(nd.Relay ? "RELAY" : "LINK")}  {nd.Linked} LINKED{(nd.Dock ? "  //  DOCK" : "  //  NO DOCK")}{(nd.Waiting ? "  //  WAITING FOR A SHIP" : "")}";
            }
        }

        // A link both of whose ends are nodes is listed twice (each end names the other): keep one.
        var kept = 0;
        for (var e = 0; e < _cmdLinkEdgeN; e++)
        {
            ref var ed = ref _cmdLinkEdges[e];
            if (ed.B >= 0 && ed.B < ed.A)
            {
                var twin = false;
                for (var f = 0; f < _cmdLinkEdgeN && !twin; f++)
                    twin = _cmdLinkEdges[f].A == ed.B && _cmdLinkEdges[f].B == ed.A;
                if (twin)
                    continue;
            }
            if (kept != e)
                _cmdLinkEdges[kept] = ed;
            kept++;
        }
        _cmdLinkEdgeN = kept;
    }

    private void CmdLinkEdgeAdd(Game g, StarSystem sys, string here, short node, in LinkFar far)
    {
        ref var e = ref _cmdLinkEdges[_cmdLinkEdgeN++];
        (e.A, e.B, e.Body, e.Planet, e.Asleep) = (node, -1, 0, -1, (far.Flags & LinkFar.Asleep) != 0);
        if (far.World.Equals(here, StringComparison.OrdinalIgnoreCase))
        {
            for (var k = 0; k < CmdMaxLinkNodes && k < _cmdLinkNodeN; k++)
                if (_cmdLinkNodes[k].Body == far.Body)
                    e.B = (short)k;
            if (e.B < 0 && g.Constructs.Get(far.Body) is { } pc && CmdLinkVisible(g, pc))
                e.Body = far.Body;
            // (a partner not seen here, or another player's: a stub with its name, never its body; a body gathered later as a
            // node is joined in CmdLinks)
        }
        else
            e.Planet = CmdPlanetOf(sys, far.World);
        if (e.Label.Stale(far.Id * 4L + far.Flags + (long)far.Body * 1_000_003 + far.Name.GetHashCode()))
            e.Label.Text = far.World.Equals(here, StringComparison.OrdinalIgnoreCase)
                ? far.Name.ToUpperInvariant()
                : $"{far.Name.ToUpperInvariant()}  ({LinkLedger.Title(far.World).ToUpperInvariant()}{(e.Asleep ? ", ASLEEP" : "")})";
    }

    private void CmdLinkRouteLeg(Construct c, LinkPart lp, StarSystem sys, string here)
    {
        if (_cmdLinkLegN >= CmdMaxLinkLegs)
            return;
        for (var k = 0; k < lp.Far.Length; k++)
        {
            if ((lp.Far[k].Flags & LinkFar.Next) == 0)
                continue;
            ref var far = ref lp.Far[k];
            ref var leg = ref _cmdLinkLegs[_cmdLinkLegN++];
            var same = far.World.Equals(here, StringComparison.OrdinalIgnoreCase);
            (leg.Ship, leg.Body, leg.Planet) = (c.Id, same ? far.Body : (ushort)0, same ? -1 : CmdPlanetOf(sys, far.World));
            if (leg.Label.Stale((long)c.Id * 65536 + far.Body + (long)far.Name.GetHashCode() * 7919L))
                leg.Label.Text = "ROUTE: TO " + far.Name.ToUpperInvariant();
            return;
        }
    }

    /// <summary>The star system's body whose world (map) is this key, or -1.</summary>
    private static int CmdPlanetOf(StarSystem sys, string world)
    {
        for (var b = 0; b < sys.Count; b++)
            if (sys.Bodies[b].Map is { } m && m.Equals(world, StringComparison.OrdinalIgnoreCase))
                return b;
        return -1;
    }

    /// <summary>The layer, over the bodies and under the craft: lines first, then the factories.</summary>
    private void CmdLinks(ImDrawListPtr dl, Game g, SpaceSession s, float fade, double now)
    {
        if (!CommandLinksOn)
            return;
        var sys = s.System;
        CmdLinksGather(g, sys, now);
        // (a partner that turned out to be a node after its edge was made: joined now, at no cost when there is none)
        for (var e = 0; e < _cmdLinkEdgeN; e++)
        {
            ref var ed = ref _cmdLinkEdges[e];
            if (ed.B < 0 && ed.Body != 0)
                for (var k = 0; k < _cmdLinkNodeN; k++)
                    if (_cmdLinkNodes[k].Body == ed.Body)
                        (ed.B, ed.Body) = ((short)k, 0);
        }
        for (var k = 0; k < _cmdLinkNodeN; k++)
        {
            ref var n = ref _cmdLinkNodes[k];
            n.Seen = g.Constructs.Get(n.Body) is { } c && g.NavState(c, _cvTime, out n.Pos, out _);
            n.At = n.Seen ? CmdP(n.Pos) : default;
        }
        var reach = (_cvSize.X + _cvSize.Y) * 1.5f;
        var flow = (float)(now * 0.35 % 1.0);
        for (var e = 0; e < _cmdLinkEdgeN; e++)
        {
            ref var ed = ref _cmdLinkEdges[e];
            ref var a = ref _cmdLinkNodes[ed.A];
            if (!a.Seen)
                continue;
            float2 to;
            if (ed.B >= 0)
            {
                if (!_cmdLinkNodes[ed.B].Seen)
                    continue;
                to = _cmdLinkNodes[ed.B].At;
            }
            else if (ed.Body != 0 && g.Constructs.Get(ed.Body) is { } pc && g.NavState(pc, _cvTime, out var pp, out _))
                to = CmdP(pp);
            else if (ed.Planet >= 0)
                to = CmdP(sys.PositionOf(ed.Planet, _cvTime));
            else
                to = a.At + new float2(70, -40) * _scale; // (a partner nowhere on the map: a stub with its name)
            var d = to - a.At;
            var len = d.Length();
            if (!(len > 1f))
                continue;
            if (len > reach)
            {
                to = a.At + d * (reach / len);
                len = reach;
            }
            var colour = ed.Asleep ? CmdDim : CmdGreen;
            if (ed.B >= 0 || ed.Body != 0)
            {
                CmdGlow(dl, a.At, to, colour, 0.75f * fade, 1.3f);
                var dot = a.At + (to - a.At) * flow;
                ImDrawListExtensions.AddCircleFilled(dl, in dot, 2.2f * _scale, Hud.Colour(CmdWhite, 0.8f * fade), 8);
            }
            else
                CmdDash(dl, a.At, to, colour, 0.7f * fade, flow);
            if (ed.B < 0)
            {
                // (the far end's name, out along the line: a world away the line runs off the screen)
                var at = a.At + (to - a.At) * MathF.Min(1, 90 * _scale / len);
                CmdText(dl, at + new float2(6 * _scale, -14 * _scale), 11, colour, fade, ed.Label.Text ?? "");
            }
        }
        for (var l = 0; l < _cmdLinkLegN; l++)
        {
            ref var leg = ref _cmdLinkLegs[l];
            var ship = CmdFind(leg.Ship);
            if (ship < 0)
                continue;
            var from = _cmdCraft[ship].At;
            float2 to;
            if (leg.Body != 0 && g.Constructs.Get(leg.Body) is { } dc && g.NavState(dc, _cvTime, out var dp, out _))
                to = CmdP(dp);
            else if (leg.Planet >= 0)
                to = CmdP(sys.PositionOf(leg.Planet, _cvTime));
            else
                continue;
            var d = to - from;
            var len = d.Length();
            if (!(len > 4f))
                continue;
            if (len > reach)
                to = from + d * (reach / len);
            CmdDash(dl, from, to, CmdAmber, 0.8f * fade, flow);
            CmdRing(dl, to, 6 * _scale, CmdAmber, 0.8f * fade, 1.2f, 4, 0.5f, (float)now);
            CmdText(dl, from + new float2(10 * _scale, 10 * _scale), 10.5f, CmdAmber, fade, leg.Label.Text ?? "");
        }
        for (var k = 0; k < _cmdLinkNodeN; k++)
        {
            ref var n = ref _cmdLinkNodes[k];
            if (!n.Seen || !CmdOn(n.At))
                continue;
            var r = 5.5f * _scale;
            var tint = n.Waiting ? CmdAmber : CmdGreen;
            // A square on its corner with a dot in it: a factory (a craft is a dart or a diamond outline; a body a round dot).
            var c0 = n.At + new float2(-r, -r);
            var c1 = n.At + new float2(r, r);
            dl.AddRectFilled(c0, c1, Hud.Colour(tint, 0.22f * fade));
            dl.AddRect(c0, c1, Hud.Colour(tint, 0.95f * fade), 0, 0, 1.4f * _scale);
            ImDrawListExtensions.AddCircleFilled(dl, in n.At, 1.8f * _scale, Hud.Colour(CmdWhite, fade), 8);
            if (n.Relay)
                CmdRing(dl, n.At, r + 5 * _scale, tint, 0.6f * fade, 1f, 3, 0.6f, (float)(now * 0.7));
            if (n.Waiting)
                CmdRing(dl, n.At, r + 9 * _scale, CmdAmber, 0.5f * fade, 1f, 8, 0.5f, (float)(-now * 0.5));
            // Its name and line, unless an earlier factory's label sits right there (factories close together at a world's zoom).
            var crowded = false;
            for (var j = 0; j < k && !crowded; j++)
                crowded = _cmdLinkNodes[j].Seen && (_cmdLinkNodes[j].At - n.At).LengthSquared() < 26 * 26 * _scale * _scale;
            if (crowded)
                continue;
            CmdText(dl, n.At + new float2(r + 8 * _scale, -r - 9 * _scale), 12, CmdWhite, fade, n.Name.Text ?? "");
            CmdText(dl, n.At + new float2(r + 8 * _scale, -r + 6 * _scale), 10, tint, 0.9f * fade, n.Sub.Text ?? "");
        }
    }

    /// <summary>A dashed line, its dashes running along it (<paramref name="phase"/> 0 to 1): at most 64 dashes.</summary>
    private void CmdDash(ImDrawListPtr dl, float2 a, float2 b, Vector3 c, float alpha, float phase)
    {
        var d = b - a;
        var len = d.Length();
        if (!(len > 1f))
            return;
        var step = MathF.Max(12 * _scale, len / 64);
        var dash = step * 0.55f;
        var u = d / len;
        for (var t = (phase - 1) * step; t < len; t += step)
        {
            var t0 = MathF.Max(0, t);
            var t1 = MathF.Min(len, t + dash);
            if (t1 > t0)
                CmdLine(dl, a + u * t0, a + u * t1, c, alpha, 1.2f);
        }
    }

    /// <summary>The layer's button (in the chrome, beside the zoom presets) and its note when nothing is linked.</summary>
    private void CmdLinksButton(ImDrawListPtr dl, float2 q0, float2 size, float fade)
    {
        if (CmdButton(dl, q0, q0 + size, "LOGISTICS", CmdGreen, fade, CommandLinksOn))
        {
            CommandLinksOn = !CommandLinksOn;
            _cmdLinksAt = -1;
        }
        if (CommandLinksOn && _cmdLinkNodeN == 0 && _cmdLinkLegN == 0)
        {
            if (_cmdLinksNone.Stale(1))
                _cmdLinksNone.Text = "NO LINKED NETWORKS HERE";
            CmdText(dl, new float2(q0.X + size.X * 0.5f, q0.Y + size.Y + 7 * _scale), 10, CmdGreen, 0.8f * fade, _cmdLinksNone.Text!, 1);
        }
    }
}
