using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The physics gun on this PC (docs/sandbox/CONSTRUCTS.md, "The physics gun"; the rules are ConstructGrab.cs and
/// GameSandboxPhysGun.cs): hold Fire on a construct, wreck or loose part to grab it (the beam keeps its offset and the
/// spring pulls it after the aim), the mouse wheel pulls it in or pushes it out, hold Interact (F) and move the mouse to
/// turn it (Sprint, Shift, snaps to 45 degree steps), Aim freezes it and lets go, letting go of Fire drops it, and two
/// presses of Reload within 0.35 s unfreeze everything you froze (the host with Sprint held: everything). The beams of
/// every player holding something, and the frozen snowflake on look-at.
/// </summary>
public static partial class Program
{
    private static float _pgDist; // the held thing's distance (the wheel changes it)
    private static Quaternion _pgRel = Quaternion.Identity; // ...and its turn relative to the look's yaw
    private static float _pgAccYaw, _pgAccPitch; // snapping: mouse movement not yet a whole step
    private static bool _pgSnapLock; // Shift held after a snapped turn: the piece keeps its WORLD turn (or the reference construct's) while the view turns
    private static Quaternion _pgSnapWorld = Quaternion.Identity;
    private static Quaternion _pgSnapFrame = Quaternion.Identity;
    private static Construct? _pgSnapRef;
    private static double _pgScroll;
    private static double _pgReloadAt = -10;
    private static double _pgRefusedUntil;
    private static Vector3 _pgRefusedAt;
    private static long _pgLookKey = -1;
    private static string _pgLookText = "";

    /// <summary>Radians the held thing turns per pixel of mouse movement.</summary>
    private const float PhysGunTurnPerPixel = 0.006f;

    private static bool PhysGunHolding()
    {
        return _game is { SandboxOn: true } gg && gg.Local is { Alive: true } me && gg.SandboxHands.Hand(me.Id) is { Tool: ConstructTool.PhysicsGun, GrabBody: not 0 } && _mouseCaptured && !MenusOpen;
    }

    /// <summary>Before the mouse look: with something held and Interact down, the mouse turns it instead of the view (returns true then).</summary>
    private static bool PhysGunMouse(double2 delta)
    {
        if (!PhysGunHolding() || !Down(GameAction.Interact))
            return false;
        var dyaw = (float)delta.X * PhysGunTurnPerPixel;
        var dpitch = (float)delta.Y * PhysGunTurnPerPixel;
        if (!float.IsFinite(dyaw + dpitch))
            return true;
        if (Down(GameAction.Sprint) && _game is { } g)
            PhysGunSnapTurn(g, dyaw, dpitch);
        else
            _pgRel = ConstructGrab.Turn(_pgRel, dyaw, dpitch);
        return true;
    }

    /// <summary>
    /// Shift + F + mouse (pw01): the piece's WORLD turn (the look's yaw times its turn relative to it) snaps to 45 degree
    /// steps about the world's axes (or those of a construct within 2 m: "snap: name"), and a whole step of mouse movement
    /// turns it one step about the frame's up (left/right) or the frame's horizontal axis nearest the look's right (up/down).
    /// </summary>
    private static void PhysGunSnapTurn(Game g, float dyaw, float dpitch)
    {
        var yaw = g.LookYaw;
        PhysGunSnapSetup(g);
        if (!_pgSnapLock)
        {
            _pgSnapLock = true;
            _pgSnapWorld = ConstructGrab.SnapIn(_pgSnapFrame, ConstructGrab.YawQ(yaw) * _pgRel);
            _pgAccYaw = _pgAccPitch = 0;
        }
        _pgAccYaw += dyaw;
        _pgAccPitch += dpitch;
        const float step = ConstructGrab.SnapStep;
        var right = ConstructGrab.RightOf(yaw);
        for (var k = 0; k < 8 && MathF.Abs(_pgAccYaw) >= step; k++)
        {
            var s = MathF.Sign(_pgAccYaw) * step;
            _pgSnapWorld = ConstructGrab.TurnSnapped(_pgSnapWorld, _pgSnapFrame, s, 0, right);
            _pgAccYaw -= s;
        }
        for (var k = 0; k < 8 && MathF.Abs(_pgAccPitch) >= step; k++)
        {
            var s = MathF.Sign(_pgAccPitch) * step;
            _pgSnapWorld = ConstructGrab.TurnSnapped(_pgSnapWorld, _pgSnapFrame, 0, s, right);
            _pgAccPitch -= s;
        }
        _pgRel = ConstructGrab.RelOf(yaw, _pgSnapWorld);
    }

    /// <summary>The frame to snap in: the nearest other construct within 2 m, else the world (and the hint line says which).</summary>
    private static void PhysGunSnapSetup(Game g)
    {
        var held = g.SandboxHands.Hand(g.LocalId).GrabBody;
        var c = held != 0 ? g.Constructs.Get(held) : null;
        var reference = c != null ? ConstructGrab.SnapReference(g.Constructs, c) : null;
        if (_pgSnapLock && reference != _pgSnapRef)
            reference = _pgSnapRef; // (the frame stays what it was while the lock holds, so the turn doesn't jump)
        if (reference != _pgSnapRef || g.SandboxSnapLabel == null)
        {
            _pgSnapRef = reference;
            g.SandboxSnapLabel = reference == null ? "snap: world" : $"snap: construct {reference.Id}";
        }
        _pgSnapFrame = reference?.Rot ?? Quaternion.Identity;
    }

    /// <summary>Every frame while holding: Shift still down after a snapped turn keeps the piece's world turn as the view turns.</summary>
    private static void PhysGunSnapHold(Game g)
    {
        if (_pgSnapLock && Down(GameAction.Sprint))
        {
            _pgRel = ConstructGrab.RelOf(g.LookYaw, _pgSnapWorld);
            return;
        }
        PhysGunSnapEnd(g);
    }

    private static void PhysGunSnapEnd(Game g)
    {
        _pgSnapLock = false;
        _pgSnapRef = null;
        g.SandboxSnapLabel = null;
    }

    /// <summary>The mouse wheel: true when the physics gun holds something (the wheel is its distance, not the weapon swap).</summary>
    private static bool PhysGunScroll(double y)
    {
        if (!PhysGunHolding())
            return false;
        _pgScroll += y;
        return true;
    }

    /// <summary>Another tool is out: nothing of the physics gun's lingers.</summary>
    private static void PhysGunIdle()
    {
        _pgScroll = 0;
        _pgReloadAt = -10;
        _pgSnapLock = false;
        _pgSnapRef = null;
        if (_game != null)
            _game.SandboxSnapLabel = null;
    }

    /// <summary>The physics gun's keys and look text, every frame it is out (SandboxInput).</summary>
    private static void PhysGunInput(Game g, Player me, ref SandboxHand h, Vector3 eye, Vector3 dir, double now)
    {
        var creative = g.SandboxInfiniteParts;
        var range = creative ? ConstructGrab.Range : ConstructGrab.SurvivalRange;
        var holding = h.GrabBody != 0 && g.Constructs.Get(h.GrabBody) != null;
        if (!holding)
        {
            _pgScroll = 0;
            PhysGunSnapEnd(g);
            var wall = World.RaycastWorld(eye, dir, range);
            var aimed = g.Constructs.Raycast(eye, dir, MathF.Min(wall, range), out var hit);
            string? why = null;
            if (aimed)
            {
                ConstructRender.AimBody = hit.Body;
                ConstructRender.AimPart = hit.Part;
                why = g.SandboxGrabRefusal(hit.Body, me.Id);
                var key = ((long)hit.Body.Id << 32) ^ (why?.GetHashCode() ?? 1) ^ ((long)(hit.Body.Frozen ? 1 : 0) << 48) ^ ((long)hit.Part << 50);
                if (key != _pgLookKey)
                {
                    _pgLookKey = key;
                    var name = hit.Body.Loose ? hit.Body.DefOf(hit.Part).Name : hit.Body.Wreck ? "wreck" : $"construct ({hit.Body.Mass:0} kg)";
                    _pgLookText = why != null ? $"Physics gun: {why}"
                        : hit.Body.Frozen ? $"Frozen {name} - hold LMB to grab it (it unfreezes)"
                        : $"Physics gun: hold LMB to grab the {name}";
                }
                g.SandboxLookText = _pgLookText;
            }
            if (Pressed(GameAction.Fire) && !_swallowClick)
            {
                if (!aimed)
                    g.SandboxSay("the beam finds nothing to hold");
                else if (why != null)
                {
                    g.SandboxSay(why);
                    _pgRefusedUntil = now + 0.35;
                    _pgRefusedAt = hit.Point;
                }
                else
                {
                    var c = hit.Body;
                    _pgDist = Math.Clamp(hit.Distance, ConstructGrab.MinDist, range);
                    _pgRel = Quaternion.Normalize(Quaternion.Conjugate(ConstructGrab.YawQ(g.LookYaw)) * c.Rot);
                    _pgAccYaw = _pgAccPitch = 0;
                    PhysGunSnapEnd(g);
                    g.SandboxGrab(c.Id, c.ToLocal(hit.Point));
                }
            }
        }
        else if (!Down(GameAction.Fire))
            g.SandboxLetGo(freeze: false);
        else if (Pressed(GameAction.Aim))
            g.SandboxLetGo(freeze: true);
        else
        {
            PhysGunSnapHold(g);
            if (_pgScroll != 0)
            {
                _pgDist = Math.Clamp(_pgDist + (float)_pgScroll * MathF.Max(0.25f, _pgDist * 0.12f), ConstructGrab.MinDist, range);
                _pgScroll = 0;
            }
            g.SandboxSteer(_pgDist, _pgRel, now);
        }
        // Two presses of reload: everything you froze moves again (the host with Sprint held: everything).
        if (Pressed(GameAction.Reload))
        {
            if (now - _pgReloadAt <= ConstructGrab.DoubleTap)
            {
                g.SandboxUnfreezeAll(everyone: g.IsServer && Down(GameAction.Sprint));
                _pgReloadAt = -10;
            }
            else
                _pgReloadAt = now;
        }
    }

    private static readonly Vector3 BeamColour = new(0.45f, 0.85f, 1f), BeamRefused = new(1f, 0.3f, 0.25f);

    /// <summary>
    /// Every physics gun holding something: a curved beam from its tip (the point where the tool was last drawn) that
    /// leaves along the holder's look and bends onto the grab point, and a glow there. Drawn in the world pass from what
    /// every machine knows (the hands in the mode-state tail, the constructs' poses), so everyone sees everyone's beam.
    /// </summary>
    private static void DrawPhysGunBeams(Game g, double now)
    {
        if (!g.SandboxOn)
            return;
        var pulse = 0.75f + 0.25f * MathF.Sin((float)(now % 1000.0) * 18f);
        foreach (var p in g.Players.Values)
        {
            if (!p.Alive)
                continue;
            ref var h = ref g.SandboxHands.Hand(p.Id);
            if (h.Tool != ConstructTool.PhysicsGun || h.GrabBody == 0 || g.Constructs.Get(h.GrabBody) is not { } c)
                continue;
            var local = p.Id == g.LocalId;
            var look = local ? g.LookDir(g.LookYaw, g.LookPitch) : World.Forward(p.RenderYaw, p.RenderPitch);
            var eye = p.RenderEye;
            var start = ToolRender.TipOf(local ? ToolRender.ViewSlot : p.Id, now) ?? eye + look * 0.5f + World.FlatRight(local ? g.LookYaw : p.RenderYaw) * 0.18f - new Vector3(0, 0.15f, 0);
            var pos = g.IsServer ? Vector3.Lerp(c.PrevPos, c.Pos, g.TickAlpha) : c.Pos;
            var rot = g.IsServer ? Quaternion.Slerp(c.PrevRot, c.Rot, g.TickAlpha) : c.Rot;
            var end = pos + Vector3.Transform(h.GrabLocal, rot);
            var span = Vector3.Distance(start, end);
            var mid = start + look * (span * 0.5f);
            var prev = start;
            const int pieces = 14;
            for (var k = 1; k <= pieces; k++)
            {
                var t = k / (float)pieces;
                var u = 1 - t;
                var at = u * u * start + 2 * u * t * mid + t * t * end;
                Beam(prev, at, BeamColour * pulse, 0.018f);
                prev = at;
            }
            Gfx.Draw(Matrix4x4.CreateScale(0.09f * pulse) * Matrix4x4.CreateTranslation(end), BeamColour, 0.9f, mode: 2);
        }
        // A refused grab: a short red flash to where the beam was refused.
        if (now < _pgRefusedUntil && g.Local is { Alive: true } me)
        {
            var start = ToolRender.TipOf(ToolRender.ViewSlot, now) ?? me.RenderEye + g.LookDir(g.LookYaw, g.LookPitch) * 0.5f;
            Beam(start, _pgRefusedAt, BeamRefused, 0.014f);
        }
    }

    private static void Beam(Vector3 a, Vector3 b, Vector3 colour, float width)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        Gfx.Draw(Matrix4x4.CreateScale(width, width, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour, 0.85f, mode: 2);
    }

    private static readonly ImColor8 FrostIcon = new(170, 225, 255, 235), FrostShadow = new(10, 20, 30, 160);

    /// <summary>A small snowflake beside the crosshair while it is on a frozen construct (any tool, or none).</summary>
    private static void DrawPhysGunHud()
    {
        if (_game is not { SandboxOn: true } g || g.Local is not { Alive: true } || ConstructRender.AimBody is not { Frozen: true } || !_mouseCaptured || MenusOpen)
            return;
        var size = ImGui.GetIO().DisplaySize;
        var s = Ui.Scale;
        var c = new float2(size.X * 0.5f + 26 * s, size.Y * 0.5f - 26 * s);
        var dl = ImGui.GetForegroundDrawList();
        var r = 9f * s;
        for (var pass = 0; pass < 2; pass++)
        {
            var col = pass == 0 ? FrostShadow : FrostIcon;
            var w = (pass == 0 ? 3.2f : 1.6f) * s;
            var o = pass == 0 ? new float2(1, 1) : new float2(0, 0);
            for (var k = 0; k < 3; k++)
            {
                var a = k * MathF.PI / 3;
                var dx = MathF.Cos(a) * r;
                var dy = MathF.Sin(a) * r;
                dl.AddLine(new float2(c.X - dx + o.X, c.Y - dy + o.Y), new float2(c.X + dx + o.X, c.Y + dy + o.Y), col, w);
                for (var e = -1; e <= 1; e += 2)
                {
                    // A little V near each end of the spoke.
                    var tip = new float2(c.X + e * dx * 0.62f + o.X, c.Y + e * dy * 0.62f + o.Y);
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var b = a + (e > 0 ? 0 : MathF.PI) + side * 0.75f;
                        dl.AddLine(tip, new float2(tip.X + MathF.Cos(b) * r * 0.38f, tip.Y + MathF.Sin(b) * r * 0.38f), col, w);
                    }
                }
            }
        }
    }
}
