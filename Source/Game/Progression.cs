namespace CallOfSlop;

/// <summary>Something you can unlock: a weapon, a grenade, a piece of equipment, a perk, a kill streak or a loadout slot.</summary>
public readonly record struct Unlock(int Level, string Name, string Kind, WeaponDef? Weapon = null, ThrowableKind? Throwable = null,
    PerkId? Perk = null, int LoadoutSlot = -1, StreakId? Streak = null);

public readonly record struct XpPopup(double Time, string Text, int Amount);

/// <summary>
/// Local progression shared by single player and multiplayer. Two tracks, earned from the same play:
///
///   Account XP (levels): kill 100, headshot +50, grenade +25, match played +150, match won +250.
///     Every level unlocks a weapon, grenade or equipment; perks and extra loadout slots are spread
///     evenly over the same levels on top.
///
///   Weapon XP (attachments): kills with that gun 100, headshot +50, plus a match bonus for the guns
///     you carried (+150 primary, +75 secondary). Each gun's attachments unlock along a gentle curve.
///
/// Pacing targets (rough, not hard-linked): a typical match is ~7 kills, ~1,000 XP on both tracks.
///   - A gun maxes out in 3-7 matches (more attachments = longer; see Attachments.MaxXp).
///   - A level costs about what it takes to get a gun ~60% kitted out, so the next weapon tends to
///     arrive as your current one is getting good, roughly every 2-3 matches.
/// Saved to progress.txt next to the game.
/// </summary>
public sealed class Progress
{
    public const int KillXp = 100;
    public const int HeadshotBonus = 50;
    public const int GrenadeBonus = 25;
    public const int MatchXp = 150;
    public const int WinXp = 250;
    public const int MatchWeaponXpPrimary = 150;
    public const int MatchWeaponXpSecondary = 75;

    /// <summary>One unlock per level: level 1 is the starter kit, then one item per level.</summary>
    public static int MaxLevel => Order.Length + 1;

    public int Xp;
    public int Kills, Headshots, Matches, Wins;

    /// <summary>A scratch copy that never writes progress.txt (used by the automatic smoke test).</summary>
    public bool ReadOnly;
    // Weapon name -> weapon XP (unlocks attachments). Keyed by name, not id: ids are positions in the loaded
    // weapon list and shift when weapon files are added. Entries for weapons that aren't loaded are kept and saved.
    private readonly Dictionary<string, int> _weaponXp = new(StringComparer.OrdinalIgnoreCase);

    public int WeaponXp(byte weaponId) => weaponId < Weapons.All.Length ? WeaponXp(Weapons.Get(weaponId).Name) : 0;

    public int WeaponXp(string weaponName) => _weaponXp.GetValueOrDefault(weaponName);
    public readonly List<XpPopup> Popups = new();
    public string LevelUpBanner = "";
    public double LevelUpTime = -100;
    public string AttachmentBanner = "";
    public double AttachmentBannerTime = -100;

    /// <summary>
    /// XP needed to go from <paramref name="level"/> to level+1: about 2,400 early rising to ~3,800,
    /// which is roughly the weapon XP for 60% of an average gun's attachments (~3,200).
    /// </summary>
    public static int XpForNext(int level) => 2400 + 30 * level;

    /// <summary>Total XP needed to reach <paramref name="level"/>.</summary>
    public static int TotalFor(int level)
    {
        var total = 0;
        for (var l = 1; l < level; l++)
            total += XpForNext(l);
        return total;
    }

    public int Level
    {
        get
        {
            var level = 1;
            while (level < MaxLevel && Xp >= TotalFor(level + 1))
                level++;
            return level;
        }
    }

    /// <summary>0..1 progress through the current level.</summary>
    public float LevelFraction
    {
        get
        {
            var l = Level;
            if (l >= MaxLevel)
                return 1;
            return (Xp - TotalFor(l)) / (float)XpForNext(l);
        }
    }

    public int XpIntoLevel => Xp - TotalFor(Level);

    // ---- Unlock table ----------------------------------------------------------------

    /// <summary>Everything that starts unlocked at level 1.</summary>
    private static readonly string[] Starters =
        ["BR-1 Prototype", "Vanguard AR", "Hornet SMG", "Breacher Pump", "Marksman M14", "P9 Sidearm", "Mk.23 Tactical"];

