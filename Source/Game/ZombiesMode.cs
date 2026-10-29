using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>Where a Zombies run is between rounds.</summary>
public enum ZRoundPhase : byte
{
    /// <summary>Before round 1 (a few seconds after the match starts).</summary>
    Waiting,

    /// <summary>Zombies are coming (the round ends when every one of them is dead).</summary>
    Round,

    /// <summary>The break between rounds: ready-up (<see cref="Game.ZWaitingReady"/>), then a short countdown (ZombiesFlow.cs).</summary>
    Break,
}

/// <summary>The mystery box.</summary>
public enum ZBoxState : byte
{
    Idle,
    Spinning,

    /// <summary>The gun floats over the box for its buyer to take.</summary>
    Offering,

    /// <summary>The teddy bear: the box refunds its price and flies off to another spot.</summary>
    Teddy,
}

/// <summary>What the interact key would do for a player right now in Zombies.</summary>
public enum ZUseKind : byte
{
    None,
    Revive,
    Door,
    WallBuy,
    WallAmmo,
    Box,
    BoxTake,
    Perk,
    SlotUpgrade,
    Power,
    Workbench,
    Salvage,
    Repair,

    /// <summary>A built weapon on the floor (dropped when someone bled out): pick it up (ZombiesDrops.cs).</summary>
    Pickup,

    /// <summary>A slot machine: pull for a part, points back or nothing (ZombiesSlots.cs).</summary>
    SlotMachine,

    /// <summary>A casino table: press to open its panel (the client opens it; moves go up in Msg.ZCasinoAct; ZombiesCasino.cs).</summary>
    Casino,

    /// <summary>Loose parts a player dropped: pick them up into your loose parts (ZombiesDropItems.cs).</summary>
    PartPickup,

    /// <summary>The trader: press to open his shelf (the client opens it; purchases go up in Msg.ZTraderBuy; ZombiesTrader.cs).</summary>
    Trader,
}

/// <summary>One thing the interact key can use: what, which one, its price and the prompt.</summary>
public readonly record struct ZUse(ZUseKind Kind, int Index, int Cost, string Text, bool Blocked = false)
{
    /// <summary>Held use: the key does it over time (revive, rebuild boards) rather than once.</summary>
    public bool Continuous => Kind is ZUseKind.Revive or ZUseKind.Repair;
}

/// <summary>A player's Zombies run (server-owned; clients get points, downs and perks in <see cref="Msg.ZWorld"/>, parts in <see cref="Msg.ZRun"/>).</summary>
public sealed class ZPlayer
{
    public int Points;
    public bool Downed;
    public double DownedAt, BleedAt, SelfReviveAt = -1;

    /// <summary>Perks held, one bit per perk in the rules' list (16 bits since protocol 57: <see cref="ZombiesRules.MaxPerkCount"/>).</summary>
    public ushort Perks;

    /// <summary>Server: an armour plate perk (Bulwark Brew) takes the next zombie hit from this time on (it grows back after a hit). Clients: <see cref="PlateReady"/>.</summary>
    public double PlateReadyAt;

    /// <summary>The plate is up (both sides: the server works it out, clients get it in <see cref="Msg.ZWorld"/> for the HUD).</summary>
    public bool PlateReady;

    /// <summary>Held by a grabber (Source/Game/ZombieTypes.cs): which zombie and until when (server); held at all (both sides: in <see cref="Msg.ZWorld"/>, protocol 73; movement is stopped by it).</summary>
    public ushort HeldBy; // (0: nobody; zombie ids are never 0)

    public double HeldUntil;
    public bool Held;

    /// <summary>The interact key is held (sent by the client in <see cref="Msg.ZUse"/>), since when, and whether this hold already bought something.</summary>
    public bool UseHeld, UseDone;

    public double UseSince;
    public double RepairNext;
    public int RepairPointsThisRound;

    /// <summary>Who this player is reviving and how far along (0-1); and, for a downed player, how far their revive is (clients).</summary>
    public byte Reviving = byte.MaxValue;

    public float ReviveProgress;
    public float BeingRevived;
    public float BleedFraction;

    /// <summary>Loose parts (by name), for the workbench. (Zombies has no scrap since 2026-10-27.)</summary>
    public readonly SortedDictionary<string, int> Parts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The build in each slot (0-3 guns, 4 the melee weapon, 5 the grenade), in text form: what the workbench edits and clients assemble.</summary>
    public readonly WeaponBuild?[] Slots = new WeaponBuild?[Game.BuildSlots];

    /// <summary>Slot machines: "nothing" results in a row on each machine (bad-luck protection, ZombiesSlots.cs).</summary>
    public readonly byte[] SlotNothing = new byte[ZombiesMap.MaxSlotMachines];

    /// <summary>Bled out: comes back with the start gear when the round ends.</summary>
    public bool NeedsStartGear = true;

    /// <summary>
    /// Ready-up between rounds (ZombiesFlow.cs): this player pressed Ready for the next round; and whether they're one of
    /// the people the break waits for (alive, not a bot). Server-owned, mirrored to clients in <see cref="Msg.ZWorld"/>.
    /// </summary>
    public bool RoundReady, ReadyVoter;

    public bool Spawned;
    public int Kills, Headshots, Downs, Revives;

    /// <summary>Client: the points last shown (the HUD's +N popups come from the difference).</summary>
    public int ShownPoints = int.MinValue;

    public bool Has(int perk) => perk >= 0 && (Perks & (1 << perk)) != 0;
}

/// <summary>A part lying on the floor (dropped by a zombie): walk over it to pick it up.</summary>
public sealed class ZDrop
{
    public ushort Id;
    public string Part = "";
    public Vector3 At;
    public double Until;
}

/// <summary>
/// The Zombies mode (docs/zombies/ZOMBIES-MODE.md): co-op for 1-4 players against rounds of zombies, World at War style,
/// played on maps with a "zombies" section (<see cref="ZombiesMap"/>). Server-authoritative and deterministic (its own
/// seeded random numbers): rounds and their curve (<see cref="ZombiesRules"/>), spawners in unlocked areas, windows with
/// boards, the points economy, doors and debris that unlock areas, wall buys, the mystery box, perk machines, the
/// weapon-slot upgrade, the power switch, part drops, salvage and the workbench, downs, revives and the game over.
/// The zombies themselves are in ZombieAI.cs, the packets in ZombiesNet.cs, the co-op bots in ZombieBots.cs.
/// </summary>
public sealed partial class Game
{
    /// <summary>Zombies is co-op for up to this many players (people and bots).</summary>
    public const int ZombiesMaxPlayers = 4;

    /// <summary>
    /// The most zombies that can be alive at once (the rules file's cap and curve go up to this; protocol 58 sends any
    /// number). 600 since the player scaling (2026-10-27): the shipped maxAlive, so "Zombie hordes" measures the budget at it.
    /// </summary>
    public const int MaxZombies = 600;

    /// <summary>The old <see cref="MaxZombies"/>: a settings file's horde cap at or above it was "no limit" and stays so.</summary>
    public const int OldMaxZombies = 512;

    /// <summary>The rules (Parts/Tables/ZombiesRules.json).</summary>
    public ZombiesRules ZRules = ZombiesRules.Current;

    /// <summary>The map's Zombies layout while playing the Zombies mode, else null.</summary>
    public ZombiesMap? ZMap => Mode == GameMode.Zombies ? World.Map.Zombies : null;

    public bool IsZombies => ZMap != null;

    // ---- Run state (server-owned, replicated in ZWorld) ----------------------------------------------
    public int ZRound;
    public ZRoundPhase ZPhase;
    public double ZPhaseLeft; // seconds left of the break (clients count it down)
    public int ZRemaining; // zombies still to come this round (queued + alive)
    public bool ZPower;
    public byte[] ZBoards = [];
    public int BoxSpot, BoxUses;
    public ZBoxState BoxState;
    public double BoxLeft; // seconds left in the box's state
    public int BoxBuild = -1; // offered build (index into StandardBuilds.All)
    public byte BoxOwner = byte.MaxValue;
    public readonly Dictionary<byte, ZPlayer> ZPlayers = new();
    public readonly List<ZDrop> ZDrops = new();

    private int _zToSpawn;

    /// <summary>
    /// The round's total (its count for <see cref="_zRoundPlayers"/> players, plus map logic's extra spawns) and the player
    /// count it was worked out for. A join or leave mid-round rescales it (<see cref="RescaleRoundForPlayers"/>).
    /// </summary>
    private int _zRoundTotal, _zRoundPlayers;
    private double _zPhaseUntil, _zNextSpawn, _boxUntil;
    private int _boxMoveAfter;
    private uint _zRng = 1;
    private int _zRunMatch = -1;
    private ushort _zNextDrop = 1;
    private readonly int[] _salvageRound = new int[64];
    private bool _zAnySpawned;

    /// <summary>The seed the next run starts from (the self-test sets it; otherwise a fixed per-match value, so runs replay the same).</summary>
    public uint ZSeed = 0x5EED2026;

    /// <summary>Self-test: the host (with no one at the keyboard) stays out of the run, so a bots-only run can be played.</summary>
    public bool ZHostSitsOut;

    /// <summary>Server: a line for the log when a run ends (the self-test reads it).</summary>
    public string ZLastResult = "";

    /// <summary>A player's run (created on first use).</summary>
    public ZPlayer ZP(Player p) => ZP(p.Id);

    public ZPlayer ZP(byte id)
    {
        if (!ZPlayers.TryGetValue(id, out var z))
            ZPlayers[id] = z = new ZPlayer { Points = ZJoinPoints }; // (more in a run started at a later round: ZombiesStartRound.cs)
        return z;
    }

    /// <summary>Downed (still alive, on the floor, waiting for a revive).</summary>
    public bool IsDowned(Player p) => ZPlayers.TryGetValue(p.Id, out var z) && z.Downed;

    /// <summary>Standing and fighting: alive and not downed.</summary>
    public bool IsStanding(Player p) => p.Alive && !IsDowned(p);

    // ---- Deterministic random numbers ------------------------------------------------------------------

    private uint ZNext()
    {
        var x = _zRng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return _zRng = x == 0 ? 0x9E3779B9u : x;
    }

    private float ZRand() => (ZNext() >> 8) / 16777216f;

    private int ZRange(int n) => n <= 1 ? 0 : (int)(ZNext() % (uint)n);

    // ---- Run lifecycle ----------------------------------------------------------------------------------

