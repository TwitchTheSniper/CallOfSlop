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
    public const int MaxHeld = ModularRack.MaxStations; // bombs a rack lets go of in turn (an Ordnance Rack's most stations)
    public const int RackInterval = World.TickRate / 3; // ticks between bombs while its input stays on
    public const int RackRetry = World.TickRate / 6; // ticks before it looks again at a blocked way down
    public const float DropClear = 8f; // m under a bomb that must be free of its own construct
    public const int BombClear = World.TickRate; // ticks a bomb passes its own construct
    public const float LookReach = 1500f; // m a gunner's look locks a missile out to, with no sight
    public const float LookCone = 6f; // degrees a target stays locked off the look
    public const float MissileCoast = 6f; // seconds a missile flies on after its burn before it goes off
    public const int ProxArm = 6; // ticks before a proximity fuse arms (it never takes the gun's own body)
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

    /// <summary>
    /// The working explosive parts bolted or welded to a rack. A designed weapon over the rack's rated load isn't held: it stays bolted on and won't let go (the look line says why);
    /// the rack and bombs of older saves keep working whatever they weigh. An Ordnance Rack (ModularRack.cs) weighs and measures everything on it: one over a
    /// station's rating, longer or wider than a station takes, isn't held, nor one that takes the whole past the rack's (an old Modular Bomb Rack's shape only weighs).
    /// </summary>
    public static int Held(Construct c, int rack, Span<ushort> into)
    {
        var n = 0;
        var rd = c.DefOf(rack);
        var rating = rd.RackLoad;
        var total = 0f;
        for (var e = c.FirstLink(rack); e >= 0 && n < into.Length; e = c.NextLink(e))
        {
            var o = c.OtherEnd(e);
            if (c.Parts[o].Working && c.DefOf(o) is { Explosive: not null } d && (rd.IsModularRack ? Takes(rd, d, ref total) : !(d.IsOrdnance && rating > 0 && d.Mass > rating)))
                into[n++] = (ushort)o;
        }
        for (var j = 0; j < c.JointCount && n < into.Length; j++)
        {
            ref var jt = ref c.Joints[j];
            if (!jt.Freeform || jt.Cut)
                continue;
            var o = jt.Hinge == rack ? jt.Child : jt.Child == rack ? jt.Hinge : -1;
            if (o >= 0 && o < c.PartCount && c.Parts[o].Working && c.DefOf(o) is { Explosive: not null } d && (!rd.IsModularRack || Takes(rd, d, ref total)))
                into[n++] = (ushort)o;
        }
        return n;
    }

    /// <summary>An Ordnance Rack's limits: a station's length, diameter and rating, and the whole rack's with what it already holds.</summary>
    private static bool Takes(ConstructPartDef rack, ConstructPartDef bomb, ref float total)
    {
        if (bomb.Mass > rack.RackStation || total + bomb.Mass > rack.RackLoad || !Fits(rack, bomb))
            return false;
        total += bomb.Mass;
        return true;
    }

    private const float SizeSlack = 0.0015f; // m an Ordnance Rack's length and diameter allow past themselves (the bench's rounding)

    /// <summary>
    /// A piece of ordnance's length and body diameter (m), as an Ordnance Rack measures it: a design's own, from the bench; an old part file's its
    /// length along it and its body under the lug (the lug stands 5 cm proud of the body, as the part generators make it).
    /// </summary>
    public static void Dims(ConstructPartDef d, out float length, out float diameter)
    {
        if (d.OrdStats is { } st)
        {
            (length, diameter) = (st.Length, st.Diameter);
            return;
        }
        length = d.BoundsMax.Z - d.BoundsMin.Z;
        var lug = LugOf(d);
        diameter = lug >= 0 ? MathF.Max(0.05f, 2f * (d.Sockets[lug].At.Y - 0.05f)) : MathF.Min(d.BoundsMax.X - d.BoundsMin.X, d.BoundsMax.Y - d.BoundsMin.Y);
    }

    /// <summary>Does it fit a station of this rack (an old rack's shape: anything does)?</summary>
    private static bool Fits(ConstructPartDef rack, ConstructPartDef bomb)
    {
        if (!rack.IsModularRack || rack.Rack.IsLegacy)
            return true;
        Dims(bomb, out var len, out var dia);
        return len <= rack.Rack.Length + SizeSlack && dia <= rack.Rack.Diameter + SizeSlack;
    }

    /// <summary>Why an Ordnance Rack won't take one more of this on top of <paramref name="carried"/> kg, or null: which limit.</summary>
    public static string? LoadWhy(ConstructPartDef rack, float carried, ConstructPartDef bomb)
    {
        if (rack.IsModularRack && !rack.Rack.IsLegacy)
        {
            Dims(bomb, out var len, out var dia);
            if (len > rack.Rack.Length + SizeSlack)
                return $"too long: {len:0.##} m, and a station of the {rack.Name} takes {rack.Rack.Length:0.##} m";
            if (dia > rack.Rack.Diameter + SizeSlack)
                return $"too wide: {dia * 100:0.#} cm, and a station of the {rack.Name} takes {rack.Rack.Diameter * 100:0.#} cm";
        }
        var mass = bomb.Mass;
        return mass > rack.RackStation ? $"too heavy: {mass:0} kg, and a station of the {rack.Name} takes {rack.RackStation:0} kg"
            : carried + mass > rack.RackLoad ? $"too heavy: {carried + mass:0} kg on the {rack.Name}, rated for {rack.RackLoad:0} kg in all" : null;
    }

    /// <summary>
    /// Does this rack send this forward, as a missile leaves, rather than drop it? A Launch Rail sends anything; an Ordnance Rack a design that is a
    /// missile (its motor lights as it leaves: <see cref="OrdnanceDesign.Powered"/>, as the bench writes it in the key). Everything else drops.
    /// </summary>
    public static bool Launches(ConstructPartDef rack, ConstructPartDef weapon) =>
        rack.RackRail || rack.IsModularRack && weapon.OrdDesign is { Kind: OrdKind.Missile };

    /// <summary>The way a launched weapon leaves (world): an Ordnance Rack's forward (its -Z, the front socket's side), a rail's the weapon's own nose.</summary>
    public static Vector3 LaunchDir(Construct c, int rack, int weapon)
    {
        var onRack = (uint)rack < (uint)c.PartCount && c.DefOf(rack).IsModularRack;
        var part = onRack ? rack : weapon;
        c.WorldFrame(c.Parts[part].Seg, out var q, out _);
        return Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, q * Rot24.Quat(c.Parts[part].Rot)));
    }

    /// <summary>
    /// Where a bomb leaves its rack (world): where it hangs, or on a drum the bottom of its ring (the drum turns it there). Its way down and its
    /// shell start here.
    /// </summary>
    public static Vector3 ReleaseAt(Construct c, int rack, int bomb)
    {
        var at = ConstructRender.PartCentre(c, bomb);
        if ((uint)rack >= (uint)c.PartCount || c.DefOf(rack).Rack is not { Stations: > 0, Layout: RackLayout.Drum })
            return at;
        ref var rp = ref c.Parts[rack];
        c.WorldFrame(rp.Seg, out var q, out var t);
        var rq = q * Rot24.Quat(rp.Rot);
        var axis = t + Vector3.Transform(rp.Pos, q); // (the drum's axis is the part's z axis)
        var local = Vector3.Transform(at - axis, Quaternion.Conjugate(rq));
        var r = MathF.Sqrt(local.X * local.X + local.Y * local.Y);
        return axis + Vector3.Transform(new Vector3(0, -r, local.Z), rq);
    }

    /// <summary>Ticks to a rack's next drop: a third of a second on the old racks; a modular rack's interval, or a tick in a salvo.</summary>
    private static ushort Interval(in CPart p, ConstructPartDef d)
    {
        if (!d.IsModularRack)
            return RackInterval;
        if (d.Rack.Layout != RackLayout.Drum && ConstructConfig.Get(p, d, CfgKey.FireOrder, 0) >= 0.5f)
            return 1;
        return (ushort)Math.Clamp((int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.RackInterval, RackInterval / (float)World.TickRate) * World.TickRate), 1, 600);
    }

    /// <summary>Server: a rack's tick. Its input on, it asks to let one go; bodies change after the logic step. Puts out how many it holds.</summary>
    public static float StepRack(ConstructWorld world, Construct c, int part, ref CPart p, bool on)
    {
        if (p.Cool > 0)
            p.Cool--;
        var held = 0;
        for (var k = 0; k < c.RackCount; k++)
            if (c.Racks[k].Part == part)
                held = c.Racks[k].Held;
        var fire = on;
        var d = p.D;
        if (d.IsModularRack && d.Rack.Layout != RackLayout.Drum && ConstructConfig.Get(p, d, CfgKey.FireOrder, 0) >= 0.5f)
        {
            // A salvo: a press (State bit 2 is the input last tick) latches bit 1, and the rack lets go a tick apart until it is empty.
            if (on && (p.State & 4) == 0 && held > 0)
                p.State |= 2;
            if (held == 0)
                p.State &= unchecked((byte)~2);
            p.State = (byte)((p.State & ~4) | (on ? 4 : 0));
            fire = (p.State & 2) != 0;
        }
        if (fire && p.Cool == 0 && world.ShellRoom && world.Drops.Count < world.Drops.Capacity)
            world.Drops.Add((c.Id, (ushort)part));
        return held;
    }

    /// <summary>The racks that asked this tick let go. One bomb a body a tick: the next rack's numbers aren't good after a part comes off, and it asks again.</summary>
    public static void Release(ConstructWorld world, IWorldQuery query)
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
            if (Drop(world, c, part, query))
                done = body;
        }
        world.Drops.Clear();
    }

    private static bool Drop(ConstructWorld world, Construct c, int rack, IWorldQuery query)
    {
        Span<ushort> held = stackalloc ushort[MaxHeld];
        var n = Held(c, rack, held);
        for (var k = 0; k < n; k++)
        {
            int bomb = held[k];
            if (Blocked(world, c, bomb, out _))
                continue;
            var def = c.DefOf(bomb);
            if (def.OrdStats != null)
            {
                if (OrdnanceFlight.Grounded(c, rack, bomb) != null)
                    continue; // (too heavy for its thrust: it stays on the rail, and the look line says so)
                if (!OrdnanceFlight.Launch(world, c, rack, bomb, query))
                    return false;
                c.Parts[rack].Cool = Interval(c.Parts[rack], c.DefOf(rack));
                world.RemovePart(c, bomb);
                return true;
            }
            var ex = def.Explosive!;
            var at = ReleaseAt(c, rack, bomb);
            var vel = c.PointVel(at) + c.SegPointVel(c.Parts[bomb].Seg, at);
            var fuse = Math.Max(1, (int)MathF.Round(ex.ExplodeFuse * World.TickRate));
            var id = world.AddShell(new CShell { Kind = ShellKind.Bomb, By = c.Owner, Gun = c.Id, GunPart = (ushort)rack, Size = def.BoundsMax - def.BoundsMin, Pos = at, Prev = at, Tested = at, Vel = vel, Blast = ex.ExplodeRadius, Damage = ex.ExplodeDamage, Crater = ex.ExplodeCrater, Delay = fuse, Clear = BombClear });
            if (id == 0)
                return false;
            world.AddFire(new CFireEvent { Kind = CFireKind.ShellOut, Body = c.Id, Part = (ushort)rack, Shell = id, At = at, To = vel, Muzzle = (byte)ShellKind.Bomb, Half = def.BoundsMax - def.BoundsMin }.WithLook(c, bomb));
            c.Parts[rack].Cool = Interval(c.Parts[rack], c.DefOf(rack));
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
        var rack = RackOf(c, bomb);
        var at = ReleaseAt(c, rack, bomb);
        var pull = ConstructShells.GravityAt(at);
        c.WorldFrame(c.Parts[bomb].Seg, out var q, out _);
        var down = pull.LengthSquared() > 0.01f ? Vector3.Normalize(pull) : Vector3.Transform(-Vector3.UnitY, q);
        if (rack >= 0 && Launches(c.DefOf(rack), c.DefOf(bomb)))
            down = LaunchDir(c, rack, bomb); // (a launch's way out is ahead of what it holds)
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
        var rd = c.DefOf(part);
        if (rd.IsModularRack)
        {
            // One on it that it doesn't hold: over a station's rating, or past the whole rack's.
            var carried = 0f;
            for (var k = 0; k < n; k++)
                carried += c.DefOf(held[k]).Mass;
            for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
                if (c.DefOf(c.OtherEnd(e)) is { Explosive: not null } over && held[..n].IndexOf((ushort)c.OtherEnd(e)) < 0)
                    return LoadWhy(rd, carried, over) ?? $"the {over.Name} isn't held";
        }
        if (n == 0)
        {
            var rating = rd.RackLoad;
            for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
                if (c.DefOf(c.OtherEnd(e)) is { IsOrdnance: true } over && rating > 0 && over.Mass > rating)
                    return $"that weighs {over.Mass:0} kg, and this is rated for {rating:0} kg";
            return "nothing on it: bolt a bomb to it, or push one on with the physics gun";
        }
        for (var k = 0; k < n; k++)
            if (OrdnanceFlight.Grounded(c, part, held[k]) is { } why)
                return why;
        if (world == null)
            return null;
        ConstructPartDef? by = null;
        for (var k = 0; k < n; k++)
            if (!Blocked(world, c, held[k], out by))
                return null;
        return by == null ? null : n > 0 && Launches(rd, c.DefOf(held[0])) ? $"the {by.Name} is in the way out" : $"the {by.Name} is in the way down";
    }

    // ---- The physics gun: snapping on and pulling off -----------------------------------------------------------------------

    public const float SnapSeen = 3f; // m from lug to lug at which the ghost shows
    public const float SnapReach = 0.45f; // ...and at which it goes on
    public const float SnapLeave = 1.5f; // m it must be from the rack it was pulled off before that rack takes it again

    /// <summary>Where a held bomb would go: the hook, and the pose it would sit in (rest frame of the hook's segment).</summary>
    public struct SnapSpot
    {
        public Construct? Body;
        public int Rack, Socket, Lug;
        public Vector3 At; // in that segment
        public byte Rot;
        public Quaternion WorldRot;
        public Vector3 WorldAt;
        public float Distance;
        public string? Why; // the nearest hook it can't go on: why (null when it can)
    }

    private static int LugOf(ConstructPartDef d) => Array.FindIndex(d.Sockets, s => s.Name.Equals("lug", StringComparison.OrdinalIgnoreCase));

    /// <summary>Is this a thing a hook takes: one loose explosive part with a lug?</summary>
    public static bool Hangs(Construct c) => c.PartCount == 1 && !c.Dirty && c.Parts[0].Working && c.DefOf(0) is { Explosive: not null } d && LugOf(d) >= 0;

    /// <summary>The hook nearest a held bomb's lug in <see cref="SnapSeen"/>, on either peer. False: none. Walks the racks near it, once a tick or frame for what is held.</summary>
    public static bool FindSnap(ConstructWorld world, Construct held, out SnapSpot spot)
    {
        spot = default;
        var d = held.DefOf(0);
        var lug = LugOf(d);
        ref var hp = ref held.Parts[0];
        var lugAt = held.ToWorld(hp.Pos + Rot24.Apply(hp.Rot, d.Sockets[lug].At));
        var best = SnapSeen;
        var found = false;
        var near = world.Near(lugAt, SnapSeen, world.OrdnanceNear);
        for (var i = 0; i < near.Count; i++)
        {
            var c = near[i];
            if (c == held || c.RackCount == 0)
                continue;
            if (held.TakenFrom == c.Id)
                continue;
            for (var k = 0; k < c.RackCount; k++)
            {
                int rack = c.Racks[k].Part;
                if (rack >= c.PartCount || !c.Parts[rack].Working)
                    continue;
                var rd = c.DefOf(rack);
                ref var rp = ref c.Parts[rack];
                c.WorldFrame(rp.Seg, out var q, out var t);
                for (var s = 0; s < rd.Sockets.Length; s++)
                {
                    if (!rd.Sockets[s].Name.StartsWith("lug", StringComparison.Ordinal))
                        continue;
                    var free = true;
                    for (var e = c.FirstLink(rack); e >= 0 && free; e = c.NextLink(e))
                        free = ((e & 1) == 0 ? c.Links[e >> 1].SA : c.Links[e >> 1].SB) != s;
                    if (!free)
                        continue;
                    var sAt = rp.Pos + Rot24.Apply(rp.Rot, rd.Sockets[s].At);
                    var dist = Vector3.Distance(t + Vector3.Transform(sAt, q), lugAt);
                    if (dist >= best)
                        continue;
                    best = dist;
                    found = true;
                    spot = new SnapSpot { Body = c, Rack = rack, Socket = s, Lug = lug, Distance = dist };
                    var want = Quaternion.Conjugate(q) * held.Rot * Rot24.Quat(hp.Rot);
                    var mine = d.Sockets[lug].Dir;
                    var theirs = Rot24.ApplyDir(rp.Rot, rd.Sockets[s].Dir);
                    var ahead = Rot24.Apply(rp.Rot, -Vector3.UnitZ); // (an Ordnance Rack's forward: what it holds lies along it, nose first)
                    var most = -1f;
                    for (var pass = rd.IsModularRack ? 0 : 1; pass < 2 && most < 0; pass++)
                        for (byte r = 0; r < Rot24.Count; r++)
                        {
                            if (Vector3.Dot(Rot24.Apply(r, ConstructParts.DirVector(mine)), ConstructParts.DirVector(theirs)) > -0.9f)
                                continue;
                            if (pass == 0 && Vector3.Dot(Rot24.Apply(r, -Vector3.UnitZ), ahead) < 0.9f)
                                continue;
                            var dot = MathF.Abs(Quaternion.Dot(Rot24.Quat(r), want));
                            if (dot > most)
                            {
                                most = dot;
                                spot.Rot = r;
                            }
                        }
                    spot.At = ConstructWorld.Quantise(sAt - Rot24.Apply(spot.Rot, d.Sockets[lug].At));
                    spot.WorldAt = t + Vector3.Transform(spot.At, q);
                    spot.WorldRot = q * Rot24.Quat(spot.Rot);
                    var carried = 0f;
                    for (var e = c.FirstLink(rack); e >= 0; e = c.NextLink(e))
                        if (c.DefOf(c.OtherEnd(e)).Explosive != null)
                            carried += c.DefOf(c.OtherEnd(e)).Mass;
                    spot.Why = rd.IsModularRack ? LoadWhy(rd, carried, d)
                        : rd.RackLoad > 0 && carried + d.Mass > rd.RackLoad ? $"too heavy: {carried + d.Mass:0} kg on a {rd.Name} rated for {rd.RackLoad:0} kg"
                        : null;
                    spot.Why ??= c.PartCount >= Construct.MaxParts ? "that construct is full"
                        : !Room(c, rack, d, spot.At, spot.Rot, rp.Seg, rd.IsModularRack) ? "something is in the way" : null;
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Does it clear the other parts of the hook's segment? (Rest-frame boxes, a centimetre in. Only at a snap.) On an Ordnance Rack its
    /// neighbours on the same rack don't count: the rack spaces its stations for bodies, and fins may overlap the next one's.
    /// </summary>
    private static bool Room(Construct c, int rack, ConstructPartDef d, Vector3 at, byte rot, int seg, bool spaced)
    {
        var half = Rot24.ApplyHalf(rot, (d.BoundsMax - d.BoundsMin) * 0.5f) - new Vector3(0.01f);
        var mid = at + Rot24.Apply(rot, (d.BoundsMin + d.BoundsMax) * 0.5f);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (i == rack || c.Parts[i].Seg != seg || spaced && c.DefOf(i).Explosive != null && RackOf(c, i) == rack)
                continue;
            c.PartBounds(i, out var mn, out var mx);
            var far = Vector3.Abs((mn + mx) * 0.5f - mid) - (mx - mn) * 0.5f;
            if (far.X < half.X && far.Y < half.Y && far.Z < half.Z)
                return false;
        }
        return true;
    }

    /// <summary>Server, each tick for a held bomb: pushed to a hook, it goes on. True when it did: the body it was is gone.</summary>
    public static bool TrySnap(ConstructWorld world, Construct held)
    {
        if (!Hangs(held))
            return false;
        if (held.TakenFrom != 0)
        {
            if (world.Get(held.TakenFrom) is not { } from || Vector3.Distance(from.WorldCom, held.WorldCom) > SnapLeave + from.Radius)
                held.TakenFrom = 0; // (well clear of what it came off)
        }
        if (!FindSnap(world, held, out var s) || s.Why != null || s.Distance > SnapReach)
            return false;
        var c = s.Body!;
        var def = held.DefOf(0);
        var health = held.Parts[0].Health;
        var paint = held.Parts[0].Paint;
        var i = world.AddPart(c, def, s.At, s.Rot, c.Parts[s.Rack].Seg);
        if (i < 0)
            return false;
        world.AddLink(c, s.Rack, s.Socket, i, s.Lug);
        c.Parts[i].Health = health;
        c.Parts[i].Paint = paint;
        c.Wake();
        return true;
    }

    /// <summary>The rack an explosive part hangs on, or -1 (what the physics gun pulls off).</summary>
    public static int RackOf(Construct c, int part)
    {
        if ((uint)part >= (uint)c.PartCount || c.DefOf(part).Explosive == null)
            return -1;
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
            if (c.DefOf(c.OtherEnd(e)).Logic == CLogic.BombRack)
                return c.OtherEnd(e);
        return -1;
    }

    /// <summary>Pulls a bomb off its hook: it comes away a loose body where it hung, moving as the carrier does.</summary>
    public static Construct? PullOff(ConstructWorld world, Construct c, int part)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        ref var p = ref c.Parts[part];
        var pos = t + Vector3.Transform(p.Pos, q);
        var rot = q * Rot24.Quat(p.Rot);
        var vel = c.PointVel(pos) + c.SegPointVel(p.Seg, pos);
        var (health, paint) = (p.Health, p.Paint);
        var body = world.Create(pos, rot);
        if (body == null)
            return null;
        var def = world.RemovePart(c, part);
        world.AddPart(body, def, Vector3.Zero, 0);
        body.Parts[0].Health = health;
        body.Parts[0].Paint = paint;
        body.Owner = c.Owner;
        body.Vel = vel;
        body.TakenFrom = c.Id;
        body.Rebuild();
        return body;
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
        var look = GravityFrames.Forward(c.WorldCom, c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
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
        return Track(world, c, ref g, ConstructWeapons.SeatEye(c, g.Seat), GravityFrames.Forward(c.WorldCom, c.Parts[g.Seat].AimYaw, c.Parts[g.Seat].AimPitch), query)?.Id ?? 0;
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
        if ((s.Flags & ShellFlags.Designed) != 0)
        {
            OrdnanceFlight.Fly(world, ref s, dt);
            return;
        }
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

    /// <summary>
    /// Is something flying within a shell's proximity fuse, or the flare that drew it, anywhere along the stretch since its last look (a fast shell goes 17 m between
    /// looks)? The bodies the broadphase gives near that stretch, each by its box. It moves the shell to where it came nearest: the burst is there.
    /// </summary>
    public static bool Near(ConstructWorld world, ref CShell s)
    {
        var from = s.Tested;
        var way = s.Pos - from;
        var len2 = way.LengthSquared();
        if (s.Decoy != 0 && world.ShellAt(s.Decoy) is var f and >= 0)
        {
            var at = Nearest(from, way, len2, world.Shells[f].Pos);
            if (Vector3.DistanceSquared(world.Shells[f].Pos, at) < s.Prox * s.Prox)
            {
                s.Pos = at;
                return true;
            }
        }
        var near = world.Near(from + way * 0.5f, s.Prox + MathF.Sqrt(len2) * 0.5f, world.OrdnanceNear);
        for (var i = 0; i < near.Count; i++)
        {
            var c = near[i];
            if (c.Id == s.Gun || c.Anchored || c.CarriedBy != 0 || c.Vel.LengthSquared() < FlyingSpeed * FlyingSpeed)
                continue;
            var at = Nearest(from, way, len2, c.WorldCom);
            if (Vector3.Distance(c.WorldCom, at) > s.Prox + c.Radius)
                continue;
            var local = c.ToLocal(at);
            if (Vector3.Distance(Vector3.Clamp(local, c.LocalMin, c.LocalMax), local) <= s.Prox)
            {
                s.Pos = at;
                return true;
            }
        }
        return false;
    }

    private static Vector3 Nearest(Vector3 from, Vector3 way, float len2, Vector3 to) =>
        len2 < 1e-6f ? from : from + way * Math.Clamp(Vector3.Dot(to - from, way) / len2, 0f, 1f);
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
            (s.Thrust, s.Turn, s.Burn, s.Target, s.Size) = (e.Thrust, e.Turn, e.Burn, e.Target, e.Half);
            if ((e.Ext & 1) != 0)
                (s.AirPush, s.TurnAir, s.TurnBurn, s.Drag, s.Lift, s.MinSpeed, s.Wait, s.Deploy, s.Flags, s.Aim, s.Life) = (e.AirPush, e.TurnAir, e.TurnBurn, e.Drag, e.Lift, e.MinSpeed, e.Wait, e.Deploy, e.Flags, e.Aim, e.Life);
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
            (s.Pos, s.Vel) = (e.At, e.To);
            if ((e.Ext & 4) == 0)
                (s.Decoy, s.Target) = (e.Target, (ushort)0); // (a flare drew it)
            else
                s.Target = e.Target;
            if ((e.Ext & 1) != 0)
            {
                s.Aim = e.Aim;
                s.Flags |= ShellFlags.HasAim;
            }
            if ((e.Ext & 2) != 0)
            {
                (s.Thrust, s.AirPush, s.Burn, s.TurnBurn, s.MinSpeed, s.Flags, s.Wait) = (e.Thrust, e.AirPush, e.Burn, e.TurnBurn, e.MinSpeed, e.Flags, 0); // (a stage dropped)
                if (s.Stage < 255)
                    s.Stage++; // (what is drawn loses it too: ShellLooks)
            }
        }
    }
}
