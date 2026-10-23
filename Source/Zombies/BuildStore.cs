namespace CallOfSlop;

/// <summary>
/// The workbench's saved builds: up to <see cref="SlotCount"/> slots in workbench.txt next to the game (a new file; no
/// other save changes). Slots 1-2 are always yours; 3-4 come with the Zombies slot upgrade (bought on the map once per
/// run; the sandbox has a toggle). Lines are <c>slot1=Name|style|Grip{...}</c>, plus <c>melee=</c> (the melee slot),
/// <c>selected=</c>, <c>second=</c>, <c>slots4=</c> (the sandbox toggle) and <c>dummies=</c> (practice dummies in the
/// range). Files from before the melee slot simply have no <c>melee=</c> line and load as they were.
/// </summary>
public static class BuildStore
{
    public const int SlotCount = 4;

    /// <summary>Slots every player has; the rest unlock in Zombies.</summary>
    public const int FreeSlots = 2;

    public static readonly WeaponBuild?[] Slots = new WeaponBuild?[SlotCount];

    /// <summary>The melee slot: a melee build (rooted on a hilt), or null for the standard knife/butt strike.</summary>
    public static WeaponBuild? Melee;

    /// <summary>Sandbox toggle: start the Workbench Range with weapon slots 3-4 unlocked (in Zombies: bought on the map, per run).</summary>
    public static bool ExtraSlotsUnlocked;

    /// <summary>Practice dummies standing down the Workbench Range (for the cart gun, cleave and stun).</summary>
    public static int Dummies = 4;

    /// <summary>The slot being edited (and the primary in the range), and the slot used as the range secondary.</summary>
    public static int Selected, Second = 1;

    /// <summary>Set by the self-test and smoke test so workbench.txt is never written.</summary>
    public static bool ReadOnly;

    private static bool _loaded;

    private static string PathName => SavePaths.PathOf("workbench.txt");

    /// <summary>The <c>version=</c> line written to workbench.txt (files without one load the same).</summary>
    public const int SaveVersion = 1;

    /// <summary>Reads workbench.txt once (missing file: the example builds fill the slots).</summary>
    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        string[]? lines = null;
        try
        {
            lines = SavePaths.ReadLines(PathName, SaveVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[workbench] couldn't read {PathName}: {ex.Message}");
        }
        if (lines == null)
        {
            var ex = BuilderExamples.All();
            for (var i = 0; i < SlotCount && i < ex.Count; i++)
                Slots[i] = ex[i].Build;
            Melee = BuilderExamples.Melee().Select(m => m.Build).FirstOrDefault();
            return;
        }
        Load(lines, Parts.Find, out _);
        // Builds saved before actions were parts got their receivers' stock actions as they loaded: save them back.
        var migrated = Slots.Sum(b => b?.ActionsAdded ?? 0);
        if (migrated > 0)
        {
            Console.WriteLine($"[workbench] added {migrated} stock action(s) to builds saved before actions were parts; saving");
            Save();
        }
    }

    /// <summary>Fills the slots from saved lines; parts that no longer exist are dropped (listed in <paramref name="dropped"/>).</summary>
    public static void Load(IEnumerable<string> lines, Func<string, PartDef?> find, out List<string> dropped)
    {
        dropped = new List<string>();
        Array.Clear(Slots);
        Melee = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq];
            var value = line[(eq + 1)..];
            if (key.StartsWith("slot", StringComparison.Ordinal) && int.TryParse(key[4..], out var n) && n >= 1 && n <= SlotCount)
            {
                try
                {
                    Slots[n - 1] = WeaponBuild.Parse(value, find, dropped);
                }
                catch (FormatException ex)
                {
                    dropped.Add($"slot {n}: {ex.Message}");
                }
            }
            else if (key == "melee")
            {
                try
                {
                    var m = WeaponBuild.Parse(value, find, dropped);
                    Melee = m.IsMelee ? m : null;
                }
                catch (FormatException ex)
                {
                    dropped.Add($"melee: {ex.Message}");
                }
            }
            else if (key == "slots4")
                ExtraSlotsUnlocked = value.Trim() == "1";
            else if (key == "dummies" && int.TryParse(value, out var dn))
                Dummies = Math.Clamp(dn, 0, 8);
            else if (key == "selected" && int.TryParse(value, out var s))
                Selected = Math.Clamp(s - 1, 0, SlotCount - 1);
            else if (key == "second" && int.TryParse(value, out var s2))
                Second = Math.Clamp(s2 - 1, 0, SlotCount - 1);
        }
        foreach (var d in dropped)
            Console.WriteLine($"[workbench] dropped from a saved build: {d}");
    }

    /// <summary>The lines workbench.txt is saved as.</summary>
    public static List<string> Lines()
    {
        var list = new List<string> { "# Call of Slop weapon workbench (Zombies preview). One build per slot: Name|style|Grip{socket:Part{...},...}" };
        for (var i = 0; i < SlotCount; i++)
            if (Slots[i] is { } b)
                list.Add($"slot{i + 1}={b.ToText()}");
        if (Melee is { } m)
            list.Add($"melee={m.ToText()}");
        list.Add($"selected={Selected + 1}");
        list.Add($"second={Second + 1}");
        list.Add($"slots4={(ExtraSlotsUnlocked ? 1 : 0)}");
        list.Add($"dummies={Dummies}");
        return list;
    }

    public static void Save()
    {
        if (ReadOnly)
            return;
        try
        {
            SavePaths.WriteLines(PathName, Lines(), SaveVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[workbench] couldn't save {PathName}: {ex.Message}");
        }
    }
}

