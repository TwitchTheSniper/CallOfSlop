using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Military props" section: the reusable military library in Props/Military (category "Military") and
/// Props/Military/Vehicles (category "Military Vehicles"): every published name is there and from that folder, sane
/// bounds, simple collision on the big vehicles, cover you can trust (vehicles and guns unbreakable), the few props that
/// explode have a fuse, the hulls and wings players are meant to vault onto are low enough, the transport plane's hold is
/// walkable, and the "Military Props" Dev map shows each prop once with every spawn reachable and no nav node inside a prop.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The published names (Props/Military/README.md); map packs place these, so they must not go missing.</summary>
    private static readonly string[] MilitaryPropNames =
    [
        "Sandbag Wall Short", "Sandbag Wall Long", "Sandbag Corner", "Sandbag Nest Ring", "Sandbag Pile", "Barbed Wire Coil",
        "Barbed Wire Fence Line", "Steel Hedgehog", "Concrete Tank Trap", "Wooden Stake Barrier", "Field Cannon", "Anti-Tank Gun",
        "Howitzer", "Mortar Pit", "Mounted Machine Gun", "Ammo Crate Wooden", "Stacked Ammo Crates", "Ammo Case Metal",
        "Steel Ammo Can", "Shell Crate", "Rifle Crate", "Artillery Shell Stack", "Spent Shell Casings", "Military Pallet",
        "Pallet of Ammo Crates", "Camo Net Canopy", "Field Radio Pack", "Field Tent", "Command Tent", "Fuel Drum Rack",
        "Military Fuel Drum", "Jerrycan Rack", "Field Searchlight", "Guard Tower",
        "Medium Tank (WWII)", "Medium Tank (WWII) Desert", "Heavy Tank", "Main Battle Tank", "Main Battle Tank Desert",
        "Main Battle Tank Winter", "Light Tank", "Wrecked Tank", "Fighter Plane (WWII)", "Twin-Engine Bomber", "Jet Fighter",
        "Transport Plane", "Light Utility Jeep", "Light Utility Jeep Desert", "Light Utility Jeep Winter", "Covered Cargo Truck",
        "Covered Cargo Truck Desert", "Covered Cargo Truck Winter", "Armoured Car", "Armoured Car Desert", "Fuel Tanker Truck",
        "Sidecar Motorcycle", "Landmine", "Mine Warning Sign",
    ];

    /// <summary>The military props that go off when broken (each with a fuse, so players get a moment to run).</summary>
    private static readonly string[] MilitaryExplosives = ["Fuel Drum Rack", "Military Fuel Drum", "Jerrycan Rack", "Shell Crate", "Landmine"];

    /// <summary>Props whose main collider (the hull, the wing) players should be able to mantle onto.</summary>
    private static readonly string[] MilitaryClimbable =
        ["Medium Tank (WWII)", "Heavy Tank", "Main Battle Tank", "Light Tank", "Wrecked Tank", "Light Utility Jeep", "Fighter Plane (WWII)", "Jet Fighter"];

    private static void CheckMilitaryProps()
    {
        var mine = Props.All.Where(p => p.Pack == null && (p.Category == "Military" || p.Category == "Military Vehicles")).ToList();
        var problems = new List<string>();
        foreach (var n in MilitaryPropNames)
            if (mine.All(p => p.Name != n))
                problems.Add($"no '{n}'");
        foreach (var p in mine)
        {
            var src = (p.SourceFile ?? "").Replace('\\', '/');
            var folder = p.Category == "Military" ? "Military/" : "Military/Vehicles/";
            if (!src.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || (p.Category == "Military" && src.StartsWith("Military/Vehicles/", StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{p.Name} ({p.Category}) isn't from Props/{folder} ({p.SourceFile})");
            var b = p.Bounds;
            var size = b.Max - b.Min;
            if (b.Min.Y < -0.03f || b.Min.Y > 0.03f)
                problems.Add($"{p.Name}: lowest part at {b.Min.Y:0.00} m (should stand on the ground)");
            if (size.X > PropFile.MaxSize || size.Y > PropFile.MaxSize || size.Z > PropFile.MaxSize)
                problems.Add($"{p.Name}: {size} is bigger than {PropFile.MaxSize} m");
            if (p.IsSolid && (p.Colliders.Length == 0 || p.Colliders.Any(c => c.Max.Y - c.Min.Y < 0.02f)))
                problems.Add($"{p.Name}: solid without sensible colliders");
            if (p.Category == "Military Vehicles")
            {
                if (!p.IsSolid || p.Breakable != null)
                    problems.Add($"{p.Name}: vehicles are solid, unbreakable cover");
                if (p.Colliders.Length > 8 || (p.Colliders.Length > 3 && size.X < 12 && size.Z < 12))
                    problems.Add($"{p.Name}: {p.Colliders.Length} collider boxes (keep vehicles to a few)");
            }
            if (p.Breakable is { } br)
            {
                var band = PropHealthBands.First(x => x.Sound == br.Sound);
                if (br.Health < (p.IsSolid ? band.Min : 1) || br.Health > band.Max)
                    problems.Add($"{p.Name}: {br.Health} hp is outside the {br.Sound} band {band.Min}-{band.Max}");
                if (br.DebrisCount <= 0)
                    problems.Add($"{p.Name}: breaks into nothing");
                var shouldExplode = MilitaryExplosives.Contains(p.Name);
                if ((br.ExplodeRadius > 0) != shouldExplode || (shouldExplode && br.ExplodeFuse <= 0))
                    problems.Add($"{p.Name}: explode radius {br.ExplodeRadius}, fuse {br.ExplodeFuse} (only {string.Join(", ", MilitaryExplosives)} go off, with a fuse)");
            }
            else if (MilitaryExplosives.Contains(p.Name))
                problems.Add($"{p.Name} should explode");
        }
        foreach (var n in new[] { "Field Cannon", "Howitzer", "Anti-Tank Gun", "Guard Tower", "Sandbag Wall Long", "Concrete Tank Trap" })
            if (Props.Find(n) is { Breakable: not null })
                problems.Add($"{n} should be unbreakable cover");
        foreach (var n in MilitaryClimbable)
            if (Props.Find(n) is { } p && p.Colliders.Min(c => c.Max.Y) > World.MantleReach + 0.01f)
                problems.Add($"{n}: lowest collider top {p.Colliders.Min(c => c.Max.Y):0.00} m is above the mantle reach ({World.MantleReach} m)");
        // The transport plane's hold: a floor you can step onto, and room to stand under its roof.
        if (Props.Find("Transport Plane") is { } tp)
        {
            var floor = tp.Colliders.OrderBy(c => c.Max.Y).First();
            var roof = tp.Colliders.Where(c => c.Min.Y > floor.Max.Y && c.Min.X < 0 && c.Max.X > 0 && c.Min.Z < 0 && c.Max.Z > 0).OrderBy(c => c.Min.Y).FirstOrDefault();
            if (floor.Max.Y > World.StepHeight || roof.Max.Y <= 0 || roof.Min.Y - floor.Max.Y < World.PlayerHeight + 0.1f)
                problems.Add($"Transport Plane: hold floor at {floor.Max.Y:0.00} m, roof at {roof.Min.Y:0.00} m (should step in and stand up)");
        }
        var vehicles = mine.Where(p => p.Category == "Military Vehicles").ToList();
        Line($"Props/Military: {mine.Count} props ({mine.Count - vehicles.Count} Military, {vehicles.Count} Military Vehicles): "
             + $"{mine.Count(p => p.IsSolid)} solid, {mine.Count(p => !p.IsSolid)} cosmetic, {mine.Count(p => p.Breakable != null)} breakable "
             + $"({mine.Count(p => p.Breakable is { ExplodeRadius: > 0 })} explosive), {mine.Count(p => p.Parts.Any(q => q.Anim != null))} animated; "
             + $"{mine.Sum(p => p.Parts.Count)} parts; vehicles {vehicles.Sum(p => p.Parts.Count) * 12} triangles in all, "
             + $"at most {(vehicles.Count > 0 ? vehicles.Max(p => p.Parts.Count) * 12 : 0)} and {(vehicles.Count > 0 ? vehicles.Max(p => p.Colliders.Length) : 0)} collider boxes each; "
             + $"largest {mine.OrderByDescending(p => (p.Bounds.Max - p.Bounds.Min).Length()).Select(p => $"{p.Name} {p.Bounds.Max.X - p.Bounds.Min.X:0.0} x {p.Bounds.Max.Y - p.Bounds.Min.Y:0.0} x {p.Bounds.Max.Z - p.Bounds.Min.Z:0.0} m").FirstOrDefault()}: "
             + (problems.Count == 0 ? "ok" : $"{problems.Count} problem(s)"));
        foreach (var pr in problems.Take(10))
            Fail("Props/Military: " + pr);

        // The gallery: every military prop once, in the Dev pack, reachable spawns, no nav node inside a prop.
        if (Maps.Find("Military Props") is not { } map)
        {
            Fail("Maps/Dev/MilitaryProps.json ('Military Props') didn't load");
            return;
        }
        var shown = map.PropInstances.Select(p => p.Def).ToList();
        var missing = mine.Where(p => !shown.Contains(p)).Select(p => p.Name).ToList();
        if (map.Pack?.Name != "Dev" || missing.Count > 0 || shown.Count != shown.Distinct().Count() || shown.Any(p => !mine.Contains(p)))
            Fail($"Military Props should be a Dev map showing every military prop once (pack {map.Pack?.Name}, missing: {string.Join(", ", missing.Take(5))}; {shown.Count} shown)");
        World.SetMap(map);
        var g = NavGraph.Build(map);
        var inside = g.Nodes.Count(n => map.PropInstances.Any(p => p.Def.IsSolid && Enumerable.Range(p.SolidStart, p.SolidCount).Any(k =>
        {
            var s = map.Solid[k];
            return n.X > s.Min.X + 0.05f && n.X < s.Max.X - 0.05f && n.Z > s.Min.Z + 0.05f && n.Z < s.Max.Z - 0.05f && n.Y < s.Max.Y - 0.05f && n.Y + World.PlayerHeight > s.Min.Y;
        })));
        var search = new NavSearch();
        var path = new int[512];
        int pairs = 0, unreachable = 0;
        foreach (var s0 in map.Spawns)
            foreach (var s1 in map.Spawns)
            {
                if (s0 == s1)
                    continue;
                pairs++;
                if (search.Find(g, g.Nearest(s0), g.Nearest(s1, needSight: false), path) == 0)
                    unreachable++;
            }
        var batches = PropBatches.Build(map, PropMeshes.Build(Props.All));
        Line($"Military Props map: {map.PropInstances.Count} props, {map.PropInstances.Sum(p => p.SolidCount)} collider boxes, "
             + $"{map.PropInstances.Sum(p => p.Def.Parts.Count) * 12} triangles, {batches.Chunks.Length} instanced draws of {batches.Types} types; "
             + $"nav {g.Nodes.Length} nodes, {inside} inside a prop, {unreachable} of {pairs} spawn pairs unreachable");
        if (inside > 0)
            Fail($"Military Props: {inside} nav nodes inside solid props");
        if (unreachable > 0)
            Fail($"Military Props: {unreachable} spawn pairs have no path");
    }
}