    /// <summary>The order things unlock in, from level 2 upwards. Spread evenly over levels 2-30.</summary>
    private static readonly string[] Order =
    [
        "Courier", "Viper .45", "@Frag", "Sentinel", "Thunder Auto", "Hawk Semi", "@Recon", "Raptor", "Wasp Machine Pistol",
        "Needle Bolt-Action", "Kestrel PDW", "@Impact", "Torrent", "Phalanx Bullpup", "Rattler Revolver", "Slug Pump",
        "Tri-Shot", "Longbow DMR", "@Smoke", "Tempest", "Anvil LMG", "Stinger Burst Pistol", "Reaper Battle Rifle", "Buzzsaw",
        "Kite Semi-Sniper", "Ratchet Burst SMG", "Double Barrel", "Specter SD", "Hush SD", "Whisper SD", "Talon Burst DMR", "Bulldog",
        "Scattergun Lever", "Hailstorm", "Ghost SD", "Bastion Hand Cannon", "Warden Lever-Action", "Nailgun",
        "Street Sweeper", "Bastille", "Pocket Derringer", "Arc Carbine", "Leviathan .50", "Old Faithful Musket",
        "Grinder Rotary", "Railspike", "Toy Blaster",
    ];

    /// <summary>
    /// Perks after the three starters, in unlock order, each with its place on the perk track: the original nine sit
    /// on whole numbers 0-8 (so their levels never move), the later ones in between. Level = 3 + place * (max - 4) / 8.
    /// </summary>
    private static readonly (PerkId Id, float Place)[] PerkOrder =
    [
        (PerkId.HeadStart, 0), (PerkId.SleightOfHand, 0.4f), (PerkId.QuickFix, 0.75f), (PerkId.Bloodthirst, 1),
        (PerkId.Stalker, 1.45f), (PerkId.Opener, 2), (PerkId.SteadyAim, 2.35f), (PerkId.Parkour, 3),
        (PerkId.Scavenger, 3.3f), (PerkId.DeadSilence, 3.65f), (PerkId.Entrenched, 4), (PerkId.Demolitions, 4.4f),
        (PerkId.Hardline, 4.75f), (PerkId.Deadeye, 5), (PerkId.Lightweight, 5.45f), (PerkId.Afterburner, 6),
        (PerkId.TightGrip, 6.35f), (PerkId.LastStand, 7), (PerkId.ColdBlooded, 7.25f), (PerkId.Ghost, 7.6f),
        (PerkId.Flanker, 8),
    ];

    /// <summary>The non-starter perks in unlock order (self-test).</summary>
    public static IEnumerable<PerkId> PerkUnlockOrder => PerkOrder.Select(e => e.Id);

    /// <summary>Loadout slots: one at level 1, the last at max level, the rest evenly in between.</summary>
    public const int MaxLoadoutSlots = 10;

    public static int LoadoutSlotLevel(int slot) =>
        slot <= 0 ? 1 : 1 + (int)MathF.Round(slot * (MaxLevel - 1) / (float)(MaxLoadoutSlots - 1));

    public int LoadoutSlots => Enumerable.Range(0, MaxLoadoutSlots).Count(i => Level >= LoadoutSlotLevel(i));

    public static int PerkLevel(PerkId id)
    {
        if (id == PerkId.None || Perks.Starters.Contains(id))
            return 1;
        foreach (var (pid, place) in PerkOrder)
            if (pid == id)
                return 3 + (int)MathF.Round(place * (MaxLevel - 4) / 8f); // from level 3 to the level before max
        return MaxLevel;
    }

    public bool IsUnlocked(PerkId id) => Level >= PerkLevel(id);

    /// <summary>Kill streaks: three starters, the rest spread evenly (see <see cref="KillStreaks.UnlockLevel"/>).</summary>
    public bool IsUnlocked(StreakId id) => Level >= KillStreaks.UnlockLevel(id);

    public static readonly Unlock[] All = BuildTable();