/// <summary>
/// The example builds from the design doc (docs/zombies/WEAPON-BUILDER.md, "Example builds"), made from the starter part
/// set. The workbench starts with them, and the self-test checks they're legal and their numbers sane.
/// </summary>
public static class BuilderExamples
{
    /// <summary>An example: the build and the body DPS band the design doc promises for it.</summary>
    public sealed record Example(WeaponBuild Build, float MinDps, float MaxDps, string Note);

    private static PartDef Need(string name, Func<string, PartDef?> find) => find(name) ?? throw new InvalidOperationException($"example build needs the part '{name}'");

    /// <summary>Every example, built from <paramref name="find"/> (default: the loaded parts).</summary>
    public static List<Example> All(Func<string, PartDef?>? find = null)
    {
        find ??= Parts.Find;
        var list = new List<Example>();
        void Try(Func<Example> make)
        {
            try
            {
                var ex = make();
                ActionMigration.Fill(ex.Build, find); // each receiver's stock action (the fire type is a part)
                list.Add(ex);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[workbench] {ex.Message}");
            }
        }

        // A receiver with one barrel, a feed and ammo, in a grip socket.
        BuildNode Rx(BuildNode grip, string socket, string receiver, string barrel, string feed, string ammo, string? muzzle = null)
        {
            var r = grip.Attach(socket, Need(receiver, find));
            var b = r.Attach(r.Part.Sockets.First(s => s.Type == "barrel").Name, Need(barrel, find));
            if (muzzle != null)
                b.Attach("muzzle", Need(muzzle, find));
            r.Attach("feed", Need(feed, find));
            r.Attach("ammo", Need(ammo, find));
            return r;
        }

        Try(() =>
        {
            var b = new WeaponBuild("Plain Rifle", Need("Rifle Grip", find));
            Rx(b.Root!, "receiver_1", "Workhorse Receiver", "Standard Barrel", "Box Mag", "5.56 FMJ", "Flash Hider");
            b.Root!.Attach("stock", Need("Fixed Stock", find));
            b.Root.Attach("optic", Need("Red Dot", find));
            return new Example(b, 200, 320, "The baseline: one receiver, one barrel.");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Twin Splitter Shotgun", Need("Rifle Grip", find));
            var r = b.Root!.Attach("receiver_1", Need("Farmhand Pump Receiver", find));
            var y = r.Attach("barrel", Need("Y-Splitter", find));
            y.Attach("barrel_1", Need("Scattermouth Barrel", find));
            y.Attach("barrel_2", Need("Scattermouth Barrel", find));
            r.Attach("feed", Need("Shell Tube", find));
            r.Attach("ammo", Need("00 Buckshot", find));
            b.Root.Attach("stock", Need("Wire Stock", find));
            return new Example(b, 100, 160, "A conserving splitter: two barrels, each with half the shot.");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Belt-Fed Chaingun", Need("Rifle Grip", find));
            Rx(b.Root!, "receiver_1", "Thumper Chain Gun Receiver", "Big Iron Barrel", "Belt Box", ".50 BMG");
            b.Root!.Attach("foregrip", Need("Chaingun Handle", find));
            b.Root.Attach("stock", Need("Fixed Stock", find));
            return new Example(b, 500, 760, "Heavy rounds off a belt, with a short spin-up.");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Minigun", Need("Rifle Grip", find));
            var r = b.Root!.Attach("receiver_1", Need("Hellwheel Rotary Receiver", find));
            for (var i = 1; i <= 6; i++)
                r.Attach($"barrel_{i}", Need("Standard Barrel", find));
            r.Attach("feed", Need("Backpack Hopper", find));
            r.Attach("ammo", Need("5.56 FMJ", find));
            b.Root.Attach("foregrip", Need("Chaingun Handle", find));
            return new Example(b, 900, 1200, "Six barrels taking turns at 2400 rpm once it has spun up.");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Party Platter", Need("Rifle Grip", find));
            var r = b.Root!.Attach("receiver_1", Need("Workhorse Receiver", find));
            var tray = r.Attach("barrel", Need("Party Tray Splitter", find));
            for (var i = 1; i <= 4; i++)
            {
                var t = tray.Attach($"barrel_{i}", Need("Trident Splitter", find));
                for (var j = 1; j <= 3; j++)
                    t.Attach($"barrel_{j}", Need("Stub Barrel", find));
            }
            r.Attach("feed", Need("Drum Mag", find));
            r.Attach("ammo", Need("5.56 FMJ", find));
            return new Example(b, 80, 220, "Ridiculous: a 4-way splitter feeding four 3-way splitters, twelve barrels (the same total damage, everywhere).");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Toaster Duck", Need("Pistol Grip", find));
            Rx(b.Root!, "receiver_1", "Bucket Launcher Receiver", "Launcher Tube", "Toaster Feed", "Rubber Duck Rounds");
            return new Example(b, 100, 160, "Ridiculous: rubber ducks, two at a time, out of a toaster.");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Triple Pistol", Need("Pistol Grip", find));
            for (var i = 1; i <= 3; i++)
                Rx(b.Root!, $"receiver_{i}", "Plinker Receiver", "Standard Barrel", "Stick Mag", "9mm Ball");
            return new Example(b, 340, 480, "Three pistol receivers on one pistol grip, fired as a volley.");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Nine Lives", Need("Rifle Grip", find));
            for (var i = 1; i <= 9; i++)
                Rx(b.Root!, $"receiver_{i}", "Workhorse Receiver", "Stub Barrel", "Stick Mag", "5.56 FMJ");
            b.Root!.Attach("stock", Need("Fixed Stock", find));
            return new Example(b, 1800, 2700, "Nine rifle receivers on a full rifle grip: heavy, slow, devastating.");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Lumberjack", Need("Chainsaw Grip", find));
            for (var i = 1; i <= 6; i++)
                Rx(b.Root!, $"receiver_{i}", "Gravedigger Auto Receiver", "Scattermouth Barrel", "Shell Drum", "00 Buckshot");
            return new Example(b, 2000, 3200, "Six auto shotguns on a chainsaw grip.");
        });
        Try(() =>
        {
            var b = new WeaponBuild("Shopping Spree", Need("Push Cart", find));
            for (var i = 1; i <= 16; i++)
                Rx(b.Root!, $"receiver_{i}", "Stampede Receiver", "Stub Barrel", "Stick Mag", "9mm Ball");
            return new Example(b, 3000, 5500, "Sixteen SMG receivers on a push cart. Hip fire only.");
        });
        return list;
    }

