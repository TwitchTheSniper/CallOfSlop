using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a Zombies perk does (the perk machines' drinks). Every number is a multiplier or a switch.</summary>
public sealed class ZombiePerkDef
{
    public required string Name;

    /// <summary>Short id the map files use ("jugg", "speed", "doubletap", "revive").</summary>
    public required string Key;

    public int Cost;

    /// <summary>Solo price (Mender's Mead is cheaper alone, World at War style). 0 = the same as <see cref="Cost"/>.</summary>
    public int SoloCost;

    public string Blurb = "";
    public Vector3 Color = new(0.8f, 0.8f, 0.8f);

    /// <summary>Zombie hits do this much damage (Ironhide Cider: 0.4, about 2.5x the health).</summary>
    public float DamageTaken = 1f;

    /// <summary>Reload time multiplier (Swiftload Seltzer: 0.5).</summary>
    public float Reload = 1f;

    /// <summary>Fire rate multiplier (Twinfire Punch: 1.33).</summary>
    public float FireRate = 1f;

    /// <summary>Revive time multiplier for the reviver (Mender's Mead: 0.5).</summary>
    public float Revive = 1f;

    /// <summary>Solo only: being downed with this perk gets you back up on your own after a few seconds (the perk is used up).</summary>
    public bool SelfRevive;

    // ---- The perks added 2026-10-27 (docs/zombies/ZOMBIES-MODE.md, "Perks"). Every field is neutral by default. ----

    /// <summary>Movement speed multiplier on foot (Fleetfoot Fizz: 1.2). The client's prediction and the server's speed check both use it.</summary>
    public float MoveSpeed = 1f;

    /// <summary>Headshot damage multiplier on zombies (Marksman Malt: 1.5), and extra points for a headshot kill (Marksman Malt: 50).</summary>
    public float HeadshotDamage = 1f;

    public int HeadshotPoints;

    /// <summary>Melee damage multiplier on zombies (Bloodroot Tonic: 2), and health back for every melee swing that hits a zombie (Bloodroot Tonic: 30).</summary>
    public float MeleeDamage = 1f;

    public int MeleeHeal;

    /// <summary>An extra flat chance per kill of yours to drop a power-up (Windfall Soda: 0.06), which may go this many over the round's cap (2).</summary>
    public float PowerUpChance;

    public int PowerUpExtra;

    /// <summary>Barrier rebuild time multiplier (Hammerhand Ale: 0.4), and how many times the board points (and the round's cap on them) pay (2).</summary>
    public float Repair = 1f;

    public float RepairPoints = 1f;

    /// <summary>Part drop chance multiplier for your kills (Salvage Stout: 2), and extra parts from every salvage pile you search (1).</summary>
    public float PartDrops = 1f;

    public int SalvageBonus;

    /// <summary>Armour plate (Bulwark Brew: 12): the plate takes a whole zombie hit, then grows back this many seconds later (0: no plate).</summary>
    public float PlateSeconds;

    /// <summary>The HUD badge's glyph (UiZombiesPerks.cs; "" draws the perk's initial).</summary>
    public string Icon = "";

    /// <summary>The label over the machine (the name in capitals; made once so the HUD allocates nothing).</summary>
    public string Label => _label ??= Name.ToUpperInvariant();

    private string? _label;

    public int Price(bool solo) => solo && SoloCost > 0 ? SoloCost : Cost;
}

/// <summary>The glyphs a perk's HUD badge can draw (the rules' "icon"; drawn in UiZombiesPerks.cs).</summary>
public static class ZombiePerkIcons
{
    public static readonly string[] Names = ["heart", "drum", "rounds", "cross", "wind", "chevrons", "target", "fang", "star", "hammer", "gear", "shield"];

    public static bool Known(string name) => Array.IndexOf(Names, name) >= 0;

    public static int Index(string name) => Array.IndexOf(Names, name);
}

/// <summary>
/// The Zombies mode's numbers (Parts/Tables/ZombiesRules.json, format "call-of-slop-zombies/1"): the round curve,
/// the zombies, the points economy, downs and revives, perks, the mystery box and ammo. Proposed World at War style
/// defaults, open for the user (docs/zombies/ZOMBIES-MODE.md). Everything here is data: the code only reads it.
/// </summary>
public sealed class ZombiesRules
{
    public const string Format = "call-of-slop-zombies/1";

    // ---- Rounds -------------------------------------------------------------------------------------------
    /// <summary>Zombies in a round at full strength (round 5 to 9, solo), and how many more per extra player.</summary>
    public int CountBase = 24, CountPerExtraPlayer = 12;

    /// <summary>Rounds 1.. use this fraction of the full count (World at War: a handful in round 1).</summary>
    public float[] EarlyFractions = [0.25f, 0.3f, 0.5f, 0.7f, 0.9f];

    /// <summary>From round 6 the count is base x round / 5; from <see cref="LateFromRound"/> it's also x round x <see cref="LatePerRound"/>.</summary>
    public int LateFromRound = 10;

    public float LatePerRound = 0.15f;

    /// <summary>Zombie health: start, plus this much each round up to <see cref="HealthLinearUntil"/>, then x <see cref="HealthGrowth"/> per round.</summary>
    public int HealthStart = 150, HealthPerRound = 60, HealthLinearUntil = 9;

    public float HealthGrowth = 1.06f;

    /// <summary>Speeds (m/s): shamblers walk, then runners from a round, then sprinters (fractions grow per round, capped).</summary>
    public float WalkSpeed = 1.3f, RunSpeed = 3.2f, SprintSpeed = 5.2f;

    public int RunFromRound = 3, SprintFromRound = 5;
    public float RunPerRound = 0.15f, SprintPerRound = 0.1f, MaxRunFraction = 0.9f, MaxSprintFraction = 0.75f;

    /// <summary>Seconds between spawns: start, x mult per round, never below the minimum.</summary>
    public float SpawnDelay = 2f, SpawnDelayMult = 0.95f, SpawnDelayMin = 0.25f;

    /// <summary>
    /// Most zombies alive at once, whatever the round (the rules' hard cap, up to <see cref="Game.MaxZombies"/>; the rest wait
    /// in a queue). The round's own cap is <see cref="MaxAliveCurve"/>; the host's "Zombie horde cap" setting can lower both.
    /// </summary>
    public int MaxAlive = 600;

    /// <summary>
    /// The alive cap by round (docs/zombies/ZOMBIES-MODE.md, "Hordes"): 24 early, 40 by round 5, 90 by round 10, 220 by
    /// round 20, 400 by round 30, 600 by round 40 (straight lines between the points, flat past the ends), plus
    /// <see cref="MaxAlivePerExtraPlayer"/> of that per extra player (the player scaling, 2026-10-27).
    /// </summary>
    public RoundCurve MaxAliveCurve = new([(1, 24), (5, 40), (10, 90), (20, 220), (30, 400), (40, 600)]);

    /// <summary>Each extra player adds this share of the round's alive cap (0.5: four players get 2.5 times as many).</summary>
    public float MaxAlivePerExtraPlayer = 0.5f;

    /// <summary>Zombies that come out at once per spawn (late rounds fill a big cap faster), by round.</summary>
    public RoundCurve SpawnBurstCurve = new([(1, 1), (12, 1), (20, 4), (30, 7), (40, 9)]); // (raised with the alive caps, 2026-10-27: round 30 still fills in about 30 s)

    /// <summary>How many of the spawners nearest the players are used, by round (late rounds spread a horde over more of them).</summary>
    public RoundCurve ActiveSpawnersCurve = new([(1, 4), (15, 4), (25, 7), (35, 10)]);

    /// <summary>A spawner rests this long (seconds) after a zombie came out of it (spreads a burst over the spawners).</summary>
    public float SpawnerCooldown = 0.35f;

    // ---- Grenades ("grenades" section) -------------------------------------------------------------------------

    /// <summary>The grenade every Zombies player carries (a lethal one: Frag or Impact). The multiplayer loadout's throwables don't apply.</summary>
    public ThrowableKind GrenadeKind = ThrowableKind.Frag;

    /// <summary>Grenades at round 1, and how many rounds each extra one takes: see <see cref="GrenadesForRound"/>.</summary>
    public int GrenadeStart = 1, GrenadeRoundsPerExtra = 3;

    /// <summary>
    /// The grenades a player is topped up to at the start of round <paramref name="round"/> (and given on a spawn in it):
    /// start + floor((round - 1) / roundsPerExtra), so 1, 1, 1, 2, 2, 2, 3 ... with the shipped numbers (no cap;
    /// roundsPerExtra 0 keeps it at start). Never below 0.
    /// </summary>
    public int GrenadesForRound(int round)
    {
        var r = Math.Max(1, round);
        var n = (long)GrenadeStart + (GrenadeRoundsPerExtra > 0 ? (r - 1) / GrenadeRoundsPerExtra : 0);
        return (int)Math.Clamp(n, 0, 255); // (a byte in Msg.ZWorld)
    }

    /// <summary>The horde controller's numbers (the "horde" section; Source/Game/Horde/HordeController.cs).</summary>
    public HordeTuning Horde = new();

    /// <summary>Dismemberment's numbers (the "limbs" section; Source/Game/ZombieLimbs.cs).</summary>
    public LimbTuning Limbs = new();

    /// <summary>The horde climbing a face (the "climb" section; Source/Game/ZombiesClimb.cs: the Cathedral's bell tower).</summary>
    public ClimbTuning Climb = new();

    /// <summary>The mass horde's crowd tier (the "crowd" section; Source/Game/Horde/HordeCrowd.cs, docs/zombies/MASS-HORDE.md). Used only on maps with "crowd".</summary>
    public CrowdTuning Crowd = new();

    /// <summary>The alive cap in round <paramref name="round"/> with <paramref name="players"/> players (the curve, the extra players, the hard cap).</summary>
    public int MaxAliveFor(int round, int players)
    {
        var v = MaxAliveCurve.At(Math.Max(1, round)) * (1f + MaxAlivePerExtraPlayer * Math.Max(0, players - 1));
        return Math.Clamp((int)MathF.Round(v), 1, Math.Min(MaxAlive, Game.MaxZombies));
    }

    /// <summary>Seconds of break between rounds, and before round 1.</summary>
    public float BreakSeconds = 10f, FirstRoundDelay = 4f;

    /// <summary>
    /// Ready-up between rounds (2026-10-23): the next round waits until every living person presses the Ready key (bots
    /// count as ready), then starts <see cref="ReadyGoSeconds"/> later. <see cref="ReadyFallbackSeconds"/>: start anyway
    /// this long after the round ended (0 = wait for ever; the host can still force it). Off, or with no person in the run
    /// (a bots-only run), the break is the plain <see cref="BreakSeconds"/> timer.
    /// </summary>
    public bool ReadyUp = true;

    public float ReadyFallbackSeconds = 300f, ReadyGoSeconds = 3f;

    // ---- Zombies ------------------------------------------------------------------------------------------
    /// <summary>A zombie's swipe: damage, reach (from its centre to yours), the windup before it lands, and the time between swipes.</summary>
    public int ZombieDamage = 40;

    public float ZombieReach = 1.25f, ZombieWindup = 0.45f, ZombieSwing = 1.1f;

    /// <summary>Seconds to tear one board off a window, and to climb in once they're all off.</summary>
    public float BoardTearSeconds = 1.4f, ClimbSeconds = 1.1f;

    /// <summary>Seconds a zombie from a ground spawner takes to climb out of the ground (it stands still meanwhile; 0.9 before the animations).</summary>
    public float RiseSeconds = 1.4f;

    /// <summary>Headshot damage multiplier on zombies (on top of the weapon's own).</summary>
    public float ZombieHeadMult = 1f;

    // ---- Points -------------------------------------------------------------------------------------------
    public int StartPoints = 500, HitPoints = 10, KillPoints = 60, HeadshotKillPoints = 100, MeleeKillPoints = 130;

    /// <summary>
    /// A run started at a later round (the lobby's "Start round", Source/Game/ZombiesStartRound.cs): each player gets
    /// <see cref="StartPoints"/> plus <see cref="StartRoundKeep"/> of what a typical player earned in the skipped rounds
    /// (their share of the zombies times <see cref="StartRoundPerZombie"/>: a kill and a few hits), the rest having gone
    /// on doors, perks and guns. The team's power-up schedule is moved on by the whole of what was earned.
    /// </summary>
    public int StartRoundPerZombie = 90;

    public float StartRoundKeep = 0.12f;

    /// <summary>Points per board put back, and the most a player earns from boards in one round.</summary>
    public int RepairPoints = 10, RepairCapPerRound = 500;

    public float RepairSeconds = 0.8f;

    // ---- Downs and revives ------------------------------------------------------------------------------
    public float BleedoutSeconds = 30f, ReviveSeconds = 3f, ReviveRange = 1.8f, SelfReviveSeconds = 5f;

    /// <summary>Downed players crawl at this fraction of the prone speed (2026-10-23; prone is 0.22 x walking: 0.8 gives about 1.06 m/s).</summary>
    public float CrawlSpeed = 0.8f;

    /// <summary>Points a revive pays the reviver, and the share of points lost on bleeding out.</summary>
    public int RevivePoints = 0;

    public float BleedoutPointLoss = 0.1f;

    /// <summary>Going down loses every perk (World at War).</summary>
    public bool LosePerksOnDown = true;

    /// <summary>Bleeding out drops your built weapons where you went down, for anyone to pick up, for this long (seconds); the HUD blinks for the last <see cref="BuildDropWarnSeconds"/>.</summary>
    public bool DropBuildsOnBleedout = true;

    public float BuildDropSeconds = 300f, BuildDropWarnSeconds = 30f;

    // ---- Things a player drops on purpose (the drop key, the parts inventory; ZombiesDropItems.cs) -------------
    /// <summary>
    /// A gun or loose parts a player drops on purpose lie on the floor until the end of the round after the one they
    /// were dropped in (<see cref="PlayerDropRounds"/> rounds on; 0: no round limit), or for <see cref="PlayerDropSeconds"/>,
    /// whichever comes first.
    /// </summary>
    public float PlayerDropSeconds = 600f;

    public int PlayerDropRounds = 1;

    /// <summary>The most loose-part pickups on a map at once; past it a drop is refused (nothing is lost).</summary>
    public int MaxPartDrops = 48;

    /// <summary>The most parts one part pickup holds (you pick how many of a kind to drop).</summary>
    public int MaxPartsPerDrop = 50;

    // ---- Buyables -----------------------------------------------------------------------------------------
    /// <summary>How long the interact key is held to buy something (seconds).</summary>
    public float BuyHoldSeconds = 0.25f;

    /// <summary>The most perks the rules may list: they're sent as 16 bits of flags (protocol 57; one byte before).</summary>
    public const int MaxPerkCount = 16;

    /// <summary>A hard cap on perks held (16: every perk; there's no real cap since 2026-10-26).</summary>
    public int MaxPerks = MaxPerkCount;

    /// <summary>Perks beyond this many held cost more (developer answer, 2026-10-26): the next perk costs its price x
    /// <see cref="ExtraPerkFactor"/> to the power (perks held - this + 1), so with 4 the 5th costs 2x, the 6th 4x, the
    /// 7th 8x. Every perk, Second Wind too. 0 = never.</summary>
    public int FullPricePerks = 4;

    public float ExtraPerkFactor = 2f;

    /// <summary>What a perk listed at <paramref name="price"/> costs someone holding <paramref name="held"/> perks.</summary>
    public int PerkPrice(int price, int held)
    {
        if (FullPricePerks <= 0 || held < FullPricePerks)
            return price;
        var m = Math.Pow(ExtraPerkFactor, held - FullPricePerks + 1);
        return (int)Math.Min(int.MaxValue / 2, Math.Round(price * m));
    }
    public readonly List<ZombiePerkDef> Perks = new();

    /// <summary>Mystery box: the box moves after this many uses (rolled between the two), the spin, and how long the gun is offered.</summary>
    public int BoxMoveMin = 4, BoxMoveMax = 8;

    public float BoxSpinSeconds = 4f, BoxOfferSeconds = 10f, BoxMoveSeconds = 3f;

    /// <summary>
    /// The last part of the offer is free for all (board item 2026-10-27): anyone may take the gun for the final
    /// <see cref="BoxFreeFraction"/> of <see cref="BoxOfferSeconds"/> or the last <see cref="BoxFreeMinSeconds"/>, whichever is
    /// longer (never more than the whole offer). Both 0: only the buyer can ever take it.
    /// </summary>
    public float BoxFreeFraction = 0.3f, BoxFreeMinSeconds = 4f;

    /// <summary>How many seconds at the end of the offer anyone may take it (see <see cref="BoxFreeFraction"/>).</summary>
    public float BoxFreeSeconds => MathF.Min(BoxOfferSeconds, MathF.Max(BoxOfferSeconds * BoxFreeFraction, BoxFreeMinSeconds));

    // ---- Co-op bots spending their points (ZombieBotsShop.cs; developer answer, 2026-10-26) --------------------
    /// <summary>Bots buy doors, wall guns, the box, perks, the slot upgrade and slot pulls with their own points.</summary>
    public bool BotsBuy = true;

    /// <summary>Points a bot keeps back; a zombie this close (m) mid-round stops its shopping; how far from its post (m) it shops.</summary>
    public int BotReserve = 250;

    public float BotQuietRange = 10f, BotShopRange = 40f;

    /// <summary>The perks a bot wants, most wanted first (rules keys).</summary>
    public string[] BotPerkOrder = ["jugg", "revive", "hammerhand", "speed", "doubletap", "secondwind"];

    /// <summary>Spare points (over the price, the reserve and what it saves for its next perk) before a bot spins the box / pulls a slot machine.</summary>
    public int BotBoxSurplus = 500, BotSlotSurplus = 2500;

    /// <summary>A wall gun is worth buying when its damage per second is this many times the gun in hand's.</summary>
    public float BotWallBetter = 1.15f;

    /// <summary>Bots open doors (between rounds only, one door a break for the whole team).</summary>
    public bool BotDoors = true;

    /// <summary>How often a bot thinks about shopping (s), and how long it tries to reach something before giving up (s).</summary>
    public float BotThinkSeconds = 0.5f, BotGiveUpSeconds = 15f;

    // ---- Slot machines (ZombiesSlots.cs; the odds by round are DropTables.json "slotTiers") ---------------------
    /// <summary>A pull's price: <see cref="SlotCostBase"/> + <see cref="SlotCostPerRound"/> a round after the first, at most <see cref="SlotCostMax"/>.</summary>
    public int SlotCostBase = 500, SlotCostPerRound = 25, SlotCostMax = 1250;

    /// <summary>How long the reels spin, and how long the result shows before the machine takes another pull.</summary>
    public float SlotSpinSeconds = 2f, SlotShowSeconds = 2.5f;

    /// <summary>
    /// Machine heat (the pity timer): every pull without a <see cref="SlotPityTier"/>-or-better part adds 1; every
    /// <see cref="SlotHeatStep"/> heat moves the part odds one rarity step up (at most <see cref="SlotHeatMaxSteps"/> steps);
    /// such a part resets it.
    /// </summary>
    public int SlotHeatStep = 5, SlotHeatMaxSteps = 3;

    public Rarity SlotPityTier = Rarity.Rare;

    /// <summary>
    /// Bad-luck protection from before consolation parts: at most this many "nothing" results in a row per player per
    /// machine. Unused since 2026-10-27 (no pull pays nothing: a blank pays a <see cref="SlotConsolation"/> part); kept so
    /// older rules files load and it can come back.
    /// </summary>
    public int SlotMaxNothing = 3;

    /// <summary>
    /// Consolation parts (board item, 2026-10-27): a pull that rolls a blank pays a part of the machine's own category
    /// instead. Its rarity ignores the round's odds table: <see cref="SlotConsolation"/>[bet level] holds the weights per
    /// rarity (common first), and the result is never rarer than the main roll's lowest part tier at that bet. A
    /// consolation never resets the heat (it isn't a pity-tier win) and frees the machine at once.
    /// </summary>
    public float[][] SlotConsolation = [[100f], [60f, 40f], [0f, 85f, 15f]];

    /// <summary>The consolation weights per rarity for bet level <paramref name="bet"/> (the last level's for higher ones).</summary>
    public float[] SlotConsolationFor(int bet) => SlotConsolation.Length == 0 ? [] : SlotConsolation[Math.Clamp(bet, 0, SlotConsolation.Length - 1)];

    /// <summary>Every Nth round is a jackpot round: the wonder symbol is on the reels and these weights are added to legendary and wonder.</summary>
    public int SlotJackpotEvery = 5;

    public float SlotJackpotLegendary = 6f, SlotJackpotWonder = 4f;

    /// <summary>
    /// Payouts: points back (times the price) for a coin; and a part you already hold this many of loose pays the coin
    /// payout instead ("dupePointsAt"; 0 = always keep the part, the shipped value since 2026-10-27: loose parts have no
    /// cap, so you can bank as many copies as you like). Zombies has no scrap since 2026-10-27.
    /// </summary>
    public float SlotPointsBack = 1.5f;

    public int SlotDupePointsAt = 0;

    /// <summary>The price of a pull in <paramref name="round"/>.</summary>
    public int SlotCost(int round) => Math.Min(SlotCostMax, SlotCostBase + SlotCostPerRound * Math.Max(0, round - 1));

    /// <summary>
    /// Bet levels (developer answer to open question 13, 2026-10-26): a pull costs the price x <see cref="SlotBets"/>[bet],
    /// and its part odds move <see cref="SlotBetSteps"/>[bet] tiers up the ladder (on top of the machine's heat). The
    /// blank and coin weights don't change: a higher bet never guarantees a reward.
    /// </summary>
    public float[] SlotBets = [1f, 2f, 5f];

    public int[] SlotBetSteps = [0, 1, 2];

    /// <summary>The price of a pull at bet level <paramref name="bet"/> on a machine whose 1x price is <paramref name="price"/> (to the nearest 10).</summary>
    public int SlotBetCost(int price, int bet) => SlotBets.Length == 0 ? price : (int)MathF.Round(price * SlotBets[Math.Clamp(bet, 0, SlotBets.Length - 1)] / 10f) * 10;

    /// <summary>The tier steps of bet level <paramref name="bet"/>.</summary>
    public int SlotBetStep(int bet) => SlotBetSteps.Length == 0 ? 0 : SlotBetSteps[Math.Clamp(bet, 0, SlotBetSteps.Length - 1)];

    /// <summary>Is <paramref name="round"/> a jackpot round?</summary>
    public bool SlotJackpot(int round) => SlotJackpotEvery > 0 && round > 0 && round % SlotJackpotEvery == 0;

    /// <summary>
    /// Per-category slot machines ("slots.machines" in the rules file; ZombiesSlots.cs): each part category's price as a
    /// multiple of the classic machine's (indexed by <see cref="PartCategory"/>), and whether its reels carry the blade (a
    /// whole melee build: on by default for the melee categories only; a gun category's machine pays a part of that
    /// tier instead). The classic machine (no category) keeps the price and the blade.
    /// </summary>
    public float[] SlotCategoryPrice = DefaultCategoryPrices();

    public bool[] SlotCategoryMelee = Enum.GetValues<PartCategory>().Select(c => c is PartCategory.Hilt or PartCategory.Head or PartCategory.MeleeMod).ToArray();

    private static float[] DefaultCategoryPrices()
    {
        var a = new float[Enum.GetValues<PartCategory>().Length];
        Array.Fill(a, 1f);
        return a;
    }

    /// <summary>How long a slot machine's win shows over it (the won part's model) for everyone near, in seconds.</summary>
    public float SlotWinSeconds = 4f;

    /// <summary>The 1x price of a pull in <paramref name="round"/> on a machine of <paramref name="category"/> (-1: the classic machine), to the nearest 10.</summary>
    public int SlotCost(int round, int category) =>
        category < 0 || category >= SlotCategoryPrice.Length ? SlotCost(round) : (int)MathF.Round(SlotCost(round) * SlotCategoryPrice[category] / 10f) * 10;

    /// <summary>Does a machine of <paramref name="category"/> (-1: classic) pay whole melee builds?</summary>
    public bool SlotPaysMelee(int category) => category != ZombiesMap.SlotCategoryGrenade && (category < 0 || category >= SlotCategoryMelee.Length || SlotCategoryMelee[category]);

    // ---- Casino tables (ZombiesCasino.cs; docs/zombies/ZOMBIES-MODE.md, "Casino tables") ---------------------------
    /// <summary>Each game's table limits (index: <see cref="CasinoGame"/>), unless a map's table sets its own: the smallest and largest bet (roulette: a player's total on one spin).</summary>
    public int[] CasinoMin = [50, 10, 50, 50], CasinoMax = [1000, 1000, 500, 1000];

    /// <summary>The bet step (every bet is a whole number of these).</summary>
    public int CasinoBetStep = 10;

    /// <summary>
    /// Blackjack: seconds the table waits for other seats after the first bet, then each hand's time to act (a hand
    /// that hasn't finished by then stands). Roulette: seconds of betting after the first chip, and the wheel's spin.
    /// High-low and dice: the time to act before a hand cashes out / rolls on its own. Every game: how long a result shows.
    /// </summary>
    public float CasinoDealSeconds = 5f, CasinoTurnSeconds = 20f, CasinoRouletteBetSeconds = 12f, CasinoSpinSeconds = 6f, CasinoActSeconds = 30f, CasinoShowSeconds = 4f;

    /// <summary>How near a table you must be to play it, and how far you may wander before an unfinished hand is forfeit (metres).</summary>
    public float CasinoUseRange = 2.2f, CasinoLeaveRange = 4f;

    /// <summary>High-low: the house edge on every correct guess (a guess pays (1 - edge) / its chance), and the most guesses in one hand (it cashes out after).</summary>
    public float HighLowEdge = 0.05f;

    public int HighLowMaxSteps = 6;

    /// <summary>A win paying at least this many points over the bet is announced to everyone (the feed and a label over the table).</summary>
    public int CasinoAnnounceAt = 1000;

    /// <summary>Reserve ammo: magazines of reserve a bought gun comes with (the start guns get fewer), and the wall ammo price as a share of the gun's.</summary>
    public int ReserveMags = 6, StartReserveMags = 4;

    public float AmmoCostFraction = 0.5f;

    // ---- Parts ---------------------------------------------------------------------------------------------
    /// <summary>A dropped part lies on the floor this long; a salvage pile can be searched once per round.</summary>
    public float DropSeconds = 30f;

    public float PickupRange = 1.4f;

    /// <summary>
    /// Loose parts a salvage pile gives when searched (once per round; each rolled like a zombie's part drop, from
    /// DropTables.json "partDrops" for the round). Before 2026-10-27 piles gave scrap.
    /// </summary>
    public int SalvageParts = 1;

    // ---- Power-ups (World at War style; ZombiesPowerUps.cs) ---------------------------------------------
    /// <summary>
    /// The drop schedule (World at War style; developer answer, 2026-10-26): a power-up becomes due once the TEAM has
    /// earned <see cref="PowerFirstAt"/> points (hits, kills, boards; Double Points counted as paid), and the next kill by a
    /// player drops it; then the next one is due <see cref="PowerFirstAt"/> x <see cref="PowerStepGrowth"/> to the power
    /// (drops so far) points later (at most <see cref="PowerStepMax"/>; 0 = no cap), so the steps rise over the game.
    /// <see cref="PowerDropChance"/>: an extra flat chance per kill on top (0 by default: the schedule replaced the old 4
    /// percent roll). At most <see cref="PowerMaxPerRound"/> a round either way (a due drop waits for the next round).
    /// </summary>
    public float PowerFirstAt = 1800f, PowerStepGrowth = 1.02f, PowerStepMax = 4000f;

    public float PowerDropChance;

    public int PowerMaxPerRound = 4;

    /// <summary>A power-up floats this long (seconds), blinking for the last <see cref="PowerBlinkSeconds"/>; the timed ones (Insta-Kill, Double Points, Infinite Ammo) last <see cref="PowerDuration"/> unless <see cref="PowerDurations"/> says otherwise.</summary>
    public float PowerLifetime = 30f, PowerBlinkSeconds = 10f, PowerDuration = 30f, PowerPickupRange = 1.3f;

    /// <summary>Points the Nuke and the Carpenter give every player.</summary>
    public int NukePoints = 400, CarpenterPoints = 200;

    /// <summary>How often each power-up drops, by <see cref="ZPowerKind"/> (Max Ammo, Insta-Kill, Double Points, Nuke, Carpenter, Infinite Ammo).</summary>
    public float[] PowerWeights = [2.5f, 1f, 1f, 0.8f, 0.8f, 0.5f];

    /// <summary>The rules file's "durations" by <see cref="ZPowerKind"/> (seconds; 0: <see cref="PowerDuration"/>). Only the timed kinds have one.</summary>
    public float[] PowerDurations = new float[(int)ZPowerKind.COUNT];

    /// <summary>How long a timed power-up lasts once taken (seconds).</summary>
    public float DurationOf(ZPowerKind k) => (int)k < PowerDurations.Length && PowerDurations[(int)k] > 0 ? PowerDurations[(int)k] : PowerDuration;

    // ---- Special ammo effects (part flags against zombies; ZombiesAmmoEffects.cs) ------------------------
    /// <summary>Incendiary: a burn of this share of the hit's damage (at least <see cref="BurnMin"/>) over <see cref="BurnSeconds"/>.</summary>
    public float BurnFraction = 0.6f, BurnSeconds = 3f;

    public int BurnMin = 40;

    /// <summary>Explosive: a splash this wide (metres) doing this share of the hit's damage to the zombies round the one hit.</summary>
    public float SplashRadius = 2.2f, SplashFraction = 0.5f;

    /// <summary>Shock: arcs to this many more zombies within the range, for this share of the hit, stunning them.</summary>
    public int ShockTargets = 3;

    public float ShockRange = 3.5f, ShockFraction = 0.4f, ShockStun = 0.4f;

    /// <summary>Penetrating: the round goes on through this many more zombies within the range behind, at this share.</summary>
    public int PierceTargets = 2;

    public float PierceRange = 8f, PierceFraction = 0.6f;

    /// <summary>Bouncy: the round bounces on to the nearest zombie within the range, at this share.</summary>
    public float BounceRange = 4f, BounceFraction = 0.5f;

    /// <summary>Plasma: zombies hit move at this share of their speed for this long.</summary>
    public float SlowSeconds = 2f, SlowMult = 0.5f;

    /// <summary>Bonk (frying pan): stun seconds; squeaky: a short stun; rocket and fish: knockback speed (m/s).</summary>
    public float BonkStun = 1f, SqueakStun = 0.3f, Knockback = 6f;

    /// <summary>The "launch" flag: how fast a launched zombie flies away and up (m/s), and how long it is stunned.</summary>
    public float LaunchSpeed = 11f, LaunchUp = 8f, LaunchStun = 1.2f;

    /// <summary>Nails and chainsaw: a bleed of this share of the hit over <see cref="BurnSeconds"/>.</summary>
    public float BleedFraction = 0.3f;

    /// <summary>Refreshing: health back per kill; bayonet: damage multiplier within the range.</summary>
    public int RefreshHealth = 5;

    public float BayonetRange = 2.2f, BayonetMult = 1.5f;

    private static ZombiesRules? _loaded;
    public static readonly List<string> LoadErrors = new();

    public static string PathName => Path.Combine(Parts.Folder, "Tables", "ZombiesRules.json");

    /// <summary>The rules from Parts/Tables/ZombiesRules.json (the built-in defaults if it's missing or broken; the error is listed).</summary>
    public static ZombiesRules Current
    {
        get
        {
            if (_loaded != null)
                return _loaded;
            try
            {
                _loaded = Load(File.ReadAllText(PathName), "Tables/ZombiesRules.json");
            }
            catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
            {
                LoadErrors.Add(ex.Message);
                Console.WriteLine($"[zombies] {ex.Message}");
                _loaded = Defaults();
            }
            return _loaded;
        }
    }

    /// <summary>The built-in rules (the same numbers the shipped file has) with all twelve perks.</summary>
    public static ZombiesRules Defaults()
    {
        var r = new ZombiesRules();
        r.Perks.Add(new ZombiePerkDef { Name = "Ironhide Cider", Key = "jugg", Cost = 2500, DamageTaken = 0.4f, Color = new Vector3(0.85f, 0.15f, 0.15f), Icon = "heart", Blurb = "Zombie hits do 40 percent of their damage." });
        r.Perks.Add(new ZombiePerkDef { Name = "Swiftload Seltzer", Key = "speed", Cost = 3000, Reload = 0.5f, Color = new Vector3(0.2f, 0.85f, 0.3f), Icon = "drum", Blurb = "Reload twice as fast." });
        r.Perks.Add(new ZombiePerkDef { Name = "Twinfire Punch", Key = "doubletap", Cost = 2000, FireRate = 1.33f, Color = new Vector3(0.95f, 0.75f, 0.15f), Icon = "rounds", Blurb = "Fire a third faster." });
        r.Perks.Add(new ZombiePerkDef { Name = "Mender's Mead", Key = "revive", Cost = 1500, Revive = 0.5f, Color = new Vector3(0.3f, 0.6f, 1f), Icon = "cross", Blurb = "Revive teammates twice as fast." });
        r.Perks.Add(new ZombiePerkDef { Name = "Second Wind", Key = "secondwind", Cost = 1500, SelfRevive = true, Color = new Vector3(1f, 0.55f, 0.1f), Icon = "wind", Blurb = "When you go down, you get back up on your own after a few seconds (used up)." });
        foreach (var d in MorePerks())
            r.Perks.Add(d);
        return r;
    }

    /// <summary>The seven perks added 2026-10-27 (the same numbers the shipped file has).</summary>
    private static IEnumerable<ZombiePerkDef> MorePerks()
    {
        yield return new ZombiePerkDef { Name = "Fleetfoot Fizz", Key = "fleetfoot", Cost = 2000, MoveSpeed = 1.2f, Color = new Vector3(0.25f, 0.9f, 0.9f), Icon = "chevrons", Blurb = "Move 20 percent faster." };
        yield return new ZombiePerkDef { Name = "Marksman Malt", Key = "marksman", Cost = 2500, HeadshotDamage = 1.5f, HeadshotPoints = 50, Color = new Vector3(0.55f, 0.35f, 0.95f), Icon = "target", Blurb = "Headshots do 50 percent more damage; headshot kills pay 50 more points." };
        yield return new ZombiePerkDef { Name = "Bloodroot Tonic", Key = "bloodroot", Cost = 2000, MeleeDamage = 2f, MeleeHeal = 30, Color = new Vector3(0.6f, 0.05f, 0.2f), Icon = "fang", Blurb = "Melee does double damage, and every melee hit heals you 30." };
        yield return new ZombiePerkDef { Name = "Windfall Soda", Key = "windfall", Cost = 3000, PowerUpChance = 0.06f, PowerUpExtra = 2, Color = new Vector3(1f, 0.85f, 0.25f), Icon = "star", Blurb = "Your kills have a 6 percent chance to drop a power-up (up to 2 over the round's limit)." };
        yield return new ZombiePerkDef { Name = "Hammerhand Ale", Key = "hammerhand", Cost = 1000, Repair = 0.4f, RepairPoints = 2f, Color = new Vector3(0.75f, 0.5f, 0.25f), Icon = "hammer", Blurb = "Rebuild barriers 2.5 times as fast, for double the board points." };
        yield return new ZombiePerkDef { Name = "Salvage Stout", Key = "salvage", Cost = 2000, PartDrops = 2f, SalvageBonus = 1, Color = new Vector3(0.6f, 0.68f, 0.45f), Icon = "gear", Blurb = "Your kills drop parts twice as often; salvage piles give you a part more." };
        yield return new ZombiePerkDef { Name = "Bulwark Brew", Key = "bulwark", Cost = 3500, PlateSeconds = 12f, Color = new Vector3(0.45f, 0.6f, 0.8f), Icon = "shield", Blurb = "An armour plate takes a whole zombie hit, then grows back 12 seconds later." };
    }

    public ZombiePerkDef? Perk(string keyOrName) => PerkIndex(keyOrName) is var i and >= 0 ? Perks[i] : null;

    /// <summary>The perk with this key or name, or -1 (no allocation: the bots look perks up every think).</summary>
    public int PerkIndex(string keyOrName)
    {
        for (var i = 0; i < Perks.Count; i++)
            if (Perks[i].Key.Equals(keyOrName, StringComparison.OrdinalIgnoreCase) || Perks[i].Name.Equals(keyOrName, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // ---- The round curve ----------------------------------------------------------------------------------

    /// <summary>How many zombies round <paramref name="round"/> sends at <paramref name="players"/> players (World at War's curve).</summary>
    public int ZombieCount(int round, int players)
    {
        round = Math.Max(1, round);
        float full = CountBase + CountPerExtraPlayer * Math.Max(0, players - 1);
        float mult = round <= EarlyFractions.Length ? EarlyFractions[round - 1] : round / 5f;
        if (round >= LateFromRound)
            mult *= round * LatePerRound;
        return Math.Max(1, (int)MathF.Round(full * mult));
    }

    /// <summary>A zombie's health in round <paramref name="round"/>: linear up to <see cref="HealthLinearUntil"/>, then growing by <see cref="HealthGrowth"/> per round.</summary>
    public int ZombieHealth(int round)
    {
        round = Math.Max(1, round);
        var linearRounds = Math.Min(round, HealthLinearUntil) - 1;
        double h = HealthStart + HealthPerRound * linearRounds;
        for (var r = HealthLinearUntil + 1; r <= round; r++)
            h *= HealthGrowth;
        return (int)Math.Min(h, 1_000_000);
    }

    /// <summary>Of the zombies in round <paramref name="round"/>: the share that run and the share that sprint (the rest walk).</summary>
    public (float Run, float Sprint) SpeedMix(int round)
    {
        var sprint = round >= SprintFromRound ? MathF.Min(MaxSprintFraction, (round - SprintFromRound + 1) * SprintPerRound) : 0f;
        var run = round >= RunFromRound ? MathF.Min(MaxRunFraction, (round - RunFromRound + 1) * RunPerRound) : 0f;
        run = MathF.Min(run, 1f - sprint);
        return (run, sprint);
    }

    /// <summary>Seconds between spawns in round <paramref name="round"/>.</summary>
    public float SpawnDelayFor(int round) => MathF.Max(SpawnDelayMin, SpawnDelay * MathF.Pow(SpawnDelayMult, Math.Max(0, round - 1)));

    // ---- Loading ------------------------------------------------------------------------------------------

    private static readonly string[] Sections = ["format", "note", "rounds", "zombie", "points", "downs", "playerDrops", "buy", "perks", "box", "ammo", "parts", "powerUps", "ammoEffects", "slots", "casino", "bots", "horde", "limbs", "climb", "crowd"];

    public static ZombiesRules Load(string json, string source)
    {
        using var doc = WeaponFile.Parse(json, source);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{source}: the rules file is one JSON object");
        var r = new ZombiesRules();
        var sawPerks = false;
        foreach (var p in root.EnumerateObject())
        {
            var at = $"{source}: \"{p.Name}\"";
            switch (p.Name)
            {
                case "format": WeaponFile.CheckFormat(p.Value, "call-of-slop-zombies/", Format, at); break;
                case "note": break;
                case "rounds":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "countBase": r.CountBase = Whole(v, w, 1, 500); break;
                            case "countPerExtraPlayer": r.CountPerExtraPlayer = Whole(v, w, 0, 100); break;
                            case "earlyFractions":
                                if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() is < 1 or > 9)
                                    throw new WeaponFileException($"{w}: a list of 1-9 fractions");
                                r.EarlyFractions = v.EnumerateArray().Select(x => Real(x, w, 0.01f, 1f)).ToArray();
                                break;
                            case "lateFromRound": r.LateFromRound = Whole(v, w, 2, 1000); break;
                            case "latePerRound": r.LatePerRound = Real(v, w, 0, 1); break;
                            case "healthStart": r.HealthStart = Whole(v, w, 1, 100000); break;
                            case "healthPerRound": r.HealthPerRound = Whole(v, w, 0, 100000); break;
                            case "healthLinearUntil": r.HealthLinearUntil = Whole(v, w, 1, 1000); break;
                            case "healthGrowth": r.HealthGrowth = Real(v, w, 1, 2); break;
                            case "walkSpeed": r.WalkSpeed = Real(v, w, 0.3f, 12); break;
                            case "runSpeed": r.RunSpeed = Real(v, w, 0.3f, 12); break;
                            case "sprintSpeed": r.SprintSpeed = Real(v, w, 0.3f, 12); break;
                            case "runFromRound": r.RunFromRound = Whole(v, w, 1, 1000); break;
                            case "sprintFromRound": r.SprintFromRound = Whole(v, w, 1, 1000); break;
                            case "runPerRound": r.RunPerRound = Real(v, w, 0, 1); break;
                            case "sprintPerRound": r.SprintPerRound = Real(v, w, 0, 1); break;
                            case "maxRunFraction": r.MaxRunFraction = Real(v, w, 0, 1); break;
                            case "maxSprintFraction": r.MaxSprintFraction = Real(v, w, 0, 1); break;
                            case "spawnDelay": r.SpawnDelay = Real(v, w, 0.05f, 30); break;
                            case "spawnDelayMult": r.SpawnDelayMult = Real(v, w, 0.5f, 1); break;
                            case "spawnDelayMin": r.SpawnDelayMin = Real(v, w, 0.02f, 30); break;
                            case "maxAlive": r.MaxAlive = Whole(v, w, 1, Game.MaxZombies); break;
                            case "maxAliveCurve": r.MaxAliveCurve = RoundCurve.Read(v, w, 1, Game.MaxZombies); break;
                            case "maxAlivePerExtraPlayer": r.MaxAlivePerExtraPlayer = Real(v, w, 0, 2); break;
                            case "spawnBurstCurve": r.SpawnBurstCurve = RoundCurve.Read(v, w, 1, 16); break;
                            case "activeSpawnersCurve": r.ActiveSpawnersCurve = RoundCurve.Read(v, w, 1, 64); break;
                            case "spawnerCooldown": r.SpawnerCooldown = Real(v, w, 0, 30); break;
                            case "breakSeconds": r.BreakSeconds = Real(v, w, 1, 120); break;
                            case "firstRoundDelay": r.FirstRoundDelay = Real(v, w, 0, 120); break;
                            case "readyUp": r.ReadyUp = Flag(v, w); break;
                            case "readyFallbackSeconds": r.ReadyFallbackSeconds = Real(v, w, 0, 3600); break;
                            case "readyGoSeconds": r.ReadyGoSeconds = Real(v, w, 0, 30); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "zombie":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "damage": r.ZombieDamage = Whole(v, w, 1, 1000); break;
                            case "reach": r.ZombieReach = Real(v, w, 0.5f, 4); break;
                            case "windup": r.ZombieWindup = Real(v, w, 0, 3); break;
                            case "swing": r.ZombieSwing = Real(v, w, 0.1f, 5); break;
                            case "boardTearSeconds": r.BoardTearSeconds = Real(v, w, 0.1f, 20); break;
                            case "climbSeconds": r.ClimbSeconds = Real(v, w, 0.1f, 10); break;
                            case "riseSeconds": r.RiseSeconds = Real(v, w, 0.1f, 10); break;
                            case "headMult": r.ZombieHeadMult = Real(v, w, 0.5f, 10); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "points":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "start": r.StartPoints = Whole(v, w, 0, 1000000); break;
                            case "startRoundPerZombie": r.StartRoundPerZombie = Whole(v, w, 0, 10000); break;
                            case "startRoundKeep": r.StartRoundKeep = Real(v, w, 0, 1); break;
                            case "hit": r.HitPoints = Whole(v, w, 0, 10000); break;
                            case "kill": r.KillPoints = Whole(v, w, 0, 10000); break;
                            case "headshotKill": r.HeadshotKillPoints = Whole(v, w, 0, 10000); break;
                            case "meleeKill": r.MeleeKillPoints = Whole(v, w, 0, 10000); break;
                            case "repairBoard": r.RepairPoints = Whole(v, w, 0, 10000); break;
                            case "repairCapPerRound": r.RepairCapPerRound = Whole(v, w, 0, 1000000); break;
                            case "repairSeconds": r.RepairSeconds = Real(v, w, 0.05f, 10); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "downs":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "bleedoutSeconds": r.BleedoutSeconds = Real(v, w, 1, 600); break;
                            case "reviveSeconds": r.ReviveSeconds = Real(v, w, 0.1f, 30); break;
                            case "reviveRange": r.ReviveRange = Real(v, w, 0.5f, 5); break;
                            case "selfReviveSeconds": r.SelfReviveSeconds = Real(v, w, 0.5f, 60); break;
                            case "crawlSpeed": r.CrawlSpeed = Real(v, w, 0, 1); break;
                            case "revivePoints": r.RevivePoints = Whole(v, w, 0, 100000); break;
                            case "bleedoutPointLoss": r.BleedoutPointLoss = Real(v, w, 0, 1); break;
                            case "losePerksOnDown": r.LosePerksOnDown = Flag(v, w); break;
                            case "dropBuildsOnBleedout": r.DropBuildsOnBleedout = Flag(v, w); break;
                            case "buildDropSeconds": r.BuildDropSeconds = Real(v, w, 5, 3600); break;
                            case "buildDropWarnSeconds": r.BuildDropWarnSeconds = Real(v, w, 0, 600); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "playerDrops":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "seconds": r.PlayerDropSeconds = Real(v, w, 5, 7200); break;
                            case "rounds": r.PlayerDropRounds = Whole(v, w, 0, 100); break;
                            case "maxPartDrops": r.MaxPartDrops = Whole(v, w, 1, 255); break;
                            case "maxPartsPerDrop": r.MaxPartsPerDrop = Whole(v, w, 1, 255); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "buy":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "holdSeconds": r.BuyHoldSeconds = Real(v, w, 0, 3); break;
                            case "maxPerks": r.MaxPerks = Whole(v, w, 0, MaxPerkCount); break;
                            case "fullPricePerks": r.FullPricePerks = Whole(v, w, 0, MaxPerkCount); break;
                            case "extraPerkFactor": r.ExtraPerkFactor = Real(v, w, 1, 10); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "perks":
                    sawPerks = true;
                    if (p.Value.ValueKind != JsonValueKind.Array)
                        throw new WeaponFileException($"{at}: a list of perks");
                    foreach (var pe in p.Value.EnumerateArray())
                        r.Perks.Add(ReadPerk(pe, at, r.Perks));
                    if (r.Perks.Count > MaxPerkCount)
                        throw new WeaponFileException($"{at}: at most {MaxPerkCount} perks (they're sent as 16 bits of flags)");
                    break;
                case "grenades":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "kind":
                                if (!Enum.TryParse<ThrowableKind>(WeaponFile.Text(v, w), true, out var kind) || kind is not (ThrowableKind.Frag or ThrowableKind.Impact))
                                    throw new WeaponFileException($"{w}: \"Frag\" or \"Impact\" (a grenade that hurts zombies)");
                                r.GrenadeKind = kind;
                                break;
                            case "start": r.GrenadeStart = Whole(v, w, 0, 50); break;
                            case "roundsPerExtra": r.GrenadeRoundsPerExtra = Whole(v, w, 0, 1000); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "box":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "moveMin": r.BoxMoveMin = Whole(v, w, 1, 100); break;
                            case "moveMax": r.BoxMoveMax = Whole(v, w, 1, 100); break;
                            case "spinSeconds": r.BoxSpinSeconds = Real(v, w, 0.2f, 20); break;
                            case "offerSeconds": r.BoxOfferSeconds = Real(v, w, 1, 60); break;
                            case "moveSeconds": r.BoxMoveSeconds = Real(v, w, 0.2f, 30); break;
                            case "freeFraction": r.BoxFreeFraction = Real(v, w, 0, 1); break;
                            case "freeSeconds": r.BoxFreeMinSeconds = Real(v, w, 0, 60); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "ammo":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "reserveMags": r.ReserveMags = Whole(v, w, 0, 100); break;
                            case "startReserveMags": r.StartReserveMags = Whole(v, w, 0, 100); break;
                            case "ammoCostFraction": r.AmmoCostFraction = Real(v, w, 0, 1); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "parts":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "dropSeconds": r.DropSeconds = Real(v, w, 1, 600); break;
                            case "pickupRange": r.PickupRange = Real(v, w, 0.3f, 5); break;
                            case "salvageParts": r.SalvageParts = Whole(v, w, 0, 10); break;
                            case "scrapPerKill" or "salvageScrap": break; // (scrap is gone, 2026-10-27: older rules files still load)
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "powerUps":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "dropChance": r.PowerDropChance = Real(v, w, 0, 1); break;
                            case "firstAt": r.PowerFirstAt = Real(v, w, 0, 1000000); break;
                            case "stepGrowth": r.PowerStepGrowth = Real(v, w, 1, 3); break;
                            case "stepMax": r.PowerStepMax = Real(v, w, 0, 1000000); break;
                            case "maxPerRound": r.PowerMaxPerRound = Whole(v, w, 0, 100); break;
                            case "lifetime": r.PowerLifetime = Real(v, w, 1, 600); break;
                            case "blinkSeconds": r.PowerBlinkSeconds = Real(v, w, 0, 600); break;
                            case "duration": r.PowerDuration = Real(v, w, 1, 600); break;
                            case "pickupRange": r.PowerPickupRange = Real(v, w, 0.3f, 5); break;
                            case "nukePoints": r.NukePoints = Whole(v, w, 0, 100000); break;
                            case "carpenterPoints": r.CarpenterPoints = Whole(v, w, 0, 100000); break;
                            case "weights":
                                if (v.ValueKind != JsonValueKind.Object)
                                    throw new WeaponFileException($"{w}: an object of weights ({string.Join(", ", PowerKeys)})");
                                foreach (var pw in v.EnumerateObject())
                                {
                                    var i = Array.IndexOf(PowerKeys, pw.Name);
                                    if (i < 0)
                                        throw new WeaponFileException($"{w}: unknown power-up \"{pw.Name}\" ({string.Join(", ", PowerKeys)})");
                                    r.PowerWeights[i] = Real(pw.Value, $"{w}.{pw.Name}", 0, 100);
                                }
                                break;
                            case "durations":
                                if (v.ValueKind != JsonValueKind.Object)
                                    throw new WeaponFileException($"{w}: an object of seconds ({string.Join(", ", TimedPowerKeys)})");
                                foreach (var pd in v.EnumerateObject())
                                {
                                    if (Array.IndexOf(TimedPowerKeys, pd.Name) < 0)
                                        throw new WeaponFileException($"{w}: \"{pd.Name}\" isn't a timed power-up ({string.Join(", ", TimedPowerKeys)})");
                                    r.PowerDurations[Array.IndexOf(PowerKeys, pd.Name)] = Real(pd.Value, $"{w}.{pd.Name}", 1, 600);
                                }
                                break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "ammoEffects":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "burnFraction": r.BurnFraction = Real(v, w, 0, 5); break;
                            case "burnSeconds": r.BurnSeconds = Real(v, w, 0.25f, 30); break;
                            case "burnMin": r.BurnMin = Whole(v, w, 0, 100000); break;
                            case "splashRadius": r.SplashRadius = Real(v, w, 0.2f, 10); break;
                            case "splashFraction": r.SplashFraction = Real(v, w, 0, 5); break;
                            case "shockTargets": r.ShockTargets = Whole(v, w, 0, 10); break;
                            case "shockRange": r.ShockRange = Real(v, w, 0.2f, 15); break;
                            case "shockFraction": r.ShockFraction = Real(v, w, 0, 5); break;
                            case "shockStun": r.ShockStun = Real(v, w, 0, 5); break;
                            case "pierceTargets": r.PierceTargets = Whole(v, w, 0, 10); break;
                            case "pierceRange": r.PierceRange = Real(v, w, 0.2f, 50); break;
                            case "pierceFraction": r.PierceFraction = Real(v, w, 0, 5); break;
                            case "bounceRange": r.BounceRange = Real(v, w, 0.2f, 15); break;
                            case "bounceFraction": r.BounceFraction = Real(v, w, 0, 5); break;
                            case "slowSeconds": r.SlowSeconds = Real(v, w, 0, 30); break;
                            case "slowMult": r.SlowMult = Real(v, w, 0.05f, 1); break;
                            case "bonkStun": r.BonkStun = Real(v, w, 0, 5); break;
                            case "squeakStun": r.SqueakStun = Real(v, w, 0, 5); break;
                            case "knockback": r.Knockback = Real(v, w, 0, 20); break;
                            case "launchSpeed": r.LaunchSpeed = Real(v, w, 0, 30); break;
                            case "launchUp": r.LaunchUp = Real(v, w, 0, 20); break;
                            case "launchStun": r.LaunchStun = Real(v, w, 0, 5); break;
                            case "bleedFraction": r.BleedFraction = Real(v, w, 0, 5); break;
                            case "refreshHealth": r.RefreshHealth = Whole(v, w, 0, 200); break;
                            case "bayonetRange": r.BayonetRange = Real(v, w, 0, 10); break;
                            case "bayonetMult": r.BayonetMult = Real(v, w, 1, 10); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "slots":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "costBase": r.SlotCostBase = Whole(v, w, 0, 100000); break;
                            case "costPerRound": r.SlotCostPerRound = Whole(v, w, 0, 10000); break;
                            case "costMax": r.SlotCostMax = Whole(v, w, 0, 100000); break;
                            case "spinSeconds": r.SlotSpinSeconds = Real(v, w, 0.3f, 10); break;
                            case "showSeconds": r.SlotShowSeconds = Real(v, w, 0.2f, 10); break;
                            case "heatStep": r.SlotHeatStep = Whole(v, w, 1, 100); break;
                            case "heatMaxSteps": r.SlotHeatMaxSteps = Whole(v, w, 0, 5); break;
                            case "bets":
                                if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() is < 1 or > 5)
                                    throw new WeaponFileException($"{w}: a list of 1 to 5 price multipliers (1, 2, 5)");
                                r.SlotBets = v.EnumerateArray().Select(x => Real(x, w, 0.1f, 100)).ToArray();
                                break;
                            case "betSteps":
                                if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() is < 1 or > 5)
                                    throw new WeaponFileException($"{w}: a list of 1 to 5 tier steps (0, 1, 2), one per bet");
                                r.SlotBetSteps = v.EnumerateArray().Select(x => Whole(x, w, 0, 5)).ToArray();
                                break;
                            case "pityTier":
                                r.SlotPityTier = Enum.TryParse<Rarity>(WeaponFile.Text(v, w), true, out var tier) && Enum.IsDefined(tier)
                                    ? tier : throw new WeaponFileException($"{w}: a rarity (common, uncommon, rare, epic, legendary, wonder)");
                                break;
                            case "maxNothing": r.SlotMaxNothing = Whole(v, w, 0, 100); break;
                            case "consolation":
                                if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() is < 1 or > 5)
                                    throw new WeaponFileException($"{w}: a list of 1 to 5 rarity weights, one per bet ({{\"common\": 60, \"uncommon\": 40}})");
                                r.SlotConsolation = v.EnumerateArray().Select((x, n) => ConsolationRow(x, $"{w}[{n}]")).ToArray();
                                break;
                            case "jackpotEvery": r.SlotJackpotEvery = Whole(v, w, 0, 100); break;
                            case "jackpotLegendary": r.SlotJackpotLegendary = Real(v, w, 0, 1000); break;
                            case "jackpotWonder": r.SlotJackpotWonder = Real(v, w, 0, 1000); break;
                            case "scrapPayout": break; // (scrap is gone, 2026-10-27: older rules files still load)
                            case "pointsBack": r.SlotPointsBack = Real(v, w, 0, 20); break;
                            case "dupePointsAt": r.SlotDupePointsAt = Whole(v, w, 0, 100); break;
                            case "autoScrapAt": break; // (replaced by dupePointsAt, 2026-10-27)
                            case "winSeconds": r.SlotWinSeconds = Real(v, w, 0.5f, 30); break;
                            case "machines":
                                Fields(v, w, (ck, cv, cw) =>
                                {
                                    if (ck == "note")
                                        return true;
                                    if (!Enum.TryParse<PartCategory>(ck, false, out var cat) || !Enum.IsDefined(cat))
                                        throw new WeaponFileException($"{cw}: not a part category ({string.Join(", ", Enum.GetValues<PartCategory>())})");
                                    Fields(cv, cw, (mk, mv, mw) =>
                                    {
                                        switch (mk)
                                        {
                                            case "price": r.SlotCategoryPrice[(int)cat] = Real(mv, mw, 0.1f, 20); break;
                                            case "melee": r.SlotCategoryMelee[(int)cat] = Flag(mv, mw); break;
                                            default: return false;
                                        }
                                        return true;
                                    });
                                    return true;
                                });
                                break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "casino":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "blackjack" or "roulette" or "highLow" or "dice":
                            {
                                var g = k switch { "blackjack" => 0, "roulette" => 1, "highLow" => 2, _ => 3 };
                                Fields(v, w, (gk, gv, gw) =>
                                {
                                    switch (gk)
                                    {
                                        case "note": break;
                                        case "min": r.CasinoMin[g] = Whole(gv, gw, 1, 1000000); break;
                                        case "max": r.CasinoMax[g] = Whole(gv, gw, 1, 1000000); break;
                                        default: return false;
                                    }
                                    return true;
                                });
                                if (r.CasinoMax[g] < r.CasinoMin[g])
                                    throw new WeaponFileException($"{w}: max is below min");
                                break;
                            }
                            case "betStep": r.CasinoBetStep = Whole(v, w, 1, 10000); break;
                            case "dealSeconds": r.CasinoDealSeconds = Real(v, w, 0.5f, 60); break;
                            case "turnSeconds": r.CasinoTurnSeconds = Real(v, w, 2, 300); break;
                            case "rouletteBetSeconds": r.CasinoRouletteBetSeconds = Real(v, w, 1, 120); break;
                            case "spinSeconds": r.CasinoSpinSeconds = Real(v, w, 1, 30); break;
                            case "actSeconds": r.CasinoActSeconds = Real(v, w, 2, 300); break;
                            case "showSeconds": r.CasinoShowSeconds = Real(v, w, 0.5f, 30); break;
                            case "useRange": r.CasinoUseRange = Real(v, w, 0.5f, 10); break;
                            case "leaveRange": r.CasinoLeaveRange = Real(v, w, 1, 30); break;
                            case "highLowEdge": r.HighLowEdge = Real(v, w, 0, 0.5f); break;
                            case "highLowMaxSteps": r.HighLowMaxSteps = Whole(v, w, 1, 10); break;
                            case "announceAt": r.CasinoAnnounceAt = Whole(v, w, 0, 10000000); break;
                            default: return false;
                        }
                        return true;
                    });
                    if (r.CasinoLeaveRange < r.CasinoUseRange)
                        throw new WeaponFileException($"{at}: leaveRange is below useRange");
                    break;
                case "bots":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "buy": r.BotsBuy = Flag(v, w); break;
                            case "reserve": r.BotReserve = Whole(v, w, 0, 100000); break;
                            case "quietRange": r.BotQuietRange = Real(v, w, 0, 100); break;
                            case "shopRange": r.BotShopRange = Real(v, w, 1, 500); break;
                            case "perkOrder":
                                if (v.ValueKind != JsonValueKind.Array)
                                    throw new WeaponFileException($"{w}: a list of perk keys");
                                r.BotPerkOrder = v.EnumerateArray().Select(x => WeaponFile.Text(x, w)).ToArray();
                                break;
                            case "boxSurplus": r.BotBoxSurplus = Whole(v, w, 0, 100000); break;
                            case "slotSurplus": r.BotSlotSurplus = Whole(v, w, 0, 100000); break;
                            case "wallBetter": r.BotWallBetter = Real(v, w, 1, 10); break;
                            case "doors": r.BotDoors = Flag(v, w); break;
                            case "thinkSeconds": r.BotThinkSeconds = Real(v, w, 0.05f, 10); break;
                            case "giveUpSeconds": r.BotGiveUpSeconds = Real(v, w, 1, 120); break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                case "horde":
                {
                    var h = r.Horde;
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "nearRange": h.NearRange = Real(v, w, 1, 500); break;
                            case "midRange": h.MidRange = Real(v, w, 1, 500); break;
                            case "farRange": h.FarRange = Real(v, w, 1, 1000); break;
                            case "thinkEvery":
                                if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 4)
                                    throw new WeaponFileException($"{w}: a list of 4 tick counts (near, mid, far, very far)");
                                h.ThinkEvery = v.EnumerateArray().Select(x => Whole(x, w, 1, 60)).ToArray();
                                break;
                            case "groupThinkSeconds": h.GroupThinkSeconds = Real(v, w, 0.05f, 10); break;
                            case "tearers": h.Tearers = Whole(v, w, 1, 8); break;
                            case "queueSplitAt": h.QueueSplitAt = Whole(v, w, 1, 100); break;
                            case "flankRange": h.FlankRange = Real(v, w, 0, 200); break;
                            case "queueCost": h.QueueCost = Real(v, w, 0, 20); break;
                            case "surroundMin": h.SurroundMin = Real(v, w, 0, 20); break;
                            case "surroundRange": h.SurroundRange = Real(v, w, 0, 50); break;
                            case "surroundRadius": h.SurroundRadius = Real(v, w, 0, 10); break;
                            case "pressureWeight": h.PressureWeight = Real(v, w, 0, 20); break;
                            case "pressureFree": h.PressureFree = Whole(v, w, 0, 500); break;
                            case "areaPenalty": h.AreaPenalty = Real(v, w, 0, 500); break;
                            case "hearGunfire": h.HearGunfire = Real(v, w, 0, 300); break;
                            case "hearPowerUp": h.HearPowerUp = Real(v, w, 0, 300); break;
                            case "hurtAlert": h.HurtAlert = Real(v, w, 0, 100); break;
                            case "engagedRange": h.EngagedRange = Real(v, w, 0, 100); break;
                            case "scatterScale": h.ScatterScale = Real(v, w, 0, 5); break;
                            case "scatterSeconds": h.ScatterSeconds = Real(v, w, 0, 5); break;
                            case "explosionStagger": h.ExplosionStagger = Real(v, w, 0, 3); break;
                            case "stuckSeconds": h.StuckSeconds = Real(v, w, 0.2f, 30); break;
                            case "stuckProgress": h.StuckProgress = Real(v, w, 0, 10); break;
                            case "stuckMinMembers": h.StuckMinMembers = Whole(v, w, 1, 500); break;
                            case "regroupSeconds": h.RegroupSeconds = Real(v, w, 0, 10); break;
                            case "lureRange": h.LureRange = Real(v, w, 0, 200); break;
                            case "separationRadius": h.SeparationRadius = Real(v, w, 0.1f, 3); break;
                            case "separationStrength": h.SeparationStrength = Real(v, w, 0, 10); break;
                            case "flowFields": h.FlowFields = Whole(v, w, 4, 128); break;
                            case "flowBuildsPerTick": h.FlowBuildsPerTick = Whole(v, w, 1, 8); break;
                            case "flowRefreshSeconds": h.FlowRefreshSeconds = Real(v, w, 0.1f, 60); break;
                            default: return false;
                        }
                        return true;
                    });
                    if (!(h.NearRange <= h.MidRange && h.MidRange <= h.FarRange))
                        throw new WeaponFileException($"{at}: nearRange <= midRange <= farRange");
                    break;
                }
                case "crowd":
                {
                    var c = r.Crowd;
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "thinkMin": c.ThinkMin = Whole(v, w, 1, 120); break;
                            case "thinkMax": c.ThinkMax = Whole(v, w, 1, 120); break;
                            case "promoteRange": c.PromoteRange = Real(v, w, 2, 200); break;
                            case "demoteRange": c.DemoteRange = Real(v, w, 2, 300); break;
                            case "demoteSeconds": c.DemoteSeconds = Real(v, w, 0, 60); break;
                            case "promotePerTick": c.PromotePerTick = Whole(v, w, 1, 64); break;
                            case "demotePerTick": c.DemotePerTick = Whole(v, w, 0, 64); break;
                            case "individualCap": c.IndividualCap = Whole(v, w, 1, Game.MaxZombies); break;
                            case "speed": c.Speed = Real(v, w, 0.1f, 10); break;
                            case "speedSpread": c.SpeedSpread = Real(v, w, 0, 0.9f); break;
                            case "separationRadius": c.SeparationRadius = Real(v, w, 0.3f, 3); break;
                            case "separationStrength": c.SeparationStrength = Real(v, w, 0, 20); break;
                            case "separationMax": c.SeparationMax = Whole(v, w, 1, 32); break;
                            case "flowCell": c.FlowCell = Real(v, w, 0.5f, 8); break;
                            case "flowRefreshTicks": c.FlowRefreshTicks = Whole(v, w, 2, 600); break;
                            case "flowCellsPerTick": c.FlowCellsPerTick = Whole(v, w, 64, 1000000); break;
                            case "netCell": c.NetCell = Real(v, w, 1, 16); break;
                            case "netEvery": c.NetEvery = Whole(v, w, 1, 60); break;
                            case "netFarRange": c.NetFarRange = Real(v, w, 0, 1000); break;
                            case "netFarEvery": c.NetFarEvery = Whole(v, w, 1, 30); break;
                            default: return false;
                        }
                        return true;
                    });
                    if (c.ThinkMax < c.ThinkMin)
                        throw new WeaponFileException($"{at}: thinkMin <= thinkMax");
                    if (c.DemoteRange < c.PromoteRange + 2)
                        throw new WeaponFileException($"{at}: demoteRange at least 2 m over promoteRange (the hysteresis)");
                    break;
                }
                case "climb":
                {
                    var c = r.Climb;
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "layerHeight": c.LayerHeight = Real(v, w, 0.5f, 3); break;
                            case "columnSpacing": c.ColumnSpacing = Real(v, w, 0.4f, 2); break;
                            case "layers": c.Layers = Whole(v, w, 1, 30); break;
                            case "baseRows": c.BaseRows = Whole(v, w, 0, 4); break;
                            case "climbEvery": c.ClimbEvery = Whole(v, w, 0, 100); break;
                            case "layerShrink": c.LayerShrink = Whole(v, w, 0, 10); break;
                            case "settleSpeed": c.SettleSpeed = Real(v, w, 0.5f, 20); break;
                            case "pileClimbSpeed": c.PileClimbSpeed = Real(v, w, 0.2f, 20); break;
                            case "wallClimbSpeed": c.WallClimbSpeed = Real(v, w, 0.1f, 20); break;
                            case "vaultSeconds": c.VaultSeconds = Real(v, w, 0.1f, 5); break;
                            case "fallStun": c.FallStun = Real(v, w, 0, 10); break;
                            case "fallPush": c.FallPush = Real(v, w, 0, 10); break;
                            case "extraZombies": c.ExtraZombies = Whole(v, w, 0, 1000); break;
                            case "extraPerRound": c.ExtraPerRound = Whole(v, w, 0, 100); break;
                            case "extraMax": c.ExtraMax = Whole(v, w, 0, 1000); break;
                            case "rewardPoints": c.RewardPoints = Whole(v, w, 0, 1000000); break;
                            case "rewardPerRound": c.RewardPerRound = Whole(v, w, 0, 100000); break;
                            case "rewardMax": c.RewardMax = Whole(v, w, 0, 1000000); break;
                            case "rewardParts": c.RewardParts = Whole(v, w, 0, 10); break;
                            case "rewardRoundBonus": c.RewardRoundBonus = Whole(v, w, 0, 100); break;
                            case "rewardPowerUp":
                            {
                                var s = v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new WeaponFileException($"{w}: a power-up name (\"maxAmmo\", ...) or \"none\"");
                                if (s.Equals("none", StringComparison.OrdinalIgnoreCase))
                                    c.RewardPowerUp = -1;
                                else if (Enum.TryParse<ZPowerKind>(s, true, out var pk) && pk != ZPowerKind.COUNT && Enum.IsDefined(pk) && !int.TryParse(s, out _))
                                    c.RewardPowerUp = (int)pk;
                                else
                                    throw new WeaponFileException($"{w}: unknown power-up \"{s}\"");
                                break;
                            }
                            default: return false;
                        }
                        return true;
                    });
                    break;
                }
                case "limbs":
                {
                    var l = r.Limbs;
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "note": break;
                            case "enabled": l.Enabled = Flag(v, w); break;
                            case "healthFraction": l.HealthFraction = Real(v, w, 0.01f, 10); break;
                            case "healthMin": l.HealthMin = Whole(v, w, 1, 100000); break;
                            case "damageShare": l.DamageShare = Real(v, w, 0, 10); break;
                            case "blastShare": l.BlastShare = Real(v, w, 0, 10); break;
                            case "oneArmDamage": l.OneArmDamage = Real(v, w, 0, 2); break;
                            case "oneArmWindup": l.OneArmWindup = Real(v, w, 0.2f, 5); break;
                            case "oneArmSwing": l.OneArmSwing = Real(v, w, 0.2f, 5); break;
                            case "biteDamage": l.BiteDamage = Real(v, w, 0, 2); break;
                            case "biteReach": l.BiteReach = Real(v, w, 0.2f, 2); break;
                            case "biteWindup": l.BiteWindup = Real(v, w, 0.2f, 5); break;
                            case "oneLegSpeed": l.OneLegSpeed = Real(v, w, 0.05f, 1); break;
                            case "crawlSpeed": l.CrawlSpeed = Real(v, w, 0.05f, 1); break;
                            case "crawlReach": l.CrawlReach = Real(v, w, 0.2f, 2); break;
                            case "crawlAttackHeight": l.CrawlAttackHeight = Real(v, w, 0.1f, 2); break;
                            case "crawlDamage": l.CrawlDamage = Real(v, w, 0, 2); break;
                            case "crawlClimb": l.CrawlClimb = Real(v, w, 1, 10); break;
                            case "limbPoints": l.LimbPoints = Whole(v, w, 0, 1000); break;
                            case "goreSeconds": l.GoreSeconds = Real(v, w, 0.5f, 60); break;
                            case "goreCap": l.GoreCap = Whole(v, w, 0, Gibs.Capacity); break;
                            case "types":
                                Fields(v, w, (tk, tv, tw) =>
                                {
                                    if (!Enum.TryParse<ZSpeed>(tk, true, out var sp) || !Enum.IsDefined(sp) || !char.IsLower(tk[0]))
                                        return false;
                                    Fields(tv, tw, (fk, fv, fw) =>
                                    {
                                        switch (fk)
                                        {
                                            case "healthMult": l.HealthMult[(int)sp] = Real(fv, fw, 0.05f, 100); break;
                                            case "severs": l.Severs[(int)sp] = Flag(fv, fw); break;
                                            default: return false;
                                        }
                                        return true;
                                    });
                                    return true;
                                });
                                break;
                            default: return false;
                        }
                        return true;
                    });
                    break;
                }
                default:
                    throw new WeaponFileException($"{source}: unknown key \"{p.Name}\" ({string.Join(", ", Sections)})");
            }
        }
        if (!sawPerks)
            r.Perks.AddRange(Defaults().Perks);
        if (r.BoxMoveMax < r.BoxMoveMin)
            throw new WeaponFileException($"{source}: box \"moveMax\" is below \"moveMin\"");
        if (r.SlotCostMax < r.SlotCostBase)
            throw new WeaponFileException($"{source}: slots \"costMax\" is below \"costBase\"");
        if (r.SpawnDelayMin > r.SpawnDelay)
            throw new WeaponFileException($"{source}: rounds \"spawnDelayMin\" is above \"spawnDelay\"");
        return r;
    }

    /// <summary>The power-up keys in the rules file's "weights", in <see cref="ZPowerKind"/> order.</summary>
    public static readonly string[] PowerKeys = ["maxAmmo", "instaKill", "doublePoints", "nuke", "carpenter", "infiniteAmmo"];

    /// <summary>The power-ups that run for a while (the keys "durations" takes).</summary>
    public static readonly string[] TimedPowerKeys = ["instaKill", "doublePoints", "infiniteAmmo"];

    private static ZombiePerkDef ReadPerk(JsonElement e, string where, List<ZombiePerkDef> others)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: each perk is an object");
        string? name = null, key = null, blurb = "";
        int cost = -1, solo = 0;
        var color = new Vector3(0.8f);
        float dmg = 1, reload = 1, rate = 1, revive = 1, move = 1, head = 1, melee = 1, chance = 0, repair = 1, repairPts = 1, parts = 1, plate = 0;
        int headPts = 0, heal = 0, extra = 0, salvage = 0;
        var icon = "";
        var self = false;
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where}.{p.Name}";
            switch (p.Name)
            {
                case "name": name = WeaponFile.Text(p.Value, w); break;
                case "key": key = WeaponFile.Text(p.Value, w).ToLowerInvariant(); break;
                case "blurb": blurb = WeaponFile.Text(p.Value, w); break;
                case "cost": cost = Whole(p.Value, w, 0, 1000000); break;
                case "soloCost": solo = Whole(p.Value, w, 0, 1000000); break;
                case "color":
                    try
                    {
                        color = MapFile.ToColour(p.Value, w, "");
                    }
                    catch (MapFileException ex)
                    {
                        throw new WeaponFileException(ex.Message);
                    }
                    break;
                case "damageTaken": dmg = Real(p.Value, w, 0.05f, 1); break;
                case "reload": reload = Real(p.Value, w, 0.1f, 1); break;
                case "fireRate": rate = Real(p.Value, w, 1, 3); break;
                case "revive": revive = Real(p.Value, w, 0.1f, 1); break;
                case "selfRevive": self = p.Value.ValueKind == JsonValueKind.True || (p.Value.ValueKind != JsonValueKind.False ? throw new WeaponFileException($"{w}: true or false") : false); break;
                case "moveSpeed": move = Real(p.Value, w, 1, 1.6f); break;
                case "headshotDamage": head = Real(p.Value, w, 1, 5); break;
                case "headshotPoints": headPts = Whole(p.Value, w, 0, 10000); break;
                case "meleeDamage": melee = Real(p.Value, w, 1, 10); break;
                case "meleeHeal": heal = Whole(p.Value, w, 0, 100); break;
                case "powerUpChance": chance = Real(p.Value, w, 0, 1); break;
                case "powerUpExtra": extra = Whole(p.Value, w, 0, 20); break;
                case "repair": repair = Real(p.Value, w, 0.1f, 1); break;
                case "repairPoints": repairPts = Real(p.Value, w, 1, 10); break;
                case "partDrops": parts = Real(p.Value, w, 1, 10); break;
                case "salvageBonus": salvage = Whole(p.Value, w, 0, 10); break;
                case "plateSeconds": plate = Real(p.Value, w, 0, 600); break;
                case "icon": icon = WeaponFile.Text(p.Value, w).ToLowerInvariant(); break;
                default:
                    throw new WeaponFileException($"{where}: unknown perk key \"{p.Name}\" (name, key, blurb, cost, soloCost, color, icon, damageTaken, reload, fireRate, revive, selfRevive, "
                                                  + "moveSpeed, headshotDamage, headshotPoints, meleeDamage, meleeHeal, powerUpChance, powerUpExtra, repair, repairPoints, partDrops, salvageBonus, plateSeconds)");
            }
        }
        if (name == null || key == null || cost < 0)
            throw new WeaponFileException($"{where}: a perk needs \"name\", \"key\" and \"cost\"");
        if (others.Any(o => o.Key == key || o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new WeaponFileException($"{where}: two perks called '{name}' / '{key}'");
        if (icon.Length > 0 && !ZombiePerkIcons.Known(icon))
            throw new WeaponFileException($"{where}.icon: unknown icon \"{icon}\" ({string.Join(", ", ZombiePerkIcons.Names)})");
        return new ZombiePerkDef
        {
            Name = name, Key = key, Blurb = blurb, Cost = cost, SoloCost = solo, Color = color, DamageTaken = dmg, Reload = reload, FireRate = rate,
            Revive = revive, SelfRevive = self, MoveSpeed = move, HeadshotDamage = head, HeadshotPoints = headPts, MeleeDamage = melee, MeleeHeal = heal,
            PowerUpChance = chance, PowerUpExtra = extra, Repair = repair, RepairPoints = repairPts, PartDrops = parts, SalvageBonus = salvage, PlateSeconds = plate,
            Icon = icon,
        };
    }

    private static void Fields(JsonElement e, string where, Func<string, JsonElement, string, bool> set)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected an object");
        foreach (var p in e.EnumerateObject())
            if (!set(p.Name, p.Value, $"{where}.{p.Name}"))
                throw new WeaponFileException($"{where}: unknown key \"{p.Name}\"");
    }

    /// <summary>One bet level's consolation weights ({"common": 60, "uncommon": 40}), per rarity (common first).</summary>
    private static float[] ConsolationRow(JsonElement e, string where)
    {
        var row = new float[(int)Rarity.Wonder + 1];
        Fields(e, where, (k, v, w) =>
        {
            if (!Enum.TryParse<Rarity>(k, true, out var t) || !Enum.IsDefined(t))
                return false;
            row[(int)t] = Real(v, w, 0, 1000);
            return true;
        });
        if (row.Sum() <= 0)
            throw new WeaponFileException($"{where}: give at least one rarity a weight above 0");
        return row;
    }

    private static int Whole(JsonElement e, string where, int min, int max)
    {
        var v = WeaponFile.Int(e, where);
        return v >= min && v <= max ? v : throw new WeaponFileException($"{where}: {v} is outside {min}-{max}");
    }

    private static bool Flag(JsonElement e, string where) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new WeaponFileException($"{where}: true or false"),
    };

    private static float Real(JsonElement e, string where, float min, float max)
    {
        var v = WeaponFile.Number(e, where);
        return v >= min && v <= max ? v : throw new WeaponFileException($"{where}: {v} is outside {min}-{max}");
    }
}

