using System.Numerics;

namespace CallOfSlop;

/// <summary>A thruster's cached set-up (rebuilt when the construct changes).</summary>
public struct CThruster
{
    public ushort Part;
    public byte Seg; // the segment it hangs on: a thruster on a turntable or a hinge vectors its push
    public Vector3 At, Dir; // the segment's frame: where it pushes, and which way (away from its exhaust)
    public float MixForward, MixYaw, MixRoll; // how much of the driver's throttle, steering and roll it takes (-1..1)
    public bool Lifts; // it pushes up the body: the hover hold works it
    public bool Fed; // on fuel: a tank reaches it
    public float Level; // 0..1 (it follows what it is asked over its spool)
    public float Ab; // 0..1: its afterburner, lit
    public Vector3 GimbalX, GimbalY; // a nozzle: the axes its pitch and yaw inputs turn it about (the segment's frame)
    public float AngleX, AngleY; // ...and how far it is turned, radians
    public bool Scanned, GimbalScanned; // a stabilizer commands it (ConstructEffectors.cs), and its nozzle: a wire by hand leaves it to the wire
    public float Command, CommandX, CommandY; // what the stabilizer tells it
    public float Gap; // a repulsor: metres to what is under it, as of its last look (float.MaxValue: nothing in reach)
    public float Shown; // a client: its level as the host last said (CBodyKind.Thrust), for its flame
    public float ShownX, ShownY; // a client: its gimbal's angles as the host last said (radians), for the bell's swing
    public float Cap; // 0..1: the share of its force its works let it have (ConstructWorks.cs; 1 with no works block)
    public float Burn; // its burn at full level as a share of its file's burnRate (over Cap when it is short of works)
    public float LightDelay; // s it takes to light from a standing start: a bell that needs an injector, with no Igniter in its train (0: at once)
    public float Lit; // ...and how long it has been asked to light (host only)
    public bool Train; // a bell that needs an injector (ConstructWorks.InjectorFedHead): its heat is kept for a Sensor Ring
    public float Heat; // ...0..1: its level eased as the drawn heat is (3 s up, 25 s down; host only, kept over rebuilds)
    public int Reheat; // ...and its Reheat Ring (-1: none)
    public float ReheatUsed; // s of boost used (host; it recovers while off, kept over rebuilds)
    public bool ReheatSpent; // ...used up: no boost until it has all recovered
    public bool ReheatOn; // ...boosting on the last step (host; ThrustOf reads it)
    public int Ring; // ...its train's Gimbal Ring, which swings a bell with no gimbal of its own (-1: none; ConstructBoost.GimbalOf)
    public int Valve, Mixer; // a bell that needs an injector: its train's Throttle Valve and Mixture Controller (part indices, -1: none; ConstructWorks.CWorksHead)
    public float Buffer; // a bell fed through a Fuel Injector (ConstructWorks.InjectorFedHead): seconds of full burn its injector holds, run down while its pipe is cut (host only; kept over rebuilds)
}

/// <summary>
/// Big drive (docs/sandbox/CONSTRUCTS.md, "Big drive"): hub motors and thrusters. A hub motor is a suspension with a
/// motor in it: its wheel or track is driven from the circuit, with no shaft to run (so a bogie on a steering pivot is
/// simply powered), by the driver or its own throttle wire. A thruster is a force at a point along its axis, from fuel or
/// power: its throttle wire sets it; unwired, the driver's throttle and steering do by how it is mounted; and the hover
/// hold works every thruster pushing up the body.
/// </summary>
public sealed partial class Construct
{
    public const int MaxThrusters = 512; // (was 64; a raycast-free force each a tick. Past this a thruster is a plain part: logged, docs/perf/LIMITS.md)
    public CThruster[] Thrusters = []; // (sized to the construct's thrusters at a rebuild, doubling; never shrinks)
    public int ThrusterCount;
    public bool HubSkid; // hub motors and no steered wheel: the steering drives the sides against each other
    public bool HasHubMotor;
    public float LiftInput; // the driver's sprint (up) and crouch (down), -1..1
    public float RollInput; // the seat's roll keys, -1 left, 1 right
    public bool HoverOn; // the hover hold: every lifting thruster holds the construct level at HoverY (it stays on with nobody at the controls: a carrier's crew can leave the seat)
    public Vector3 UpNow; // the way up for it now (ConstructPhysics.BodyUp)
    public float HoverY; // the height it holds its centre of mass at (world Y; on a planet, the distance from its centre: ConstructPhysics.HoldHeight)
    public Vector3 TiltHold; // on a planet: the level hold's integral (world axis x rad s), so a steady twist (a lifter a little off) can't leave it leaning
    public float HoverRateNow; // on a planet: how fast the height held moves this tick (m/s), which the hold follows as well as the height
    public float LiftHeld; // how long the driver has held Shift or Ctrl (s): on a planet the hold climbs and sinks faster the longer it is held
    public float ThrustShow; // 0..1: how hard its thrusters push, of all they could (server; clients get it in the pose)
    public bool Swung; // a bell on it stands swung on its gimbal (the host's angles, or what a client was told): only then does the draw look for one (ConstructRender.SwingThruster)
    public long ThrustSentKey; // the server: the levels clients were last told (ConstructThrust.WriteShown)
    public int ThrustDark; // the server: how many times in a row it has told them every thruster is out
    public bool ThrustHeard; // a client: the host has told it each thruster's level since the last rebuild (else a flame burns by ThrustShow)
    public bool ActiveSignal; // this tick a wire asks a hub motor or a thruster for something: it stays awake
    // The nav computer flies it in space (server; Game/GameNav.cs, docs/space/COMMAND.md): how hard its main drive is
    // asked to push (0..1; negative: no order flies it, the driver does), which way the drive pushes (body frame), whether
    // that drive is its lifters, and where the order wants that push to point (world, unit; zero: as it is).
    public float NavThrottle = -1;
    public Vector3 NavAxis, NavAim;
    public bool NavLift;

    /// <summary>Host: the climb an order asks of the stabilizer's autopilot in a sky (m/s; 0: the stabilizer's own limit). Game/GameNav.cs.</summary>
    public float NavClimb;

