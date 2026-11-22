using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Landing gear suspension (Karl, 2026-11-19: "add suspension to the landing gear"). A landing leg pod's foot with a
/// <c>"spring"</c> in its door block (<see cref="CDoorDef.SpringTravel"/>), lowered and settled, stands on a raycast spring
/// and damper as a spring foot does (<see cref="SpringFeet"/>): a ray down from its sole's middle, raised by the travel, and
/// the spring takes the craft's weight as the pad gives. The pad's solid boxes meet the ground only past the travel
/// (<see cref="ConstructDoors.FootGive"/>, in the contacts), so a hard landing bottoms out on them. The spring is tuned by
/// frequency for the craft's own mass (each foot its share), so every craft sags the same share of its travel at 1 g:
/// heavy or light, the legs settle and the pads look planted. What is drawn: the foot's "oleo" boxes rise by the squash
/// (<see cref="CDoor.Shown"/>, from <see cref="FitGear"/> on every peer: nothing new on the wire).
/// </summary>
public static partial class ConstructPhysics
{
    /// <summary>Grip of a sprung pad along the ground, as a share of its load (a rubber sole on dirt).</summary>
    private const float PadGrip = 0.8f;

    /// <summary>A sprung foot's sole in the rest frame: the middle of its pad's underside.</summary>
    private static Vector3 Sole(Construct c, int part)
    {
        c.PartBounds(part, out var mn, out var mx);
        return c.ToRest(c.Parts[part].Seg, new Vector3((mn.X + mx.X) * 0.5f, mn.Y, (mn.Z + mx.Z) * 0.5f)); // (a foot on a freeform weld: from its weld's frame)
    }

    /// <summary>Server, each tick of a moving body with sprung landing feet: each lowered foot's spring on what is under it.</summary>
    private static void GearFeet(Construct c, IWorldQuery query, in Matrix4x4 invI, float dt)
    {
        if (!c.EnsureDoors())
            return;
        var feet = 0;
        var touching = 0;
        for (var k = 0; k < c.DoorCount; k++)
            if (ConstructDoors.FootGive(c, c.Doors[k].Part) > 0)
            {
                feet++;
                if (c.Doors[k].Touch)
                    touching++;
            }
        if (feet == 0)
            return;
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        var com = c.WorldCom;
        Span<float> shares = stackalloc float[c.DoorCount]; // (at most Construct.MaxDoors: 4 KB)
        FootShares(c, shares);
        for (var k = 0; k < c.DoorCount; k++)
        {
            ref var dr = ref c.Doors[k];
            var part = dr.Part;
            var travel = ConstructDoors.FootGive(c, part);
            if (travel <= 0)
            {
                dr.Touch = false;
                dr.Squash = dr.PrevSquash = 0;
                continue;
            }
            var dd = c.Parts[part].D.Door!;
            var share = k < shares.Length && shares[k] > 0 ? shares[k] : c.Mass / feet;
            var origin = c.ToWorld(Sole(c, part)) + up * (travel + 0.02f);
            LastRays++;
            dr.PrevSquash = dr.Squash;
            _rayFrom = part;
            if (!GroundRay(c, query, origin, -up, travel + 0.02f, out var hit, out var deck, out var deckSeg))
            {
                dr.Touch = false;
                dr.Squash = 0;
                continue;
            }
            var was = dr.Touch;
            dr.Touch = true;
            var x = Math.Clamp(travel + 0.02f - hit.Distance, 0, travel);
            dr.Squash = x;
            if (!was)
                dr.PrevSquash = x; // (first touch: no damping kick)
            var omega = MathF.Tau * dd.SpringHz;
            var ks = share * omega * omega;
            var cd = 2 * dd.SpringDamp * MathF.Sqrt(ks * share);
            var force = MathF.Max(0, ks * x + cd * (dr.Squash - dr.PrevSquash) / dt);
            var point = origin - up * hit.Distance;
            var r = point - com;
            var jn = force * dt;
            var deckVel = deck != null ? DeckVel(deck, deckSeg, point) : Vector3.Zero;
            if (x >= travel)
            {
                var vn = Vector3.Dot(c.PointVel(point) - deckVel, up);
                if (vn < 0)
                    jn += -vn / InvMassAt(c, invI, r, up); // (bottomed out: a hard stop; the pad's boxes are just under the ground too)
            }
            ApplyImpulse(c, invI, r, up * jn);
            if (deck != null)
                DeckPush(deck, deckSeg, point, up * jn);
            // The pad's grip: what slides along the ground is stopped (its share of it), up to what the load allows.
            var vp = c.PointVel(point) - deckVel;
            var vt = vp - hit.Normal * Vector3.Dot(vp, hit.Normal);
            var speed = vt.Length();
            if (speed > 1e-4f && jn > 0)
            {
                var dir = vt / speed;
                var jt = MathF.Min(speed / InvMassAt(c, invI, r, dir) / MathF.Max(1, touching), jn * PadGrip);
                ApplyImpulse(c, invI, r, -dir * jt);
                if (deck != null)
                    DeckPush(deck, deckSeg, point, -dir * jt);
            }
        }
    }