    /// <summary>True if the weapon is one of the hand-placed ones in the built-in unlock table.</summary>
    public static bool HasBuiltInUnlock(string weaponName) =>
        Starters.Contains(weaponName, StringComparer.OrdinalIgnoreCase) || Order.Contains(weaponName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The unlock table. Shipped weapons unlock in the hand-tuned order above (by name). A weapon file can set
    /// "unlockLevel" to choose its own level; weapons with neither unlock at the max level.
    /// </summary>
    private static Unlock[] BuildTable()
    {
        var list = new List<Unlock>();
        // A weapon in the table by name, unless it isn't loaded or its file picks its own level.
        WeaponDef? Tabled(string name) => Weapons.Find(name) is { UnlockLevel: null } w ? w : null;
        foreach (var n in Starters)
            if (Tabled(n) is { } w)
                list.Add(new Unlock(1, w.Name, "Weapon", w));
        for (var i = 0; i < Order.Length; i++)
        {
            var level = 2 + i; // one unlock per level
            var name = Order[i];
            if (name.StartsWith('@'))
            {
                var kind = Enum.Parse<ThrowableKind>(name[1..]);
                var def = Throwables.Get(kind);
                list.Add(new Unlock(level, def.Name, kind == ThrowableKind.Recon ? "Equipment" : "Grenade", Throwable: kind));
            }
            else if (Tabled(name) is { } w)
            {
                list.Add(new Unlock(level, w.Name, "Weapon", w));
            }
        }
        // Anything else (a third-party weapon file) unlocks at its "unlockLevel", or at the end.
        foreach (var w in Weapons.All)
            if (list.All(u => u.Weapon != w))
                list.Add(new Unlock(Math.Clamp(w.UnlockLevel ?? MaxLevel, 1, MaxLevel), w.Name, "Weapon", w));
        foreach (var perk in Perks.All.Where(pk => pk.Id != PerkId.None))
            list.Add(new Unlock(PerkLevel(perk.Id), perk.Name, $"{Perks.CategoryName(perk.Category)} perk", Perk: perk.Id));
        foreach (var streak in KillStreaks.All.Where(s => s.Id != StreakId.None))
            list.Add(new Unlock(KillStreaks.UnlockLevel(streak.Id), streak.Name, "Kill streak", Streak: streak.Id));
        for (var slot = 0; slot < MaxLoadoutSlots; slot++)
            list.Add(new Unlock(LoadoutSlotLevel(slot), $"Loadout slot {slot + 1}", "Loadout", LoadoutSlot: slot));
        return list.OrderBy(u => u.Level).ToArray();
    }

    public static int UnlockLevel(WeaponDef w) => All.FirstOrDefault(u => u.Weapon == w).Level is var l and > 0 ? l : MaxLevel;

    public static int UnlockLevel(ThrowableKind k) => All.FirstOrDefault(u => u.Throwable == k).Level is var l and > 0 ? l : MaxLevel;

    public bool IsUnlocked(WeaponDef w) => Level >= UnlockLevel(w);

    public bool IsUnlocked(ThrowableKind k) => k == ThrowableKind.None || Level >= UnlockLevel(k);

    public IEnumerable<Unlock> UnlocksAt(int level) => All.Where(u => u.Level == level);

    public Unlock? NextUnlock => All.Where(u => u.Level > Level).Select(u => (Unlock?)u).FirstOrDefault();

    /// <summary>Short description for lists: "Raptor", "Frag Grenade (grenade)", "Opener (damage perk)".</summary>
    public static string Describe(Unlock u) => u.Kind == "Weapon" ? u.Name : u.Kind == "Loadout" ? u.Name : $"{u.Name} ({u.Kind.ToLowerInvariant()})";

    // ---- Earning XP ------------------------------------------------------------------

    /// <summary>Called when the local player gets a kill. Returns the new level if it changed.</summary>
    public int? AwardKill(bool headshot, bool grenade, double now, byte weaponId = 255)
    {
        var before = Level;
        Kills++;
        if (!grenade && weaponId < Weapons.All.Length)
            AddWeaponXp(weaponId, KillXp + (headshot ? HeadshotBonus : 0), now);
        Add(KillXp, "KILL", now);
        if (headshot)
        {
            Headshots++;
            Add(HeadshotBonus, "HEADSHOT", now);
        }
        if (grenade)
            Add(GrenadeBonus, "GRENADE", now);
        Save();
        return CheckLevelUp(before, now);
    }

    /// <summary>Called when a match the local player was in ends. Returns the new level if it changed.</summary>
    public int? AwardMatch(bool won, byte primary, byte secondary, double now)
    {
        var before = Level;
        Matches++;
        Add(MatchXp, "MATCH", now);
        if (won)
        {
            Wins++;
            Add(WinXp, "VICTORY", now);
        }
        AddWeaponXp(primary, MatchWeaponXpPrimary, now);
        AddWeaponXp(secondary, MatchWeaponXpSecondary, now);
        Save();
        return CheckLevelUp(before, now);
    }

    private void AddWeaponXp(byte weaponId, int amount, double now)
    {
        if (weaponId >= Weapons.All.Length)
            return;
        var w = Weapons.Get(weaponId);
        var before = WeaponXp(w.Name);
        var after = _weaponXp[w.Name] = before + amount;
        var unlocked = Attachments.For(w).Where(a => Attachments.UnlockXp(w, a) is var need && need > before && need <= after).Select(a => a.Name).ToArray();
        if (unlocked.Length > 0)
        {
            AttachmentBanner = $"{w.Name}: unlocked {string.Join(", ", unlocked)}";
            AttachmentBannerTime = now;
        }
    }

    private int? CheckLevelUp(int before, double now)
    {
        var after = Level;
        if (after == before)
            return null;
        var names = Enumerable.Range(before + 1, after - before).SelectMany(UnlocksAt).Select(u => u.Name).ToArray();
        LevelUpBanner = names.Length > 0 ? $"LEVEL {after}!  Unlocked: {string.Join(", ", names)}" : $"LEVEL {after}!";
        LevelUpTime = now;
        return after;
    }

    private void Add(int amount, string label, double now)
    {
        Xp += amount;
        Popups.Add(new XpPopup(now, label, amount));
        if (Popups.Count > 6)
            Popups.RemoveAt(0);
    }

    /// <summary>Back to level 1 with no XP (Settings > Player > Reset level). Everything locks again.</summary>
    public void Reset()
    {
        Xp = 0;
        Kills = 0;
        Headshots = 0;
        Matches = 0;
        Wins = 0;
        _weaponXp.Clear();
        Popups.Clear();
        LevelUpBanner = "";
        LevelUpTime = -100;
        Save();
    }

    // ---- Save file -------------------------------------------------------------------

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "progress.txt");

