using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Whether a part goes into a storage slot (<see cref="ConstructStorage.Check"/>).</summary>
public enum SlotCheck : byte
{
    Ok,
    NoSlot,
    WrongShape, // the slot takes only some roles (a wheel)
    TooBig, // its box doesn't fit the slot's in any turn
    Taken,
    OtherKind, // a pile of another part
    Full, // a pile at its limit
    TooHeavy, // heavier than the container takes (a backpack's maxMass)
    PhysicsOnly, // decision 21: a welded, heavy or bulky part is never an item (ConstructPartDef.PhysicsOnly)
}

/// <summary>
/// One slot of a storage part (decision 18, physical storage): the box in the part's frame a stored part sits in (on its
/// floor, centred), the roles that fit it (none listed: any part that fits the box), and how many of one kind it piles
/// up (<see cref="Stack"/> above 1: a bin of small loose items, drawn as a pile with a count on look-at).
/// </summary>
public readonly record struct CSlotDef(Vector3 Min, Vector3 Max, PartRole[] Fits, int Stack, string Label)
{
    public Vector3 Size => Max - Min;
}

/// <summary>
/// One storage part's contents (server: the real ones; client: as last heard). Fixed arrays, one per slot: the stored
/// part's type (255: empty), the turn it was set in at, how many (a pile), the top one's health (0..1 of its own); a
/// bin's bulk amount. Pooled in <see cref="ConstructWorld.Stores"/> and found from the part by <see cref="CPart.Store"/>.
/// </summary>
public sealed class CStore
{
    public const int MaxSlots = 12;

    public ushort Handle;
    public bool Live;
    public byte SlotCount; // the container's slots (its def's)
    public readonly ushort[] Def = new ushort[MaxSlots];
    public readonly byte[] Rot = new byte[MaxSlots];
    public readonly byte[] Count = new byte[MaxSlots];
    public readonly float[] Health = new float[MaxSlots];
    public int Amount; // a bin: how much bulk material
    public int Version; // goes up on every change (the look text rebuilds only then)

    public void Reset(int slots)
    {
        SlotCount = (byte)Math.Min(slots, MaxSlots);
        for (var k = 0; k < MaxSlots; k++)
        {
            Def[k] = ConstructParts.None;
            Rot[k] = 0;
            Count[k] = 0;
            Health[k] = 1f;
        }
        Amount = 0;
        Version++;
    }

    /// <summary>Stored parts in all (a pile counts each).</summary>
    public int Items
    {
        get
        {
            var n = 0;
            for (var k = 0; k < SlotCount; k++)
                n += Def[k] == ConstructParts.None ? 0 : Count[k];
            return n;
        }
    }

    public bool Empty => Items == 0 && Amount == 0;
}

/// <summary>
/// Physical storage (decision 18; docs/sandbox/CONSTRUCTS.md "Physical storage"): storage parts are construct parts
/// with slots that parts are set into, drawn where they sit; a bin holds bulk material with a visible fill level. This
/// is the part data, the fitting rules and the slot geometry; the world's side (the pool, setting in, taking out,
/// damage, spilling) is <see cref="ConstructWorld"/>'s, below; the actions are in ConstructEdits; the look and drawing in
/// Program.Sandbox.cs and ConstructRender.
/// </summary>
public static class ConstructStorage
{
    public const float SmallSize = 0.35f, MediumSize = 0.7f; // a slot's size class by its longest side (metres): small, medium, large
    public const int MaxStack = 12;
    public const int MaxBulk = 60000;
    public const string Scrap = "scrap";
    public const string ScrapBundle = "Scrap Bundle"; // what a broken bin's scrap spills as (wrecked: salvage gives its scrap)
    public const int MaxSpillBundles = 6;
    public const int TakeLoad = 100; // what F takes out of a bin at a time

    /// <summary>The bulk materials a bin can hold (resources come later; scrap is the one there is).</summary>
    public static readonly string[] BulkMaterials = [Scrap];

    private const float Fit = 0.005f; // slack when a part's box is checked against a slot's

