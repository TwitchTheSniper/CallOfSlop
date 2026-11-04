using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Analog Lever in the hand (AnalogLever.cs). Hold the use key on one, with any tool or none, walking or seated, and
/// the mouse moves it: the view stands still, the handle goes the way the mouse goes on the screen, letting go leaves it
/// there. LMB does the same with the hands out (a gun's or a tool's LMB is its own). A press with no move changes nothing;
/// in a seat it is the use key as ever: it gets you out. The wheel nudges one under the crosshair, with the hands, the
/// attach tool or a bare hand and nothing held, and any tool in a seat.
/// This PC shows its own value at once. The server hears at most 10 Hz and a last word on letting go, and until that
/// has been answered the value is held here as shown, so it never snaps back. The lever is kept as a part index, which
/// another player's edit can shift, so it is stamped when taken and checked every frame.
/// </summary>
public static partial class Program
{
    private const float LeverLook = 0.45f; // radians of mouse look (at the player's sensitivity) that take it from end to end
    private const double LeverSendEvery = 0.1, LeverKeepAlive = 0.2, LeverSettleTime = 0.6; // (seconds; keep-alive: a spring holds off while it hears a hand)
    private const float LeverMoved = 3; // pixels: less than this is a press, not a drag

    private static ushort _lvBody;
    private static int _lvPart = -1;
    private static long _lvStamp;
    private static bool _lvHeld, _lvSeated, _lvMoved;
    private static GameAction _lvKey;
    private static float _lvRaw, _lvValue, _lvSent, _lvTravel; // (raw: unsnapped, so a slow drag still crosses a snap step)
    private static Vector2 _lvPending, _lvScreen; // (mouse moved since the last frame; the way along the screen that raises it)
    private static double _lvSentAt, _lvSettleUntil, _lvScroll;
    private static bool _lvWheelOk;
    private static int _lvLookKey = int.MinValue;
    private static string _lvLook = "";

    /// <summary>The value as the look line shows it.</summary>
    private static string LeverValueText(in CPart p, ConstructPartDef d) => AnalogLever.Text(AnalogLever.Value(p, d));

    /// <summary>A hand on a lever: the frame's mouse moves it and the view stands still.</summary>
    private static bool LeverMouse(double2 delta)
    {
        if (!_lvHeld)
            return false;
        _lvPending += new Vector2((float)delta.X, (float)delta.Y);
        return true;
    }

    /// <summary>The wheel on the lever under the crosshair, or the one in the hand.</summary>
    private static bool LeverScroll(double y)
    {
        if (!_lvHeld && !_lvWheelOk)
            return false;
        _lvScroll += y;
        return true;
    }

    /// <summary>This PC's hold goes, with no word to the server (a recovered frame, the mode gone).</summary>
    private static void LeverReset()
    {
        _lvHeld = false;
        _lvWheelOk = false;
        _lvPart = -1;
        _lvSettleUntil = 0;
        _lvPending = default;
        _lvScroll = 0;
        ConstructRender.LeverHotBody = null;
        ConstructRender.LeverHotPart = -1;
    }

    /// <summary>The lever held, as it is now: null when the construct changed under it (a part taken off shifts the indices).</summary>
    private static Construct? LeverNow(Game g)
    {
        if (_lvPart < 0 || g.Constructs.Get(_lvBody) is not { } c || _lvPart >= c.PartCount || LinkStamp(c, _lvPart, 255, 0) != _lvStamp || c.DefOf(_lvPart).Logic != CLogic.AnalogLever)
            return null;
        return c;
    }

    private static void LeverSend(Game g, float value, bool final, double now)
    {
        g.SandboxSend(CActKind.AnalogLever, body: _lvBody, part: (ushort)_lvPart, socket: AnalogLever.ToWire(value), flags: (byte)(final ? 1 : 0));
        _lvSent = value;
        _lvSentAt = now;
    }

    private static void LeverShow(Construct c, float value)
    {
        ConstructRender.LeverHotBody = c;
        ConstructRender.LeverHotPart = _lvPart;
        AnalogLever.SetValue(ref c.Parts[_lvPart], c.DefOf(_lvPart), value);
    }

