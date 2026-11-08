using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// The vehicle armoury (docs/vehiclekit/VEHICLEKIT.md, "The armoury"): gun recipes for the kit's turrets as data, in <c>Constructs/VehicleArmoury.json</c>. Each recipe is a gun in the weapon
/// builder's own text, fitted through the game's build rules as any style's gun is (<see cref="Vehicle.FitGun"/>), so it is a gun a player could build: single receivers, merged NxN
/// receivers (a 2x2 of rifle receivers is one receiver twice the size, with twice-size gun parts: damage x4, fire rate / 2), splitters for multi-barrel guns, shells, harpoons and fuel.
/// A recipe carries its class and least tier, job tags, a mount ("pintle", "shield", "twin") and, by faction, a weight and the faction's own name for it. <see cref="FactionStyle.Weapons"/>
/// is the style's own list plus every recipe its faction names, each as many times as its weight, so every pick the kit makes (the grammar's, a role's, a generator's) draws from both.
/// </summary>
public static class VehicleArmoury
{
    public const string Format = "call-of-slop-vehicle-armoury/1";

    /// <summary>The mounts a recipe may ask for.</summary>
    public static readonly string[] Mounts = ["pintle", "shield", "twin"];

    /// <summary>The tags a recipe may carry.</summary>
    public static readonly string[] KnownTags = ["light", "medium", "heavy", "boss", "aa", "anti-armour", "close", "suppress", "flame", "oddity"];

    /// <summary>Off: <c>--no-armoury</c>, the styles' own guns only, to measure what the armoury changes.</summary>
    public static bool On = true;

    /// <summary>One recipe as the file has it.</summary>
    public sealed record Recipe(string Id, string Build, int Merge, string? Box, GunClass? Class, int Tier, string[] Tags, string Mount, string[] Inspired, string About,
        IReadOnlyDictionary<string, (int Weight, string Name)> Factions);

