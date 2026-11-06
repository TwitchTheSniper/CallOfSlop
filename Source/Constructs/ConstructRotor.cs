using System.Numerics;

namespace CallOfSlop;

/// <summary>One rotor of a construct, cached with its parts.</summary>
public struct CRotor
{
    public ushort Part;
    public byte Seg; // what it hangs on: a joint tilts it
    public byte Blades; // how many it has (its setting)
    public byte Style; // a row of Air.json's "bladeStyles"
    public byte Alive; // blades left
    public short Feed; // its entry in the shaft feeds, or -1
    public ushort Lost; // the blades gone, a bit each
    public Vector3 At, Dir, Fwd, Right; // the segment's frame: its disc's middle, its push, which way cyclic tilts it
    public float Sense; // 1 right hand about its push, -1 left
    public float PowerScale, PushScale, TopScale; // what its blades make of its part's numbers: their style, and how many
    public float Reduction; // shaft speed over its speed
    public float Omega; // rad/s now (the pose carries it to clients)
    public float Angle; // radians, drawn
    public float Induced; // m/s through the disc
    public float Thrust; // N now
    public float Power; // W now
    public float Sent; // the share of rated speed the pose last carried
}

public sealed partial class Construct
{
    public const int MaxRotors = 32;
    public CRotor[] Rotors = [];
    public int RotorCount;
    public float AeroTrim; // the driver's throttle or collective setting, 0..1 (Shift raises it, Ctrl lowers it)
    public float RotorLoad; // N m at the crank that shaft-driven rotors take
    public float RotorThrust; // N this tick, all of them
    public bool RotorLive; // something turns, or is asked to
    public bool RotorEngine; // a rotor is on the engine's shaft: the trim is the engine's throttle
    public bool RotorRuns; // a wire asks a rotor on the shaft for something: that runs the engine, as a wire into it does
    public bool RotorGoverned; // a helicopter rotor is on it: the engine runs at full speed and collective takes the load
    public float RpmGovern; // the crank's most while a rotor is on its shaft, a little past where the hub is rated (0: none)
    public Vector3 RotorReaction; // N m on the body last tick from the rotors a torque mix doesn't count (the body's frame)
}

/// <summary>
/// Propellers and rotors (docs/sandbox/CONSTRUCTS.md, "Propellers and rotors"): a disc that takes power and pushes on the air.
/// Power comes down a real drive shaft from an engine (a "Drive in" port, as a drill's; what the rotor takes comes off the
/// crank) or from an electric motor in the hub, on the circuit. What it does with it is momentum theory: the air through the
/// disc, so its push goes with its power, its size, how thick the air is and how fast it already moves, and fades to nothing
/// at the speed its blades can no longer bite. Its torque turns the craft the other way about its axis, which a tail rotor or
/// a rotor of the other hand has to cancel. A helicopter rotor's first input is collective and it takes cyclic. Each blade has
/// its own health: one shot off cuts the push and shakes the craft. Runs on a part's own inputs, on any construct, free or
/// anchored. Nothing allocates in a tick.
/// </summary>
public static class ConstructRotor
{
    public const int MaxBlades = 16; // CfgKey.BladeLost and the wear settings are 16-bit masks; past this a disc is near solid anyway
    public const int WearSlots = MaxBlades / 4; // (four blades a setting: a rotor's settings stay few, and a tick never reads them)
    private const int WearLevels = 16;
    private const float CountPower = 0.3f, CountPush = 0.1f; // power and push go with (blades / its base count) to these
    private const float SeaAir = 1.225f;
    private const float TrimRate = 0.4f; // a second, the driver's setting
    private const float Translift = 1.3f; // the most forward speed adds to a disc's push on the same power
    private const float HeadDamp = 2.5f; // a turning rotor head holds its plane: how fast it takes a craft's pitch and roll down, a second
    private const float RateOver = 1.1f; // a shaft's rotor is held to this much of its rated speed
    private const float ShakeHz = 9f; // a lost blade's shake: slow enough to survive the poses (it is felt, not only drawn)

