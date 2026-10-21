using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Part of the "Props" section: the indoor and everyday prop set (Props/Household, Office, Kitchen, Garden, Misc, made by
/// tools/propviz/gen_indoor.py). Per folder: the count, every file loaded, unique names, sane bounds, nothing sunk into
/// the ground or floating, solid props standing on a base (not upside down or on a point), and breakable health that
/// follows the material health guide in Props/README.md (glass lowest, then soft, then wood, then metal; bigger is
/// tougher). Then the "Prop Gallery 3" map: every one of these props once, a nav graph that reaches every spawn.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The indoor set's category folders and how many props each ships at least.</summary>
    private static readonly (string Folder, int Min)[] IndoorPropFolders =
        [("Household", 30), ("Office", 18), ("Kitchen", 19), ("Garden", 20), ("Misc", 22)];

    /// <summary>Heavy things that must stay unbreakable so the cover they give stays put.</summary>
    private static readonly string[] IndoorUnbreakable = ["Bathtub", "Fireplace", "Safe", "Bird Bath"];

    private static void CheckIndoorProps()
    {
        var all = Props.All;
        var mine = new List<PropDef>();
        foreach (var (folder, min) in IndoorPropFolders)
        {
            var props = all.Where(p => p.Category.Equals(folder, StringComparison.OrdinalIgnoreCase)).ToList();
            mine.AddRange(props);
            var dir = Path.Combine(Props.Folder, folder);
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Length : 0;
            var errors = Props.LoadErrors.Count(e => e.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                                     || e.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
            var problems = new List<string>();
            foreach (var p in props)
                problems.AddRange(IndoorPropProblems(p));
            Line($"{folder}: {props.Count} props from {files} files ({props.Count(p => p.IsSolid)} solid, {props.Count(p => p.Breakable != null)} breakable), "
                 + $"{errors} file errors, {problems.Count} shape problems");
            if (props.Count < min)
                Fail($"Props/{folder} has {props.Count} props (expected at least {min})");
            if (files != props.Count || errors > 0)
                Fail($"Props/{folder}: {files} files but {props.Count} props loaded ({errors} errors)");
            if (props.Select(p => p.Name.ToLowerInvariant()).Distinct().Count() != props.Count)
                Fail($"Props/{folder}: two props share a name");
            foreach (var problem in problems.Take(8))
                Fail(problem);
        }

        // Health by material: the break sound is the material class (glass/porcelain, soft = paper/cardboard/plastic/fabric, wood, metal).
        (PropBreakSound Sound, int Max)[] classes = [(PropBreakSound.Glass, 50), (PropBreakSound.Soft, 150), (PropBreakSound.Wood, 500), (PropBreakSound.Metal, 500)];
        var medians = new List<float>();
        var report = new List<string>();
        foreach (var (sound, max) in classes)
        {
            var list = mine.Where(p => p.Breakable?.Sound == sound).ToList();
            if (list.Count == 0)
            {
                Fail($"no indoor props break with the {sound} sound");
                medians.Add(0);
                continue;
            }
            var hp = list.Select(p => (float)p.Breakable!.Health).OrderBy(h => h).ToList();
            var median = hp[hp.Count / 2];
            medians.Add(median);
            // Bigger is tougher: props over 0.5 m^3 take more on average than ones under 0.05 m^3 of the same material.
            float Volume(PropDef p) => p.Bounds.Half.X * p.Bounds.Half.Y * p.Bounds.Half.Z * 8;
            var big = list.Where(p => Volume(p) >= 0.5f).Select(p => (float)p.Breakable!.Health).DefaultIfEmpty(0).Average();
            var small = list.Where(p => Volume(p) < 0.05f).Select(p => (float)p.Breakable!.Health).DefaultIfEmpty(0).Average();
            report.Add($"{sound} {list.Count} props, {hp[0]:0}-{hp[^1]:0} hp, median {median:0} (small {small:0}, big {big:0})");
            foreach (var p in list.Where(p => p.Breakable!.Health > max))
                Fail($"{p.Name}: {p.Breakable!.Health} hp is a lot for {sound.ToString().ToLowerInvariant()} (at most {max})");
            if (big > 0 && small > 0 && big <= small)
                Fail($"{sound} props: big ones ({big:0} hp on average) aren't tougher than small ones ({small:0})");
        }
        Line("health by material: " + string.Join("; ", report));
        for (var i = 1; i < medians.Count; i++)
            if (medians[i] < medians[i - 1])
                Fail($"health by material out of order: {classes[i].Sound} (median {medians[i]:0}) is weaker than {classes[i - 1].Sound} ({medians[i - 1]:0})");
        foreach (var name in IndoorUnbreakable)
            if (Props.Find(name) is not { } heavy || heavy.Breakable != null || !heavy.IsSolid)
                Fail($"'{name}' should be a solid, unbreakable prop");
        foreach (var want in new[] { "Rubber Duck", "Duck Decoy", "Mallard Duck", "Fridge", "Washing Machine", "Office Desk", "Server Rack", "Toilet", "Lawnmower", "Garden Gnome" })
            if (Props.Find(want) == null)
                Fail($"the indoor set has no '{want}'");

        CheckIndoorGallery(mine);
    }

    /// <summary>Shape problems of one prop: odd bounds, parts sunk into the ground, floating, solid props not standing on a base.</summary>
    private static IEnumerable<string> IndoorPropProblems(PropDef p)
    {
        var size = p.Bounds.Half * 2;
        if (size.X < 0.02f || size.Z < 0.02f || size.Y < 0.005f || size.X > 6f || size.Z > 6f || size.Y > 3.5f)
            yield return $"{p.Name}: odd size {size.X:0.00} x {size.Y:0.00} x {size.Z:0.00} m for an everyday prop";
        var lowest = p.Parts.Min(q => q.Bounds.Min.Y);
        if (lowest < -0.05f)
            yield return $"{p.Name}: a part reaches {-lowest:0.00} m below the ground";
        if (lowest > 0.02f)
            yield return $"{p.Name}: floats {lowest:0.00} m above the ground";
        if (!p.IsSolid)
            yield break;
        // A solid prop stands on a base: the parts touching the ground spread over at least a fifth of its width and depth
        // (an upside-down chair or a model balanced on a point fails this), and its bulk isn't up at the top.
        var ground = p.Parts.Where(q => q.Bounds.Min.Y < lowest + 0.05f).Select(q => q.Bounds).ToList();
        var gMin = ground.Aggregate(new Vector3(float.MaxValue), (m, b) => Vector3.Min(m, b.Min));
        var gMax = ground.Aggregate(new Vector3(float.MinValue), (m, b) => Vector3.Max(m, b.Max));
        var fx = (gMax.X - gMin.X) / size.X;
        var fz = (gMax.Z - gMin.Z) / size.Z;
        if (fx < 0.2f || fz < 0.2f)
            yield return $"{p.Name}: stands on {fx:P0} x {fz:P0} of its footprint (upside down or on a point?)";
        var volume = 0f;
        var weighted = 0f;
        foreach (var q in p.Parts)
        {
            var v = q.Size.X * q.Size.Y * q.Size.Z;
            volume += v;
            weighted += v * q.Offset.Y;
        }
        var com = (weighted / volume - p.Bounds.Min.Y) / size.Y;
        if (com > 0.8f)
            yield return $"{p.Name}: most of it is at the top ({com:P0} of its height), upside down?";
        if (p.Colliders.Any(c => c.Max.Y > p.Bounds.Max.Y + 0.1f || c.Min.Y < -0.001f))
            yield return $"{p.Name}: a collider sticks out above the model or below the ground";
    }

    private static void CheckIndoorGallery(List<PropDef> mine)
    {
        if (Maps.Find("Prop Gallery 3") is not { } map)
        {
            Fail("Maps/Dev/PropGallery3.json ('Prop Gallery 3') didn't load");
            return;
        }
        var shown = map.PropInstances.Select(p => p.Def).ToList();
        var missing = mine.Where(p => !shown.Contains(p)).Select(p => p.Name).ToList();
        var twice = shown.GroupBy(p => p).Count(g => g.Count() > 1);
        var others = shown.Count(p => !mine.Contains(p));
        World.SetMap(map);
        var g = NavGraph.Build(map);
        var inside = g.Nodes.Count(n => map.PropInstances.Any(p => p.Def.IsSolid && Enumerable.Range(p.SolidStart, p.SolidCount).Any(k =>
        {
            var s = map.Solid[k];
            return n.X > s.Min.X + 0.05f && n.X < s.Max.X - 0.05f && n.Z > s.Min.Z + 0.05f && n.Z < s.Max.Z - 0.05f && n.Y < s.Max.Y - 0.05f && n.Y + World.PlayerHeight > s.Min.Y;
        })));
        var search = new NavSearch();
        var path = new int[512];
        var unreachable = 0;
        foreach (var s0 in map.Spawns)
            foreach (var s1 in map.Spawns)
                if (s0 != s1 && search.Find(g, g.Nearest(s0), g.Nearest(s1, needSight: false), path) == 0)
                    unreachable++;
        var outside = map.PropInstances.Count(p => MathF.Abs(p.Position.X) > map.HalfX - 1 || MathF.Abs(p.Position.Z) > map.HalfZ - 1);
        var batches = PropBatches.Build(map, PropMeshes.Build(Props.All));
        Line($"Prop Gallery 3: {map.PropInstances.Count} props ({map.PropInstances.Count(p => p.Def.IsSolid)} solid), {missing.Count} of the set missing, {twice} twice, "
             + $"{others} from other folders; {batches.Chunks.Length} render chunks; nav graph {g.Nodes.Length} nodes, {inside} inside a prop, "
             + $"{unreachable} spawn pairs unreachable (it also plays the bot match in \"Matches\")");
        if (missing.Count > 0)
            Fail($"Prop Gallery 3 doesn't show {string.Join(", ", missing.Take(6))}");
        if (twice > 0 || others > 0)
            Fail($"Prop Gallery 3 shows {twice} props twice and {others} from other folders");
        if (inside > 0 || unreachable > 0)
            Fail($"Prop Gallery 3: {inside} nav nodes inside props, {unreachable} spawn pairs unreachable");
        if (outside > 0)
            Fail($"Prop Gallery 3: {outside} props at the map's edge");
        if (map.Pack?.Name != "Dev")
            Fail("Prop Gallery 3 isn't in the Dev pack");
    }
}
