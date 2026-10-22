namespace CallOfSlop;

/// <summary>One loadout slot: two guns with their attachments, a grenade, a piece of equipment, three perks and three kill streaks.</summary>
public sealed class Loadout
{
    public string Name = "";
    public byte Primary = Weapons.DefaultPrimary.Id;
    public byte Secondary = Weapons.DefaultSecondary.Id;
    public uint PrimaryKit, SecondaryKit;
    public ThrowableKind Grenade, Equipment;
    public PerkSet Perks = PerkSet.Empty;
    public StreakSet Streaks = StreakSet.Starter;

    /// <summary>
    /// The hat worn with this loadout. Hats are identity, not tactics: one choice per profile
    /// (<see cref="LoadoutStore.Hat"/>) applies to every slot, so a slot itself never stores one;
    /// <see cref="LoadoutStore.Effective"/> fills this in from the profile (None while it's locked).
    /// </summary>
    public HatId Hat;

    public Loadout Clone() => (Loadout)MemberwiseClone();

    public uint KitFor(int slot) => slot == 0 ? PrimaryKit : SecondaryKit;

    public byte WeaponIn(int slot) => slot == 0 ? Primary : Secondary;
}

/// <summary>
/// Your loadout slots, saved to loadout.txt next to the game. Slots unlock with level (see
/// <see cref="Progress.LoadoutSlotLevel"/>); locked slots keep their contents for later.
/// Each slot has its own attachments; the last build used on each gun is remembered too, so
/// putting a gun into another slot starts with the attachments you like on it.
/// </summary>
public static class LoadoutStore
{
    private static string PathName => Path.Combine(AppContext.BaseDirectory, "loadout.txt");

    public static readonly Loadout[] Slots = Enumerable.Range(0, Progress.MaxLoadoutSlots).Select(DefaultLoadout).ToArray();

    /// <summary>The slot you spawn with.</summary>
    public static int Selected;

    /// <summary>
    /// The hat you wear (one cosmetic choice for the whole profile, used by every loadout slot; saved as
    /// <c>hat=&lt;key&gt;</c>). A hat that's locked at your prestige is kept here for later but worn as None.
    /// </summary>
    public static HatId Hat;

    /// <summary>The hat you'd actually wear: <see cref="Hat"/> if your prestige unlocks it, otherwise None.</summary>
    public static HatId EffectiveHat(Progress progress) =>
        Cosmetics.IsUnlocked(Hat, CosmeticPrestige.Of(progress)) ? Hat : HatId.None;

    /// <summary>Last attachments used on each weapon (the starting kit when a gun goes into a slot).</summary>
    private static readonly Dictionary<byte, uint> LastKit = new();

    /// <summary>Smoke test only: hands out full kits regardless of progress (nothing is saved).</summary>
    public static Func<byte, uint>? SmokeKit;

    /// <summary>The smoke test sets this so it never writes your loadout.txt.</summary>
    public static bool ReadOnly;

    public static string DefaultName(int slot) => $"Custom Loadout {slot + 1}";

    private static Loadout DefaultLoadout(int slot) => new()
    {
        Name = DefaultName(slot),
        Perks = new PerkSet(Perks.Starters[0], Perks.Starters[1], Perks.Starters[2]),
    };

    /// <summary>How many slots you can see and use at your level (always at least 1). Slots unlock in order, so these are slots 0..n-1.</summary>
    public static int UnlockedSlots(Progress progress) => Math.Clamp(progress.LoadoutSlots, 1, Slots.Length);

    /// <summary>
    /// <paramref name="slot"/> if it's unlocked, otherwise the highest unlocked slot (a locked pick after a
    /// prestige or level reset, or an edited file). Only the choice moves: a locked slot's contents stay saved.
    /// </summary>
    public static int UnlockedSlot(int slot, Progress progress) => Math.Clamp(slot, 0, UnlockedSlots(progress) - 1);

    /// <summary>The level the next loadout slot unlocks at, or null once every slot is unlocked.</summary>
    public static int? NextSlotLevel(Progress progress) =>
        UnlockedSlots(progress) < Slots.Length ? Progress.LoadoutSlotLevel(UnlockedSlots(progress)) : null;

    /// <summary>The selected slot as you'd actually spawn with it (locked items swapped for defaults).</summary>
    public static Loadout Active(Progress progress)
    {
        Selected = UnlockedSlot(Selected, progress);
        return Effective(Slots[Selected], progress);
    }

