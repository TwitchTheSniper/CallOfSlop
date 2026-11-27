using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Noclip boarding (creative Sandbox; docs/sandbox/constructs/overview-and-playing.md "Noclip"): a flyer who leaves noclip
/// inside a construct's hull, or who boards one with the Board inside key (<see cref="InputCmd.NoclipBoard"/>), is set down on
/// a floor inside it, not pushed up out of it onto the roof. The shared step only holds them still in its frame
/// (<see cref="MoveState.NoclipHold"/>); the server looks for the floor over a few ticks (<see cref="InteriorSearch"/>, at
/// most <see cref="InteriorBudgetMs"/> a tick per player: a ray through a capital ship costs 50-100 us) and then sets them
/// down (Game.NoclipHoldTick). The search runs in the construct's rest frame along its rider's up, so a ship in a banking
/// turn or in orbit is the same search as a parked truck, and the ship moving between its ticks changes nothing.
/// </summary>
public static partial class ConstructRide
{
    /// <summary>Columns looked down at most per search, and rays cast at most (a capital ship spreads its columns wider).</summary>
    public const int InteriorColumns = 900, InteriorRays = 2400;

    /// <summary>A floor counts as inside when the construct is over it within this (m): a deck under its own roof.</summary>
    public const float InteriorRoof = 8f;

    /// <summary>The server's time for one player's search in one tick (ms).</summary>
    public const double InteriorBudgetMs = 1.0;

    /// <summary>The first pass looks for floors this far (m) above and below the start only.</summary>
    private const float WindowAbove = 2.5f, WindowBelow = 6f;

    /// <summary>Seats looked at for a standing spot beside them, when no floor under a roof is found.</summary>
    private const int InteriorSeats = 4;

    /// <summary>
    /// Shared step: is a flyer leaving noclip in frame <paramref name="c"/> inside its hull (within its bounds, and under its
    /// own roof or in its geometry)? Then they are held for a set-down inside; hovering over an open deck, they aren't, and
    /// drop onto it as before.
    /// </summary>
    public static bool NoclipInsideHull(ref MoveState s, Construct c)
    {
        if (Bodies is not { } w)
            return false;
        var iq = Quaternion.Conjugate(c.Rot);
        var up = s.RideUpW != Vector3.Zero ? s.RideUpW : Vector3.Normalize(Vector3.Transform(s.RideUp, c.Rot));
        if (!InFrame(c, c.ToLocal(s.Position), Vector3.Transform(up, iq), 0f, 0f))
            return false;
        if (w.Raycast(s.Position + up * 0.5f, up, InteriorRoof, out var roof, only: c) && roof.Body == c)
            return true;
        var r = World.BodyRadius;
        return !w.CapsuleClear(c, s.Position + up * (r + 0.05f), s.Position + up * (World.HeightOf(Stance.Stand) - r), r);
    }

    /// <summary>
    /// Shared step: a flyer leaving noclip in no construct's frame (they flew in from outside, so <see cref="NoclipFrame"/> has none)
    /// who is inside a construct's hull all the same: they join its frame, to be held for a set-down inside as a rider would. Karl,
    /// 2026-11-27: noclip off in a Zero Day's airlock room took the plain way out (the first room straight up), through the room's
    /// ceiling onto the hull's roof. Null: inside none. Only on the tick noclip ends; the bodies near the flyer only.
    /// </summary>
    public static Construct? NoclipHullAround(ref MoveState s)
    {
        if (Bodies is not { } w)
            return null;
        var up = GravityFrames.UpAt(s.Position);
        foreach (var c in w.Bodies)
        {
            if (c.PartCount == 0 || c.SpaceAway != 0 || Vector3.DistanceSquared(c.WorldCom, s.Position) > (c.Radius + 1f) * (c.Radius + 1f))
                continue;
            if (c.Dirty)
                c.Rebuild();
            if (!InFrame(c, c.ToLocal(s.Position), Vector3.Transform(up, Quaternion.Conjugate(c.Rot)), 0f, 0f))
                continue;
            var keep = s;
            Join(ref s, c, 0, UpFor(c, up), Vector3.Zero);
            if (NoclipInsideHull(ref s, c) && Enclosed(w, ref s, c))
                return c;
            s = keep; // (over an open deck or beside it: left as they were)
        }
        return null;
    }

