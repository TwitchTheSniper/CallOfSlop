namespace CallOfSlop;

/// <summary>
/// Self-test section "Sniper scopes": every sniper starts with a magnified scope (default kit, a fresh profile, bots,
/// Gun Game), its iron sights (the empty optic slot; the slot is full, so they aren't a 16th optic) stay locked until
/// half its attachment XP and are selectable after, every sniper model has iron sights with a clear sight line, other
/// classes are untouched, and loadout.txt saves and migrates kits (older "no optic" sniper kits get the scope).
/// </summary>
public static partial class SelfTest
{
    /// <summary>The scope each shipped sniper names in its file ("defaultOptic").</summary>
    private static readonly (string Gun, string Scope)[] ShippedSniperScopes =
    [
        ("Needle Bolt-Action", "8x Sniper Scope"), ("Leviathan .50", "8x Sniper Scope"), ("Kite Semi-Sniper", "4x Scope"),
        ("Ghost SD", "4x Scope"), ("Railspike", "8x Sniper Scope"),
    ];

    private static void CheckSniperScopes()
    {
        var snipers = Weapons.All.Where(w => w.Class == WeaponClass.Sniper).ToArray();
        if (snipers.Length == 0)
        {
            Fail("no sniper rifles loaded");
            return;
        }
        var fresh = new Progress { ReadOnly = true };
        foreach (var (gun, scope) in ShippedSniperScopes)
            if (Weapons.Find(gun) is { } w && Attachments.DefaultOptic(w)?.Name != scope)
                Fail($"{gun}: starts with {Attachments.DefaultOptic(w)?.Name ?? "iron sights"} (its file names the {scope})");

        // ---- Every sniper starts with a magnified scope; nothing else changes ----------------------------------------
        foreach (var w in snipers)
        {
            var scope = Attachments.DefaultOptic(w);
            if (scope == null)
            {
                Fail($"{w.Name}: no default scope");
                continue;
            }
            var kit = Attachments.DefaultKit(w);
            var d = Attachments.Apply(w, kit);
            var ironsXp = Attachments.IronSightsXp(w);
            var max = Attachments.MaxXp(w);
            Line($"  {w.Name}: starts with the {scope.Name} ({d.AdsZoom:0.#}x, {d.AdsTime:0.00} s to aim, mobility {d.Mobility:0.00}); "
                 + $"{Attachments.NoOpticName(w).ToLowerInvariant()} {w.AdsZoom:0.#}x ({w.AdsTime:0.00} s, mobility {w.Mobility:0.00}) unlock at {ironsXp:N0} of {max:N0} weapon XP");
            if (scope.Slot != AttachSlot.Optic || !scope.Fits(w) || !Sights.Magnified(d) || !GunModel.FullScope(d) || d.AdsZoom <= w.AdsZoom)
                Fail($"{w.Name}: the {scope.Name} isn't a magnified full scope ({d.Optic}, {d.AdsZoom}x)");
            if (Attachments.UnlockXp(w, scope) != 0)
                Fail($"{w.Name}: its starting {scope.Name} needs {Attachments.UnlockXp(w, scope)} weapon XP");
            if (Attachments.Sanitize(w, 0, fresh) != kit || Attachments.Sanitize(w, kit, fresh) != kit)
                Fail($"{w.Name}: a fresh profile gets [{Attachments.Describe(Attachments.Sanitize(w, 0, fresh))}], not the default scope");
            if (Attachments.Sanitize(w, 0) != 0)
                Fail($"{w.Name}: Sanitize without progress (the server) changed an empty optic slot");
            if (ironsXp < max * 0.3f || ironsXp > max * 0.7f || ironsXp % 50 != 0)
                Fail($"{w.Name}: iron sights unlock at {ironsXp} of {max} weapon XP (expected about half, a multiple of 50)");

            // Locked until the unlock XP, then selectable; other attachments still need their own XP.
            var before = Progress.Parse([$"wx.{w.Name}={ironsXp - 50}"]);
            var after = Progress.Parse([$"wx.{w.Name}={ironsXp}"]);
            if (Attachments.IronSightsUnlocked(w, ironsXp - 50) || !Attachments.IronSightsUnlocked(w, ironsXp))
                Fail($"{w.Name}: IronSightsUnlocked is wrong around {ironsXp} weapon XP");
            if (Attachments.Sanitize(w, 0, before) != kit)
                Fail($"{w.Name}: iron sights usable at {ironsXp - 50} weapon XP");
            if (Attachments.Sanitize(w, 0, after) != 0)
                Fail($"{w.Name}: iron sights not selectable at {ironsXp} weapon XP (got [{Attachments.Describe(Attachments.Sanitize(w, 0, after))}])");
            var other = Attachments.For(w).FirstOrDefault(a => a.Slot == AttachSlot.Optic && a != scope && Attachments.UnlockXp(w, a) <= ironsXp - 50);
            if (other != null && Attachments.Slot(Attachments.Sanitize(w, Attachments.With(0, AttachSlot.Optic, other.Index), before), AttachSlot.Optic) != other.Index)
                Fail($"{w.Name}: an unlocked {other.Name} was swapped for the default scope");

            // Bots always aim through the scope (and their random rolls are the same as before: it takes none).
            var rng = new Random(77);
            for (var i = 0; i < 40; i++)
                if (Attachments.Slot(Game.BotKit(w, rng), AttachSlot.Optic) != scope.Index)
                {
                    Fail($"{w.Name}: a bot rolled a kit without its scope");
                    break;
                }
        }
        foreach (var w in Weapons.All.Where(x => x.Class != WeaponClass.Sniper && x.DefaultOptic == null))
        {
            if (Attachments.StartsScoped(w) || Attachments.DefaultKit(w) != 0 || Attachments.IronSightsXp(w) != 0)
                Fail($"{w.Name} ({w.ClassName}): starts with an optic, but only snipers should");
            if (Attachments.Sanitize(w, 0, fresh) != 0)
                Fail($"{w.Name}: a fresh profile's empty kit changed");
            if (Attachments.Slot(Game.BotKit(w, new Random(5)), AttachSlot.Optic) != 0)
                Fail($"{w.Name}: a bot fitted an optic");
        }

        // ---- Iron sights on every sniper model: posts on the sight line, nothing in the way ---------------------------
        foreach (var w in Weapons.All.Where(Attachments.StartsScoped))
        {
            if (w.Model == null || GunModel.HasBuiltInScope(w))
                continue; // a per-class model or a built-in scope: no irons to check
            if (Sights.HasLens(w) || Sights.Magnified(w))
                Fail($"{w.Name}: iron sights have a lens");
            var sight = GunModel.SightHeight(w);
            var eyeZ = -GunModel.AdsZ(w);
            var parts = GunModel.RecordParts(w, 0f);
            var posts = parts.Where(p => !p.Optic && MathF.Abs(p.Max.Y - sight) < 0.002f && p.Min.X < 0.004f && p.Max.X > -0.004f).ToArray();
            var spanZ = posts.Length == 0 ? 0f : posts.Max(p => (p.Min.Z + p.Max.Z) / 2) - posts.Min(p => (p.Min.Z + p.Max.Z) / 2);
            if (posts.Length < 2 || spanZ < 0.15f)
            {
                Fail($"{w.Name}: no front and rear sight on the sight line at {sight:0.000} m ({posts.Length} parts reach it, {spanZ * 100:0} cm apart)");
                continue;
            }
            var front = posts.Min(p => p.Min.Z);
            var block = parts.FirstOrDefault(p => p.Max.Y > sight + 0.0005f && p.Min.X < 0.003f && p.Max.X > -0.003f && p.Max.Z > front && p.Min.Z < eyeZ);
            if (block != default)
                Fail($"{w.Name}: a part (y {block.Min.Y:0.000}..{block.Max.Y:0.000}, z {block.Min.Z:0.000}..{block.Max.Z:0.000}) rises over the iron sight line at {sight:0.000} m");
            // With the starting scope fitted the irons come off.
            var scoped = GunModel.RecordParts(Attachments.Apply(w, Attachments.DefaultKit(w)), 0f);
            if (scoped.Count(p => !p.Optic) >= parts.Count)
                Fail($"{w.Name}: the iron sights stay on with the {Attachments.DefaultOptic(w)!.Name}");
            Line($"  {w.Name} irons: sight line {sight * 100:0.0} cm over the grip, sights {spanZ * 100:0} cm apart, {w.AdsZoom:0.#}x");
        }

        // ---- Gun Game hands a sniper rung its scope ------------------------------------------------------------------
        var probe = new Player { Id = 200, Name = "GunGameProbe" };
        var level = Enumerable.Range(0, GunGame.Final + 1).FirstOrDefault(i => GunGame.At(i).Class == WeaponClass.Sniper, -1);
        if (level >= 0)
        {
            probe.GunLevel = (byte)level;
            new GunGameModule().OnSpawn(null!, probe);
            if (probe.PrimaryKit != Attachments.DefaultKit(GunGame.At(level)))
                Fail($"Gun Game: the {GunGame.At(level).Name} rung came with [{Attachments.Describe(probe.PrimaryKit)}]");
        }

        CheckSniperScopeSaves(snipers);
    }

