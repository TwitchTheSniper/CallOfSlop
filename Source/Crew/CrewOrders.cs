using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// An admiral's orders at the helm (docs/sandbox/design/faction-controllers.md phase 2; the orders are written by CrewFleet.cs into
/// <see cref="CrewShip.Order"/>): a crew keeps a slot in its flagship's frame, goes to a place (and works there: load, unload, mine, deliver),
/// escorts or screens a body, retreats to a place. It presses the same keys a pilot or driver would; Engage and Board need nothing here (the
/// crew's target is the order's body, and Board closes to <see cref="BoardRange"/>). Rule 1 holds: nobody at the helm, nothing moves.
/// Server only; allocates nothing.
/// </summary>
public sealed partial class ShipAi
{
    /// <summary>How close a Board order brings it (m); a formation's arrival band (m); cruise and retreat speeds under orders (m/s).</summary>
    public const float BoardRange = 40, SlotBand = 20, OrderCruise = 45, OrderRun = 80;

    private OrderKind _orderKind;

    private bool Ordered(out ShipOrder o)
    {
        if (Ship != null && Crew != null && Ship.Ordered(Crew.Tick))
        {
            o = Ship.Order;
            return true;
        }
        o = default;
        return false;
    }

    /// <summary>The stand-off this tick: a Board order closes to boarding range.</summary>
    private float StandOffNow => Ordered(out var o) && o.Kind == OrderKind.Board ? BoardRange : StandOff;

    /// <summary>A body's way ahead, level: a ship's nose, a vehicle's drive forward.</summary>
    private static Vector3 Heading(Construct f, Vector3 up) => Level(Vector3.Transform(f.StabCount > 0 ? -Vector3.UnitZ : f.DriveFwd, f.Rot), up);

    /// <summary>Where its order puts it (world), the way to face there, and the velocity to keep with; false: no place (fight or hold as ever).</summary>
    private static bool OrderPoint(ConstructWorld world, Construct c, in ShipOrder o, Vector3 up, out Vector3 to, out Vector3 face, out Vector3 keep)
    {
        to = face = keep = default;
        switch (o.Kind)
        {
            case OrderKind.Form:
            case OrderKind.Escort:
            {
                if (world.Get(o.Body) is not { Wreck: false } f || f.Id == c.Id)
                    return false;
                var fwd = Heading(f, up);
                var right = Vector3.Cross(fwd, up);
                to = f.WorldCom + right * o.At.X + up * o.At.Y + fwd * o.At.Z;
                face = fwd;
                keep = f.Vel;
                return true;
            }
            case OrderKind.Screen:
            {
                if (world.Get(o.Body) is not { Wreck: false } p)
                    return false;
                var th = world.Get(o.Threat) is { Wreck: false } tc ? tc.WorldCom : p.WorldCom + Heading(p, up) * 100;
                var way = Level(th - p.WorldCom, up);
                to = p.WorldCom + way * MathF.Max(30, o.At.Z);
                face = way;
                keep = p.Vel;
                return true;
            }
            case OrderKind.GoTo or OrderKind.Retreat or OrderKind.Load or OrderKind.Unload or OrderKind.Mine or OrderKind.Deliver or OrderKind.Route:
                to = o.At;
                face = Level(o.At - c.WorldCom, up);
                return true;
        }
        return false;
    }

