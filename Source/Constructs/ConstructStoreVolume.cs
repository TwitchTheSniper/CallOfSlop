using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// One thing a store holds, for anything that reads stores without knowing their kind (<see cref="ConstructWorld.Holdings"/>,
/// <see cref="ConstructWorld.TakeHolding"/>): a part kind (<see cref="Id"/> a part id) or a bulk material (a RecyclingTable id).
/// </summary>
public readonly record struct CHolding(bool IsPart, ushort Id, int Count);

/// <summary>
/// The Storage Volume (docs/sandbox/CONSTRUCTS.md "Storage volumes"): a storage part with a player-sized box of interior
/// space on its construct, sized with the configure tool exactly as a cargo volume is (its size and centre are its
/// settings, so its handles, panel and blueprints come free). The box holds one bulk material, as a bin does, or parts by
/// kind, never both at once. It isn't solid: the walls are the player's own panels. The whole box counts, as sized
/// (<see cref="Usable(Construct, int, out Vector3, out Vector3)"/>).
/// wire follow the store's handle as for any bin.
/// </summary>
public static class ConstructStoreVolume
{
    public const int MaxPerConstruct = 4;
    public const float MaxSide = 40f; // a box's side (metres; the part file's settings stop here too)
    public const int MaxBulk = 10_000_000; // units, whatever the box's size: every weight stays exact in a float and the inertia in range
    public const int MaxSpill = 24; // bundles or loose parts a destroyed volume leaves; the rest is lost

    public static readonly Vector3 PartsColour = new(0.48f, 0.42f, 0.33f); // (a level of stored parts: crates)

    /// <summary>The box in the frame of the part's segment, exactly as the player sized it (the developer, 2026-11-02: it isn't clipped to the construct).</summary>
    public static void Usable(Construct c, int part, Vector3 size, Vector3 at, out Vector3 min, out Vector3 max)
    {
        ConstructCargo.BoxFrom(c.Parts[part], size, at, out var centre, out var half);
        min = centre - half;
        max = centre + half;
    }

    /// <summary>The part's box as it stands (see <see cref="Usable(Construct, int, Vector3, Vector3, out Vector3, out Vector3)"/>).</summary>
    public static void Usable(Construct c, int part, out Vector3 min, out Vector3 max)
    {
        ConstructCargo.Settings(c.Parts[part], c.Parts[part].D, out var size, out var at);
        Usable(c, part, size, at, out min, out max);
    }

    private static float CubicMetres(Vector3 min, Vector3 max)
    {
        var s = max - min;
        return s.X * s.Y * s.Z;
    }

    /// <summary>Units of bulk for a usable box: <c>binUnitsPerCubicMetre</c> in Recycling.json, at most <see cref="MaxBulk"/>.</summary>
    public static int UnitsFor(Vector3 min, Vector3 max) => (int)MathF.Min(MaxBulk, MathF.Round(CubicMetres(min, max) * RecyclingTable.Current.UnitsPerCubicMetre));

    public static int Capacity(Construct c, int part)
    {
        Usable(c, part, out var min, out var max);
        return UnitsFor(min, max);
    }

    /// <summary>The room for parts (m3): the usable box less the packing gaps (<c>itemPackShare</c> in Recycling.json).</summary>
    public static float ItemRoom(Vector3 min, Vector3 max) => CubicMetres(min, max) * RecyclingTable.Current.ItemPackShare;

    /// <summary>What one part takes of that room: its bounding box, in m3, at least a litre.</summary>
    public static float SpaceOf(ConstructPartDef d) => MathF.Max(0.001f, CubicMetres(d.BoundsMin, d.BoundsMax));

    /// <summary>A store's fill as a box in the part's segment frame, rising along the part's up from the usable box's floor; false when empty. Over-full (its walls went) it stays at the top.</summary>
    public static bool Fill(Construct c, int part, CStore st, out Vector3 centre, out Vector3 half)
    {
        centre = half = Vector3.Zero;
        if (st.Amount <= 0 && st.KindN == 0)
            return false;
        Usable(c, part, out var min, out var max);
        var share = st.Amount > 0 ? st.Amount / MathF.Max(1, UnitsFor(min, max)) : st.ItemSpace / MathF.Max(1e-4f, ItemRoom(min, max));
        share = Math.Clamp(share, 0.02f, 1f);
        Level(c.Parts[part].Rot, ref min, ref max, share);
        centre = (min + max) * 0.5f;
        half = (max - min) * 0.5f;
        return true;
    }

