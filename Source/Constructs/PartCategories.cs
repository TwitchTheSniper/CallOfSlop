using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The Tab inventory's categories (Constructs/Categories.json; docs/sandbox/CONSTRUCTS.md, "The inventory"): the one place
/// that says where a part goes. A part's file only says what it is (its role, name, and the "group" and "section" its
/// generator gave it, which here are clues, not the menu); the file's rules turn that into a category by function and a
/// section. The first rule that matches wins. A "given" rule keeps a part whose own group is a category's name, the way a
/// file places itself when no rule can decide. A bad file is refused with a reason (<see cref="Problem"/>;
/// <c>--check-construct-parts</c> prints it) and every part goes by the role words in SandboxCatalogue.cs.
/// </summary>
public sealed class PartCategories
{
    public const string Format = "call-of-slop-categories/1";

    private sealed class Rule
    {
        public string[] Groups = [], Sections = [], Names = [];
        public PartRole[] Roles = [];
        public CLogic[] Logic = [];
        public bool Given;
        public string Category = "", Section = "";
    }

    /// <summary>The categories in the tree's order, each with its sections in order.</summary>
    public (string Name, string[] Sections)[] Categories = [];

    public string Problem = "";

    private Rule[] _rules = [];

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Categories.json");

    private static PartCategories? _current;

    /// <summary>The table in use (loaded once; an empty one when the file is missing or refused).</summary>
    public static PartCategories Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (null: load the file again).</summary>
    public static void Use(PartCategories? t) => _current = t;

    public static PartCategories Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PartCategories { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    /// <summary>Where a part goes by the rules; false when none places it.</summary>
    public bool Place(ConstructPartDef d, out string category, out string section)
    {
        foreach (var r in _rules)
        {
            if (r.Given)
            {
                if (d.Group.Length == 0 || IndexOf(d.Group) < 0)
                    continue;
                category = d.Group;
                section = d.Section.Length > 0 ? d.Section : d.Group;
                return true;
            }
            if ((r.Groups.Length > 0 && Array.IndexOf(r.Groups, d.Group) < 0)
                || (r.Sections.Length > 0 && Array.IndexOf(r.Sections, d.Section) < 0)
                || (r.Roles.Length > 0 && Array.IndexOf(r.Roles, d.Role) < 0)
                || (r.Logic.Length > 0 && (d.Role != PartRole.Logic || Array.IndexOf(r.Logic, d.Logic) < 0))
                || (r.Names.Length > 0 && !HasWord(d.Name, r.Names)))
                continue;
            category = r.Category;
            section = r.Section;
            return true;
        }
        category = section = "";
        return false;
    }

    /// <summary>A category's place in the tree, or -1.</summary>
    public int IndexOf(string category)
    {
        for (var i = 0; i < Categories.Length; i++)
            if (Categories[i].Name == category)
                return i;
        return -1;
    }

    /// <summary>A section's place in its category; a section the file doesn't list comes after the ones it does.</summary>
    public int SectionOrder(string category, string section)
    {
        var c = IndexOf(category);
        var at = c < 0 ? -1 : Array.IndexOf(Categories[c].Sections, section);
        return at < 0 ? int.MaxValue / 2 : at;
    }

    private static bool HasWord(string name, string[] words)
    {
        foreach (var w in words)
            if (name.Contains(w, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static PartCategories Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "categories" or "rules"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            if (!root.TryGetProperty("categories", out var cats) || cats.ValueKind != JsonValueKind.Array || cats.GetArrayLength() == 0
                || !root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
                throw new FormatException("\"categories\" and \"rules\" are lists");
            var t = new PartCategories();
            var list = new List<(string, string[])>();
            foreach (var c in cats.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String || !c.TryGetProperty("sections", out var secs))
                    throw new FormatException("a category has a name and sections");
                var name = n.GetString()!;
                if (name.Length is 0 or > 32 || list.Any(x => x.Item1 == name))
                    throw new FormatException($"category \"{name}\": empty, over 32 characters or twice");
                list.Add((name, Strings(secs, name)));
            }
            t.Categories = list.ToArray();
            var made = new List<Rule>();
            foreach (var r in rules.EnumerateArray())
                made.Add(ParseRule(r, t));
            t._rules = made.ToArray();
            return t;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return new PartCategories { Problem = ex.Message };
        }
    }

    private static Rule ParseRule(JsonElement e, PartCategories t)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new FormatException("a rule is an object");
        var r = new Rule();
        var hasTo = false;
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "group": r.Groups = Strings(p.Value, p.Name); break;
                case "section": r.Sections = Strings(p.Value, p.Name); break;
                case "name": r.Names = Strings(p.Value, p.Name); break;
                case "role": r.Roles = Strings(p.Value, p.Name).Select(Enums<PartRole>).ToArray(); break;
                case "logic": r.Logic = Strings(p.Value, p.Name).Select(Enums<CLogic>).ToArray(); break;
                case "given": r.Given = p.Value.ValueKind == JsonValueKind.True; break;
                case "to":
                    var to = Strings(p.Value, p.Name);
                    if (to.Length != 2)
                        throw new FormatException("\"to\" is [category, section]");
                    (r.Category, r.Section) = (to[0], to[1]);
                    var c = t.IndexOf(r.Category);
                    if (c < 0 || Array.IndexOf(t.Categories[c].Sections, r.Section) < 0)
                        throw new FormatException($"a rule goes to \"{r.Category}\" > \"{r.Section}\", which \"categories\" doesn't list");
                    hasTo = true;
                    break;
                default: throw new FormatException($"unknown rule key \"{p.Name}\"");
            }
        if (hasTo == r.Given)
            throw new FormatException("a rule has either \"to\" or \"given\": true");
        return r;
    }

    private static T Enums<T>(string s) where T : struct, Enum =>
        Enum.TryParse<T>(s, true, out var v) && Enum.IsDefined(v) && !int.TryParse(s, out _) ? v : throw new FormatException($"no {typeof(T).Name} \"{s}\"");

    /// <summary>A string or a list of strings.</summary>
    private static string[] Strings(JsonElement e, string what)
    {
        if (e.ValueKind == JsonValueKind.String)
            return [e.GetString()!];
        if (e.ValueKind != JsonValueKind.Array || e.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
            throw new FormatException($"\"{what}\" is a string or a list of strings");
        return e.EnumerateArray().Select(x => x.GetString()!).ToArray();
    }
}
