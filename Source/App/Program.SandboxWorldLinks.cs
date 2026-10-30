using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The linking tool's world links and the hoses on this PC (board item wl01; docs/sandbox/CONSTRUCTS.md, "World links",
/// "Hoses"). While routing a link from a construct's output, a click on the ground, a wall or another construct turns it
/// into a world link: from then on each LMB places a node on whatever is under the crosshair (the terrain and static map
/// geometry, or any construct's part), snapped off the surface by the link's radius and to its angle rule; a red node
/// moves to the nearest valid spot along its surface normal if there is one (the shared adjust search); LMB on a matching
/// input of any construct finishes it (CAct.WorldRoute; the server checks it all again with the same rules). RMB or
/// Backspace takes the last node off, Esc cancels. The delete mode deletes a world link. Hoses: the look line on a reel or
/// an inlet says what F does there; while a receiver is held the HUD says how much hose is out.
/// </summary>
public static partial class Program
{
    private static bool _lkWorld; // the link being routed has left its construct: a world link
    private static readonly WNode[] _lkWNodes = new WNode[Construct.MaxNodesPerRoute];
    private static int _lkWCount;
    private static readonly Vector3[] _lkWPts = new Vector3[Construct.MaxNodesPerRoute + 3];
    private static readonly ConstructRoutes.WHost[] _lkWHosts = new ConstructRoutes.WHost[Construct.MaxNodesPerRoute + 3];
    private static int _lkWKey;
    private static string? _lkWWhy;
    private static long _hoseLookKey = long.MinValue;
    private static string _hoseLook = "";

    /// <summary>Can the link being routed become a world link? Null, or why not.</summary>
    private static string? LinkWorldRefusal() =>
        _lkParent != 255 ? "a branch stays on its construct" : _lkType == RouteType.Shaft ? "a drive shaft stays on one construct: torque doesn't cross between bodies" : null;

    /// <summary>The link being routed leaves its construct: its nodes so far become world link nodes on it.</summary>
    private static void LinkGoWorld(Construct rc)
    {
        _lkWorld = true;
        _lkWCount = 0;
        for (var i = 0; i < _lkCount; i++)
            _lkWNodes[_lkWCount++] = new WNode { Body = rc.Id, Part = _lkNodes[i].Host, Pos = _lkNodes[i].Pos };
        _lkWKey = 0;
    }

    /// <summary>The ground or a wall under the crosshair within reach (static world), nearer than a construct hit.</summary>
    private static bool WorldAim(Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, out RayHit wh)
    {
        var reach = ConstructBuild.Reach + 1f;
        return World.Query.Raycast(eye, dir, reach, QueryFilter.StaticOnly, out wh) && (!aimed || wh.Distance < hit.Distance);
    }