    /// <summary>Cuts a box down to a <paramref name="share"/> of it, up from its floor along the up of a part turned <paramref name="rot"/>.</summary>
    public static void Level(byte rot, ref Vector3 min, ref Vector3 max, float share)
    {
        var up = Rot24.Apply(rot, Vector3.UnitY);
        var axis = MathF.Abs(up.X) > 0.5f ? 0 : MathF.Abs(up.Y) > 0.5f ? 1 : 2;
        var len = (max[axis] - min[axis]) * share;
        if (up[axis] > 0)
            max[axis] = min[axis] + len;
        else
            min[axis] = max[axis] - len;
    }

    /// <summary>The point of the usable box nearest <paramref name="world"/> (what a player must reach, however far the part itself is).</summary>
    public static Vector3 NearestPoint(Construct c, int part, Vector3 world)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        Usable(c, part, out var min, out var max);
        var local = Vector3.Transform(world - t, Quaternion.Conjugate(q));
        return t + Vector3.Transform(Vector3.Clamp(local, min, max), q);
    }

    /// <summary>Do this part's sides go in that box in some turn (sides compared longest to longest)?</summary>
    private static bool Fits(ConstructPartDef d, Vector3 min, Vector3 max)
    {
        var s = d.BoundsMax - d.BoundsMin;
        var b = max - min;
        return Sort(s).X <= Sort(b).X + 0.005f && Sort(s).Y <= Sort(b).Y + 0.005f && Sort(s).Z <= Sort(b).Z + 0.005f;
    }

    private static Vector3 Sort(Vector3 v)
    {
        var lo = MathF.Min(v.X, MathF.Min(v.Y, v.Z));
        var hi = MathF.Max(v.X, MathF.Max(v.Y, v.Z));
        return new Vector3(hi, v.X + v.Y + v.Z - lo - hi, lo);
    }

    /// <summary>Whether a part goes in this volume now. Allocates nothing (the look calls it every frame).</summary>
    public static SlotCheck Check(Construct c, int part, CStore st, ConstructPartDef d)
    {
        if (d.PhysicsOnly)
            return SlotCheck.PhysicsOnly;
        if (d.Role == PartRole.Storage)
            return SlotCheck.WrongShape; // (no storage inside storage: its contents would be lost)
        if (st.Amount > 0)
            return SlotCheck.HoldsBulk;
        Usable(c, part, out var min, out var max);
        if (!Fits(d, min, max))
            return SlotCheck.TooBig;
        var k = st.KindIndex(d.Id);
        if (k < 0 && st.KindN >= CStore.MaxKinds)
            return SlotCheck.TooManyKinds;
        if (k >= 0 && st.KindCount![k] >= CStore.MaxPerKind || st.ItemSpace + SpaceOf(d) > ItemRoom(min, max))
            return SlotCheck.Full;
        return SlotCheck.Ok;
    }

    /// <summary>Why <paramref name="d"/> can't go in (the server's refusal, said to the player), or null.</summary>
    public static string? Refusal(Construct c, int part, CStore st, ConstructPartDef d)
    {
        var name = c.DefOf(part).Name;
        var names = RecyclingTable.Current;
        return Check(c, part, st, d) switch
        {
            SlotCheck.Ok => null,
            SlotCheck.PhysicsOnly => ConstructEdits.PhysicsOnlyRefusal(d, $"go in the {name}"),
            SlotCheck.WrongShape => $"storage doesn't go in storage (the {d.Name} would lose what it holds)",
            SlotCheck.HoldsBulk => $"the {name} holds {names.NameOf(st.Material)}: it holds bulk or parts, not both",
            SlotCheck.TooBig => $"the {d.Name} doesn't fit the {name}'s box",
            SlotCheck.TooManyKinds => $"the {name} holds {CStore.MaxKinds} kinds of part already",
            _ => $"the {name} is full",
        };
    }

    /// <summary>The refusal for a box a player just set, or null: it must still take what the volume holds, and may not grow into another Storage Volume on this segment (<paramref name="overlap0"/>: the overlap it had before).</summary>
    public static string? ResizeRefusal(ConstructWorld world, Construct c, int part, float overlap0)
    {
        var st = world.StoreOf(c, part);
        var name = c.DefOf(part).Name;
        Usable(c, part, out var min, out var max);
        if (st != null && st.Amount > UnitsFor(min, max))
            return $"the {name} holds {st.Amount} {RecyclingTable.Current.NameOf(st.Material)}: that box takes only {UnitsFor(min, max)}. Empty it first";
        if (st != null && st.ItemSpace > ItemRoom(min, max))
            return $"the {name} holds {st.ItemCount} parts: they don't fit that box. Empty it first";
        if (Overlap(c, part) > overlap0 + 1e-3f)
            return $"that box runs into another {name}: their contents would share the space";
        return null;
    }

    /// <summary>How much of this volume's usable box (m3) other sized boxes on its segment (Storage or Tank Volumes) share.</summary>
    public static float Overlap(Construct c, int part)
    {
        Usable(c, part, out var min, out var max);
        var all = 0f;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (i == part || !c.Parts[i].D.IsSizedBox || c.Parts[i].Seg != c.Parts[part].Seg)
                continue;
            Usable(c, i, out var omin, out var omax);
            var s = Vector3.Min(max, omax) - Vector3.Max(min, omin);
            if (s.X > 0 && s.Y > 0 && s.Z > 0)
                all += s.X * s.Y * s.Z;
        }
        return all;
    }

    public static int Count(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.IsVolumeStore)
                n++;
        return n;
    }

    /// <summary>The look line for a Storage Volume: its level and contents, what F or a click does. Built only on a change (Program.StorageLook).</summary>
    public static string Look(Construct c, int part, CStore st, bool near, SandboxInventory inv, ConstructPartDef? held)
    {
        var d = c.DefOf(part);
        var names = RecyclingTable.Current;
        Usable(c, part, out var min, out var max);
        var cap = UnitsFor(min, max);
        var sb = new StringBuilder(d.Name).Append(": ");
        if (st.KindN > 0)
        {
            sb.Append((int)MathF.Round(100 * st.ItemSpace / MathF.Max(1e-4f, ItemRoom(min, max)))).Append("% full:");
            for (var k = st.KindN - 1; k >= 0 && k >= st.KindN - 3; k--)
                sb.Append(k == st.KindN - 1 ? " " : ", ").Append(ConstructParts.Get(st.KindDef![k])?.Name ?? "?").Append(" x").Append(st.KindCount![k]);
            if (st.KindN > 3)
                sb.Append("...");
        }
        else if (st.Amount > 0)
        {
            sb.Append(st.Amount).Append('/').Append(cap).Append(' ').Append(names.NameOf(st.Material));
            if (st.Amount > cap)
                sb.Append(" - over-full: nothing more goes in");
        }
        else
            sb.Append("0/").Append(cap).Append(", one material or parts");
        if (near && held != null)
        {
            if (Check(c, part, st, held) == SlotCheck.Ok)
                sb.Append(" - ").Append(SandboxKeys.Fire).Append(": put it in");
            else
                sb.Append(" - ").Append(Refusal(c, part, st, held));
        }
        else if (near && st.KindN > 0)
            sb.Append(" - ").Append(SandboxKeys.Use).Append(": take a ").Append(ConstructParts.Get(st.KindDef![st.KindN - 1])?.Name ?? "part");
        else if (near && ConstructEdits.TipMaterial(inv, d, st) is var tip and not 0)
            sb.Append(" - ").Append(SandboxKeys.Use).Append(": tip in ").Append(Math.Max(0, Math.Min(inv.Materials[tip], cap - st.Amount))).Append(' ').Append(names.NameOf(tip));
        else if (near && st.Amount > 0)
            sb.Append(" - ").Append(SandboxKeys.Use).Append(": take ").Append(Math.Min(ConstructStorage.TakeLoad, st.Amount)).Append(' ').Append(names.NameOf(st.Material));
        return sb.ToString();
    }
}

