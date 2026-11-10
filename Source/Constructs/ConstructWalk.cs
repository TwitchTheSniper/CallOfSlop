using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One walker leg on a body (docs/sandbox/GIANTS.md): where its hip is, where its foot rests, and where the foot is now.
/// The foot is a world point: planted on the ground, or in the air between two footholds.
/// </summary>
public struct CLeg
{
    public ushort Part;
    public Vector3 Hip; // body frame: the hip's pivot
    public Vector3 Rest; // body frame: where the foot rests (out from the hip and down at the standing height)
    public float Reach; // hip to foot, every segment straight
    public float Angle; // round the centre of mass in the body's plan (the legs are kept in this order)
    public byte Group; // which step of the gait lifts it
    public bool Swinging;
    public Vector3 Foot, From, To; // world: the foot now, where its step started and where it goes
    public Vector3 Normal; // the ground's normal under the foot
}

public sealed partial class Construct
{
    public const int MaxLegs = 64;
    public CLeg[] Legs = [];
    public int LegCount;
    public byte LegGroups; // steps in a gait cycle (2: a tripod; one a leg: a wave)
    public int WalkLive = -1; // working legs when the gait was last planned (re-planned after a leg goes)
    public byte WalkGroup; // the group in the air, or the one that last stepped
    public bool WalkStepping; // a group is in the air
    public float WalkClock; // 0 to 1 through the step in the air
    public float WalkHeight; // metres the driver has raised (or lowered) the body from its standing height
    public bool Walking; // the gait brain holds the body up (else it is a body like any other: it falls)
    public bool FeetSet; // the feet have been put down once
    public float WalkTop; // m/s the build walks at, at best (its legs, their load)
    public string WalkProblem = ""; // why it doesn't stand (the HUD)
    public long WalkStamp; // when the server last stepped the feet (the renderer poses them itself when it doesn't)
    public long WalkDrawStamp; // the renderer's last pose of them (its own clock)

    /// <summary>
    /// The legs (on every rebuild): every working leg part on the body's own frame, its hip and its resting foot, ordered
    /// round the centre of mass. A leg that was there before keeps its foot where it was.
    /// </summary>
    internal void BuildLegs()
    {
        Span<ushort> was = stackalloc ushort[MaxLegs];
        Span<Vector3> foot = stackalloc Vector3[MaxLegs];
        var old = Math.Min(LegCount, MaxLegs);
        for (var k = 0; k < old; k++)
        {
            was[k] = Legs[k].Part;
            foot[k] = Legs[k].Foot;
        }
        var feetWere = FeetSet;
        LegCount = 0;
        foreach (var i in Specials)
        {
            ref var p = ref Parts[i];
            if (p.D.Role != PartRole.Leg || p.D.Leg is not { } d || p.Seg != 0)
                continue;
            if (LegCount >= MaxLegs)
            {
                Caps.Hit("walker legs a construct", MaxLegs);
                break;
            }
            var hip = p.Pos + Rot24.Apply(p.Rot, d.Hip);
            var outward = new Vector3(hip.X - Com.X, 0, hip.Z - Com.Z);
            outward = outward.LengthSquared() > 1e-4f ? Vector3.Normalize(outward) : Vector3.UnitX;
            var reach = d.Reach;
            if (Legs.Length == LegCount)
                Array.Resize(ref Legs, Math.Min(MaxLegs, Math.Max(6, LegCount * 2)));
            Legs[LegCount++] = new CLeg
            {
                Part = (ushort)i,
                Hip = hip,
                Rest = hip + outward * (d.Spread * reach) - Vector3.UnitY * (d.Stand * reach),
                Reach = reach,
                Angle = MathF.Atan2(outward.Z, outward.X),
                Normal = Vector3.UnitY,
            };
        }
        // Round the centre of mass, so the support polygon is the planted feet in this order.
        for (var k = 1; k < LegCount; k++)
        {
            var leg = Legs[k];
            var j = k - 1;
            while (j >= 0 && Legs[j].Angle > leg.Angle)
            {
                Legs[j + 1] = Legs[j];
                j--;
            }
            Legs[j + 1] = leg;
        }
        var kept = 0;
        for (var k = 0; k < LegCount; k++)
            for (var m = 0; m < old; m++)
                if (was[m] == Legs[k].Part)
                {
                    Legs[k].Foot = Legs[k].From = Legs[k].To = foot[m];
                    kept++;
                    break;
                }
        FeetSet = feetWere && kept == LegCount;
        WalkLive = -1;
        WalkStepping = false;
    }
}

