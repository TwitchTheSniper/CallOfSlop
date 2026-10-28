namespace CallOfSlop;

/// <summary>
/// The workbench's blueprint library (docs/zombies/WEAPON-BUILDER.md, "Blueprints"): any number of named builds (guns,
/// melee weapons and grenades) saved in blueprints.txt in the save folder, kept across sessions and updates. One line per
/// blueprint, <c>bp=Name|style|Grip{...}</c> (the build text of <see cref="WeaponBuild.ToText"/>), in the library's order;
/// a part that no longer exists loads with that part dropped, as workbench.txt does. The first time the library is opened
/// it takes a copy of the builds already saved in workbench.txt (its slots, the melee and the grenade line), so nobody
/// starts with an empty library; <c>migrated=1</c> records that it has. The range's slots in workbench.txt are unchanged.
/// </summary>
public static class BlueprintStore
{
    public const string FileName = "blueprints.txt";

    /// <summary>The <c>version=</c> line written to blueprints.txt.</summary>
    public const int SaveVersion = 1;

    /// <summary>The most blueprints the library keeps (Save as blueprint is refused past it).</summary>
    public const int MaxCount = 500;

    /// <summary>The library, in its saved order (new blueprints go at the end).</summary>
    public static readonly List<WeaponBuild> All = new();

    /// <summary>Bumped on every change (the workbench's cached badges compare it).</summary>
    public static int Version { get; private set; }

    /// <summary>Set by the self-test and smoke test so blueprints.txt is never written.</summary>
    public static bool ReadOnly;

    private static bool _loaded;

    private static string PathName => SavePaths.PathOf(FileName);

    /// <summary>Reads blueprints.txt once; with no file yet, copies the builds in workbench.txt in and saves (once).</summary>
    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        BuildStore.EnsureLoaded();
        LoadFolder(BuildStore.FromFile);
    }

    /// <summary>
    /// Reads blueprints.txt from the save folder. With no file (nor backup) yet: the library starts with a copy of
    /// <see cref="BuildStore"/>'s builds when <paramref name="migrateSlots"/> (workbench.txt was there), and is saved
    /// at once so that copy is made only once.
    /// </summary>
    internal static void LoadFolder(bool migrateSlots)
    {
        string[]? lines = null;
        try
        {
            lines = SavePaths.ReadLines(PathName, SaveVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[blueprints] couldn't read {PathName}: {ex.Message}");
        }
        if (lines != null)
        {
            Load(lines, Parts.Find, out _);
            return;
        }
        All.Clear();
        if (migrateSlots)
        {
            var n = MigrateFromSlots();
            Console.WriteLine($"[blueprints] no {FileName} yet: copied {n} build(s) from workbench.txt into the library");
        }
        Save(); // with migrated=1, so the slots are copied in only once
    }

    /// <summary>Copies workbench.txt's builds (slots 1-4, then the melee and grenade lines) to the end of the library; returns how many.</summary>
    public static int MigrateFromSlots()
    {
        var n = 0;
        void Take(WeaponBuild? b)
        {
            if (b?.Root == null || All.Count >= MaxCount)
                return;
            var copy = b.Clone();
            copy.Name = UniqueName(copy.Name);
            All.Add(copy);
            n++;
        }
        foreach (var s in BuildStore.Slots)
            Take(s);
        Take(BuildStore.Melee);
        Take(BuildStore.Grenade);
        Version++;
        return n;
    }

    /// <summary>Fills the library from saved lines; parts that no longer exist are dropped (listed in <paramref name="dropped"/>).</summary>
    public static void Load(IEnumerable<string> lines, Func<string, PartDef?> find, out List<string> dropped)
    {
        dropped = new List<string>();
        All.Clear();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("bp=", StringComparison.Ordinal) || All.Count >= MaxCount)
                continue;
            try
            {
                var b = WeaponBuild.Parse(line[3..], find, dropped);
                if (b.Root != null)
                    All.Add(b);
                else
                    dropped.Add($"blueprint '{b.Name}': nothing left of it");
            }
            catch (FormatException ex)
            {
                dropped.Add($"a blueprint: {ex.Message}");
            }
        }
        Version++;
        foreach (var d in dropped)
            Console.WriteLine($"[blueprints] dropped from a blueprint: {d}");
    }

    /// <summary>The lines blueprints.txt is saved as.</summary>
    public static List<string> Lines()
    {
        var list = new List<string>(All.Count + 2)
        {
            "# Call of Slop workbench blueprints. One build per line: bp=Name|style|Grip{socket:Part{...},...}",
            "migrated=1",
        };
        foreach (var b in All)
            list.Add($"bp={b.ToText()}");
        return list;
    }

    public static void Save()
    {
        Version++;
        if (ReadOnly)
            return;
        try
        {
            SavePaths.WriteLines(PathName, Lines(), SaveVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[blueprints] couldn't save {PathName}: {ex.Message}");
        }
    }

    /// <summary><paramref name="name"/>, or "name 2", "name 3"... when the library already has it (case-insensitive; never over 32 characters).</summary>
    public static string UniqueName(string name, WeaponBuild? except = null)
    {
        name = WeaponBuild.CleanName(name);
        bool Taken(string n) => All.Any(b => b != except && b.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name))
            return name;
        for (var i = 2; ; i++)
        {
            var tail = $" {i}";
            var head = name.Length + tail.Length > 32 ? name[..(32 - tail.Length)].TrimEnd() : name;
            if (!Taken(head + tail))
                return head + tail;
        }
    }

    /// <summary>Saves a copy of <paramref name="b"/> as a new blueprint (a unique name); its index, or -1 when it can't be saved and why.</summary>
    public static int Add(WeaponBuild b, out string? why)
    {
        why = b.Root == null ? "Pick a grip first: an empty build has nothing to save." : All.Count >= MaxCount ? $"The library is full ({MaxCount} blueprints): delete some first." : null;
        if (why != null)
            return -1;
        var copy = b.Clone();
        copy.Name = UniqueName(copy.Name);
        All.Add(copy);
        Save();
        return All.Count - 1;
    }

    /// <summary>Puts a copy of <paramref name="b"/> in place of blueprint <paramref name="i"/> (keeping that blueprint's name).</summary>
    public static void Overwrite(int i, WeaponBuild b)
    {
        if ((uint)i >= (uint)All.Count || b.Root == null)
            return;
        var copy = b.Clone();
        copy.Name = All[i].Name;
        All[i] = copy;
        Save();
    }

    /// <summary>Renames blueprint <paramref name="i"/> (cleaned, made unique); returns the name it got.</summary>
    public static string Rename(int i, string name)
    {
        if ((uint)i >= (uint)All.Count)
            return name;
        All[i].Name = UniqueName(name, All[i]);
        Save();
        return All[i].Name;
    }

    /// <summary>A copy of blueprint <paramref name="i"/> right after it ("Name 2"); its index, or -1.</summary>
    public static int Duplicate(int i)
    {
        if ((uint)i >= (uint)All.Count || All.Count >= MaxCount)
            return -1;
        var copy = All[i].Clone();
        copy.Name = UniqueName(copy.Name);
        All.Insert(i + 1, copy);
        Save();
        return i + 1;
    }

    public static void Delete(int i)
    {
        if ((uint)i >= (uint)All.Count)
            return;
        All.RemoveAt(i);
        Save();
    }

    /// <summary>Self-test: forget what was loaded, so the next <see cref="EnsureLoaded"/> reads the save folder again.</summary>
    internal static void ResetForTest()
    {
        _loaded = false;
        All.Clear();
        Version++;
    }
}

