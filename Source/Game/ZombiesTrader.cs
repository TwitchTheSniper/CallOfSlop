using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a thing on the trader's shelf is.</summary>
public enum ZTraderItemKind : byte
{
    /// <summary>A loose part (<see cref="ZTraderItem.Part"/>), one of them; it goes to your loose parts.</summary>
    Part,

    /// <summary>A grenade refill: the round's count of your Zombies grenade (never more than you'd get at a round's start).</summary>
    Grenades,

    /// <summary>A full reserve for every gun in your slots (what Max Ammo gives).</summary>
    Ammo,
}

/// <summary>One thing on the shelf.</summary>
public struct ZTraderItem
{
    public ZTraderItemKind Kind;

    /// <summary>The part (an index into <see cref="Parts.All"/>), or -1.</summary>
    public short Part;

    public int Price;

    /// <summary>How many are left (parts: 1 at first; consumables: <see cref="Unlimited"/>).</summary>
    public byte Left;

    public const byte Unlimited = 255;

    public readonly bool SoldOut => Left == 0;
}

/// <summary>The trader as both sides see him (server-owned, mirrored in <see cref="Msg.ZTrader"/>).</summary>
public sealed class ZTrader
{
    /// <summary>He's on the map now.</summary>
    public bool Here;

    /// <summary>Counts visits (wrapping; 0 before the first), so a purchase names the shelf it was made from and clients see a new visit.</summary>
    public byte Visit;

    /// <summary>Where he stands and which way he faces (the spot's, or in front of the box spot / workbench he stands in for).</summary>
    public Vector3 At;

    public float Yaw;

    /// <summary>The round whose start sends him away.</summary>
    public int LeaveRound;

    /// <summary>The round he came in (the shelf's prices and tiers are that round's).</summary>
    public int CameRound;

    public readonly ZTraderItem[] Stock = new ZTraderItem[ZombiesRules.MaxTraderStock];

    public int Count;

    /// <summary>Client: when the last message that changed him came (the HUD's arrival flash).</summary>
    public double ChangedAt = -1e9;

    public void Reset()
    {
        Here = false;
        Visit = 0;
        At = Vector3.Zero;
        Yaw = 0;
        LeaveRound = 0;
        CameRound = 0;
        Count = 0;
        Array.Clear(Stock);
        ChangedAt = -1e9;
    }
}

/// <summary>
/// The trader (board item dev-zombies-trader-tr01, 2026-10-28; docs/zombies/ZOMBIES-MODE.md, "The trader"): a wandering
/// parts dealer who sets up his stall at the start of every <see cref="ZombiesRules.TraderEveryRounds"/>th round at one
/// of the map's trader spots (a box spot or workbench on maps without any), announced to everyone (a feed line, a sound, a
/// banner and a waypoint label through the walls), stays <see cref="ZombiesRules.TraderStayRounds"/> rounds and their
/// breaks, then packs up when the next round begins. The interact key opens his shelf: a shared, limited stock of parts
/// (first come, first served; a fresh shelf every visit) from the rarity tiers below what the round's drop tables favour,
/// plus a grenade refill and a full reserve, priced by rarity and round (<see cref="ZombiesRules.TraderPrice"/>).
/// Server-authoritative: a purchase is a <see cref="Msg.ZTraderBuy"/>, the server checks the visit, the shelf, the range
/// and the points, and sends the shelf again in <see cref="Msg.ZTrader"/>. He isn't a body: zombies take no notice of
/// him and nothing can hurt him. Nothing here allocates per tick.
/// </summary>
public sealed partial class Game
{
    public readonly ZTrader ZTrader = new();

    private void ResetTrader() => ZTrader.Reset();

