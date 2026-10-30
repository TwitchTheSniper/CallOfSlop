using Brutal.GlfwApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The default key bindings (no two actions share a key; Interact is F, tactical equipment E) and the one-time
    /// settings.txt migration from the old defaults (Interact E, equipment F): untouched binds move to the new defaults,
    /// rebound ones stay, and migrating never puts two actions on one key. Uses temporary files only.
    /// </summary>
    private static void CheckKeybinds()
    {
        var d = Keybinds.Defaults;
        if (d.Length != (int)GameAction.COUNT)
            Fail($"keybinds: {d.Length} defaults for {(int)GameAction.COUNT} actions");
        var dupes = Duplicates(d);
        if (dupes.Length > 0)
            Fail($"keybinds: default binds share a key: {dupes}");
        if (d[(int)GameAction.Interact] != Keybinds.Key(GlfwKey.F))
            Fail($"keybinds: Interact defaults to {(GlfwKey)d[(int)GameAction.Interact]}, not F");
        if (d[(int)GameAction.Equipment] != Keybinds.Key(GlfwKey.E))
            Fail($"keybinds: tactical equipment defaults to {(GlfwKey)d[(int)GameAction.Equipment]}, not E");
        Line($"defaults: {d.Count(c => c != Keybinds.Unbound)} bound actions, no shared keys (within a context); Interact F, Use equipment E, Throw grenade {(GlfwKey)d[(int)GameAction.Grenade]}, Melee {(GlfwKey)d[(int)GameAction.Melee]}");

        var e = Keybinds.Key(GlfwKey.E);
        var f = Keybinds.Key(GlfwKey.F);
        var h = Keybinds.Key(GlfwKey.H);
        var v = Keybinds.Key(GlfwKey.V);
        var old = (int[])d.Clone(); // the version 1 defaults
        old[(int)GameAction.Interact] = e;
        old[(int)GameAction.Equipment] = f;

        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-keybinds-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            int[] LoadOld(int[] binds, int? version, params GameAction[] skip)
            {
                var lines = new List<string> { "name=Old", "fov=90" };
                if (version is { } ver)
                    lines.Add($"binds_version={ver}");
                for (var i = 0; i < binds.Length; i++)
                    if (!skip.Contains((GameAction)i))
                        lines.Add($"bind_{(GameAction)i}={binds[i]}");
                File.WriteAllLines(tmp, lines);
                return Settings.Load(tmp).Binds;
            }

            // 1. Never rebound: the whole old default set becomes today's defaults.
            var b = LoadOld(old, null);
            if (!b.SequenceEqual(d))
                Fail($"keybinds migration: old defaults load as {Describe(b)}, not today's defaults");
            // Saving writes binds_version, so a later load doesn't migrate again.
            var s = Settings.Load(tmp);
            s.Save(tmp);
            if (!File.ReadAllLines(tmp).Contains($"binds_version={Keybinds.DefaultsVersion}") || !Settings.Load(tmp).Binds.SequenceEqual(d))
                Fail("keybinds migration: settings.txt doesn't save binds_version or doesn't load back the same binds");

            // 2. Interact rebound to H, equipment still on the old F: only equipment moves (to E).
            var custom = (int[])old.Clone();
            custom[(int)GameAction.Interact] = h;
            b = LoadOld(custom, null);
            if (b[(int)GameAction.Interact] != h || b[(int)GameAction.Equipment] != e || Duplicates(b).Length > 0)
                Fail($"keybinds migration: custom Interact H + old equipment F gave {Describe(b)}");

            // 3. Interact on the old E, but the player put Melee on F (and equipment on V): F is taken, so Interact stays E.
            custom = (int[])old.Clone();
            custom[(int)GameAction.Melee] = f;
            custom[(int)GameAction.Equipment] = v;
            b = LoadOld(custom, null);
            if (b[(int)GameAction.Interact] != e || b[(int)GameAction.Melee] != f || b[(int)GameAction.Equipment] != v || Duplicates(b).Length > 0)
                Fail($"keybinds migration: a custom bind on F should keep Interact on E, got {Describe(b)}");

            // 4. A version 2 file with Interact E and equipment F: the player chose that, so nothing moves.
            b = LoadOld(old, Keybinds.DefaultsVersion);
            if (!b.SequenceEqual(old))
                Fail($"keybinds migration: a binds_version=2 file was changed: {Describe(b)}");

            // 5. Files from before Interact existed (no bind_Interact line): Interact F, equipment F -> E.
            b = LoadOld(old, null, GameAction.Interact, GameAction.Weapon3, GameAction.Weapon4);
            if (!b.SequenceEqual(d))
                Fail($"keybinds migration: a file without bind_Interact loads as {Describe(b)}");

            // 6. No bind lines at all, and no file at all: today's defaults.
            File.WriteAllLines(tmp, ["name=Old"]);
            var none = Settings.Load(tmp).Binds;
            File.Delete(tmp);
            if (!none.SequenceEqual(d) || !Settings.Load(tmp).Binds.SequenceEqual(d))
                Fail("keybinds migration: a file without binds (or no file) doesn't load today's defaults");

            // 7. Every single-key starting point: migration never leaves two actions on one key that weren't already.
            var conflicts = 0;
            for (var a = 0; a < old.Length; a++)
                foreach (var code in new[] { e, f, h })
                {
                    var start = (int[])old.Clone();
                    Keybinds.Assign(start, (GameAction)a, code);
                    var before = Duplicates(start).Length;
                    Keybinds.Migrate(start, 1);
                    if (Duplicates(start).Length > before)
                        conflicts++;
                }
            if (conflicts > 0)
                Fail($"keybinds migration: {conflicts} starting point(s) ended with two actions on one key");
            Line("migration: old defaults -> Interact F / equipment E; rebound keys kept; blocked moves skipped; version 2 files and new files untouched");
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Actions sharing a key while both are live, as "Key: A, B; ..." (empty when there are none). Unbound doesn't count,
    /// nor does a Sandbox key shared with one live only outside Sandbox (Keybinds.Overlap).
    /// </summary>
    private static string Duplicates(int[] binds) =>
        string.Join("; ", Enumerable.Range(0, binds.Length)
            .Where(i => binds[i] != Keybinds.Unbound)
            .GroupBy(i => binds[i])
            .Where(g => g.Any(a => g.Any(b => a != b && Keybinds.Overlap((GameAction)a, (GameAction)b))))
            .Select(g => $"{Code(g.Key)}: {string.Join(", ", g.Select(i => (GameAction)i))}"));

    private static string Describe(int[] binds) =>
        $"Interact {Code(binds[(int)GameAction.Interact])}, Use equipment {Code(binds[(int)GameAction.Equipment])}, Melee {Code(binds[(int)GameAction.Melee])}"
        + (Duplicates(binds) is { Length: > 0 } dup ? $" (shared: {dup})" : "");

    private static string Code(int code) => code == Keybinds.Unbound ? "-" : code < 0 ? $"Mouse{-code - 2}" : ((GlfwKey)code).ToString();
}
