using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// How different two generated vehicles are, from cheap features of the built body (docs/vehiclekit/VEHICLEKIT.md, "Quality tools"). Four parts, each 0 to 1:
/// <list type="bullet">
/// <item><b>front</b> (weight 0.15, since 2026-11-07): the cross-section from the front (<see cref="FrontProfile"/>): the rooms' when both have rooms, and the whole body's
/// (wheels left out), each the mean difference per band times <see cref="FrontScale"/>; what the user sees from the front and the rear that the side outline can't.</item>
/// <item><b>silhouette</b> (weight 0.35; 0.40 before the front term): the side fill, plan width and top height along the length in <see cref="Bins"/> slices, each in its own bounding box (<see cref="VehiclePlan.Profiles"/>), plus the
/// whole side and plan fill that <c>MaxSideFill</c>/<c>MaxTopFill</c> judge; the mean difference per slice.</item>
/// <item><b>parts</b> (0.15; 0.20 before): kinds of part (<see cref="VehicleVariety.Kind"/>, counts capped at <see cref="MostOfAKind"/>), one less their weighted overlap.</item>
/// <item><b>layout</b> (0.20; 0.25 before): rooms, turrets, seats, axles, wheelbase, track, and the sets of wheel and spring parts.</item>
/// <item><b>size</b> (0.15): the bounding box, each axis as a fraction of the larger.</item>
/// </list>
/// The weighted sum is 0 for the same vehicle and 1 for vehicles that share nothing. Paint and loot don't count: they are not what a player calls "the same truck".
/// </summary>
public static class VehicleDiversity
{
    public const int Bins = 16;
    public const int MostOfAKind = 8;
    public const float WSilhouette = 0.35f, WParts = 0.15f, WLayout = 0.20f, WSize = 0.15f, WFront = 0.15f;

    /// <summary>The weights before the front term (2026-11-07), for <c>--no-sections</c>: the old distance, to measure against.</summary>
    public const float OldSilhouette = 0.40f, OldParts = 0.20f, OldLayout = 0.25f;

    /// <summary>A front profile's mean difference (0 to about 0.3, a box against a triangle; styles' rooms differ by 0.05 to 0.16) times this, capped at 1.</summary>
    public const float FrontScale = 3f;

    /// <summary><c>--no-sections</c>: the distance without the front term and identity without the section, as both were before 2026-11-07.</summary>
    public static bool SectionsOn = true;

    /// <summary>Two seeds closer than this read as the same vehicle to a person looking at them (set from a look at Bandit and IT sweeps).</summary>
    public const float Alike = 0.08f;

    public sealed record Features(
        string Name, int Seed,
        float[] Side, float[] Top, float[] Height, float SideFill, float TopFill,
        Dictionary<string, int> Kinds,
        int Rooms, int Turrets, int Seats, int Axles, int Wheels, float Wheelbase, float Track, string[] WheelParts, string[] Springs,
        Vector3 Size, int Parts, float Mass,
        Dictionary<string, string> Traits)
    {
        /// <summary>The rooms' cross-section from the front (zeros without a room) and the whole body's, wheels left out (<see cref="FrontProfile"/>).</summary>
        public float[] RoomFront { get; init; } = new float[FrontProfile.Bins];
        public float[] BodyFront { get; init; } = new float[FrontProfile.Bins];

        /// <summary>Phase 3: the cab and hood seen from the front and their outline from the side (the height along their length: a screen's rake, a hood's length), <see cref="FrontProfile.OfCab"/>.</summary>
        public float[] CabFront { get; init; } = new float[FrontProfile.Bins];
        public float[] CabSide { get; init; } = new float[FrontProfile.Bins];
    }

    /// <summary>The parts of a distance, and their weighted sum.</summary>
    public readonly record struct Parts(float Silhouette, float Kinds, float Layout, float Size, float Front = 0)
    {
        public float Total => SectionsOn
            ? WSilhouette * Silhouette + WParts * Kinds + WLayout * Layout + WSize * Size + WFront * Front
            : OldSilhouette * Silhouette + OldParts * Kinds + OldLayout * Layout + WSize * Size;
    }

    /// <summary>Generates one seed with its picks traced, builds it from its own text, and measures it. Throws what the generator throws.</summary>
    /// <remarks><c>Ms</c> is the generation alone (the generator and the blueprint's text), not the measuring.</remarks>
    public static (Features F, Vehicle V, string Text, double Ms) Generate(IVehicleGenerator g, int seed)
    {
        var trace = new List<(string Slot, string Value)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        FactionStyle.Trace = trace;
        Vehicle v;
        try
        {
            v = VehicleRegistry.Make(g, seed);
        }
        finally
        {
            FactionStyle.Trace = null;
        }
        var text = v.Text();
        var ms = clock.Elapsed.TotalMilliseconds;
        return (Of(g, seed, v, text, trace), v, text, ms);
    }