    /// <summary>
    /// Server: forget the last run (a new match, a mode change): the world resets (<see cref="ResetZombiesWorld"/>), every
    /// player's run (points, perks, parts) and the built guns go. Nothing happens outside Zombies.
    /// </summary>
    internal void ResetZombiesRun()
    {
        if (Mode != GameMode.Zombies && !_zBuildsGiven)
        {
            World.Map.OpenAllDoors(); // another mode on a Zombies map: every door is open (one big map)
            return;
        }
        _zBuildsGiven = Mode == GameMode.Zombies;
        if (Mode == GameMode.Zombies)
            while (Players.Count > ZombiesMaxPlayers && KickOneBot())
            {
                // co-op for up to 4: extra bots go
            }
        ResetZombiesWorld();
        ZPlayers.Clear();
        _zRunMatch = -1;
        _zAnySpawned = false;
        foreach (var p in Players.Values)
        {
            if (Mode != GameMode.Zombies && p.Bot == null)
            {
                // Out of Zombies (BACK TO LOBBY and another mode): the person's own multiplayer guns again, not the
                // run's built guns (whose ids mean nothing outside it). Bots draw a fresh loadout every spawn.
                (p.WantPrimary, p.WantSecondary, p.WantPrimaryKit, p.WantSecondaryKit) = (p.MpPrimary, p.MpSecondary, p.MpPrimaryKit, p.MpSecondaryKit);
                (p.Primary, p.Secondary, p.PrimaryKit, p.SecondaryKit, p.Weapon) = (p.MpPrimary, p.MpSecondary, p.MpPrimaryKit, p.MpSecondaryKit, p.MpPrimary);
            }
            p.BuiltPrimary = p.BuiltSecondary = null;
            p.BuiltExtra[0] = p.BuiltExtra[1] = null;
            p.ExtraSlots = false;
            p.MeleeBuild = null;
            p.GrenadeBuild = null;
        }
    }

    /// <summary>Build slots per player: 0-3 guns, <see cref="MeleeSlot"/> the melee weapon, <see cref="GrenadeSlot"/> the grenade (modular grenades, protocol 67).</summary>
    public const int MeleeSlot = 4, GrenadeSlot = 5, BuildSlots = 6;

    /// <summary>"slot 2", "melee", "grenade": a slot in the feed's words.</summary>
    public static string SlotWord(int slot) => slot == MeleeSlot ? "melee" : slot == GrenadeSlot ? "grenade" : (slot + 1).ToString();

    /// <summary>Which kind of build a slot takes.</summary>
    public static BuildKind SlotKind(int slot) => slot == MeleeSlot ? BuildKind.Melee : slot == GrenadeSlot ? BuildKind.Grenade : BuildKind.Gun;

    private bool _zBuildsGiven;

    /// <summary>Both sides: the map and the run state back to the start (doors shut, boards up, no zombies, round 0).</summary>
    internal void ResetZombiesWorld()
    {
        StressSummary("run over"); // a horde stress round's figures, if not written yet (ZombiesCrowdStress.cs)
        StressState = StressPhase.Off;
        ClearHorde();
        CrowdRoundTotal = CrowdRoundKills = 0;
        CrowdSeen.Clear(); // (a client's picture of the crowd)
        ZDrops.Clear();
        ZWeaponDrops.Clear();
        ZPartDrops.Clear(); // ZombiesDropItems.cs
        ResetPowerUps(); // ZombiesPowerUps.cs
        ResetBotShops(); // ZombieBotsShop.cs
        ZFxList.Clear();
        ZPlayers.Clear();
        ZRound = 0;
        _zRunStartPoints = 0;
        ZPhase = ZRoundPhase.Waiting;
        ZPhaseLeft = 0;
        ZRemaining = 0;
        ZWaitingReady = false;
        _zForceRound = false;
        ZPower = false;
        BoxState = ZBoxState.Idle;
        BoxBuild = -1;
        BoxOwner = byte.MaxValue;
        BoxUses = 0;
        ResetSlots(); // ZombiesSlots.cs
        ResetCasino(); // ZombiesCasino.cs
        ResetTrader(); // ZombiesTrader.cs
        Array.Clear(_salvageRound);
        World.Map.CloseAllDoors();
        var z = World.Map.Zombies;
        ZBoards = z == null ? [] : z.Windows.Select(w => (byte)w.Boards).ToArray();
        BoxSpot = z?.BoxStart ?? 0;
    }

    /// <summary>Server: the run starts (the match just went live).</summary>
    private void StartZombiesRun(double now)
    {
        _zRunMatch = MatchNumber;
        _zRng = ZSeed ^ (uint)(MatchNumber * 7919);
        ZNext();
        _boxMoveAfter = ZRules.BoxMoveMin + ZRange(ZRules.BoxMoveMax - ZRules.BoxMoveMin + 1);
        ZRound = 0;
        ZPhase = ZRoundPhase.Waiting;
        _zPhaseUntil = now + ZRules.FirstRoundDelay;
        if (NavGraph.Current == NavGraph.Empty)
            NavGraph.Pump(10000); // the zombies' nav graph, now (doors shut), so every run plans on the same graph from its first zombie
        foreach (var p in Players.Values.OrderBy(p => p.Id))
        {
            var zp = ZP(p);
            if (!zp.Spawned && p.Alive)
            {
                zp.Spawned = true;
                if (zp.NeedsStartGear)
                    GiveStartGear(p);
            }
        }
        ApplyStartRound(); // the lobby's Start round and Test kit (ZombiesStartRound.cs; nothing at round 1 without the kit)
        ModeEvent($"Zombies on {World.Map.Name}: survive as long as you can. Round {ZRound + 1} in {ZRules.FirstRoundDelay:0} seconds.", Sfx.StreakCall);
        Console.WriteLine($"[zombies] run started: {Players.Count} player(s), seed {_zRng:x8}, {World.Map.Zombies!.Spawners.Count} spawners, {World.Map.Zombies.Doors.Count} doors");
    }

    /// <summary>Server, every tick while a Zombies match is playing.</summary>
    internal void ServerTickZombies(double now)
    {
        if (ZMap is not { } zm)
            return;
        using var perf = Perf.Measure(PerfArea.Zombies); // the perf window (Source/Perf)
        _zNow = now;
        if (_zRunMatch != MatchNumber)
            StartZombiesRun(now);

        switch (ZPhase)
        {
            case ZRoundPhase.Break when ZWaitingReady:
                TickReadyUp(now); // everyone ready (or the fallback, or the host's force start): the short countdown (ZombiesFlow.cs)
                break;
            case ZRoundPhase.Waiting or ZRoundPhase.Break when now >= _zPhaseUntil:
                BeginRound(ZRound + 1, now);
                break;
            case ZRoundPhase.Round:
                RescaleRoundForPlayers(); // someone joined or left (bots too): the round's total follows at once
                SpawnTick(zm, now);
                if (_zToSpawn == 0 && AliveZombies == 0 && Crowd.Total == 0)
                    EndRound(now);
                break;
        }
        ZPhaseLeft = ZPhase == ZRoundPhase.Round ? 0 : ZWaitingReady ? ReadyFallbackLeft(now) : Math.Max(0, _zPhaseUntil - now);
        ZRemaining = _zToSpawn + AliveZombies + Crowd.Total; // (the crowd: ZombiesCrowd.cs, none off crowd maps)

        TickHorde(zm, now); // ZombieAI.cs
        foreach (var zpl in ZPlayers.Values)
            zpl.BeingRevived = 0; // set again by whoever is reviving them this tick: crawling out of reach, or the reviver letting go, clears it
        for (var id = 0; id < 256; id++) // (in id order without OrderBy: no allocation every tick)
            if (Players.TryGetValue((byte)id, out var p))
                TickZPlayer(p, ZP(p), zm, now);
        TickBox(zm, now);
        TickSlots(zm, now); // ZombiesSlots.cs
        TickCasino(zm, now); // ZombiesCasino.cs
        TickDrops(now);
        TickWeaponDrops(now); // ZombiesDrops.cs
        TickPartDrops(now); // ZombiesDropItems.cs
        TickPowerUps(now); // ZombiesPowerUps.cs

        // The game over: nobody left standing (everyone downed or bled out).
        if (_zAnySpawned && ZRound > 0 && Players.Count > 0 && !AnyoneStanding())
        {
            var survived = Math.Max(0, ZRound - 1);
            ZLastResult = $"Game over on round {ZRound}: survived {survived} round{(survived == 1 ? "" : "s")}";
            Console.WriteLine($"[zombies] {ZLastResult}, {Players.Values.Sum(p => ZP(p).Kills)} kills");
            EndMatch(null, ZLastResult, now);
            return;
        }
        ServerSendZombies(now); // ZombiesNet.cs
    }

    /// <summary>Is anyone standing, or about to get up on their own (Second Wind)? (A loop: no closure every tick.)</summary>
    private bool AnyoneStanding()
    {
        foreach (var p in Players.Values)
            if (IsStanding(p) || (p.Alive && ZP(p).SelfReviveAt > 0))
                return true;
        return false;
    }

    private void BeginRound(int round, double now)
    {
        ZRound = round;
        ZPhase = ZRoundPhase.Round;
        ZWaitingReady = false;
        _zForceRound = false;
        _zRoundPlayers = ZRoundPlayerCount();
        _zToSpawn = _zRoundTotal = ZRules.ZombieCount(round, _zRoundPlayers);
        if (World.Map.Zombies is { Crowd: > 0 } czm)
            CrowdBeginRound(czm, round, now); // a mass-horde map: the round is its crowd (ZombiesCrowd.cs)
        _zNextSpawn = now + 1.0;
        TypesBeginRound(round); // a boss round queues its boss (ZombieTypes.cs)
        _zPowerThisRound = 0; // the per-round power-up cap (ZombiesPowerUps.cs)
        foreach (var p in Players.Values)
        {
            ZP(p).RepairPointsThisRound = 0;
            GiveZombiesGrenades(p, ZRules.GrenadesForRound(round), topUp: true); // refreshed every round, never taken away
            if (!p.Alive && !(ZHostSitsOut && p.Id == LocalId))
            {
                p.RespawnAt = now; // bled out during the break (or joined late): back in with the start gear (bleeding out in a round: back at its end, EndRound)
                p.WantsRespawn = true;
            }
        }
        ModeEvent($"ROUND {round}", Sfx.MortarWhistle);
        ZBanner($"ROUND {round}");
        LogicZombies(LogicEvent.RoundStarted, round); // map logic: zombies.roundStarted, n = the round
        if (ZMap is { } tzm)
            TraderOnRound(tzm, round, now); // the trader comes every 5th round and leaves when his time is up (ZombiesTrader.cs)
        WarmZombiePool(); // the round's zombies made now, not as they spawn (ZombiePool.cs)
    }

