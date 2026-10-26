using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Zombies: taking a whole build apart at the workbench (docs/zombies/ZOMBIES-MODE.md, "Disassembling a build"). Every
/// part of the slot's build, the grip or hilt included, goes to the player's loose parts and the slot is left empty.
/// Server-authoritative: a client asks with <see cref="Msg.ZBench"/> (kind <see cref="BenchDisassemble"/>), the server
/// checks it, moves the parts, and sends <see cref="Msg.ZRun"/> (the parts) and a <see cref="Msg.ZBuild"/> with
/// <see cref="ZBuildCleared"/> (the slot is empty) to everyone; joiners get empty slots when they connect (protocol 41).
/// An empty gun slot 1-2 holds <see cref="EmptyHands"/> (nothing drawn, can't fire); an empty slot 3-4 is null, as
/// before the slot upgrade filled it; an empty melee slot falls back to the standard melee strike.
/// </summary>
public sealed partial class Game
{
    /// <summary>The <see cref="Msg.ZBench"/> kind that takes the build in a slot apart (0 put, 1 salvage, 2 craft).</summary>
    public const int BenchDisassemble = 3;

    /// <summary><see cref="Msg.ZBuild"/> flag (with 1 = full ammo, 2 = start gear): the slot is now empty; no build text follows.</summary>
    public const byte ZBuildCleared = 4;

    private static WeaponDef? _emptyHands;

    /// <summary>
    /// What an empty gun slot 1-2 holds: no model (an empty built mesh), no magazine, no damage. Firing it only clicks,
    /// and the server refuses its shots (<see cref="CartBlocksShot"/>). Each player's copy gets its own weapon id.
    /// </summary>
    public static WeaponDef EmptyHands
    {
        get
        {
            if (_emptyHands != null)
                return _emptyHands;
            var d = Weapons.DefaultSecondary.Clone();
            d.Name = "Empty hands";
            d.Blurb = "Nothing in this slot: build a gun at a workbench, buy one off a wall or from the box.";
            d.Mag = 0;
            d.Damage = 0;
            d.Pellets = 1;
            d.Mobility = 1f;
            d.EquipTime = 0.2f;
            d.Optic = OpticKind.Iron;
            d.BuildFlags = ["empty"];
            d.BuiltMesh = new GunMesh(); // no parts: nothing is drawn in hands, first or third person
            return _emptyHands = d;
        }
    }

    /// <summary>Is <paramref name="d"/> an empty gun slot (<see cref="EmptyHands"/> or a player's copy of it)?</summary>
    public static bool IsEmptyGun(WeaponDef? d) => d != null && d.BuildFlags.Contains("empty");

    /// <summary>Does <paramref name="p"/> have a gun in slot <paramref name="slot"/> (0-3; a stock gun counts)?</summary>
    public static bool HasGunIn(Player p, int slot) => slot switch
    {
        0 => !IsEmptyGun(p.BuiltPrimary),
        1 => !IsEmptyGun(p.BuiltSecondary),
        2 or 3 => p.ExtraSlots && p.BuiltExtra[slot - 2] != null,
        _ => false,
    };

    /// <summary>The weapon id in <paramref name="p"/>'s slot <paramref name="slot"/> (0 for an empty slot 3-4).</summary>
    public static byte WeaponIn(Player p, int slot) => slot switch
    {
        0 => p.Primary,
        1 => p.Secondary,
        2 or 3 => p.BuiltExtra[slot - 2]?.Id ?? 0,
        _ => 0,
    };

    /// <summary>
    /// Where the hands go when slot <paramref name="cleared"/> empties under them (both sides pick the same): the first
    /// slot that still holds a gun, else the emptied slot itself if it's slot 1-2 (empty hands), else slot 1.
    /// </summary>
    public static int FallbackSlot(Player p, int cleared)
    {
        for (var s = 0; s < 4; s++)
            if (HasGunIn(p, s))
                return s;
        return cleared is 0 or 1 ? cleared : 0;
    }

