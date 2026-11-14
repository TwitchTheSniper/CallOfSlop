using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Nudge tool on this PC (ConstructNudge.cs; docs/sandbox/CONSTRUCTS.md, "The Nudge tool"). Its target is what the Selector has
/// selected (every body with a part in it), else the construct clicked. LMB on an arrow or arc of the gizmo and drag: the
/// view holds still and the mouse's travel along the axis on screen, or round the arc, is the move or turn since the drag
/// began, on the step or snap angle (the mouse wheel, when not dragging, sets them). The modes are on the outer ring, R
/// toggles world or local, RMB or Esc during a drag puts it back, RMB otherwise lets the target go. The server moves the
/// bodies. What this holds of a construct is the body itself, checked against the world every frame; never a part index.
/// Nothing allocates per frame: the look line is remade only when what it says changes.
/// </summary>
public static partial class Program
{
    private static readonly SandboxNudgeModes _nudgeModes = new();
    private static readonly Construct[] _ndList = new Construct[Nudge.MaxBodies];
    private static int _ndCount, _ndDrag = -1;
    private static Construct? _ndClicked;
    private static Vector3 _ndPivot, _ndAxisNow;
    private static Quaternion _ndTurnNow = Quaternion.Identity;
    private static Vector3 _ndMoveNow;
    private static Vector2 _ndScreenDir, _ndMouse;
    private static float _ndPixels, _ndValue;
    private static long _ndKey = long.MinValue;
    private static string _ndText = "";
    private static readonly Vector3[] _ndAxes = new Vector3[3];
    private const float NudgeGrabSine = 0.022f, NudgeOnScreen = 0.16f;
    private static readonly char[] NudgeNames = ['X', 'Y', 'Z'];

    /// <summary>A drag is under way (the view holds still).</summary>
    private static bool NudgeDragging => _ndDrag >= 0;

    internal static SandboxNudgeModes SweepNudgeModes => _nudgeModes;

    /// <summary>Before the mouse look: a drag takes the mouse.</summary>
    private static bool NudgeMouse(double2 delta)
    {
        if (!NudgeDragging || !_mouseCaptured || MenusOpen)
            return false;
        _ndMouse += new Vector2((float)delta.X, (float)delta.Y);
        return true;
    }

    /// <summary>The wheel with the Nudge tool out: the step or snap angle.</summary>
    private static bool NudgeScroll(double y)
    {
        if (_game is not { SandboxOn: true } g || g.Local is not { Alive: true } me || g.SandboxHands.Hand(me.Id).Tool != ConstructTool.Nudge || MenusOpen)
            return false;
        if (!NudgeDragging)
            _nudgeModes.Scroll(y > 0 ? 1 : -1);
        return true;
    }

    /// <summary>Esc during a drag: it goes back to where it started (true), not the match.</summary>
    private static bool NudgeEsc()
    {
        if (!NudgeDragging || _game is not { } g)
            return false;
        NudgeCancel(g);
        return true;
    }

    private static void NudgeCancel(Game g)
    {
        g.SandboxNudgeEnd(Vector3.Zero, Quaternion.Identity);
        _ndDrag = -1;
    }

    /// <summary>The tool put away: a drag ends where it is.</summary>
    /// <summary>Every frame: the gizmo and any drag go the moment the Nudge tool isn't in hand, whatever took its place.</summary>
    private static void NudgePutAway()
    {
        if (_ndDrag < 0 && !NudgeRender.On && _ndClicked == null)
            return;
        if (_game is not { SandboxOn: true } g)
        {
            _ndDrag = -1;
            _ndClicked = null;
            NudgeRender.Clear();
        }
        else if (g.Local is not { Alive: true } me || g.SandboxHands.Hand(me.Id) is { Tool: not ConstructTool.Nudge } or { Seated: true })
            NudgeIdle(g);
    }

    private static void NudgeIdle(Game g)
    {
        if (NudgeDragging)
        {
            g.SandboxNudgeEnd(_ndMoveNow, _ndTurnNow);
            _ndDrag = -1;
        }
        _ndClicked = null;
        NudgeRender.Clear();
    }