    /// <summary>Host: an order sets it down (m/s it comes down at, slowing near the ground; 0: none). Its autopilot's height loop reads it (ConstructFlight.cs). Game/GameNavLand.cs.</summary>
    public float NavLand;

    /// <summary>Hub motors on the wheels (with the wheels: a track's rays share its motor).</summary>
    private void BuildHubMotors()
    {
        HubSkid = false;
        HasHubMotor = false;
        var steers = false;
        var right = Vector3.Cross(SeatForward(), Vector3.UnitY);
        for (var k = 0; k < WheelCount; k++)
        {
            ref var w = ref Wheels[k];
            w.SideSign = Vector3.Dot(w.Centre - Com, right) >= 0 ? 1f : -1f;
            ref var s = ref Parts[w.Suspension];
            if (s.D.MotorTorque <= 0 || !s.Working || w.Station)
                continue;
            var rays = MathF.Max(1, Parts[w.Part].D.TrackRays);
            w.MotorTorque = s.D.MotorTorque / rays;
            w.MotorPower = s.D.MotorPower * 1000f / rays;
            w.MotorTop = s.D.MotorTop;
            HasHubMotor = true;
            steers |= w.Steers;
        }
        HubSkid = HasHubMotor && !steers;
    }

    /// <summary>With the caches: the thrusters, and how each answers the driver from where it sits.</summary>
    private void BuildThrusters()
    {
        Span<float> keep = stackalloc float[MaxThrusters];
        Span<ushort> keepPart = stackalloc ushort[MaxThrusters];
        Span<float> keepAb = stackalloc float[MaxThrusters];
        Span<float> keepBuffer = stackalloc float[MaxThrusters];
        Span<float> keepHeat = stackalloc float[MaxThrusters];
        Span<float> keepReheat = stackalloc float[MaxThrusters];
        Span<bool> keepSpent = stackalloc bool[MaxThrusters];
        var old = ThrusterCount;
        for (var k = 0; k < old; k++)
        {
            keepAb[k] = Thrusters[k].Ab;
            keepBuffer[k] = Thrusters[k].Buffer;
            keepHeat[k] = Thrusters[k].Heat;
            keepReheat[k] = Thrusters[k].ReheatUsed;
            keepSpent[k] = Thrusters[k].ReheatSpent;
            keep[k] = Thrusters[k].Level;
            keepPart[k] = Thrusters[k].Part;
        }
        ThrusterCount = 0;
        ThrustHeard = false; // (a client's levels were by the old list: until the host speaks again its flames burn by ThrustShow)
        ThrustSentKey = 0; // (the server tells them again)
        var want = 0;
        foreach (var i in Specials) // (thrusters are special parts)
            if (Parts[i].D.Role == PartRole.Thruster)
                want++;
        if (want > MaxThrusters)
            Caps.Hit("thrusters a construct", MaxThrusters, want);
        want = Math.Min(want, MaxThrusters);
        if (Thrusters.Length < want)
            Array.Resize(ref Thrusters, Math.Min(MaxThrusters, Math.Max(want, Math.Max(8, Thrusters.Length * 2))));
        var most = 1e-3f;
        var mostRoll = 1e-3f;
        var fwd = SeatForward();
        var kept = 0; // (the old list is in part order, as the new one is: one walk through both)
        foreach (var i in Specials)
        {
            if (ThrusterCount >= want)
                break;
            ref var p = ref Parts[i];
            var d = p.D;
            if (d.Role != PartRole.Thruster)
                continue;
            var at = p.Pos + Rot24.Apply(p.Rot, d.ThrustAt);
            var dir = -ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, d.ThrustDir));
            ref var t = ref Thrusters[ThrusterCount];
            ConstructWorks.CapOf(this, i, out var cap, out var burnShare); // (what its works let it push: a dead drive isn't counted on for yaw)
            t = new CThruster { Part = (ushort)i, Seg = p.Seg, At = at, Dir = dir, Lifts = dir.Y > 0.7f && d.ThrustKind is ThrustKind.Plain or ThrustKind.Air or ThrustKind.Rocket, Gap = float.MaxValue, GimbalX = Rot24.Apply(p.Rot, Vector3.UnitX), GimbalY = Rot24.Apply(p.Rot, Vector3.UnitY), Fed = d.ThrustFuel != EngineFuel.Fuel || p.Working && (ConstructWorks.InjectorFedHead(d) ? ConstructWorks.TrainFed(this, i) : FuelReaches(i)), Cap = cap, Burn = burnShare };
            t.MixForward = Vector3.Dot(dir, fwd);
            t.MixYaw = Vector3.Cross(at - Com, dir).Y * d.ThrustForce * cap; // (made a share below)
            most = MathF.Max(most, MathF.Abs(t.MixYaw));
            t.MixRoll = Vector3.Dot(Vector3.Cross(at - Com, dir), fwd) * d.ThrustForce * cap; // (a roll to the right turns about the way it faces, clockwise from behind)
            mostRoll = MathF.Max(mostRoll, MathF.Abs(t.MixRoll));
            while (kept < old && keepPart[kept] < i)
                kept++;
            if (kept < old && keepPart[kept] == i)
            {
                t.Level = keep[kept];
                t.Ab = keepAb[kept];
                t.Buffer = keepBuffer[kept];
                t.Heat = keepHeat[kept];
                t.ReheatUsed = keepReheat[kept];
                t.ReheatSpent = keepSpent[kept];
            }
            if (WorksHeadOf(i) is var wb and >= 0 && ConstructWorks.InjectorFedHead(d))
                t.Buffer = MathF.Min(t.Buffer, WorksHeads[wb].BufferMax); // (a Fuel Buffer shot off takes its seconds with it)
            t.Train = ConstructWorks.InjectorFedHead(d);
            t.Valve = t.Mixer = t.Reheat = t.Ring = -1;
            if (t.Train)
            {
                var wl = WorksHeadOf(i);
                t.LightDelay = wl >= 0 && WorksHeads[wl].Igniter ? 0 : ShipWorks.Current.LightSeconds;
                if (wl >= 0)
                    (t.Valve, t.Mixer, t.Reheat, t.Ring) = (WorksHeads[wl].Valve, WorksHeads[wl].Mixer, WorksHeads[wl].Reheat, WorksHeads[wl].Gimbal);
            }
            if (t.Fed && ConstructWorks.InjectorFedHead(d))
                t.Buffer = WorksHeadOf(i) is var wk and >= 0 ? WorksHeads[wk].BufferMax : ShipWorks.Current.InjectorBuffer; // (fed: its injector and its Fuel Buffers are full)
            ThrusterCount++;
        }
        for (var k = 0; k < ThrusterCount; k++)
        {
            Thrusters[k].MixYaw = -Thrusters[k].MixYaw / most; // (a turn to the right is a clockwise turn from above)
            Thrusters[k].MixRoll /= mostRoll;
        }
    }
}

