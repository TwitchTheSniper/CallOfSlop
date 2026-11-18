using System.Numerics;

namespace CallOfSlop;

/// <summary>What a part's <c>"breach"</c> block makes it (ConstructBreach.cs).</summary>
public enum BreachKind : byte
{
    None,
    Grapple, // a hull grapple: while its input is on, it grips the hull under its face, and the craft rides on that body
    Cutter, // a breaching cutter: while its craft is gripped on and its input is on, it burns through what is under its face
}

/// <summary>
/// Boarding craft (Karl, 2026-11-17: the Zero Day's Shuttle, docs/sandbox/constructs/shuttle.md): a craft with **hull grapples**
/// (lamp-kind parts with a <c>"breach": {"kind": "grapple", "reach"}</c> block) sets down on another body's hull and, while a
/// grapple's input is on and the hull is within its reach under the grapple's face (its -y), rides on that body: it is carried as
/// a docking clamp carries a craft (<see cref="ConstructCargo"/>: <c>CarriedBy</c> that body, no volume, its pose in the frame of
/// the segment of the part it gripped), so it moves with the hull, its riders with it, and nothing new goes over the wire.
/// Turning every grapple off (or the last one breaking) lets go. A **breaching cutter** (<c>"kind": "cutter"</c>, its
/// <c>"rate"</c> damage a second, raw: armour doesn't stop it) on a gripped craft burns, while its input is on, the nearest
/// part of the gripped body under its face within its reach, one after another, until nothing is left there: the hole is open
/// and the boarders drop through it into the room below. The boarding itself (a crew that holds, surrenders, fights or scuttles,
/// Game/GameCrewBoard.cs) starts when they stand in the other body. Server only, ten times a second, allocates nothing.
/// </summary>
public static class ConstructBreach
{
    public const int StepEvery = 6; // ticks between looks (10 Hz)
    public const float GripSpeed = 3f; // m/s against the hull under which a grapple takes hold
    public const int Rays = 5; // a cutter's rays: its middle and four round its ring (a fitting off the middle still stands in the way)

    /// <summary>Does body <paramref name="b"/> have a breach part (counted once a rebuild, on the body)?</summary>
    private static bool HasAny(Construct b)
    {
        if (b.BreachStamp != b.RebuildStamp)
        {
            b.BreachStamp = b.RebuildStamp;
            b.BreachParts = 0;
            for (var i = 0; i < b.PartCount; i++)
                if (b.DefOf(i).Breach != BreachKind.None)
                    b.BreachParts++;
        }
        return b.BreachParts > 0;
    }

    /// <summary>Is <paramref name="b"/> a boarding pod (a grapple and a cutter both: the Limpet), which a Board order can fly (Game/GameNavBoard.cs)?
    /// Walks the parts: for an order and the order menu, not a tick.</summary>
    public static bool IsPod(Construct b)
    {
        bool grip = false, cut = false;
        for (var i = 0; i < b.PartCount; i++)
        {
            var k = b.DefOf(i).Breach;
            grip |= k == BreachKind.Grapple;
            cut |= k == BreachKind.Cutter;
        }
        return grip && cut;
    }

    /// <summary>Is a cutter of <paramref name="b"/> through (on, and nothing left under its ring)?</summary>
    public static bool CutThrough(Construct b)
    {
        for (var i = 0; i < b.PartCount; i++)
            if (b.DefOf(i).Breach == BreachKind.Cutter && On(b, i) && (b.Parts[i].State & 8) != 0)
                return true;
        return false;
    }

    private static bool On(Construct b, int i) => !b.Parts[i].Broken && b.Parts[i].Out >= 0.5f;

    /// <summary>Does a grapple of <paramref name="b"/> hold on now (one of them unbroken and on)? A carried body with no volume of
    /// its carrier's under it rides on as long as this says so (ConstructCargo.Step).</summary>
    public static bool Holds(Construct b)
    {
        if (!HasAny(b))
            return false; // (a body dirty since its last rebuild still holds: a part broken this tick is Broken already)
        for (var i = 0; i < b.PartCount; i++)
            if (b.DefOf(i).Breach == BreachKind.Grapple && On(b, i))
                return true;
        return false;
    }