/// <summary>
/// The horde climbing a face (the rules file's "climb" section; Source/Game/ZombiesClimb.cs; docs/zombies/ZOMBIES-MODE.md,
/// "The bell: a siege up the tower"): the living pyramid's shape, how fast the horde climbs and falls, the zombies a
/// siege adds to the round and what surviving it pays.
/// </summary>
public sealed class ClimbTuning
{
    /// <summary>The pyramid: each layer of bodies this much higher, columns this far apart along the face, this many layers at most, the bottom layers this many rows deep.</summary>
    public float LayerHeight = 1.3f, ColumnSpacing = 0.75f;

    public int Layers = 10, BaseRows = 2;

    /// <summary>Each layer up has this many fewer columns than the one under it (a pyramid; at least one column).</summary>
    public int LayerShrink = 2;

    /// <summary>One zombie in this many reaching the foot climbs straight up the wall instead of joining the pyramid (0: only once it's full).</summary>
    public int ClimbEvery = 3;

    /// <summary>How fast a body moves to its place in the pile, climbers go up the pile and up the bare wall above it (m/s), and the vault over the parapet (s).</summary>
    public float SettleSpeed = 3f, PileClimbSpeed = 2f, WallClimbSpeed = 0.8f, VaultSeconds = 0.8f;