    /// <summary>Server, at the start of every round: he leaves when his time is up, and comes on his rounds (a fresh shelf).</summary>
    private void TraderOnRound(ZombiesMap zm, int round, double now)
    {
        if (ZTrader.Here && round >= ZTrader.LeaveRound)
        {
            ZTrader.Here = false;
            ZTrader.Count = 0;
            ModeEvent("The trader packed up his stall and left. He'll be back.", Sfx.ReconBeep);
            BroadcastTrader();
        }
        if (!ZRules.TraderComes(round))
            return;
        if (!TraderSpotFor(zm, out var at, out var yaw, out var where))
            return;
        var t = ZTrader;
        t.Here = true;
        t.Visit = (byte)Math.Max(1, (t.Visit + 1) & 255);
        t.At = at;
        t.Yaw = yaw;
        t.CameRound = round;
        t.LeaveRound = round + Math.Max(1, ZRules.TraderStayRounds);
        StockTrader(round);
        ModeEvent($"The trader has set up his stall {where}: {t.Count - TraderConsumables()} parts up to {PartDef.RarityName(ZRules.TraderTopTier(RunTables.Current, round))}. He leaves when round {t.LeaveRound} starts.", Sfx.StreakReady);
        ZBanner("THE TRADER IS HERE");
        Audio.Effect3D(Sfx.LevelUp, at + new Vector3(0, 1.4f, 0), 0.8f, MixBus.Zombies);
        BroadcastTrader();
    }

    /// <summary>How many consumables the shelf carries (from the rules: each with a price above 0).</summary>
    private int TraderConsumables() => (ZRules.TraderGrenadePrice > 0 ? 1 : 0) + (ZRules.TraderAmmoPrice > 0 ? 1 : 0);

    /// <summary>
    /// Server: a spot for this visit: one of the map's trader spots at random (an open area's first), else in front of a
    /// box spot or workbench. False when the map has nowhere at all.
    /// </summary>
    private bool TraderSpotFor(ZombiesMap zm, out Vector3 at, out float yaw, out string where)
    {
        at = Vector3.Zero;
        yaw = 0;
        where = "";
        var own = zm.TraderSpots.Count > 0;
        var n = own ? zm.TraderSpots.Count : zm.BoxSpots.Count + zm.Benches.Count;
        if (n == 0)
            return false;
        // Open areas first: count them, pick among them; none open (odd data): any.
        var open = 0;
        for (var i = 0; i < n; i++)
            if (AreaOpen(TraderCandidate(zm, own, i).Area))
                open++;
        var pick = ZRange(open > 0 ? open : n);
        var k = 0;
        for (var i = 0; i < n; i++)
        {
            var spot = TraderCandidate(zm, own, i);
            if (open > 0 && !AreaOpen(spot.Area))
                continue;
            if (k++ != pick)
                continue;
            yaw = spot.Yaw;
            at = own ? spot.At : spot.At + World.FlatForward(spot.Yaw) * 1.4f; // (standing in for a machine: in front of it, its back to it)
            var area = zm.Areas[Math.Clamp(spot.Area, 0, zm.Areas.Count - 1)].Name;
            where = own ? $"in {area}" : i < zm.BoxSpots.Count ? $"by the box spot in {area}" : $"by the workbench in {area}";
            return true;
        }
        return false;
    }

    private static ZSpot TraderCandidate(ZombiesMap zm, bool own, int i) =>
        own ? zm.TraderSpots[i] : i < zm.BoxSpots.Count ? zm.BoxSpots[i] : zm.Benches[i - zm.BoxSpots.Count];

