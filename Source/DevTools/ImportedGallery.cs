using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CallOfSlop;

/// <summary>
/// <c>--imported-gallery DIR</c>: the Imported ships gallery (tools/imports/README.md), a page (index.html, models/index.json, one models/NAME.json a ship) showing every ship the importer made
/// from another tool's model (tools/imports/*/import.json). Each ship is the Animation gallery's model of its blueprint (every event door
/// a track, sampled from the game's own door poses) plus, per part, the model group it came from, so the page can show the hull or the
/// fittings alone, cut the roof away, explode the ship or lay its parts out as a kit, and work each import's "gallery" controls (a ramp,
/// VTOL pods, the doors) on their own.
/// </summary>
public static partial class ConstructPreview
{
    private static readonly Regex FromModel = new(@"from its model's (.+?) \((.+)\)\.$", RegexOptions.Compiled);

    private static int ImportedGallery(string dir)
    {
        string? root = null;
        for (var up = new DirectoryInfo(AppContext.BaseDirectory); up != null && root == null; up = up.Parent)
            if (File.Exists(Path.Combine(up.FullName, "tools", "imports", "viewer.html")))
                root = up.FullName;
        if (root == null)
        {
            Console.WriteLine($"--imported-gallery: no tools/imports/viewer.html above {AppContext.BaseDirectory}");
            return 1;
        }
        var lenient = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var ships = new JsonArray();
        var failed = 0;
        foreach (var spec in Directory.GetFiles(Path.Combine(root, "tools", "imports"), "import.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var s = JsonDocument.Parse(File.ReadAllText(spec), lenient).RootElement;
            var name = s.GetProperty("name").GetString()!;
            var timing = Path.Combine(Path.GetTempPath(), $"imported-{Slug(name)}-{Environment.ProcessId}.json");
            File.WriteAllText(timing, JsonSerializer.Serialize(new { blueprint = name, title = name, steps = Array.Empty<object>() }));
            string? json;
            try
            {
                json = AssemblyModel(timing, null, out _, out _);
            }
            finally
            {
                File.Delete(timing);
            }
            if (json == null)
            {
                Console.WriteLine($"REFUSED {spec}: no model of \"{name}\"");
                failed++;
                continue;
            }
            var m = JsonNode.Parse(json)!.AsObject();
            var gallery = s.TryGetProperty("gallery", out var ge) ? ge : default;
            string[] Patterns(string key) => gallery.ValueKind == JsonValueKind.Object && gallery.TryGetProperty(key, out var a) ? [.. a.EnumerateArray().Select(x => x.GetString()!)] : [];
            var roof = Patterns("roof");
            var hull = Patterns("hull");

            // Each part's model object and group, from the description the importer gave it.
            (string Obj, string Group) Origin(string part)
            {
                var d = ConstructParts.Find(part)?.Description ?? "";
                var mt = FromModel.Match(d);
                return mt.Success ? (mt.Groups[1].Value.Replace(' ', '_'), mt.Groups[2].Value.Replace(' ', '_')) : ("", "");
            }
            var info = new JsonArray();
            foreach (var p in m["parts"]!.AsArray())
            {
                var (obj, group) = Origin(p![0]!.GetValue<string>());
                var kind = roof.Any(r => Like(r, obj) || Like(r, group)) ? "roof" : hull.Any(h => Like(h, obj) || Like(h, group)) ? "hull" : "part";
                info.Add(new JsonArray(group.Replace('_', ' '), kind));
            }
            m["origin"] = info;
            // The controls: each a label and the tracks (doors) it works, by the object they came from.
            var controls = new JsonArray();
            if (gallery.ValueKind == JsonValueKind.Object && gallery.TryGetProperty("controls", out var ce))
                foreach (var c in ce.EnumerateArray())
                {
                    var match = c.GetProperty("match").GetString()!;
                    var tracks = new JsonArray();
                    var k = 0;
                    foreach (var t in m["anim"]!["tracks"]!.AsArray())
                    {
                        k++;
                        if (Like(match, Origin(t!["name"]!.GetValue<string>()).Obj))
                            tracks.Add(k);
                    }
                    if (tracks.Count == 0)
                        Console.WriteLine($"{spec}: the gallery control \"{c.GetProperty("label").GetString()}\" works no door (match \"{match}\")");
                    controls.Add(new JsonObject
                    {
                        ["label"] = c.GetProperty("label").GetString(),
                        ["shut"] = c.TryGetProperty("shut", out var sh) ? sh.GetString() : "Shut",
                        ["open"] = c.TryGetProperty("open", out var op) ? op.GetString() : "Open",
                        ["start"] = c.TryGetProperty("start", out var st) && st.GetString() == "open",
                        ["tracks"] = tracks,
                    });
                }
            m["controls"] = controls;
            Directory.CreateDirectory(Path.Combine(dir, "models"));
            File.WriteAllText(Path.Combine(dir, "models", Slug(name) + ".json"), m.ToJsonString());
            ships.Add(new JsonObject { ["id"] = Slug(name), ["name"] = name, ["file"] = $"models/{Slug(name)}.json" });
            Console.WriteLine($"imported gallery: {name}, {m["parts"]!.AsArray().Count} parts, {m["anim"]!["tracks"]!.AsArray().Count} doors, {controls.Count} controls");
        }
        Directory.CreateDirectory(Path.Combine(dir, "models"));
        File.WriteAllText(Path.Combine(dir, "models", "index.json"), ships.ToJsonString());
        File.Copy(Path.Combine(root, "tools", "imports", "viewer.html"), Path.Combine(dir, "index.html"), true);
        Console.WriteLine($"imported gallery {Path.GetFullPath(dir)}: {ships.Count} ships, {failed} refused (index.html, models/index.json, one models/NAME.json a ship)");
        return failed;
    }

    /// <summary>A name against a pattern with * for any run of characters.</summary>
    private static bool Like(string pattern, string name) =>
        name != "" && Regex.IsMatch(name, "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$");
}