/// <summary>A part of a blueprint that a Zombies run couldn't fill: the empty socket it goes in, the part, and how many parts were on it.</summary>
public readonly record struct BlueprintGap(string ParentPath, string Socket, PartDef Part, int Under);

/// <summary>
/// Loading a blueprint in a Zombies run (docs/zombies/WEAPON-BUILDER.md, "Blueprints"): the build is put together from
/// the parts you own only (your loose parts plus the slot's current build: what the server accepts), in the build's order;
/// a part you don't have leaves its socket empty (a <see cref="BlueprintGap"/>, drawn as a red ring on the gun and listed),
/// and whatever was on that part is left off with it. It never makes a part you don't have.
/// </summary>
public static class BlueprintFit
{
    /// <summary>
    /// <paramref name="bp"/> filled from <paramref name="stock"/>, with the parts it couldn't fill in <paramref name="gaps"/>;
    /// null when you don't own its grip, hilt or casing (nothing to build on).
    /// </summary>
    public static WeaponBuild? Fit(WeaponBuild bp, BenchStock stock, List<BlueprintGap> gaps)
    {
        gaps.Clear();
        if (bp.Root == null)
            return null;
        if (stock.IsUnlimited)
            return bp.Clone();
        var left = new Dictionary<PartDef, int>(ReferenceEqualityComparer.Instance);
        bool Take(PartDef p)
        {
            if (!left.TryGetValue(p, out var n))
                n = stock.Owned(p);
            if (n <= 0)
                return false;
            left[p] = n - 1;
            return true;
        }
        if (!Take(bp.Root.Part))
            return null;
        var b = new WeaponBuild(bp.Name, bp.Root.Part) { StyleChoice = bp.StyleChoice };
        void Copy(BuildNode from, BuildNode to)
        {
            foreach (var (s, c) in from.Children())
            {
                if (!Take(c.Part))
                {
                    gaps.Add(new BlueprintGap(to.Path(), s.Name, c.Part, c.Walk().Count() - 1));
                    continue;
                }
                Copy(c, to.Attach(s.Name, c.Part));
            }
        }
        Copy(bp.Root, b.Root!);
        return b;
    }
}