    /// <summary>Is <paramref name="b"/> gripped on a hull (carried with no volume holding it)?</summary>
    public static bool Gripped(ConstructWorld world, Construct b) =>
        b.CarriedBy != 0 && world.Get(b.CarriedBy) is { } c && ConstructCargo.CarriedIn(c, b) < 0;

    /// <summary>A breach part's face: its middle (world) and the way out of its face (its -y, world).</summary>
    internal static void Face(Construct b, int i, out Vector3 at, out Vector3 down)
    {
        ref var p = ref b.Parts[i];
        b.PartBounds(i, out var mn, out var mx);
        b.WorldFrame(p.Seg, out var q, out var t);
        var mid = (mn + mx) * 0.5f;
        var lowest = Rot24.Apply(p.Rot, new Vector3(0, -1, 0));
        at = t + Vector3.Transform(new Vector3(mid.X, mid.Y, mid.Z) + lowest * (Vector3.Dot(mx - mn, Vector3.Abs(lowest)) * 0.5f - 0.02f), q);
        down = Vector3.Normalize(Vector3.Transform(lowest, q));
    }

    /// <summary>Server, every tick: ten times a second each craft with breach parts grips, lets go or cuts.</summary>
    public static void Step(ConstructWorld world)
    {
        if (++world.BreachTicks < StepEvery)
            return;
        world.BreachTicks = 0;
        var bodies = world.Bodies;
        for (var k = 0; k < bodies.Count; k++)
        {
            var b = bodies[k];
            if (!b.HasLogic || b.Dirty || !HasAny(b))
                continue;
            if (b.CarriedBy == 0)
                TryGrip(world, b);
            else if (Gripped(world, b) && world.Get(b.CarriedBy) is { } c)
            {
                if (!OnHull(world, b, c))
                {
                    world.Events.Add($"construct {b.Id}: nothing under its grapples now: it lets go of construct {c.Id}");
                    ConstructCarrier.LetGo(world, b); // (the hull under it was shot away or broke off)
                    continue;
                }
                Cut(world, b, c);
            }
        }
    }

    /// <summary>Is the gripped body's hull still under a grapple that is on (its reach and a little more: the grip holds it there), or
    /// was it sealed on by a Board order?</summary>
    private static bool OnHull(ConstructWorld world, Construct b, Construct c)
    {
        if (b.BreachSealed)
            return true; // (sealed on by a Board order, its nose round a door or wedged in its cut: Holds alone lets it go)
        for (var i = 0; i < b.PartCount; i++)
        {
            var kind = b.DefOf(i).Breach;
            if (kind != BreachKind.Grapple || !On(b, i))
                continue;
            Face(b, i, out var at, out var down);
            if (world.Raycast(at, down, b.DefOf(i).BreachReach + 0.3f, out _, b, only: c))
                return true;
        }
        return false;
    }

    private static void TryGrip(ConstructWorld world, Construct b)
    {
        if (world.CarrierClock < b.DockAway || b.Anchored || b.Pinned || b.Frozen || b.GrabbedBy != 255)
            return; // (just let go of a dock: it doesn't take hold of the ship it left at once)
        for (var i = 0; i < b.PartCount; i++)
        {
            if (b.DefOf(i).Breach != BreachKind.Grapple || !On(b, i))
                continue;
            Face(b, i, out var at, out var down);
            if (!world.Raycast(at, down, b.DefOf(i).BreachReach, out var hit, b) || hit.Body == null || hit.Body == b || hit.Body.CarriedBy != 0 || hit.Body.SpaceShip != 0)
                continue;
            var t = hit.Body;
            if ((b.Vel - t.PointVel(hit.Point)).Length() > GripSpeed)
                continue;
            Grip(world, b, t, hit.Part);
            return;
        }
    }