    /// <summary>
    /// Server: a fresh shelf for <paramref name="round"/>: the parts first (the rarest tier first, then round and round
    /// down to common; a random category each, no part twice), then the consumables. Deterministic from the run's seed.
    /// </summary>
    private void StockTrader(int round)
    {
        var t = ZTrader;
        var top = (int)ZRules.TraderTopTier(RunTables.Current, round);
        var parts = Math.Clamp(ZRules.TraderStock, 0, ZombiesRules.MaxTraderStock - TraderConsumables());
        t.Count = 0;
        Array.Clear(t.Stock);
        var cats = ZombiesMap.SlotCategories;
        for (var i = 0; i < parts; i++)
        {
            var tier = (Rarity)(top - i % (top + 1));
            short part = -1;
            for (var tries = 0; tries < 6 && part < 0; tries++)
            {
                var cat = cats[ZRange(cats.Length)];
                var cand = PickSlotPart(tier, ZNext(), cat, jackpot: false);
                if (cand < 0)
                    cand = PickSlotPart(tier, ZNext());
                if (cand >= 0 && Parts.All[cand].Rarity == tier && !TraderHas(cand))
                    part = cand;
            }
            if (part < 0)
                continue; // (a modded part set without that tier in any category: the shelf is a little emptier)
            t.Stock[t.Count++] = new ZTraderItem { Kind = ZTraderItemKind.Part, Part = part, Price = ZRules.TraderPrice(tier, round), Left = 1 };
        }
        if (ZRules.TraderGrenadePrice > 0 && t.Count < t.Stock.Length)
            t.Stock[t.Count++] = new ZTraderItem { Kind = ZTraderItemKind.Grenades, Part = -1, Price = ZRules.TraderMarkUp(ZRules.TraderGrenadePrice, round), Left = ZTraderItem.Unlimited };
        if (ZRules.TraderAmmoPrice > 0 && t.Count < t.Stock.Length)
            t.Stock[t.Count++] = new ZTraderItem { Kind = ZTraderItemKind.Ammo, Part = -1, Price = ZRules.TraderMarkUp(ZRules.TraderAmmoPrice, round), Left = ZTraderItem.Unlimited };
    }

    private bool TraderHas(short part)
    {
        for (var i = 0; i < ZTrader.Count; i++)
            if (ZTrader.Stock[i].Kind == ZTraderItemKind.Part && ZTrader.Stock[i].Part == part)
                return true;
        return false;
    }

    /// <summary>The shelf item's name for the panel and the feed.</summary>
    public static string TraderItemName(in ZTraderItem item) => item.Kind switch
    {
        ZTraderItemKind.Part when item.Part >= 0 && item.Part < Parts.All.Length => Parts.All[item.Part].Name,
        ZTraderItemKind.Grenades => "Grenade refill",
        ZTraderItemKind.Ammo => "Full reserve (every gun)",
        _ => "?",
    };

    /// <summary>Is <paramref name="p"/> near enough the trader to shop (the rules' range, a little slack for a moving client)?</summary>
    public bool AtTrader(Player p) => ZTrader.Here && Flat(ZTrader.At - p.Position) <= ZRules.TraderUseRange + 0.4f && MathF.Abs(ZTrader.At.Y - p.Position.Y) < 2.5f;

    /// <summary>The interact prompt at the trader (FindZUse).</summary>
    private ZUse TraderUse() => new(ZUseKind.Trader, 0, 0, "Press [E] to see what the trader has");