    /// <summary>
    /// Under a roof of <paramref name="c"/> and on a floor of it too (a ray down meets it within <see cref="InteriorRoof"/>), or in its
    /// geometry: a flyer under a landed ship's belly, between its legs, has its roof overhead but the ground below, and drops to the ground.
    /// </summary>
    private static bool Enclosed(ConstructWorld w, ref MoveState s, Construct c)
    {
        var up = s.RideUpW != Vector3.Zero ? s.RideUpW : Vector3.Normalize(Vector3.Transform(s.RideUp, c.Rot));
        if (w.Raycast(s.Position + up * 0.5f, -up, InteriorRoof, out var floor, only: c) && floor.Body == c)
            return true;
        var r = World.BodyRadius;
        return !w.CapsuleClear(c, s.Position + up * (r + 0.05f), s.Position + up * (World.HeightOf(Stance.Stand) - r), r);
    }

    /// <summary>Shared step: held still in frame <paramref name="c"/>, noclip on, while the server finds them a floor inside.</summary>
    public static void NoclipHoldStill(ref MoveState s, Construct c)
    {
        s.NoclipHold = true;
        s.Noclip = true;
        s.NoclipExit = false;
        s.Grounded = false;
        s.Stance = Stance.Stand;
        s.MantleTicks = 0;
        s.RideVel = Vector3.Zero;
        s.RideOn = 0;
        Place(ref s, c);
    }

    /// <summary>
    /// Shared step: a flyer using the Board inside key on construct <paramref name="body"/> goes into its frame at
    /// <paramref name="at"/> (where they aimed, in its rest frame), to be held there for a set-down inside. False: no such construct here.
    /// </summary>
    public static bool NoclipBoardInto(ref MoveState s, ushort body, Vector3 at)
    {
        if (Bodies?.Get(body) is not { PartCount: > 0 } c || !float.IsFinite(at.X + at.Y + at.Z))
            return false;
        if (c.Dirty)
            c.Rebuild();
        if (!InFrame(c, at, Vector3.UnitY, BoundsPad, BoundsPad))
            at = Vector3.Clamp(at, c.LocalMin, c.LocalMax); // (a stale or made-up aim: inside its bounds)
        s.Position = c.ToWorld(at);
        Join(ref s, c, 0, UpFor(c, GravityFrames.UpAt(s.Position)), Vector3.Zero);
        return true;
    }

    /// <summary>Server: a held flyer set down at <paramref name="spot"/> (rest frame) of <paramref name="c"/>, standing still in its frame.</summary>
    public static void NoclipSetDown(ref MoveState s, Construct c, Vector3 spot)
    {
        var feet = c.ToWorld(spot);
        s.NoclipHold = s.Noclip = s.NoclipExit = false;
        s.RideBody = c.Id;
        s.RideSeg = 0;
        s.RideOn = 0;
        s.RideUp = UpFor(c, GravityFrames.UpAt(feet));
        s.RideLocal = spot;
        s.RideVel = Vector3.Zero;
        s.RideAir = s.RideClimb = s.RideFree = s.RideLadder = 0;
        s.Stance = Stance.Stand;
        s.Grounded = true;
        Place(ref s, c);
    }

    /// <summary>A whole search at once (the ride check's): the floor inside <paramref name="c"/> nearest world point <paramref name="from"/>.</summary>
    public static bool InteriorSpot(ConstructWorld w, Construct c, Vector3 from, float height, out Vector3 feet)
    {
        var search = new InteriorSearch();
        search.Begin(w, c, from, height);
        while (!search.Step(double.MaxValue)) { }
        feet = c.ToWorld(search.Spot);
        return search.Found;
    }

    /// <summary>
    /// One player's search for a place to stand inside a construct, stepped a slice at a time: a walkable floor with room for
    /// the body (the frame mover's own shapes, skin included) under the construct's own roof, nearest the start; else a spot
    /// beside one of its seats; else the nearest floor with headroom (an open truck bed). <see cref="Spot"/> is in the rest frame.
    /// </summary>
    public sealed class InteriorSearch
    {
        public ushort Body;
        public int Parts;
        public bool Active, Found;
        public Vector3 Spot;
        public int Rays, Columns, Ticks;
        public double Ms, WorstMs;
        private IEnumerator<int>? _run;

