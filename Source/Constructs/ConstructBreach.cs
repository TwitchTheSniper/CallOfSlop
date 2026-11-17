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
/// Boarding craft (Karl, 2026-11-17: the Zero Day's Limpet, docs/sandbox/constructs/limpet.md): a craft with **hull grapples**
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
    private static void Face(Construct b, int i, out Vector3 at, out Vector3 down)
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

    /// <summary>Is the gripped body's hull still under a grapple that is on (its reach and a little more: the grip holds it there)?</summary>
    private static bool OnHull(ConstructWorld world, Construct b, Construct c)
    {
        for (var i = 0; i < b.PartCount; i++)
        {
            if (b.DefOf(i).Breach != BreachKind.Grapple || !On(b, i))
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
                continue;
            }
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
}