    /// <summary>A shot zombie lets go: pushed off the wall this fast (m/s) and dazed for this long once it lands (s).</summary>
    public float FallStun = 1.2f, FallPush = 2.5f;

    /// <summary>Zombies a siege adds to the round: this many plus this many a round, at most this many.</summary>
    public int ExtraZombies = 12, ExtraPerRound = 2, ExtraMax = 80;

    /// <summary>Surviving pays every player standing: points (plus per round, at most), loose parts rolled as if the round were this many later, and a power-up at the top (-1: none).</summary>
    public int RewardPoints = 1500, RewardPerRound = 100, RewardMax = 6000, RewardParts = 2, RewardRoundBonus = 10;

    public int RewardPowerUp = (int)ZPowerKind.MaxAmmo;

    public int ExtraFor(int round) => Math.Min(ExtraMax, ExtraZombies + ExtraPerRound * Math.Max(1, round));

    public int RewardFor(int round) => Math.Min(RewardMax, RewardPoints + RewardPerRound * Math.Max(1, round));
}

/// <summary>
/// A number that changes with the round (the rules file's curves): points of (round, value), straight lines between
/// them, flat before the first and after the last. In the file: <c>[[1, 24], [10, 60], [20, 150]]</c>.
/// </summary>
public sealed class RoundCurve
{
    private readonly (int Round, float Value)[] _points;

