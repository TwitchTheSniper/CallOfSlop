using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Carrier work (docs/sandbox/CONSTRUCTS.md, "Carrier work"), fun before realism. The pilot's one key
/// (<see cref="ConstructParts.KeyHook"/>): on a launch rail it hooks the craft on, again it fires; held by a dock or hangar bay it
/// lets go. A dock with a <c>"pull"</c> reaches out and draws a slow craft into its box; a mooring mast ropes a balloon that
/// drifts past. Server only, called from <see cref="ConstructLauncher.Step"/>, once for each part it counts. Allocates nothing.
/// </summary>
public static class ConstructCarrier
{
    public const float HookSpeed = 8f; // m/s against the deck: what a rail will hook
    public const float PullFastest = 8f, PullGrip = 30f, PullStart = 15f; // a dock's tractor: how fast it draws, how firmly (1/s), and what it won't take on
    public const float SnapReach = 1.2f; // across a pull dock's box: how near its middle a craft is held
    public const uint AwayTicks = 300; // after a dock lets go it leaves that craft alone this long
    public const float MooringSpeed = 8f; // m/s against the mast: what a mast ties
    public const ushort MooringAway = 50; // looks (ten a second) after casting off before it ties again

    private static readonly List<Construct> _near = new(64);

    /// <summary>The pilot's key, pressed in <paramref name="c"/>.</summary>
    public static void PilotKey(ConstructWorld world, Construct c)
    {
        if (c.CarriedBy != 0)
        {
            LetGo(world, c);
            return;
        }
        if (!FindRail(world, c, out var ship, out var k))
        {
            c.DockAway = world.CarrierClock + AwayTicks; // (not on a rail: a clamp reaching for it lets go)
            return;
        }
        ref var p = ref ship!.Parts[k];
        if ((ushort)p.Mem == c.Id)
            p.State |= 1; // (hooked: fire)
        else if (p.Mem == 0 && p.Count == 0)
        {
            p.Mem = c.Id;
            c.ArrestFree = world.CarrierClock + 120;
            world.Events.Add($"construct {ship.Id}: the {p.D.Name} hooked construct {c.Id}");
        }
    }

    /// <summary>A dock or bay lets <paramref name="b"/> go: out along its bay's front, and it stays away a while.</summary>
    public static void LetGo(ConstructWorld world, Construct b)
    {
        if (world.Get(b.CarriedBy) is not { } c)
        {
            ConstructCargo.Release(world, b);
            return;
        }
        var part = ConstructCargo.CarriedIn(c, b);
        var eject = part >= 0 ? c.Parts[part].D.DockEject : 0;
        var dir = part >= 0 ? Front(c, part) : Vector3.Zero;
        ConstructCargo.Release(world, b);
        b.DockAway = world.CarrierClock + AwayTicks;
        b.Vel += dir * eject;
    }