public static partial class ConstructPhysics
{
    public const float HoverRate = 3f; // m/s up or down the driver moves the height it holds
    public const float HoverRoll = 0.3f; // holding a height, the roll keys lean the lifters this share of what each carries, either way
    public const float HoverLand = 3f; // sinking on, this far above the height asked (it is resting on something): the hover lets go
    public const float FlightDrag = 0.02f; // a lit construct's speed dies this much a second as well as by the air drag
    public const float FlightTurnDrag = 0.5f; // its turning, a second
    public const float DragMass = 20_000f; // above this the air drag thins: it goes with the area, the mass with the volume
    public const float HoverClimbRamp = 20f, HoverClimbMax = 120f; // on a planet: past the first second, Shift or Ctrl moves the height held this much faster each second, up to this (m/s)
    public const float AirScale = 1200f; // on a planet the air thins by e every this many metres above its radius (so a flyer gets fast high up)

    /// <summary>
    /// The height the hover hold measures (m): world Y, or on a planet (point gravity) the distance from its centre, so a
    /// flyer holds its height over the ball wherever it is, not a flat Y that the ground curves away from.
    /// </summary>
    public static float HoldHeight(Vector3 p) => GravityFrames.Point is { } planet ? Vector3.Distance(p, planet.Center) : GravityFrames.HeightOf(p);

    /// <summary>The way up the hold pushes and measures: world +Y, or away from a planet's centre.</summary>
    public static Vector3 HoldUp(Vector3 p) => GravityFrames.Point is { } planet ? GravityFrames.UpOf(planet, p) : GravityFrames.UpAt(p);

    private const float UpTurn = 2f; // rad/s: the most a craft's up turns when the field steps (a zone's wall, a hollow world's cavity)
    private const float NoGravity = 0.01f; // (m/s2)^2: under this it keeps the up it had

    /// <summary>
    /// A craft's up now, from the gravity where it is (<see cref="Construct.UpNow"/>; world +Y on a flat map). It follows a smooth field exactly, turns at most <see cref="UpTurn"/>
    /// over a step, and where gravity all but vanishes it keeps the last good one.
    /// </summary>
    public static Vector3 BodyUp(Construct c, float dt)
    {
        if (!GravityFrames.Curved)
            return c.UpNow = Vector3.UnitY;
        var com = c.WorldCom;
        var have = c.UpNow.LengthSquared() > 0.5f;
        if (have && GravityFrames.GravityAt(com).LengthSquared() < NoGravity)
            return c.UpNow;
        var target = HoldUp(com);
        if (!have)
            return c.UpNow = target;
        var dot = Math.Clamp(Vector3.Dot(c.UpNow, target), -1f, 1f);
        var angle = MathF.Acos(dot);
        var most = UpTurn * dt;
        return c.UpNow = angle <= most ? target : Vector3.Normalize(World.Slerp(c.UpNow, target, most / angle));
    }

    /// <summary>A hold height as the HUD shows it: on a planetoid, above its radius.</summary>
    public static float HoldShown(float height) => GravityFrames.Point != null && World.Map.Planetoid is { } pl ? height - (float)pl.Radius : height;

    /// <summary>How thick the air is (1 at the ground; on a planetoid it thins with height above the radius).</summary>
    public static float AirAt(Vector3 p) => ConstructAir.DragShare(p);

    /// <summary>The air drag (m/s2 for 1 m/s): the same for every small construct, thinning with size past <see cref="DragMass"/>.</summary>
    public static float AirDragOf(Construct c) => c.Mass <= DragMass ? AirDrag : AirDrag * MathF.Cbrt(DragMass / c.Mass);

    /// <summary>A hub motor's push on this wheel contact this tick (N s along its roll), and its work for the circuit.</summary>
    private static float HubDrive(Construct c, ref CWheel w, float vFwd, float dt)
    {
        ref var sp = ref c.Parts[w.Suspension];
        var cmd = sp.InCount > 0 ? Math.Clamp(sp.In, -1, 1)
            : !c.Driven ? 0f
            : c.HubSkid ? Math.Clamp(c.Throttle - c.SteerInput * w.SideSign, -1, 1) : c.Throttle;
        if (cmd == 0 || !c.Electrical || !ConstructPower.On(c, w.Suspension))
            return 0;
        var v = MathF.Abs(vFwd);
        var f = w.MotorTorque / w.Radius;
        f = MathF.Min(f, w.MotorPower / MathF.Max(v, 0.5f));
        if (vFwd * cmd > 0)
            f *= Math.Clamp((w.MotorTop - v) / (0.1f * w.MotorTop), 0, 1);
        c.MotorWork += f * MathF.Abs(cmd) * MathF.Max(v, 0.5f);
        return cmd * f * dt;
    }

