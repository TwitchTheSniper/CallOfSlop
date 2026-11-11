using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The linking tool's auto-routing on this PC (docs/sandbox/CONSTRUCTS.md, "Routed links", "Auto-routing"): with an output picked,
/// aiming at an input shows the route the game found (<see cref="RouteSearch"/>, a couple of milliseconds a frame) as a live
/// ghost, green or red with the reason; a click confirms it. A node clicked on the way stays a node, and the router carries on
/// from it; aiming at a surface the straight line can't reach routes to that spot. The free routing key turns it all off.
/// The ghost's nodes are ordinary nodes: the client checks them with <see cref="ConstructRoutes.Check"/> and sends them, and
/// the server checks them as it checks a manual link.
/// </summary>
public static partial class Program
{
    private const long AutoSettleMs = 150; // a waypoint is searched for once the crosshair has stayed on it this long

    private static readonly RouteSearch _lkAuto = new();
    private static RouteSearch.Result? _lkAutoRes; // the search behind this frame's ghost (null: none, the straight line or the manual rules)
    private static Vector3 _lkSpot;
    private static long _lkSpotAt;
    private static long _lkAutoLookKey = long.MinValue;
    private static string _lkAutoLook = "";
    internal static bool LinkAutoOff; // the old flow: the straight line only and always the picker (the self-test seam's default)

    /// <summary>A straight link fits as well as any: a shaft's, a short one's, or one square with the construct's axes.</summary>
    private static bool LinkStraightTidy(Vector3 from, Vector3 to)
    {
        if (_lkType == RouteType.Shaft)
            return true;
        var d = Vector3.Abs(to - from);
        return d.X + d.Y + d.Z < 0.75f || (d.X > 0.02f ? 1 : 0) + (d.Y > 0.02f ? 1 : 0) + (d.Z > 0.02f ? 1 : 0) <= 1;
    }

    private static RouteAsk LinkAsk(Game g, Construct c, int to, byte port, Vector3 openAt, ushort openHost) => new()
    {
        World = g.Constructs, C = c, Type = _lkType, From = _lkFrom, FromPort = _lkFromPort, Parent = _lkParent, ParentSeg = _lkParentSeg, Junction = _lkJunction,
        Nodes = _lkNodes, Count = _lkCount, To = to, ToPort = port, OpenAt = openAt, OpenHost = openHost,
    };

    /// <summary>
    /// The ghost to an input: the straight line while that is tidy and fits, else the search's route. Sets the preview past the
    /// link so far (<paramref name="n0"/> points) and <see cref="_lkWhy"/>, from the straight check <paramref name="straightWhy"/>.
    /// </summary>
    private static void LinkAutoInput(Game g, Construct c, Vector3 eye, Vector3 tail, int to, byte port, string? straightWhy, int n0)
    {
        _lkWhy = straightWhy;
        var end = ConstructRoutes.PortPoint(c, to, port, _lkType);
        if (LinkAutoOff || _lkFree || port == 255 && _lkType == RouteType.Shaft
            || ConstructRoutes.CheckEnds(g.Constructs, c, _lkType, _lkFrom, _lkFromPort, _lkParent, _lkParentSeg, _lkJunction, _lkCount, to, port) != null
            || straightWhy == null && LinkStraightTidy(tail, end))
        {
            LinkGhostEnd(c, n0, to, port);
            return;
        }
        var res = _lkAuto.Poll(LinkAsk(g, c, to, port, default, 0));
        _lkAutoRes = res;
        if (res.State == RouteSearchState.Failed && straightWhy == null)
        {
            _lkAutoRes = null; // (no square way, but the straight line fits)
            LinkGhostEnd(c, n0, to, port);
            return;
        }
        _lkWhy = res.State switch
        {
            RouteSearchState.Found => null,
            RouteSearchState.Failed => res.Why ?? "no way round the parts",
            _ => "routing",
        };
        RouteRender.PreviewBusy = res.State == RouteSearchState.Busy;
        if (res.State == RouteSearchState.Found && res.Width > 0)
            RouteRender.PreviewWidth = res.Width / 100f; // (the bed it will be laid at: what its ends take and its turns leave room for)
        var n = LinkGhostNodes(c, res, n0);
        RouteRender.PreviewPts[n] = ConstructRoutes.LivePort(c, to, port, _lkType);
        RouteRender.PreviewSeg[n++] = c.Parts[to].Seg;
        RouteRender.PreviewCount = n;
        RouteRender.PreviewOk = _lkWhy == null;
        if (res.State == RouteSearchState.Found)
        LinkAutoLookLine(g, c, to);
    }

    private static void LinkGhostEnd(Construct c, int n0, int to, byte port)
    {
        if (_lkType == RouteType.Conveyor && _lkParent == 255)
            RouteRender.PreviewWidth = ConstructRoutes.ConveyorWidth(c, _lkFrom, _lkFromPort, to, port); // (a straight conveyor opens to what both ends take)
        RouteRender.PreviewPts[n0] = ConstructRoutes.LivePort(c, to, port, _lkType);
        RouteRender.PreviewSeg[n0++] = c.Parts[to].Seg;
        RouteRender.PreviewCount = n0;
        RouteRender.PreviewOk = _lkWhy == null;
    }

    private static int LinkGhostNodes(Construct c, RouteSearch.Result res, int n)
    {
        RouteRender.PreviewFrom = n;
        for (var i = 0; i < res.Count && n < RouteRender.PreviewPts.Length - 1; i++)
        {
            RouteRender.PreviewPts[n] = res.Nodes[i].Pos;
            RouteRender.PreviewSeg[n++] = c.Parts[Math.Min((int)res.Nodes[i].Host, c.PartCount - 1)].Seg;
        }
        return n;
    }

