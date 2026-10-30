using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The linking tool's routed links on this PC (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"). Hold the tool
/// wheel key for the link wheel (the type, a pipe's style, the edit modes). Routing: LMB on an output (a picker when it
/// has several ports), then each LMB places a node on the surface under the crosshair (snapped; the preview turns red
/// with the reason when a rule is broken), RMB or Backspace takes the last node off, LMB on an input finishes (a picker
/// for its port), Esc cancels. LMB on a link of the same type branches off it. The edit modes delete a link, move a
/// node or re-route a link; "highlight all" shows every link through the parts. R still cycles a logic part's setting;
/// RMB on a part (with nothing being routed) cuts its data links. The wrench or the welder repairs a damaged link (LMB).
/// The server checks every link again (CAct.Route, CAct.RouteEdit) with the same rules (ConstructRoutes.PathRule).
/// Hold to adjust (board item lg01): LMB on a red node (or a red input end, or a red moved node) freezes it and enters
/// the adjust mode: the nearest valid offset along the surface normal is found, the wheel moves it while LMB is held
/// (1 cm a notch, Ctrl 5 cm), and letting go on green places it (on red: nothing). Free routing (board item ls01): while
/// the Free routing key (Left Shift) is held, aiming at a device places an ordinary node on it (no port snap, no finish).
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
    private static readonly Vector3[] _lkNormals = new Vector3[Construct.MaxNodesPerRoute]; // each placed node's surface normal (rest frame): the input end's adjust moves the last node along it
    private static bool _lkFree; // the Free routing key is held while routing (no port snap, no finishing, no branching)

    // Hold to adjust (board item lg01): a red node frozen at the click, moved along its surface normal by the wheel.
    private enum LinkAdjust : byte
    {
        None,
        Node, // the next node of the link being routed
        End, // the last node before the input it ends at (then the link finishes)
        Move, // the edit mode's node being moved
    }

    private static LinkAdjust _lkAdj;
    private static Vector3 _lkAdjBase, _lkAdjNormal; // rest frame: the snapped spot at the click, its surface normal
    private static int _lkAdjCm, _lkAdjKey = int.MinValue, _lkAdjEnd = -1, _lkAdjPorts;
    private static byte _lkAdjHost, _lkAdjEndPort;
    private static string? _lkAdjWhy;
    private static double _lkScroll; // wheel notches while adjusting
    private static string _lkAdjText = "";
    private static int _lkAdjTextKey = int.MinValue;

    /// <summary>The link being routed is dropped (the tool put away, the construct gone, Esc).</summary>
    private static void LinkReset()
    {
        _lkBody = 0;
        _lkFrom = -1;
        _lkParent = 255;
        _lkCount = 0;
        _lkMoveRoute = -1;
        _lkCheckKey = 0;
        _lkAdj = LinkAdjust.None;
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

    /// <summary>The link wheel while the linking tool is out (the tool wheel otherwise). Returns true while it (or the port picker) is open: the view is frozen.</summary>
    private static bool LinkWheelInput(Game g, Vector2 delta, bool blocked, float dt)
    {
        if (_lkPicker.IsOpen)
        {
            if (blocked)
                _lkPicker.Close();
            else
                _lkPicker.Move(delta);
            return _lkPicker.IsOpen;
        }
        var open = _linkWheel.Step(Down(GameAction.ToolWheel), delta, dt, blocked);
        if (_linkWheel.WantsTools)
        {
            _toolWheel.Menu.Open(); // (the tool wheel in its place: the key is still held)
            return true;
        }
        return open;
    }

    private static void DrawLinkWheelInMatch()
    {
        _linkWheel.Draw(Ui.Scale);
        if (_lkPicker.IsOpen)
        {
            var size = Brutal.ImGuiApi.ImGui.GetIO().DisplaySize;
            _lkPicker.Draw(Brutal.ImGuiApi.ImGui.GetForegroundDrawList(), new Brutal.Numerics.float2(size.X * 0.5f, size.Y * 0.5f), Ui.Scale * 0.8f, SandboxLinkWheel.Icons);
        }
        if (_lkAdj != LinkAdjust.None && _lkAdjText.Length > 0)
        {
            // The adjust offset next to the crosshair: green while valid, red with the reason in the HUD line.
            var size = Brutal.ImGuiApi.ImGui.GetIO().DisplaySize;
            var px = Brutal.ImGuiApi.ImGui.GetFontSize() * 1.1f;
            Hud.ShadowText(Brutal.ImGuiApi.ImGui.GetForegroundDrawList(), new Brutal.Numerics.float2(size.X * 0.5f + 18f * Ui.Scale, size.Y * 0.5f + 8f * Ui.Scale), px, _lkAdjText,
                _lkAdjWhy == null ? new Vector3(0.4f, 1f, 0.5f) : new Vector3(1f, 0.4f, 0.35f), 1f);
        }
    }

    /// <summary>The mouse wheel: true while a node is being adjusted (LMB held): the wheel moves it, not the weapon or the wheels.</summary>
    private static bool LinkAdjustScroll(double y)
    {
        if (_lkAdj == LinkAdjust.None)
            return false;
        _lkScroll += y;
        return true;
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

    /// <summary>The rule for the node being adjusted at <paramref name="pos"/> (the routing, the input end or the move: one rule with the server's).</summary>
    private static string? LinkAdjustWhy(Construct c, Vector3 pos)
    {
        switch (_lkAdj)
        {
            case LinkAdjust.Node:
                var open = LinkOpenRule(c, _lkAdjHost);
                return open.Why(pos);
            case LinkAdjust.End:
                var closed = LinkClosedRule(c);
                return closed.Why(pos);
            case LinkAdjust.Move:
                var move = new ConstructRoutes.MoveRule { C = c, Route = _lkMoveRoute, Node = _lkMoveNode, Host = _lkAdjHost };
                return move.Why(pos);
            default:
                return "nothing to adjust";
        }
    }

    private static ConstructRoutes.OpenRule LinkOpenRule(Construct c, byte host) => new()
    {
        C = c, Type = _lkType, Style = _lkStyle, From = _lkFrom, FromPort = _lkFromPort, Parent = _lkParent, ParentSeg = _lkParentSeg, Junction = _lkJunction, Nodes = _lkNodes,
        Count = _lkCount, Host = host,
    };

    private static ConstructRoutes.ClosedRule LinkClosedRule(Construct c) => new()
    {
        World = _game?.Constructs, C = c, Type = _lkType, Style = _lkStyle, From = _lkFrom, FromPort = _lkFromPort, Parent = _lkParent, ParentSeg = _lkParentSeg, Junction = _lkJunction,
        Nodes = _lkNodes, Count = _lkCount, Node = _lkCount - 1, To = _lkAdjEnd, ToPort = _lkAdjEndPort,
    };

    /// <summary>
    /// A click on a red spot: the node freezes there and the adjust mode starts. The search jumps to the nearest valid
    /// offset along the normal (out first, then in to the embedding limit), if any.
    /// </summary>
    private static void LinkAdjustStart(Game g, Construct c, LinkAdjust kind, Vector3 basePos, Vector3 normal, byte host, string why)
    {
        _lkAdj = kind;
        _lkAdjBase = basePos;
        _lkAdjNormal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY;
        _lkAdjHost = host;
        _lkScroll = 0;
        _lkAdjKey = int.MinValue;
        LinkAdjustType(c, out var type, out var style);
        int? found;
        switch (kind)
        {
            case LinkAdjust.Node:
                var open = LinkOpenRule(c, host);
                found = ConstructRoutes.AdjustSearch(type, style, _lkAdjBase, _lkAdjNormal, ref open);
                break;
            case LinkAdjust.End:
                var closed = LinkClosedRule(c);
                found = ConstructRoutes.AdjustSearch(type, style, _lkAdjBase, _lkAdjNormal, ref closed);
                break;
            default:
                var move = new ConstructRoutes.MoveRule { C = c, Route = _lkMoveRoute, Node = _lkMoveNode, Host = host };
                found = ConstructRoutes.AdjustSearch(type, style, _lkAdjBase, _lkAdjNormal, ref move);
                break;
        }
        _lkAdjCm = found ?? 0;
        if (found == null)
            g.SandboxSay($"{why} (hold LMB and scroll to move it along the surface normal)");
    }

    private static void LinkAdjustType(Construct c, out RouteType type, out byte style)
    {
        if (_lkAdj == LinkAdjust.Move && _lkMoveRoute >= 0 && _lkMoveRoute < c.RouteCount)
        {
            type = c.Routes[_lkMoveRoute].Type;
            style = c.Routes[_lkMoveRoute].Style;
        }
        else
        {
            type = _lkType;
            style = _lkStyle;
        }
    }

    /// <summary>
    /// A frame of the adjust mode (LMB held): the wheel steps the node along its normal, the preview re-checks each step;
    /// letting go places it when valid (nothing otherwise), RMB cancels. <paramref name="n0"/>: the preview's points so
    /// far (the routing: the link up to its last node).
    /// </summary>
    private static void LinkAdjustFrame(Game g, Construct c, int n0)
    {
        LinkAdjustType(c, out var type, out var style);
        var steps = (int)_lkScroll;
        _lkScroll -= steps;
        if (steps != 0)
        {
            var ctrl = _window.GetKey(Brutal.GlfwApi.GlfwKey.LeftControl) == Brutal.GlfwApi.GlfwKeyAction.Press || _window.GetKey(Brutal.GlfwApi.GlfwKey.RightControl) == Brutal.GlfwApi.GlfwKeyAction.Press;
            _lkAdjCm = ConstructRoutes.AdjustClamp(type, style, _lkAdjCm + steps * (ctrl ? ConstructRoutes.AdjustBigStepCm : ConstructRoutes.AdjustStepCm));
        }
        var pos = ConstructRoutes.AdjustPos(_lkAdjBase, _lkAdjNormal, _lkAdjCm);
        // (checked again only when the spot changes)
        var key = HashCode.Combine(pos, (int)_lkAdj, _lkAdjHost, _lkCount, c.RouteCount);
        if (key != _lkAdjKey)
        {
            _lkAdjKey = key;
            _lkAdjWhy = LinkAdjustWhy(c, pos);
        }
        var tk = _lkAdjCm * 2 + (_lkAdjWhy == null ? 1 : 0);
        if (tk != _lkAdjTextKey)
        {
            _lkAdjTextKey = tk;
            _lkAdjText = $"{(_lkAdjCm > 0 ? "+" : "")}{_lkAdjCm} cm";
        }
        _lkWhy = _lkAdjWhy;
        // The preview.
        RouteRender.PreviewBody = c;
        RouteRender.PreviewType = type;
        RouteRender.PreviewStyle = style;
        var hostSeg = (byte)c.Parts[Math.Min((int)_lkAdjHost, c.PartCount - 1)].Seg;
        switch (_lkAdj)
        {
            case LinkAdjust.Node:
                RouteRender.PreviewPts[n0] = pos;
                RouteRender.PreviewSeg[n0++] = hostSeg;
                break;
            case LinkAdjust.End:
                RouteRender.PreviewPts[n0 - 1] = pos; // (the last node, moved)
                if (_lkAdjEnd >= 0 && _lkAdjEnd < c.PartCount)
                {
                    RouteRender.PreviewPts[n0] = ConstructRoutes.PortPoint(c, _lkAdjEnd, _lkAdjEndPort);
                    RouteRender.PreviewSeg[n0++] = (byte)c.Parts[_lkAdjEnd].Seg;
                }
                break;
            default:
                n0 = 0;
                RouteRender.PreviewPts[n0] = ConstructRoutes.Point(c, _lkMoveRoute, _lkMoveNode - 1, out var s0);
                RouteRender.PreviewSeg[n0++] = (byte)s0;
                RouteRender.PreviewPts[n0] = pos;
                RouteRender.PreviewSeg[n0++] = hostSeg;
                RouteRender.PreviewPts[n0] = ConstructRoutes.Point(c, _lkMoveRoute, _lkMoveNode + 1, out var s2);
                RouteRender.PreviewSeg[n0++] = (byte)s2;
                break;
        }
        RouteRender.PreviewCount = n0;
        RouteRender.PreviewOk = _lkAdjWhy == null;
        if (Pressed(GameAction.Aim))
        {
            _lkAdj = LinkAdjust.None; // (RMB: cancel the adjust; nothing placed)
            return;
        }
        if (Down(GameAction.Fire))
            return;
        // Let go: placed when valid, else nothing.
        var kind = _lkAdj;
        _lkAdj = LinkAdjust.None;
        if (_lkAdjWhy != null)
        {
            g.SandboxSay($"not placed: {_lkAdjWhy}");
            return;
        }
        switch (kind)
        {
            case LinkAdjust.Node:
                _lkNormals[_lkCount] = _lkAdjNormal;
                _lkNodes[_lkCount++] = new CRouteNode { Pos = pos, Host = _lkAdjHost };
                break;
            case LinkAdjust.End:
                _lkNodes[_lkCount - 1].Pos = pos;
                if (_lkAdjPorts > 1)
                    OpenPicker(c, _lkAdjEnd, end: true);
                else
                    LinkFinish(g, c, _lkAdjEnd, _lkAdjEndPort);
                break;
            default:
                ref var mr = ref c.Routes[_lkMoveRoute];
                g.SandboxSendRouteEdit(c.Id, RouteEditOp.MoveNode, _lkMoveRoute, _lkMoveNode, mr.Source, mr.To, pos, _lkAdjHost);
                _lkMoveRoute = -1;
                break;
        }
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
            if (_lkMoveRoute >= 0 && g.Constructs.Get(_lkMoveBody) is { } mc && _lkMoveRoute < mc.RouteCount && _lkMoveNode < mc.Routes[_lkMoveRoute].Count)
            {
                _lkFree = Down(GameAction.FreeRouting); // (a moved node may sit on a device either way: the hint says so)
                if (_lkAdj == LinkAdjust.Move)
                {
                    LinkAdjustFrame(g, mc, 0);
                    LinkHud(g);
                    return;
                }
                // Moving a node: the preview from its neighbours to where it would go, checked with the server's rule.
                if (aimed && hit.Body == mc && !onRoute)
                {
                    RestPoint(hit, out var p, out var n);
                    var at = ConstructRoutes.Snap(mc, mc.Routes[_lkMoveRoute].Type, mc.Routes[_lkMoveRoute].Style, p, Vector3.Zero, p - n * ConstructRoutes.Radius(mc.Routes[_lkMoveRoute].Type, mc.Routes[_lkMoveRoute].Style), n);
                    var mk = HashCode.Combine(at, hit.Part, _lkMoveRoute, _lkMoveNode, mc.RouteCount, 1);
                    if (mk != _lkCheckKey)
                    {
                        _lkCheckKey = mk;
                        _lkCheckWhy = ConstructRoutes.CheckMove(mc, _lkMoveRoute, _lkMoveNode, at, (byte)hit.Part);
                    }
                    _lkWhy = _lkCheckWhy;
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
                    RouteRender.PreviewOk = _lkWhy == null;
                    if (fire && _lkWhy != null)
                        LinkAdjustStart(g, mc, LinkAdjust.Move, at, n, (byte)hit.Part, _lkWhy);
                    else if (fire)
                    {
                        ref var mr = ref mc.Routes[_lkMoveRoute];
                        g.SandboxSendRouteEdit(mc.Id, RouteEditOp.MoveNode, _lkMoveRoute, _lkMoveNode, mr.Source, mr.To, at, (byte)hit.Part);
                        _lkMoveRoute = -1;
                    }
                }
                else
                    _lkWhy = null;
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
            _lkFree = Down(GameAction.FreeRouting);
            LinkTail(rc, out var at, out var tailDir, out _);
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
            if (_lkAdj is LinkAdjust.Node or LinkAdjust.End)
            {
                LinkAdjustFrame(g, rc, n0);
                LinkHud(g);
                return;
            }
            _lkWhy = "point at a surface for a node, or at an input";
            var endPart = -1;
            byte endPort = 255;
            var ports = 0;
            Vector3 next = default, nrm = default;
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
                RestPoint(hit, out var p, out nrm);
                var d = hit.Body.DefOf(hit.Part);
                // (free routing: a device is just a surface, its port highlight hidden)
                var input = ConstructRoutes.ClickEnds(rc, _lkType, _lkFrom, _lkParent, hit.Part, _lkFree);
                if (_lkFree)
                    ConstructRender.AimPart = -1;
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
                        : ConstructRoutes.OpenPath(rc, _lkType, _lkStyle, _lkFrom, _lkFromPort, _lkParent, _lkParentSeg, _lkJunction, _lkNodes.AsSpan(0, _lkCount), next, hit.Part);
                }
                _lkWhy = _lkCheckWhy;
                RouteRender.PreviewPts[n0] = next;
                RouteRender.PreviewSeg[n0++] = rc.Parts[hit.Part].Seg;
            }
            RouteRender.PreviewCount = n0;
            RouteRender.PreviewOk = _lkWhy == null;
            if (fire && aimed && hit.Body == rc && !onRoute)
            {
                if (_lkWhy != null && endPart < 0 && _lkCount < Construct.MaxNodesPerRoute)
                    LinkAdjustStart(g, rc, LinkAdjust.Node, next, nrm, (byte)hit.Part, _lkWhy); // (hold to adjust the red node)
                else if (_lkWhy != null && endPart >= 0 && ports > 0 && _lkCount > 0)
                {
                    // (hold to adjust the input end: its last node moves along the normal it was placed on)
                    _lkAdjEnd = endPart;
                    _lkAdjEndPort = endPort;
                    _lkAdjPorts = ports;
                    LinkAdjustStart(g, rc, LinkAdjust.End, _lkNodes[_lkCount - 1].Pos, _lkNormals[_lkCount - 1], _lkNodes[_lkCount - 1].Host, _lkWhy);
                }
                else if (_lkWhy != null)
                    g.SandboxSay(_lkWhy);
                else if (endPart >= 0)
                {
                    if (ports > 1)
                        OpenPicker(rc, endPart, end: true);
                    else
                        LinkFinish(g, rc, endPart, endPort);
                }
                else
                {
                    _lkNormals[_lkCount] = nrm;
                    _lkNodes[_lkCount++] = new CRouteNode { Pos = next, Host = (byte)hit.Part };
                }
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
                    g.SandboxSay($"that is a {ConstructRoutes.Name(r.Type, r.Style)}: pick its type on the link wheel to branch off it");
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
                  ^ ((long)(_lkWhy?.GetHashCode() ?? 0) << 20) ^ (_lkMoveRoute >= 0 ? 1L << 51 : 0) ^ ((long)_lkStyle << 52) ^ ((long)g.SandboxWheelKey.GetHashCode() << 32)
                  ^ (_lkFree ? 1L << 53 : 0) ^ ((long)_lkAdj << 54) ^ ((long)(_lkAdjCm & 0xff) << 40) ^ ((long)_settings.Bind(GameAction.FreeRouting) << 8);
        if (key == _lkHudKey)
        {
            g.SandboxWireFrom = _lkHud;
            return;
        }
        _lkHudKey = key;
        var type = ConstructRoutes.Name(_lkBody != 0 ? _lkType : w.Type, _lkBody != 0 ? _lkStyle : w.Type == RouteType.Pipe && w.Style != 255 ? w.Style : (byte)0);
        if (_lkBody == 0 && w.Type == RouteType.Pipe && w.Style == 255)
            type = "pipe (style from the output)";
        var free = Keybinds.NameOf(_settings, GameAction.FreeRouting);
        var why = _lkWhy != null ? $" | {_lkWhy}" : "";
        _lkHud = _lkAdj != LinkAdjust.None
            ? $"Adjusting the node {(_lkAdjCm > 0 ? "+" : "")}{_lkAdjCm} cm: scroll to move it (Ctrl: 5 cm). Let go of LMB on green to place it (on red: nothing). RMB: cancel{why}"
            : w.Mode != LinkMode.Link
                ? _lkMoveRoute >= 0 ? $"Move node: LMB where it goes (red: hold LMB to adjust). RMB: let go{why}" : $"Linking tool: {w.ModeName} - LMB on a link. Hold {g.SandboxWheelKey}: the link wheel"
                : _lkBody == 0
                    ? $"Linking tool: {type}{(w.Highlight ? " (highlight all)" : "")} - LMB an output (or a link to branch). RMB: cut a part's wires. R: setting. Hold {g.SandboxWheelKey}: link wheel"
                    : _lkFree
                        ? $"Free routing (no port snap): LMB places a node, on a device too. Let go of {free} to end at an input{why}"
                        : $"Routing a {type}, {_lkCount} node{(_lkCount == 1 ? "" : "s")}: LMB a node or the input (red: hold LMB to adjust). {free}: free routing (no port snap). RMB/Backspace: last node off. Esc: cancel{why}";
        g.SandboxWireFrom = _lkHud;
    }
}
