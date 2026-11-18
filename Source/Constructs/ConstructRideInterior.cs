using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Noclip boarding (creative Sandbox; docs/sandbox/constructs/overview-and-playing.md "Noclip"): a flyer who leaves noclip in a
/// construct's frame, or who boards one with the Board inside key (<see cref="InputCmd.NoclipBoard"/>), is set down inside its
/// hull, not pushed up out of it onto the roof. The search runs in the construct's rest frame, along its rider's up, so a ship
/// in a banking turn or in orbit is the same search as a parked truck, on the server, in a client's prediction and its replays.
/// </summary>
public static partial class ConstructRide
{
    /// <summary>Columns looked down at most per search, and rays cast at most (a capital ship spreads its columns wider).</summary>
    public const int InteriorColumns = 900, InteriorRays = 2400;

    /// <summary>A floor counts as inside when the construct is over it within this (m): a deck under its own roof.</summary>
    public const float InteriorRoof = 8f;

    /// <summary>Seats looked at for a standing spot beside them, when no floor under a roof is found.</summary>
    private const int InteriorSeats = 24;

    // The last search (replays of the same input ask again): body, start and shape, and the spot found, in the rest frame.
    private static ushort _inBody;
    private static int _inParts;
    private static Vector3 _inFrom, _inSpot;
    private static bool _inFound;

    /// <summary>
    /// A place to stand inside construct <paramref name="c"/> nearest world point <paramref name="from"/>: a walkable floor
    /// with room for the body under the construct's own roof, else a spot beside one of its seats, else the nearest floor of
    /// it with headroom (an open truck bed). False: nowhere on it a body fits.
    /// </summary>
    public static bool InteriorSpot(ConstructWorld w, Construct c, Vector3 from, float height, out Vector3 feet)
    {
        if (c.Dirty)
            c.Rebuild();
        var local = c.ToLocal(from);
        if (_inBody == c.Id && _inParts == c.PartCount && Vector3.DistanceSquared(_inFrom, local) < 1e-4f)
        {
            feet = c.ToWorld(_inSpot);
            return _inFound;
        }
        _inFound = Search(w, c, local, height, out var spot);
        (_inBody, _inParts, _inFrom, _inSpot) = (c.Id, c.PartCount, local, spot);
        feet = c.ToWorld(spot);
        return _inFound;
    }

