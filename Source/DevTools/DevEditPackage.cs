using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// A change package from the dev prop editor: a folder a tester attaches to a Playtest Desk report, never a change to a
/// shipped file. <c>DevEdits/&lt;yyyyMMdd-HHmmss&gt;-&lt;Prop&gt;/</c> in the save folder holds:
/// <list type="bullet">
/// <item><c>edit.json</c> (format <see cref="Format"/>): game build, map and map file, prop and prop file, the SHA-256 of
/// the prop file it started from, every changed value (<c>propChanges</c>: path, from, to), the map op patches
/// (<c>mapOps</c>: op number, the op before and after) and, for a prop no single op placed, its new <c>placement</c>.</item>
/// <item>the whole edited prop file at its own path (<c>Props/Animated/Anemometer.json</c>), when the prop changed: copy it
/// over the original to apply it.</item>
/// <item><c>README.txt</c>: the same in words.</item>
/// </list>
/// <see cref="Validate"/> (<c>--check-devedit DIR</c>) loads the package's prop file with the real loader and applies its
/// map op patches to the map file and loads that, so a package that would not load is caught before anyone applies it.
/// </summary>
public static class DevEditPackage
{
    public const string Format = "call-of-slop-devedit/1";

    public const string FileName = "edit.json";

    /// <summary>The folder packages go in (in the save folder; not a save file: never migrated, never shipped).</summary>
    public static string Folder => Path.Combine(SavePaths.Folder, "DevEdits");

