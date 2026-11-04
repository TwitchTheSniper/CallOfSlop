using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Build Gantry's camera on this PC (docs/sandbox/CONSTRUCTS.md "Build stations"): a camera free of the body, orbiting a
/// point on one construct. The body stays where it is, its inputs off; the mouse moves a cursor, the orbit key turns the view,
/// the wheel zooms and the move keys slide the point. Every tool works as ever, along the cursor's ray
/// (<see cref="SandboxAimRay"/>), and the server measures reach from the gantry (ConstructEdits.GantryReach). A build plane,
/// stepped by keys, catches the ray in empty space: the nearest free socket to where it crosses takes the part.
/// Being hit, the server letting go, or the construct going ends it.
/// </summary>
public static unsafe partial class Program
{
    public const float GantryPlaneStep = 0.5f;
    private const float GantryMinDist = 3f;

    private static bool _gWanted, _gOn;
    private static double _gWantedAt;
    private static ushort _gBody;
    private static float _gYaw, _gPitch, _gDist, _gHealth;
    private static Vector3 _gPivot, _gCam, _gFwd;
    private static int _gPlaneStep;
    private static float _gPlaneBase;
    private static Vector2 _gCursor;

    /// <summary>Is the gantry's camera on?</summary>
    public static bool GantryOn => _gOn;

    private static void GantryLeave()
    {
        _gWanted = _gOn = false;
        _gBody = 0;
    }

    private static void GantrySend(Game g, ushort body)
    {
        var w = g.SandboxAidBegin(BuildAidOp.Gantry);
        w.U16(body);
        g.SandboxAidSend(w);
    }

    /// <summary>Its key, and the rest of the frame: starting (once the server says so), the move keys, the plane keys, and ending.</summary>
    private static void SandboxGantryFrame(Game g, Player me, bool aimed, in ConstructHit hit, double now)
    {
        var v = g.SandboxAids(me.Id);
        if (Pressed(GameAction.BuildGantry))
        {
            if (_gOn || _gWanted)
            {
                GantrySend(g, 0);
                GantryLeave();
            }
            else if (!g.SandboxAidLive(BuildAid.Gantry))
                g.SandboxSay(AidOffText(g, BuildAid.Gantry));
            else if ((aimed ? hit.Body : GantryNearest(g, me)) is not { } target)
                g.SandboxSay("Gantry: aim at a construct");
            else
            {
                _gWanted = true;
                _gWantedAt = now;
                _gBody = target.Id;
                GantrySend(g, target.Id);
            }
        }
        if (_gWanted && !_gOn)
        {
            if (v.Gantry && g.Constructs.Get(_gBody) is { } c)
                GantryBegin(g, me, c);
            else if (now > _gWantedAt + 2)
                GantryLeave();
        }
        else if (_gOn)
        {
            if (!v.Gantry || g.Constructs.Get(_gBody) is not { } c2 || me.Health < _gHealth)
            {
                if (v.Gantry)
                    GantrySend(g, 0);
                if (me.Health < _gHealth)
                    g.SandboxSay("Gantry: you were hit");
                GantryLeave();
                return;
            }
            _gHealth = me.Health;
            var dt = (float)FrameDt;
            var flat = g.LookDir(_gYaw, 0);
            var right = g.LookDir(_gYaw + MathF.PI / 2, 0);
            var up = Vector3.UnitY;
            var speed = MathF.Max(4f, _gDist) * (Down(GameAction.Sprint) ? 1.6f : 0.8f) * dt;
            var move = (Down(GameAction.MoveForward) ? flat : default) - (Down(GameAction.MoveBack) ? flat : default) + (Down(GameAction.MoveRight) ? right : default) - (Down(GameAction.MoveLeft) ? right : default)
                       + (Down(GameAction.Jump) ? up : default) - (Down(GameAction.CrouchHold) ? up : default);
            _gPivot += Vector3.Transform(move, Quaternion.Conjugate(c2.Rot)) * speed;
            if (Pressed(GameAction.BuildPlaneUp))
                _gPlaneStep++;
            if (Pressed(GameAction.BuildPlaneDown))
                _gPlaneStep--;
            ConstructRender.ShowGrid(c2, _gPlaneBase + _gPlaneStep * GantryPlaneStep, _gPivot);
        }
    }

    private static Construct? GantryNearest(Game g, Player me)
    {
        Construct? best = null;
        var bestD = BeaconTable.Current.Range("gantry");
        foreach (var c in g.Constructs.Bodies)
        {
            var d = Vector3.Distance(c.WorldCom, me.Eye) - c.Radius;
            if (d < bestD && !c.Loose)
            {
                bestD = d;
                best = c;
            }
        }
        return best;
    }