    /// <summary>The players a round is sized for: everyone in the run (bots count), less a host who sits out; at least 1.</summary>
    private int ZRoundPlayerCount() => Math.Max(1, Players.Count - (ZHostSitsOut ? 1 : 0));

    /// <summary>
    /// Server, every tick of a round (2026-10-27, the player scaling): when the player count has changed since the round's
    /// total was worked out (a join, a leave, a bot added or a player dropping), the total becomes the round's count for
    /// the new number (map logic's extra spawns kept) and the zombies still to spawn become that total less the ones
    /// already out (killed or alive), never below 0. A leave never ends the round there and then: with nobody alive and
    /// zombies still owed, at least one more comes. The alive cap (<see cref="ZAliveCap"/>) reads the player count live.
    /// </summary>
    private void RescaleRoundForPlayers()
    {
        var n = ZRoundPlayerCount();
        if (n == _zRoundPlayers || CrowdOn)
            return; // (a crowd round's total is the map's crowd)
        var outAlready = _zRoundTotal - _zToSpawn; // killed + alive
        _zRoundTotal = Math.Max(0, _zRoundTotal + ZRules.ZombieCount(ZRound, n) - ZRules.ZombieCount(ZRound, _zRoundPlayers));
        var left = Math.Max(0, _zRoundTotal - outAlready);
        if (left == 0 && _zToSpawn > 0 && AliveZombies == 0)
            left = 1; // (the round still ends the usual way: its last zombie killed)
        Console.WriteLine($"[zombies] round {ZRound}: {_zRoundPlayers} -> {n} players, total {_zRoundTotal}, {outAlready} out already, {_zToSpawn} -> {left} to spawn, alive cap {ZAliveCap()}");
        _zToSpawn = left;
        _zRoundPlayers = n;
        WarmZombiePool(); // (the alive cap follows the player count)
    }

    private void EndRound(double now)
    {
        ZPhase = ZRoundPhase.Break;
        StressSummary("round survived"); // a horde stress round's figures (ZombiesCrowdStress.cs)
        LogicZombies(LogicEvent.RoundEnded, ZRound); // map logic: zombies.roundEnded, n = the round survived
        ExpireDropsAtRoundEnd(ZRound); // things dropped on purpose last to the end of the next round (ZombiesDropItems.cs)
        RespawnAtRoundEnd(now); // the dead come back now, with the whole break to rebuild (ZombiesFlow.cs)
        if (BeginReadyUp(now)) // the next round waits for everyone's Ready (ZombiesFlow.cs)
            return;
        _zPhaseUntil = now + ZRules.BreakSeconds; // ready-up off, or nobody to wait for (a bots-only run)
        ModeEvent($"Round {ZRound} survived. Next round in {ZRules.BreakSeconds:0} seconds.", Sfx.ReconBeep);
    }

    // ---- Areas -------------------------------------------------------------------------------------------

    /// <summary>Is area <paramref name="area"/> unlocked: the start area, or behind an open door?</summary>
    public bool AreaOpen(int area)
    {
        if (World.Map.Zombies is not { } zm)
            return false;
        if (area == zm.StartArea)
            return true;
        for (var d = 0; d < zm.Doors.Count; d++)
            if ((zm.Doors[d].AreaA == area || zm.Doors[d].AreaB == area) && World.Map.IsDoorOpen(d))
                return true;
        return false;
    }

    /// <summary>The first open door on the way from area <paramref name="from"/> to <paramref name="to"/> (-1: same area or no way).</summary>
    public int DoorTowards(int from, int to)
    {
        if (from == to || World.Map.Zombies is not { } zm)
            return -1;
        Span<int> via = stackalloc int[ZombiesMap.MaxAreas];
        Span<int> queue = stackalloc int[ZombiesMap.MaxAreas];
        via.Fill(-2);
        via[from] = -1;
        int head = 0, tail = 0;
        queue[tail++] = from;
        while (head < tail)
        {
            var a = queue[head++];
            for (var d = 0; d < zm.Doors.Count; d++)
            {
                var door = zm.Doors[d];
                if ((door.AreaA != a && door.AreaB != a) || !World.Map.IsDoorOpen(d))
                    continue;
                var b = door.Other(a);
                if (via[b] != -2)
                    continue;
                via[b] = d;
                if (b == to)
                {
                    // Walk back to the first door out of "from".
                    var cur = b;
                    var first = d;
                    while (true)
                    {
                        var dd = via[cur];
                        if (dd < 0)
                            break;
                        first = dd;
                        cur = zm.Doors[dd].Other(cur);
                    }
                    return first;
                }
                queue[tail++] = b;
            }
        }
        return -1;
    }

    // ---- Players ----------------------------------------------------------------------------------------

