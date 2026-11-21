using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Packed groups (Sean, 2026-11-21: "welded groups could go on storage pads ... I like how they arrange the objects a lot better than
/// cargo volumes"): a store keeps kinds and counts, so a welded group set down on a Lay-down Pad or a Pad Tile goes in as one kind,
/// <c>Packed (#hash)</c>, made from the same design store as a baked part (BakedParts.cs): one line a part, its place and turn, its paint
/// and finish. Unlike a baked part, any part may be in it (it never works while packed), and it is never a part of a body for long:
/// whatever brings it out of a store (a hand, an arm, a crane, a spill) makes a body of it, and the server turns that body back into
/// the group at the next lifters' scan (<see cref="ConstructLifters.Scan"/>). The design is written in the pad's frame, the group's
/// longest side along the pad, so the same group set down either way round is the same kind and stacks with its fellows.
/// </summary>
public static partial class BakedParts
{
    public const string PackedPrefix = "Packed (#";

    public static string PackedKeyOf(string hash) => PackedPrefix + hash + ")";

    public static bool IsPacked(ConstructPartDef d) => d.IsNamedRuntime && d.Name.StartsWith(PackedPrefix, StringComparison.Ordinal);

    private static readonly byte Yaw90 = Rot24.Nearest(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), out _);

    /// <summary>
    /// Why welded group <paramref name="lo"/> can't go into a store as one piece (null: it can). A store keeps the kind and the count,
    /// so each part must be as new, as a single piece must (whole, empty, no rounds, text, queue or fluid, settings at their defaults),
    /// though paint and finish are kept; and the group is rigid: no joints, every part at a grid turn, no wires.
    /// </summary>
    public static string? GroupWhy(ConstructWorld world, Construct lo)
    {
        if (lo.PartCount < 2)
            return "not a group";
        if (lo.PartCount > MaxSubparts)
            return $"over {MaxSubparts} parts";
        if (lo.Anchored)
            return "it is fixed in place";
        if (lo.JointCount > 0)
            return "it has a joint (a hinge or a moving part): only a rigid group packs";
        if (lo.WireCount > 0)
            return "it is wired: a store keeps no wires";
        for (var i = 0; i < lo.PartCount; i++)
        {
            ref readonly var p = ref lo.Parts[i];
            var d = lo.DefOf(i);
            if (!Place(lo, i, out _))
                return $"the {Shown(d)} isn't fixed at a grid turn";
            if (d.Role == PartRole.Seat)
                return $"the {Shown(d)}: a seat makes it a craft";
            if (d.IsPieceRack || d.Lifter != null)
                return $"the {Shown(d)}: a rack or a lifter";
            if (IsPacked(d))
                return "a packed group in it";
            if (p.Broken || p.Health < d.Health * 0.999f)
                return $"the {Shown(d)} is damaged: mend it first";
            if (p.Store != 0 && world.StoreOf(lo, i) is { } st && (st.Amount > 0 || st.ItemCount > 0 || st.InAmount > 0 || st.KindN > 0))
                return $"the {Shown(d)} {ConstructLifters.LoadedWhy}";
            if (p.Rounds != 0 || p.Text != null || p.Text2 != null || p.Orders != null || p.Craft != null || !ConstructConfig.AllDefault(p, d)
                || d.Role == PartRole.FluidTank && ConstructFluid.Held(p, d) != null)
                return $"the {Shown(d)} holds rounds, fluid, text or settings: empty or reset it first";
        }
        return null;
    }

    /// <summary>
    /// Server or client: the packed part group <paramref name="lo"/> goes into a store as, seen from a store whose frame turns by
    /// <paramref name="storeRot"/> (null: it can't; <paramref name="why"/> says why). Kept on the body until its parts change or it
    /// lies another way, so a pad's look each second makes nothing.
    /// </summary>
    public static ConstructPartDef? PackOf(ConstructWorld world, Construct lo, Quaternion storeRot, out string? why)
    {
        if (lo.Dirty)
            lo.Rebuild();
        var turn = Rot24.Nearest(Quaternion.Conjugate(storeRot) * lo.Rot, out _);
        if (lo.PackStamp == lo.RebuildStamp && lo.PackParts == lo.PartCount && lo.PackTurn == turn)
        {
            why = lo.PackWhy;
            return lo.PackDef;
        }
        (lo.PackStamp, lo.PackParts, lo.PackTurn) = (lo.RebuildStamp, lo.PartCount, turn);
        lo.PackDef = Pack(world, lo, turn, out why);
        lo.PackWhy = why;
        return lo.PackDef;
    }

    private static ConstructPartDef? Pack(ConstructWorld world, Construct lo, byte turn, out string? why)
    {
        if (GroupWhy(world, lo) is { } gw)
        {
            why = gw;
            return null;
        }
        var placed = new Placed[lo.PartCount];
        for (var i = 0; i < lo.PartCount; i++)
            Place(lo, i, out placed[i]);
        string? best = null;
        var yaw = turn;
        for (var k = 0; k < 4; k++, yaw = Rot24.Compose(Yaw90, yaw))
        {
            // The parts in the store's frame at this yaw; the longest side across the store's length (Z) is the one a rack packs by.
            Vector3 lo3 = new(float.MaxValue), hi3 = new(float.MinValue);
            for (var i = 0; i < placed.Length; i++)
            {
                var d = lo.DefOf(i);
                var at = Rot24.Apply(yaw, placed[i].Pos);
                var r = Rot24.Compose(yaw, placed[i].Rot);
                for (var b = 0; b < d.SolidBoxes; b++)
                {
                    var bx = d.Boxes[b];
                    var c = at + Rot24.Apply(r, bx.Center);
                    var h = Rot24.ApplyHalf(r, bx.Half);
                    (lo3, hi3) = (Vector3.Min(lo3, c - h), Vector3.Max(hi3, c + h));
                }
            }
            if (hi3.X - lo3.X > hi3.Z - lo3.Z + 0.001f)
                continue;
            var mid = (lo3 + hi3) * 0.5f;
            var origin = new Vector3(MathF.Round(mid.X * 100), MathF.Round(mid.Y * 100), MathF.Round(mid.Z * 100)) / 100f;
            var subs = new Sub[placed.Length];
            for (var i = 0; i < placed.Length; i++)
            {
                ref readonly var p = ref lo.Parts[i];
                var rel = (Rot24.Apply(yaw, placed[i].Pos) - origin) * 100;
                subs[i] = new Sub(lo.DefOf(i).Name, (int)MathF.Round(rel.X), (int)MathF.Round(rel.Y), (int)MathF.Round(rel.Z), Rot24.Compose(yaw, placed[i].Rot),
                    ConstructPaint.IsSet(p.Paint) ? p.Paint : 0, ConstructFinish.Name(p.Finish));
            }
            var text = TextOf(subs);
            if (best == null || string.CompareOrdinal(text, best) < 0)
                best = text; // (the same group, either end first: the same text, so one kind)
        }
        if (best == null || Register(best) is not { } hash)
        {
            why = $"its design can't be kept ({LastRefusal})";
            return null;
        }
        if (Parse(PackedKeyOf(hash)) is not { } def)
        {
            why = $"it can't be made one piece ({LastRefusal})";
            return null;
        }
        why = null;
        return def;
    }

    /// <summary>
    /// Server: a body of one packed part (out of a store) becomes its group again where it lies: the parts at their places, welded where
    /// their sockets meet, their paint and finish, the damage shared out. Null: it can't (<see cref="LastRefusal"/>); it stays packed.
    /// </summary>
    public static Construct? Unpack(ConstructWorld world, Construct c)
    {
        if (c.PartCount != 1 || !IsPacked(c.DefOf(0)) || DesignOf(c.DefOf(0)) is not { } design)
        {
            LastRefusal = "not a packed group";
            return null;
        }
        var pd = c.DefOf(0);
        ref var pp = ref c.Parts[0];
        var share = Math.Clamp(pp.Health / MathF.Max(pd.Health, 1f), 0f, 1f);
        var defs = new ConstructPartDef[design.Subs.Length];
        for (var k = 0; k < defs.Length; k++)
            if (ConstructParts.Find(design.Subs[k].Name) is { } sd)
                defs[k] = sd;
            else
            {
                LastRefusal = $"no part \"{design.Subs[k].Name}\" in this build";
                return null;
            }
        if (world.Create(c.Pos, c.Rot) is not { } nb)
        {
            LastRefusal = "the world is full";
            return null;
        }
        var n = 0;
        for (var k = 0; k < defs.Length; k++)
        {
            var s = design.Subs[k];
            var i = world.AddPart(nb, defs[k], pp.Pos + Rot24.Apply(pp.Rot, s.Pos), Rot24.Compose(pp.Rot, s.Rot));
            if (i < 0)
                continue;
            n++;
            ref var np = ref nb.Parts[i];
            np.Health = MathF.Max(1f, defs[k].Health * share);
            np.Paint = s.Paint;
            np.Finish = ConstructFinish.Of(s.Finish);
        }
        if (n < defs.Length)
        {
            world.Remove(nb, quiet: true);
            LastRefusal = "the world is full";
            return null;
        }
        world.AutoLinkMany(nb, 0, nb.PartCount); // (welded again where the group's sockets meet)
        Settle(world, c, nb);
        world.Events.Add($"construct {c.Id}: unpacked {pd.Name} into {n} parts -> construct {nb.Id}");
        return nb;
    }
}