    /// <summary>A frame of routing a world link (the linking tool, LMB/RMB as for an ordinary link).</summary>
    private static void LinkWorldFrame(Game g, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, bool fire)
    {
        var w = g.Constructs;
        if (w.Get(_lkBody) is not { } cf || _lkFrom < 0 || _lkFrom >= cf.PartCount)
        {
            LinkReset();
            return;
        }
        _lkFree = Down(GameAction.FreeRouting);
        // The path so far, in the world.
        var n0 = 0;
        _lkWPts[n0] = ConstructWorld.PortWorld(cf, _lkFrom, _lkFromPort);
        _lkWHosts[n0++] = new ConstructRoutes.WHost { C = cf, Part = _lkFrom };
        for (var i = 0; i < _lkWCount; i++)
        {
            if (!w.NodeWorld(_lkWNodes[i], out _lkWPts[n0]))
            {
                LinkReset();
                return;
            }
            _lkWHosts[n0++] = new ConstructRoutes.WHost { C = _lkWNodes[i].Body != 0 ? w.Get(_lkWNodes[i].Body) : null, Part = _lkWNodes[i].Body != 0 ? _lkWNodes[i].Part : -1 };
        }
        var at = _lkWPts[n0 - 1];
        var tailDir = n0 > 1 && Vector3.DistanceSquared(at, _lkWPts[n0 - 2]) > 1e-8f ? Vector3.Normalize(at - _lkWPts[n0 - 2]) : Vector3.Zero;
        for (var i = 0; i < n0; i++)
            WorldLinkRender.PreviewPts[i] = _lkWPts[i];
        // The candidate: the input it ends at, or the next node (on a construct's part, or on the world).
        var onWorld = WorldAim(eye, dir, aimed, hit, out var wh);
        var onPart = aimed && !onWorld;
        Construct? endC = null;
        var endPart = -1;
        Vector3 next = default, nrm = Vector3.UnitY;
        var host = new ConstructRoutes.WHost { Part = -1 };
        var have = false;
        if (onPart && ConstructRoutes.WorldClickEnds(cf, _lkFrom, hit.Body, hit.Part, _lkType, _lkFree))
        {
            endC = hit.Body;
            endPart = hit.Part;
            next = ConstructWorld.PortWorld(endC, endPart, 255);
            have = true;
        }
        else if (onPart)
        {
            if (_lkFree)
                ConstructRender.AimPart = -1;
            nrm = hit.Normal;
            next = ConstructRoutes.WorldSnap(null, _lkType, _lkStyle, at, tailDir, hit.Point, hit.Normal);
            host = new ConstructRoutes.WHost { C = hit.Body, Part = hit.Part };
            have = true;
        }
        else if (onWorld)
        {
            nrm = wh.Normal;
            next = ConstructRoutes.WorldSnap(World.Query, _lkType, _lkStyle, at, tailDir, eye + dir * wh.Distance, wh.Normal);
            have = true;
        }
        var rule = new ConstructRoutes.WorldOpenRule { World = w, Query = World.Query, Type = _lkType, Style = _lkStyle, Pts = _lkWPts, Hosts = _lkWHosts, Count = n0, Host = host };
        if (have)
        {
            var key = HashCode.Combine(next, endPart, endC?.Id ?? 0, host.C?.Id ?? 0, host.Part, _lkWCount, w.WorldLinkCount);
            if (key != _lkWKey)
            {
                _lkWKey = key;
                if (endC != null)
                {
                    Span<WNode> nodes = _lkWNodes.AsSpan(0, _lkWCount);
                    _lkWWhy = w.CheckWorldLink(World.Query, _lkType, _lkStyle, cf, _lkFrom, _lkFromPort, nodes, endC, endPart, 255);
                }
                else
                    _lkWWhy = rule.Why(next);
            }
            _lkWhy = _lkWWhy;
            WorldLinkRender.PreviewPts[n0] = next;
            WorldLinkRender.PreviewCount = n0 + 1;
        }
        else
        {
            _lkWhy = "point at the ground, a wall or a construct for a node, or at an input";
            WorldLinkRender.PreviewCount = n0;
        }
        WorldLinkRender.PreviewOk = _lkWhy == null;
        WorldLinkRender.PreviewType = _lkType;
        WorldLinkRender.PreviewStyle = _lkStyle;
        if (fire && have)
        {
            if (endC != null)
            {
                if (_lkWhy != null)
                    g.SandboxSay(_lkWhy);
                else
                {
                    g.SandboxSendWorldRoute(_lkType, _lkStyle, cf.Id, _lkFrom, _lkFromPort, _lkWNodes.AsSpan(0, _lkWCount), endC.Id, endPart, 255);
                    LinkReset();
                    return;
                }
            }
            else if (_lkWCount >= Construct.MaxNodesPerRoute)
                g.SandboxSay($"a link has at most {Construct.MaxNodesPerRoute} nodes");
            else
            {
                if (_lkWhy != null && ConstructRoutes.AdjustSearch(_lkType, _lkStyle, next, nrm, ref rule) is { } cm)
                {
                    next = ConstructRoutes.AdjustPos(next, nrm, cm); // (the nearest valid spot along the surface normal)
                    _lkWhy = null;
                }
                if (_lkWhy != null)
                    g.SandboxSay(_lkWhy);
                else if (host.C is { } hc)
                {
                    hc.WorldFrame(hc.Parts[host.Part].Seg, out var q, out var t);
                    _lkWNodes[_lkWCount++] = new WNode { Body = hc.Id, Part = (byte)host.Part, Pos = ConstructWorld.Quantise(Vector3.Transform(next - t, Quaternion.Conjugate(q))) };
                }
                else
                    _lkWNodes[_lkWCount++] = new WNode { Pos = ConstructWorld.Quantise(next) };
            }
        }
        if (Pressed(GameAction.Aim))
        {
            if (_lkWCount > 0)
                _lkWCount--;
            else
                LinkReset();
        }
        _lkCount = _lkWCount; // (the HUD counts the nodes)
        LinkHud(g);
    }

