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

    // No hat here: hats are identity, not tactics. The hat lives on the APPEARANCE screen with the rest of your look
    // (AppearanceStore, Source/Cosmetics/Appearance.cs, saved in appearance.txt).

    public Loadout Clone() => (Loadout)MemberwiseClone();

    public uint KitFor(int slot) => slot == 0 ? PrimaryKit : SecondaryKit;

    public byte WeaponIn(int slot) => slot == 0 ? Primary : Secondary;
}

/// <summary>
/// Your loadout slots, saved to loadout.txt next to the game. Every slot is open (the level gates went with levelling,
/// 2026-11-17).
/// Each slot has its own attachments; the last build used on each gun is remembered too, so
/// putting a gun into another slot starts with the attachments you like on it.
/// </summary>
public static class LoadoutStore
{
    private static string PathName => SavePaths.PathOf("loadout.txt");

    /// <summary>The <c>version=</c> line written to loadout.txt (files without one load the same).</summary>
    public const int SaveVersion = 1;

    public static readonly Loadout[] Slots = Enumerable.Range(0, MaxSlots).Select(DefaultLoadout).ToArray();

    /// <summary>The slot you spawn with.</summary>
    public static int Selected;

    /// <summary>Last attachments used on each weapon (the starting kit when a gun goes into a slot).</summary>
    private static readonly Dictionary<byte, uint> LastKit = new();

    /// <summary>A test sets this so it never writes your loadout.txt.</summary>
    public static bool ReadOnly;

    public static string DefaultName(int slot) => $"Custom Loadout {slot + 1}";

    private static Loadout DefaultLoadout(int slot) => new()
    {
        Name = DefaultName(slot),
        Perks = new PerkSet(Perks.Starters[0], Perks.Starters[1], Perks.Starters[2]),
    };

    /// <summary>How many slots there are to use (every one, since levelling went: 2026-11-17).</summary>
    public const int MaxSlots = 10;

    /// <summary><paramref name="slot"/> clamped to the slots there are (an edited file).</summary>
    public static int ValidSlot(int slot) => Math.Clamp(slot, 0, Slots.Length - 1);

    /// <summary>The selected slot as you'd actually spawn with it (anything not valid swapped for defaults).</summary>
    public static Loadout Active()
    {
        Selected = ValidSlot(Selected);
        return Effective(Slots[Selected]);
    }

    /// <summary>A copy of <paramref name="l"/> with anything not valid replaced (a gun in the wrong slot, a grenade in the equipment slot).</summary>
    public static Loadout Effective(Loadout l)
    {
        var e = l.Clone();
        var p = Weapons.Get(l.Primary);
        var s = Weapons.Get(l.Secondary);
        if (p.Slot != Slot.Primary)
            p = Weapons.DefaultPrimary;
        if (s.Slot != Slot.Secondary)
            s = Weapons.DefaultSecondary;
        e.Primary = p.Id;
        e.Secondary = s.Id;
        e.PrimaryKit = Attachments.Sanitize(p, p.Id == l.Primary ? l.PrimaryKit : KitMemory(p.Id));
        e.SecondaryKit = Attachments.Sanitize(s, s.Id == l.Secondary ? l.SecondaryKit : KitMemory(s.Id));
        if (Throwables.Get(e.Grenade).IsEquipment)
            e.Grenade = ThrowableKind.None;
        if (e.Equipment != ThrowableKind.None && !Throwables.Get(e.Equipment).IsEquipment)
            e.Equipment = ThrowableKind.None;
        e.Perks = l.Perks.Sanitized();
        e.Streaks = l.Streaks.Sanitized();
        return e;
    }

    /// <summary>The attachments last used on a weapon (a gun never used yet: its default kit, a sniper's scope).</summary>
    public static uint KitMemory(byte weaponId) =>
        LastKit.TryGetValue(weaponId, out var kit) ? kit : weaponId < Weapons.All.Length ? Attachments.DefaultKit(Weapons.Get(weaponId)) : 0;

