using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Building faces": no z-fighting between a building's own boxes. Two boxes of different colour or texture whose faces
/// lie in the same plane, face the same way and overlap draw one over the other at random, pixel by pixel (the floor
/// slab's edge flickering through the outer wall on Prop Yard, interior wall ends showing on the outside of the Village
/// Buildings cottages: playtest reports, 2026-10-28). Checks every building in the Buildings folder (each repeat floor
/// count, mirrored and turned) and the map files' old walk-in "Building" op (one to three storeys, with and without
/// roof access): any such pair whose shared patch is visible (not inside a third box) fails, named with its place.
/// </summary>
public static partial class SelfTest
{
    private static void CheckBuildingFaces()
    {
        var scope = Props.Global;
        var checkedPlans = 0;
        var faces = 0;
        var bad = 0;
        foreach (var d in Buildings.All)
        {
            var variants = new List<BuildingVariant> { new() { Furnish = false }, new() { Mirror = true, Furnish = false } };
            if (d.MaxFloors > d.MinFloors)
            {
                variants.Add(new BuildingVariant { Floors = d.MinFloors, Furnish = false });
                variants.Add(new BuildingVariant { Floors = Math.Min(d.MaxFloors, d.MinFloors + 2), Furnish = false });
            }
            var problems = new List<string>();
            foreach (var v in variants)
                for (var q = 0; q < 2; q++)
                {
                    BuildingPlan plan;
                    try
                    {
                        plan = BuildingGen.Generate(d, v, 0, new Vector3(5, 0, -3), q, scope);
                    }
                    catch (BuildingFileException)
                    {
                        continue; // "Buildings" reports files that don't build
                    }
                    checkedPlans++;
                    var boxes = plan.Shell.Select(b => (b.Box, Key: (b.Color, b.Layer))).ToList();
                    var fights = CoplanarFights(boxes, out var n, plan.BaseY);
                    foreach (var c in fights)
                        problems.Add($"{d.Name} (floors {(v.Floors > 0 ? v.Floors : d.DefaultFloors)}, mirror {v.Mirror}, turn {q * 90}): {c}");
                    faces += n;
                }
            bad += problems.Count;
            foreach (var p in problems.Take(4))
                Fail(p);
            if (problems.Count > 4)
                Fail($"{d.Name}: {problems.Count - 4} more coplanar face fight(s)");
        }

        // The map files' walk-in "Building" op (Source/Maps/Maps.cs): Prop Yard and the Legacy pack use it.
        foreach (var stories in new[] { 1, 2, 3 })
            foreach (var roof in new[] { false, true })
            {
                var json = $$"""
                    { "format": "call-of-slop-map/1", "name": "Faces Test", "halfX": 30, "halfZ": 30,
                      "ops": [ {"op": "Building", "cx": 1, "cz": -2, "w": 14, "d": 12, "stories": {{stories}}, "wall": [0.62, 0.36, 0.3], "floor": [0.42, 0.42, 0.44], "doors": "SE", "roofAccess": {{(roof ? "true" : "false")}}}, {"op": "RingSpawns", "count": 4, "frac": 0.8} ] }
                    """;
                Map m;
                try
                {
                    m = MapFile.Load(json, "Faces Test");
                }
                catch (Exception ex)
                {
                    Fail($"map Building op ({stories} storeys): didn't load: {ex.Message}");
                    continue;
                }
                checkedPlans++;
                var boxes = Enumerable.Range(0, m.Solid.Count).Select(i => (m.Solid[i], Key: (m.Colors[i], -1))).ToList();
                var problems = CoplanarFights(boxes, out var n, 0f).ToList();
                faces += n;
                bad += problems.Count;
                foreach (var p in problems.Take(4))
                    Fail($"map Building op ({stories} storeys, roof access {roof}): {p}");
            }
        Line($"{checkedPlans} building plans, {faces} coplanar same-facing face pairs of different surfaces, {bad} visible (z-fighting)");
    }

