using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>One body a carrier carries, as its mass sees it (the carrier's body frame): its mass, centre and half size.</summary>
public readonly record struct CCargoMass(float Mass, Vector3 Centre, Vector3 Half);

/// <summary>
/// Cargo volumes (the developer's decision 21, board item dev-sandbox-cargo-volumes-cv01; docs/sandbox/CONSTRUCTS.md,
/// "Cargo volumes"): a Cargo Volume part is a small control panel with a box of space on its construct (its size and
/// centre are its settings, so the configure tool's panel, its face handles and blueprints all set them). The panel is
/// frozen or not: F on it flips it, and a change of its data input sets it (the last change wins). While it is frozen,
/// every loose body mostly inside the box (its centre of mass in it and at least half its part mass) rides with the
/// construct kinematically: no physics of its own, no collisions with the carrier or the carrier's other cargo, its mass
/// and centre of mass counted in the carrier's. Unfreezing (or the volume going) lets everything go with the carrier's
/// velocity at that point. Something that enters while it is frozen is caught once it has settled (moving with the
/// construct, under <see cref="SettleSpeed"/> relative to it); a thing held by the physics gun is never caught until it
/// is let go. Server-authoritative: a carried body's structure message carries its carrier and its pose in the frame of
/// the volume's segment, and every machine moves it with the carrier.
/// </summary>
public static class ConstructCargo
{
    public const int MaxVolumes = 4; // cargo volumes a construct (more are refused when fitted)
    public const int MaxPerConstruct = 24; // bodies a construct carries at once (the rest stay loose)
    public const float MinSide = 0.2f, MaxSide = 8f; // a volume's box, each side (metres)
    public const int ScanEvery = 6; // ticks between looks for what entered or left (10 Hz)
    public const float SettleSpeed = 1f; // m/s relative to the carrier under which something entering is caught
    public const float InsideShare = 0.5f; // of a body's part mass that must be in the box ("mostly inside")
    public const float LeaveMargin = 0.3f; // a carried body's centre this far out of its box (it was resized) lets it go

    public static readonly Vector3 DefaultSize = new(2f, 1f, 2f);

    public static bool IsVolume(ConstructPartDef d) => d.Role == PartRole.Logic && d.Logic == CLogic.Cargo;

    /// <summary>A volume part that is frozen now (its signal; on a client too).</summary>
    public static bool IsFrozen(in CPart p) => !p.Broken && p.Out >= 0.5f;

    /// <summary>The volume's size and centre from its settings (the part's frame).</summary>
    public static void Settings(in CPart p, ConstructPartDef d, out Vector3 size, out Vector3 at)
    {
        size = new Vector3(ConstructConfig.Get(p, d, CfgKey.SizeX, DefaultSize.X), ConstructConfig.Get(p, d, CfgKey.SizeY, DefaultSize.Y), ConstructConfig.Get(p, d, CfgKey.SizeZ, DefaultSize.Z));
        at = new Vector3(ConstructConfig.Get(p, d, CfgKey.AtX, 0), ConstructConfig.Get(p, d, CfgKey.AtY, DefaultSize.Y * 0.5f), ConstructConfig.Get(p, d, CfgKey.AtZ, 0));
    }

    /// <summary>The volume's box in its segment's rest frame (axis-aligned there: every part turn is a Rot24).</summary>
    public static void Box(Construct c, int part, out Vector3 centre, out Vector3 half)
    {
        ref var p = ref c.Parts[part];
        Settings(p, p.D, out var size, out var at);
        BoxFrom(p, size, at, out centre, out half);
    }

    /// <summary>The box a volume part would have with this size and centre (the configure tool's handles preview it).</summary>
    public static void BoxFrom(in CPart p, Vector3 size, Vector3 at, out Vector3 centre, out Vector3 half)
    {
        centre = p.Pos + Rot24.Apply(p.Rot, at);
        half = Rot24.ApplyHalf(p.Rot, size * 0.5f);
    }

    /// <summary>The cargo volume parts a construct has.</summary>
    public static int CountVolumes(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (IsVolume(c.Parts[i].D))
                n++;
        return n;
    }

    /// <summary>What a carried body's carrier carries it in: the volume part (found again by its tag after parts are renumbered), or -1.</summary>
    private static int VolumeOf(Construct carrier, Construct b)
    {
        if (b.CarryPart < carrier.PartCount && carrier.Parts[b.CarryPart].Tag == b.CarryTag && IsVolume(carrier.Parts[b.CarryPart].D))
            return b.CarryPart;
        for (var i = 0; i < carrier.PartCount; i++)
            if (carrier.Parts[i].Tag == b.CarryTag && IsVolume(carrier.Parts[i].D))
            {
                b.CarryPart = (byte)i;
                return i;
            }
        return -1;
    }

    /// <summary>Is a world point inside a volume's box (grown by <paramref name="margin"/>)?</summary>
    private static bool Inside(Construct c, int part, Vector3 world, float margin)
    {
        Box(c, part, out var centre, out var half);
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var local = Vector3.Transform(world - t, Quaternion.Conjugate(q)) - centre;
        var h = half + new Vector3(margin);
        return MathF.Abs(local.X) <= h.X && MathF.Abs(local.Y) <= h.Y && MathF.Abs(local.Z) <= h.Z;
    }

    /// <summary>Is body <paramref name="b"/> mostly inside the volume: its centre of mass in the box and at least <see cref="InsideShare"/> of its part mass (by part centres)?</summary>
    public static bool MostlyInside(Construct c, int part, Construct b)
    {
        if (b.Dirty)
            b.Rebuild();
        if (!Inside(c, part, b.WorldCom, 0.02f))
            return false;
        float inside = 0, all = 0;
        for (var i = 0; i < b.PartCount; i++)
        {
            var m = b.DefOf(i).Mass;
            all += m;
            b.WorldFrame(b.Parts[i].Seg, out var q, out var t);
            b.PartBounds(i, out var mn, out var mx);
            if (Inside(c, part, t + Vector3.Transform((mn + mx) * 0.5f, q), 0.02f))
                inside += m;
        }
        return all > 0 && inside >= all * InsideShare;
    }

    /// <summary>Why <paramref name="b"/> can't be caught by carrier <paramref name="c"/> (null: it can).</summary>
    private static bool Catchable(ConstructWorld world, Construct c, Construct b)
    {
        if (b == c || b.CarriedBy != 0 || b.CargoCount > 0 || b.PartCount == 0 || b.Anchored || b.Pinned || b.Frozen || b.GrabbedBy != 255 || b.Driver != 255)
            return false;
        for (var i = 0; i < b.PartCount; i++)
            if (b.Parts[i].SitterPlus1 != 0)
                return false;
        for (var k = 0; k < world.CouplingCount; k++)
        {
            ref var cp = ref world.Couplings[k];
            if (cp.BodyA == b.Id && cp.BodyB == c.Id || cp.BodyA == c.Id && cp.BodyB == b.Id)
                return false; // (a trailer hitched to it stays a trailer)
        }
        return true;
    }

    /// <summary>Server: <paramref name="b"/> rides in volume <paramref name="part"/> of <paramref name="c"/> from now on (where it is now).</summary>
    public static void Catch(ConstructWorld world, Construct c, int part, Construct b)
    {
        if (c.Dirty)
            c.Rebuild();
        if (b.Dirty)
            b.Rebuild();
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        var inv = Quaternion.Conjugate(q);
        // The carrier takes the cargo's momentum (a free carrier; an anchored one holds still).
        if (!c.Anchored && c.Mass + b.Mass > 0)
            c.Vel = (c.Vel * c.Mass + b.Vel * b.Mass) / (c.Mass + b.Mass);
        b.CarriedBy = c.Id;
        b.CarryTag = p.Tag;
        b.CarryPart = (byte)part;
        b.CarrySeg = p.Seg;
        b.CarryPos = Vector3.Transform(b.Pos - t, inv);
        b.CarryRot = Quaternion.Normalize(inv * b.Rot);
        b.Asleep = true;
        world.StructureDirty.Add(b.Id);
        world.CargoChanged = true;
        world.Events.Add($"construct {c.Id}: the {c.DefOf(part).Name} caught construct {b.Id} ({b.Mass:0} kg)");
    }

    /// <summary>Server: <paramref name="b"/> rides no longer: normal physics again, at the velocity its carrier gave it there.</summary>
    public static void Release(ConstructWorld world, Construct b)
    {
        if (b.CarriedBy == 0)
            return;
        if (world.Get(b.CarriedBy) is { } c)
        {
            var at = b.WorldCom;
            b.Vel = c.PointVel(at) + c.SegPointVel(b.CarrySeg, at);
            b.AngVel = c.AngVel;
            world.Events.Add($"construct {c.Id}: let construct {b.Id} go");
        }
        b.CarriedBy = 0;
        b.CarryTag = 0;
        b.Wake();
        world.StructureDirty.Add(b.Id);
        world.CargoChanged = true;
    }

    /// <summary>A carried body's pose from its carrier's (the server every tick; a client every frame from the blended poses).</summary>
    public static void Follow(Construct carrier, Construct b)
    {
        var seg = b.CarrySeg <= carrier.JointCount ? b.CarrySeg : 0;
        carrier.WorldFrame(seg, out var q, out var t);
        b.Pos = t + Vector3.Transform(b.CarryPos, q);
        b.Rot = Quaternion.Normalize(q * b.CarryRot);
        var at = b.WorldCom;
        b.Vel = carrier.PointVel(at) + carrier.SegPointVel(seg, at);
        b.AngVel = carrier.AngVel;
    }

    /// <summary>Client, every frame after the poses are blended: every carried body drawn where its carrier holds it.</summary>
    public static void FollowAll(ConstructWorld world)
    {
        foreach (var b in world.Bodies)
            if (b.CarriedBy != 0 && world.Get(b.CarriedBy) is { } c && c.CarriedBy == 0)
                Follow(c, b);
    }

    /// <summary>
    /// Server, every tick after the physics: carried bodies follow (or are let go when their volume unfroze, broke or
    /// went); ten times a second (and at once after a volume changes state) what has entered a frozen volume is caught and
    /// what has left one is let go; the carriers' masses follow. Allocates nothing.
    /// </summary>
    public static void Step(ConstructWorld world)
    {
        var bodies = world.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b.CarriedBy == 0)
                continue;
            var c = world.Get(b.CarriedBy);
            var part = c == null || c.CarriedBy != 0 ? -1 : VolumeOf(c, b);
            if (c == null || part < 0 || !IsFrozen(c.Parts[part]))
            {
                Release(world, b);
                continue;
            }
            b.CarrySeg = c.Parts[part].Seg;
            Follow(c, b);
            b.Asleep = true;
            b.SleepTicks = 0;
        }
        if (++world.CargoTicks < ScanEvery && !world.CargoScanNow)
        {
            if (world.CargoChanged)
                Refresh(world);
            return;
        }
        world.CargoTicks = 0;
        world.CargoScanNow = false;
        Scan(world);
        Refresh(world);
    }

    private static void Scan(ConstructWorld world)
    {
        var bodies = world.Bodies;
        // What left its box (a volume made smaller) goes.
        for (var i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b.CarriedBy != 0 && world.Get(b.CarriedBy) is { } c && VolumeOf(c, b) is var v and >= 0 && !Inside(c, v, b.WorldCom, LeaveMargin))
                Release(world, b);
        }
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (c.CarriedBy != 0)
                continue; // (no cargo in cargo)
            for (var part = 0; part < c.PartCount; part++)
            {
                ref var p = ref c.Parts[part];
                if (!IsVolume(p.D))
                    continue;
                var frozen = IsFrozen(p);
                var fresh = frozen && (p.State & 4) == 0; // (just frozen: everything inside is caught, moving or not)
                p.State = (byte)(frozen ? p.State | 4 : p.State & ~4);
                if (!frozen)
                    continue;
                Box(c, part, out var centre, out var half);
                c.WorldFrame(p.Seg, out var q, out var t);
                var world0 = t + Vector3.Transform(centre, q);
                var reach0 = half.Length();
                for (var bi = 0; bi < bodies.Count; bi++)
                {
                    if (CarriedCount(world, c) >= MaxPerConstruct)
                        break;
                    var b = bodies[bi];
                    if (!Catchable(world, c, b))
                        continue;
                    var reach = reach0 + b.Radius;
                    if (Vector3.DistanceSquared(b.WorldCom, world0) > reach * reach)
                        continue;
                    if (!fresh && !b.Asleep && (b.Vel - c.PointVel(b.WorldCom)).LengthSquared() > SettleSpeed * SettleSpeed)
                        continue; // (still falling in, or driving through: caught once it settles)
                    if (MostlyInside(c, part, b))
                        Catch(world, c, part, b);
                }
            }
        }
    }

    private static int CarriedCount(ConstructWorld world, Construct c)
    {
        var n = 0;
        foreach (var b in world.Bodies)
            if (b.CarriedBy == c.Id)
                n++;
        return n;
    }

    /// <summary>Every carrier's cargo list (its mass and where it sits, body frame); a carrier whose list changed rebuilds its mass.</summary>
    private static void Refresh(ConstructWorld world)
    {
        world.CargoChanged = false;
        var bodies = world.Bodies;
        foreach (var c in bodies)
        {
            var n = 0;
            var mass = 0f;
            var changed = false;
            foreach (var b in bodies)
            {
                if (b.CarriedBy != c.Id || n >= MaxPerConstruct)
                    continue;
                if (b.Dirty)
                    b.Rebuild();
                var rel = Quaternion.Conjugate(c.Rot) * b.Rot;
                var m = Matrix4x4.CreateFromQuaternion(rel);
                var h = (b.LocalMax - b.LocalMin) * 0.5f;
                var half = new Vector3(
                    MathF.Abs(m.M11) * h.X + MathF.Abs(m.M21) * h.Y + MathF.Abs(m.M31) * h.Z,
                    MathF.Abs(m.M12) * h.X + MathF.Abs(m.M22) * h.Y + MathF.Abs(m.M32) * h.Z,
                    MathF.Abs(m.M13) * h.X + MathF.Abs(m.M23) * h.Y + MathF.Abs(m.M33) * h.Z);
                var entry = new CCargoMass(b.Mass, c.ToLocal(b.WorldCom), half);
                if (n >= c.CargoCount || MathF.Abs(c.Cargo[n].Mass - entry.Mass) > 0.01f || Vector3.DistanceSquared(c.Cargo[n].Centre, entry.Centre) > 1e-4f)
                    changed = true;
                c.Cargo[n++] = entry;
                mass += entry.Mass;
            }
            if (n != c.CargoCount)
                changed = true;
            c.CargoCount = n;
            c.CargoMass = mass;
            if (changed)
                c.Dirty = true; // (its mass, centre of mass and inertia are rebuilt with the cargo in)
        }
    }

    // ---- Blueprints: the cargo a blueprint is built carrying ---------------------------------------------------------

    /// <summary>One loose part a blueprint carries: its kind, its origin in the construct's frame and its Rot24 turn there.</summary>
    public readonly record struct CargoSpec(string Part, Vector3 At, byte Turn);

    /// <summary>A blueprint's <c>"cargo"</c> list: <c>[{ "part": "Crate", "at": [x, y, z], "turn": 0 }]</c>.</summary>
    public static CargoSpec ParseSpec(JsonElement e, string file, int k)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: cargo[{k}] is {{\"part\", \"at\", \"turn\"}}");
        string part = "";
        var at = Vector3.Zero;
        byte turn = 0;
        foreach (var f in e.EnumerateObject())
            switch (f.Name)
            {
                case "part": part = f.Value.GetString() ?? ""; break;
                case "at":
                    if (f.Value.ValueKind != JsonValueKind.Array || f.Value.GetArrayLength() != 3)
                        throw new ConstructPartException($"{file}: cargo[{k}].at is [x, y, z]");
                    at = new Vector3(f.Value[0].GetSingle(), f.Value[1].GetSingle(), f.Value[2].GetSingle());
                    break;
                case "turn":
                    var tn = f.Value.GetInt32();
                    if (tn < 0 || tn >= Rot24.Count)
                        throw new ConstructPartException($"{file}: cargo[{k}].turn is 0 to {Rot24.Count - 1}");
                    turn = (byte)tn;
                    break;
                default:
                    throw new ConstructPartException($"{file}: cargo[{k}]: unknown key \"{f.Name}\" (part, at, turn)");
            }
        if (ConstructParts.Find(part) == null)
            throw new ConstructPartException($"{file}: cargo[{k}]: no part named \"{part}\"");
        if (!float.IsFinite(at.X + at.Y + at.Z) || at.Length() > 30)
            throw new ConstructPartException($"{file}: cargo[{k}].at is more than 30 m out");
        return new CargoSpec(part, at, turn);
    }

    /// <summary>
    /// A blueprint's cargo, built: each one a loose part at its place on construct <paramref name="c"/>, caught by the
    /// frozen volume it is in (a volume a blueprint step sets to start frozen). Null, or why not (the caller removes the lot).
    /// </summary>
    public static string? BuildCargo(ConstructWorld world, Construct c, IReadOnlyList<CargoSpec> cargo, List<Construct> made)
    {
        if (cargo.Count == 0)
            return null;
        c.Rebuild();
        for (var i = 0; i < c.PartCount; i++)
            if (IsVolume(c.Parts[i].D))
                c.Parts[i].Out = c.Parts[i].OutNext = (c.Parts[i].State & 1) != 0 ? 1 : 0; // (its state now, before the first logic step)
        for (var k = 0; k < cargo.Count; k++)
        {
            var spec = cargo[k];
            var def = ConstructParts.Need(spec.Part);
            var rot = Quaternion.Normalize(c.Rot * Rot24.Quat(spec.Turn));
            if (ConstructBuild.PlaceNew(world, def, new FreePlacement { Pos = c.ToWorld(spec.At), Rot = rot }) is not { } b)
                return $"cargo[{k}]: the world is full";
            made.Add(b);
            b.Rebuild();
            var vol = -1;
            for (var i = 0; i < c.PartCount && vol < 0; i++)
                if (IsVolume(c.Parts[i].D) && IsFrozen(c.Parts[i]) && MostlyInside(c, i, b))
                    vol = i;
            if (vol < 0)
                return $"cargo[{k}]: the {def.Name} isn't inside a cargo volume that starts frozen";
            Catch(world, c, vol, b);
        }
        Refresh(world);
        c.Rebuild();
        return null;
    }

    /// <summary>Construct <paramref name="c"/>'s cargo as a blueprint's <c>"cargo"</c> list (the loose single parts it carries; null: none).</summary>
    public static string? ToJson(ConstructWorld world, Construct c)
    {
        var sb = new StringBuilder();
        foreach (var b in world.Bodies)
        {
            if (b.CarriedBy != c.Id || b.PartCount != 1)
                continue;
            var at = c.ToLocal(b.Pos);
            // (the part's whole turn in the construct's frame: its body's turn there, then its own in the body)
            var rel = Quaternion.Normalize(Quaternion.Conjugate(c.Rot) * b.Rot * Rot24.Quat(b.Parts[0].Rot));
            var turn = Rot24.Nearest(rel, out _);
            at = c.ToLocal(b.ToWorld(b.Parts[0].Pos));
            sb.Append(sb.Length == 0 ? "[" : ", ");
            sb.Append(CultureInfo.InvariantCulture, $"{{ \"part\": \"{b.DefOf(0).Name}\", \"at\": [{at.X:0.###}, {at.Y:0.###}, {at.Z:0.###}], \"turn\": {turn} }}");
        }
        return sb.Length == 0 ? null : sb.Append(']').ToString();
    }
}

/// <summary>The world's side of cargo volumes (ConstructCargo.cs).</summary>
public sealed partial class ConstructWorld
{
    internal int CargoTicks;
    /// <summary>A volume changed state (frozen or not): the cargo step looks at once rather than at its next 10 Hz look.</summary>
    public bool CargoScanNow;
    internal bool CargoChanged;
}
