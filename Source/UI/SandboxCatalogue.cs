namespace CallOfSlop;

/// <summary>
/// The Sandbox inventory's categories (UiSandboxParts.cs; docs/sandbox/CONSTRUCTS.md, "The inventory"), made from the
/// loaded data, never from a list of part names: every part goes in a category by what it does and a section in it, by the
/// rules of Constructs/Categories.json (<see cref="PartCategories"/>). Size is not a category: the window filters by a
/// part's size class, so each group and section keeps its count per size. A role the rules don't know yet (a new role from
/// another branch) still lands somewhere sensible: by a word in its name (power, battery, gun, turret, ...) or else in
/// "Other", under its own name. Blueprints are grouped by what they hold (vehicles, machines with joints, the rest), tools
/// by whether they are in the game yet. Built once per set of loaded files; the window only reads it.
/// </summary>
public sealed class SandboxCatalogue
{
    /// <summary>The window's size filter: 0 every size, then each size class + 1 (<see cref="ConstructParts.ClassNames"/>).</summary>
    public const int SizeViews = 5;

    /// <summary>A section: a header in the grid and a leaf of the tree, holding items (part ids, blueprint or tool indices): parts by size class, tier (so a machine's line reads in order), then name with its numbers in order; the rest by name.</summary>
    public sealed class Section
    {
        public string Name = "", Label = "", Header = "";
        public int Group;
        public int[] Items = [];
        public string[] Labels = []; // per size view: the tree's row, its count for that size
        public int[] Counts = []; // per size view: how many it holds
    }

    /// <summary>A group: a branch of the tree, holding sections.</summary>
    public sealed class Group
    {
        public string Name = "", Label = "";
        public int[] Sections = [];
        public int Items;
        public string[] Labels = []; // per size view, as a section's
        public int[] Counts = [];
    }

    /// <summary>One tab's tree: groups, then their sections (in the grid's order).</summary>
    public sealed class Tree
    {
        public Group[] Groups = [];
        public Section[] Sections = [];
        public int Items;
        public int[] Counts = []; // per size view
    }

    public Tree Parts = new(), Blueprints = new(), Tools = new();

    /// <summary>Each part's (by id) and blueprint's group and section names, for the search.</summary>
    public string[] PartGroup = [], PartSection = [], BlueprintSection = [];

    /// <summary>What it was made from (<see cref="SandboxTiles.Key"/>): made again when that changes.</summary>
    public int Key;

    /// <summary>
    /// A part's category and section: by the rules of Constructs/Categories.json, else (a role they don't know, or no rules)
    /// by a word in the role's name, else "Other".
    /// </summary>
    public static (string Group, string Section) Classify(ConstructPartDef d)
    {
        if (PartCategories.Current.Place(d, out var group, out var section))
            return (group, section);
        var role = d.Role.ToString();
        var name = Words(role);
        if (Has(role, "power", "battery", "generator", "solar", "cable", "capacitor", "fuse", "motor", "electric"))
            return ("Power and Fuel", name);
        if (Has(role, "weapon", "gun", "turret", "cannon", "launcher", "ammo", "magazine", "mount", "barrel"))
            return ("Weapons", name);
        if (Has(role, "salvage", "storage", "crate", "container", "inventory"))
            return ("Storage and Logistics", name);
        if (Has(role, "armour", "armor", "plate", "beam", "block", "wall", "frame"))
            return ("Structure", name);
        if (Has(role, "wheel", "track", "drive", "axle", "engine", "fuel", "gear"))
            return ("Drivetrain and Engines", name);
        return ("Other", byte.TryParse(role, out _) ? "Other parts" : name);
    }

    /// <summary>A blueprint's section: vehicles (a seat and wheels), machines (joints), or structures.</summary>
    public static string ClassifyBlueprint(ConstructBuild.Blueprint bp)
    {
        bool seat = false, wheel = false, joint = false, weapon = false;
        foreach (var step in bp.Steps)
        {
            if (ConstructParts.Find(step.Part) is not { } d)
                continue;
            seat |= d.Role == PartRole.Seat;
            wheel |= d.Role == PartRole.Wheel;
            joint |= d.HasJoint;
            weapon |= Classify(d).Group == "Weapons";
        }
        return weapon ? "Armed" : seat && wheel ? "Vehicles" : joint ? "Machines" : "Structures";
    }