    /// <summary>
    /// Each lowered sprung foot's share of the craft's mass as it stands (by door, 0 for the rest): a plane through the feet's
    /// places that holds the centre of mass up, as the wheels' resting shares are (<see cref="RestShares"/>), so a craft heavy at
    /// one end sinks as far on its feet there as at the other and stands level, each spring its sag in.
    /// </summary>
    private static void FootShares(Construct c, Span<float> shares)
    {
        shares.Clear();
        float mx = 0, mz = 0;
        var n = 0;
        for (var k = 0; k < shares.Length; k++)
            if (ConstructDoors.FootGive(c, c.Doors[k].Part) > 0)
            {
                var p = Sole(c, c.Doors[k].Part);
                mx += p.X;
                mz += p.Z;
                n++;
            }
        if (n == 0)
            return;
        mx /= n;
        mz /= n;
        float sxx = 0, sxz = 0, szz = 0;
        for (var k = 0; k < shares.Length; k++)
            if (ConstructDoors.FootGive(c, c.Doors[k].Part) > 0)
            {
                var p = Sole(c, c.Doors[k].Part);
                sxx += (p.X - mx) * (p.X - mx);
                sxz += (p.X - mx) * (p.Z - mz);
                szz += (p.Z - mz) * (p.Z - mz);
            }
        var cx = c.Com.X - mx;
        var cz = c.Com.Z - mz;
        var tr = sxx + szz;
        var det = sxx * szz - sxz * sxz;
        float ax = 0, az = 0;
        if (det > 1e-4f * tr * tr && det > 0)
        {
            ax = (szz * cx - sxz * cz) / det;
            az = (sxx * cz - sxz * cx) / det;
        }
        else if (tr > 1e-6f)
        {
            ax = (sxx * cx + sxz * cz) / (tr * tr);
            az = (sxz * cx + szz * cz) / (tr * tr);
        }
        var sum = 0f;
        for (var k = 0; k < shares.Length; k++)
            if (ConstructDoors.FootGive(c, c.Doors[k].Part) > 0)
            {
                var p = Sole(c, c.Doors[k].Part);
                shares[k] = MathF.Max(0.05f / n, 1f / n + ax * (p.X - mx) + az * (p.Z - mz)); // (a foot the centre of mass lies far beyond still has a little spring)
                sum += shares[k];
            }
        var scale = c.Mass / sum;
        for (var k = 0; k < shares.Length; k++)
            shares[k] *= scale;
    }

    /// <summary>
    /// Every peer, as it draws (with the wheels: ConstructRender.FitWheels): how far each lowered sprung foot is pressed in, from a
    /// ray to the ground under it, eased so a client's few poses a second don't make it jump. The host's drawing uses it too.
    /// </summary>
    public static void FitGear(ConstructWorld world, Construct c, IWorldQuery query, Vector3 up)
    {
        if (c.GearFootCount == 0 || c.DoorCount == 0)
            return;
        for (var k = 0; k < c.DoorCount; k++)
        {
            ref var dr = ref c.Doors[k];
            var part = dr.Part;
            if ((uint)part >= (uint)c.PartCount)
                continue;
            var travel = ConstructDoors.FootGive(c, part);
            if (travel <= 0)
            {
                dr.Shown = 0;
                continue;
            }
            var origin = c.ToWorld(Sole(c, part)) + up * (travel + 0.02f);
            var want = 0f;
            if (query.Raycast(origin, -up, travel + 0.02f, QueryFilter.All, out var hit))
                want = Math.Clamp(travel + 0.02f - hit.Distance, 0, travel);
            else if (DeckUnder(world, c, origin, -up, travel + 0.02f) is { } d)
                want = Math.Clamp(travel + 0.02f - d, 0, travel); // (a station's or a carrier's deck: the map's ray doesn't see bodies)
            dr.Shown = float.Lerp(dr.Shown, want, 0.5f);
        }
    }
}