    /// <summary>The hand lets go (the key up, the menu opened, dead): the last word goes to the server.</summary>
    private static void LeverLetGo(Game g, double now)
    {
        if (!_lvHeld)
            return;
        _lvHeld = false;
        _lvPending = default;
        if (LeverNow(g) is not { } c)
        {
            LeverReset();
            return;
        }
        if (_lvMoved)
        {
            LeverSend(g, _lvValue, final: true, now);
            _lvSettleUntil = ConstructConfig.Get(c.Parts[_lvPart], c.DefOf(_lvPart), CfgKey.Spring, 0) >= 0.5f ? 0 : now + LeverSettleTime; // (a spring is the server's from here)
        }
        if (_lvSettleUntil <= now)
            ConstructRender.LeverHotBody = null;
    }

    /// <summary>Every frame: a value just let go stays as this hand left it until the server has answered.</summary>
    private static void LeverSettle(Game g, double now)
    {
        _lvWheelOk = false; // (until this frame's aim has been looked at: LeverFrame)
        if (_lvHeld || _lvSettleUntil == 0)
            return;
        if (now < _lvSettleUntil && LeverNow(g) is { } c)
            LeverShow(c, _lvValue);
        else
        {
            _lvSettleUntil = 0;
            ConstructRender.LeverHotBody = null;
        }
    }

