using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Part of the "Props" section: the outdoor prop folders (Props/Vehicles, Rooftop, Utility, Signage, Urban) one by one:
/// count, unique names, every file valid and in its folder, the kinds of props the brief asked for, health in the band for
/// its material (the material health guide in Props/README.md) and in the right order between materials, sane bounds,
/// nothing sunk into the ground, floating or upside down, sensible collision; and the "Prop Gallery 2" map that shows them.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The outdoor prop folders: the fewest props each should hold and props that must be there.</summary>
    private static readonly (string Folder, int Min, string[] Want)[] OutdoorPropFolders =
    [
        ("Vehicles", 20, ["Hatchback", "Sedan", "Pickup Truck", "Van", "SUV", "Taxi", "Police Car", "Burnt Out Car", "Car On Bricks", "Bus",
                          "Box Truck", "Motorbike", "Bicycle", "Moped Scooter", "Forklift", "Golf Cart"]),
        ("Rooftop", 18, ["Window AC Unit", "Rooftop Condenser", "Split AC Unit", "Mushroom Vent", "Air Duct", "Air Duct Elbow", "Exhaust Fan",
                         "Brick Chimney", "Water Tower", "Satellite Dish", "TV Antenna", "Solar Panel"]),
        ("Utility", 18, ["Utility Pole", "Pad Transformer", "Junction Box", "Manhole Cover", "Storm Drain", "Fire Escape Stairs",
                         "Fire Escape Landing", "Extension Ladder", "Scaffold Tower"]),
        ("Signage", 15, ["Traffic Light", "Parking Meter", "Bus Stop Sign", "Billboard", "Shop Sign"]),
        ("Urban", 18, ["Bus Shelter", "Phone Booth", "Newsstand", "Newspaper Box", "ATM"]),
    ];

    /// <summary>
    /// Health bands by break sound, which stands for the material (Props/README.md, "Material health guide"): glass and paper
    /// break at a touch, plastic and cardboard ("soft") are low, wood medium, metal higher (thin sheet to heavy steel).
    /// </summary>
    private static readonly (PropBreakSound Sound, int Min, int Max)[] PropHealthBands =
    [
        (PropBreakSound.Glass, 1, 25), (PropBreakSound.Soft, 1, 60), (PropBreakSound.Wood, 30, 150), (PropBreakSound.Metal, 60, 450),
    ];

    private static void CheckOutdoorProps()
    {
        var all = Props.All;
        var mine = new List<PropDef>();
        foreach (var (folder, min, want) in OutdoorPropFolders)
        {
            var list = all.Where(p => p.Category.Equals(folder, StringComparison.OrdinalIgnoreCase)).ToList();
            mine.AddRange(list);
            var problems = new List<string>();
            if (list.Count < min)
                problems.Add($"only {list.Count} props (at least {min} expected)");
            if (list.Select(p => p.Name.ToLowerInvariant()).Distinct().Count() != list.Count)
                problems.Add("two props share a name");
            foreach (var p in list)
            {
                var src = (p.SourceFile ?? "").Replace('\\', '/');
                if (!src.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{p.Name} isn't from Props/{folder} ({p.SourceFile})");
                var b = p.Bounds;
                var size = b.Half * 2;
                if (size.X < 0.05f || size.Y < 0.005f || size.Z < 0.05f || size.X > PropFile.MaxSize || size.Y > PropFile.MaxSize || size.Z > PropFile.MaxSize)
                    problems.Add($"{p.Name}: odd size {size}");
                if (b.Min.Y < -0.05f)
                    problems.Add($"{p.Name}: parts {-b.Min.Y:0.00} m below the ground");
                if (b.Min.Y > 0.03f)
                    problems.Add($"{p.Name}: floats {b.Min.Y:0.00} m above the ground");
                // Upside down: the model's volume sits high, as if flipped (a sawhorse with its legs up once did).
                var vol = 0f;
                var cy = 0f;
                foreach (var part in p.Parts)
                {
                    var v = part.Size.X * part.Size.Y * part.Size.Z;
                    vol += v;
                    cy += v * part.Offset.Y;
                }
                var centre = vol > 0 ? (cy / vol - b.Min.Y) / Math.Max(size.Y, 1e-4f) : 0;
                if (centre > 0.9f)
                    problems.Add($"{p.Name}: top-heavy (volume centre at {centre:0.00} of its height), upside down?");
                // Collision: solid props of real size block; anything a player couldn't sensibly bump into is cosmetic.
                if (p.IsSolid && p.Colliders.Any(c => c.Max.Y - c.Min.Y < 0.02f))
                    problems.Add($"{p.Name}: a collider box with no height");
                if (!p.IsSolid && size.Y > 2.7f)
                    problems.Add($"{p.Name}: cosmetic but {size.Y:0.0} m tall");
                if (p.Breakable is { } br)
                {
                    var band = PropHealthBands.First(x => x.Sound == br.Sound);
                    if (br.Health < band.Min || br.Health > band.Max)
                        problems.Add($"{p.Name}: {br.Health} hp is outside the {br.Sound} band {band.Min}-{band.Max}");
                    if (br.DebrisCount <= 0)
                        problems.Add($"{p.Name}: breaks into nothing");
                }
            }
            foreach (var w in want)
                if (list.All(p => !p.Name.Equals(w, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"no '{w}'");
            Line($"Props/{folder}: {list.Count} props ({list.Count(p => p.IsSolid)} solid, {list.Count(p => !p.IsSolid)} cosmetic, "
                 + $"{list.Count(p => p.Breakable != null)} breakable, {list.Count(p => p.IsSolid && p.Breakable == null)} unbreakable cover), "
                 + $"{list.Sum(p => p.Parts.Count)} parts, tallest {(list.Count > 0 ? list.Max(p => p.Bounds.Half.Y * 2) : 0):0.0} m: {(problems.Count == 0 ? "ok" : $"{problems.Count} problem(s)")}");
            foreach (var pr in problems.Take(8))
                Fail($"Props/{folder}: {pr}");
        }

        // Health by material: each material's median sits in order (glass <= soft <= wood <= metal) across these folders.
        var medians = new List<(PropBreakSound Sound, float Median, int Count)>();
        foreach (var (sound, _, _) in PropHealthBands)
        {
            var hp = mine.Where(p => p.Breakable?.Sound == sound).Select(p => p.Breakable!.Health).Order().ToArray();
            if (hp.Length > 0)
                medians.Add((sound, hp.Length % 2 == 1 ? hp[hp.Length / 2] : (hp[hp.Length / 2 - 1] + hp[hp.Length / 2]) / 2f, hp.Length));
        }
        Line("outdoor health by material (median hp): " + string.Join(", ", medians.Select(m => $"{m.Sound} {m.Median:0} ({m.Count})"))
             + $"; {mine.Count(p => p.Breakable == null)} unbreakable");
        for (var i = 1; i < medians.Count; i++)
            if (medians[i].Median < medians[i - 1].Median)
                Fail($"outdoor props: {medians[i].Sound} props break easier (median {medians[i].Median:0} hp) than {medians[i - 1].Sound} ones ({medians[i - 1].Median:0} hp)");
        // Vehicles and the big structures are cover you can rely on: unbreakable.
        foreach (var n in new[] { "Sedan", "Bus", "Box Truck", "Water Tower", "Utility Pole", "Billboard", "Bus Shelter" })
            if (Props.Find(n) is { Breakable: not null })
                Fail($"{n} should be unbreakable cover");

        // Prop Gallery 2 shows exactly the outdoor props, each once.
        if (Maps.Find("Prop Gallery 2") is not { } g)
        {
            Fail("Maps/Dev/PropGallery2.json ('Prop Gallery 2') didn't load");
            return;
        }
        var shown = g.PropInstances.Select(p => p.Def).ToList();
        var missing = mine.Where(p => !shown.Contains(p)).Select(p => p.Name).ToList();
        var extra = shown.Where(p => !mine.Contains(p)).Select(p => p.Name).Distinct().ToList();
        Line($"Prop Gallery 2: {shown.Count} props on {g.Name} ({g.Pack?.Name} pack), {missing.Count} outdoor props missing, {extra.Count} others, "
             + $"{shown.Count - shown.Distinct().Count()} twice");
        if (missing.Count > 0 || extra.Count > 0 || shown.Count != shown.Distinct().Count())
            Fail($"Prop Gallery 2 should show every outdoor prop once (missing: {string.Join(", ", missing.Take(5))}; others: {string.Join(", ", extra.Take(5))})");
        if (g.Pack?.Name != "Dev")
            Fail("Prop Gallery 2 isn't in the Dev pack");
        // Every prop in the library is on some Dev map, so each can be checked by eye.
        var devShown = Maps.All.Where(m => m.Pack?.Name == "Dev").SelectMany(m => m.PropInstances.Select(p => p.Def)).ToHashSet();
        var nowhere = all.Where(p => !devShown.Contains(p)).Select(p => p.Name).ToList();
        if (nowhere.Count > 0)
            Fail($"{nowhere.Count} props aren't shown on any Dev map: {string.Join(", ", nowhere.Take(6))}");
    }
}
