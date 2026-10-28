using System.Numerics;

namespace CallOfSlop;

/// <summary>What a co-op bot is on its way to buy (ZombieBotsShop.cs).</summary>
public sealed class ZBotShop
{
    public ZUseKind Kind = ZUseKind.None;
    public int Index;

    /// <summary>Where it stands to buy, and the area that spot is in.</summary>
    public Vector3 Stand;

    public int Area;

    /// <summary>When it gives up on reaching it; when it next thinks about shopping.</summary>
    public double Until, NextThink;

    /// <summary>Holding the interact key at the spot (the buy goes through after the hold time).</summary>
    public bool Holding;

    /// <summary>The last round this bot pulled a slot machine (one pull a round).</summary>
    public int SlotRound = -1;

    /// <summary>The trader's visit this bot last shopped at (one part a visit; ZombiesTrader.cs).</summary>
    public int TraderVisit = -1;

    /// <summary>Things it couldn't reach lately (kind, index, until), so it doesn't walk back to them at once.</summary>
    public readonly (ZUseKind Kind, int Index, double Until)[] Skip = new (ZUseKind, int, double)[4];

    public int Bought;

    /// <summary>The last free-for-all box offer (<see cref="Game.BoxOfferSeq"/>) this bot looked at and let go (checked once, not every tick).</summary>
    public int FreeBoxSeen = -1;

    public void Clear()
    {
        Kind = ZUseKind.None;
        Holding = false;
    }
}

/// <summary>
/// Co-op bots spend their own points (developer answer, 2026-10-26; docs/zombies/ZOMBIES-MODE.md, "Co-op bots"): they
/// walk to and buy the power switch, perks (in the rules' order, saving up for the next one they can reach; the same
/// prices as players, the doubling past the 4th included), a wall gun better than the one in hand, the slot upgrade, the
/// mystery box (taking the offer only when it beats the gun in hand), doors (between rounds, one a break for the whole
/// team) and the odd slot machine pull, all through the same interact key as a person. They only shop when it's safe:
/// in the break, or mid-round with no zombie within <see cref="ZombiesRules.BotQuietRange"/>; reviving a downed teammate,
/// running from a zombie and boarding windows always come first, and they never go further than
/// <see cref="ZombiesRules.BotShopRange"/> from their post (they walk back to it afterwards: nobody is left alone at the
/// windows). Area to area they go through the open doors' sides. Deterministic (no random numbers, fixed orders); a
/// think every <see cref="ZombiesRules.BotThinkSeconds"/>, nothing allocated per tick.
/// </summary>
public sealed partial class Game
{
    private readonly Dictionary<byte, ZBotShop> _zBotShops = new();

    /// <summary>The round in which a bot last opened a door (one door a break for the whole team).</summary>
    private int _zBotDoorRound = -1;

    /// <summary>Damage per second of each wall buy's gun (worked out once per map: wall builds don't change).</summary>
    private float[] _zWallDps = [];

    private ZombiesMap? _zWallDpsFor;

    /// <summary>Purchases bots made this run (self-test and the log), and the last few ("Bot 2: Perk 1").</summary>
    public int ZBotPurchases;

    public readonly List<string> ZBotBought = new();

    private ZBotShop BotShop(Player p)
    {
        if (!_zBotShops.TryGetValue(p.Id, out var s))
            _zBotShops[p.Id] = s = new ZBotShop();
        return s;
    }

    /// <summary>Both a new run and a respawn forget the plan.</summary>
    private void ResetBotShops()
    {
        foreach (var s in _zBotShops.Values)
        {
            s.Clear();
            s.NextThink = 0;
            s.SlotRound = -1;
            s.TraderVisit = -1;
            s.Bought = 0;
            s.FreeBoxSeen = -1;
            Array.Clear(s.Skip);
        }
        _zBotDoorRound = -1;
        ZBotPurchases = 0;
        ZBotBought.Clear();
    }

