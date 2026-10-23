namespace CallOfSlop;

/// <summary>Host setting: which items bots may carry (settings.txt <c>bot_gear=match|everything|starter</c>).</summary>
public enum BotGearMode : byte
{
    /// <summary>What the people in the game have unlocked: their average account level (the default).</summary>
    MatchPlayers,

    /// <summary>Anything, like before this setting existed.</summary>
    Everything,

    /// <summary>Only what a level-1 player has.</summary>
    Starter,
}

/// <summary>
/// The rules for bot loadouts that match the lobby's level (pure functions; the game side is in <see cref="Game"/>,
/// below). A bot's "gear level" is an account level; its loadout only holds items a player of that level could have:
///
///   - Lobby level: the average account level of the people in the game (bots don't count), rounded. A player who has
///     prestiged counts as the max level (they have earned everything at least once). A player whose level is unknown
///     (0: an old client) doesn't count. Offline single player: your own level. Nobody with a known level (a bot-only
///     game, the self-test's seeded matches): everything, exactly as before.
///   - Per-bot jitter: each bot sits up to <see cref="JitterRange"/> levels either side of the lobby level, fixed by its
///     seed, so not every bot is the same. The upward side is capped at (lobby level - 1): a brand-new lobby's bots never
///     out-gear it. Clamped to 1..max.
///   - Weapons, grenades, equipment, perks and kill streaks: unlocked at or below the gear level (the same table as
///     players, <see cref="Progress"/>).
///   - Attachments (weapon XP): a bot's weapon XP on a gun is taken as (gear level - the gun's unlock level) /
///     <see cref="AttachmentLevels"/> of the gun's max XP, so a gun gains attachments over the six levels after it
///     unlocks (a level is 2-3 matches, a gun maxes out in 3-7), and a just-unlocked or starter gun at level 1 has only
///     what needs no XP.
///   - At the max level (or <see cref="BotGearMode.Everything"/>) everything is open with full kits, and the loadout
///     is drawn exactly as before (same random draws), so seeded matches play as they always did.
/// </summary>
public static class BotGear
{
    /// <summary>How many levels a bot's gear may sit above or below the lobby's level.</summary>
    public const int JitterRange = 3;

    /// <summary>Levels after a gun's unlock until a bot has all its attachments.</summary>
    public const int AttachmentLevels = 6;

    /// <summary>The gear level that opens everything (the max account level).</summary>
    public static int Full => Progress.MaxLevel;

    /// <summary>The level a person counts as: 0 if unknown, the max level once they have prestiged, otherwise their level.</summary>
    public static int EffectiveLevel(int prestige, int level) =>
        prestige > 0 ? Full : level <= 0 ? 0 : Math.Min(level, Full);

    /// <summary>The rounded average of <paramref name="count"/> levels adding up to <paramref name="sum"/> (0 for nobody).</summary>
    public static int Average(int sum, int count) =>
        count <= 0 ? 0 : Math.Clamp((int)Math.Round(sum / (double)count, MidpointRounding.AwayFromZero), 1, Full);

    /// <summary>A bot's fixed offset from the lobby level, -<see cref="JitterRange"/>..+<see cref="JitterRange"/>, from its seed.</summary>
    public static int Jitter(int seed) =>
        Math.Clamp((int)(TacticalGear.Roll((uint)seed, 0x6EA2u, 0xB07u) * (2 * JitterRange + 1)) - JitterRange, -JitterRange, JitterRange);

    /// <summary>The settings.txt word for the setting.</summary>
    public static string Key(BotGearMode mode) => mode switch { BotGearMode.Everything => "everything", BotGearMode.Starter => "starter", _ => "match" };

    /// <summary>Reads <see cref="Key"/> (anything else: Match players).</summary>
    public static BotGearMode Parse(string key) => key.Trim().ToLowerInvariant() switch
    {
        "everything" => BotGearMode.Everything,
        "starter" => BotGearMode.Starter,
        _ => BotGearMode.MatchPlayers,
    };

