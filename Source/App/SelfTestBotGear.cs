namespace CallOfSlop;

/// <summary>
/// Bot loadouts that match the lobby's level (Source/Game/BotGear.cs): the lobby level (people only, prestige, unknown
/// levels, joins and leaves), the per-bot jitter, every item a bot carries being unlocked at its gear level (starter gear
/// at level 1, nothing above 30 at level 30), personalities kept, the host setting and its save, determinism, and no
/// allocations per bot life. Short: no bot matches, only loadout draws.
/// </summary>
public static partial class SelfTest
{
    private static void CheckBotGearLevel()
    {
        CheckBotGearRules();
        CheckBotGearSettings();
        CheckBotGearInGame();
        CheckBotGearDeterminism();
    }

    // ---- The pure rules ----------------------------------------------------------------------------------------

    private static void CheckBotGearRules()
    {
        var full = BotGear.Full;
        if (BotGear.EffectiveLevel(0, 0) != 0 || BotGear.EffectiveLevel(0, 12) != 12 || BotGear.EffectiveLevel(1, 3) != full
            || BotGear.EffectiveLevel(2, full) != full || BotGear.EffectiveLevel(0, 999) != full)
            Fail("bot gear: a person's level is wrong (unknown 0, level as is, any prestige = max level)");
        if (BotGear.Average(0, 0) != 0 || BotGear.Average(30 + 41, 2) != 36 || BotGear.Average(5, 1) != 5)
            Fail("bot gear: the average level is wrong (rounded, 0 for nobody)");

        int min = int.MaxValue, max = int.MinValue;
        var seen = new HashSet<int>();
        for (var seed = 0; seed < 2000; seed++)
        {
            var j = BotGear.Jitter(seed);
            min = Math.Min(min, j);
            max = Math.Max(max, j);
            seen.Add(j);
            if (j != BotGear.Jitter(seed))
                Fail("bot gear: a bot's jitter isn't fixed by its seed");
        }
        Line($"bot gear: jitter over 2000 seeds {min}..{max} ({seen.Count} values), max level {full}");
        if (min != -BotGear.JitterRange || max != BotGear.JitterRange || seen.Count != 2 * BotGear.JitterRange + 1)
            Fail($"bot gear: the jitter should cover -{BotGear.JitterRange}..+{BotGear.JitterRange}");

        var bad = 0;
        for (var seed = 0; seed < 500; seed++)
        {
            if (BotGear.BotLevel(BotGearMode.Starter, 30, seed) != 1 || BotGear.BotLevel(BotGearMode.Everything, 3, seed) != full)
                bad++;
            if (BotGear.BotLevel(BotGearMode.MatchPlayers, 0, seed) != full) // nobody known: everything, like before
                bad++;
            if (BotGear.BotLevel(BotGearMode.MatchPlayers, 1, seed) != 1) // a brand-new lobby: starter gear only
                bad++;
            if (BotGear.BotLevel(BotGearMode.MatchPlayers, 2, seed) is < 1 or > 3)
                bad++;
            if (BotGear.BotLevel(BotGearMode.MatchPlayers, 20, seed) is var l20 && (l20 < 17 || l20 > 23 || l20 != 20 + BotGear.Jitter(seed)))
                bad++;
            var lFull = BotGear.BotLevel(BotGearMode.MatchPlayers, full, seed);
            if (lFull < full - BotGear.JitterRange || lFull > full)
                bad++;
        }
        if (bad > 0)
            Fail($"bot gear: {bad} wrong gear levels (Starter 1, Everything max, level 1 lobby 1, level 20 lobby 17-23, nobody known max)");

        // Attachments: none of the XP at a gun's own unlock level, all of it six levels later, everything at max level.
        var gun = Weapons.All.FirstOrDefault(w => w.Slot == Slot.Primary && Attachments.For(w).Count > 3 && Progress.UnlockLevel(w) <= 10);
        if (gun != null)
        {
            var at = WeaponSelect.UnlockLevel(gun);
            var xp0 = BotGear.WeaponXp(gun, at);
            var xp3 = BotGear.WeaponXp(gun, at + 3);
            var xp6 = BotGear.WeaponXp(gun, at + BotGear.AttachmentLevels);
            Line($"bot gear: {gun.Name} (level {at}) weapon XP at its level {xp0}, +3 {xp3}, +6 {xp6} of {Attachments.MaxXp(gun)}");
            if (xp0 != 0 || xp3 <= 0 || xp3 >= Attachments.MaxXp(gun) || xp6 != Attachments.MaxXp(gun) || BotGear.WeaponXp(gun, full) != int.MaxValue)
                Fail("bot gear: the weapon XP rule is wrong (0 at the gun's level, max six levels later)");
        }
    }

