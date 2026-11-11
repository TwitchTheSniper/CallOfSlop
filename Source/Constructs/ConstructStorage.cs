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
    HoldsBulk, // a Storage Volume holding a bulk material: bulk or parts, not both
    TooManyKinds, // a Storage Volume at CStore.MaxKinds
    OutputOnly, // an Assembler's tray: what it made sits there, nothing is set in (ConstructAssembling.cs)
}

/// <summary>
/// One slot of a storage part (decision 18, physical storage): the box in the part's frame a stored part sits in (on its
/// floor, centred), the roles that fit it (none listed: any part that fits the box), and how many of one kind it piles
/// up (<see cref="Stack"/> above 1: a bin of small loose items, drawn as a pile with a count on look-at). A
/// <see cref="Rack"/> slot (a shelf's) takes as many as really fit: its copies stand in rows across the floor and in layers
/// up it (<see cref="ConstructStorage.StackOf"/>).
/// </summary>
public readonly record struct CSlotDef(Vector3 Min, Vector3 Max, PartRole[] Fits, int Stack, string Label, bool Rack = false)
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
    public const int MaxKinds = 256, MaxPerKind = 60000; // a Storage Volume's parts: kinds, and how many of one (each a u16 on the wire)
    public const int MaxMix = 8; // a mixed input's materials (a Foundry's or an Assembler's: ConstructRefining.cs, ConstructAssembling.cs)

    public ushort Handle;
    public bool Live;
    public byte SlotCount; // the container's slots (its def's)
    public readonly ushort[] Def = new ushort[MaxSlots];
    public readonly byte[] Rot = new byte[MaxSlots];
    public readonly byte[] Count = new byte[MaxSlots];
    public readonly float[] Health = new float[MaxSlots];
    public int Amount; // a bin: how much bulk material
    public byte Material; // a bin: which one (RecyclingTable id; 0: none yet, the first deposit sets it)
    public bool Busy; // a Recycler or gathering module: working (on the wire; the look text and the lamp)
    public LineState LineRaw, LineShown; // a production machine's line state as this peer last worked it out, and as its lamp shows it (never sent: ProductionLines.Of)
    public double LineSince; // ...when the raw state last changed (ConstructRender.Now)
    public bool LineSeen; // ...worked out at least once
    public bool Held; // a Recycler or gathering module: its Run wire is off (on the wire; the look text)
    public ShaftState Drive; // a drill with a drive shaft linked: how the shaft stands (on the wire; the look text); None: it runs on electric power
    public ushort DriveRpm; // ...the shaft's speed at the drill (rounded a little, on the wire)
    public byte DrivePct; // ...and the share of its rate that gives, 0 to 100 (on the wire)
    public int InAmount; // a production machine's input bin (ConstructRefining.cs): how much; Amount and Material are its output
    public byte InMaterial; // ...of which (0: none yet); it picks the recipe
    public byte Refused; // a production machine: the material a chute last brought that it can't take (0: none; on the wire: the look text)
    public byte RefusedAge; // ...steps since (server only: it clears a few seconds after the chute stops trying)
    public ushort RefusedPart = ConstructParts.None; // an Assembler: the part a chute last brought that it can't take (on the wire: the look text)
    public byte Blocked; // an Assembler: why a finished batch can't leave (AsmBlock; on the wire: the look text)
    public byte[]? MixMat; // a mixed input's materials (made the first time one goes in, kept with the pooled store)
    public int[]? MixAmount;
    public int MixN; // materials held
    public int JobTicks, JobAmount; // a Recycler (server only): ticks left on the part it took, and the units it will give; a drill's JobAmount: units towards its next scoop; a production machine's JobTicks: ticks of work on this batch
    public byte JobMaterial;
    public int JobFull; // a Recycler (server only): the ticks its job took at the start
    public byte Progress; // an Assembler's or a Recycler's job, 0 to 100 (on the wire about once a second: its screen, MachineScreens.cs)
    public byte ProgressAge; // ...steps since it was last sent (server only)
    public int BeaconStores, BeaconParts; // an Inventory Beacon: its chute network's stores and parts (on the wire: the look line; Empty ignores them)
    public ushort PullId; // a Smart Terminal: what it is bringing now, a part or material id (on the wire: its panel; a pull is never saved)
    public bool PullMaterial; // ...the id is a material
    public byte PullWaiting, PullSeconds; // ...pulls under way here, the one coming first among them, and whole seconds until it lands
    /// <summary>How full it is, 0 to 1 (a Level Sensor): its bulk, or the share of its slots in use.</summary>
    public float Fullness(ConstructPartDef d)
    {
        if (d.BulkCapacity > 0)
            return Math.Clamp(Amount / (float)d.BulkCapacity, 0, 1);
        var used = 0;
        for (var k = 0; k < SlotCount; k++)
            used += Def[k] != ConstructParts.None ? 1 : 0;
        return SlotCount > 0 ? used / (float)SlotCount : 0;
    }

    public int Version; // goes up on every change (the look text rebuilds only then)
    public ushort[]? KindDef; // a Storage Volume's parts by kind, last put in last (made the first time one goes in, kept with the pooled store)
    public int[]? KindCount;
    public int KindN; // kinds held
    public int ItemCount; // parts held, all kinds
    public float ItemSpace, ItemKg; // what they take of the box (m3) and weigh: added up again on every change, so no error creeps in
    public RackSlab[]? Slabs; // a Piece Rack: its columns of pieces along its length, in place order (ConstructRackPack.cs; made the first time one goes in)
    public byte[]? SlabLanes; // ...each column's stacks: how many high, MaxLanes a column
    public int SlabN, SlabSum; // ...columns, and the pieces in them (not ItemCount: the places are worked out again)

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
        Material = 0;
        Busy = false;
        Held = false;
        Drive = ShaftState.None;
        DriveRpm = 0;
        DrivePct = 0;
        InAmount = 0;
        InMaterial = Refused = RefusedAge = 0;
        RefusedPart = ConstructParts.None;
        Blocked = 0;
        MixN = 0;
        JobTicks = JobAmount = 0;
        JobMaterial = 0;
        JobFull = 0;
        Progress = ProgressAge = 0;
        BeaconStores = BeaconParts = 0;
        PullId = 0;
        PullMaterial = false;
        PullWaiting = PullSeconds = 0;
        KindN = ItemCount = 0;
        SlabN = SlabSum = 0;
        ItemSpace = ItemKg = 0;
        Version++;
    }

    /// <summary>Where a part kind sits in <see cref="KindDef"/>, or -1.</summary>
    public int KindIndex(ushort def)
    {
        for (var k = 0; k < KindN; k++)
            if (KindDef![k] == def)
                return k;
        return -1;
    }

    /// <summary>Adds (or takes, when negative) parts of one kind; a kind that reaches zero goes. The caller checked the limits.</summary>
    public void AddKind(ushort def, int n)
    {
        KindDef ??= new ushort[MaxKinds];
        KindCount ??= new int[MaxKinds];
        var k = KindIndex(def);
        if (k < 0)
        {
            if (n <= 0 || KindN >= MaxKinds)
                return;
            k = KindN++;
            KindDef[k] = def;
            KindCount[k] = 0;
        }
        KindCount[k] += n;
        if (KindCount[k] <= 0)
        {
            KindN--;
            Array.Copy(KindDef, k + 1, KindDef, k, KindN - k);
            Array.Copy(KindCount, k + 1, KindCount, k, KindN - k);
        }
        Recount();
    }

    /// <summary>The totals from the kinds.</summary>
    public void Recount()
    {
        ItemCount = 0;
        ItemSpace = ItemKg = 0;
        for (var k = 0; k < KindN; k++)
        {
            ItemCount += KindCount![k];
            if (ConstructParts.Get(KindDef![k]) is { } d)
            {
                ItemSpace += ConstructStoreVolume.SpaceOf(d) * KindCount[k];
                ItemKg += d.Mass * KindCount[k];
            }
        }
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

    public bool Empty => Items == 0 && Amount == 0 && InAmount == 0 && KindN == 0 && MixN == 0;

    /// <summary>How much of a material a mixed input holds.</summary>
    public int MixOf(byte material)
    {
        for (var k = 0; k < MixN; k++)
            if (MixMat![k] == material)
                return MixAmount![k];
        return 0;
    }

    /// <summary>Adds (or takes, when negative) a material of a mixed input; one that reaches zero goes. The caller checked the limits.</summary>
    public void AddMix(byte material, int n)
    {
        MixMat ??= new byte[MaxMix];
        MixAmount ??= new int[MaxMix];
        var k = 0;
        while (k < MixN && MixMat[k] != material)
            k++;
        if (k == MixN)
        {
            if (n <= 0 || MixN >= MaxMix)
                return;
            MixMat[MixN] = material;
            MixAmount[MixN++] = 0;
        }
        MixAmount[k] += n;
        if (MixAmount[k] > 0)
            return;
        MixN--;
        Array.Copy(MixMat, k + 1, MixMat, k, MixN - k);
        Array.Copy(MixAmount, k + 1, MixAmount, k, MixN - k);
    }

    /// <summary>What a machine's input holds in all: a single input, a mixed one, and an Assembler's parts (the fill's level).</summary>
    public int InTotal
    {
        get
        {
            var n = InAmount + ItemCount;
            for (var k = 0; k < MixN; k++)
                n += MixAmount![k];
            return n;
        }
    }
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
    public const int MaxRack = 48; // a shelf's slot: at most this many of a kind, however small the part (one byte each on the wire; 48 keeps a full bay's draw calls bounded)
    public const int MaxBulk = 60000;
    public const string Any = "any"; // a bin that takes any one material (the first deposit sets it)
    public const string MaterialBundle = "Material Bundle"; // what a broken bin's contents spill as (a wrecked part: its Setting is the material id)
    public const int MaxSpillBundles = 6;
    public const int TakeLoad = 100; // what F takes out of a bin at a time

    /// <summary>Does a bin of this part (its "material") take <paramref name="material"/>?</summary>
    public static bool BinTakes(ConstructPartDef d, byte material) => d.BulkMaterial == Any || RecyclingTable.Current.IdOf(d.BulkMaterial) == material;

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
        var best = 0;
        foreach (var r in TurnOrder)
        {
            Turned(d, r, out var mn, out var mx);
            var size = mx - mn;
            if (size.X > room.X || size.Y > room.Y || size.Z > room.Z)
                continue;
            if (!s.Rack)
            {
                rot = r;
                return true;
            }
            var n = StackOf(s, d, r); // (a shelf: the turn that packs most; the upright ones win a tie)
            if (n > best)
            {
                best = n;
                rot = r;
            }
        }
        return best > 0;
    }

    /// <summary>A shelf slot's rows across, layers up and rows deep of a part in this turn (each at least one).</summary>
    public static void PileShape(in CSlotDef s, ConstructPartDef d, byte rot, out int across, out int up, out int deep)
    {
        Turned(d, rot, out var mn, out var mx);
        var size = Vector3.Max(mx - mn, new Vector3(0.01f));
        var room = s.Size + new Vector3(Fit);
        across = Math.Max(1, (int)(room.X / size.X));
        up = Math.Max(1, (int)(room.Y / size.Y));
        deep = Math.Max(1, (int)(room.Z / size.Z));
    }

    /// <summary>How many of a part a slot piles up: its <c>stack</c>, or on a shelf as many as fit it in this turn.</summary>
    public static int StackOf(in CSlotDef s, ConstructPartDef d, byte rot)
    {
        if (!s.Rack)
            return s.Stack;
        PileShape(s, d, rot, out var across, out var up, out var deep);
        return Math.Min(across * up * deep, MaxRack);
    }

    /// <summary>Whether <paramref name="d"/> can go into slot <paramref name="slot"/> of a container of type <paramref name="cd"/> holding <paramref name="st"/> now (and the turn it goes in at). Allocates nothing (the look calls it every frame).</summary>
    public static SlotCheck Check(ConstructPartDef cd, CStore st, int slot, ConstructPartDef d, out byte rot)
    {
        rot = 0;
        if (slot < 0 || slot >= cd.Slots.Length || slot >= st.SlotCount)
            return SlotCheck.NoSlot;
        ref readonly var s = ref cd.Slots[slot];
        if (cd.IsAssembler || cd.IsTerminal || cd.IsPress)
            return SlotCheck.OutputOnly;
        if (d.PhysicsOnly)
            return SlotCheck.PhysicsOnly;
        if (cd.StoreMaxMass > 0 && d.Mass > cd.StoreMaxMass)
            return SlotCheck.TooHeavy;
        if (!Fits(s, d, out rot))
            return s.Fits.Length > 0 ? SlotCheck.WrongShape : SlotCheck.TooBig;
        if (st.Def[slot] != ConstructParts.None)
        {
            if (!s.Rack && s.Stack <= 1)
                return SlotCheck.Taken;
            if (st.Def[slot] != d.Id)
                return SlotCheck.OtherKind;
            rot = st.Rot[slot];
            if (st.Count[slot] >= StackOf(s, d, rot))
                return SlotCheck.Full;
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
            SlotCheck.OtherKind => $"that {(s.Rack ? "shelf" : "bin")} holds {ConstructParts.Get(st.Def[slot])?.Name ?? "something"}s",
            SlotCheck.OutputOnly => cd.IsTerminal ? $"that is the {cd.Name}'s output tray: it fills from the network" : $"that is the {cd.Name}'s output tray: F or a chute feeds its input",
            _ => $"that {(s.Rack ? "shelf" : "bin")} is full ({StackOf(s, d, st.Rot[slot])})",
        };
    }

    public static string RoleWord(PartRole r) => r switch
    {
        PartRole.FuelTank => "fuel tank",
        PartRole.FluidTank => "fluid tank",
        PartRole.FuelLine => "fuel line",
        PartRole.AmmoBox => "ammo box",
        PartRole.GunPart => "gun part",
        PartRole.FeedChute => "feed chute",
        _ => r.ToString().ToLowerInvariant(),
    };

    /// <summary>How many copies of a pile are drawn stacked up in the slot (as many as its height takes, at least one).</summary>
    public static int DrawnCopies(in CSlotDef s, ConstructPartDef d, byte rot, int count)
    {
        if (count <= 1 || s.Rack)
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
        if (s.Rack)
        {
            // Rows across the floor, the back row first so the front stays open, then layer on layer.
            PileShape(s, d, rot, out var across, out _, out var deep);
            var floor = across * deep;
            var at = copy % floor;
            var size = s.Size;
            return new Vector3(s.Min.X + (at % across + 0.5f) * size.X / across - own.X, s.Min.Y - mn.Y + copy / floor * (mx.Y - mn.Y), s.Min.Z + (at / across + 0.5f) * size.Z / deep - own.Z);
        }
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

    /// <summary>The box a shelf's pile fills, in the container's segment frame: what a ray meets, and the far drawing, so neither walks every copy.</summary>
    public static void PileBox(in CPart container, in CSlotDef s, ConstructPartDef d, byte rot, int count, out Vector3 centre, out Vector3 half)
    {
        PileShape(s, d, rot, out var across, out var up, out var deep);
        var floor = across * deep;
        var size = s.Size;
        var layers = Math.Min((count + floor - 1) / floor, up);
        var rows = layers > 1 ? deep : Math.Min((count + across - 1) / across, deep);
        var wide = rows > 1 ? across : Math.Min(count, across);
        var max = new Vector3(s.Min.X + size.X * wide / across, s.Min.Y + size.Y * layers / up, s.Min.Z + size.Z * rows / deep);
        centre = container.Pos + Rot24.Apply(container.Rot, (s.Min + max) * 0.5f);
        half = Rot24.ApplyHalf(container.Rot, (max - s.Min) * 0.5f);
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
        return cd.IsBin && !cd.IsVolumeStore && Level(container, cd.FillMin, cd.FillMax, amount, cd.BulkCapacity, out centre, out half);
    }

    /// <summary>A bin's fill on its construct: as <see cref="FillBox(in CPart, ConstructPartDef, int, out Vector3, out Vector3)"/>, and a Storage Volume's from its box.</summary>
    public static bool FillBox(Construct c, int part, CStore st, out Vector3 centre, out Vector3 half)
    {
        var d = c.Parts[part].D;
        if (d.IsVolumeStore)
            return ConstructStoreVolume.Fill(c, part, st, out centre, out half);
        return FillBox(c.Parts[part], d, st.Amount, out centre, out half);
    }

    /// <summary>What a bin of this part takes in all: its file's capacity, or a Storage Volume's from its box.</summary>
    public static int Capacity(Construct c, int part) => c.DefOf(part).IsVolumeStore ? ConstructStoreVolume.Capacity(c, part) : c.DefOf(part).BulkCapacity;

    /// <summary>A production machine's input fill, as <see cref="FillBox"/>; false when empty.</summary>
    public static bool InputBox(in CPart container, ConstructPartDef cd, int amount, out Vector3 centre, out Vector3 half)
    {
        centre = half = Vector3.Zero;
        return (cd.IsRefiner || cd.IsAssembler) && Level(container, cd.InFillMin, cd.InFillMax, amount, cd.RefineMix ? cd.InCapacity * 2 : cd.InCapacity, out centre, out half);
    }

    private static bool Level(in CPart container, Vector3 min, Vector3 full, int amount, int capacity, out Vector3 centre, out Vector3 half)
    {
        centre = half = Vector3.Zero;
        if (amount <= 0)
            return false;
        var share = Math.Clamp(amount / (float)capacity, 0.02f, 1f);
        var max = full with { Y = min.Y + (full.Y - min.Y) * share };
        centre = container.Pos + Rot24.Apply(container.Rot, (min + max) * 0.5f);
        half = Rot24.ApplyHalf(container.Rot, (max - min) * 0.5f);
        return true;
    }

    // ---- The part data ---------------------------------------------------------------------------------------------

    /// <summary>A storage part's <c>"storage"</c> block: <c>"slots"</c> (each a box, <c>"fits"</c>, <c>"stack"</c>, <c>"label"</c>) and/or <c>"bulk"</c>.</summary>
    internal static void ParseStorage(ConstructPartDef d, JsonElement b, string file)
    {
        var bw = $"{file}, storage";
        ConstructParts.Known(b, ["ammoStore", "gasLitres", "slots", "bulk", "maxMass", "gunBench", "ordnanceBench", "recycler", "gather", "refine", "assemble", "extrude", "wheel", "beacon", "scoop", "terminal", "press", "pieces"], bw);
        if (ConstructParts.Opt(b, "pieces") is { } pc)
        {
            // A Piece Rack (ConstructConveyor.cs): whole pieces by kind, the largest section and length it holds and how many.
            if (pc.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{file}: storage \"pieces\" is an object (section, length, count)");
            ConstructParts.Known(pc, ["section", "length", "count", "hold"], $"{bw}.pieces");
            var sec = ConstructParts.Need(pc, "section", $"{bw}.pieces");
            if (sec.ValueKind != JsonValueKind.Array || sec.GetArrayLength() != 2)
                throw new ConstructPartException($"{file}: storage.pieces.section is [across, up] in metres");
            d.RackSection = new Vector2(sec[0].GetSingle(), sec[1].GetSingle());
            d.RackLength = ConstructParts.Num(ConstructParts.Need(pc, "length", $"{bw}.pieces"), file, "storage.pieces.length");
            d.RackCount = (int)ConstructParts.Num(ConstructParts.Need(pc, "count", $"{bw}.pieces"), file, "storage.pieces.count");
            ConstructParts.Range(d.RackSection.X, 0.1f, 32, file, "storage.pieces.section");
            ConstructParts.Range(d.RackSection.Y, 0.1f, 32, file, "storage.pieces.section");
            ConstructParts.Range(d.RackLength, 0.1f, 192, file, "storage.pieces.length");
            ConstructParts.Range(d.RackCount, 1, CStore.MaxPerKind, file, "storage.pieces.count");
            // Its hold: the box pieces pack into (the part kit writes it from the frame; else inside the bounds, as the first racks were).
            if (ConstructParts.Opt(pc, "hold") is { } hb)
            {
                if (hb.ValueKind != JsonValueKind.Array || hb.GetArrayLength() != 2 || hb[0].GetArrayLength() != 3 || hb[1].GetArrayLength() != 3)
                    throw new ConstructPartException($"{file}: storage.pieces.hold is [[x, y, z], [x, y, z]] in metres");
                d.RackHoldMin = new Vector3(hb[0][0].GetSingle(), hb[0][1].GetSingle(), hb[0][2].GetSingle());
                d.RackHoldMax = new Vector3(hb[1][0].GetSingle(), hb[1][1].GetSingle(), hb[1][2].GetSingle());
                if (!(d.RackHoldMax.X - d.RackHoldMin.X >= 0.1f && d.RackHoldMax.Y - d.RackHoldMin.Y >= 0.1f && d.RackHoldMax.Z - d.RackHoldMin.Z >= 0.1f))
                    throw new ConstructPartException($"{file}: storage.pieces.hold is at least 0.1 m each way");
            }
            d.IsPieceRack = true;
        }
        d.IsGunBench = ConstructParts.Opt(b, "gunBench") is { ValueKind: System.Text.Json.JsonValueKind.True };
        d.IsOrdnanceBench = ConstructParts.Opt(b, "ordnanceBench") is { ValueKind: System.Text.Json.JsonValueKind.True };
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
                ConstructParts.Known(s, ["min", "max", "at", "size", "fits", "stack", "label", "rack"], $"{file}, {where}");
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
                var rack = ConstructParts.Opt(s, "rack") is { ValueKind: JsonValueKind.True };
                if (rack && ConstructParts.Opt(s, "stack") != null)
                    throw new ConstructPartException($"{file}: {where}: a rack slot takes as many as fit: no \"stack\"");
                var label = ConstructParts.Opt(s, "label") is { } lb ? ConstructParts.Str(lb, file, where + ".label") : "";
                if (label.Length > 16 || label.Any(c => c < 32 || c > 126))
                    throw new ConstructPartException($"{file}: {where}.label is at most 16 plain ASCII characters");
                slots.Add(new CSlotDef(min, max, fits.ToArray(), rack ? MaxRack : stack, label, rack));
            }
            if (slots.Count > CStore.MaxSlots)
                throw new ConstructPartException($"{file}: at most {CStore.MaxSlots} slots a storage part");
        }
        d.GasLitres = ConstructParts.Opt(b, "gasLitres") is { } gl ? gl.GetSingle() : 0f;
        if (ConstructParts.Opt(b, "bulk") is { } bulk)
        {
            if (bulk.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{file}: storage \"bulk\" is an object (material, capacity, min, max)");
            ConstructParts.Known(bulk, ["material", "capacity", "min", "max", "volume", "chuteLoad"], $"{bw}.bulk");
            d.BulkMaterial = ConstructParts.Str(ConstructParts.Need(bulk, "material", $"{bw}.bulk"), file, "storage.bulk.material");
            d.IsVolumeStore = ConstructParts.Opt(bulk, "volume") is { ValueKind: JsonValueKind.True };
            if (d.IsVolumeStore && (d.BulkMaterial != Any || ConstructParts.Opt(bulk, "capacity") != null || ConstructParts.Opt(bulk, "min") != null || ConstructParts.Opt(bulk, "max") != null))
                throw new ConstructPartException($"{file}: a storage volume's \"bulk\" is {{\"material\": \"{Any}\", \"volume\": true}}: its box's settings give its size and capacity");
            if (d.BulkMaterial != Any && RecyclingTable.Current.IdOf(d.BulkMaterial) == 0)
                throw new ConstructPartException($"{file}: storage.bulk.material \"{d.BulkMaterial}\" isn't a bulk material (\"{Any}\", {string.Join(", ", RecyclingTable.Current.Materials)})");
            d.AmmoStore = ConstructParts.Opt(b, "ammoStore") is { ValueKind: JsonValueKind.True };
            d.ChuteLoad = ConstructParts.Opt(bulk, "chuteLoad") is { } cl ? (int)ConstructParts.Num(cl, file, "storage.bulk.chuteLoad") : ConstructRoutes.ChuteLoad;
            ConstructParts.Range(d.ChuteLoad, ConstructRoutes.ChuteLoad, MaxBulk, file, "storage.bulk.chuteLoad");
            if (d.IsVolumeStore)
                d.BulkCapacity = ConstructStoreVolume.MaxBulk; // (the most any box takes; the real figure is ConstructStorage.Capacity)
            else
            {
                var cap = ConstructParts.Num(ConstructParts.Need(bulk, "capacity", $"{bw}.bulk"), file, "storage.bulk.capacity");
                ConstructParts.Range(cap, 1, ConstructParts.Opt(b, "scoop") != null ? ConstructStoreVolume.MaxBulk : MaxBulk, file, "storage.bulk.capacity"); // (a capital bucket holds more than any bin)
                d.BulkCapacity = (int)cap;
                d.FillMin = ConstructParts.Vec(ConstructParts.Need(bulk, "min", $"{bw}.bulk"), file, "storage.bulk.min");
                d.FillMax = ConstructParts.Vec(ConstructParts.Need(bulk, "max", $"{bw}.bulk"), file, "storage.bulk.max");
                if (d.FillMax.X - d.FillMin.X < 0.05f || d.FillMax.Y - d.FillMin.Y < 0.05f || d.FillMax.Z - d.FillMin.Z < 0.05f)
                    throw new ConstructPartException($"{file}: storage.bulk's fill box is smaller than 5 cm a side (or inside out)");
                if (Outside(d, d.FillMin, d.FillMax))
                    throw new ConstructPartException($"{file}: storage.bulk's fill box is outside the part");
            }
        }
        if (d.IsVolumeStore && (ConstructParts.Opt(b, "recycler") != null || ConstructParts.Opt(b, "gather") != null || ConstructParts.Opt(b, "refine") != null))
            throw new ConstructPartException($"{file}: a storage volume is only a store: no recycler, gather or refine block");
        if (ConstructParts.Opt(b, "recycler") is { } rec)
        {
            if (rec.ValueKind != JsonValueKind.Object || d.BulkCapacity == 0)
                throw new ConstructPartException($"{file}: storage \"recycler\" is an object (intake: min, max) on a bin: its output");
            ConstructParts.Known(rec, ["min", "max"], $"{bw}.recycler");
            d.IntakeMin = ConstructParts.Vec(ConstructParts.Need(rec, "min", $"{bw}.recycler"), file, "storage.recycler.min");
            d.IntakeMax = ConstructParts.Vec(ConstructParts.Need(rec, "max", $"{bw}.recycler"), file, "storage.recycler.max");
            if (d.IntakeMax.X - d.IntakeMin.X < 0.2f || d.IntakeMax.Y - d.IntakeMin.Y < 0.2f || d.IntakeMax.Z - d.IntakeMin.Z < 0.2f)
                throw new ConstructPartException($"{file}: storage.recycler's intake box is smaller than 20 cm a side (or inside out)");
            if (d.BulkMaterial != Any)
                throw new ConstructPartException($"{file}: a recycler's bin takes \"{Any}\" material");
            d.IsRecycler = true;
            d.DataIn = [ConstructGatherer.RunPort];
        }
        if (ConstructParts.Opt(b, "gather") is { } gat)
        {
            if (gat.ValueKind != JsonValueKind.Object || d.BulkCapacity == 0 || d.IsRecycler)
                throw new ConstructPartException($"{file}: storage \"gather\" is an object (kind, rate, min, max) on a bin: its output");
            ConstructParts.Known(gat, ["kind", "rate", "min", "max", "ratedRpm", "shaftTorque"], $"{bw}.gather");
            var kind = ConstructParts.Need(gat, "kind", $"{bw}.gather").GetString();
            if (kind is not ("saw" or "drill"))
                throw new ConstructPartException($"{file}: storage.gather.kind is \"saw\" or \"drill\"");
            d.GatherSaw = kind == "saw";
            d.GatherRate = ConstructParts.Num(ConstructParts.Need(gat, "rate", $"{bw}.gather"), file, "storage.gather.rate");
            ConstructParts.Range(d.GatherRate, 0.1f, 1000, file, "storage.gather.rate");
            if (ConstructParts.Opt(gat, "ratedRpm") is { } rpm)
            {
                d.GatherRpm = ConstructParts.Num(rpm, file, "storage.gather.ratedRpm");
                ConstructParts.Range(d.GatherRpm, 100, 20000, file, "storage.gather.ratedRpm");
                d.GatherTorque = ConstructParts.Num(ConstructParts.Need(gat, "shaftTorque", $"{bw}.gather"), file, "storage.gather.shaftTorque");
                ConstructParts.Range(d.GatherTorque, 1, 20000, file, "storage.gather.shaftTorque");
            }
            d.WorkMin = ConstructParts.Vec(ConstructParts.Need(gat, "min", $"{bw}.gather"), file, "storage.gather.min");
            d.WorkMax = ConstructParts.Vec(ConstructParts.Need(gat, "max", $"{bw}.gather"), file, "storage.gather.max");
            if (d.WorkMax.X - d.WorkMin.X < 0.1f || d.WorkMax.Y - d.WorkMin.Y < 0.1f || d.WorkMax.Z - d.WorkMin.Z < 0.1f)
                throw new ConstructPartException($"{file}: storage.gather's work box is smaller than 10 cm a side (or inside out)");
            if (d.BulkMaterial != Any)
                throw new ConstructPartException($"{file}: a gathering module's bin takes \"{Any}\" material");
            d.IsGatherer = true;
            d.DataIn = [ConstructGatherer.RunPort];
        }
        if (ConstructParts.Opt(b, "refine") is { } rf)
        {
            if (rf.ValueKind != JsonValueKind.Object || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer)
                throw new ConstructPartException($"{file}: storage \"refine\" is an object (machine, capacity, min, max: its input) on a bin: its output");
            ConstructParts.Known(rf, ["machine", "capacity", "min", "max", "speed"], $"{bw}.refine");
            d.RefineMachine = ConstructParts.Str(ConstructParts.Need(rf, "machine", $"{bw}.refine"), file, "storage.refine.machine");
            if (RefiningTable.Current.Takes(d.RefineMachine).Length == 0)
                throw new ConstructPartException($"{file}: storage.refine.machine \"{d.RefineMachine}\" has no recipes in Refining.json ({string.Join(", ", RefiningTable.Current.Machines)})");
            var ic = ConstructParts.Num(ConstructParts.Need(rf, "capacity", $"{bw}.refine"), file, "storage.refine.capacity");
            ConstructParts.Range(ic, RefiningTable.MaxUnits, MaxBulk, file, "storage.refine.capacity");
            d.InCapacity = (int)ic;
            d.RefineMix = RefiningTable.Current.IsMix(d.RefineMachine);
            var speed = ConstructParts.Opt(rf, "speed") is { } sp ? ConstructParts.Num(sp, file, "storage.refine.speed") : 1f;
            ConstructParts.Range(speed, 0.1f, 100, file, "storage.refine.speed");
            d.RefineWork = (int)MathF.Round(speed * ConstructRefining.BaseWork);
            d.InFillMin = ConstructParts.Vec(ConstructParts.Need(rf, "min", $"{bw}.refine"), file, "storage.refine.min");
            d.InFillMax = ConstructParts.Vec(ConstructParts.Need(rf, "max", $"{bw}.refine"), file, "storage.refine.max");
            if (d.InFillMax.X - d.InFillMin.X < 0.05f || d.InFillMax.Y - d.InFillMin.Y < 0.05f || d.InFillMax.Z - d.InFillMin.Z < 0.05f)
                throw new ConstructPartException($"{file}: storage.refine's fill box is smaller than 5 cm a side (or inside out)");
            if (Outside(d, d.InFillMin, d.InFillMax))
                throw new ConstructPartException($"{file}: storage.refine's fill box is outside the part");
            if (Overlap(d.InFillMin, d.InFillMax, d.FillMin, d.FillMax))
                throw new ConstructPartException($"{file}: storage.refine's fill box overlaps the output's");
            if (d.BulkMaterial != Any)
                throw new ConstructPartException($"{file}: a production machine's output bin takes \"{Any}\" material");
            if (d.BulkCapacity < RefiningTable.MaxUnits)
                throw new ConstructPartException($"{file}: a production machine's output holds at least {RefiningTable.MaxUnits} (a batch must fit)");
            d.IsRefiner = true;
            d.DataIn = [ConstructGatherer.RunPort];
        }
        if (ConstructParts.Opt(b, "scoop") is { } scoop)
            ConstructScoops.Parse(d, scoop, file, bw);
        if (ConstructParts.Opt(b, "assemble") is { } asm)
            ConstructAssembling.ParseBlock(d, asm, file, slots.Count);
        if (ConstructParts.Opt(b, "extrude") is { } ex)
            ConstructExtruderParts.Parse(d, ex, file, bw);
        if (ConstructParts.Opt(b, "wheel") is { } wh)
            ConstructExtruderParts.ParseWheel(d, wh, file, bw);
        if (ConstructParts.Opt(b, "press") is { } pr)
            ConstructPress.Parse(d, pr, file, slots.Count);
        if (ConstructParts.Opt(b, "beacon") is { } bc)
        {
            d.BeaconKind = ConstructParts.Str(bc, file, "storage.beacon");
            if (!BeaconTable.Current.Has(d.BeaconKind))
                throw new ConstructPartException($"{file}: storage.beacon \"{d.BeaconKind}\" isn't in Beacons.json ({string.Join(", ", BeaconTable.Current.Ids)})");
            if (slots.Count > 0 || d.BulkCapacity > 0)
                throw new ConstructPartException($"{file}: a beacon holds nothing itself: no \"slots\" or \"bulk\"");
            d.IsBeacon = true;
            d.Aid = BuildAids.Of(d.BeaconKind);
            d.DataIn = [ConstructGatherer.RunPort];
        }
        if (ConstructParts.Opt(b, "terminal") is { ValueKind: JsonValueKind.True })
        {
            if (slots.Count == 0 || d.BulkCapacity > 0 || d.IsBeacon)
                throw new ConstructPartException($"{file}: a terminal has \"slots\" (its output tray), no \"bulk\" and no \"beacon\"");
            d.IsTerminal = true;
            d.DataIn = [ConstructGatherer.RunPort];
        }
        if (slots.Count == 0 && d.BulkCapacity == 0 && !d.IsBeacon && !d.IsPieceRack)
            throw new ConstructPartException($"{file}: a storage part needs \"slots\", \"bulk\" or \"pieces\"");
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

    // ---- The state as bytes (the wire; a world save has its own: ConstructWorld.WriteStore) ---------------------------

    /// <summary>A store's contents (compact: four bytes a slot, or the bin's amount): the network's format.</summary>
    public static void Write(PacketWriter w, CStore s)
    {
        w.U8(s.SlotCount);
        for (var k = 0; k < s.SlotCount; k++)
        {
            WriteDef(w, s.Def[k]);
            w.U8(s.Rot[k]).U8(s.Count[k]).U8((byte)Math.Clamp((int)MathF.Round(s.Health[k] * 255), 1, 255));
        }
        var input = s.InAmount > 0 || s.Refused != 0; // (a production machine's: only when it has something to say, so other stores cost no more)
        var machine = s.Blocked != 0 || s.RefusedPart != ConstructParts.None; // (an Assembler's look line)
        var beacon = s.BeaconStores > 0 || s.BeaconParts > 0; // (an Inventory Beacon's network, for the look line: others cost nothing)
        var amount = Math.Clamp(s.Amount, 0, ConstructStoreVolume.MaxBulk);
        var big = amount > ushort.MaxValue; // (a Storage Volume: the top half follows, so a small store costs the same)
        var mix = s.MixN > 0;
        var drive = s.Drive != ShaftState.None;
        var pull = s.PullWaiting > 0; // (a Smart Terminal under way: its panel, on every peer)
        var progress = s.Progress > 0;
        var slabs = s.SlabN > 0 && s.SlabSum == s.ItemCount; // (a Piece Rack's columns: ConstructRackPack.cs)
        var more = drive || mix || machine || pull || progress || slabs; // (the flags byte is full: 64 says a second one follows, so a plain store costs the same)
        w.U16((ushort)amount).U8(s.Material).U8((byte)((s.Busy ? 1 : 0) | (s.Held ? 2 : 0) | (input ? 4 : 0) | (big ? 8 : 0) | (s.KindN > 0 ? 16 : 0) | (beacon ? 32 : 0) | (more ? 64 : 0)));
        if (more)
            w.U8((byte)((drive ? 1 : 0) | (mix ? 2 : 0) | (machine ? 4 : 0) | (pull ? 8 : 0) | (progress ? 16 : 0) | (slabs ? 32 : 0)));
        if (drive)
            w.U8((byte)s.Drive).U16(s.DriveRpm).U8(s.DrivePct);
        if (progress)
            w.U8(s.Progress);
        if (input)
            w.U16((ushort)Math.Clamp(s.InAmount, 0, MaxBulk)).U8(s.InMaterial).U8(s.Refused);
        if (big)
            w.U16((ushort)(amount >> 16));
        if (beacon)
            w.U16((ushort)Math.Min(s.BeaconStores, ushort.MaxValue)).U16((ushort)Math.Min(s.BeaconParts, ushort.MaxValue));
        if (s.KindN > 0)
        {
            w.U16((ushort)s.KindN);
            for (var k = 0; k < s.KindN; k++)
            {
                WriteDef(w, s.KindDef![k]);
                w.U16((ushort)s.KindCount![k]);
            }
        }
        if (mix)
        {
            w.U8((byte)s.MixN);
            for (var k = 0; k < s.MixN; k++)
                w.U8(s.MixMat![k]).U16((ushort)Math.Clamp(s.MixAmount![k], 0, MaxBulk));
        }
        if (machine)
            w.U8(s.Blocked).U16(s.RefusedPart);
        if (pull)
            w.U16(s.PullId).U8((byte)(s.PullMaterial ? 1 : 0)).U8(s.PullWaiting).U8(s.PullSeconds);
        if (slabs)
            ConstructRackPack.Write(w, s);
    }

    /// <summary>A stored part's id, or a run-time piece's key (its id is this machine's own: protocol 244).</summary>
    internal static void WriteDef(PacketWriter w, ushort def)
    {
        if (def != ConstructParts.None && Extrusion.IsRuntimeId(def) && ConstructParts.Get(def) is { } d)
            Extrusion.Write(w, d);
        else
            w.U16(def);
    }

    internal static ushort ReadDef(ref PacketReader r)
    {
        var def = r.U16();
        return def == Extrusion.WireMark ? Extrusion.Read(ref r) : def;
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
            var def = ReadDef(ref r);
            var rot = r.U8();
            var count = r.U8();
            var hp = r.U8();
            if (def != ConstructParts.None && (ConstructParts.Get(def) == null || rot >= Rot24.Count || count == 0 || count > MaxRack))
                throw new MalformedPacketException($"store slot {k}: part {def}, turn {rot}, {count} of them");
            s.Def[k] = def;
            s.Rot[k] = def == ConstructParts.None ? (byte)0 : rot;
            s.Count[k] = def == ConstructParts.None ? (byte)0 : count;
            s.Health[k] = hp / 255f;
        }
        s.Amount = r.U16();
        s.Material = r.U8();
        if (s.Material > RecyclingTable.Current.Materials.Length)
            throw new MalformedPacketException($"a store of material {s.Material}");
        var flags = r.U8();
        s.Busy = (flags & 1) != 0;
        s.Held = (flags & 2) != 0;
        var more = (flags & 64) != 0 ? r.U8() : 0;
        if (more >= 64)
            throw new MalformedPacketException($"a store with more flags {more}");
        if ((more & 1) != 0)
        {
            var state = r.U8();
            if (state == 0 || state >= (byte)ShaftState.COUNT)
                throw new MalformedPacketException($"a store with shaft state {state}");
            s.Drive = (ShaftState)state;
            s.DriveRpm = r.U16();
            s.DrivePct = Math.Min(r.U8(), (byte)100);
        }
        s.Progress = (more & 16) != 0 ? Math.Min(r.U8(), (byte)100) : (byte)0;
        if ((flags & 4) != 0)
        {
            s.InAmount = Math.Min((int)r.U16(), MaxBulk);
            s.InMaterial = r.U8();
            s.Refused = r.U8();
            var known = RecyclingTable.Current.Materials.Length;
            if (s.InMaterial > known || s.Refused > known)
                throw new MalformedPacketException($"a machine's input of material {s.InMaterial}, refused {s.Refused}");
        }
        else
            s.InAmount = s.InMaterial = s.Refused = 0; // (nothing to say any more: the writer sends these only when it has)
        if ((flags & 8) != 0)
            s.Amount |= r.U16() << 16;
        s.Amount = Math.Min(s.Amount, ConstructStoreVolume.MaxBulk);
        if ((flags & 32) != 0)
        {
            s.BeaconStores = r.U16();
            s.BeaconParts = r.U16();
        }
        if ((flags & 16) != 0)
        {
            int kinds = r.U16();
            if (kinds == 0 || kinds > CStore.MaxKinds)
                throw new MalformedPacketException($"a store of {kinds} part kinds");
            s.KindDef ??= new ushort[CStore.MaxKinds];
            s.KindCount ??= new int[CStore.MaxKinds];
            for (var k = 0; k < kinds; k++)
            {
                var def = ReadDef(ref r);
                int count = r.U16();
                if (ConstructParts.Get(def) == null || count == 0 || count > CStore.MaxPerKind)
                    throw new MalformedPacketException($"store kind {k}: part {def}, {count} of them");
                s.KindDef[k] = def;
                s.KindCount[k] = count;
            }
            s.KindN = kinds;
            s.Recount();
        }
        if ((more & 2) != 0)
        {
            int mix = r.U8();
            if (mix == 0 || mix > CStore.MaxMix)
                throw new MalformedPacketException($"a mixed input of {mix} materials");
            var known = RecyclingTable.Current.Materials.Length;
            for (var k = 0; k < mix; k++)
            {
                var m = r.U8();
                int amount = r.U16();
                if (m == 0 || m > known || amount == 0 || amount > MaxBulk || s.MixOf(m) != 0)
                    throw new MalformedPacketException($"a mixed input of {amount} of material {m}");
                s.AddMix(m, amount);
            }
        }
        if ((more & 4) != 0)
        {
            s.Blocked = r.U8();
            s.RefusedPart = r.U16();
            if (s.RefusedPart != ConstructParts.None && ConstructParts.Get(s.RefusedPart) == null)
                throw new MalformedPacketException($"refused part {s.RefusedPart}");
        }
        if ((more & 8) != 0)
        {
            s.PullId = r.U16();
            s.PullMaterial = r.U8() != 0;
            s.PullWaiting = r.U8();
            s.PullSeconds = r.U8();
            if (s.PullWaiting == 0 || s.PullWaiting > ConstructTerminals.MaxJobs || (s.PullMaterial ? s.PullId > RecyclingTable.Current.Materials.Length : ConstructParts.Get(s.PullId) == null))
                throw new MalformedPacketException($"a terminal pulling {s.PullId}, {s.PullWaiting} waiting");
        }
        if ((more & 32) != 0)
        {
            ConstructRackPack.Read(ref r, s);
            if (s.SlabSum != s.ItemCount)
                throw new MalformedPacketException($"a rack of {s.ItemCount} pieces placed {s.SlabSum}");
        }
        s.Live = true;
        s.Version++;
    }
}