    private static void LeverGrab(Game g, Construct c, int part, bool seated, GameAction key)
    {
        _lvBody = c.Id;
        _lvPart = part;
        _lvStamp = LinkStamp(c, part, 255, 0);
        _lvHeld = true;
        _lvSeated = seated;
        _lvMoved = false;
        _lvKey = key;
        _lvTravel = 0;
        _lvPending = default;
        _lvScroll = 0;
        _lvSettleUntil = 0;
        ref var p = ref c.Parts[part];
        _lvRaw = _lvValue = _lvSent = AnalogLever.Value(p, p.D);
        _lvSentAt = 0;
        // The way the handle's tip goes on the screen, taken once so the drag doesn't turn over halfway. Seen end on
        // (it would go straight at the eye): mouse up raises it.
        c.WorldFrame(p.Seg, out var q, out _);
        var along = Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitZ), q);
        SandboxCamera(g, out var right, out _);
        var up = Vector3.Cross(right, g.LookDir(g.LookYaw, g.LookPitch));
        var screen = new Vector2(Vector3.Dot(along, right), -Vector3.Dot(along, up));
        _lvScreen = screen.Length() > 0.3f ? Vector2.Normalize(screen) : new Vector2(0, -1);
        ConstructRender.LeverHotBody = c;
        ConstructRender.LeverHotPart = part;
    }

    private static void LeverDrag(Game g, in SandboxHand h, Vector3 eye, double now)
    {
        if (LeverNow(g) is not { } c)
        {
            LeverReset();
            return;
        }
        ref var p = ref c.Parts[_lvPart];
        var d = p.D;
        var stood = _lvSeated != h.Seated;
        if (!Down(_lvKey) || p.Broken || stood || Vector3.Distance(eye, ConstructEdits.ReachPoint(c, _lvPart, eye)) > ConstructBuild.UseReach + AnalogLever.DragReach)
        {
            var press = !_lvMoved && _lvSeated && !stood && !Down(_lvKey);
            LeverLetGo(g, now);
            if (press)
                g.SandboxSend(CActKind.Stand); // (seated, a press with no drag is the use key's own: out of the seat)
            return;
        }
        var delta = Vector2.Dot(_lvPending, _lvScreen);
        _lvTravel += MathF.Abs(delta);
        _lvRaw = Math.Clamp(_lvRaw + delta * (BaseSensitivity * _settings.Sensitivity) / LeverLook, 0, 1);
        _lvPending = default;
        var notches = (float)_lvScroll;
        if (notches != 0)
        {
            _lvScroll = 0;
            _lvRaw = Math.Clamp(_lvRaw + notches * AnalogLever.WheelStep, 0, 1);
            _lvTravel += LeverMoved;
        }
        _lvMoved |= _lvTravel >= LeverMoved;
        if (_lvMoved)
        {
            _lvValue = AnalogLever.Snapped(p, d, _lvRaw);
            LeverShow(c, _lvValue);
            if (_lvValue != _lvSent && now - _lvSentAt >= LeverSendEvery || now - _lvSentAt >= LeverKeepAlive)
                LeverSend(g, _lvValue, final: false, now);
        }
        g.SandboxLookText = LeverLine(c, _lvPart, _lvValue, "dragging");
    }

    /// <summary>The look line, made again only when it changes.</summary>
    private static string LeverLine(Construct c, int part, float value, string action)
    {
        var key = HashCode.Combine(c.Parts[part].Def, (int)MathF.Round(value * 100), action, SandboxKeys.Use);
        if (key != _lvLookKey)
        {
            _lvLookKey = key;
            _lvLook = SandboxKeys.Line(c.DefOf(part).Name, AnalogLever.Text(value), null, action);
        }
        return _lvLook;
    }

    /// <summary>
    /// One frame: the hand on a lever, or what the crosshair's lever takes. True when the use key's press went to a lever
    /// (so it does nothing else). <paramref name="join"/>: the welder's use key is the join's.
    /// </summary>
    private static bool LeverFrame(Game g, in SandboxHand h, bool aimed, in ConstructHit hit, Vector3 eye, double now, bool join, bool slotted)
    {
        if (_lvHeld)
        {
            LeverDrag(g, in h, eye, now);
            return Pressed(GameAction.Interact);
        }
        if (!aimed || hit.SlotPlus1 != 0 || hit.Part < 0 || hit.Part >= hit.Body.PartCount || hit.Distance > ConstructBuild.UseReach
            || hit.Body.Loose || hit.Body.DefOf(hit.Part).Logic != CLogic.AnalogLever || hit.Body.Parts[hit.Part].Broken)
        {
            _lvScroll = 0;
            return false;
        }
        _lvWheelOk = h.Seated || h.Held == ConstructParts.None && h.Tool is ConstructTool.None or ConstructTool.Hands or ConstructTool.Welder;
        if (_lvScroll != 0)
        {
            LeverNudge(g, hit.Body, hit.Part, now);
            _lvScroll = 0;
        }
        if (Pressed(GameAction.Interact) && !join && !(h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0))
        {
            LeverGrab(g, hit.Body, hit.Part, h.Seated, GameAction.Interact);
            return true;
        }
        if (h.Tool == ConstructTool.Hands && !h.Seated && !slotted && Pressed(GameAction.Fire) && !_swallowClick)
            LeverGrab(g, hit.Body, hit.Part, false, GameAction.Fire);
        return false;
    }

    /// <summary>The wheel: a step (its own snap step, if it has one) up or down, sent at once.</summary>
    private static void LeverNudge(Game g, Construct c, int part, double now)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var step = ConstructConfig.Get(p, d, CfgKey.Snap, 0);
        _lvBody = c.Id;
        _lvPart = part;
        _lvStamp = LinkStamp(c, part, 255, 0);
        _lvValue = AnalogLever.Snapped(p, d, AnalogLever.Value(p, d) + (float)_lvScroll * (step > 0 ? step : AnalogLever.WheelStep));
        LeverShow(c, _lvValue);
        LeverSend(g, _lvValue, final: true, now);
        _lvSettleUntil = now + LeverSettleTime;
    }

    /// <summary>Seated: the same, on a lever in reach of the seat. True when the use key's press went to a lever.</summary>
    private static bool LeverSeated(Game g, Player me, in SandboxHand h, double now)
    {
        SandboxAimRay(g, me, out var eye, out var dir);
        var left = MathF.Min(World.RaycastWorld(eye, dir, ConstructBuild.UseReach), ConstructBuild.UseReach);
        var from = eye;
        var aimed = g.Constructs.Raycast(from, dir, left, out var hit, look: true);
        // (the seat you sit in is round the eye and in the way of what lies low beside it: the ray goes on through it)
        for (var k = 0; k < 4 && aimed && hit.Body.Id == h.SeatBody && hit.Part == h.SeatPart; k++)
        {
            var on = hit.Distance + 0.2f;
            from += dir * on;
            left -= on;
            aimed = left > 0 && g.Constructs.Raycast(from, dir, left, out hit, look: true);
        }
        var took = LeverFrame(g, in h, aimed, hit, eye, now, false, false);
        if (_lvHeld)
            return true;
        if (_lvWheelOk)
        {
            ConstructRender.AimBody = hit.Body;
            ConstructRender.AimPart = hit.Part;
            ConstructRender.AimRefused = false;
            ConstructRender.AimHands = true;
            ref var p = ref hit.Body.Parts[hit.Part];
            g.SandboxLookText = LeverLine(hit.Body, hit.Part, AnalogLever.Value(p, p.D), SandboxKeys.Do(SandboxKeys.Use, "drag"));
        }
        return took;
    }
}
