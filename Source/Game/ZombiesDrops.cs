using System.Numerics;
using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// A built weapon lying on the floor in Zombies (docs/zombies/ZOMBIES-MODE.md, "Dropped builds"): left where a player
/// bled out, or swapped out by someone picking one up. Anyone on the team can pick it up (hold the interact key) until it
/// despawns. Server-owned; clients get it in <see cref="Msg.ZPickup"/>.
/// </summary>
public sealed class ZWeaponDrop
{
    public ushort Id;

    /// <summary>The build (its text is what goes over the network; every PC assembles the same weapon).</summary>
    public WeaponBuild Build = new();

    public Vector3 At;

    /// <summary>When it despawns (the game's clock: the server's on the host, the local one on a client).</summary>
    public double Until;

    /// <summary>Who it came from (the player id and the name, kept after they leave).</summary>
    public byte Owner = byte.MaxValue;

    public string OwnerName = "";

    /// <summary>Comes with a full magazine and reserve (a bleed-out drop); a weapon left by a swap keeps the slot's reserve instead.</summary>
    public bool Full;

    /// <summary>A melee build (goes to the melee slot).</summary>
    public bool Melee;

    /// <summary>
    /// Dropped on purpose (the drop key, ZombiesDropItems.cs): it also despawns when round <see cref="ExpireRound"/> ends
    /// (0: only <see cref="Until"/> counts, as for a bleed-out drop).
    /// </summary>
    public int ExpireRound;

    /// <summary>The build's rarity for its label and ring: its rarest part (both sides work it out from the text).</summary>
    public Rarity Rarity;
}

/// <summary>Dropped builds in Zombies: bleeding out drops every built weapon where you went down; hold the interact key to pick one up.</summary>
public sealed partial class Game
{
    /// <summary>Built weapons on the floor (both sides).</summary>
    public readonly List<ZWeaponDrop> ZWeaponDrops = new();

    private ushort _zNextWeaponDrop = 1;

    /// <summary>The most dropped builds at once (4 players x 5 slots; the oldest goes past it).</summary>
    public const int MaxWeaponDrops = 40;

    private static HashSet<string>? _startTexts;

    /// <summary>Is <paramref name="b"/> exactly one of the start builds (you get those back anyway, so they don't drop)?</summary>
    public static bool IsStartBuild(WeaponBuild b)
    {
        _startTexts ??= StandardBuilds.From("start").Select(s => s.Build().ToText()).ToHashSet();
        return _startTexts.Contains(b.ToText());
    }

    // ---- Server ---------------------------------------------------------------------------------------

    /// <summary>
    /// Server, when <paramref name="p"/> bleeds out: every built weapon in their slots (guns and the melee build, but not
    /// untouched start builds) drops at <paramref name="at"/>, spread out, for <see cref="ZombiesRules.BuildDropSeconds"/>.
    /// Returns how many dropped.
    /// </summary>
    private int DropBuildsOnBleedout(Player p, ZPlayer zp, Vector3 at, double now)
    {
        var drops = new List<(int Slot, WeaponBuild Build)>();
        for (var s = 0; s < 5; s++)
            if (zp.Slots[s] is { } b && (s is not (2 or 3) || p.ExtraSlots) && !IsStartBuild(b))
                drops.Add((s, b));
        if (drops.Count == 0)
            return 0;
        var spots = DropSpots(at, drops.Count, p.Id);
        for (var i = 0; i < drops.Count; i++)
        {
            SpawnWeaponDrop(drops[i].Build, spots[i], now + ZRules.BuildDropSeconds, p, full: true, now);
            zp.Slots[drops[i].Slot] = null; // it's on the floor now (the start gear fills the slots again next round)
        }
        return drops.Count;
    }