    private static Vector3 Front(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out _);
        return Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, -Vector3.UnitZ), q));
    }

    /// <summary>The launch rail whose zone <paramref name="c"/> stands in, moving with it (on a client too: the seat's hint).</summary>
    public static bool FindRail(ConstructWorld world, Construct c, out Construct? ship, out int part)
    {
        ship = null;
        part = -1;
        var com = c.WorldCom;
        var near = world.Near(com, c.Radius + 40f, _near);
        for (var n = 0; n < near.Count; n++)
        {
            var s = near[n];
            if (s == c || s.LaunchCount == 0)
                continue;
            for (var k = 0; k < s.PartCount; k++)
            {
                ref var p = ref s.Parts[k];
                if (p.D.Logic != CLogic.Launch || p.Broken || c.Mass > p.D.LaunchMaxMass || !ConstructLauncher.InZone(s, k, com, 0, out _))
                    continue;
                if ((c.Vel - ConstructRide.SurfaceVel(s, p.Seg, com)).LengthSquared() > HookSpeed * HookSpeed)
                    continue;
                ship = s;
                part = k;
                return true;
            }
        }
        return false;
    }

    /// <summary>What the pilot's key does here, for the seat's line (null: nothing).</summary>
    public static string? Hint(ConstructWorld world, Construct c)
    {
        if (c.CarriedBy != 0)
            return SandboxKeys.PilotUndock;
        if (!FindRail(world, c, out var ship, out var k))
            return null;
        return ship!.Parts[k].Out > 0 && ship.Parts[k].Out < 0.5f ? SandboxKeys.PilotLaunch : SandboxKeys.PilotHook;
    }

    /// <summary>One tick of a rail holding a hooked craft: moving with the deck, slid to the rail's back end and lined up with it.</summary>
    public static void HoldOnRail(Construct c, int k, Construct b, Vector3 local, Vector3 dir, float dt)
    {
        ref var p = ref c.Parts[k];
        c.WorldFrame(p.Seg, out var q, out _);
        var turn = q * Rot24.Quat(p.Rot);
        var com = b.WorldCom;
        var surface = ConstructRide.SurfaceVel(c, p.Seg, com);
        var up = Vector3.Transform(Vector3.UnitY, turn);
        var d = p.D;
        var back = d.BoundsMax.Z - (d.BoundsMax.Z - d.BoundsMin.Z) * 0.1f;
        var slide = new Vector3(Math.Clamp(-local.X * 2f, -3f, 3f), 0, Math.Clamp((back - local.Z) * 2f, -4f, 4f));
        var height = Vector3.Dot(b.Vel - surface, up);
        b.Vel = surface + Vector3.Transform(slide, turn) + up * height;
        var nose = Vector3.Transform(-Vector3.UnitZ, b.Rot);
        b.AngVel = c.AngVel + Vector3.Cross(nose, dir) * 3f;
        b.Towed = true;
        b.Wake();
    }

    /// <summary>A dock's tractor: it picks the nearest slow craft in reach, ten times a second, and draws it to the middle of its box, upright.</summary>
    public static void Tractor(ConstructWorld world, Construct c, int k, float dt)
    {
        ref var p = ref c.Parts[k];
        var d = p.D;
        if (!ConstructCargo.IsFrozen(p) || !ConstructPower.On(c, k))
        {
            p.Mem = 0;
            return;
        }
        ConstructCargo.Box(c, k, out var centre, out var half);
        c.WorldFrame(p.Seg, out var q, out var t);
        var target = t + Vector3.Transform(centre, q);
        var reach = half.Length() + d.DockPull;
        if ((world.CarrierClock + (uint)k) % ConstructCargo.ScanEvery == 0 && p.Mem == 0)
        {
            var near = world.Near(target, reach, _near);
            var best = float.MaxValue;
            for (var n = 0; n < near.Count; n++)
            {
                var b = near[n];
                if (b.CarriedBy != 0 || b.DriveSeat < 0 && b.ThrusterCount == 0 || world.CarrierClock < b.DockAway || !ConstructCargo.Catchable(world, c, b, d))
                    continue;
                var gap = Vector3.DistanceSquared(b.WorldCom, target);
                if (gap < best && gap < reach * reach && (b.Vel - c.PointVel(b.WorldCom)).LengthSquared() < PullStart * PullStart)
                {
                    best = gap;
                    p.Mem = b.Id;
                }
            }
        }
        if (p.Mem == 0)
            return;
        if (world.Get((ushort)p.Mem) is not { } body || body.CarriedBy != 0 || world.CarrierClock < body.DockAway || !ConstructCargo.Catchable(world, c, body, d)
            || Vector3.DistanceSquared(body.WorldCom, target) > reach * reach * 1.7f)
        {
            p.Mem = 0;
            return;
        }
        var at = body.WorldCom;
        var err = target - at;
        var size = err.Length();
        var want = size > 0.01f ? err / size * MathF.Min(size * 1.2f, PullFastest) : Vector3.Zero;
        var rel = body.Vel - c.PointVel(at);
        var dv = (want - rel) * MathF.Min(1f, PullGrip * dt); // (firm: a stabilizer holding station doesn't win)
        body.Wake();
        body.Vel += dv;
        // Upright with the ship, and it stops turning; its heading stays its own.
        var upB = Vector3.Transform(Vector3.UnitY, body.Rot);
        var upC = Vector3.Transform(Vector3.UnitY, q);
        body.AngVel += (c.AngVel + Vector3.Cross(upB, upC) * 2f - body.AngVel) * MathF.Min(1f, 4f * dt);
        ConstructLauncher.Push(c, at, -dv * body.Mass);
    }

    /// <summary>Is <paramref name="b"/> where a pull dock holds a craft: over the middle of its box (the height is what the craft's gear makes it)?</summary>
    public static bool Snapped(Construct c, int part, Construct b)
    {
        ConstructCargo.Box(c, part, out var centre, out _);
        var local = ConstructCargo.ToRest(c, part, b.WorldCom) - centre;
        var up = Rot24.Apply(c.Parts[part].Rot, Vector3.UnitY);
        local -= up * Vector3.Dot(local, up);
        return local.LengthSquared() < SnapReach * SnapReach;
    }

    /// <summary>A mooring mast, ten times a second: on, it ropes the nearest balloon in reach; off, it casts its rope off.</summary>
    public static void Mooring(ConstructWorld world, Construct c, int k)
    {
        ref var p = ref c.Parts[k];
        if ((world.CarrierClock + (uint)k) % ConstructCargo.ScanEvery != 0)
            return;
        var d = p.D;
        var on = !p.Broken && p.Out >= 0.5f;
        if (p.Mem != 0)
        {
            var i = world.ConnectorIndex((ushort)p.Mem);
            if (i < 0)
                p.Mem = 0;
            else if (!on)
            {
                world.RemoveConnector(i);
                p.Mem = 0;
                p.Count = MooringAway;
            }
            return;
        }
        if (p.Count > 0)
        {
            p.Count--;
            return;
        }
        if (!on)
            return;
        c.WorldFrame(p.Seg, out var q, out var t);
        var topLocal = new Vector3(0, d.BoundsMax.Y, 0);
        var top = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, topLocal), q);
        var near = world.Near(top, d.MastReach, _near);
        Construct? balloon = null;
        var best = d.MastReach * d.MastReach;
        var bestPart = -1;
        var bestAt = Vector3.Zero;
        for (var n = 0; n < near.Count; n++)
        {
            var b = near[n];
            if (b == c || b.CellCount == 0 || b.Anchored || b.CarriedBy != 0 || (b.Vel - c.PointVel(b.WorldCom)).LengthSquared() > MooringSpeed * MooringSpeed)
                continue;
            for (var i = 0; i < b.PartCount; i++)
            {
                b.WorldFrame(b.Parts[i].Seg, out var bq, out var bt);
                b.PartBounds(i, out var mn, out var mx);
                var rest = Vector3.Transform(top - bt, Quaternion.Conjugate(bq));
                var on1 = Vector3.Clamp(rest, mn, mx);
                var gap = Vector3.DistanceSquared(rest, on1);
                if (gap < best)
                {
                    best = gap;
                    balloon = b;
                    bestPart = i;
                    bestAt = bt + Vector3.Transform(on1, bq);
                }
            }
        }
        if (balloon == null)
            return;
        var a = new ConnectorEnd { Body = c.Id, Part = (ushort)k, Local = topLocal };
        var e = new ConnectorEnd { Body = balloon.Id, Part = (ushort)bestPart, Local = ConstructCargo.ToPart(balloon, bestPart, bestAt) };
        if (world.CheckConnector(a, e) != null)
            return;
        var at = world.AddConnector(World.Query, ConnectorKind.Rope, d.MastRope, a, e);
        if (at >= 0)
        {
            p.Mem = world.Connectors[at].Id;
            world.Events.Add($"construct {c.Id}: the {d.Name} moored construct {balloon.Id}");
        }
    }
}
