using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Weapon tweaks" (Source/Weapons/WeaponTweaks.cs, WeaponFilePatch.cs, Source/UI/UiWeaponTweaks.cs,
/// Source/Game/GameWeaponTweaks.cs): the stat table covers every number a weapon file holds; a tweak changes the live
/// weapon and its kitted copies in place but not the file weapon, the fingerprint, attachment fits or unlocks; class
/// multipliers stack on every weapon of the class (and on an override); Revert and Reset all put the files' values back;
/// Save writes only the changed values into a copy of the file (in a temp folder), which loads back as exactly the tweaked
/// weapon and passes --check-weapon; a missing key or recoil block is added; the network payload round-trips; a joining
/// client gets the host's set and later changes over loopback; the F7 key; the feel numbers; no allocations closed.
/// </summary>
public static partial class SelfTest
{
    private static void CheckWeaponTweaks()
    {
        var baseline = WeaponTweaks.SnapshotFileDefs();
        var hash0 = WeaponSet.WeaponsHash;
        try
        {
            CheckTweakTable();
            CheckTweakLive(hash0);
            CheckTweakSave(baseline, hash0);
            CheckTweakNet();
            CheckTweakMisc();
        }
        finally
        {
            WeaponTweaks.RestoreFileDefs(baseline);
        }
        if (WeaponSet.WeaponsHash != hash0 || WeaponTweaks.Active || Weapons.All.Any(w => WeaponFile.ToJson(w) != WeaponFile.ToJson(baseline[w.Id])))
            Fail("weapon tweaks: the weapons or the fingerprint didn't come back after the test");
    }

    private static void CheckTweakTable()
    {
        var f = WeaponTweaks.Fields;
        var labels = f.Select(x => x.Label).ToArray();
        var missing = WeaponFile.Ranges.Keys.Where(k => k is not (nameof(WeaponDef.Length) or nameof(WeaponDef.Bulk) or nameof(WeaponDef.Caliber) or nameof(WeaponDef.Bright) or nameof(WeaponDef.Tail)))
            .Where(k => f.All(x => x.Field.Name != k))
            .Concat(Recoil.Fields.Where(r => f.All(x => x.Field != r)).Select(r => r.Name))
            .Concat(LauncherStats.Fields.Where(l => !l.FieldType.IsEnum && f.All(x => x.Field != l)).Select(l => l.Name))
            .ToArray();
        if (missing.Length > 0 || labels.Distinct().Count() != labels.Length || f.All(x => x.Field.Name != nameof(WeaponDef.Mode)))
            Fail($"weapon tweaks: the stat table misses {string.Join(", ", missing)} or has duplicate labels or no fire mode");
        var badRange = f.Where(x => !(x.Min <= x.SoftMin && x.SoftMin < x.SoftMax && x.SoftMax <= x.Max)).Select(x => x.Label).ToArray();
        if (badRange.Length > 0)
            Fail($"weapon tweaks: slider ranges outside the file ranges: {string.Join(", ", badRange)}");
        // Every shipped weapon's values sit inside the ranges (so a slider never clamps a file value on its own).
        var outside = Weapons.All.SelectMany(w => f.Where(x => (!x.LauncherOnly || w.IsLauncher) && x.Sanitize(x.Get(w)) != x.Get(w)).Select(x => $"{w.Name} {x.Label}")).ToArray();
        if (outside.Length > 0)
            Fail($"weapon tweaks: file values that the tweak ranges would change: {string.Join(", ", outside.Take(5))}");
        Line($"stat table: {f.Length} stats ({f.Count(x => x.Block == null)} top level, {f.Count(x => x.Block == "recoil")} recoil, {f.Count(x => x.Block == "launcher")} launcher), {WeaponTweaks.StatField.Length} class multipliers");
    }

    private static TweakField TF(string name) => WeaponTweaks.Fields.First(x => x.Field.Name == name);

