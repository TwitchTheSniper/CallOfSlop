namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The rarity power curve (docs/zombies/WEAPON-BUILDER.md, "The power curve"): in every category the best part of a
    /// tier is never weaker than the best of the tier below, and in the damage rows each tier's best reaches its floor
    /// times the best common part.
    /// </summary>
    private static void CheckPowerCurve()
    {
        var names = new[] { "common", "uncommon", "rare", "epic", "legendary", "wonder" };
        foreach (var (row, tiers) in PowerCurve.Table())
        {
            Line($"{row.Name} ({row.Measures}): " + string.Join(" | ", tiers.Select((t, i) => t.Count == 0 ? $"{names[i]} -" :
                $"{names[i]} best {t[0].Score:0.###} ({t[0].Part.Name}), {t.Count} parts: " + string.Join(", ", t.Select(x => $"{x.Part.Name} {x.Score:0.###}")))));
            var common = tiers[0].Count > 0 ? tiers[0][0].Score : 0f;
            var last = 0f;
            var lastTier = -1;
            for (var i = 0; i < tiers.Length; i++)
            {
                if (tiers[i].Count == 0)
                    continue;
                var best = tiers[i][0].Score;
                if (lastTier >= 0 && best < last * 0.999f)
                    Fail($"power curve: {row.Name}: the best {names[i]} part ({tiers[i][0].Part.Name}, {best:0.###}) is weaker than the best {names[lastTier]} one ({last:0.###})");
                if (row.Damage && common > 0 && best < common * PowerCurve.DamageFloor[i] * 0.999f)
                    Fail($"power curve: {row.Name}: the best {names[i]} part ({tiers[i][0].Part.Name}, {best:0.###}) should be at least x{PowerCurve.DamageFloor[i]} the best common ({common:0.###})");
                last = best;
                lastTier = i;
            }
        }
    }
}