    /// <summary>
    /// Server, in a bot's Zombies tick once nothing more urgent is on (no zombie on top of it, nobody to revive, no boards
    /// to put back): where the bot walks for its shopping (<paramref name="goal"/>), and whether it holds the interact key
    /// (<paramref name="use"/>). False: nothing to buy now (it goes back to its post as before).
    /// </summary>
    private bool ZombieBotShop(Player p, BotBrain b, ZPlayer zp, float nearZombie, double now, out Vector3? goal, out bool use)
    {
        goal = null;
        use = false;
        if (!ZRules.BotsBuy || ZMap is not { } zm || Phase != MatchPhase.Playing)
            return false;
        var s = BotShop(p);
        var safe = ZPhase != ZRoundPhase.Round || nearZombie > ZRules.BotQuietRange;
        if (!safe)
        {
            s.Clear();
            return false;
        }

        // The box: a spin of ours is turning (wait by it) or offering (take it if it's better); someone else's offer in its
        // free-for-all window (board item 2026-10-27) only when it would have taken it as its own, within reach of its post.
        if (zm.BoxSpots.Count > 0 && (BoxState is ZBoxState.Spinning or ZBoxState.Offering && BoxOwner == p.Id || BotWantsFreeBox(p, zm, s, b)))
        {
            var spot = zm.BoxSpots[Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1)];
            if (BoxState == ZBoxState.Spinning)
            {
                s.Clear();
                return Flat(spot.At - p.Position) > 2.2f ? (goal = BotWaypoint(zm, p.Position, spot.At, spot.Area)) != null : true;
            }
            if (s.Kind != ZUseKind.BoxTake)
            {
                if (!BoxOfferBetter(p))
                    return false; // not worth it: let it go back in the box
                s.Kind = ZUseKind.BoxTake;
                s.Index = BoxSpot;
                s.Stand = BoxStand(spot);
                s.Area = spot.Area;
                s.Until = now + ZRules.BoxOfferSeconds;
                s.Holding = false;
            }
        }
        else if (s.Kind == ZUseKind.BoxTake)
            s.Clear(); // taken, or gone back in the box

        // A plan: (re)think now and then, when it has none.
        if (s.Kind == ZUseKind.None)
        {
            if (now < s.NextThink)
                return false;
            s.NextThink = now + ZRules.BotThinkSeconds;
            if (!PlanBotShop(p, zp, zm, s, b, now))
                return false;
        }