    private static bool Has(string role, params string[] words)
    {
        foreach (var w in words)
            if (role.Contains(w, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>"FuelTank" -> "Fuel tanks" (a role name as a section's name).</summary>
    private static string Words(string role)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < role.Length; i++)
        {
            if (i > 0 && char.IsUpper(role[i]))
                sb.Append(' ').Append(char.ToLowerInvariant(role[i]));
            else
                sb.Append(role[i]);
        }
        var s = sb.ToString();
        return s.EndsWith('s') ? s : s + "s";
    }

    /// <summary>The catalogue of the loaded parts, blueprints and tools.</summary>
    public static SandboxCatalogue Make()
    {
        var cat = new SandboxCatalogue { Key = SandboxTiles.Key() };
        var parts = ConstructParts.All;
        cat.PartGroup = new string[parts.Count];
        cat.PartSection = new string[parts.Count];
        var partItems = new List<(string Group, string Section, int Order, string Name, int Id)>();
        foreach (var d in parts)
        {
            var (g, s) = Classify(d);
            cat.PartGroup[d.Id] = g;
            cat.PartSection[d.Id] = s;
            if (!d.Offered)
                continue; // (replaced by a routed link: the linking tool makes pipes and shafts now; or a wall's damage stage)
            partItems.Add((g, s, PartCategories.Current.SectionOrder(g, s), d.Name, d.Id));
        }
        cat.Parts = MakeTree(partItems, "part");

        var bps = ConstructBuild.Blueprints;
        cat.BlueprintSection = new string[bps.Count];
        var bpItems = new List<(string, string, int, string, int)>();
        for (var i = 0; i < bps.Count; i++)
        {
            var s = ClassifyBlueprint(bps[i]);
            cat.BlueprintSection[i] = s;
            bpItems.Add((s, s, s switch { "Vehicles" => 0, "Machines" => 1, "Armed" => 2, _ => 3 }, bps[i].Name, i));
        }
        cat.Blueprints = MakeTree(bpItems, "blueprint");

        var tools = ConstructTools.All;
        var toolItems = new List<(string, string, int, string, int)>();
        for (var i = 0; i < tools.Count; i++)
            toolItems.Add((tools[i].Tool != null ? "In the game" : "Still to come", tools[i].Tool != null ? "In the game" : "Still to come", tools[i].Tool != null ? 0 : 1, tools[i].Name, i));
        cat.Tools = MakeTree(toolItems, "tool");
        return cat;
    }

    /// <summary>Names with their numbers in order: "Large Deck 4x4 m" before "Large Deck 16x8 m", "0.5 m" before "2 m".</summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var x = Number(a, ref i);
                var y = Number(b, ref j);
                if (x != y)
                    return x.CompareTo(y);
                continue;
            }
            var c = char.ToLowerInvariant(a[i++]).CompareTo(char.ToLowerInvariant(b[j++]));
            if (c != 0)
                return c;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    private static double Number(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || (s[i] == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1]))))
            i++;
        return double.Parse(s.AsSpan(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The size views an item counts in: every view for a blueprint or tool, "all" and its size class for a part.</summary>
    private static bool InView(string what, int id, int view) =>
        what != "part" || view == 0 || ConstructParts.All[id].Class == view - 1;

    private static int[] CountViews(string what, IEnumerable<int> ids)
    {
        var counts = new int[SizeViews];
        foreach (var id in ids)
            for (var v = 0; v < SizeViews; v++)
                counts[v] += InView(what, id, v) ? 1 : 0;
        return counts;
    }

    private static Tree MakeTree(List<(string Group, string Section, int Order, string Name, int Id)> items, string what)
    {
        // Categories in the file's order (new ones after, Other last), sections in their given order.
        var table = PartCategories.Current;
        var groups = items.Select(t => t.Group).Distinct()
            .OrderBy(g => what != "part" ? 0 : g == "Other" ? int.MaxValue : table.IndexOf(g) is var k and >= 0 ? k : table.Categories.Length)
            .ThenBy(g => items.First(t => t.Group == g).Order).ToList();
        var tree = new Tree { Items = items.Count, Counts = CountViews(what, items.Select(t => t.Id)) };
        var sections = new List<Section>();
        var groupList = new List<Group>();
        foreach (var g in groups)
        {
            var gi = groupList.Count;
            var mine = items.Where(t => t.Group == g).ToList();
            var names = mine.GroupBy(t => t.Section).OrderBy(x => x.Min(t => t.Order)).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var first = sections.Count;
            foreach (var sec in names)
            {
                var ids = (what == "part"
                    ? sec.OrderBy(t => ConstructParts.All[t.Id].Class).ThenBy(t => ConstructParts.All[t.Id].Tier).ThenBy(t => t.Name, Comparer<string>.Create(NaturalCompare))
                    : sec.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)).Select(t => t.Id).ToArray();
                var n = sections.Count;
                var counts = CountViews(what, ids);
                sections.Add(new Section
                {
                    Name = sec.Key,
                    Group = gi,
                    Items = ids,
                    Label = $"{sec.Key} ({ids.Length})##sbsec{what}{n}",
                    Header = names.Count > 1 && g != sec.Key ? $"{g}: {sec.Key}" : sec.Key,
                    Counts = counts,
                    Labels = counts.Select(c => $"{sec.Key} ({c})##sbsec{what}{n}").ToArray(),
                });
            }
            var groupCounts = CountViews(what, mine.Select(t => t.Id));
            groupList.Add(new Group
            {
                Name = g,
                Label = $"{g} ({mine.Count})##sbgrp{what}{gi}",
                Sections = Enumerable.Range(first, sections.Count - first).ToArray(),
                Items = mine.Count,
                Counts = groupCounts,
                Labels = groupCounts.Select(c => $"{g} ({c})##sbgrp{what}{gi}").ToArray(),
            });
        }
        tree.Groups = groupList.ToArray();
        tree.Sections = sections.ToArray();
        return tree;
    }
}