    /// <summary>The world link under the crosshair (nearer than what else is aimed at), or -1; its look line, and the delete mode.</summary>
    private static int LinkWorldAimed(Game g, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, bool fire, LinkMode mode)
    {
        var best = aimed ? hit.Distance : ConstructBuild.Reach + 1f;
        if (!g.Constructs.RayWorldLinks(eye, dir, ref best, out var link, out _, out _))
            return WorldLinkRender.AimLink = -1;
        WorldLinkRender.AimLink = link;
        ref var l = ref g.Constructs.WorldLinks[link];
        var key = HashCode.Combine(link, l.BodyFrom, l.BodyTo, (int)mode);
        if (key != _lkLookKey)
        {
            _lkLookKey = key;
            var fc = g.Constructs.Get(l.BodyFrom);
            var tc = g.Constructs.Get(l.BodyTo);
            var name = ConstructRoutes.Name(l.Type, l.Style);
            _lkLook = $"World {name}: {(fc != null && l.From < fc.PartCount ? fc.DefOf(l.From).Name : "?")} -> {(tc != null && l.To < tc.PartCount ? tc.DefOf(l.To).Name : "?")}{(mode == LinkMode.Delete ? " - LMB to delete" : "")}";
        }
        g.SandboxLookText = _lkLook;
        if (fire && mode == LinkMode.Delete)
            g.SandboxSendWorldRouteDelete(link);
        return link;
    }

    /// <summary>Where a player's hands are on this PC (the hose to a held receiver is drawn to them).</summary>
    private static Vector3? HoseHandOf(byte id)
    {
        if (_game is not { } g || !g.Players.TryGetValue(id, out var p) || !p.Alive)
            return null;
        return ConstructWorld.HandPoint(p.RenderEye, id == g.LocalId ? g.LookYaw : p.Yaw);
    }

    /// <summary>The look line on a hose part, and the HUD line while a receiver is held (remade only when it changes).</summary>
    private static void HoseLook(Game g, Player me, bool aimed, in ConstructHit hit, bool near)
    {
        var w = g.Constructs;
        var held = w.HoseHeldBy(me.Id);
        var onHose = aimed && hit.Body.DefOf(hit.Part).Role == PartRole.Hose;
        if (!onHose && held < 0)
            return;
        float outM = 0, length = 0;
        HoseCarry carry = default;
        if (held >= 0 && w.Get(w.Hoses[held].BodyOut) is { } rc && w.Hoses[held].PartOut < rc.PartCount)
        {
            var rd = rc.DefOf(w.Hoses[held].PartOut);
            length = rd.HoseLength;
            carry = rd.HoseCarry;
            outM = Vector3.Distance(ConstructWorld.HosePoint(rc, w.Hoses[held].PartOut), ConstructWorld.HandPoint(me.Eye, g.LookYaw));
        }
        var here = onHose ? w.HoseAt(hit.Body, hit.Part) : -1;
        var key = (onHose ? (long)hit.Body.Id << 40 | (long)hit.Part << 32 : 0) ^ (long)(held + 1) << 24 ^ (long)(here + 1) << 16 ^ (long)(int)(outM * 10) ^ (near ? 1L << 62 : 0)
                  ^ (here >= 0 && w.Hoses[here].Plugged ? 1L << 61 : 0) ^ (long)_settings.Bind(GameAction.DropItem) << 48;
        if (key != _hoseLookKey)
        {
            _hoseLookKey = key;
            var drop = Keybinds.NameOf(_settings, GameAction.DropItem);
            if (onHose)
            {
                var d = hit.Body.DefOf(hit.Part);
                var thing = d.HoseCarry is HoseCarry.Fuel or HoseCarry.Water ? "nozzle" : "plug";
                var hoseName = ConstructWorld.HoseName(d.HoseCarry);
                _hoseLook = d.HoseEnd == HoseEnd.Outlet
                    ? here >= 0
                        ? held == here ? $"{d.Name}: F to hang the {hoseName} back" : w.Hoses[here].Plugged ? $"{d.Name}: plugged in elsewhere" : $"{d.Name}: someone has the {thing}"
                        : $"{d.Name} ({d.HoseLength:0} m{(d.HoseFlow > 0 && d.HoseCarry != HoseCarry.Data ? $", {d.HoseFlow:0.#} {(d.HoseCarry == HoseCarry.Power ? "kW" : "l/s")}" : "")}){(near ? $": F to take the {thing}" : "")}"
                    : here >= 0 && w.Hoses[here].Plugged
                        ? $"{d.Name}: {hoseName} plugged in{(near ? $" - F to take the {thing} out" : "")}"
                        : held >= 0 ? $"{d.Name}: {(carry == d.HoseCarry ? near ? "F to plug it in" : "come closer to plug it in" : $"a {ConstructWorld.HoseName(carry)} doesn't fit it")}" : $"{d.Name}: empty";
            }
            else
                _hoseLook = $"Holding the {ConstructWorld.HoseName(carry)}: {outM:0.0} of {length:0} m out. F on a matching inlet plugs it in, F on its reel hangs it back, {drop} lets go";
        }
        g.SandboxLookText = _hoseLook;
    }
}
