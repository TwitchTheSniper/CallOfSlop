using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Loose parts a player dropped on purpose (docs/zombies/ZOMBIES-MODE.md, "Dropping things"): a small labelled pickup
/// with its rarity glow. Anyone on the team picks it up with the interact key (into their loose parts). Server-owned;
/// clients get it in <see cref="Msg.ZPartDrop"/>.
/// </summary>
public sealed class ZPartDrop
{
    public ushort Id;

    /// <summary>The part (resolved on both sides; a pickup of a part this PC doesn't know is never made).</summary>
    public PartDef Part = null!;

    /// <summary>How many of that part it holds (1 to <see cref="ZombiesRules.MaxPartsPerDrop"/>).</summary>
    public int Count = 1;

    public Vector3 At;

    /// <summary>When it despawns (the game's clock), and the round whose end takes it too (0: time only).</summary>
    public double Until;

    public int ExpireRound;

    /// <summary>Who dropped it (the player id and the name, kept after they leave).</summary>
    public byte Owner = byte.MaxValue;

    public string OwnerName = "";
}

/// <summary>
/// Dropping things on purpose in Zombies (board item "drop items", 2026-10-27): the drop key puts the gun in your hands
/// on the floor as a dropped build (<see cref="ZWeaponDrop"/>: its own model, anyone picks it up with the interact key,
/// swapping if their slots are full), and the parts inventory (the inventory key, or the workbench's loose parts) puts
/// loose parts down as <see cref="ZPartDrop"/>s. Server-authoritative: a client asks with <see cref="Msg.ZDropItem"/>;
/// the server checks it, takes the item from the dropper in the same step as it makes the pickup (nothing can be in two
/// places), and the first claim of a pickup wins (it is gone for the next one in the same tick). You can't drop your last
/// gun. Dropped things last to the end of the next round or <see cref="ZombiesRules.PlayerDropSeconds"/>, whichever is
/// first, and a map holds at most <see cref="MaxWeaponDrops"/> builds and <see cref="ZombiesRules.MaxPartDrops"/> part
/// pickups (past that a drop is refused, so nothing is lost). Co-op bots never pick anything up.
/// </summary>
public sealed partial class Game
{
    /// <summary>Loose-part pickups on the floor (both sides).</summary>
    public readonly List<ZPartDrop> ZPartDrops = new();

    private ushort _zNextPartDrop = 1;

    /// <summary><see cref="Msg.ZDropItem"/> kinds: the gun in your hands, or loose parts.</summary>
    public const byte DropGun = 0, DropParts = 1;

    /// <summary>How far a pickup counts as "looked at" (metres), and the cone (cosine of the half angle).</summary>
    public const float LookRange = 6f, LookCos = 0.97f;

    // ---- Both sides ---------------------------------------------------------------------------------------

    /// <summary>
    /// Why <paramref name="p"/> can't drop the gun in their hands now, or null (both sides: the client says so at once,
    /// the server checks again). Only a build can be dropped (not empty hands), and never your last gun.
    /// </summary>
    public string? DropGunRefusal(Player p)
    {
        if (Mode != GameMode.Zombies || ZMap == null || Phase != MatchPhase.Playing)
            return "Nothing to drop here.";
        if (!p.Alive || IsDowned(p))
            return "Not while you're down.";
        var slot = SlotOf(p, p.Weapon);
        if (slot < 0 || !HasGunIn(p, slot))
            return "There's no gun in your hands to drop.";
        if (ZP(p).Slots[slot] == null)
            return "That gun can't be dropped.";
        var others = 0;
        for (var s = 0; s < 4; s++)
            if (s != slot && HasGunIn(p, s))
                others++;
        if (others == 0)
            return "You can't drop your last gun.";
        if (ZWeaponDrops.Count >= MaxWeaponDrops)
            return "Too many guns on the floor: pick some up first.";
        return null;
    }

    /// <summary>The round whose end takes a drop made now (0: no round limit).</summary>
    private int DropExpireRound() => ZRules.PlayerDropRounds > 0 ? ZRound + ZRules.PlayerDropRounds : 0;

    /// <summary>
    /// Is <paramref name="p"/> looking at <paramref name="at"/> (a pickup on the floor; its middle is a little above)?
    /// Used to pick which pickup the interact key takes, and which one the HUD names.
    /// </summary>
    public static bool LooksAt(Player p, Vector3 at)
    {
        var to = at + new Vector3(0, 0.3f, 0) - p.Eye;
        var len = to.Length();
        if (len < 0.01f)
            return true;
        if (len > LookRange)
            return false;
        return Vector3.Dot(to / len, World.Forward(p.Yaw, p.Pitch)) >= LookCos;
    }

