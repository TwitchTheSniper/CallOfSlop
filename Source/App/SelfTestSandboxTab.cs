using Brutal.GlfwApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox Tab inventory" (board item dev-sandbox-tab-inventory-ti01; Keybinds.Context): the Sandbox inventory is its
    /// own action on Tab, live only in Sandbox, and the scoreboard is live everywhere but Sandbox, so Tab in a Sandbox match
    /// gives the inventory and no scoreboard while Tab in Team Deathmatch and Zombies still gives the scoreboard (the Zombies
    /// parts inventory keeps I); the defaults share no key within a context; and settings.txt from binds version 12 migrate:
    /// a never-rebound inventory gets Tab, a rebound one keeps its key for the Sandbox inventory, and with Tab taken by a
    /// key used everywhere the Sandbox inventory keeps I. Uses temporary files only.
    /// </summary>
    private static void CheckSandboxTab()
    {
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var kd = Keybinds.Defaults;
        var tab = Keybinds.Key(GlfwKey.Tab);
        var i = Keybinds.Key(GlfwKey.I);
        Want(kd[(int)GameAction.SandboxInventory] == tab && kd[(int)GameAction.Scoreboard] == tab && kd[(int)GameAction.Inventory] == i && Duplicates(kd).Length == 0,
            "the Sandbox inventory doesn't default to Tab beside the scoreboard (or the Zombies inventory left I, or the defaults clash)");

        // Tab held in a real match of each mode (what PollActions reads: Keybinds.Resolve with the game's SandboxOn).
        var down = new bool[(int)GameAction.COUNT];
        var seen = new List<string>();
        foreach (var mode in new[] { GameMode.Sandbox, GameMode.TeamDeathmatch, GameMode.Zombies })
        {
            byte mapId = 0;
            if (mode == GameMode.Sandbox && Maps.Find(ConstructYardMap) is { } yard)
                mapId = yard.Id;
            using var g = ModeGame(0, mode, mapId);
            for (var k = 0; k < down.Length; k++)
                down[k] = kd[k] == tab;
            Keybinds.Resolve(kd, g.SandboxOn, down);
            var inv = down[(int)GameAction.SandboxInventory];
            var scores = down[(int)GameAction.Scoreboard];
            seen.Add($"{mode}: inventory {inv}, scoreboard {scores}");
            if (mode == GameMode.Sandbox)
                Want(g.SandboxOn && inv && !scores, "Tab in a Sandbox match doesn't open the inventory, or shows the scoreboard");
            else
                Want(!g.SandboxOn && scores && !inv, $"Tab in {mode} doesn't show the scoreboard (or opens the Sandbox inventory)");
        }
        // The Zombies parts inventory (I) is off in Sandbox, the Sandbox one off elsewhere.
        for (var k = 0; k < down.Length; k++)
            down[k] = kd[k] == i;
        Keybinds.Resolve(kd, true, down);
        Want(!down[(int)GameAction.Inventory], "I still opens the Zombies parts inventory in Sandbox");
        Line($"Tab: {string.Join("; ", seen)}; the Zombies inventory on {Keybinds.Name(kd[(int)GameAction.Inventory])}");

        // Migration through settings.txt, from binds version 12 (no bind_SandboxInventory line).
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-sbtab-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            int[] Load(Action<int[]> change)
            {
                var b = (int[])kd.Clone();
                change(b);
                var lines = new List<string> { "name=Old", "binds_version=12" };
                for (var a = 0; a < b.Length; a++)
                    if ((GameAction)a != GameAction.SandboxInventory)
                        lines.Add($"bind_{(GameAction)a}={b[a]}");
                File.WriteAllLines(tmp, lines);
                return Settings.Load(tmp).Binds;
            }
            var plain = Load(_ => { });
            var k = Keybinds.Key(GlfwKey.K);
            var custom = Load(b => b[(int)GameAction.Inventory] = k);
            var tabTaken = Load(b =>
            {
                b[(int)GameAction.Scoreboard] = Keybinds.Key(GlfwKey.N);
                b[(int)GameAction.Jump] = tab; // (a key used everywhere on Tab)
            });
            Line($"migration from version 12: never rebound: Sandbox inventory {Keybinds.Name(plain[(int)GameAction.SandboxInventory])}; parts inventory rebound to K: {Keybinds.Name(custom[(int)GameAction.SandboxInventory])} (Zombies {Keybinds.Name(custom[(int)GameAction.Inventory])}); Tab taken by Jump: {Keybinds.Name(tabTaken[(int)GameAction.SandboxInventory])}");
            Want(plain.SequenceEqual(kd), "a never-rebound version 12 file doesn't load as today's defaults (the Sandbox inventory on Tab)");
            Want(custom[(int)GameAction.SandboxInventory] == k && custom[(int)GameAction.Inventory] == k && Duplicates(custom).Length == 0, "a rebound inventory key isn't kept for the Sandbox inventory");
            Want(tabTaken[(int)GameAction.SandboxInventory] == i && tabTaken[(int)GameAction.Jump] == tab && Duplicates(tabTaken).Length == 0, "with Tab taken the Sandbox inventory doesn't keep I");
            var s = Settings.Load(tmp);
            s.Save(tmp);
            Want(Settings.Load(tmp).Binds.SequenceEqual(s.Binds), "the migrated binds don't load back the same");
        }
        finally
        {
            try
            {
                File.Delete(tmp);
                File.Delete(tmp + ".bak");
            }
            catch (IOException) { }
        }
        if (problems.Count > 0)
            Fail("sandbox Tab inventory: " + string.Join("; ", problems));
    }
}