    private static void CheckTweakLive(uint hash0)
    {
        var w = Weapons.Find("Vanguard AR") ?? Weapons.All.First(x => x.Class == WeaponClass.AssaultRifle);
        var id = w.Id;
        var damage0 = w.Damage;
        var mag0 = w.Mag;
        // A kitted copy made before the tweak, with an attachment that doesn't touch damage.
        var att = Attachments.For(w).FirstOrDefault(a => a.Effects.All(e => e.Field.Name != nameof(WeaponDef.Damage)));
        var kit = att != null ? Attachments.With(0, att.Slot, att.Index) : 0u;
        var kitted = Attachments.Apply(w, kit);
        var fits0 = Attachments.For(w).Count;
        var unlock0 = att != null ? Attachments.UnlockXp(w, att) : 0;
        var dmg = TF(nameof(WeaponDef.Damage));
        var magF = TF(nameof(WeaponDef.Mag));
        WeaponTweaks.SetOverride(id, dmg, 40);
        var file = WeaponTweaks.FileDef(w); // a copy from the first tweak on
        WeaponTweaks.SetOverride(id, magF, 3); // a magazine under some attachments' minMag: fits must not change
        var liveOk = w.Damage == 40 && w.Mag == 3 && ReferenceEquals(Weapons.Get(id), w) && WeaponTweaks.Active && WeaponTweaks.IsTweaked(id);
        var kitOk = kit == 0 || (ReferenceEquals(Attachments.Apply(w, kit), kitted) && kitted.Damage == 40);
        var fileOk = file.Damage == damage0 && file.Mag == mag0 && !ReferenceEquals(file, w) && WeaponSet.WeaponsHash == hash0;
        var fitsOk = Attachments.For(w).Count == fits0 && (att == null || (Attachments.UnlockXp(w, att) == unlock0 && Attachments.Sanitize(w, kit) == kit));
        Line($"live tweak: {w.Name} damage {damage0} > {w.Damage}, mag {mag0} > {w.Mag}; kitted copy ({(att?.Name ?? "none")}) follows in place {kitOk}; file weapon and fingerprint unchanged {fileOk}; fits and unlocks unchanged {fitsOk}");
        if (!liveOk || !kitOk || !fileOk || !fitsOk)
            Fail("weapon tweaks: a tweak didn't change the live weapon (or its kitted copy), or it changed the file weapon, the fingerprint or the attachment fits");

        // Class multipliers stack on every weapon of the class, and on the override.
        WeaponTweaks.SetClassMult(WeaponClass.AssaultRifle, TweakStat.Damage, 1.5f);
        WeaponTweaks.SetClassMult(WeaponClass.AssaultRifle, TweakStat.FireRate, 0.5f);
        WeaponTweaks.SetClassMult(WeaponClass.AssaultRifle, TweakStat.Mobility, 1.1f);
        var bad = new List<string>();
        foreach (var x in Weapons.All)
        {
            var fx = WeaponTweaks.FileDef(x);
            if (x.Class != WeaponClass.AssaultRifle)
            {
                if (WeaponFile.ToJson(x) != WeaponFile.ToJson(fx))
                    bad.Add($"{x.Name} (another class) changed");
                continue;
            }
            var baseDamage = x.Id == id ? 40 : fx.Damage;
            int wantDamage = (int)Math.Clamp(MathF.Round(baseDamage * 1.5f), 1, 1000), wantRpm = (int)Math.Clamp(MathF.Round(MathF.Max(1, fx.Rpm * 0.5f)), 1, 3000);
            var wantMob = Math.Clamp(fx.Mobility * 1.1f, 0.3f, 1.5f);
            if (x.Damage != wantDamage || x.Rpm != wantRpm || x.Mobility != wantMob || x.Reload != fx.Reload)
                bad.Add($"{x.Name}: damage {x.Damage} (want {wantDamage}), rpm {x.Rpm} (want {wantRpm}), mobility {x.Mobility} (want {wantMob})");
        }
        var arCount = Weapons.All.Count(x => x.Class == WeaponClass.AssaultRifle);
        Line($"class multipliers: damage x1.5, fire rate x0.5, mobility x1.1 on {arCount} assault rifles (one with damage 40 > {w.Damage}); other classes untouched: {(bad.Count == 0 ? "OK" : string.Join("; ", bad.Take(3)))}");
        if (bad.Count > 0)
            Fail($"weapon tweaks: class multipliers don't stack right: {string.Join("; ", bad.Take(3))}");

        // Revert: the weapon's own changes go, the class multipliers stay; Revert class; Reset all.
        WeaponTweaks.RevertWeapon(id);
        var revertOk = w.Damage == (int)MathF.Round(damage0 * 1.5f) && w.Mag == mag0 && WeaponTweaks.OverrideCount == 0 && WeaponTweaks.MultCount == 3;
        WeaponTweaks.RevertClass(WeaponClass.AssaultRifle);
        var classOk = !WeaponTweaks.Active && WeaponFile.ToJson(w) == WeaponFile.ToJson(file) && (kit == 0 || kitted.Damage == damage0);
        WeaponTweaks.SetOverride(id, dmg, 90);
        WeaponTweaks.SetClassMult(WeaponClass.Pistol, TweakStat.Reload, 2f);
        WeaponTweaks.ResetAll();
        var resetOk = !WeaponTweaks.Active && Weapons.All.All(x => WeaponFile.ToJson(x) == WeaponFile.ToJson(WeaponTweaks.FileDef(x))) && WeaponSet.WeaponsHash == hash0;
        Line($"revert weapon (class multiplier stays) {revertOk}, revert class {classOk}, reset all to files {resetOk}");
        if (!revertOk || !classOk || !resetOk)
            Fail("weapon tweaks: Revert, Revert class or Reset all didn't put the files' values back");
    }

