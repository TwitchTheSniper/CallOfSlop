using System.Numerics;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's three-step socket weld on this PC (board item dev-sandbox-socket-weld-3step-sw01, decision 26; the default
/// welder mode, SandboxWeldModes; the steps are SocketWeldFlow, the rules SocketWeld.cs): socket markers on the aimed body
/// and the bodies near the crosshair; LMB on a socket of the piece to move, LMB on a socket (or a flat face) of the
/// construct, then mouse X or the wheel spins the ghost about the socket axis (15 degree snaps with a tick on 90, Shift
/// free, Ctrl 5 degrees, R the next facing) and LMB welds it. RMB steps back, Esc cancels. The server checks the final pose
/// (ConstructEditsSocketWeld.cs). Nothing allocates per frame: the look line is remade only when what it says changes.
/// </summary>
public static partial class Program
{
    private static readonly SocketWeldFlow _swFlow = new();
    private static long _swKey = long.MinValue;
    private static string? _swWhy;
    private static string _swText = "";
    private const float SocketWeldDegreesPerPixel = 0.35f;
    private const float SocketWeldDegreesPerNotch = 15f;

    /// <summary>The socket weld is set up past step 1 (the view is the flow's: RMB steps back, Esc cancels).</summary>
    private static bool SocketWeldBusy => _swFlow.Step != SocketWeldFlow.Stage.Source;

    private static bool SocketWeldSpinning() =>
        _swFlow.Step == SocketWeldFlow.Stage.Rotate && _mouseCaptured && !MenusOpen && _game is { SandboxOn: true } g && g.Local is { Alive: true } me
        && g.SandboxHands.Hand(me.Id).Tool == ConstructTool.Welder && !_weldModes.Freeform;

    /// <summary>Before the mouse look: in step 3, mouse X spins the piece (the view holds still).</summary>
    private static bool SocketWeldMouse(double2 delta)
    {
        if (!SocketWeldSpinning())
            return false;
        SocketWeldSpin((float)delta.X * SocketWeldDegreesPerPixel);
        return true;
    }

    /// <summary>The wheel in step 3: a notch is a snap step.</summary>
    private static bool SocketWeldScroll(double y)
    {
        if (!SocketWeldSpinning())
            return false;
        SocketWeldSpin((float)y * SocketWeldDegreesPerNotch);
        return true;
    }

    private static void SocketWeldSpin(float degrees)
    {
        var ctrl = _window.GetKey(GlfwKey.LeftControl) == GlfwKeyAction.Press || _window.GetKey(GlfwKey.RightControl) == GlfwKeyAction.Press;
        if (_swFlow.Spin(degrees, Down(GameAction.Sprint), ctrl) && _game != null)
            _game.Audio.Effect(Sfx.HitTick, SocketWeld.OnGrid(_swFlow.Angle) ? 0.22f : 0.08f); // (the click on a 90 degree step)
    }

    /// <summary>Esc while a socket weld is being set up: it is cancelled (true), not the match.</summary>
    private static bool SocketWeldEsc()
    {
        if (!SocketWeldBusy)
            return false;
        _swFlow.Cancel();
        return true;
    }

    /// <summary>The welder put away or switched to freeform: the socket weld being set up goes.</summary>
    private static void SocketWeldIdle()
    {
        if (_swFlow.Step != SocketWeldFlow.Stage.Source || _swFlow.Source != 0)
            _swFlow.Cancel();
        SocketWeldRender.Clear();
    }

