using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Decks (docs/sandbox/CONSTRUCTS.md, "Large systems"): a raycast wheel or spring foot stands on other constructs as it
/// does on the map. The tyre works against the deck's own motion at the contact (<see cref="ConstructRide.SurfaceVel"/>:
/// the body's, and its joints'), so a parked vehicle rides a moving mothership, a lift or a turntable, and a driven one
/// rolls off it onto the ground; the deck takes the reaction, and a joint under it the weight
/// (<see cref="Construct.DeckLoads"/>: a lift shows the vehicle on its Load bar and gives way past its rating). A body
/// much lighter than the wheel's isn't ground (a crate under a truck stays a hull contact), and neither is something
/// coupled to it. Server only; nothing allocates.
/// </summary>
public static partial class ConstructPhysics
{
    /// <summary>A free body counts as ground under a wheel when it weighs at least this share of the wheel's body (an anchored one always).</summary>
    public const float DeckMassShare = 0.25f;

    private const int MaxDeckPushes = 1024;

    private struct DeckImpulse
    {
        public Construct Body;
        public int Seg;
        public Vector3 At, J;
    }

    private static readonly DeckImpulse[] _deckImpulses = new DeckImpulse[MaxDeckPushes];
    private static int _deckPushes;
    private static int _deckPart; // the part the last GroundRay stood on
    private static int _rayPart; // the part the last DeckRay hit
    private static int _rayFrom = -1; // the wheel or foot asking
    private static readonly List<Construct> _deckNear = new(64);
    private static readonly int[] _deckHits = new int[Construct.MaxParts];

    /// <summary>
    /// The ground under a wheel or foot: the map, or the nearest part of a construct that can carry it.
    /// <paramref name="deck"/> and <paramref name="seg"/> name that construct and its segment (null: the map).
    /// </summary>
    private static bool GroundRay(Construct c, IWorldQuery query, Vector3 origin, Vector3 dir, float len, out RayHit hit, out Construct? deck, out int seg, bool decks = true)
    {
        deck = null;
        seg = 0;
        var found = query.Raycast(origin, dir, len, QueryFilter.All, out hit);
        if (!decks || _world is not { } world || world.Bodies.Count < 2)
            return found;
        var best = found ? hit.Distance : len;
        var near = world.NearRay(origin, dir, best, _deckNear);
        for (var k = 0; k < near.Count; k++)
        {
            var b = near[k];
            if (b == c || b.CarriedBy == c.Id || !b.Anchored && b.Mass < c.Mass * DeckMassShare)
                continue;
            var oc = origin - b.WorldCom;
            var along = Vector3.Dot(oc, dir);
            var cc = oc.LengthSquared() - b.Radius * b.Radius;
            if (cc > 0 && along > 0 || along * along - cc < 0)
                continue;
            if (!DeckRay(b, origin, dir, best, out var d, out var normal, out var s) || d < 1e-3f || world.Coupled(c.Id, b.Id))
                continue; // (a ray starting inside a box tells nothing)
            if (world.FirstNoCollide(c, b) is var nc and >= 0 && _rayFrom >= 0 && world.NoCollideParts(nc, c, c.MoveSeg(c.Parts[_rayFrom].Seg), _rayFrom, b, b.MoveSeg(s), _rayPart))
                continue; // (a no-collide pair: the wheel or foot goes through)
            best = d;
            deck = b;
            seg = s;
            _deckPart = _rayPart;
            hit = default;
            hit.Distance = d;
            hit.Point = origin + dir * d;
            hit.Normal = normal;
            found = true;
        }
        return found;
    }

    /// <summary>The nearest of <paramref name="b"/>'s solid boxes along a ray, through its part tree (a carrier has thousands of parts). <see cref="_rayPart"/> names the part.</summary>
    private static bool DeckRay(Construct b, Vector3 origin, Vector3 dir, float maxDist, out float dist, out Vector3 normal, out int seg)
    {
        dist = maxDist;
        normal = Vector3.Zero;
        seg = 0;
        _rayPart = -1;
        var found = false;
        var n = b.PartsOnRay(b.ToLocal(origin), Vector3.Transform(dir, Quaternion.Conjugate(b.Rot)), maxDist, _deckHits);
        for (var k = 0; k < n; k++)
        {
            var pi = _deckHits[k];
            ref var part = ref b.Parts[pi];
            b.WorldFrame(part.Seg, out var q, out var t);
            var iq = Quaternion.Conjugate(q);
            var lo = Vector3.Transform(origin - t, iq);
            var ld = Vector3.Transform(dir, iq);
            var boxes = b.SolidCount(pi);
            for (var bi = 0; bi < boxes; bi++)
            {
                b.SolidBox(pi, bi, out var centre, out var half);
                if (Geometry.RayAabb(centre - half, centre + half, lo, ld, dist) is { } d && d < dist)
                {
                    dist = d;
                    found = true;
                    seg = part.Seg;
                    _rayPart = pi;
                    normal = Vector3.Transform(Geometry.BoxEntryNormal(centre - half, centre + half, lo, ld, d), q);
                }
            }
        }
        return found;
    }