/// <summary>Parts in a Storage Volume, and the one read API over every kind of store (ConstructStoreVolume.cs).</summary>
public sealed partial class ConstructWorld
{
    /// <summary>Server: a part into a Storage Volume. Null, or why not.</summary>
    public string? PutPart(Construct c, int part, ConstructPartDef d)
    {
        if (StoreOf(c, part) is not { } st || !c.DefOf(part).IsVolumeStore)
            return "that isn't a Storage Volume";
        if (ConstructStoreVolume.Refusal(c, part, st, d) is { } why)
            return why;
        st.AddKind(d.Id, 1);
        LoadChanged = true;
        Changed(st);
        c.Wake();
        return null;
    }

    /// <summary>Server: one part of a kind out of a Storage Volume (null: none left).</summary>
    public ConstructPartDef? TakePart(Construct c, int part, ushort def)
    {
        if (StoreOf(c, part) is not { } st || st.KindIndex(def) < 0 || ConstructParts.Get(def) is not { } d)
            return null;
        st.AddKind(def, -1);
        LoadChanged = true;
        Changed(st);
        c.Wake();
        return d;
    }

    /// <summary>
    /// What any store holds, one line a kind: slots (piles of one part added up), a Storage Volume's parts, a bin's bulk (a
    /// production machine's output; its input is never taken from). Returns how many were written; a span of
    /// <see cref="CStore.MaxKinds"/> + 1 always has room. Works on either side from the store as heard. Allocates nothing.
    /// </summary>
    public int Holdings(Construct c, int part, Span<CHolding> into)
    {
        if (StoreOf(c, part) is not { } st)
            return 0;
        var n = 0;
        for (var k = 0; k < st.SlotCount; k++)
        {
            if (st.Def[k] == ConstructParts.None)
                continue;
            var at = 0;
            while (at < n && into[at].Id != st.Def[k])
                at++;
            if (at < n)
                into[at] = into[at] with { Count = into[at].Count + st.Count[k] };
            else if (n < into.Length)
                into[n++] = new CHolding(true, st.Def[k], st.Count[k]);
        }
        for (var k = 0; k < st.KindN && n < into.Length; k++)
            into[n++] = new CHolding(true, st.KindDef![k], st.KindCount![k]);
        if (st.Amount > 0 && n < into.Length)
            into[n++] = new CHolding(false, st.Material, st.Amount);
        return n;
    }

