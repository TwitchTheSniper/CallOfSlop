namespace CallOfSlop;

/// <summary>
/// The Sandbox inventory's categories (UiSandboxParts.cs; docs/sandbox/CONSTRUCTS.md, "The inventory"), made from the
/// loaded data, never from a list of part names: every part goes in a group by its role (Structure, Wheels and
/// drivetrain, Seats, Joints, Logic, Power, Weapons, ...) and a section in it (its role; logic parts by what kind of
/// logic they are). A role this file doesn't know yet (a new role from another branch) still lands somewhere sensible:
/// by a word in its name (power, battery, gun, turret, ...) or else in "Other", under its own name. Blueprints are
/// grouped by what they hold (vehicles, machines with joints, the rest), tools by whether they are in the game yet.
/// Built once per set of loaded files; the window only reads it.
/// </summary>
public sealed class SandboxCatalogue
{
    /// <summary>A section: a header in the grid and a leaf of the tree, holding items (part ids, blueprint or tool indices) sorted by name.</summary>
    public sealed class Section
    {
        public string Name = "", Label = "", Header = "";
        public int Group;
        public int[] Items = [];
    }

    /// <summary>A group: a branch of the tree, holding sections.</summary>
    public sealed class Group
    {
        public string Name = "", Label = "";
        public int[] Sections = [];
        public int Items;
    }

    /// <summary>One tab's tree: groups, then their sections (in the grid's order).</summary>
    public sealed class Tree
    {
        public Group[] Groups = [];
        public Section[] Sections = [];
        public int Items;
    }

    public Tree Parts = new(), Blueprints = new(), Tools = new();

    /// <summary>Each part's (by id) and blueprint's group and section names, for the search.</summary>
    public string[] PartGroup = [], PartSection = [], BlueprintSection = [];

    /// <summary>What it was made from (<see cref="SandboxTiles.Key"/>): made again when that changes.</summary>
    public int Key;

    /// <summary>The group order (the tree's), then any new group in the order met.</summary>
    public static readonly string[] GroupOrder = ["Structure", "Beams", "Panels", "Armour", "Shapes", "Wheels and drivetrain", "Seats", "Joints", "Logic", "Power", "Weapons", "Other"];