    /// <summary>Builds the vehicle from its blueprint text, as the game would, and reads its features.</summary>
    public static Features Of(IVehicleGenerator g, int seed, Vehicle v, string text, List<(string Slot, string Value)> trace)
    {
        var bp = ConstructBuild.ParseBlueprint(text, $"{seed}.json");
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out var why, VehicleChecks.TowOf(bp)) ?? throw new VehicleKitException($"it doesn't build: {why}");
        var (side, top, height) = VehiclePlan.Profiles(c, Bins);
        var (sideFill, topFill, _) = VehiclePlan.Outline(c);
        var kinds = new Dictionary<string, int>();
        var springs = new SortedSet<string>(StringComparer.Ordinal);
        var wheelParts = new SortedSet<string>(StringComparer.Ordinal);
        var seats = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            var k = VehicleVariety.Kind(d.Name);
            kinds[k] = Math.Min(MostOfAKind, kinds.GetValueOrDefault(k) + 1);
            if (d.Role == PartRole.Suspension)
                springs.Add(d.Name);
            else if (d.Role == PartRole.Wheel)
                wheelParts.Add(d.Name);
            else if (d.Role == PartRole.Seat)
                seats++;
        }
        float zF = float.MaxValue, zB = float.MinValue, xL = float.MaxValue, xR = float.MinValue;
        var axles = new HashSet<float>();
        for (var k = 0; k < c.WheelCount; k++)
        {
            var p = c.Wheels[k].Centre;
            (zF, zB, xL, xR) = (MathF.Min(zF, p.Z), MathF.Max(zB, p.Z), MathF.Min(xL, p.X), MathF.Max(xR, p.X));
            axles.Add(MathF.Round(p.Z * 2) / 2); // (a track unit's rays sit within half a metre of their wheel)
        }
        var size = c.LocalMax - c.LocalMin;
        var (tris, owner) = VehiclePlan.PartTriangles(v.Built);
        var (roomFront, bodyFront) = FrontProfile.OfVehicle(v, tris, owner);
        var (cabFront, cabSide) = FrontProfile.OfCab(v, tris, owner);
        var f = new Features(g.Name, seed, side, top, height, sideFill, topFill, kinds,
            v.Rooms.Count, v.Turrets.Count, seats, axles.Count, c.WheelCount, c.WheelCount > 0 ? zB - zF : 0, c.WheelCount > 0 ? xR - xL : 0,
            wheelParts.ToArray(), springs.ToArray(), size, c.PartCount, c.Mass, new Dictionary<string, string>(StringComparer.Ordinal)) { RoomFront = roomFront, BodyFront = bodyFront, CabFront = cabFront, CabSide = cabSide };
        Traits(f, v, trace);
        return f;
    }

    /// <summary>
    /// The knobs a seed turned, by name: each style slot it picked from (the choice it picked most), the gear recipe, the weapons, and the layout bucketed so a spread can be read:
    /// rooms, turrets, axles, length and height to the half metre.
    /// </summary>
    private static void Traits(Features f, Vehicle v, List<(string Slot, string Value)> trace)
    {
        foreach (var g in trace.GroupBy(p => p.Slot))
            f.Traits[g.Key] = g.GroupBy(p => p.Value).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;
        f.Traits["=weapons"] = v.Turrets.Count == 0 ? "none" : string.Join("+", v.Turrets.Select(t => t.Weapon.Name).OrderBy(n => n, StringComparer.Ordinal));
        f.Traits["=rooms"] = f.Rooms.ToString();
        f.Traits["=turrets"] = f.Turrets.ToString();
        f.Traits["=axles"] = f.Axles.ToString();
        f.Traits["=wheels"] = string.Join("+", f.WheelParts);
        f.Traits["=springs"] = string.Join("+", f.Springs);
        f.Traits["=suspension"] = string.Join("+", f.Springs.Select(VehicleChassis.TypeOf).Distinct().OrderBy(n => n, StringComparer.Ordinal)); // (the kinds: leaf, coil, arms...)
        f.Traits["=wheelbase"] = $"{MathF.Round(f.Wheelbase * 2) / 2:0.0} m";
        f.Traits["=wheelbase share"] = $"{MathF.Round(f.Wheelbase / MathF.Max(1, f.Size.Z) * 20) / 20:0.00}"; // (of the length: where the axles stand, whatever the size)
        f.Traits["=track"] = $"{MathF.Round(f.Track * 4) / 4:0.00} m";
        f.Traits["=length"] = $"{MathF.Round(f.Size.Z * 2) / 2:0.0} m";
        f.Traits["=height"] = $"{MathF.Round(f.Size.Y * 2) / 2:0.0} m";
    }

    public static Parts Between(Features a, Features b)
    {
        static float Profile(float[] x, float[] y)
        {
            var s = 0f;
            for (var i = 0; i < x.Length; i++)
                s += MathF.Abs(x[i] - y[i]);
            return s / x.Length;
        }
        var silhouette = (Profile(a.Side, b.Side) + Profile(a.Top, b.Top) + Profile(a.Height, b.Height) + MathF.Abs(a.SideFill - b.SideFill) + MathF.Abs(a.TopFill - b.TopFill)) / 5;
        float both = 0, either = 0;
        foreach (var k in a.Kinds.Keys.Union(b.Kinds.Keys))
        {
            var (x, y) = (a.Kinds.GetValueOrDefault(k), b.Kinds.GetValueOrDefault(k));
            both += Math.Min(x, y);
            either += Math.Max(x, y);
        }
        var kinds = 1 - both / MathF.Max(1, either);
        var layout = (Rel(a.Rooms, b.Rooms) + Rel(a.Turrets, b.Turrets) + Rel(a.Seats, b.Seats) + Rel(a.Axles, b.Axles) + Rel(a.Wheelbase, b.Wheelbase) + Rel(a.Track, b.Track)
            + Jaccard(a.WheelParts, b.WheelParts) + Jaccard(a.Springs, b.Springs)) / 8;
        var size = (Rel(a.Size.X, b.Size.X) + Rel(a.Size.Y, b.Size.Y) + Rel(a.Size.Z, b.Size.Z)) / 3;
        var body = FrontProfile.Dist(a.BodyFront, b.BodyFront);
        var room = a.Rooms > 0 && b.Rooms > 0 ? FrontProfile.Dist(a.RoomFront, b.RoomFront) : body;
        var cab = (FrontProfile.Dist(a.CabFront, b.CabFront) + FrontProfile.Dist(a.CabSide, b.CabSide)) / 2; // (phase 3: the cab and hood, which were one fixed part each)
        var front = MathF.Min(1, FrontScale * (body + room + cab) / 3);
        return new Parts(silhouette, kinds, layout, size, front);
    }

    public static float Distance(Features a, Features b) => Between(a, b).Total;

    private static float Rel(float a, float b) => MathF.Max(a, b) <= 1e-4f ? 0 : MathF.Abs(a - b) / MathF.Max(a, b);

    private static float Rel(int a, int b) => Math.Abs(a - b) / (float)Math.Max(Math.Max(a, b), 1);

    private static float Jaccard(string[] a, string[] b)
    {
        var union = a.Union(b).Count();
        return union == 0 ? 0 : 1 - a.Intersect(b).Count() / (float)union;
    }

    /// <summary>A set's spread: the mean over every pair, and each one's nearest other (mean and least).</summary>
    public sealed record Summary(int Count, float MeanPair, float MeanNearest, float LeastNearest, (int A, int B) Closest, Parts MeanParts);

    public static Summary Summarise(IReadOnlyList<Features> list)
    {
        if (list.Count < 2)
            return new Summary(list.Count, 0, 0, 0, (0, 0), default);
        var nearest = Enumerable.Repeat(float.MaxValue, list.Count).ToArray();
        float sum = 0, s = 0, k = 0, l = 0, z = 0, fr = 0;
        var pairs = 0;
        var closest = (9f, 0, 0);
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                var p = Between(list[i], list[j]);
                var d = p.Total;
                (sum, s, k, l, z, fr, pairs) = (sum + d, s + p.Silhouette, k + p.Kinds, l + p.Layout, z + p.Size, fr + p.Front, pairs + 1);
                nearest[i] = MathF.Min(nearest[i], d);
                nearest[j] = MathF.Min(nearest[j], d);
                if (d < closest.Item1)
                    closest = (d, list[i].Seed, list[j].Seed);
            }
        return new Summary(list.Count, sum / pairs, nearest.Average(), nearest.Min(), (closest.Item2, closest.Item3), new Parts(s / pairs, k / pairs, l / pairs, z / pairs, fr / pairs));
    }

    /// <summary>Groups of seeds joined by any pair closer than <paramref name="below"/> (single linkage): each group reads as one vehicle. Only groups of two or more.</summary>
    public static List<List<Features>> Clusters(IReadOnlyList<Features> list, float below)
    {
        var parent = Enumerable.Range(0, list.Count).ToArray();
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                if (Distance(list[i], list[j]) < below)
                    parent[Root(i)] = Root(j);
        return Enumerable.Range(0, list.Count).GroupBy(Root).Where(g => g.Count() > 1).Select(g => g.Select(i => list[i]).ToList()).OrderByDescending(g => g.Count).ToList();
    }

    /// <summary>One trait over a set of seeds: how many values it took and how often the commonest came up.</summary>
    public sealed record Spread(string Trait, int Values, string Commonest, float Share, int Seen);

    public static List<Spread> Traits(IReadOnlyList<Features> list)
    {
        var names = list.SelectMany(f => f.Traits.Keys).Distinct().OrderBy(n => n, StringComparer.Ordinal);
        var result = new List<Spread>();
        foreach (var n in names)
        {
            var values = list.Where(f => f.Traits.ContainsKey(n)).Select(f => f.Traits[n]).ToList();
            var top = values.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First();
            result.Add(new Spread(n, values.Distinct().Count(), top.Key, top.Count() / (float)values.Count, values.Count));
        }
        return result;
    }

    /// <summary>The mean distance between two sets (two shapes of a style): how far apart they read as a whole.</summary>
    public static float Apart(IReadOnlyList<Features> a, IReadOnlyList<Features> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return 0;
        var s = 0f;
        foreach (var x in a)
            foreach (var y in b)
                s += Distance(x, y);
        return s / (a.Count * b.Count);
    }
}
