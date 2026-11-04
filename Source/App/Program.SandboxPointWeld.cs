using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's point weld on this PC (board item dev-point-weld-pw01; a mode on the welder's outer ring, SandboxWeldModes;
/// the rules PointWeld.cs and SocketWeld.cs, the steps SocketWeldFlow with a face point as the source). Step 1: LMB on the
/// piece to move (up to SocketWeld.PullReach away): the point under the crosshair and the surface's normal there are the
/// weld's origin (an orange marker). Step 2: LMB on the target (another body, in reach): a live ghost puts the origin on the
/// point under the crosshair, its normal facing the target's (face to face), green or red with the reason; a red one goes on
/// too. Step 3: mouse X or the wheel spins it about the target normal (15 degree snaps, Shift free, Ctrl 5 degrees), R flips
/// it half a turn, LMB welds once it is green. RMB steps back, Esc cancels,
/// putting the welder away or another mode clears it. The server decides (CActKind.PointWeld, ConstructEdits.BeginPointWeld).
/// What the flow holds (body, part, its def and place) is stamped when picked and checked against the construct every frame.
/// Nothing allocates per frame: the look line is remade only when what it says changes.
/// </summary>
public static partial class Program
{
    private static readonly SocketWeldFlow _pwFlow = new();
    private static int _pwSrcDef = -1, _pwDstDef = -1; // the stamps: the picked parts' kinds and places
    private static Vector3 _pwSrcPos, _pwDstPos;
    private static long _pwKey = long.MinValue;
    private static string? _pwWhy, _pwReach;
    private static string _pwText = "";

    /// <summary>The point weld is set up past step 1 (RMB steps back, Esc cancels).</summary>
    private static bool PointWeldBusy => _pwFlow.Step != SocketWeldFlow.Stage.Source;

    private static bool PointWeldSpinning() =>
        _pwFlow.Step == SocketWeldFlow.Stage.Rotate && _mouseCaptured && !MenusOpen && _game is { SandboxOn: true } g && g.Local is { Alive: true } me
        && g.SandboxHands.Hand(me.Id).Tool == ConstructTool.Welder && _weldModes.Point;

    /// <summary>Before the mouse look: in step 3, mouse X spins the piece (the view holds still).</summary>
    private static bool PointWeldMouse(double2 delta)
    {
        if (!PointWeldSpinning())
            return false;
        PointWeldSpin((float)delta.X * SocketWeldDegreesPerPixel);
        return true;
    }

    /// <summary>The wheel in step 3: a notch is a snap step.</summary>
    private static bool PointWeldScroll(double y)
    {
        if (!PointWeldSpinning())
            return false;
        PointWeldSpin((float)y * SocketWeldDegreesPerNotch);
        return true;
    }

    private static void PointWeldSpin(float degrees)
    {
        if (_pwFlow.Spin(degrees, Down(GameAction.Sprint), CtrlHeld) && _game != null)
            BuildSounds.Play(BuildSound.Rotate, default, SocketWeld.OnGrid(_pwFlow.Angle) ? 1f : 0.4f);
    }

    /// <summary>Esc while a point weld is being set up: it is cancelled (true), not the match.</summary>
    private static bool PointWeldEsc()
    {
        if (!PointWeldBusy)
            return false;
        _pwFlow.Cancel();
        return true;
    }

    /// <summary>The welder put away or another mode: the point weld being set up goes.</summary>
    private static void PointWeldIdle()
    {
        if (_pwFlow.Step != SocketWeldFlow.Stage.Source || _pwFlow.Source != 0)
            _pwFlow.Cancel();
        SocketWeldRender.Clear();
    }

    /// <summary>Is what step 1 picked still there as it was (the part, its kind and place on the main body)?</summary>
    private static bool PointSourceLive(Construct? mover) =>
        mover != null && _pwFlow.SourcePart >= 0 && _pwFlow.SourcePart < mover.PartCount && mover.Parts[_pwFlow.SourcePart].Seg == 0
        && mover.Parts[_pwFlow.SourcePart].Def == _pwSrcDef && mover.Parts[_pwFlow.SourcePart].Pos == _pwSrcPos && !mover.Parts[_pwFlow.SourcePart].Broken;

    private static bool PointTargetLive(Construct? target, in WeldTarget w) =>
        target != null && w.Part < target.PartCount && target.Parts[w.Part].Def == _pwDstDef && target.Parts[w.Part].Pos == _pwDstPos;

