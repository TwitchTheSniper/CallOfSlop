using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A wheel's shape (docs/sandbox/CONSTRUCTS.md, "Wheels"). The spring still stands on rays, but the wheel is a cylinder of its
/// radius and width at the hub as drawn. <b>The rim</b> (<see cref="RimGround"/>): where it matters (<see cref="RimWanted"/>),
/// a few more rays down either side of the axle and across the tread say how high the hub must ride for the circle to clear
/// what is under it, so a kerb lifts the wheel from where the rim first meets it, a gap narrower than the wheel is bridged and
/// a big wheel rides over the tops of rough ground. <b>The cylinder</b>: one leaf per wheel in the construct's collider (<see cref="ConstructCollider.AddCylinder"/>),
/// whose bounds hold its whole travel, so the tree is never touched as the hub moves; what comes near it goes through the
/// hull test against <see cref="ConstructHulls.WheelPrism"/>, so walls, the sides of kerbs, other vehicles and steep ground stop
/// the wheel. What the rays carry isn't met twice: a point that pushes the wheel up onto something it rides is dropped
/// (<see cref="Rides"/>), until the spring has run out of travel. Nothing allocates.
/// </summary>
public static partial class ConstructPhysics
{
    private const float RimNear = 0.5f, RimFar = 0.87f; // how far out along the radius the rim's rays go
    private const int RimBisects = 3; // how closely a kerb's edge between two rays is found
    private const float RimStep = 0.03f; // (of the radius) a rise of less than this is a slope, and needs no edge found
    private const float RimClimb = 0.6f; // the highest step a wheel rolls up, over its radius; higher is a wall to its cylinder
    private const float RimShoulder = 0.12f; // the same for the tread's edges: a tyre's shoulder rides up a low kerb met side-on, a higher one stops it
    private const float RimRate = 2.5f; // m/s: the fastest the rim lifts the damper; the tyre takes the rest of a blow
    private const float RideNormal = 0.4f; // a contact pushing the wheel up by this much (cos of the steepest RimClimb step) is the spring's...
    private const float SoleNormal = 0.85f; // ...and straight up, even with the spring bottomed (the rays' hard stop has it)
    private const float WheelFloorSkin = 0.01f; // a piece no higher than this over the wheel's bottom can only be under it

    private const float RollFriction = 0.15f; // a rolling wheel against what stops it: it turns, so little drags. (Not the tread's speed, as on a loose crate: full friction against it ran a car up every wall it hit, and a wheel's own spin holds too little for that)
    private const int RimEvery = 32; // a vehicle with more wheels than this asks for each wheel's rim in turn, holding the last answer

    private const float RimFall = 0.02f; // m: the axle's ray dropping this much in a tick has the rim looked at

    private static RayHit _rimHit;

    /// <summary>
    /// Is this wheel's rim worth the rays this tick? On an open road or a deck it isn't: they are for something standing near
    /// the wheel (the cylinder's quick test saw it last tick: <see cref="CWheel.RimNear"/>), the ground falling away under
    /// the axle (a gap to bridge, an edge to roll off), and terrain, where the wheel spans its cells. A small wheel on
    /// terrain's flat triangles has nothing to find.
    /// </summary>
    private static bool RimWanted(IWorldQuery query, in CWheel w, bool found, in RayHit hit, float len, float lastX)
    {
        if (w.RimNear)
            return true;
        if (!found)
            return w.Touching;
        if (len - hit.Distance < lastX - RimFall)
            return true;
        return hit.Kind == ShapeKind.Terrain && w.Radius * 2 >= (query.Terrain?.Data.Cell ?? 1f);
    }

