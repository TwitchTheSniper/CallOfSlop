using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The linking tool's routed links on this PC (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"). The tool wheel's
/// outer ring round the linking tool picks the type, a pipe's style and the edit modes (SandboxLinkWheel). Routing: LMB on an output (a picker when it
/// has several ports), then each LMB places a node on the surface under the crosshair (snapped; the preview turns red
/// with the reason when a rule is broken), RMB or Backspace takes the last node off, LMB on an input finishes (a picker
/// for its port), Esc cancels. LMB on a link of the same type branches off it. The edit modes delete a link, move a
/// node or re-route a link; "highlight all" shows every link through the parts. R still cycles a logic part's setting;
/// RMB on a part (with nothing being routed) cuts its data links. The wrench or the welder repairs a damaged link (LMB).
/// The server checks every link again (CAct.Route, CAct.RouteEdit).
/// </summary>
public static partial class Program
{
    private static readonly SandboxLinkWheel _linkWheel = new();
    private static readonly RadialMenu _lkPicker = new(8) { DeadZone = 16f, Reach = 60f };
    private static readonly byte[] _lkPickPorts = new byte[8];
    private static int _lkPickCount, _lkPickPart = -1;
    private static bool _lkPickEnd;
    private static readonly string[] _lkPickNames = new string[8];

    private static ushort _lkBody; // a link being routed on this construct (0: none)
    private static int _lkFrom = -1, _lkParent = 255, _lkParentSeg;
    private static byte _lkFromPort = 255, _lkStyle;
    private static RouteType _lkType;
    private static Vector3 _lkJunction;
    private static readonly CRouteNode[] _lkNodes = new CRouteNode[Construct.MaxNodesPerRoute];
    private static int _lkCount;
    private static int _lkMoveRoute = -1, _lkMoveNode = -1; // the edit mode's node being moved
    private static ushort _lkMoveBody;
    private static long _lkHudKey = long.MinValue, _lkLookKey = long.MinValue;
    private static string _lkHud = "", _lkLook = "";
    private static string? _lkWhy; // the preview's reason (a rule broken), or null

    /// <summary>The link being routed is dropped (the tool put away, the construct gone, Esc).</summary>
    private static void LinkReset()
    {
        _lkBody = 0;
        _lkFrom = -1;
        _lkParent = 255;
        _lkCount = 0;
        _lkMoveRoute = -1;
        _lkCheckKey = 0;
        _lkPicker.Close();
        RouteRender.ClearPreview();
    }

    /// <summary>Esc while routing: cancels the link (true: the key was used).</summary>
    private static bool SandboxLinkEsc()
    {
        if (_lkPicker.IsOpen || _lkBody != 0 || _lkMoveRoute >= 0)
        {
            LinkReset();
            return true;
        }
        return false;
    }

    /// <summary>Backspace while routing: the last node comes off (true: the key was used).</summary>
    private static bool SandboxLinkBack()
    {
        if (_lkBody == 0 || !_mouseCaptured)
            return false;
        if (_lkCount > 0)
            _lkCount--;
        else
            LinkReset();
        return true;
    }

    /// <summary>The port picker while the linking tool is out (its options are on the tool wheel's outer ring). Returns true while it is open: the view is frozen.</summary>
    private static bool LinkWheelInput(Game g, Vector2 delta, bool blocked, float dt)
    {
        if (!_lkPicker.IsOpen)
            return false;
        if (blocked)
            _lkPicker.Close();
        else
            _lkPicker.Move(delta, dt);
        return _lkPicker.IsOpen;
    }

    private static void DrawLinkWheelInMatch()
    {
        if (_lkPicker.IsOpen)
        {
            var size = Brutal.ImGuiApi.ImGui.GetIO().DisplaySize;
            _lkPicker.Draw(Brutal.ImGuiApi.ImGui.GetForegroundDrawList(), new Brutal.Numerics.float2(size.X * 0.5f, size.Y * 0.5f), Ui.Scale * 0.8f, SandboxLinkWheel.Icons);
        }
    }

