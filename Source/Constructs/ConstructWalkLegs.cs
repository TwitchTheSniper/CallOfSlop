using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A walker leg's shape in the world (docs/sandbox/GIANTS.md, phase 2): the analytic IK both the renderer and the server
/// pose a leg by, the segments' boxes that rays hit (a shot at a drawn segment hits that segment, and the leg part takes the
/// damage), and the foot pads that players and loose bodies touch. Nothing allocates.
/// </summary>
public static partial class ConstructPhysics
{
    private static readonly Vector3[] RayLegPts = new Vector3[LegDef.MaxSegments + 1];

    /// <summary>Segment <paramref name="s"/>'s side (they taper towards the foot).</summary>
    public static float LegWidth(LegDef d, int s) => d.Width * (1 - 0.14f * s);

    /// <summary>
    /// Leg <paramref name="k"/> of a body at <paramref name="pos"/>, <paramref name="rot"/>: its points, hip to foot, into
    /// <paramref name="pts"/> (at least <see cref="LegDef.MaxSegments"/> + 1 long); returns its segment count.
    /// </summary>
    public static int LegPose(Construct c, int k, Vector3 pos, Quaternion rot, Vector3[] pts, out Vector3 outward)
    {
        ref var leg = ref c.Legs[k];
        var d = c.Parts[leg.Part].D.Leg!;
        var up = Vector3.Transform(Vector3.UnitY, rot);
        var fwd = Vector3.Transform(c.SeatForward(), rot);
        var hip = pos + Vector3.Transform(leg.Hip, rot);
        outward = Vector3.Transform(leg.Rest - leg.Hip, rot);
        outward -= up * Vector3.Dot(outward, up);
        outward = outward.LengthSquared() > 1e-6f ? Vector3.Normalize(outward) : Vector3.UnitX;
        var foot = c.FeetSet ? leg.Foot : pos + Vector3.Transform(leg.Rest, rot);
        return Pose(d, hip, foot, up, fwd, outward, pts);
    }

    /// <summary>The leg's points, hip to foot, into <paramref name="pts"/>; returns its segment count.</summary>
    public static int Pose(LegDef d, Vector3 hip, Vector3 foot, Vector3 up, Vector3 fwd, Vector3 outward, Vector3[] pts)
    {
        var segs = d.Segments;
        var n = segs.Length;
        var i = 0;
        pts[0] = hip;
        var root = hip;
        if (d.Kind == LegKind.Insect && n >= 3)
        {
            // The coxa: out from the hip towards the foot, level.
            var across = foot - hip;
            across -= up * Vector3.Dot(across, up);
            var dir = across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : outward;
            root = hip + dir * MathF.Min(segs[0], across.Length() * 0.8f);
            pts[1] = root;
            i = 1;
        }
        var end = foot;
        if (n - i == 3)
        {
            // The last segment: straight down to the foot, or swept back on its toes.
            var last = segs[n - 1];
            var lean = d.Kind == LegKind.Digitigrade ? Vector3.Normalize(up * 0.75f - fwd * 0.66f) : up;
            end = foot + lean * last;
        }
        var pole = d.Kind == LegKind.Insect ? Vector3.Normalize(up + outward * 0.3f) : fwd;
        pts[i + 1] = Knee(root, end, segs[i], segs[i + 1], pole);
        pts[i + 2] = end;
        if (n - i == 3)
            pts[i + 3] = foot;
        return n;
    }