    /// <summary>The distance <see cref="FindZUse"/> ranks a pickup by: one looked at comes first in a pile.</summary>
    private static float PickupRank(Player p, Vector3 at, float d) => LooksAt(p, at) ? d * 0.25f : d;

    /// <summary>The prompt for a part pickup (both sides).</summary>
    private static ZUse PartDropUse(Player p, ZPartDrop d)
    {
        var whose = d.Owner == p.Id ? "your " : d.OwnerName.Length > 0 ? $"{d.OwnerName}'s " : "the ";
        return new ZUse(ZUseKind.PartPickup, d.Id, 0, $"Hold [E] to pick up {whose}{d.Part.Name} x{d.Count} ({PartDef.RarityName(d.Part.Rarity)})");
    }

    // ---- The local player ---------------------------------------------------------------------------------

    /// <summary>
    /// Local: drop something (<see cref="DropGun"/>: the gun in your hands; <see cref="DropParts"/>: <paramref name="count"/>
    /// of loose part <paramref name="part"/>). The host does it at once; a client asks the server. Returns why not when the
    /// local check already says no (the server checks again either way), else null.
    /// </summary>
    public string? DropItemRequest(int kind, string part = "", int count = 1)
    {
        if (Local is not { } me || Mode != GameMode.Zombies)
            return "Nothing to drop here.";
        if (kind == DropGun && DropGunRefusal(me) is { } why)
        {
            LoadoutNotice = why;
            return why;
        }
        if (IsServer)
        {
            var refused = ServerDropItem(me, kind, part, count, _zNow);
            if (refused != null)
                LoadoutNotice = refused;
            return refused;
        }
        WriteDropRequest(_w, kind, part, count);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        return null;
    }

    /// <summary>A client's <see cref="Msg.ZDropItem"/>: the kind, how many, the part's name (empty for a gun).</summary>
    public static void WriteDropRequest(PacketWriter w, int kind, string part, int count) =>
        w.Begin(Msg.ZDropItem).U8((byte)kind).U8((byte)Math.Clamp(count, 0, 255)).Str(part.Length > 64 ? part[..64] : part);

    // ---- Server -------------------------------------------------------------------------------------------

    /// <summary>
    /// Server: <paramref name="p"/> drops something (see <see cref="DropItemRequest"/>). The item leaves the player in the
    /// same step as the pickup appears. Returns why not, or null.
    /// </summary>
    public string? ServerDropItem(Player p, int kind, string part, int count, double now)
    {
        if (ZMap == null || Phase != MatchPhase.Playing || !p.Alive || IsDowned(p))
            return "Not now.";
        var zp = ZP(p);
        var at = DropSpotFor(p);
        if (kind == DropGun)
        {
            if (DropGunRefusal(p) is { } why)
                return why;
            var slot = SlotOf(p, p.Weapon);
            var build = zp.Slots[slot]!;
            ClearSlot(p, slot); // out of your hands (the next gun comes up) ...
            SpawnWeaponDrop(build, at, now + ZRules.PlayerDropSeconds, p, full: false, now, DropExpireRound()); // ... and onto the floor
            ZFeedTo(p, $"Dropped your {build.Name}.", Sfx.HitTick);
            return null;
        }
        if (kind != DropParts)
            return "You can't drop that.";
        if (Parts.Find(part) is not { } def)
            return "There's no such part.";
        var have = zp.Parts.GetValueOrDefault(def.Name);
        if (count < 1)
            return "Pick how many to drop.";
        if (count > ZRules.MaxPartsPerDrop)
            return $"At most {ZRules.MaxPartsPerDrop} at a time.";
        if (have < count)
            return have == 0 ? $"You have no {def.Name}." : $"You only have {have} {def.Name}.";
        if (ZPartDrops.Count >= ZRules.MaxPartDrops)
            return "Too many parts on the floor: pick some up first.";
        if (have == count)
            zp.Parts.Remove(def.Name);
        else
            zp.Parts[def.Name] = have - count;
        SendZRun(p);
        SpawnPartDrop(def, count, at, now + ZRules.PlayerDropSeconds, DropExpireRound(), p, now);
        ZFeedTo(p, $"Dropped {count} {def.Name}.", Sfx.HitTick);
        return null;
    }