    /// <summary>Server: a build goes on the floor (and every client hears it).</summary>
    private ZWeaponDrop SpawnWeaponDrop(WeaponBuild b, Vector3 at, double until, Player? owner, bool full, double now, int expireRound = 0)
    {
        while (ZWeaponDrops.Count >= MaxWeaponDrops)
        {
            BroadcastWeaponDropGone(ZWeaponDrops[0].Id, byte.MaxValue);
            ZWeaponDrops.RemoveAt(0);
        }
        var d = new ZWeaponDrop
        {
            Id = _zNextWeaponDrop++, Build = b.Clone(), At = at, Until = until, Owner = owner?.Id ?? byte.MaxValue, OwnerName = owner?.Name ?? "",
            Full = full, Melee = b.IsMelee, ExpireRound = expireRound, Rarity = BuildRarity(b),
        };
        if (_zNextWeaponDrop == 0)
            _zNextWeaponDrop = 1;
        ZWeaponDrops.Add(d);
        if (!Net.IsOffline)
        {
            WriteWeaponDrop(_w, d, until - now);
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        }
        return d;
    }

    /// <summary>Server, every tick: dropped builds whose time is up despawn.</summary>
    private void TickWeaponDrops(double now)
    {
        for (var i = ZWeaponDrops.Count - 1; i >= 0; i--)
            if (now >= ZWeaponDrops[i].Until)
            {
                BroadcastWeaponDropGone(ZWeaponDrops[i].Id, byte.MaxValue);
                ZWeaponDrops.RemoveAt(i);
            }
    }

    /// <summary>
    /// Where a pickup of <paramref name="d"/> would go for <paramref name="p"/> (both sides, for the prompt and the claim):
    /// the melee slot for a melee build, else a free slot 3-4, else the slot of the gun in hand. <paramref name="replaced"/>
    /// is the build that would be swapped out onto the floor (null: a free slot, or a plain stock gun).
    /// </summary>
    public int PickupSlot(Player p, ZWeaponDrop d, out WeaponBuild? replaced)
    {
        var zp = ZP(p);
        int slot;
        if (d.Melee)
            slot = 4;
        else if (FreeGunSlot(p) is var free and >= 0)
            slot = free; // an empty slot: 1-2 taken apart at a workbench, or a free 3-4 (ZombiesDisassemble.cs)
        else
            slot = SlotOf(p, p.Weapon) is var s and >= 0 ? s : 0;
        replaced = zp.Slots[slot];
        return slot;
    }

    /// <summary>The prompt for a dropped build (both sides).</summary>
    private ZUse WeaponDropUse(Player p, ZWeaponDrop d)
    {
        var whose = d.Owner == p.Id ? "your " : d.OwnerName.Length > 0 ? $"{d.OwnerName}'s " : "the ";
        var slot = PickupSlot(p, d, out var replaced);
        string how;
        if (replaced != null)
            how = $" (swap for your {replaced.Name})";
        else if (slot == 4)
            how = " (melee)";
        else if (slot >= 2 || !HasGunIn(p, slot))
            how = $" (slot {slot + 1})";
        else
            how = $" (in place of your {p.DefFor(p.Weapon).Name})";
        return new ZUse(ZUseKind.Pickup, d.Id, 0, $"Hold [E] to pick up {whose}{d.Build.Name}{how}");
    }

    /// <summary>
    /// Server: <paramref name="p"/> picks up dropped build <paramref name="id"/>: it goes into <see cref="PickupSlot"/>. If
    /// that slot held a build, the swapped-out build drops in its place with the picked one's remaining time (never a fresh
    /// timer: swapping back and forth can't keep a build on the floor forever) and without free ammo.
    /// </summary>
    private bool ClaimWeaponDrop(Player p, int id, double now)
    {
        var i = ZWeaponDrops.FindIndex(x => x.Id == id);
        if (i < 0 || !p.Alive || IsDowned(p))
            return false;
        var d = ZWeaponDrops[i];
        var slot = PickupSlot(p, d, out var replaced);
        if (slot is 2 or 3 && !p.ExtraSlots)
            return false;
        ZWeaponDrops.RemoveAt(i);
        BroadcastWeaponDropGone(d.Id, p.Id);
        if (!GiveBuild(p, slot, d.Build, full: d.Full))
        {
            ZWeaponDrops.Insert(i, d); // shouldn't happen (a broken build): put it back
            if (!Net.IsOffline)
            {
                WriteWeaponDrop(_w, d, d.Until - now);
                Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
            }
            return false;
        }
        if (replaced != null)
            SpawnWeaponDrop(replaced, d.At, d.Until, p, full: false, now, d.ExpireRound); // (the same time left and the same last round)
        var whose = d.Owner == p.Id ? "your" : d.OwnerName.Length > 0 ? $"{d.OwnerName}'s" : "the";
        ZFeedTo(p, $"Picked up {whose} {d.Build.Name}{(replaced != null ? $", left your {replaced.Name} there" : "")}.", Sfx.LevelUp);
        if (d.Owner != p.Id && Players.TryGetValue(d.Owner, out var owner))
            ZFeedTo(owner, $"{p.Name} picked up your {d.Build.Name}.", Sfx.HitTick);
        return true;
    }

