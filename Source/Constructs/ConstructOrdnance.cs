using System.Numerics;

namespace CallOfSlop;

/// <summary>What a part bolted to a gun adds to it ("sight" in a part file).</summary>
public enum SightKind : byte
{
    None,
    Lead, // the gunner looks at a body and the gun leads it; its shells burst on the way there
    Seeker, // a missile gun's own eye: it locks what is ahead of the gun, with no gunner
}

/// <summary>A Bomb Rack (with the construct's caches) and how many bombs it holds.</summary>
public struct CRack
{
    public ushort Part;
    public byte Held;
}

public sealed partial class Construct
{
    public CRack[] Racks = [];
    public int RackCount;
}

/// <summary>
/// Air weapons on the shell pool (docs/sandbox/CONSTRUCTS.md, "Air weapons"): bombs let go from a Bomb Rack, shells with a proximity
/// fuse, rockets that push themselves, missiles that turn, flares that draw them. Everything flies in <see cref="ConstructWorld.Shells"/>
/// and goes off through the same blast walk, so a busy sky costs what <see cref="ConstructShells"/> says plus the fixed work here. Nothing
/// allocates in a tick: a bomb's release is the one costly thing, a part coming off, and it happens at most once a body a tick.
/// </summary>
public static class ConstructOrdnance
{
    public const int MaxHeld = 16; // bombs a rack lets go of in turn
    public const int RackInterval = World.TickRate / 3; // ticks between bombs while its input stays on
    public const int RackRetry = World.TickRate / 6; // ticks before it looks again at a blocked way down
    public const float DropClear = 8f; // m under a bomb that must be free of its own construct
    public const int BombClear = World.TickRate; // ticks a bomb passes its own construct
    public const float LookReach = 1500f; // m a gunner's look locks a missile out to, with no sight
    public const float LookCone = 6f; // degrees a target stays locked off the look
    public const float MissileCoast = 6f; // seconds a missile flies on after its burn before it goes off
    public const float FlyingSpeed = 3f; // m/s: a proximity fuse takes what moves at this or faster for something flying
    public const float FlareDrag = 1.5f; // a flare's drag, a second: it hangs and sinks
    public const float FlareReach = 300f; // m a missile sees a flare from
    public const float FlareCone = 0.5f; // the cosine of what a missile's eye takes in
    public const int FlareChance = 65; // % of flares a missile falls for (each pair decides once)
    public const int FlareLook = 12; // ticks between a missile's looks for flares

    // ---- Bomb racks ------------------------------------------------------------------------------------------------