    /// <summary>A copy of <paramref name="l"/> with anything still locked replaced (after a level reset, say).</summary>
    public static Loadout Effective(Loadout l, Progress progress)
    {
        var e = l.Clone();
        var p = Weapons.Get(l.Primary);
        var s = Weapons.Get(l.Secondary);
        if (p.Slot != Slot.Primary || !progress.IsUnlocked(p))
            p = Weapons.DefaultPrimary;
        if (s.Slot != Slot.Secondary || !progress.IsUnlocked(s))
            s = Weapons.DefaultSecondary;
        e.Primary = p.Id;
        e.Secondary = s.Id;
        e.PrimaryKit = SmokeKit?.Invoke(p.Id) ?? Attachments.Sanitize(p, p.Id == l.Primary ? l.PrimaryKit : KitMemory(p.Id), progress);
        e.SecondaryKit = SmokeKit?.Invoke(s.Id) ?? Attachments.Sanitize(s, s.Id == l.Secondary ? l.SecondaryKit : KitMemory(s.Id), progress);
        if (!progress.IsUnlocked(e.Grenade) || Throwables.Get(e.Grenade).IsEquipment)
            e.Grenade = ThrowableKind.None;
        if (!progress.IsUnlocked(e.Equipment) || (e.Equipment != ThrowableKind.None && !Throwables.Get(e.Equipment).IsEquipment))
            e.Equipment = ThrowableKind.None;
        var perks = l.Perks.Sanitized();
        foreach (var c in new[] { PerkCategory.Movement, PerkCategory.Survival, PerkCategory.Damage })
            if (!progress.IsUnlocked(perks[c]))
                perks = perks.With(c, PerkId.None);
        e.Perks = perks;
        e.Streaks = EffectiveStreaks(l.Streaks, progress);
        e.Hat = EffectiveHat(progress); // profile-wide (see Loadout.Hat)
        return e;
    }

    /// <summary>Locked streaks swapped for a starter that isn't in the set yet (or left empty).</summary>
    public static StreakSet EffectiveStreaks(StreakSet set, Progress progress)
    {
        var s = set.Sanitized();
        foreach (var id in s.Ids.ToArray())
        {
            if (progress.IsUnlocked(id))
                continue;
            s = s.Without(id);
            if (KillStreaks.Starters.FirstOrDefault(x => !s.Has(x)) is var starter and not StreakId.None)
                s = s.With(starter);
        }
        return s;
    }

    /// <summary>The attachments last used on a weapon.</summary>
    public static uint KitMemory(byte weaponId) => LastKit.GetValueOrDefault(weaponId);

    /// <summary>Puts a gun into a slot of a loadout, with the attachments last used on it.</summary>
    public static void SetWeapon(Loadout l, int slot, byte weaponId, Progress progress)
    {
        var kit = Attachments.Sanitize(Weapons.Get(weaponId), KitMemory(weaponId), progress);
        if (slot == 0)
        {
            l.Primary = weaponId;
            l.PrimaryKit = kit;
        }
        else
        {
            l.Secondary = weaponId;
            l.SecondaryKit = kit;
        }
    }

    /// <summary>Changes the attachments on one of a loadout's guns (and remembers them for that gun).</summary>
    public static void SetKit(Loadout l, int slot, uint kit)
    {
        if (slot == 0)
            l.PrimaryKit = kit;
        else
            l.SecondaryKit = kit;
        LastKit[l.WeaponIn(slot)] = kit;
    }

    // ---- Save file -----------------------------------------------------------------------
    //
    //   selected=2
    //   loadout1.name=Rusher
    //   loadout1.primary=Hornet SMG
    //   loadout1.primary.kit=Red Dot, Suppressor
    //   loadout1.secondary=P9 Sidearm
    //   loadout1.secondary.kit=
    //   loadout1.grenade=Frag
    //   loadout1.equipment=None
    //   loadout1.perks=Low Profile, Bloodthirst, Point Blank
    //   loadout1.streaks=Recon Sweep, Mortar Barrage, Sentry Turret   (files from before kill streaks: the starters)
    //   kit.Hornet SMG=Red Dot, Suppressor      (last build per gun)
    //   hat=fedora                              (the profile's hat, for every slot; files without it: none)
    //
    // Older files had one loadout (primary=..., secondary=..., grenade=..., equipment=...): it becomes slot 1.

    public static void Load()
    {
        string[]? lines = null;
        try
        {
            if (File.Exists(PathName))
                lines = File.ReadAllLines(PathName);
        }
        catch
        {
            lines = []; // unreadable file: defaults (but not a brand-new player)
        }
        LoadLines(lines);
    }