    /// <summary>
    /// The ghost to a surface the straight line can't reach (the link's next node is <paramref name="next"/> on
    /// <paramref name="host"/>): once the crosshair settles, a route to it. Nothing changes while the straight line fits.
    /// </summary>
    private static bool LinkAutoSpot(Game g, Construct c, Vector3 next, int host, string? straightWhy, int n0)
    {
        if (LinkAutoOff || _lkFree || straightWhy == null || _lkCount >= Construct.MaxNodesPerRoute)
            return false;
        var now = Environment.TickCount64;
        if (Vector3.DistanceSquared(next, _lkSpot) > 1e-6f)
        {
            _lkSpot = next;
            _lkSpotAt = now;
        }
        if (now - _lkSpotAt < AutoSettleMs)
            return false;
        var res = _lkAuto.Poll(LinkAsk(g, c, -1, 255, next, (ushort)host));
        if (res.State == RouteSearchState.Busy)
        {
            RouteRender.PreviewBusy = true;
            return false;
        }
        if (res.State != RouteSearchState.Found)
            return false;
        _lkAutoRes = res;
        _lkWhy = null;
        RouteRender.PreviewCount = LinkGhostNodes(c, res, n0);
        RouteRender.PreviewOk = true;
        return true;
    }

    /// <summary>The ghost for the port the open end picker is on: what a click will make.</summary>
    private static void LinkAutoPicker(Game g, Vector3 eye)
    {
        if (!_lkPickEnd || g.Constructs.Get(_lkBody) is not { } c || _lkPickPart < 0 || _lkPickPart >= c.PartCount)
            return;
        var i = _lkPicker.Hovered;
        if (i < 0 || i >= _lkPickCount || !_lkPicker.Items[i].Enabled)
            return;
        var port = _lkPickPorts[i];
        var n0 = LinkPreviewSoFar(c);
        LinkTail(c, out var tail, out _, out _);
        var ck = HashCode.Combine(port, _lkPickPart, _lkCount, (int)_lkType, c.RouteCount, c.RebuildStamp, 7);
        if (ck != _lkCheckKey)
        {
            _lkCheckKey = ck;
            _lkCheckWhy = ConstructRoutes.Check(g.Constructs, c, _lkType, _lkFrom, _lkFromPort, _lkParent, _lkParentSeg, _lkJunction, _lkNodes.AsSpan(0, _lkCount), _lkPickPart, port);
        }
        LinkAutoInput(g, c, eye, tail, _lkPickPart, port, _lkCheckWhy, n0);
    }

    /// <summary>The route the ghost shows goes on the link being made. False: it doesn't fit (the link is left as it was).</summary>
    private static bool LinkAutoTake(RouteSearch.Result res)
    {
        if (_lkCount + res.Count > Construct.MaxNodesPerRoute)
            return false;
        for (var i = 0; i < res.Count; i++)
        {
            _lkNormals[_lkCount] = Vector3.UnitY;
            _lkNodes[_lkCount++] = res.Nodes[i];
        }
        return true;
    }

    /// <summary>The ghost's click on an input with one port: the route goes in, checked once more against the construct as it is now.</summary>
    private static void LinkAutoFinish(Game g, Construct c, RouteSearch.Result res, int to, byte port)
    {
        var keep = _lkCount;
        if (!LinkAutoTake(res))
        {
            g.SandboxSay($"a link has at most {Construct.MaxNodesPerRoute} nodes");
            return;
        }
        if (ConstructRoutes.Check(g.Constructs, c, _lkType, _lkFrom, _lkFromPort, _lkParent, _lkParentSeg, _lkJunction, _lkNodes.AsSpan(0, _lkCount), to, port) is { } why)
        {
            _lkCount = keep;
            g.SandboxSay(why);
            return;
        }
        LinkFinish(g, c, to, port);
    }

    /// <summary>The look line for the ghost, as every look line: name, state, what's wrong, the key.</summary>
    private static void LinkAutoLookLine(Game g, Construct c, int to)
    {
        var res = _lkAutoRes;
        if (res == null)
            return;
        var key = ((long)res.Key << 3) ^ (long)res.State ^ ((long)to << 44) ^ ((long)SandboxKeys.Stamp << 56);
        if (key != _lkAutoLookKey)
        {
            _lkAutoLookKey = key;
            var name = $"{Cap(ConstructRoutes.Name(_lkType))} to {c.DefOf(to).Name}";
            _lkAutoLook = res.State switch
            {
                RouteSearchState.Found => $"{name}: route found, {res.Count} node{(res.Count == 1 ? "" : "s")}, {res.Length:0.0} m{(res.Width > 0 ? $", {res.Width / 100f:0.00} m wide" : "")} - {SandboxKeys.Fire}: confirm",
                RouteSearchState.Failed => $"{name}: no route - {res.Why} - {SandboxKeys.Fire} a surface: place a node",
                _ => $"{name}: routing",
            };
        }
        g.SandboxLookText = _lkAutoLook;
    }

    /// <summary>The self-test seam: <see cref="LinkTestFrame"/> runs the old flow unless a run asks for this one.</summary>
    internal static bool LinkTestAuto;
    internal static RouteSearch.Result? LinkTestAutoResult => _lkAutoRes;
    internal static RouteSearch LinkTestAutoSearch => _lkAuto;
}