    /// <summary>What the tool works on now: the Selector's bodies, else the one clicked. Bodies that went drop out.</summary>
    private static void NudgeTargets(Game g)
    {
        var world = g.Constructs;
        _ndCount = 0;
        var sel = _select.Selection;
        if (sel.Total > 0)
        {
            if (sel.Count > 0 && sel.Body is { } first && ReferenceEquals(world.Get(first.Id), first))
                _ndList[_ndCount++] = first;
            foreach (var e in sel.Extra)
                if (e.Count > 0 && e.Body is { } b && ReferenceEquals(world.Get(b.Id), b) && _ndCount < Nudge.MaxBodies)
                    _ndList[_ndCount++] = b;
        }
        else if (_ndClicked is { } c)
        {
            if (ReferenceEquals(world.Get(c.Id), c) && c.PartCount > 0)
                _ndList[_ndCount++] = c;
            else
                _ndClicked = null;
        }
    }

    /// <summary>How far the aim ray passes from a piece of the gizmo, as a share of the distance to it (a sine: the same on screen near or far).</summary>
    private static float NudgeGap(Vector3 eye, Vector3 dir, Vector3 a, Vector3 b)
    {
        var u = b - a;
        var w = a - eye;
        var uu = Vector3.Dot(u, u);
        var ud = Vector3.Dot(u, dir);
        var denom = uu - ud * ud;
        var t = denom > 1e-8f ? Math.Clamp((ud * Vector3.Dot(w, dir) - Vector3.Dot(w, u)) / denom, 0f, 1f) : 0f;
        var p = a + u * t - eye;
        var along = Vector3.Dot(p, dir);
        return along <= 0.05f ? float.MaxValue : (p - dir * along).Length() / along;
    }

    /// <summary>A world direction as the view sees it, a metre at distance <paramref name="dist"/>, in pixels (y down), so mouse travel along it is metres.</summary>
    private static Vector2 NudgeOnView(Vector3 v, Vector3 dir, float dist)
    {
        var right = Vector3.Cross(dir, Vector3.UnitY);
        right = right.LengthSquared() > 1e-4f ? Vector3.Normalize(right) : Vector3.UnitX;
        var up = Vector3.Cross(right, dir);
        return new Vector2(Vector3.Dot(v, right), -Vector3.Dot(v, up)) * (ScreenSize.Y * 0.9f / MathF.Max(dist, 0.5f));
    }