    /// <summary>
    /// Where <paramref name="p"/>'s drop lands: about 0.7 m in front of their feet (on the ground below them if they're
    /// in the air), placed by <see cref="DropSpots"/> (never in a wall, in reach, in the same area).
    /// </summary>
    private static Vector3 DropSpotFor(Player p)
    {
        var feet = p.Position;
        var down = World.RaycastWorld(feet + new Vector3(0, 0.3f, 0), -Vector3.UnitY, 4f);
        if (down < 4f)
            feet.Y = feet.Y + 0.3f - down;
        // DropSpots' first spot is at angle (salt % 12) / 12 of a turn, offset (sin a, cos a): in front is a = pi - yaw.
        var k = (int)MathF.Round((MathF.PI - p.Yaw) / (MathF.Tau / 12));
        return DropSpots(feet, 1, ((k % 12) + 12) % 12)[0];
    }

    /// <summary>Server: a part pickup goes on the floor (and every client hears it).</summary>
    private ZPartDrop SpawnPartDrop(PartDef part, int count, Vector3 at, double until, int expireRound, Player? owner, double now)
    {
        var d = new ZPartDrop
        {
            Id = _zNextPartDrop++, Part = part, Count = count, At = at, Until = until, ExpireRound = expireRound,
            Owner = owner?.Id ?? byte.MaxValue, OwnerName = owner?.Name ?? "",
        };
        if (_zNextPartDrop == 0)
            _zNextPartDrop = 1;
        ZPartDrops.Add(d);
        if (!Net.IsOffline)
        {
            WritePartDrop(_w, d, until - now);
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        }
        return d;
    }

    /// <summary>Server, every tick: part pickups whose time is up despawn.</summary>
    private void TickPartDrops(double now)
    {
        for (var i = ZPartDrops.Count - 1; i >= 0; i--)
            if (now >= ZPartDrops[i].Until)
            {
                BroadcastPartDropGone(ZPartDrops[i].Id, byte.MaxValue);
                ZPartDrops.RemoveAt(i);
            }
    }

    /// <summary>Server, when a round ends: things dropped on purpose whose last round this was despawn.</summary>
    private void ExpireDropsAtRoundEnd(int round)
    {
        for (var i = ZPartDrops.Count - 1; i >= 0; i--)
            if (ZPartDrops[i].ExpireRound > 0 && round >= ZPartDrops[i].ExpireRound)
            {
                BroadcastPartDropGone(ZPartDrops[i].Id, byte.MaxValue);
                ZPartDrops.RemoveAt(i);
            }
        for (var i = ZWeaponDrops.Count - 1; i >= 0; i--)
            if (ZWeaponDrops[i].ExpireRound > 0 && round >= ZWeaponDrops[i].ExpireRound)
            {
                BroadcastWeaponDropGone(ZWeaponDrops[i].Id, byte.MaxValue);
                ZWeaponDrops.RemoveAt(i);
            }
    }

    /// <summary>
    /// Server: <paramref name="p"/> picks up part pickup <paramref name="id"/> into their loose parts. The first claim
    /// wins: the pickup is removed before anything is given, so a second claim (the same tick or later) finds nothing.
    /// Co-op bots never pick up.
    /// </summary>
    private bool ClaimPartDrop(Player p, int id)
    {
        var i = ZPartDrops.FindIndex(x => x.Id == id);
        if (i < 0 || !p.Alive || IsDowned(p) || p.Bot != null)
            return false;
        var d = ZPartDrops[i];
        ZPartDrops.RemoveAt(i);
        BroadcastPartDropGone(d.Id, p.Id);
        var zp = ZP(p);
        zp.Parts[d.Part.Name] = zp.Parts.GetValueOrDefault(d.Part.Name) + d.Count;
        SendZRun(p);
        var whose = d.Owner == p.Id ? "your" : d.OwnerName.Length > 0 ? $"{d.OwnerName}'s" : "the";
        ZFeedTo(p, $"Picked up {whose} {d.Part.Name} x{d.Count} ({PartDef.RarityName(d.Part.Rarity)}).", Sfx.HitTick);
        if (d.Owner != p.Id && Players.TryGetValue(d.Owner, out var owner))
            ZFeedTo(owner, $"{p.Name} picked up your {d.Part.Name} x{d.Count}.", Sfx.HitTick);
        return true;
    }

