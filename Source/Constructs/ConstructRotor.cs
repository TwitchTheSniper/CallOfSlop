using System.Numerics;

namespace CallOfSlop;

/// <summary>One rotor of a construct, cached with its parts.</summary>
public struct CRotor
{
    public ushort Part;
    public byte Seg; // what it hangs on: a joint tilts it
    public byte Alive; // blades left
    public short Feed; // its entry in the shaft feeds, or -1
    public ushort Lost; // the blades gone, a bit each
    public Vector3 At, Dir, Fwd, Right; // the segment's frame: its disc's middle, its push, which way cyclic tilts it
    public float Sense; // 1 right hand about its push, -1 left
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
    public const int MaxBlades = 6;
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
            var lost = (ushort)ConstructConfig.Get(p, p.D, CfgKey.BladeLost, 0);
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
                Alive = (byte)Math.Max(0, d.Blades - BitOperations.PopCount(lost)),
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
                    want = d.Heli ? wired || c.Driver != 255 ? rated : 0 : MathF.Abs(cmd) * rated;
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
            var blades = r.Alive / (float)d.Blades;
            var work = pitch * MathF.Sqrt(pitch);
            var cube = share * share * share;
            var full = d.RatedKw * 1000f * density * cube;
            var ind = full * (1 - t.RotorProfile) * work * blades;
            var prof = full * t.RotorProfile * blades;
            if (electric)
            {
                var cap = d.MotorKw * 1000f;
                var most = d.RatedKw * 1000f * density * ((1 - t.RotorProfile) * work + t.RotorProfile) * blades;
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
            var bite = axial / MathF.Max(1, d.TopSpeed * share);
            var fade = axial <= 0 ? 1f : 1 - bite * bite;
            push *= MathF.Max(0, fade) * d.Duct * blades;
            if (d.Heli && cmd < 0)
                push = -push;
            r.Thrust = push;
            thrust += MathF.Abs(push);
            if (!free)
                continue;
            var arm = at - com;
            if (d.Heli && (p.InCount1 > 0 || p.InCount2 > 0))
            {
                // Cyclic tips the disc: the push leans, and the hub adds its own moment (how a rotor's head steers).
                var fwd = Vector3.Transform(r.Fwd, q);
                var right = Vector3.Transform(r.Right, q);
                var tilt = (fwd * Math.Clamp(p.InCount1 > 0 ? p.In1 : 0, -1, 1) + right * Math.Clamp(p.InCount2 > 0 ? p.In2 : 0, -1, 1)) * MathF.Tan(d.Cyclic);
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
            if (r.Alive < d.Blades)
            {
                // Out of balance: a hard shake across the disc, and worse the faster it turns.
                var phase = ConstructPhysics.WarmTick * (ShakeHz * MathF.Tau / World.TickRate) + k;
                var across = Vector3.Normalize(Vector3.Cross(bodyDir, MathF.Abs(bodyDir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                var side = Vector3.Cross(bodyDir, across);
                var shake = (across * MathF.Cos(phase) + side * MathF.Sin(phase)) * (MathF.Abs(push) * t.RotorShake * (d.Blades - r.Alive) * dt);
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
        var angle = MathF.Atan2(Vector3.Dot(off, r.Fwd), Vector3.Dot(off, r.Right)) - r.Angle; // (a blade's box lies along Right at no angle, and turns towards Fwd, as drawn)
        var slice = MathF.Tau / d.Blades;
        var turn = angle / slice;
        var near = (int)MathF.Round(turn);
        var away = MathF.Abs(turn - near) * slice;
        var reach = MathF.Max(d.Chord * 0.5f / radius, r.Omega > 1 ? AirTable.Current.RotorHit * slice * 0.5f : 0);
        var at = (near % d.Blades + d.Blades) % d.Blades;
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
        var lost = (int)ConstructConfig.Get(p, d, CfgKey.BladeLost, 0);
        var blade = aim;
        if (blade < 0 || blade >= rd.Blades || (lost >> blade & 1) != 0)
        {
            blade = -1;
            var pick = (int)(ConstructPhysics.WarmTick * 2654435761u >> 16) % rd.Blades;
            for (var k = 0; k < rd.Blades && blade < 0; k++)
                if ((lost >> (pick + k) % rd.Blades & 1) == 0)
                    blade = (pick + k) % rd.Blades;
            if (blade < 0)
                return 1;
        }
        var key = (CfgKey)((int)CfgKey.Blade0 + blade);
        var left = ConstructConfig.Get(p, d, key, 1) - dealt / rd.BladeHealth;
        ConstructConfig.Put(ref p, d, key, MathF.Max(0, left));
        if (left <= 0)
            Lose(world, c, part, blade, lost);
        return 0.1f;
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
        var angle = r.Angle + blade * MathF.Tau / rd.Blades;
        var fwd = Vector3.Transform(r.Fwd, q);
        var right = Vector3.Transform(r.Right, q);
        var dir = Vector3.Transform(r.Dir, q);
        var out_ = right * MathF.Cos(angle) + fwd * MathF.Sin(angle);
        var at = o + Vector3.Transform(r.At, q) + out_ * (rd.Radius * 0.55f);
        var name = rd.Radius < 2f ? "Loose Blade" : rd.Radius < 8f ? "Medium Loose Blade" : rd.Radius < 24f ? "Large Loose Blade" : "Capital Loose Blade";
        if (ConstructParts.Find(name) is not { } piece || ConstructEdits.DropLoose(world, piece, at, q) is not { } body)
            return;
        body.Owner = c.Owner;
        body.Wreck = true;
        body.Vel = c.PointVel(at) + Vector3.Cross(dir, out_) * MathF.Min(40, r.Sense * r.Omega * rd.Radius * 0.1f);
        body.AngVel = dir * Math.Clamp(r.Sense * r.Omega * 0.25f, -ConstructPhysics.MaxSpin, ConstructPhysics.MaxSpin);
    }

    /// <summary>The attach tool's new blade. Null when it worked, else why not.</summary>
    public static string? Refit(ConstructWorld world, Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount || c.Parts[part].D.Rotor is not { } rd)
            return "that isn't a rotor";
        ref var p = ref c.Parts[part];
        var d = p.D;
        var lost = (int)ConstructConfig.Get(p, d, CfgKey.BladeLost, 0);
        if (lost == 0)
            return "it has all its blades";
        var blade = BitOperations.TrailingZeroCount(lost);
        ConstructConfig.Put(ref p, d, CfgKey.BladeLost, lost & ~(1 << blade));
        ConstructConfig.Put(ref p, d, (CfgKey)((int)CfgKey.Blade0 + blade), 1);
        c.Dirty = true;
        world.StructureDirty.Add(c.Id);
        return null;
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
        var blades = r.Alive < d.Blades ? $"{r.Alive} of {d.Blades} blades, out of balance" : $"{d.Blades} blades";
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
