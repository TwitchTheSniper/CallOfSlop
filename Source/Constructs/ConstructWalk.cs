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
    public float Power; // its strength from what feeds it (0: unfed, locked; ConstructWalkLegs.cs), a share of its own
    public float Carry; // kg of parts riding its segments (ConstructWalkRiders.cs): it swings them, so it steps slower
    public byte Feed; // what feeds it (ConstructPhysics.FeedElectric, FeedOil, FeedShaft; 0: nothing)
    public short Pump; // the pump whose oil it takes (FeedOil)
    public bool Hold; // its next foothold has ground under it (within reach, not too steep); else it doesn't step
    public bool Grounded; // its foot is on ground (or stepping onto it); a foot hanging over nothing carries nothing
    // The wire (protocol 229, ConstructNet.WriteWalk): the server's foot as last noted and the walk tick it last moved at (a
    // foot that moved lately goes in every pose; a planted one only in its turn); a client's foot as last heard, its flags
    // (ConstructNet.FootSwinging and the rest) and whether it has been heard at all.
    public Vector3 NetFoot;
    public uint NetMoved;
    public byte NetFlags;
    public bool NetHeard;
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
    public byte WalkWait = 255; // the group that needs a step but can't lift yet (the body shifts over the other feet first; 255: none)
    public float WalkClock; // 0 to 1 through the step in the air
    public float WalkHeight; // metres the driver has raised (or lowered) the body from its standing height
    public bool Walking; // the gait brain holds the body up (else it is a body like any other: it falls)
    public bool FeetSet; // the feet have been put down once
    public float WalkTop; // m/s the build walks at, at best (its legs, their load)
    public string WalkProblem = ""; // why it doesn't stand (the HUD)
    public uint WalkTicks; // server: ticks the gait brain has run (the feet's NetMoved is on this clock)
    internal int PoseFeetAt; // server: where the next pose's feet in turn start (ConstructNet.WriteWalk)
    public bool NetFeetSet; // client: the server has put the feet down (its FeetSet), as last heard
    public byte NetProblem, NetProblemArg; // client: the walk problem as last heard (its string is made only when it changes)
    public Vector3[] NetHistFeet = []; // client: each pose's feet (NetHistMax rows of LegCount), drawn at the body's tick (ConstructNet.SampleFeet)
    public byte[] NetHistFootFlags = [];
    public float LegLoad; // N m at the crank that shaft-fed legs take (server; ConstructWalkLegs.cs, LegWork)
    public float LegWatts; // W the legs did last tick (server; the HUD and the checks)
    public float WalkPowerLift = float.PositiveInfinity; // m/s of lifting the body the circuit can feed the legs at (server; ConstructWalkLegs.cs, PowerLift)

    /// <summary>
    /// The legs (on every rebuild): every working leg part on the body's own frame, its hip and its resting foot, ordered
    /// round the centre of mass. A leg that was there before keeps its foot where it was.
    /// </summary>
    internal void BuildLegs()
    {
        Span<ushort> was = stackalloc ushort[MaxLegs];
        Span<Vector3> foot = stackalloc Vector3[MaxLegs];
        Span<bool> grounded = stackalloc bool[MaxLegs];
        Span<CLeg> net = stackalloc CLeg[MaxLegs]; // (the wire's state of each foot: kept with its leg)
        var old = Math.Min(LegCount, MaxLegs);
        for (var k = 0; k < old; k++)
        {
            net[k] = Legs[k];
            was[k] = Legs[k].Part;
            foot[k] = Legs[k].Foot;
            grounded[k] = Legs[k].Grounded;
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
                Carry = SegRiderMass(i),
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
                    Legs[k].Grounded = Legs[k].Hold = grounded[m];
                    Legs[k].NetFoot = net[m].NetFoot;
                    Legs[k].NetMoved = net[m].NetMoved;
                    Legs[k].NetFlags = net[m].NetFlags;
                    Legs[k].NetHeard = net[m].NetHeard;
                    kept++;
                    break;
                }
        FeetSet = feetWere && kept == LegCount;
        WalkLive = -1;
        WalkStepping = false;
        if (LegCount > 0)
            BuildSteady(); // (the stabilisers: ConstructWalkFeel.cs)
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
    public const float MaxStretch = 0.94f; // a planted leg's hip to foot, a share of its reach: the body rises no higher

    // What stops a walker (the HUD; on the wire by number: ConstructNet.WriteWalk).
    public const string ProblemNotRound = "its legs don't stand round its weight", ProblemTooHeavy = "the legs can't carry its weight",
        ProblemNoGround = "no ground under its feet", ProblemNoFooting = "no footing ahead: the legs find no ground in reach";

    private static readonly string[] _problemLegs = new string[MinWalkLegs];

    /// <summary>"N working legs with power": made once for each N below the least (nothing allocates per tick).</summary>
    public static string ProblemLegs(int live)
    {
        var n = Math.Clamp(live, 0, MinWalkLegs - 1);
        return _problemLegs[n] ??= $"{n} working legs with power: it needs {MinWalkLegs}";
    }

    /// <summary>Server, each tick of a free awake body with legs: the feet, then the body.</summary>
    private static void Walk(ConstructWorld world, Construct c, IWorldQuery query, float dt)
    {
        var fed = PowerLegs(world, c, true);
        c.LegLoad = 0;
        c.LegWatts = 0;
        WalkTune(c); // (the gait profile: ConstructWalkProfile.cs)
        var live = 0;
        var load = 0f;
        var reach = 0f;
        var stepTime = 0f;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var p = ref c.Parts[c.Legs[k].Part];
            if (!LegLive(c, k))
                continue;
            live++;
            var sound = LegSound(p) * MathF.Min(1.5f, c.Legs[k].Power); // (a damaged leg carries less and steps slower; so does a weaker feed)
            load += p.D.Leg!.Load * sound;
            reach += c.Legs[k].Reach;
            stepTime += p.D.Leg.StepTime / sound * CarrySlows(c.Legs[k].Carry, p.D.Leg.Load) / LegPace(c, k);
        }
        c.WalkTicks++;
        if (live != c.WalkLive)
            PlanGait(c, live);
        var ratio = load > 0 ? c.Mass / load : float.PositiveInfinity;
        var over = live >= MinWalkLegs && c.FeetSet && !HeldWithout(c, c.WorldCom, 255); // (every working foot down, and still the centre of mass is outside them)
        if (live < MinWalkLegs || ratio > MaxWalkLoad || over)
        {
            c.WalkProblem = fed == 0 ? NoLegPower : live < MinWalkLegs ? ProblemLegs(live) : over ? ProblemNotRound : ProblemTooHeavy;
            c.Walking = false;
            c.WalkTop = 0;
            HangFeet(c);
            return;
        }
        reach /= live;
        stepTime /= live;
        var pace = Math.Clamp(1.25f - ratio * 0.5f, 0.25f, 1f); // (lightly loaded legs step quickly; at their load, slower)
        var swing = stepTime / pace;
        c.WalkTop = 0.45f * c.WalkStride * reach / (c.LegGroups * swing) * MathF.Min(1, live / (float)Math.Max(1, c.LegCount) * 1.5f); // (a lost leg slows it)
        c.WalkPowerLift = PowerLift(c); // (what the plant and the bank can feed: ConstructWalkLegs.cs)
        c.WalkTop = MathF.Min(c.WalkTop, c.WalkPowerLift / LegCarryCost);
        c.WalkProblem = "";
        c.Walking = true;
        c.WalkLanded = false;
        WalkFeet(c, query, c.Pos, c.Rot, c.Vel, c.AngVel, swing, dt, true);
        if (!HeldWithout(c, c.WorldCom, 255))
        {
            // (the feet as found this tick don't stand round it: none on ground, or it is already over the edge; it falls as physics)
            c.WalkProblem = Grounded(c) < MinWalkLegs ? ProblemNoGround : ProblemNotRound;
            c.Walking = false;
            c.WalkTop = 0;
            HangFeet(c);
            return;
        }
        if (c.WalkLanded)
            Thump(world, c); // (the ride feel: ConstructWalkFeel.cs)
        WalkBody(c, reach, dt);
        LegWork(c);
        FeetPushBodies(world, c);
        if (c.WalkStepping)
            c.Wake(); // (a foot in the air finishes its step)
    }

    /// <summary>A leg swinging what rides its segments steps slower: a tenth of its load riding it, half as slow again (at most twice).</summary>
    public static float CarrySlows(float carry, float load) => Math.Clamp(1 + 0.5f * carry / MathF.Max(1, 0.1f * load), 1, 2);

    /// <summary>The gait for <paramref name="live"/> working legs: a tripod (alternate legs round the body) or a wave.</summary>
    internal static void PlanGait(Construct c, int live)
    {
        c.WalkLive = live;
        var tripod = c.WalkGaitPick switch
        {
            GaitTripod => live >= 4, // (half the legs at a time; the feet left down must still hold it to lift)
            GaitWave => false,
            _ => live >= 6 && live % 2 == 0,
        };
        var rank = 0;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (!LegLive(c, k))
            {
                leg.Swinging = false;
                continue;
            }
            var pick = tripod ? LegGroupPick(c, k) : 0; // (Advanced: a leg's own half)
            leg.Group = (byte)(tripod ? pick > 0 ? pick - 1 : rank % 2 : rank);
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

    /// <summary>How many working legs have a foot on ground.</summary>
    private static int Grounded(Construct c)
    {
        var n = 0;
        for (var k = 0; k < c.LegCount; k++)
            if (c.Legs[k].Grounded && LegLive(c, k))
                n++;
        return n;
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
        c.WalkWait = 255;
        c.FeetSet = false;
    }

    /// <summary>
    /// The feet for a body at pose <paramref name="pos"/>, <paramref name="rot"/> moving at <paramref name="vel"/> and
    /// <paramref name="angVel"/>: each foot's next foothold (its rest point, led by the motion, dropped onto the ground) and
    /// the group in the air. The server runs it on the body; a client hears the feet in the poses (protocol 229,
    /// ConstructNetWalk.cs). Allocates nothing.
    /// </summary>
    public static void WalkFeet(Construct c, IWorldQuery query, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel, float swing, float dt, bool server)
    {
        var com = pos + Vector3.Transform(c.Com, rot);
        var lead = swing * c.LegGroups * 0.5f * c.WalkStride; // (seconds ahead a foot is put: half a cycle, by the stride)
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (!LegLive(c, k))
            {
                // Unfed or wrecked: locked at its rest pose, sagging a little towards the hip, carried along (it takes no weight).
                leg.Foot = leg.From = leg.To = pos + Vector3.Transform(leg.Hip + (leg.Rest - leg.Hip) * 0.92f, rot);
                leg.Swinging = false;
                continue;
            }
            var rest = pos + Vector3.Transform(LegRest(c, k), rot);
            var v = vel + Vector3.Cross(angVel, rest - com);
            v.Y = 0;
            var aim = rest + v * lead;
            var hip = pos + Vector3.Transform(leg.Hip, rot);
            leg.Hold = Foothold(c, query, aim, hip, leg.Reach, server, out var to, out var normal);
            if (leg.Hold)
            {
                leg.To = to;
                leg.Normal = normal;
            }
            if (!c.FeetSet)
            {
                leg.Foot = leg.From = leg.To = to; // (put down where it is aimed: on ground, or hanging over nothing)
                leg.Grounded = leg.Hold;
                leg.Normal = normal;
                leg.Swinging = false;
            }
            else if (!leg.Swinging && !leg.Grounded)
                leg.Foot = leg.From = leg.To = to; // (a foot over nothing hangs, carried along, until it finds ground to step onto)
            else if (!leg.Hold && !leg.Swinging)
                leg.To = leg.Foot; // (no ground ahead: a planted foot stays down where it is)
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
                var h = c.Parts[leg.Part].D.Leg!.StepHeight * leg.Reach * c.WalkLift * LegLift(c, k);
                var rise = MathF.Max(0, leg.To.Y - leg.From.Y); // (over a step up, the foot clears it)
                leg.Foot = Vector3.Lerp(leg.From, leg.To, s * s * (3 - 2 * s)) + Vector3.UnitY * ((h + rise) * lift);
                if (s >= 1)
                {
                    leg.Foot = leg.To;
                    leg.Swinging = false;
                    leg.Grounded = true; // (it stepped onto a foothold that had ground when it lifted)
                }
            }
            if (s >= 1)
            {
                c.WalkStepping = false;
                c.WalkLanded = true; // (the server thumps the body: ConstructWalkFeel.cs)
            }
            return;
        }
        // Down: lift the next group that needs a step, if the feet left down hold the body (with a margin). A group that needs a
        // step but can't lift yet is the one the body waits for (WalkBody shifts its weight over the other feet first).
        c.WalkWait = 255;
        for (var t = 1; t <= c.LegGroups; t++)
        {
            var g = (byte)((c.WalkGroup + t) % c.LegGroups);
            if (!GroupNeeds(c, pos, rot, g))
                continue;
            if (!HeldWithout(c, com, g, LiftMargin))
            {
                if (c.WalkWait == 255)
                    c.WalkWait = g;
                continue;
            }
            c.WalkWait = 255;
            c.WalkGroup = g;
            c.WalkStepping = true;
            c.WalkClock = 0;
            for (var k = 0; k < c.LegCount; k++)
            {
                ref var leg = ref c.Legs[k];
                if (Lifts(c, k, g))
                {
                    leg.Swinging = true;
                    leg.From = leg.Foot;
                }
            }
            break;
        }
    }

    public const float LiftMargin = 0.04f; // a group lifts only with the centre of mass this share of the legs' reach inside the feet left down

    /// <summary>Does leg <paramref name="k"/> lift with group <paramref name="g"/> (it works and has a foothold to go to; a hanging foot steps onto ground)?</summary>
    private static bool Lifts(Construct c, int k, byte g) => c.Legs[k].Group == g && c.Legs[k].Hold && LegLive(c, k);

    /// <summary>Is a foot of group <paramref name="g"/> far enough from its foothold (or its hip) to step?</summary>
    private static bool GroupNeeds(Construct c, Vector3 pos, Quaternion rot, byte g)
    {
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (!Lifts(c, k, g))
                continue; // (no foothold ahead: it stays down; the body stops when it is stretched: WalkBody)
            if (!leg.Grounded)
                return true; // (hanging over nothing, with ground in reach now)
            var d = leg.To - leg.Foot;
            d.Y *= 0.5f;
            if (d.LengthSquared() > leg.Reach * leg.Reach * 0.0064f * c.WalkStride * c.WalkStride) // (8% of its reach, by the stride)
                return true;
            var hip = pos + Vector3.Transform(leg.Hip, rot);
            if (Vector3.DistanceSquared(hip, leg.Foot) > leg.Reach * leg.Reach * 0.9f) // (over-stretched: it must step)
                return true;
        }
        return false;
    }

    /// <summary>
    /// With group <paramref name="g"/> in the air (255: none), do at least three feet on ground stay down round the centre of
    /// mass, at least <paramref name="margin"/> of the legs' reach inside their edges?
    /// </summary>
    private static bool HeldWithout(Construct c, Vector3 com, byte g, float margin = 0)
    {
        var first = -1;
        var prev = -1;
        var down = 0;
        var m = 0f;
        for (var k = 0; k < c.LegCount; k++)
        {
            if (!Supports(c, k, g))
                continue;
            ref var leg = ref c.Legs[k];
            down++;
            m = MathF.Max(m, margin * leg.Reach);
            if (prev >= 0 && !LeftOf(c.Legs[prev].Foot, leg.Foot, com, m))
                return false;
            if (first < 0)
                first = k;
            prev = k;
        }
        return down >= 3 && LeftOf(c.Legs[prev].Foot, c.Legs[first].Foot, com, m);
    }

    /// <summary>Does leg <paramref name="k"/>'s foot hold the body with group <paramref name="g"/> in the air (255: none)?</summary>
    private static bool Supports(Construct c, int k, byte g) => c.Legs[k].Grounded && !Lifts(c, k, g) && LegLive(c, k);

    /// <summary>Is <paramref name="p"/> on the inner side of the edge from <paramref name="a"/> to <paramref name="b"/> (the plan, counter-clockwise by angle), by at least <paramref name="margin"/> metres?</summary>
    private static bool LeftOf(Vector3 a, Vector3 b, Vector3 p, float margin = 0)
    {
        var ex = b.X - a.X;
        var ez = b.Z - a.Z;
        var cross = ex * (p.Z - a.Z) - ez * (p.X - a.X);
        return margin <= 0 ? cross >= 0 : cross >= margin * MathF.Sqrt(ex * ex + ez * ez);
    }

    public const float FootSteep = 0.55f; // a foothold's ground normal must be at least this up (about 57 degrees of slope at most)
    public const float FootReach = 0.98f; // ... and within this share of the leg's reach from its hip

    /// <summary>
    /// The foothold for a foot aimed at <paramref name="aim"/>: the ground under it by a ray down from a reach above to a reach
    /// and a half below, kept only if it is within <see cref="FootReach"/> of the leg's reach from <paramref name="hip"/> and
    /// no steeper than <see cref="FootSteep"/>. Failing that, a few rays nearer the body and to either side (at most
    /// <see cref="FootTries"/> in all). False when none holds: <paramref name="at"/> is then where the foot hangs, reaching down.
    /// </summary>
    private static bool Foothold(Construct c, IWorldQuery query, Vector3 aim, Vector3 hip, float reach, bool server, out Vector3 at, out Vector3 normal)
    {
        var inward = new Vector3(hip.X - aim.X, 0, hip.Z - aim.Z);
        var side = new Vector3(-inward.Z, 0, inward.X);
        for (var t = 0; t < FootTries; t++)
        {
            // (the aim; then a third and two thirds of the way in towards the hip; then a quarter reach to either side of the aim)
            var p = t switch
            {
                0 => aim,
                1 => aim + inward * 0.33f,
                2 => aim + inward * 0.66f,
                3 => aim + (side.LengthSquared() > 1e-4f ? Vector3.Normalize(side) : Vector3.UnitX) * (0.25f * reach),
                _ => aim - (side.LengthSquared() > 1e-4f ? Vector3.Normalize(side) : Vector3.UnitX) * (0.25f * reach),
            };
            var from = p + Vector3.UnitY * reach;
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
                continue;
            var ground = from - Vector3.UnitY * hit.Distance;
            var n = hit.Normal.LengthSquared() > 1e-6f ? Vector3.Normalize(hit.Normal) : Vector3.UnitY;
            if (n.Y < FootSteep || Vector3.DistanceSquared(hip, ground) > reach * reach * FootReach * FootReach)
                continue;
            at = ground;
            normal = n;
            return true;
        }
        normal = Vector3.UnitY;
        var hang = aim - Vector3.UnitY * reach * 0.5f - hip; // (nothing to stand on: the foot reaches down and carries nothing)
        var most = reach * 0.95f;
        at = hip + (hang.LengthSquared() > most * most ? Vector3.Normalize(hang) * most : hang);
        return false;
    }

    public const int FootTries = 5;

    /// <summary>
    /// The locomotion controller: the body's speed along the ground from the driver's keys (forward and back, turn; the
    /// roll keys strafe, Sprint and Crouch raise and lower it), its height over its feet, its tilt with the ground under
    /// them, and a shift back over the feet when the centre of mass leaves them. Velocity-level: it sets what the body moves
    /// at, so gravity is held by the legs; contacts still stop it.
    /// </summary>
    public const float EdgeLag = 0.3f; // a foot with no foothold ahead lets the body go on until it is this share of its reach behind its rest point
    public const float StretchSlow = 0.95f, StretchStop = 0.99f; // a planted leg's hip to foot, a share of its reach: the body slows past the first and stops at the second

    /// <summary>
    /// How much of the driver's speed the gait lets the body take (0 to 1): a planted leg the motion stretches slows it from
    /// <see cref="StretchSlow"/> and stops it at <see cref="StretchStop"/> of its reach (a foot with no foothold ahead stays
    /// down, so at an edge the body stops there); a group that needs a step but can't lift holds it while it shifts its centre
    /// of mass (<paramref name="shift"/>, m/s) towards the middle of the feet that stay down. Says why in WalkProblem when it stops.
    /// </summary>
    private static float GaitShare(Construct c, Vector3 com, Vector3 target, float yaw, out Vector3 shift)
    {
        shift = Vector3.Zero;
        var share = 1f;
        var edge = false;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (leg.Swinging || !leg.Grounded || !LegLive(c, k))
                continue;
            var hip = c.ToWorld(leg.Hip);
            if (!leg.Hold)
            {
                // (no foothold ahead: the foot stays down, and the body goes no further than leaves it EdgeLag behind its rest point)
                var rest = c.ToWorld(LegRest(c, k));
                var lagBy = new Vector3(rest.X - leg.Foot.X, 0, rest.Z - leg.Foot.Z);
                var rv = target + new Vector3(yaw * (rest.Z - com.Z), 0, -yaw * (rest.X - com.X));
                if (Vector3.Dot(rv, lagBy) > 0)
                {
                    var most = EdgeLag * leg.Reach;
                    var keep = Math.Clamp((most - lagBy.Length()) / (most * 0.4f), 0, 1);
                    if (keep < share)
                    {
                        share = keep;
                        edge = true;
                    }
                }
            }
            var s = Vector3.Distance(hip, leg.Foot) / MathF.Max(0.1f, leg.Reach);
            if (s <= StretchSlow)
                continue;
            var away = new Vector3(hip.X - leg.Foot.X, 0, hip.Z - leg.Foot.Z);
            var hv = target + new Vector3(yaw * (hip.Z - com.Z), 0, -yaw * (hip.X - com.X)); // (the hip's motion: along, and round the turn)
            if (Vector3.Dot(hv, away) <= 0)
                continue; // (this way of moving brings the hip back over its foot)
            var leave = Math.Clamp((StretchStop - s) / (StretchStop - StretchSlow), 0, 1);
            if (leave < share)
            {
                share = leave;
                edge = !leg.Hold;
            }
        }
        if (c.WalkWait != 255 && !c.WalkStepping)
        {
            // (the feet that stay down when the waiting group lifts: their middle, and the body's weight moves towards it)
            var mid = Vector3.Zero;
            var n = 0;
            var reach = 0f;
            for (var k = 0; k < c.LegCount; k++)
                if (Supports(c, k, c.WalkWait))
                {
                    mid += c.Legs[k].Foot;
                    reach += c.Legs[k].Reach;
                    n++;
                }
            if (n >= 3)
            {
                mid /= n;
                reach /= n;
                var to = new Vector3(mid.X - com.X, 0, mid.Z - com.Z);
                var d = to.Length();
                if (d > 1e-3f)
                    shift = to / d * MathF.Min(d * 1.5f, MathF.Max(0.3f, c.WalkTop * 0.6f));
                share = MathF.Min(share, 0.15f); // (a crawl while the weight moves over)
            }
        }
        if (share < 0.05f && edge && (target.LengthSquared() > 1e-4f || yaw != 0))
            c.WalkProblem = ProblemNoFooting;
        return share;
    }

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
        stand = MathF.Min(0.84f, stand / Math.Max(1, c.LegCount) * c.WalkStand); // (Basic: the body height)
        // Height: the hips over their feet, and how far the body can still rise before a planted leg is stretched past
        // MaxStretch of its reach (on a slope the uphill legs' hips stand lower over their feet than the others').
        var have = 0f;
        var n = 0;
        var normal = Vector3.Zero;
        var com = c.WorldCom;
        var centre = Vector3.Zero;
        var down = 0;
        var rise = float.PositiveInfinity;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            if (!LegLive(c, k))
                continue;
            var hip = c.ToWorld(leg.Hip);
            have += hip.Y - (leg.Swinging ? leg.To.Y : leg.Foot.Y);
            n++;
            if (!leg.Swinging && leg.Grounded)
            {
                normal += leg.Normal;
                centre += leg.Foot;
                down++;
                var dx = hip.X - leg.Foot.X;
                var dz = hip.Z - leg.Foot.Z;
                var most = leg.Reach * MaxStretch;
                rise = MathF.Min(rise, MathF.Sqrt(MathF.Max(0, most * most - dx * dx - dz * dz)) - (hip.Y - leg.Foot.Y));
            }
        }
        have /= Math.Max(1, n);
        if (raise <= 0 || rise > 0.01f * reach)
            c.WalkHeight += raise * reach * 0.15f * dt; // (raising stops where a planted leg runs out of stretch)
        c.WalkHeight = Math.Clamp(c.WalkHeight, -0.3f * stand * reach, (0.85f - stand) * reach);
        var want = stand * reach + c.WalkHeight;
        var vy = Math.Clamp(MathF.Min(want - have, rise) * 2f, -0.25f * reach, 0.25f * reach);
        vy = MathF.Min(vy, c.WalkPowerLift); // (a rise takes its weight times the speed: no faster than the circuit feeds)
        // Along the ground: the keys, at what the build walks at.
        var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
        fwd.Y = 0;
        fwd = fwd.LengthSquared() > 1e-4f ? Vector3.Normalize(fwd) : -Vector3.UnitZ;
        var right = Vector3.Cross(fwd, Vector3.UnitY);
        var target = fwd * (throttle * c.WalkTop) + right * (strafe * c.WalkTop * 0.6f);
        var radius = MathF.Max(1, c.Radius);
        var yaw = -turn * c.WalkTop / radius * 0.8f;
        // The gait has its say: the body goes no faster than the feet can follow (GaitShare: a planted leg near its stretch,
        // with no foothold ahead or a group waiting to lift), and with a group waiting, it first shifts its weight over the
        // feet that stay down (a wave gait, a body with legs gone).
        var share = GaitShare(c, com, target, yaw, out var shift);
        if (vy > 0 && c.WalkPowerLift < float.PositiveInfinity)
        {
            // (rising and walking share the power: the rise first, so a walker climbing a slope slows to what it can feed)
            var along = target.Length();
            var most = MathF.Max(0, c.WalkPowerLift - vy) / LegCarryCost;
            if (along > most)
                share = MathF.Min(share, most / along);
        }
        target = target * share + shift;
        yaw *= share;
        if (down >= 3)
        {
            // The centre of mass over the feet that are down: if it is outside them, the body leans back over them.
            centre /= down;
            var inside = true;
            int first = -1, prev = -1;
            for (var k = 0; k < c.LegCount && inside; k++)
            {
                if (c.Legs[k].Swinging || !c.Legs[k].Grounded || !LegLive(c, k))
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
        var vyNow = c.Vel.Y + WalkGravity(c) * dt; // (the legs hold the weight: this tick's gravity is theirs, so the height loop has no lag of g / 6 under it)
        c.Vel = new Vector3(flat.X, vyNow + (vy - vyNow) * MathF.Min(1, 6 * dt), flat.Z);
        // Tilt with the ground (half of it), and the turn.
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        var ground = down > 0 && normal.LengthSquared() > 1e-4f ? Vector3.Normalize(normal) : Vector3.UnitY;
        var sway = Vector3.Zero;
        if (c.WalkStepping && down >= 3)
        {
            // The ride feel (ConstructWalkFeel.cs): with a group in the air it leans towards the feet still down, less with stabilisers.
            sway = new Vector3(centre.X - com.X, 0, centre.Z - com.Z);
            sway = sway.LengthSquared() > 1e-4f ? Vector3.Normalize(sway) * (SwayLean * c.WalkRough) : Vector3.Zero;
        }
        var level = Vector3.Normalize(Vector3.UnitY + (ground - Vector3.UnitY) * 0.5f + sway);
        var turnAccel = MathF.Max(0.2f, c.WalkTop / radius) * 2 * dt;
        var yawNow = c.AngVel.Y;
        var yawNext = yawNow + Math.Clamp(yaw - yawNow, -turnAccel, turnAccel);
        var tiltNow = c.AngVel - Vector3.UnitY * yawNow;
        var tiltNext = Vector3.Lerp(tiltNow, Vector3.Cross(up, level) * 2.5f, MathF.Min(1, 6 * dt));
        c.AngVel = tiltNext + Vector3.UnitY * yawNext;
    }
}