    /// <summary>The style a new pipe from part <paramref name="from"/> gets: the wheel's override, else what the output carries.</summary>
    private static byte LinkStyle(Construct c, int from) =>
        _linkWheel.Type != RouteType.Pipe ? (byte)0 : _linkWheel.Style != 255 ? _linkWheel.Style : (byte)ConstructRoutes.StyleFor(c.DefOf(from));

    /// <summary>A point under the crosshair in the rest frame of the hit part's segment (and the normal there).</summary>
    private static void RestPoint(in ConstructHit hit, out Vector3 p, out Vector3 n)
    {
        var c = hit.Body;
        c.WorldFrame(c.Parts[hit.Part].Seg, out var q, out var t);
        var iq = Quaternion.Conjugate(q);
        p = Vector3.Transform(hit.Point - t, iq);
        n = Vector3.Transform(hit.Normal, iq);
    }

    /// <summary>Where the link being routed is now: its last point and the way its last segment runs.</summary>
    private static void LinkTail(Construct c, out Vector3 at, out Vector3 dir, out int host)
    {
        Vector3 start;
        if (_lkParent != 255)
        {
            start = _lkJunction;
            host = ConstructRoutes.HostOf(c, _lkParent, _lkParentSeg);
        }
        else
        {
            start = ConstructRoutes.PortPoint(c, _lkFrom, _lkFromPort);
            host = _lkFrom;
        }
        at = _lkCount > 0 ? _lkNodes[_lkCount - 1].Pos : start;
        var before = _lkCount > 1 ? _lkNodes[_lkCount - 2].Pos : start;
        dir = _lkCount > 0 && Vector3.DistanceSquared(at, before) > 1e-8f ? Vector3.Normalize(at - before)
            : _lkType == RouteType.Shaft && _lkParent == 255 ? ConstructRoutes.PortDir(c, _lkFrom, _lkFromPort) : Vector3.Zero;
        if (_lkCount > 0)
            host = _lkNodes[_lkCount - 1].Host;
    }

    /// <summary>Why a next point breaks the rules (length, sinking into a part, passing through one, the bend), or null.</summary>
    private static string? LinkLeg(Construct c, Vector3 at, Vector3 dir, int fromHost, Vector3 next, int skipEnd, bool isNode)
    {
        var len = Vector3.Distance(at, next);
        if (isNode && len < ConstructRoutes.MinSegment)
            return "too close to the last node";
        if (len > ConstructRoutes.MaxSegment)
            return $"a segment reaches at most {ConstructRoutes.MaxSegment:0} m: add a node";
        var radius = ConstructRoutes.Radius(_lkType, _lkStyle);
        if (isNode && ConstructRoutes.InsidePart(c, next, radius * 0.9f, -1, -1) is { } inside)
            return $"it would sink into the {c.DefOf(inside).Name}";
        if (ConstructRoutes.Blocked(c, at, next, radius * 0.5f, _lkCount == 0 ? fromHost : -1, skipEnd) is { } wall)
            return $"it would pass through the {c.DefOf(wall).Name}";
        if (dir != Vector3.Zero && len > 1e-4f)
        {
            var bend = MathF.Acos(Math.Clamp(Vector3.Dot(dir, (next - at) / len), -1f, 1f)) * 180f / MathF.PI;
            if (ConstructRoutes.BendRule(_lkType, bend) is { } why)
                return why;
        }
        return null;
    }

    /// <summary>The nearest free port of a part to a point (rest frame), and how many it has.</summary>
    private static int NearestPort(Construct c, int part, Vector3 near, out byte port)
    {
        Span<byte> ports = stackalloc byte[8];
        var n = ConstructRoutes.Ports(c, part, _lkType, ports);
        port = 255;
        var best = float.MaxValue;
        for (var i = 0; i < n; i++)
        {
            var d = Vector3.DistanceSquared(ConstructRoutes.PortPoint(c, part, ports[i]), near);
            if (d < best)
            {
                best = d;
                port = ports[i];
            }
        }
        return n;
    }

