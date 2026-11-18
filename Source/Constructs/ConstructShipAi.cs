using System.Numerics;

namespace CallOfSlop;

/// <summary>What a ship's AI crew fights (<see cref="CfgKey.AiCrew"/> on its Flight Stabilizer).</summary>
public enum ShipAiMode : byte
{
    Off,
    Strangers, // anyone outside its owner's faction (a nobody's ship: everyone)
    Everyone, // its owner too: a test enemy spawned from the parts menu
}

/// <summary>Who a crew is after this tick: a body (a ship or a car someone drives or rides) or a player on foot.</summary>
public struct ShipAiTarget
{
    public Vector3 At, Vel; // its centre and velocity (world)
    public ushort Body; // 0: a player on foot
    public byte Player; // the player it is after (255: a body with nobody, in a check)
}

/// <summary>
/// An AI crew for a ship (docs/sandbox/constructs/torpedo-frigate.md "The crew"): server only. It works the ship's own seats, as players
/// would, so nothing new goes over the wire: the pilot's seat flies it through its Flight Stabilizer (mouse aim, so it turns to where
/// the pilot looks; Shift and Ctrl for height; W and S for range; Q and E to slide about), the gunners' seats lay and fire their turrets,
/// and the Buttons wired to Ordnance Racks launch them. It closes on its target, keeps its guns' range, and holds where it is with
/// nothing to fight. A player in a seat has that seat: in the pilot's, the helm is theirs and the crew stands down.
/// Since the bot brain (docs/sandbox/design/bot-brain.md, phase 1) it drives only what its crew mans (<see cref="Crew"/>): the helm
/// with a living pilot at it (or for <see cref="CrewWorld.HelmGraceTicks"/> after one is lost, its last input held), and each gun
/// seat with a living gunner. With no crew set (a tool) it works every seat.
/// </summary>
public sealed partial class ShipAi
{
    /// <summary>How far it sees a target (m), the range it keeps (a little inside its guns' 300 m), and the band either side it is content in.</summary>
    public const float EngageRange = 2500, StandOff = 220, StandBand = 40;

    /// <summary>The height it keeps above its target (m), the least clearance over the ground under it and ahead of it (m), and how far ahead it looks (s).</summary>
    public const float AboveTarget = 4, MinClearance = 35, LookAhead = 5;

    /// <summary>Racks: launched from this far to this far (m), this long apart (s), and only at a target this close to their axis (degrees).</summary>
    public const float LaunchMin = 350, LaunchMax = 2000, LaunchGap = 8, LaunchCone = 15;

    /// <summary>How far off a gun may point and still fire (degrees): a fixed gun the ship lays, a turret its gunner lays.</summary>
    public const float FixedBear = 2.5f, TurretBear = 4f;

    /// <summary>A spotter beside the capital gun (bot-brain.md phase 7) shares the target's range and lead: its gunner fires only this share as far off.</summary>
    public const float SpotBear = 0.5f;

    private const int ScanTicks = 120, LookTicks = 30; // (its seats, guns and racks again; the ground under it and ahead)
    private const int MaxSeats = 16, MaxRacks = 16;

    public ushort Body;
    public ShipAiMode Mode;
    /// <summary>The crew that mans its seats (the rule-1 gate), and its roster on this body; null: every seat is worked (a tool).</summary>
    public CrewWorld? Crew;
    public CrewShip? Ship;
    /// <summary>What it is doing, for the check's lines and a log (made when read: nothing on the tick).</summary>
    /// <summary>The keys that slide it sideways (by its flight pattern: Scan).</summary>
    private ushort SlideLeft = ConstructParts.KeyRollLeft, SlideRight = ConstructParts.KeyRollRight;

    private bool _shipPattern;