    /// <summary>One tick of a construct's thrusters (free and awake): the hover hold, what each is asked, its spool, its push and what it burns.</summary>
    private static void Thrusters(Construct c, IWorldQuery query, in Matrix4x4 invI, float dt)
    {
        if (c.ThrustPuppet)
            return; // (a Board order flies it and lights its jets itself: GameNavBoard.cs)
        var com = c.WorldCom;
        var g = GravityAccel;
        var planet = GravityFrames.Curved; // (the hold measures its height along its gravity: HoldHeight)
        var up = BodyUp(c, dt);
        var height = HoldHeight(com);
        if (!c.Driven)
            c.LiftInput = c.RollInput = 0;
        c.LiftHeld = c.LiftInput != 0 ? c.LiftHeld + dt : 0;
        // The hold: the driver's sprint starts it, or a height wired into a thruster.
        if (c.LiftInput > 0 && !c.HoverOn && c.StabCount == 0) // (a stabilizer holds height itself: ConstructFlight.cs)
        {
            c.HoverOn = true;
            c.HoverY = height;
        }
        var wiredHeight = -1f;
        var liftForce = 0f;
        var spool = 0f;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            if (p.InCount1 > 0 && p.D.ThrustHolds)
                wiredHeight = MathF.Max(wiredHeight, p.In1);
            if (t.Lifts && Live(c, ref t, p))
            {
                liftForce += c.ThrustOf(k); // (with a Mixture Controller's share, as the trims' weights take it)
                spool = MathF.Max(spool, p.D.ThrustSpool);
            }
        }
        if (wiredHeight >= 0)
        {
            var ground = query.Raycast(com, -up, 4000f, QueryFilter.All, out var hit) ? HoldHeight(hit.Point) : planet ? height - 4000f : 0f;
            c.HoverOn = true;
            c.HoverY = ground + wiredHeight;
        }
        else if (c.HoverOn)
        {
            // On a planet (a sky kilometres high) holding the key on climbs or sinks faster and faster; elsewhere the old steady rate.
            var rate = planet ? MathF.Min(HoverClimbMax, HoverRate + HoverClimbRamp * MathF.Max(0, c.LiftHeld - 1)) : HoverRate;
            var was = c.HoverY;
            c.HoverY += c.LiftInput * rate * dt;
            if (planet && liftForce > 0)
            {
                // The height asked never runs further ahead than half the lifters' spare push can close, so they never all
                // run flat out (with nothing left to keep it level, a flyer whose weight isn't over their middle tips over).
                var spare = MathF.Max(0.5f, liftForce / MathF.Max(1, c.Mass) - g);
                var lead = MathF.Max(HoverLand + 1, 0.5f * spare / HoverGain(spool));
                c.HoverY = Math.Clamp(c.HoverY, height - lead, height + lead);
                c.HoverRateNow = (c.HoverY - was) / dt;
            }
            if (c.LiftInput < 0 && height - c.HoverY > HoverLand && (!planet || Vector3.Dot(c.Vel, up) > -1f))
                c.HoverOn = false; // (down on something: it lets go; on a planet only once it has stopped sinking, since a fast descent leaves the aim below it)
        }
        if (liftForce <= 0)
            c.HoverOn = false; // (nothing left to hold it: it falls)
        if (!c.HoverOn || c.LiftInput == 0 || !planet)
            c.HoverRateNow = 0;
        // Each lifter's share of the weight, so they turn nothing: the centre of mass needn't be over their middle (or a corner one shot off).
        var trim = Vector2.Zero;
        var mid = Vector2.Zero;
        if (c.HoverOn)
            LiftTrim(c, liftForce, out trim, out mid);
        var counter = Vector2.Zero;
        if (c.HoverOn && planet)
            counter = LiftCounter(c, liftForce, mid, com); // (and they lean against the other jets' twist: the push of side jets low or high on it)
        // A critically damped hold, as quick as the slowest thruster can follow: it needs no tuning for a size.
        var w = MathF.Min(1.2f, 0.8f / MathF.Max(0.05f, spool));
        var kp = HoverGain(spool);
        var kd = 2.4f * w;
        var tw = MathF.Min(4f, 1.2f / MathF.Max(0.05f, spool)); // (the level hold on a planet: quicker than the height's)
        var tiltKp = tw * tw;
        var tiltKd = 2f * tw;
        if (planet && c.HoverOn)
        {
            var lean = Vector3.Cross(Vector3.Transform(Vector3.UnitY, c.Rot), up); // (the turn that would bring it level)
            c.TiltHold = Vector3.Clamp(c.TiltHold + lean * dt, new Vector3(-0.5f), new Vector3(0.5f));
        }
        else
            c.TiltHold = Vector3.Zero;
        var hoverShare = liftForce > 0 ? MathF.Max(0, c.Mass * g * (1 - c.AntiShare) - c.LiftNow) / liftForce : 0f; // (cells carry their share: ConstructLift.cs)
        var shown = 0f;
        var all = 0f;
        var air = c.HasBoost ? ConstructBoost.AirShare(c, com) : 1f;
        if (c.RepulsorCount > 0)
            RepulsorLook(c, query, _warmTick);
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            var d = p.D;
            c.WorldFrame(t.Seg, out var q, out var o);
            var at = o + Vector3.Transform(t.At, q);
            var dir = Vector3.Transform(t.Dir, q);
            var want = 0f;
            if (Live(c, ref t, p))
            {
                if (t.Scanned && c.StabOwns)
                    want = Math.Clamp(t.Command, 0, 1);
                else if (d.ThrustKind == ThrustKind.Solid)
                    want = ConstructBoost.Grain(c, ref p, d, dt);
                else if (d.ThrustKind == ThrustKind.Repulsor)
                    want = p.InCount > 0 ? Math.Clamp(p.In, 0, 1) : 1;
                else if (p.InCount > 0)
                    want = Math.Clamp(p.In, 0, 1);
                else if (c.HoverOn && t.Lifts)
                {
                    // Each holds its own point: the construct stays level, and a thruster shot off the corner tilts it, not drops it.
                    var aim = c.HoverY + t.At.Y - c.Com.Y;
                    var a = planet ? kp * (aim - HoldHeight(at)) - kd * (Vector3.Dot(c.PointVel(at), up) - c.HoverRateNow) : kp * (aim - at.Y) - kd * c.PointVel(at).Y;
                    if (planet)
                    {
                        // And a stiffer hold on its own level (its height against the centre of mass's, as when level), so a
                        // small flyer whose lifters sit close together doesn't slowly tip over as it climbs.
                        var rel = at - com;
                        a -= tiltKp * (Vector3.Dot(rel, up) - (t.At.Y - c.Com.Y)) + tiltKd * Vector3.Dot(Vector3.Cross(c.AngVel, rel), up);
                        a += 0.5f * tiltKp * Vector3.Dot(Vector3.Cross(rel, up), c.TiltHold);
                    }
                    var off = new Vector2(t.At.X - c.Com.X, t.At.Z - c.Com.Z) - mid;
                    want = Math.Clamp(hoverShare * (1 + a / g + Vector2.Dot(trim, off) + HoverRoll * t.MixRoll * c.RollInput) + Vector2.Dot(counter, off), 0, 1);
                }
                else if (c.Driven)
                    want = Math.Clamp(t.MixForward * c.Throttle + t.MixYaw * c.SteerInput + t.MixRoll * c.RollInput, 0, 1);
                if (t.Valve >= 0)
                    want = ValveWant(c, t.Valve, want, t.Scanned && c.StabOwns);
            }
            if (t.LightDelay > 0)
                Lighting(ref t, ref want, dt);
            var rate = d.ThrustSpool > 0 ? dt / d.ThrustSpool : 1f;
            t.Level = Math.Clamp(t.Level + Math.Clamp(want - t.Level, -2 * rate, rate), 0, 1);
            if (t.Train)
                t.Heat += (t.Level * t.Cap - t.Heat) * (1 - MathF.Exp(-dt / (t.Level * t.Cap > t.Heat ? 3f : 25f)));
            all += d.ThrustForce;
            var boost = t.Reheat >= 0 && ReheatStep(c, ref t, dt); // (before the skip: an off ring recovers)
            if (t.Level <= 0 && t.Ab <= 0)
                continue;
            var push = d.ThrustForce * t.Cap * t.Level; // (what its works let it push)
            var burn = d.ThrustBurn * t.Burn * t.Level;
            if (t.Mixer >= 0)
            {
                var (mp, mb) = MixOf(c, t.Mixer);
                push *= mp;
                burn *= mb;
            }
            if (boost)
            {
                push *= 1 + ShipWorks.Current.ReheatBoost;
                burn *= 2;
            }
            if (d.ThrustKind != ThrustKind.Plain || d.ThrustGimbal > 0 || t.Ring >= 0) // (a plain drive with a gimbal, a ship's pod, turns its nozzle there too)
                ConstructBoost.Scale(c, ref t, p, d, air, want, ref dir, ref push, ref burn, at, q, dt);
            if (push <= 0)
                continue;
            shown += push;
            ApplyImpulse(c, invI, at - com, dir * (push * dt));
            if (d.ThrustFuel == EngineFuel.Fuel)
            {
                if (d.ThrustKind != ThrustKind.Solid)
                    c.UseFuel(burn * dt);
                if (d.ThrustOx > 0)
                    ConstructFluid.BurnOx(c, d.ThrustOx * t.Burn * t.Level * dt);
                if (!t.Fed)
                    t.Buffer = MathF.Max(0, t.Buffer - t.Level * dt); // (a cut line: it burns its injector's buffer, then stops)
            }
            else
                c.MotorWork += d.ThrustPower * 1000f * (d.ThrustKind == ThrustKind.Repulsor ? push / d.ThrustForce : t.Level * t.Cap);
        }
        c.ThrustShow = all > 0 ? MathF.Min(1, shown / all) : 0;
        if (shown > 0)
        {
            c.Vel -= c.Vel * MathF.Min(1, (FlightDrag + c.Vel.Length() * AirDragOf(c)) * (planet ? AirAt(com) : 1f) * dt);
            c.AngVel -= c.AngVel * MathF.Min(1, FlightTurnDrag * dt);
        }
    }

    /// <summary>
    /// On a planet: how the lifters' levels lean (per metre across the body from their middle) so their pushes cancel the
    /// twist of the other thrusters (a pair of jets low or high on the sides pitches it as they push it on), the same least
    /// squares as <see cref="LiftTrim"/>. Their twist is taken from last tick's levels.
    /// </summary>
    private static Vector2 LiftCounter(Construct c, float liftForce, Vector2 mid, Vector3 com)
    {
        var twist = Vector3.Zero;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            if (t.Lifts || t.Level <= 0)
                continue;
            c.WorldFrame(t.Seg, out var q, out var o);
            var at = o + Vector3.Transform(t.At, q);
            twist += Vector3.Cross(at - com, Vector3.Transform(t.Dir, q) * (c.ThrustOf(k) * t.Level));
        }
        if (twist == Vector3.Zero)
            return Vector2.Zero;
        var local = Vector3.Transform(twist, Quaternion.Conjugate(c.Rot));
        float xx = 0, xz = 0, zz = 0;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            if (!t.Lifts || !Live(c, ref t, p))
                continue;
            var d = new Vector2(t.At.X - c.Com.X, t.At.Z - c.Com.Z) - mid;
            var f = c.ThrustOf(k);
            xx += f * d.X * d.X;
            xz += f * d.X * d.Y;
            zz += f * d.Y * d.Y;
        }
        // A lifter at (x, z) pushing f up twists the body by (-z f, 0, x f): want sum(x df) = -twist.z, sum(z df) = twist.x.
        var bx = -local.Z;
        var bz = local.X;
        var det = xx * zz - xz * xz;
        if (!(det > 1e-6f * (xx + zz) * (xx + zz)))
            return Vector2.Zero;
        return new Vector2(zz * bx - xz * bz, xx * bz - xz * bx) / det;
    }

    /// <summary>The hold's stiffness (1/s2) for the slowest lifter's spool: a critically damped hold as quick as it can follow.</summary>
    private static float HoverGain(float spool)
    {
        var w = MathF.Min(1.2f, 0.8f / MathF.Max(0.05f, spool));
        return w * w;
    }

    /// <summary>
    /// The lifters' levels as 1 + trim . (p - mid), p each one's place from the centre of mass across the body: the trim
    /// that makes their pushes balance about the centre of mass (a least squares fit, so any number of them will do).
    /// </summary>
    private static void LiftTrim(Construct c, float liftForce, out Vector2 trim, out Vector2 mid)
    {
        mid = Vector2.Zero;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            if (t.Lifts && Live(c, ref t, p))
                mid += new Vector2(t.At.X - c.Com.X, t.At.Z - c.Com.Z) * (c.ThrustOf(k) / liftForce);
        }
        float xx = 0, xz = 0, zz = 0;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            if (!t.Lifts || !Live(c, ref t, p))
                continue;
            var d = new Vector2(t.At.X - c.Com.X, t.At.Z - c.Com.Z) - mid;
            var f = c.ThrustOf(k) / liftForce;
            xx += f * d.X * d.X;
            xz += f * d.X * d.Y;
            zz += f * d.Y * d.Y;
        }
        var det = xx * zz - xz * xz;
        trim = det > 1e-4f * (xx + zz) * (xx + zz)
            ? new Vector2(-(zz * mid.X - xz * mid.Y), -(xx * mid.Y - xz * mid.X)) / det
            : Vector2.Zero; // (in a line: it can't be trimmed across it)
    }

    /// <summary>
    /// Out of the bubble (Game/GameSpaceLaunch.cs): one tick of the thrusters of a construct that is a space ship, whose
    /// physics sleeps. Each is asked as in the air (its wire, else the driver: W and S by how it faces the seat, Shift for
    /// those that push up), spools, burns its fuel or power, and the sum of their pushes (N, world axes, through the centre
    /// of mass: in space the ship turns on the driver's A and D instead) is what the ship's flight takes.
    /// </summary>
    public static Vector3 SpaceThrust(Construct c, float dt) => SpaceThrust(c, dt, out _);

    /// <summary>
    /// The same, and its turn about the centre of mass (world axes). A stabilizer flying on Ship keys sets each thruster as it
    /// does in the bubble (its mix holds the nose on the look and the speed asked), so a ship in space turns any way it has jets for.
    /// </summary>
    public static Vector3 SpaceThrust(Construct c, float dt, out Vector3 torque)
    {
        if (c.ThrustPuppet)
        {
            torque = Vector3.Zero;
            return Vector3.Zero; // (as Thrusters: its jets are lit for show while a Board order flies it)
        }
        torque = Vector3.Zero;
        var owned = ConstructFlight.FliesShip(c);
        var com = c.WorldCom;
        var fwd = !c.Driven ? 0f : ((c.DriveKeys & Buttons.Forward) != 0 ? 1f : 0f) - ((c.DriveKeys & Buttons.Back) != 0 ? 1f : 0f);
        var lift = !c.Driven ? 0f : MathF.Max(0, c.LiftInput);
        if (c.NavThrottle >= 0)
        {
            // An order flies it (Game/GameNav.cs): its main drive at the nav computer's throttle, nothing else.
            fwd = c.NavLift ? 0f : c.NavThrottle;
            lift = c.NavLift ? c.NavThrottle : 0f;
        }
        var sum = Vector3.Zero;
        var shown = 0f;
        var all = 0f;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            var d = p.D;
            var want = 0f;
            if (Live(c, ref t, p))
            {
                want = owned && t.Scanned ? Math.Clamp(t.Command, 0, 1) : d.ThrustKind == ThrustKind.Solid ? ConstructBoost.Grain(c, ref p, d, dt) : p.InCount > 0 ? Math.Clamp(p.In, 0, 1) : t.Lifts ? lift : Math.Clamp(t.MixForward * fwd, 0, 1);
                if (t.Valve >= 0)
                    want = ValveWant(c, t.Valve, want, owned && t.Scanned);
            }
            if (t.LightDelay > 0)
                Lighting(ref t, ref want, dt);
            var rate = d.ThrustSpool > 0 ? dt / d.ThrustSpool : 1f;
            t.Level = Math.Clamp(t.Level + Math.Clamp(want - t.Level, -2 * rate, rate), 0, 1);
            if (t.Train)
                t.Heat += (t.Level * t.Cap - t.Heat) * (1 - MathF.Exp(-dt / (t.Level * t.Cap > t.Heat ? 3f : 25f)));
            all += d.ThrustForce;
            var boost = t.Reheat >= 0 && ReheatStep(c, ref t, dt); // (before the skip: an off ring recovers)
            if (t.Level <= 0 || d.ThrustKind is ThrustKind.Air or ThrustKind.Repulsor) // (nothing to breathe, nothing to push on)
                continue;
            c.WorldFrame(t.Seg, out var q, out var o);
            var (sp, sb) = t.Mixer >= 0 ? MixOf(c, t.Mixer) : (1f, 1f);
            if (boost)
                (sp, sb) = (sp * (1 + ShipWorks.Current.ReheatBoost), sb * 2);
            var push = Vector3.Transform(t.Dir, q) * (d.ThrustForce * t.Cap * t.Level * sp);
            sum += push;
            torque += Vector3.Cross(o + Vector3.Transform(t.At, q) - com, push);
            shown += d.ThrustForce * t.Cap * t.Level * sp;
            if (d.ThrustFuel == EngineFuel.Fuel)
            {
                if (d.ThrustKind != ThrustKind.Solid)
                    c.UseFuel(d.ThrustBurn * t.Burn * t.Level * sb * dt * SpaceFuelScale);
                if (d.ThrustOx > 0)
                    ConstructFluid.BurnOx(c, d.ThrustOx * t.Burn * t.Level * dt * SpaceFuelScale);
                if (!t.Fed)
                    t.Buffer = MathF.Max(0, t.Buffer - t.Level * dt);
            }
            else
                c.MotorWork += d.ThrustPower * 1000f * t.Level * t.Cap;
        }
        c.ThrustShow = all > 0 ? shown / all : 0;
        return sum;
    }

    /// <summary>
    /// The drive the nav computer flies a ship in space with (Game/GameNav.cs): of its thrusters that answer forward and
    /// those that lift, the set that pushes harder, as the driver's W or Shift would work them (<see cref="SpaceThrust"/>).
    /// Returns the push they add up to (N; 0: none can push now), with its direction in the body's frame and whether it is
    /// the lifters (a tail-sitter's main drive). A wired thruster is left to its wire and not counted.
    /// </summary>
    /// <summary>
    /// The share of their fuel thrusters burn in space (the host's Settings > Gameplay "Fuel use in space", Karl 2026-11-16:
    /// a slider for fuel and speed; clients hear it with the space clock). Lower, ships carry more dV and trips between
    /// planets are quicker. The air and the ground are untouched.
    /// </summary>
    public static float SpaceFuelScale = 1f;

    public static float NavDrive(Construct c, out Vector3 axis, out bool lift, out float burn)
    {
        var scale = SpaceFuelScale; // (its burn is a burn in space: the ship card's dV and the trip planner read it)
        Vector3 fwd = Vector3.Zero, up = Vector3.Zero;
        float fwdBurn = 0, upBurn = 0;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            var d = p.D;
            if (d.ThrustKind is ThrustKind.Air or ThrustKind.Repulsor or ThrustKind.Solid || p.InCount > 0 || !Live(c, ref t, p))
                continue;
            c.WorldFrame(t.Seg, out var q, out _);
            var push = Vector3.Transform(Vector3.Transform(t.Dir, q), Quaternion.Conjugate(c.Rot)) * (d.ThrustForce * t.Cap);
            var fuel = d.ThrustFuel == EngineFuel.Fuel ? d.ThrustBurn * t.Burn : 0f;
            if (t.Lifts)
            {
                up += push;
                upBurn += fuel;
            }
            else if (t.MixForward > 0)
            {
                fwd += push * t.MixForward;
                fwdBurn += fuel * t.MixForward;
            }
        }
        lift = up.LengthSquared() > fwd.LengthSquared();
        var sum = lift ? up : fwd;
        burn = (lift ? upBurn : fwdBurn) * scale;
        var f = sum.Length();
        axis = f > 1e-3f ? sum / f : Vector3.Zero;
        return f;
    }

    /// <summary>Is this thruster able to push now (a stabilizer's mix leaves out one that can't)?</summary>
    /// <summary>A bell with no Igniter in its train, asked to burn from cold, waits <see cref="CThruster.LightDelay"/> before its level rises (a warm one, off for a moment, relights at once).</summary>
    /// <summary>
    /// What a bell asks with a Throttle Valve in its train: wired, the valve's Throttle in (0..1) sets the level; unwired, its Level setting
    /// holds the drive at most there. Either way a level above nothing is at least 5%, in 2% steps. A broken valve passes the ask on. A bell
    /// a stabilizer commands (<paramref name="stab"/>) keeps its command, the wire only holding it at most there as the setting does.
    /// </summary>
    private static float ValveWant(Construct c, int valve, float want, bool stab)
    {
        if (valve >= c.PartCount)
            return want;
        ref var v = ref c.Parts[valve];
        if (v.Broken)
            return want;
        var hold = v.InCount > 0 ? Math.Clamp(v.In, 0, 1) : ConstructConfig.Get(v, v.D, CfgKey.Strength, 100) / 100f;
        var w = v.InCount > 0 && !stab ? hold : MathF.Min(want, hold);
        return w <= 0.005f ? 0 : MathF.Max(0.05f, MathF.Round(w * 50) / 50);
    }

    /// <summary>
    /// A Mixture Controller's push and burn shares: its dial (or its Mix in, -1..1, when wired) from -100 (80% push at 70% burn) through 0
    /// (as it is) to 100 (110% push at 130% burn). A broken one is as it is.
    /// </summary>
    public const float MixRichPush = 0.1f, MixLeanPush = 0.2f; // (a Mixture Controller's push at +100 is 1 + rich, at -100 1 - lean: a module's peak thrust counts the rich)

    public static (float Push, float Burn) MixOf(Construct c, int mixer)
    {
        if (mixer >= c.PartCount || c.Parts[mixer].Broken)
            return (1, 1);
        ref var m = ref c.Parts[mixer];
        var x = Math.Clamp(m.InCount > 0 ? m.In : ConstructConfig.Get(m, m.D, CfgKey.Strength, 0) / 100f, -1, 1);
        return (1 + (x < 0 ? MixLeanPush : MixRichPush) * x, 1 + 0.3f * x);
    }

    /// <summary>
    /// A Reheat Ring's step (host): while its Boost in is on and the bell burns it boosts, for up to <see cref="ShipWorks.ReheatSeconds"/>;
    /// spent, it gives nothing until it has all recovered over <see cref="ShipWorks.ReheatRecover"/>; off, what it used comes back at the
    /// same pace. True: boosting this tick. A broken ring boosts nothing and recovers.
    /// </summary>
    public static bool ReheatStep(Construct c, ref CThruster t, float dt)
    {
        var w = ShipWorks.Current;
        var on = t.Reheat < c.PartCount && c.Parts[t.Reheat] is { Broken: false, InCount: > 0, In: >= 0.5f } && t.Level > 0 && !t.ReheatSpent;
        t.ReheatOn = on;
        if (on)
        {
            t.ReheatUsed += dt;
            if (t.ReheatUsed >= w.ReheatSeconds)
                t.ReheatSpent = true;
            return true;
        }
        t.ReheatUsed = MathF.Max(0, t.ReheatUsed - dt * w.ReheatSeconds / w.ReheatRecover);
        if (t.ReheatUsed <= 0)
            t.ReheatSpent = false;
        return false;
    }

    private const float WarmRelight = 0.05f; // (a bell's heat over this relights at once: about 75 s after a full burn stops)

    private static void Lighting(ref CThruster t, ref float want, float dt)
    {
        if (t.Level > 0 || want <= 0 || t.Heat > WarmRelight)
        {
            t.Lit = 0;
            return;
        }
        t.Lit += dt;
        if (t.Lit < t.LightDelay)
            want = 0;
    }

    public static bool ThrusterLive(Construct c, ref CThruster t, in CPart p) => Live(c, ref t, p);

    private static bool Live(Construct c, ref CThruster t, in CPart p)
    {
        if (!p.Working || ConstructShutdown.Off(p)) // (shut down from a seat or its panel)
            return false;
        if (p.D.ThrustKind == ThrustKind.Solid)
            return ConstructConfig.Get(p, p.D, CfgKey.Grain, 0) < p.D.BurnTime;
        if (p.D.ThrustOx > 0 && c.OxLeft <= 0)
            return false;
        return p.D.ThrustFuel == EngineFuel.Fuel ? (t.Fed || t.Buffer > 0) && c.FuelLeft > 0 : c.Electrical && ConstructPower.On(c, t.Part);
    }
}