    /// <summary>
    /// How high the rim needs the hub: the rays down either side of the axle (the wheel's next place, <paramref name="ahead"/>
    /// metres on along it), and across the tread. True when that is above what the axle's own ray gave (<paramref name="found"/>,
    /// <paramref name="hit"/>): <paramref name="hit"/> is then the rim's, its distance what the hub's height asks of the axle's
    /// ray, capped at the spring's travel (a step past that is the cylinder's). <paramref name="lastX"/>: last tick's compression.
    /// </summary>
    private static bool RimGround(Construct c, IWorldQuery query, ref CWheel w, Vector3 origin, Vector3 up, Vector3 axle, float ahead, float len, float lastX, bool found, ref RayHit hit, ref Construct? deck, ref int seg, bool decks)
    {
        var along = Vector3.Cross(axle, up);
        var flat = along.LengthSquared();
        if (flat < 1e-4f)
            return false;
        along /= MathF.Sqrt(flat);
        var across = axle - up * Vector3.Dot(axle, up);
        var best = found ? len - hit.Distance : 0f;
        var bottom = len - lastX; // (how far under the origin the wheel's bottom was, to tell a step from a wall)
        var climb = RimClimb * w.Radius;
        var r = w.Radius;
        var any = false;
        var part = _deckPart;
        Construct? bestDeck = deck;
        var bestSeg = seg;
        var at = origin + along * ahead;
        for (var sign = -1f; sign <= 1f; sign += 2f)
        {
            var low = 0f; // (the inside of the last two rays: nothing there asks the wheel up)
            var fr = RimNear;
            for (var ring = 0; ring < 2; ring++)
            {
                var s = sign * r * fr;
                var before = best;
                if (Sample(c, query, at + along * s, up, r, s, w.Travel, bottom, climb, decks, ref best))
                {
                    Take(ref any, ref part, ref bestDeck, ref bestSeg);
                    // A step between these two: its edge, by halves, since the height changes fastest there.
                    var high = s;
                    for (var b = 0; b < RimBisects && best - before > RimStep * r; b++)
                    {
                        var mid = (low + high) * 0.5f;
                        if (Sample(c, query, at + along * mid, up, r, mid, w.Travel, bottom, climb, decks, ref best))
                        {
                            Take(ref any, ref part, ref bestDeck, ref bestSeg);
                            high = mid;
                        }
                        else
                            low = mid;
                    }
                }
                else
                    low = s;
                fr = RimFar;
            }
        }
        // Across the tread: the edges carry it where the middle has nothing, and the shoulder rides up a low kerb.
        if (w.HalfWidth > 0.02f && across.LengthSquared() > 1e-4f)
            for (var sign = -1f; sign <= 1f; sign += 2f)
            {
                var edge = any || found ? RimShoulder * r : climb;
                if (Sample(c, query, at + across * (sign * 0.8f * w.HalfWidth), up, r, 0, w.Travel, any || found ? len - best : bottom, edge, decks, ref best))
                    Take(ref any, ref part, ref bestDeck, ref bestSeg);
            }
        _deckPart = part;
        if (!any)
            return false;
        _rimAxle = found ? hit.Distance : len;
        hit = _rimHit;
        hit.Distance = len - MathF.Min(best, w.Travel);
        deck = bestDeck;
        seg = bestSeg;
        return true;
    }

    private static void Take(ref bool any, ref int part, ref Construct? deck, ref int seg)
    {
        any = true;
        part = _deckPart;
        deck = _sampleDeck;
        seg = _sampleSeg;
    }

    private static float _rimAxle; // what the axle's ray gave, beside the rim's answer
    private static Construct? _sampleDeck;
    private static int _sampleSeg;

    /// <summary>One ray of the rim, <paramref name="s"/> metres from the axle: does what it finds ask the hub higher than <paramref name="best"/> (a compression)?</summary>
    private static bool Sample(Construct c, IWorldQuery query, Vector3 from, Vector3 up, float r, float s, float travel, float bottom, float climb, bool decks, ref float best)
    {
        var rise = MathF.Sqrt(MathF.Max(0, r * r - s * s));
        var reach = travel + rise - best;
        if (reach <= 1e-3f)
            return false;
        reach = MathF.Min(reach, travel + r);
        LastRays++;
        if (!GroundRay(c, query, from, -up, reach, out var hit, out _sampleDeck, out _sampleSeg, decks) || hit.Distance < 1e-3f || bottom - hit.Distance > climb)
            return false; // (nothing; or it starts inside something, or stands higher than a wheel rolls up: the cylinder's)
        best = travel + rise - hit.Distance;
        _rimHit = hit;
        return true;
    }

    /// <summary>Wheel <paramref name="k"/>'s cylinder in the world: its frame (X along the axle) and the box round it. <paramref name="f"/>: its rigid group's frame.</summary>
    private static void WheelBox(Construct c, int k, in CFrame f, out CFrame hub, out WBox box)
    {
        ref var w = ref c.Wheels[k];
        c.HubFrame(k, out var at, out var axle);
        hub.T = f.Point(at);
        hub.X = f.Dir(axle);
        var y = f.Y - hub.X * Vector3.Dot(f.Y, hub.X);
        hub.Y = Vector3.Normalize(y);
        hub.Z = Vector3.Cross(hub.X, hub.Y);
        box = new WBox { C = hub.T, X = hub.X, Y = hub.Y, Z = hub.Z, H = new Vector3(w.HalfWidth, w.Radius, w.Radius), Hull = -1 };
    }