    /// <summary>Server: <paramref name="b"/> rides on <paramref name="t"/> from now on, where it is, in the frame of part <paramref name="part"/>'s segment.</summary>
    public static void Grip(ConstructWorld world, Construct b, Construct t, int part)
    {
        if (t.Dirty)
            t.Rebuild();
        var seg = part >= 0 && part < t.PartCount ? t.Parts[part].Seg : (byte)0;
        t.WorldFrame(seg, out var q, out var tt);
        var inv = Quaternion.Conjugate(q);
        if (!t.Anchored && t.Mass + b.Mass > 0)
            t.Vel = (t.Vel * t.Mass + b.Vel * b.Mass) / (t.Mass + b.Mass);
        b.CarriedBy = t.Id;
        b.CarryTag = 0; // (no volume holds it: ConstructCargo.CarriedIn finds none, and Holds keeps it)
        b.CarryPart = 0;
        b.CarrySeg = seg;
        b.CarryPos = Vector3.Transform(b.Pos - tt, inv);
        b.CarryRot = Quaternion.Normalize(inv * b.Rot);
        b.Asleep = true;
        for (var i = 0; i < b.PartCount; i++)
            if (b.DefOf(i).Breach == BreachKind.Cutter)
                b.Parts[i].State = (byte)(b.Parts[i].State & ~8); // (a new grip is a new cut: a cutter left on through the last hull isn't through this one)
        b.BreachCut = 0;
        world.StructureDirty.Add(b.Id);
        world.CargoChanged = true;
        world.Events.Add($"construct {b.Id}: its grapples took hold of construct {t.Id}");
    }