    /// <summary>
    /// A ship-pattern craft holds no level of its own (ConstructFlight: "no level held"): a slide's push off its centre rolls it a little at a time
    /// until it lies on its side, loses its lift and settles on the ground for good (phase 2's frigates that settled mid-route). Its pilot keeps it
    /// level with Q and E, and stops sliding while it rights itself.
    /// </summary>
    private ushort Level(Construct c, Vector3 up, ushort keys)
    {
        if (!_shipPattern)
            return keys;
        var tilt = Vector3.Dot(Vector3.Transform(Vector3.UnitX, c.Rot), up); // (> 0: its right side is up)
        if (MathF.Abs(tilt) < 0.03f)
            return keys;
        keys &= unchecked((ushort)~(ConstructParts.KeyRollLeft | ConstructParts.KeyRollRight));
        if (MathF.Abs(tilt) > 0.12f)
            keys &= unchecked((ushort)~(SlideLeft | SlideRight));
        return (ushort)(keys | (tilt > 0 ? ConstructParts.KeyRollRight : ConstructParts.KeyRollLeft));
    }

    /// <summary>A check's look inside (CrewAdmiralsCheck): the clearances under and ahead, the pilot's keys.</summary>
    public string Debug => FormattableString.Invariant($"ground {(_ground > 1e6 ? -1 : _ground):0} ahead {(_ahead > 1e6 ? -1 : _ahead):0} keys {_pilotKeys:X}");

    public string State => _state switch
    {
        1 => "no pilot's seat",
        2 => "no target: holding",
        3 => "no target: landing",
        4 => $"closing, {_stateDist:0} m",
        5 => $"opening, {_stateDist:0} m",
        6 => $"holding at {_stateDist:0} m",
        7 => $"following, {_stateDist:0} m behind its place",
        8 => $"patrolling, {_stateDist:0} m to go",
        9 => $"fleeing, {_stateDist:0} m",
        10 => $"ordered: {_orderKind.ToString().ToLowerInvariant()}, {_stateDist:0} m",
        _ => "standing by",
    };
    private int _state;
    private float _stateDist;
    public int Shots, GunnerShots, Launches, Flares; // (fired, for the check; GunnerShots: from gunners' seats)

    private int _stab = -1, _pilot = -1;
    private readonly short[] _seats = new short[MaxSeats]; // gunners' seats (the pilot's is _pilot)
    private int _seatCount;
    private readonly short[] _rackGun = new short[MaxRacks], _rackButton = new short[MaxRacks]; // (an index into Construct.Racks, and the Button that fires it)
    private readonly short[] _flares = new short[MaxRacks]; // the Buttons that fire its countermeasures
    private int _flareCount, _flaresAt;
    private int _rackCount, _nextRack;
    private int _tick, _structure = -1;
    private float _ground = float.MaxValue, _ahead = float.MaxValue; // clearance under it and along its way (m)
    private int _launchAt, _strafeUntil, _idle;
    private sbyte _strafe = 1;

    // What each seat was last told (Replay holds it on the ticks a far crew doesn't think).
    private ushort _pilotKeys;
    private float _pilotYaw, _pilotPitch;
    private readonly ushort[] _seatKeys = new ushort[MaxSeats];
    private readonly float[] _seatYaw = new float[MaxSeats], _seatPitch = new float[MaxSeats];
    private bool _pilotWas;

    public ShipAi(ushort body, ShipAiMode mode)
    {
        Body = body;
        Mode = mode;
    }

    /// <summary>Back to new, for another body (the crew's pool of drivers).</summary>
    public void Reset(ushort body, ShipAiMode mode)
    {
        Body = body;
        Mode = mode;
        _state = 0;
        _stab = _pilot = -1;
        _seatCount = _rackCount = _flareCount = _nextRack = 0;
        _tick = 0;
        _structure = -1;
        _ground = _ahead = float.MaxValue;
        _launchAt = _strafeUntil = _idle = _flaresAt = 0;
        _pilotKeys = 0;
        _pilotWas = false;
        Shots = GunnerShots = Launches = Flares = PointDefenceShots = 0;
        Crew = null;
        Ship = null;
        GroundReset();
    }

