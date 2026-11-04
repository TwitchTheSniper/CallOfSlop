using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Mods" (phase 6, docs/mods/MODS.md): mods in a temporary folder over the shipped data. The example mod's patch on the
    /// Jump Drive changes its range and keeps its other values, its patch on Ember adds a body by name and changes one value
    /// of another, and its new system joins the galaxy with its own hash; a later mod (by order) patches what an earlier one
    /// left, removes a named list entry and adds a blueprint. Refused, each with the mod's name and the reason, the rest
    /// still loading: a patch of nothing, a new file with a taken name and no "override", a patch that renames, a patched
    /// value the part's own checks refuse, a bad manifest and a disabled mod (skipped, not refused).
    /// </summary>
    private static void CheckMods()
    {
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }

        var root = Path.Combine(Path.GetTempPath(), $"cos-mods-{Environment.ProcessId}");
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
            // The shipped example mod, copied in.
            var example = Path.Combine(FindRepoRoot() ?? AppFolders.Data, "docs", "mods", "example", "LongJumps");
            if (!Directory.Exists(example))
            {
                Line("mods: skipped, the example mod (docs/mods/example) is only in a source checkout");
                return;
            }
            Copy(example, Path.Combine(root, "LongJumps"));
            void Put(string rel, string text)
            {
                var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, text);
            }
            Put("Later/mod.json", """{ "name": "Later", "order": 5 }""");
            Put("Later/Constructs/Parts/Drive.json", """{ "patch": "Jump Drive", "jump": { "charge": 3 } }""");
            Put("Later/Systems/Ember.json", """{ "patch": "Ember", "bodies": [ { "name": "Soot", "remove": true } ] }""");
            Put("Later/Constructs/Blueprints/Copy.json", File.ReadAllText(Path.Combine(ConstructBuild.BlueprintFolder, "Jumper.json")).Replace("\"Jumper\"", "\"Jumper Copy\""));
            Put("Bad/mod.json", """{ "name": "Bad", "order": 9 }""");
            Put("Bad/Systems/Nothing.json", """{ "patch": "No Such System", "description": "x" }""");
            Put("Bad/Systems/Dev.json", File.ReadAllText(Path.Combine(AppFolders.Data, "Systems", "Dev.json")));
            Put("Bad/Constructs/Parts/Rename.json", """{ "patch": "Jump Drive", "name": "Warp Drive" }""");
            Put("Bad/Constructs/Parts/Far.json", "// a value the part's own checks refuse\n{ \"patch\": \"Battery\", \"power\": -5 }");
            Put("Broken/mod.json", """{ "name": "Broken", "colour": "red" }""");
            Put("Off/mod.json", """{ "name": "Off", "enabled": false }""");
            Put("Off/Systems/Ember.json", """{ "patch": "Ember", "description": "never" }""");

            var errors = new List<string>();
            var mods = Mods.Discover(root, errors);
            Want(string.Join(",", mods.Select(m => m.Name)) == "Long Jumps,Later,Bad", $"the enabled mods in order (got {string.Join(",", mods.Select(m => m.Name))})");
            Want(errors.Count == 1 && errors[0].Contains("Broken") && errors[0].Contains("colour"), $"a bad manifest is refused with the key ({string.Join("; ", errors)})");

            // Star systems.
            var sysErrors = new List<string>();
            var plain = StarSystems.LoadFolder(Path.Combine(AppFolders.Data, "Systems"), new List<string>());
            var modded = StarSystems.LoadFolder(Path.Combine(AppFolders.Data, "Systems"), sysErrors, mods);
            var ember = modded.FirstOrDefault(e => e.System.Name == "Ember");
            var emberBase = plain.FirstOrDefault(e => e.System.Name == "Ember");
            var names = ember.System == null ? "" : string.Join(",", ember.System.Bodies.Select(b => b.Name));
            Want(names == "Ember,Cinder,Ashfall,Kiln", $"Ember gains Kiln and loses Soot (bodies {names})");
            Want(ember.System != null && emberBase.System != null && ember.Hash != emberBase.Hash, "a patched system hashes differently");
            Want(modded.Count == plain.Count + 1 && modded[^1].System.Name == "Lantern" && modded.Take(plain.Count).Select(e => e.System.Name).SequenceEqual(plain.Select(e => e.System.Name)),
                $"Lantern is added after the shipped systems, which keep their order ({string.Join(",", modded.Select(e => e.System.Name))})");
            Want(sysErrors.Any(e => e.Contains("mod Bad") && e.Contains("No Such System")), "a patch of nothing is refused");
            Want(sysErrors.Any(e => e.Contains("mod Bad") && e.Contains("override")), "a taken name with no override is refused");

            // Construct parts.
            var partErrors = new List<string>();
            var parts = ConstructParts.LoadFolder(ConstructParts.Folder, partErrors, mods);
            var drive = parts.FirstOrDefault(d => d.Name == "Jump Drive");
            var stock = ConstructParts.Find("Jump Drive");
            Want(drive != null && stock != null && drive.JumpRange == 20 && drive.JumpCharge == 3 && drive.JumpEnergy == stock.JumpEnergy && drive.Mass == stock.Mass && drive.Id == stock.Id,
                $"the Jump Drive patched twice keeps its place and other values (range {drive?.JumpRange}, charge {drive?.JumpCharge}, energy {drive?.JumpEnergy}, id {drive?.Id} vs {stock?.Id})");
            Want(partErrors.Any(e => e.Contains("mod Bad") && e.Contains("rename")), "a renaming patch is refused");
            Want(partErrors.Any(e => e.Contains("mod Bad") && e.Contains("Battery")), $"a patched value the part's checks refuse is refused with the mod's name ({string.Join("; ", partErrors.Where(e => e.Contains("Battery")))})");
            Want(parts.Any(d => d.Name == "Battery"), "the battery still loads after a bad patch on it");

            // Blueprints.
            var bpErrors = new List<string>();
            var bps = ConstructBuild.LoadBlueprints(ConstructBuild.BlueprintFolder, bpErrors, mods);
            Want(bps.Count == ConstructBuild.Blueprints.Count + 1 && bps[^1].Name == "Jumper Copy" && bpErrors.Count == 0, $"a mod's blueprint is added ({bps.Count} vs {ConstructBuild.Blueprints.Count}; {string.Join("; ", bpErrors)})");

            // No mods: exactly the shipped data.
            Want(StarSystems.LoadFolder(Path.Combine(AppFolders.Data, "Systems"), new List<string>(), []).Select(e => e.Hash).SequenceEqual(plain.Select(e => e.Hash)), "no mods load the shipped systems unchanged");
            Line($"mods: {mods.Count} mods; systems {modded.Count} ({sysErrors.Count} refused), parts {parts.Length} ({partErrors.Count} refused), blueprints {bps.Count}");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
        if (problems.Count > 0)
            Fail($"mods: {string.Join("; ", problems)}");
        else
            Line("mods: patches, overrides, new files and refusals all as expected");

        static void Copy(string from, string to)
        {
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(to, Path.GetRelativePath(from, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }
        }
    }
}