    /// <summary>The name in the menus.</summary>
    public static string Name(BotGearMode mode) => mode switch { BotGearMode.Everything => "Everything", BotGearMode.Starter => "Starter", _ => "Match players" };

    /// <summary>A bot's gear level for the setting, the lobby's average level (0: nobody known) and the bot's seed.</summary>
    public static int BotLevel(BotGearMode mode, int average, int seed)
    {
        if (mode == BotGearMode.Starter)
            return 1;
        if (mode == BotGearMode.Everything || average <= 0)
            return Full;
        var jitter = Math.Min(Jitter(seed), average - 1); // never above a brand-new lobby
        return Math.Clamp(average + jitter, 1, Full);
    }

    /// <summary>May a bot of this gear level carry the weapon?</summary>
    public static bool Weapon(WeaponDef w, int level) => level >= Full || UnlockLevel(w) <= level;

    /// <summary>The account level that unlocks the weapon (<see cref="Progress.UnlockLevel(WeaponDef)"/>, cached per weapon list).</summary>
    public static int UnlockLevel(WeaponDef w)
    {
        var all = Weapons.All;
        var levels = _weaponLevels;
        if (!ReferenceEquals(all, _weaponSource) || levels == null)
        {
            levels = new int[all.Length];
            foreach (var x in all)
                if (x.Id < levels.Length)
                    levels[x.Id] = Progress.UnlockLevel(x);
            _weaponLevels = levels;
            _weaponSource = all;
            _attachXp = new int[all.Length][];
        }
        return w.Id < levels.Length && ReferenceEquals(all[w.Id], w) ? levels[w.Id] : Progress.UnlockLevel(w);
    }

    private static WeaponDef[]? _weaponSource;
    private static int[]? _weaponLevels;
    private static int[]?[] _attachXp = [];

    /// <summary>The weapon XP a bot of this gear level has on the gun (see the class notes).</summary>
    public static int WeaponXp(WeaponDef w, int level)
    {
        if (level >= Full)
            return int.MaxValue;
        if (_maxXp.Length != Weapons.All.Length || !ReferenceEquals(_maxXpSource, Weapons.All))
        {
            var all = Weapons.All;
            var m = new int[all.Length];
            foreach (var x in all)
                if (x.Id < m.Length)
                    m[x.Id] = Attachments.MaxXp(x);
            _maxXp = m;
            _maxXpSource = all;
        }
        var maxXp = w.Id < _maxXp.Length ? _maxXp[w.Id] : Attachments.MaxXp(w);
        var past = Math.Clamp(level - UnlockLevel(w), 0, AttachmentLevels);
        return (int)((long)maxXp * past / AttachmentLevels);
    }

    private static int[] _maxXp = [];
    private static WeaponDef[]? _maxXpSource;

    /// <summary>May a bot of this gear level put the attachment on the gun?</summary>
    public static bool Attachment(WeaponDef w, AttachmentDef a, int level) =>
        level >= Full || Attachments.UnlockXp(w, a) <= WeaponXp(w, level);

    /// <summary>The same for the <paramref name="index"/>-th of <see cref="Attachments.For"/> (cached unlock XP: bots check these every life).</summary>
    public static bool Attachment(WeaponDef w, int index, int level)
    {
        if (level >= Full)
            return true;
        UnlockLevel(w); // (re)builds the per-weapon caches for this weapon list
        var cache = _attachXp;
        if (w.Id >= cache.Length || !ReferenceEquals(Weapons.All[w.Id], w))
            return Attachment(w, Attachments.For(w)[index], level);
        var xps = cache[w.Id];
        if (xps == null)
        {
            var list = Attachments.For(w);
            xps = new int[list.Count];
            for (var i = 0; i < xps.Length; i++)
                xps[i] = Attachments.UnlockXp(w, list[i]);
            cache[w.Id] = xps;
        }
        return index < xps.Length && xps[index] <= WeaponXp(w, level);
    }