    /// <summary>
    /// Server: up to <paramref name="count"/> of what <paramref name="what"/> names out of any store (<see cref="Holdings"/>'s
    /// IsPart and Id; its Count is ignored). Returns how many came out: the caller gives them to someone. The owner rules
    /// (<c>ConstructEdits.Contested</c>) and the reach are the caller's, as for every other store call.
    /// </summary>
    public int TakeHolding(Construct c, int part, CHolding what, int count)
    {
        if (StoreOf(c, part) is not { } st || count <= 0)
            return 0;
        if (!what.IsPart)
            return st.Material == what.Id ? Withdraw(c, part, count) : 0;
        var got = 0;
        for (var k = 0; k < st.SlotCount && got < count; k++)
            while (got < count && st.Def[k] == what.Id && TakeOut(c, part, k) != null)
                got++;
        while (got < count && TakePart(c, part, what.Id) != null)
            got++;
        return got;
    }

    /// <summary>A destroyed Storage Volume's parts: loose where its fill was, at most <see cref="ConstructStoreVolume.MaxSpill"/>. How many came out; the rest are lost.</summary>
    private void SpillParts(Construct c, CStore st, Vector3 mid, Quaternion q, Vector3 t, ref int spilled, ref int lost)
    {
        var made = 0;
        for (var k = 0; k < st.KindN; k++)
        {
            if (ConstructParts.Get(st.KindDef![k]) is not { } d)
                continue;
            for (var n = 0; n < st.KindCount![k]; n++)
            {
                if (made >= ConstructStoreVolume.MaxSpill)
                {
                    lost += st.KindCount[k] - n;
                    break;
                }
                var at = t + Vector3.Transform(mid + new Vector3((made % 4 - 1.5f) * 0.45f, 0.35f * (made / 8), (made / 4 % 2 - 0.5f) * 0.45f), q);
                if (Spawn(d, at, c.PointVel(at) + SpillKick(k, n)) is { } loose)
                {
                    loose.Owner = 255;
                    spilled++;
                }
                else
                    lost++;
                made++;
            }
        }
    }
}