    private static void OpenPicker(Construct c, int part, bool end)
    {
        Span<byte> ports = stackalloc byte[8];
        _lkPickCount = ConstructRoutes.Ports(c, part, _lkType, ports);
        for (var i = 0; i < _lkPickCount; i++)
        {
            _lkPickPorts[i] = ports[i];
            _lkPickNames[i] = c.DefOf(part).Sockets[ports[i]].Name;
        }
        for (var i = 0; i < _lkPicker.Items.Length; i++)
        {
            var it = _lkPicker.Items[i];
            it.Label = i < _lkPickCount ? $"port {_lkPickNames[i]}" : "";
            it.Hint = "";
            it.Enabled = i < _lkPickCount;
            it.Icon = 3;
            it.Note = i < _lkPickCount ? "" : "-";
        }
        _lkPicker.Title = $"{c.DefOf(part).Name}: which port?";
        _lkPicker.Help = "point at one, LMB (RMB: cancel)";
        _lkPicker.Open();
        _lkPickPart = part;
        _lkPickEnd = end;
    }

    private static void LinkStart(Construct c, int from, byte port)
    {
        _lkBody = c.Id;
        _lkFrom = from;
        _lkFromPort = port;
        _lkParent = 255;
        _lkCount = 0;
        _lkType = _linkWheel.Type;
        _lkStyle = LinkStyle(c, from);
    }

    private static void LinkFinish(Game g, Construct c, int to, byte port)
    {
        g.SandboxSendRoute(c.Id, _lkType, _lkStyle, _lkFrom, _lkFromPort, _lkParent, _lkParentSeg, _lkJunction, _lkNodes.AsSpan(0, _lkCount), to, port);
        LinkReset();
    }

    /// <summary>The look line for a routed link under the crosshair (its type, health, what it carries and anything wrong), and the wrench's or welder's repair.</summary>
    private static void SandboxRouteLook(Game g, ref SandboxHand h, bool aimed, in ConstructHit hit)
    {
        if (!aimed || hit.RoutePlus1 == 0 || hit.Body.RouteCount < hit.RoutePlus1)
            return;
        var c = hit.Body;
        var k = hit.RoutePlus1 - 1;
        ref var r = ref c.Routes[k];
        RouteRender.AimBody = c;
        RouteRender.AimRoute = k;
        var hp = ConstructRoutes.SegmentHealth(c, k, hit.RouteSeg);
        var repair = h.Tool is ConstructTool.Wrench or ConstructTool.Welder && h.Held == ConstructParts.None;
        var key = ((long)c.Id << 40) ^ ((long)k << 32) ^ ((long)hit.RouteSeg << 24) ^ (long)(hp * 10) ^ ((long)r.Issue << 20) ^ (repair ? 1L << 62 : 0) ^ ((long)h.Tool << 56)
                  ^ (r.FedEnd ? 1L << 61 : 0) ^ ((long)(r.Share * 100) << 12);
        if (key != _lkLookKey)
        {
            _lkLookKey = key;
            var full = ConstructRoutes.Health(r.Type);
            var what = r.Source < c.PartCount && r.To < c.PartCount ? $"{c.DefOf(r.Source).Name} -> {c.DefOf(r.To).Name}" : "";
            var share = r.Type != RouteType.Data && r.Share < 0.999f ? $", {r.Share * 100:0}% of the flow" : "";
            var state = hp <= 0 ? " - CUT" : !r.FedEnd ? " - cut further back" : "";
            var issue = r.Source < c.PartCount && ConstructRoutes.FluidOf(c.DefOf(r.Source)) is { } f ? ConstructRoutes.IssueText(r.Issue, f, (PipeStyle)r.Style) : null;
            var damaged = hp < full || ConstructRoutes.SegmentHealth(c, k, r.Count) < full;
            _lkLook = $"{char.ToUpperInvariant(ConstructRoutes.Name(r.Type, r.Style)[0])}{ConstructRoutes.Name(r.Type, r.Style)[1..]}: {what}{share} {MathF.Max(0, hp):0}/{full:0}{state}"
                      + (issue != null ? $" | {issue}" : "")
                      + (repair && damaged ? " - LMB to repair" : "");
        }
        g.SandboxLookText = _lkLook;
        if (repair && Pressed(GameAction.Fire) && !_swallowClick)
            g.SandboxSendRouteEdit(c.Id, RouteEditOp.Repair, k, 0, r.Source, r.To);
    }

