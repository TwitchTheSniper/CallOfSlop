using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Catapults and arrestor gear (docs/sandbox/CONSTRUCTS.md, "Large systems"): a logic part with a working zone over its
/// length (its bounds along its z, its "launch" block's width and height above its top). A catapult fires on a rising
/// input or F: the free body in the zone (a docked one is let go first) is pushed along the rail (the part's -z, its
/// front) at the block's acceleration until its centre of mass leaves the zone; the ship takes the reaction, and an
/// electrical ship pays the energy from its batteries (flat: the shot stops). Arrestor gear, while on (unwired: always),
/// slows whatever moves through its zone against the ship, at most at its acceleration, until it moves with the deck.
/// Server only, between the logic and the physics. Allocates nothing.
/// </summary>
public static class ConstructLauncher
{
    public const float SettleSpeed = 3f; // m/s against the deck: what a catapult takes from rest, and arrestor gear leaves alone
    public const float Efficiency = 0.8f; // a catapult draws its work / this

    private static readonly List<Construct> _near = new(64);

    /// <summary>Is the world point in part <paramref name="part"/>'s working zone? <paramref name="local"/>: the point in the part's frame.</summary>
    public static bool InZone(Construct c, int part, Vector3 world, float reach, out Vector3 local)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        c.WorldFrame(p.Seg, out var q, out var t);
        var rest = Vector3.Transform(world - t, Quaternion.Conjugate(q));
        local = Vector3.Transform(rest - p.Pos, Quaternion.Conjugate(Rot24.Quat(p.Rot)));
        return MathF.Abs(local.X) <= d.LaunchWidth * 0.5f && local.Y >= d.BoundsMax.Y - 0.5f && local.Y <= d.BoundsMax.Y + d.LaunchHeight
               && local.Z >= d.BoundsMin.Z - reach && local.Z <= d.BoundsMax.Z + reach;
    }

    private static bool Takes(Construct c, Construct b, float maxMass) =>
        b != c && !b.Anchored && !b.Frozen && !b.Pinned && b.GrabbedBy == 255 && b.PartCount > 0 && b.Mass <= maxMass && (b.CarriedBy == 0 || b.CarriedBy == c.Id);

    /// <summary>One tick of every catapult and arrestor.</summary>
    public static void Step(ConstructWorld world, float dt)
    {
        world.CarrierClock++;
        var bodies = world.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            if (c.LaunchCount == 0)
                continue;
            for (var k = 0; k < c.PartCount; k++)
            {
                var d = c.Parts[k].D;
                if (d.Logic == CLogic.Launch)
                    Catapult(world, c, k, dt);
                else if (d.Logic == CLogic.Arrest)
                    Arrest(world, c, k, dt);
                else if (d.Logic == CLogic.Mooring)
                    ConstructCarrier.Mooring(world, c, k);
                else if (d.DockPull > 0)
                    ConstructCarrier.Tractor(world, c, k, dt);
            }
        }
    }

    private static void Catapult(ConstructWorld world, Construct c, int k, float dt)
    {
        ref var p = ref c.Parts[k];
        var d = p.D;
        if (p.Broken || !ConstructPower.On(c, k))
        {
            p.Count = 0;
            p.Mem = 0;
            p.State = (byte)(p.State & ~1);
            return;
        }
        c.WorldFrame(p.Seg, out var q, out _);
        var dir = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, -Vector3.UnitZ), q));
        // Hooked by its pilot (ConstructCarrier.cs): it waits on the rail for a press.
        if (p.Mem != 0)
        {
            if (world.Get((ushort)p.Mem) is not { } held || !Takes(c, held, d.LaunchMaxMass) || held.CarriedBy != 0 || !InZone(c, k, held.WorldCom, 0, out var local))
                p.Mem = 0;
            else if ((p.State & 1) != 0)
            {
                p.State = (byte)(p.State & ~1);
                p.Count = held.Id;
                p.Mem = 0;
                held.ArrestFree = world.CarrierClock + 120;
                world.Events.Add($"construct {c.Id}: the {d.Name} fired construct {held.Id} ({held.Mass:0} kg)");
            }
            else
            {
                ConstructCarrier.HoldOnRail(c, k, held, local, dir, dt);
                return;
            }
        }
        if ((p.State & 1) != 0)
        {
            p.State = (byte)(p.State & ~1);
            if (p.Count == 0)
            {
                var near = world.Near(ConstructRender.PartCentre(c, k), MathF.Max(c.DefOf(k).BoundsMax.Z - c.DefOf(k).BoundsMin.Z, d.LaunchWidth), _near);
                for (var n = 0; n < near.Count; n++)
                {
                    var b = near[n];
                    if (!Takes(c, b, d.LaunchMaxMass) || !InZone(c, k, b.WorldCom, 0, out _))
                        continue;
                    if (b.CarriedBy == 0 && (b.Vel - ConstructRide.SurfaceVel(c, p.Seg, b.WorldCom)).LengthSquared() > SettleSpeed * SettleSpeed)
                        continue;
                    if (b.CarriedBy != 0)
                        ConstructCargo.Release(world, b);
                    p.Count = b.Id;
                    b.ArrestFree = world.CarrierClock + 120;
                    world.Events.Add($"construct {c.Id}: the {d.Name} fired construct {b.Id} ({b.Mass:0} kg)");
                    break;
                }
            }
        }
        if (p.Count == 0)
            return;
        var body = world.Get(p.Count);
        if (body == null || !Takes(c, body, d.LaunchMaxMass) || !InZone(c, k, body.WorldCom, 0, out _))
        {
            p.Count = 0;
            return;
        }
        if (body.CarriedBy != 0)
            ConstructCargo.Release(world, body); // (a dock that caught it again in the first ticks of the shot: the shot wins)
        var dv = d.LaunchAccel * dt;
        var v = Vector3.Dot(body.Vel, dir);
        if (d.LaunchSpeed > 0)
        {
            // Off the rail at its speed over the deck, so a ship under way adds its own.
            var over = d.LaunchSpeed - Vector3.Dot(body.Vel - ConstructRide.SurfaceVel(c, p.Seg, body.WorldCom), dir);
            if (over <= 0)
            {
                p.Count = 0;
                return;
            }
            dv = MathF.Min(dv, over);
        }
        if (c.Electrical)
        {
            var kj = 0.5f * body.Mass * MathF.Abs((v + dv) * (v + dv) - v * v) / 1000f / Efficiency;
            if (c.Charge < kj)
            {
                p.Count = 0; // (the capacitors are flat: it runs on as it is)
                return;
            }
            c.Charge -= kj;
        }
        body.Wake();
        body.Vel += dir * dv;
        body.Towed = true; // (its brakes and parked wheels don't hold it)
        Push(c, body.WorldCom, -dir * dv * body.Mass);
    }

    private static void Arrest(ConstructWorld world, Construct c, int k, float dt)
    {
        ref var p = ref c.Parts[k];
        var d = p.D;
        if (p.Broken || p.Out < 0.5f)
            return;
        var near = world.Near(ConstructRender.PartCentre(c, k), MathF.Max(c.DefOf(k).BoundsMax.Z - c.DefOf(k).BoundsMin.Z + d.LaunchReach * 2, d.LaunchWidth), _near);
        for (var n = 0; n < near.Count; n++)
        {
            var b = near[n];
            if (b.CarriedBy != 0 || !Takes(c, b, d.LaunchMaxMass) || !InZone(c, k, b.WorldCom, d.LaunchReach, out _) || world.CarrierClock < b.ArrestFree)
                continue;
            var rel = b.Vel - ConstructRide.SurfaceVel(c, c.Parts[k].Seg, b.WorldCom);
            var speed = rel.Length();
            if (speed <= SettleSpeed)
            {
                b.ArrestFree = world.CarrierClock + 180; // (caught: now it may go round again under its own power)
                continue;
            }
            var dv = MathF.Min(d.LaunchAccel * dt, speed - SettleSpeed);
            b.Wake();
            b.Vel -= rel / speed * dv;
            if (d.LaunchReach > 0)
                b.AngVel += (c.AngVel - b.AngVel) * MathF.Min(1f, 4f * dt); // (a clumsy landing doesn't tumble)
            Push(c, b.WorldCom, rel / speed * (dv * b.Mass));
        }
    }

    /// <summary>The reaction on a ship that moves (an impulse at a world point).</summary>
    public static void Push(Construct c, Vector3 at, Vector3 j)
    {
        if (c.Anchored || c.CarriedBy != 0 || c.InvMass <= 0)
            return;
        c.Wake();
        c.Vel += j * c.InvMass;
        c.AngVel += Construct.Mul(c.InvInertiaWorld(), Vector3.Cross(at - c.WorldCom, j));
    }
}