    /// <summary>
    /// Has the spring got this contact? <paramref name="n"/> runs into the wheel; <paramref name="ground"/>: the rays stand on
    /// what it touches. What falls between the rays (a rail, a lip while the wheel hangs in a gap) is asked of the spring next
    /// tick: <see cref="CWheel.RimAsk"/>, the compression that clears this point.
    /// </summary>
    private static bool Rides(Construct c, ref CWheel w, Vector3 n, float gap, bool ground)
    {
        if (!ground)
            return false;
        var lift = Vector3.Dot(n, Vector3.Transform(Vector3.UnitY, c.Rot));
        if (lift < SoleNormal && (lift < RideNormal || w.Compression >= w.Travel - 1e-3f))
            return false;
        if (lift < SoleNormal && gap < Skin)
            w.RimAsk = MathF.Max(w.RimAsk, w.Compression - gap / lift);
        return true;
    }

    /// <summary>Is the leaf a wheel of <paramref name="c"/>'s, as the collider and the wheels agree (a rebuild changes both at once)?</summary>
    private static bool WheelLive(Construct c, in ConstructCollider.Leaf l) => (uint)l.WheelIx < (uint)c.WheelCount && c.Wheels[l.WheelIx].Part == l.Part;

    /// <summary>Can a piece reach the wheel? Quick answers before the hull test: is it further off than the cylinder's sphere, or wholly under a wheel the rays carry?</summary>
    private static bool WheelNear(Construct c, in CWheel w, in WBox hub, in WBox o, float margin, bool ground)
    {
        var d = o.C - hub.C;
        var reach = w.Radius + w.HalfWidth + margin;
        if (MathF.Abs(Vector3.Dot(d, o.X)) > o.H.X + reach || MathF.Abs(Vector3.Dot(d, o.Y)) > o.H.Y + reach || MathF.Abs(Vector3.Dot(d, o.Z)) > o.H.Z + reach)
            return false;
        if (!ground || !w.Touching)
            return true;
        // (its top face against the wheel's lowest point that way, so a long slab under a tilted body is still a floor)
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        float ax = Vector3.Dot(o.X, up), ay = Vector3.Dot(o.Y, up), az = Vector3.Dot(o.Z, up);
        float bx = MathF.Abs(ax), by = MathF.Abs(ay), bz = MathF.Abs(az);
        var face = bx >= by && bx >= bz ? (ax > 0 ? o.X : -o.X) : by >= bz ? (ay > 0 ? o.Y : -o.Y) : az > 0 ? o.Z : -o.Z;
        var half = bx >= by && bx >= bz ? o.H.X : by >= bz ? o.H.Y : o.H.Z;
        if (MathF.Max(bx, MathF.Max(by, bz)) < SoleNormal)
            return true;
        var side = MathF.Abs(Vector3.Dot(face, hub.X));
        var low = w.Radius * MathF.Sqrt(MathF.Max(0, 1 - side * side)) + w.HalfWidth * side;
        return Vector3.Dot(o.C - hub.C, face) + half > WheelFloorSkin - low;
    }

    /// <summary>A wheel's cylinder against a box of the map, appending to the candidates. The normal runs into the wheel.</summary>
    private static int WheelBoxShape(Construct c, int leaf, ConstructCollider col, in CFrame f, in WBox other, float margin, int n)
    {
        ref var l = ref col.Leaves[leaf];
        if (!WheelLive(c, l))
            return n;
        ref var w = ref c.Wheels[l.WheelIx];
        WheelBox(c, l.WheelIx, f, out var hub, out var box);
        if (!WheelNear(c, w, box, other, margin, true))
            return n;
        w.RimNear = true;
        _sa.FromWheel(hub, w.Radius, w.HalfWidth);
        _sb.FromBox(other);
        var from = n;
        n = ConstructNarrow.HullHull(_sa, _sb, margin, _cand, n);
        var kept = from;
        for (var j = from; j < n; j++)
        {
            ref var p = ref _cand[j];
            if (Rides(c, ref w, p.N, p.Gap, true))
                continue;
            p.LeafA = leaf;
            if (kept != j)
                _cand[kept] = p;
            kept++;
        }
        return kept;
    }