    /// <summary>With the caches: the racks and what each holds.</summary>
    public static void Build(Construct c)
    {
        c.RackCount = 0;
        Span<ushort> held = stackalloc ushort[MaxHeld];
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].D.Logic != CLogic.BombRack)
                continue;
            if (c.Racks.Length == c.RackCount)
                Array.Resize(ref c.Racks, Math.Max(4, c.RackCount * 2));
            c.Racks[c.RackCount++] = new CRack { Part = (ushort)i, Held = (byte)Held(c, i, held) };
        }
    }

    /// <summary>The working explosive parts bolted or welded to a rack.</summary>
    public static int Held(Construct c, int rack, Span<ushort> into)
    {
        var n = 0;
        for (var e = c.FirstLink(rack); e >= 0 && n < into.Length; e = c.NextLink(e))
        {
            var o = c.OtherEnd(e);
            if (c.Parts[o].Working && c.DefOf(o).Explosive != null)
                into[n++] = (ushort)o;
        }
        for (var j = 0; j < c.JointCount && n < into.Length; j++)
        {
            ref var jt = ref c.Joints[j];
            if (!jt.Freeform || jt.Cut)
                continue;
            var o = jt.Hinge == rack ? jt.Child : jt.Child == rack ? jt.Hinge : -1;
            if (o >= 0 && o < c.PartCount && c.Parts[o].Working && c.DefOf(o).Explosive != null)
                into[n++] = (ushort)o;
        }
        return n;
    }

    /// <summary>Server: a rack's tick. Its input on, it asks to let one go; bodies change after the logic step. Puts out how many it holds.</summary>
    public static float StepRack(ConstructWorld world, Construct c, int part, ref CPart p, bool on)
    {
        if (p.Cool > 0)
            p.Cool--;
        if (on && p.Cool == 0 && world.ShellRoom && world.Drops.Count < world.Drops.Capacity)
            world.Drops.Add((c.Id, (ushort)part));
        for (var k = 0; k < c.RackCount; k++)
            if (c.Racks[k].Part == part)
                return c.Racks[k].Held;
        return 0;
    }

    /// <summary>The racks that asked this tick let go. One bomb a body a tick: the next rack's numbers aren't good after a part comes off, and it asks again.</summary>
    public static void Release(ConstructWorld world)
    {
        if (world.Drops.Count == 0)
            return;
        ushort done = 0;
        foreach (var (body, part) in world.Drops)
        {
            if (body == done || world.Get(body) is not { } c)
                continue;
            if (c.Dirty)
                c.Rebuild();
            if (part >= c.PartCount || c.Parts[part].D.Logic != CLogic.BombRack || !c.Parts[part].Working)
                continue;
            if (Drop(world, c, part))
                done = body;
        }
        world.Drops.Clear();
    }

    private static bool Drop(ConstructWorld world, Construct c, int rack)
    {
        Span<ushort> held = stackalloc ushort[MaxHeld];
        var n = Held(c, rack, held);
        for (var k = 0; k < n; k++)
        {
            int bomb = held[k];
            if (Blocked(world, c, bomb, out _))
                continue;
            var def = c.DefOf(bomb);
            var ex = def.Explosive!;
            var at = ConstructRender.PartCentre(c, bomb);
            var vel = c.PointVel(at) + c.SegPointVel(c.Parts[bomb].Seg, at);
            var fuse = Math.Max(1, (int)MathF.Round(ex.ExplodeFuse * World.TickRate));
            var id = world.AddShell(new CShell { Kind = ShellKind.Bomb, By = c.Owner, Gun = c.Id, GunPart = (ushort)rack, Def = def.Id, Pos = at, Prev = at, Tested = at, Vel = vel, Blast = ex.ExplodeRadius, Damage = ex.ExplodeDamage, Crater = ex.ExplodeCrater, Delay = fuse, Clear = BombClear });
            if (id == 0)
                return false;
            world.AddFire(new CFireEvent { Kind = CFireKind.ShellOut, Body = c.Id, Part = def.Id, Shell = id, At = at, To = vel, Muzzle = (byte)ShellKind.Bomb });
            c.Parts[rack].Cool = RackInterval;
            world.RemovePart(c, bomb);
            return true;
        }
        if (n > 0)
            c.Parts[rack].Cool = RackRetry; // (a closed door under them all: nothing happens, it looks again soon)
        return false;
    }

    /// <summary>Is the way down from a bomb shut by its own construct (a closed bay door, a hull, a wheel)? An open door has swung out of the ray.</summary>
    public static bool Blocked(ConstructWorld world, Construct c, int bomb, out ConstructPartDef? by)
    {
        by = null;
        var at = ConstructRender.PartCentre(c, bomb);
        var pull = ConstructShells.GravityAt(at);
        c.WorldFrame(c.Parts[bomb].Seg, out var q, out _);
        var down = pull.LengthSquared() > 0.01f ? Vector3.Normalize(pull) : Vector3.Transform(-Vector3.UnitY, q);
        c.PartBounds(bomb, out var mn, out var mx);
        var half = (mx - mn) * 0.5f;
        var local = Vector3.Abs(Vector3.Transform(down, Quaternion.Conjugate(q)));
        var from = at + down * (Vector3.Dot(local, half) + 0.03f);
        if (!world.Raycast(from, down, DropClear, out var hit, only: c))
            return false;
        by = hit.Body.DefOf(hit.Part);
        return true;
    }

    /// <summary>The look line's trouble with a rack, or null.</summary>
    public static string? Problem(ConstructWorld? world, Construct c, int part)
    {
        Span<ushort> held = stackalloc ushort[MaxHeld];
        var n = Held(c, part, held);
        if (n == 0)
            return "nothing on it: bolt a bomb to it";
        if (world == null)
            return null;
        ConstructPartDef? by = null;
        for (var k = 0; k < n; k++)
            if (!Blocked(world, c, held[k], out by))
                return null;
        return by == null ? null : $"the {by.Name} is in the way down";
    }

    // ---- Sights and locks ------------------------------------------------------------------------------------------

    /// <summary>With the guns: a sight bolted to this gun.</summary>
    public static void SightOn(Construct c, ref CGun g)
    {
        for (var e = c.FirstLink(g.Part); e >= 0; e = c.NextLink(e))
        {
            var o = c.OtherEnd(e);
            var d = c.DefOf(o);
            if (d.Sight == SightKind.None || !c.Parts[o].Working)
                continue;
            g.Sight = d.Sight;
            g.SightReach = d.SightReach;
            g.SightCos = MathF.Cos(d.SightCone * MathF.PI / 180f);
            g.FuseRange = ConstructConfig.Get(c.Parts[o], d, CfgKey.FuseRange, 0);
            return;
        }
    }

    /// <summary>
    /// The body the gunner has in view: the one they have, while the look stays near it, else what the look meets. One ray at most,
    /// none while a lock holds.
    /// </summary>
    public static Construct? Track(ConstructWorld world, Construct c, ref CGun g, Vector3 eye, Vector3 look, IWorldQuery query)
    {
        var reach = g.SightReach > 0 ? g.SightReach : LookReach;
        var cos = g.SightReach > 0 ? g.SightCos : MathF.Cos(LookCone * MathF.PI / 180f);
        if (g.Track != 0 && world.Get(g.Track) is { } held && held != c)
        {
            var to = held.WorldCom - eye;
            var len = to.Length();
            if (len < reach && len > 1f && Vector3.Dot(to / len, look) > cos)
                return held;
        }
        g.Track = 0;
        var d = query.Raycast(eye, look, reach, QueryFilter.Shots);
        if (world.Raycast(eye, look, d, out var hit, c) && !hit.Body.Anchored)
        {
            g.Track = hit.Body.Id;
            return hit.Body;
        }
        return null;
    }

    /// <summary>Where a lead sight's gun should point, for the gunner in <paramref name="seat"/>: ahead of what they look at by its time of flight, and how long its shells fly before they burst.</summary>
    public static Vector3 SightPoint(ConstructWorld world, Construct c, ref CGun g, int seat, Vector3 muzzle, IWorldQuery query, float reach)
    {
        var eye = ConstructWeapons.SeatEye(c, seat);
        var look = World.Forward(c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
        var speed = MathF.Max(1f, g.Speed);
        if (Track(world, c, ref g, eye, look, query) is { } t)
        {
            var at = t.WorldCom;
            var rel = t.Vel - c.PointVel(muzzle); // (a shell takes the gun's speed with it)
            var secs = 0f;
            for (var i = 0; i < 3; i++)
                secs = Vector3.Distance(muzzle, at + rel * secs) / speed;
            g.FuseTicks = (int)(secs * 1.15f * World.TickRate) + 3; // (a little past it: the proximity fuse has the first go)
            return at + rel * secs;
        }
        g.FuseTicks = g.FuseRange > 0 ? (int)(g.FuseRange / speed * World.TickRate) : 0;
        return ConstructWeapons.GunnerPoint(world, c, seat, query, reach);
    }

    /// <summary>What a missile fired now locks on to: what its seeker sees, else what its gunner has in view; 0: it flies straight.</summary>
    public static ushort Lock(ConstructWorld world, Construct c, ref CGun g, Vector3 muzzle, Vector3 dir, IWorldQuery query)
    {
        if (g.Sight == SightKind.Seeker)
            return Seek(world, c, muzzle, dir, g.SightReach, g.SightCos, query);
        if (g.Seat < 0 || g.Seat >= c.PartCount || (c.Parts[g.Seat].Keys & CPart.Occupied) == 0)
            return 0;
        return Track(world, c, ref g, ConstructWeapons.SeatEye(c, g.Seat), World.Forward(c.Parts[g.Seat].AimYaw, c.Parts[g.Seat].AimPitch), query)?.Id ?? 0;
    }

    /// <summary>A seeker's look: the moving body nearest its axis in its cone, that the map doesn't hide. Walks the bodies near the cone, so only at a launch or now and then for a HUD.</summary>
    public static ushort Seek(ConstructWorld world, Construct c, Vector3 at, Vector3 dir, float reach, float cos, IWorldQuery query)
    {
        var mid = at + dir * (reach * 0.5f);
        var near = world.Near(mid, reach * 0.5f, world.OrdnanceNear);
        Construct? best = null;
        var most = cos;
        for (var i = 0; i < near.Count; i++)
        {
            var b = near[i];
            if (b == c || b.Anchored || b.Vel.LengthSquared() < FlyingSpeed * FlyingSpeed)
                continue;
            var to = b.WorldCom - at;
            var len = to.Length();
            if (len > reach || len < 1f)
                continue;
            var dot = Vector3.Dot(to / len, dir);
            if (dot > most)
            {
                most = dot;
                best = b;
            }
        }
        if (best == null)
            return 0;
        var way = best.WorldCom - at;
        var dist = way.Length();
        return query.Raycast(at, way / dist, dist, QueryFilter.Shots) < dist - 1f ? (ushort)0 : best.Id;
    }

    // ---- In flight -------------------------------------------------------------------------------------------------

    /// <summary>A rocket's push, a missile's turn, a flare's drag: every PC, each tick, for kinds from <see cref="ShellKind.Rocket"/> on.</summary>
    public static void Fly(ConstructWorld world, ref CShell s, float dt)
    {
        if (s.Kind == ShellKind.Flare)
        {
            s.Vel -= s.Vel * MathF.Min(1f, FlareDrag * dt);
            return;
        }
        var speed = s.Vel.Length();
        if (s.Burn > 0)
        {
            s.Burn--;
            var along = speed > 1f ? s.Vel / speed : Vector3.UnitY;
            s.Vel += along * (s.Thrust * dt);
            speed += s.Thrust * dt;
        }
        if (s.Kind != ShellKind.Missile || s.Turn <= 0 || speed < 1f)
            return;
        Vector3 aim;
        if (s.Decoy != 0 && world.ShellAt(s.Decoy) is var f and >= 0)
            aim = world.Shells[f].Pos;
        else if (s.Decoy != 0)
        {
            s.Decoy = 0; // (it burnt out: the lock went to it, and nothing is left to steer for)
            return;
        }
        else if (s.Target != 0 && world.Get(s.Target) is { } t)
        {
            var dist = Vector3.Distance(s.Pos, t.WorldCom);
            aim = t.WorldCom + t.Vel * MathF.Min(dist / speed, 3f);
        }
        else
            return;
        var want = aim - s.Pos;
        if (want.LengthSquared() > 1e-2f)
            s.Vel = RocketFlight.Turn(s.Vel / s.Vel.Length(), Vector3.Normalize(want), s.Turn * dt) * speed;
    }

    /// <summary>Server, now and then for a missile with a lock: a flare in its eye may draw it. A pair decides once, by their ids, so one flare can't be rolled again.</summary>
    public static void LookForFlares(ConstructWorld world, ref CShell s)
    {
        if (s.Target == 0 || s.Decoy != 0 || world.FlareCount == 0)
            return;
        var speed = s.Vel.Length();
        if (speed < 1f)
            return;
        var along = s.Vel / speed;
        for (var k = 0; k < world.ShellCount; k++)
        {
            ref var f = ref world.Shells[k];
            if (f.Kind != ShellKind.Flare || f.Landed)
                continue;
            var to = f.Pos - s.Pos;
            var dist = to.Length();
            if (dist > FlareReach || dist < 0.1f || Vector3.Dot(to / dist, along) < FlareCone)
                continue;
            if ((uint)(s.Id * 2654435761u ^ f.Id * 2246822519u) % 100u >= FlareChance)
                continue;
            s.Decoy = f.Id;
            s.Target = 0;
            world.AddFire(new CFireEvent { Kind = CFireKind.ShellSteer, Shell = s.Id, Target = f.Id, At = s.Pos, To = s.Vel });
            return;
        }
    }

    /// <summary>Is something flying within a shell's proximity fuse, or the flare that drew it? The bodies the broadphase gives near it, each by its box.</summary>
    public static bool Near(ConstructWorld world, in CShell s)
    {
        if (s.Decoy != 0 && world.ShellAt(s.Decoy) is var f and >= 0 && Vector3.DistanceSquared(world.Shells[f].Pos, s.Pos) < s.Prox * s.Prox)
            return true;
        var near = world.Near(s.Pos, s.Prox, world.OrdnanceNear);
        for (var i = 0; i < near.Count; i++)
        {
            var c = near[i];
            if (c.Id == s.Gun || c.Anchored || c.CarriedBy != 0 || c.Vel.LengthSquared() < FlyingSpeed * FlyingSpeed)
                continue;
            if (Vector3.Distance(c.WorldCom, s.Pos) > s.Prox + c.Radius)
                continue;
            var local = c.ToLocal(s.Pos);
            if (Vector3.Distance(Vector3.Clamp(local, c.LocalMin, c.LocalMax), local) <= s.Prox)
                return true;
        }
        return false;
    }
}