    /// <summary>A gripped craft's cutters at work: each on burns the nearest part of the gripped body under its ring.</summary>
    private static void Cut(ConstructWorld world, Construct b, Construct c)
    {
        var dt = StepEvery * World.TickDt;
        for (var i = 0; i < b.PartCount; i++)
        {
            var d = b.DefOf(i);
            if (d.Breach != BreachKind.Cutter)
                continue;
            ref var p = ref b.Parts[i];
            if (!On(b, i))
            {
                p.State = (byte)(p.State & ~8);
                b.BreachCut = 0; // (turned off: a new burn starts over)
                continue;
            }
            if ((p.State & 8) != 0)
                continue; // (through: it cuts no deeper, into the floor beyond, until it is turned off and on again)
            Face(b, i, out var at, out var down);
            b.PartBounds(i, out var mn, out var mx);
            var r = MathF.Min(mx.X - mn.X, mx.Z - mn.Z) * 0.3f;
            b.WorldFrame(p.Seg, out var q, out _);
            var across = Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitX), q);
            var along = Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitZ), q);
            var best = float.MaxValue;
            var part = -1;
            for (var k = 0; k < Rays; k++)
            {
                var off = k switch { 1 => across, 2 => -across, 3 => along, 4 => -along, _ => Vector3.Zero } * r;
                if (world.Raycast(at + off, down, d.BreachReach, out var hit, b, only: c) && hit.Distance < best)
                {
                    best = hit.Distance;
                    part = hit.Part;
                }
            }
            if (part < 0)
            {
                if ((p.State & 8) == 0)
                    world.Events.Add($"construct {b.Id}: the {d.Name} is through construct {c.Id}'s hull");
                p.State = (byte)(p.State | 8); // (through: said once)
                continue;
            }
            p.State = (byte)(p.State & ~8);
            // Burned long enough on one spot: the door-sized hole comes out of the hull (HullHoles.cs), into the room behind it.
            // Anything it can't hole (a machine in the way, a piece too small to hole) is burned away as before.
            if (b.BreachCut < HoleTicks)
            {
                b.BreachCut += StepEvery;
                if (b.BreachCut >= HoleTicks && OpenHole(world, b, i, c))
                {
                    p.State = (byte)(p.State | 8);
                    world.Events.Add($"construct {b.Id}: the {d.Name} is through construct {c.Id}'s hull");
                    return; // (the body rebuilds before the next look)
                }
                if (b.BreachCut < HoleTicks)
                    continue; // (still burning: the hull holds until the hole comes out)
            }
            var by = world.DamageBy;
            var body = world.DamageBody;
            world.DamageBy = b.Driver != 255 ? b.Driver : b.Owner;
            world.DamageBody = b.Id;
            var what = c.DefOf(part);
            if (world.DamagePart(c, part, d.BreachRate * dt, out _, raw: true))
                world.Events.Add($"construct {b.Id}: the {d.Name} cut through construct {c.Id}'s {what.Name}");
            world.DamageBy = by;
            world.DamageBody = body;
            if (c.Dirty)
                return; // (a part broke off: the body rebuilds before the next look)
        }
    }
    // ---- The hole (Karl, 2026-11-18: option C, a real hole you see space through and walk through) ------------------------

    public const int HoleTicks = 10 * World.TickRate; // a cutter burns this long on one spot, then the hole comes out
    public const float HoleHalfW = 0.55f; // m: half the hole's width (the Limpet's ring opens 1.2 m)
    public const float HoleTall = 2.1f; // m: from just over the room's floor up
    public const float HoleRoomMost = 6f; // m at most from the hull's face to the room behind it
    public const float HoleBlind = 1.2f; // m deep with no room found: a pocket in the hull, not a way in
    private const float HoleBreakMost = 2.5f; // m: a part the hole can't take but no bigger than this each way is burned away

    /// <summary>The way in, level in <paramref name="t"/>'s frame when it goes in through a side (a slanted flank's face points down
    /// or up a little: a hole along it would run down through the deck): people walk in on the level.</summary>
    public static Vector3 Level(Construct t, Vector3 down)
    {
        var up = Vector3.Transform(Vector3.UnitY, t.Rot);
        var k = Vector3.Dot(down, up);
        if (MathF.Abs(k) > 0.7f)
            return down; // (in through a roof or a floor: straight on)
        var flat = down - up * k;
        return flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : down;
    }

    /// <summary>
    /// The first spot a person can stand in <paramref name="t"/> straight on from <paramref name="face"/> along <paramref name="down"/>
    /// (world), within <see cref="HoleRoomMost"/>: on a floor, clear to head height, under a roof of its own, a step past the room's
    /// wall. <paramref name="room"/>: how far in that room begins. False: solid all the way, or open sky.
    /// </summary>
    public static bool RoomBehind(ConstructWorld world, Construct t, Vector3 face, Vector3 down, out Vector3 feet, out float room)
    {
        down = Level(t, down);
        var up = Vector3.Transform(Vector3.UnitY, t.Rot);
        up -= down * Vector3.Dot(up, down);
        up = up.LengthSquared() > 1e-4f ? Vector3.Normalize(up) : Vector3.Transform(Vector3.UnitY, t.Rot);
        const float r = World.PlayerRadius;
        var found = -1f;
        feet = default;
        room = 0;
        for (var d = 0.3f; d <= HoleRoomMost; d += 0.25f)
        {
            var q = face + down * d;
            if (!world.Raycast(q + up * 0.6f, -up, 2.6f, out var floor, only: t))
            {
                found = -1;
                continue;
            }
            var f = floor.Point + up * 0.03f;
            if (floor.Distance < 0.05f || !world.CapsuleClear(t, f + up * (r + 0.05f), f + up * (World.PlayerHeight - r), r)
                || !world.Raycast(f + up * 0.5f, up, 6f, out _, only: t))
            {
                found = -1; // (in a wall or a solid piece, or out under the sky: not in a room)
                continue;
            }
            if (found < 0)
                found = d;
            if (d < found + 0.45f)
                continue; // (a step on, so it is plainly in the room and not on its wall's face)
            feet = f;
            room = found;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Server: cutter <paramref name="cutter"/> of <paramref name="b"/> takes its hole out of <paramref name="c"/>: a tunnel its ring's
    /// width, from just over the floor of the room behind up to head height, as deep as that room's wall; every part it crosses that
    /// can be holed is swapped for its holed part (HullHoles.cs), a small one that can't is burned away. The body rebuilds and its
    /// structure goes out to every client, as any edit's does. False: nothing in the way could be holed or burned.
    /// </summary>
    public static bool OpenHole(ConstructWorld world, Construct b, int cutter, Construct c)
    {
        if (c.Dirty)
            c.Rebuild();
        Face(b, cutter, out var at, out var down);
        down = Level(c, down);
        var podUp = Vector3.Normalize(Vector3.Transform(Rot24.Apply(b.Parts[cutter].Rot, Vector3.UnitZ), b.Rot));
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        up -= down * Vector3.Dot(up, down);
        up = up.LengthSquared() > 0.05f ? Vector3.Normalize(up) : Vector3.Normalize(podUp - down * Vector3.Dot(podUp, down));
        float depth, bottom;
        var inRoom = RoomBehind(world, c, at, down, out var feet, out var room);
        if (inRoom)
        {
            depth = room + 0.15f;
            bottom = Math.Clamp(Vector3.Dot(feet - at, up) + 0.02f, -1.4f, -0.4f); // (the room's floor, as the ring's opening allows)
        }
        else
        {
            depth = HoleBlind;
            bottom = -1f;
        }
        var top = bottom + HoleTall;
        var mouth = at - down * 0.15f + up * ((bottom + top) * 0.5f);
        var halfH = (top - bottom) * 0.5f;
        var len = depth + 0.15f;
        var holed = 0;
        var burned = 0;
        var changed = false;
        for (var j = 0; j < c.PartCount; j++)
        {
            ref var p = ref c.Parts[j];
            if (p.Broken || c.Rides(j))
                continue;
            var d = c.DefOf(j);
            Freeform.PartWorld(c, j, out var pos, out var rot);
            var iq = Quaternion.Conjugate(rot);
            var local = HullHoles.Hole.Of(Vector3.Transform(mouth - pos, iq), Vector3.Transform(down, iq), Vector3.Transform(up, iq), HoleHalfW, halfH, len);
            var mn = d.HasSkin ? Vector3.Min(d.BoundsMin, d.SkinMin) : d.BoundsMin;
            var mx = d.HasSkin ? Vector3.Max(d.BoundsMax, d.SkinMax) : d.BoundsMax;
            if (!HullHoles.ReachesBounds(mn, mx, local))
                continue;
            if (HullHoles.WithHole(d, local) is { } h)
            {
                p.Def = h.Id;
                changed = true;
                holed++;
                world.Events.Add($"construct {b.Id}: its cutter holed construct {c.Id}'s {HullHoles.BaseOf(d).Name}");
                continue;
            }
            var why = HullHoles.LastRefusal ?? "";
            if (why == HullHoles.Misses)
                continue;
            var size = mx - mn;
            if (d.Explosive == null && !d.Core && size.X <= HoleBreakMost && size.Y <= HoleBreakMost && size.Z <= HoleBreakMost)
            {
                world.DamagePart(c, j, p.Health + 1f, out _, raw: true); // (a fitting in the way: burned away)
                burned++;
                world.Events.Add($"construct {b.Id}: its cutter burned away construct {c.Id}'s {d.Name} ({why})");
                continue;
            }
            Console.WriteLine($"[breach] construct {b.Id}: the hole can't take construct {c.Id}'s {d.Name} ({why}): left as it is");
        }
        if (changed)
        {
            c.Dirty = true;
            world.StructureDirty.Add(c.Id);
            c.Wake();
        }
        Console.WriteLine(FormattableString.Invariant($"[breach] construct {b.Id} cut a hole {HoleHalfW * 2:0.0} by {halfH * 2:0.0} m, {len:0.0} m deep, into construct {c.Id}: {holed} parts holed, {burned} burned{(inRoom ? "" : ", no room behind it")}"));
        return holed + burned > 0;
    }
}