    /// <summary>
    /// <paramref name="n"/> spots around <paramref name="at"/> for dropped builds (deterministic): on rings of 0.7, 0.35 and
    /// 1.05 m, each one where a player could stand (not in a wall, inside the map, on the ground at about the same height,
    /// in straight walking reach of <paramref name="at"/>), at least 0.45 m apart. Short of spots, the rest go to
    /// <paramref name="at"/> itself (or the nearest nav node if even that is blocked).
    /// </summary>
    public static List<Vector3> DropSpots(Vector3 at, int n, int salt = 0)
    {
        var map = World.Map;
        var spots = new List<Vector3>(n);
        var area = map.Zombies?.Areas.FindIndex(a => a.Contains(at)) ?? -1;
        bool Ok(Vector3 c, out Vector3 placed)
        {
            placed = c;
            var clamped = World.ClampToArena(c);
            if (MathF.Abs(clamped.X - c.X) > 0.01f || MathF.Abs(clamped.Z - c.Z) > 0.01f)
                return false; // outside the map (or its outline)
            if (area >= 0 && !map.Zombies!.Areas[area].Contains(c))
                return false; // stays in the area they went down in (not through a window or a shut door)
            var top = c.Y + 0.9f;
            var hit = World.RaycastWorld(new Vector3(c.X, top, c.Z), -Vector3.UnitY, 2.5f);
            float ground;
            if (hit < 2.5f)
                ground = top - hit;
            else if (map.TerrainCollision is { } terrain)
                ground = terrain.HeightAt(c.X, c.Z);
            else if (top - 2.5f < 0)
                ground = 0; // the flat floor at 0
            else
                return false; // a hole
            if (MathF.Abs(ground - at.Y) > 0.6f)
                return false; // a ledge or a drop
            placed = new Vector3(c.X, ground, c.Z);
            if (World.Blocked(placed + new Vector3(0, 0.02f, 0)) || !World.CanWalkStraight(at, placed))
                return false; // in a wall, or across one
            foreach (var s in spots)
                if (Flat(s - placed) < 0.45f)
                    return false;
            return true;
        }
        var start = (salt % 12) * MathF.Tau / 12f;
        foreach (var r in new[] { 0.7f, 0.35f, 1.05f })
            for (var k = 0; k < 12 && spots.Count < n; k++)
            {
                var a = start + k * MathF.Tau / 12f + (r < 0.5f ? MathF.PI / 12 : 0);
                if (Ok(at + new Vector3(MathF.Sin(a) * r, 0, MathF.Cos(a) * r), out var placed))
                    spots.Add(placed);
            }
        if (spots.Count < n)
        {
            var fallback = at;
            if (World.Blocked(at + new Vector3(0, 0.02f, 0)) && NavGraph.Current is { Nodes.Length: > 0 } nav && nav.Nearest(at, needSight: false) is var node and >= 0)
                fallback = nav.Nodes[node];
            while (spots.Count < n)
                spots.Add(fallback);
        }
        return spots;
    }

    // ---- Network ----------------------------------------------------------------------------------------