/// <summary>The world's side of physical storage (ConstructStorage.cs): the store pool and every change to what is stored.</summary>
public sealed partial class ConstructWorld
{
    public const int MaxStores = 1024; // (a wall of shelf bays is a store each; on the wire every handle is a u16)

    /// <summary>Every container's contents, by handle (<see cref="CPart.Store"/>; 0 is none). Server: given out as storage parts are added; client: as heard.</summary>
    public readonly CStore[] Stores = MakeStores();

    /// <summary>Inventory Beacons (ConstructBeacons.cs): the chute networks they broadcast. Server only.</summary>
    public readonly ConstructBeacons Beacons = new();

    /// <summary>Server: the stores that changed (sent to clients after the structure).</summary>
    public readonly HashSet<ushort> StoreDirty = new(MaxStores);

    public const int LoadEvery = 15; // ticks between looks at the bins' loads (4 Hz); a gatherer deposits at 10 Hz, a chute at 1 Hz

    /// <summary>A bin's bulk changed (server: deposited or taken; client: as heard): <see cref="LoadStep"/> looks at the next turn.</summary>
    public bool LoadChanged;

    public int LoadRefreshes; // bodies redone, ever (the self-test checks the throttle)

    private int _loadTicks;

    /// <summary>
    /// A bin's load counts in its construct's mass, centre of mass and inertia (docs/sandbox/CONSTRUCTS.md "Gathering"). Bulk
    /// moves as often as a gatherer deposits, so a deposit only raises <see cref="LoadChanged"/> and this, four times a
    /// second at most, redoes the mass of the bodies whose bins differ from their last build (<see cref="Construct.RefreshMass"/>,
    /// not a rebuild). A rebuild reads the stores too, so a load is never stale after a structural change. The server wakes
    /// what it changed (a parked truck settles under its load); a client only follows. Allocates nothing.
    /// </summary>
    public void LoadStep(bool server)
    {
        if (++_loadTicks < LoadEvery || !LoadChanged)
            return;
        _loadTicks = 0;
        LoadChanged = false;
        for (var i = 0; i < Bodies.Count; i++)
        {
            var c = Bodies[i];
            if (!c.HasBin || c.Dirty || c.BinLoadKey() == c.LoadKey)
                continue; // (a dirty body's rebuild reads the stores anyway)
            c.RefreshMass();
            LoadRefreshes++;
            if (server && !c.Anchored)
                c.Wake();
        }
    }

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
            Beacons.Dirty = true;
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
        Beacons.Dirty = true;
    }

    private void Changed(CStore s)
    {
        s.Version++;
        Beacons.Stock++;
        StoreDirty.Add(s.Handle);
    }

    /// <summary>Server: sets a part into a slot (the caller checked reach and the hands). Null, or why not.</summary>
    public string? SetIn(Construct c, int part, int slot, ConstructPartDef d)
    {
        if (StoreOf(c, part) is not { } st)
            return "that isn't storage";
        var cd = c.DefOf(part);
        if (cd.IsVolumeStore)
            return PutPart(c, part, d);
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
    public int Deposit(Construct c, int part, int amount, byte material)
    {
        if (StoreOf(c, part) is not { } st || !c.DefOf(part).IsBin || amount <= 0 || material == 0 || !ConstructStorage.BinTakes(c.DefOf(part), material))
            return 0;
        if (st.Amount > 0 && st.Material != material || st.KindN > 0)
            return 0; // (a bin holds one material at a time; a Storage Volume bulk or parts)
        var n = Math.Min(amount, ConstructStorage.Capacity(c, part) - st.Amount);
        if (n <= 0)
            return 0;
        st.Material = material;
        st.Amount += n;
        LoadChanged = true;
        Changed(st);
        return n;
    }

    /// <summary>Server: bulk out of a bin; returns how much came out.</summary>
    /// <summary>Server: parts of one kind taken out of a box of parts (a faction's courier at a Requisition Crate: GameStoryJobs.cs).</summary>
    public void TakeKind(CStore st, ushort def, int n)
    {
        if (n <= 0 || st.KindIndex(def) < 0)
            return;
        st.AddKind(def, -Math.Min(n, st.KindCount![st.KindIndex(def)]));
        LoadChanged = true;
        Changed(st);
    }

    /// <summary>Server: bulk taken out of a store's bin, gone (a faction's courier at a Requisition Crate).</summary>
    public void TakeBulk(CStore st, int units)
    {
        if (units <= 0 || st.Amount <= 0)
            return;
        SetBulk(st, st.Material, Math.Max(0, st.Amount - units));
    }

    /// <summary>Server: a store's bulk set outright (the configure tool in creative: ConstructStoreVolume.SetContents).</summary>
    public void SetBulk(CStore st, byte material, int units)
    {
        st.Material = units > 0 ? material : (byte)0;
        st.Amount = units;
        LoadChanged = true;
        Changed(st);
    }

    public int Withdraw(Construct c, int part, int amount)
    {
        if (StoreOf(c, part) is not { } st || amount <= 0)
            return 0;
        var n = Math.Min(amount, st.Amount);
        if (n <= 0)
            return 0;
        st.Amount -= n;
        if (st.Amount == 0)
            st.Material = 0;
        LoadChanged = true;
        Changed(st);
        return n;
    }

    /// <summary>
    /// Server: bulk into a production machine's input (a chute, or F); returns how much went in, up to its room. A material
    /// the machine has no recipe for, or another than its input holds, goes nowhere: it is noted for the look line.
    /// </summary>
    public int Feed(Construct c, int part, int amount, byte material)
    {
        if (StoreOf(c, part) is not { } st || !c.DefOf(part).IsRefiner || amount <= 0 || material == 0)
            return 0;
        var d = c.DefOf(part);
        if (d.RefineMix)
            return FeedMix(c, part, st, d, amount, material);
        if (RefiningTable.Current.Find(d.RefineMachine, material) is not { } recipe || recipe.MinTier > d.Tier || st.InAmount > 0 && st.InMaterial != material
            || ConstructRefining.SetOf(c.Parts[part], d) is { } set && set.Input != material) // (a set recipe takes only its own input: ConstructRefining.SetOf)
        {
            st.RefusedAge = 0;
            if (st.Refused != material)
            {
                st.Refused = material;
                Changed(st);
            }
            return 0;
        }
        var n = Math.Min(amount, d.InCapacity - st.InAmount);
        if (n <= 0)
            return 0;
        st.InMaterial = material;
        st.InAmount += n;
        LoadChanged = true;
        Changed(st);
        return n;
    }

    /// <summary>Server: into a mixed input: a material some recipe takes alongside what it holds, up to its capacity of that material.</summary>
    private int FeedMix(Construct c, int part, CStore st, ConstructPartDef d, int amount, byte material)
    {
        if (!ConstructRefining.MixTakes(RefiningTable.Current, d, st, material, ConstructRefining.SetOf(c.Parts[part], d)))
        {
            NoteRefused(st, material);
            return 0;
        }
        var n = Math.Min(amount, d.InCapacity - st.MixOf(material));
        if (n <= 0)
            return 0;
        AddInput(st, material, n);
        return n;
    }

    /// <summary>Server: a chute's load stays in its source: the look line says so for a few seconds.</summary>
    internal void NoteRefused(CStore st, byte material)
    {
        st.RefusedAge = 0;
        if (st.Refused == material)
            return;
        st.Refused = material;
        Changed(st);
    }

    /// <summary>Server: a chute's part stays in its source: the look line says so for a few seconds.</summary>
    internal void NoteRefusedPart(CStore st, ushort part)
    {
        st.RefusedAge = 0;
        if (st.RefusedPart == part)
            return;
        st.RefusedPart = part;
        Changed(st);
    }

    /// <summary>Server: a mixed input gains (or, negative, loses) some of a material.</summary>
    internal void AddInput(CStore st, byte material, int n)
    {
        st.AddMix(material, n);
        LoadChanged = true;
        Changed(st);
    }

    /// <summary>Server: a batch out of a production machine's input.</summary>
    internal void UseInput(CStore st, int amount)
    {
        st.InAmount -= amount;
        if (st.InAmount <= 0)
        {
            st.InAmount = 0;
            st.InMaterial = 0;
        }
        LoadChanged = true;
        Changed(st);
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
        if (d.Warhead && d.Explosive is { } bomb)
            SetOff(d, bomb, ConstructRender.PartCentre(c, part), c, (int)(ConstructWeapons.CookOffDelay * World.TickRate)); // (stored ordnance with a warhead goes off in its container)
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
    /// parts anyone may pick up (a pile as many as there were, while the caps allow); a bin's contents as wrecked bundles
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
        var box = cd.IsVolumeStore;
        var boxMid = Vector3.Zero;
        if (box)
            ConstructStorage.FillBox(c, part, st, out boxMid, out _);
        if (cd.IsAssembler)
            boxMid = cp.Pos + Rot24.Apply(cp.Rot, (cd.InFillMin + cd.InFillMax) * 0.5f);
        if (st.KindN > 0)
            SpillParts(c, st, boxMid, q, t, ref spilled, ref lost);
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
        if ((st.Amount > 0 || st.InAmount > 0 || st.MixN > 0) && ConstructParts.Find(ConstructStorage.MaterialBundle) is { } bundle)
        {
            var each = Math.Max(1, bundle.Yield);
            var (pos, rot) = (box ? boxMid : cp.Pos, cp.Rot);
            var most = box ? ConstructStoreVolume.MaxSpill : ConstructStorage.MaxSpillBundles;
            void Heap(int amount, byte material, Vector3 mid)
            {
                var n = amount <= 0 ? 0 : Math.Clamp((amount + each - 1) / each, 1, most);
                for (var k = 0; k < n; k++)
                {
                    var at = t + Vector3.Transform(pos + Rot24.Apply(rot, mid + new Vector3((k % 3 - 1) * 0.25f, 0.3f * (k / 3), 0)), q);
                    if (Spawn(bundle, at, c.PointVel(at) + SpillKick(k, 0)) is { } heap)
                    {
                        heap.Parts[0].Setting = material; // (a wrecked bundle: the Recycler gives this material back; it goes as debris does)
                        heap.Parts[0].Rounds = (ushort)Math.Clamp(amount - k * each, 1, each); // (what it is worth: a bin's last few units aren't a whole bundle)
                        heap.Parts[0].Broken = true;
                        heap.Parts[0].Health = 0;
                        heap.Wreck = true;
                        heap.Damaged = true;
                        heap.Owner = 255;
                        heap.Rebuild();
                        spilled++;
                    }
                }
            }
            Heap(st.Amount, st.Material, box ? Vector3.Zero : (cd.FillMin + cd.FillMax) * 0.5f);
            Heap(st.InAmount, st.InMaterial, (cd.InFillMin + cd.InFillMax) * 0.5f); // (a production machine's input spills the same way)
            for (var k = 0; k < st.MixN; k++)
                Heap(st.MixAmount![k], st.MixMat![k], (cd.InFillMin + cd.InFillMax) * 0.5f + new Vector3(0, 0.3f * k, 0));
        }
        if (spilled + lost > 0)
            Events.Add($"construct {c.Id}: a {cd.Name} spilled {spilled} thing(s){(lost > 0 ? $" ({lost} lost: the world is full)" : "")}");
        FreeStore(ref cp);
    }

    private static Vector3 SpillKick(int k, int n) => new(((k * 7 + n * 3) % 5 - 2) * 0.35f, 1.6f, ((k * 3 + n * 5) % 5 - 2) * 0.35f);

    private Construct? Spawn(ConstructPartDef d, Vector3 at, Vector3 vel)
    {
        if (!HasRoom())
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

    /// <summary>The nearest stored part (or bin's fill) of container <paramref name="i"/> along a ray in its segment frame (before <paramref name="best"/>); the slot (or -1 for a fill), else -2. A Storage Volume's fill answers only a player's look: it isn't solid, and a shot through it mustn't land on its far-off part.</summary>
    private int RayStored(Construct c, int i, Vector3 lo, Vector3 ld, ref float best, bool look)
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
            if (cd.Slots[k].Rack)
            {
                // (a shelf's pile is one box to a ray, however many stand in it: a look or a shot never walks them)
                ConstructStorage.PileBox(p, cd.Slots[k], d, st.Rot[k], st.Count[k], out var pc, out var ph);
                if (Geometry.RayAabb(pc - ph, pc + ph, lo, ld, best) is { } pd && pd < best)
                {
                    best = pd;
                    found = k;
                }
                continue;
            }
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
        if ((look || !cd.IsVolumeStore) && ConstructStorage.FillBox(c, i, st, out var fc, out var fh) && Geometry.RayAabb(fc - fh, fc + fh, lo, ld, best) is { } fd && fd < best)
        {
            best = fd;
            found = -1;
        }
        if (ConstructStorage.InputBox(p, cd, st.InTotal, out var ic, out var ih) && Geometry.RayAabb(ic - ih, ic + ih, lo, ld, best) is { } id && id < best)
        {
            best = id;
            found = -1;
        }
        return found;
    }
}