    /// <summary>Reads loadout.txt lines (null = no file yet: a brand-new player). The self-test uses this directly.</summary>
    public static void LoadLines(string[]? fileLines)
    {
        for (var i = 0; i < Slots.Length; i++)
            Slots[i] = DefaultLoadout(i);
        Selected = 0;
        Hat = HatId.None;
        LastKit.Clear();
        var fresh = fileLines == null;
        try
        {
            if (!fresh)
            {
                foreach (var line in fileLines!)
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2)
                        continue;
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (key == "selected" && int.TryParse(value, out var sel))
                        Selected = Math.Clamp(sel, 0, Slots.Length - 1);
                    else if (key == "hat")
                        Hat = Cosmetics.FromKey(value); // unknown or "none": no hat
                    else if (key.StartsWith("kit.") && Weapons.Find(key[4..]) is { } kw)
                        LastKit[kw.Id] = Attachments.Sanitize(kw, ParseKit(value));
                    else if (key.StartsWith("loadout") && key.IndexOf('.') is var dot and > 7
                             && int.TryParse(key[7..dot], out var n) && n >= 1 && n <= Slots.Length)
                        Apply(Slots[n - 1], key[(dot + 1)..], value);
                    else
                        Apply(Slots[0], key, value); // old single-loadout file
                }
            }
        }
        catch
        {
            // Unreadable file: defaults.
        }
        // Old files kept attachments per gun only: give each slot its gun's remembered build.
        foreach (var l in Slots)
        {
            if (l.PrimaryKit == 0)
                l.PrimaryKit = KitMemory(l.Primary);
            if (l.SecondaryKit == 0)
                l.SecondaryKit = KitMemory(l.Secondary);
        }
        // Brand-new players get the frag as soon as it unlocks.
        if (fresh)
            foreach (var l in Slots)
                l.Grenade = ThrowableKind.Frag;
    }

    private static uint ParseKit(string value)
    {
        uint kit = 0;
        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (Attachments.Find(name) is { } a)
                kit = Attachments.With(kit, a.Slot, a.Index);
        return kit;
    }

    private static void Apply(Loadout l, string key, string value)
    {
        switch (key)
        {
            case "name":
                l.Name = CleanName(value);
                break;
            case "primary" when Weapons.Find(value) is { Slot: Slot.Primary } w:
                l.Primary = w.Id;
                break;
            case "secondary" when Weapons.Find(value) is { Slot: Slot.Secondary } w:
                l.Secondary = w.Id;
                break;
            case "primary.kit":
                l.PrimaryKit = Attachments.Sanitize(Weapons.Get(l.Primary), ParseKit(value));
                break;
            case "secondary.kit":
                l.SecondaryKit = Attachments.Sanitize(Weapons.Get(l.Secondary), ParseKit(value));
                break;
            case "grenade" when Enum.TryParse<ThrowableKind>(value, out var g) && !Throwables.Get(g).IsEquipment:
                l.Grenade = g;
                break;
            case "equipment" when Enum.TryParse<ThrowableKind>(value, out var e) && (e == ThrowableKind.None || Throwables.Get(e).IsEquipment):
                l.Equipment = e;
                break;
            case "streaks":
            {
                var set = StreakSet.Empty;
                foreach (var name in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (KillStreaks.Find(name) is { } streak)
                        set = set.With(streak.Id);
                l.Streaks = set;
                break;
            }
            case "perks":
            {
                var set = PerkSet.Empty;
                foreach (var name in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (Perks.Find(name) is { } perk)
                        set = set.With(perk.Category, perk.Id);
                l.Perks = set;
                break;
            }
        }
    }

    public static string CleanName(string name)
    {
        // Printable ASCII (the font draws nothing else, and a pasted emoji could overflow the rename box's byte buffer).
        var clean = new string(name.Where(c => c is >= ' ' and <= '~' && c != '=').ToArray()).Trim();
        return clean.Length == 0 ? "Loadout" : clean.Length > 24 ? clean[..24] : clean;
    }

    private static string KitText(uint kit) => string.Join(", ", Attachments.InKit(kit).Select(a => a.Name));

    public static void Save()
    {
        if (ReadOnly)
            return;
        try
        {
            File.WriteAllLines(PathName, SaveLines());
        }
        catch
        {
            // Read-only folder: not worth bothering the player about.
        }
    }

    /// <summary>The lines loadout.txt is saved as.</summary>
    public static List<string> SaveLines()
    {
        var lines = new List<string> { $"selected={Selected}", $"hat={Cosmetics.KeyOf(Hat)}" };
        for (var i = 0; i < Slots.Length; i++)
        {
            var l = Slots[i];
            var k = $"loadout{i + 1}.";
            string PerkName(PerkId id) => id == PerkId.None ? "" : Perks.Get(id).Name;
            lines.Add($"{k}name={l.Name}");
            lines.Add($"{k}primary={Weapons.Get(l.Primary).Name}");
            lines.Add($"{k}primary.kit={KitText(l.PrimaryKit)}");
            lines.Add($"{k}secondary={Weapons.Get(l.Secondary).Name}");
            lines.Add($"{k}secondary.kit={KitText(l.SecondaryKit)}");
            lines.Add($"{k}grenade={l.Grenade}");
            lines.Add($"{k}equipment={l.Equipment}");
            lines.Add($"{k}perks={string.Join(", ", new[] { l.Perks.Movement, l.Perks.Survival, l.Perks.Damage }.Where(p => p != PerkId.None).Select(PerkName))}");
            lines.Add($"{k}streaks={l.Streaks}");
        }
        lines.AddRange(LastKit.Where(kv => kv.Value != 0).Select(kv => $"kit.{Weapons.Get(kv.Key).Name}={KitText(kv.Value)}"));
        return lines;
    }
}