/// <summary>The world's side of the racks and of shells that know each other.</summary>
public sealed partial class ConstructWorld
{
    public readonly List<(ushort Body, ushort Part)> Drops = new(16); // racks that asked to let go this tick
    internal readonly List<Construct> OrdnanceNear = new(64);
    public int FlareCount;

    /// <summary>A shell's place in the pool by id, or -1.</summary>
    public int ShellAt(ushort id)
    {
        for (var k = 0; k < ShellCount; k++)
            if (Shells[k].Id == id)
                return k;
        return -1;
    }

    /// <summary>Client: what the launch event says beyond a shell's place and speed.</summary>
    public void ClientShellFlight(ushort id, in CFireEvent e)
    {
        if (ShellAt(id) is var k and >= 0)
        {
            ref var s = ref Shells[k];
            (s.Thrust, s.Turn, s.Burn, s.Target, s.Def) = (e.Thrust, e.Turn, e.Burn, e.Target, e.Part);
        }
    }

    /// <summary>Client: a bomb hit and lies waiting for its fuse.</summary>
    public void ClientShellStop(ushort id, Vector3 at)
    {
        if (ShellAt(id) is var k and >= 0)
        {
            ref var s = ref Shells[k];
            (s.Landed, s.Pos, s.Prev) = (true, at, at);
        }
    }

    /// <summary>Client: a missile went for a flare: the server's own numbers, so its copy turns the same way.</summary>
    public void ClientShellSteer(in CFireEvent e)
    {
        if (ShellAt(e.Shell) is var k and >= 0)
        {
            ref var s = ref Shells[k];
            (s.Pos, s.Vel, s.Decoy, s.Target) = (e.At, e.To, e.Target, (ushort)0);
        }
    }
}