    /// <summary>The first empty gun slot <paramref name="p"/> has (1-2 emptied by disassembly, then free slots 3-4), or -1.</summary>
    public static int FreeGunSlot(Player p)
    {
        for (var s = 0; s < 4; s++)
            if (!HasGunIn(p, s) && (s < 2 || p.ExtraSlots))
                return s;
        return -1;
    }

    // ---- Server --------------------------------------------------------------------------------------

    /// <summary>
    /// Server (from <see cref="ServerBench"/>, which already checked you're standing at a workbench): takes the build in
    /// <paramref name="slot"/> (0-3 guns, 4 melee) apart into your loose parts. Returns why not, or null.
    /// </summary>
    private string? ServerDisassemble(Player p, ZPlayer zp, int slot)
    {
        if (slot is < 0 or > 4 || (slot is 2 or 3 && !p.ExtraSlots))
            return "That slot isn't yours.";
        if (zp.Slots[slot] is not { } old)
            return slot == 4 ? "There's no melee build to take apart." : $"Slot {slot + 1} has no build to take apart.";
        var counts = PartCounts(old);
        var total = 0;
        foreach (var (name, n) in counts)
        {
            zp.Parts[name] = zp.Parts.GetValueOrDefault(name) + n;
            total += n;
        }
        ClearSlot(p, slot);
        SendZRun(p);
        var where = AtWorkbench(p) ? "Workbench" : "At the box";
        ZFeedTo(p, $"{where}: took the {old.Name} apart: {total} part{(total == 1 ? "" : "s")} to your loose parts.", Sfx.HitTick);
        return null;
    }

    // ---- At the mystery box (board item "Zombies": disassemble at the box, 2026-10-26) ------------------

    /// <summary>How close to the mystery box (its current spot) a player must stand to take a build apart there.</summary>
    public const float BoxDisassembleRange = 2.4f;

    /// <summary>Is <paramref name="p"/> standing at a workbench (the range every bench action allows)?</summary>
    private bool AtWorkbench(Player p)
    {
        if (ZMap is not { } zm)
            return false;
        foreach (var b in zm.Benches)
            if (Flat(b.At - p.Position) <= 3.5f)
                return true;
        return false;
    }