    /// <summary>
    /// A part's group and section (data first: the file's own "group" and "section", else its role, else a word in a new
    /// role's name, else "Other"). A "group" without a "section" goes in a section named like the group.
    /// </summary>
    public static (string Group, string Section) Classify(ConstructPartDef d)
    {
        if (d.Group.Length > 0)
            return (d.Group, d.Section.Length > 0 ? d.Section : d.Group);
        switch (d.Role)
        {
            case PartRole.Frame: return ("Structure", "Frames");
            case PartRole.Panel: return ("Structure", "Panels");
            case PartRole.Foundation: return ("Structure", "Foundations");
            case PartRole.Door: return ("Structure", "Doors");
            case PartRole.Wheel: return ("Wheels and drivetrain", "Wheels");
            case PartRole.Suspension: return ("Wheels and drivetrain", "Suspension");
            case PartRole.Engine: return ("Wheels and drivetrain", "Engines");
            case PartRole.FuelTank: return ("Wheels and drivetrain", "Fuel tanks");
            case PartRole.FuelLine: return ("Wheels and drivetrain", "Fuel lines");
            case PartRole.Transmission: return ("Wheels and drivetrain", "Transmissions");
            case PartRole.Differential: return ("Wheels and drivetrain", "Differentials");
            case PartRole.Seat: return ("Seats", "Seats");
            case PartRole.Hinge: return ("Joints", "Hinges");
            case PartRole.Joint: return ("Joints", "Moving joints");
            case PartRole.Hitch: return ("Joints", "Hitches and chains");
            case PartRole.Battery: return ("Power", "Batteries");
            case PartRole.Alternator: return ("Power", "Alternators");
            case PartRole.Gun: return ("Weapons", "Guns");
            case PartRole.GunPart: return ("Weapons", "Gun parts");
            case PartRole.AmmoBox: return ("Weapons", "Ammo boxes");
            case PartRole.FeedChute: return ("Weapons", "Feed chutes");
            case PartRole.Logic:
                return ("Logic", d.Logic switch
                {
                    CLogic.Button or CLogic.Lever or CLogic.Switch or CLogic.Plate or CLogic.Reader or CLogic.Selector => "Controls",
                    CLogic.Speed or CLogic.Proximity => "Sensors",
                    CLogic.FuelGauge or CLogic.ChargeGauge => "Gauges",
                    CLogic.And or CLogic.Or or CLogic.Not or CLogic.Compare or CLogic.Scale or CLogic.Toggle => "Gates",
                    CLogic.Timer or CLogic.Delay => "Timing",
                    CLogic.Lamp or CLogic.Horn or CLogic.Brake => "Outputs",
                    _ => "More logic",
                });
        }
        // A role from a newer branch: its name says where it goes.
        var role = d.Role.ToString();
        var name = Words(role);
        if (Has(role, "power", "battery", "generator", "solar", "cable", "capacitor", "fuse", "motor", "electric"))
            return ("Power", name);
        if (Has(role, "weapon", "gun", "turret", "cannon", "launcher", "ammo", "magazine", "mount", "barrel"))
            return ("Weapons", name);
        if (Has(role, "salvage", "storage", "crate", "container", "inventory"))
            return ("Storage", name);
        if (Has(role, "armour", "armor", "plate", "beam", "block", "wall", "frame"))
            return ("Structure", name);
        if (Has(role, "wheel", "track", "drive", "axle", "engine", "fuel", "gear"))
            return ("Wheels and drivetrain", name);
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
            partItems.Add((g, s, d.Group.Length > 0 ? GivenOrder(d) : (int)d.Role * 64 + (d.Role == PartRole.Logic ? LogicOrder(s) : 0), d.Name, d.Id));
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

    /// <summary>
    /// The order of a part that names its own group and section: the files' order (so a folder's sections come in the
    /// order of its subfolders, Structure/Beams/10-Square before 20-I-beams), the top folder's parts after the subfolders'.
    /// </summary>
    private static int GivenOrder(ConstructPartDef d) => 10_000 + (d.Folder.Length == 0 ? ConstructParts.MaxTypes : 0) + d.Id;

    private static int LogicOrder(string section) => section switch
    {
        "Controls" => 0,
        "Sensors" => 1,
        "Gates" => 2,
        "Timing" => 3,
        "Outputs" => 4,
        _ => 5,
    };

    private static Tree MakeTree(List<(string Group, string Section, int Order, string Name, int Id)> items, string what)
    {
        // Groups in the fixed order (new ones after), sections in their first item's order, items by name.
        var groups = items.Select(t => t.Group).Distinct()
            .OrderBy(g => Array.IndexOf(GroupOrder, g) is var k and >= 0 ? k : GroupOrder.Length)
            .ThenBy(g => items.First(t => t.Group == g).Order).ToList();
        var tree = new Tree { Items = items.Count };
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
                var ids = sec.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => t.Id).ToArray();
                sections.Add(new Section
                {
                    Name = sec.Key,
                    Group = gi,
                    Items = ids,
                    Label = $"{sec.Key} ({ids.Length})##sbsec{what}{sections.Count}",
                    Header = names.Count > 1 && g != sec.Key ? $"{g}: {sec.Key}" : sec.Key,
                });
            }
            groupList.Add(new Group
            {
                Name = g,
                Label = $"{g} ({mine.Count})##sbgrp{what}{gi}",
                Sections = Enumerable.Range(first, sections.Count - first).ToArray(),
                Items = mine.Count,
            });
        }
        tree.Groups = groupList.ToArray();
        tree.Sections = sections.ToArray();
        return tree;
    }
}
