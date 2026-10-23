namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Whole part categories hidden in the workbench inventory (BenchInventory in BenchModel.cs) when nothing in them can
    /// go on the build now: an empty grip hides the categories that hang off a receiver, a grip without a stock socket
    /// hides Stock, fitting a receiver brings Feed, Ammo and Barrel back at once, a chosen category that is hidden falls
    /// back to All, "Show incompatible parts" shows every owned category, and categories you own none of stay hidden.
    /// </summary>
    private static void CheckWorkbenchHideCats()
    {
        PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"workbench test needs the part '{name}'");
        PartCategory[] onReceiver = [PartCategory.Feed, PartCategory.Ammo, PartCategory.Barrel, PartCategory.Splitter];
        string Names(BenchInventory inv) => string.Join(" ", inv.Cats.Select(c => PartDef.CategoryName(c)));
        // Shows() must agree with what the list holds with no chip and no search.
        int Disagree(BenchInventory inv) => Enum.GetValues<PartCategory>().Count(c => inv.Shows(c) != inv.Cats.Contains(c));

        // ---- An empty rifle grip: nothing that needs a receiver ----------------------------------------------------
        var rifle = new BenchModel(BuildOf("Service Rifle Grip"), BenchStock.Unlimited);
        var inv = new BenchInventory();
        inv.Refresh(rifle, Parts.All, -1, "", false, false);
        var hiddenOk = onReceiver.All(c => !inv.Shows(c) && !inv.Cats.Contains(c) && inv.Items.All(p => p.Category != c));
        Line($"empty Service Rifle Grip: chips {Names(inv)}; {Disagree(inv)} disagreements");
        if (!hiddenOk || !inv.Shows(PartCategory.Receiver) || !inv.Shows(PartCategory.Stock) || Disagree(inv) > 0)
            Fail("workbench hide categories: an empty rifle grip should list receivers and stocks but hide feeds, ammo, barrels and splitters");

        // A chosen category that is hidden falls back to All (no empty pane), without rebuilding every frame.
        inv.Refresh(rifle, Parts.All, (int)PartCategory.Feed, "", false, false);
        var fellBack = inv.Filter == -1 && inv.Items.Count > 0 && inv.Items.Any(p => p.Category == PartCategory.Receiver);
        var again = 0;
        for (var f = 0; f < 10; f++)
            if (inv.Refresh(rifle, Parts.All, (int)PartCategory.Feed, "", false, false))
                again++;
        Line($"chip Feed on the empty grip -> filter {inv.Filter}, {inv.Items.Count} listed, {again} rebuilds on unchanged inputs");
        if (!fellBack || again > 0)
            Fail("workbench hide categories: a hidden chosen category should fall back to All with parts listed, and not rebuild every frame");

        // ---- Fitting a receiver brings Feed, Ammo and Barrel back at once -----------------------------------------
        var why = rifle.AutoPlace(P("Workhorse Receiver"), out var placed);
        var rebuilt = inv.Refresh(rifle, Parts.All, (int)PartCategory.Feed, "", false, false);
        var feedList = inv.Filter == (int)PartCategory.Feed && inv.Items.Count > 0 && inv.Items.All(p => p.Category == PartCategory.Feed);
        inv.Refresh(rifle, Parts.All, -1, "", false, false);
        var back = new[] { PartCategory.Feed, PartCategory.Ammo, PartCategory.Barrel }.All(c => inv.Shows(c) && inv.Cats.Contains(c));
        Line($"after fitting the Workhorse Receiver ('{why ?? "ok"}'): rebuilt {rebuilt}, chip Feed keeps {inv.Filter} -> chips {Names(inv)}; {Disagree(inv)} disagreements");
        if (why != null || placed == null || !rebuilt || !feedList || !back || Disagree(inv) > 0)
            Fail("workbench hide categories: fitting a receiver should bring Feed, Ammo and Barrel back right away (the Feed chip listing feeds)");

        // ---- A grip without a stock socket hides Stock --------------------------------------------------------------
        var pistol = new BenchModel(BuildOf("Pistol Grip"), BenchStock.Unlimited);
        inv.Refresh(pistol, Parts.All, -1, "", false, false);
        Line($"empty Pistol Grip: chips {Names(inv)}");
        if (inv.Shows(PartCategory.Stock) || inv.Cats.Contains(PartCategory.Stock) || onReceiver.Any(inv.Shows) || !inv.Shows(PartCategory.Receiver))
            Fail("workbench hide categories: a pistol grip (no stock socket) should hide Stock and the receiver categories");

        // ---- "Show incompatible parts" shows every category ---------------------------------------------------------
        inv.Refresh(pistol, Parts.All, (int)PartCategory.Feed, "", false, true);
        var keptFeed = inv.Filter == (int)PartCategory.Feed && inv.Items.Count > 0 && inv.Items.All(p => p.Category == PartCategory.Feed && !inv.Fits[inv.Items.IndexOf(p)]);
        inv.Refresh(pistol, Parts.All, -1, "", false, true);
        var owned = Parts.All.Where(p => !p.IsRoot && !p.IsMeleePart).Select(p => p.Category).Distinct().ToList();
        var missing = owned.Where(c => !inv.Shows(c) || !inv.Cats.Contains(c)).ToList();
        Line($"toggle on, empty Pistol Grip: chips {Names(inv)} ({owned.Count} gun categories, missing: {string.Join(" ", missing)}); chip Feed kept {keptFeed}");
        if (missing.Count > 0 || !keptFeed || Disagree(inv) > 0)
            Fail("workbench hide categories: with 'Show incompatible parts' on every gun category should be shown, and a Feed chip should list the (dimmed) feeds");

        // ---- Categories you own none of stay hidden, toggle or not ----------------------------------------------
        var stock = BenchStock.FromRun([new("Standard Barrel", 1), new("Box Mag", 1)], BuildOf("Pistol Grip"), Parts.Find);
        var run = new BenchModel(BuildOf("Pistol Grip"), stock);
        inv.Refresh(run, Parts.All, -1, "", false, true);
        var onCats = Names(inv);
        var onOk = inv.Cats.Count == 2 && inv.Shows(PartCategory.Barrel) && inv.Shows(PartCategory.Feed) && !inv.Shows(PartCategory.Receiver);
        inv.Refresh(run, Parts.All, -1, "", false, false);
        Line($"run stock (a barrel and a mag, pistol grip): toggle on chips {onCats}; off chips '{Names(inv)}', {inv.Hidden} hidden, {inv.Items.Count} listed");
        if (!onOk || inv.Cats.Count != 0 || inv.Items.Count != 0 || inv.Hidden != 2)
            Fail("workbench hide categories: categories with no parts owned should stay hidden even with the toggle on; with it off a receiverless build hides both");
    }
}