    private static IReadOnlyList<Recipe>? _recipes;
    private static string _problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "VehicleArmoury.json");

    /// <summary>The recipes in use (loaded once; none, with <see cref="Problem"/> saying why, when the file is missing or refused).</summary>
    public static IReadOnlyList<Recipe> Recipes
    {
        get
        {
            if (_recipes != null)
                return _recipes;
            try
            {
                _recipes = Parse(File.ReadAllText(FilePath), Path.GetFileName(FilePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                _problem = ex.Message;
                Console.WriteLine($"[vehicles] the armoury was refused: {ex.Message}");
                _recipes = [];
            }
            return _recipes;
        }
    }

    /// <summary>Why the file was refused ("" when it loaded).</summary>
    public static string Problem
    {
        get
        {
            _ = Recipes;
            return _problem;
        }
    }

    /// <summary>Reads an armoury file; a bad one throws <see cref="FormatException"/> with the recipe and the reason.</summary>
    public static List<Recipe> Parse(string json, string file)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"{file}: \"format\" must be \"{Format}\"");
        var list = new List<Recipe>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var styles = Styles.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var r in root.GetProperty("recipes").EnumerateArray())
        {
            var id = r.GetProperty("id").GetString() ?? "";
            string Where() => $"{file}: recipe \"{id}\"";
            if (id.Length == 0 || !ids.Add(id))
                throw new FormatException($"{Where()}: an id is required and unique");
            var build = r.GetProperty("build").GetString() ?? "";
            var (receiver, _) = WeaponOption.Parse(build);
            if (Parts.Find(receiver) is not { Category: PartCategory.Receiver })
                throw new FormatException($"{Where()}: \"{receiver}\" is not a weapon builder receiver");
            var merge = r.TryGetProperty("merge", out var m) ? m.GetInt32() : 1;
            if (merge < 1 || merge > 3)
                throw new FormatException($"{Where()}: \"merge\" is 1 (a single receiver), 2 or 3 (a 2x2 or 3x3 square)");
            string? box = "Ammo Box";
            if (r.TryGetProperty("box", out var b))
                box = b.ValueKind == JsonValueKind.Null ? null : b.GetString();
            GunClass? cls = null;
            if (r.TryGetProperty("class", out var c))
                cls = Enum.TryParse<GunClass>(c.GetString(), true, out var gc) && gc != GunClass.None ? gc : throw new FormatException($"{Where()}: \"class\" is light, medium or heavy");
            var tier = r.TryGetProperty("tier", out var t) ? t.GetInt32() : 1;
            if (tier < 1 || tier > 4)
                throw new FormatException($"{Where()}: \"tier\" is 1 to 4");
            var tags = r.TryGetProperty("tags", out var tg) ? tg.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [];
            if (tags.FirstOrDefault(x => !KnownTags.Contains(x)) is { } badTag)
                throw new FormatException($"{Where()}: unknown tag \"{badTag}\" ({string.Join(", ", KnownTags)})");
            var mount = r.TryGetProperty("mount", out var mt) ? mt.GetString() ?? "pintle" : "pintle";
            if (!Mounts.Contains(mount))
                throw new FormatException($"{Where()}: \"mount\" is one of {string.Join(", ", Mounts)}");
            if (mount == "twin" && merge > 1)
                throw new FormatException($"{Where()}: a twin mount is two single receivers");
            var inspired = r.TryGetProperty("inspired", out var ins) ? ins.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [];
            var about = r.TryGetProperty("about", out var ab) ? ab.GetString() ?? "" : "";
            var factions = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
            foreach (var p in r.GetProperty("factions").EnumerateObject())
            {
                if (!styles.Contains(p.Name))
                    throw new FormatException($"{Where()}: no style named \"{p.Name}\" ({string.Join(", ", styles)})");
                if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() != 2 || p.Value[0].GetInt32() is var w && w < 1 || w > 6)
                    throw new FormatException($"{Where()}: factions.{p.Name} is [weight 1 to 6, \"the faction's name for it\"]");
                var name = p.Value[1].GetString() ?? "";
                if (name.Length == 0 || name.Any(ch => ch > 126))
                    throw new FormatException($"{Where()}: factions.{p.Name}: a name in ASCII is required (players read it)");
                factions[p.Name] = (w, name);
            }
            list.Add(new Recipe(id, build, merge, box, cls, tier, tags, mount, inspired, about, factions));
        }
        return list;
    }

    /// <summary>A recipe as a faction's gun: its name for it and everything the kit needs to fit and weigh it.</summary>
    public static WeaponOption Option(Recipe r, string name) =>
        new(name, r.Build, r.Box) { Merge = r.Merge, Mount = r.Mount, Recipe = r.Id, Class = r.Class, Tier = r.Tier, Tags = r.Tags };

    /// <summary>The style's guns: its own, then each recipe its faction names, as many times as its weight (a name twice is twice as likely, as everywhere in a style).</summary>
    public static WeaponOption[] Arm(FactionStyle st, WeaponOption[] own)
    {
        var list = new List<WeaponOption>(own);
        foreach (var r in Recipes)
            if (r.Factions.TryGetValue(st.Name, out var f))
            {
                var o = Option(r, f.Name);
                for (var k = 0; k < f.Weight; k++)
                    list.Add(o);
            }
        return list.ToArray();
    }

    /// <summary>The tier a vehicle made without a role is armed for: the bandits' rung of the ladder (zombies 1, bandits 2, military 3, high tech 4).</summary>
    public const int PlainTier = 2;

    /// <summary>One of <paramref name="list"/> for a vehicle with no role: weighted by <see cref="TierWeight"/> at <see cref="PlainTier"/>, one roll as <see cref="Dice.Pick"/> takes.</summary>
    public static WeaponOption Pick(Dice d, IReadOnlyList<WeaponOption> list)
    {
        var total = 0.0;
        foreach (var w in list)
            total += TierWeight(w, PlainTier);
        var roll = d.Next() * total;
        foreach (var w in list)
            if ((roll -= TierWeight(w, PlainTier)) < 0)
                return w;
        return list[^1];
    }

    /// <summary>The list in a seeded order for a vehicle with no role, heavier weights first more often (a weighted draw without replacement).</summary>
    public static List<WeaponOption> Order(Dice d, IReadOnlyList<WeaponOption> list)
    {
        var left = list.ToList();
        var order = new List<WeaponOption>(left.Count);
        while (left.Count > 0)
        {
            var pick = Pick(d, left);
            order.Add(pick);
            left.Remove(pick);
        }
        return order;
    }

    /// <summary>A gun's weight for a vehicle of <paramref name="tier"/>: a recipe below its least tier is rare (a tenth), at or past it as listed.</summary>
    public static double TierWeight(WeaponOption w, int tier) => w.Tier > tier ? 0.1 : 1;
}