    /// <summary>May a bot of this gear level carry the grenade or equipment? (None always.)</summary>
    public static bool Throwable(ThrowableKind k, int level)
    {
        if (k == ThrowableKind.None || level >= Full)
            return true;
        var levels = _throwableLevels ??= BuildThrowableLevels();
        return (int)k < levels.Length ? levels[(int)k] <= level : Progress.UnlockLevel(k) <= level;
    }

    private static int[]? _throwableLevels;

    private static int[] BuildThrowableLevels()
    {
        var kinds = Enum.GetValues<ThrowableKind>();
        var levels = new int[kinds.Length];
        foreach (var k in kinds)
            if ((int)k < levels.Length)
                levels[(int)k] = k == ThrowableKind.None ? 1 : Progress.UnlockLevel(k);
        return levels;
    }

    /// <summary>May a bot of this gear level run the perk?</summary>
    public static bool Perk(PerkId id, int level)
    {
        if (level >= Full)
            return true;
        var levels = _perkLevels ??= BuildPerkLevels();
        return (int)id < levels.Length ? levels[(int)id] <= level : Progress.PerkLevel(id) <= level;
    }

    private static int[]? _perkLevels;

    private static int[] BuildPerkLevels()
    {
        var levels = new int[Perks.All.Length];
        foreach (var p in Perks.All)
            if ((int)p.Id < levels.Length)
                levels[(int)p.Id] = Progress.PerkLevel(p.Id);
        return levels;
    }

    /// <summary>May a bot of this gear level call in the kill streak?</summary>
    public static bool Streak(StreakId id, int level) => level >= Full || KillStreaks.UnlockLevel(id) <= level;

    /// <summary>
    /// The tactical item for a personality and a roll (<see cref="TacticalGear.BotPick"/>), limited to the gear level:
    /// a locked pick moves on to the next item in the personality's list that is unlocked (carrying nothing counts).
    /// </summary>
    public static ThrowableKind Equipment(BotPersonality kind, double roll, int level)
    {
        var pool = TacticalGear.BotPool(kind);
        var start = Math.Clamp((int)(roll * pool.Length), 0, pool.Length - 1);
        for (var i = 0; i < pool.Length; i++)
        {
            var k = pool[(start + i) % pool.Length];
            if (Throwable(k, level))
                return k;
        }
        return ThrowableKind.None;
    }

    /// <summary>The perks of a category in unlock order (<see cref="Perks.In"/>, cached: bots pick from these every life).</summary>
    public static PerkDef[] PerkPool(PerkCategory c)
    {
        var i = (int)c;
        if (i >= _perkPools.Length)
            return Perks.In(c).ToArray();
        return _perkPools[i] ??= Perks.In(c).ToArray();
    }

    private static readonly PerkDef[]?[] _perkPools = new PerkDef[]?[Enum.GetValues<PerkCategory>().Length];
}

/// <summary>A bot's gear level (BotGear.cs).</summary>
public sealed partial class BotBrain
{
    /// <summary>The gear level its current loadout was drawn at (<see cref="BotGear"/>).</summary>
    public int GearLevel = BotGear.Full;

    /// <summary>The gear level its kill streaks were picked at (-1: not picked by the gear rules yet).</summary>
    public int StreakLevel = -1;
}

/// <summary>Bot loadouts that match the lobby's level (rules in <see cref="BotGear"/>; the loadout itself in BotPersonality.cs).</summary>
public sealed partial class Game
{
    /// <summary>Host setting: what bots may carry (<see cref="BotGearMode"/>). Server only.</summary>
    public BotGearMode BotGearMode = BotGearMode.MatchPlayers;

    /// <summary>The people's average account level the bots last matched (0: nobody with a known level). Server only.</summary>
    public int BotGearAverage { get; private set; }