    private static void CheckTweakSave(WeaponDef[] baseline, uint hash0)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-tweaks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rifle = Weapons.Find("Vanguard AR") ?? Weapons.All.First(x => x.SourceFile != null && !x.IsLauncher);
            var launcher = Weapons.All.FirstOrDefault(x => x.IsLauncher && x.SourceFile != null);
            var picked = launcher != null ? new[] { rifle, launcher } : [rifle];
            var original = new Dictionary<int, string>();
            foreach (var w in picked)
            {
                var path = Path.Combine(tmp, w.SourceFile!);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                original[w.Id] = File.ReadAllText(Path.Combine(Weapons.Folder, w.SourceFile!));
                File.WriteAllText(path, original[w.Id]);
            }
            var rid = rifle.Id;
            WeaponTweaks.SetOverride(rid, TF(nameof(WeaponDef.Damage)), rifle.Damage + 3);
            WeaponTweaks.SetOverride(rid, TF(nameof(WeaponDef.Reload)), 1.75f);
            WeaponTweaks.SetOverride(rid, TF(nameof(WeaponDef.Mode)), (float)(rifle.Mode == FireMode.Auto ? FireMode.Burst : FireMode.Auto));
            WeaponTweaks.SetOverride(rid, TF(nameof(WeaponDef.RecoilVertical)), rifle.RecoilVertical + 0.25f);
            WeaponTweaks.SetOverride(rid, TF(nameof(WeaponDef.Penetration)), 12); // the shipped files don't give it: added after the key before it
            if (launcher != null)
                WeaponTweaks.SetOverride(launcher.Id, TF(nameof(WeaponDef.LauncherRadius)), Math.Clamp(launcher.LauncherRadius + 1, 0.5f, 20));
            var want = picked.ToDictionary(w => (int)w.Id, w => WeaponFile.ToJson(w)); // the live, tweaked weapons
            var (saved, lines) = WeaponTweaks.Save([tmp]);
            var problems = lines.Where(l => !l.StartsWith("saved ", StringComparison.Ordinal)).ToArray();
            var roundTrip = true;
            var maxChanged = 0;
            var checkOk = true;
            foreach (var w in picked)
            {
                var path = Path.Combine(tmp, w.SourceFile!);
                var text = File.ReadAllText(path);
                var back = WeaponFile.Load(text, w.SourceFile!);
                roundTrip &= WeaponFile.ToJson(back) == want[w.Id];
                // Only the tweaked values' lines change (the recoil block is one line per few keys): no rewrite.
                var before = original[w.Id].Split('\n');
                var after = text.Split('\n');
                var changed = after.Except(before).Count() + before.Except(after).Count();
                maxChanged = Math.Max(maxChanged, changed);
                var console = Console.Out;
                Console.SetOut(TextWriter.Null);
                try
                {
                    checkOk &= WeaponSet.Check(path) == 0;
                }
                finally
                {
                    Console.SetOut(console);
                }
            }
            var baselineMoved = !WeaponTweaks.Active && WeaponFile.ToJson(rifle) == want[rid] && WeaponFile.ToJson(WeaponTweaks.FileDef(rifle)) == want[rid] && WeaponSet.WeaponsHash != hash0;
            var realUntouched = File.ReadAllText(Path.Combine(Weapons.Folder, rifle.SourceFile!)) == original[rid];
            Line($"save: {saved} weapon file(s) in a temp folder, problems {problems.Length}; they load back as the tweaked weapons {roundTrip}; at most {maxChanged} changed line(s) per file; --check-weapon passes {checkOk}; files are the new baseline (fingerprint moved) {baselineMoved}; the real files untouched {realUntouched}");
            if (saved != picked.Length || problems.Length > 0 || !roundTrip || !checkOk || !baselineMoved || !realUntouched || maxChanged > 12)
                Fail($"weapon tweaks: Save didn't round-trip ({string.Join("; ", problems.Take(2))}; {maxChanged} lines changed)");
            WeaponTweaks.RestoreFileDefs(baseline);

