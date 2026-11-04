using System.Numerics;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's three-step socket weld on this PC (board item dev-sandbox-socket-weld-3step-sw01, decision 26; the default
/// welder mode, SandboxWeldModes; the steps are SocketWeldFlow, the rules SocketWeld.cs): socket markers on the aimed body
/// and the bodies near the crosshair; LMB on a socket of the piece to move (up to SocketWeld.PullReach away), LMB on a
/// socket (or a flat face) of the construct, in reach, even with the ghost red, then mouse X or the wheel spins the ghost
/// about the socket axis (15 degree snaps with a tick on 90, Shift free, Ctrl 5 degrees, R the next facing) and LMB welds it
/// once it is green. RMB steps back, Esc cancels. The server checks the final pose (ConstructEditsSocketWeld.cs). What the
/// flow holds is stamped when picked and checked every frame. Nothing allocates per frame: the look line is remade only
/// when what it says changes.
/// </summary>
public static partial class Program
{
    private static readonly SocketWeldFlow _swFlow = new();
    private static long _swKey = long.MinValue;
    private static string? _swWhy, _swReach;
    private static int _swSrcDef = -1, _swDstDef = -1; // the stamps: the picked parts' kinds and places
    private static Vector3 _swSrcPos, _swDstPos;
    private static string _swText = "";
    private const float SocketWeldDegreesPerPixel = 0.35f;
    private const float SocketWeldDegreesPerNotch = 15f;

    /// <summary>The socket weld is set up past step 1 (the view is the flow's: RMB steps back, Esc cancels).</summary>
    private static bool SocketWeldBusy => _swFlow.Step != SocketWeldFlow.Stage.Source;