    /// <summary>Its crew works seat <paramref name="seat"/> (no crew: a tool's, every seat is worked).</summary>
    private bool Manned(int seat) => Crew == null || Ship == null || Ship.Manned(Crew, seat);

    /// <summary>The helm is empty but keeps its last input a little while (the user's answer 2).</summary>
    private bool Grace => Crew != null && Ship != null && _pilotWas && Ship.InGrace(Crew.Tick);

    /// <summary>The crew a Flight Stabilizer's setting asks for on this construct (its first stabilizer: the one that flies it).</summary>
    public static ShipAiMode ModeOf(Construct c)
    {
        if (c.StabCount == 0 || c.Stabs[0].Part >= c.PartCount)
            return ShipAiMode.Off;
        ref var p = ref c.Parts[c.Stabs[0].Part];
        return p.Broken ? ShipAiMode.Off : (ShipAiMode)Math.Clamp((int)ConstructConfig.Get(p, p.D, CfgKey.AiCrew, 0), 0, 2);
    }

    /// <summary>What it works, for a check's line (allocates).</summary>
    public string Describe(Construct c)
    {
        Scan(c);
        var racks = string.Join(", ", Enumerable.Range(0, _rackCount).Select(r => $"{c.Parts[c.Racks[_rackGun[r]].Part].D.Name} ({c.Racks[_rackGun[r]].Held} held)"));
        var guns = string.Join(", ", Enumerable.Range(0, c.GunCount).Where(k => c.Guns[k].Seat >= 0).Select(k => $"{c.Parts[c.Guns[k].Part].D.Name} from seat {c.Guns[k].Seat}"));
        return $"pilot's seat {_pilot}, {_seatCount} gunners' seats, {_flareCount} flare buttons; racks: {(racks.Length > 0 ? racks : "none")}; guns: {guns}";
    }

    /// <summary>Its pilot's seat has a player in it: the helm is theirs.</summary>
    public bool Boarded(Construct c, CrewWorld? crew = null) => _pilot >= 0 && _pilot < c.PartCount && c.Parts[_pilot].SitterPlus1 is var s && s != 0 && (crew == null || !crew.IsBody[s - 1]);