    /// <summary>"small", "medium" or "large", from a slot's longest side; or the slot's own label (a shape: "wheel").</summary>
    public static string SizeClass(in CSlotDef s)
    {
        if (s.Label.Length > 0)
            return s.Label;
        var size = s.Size;
        var big = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        return big <= SmallSize + Fit ? "small" : big <= MediumSize + Fit ? "medium" : "large";
    }

    // The turns tried when a part goes into a slot: upright ones first (no turn first), then the rest, in a fixed order.
    private static readonly byte[] TurnOrder = BuildTurnOrder();

    private static byte[] BuildTurnOrder()
    {
        var list = new List<byte>();
        for (byte r = 0; r < Rot24.Count; r++)
            if (Rot24.ApplyDir(r, 2) == 2)
                list.Add(r);
        for (byte r = 0; r < Rot24.Count; r++)
            if (Rot24.ApplyDir(r, 2) != 2)
                list.Add(r);
        return list.ToArray();
    }

    /// <summary>A part's solid bounds turned by <paramref name="rot"/> (its own frame).</summary>
    public static void Turned(ConstructPartDef d, byte rot, out Vector3 min, out Vector3 max)
    {
        var a = Rot24.Apply(rot, d.BoundsMin);
        var b = Rot24.Apply(rot, d.BoundsMax);
        min = Vector3.Min(a, b);
        max = Vector3.Max(a, b);
    }