    /// <summary>
    /// The average account level of the people in the game right now (bots don't count; prestige counts as the max level;
    /// unknown levels are left out), or 0 if nobody's level is known. The host's own level comes from its progression.
    /// </summary>
    public int HumanAverageLevel()
    {
        int sum = 0, count = 0;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null)
                continue;
            var level = IsServer && p.Id == LocalId && Progress is { } prog
                ? BotGear.EffectiveLevel(prog.Prestige, prog.Level)
                : BotGear.EffectiveLevel(p.Prestige, p.Level);
            if (level <= 0)
                continue;
            sum += level;
            count++;
        }
        return BotGear.Average(sum, count);
    }

    /// <summary>Works the lobby's level out again (people join and leave, levels go up): on every roster change and every bot life.</summary>
    private void RefreshBotGear()
    {
        if (!IsServer)
            return;
        var avg = HumanAverageLevel();
        if (avg == BotGearAverage)
            return;
        BotGearAverage = avg;
        if (BotGearMode == BotGearMode.MatchPlayers && WantedBots > 0)
            Console.WriteLine(avg > 0 ? $"[bots] gear matches the players' average level {avg}" : "[bots] gear: everything (no player level known)");
    }

    /// <summary>The gear level a bot draws its next loadout at.</summary>
    public int BotGearLevel(Player bot) => BotGear.BotLevel(BotGearMode, BotGearAverage, bot.Bot?.Seed ?? bot.Id);

    /// <summary>
    /// A bot's kill streaks at spawn (called from StreakOnSpawn): picked once like before, and picked again whenever its
    /// gear level has changed since (the lobby's level moved), from what that level has unlocked.
    /// </summary>
    private void BotStreakPick(Player p, StreakState s)
    {
        var level = p.Bot?.GearLevel ?? BotGear.Full;
        if (!s.BotPicked)
        {
            s.Want = RandomStreaks(level);
            s.BotPicked = true;
            if (p.Bot != null)
                p.Bot.StreakLevel = level;
            return;
        }
        if (p.Bot is not { } b || b.StreakLevel == level)
            return;
        if (b.StreakLevel < 0)
        {
            b.StreakLevel = level; // set by hand (the self-tests): keep it
            return;
        }
        b.StreakLevel = level;
        s.Want = SeededStreaks(p, level);
    }

    /// <summary>Three unlocked streaks in an order fixed by the bot (its own hash: the shared random sequence is untouched).</summary>
    private static StreakSet SeededStreaks(Player p, int level)
    {
        var seed = (uint)(p.Bot?.Seed ?? p.Id);
        StreakId a = StreakId.None, b = StreakId.None, c = StreakId.None;
        double ra = 2, rb = 2, rc = 2;
        foreach (var def in KillStreaks.All)
        {
            if (def.Id == StreakId.None || !BotGear.Streak(def.Id, level))
                continue;
            var r = TacticalGear.Roll(seed, (uint)level, 0x5700u + (uint)def.Id);
            if (r < ra)
            {
                (c, rc) = (b, rb);
                (b, rb) = (a, ra);
                (a, ra) = (def.Id, r);
            }
            else if (r < rb)
            {
                (c, rc) = (b, rb);
                (b, rb) = (def.Id, r);
            }
            else if (r < rc)
            {
                (c, rc) = (def.Id, r);
            }
        }
        return new StreakSet(a, b, c).Sanitized();
    }

    // ---- Self-test hooks --------------------------------------------------------------------

    /// <summary>Self-test: draws a bot's next loadout as a spawn would (the lobby level included) and picks its streaks.</summary>
    public void SelfTestPickBotLoadout(Player bot)
    {
        PickBotLoadout(bot);
        BotStreakPick(bot, bot.Streak);
    }

    /// <summary>Self-test: only the kill streak part of a bot's spawn.</summary>
    public void SelfTestStreakPick(Player bot) => BotStreakPick(bot, bot.Streak);

    /// <summary>Self-test: only the loadout draw.</summary>
    public void SelfTestDrawOnly(Player bot) => PickBotLoadout(bot);

    /// <summary>Self-test: the lobby level as the roster code works it out after a join or a leave.</summary>
    public void SelfTestRefreshBotGear() => RefreshBotGear();
}