    /// <summary>The example melee builds (machete, nail bat, sledgehammer, fire axe, chainsaw sword, frying pan, frozen fish).</summary>
    public static List<(WeaponBuild Build, string Note)> Melee(Func<string, PartDef?>? find = null)
    {
        find ??= Parts.Find;
        var list = new List<(WeaponBuild, string)>();
        void Add(string name, string hilt, string head, string? mod1, string? mod2, string note)
        {
            if (find(hilt) is not { } h || find(head) is not { } hd)
                return;
            var b = new WeaponBuild(name, h);
            b.Root!.Attach("head", hd);
            if (mod1 != null && find(mod1) is { } m1)
                b.Root.Attach("mod_1", m1);
            if (mod2 != null && find(mod2) is { } m2)
                b.Root.Attach("mod_2", m2);
            list.Add((b, note));
        }
        Add("Machete", "Wooden Handle", "Machete Blade", null, null, "Fast, cleaves two.");
        Add("Nail Bat", "Bat Grip", "Bat Barrel", "Nail Ring", null, "A bat with nails: hits hard, stuns a little.");
        Add("Sledgehammer", "Long Haft", "Sledgehammer Head", null, null, "Slow, huge damage, long stun.");
        Add("Fire Axe", "Long Haft", "Fire Axe Head", null, null, "Wide swings that take several at once.");
        Add("Chainsaw Sword", "Motor Hilt", "Chainsaw Blade", "Grip Tape", null, "Very fast sweeps through crowds.");
        Add("Frying Pan", "Wooden Handle", "Frying Pan", null, null, "Bonk: big stun.");
        Add("The Codfather", "Fishing Rod Grip", "Frozen Fish", null, "Whoopee Cushion", "Long reach, stuns everything it slaps.");
        return list;
    }
}
