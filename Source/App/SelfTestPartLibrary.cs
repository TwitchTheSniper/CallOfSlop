using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The part library (Source/Zombies/PartLibrary.cs, PartReload.cs): the registry file matches the built-in lists,
    /// fragments and "extends" resolve to exactly what a part written out in full gives, templates and fragments load,
    /// the cross-part checks pass on the shipped set and catch broken ones, broken files get readable errors, the
    /// normaliser is stable, and hot reload swaps definitions in place (builds, built weapons and workbench saves follow,
    /// a broken file keeps the old version) and moves the fingerprint joiners are checked against. Temp files only.
    /// </summary>
    private static void CheckPartLibrary()
    {
        BuildStore.ReadOnly = true;
        var lib = Parts.Library;
        var reg = lib.Registry;
        Line($"registry {reg.Source ?? "built-in"}: {reg.SocketTypes.Length} socket types, {reg.Bores.Length} bores; {lib.Fragments.Count} fragment(s), {lib.Bases.Count} base(s)");
        if (reg.Source == null)
            Fail("Parts/_Registry.json didn't load (using the built-in registry)");
        foreach (var t in PartRegistry.BuiltIn.SocketTypes)
            if (!reg.SocketTypes.Contains(t))
                Fail($"registry: lost the socket type \"{t}\"");
        foreach (var b in PartRegistry.BuiltIn.Bores)
            if (!reg.Bores.Contains(b))
                Fail($"registry: lost the bore \"{b}\"");
        foreach (var c in Enum.GetValues<PartCategory>())
            if (reg.MountOf(c) != PartFile.BuiltInMount(c) || reg.DisplayOf(c) != PartDef.BuiltInCategoryName(c))
                Fail($"registry: {c} is \"{reg.MountOf(c)}\"/\"{reg.DisplayOf(c)}\", the built-in \"{PartFile.BuiltInMount(c)}\"/\"{PartDef.BuiltInCategoryName(c)}\"");
        if (lib.Fragments.Count < 5 || lib.Bases.Count < 1)
            Fail("expected the fragment library (Parts/_Fragments) and bases (Parts/_Bases)");

        // Cross-part checks on the shipped set; fragments expand; templates load.
        var notes = new List<string>();
        foreach (var p in Parts.Lint(Parts.All, reg, notes))
            Fail("parts: " + p);
        foreach (var n in notes)
            Line("note: " + n);
        foreach (var f in lib.Fragments.Values)
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse($"{{\"use\": {WeaponFile.Str(f.Name)}}}");
                if (lib.Use(doc.RootElement, f.Source, 0).ToArray().Length == 0)
                    Fail($"fragment '{f.Name}' has no boxes");
            }
            catch (WeaponFileException ex)
            {
                Fail("fragment: " + ex.Message);
            }
        var templates = PartTools.TemplateFiles(Parts.Folder);
        if (templates.Count < 2)
            Fail("Parts/_Template should hold the commented template and examples");
        foreach (var (rel, text) in templates)
            try
            {
                var tp = PartFile.Load(text, rel, lib);
                if (Parts.Find(tp.Name) != null)
                    Fail($"{rel}: template '{tp.Name}' shares a name with a real part");
                if (tp.Boxes.Length == 0)
                    Fail($"{rel}: template has no model");
                Line($"template {rel}: '{tp.Name}' {tp.Category}, {tp.Boxes.Length} boxes, {tp.Sockets.Length} sockets");
            }
            catch (WeaponFileException ex)
            {
                Fail("template: " + ex.Message);
            }

        CheckPartEquivalence(lib);
        CheckPartLibraryErrors();
        CheckPartNormaliser(lib);
        CheckPartHotReload();
    }

    /// <summary>Fragments and "extends" give exactly what writing the part out in full gives.</summary>
    private static void CheckPartEquivalence(PartLibrary lib)
    {
        if (Parts.Find("Standard Barrel") is not { } sb || Parts.Find("Box Mag") is not { } box)
        {
            Fail("parts: Standard Barrel / Box Mag missing");
            return;
        }
        // "extends": the Standard Barrel from its base in three lines.
        var viaBase = PartFile.Load($$"""{ "extends": "Base Barrel", "name": "Standard Barrel", "flavor": {{WeaponFile.Str(sb.Flavor)}} }""", "Barrel/StandardBarrel.json", lib);
        if (PartDigest.Of(viaBase) != PartDigest.Of(sb))
            Fail("extends: 'Base Barrel' + name + flavor isn't identical to the Standard Barrel");
        // A fragment holding the Box Mag's box, used at the origin: identical to the box written in place.
        var flib = PartLibrary.Empty;
        flib.AddFragment("""{ "name": "Mag", "model": [{"size": [0.024, 0.12, 0.05], "pos": [0, -0.06, 0], "color": "accent"}] }""", "_Fragments/Mag.json");
        var text = File.ReadAllText(Path.Combine(Parts.Folder, box.SourceFile!));
        var usesMag = text.Replace("""{"size": [0.024, 0.12, 0.05], "pos": [0, -0.06, 0], "color": "accent"}""", """{"use": "Mag"}""");
        if (usesMag == text)
            Fail("fragments: Box Mag's model box isn't where the test expects it");
        else if (PartDigest.Of(PartFile.Load(usesMag, box.SourceFile!, flib)) != PartDigest.Of(box))
            Fail("fragments: a fragment use at the origin isn't identical to the box written in place");
        // Turned and scaled fragments: the box's own turn, then the use's; positions scaled in the fragment's axes.
        flib.AddFragment("""{ "name": "Turned", "model": [{"size": [0.01, 0.02, 0.03], "pos": [0.01, 0.02, -0.03], "rot": [0, 30, 0]}] }""", "_Fragments/Turned.json");
        var t = PartFile.Load("""{ "name": "T", "category": "stock", "rarity": "common", "flavor": "x", "model": [{"use": "Turned", "pos": [0, 0.1, 0], "rot": [10, 0, 20], "scale": [2, 1, 1]}] }""", "t.json", flib);
        var tb = t.Boxes.Single();
        var q = PartDef.Rot(new Vector3(10, 0, 20));
        var wantPos = Vector3.Transform(new Vector3(0.02f, 0.02f, -0.03f), q) + new Vector3(0, 0.1f, 0);
        var probe = new Vector3(0.3f, -0.7f, 0.2f);
        var wantDir = Vector3.Transform(Vector3.Transform(probe, PartDef.Rot(new Vector3(0, 30, 0))), q);
        if (Vector3.Distance(tb.Pos, wantPos) > 1e-5f || Vector3.Distance(Vector3.Transform(probe, tb.Rotation), wantDir) > 1e-5f || Vector3.Distance(tb.Size, new Vector3(0.02f, 0.02f, 0.03f)) > 1e-6f)
            Fail($"fragments: turned/scaled box at {tb.Pos} size {tb.Size} (want {wantPos})");
        var e = PartJson.EulerDeg(tb.Rotation);
        if (Vector3.Distance(Vector3.Transform(probe, PartDef.Rot(e)), wantDir) > 1e-4f)
            Fail($"fragments: Euler angles {e} of a combined turn don't turn things the same way");
        // Merge rules: objects merge key by key, "+sockets" replaces by name, "-sockets" / "-stats" remove, null removes.
        try
        {
            var v = PartFile.Load("""
                { "extends": "Base Auto Receiver", "name": "Merge Test", "receiver": {"soundOf": "Raptor"},
                  "-sockets": ["utility"], "+sockets": [{"name": "amp", "type": "amp", "pos": [0, 0.1, 0]}],
                  "-stats": "reload", "+stats": [{"stat": "reload", "set": 3}], "+model": [{"use": "Screw Head", "pos": [0, 0.07, 0.05]}] }
                """, "merge.json", Parts.Library);
            var baseRx = PartFile.Load(File.ReadAllText(Path.Combine(Parts.Folder, PartLibrary.BaseFolder, "BaseAutoReceiver.json")).Replace("\"Base Auto Receiver\"", "\"B\""), "b.json", Parts.Library);
            if (v.Receiver!.SoundOf != "Raptor" || v.Receiver.Bore != baseRx.Receiver!.Bore || v.Receiver.StockAction != baseRx.Receiver.StockAction || v.Receiver.StockAction == null)
                Fail("extends: the receiver block didn't merge key by key");
            if (v.Sockets.Any(s => s.Name == "utility") || v.Sockets.Length != baseRx.Sockets.Length - 1 || v.Sockets.First(s => s.Name == "amp").Pos.Y != 0.1f)
                Fail("extends: -sockets / +sockets (replace by name) went wrong");
            if (v.Effects.Count(x => x.Field.Name == nameof(WeaponDef.Reload)) != 1 || v.Boxes.Length != baseRx.Boxes.Length + 2)
                Fail("extends: -stats / +stats / +model went wrong");
            var n = PartFile.Load("""{ "extends": "Standard Barrel", "name": "No Hand", "hand": null }""", "n.json", Parts.Library);
            if (n.Hand != null)
                Fail("extends: null didn't remove the key");
        }
        catch (WeaponFileException ex)
        {
            Fail("extends: " + ex.Message);
        }
    }

    private static void ExpectLibError(Action load, string expect, string what)
    {
        try
        {
            load();
            Fail($"part library: {what} was accepted");
        }
        catch (WeaponFileException ex)
        {
            if (!ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
                Fail($"part library: {what}: message \"{ex.Message}\" doesn't mention \"{expect}\"");
        }
    }

    /// <summary>Broken fragments, bases, registries and set-wide problems get messages that say what to fix.</summary>
    private static void CheckPartLibraryErrors()
    {
        var lib = Parts.Library;
        const string head = """ "category": "stock", "rarity": "common", "flavor": "x" """;
        ExpectLibError(() => PartFile.Load($$"""{ "name": "X", {{head}}, "model": [{"use": "Nope Rail"}] }""", "x.json", lib), "no model fragment called 'Nope Rail'", "an unknown fragment");
        var loop = PartLibrary.Empty;
        loop.AddFragment("""{ "name": "A", "model": [{"use": "B"}] }""", "_Fragments/A.json");
        loop.AddFragment("""{ "name": "B", "model": [{"use": "A"}] }""", "_Fragments/B.json");
        ExpectLibError(() => PartFile.Load($$"""{ "name": "X", {{head}}, "model": [{"use": "A"}] }""", "x.json", loop), "use itself", "fragments using each other");
        ExpectLibError(() => PartFile.Load($$"""{ "name": "X", {{head}}, "model": [{"use": "Screw Head", "scale": 100}] }""", "x.json", lib), "between", "a scale out of range");
        ExpectLibError(() => PartFile.Load($$"""{ "name": "X", {{head}}, "model": [{"use": "Screw Head", "spin": 1}] }""", "x.json", lib), "unknown key", "a typo in a fragment use");
        ExpectLibError(() => PartFile.Load("""{ "extends": "Base Nothing", "name": "X" }""", "x.json", lib), "no part or base called 'Base Nothing'", "extends of an unknown base");
        ExpectLibError(() => PartFile.Load("""{ "extends": "Standard Barrel" }""", "x.json", lib), "own \"name\"", "extends without a name");
        var circle = PartLibrary.Empty;
        circle.AddBase("""{ "name": "Base A", "extends": "Base B" }""", "_Bases/A.json");
        circle.AddBase("""{ "name": "Base B", "extends": "Base A" }""", "_Bases/B.json");
        ExpectLibError(() => PartFile.Load("""{ "extends": "Base A", "name": "X" }""", "x.json", circle), "circle", "extends going round in a circle");
        ExpectLibError(() => PartFile.Load($$"""{ "name": "X", {{head}}, "+model": [] }""", "x.json", lib), "only works in a part that \"extends\"", "+model without extends");
        ExpectLibError(() => PartFile.Load("""{ "extends": "Standard Barrel", "name": "X", "-sockets": ["bayonet"] }""", "x.json", lib), "no socket \"bayonet\"", "removing a socket the base doesn't have");
        ExpectLibError(() => PartFile.Load("""{ "extends": "Standard Barrel", "name": "X", "+weight": [1] }""", "x.json", lib), "isn't a list you can add to", "+ on a non-list key");
        ExpectLibError(() => PartFile.Load($$"""{ "name": "X", {{head}}, "sockets": [{"name": "s", "type": "bayonet", "pos": [0, 0, 0]}] }""", "x.json", lib), "_Registry.json", "an unknown socket type");
        ExpectLibError(() => PartRegistry.Load("""{ "socketTypes": ["barrel"], "bores": ["rifle"], "categories": [] }""", "_Registry.json"), "must keep \"receiver\"", "a registry without a core socket type");
        ExpectLibError(() => PartRegistry.Load("""{ "socketTypes": ["receiver", "barrel", "feed", "ammo", "stock", "foregrip", "optic", "muzzle", "hopper", "head", "action"], "bores": ["rifle"], "categories": [] }""", "_Registry.json"),
            "is missing", "a registry without every category");
        ExpectLibError(() => PartLibrary.Empty.AddFragment("""{ "name": "F" }""", "_Fragments/F.json"), "model", "a fragment without a model");

        // A registry with a new socket type: parts can use it straight away.
        var extra = PartLibrary.Empty;
        var regText = File.ReadAllText(Path.Combine(Parts.Folder, PartLibrary.RegistryFile)).Replace("""{"name": "mod",""", """{"name": "bayonet", "about": "test"}, {"name": "mod",""");
        extra.Registry = PartRegistry.Load(regText, "_Registry.json");
        try
        {
            var lug = PartFile.Load($$"""{ "name": "X", {{head}}, "sockets": [{"name": "lug", "type": "bayonet", "pos": [0, 0, 0]}] }""", "x.json", extra);
            var knife = PartFile.Load("""{ "name": "Y", "category": "utility", "rarity": "common", "flavor": "x", "mount": "bayonet" }""", "y.json", extra);
            if (Parts.Lint([lug, knife], extra.Registry).Any(x => x.StartsWith("Y ", StringComparison.Ordinal) || x.Contains("\"lug\"")))
                Fail("registry: a new socket type and a part for it should pass the checks");
        }
        catch (WeaponFileException ex)
        {
            Fail("registry: a socket type added in the registry was refused: " + ex.Message);
        }

        // Set-wide checks: a barrel on its own fits nowhere and its muzzle socket takes nothing.
        var alone = Parts.Lint([Parts.Find("Standard Barrel")!], Parts.Registry);
        if (!alone.Any(x => x.Contains("fits nowhere")) || !alone.Any(x => x.Contains("takes nothing")))
            Fail($"lint: a lone barrel should fit nowhere with a dangling socket (got: {string.Join(" | ", alone)})");
        else
            Line("lint example: " + alone[0]);
    }

    /// <summary>--export-parts: normalising is stable and changes nothing a part does; --resolved gives plain boxes.</summary>
    private static void CheckPartNormaliser(PartLibrary lib)
    {
        var same = 0;
        foreach (var (rel, text) in lib.PartTexts)
            try
            {
                var a = PartJson.Normalise(text, rel);
                if (PartJson.Normalise(a, rel) != a)
                    Fail($"normalise: {rel} changes when normalised twice");
                if (PartDigest.Of(PartFile.Load(a, rel, lib)) != PartDigest.Of(PartFile.Load(text, rel, lib)))
                    Fail($"normalise: {rel} loads differently after normalising");
                if (a.Replace("\r", "") == text.Replace("\r", ""))
                    same++;
            }
            catch (WeaponFileException ex)
            {
                Fail("normalise: " + ex.Message);
            }
        Line($"normaliser: {same}/{lib.PartTexts.Count} part files are already in the normalised layout");
        foreach (var (rel, text) in PartTools.TemplateFiles(Parts.Folder))
            try
            {
                var p = PartFile.Load(text, rel, lib);
                var flat = PartJson.Resolved(text, rel, lib);
                if (flat.Contains("\"use\"") || flat.Contains("\"extends\""))
                    Fail($"resolved: {rel} still has fragments or extends");
                var q = PartFile.Load(flat, rel, PartLibrary.Empty);
                var probe = new Vector3(0.3f, 0.5f, -0.7f);
                if (q.Boxes.Length != p.Boxes.Length || q.Sockets.Length != p.Sockets.Length || q.Effects.Length != p.Effects.Length
                    || p.Boxes.Zip(q.Boxes).Any(z => Vector3.Distance(z.First.Pos, z.Second.Pos) > 1e-4f || Vector3.Distance(z.First.Size, z.Second.Size) > 1e-4f
                                                     || Vector3.Distance(Vector3.Transform(probe, z.First.Rotation), Vector3.Transform(probe, z.Second.Rotation)) > 1e-3f))
                    Fail($"resolved: {rel} written out in full doesn't match");
            }
            catch (WeaponFileException ex)
            {
                Fail("resolved: " + ex.Message);
            }
    }

    /// <summary>Hot reload from a temporary copy of the Parts folder, then back from the real one.</summary>
    private static void CheckPartHotReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "callofslop-parts-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var f in Directory.GetFiles(Parts.Folder, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir, Path.GetRelativePath(Parts.Folder, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            var sb = Parts.Find("Standard Barrel")!;
            var boxMag = Parts.Find("Box Mag")!;
            var hashBefore = PartSet.Hash;
            var genBefore = Parts.Generation;
            var countBefore = Parts.All.Length;
            var rangeBefore = sb.Effects.Length;
            var plain = BuilderExamples.All().First(e => e.Build.Name == "Plain Rifle").Build;
            var before = WeaponBuilder.Assemble(plain).Def!;
            if (before.BuildText == null)
                Fail("hot reload: built weapons don't carry their build text");
            var melee = BuilderExamples.Melee().Select(m => WeaponBuilder.Assemble(m.Build).Melee).FirstOrDefault(m => m != null);

            // Edit: a longer-ranged Standard Barrel with a longer rod, a new part, a broken Box Mag.
            var sbFile = Path.Combine(dir, sb.SourceFile!);
            File.WriteAllText(sbFile, File.ReadAllText(sbFile).Replace("\"stats\": []", "\"stats\": [{\"stat\": \"rangeM\", \"mul\": 2}]")
                .Replace("\"size\": [0.022, 0.022, 0.2]", "\"size\": [0.022, 0.022, 0.4]"));
            File.WriteAllText(Path.Combine(dir, "Muzzle", "HotReloadTest.json"),
                File.ReadAllText(Path.Combine(Parts.Folder, Parts.Find("Flash Hider")!.SourceFile!)).Replace("\"Flash Hider\"", "\"Hot Reload Test Hider\""));
            File.WriteAllText(Path.Combine(dir, boxMag.SourceFile!), "{ \"name\": \"Box Mag\", oops");

            var r = Parts.Reload(dir);
            Line("reload (edited copy): " + r.Summary);
            if (!ReferenceEquals(Parts.Find("Standard Barrel"), sb) || sb.Effects.Length != rangeBefore + 1 || !r.Changed.Contains("Standard Barrel"))
                Fail("hot reload: the Standard Barrel wasn't updated in place");
            if (!r.Added.Contains("Hot Reload Test Hider") || Parts.Find("Hot Reload Test Hider") == null)
                Fail("hot reload: the new part wasn't added");
            if (!r.Kept.Contains("Box Mag") || !ReferenceEquals(Parts.Find("Box Mag"), boxMag) || r.Errors.Length != 1 || Parts.LoadErrors.Count != 1)
                Fail($"hot reload: a broken file should keep its old part and report one error (kept {string.Join(", ", r.Kept)}, {r.Errors.Length} error(s))");
            if (!r.FingerprintChanged || PartSet.Hash == hashBefore || Parts.Generation != genBefore + 1 || Parts.All.Length != countBefore + 1)
                Fail("hot reload: fingerprint, generation or count didn't move");
            if (PartSet.Mismatch(hashBefore, countBefore) is not { } why || !why.Contains("Can't join"))
                Fail("hot reload: a joiner with the old parts should be refused");
            else
                Line("joiner with the old parts: " + why);

            // Built weapons follow: assembled again in place (id kept), with the new numbers and model.
            var def = WeaponBuilder.Assemble(plain).Def!;
            if (def.RangeM < before.RangeM * 1.5f || def.BuiltMesh!.Front >= before.BuiltMesh!.Front)
                Fail($"hot reload: the rebuilt rifle has range {def.RangeM} (was {before.RangeM}) and front {def.BuiltMesh!.Front} (was {before.BuiltMesh!.Front})");
            var held = before.Clone();
            var otherId = Weapons.All.First(w => w.Id != before.Id).Id;
            held.Id = otherId;
            var mesh = held.BuiltMesh;
            if (!Parts.Rebuild(held, out var problem) || held.Id != otherId || MathF.Abs(held.RangeM - def.RangeM) > 0.01f || ReferenceEquals(held.BuiltMesh, mesh))
                Fail($"hot reload: rebuilding a held weapon in place failed ({problem})");
            if (melee != null && (!Parts.Rebuild(melee, out var mp) || melee.BuildText == null))
                Fail($"hot reload: rebuilding a melee weapon failed ({mp})");

            // In a session (offline: allowed): the real folder comes back and the guns in hand are rebuilt.
            World.SetMap(Maps.Find("Workbench Range") ?? Maps.All[0]);
            using (var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0))
            {
                game.BuilderRange = true;
                game.UseBuiltWeapons(def, held);
                if (game.PartReloadBlocked is { } blocked)
                    Fail("hot reload: refused offline: " + blocked);
                var msg = game.ReloadPartFiles();
                Line("reload (session, real folder): " + msg);
                var me = game.Local!;
                if (MathF.Abs(me.BuiltPrimary!.RangeM - before.RangeM) > 0.01f || !msg.Contains("Rebuilt"))
                    Fail($"hot reload: the gun in hand wasn't rebuilt (range {me.BuiltPrimary!.RangeM}, want {before.RangeM})");
                if (game.Slots[0].Def != me.BuiltPrimary)
                    Fail("hot reload: the weapon slot doesn't hold the rebuilt gun");
            }
            if (PartSet.Hash != hashBefore || Parts.All.Length != countBefore || Parts.LoadErrors.Count != 0 || Parts.Find("Hot Reload Test Hider") != null || sb.Effects.Length != rangeBefore)
                Fail("hot reload: reloading the real folder didn't restore the parts exactly");

            // Workbench saves still load after a reload (workbench.txt format through SavePaths, in a temp file).
            var save = Path.Combine(dir, "workbench.txt");
            var lines = BuildStore.Lines();
            SavePaths.WriteLines(save, lines, BuildStore.SaveVersion);
            BuildStore.Load(SavePaths.ReadLines(save, BuildStore.SaveVersion) ?? [], Parts.Find, out var dropped);
            if (dropped.Count > 0 || !BuildStore.Lines().SequenceEqual(lines))
                Fail($"hot reload: workbench saves changed after a reload (dropped {string.Join(", ", dropped)})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail("hot reload: temp folder: " + ex.Message);
        }
        finally
        {
            if (Parts.All.Any(p => p.Name == "Hot Reload Test Hider"))
                Parts.Reload(); // never leave the test copy loaded
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