    /// <summary>Every frame with the Nudge tool out (SandboxInput).</summary>
    private static void SandboxNudgeInput(Game g, Player me, Vector3 eye, Vector3 dir, bool aimed, ConstructHit hit)
    {
        var world = g.Constructs;
        var hands = g.SandboxHands;
        NudgeRender.Clear();
        NudgeTargets(g);
        var range = hands.NudgeRange + 1f;
        if (!NudgeDragging && !aimed)
        {
            var wall = World.RaycastWorld(eye, dir, range);
            aimed = world.Raycast(eye, dir, MathF.Min(wall, range), out hit, look: true);
        }
        var aimOk = aimed && hit.Body != null && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0;
        if (_ndCount == 0)
        {
            if (NudgeDragging)
                NudgeCancel(g);
            g.SandboxWeldHint = "";
            if (!aimOk)
                return;
            var why = g.SandboxNudgeRefusal(hit.Body!, me.Id);
            if (NewLook(40, hit.Body!.Id, hit.Body.PartCount, why == null ? 0 : 1, 0))
                _ndText = why ?? SandboxKeys.Line(hit.Body.DefOf(hit.Part).Name, null, null, SandboxKeys.Do(SandboxKeys.Fire, "nudge it"));
            g.SandboxLookText = _ndText;
            if (Pressed(GameAction.Fire) && !_swallowClick)
            {
                if (why != null)
                    g.SandboxSay(why);
                else
                    _ndClicked = hit.Body;
            }
            return;
        }
        var c0 = _ndList[0];
        if (!NudgeDragging)
        {
            // The pivot: the one body's centre of mass, or the mass-weighted centre of them all, cargo in, as the server
            // works it out (ConstructEdits.NudgeCentre: the server turns about its own, this one draws and predicts).
            var sum = Vector3.Zero;
            var mass = 0f;
            for (var i = 0; i < _ndCount; i++)
                ConstructEdits.NudgeWeigh(g.Constructs, _ndList[i], ref sum, ref mass);
            _ndPivot = sum / mass;
            Nudge.Axes(ConstructGravity.GravityFor(c0, _ndPivot), _nudgeModes.Local, c0.Rot, out _ndAxes[0], out _ndAxes[1], out _ndAxes[2]);
        }
        var size = MathF.Max(Vector3.Distance(eye, _ndPivot), 0.5f) * NudgeOnScreen;
        NudgeRender.On = true;
        NudgeRender.Arcs = _nudgeModes.Rotating;
        NudgeRender.Size = size;
        NudgeRender.At = _ndPivot;
        for (var k = 0; k < 3; k++)
        {
            NudgeRender.Axis[k] = _ndAxes[k];
            NudgeRender.Ref[k] = _ndAxes[(k + 1) % 3];
        }
        if (NudgeDragging)
        {
            NudgeDragFrame(g);
            return;
        }
        if (Pressed(GameAction.Reload))
            _nudgeModes.Local = !_nudgeModes.Local;
        var best = -1;
        var bestGap = NudgeGrabSine;
        Vector2 bestDir = default;
        var bestPixels = 0f;
        var dist = MathF.Max(Vector3.Distance(eye, _ndPivot), 0.5f);
        for (var k = 0; k < 3; k++)
        {
            if (!_nudgeModes.Rotating)
            {
                var along = NudgeOnView(_ndAxes[k], dir, dist);
                var px = along.Length();
                if (px * size < 20f)
                    continue; // (end on to the view: no way to drag along it)
                var d = NudgeGap(eye, dir, _ndPivot, _ndPivot + _ndAxes[k] * size * 1.16f);
                if (d < bestGap)
                {
                    bestGap = d;
                    best = k;
                    bestDir = along / px;
                    bestPixels = px;
                }
                continue;
            }
            for (var i = 0; i < NudgeRender.ArcPoints; i++)
            {
                var a = NudgeRender.ArcPoint(k, i);
                var b = NudgeRender.ArcPoint(k, i + 1);
                var d = NudgeGap(eye, dir, a, b);
                if (d >= bestGap)
                    continue;
                bestGap = d;
                best = k;
                var along = NudgeOnView(Vector3.Normalize(b - a), dir, dist);
                var px = MathF.Max(along.Length(), 1e-3f);
                bestDir = along / px;
                bestPixels = MathF.Max(px * size, 40f); // (pixels a radian, round this bit of the arc)
            }
        }
        NudgeRender.Hot = best;
        var setting = _nudgeModes.Rotating ? _nudgeModes.Mode == SandboxNudgeModes.Option.RotateFree ? "free" : $"snap {_nudgeModes.Angle:0.#} deg" : _nudgeModes.Step > 0 ? $"step {_nudgeModes.Step:0.##} m" : "free";
        var state = (int)_nudgeModes.Mode * 4 + (_nudgeModes.Local ? 1 : 0) + (best + 1) * 16 + _ndCount * 256;
        if (NewLook(41, c0.Id, (int)(_nudgeModes.Step * 100), (int)(_nudgeModes.Angle * 10), state))
        {
            var what = _ndCount > 1 ? $"{_ndCount} bodies" : c0.DefOf(0).Name;
            var mode = _nudgeModes.Rotating ? "Rotate" : "Move";
            var frame = _nudgeModes.Local ? "local" : "world";
            _ndText = SandboxKeys.Line(what, SandboxKeys.Join(mode, frame, setting), null,
                best >= 0 ? SandboxKeys.Do(SandboxKeys.Fire, $"drag {NudgeNames[best]}") : SandboxKeys.Do(SandboxKeys.Reload, _nudgeModes.Local ? "world" : "local"));
            g.SandboxNudgeHint = $"{SandboxKeys.Fire} and drag an {(_nudgeModes.Rotating ? "arc" : "arrow")}, {SandboxKeys.Reload}: world or local, wheel: {(_nudgeModes.Rotating ? "snap angle" : "step")}, {SandboxKeys.Aim}: let go";
        }
        g.SandboxLookText = _ndText;
        if (Pressed(GameAction.Aim))
        {
            _ndClicked = null;
            _down[(int)GameAction.Aim] = false;
            return;
        }
        if (!Pressed(GameAction.Fire) || _swallowClick)
            return;
        if (best >= 0)
        {
            string? refused = null;
            for (var i = 0; i < _ndCount && refused == null; i++)
                refused = g.SandboxNudgeRefusal(_ndList[i], me.Id);
            if (refused != null)
            {
                g.SandboxSay(refused);
                return;
            }
            _ndDrag = best;
            _ndAxisNow = _ndAxes[best];
            _ndScreenDir = bestDir;
            _ndPixels = bestPixels;
            _ndMouse = Vector2.Zero;
            _ndMoveNow = Vector3.Zero;
            _ndTurnNow = Quaternion.Identity;
            _ndValue = 0;
            g.SandboxNudgeBegin(_ndList.AsSpan(0, _ndCount), _ndPivot);
            return;
        }
        // Off the gizmo: another construct, or nothing.
        if (_select.Selection.Total == 0)
            _ndClicked = aimOk ? hit.Body : null;
    }