            // A file without a recoil block, a mode or penetration: they're added and it still loads as the tweaked weapon.
            const string bare = "{\n  \"format\": \"call-of-slop-weapon/1\",\n  \"name\": \"Tweak Test\",\n  \"class\": \"Pistol\",\n  \"damage\": 20,\n  \"rangeM\": 10,\n"
                                + "  \"rpm\": 300,\n  \"mag\": 10,\n  \"reload\": 1,\n  \"hipSpreadDeg\": 1\n}\n";
            var t = WeaponFile.Load(bare, "bare.json");
            var live = t.Clone();
            TweakField[] changes = [TF(nameof(WeaponDef.RecoilVertical)), TF(nameof(WeaponDef.RecoilKickUp)), TF(nameof(WeaponDef.Mode)), TF(nameof(WeaponDef.Penetration)), TF(nameof(WeaponDef.Damage))];
            live.RecoilVertical = 1.5f;
            live.RecoilKickUp = 4f;
            live.Mode = FireMode.Semi;
            live.Penetration = 30;
            live.Damage = 33;
            var patched = WeaponTweaks.PatchText(bare, "bare.json", live, changes);
            var bareOk = WeaponFile.ToJson(WeaponFile.Load(patched, "bare.json")) == WeaponFile.ToJson(live) && patched.Contains("\"recoil\": {", StringComparison.Ordinal);
            var crlf = WeaponTweaks.PatchText(bare.Replace("\n", "\r\n"), "bare.json", live, changes);
            var crlfOk = !crlf.Replace("\r\n", "").Contains('\n') && WeaponFile.ToJson(WeaponFile.Load(crlf, "bare.json")) == WeaponFile.ToJson(live);
            Line($"patching a bare file: recoil block, mode and penetration added, loads as the tweaked weapon {bareOk}; Windows line endings kept {crlfOk}");
            if (!bareOk || !crlfOk)
                Fail("weapon tweaks: patching a file without the keys (or with CRLF line endings) went wrong");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }

    private static void CheckTweakNet()
    {
        // The payload on its own: written, cleared, read back.
        var w = Weapons.DefaultPrimary;
        var pistol = Weapons.DefaultSecondary;
        WeaponTweaks.SetOverride(w.Id, TF(nameof(WeaponDef.Damage)), 31);
        WeaponTweaks.SetOverride(pistol.Id, TF(nameof(WeaponDef.RecoilSway)), 1.1f);
        WeaponTweaks.SetClassMult(WeaponClass.SMG, TweakStat.Spread, 0.8f);
        var expect = Weapons.All.Select(WeaponFile.ToJson).ToArray();
        var pw = new PacketWriter();
        pw.Begin(Msg.WeaponTweaks);
        WeaponTweaks.Write(pw);
        var bytes = pw.Span.ToArray();
        WeaponTweaks.ResetAll();
        var r = new PacketReader(bytes.AsSpan(1));
        var applied = WeaponTweaks.Read(ref r);
        var smgs = Weapons.All.Count(x => x.Class == WeaponClass.SMG);
        var payloadOk = applied == 2 + smgs && r.Remaining == 0 && Weapons.All.Select(WeaponFile.ToJson).SequenceEqual(expect); // multipliers go baked into the values

        // Over loopback: a joining client gets the set after its Hello; a later change and a reset reach it too.
        var small = FindMap("Proto Box", m => m.MaxPlayers >= 4)!;
        const ushort port = 26227;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(small);
        bool joined, gotSet, gotChange, gotReset;
        int packets;
        using (var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, mapId: small.Id))
        {
            server.MapVoteEnabled = false;
            using var c = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Game[] clients = [c];
            Pump(server, clients, 1.5, ref now, clock);
            joined = c.LocalId != byte.MaxValue;
            gotSet = c.TweakPacketsReceived >= 1 && c.HostTweaked && c.WeaponsTweaked;
            var before = c.TweakPacketsReceived;
            WeaponTweaks.SetOverride(w.Id, TF(nameof(WeaponDef.Rpm)), 777);
            Pump(server, clients, 0.5, ref now, clock);
            gotChange = c.TweakPacketsReceived > before && Weapons.Get(w.Id).Rpm == 777 && Weapons.Get(w.Id).Damage == 31;
            before = c.TweakPacketsReceived;
            WeaponTweaks.ResetAll();
            Pump(server, clients, 0.5, ref now, clock);
            gotReset = c.TweakPacketsReceived > before && !c.HostTweaked && !c.WeaponsTweaked;
            packets = c.TweakPacketsReceived;
        }
        Line($"network: payload {bytes.Length} bytes for 2 overrides + an SMG spread multiplier ({smgs} SMGs) round-trips {payloadOk}; loopback: joined {joined}, the joining client got the host's set {gotSet}, a change {gotChange}, the reset {gotReset} ({packets} packet(s))");
        if (!payloadOk || !joined || !gotSet || !gotChange || !gotReset)
            Fail("weapon tweaks: the host's tweaks didn't reach the client (payload, join or change)");
        WeaponTweaks.ResetAll();
    }

    private static void CheckTweakMisc()
    {
        // Feel numbers.
        var v = Weapons.Find("Vanguard AR") ?? Weapons.DefaultPrimary;
        var stk = WeaponTweaks.ShotsToKill(v, 5);
        var ttk = WeaponTweaks.TimeToKill(v, 5);
        var wantStk = (int)MathF.Ceiling(World.MaxHealth / (float)(v.Damage * v.Pellets));
        var far = WeaponTweaks.ShotsToKill(v, v.RangeM * 3);
        var feelOk = stk == wantStk && MathF.Abs(ttk - (stk - 1) * v.FireInterval - (stk - 1) / v.Mag * v.Reload) < 1e-4f && far >= stk;
        Line($"feel: {v.Name} {stk} shots / {ttk * 1000:0} ms at 5 m, {far} shots at {v.RangeM * 3:0} m, {v.Dps:0} DPS");
        if (!feelOk)
            Fail($"weapon tweaks: shots / time to kill for {v.Name} wrong ({stk} shots, {ttk} s; want {wantStk})");

        // The key: F7 by default, unshared.
        var kd = Keybinds.Defaults;
        if (kd[(int)GameAction.WeaponTweaks] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F7) || Duplicates(kd).Length > 0)
            Fail("weapon tweaks: the default key isn't an unshared F7");

        // Closed: no allocations a frame.
        var ui = new Ui();
        var w = Weapons.All[0];
        for (var i = 0; i < 100; i++)
            ui.DrawWeaponTweaks(null, i * 0.016, w, Keybinds.Unbound);
        var closed = SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 20000; i++)
                ui.DrawWeaponTweaks(null, i * 0.016, w, Keybinds.Unbound);
        });
        Line($"key {Keybinds.Name(kd[(int)GameAction.WeaponTweaks])}; window closed: {closed} bytes over 20000 frames");
        if (closed > 0)
            Fail($"weapon tweaks: {closed} bytes allocated with the window closed (want 0)");
    }
}
