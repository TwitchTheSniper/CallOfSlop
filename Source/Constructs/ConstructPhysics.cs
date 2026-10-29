using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The construct physics step (server; the client only draws what it hears). Cheap and stable on purpose:
/// <list type="bullet">
/// <item>each construct is one rigid body (box inertia from its parts) integrated at the fixed 60 Hz tick;</item>
/// <item>against the world: up to <see cref="Construct.MaxPoints"/> hull points per body, each probed with two rays (down,
/// and along its motion), giving speculative contacts solved with sequential impulses (friction included);</item>
/// <item>between constructs: hull points of one against the part boxes of the other;</item>
/// <item>wheels are raycast wheels: a spring-damper along the suspension, side grip, drive and brakes, clamped to a
/// friction circle;</item>
/// <item>joints are angles (reduced coordinates): a passive hinge swings, damped, between its limits;</item>
/// <item>a body that stays still sleeps and costs nothing until something wakes it.</item>
/// </list>
/// Everything uses fixed arrays: no allocation per tick.
/// </summary>
public static class ConstructPhysics
{
    public const int Iterations = 6;
    public const int MaxContacts = 1024;
    public const float Slop = 0.01f;
    public const float Beta = 0.25f;
    public const float MaxSpeed = 60f;
    public const float MaxSpin = 18f;
    public const int SleepAfterTicks = 40;
    public const float AntiRoll = 0.7f; // 0: tyres push at the ground; 1: at the centre of mass's height (no roll at all)

    public struct Contact
    {
        public short A, B; // body indices in the world's list (B -1: the static world)
        public Vector3 Point, Normal; // normal points from B into A
        public float Gap; // > 0: still apart by this much; < 0: overlapping
        public float Friction;
        public float Jn, Jt1, Jt2; // accumulated impulses
        public Vector3 T1, T2;
        public float MassN, MassT1, MassT2, Bias;
    }

    private static readonly Contact[] _contacts = new Contact[MaxContacts];
    private static int _contactCount;
    private static Matrix4x4[] _invI = new Matrix4x4[ConstructWorld.MaxBodies];

    public static int LastContacts => _contactCount;
    public static int LastRays { get; private set; }

    public static float GravityAccel => World.Gravity * World.GravityScale;

    /// <summary>One tick for every awake free body of <paramref name="world"/>.</summary>
    public static void Step(ConstructWorld world, IWorldQuery query, float dt)
    {
        var bodies = world.Bodies;
        var n = bodies.Count;
        if (_invI.Length < n)
            _invI = new Matrix4x4[n];
        _contactCount = 0;
        LastRays = 0;
        var g = new Vector3(0, -GravityAccel, 0);
        for (var i = 0; i < n; i++)
        {
            var c = bodies[i];
            if (c.Dirty)
                c.Rebuild();
            c.PrevPos = c.Pos;
            c.PrevRot = c.Rot;
            for (var j = 0; j < c.JointCount; j++)
                c.Joints[j].PrevAngle = c.Joints[j].Angle;
            if (c.Anchored || c.Asleep)
            {
                _invI[i] = default;
                StepJoints(c, dt);
                continue;
            }
            _invI[i] = c.InvInertiaWorld();
            c.Vel += g * dt;
            if (c.WheelCount > 0)
                Wheels(c, query, _invI[i], dt);
            WorldContacts(c, (short)i, query, dt);
        }
        BodyContacts(world, dt);
        Solve(world, dt);
        for (var i = 0; i < n; i++)
        {
            var c = bodies[i];
            if (c.Anchored || c.Asleep)
                continue;
            Integrate(c, dt);
            StepJoints(c, dt);
            Sleep(c);
        }
    }

    // ---- Wheels -------------------------------------------------------------------------------------------------

