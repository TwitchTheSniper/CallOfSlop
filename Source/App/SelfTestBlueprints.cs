namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The workbench blueprint library (BlueprintStore, BlueprintFit; docs/zombies/WEAPON-BUILDER.md, "Blueprints"), in a
    /// temp save folder only: the first open copies workbench.txt's slots, melee and grenade in once (not again after a
    /// restart); add, rename, duplicate, save over and delete round-trip through blueprints.txt and survive a restart;
    /// guns, melee weapons and grenades keep their kind; a removed part loads dropped and a garbage line is skipped; a
    /// Zombies load fills only owned parts (counts included), leaves the rest as gaps (red rings on exactly those empty
    /// sockets, a list) that clear as filled and go when another build loads, and refuses a blueprint whose grip you lack;
    /// reading the marks allocates nothing.
    /// </summary>
    private static void CheckWorkbenchBlueprints()
    {
        const string rifle = "Service Rifle Grip{receiver_1:Workhorse Receiver{action:Full-Auto Sear,barrel:Standard Barrel{muzzle:Flash Hider},feed:Box Mag,ammo:FMJ},optic:Red Dot}";
        const string melee = "Long Haft{head:Sledgehammer Head}";
        const string grenade = "Pineapple Casing{fuse:Short Fuse,payload:Black Powder}";
        var oldFolder = SavePaths.Folder;
        var oldMode = SavePaths.Mode;
        var temp = Path.Combine(Path.GetTempPath(), $"CallOfSlop-blueprints-{Guid.NewGuid():N}");
        var slotsBackup = BuildStore.Lines();
        var (selected, second, extra, dummies) = (BuildStore.Selected, BuildStore.Second, BuildStore.ExtraSlotsUnlocked, BuildStore.Dummies);
        var ro = BlueprintStore.ReadOnly;
        try
        {
            SavePaths.UseFolder(temp, "self-test");
            BlueprintStore.ReadOnly = false;
            var path = SavePaths.PathOf(BlueprintStore.FileName);
            if (!SavePaths.SaveFiles.Contains(BlueprintStore.FileName))
                Fail("blueprints: blueprints.txt isn't in SavePaths.SaveFiles");

            // ---- Migration: the old slots come in once --------------------------------------------------------------
            BuildStore.Load([$"slot1=Old Rifle|volley|{rifle}", $"slot3=Old Rifle|volley|{rifle}", $"melee=Old Hammer|volley|{melee}", $"grenade=Old Pineapple|volley|{grenade}"], Parts.Find, out var d0);
            BlueprintStore.ResetForTest();
            BlueprintStore.LoadFolder(migrateSlots: true);
            var names = string.Join(", ", BlueprintStore.All.Select(b => $"{b.Name} ({b.Kind})"));
            Line($"migrated from workbench.txt: {names}; file written {File.Exists(path)}");
            if (d0.Count > 0 || BlueprintStore.All.Count != 4 || names != "Old Rifle (Gun), Old Rifle 2 (Gun), Old Hammer (Melee), Old Pineapple (Grenade)" || !File.Exists(path))
                Fail("blueprints: the first open should copy the 2 slot builds, the melee and the grenade into the library (unique names) and save it");
            BlueprintStore.ResetForTest();
            BlueprintStore.LoadFolder(migrateSlots: true); // a restart: the file is there, nothing copied again
            Line($"after a restart: {BlueprintStore.All.Count} blueprints");
            if (BlueprintStore.All.Count != 4)
                Fail("blueprints: the slots must be copied in only once (the library had them again after a restart)");

            // ---- Editing round-trips and survives a restart ----------------------------------------------------------
            var added = BlueprintStore.Add(BuildOf(rifle, "Old Rifle"), out var why);
            BlueprintStore.Rename(0, "Main|Gun{x}");
            var dup = BlueprintStore.Duplicate(0);
            BlueprintStore.Delete(1); // the duplicate, right after the original
            BlueprintStore.Overwrite(BlueprintStore.All.FindIndex(b => b.Kind == BuildKind.Melee), BuildOf("Long Haft{head:Fire Axe Head}", "ignored"));
            var empty = BlueprintStore.Add(new WeaponBuild(), out var whyEmpty);
            var want = BlueprintStore.All.Select(b => b.ToText()).ToList();
            BlueprintStore.ResetForTest();
            BlueprintStore.LoadFolder(migrateSlots: false);
            var got = BlueprintStore.All.Select(b => b.ToText()).ToList();
            Line($"edited: added at {added} ({why ?? "ok"}), duplicate at {dup}, empty build refused ({empty}: {whyEmpty}); after a restart: {string.Join(" / ", BlueprintStore.All.Select(b => b.Name))}");
            if (added < 0 || dup != 1 || empty != -1 || !got.SequenceEqual(want) || got.Count != 5)
                Fail("blueprints: add / rename / duplicate / delete / save over should round-trip through blueprints.txt");
            if (BlueprintStore.All[0].Name != "MainGunx" || BlueprintStore.All[1].Name != "Old Rifle 2" || BlueprintStore.All[^1].Name != "Old Rifle 3")
                Fail("blueprints: names should be cleaned and kept unique (Name 2, Name 3...)");
            if (BlueprintStore.All.FirstOrDefault(b => b.Kind == BuildKind.Melee) is not { Name: "Old Hammer" } hammer || hammer.Root!.Child("head")?.Part.Name != "Fire Axe Head"
                || !BlueprintStore.All.Any(b => b.Kind == BuildKind.Grenade && b.Root!.Child("fuse")?.Part.Name == "Short Fuse"))
                Fail("blueprints: melee and grenade blueprints should keep their kind and parts (save over keeps the name)");

            // ---- Removed parts are dropped, garbage skipped --------------------------------------------------------
            BlueprintStore.Load(["bp=Gone|volley|Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Gold Plated Unobtainium Barrel},optic:Red Dot}", "bp=garbage", "bp=Nothing|volley|No Such Grip", $"bp=Fine|volley|{rifle}"], Parts.Find, out var dropped);
            Line($"removed parts: {BlueprintStore.All.Count} loaded ({string.Join(", ", BlueprintStore.All.Select(b => $"{b.Name} {b.PartCount} parts"))}); dropped: {string.Join("; ", dropped)}");
            if (BlueprintStore.All.Count != 2 || BlueprintStore.All[0].At("receiver_1")?.Child("barrel") != null || BlueprintStore.All[0].Root!.Child("optic") == null || dropped.Count < 3)
                Fail("blueprints: a part that no longer exists should load dropped (the rest kept), a garbage line skipped");

            // ---- A Zombies load: only the parts you own --------------------------------------------------------------
            var bp = BuildOf("Sport Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Y-Splitter{barrel_1:Scattermouth Barrel,barrel_2:Scattermouth Barrel},feed:Shell Tube,ammo:Buckshot},optic:Red Dot}", "Plan");
            ActionMigration.Fill(bp, Parts.Find);
            PartDef P(string n) => Parts.Find(n) ?? throw new InvalidOperationException(n);
            var owned = bp.Nodes.Select(n => n.Part).Where(p => p.Name is not ("Red Dot" or "Scattermouth Barrel")).GroupBy(p => p).Select(g => (g.Key, g.Count())).Append((P("Scattermouth Barrel"), 1)).ToList();
            var stock = BenchStock.Of(owned);
            var gaps = new List<BlueprintGap>();
            var fit = BlueprintFit.Fit(bp, stock, gaps);
            var over = fit == null ? -1 : MissingReport.Of(fit, stock).Total;
            Line($"zombies load: {bp.PartCount} parts in the blueprint, {fit?.PartCount} filled, {over} beyond your parts; gaps: {string.Join(", ", gaps.Select(g => $"{g.Part.Name} at {(g.ParentPath.Length == 0 ? "grip" : g.ParentPath)}/{g.Socket}"))}");
            if (fit == null || over != 0 || fit.PartCount != bp.PartCount - 2 || gaps.Count != 2
                || !gaps.Any(g => g.Part.Name == "Red Dot" && g.ParentPath == "" && g.Socket == "optic")
                || !gaps.Any(g => g.Part.Name == "Scattermouth Barrel" && g.Socket == "barrel_2"))
                Fail("blueprints: a Zombies load should fill only the parts you own (one Scattermouth for two sockets) and list the rest");

            // A missing part takes what's on it with it.
            var noRx = BenchStock.Of(bp.Nodes.Select(n => n.Part).Where(p => p.Category != PartCategory.Receiver).Distinct().Select(p => (p, 4)));
            var fit2 = BlueprintFit.Fit(bp, noRx, gaps);
            Line($"no receiver owned: {fit2?.PartCount} parts filled, gaps {string.Join(", ", gaps.Select(g => $"{g.Part.Name} (+{g.Under})"))}");
            if (fit2 == null || fit2.PartCount != 2 || gaps.Count != 1 || gaps[0].Under != bp.Root!.Child("receiver_1")!.Walk().Count() - 1)
                Fail("blueprints: a missing receiver leaves its socket empty and the parts on it off");
            var noGrip = BlueprintFit.Fit(bp, BenchStock.Of([(P("Red Dot"), 1)]), gaps);
            if (noGrip != null)
                Fail("blueprints: a blueprint whose grip you don't own can't be loaded in a run");

            // The red rings: exactly the gap sockets, cleared as filled, gone with another build.
            BlueprintFit.Fit(bp, stock, gaps);
            var bm = new BenchModel(fit!, stock);
            bm.SetGaps(gaps);
            string Rings() => string.Join(", ", bm.Spots.Where(s => s.Gap != null).Select(s => $"{(s.ParentPath.Length == 0 ? "grip" : s.ParentPath)}/{s.Socket.Name}"));
            var before = Rings();
            var free = new BenchModel(fit!.Clone(), BenchStock.Unlimited); // fill one with an unlimited stock to clear a ring
            free.SetGaps(gaps);
            var placeWhy = free.Place(free.SpotOf("", "optic"), P("Red Dot"), out _);
            var afterFill = string.Join(", ", free.Spots.Where(s => s.Gap != null).Select(s => s.Socket.Name));
            free.Undo();
            var afterUndo = free.Gaps.Count;
            Line($"rings: {before}; after fitting the optic ({placeWhy ?? "ok"}): {afterFill}; after undo: {afterUndo} gap(s)");
            if (bm.Gaps.Count != 2 || bm.Spots.Count(s => s.Gap != null) != 2 || placeWhy != null || afterFill != "barrel_2" || afterUndo != 1)
                Fail("blueprints: the red rings should be on exactly the missing sockets and each clear for good once filled");
            long best = long.MaxValue;
            for (var round = 0; round < 3; round++)
            {
                var start = GC.GetAllocatedBytesForCurrentThread();
                var n = 0;
                for (var f = 0; f < 600; f++)
                    for (var i = 0; i < bm.Spots.Count; i++)
                        if (bm.Spots[i].Gap != null)
                            n++;
                n += bm.Gaps.Count;
                best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - start);
            }
            if (best > 0)
                Fail($"blueprints: reading the gap marks allocated {best} bytes (expected none)");
            bm.SetBuild(BuildOf(rifle));
            if (bm.Gaps.Count != 0 || bm.Spots.Any(s => s.Gap != null))
                Fail("blueprints: loading another build should clear the blueprint's marks");
            // The sandbox loads the whole build.
            var whole = BlueprintFit.Fit(bp, BenchStock.Unlimited, gaps);
            if (whole?.ToText() != bp.ToText() || gaps.Count != 0)
                Fail("blueprints: in the sandbox a blueprint loads whole");
        }
        finally
        {
            BlueprintStore.ReadOnly = ro;
            BlueprintStore.ResetForTest();
            BuildStore.Load(slotsBackup, Parts.Find, out _);
            (BuildStore.Selected, BuildStore.Second, BuildStore.ExtraSlotsUnlocked, BuildStore.Dummies) = (selected, second, extra, dummies);
            SavePaths.UseFolder(oldFolder, oldMode);
            try
            {
                Directory.Delete(temp, true); // our own temp folder
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