    /// <summary>Its seats, guns and racks, from the construct as it is now.</summary>
    private void Scan(Construct c)
    {
        _structure = c.PartCount;
        _stab = c.StabCount > 0 ? c.Stabs[0].Part : -1;
        _pilot = _stab >= 0 ? ConstructFlight.SeatFor(c, _stab) : Crew != null ? c.DriveSeat : -1; // (no stabilizer: a ground vehicle's drive seat, driven by Source/Crew/CrewGround.cs)
        _onGround = _stab < 0 && _pilot >= 0;
        _seatCount = 0;
        _rackCount = 0;
        _flareCount = 0;
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var g = ref c.Guns[k];
            if (g.Seat >= 0 && g.Seat != _pilot && _seatCount < MaxSeats && Array.IndexOf(_seats, g.Seat, 0, _seatCount) < 0)
                _seats[_seatCount++] = g.Seat;
            if (g.Seat < 0 && (g.Effects & GunEffect.Shell) != 0 && ButtonInto(c, g.Part) is var b and >= 0 && _flareCount < MaxRacks && Array.IndexOf(_flares, (short)b, 0, _flareCount) < 0)
                _flares[_flareCount++] = (short)b; // (a launcher no seat fires, on a Button: the Zero Day's flares)
        }
        // The Ordnance Racks fired by a Button (the Zero Day's torpedo room), each with its button.
        for (var k = 0; k < c.RackCount && _rackCount < MaxRacks; k++)
        {
            int part = c.Racks[k].Part;
            if (part < c.PartCount && ButtonInto(c, part) is var b and >= 0)
            {
                _rackGun[_rackCount] = (short)k;
                _rackButton[_rackCount++] = (short)b;
            }
        }
        c.CrewAims = _stab >= 0 && Ship is not { Own: true, Group: < 0 };
        // Its slide keys: a ship-pattern craft (FlightPattern.Ship: the Zero Day) slides on A and D while the crew has its nose (Q and E roll it,
        // and a frigate rolled on its side lost its lift and settled on the ground: faction-controllers-notes.md phase 6); a rotorcraft on Q and E.
        var ship = _stab >= 0 && ConstructFlight.KeysOf(c, c.Parts[_stab], c.Parts[_stab].D) == FlightPattern.Ship;
        _shipPattern = ship;
        SlideLeft = ship ? (ushort)Buttons.Left : ConstructParts.KeyRollLeft;
        SlideRight = ship ? (ushort)Buttons.Right : ConstructParts.KeyRollRight; // (its stabilizer turns to where the crew's pilot looks, whatever its Mouse aim setting; never saved. A player's own crew flies it only in one of the player's fleet groups: phase 6)
    }

    /// <summary>A Button wired into part <paramref name="part"/>, or -1.</summary>
    private static int ButtonInto(Construct c, int part)
    {
        for (var w = 0; w < c.WireCount; w++)
            if (c.Wires[w].To == part && c.Wires[w].From < c.PartCount && c.Parts[c.Wires[w].From].D.Logic == CLogic.Button)
                return c.Wires[w].From;
        return -1;
    }

    /// <summary>The crew stands down (the setting went off, or a player took the helm): its stabilizer goes back to its own Mouse aim setting.</summary>
    public void Release(Construct c)
    {
        c.CrewAims = false;
        c.CrewDrives = false;
        _structure = -1;
        _state = 0;
    }

    /// <summary>
    /// One tick, before the constructs step: the keys and looks of its seats. <paramref name="has"/> false: nothing to fight. Server only;
    /// allocates nothing.
    /// </summary>
    public void Step(ConstructWorld world, Construct c, IWorldQuery query, bool has, in ShipAiTarget t)
    {
        if (c.Dirty)
            c.Rebuild();
        if (_structure != c.PartCount || ++_tick % ScanTicks == 0)
            Scan(c);
        if (_pilot < 0 || _pilot >= c.PartCount || c.Parts[_pilot].Broken)
        {
            _state = 1;
            c.CrewDrives = false;
            return;
        }
        if (_onGround)
        {
            StepGround(world, c, query, has, t); // (Source/Crew/CrewGround.cs)
            return;
        }
        var com = c.WorldCom;
        var up = GravityFrames.UpAt(com);
        if (_tick % LookTicks == 0)
        {
            _ground = Clearance(world, c, query, com, -up, 400);
            var way = c.Vel - up * Vector3.Dot(c.Vel, up);
            var far = way.Length() * LookAhead;
            _ahead = far > 10 ? Clearance(world, c, query, com, way / way.Length(), far, terrainOnly: Ordered(out var lo) && lo.Kind is OrderKind.Form or OrderKind.Escort or OrderKind.Screen) : float.MaxValue;
            // (in a formation the ship ahead is its anchor or a group mate, kept clear by the slots' spacing: only the ground ahead makes it climb,
            // or a formation behind a ship climbs for ever: faction-controllers-notes.md phase 6)
        }
        var pilotOn = Manned(_pilot);
        if (pilotOn)
            _pilotWas = true;
        var grace = !pilotOn && Grace;
        if (OrderFly(world, c, has, t, com, up, pilotOn, grace))
            return; // (its admiral's order: a slot, a place, a screen or a retreat: Source/Crew/CrewOrders.cs)
        ushort pilot = CPart.Occupied;
        if (!has)
        {
            // Nothing to fight: it holds where it is, and after half a minute comes down where it is (the stabilizer lets go once landed).
            _idle++;
            if (_idle > 30 * World.TickRate && _ground < 400)
                pilot |= (ushort)Buttons.Crouch;
            _state = _idle > 30 * World.TickRate ? 3 : 2;
            if (pilotOn)
                PilotSeat(c, pilot, c.Parts[_pilot].AimYaw, c.Parts[_pilot].AimPitch);
            else if (grace)
                Seat(c, _pilot, _pilotKeys, _pilotYaw, _pilotPitch);
            for (var s = 0; s < _seatCount; s++)
                if (Manned(_seats[s]) && !(_seats[s] < c.PartCount && SeatFree(c, _seats[s]) && !c.Parts[_seats[s]].Broken && PointDefence(world, c, s)))
                    GunSeat(c, s, CPart.Occupied, c.Parts[_seats[s]].AimYaw, c.Parts[_seats[s]].AimPitch);
            return;
        }
        _idle = 0;
        var line = t.At - com;
        var dist = line.Length();
        var dir = dist > 1 ? line / dist : Vector3.UnitZ;

        // Height: level with the target, a little over it, never low over the ground or into a hill ahead.
        var height = ConstructPhysics.HoldHeight(com);
        var want = ConstructPhysics.HoldHeight(t.At) + AboveTarget;
        if (_ground < MinClearance)
            want = MathF.Max(want, height + (MinClearance - _ground));
        var climb = Vector3.Dot(c.Vel, up);
        if (_ahead < MinClearance * 2 || want - height > 6 && climb < 25)
            pilot |= (ushort)Buttons.Sprint;
        else if (want - height < -6 && climb > -15 && _ground > MinClearance)
            pilot |= (ushort)Buttons.Crouch;

        // Range: a closing speed that falls to nothing at the stand-off, held with W and S (it faces its target, so forward is towards it).
        var close = Vector3.Dot(c.Vel - t.Vel, dir);
        var gap = dist - StandOffNow; // (a Board order closes to boarding range)
        var wantClose = Math.Clamp(gap * 0.12f, -25, 60);
        if (wantClose - close > 2)
            pilot |= (ushort)Buttons.Forward;
        else if (wantClose - close < -2)
            pilot |= (ushort)Buttons.Back;
        // In its band it slides one way and then the other, so it isn't a sitting target.
        if (MathF.Abs(gap) < StandBand * 2)
        {
            if (_tick >= _strafeUntil)
            {
                _strafe = (sbyte)-_strafe;
                _strafeUntil = _tick + (6 + (int)(c.Id % 5)) * World.TickRate;
            }
            pilot |= _strafe > 0 ? SlideRight : SlideLeft;
        }
        pilot = Level(c, up, pilot);
        _state = gap > StandBand ? 4 : gap < -StandBand ? 5 : 6;
        _stateDist = dist;
        var fleeing = Fleeing(ref pilot); // (its captain runs: nose away, full ahead; Source/Crew/CrewGround.cs)
        var held = WardInLine(world, com, t.At); // (what its admiral has it guard is in the line of fire: nobody fires through it, Source/Crew/CrewOrders.cs)

        // The guns: each seat looks at where its guns' rounds meet the target, and fires when a gun on it bears and reaches.
        if (pilotOn)
        {
            var (pilotLook, pilotFire) = fleeing ? (GravityFrames.Angles(com, -dir * 100), false) : Gunnery(world, c, _pilot, t, dist, fixedGuns: true);
            if (pilotFire && !held)
                pilot |= ConstructParts.KeyFire;
            PilotSeat(c, pilot, pilotLook.Yaw, pilotLook.Pitch);
        }
        else if (grace)
            Seat(c, _pilot, (ushort)(_pilotKeys & ~ConstructParts.KeyFire), _pilotYaw, _pilotPitch); // (the last input held; nobody fires)
        for (var s = 0; s < _seatCount; s++)
        {
            int seat = _seats[s];
            if (seat >= c.PartCount || !SeatFree(c, seat) || c.Parts[seat].Broken || !Manned(seat))
                continue; // (a player's seat is theirs; an empty gunner's seat fires nothing)
            if (PointDefence(world, c, s))
                continue; // (a missile coming for it first)
            var (look, fire) = Gunnery(world, c, seat, t, dist, fixedGuns: false);
            GunSeat(c, s, (ushort)(CPart.Occupied | (fire && !held ? ConstructParts.KeyFire : 0)), look.Yaw, look.Pitch);
        }
        if (!pilotOn)
            return; // (the racks and flares are the pilot's buttons)

        // The racks: one launch at a time, taking turns, at a ship or a car (a seeker wants something that moves), with the target off the nose.
        if (!fleeing && !held && _rackCount > 0 && t.Body != 0 && dist is > LaunchMin and < LaunchMax && _tick >= _launchAt)
        {
            var nose = Vector3.Transform(-Vector3.UnitZ, c.Rot); // (the racks' ports face forward)
            if (Vector3.Dot(nose, dir) > MathF.Cos(LaunchCone * MathF.PI / 180))
                for (var n = 0; n < _rackCount; n++)
                {
                    var r = (_nextRack + n) % _rackCount;
                    int k = _rackGun[r], button = _rackButton[r];
                    if (k >= c.RackCount || c.Racks[k].Held == 0 || button >= c.PartCount || c.Parts[button].Broken)
                        continue;
                    ConstructRadar.SetLock(world, c, t.Body); // (the radar's lock for the torpedo, when the ship can lock: a Fire Director wired to its radar)
                    Press(c, button, 2); // (two ticks: one round out before the rack's interval)
                    _nextRack = r + 1;
                    _launchAt = _tick + (int)(LaunchGap * World.TickRate);
                    Launches++;
                    break;
                }
        }

        // Flares: when a missile steers for it, every few seconds while one does.
        if (_flareCount > 0 && _tick >= _flaresAt && _tick % 10 == 0 && Incoming(world, c, com))
        {
            for (var f = 0; f < _flareCount; f++)
                if (_flares[f] < c.PartCount && !c.Parts[_flares[f]].Broken)
                    Press(c, _flares[f], 2);
            _flaresAt = _tick + 3 * World.TickRate;
            Flares++;
        }
    }

    /// <summary>A press of a Button, on for <paramref name="ticks"/>.</summary>
    private static void Press(Construct c, int button, int ticks)
    {
        c.Parts[button].Count = (ushort)ticks;
        c.Wake();
    }

    /// <summary>A missile is coming for it, inside a kilometre.</summary>
    private static bool Incoming(ConstructWorld world, Construct c, Vector3 at)
    {
        for (var k = 0; k < world.ShellCount; k++)
        {
            ref var sh = ref world.Shells[k];
            if (sh.Target == c.Id && sh.Gun != c.Id && sh.Decoy == 0 && Vector3.DistanceSquared(sh.Pos, at) < 1000 * 1000)
                return true;
        }
        return false;
    }

    /// <summary>Point defence: a missile this many ticks ahead is aimed at (what it closes in the tick the turret takes to fire).</summary>
    public const int PointDefenceLeadTicks = 2;

    public int PointDefenceShots; // (rounds fired at missiles, for the check)

    /// <summary>
    /// Point defence: gunner's seat <paramref name="s"/> turns its turrets on the nearest missile or torpedo steering for this ship (not one a flare has
    /// drawn off) inside its guns' reach, and fires when a gun bears, before anything else it would shoot at. False when there is none (the seat
    /// is left to the caller). The rounds meet it in the air (ConstructWeapons.Round: RayShell). Allocates nothing.
    /// </summary>
    private bool PointDefence(ConstructWorld world, Construct c, int s)
    {
        if (world.ShellCount == 0)
            return false;
        int seat = _seats[s];
        var reach = 0f;
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var g = ref c.Guns[k];
            if (g.Seat == seat && g.Def != null && g.AimJoints > 0 && g.Part < c.PartCount && !c.Parts[g.Part].Broken && (g.Effects & GunEffect.Shell) == 0)
                reach = MathF.Max(reach, g.MaxRange);
        }
        if (reach <= 0)
            return false;
        var eye = ConstructWeapons.SeatEye(c, seat);
        var best = -1;
        var bestD = reach * reach;
        for (var k = 0; k < world.ShellCount; k++)
        {
            ref var sh = ref world.Shells[k];
            if (sh.Landed || sh.Target != c.Id || sh.Gun == c.Id || sh.Decoy != 0 || sh.Design == null && sh.Look == null)
                continue; // (only what steers for it and a round can meet: RayShell's rule)
            var d = Vector3.DistanceSquared(sh.Pos, eye);
            if (d < bestD)
            {
                bestD = d;
                best = k;
            }
        }
        if (best < 0)
            return false;
        ref var m = ref world.Shells[best];
        var t = new ShipAiTarget { At = m.Pos + (m.Vel - c.Vel) * (PointDefenceLeadTicks * World.TickDt), Vel = c.Vel, Player = 255 }; // (the rounds are there at once inside the guns' reach: no lead for their flight)
        var shots = Shots;
        var (look, fire) = Gunnery(world, c, seat, t, MathF.Sqrt(bestD), fixedGuns: false);
        if (fire)
            PointDefenceShots += Shots - shots;
        GunSeat(c, s, (ushort)(CPart.Occupied | (fire ? ConstructParts.KeyFire : 0)), look.Yaw, look.Pitch);
        return true;
    }

    /// <summary>Where seat <paramref name="seat"/> looks this tick (at the target, led for its guns' rounds) and whether it fires.</summary>
    private ((float Yaw, float Pitch), bool) Gunnery(ConstructWorld world, Construct c, int seat, in ShipAiTarget t, float dist, bool fixedGuns)
    {
        var eye = ConstructWeapons.SeatEye(c, seat);
        var speed = 0f;
        for (var k = 0; k < c.GunCount; k++)
            if (c.Guns[k].Seat == seat && c.Guns[k].Def != null)
                speed = MathF.Max(speed, c.Guns[k].RoundV > 0 ? c.Guns[k].RoundV : c.Guns[k].Speed);
        var at = Lead(c, eye, t, speed);
        var look = GravityFrames.Angles(c.WorldCom, at - eye);
        var fire = false;
        var spotted = Ship != null && Crew != null ? Crew.Spotted(Ship) : -1; // (phase 7: a spotter at the capital gun)
        for (var k = 0; k < c.GunCount && !fire; k++)
        {
            ref var g = ref c.Guns[k];
            if (g.Seat != seat || g.Def == null || g.Part >= c.PartCount || c.Parts[g.Part].Broken || dist > g.MaxRange * 1.2f)
                continue;
            ConstructWeapons.MuzzleWorld(c, k, out var muzzle, out var fwd);
            var to = Lead(c, muzzle, t, g.RoundV > 0 ? g.RoundV : g.Speed) - muzzle;
            var bear = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(fwd), Vector3.Normalize(to)), -1, 1)) * 180 / MathF.PI;
            if (bear > (g.AimJoints > 0 ? TurretBear : FixedBear) * (g.Part == spotted ? SpotBear : 1))
                continue;
            // Never through its own hull: a turret on the far beam holds.
            if (!world.Raycast(muzzle + fwd * 0.5f, Vector3.Normalize(fwd), MathF.Min(to.Length(), 60), out _, only: c))
                fire = true;
        }
        if (fire)
        {
            Shots++;
            if (!fixedGuns)
                GunnerShots++;
        }
        return (look, fire);
    }

    /// <summary>The point to aim at from <paramref name="from"/>: ahead of the target by its rounds' time of flight (not at all for a round that steers or a speed of 0).</summary>
    private static Vector3 Lead(Construct c, Vector3 from, in ShipAiTarget t, float speed)
    {
        if (speed <= 1)
            return t.At;
        var rel = t.Vel - c.Vel; // (a round takes the ship's speed with it)
        var secs = 0f;
        for (var i = 0; i < 3; i++)
            secs = MathF.Min(6, Vector3.Distance(from, t.At + rel * secs) / speed);
        return t.At + rel * secs;
    }

    /// <summary>The seat has nobody in it, or one of the crew's bodies.</summary>
    private bool SeatFree(Construct c, int seat) => c.Parts[seat].SitterPlus1 is var s && (s == 0 || Crew != null && Crew.IsBody[s - 1]);

    private void PilotSeat(Construct c, ushort keys, float yaw, float pitch)
    {
        _pilotKeys = keys;
        _pilotYaw = yaw;
        _pilotPitch = pitch;
        Seat(c, _pilot, keys, yaw, pitch);
    }

    private void GunSeat(Construct c, int s, ushort keys, float yaw, float pitch)
    {
        _seatKeys[s] = keys;
        _seatYaw[s] = yaw;
        _seatPitch[s] = pitch;
        Seat(c, _seats[s], keys, yaw, pitch);
    }

    /// <summary>
    /// A tick a far crew doesn't think (CrewWorld: a seated crew thinks every other tick): each manned seat is told what it was last time,
    /// so the helm and guns carry on. Allocates nothing.
    /// </summary>
    public void Replay(Construct c)
    {
        if (_structure != c.PartCount || _pilot < 0 || _pilot >= c.PartCount || c.Parts[_pilot].Broken)
            return;
        _tick++;
        if (Manned(_pilot))
            Seat(c, _pilot, _pilotKeys, _pilotYaw, _pilotPitch);
        else if (Grace)
            Seat(c, _pilot, (ushort)(_pilotKeys & ~ConstructParts.KeyFire), _pilotYaw, _pilotPitch);
        else if (_onGround)
            GroundStop(c);
        for (var s = 0; s < _seatCount; s++)
            if (_seats[s] < c.PartCount && Manned(_seats[s]))
                Seat(c, _seats[s], _seatKeys[s], _seatYaw[s], _seatPitch[s]);
    }

    private void Seat(Construct c, int seat, ushort keys, float yaw, float pitch)
    {
        ref var p = ref c.Parts[seat];
        if (!SeatFree(c, seat))
            return;
        p.Keys = keys;
        p.AimYaw = yaw;
        p.AimPitch = pitch;
        if (seat == c.DriveSeat)
            ConstructEdits.DriveInput(c, (Buttons)(keys & 0xFF), World.TickDt);
        if ((keys & ~CPart.Occupied) != 0)
            c.Wake();
    }

    /// <summary>How far the ground or another body is from the ship's centre along a way, out to <paramref name="reach"/> (m), less the ship's own extent that way.</summary>
    private static float Clearance(ConstructWorld world, Construct c, IWorldQuery query, Vector3 from, Vector3 way, float reach, bool terrainOnly = false)
    {
        var d = query.Raycast(from, way, reach + c.Radius, QueryFilter.All);
        if (!terrainOnly && world.Raycast(from, way, d, out var hit, c, ignoreCargo: true) && hit.Distance < d) // (not what it carries: a Zero Day's docked Limpet under it read as the ground 4 m down, and it climbed for ever)
            d = hit.Distance;
        var local = Vector3.Transform(way, Quaternion.Conjugate(c.Rot));
        var own = MathF.Abs(local.X) * MathF.Max(-c.LocalMin.X, c.LocalMax.X) + MathF.Abs(local.Y) * MathF.Max(c.Com.Y - c.LocalMin.Y, c.LocalMax.Y - c.Com.Y) + MathF.Abs(local.Z) * MathF.Max(-c.LocalMin.Z, c.LocalMax.Z);
        return d >= reach + c.Radius ? float.MaxValue : d - own;
    }
}