    /// <summary>
    /// Writes the session's changes as a package under <paramref name="root"/> (default <see cref="Folder"/>); returns the
    /// package folder. <paramref name="note"/>: the tester's own words, kept in the readme and edit.json.
    /// </summary>
    public static string Write(PropEditSession s, DateTime now, string note = "", string? root = null, string? dataFolder = null)
    {
        var dir = Path.Combine(root ?? Folder, $"{now:yyyyMMdd-HHmmss}-{s.Slug}");
        for (var n = 2; Directory.Exists(dir); n++)
            dir = Path.Combine(root ?? Folder, $"{now:yyyyMMdd-HHmmss}-{s.Slug}-{n}");
        Directory.CreateDirectory(dir);

        var changes = s.PropChanges();
        var edit = new JsonObject
        {
            ["format"] = Format,
            ["game"] = BuildInfo.Label,
            ["created"] = now.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["map"] = s.Map.Name,
            ["mapFile"] = LookAt.MapPath(s.Map),
            ["prop"] = s.Def.Name,
            ["propFile"] = s.PropPath,
            ["baseSha256"] = Sha(s.OriginalText),
        };
        if (note.Length > 0)
            edit["note"] = note;
        var propChanges = new JsonArray();
        foreach (var (path, from, to) in changes)
            propChanges.Add(new JsonObject { ["path"] = path, ["from"] = from?.DeepClone(), ["to"] = to?.DeepClone() });
        edit["propChanges"] = propChanges;

        var mapOps = new JsonArray();
        var placementNote = "";
        if (s.PlacementChanged)
        {
            if (s.MapOpPatch(out var why, dataFolder) is { } patch)
                mapOps.Add(new JsonObject { ["opNumber"] = patch.OpNumber, ["before"] = patch.Before, ["after"] = patch.After });
            else
            {
                placementNote = why;
                edit["placement"] = new JsonObject
                {
                    ["instance"] = s.Instance + 1,
                    ["why"] = why,
                    ["fromPos"] = PropEditSession.Vec(s.OriginalPlacement.Position),
                    ["fromYaw"] = Math.Round(s.OriginalPlacement.Yaw * 180.0 / Math.PI, 2),
                    ["fromScale"] = Math.Round(s.OriginalPlacement.Scale, 3),
                    ["toPos"] = PropEditSession.Vec(s.Position),
                    ["toYaw"] = Math.Round(s.YawDeg, 2),
                    ["toScale"] = Math.Round(s.Scale, 3),
                };
            }
        }
        edit["mapOps"] = mapOps;

        if (changes.Count > 0)
        {
            var propFile = Path.Combine(dir, s.PropPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(propFile)!);
            File.WriteAllText(propFile, PropEditSession.Format(s.Json));
        }
        File.WriteAllText(Path.Combine(dir, FileName), edit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.WriteAllText(Path.Combine(dir, "README.txt"), Readme(s, changes, mapOps, placementNote, note));
        Console.WriteLine($"[devedit] saved a change package for \"{s.Def.Name}\": {changes.Count} prop change(s), {mapOps.Count} map op patch(es) in {dir}");
        return dir;
    }

    private static string Readme(PropEditSession s, List<(string Path, JsonNode? From, JsonNode? To)> changes, JsonArray mapOps, string placementNote, string note)
    {
        var sb = new StringBuilder();
        sb.Append("Call of Slop dev edit: ").Append(s.Def.Name).Append(" on ").Append(s.Map.Name).Append('\n');
        sb.Append("Made in the in-game prop editor (").Append(BuildInfo.Label).Append(").\n");
        if (note.Length > 0)
            sb.Append("\nNote: ").Append(note).Append('\n');
        sb.Append("\nProp file: ").Append(s.PropPath).Append('\n');
        if (changes.Count == 0)
            sb.Append("  (unchanged)\n");
        else
        {
            sb.Append("  The whole edited file is in this folder at the same path: copy it over the original to apply it.\n");
            foreach (var (path, from, to) in changes)
                sb.Append("  ").Append(path).Append(": ").Append(from?.ToJsonString() ?? "(none)").Append(" -> ").Append(to?.ToJsonString() ?? "(removed)").Append('\n');
        }
        sb.Append("\nMap file: ").Append(LookAt.MapPath(s.Map)).Append('\n');
        if (mapOps.Count == 0 && placementNote.Length == 0)
            sb.Append("  (the placed prop was not moved)\n");
        foreach (var op in mapOps)
            sb.Append("  op #").Append(op!["opNumber"]).Append(": ").Append(op["before"]!.ToJsonString()).Append("\n    becomes ").Append(op["after"]!.ToJsonString()).Append('\n');
        if (placementNote.Length > 0)
            sb.Append("  moved, but ").Append(placementNote).Append(" (see \"placement\" in edit.json)\n");
        sb.Append("\nCheck it: CallOfSlop.exe --check-devedit \"<this folder>\" (loads the prop file and the patched map).\n");
        sb.Append("Attach this folder (zipped) to the Playtest Desk report.\n");
        return sb.ToString();
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>
    /// Checks a package: edit.json's format, the prop file (it loads with the real loader, under the prop's name), and each
    /// map op patch (the map file's op still matches "before", and the map loads with "after" in its place). Returns the
    /// problems (empty: good). <paramref name="dataFolder"/>: where the game's Props and Maps are (default the game's).
    /// </summary>
    public static List<string> Validate(string dir, string? dataFolder = null)
    {
        var problems = new List<string>();
        var data = dataFolder ?? AppFolders.Data;
        JsonObject edit;
        try
        {
            edit = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, FileName))) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            problems.Add($"{FileName}: {ex.Message}");
            return problems;
        }
        if (edit["format"]?.GetValue<string>() != Format)
            problems.Add($"{FileName}: \"format\" is not \"{Format}\"");
        var propName = edit["prop"]?.GetValue<string>() ?? "";
        var propFile = edit["propFile"]?.GetValue<string>() ?? "";
        if (propFile.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(propFile))
        {
            problems.Add($"{FileName}: \"propFile\" must be a path inside the game folder");
            return problems;
        }
        var changes = edit["propChanges"] as JsonArray;
        if (changes is { Count: > 0 })
        {
            var file = Path.Combine(dir, propFile.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file))
                problems.Add($"the package lists prop changes but has no {propFile}");
            else
            {
                try
                {
                    var category = propFile.Split('/') is { Length: > 2 } seg && seg[0] == "Props" ? seg[1] : "Misc";
                    var mapScope = Props.ForPack(PackDirOf(edit["mapFile"]?.GetValue<string>(), data));
                    var def = PropFile.Load(File.ReadAllText(file), propFile, category, n => mapScope.Find(n));
                    if (!def.Name.Equals(propName, StringComparison.OrdinalIgnoreCase))
                        problems.Add($"{propFile}: the prop is called \"{def.Name}\", the package says \"{propName}\"");
                }
                catch (PropFileException ex)
                {
                    problems.Add($"{propFile}: {ex.Message}");
                }
            }
        }
        if (edit["mapOps"] is JsonArray ops && ops.Count > 0)
        {
            var mapFile = edit["mapFile"]?.GetValue<string>() ?? "";
            var rel = mapFile.StartsWith("Maps/", StringComparison.Ordinal) ? mapFile[5..] : mapFile;
            if (rel.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                problems.Add($"{FileName}: \"mapFile\" must be a path inside the Maps folder");
            else
            {
                var full = Path.Combine(data, "Maps", rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(full), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                               ?? throw new JsonException("not an object");
                    var list = root["ops"] as JsonArray ?? throw new JsonException("no \"ops\" list");
                    foreach (var op in ops)
                    {
                        var n = op?["opNumber"]?.GetValue<int>() ?? 0;
                        if (n < 1 || n > list.Count)
                        {
                            problems.Add($"{mapFile}: no op #{n}");
                            continue;
                        }
                        if (!JsonNode.DeepEquals(list[n - 1], op!["before"]))
                            problems.Add($"{mapFile} op #{n} has changed since the package was made (it no longer matches \"before\")");
                        list[n - 1] = op["after"]?.DeepClone();
                    }
                    var packDir = Path.GetDirectoryName(full);
                    var patched = MapFile.Load(root.ToJsonString(), rel, packDir != null && Path.GetDirectoryName(packDir) is { } md && Path.GetFileName(md).Equals("Maps", StringComparison.OrdinalIgnoreCase) ? packDir : "");
                    if (propName.Length > 0 && !patched.PropInstances.Any(p => p.Def.Name.Equals(propName, StringComparison.OrdinalIgnoreCase)))
                        problems.Add($"{mapFile}: the patched map has no \"{propName}\"");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or MapFileException or InvalidOperationException)
                {
                    problems.Add($"{mapFile}: {ex.Message}");
                }
            }
        }
        return problems;
    }

    /// <summary>The pack folder of a map file path ("Maps/Space/x.json" is Maps/Space), or null.</summary>
    private static string? PackDirOf(string? mapFile, string data)
    {
        if (mapFile == null || !mapFile.StartsWith("Maps/", StringComparison.Ordinal))
            return null;
        var seg = mapFile.Split('/');
        return seg.Length > 2 ? Path.Combine(data, "Maps", seg[1]) : null;
    }

    /// <summary>Opens a folder in the file manager (made first if it isn't there).</summary>
    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var psi = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }
                : new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", dir) { UseShellExecute = false };
            System.Diagnostics.Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[devedit] couldn't open {dir}: {ex.Message}");
        }
    }

    /// <summary><c>CallOfSlop.exe --check-devedit DIR</c>: checks one package (or every package in a folder of them). Exit code: the number of problems.</summary>
    public static int Cli(string dir)
    {
        var dirs = File.Exists(Path.Combine(dir, FileName)) ? [dir]
            : Directory.Exists(dir) ? Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, FileName))).Order(StringComparer.Ordinal).ToArray() : [];
        if (dirs.Length == 0)
        {
            Console.Error.WriteLine($"no dev edit package ({FileName}) in {dir}");
            return 1;
        }
        var total = 0;
        foreach (var d in dirs)
        {
            var problems = Validate(d);
            total += problems.Count;
            Console.WriteLine(problems.Count == 0 ? $"OK: {d}" : $"PROBLEMS in {d}:");
            foreach (var p in problems)
                Console.WriteLine("  " + p);
        }
        return total;
    }
}
