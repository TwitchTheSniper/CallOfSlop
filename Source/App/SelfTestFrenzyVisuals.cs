using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Frenzy visuals" self-test section: the per-class frenzy looks (Source/Render/FrenzyFx): every class has its own
/// look, level scaling, the setting, no strobing, edge tint limits, the classes.json "visual" overrides, the tracer hook
/// on a real offline match, no per-frame allocations, and the CPU cost of a full lobby at level 6.
/// </summary>
public static partial class SelfTest
{
    /// <summary>CPU budget per frame for a 16-player lobby all at frenzy level 6 firing LMGs (particles, glows, tracer hooks).</summary>
    private const double FrenzyFxCpuBudgetMs = 0.1;

    private static readonly List<string> FrenzyFxNotes = [];

    /// <summary>A result line, also collected for the run's "Frenzy visuals (results)" annotation.</summary>
    private static void FrenzyFxLine(string s)
    {
        Line(s);
        FrenzyFxNotes.Add(s);
    }

    private static void CheckFrenzyVisuals()
    {
        FrenzyFxNotes.Clear();
        var mode = FrenzyFx.Mode;
        var over = FrenzyFx.Override;
        try
        {
            FrenzyVisualsMapping();
            FrenzyVisualsScaling();
            FrenzyVisualsStrobe();
            FrenzyVisualsJson();
            FrenzyVisualsSettings();
            FrenzyVisualsMatch();
        }
        finally
        {
            FrenzyFx.Mode = mode;
            FrenzyFx.Override = over;
            FrenzyFx.LocalLevel = 0;
            FrenzyParticles.Clear();
            Line("::warning title=Frenzy visuals (results)::" + string.Join("%0A", FrenzyFxNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    // ---- Every class has a look, and each one is its own ----
    private static void FrenzyVisualsMapping()
    {
        var all = FrenzyLooks.Defaults();
        var classes = Enum.GetValues<WeaponClass>();
        foreach (var c in classes)
        {
            var l = FrenzyLooks.Get(c);
            if (l.Class != c || string.IsNullOrWhiteSpace(l.Name))
                Fail($"frenzy visuals: class {c} has no look of its own (got {l.Class} '{l.Name}')");
            if (l.Color.Length() < 0.5f || l.EdgeAlpha > FrenzyLooks.MaxEdgeAlpha || l.TracerWidth < 1f || l.BarrelGlow <= 0)
                Fail($"frenzy visuals: {c}'s look is too faint or out of range (colour {l.Color}, edge {l.EdgeAlpha}, width x{l.TracerWidth}, glow {l.BarrelGlow})");
        }
        if (all.Length != classes.Length)
            Fail($"frenzy visuals: {all.Length} looks for {classes.Length} weapon classes");
        var sigs = all.Select(l => l.Signature).Distinct().Count();
        if (sigs != all.Length)
            Fail($"frenzy visuals: only {sigs} distinct signature effects for {all.Length} classes");
        var closest = float.MaxValue;
        var pair = "";
        for (var i = 0; i < all.Length; i++)
            for (var j = i + 1; j < all.Length; j++)
            {
                var d = Vector3.Distance(all[i].Color, all[j].Color);
                if (d < closest)
                    (closest, pair) = (d, $"{all[i].Class}/{all[j].Class}");
            }
        if (closest < 0.15f)
            Fail($"frenzy visuals: {pair} colours are too alike ({closest:0.00})");
        FrenzyFxLine($"mapping: {all.Length} classes, {sigs} signatures ({string.Join(", ", all.Select(l => $"{l.Class}={l.Signature}"))}); closest colours {pair} {closest:0.00}");
    }

    // ---- Subtle at 1-2, strong at 5-6; the setting ----
    private static void FrenzyVisualsScaling()
    {
        var prev = -1f;
        for (var lv = 0; lv <= FrenzyMeter.MaxLevel; lv++)
        {
            var k = FrenzyLooks.Intensity(lv);
            if (k < prev)
                Fail($"frenzy visuals: intensity drops from level {lv - 1} to {lv} ({prev} -> {k})");
            prev = k;
            var sub = FrenzyLooks.Intensity(lv, FrenzyVisualMode.Subtle);
            var off = FrenzyLooks.Intensity(lv, FrenzyVisualMode.Off);
            if (off != 0 || FrenzyLooks.TracerBlend(lv, FrenzyVisualMode.Off) != 0 || (lv > 0 && !(sub < k && sub > 0)))
                Fail($"frenzy visuals: level {lv}: Full {k}, Subtle {sub}, Off {off} (want Off 0 < Subtle < Full)");
            if (FrenzyLooks.TracerBlend(lv, FrenzyVisualMode.Full) is var b && (b < 0 || b > 1))
                Fail($"frenzy visuals: tracer blend {b} at level {lv}");
        }
        if (FrenzyLooks.Intensity(0) != 0 || FrenzyLooks.Intensity(1) > 0.3f || FrenzyLooks.Intensity(2) > 0.3f || FrenzyLooks.Intensity(5) < 0.85f || FrenzyLooks.Intensity(6) != 1f)
            Fail($"frenzy visuals: levels 0-6 give {string.Join(" ", FrenzyLooks.LevelIntensity)} (want 0, subtle at 1-2, strong at 5-6, 1 at 6)");
        if (FrenzyLooks.Intensity(-3) != 0 || FrenzyLooks.Intensity(99) != 1)
            Fail("frenzy visuals: out-of-range levels aren't clamped");
        if (FrenzyLooks.ScreenEffects(FrenzyVisualMode.Subtle) || FrenzyLooks.ScreenEffects(FrenzyVisualMode.Off) || !FrenzyLooks.ScreenEffects(FrenzyVisualMode.Full))
            Fail("frenzy visuals: screen effects should be on in Full only");
        FrenzyFxLine($"scaling: Full {string.Join(" ", Enumerable.Range(0, 7).Select(l => FrenzyLooks.Intensity(l).ToString("0.00")))}; "
             + $"Subtle {string.Join(" ", Enumerable.Range(0, 7).Select(l => FrenzyLooks.Intensity(l, FrenzyVisualMode.Subtle).ToString("0.00")))}; Off 0");
    }

    /// <summary>Peaks (local maxima above 10% swing) per second of <paramref name="f"/> sampled at 1 kHz for 20 s.</summary>
    private static double PeaksPerSecond(Func<double, float> f)
    {
        const double dt = 0.001, seconds = 20;
        int peaks = 0;
        float a = f(0), b = f(dt), lo = MathF.Min(a, b);
        var rising = b > a;
        for (var t = 2 * dt; t < seconds; t += dt)
        {
            var c = f(t);
            if (rising && c < b && b - lo > 0.1f * MathF.Max(MathF.Abs(b), 1e-3f))
            {
                peaks++;
                lo = c;
            }
            if (c < lo)
                lo = c;
            rising = c > b || (c == b && rising);
            b = c;
        }
        return peaks / seconds;
    }

    // ---- Nothing strobes faster than 3 Hz; the edge tint stays at the edges and faint ----
    private static void FrenzyVisualsStrobe()
    {
        var worst = 0.0;
        var what = "";
        void Check(string name, double hz)
        {
            if (hz > worst)
                (worst, what) = (hz, name);
            if (hz > FrenzyLooks.MaxFlashHz + 0.05)
                Fail($"frenzy visuals: {name} changes {hz:0.00} times per second (max {FrenzyLooks.MaxFlashHz} Hz)");
        }
        for (var m = 0; m <= FrenzyMeter.MaxLevel; m++)
        {
            Check($"pulse rate at music level {m}", FrenzyLooks.PulseHz(m));
            var hz = FrenzyLooks.PulseHz(m);
            Check($"edge pulse at music level {m} (sampled)", PeaksPerSecond(t => FrenzyLooks.Pulse(t, hz)));
        }
        Check("glitch flicker (sampled)", PeaksPerSecond(t => FrenzyLooks.Flicker(t)));
        Check("glow breathing", FrenzyLooks.GlowBreathHz);
        Check("glow breathing (sampled)", PeaksPerSecond(t => FrenzyLooks.Pulse(t, FrenzyLooks.GlowBreathHz)));
        // A kill pulse is one swell: even kills at the fastest possible rate (every frame) can't make it flash.
        FrenzyFx.KillAt = 0;
        var swells = PeaksPerSecond(t => FrenzyFx.KillPulse(t % 20 < FrenzyFx.KillPulseSeconds ? t : -1)) * 20;
        if (swells > 1.01)
            Fail($"frenzy visuals: one kill pulse rises and falls {swells:0} times");

        var maxEdge = 0f;
        foreach (var look in FrenzyLooks.Defaults())
            foreach (var md in Enum.GetValues<FrenzyVisualMode>())
                for (var lv = 0; lv <= 6; lv++)
                    for (var t = 0.0; t < 3; t += 0.013)
                    {
                        var a = FrenzyLooks.EdgeAlpha(look, lv, 6, md, t);
                        maxEdge = MathF.Max(maxEdge, a);
                        if (a > FrenzyLooks.MaxEdgeAlpha || (md == FrenzyVisualMode.Off && a != 0) || (lv == 0 && a != 0))
                            Fail($"frenzy visuals: edge tint {a:0.000} for {look.Class} level {lv} {md}");
                    }
        var edgeDepth = FrenzyLooks.EdgeDepth; // a variable, so the check isn't folded away at compile time
        if (edgeDepth > 0.2f)
            Fail($"frenzy visuals: the edge tint reaches {FrenzyLooks.EdgeDepth:P0} into the screen (the middle must stay clear)");
        FrenzyFxLine($"strobe: fastest light change {worst:0.00} Hz ({what}), max {FrenzyLooks.MaxFlashHz} Hz; edge tint at most {maxEdge:0.000} alpha, {FrenzyLooks.EdgeDepth:P0} deep at the edges");
    }

    // ---- classes.json "visual" overrides ----
    private static void FrenzyVisualsJson()
    {
        var (looks, notes) = FrenzyLooks.Parse("""
            { "format": "call-of-slop-frenzy-classes/1",
              "classes": {
                "Pistol": { "music": { "lead": "whistle" }, "visual": { "color": "#ff0000", "muzzleSparks": 99, "edgeAlpha": 0.9, "wobble": 1 } },
                "Assault Rifle": { "visual": { "accent": [0, 1, 0], "trailLife": 0.5 } },
                "Bazooka": { "visual": { "color": "#00ff00" } },
                "SMG": { "buffs": { "sprintToFire": 0.8 } }
              } }
            """);
        var p = looks[(int)WeaponClass.Pistol];
        var ar = looks[(int)WeaponClass.AssaultRifle];
        var smg = looks[(int)WeaponClass.SMG];
        var def = FrenzyLooks.Defaults();
        if (p.Color != new Vector3(1, 0, 0) || p.MuzzleSparks != 12 || p.EdgeAlpha != FrenzyLooks.MaxEdgeAlpha || ar.Accent != new Vector3(0, 1, 0) || MathF.Abs(ar.TrailLife - 0.5f) > 1e-6f
            || smg.Color != def[(int)WeaponClass.SMG].Color || looks.Length != def.Length)
            Fail($"frenzy visuals: classes.json overrides not applied right (pistol {p.Color} x{p.MuzzleSparks} edge {p.EdgeAlpha}; AR accent {ar.Accent} trail {ar.TrailLife})");
        if (!notes.Any(n => n.Contains("Bazooka")) || !notes.Any(n => n.Contains("wobble")))
            Fail($"frenzy visuals: classes.json notes missed the unknown class / key: {string.Join("; ", notes)}");
        var (arr, _) = FrenzyLooks.Parse("""{ "classes": [ { "class": "marksman", "visual": { "color": [0.1, 0.2, 0.3], "rainbow": true } } ] }""");
        if (arr[(int)WeaponClass.DMR].Color != new Vector3(0.1f, 0.2f, 0.3f) || !arr[(int)WeaponClass.DMR].Rainbow)
            Fail("frenzy visuals: the array form of classes.json isn't read");
        // The built-in looks written out as "visual" sections read back the same.
        var back = FrenzyLooks.Parse("{ \"classes\": { " + string.Join(", ", def.Select(l => $"\"{l.Class}\": {{ \"visual\": {FrenzyLooks.VisualJson(l)} }}")) + " } }").Looks;
        for (var i = 0; i < def.Length; i++)
            if (Vector3.Distance(back[i].Color, def[i].Color) > 0.01f || back[i].MuzzleSparks != def[i].MuzzleSparks || MathF.Abs(back[i].TrailLife - def[i].TrailLife) > 1e-4f)
                Fail($"frenzy visuals: {def[i].Class}'s look doesn't survive a write and read of its visual section");
        // The shipped Weapons/Frenzy/classes.json: its "visual" sections are clean and match the built-in looks.
        if (File.Exists(FrenzyLooks.DataFile))
        {
            var (shipped, shippedNotes) = FrenzyLooks.Parse(File.ReadAllText(FrenzyLooks.DataFile));
            if (shippedNotes.Count > 0)
                Fail($"frenzy visuals: Weapons/Frenzy/classes.json visual notes: {string.Join("; ", shippedNotes)}");
            for (var i = 0; i < def.Length; i++)
                if (Vector3.Distance(shipped[i].Color, def[i].Color) > 0.01f || Vector3.Distance(shipped[i].Accent, def[i].Accent) > 0.01f
                    || shipped[i].ImpactSparks != def[i].ImpactSparks || MathF.Abs(shipped[i].TrailLife - def[i].TrailLife) > 1e-4f || shipped[i].Rainbow != def[i].Rainbow)
                    Fail($"frenzy visuals: classes.json's {def[i].Class} visual section differs from the built-in look");
        }
        FrenzyFxLine($"classes.json: overrides, clamps and both layouts read; {notes.Count} notes; data file {(File.Exists(FrenzyLooks.DataFile) ? "present" : "absent (built-in looks)")}");
    }

    // ---- settings.txt: frenzy_visuals ----
    private static void FrenzyVisualsSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-frenzyfx-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllLines(tmp, ["name=Old", "fov=90", "frenzy_music=1"]); // from before the setting
            var old = Settings.Load(tmp);
            var ok = new Settings().FrenzyVisuals == FrenzyVisualMode.Full && old.FrenzyVisuals == FrenzyVisualMode.Full && old.Fov == 90;
            foreach (var m in Enum.GetValues<FrenzyVisualMode>())
            {
                new Settings { FrenzyVisuals = m }.Save(tmp);
                ok &= Settings.Load(tmp).FrenzyVisuals == m;
            }
            File.WriteAllLines(tmp, ["frenzy_visuals=7"]);
            ok &= Settings.Load(tmp).FrenzyVisuals == FrenzyVisualMode.Full;
            File.WriteAllLines(tmp, ["frenzy_visuals=sparkly"]);
            ok &= Settings.Load(tmp).FrenzyVisuals == FrenzyVisualMode.Full;
            File.WriteAllLines(tmp, ["frenzy_visuals=subtle"]);
            ok &= Settings.Load(tmp).FrenzyVisuals == FrenzyVisualMode.Subtle;
            if (!ok)
                Fail("frenzy visuals: the setting doesn't default to Full, round-trip Full/Subtle/Off, or reject junk");
            FrenzyFxLine($"settings: {string.Join(" / ", Enum.GetNames<FrenzyVisualMode>())}, default {new Settings().FrenzyVisuals}, old files load as Full");
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { }
        }
    }

    // ---- A real offline match: the tracer hook, Off/Subtle/Full, allocations, cost ----
    private static void FrenzyVisualsMatch()
    {
        // A map that takes 16 players, so the cost check below is a full 16-player lobby at level 6.
        var map = Maps.All.Where(m => m.MaxPlayers >= 16).OrderBy(m => m.MaxPlayers).FirstOrDefault()
            ?? Maps.All.OrderByDescending(m => m.MaxPlayers).First();
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 15, mapId: map.Id);
        game.SelfTestStreakTick(0);
        if (game.Local is not { } me)
        {
            Fail("frenzy visuals: no local player in the offline match");
            return;
        }
        var players = game.Players.Values.ToArray();
        if (players.Length < 16)
            Fail($"frenzy visuals: the cost check has {players.Length} players on {map.Name}, want 16");
        FrenzyFx.Camera = me.RenderEye;

        // Tracer colours per class at level 6, and nothing at level 0 / Off.
        var baseCol = new Vector3(1f, 0.85f, 0.5f);
        foreach (var cls in Enum.GetValues<WeaponClass>())
        {
            FrenzyFx.Mode = FrenzyVisualMode.Full;
            FrenzyFx.Override = (6, cls);
            FrenzyParticles.Clear();
            var col = baseCol;
            var w = 0.02f;
            FrenzyFx.OnTracer(game, me, Weapons.DefaultPrimary, me.RenderEye, me.RenderEye + new Vector3(0, -1, -20), 1.0, ref col, ref w);
            var look = FrenzyLooks.Get(cls);
            if (!look.Rainbow && Vector3.Distance(Vector3.Normalize(col), Vector3.Normalize(look.Color)) > 0.05f)
                Fail($"frenzy visuals: a level-6 {cls} tracer is {col}, not its colour {look.Color}");
            if (w <= 0.02f || FrenzyParticles.Count == 0)
                Fail($"frenzy visuals: a level-6 {cls} tracer: width {w}, {FrenzyParticles.Count} particles");
        }
        foreach (var (lv, m) in new[] { (0, FrenzyVisualMode.Full), (6, FrenzyVisualMode.Off) })
        {
            FrenzyFx.Mode = m;
            FrenzyFx.Override = (lv, WeaponClass.SMG);
            FrenzyParticles.Clear();
            var col = baseCol;
            var w = 0.02f;
            FrenzyFx.OnTracer(game, me, Weapons.DefaultPrimary, me.RenderEye, me.RenderEye + new Vector3(0, -1, -20), 1.0, ref col, ref w);
            var buf = new float[FrenzyFxRenderer.MaxInstances * FrenzyParticles.FloatsPerInstance];
            var n = FrenzyFx.BuildWorld(game, 1.0, buf) + FrenzyFx.BuildViewModel(Weapons.DefaultPrimary, Matrix4x4.Identity, 1.0, buf);
            if (col != baseCol || w != 0.02f || FrenzyParticles.Count != 0 || n != 0)
                Fail($"frenzy visuals: level {lv} with {m} still changed the tracer ({col}, {w}) or drew {n} streaks / {FrenzyParticles.Count} particles");
        }

        // Other players' frenzy comes from the server-replicated Player.FrenzyLevel / FrenzyClass (bots included).
        FrenzyFx.Override = null;
        FrenzyFx.Mode = FrenzyVisualMode.Full;
        if (players.FirstOrDefault(p => p.Id != me.Id) is { } other)
        {
            var (oldLv, oldCls) = (other.FrenzyLevel, other.FrenzyClass);
            other.FrenzyLevel = 5;
            other.FrenzyClass = WeaponClass.Sniper;
            var seen = (FrenzyFx.LevelOf(game, other), FrenzyFx.ClassOf(game, other));
            other.FrenzyLevel = 0;
            var calm = FrenzyFx.LevelOf(game, other);
            (other.FrenzyLevel, other.FrenzyClass) = (oldLv, oldCls);
            if (seen != (5, WeaponClass.Sniper) || calm != 0)
                Fail($"frenzy visuals: another player at replicated level 5 Sniper shows as {seen}, at level 0 as {calm}");
            else
                FrenzyFxLine("others: replicated Player.FrenzyLevel / FrenzyClass drive their look (level 5 Sniper read back)");
        }

        // Full vs Subtle particle counts for the same spree.
        int Spree(FrenzyVisualMode m)
        {
            FrenzyFx.Mode = m;
            FrenzyFx.Override = (6, WeaponClass.LMG);
            FrenzyParticles.Clear();
            for (var i = 0; i < 40; i++)
            {
                var c = baseCol;
                var w = 0.02f;
                FrenzyFx.OnTracer(game, me, Weapons.DefaultPrimary, me.RenderEye, me.RenderEye + new Vector3(i * 0.1f, -1, -20), 1.0, ref c, ref w);
            }
            return FrenzyParticles.Count;
        }
        var full = Spree(FrenzyVisualMode.Full);
        var subtle = Spree(FrenzyVisualMode.Subtle);
        if (!(subtle < full * 0.7f && subtle > 0))
            Fail($"frenzy visuals: 40 LMG shots make {full} particles in Full and {subtle} in Subtle (want clearly fewer, but some)");

        // 16 players at level 6, everyone firing an LMG 12 times a second, 240 fps: per-frame work, allocations, cost.
        FrenzyFx.Mode = FrenzyVisualMode.Full;
        FrenzyFx.Override = (6, WeaponClass.LMG);
        var buffer = new float[FrenzyFxRenderer.MaxInstances * FrenzyParticles.FloatsPerInstance];
        var lmg = Weapons.All.FirstOrDefault(x => x.Class == WeaponClass.LMG) ?? Weapons.DefaultPrimary;
        var pose = Matrix4x4.CreateTranslation(0.2f, -0.2f, -0.4f);
        var frame = 0;
        var peak = 0;
        var peakInstances = 0;
        void Frame()
        {
            const float dt = 1f / 240;
            var now = frame * (double)dt;
            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if ((frame + i * 3) % 20 != 0) // 12 shots a second each
                    continue;
                var c = baseCol;
                var w = 0.03f;
                var from = p.RenderEye;
                var to = from + World.Forward(p.RenderYaw + (frame % 7) * 0.01f, -0.1f) * 25f;
                FrenzyFx.OnTracer(game, p, lmg, from, to, now, ref c, ref w);
            }
            FrenzyFx.Update(dt);
            var n = FrenzyFx.BuildWorld(game, now, buffer);
            n += FrenzyFx.BuildViewModel(lmg, pose, now, buffer);
            peak = Math.Max(peak, FrenzyParticles.Count);
            peakInstances = Math.Max(peakInstances, n);
            frame++;
        }
        FrenzyParticles.Clear();
        for (var i = 0; i < 480; i++)
            Frame(); // warm up (JIT) and fill to the steady state
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2400; i++)
            Frame();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes != 0)
            Fail($"frenzy visuals: the per-frame work allocates ({bytes} bytes over 2400 frames)");

        var runs = new double[3];
        for (var r = 0; r < runs.Length; r++)
        {
            FrenzyFx.TakeCpuMs();
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 1200; i++)
                Frame();
            runs[r] = sw.Elapsed.TotalMilliseconds / 1200;
        }
        Array.Sort(runs);
        var ms = runs[1];
        if (ms > FrenzyFxCpuBudgetMs)
            Fail($"frenzy visuals: {players.Length} players at level 6 cost {ms:0.000} ms of CPU per frame (budget {FrenzyFxCpuBudgetMs} ms)");
        if (peak >= FrenzyParticles.Capacity)
            Fail($"frenzy visuals: a full lobby at level 6 fills the particle pool ({peak} of {FrenzyParticles.Capacity})");
        FrenzyFxLine($"cost: {players.Length} players at level 6 firing LMGs (12/s each) at 240 fps: CPU {ms:0.0000} ms/frame median of 3 (budget {FrenzyFxCpuBudgetMs} ms), "
             + $"peak {peak} particles of {FrenzyParticles.Capacity}, peak {peakInstances} streaks in 2 instanced draws; allocations {bytes} bytes over 2400 frames");
    }
}
