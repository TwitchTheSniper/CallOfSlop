using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The ground helm (docs/sandbox/design/bot-brain.md, phase 2): a crewed ground vehicle or walker with no Flight Stabilizer is driven from its
/// drive seat by the keys a player there would press (W, S, A, D, Space for the brake). A convoy's members keep the place they were set down in,
/// in their leader's frame; the leader drives out and back along the way it was set down facing; a lone vehicle holds where it is and, with
/// something to fight, closes to its guns' range (the brief's <c>Engage</c>) and holds there, turning its weak side (<c>KeepAway</c>) off the
/// enemy. Rule 1: with nobody at the helm (after the helm's grace) the throttle is 0 and the brakes are on. Gunners are Karl's gunnery,
/// unchanged. Server only; allocates nothing.
/// </summary>
public sealed partial class ShipAi
{
    /// <summary>Speeds (m/s): a convoy's cruise, a lone vehicle closing; a patrol leg (m); a follower's look ahead (m).</summary>
    public const float GroundCruise = 9, GroundClose = 12, PatrolLeg = 600, FollowAhead = 10;

    /// <summary>The range band either side of the guns' range a lone vehicle is content in (m), and its range with no brief (m).</summary>
    public const float GroundBand = 15, GroundEngage = 80;

    private bool _onGround;
    private bool _gHomeSet, _gOffsetSet;
    private Vector3 _gHome, _gWay, _gOffset; // its patrol's start and way (world, level); its place in its leader's frame
    private sbyte _gLeg = 1;
    private int _gStuck, _gReverseUntil;

    /// <summary>It drives a ground vehicle (no stabilizer): for the check's lines.</summary>
    public bool OnGround => _onGround;

    private void GroundReset()
    {
        _onGround = _gHomeSet = _gOffsetSet = false;
        _gLeg = 1;
        _gStuck = _gReverseUntil = 0;
    }

    /// <summary>Nobody at the helm: throttle 0, brakes on (rule 1). A player's seat is theirs.</summary>
    private void GroundStop(Construct c)
    {
        c.CrewDrives = false;
        if (_pilot < 0 || _pilot >= c.PartCount || !SeatFree(c, _pilot))
            return;
        ref var p = ref c.Parts[_pilot];
        p.Keys = 0;
        ConstructEdits.DriveInput(c, Buttons.Jump, World.TickDt);
        _pilotKeys = (ushort)Buttons.Jump;
    }

