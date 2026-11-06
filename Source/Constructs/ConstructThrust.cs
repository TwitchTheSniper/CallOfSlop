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
    public const int MaxThrusters = 64;
    public readonly CThruster[] Thrusters = new CThruster[MaxThrusters];
    public int ThrusterCount;
    public bool HubSkid; // hub motors and no steered wheel: the steering drives the sides against each other
    public bool HasHubMotor;
    public float LiftInput; // the driver's sprint (up) and crouch (down), -1..1
    public float RollInput; // the seat's roll keys, -1 left, 1 right
    public bool HoverOn; // the hover hold: every lifting thruster holds the construct level at HoverY (it stays on with nobody at the controls: a carrier's crew can leave the seat)
    public float HoverY; // the height it holds its centre of mass at (world Y; on a planet, the distance from its centre: ConstructPhysics.HoldHeight)
    public Vector3 TiltHold; // on a planet: the level hold's integral (world axis x rad s), so a steady twist (a lifter a little off) can't leave it leaning
    public float HoverRateNow; // on a planet: how fast the height held moves this tick (m/s), which the hold follows as well as the height
    public float LiftHeld; // how long the driver has held Shift or Ctrl (s): on a planet the hold climbs and sinks faster the longer it is held
    public float ThrustShow; // 0..1: how hard its thrusters push, of all they could (server; clients get it in the pose)
    public bool ActiveSignal; // this tick a wire asks a hub motor or a thruster for something: it stays awake

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
        var old = ThrusterCount;
        for (var k = 0; k < old; k++)
        {
            keep[k] = Thrusters[k].Level;
            keepPart[k] = Thrusters[k].Part;
        }
        ThrusterCount = 0;
        var most = 1e-3f;
        var mostRoll = 1e-3f;
        var fwd = SeatForward();
        for (var i = 0; i < PartCount && ThrusterCount < MaxThrusters; i++)
        {
            ref var p = ref Parts[i];
            var d = p.D;
            if (d.Role != PartRole.Thruster)
                continue;
            var at = p.Pos + Rot24.Apply(p.Rot, d.ThrustAt);
            var dir = -ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, d.ThrustDir));
            ref var t = ref Thrusters[ThrusterCount];
            t = new CThruster { Part = (ushort)i, Seg = p.Seg, At = at, Dir = dir, Lifts = dir.Y > 0.7f, Fed = d.ThrustFuel != EngineFuel.Fuel || p.Working && FuelReaches(i) };
            t.MixForward = Vector3.Dot(dir, fwd);
            t.MixYaw = Vector3.Cross(at - Com, dir).Y * d.ThrustForce; // (made a share below)
            most = MathF.Max(most, MathF.Abs(t.MixYaw));
            t.MixRoll = Vector3.Dot(Vector3.Cross(at - Com, dir), fwd) * d.ThrustForce; // (a roll to the right turns about the way it faces, clockwise from behind)
            mostRoll = MathF.Max(mostRoll, MathF.Abs(t.MixRoll));
            for (var k = 0; k < old; k++)
                if (keepPart[k] == i)
                    t.Level = keep[k];
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
    public static float HoldHeight(Vector3 p) => GravityFrames.Point is { } planet ? Vector3.Distance(p, planet.Center) : p.Y;

    /// <summary>The way up the hold pushes and measures: world +Y, or away from a planet's centre.</summary>
    public static Vector3 HoldUp(Vector3 p) => GravityFrames.Point is { } planet ? GravityFrames.UpOf(planet, p) : Vector3.UnitY;

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
            : c.Driver == 255 ? 0f
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
        var com = c.WorldCom;
        var g = GravityAccel;
        var planet = GravityFrames.Point != null; // (the hold measures its height from the planet's centre: HoldHeight)
        var up = HoldUp(com);
        var height = HoldHeight(com);
        if (c.Driver == 255)
            c.LiftInput = c.RollInput = 0;
        c.LiftHeld = c.LiftInput != 0 ? c.LiftHeld + dt : 0;
        // The hold: the driver's sprint starts it, or a height wired into a thruster.
        if (c.LiftInput > 0 && !c.HoverOn)
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
            if (p.InCount1 > 0)
                wiredHeight = MathF.Max(wiredHeight, p.In1);
            if (t.Lifts && Live(c, ref t, p))
            {
                liftForce += p.D.ThrustForce;
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
        var hoverShare = liftForce > 0 ? MathF.Max(0, c.Mass * g - c.LiftNow) / liftForce : 0f; // (cells carry their share: ConstructLift.cs)
        var shown = 0f;
        var all = 0f;
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
                if (p.InCount > 0)
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
                else if (c.Driver != 255)
                    want = Math.Clamp(t.MixForward * c.Throttle + t.MixYaw * c.SteerInput + t.MixRoll * c.RollInput, 0, 1);
            }
            var rate = d.ThrustSpool > 0 ? dt / d.ThrustSpool : 1f;
            t.Level = Math.Clamp(t.Level + Math.Clamp(want - t.Level, -2 * rate, rate), 0, 1);
            all += d.ThrustForce;
            if (t.Level <= 0)
                continue;
            shown += d.ThrustForce * t.Level;
            ApplyImpulse(c, invI, at - com, dir * (d.ThrustForce * t.Level * dt));
            if (d.ThrustFuel == EngineFuel.Fuel)
                c.UseFuel(d.ThrustBurn * t.Level * dt);
            else
                c.MotorWork += d.ThrustPower * 1000f * t.Level;
        }
        c.ThrustShow = all > 0 ? shown / all : 0;
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
            twist += Vector3.Cross(at - com, Vector3.Transform(t.Dir, q) * (c.Parts[t.Part].D.ThrustForce * t.Level));
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
            var f = p.D.ThrustForce;
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
                mid += new Vector2(t.At.X - c.Com.X, t.At.Z - c.Com.Z) * (p.D.ThrustForce / liftForce);
        }
        float xx = 0, xz = 0, zz = 0;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            if (!t.Lifts || !Live(c, ref t, p))
                continue;
            var d = new Vector2(t.At.X - c.Com.X, t.At.Z - c.Com.Z) - mid;
            var f = p.D.ThrustForce / liftForce;
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
    public static Vector3 SpaceThrust(Construct c, float dt)
    {
        var fwd = c.Driver == 255 ? 0f : ((c.DriveKeys & Buttons.Forward) != 0 ? 1f : 0f) - ((c.DriveKeys & Buttons.Back) != 0 ? 1f : 0f);
        var lift = c.Driver == 255 ? 0f : MathF.Max(0, c.LiftInput);
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
                want = p.InCount > 0 ? Math.Clamp(p.In, 0, 1) : t.Lifts ? lift : Math.Clamp(t.MixForward * fwd, 0, 1);
            var rate = d.ThrustSpool > 0 ? dt / d.ThrustSpool : 1f;
            t.Level = Math.Clamp(t.Level + Math.Clamp(want - t.Level, -2 * rate, rate), 0, 1);
            all += d.ThrustForce;
            if (t.Level <= 0)
                continue;
            c.WorldFrame(t.Seg, out var q, out _);
            sum += Vector3.Transform(t.Dir, q) * (d.ThrustForce * t.Level);
            shown += d.ThrustForce * t.Level;
            if (d.ThrustFuel == EngineFuel.Fuel)
                c.UseFuel(d.ThrustBurn * t.Level * dt);
            else
                c.MotorWork += d.ThrustPower * 1000f * t.Level;
        }
        c.ThrustShow = all > 0 ? shown / all : 0;
        return sum;
    }

    private static bool Live(Construct c, ref CThruster t, in CPart p)
    {
        if (!p.Working)
            return false;
        return p.D.ThrustFuel == EngineFuel.Fuel ? t.Fed && c.FuelLeft > 0 : c.Electrical && ConstructPower.On(c, t.Part);
    }
}