    /// <summary>
    /// Server: <paramref name="p"/> buys shelf item <paramref name="item"/> of visit <paramref name="visit"/>. Returns why
    /// not (he's gone, an old shelf, sold out, too far, too poor, nothing to refill), or null when it went through: the
    /// points are taken, the part goes to the loose parts (or the grenades / reserve are filled) and everyone gets the shelf.
    /// </summary>
    public string? ServerTraderBuy(Player p, int item, int visit, double now)
    {
        var t = ZTrader;
        if (ZMap == null || !t.Here)
            return "The trader isn't here.";
        if (visit != t.Visit)
            return "That shelf is gone: the trader has a new one.";
        if (item < 0 || item >= t.Count)
            return "The trader doesn't have that.";
        if (!p.Alive || IsDowned(p))
            return "You can't trade now.";
        if (!AtTrader(p))
            return "You're not at the trader.";
        ref var it = ref t.Stock[item];
        if (it.SoldOut)
            return $"Sold out: {TraderItemName(it)}.";
        var zp = ZP(p);
        if (zp.Points < it.Price)
            return $"Not enough points for {TraderItemName(it)} ({zp.Points} of {it.Price}).";
        switch (it.Kind)
        {
            case ZTraderItemKind.Part when it.Part >= 0 && it.Part < Parts.All.Length:
            {
                var part = Parts.All[it.Part];
                zp.Points -= it.Price;
                it.Left--;
                zp.Parts[part.Name] = zp.Parts.GetValueOrDefault(part.Name) + 1;
                SendZRun(p);
                ZFeedTo(p, $"Bought {part.Name} ({PartDef.RarityName(part.Rarity)}) from the trader for {it.Price}. It's in your loose parts.", Sfx.LevelUp);
                break;
            }
            case ZTraderItemKind.Grenades:
            {
                var want = Math.Clamp(ZRules.GrenadesForRound(Math.Max(1, ZRound)) + (p.GrenadeBuild?.Extra ?? 0), 0, 255);
                if (p.GrenadesLeft >= want)
                    return "You have all your grenades already.";
                zp.Points -= it.Price;
                GiveZombiesGrenades(p, ZRules.GrenadesForRound(Math.Max(1, ZRound)), topUp: true);
                ZFeedTo(p, $"The trader topped up your grenades ({p.GrenadesLeft}) for {it.Price}.", Sfx.HitTick);
                break;
            }
            case ZTraderItemKind.Ammo:
                zp.Points -= it.Price;
                MaxAmmoFor(p); // (ZombiesPowerUps.cs: a full reserve in every slot)
                ZFeedTo(p, $"The trader filled your reserves for {it.Price}.", Sfx.HitTick);
                break;
            default:
                return "The trader doesn't have that.";
        }
        Audio.Effect3D(Sfx.HitTick, t.At + new Vector3(0, 1.2f, 0), 0.5f, MixBus.Zombies);
        BroadcastTrader();
        return null;
    }

    // ---- Co-op bots (ZombieBotsShop.cs) --------------------------------------------------------------------

    /// <summary>
    /// Server: the shelf item a bot would buy with <paramref name="spare"/> points (its points less the bot reserve): the
    /// rarest part it can afford with <see cref="ZombiesRules.TraderBotSurplus"/> to spare, or -1.
    /// </summary>
    private int BotTraderPick(int spare)
    {
        if (!ZTrader.Here || ZRules.TraderBotSurplus <= 0)
            return -1;
        var best = -1;
        for (var i = 0; i < ZTrader.Count; i++)
        {
            ref var it = ref ZTrader.Stock[i];
            if (it.Kind != ZTraderItemKind.Part || it.SoldOut || it.Part < 0 || spare < it.Price + ZRules.TraderBotSurplus)
                continue;
            if (best < 0 || Parts.All[it.Part].Rarity > Parts.All[ZTrader.Stock[best].Part].Rarity)
                best = i;
        }
        return best;
    }

    // ---- Network (protocol 73) -----------------------------------------------------------------------------

    /// <summary>Server: the trader and his shelf as they are now.</summary>
    private void WriteTrader(PacketWriter w)
    {
        var t = ZTrader;
        w.Begin(Msg.ZTrader).U8(t.Here ? (byte)1 : (byte)0).U8(t.Visit).F32(t.At.X).F32(t.At.Y).F32(t.At.Z).F32(t.Yaw)
            .U16((ushort)Math.Clamp(t.LeaveRound, 0, 65535)).U16((ushort)Math.Clamp(t.CameRound, 0, 65535)).U8((byte)t.Count);
        for (var i = 0; i < t.Count; i++)
        {
            ref var it = ref t.Stock[i];
            w.U8((byte)it.Kind).U16((ushort)it.Part).U32((uint)Math.Max(0, it.Price)).U8(it.Left);
        }
    }

    /// <summary>Bytes of <see cref="Msg.ZTrader"/> after the id before the items, and per item.</summary>
    public const int ZTraderHeader = 23, ZTraderItemBytes = 8;