    private void StepGround(ConstructWorld world, Construct c, IWorldQuery query, bool has, in ShipAiTarget t)
    {
        var pilotOn = Manned(_pilot);
        if (pilotOn)
            _pilotWas = true;
        var grace = !pilotOn && Grace;
        var com = c.WorldCom;
        var dist = has ? Vector3.Distance(t.At, com) : 0f;
        if (!pilotOn && !grace)
            GroundStop(c);
        else
        {
            var keys = GroundHelm(world, c, has, t, dist);
            c.CrewDrives = true; // (a seated crew member drives as a sitter would: Construct.Driven)
            if (pilotOn)
            {
                var look = (Yaw: c.Parts[_pilot].AimYaw, Pitch: c.Parts[_pilot].AimPitch);
                if (has)
                {
                    var (l, fire) = Gunnery(world, c, _pilot, t, dist, fixedGuns: true);
                    look = l;
                    if (fire)
                        keys |= ConstructParts.KeyFire;
                }
                PilotSeat(c, (ushort)(CPart.Occupied | keys), look.Yaw, look.Pitch);
            }
            else
                Seat(c, _pilot, (ushort)(CPart.Occupied | keys), _pilotYaw, _pilotPitch); // (the last look held; nobody fires)
        }
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
            var (look, fire) = Gunnery(world, c, seat, t, dist, fixedGuns: false);
            GunSeat(c, s, (ushort)(CPart.Occupied | (fire ? ConstructParts.KeyFire : 0)), look.Yaw, look.Pitch);
        }
    }

    /// <summary>The helm's keys this tick: follow the leader, patrol, close on and hold off a target, or hold still.</summary>
    private ushort GroundHelm(ConstructWorld world, Construct c, bool has, in ShipAiTarget t, float dist)
    {
        var com = c.WorldCom;
        var up = GravityFrames.UpAt(com);
        var brief = c.Brief;
        var leader = brief is { ConvoyIndex: > 0, Leader: not 0 } && world.Get(brief.Leader) is { Wreck: false } l && l.Id != c.Id ? l : null;
        if (leader != null)
        {
            // A follower: its place in the leader's frame (as set down), and the leader's speed, a little more to catch up.
            var lf = Level(Vector3.Transform(leader.DriveFwd, leader.Rot), up);
            var lr = Vector3.Cross(lf, up);
            if (!_gOffsetSet)
            {
                var d = com - leader.WorldCom;
                _gOffset = Vector3.DistanceSquared(com, leader.WorldCom) < 250 * 250
                    ? new Vector3(Vector3.Dot(d, lr), 0, Vector3.Dot(d, lf))
                    : new Vector3((brief!.Lane) * VehicleKit.VehicleConvoys.Lane, 0, -brief.ConvoyIndex * (brief.Length + VehicleKit.VehicleConvoys.Gap + 2));
                _gOffsetSet = true;
            }
            var spot = leader.WorldCom + lr * _gOffset.X + lf * _gOffset.Z;
            var behind = Vector3.Dot(spot - com, lf); // (m it is behind its place)
            var lv = Vector3.Dot(leader.Vel, lf);
            var want = Math.Clamp(lv + behind * 0.35f, 0, GroundClose * 1.5f);
            _state = 7;
            _stateDist = behind;
            return Drive(c, spot + lf * (FollowAhead + MathF.Max(0, lv)), want, up, stopAt: false);
        }
        if (!_gHomeSet)
        {
            _gHome = com;
            _gWay = Level(Vector3.Transform(c.DriveFwd, c.Rot), up);
            _gHomeSet = true;
        }
        var convoyLead = brief is { Convoy: not 0, ConvoyIndex: 0 };
        if (convoyLead)
        {
            // The leader drives out and back; its gunners and the followers' fight what comes, the convoy doesn't stop for it.
            var end = _gLeg > 0 ? _gHome + _gWay * PatrolLeg : _gHome;
            if (Vector3.Distance(Level(end - com, up), Vector3.Zero) < 25)
                _gLeg = (sbyte)-_gLeg;
            _state = 8;
            _stateDist = Vector3.Distance(end, com);
            return Drive(c, end, has ? GroundCruise * 0.8f : GroundCruise, up, stopAt: false);
        }
        if (!has)
        {
            _state = 2;
            return (ushort)Buttons.Jump; // (a lone vehicle holds where it is)
        }
        // A lone vehicle closes to its guns' range and holds there, its weak side off the enemy.
        var band = brief is { Engage: > 0 } ? brief.Engage : GroundEngage;
        _stateDist = dist;
        if (dist > band + GroundBand)
        {
            _state = 4;
            return Drive(c, t.At, GroundClose, up, stopAt: false);
        }
        if (dist < band - GroundBand * 2)
        {
            _state = 5;
            var away = Level(com - t.At, up);
            return Drive(c, com + away * 40, GroundClose * 0.7f, up, stopAt: false);
        }
        _state = 6;
        if (brief is { KeepAway: < 4 } && Vector3.Dot(c.Vel, c.Vel) < 4)
        {
            // Which side faces the enemy, in its own frame: on the weak side, creep forward turning away.
            var to = Vector3.Transform(Level(t.At - com, up), Quaternion.Conjugate(c.Rot));
            var bearing = MathF.Atan2(Vector3.Dot(to, Vector3.Cross(c.DriveFwd, Vector3.UnitY)), Vector3.Dot(to, c.DriveFwd)) * 180 / MathF.PI;
            var side = VehicleKit.VehicleBrief.SideOf(bearing);
            if (side == brief.KeepAway)
                return (ushort)(Buttons.Forward | (side == 1 ? Buttons.Left : side == 3 ? Buttons.Right : 0));
        }
        return (ushort)Buttons.Jump;
    }

    /// <summary>W, S, A, D and the brake to go towards <paramref name="to"/> at about <paramref name="speed"/> m/s; a vehicle stuck with its throttle open backs off a moment.</summary>
    private ushort Drive(Construct c, Vector3 to, float speed, Vector3 up, bool stopAt)
    {
        var com = c.WorldCom;
        var fwd = Level(Vector3.Transform(c.DriveFwd, c.Rot), up);
        var d = to - com;
        d -= up * Vector3.Dot(d, up);
        var dist = d.Length();
        var vf = Vector3.Dot(c.Vel, fwd);
        if (dist < 1 || speed <= 0.2f && MathF.Abs(vf) < 0.5f)
            return (ushort)Buttons.Jump;
        var dir = d / dist;
        var ang = MathF.Atan2(Vector3.Dot(Vector3.Cross(fwd, dir), up), Vector3.Dot(fwd, dir)); // (+: to its left)
        Buttons keys = 0;
        if (_tick < _gReverseUntil)
            return (ushort)(Buttons.Back | (ang > 0 ? Buttons.Right : Buttons.Left)); // (backing off: the nose swings the way it wants)
        if (MathF.Abs(ang) > 0.08f)
            keys |= ang > 0 ? Buttons.Left : Buttons.Right;
        var want = speed * Math.Clamp(1 - MathF.Abs(ang) / 2.4f, 0.3f, 1);
        if (stopAt)
            want = MathF.Min(want, dist * 0.4f);
        if (vf < want - 0.5f)
            keys |= Buttons.Forward;
        else if (vf > want + 3f)
            keys |= Buttons.Jump;
        // Stuck: its throttle open and hardly moving for 3 s, it backs off for 2.
        if ((keys & Buttons.Forward) != 0 && MathF.Abs(vf) < 0.4f)
        {
            if (++_gStuck > 3 * World.TickRate)
            {
                _gStuck = 0;
                _gReverseUntil = _tick + 2 * World.TickRate;
            }
        }
        else
            _gStuck = 0;
        return (ushort)keys;
    }

    private static Vector3 Level(Vector3 v, Vector3 up)
    {
        v -= up * Vector3.Dot(v, up);
        var n = v.Length();
        return n > 1e-4f ? v / n : Vector3.Cross(up, Vector3.UnitX) is var x && x.LengthSquared() > 1e-4f ? Vector3.Normalize(x) : -Vector3.UnitZ;
    }
}