    /// <summary>Rebuilt with the caches, after the drive line.</summary>
    public static void Build(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Rotor != null)
                n++;
        n = Math.Min(n, Construct.MaxRotors);
        Span<CRotor> keep = stackalloc CRotor[Math.Min(c.RotorCount, Construct.MaxRotors)];
        for (var k = 0; k < keep.Length; k++)
            keep[k] = c.Rotors[k];
        if (n > c.Rotors.Length)
            c.Rotors = new CRotor[Math.Max(n, 2)];
        c.RotorCount = 0;
        c.RotorEngine = c.RotorGoverned = false;
        c.RpmGovern = 0;
        for (var i = 0; i < c.PartCount && c.RotorCount < n; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.D.Rotor is not { } d)
                continue;
            var dir = Rot24.Apply(p.Rot, d.Axis);
            var fwd = Rot24.Apply(p.Rot, d.Forward);
            var pusher = ConstructConfig.Get(p, p.D, CfgKey.Pusher, 0) >= 0.5f;
            var blades = Math.Clamp((int)ConstructConfig.Get(p, p.D, CfgKey.FanBlades, d.Blades), 2, MaxBlades);
            var lost = (ushort)((int)ConstructConfig.Get(p, p.D, CfgKey.BladeLost, 0) & (1 << blades) - 1);
            var style = StyleOf(in p, p.D);
            var more = (float)blades / d.BaseBlades;
            ref var r = ref c.Rotors[c.RotorCount++];
            r = new CRotor
            {
                Part = (ushort)i,
                Seg = p.Seg,
                At = p.Pos + Rot24.Apply(p.Rot, d.At),
                Dir = pusher ? -dir : dir,
                Fwd = fwd,
                Right = Vector3.Cross(fwd, dir),
                Sense = ConstructConfig.Get(p, p.D, CfgKey.Hand, 0) >= 0.5f ? -1 : 1,
                Lost = lost,
                Blades = (byte)blades,
                Style = (byte)Array.IndexOf(AirTable.Current.BladeStyles, style),
                PowerScale = style.Power * MathF.Pow(more, CountPower),
                PushScale = style.Push * MathF.Pow(more, CountPush),
                TopScale = style.Top,
                Alive = (byte)Math.Max(0, blades - BitOperations.PopCount(lost)),
                Feed = (short)ConstructDrillDrive.FeedOf(c, i),
                Reduction = 1,
            };
            for (var k = 0; k < keep.Length; k++)
                if (keep[k].Part == i)
                {
                    r.Omega = keep[k].Omega;
                    r.Angle = keep[k].Angle;
                    r.Induced = keep[k].Induced;
                }
            if (r.Feed >= 0)
            {
                c.RotorEngine = true;
                c.RotorGoverned |= d.Heli;
                if (c.Engine >= 0 && c.Engine < c.PartCount)
                {
                    // Reduction gears in the hub: its rated speed at the engine's peak, whatever the size of either.
                    var peak = c.EngineDef.PeakRpm / ConstructDrive.RpmPerRadS;
                    r.Reduction = MathF.Max(0.01f, peak / MathF.Max(0.01f, c.ShaftFeeds![r.Feed].Ratio) / d.RatedOmega);
                    c.RpmGovern = c.EngineDef.PeakRpm * RateOver;
                }
            }
        }
    }

    /// <summary>A rotor's blade style, from its setting.</summary>
    public static BladeStyleRow StyleOf(in CPart p, ConstructPartDef d)
    {
        var rows = AirTable.Current.BladeStyles;
        return rows[Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.BladeStyle, d.Rotor?.Style ?? 0), 0, rows.Length - 1)];
    }

    /// <summary>The rotor of a part, or -1.</summary>
    public static int Of(Construct c, int part)
    {
        for (var k = 0; k < c.RotorCount; k++)
            if (c.Rotors[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>The engine's throttle while a rotor is on its shaft: the driver's setting, or full under a helicopter rotor. A wired engine keeps its wire.</summary>
    public static float EngineThrottle(Construct c, float throttle)
    {
        if (!c.RotorEngine || c.EngineWired)
            return throttle;
        var asked = c.Driver != 255;
        for (var k = 0; k < c.RotorCount && !asked; k++)
            asked = c.Parts[c.Rotors[k].Part].InCount > 0;
        if (!asked)
            return throttle;
        return c.RotorGoverned ? 1f : MathF.Max(MathF.Abs(throttle), RotorWired(c) ? WiredThrottle(c) : c.AeroTrim);
    }

    private static bool RotorWired(Construct c)
    {
        for (var k = 0; k < c.RotorCount; k++)
            if (c.Rotors[k].Feed >= 0 && c.Parts[c.Rotors[k].Part].InCount > 0)
                return true;
        return false;
    }

    private static float WiredThrottle(Construct c)
    {
        var most = 0f;
        for (var k = 0; k < c.RotorCount; k++)
            if (c.Rotors[k].Feed >= 0 && c.Parts[c.Rotors[k].Part].InCount > 0)
                most = MathF.Max(most, Math.Clamp(c.Parts[c.Rotors[k].Part].In, 0, 1));
        return most;
    }

    /// <summary>
    /// One tick of every rotor: what it is asked, its speed, what it takes and pushes. <paramref name="free"/>: the construct
    /// moves, so the push and the torque go into it; anchored, it only turns (a test stand, a fan in a wall).
    /// </summary>
    public static void Step(Construct c, in Matrix4x4 invI, bool free, float dt)
    {
        var drive = c.Driver != 255 ? c.LiftInput : 0f;
        c.AeroTrim = c.Driver != 255 ? Math.Clamp(c.AeroTrim + drive * TrimRate * dt, 0, 1) : 0;
        var com = c.WorldCom;
        var density = ConstructAir.Density(com);
        var wind = ConstructAir.Wind(com);
        var t = AirTable.Current;
        var engineOn = ConstructPhysics.DriveOn(c);
        var load = 0f;
        var thrust = 0f;
        var live = false;
        var reaction = Vector3.Zero;
        var back = Quaternion.Conjugate(c.Rot);
        var runs = false;
        for (var k = 0; k < c.RotorCount; k++)
        {
            ref var r = ref c.Rotors[k];
            if (r.Part >= c.PartCount || c.Parts[r.Part].D.Rotor is not { } d)
                continue;
            ref var p = ref c.Parts[r.Part];
            var wired = p.InCount > 0;
            runs |= r.Feed >= 0 && wired && p.In != 0;
            var cmd = wired ? Math.Clamp(p.In, d.Heli ? -1 : 0, 1) : c.Driver != 255 ? c.AeroTrim : 0f;
            if (wired && ConstructConfig.Get(p, p.D, CfgKey.Reverse, 0) >= 0.5f)
                cmd = -cmd;
            var mix = ConstructConfig.Get(p, p.D, CfgKey.TorqueMix, 0) >= 0.5f;
            if (mix)
                cmd = Math.Clamp(cmd + MixFor(c, ref r, d, com, density), -1, 1);
            var working = p.Working && r.Alive > 0;
            var electric = r.Feed < 0 && d.MotorKw > 0;
            var want = 0f;
            var eff = 1f;
            ref readonly var rated = ref d.RatedOmega;
            if (electric)
            {
                if (working && c.Electrical && ConstructPower.On(c, r.Part))
                    want = d.Heli ? wired || c.Driver != 255 ? rated : 0 : MathF.Sqrt(MathF.Abs(cmd)) * rated; // (push goes with the square of its speed: a wire asks for push)
            }
            else if (r.Feed >= 0 && working && engineOn)
            {
                ref var f = ref c.ShaftFeeds![r.Feed];
                if ((f.GateMask & ~c.GateOpen) == 0)
                {
                    want = c.Rpm / ConstructDrive.RpmPerRadS / (f.Geared ? MathF.Max(0.01f, MathF.Abs(c.GearRatio)) : 1f) / MathF.Max(0.01f, f.Ratio) / r.Reduction;
                    eff = f.Eff * c.ShaftShare;
                }
            }
            var pitch = d.Heli ? MathF.Abs(cmd) : working ? 1f : 0f;
            var share = r.Omega / rated;
            var blades = r.Alive / (float)r.Blades;
            var work = pitch * MathF.Sqrt(pitch);
            var cube = share * share * share;
            var full = d.RatedKw * r.PowerScale * 1000f * density * cube;
            var ind = full * (1 - t.RotorProfile) * work * blades;
            var prof = full * t.RotorProfile * blades;
            if (electric)
            {
                var cap = d.MotorKw * 1000f;
                var most = d.RatedKw * r.PowerScale * 1000f * density * ((1 - t.RotorProfile) * work + t.RotorProfile) * blades;
                if (most > cap)
                    want = MathF.Min(want, rated * MathF.Cbrt(cap / most)); // (more than its motor has: it holds a lower speed)
            }
            var rate = rated / d.Spool * dt;
            r.Omega += Math.Clamp(want - r.Omega, -rate, rate);
            if (r.Omega < 0.05f && want <= 0)
                r.Omega = 0;
            r.Angle = (r.Angle + r.Sense * r.Omega * dt) % MathF.Tau;
            if (r.Omega <= 0)
                r.Angle = 0; // (stopped: every machine draws and hits it in the same place)
            live |= r.Omega > 0 || want > 0;
            r.Thrust = r.Power = 0;
            if (r.Omega <= 0 || density <= 0)
                continue;
            var used = (ind + prof) * eff;
            r.Power = used;
            if (electric)
                c.MotorWork += used;
            else if (r.Feed >= 0)
            {
                // At the crank: its torque through the hub's gears and the shaft, at the speed the rotor (not the engine's needle) turns.
                ref var f = ref c.ShaftFeeds![r.Feed];
                var crank = r.Omega * r.Reduction * MathF.Max(0.01f, f.Ratio) * (f.Geared ? MathF.Max(0.01f, MathF.Abs(c.GearRatio)) : 1f);
                load += (ind + prof) / MathF.Max(0.05f, f.Eff) / MathF.Max(1, crank);
            }
            c.WorldFrame(r.Seg, out var q, out var o);
            var at = o + Vector3.Transform(r.At, q);
            var dir = Vector3.Transform(r.Dir, q);
            var bodyDir = dir;
            var rel = c.Vel + Vector3.Cross(c.AngVel, at - com) - wind;
            var axial = Vector3.Dot(rel, dir);
            var edge2 = MathF.Max(0, rel.LengthSquared() - axial * axial);
            var push = Push(ref r, d, ind * eff * t.RotorFigure, MathF.Max(0, axial), edge2, SeaAir * density);
            var bite = axial / MathF.Max(1, d.TopSpeed * r.TopScale * share);
            var fade = axial <= 0 ? 1f : 1 - bite * bite;
            push *= MathF.Max(0, fade) * d.Duct * blades * r.PushScale;
            if (d.Heli && cmd < 0)
                push = -push;
            r.Thrust = push;
            thrust += MathF.Abs(push);
            if (!free)
                continue;
            var arm = at - com;
            if (d.Heli && (p.InCount1 > 0 || p.InCount2 > 0))
            {
                // Cyclic tips the disc: the push leans, and the hub adds its own moment (how a rotor's head steers). Plus is nose up and right wing down, as a Flight Stabilizer's outputs.
                var fwd = Vector3.Transform(r.Fwd, q);
                var right = Vector3.Transform(r.Right, q);
                var tilt = (fwd * -Math.Clamp(p.InCount1 > 0 ? p.In1 : 0, -1, 1) + right * Math.Clamp(p.InCount2 > 0 ? p.In2 : 0, -1, 1)) * MathF.Tan(d.Cyclic);
                dir = Vector3.Normalize(dir + tilt);
                c.AngVel += Construct.Mul(invI, Vector3.Cross(bodyDir, tilt) * (MathF.Abs(push) * d.Radius * d.HubMoment * dt));
            }
            if (d.Heli)
            {
                var off = c.AngVel - bodyDir * Vector3.Dot(c.AngVel, bodyDir);
                c.AngVel -= off * MathF.Min(1, HeadDamp * MathF.Abs(push) * c.InvMass / ConstructPhysics.GravityAccel * dt);
            }
            c.Vel += dir * (push * dt * c.InvMass);
            c.AngVel += Construct.Mul(invI, Vector3.Cross(arm, dir * (push * dt)));
            // The torque that turns it turns the craft the other way.
            var twist = bodyDir * (-r.Sense * used / MathF.Max(1, r.Omega));
            c.AngVel += Construct.Mul(invI, twist * dt);
            if (!mix)
                reaction += Vector3.Transform(twist, back);
            if (r.Alive < r.Blades)
            {
                // Out of balance: a hard shake across the disc, and worse the faster it turns.
                var phase = ConstructPhysics.WarmTick * (ShakeHz * MathF.Tau / World.TickRate) + k;
                var across = Vector3.Normalize(Vector3.Cross(bodyDir, MathF.Abs(bodyDir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                var side = Vector3.Cross(bodyDir, across);
                var shake = (across * MathF.Cos(phase) + side * MathF.Sin(phase)) * (MathF.Abs(push) * t.RotorShake * (r.Blades - r.Alive) * 2f / r.Blades * dt);
                c.Vel += shake * c.InvMass;
                c.AngVel += Construct.Mul(invI, Vector3.Cross(arm, shake));
            }
        }
        c.RotorLoad += (load - c.RotorLoad) * MathF.Min(1, 8f * dt); // (eased: the engine's speed answers it, and the two would chatter)
        c.RotorThrust = thrust;
        c.RotorLive = live;
        c.RotorReaction = reaction;
        c.RotorRuns = runs;
    }

    /// <summary>
    /// What a rotor can do, for a stabilizer's scan of the body: where it pushes and which way (segment frame), flat out at sea level (N), with the torque it turns the
    /// craft by (N m). Its inputs are its part's <c>DataIn</c>: throttle, or collective, cyclic pitch and cyclic roll; a wire into one is <c>InCount</c>, <c>InCount1</c>, <c>InCount2</c>.
    /// </summary>
    public static void Effector(Construct c, int k, out Vector3 at, out Vector3 dir, out float force, out Vector3 twist)
    {
        ref var r = ref c.Rotors[k];
        at = r.At;
        dir = r.Dir;
        force = Live(c, k) ? FullPush(c, k) : 0;
        twist = FullTwist(c, k);
    }

    /// <summary>What a rotor pushes flat out, at sea level, standing still (N).</summary>
    public static float FullPush(Construct c, int k)
    {
        ref var r = ref c.Rotors[k];
        if (r.Part >= c.PartCount || c.Parts[r.Part].D.Rotor is not { } d)
            return 0;
        var t = AirTable.Current;
        var share = r.Alive / (float)r.Blades;
        var power = d.RatedKw * r.PowerScale * 1000f * (1 - t.RotorProfile) * t.RotorFigure * share;
        return MathF.Pow(power, 2f / 3f) * MathF.Cbrt(2 * SeaAir * MathF.PI * d.Radius * d.Radius) * d.Duct * share * r.PushScale;
    }

    /// <summary>The torque a rotor turns the craft with flat out (N m, rest frame): what a stabilizer's mix counts on a corner.</summary>
    public static Vector3 FullTwist(Construct c, int k)
    {
        ref var r = ref c.Rotors[k];
        if (r.Part >= c.PartCount || c.Parts[r.Part].D.Rotor is not { } d)
            return Vector3.Zero;
        return r.Dir * (-r.Sense * d.RatedKw * r.PowerScale * 1000f / d.RatedOmega);
    }

    /// <summary>Can it turn now: a working rotor with its power? (A stabilizer maps what can answer it.)</summary>
    public static bool Live(Construct c, int k)
    {
        ref var r = ref c.Rotors[k];
        if (r.Part >= c.PartCount || !c.Parts[r.Part].Working || r.Alive == 0 || c.Parts[r.Part].D.Rotor is not { } d)
            return false;
        return r.Feed >= 0 ? c.EngineRunning || c.Engine >= 0 : d.MotorKw > 0 && c.Electrical && ConstructPower.On(c, r.Part);
    }

    /// <summary>A tail rotor's torque mix: the collective that cancels the other rotors' torque last tick, the way a helicopter's pedals are linked to its collective.</summary>
    private static float MixFor(Construct c, ref CRotor r, RotorDef d, Vector3 com, float density)
    {
        if (density <= 0 || c.RotorReaction == Vector3.Zero)
            return 0;
        // A unit of its push turns the body by this: about the centre of mass, in the body's frame.
        var lever = Vector3.Cross(r.At - c.Com, r.Dir);
        var size = lever.LengthSquared();
        if (size < 1e-4f)
            return 0;
        var full = MathF.Pow(d.RatedKw * 1000f * density * (1 - AirTable.Current.RotorProfile) * AirTable.Current.RotorFigure, 2f / 3f) * MathF.Cbrt(2 * SeaAir * density * MathF.PI * d.Radius * d.Radius) * d.Duct;
        return -Vector3.Dot(c.RotorReaction, lever) / size / MathF.Max(1, full);
    }

    /// <summary>
    /// The disc's momentum: the speed the power puts through it, down from the most it can be (the hover's: the sum is convex, so
    /// Newton's steps can't overshoot from above), and the push that makes.
    /// T = 2 rho A vi U, U the air's speed through it and across it; the power is T (V + vi).
    /// </summary>
    private static float Push(ref CRotor r, RotorDef d, float power, float axial, float edge2, float rho)
    {
        if (power <= 0)
        {
            r.Induced = 0;
            return 0;
        }
        var flow = 2 * rho * MathF.PI * d.Radius * d.Radius;
        var v = MathF.Cbrt(power / flow);
        for (var i = 0; i < 3; i++)
        {
            var w = axial + v;
            var u = MathF.Sqrt(edge2 + w * w);
            var f = flow * v * u * w - power;
            var slope = flow * (u * w + v * w * w / u + v * u);
            v = MathF.Max(0.05f, v - f / slope);
        }
        r.Induced = v;
        var still = MathF.Cbrt(power / flow); // (blades stall: forward speed doesn't lift it for ever on the same power)
        return MathF.Min(flow * v * MathF.Sqrt(edge2 + (axial + v) * (axial + v)), flow * still * still * Translift);
    }

    // ---- Blades ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A ray at a rotor's disc (the part's segment frame): a stopped rotor is its blades as they lie; a spinning one is a blur,
    /// and a round through it meets a blade by the share <see cref="AirTable.RotorHit"/> of the disc, however thin they are.
    /// </summary>
    public static bool Ray(Construct c, int part, Vector3 lo, Vector3 ld, ref float best, out int blade)
    {
        blade = -1;
        var k = Of(c, part);
        if (k < 0)
            return false;
        ref var r = ref c.Rotors[k];
        var d = c.Parts[part].D.Rotor!;
        var dir = r.Dir;
        var dn = Vector3.Dot(ld, dir);
        if (MathF.Abs(dn) < 1e-4f)
            return false;
        var t = Vector3.Dot(r.At - lo, dir) / dn;
        if (t < 0 || t >= best)
            return false;
        var off = lo + ld * t - r.At;
        var radius = off.Length();
        if (radius > d.Radius || radius < d.Radius * 0.08f)
            return false;
        var style = AirTable.Current.BladeStyles[r.Style];
        var station = style.Stations[Math.Clamp((int)(radius / d.Radius * 4), 0, 3)]; // (its shape where the ray crosses: a swept blade lies off its radius)
        var angle = MathF.Atan2(Vector3.Dot(off, r.Fwd), Vector3.Dot(off, r.Right)) - r.Angle - station.Y * d.Radius / radius; // (a blade's box lies along Right at no angle, and turns towards Fwd, as drawn)
        var slice = MathF.Tau / r.Blades;
        var turn = angle / slice;
        var near = (int)MathF.Round(turn);
        var away = MathF.Abs(turn - near) * slice;
        var reach = MathF.Max(d.Chord * station.X * 0.5f / radius, r.Omega > 1 ? AirTable.Current.RotorHit * style.Area * slice * 0.5f : 0);
        var at = (near % r.Blades + r.Blades) % r.Blades;
        if (away > reach || (r.Lost >> at & 1) != 0)
            return false;
        best = t;
        blade = at;
        return true;
    }

    /// <summary>
    /// A hit got through a rotor's armour: it takes it out of the blade aimed at (<paramref name="aim"/>, -1 the hub, -2: none in
    /// particular, a blast or a crash, which picks one of those left). The share of the hit the hub takes comes back.
    /// </summary>
    public static float Hit(ConstructWorld world, Construct c, int part, float dealt, int aim)
    {
        if (aim == -1)
            return 1;
        ref var p = ref c.Parts[part];
        var d = p.D;
        var rd = d.Rotor!;
        var count = Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.FanBlades, rd.Blades), 2, MaxBlades);
        var lost = (int)ConstructConfig.Get(p, d, CfgKey.BladeLost, 0);
        var blade = aim;
        if (blade < 0 || blade >= count || (lost >> blade & 1) != 0)
        {
            blade = -1;
            var pick = (int)((ConstructPhysics.WarmTick * 2654435761u >> 16) % (uint)count);
            for (var k = 0; k < count && blade < 0; k++)
                if ((lost >> (pick + k) % count & 1) == 0)
                    blade = (pick + k) % count;
            if (blade < 0)
                return 1;
        }
        if (Wear(ref p, d, blade, dealt / rd.BladeHealth))
            Lose(world, c, part, blade, lost);
        return 0.3f / count; // (the hub takes a share by how many blades share the blow: more of them don't wreck it sooner)
    }

    /// <summary>
    /// One blade's wear goes up by this share of its health; true when it is gone. Four bits a blade in the hidden wear settings, so they ride saves and CBody, however
    /// many blades there are, and only a hit touches them: the tick reads none of it. Every hit counts for at least a step.
    /// </summary>
    private static bool Wear(ref CPart p, ConstructPartDef d, int blade, float share)
    {
        var key = (CfgKey)((int)CfgKey.Blade0 + blade / 4);
        var shift = blade % 4 * 4;
        var bits = (int)ConstructConfig.Get(p, d, key, 0);
        var worn = (bits >> shift & 15) + Math.Max(1, (int)MathF.Ceiling(share * WearLevels));
        ConstructConfig.Put(ref p, d, key, bits & ~(15 << shift) | Math.Min(15, worn) << shift);
        return worn >= WearLevels;
    }

    /// <summary>A blade goes: the rotor rebuilds with one fewer, every client hears, and the blade flies off along its turn.</summary>
    private static void Lose(ConstructWorld world, Construct c, int part, int blade, int lost)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var rd = d.Rotor!;
        ConstructConfig.Put(ref p, d, CfgKey.BladeLost, lost | 1 << blade);
        c.Dirty = true;
        world.StructureDirty.Add(c.Id);
        world.Events.Add($"construct {c.Id}: a blade of the {d.Name} came off");
        var k = Of(c, part);
        if (k < 0)
            return;
        ref var r = ref c.Rotors[k];
        c.WorldFrame(r.Seg, out var q, out var o);
        var angle = r.Angle + blade * MathF.Tau / r.Blades;
        var fwd = Vector3.Transform(r.Fwd, q);
        var right = Vector3.Transform(r.Right, q);
        var dir = Vector3.Transform(r.Dir, q);
        var out_ = right * MathF.Cos(angle) + fwd * MathF.Sin(angle);
        var at = o + Vector3.Transform(r.At, q) + out_ * (rd.Radius * 0.55f);
        var material = d.IsExtrusion ? d.ExKey.Material : RecyclingTable.Current.IdOf("metal");
        if (Extrusion.Blade(rd.Radius, rd.Chord, r.Style, material) is not { } piece || ConstructEdits.DropLoose(world, piece, at, q * Quaternion.CreateFromAxisAngle(r.Dir, angle)) is not { } body)
            return;
        body.Owner = c.Owner;
        body.Wreck = true;
        body.Vel = c.PointVel(at) + Vector3.Cross(dir, out_) * MathF.Min(40, r.Sense * r.Omega * rd.Radius * 0.1f);
        body.AngVel = dir * Math.Clamp(r.Sense * r.Omega * 0.25f, -ConstructPhysics.MaxSpin, ConstructPhysics.MaxSpin);
    }

    /// <summary>The look line's note on a rotor.</summary>
    public static string Note(Construct c, int part)
    {
        var k = Of(c, part);
        if (k < 0)
            return "";
        ref var r = ref c.Rotors[k];
        var d = c.Parts[part].D.Rotor!;
        var source = r.Feed >= 0 ? "on the shaft" : d.MotorKw > 0 ? "electric" : "no shaft linked";
        var label = AirTable.Current.BladeStyles[r.Style].Label.ToLowerInvariant();
        var blades = r.Alive < r.Blades ? $"{r.Alive} of {r.Blades} {label} blades, out of balance" : $"{r.Blades} {label} blades";
        return $"{blades}, {r.Omega * ConstructDrive.RpmPerRadS:0} of {d.RatedOmega * ConstructDrive.RpmPerRadS:0} rpm, {source}";
    }

    public static long LookKey(Construct c, int part) => Of(c, part) is var k and >= 0 ? (long)(c.Rotors[k].Omega * 0.2f) ^ (long)c.Rotors[k].Alive << 24 : 0;

    /// <summary>The driver's HUD: how fast its rotors turn, a share of rated (this reads on a client too).</summary>
    public static float SpeedShare(Construct c)
    {
        var most = 0f;
        for (var k = 0; k < c.RotorCount; k++)
            if (c.Rotors[k].Part < c.PartCount && c.Parts[c.Rotors[k].Part].D.Rotor is { } d)
                most = MathF.Max(most, c.Rotors[k].Omega / d.RatedOmega);
        return most;
    }
}

public static partial class ConstructPhysics
{
    /// <summary>The physics' tick count (a lost blade's shake, which blade a blast finds).</summary>
    public static uint WarmTick => _warmTick;
}