    /// <summary>Client: a <see cref="Msg.ZTrader"/> (after the id). A short or odd message changes nothing it can't read.</summary>
    private void ReadTrader(ref PacketReader r, double now)
    {
        if (!r.Has(ZTraderHeader))
            return;
        var t = ZTrader;
        var here = r.U8() != 0;
        var visit = r.U8();
        var at = new Vector3(r.F32(), r.F32(), r.F32());
        var yaw = r.F32();
        int leave = r.U16();
        int came = r.U16();
        int n = r.U8();
        if (!float.IsFinite(at.X + at.Y + at.Z + yaw))
            return;
        var arrived = here && (!t.Here || visit != t.Visit);
        t.Here = here;
        t.Visit = visit;
        t.At = at;
        t.Yaw = yaw;
        t.LeaveRound = leave;
        t.CameRound = came;
        t.Count = 0;
        for (var i = 0; i < n && r.Has(ZTraderItemBytes); i++)
        {
            var kind = (ZTraderItemKind)Math.Min(r.U8(), (byte)ZTraderItemKind.Ammo);
            var part = (short)r.U16();
            var price = (int)Math.Min(r.U32(), int.MaxValue);
            var left = r.U8();
            if (t.Count >= t.Stock.Length || (kind == ZTraderItemKind.Part && (part < 0 || part >= Parts.All.Length)))
                continue; // (a part this PC doesn't know: not on its shelf)
            t.Stock[t.Count++] = new ZTraderItem { Kind = kind, Part = part, Price = price, Left = left };
        }
        if (arrived)
            t.ChangedAt = now;
    }

    /// <summary>Server: everyone gets the trader (on an arrival, a departure, a purchase).</summary>
    private void BroadcastTrader()
    {
        ZTrader.ChangedAt = Brutal.GlfwApi.Glfw.Time; // (this PC's clock, like the banner: the HUD and the beam count from it)
        if (Net.IsOffline)
            return;
        WriteTrader(_w);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: the trader to a joiner.</summary>
    private void SendTraderTo(RakNetGUID guid)
    {
        WriteTrader(_w);
        Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>A client's <see cref="Msg.ZTraderBuy"/>: the shelf item and the visit it was seen on.</summary>
    public static void WriteTraderBuy(PacketWriter w, int item, int visit) => w.Begin(Msg.ZTraderBuy).U8((byte)Math.Clamp(item, 0, 255)).U8((byte)visit);

    /// <summary>Server: a <see cref="Msg.ZTraderBuy"/> from <paramref name="p"/> (after the id); returns why not, or null.</summary>
    private string? OnTraderBuy(Player p, ref PacketReader r, double now)
    {
        int item = r.U8();
        int visit = r.U8();
        return ServerTraderBuy(p, item, visit, now);
    }

    /// <summary>The local player buys shelf item <paramref name="item"/>: the host does it now, a client asks the server.</summary>
    public void TraderBuyRequest(int item)
    {
        if (Local is not { } me || !ZTrader.Here)
            return;
        if (IsServer)
        {
            if (ServerTraderBuy(me, item, ZTrader.Visit, _zNow) is { } why)
                LoadoutNotice = $"Trader: {why}";
            return;
        }
        WriteTraderBuy(_w, item, ZTrader.Visit);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    // ---- Self-test hooks -------------------------------------------------------------------------------------

    /// <summary>Self-test: the <see cref="Msg.ZTrader"/> the server sends now.</summary>
    public void SelfTestWriteTrader(PacketWriter w) => WriteTrader(w);

    /// <summary>Self-test: a client reads a whole <see cref="Msg.ZTrader"/>.</summary>
    public void SelfTestReadTrader(ReadOnlySpan<byte> msg, double now)
    {
        var r = new PacketReader(msg[1..]);
        ReadTrader(ref r, now);
    }

    /// <summary>Self-test: the server gets a whole <see cref="Msg.ZTraderBuy"/> from <paramref name="p"/>.</summary>
    public string? SelfTestTraderPacket(Player p, ReadOnlySpan<byte> msg, double now)
    {
        var r = new PacketReader(msg[1..]);
        return OnTraderBuy(p, ref r, now);
    }
}