    /// <summary>Server, from Spawn: a Zombies player starts in the start area with the start gear (a fresh run, or back after bleeding out).</summary>
    internal void ZombiesOnSpawn(Player p)
    {
        if (World.Map.Zombies is not { } zm)
            return;
        ApplyPerks(p, PerkSet.Empty); // the multiplayer perks don't apply in Zombies; the perk machines do
        // The Zombies grenade, not the loadout's throwables (ZombiesRules.json "grenades"): the round's count. Before
        // 2026-10-27 this took every grenade away while the client kept its loadout count, so a throw played its sound
        // and the server dropped it ("grenades do nothing in Zombies").
        GiveZombiesGrenades(p, ZRules.GrenadesForRound(ZRound), topUp: false);
        p.Equipment = ThrowableKind.None;
        p.EquipmentLeft = 0;
        var zp = ZP(p);
        zp.Downed = false;
        zp.SelfReviveAt = -1;
        // A spawn in the start area, by player id (deterministic; spread so nobody stands on anyone).
        var starts = World.Map.Spawns.Where(s => zm.Areas[zm.StartArea].Contains(s)).ToList();
        if (starts.Count == 0)
            starts = World.Map.Spawns;
        var at = starts[p.Id % starts.Count];
        p.Teleport(at);
        p.Yaw = 0;
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = at, Yaw = p.Yaw };
        if (zp.NeedsStartGear)
            GiveStartGear(p, quiet: _zRunMatch != MatchNumber);
        else
            KeepBuiltGuns(p);
    }

    /// <summary>
    /// Server: <paramref name="p"/> carries the Zombies grenade (<see cref="ZombiesRules.GrenadeKind"/>) with
    /// <paramref name="count"/> of them, or (<paramref name="topUp"/>) at least that many. Clients learn it from
    /// <see cref="Msg.ZWorld"/> (protocol 63).
    /// </summary>
    private void GiveZombiesGrenades(Player p, int count, bool topUp)
    {
        // A grenade built in the grenade slot replaces the stock one, and its parts may add to the round's count.
        var kind = p.GrenadeBuild != null ? ThrowableKind.Built : ZRules.GrenadeKind;
        count = Math.Clamp(count + (p.GrenadeBuild?.Extra ?? 0), 0, 255);
        if (p.Grenade != kind)
        {
            p.Grenade = kind;
            p.GrenadesLeft = 0;
        }
        p.GrenadesLeft = topUp ? Math.Max(p.GrenadesLeft, count) : count;
    }

    /// <summary>Server: <paramref name="p"/>'s grenade changed at a workbench (built, rebuilt or taken apart): they keep the grenades they carry, now of the new kind.</summary>
    private void SwapZombiesGrenade(Player p, GrenadeDef? build)
    {
        p.GrenadeBuild = build;
        p.Grenade = build != null ? ThrowableKind.Built : ZRules.GrenadeKind;
    }

    /// <summary>
    /// Server, a spawn that keeps the guns already given (the warmup's end respawns everyone): Spawn has just put the
    /// loadout's guns back in the primary and secondary ids (and bots draw a fresh multiplayer loadout every spawn), while
    /// the built guns stay in <see cref="Player.BuiltPrimary"/> and <see cref="Player.BuiltSecondary"/>. The ids go back
    /// to the built guns' ids, or <see cref="Player.WeaponDef"/> would be a gun the player doesn't
    /// <see cref="Player.Owns"/> and the server would drop every shot (bots never hit anything after a warmup, 2026-10-27).
    /// </summary>
    private static void KeepBuiltGuns(Player p)
    {
        if (p.BuiltPrimary is { } prim)
        {
            p.Primary = p.WantPrimary = prim.Id;
            p.PrimaryKit = p.WantPrimaryKit = 0;
        }
        if (p.BuiltSecondary is { } sec)
        {
            p.Secondary = p.WantSecondary = sec.Id;
            p.SecondaryKit = p.WantSecondaryKit = 0;
        }
        p.Weapon = p.Primary;
    }

    /// <summary>Server: the start builds ("start" in Parts/Builds): the guns in slots 1-2, the melee weapon on the melee key.</summary>
    private void GiveStartGear(Player p, bool quiet = false)
    {
        var zp = ZP(p);
        zp.NeedsStartGear = false;
        p.ExtraSlots = false;
        p.BuiltExtra[0] = p.BuiltExtra[1] = null;
        zp.Slots[2] = zp.Slots[3] = null;
        var starts = StandardBuilds.From("start").ToList();
        var guns = starts.Where(b => !b.IsMelee).OrderBy(b => AssembleCached(b.Build()).Def?.Class == WeaponClass.Pistol ? 1 : 0).ToList(); // a pistol goes second
        var slot = 0;
        foreach (var b in guns.Take(2))
            GiveBuild(p, slot++, b.Build(), full: true, start: true);
        while (slot < 2)
        {
            // Not enough start guns in the data: a stock gun fills the slot.
            var stock = slot == 0 ? Weapons.DefaultPrimary : Weapons.DefaultSecondary;
            if (slot == 0)
            {
                p.BuiltPrimary = null;
                p.Primary = p.WantPrimary = stock.Id;
            }
            else
            {
                p.BuiltSecondary = null;
                p.Secondary = p.WantSecondary = stock.Id;
            }
            slot++;
        }
        if (starts.FirstOrDefault(b => b.IsMelee) is { } melee)
            GiveBuild(p, 4, melee.Build(), full: true, start: true);
        if (TestKitOn)
            GiveTestKit(p); // every perk, 4 slots, strong guns (ZombiesStartRound.cs)
        p.Weapon = p.Primary;
        if (p.Bot is { } brain)
        {
            brain.Ammo = p.DefFor(p.Primary).Mag;
            brain.ReloadUntil = 0;
        }
        if (!quiet && p.Id == LocalId)
            LoadoutNotice = "Zombies: you start with the Wall Rifle, the Wall Pistol and a Machete. Earn points, buy better.";
    }

    /// <summary>Server, every tick for every player: bleeding out, self-revive, the interact key (buy, revive, rebuild), pickups.</summary>
    private void TickZPlayer(Player p, ZPlayer zp, ZombiesMap zm, double now)
    {
        if (!p.Alive)
        {
            zp.Downed = false;
            zp.UseHeld = false;
            return;
        }
        zp.Spawned = true;
        _zAnySpawned = true;
        zp.PlateReady = zp.Perks != 0 && PlateUp(zp, now); // (the HUD's plate badge; sent in ZWorld)
        if (zp.Downed)
        {
            zp.BleedFraction = (float)Math.Clamp((zp.BleedAt - now) / ZRules.BleedoutSeconds, 0, 1);
            if (zp.SelfReviveAt > 0 && now >= zp.SelfReviveAt)
            {
                Revive(p, null, now);
                return;
            }
            if (now >= zp.BleedAt)
                BleedOut(p, zp, now);
            return;
        }

        // Pickups: walk over a part to take it.
        for (var i = ZDrops.Count - 1; i >= 0; i--)
        {
            var d = ZDrops[i];
            var dx = d.At.X - p.Position.X;
            var dz = d.At.Z - p.Position.Z;
            if (dx * dx + dz * dz > ZRules.PickupRange * ZRules.PickupRange || MathF.Abs(d.At.Y - p.Position.Y) > 2f)
                continue;
            ZDrops.RemoveAt(i);
            zp.Parts[d.Part] = zp.Parts.GetValueOrDefault(d.Part) + 1;
            var part = Parts.Find(d.Part);
            ZFeedTo(p, $"Picked up a part: {d.Part} ({(part != null ? PartDef.RarityName(part.Rarity) : "?")}). Fit it at a workbench.", Sfx.HitTick);
            SendZRun(p);
        }

        if (!zp.UseHeld)
        {
            zp.Reviving = byte.MaxValue;
            zp.ReviveProgress = 0;
            zp.UseDone = false;
            return;
        }
        var use = FindZUse(p);
        if (use is not { } u || u.Kind == ZUseKind.None)
        {
            zp.Reviving = byte.MaxValue;
            zp.ReviveProgress = 0;
            return;
        }
        switch (u.Kind)
        {
            case ZUseKind.Revive:
            {
                if (zp.Reviving != u.Index)
                {
                    zp.Reviving = (byte)u.Index;
                    zp.ReviveProgress = 0;
                }
                var mult = PerkMult(zp, static d => d.Revive);
                zp.ReviveProgress += World.TickDt / MathF.Max(0.05f, ZRules.ReviveSeconds * mult);
                if (Players.TryGetValue((byte)u.Index, out var downed))
                {
                    ZP(downed).BeingRevived = zp.ReviveProgress;
                    if (zp.ReviveProgress >= 1f)
                    {
                        zp.ReviveProgress = 0;
                        zp.Reviving = byte.MaxValue;
                        zp.Revives++;
                        if (ZRules.RevivePoints > 0)
                            AddPoints(p, ZRules.RevivePoints);
                        Revive(downed, p, now);
                    }
                }
                break;
            }
            case ZUseKind.Repair:
                zp.Reviving = byte.MaxValue; // (moved off a revive: it starts again from nothing)
                zp.ReviveProgress = 0;
                if (now >= zp.RepairNext && ZBoards[u.Index] < zm.Windows[u.Index].Boards)
                {
                    zp.RepairNext = now + ZRules.RepairSeconds * PerkMult(zp, static d => d.Repair); // Hammerhand Ale: faster
                    ZBoards[u.Index]++;
                    var boardMult = PerkMult(zp, static d => d.RepairPoints); // Hammerhand Ale: more points a board, and a cap raised to match
                    var boardPoints = (int)MathF.Round(ZRules.RepairPoints * boardMult);
                    if (zp.RepairPointsThisRound + boardPoints <= (int)MathF.Round(ZRules.RepairCapPerRound * boardMult))
                    {
                        zp.RepairPointsThisRound += boardPoints;
                        EarnPoints(p, boardPoints, now); // (Double Points doubles boards too)
                    }
                    Audio.Effect3D(Sfx.ImpactWood, zm.Windows[u.Index].Inside + new Vector3(0, 1.2f, 0), 0.8f, MixBus.Zombies);
                }
                break;
            case ZUseKind.Workbench or ZUseKind.Casino or ZUseKind.Trader:
                zp.Reviving = byte.MaxValue;
                zp.ReviveProgress = 0;
                break; // the client opens the workbench (or the table's panel, or the trader's shelf) itself; its changes come back in ZBench (ZCasinoAct, ZTraderBuy)
            default:
                zp.Reviving = byte.MaxValue; // the downed player crawled out of reach: the revive is interrupted
                zp.ReviveProgress = 0;
                if (!zp.UseDone && now - zp.UseSince >= ZRules.BuyHoldSeconds)
                {
                    zp.UseDone = true;
                    if (!u.Blocked)
                        Buy(p, zp, zm, u, now);
                    else if (p.Id == LocalId)
                        LoadoutNotice = u.Text;
                }
                break;
        }
    }

    /// <summary>Server: the interact key went down or up for <paramref name="p"/>.</summary>
    public void ServerSetUse(Player p, bool held, double now)
    {
        var zp = ZP(p);
        if (held == zp.UseHeld)
            return;
        zp.UseHeld = held;
        zp.UseSince = now;
        zp.UseDone = false;
        zp.RepairNext = now + 0.15;
        if (!held)
        {
            zp.Reviving = byte.MaxValue;
            zp.ReviveProgress = 0;
        }
    }

    /// <summary>A perk effect multiplied over the perks held (1 with none). <paramref name="f"/> is a static lambda: no allocation.</summary>
    private float PerkMult(ZPlayer zp, Func<ZombiePerkDef, float> f)
    {
        var m = 1f;
        if (zp.Perks == 0)
            return m;
        for (var i = 0; i < ZRules.Perks.Count; i++)
            if (zp.Has(i))
                m *= f(ZRules.Perks[i]);
        return m;
    }

    /// <summary>A perk effect added up over the perks held (0 with none).</summary>
    private float PerkSum(ZPlayer zp, Func<ZombiePerkDef, float> f)
    {
        var m = 0f;
        if (zp.Perks == 0)
            return m;
        for (var i = 0; i < ZRules.Perks.Count; i++)
            if (zp.Has(i))
                m += f(ZRules.Perks[i]);
        return m;
    }

    /// <summary>A Zombies perk multiplier for <paramref name="p"/> (1 outside Zombies, or for a player with no run).</summary>
    private float ZPerkMult(Player? p, Func<ZombiePerkDef, float> f) => p != null && Mode == GameMode.Zombies && ZPlayers.TryGetValue(p.Id, out var z) ? PerkMult(z, f) : 1f;

    /// <summary>Reload time multiplier from Zombies perks (Swiftload Seltzer) for <paramref name="p"/> (1 outside Zombies).</summary>
    public float ZombieReloadMult(Player? p) => ZPerkMult(p, static d => d.Reload);

    /// <summary>Fire interval multiplier from Zombies perks (Twinfire Punch) for <paramref name="p"/> (1 outside Zombies).</summary>
    public float ZombieFireIntervalMult(Player? p) => 1f / ZPerkMult(p, static d => d.FireRate);

    /// <summary>
    /// Movement speed multiplier from Zombies perks (Fleetfoot Fizz) for <paramref name="p"/> (1 outside Zombies, and
    /// while downed: the crawl stays a crawl). The client's input, the bots' input and the server's speed check use it.
    /// </summary>
    public float ZombieMoveMult(Player? p) => p != null && Mode == GameMode.Zombies && ZPlayers.TryGetValue(p.Id, out var z) && !z.Downed ? (z.Held ? HeldMoveMult : PerkMult(z, static d => d.MoveSpeed)) : 1f;

    /// <summary>Held by a grabber: the movement speed left (a shuffle in its arms; the server's speed check and the client's prediction agree on it).</summary>
    public const float HeldMoveMult = 0.02f;

    /// <summary>Server: points for <paramref name="p"/> (the host's own +N popups come straight from here; clients see the change).</summary>
    public void AddPoints(Player p, int amount)
    {
        if (amount == 0)
            return;
        var zp = ZP(p);
        zp.Points = Math.Max(0, zp.Points + amount);
    }

    /// <summary>Server: a zombie's swipe landed on <paramref name="p"/>. False: it did nothing (immune, down, or an armour plate took it), so a type's special doesn't follow.</summary>
    public bool ZombieHurt(Player p, int damage, double now, Vector3? from = null)
    {
        var zp = ZP(p);
        if (!p.Alive || zp.Downed || !DamageEnabled || now < p.ImmuneUntil)
            return false;
        if (zp.Perks != 0 && PlateUp(zp, now))
        {
            // Bulwark Brew: the plate takes the whole hit and grows back later.
            zp.PlateReadyAt = now + PerkSum(zp, static d => d.PlateSeconds);
            zp.PlateReady = false;
            p.LastCombatTime = now;
            Audio.Effect3D(Sfx.BreakMetal, p.Position + new Vector3(0, 1.2f, 0), 0.7f, MixBus.Zombies);
            return false;
        }
        var dmg = Math.Max(1, (int)MathF.Round(damage * PerkMult(zp, static d => d.DamageTaken)));
        p.Health -= dmg;
        BloodOnSwipe(p, from ?? p.Position + World.FlatForward(p.Yaw)); // blood marks (BodyBlood.cs)
        p.LastHurtTime = now;
        p.LastCombatTime = now;
        if (p.Health <= 0)
            Down(p, zp, now);
        return true;
    }

    /// <summary>Server: <paramref name="zp"/> holds an armour plate perk and its plate is up.</summary>
    private bool PlateUp(ZPlayer zp, double now) => now >= zp.PlateReadyAt && PerkSum(zp, static d => d.PlateSeconds) > 0;

    private void Down(Player p, ZPlayer zp, double now)
    {
        // A self-revive perk (Second Wind) gets you back up on your own, solo or co-op, and is used up by it.
        var selfPerk = -1;
        for (var i = 0; i < ZRules.Perks.Count && selfPerk < 0; i++)
            if (ZRules.Perks[i].SelfRevive && zp.Has(i))
                selfPerk = i;
        var selfRevive = selfPerk >= 0;
        if (selfRevive)
            zp.Perks &= (ushort)~(1 << selfPerk); // consumed (even if perks were kept on down)
        zp.Downed = true;
        zp.Downs++;
        zp.DownedAt = now;
        zp.BleedAt = now + ZRules.BleedoutSeconds;
        zp.SelfReviveAt = selfRevive ? now + ZRules.SelfReviveSeconds : -1;
        if (ZRules.LosePerksOnDown)
            zp.Perks = 0; // every perk is lost when you go down
        zp.UseHeld = false;
        zp.BeingRevived = 0;
        p.Health = World.MaxHealth;
        ModeEvent(selfRevive ? $"{p.Name} is down! {ZRules.Perks[selfPerk].Name} gets them up in {ZRules.SelfReviveSeconds:0} seconds." : $"{p.Name} is down! Hold the interact key on them to revive.", Sfx.Death);
    }

    private void Revive(Player p, Player? by, double now)
    {
        var zp = ZP(p);
        zp.Downed = false;
        zp.SelfReviveAt = -1;
        zp.BeingRevived = 0;
        p.Health = World.MaxHealth;
        p.LastHurtTime = now;
        p.ImmuneUntil = Math.Max(p.ImmuneUntil, now + 1.0); // a moment to get up
        ModeEvent(by != null ? $"{by.Name} revived {p.Name}" : $"{p.Name} is back up", Sfx.Respawn);
    }

    private void BleedOut(Player p, ZPlayer zp, double now)
    {
        zp.Downed = false;
        zp.NeedsStartGear = true;
        zp.Points -= (int)(zp.Points * ZRules.BleedoutPointLoss);
        var dropped = ZRules.DropBuildsOnBleedout ? DropBuildsOnBleedout(p, zp, p.Position, now) : 0; // ZombiesDrops.cs
        p.Alive = false;
        p.Health = 0;
        p.Deaths++;
        p.RespawnAt = double.MaxValue; // back when the round ends (EndRound; during the break: when the next one starts)
        p.WantsRespawn = false;
        zp.RoundReady = false;
        var when = ZPhase == ZRoundPhase.Round ? "when this round ends" : "when the next round starts";
        ModeEvent(dropped > 0
            ? $"{p.Name} bled out. They'll be back {when}; their {(dropped == 1 ? "weapon is" : $"{dropped} weapons are")} on the floor where they went down for {ZRules.BuildDropSeconds / 60:0.#} minutes."
            : $"{p.Name} bled out. They'll be back {when}.", Sfx.Death);
    }

    // ---- The interact key ----------------------------------------------------------------------------

    internal const float UseRange = 1.7f;

    /// <summary>A wall buy's height band about its chalk (<see cref="ZWallBuy.At"/>, usually 1.3 m up the wall): down to the floor, a little over the top.</summary>
    internal const float WallBuyBandLo = -1.3f, WallBuyBandHi = 0.4f;

    /// <summary>
    /// What the interact key would do for <paramref name="p"/> right now (both sides: the client shows the prompt, the
    /// server does it). Reviving a downed teammate comes first, then the nearest buyable in reach.
    /// </summary>
    public ZUse? FindZUse(Player p)
    {
        if (ZMap is not { } zm || !p.Alive || IsDowned(p) || Phase != MatchPhase.Playing)
            return null;
        var pos = p.Position;
        var zp = ZP(p);
        var solo = Players.Count <= 1;

        // A downed teammate in reach.
        foreach (var o in Players.Values)
        {
            if (o == p || !o.Alive || !IsDowned(o))
                continue;
            if (Flat(o.Position - pos) <= ZRules.ReviveRange && ZReachSpot(p, o.Position))
                return new ZUse(ZUseKind.Revive, o.Id, 0, $"Hold [E] to revive {o.Name}");
        }

        ZUse? best = null;
        var bestD = float.MaxValue;
        void Consider(float d, float range, ZUse u)
        {
            if (d <= range && d < bestD)
            {
                bestD = d;
                best = u;
            }
        }
        // Every thing below also has to be in reach in 3D and in sight (ZombiesReach.cs); the cheap flat range goes first.
        bool Near(float d, float range, Vector3 at) => d <= range && d < bestD && ZReachSpot(p, at);

        for (var i = 0; i < zm.Doors.Count; i++)
        {
            if (World.Map.IsDoorOpen(i))
                continue;
            var door = zm.Doors[i];
            var closest = Vector3.Clamp(pos + new Vector3(0, 1, 0), door.Box.Min, door.Box.Max);
            var d = Flat(closest - pos);
            if (d > 1.9f || d >= bestD || !ZReach(p, closest with { Y = door.Box.Min.Y }, 0, door.Box.Max.Y - door.Box.Min.Y))
                continue;
            var what = door.Debris ? "clear the debris" : "open the door";
            Consider(d, 1.9f, door.NeedsPower && !ZPower
                ? new ZUse(ZUseKind.Door, i, door.Cost, $"{door.Name}: needs the power on", Blocked: true)
                : new ZUse(ZUseKind.Door, i, door.Cost, $"Hold [E] to {what} ({door.Name}) [{door.Cost}]"));
        }
        for (var i = 0; i < zm.WallBuys.Count; i++)
        {
            var wb = zm.WallBuys[i];
            var d = Flat(wb.At - pos);
            if (d > UseRange || d >= bestD || !ZReach(p, wb.At, WallBuyBandLo, WallBuyBandHi))
                continue;
            var cost = WallCost(wb);
            var owned = OwnsBuild(p, wb.Build) >= 0;
            if (owned)
            {
                var ammo = WallAmmoCost(wb);
                Consider(d, UseRange, new ZUse(ZUseKind.WallAmmo, i, ammo, $"Hold [E] to buy ammo for the {wb.Build} [{ammo}]"));
            }
            else
                Consider(d, UseRange, new ZUse(ZUseKind.WallBuy, i, cost, $"Hold [E] to buy the {wb.Build} [{cost}]"));
        }
        if (zm.BoxSpots.Count > 0)
        {
            var spot = zm.BoxSpots[Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1)];
            var d = Flat(spot.At - pos);
            var cost = BoxCost(zm);
            if (!Near(d, 1.9f, spot.At))
                d = float.MaxValue; // (out of reach: nothing below considers it)
            switch (BoxState)
            {
                case ZBoxState.Idle:
                    Consider(d, 1.9f, new ZUse(ZUseKind.Box, BoxSpot, cost, $"Hold [E] to spin the mystery box [{cost}]"));
                    break;
                case ZBoxState.Offering when BoxBuild >= 0 && BoxBuild < StandardBuilds.All.Length:
                {
                    // The buyer's alone at first; the last part of the offer is anyone's (board item 2026-10-27).
                    var name = StandardBuilds.All[BoxBuild].Name;
                    if (BoxOwner == p.Id)
                        Consider(d, 1.9f, new ZUse(ZUseKind.BoxTake, BoxSpot, 0, BoxFreeForAll ? $"Hold [E] to take the {name} (free for all now)" : $"Hold [E] to take the {name}"));
                    else if (BoxFreeForAll)
                        Consider(d, 1.9f, new ZUse(ZUseKind.BoxTake, BoxSpot, 0, $"Free for all - hold [E] to take the {name}"));
                    else
                        Consider(d, 1.9f, new ZUse(ZUseKind.BoxTake, BoxSpot, 0, $"Reserved for {NameOf(BoxOwner)}: the {name}", Blocked: true));
                    break;
                }
            }
        }
        for (var i = 0; i < zm.PerkMachines.Count; i++)
        {
            var m = zm.PerkMachines[i];
            var d = Flat(m.At - pos);
            if (!Near(d, UseRange, m.At))
                continue;
            var k = ZRules.PerkIndex(m.Perk);
            if (k < 0)
                continue;
            var perk = ZRules.Perks[k];
            var cost = ZRules.PerkPrice(m.Cost > 0 ? m.Cost : perk.Price(solo), PerkCount(zp));
            if (m.NeedsPower && !ZPower)
                Consider(d, UseRange, new ZUse(ZUseKind.Perk, i, cost, $"{perk.Name}: needs the power on", Blocked: true));
            else if (zp.Has(k))
                Consider(d, UseRange, new ZUse(ZUseKind.Perk, i, cost, $"You have {perk.Name}", Blocked: true));
            else if (PerkCount(zp) >= ZRules.MaxPerks)
                Consider(d, UseRange, new ZUse(ZUseKind.Perk, i, cost, $"You can't carry more than {ZRules.MaxPerks} perks", Blocked: true));
            else
            {
                var held = PerkCount(zp);
                var extra = ZRules.FullPricePerks > 0 && held >= ZRules.FullPricePerks
                    ? $" ({Ordinal(held + 1)} perk: x{ZRules.PerkPrice(1000, held) / 1000f:0.#})" : "";
                Consider(d, UseRange, new ZUse(ZUseKind.Perk, i, cost, $"Hold [E] to buy {perk.Name} [{cost}]{extra}: {perk.Blurb}"));
            }
        }
        if (zm.SlotUpgrade is { } su)
        {
            var d = Flat(su.At - pos);
            var cost = SlotCost(zm);
            if (!Near(d, UseRange, su.At))
                d = float.MaxValue;
            if (p.ExtraSlots)
                Consider(d, UseRange, new ZUse(ZUseKind.SlotUpgrade, 0, cost, "You have 4 weapon slots", Blocked: true));
            else if (su.NeedsPower && !ZPower)
                Consider(d, UseRange, new ZUse(ZUseKind.SlotUpgrade, 0, cost, "Weapon slots: needs the power on", Blocked: true));
            else
                Consider(d, UseRange, new ZUse(ZUseKind.SlotUpgrade, 0, cost, $"Hold [E] for 2 more weapon slots [{cost}]"));
        }
        if (zm.Power is { } pw && !ZPower && Near(Flat(pw.At - pos), UseRange, pw.At))
            Consider(Flat(pw.At - pos), UseRange, new ZUse(ZUseKind.Power, 0, 0, "Hold [E] to turn on the power"));
        for (var i = 0; i < zm.SlotMachines.Count && i < ZSlots.Length; i++)
        {
            var d = Flat(zm.SlotMachines[i].At - pos);
            if (Near(d, UseRange, zm.SlotMachines[i].At))
                Consider(d, UseRange, SlotUse(zm, i)); // ZombiesSlots.cs
        }
        for (var i = 0; i < zm.CasinoTables.Count && i < ZCasino.Length; i++)
        {
            var d = Flat(zm.CasinoTables[i].At - pos);
            if (Near(d, ZRules.CasinoUseRange, zm.CasinoTables[i].At))
                Consider(d, ZRules.CasinoUseRange, CasinoUse(zm, i)); // ZombiesCasino.cs
        }
        for (var i = 0; i < zm.Benches.Count; i++)
            if (Near(Flat(zm.Benches[i].At - pos), 1.9f, zm.Benches[i].At))
                Consider(Flat(zm.Benches[i].At - pos), 1.9f, new ZUse(ZUseKind.Workbench, i, 0, "Press [E] to use the workbench (your parts)"));
        if (ZTrader.Here && Near(Flat(ZTrader.At - pos), ZRules.TraderUseRange, ZTrader.At))
            Consider(Flat(ZTrader.At - pos), ZRules.TraderUseRange, TraderUse()); // ZombiesTrader.cs
        for (var i = 0; i < zm.Salvage.Count && i < _salvageRound.Length; i++)
        {
            var s = zm.Salvage[i];
            if (!Near(Flat(s.At - pos), UseRange, s.At))
                continue;
            Consider(Flat(s.At - pos), UseRange, _salvageRound[i] == ZRound
                ? new ZUse(ZUseKind.Salvage, i, 0, "Picked clean this round", Blocked: true)
                : new ZUse(ZUseKind.Salvage, i, 0, ZRules.SalvageParts == 1 ? "Hold [E] to search the pile for a part" : $"Hold [E] to search the pile for parts (+{ZRules.SalvageParts})"));
        }
        if (p.Bot == null) // (co-op bots never pick anything up)
        {
            // Pickups: the nearest, but one you look at comes first in a pile (ZombiesDropItems.cs).
            foreach (var wd in ZWeaponDrops)
            {
                var d = Flat(wd.At - pos);
                if (d <= UseRange && MathF.Abs(wd.At.Y - pos.Y) < 1.5f && PickupRank(p, wd.At, d) is var rank && rank < bestD && ZReachSpot(p, wd.At))
                    Consider(rank, UseRange, WeaponDropUse(p, wd)); // ZombiesDrops.cs
            }
            foreach (var pd in ZPartDrops)
            {
                var d = Flat(pd.At - pos);
                if (d <= UseRange && MathF.Abs(pd.At.Y - pos.Y) < 1.5f && PickupRank(p, pd.At, d) is var rank && rank < bestD && ZReachSpot(p, pd.At))
                    Consider(rank, UseRange, PartDropUse(p, pd));
            }
        }
        for (var i = 0; i < zm.Windows.Count; i++)
        {
            var w = zm.Windows[i];
            if (i < ZBoards.Length && ZBoards[i] < w.Boards && Near(Flat(w.Inside - pos) + 0.3f, 1.6f, w.Inside))
                Consider(Flat(w.Inside - pos) + 0.3f, 1.6f, new ZUse(ZUseKind.Repair, i, 0, "Hold [E] to rebuild the barrier"));
        }
        if (best is { Blocked: false } b && LogicMachineOf(b.Kind) is var (lk, lm) && LogicLocksZ(lk, lm, b.Index))
            return b with { Text = "Locked", Blocked = true }; // switched off by map logic (a disabled zdoor or zmachine, GameLogic.cs)
        return best;
    }


    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    public static int PerkCount(ZPlayer zp) => System.Numerics.BitOperations.PopCount(zp.Perks);

    public int WallCost(ZWallBuy wb) => wb.Cost > 0 ? wb.Cost : RunTables.Current.WallCost;

    public int WallAmmoCost(ZWallBuy wb) => wb.AmmoCost > 0 ? wb.AmmoCost : Math.Max(1, (int)MathF.Round(WallCost(wb) * ZRules.AmmoCostFraction / 10f) * 10);

    public int BoxCost(ZombiesMap zm) => zm.BoxCost > 0 ? zm.BoxCost : RunTables.Current.BoxCost;

    public int SlotCost(ZombiesMap zm) => zm.SlotUpgrade is { Cost: > 0 } su ? su.Cost : RunTables.Current.SlotUpgradeCost;

    /// <summary>Server: pays for and does a buy.</summary>
    private void Buy(Player p, ZPlayer zp, ZombiesMap zm, ZUse u, double now)
    {
        if (zp.Points < u.Cost)
        {
            if (p.Id == LocalId)
                LoadoutNotice = $"Not enough points ({zp.Points} of {u.Cost}).";
            Audio.Effect(Sfx.DryFire, 0.5f);
            return;
        }
        switch (u.Kind)
        {
            case ZUseKind.Door:
                zp.Points -= u.Cost;
                OpenDoor(u.Index, p);
                break;
            case ZUseKind.WallBuy when StandardBuilds.Find(zm.WallBuys[u.Index].Build) is { } sb:
                zp.Points -= u.Cost;
                GiveBuild(p, TargetSlot(p), sb.Build(), full: true);
                ZFeedTo(p, $"Bought the {sb.Name}.", Sfx.LevelUp);
                break;
            case ZUseKind.WallAmmo:
            {
                var slot = OwnsBuild(p, zm.WallBuys[u.Index].Build);
                if (slot < 0)
                    return;
                zp.Points -= u.Cost;
                RefillAmmo(p, slot);
                ZFeedTo(p, $"Ammo for the {zm.WallBuys[u.Index].Build}.", Sfx.HitTick);
                break;
            }
            case ZUseKind.Box:
                zp.Points -= u.Cost;
                SpinBox(p, zm, now);
                break;
            case ZUseKind.BoxTake when CanTakeBox(p): // (server-authoritative: the first valid take wins, the box is closed for the rest)
            {
                var ob = StandardBuilds.All[BoxBuild];
                var owner = BoxOwner;
                GiveBuild(p, ob.IsMelee ? 4 : TargetSlot(p), ob.Build(), full: true);
                ZFeedTo(p, $"You took the {ob.Name} ({PartDef.RarityName(ob.Tier)}).", Sfx.LevelUp);
                if (owner != p.Id && Players.TryGetValue(owner, out var buyer))
                    ZFeedTo(buyer, $"{p.Name} took the {ob.Name} from your box spin.", Sfx.HitTick);
                CloseBox();
                break;
            }
            case ZUseKind.Perk:
            {
                var k = ZRules.PerkIndex(zm.PerkMachines[u.Index].Perk);
                zp.Points -= u.Cost;
                zp.Perks |= (ushort)(1 << k);
                if (ZRules.Perks[k].PlateSeconds > 0)
                    zp.PlateReadyAt = now; // the plate is up at once
                ModeEvent($"{p.Name} drank {ZRules.Perks[k].Name}", Sfx.LevelUp);
                break;
            }
            case ZUseKind.SlotUpgrade:
                zp.Points -= u.Cost;
                ServerBuySlots(p);
                ModeEvent($"{p.Name} bought 2 more weapon slots", Sfx.LevelUp);
                break;
            case ZUseKind.Power:
                ZPower = true;
                ModeEvent($"{p.Name} turned on the power!", Sfx.StreakReady);
                ZBanner("THE POWER IS ON");
                LogicZombies(LogicEvent.Power, 0, p); // map logic: zombies.power (GameLogic.cs)
                break;
            case ZUseKind.Pickup:
                if (p.Bot == null)
                    ClaimWeaponDrop(p, u.Index, now); // ZombiesDrops.cs
                break;
            case ZUseKind.PartPickup:
                ClaimPartDrop(p, u.Index); // ZombiesDropItems.cs (first claim wins)
                break;
            case ZUseKind.SlotMachine when u.Index >= 0 && u.Index < zm.SlotMachines.Count && ZSlots[u.Index].State == ZSlotState.Idle:
                zp.Points -= u.Cost;
                PullSlot(p, zp, zm, u.Index, u.Cost, now); // ZombiesSlots.cs
                break;
            case ZUseKind.Salvage:
            {
                _salvageRound[u.Index] = ZRound;
                SalvagePile(p, zp, ZRound);
                break;
            }
        }
        if (u.Kind != ZUseKind.Door && LogicMachineOf(u.Kind) is (LogicKind.ZMachine, var machine))
            LogicZMachineUsed(machine, u.Index, p); // map logic: a zmachine's "used" (GameLogic.cs)
    }

    /// <summary>The map logic entity kind (and machine) a Zombies use belongs to, or null.</summary>
    private static (LogicKind, LogicMachine)? LogicMachineOf(ZUseKind k) => k switch
    {
        ZUseKind.Door => (LogicKind.ZDoor, LogicMachine.Power),
        ZUseKind.Power => (LogicKind.ZMachine, LogicMachine.Power),
        ZUseKind.Box => (LogicKind.ZMachine, LogicMachine.Box),
        ZUseKind.Perk => (LogicKind.ZMachine, LogicMachine.Perk),
        ZUseKind.SlotUpgrade => (LogicKind.ZMachine, LogicMachine.SlotUpgrade),
        ZUseKind.SlotMachine => (LogicKind.ZMachine, LogicMachine.SlotMachine),
        ZUseKind.WallBuy or ZUseKind.WallAmmo => (LogicKind.ZMachine, LogicMachine.WallBuy),
        ZUseKind.Workbench => (LogicKind.ZMachine, LogicMachine.Bench),
        ZUseKind.Salvage => (LogicKind.ZMachine, LogicMachine.Salvage),
        ZUseKind.Casino => (LogicKind.ZMachine, LogicMachine.Casino),
        _ => null,
    };

    /// <summary>Server: opens a door (unlocking the area behind it: its spawners and routes go live).</summary>
    public void OpenDoor(int door, Player? by)
    {
        if (World.Map.Zombies is not { } zm || World.Map.IsDoorOpen(door))
            return;
        World.Map.SetDoorOpen(door, true);
        var d = zm.Doors[door];
        ModeEvent(by != null ? $"{by.Name} opened {d.Name}: {zm.Areas[d.AreaA].Name} - {zm.Areas[d.AreaB].Name}" : $"{d.Name} opened", d.Debris ? Sfx.BreakWood : Sfx.BreakMetal);
        ZDoorOpenedAt[door] = Brutal.GlfwApi.Glfw.Time;
        LogicZDoorOpened(door, by); // map logic: a zdoor's "opened" (GameLogic.cs)
    }

    /// <summary>When each door opened (client clock), for the opening animation.</summary>
    public readonly double[] ZDoorOpenedAt = new double[ZombiesMap.MaxDoors];

    // ---- Weapons ------------------------------------------------------------------------------------------

    /// <summary>The slot holding a build called <paramref name="name"/> (0-3), or -1.</summary>
    public static int OwnsBuild(Player p, string name)
    {
        bool Is(WeaponDef? d) => d != null && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        if (Is(p.BuiltPrimary))
            return 0;
        if (Is(p.BuiltSecondary))
            return 1;
        if (p.ExtraSlots && Is(p.BuiltExtra[0]))
            return 2;
        if (p.ExtraSlots && Is(p.BuiltExtra[1]))
            return 3;
        return -1;
    }

    /// <summary>The slot a new gun goes to: an empty slot (1-2 taken apart at a workbench, or 3-4 if you have them), else the one in your hands.</summary>
    public static int TargetSlot(Player p)
    {
        if (FreeGunSlot(p) is var free and >= 0)
            return free;
        return SlotOf(p, p.Weapon) is var s and >= 0 ? s : 0;
    }

    /// <summary>Which slot (0-3) weapon id <paramref name="weapon"/> is in for <paramref name="p"/>, or -1.</summary>
    public static int SlotOf(Player p, byte weapon)
    {
        if (weapon == p.Primary)
            return 0;
        if (weapon == p.Secondary)
            return 1;
        if (p.ExtraSlots && p.BuiltExtra[0]?.Id == weapon)
            return 2;
        if (p.ExtraSlots && p.BuiltExtra[1]?.Id == weapon)
            return 3;
        return -1;
    }

    private static readonly Dictionary<string, AssembledWeapon> AssembledCache = new();

    /// <summary>Drops every cached assembled weapon (after a part hot reload: the same text now assembles differently).</summary>
    internal static void ForgetAssembled()
    {
        lock (AssembledCache)
            AssembledCache.Clear();
    }

    /// <summary>A build assembled (cached by its text: the same text always gives the same weapon, on every PC).</summary>
    public static AssembledWeapon AssembleCached(WeaponBuild b)
    {
        var key = b.ToText();
        if (!AssembledCache.TryGetValue(key, out var a))
        {
            if (AssembledCache.Count > 256)
                AssembledCache.Clear();
            AssembledCache[key] = a = WeaponBuilder.Assemble(b);
        }
        return a;
    }

    /// <summary>
    /// Server: puts build <paramref name="b"/> in <paramref name="slot"/> of <paramref name="p"/> (0-3 guns, 4 the melee
    /// weapon) and tells everyone (<see cref="Msg.ZBuild"/>: clients assemble the same text into the same weapon). Each
    /// gun in a player's slots gets its own weapon id (built guns borrow a shipped gun's id for their sounds).
    /// <paramref name="full"/>: a full magazine and full reserve; otherwise the reserve stays as it was (workbench edits).
    /// </summary>
    public bool GiveBuild(Player p, int slot, WeaponBuild b, bool full, bool start = false)
    {
        var a = AssembleCached(b);
        var zp = ZP(p);
        if (slot == 4)
        {
            if (a.Melee == null)
                return false;
            p.MeleeBuild = a.Melee;
            zp.Slots[4] = b.Clone();
            BroadcastBuild(p, 4, 0, b, full, start);
            return true;
        }
        if (slot == GrenadeSlot)
        {
            if (a.Grenade == null)
                return false;
            SwapZombiesGrenade(p, a.Grenade);
            zp.Slots[GrenadeSlot] = b.Clone();
            BroadcastBuild(p, GrenadeSlot, 0, b, full, start);
            return true;
        }
        if (a.Def == null || slot is < 0 or > 3 || (slot >= 2 && !p.ExtraSlots))
            return false;
        var def = PlaceBuilt(p, slot, a.Def);
        zp.Slots[slot] = b.Clone();
        p.Weapon = def.Id;
        if (p.Bot is { } brain)
        {
            brain.Ammo = def.Mag;
            brain.ReloadUntil = 0;
        }
        BroadcastBuild(p, slot, def.Id, b, full, start);
        _zBuildsGiven = true;
        return true;
    }

    /// <summary>Both sides: puts a copy of <paramref name="built"/> in a player's slot with an id none of their other slots use.</summary>
    private static WeaponDef PlaceBuilt(Player p, int slot, WeaponDef built, byte? id = null)
    {
        var used = new HashSet<byte>();
        if (slot != 0)
            used.Add(p.Primary);
        if (slot != 1)
            used.Add(p.Secondary);
        if (slot != 2 && p.BuiltExtra[0] != null)
            used.Add(p.BuiltExtra[0]!.Id);
        if (slot != 3 && p.BuiltExtra[1] != null)
            used.Add(p.BuiltExtra[1]!.Id);
        var def = built.Clone();
        def.Id = id ?? (used.Contains(built.Id) ? Weapons.All.First(w => !used.Contains(w.Id)).Id : built.Id);
        switch (slot)
        {
            case 0:
                p.BuiltPrimary = def;
                p.Primary = p.WantPrimary = def.Id;
                p.PrimaryKit = p.WantPrimaryKit = 0;
                break;
            case 1:
                p.BuiltSecondary = def;
                p.Secondary = p.WantSecondary = def.Id;
                p.SecondaryKit = p.WantSecondaryKit = 0;
                break;
            default:
                p.BuiltExtra[slot - 2] = def;
                break;
        }
        return def;
    }

    /// <summary>The local player's slot <paramref name="slot"/> now holds <paramref name="def"/> (in hand straight away).</summary>
    private void LocalSetSlot(int slot, WeaponDef def, bool full, bool start, bool select)
    {
        var old = Slots[slot];
        var mags = start ? ZRules.StartReserveMags : ZRules.ReserveMags;
        Slots[slot] = new WeaponState(def) { Reserve = full ? def.Mag * mags : Math.Max(0, old.Reserve) };
        if (!select)
            return;
        var now = Brutal.GlfwApi.Glfw.Time;
        ActiveSlot = slot;
        EquipUntil = now + def.EquipTime * FrenzyBuffs.Value(Local, def, FrenzyStat.EquipTime); // the Pistol frenzy buff
        NextFireTime = Math.Max(NextFireTime, EquipUntil);
        Audio.Equip(def);
    }

    /// <summary>Server: full ammo for the gun in <paramref name="slot"/> (wall ammo).</summary>
    private void RefillAmmo(Player p, int slot)
    {
        if (p.Bot is { } b)
            b.Ammo = p.DefFor(p.Weapon).Mag;
        if (p.Id == LocalId)
            LocalRefill(slot);
        else
            SendZEvent(p, ZEventKind.Refill, (ushort)slot, "");
    }

    private void LocalRefill(int slot)
    {
        var w = Slots[Math.Clamp(slot, 0, 3)];
        w.Ammo = w.Def.Mag;
        w.Reserve = w.Def.Mag * ZRules.ReserveMags;
        Audio.Reload(w.Def, null);
    }

    // ---- The mystery box -----------------------------------------------------------------------------

    /// <summary>
    /// Both sides: is the box's offer open to everyone now? The last <see cref="ZombiesRules.BoxFreeSeconds"/> of the offer
    /// (board item 2026-10-27), or at once when its buyer has left. Clients work it out from the replicated
    /// <see cref="BoxLeft"/> (no wire change).
    /// </summary>
    public bool BoxFreeForAll => BoxState == ZBoxState.Offering && BoxBuild >= 0 && BoxBuild < StandardBuilds.All.Length
                                 && ((ZRules.BoxFreeSeconds > 0 && BoxLeft <= ZRules.BoxFreeSeconds + 1e-4) || !Players.ContainsKey(BoxOwner));

    /// <summary>Both sides: may <paramref name="p"/> take the box's offer now (its buyer, or anyone in the free-for-all window)?</summary>
    public bool CanTakeBox(Player p) => BoxState == ZBoxState.Offering && BoxBuild >= 0 && BoxBuild < StandardBuilds.All.Length && (BoxOwner == p.Id || BoxFreeForAll);

    /// <summary>
    /// Server, just before the offer goes (taken, taken apart or timed out): everyone else still holding the key on the box
    /// has that hold used up, so the closed box doesn't turn their hold into a spin they didn't ask for (let go, press again).
    /// </summary>
    private void SpendBoxHolds()
    {
        if (BoxState != ZBoxState.Offering)
            return;
        foreach (var o in Players.Values)
            if (ZPlayers.TryGetValue(o.Id, out var oz) && oz.UseHeld && !oz.UseDone && FindZUse(o) is { Kind: ZUseKind.BoxTake })
                oz.UseDone = true;
    }

    /// <summary>Server: counts box spins this run (each offer its own number; bots remember which free offers they let go).</summary>
    public int BoxOfferSeq { get; private set; }

    private void SpinBox(Player p, ZombiesMap zm, double now)
    {
        BoxOfferSeq++;
        BoxUses++;
        BoxOwner = p.Id;
        BoxState = ZBoxState.Spinning;
        _boxUntil = now + ZRules.BoxSpinSeconds;
        if (BoxUses > _boxMoveAfter && zm.BoxSpots.Count > 1)
            BoxBuild = -2; // the teddy bear (shown when the spin ends)
        else
            BoxBuild = RollBoxBuild(p, ZRound, ZNext());
        Audio.Effect3D(Sfx.StreakCall, zm.BoxSpots[BoxSpot].At, 0.8f);
    }

    /// <summary>
    /// A complete build from the mystery box: its tier rolled from DropTables.json's "boxTiers" for the round, then one of
    /// the "box" builds of that tier the player doesn't already carry (the nearest tier with one, if that tier has none).
    /// Returns an index into <see cref="StandardBuilds.All"/>.
    /// </summary>
    public int RollBoxBuild(Player? p, int round, uint seed)
    {
        var all = StandardBuilds.All;
        var box = Enumerable.Range(0, all.Length).Where(i => all[i].Sources.Contains("box")).ToList(); // guns only: the loader refuses a melee "box" build (2026-10-26)
        if (box.Count == 0)
            return -1;
        var tier = RunTables.Roll(RunTables.Current.BoxTiers, Math.Max(1, round), seed);
        var notOwned = box.Where(i => p == null || OwnsBuild(p, all[i].Name) < 0).ToList();
        if (notOwned.Count == 0)
            notOwned = box;
        for (var step = 0; step < 6; step++)
        {
            foreach (var t in step == 0 ? new[] { (int)tier } : new[] { (int)tier - step, (int)tier + step })
            {
                var pool = notOwned.Where(i => (int)all[i].Tier == t).ToList();
                if (pool.Count > 0)
                    return pool[(int)((seed >> 7) % (uint)pool.Count)];
            }
        }
        return notOwned[(int)(seed % (uint)notOwned.Count)];
    }

    private void TickBox(ZombiesMap zm, double now)
    {
        if (zm.BoxSpots.Count == 0)
            return;
        switch (BoxState)
        {
            case ZBoxState.Spinning when now >= _boxUntil:
                if (BoxBuild == -2)
                {
                    BoxState = ZBoxState.Teddy;
                    _boxUntil = now + ZRules.BoxMoveSeconds;
                    if (Players.TryGetValue(BoxOwner, out var owner))
                        AddPoints(owner, BoxCost(zm)); // the teddy bear refunds the spin
                    ModeEvent("The teddy bear! The mystery box is moving (your points are refunded).", Sfx.JetFlyby);
                }
                else
                {
                    BoxState = ZBoxState.Offering;
                    _boxUntil = now + ZRules.BoxOfferSeconds;
                }
                break;
            case ZBoxState.Offering when now >= _boxUntil:
                CloseBox();
                break;
            case ZBoxState.Teddy when now >= _boxUntil:
            {
                var next = ZRange(zm.BoxSpots.Count - 1);
                BoxSpot = next >= BoxSpot ? next + 1 : next;
                BoxSpot = Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1);
                BoxState = ZBoxState.Idle;
                BoxBuild = -1;
                BoxOwner = byte.MaxValue;
                BoxUses = 0;
                _boxMoveAfter = ZRules.BoxMoveMin + ZRange(ZRules.BoxMoveMax - ZRules.BoxMoveMin + 1);
                ModeEvent($"The mystery box is now in {zm.Areas[zm.BoxSpots[BoxSpot].Area].Name}.", Sfx.ReconBeep);
                break;
            }
        }
        BoxLeft = BoxState == ZBoxState.Idle ? 0 : Math.Max(0, _boxUntil - now);
    }

    // ---- Parts: drops and the workbench ------------------------------------------------------------------

    /// <summary>Server: a zombie died at <paramref name="at"/> in round <paramref name="round"/>: maybe a part drops (DropTables.json).</summary>
    private void RollDrop(Vector3 at, int round, double now, float chanceMult = 1f)
    {
        var tables = RunTables.Current;
        var chance = (RunTables.RowFor(tables.DropChance, round)?.Chance ?? 0) * chanceMult; // Salvage Stout: the killer's perks
        if (ZRand() >= chance)
            return;
        var tier = RunTables.Roll(tables.PartDrops, round, ZNext());
        var pool = Parts.All.Where(p => p.Rarity == tier).ToList();
        if (pool.Count == 0)
            return;
        var part = pool[ZRange(pool.Count)];
        if (ZDrops.Count >= 32)
            ZDrops.RemoveAt(0);
        ZDrops.Add(new ZDrop { Id = _zNextDrop++, Part = part.Name, At = at + new Vector3(0, 0.15f, 0), Until = now + ZRules.DropSeconds });
    }

    private void TickDrops(double now)
    {
        for (var i = ZDrops.Count - 1; i >= 0; i--) // (a loop: RemoveAll with a lambda allocated a closure every tick)
            if (ZDrops[i].Until <= now)
                ZDrops.RemoveAt(i);
    }

    /// <summary>
    /// Server: <paramref name="p"/> searched a salvage pile: <see cref="ZombiesRules.SalvageParts"/> loose parts, each
    /// rolled like a zombie's part drop for <paramref name="round"/> (DropTables.json "partDrops"). Piles gave scrap until
    /// 2026-10-27; with scrap gone they give the parts scrap used to craft.
    /// </summary>
    private void SalvagePile(Player p, ZPlayer zp, int round)
    {
        var got = 0;
        PartDef? last = null;
        var count = ZRules.SalvageParts + (int)PerkSum(zp, static d => d.SalvageBonus); // Salvage Stout: a part more
        for (var k = 0; k < count; k++)
        {
            var i = PickSlotPart(RunTables.Roll(RunTables.Current.PartDrops, round, ZNext()), ZNext());
            if (i < 0)
                continue;
            last = Parts.All[i];
            zp.Parts[last.Name] = zp.Parts.GetValueOrDefault(last.Name) + 1;
            got++;
        }
        if (last == null)
        {
            ZFeedTo(p, "Nothing useful in the pile.", Sfx.BreakMetal);
            return;
        }
        ZFeedTo(p, got == 1 ? $"Salvaged a part: {last.Name} ({PartDef.RarityName(last.Rarity)}). Fit it at a workbench." : $"Salvaged {got} parts. Fit them at a workbench.", Sfx.BreakMetal);
        SendZRun(p);
    }

    /// <summary>
    /// Server: a workbench change from <paramref name="p"/> (in <see cref="Msg.ZBench"/>, or the host's own): 0 = put
    /// <paramref name="text"/> in slot <paramref name="slot"/> (the parts it adds must be in their loose parts; parts it
    /// drops go back there), 3 = take the build in
    /// <paramref name="slot"/> apart into your loose parts (<see cref="BenchDisassemble"/>), 5 = at the mystery box, take
    /// its offered build apart (<see cref="BenchBoxDisassemble"/>). Kinds 1 (break a part down for scrap) and 2 (craft a
    /// part with scrap) went with scrap (2026-10-27) and are refused. Returns why not, or null.
    /// </summary>
    public string? ServerBench(Player p, int kind, int slot, string text)
    {
        if (ZMap is not { } zm || !p.Alive || IsDowned(p))
            return "Not now.";
        if (kind == BenchSlotBet)
            return ServerSlotBet(p, slot); // a slot machine's bet (ZombiesSlots.cs), not a workbench change
        if (kind == BenchBoxDisassemble)
            return ServerBoxDisassemble(p, ZP(p)); // the mystery box's OFFERED build, at the box (ZombiesDisassemble.cs)
        if (!AtWorkbench(p))
            return "You're not at a workbench.";
        var zp = ZP(p);
        switch (kind)
        {
            case 0:
            {
                if (slot is < 0 or >= BuildSlots || (slot is 2 or 3 && !p.ExtraSlots))
                    return "That slot isn't yours.";
                WeaponBuild nb;
                var dropped = new List<string>();
                try
                {
                    nb = WeaponBuild.Parse(text, Parts.Find, dropped, migrate: false); // clients send current builds: no stock actions added here
                }
                catch (FormatException ex)
                {
                    return $"That isn't a build: {ex.Message}";
                }
                if (dropped.Count > 0)
                    return $"Unknown parts: {string.Join(", ", dropped)}";
                var asm = AssembleCached(nb);
                if (!asm.Ok || SlotKind(slot) != nb.Kind)
                    return asm.Problems.Count > 0 && !asm.Ok ? asm.Problems[0].Message
                        : slot == 4 ? "The melee slot takes a melee build." : slot == GrenadeSlot ? "The grenade slot takes a grenade build." : "Gun slots take a gun build.";
                // Parts accounting: the new build may use its old parts plus loose ones.
                var oldCount = PartCounts(zp.Slots[slot]);
                var newCount = PartCounts(nb);
                foreach (var (name, n) in newCount)
                {
                    var have = oldCount.GetValueOrDefault(name) + zp.Parts.GetValueOrDefault(name);
                    if (n > have)
                        return $"You don't have the {name} (have {have}, the build needs {n}).";
                }
                foreach (var name in oldCount.Keys.Union(newCount.Keys).ToList())
                {
                    var left = zp.Parts.GetValueOrDefault(name) + oldCount.GetValueOrDefault(name) - newCount.GetValueOrDefault(name);
                    if (left > 0)
                        zp.Parts[name] = left;
                    else
                        zp.Parts.Remove(name);
                }
                GiveBuild(p, slot, nb, full: false);
                SendZRun(p);
                ZFeedTo(p, $"Workbench: {nb.Name} is in slot {SlotWord(slot)}.", Sfx.HitTick);
                return null;
            }
            case BenchDisassemble:
                return ServerDisassemble(p, zp, slot); // take the slot's whole build apart (ZombiesDisassemble.cs)
        }
        return "Unknown workbench action.";
    }

    private static Dictionary<string, int> PartCounts(WeaponBuild? b)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (b != null)
            foreach (var n in b.Nodes)
                d[n.Part.Name] = d.GetValueOrDefault(n.Part.Name) + 1;
        return d;
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    /// <summary>Self-test: run the Zombies logic for one tick at <paramref name="now"/> (no player movement).</summary>
    public void SelfTestZombiesTick(double now)
    {
        ServerTick++;
        if (Phase == MatchPhase.Playing)
            ServerTickZombies(now);
    }

    /// <summary>Self-test: start the next round now.</summary>
    public void SelfTestBeginRound(int round, double now)
    {
        if (_zRunMatch != MatchNumber)
            StartZombiesRun(now);
        BeginRound(round, now);
    }

    /// <summary>Self-test: the interact key for <paramref name="p"/> as if it were held for a while.</summary>
    public void SelfTestUse(Player p, double now)
    {
        ServerSetUse(p, true, now - 1);
        TickZPlayer(p, ZP(p), ZMap!, now);
        ServerSetUse(p, false, now);
    }

    public void SelfTestDown(Player p, double now) => Down(p, ZP(p), now);

    /// <summary>Self-test: a downed <paramref name="p"/> is revived.</summary>
    public void SelfTestRevive(Player p, double now) => Revive(p, null, now);

    /// <summary>Self-test: a shot from <paramref name="p"/>'s gun in hand through the server's shot path (no spread).</summary>
    public void SelfTestServerShot(Player p, Vector3 origin, Vector3 dir, double now) => ServerResolveShot(p, p.WeaponDef, origin, dir, 0f, 1, ServerTick, now);

    public int SelfTestBoxMoveAfter
    {
        get => _boxMoveAfter;
        set => _boxMoveAfter = value;
    }

    public int SelfTestToSpawn => _zToSpawn;

    /// <summary>Self-test: the round's total as worked out for the current players (<see cref="RescaleRoundForPlayers"/>).</summary>
    public int SelfTestRoundTotal => _zRoundTotal;

    public void SelfTestRollDrop(Vector3 at, int round, double now) => RollDrop(at, round, now);

    /// <summary>Self-test: a melee swing from <paramref name="p"/>'s eye along <paramref name="dir"/> against zombies (2 m reach, one zombie).</summary>
    public byte SelfTestZombieMelee(Player p, Vector3 dir, float damage, double now) => ZombieMelee(p, p.Eye, dir, 2f, 0.5f, damage, 1, 0, ServerTick, now);

    /// <summary>Self-test: the round's power-up count back to none (a new round's cap).</summary>
    public void SelfTestResetPowerCount() => _zPowerThisRound = 0;

    /// <summary>Self-test: one server tick of <paramref name="p"/>'s Zombies state (the interact key, the plate, bleeding out).</summary>
    public void SelfTestPerkTick(Player p, double now) => TickZPlayer(p, ZP(p), ZMap!, now);
}
