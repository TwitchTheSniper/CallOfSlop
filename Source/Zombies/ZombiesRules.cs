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

    /// <summary>Solo price (Quick Revive is cheaper alone, World at War style). 0 = the same as <see cref="Cost"/>.</summary>
    public int SoloCost;

    public string Blurb = "";
    public Vector3 Color = new(0.8f, 0.8f, 0.8f);

    /// <summary>Zombie hits do this much damage (Juggernog: 0.4, about 2.5x the health).</summary>
    public float DamageTaken = 1f;

    /// <summary>Reload time multiplier (Speed Cola: 0.5).</summary>
    public float Reload = 1f;

    /// <summary>Fire rate multiplier (Double Tap: 1.33).</summary>
    public float FireRate = 1f;

    /// <summary>Revive time multiplier for the reviver (Quick Revive: 0.5).</summary>
    public float Revive = 1f;

    /// <summary>Solo only: being downed with this perk gets you back up on your own after a few seconds (the perk is used up).</summary>
    public bool SelfRevive;

    public int Price(bool solo) => solo && SoloCost > 0 ? SoloCost : Cost;
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
    public int CountBase = 24, CountPerExtraPlayer = 6;

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

    /// <summary>Most zombies alive at once (performance cap; the rest wait in a queue).</summary>
    public int MaxAlive = 24;

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

    // ---- Buyables -----------------------------------------------------------------------------------------
    /// <summary>How long the interact key is held to buy something (seconds).</summary>
    public float BuyHoldSeconds = 0.25f;

    /// <summary>A hard cap on perks held (8: every perk; there's no real cap since 2026-10-26).</summary>
    public int MaxPerks = 8;

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

    // ---- Co-op bots spending their points (ZombieBotsShop.cs; developer answer, 2026-10-26) --------------------
    /// <summary>Bots buy doors, wall guns, the box, perks, the slot upgrade and slot pulls with their own points.</summary>
    public bool BotsBuy = true;

    /// <summary>Points a bot keeps back; a zombie this close (m) mid-round stops its shopping; how far from its post (m) it shops.</summary>
    public int BotReserve = 250;

    public float BotQuietRange = 10f, BotShopRange = 40f;

    /// <summary>The perks a bot wants, most wanted first (rules keys).</summary>
    public string[] BotPerkOrder = ["jugg", "revive", "speed", "doubletap", "secondwind"];

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

    /// <summary>Bad-luck protection: at most this many "nothing" results in a row per player per machine (the next one pays coins: points back).</summary>
    public int SlotMaxNothing = 3;

    /// <summary>Every Nth round is a jackpot round: the wonder symbol is on the reels and these weights are added to legendary and wonder.</summary>
    public int SlotJackpotEvery = 5;

    public float SlotJackpotLegendary = 6f, SlotJackpotWonder = 4f;

    /// <summary>
    /// Payouts: points back (times the price) for a coin; and a part you already hold this many of loose pays the coin
    /// payout instead ("dupePointsAt"; 0 = always keep the part). Zombies has no scrap since 2026-10-27.
    /// </summary>
    public float SlotPointsBack = 1.5f;

    public int SlotDupePointsAt = 3;

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
    public bool SlotPaysMelee(int category) => category < 0 || category >= SlotCategoryMelee.Length || SlotCategoryMelee[category];

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

    /// <summary>The built-in rules (the same numbers the shipped file has) with the four World at War perks.</summary>
    public static ZombiesRules Defaults()
    {
        var r = new ZombiesRules();
        r.Perks.Add(new ZombiePerkDef { Name = "Juggernog", Key = "jugg", Cost = 2500, DamageTaken = 0.4f, Color = new Vector3(0.85f, 0.15f, 0.15f), Blurb = "Zombie hits do 40 percent of their damage." });
        r.Perks.Add(new ZombiePerkDef { Name = "Speed Cola", Key = "speed", Cost = 3000, Reload = 0.5f, Color = new Vector3(0.2f, 0.85f, 0.3f), Blurb = "Reload twice as fast." });
        r.Perks.Add(new ZombiePerkDef { Name = "Double Tap", Key = "doubletap", Cost = 2000, FireRate = 1.33f, Color = new Vector3(0.95f, 0.75f, 0.15f), Blurb = "Fire a third faster." });
        r.Perks.Add(new ZombiePerkDef { Name = "Quick Revive", Key = "revive", Cost = 1500, Revive = 0.5f, Color = new Vector3(0.3f, 0.6f, 1f), Blurb = "Revive teammates twice as fast." });
        r.Perks.Add(new ZombiePerkDef { Name = "Second Wind", Key = "secondwind", Cost = 1500, SelfRevive = true, Color = new Vector3(1f, 0.55f, 0.1f), Blurb = "When you go down, you get back up on your own after a few seconds (used up)." });
        return r;
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

    private static readonly string[] Sections = ["format", "note", "rounds", "zombie", "points", "downs", "buy", "perks", "box", "ammo", "parts", "powerUps", "ammoEffects", "slots", "bots"];

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
                case "buy":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "holdSeconds": r.BuyHoldSeconds = Real(v, w, 0, 3); break;
                            case "maxPerks": r.MaxPerks = Whole(v, w, 0, 8); break;
                            case "fullPricePerks": r.FullPricePerks = Whole(v, w, 0, 8); break;
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
                    if (r.Perks.Count > 8)
                        throw new WeaponFileException($"{at}: at most 8 perks (they're sent as one byte of flags)");
                    break;
                case "box":
                    Fields(p.Value, at, (k, v, w) =>
                    {
                        switch (k)
                        {
                            case "moveMin": r.BoxMoveMin = Whole(v, w, 1, 100); break;
                            case "moveMax": r.BoxMoveMax = Whole(v, w, 1, 100); break;
                            case "spinSeconds": r.BoxSpinSeconds = Real(v, w, 0.2f, 20); break;
                            case "offerSeconds": r.BoxOfferSeconds = Real(v, w, 1, 60); break;
                            case "moveSeconds": r.BoxMoveSeconds = Real(v, w, 0.2f, 30); break;
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
        float dmg = 1, reload = 1, rate = 1, revive = 1;
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
                default: throw new WeaponFileException($"{where}: unknown perk key \"{p.Name}\" (name, key, blurb, cost, soloCost, color, damageTaken, reload, fireRate, revive, selfRevive)");
            }
        }
        if (name == null || key == null || cost < 0)
            throw new WeaponFileException($"{where}: a perk needs \"name\", \"key\" and \"cost\"");
        if (others.Any(o => o.Key == key || o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new WeaponFileException($"{where}: two perks called '{name}' / '{key}'");
        return new ZombiePerkDef
        {
            Name = name, Key = key, Blurb = blurb, Cost = cost, SoloCost = solo, Color = color, DamageTaken = dmg, Reload = reload, FireRate = rate,
            Revive = revive, SelfRevive = self,
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