/// <summary>What big drive parts say on the HUD and the look line (ASCII, cached by the caller's text).</summary>
public static class ConstructThrustHud
{
    /// <summary>What the driver's second line shows, as a key (it is made again when this changes).</summary>
    public static long Key(Construct c) => (c.StabCount > 0 ? (long)ConstructFlight.ApText(c).Length * 104729 + c.Stabs[0].Part : 0) + (long)(c.Vel.Length() * 1.5f) * 1299709 + (long)(ConstructRotor.SpeedShare(c) * 100) * 15485863 + (c.CellCount > 0 ? (long)(ConstructLift.LiftKg(c) / 10f) * 7919 + (long)(c.BurnTrim * 100) * 31 : 0) + (int)MathF.Round(c.ThrustShow * 100) + ((long)(c.HoverOn ? (int)MathF.Round(ConstructPhysics.HoldShown(c.HoverY)) + 100000 : 0) << 8) ^ (long)MathF.Round(c.PowerUse / 1e6f) << 32;

    /// <summary>The driver's second line: the thrust, the height held, the power drawn.</summary>
    public static string Line(Construct c) =>
        (c.StabCount > 0 && ConstructFlight.ApText(c) is { Length: > 0 } ap ? ap + ", " : "") + (c.SurfaceCount > 0 ? $"Air {c.Vel.Length() * 3.6f:0} km/h, stall {ConstructAero.StallSpeed(c) * 3.6f:0}, " : "") + (c.RotorCount > 0 ? $"Rotor {ConstructRotor.SpeedShare(c) * 100:0}%, " : "") + (c.CellCount > 0 ? $"Lift {ConstructLift.LiftKg(c) / 1000f:0.##} of {c.Mass / 1000f:0.##} t{(c.BurnerCount > 0 ? $", burner {c.BurnTrim * 100:0}%" : "")}, " : "") + (c.ThrusterCount > 0 ? $"Thrust {c.ThrustShow * 100:0}%{(c.HoverOn ? $", holding {ConstructPhysics.HoldShown(c.HoverY):0} m" : "")}, " : "") + $"drawing {c.PowerUse / 1e6f:0.#} MW";