    /// <summary>Does this part go in that slot at all (its role, and its box in some turn; the slot empty or not)? The turn it goes in at.</summary>
    public static bool Fits(in CSlotDef s, ConstructPartDef d, out byte rot)
    {
        rot = 0;
        if (d.Role == PartRole.Storage)
            return false; // (no storage inside storage: its contents would be lost)
        if (s.Fits.Length > 0 && Array.IndexOf(s.Fits, d.Role) < 0)
            return false;
        var room = s.Size + new Vector3(Fit);
        foreach (var r in TurnOrder)
        {
            Turned(d, r, out var mn, out var mx);
            var size = mx - mn;
            if (size.X <= room.X && size.Y <= room.Y && size.Z <= room.Z)
            {
                rot = r;
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether <paramref name="d"/> can go into slot <paramref name="slot"/> of a container of type <paramref name="cd"/> holding <paramref name="st"/> now (and the turn it goes in at). Allocates nothing (the look calls it every frame).</summary>
    public static SlotCheck Check(ConstructPartDef cd, CStore st, int slot, ConstructPartDef d, out byte rot)
    {
        rot = 0;
        if (slot < 0 || slot >= cd.Slots.Length || slot >= st.SlotCount)
            return SlotCheck.NoSlot;
        ref readonly var s = ref cd.Slots[slot];
        if (d.PhysicsOnly)
            return SlotCheck.PhysicsOnly;
        if (cd.StoreMaxMass > 0 && d.Mass > cd.StoreMaxMass)
            return SlotCheck.TooHeavy;
        if (!Fits(s, d, out rot))
            return s.Fits.Length > 0 ? SlotCheck.WrongShape : SlotCheck.TooBig;
        if (st.Def[slot] != ConstructParts.None)
        {
            if (s.Stack <= 1)
                return SlotCheck.Taken;
            if (st.Def[slot] != d.Id)
                return SlotCheck.OtherKind;
            if (st.Count[slot] >= s.Stack)
                return SlotCheck.Full;
            rot = st.Rot[slot];
        }
        return SlotCheck.Ok;
    }

    /// <summary>Why <paramref name="d"/> can't go into that slot (the server's refusal, said to the player), or null.</summary>
    public static string? CanSet(ConstructPartDef cd, CStore st, int slot, ConstructPartDef d, out byte rot)
    {
        var why = Check(cd, st, slot, d, out rot);
        if (why == SlotCheck.Ok)
            return null;
        var s = slot >= 0 && slot < cd.Slots.Length ? cd.Slots[slot] : default;
        return why switch
        {
            SlotCheck.NoSlot => "no such slot",
            SlotCheck.WrongShape => $"that slot takes a {RoleWord(s.Fits[0])}",
            SlotCheck.TooBig => $"the {d.Name} doesn't fit a {SizeClass(s)} slot",
            SlotCheck.Taken => "that slot is taken",
            SlotCheck.PhysicsOnly => ConstructEdits.PhysicsOnlyRefusal(d, $"go in the {cd.Name}"),
            SlotCheck.TooHeavy => $"the {d.Name} is too heavy for the {cd.Name} ({d.Mass:0} kg; it takes {cd.StoreMaxMass:0})",
            SlotCheck.OtherKind => $"that bin holds {ConstructParts.Get(st.Def[slot])?.Name ?? "something"}s",
            _ => $"that bin is full ({s.Stack})",
        };
    }

    public static string RoleWord(PartRole r) => r switch
    {
        PartRole.FuelTank => "fuel tank",
        PartRole.FuelLine => "fuel line",
        PartRole.AmmoBox => "ammo box",
        PartRole.GunPart => "gun part",
        PartRole.FeedChute => "feed chute",
        _ => r.ToString().ToLowerInvariant(),
    };

    /// <summary>How many copies of a pile are drawn stacked up in the slot (as many as its height takes, at least one).</summary>
    public static int DrawnCopies(in CSlotDef s, ConstructPartDef d, byte rot, int count)
    {
        if (count <= 1)
            return count;
        Turned(d, rot, out var mn, out var mx);
        var h = MathF.Max(0.01f, mx.Y - mn.Y);
        return Math.Clamp((int)((s.Max.Y - s.Min.Y + Fit) / h), 1, count);
    }

    /// <summary>Where copy <paramref name="copy"/> of a stored part's origin is, in the container part's frame (centred on the slot, on its floor, piled up).</summary>
    public static Vector3 ItemOrigin(in CSlotDef s, ConstructPartDef d, byte rot, int copy)
    {
        Turned(d, rot, out var mn, out var mx);
        var mid = (s.Min + s.Max) * 0.5f;
        var own = (mn + mx) * 0.5f;
        return new Vector3(mid.X - own.X, s.Min.Y - mn.Y + copy * (mx.Y - mn.Y), mid.Z - own.Z);
    }

    /// <summary>Box <paramref name="b"/> of copy <paramref name="copy"/> of a stored part, in the container's segment frame (axis-aligned there: every turn is a Rot24).</summary>
    public static void ItemBox(in CPart container, in CSlotDef s, ConstructPartDef d, byte rot, int copy, int b, out Vector3 centre, out Vector3 half)
    {
        var o = ItemOrigin(s, d, rot, copy);
        var c = o + Rot24.Apply(rot, d.Boxes[b].Center);
        var h = Rot24.ApplyHalf(rot, d.Boxes[b].Half);
        centre = container.Pos + Rot24.Apply(container.Rot, c);
        half = Rot24.ApplyHalf(container.Rot, h);
    }

    /// <summary>A slot's box in the container's segment frame.</summary>
    public static void SlotBox(in CPart container, in CSlotDef s, out Vector3 centre, out Vector3 half)
    {
        centre = container.Pos + Rot24.Apply(container.Rot, (s.Min + s.Max) * 0.5f);
        half = Rot24.ApplyHalf(container.Rot, s.Size * 0.5f);
    }

    /// <summary>A bin's fill box in its segment frame (from the bottom of its fill region up to its level); false when empty.</summary>
    public static bool FillBox(in CPart container, ConstructPartDef cd, int amount, out Vector3 centre, out Vector3 half)
    {
        centre = half = Vector3.Zero;
        if (!cd.IsBin || amount <= 0)
            return false;
        var share = Math.Clamp(amount / (float)cd.BulkCapacity, 0.02f, 1f);
        var max = cd.FillMax with { Y = cd.FillMin.Y + (cd.FillMax.Y - cd.FillMin.Y) * share };
        centre = container.Pos + Rot24.Apply(container.Rot, (cd.FillMin + max) * 0.5f);
        half = Rot24.ApplyHalf(container.Rot, (max - cd.FillMin) * 0.5f);
        return true;
    }

    // ---- The part data ---------------------------------------------------------------------------------------------

    /// <summary>A storage part's <c>"storage"</c> block: <c>"slots"</c> (each a box, <c>"fits"</c>, <c>"stack"</c>, <c>"label"</c>) and/or <c>"bulk"</c>.</summary>
    internal static void ParseStorage(ConstructPartDef d, JsonElement b, string file)
    {
        var bw = $"{file}, storage";
        ConstructParts.Known(b, ["slots", "bulk", "maxMass"], bw);
        d.StoreMaxMass = ConstructParts.Opt(b, "maxMass") is { } mm ? ConstructParts.Num(mm, file, "storage.maxMass") : 0f;
        ConstructParts.Range(d.StoreMaxMass, 0, 100000, file, "storage.maxMass");
        var slots = new List<CSlotDef>();
        if (ConstructParts.Opt(b, "slots") is { } arr)
        {
            if (arr.ValueKind != JsonValueKind.Array)
                throw new ConstructPartException($"{file}: storage \"slots\" is a list");
            var si = 0;
            foreach (var s in arr.EnumerateArray())
            {
                var where = $"storage.slots[{si++}]";
                ConstructParts.Known(s, ["min", "max", "at", "size", "fits", "stack", "label"], $"{file}, {where}");
                Vector3 min, max;
                if (ConstructParts.Opt(s, "min") is { } mn && ConstructParts.Opt(s, "max") is { } mx)
                {
                    min = ConstructParts.Vec(mn, file, where + ".min");
                    max = ConstructParts.Vec(mx, file, where + ".max");
                }
                else if (ConstructParts.Opt(s, "size") is { } sz)
                {
                    var size = ConstructParts.Vec(sz, file, where + ".size");
                    var at = ConstructParts.Opt(s, "at") is { } a ? ConstructParts.Vec(a, file, where + ".at") : Vector3.Zero;
                    min = at - size / 2;
                    max = at + size / 2;
                }
                else
                    throw new ConstructPartException($"{file}: {where} needs \"min\" and \"max\", or \"size\" (and \"at\")");
                if (max.X - min.X < 0.05f || max.Y - min.Y < 0.05f || max.Z - min.Z < 0.05f)
                    throw new ConstructPartException($"{file}: {where} is smaller than 5 cm a side (or inside out)");
                var fits = new List<PartRole>();
                if (ConstructParts.Opt(s, "fits") is { } f)
                {
                    if (f.ValueKind != JsonValueKind.Array)
                        throw new ConstructPartException($"{file}: {where}.fits lists roles (\"wheel\", \"engine\"...)");
                    foreach (var r in f.EnumerateArray())
                    {
                        var name = ConstructParts.Str(r, file, where + ".fits");
                        if (!Enum.TryParse<PartRole>(name, true, out var role) || !Enum.IsDefined(role) || int.TryParse(name, out _))
                            throw new ConstructPartException($"{file}: {where}.fits: \"{name}\" isn't a part role");
                        fits.Add(role);
                    }
                }
                var stack = ConstructParts.Opt(s, "stack") is { } st ? (int)ConstructParts.Num(st, file, where + ".stack") : 1;
                ConstructParts.Range(stack, 1, MaxStack, file, where + ".stack");
                var label = ConstructParts.Opt(s, "label") is { } lb ? ConstructParts.Str(lb, file, where + ".label") : "";
                if (label.Length > 16 || label.Any(c => c < 32 || c > 126))
                    throw new ConstructPartException($"{file}: {where}.label is at most 16 plain ASCII characters");
                slots.Add(new CSlotDef(min, max, fits.ToArray(), stack, label));
            }
            if (slots.Count > CStore.MaxSlots)
                throw new ConstructPartException($"{file}: at most {CStore.MaxSlots} slots a storage part");
        }
        if (ConstructParts.Opt(b, "bulk") is { } bulk)
        {
            if (bulk.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{file}: storage \"bulk\" is an object (material, capacity, min, max)");
            ConstructParts.Known(bulk, ["material", "capacity", "min", "max"], $"{bw}.bulk");
            d.BulkMaterial = ConstructParts.Str(ConstructParts.Need(bulk, "material", $"{bw}.bulk"), file, "storage.bulk.material");
            if (Array.IndexOf(BulkMaterials, d.BulkMaterial) < 0)
                throw new ConstructPartException($"{file}: storage.bulk.material \"{d.BulkMaterial}\" isn't a bulk material ({string.Join(", ", BulkMaterials)})");
            var cap = ConstructParts.Num(ConstructParts.Need(bulk, "capacity", $"{bw}.bulk"), file, "storage.bulk.capacity");
            ConstructParts.Range(cap, 1, MaxBulk, file, "storage.bulk.capacity");
            d.BulkCapacity = (int)cap;
            d.FillMin = ConstructParts.Vec(ConstructParts.Need(bulk, "min", $"{bw}.bulk"), file, "storage.bulk.min");
            d.FillMax = ConstructParts.Vec(ConstructParts.Need(bulk, "max", $"{bw}.bulk"), file, "storage.bulk.max");
            if (d.FillMax.X - d.FillMin.X < 0.05f || d.FillMax.Y - d.FillMin.Y < 0.05f || d.FillMax.Z - d.FillMin.Z < 0.05f)
                throw new ConstructPartException($"{file}: storage.bulk's fill box is smaller than 5 cm a side (or inside out)");
            if (Outside(d, d.FillMin, d.FillMax))
                throw new ConstructPartException($"{file}: storage.bulk's fill box is outside the part");
        }
        if (slots.Count == 0 && d.BulkCapacity == 0)
            throw new ConstructPartException($"{file}: a storage part needs \"slots\" or \"bulk\"");
        if (slots.Count > 0 && d.BulkCapacity > 0)
            throw new ConstructPartException($"{file}: a storage part has \"slots\" or \"bulk\", not both");
        for (var k = 0; k < slots.Count; k++)
        {
            var s = slots[k];
            if (Outside(d, s.Min, s.Max))
                throw new ConstructPartException($"{file}: storage.slots[{k}] is outside the part");
            for (var b2 = 0; b2 < d.SolidBoxes; b2++)
                if (Overlap(s.Min, s.Max, d.Boxes[b2].Center - d.Boxes[b2].Half, d.Boxes[b2].Center + d.Boxes[b2].Half))
                    throw new ConstructPartException($"{file}: storage.slots[{k}] runs into the part's box {b2} (a slot is the room a stored part takes)");
            for (var o = 0; o < k; o++)
                if (Overlap(s.Min, s.Max, slots[o].Min, slots[o].Max))
                    throw new ConstructPartException($"{file}: storage.slots[{k}] overlaps slots[{o}]");
        }
        d.Slots = slots.ToArray();
    }

    private static bool Outside(ConstructPartDef d, Vector3 min, Vector3 max) =>
        min.X < d.BoundsMin.X - 0.01f || min.Y < d.BoundsMin.Y - 0.01f || min.Z < d.BoundsMin.Z - 0.01f
        || max.X > d.BoundsMax.X + 0.01f || max.Y > d.BoundsMax.Y + 0.01f || max.Z > d.BoundsMax.Z + 0.01f;

    private static bool Overlap(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax) =>
        aMin.X < bMax.X - 0.01f && bMin.X < aMax.X - 0.01f && aMin.Y < bMax.Y - 0.01f && bMin.Y < aMax.Y - 0.01f && aMin.Z < bMax.Z - 0.01f && bMin.Z < aMax.Z - 0.01f;

    // ---- The state as bytes (the wire, and a world save when there is one) ----------------------------------------

    /// <summary>A store's contents (compact: four bytes a slot, or the bin's amount): the network's format and the one a world save will use.</summary>
    public static void Write(PacketWriter w, CStore s)
    {
        w.U8(s.SlotCount);
        for (var k = 0; k < s.SlotCount; k++)
            w.U16(s.Def[k]).U8(s.Rot[k]).U8(s.Count[k]).U8((byte)Math.Clamp((int)MathF.Round(s.Health[k] * 255), 1, 255));
        w.U16((ushort)Math.Clamp(s.Amount, 0, MaxBulk));
    }

    /// <summary>Reads <see cref="Write"/>'s bytes into <paramref name="s"/> (bad values refused, never trusted).</summary>
    public static void Read(ref PacketReader r, CStore s)
    {
        int n = r.U8();
        if (n > CStore.MaxSlots)
            throw new MalformedPacketException($"a store of {n} slots");
        s.Reset(n);
        for (var k = 0; k < n; k++)
        {
            var def = r.U16();
            var rot = r.U8();
            var count = r.U8();
            var hp = r.U8();
            if (def != ConstructParts.None && (ConstructParts.Get(def) == null || rot >= Rot24.Count || count == 0 || count > MaxStack))
                throw new MalformedPacketException($"store slot {k}: part {def}, turn {rot}, {count} of them");
            s.Def[k] = def;
            s.Rot[k] = def == ConstructParts.None ? (byte)0 : rot;
            s.Count[k] = def == ConstructParts.None ? (byte)0 : count;
            s.Health[k] = hp / 255f;
        }
        s.Amount = Math.Min((int)r.U16(), MaxBulk);
        s.Live = true;
        s.Version++;
    }
}

/// <summary>The world's side of physical storage (ConstructStorage.cs): the store pool and every change to what is stored.</summary>
public sealed partial class ConstructWorld
{
    public const int MaxStores = 128;

    /// <summary>Every container's contents, by handle (<see cref="CPart.Store"/>; 0 is none). Server: given out as storage parts are added; client: as heard.</summary>
    public readonly CStore[] Stores = MakeStores();

    /// <summary>Server: the stores that changed (sent to clients after the structure).</summary>
    public readonly HashSet<ushort> StoreDirty = new(MaxStores);

    private static CStore[] MakeStores()
    {
        var a = new CStore[MaxStores + 1];
        for (var k = 0; k < a.Length; k++)
            a[k] = new CStore { Handle = (ushort)k };
        return a;
    }

    /// <summary>The store of a storage part, or null (not a storage part, or the pool was full when it was added).</summary>
    public CStore? StoreOf(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount)
            return null;
        var h = c.Parts[part].Store;
        return h != 0 && h <= MaxStores && Stores[h].Live ? Stores[h] : null;
    }

    /// <summary>Stores in use (the self-test).</summary>
    public int LiveStores
    {
        get
        {
            var n = 0;
            for (var k = 1; k <= MaxStores; k++)
                n += Stores[k].Live ? 1 : 0;
            return n;
        }
    }

    private void AllocStore(ref CPart p, ConstructPartDef d)
    {
        p.Store = 0;
        if (d.Role != PartRole.Storage)
            return;
        for (ushort k = 1; k <= MaxStores; k++)
            if (!Stores[k].Live)
            {
                Stores[k].Reset(d.Slots.Length);
                Stores[k].Live = true;
                p.Store = k;
                StoreDirty.Add(k);
                return;
            }
        Events.Add($"the store pool is full: a {d.Name} holds nothing");
    }

    private void FreeStore(ref CPart p)
    {
        if (p.Store != 0 && p.Store <= MaxStores)
        {
            Stores[p.Store].Live = false;
            Stores[p.Store].Reset(0);
            StoreDirty.Remove(p.Store);
        }
        p.Store = 0;
    }

    private void ClearStores()
    {
        for (var k = 1; k <= MaxStores; k++)
        {
            Stores[k].Live = false;
            Stores[k].Reset(0);
        }
        StoreDirty.Clear();
    }

    private void Changed(CStore s)
    {
        s.Version++;
        StoreDirty.Add(s.Handle);
    }

    /// <summary>Server: sets a part into a slot (the caller checked reach and the hands). Null, or why not.</summary>
    public string? SetIn(Construct c, int part, int slot, ConstructPartDef d)
    {
        if (StoreOf(c, part) is not { } st)
            return "that isn't storage";
        var cd = c.DefOf(part);
        if (ConstructStorage.CanSet(cd, st, slot, d, out var rot) is { } why)
            return why;
        if (st.Def[slot] == ConstructParts.None)
        {
            st.Def[slot] = d.Id;
            st.Rot[slot] = rot;
            st.Count[slot] = 1;
            st.Health[slot] = 1f;
        }
        else
            st.Count[slot]++;
        Changed(st);
        c.Wake();
        return null;
    }

    /// <summary>Server: takes the top stored part out of a slot (null when the slot is empty: someone else took it first).</summary>
    public ConstructPartDef? TakeOut(Construct c, int part, int slot)
    {
        if (StoreOf(c, part) is not { } st || slot < 0 || slot >= st.SlotCount || ConstructParts.Get(st.Def[slot]) is not { } d)
            return null;
        if (--st.Count[slot] == 0)
        {
            st.Def[slot] = ConstructParts.None;
            st.Rot[slot] = 0;
        }
        st.Health[slot] = 1f;
        Changed(st);
        c.Wake();
        return d;
    }

    /// <summary>Server: bulk into a bin; returns how much went in (up to its room).</summary>
    public int Deposit(Construct c, int part, int amount)
    {
        if (StoreOf(c, part) is not { } st || !c.DefOf(part).IsBin || amount <= 0)
            return 0;
        var n = Math.Min(amount, c.DefOf(part).BulkCapacity - st.Amount);
        if (n <= 0)
            return 0;
        st.Amount += n;
        Changed(st);
        return n;
    }

    /// <summary>Server: bulk out of a bin; returns how much came out.</summary>
    public int Withdraw(Construct c, int part, int amount)
    {
        if (StoreOf(c, part) is not { } st || amount <= 0)
            return 0;
        var n = Math.Min(amount, st.Amount);
        if (n <= 0)
            return 0;
        st.Amount -= n;
        Changed(st);
        return n;
    }

    /// <summary>A hit on a stored part (shot through, blasted): its armour first, then its health; at zero the top one is destroyed. True if one was.</summary>
    public bool DamageStored(Construct c, int part, int slot, float damage)
    {
        if (StoreOf(c, part) is not { } st || slot < 0 || slot >= st.SlotCount || ConstructParts.Get(st.Def[slot]) is not { } d)
            return false;
        var dealt = damage - d.Armour;
        if (dealt <= 0)
            return false;
        st.Health[slot] -= dealt / MathF.Max(1, d.Health);
        Changed(st);
        if (st.Health[slot] > 0)
            return false;
        Events.Add($"construct {c.Id}: a stored {d.Name} was destroyed");
        if (--st.Count[slot] == 0)
        {
            st.Def[slot] = ConstructParts.None;
            st.Rot[slot] = 0;
        }
        st.Health[slot] = 1f;
        return true;
    }

    /// <summary>A blast on a container: everything stored in it takes the container's share.</summary>
    private void BlastStored(Construct c, int part, float damage)
    {
        if (StoreOf(c, part) is not { } st)
            return;
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] != ConstructParts.None)
                DamageStored(c, part, k, damage);
    }