        // On the way, or there: hold the key; the buy goes through after the hold time (ZUseTick), then the plan is done.
        if (s.Holding)
        {
            if (zp.UseDone)
            {
                s.Bought++;
                ZBotPurchases++;
                if (ZBotBought.Count >= 32)
                    ZBotBought.RemoveAt(0);
                ZBotBought.Add($"{p.Name}: {s.Kind} {s.Index}"); // (one string per purchase, not per tick)
                if (s.Kind == ZUseKind.Door)
                    _zBotDoorRound = ZRound;
                if (s.Kind == ZUseKind.SlotMachine)
                    s.SlotRound = ZRound;
                s.Clear();
                s.NextThink = now + ZRules.BotThinkSeconds;
                return false;
            }
            if (now <= s.Until && ShopStillWanted(p, zp, zm, s))
            {
                use = true;
                return true;
            }
        }
        if (now > s.Until || !ShopStillWanted(p, zp, zm, s))
        {
            NoteSkip(s, now);
            s.Clear();
            return false;
        }
        if (Flat(s.Stand - p.Position) > 0.45f)
        {
            goal = BotWaypoint(zm, p.Position, s.Stand, s.Area);
            return true;
        }
        if (s.Kind == ZUseKind.Trader)
        {
            // At the trader: no hold, the purchase is made outright (a person's panel asks the same way; ZombiesTrader.cs).
            s.TraderVisit = ZTrader.Visit;
            if (ServerTraderBuy(p, s.Index, ZTrader.Visit, now) == null)
            {
                s.Bought++;
                ZBotPurchases++;
                if (ZBotBought.Count >= 32)
                    ZBotBought.RemoveAt(0);
                ZBotBought.Add($"{p.Name}: Trader {s.Index}");
            }
            s.Clear();
            s.NextThink = now + ZRules.BotThinkSeconds;
            return false;
        }
        // There: is the thing it came for really what the key does here? (the nearest buyable wins)
        if (FindZUse(p) is { } u && u.Kind == s.Kind && u.Index == s.Index && !u.Blocked && zp.Points >= u.Cost)
        {
            s.Holding = true;
            use = true;
            return true;
        }
        NoteSkip(s, now);
        s.Clear();
        return false;
    }

    /// <summary>Picks what to buy next, by priority (see the class summary). False: nothing now.</summary>
    private bool PlanBotShop(Player p, ZPlayer zp, ZombiesMap zm, ZBotShop s, BotBrain b, double now)
    {
        if (!ZRules.BotsBuy)
            return false;
        var points = zp.Points - ZRules.BotReserve;
        if (points <= 0)
            return false;
        var post = b.HoldAnchor;
        bool Reach(Vector3 at, int area) => AreaOpen(area) && Flat(at - post) <= ZRules.BotShopRange;
        bool Pick(ZUseKind kind, int index, Vector3 stand, int area)
        {
            if (Skipped(s, kind, index, now))
                return false;
            s.Kind = kind;
            s.Index = index;
            s.Stand = stand;
            s.Area = area;
            s.Until = now + ZRules.BotGiveUpSeconds;
            s.Holding = false;
            return true;
        }

        // 1. The power (free).
        if (zm.Power is { } pw && !ZPower && Reach(pw.At, pw.Area) && Pick(ZUseKind.Power, 0, SpotStand(pw, 0.8f), pw.Area))
            return true;

        // 2. Perks, most wanted first; the first one it can reach but not afford is what it saves for.
        var saving = 0;
        var held = PerkCount(zp);
        foreach (var key in ZRules.BotPerkOrder)
        {
            var k = ZRules.PerkIndex(key);
            if (k < 0 || zp.Has(k))
                continue;
            for (var i = 0; i < zm.PerkMachines.Count; i++)
            {
                var m = zm.PerkMachines[i];
                if (ZRules.PerkIndex(m.Perk) != k || (m.NeedsPower && !ZPower) || !Reach(m.At, m.Area) || Skipped(s, ZUseKind.Perk, i, now))
                    continue;
                var price = ZRules.PerkPrice(m.Cost > 0 ? m.Cost : ZRules.Perks[k].Price(Players.Count <= 1), held);
                if (points >= price)
                    return Pick(ZUseKind.Perk, i, SpotStand(m, 1.0f), m.Area);
                saving = price;
                break;
            }
            if (saving > 0)
                break;
        }
        var spare = points - saving;

        // 3. A wall gun better than the one in hand (or into a free slot).
        WallDps(zm);
        var inHand = p.WeaponDef.Dps;
        var free = FreeGunSlot(p) >= 0;
        for (var i = 0; i < zm.WallBuys.Count; i++)
        {
            var wb = zm.WallBuys[i];
            if (OwnsBuild(p, wb.Build) >= 0 || !Reach(wb.At, wb.Area) || spare < WallCost(wb))
                continue;
            if ((free || _zWallDps[i] > inHand * ZRules.BotWallBetter) && Pick(ZUseKind.WallBuy, i, wb.At with { Y = p.Position.Y } + World.FlatForward(wb.Yaw) * 0.7f, wb.Area))
                return true;
        }

        // 4. The slot upgrade.
        if (zm.SlotUpgrade is { } su && !p.ExtraSlots && (!su.NeedsPower || ZPower) && Reach(su.At, su.Area) && spare >= SlotCost(zm)
            && Pick(ZUseKind.SlotUpgrade, 0, SpotStand(su, 0.9f), su.Area))
            return true;

        // 4b. The trader, while he's here: one part a visit, the rarest it can afford with plenty to spare.
        if (ZTrader.Here && s.TraderVisit != ZTrader.Visit && BotTraderPick(spare) is var ti and >= 0)
        {
            var area = zm.AreaAt(ZTrader.At);
            if (Reach(ZTrader.At, area) && Pick(ZUseKind.Trader, ti, ZTrader.At + World.FlatForward(ZTrader.Yaw) * 1.1f, area))
                return true;
        }

        // 5. The mystery box.
        if (BoxState == ZBoxState.Idle && zm.BoxSpots.Count > 0)
        {
            var spot = zm.BoxSpots[Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1)];
            if (Reach(spot.At, spot.Area) && spare >= BoxCost(zm) + ZRules.BotBoxSurplus && Pick(ZUseKind.Box, BoxSpot, BoxStand(spot), spot.Area))
                return true;
        }

        // 6. A door, between rounds only, one a break for the whole team: the cheapest one off an open area.
        if (ZRules.BotDoors && ZPhase != ZRoundPhase.Round && _zBotDoorRound != ZRound)
        {
            var best = -1;
            for (var i = 0; i < zm.Doors.Count; i++)
            {
                var d = zm.Doors[i];
                if (World.Map.IsDoorOpen(i) || (d.NeedsPower && !ZPower) || spare < d.Cost || (AreaOpen(d.AreaA) == AreaOpen(d.AreaB)))
                    continue;
                var side = d.SideIn(AreaOpen(d.AreaA) ? d.AreaA : d.AreaB);
                if (Flat(side - post) > ZRules.BotShopRange || Skipped(s, ZUseKind.Door, i, now))
                    continue;
                if (best < 0 || d.Cost < zm.Doors[best].Cost)
                    best = i;
            }
            if (best >= 0)
            {
                var d = zm.Doors[best];
                var area = AreaOpen(d.AreaA) ? d.AreaA : d.AreaB;
                return Pick(ZUseKind.Door, best, d.SideIn(area), area);
            }
        }

        // 7. A slot machine pull, with plenty to spare (one a round).
        if (s.SlotRound != ZRound)
            for (var i = 0; i < zm.SlotMachines.Count && i < ZSlots.Length; i++)
            {
                var m = zm.SlotMachines[i];
                if (ZSlots[i].State != ZSlotState.Idle || (m.NeedsPower && !ZPower) || !Reach(m.At, m.Area) || spare < SlotCost(zm, i) + ZRules.BotSlotSurplus)
                    continue;
                if (Pick(ZUseKind.SlotMachine, i, SpotStand(m, 0.9f), m.Area))
                    return true;
            }
        return false;
    }

    /// <summary>Is the plan still worth it (nobody else bought it, the price still fits)?</summary>
    private bool ShopStillWanted(Player p, ZPlayer zp, ZombiesMap zm, ZBotShop s) => s.Kind switch
    {
        ZUseKind.Power => !ZPower,
        ZUseKind.Door => s.Index < zm.Doors.Count && !World.Map.IsDoorOpen(s.Index) && zp.Points >= zm.Doors[s.Index].Cost,
        ZUseKind.Perk => s.Index < zm.PerkMachines.Count && ZRules.PerkIndex(zm.PerkMachines[s.Index].Perk) is var k and >= 0 && !zp.Has(k),
        ZUseKind.WallBuy => s.Index < zm.WallBuys.Count && OwnsBuild(p, zm.WallBuys[s.Index].Build) < 0 && zp.Points >= WallCost(zm.WallBuys[s.Index]),
        ZUseKind.SlotUpgrade => !p.ExtraSlots && zp.Points >= SlotCost(zm),
        ZUseKind.Box => BoxState == ZBoxState.Idle && BoxSpot == s.Index && zp.Points >= BoxCost(zm),
        ZUseKind.BoxTake => CanTakeBox(p),
        ZUseKind.SlotMachine => s.Index < ZSlots.Length && (ZSlots[s.Index].State == ZSlotState.Idle || s.Holding) && zp.Points >= SlotCost(zm, s.Index),
        ZUseKind.Trader => ZTrader.Here && s.TraderVisit != ZTrader.Visit && s.Index < ZTrader.Count && !ZTrader.Stock[s.Index].SoldOut && zp.Points >= ZTrader.Stock[s.Index].Price,
        _ => false,
    };

    /// <summary>
    /// Someone else's box offer is free for all, and this bot would take it as its own (it beats the gun in hand), the box
    /// is within its shopping reach and in an open area, and it isn't already holding the key for something else.
    /// </summary>
    private bool BotWantsFreeBox(Player p, ZombiesMap zm, ZBotShop s, BotBrain b)
    {
        if (BoxOwner == p.Id || !BoxFreeForAll)
            return false;
        if (s.Kind == ZUseKind.BoxTake)
            return true; // (on its way already)
        if (s.Holding)
            return false;
        if (s.FreeBoxSeen == BoxOfferSeq)
            return false; // (already let this one go)
        s.FreeBoxSeen = BoxOfferSeq;
        var spot = zm.BoxSpots[Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1)];
        return AreaOpen(spot.Area) && Flat(spot.At - b.HoldAnchor) <= ZRules.BotShopRange && BoxOfferBetter(p);
    }

    /// <summary>Does the box's offer beat the gun in the bot's hands (damage per second)?</summary>
    private bool BoxOfferBetter(Player p)
    {
        if (BoxBuild < 0 || BoxBuild >= StandardBuilds.All.Length)
            return false;
        var ob = StandardBuilds.All[BoxBuild];
        if (ob.IsMelee)
            return false;
        return AssembleCached(ob.Build()).Def is { } d && (FreeGunSlot(p) >= 0 || d.Dps > p.WeaponDef.Dps);
    }

    private void WallDps(ZombiesMap zm)
    {
        if (ReferenceEquals(_zWallDpsFor, zm) && _zWallDps.Length == zm.WallBuys.Count)
            return;
        _zWallDpsFor = zm;
        _zWallDps = new float[zm.WallBuys.Count];
        for (var i = 0; i < zm.WallBuys.Count; i++)
            _zWallDps[i] = StandardBuilds.Find(zm.WallBuys[i].Build) is { } sb && AssembleCached(sb.Build()).Def is { } d ? d.Dps : 0f;
    }

    private static Vector3 SpotStand(ZSpot s, float forward) => s.At + World.FlatForward(s.Yaw) * forward;

    private static Vector3 BoxStand(ZSpot s) => s.At + World.FlatForward(s.Yaw) * 1.2f;

    private static bool Skipped(ZBotShop s, ZUseKind kind, int index, double now)
    {
        foreach (var k in s.Skip)
            if (k.Kind == kind && k.Index == index && now < k.Until)
                return true;
        return false;
    }

    /// <summary>Couldn't buy it: leave it alone for a while (the oldest entry goes).</summary>
    private void NoteSkip(ZBotShop s, double now)
    {
        if (s.Kind == ZUseKind.None)
            return;
        var at = 0;
        for (var i = 1; i < s.Skip.Length; i++)
            if (s.Skip[i].Until < s.Skip[at].Until)
                at = i;
        s.Skip[at] = (s.Kind, s.Index, now + 20);
    }

    /// <summary>
    /// The next point on the way to <paramref name="to"/> in area <paramref name="toArea"/>: straight on in the same area,
    /// else the near side of the first open door towards it, then its far side. (Bots walk area to area through doorways.)
    /// </summary>
    private Vector3 BotWaypoint(ZombiesMap zm, Vector3 from, Vector3 to, int toArea)
    {
        var a = zm.AreaAt(from);
        if (a < 0 || toArea < 0 || a == toArea)
            return to;
        var d = DoorTowards(a, toArea);
        if (d < 0)
            return to;
        var door = zm.Doors[d];
        var near = door.SideIn(a);
        return Flat(near - from) > 0.6f ? near : door.SideOut(a);
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    /// <summary>Self-test: what bot <paramref name="p"/> plans to buy now (None: nothing), without walking there.</summary>
    public ZUseKind SelfTestBotPlan(Player p, double now)
    {
        if (ZMap is not { } zm || p.Bot is not { } b)
            return ZUseKind.None;
        var s = BotShop(p);
        s.Clear();
        return PlanBotShop(p, ZP(p), zm, s, b, now) ? s.Kind : ZUseKind.None;
    }

    /// <summary>Self-test: the bot's current plan.</summary>
    public ZUseKind SelfTestBotShopKind(Player p) => _zBotShops.TryGetValue(p.Id, out var s) ? s.Kind : ZUseKind.None;
}
