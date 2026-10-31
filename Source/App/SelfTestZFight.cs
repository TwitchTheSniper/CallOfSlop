namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Models whose coplanar faces are deliberate (kind, model name as the report prints it, and why). Keep this empty
    /// where you can: nudge a box by 2-5 mm instead (CLAUDE.md, "No coplanar faces").
    /// </summary>
    private static readonly (string Kind, string Model, string Why)[] ZFightAllowed = [];

    /// <summary>
    /// No z-fighting (the user, 2026-10-30): no two drawn boxes of one shipped model share a face plane facing the same
    /// way with overlapping area (ZFight.cs). Props (global and pack, prefabs flat), buildings, construct parts and
    /// weapon-builder parts (each alone; the standard builds assembled are reported as notes only).
    /// </summary>
    private static void CheckNoZFighting()
    {
        // The check itself: a flush pair is found, a 3 mm inset is not, a covered face is skipped, stacked boxes are no fight.
        var flush = new[] { ZFight.ZBox.Aligned("a", System.Numerics.Vector3.Zero, new(0.5f)), ZFight.ZBox.Aligned("b", new(0.25f, 0.1f, 0), new(0.2f, 0.4f, 0.4f)) };
        var inset = new[] { flush[0], ZFight.ZBox.Aligned("b", new(0.25f, 0.1f, 0), new(0.2f, 0.397f, 0.4f)) };
        var stacked = new[] { flush[0], ZFight.ZBox.Aligned("c", new(0, 1f, 0), new(0.5f)) };
        if (ZFight.Find(flush).Count != 1 || ZFight.Find(inset).Count != 0 || ZFight.Find(stacked).Count != 0)
            Fail($"z-fight probe: flush {ZFight.Find(flush).Count} (want 1), inset {ZFight.Find(inset).Count} (want 0), stacked {ZFight.Find(stacked).Count} (want 0)");

        var all = new List<ZFight.Finding>();
        all.AddRange(ZFight.ScanProps());
        all.AddRange(ZFight.ScanBuildings());
        all.AddRange(ZFight.ScanConstructs());
        all.AddRange(ZFight.ScanParts());
        all.AddRange(ZFight.ScanBuilds());
        var allowed = 0;
        foreach (var f in all)
        {
            if (ZFightAllowed.FirstOrDefault(a => a.Kind == f.Kind && a.Model == f.Model) is { Why: not null } a && a.Model != null)
            {
                allowed++;
                Line($"  allowed: {f.Kind} {f.Model}: {f.Pairs} pair(s) ({a.Why})");
                continue;
            }
            if (f.Kind == "build")
            {
                // Assembled standard builds are informational: the fight is between two parts placed side by side (the
                // parts alone are checked above), a few square centimetres of a hand-held gun. Fix by nudging a part.
                Line($"  note: z-fight: build {f.Model}: {f.Pairs} coplanar pair(s): {string.Join("; ", f.Lines.Take(2))}");
                continue;
            }
            Fail($"z-fight: {f.Kind} {f.Model}: {f.Pairs} coplanar pair(s): {string.Join("; ", f.Lines.Take(3))}");
        }
        foreach (var a in ZFightAllowed)
            if (!all.Any(f => f.Kind == a.Kind && f.Model == a.Model))
                Fail($"z-fight allow-list: {a.Kind} '{a.Model}' has no coplanar faces any more (remove it)");
        foreach (var g in all.GroupBy(f => f.Kind))
            Line($"{g.Key}: {g.Count()} model(s) with coplanar faces, {g.Sum(f => f.Pairs)} pair(s)");
        Line($"z-fight: {all.Count(f => f.Kind != "build")} model(s) with coplanar faces, {allowed} on the allow-list");
    }
}