    private static bool Search(ConstructWorld w, Construct c, Vector3 from, float height, out Vector3 best)
    {
        best = from;
        var upL = Vector3.Normalize(UpFor(c, GravityFrames.UpAt(c.ToWorld(from))));
        var upW = Vector3.Normalize(Vector3.Transform(upL, c.Rot));
        var e1 = Vector3.Normalize(Vector3.Cross(upL, MathF.Abs(upL.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var e2 = Vector3.Cross(upL, e1);
        // The bounds along the three directions.
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
        var reach = (int)MathF.Ceiling(MathF.Max(MathF.Max(a0 - lo.X, hi.X - a0), MathF.Max(b0 - lo.Y, hi.Y - b0)) / step) + 1;
        float inside = float.MaxValue, open = float.MaxValue;
        var openSpot = from;
        var rays = 0;
        // Ring by ring round the start, nearest first: a ring further out than the best spot inside can't beat it.
        for (var ring = 0; ring <= reach && rays < InteriorRays; ring++)
        {
            if ((ring - 1) * step > MathF.Sqrt(inside))
                break;
            for (var i = -ring; i <= ring && rays < InteriorRays; i++)
                for (var j = -ring; j <= ring && rays < InteriorRays; j++)
                {
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring)
                        continue;
                    var a = a0 + i * step;
                    var b = b0 + j * step;
                    if (a < lo.X - 0.5f || a > hi.X + 0.5f || b < lo.Y - 0.5f || b > hi.Y + 0.5f)
                        continue;
                    Column(w, c, e1 * a + e2 * b, upL, upW, lo.Z, hi.Z, from, height, ref rays, ref inside, ref best, ref open, ref openSpot);
                }
        }
        if (inside < float.MaxValue)
            return true;
        // No floor under a roof: beside the seat nearest the start, standing.
        if (SeatSpot(w, c, from, height, out var seatSpot))
        {
            best = seatSpot;
            return true;
        }
        if (open < float.MaxValue)
        {
            best = openSpot;
            return true;
        }
        return false;
    }

    /// <summary>Down one column of the construct, floor after floor: the nearest one with room under a roof, and the nearest with room at all.</summary>
    private static void Column(ConstructWorld w, Construct c, Vector3 across, Vector3 upL, Vector3 upW, float low, float high, Vector3 from, float height,
        ref int rays, ref float inside, ref Vector3 best, ref float open, ref Vector3 openSpot)
    {
        var top = high + 0.5f;
        var bottom = low - 0.5f;
        for (var guard = 0; guard < 32 && top > bottom && rays < InteriorRays; guard++)
        {
            var origin = c.ToWorld(across + upL * top);
            rays++;
            if (!w.Raycast(origin, -upW, top - bottom, out var hit, only: c) || hit.Body != c)
                return;
            if (hit.Distance < 0.02f)
            {
                top -= 0.3f; // (starting inside a solid box: past it a little)
                continue;
            }
            top -= hit.Distance + 0.05f;
            if (Vector3.Dot(hit.Normal, upW) < World.MinWalkNormalY || hit.Part < 0 || hit.Part >= c.PartCount || c.IsRaycastWheel(hit.Part))
                continue;
            var spot = c.ToLocal(hit.Point + upW * 0.02f);
            var d = Vector3.DistanceSquared(spot, from);
            if (d >= inside && d >= open)
                continue;
            var feetW = c.ToWorld(spot);
            if (!BodyFits(w, c, c, feetW, upW, height))
                continue;
            rays++;
            var roofed = w.Raycast(feetW + upW * 0.5f, upW, InteriorRoof, out var roof, only: c) && roof.Body == c;
            if (roofed && d < inside)
                (inside, best) = (d, spot);
            else if (!roofed && d < open)
                (open, openSpot) = (d, spot);
        }
    }

    /// <summary>A spot to stand beside the free seat of <paramref name="c"/> nearest <paramref name="from"/> (rest frame), on the construct itself.</summary>
    private static bool SeatSpot(ConstructWorld w, Construct c, Vector3 from, float height, out Vector3 spot)
    {
        spot = from;
        Span<int> seats = stackalloc int[InteriorSeats];
        Span<float> dist = stackalloc float[InteriorSeats];
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.D.Role != PartRole.Seat || p.D.RemoteReceiver || p.Broken)
                continue;
            var d = Vector3.DistanceSquared(c.ToLocal(ConstructEdits.SeatFeet(c, i)), from);
            var k = n < InteriorSeats ? n++ : InteriorSeats - 1;
            if (k == InteriorSeats - 1 && n == InteriorSeats && d >= dist[k])
                continue;
            for (; k > 0 && dist[k - 1] > d; k--)
            {
                seats[k] = seats[k - 1];
                dist[k] = dist[k - 1];
            }
            seats[k] = i;
            dist[k] = d;
        }
        for (var k = 0; k < n; k++)
        {
            var seat = seats[k];
            var vel = SurfaceVel(c, c.Parts[seat].Seg, ConstructEdits.SeatFeet(c, seat));
            if (ExitDeck(w, c, seat, height, vel, out var deck, out _, out var feet) && deck == c)
            {
                spot = c.ToLocal(feet);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Shared step: a noclip flyer leaving it with the Board inside key on construct <paramref name="body"/> goes into its frame
    /// at <paramref name="at"/> (where they aimed, in its rest frame), for <see cref="NoclipInside"/> to set them down inside.
    /// False: no such construct here.
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

    /// <summary>
    /// Someone just left noclip in a construct's frame (<see cref="NoclipMove.Leave"/>): set down on the nearest floor inside its
    /// hull (<see cref="InteriorSpot"/>), standing still in its frame. False: not in a frame, or nowhere on it to stand.
    /// </summary>
    public static bool NoclipInside(ref MoveState s, ConstructWorld w)
    {
        if (s.RideUp == Vector3.Zero || s.RideBody == 0 || w.Get(s.RideBody) is not { } c || s.RideSeg > c.JointCount)
            return false;
        if (!InteriorSpot(w, c, s.Position, World.HeightOf(Stance.Stand), out var feet))
            return false;
        s.RideSeg = 0;
        s.RideOn = 0;
        s.RideUp = UpFor(c, GravityFrames.UpAt(feet));
        s.RideLocal = c.ToLocal(feet);
        s.RideVel = Vector3.Zero;
        s.RideAir = s.RideClimb = s.RideFree = s.RideLadder = 0;
        s.Stance = Stance.Stand;
        s.Grounded = true;
        Place(ref s, c);
        w.Events.Add($"a noclip flyer was set down inside construct {c.Id} at {s.RideLocal:0.0}");
        return true;
    }
}