    /// <summary>
    /// Can <paramref name="p"/> take a build apart at the mystery box now? Null when yes, else why not. Free, and it works
    /// while the box is idle, spinning or offering (it's your own gun, not the box's); not while the teddy bear has it
    /// (the box is flying off to another spot).
    /// </summary>
    public string? BoxDisassembleRefusal(Player p)
    {
        if (ZMap is not { } zm || zm.BoxSpots.Count == 0)
            return "There's no mystery box on this map.";
        var spot = zm.BoxSpots[Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1)];
        if (Flat(spot.At - p.Position) > BoxDisassembleRange)
            return "You're not at a workbench or the mystery box.";
        if (BoxState == ZBoxState.Teddy)
            return "The mystery box is moving: take it apart at a workbench, or wait for the box.";
        return null;
    }

    /// <summary>Local: the slot armed for a disassembly at the box (the first press of the key) and when; -1 when not armed.</summary>
    private int _boxDisArmedSlot = -1;

    private double _boxDisArmedAt;

    /// <summary>Local: is the box-side disassembly of slot <paramref name="slot"/> armed (a second press confirms it)?</summary>
    public bool BoxDisassembleArmed(int slot, double now) => _boxDisArmedSlot == slot && now - _boxDisArmedAt < 4.0;

    /// <summary>
    /// Local: what the second key at the mystery box would take apart (the build in your hands, slot 0-3), or -1: you're
    /// standing at the box (within its prompt's reach; idle, spinning or offering), alive and not downed, and the gun in your hands is a build.
    /// </summary>
    public int BoxDisassembleSlot()
    {
        if (Local is not { } me || ZMap is not { } zm || zm.BoxSpots.Count == 0 || !IsStanding(me) || Phase != MatchPhase.Playing || BoxDisassembleRefusal(me) != null)
            return -1;
        if (Flat(zm.BoxSpots[Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1)].At - me.Position) > 1.9f)
            return -1; // the box's own prompt range (the server allows a little more, for lag)
        var slot = SlotOf(me, me.Weapon);
        return slot is >= 0 and < 4 && ZP(me).Slots[slot] != null ? slot : -1;
    }

    /// <summary>
    /// Local: the box's disassemble key (the grenade key, G: no grenades in Zombies) was pressed. The first press arms it
    /// and says what will happen; a second press within 4 s sends the same request as the workbench's Disassemble
    /// (<see cref="BenchDisassemble"/>), which the server checks against the box instead of a bench.
    /// </summary>
    public void BoxDisassembleKey(double now)
    {
        if (Local == null || BoxDisassembleSlot() is not (>= 0 and var slot))
            return;
        if (!BoxDisassembleArmed(slot, now))
        {
            _boxDisArmedSlot = slot;
            _boxDisArmedAt = now;
            return;
        }
        _boxDisArmedSlot = -1;
        BenchRequest(BenchDisassemble, slot, ""); // the server checks the box and the slot, and moves the parts
    }

    /// <summary>Local: the HUD's second line at the mystery box (null when there's nothing in your hands to take apart).</summary>
    public string? BoxDisassemblePrompt(string key, double now)
    {
        if (BoxDisassembleSlot() is not (>= 0 and var slot) || Local is not { } me || ZP(me).Slots[slot] is not { } b)
            return null;
        return BoxDisassembleArmed(slot, now)
            ? $"Press [{key}] again to take the {b.Name} apart: all {b.Nodes.Count()} parts to your loose parts, slot {slot + 1} left empty"
            : $"Press [{key}] twice to take the {b.Name} apart into parts (free)";
    }

    /// <summary>
    /// Server: empties <paramref name="p"/>'s slot <paramref name="slot"/> (no parts move here) and tells everyone. The gun
    /// in hand moves to <see cref="FallbackSlot"/> (the client switches the same way).
    /// </summary>
    public void ClearSlot(Player p, int slot)
    {
        if (slot is < 0 or > 4)
            return;
        var zp = ZP(p);
        zp.Slots[slot] = null;
        byte weapon = 0;
        if (slot == 4)
            p.MeleeBuild = null; // the standard melee strike
        else
        {
            var inHand = SlotOf(p, p.Weapon) == slot;
            if (slot < 2)
                weapon = PlaceBuilt(p, slot, EmptyHands).Id;
            else
                p.BuiltExtra[slot - 2] = null;
            if (inHand)
                p.Weapon = WeaponIn(p, FallbackSlot(p, slot));
            if (p.Bot is { } brain)
            {
                brain.Ammo = p.DefFor(p.Weapon).Mag;
                brain.ReloadUntil = 0;
            }
        }
        if (p.Id == LocalId)
            LocalClearSlot(slot);
        if (Net.IsOffline)
            return;
        WriteBuildCleared(_w, p.Id, slot, weapon);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private static void WriteBuildCleared(PacketWriter w, byte player, int slot, byte weapon) =>
        w.Begin(Msg.ZBuild).U8(player).U8((byte)slot).U8(weapon).U8(ZBuildCleared).U16(0);

    /// <summary>Server, for a joiner: the empty gun slots 1-2 of <paramref name="p"/> (others are drawn empty-handed, not with a stock gun).</summary>
    private void SendEmptySlotsTo(RakNetGUID guid, Player p, ZPlayer zp)
    {
        foreach (var s in EmptyGunSlots(p, zp))
        {
            WriteBuildCleared(_w, p.Id, s, WeaponIn(p, s));
            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>The gun slots 1-2 of <paramref name="p"/> emptied at a workbench (slots 3-4 and the melee slot start empty on a joiner anyway).</summary>
    private static IEnumerable<int> EmptyGunSlots(Player p, ZPlayer zp)
    {
        for (var s = 0; s < 2; s++)
            if (zp.Slots[s] == null && !HasGunIn(p, s))
                yield return s;
    }

    // ---- Both sides ----------------------------------------------------------------------------------

    /// <summary>Client: a <see cref="ZBuildCleared"/> build message: player <paramref name="p"/>'s slot is empty now.</summary>
    private void ApplyCleared(Player p, int slot, byte weapon)
    {
        if (slot == 4)
        {
            p.MeleeBuild = null;
            if (p.Id == LocalId)
                ZP(p).Slots[4] = null;
            return;
        }
        if (slot is < 0 or > 3)
            return;
        if (slot < 2)
            PlaceBuilt(p, slot, EmptyHands, weapon);
        else
            p.BuiltExtra[slot - 2] = null;
        if (p.Id != LocalId)
            return;
        ZP(p).Slots[slot] = null;
        LocalClearSlot(slot);
    }

    /// <summary>
    /// The local player's slot <paramref name="slot"/> (0-3) is empty now: it keeps its reserve count (like a workbench
    /// edit, the next build put there at the bench gets it), and if it was in your hands you switch to
    /// <see cref="FallbackSlot"/>: another gun, or empty hands (the melee key still works).
    /// </summary>
    private void LocalClearSlot(int slot)
    {
        if (slot is < 0 or > 3 || Local is not { } me)
            return;
        var old = Slots[slot];
        var def = slot < 2 ? me.DefFor(slot == 0 ? me.Primary : me.Secondary) : EmptyHands;
        Slots[slot] = new WeaponState(def) { Reserve = Math.Max(0, old.Reserve) };
        if (ActiveSlot != slot && ActiveSlot < WeaponSlots)
            return;
        _burstLeft = 0;
        ActiveSlot = FallbackSlot(me, slot);
        var now = Brutal.GlfwApi.Glfw.Time;
        EquipUntil = now + Active.Def.EquipTime;
        NextFireTime = Math.Max(NextFireTime, EquipUntil);
        if (!IsEmptyGun(Active.Def))
            Audio.Equip(Active.Def);
    }

    /// <summary>Local: does slot <paramref name="slot"/> hold a gun you can switch to?</summary>
    public bool SlotHasGun(int slot) => Local is { } me && slot >= 0 && slot < WeaponSlots && HasGunIn(me, slot);

    /// <summary>The weapon switch is refused: that slot is empty (taken apart at a workbench).</summary>
    private bool EmptySlotRefusesSwitch(int slot)
    {
        if (SlotHasGun(slot))
            return false;
        LoadoutNotice = $"Slot {slot + 1} is empty: build a gun for it at a workbench.";
        return true;
    }

    // ---- Self-test hooks -----------------------------------------------------------------------------

    /// <summary>Self-test: the cleared-slot build message for player <paramref name="p"/>'s slot.</summary>
    public static void SelfTestWriteCleared(PacketWriter w, Player p, int slot) => WriteBuildCleared(w, p.Id, slot, WeaponIn(p, slot));

    /// <summary>Self-test: what a joiner gets for <paramref name="p"/>'s slots (the build messages, then the empty slots), as the server sends them.</summary>
    public List<byte[]> SelfTestJoinerBuilds(Player p)
    {
        var list = new List<byte[]>();
        var w = new PacketWriter();
        var zp = ZP(p);
        for (var s = 0; s < 5; s++)
            if (zp.Slots[s] is { } b)
            {
                WriteBuild(w, p.Id, s, WeaponIn(p, s), 0, b.ToText());
                list.Add(w.Span.ToArray());
            }
        foreach (var s in EmptyGunSlots(p, zp))
        {
            WriteBuildCleared(w, p.Id, s, WeaponIn(p, s));
            list.Add(w.Span.ToArray());
        }
        return list;
    }

    /// <summary>Self-test: a client's copy reads a run message (its loose parts and scrap).</summary>
    public void SelfTestReadRun(ReadOnlySpan<byte> msg)
    {
        var r = new PacketReader(msg[1..]);
        ReadZRun(ref r);
    }

    /// <summary>Self-test: the run message the server would send <paramref name="p"/> (its loose parts and scrap).</summary>
    public void SelfTestWriteRun(PacketWriter w, Player p) => WriteZRun(w, ZP(p));
}
