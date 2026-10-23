namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The workbench's "Show incompatible parts" checkbox (BenchInventory in BenchModel.cs): with it off only parts that
    /// fit somewhere on the build and have a spare copy are listed, with it on every owned part is (the rest not
    /// draggable, with a short reason: incompatible or "none spare"); category counts follow; the short reasons agree with the full socket rules; the setting saves to
    /// settings.txt (old files: off); refreshing with unchanged inputs allocates nothing.
    /// </summary>
    private static void CheckWorkbenchIncompat()
    {
        PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"workbench test needs the part '{name}'");
        const string tree = "Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel{muzzle:Flash Hider},feed:Box Mag,ammo:5.56 FMJ},stock:Fixed Stock,optic:Red Dot}";

        // ---- Every part, the toggle off and on ----------------------------------------------------------------
        var full = new BenchModel(BuildOf(tree), BenchStock.Unlimited);
        var inv = new BenchInventory();
        inv.Refresh(full, Parts.All, -1, "", false, false);
        int offCount = inv.Items.Count, hidden = inv.Hidden, offTotal = inv.CatCounts.Sum();
        var offOk = inv.Compatible.All(c => c) && inv.Why.All(w => w == null) && offTotal == offCount;
        inv.Refresh(full, Parts.All, -1, "", false, true);
        var onCount = inv.Items.Count;
        var bad = 0;
        for (var i = 0; i < inv.Items.Count; i++)
            if (inv.Fits[i] != (inv.Why[i] == null) || (!inv.Compatible[i] && inv.Fits[i]) || inv.Compatible[i] != full.FitsAnywhere(inv.Items[i]))
                bad++;
        Line($"every part on a rifle: toggle off {offCount} listed ({hidden} hidden, chips sum {offTotal}), on {onCount} listed (chips sum {inv.CatCounts.Sum()}, {inv.Hidden} hidden), {bad} inconsistent rows; labels: {inv.AllLabel}, {string.Join(", ", inv.CatLabels.Take(4))}");
        if (!offOk || hidden == 0 || onCount != offCount + hidden || inv.Hidden != 0 || inv.CatCounts.Sum() != onCount || bad > 0)
            Fail("workbench incompatible: toggle off should list only compatible parts, on should add exactly the hidden ones (counts in the chips)");
        if (inv.AllLabel != $"All {onCount}")
            Fail($"workbench incompatible: the All chip should read 'All {onCount}' (got '{inv.AllLabel}')");
        // A category chip: the list narrows, the chip counts stay the whole (search-filtered) set.
        var barrelCat = (int)PartCategory.Barrel;
        inv.Refresh(full, Parts.All, barrelCat, "", false, false);
        var bi = inv.Cats.IndexOf(PartCategory.Barrel);
        if (inv.Items.Any(p => p.Category != PartCategory.Barrel) || bi < 0 || inv.CatCounts[bi] != inv.Items.Count || inv.CatCounts.Sum() != offCount)
            Fail("workbench incompatible: a category chip should list just that category, its count matching, the other counts kept");

        // ---- A sample Zombies stock -----------------------------------------------------------------------------
        var std = P("Standard Barrel");
        var scatter = P("Scattermouth Barrel");
        var stock = BenchStock.FromRun([new("Standard Barrel", 1), new("Scattermouth Barrel", 2)], BuildOf(tree), Parts.Find);
        var m = new BenchModel(BuildOf(tree), stock);
        var sample = new BenchInventory();
        sample.Refresh(m, Parts.All, -1, "", false, false);
        var offNames = string.Join(", ", sample.Items.Select(p => p.Name));
        var scatterOff = sample.Items.Contains(scatter);
        var stdIdx = sample.Items.IndexOf(std);
        sample.Refresh(m, Parts.All, -1, "", false, true);
        var si = sample.Items.IndexOf(scatter);
        var why = si >= 0 ? sample.Why[si] : null;
        Line($"sample stock off: {offNames}; on adds: {string.Join(", ", sample.Items.Where((p, i) => !sample.Compatible[i]).Select(p => p.Name))}; Scattermouth reason: '{why}'");
        if (scatterOff || stdIdx < 0 || si < 0 || sample.Fits[si] || why != "needs a shell bore barrel socket")
            Fail("workbench incompatible: the shell Scattermouth should be hidden (off) / dimmed with 'needs a shell bore barrel socket' (on) on a rifle, the Standard Barrel listed");
        if (sample.Items.IndexOf(std) is var sj and >= 0 && (!sample.Fits[sj] || sample.Free[sj] != 1))
            Fail("workbench incompatible: the spare Standard Barrel should be listed as fitting with 1 free");
        // Parts with none spare (every copy on the build): hidden with the toggle off, dimmed "none spare" with it on.
        var hider = P("Flash Hider");
        var hi = sample.Items.IndexOf(hider);
        var hiderWhy = hi >= 0 ? sample.Why[hi] : null;
        var zeroOn = sample.Items.Where((p, i) => sample.Free[i] <= 0).Count();
        var badSpare = 0;
        for (var i = 0; i < sample.Items.Count; i++)
            if (sample.Compatible[i] && sample.Free[i] <= 0 && (sample.Fits[i] || sample.Why[i] != BenchInventory.NoneSpare))
                badSpare++;
        sample.Refresh(m, Parts.All, -1, "", false, false);
        var offHidden = sample.Hidden;
        var expectHidden = Parts.All.Count(p => !p.IsRoot && !p.IsMeleePart && stock.Owned(p) > 0 && (!m.FitsAnywhere(p) || stock.Free(p, m.Build) <= 0));
        Line($"none spare: Flash Hider on the build -> toggle on '{hiderWhy}', {zeroOn} parts with none spare when shown, {badSpare} bad rows; toggle off hides {offHidden} (expected {expectHidden}), lists {sample.Items.Count} all with a spare");
        if (hiderWhy != BenchInventory.NoneSpare || zeroOn == 0 || badSpare > 0 || sample.Items.Contains(hider) || offHidden != expectHidden || sample.Free.Any(f => f <= 0) || sample.Fits.Any(f => !f))
            Fail("workbench incompatible: with the toggle off, parts with none spare should be hidden (counted in the hint); with it on, dimmed with 'none spare'");
        if (sample.AllLabel != $"All {sample.Items.Count}" || sample.CatCounts.Sum() != sample.Items.Count)
            Fail($"workbench incompatible: the chip counts should leave out hidden parts ({sample.AllLabel}, chips sum {sample.CatCounts.Sum()}, {sample.Items.Count} listed)");
        // It can't be dropped anywhere: no valid socket, and double-click (auto-place) refuses with the reason.
        var valid = new bool[64];
        var nValid = m.ValidSpots(scatter, valid);
        var refusal = m.AutoPlace(scatter, out var placed);
        Line($"dropping the Scattermouth: {nValid} valid sockets, auto-place says '{refusal}'");
        if (nValid != 0 || refusal == null || placed != null)
            Fail("workbench incompatible: an incompatible part should have no valid socket and bounce back with a reason");
        // Search with everything matching hidden.
        sample.Refresh(m, Parts.All, -1, "scatter", false, false);
        if (sample.Items.Count != 0 || sample.Hidden != 1)
            Fail($"workbench incompatible: searching 'scatter' with the toggle off should list nothing and count 1 hidden (got {sample.Items.Count}, {sample.Hidden})");
        // A part whose socket type isn't on the build at all.
        var types = m.Spots.Select(s => s.Socket.Type).ToHashSet();
        if (Parts.All.FirstOrDefault(p => !p.IsRoot && !p.IsMeleePart && p.Mount.Length > 0 && !types.Contains(p.Mount)) is { } stray)
        {
            var w = m.WhyNowhereShort(stray);
            Line($"{stray.Name} ({stray.Mount}): '{w}'");
            if (w != $"no {stray.Mount} socket on this build" || m.FitsAnywhere(stray))
                Fail($"workbench incompatible: a part without a socket type on the build should say 'no {stray.Mount} socket on this build' (got '{w}')");
        }

        // ---- Short reasons agree with the full socket rules ------------------------------------------------------
        var disagree = 0;
        var samples = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var model in new[] { full, new BenchModel(BuildOf("Sport Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Y-Splitter{barrel_1:Scattermouth Barrel,barrel_2:Scattermouth Barrel},feed:Shell Tube,ammo:00 Buckshot}}"), BenchStock.Unlimited) })
            foreach (var p in Parts.All)
                foreach (var s in model.Spots)
                {
                    var longWhy = BuildRules.SocketWhy(s.Parent, s.Socket, p);
                    var shortWhy = BuildRules.SocketWhyShort(s.Parent, s.Socket, p);
                    if ((longWhy == null) != (shortWhy == null))
                    {
                        if (disagree++ < 3)
                            Fail($"workbench incompatible: {p.Name} in {s.Parent.Part.Name}/{s.Socket.Name}: full rule '{longWhy}' vs short '{shortWhy}'");
                    }
                    else if (shortWhy != null && samples.Count < 40)
                        samples.Add(shortWhy.StartsWith("needs a", StringComparison.Ordinal) && shortWhy.EndsWith("socket", StringComparison.Ordinal) && shortWhy.Contains("bore") ? "needs a <bore> bore ... socket" : shortWhy);
                }
        Line($"short reasons agree with the socket rules ({disagree} disagreements); e.g. {string.Join("; ", samples.Take(8))}");

        // ---- The setting --------------------------------------------------------------------------------------------
        var path = Path.Combine(Path.GetTempPath(), $"cos_wbincompat_{Environment.ProcessId}.txt");
        try
        {
            new Settings { WorkbenchShowIncompatible = true }.Save(path);
            var on = Settings.Load(path).WorkbenchShowIncompatible;
            new Settings().Save(path);
            var off = Settings.Load(path).WorkbenchShowIncompatible;
            File.WriteAllText(path, "volume=0.5\n"); // an old save without the key
            var old = Settings.Load(path).WorkbenchShowIncompatible;
            Line($"settings: saved on -> {on}, saved off -> {off}, old file -> {old}; default {new Settings().WorkbenchShowIncompatible}");
            if (!on || off || old || new Settings().WorkbenchShowIncompatible)
                Fail("workbench incompatible: 'Show incompatible parts' should save to settings.txt and default off (old files too)");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }

        // ---- No per-frame allocation --------------------------------------------------------------------------------
        var frame = new BenchInventory();
        var search = "barrel";
        long best = long.MaxValue;
        var rebuilt = 0;
        for (var round = 0; round < 3; round++)
        {
            frame.Refresh(m, Parts.All, -1, search, false, true); // warm (the first round builds it)
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 600; f++)
                if (frame.Refresh(m, Parts.All, -1, search, false, true))
                    rebuilt++;
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - start);
        }
        Line($"inventory refresh with unchanged inputs x600: {best} bytes allocated (best of 3), {rebuilt} rebuilds");
        if (best > 0 || rebuilt > 0)
            Fail($"workbench incompatible: refreshing the inventory with unchanged inputs allocated {best} bytes / rebuilt {rebuilt} times (expected none)");
    }
}
