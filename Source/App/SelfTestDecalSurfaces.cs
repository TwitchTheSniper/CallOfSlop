using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The flat-surface and object decals (Decals/Ground, Road, Wall, Objects, Ceiling), see Decals/README.md.</summary>
    private static readonly string[] SurfaceDecalNames =
    [
        // Ground
        "Puddle", "Mud Patch", "Dirt Patch", "Leaf Litter", "Gravel Scatter", "Sand Drift", "Asphalt Patch", "Skid Marks", "Tyre Tracks", "Footprints",
        "Paint Spill", "Coffee Stain", "Rust Stain", "Dry Grass", "Moss Patch", "Concrete Patch", "Expansion Joint", "Floor Drain", "Cracked Tiles", "Carpet Stain",
        // Road
        "Lane Dash", "Crosswalk Bars", "Stop Line", "Parking Bay", "Stencil Number 1", "Stencil Number 2", "Stencil Number 3", "Turn Arrow",
        "Yellow Kerb Paint", "Double Yellow Lines", "Hazard Chevrons", "Loading Bay Box", "Helipad", "Court Centre Circle", "Court Key",
        // Wall
        "Water Damage", "Rust Bleed", "Peeling Paint", "Mould", "Wall Cracks", "Soot Plume", "Stencil Arrow", "Exit Sign", "Poster Circles", "Poster Diagonal",
        "Graffiti Throw-Up", "Graffiti Burst", "Graffiti Scribble", "Chalk Marks", "Handprints", "Vent Grille", "Electrical Panel", "Brick Patch", "Plaster Patch",
        // Objects
        "Square Manhole Cover", "Utility Cover", "Round Sewer Grate", "Trench Grate", "Rectangular Rug", "Round Rug", "Doormat", "Paper Litter", "Cardboard Flat",
        "Cigarette Butts", "Broken Glass", "Leaf Pile", "Rubble Dust", "Oil Drips",
        // Ceiling
        "Ceiling Water Spot", "Ceiling Tile Stain", "Cobweb",
    ];

    /// <summary>
    /// The surface and object decal set: every one loads with a unique name and an explicit "surfaces" rule that suits its
    /// folder, no stroke is thinner than an atlas pixel, painting stays cheap, every placeable (non-impact) decal in the
    /// library is shown by a Decal op on a Dev decal map, and every single Decal op on the "Decal Surfaces" map lands.
    /// </summary>
    private static void CheckDecalSurfaces()
    {
        var all = Decals.All;
        var missing = SurfaceDecalNames.Where(n => Decals.Find(n) == null).ToList();
        if (missing.Count > 0)
            Fail($"surface decals that didn't load: {string.Join(", ", missing)}");
        if (SurfaceDecalNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != SurfaceDecalNames.Length)
            Fail("the surface decal list names a decal twice");
        var dupes = all.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            Fail($"decal names used twice: {string.Join(", ", dupes)}");
        var set = SurfaceDecalNames.Select(Decals.Find).Where(d => d != null).Select(d => d!).ToArray();
        if (set.Any(d => d.IsImpact))
            Fail("a surface or object decal has an \"impact\" section (impacts live in Decals/Impacts)");

        // Surfaces: written in every placeable decal's file (no silent "all"), and suiting the folder.
        var placeable = all.Where(d => !d.IsImpact).ToArray();
        var noRule = new List<string>();
        foreach (var d in placeable)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Decals.Folder, d.SourceFile)),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (!doc.RootElement.EnumerateObject().Any(p => p.Name.Equals("surfaces", StringComparison.OrdinalIgnoreCase)))
                    noRule.Add(d.Name);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Fail($"decal {d.Name}: can't re-read {d.SourceFile} ({ex.Message})");
            }
        }
        if (noRule.Count > 0)
            Fail($"decals without a \"surfaces\" rule (scatters could put them anywhere): {string.Join(", ", noRule)}");
        foreach (var d in set)
        {
            var need = d.Category switch
            {
                "Ground" or "Road" => DecalSurfaces.Floor,
                "Wall" => DecalSurfaces.Wall,
                "Ceiling" => DecalSurfaces.Ceiling,
                "Objects" => DecalSurfaces.Floor,
                _ => DecalSurfaces.None,
            };
            if (need == DecalSurfaces.None)
                Fail($"surface decal {d.Name} is in an unexpected folder ({d.SourceFile})");
            else if ((d.Surfaces & need) == 0)
                Fail($"{d.Category} decal {d.Name} can't go on its folder's surface ({d.Surfaces})");
            if (d.Category == "Objects" && d.Surfaces != DecalSurfaces.Floor)
                Fail($"object decal {d.Name} should be floors only (it's a thing lying on the ground), not {d.Surfaces}");
        }

        // Bold detail: the atlas is 128 px per decal, so a stroke or a rect side under ~1.2 px vanishes or shimmers.
        const float minFeature = 1.2f / DecalAtlas.LayerSize;
        var thin = new List<string>();
        foreach (var d in placeable)
            foreach (var s in d.Layers)
            {
                var size = s.Kind switch
                {
                    DecalShapeKind.Line or DecalShapeKind.Ring => s.Width,
                    DecalShapeKind.Rect or DecalShapeKind.Stripes => MathF.Min(s.Size.X, s.Size.Y),
                    DecalShapeKind.Circle => s.Radius * 2,
                    _ => 1f,
                };
                if (size < minFeature && !s.Erase)
                {
                    thin.Add($"{d.Name} ({s.Kind} {size * DecalAtlas.LayerSize:0.0} px)");
                    break;
                }
            }
        if (thin.Count > 0)
            Fail($"decal detail thinner than {minFeature * DecalAtlas.LayerSize:0.0} atlas pixels: {string.Join(", ", thin)}");

        // Paint cost, one decal at a time on one core (startup paints the whole library on all cores).
        var px = new Vector4[DecalAtlas.LayerSize * DecalAtlas.LayerSize];
        var layer = new byte[DecalAtlas.BytesPerLayer];
        if (set.Length > 0)
            DecalAtlas.Paint(set[0], px, layer); // warm up
        var total = Stopwatch.StartNew();
        var slowest = (Name: "", Ms: 0.0);
        foreach (var d in set)
        {
            var sw = Stopwatch.StartNew();
            DecalAtlas.Paint(d, px, layer);
            var ms = sw.Elapsed.TotalMilliseconds;
            if (ms > slowest.Ms)
                slowest = (d.Name, ms);
        }
        var totalMs = total.Elapsed.TotalMilliseconds;
        var layers = set.Sum(d => d.Layers.Count);
        DecalLine($"surface decals: {set.Length}/{SurfaceDecalNames.Length} loaded ({set.Count(d => d.Blend == DecalBlend.Multiply)} multiply), {layers} shape layers, "
                  + $"painted one by one in {totalMs:0.0} ms (slowest {slowest.Name} {slowest.Ms:0.00} ms); library {all.Length}/{DecalAtlas.MaxLayers} atlas layers");
        if (slowest.Ms > 60)
            Fail($"painting decal {slowest.Name} took {slowest.Ms:0} ms (budget 60 ms per decal)");
        if (totalMs > 1000)
            Fail($"painting the surface decals one by one took {totalMs:0} ms (budget 1000 ms)");
        if (all.Length > DecalAtlas.MaxLayers * 3 / 4)
            Fail($"{all.Length} decals: the atlas holds {DecalAtlas.MaxLayers}; leave room or raise MaxLayers (GL guarantees 256 array layers on 3.3, 2048 on 4.1)");

        CheckDecalSurfacesMap(placeable);
        Line("::notice title=Decal surfaces (results)::" + string.Join("%0A", DecalNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        DecalNotes.Clear();
    }

    private static void CheckDecalSurfacesMap(DecalDef[] placeable)
    {
        var map = Maps.Find("Decal Surfaces");
        if (map == null)
        {
            Fail("the \"Decal Surfaces\" map (Maps/Dev/DecalSurfaces.json) didn't load");
            return;
        }
        if (map.Pack?.Name != "Dev")
            Fail("Decal Surfaces isn't in the Dev pack");

        // Every placeable decal is shown on its own by a Decal op on one of the Dev decal maps.
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in new[] { map, Maps.Find("Decal Test") })
        {
            if (m == null)
                continue;
            foreach (var r in m.DecalRequests)
                if (!r.Scatter)
                    shown.UnionWith(r.Names);
        }
        var unshown = placeable.Where(d => !shown.Contains(d.Name)).Select(d => d.Name).ToList();
        if (unshown.Count > 0)
            Fail($"decals no Dev decal map shows (add a Decal op to Maps/Dev/DecalSurfaces.json): {string.Join(", ", unshown)}");

        // Every single Decal op lands on a surface its decal allows (the same placement the bake does).
        var boxes = new Box[map.Solid.Count];
        for (var i = 0; i < boxes.Length; i++)
            boxes[i] = map.IsDecalSurface(i) ? map.HashBox(i) : new Box(new Vector3(0, -1000f, 0), new Vector3(0.01f));
        var world = new PhysicsWorld(boxes, map.TerrainCollision, map.HalfX, map.HalfZ);
        var singles = 0;
        var lost = new List<string>();
        foreach (var r in map.DecalRequests.Where(r => !r.Scatter))
        {
            singles++;
            var def = Decals.Find(r.Names[0]);
            var rng = new DecalAtlas.Rng((uint)(r.Seed * 7919 + r.OpIndex * 104729 + 1));
            if (def == null || !DecalBaker.TryPlace(map, world, r, def, r.X0, r.Z0, r.Y, ref rng, out _))
                lost.Add($"{r.Names[0]} at ({r.X0:0.#}, {r.Y:0.#}, {r.Z0:0.#}) onto {r.Onto} (op #{r.OpIndex})");
        }
        if (lost.Count > 0)
            Fail($"Decal Surfaces: {lost.Count} Decal op(s) found no allowed surface: {string.Join("; ", lost.Take(8))}");

        var sw = Stopwatch.StartNew();
        var mesh = DecalBaker.Bake(map);
        var bakeMs = sw.Elapsed.TotalMilliseconds;
        DecalLine($"Decal Surfaces: {singles} single decals and {map.DecalRequests.Count(r => r.Scatter)} scatters asking for {mesh.Requested}: {mesh.Placed} placed, "
                  + $"{mesh.Missed} missed; {mesh.Triangles} triangles in {mesh.Chunks.Length} chunks, baked in {bakeMs:0.0} ms");
        if (mesh.Placed < mesh.Requested * 0.9 || mesh.Truncated)
            Fail($"Decal Surfaces: only {mesh.Placed}/{mesh.Requested} decals placed" + (mesh.Truncated ? " (vertex budget reached)" : ""));
        if (bakeMs > 1500)
            Fail($"baking the Decal Surfaces map's decals took {bakeMs:0} ms");
    }
}
