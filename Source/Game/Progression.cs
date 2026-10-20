namespace CallOfSlop;

/// <summary>Something you can unlock: a weapon, a grenade, a piece of equipment, a perk or a loadout slot.</summary>
public readonly record struct Unlock(int Level, string Name, string Kind, WeaponDef? Weapon = null, ThrowableKind? Throwable = null,
    PerkId? Perk = null, int LoadoutSlot = -1);

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
    private readonly Dictionary<int, int> _weaponXp = new(); // weapon id -> weapon XP (unlocks attachments)

    public int WeaponXp(byte weaponId) => _weaponXp.GetValueOrDefault(weaponId);
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

    /// <summary>Perks after the three starters, in unlock order (alternating categories).</summary>
    private static readonly PerkId[] PerkOrder =
    [
        PerkId.HeadStart, PerkId.Bloodthirst, PerkId.Opener, PerkId.Parkour, PerkId.Entrenched,
        PerkId.Deadeye, PerkId.Afterburner, PerkId.LastStand, PerkId.Flanker,
    ];

    /// <summary>Loadout slots: one at level 1, the last at max level, the rest evenly in between.</summary>
    public const int MaxLoadoutSlots = 10;

    public static int LoadoutSlotLevel(int slot) =>
        slot <= 0 ? 1 : 1 + (int)MathF.Round(slot * (MaxLevel - 1) / (float)(MaxLoadoutSlots - 1));

    public int LoadoutSlots => Enumerable.Range(0, MaxLoadoutSlots).Count(i => Level >= LoadoutSlotLevel(i));

    public static int PerkLevel(PerkId id)
    {
        if (id == PerkId.None || Perks.Starters.Contains(id))
            return 1;
        var i = Array.IndexOf(PerkOrder, id);
        // From level 3 to the level before max, evenly spaced.
        return i < 0 ? MaxLevel : 3 + (int)MathF.Round(i * (MaxLevel - 4) / (float)(PerkOrder.Length - 1));
    }

    public bool IsUnlocked(PerkId id) => Level >= PerkLevel(id);

    public static readonly Unlock[] All = BuildTable();

    private static Unlock[] BuildTable()
    {
        var list = new List<Unlock>();
        foreach (var n in Starters)
            list.Add(new Unlock(1, n, "Weapon", Weapons.Find(n)));
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
            else
            {
                list.Add(new Unlock(level, name, "Weapon", Weapons.Find(name)));
            }
        }
        // Anything not listed (e.g. a weapon added later) unlocks at the end.
        foreach (var w in Weapons.All)
            if (list.All(u => u.Weapon != w))
                list.Add(new Unlock(MaxLevel, w.Name, "Weapon", w));
        foreach (var perk in Perks.All.Where(pk => pk.Id != PerkId.None))
            list.Add(new Unlock(PerkLevel(perk.Id), perk.Name, $"{Perks.CategoryName(perk.Category)} perk", Perk: perk.Id));
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
        var before = WeaponXp(weaponId);
        var after = _weaponXp[weaponId] = before + amount;
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
        var p = new Progress();
        try
        {
            if (File.Exists(FilePath))
            {
                foreach (var line in File.ReadAllLines(FilePath))
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
                        case var key when key.StartsWith("wx") && int.TryParse(key[2..], out var wid): p._weaponXp[wid] = Math.Max(0, v); break;
                        // Older saves counted weapon kills: carry them over as weapon XP.
                        case var key when key.StartsWith("wk") && int.TryParse(key[2..], out var kid): p._weaponXp.TryAdd(kid, Math.Max(0, v) * KillXp); break;
                    }
                }
            }
        }
        catch
        {
            // Unreadable: start fresh.
        }
        return p;
    }

    public void Save()
    {
        if (ReadOnly)
            return;
        try
        {
            File.WriteAllLines(FilePath, [$"xp={Xp}", $"kills={Kills}", $"headshots={Headshots}", $"matches={Matches}", $"wins={Wins}", .. _weaponXp.Select(kv => $"wx{kv.Key}={kv.Value}")]);
        }
        catch
        {
            // Read-only folder.
        }
    }
}