    /// <summary>A piece of one construct against a piece of another where one (or both) is a wheel's cylinder.</summary>
    private static int WheelPair(Construct a, ConstructCollider ca, int la, in CFrame fa, Construct b, ConstructCollider cb, int lb, in CFrame fb, float margin, int n)
    {
        ref var pa = ref ca.Leaves[la];
        ref var pb = ref cb.Leaves[lb];
        var wheelA = pa.WheelIx >= 0;
        var wheelB = pb.WheelIx >= 0;
        if (wheelA && !WheelLive(a, pa) || wheelB && !WheelLive(b, pb))
            return n;
        var groundA = wheelA && WheelGround(a, b) && _world is { } wa && !wa.Coupled(a.Id, b.Id);
        var groundB = wheelB && WheelGround(b, a) && _world is { } wb && !wb.Coupled(a.Id, b.Id);
        CFrame hubA = default, hubB = default;
        WBox boxA, boxB;
        if (wheelA)
            WheelBox(a, pa.WheelIx, fa, out hubA, out boxA);
        else
            ca.World(la, fa, out boxA);
        if (wheelB)
            WheelBox(b, pb.WheelIx, fb, out hubB, out boxB);
        else
            cb.World(lb, fb, out boxB);
        if (wheelA && !WheelNear(a, a.Wheels[pa.WheelIx], boxA, boxB, margin, groundA) || wheelB && !WheelNear(b, b.Wheels[pb.WheelIx], boxB, boxA, margin, groundB))
            return n;
        if (wheelA)
        {
            a.Wheels[pa.WheelIx].RimNear = true;
            _sa.FromWheel(hubA, boxA.H.Y, boxA.H.X);
        }
        else if (boxA.Hull >= 0)
            _sa.FromHull(ca.Hulls[boxA.Hull], fa);
        else
            _sa.FromBox(boxA);
        if (wheelB)
        {
            b.Wheels[pb.WheelIx].RimNear = true;
            _sb.FromWheel(hubB, boxB.H.Y, boxB.H.X);
        }
        else if (boxB.Hull >= 0)
            _sb.FromHull(cb.Hulls[boxB.Hull], fb);
        else
            _sb.FromBox(boxB);
        var from = n;
        n = ConstructNarrow.HullHull(_sa, _sb, margin, _cand, n);
        var kept = from;
        for (var j = from; j < n; j++)
        {
            ref var p = ref _cand[j];
            if (wheelA && Rides(a, ref a.Wheels[pa.WheelIx], p.N, p.Gap, groundA) || wheelB && Rides(b, ref b.Wheels[pb.WheelIx], -p.N, p.Gap, groundB))
                continue;
            p.LeafA = la;
            p.LeafB = lb;
            if (kept != j)
                _cand[kept] = p;
            kept++;
        }
        return kept;
    }

    /// <summary>The friction scale of a wheel's contacts (its tyre's dry grip against its part's own) and what a blow gives back (<see cref="BounceBack"/> of its bounciness).</summary>
    private static void WheelContact(Construct c, in ConstructCollider.Leaf l, out float friction, out float bounce)
    {
        friction = 1;
        bounce = 0;
        if (l.WheelIx < 0 || (uint)l.Part >= (uint)c.PartCount)
            return;
        ref var p = ref c.Parts[l.Part];
        friction = MathF.Min(2, ConstructConfig.Ratio(p, p.D, CfgKey.Friction));
        bounce = ConstructConfig.Get(p, p.D, CfgKey.Bounciness, 0) * 0.01f * BounceBack;
    }

    /// <summary>
    /// The drawn wheel on a client (<see cref="ConstructRender.FitWheels"/>): the axle's ray and, on a vehicle of no more than
    /// <see cref="FitRimWheels"/> wheels, the rim's, so a wheel sits on the kerb the server's does. Its compression, its touch.
    /// </summary>
    public static void FitWheel(Construct c, IWorldQuery query, int k, Vector3 up)
    {
        ref var w = ref c.Wheels[k];
        var len = w.Travel + w.Radius + w.Band;
        c.WheelFrame(k, out var centre, out var axleBody);
        var origin = c.ToWorld(centre);
        var found = query.Raycast(origin, -up, len, QueryFilter.All, out var hit);
        Construct? deck = null;
        var seg = 0;
        var rim = !w.Track && c.WheelCount <= FitRimWheels && RimGround(c, query, ref w, origin, up, Vector3.Transform(axleBody, c.Rot), 0, len, w.Compression, found, ref hit, ref deck, ref seg, false);
        w.Touching = found || rim;
        w.Compression = found || rim ? MathF.Min(len - hit.Distance, w.Travel) : 0;
    }

    private const int FitRimWheels = 32;

    /// <summary>What a blow to the wheel gives back at 100% bounciness (of the closing speed).</summary>
    private const float BounceBack = 0.7f;
}