    /// <summary>Two bones of <paramref name="a"/> and <paramref name="b"/> from <paramref name="root"/> towards <paramref name="target"/>, bending towards <paramref name="pole"/>.</summary>
    private static Vector3 Knee(Vector3 root, Vector3 target, float a, float b, Vector3 pole)
    {
        var to = target - root;
        var d = to.Length();
        var dir = d > 1e-5f ? to / d : -Vector3.UnitY;
        d = Math.Clamp(d, MathF.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        var cos = Math.Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1);
        var side = pole - dir * Vector3.Dot(pole, dir);
        side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitX + Vector3.UnitZ * 0.3f));
        return root + dir * (a * cos) + side * (a * MathF.Sqrt(1 - cos * cos));
    }

    /// <summary>
    /// A segment's frame from <paramref name="a"/> to <paramref name="b"/>: its length runs along the frame's z, its side
    /// along <paramref name="across"/> (x). The renderer draws a bone in it and rays hit the same box.
    /// </summary>
    public static Quaternion BoneFrame(Vector3 a, Vector3 b, Vector3 across)
    {
        var z = b - a;
        z = z.LengthSquared() > 1e-8f ? Vector3.Normalize(z) : -Vector3.UnitY;
        var x = across - z * Vector3.Dot(across, z);
        x = x.LengthSquared() > 1e-6f ? Vector3.Normalize(x) : Vector3.Normalize(Vector3.Cross(z, Vector3.UnitY + Vector3.UnitX * 0.1f));
        var y = Vector3.Cross(z, x);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
    }

    /// <summary>
    /// The nearest leg segment or foot pad of <paramref name="c"/> along a ray, nearer than <paramref name="best"/> (which it
    /// shortens). Returns the leg part hit, or a part riding one of its segments (-1: none), and which segment (the last
    /// segment for its foot pad; -1 for a rider).
    /// </summary>
    public static int LegRay(Construct c, Vector3 origin, Vector3 dir, ref float best, out int segment, out Vector3 normal)
    {
        segment = -1;
        normal = -dir;
        var part = -1;
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var p = ref c.Parts[c.Legs[k].Part];
            if (p.D.Leg is not { } d)
                continue;
            var n = LegPose(c, k, c.Pos, c.Rot, RayLegPts, out var outward);
            for (var s = 0; s <= n; s++)
            {
                Quaternion q;
                Vector3 centre, half;
                if (s < n)
                {
                    var a = RayLegPts[s];
                    var b = RayLegPts[s + 1];
                    var w = LegWidth(d, s);
                    q = BoneFrame(a, b, outward);
                    centre = (a + b) * 0.5f;
                    half = new Vector3(w * 0.5f, w * 0.46f, Vector3.Distance(a, b) * 0.5f);
                }
                else
                {
                    q = c.Rot;
                    centre = RayLegPts[n] + up * (d.Foot * 0.175f);
                    half = new Vector3(d.Foot, d.Foot * 0.175f, d.Foot);
                }
                var iq = Quaternion.Conjugate(q);
                var lo = Vector3.Transform(origin - centre, iq);
                var ld = Vector3.Transform(dir, iq);
                if (Geometry.RayAabb(-half, half, lo, ld, best) is { } hit && hit < best)
                {
                    best = hit;
                    part = c.Legs[k].Part;
                    segment = Math.Min(s, n - 1);
                    normal = Vector3.Transform(Geometry.BoxEntryNormal(-half, half, lo, ld, hit), q);
                }
            }
            if (c.RiderCount > 0 && RiderRay(c, k, origin, dir, ref best, out var rn) is var rider and >= 0)
            {
                part = rider; // (a part welded to a segment: ConstructWalkRiders.cs)
                segment = -1;
                normal = rn;
            }
        }
        return part;
    }

    /// <summary>
    /// A leg's share of its strength left: a damaged leg part (its segments are hit as the part) carries less and steps
    /// slower, down to a quarter just before it breaks.
    /// </summary>
    public static float LegSound(in CPart p) => p.D.Health > 0 ? Math.Clamp(0.25f + 0.75f * p.Health / p.D.Health, 0.25f, 1f) : 1f;

    /// <summary>The foot pad of leg <paramref name="k"/> as a world box (centre, half sizes; the body's turn ignored: pads are square and low).</summary>
    public static void FootPad(Construct c, int k, out Vector3 centre, out Vector3 half)
    {
        var d = c.Parts[c.Legs[k].Part].D.Leg!;
        half = new Vector3(d.Foot, d.Foot * 0.175f, d.Foot);
        centre = (c.FeetSet ? c.Legs[k].Foot : c.ToWorld(c.Legs[k].Rest)) + Vector3.UnitY * half.Y;
    }

    /// <summary>
    /// The feet against a player (ConstructWorld.BlockPlayer): a pad met from above is stood on, else the player is pushed
    /// out of it sideways. True when the player stands on a pad.
    /// </summary>
    public static bool FeetBlockPlayer(Construct c, ref MoveState s, float height)
    {
        var stood = false;
        var r = World.PlayerRadius;
        for (var k = 0; k < c.LegCount; k++)
        {
            if (!c.Parts[c.Legs[k].Part].Working)
                continue;
            FootPad(c, k, out var centre, out var half);
            var feet = s.Position;
            var top = centre.Y + half.Y;
            if (feet.Y > top + 0.06f || feet.Y + height < centre.Y - half.Y)
                continue;
            var dx = feet.X - centre.X;
            var dz = feet.Z - centre.Z;
            var ox = half.X + r - MathF.Abs(dx);
            var oz = half.Z + r - MathF.Abs(dz);
            if (ox <= 0 || oz <= 0)
                continue;
            if (feet.Y >= top - World.StepHeight && s.Velocity.Y <= 0.5f && MathF.Abs(dx) <= half.X && MathF.Abs(dz) <= half.Z)
            {
                s.Position.Y = top; // (on the pad)
                if (s.Velocity.Y < 0)
                    s.Velocity.Y = 0;
                s.Grounded = true;
                stood = true;
                continue;
            }
            // Out of the side it is nearest.
            if (ox < oz)
            {
                s.Position.X += dx >= 0 ? ox : -ox;
                if (s.Velocity.X * dx < 0)
                    s.Velocity.X = 0;
            }
            else
            {
                s.Position.Z += dz >= 0 ? oz : -oz;
                if (s.Velocity.Z * dz < 0)
                    s.Velocity.Z = 0;
            }
        }
        return stood;
    }

    /// <summary>
    /// Server: the feet against loose bodies. A body much lighter than the walker that a working foot pad overlaps is pushed
    /// out of it (up and away from the pad's centre) and moves with the foot; heavier ones are left to the walker's own contacts.
    /// </summary>
    private static void FeetPushBodies(ConstructWorld world, Construct c)
    {
        for (var k = 0; k < c.LegCount; k++)
        {
            if (!c.Parts[c.Legs[k].Part].Working)
                continue;
            FootPad(c, k, out var centre, out var half);
            var near = world.Near(centre - half, centre + half, _feetNear);
            for (var b = 0; b < near.Count; b++)
            {
                var o = near[b];
                if (o == c || o.Anchored || o.Frozen || o.CarriedBy != 0 || o.Mass > c.Mass * 0.25f || !PadMeets(world, o, centre, half))
                    continue;
                var away = o.WorldCom - centre;
                away.Y = 0;
                away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.UnitX;
                var foot = c.Legs[k].Swinging ? (c.Legs[k].To - c.Legs[k].From) / MathF.Max(0.2f, c.Parts[c.Legs[k].Part].D.Leg!.StepTime) : Vector3.Zero;
                var push = 2f + MathF.Max(0, Vector3.Dot(foot, away));
                var vel = o.Vel;
                if (Vector3.Dot(vel, away) < push)
                    vel += away * (push - Vector3.Dot(vel, away));
                if (vel.Y < 1f)
                    vel.Y = 1f;
                o.Vel = vel;
                o.Wake();
            }
        }
    }

    private static readonly List<Construct> _feetNear = new(64);

    /// <summary>Does a world box meet any solid part of <paramref name="o"/>?</summary>
    private static bool PadMeets(ConstructWorld world, Construct o, Vector3 centre, Vector3 half)
    {
        var reach = o.Radius + half.Length();
        if (Vector3.DistanceSquared(o.WorldCom, centre) > reach * reach)
            return false;
        var local = o.ToLocal(centre);
        var pad = new Vector3(half.Length());
        return world.AnyPartIn(o, local - pad, local + pad);
    }
}