    public static Progress Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Parse(File.ReadAllLines(FilePath));
        }
        catch
        {
            // Unreadable: start fresh.
        }
        return new Progress();
    }

    /// <summary>
    /// Reads progress.txt lines. Weapon XP is "wx.&lt;weapon name&gt;=xp". Older saves used weapon ids ("wx&lt;id&gt;",
    /// and before that kill counts as "wk&lt;id&gt;"); those are mapped to names with <see cref="Weapons.LegacyNames"/>.
    /// If a weapon appears more than once, the name key wins over "wx", which wins over "wk".
    /// </summary>
    public static Progress Parse(IEnumerable<string> lines)
    {
        var p = new Progress();
        var legacyXp = new Dictionary<string, int>();
        var legacyKills = new Dictionary<string, int>();
        string? Legacy(string digits) => int.TryParse(digits, out var id) && id >= 0 && id < Weapons.LegacyNames.Length ? Weapons.LegacyNames[id] : null;
        foreach (var line in lines)
        {
            var kv = line.Split('=', 2);
            if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out var v))
                continue;
            switch (kv[0].Trim())
            {
                case "xp": p.Xp = Math.Max(0, v); break;
                case "kills": p.Kills = v; break;
                case "headshots": p.Headshots = v; break;
                case "matches": p.Matches = v; break;
                case "wins": p.Wins = v; break;
                case var key when key.StartsWith("wx.") && key.Length > 3: p._weaponXp[key[3..]] = Math.Max(0, v); break;
                case var key when key.StartsWith("wx") && Legacy(key[2..]) is { } name: legacyXp[name] = Math.Max(0, v); break;
                // Older saves counted weapon kills: carry them over as weapon XP.
                case var key when key.StartsWith("wk") && Legacy(key[2..]) is { } name: legacyKills[name] = Math.Max(0, v) * KillXp; break;
            }
        }
        foreach (var (name, xp) in legacyXp)
            p._weaponXp.TryAdd(name, xp);
        foreach (var (name, xp) in legacyKills)
            p._weaponXp.TryAdd(name, xp);
        return p;
    }

    /// <summary>The lines progress.txt is saved as.</summary>
    public IEnumerable<string> ToLines() =>
        [$"xp={Xp}", $"kills={Kills}", $"headshots={Headshots}", $"matches={Matches}", $"wins={Wins}", .. _weaponXp.Select(kv => $"wx.{kv.Key}={kv.Value}")];

    public void Save()
    {
        if (ReadOnly)
            return;
        try
        {
            File.WriteAllLines(FilePath, ToLines());
        }
        catch
        {
            // Read-only folder.
        }
    }
}