    /// <summary>
    /// Server: a container is going (broken off, taken off, wrecked): what it holds spills out where it was, as loose
    /// parts anyone may pick up (a pile as many as there were, while the caps allow); a bin's scrap as wrecked scrap
    /// bundles to salvage (at most <see cref="ConstructStorage.MaxSpillBundles"/>; the rest is lost). The store is freed.
    /// </summary>
    private void Spill(Construct c, int part)
    {
        if (StoreOf(c, part) is not { } st)
        {
            FreeStore(ref c.Parts[part]);
            return;
        }
        ref var cp = ref c.Parts[part];
        var cd = cp.D;
        c.WorldFrame(cp.Seg, out var q, out var t);
        var spilled = 0;
        var lost = 0;
        for (var k = 0; k < st.SlotCount; k++)
        {
            if (ConstructParts.Get(st.Def[k]) is not { } d)
                continue;
            for (var n = 0; n < st.Count[k]; n++)
            {
                var copy = ConstructStorage.DrawnCopies(cd.Slots[k], d, st.Rot[k], st.Count[k]) > n ? n : 0;
                var o = ConstructStorage.ItemOrigin(cd.Slots[k], d, st.Rot[k], copy) + new Vector3(0, copy == n ? 0 : 0.3f * (n + 1), 0);
                var at = t + Vector3.Transform(cp.Pos + Rot24.Apply(cp.Rot, o), q);
                if (Spawn(d, at, c.PointVel(at) + SpillKick(k, n)) is { } loose)
                {
                    loose.Owner = 255; // (spilled: anyone's to pick up)
                    spilled++;
                }
                else
                    lost++;
            }
        }
        if (st.Amount > 0 && ConstructParts.Find(ConstructStorage.ScrapBundle) is { } bundle)
        {
            var each = Math.Max(1, bundle.Scrap);
            var n = Math.Clamp((st.Amount + each - 1) / each, 1, ConstructStorage.MaxSpillBundles);
            var mid = (cd.FillMin + cd.FillMax) * 0.5f;
            for (var k = 0; k < n; k++)
            {
                var at = t + Vector3.Transform(cp.Pos + Rot24.Apply(cp.Rot, mid + new Vector3((k % 3 - 1) * 0.25f, 0.3f * (k / 3), 0)), q);
                if (Spawn(bundle, at, c.PointVel(at) + SpillKick(k, 0)) is { } heap)
                {
                    heap.Parts[0].Broken = true; // (wrecked: salvage gives its scrap; it goes as debris does)
                    heap.Parts[0].Health = 0;
                    heap.Wreck = true;
                    heap.Damaged = true;
                    heap.Owner = 255;
                    heap.Rebuild();
                    spilled++;
                }
            }
        }
        if (spilled + lost > 0)
            Events.Add($"construct {c.Id}: a {cd.Name} spilled {spilled} thing(s){(lost > 0 ? $" ({lost} lost: the world is full)" : "")}");
        FreeStore(ref cp);
    }