    /// <summary>The linking tool's frame (routing, the edit modes, settings). Called from SandboxInput with the crosshair's hit.</summary>
    private static void SandboxLinkerInput(Game g, Player me, ref SandboxHand h, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit)
    {
        RouteRender.Highlight = _linkWheel.Highlight;
        RouteRender.PreviewBody = null;
        RouteRender.PreviewCount = 0;
        ConstructRender.WireEye = eye;
        if (_lkBody != 0 && g.Constructs.Get(_lkBody) is null || _lkMoveRoute >= 0 && g.Constructs.Get(_lkMoveBody) is null)
            LinkReset();
        var fire = Pressed(GameAction.Fire) && !_swallowClick;
        // The port picker (opened by a click on a part with several ports): LMB picks, RMB cancels.
        if (_lkPicker.IsOpen)
        {
            if (Pressed(GameAction.Aim))
                _lkPicker.Close();
            else if (fire && g.Constructs.Get(_lkPickEnd ? _lkBody : _lkPickBody) is { } pc)
            {
                var pick = _lkPicker.Release();
                if (pick >= 0 && pick < _lkPickCount)
                {
                    if (_lkPickEnd)
                        LinkFinish(g, pc, _lkPickPart, _lkPickPorts[pick]);
                    else
                        LinkStart(pc, _lkPickPart, _lkPickPorts[pick]);
                }
            }
            return;
        }
        var w = _linkWheel;
        var onRoute = aimed && hit.RoutePlus1 > 0 && hit.RoutePlus1 <= hit.Body.RouteCount;
        // The edit modes.
        if (w.Mode != LinkMode.Link && _lkBody == 0)
        {
            if (_lkMoveRoute >= 0 && g.Constructs.Get(_lkMoveBody) is { } mc && _lkMoveRoute < mc.RouteCount)
            {
                // Moving a node: the preview from its neighbours to where it would go.
                if (aimed && hit.Body == mc && !onRoute)
                {
                    RestPoint(hit, out var p, out var n);
                    var at = ConstructRoutes.Snap(mc, mc.Routes[_lkMoveRoute].Type, mc.Routes[_lkMoveRoute].Style, p, Vector3.Zero, p - n * ConstructRoutes.Radius(mc.Routes[_lkMoveRoute].Type, mc.Routes[_lkMoveRoute].Style), n);
                    RouteRender.PreviewBody = mc;
                    RouteRender.PreviewType = mc.Routes[_lkMoveRoute].Type;
                    RouteRender.PreviewStyle = mc.Routes[_lkMoveRoute].Style;
                    RouteRender.PreviewPts[0] = ConstructRoutes.Point(mc, _lkMoveRoute, _lkMoveNode - 1, out var s0);
                    RouteRender.PreviewSeg[0] = (byte)s0;
                    RouteRender.PreviewPts[1] = at;
                    RouteRender.PreviewSeg[1] = mc.Parts[hit.Part].Seg;
                    RouteRender.PreviewPts[2] = ConstructRoutes.Point(mc, _lkMoveRoute, _lkMoveNode + 1, out var s2);
                    RouteRender.PreviewSeg[2] = (byte)s2;
                    RouteRender.PreviewCount = 3;
                    RouteRender.PreviewOk = true;
                    if (fire)
                    {
                        ref var mr = ref mc.Routes[_lkMoveRoute];
                        g.SandboxSendRouteEdit(mc.Id, RouteEditOp.MoveNode, _lkMoveRoute, _lkMoveNode, mr.Source, mr.To, at, (byte)hit.Part);
                        _lkMoveRoute = -1;
                    }
                }
                if (Pressed(GameAction.Aim))
                    _lkMoveRoute = -1;
            }
            else if (fire && onRoute)
            {
                var c = hit.Body;
                var k = hit.RoutePlus1 - 1;
                ref var r = ref c.Routes[k];
                switch (w.Mode)
                {
                    case LinkMode.Delete:
                        g.SandboxSendRouteEdit(c.Id, RouteEditOp.Delete, k, 0, r.Source, r.To);
                        break;
                    case LinkMode.Move:
                        if (r.Count == 0)
                            g.SandboxSay("that link has no nodes to move (route it again with nodes)");
                        else
                        {
                            // The node nearest the crosshair.
                            var best = float.MaxValue;
                            for (var i = 0; i < r.Count; i++)
                            {
                                var d = Vector3.DistanceSquared(ConstructRoutes.WorldPoint(c, k, i), hit.Point);
                                if (d < best)
                                {
                                    best = d;
                                    _lkMoveNode = i;
                                }
                            }
                            _lkMoveRoute = k;
                            _lkMoveBody = c.Id;
                        }
                        break;
                    case LinkMode.Reroute:
                        var type = r.Type;
                        var style = r.Style;
                        var source = r.Source;
                        var port = r.Parent == 255 ? r.FromPort : (byte)255;
                        g.SandboxSendRouteEdit(c.Id, RouteEditOp.Delete, k, 0, r.Source, r.To);
                        w.Type = type;
                        LinkStart(c, source, port);
                        _lkStyle = style;
                        w.Mode = LinkMode.Link;
                        break;
                }
            }
            LinkHud(g);
            return;
        }
        // Routing.
        if (_lkBody != 0 && g.Constructs.Get(_lkBody) is { } rc)
        {
            LinkTail(rc, out var at, out var tailDir, out var tailHost);
            // The preview: the link so far, then the next point (a node on the surface, or the input that ends it).
            var n0 = 0;
            RouteRender.PreviewBody = rc;
            RouteRender.PreviewType = _lkType;
            RouteRender.PreviewStyle = _lkStyle;
            RouteRender.PreviewPts[n0] = _lkParent != 255 ? _lkJunction : ConstructRoutes.PortPoint(rc, _lkFrom, _lkFromPort);
            RouteRender.PreviewSeg[n0++] = (byte)(_lkParent != 255 ? rc.Parts[Math.Min(ConstructRoutes.HostOf(rc, _lkParent, _lkParentSeg), rc.PartCount - 1)].Seg : rc.Parts[_lkFrom].Seg);
            for (var i = 0; i < _lkCount; i++)
            {
                RouteRender.PreviewPts[n0] = _lkNodes[i].Pos;
                RouteRender.PreviewSeg[n0++] = rc.Parts[Math.Min((int)_lkNodes[i].Host, rc.PartCount - 1)].Seg;
            }
            _lkWhy = "point at a surface for a node, or at an input";
            var endPart = -1;
            byte endPort = 255;
            var ports = 0;
            Vector3 next = default;
            if (aimed && hit.Body != rc)
            {
                _lkWhy = _lkType == RouteType.Data && _lkCount == 0 && _lkParent == 255 ? null : "a routed link stays on one construct";
                if (_lkWhy == null && !ConstructRoutes.IsInput(hit.Body.DefOf(hit.Part), RouteType.Data))
                    _lkWhy = $"a {hit.Body.DefOf(hit.Part).Name} takes no input";
                if (fire && _lkWhy == null)
                {
                    // (a straight data cable into another construct: milestone 3's wire between constructs)
                    g.SandboxSend(CActKind.Wire, body: rc.Id, part: (byte)_lkFrom, socket: (byte)hit.Part, body2: hit.Body.Id);
                    LinkReset();
                    return;
                }
            }
            else if (aimed && !onRoute)
            {
                RestPoint(hit, out var p, out var nrm);
                var d = hit.Body.DefOf(hit.Part);
                var input = ConstructRoutes.IsInput(d, _lkType) && !(hit.Part == _lkFrom && _lkParent == 255 && !(_lkType == RouteType.Data && d.Role == PartRole.Logic));
                if (input)
                {
                    endPart = hit.Part;
                    ports = NearestPort(rc, endPart, p, out endPort);
                    next = ConstructRoutes.PortPoint(rc, endPart, endPort);
                }
                else
                    next = ConstructRoutes.Snap(rc, _lkType, _lkStyle, at, tailDir, p, nrm);
                // (checked again only when the candidate changes: the reasons are made once, not every frame)
                var ck = HashCode.Combine(next, endPart, endPort, _lkCount, hit.Part, (int)_lkType, _lkStyle, rc.RouteCount);
                if (ck != _lkCheckKey)
                {
                    _lkCheckKey = ck;
                    _lkCheckWhy = input
                        ? ports == 0 ? $"every port of the {d.Name} is taken" : ConstructRoutes.Check(g.Constructs, rc, _lkType, _lkStyle, _lkFrom, _lkFromPort, _lkParent, _lkParentSeg, _lkJunction, _lkNodes.AsSpan(0, _lkCount), endPart, endPort)
                        : _lkCount >= Construct.MaxNodesPerRoute ? $"a link has at most {Construct.MaxNodesPerRoute} nodes" : LinkLeg(rc, at, tailDir, tailHost, next, -1, true);
                }
                _lkWhy = _lkCheckWhy;
                RouteRender.PreviewPts[n0] = next;
                RouteRender.PreviewSeg[n0++] = rc.Parts[hit.Part].Seg;
            }
            RouteRender.PreviewCount = n0;
            RouteRender.PreviewOk = _lkWhy == null;
            if (fire && aimed && hit.Body == rc && !onRoute)
            {
                if (_lkWhy != null)
                    g.SandboxSay(_lkWhy);
                else if (endPart >= 0)
                {
                    if (ports > 1)
                        OpenPicker(rc, endPart, end: true);
                    else
                        LinkFinish(g, rc, endPart, endPort);
                }
                else
                    _lkNodes[_lkCount++] = new CRouteNode { Pos = next, Host = (byte)hit.Part };
            }
            if (Pressed(GameAction.Aim))
            {
                if (_lkCount > 0)
                    _lkCount--;
                else
                    LinkReset();
            }
            LinkHud(g);
            return;
        }
        // Nothing routed yet: LMB on an output starts one, on a link of the same type branches off it.
        if (fire && aimed)
        {
            var c = hit.Body;
            if (onRoute)
            {
                var k = hit.RoutePlus1 - 1;
                ref var r = ref c.Routes[k];
                if (r.Type != w.Type)
                    g.SandboxSay($"that is a {ConstructRoutes.Name(r.Type, r.Style)}: pick its type on the tool wheel to branch off it");
                else if (r.Type == RouteType.Shaft && ConstructRoutes.ShaftJunctionNeedsTBox)
                    g.SandboxSay("a drive shaft branches only through a T gearbox");
                else
                {
                    // The junction: the nearest point on that segment (rest frame), on the 1 cm grid.
                    var a = ConstructRoutes.Point(c, k, hit.RouteSeg - 1, out var sa);
                    var b = ConstructRoutes.Point(c, k, hit.RouteSeg, out _);
                    c.WorldFrame(sa, out var q, out var t);
                    var local = Vector3.Transform(hit.Point - t, Quaternion.Conjugate(q));
                    Geometry.ClosestSegmentSegment(a, b, local, local, out var on, out _);
                    _lkBody = c.Id;
                    _lkFrom = -1;
                    _lkFromPort = 255;
                    _lkParent = k;
                    _lkParentSeg = hit.RouteSeg;
                    _lkJunction = ConstructWorld.Quantise(on);
                    _lkCount = 0;
                    _lkType = r.Type;
                    _lkStyle = r.Style;
                }
            }
            else
            {
                var d = c.DefOf(hit.Part);
                if (!ConstructRoutes.IsOutput(d, w.Type))
                    g.SandboxSay($"a {d.Name} has no {ConstructRoutes.Name(w.Type, LinkStyle(c, hit.Part))} output ({ConstructRoutes.OutputHint(w.Type)})");
                else
                {
                    _lkType = w.Type;
                    RestPoint(hit, out var p, out _);
                    var nports = NearestPort(c, hit.Part, p, out var port);
                    if (nports == 0)
                        g.SandboxSay($"every port of the {d.Name} is taken");
                    else if (nports > 1)
                    {
                        _lkPickBody = c.Id;
                        OpenPicker(c, hit.Part, end: false);
                    }
                    else
                        LinkStart(c, hit.Part, port);
                }
            }
        }
        if (Pressed(GameAction.Aim) && aimed && !onRoute)
            g.SandboxSend(CActKind.Unwire, body: hit.Body.Id, part: (byte)hit.Part);
        if (aimed && !onRoute && Pressed(GameAction.Reload))
            g.SandboxSend(CActKind.Setting, body: hit.Body.Id, part: (byte)hit.Part);
        LinkHud(g);
    }