        public void Begin(ConstructWorld w, Construct c, Vector3 fromWorld, float height)
        {
            if (c.Dirty)
                c.Rebuild();
            Body = c.Id;
            Parts = c.PartCount;
            Active = true;
            Found = false;
            Rays = Columns = Ticks = 0;
            Ms = WorstMs = 0;
            Spot = c.ToLocal(fromWorld);
            _run = Run(this, w, c, Spot, height);
        }

        /// <summary>Works on for up to <paramref name="budgetMs"/>; true when it is over (<see cref="Found"/> says how).</summary>
        public bool Step(double budgetMs)
        {
            if (_run == null)
                return true;
            var start = Stopwatch.GetTimestamp();
            Ticks++;
            var done = false;
            while (true)
            {
                if (!_run.MoveNext())
                {
                    done = true;
                    break;
                }
                if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= budgetMs)
                    break;
            }
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Ms += ms;
            WorstMs = Math.Max(WorstMs, ms);
            if (done)
            {
                Active = false;
                _run = null;
            }
            return done;
        }

        public void Stop()
        {
            Active = false;
            _run = null;
        }
    }

    /// <summary>The search itself, one ray (or one room test) between yields.</summary>
    private static IEnumerator<int> Run(InteriorSearch me, ConstructWorld w, Construct c, Vector3 from, float height)
    {
        var upL = Vector3.Normalize(UpFor(c, GravityFrames.UpAt(c.ToWorld(from))));
        var e1 = Vector3.Normalize(Vector3.Cross(upL, MathF.Abs(upL.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var e2 = Vector3.Cross(upL, e1);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var k = 0; k < 8; k++)
        {
            var corner = new Vector3((k & 1) != 0 ? c.LocalMax.X : c.LocalMin.X, (k & 2) != 0 ? c.LocalMax.Y : c.LocalMin.Y, (k & 4) != 0 ? c.LocalMax.Z : c.LocalMin.Z);
            var p = new Vector3(Vector3.Dot(corner, e1), Vector3.Dot(corner, e2), Vector3.Dot(corner, upL));
            lo = Vector3.Min(lo, p);
            hi = Vector3.Max(hi, p);
        }
        var area = MathF.Max(1f, (hi.X - lo.X) * (hi.Y - lo.Y));
        var step = MathF.Max(0.75f, MathF.Sqrt(area / InteriorColumns));
        var a0 = Vector3.Dot(from, e1);
        var b0 = Vector3.Dot(from, e2);
        var h0 = Vector3.Dot(from, upL);
        var reach = (int)MathF.Ceiling(MathF.Max(MathF.Max(a0 - lo.X, hi.X - a0), MathF.Max(b0 - lo.Y, hi.Y - b0)) / step) + 1;
        float inside = float.MaxValue, open = float.MaxValue;
        Vector3 best = from, openSpot = from;
        // Ring by ring round the start, nearest first: a ring further out than the best spot inside can't beat it. First only
        // the decks near the start's height (a ray through a whole capital ship's height meets hundreds of parts), then all.
        for (var pass = 0; pass < 2 && inside == float.MaxValue; pass++)
        {
            var low = pass == 0 ? MathF.Max(lo.Z, h0 - WindowBelow) : lo.Z;
            var high = pass == 0 ? MathF.Min(hi.Z, h0 + WindowAbove) : hi.Z;
            if (pass == 1 && low >= h0 - WindowBelow && high <= h0 + WindowAbove)
                break; // (the first pass saw it all)
            for (var ring = 0; ring <= reach && me.Rays < InteriorRays; ring++)
            {
                if ((ring - 1) * step > MathF.Sqrt(inside))
                    break;
                for (var i = -ring; i <= ring && me.Rays < InteriorRays; i++)
                    for (var j = -ring; j <= ring && me.Rays < InteriorRays; j++)
                    {
                        if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring)
                            continue;
                        var a = a0 + i * step;
                        var b = b0 + j * step;
                        if (a < lo.X - 0.5f || a > hi.X + 0.5f || b < lo.Y - 0.5f || b > hi.Y + 0.5f)
                            continue;
                        me.Columns++;
                        var across = e1 * a + e2 * b;
                        // Down the column, floor after floor.
                        var top = high + 0.5f;
                        var bottom = low - 0.5f;
                        for (var guard = 0; guard < 32 && top > bottom && me.Rays < InteriorRays; guard++)
                        {
                            var upW = Vector3.Normalize(Vector3.Transform(upL, c.Rot));
                            me.Rays++;
                            var got = w.Raycast(c.ToWorld(across + upL * top), -upW, top - bottom, out var hit, only: c) && hit.Body == c;
                            var hitDist = hit.Distance;
                            var hitPart = hit.Part;
                            var normalL = got ? Vector3.Transform(hit.Normal, Quaternion.Conjugate(c.Rot)) : default;
                            yield return 0;
                            if (!got)
                                break;
                            if (hitDist < 0.02f)
                            {
                                top -= 0.3f; // (starting inside a solid box: past it a little)
                                continue;
                            }
                            var spot = across + upL * (top - hitDist + 0.02f);
                            top -= hitDist + 0.05f;
                            if (Vector3.Dot(normalL, upL) < World.MinWalkNormalY || hitPart < 0 || hitPart >= c.PartCount || c.IsRaycastWheel(hitPart))
                                continue;
                            var d = Vector3.DistanceSquared(spot, from);
                            if (d >= inside)
                                continue; // (no nearer than the best spot inside: nothing more to learn here)
                            me.Rays++;
                            upW = Vector3.Normalize(Vector3.Transform(upL, c.Rot));
                            var feetW = c.ToWorld(spot);
                            var roofed = w.Raycast(feetW + upW * 0.5f, upW, InteriorRoof, out var roof, only: c) && roof.Body == c;
                            yield return 0;
                            if (!roofed && (d >= open || inside < float.MaxValue))
                                continue;
                            var fits = FrameRoom(w, c, spot, upL, height);
                            if (fits)
                            {
                                upW = Vector3.Normalize(Vector3.Transform(upL, c.Rot));
                                fits = BodyFits(w, c, c, c.ToWorld(spot), upW, height);
                            }
                            yield return 0;
                            if (!fits)
                                continue;
                            if (roofed)
                                (inside, best) = (d, spot);
                            else
                                (open, openSpot) = (d, spot);
                        }
                    }
            }
        }
        if (inside < float.MaxValue)
        {
            (me.Found, me.Spot) = (true, best);
            yield break;
        }
        // No floor under a roof: beside the free seat nearest the start, standing; one seat a slice.
        var seats = new int[InteriorSeats];
        var dist = new float[InteriorSeats];
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.D.Role != PartRole.Seat || p.D.RemoteReceiver || p.Broken)
                continue;
            var d = Vector3.DistanceSquared(c.ToLocal(ConstructEdits.SeatFeet(c, i)), from);
            if (n == InteriorSeats && d >= dist[n - 1])
                continue;
            var k = n < InteriorSeats ? n++ : n - 1;
            for (; k > 0 && dist[k - 1] > d; k--)
                (seats[k], dist[k]) = (seats[k - 1], dist[k - 1]);
            (seats[k], dist[k]) = (i, d);
        }
        for (var k = 0; k < n; k++)
        {
            var seat = seats[k];
            if (seat >= c.PartCount)
                continue;
            var vel = SurfaceVel(c, c.Parts[seat].Seg, ConstructEdits.SeatFeet(c, seat));
            var ok = ExitDeck(w, c, seat, height, vel, out var deck, out _, out var feet) && deck == c;
            var local = c.ToLocal(feet);
            yield return 0;
            if (ok)
            {
                (me.Found, me.Spot) = (true, local);
                yield break;
            }
        }
        if (open < float.MaxValue)
            (me.Found, me.Spot) = (true, openSpot);
    }

    /// <summary>
    /// Room for a standing body as the frame's mover has it: the shapes it would gather there (the construct's boxes and its
    /// hull's skin, which stands off its rooms by up to metres: <see cref="BodyFits"/> tests the boxes only, and a spot under a
    /// bow's skin was popped up through it), against the standing capsule at <paramref name="spot"/> (rest frame).
    /// </summary>
    private static bool FrameRoom(ConstructWorld w, Construct c, Vector3 spot, Vector3 upL, float height)
    {
        var r = World.BodyRadius;
        Gather(w, c, 0, spot, upL, 0.2f);
        var shapes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(SweptMover.Instance.FrameShapes);
        var p = spot + upL * (r + 0.03f);
        var e = spot + upL * MathF.Max(r + 0.03f, height - r);
        for (var i = 0; i < shapes.Length; i++)
            if (CapsuleContact.Separation(shapes[i], p, e, r, out _, out _) < -0.01f)
                return false;
        return true;
    }
}