/// <summary>What big drive parts say on the HUD and the look line (ASCII, cached by the caller's text).</summary>
public static class ConstructThrustHud
{
    /// <summary>What the driver's second line shows, as a key (it is made again when this changes).</summary>
    public static long Key(Construct c) => (long)(c.Vel.Length() * 1.5f) * 104729 + (long)(c.AeroTrim * 100) * 15485863 + (long)(c.RotorThrust * 0.02f) * 257 + (c.CellCount > 0 ? (long)(ConstructLift.LiftKg(c) / 10f) * 7919 + (long)(c.BurnTrim * 100) * 31 : 0) + (int)MathF.Round(c.ThrustShow * 100) + ((long)(c.HoverOn ? (int)MathF.Round(ConstructPhysics.HoldShown(c.HoverY)) + 100000 : 0) << 8) ^ (long)MathF.Round(c.PowerUse / 1e6f) << 32;

    /// <summary>The driver's second line: the thrust, the height held, the power drawn.</summary>
    public static string Line(Construct c) =>
        (c.SurfaceCount > 0 ? $"Air {c.Vel.Length() * 3.6f:0} km/h, stall {ConstructAero.StallSpeed(c) * 3.6f:0}, " : "") + (c.RotorCount > 0 ? $"Throttle {c.AeroTrim * 100:0}%, push {c.RotorThrust / 9.81f:0} kg, " : "") + (c.CellCount > 0 ? $"Lift {ConstructLift.LiftKg(c) / 1000f:0.##} of {c.Mass / 1000f:0.##} t{(c.BurnerCount > 0 ? $", burner {c.BurnTrim * 100:0}%" : "")}, " : "") + (c.ThrusterCount > 0 ? $"Thrust {c.ThrustShow * 100:0}%{(c.HoverOn ? $", holding {ConstructPhysics.HoldShown(c.HoverY):0} m" : "")}, " : "") + $"drawing {c.PowerUse / 1e6f:0.#} MW";

    private static string Mw(float kw) => (kw / 1000f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