    private static void NudgeDragFrame(Game g)
    {
        NudgeRender.Hot = _ndDrag;
        if (Pressed(GameAction.Aim))
        {
            _down[(int)GameAction.Aim] = false;
            NudgeCancel(g);
            return;
        }
        var travel = Vector2.Dot(_ndMouse, _ndScreenDir) / MathF.Max(_ndPixels, 1f);
        if (_nudgeModes.Rotating)
        {
            var degrees = travel * 180f / MathF.PI;
            if (_nudgeModes.Mode == SandboxNudgeModes.Option.RotateSnap)
                degrees = Nudge.Snap(degrees, _nudgeModes.Angle);
            _ndValue = degrees;
            _ndMoveNow = Vector3.Zero;
            _ndTurnNow = Quaternion.CreateFromAxisAngle(_ndAxisNow, degrees * MathF.PI / 180f);
        }
        else
        {
            _ndValue = Nudge.Snap(travel, _nudgeModes.Step);
            _ndMoveNow = _ndAxisNow * _ndValue;
            _ndTurnNow = Quaternion.Identity;
        }
        var key = (long)(_ndValue * 1000) * 4 + _ndDrag;
        if (key != _ndKey)
        {
            _ndKey = key;
            _ndText = SandboxKeys.Line(_nudgeModes.Rotating ? $"Rotate {NudgeNames[_ndDrag]}" : $"Move {NudgeNames[_ndDrag]}", _nudgeModes.Rotating ? $"{_ndValue:+0.##;-0.##;0} deg" : $"{_ndValue:+0.###;-0.###;0} m", null, SandboxKeys.Do(SandboxKeys.Aim, "put it back"));
        }
        g.SandboxLookText = _ndText;
        g.SandboxNudgeSteer(_ndMoveNow, _ndTurnNow);
        if (!Down(GameAction.Fire))
        {
            g.SandboxNudgeEnd(_ndMoveNow, _ndTurnNow);
            _ndDrag = -1;
        }
    }

    /// <summary>The ImGui pass: the axes' letters. (Input only stores: ImGui runs between NewFrame and Render.)</summary>
    private static void DrawNudgeInMatch()
    {
        if (!NudgeRender.On || NudgeRender.Arcs)
            return;
        for (var k = 0; k < 3; k++)
            Hud.WorldLabel(NudgeRender.At + NudgeRender.Axis[k] * NudgeRender.Size * 1.28f, k == 0 ? "X" : k == 1 ? "Y" : "Z", Vector3.One, 0.95f, 13f * Ui.Scale, 12f);
    }

    // ---- The self-test's handles -------------------------------------------------------------------------------------

    internal static int NudgeTestDrag => _ndDrag;
    internal static Vector3 NudgeTestPivot => _ndPivot;
    internal static ReadOnlySpan<Vector3> NudgeTestAxes => _ndAxes;
    internal static void NudgeTestForget() => _ndClicked = null;
}
