using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Animated props": the "anim" block of prop parts (PropAnim.cs) and the animated props in Props/Animated. Readable
/// errors for broken anim blocks, every animated prop loading with sane pivots and axes, the GPU data (records and the
/// per-vertex stream), animation as a pure function of time and position (deterministic, a different phase per copy),
/// the freeze distance, colliders untouched by animation, static props' mesh and instance data unchanged (pinned
/// hashes), and the "Animated Props" gallery map with a bot match.
/// </summary>
public static partial class SelfTest
{
    private const string AnimCategory = "Animated";
    private const string AnimGalleryMap = "Animated Props";

    /// <summary>At least this many animated props ship in Props/Animated.</summary>
    private const int MinAnimatedProps = 45;

    /// <summary>The categories the starter library had before animation existed (their meshes are pinned below).</summary>
    private static readonly string[] StarterCategories = ["Street", "Industrial", "Debris", "Furniture", "Nature"];

    /// <summary>
    /// FNV-1a of the starter categories' prop meshes (vertices relative to each prop, indices relative to its first
    /// vertex) and of the Prop Gallery's instance buffer, as origin/main built them before prop animation (56e7945).
    /// If the mesh builder changes on purpose (a new vertex layout), update these from the line this section prints.
    /// </summary>
    private const uint StarterMeshHash = 0x6b1e6818u, GalleryInstanceHash = 0x6e51cf4bu;

    private static readonly List<string> AnimNotes = new();

    /// <summary>A result line, also collected for the section's GitHub notice.</summary>
    private static void AnimLine(string text)
    {
        Line(text);
        AnimNotes.Add(text);
    }