    /// <summary>
    /// A flying crew under a moving order: true when it flew to its order's place this tick (the pilot's keys and look, its gunners laying on
    /// whatever it was fighting). False: it fights or holds as ever (an Engage, a Board or a Hold order; a fighting order with a target).
    /// </summary>
    private bool OrderFly(ConstructWorld world, Construct c, bool has, in ShipAiTarget t, Vector3 com, Vector3 up, bool pilotOn, bool grace)
    {
        if (!Ordered(out var o) || o.Kind is OrderKind.Engage or OrderKind.Board or OrderKind.Hold || has && o.Fight && o.Kind != OrderKind.Retreat)
            return false;
        if (!OrderPoint(world, c, o, up, out var to, out var face, out var keep))
            return false;
        _idle = 0;
        _orderKind = o.Kind;
        var d = to - com;
        var flat = d - up * Vector3.Dot(d, up);
        var fdist = flat.Length();
        _state = 10;
        _stateDist = fdist;
        ushort pilot = CPart.Occupied;
        // Height: the place's (a slot's: its flagship's), over the ground by the clearance at a place on the ground.
        var height = ConstructPhysics.HoldHeight(com);
        var want = ConstructPhysics.HoldHeight(to) + (o.Kind is OrderKind.Form or OrderKind.Escort or OrderKind.Screen ? 0 : MinClearance);
        if (_ground < MinClearance)
            want = MathF.Max(want, height + (MinClearance - _ground));
        var climb = Vector3.Dot(c.Vel, up);
        if (_ahead < MinClearance * 2 || want - height > 6 && climb < 25)
            pilot |= (ushort)Buttons.Sprint;
        else if (want - height < -6 && climb > -15 && _ground > MinClearance)
            pilot |= (ushort)Buttons.Crouch;
        // The way: nose to the place while far, then the formation's heading; W and S hold the speed (its slot's own way and a closing speed).
        var dir = fdist > 1 ? flat / fdist : face;
        var look = GravityFrames.Angles(com, (fdist > SlotBand || face == default ? dir : face) * 100);
        var nose = Level(Vector3.Transform(-Vector3.UnitZ, c.Rot), up);
        var top = o.Speed > 0 ? o.Speed : o.Kind == OrderKind.Retreat ? OrderRun : OrderCruise;
        var keepFwd = Vector3.Dot(keep, nose);
        var wantFwd = Math.Clamp(keepFwd + Vector3.Dot(flat, nose) * 0.12f, -15, top);
        var fwdNow = Vector3.Dot(c.Vel, nose);
        if (fdist > SlotBand && Vector3.Dot(nose, dir) < 0.3f)
            wantFwd = MathF.Min(wantFwd, 5); // (turn first)
        if (wantFwd - fwdNow > 2)
            pilot |= (ushort)Buttons.Forward;
        else if (wantFwd - fwdNow < -2)
            pilot |= (ushort)Buttons.Back;
        // Sideways inside the band (Q and E), so a slot is kept, not circled.
        if (fdist < SlotBand * 3)
        {
            var side = Vector3.Dot(flat, Vector3.Cross(nose, up));
            if (side > 4)
                pilot |= ConstructParts.KeyRollRight;
            else if (side < -4)
                pilot |= ConstructParts.KeyRollLeft;
        }
        if (pilotOn)
            PilotSeat(c, pilot, look.Yaw, look.Pitch);
        else if (grace)
            Seat(c, _pilot, (ushort)(_pilotKeys & ~ConstructParts.KeyFire), _pilotYaw, _pilotPitch);
        var dist = has ? Vector3.Distance(t.At, com) : 0;
        for (var s = 0; s < _seatCount; s++)
        {
            int seat = _seats[s];
            if (seat >= c.PartCount || !SeatFree(c, seat) || c.Parts[seat].Broken || !Manned(seat))
                continue;
            if (!has)
            {
                GunSeat(c, s, CPart.Occupied, c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
                continue;
            }
            var (gl, fire) = Gunnery(world, c, seat, t, dist, fixedGuns: false);
            GunSeat(c, s, (ushort)(CPart.Occupied | (fire ? ConstructParts.KeyFire : 0)), gl.Yaw, gl.Pitch);
        }
        return true;
    }

    /// <summary>
    /// A ground crew under a moving order: the keys for its helm, or false (fight, follow or patrol as ever). A spawner's convoy follower whose
    /// slot leader is its set-down leader keeps its own convoy place (CrewGround's follower, kept whole).
    /// </summary>
    private bool OrderDrive(ConstructWorld world, Construct c, bool has, Vector3 up, out ushort keys)
    {
        keys = 0;
        if (!Ordered(out var o) || o.Kind is OrderKind.Engage or OrderKind.Board or OrderKind.Hold || has && o.Fight && o.Kind is not (OrderKind.Retreat or OrderKind.Form))
            return false;
        if (o.Kind == OrderKind.Form && c.Brief is { ConvoyIndex: > 0 } b && b.Leader == o.Body)
            return false; // (its convoy place, as set down: CrewGround.cs)
        if (!OrderPoint(world, c, o, up, out var to, out var face, out var keep))
            return false;
        _orderKind = o.Kind;
        var com = c.WorldCom;
        var flat = to - com;
        flat -= up * Vector3.Dot(flat, up);
        _state = 10;
        _stateDist = flat.Length();
        if (o.Kind is OrderKind.Form or OrderKind.Escort)
        {
            var lv = MathF.Max(0, Vector3.Dot(keep, face));
            var behind = Vector3.Dot(flat, face);
            var want = Math.Clamp(lv + behind * 0.35f, 0, GroundClose * 1.5f);
            keys = Drive(world, c, to + face * (FollowAhead + lv), want, up, stopAt: false);
            return true;
        }
        var speed = o.Speed > 0 ? o.Speed : o.Kind == OrderKind.Retreat ? GroundClose * 2 : GroundCruise;
        keys = Drive(world, c, to, speed, up, stopAt: o.Kind != OrderKind.Retreat);
        return true;
    }
}
