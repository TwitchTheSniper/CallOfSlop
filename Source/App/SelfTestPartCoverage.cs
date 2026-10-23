namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The part set covers every option (the user, 2026-10-23; docs/zombies/WEAPON-BUILDER.md, "Universal ammo"): for
    /// every bore at least 3 per-receiver feeds fit it, 2 of them common or uncommon (so a run can feed any receiver it
    /// finds); every ammo part fits every receiver (ammo is universal) and the Ammo category has at least 3 common or
    /// uncommon rounds; and every part category has at least 3 parts. Also that a receiver of every bore really
    /// assembles with each of its feeds and every ammo, and the ammo rules give sane numbers on every calibre.
    /// </summary>
    private static void CheckPartCoverage()
    {
        var reg = Parts.Registry;
        var feeds = Parts.InCategory(PartCategory.Feed).Where(f => f.Mount == "feed" && f.Shared == null).ToList();
        var ammo = Parts.InCategory(PartCategory.Ammo).ToList();
        var receivers = Parts.InCategory(PartCategory.Receiver).ToList();

        // ---- Feeds per bore: 3 or more, 2 of them common or uncommon --------------------------------------------
        foreach (var bore in reg.Bores)
        {
            var fit = feeds.Where(f => f.FitsBore(bore)).OrderBy(f => f.Rarity).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
            var cheap = fit.Count(f => f.Rarity <= Rarity.Uncommon);
            Line($"feeds for {bore}: {fit.Count} ({cheap} common/uncommon): {string.Join(", ", fit.Select(f => $"{f.Name} ({PartDef.RarityName(f.Rarity).ToLowerInvariant()}, {MagOf(f)})"))}");
            if (fit.Count < 3 || cheap < 2)
                Fail($"part coverage: the {bore} bore has {fit.Count} feeds ({cheap} common/uncommon); every bore needs 3 or more, 2 of them common or uncommon");
            if (!receivers.Any(r => r.Receiver!.Bore == bore))
                Fail($"part coverage: no receiver fires {bore} rounds");
        }

        // ---- Ammo: universal, with a spread of tiers --------------------------------------------------------------
        var bored = ammo.Where(a => a.Bores.Length > 0).ToList();
        var cheapAmmo = ammo.Count(a => a.Rarity <= Rarity.Uncommon);
        Line($"ammo: {ammo.Count} round types, {cheapAmmo} common/uncommon, by tier {string.Join(", ", ammo.GroupBy(a => a.Rarity).OrderBy(g => g.Key).Select(g => $"{PartDef.RarityName(g.Key).ToLowerInvariant()} {g.Count()}"))}; bore-bound: {(bored.Count == 0 ? "none" : string.Join(", ", bored.Select(a => a.Name)))}");
        if (bored.Count > 0)
            Fail($"part coverage: ammo fits every receiver, but these name bores: {string.Join(", ", bored.Select(a => a.Name))}");
        if (cheapAmmo < 3 || ammo.Count < 3)
            Fail($"part coverage: the Ammo category needs 3 or more common/uncommon round types (has {cheapAmmo} of {ammo.Count})");

        // ---- Every category: 3 or more parts --------------------------------------------------------------------
        var thin = Enum.GetValues<PartCategory>().Select(c => (Cat: c, Count: Parts.InCategory(c).Count())).Where(x => x.Count < 3).ToList();
        Line($"categories: {string.Join(", ", Enum.GetValues<PartCategory>().Select(c => $"{PartDef.CategoryName(c)} {Parts.InCategory(c).Count()}"))}");
        if (thin.Count > 0)
            Fail($"part coverage: every part category needs 3 or more parts: {string.Join(", ", thin.Select(x => $"{PartDef.CategoryName(x.Cat)} {x.Count}"))}");

        // ---- One receiver of every bore takes each of its feeds and every ammo, and the numbers make sense -------
        var stdBarrel = Parts.Find("Standard Barrel");
        var grip = Parts.Find("Rifle Grip");
        if (stdBarrel == null || grip == null)
        {
            Fail("part coverage: needs the Standard Barrel and the Rifle Grip");
            return;
        }
        var problems = new List<string>();
        var lines = new List<string>();
        foreach (var bore in reg.Bores)
        {
            // The commonest receiver of the bore, with its stock action (an auto one where a belt needs it).
            var r = receivers.Where(x => x.Receiver!.Bore == bore).OrderBy(x => x.Rarity).ThenBy(x => x.Name, StringComparer.Ordinal).First();
            var action = ActionMigration.StockActionOf(r, Parts.Find);
            var cal = reg.ClassOf(bore).Calibre;
            foreach (var f in feeds.Where(f => f.FitsBore(bore)))
            {
                var b = new WeaponBuild("Coverage", grip);
                var rn = b.Root!.Attach("receiver_1", r);
                rn.Attach("barrel", stdBarrel);
                rn.Attach("feed", f);
                rn.Attach("ammo", ammo[0]);
                // A belt needs an automatic action: give it one that the feed accepts.
                var act = f.RequiresAction.Length > 0 && (action == null || !f.RequiresAction.Contains(action.Action!.Kind))
                    ? Parts.InCategory(PartCategory.Action).Where(a => f.RequiresAction.Contains(a.Action!.Kind) && a.Action.RpmPerBarrel == 0).OrderBy(a => a.Rarity).First()
                    : action;
                if (act != null)
                    rn.Attach("action", act);
                var probs = BuildRules.Check(b);
                if (probs.Count > 0)
                    problems.Add($"{f.Name} on the {r.Name}: {string.Join("; ", probs.Select(p => p.Message))}");
            }
            foreach (var a in ammo)
            {
                var b = new WeaponBuild("Coverage", grip);
                var rn = b.Root!.Attach("receiver_1", r);
                rn.Attach("barrel", stdBarrel);
                rn.Attach("feed", feeds.First(f => f.FitsBore(bore) && f.RequiresAction.Length == 0));
                rn.Attach("ammo", a);
                if (action != null)
                    rn.Attach("action", action);
                var probs = BuildRules.Check(b);
                if (probs.Count > 0)
                {
                    problems.Add($"{a.Name} on the {r.Name}: {string.Join("; ", probs.Select(p => p.Message))}");
                    continue;
                }
                var def = WeaponBuilder.Assemble(b).Def;
                if (def == null)
                {
                    problems.Add($"{a.Name} on the {r.Name}: no weapon");
                    continue;
                }
                var perPull = def.Damage * def.Pellets;
                var basePull = cal.Damage * cal.Pellets;
                lines.Add($"{bore}/{a.Name}: {def.Damage}x{def.Pellets} = {perPull} per shot, {def.RangeM:0} m");
                // A round type changes the calibre's numbers by a sensible ratio: damage within x0.5 to x1.6 of what the bore fires plain, range x0.5 to x3
                // (a split round keeps the total within rounding), and never below 1 damage a projectile.
                var ratio = perPull / (float)basePull;
                if (ratio < 0.5f || ratio > 1.6f || def.Damage < 1 || def.Pellets < 1 || def.Pellets > 30 || def.RangeM < cal.RangeM * 0.5f || def.RangeM > cal.RangeM * 3f)
                    problems.Add($"{a.Name} on the {r.Name}: {perPull} per shot ({def.Damage}x{def.Pellets}) and {def.RangeM:0} m against the {bore} calibre's {basePull} and {cal.RangeM:0} m");
                if (a.Round is { Pellets: > 0 } rd && def.Pellets != rd.Pellets && !(reg.ClassOf(bore).Pellets && a.Effects.Any(e => e.Field.Name == nameof(WeaponDef.Pellets))))
                    problems.Add($"{a.Name} on the {r.Name}: {def.Pellets} pellets, its round block says {rd.Pellets}");
            }
        }
        Line($"assembled: {reg.Bores.Length} bores x every feed and ammo; {string.Join("; ", lines.Where(l => l.Contains("Buckshot") || l.Contains("Slug") || l.Contains("FMJ")))}");
        if (problems.Count > 0)
            Fail($"part coverage: {string.Join(" | ", problems)}");

        // ---- Old saves name the calibre their receiver fires: they load as the round type with the same numbers -------
        var dropped = new List<string>();
        var oldText = "Old Cannon|volley|Pistol Grip{receiver_1:Kingpin Revolver Receiver{barrel:Long Barrel,feed:Heavy Box,ammo:.50 BMG,action:Revolver Hammer},"
                      + "receiver_2:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball,action:Semi-Auto Trigger},"
                      + "receiver_3:Farmhand Pump Receiver{barrel:Scattermouth Barrel,feed:Shell Tube,ammo:00 Buckshot,action:Pump Slide}}";
        var old = WeaponBuild.Parse(oldText, Parts.Find, dropped);
        var oldDef = BuildRules.IsComplete(old) ? WeaponBuilder.Assemble(old).Def : null;
        var names = string.Join(", ", old.Receivers.Select(r => r.Child("ammo")?.Part.Name ?? "none"));
        Line($"old save: ammo loaded as {names}; dropped {dropped.Count}; {(oldDef == null ? "incomplete" : $"{oldDef.Damage} dmg, flags {string.Join("/", oldDef.BuildFlags)}")}");
        if (dropped.Count > 0 || oldDef == null || names != "Armour Piercing, FMJ, Buckshot" || !oldDef.BuildFlags.Contains("penetrating"))
            Fail($"part coverage: a build saved with the calibre-named ammo should load with the round types (got {names}; dropped: {string.Join("; ", dropped)})");

        static string MagOf(PartDef f) => f.Effects.FirstOrDefault(e => e.Field.Name == nameof(WeaponDef.Mag) && e.Op == "set")?.Value is int m ? $"{m} rounds" : "?";
    }
}