/// <summary>
/// The gait brain (walk assist; docs/sandbox/GIANTS.md): the player never makes a walk cycle. It finds the body's legs,
/// picks a gait from how many work (six or more, an even count: a tripod; else a wave, one leg at a time), finds each
/// foothold by a ray down through the world (TerrainData answers for the ground), lifts a group only while the feet left
/// down hold the centre of mass inside their polygon, and re-plans when a leg goes. The body is moved by a locomotion
/// controller (velocity, height, tilt and turn), not by the legs pushing: the hybrid the design asks for. Too few legs, or
/// more weight than they carry, and the controller lets go: the body falls as real physics.
/// </summary>
public static partial class ConstructPhysics
{
    public const int MinWalkLegs = 3; // fewer working legs and it collapses
    public const float MaxWalkLoad = 1.5f; // the body's mass over what its legs carry: past it, it collapses

    /// <summary>Server, each tick of a free awake body with legs: the feet, then the body.</summary>
    private static void Walk(Construct c, IWorldQuery query, float dt)
    {
        var live = 0;
        var load = 0f;
        var reach = 0f;
        var stepTime = 0f;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var p = ref c.Parts[c.Legs[k].Part];
            if (!p.Working)
                continue;
            live++;
            load += p.D.Leg!.Load;
            reach += c.Legs[k].Reach;
            stepTime += p.D.Leg.StepTime;
        }
        c.WalkStamp = Stopwatch.GetTimestamp();
        if (live != c.WalkLive)
            PlanGait(c, live);
        var ratio = load > 0 ? c.Mass / load : float.PositiveInfinity;
        var over = live >= MinWalkLegs && c.FeetSet && !HeldWithout(c, c.WorldCom, 255); // (every working foot down, and still the centre of mass is outside them)
        if (live < MinWalkLegs || ratio > MaxWalkLoad || over)
        {
            c.WalkProblem = live < MinWalkLegs ? $"{live} working legs: it needs {MinWalkLegs}" : over ? "its legs don't stand round its weight" : "the legs can't carry its weight";
            c.Walking = false;
            c.WalkTop = 0;
            HangFeet(c);
            return;
        }
        reach /= live;
        stepTime /= live;
        var pace = Math.Clamp(1.25f - ratio * 0.5f, 0.25f, 1f); // (lightly loaded legs step quickly; at their load, slower)
        var swing = stepTime / pace;
        c.WalkTop = 0.45f * reach / (c.LegGroups * swing) * MathF.Min(1, live / (float)Math.Max(1, c.LegCount) * 1.5f); // (a lost leg slows it)
        c.WalkProblem = "";
        c.Walking = true;
        WalkFeet(c, query, c.Pos, c.Rot, c.Vel, c.AngVel, swing, dt, true);
        WalkBody(c, reach, dt);
        if (c.WalkStepping)
            c.Wake(); // (a foot in the air finishes its step)
    }

    /// <summary>The gait for <paramref name="live"/> working legs: a tripod (alternate legs round the body) or a wave.</summary>
    private static void PlanGait(Construct c, int live)
    {
        c.WalkLive = live;
        var tripod = live >= 6 && live % 2 == 0;
        var rank = 0;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (!c.Parts[leg.Part].Working)
            {
                leg.Swinging = false;
                continue;
            }
            leg.Group = (byte)(tripod ? rank % 2 : rank);
            rank++;
        }
        c.LegGroups = (byte)Math.Max(1, tripod ? 2 : live);
        if (c.WalkGroup >= c.LegGroups)
            c.WalkGroup = 0;
        if (c.WalkStepping)
        {
            // (a leg went mid-step: what was in the air comes down where it is going)
            for (var k = 0; k < c.LegCount; k++)
                if (c.Legs[k].Swinging)
                {
                    c.Legs[k].Foot = c.Legs[k].To;
                    c.Legs[k].Swinging = false;
                }
            c.WalkStepping = false;
        }
    }

    /// <summary>No walking: each foot hangs at its rest pose under the body (the body falls on what it has).</summary>
    private static void HangFeet(Construct c)
    {
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            leg.Foot = leg.From = leg.To = c.ToWorld(leg.Rest);
            leg.Swinging = false;
        }
        c.WalkStepping = false;
        c.FeetSet = false;
    }

    /// <summary>
    /// The feet for a body at pose <paramref name="pos"/>, <paramref name="rot"/> moving at <paramref name="vel"/> and
    /// <paramref name="angVel"/>: each foot's next foothold (its rest point, led by the motion, dropped onto the ground) and
    /// the group in the air. The server runs it on the body; a client's renderer runs it on the pose it draws, so the feet
    /// need nothing on the wire. Allocates nothing.
    /// </summary>
    public static void WalkFeet(Construct c, IWorldQuery query, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel, float swing, float dt, bool server)
    {
        var com = pos + Vector3.Transform(c.Com, rot);
        var lead = swing * c.LegGroups * 0.5f; // (seconds ahead a foot is put: half a cycle)
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (!c.Parts[leg.Part].Working)
                continue;
            var rest = pos + Vector3.Transform(leg.Rest, rot);
            var v = vel + Vector3.Cross(angVel, rest - com);
            v.Y = 0;
            var aim = rest + v * lead;
            leg.To = Foothold(c, query, aim, leg.Reach, server, out leg.Normal);
            if (!c.FeetSet)
                leg.Foot = leg.From = leg.To;
        }
        c.FeetSet = true;
        if (c.WalkStepping)
        {
            c.WalkClock += dt / swing;
            var s = MathF.Min(1, c.WalkClock);
            var lift = s * (1 - s) * 4; // (0 at both ends, 1 at the middle)
            for (var k = 0; k < c.LegCount; k++)
            {
                ref var leg = ref c.Legs[k];
                if (!leg.Swinging)
                    continue;
                var h = c.Parts[leg.Part].D.Leg!.StepHeight * leg.Reach;
                var rise = MathF.Max(0, leg.To.Y - leg.From.Y); // (over a step up, the foot clears it)
                leg.Foot = Vector3.Lerp(leg.From, leg.To, s * s * (3 - 2 * s)) + Vector3.UnitY * ((h + rise) * lift);
                if (s >= 1)
                {
                    leg.Foot = leg.To;
                    leg.Swinging = false;
                }
            }
            if (s >= 1)
                c.WalkStepping = false;
            return;
        }
        // Down: lift the next group that needs a step, if the feet left down hold the body.
        for (var t = 1; t <= c.LegGroups; t++)
        {
            var g = (byte)((c.WalkGroup + t) % c.LegGroups);
            if (!GroupNeeds(c, pos, rot, g) || !HeldWithout(c, com, g))
                continue;
            c.WalkGroup = g;
            c.WalkStepping = true;
            c.WalkClock = 0;
            for (var k = 0; k < c.LegCount; k++)
            {
                ref var leg = ref c.Legs[k];
                if (leg.Group == g && c.Parts[leg.Part].Working)
                {
                    leg.Swinging = true;
                    leg.From = leg.Foot;
                }
            }
            break;
        }
    }

    /// <summary>Is a foot of group <paramref name="g"/> far enough from its foothold (or its hip) to step?</summary>
    private static bool GroupNeeds(Construct c, Vector3 pos, Quaternion rot, byte g)
    {
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (leg.Group != g || !c.Parts[leg.Part].Working)
                continue;
            var d = leg.To - leg.Foot;
            d.Y *= 0.5f;
            if (d.LengthSquared() > leg.Reach * leg.Reach * 0.0064f) // (8% of its reach)
                return true;
            var hip = pos + Vector3.Transform(leg.Hip, rot);
            if (Vector3.DistanceSquared(hip, leg.Foot) > leg.Reach * leg.Reach * 0.9f) // (over-stretched: it must step)
                return true;
        }
        return false;
    }

    /// <summary>With group <paramref name="g"/> in the air (255: none), do at least three feet stay down round the centre of mass?</summary>
    private static bool HeldWithout(Construct c, Vector3 com, byte g)
    {
        var first = -1;
        var prev = -1;
        var down = 0;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (leg.Group == g || !c.Parts[leg.Part].Working)
                continue;
            down++;
            if (prev >= 0 && !LeftOf(c.Legs[prev].Foot, leg.Foot, com))
                return false;
            if (first < 0)
                first = k;
            prev = k;
        }
        return down >= 3 && LeftOf(c.Legs[prev].Foot, c.Legs[first].Foot, com);
    }

    /// <summary>Is <paramref name="p"/> on the inner side of the edge from <paramref name="a"/> to <paramref name="b"/> (the plan, counter-clockwise by angle)?</summary>
    private static bool LeftOf(Vector3 a, Vector3 b, Vector3 p) => (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X) >= 0;

    /// <summary>The ground under <paramref name="aim"/>: a ray down from a reach above it to a reach below.</summary>
    private static Vector3 Foothold(Construct c, IWorldQuery query, Vector3 aim, float reach, bool server, out Vector3 normal)
    {
        var from = aim + Vector3.UnitY * reach;
        var len = reach * 2.5f;
        bool found;
        RayHit hit;
        if (server)
        {
            _rayFrom = -1;
            found = GroundRay(c, query, from, -Vector3.UnitY, len, out hit, out _, out _);
        }
        else
            found = query.Raycast(from, -Vector3.UnitY, len, QueryFilter.All, out hit);
        if (!found)
        {
            normal = Vector3.UnitY;
            return aim - Vector3.UnitY * reach * 0.5f; // (nothing under it: the foot reaches down)
        }
        normal = hit.Normal.Y > 0.1f ? Vector3.Normalize(hit.Normal) : Vector3.UnitY;
        return from - Vector3.UnitY * hit.Distance;
    }

    /// <summary>
    /// The locomotion controller: the body's speed along the ground from the driver's keys (forward and back, turn; the
    /// roll keys strafe, Sprint and Crouch raise and lower it), its height over its feet, its tilt with the ground under
    /// them, and a shift back over the feet when the centre of mass leaves them. Velocity-level: it sets what the body moves
    /// at, so gravity is held by the legs; contacts still stop it.
    /// </summary>
    private static void WalkBody(Construct c, float reach, float dt)
    {
        var driven = c.Driver != 255;
        var throttle = driven ? c.Throttle : 0;
        var turn = driven ? c.SteerInput : 0;
        var strafe = driven ? c.RollInput : 0;
        var raise = driven ? c.LiftInput : 0;
        var stand = 0f;
        for (var k = 0; k < c.LegCount; k++)
            stand += c.Parts[c.Legs[k].Part].D.Leg!.Stand;
        stand /= Math.Max(1, c.LegCount);
        c.WalkHeight = Math.Clamp(c.WalkHeight + raise * reach * 0.15f * dt, -0.3f * stand * reach, (0.85f - stand) * reach);
        // Height: the hips over their feet.
        var have = 0f;
        var n = 0;
        var normal = Vector3.Zero;
        var com = c.WorldCom;
        var centre = Vector3.Zero;
        var down = 0;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (!c.Parts[leg.Part].Working)
                continue;
            have += c.ToWorld(leg.Hip).Y - (leg.Swinging ? leg.To.Y : leg.Foot.Y);
            n++;
            if (!leg.Swinging)
            {
                normal += leg.Normal;
                centre += leg.Foot;
                down++;
            }
        }
        have /= Math.Max(1, n);
        var want = stand * reach + c.WalkHeight;
        var vy = Math.Clamp((want - have) * 2f, -0.25f * reach, 0.25f * reach);
        // Along the ground: the keys, at what the build walks at.
        var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
        fwd.Y = 0;
        fwd = fwd.LengthSquared() > 1e-4f ? Vector3.Normalize(fwd) : -Vector3.UnitZ;
        var right = Vector3.Cross(fwd, Vector3.UnitY);
        var target = fwd * (throttle * c.WalkTop) + right * (strafe * c.WalkTop * 0.6f);
        if (down >= 3)
        {
            // The centre of mass over the feet that are down: if it is outside them, the body leans back over them.
            centre /= down;
            var inside = true;
            int first = -1, prev = -1;
            for (var k = 0; k < c.LegCount && inside; k++)
            {
                if (c.Legs[k].Swinging || !c.Parts[c.Legs[k].Part].Working)
                    continue;
                if (prev >= 0)
                    inside = LeftOf(c.Legs[prev].Foot, c.Legs[k].Foot, com);
                if (first < 0)
                    first = k;
                prev = k;
            }
            if (inside && prev >= 0)
                inside = LeftOf(c.Legs[prev].Foot, c.Legs[first].Foot, com);
            if (!inside)
            {
                var back = new Vector3(centre.X - com.X, 0, centre.Z - com.Z);
                if (back.LengthSquared() > 1e-4f)
                    target = Vector3.Normalize(back) * MathF.Max(0.5f, c.WalkTop * 0.5f);
            }
        }
        var flat = new Vector3(c.Vel.X, 0, c.Vel.Z);
        var accel = MathF.Max(0.5f, c.WalkTop) * dt; // (up to speed in about a second)
        var dv = target - flat;
        if (dv.Length() > accel)
            dv = Vector3.Normalize(dv) * accel;
        flat += dv;
        c.Vel = new Vector3(flat.X, c.Vel.Y + (vy - c.Vel.Y) * MathF.Min(1, 6 * dt), flat.Z);
        // Tilt with the ground (half of it), and the turn.
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        var ground = down > 0 && normal.LengthSquared() > 1e-4f ? Vector3.Normalize(normal) : Vector3.UnitY;
        var level = Vector3.Normalize(Vector3.UnitY + (ground - Vector3.UnitY) * 0.5f);
        var radius = MathF.Max(1, c.Radius);
        var yaw = -turn * c.WalkTop / radius * 0.8f;
        var turnAccel = MathF.Max(0.2f, c.WalkTop / radius) * 2 * dt;
        var yawNow = c.AngVel.Y;
        var yawNext = yawNow + Math.Clamp(yaw - yawNow, -turnAccel, turnAccel);
        var tiltNow = c.AngVel - Vector3.UnitY * yawNow;
        var tiltNext = Vector3.Lerp(tiltNow, Vector3.Cross(up, level) * 2.5f, MathF.Min(1, 6 * dt));
        c.AngVel = tiltNext + Vector3.UnitY * yawNext;
    }
}