/// <summary>
/// Leg power (docs/sandbox/GIANTS.md, phase 2): a leg works only when it is fed, by a hose from a running pump (straight, or
/// through a valve), by the electric circuit (a working battery's links reach it), or by a drive shaft the engine turns.
/// Its strength is the best of what feeds it, a share of its own from the part's <c>power</c> block: what it carries and
/// how fast it steps. An unfed leg is locked: it carries nothing, takes no step and hangs at its rest pose.
/// </summary>
public static partial class ConstructPhysics
{
    /// <summary>
    /// Leg part <paramref name="part"/>'s strength from what feeds it (0: nothing). <paramref name="live"/> (the server, each
    /// tick) asks for a pump that is pumping and a shaft that is turning; otherwise (a client, the checks) only that they
    /// are fitted, as their links stand.
    /// </summary>
    public static float LegPower(ConstructWorld? world, Construct c, int part, bool live)
    {
        ref var p = ref c.Parts[part];
        if (p.Broken || p.D.Leg is not { } d)
            return 0;
        var best = 0f;
        if (d.Electric > 0 && c.HasBattery && p.Powered && (!live || c.PowerOn || world is { CreativeRecycling: true }))
            best = d.Electric;
        if (d.Hydraulic > best && OilFed(c, part, live))
            best = d.Hydraulic;
        if (d.Shaft > best && d.Shaft > 0)
        {
            var state = ConstructLinear.ShaftOf(c, part, out _, out _);
            if (state == ShaftState.Turning || !live && state is ShaftState.EngineOff or ShaftState.ClutchOut)
                best = d.Shaft * (live ? Math.Clamp(c.ShaftShare, 0.25f, 1f) : 1f);
        }
        return best;
    }

