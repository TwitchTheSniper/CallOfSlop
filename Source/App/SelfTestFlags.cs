using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Flag pool": the flag designs in Props/Flags/flags.json (US, UK, France, Finland) and the flag props that fly them
/// (FlagPool.cs, MapFlags.cs). Every design loads and stays inside its flag with the right aspect; each flag prop's
/// variants keep the prop's colliders and motion (stripes and crosses move with the cloth); random picks are
/// deterministic and roughly uniform; placement overrides and a map's "flagPool" list work; fingerprints never change;
/// older map packs fly no designs; and a flag costs a bounded number of vertices.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The designs that must ship, with their aspect (height / width).</summary>
    private static readonly (string Id, float Aspect)[] ShippedFlags = [("us", 10f / 19f), ("uk", 1f / 2f), ("france", 2f / 3f), ("finland", 11f / 18f)];

    /// <summary>A flag prop flying a design is at most this many vertices (the plain striped flag on a pole is 288).</summary>
    private const int MaxFlagVertices = 1800;

    private static readonly string[] FlagProps = ["Flag on a Pole", "Pennant Pole", "Hanging Banner"];

    private static void CheckFlagPool()
    {
        CheckFlagDesigns();
        CheckFlagFileErrors();
        CheckFlagVariants();
        CheckFlagPicks();
        CheckFlagMaps();
    }

    private static void CheckFlagDesigns()
    {
        var designs = FlagPool.Designs;
        foreach (var e in FlagPool.LoadErrors)
            Fail("flag file problem: " + e);
        Line($"flag pool: {designs.Length} designs from {FlagPool.FilePath}: "
             + string.Join(", ", designs.Select(d => $"{d.Id} ({d.Name}, {d.AspectHeight:0.##}:{d.AspectWidth:0.##}, {d.Shapes.Count} pieces in {d.Layers} layer(s))")));
        foreach (var (id, aspect) in ShippedFlags)
        {
            if (FlagPool.Find(id) is not { } d)
            {
                Fail($"the flag pool has no \"{id}\" flag");
                continue;
            }
            if (MathF.Abs(d.Aspect - aspect) > 1e-3f)
                Fail($"{id}: aspect {d.Aspect:0.000}, expected {aspect:0.000}");
            if (d.Shapes.Count == 0 || d.Layers < 1)
                Fail($"{id}: no pieces on the field");
            foreach (var s in d.Shapes)
            {
                var inside = new[] { s.U0, s.V0, s.U1, s.V1 }.All(v => v is >= 0 and <= 1) && (s.Kind == FlagShapeKind.Line || s.U1 > s.U0 && s.V1 > s.V0);
                var colour = new[] { s.Color.X, s.Color.Y, s.Color.Z }.All(c => c is >= 0 and <= 1);
                if (!inside || !colour || s.Layer < 1 || s.Layer > FlagPool.MaxLayers || s.Kind == FlagShapeKind.Line && s.Width <= 0)
                    Fail($"{id}: a piece isn't sane ({s})");
            }
        }
        // A few by eye in numbers: the US has 7 red stripes and 50-ish stars (28 simplified), the UK two saltires, Finland one cross.
        if (FlagPool.Find("us") is { } us && (us.Shapes.Count(s => s.Layer == 1) != 7 || us.Shapes.Count(s => s.Layer == 3) < 20))
            Fail($"us: {us.Shapes.Count(s => s.Layer == 1)} stripes and {us.Shapes.Count(s => s.Layer == 3)} stars (expected 7 and 20+)");
        if (FlagPool.Find("uk") is { } uk && uk.Shapes.Count(s => s.Kind == FlagShapeKind.Line) != 4)
            Fail("uk: expected 4 diagonal bands (white and red saltires)");
    }

    private static void CheckFlagFileErrors()
    {
        (string Json, string Expect)[] cases =
        [
            ("""{ "format": "call-of-slop-flags/1", "flags": [ { "id": "x", "aspect": [2, 3], "field": "red", "shapes": [ { "color": "blue", "rects": [[0, 0, 1.5, 1]] } ] } ] }""", "isn't inside the flag"),
            ("""{ "format": "call-of-slop-flags/1", "flags": [ { "id": "x", "aspect": [5, 1], "field": "red" } ] }""", "height / width"),
            ("""{ "format": "call-of-slop-flags/1", "flags": [ { "id": "x", "aspect": [2, 3], "field": "red", "colour": "blue" } ] }""", "unknown key"),
            ("""{ "format": "call-of-slop-flags/1", "flags": [ { "id": "Big Flag", "aspect": [2, 3], "field": "red" } ] }""", "lowercase"),
            ("""{ "format": "call-of-slop-flags/1", "flags": [ { "id": "x", "aspect": [2, 3], "field": "red", "shapes": [ { "color": "blue", "lines": [[0, 0, 1, 1]] } ] } ] }""", "need a \"width\""),
            ("""{ "format": "call-of-slop-flags/1", "flags": [ { "id": "x", "aspect": [2, 3], "field": "red" }, { "id": "x", "aspect": [2, 3], "field": "red" } ] }""", "already"),
            ("""{ "flags": [] }""", "format"),
        ];
        var ok = 0;
        foreach (var (json, expect) in cases)
        {
            var errors = new List<string>();
            FlagPool.Parse(json, "test.json", errors);
            if (errors.Any(e => e.Contains(expect, StringComparison.OrdinalIgnoreCase)))
                ok++;
            else
                Fail($"bad flag file didn't give an error with \"{expect}\": got [{string.Join(" | ", errors)}]");
        }
        // Prop files: a "flag" block naming a part that doesn't move, or an unknown default.
        (string Json, string Expect)[] props =
        [
            (FlagPropJson("""{ "cloth": [1] }"""), "needs an \"anim\""),
            (FlagPropJson("""{ "cloth": [2], "default": "Mars!" }"""), "\"default\""),
            (FlagPropJson("""{ "cloth": [9] }"""), "has 3 parts"),
            (FlagPropJson("""{ "segments": [2] }"""), "unknown key"),
        ];
        foreach (var (json, expect) in props)
        {
            try
            {
                PropFile.Load(json, "test.json");
                Fail($"bad \"flag\" block loaded (expected an error with \"{expect}\")");
            }
            catch (PropFileException ex) when (ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
            {
                ok++;
            }
            catch (PropFileException ex)
            {
                Fail($"bad \"flag\" block: expected \"{expect}\", got: {ex.Message}");
            }
        }
        Line($"flag file and \"flag\" block errors: {ok} of {cases.Length + props.Length} bad files rejected with a readable message");

        static string FlagPropJson(string flag) => $$"""
            { "format": "call-of-slop-prop/1", "name": "Test Flag", "collision": "cosmetic", "flag": {{flag}},
              "parts": [ { "size": [0.08, 3, 0.08], "offset": [0, 1.5, 0] },
                         { "size": [1, 0.6, 0.02], "offset": [0.5, 2.6, 0], "anim": { "type": "swing", "axis": "y", "pivot": [0, 2.6, 0], "angle": 10 } },
                         { "size": [1, 0.1, 0.025], "offset": [0.5, 2.6, 0], "color": "red", "anim": { "type": "follow", "parent": 2 } } ] }
            """;
    }

    private static void CheckFlagVariants()
    {
        var worst = 0;
        var lines = new List<string>();
        foreach (var name in FlagProps)
        {
            if (Props.Find(name) is not { FlagCloth: { } cloth } def)
            {
                Fail($"'{name}' has no \"flag\" block");
                continue;
            }
            var expectDefault = name == "Flag on a Pole" ? FlagPool.Random : FlagPool.None;
            if (cloth.Default != expectDefault)
                Fail($"'{name}': flag default \"{cloth.Default}\", expected \"{expectDefault}\"");
            var baseVerts = PropMeshes.Build([def]).Vertices.Length / PropMeshes.FloatsPerVertex;
            var owner = FlagPool.ClothOwners(def);
            var kept = def.Parts.Where((_, i) => owner[i] < 0).ToList();
            var perDesign = new List<string>();
            foreach (var design in FlagPool.Designs)
            {
                if (FlagPool.Variant(def, design.Id) is not { } v)
                {
                    Fail($"'{name}': couldn't build the {design.Id} flag");
                    continue;
                }
                var meshes = PropMeshes.Build([v]);
                var verts = meshes.Vertices.Length / PropMeshes.FloatsPerVertex;
                worst = Math.Max(worst, verts);
                perDesign.Add($"{design.Id} {verts}");
                if (verts != v.Parts.Count * 24 || meshes.Vertices.Any(f => !float.IsFinite(f)))
                    Fail($"'{name}' {design.Id}: mesh doesn't add up ({verts} vertices for {v.Parts.Count} parts, or a NaN)");
                if (verts > MaxFlagVertices)
                    Fail($"'{name}' {design.Id}: {verts} vertices, more than the budget of {MaxFlagVertices}");
                if (v.Name != def.Name || !ReferenceEquals(v.Colliders, def.Colliders) || v.Collision != def.Collision || v.Key == def.Key || v.FlagBase != def
                    || !ReferenceEquals(FlagPool.Variant(def, design.Id), v))
                    Fail($"'{name}' {design.Id}: the variant isn't the same prop with its own key (name, colliders, cache)");
                // The parts outside the cloth (pole, base, finial) are untouched and come first.
                for (var i = 0; i < kept.Count; i++)
                    if (v.Parts[i].Size != kept[i].Size || v.Parts[i].Offset != kept[i].Offset || v.Parts[i].Color != kept[i].Color)
                        Fail($"'{name}' {design.Id}: part #{i + 1} (not cloth) changed");
                // Each design colour is on the flag, every cloth piece moves with a swinging segment, and the flag keeps the design's shape.
                var cloth0 = kept.Count;
                var colours = v.Parts.Skip(cloth0).Select(p => p.Color).ToHashSet();
                if (!colours.Contains(design.Field) || design.Shapes.Any(s => !colours.Contains(s.Color)))
                    Fail($"'{name}' {design.Id}: a design colour is missing from the cloth");
                var segIdx = Enumerable.Range(cloth0, cloth.Segments.Length).ToHashSet();
                for (var i = cloth0; i < v.Parts.Count; i++)
                {
                    var a = v.Parts[i].Anim;
                    var ok = segIdx.Contains(i) ? a is { Type: PropAnimType.Swing } : a is { Type: PropAnimType.Follow } && segIdx.Contains(a.Parent);
                    if (!ok)
                        Fail($"'{name}' {design.Id}: cloth part #{i + 1} doesn't move with the cloth");
                }
                var lo = new Vector3(float.MaxValue);
                var hi = new Vector3(float.MinValue);
                foreach (var p in v.Parts.Skip(cloth0 + cloth.Segments.Length))
                {
                    lo = Vector3.Min(lo, p.Bounds.Min);
                    hi = Vector3.Max(hi, p.Bounds.Max);
                }
                for (var k = 0; k < cloth.Segments.Length; k++)
                {
                    var b = v.Parts[cloth0 + k].Bounds;
                    if (b.Half.X + b.Half.Y < 0.01f)
                        continue; // a speck: no design on this segment
                    lo = Vector3.Min(lo, b.Min);
                    hi = Vector3.Max(hi, b.Max);
                }
                var span = hi - lo;
                var width = MathF.Max(span.X, span.Z);
                var aspect = span.Y / width;
                if (MathF.Abs(aspect - design.Aspect) > 0.03f * design.Aspect)
                    Fail($"'{name}' {design.Id}: the flag is {width:0.00} x {span.Y:0.00} m (aspect {aspect:0.000}), expected the design's {design.Aspect:0.000}");
                var clothBounds = ClothBounds(def, owner);
                if (lo.X < clothBounds.Min.X - 0.02f || hi.X > clothBounds.Max.X + 0.02f || lo.Y < clothBounds.Min.Y - 0.02f || hi.Y > clothBounds.Max.Y + 0.02f)
                    Fail($"'{name}' {design.Id}: the flag reaches outside the prop's cloth");

                // Wind: a stripe vertex moves exactly like the cloth under it (follow = the segment's motion), and it does move.
                var anim = PropAnimData.Build(meshes);
                var seg = anim.PartRecord[0][cloth0];
                var stripe = anim.PartRecord[0][^1];
                var probe = v.Parts[^1].Offset;
                float moved = 0, differ = 0;
                for (var t = 0.1f; t < 3f; t += 0.37f)
                {
                    var ps = PropAnim.Animate(anim.Records, stripe, probe, t, 0.3f, 0f, 5f, anim.FreezeDistance[0], out _);
                    var pc = PropAnim.Animate(anim.Records, (int)anim.Records[stripe * PropAnimData.RecordFloats + 1], probe, t, 0.3f, 0f, 5f, anim.FreezeDistance[0], out _);
                    moved = MathF.Max(moved, Vector3.Distance(ps, probe));
                    differ = MathF.Max(differ, Vector3.Distance(ps, pc));
                }
                if (seg < 0 || stripe < 0 || moved < 0.005f || differ > 1e-4f)
                    Fail($"'{name}' {design.Id}: the last stripe moves {moved:0.000} m and is {differ:0.0000} m off its segment (should move, and with the cloth)");
            }
            lines.Add($"{name} ({baseVerts} vertices as its file has it): {string.Join(", ", perDesign)}");
        }
        Line($"flag variants (vertices per flag, budget {MaxFlagVertices}): " + string.Join("; ", lines));
        // The render library: the global props plus one variant per design of each flag prop, same array every call.
        var lib = Props.Global.Library;
        var with = FlagPool.WithVariants(lib);
        var want = lib.Count(d => d.FlagCloth != null) * FlagPool.Designs.Length;
        if (with.Length != lib.Length + want || !ReferenceEquals(with, FlagPool.WithVariants(lib)) || !with.Take(lib.Length).SequenceEqual(lib)
            || with.Select(d => d.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != with.Length)
            Fail($"FlagPool.WithVariants: {with.Length} props for a library of {lib.Length} (expected {lib.Length + want}, cached, library first, unique keys)");
        var noFlags = lib.Where(d => d.FlagCloth == null).Take(5).ToArray();
        if (!ReferenceEquals(FlagPool.WithVariants(noFlags), noFlags))
            Fail("FlagPool.WithVariants changed a library without flag props");
        var full = PropMeshes.Build(with);
        var plain = PropMeshes.Build(lib);
        var extra = (full.Vertices.Length - plain.Vertices.Length) / PropMeshes.FloatsPerVertex;
        Line($"render library: {lib.Length} props + {want} flag variants, {extra} extra vertices on the GPU in total (static, uploaded once); flags stay instanced, one draw per design and chunk");
    }

    private static Box ClothBounds(PropDef def, int[] owner)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var i = 0; i < def.Parts.Count; i++)
            if (owner[i] >= 0)
            {
                lo = Vector3.Min(lo, def.Parts[i].Bounds.Min);
                hi = Vector3.Max(hi, def.Parts[i].Bounds.Max);
            }
        return new Box((lo + hi) / 2, (hi - lo) / 2);
    }

    private static void CheckFlagPicks()
    {
        var counts = new int[4];
        var rng = new Random(1234);
        int nondet = 0, unmirrored = 0;
        const int N = 4000;
        for (var i = 0; i < N; i++)
        {
            var pos = new Vector3((float)(rng.NextDouble() * 400 - 200), (float)rng.NextDouble() * 5, (float)(rng.NextDouble() * 400 - 200));
            var k = FlagPool.Pick(pos, 4);
            counts[k]++;
            if (FlagPool.Pick(pos, 4) != k || FlagPool.Pick(pos with { Y = pos.Y + 3 }, 4) != k)
                nondet++;
            if (FlagPool.Pick(pos with { X = -pos.X }, 4) != k || FlagPool.Pick(-pos, 4) != k)
                unmirrored++;
        }
        // A grid of flags 4 m apart (how maps line them up) spreads too.
        var grid = new int[4];
        for (var x = 0; x < 20; x++)
            for (var z = 0; z < 20; z++)
                grid[FlagPool.Pick(new Vector3(x * 4f - 40f, 0, z * 4f - 40f), 4)]++;
        Line($"random flags: {N} random positions pick [{string.Join(", ", counts)}] of 4 designs, a 4 m grid of 400 picks [{string.Join(", ", grid)}], "
             + $"{nondet} not repeatable, {unmirrored} different from their mirror image (mirrored flags on symmetric maps match)");
        if (nondet > 0 || unmirrored > 0)
            Fail($"{nondet} flag picks weren't the same twice for the same position, {unmirrored} differ from their mirror image");
        if (counts.Any(c => c < N * 0.2 || c > N * 0.3) || grid.Any(c => c < 400 * 0.15 || c > 400 * 0.35))
            Fail("random flag picks aren't roughly uniform");
    }

    private static void CheckFlagMaps()
    {
        // The Dev line-up: the four designs at the front, random ones behind, overrides on pennants and banners.
        if (Maps.Find("Flag Lineup") is not { } lineup)
            Fail("Maps/Dev/FlagLineup.json ('Flag Lineup') didn't load");
        else
        {
            var front = lineup.PropInstances.Where(p => p.Position.Z < -5).OrderBy(p => p.Position.X).Select(p => p.Def.FlagId ?? "-").ToArray();
            if (!front.SequenceEqual(new[] { "us", "uk", "france", "finland" }))
                Fail($"Flag Lineup front row flies [{string.Join(", ", front)}], expected [us, uk, france, finland]");
            var again = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, lineup.SourceFile!)), lineup.SourceFile!);
            var roundTrip = MapFile.Load(MapFile.ToJson(lineup), "Flag Lineup (round trip)", lineup.PackDir);
            var ids = lineup.PropInstances.Select(p => p.Def.FlagId).ToArray();
            if (!again.PropInstances.Select(p => p.Def.FlagId).SequenceEqual(ids) || !roundTrip.PropInstances.Select(p => p.Def.FlagId).SequenceEqual(ids)
                || again.ContentHash != lineup.ContentHash || roundTrip.ContentHash != lineup.ContentHash)
                Fail("Flag Lineup: reloading or saving and reloading changed its flags or fingerprint");
            var none = lineup.PropInstances.Count(p => p.Def.FlagCloth != null && p.Def.FlagId == null);
            var batches = PropBatches.Build(lineup, PropMeshes.Build(FlagPool.WithVariants(lineup.PropScope.Library)));
            var fallback = PropBatches.Build(lineup, PropMeshes.Build(lineup.PropScope.Library));
            Line($"Flag Lineup: {lineup.FlagsFlying} flags fly a design ({string.Join(", ", ids.Where(i => i != null).GroupBy(i => i).Select(g => $"{g.Key} {g.Count()}"))}), "
                 + $"{none} keep their own cloth; {batches.Types} render types in {batches.Chunks.Length} chunks; fingerprint {lineup.ContentHash:x8}, same after reload and round trip");
            if (batches.SlotOf.Any(s => s < 0) || fallback.SlotOf.Any(s => s < 0))
                Fail("Flag Lineup: a flag is missing from the render batches");
            if (!lineup.PropInstances.Any(p => p.Def.Name == "Hanging Banner" && p.Def.FlagId == "france") || !lineup.PropInstances.Any(p => p.Def.Name == "Pennant Pole" && p.Def.FlagId == null))
                Fail("Flag Lineup: per-placement flags on banners and pennants (or the pennant's own look) are wrong");
        }

        // A test map: 40 random flags with the pool cut to two, one France override, a "none", and the same map with every flag off.
        var ops = string.Join(",\n", Enumerable.Range(0, 40).Select(i => $$"""{ "op": "Prop", "name": "Flag on a Pole", "x": {{-15 + (i % 8) * 4}}, "z": {{-12 + (i / 8) * 5}} }"""));
        var withOverride = ops + """, { "op": "Prop", "name": "Flag on a Pole", "x": 14, "z": 14, "flag": "france" }, { "op": "Prop", "name": "Flag on a Pole", "x": 10, "z": 14, "flag": "none" }""";
        var restricted = MapFile.Load(PropTestMap(withOverride, "\"flagPool\": [\"uk\", \"us\"],"), "flag pool test");
        var picked = restricted.PropInstances.Take(40).Select(p => p.Def.FlagId).ToList();
        var last = restricted.PropInstances[^2].Def.FlagId;
        var noneId = restricted.PropInstances[^1].Def.FlagId;
        if (picked.Any(id => id is not ("uk" or "us")) || !picked.Contains("uk") || !picked.Contains("us"))
            Fail($"\"flagPool\": [\"uk\", \"us\"]: random flags flew [{string.Join(", ", picked.Distinct())}]");
        if (last != "france" || noneId != null)
            Fail($"placement overrides: \"france\" flew {last ?? "its own cloth"}, \"none\" flew {noneId ?? "its own cloth"}");
        var off = MapFile.Load(PropTestMap(withOverride, "\"flagPool\": \"none\","), "flag pool test");
        var all = MapFile.Load(PropTestMap(withOverride), "flag pool test");
        if (off.PropInstances.Take(40).Any(p => p.Def.FlagId != null) || off.PropInstances[^2].Def.FlagId != "france")
            Fail("\"flagPool\": \"none\" didn't keep random flags' own cloth (or dropped a placement's own flag)");
        if (all.PropInstances.Take(40).Select(p => p.Def.FlagId).Distinct().Count() != 4)
            Fail($"the default pool: 40 random flags flew {all.PropInstances.Take(40).Select(p => p.Def.FlagId).Distinct().Count()} designs (expected all 4)");
        if (off.ContentHash != restricted.ContentHash || all.ContentHash != restricted.ContentHash)
            Fail("flags changed a map's fingerprint (they're cosmetic)");
        var rt = MapFile.Load(MapFile.ToJson(restricted), "flag pool test (round trip)");
        if (rt.FlagPoolIds is not ["uk", "us"] || !rt.PropInstances.Select(p => p.Def.FlagId).SequenceEqual(restricted.PropInstances.Select(p => p.Def.FlagId)))
            Fail("a map's \"flagPool\" or a placement's \"flag\" didn't survive saving and reloading");
        Line($"overrides and pools: [uk, us] pool flew uk {picked.Count(i => i == "uk")} / us {picked.Count(i => i == "us")}, the \"france\" placement {last}, \"none\" its own cloth; "
             + $"\"flagPool\": \"none\" keeps every random flag's own cloth; fingerprint {restricted.ContentHash:x8} the same with flags on, off or cut down");

        // Mistakes are map errors with a readable message.
        (string Ops, string Extra, string Expect)[] bad =
        [
            ("""{ "op": "Prop", "name": "Flag on a Pole", "x": 0, "z": 0, "flag": "mars" }""", "", "unknown flag"),
            ("""{ "op": "Prop", "name": "Wooden Crate", "x": 0, "z": 0, "flag": "uk" }""", "", "only for flag props"),
            ("""{ "op": "Prop", "name": "Flag on a Pole", "x": 0, "z": 0 }""", "\"flagPool\": [\"uk\", \"atlantis\"],", "unknown flag"),
        ];
        foreach (var (o, extra, expect) in bad)
        {
            try
            {
                MapFile.Load(PropTestMap(o, extra), "flag pool test");
                Fail($"a map with a bad flag loaded (expected \"{expect}\")");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (MapFileException ex)
            {
                Fail($"bad flag map: expected \"{expect}\", got: {ex.Message}");
            }
        }

        // The newest pack flies the pool; the Legacy pack (the Classic, Collision Point and High Ground milestones) flies nothing from it and keeps its look.
        var older = Maps.Packs.Where(p => p.Folder == "Legacy").SelectMany(p => p.Maps).ToList();
        var oldFlags = older.Where(m => m.PropInstances.Any(p => p.Def.FlagId != null)).Select(m => m.Name).ToList();
        if (oldFlags.Count > 0)
            Fail($"Legacy maps fly pool flags (they keep their look; add \"flagPool\": \"none\"): {string.Join(", ", oldFlags)}");
        var cluttered = Maps.Packs.Where(p => p.Folder == "Cluttered").SelectMany(p => p.Maps).Where(m => m.PropInstances.Any(p => p.Def.FlagCloth != null || p.Def.FlagBase != null)).ToList();
        Line($"packs: {older.Count} Legacy maps fly no pool flags; Cluttered: {string.Join(", ", cluttered.Select(m => $"{m.Name} {m.FlagsFlying} ({string.Join("/", m.PropInstances.Where(p => p.Def.FlagId != null).Select(p => p.Def.FlagId).Distinct())})"))}");
        foreach (var name in new[] { "Cul-de-sac", "Dockside", "Old Town" })
            if (Maps.Find(name) is { } m && m.FlagsFlying == 0)
                Fail($"{name}: none of its flags fly a design from the pool");
    }
}
