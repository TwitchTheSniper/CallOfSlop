using System.Numerics;

namespace CallOfSlop;

/// <summary>One lifting surface of a construct, cached with its parts (segment frame: a wing on a joint flies by its real pose).</summary>
public struct CSurface
{
    public ushort Part;
    public byte Seg;
    public bool Control; // it has a logic input and a hinge in the part
    public Vector3 At, N, F; // where it acts, its lift side, its leading edge
    public float Area, Cl0, Slope, Stall, Cd0, Induced;
    public float Deflect; // radians now, leading edge up is plus (it rides the pose to clients)
    public float Sent; // what the pose last carried
    public bool Scanned; // a stabilizer commands it (ConstructEffectors.cs); a wire by hand leaves it to the wire
    public float Command; // what it tells it, -1..1
}

public sealed partial class Construct
{
    public const int MaxSurfaces = 512;
    public CSurface[] Surfaces = [];
    public int SurfaceCount;
    public float SurfaceArea; // m2 in all
    public float SurfaceScale = 1; // the kinder scale past a size (Air.json)
    public float SurfaceSpan; // m: how wide its surfaces reach (the ground's cushion goes with it)
    public float FarArea, FarCl0, FarSlope, FarStall, FarCd0, FarInduced; // the summed path: the surfaces that lift, as one
    public Vector3 FarN = Vector3.UnitY, FarF = -Vector3.UnitZ;
    public float BodyDragArea; // Cd A of the rest of it, m2
    public ushort[] Controls = []; // the control surfaces, indices into Surfaces (their angles ride the pose)
    public int ControlCount;
    public int PoseAirAt; // the pose's turn through the rotors and control surfaces
    public float GroundHeight = 1e6f; // m under its centre of mass (the 4 Hz step's ray)
    public float AeroLift; // N this tick the surfaces carry up
    public float AeroDrag; // N this tick, the whole craft
    public bool AeroFlying; // the air, not the arcade damping, slows it this tick
}

/// <summary>
/// Wings (docs/sandbox/CONSTRUCTS.md, "Wings"): every lifting surface in one place. Near a player each surface works out its
/// own angle of attack from its own velocity through the air, lift and drag by that angle with a stall past it, and pushes at
/// its own centre, so a badly balanced plane really tumbles. Further off (<see cref="Construct.LiftNear"/>) the surfaces that
/// lift work as one through the centre of mass, and a sleeping craft costs nothing. Everything here is read from
/// <see cref="SurfaceDef"/>, so a new surface is data, and from <see cref="ConstructAir"/>. A surface's frame is its segment's,
/// so a wing on an ordinary hinge changes its lift by its real pose. Nothing allocates.
/// </summary>
public static class ConstructAero
{
    private const float SeaAir = 1.225f; // kg/m3
    private const float StallWidth = 10 * MathF.PI / 180f; // radians over which a stall goes from lift to a flat plate

