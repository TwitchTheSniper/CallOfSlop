namespace CallOfSlop;

/// <summary>The stats compared on the weapon-select screen, in display order.</summary>
public enum SelectStat : byte
{
    Damage,
    FireRate,
    Range,
    Accuracy,
    Control,
    Mobility,
    Handling,
}

/// <summary>
/// Pure helpers behind the weapon-select screen (see UiWeaponSelect.cs): which class tabs a slot has,
/// which guns sit in each tab, and the 0-1 stat scores that the comparison bars are drawn from.
/// The lists are built once per weapon list and reused every frame, so drawing the screen allocates nothing.
/// </summary>
public static class WeaponSelect
{
    /// <summary>Tab order for primaries (the classes a slot actually has are shown, in this order); pistols go in the secondary slot.</summary>
    private static readonly WeaponClass[] TabOrder =
    [
        WeaponClass.AssaultRifle, WeaponClass.SMG, WeaponClass.Shotgun, WeaponClass.LMG, WeaponClass.DMR, WeaponClass.Sniper, WeaponClass.Special, WeaponClass.Pistol, WeaponClass.Launcher,
    ];

    /// <summary>Number of stats in <see cref="SelectStat"/>.</summary>
    public const int StatCount = 7;

    private static WeaponDef[]? _source; // the Weapons.All the caches below were built from
    private static WeaponClass[] _primaryTabs = [], _secondaryTabs = [];
    private static readonly WeaponDef[][] ByClass = new WeaponDef[TabOrder.Length][];
    private static int[] _unlockLevel = [];
    private static string[] _upperName = [];

    private static void Build()
    {
        var all = Weapons.All;
        if (ReferenceEquals(all, _source))
            return;
        var primary = new List<WeaponClass>();
        var secondary = new List<WeaponClass>();
        var levels = new int[all.Length];
        var upper = new string[all.Length];
        foreach (var w in all)
        {
            levels[w.Id] = Progress.UnlockLevel(w);
            upper[w.Id] = w.Name.ToUpperInvariant();
        }
        foreach (var c in TabOrder)
        {
            var guns = all.Where(w => w.Class == c).OrderBy(w => levels[w.Id]).ThenBy(w => w.Order).ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            ByClass[(int)c] = guns;
            if (guns.Length == 0)
                continue;
            (c is WeaponClass.Pistol or WeaponClass.Launcher ? secondary : primary).Add(c); // launchers: secondary only
        }
        _primaryTabs = primary.ToArray();
        _secondaryTabs = secondary.ToArray();
        _unlockLevel = levels;
        _upperName = upper;
        _source = all;
    }

    /// <summary>The class tabs for a loadout slot: only classes that have at least one gun, in <see cref="TabOrder"/>.</summary>
    public static WeaponClass[] Tabs(Slot slot)
    {
        Build();
        return slot == Slot.Primary ? _primaryTabs : _secondaryTabs;
    }

    /// <summary>The guns in a class tab, cheapest unlock first.</summary>
    public static WeaponDef[] InTab(WeaponClass c)
    {
        Build();
        return (int)c < ByClass.Length ? ByClass[(int)c] ?? [] : [];
    }

    /// <summary>Account level that unlocks the weapon (cached <see cref="Progress.UnlockLevel(WeaponDef)"/>).</summary>
    public static int UnlockLevel(WeaponDef w)
    {
        Build();
        return w.Id < _unlockLevel.Length ? _unlockLevel[w.Id] : Progress.UnlockLevel(w);
    }

    /// <summary>The weapon's name in capitals (cached, for the big hero title).</summary>
    public static string UpperName(WeaponDef w)
    {
        Build();
        return w.Id < _upperName.Length ? _upperName[w.Id] : w.Name;
    }

    /// <summary>How many guns in the tab are still locked at this account level.</summary>
    public static int LockedCount(WeaponClass c, int level)
    {
        var n = 0;
        foreach (var w in InTab(c))
            if (UnlockLevel(w) > level)
                n++;
        return n;
    }

    /// <summary>Tab caption (capitals).</summary>
    public static string TabName(WeaponClass c) => c switch
    {
        WeaponClass.AssaultRifle => "ASSAULT RIFLES",
        WeaponClass.SMG => "SMGS",
        WeaponClass.Shotgun => "SHOTGUNS",
        WeaponClass.LMG => "LMGS",
        WeaponClass.DMR => "MARKSMAN",
        WeaponClass.Sniper => "SNIPERS",
        WeaponClass.Special => "SPECIAL",
        WeaponClass.Pistol => "PISTOLS",
        WeaponClass.Launcher => "LAUNCHERS",
        _ => "OTHER",
    };

    /// <summary>Singular class label (capitals), for the hero's class line.</summary>
    public static string ClassLabel(WeaponClass c) => c switch
    {
        WeaponClass.AssaultRifle => "ASSAULT RIFLE",
        WeaponClass.SMG => "SMG",
        WeaponClass.Shotgun => "SHOTGUN",
        WeaponClass.LMG => "LMG",
        WeaponClass.DMR => "MARKSMAN RIFLE",
        WeaponClass.Sniper => "SNIPER RIFLE",
        WeaponClass.Special => "SPECIAL",
        WeaponClass.Pistol => "PISTOL",
        WeaponClass.Launcher => "LAUNCHER",
        _ => "WEAPON",
    };

    /// <summary>Attachments on the gun that its weapon XP has unlocked (the gun's "level"), without allocating.</summary>
    public static int UnlockedAttachments(WeaponDef w, int weaponXp)
    {
        var list = Attachments.For(w);
        var n = 0;
        for (var i = 0; i < list.Count; i++)
            if (weaponXp >= Attachments.UnlockXp(w, list[i]))
                n++;
        return n;
    }

    public static string StatName(SelectStat s) => s switch
    {
        SelectStat.Damage => "Damage",
        SelectStat.FireRate => "Fire rate",
        SelectStat.Range => "Range",
        SelectStat.Accuracy => "Accuracy",
        SelectStat.Control => "Recoil control",
        SelectStat.Mobility => "Mobility",
        _ => "Handling",
    };

    /// <summary>
    /// A stat as a 0-1 score, higher is always better (the same scales as the gunsmith's bars).
    /// Handling blends draw time and aim-down-sights time.
    /// </summary>
    public static float Score(WeaponDef w, SelectStat s) => Math.Clamp(s switch
    {
        SelectStat.Damage => w.Damage * w.Pellets / 140f,
        SelectStat.FireRate => w.Rpm / 1500f,
        SelectStat.Range => w.RangeM / 60f,
        SelectStat.Accuracy => 1f - w.HipSpreadDeg / 7.5f,
        SelectStat.Control => 1f - w.RecoilDeg / 5.5f,
        SelectStat.Mobility => (w.Mobility - 0.7f) / 0.45f,
        _ => 1f - 0.5f * (w.EquipTime / 1.2f) - 0.5f * (w.AdsTime / 0.45f),
    }, 0f, 1f);

    /// <summary>The difference shown next to a bar, in points out of 100 (positive = <paramref name="shown"/> is better).</summary>
    public static int Delta(WeaponDef equipped, WeaponDef shown, SelectStat s) =>
        (int)MathF.Round((Score(shown, s) - Score(equipped, s)) * 100f);
}