    /// <summary>loadout.txt: iron sights save as "Iron Sights", older sniper kits without an optic get the scope, a temp file round-trips.</summary>
    private static void CheckSniperScopeSaves(WeaponDef[] snipers)
    {
        if (snipers.Length < 2)
            return;
        var a = snipers[0];
        var b = snipers[1];
        var rifle = Weapons.Find("Vanguard AR") ?? Weapons.DefaultPrimary;
        var barrel = Attachments.For(a).FirstOrDefault(x => x.Slot == AttachSlot.Barrel);
        var backup = LoadoutStore.SaveLines().ToArray();
        var file = Path.Combine(Path.GetTempPath(), $"cos_sniper_scopes_{Environment.ProcessId}.txt");
        try
        {
            // An older file: a sniper with no optic (it looked through the built-in scope), one with no kit line at all,
            // a rifle with an empty kit.
            File.WriteAllLines(file, new[]
            {
                "selected=0",
                $"loadout1.primary={a.Name}", $"loadout1.primary.kit={barrel?.Name}",
                $"loadout2.primary={b.Name}",
                $"loadout3.primary={rifle.Name}", "loadout3.primary.kit=",
                $"kit.{a.Name}={barrel?.Name}",
            });
            LoadoutStore.LoadLines(File.ReadAllLines(file));
            var wantA = Attachments.With(Attachments.DefaultKit(a), AttachSlot.Barrel, barrel?.Index ?? 0);
            var s = LoadoutStore.Slots;
            Line($"  old loadout.txt: {a.Name} [{Attachments.Describe(s[0].PrimaryKit)}], {b.Name} [{Attachments.Describe(s[1].PrimaryKit)}], {rifle.Name} [{Attachments.Describe(s[2].PrimaryKit)}]");
            if (s[0].PrimaryKit != wantA || LoadoutStore.KitMemory(a.Id) != wantA)
                Fail($"old save: {a.Name} with no optic loaded as [{Attachments.Describe(s[0].PrimaryKit)}] (memory [{Attachments.Describe(LoadoutStore.KitMemory(a.Id))}]), expected its scope");
            if (s[1].PrimaryKit != Attachments.DefaultKit(b))
                Fail($"old save: {b.Name} without a kit line loaded as [{Attachments.Describe(s[1].PrimaryKit)}]");
            if (s[2].PrimaryKit != 0)
                Fail($"old save: the {rifle.Name}'s empty kit became [{Attachments.Describe(s[2].PrimaryKit)}]");

            // Choosing iron sights: saved as "Iron Sights", kept over the gun's remembered build, back after a reload.
            LoadoutStore.SetKit(s[1], 0, Attachments.With(Attachments.DefaultKit(b), AttachSlot.Optic, 0));
            LoadoutStore.SetKit(s[0], 0, wantA);
            s[3].Primary = b.Id;
            s[3].PrimaryKit = Attachments.DefaultKit(b); // the same gun with its scope in another slot (remembered last)
            LoadoutStore.SetKit(s[3], 0, Attachments.DefaultKit(b));
            var saved = LoadoutStore.SaveLines();
            File.WriteAllLines(file, saved);
            if (!saved.Contains($"loadout2.primary.kit={Attachments.IronSightsName}"))
                Fail($"iron sights weren't saved as \"{Attachments.IronSightsName}\": {string.Join(" | ", saved.Where(x => x.StartsWith("loadout2.primary", StringComparison.Ordinal)))}");
            LoadoutStore.LoadLines(File.ReadAllLines(file));
            s = LoadoutStore.Slots;
            if (s[1].PrimaryKit != 0 || s[0].PrimaryKit != wantA || s[3].PrimaryKit != Attachments.DefaultKit(b) || s[2].PrimaryKit != 0)
                Fail($"a loadout with iron sights didn't round-trip: [{Attachments.Describe(s[0].PrimaryKit)}], [{Attachments.Describe(s[1].PrimaryKit)}], "
                     + $"[{Attachments.Describe(s[2].PrimaryKit)}], [{Attachments.Describe(s[3].PrimaryKit)}]");
            var lines = File.ReadAllLines(file);
            if (!LoadoutStore.SaveLines().SequenceEqual(lines))
                Fail("loadout.txt changed on a second save");

            // Spawning: locked iron sights put the scope back; unlocked ones stay.
            var max = Progress.TotalFor(Progress.MaxLevel);
            var low = Progress.Parse([$"xp={max}", $"wx.{b.Name}={Attachments.IronSightsXp(b) - 50}"]);
            var high = Progress.Parse([$"xp={max}", $"wx.{b.Name}={Attachments.IronSightsXp(b)}"]);
            var effLow = LoadoutStore.Effective(s[1], low).PrimaryKit;
            var effHigh = LoadoutStore.Effective(s[1], high).PrimaryKit;
            Line($"  iron sights on the {b.Name}: saved as \"{Attachments.IronSightsName}\"; spawn with [{Attachments.Describe(effLow)}] below {Attachments.IronSightsXp(b):N0} weapon XP, [{Attachments.Describe(effHigh)}] from it");
            if (effLow != Attachments.DefaultKit(b) || effHigh != 0)
                Fail($"spawning with locked / unlocked iron sights gave [{Attachments.Describe(effLow)}] / [{Attachments.Describe(effHigh)}]");
            // A gun put into a slot for the first time comes with its scope.
            LoadoutStore.LoadLines(null);
            var l = LoadoutStore.Slots[0];
            LoadoutStore.SetWeapon(l, 0, a.Id, high);
            if (l.PrimaryKit != Attachments.DefaultKit(a))
                Fail($"a brand-new {a.Name} came with [{Attachments.Describe(l.PrimaryKit)}]");
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            LoadoutStore.LoadLines(backup);
        }
    }
}
