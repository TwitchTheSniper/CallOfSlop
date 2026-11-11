using System.Text;
using System.Text.RegularExpressions;
using CallOfSlop.PartKit;

namespace CallOfSlop.Story;

/// <summary>
/// <c>--story-map [OUT.html]</c>: the story map, one read-only page of every storylet in Story/Storylets as a flowchart with each
/// beat spelt out (docs/story/STORY.md "Storylets"). It reads the game's files and fills the page template
/// tools/story/story_map.html; run it again after the storylets change. Was tools/story/story_map.py (same bytes).
/// </summary>
public static class StoryMapPage
{
    public static int Cli(string[] args)
    {
        var root = PartKit.PartKit.FindRoot();
        var outArg = args.SkipWhile(a => !a.Equals("--story-map", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var outPath = outArg is null || outArg.StartsWith("--") ? "story_map_out.html" : outArg;
        var files = new List<object?>();
        var count = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(root, "Story", "Storylets"), "*.json").Order(StringComparer.Ordinal))
        {
            // As Python read it: universal newlines, "//" comment lines dropped, trailing commas dropped.
            var text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").Replace('\r', '\n');
            text = Regex.Replace(text, @"^\s*//.*$", "", RegexOptions.Multiline);
            var data = (PyDict)PyJson.Loads(Regex.Replace(text, @",(\s*[}\]])", "$1"))!;
            data["file"] = Path.GetFileName(path);
            count += data.Get("storylets") is List<object?> s ? s.Count : 0;
            files.Add(data);
        }
        var page = File.ReadAllText(Path.Combine(root, "tools", "story", "story_map.html"), Encoding.UTF8).Replace("\r\n", "\n");
        page = page.Replace("/*DATA*/[]", PyJson.Dumps(files).Replace("</", "<\\/"));
        File.WriteAllBytes(outPath, new UTF8Encoding(false).GetBytes(page));
        var line = $"{outPath}: {count} storylets from {files.Count} files";
        Console.WriteLine(line);
        using var tty = new StreamWriter(Console.OpenStandardOutput());
        tty.WriteLine(line);
        return 0;
    }
}