    private static void CheckAnimatedProps()
    {
        AnimNotes.Clear();
        try
        {
            CheckAnimFileErrors();
            var anim = CheckAnimLibrary();
            if (anim == null)
                return;
            CheckAnimMaths(anim.Value.Data, anim.Value.Meshes);
            CheckStaticPropsUnchanged(anim.Value.Data, anim.Value.Meshes);
            CheckAnimGallery(anim.Value.Data, anim.Value.Meshes);
        }
        finally
        {
            // The section's results as one GitHub Actions notice (the workflow prints selftest.log; "::notice" lines become annotations).
            Line("::notice title=Animated props::" + string.Join("%0A", AnimNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    // ---- Readable errors ---------------------------------------------------------------------------

    private static void CheckAnimFileErrors()
    {
        const string still = """{ "size": [1, 0.2, 1] }""";
        (string Parts, string Want)[] bad =
        [
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "twirl" } }""", "unknown anim type \"twirl\""),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "axis": "y" } }""", "needs a \"type\""),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin" } }""", "needs \"rpm\""),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 2000 } }""", "outside -600 to 600"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 30, "speed": 2 } }""", "unknown key"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 30, "angle": 20 } }""", "\"angle\" doesn't apply"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "swing", "period": 2 } }""", "needs \"angle\""),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "bob", "period": 2 } }""", "needs \"distance\""),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 30, "axis": [0, 0, 0] } }""", "has no direction"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 30, "axis": "w" } }""", "\"x\", \"y\", \"z\""),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 30, "pivot": [9, 0, 0] } }""", "more than 1 m outside the model"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 30, "off": "red" } }""", "\"off\" doesn't apply"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "blink", "axis": "y" } }""", "\"axis\" doesn't apply"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "cycle", "steps": 3 } }""", "needs \"on\""),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "cycle", "steps": 3, "on": [4] } }""", "past the last one"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "blink", "oneWay": true } }""", "\"oneWay\" doesn't apply"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "follow" } }""", "needs \"parent\""),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "follow", "parent": 2 } }""", "has no \"anim\" block"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "follow", "parent": 1 } }""", "is the part itself"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "follow", "parent": 9 } }""", "the prop has 2 parts"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": "spin" }""", "\"anim\" is an object"),
            ("""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "swing", "angle": 10, "period": 0.01 } }""", "outside 0.05 to 120"),
        ];
        var readable = 0;
        foreach (var (part, want) in bad)
        {
            var json = $$"""{ "name": "Bad Anim", "parts": [ {{part}}, {{still}} ] }""";
            try
            {
                PropFile.Load(json, "Animated/BadAnim.json");
                Fail($"a broken anim block loaded without an error: {part}");
            }
            catch (PropFileException ex)
            {
                if (ex.Message.Contains(want, StringComparison.Ordinal) && ex.Message.Contains("BadAnim.json", StringComparison.Ordinal) && ex.Message.Contains("part #1", StringComparison.Ordinal))
                    readable++;
                else
                    Fail($"anim error for {part} doesn't say \"{want}\" (and the file and part): {ex.Message}");
            }
        }

        // Loops and long chains between parts.
        var loop = """{ "name": "Bad Anim", "parts": [ { "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 10, "parent": 2 } }, { "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 10, "parent": 1 } } ] }""";
        var chain = """{ "name": "Bad Anim", "parts": [ """ + string.Join(", ", Enumerable.Range(1, 6).Select(i =>
            $$"""{ "size": [0.4, 0.1, 0.1], "anim": { "type": "spin", "rpm": 10{{(i < 6 ? $", \"parent\": {i + 1}" : "")}} } }""")) + " ] }";
        foreach (var (json, want) in new[] { (loop, "loop"), (chain, "more than 4 parts") })
        {
            try
            {
                PropFile.Load(json, "Animated/BadAnim.json");
                Fail($"anim parents that {want} loaded without an error");
            }
            catch (PropFileException ex)
            {
                if (ex.Message.Contains(want, StringComparison.Ordinal))
                    readable++;
                else
                    Fail($"anim parent error doesn't say \"{want}\": {ex.Message}");
            }
        }

        // A good block with every key round-trips, and a part without "anim" stays still.
        var good = PropFile.Load("""
            { "name": "Good Anim", "parts": [
              { "size": [1, 0.2, 1] },
              { "size": [0.5, 0.05, 0.1], "offset": [0.3, 1, 0], "anim": { "type": "spin", "axis": [0, 2, 0], "pivot": [0, 1, 0], "rpm": -30, "phase": 0.25, "jitter": 0.1, "wind": 0.5, "randomPhase": false, "freezeDistance": 120 } },
              { "size": [0.1, 0.1, 0.1], "offset": [0.6, 1, 0], "anim": { "type": "cycle", "period": 4, "steps": 4, "on": [1, 3], "color": "red", "off": "black", "glow": 2, "parent": 2 } },
              { "size": [0.1, 0.3, 0.02], "offset": [0, 0.6, 0.5], "anim": { "type": "swing", "angle": 20, "period": 1.5, "oneWay": true } } ] }
            """, "Animated/GoodAnim.json");
        var spin = good.Parts[1].Anim;
        var cycle = good.Parts[2].Anim;
        if (good.Parts[0].Anim != null || spin is not { Type: PropAnimType.Spin, Rpm: -30, Parent: -1, RandomPhase: false, FreezeDistance: 120 } || spin.Axis != Vector3.UnitY
            || cycle is not { Type: PropAnimType.Cycle, Steps: 4, OnMask: 0b1010, Parent: 1, Glow: 2 } || good.Parts[3].Anim is not { OneWay: true, Pivot: not null })
            Fail("a good anim block didn't read back as written");
        AnimLine($"anim blocks: {readable} of {bad.Length + 2} broken ones rejected with a readable message (file, part and what's wrong), e.g. "
             + "\"Animated/BadAnim.json, part #1 (\"anim\"): anim type \"spin\" needs \"rpm\" (turns a minute; negative turns the other way)\"; a block with every key reads back");
    }

    // ---- The library -----------------------------------------------------------------------------

    private static (PropAnimData Data, PropMeshes Meshes)? CheckAnimLibrary()
    {
        var all = Props.All;
        foreach (var e in Props.LoadErrors.Where(e => e.StartsWith(AnimCategory, StringComparison.OrdinalIgnoreCase)))
            Fail("animated prop file problem: " + e);
        var animated = all.Where(p => p.Category.Equals(AnimCategory, StringComparison.OrdinalIgnoreCase)).ToList();
        var meshes = PropMeshes.Build(all);
        var sw = Stopwatch.StartNew();
        var data = PropAnimData.Build(meshes);
        var buildMs = sw.Elapsed.TotalMilliseconds;
        var byType = all.SelectMany(p => p.Parts).Where(p => p.Anim != null).GroupBy(p => p.Anim!.Type).OrderBy(g => g.Key).Select(g => $"{g.Key.ToString().ToLowerInvariant()} {g.Count()}");
        AnimLine($"{animated.Count} animated props in Props/{AnimCategory} ({animated.Count(p => p.IsSolid)} solid, {animated.Count(p => !p.IsSolid)} cosmetic, {animated.Count(p => p.Breakable != null)} breakable); "
             + $"{data.AnimatedTypes} animated types in the library with {data.AnimatedParts} moving or lit parts ({string.Join(", ", byType)}); {data.RecordCount} records "
             + $"({data.Records.Length * 4 / 1024.0:0.0} KB texture), built in {buildMs:0.0} ms");
        if (animated.Count < MinAnimatedProps)
            Fail($"only {animated.Count} animated props in Props/{AnimCategory} (want at least {MinAnimatedProps})");
        var problems = 0;
        foreach (var p in animated)
        {
            if (!p.Parts.Any(x => x.Anim != null))
            {
                Fail($"{p.Name}: in Props/{AnimCategory} but nothing on it moves or lights up");
                problems++;
            }
            for (var i = 0; i < p.Parts.Count; i++)
            {
                if (p.Parts[i].Anim is not { } a)
                    continue;
                var pv = a.Pivot ?? Vector3.Zero;
                if (a.Pivot == null || !float.IsFinite(pv.X + pv.Y + pv.Z) || MathF.Abs(a.Axis.Length() - 1f) > 1e-3f || pv.Y < -0.5f)
                {
                    Fail($"{p.Name} part #{i + 1}: pivot {pv} or axis {a.Axis} isn't sane");
                    problems++;
                }
            }
        }
        // The GPU data: one record per animated part, every animated part's vertices pointing at its record, still ones at -1.
        var animVerts = 0;
        for (var d = 0; d < all.Length; d++)
        {
            var range = meshes.Ranges[d];
            var start = int.MaxValue;
            var end = 0;
            for (var k = 0; k < range.IndexCount; k++)
            {
                start = Math.Min(start, (int)meshes.Indices[range.FirstIndex + k]);
                end = Math.Max(end, (int)meshes.Indices[range.FirstIndex + k] + 1);
            }
            var marked = 0;
            for (var v = start; v < end; v++)
                if (data.VertexRecord[v] >= 0)
                    marked++;
            var want = all[d].Parts.Count(x => x.Anim != null) * 24;
            animVerts += marked;
            if (marked != want || data.Animated[d] != want > 0)
            {
                Fail($"{all[d].Name}: {marked} vertices point at animation records, expected {want}");
                problems++;
            }
        }
        if (data.RecordCount != data.AnimatedParts || data.Records.Any(f => !float.IsFinite(f)) || data.VertexRecord.Any(r => r >= data.RecordCount))
            Fail($"animation records don't add up ({data.RecordCount} records for {data.AnimatedParts} parts, or a NaN, or a vertex pointing past the end)");
        for (var d = 0; d < all.Length; d++)
        {
            var recs = data.PartRecord[d].Where(r => r >= 0).ToArray();
            foreach (var r in recs)
            {
                var parent = (int)data.Records[r * PropAnimData.RecordFloats + 1];
                if (parent == r || (parent >= 0 && !recs.Contains(parent)))
                    Fail($"{all[d].Name}: animation record {r} has parent {parent}, not another record of the same prop");
            }
        }
        AnimLine($"animation stream: {animVerts} of {data.VertexRecord.Length} vertices animated, {problems} problem(s) with pivots, axes or vertex ranges; "
             + $"largest reach {data.Reach.Max():0.00} m ({all[Array.IndexOf(data.Reach, data.Reach.Max())].Name}), freeze distances {data.FreezeDistance.Where((_, d) => data.Animated[d]).DefaultIfEmpty(0).Min():0}-{data.FreezeDistance.Max():0} m");
        return (data, meshes);
    }

    // ---- The maths: deterministic, varied per copy, frozen far away ----------------------------------

    private static void CheckAnimMaths(PropAnimData data, PropMeshes meshes)
    {
        var defs = meshes.Defs;
        int moving = 0, still = 0, nondet = 0, frozenOk = 0, frozenBad = 0, fadeOk = 0;
        var maxMove = 0f;
        var maxMoveName = "";
        for (var d = 0; d < defs.Length; d++)
        {
            if (!data.Animated[d])
                continue;
            var range = meshes.Ranges[d];
            var freeze = data.FreezeDistance[d];
            var moved = 0f;
            var lit = false;
            for (var k = 0; k < range.IndexCount; k += 3)
            {
                var v = (int)meshes.Indices[range.FirstIndex + k];
                var rec = (int)data.VertexRecord[v];
                if (rec < 0)
                    continue;
                var o = v * PropMeshes.FloatsPerVertex;
                var p = new Vector3(meshes.Vertices[o], meshes.Vertices[o + 1], meshes.Vertices[o + 2]);
                var seed = PropAnim.Seed(new Vector3(3.1f, 0, -7.4f));
                var a1 = PropAnim.Animate(data.Records, rec, p, 12.37f, seed, 0f, 5f, freeze, out var l1);
                var a2 = PropAnim.Animate(data.Records, rec, p, 12.37f, seed, 0f, 5f, freeze, out var l2);
                if (a1 != a2 || l1 != l2 || !float.IsFinite(a1.X + a1.Y + a1.Z))
                    nondet++;
                for (var t = 0; t < 24; t++)
                {
                    var at = PropAnim.Animate(data.Records, rec, p, 0.21f * t, seed, 0f, 5f, freeze, out var lt);
                    moved = MathF.Max(moved, Vector3.Distance(at, p));
                    lit |= lt >= 0 && MathF.Abs(lt - l1) > 0.05f;
                }
                // Beyond the freeze distance: exactly the rest pose (what the plain shader draws), and fading in the band before it.
                var far = PropAnim.Animate(data.Records, rec, p, 12.37f, seed, 0f, freeze + 0.01f, freeze, out var lf);
                if (far == p && lf < 0)
                    frozenOk++;
                else
                    frozenBad++;
                var type = (PropAnimType)(int)data.Records[rec * PropAnimData.RecordFloats];
                if (type is PropAnimType.Swing or PropAnimType.Bob)
                {
                    var near = PropAnim.Animate(data.Records, rec, p, 0.3f, seed, 0f, 5f, freeze, out _);
                    var band = PropAnim.Animate(data.Records, rec, p, 0.3f, seed, 0f, freeze * (1 - PropAnim.FadeBand / 2), freeze, out _);
                    if (Vector3.Distance(band, p) <= Vector3.Distance(near, p) * 0.55f + 1e-5f)
                        fadeOk++;
                }
            }
            if (moved > 0.002f || lit)
                moving++;
            else
            {
                still++;
                Fail($"{defs[d].Name}: has anim blocks but nothing moved or changed light over 5 s");
            }
            if (moved > maxMove)
            {
                maxMove = moved;
                maxMoveName = defs[d].Name;
            }
            if (moved > data.Reach[d] + 0.01f)
                Fail($"{defs[d].Name}: a vertex moved {moved:0.00} m, more than its cull margin {data.Reach[d]:0.00} m");
        }
        AnimLine($"animation is a pure function of time and position: {moving} animated types move or change light over 5 s, {still} don't; {nondet} vertices gave different answers "
             + $"for the same time and seed; furthest move {maxMove:0.00} m ({maxMoveName}); beyond the freeze distance {frozenOk} vertices at rest, {frozenBad} not; "
             + $"{fadeOk} swing/bob vertices fading out in the last {PropAnim.FadeBand * 100:0}% before it");
        if (nondet > 0 || frozenBad > 0)
            Fail($"animation isn't deterministic ({nondet}) or doesn't freeze far away ({frozenBad})");

        // Per-copy phase: a row of the same prop at 1.5 m steps gets different seeds, so it doesn't move in lockstep.
        var seeds = Enumerable.Range(0, 40).Select(i => PropAnim.Seed(new Vector3(-30f + i * 1.5f, 0, 4f))).ToList();
        var distinct = seeds.Select(s => MathF.Round(s, 3)).Distinct().Count();
        var spread = seeds.Max() - seeds.Min();
        var fan = Array.FindIndex(defs, x => x.Name == "Pedestal Fan");
        var differ = 0;
        if (fan >= 0)
        {
            var v = (int)meshes.Indices[meshes.Ranges[fan].FirstIndex + meshes.Ranges[fan].IndexCount - 3];
            var rec = (int)data.VertexRecord[v];
            var o = v * PropMeshes.FloatsPerVertex;
            var p = new Vector3(meshes.Vertices[o], meshes.Vertices[o + 1], meshes.Vertices[o + 2]);
            for (var i = 0; i + 1 < seeds.Count; i++)
                if (Vector3.Distance(PropAnim.Animate(data.Records, rec, p, 3f, seeds[i], 0f, 5f, 80f, out _), PropAnim.Animate(data.Records, rec, p, 3f, seeds[i + 1], 0f, 5f, 80f, out _)) > 0.01f)
                    differ++;
        }
        AnimLine($"per-copy phase: 40 props in a row get {distinct} different seeds (spread {spread:0.00}); {differ} of 39 neighbouring pedestal fans are at a different point of their swing");
        if (distinct < 36 || spread < 0.8f || fan < 0 || differ < 30)
            Fail($"copies of a prop move in lockstep ({distinct} seeds, {differ} of 39 neighbours differ)");
        if (PropAnim.Seed(new Vector3(1.23f, 5f, 4.56f)) != PropAnim.Seed(new Vector3(1.23f, -2f, 4.56f)))
            Fail("the seed depends on height (it should come from the ground position only, like the shader's)");

        // The renderer's LOD decision (CPU side): a chunk animates only nearer than its freeze distance.
        if (!PropAnim.ShouldAnimate(10, 80) || PropAnim.ShouldAnimate(80, 80) || PropAnim.ShouldAnimate(200, 80)
            || PropAnim.Strength(10, 80) != 1f || PropAnim.Strength(80, 80) != 0f || PropAnim.Strength(80 - 80 * PropAnim.FadeBand / 2, 80) is < 0.45f or > 0.55f)
            Fail("the freeze distance logic is off (ShouldAnimate / Strength)");

        // Shader time wraps without losing precision; an hour in, a 600 rpm fan still steps smoothly.
        var t0 = PropAnim.ShaderTime(3600.0);
        var t1 = PropAnim.ShaderTime(3600.0 + 1.0 / 240);
        if (MathF.Abs(t1 - t0 - 1f / 240) > 2e-4f || PropAnim.ShaderTime(PropAnim.TimeWrap + 1) > 1.01f)
            Fail($"shader time loses precision ({t1 - t0} s for a 1/240 s frame)");

        // No allocations: evaluating a whole frame of animated vertices on the CPU (the reference maths) allocates nothing.
        var r0 = Array.FindIndex(data.VertexRecord, r => r >= 0);
        var sink = Vector3.Zero;
        for (var i = 0; i < 1000; i++)
            sink += PropAnim.Animate(data.Records, (int)data.VertexRecord[r0], Vector3.One, i * 0.01f, 0.3f, 0f, 5f, 80f, out _);
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
            sink += PropAnim.Animate(data.Records, (int)data.VertexRecord[r0], Vector3.One, i * 0.01f, 0.3f, 0f, 5f, 80f, out _);
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        if (alloc > 0 || !float.IsFinite(sink.X))
            Fail($"the animation maths allocated {alloc} bytes for 10000 vertices");
    }

    // ---- Static props unchanged ---------------------------------------------------------------------

    private static void CheckStaticPropsUnchanged(PropAnimData data, PropMeshes meshes)
    {
        var all = meshes.Defs;
        // The starter props' meshes, each relative to its own first vertex, built inside the full library and on their own.
        var starter = all.Where(p => StarterCategories.Contains(p.Category, StringComparer.OrdinalIgnoreCase)).ToArray();
        var alone = PropMeshes.Build(starter);
        var meshHash = 2166136261u;
        var differ = 0;
        for (var s = 0; s < starter.Length; s++)
        {
            var d = Array.IndexOf(all, starter[s]);
            var (fullV, fullI) = Slice(meshes, d);
            var (aloneV, aloneI) = Slice(alone, s);
            if (!fullV.SequenceEqual(aloneV) || !fullI.SequenceEqual(aloneI))
                differ++;
            foreach (var f in aloneV)
                meshHash = Fnv(meshHash, BitConverter.SingleToUInt32Bits(f));
            foreach (var i in aloneI)
                meshHash = Fnv(meshHash, i);
            if (data.Animated[d])
                Fail($"{all[d].Name}: a starter prop picked up animation");
        }
        uint instHash = 0;
        var galleryCount = 0;
        if (Maps.Find("Prop Gallery") is { } gallery)
        {
            World.SetMap(gallery);
            var batches = PropBatches.Build(gallery, meshes);
            galleryCount = batches.Count;
            instHash = 2166136261u;
            foreach (var f in batches.Instances)
                instHash = Fnv(instHash, BitConverter.SingleToUInt32Bits(f));
            foreach (var c in batches.Chunks)
                instHash = Fnv(instHash, (uint)c.Count);
            if (batches.Chunks.Any(c => data.Animated[c.Def]))
                Fail("the Prop Gallery has animated props (old maps keep their look)");
        }
        AnimLine($"static props unchanged: {starter.Length} starter props' meshes hash 0x{meshHash:x8} (pinned 0x{StarterMeshHash:x8}), {differ} differ between the full library and on their own; "
             + $"Prop Gallery instance buffer ({galleryCount} props) hashes 0x{instHash:x8} (pinned 0x{GalleryInstanceHash:x8}); the plain prop shader and vertex layout are untouched "
             + "(animation is a second vertex stream read only by the animated pass)");
        if (differ > 0)
            Fail($"{differ} starter props' meshes changed when the animated props joined the library");
        if (meshHash != StarterMeshHash)
            Fail($"the starter props' meshes changed: hash 0x{meshHash:x8}, pinned 0x{StarterMeshHash:x8}");
        if (instHash != GalleryInstanceHash)
            Fail($"the Prop Gallery's instance buffer changed: hash 0x{instHash:x8}, pinned 0x{GalleryInstanceHash:x8}");

        static uint Fnv(uint h, uint v)
        {
            for (var b = 0; b < 4; b++)
            {
                h ^= (v >> (b * 8)) & 0xFF;
                h *= 16777619u;
            }
            return h;
        }
    }

    /// <summary>Prop <paramref name="d"/>'s vertices and indices, the indices relative to its first vertex.</summary>
    private static (float[] Vertices, uint[] Indices) Slice(PropMeshes m, int d)
    {
        var range = m.Ranges[d];
        var idx = m.Indices.AsSpan(range.FirstIndex, range.IndexCount).ToArray();
        var first = idx.Min();
        var last = idx.Max();
        for (var i = 0; i < idx.Length; i++)
            idx[i] -= first;
        return (m.Vertices.AsSpan((int)first * PropMeshes.FloatsPerVertex, (int)(last - first + 1) * PropMeshes.FloatsPerVertex).ToArray(), idx);
    }

    // ---- The gallery map ------------------------------------------------------------------------------

    private static void CheckAnimGallery(PropAnimData data, PropMeshes meshes)
    {
        if (Maps.Find(AnimGalleryMap) is not { } map)
        {
            Fail($"Maps/Dev/AnimatedProps.json ('{AnimGalleryMap}') didn't load");
            return;
        }
        World.SetMap(map);
        var animatedDefs = meshes.Defs.Where((_, d) => data.Animated[d]).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shown = map.PropInstances.Select(p => p.Def.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = animatedDefs.Where(n => !shown.Contains(n)).ToList();
        if (missing.Count > 0)
            Fail($"{AnimGalleryMap} doesn't show {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? "..." : "")}");
        if (map.Pack?.Name != "Dev")
            Fail($"{AnimGalleryMap} is in the '{map.Pack?.Name}' pack, not Dev");

        // Colliders: the solid boxes are the prop files' colliders, and nothing about them depends on time.
        var hash = map.ContentHash;
        var solid = map.Solid.ToArray();
        var colliderBoxes = map.PropInstances.Sum(p => p.SolidCount);
        var wantBoxes = 0;
        foreach (var p in map.PropInstances)
            wantBoxes += p.Def.Colliders.Length; // placed at yaw 0, so one world box per collider box
        var probes = 0;
        var probeDiffer = 0;
        var batches = PropBatches.Build(map, meshes);
        foreach (var p in map.PropInstances.Where(p => p.Def.IsSolid).Take(40))
        {
            var from = p.Bounds.Center + new Vector3(0, 0, 6f);
            var dir = Vector3.Normalize(p.Bounds.Center - from);
            var before = World.RaycastWorld(from, dir, 12f);
            // Step the "animation" through a minute of frames on the CPU (what the GPU does each frame): nothing in the world changes.
            var sink = 0f;
            for (var f = 0; f < 60; f++)
                sink += PropAnim.Animate(data.Records, 0, Vector3.One, f * 1.01f, 0.5f, 0f, 3f, 80f, out _).X;
            var after = World.RaycastWorld(from, dir, 12f);
            probes++;
            if (before != after || !float.IsFinite(sink))
                probeDiffer++;
        }
        var unchanged = map.Solid.Count == solid.Length && map.Solid.Zip(solid).All(z => z.First == z.Second) && map.ContentHash == hash;
        var g = NavGraph.Build(map);
        var search = new NavSearch();
        var path = new int[512];
        var unreachable = 0;
        var pairs = 0;
        foreach (var s0 in map.Spawns)
            foreach (var s1 in map.Spawns)
            {
                if (s0 == s1)
                    continue;
                pairs++;
                if (search.Find(g, g.Nearest(s0), g.Nearest(s1, needSight: false), path) == 0)
                    unreachable++;
            }
        var animatedInstances = batches.Chunks.Where(c => data.Animated[c.Def]).Sum(c => c.Count);
        AnimLine($"{AnimGalleryMap}: {map.PropInstances.Count} props ({animatedInstances} animated, all {animatedDefs.Count} animated types shown), {colliderBoxes} collider boxes "
             + $"({wantBoxes} in the prop files), {batches.Chunks.Length} render chunks; {probes} rays at solid animated props hit the same before and after a minute of animation "
             + $"({probeDiffer} differ), solid boxes and fingerprint {(unchanged ? "unchanged" : "CHANGED")}; nav {g.Nodes.Length} nodes, {unreachable} of {pairs} spawn pairs unreachable");
        if (colliderBoxes != wantBoxes || probeDiffer > 0 || !unchanged)
            Fail($"{AnimGalleryMap}: animation touched collision ({colliderBoxes} vs {wantBoxes} boxes, {probeDiffer} rays differ, boxes {(unchanged ? "same" : "changed")})");
        if (unreachable > map.Spawns.Count)
            Fail($"{AnimGalleryMap}: {unreachable} spawn pairs have no path");
        if (animatedInstances < animatedDefs.Count || batches.Count != map.PropInstances.Count)
            Fail($"{AnimGalleryMap}: render batches cover {batches.Count} of {map.PropInstances.Count} props, {animatedInstances} animated");
        AnimBotMatch(map);
    }

    private static void AnimBotMatch(Map map)
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: Math.Min(map.MaxPlayers, Game.ClassicPlayers) - 1, botSkill: BotDifficulty.Normal, mapId: map.Id);
        game.MapVoteEnabled = false;
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var moved = new Dictionary<byte, float>();
        var last = new Dictionary<byte, Vector3>();
        var fallen = 0;
        var cpu = new TickCpu();
        for (var i = 0; i < 30 * World.TickRate; i++)
        {
            now += dt;
            cpu.Start();
            var t0 = Stopwatch.GetTimestamp();
            game.Update(now, dt);
            cpu.Stop(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, count: i > 60);
            foreach (var p in game.Players.Values)
            {
                if (!p.Alive)
                    continue;
                if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                {
                    Fail($"player {p.Id} position is NaN on {map.Name}");
                    return;
                }
                if (p.Position.Y < -0.5f)
                    fallen++;
            }
            if (i % 60 == 0)
                foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                {
                    if (last.TryGetValue(p.Id, out var lp))
                        moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                    last[p.Id] = p.Position;
                }
        }
        var stuck = moved.Count(kv => kv.Value < 10f);
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        AnimLine($"bot match on {map.Name} (30 s, {game.Players.Count} players): kills {kills}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, stuck {stuck}, "
             + $"below the floor {fallen}, broken props {map.BrokenProps.Count}, worst tick {cpu.WorstText}");
        if (stuck > 1)
            Fail($"{map.Name}: {stuck} bots barely moved");
        if (fallen > 0)
            Fail($"{map.Name}: {fallen} player-ticks below the floor");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{map.Name}: a tick did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
    }
}