/// <summary>
/// The armoury's commands: <c>--check-armoury [--recipe ID]</c> fits every recipe on a test turret through the game's rules and round-trips it through a blueprint, printing
/// its numbers; <c>--armoury-report [--seeds N] [--from S] [--style S] [--shape NAME] [--check] [--no-armoury]</c> counts the distinct guns each style's vehicles carry.
/// </summary>
public static class VehicleArmouryCli
{
    public static int CheckArmoury(string[] args)
    {
        var only = Flag(args, "--recipe");
        if (VehicleArmoury.Problem.Length > 0)
        {
            Console.WriteLine($"armoury: refused: {VehicleArmoury.Problem}");
            return 1;
        }
        var fails = 0;
        Console.WriteLine($"armoury: {VehicleArmoury.Recipes.Count} recipe(s) in {VehicleArmoury.FilePath}");
        Console.WriteLine($"  {"recipe",-26} {"N",2} {"mount",-6} {"class",-6} {"t",1} {"dmg",5} {"rpm",5} {"dps",5} {"shots",5} {"range",5} {"mag",4} {"kg",5} {"reach",5}  factions");
        foreach (var r in VehicleArmoury.Recipes.Where(r => only == null || r.Id.Equals(only, StringComparison.OrdinalIgnoreCase)))
        {
            var w = VehicleArmoury.Option(r, r.Id);
            try
            {
                var v = new Vehicle("Armoury Stand", Styles.Militia, 1);
                var root = v.Root("Plate Steel 1 m", new Vector3(0, 2f, 0), Orient.Facing(Vector3.UnitY));
                v.Turret(root, w);
                var c = v.Live;
                var gun = -1;
                for (var g = 0; g < c.GunCount; g++)
                    if (c.Guns[g].Part == v.Gun!.Index)
                        gun = g;
                if (gun < 0 || c.Guns[gun].Def is not { } def)
                    throw new VehicleKitException(gun < 0 ? "no gun came of it" : c.Guns[gun].Problem);
                var cg = c.Guns[gun];
                if (r.Merge > 1 && cg.MergeN != r.Merge)
                    throw new VehicleKitException($"its receivers didn't merge into a {r.Merge}x{r.Merge} (merge N {cg.MergeN})");
                var guns = c.GunCount;
                var text = v.Text();
                var bp = ConstructBuild.ParseBlueprint(text, "armoury.json");
                var world = new ConstructWorld();
                var back = ConstructBuild.Build(world, bp, Vector3.Zero, Quaternion.Identity, out var why) ?? throw new VehicleKitException($"its blueprint won't build back: {why}");
                back.Rebuild();
                var backGun = Enumerable.Range(0, back.GunCount).Select(g => back.Guns[g]).FirstOrDefault(g => g.MergeN == cg.MergeN && g.Def != null);
                if (back.GunCount != guns || backGun.Def == null || backGun.Def.Damage != def.Damage || backGun.Interval != cg.Interval)
                    throw new VehicleKitException($"its blueprint builds back another gun ({back.GunCount} guns against {guns})");
                // And as a player's Save as blueprint writes it (ConstructClip): a merged gun's centre parts go in by their "merged" socket names.
                var clipText = ConstructClip.Of(c, "armoury").ToBlueprint("armoury", "", out var clipWhy) ?? throw new VehicleKitException($"Save as blueprint refuses it: {clipWhy}");
                var saved = ConstructBuild.Build(new ConstructWorld(), ConstructBuild.ParseBlueprint(clipText, "saved.json"), Vector3.Zero, Quaternion.Identity, out var savedWhy)
                    ?? throw new VehicleKitException($"its saved blueprint won't build back: {savedWhy}");
                saved.Rebuild();
                if (saved.GunCount != guns || !Enumerable.Range(0, saved.GunCount).Any(g => saved.Guns[g].MergeN == cg.MergeN && saved.Guns[g].Def?.Damage == def.Damage && saved.Guns[g].Interval == cg.Interval))
                    throw new VehicleKitException($"its saved blueprint builds back another gun ({saved.GunCount} guns against {guns})");
                var cls = VehicleRoles.ClassOf(w);
                var mass = 0f;
                for (var i = 0; i < c.PartCount; i++)
                    if (c.Parts[i].Seg == c.Parts[v.Gun.Index].Seg)
                        mass += c.PartMass(i);
                var shield = v.Parts.Any(x => x.Name == "Plate Steel 0.5 m");
                var firing = Enumerable.Range(0, c.GunCount).Count(g => c.Guns[g].Def != null && c.Guns[g].Problem.Length == 0);
                if (firing != (r.Mount == "twin" ? 2 : 1))
                    throw new VehicleKitException($"{firing} gun(s) fire on it");
                var rpm = cg.Interval == 0 ? 0 : 60f * World.TickRate / cg.Interval;
                Console.WriteLine($"  {r.Id,-26} {r.Merge,2} {(r.Mount == "shield" && !shield ? "(shld)" : r.Mount),-6} {cls,-6} {r.Tier,1} {def.Damage * Math.Max(1, cg.Shots),5} {rpm,5:0} {def.Damage * Math.Max(1, cg.Shots) * rpm / 60f,5:0} {cg.Shots,5} {cg.MaxRange,5:0} {cg.Mag,4} {mass,5:0} {v.GunReach(w),5:0.00}  {string.Join(", ", r.Factions.Select(x => $"{x.Key} {x.Value.Weight} \"{x.Value.Name}\""))}");
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                fails++;
                Console.WriteLine($"  {r.Id,-26} FAIL: {ex.Message}");
            }
        }
        Console.WriteLine($"armoury: {fails} recipe(s) failed; (shld): a shield mount whose plate found no room on a turret with its seat on the arm (a room's turret, whose seat is inside, has more)");
        return fails;
    }

    /// <summary>Distinct guns by style and shape over seeds, the guns a vehicle, generation time, and with <c>--check</c> the full check's pass rate.</summary>
    public static int Report(string[] args)
    {
        if (args.Any(a => a.Equals("--no-armoury", StringComparison.OrdinalIgnoreCase)))
            VehicleArmoury.On = false;
        var seeds = VehicleQualityCli.SeedsFrom(args, 10);
        var check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        var styleOnly = Flag(args, "--style");
        var shapeOnly = Flag(args, "--shape");
        var shapes = shapeOnly != null ? [shapeOnly] : (string[])["Box Truck", "War Bus", "Flatbed Fort", "Gun Wagon", "Scrap Tanker"];
        try
        {
            VehicleDiversity.Generate(VehicleRegistry.Find("Bandit Box Truck")!, 100); // (one throwaway, so the first seed timed doesn't carry the JIT)
        }
        catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
        {
        }
        Console.WriteLine($"armoury report: {(VehicleArmoury.On ? "with" : "WITHOUT")} the armoury, seeds {seeds.FirstOrDefault()} to {seeds.LastOrDefault()}{(check ? ", with the full check" : "")}");
        Console.WriteLine($"  {"style",-9} {"shape",-13} {"made",4} {"guns",5} {"names",5} {"builds",6} {"mixed",5} {"merged",6} {"heavy",5} {"ms",6} {(check ? "check" : ""),5}  guns seen");
        int made = 0, failed = 0, passed = 0, checkedN = 0;
        var times = new List<double>();
        var byStyle = new Dictionary<string, (HashSet<string> Names, HashSet<string> Builds)>();
        var failures = new List<string>();
        foreach (var st in Styles.All.Where(s => styleOnly == null || s.Name.Equals(styleOnly, StringComparison.OrdinalIgnoreCase)))
            foreach (var shape in shapes)
            {
                if (VehicleRegistry.Find($"{st.Name} {shape}") is not { } g)
                    continue;
                var names = new HashSet<string>();
                var builds = new HashSet<string>();
                int vehicles = 0, guns = 0, mixed = 0, merged = 0, heavy = 0, ok = 0;
                var ms = new List<double>();
                foreach (var seed in seeds)
                {
                    try
                    {
                        var (_, v, text, t) = VehicleDiversity.Generate(g, seed);
                        ms.Add(t);
                        vehicles++;
                        var here = new HashSet<string>();
                        foreach (var (_, _, w) in v.Turrets)
                        {
                            guns++;
                            names.Add(w.Name);
                            builds.Add(Key(w));
                            here.Add(Key(w));
                            merged += w.Merge > 1 ? 1 : 0;
                            heavy += VehicleRoles.ClassOf(w) == GunClass.Heavy ? 1 : 0;
                        }
                        mixed += here.Count > 1 ? 1 : 0;
                        if (check)
                        {
                            checkedN++;
                            var rep = VehicleChecks.Check(text, $"{seed}.json", g.Checks, v.Rooms);
                            if (rep.Passed)
                            {
                                ok++;
                                passed++;
                            }
                            else
                                failures.Add($"{g.Name} {seed}: {rep.Failures[0]}");
                        }
                    }
                    catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
                    {
                        failed++;
                        failures.Add($"{g.Name} {seed}: generation: {ex.Message}");
                    }
                }
                made += vehicles;
                times.AddRange(ms);
                if (!byStyle.TryGetValue(st.Name, out var all))
                    byStyle[st.Name] = all = (new(), new());
                all.Names.UnionWith(names);
                all.Builds.UnionWith(builds);
                Console.WriteLine($"  {st.Name,-9} {shape,-13} {vehicles,4} {guns,5} {names.Count,5} {builds.Count,6} {mixed,5} {merged,6} {heavy,5} {ms.DefaultIfEmpty().Average(),6:0} {(check ? $"{ok}/{vehicles}" : ""),5}  {string.Join("; ", names.Order())}");
            }
        foreach (var (style, all) in byStyle)
            Console.WriteLine($"  style {style}: {all.Names.Count} gun names, {all.Builds.Count} distinct guns over every shape");
        foreach (var f in failures)
            Console.WriteLine($"  FAIL {f}");
        Console.WriteLine($"armoury report: {made} made, {failed} failed to generate, {times.DefaultIfEmpty().Average():0} ms a generation (worst {times.DefaultIfEmpty().Max():0}){(check ? $", full check passed {passed}/{checkedN}" : "")}");
        return 0;
    }

    private static string? Flag(string[] args, string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));

    /// <summary>What makes two guns different: their parts, merge and mount (the name is the faction's).</summary>
    public static string Key(WeaponOption w) => $"{w.Build}|{w.Merge}|{w.Mount}";
}