    /// <summary>Puts a gun into a slot of a loadout, with the attachments last used on it.</summary>
    public static void SetWeapon(Loadout l, int slot, byte weaponId)
    {
        var kit = Attachments.Sanitize(Weapons.Get(weaponId), KitMemory(weaponId));
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
    //   kit.Needle Bolt-Action=Iron Sights, Match Barrel   (a gun that starts scoped, run without an optic)
    //   hat=fedora                              (older builds only: the hat moved to appearance.txt, which takes it from here
    //                                            once, AppearanceStore.Load; this build ignores it and no longer writes it)
    //
    // Older files had one loadout (primary=..., secondary=..., grenade=..., equipment=...): it becomes slot 1.
    // On a gun that starts scoped (snipers) "Iron Sights" in a kit means no optic. A kit for such a gun with neither an optic
    // nor "Iron Sights" (every file from before snipers started scoped) gets the gun's default scope: their empty optic
    // slot used to show a built-in scope, so they keep looking through one. Iron sights are then a choice made in the gunsmith.

    public static void Load()
    {
        string[]? lines = null;
        try
        {
            lines = SavePaths.ReadLines(PathName, SaveVersion);
            if (lines == null && File.Exists(PathName))
                lines = []; // damaged file and no usable backup: defaults (but not a brand-new player)
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
                        continue; // an older build's hat: migrated to appearance.txt (AppearanceStore.LegacyHat), not a loadout field
                    else if (key.StartsWith("kit.") && Weapons.Find(key[4..]) is { } kw)
                        LastKit[kw.Id] = Attachments.Sanitize(kw, ParseKit(kw, value, out _));
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
        // Old files kept attachments per gun only: give each slot its gun's remembered build
        // (not over a sniper's iron sights, which are an empty kit chosen on purpose).
        foreach (var l in Slots)
        {
            if (l.PrimaryKit == 0 && !ChosenIrons.Contains((l, 0)))
                l.PrimaryKit = KitMemory(l.Primary);
            if (l.SecondaryKit == 0 && !ChosenIrons.Contains((l, 1)))
                l.SecondaryKit = KitMemory(l.Secondary);
        }
        ChosenIrons.Clear();
        // Brand-new players get the frag as soon as it unlocks.
        if (fresh)
            foreach (var l in Slots)
                l.Grenade = ThrowableKind.Frag;
    }

    /// <summary>Loadout guns whose kit line said "Iron Sights" while loading (so the empty kit isn't replaced).</summary>
    private static readonly HashSet<(Loadout, int)> ChosenIrons = new();

    /// <summary>
    /// A kit from its saved attachment names. On a gun that starts scoped, "Iron Sights" means no optic, and a kit with
    /// neither an optic nor "Iron Sights" (an older file) gets the default scope (<paramref name="irons"/>: it said "Iron Sights").
    /// </summary>
    private static uint ParseKit(WeaponDef w, string value, out bool irons)
    {
        uint kit = 0;
        irons = false;
        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (name.Equals(Attachments.IronSightsName, StringComparison.OrdinalIgnoreCase))
                irons = true;
            else if (Attachments.Find(name) is { } a)
                kit = Attachments.With(kit, a.Slot, a.Index);
        }
        if (Attachments.DefaultOptic(w) is { } scope && Attachments.Slot(kit, AttachSlot.Optic) == 0)
        {
            if (irons)
                return kit;
            kit = Attachments.With(kit, AttachSlot.Optic, scope.Index);
        }
        irons = false; // only meaningful where the empty optic slot is a choice
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
                l.PrimaryKit = Attachments.Sanitize(Weapons.Get(l.Primary), ParseKit(Weapons.Get(l.Primary), value, out var ironsP));
                if (ironsP)
                    ChosenIrons.Add((l, 0));
                break;
            case "secondary.kit":
                l.SecondaryKit = Attachments.Sanitize(Weapons.Get(l.Secondary), ParseKit(Weapons.Get(l.Secondary), value, out var ironsS));
                if (ironsS)
                    ChosenIrons.Add((l, 1));
                break;
            case "grenade" when Enum.TryParse<ThrowableKind>(value, out var g) && !Throwables.Get(g).IsEquipment && Throwables.Get(g).InLoadouts:
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

    /// <summary>A kit as saved: its attachment names, with "Iron Sights" first on a gun that starts scoped and has no optic.</summary>
    private static string KitText(WeaponDef w, uint kit)
    {
        var names = Attachments.InKit(kit).Select(a => a.Name);
        if (Attachments.StartsScoped(w) && Attachments.Slot(kit, AttachSlot.Optic) == 0)
            names = names.Prepend(Attachments.IronSightsName);
        return string.Join(", ", names);
    }

    public static void Save()
    {
        if (ReadOnly)
            return;
        try
        {
            SavePaths.WriteLines(PathName, SaveLines(), SaveVersion);
        }
        catch
        {
            // Read-only folder: not worth bothering the player about.
        }
    }

    /// <summary>The lines loadout.txt is saved as.</summary>
    public static List<string> SaveLines()
    {
        var lines = new List<string> { $"selected={Selected}" }; // (no hat= line any more: appearance.txt)
        for (var i = 0; i < Slots.Length; i++)
        {
            var l = Slots[i];
            var k = $"loadout{i + 1}.";
            string PerkName(PerkId id) => id == PerkId.None ? "" : Perks.Get(id).Name;
            lines.Add($"{k}name={l.Name}");
            lines.Add($"{k}primary={Weapons.Get(l.Primary).Name}");
            lines.Add($"{k}primary.kit={KitText(Weapons.Get(l.Primary), l.PrimaryKit)}");
            lines.Add($"{k}secondary={Weapons.Get(l.Secondary).Name}");
            lines.Add($"{k}secondary.kit={KitText(Weapons.Get(l.Secondary), l.SecondaryKit)}");
            lines.Add($"{k}grenade={l.Grenade}");
            lines.Add($"{k}equipment={l.Equipment}");
            lines.Add($"{k}perks={string.Join(", ", new[] { l.Perks.Movement, l.Perks.Survival, l.Perks.Damage }.Where(p => p != PerkId.None).Select(PerkName))}");
            lines.Add($"{k}streaks={l.Streaks}");
        }
        lines.AddRange(LastKit.Where(kv => kv.Value != 0 || Attachments.StartsScoped(Weapons.Get(kv.Key)))
            .Select(kv => $"kit.{Weapons.Get(kv.Key).Name}={KitText(Weapons.Get(kv.Key), kv.Value)}"));
        return lines;
    }
}