    private static bool SocketWeldSpinning() =>
        _swFlow.Step == SocketWeldFlow.Stage.Rotate && _mouseCaptured && !MenusOpen && _game is { SandboxOn: true } g && g.Local is { Alive: true } me
        && g.SandboxHands.Hand(me.Id).Tool == ConstructTool.Welder && _weldModes.Mode == SandboxWeldModes.Option.Socket;

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
        var ctrl = CtrlHeld;
        if (_swFlow.Spin(degrees, Down(GameAction.Sprint), ctrl) && _game != null)
            BuildSounds.Play(BuildSound.Rotate, default, SocketWeld.OnGrid(_swFlow.Angle) ? 1f : 0.4f); // (the click on a 90 degree step)
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
    private static void SocketWeldInput(Game g, Vector3 eye, Vector3 dir, bool aimed, ConstructHit hit)
    {
        var flow = _swFlow;
        var world = g.Constructs;
        SocketWeldRender.Clear();
        var mover = flow.Source != 0 ? world.Get(flow.Source) : null;
        if (flow.Step != SocketWeldFlow.Stage.Source && !PartLive(mover, flow.SourcePart, _swSrcDef, _swSrcPos))
        {
            flow.Cancel(); // (the piece is gone, or changed under it)
            mover = null;
        }
        var picking = flow.Step == SocketWeldFlow.Stage.Source;
        if (picking)
            aimed = WeldFarHit(g, eye, dir, aimed, ref hit);
        var aimReach = WeldAimReach(picking);
        var reachHit = aimed && hit.Distance <= aimReach && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0 && hit.Part >= 0 && hit.Part < hit.Body.PartCount;
        var n = SocketWeld.Markers(world, eye, dir, reachHit ? hit.Body : null, SocketWeldRender.Markers, flow.Source, flow.SourcePart, flow.SourceSocket,
            targets: !picking, range: MathF.Max(SocketWeld.ShowRange, aimReach));
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
                        _swText = "Aim at a socket marker";
                    if (reachHit)
                        g.SandboxLookText = _swText;
                    g.SandboxWeldHint = SandboxKeys.SocketWeld1;
                    return;
                }
                ref var m = ref marks[k];
                SocketWeldRender.Hot = k;
                var body = world.Get(m.Body)!;
                var why = WeldPieceWhy(g, body);
                if (NewLook(11, m.Body, m.Part, m.Socket, body.PartCount * 2 + (why == null ? 0 : 1)))
                    _swText = why ?? SandboxKeys.Line(body.DefOf(m.Part).Name, body.DefOf(m.Part).Sockets[m.Socket].Name, null, SandboxKeys.Do(SandboxKeys.Fire, "pick") + ", " + SandboxKeys.Do(SandboxKeys.Sprint, "point attach"));
                g.SandboxLookText = _swText;
                g.SandboxWeldHint = SandboxKeys.SocketWeld1;
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    if (why != null)
                        g.SandboxSay(why);
                    else
                    {
                        flow.PickSource(m.Body, m.Part, m.Socket);
                        _swSrcDef = body.Parts[m.Part].Def;
                        _swSrcPos = body.Parts[m.Part].Pos;
                        _swKey = long.MinValue;
                        BuildSounds.Play(BuildSound.Snap, default);
                    }
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
                g.SandboxWeldHint = SandboxKeys.SocketWeld2;
                if (!have || world.Get(w.Body) is not { } target)
                {
                    if (NewLook(12, flow.Source, flow.SourcePart, flow.SourceSocket, 0))
                        _swText = "Aim at a socket or flat face on the construct";
                    g.SandboxLookText = _swText;
                    return;
                }
                flow.Aim(target, w, mover!, SmartRotation.Memory);
                if (SocketWeldPreview(g, mover!, target, w, flow.StartAngle) && Pressed(GameAction.Fire) && !_swallowClick)
                {
                    // (a red first guess goes on too: step 3 is where it is put right, and only its confirm needs a valid pose)
                    flow.PickTarget(target, w, mover!, SmartRotation.Memory);
                    _swDstDef = target.Parts[w.Part].Def;
                    _swDstPos = target.Parts[w.Part].Pos;
                    BuildSounds.Play(BuildSound.Snap, default);
                }
                return;
            }
            case SocketWeldFlow.Stage.Rotate:
            {
                if (world.Get(flow.Target.Body) is not { } target || !PartLive(target, flow.Target.Part, _swDstDef, _swDstPos))
                {
                    flow.Back(); // (the target changed under it: pick it again)
                    return;
                }
                if (Pressed(GameAction.Reload))
                    flow.NextVariant(mover!, target);
                g.SandboxWeldHint = SandboxKeys.SocketWeld3;
                SocketWeldPreview(g, mover!, target, flow.Target, flow.Angle);
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    if ((_swReach ?? _swWhy) is { } why)
                    {
                        g.SandboxSay(why);
                        return;
                    }
                    var angle = SocketWeld.FromWire(SocketWeld.WireAngle(flow.Angle));
                    if (SocketWeld.Pose(mover!, flow.SourcePart, flow.SourceSocket, target, flow.Target, angle, out _, out var relRot, out _, out _))
                        SandboxLearn(flow.Learn(target, mover!, SmartRotation.Memory, relRot));
                    WeldPull(g, mover!, flow);
                    g.SandboxSendSocketWeld(mover!.Id, flow.SourcePart, flow.SourceSocket, flow.Target, angle);
                    _sbFiring = !g.SandboxInfiniteParts; // (survival: a timed weld; letting go stops it)
                    flow.Cancel();
                }
                return;
            }
        }
    }

    /// <summary>The ghost at the pose, green or red with the reason (the rules worked out again only when what they depend on changes). False: there is no pose to show.</summary>
    private static bool SocketWeldPreview(Game g, Construct mover, Construct target, in WeldTarget w, float degrees)
    {
        var angle = SocketWeld.FromWire(SocketWeld.WireAngle(degrees));
        if (!SocketWeld.Pose(mover, _swFlow.SourcePart, _swFlow.SourceSocket, target, w, angle, out _, out _, out var pos, out var rot))
            return false;
        var at = target.Pos;
        var key = ((((long)mover.Id * 65599 + target.Id) * 257 + w.Part) * 257 + w.Socket) * 40009 + SocketWeld.WireAngle(angle);
        key = key * 1_000_003 + (long)(w.At.X * 1000) * 7 + (long)(w.At.Y * 1000) * 131 + (long)(w.At.Z * 1000) * 1031 + w.Dir;
        key = key * 31 + mover.PartCount * 1009 + target.PartCount + (long)MathF.Round(at.X * 50) * 17 + (long)MathF.Round(at.Y * 50) * 89 + (long)MathF.Round(at.Z * 50) * 263;
        key = key * 7 + (long)_swFlow.Step;
        if (key != _swKey)
        {
            _swKey = key;
            var rule = SocketWeld.Check(g.Constructs, World.Query, mover, _swFlow.SourcePart, _swFlow.SourceSocket, target, w, angle, ReadOnlySpan<Vector3>.Empty);
            _swWhy = rule ?? (ConstructEdits.SalvageRefusal(mover, g.LocalId) != null || ConstructEdits.SalvageRefusal(target, g.LocalId) != null
                ? "that construct is someone else's" : null);
            var where = w.Face ? target.DefOf(w.Part).Name : $"{target.DefOf(w.Part).Name}, {target.DefOf(w.Part).Sockets[w.Socket].Name}";
            var grid = SocketWeld.OnRotGrid(mover, _swFlow.SourcePart, _swFlow.SourceSocket, target, w, angle);
            var how = grid ? "on the grid" : SocketWeld.Rolling(mover) ? "freeform: its wheel won't roll (attach it to a socket to roll)" : "freeform";
            if (SocketWeld.OnLeaf(target, w))
                how += ", on the moving side"; // (it hangs on the joint and moves with it: board item wh01)
            var spinning = _swFlow.Step == SocketWeldFlow.Stage.Rotate;
            var noFit = spinning && _swWhy != null && (rule == null || WeldNoFit(g, _swFlow, mover, _swFlow.Src, target, w, point: false));
            _swText = !spinning ? WeldLine(where, $"{angle:0} deg, {how}", _swWhy, false, false, "put it here")
                : WeldLine($"{angle:0.#} deg", how, _swWhy, true, noFit, g.SandboxInfiniteParts ? "attach" : $"attach ({ConstructBuild.AttachSeconds(mover.DefOf(_swFlow.SourcePart)):0.#} s)");
        }
        _swReach = WeldReachNow(g, mover, _swFlow.Src, target, w);
        g.SandboxLookText = _swReach ?? _swText;
        SocketWeldRender.Ghost = mover;
        SocketWeldRender.GhostPos = pos;
        SocketWeldRender.GhostRot = rot;
        SocketWeldRender.Valid = (_swReach ?? _swWhy) == null;
        SocketWeldRender.Spinning = _swFlow.Step == SocketWeldFlow.Stage.Rotate;
        SocketWeldRender.Angle = angle;
        if (SocketWeld.TargetFrame(target, w, out var tAt, out var tDir))
        {
            target.WorldFrame(target.Parts[w.Part].Seg, out var q, out var t);
            SocketWeldRender.AxisAt = t + Vector3.Transform(tAt, q);
            SocketWeldRender.Axis = Vector3.Transform(ConstructParts.DirVector(tDir), q);
            SocketWeldRender.AxisRef = Vector3.Transform(ConstructParts.DirVector(SocketWeld.PerpDir(tDir)), q);
            WeldTether(mover, _swFlow.Src);
        }
        return true;
    }

    // ---- What the socket and point welds share ---------------------------------------------------------------------

    /// <summary>Is the part a flow picked still there as it was (a part taken off shifts the indices)?</summary>
    private static bool PartLive(Construct? c, int part, int def, Vector3 pos) =>
        c != null && part >= 0 && part < c.PartCount && c.Parts[part].Def == def && c.Parts[part].Pos == pos;

    /// <summary>How far the crosshair picks: the piece from SocketWeld.PullReach; a target within the usual reach; the Build Gantry's camera as far as it sees.</summary>
    private static float WeldAimReach(bool piece) =>
        piece ? MathF.Max(SocketWeld.PullReach, GantryOn ? GantryMaxDist() + _gDist : 0)
            : GantryOn ? GantryMaxDist() + _gDist : ConstructBuild.Reach + 1f;

    /// <summary>Step 1: the look ray stops at the usual reach, so a crosshair on nothing looks on to the piece's reach (through the frame like any aim). This frame's aim, or the far one.</summary>
    private static bool WeldFarHit(Game g, Vector3 eye, Vector3 dir, bool aimed, ref ConstructHit hit)
    {
        if (aimed)
            return true;
        var reach = WeldAimReach(true);
        return g.Constructs.Raycast(eye, dir, MathF.Min(World.RaycastWorld(eye, dir, reach), reach), out hit, look: true);
    }

    /// <summary>What step 1 says at once about a piece (no target or angle would help). Null: it may be picked.</summary>
    private static string? WeldPieceWhy(Game g, Construct piece) =>
        SocketWeld.PieceRefusal(piece) ?? (ConstructEdits.SalvageRefusal(piece, g.LocalId) != null ? "that construct is someone else's" : null);

    /// <summary>The server's reach as this PC sees it, every frame (walking away turns the ghost red). With the Build Gantry's camera the server decides: the station's place is its own.</summary>
    private static string? WeldReachNow(Game g, Construct mover, in WeldTarget src, Construct target, in WeldTarget w) =>
        !GantryOn && g.Local is { } me ? ConstructEdits.WeldReach(me.RenderEye, mover, src, target, w) : null;

    /// <summary>The look line of a ghost: green, the action; red before step 3, the action still (the spin comes next); red in step 3, what to do about it.</summary>
    private static string WeldLine(string name, string state, string? why, bool spinning, bool noFit, string action) =>
        why == null ? SandboxKeys.Line(name, state, null, SandboxKeys.Do(SandboxKeys.Fire, action))
        : !spinning ? SandboxKeys.Line(name, state, why, SandboxKeys.Do(SandboxKeys.Fire, "put it here, then spin it"))
        : noFit ? SandboxKeys.Line(name, state, why, "no angle fits here - " + SandboxKeys.Do(SandboxKeys.Aim, "another spot"))
        : SandboxKeys.Line(name, state, why, "spin it until it fits");

    /// <summary>Step 3 in the red: does no angle fit? Worked out at most four times a second, and only when the target changed: up to 24 full checks.</summary>
    private static bool WeldNoFit(Game g, SocketWeldFlow flow, Construct mover, in WeldTarget src, Construct target, in WeldTarget w, bool point)
    {
        var key = ((long)mover.Id * 65599 + target.Id) * 40009 + mover.PartCount * 1009 + target.PartCount;
        key = key * 31 + (long)MathF.Round(target.Pos.X) * 17 + (long)MathF.Round(target.Pos.Y) * 89 + (long)MathF.Round(target.Pos.Z) * 263;
        var clock = Environment.TickCount64;
        if (key == flow.FitKey || clock < flow.FitAt)
            return flow.NoFit;
        flow.FitKey = key;
        flow.FitAt = clock + 250;
        return flow.NoFit = !SocketWeld.AnyAngleFits(g.Constructs, World.Query, mover, src, target, w, point);
    }

    /// <summary>A thin line from a piece that isn't next to the ghost to where it goes (AxisAt, the frame's target point): which piece is coming, wherever it lies.</summary>
    private static void WeldTether(Construct mover, in WeldTarget src)
    {
        if (!SocketWeld.SourceFrame(mover, src, out var sAt, out _))
            return;
        SocketWeldRender.TetherA = mover.ToWorld(sAt);
        SocketWeldRender.TetherB = SocketWeldRender.AxisAt;
        SocketWeldRender.TetherOn = Vector3.Distance(SocketWeldRender.TetherA, SocketWeldRender.TetherB) > SocketWeldRender.TetherFrom;
    }

    /// <summary>The weld is sent: the streak along the tether, on this PC (a timed weld's for its time; letting go stops it).</summary>
    private static void WeldPull(Game g, Construct mover, SocketWeldFlow flow)
    {
        if (SocketWeldRender.TetherOn)
            SocketWeldRender.Pull(SocketWeldRender.TetherA, SocketWeldRender.TetherB, g.SandboxInfiniteParts ? 0 : ConstructBuild.AttachSeconds(mover.DefOf(flow.SourcePart)));
    }
}