    /// <summary>Rebuilt with the caches, after the mass.</summary>
    public static void Build(Construct c)
    {
        var n = 0;
        foreach (var i in c.Specials) // (surfaces are special parts)
            if (c.Parts[i].D.Surface != null && !c.Parts[i].Broken)
                n++;
        n = Math.Min(n, Construct.MaxSurfaces);
        Span<float> keep = stackalloc float[Math.Min(c.SurfaceCount, 64)];
        Span<ushort> keepPart = stackalloc ushort[keep.Length];
        for (var k = 0; k < keep.Length; k++)
        {
            keep[k] = c.Surfaces[k].Deflect;
            keepPart[k] = c.Surfaces[k].Part;
        }
        if (n > c.Surfaces.Length)
            c.Surfaces = new CSurface[Math.Min(Construct.MaxSurfaces, Math.Max(n, 8))];
        if (n > c.Controls.Length)
            c.Controls = new ushort[c.Surfaces.Length];
        c.SurfaceCount = c.ControlCount = 0;
        c.SurfaceArea = 0;
        c.FarArea = c.FarCl0 = c.FarSlope = c.FarStall = c.FarCd0 = c.FarInduced = 0;
        var far = Vector3.Zero;
        var farF = Vector3.Zero;
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var i in c.Specials)
        {
            if (c.SurfaceCount >= n)
                break;
            ref var p = ref c.Parts[i];
            if (p.D.Surface is not { } d || p.Broken)
                continue;
            var at = p.Pos + Rot24.Apply(p.Rot, d.At);
            var normal = Rot24.Apply(p.Rot, d.Normal);
            var forward = Rot24.Apply(p.Rot, d.Forward);
            ref var s = ref c.Surfaces[c.SurfaceCount++];
            s = new CSurface { Part = (ushort)i, Seg = p.Seg, Control = d.Control > 0, At = at, N = normal, F = forward, Area = d.Area, Cl0 = d.Cl0, Slope = d.Slope, Stall = d.Stall, Cd0 = d.Cd0, Induced = d.Induced };
            for (var k = 0; k < keep.Length; k++)
                if (keepPart[k] == i)
                    s.Deflect = keep[k];
            if (s.Control)
                c.Controls[c.ControlCount++] = (ushort)(c.SurfaceCount - 1);
            c.SurfaceArea += d.Area;
            if (normal.Y > 0.7f && p.Seg == 0 && !s.Control)
            {
                c.FarArea += d.Area;
                c.FarCl0 += d.Cl0 * d.Area;
                c.FarSlope += d.Slope * d.Area;
                c.FarStall += d.Stall * d.Area;
                c.FarCd0 += d.Cd0 * d.Area;
                c.FarInduced += d.Induced * d.Area;
                far += normal * d.Area;
                farF += forward * d.Area;
            }
            lo = Vector3.Min(lo, at);
            hi = Vector3.Max(hi, at);
        }
        if (c.FarArea > 0)
        {
            c.FarCl0 /= c.FarArea;
            c.FarSlope /= c.FarArea;
            c.FarStall /= c.FarArea;
            c.FarCd0 /= c.FarArea;
            c.FarInduced /= c.FarArea;
            c.FarN = Vector3.Normalize(far);
            c.FarF = farF.LengthSquared() > 1e-6f ? Vector3.Normalize(farF) : -Vector3.UnitZ;
        }
        var t = AirTable.Current;
        c.SurfaceScale = c.SurfaceArea <= t.SoftWingM2 ? 1f : MathF.Min(t.SoftMost, MathF.Pow(c.SurfaceArea / t.SoftWingM2, t.SoftPower));
        c.SurfaceSpan = c.SurfaceCount > 0 ? MathF.Max(1, MathF.Max(hi.X - lo.X, hi.Z - lo.Z)) : 0;
        c.BodyDragArea = t.BodyDrag * MathF.Pow(MathF.Max(1, c.Mass), 2f / 3f);
    }

    /// <summary>Lift and drag coefficients at an angle of attack (radians): straight to the stall, then over to a flat plate; any angle, flying backwards too.</summary>
    public static void Coefficients(float alpha, float cl0, float slope, float stall, float cd0, float induced, out float cl, out float cd)
    {
        var a = alpha;
        if (a > MathF.PI / 2)
            a -= MathF.PI;
        else if (a < -MathF.PI / 2)
            a += MathF.PI;
        var fromZero = a + (slope > 0 ? cl0 / slope : 0); // (from the angle of no lift)
        var past = MathF.Abs(fromZero) - stall;
        cl = slope * fromZero;
        cd = cd0 + induced * cl * cl;
        if (past <= 0)
            return;
        var t = MathF.Min(1, past / StallWidth);
        t *= t * (3 - 2 * t);
        var sin = MathF.Sin(a);
        var cos = MathF.Cos(a);
        cl += (2.2f * sin * cos - cl) * t;
        cd += (cd0 + 1.8f * sin * sin - cd) * t;
    }

    /// <summary>The lift coefficient a surface can reach: the top of its straight line.</summary>
    public static float ClMax(in CSurface s) => s.Slope * s.Stall;

    /// <summary>Where all its surfaces lift from (rest frame, by area), and how much area: the balance marker's and the HUD's sum.</summary>
    public static float CentreOfLift(Construct c, out Vector3 at)
    {
        at = Vector3.Zero;
        var area = 0f;
        for (var k = 0; k < c.SurfaceCount; k++)
        {
            ref var s = ref c.Surfaces[k];
            var share = s.Area * MathF.Abs(s.N.Y);
            at += s.At * share;
            area += share;
        }
        at = area > 0 ? at / area : c.Com;
        return area;
    }

    /// <summary>The speed (m/s) at which its surfaces lift its weight at their best angle, in air this thick (share of sea level), level: its stall speed.</summary>
    public static float StallSpeed(Construct c, float density = 1)
    {
        var lift = 0f;
        for (var k = 0; k < c.SurfaceCount; k++)
        {
            ref var s = ref c.Surfaces[k];
            if (s.N.Y > 0.7f && !s.Control)
                lift += s.Area * ClMax(in s);
        }
        lift *= c.SurfaceScale;
        return lift > 0 && density > 0 ? MathF.Sqrt(2 * c.Mass * ConstructPhysics.GravityAccel / (SeaAir * density * lift)) : 0;
    }

    private const float MarkerSpeed = 40; // m/s: the speed the balance markers count a surface's lift at

    /// <summary>What a surface lifts at sea level at <see cref="MarkerSpeed"/>, at a small angle, kg: by its lift slope, as it answers a change of pitch.</summary>
    public static float LiftKgAt(Construct c, in CSurface s) => 0.5f * SeaAir * MarkerSpeed * MarkerSpeed * s.Area * s.Slope * 0.1f * c.SurfaceScale / MathF.Max(1, ConstructPhysics.GravityAccel);

    /// <summary>
    /// What a control surface can do, for a stabilizer's scan of the body: where it pushes and which way for a plus signal (segment frame), and the
    /// force (N) a full signal changes at this air speed, at sea level. False: it isn't a control surface. Its input is the part's own, <c>DataIn[0]</c>; a wire into it is <c>InCount &gt; 0</c>.
    /// </summary>
    public static bool Effector(Construct c, int surface, float speed, out Vector3 at, out Vector3 dir, out float force)
    {
        ref var s = ref c.Surfaces[surface];
        at = s.At;
        dir = s.N;
        force = 0;
        if (!s.Control || s.Part >= c.PartCount || c.Parts[s.Part].D.Surface is not { } d)
            return false;
        if (d.Sign < 0 != ConstructConfig.Get(c.Parts[s.Part], c.Parts[s.Part].D, CfgKey.Reverse, 0) >= 0.5f)
            dir = -dir;
        force = 0.5f * SeaAir * speed * speed * s.Area * s.Slope * ThrowOf(c.Parts[s.Part], c.Parts[s.Part].D, d.Control) * d.Effect * c.SurfaceScale;
        return true;
    }

    public const float ThrowMost = 0.75f; // rad: no setting takes a surface or nozzle past this

    /// <summary>A control surface's or nozzle's full deflection as its Throw sets it (radians, from what its part gives it).</summary>
    public static float ThrowOf(in CPart p, ConstructPartDef d, float full) => MathF.Min(full * ConstructConfig.Get(p, d, CfgKey.Throw, 100) / 100f, MathF.Max(full, ThrowMost));

    /// <summary>Soft around the middle, the same at the ends (expo 0..1).</summary>
    public static float Curve(float x, float expo) => x * (1 - expo) + x * x * x * expo;

    /// <summary>A control surface's input, its trim and its reversal: how far it wants to be (radians). Only its own wire moves it.</summary>
    public static float Asked(in CPart p, ConstructPartDef d, bool scanned = false, float command = 0)
    {
        var s = d.Surface!;
        var cmd = !p.Working ? 0f : scanned ? Math.Clamp(command, -1, 1) * s.Sign : p.InCount > 0 ? Math.Clamp(p.In, -1, 1) * s.Sign : 0f;
        if (ConstructConfig.Get(p, d, CfgKey.Reverse, 0) >= 0.5f)
            cmd = -cmd;
        var most = ThrowOf(p, d, s.Control);
        return Math.Clamp(Curve(cmd, ConstructConfig.Get(p, d, CfgKey.Expo, 0) / 100f) * most + ConstructConfig.Get(p, d, CfgKey.Trim, 0) * MathF.PI / 180f, -most, most);
    }

    /// <summary>One tick of a free, awake construct's surfaces (the thrusters and rotors' hover hold reads what they carry).</summary>
    public static void Step(Construct c, in Matrix4x4 invI, Vector3 weight, float dt)
    {
        var com = c.WorldCom;
        var density = ConstructAir.Density(com);
        c.AeroLift = 0;
        c.AeroDrag = 0;
        var wind = ConstructAir.Wind(com);
        var rel = c.Vel - wind;
        var speed2 = rel.LengthSquared();
        c.AeroFlying = density > 0 && speed2 > 4;
        for (var k = 0; k < c.SurfaceCount; k++)
        {
            ref var s = ref c.Surfaces[k];
            if (!s.Control || s.Part >= c.PartCount || c.Parts[s.Part].D.Surface is not { } sd)
                continue;
            var want = Asked(in c.Parts[s.Part], c.Parts[s.Part].D, s.Scanned && c.StabOwns, s.Command);
            var rate = sd.ControlRate * dt;
            s.Deflect += Math.Clamp(want - s.Deflect, -rate, rate);
        }
        if (density <= 0 || c.Mass <= 0)
            return;
        var air = 0.5f * SeaAir * density;
        var up = weight.LengthSquared() > 1e-6f ? -Vector3.Normalize(weight) : ConstructPhysics.HoldUp(com);
        var cushion = c.SurfaceCount > 0 ? ConstructAir.GroundEffect(c.GroundHeight, c.SurfaceSpan) : 1f;
        var vel = c.Vel;
        var ang = c.AngVel;
        if (c.LiftNear)
        {
            for (var k = 0; k < c.SurfaceCount; k++)
                OneSurface(c, ref c.Surfaces[k], invI, com, vel - wind, ang, up, air, cushion, dt);
        }
        else if (c.FarArea > 0 && speed2 > 1)
            Far(c, rel, MathF.Sqrt(speed2), up, air, cushion, dt);
        if (speed2 > 1 && c.BodyDragArea > 0)
        {
            // The rest of it: what isn't wing still has to go through the air.
            var v = MathF.Sqrt(speed2);
            var dv = MathF.Min(v * 0.5f, air * c.BodyDragArea * speed2 * c.InvMass * dt);
            c.Vel -= rel / v * dv;
            c.AeroDrag += dv * c.Mass / dt;
        }
    }

    private static void OneSurface(Construct c, ref CSurface s, in Matrix4x4 invI, Vector3 com, Vector3 relVel, Vector3 ang, Vector3 up, float air, float cushion, float dt)
    {
        if (s.Part >= c.PartCount)
            return;
        Quaternion q;
        Vector3 at;
        if (s.Seg == 0)
        {
            q = c.Rot;
            at = c.Pos + Vector3.Transform(s.At, q);
        }
        else
        {
            c.WorldFrame(s.Seg, out q, out var o);
            at = o + Vector3.Transform(s.At, q);
        }
        var r = at - com;
        var v = relVel + Vector3.Cross(ang, r);
        var n = Vector3.Transform(s.N, q);
        var f = Vector3.Transform(s.F, q);
        var vn = Vector3.Dot(v, n);
        var vf = Vector3.Dot(v, f);
        var v2 = vn * vn + vf * vf;
        if (v2 < 0.25f)
            return;
        var alpha = MathF.Atan2(-vn, vf);
        if (s.Control && c.Parts[s.Part].D.Surface is { } sd)
            alpha += s.Deflect * sd.Effect;
        ConstructAero.Coefficients(alpha, s.Cl0, s.Slope, s.Stall, s.Cd0, s.Induced, out var cl, out var cd);
        ref var p = ref c.Parts[s.Part];
        var share = p.D.Surface is { Tears: true } ? Math.Clamp(p.Health / MathF.Max(1, p.D.Health), 0, 1) : 1f;
        var q0 = air * v2 * s.Area * c.SurfaceScale * share;
        var speed = MathF.Sqrt(v2);
        var eL = (n * vf - f * vn) / speed; // (across the flow, in the surface's chord plane: lift)
        var eD = -(n * vn + f * vf) / speed; // (down the flow: drag)
        var j = (eL * (cl * q0 * cushion) + eD * (cd * q0 / cushion)) * dt;
        // Never more than brings that point to the air's speed: a light wing at speed can't ring in one tick.
        var most = 0.8f * speed / MathF.Max(1e-6f, c.InvMass + 2 * Vector3.Dot(Vector3.Cross(Construct.Mul(invI, Vector3.Cross(r, eL)), r), eL));
        var size = j.Length();
        if (size > most)
            j *= most / size;
        c.Vel += j * c.InvMass;
        c.AngVel += Construct.Mul(invI, Vector3.Cross(r, j));
        c.AeroLift += Vector3.Dot(j, up) / dt;
        c.AeroDrag += -Vector3.Dot(j, v) / speed / dt;
    }

    /// <summary>The summed path: what lifts, as one, through the centre of mass (it can't tumble out here, so it costs one surface).</summary>
    private static void Far(Construct c, Vector3 rel, float speed, Vector3 up, float air, float cushion, float dt)
    {
        var n = Vector3.Transform(c.FarN, c.Rot);
        var f = Vector3.Transform(c.FarF, c.Rot);
        var vn = Vector3.Dot(rel, n);
        var vf = Vector3.Dot(rel, f);
        var v2 = vn * vn + vf * vf;
        if (v2 < 0.25f)
            return;
        ConstructAero.Coefficients(MathF.Atan2(-vn, vf), c.FarCl0, c.FarSlope, c.FarStall, c.FarCd0, c.FarInduced, out var cl, out var cd);
        var q0 = air * v2 * c.FarArea * c.SurfaceScale;
        var v = MathF.Sqrt(v2);
        var force = (n * vf - f * vn) / v * (cl * q0 * cushion) - (n * vn + f * vf) / v * (cd * q0 / cushion);
        var j = force * dt;
        var most = 0.8f * speed * c.Mass;
        if (j.Length() > most)
            j *= most / j.Length();
        c.Vel += j * c.InvMass;
        c.AngVel *= 1 - MathF.Min(1, 2f * dt); // (no tail out here to hold it: it doesn't swing either)
        c.AeroLift += Vector3.Dot(j, up) / dt;
    }

    /// <summary>The look line's note on a surface.</summary>
    public static string Note(Construct c, int part)
    {
        var d = c.Parts[part].D.Surface!;
        var stall = StallSpeed(c);
        return $"{d.Area:0.##} m2 of {c.SurfaceArea:0.#}, stalls at {stall * 3.6f:0} km/h{(d.Control > 0 ? ", wire its angle" : "")}";
    }

    public static long LookKey(Construct c, int part) => (long)(c.SurfaceArea * 10) ^ (long)(c.Mass * 0.1f) << 12;
}