    /// <summary>Every frame with the welder out in socket-weld mode and nothing in the hands (SandboxInput).</summary>
    private static void SocketWeldInput(Game g, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit)
    {
        var flow = _swFlow;
        var world = g.Constructs;
        SocketWeldRender.Clear();
        var mover = flow.Source != 0 ? world.Get(flow.Source) : null;
        if (flow.Step != SocketWeldFlow.Stage.Source && (mover == null || flow.SourcePart >= mover.PartCount))
        {
            flow.Cancel(); // (the piece is gone, or changed under it)
            mover = null;
        }
        var reachHit = aimed && hit.Distance <= ConstructBuild.Reach + 1f && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0;
        var n = SocketWeld.Markers(world, eye, dir, reachHit ? hit.Body : null, SocketWeldRender.Markers, flow.Source, flow.SourcePart, flow.SourceSocket, targets: flow.Step != SocketWeldFlow.Stage.Source);
        SocketWeldRender.MarkerCount = n;
        var marks = SocketWeldRender.Markers.AsSpan(0, n);
        // RMB: one step back (step 1: RMB is the welder's usual cut free).
        if (flow.Step != SocketWeldFlow.Stage.Source && Pressed(GameAction.Aim))
        {
            flow.Back();
            _down[(int)GameAction.Aim] = false; // (not also cutting a part free)
            _swKey = long.MinValue;
        }
        switch (flow.Step)
        {
            case SocketWeldFlow.Stage.Source:
            {
                var k = SocketWeld.PickMarker(marks, onlyBody: reachHit ? hit.Body.Id : (ushort)0);
                if (k < 0)
                {
                    if (reachHit && NewLook(10, hit.Body.Id, hit.Part, 0, 0))
                        _swText = $"Socket weld: aim at a socket marker on the {hit.Body.DefOf(hit.Part).Name} to move it (LMB picks it)";
                    if (reachHit)
                        g.SandboxLookText = _swText;
                    g.SandboxWeldHint = "Socket weld 1/3: LMB a socket on the piece to move (a loose body or a separate construct). Outer ring: freeform";
                    return;
                }
                ref var m = ref marks[k];
                SocketWeldRender.Hot = k;
                var body = world.Get(m.Body)!;
                if (NewLook(11, m.Body, m.Part, m.Socket, body.PartCount))
                    _swText = $"Socket weld: LMB picks the {body.DefOf(m.Part).Name}'s {body.DefOf(m.Part).Sockets[m.Socket].Name} socket to move it by";
                g.SandboxLookText = _swText;
                g.SandboxWeldHint = "Socket weld 1/3: LMB picks this socket. Then pick where it goes";
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    flow.PickSource(m.Body, m.Part, m.Socket);
                    g.Audio.Effect(Sfx.HitTick, 0.16f);
                }
                return;
            }
            case SocketWeldFlow.Stage.Target:
            {
                // A socket marker on another body (the magnet), else a point on the flat face under the crosshair.
                var k = SocketWeld.PickMarker(marks, notBody: flow.Source);
                WeldTarget w = default;
                var have = false;
                if (k >= 0)
                {
                    ref var m = ref marks[k];
                    w = new WeldTarget { Body = m.Body, Part = m.Part, Socket = m.Socket };
                    have = true;
                    SocketWeldRender.Hot = k;
                }
                else if (reachHit && hit.Body.Id != flow.Source)
                    have = SocketWeld.FaceAt(hit.Body, hit.Part, hit.Point, hit.Normal, out w);
                g.SandboxWeldHint = "Socket weld 2/3: LMB a socket (or a flat face) on the construct. RMB: back, Esc: cancel";
                if (!have || world.Get(w.Body) is not { } target)
                {
                    if (NewLook(12, flow.Source, flow.SourcePart, flow.SourceSocket, 0))
                        _swText = $"Socket weld: the {mover!.DefOf(flow.SourcePart).Name} is picked - now aim at a socket or a flat face on the construct to weld it to";
                    g.SandboxLookText = _swText;
                    return;
                }
                flow.Aim(target, w, mover!, SmartRotation.Memory);
                SocketWeldPreview(g, mover!, target, w, flow.StartAngle);
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    if (_swWhy != null)
                        g.SandboxSay(_swWhy);
                    else
                    {
                        flow.PickTarget(target, w, mover!, SmartRotation.Memory);
                        g.Audio.Effect(Sfx.HitTick, 0.16f);
                    }
                }
                return;
            }
            case SocketWeldFlow.Stage.Rotate:
            {
                if (world.Get(flow.Target.Body) is not { } target || flow.Target.Part >= target.PartCount)
                {
                    flow.Back();
                    return;
                }
                if (Pressed(GameAction.Reload))
                    flow.NextVariant(mover!, target);
                g.SandboxWeldHint = "Socket weld 3/3: mouse or wheel spins it (15 deg; Shift free, Ctrl 5 deg), R: next facing, LMB: weld. RMB: back, Esc: cancel";
                SocketWeldPreview(g, mover!, target, flow.Target, flow.Angle);
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    if (_swWhy != null)
                    {
                        g.SandboxSay(_swWhy);
                        return;
                    }
                    var angle = SocketWeld.FromWire(SocketWeld.WireAngle(flow.Angle));
                    if (SocketWeld.Pose(mover!, flow.SourcePart, flow.SourceSocket, target, flow.Target, angle, out _, out var relRot, out _, out _))
                        SandboxLearn(flow.Learn(target, mover!, SmartRotation.Memory, relRot));
                    g.SandboxSendSocketWeld(mover!.Id, flow.SourcePart, flow.SourceSocket, flow.Target, angle);
                    _sbFiring = !g.SandboxInfiniteParts; // (survival: a timed weld; letting go stops it)
                    flow.Cancel();
                }
                return;
            }
        }
    }

    /// <summary>The ghost at the pose, green or red with the reason (the rules worked out again only when what they depend on changes).</summary>
    private static void SocketWeldPreview(Game g, Construct mover, Construct target, in WeldTarget w, float degrees)
    {
        var angle = SocketWeld.FromWire(SocketWeld.WireAngle(degrees));
        if (!SocketWeld.Pose(mover, _swFlow.SourcePart, _swFlow.SourceSocket, target, w, angle, out _, out _, out var pos, out var rot))
            return;
        var at = target.Pos;
        var key = ((((long)mover.Id * 65599 + target.Id) * 257 + w.Part) * 257 + w.Socket) * 40009 + SocketWeld.WireAngle(angle);
        key = key * 1_000_003 + (long)(w.At.X * 1000) * 7 + (long)(w.At.Y * 1000) * 131 + (long)(w.At.Z * 1000) * 1031 + w.Dir;
        key = key * 31 + mover.PartCount * 1009 + target.PartCount + (long)MathF.Round(at.X * 50) * 17 + (long)MathF.Round(at.Y * 50) * 89 + (long)MathF.Round(at.Z * 50) * 263;
        if (key != _swKey)
        {
            _swKey = key;
            _swWhy = SocketWeld.Check(g.Constructs, World.Query, mover, _swFlow.SourcePart, _swFlow.SourceSocket, target, w, angle, ReadOnlySpan<Vector3>.Empty)
                     ?? (ConstructEdits.SalvageRefusal(mover, g.LocalId) != null || ConstructEdits.SalvageRefusal(target, g.LocalId) != null
                         ? "that construct is someone else's: you can only weld your own (or wrecks and loose parts)" : null);
            var where = w.Face ? $"a point on the {target.DefOf(w.Part).Name}" : $"the {target.DefOf(w.Part).Name}'s {target.DefOf(w.Part).Sockets[w.Socket].Name} socket";
            var grid = SocketWeld.OnRotGrid(mover, _swFlow.SourcePart, _swFlow.SourceSocket, target, w, angle);
            var how = grid ? "on the grid" : SocketWeld.Rolling(mover) ? "freeform: a wheel on it is fixed and won't roll (weld onto a socket to roll)" : "freeform";
            _swText = _swWhy != null ? $"Socket weld: {_swWhy}"
                : _swFlow.Step == SocketWeldFlow.Stage.Target ? $"Socket weld: LMB puts it on {where} ({angle:0} deg, {how}), then spin it"
                : $"Socket weld: {angle:0.#} deg ({how}) - LMB welds it {(g.SandboxInfiniteParts ? "at once" : $"({ConstructBuild.AttachSeconds(mover.DefOf(_swFlow.SourcePart)):0.#} s)")}";
        }
        g.SandboxLookText = _swText;
        SocketWeldRender.Ghost = mover;
        SocketWeldRender.GhostPos = pos;
        SocketWeldRender.GhostRot = rot;
        SocketWeldRender.Valid = _swWhy == null;
        SocketWeldRender.Spinning = _swFlow.Step == SocketWeldFlow.Stage.Rotate;
        SocketWeldRender.Angle = angle;
        if (SocketWeld.TargetFrame(target, w, out var tAt, out var tDir))
        {
            target.WorldFrame(target.Parts[w.Part].Seg, out var q, out var t);
            SocketWeldRender.AxisAt = t + Vector3.Transform(tAt, q);
            SocketWeldRender.Axis = Vector3.Transform(ConstructParts.DirVector(tDir), q);
            SocketWeldRender.AxisRef = Vector3.Transform(ConstructParts.DirVector(SocketWeld.PerpDir(tDir)), q);
        }
    }
}