    private static void GantryBegin(Game g, Player me, Construct c)
    {
        _gOn = true;
        _gHealth = me.Health;
        _gPivot = (c.LocalMin + c.LocalMax) * 0.5f;
        _gYaw = g.LookYaw;
        _gPitch = -0.45f;
        _gDist = Math.Clamp((c.LocalMax - c.LocalMin).Length() * 1.1f, GantryMinDist + 3, GantryMaxDist());
        _gPlaneBase = MathF.Round(c.LocalMin.Y * 2) / 2;
        _gPlaneStep = 0;
        _gCursor = ScreenSize * 0.5f;
    }

    private static float GantryMaxDist() => BeaconTable.Current.Range("gantry");

    /// <summary>The camera's mouse: the orbit key turns the view, else the cursor moves. True when the gantry took it (nothing looks round).</summary>
    private static bool GantryMouse(double2 delta)
    {
        if (!_gOn)
            return false;
        if (Down(GameAction.GantryOrbit))
        {
            _gYaw = World.WrapAngle(_gYaw + (float)delta.X * 0.004f);
            _gPitch = Math.Clamp(_gPitch - (float)delta.Y * 0.004f * (_settings.InvertY ? -1 : 1), -1.5f, 1.5f);
        }
        else
            _gCursor = Vector2.Clamp(_gCursor + new Vector2((float)delta.X, (float)delta.Y), Vector2.Zero, ScreenSize);
        return true;
    }

    private static bool GantryScroll(double y)
    {
        if (!_gOn)
            return false;
        _gDist = Math.Clamp(_gDist * (y > 0 ? 0.9f : 1.1f), GantryMinDist, GantryMaxDist());
        return true;
    }

    /// <summary>The body's buttons while parked: none.</summary>
    private static Buttons GantryButtons(Buttons b) => _gOn ? default : b;

    /// <summary>In the frame: puts the camera on its orbit. True when it did.</summary>
    private static bool GantryCamera(ref Vector3 eye, ref Vector3 fwd)
    {
        if (!_gOn || _game?.Constructs.Get(_gBody) is not { } c)
            return false;
        _gFwd = _game.LookDir(_gYaw, _gPitch);
        _gCam = c.ToWorld(_gPivot) - _gFwd * _gDist;
        eye = _gCam;
        fwd = _gFwd;
        return true;
    }

    /// <summary>The ray through the cursor, from last frame's view (a frame behind the turn at most).</summary>
    private static void GantryRay(out Vector3 eye, out Vector3 dir)
    {
        eye = _gCam;
        dir = _gFwd;
        if (!Matrix4x4.Invert(Gfx.ViewProj, out var inv))
            return;
        var ndc = new Vector2(_gCursor.X / ScreenSize.X * 2 - 1, 1 - _gCursor.Y / ScreenSize.Y * 2);
        var a = Vector4.Transform(new Vector4(ndc, 0.2f, 1), inv);
        var b = Vector4.Transform(new Vector4(ndc, 0.8f, 1), inv);
        if (MathF.Abs(a.W) < 1e-9f || MathF.Abs(b.W) < 1e-9f)
            return;
        var d = new Vector3(b.X, b.Y, b.Z) / b.W - new Vector3(a.X, a.Y, a.Z) / a.W;
        if (d.LengthSquared() > 1e-12f)
            dir = Vector3.Normalize(d);
        if (Vector3.Dot(dir, _gFwd) < 0)
            dir = -dir;
    }

    /// <summary>The cursor, in pixels from the screen's centre, up positive (the magnet's centre).</summary>
    private static Vector2 GantryCursorOffset => _gOn ? new Vector2(_gCursor.X - ScreenSize.X * 0.5f, ScreenSize.Y * 0.5f - _gCursor.Y) : default;

    /// <summary>A held part and a ray that meets nothing: where it crosses the build plane, as a hit on the nearest part there.</summary>
    private static bool GantryPlaneHit(Game g, Vector3 eye, Vector3 dir, out ConstructHit hit)
    {
        hit = default;
        if (!_gOn || g.Constructs.Get(_gBody) is not { } c)
            return false;
        var inv = Quaternion.Conjugate(c.Rot);
        var from = Vector3.Transform(eye - c.Pos, inv);
        var ray = Vector3.Transform(dir, inv);
        var y = _gPlaneBase + _gPlaneStep * GantryPlaneStep;
        if (MathF.Abs(ray.Y) < 1e-3f || (y - from.Y) / ray.Y is not (var s and > 0))
            return false;
        var at = from + ray * s;
        var hits = Construct.PartScratch;
        var n = c.PartsInBox(at - new Vector3(3f), at + new Vector3(3f), hits);
        var best = -1;
        var bestD = float.MaxValue;
        for (var k = 0; k < n; k++)
        {
            c.PartBounds(hits[k], out var mn, out var mx);
            var d = Vector3.DistanceSquared(Vector3.Clamp(at, mn, mx), at);
            if (d < bestD)
            {
                bestD = d;
                best = hits[k];
            }
        }
        if (best < 0)
            return false;
        hit = new ConstructHit { Body = c, Part = best, Distance = s, Point = c.ToWorld(at), Normal = Vector3.UnitY };
        return true;
    }
}
