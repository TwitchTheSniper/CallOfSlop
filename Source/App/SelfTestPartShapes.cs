using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Part shapes (board item dev-part-models-pm01): the contoured model shapes (cylinder, tube, wedge, bevel, rounded,
    /// capsule, sphere, arc; Source/Zombies/PartShapes.cs) read from part files with their settings, stay inside their box
    /// (so bounds, the optic seat and picking still hold), face outwards, keep to their triangle budgets, are made once and
    /// shared, reach the built gun mesh, its scene nodes and draw list, and bad settings are refused with a reason.
    /// </summary>
    private static void CheckPartShapes()
    {
        static string Part(string name, string model) => $$"""
            {"format": "call-of-slop-part/1", "name": "{{name}}", "category": "stock", "rarity": "common", "weight": 0.1, "model": [{{model}}]}
            """;
        var cases = new (string Model, bool Convex)[]
        {
            ("""{"shape": "cylinder", "size": [0.03, 0.03, 0.2]}""", true),
            ("""{"shape": "cylinder", "size": [0.04, 0.03, 0.1], "segments": 6, "taper": 0.5, "shift": [0, 0.1]}""", true),
            ("""{"shape": "tube", "size": [0.03, 0.03, 0.12], "inner": 0.6, "ends": "back"}""", false),
            ("""{"shape": "tube", "size": [0.03, 0.03, 0.12], "segments": 5}""", false),
            ("""{"shape": "wedge", "size": [0.04, 0.08, 0.1], "taper": [0.6, 0.3], "shift": [0, 0.35]}""", true),
            ("""{"shape": "bevel", "size": [0.05, 0.06, 0.2], "radius": 0.2}""", true),
            ("""{"shape": "bevel", "size": [0.05, 0.06, 0.2], "edges": "long"}""", true),
            ("""{"shape": "rounded", "size": [0.05, 0.03, 0.1], "segments": 6}""", true),
            ("""{"shape": "rounded", "size": [0.05, 0.03, 0.1], "edges": "long", "radius": 0.5}""", true),
            ("""{"shape": "capsule", "size": [0.03, 0.03, 0.12]}""", true),
            ("""{"shape": "sphere", "size": [0.05, 0.04, 0.05], "segments": 10}""", true),
            ("""{"shape": "arc", "size": [0.012, 0.04, 0.07], "angle": 180, "inner": 0.75}""", false),
            ("""{"shape": "arc", "size": [0.03, 0.12, 0.06], "angle": 30, "dir": 90, "segments": 4}""", false),
            ("""{"shape": "arc", "size": [0.02, 0.06, 0.06], "angle": 360, "inner": 0}""", false),
        };
        var i = 0;
        foreach (var (model, convex) in cases)
        {
            i++;
            PartDef p;
            try
            {
                p = PartFile.Load(Part($"Shape Test {i}", model), $"shape-{i}.json");
            }
            catch (WeaponFileException e)
            {
                Fail($"part shapes: case {i} was refused: {e.Message}");
                continue;
            }
            var b = p.Boxes[0];
            var mesh = PartShapes.Mesh(b.ShapeId);
            var tris = mesh.Length / 18;
            var proxies = PartShapes.Proxies(b.ShapeId);
            Line($"{b.Shape} #{i}: shape {b.ShapeId}, {tris} triangles, {proxies.Length} proxy boxes");
            if (b.ShapeId == 0 || tris < 4 || tris > 1200)
                Fail($"part shapes: case {i} ({b.Shape}) made {tris} triangles (shape id {b.ShapeId})");
            if (p.Triangles != tris)
                Fail($"part shapes: case {i}: the part counts {p.Triangles} triangles, its shape has {tris}");
            var inside = 0;
            var wrong = 0;
            for (var v = 0; v < mesh.Length; v += 18)
            {
                var c = Vector3.Zero;
                for (var k = 0; k < 3; k++)
                {
                    var q = new Vector3(mesh[v + k * 6], mesh[v + k * 6 + 1], mesh[v + k * 6 + 2]);
                    if (MathF.Abs(q.X) > 0.5001f || MathF.Abs(q.Y) > 0.5001f || MathF.Abs(q.Z) > 0.5001f)
                        inside++;
                    c += q;
                }
                // The true normal is the stored one times the size (the shader's mat3(model) does that); it faces outwards.
                var n = new Vector3(mesh[v + 3], mesh[v + 4], mesh[v + 5]) * b.Size;
                if (convex && Vector3.Dot(n, c / 3 * b.Size) < -1e-7f)
                    wrong++;
                if (!float.IsFinite(n.X + n.Y + n.Z) || n.LengthSquared() < 1e-12f)
                    wrong++;
            }
            if (inside > 0)
                Fail($"part shapes: case {i} ({b.Shape}): {inside} vertices outside the box (bounds and the optic seat count on the box)");
            if (wrong > 0)
                Fail($"part shapes: case {i} ({b.Shape}): {wrong} triangles with a bad or inward normal");
            foreach (var m in proxies)
                for (var k = 0; k < 8; k++)
                {
                    var q = Vector3.Transform(new Vector3((k & 1) - 0.5f, ((k >> 1) & 1) - 0.5f, ((k >> 2) & 1) - 0.5f), m);
                    if (MathF.Abs(q.X) > 0.52f || MathF.Abs(q.Y) > 0.52f || MathF.Abs(q.Z) > 0.52f)
                    {
                        Fail($"part shapes: case {i} ({b.Shape}): a proxy box reaches outside the shape's box ({q})");
                        goto nextCase;
                    }
                }
            // Made once: the same shape again shares the mesh.
            var again = PartFile.Load(Part($"Shape Test {i}b", model), $"shape-{i}b.json");
            if (again.Boxes[0].ShapeId != b.ShapeId)
                Fail($"part shapes: case {i}: the same shape made a second mesh ({again.Boxes[0].ShapeId} vs {b.ShapeId})");
            nextCase: ;
        }

        // Plain boxes stay plain.
        var plain = PartFile.Load(Part("Shape Test Plain", """{"size": [0.1, 0.1, 0.1]}, {"shape": "rod", "size": [0.02, 0.02, 0.2]}, {"shape": "plate", "size": [0.1, 0.004, 0.1]}"""), "plain.json");
        if (plain.Boxes.Any(b => b.ShapeId != 0) || plain.Triangles != 12 + 24 + 12)
            Fail($"part shapes: box, rod and plate should stay plain boxes (12, 24, 12 triangles; got {plain.Triangles})");

        // Refusals, with a reason.
        foreach (var (model, want) in new[]
                 {
                     ("""{"size": [0.1, 0.1, 0.1], "segments": 8}""", "box has none"),
                     ("""{"shape": "wedge", "size": [0.1, 0.1, 0.1], "inner": 0.5}""", "doesn't take"),
                     ("""{"shape": "wedge", "size": [0.1, 0.1, 0.1], "taper": 0.6, "shift": 0.4}""", "stay inside"),
                     ("""{"shape": "tube", "size": [0.1, 0.1, 0.1], "ends": "sideways"}""", "closed, open"),
                     ("""{"shape": "cylinder", "size": [0.1, 0.1, 0.1], "segments": 2}""", "at least 3"),
                     ("""{"shape": "blob", "size": [0.1, 0.1, 0.1]}""", "isn't one of"),
                 })
        {
            try
            {
                PartFile.Load(Part("Shape Refusal", model), "refuse.json");
                Fail($"part shapes: {model} should be refused");
            }
            catch (WeaponFileException e)
            {
                if (!e.Message.Contains(want))
                    Fail($"part shapes: the refusal of {model} should say \"{want}\": {e.Message}");
            }
        }

        // The shape reaches the built gun, its scene nodes and the draw list.
        if (Parts.Find("Rifle Grip") is { } rg && Parts.Find("Workhorse Receiver") is { } wh)
        {
            var b = new WeaponBuild("Shapes", rg);
            b.Root!.Attach("receiver_1", wh);
            var shaped = Parts.All.Where(p => p.Boxes.Any(x => x.ShapeId != 0)).ToList();
            Line($"shipped parts using contoured shapes: {shaped.Count} of {Parts.All.Count()}; {PartShapes.Count - 1} distinct shape meshes");
            var asm = WeaponBuilder.Assemble(b);
            var mesh = asm.Model!.Mesh;
            var wantShaped = rg.Boxes.Concat(wh.Boxes).Count(x => x.ShapeId != 0 && BuildModel.Shown(x, new BuildNode(rg), true));
            var gotShaped = mesh.Parts.Count(x => x.Shape != 0);
            if (gotShaped < wantShaped)
                Fail($"part shapes: the built mesh has {gotShaped} shaped pieces, its parts {wantShaped}");
            var rig = new GunRig();
            rig.Build(asm.PreviewDef!);
            var list = new DrawList();
            rig.Root.UpdateWorld();
            list.Flatten(rig.Root);
            var listShaped = 0;
            for (var k = 0; k < list.Count; k++)
                if (list.Item(k).Shape != 0)
                    listShaped++;
            Line($"Rifle Grip + Workhorse: {gotShaped} shaped pieces in the mesh, {listShaped} in the view model's draw list");
            if (listShaped != gotShaped)
                Fail($"part shapes: the view model draws {listShaped} shaped pieces, the mesh has {gotShaped}");
        }
    }
}