    private static void CheckBotGearSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cos_botgear_{Environment.ProcessId}.txt");
        try
        {
            new Settings { BotGear = BotGearMode.Starter }.Save(path);
            var back = Settings.Load(path);
            new Settings { BotGear = BotGearMode.Everything }.Save(path);
            var back2 = Settings.Load(path);
            File.WriteAllText(path, "volume=0.5\n"); // an old save without the key
            var old = Settings.Load(path);
            Line($"bot gear setting: saved Starter -> {back.BotGear}, Everything -> {back2.BotGear}; an old save loads {old.BotGear}");
            if (back.BotGear != BotGearMode.Starter || back2.BotGear != BotGearMode.Everything || old.BotGear != BotGearMode.MatchPlayers
                || new Settings().BotGear != BotGearMode.MatchPlayers || BotGear.Parse("junk") != BotGearMode.MatchPlayers)
                Fail("bot gear setting: doesn't save, or doesn't default to Match players");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    // ---- In a game -------------------------------------------------------------------------------------------

    /// <summary>What's wrong with a bot's loadout for its gear level (null: nothing).</summary>
    private static string? BotGearProblem(Player bot, int level)
    {
        foreach (var id in new[] { bot.WantPrimary, bot.WantSecondary })
            if (Progress.UnlockLevel(Weapons.Get(id)) > level)
                return $"{Weapons.Get(id).Name} unlocks at {Progress.UnlockLevel(Weapons.Get(id))}";
        foreach (var (id, kit) in new[] { (bot.WantPrimary, bot.WantPrimaryKit), (bot.WantSecondary, bot.WantSecondaryKit) })
        {
            var w = Weapons.Get(id);
            foreach (var a in Attachments.For(w))
                if (a.Index != 0 && Attachments.Slot(kit, a.Slot) == a.Index && level < BotGear.Full && Attachments.UnlockXp(w, a) > BotGear.WeaponXp(w, level))
                    return $"{a.Name} on the {w.Name} needs {Attachments.UnlockXp(w, a)} weapon XP";
        }
        foreach (var k in new[] { bot.WantGrenade, bot.WantEquipment })
            if (k != ThrowableKind.None && Progress.UnlockLevel(k) > level)
                return $"{k} unlocks at {Progress.UnlockLevel(k)}";
        foreach (var perk in new[] { bot.WantPerks.Movement, bot.WantPerks.Survival, bot.WantPerks.Damage })
            if (Progress.PerkLevel(perk) > level)
                return $"perk {perk} unlocks at {Progress.PerkLevel(perk)}";
        foreach (var s in bot.Streak.Want.Ids)
            if (KillStreaks.UnlockLevel(s) > level)
                return $"streak {s} unlocks at {KillStreaks.UnlockLevel(s)}";
        if (bot.Hat != HatId.None || bot.Prestige != 0)
            return "wears a prestige hat";
        return null;
    }

    private static void CheckBotGearInGame()
    {
        World.SetMap(Maps.All[0]);
        Game.SeedNextGame = 4242;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 3);
        game.MapVoteEnabled = false;
        game.AddBot(BotDifficulty.Hard, BotPersonality.Marksman, quiet: true);
        game.AddBot(BotDifficulty.Hard, BotPersonality.Rusher, quiet: true);
        var bots = game.Players.Values.Where(p => p.Bot != null).ToList();
        var marksman = bots.FirstOrDefault(b => b.Bot!.Personality == BotPersonality.Marksman);
        var rusher = bots.FirstOrDefault(b => b.Bot!.Personality == BotPersonality.Rusher);
        if (marksman == null || rusher == null)
        {
            Fail("bot gear: couldn't add a Marksman and a Rusher");
            return;
        }
        uint tick = 1000;

        // Picks every bot's loadout n times (as n lives) and returns the first problem, checking each against its gear level.
        string? Draw(int n, Func<int, bool> levelOk, out int lo, out int hi, out int highItems)
        {
            lo = int.MaxValue;
            hi = 0;
            highItems = 0;
            for (var i = 0; i < n; i++)
                foreach (var b in bots)
                {
                    b.SpawnTick = tick++;
                    game.SelfTestPickBotLoadout(b);
                    var lvl = b.Bot!.GearLevel;
                    lo = Math.Min(lo, lvl);
                    hi = Math.Max(hi, lvl);
                    if (!levelOk(lvl))
                        return $"{b.Name} has gear level {lvl}";
                    if (BotGearProblem(b, lvl) is { } why)
                        return $"{b.Name} (gear level {lvl}): {why}";
                    if (Progress.UnlockLevel(Weapons.Get(b.WantPrimary)) > 1 || b.WantGrenade != ThrowableKind.None || b.WantEquipment != ThrowableKind.None)
                        highItems++;
                }
            return null;
        }

        // Nobody's level known (no progression, like the seeded matches): everything, as before.
        game.SelfTestPickBotLoadout(bots[0]);
        Line($"bot gear: no known level -> average {game.BotGearAverage}, gear level {bots[0].Bot!.GearLevel}");
        if (game.BotGearAverage != 0 || bots[0].Bot!.GearLevel != BotGear.Full)
            Fail("bot gear: with nobody's level known bots should carry everything");

        // Level 1: starter gear only.
        game.Progress = new Progress { ReadOnly = true };
        var problem = Draw(20, l => l == 1, out _, out _, out var high1);
        var starterOnly = bots.All(b => b.WantGrenade == ThrowableKind.None && b.WantEquipment == ThrowableKind.None
                                        && !Weapons.Get(b.WantSecondary).IsLauncher && b.Streak.Want == StreakSet.Starter);
        Line($"bot gear: level-1 lobby -> average {game.BotGearAverage}; {high1} non-starter picks; streaks {string.Join("/", bots[0].Streak.Want.Ids)}");
        if (game.BotGearAverage != 1 || problem != null || high1 != 0 || !starterOnly)
            Fail($"bot gear: a level-1 lobby's bots should carry starter gear only ({problem ?? "a grenade, equipment, launcher or non-starter streak"})");

        // Level 30: nothing above each bot's gear level (27-33), and real variety.
        game.Progress = new Progress { ReadOnly = true, Xp = Progress.TotalFor(30) };
        problem = Draw(30, l => l >= 27 && l <= 33, out var lo30, out var hi30, out var high30);
        Line($"bot gear: level-30 lobby -> average {game.BotGearAverage}, gear levels {lo30}..{hi30}; {high30} picks with unlocked extras");
        if (game.Progress.Level != 30 || game.BotGearAverage != 30 || problem != null || high30 == 0)
            Fail($"bot gear: a level-30 lobby's bots carry something they couldn't have ({problem ?? "no variety"})");

        // Personalities: Marksmen still take DMRs and snipers, Rushers SMGs and shotguns (at level 1 and at level 30).
        foreach (var lvl in new[] { 1, 30 })
        {
            game.Progress = new Progress { ReadOnly = true, Xp = Progress.TotalFor(lvl) };
            int longGuns = 0, closeGuns = 0;
            const int lives = 60;
            for (var i = 0; i < lives; i++)
            {
                marksman.SpawnTick = rusher.SpawnTick = tick++;
                game.SelfTestPickBotLoadout(marksman);
                game.SelfTestPickBotLoadout(rusher);
                if (Weapons.Get(marksman.WantPrimary).Class is WeaponClass.DMR or WeaponClass.Sniper)
                    longGuns++;
                if (Weapons.Get(rusher.WantPrimary).Class is WeaponClass.SMG or WeaponClass.Shotgun)
                    closeGuns++;
            }
            Line($"bot gear: level {lvl}: Marksman DMR/sniper {longGuns}/{lives}, Rusher SMG/shotgun {closeGuns}/{lives}");
            if (longGuns < lives / 2 || closeGuns < lives / 2)
                Fail($"bot gear: personalities lost their weapon preference at level {lvl}");
        }

        // Joins and leaves: people only, prestige as max level, unknown levels left out; a bot life picks it up too.
        game.Progress = new Progress { ReadOnly = true, Xp = Progress.TotalFor(30) };
        game.SelfTestRefreshBotGear();
        var a0 = game.BotGearAverage;
        game.Players[200] = new Player { Id = 200, Name = "Guest", Level = 40 };
        game.SelfTestRefreshBotGear();
        var a1 = game.BotGearAverage;
        game.Players[201] = new Player { Id = 201, Name = "Old client" }; // level 0: unknown
        game.Players[202] = new Player { Id = 202, Name = "Veteran", Prestige = 2, Level = 1 };
        game.SelfTestPickBotLoadout(bots[0]); // a bot life works it out again on its own
        var a2 = game.BotGearAverage;
        var want2 = BotGear.Average(30 + 40 + BotGear.Full, 3);
        game.Players.Remove(200);
        game.Players.Remove(201);
        game.Players.Remove(202);
        game.SelfTestRefreshBotGear();
        var a3 = game.BotGearAverage;
        Line($"bot gear: host 30 -> {a0}; + a level-40 guest -> {a1}; + unknown + prestige -> {a2} (want {want2}); all left -> {a3}");
        if (a0 != 30 || a1 != 35 || a2 != want2 || a3 != 30)
            Fail("bot gear: the lobby level doesn't follow people joining and leaving (people only, prestige = max, unknown left out)");

        // Prestige host: counts as max level.
        game.Progress = new Progress { ReadOnly = true, Prestige = 1 };
        game.SelfTestRefreshBotGear();
        if (game.BotGearAverage != BotGear.Full)
            Fail($"bot gear: a prestiged host (level 1) should count as the max level, got {game.BotGearAverage}");

        // The host setting.
        game.Progress = new Progress { ReadOnly = true, Xp = Progress.TotalFor(30) };
        game.BotGearMode = BotGearMode.Starter;
        problem = Draw(10, l => l == 1, out _, out _, out var highStarter);
        if (problem != null || highStarter != 0)
            Fail($"bot gear: Starter should give level-1 gear ({problem ?? "extras picked"})");
        game.BotGearMode = BotGearMode.Everything;
        problem = Draw(10, l => l == BotGear.Full, out _, out _, out _);
        if (problem != null)
            Fail($"bot gear: Everything should give max-level gear ({problem})");
        game.BotGearMode = BotGearMode.MatchPlayers;
        Line("bot gear: host setting Starter -> level 1, Everything -> max level");

        // No allocations per bot life (the lobby level and the whole loadout draw).
        Draw(3, _ => true, out _, out _, out _); // warm
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
            foreach (var b in bots)
            {
                b.SpawnTick = tick++;
                game.SelfTestPickBotLoadout(b);
            }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"bot gear: {100 * bots.Count} loadout draws allocated {bytes} bytes");
        if (bytes > 0)
        {
            Fail($"bot gear: a bot's loadout draw allocates ({bytes} bytes over {100 * bots.Count} lives)");
            // Where: each piece on its own, 100 times.
            long Cost(Action a)
            {
                a();
                var t0 = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 100; i++)
                    a();
                return GC.GetAllocatedBytesForCurrentThread() - t0;
            }
            var b0 = bots[0];
            var w0 = Weapons.Get(b0.WantPrimary);
            Line($"bot gear allocations x100: refresh {Cost(game.SelfTestRefreshBotGear)}, level {Cost(() => game.BotGearLevel(b0))}, "
                 + $"weapons {Cost(() => { foreach (var w in Weapons.All) BotGear.Weapon(w, 20); })}, "
                 + $"attachments {Cost(() => { for (var i = 0; i < Attachments.For(w0).Count; i++) BotGear.Attachment(w0, i, 20); })}, "
                 + $"perks {Cost(() => { foreach (var p in Perks.All) BotGear.Perk(p.Id, 20); })}, "
                 + $"equipment {Cost(() => BotGear.Equipment(BotPersonality.Tactician, 0.3, 20))}, "
                 + $"streak pick {Cost(() => game.SelfTestStreakPick(b0))}, draw {Cost(() => game.SelfTestDrawOnly(b0))}");
        }
    }

    /// <summary>A seeded game's bot loadouts at a level, as text.</summary>
    private static string BotGearSignature(int level)
    {
        World.SetMap(Maps.All[0]);
        Game.SeedNextGame = 777;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 5);
        game.MapVoteEnabled = false;
        game.Progress = new Progress { ReadOnly = true, Xp = Progress.TotalFor(level) };
        var sb = new System.Text.StringBuilder();
        uint tick = 50;
        for (var i = 0; i < 8; i++)
            foreach (var b in game.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id))
            {
                b.SpawnTick = tick++;
                game.SelfTestPickBotLoadout(b);
                sb.Append($"{b.Bot!.GearLevel}:{b.WantPrimary}/{b.WantPrimaryKit:X}/{b.WantSecondary}/{b.WantSecondaryKit:X}/{b.WantGrenade}/{b.WantEquipment}/{b.WantPerks}/{b.Streak.Want};");
            }
        return sb.ToString();
    }

    private static void CheckBotGearDeterminism()
    {
        var a = BotGearSignature(12);
        var b = BotGearSignature(12);
        var c = BotGearSignature(40);
        Line($"bot gear: seeded loadouts at level 12 match twice: {a == b} ({a.Length} chars); level 40 differs: {a != c}");
        if (a != b)
            Fail("bot gear: the same seeded game gave different bot loadouts");
        if (a == c)
            Fail("bot gear: bot loadouts don't change with the lobby's level");
    }
}
