namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The grip picker's "Show all grips" checkbox (BenchGrips in BenchModel.cs): off lists only the grips you own, on
    /// lists every grip in the game with the unowned ones flagged; an unowned grip can't be picked where the session uses
    /// an inventory (a Zombies run's bench) but can in the sandbox; the sandbox (every part) lists everything either way;
    /// the setting saves to settings.txt (default off); refreshing with unchanged inputs allocates nothing.
    /// </summary>
    private static void CheckWorkbenchGrips()
    {
        PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"grip test needs the part '{name}'");
        var service = P("Service Rifle Grip");
        var allGrips = Parts.All.Where(p => p.Frame != null).ToList();
        var allHilts = Parts.All.Where(p => p.Hilt != null).ToList();
        var stock = BenchStock.FromRun([new("Service Rifle Grip", 1), new("Standard Barrel", 2)], null, Parts.Find);
        var list = new BenchGrips();

        list.Refresh(stock, Parts.All, false, false);
        var offNames = string.Join(", ", list.Items.Select(p => p.Name));
        var offHidden = list.Hidden;
        Line($"show all grips OFF with one grip owned: {list.Items.Count} listed ({offNames}), {offHidden} hidden of {allGrips.Count}");
        if (list.Items.Count != 1 || list.Items[0] != service || !list.Owned[0] || offHidden != allGrips.Count - 1)
            Fail("workbench grips: with 'Show all grips' off only the owned grip should be listed");

        list.Refresh(stock, Parts.All, false, true);
        var unowned = list.Items.Where((p, i) => !list.Owned[i]).ToList();
        var badFlags = list.Items.Where((p, i) => list.Owned[i] != stock.Owned(p) > 0 || list.Slots[i] != p.ReceiverCapacity).Count();
        var sorted = list.Slots.Zip(list.Slots.Skip(1)).All(t => t.First <= t.Second);
        Line($"show all grips ON: {list.Items.Count} listed of {allGrips.Count}, {unowned.Count} unowned (dimmed), {list.Hidden} hidden, {badFlags} bad flags, by slot count {sorted}");
        if (list.Items.Count != allGrips.Count || !allGrips.All(list.Items.Contains) || unowned.Count != allGrips.Count - 1 || list.Hidden != 0 || badFlags > 0 || !sorted)
            Fail("workbench grips: with 'Show all grips' on every grip in the game should be listed, the unowned ones flagged");

        // Picking: an unowned grip is locked at a run's bench (inventory), allowed in the sandbox (planning).
        var other = unowned.FirstOrDefault() ?? throw new InvalidOperationException("grip test needs a second grip");
        bool zOwned = BenchGrips.CanPick(stock, service, true), zOther = BenchGrips.CanPick(stock, other, true), sbOther = BenchGrips.CanPick(stock, other, false);
        var unlimitedPick = BenchGrips.CanPick(BenchStock.Unlimited, other, true);
        Line($"picking in Zombies: owned {service.Name} {zOwned}, unowned {other.Name} {zOther}; sandbox practice stock: {sbOther}; every part: {unlimitedPick}");
        if (!zOwned || zOther || !sbOther || !unlimitedPick)
            Fail("workbench grips: an unowned grip must not be pickable in Zombies (owned ones are); the sandbox can plan with any grip");

        // The sandbox's every-part stock: everything shows, ticked or not. The melee slot lists hilts.
        list.Refresh(BenchStock.Unlimited, Parts.All, false, false);
        var sandboxOff = list.Items.Count;
        list.Refresh(BenchStock.Unlimited, Parts.All, true, false);
        var hiltsOff = list.Items.Count;
        list.Refresh(stock, Parts.All, true, true);
        var hiltsOn = list.Items.Count;
        var hiltsOwned = list.Owned.Count(o => o);
        Line($"sandbox (every part), unticked: {sandboxOff} grips, {hiltsOff} hilts; melee slot ticked with no hilt owned: {hiltsOn} hilts, {hiltsOwned} owned");
        if (sandboxOff != allGrips.Count || hiltsOff != allHilts.Count || hiltsOn != allHilts.Count || hiltsOwned != 0 || list.Items.Any(p => p.Hilt == null))
            Fail("workbench grips: the sandbox should list every grip unticked; the melee slot lists hilts");

        // ---- The setting ------------------------------------------------------------------------------------------
        var path = Path.Combine(Path.GetTempPath(), $"cos_wbgrips_{Environment.ProcessId}.txt");
        try
        {
            new Settings { WorkbenchShowAllGrips = true }.Save(path);
            var on = Settings.Load(path).WorkbenchShowAllGrips;
            new Settings().Save(path);
            var off = Settings.Load(path).WorkbenchShowAllGrips;
            File.WriteAllText(path, "volume=0.5\n"); // an old save without the key
            var old = Settings.Load(path).WorkbenchShowAllGrips;
            Line($"settings: saved on -> {on}, saved off -> {off}, old file -> {old}; default {new Settings().WorkbenchShowAllGrips}");
            if (!on || off || old || new Settings().WorkbenchShowAllGrips)
                Fail("workbench grips: 'Show all grips' should save to settings.txt and default off (old files too)");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }

        // ---- No per-frame allocation ------------------------------------------------------------------------------
        var frame = new BenchGrips();
        long best = long.MaxValue;
        var rebuilt = 0;
        for (var round = 0; round < 3; round++)
        {
            frame.Refresh(stock, Parts.All, false, true);
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 600; f++)
                if (frame.Refresh(stock, Parts.All, false, true))
                    rebuilt++;
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - start);
        }
        Line($"grip list refresh with unchanged inputs x600: {best} bytes allocated (best of 3), {rebuilt} rebuilds");
        if (best > 0 || rebuilt > 0)
            Fail($"workbench grips: refreshing the grip list with unchanged inputs allocated {best} bytes / rebuilt {rebuilt} times (expected none)");
    }
}