    /// <summary>Every frame with the welder out in point-weld mode and nothing in the hands (SandboxInput).</summary>
    private static void PointWeldInput(Game g, Vector3 eye, Vector3 dir, bool aimed, ConstructHit hit)
    {
        var flow = _pwFlow;
        var world = g.Constructs;
        SocketWeldRender.Clear();
        var mover = flow.Source != 0 ? world.Get(flow.Source) : null;
        if (flow.Step != SocketWeldFlow.Stage.Source && !PointSourceLive(mover))
        {
            flow.Cancel(); // (the piece is gone, or changed under it)
            mover = null;
        }
        var picking = flow.Step == SocketWeldFlow.Stage.Source;
        if (picking)
            aimed = WeldFarHit(g, eye, dir, aimed, ref hit);
        var reachHit = aimed && hit.Distance <= WeldAimReach(picking) && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0
                       && hit.Part >= 0 && hit.Part < hit.Body.PartCount;
        if (flow.Step != SocketWeldFlow.Stage.Source && Pressed(GameAction.Aim))
        {
            flow.Back();
            _down[(int)GameAction.Aim] = false; // (not also cutting a part free)
            _pwKey = long.MinValue;
        }
        if (mover != null && flow.Step != SocketWeldFlow.Stage.Source)
        {
            // The origin's marker (orange), where it is now.
            mover.WorldFrame(0, out var q, out var t);
            SocketWeldRender.PointOn = true;
            SocketWeldRender.PointAt = t + Vector3.Transform(flow.SourceAt, q);
            SocketWeldRender.PointDir = Vector3.Transform(ConstructParts.DirVector(flow.SourceDir), q);
        }
        switch (flow.Step)
        {
            case SocketWeldFlow.Stage.Source:
            {
                g.SandboxWeldHint = SandboxKeys.PointWeld1;
                if (!reachHit)
                    return;
                var body = hit.Body;
                if (!PointWeld.OriginAt(body, hit.Part, hit.Point, hit.Normal, out var src))
                {
                    if (NewLook(20, body.Id, hit.Part, 0, 0))
                        _pwText = body.Parts[hit.Part].Seg != 0 ? "On a joint: pick the piece's main body" : "Aim at a flat face";
                    g.SandboxLookText = _pwText;
                    return;
                }
                body.WorldFrame(0, out var q, out var t);
                SocketWeldRender.PointOn = true;
                SocketWeldRender.PointAt = t + Vector3.Transform(src.At, q);
                SocketWeldRender.PointDir = Vector3.Transform(ConstructParts.DirVector(src.Dir), q);
                var why = WeldPieceWhy(g, body);
                if (NewLook(21, body.Id, hit.Part, why == null ? 0 : 1, body.PartCount))
                    _pwText = why ?? SandboxKeys.Line(body.DefOf(hit.Part).Name, null, null, SandboxKeys.Do(SandboxKeys.Fire, "pick this point"));
                g.SandboxLookText = _pwText;
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    if (why != null)
                        g.SandboxSay(why);
                    else
                    {
                        flow.PickPoint(src);
                        _pwSrcDef = body.Parts[hit.Part].Def;
                        _pwSrcPos = body.Parts[hit.Part].Pos;
                        _pwKey = long.MinValue;
                        BuildSounds.Play(BuildSound.Snap, default);
                    }
                }
                return;
            }
            case SocketWeldFlow.Stage.Target:
            {
                g.SandboxWeldHint = SandboxKeys.PointWeld2;
                WeldTarget w = default;
                var have = reachHit && hit.Body.Id != flow.Source && SocketWeld.FaceAt(hit.Body, hit.Part, hit.Point, hit.Normal, out w);
                if (!have || world.Get(w.Body) is not { } target)
                {
                    if (NewLook(22, flow.Source, flow.SourcePart, reachHit && hit.Body.Id == flow.Source ? 1 : 0, 0))
                        _pwText = reachHit && hit.Body.Id == flow.Source
                            ? "That's the piece itself: aim at the construct"
                            : "Aim at where it goes";
                    g.SandboxLookText = _pwText;
                    return;
                }
                flow.Aim(target, w, mover!, null);
                if (PointWeldPreview(g, mover!, target, w, flow.Angle) && Pressed(GameAction.Fire) && !_swallowClick)
                {
                    // (a red first guess goes on too: step 3 is where it is put right, and only its confirm needs a valid pose)
                    flow.PickTarget(target, w, mover!, null);
                    _pwDstDef = target.Parts[w.Part].Def;
                    _pwDstPos = target.Parts[w.Part].Pos;
                    BuildSounds.Play(BuildSound.Snap, default);
                }
                return;
            }
            case SocketWeldFlow.Stage.Rotate:
            {
                var target = world.Get(flow.Target.Body);
                if (!PointTargetLive(target, flow.Target))
                {
                    flow.Back(); // (the target changed under it: pick it again)
                    return;
                }
                if (Pressed(GameAction.Reload))
                    flow.NextVariant(mover!, target!);
                g.SandboxWeldHint = SandboxKeys.PointWeld3;
                PointWeldPreview(g, mover!, target!, flow.Target, flow.Angle);
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    if ((_pwReach ?? _pwWhy) is { } why)
                    {
                        g.SandboxSay(why);
                        return;
                    }
                    WeldPull(g, mover!, flow);
                    g.SandboxSendPointWeld(flow.Src, flow.Target, SocketWeld.FromWire(SocketWeld.WireAngle(flow.Angle)));
                    _sbFiring = !g.SandboxInfiniteParts; // (survival: a timed weld; letting go stops it)
                    flow.Cancel();
                }
                return;
            }
        }
    }

    /// <summary>The ghost at the pose (sockets lined up taken as the server will: PointWeld.Resolve), green or red with the reason. False: there is no pose to show.</summary>
    private static bool PointWeldPreview(Game g, Construct mover, Construct target, in WeldTarget w0, float degrees)
    {
        var src = _pwFlow.Src;
        var w = w0;
        var angle = SocketWeld.FromWire(SocketWeld.WireAngle(degrees));
        var snapped = PointWeld.Resolve(mover, ref src, target, ref w, ref angle);
        if (!SocketWeld.Pose(mover, src, target, w, angle, out _, out _, out var pos, out var rot))
            return false;
        SocketWeldRender.TargetOn = true;
        if (SocketWeld.TargetFrame(target, w0, out var tAt0, out var tDir0))
        {
            target.WorldFrame(target.Parts[w0.Part].Seg, out var q0, out var t0);
            SocketWeldRender.TargetAt = t0 + Vector3.Transform(tAt0, q0);
            SocketWeldRender.TargetDir = Vector3.Transform(ConstructParts.DirVector(tDir0), q0);
        }
        var at = target.Pos;
        var key = ((((long)mover.Id * 65599 + target.Id) * 257 + w0.Part) * 257 + (snapped ? 1 : 0)) * 40009 + SocketWeld.WireAngle(angle);
        key = key * 1_000_003 + (long)(w0.At.X * 1000) * 7 + (long)(w0.At.Y * 1000) * 131 + (long)(w0.At.Z * 1000) * 1031 + w0.Dir;
        key = key * 31 + mover.PartCount * 1009 + target.PartCount + (long)MathF.Round(at.X * 50) * 17 + (long)MathF.Round(at.Y * 50) * 89 + (long)MathF.Round(at.Z * 50) * 263;
        key = key * 7 + (long)_pwFlow.Step;
        if (key != _pwKey)
        {
            _pwKey = key;
            var rule = SocketWeld.Check(g.Constructs, World.Query, mover, src, target, w, angle, ReadOnlySpan<Vector3>.Empty);
            _pwWhy = rule ?? (ConstructEdits.SalvageRefusal(mover, g.LocalId) != null || ConstructEdits.SalvageRefusal(target, g.LocalId) != null
                ? "that construct is someone else's" : null);
            var grid = SocketWeld.OnRotGrid(mover, src, target, w, angle);
            var how = grid ? "lines up sockets: on the grid" : snapped ? "lines up sockets, freeform" : "freeform";
            if (snapped && !src.Face && mover.DefOf(src.Part).Sockets[src.Socket].Kind == SocketKind.Axle && grid)
                how = "on the axle: it rolls";
            else if (!grid && SocketWeld.Rolling(mover))
                how += ": a wheel on it is fixed and won't roll";
            if (SocketWeld.OnLeaf(target, w))
                how += ", on the moving side";
            var spinning = _pwFlow.Step == SocketWeldFlow.Stage.Rotate;
            var noFit = spinning && _pwWhy != null && (rule == null || WeldNoFit(g, _pwFlow, mover, _pwFlow.Src, target, w0, point: true));
            _pwText = !spinning ? WeldLine(target.DefOf(w0.Part).Name, how, _pwWhy, false, false, "put it here")
                : WeldLine($"{angle:0.#} deg", how, _pwWhy, true, noFit, g.SandboxInfiniteParts ? "weld" : $"weld ({ConstructBuild.AttachSeconds(mover.DefOf(_pwFlow.SourcePart)):0.#} s)");
        }
        _pwReach = WeldReachNow(g, mover, _pwFlow.Src, target, w0);
        g.SandboxLookText = _pwReach ?? _pwText;
        SocketWeldRender.Ghost = mover;
        SocketWeldRender.GhostPos = pos;
        SocketWeldRender.GhostRot = rot;
        SocketWeldRender.Valid = (_pwReach ?? _pwWhy) == null;
        SocketWeldRender.Spinning = _pwFlow.Step == SocketWeldFlow.Stage.Rotate;
        SocketWeldRender.Angle = angle;
        if (SocketWeld.TargetFrame(target, w, out var tAt, out var tDir))
        {
            target.WorldFrame(target.Parts[w.Part].Seg, out var q, out var t);
            SocketWeldRender.AxisAt = t + Vector3.Transform(tAt, q);
            SocketWeldRender.Axis = Vector3.Transform(ConstructParts.DirVector(tDir), q);
            SocketWeldRender.AxisRef = Vector3.Transform(ConstructParts.DirVector(SocketWeld.PerpDir(tDir)), q);
            WeldTether(mover, _pwFlow.Src);
        }
        return true;
    }

    // ---- The self-test's handles -------------------------------------------------------------------------------------

    internal static SocketWeldFlow PointWeldTestFlow => _pwFlow;
    internal static string? PointWeldTestWhy => _pwWhy;
    internal static string PointWeldTestText => _pwText;
    internal static bool PointWeldTestEsc() => PointWeldEsc();
}