    /// <summary>Does a hose reach the leg's oil port from a pump, or from a valve on a pump (pumping, when <paramref name="live"/>)?</summary>
    private static bool OilFed(Construct c, int part, bool live)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type != RouteType.Pipe || r.To != part || !r.FedEnd || r.Source >= c.PartCount)
                continue;
            int src = r.Source;
            if (c.Parts[src].D.Hyd == HydKind.Valve)
            {
                if (c.Parts[src].Broken)
                    continue;
                for (var v = 0; v < c.HydValveCount; v++)
                {
                    ref var hv = ref c.HydValves![v];
                    if (hv.Part == src && hv.Pump >= 0 && hv.Route >= 0 && c.Routes[hv.Route].FedEnd && PumpLive(c, hv.Pump, live))
                        return true;
                }
                continue;
            }
            for (var u = 0; u < c.HydPumpCount; u++)
                if (c.HydPumps![u].Part == src && PumpLive(c, u, live))
                    return true;
        }
        return false;
    }

    private static bool PumpLive(Construct c, int pump, bool live) => !c.Parts[c.HydPumps![pump].Part].Broken && (!live || c.HydPumps[pump].Flow > 0);

    /// <summary>Sets each leg's strength (<see cref="CLeg.Power"/>); returns how many working legs are fed.</summary>
    public static int PowerLegs(ConstructWorld? world, Construct c, bool live)
    {
        var fed = 0;
        for (var k = 0; k < c.LegCount; k++)
        {
            c.Legs[k].Power = LegPower(world, c, c.Legs[k].Part, live);
            if (c.Legs[k].Power > 0)
                fed++;
        }
        return fed;
    }

    /// <summary>Does leg <paramref name="k"/> walk (its part works and something feeds it)?</summary>
    public static bool LegLive(Construct c, int k) => c.Parts[c.Legs[k].Part].Working && c.Legs[k].Power > 0;

    public const string NoLegPower = "no power to the legs: a hose from a pump, a battery's circuit or a drive shaft";

    /// <summary>What stops a walker, for the HUD and the checks, the same on every peer: unfed legs (from their links), else what the server said.</summary>
    public static string WalkerProblem(Construct c)
    {
        var fed = 0;
        for (var k = 0; k < c.LegCount; k++)
            if (LegPower(null, c, c.Legs[k].Part, false) > 0)
                fed++;
        return fed == 0 ? NoLegPower : fed < MinWalkLegs ? $"{fed} legs have power: it needs {MinWalkLegs}" : c.WalkProblem;
    }
}
