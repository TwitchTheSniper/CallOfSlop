namespace CallOfSlop;

/// <summary>
/// Frenzy pacing (part of the "Frenzy music" section): pure maths on the shared heat model
/// (<see cref="FrenzyMeterCore"/>), no matches. The tuning is set for the kill pace a strong player keeps on a small
/// 8v8 map (a kill every 4-8 s during a streak), so the higher levels are attainable in small lobbies too. Pins the
/// kills needed per level at fixed intervals (the table the tuning was made from) and checks the design targets:
/// casual kills start it after 2-3 kills, a good streak (every 4-6 s) reaches level 4-5, a great one (every 3 s)
/// reaches MAX FRENZY in 10-12 kills, nothing maxes it instantly, a lone kill never starts it, Zombies hordes need
/// many more kills.
/// </summary>
public static partial class SelfTest
{
    /// <summary>For kills <paramref name="gap"/> s apart: the kill (1-based) on which each level 1-6 is first reached (0: never in <paramref name="kills"/>).</summary>
    private static int[] FrenzyKillsTo(double gap, bool headshots = false, bool zombies = false, int kills = 40)
    {
        var m = new FrenzyMeterCore { Zombies = zombies };
        var first = new int[FrenzyMeterCore.MaxLevel + 1];
        for (var k = 1; k <= kills; k++)
        {
            m.OnKill(k * gap, headshots);
            for (var l = 1; l <= m.Level; l++)
                if (first[l] == 0)
                    first[l] = k;
        }
        return first;
    }

    private static string FrenzyKillsText(int[] f) => string.Join("/", f.Skip(1).Select(k => k == 0 ? "-" : k.ToString()));

    private static void CheckFrenzyPacing()
    {
        // The table (no headshots): kills needed for levels 1..6 (0 = never in 40 kills). Also in the README and
        // FrenzyMeterCore's doc comment; a change here is a re-tune, so update those too.
        (double Gap, int[] Want)[] table =
        [
            (1.0, [2, 2, 3, 4, 6, 8]),
            (2.0, [2, 2, 3, 4, 6, 9]),
            (3.0, [2, 2, 3, 4, 6, 10]),
            (4.0, [2, 2, 3, 5, 7, 13]),
            (5.0, [2, 3, 4, 5, 8, 23]),
            (6.0, [2, 3, 4, 7, 0, 0]),
            (8.0, [2, 3, 6, 0, 0, 0]),
        ];
        var parts = new List<string>();
        foreach (var (gap, want) in table)
        {
            var got = FrenzyKillsTo(gap);
            parts.Add($"{gap:0}s {FrenzyKillsText(got)}");
            if (!got.Skip(1).SequenceEqual(want))
                Fail($"frenzy pacing: kills {gap} s apart reach levels 1-6 on kills {FrenzyKillsText(got)} (want {string.Join("/", want.Select(k => k == 0 ? "-" : k.ToString()))})");
        }
        FrenzyLine($"kills needed for L1..L6 (no headshots, - = never): {string.Join(", ", parts)}");

        // Design targets.
        var lone = new FrenzyMeterCore();
        lone.OnKill(0, true);
        if (lone.Level != 0)
            Fail($"a lone headshot kill starts the frenzy (level {lone.Level})");
        var casual = FrenzyKillsTo(8.0);
        if (casual[1] is 0 or > 2 || casual[2] is 0 or > 3)
            Fail($"a casual pace (a kill every 8 s) should give level 1 by the 2nd kill and 2 by the 3rd (got {FrenzyKillsText(casual)})");
        foreach (var gap in new[] { 4.0, 5.0, 6.0 })
        {
            var good = FrenzyKillsTo(gap);
            if (good[4] is 0 or > 8)
                Fail($"a good streak (a kill every {gap} s) should reach level 4 within 8 kills (got {FrenzyKillsText(good)})");
            if (gap <= 5 && good[5] is 0 or > 10)
                Fail($"a good streak (a kill every {gap} s) should reach level 5 within 10 kills (got {FrenzyKillsText(good)})");
        }
        var great = FrenzyKillsTo(3.0);
        if (great[6] is 0 or > 12 || great[6] < 8)
            Fail($"a great streak (a kill every 3 s) should reach MAX FRENZY on kill 8-12 (got {FrenzyKillsText(great)})");
        var frantic = FrenzyKillsTo(0.5, headshots: true);
        if (frantic[6] < 7)
            Fail($"headshot kills half a second apart max the frenzy on kill {frantic[6]} (want 7 or later: never instant)");
        if (FrenzyKillsTo(12.0, headshots: true)[1] != 0)
            Fail("a kill every 12 s starts the frenzy (it should stay off)");
        var horde = FrenzyKillsTo(1.0, zombies: true, kills: 60);
        var hordeFast = FrenzyKillsTo(0.5, zombies: true, kills: 60);
        FrenzyLine($"pacing targets: 8 s {FrenzyKillsText(casual)}; 3 s {FrenzyKillsText(great)}; 0.5 s headshots {FrenzyKillsText(frantic)}; "
                   + $"Zombies 1 s {FrenzyKillsText(horde)}, 0.5 s {FrenzyKillsText(hordeFast)}");
        if (horde[6] is 0 or < 15 or > 30 || hordeFast[6] < 12)
            Fail($"Zombies: kills a second apart max the frenzy on kill {horde[6]} (want about 15-30), half a second apart on {hordeFast[6]} (want 12 or later)");
    }
}