    /// <summary>
    /// Pairs of boxes with different <paramref name="boxes"/> keys (colour, texture layer) that have a face in the same
    /// plane, facing the same way, overlapping by more than a sliver, where some of the overlap is visible (just in front
    /// of it is not inside any box). <paramref name="pairs"/> counts the coplanar pairs found, visible or not. Undersides at
    /// <paramref name="groundY"/> (where the building stands on the ground) are never seen and are skipped.
    /// </summary>
    internal static List<string> CoplanarFights(List<(Box Box, (Vector3 Color, int Layer) Key)> boxes, out int pairs, float groundY)
    {
        const float Eps = 0.01f; // a crack narrower than this in front of the patch hides it
        var found = new List<string>();
        pairs = 0;
        // Faces bucketed by axis, side and plane (to the millimetre).
        var buckets = new Dictionary<(int Axis, int Side, long Plane), List<int>>();
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i].Box;
            for (var axis = 0; axis < 3; axis++)
                for (var side = 0; side < 2; side++)
                {
                    var p = side == 0 ? Comp(b.Min, axis) : Comp(b.Max, axis);
                    var key = (axis, side, (long)MathF.Round(p * 1000f));
                    if (!buckets.TryGetValue(key, out var l))
                        buckets[key] = l = new List<int>();
                    l.Add(i);
                }
        }
        foreach (var ((axis, side, plane), list) in buckets)
        {
            if (list.Count < 2 || axis == 1 && side == 0 && MathF.Abs(plane / 1000f - groundY) < 0.002f)
                continue;
            int u = (axis + 1) % 3, w = (axis + 2) % 3;
            for (var a = 0; a < list.Count; a++)
                for (var c = a + 1; c < list.Count; c++)
                {
                    var (ba, ka) = boxes[list[a]];
                    var (bc, kc) = boxes[list[c]];
                    if (ka == kc)
                        continue; // same colour and texture (world-space mapping): nothing to see
                    var u0 = MathF.Max(Comp(ba.Min, u), Comp(bc.Min, u));
                    var u1 = MathF.Min(Comp(ba.Max, u), Comp(bc.Max, u));
                    var w0 = MathF.Max(Comp(ba.Min, w), Comp(bc.Min, w));
                    var w1 = MathF.Min(Comp(ba.Max, w), Comp(bc.Max, w));
                    if (u1 - u0 < 0.025f || w1 - w0 < 0.025f || (u1 - u0) * (w1 - w0) < 0.01f)
                        continue; // slivers no wider than the 2 cm wall lining, and specks under 0.01 m2 (a sill end in a chimney)
                    pairs++;
                    // Visible: a point just in front of the shared patch that no box covers.
                    var n = side == 0 ? -1f : 1f;
                    var p = plane / 1000f + n * Eps;
                    Vector3? seen = null;
                    for (var i = 0; i < 5 && seen == null; i++)
                        for (var j = 0; j < 5 && seen == null; j++)
                        {
                            var pt = Make(axis, p, u, u0 + (u1 - u0) * (i + 0.5f) / 5f, w, w0 + (w1 - w0) * (j + 0.5f) / 5f);
                            if (!boxes.Any(x => Inside(x.Box, pt)))
                                seen = pt;
                        }
                    if (seen is { } s)
                        found.Add($"faces fight at ({s.X:0.##}, {s.Y:0.##}, {s.Z:0.##}), facing {"-+"[side]}{"xyz"[axis]}: "
                                  + $"{Describe(ba)} and {Describe(bc)}, {(u1 - u0) * (w1 - w0):0.###} m2 shared");
                }
        }
        return found;

        static float Comp(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
        static Vector3 Make(int a, float pa, int b, float pb, int c, float pc)
        {
            var v = new float[3];
            v[a] = pa;
            v[b] = pb;
            v[c] = pc;
            return new Vector3(v[0], v[1], v[2]);
        }
        static bool Inside(Box b, Vector3 p) =>
            p.X > b.Min.X && p.X < b.Max.X && p.Y > b.Min.Y && p.Y < b.Max.Y && p.Z > b.Min.Z && p.Z < b.Max.Z;
        static string Describe(Box b) => $"box ({b.Min.X:0.##}, {b.Min.Y:0.##}, {b.Min.Z:0.##})-({b.Max.X:0.##}, {b.Max.Y:0.##}, {b.Max.Z:0.##})";
    }
}