    private static ushort _lkPickBody;
    private static int _lkCheckKey;
    private static string? _lkCheckWhy;

    /// <summary>The linking tool's HUD line (remade only when what it says changes).</summary>
    private static void LinkHud(Game g)
    {
        var w = _linkWheel;
        var key = (long)w.Type ^ ((long)w.Style << 4) ^ ((long)w.Mode << 12) ^ ((long)_lkCount << 16) ^ ((long)_lkBody << 24) ^ (w.Highlight ? 1L << 50 : 0)
                  ^ ((long)(_lkWhy?.GetHashCode() ?? 0) << 20) ^ (_lkMoveRoute >= 0 ? 1L << 51 : 0) ^ ((long)_lkStyle << 52) ^ ((long)g.SandboxWheelKey.GetHashCode() << 32);
        if (key == _lkHudKey)
        {
            g.SandboxWireFrom = _lkHud;
            return;
        }
        _lkHudKey = key;
        var type = ConstructRoutes.Name(_lkBody != 0 ? _lkType : w.Type, _lkBody != 0 ? _lkStyle : w.Type == RouteType.Pipe && w.Style != 255 ? w.Style : (byte)0);
        if (_lkBody == 0 && w.Type == RouteType.Pipe && w.Style == 255)
            type = "pipe (style from the output)";
        _lkHud = w.Mode != LinkMode.Link
            ? _lkMoveRoute >= 0 ? "Move node: LMB where it goes (RMB: let go)" : $"Linking tool: {w.ModeName} - LMB on a link. Hold {g.SandboxWheelKey}: tool wheel, link types"
            : _lkBody == 0
                ? $"Linking tool: {type}{(w.Highlight ? " (highlight all)" : "")} - LMB an output (or a link to branch). RMB: cut a part's wires. R: setting. Hold {g.SandboxWheelKey}: tool wheel, link types"
                : $"Routing a {type}, {_lkCount} node{(_lkCount == 1 ? "" : "s")}: LMB a node or the input. RMB/Backspace: last node off. Esc: cancel{(_lkWhy != null ? $" | {_lkWhy}" : "")}";
        g.SandboxWireFrom = _lkHud;
    }
}