    /// <summary>A dropped build's rarity: its rarest part.</summary>
    public static Rarity BuildRarity(WeaponBuild b)
    {
        var r = Rarity.Common;
        foreach (var n in b.Nodes)
            if (n.Part.Rarity > r)
                r = n.Part.Rarity;
        return r;
    }

    /// <summary>
    /// A dropped build appeared: id, where, seconds left, the owner, flags (1 full ammo, 2 melee), the owner's name, the
    /// build's text, and (protocol 69) the round it lasts to (0: time only).
    /// </summary>
    public static void WriteWeaponDrop(PacketWriter w, ZWeaponDrop d, double secondsLeft)
    {
        var bytes = Encoding.UTF8.GetBytes(d.Build.ToText());
        var n = Math.Min(bytes.Length, 6000);
        w.Begin(Msg.ZPickup).U8(0).U16(d.Id).F32(d.At.X).F32(d.At.Y).F32(d.At.Z).F32((float)Math.Max(0, secondsLeft)).U8(d.Owner)
            .U8((byte)((d.Full ? 1 : 0) | (d.Melee ? 2 : 0))).Str(d.OwnerName.Length > 32 ? d.OwnerName[..32] : d.OwnerName).U16((ushort)n).Bytes(bytes.AsSpan(0, n))
            .U16((ushort)Math.Clamp(d.ExpireRound, 0, ushort.MaxValue));
    }

    /// <summary>A dropped build is gone: picked up by player <paramref name="by"/>, or despawned (255).</summary>
    public static void WriteWeaponDropGone(PacketWriter w, ushort id, byte by) => w.Begin(Msg.ZPickup).U8(1).U16(id).U8(by);

    private void BroadcastWeaponDropGone(ushort id, byte by)
    {
        if (Net.IsOffline)
            return;
        WriteWeaponDropGone(_w, id, by);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: every dropped build to a joiner.</summary>
    private void SendWeaponDropsTo(RakNetGUID guid, double now)
    {
        foreach (var d in ZWeaponDrops)
        {
            WriteWeaponDrop(_w, d, d.Until - now);
            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client: a <see cref="Msg.ZPickup"/> message.</summary>
    private void ReadWeaponDrop(ref PacketReader r, double now)
    {
        if (!r.Has(3))
            return;
        var kind = r.U8();
        var id = r.U16();
        if (kind == 1)
        {
            ZWeaponDrops.RemoveAll(x => x.Id == id);
            return;
        }
        if (kind != 0 || !r.Has(20))
            return;
        var at = new Vector3(r.F32(), r.F32(), r.F32());
        var left = r.F32();
        var owner = r.U8();
        var flags = r.U8();
        var name = r.Text(32);
        var text = ReadLong(ref r);
        int expire = r.Has(2) ? r.U16() : 0;
        if (!float.IsFinite(at.X + at.Y + at.Z + left))
            return; // garbage
        WeaponBuild b;
        try
        {
            b = WeaponBuild.Parse(text, Parts.Find);
        }
        catch (FormatException)
        {
            return;
        }
        ZWeaponDrops.RemoveAll(x => x.Id == id);
        ZWeaponDrops.Add(new ZWeaponDrop { Id = id, Build = b, At = at, Until = now + left, Owner = owner, OwnerName = name, Full = (flags & 1) != 0, Melee = (flags & 2) != 0,
            ExpireRound = expire, Rarity = BuildRarity(b) });
    }

    // ---- Self-test hooks ----------------------------------------------------------------------------------

    /// <summary>Self-test: a client's copy reads a <see cref="Msg.ZPickup"/> message.</summary>
    public void SelfTestReadWeaponDrop(ReadOnlySpan<byte> msg, double now)
    {
        var r = new PacketReader(msg[1..]);
        ReadWeaponDrop(ref r, now);
    }

    /// <summary>Self-test: a build drops at <paramref name="at"/> as if <paramref name="owner"/> had bled out there.</summary>
    public ZWeaponDrop SelfTestSpawnWeaponDrop(WeaponBuild b, Vector3 at, double now, Player? owner) => SpawnWeaponDrop(b, at, now + ZRules.BuildDropSeconds, owner, full: true, now);
}