    public RoundCurve(IEnumerable<(int Round, float Value)> points)
    {
        _points = points.OrderBy(p => p.Round).ToArray();
        if (_points.Length == 0)
            throw new ArgumentException("a curve needs a point");
    }

    public IReadOnlyList<(int Round, float Value)> Points => _points;

    /// <summary>The value in round <paramref name="round"/>.</summary>
    public float At(int round)
    {
        var p = _points;
        if (round <= p[0].Round)
            return p[0].Value;
        for (var i = 1; i < p.Length; i++)
            if (round <= p[i].Round)
            {
                var t = (round - p[i - 1].Round) / (float)Math.Max(1, p[i].Round - p[i - 1].Round);
                return p[i - 1].Value + (p[i].Value - p[i - 1].Value) * t;
            }
        return p[^1].Value;
    }

    /// <summary>Reads <c>[[round, value], ...]</c> (rounds 1-1000 rising, values in <paramref name="min"/>-<paramref name="max"/>).</summary>
    public static RoundCurve Read(JsonElement e, string where, float min, float max)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() is < 1 or > 32)
            throw new WeaponFileException($"{where}: a list of 1-32 [round, value] pairs");
        var list = new List<(int, float)>();
        var last = 0;
        foreach (var p in e.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 2)
                throw new WeaponFileException($"{where}: each point is [round, value]");
            var round = WeaponFile.Int(p[0], where);
            var value = WeaponFile.Number(p[1], where);
            if (round < 1 || round > 1000 || round <= last)
                throw new WeaponFileException($"{where}: rounds go up from 1 (got {round} after {last})");
            if (value < min || value > max)
                throw new WeaponFileException($"{where}: {value} is outside {min}-{max}");
            last = round;
            list.Add((round, value));
        }
        return new RoundCurve(list);
    }

    public override string ToString() => string.Join(" ", _points.Select(p => $"r{p.Round}:{p.Value:0.##}"));
}