public static partial class ConstructLifters
{
    /// <summary>
    /// Server, with the scan: bodies of one packed part (out of a store) become their groups again, once nobody holds them (a crane, a
    /// hand, the physics gun). A body whose group can't be made stays packed and is said once.
    /// </summary>
    private static void UnpackWaiting(ConstructWorld world)
    {
        for (var k = world.PackedBodies.Count - 1; k >= 0; k--)
        {
            if (world.Get(world.PackedBodies[k]) is not { } lo || lo.PartCount != 1 || !BakedParts.IsPacked(lo.DefOf(0)))
            {
                world.PackedBodies.RemoveAt(k);
                continue;
            }
            if (lo.HeldBy != 0 || lo.GrabbedBy != 255 || lo.CarriedBy != 0 || lo.NudgeBy != 255 || lo.Frozen)
                continue; // (someone has it: when they let go)
            world.PackedBodies.RemoveAt(k);
            if (BakedParts.Unpack(world, lo) is { } nb)
                Console.WriteLine($"[lifter] a packed group out of a store: construct {lo.Id} unpacked into {nb.PartCount} parts (construct {nb.Id})");
            else
                Console.WriteLine($"[lifter] REFUSED unpacking construct {lo.Id} ({lo.DefOf(0).Name}): {BakedParts.LastRefusal}; it stays one piece");
        }
    }
}

public sealed partial class Construct
{
    // (BakedParts.PackOf: what this body packs into, worked out at this RebuildStamp, part count and turn to the store)
    public int PackStamp = -1, PackParts = -1;
    public byte PackTurn;
    public ConstructPartDef? PackDef;
    public string? PackWhy;
}

public sealed partial class ConstructWorld
{
    public readonly List<ushort> PackedBodies = new(8); // server: bodies of one packed part waiting to be their group again (PackedGroups.cs)
}