    private static string Mw(float kw) => (kw / 1000f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Each thruster's own level on the clients, for its flame (protocol 319): a ship with attitude jets every way fires a few at a time, and one level for the whole
/// construct (<see cref="Construct.ThrustShow"/>) lit them all. The server sends a body's levels, a byte each, 10 times a second while any of them moves.
/// </summary>
public static class ConstructThrust
{
    public const int SendEvery = World.TickRate / 10;

    /// <summary>One body's levels if they changed since it last sent them: u16 body, u16 n, u8 each. False: nothing to send.</summary>
    public static bool WriteShown(PacketWriter w, Construct c)
    {
        var n = c.ThrusterCount;
        if (n == 0)
            return false;
        var key = 17L + n;
        var lit = false;
        var swung = 0;
        for (var k = 0; k < n; k++)
        {
            ref var t = ref c.Thrusters[k];
            var v = Level(t);
            key = key * 31 + v;
            lit |= v > 0;
            if (Angle(t.AngleX) != 0 || Angle(t.AngleY) != 0)
            {
                key = (key * 31 + Angle(t.AngleX)) * 31 + Angle(t.AngleY);
                swung++;
            }
        }
        c.Swung = swung > 0;
        swung = Math.Min(swung, MaxSwung(n)); // (the rest are drawn straight on a client: one message stays under the MTU)
        if (key == c.ThrustSentKey && (lit || c.ThrustDark >= 3))
            return false; // (all out goes three times: it goes unreliably, and a lost one would leave a flame burning)
        c.ThrustDark = lit ? 0 : key == c.ThrustSentKey ? c.ThrustDark + 1 : 1;
        c.ThrustSentKey = key;
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Thrust).U16(c.Id).U16((ushort)n);
        for (var k = 0; k < n; k++)
            w.U8(Level(c.Thrusters[k]));
        w.U16((ushort)swung); // (protocol 464: then each swung bell's index and its gimbal's two angles, 0.4 degree a step; the rest stand straight)
        for (var k = 0; k < n && swung > 0; k++)
            if (Angle(c.Thrusters[k].AngleX) != 0 || Angle(c.Thrusters[k].AngleY) != 0)
            {
                w.U16((ushort)k).U8((byte)Angle(c.Thrusters[k].AngleX)).U8((byte)Angle(c.Thrusters[k].AngleY));
                swung--;
            }
        return true;
    }

    /// <summary>How many swung bells one message of <paramref name="n"/> levels can carry under <see cref="PacketWriter.Mtu"/> (4 bytes each).</summary>
    private static int MaxSwung(int n) => Math.Max(0, (PacketWriter.Mtu - 9 - n) / 4);

    private const float AngleStep = 0.4f * MathF.PI / 180f; // (127 steps: 50.8 degrees, past ConstructAero.ThrowMost's 43)

    private static sbyte Angle(float a) => (sbyte)Math.Clamp(MathF.Round(a / AngleStep), -127, 127);

    private static byte Level(in CThruster t) => (byte)Math.Clamp(MathF.Round(MathF.Max(t.Level * t.Cap, t.Ab) * 255), 0, 255); // (a drive short of its works burns low)

    public static void ReadShown(ref PacketReader r, ConstructWorld world)
    {
        var c = world.Get(r.U16());
        var n = r.U16();
        var fits = c != null && c.ThrusterCount == n;
        for (var k = 0; k < n; k++)
        {
            var v = r.U8();
            if (fits)
                c!.Thrusters[k].Shown = v / 255f;
        }
        if (fits)
            for (var k = 0; k < n; k++)
                c!.Thrusters[k].ShownX = c.Thrusters[k].ShownY = 0;
        var swung = r.U16();
        if (fits)
            c!.Swung = swung > 0;
        for (var m = 0; m < swung; m++)
        {
            var k = r.U16();
            var x = (sbyte)r.U8();
            var y = (sbyte)r.U8();
            if (fits && k < n)
                (c!.Thrusters[k].ShownX, c.Thrusters[k].ShownY) = (x * AngleStep, y * AngleStep);
        }
        if (fits)
            c!.ThrustHeard = true;
    }
}