    /// <summary>How fast the part the last <see cref="GroundRay"/> stood on moves at a point: its body and joints, and a turning wheel's tread or a conveyor's belt.</summary>
    private static Vector3 DeckVel(Construct deck, int seg, Vector3 at) =>
        ConstructRide.SurfaceVel(deck, seg, at) + BeltVel(deck, _deckPart) + (deck.WheelCount > 0 ? TreadVel(deck, _deckPart, at) : Vector3.Zero);

    /// <summary>An impulse a wheel or foot gave its body at <paramref name="at"/>: the deck takes it back after every body's wheels are done.</summary>
    private static void DeckPush(Construct deck, int seg, Vector3 at, Vector3 j)
    {
        if (_deckPushes >= MaxDeckPushes)
            return;
        ref var p = ref _deckImpulses[_deckPushes++];
        p.Body = deck;
        p.Seg = seg;
        p.At = at;
        p.J = j;
    }

    /// <summary>Each deck takes its reactions (when it moves at all), and the joints under the parts stood on take the weight.</summary>
    private static void ApplyDeckPushes(int n, float dt)
    {
        for (var i = 0; i < n; i++)
            if (_bodies[i].DeckLoads is { } loads)
                Array.Clear(loads);
        for (var k = 0; k < _deckPushes; k++)
        {
            ref var p = ref _deckImpulses[k];
            var b = p.Body;
            var i = b.SimIndex;
            if ((uint)i >= (uint)n || !ReferenceEquals(_bodies[i], b))
                continue;
            if (_dyn[i])
                ApplyImpulse(b, _invI[i], p.At - b.WorldCom, -p.J);
            if (p.Seg <= 0 || p.J.Y <= 0)
                continue;
            var kg = p.J.Y / MathF.Max(dt, 1e-4f) / MathF.Max(GravityAccel, 0.01f);
            var loads2 = b.DeckLoads ??= new float[Construct.MaxJoints + 1]; // (made once, the first time something stands on a joint)
            for (int s = p.Seg, guard = 0; s > 0 && s <= b.JointCount && guard <= Construct.MaxJoints; guard++)
            {
                loads2[s] += kg;
                s = b.Joints[s - 1].ParentSeg;
            }
        }
    }

    /// <summary>Does a real joint of <paramref name="c"/> move right now?</summary>
    private static bool JointMoves(Construct c)
    {
        for (var j = 0; j < c.JointCount; j++)
            if (!c.Joints[j].Freeform && MathF.Abs(c.Joints[j].Vel) > 0.02f)
                return true;
        return false;
    }

    /// <summary>The velocity a running conveyor's belt gives what touches it, beyond the part's own motion (zero for any other part).</summary>
    internal static Vector3 BeltVel(Construct b, int part)
    {
        if (b.BeltCount == 0 || (uint)part >= (uint)b.PartCount)
            return Vector3.Zero;
        ref var p = ref b.Parts[part];
        if (p.D.Logic != CLogic.Conveyor || p.Broken || p.Out == 0)
            return Vector3.Zero;
        b.WorldFrame(p.Seg, out var q, out _);
        return Vector3.Transform(Rot24.Apply(p.Rot, p.D.BeltDir), q) * (p.D.BeltSpeed * p.Out);
    }

    /// <summary>Has <paramref name="c"/> a conveyor running?</summary>
    internal static bool BeltMoving(Construct c)
    {
        if (c.BeltCount == 0)
            return false;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Logic == CLogic.Conveyor && c.Parts[i].Out != 0 && !c.Parts[i].Broken)
                return true;
        return false;
    }
}