    private static Vector3 SpillKick(int k, int n) => new(((k * 7 + n * 3) % 5 - 2) * 0.35f, 1.6f, ((k * 3 + n * 5) % 5 - 2) * 0.35f);

    private Construct? Spawn(ConstructPartDef d, Vector3 at, Vector3 vel)
    {
        if (Bodies.Count >= MaxBodies || TotalParts >= MaxTotalParts)
            return null;
        if (Create(at + new Vector3(0, 0.05f, 0), Quaternion.Identity) is not { } c)
            return null;
        if (AddPart(c, d, Vector3.Zero, 0) < 0)
        {
            Remove(c);
            return null;
        }
        c.Rebuild();
        c.Vel = vel;
        return c;
    }

    /// <summary>The nearest stored part (or bin's fill) of container <paramref name="i"/> along a ray in its segment frame (before <paramref name="best"/>); the slot (or -1 for a fill), else -2.</summary>
    private int RayStored(Construct c, int i, Vector3 lo, Vector3 ld, ref float best)
    {
        if (StoreOf(c, i) is not { } st)
            return -2;
        ref var p = ref c.Parts[i];
        var cd = p.D;
        var found = -2;
        for (var k = 0; k < st.SlotCount; k++)
        {
            if (ConstructParts.Get(st.Def[k]) is not { } d)
                continue;
            var copies = ConstructStorage.DrawnCopies(cd.Slots[k], d, st.Rot[k], st.Count[k]);
            for (var n = 0; n < copies; n++)
                for (var b = 0; b < d.SolidBoxes; b++)
                {
                    ConstructStorage.ItemBox(p, cd.Slots[k], d, st.Rot[k], n, b, out var centre, out var half);
                    if (Geometry.RayAabb(centre - half, centre + half, lo, ld, best) is { } dist && dist < best)
                    {
                        best = dist;
                        found = k;
                    }
                }
        }
        if (ConstructStorage.FillBox(p, cd, st.Amount, out var fc, out var fh) && Geometry.RayAabb(fc - fh, fc + fh, lo, ld, best) is { } fd && fd < best)
        {
            best = fd;
            found = -1;
        }
        return found;
    }
}
