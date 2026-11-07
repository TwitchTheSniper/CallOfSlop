using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// What two seeds of a shape share: the kinds of part they use (a plate is a plate in any material or size, a beam a beam, so paint and tiling don't count; a spring or a wheel counts as itself),
/// their bounding shape, and how they stand: wheelbase, track and axles.
/// 0 is the same vehicle; --sweep-vehicle fails a shape whose shipped seeds come closer than <see cref="Least"/>, so a generator can't ship seeds that only differ in colour and trophies.
/// </summary>
public static class VehicleVariety
{
    public const float Least = 0.25f;
    private const int MostOfAKind = 4; // (twenty plates against thirty say nothing about the look)

    public sealed record Signature(int Seed, Dictionary<string, int> Kinds, Vector3 Size, float Wheelbase, float Track, int Axles);

    public static Signature Of(int seed, Vehicle v)
    {
        var wheels = v.Wheels.Select(w => v.WheelAt(w)).ToList();
        return new(seed, v.PartNames().GroupBy(Kind).ToDictionary(g => g.Key, g => Math.Min(g.Count(), MostOfAKind)), v.Size(),
            wheels.Max(p => p.Z) - wheels.Min(p => p.Z), wheels.Max(p => p.X) - wheels.Min(p => p.X), wheels.Select(p => MathF.Round(p.Z, 1)).Distinct().Count());
    }

    /// <summary>A part's kind: plates are plates and beams beams, whatever their material or size; anything else is itself.</summary>
    public static string Kind(string part)
    {
        if (part.StartsWith(HullLoft.Prefix, StringComparison.Ordinal))
            return "Hull Loft " + part[HullLoft.Prefix.Length..].TakeWhile(c => c != ' ').Where(char.IsLetter).Aggregate("", (s, c) => s + c); // (a lofted bay's key is its shape: count it as a bay, a door bay, a brow or a tail, or every seed's bays would read as new kinds)
        foreach (var plate in (string[])["Plate Steel", "Sheet Steel", "Tread Plate", "Wood Planks", "Bolt Deck"])
            if (part.StartsWith(plate, StringComparison.Ordinal))
                return "plate";
        return part.StartsWith(Vehicle.Beam, StringComparison.Ordinal) ? "beam" : part;
    }

    /// <summary>Parts not shared (0 to 1) plus how far the bounding shapes differ (each axis as a fraction of the larger).</summary>
    public static float Distance(Signature a, Signature b)
    {
        float both = 0, either = 0;
        foreach (var k in a.Kinds.Keys.Union(b.Kinds.Keys))
        {
            var (x, y) = (a.Kinds.GetValueOrDefault(k), b.Kinds.GetValueOrDefault(k));
            both += Math.Min(x, y);
            either += Math.Max(x, y);
        }
        var d = Vector3.Abs(a.Size - b.Size) / Vector3.Max(a.Size, b.Size);
        return 1 - both / MathF.Max(1, either) + d.X + d.Y + d.Z + MathF.Abs(a.Wheelbase - b.Wheelbase) / MathF.Max(a.Wheelbase, b.Wheelbase) + MathF.Abs(a.Track - b.Track) / MathF.Max(a.Track, b.Track) + 0.25f * Math.Abs(a.Axles - b.Axles);
    }

    /// <summary>The closest pair: (distance, seed, seed), distance 9 for fewer than two.</summary>
    public static (float D, int A, int B) Closest(IReadOnlyList<Signature> list)
    {
        var best = (9f, 0, 0);
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                if (Distance(list[i], list[j]) < best.Item1)
                    best = (Distance(list[i], list[j]), list[i].Seed, list[j].Seed);
        return best;
    }

    /// <summary><paramref name="n"/> seeds picked far from each other: each next one the farthest from those already picked.</summary>
    public static List<Signature> Spread(IReadOnlyList<Signature> list, int n)
    {
        var picked = list.Take(1).ToList();
        while (picked.Count < n && picked.Count < list.Count)
            picked.Add(list.Except(picked).MaxBy(s => picked.Min(p => Distance(p, s)))!);
        return picked;
    }
}