    private static void Wheels(Construct c, IWorldQuery query, in Matrix4x4 invI, float dt)
    {
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        var fwdBody = Vector3.Transform(c.SeatForward(), c.Rot);
        var com = c.WorldCom;
        var share = c.Mass / c.WheelCount;
        var driving = c.CanDrive && c.Driver != 255 && c.Fuel > 0;
        var engine = driving ? c.DefOf(c.Engine) : null;
        var touching = 0;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Touching)
                touching++;
        var forwardSpeed = Vector3.Dot(c.Vel, fwdBody);
        if (driving && c.Throttle != 0)
            c.Fuel = MathF.Max(0, c.Fuel - engine!.BurnRate * MathF.Abs(c.Throttle) * dt);
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            var origin = c.ToWorld(w.Centre);
            var len = w.Travel + w.Radius;
            LastRays++;
            w.PrevCompression = w.Compression;
            if (!query.Raycast(origin, -up, len, QueryFilter.All, out var hit))
            {
                w.Touching = false;
                w.Compression = 0;
                continue;
            }
            var wasTouching = w.Touching;
            w.Touching = true;
            var x = MathF.Min(len - hit.Distance, w.Travel);
            w.Compression = x;
            if (!wasTouching)
                w.PrevCompression = x; // first touch: no damping kick
            var omega = MathF.Tau * w.Frequency;
            var k_s = share * omega * omega;
            var c_d = 2 * w.Damping * MathF.Sqrt(k_s * share);
            var rate = (w.Compression - w.PrevCompression) / dt;
            var force = MathF.Max(0, k_s * x + c_d * rate);
            var at = origin - up * hit.Distance; // (the contact under the wheel)
            var r = at - com;
            // Bottomed out: a hard stop on top of the spring (whatever is left of the step, at once).
            var normalImpulse = force * dt;
            if (len - hit.Distance > w.Travel)
            {
                var vn = Vector3.Dot(c.PointVel(at), up);
                if (vn < 0)
                    normalImpulse += -vn / InvMassAt(c, invI, r, up);
            }
            ApplyImpulse(c, invI, r, up * normalImpulse);
            // Tyre: forward along the ground across the axle, side along the axle.
            var n = hit.Normal;
            var axle = Vector3.TransformNormal(w.Axle, Matrix4x4.CreateFromQuaternion(c.Rot));
            var fwd = Vector3.Cross(axle, n);
            if (fwd.LengthSquared() < 1e-6f)
                continue;
            fwd = Vector3.Normalize(fwd);
            if (Vector3.Dot(fwd, fwdBody) < 0)
                fwd = -fwd;
            if (w.Steers && c.Steer != 0)
                fwd = Vector3.Normalize(Vector3.Transform(fwd, Quaternion.CreateFromAxisAngle(n, -c.Steer)));
            var side = Vector3.Normalize(Vector3.Cross(n, fwd));
            var vp = c.PointVel(at);
            var maxFriction = normalImpulse * w.Grip;
            var vSide = Vector3.Dot(vp, side);
            var shareOf = 1f / MathF.Max(1, touching); // each wheel takes its share of stopping the body (not all of it)
            var jSide = -vSide / InvMassAt(c, invI, r, side) * 0.9f * shareOf;
            var vFwd = Vector3.Dot(vp, fwd);
            var jFwd = 0f;
            if (driving && touching > 0)
            {
                var want = c.Throttle;
                var top = engine!.TopSpeed;
                if (want > 0 && forwardSpeed < top)
                    jFwd += want * engine.EngineForce / touching * dt * MathF.Min(1, (top - forwardSpeed) / 3f);
                else if (want < 0 && forwardSpeed > -top * 0.4f)
                    jFwd += want * engine.EngineForce / touching * dt * 0.6f;
            }
            var stop = -vFwd / InvMassAt(c, invI, r, fwd) * shareOf;
            if (c.Brake || ((!driving || c.Throttle == 0) && MathF.Abs(vFwd) < 1.5f))
                jFwd = stop; // brakes (and a car left in neutral stays put)
            else
                jFwd += stop * 0.01f; // rolling resistance
            // Friction circle.
            var total = MathF.Sqrt(jSide * jSide + jFwd * jFwd);
            if (total > maxFriction && total > 0)
            {
                var k2 = maxFriction / total;
                jSide *= k2;
                jFwd *= k2;
            }
            // Grip pushes at a point raised most of the way to the centre of mass: much less roll (an arcade anti-roll bar).
            var rGrip = r + up * (Vector3.Dot(-r, up) * AntiRoll);
            ApplyImpulse(c, invI, rGrip, side * jSide + fwd * jFwd);
            w.Spin += vFwd / w.Radius * dt;
        }
    }

    private static float InvMassAt(Construct c, in Matrix4x4 invI, Vector3 r, Vector3 dir)
    {
        var rn = Vector3.Cross(r, dir);
        return c.InvMass + Vector3.Dot(rn, Construct.Mul(invI, rn));
    }

    private static void ApplyImpulse(Construct c, in Matrix4x4 invI, Vector3 r, Vector3 j)
    {
        c.Vel += j * c.InvMass;
        c.AngVel += Construct.Mul(invI, Vector3.Cross(r, j));
    }

    // ---- Contacts -----------------------------------------------------------------------------------------------

    private static void WorldContacts(Construct c, short index, IWorldQuery query, float dt)
    {
        var com = c.WorldCom;
        var up = Vector3.UnitY;
        for (var k = 0; k < c.PointCount; k++)
        {
            if (_contactCount >= MaxContacts - 2)
                return;
            var p = c.ToWorld(c.Points[k]);
            var v = c.Vel + Vector3.Cross(c.AngVel, p - com);
            // Down: the ground under this corner.
            const float lift = 0.4f;
            var reach = lift + MathF.Max(0.05f, -v.Y * dt * 2 + 0.05f);
            LastRays++;
            if (query.Raycast(p + up * lift, -up, reach, QueryFilter.All, out var hit))
                Add(index, -1, p, hit.Normal, hit.Distance - lift, 0.7f);
            // Along the motion: walls and kerbs.
            var speed = v.Length();
            if (speed * dt > 0.002f)
            {
                var dir = v / speed;
                if (dir.Y > -0.7f && dir.Y < 0.5f) // (straight down is the first ray's job; rising points never meet a wall that matters)
                {
                    const float back = 0.25f;
                    LastRays++;
                    if (query.Raycast(p - dir * back, dir, back + speed * dt + 0.05f, QueryFilter.All, out var hit2) && hit2.Distance > 1e-3f && Vector3.Dot(hit2.Normal, dir) < -0.1f) // (a ray starting inside something tells nothing)
                        Add(index, -1, p, hit2.Normal, (hit2.Distance - back) * MathF.Max(0.1f, -Vector3.Dot(hit2.Normal, dir)), 0.6f);
                }
            }
        }
    }

    private static void Add(short a, short b, Vector3 p, Vector3 n, float gap, float friction)
    {
        ref var ct = ref _contacts[_contactCount++];
        ct = default;
        ct.A = a;
        ct.B = b;
        ct.Point = p;
        ct.Normal = n;
        ct.Gap = gap;
        ct.Friction = friction;
    }

    /// <summary>Hull points of each moving body against the part boxes of every body near it.</summary>
    private static void BodyContacts(ConstructWorld world, float dt)
    {
        var bodies = world.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var a = bodies[i];
            if (a.Anchored || a.Asleep)
                continue;
            for (var j = 0; j < bodies.Count; j++)
            {
                if (i == j)
                    continue;
                var b = bodies[j];
                var reach = a.Radius + b.Radius;
                if (Vector3.DistanceSquared(a.WorldCom, b.WorldCom) > reach * reach)
                    continue;
                if (b.Asleep && !b.Anchored)
                    b.Wake();
                PointsAgainst(a, (short)i, b, (short)j);
            }
        }
    }

    private static void PointsAgainst(Construct a, short ia, Construct b, short ib)
    {
        var bc = b.WorldCom;
        var r2 = b.Radius * b.Radius;
        for (var k = 0; k < a.PointCount; k++)
        {
            if (_contactCount >= MaxContacts - 1)
                return;
            var p = a.ToWorld(a.Points[k]);
            if (Vector3.DistanceSquared(p, bc) > r2)
                continue;
            var bestPen = float.MaxValue;
            var bestN = Vector3.Zero;
            for (var pi = 0; pi < b.PartCount; pi++)
            {
                ref var part = ref b.Parts[pi];
                b.WorldFrame(part.Seg, out var q, out var t);
                var local = Vector3.Transform(p - t, Quaternion.Conjugate(q));
                var boxes = part.D.SolidBoxes;
                for (var bi = 0; bi < boxes; bi++)
                {
                    b.BoxOf(pi, bi, out var centre, out var half);
                    var d = local - centre;
                    var o = half + new Vector3(0.02f) - Vector3.Abs(d);
                    if (o.X <= 0 || o.Y <= 0 || o.Z <= 0)
                        continue;
                    // The shallowest way out.
                    Vector3 nLocal;
                    float pen;
                    if (o.X < o.Y && o.X < o.Z)
                    {
                        pen = o.X;
                        nLocal = new Vector3(MathF.Sign(d.X) is var s && s == 0 ? 1 : s, 0, 0);
                    }
                    else if (o.Y < o.Z)
                    {
                        pen = o.Y;
                        nLocal = new Vector3(0, MathF.Sign(d.Y) is var s && s == 0 ? 1 : s, 0);
                    }
                    else
                    {
                        pen = o.Z;
                        nLocal = new Vector3(0, 0, MathF.Sign(d.Z) is var s && s == 0 ? 1 : s);
                    }
                    if (pen < bestPen)
                    {
                        bestPen = pen;
                        bestN = Vector3.Transform(nLocal, q);
                    }
                }
            }
            if (bestPen < float.MaxValue)
                Add(ia, b.Anchored ? (short)-1 : ib, p, bestN, -(bestPen - 0.02f), 0.6f);
        }
    }

    private static void Solve(ConstructWorld world, float dt)
    {
        var bodies = world.Bodies;
        // Prepare.
        for (var k = 0; k < _contactCount; k++)
        {
            ref var ct = ref _contacts[k];
            var a = bodies[ct.A];
            var b = ct.B >= 0 ? bodies[ct.B] : null;
            var ra = ct.Point - a.WorldCom;
            var rb = b != null ? ct.Point - b.WorldCom : Vector3.Zero;
            ct.MassN = 1f / MathF.Max(1e-6f, Eff(a, _invI[ct.A], ra, ct.Normal) + (b != null ? Eff(b, _invI[ct.B], rb, ct.Normal) : 0));
            var t1 = MathF.Abs(ct.Normal.X) < 0.9f ? Vector3.Cross(ct.Normal, Vector3.UnitX) : Vector3.Cross(ct.Normal, Vector3.UnitZ);
            t1 = Vector3.Normalize(t1);
            var t2 = Vector3.Cross(ct.Normal, t1);
            ct.T1 = t1;
            ct.T2 = t2;
            ct.MassT1 = 1f / MathF.Max(1e-6f, Eff(a, _invI[ct.A], ra, t1) + (b != null ? Eff(b, _invI[ct.B], rb, t1) : 0));
            ct.MassT2 = 1f / MathF.Max(1e-6f, Eff(a, _invI[ct.A], ra, t2) + (b != null ? Eff(b, _invI[ct.B], rb, t2) : 0));
            // Speculative: approaching is fine up to the gap; overlap is pushed out a share per tick.
            ct.Bias = ct.Gap > 0 ? ct.Gap / dt : MathF.Max(-2.5f, Beta * (ct.Gap + Slop) / dt);
        }
        for (var it = 0; it < Iterations; it++)
        {
            for (var k = 0; k < _contactCount; k++)
            {
                ref var ct = ref _contacts[k];
                var a = bodies[ct.A];
                var b = ct.B >= 0 ? bodies[ct.B] : null;
                var ra = ct.Point - a.WorldCom;
                var rb = b != null ? ct.Point - b.WorldCom : Vector3.Zero;
                var rel = a.Vel + Vector3.Cross(a.AngVel, ra) - (b != null ? b.Vel + Vector3.Cross(b.AngVel, rb) : Vector3.Zero);
                var vn = Vector3.Dot(rel, ct.Normal);
                var dj = -(vn + ct.Bias) * ct.MassN;
                var old = ct.Jn;
                ct.Jn = MathF.Max(0, old + dj);
                dj = ct.Jn - old;
                if (dj != 0)
                    Push(a, _invI[ct.A], ra, b, ct.B >= 0 ? _invI[ct.B] : default, rb, ct.Normal * dj);
                if (ct.Jn <= 0)
                    continue;
                // Friction (two tangents, inside the cone).
                rel = a.Vel + Vector3.Cross(a.AngVel, ra) - (b != null ? b.Vel + Vector3.Cross(b.AngVel, rb) : Vector3.Zero);
                var max = ct.Friction * ct.Jn;
                var d1 = -Vector3.Dot(rel, ct.T1) * ct.MassT1;
                var o1 = ct.Jt1;
                ct.Jt1 = Math.Clamp(o1 + d1, -max, max);
                var d2 = -Vector3.Dot(rel, ct.T2) * ct.MassT2;
                var o2 = ct.Jt2;
                ct.Jt2 = Math.Clamp(o2 + d2, -max, max);
                var jt = ct.T1 * (ct.Jt1 - o1) + ct.T2 * (ct.Jt2 - o2);
                if (jt != Vector3.Zero)
                    Push(a, _invI[ct.A], ra, b, ct.B >= 0 ? _invI[ct.B] : default, rb, jt);
            }
        }
    }

    private static float Eff(Construct c, in Matrix4x4 invI, Vector3 r, Vector3 n)
    {
        if (c.Anchored || c.Asleep)
            return 0;
        var rn = Vector3.Cross(r, n);
        return c.InvMass + Vector3.Dot(rn, Construct.Mul(invI, rn));
    }

    private static void Push(Construct a, in Matrix4x4 ia, Vector3 ra, Construct? b, in Matrix4x4 ib, Vector3 rb, Vector3 j)
    {
        if (!a.Anchored && !a.Asleep)
        {
            a.Vel += j * a.InvMass;
            a.AngVel += Construct.Mul(ia, Vector3.Cross(ra, j));
        }
        if (b != null && !b.Anchored && !b.Asleep)
        {
            b.Vel -= j * b.InvMass;
            b.AngVel -= Construct.Mul(ib, Vector3.Cross(rb, j));
        }
    }

    // ---- Integration, joints, sleep -------------------------------------------------------------------------------

    private static void Integrate(Construct c, float dt)
    {
        // Gentle damping (air, the axles' losses) keeps a still body from creeping.
        c.Vel *= 0.999f;
        c.AngVel *= 0.985f;
        var v2 = c.Vel.LengthSquared();
        if (v2 > MaxSpeed * MaxSpeed)
            c.Vel *= MaxSpeed / MathF.Sqrt(v2);
        var w2 = c.AngVel.LengthSquared();
        if (w2 > MaxSpin * MaxSpin)
            c.AngVel *= MaxSpin / MathF.Sqrt(w2);
        var com = c.WorldCom + c.Vel * dt;
        var w = c.AngVel;
        var spin = new Quaternion(w.X, w.Y, w.Z, 0) * c.Rot;
        var rot = new Quaternion(c.Rot.X + 0.5f * dt * spin.X, c.Rot.Y + 0.5f * dt * spin.Y, c.Rot.Z + 0.5f * dt * spin.Z, c.Rot.W + 0.5f * dt * spin.W);
        c.Rot = Quaternion.Normalize(rot);
        c.Pos = com - Vector3.Transform(c.Com, c.Rot);
        if (!float.IsFinite(c.Pos.X + c.Pos.Y + c.Pos.Z))
        {
            // Never let a bad step poison the world: put it back still.
            c.Pos = c.PrevPos;
            c.Rot = c.PrevRot;
            c.Vel = c.AngVel = Vector3.Zero;
        }
    }

    /// <summary>Passive hinges: damped, swinging towards the latch, held inside their limits.</summary>
    private static void StepJoints(Construct c, float dt)
    {
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            var target = jt.Open ? (jt.Max >= -jt.Min ? jt.Max : jt.Min) * 0.95f : 0f;
            jt.Vel += (target - jt.Angle) * 30f * dt;
            jt.Vel *= MathF.Max(0, 1 - 7f * dt);
            jt.Angle += jt.Vel * dt;
            if (jt.Angle < jt.Min)
            {
                jt.Angle = jt.Min;
                jt.Vel = MathF.Max(0, jt.Vel);
            }
            else if (jt.Angle > jt.Max)
            {
                jt.Angle = jt.Max;
                jt.Vel = MathF.Min(0, jt.Vel);
            }
        }
    }

    private static void Sleep(Construct c)
    {
        var still = c.Vel.LengthSquared() < 0.02f && c.AngVel.LengthSquared() < 0.02f && (c.Driver == 255 || (c.Throttle == 0 && c.SteerInput == 0));
        for (var j = 0; j < c.JointCount && still; j++)
            still = MathF.Abs(c.Joints[j].Vel) < 0.05f;
        if (!still)
        {
            c.SleepTicks = 0;
            return;
        }
        if (++c.SleepTicks >= SleepAfterTicks)
        {
            c.Asleep = true;
            c.Vel = c.AngVel = Vector3.Zero;
        }
    }
}