    /// <summary>Server: a <see cref="Msg.ZDropItem"/> from <paramref name="p"/> (after the message id); returns why not, or null.</summary>
    private string? OnDropRequest(Player p, ref PacketReader r, double now)
    {
        var kind = r.U8();
        var count = r.U8();
        var part = r.Has(1) ? r.Text(64) : "";
        return ServerDropItem(p, kind, part, count, now);
    }

    // ---- Network ----------------------------------------------------------------------------------------

    /// <summary>A part pickup appeared: id, where, seconds left, its last round, the owner, how many, the owner's name, the part.</summary>
    public static void WritePartDrop(PacketWriter w, ZPartDrop d, double secondsLeft) =>
        w.Begin(Msg.ZPartDrop).U8(0).U16(d.Id).F32(d.At.X).F32(d.At.Y).F32(d.At.Z).F32((float)Math.Max(0, secondsLeft))
            .U16((ushort)Math.Clamp(d.ExpireRound, 0, ushort.MaxValue)).U8(d.Owner).U8((byte)Math.Clamp(d.Count, 1, 255))
            .Str(d.OwnerName.Length > 32 ? d.OwnerName[..32] : d.OwnerName).Str(d.Part.Name);

    /// <summary>A part pickup is gone: picked up by player <paramref name="by"/>, or despawned (255).</summary>
    public static void WritePartDropGone(PacketWriter w, ushort id, byte by) => w.Begin(Msg.ZPartDrop).U8(1).U16(id).U8(by);

    private void BroadcastPartDropGone(ushort id, byte by)
    {
        if (Net.IsOffline)
            return;
        WritePartDropGone(_w, id, by);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: every part pickup to a joiner.</summary>
    private void SendPartDropsTo(RakNetGUID guid, double now)
    {
        foreach (var d in ZPartDrops)
        {
            WritePartDrop(_w, d, d.Until - now);
            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client: a <see cref="Msg.ZPartDrop"/> message.</summary>
    private void ReadPartDrop(ref PacketReader r, double now)
    {
        if (!r.Has(3))
            return;
        var kind = r.U8();
        var id = r.U16();
        if (kind == 1)
        {
            for (var i = ZPartDrops.Count - 1; i >= 0; i--)
                if (ZPartDrops[i].Id == id)
                    ZPartDrops.RemoveAt(i);
            return;
        }
        if (kind != 0 || !r.Has(22))
            return;
        var at = new Vector3(r.F32(), r.F32(), r.F32());
        var left = r.F32();
        int expire = r.U16();
        var owner = r.U8();
        int count = r.U8();
        var name = r.Text(32);
        var partName = r.Has(1) ? r.Text(64) : "";
        if (!float.IsFinite(at.X + at.Y + at.Z + left) || count < 1 || Parts.Find(partName) is not { } part)
            return; // garbage, or a part this PC doesn't have
        for (var i = ZPartDrops.Count - 1; i >= 0; i--)
            if (ZPartDrops[i].Id == id)
                ZPartDrops.RemoveAt(i);
        ZPartDrops.Add(new ZPartDrop { Id = id, Part = part, Count = count, At = at, Until = now + left, ExpireRound = expire, Owner = owner, OwnerName = name });
    }

    // ---- Self-test hooks ----------------------------------------------------------------------------------

    /// <summary>Self-test: the server receives <paramref name="msg"/> (a whole <see cref="Msg.ZDropItem"/>) from player <paramref name="p"/>.</summary>
    public string? SelfTestDropPacket(Player p, ReadOnlySpan<byte> msg, double now)
    {
        var r = new PacketReader(msg[1..]);
        return OnDropRequest(p, ref r, now);
    }

    /// <summary>Self-test: a client's copy reads a <see cref="Msg.ZPartDrop"/> message.</summary>
    public void SelfTestReadPartDrop(ReadOnlySpan<byte> msg, double now)
    {
        var r = new PacketReader(msg[1..]);
        ReadPartDrop(ref r, now);
    }

    /// <summary>Self-test: <paramref name="p"/> claims pickup <paramref name="id"/> directly (a part pickup, or a dropped build with <paramref name="gun"/>).</summary>
    public bool SelfTestClaim(Player p, int id, bool gun, double now) => gun ? ClaimWeaponDrop(p, id, now) : ClaimPartDrop(p, id);
}
