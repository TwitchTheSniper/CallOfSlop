namespace CallOfSlop;

/// <summary>
/// Self-test for hidden locked loadout slots: how many slots are listed at a few levels, the "next slot" hint,
/// a locked selection falling back to an unlocked slot (edited file, prestige), and locked slots keeping their
/// contents through a loadout.txt save/load (a temp file, never the real save).
/// </summary>
public static partial class SelfTest
{
    private static void CheckHiddenSlots()
    {
        static Progress At(int level) => new() { ReadOnly = true, Xp = Progress.TotalFor(level) };

        // Visible slot count and the next-slot hint at a few levels.
        foreach (var (level, slots, next) in new (int, int, int?)[] { (1, 1, 6), (6, 2, 11), (17, 4, 22), (48, 10, null) })
        {
            var p = At(level);
            var shown = LoadoutStore.UnlockedSlots(p);
            var hint = LoadoutStore.NextSlotLevel(p);
            Line($"level {p.Level}: {shown} slot(s) listed, next slot at {(hint?.ToString() ?? "-")}");
            if (p.Level != level || shown != slots)
                Fail($"level {level}: {shown} loadout slots listed (should be {slots})");
            if (hint != next)
                Fail($"level {level}: next slot hint says {hint?.ToString() ?? "none"} (should be {next?.ToString() ?? "none"})");
            for (var i = 0; i < Progress.MaxLoadoutSlots; i++)
                if (LoadoutStore.UnlockedSlot(i, p) != Math.Min(i, slots - 1))
                    Fail($"level {level}: slot {i + 1} falls back to {LoadoutStore.UnlockedSlot(i, p) + 1} (should be {Math.Min(i, slots - 1) + 1})");
        }

        var backup = LoadoutStore.SaveLines().ToArray();
        var selectedBefore = LoadoutStore.Selected;
        var temp = Path.Combine(Path.GetTempPath(), $"cos_hidden_slots_{Environment.ProcessId}.txt");
        try
        {
            // An edited file picks slot 9 at level 6 (2 slots): spawning uses slot 2, slot 9 keeps its gun and name.
            var smg = Weapons.All.First(w => w.Slot == Slot.Primary && w != Weapons.DefaultPrimary);
            LoadoutStore.LoadLines(["selected=8", "loadout9.name=Later", $"loadout9.primary={smg.Name}", "loadout2.name=Two"]);
            var low = At(6);
            var active = LoadoutStore.Active(low);
            if (LoadoutStore.Selected != 1 || active.Name != "Two")
                Fail($"a locked selected slot fell back to slot {LoadoutStore.Selected + 1} '{active.Name}' (should be slot 2 'Two')");

            // Save at the low level, load again from a temp file: the locked slot is untouched.
            File.WriteAllLines(temp, LoadoutStore.SaveLines());
            LoadoutStore.LoadLines(File.ReadAllLines(temp));
            var kept = LoadoutStore.Slots[8];
            if (kept.Name != "Later" || kept.Primary != smg.Id)
                Fail($"locked slot 9 lost its contents through save/load ('{kept.Name}', {Weapons.Get(kept.Primary).Name})");
            else
                Line($"locked slot 9 kept '{kept.Name}' with the {smg.Name} through save/load, back once unlocked: {LoadoutStore.UnlockedSlots(At(Progress.LoadoutSlotLevel(8))) > 8}");

            // Prestige: back to level 1, so only slot 1 is listed and the selection falls back to it; nothing is deleted.
            var maxed = At(Progress.MaxLevel);
            LoadoutStore.Selected = 9;
            if (maxed.DoPrestige())
            {
                LoadoutStore.Active(maxed);
                if (LoadoutStore.UnlockedSlots(maxed) != 1 || LoadoutStore.Selected != 0 || LoadoutStore.Slots[8].Name != "Later")
                    Fail($"after prestige: {LoadoutStore.UnlockedSlots(maxed)} slot(s), selected {LoadoutStore.Selected + 1}, slot 9 '{LoadoutStore.Slots[8].Name}'");
            }
            else
                Fail("a max-level player couldn't prestige");
        }
        finally
        {
            try { File.Delete(temp); } catch { /* temp file: not important */ }
            LoadoutStore.LoadLines(backup);
            LoadoutStore.Selected = selectedBefore;
        }
    }
}
