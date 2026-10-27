using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Shotgun patterns" (Weapons/README.md, "Shotgun patterns"): the buckshot guns' baseline pattern is
/// a real unchoked one (0.4-1.5 inches of spread per yard), in the order the guns' characters promise; the Choke and Full
/// Choke still take exactly their percentages off that baseline (spread and range); and with the tight pattern, damage
/// falloff still keeps shotguns short-ranged. Shots go through the real pellet rays (Game.ShotRays) and body hit boxes.
/// </summary>
public static partial class SelfTest
{
    private static void CheckShotgunPatterns()
    {
        var guns = Weapons.All.Where(w => w.Class == WeaponClass.Shotgun && w.Pellets > 1 && !w.IsLauncher).ToList();
        if (guns.Count == 0)
        {
            Fail("Shotgun patterns: no buckshot shotguns loaded");
            return;
        }

        // Inches of total spread per yard: the pattern is 2 * tan(half-angle) wide per unit of distance.
        static float InchesPerYard(WeaponDef w) => 2f * MathF.Tan(w.HipSpreadRad) * 36f;
        foreach (var w in guns)
        {
            var ipy = InchesPerYard(w);
            if (ipy < 0.4f || ipy > 1.5f)
                Fail($"Shotgun patterns: {w.Name} spreads {ipy:0.00} in/yd, outside a real unchoked buckshot pattern (0.4-1.5)");
        }
        WeaponDef? G(string name) => guns.FirstOrDefault(w => w.Name == name);
        if (G("Scattergun Lever") is { } lever && G("Breacher Pump") is { } pump && G("Thunder Auto") is { } auto
            && G("Double Barrel") is { } db && G("Street Sweeper") is { } sweeper)
        {
            if (!(lever.HipSpreadDeg < pump.HipSpreadDeg && pump.HipSpreadDeg < auto.HipSpreadDeg && auto.HipSpreadDeg < db.HipSpreadDeg
                  && auto.HipSpreadDeg < sweeper.HipSpreadDeg))
                Fail("Shotgun patterns: the order changed (Lever tightest, then Breacher, Thunder Auto, Double Barrel / Street Sweeper widest)");
            if (MathF.Abs(InchesPerYard(pump) - 1f) > 0.03f)
                Fail($"Shotgun patterns: the Breacher Pump is the 1 inch per yard cylinder-bore baseline, got {InchesPerYard(pump):0.00}");
        }
        else
            Fail("Shotgun patterns: a shipped shotgun is missing");

        // The chokes: exactly their percentages of the new baseline.
        var chokes = new (string Name, float Spread, float Range)[] { ("Choke", 0.6f, 1.22f), ("Full Choke", 0.5f, 1.35f) };
        var kits = new List<(string Name, uint Kit)> { ("open", 0u) };
        foreach (var (name, spreadMul, rangeMul) in chokes)
        {
            if (Attachments.Find(name) is not { } a)
            {
                Fail($"Shotgun patterns: attachment {name} not found");
                continue;
            }
            var kit = Attachments.With(0, a.Slot, a.Index);
            kits.Add((name, kit));
            foreach (var w in guns)
            {
                if (!a.Fits(w))
                {
                    Fail($"Shotgun patterns: {name} no longer fits the {w.Name}");
                    continue;
                }
                var k = Attachments.Apply(w, kit);
                if (MathF.Abs(k.HipSpreadDeg - w.HipSpreadDeg * spreadMul) > 1e-5f || MathF.Abs(k.RangeM - w.RangeM * rangeMul) > 1e-4f)
                    Fail($"Shotgun patterns: {name} on the {w.Name} gives spread {k.HipSpreadDeg:0.0000} (want {w.HipSpreadDeg * spreadMul:0.0000}), range {k.RangeM:0.00} (want {w.RangeM * rangeMul:0.00})");
                if (MathF.Abs(k.HipSpreadDeg * k.AdsSpreadMult - w.HipSpreadDeg * w.AdsSpreadMult * spreadMul) > 1e-5f)
                    Fail($"Shotgun patterns: {name} on the {w.Name} changes the aimed pattern by other than {spreadMul:0.00}");
            }
        }

        // The real pellets of one shot at a standing player straight ahead, aimed at the chest: body damage with falloff.
        var rays = new List<ShotRay>(16);
        var eye = new Vector3(0, World.EyeHeight, 0);
        float ShotDamage(WeaponDef w, float dist, uint seed, out int hits)
        {
            var feet = new Vector3(0, 0, -dist);
            var dir = Vector3.Normalize(new Vector3(0, 1.2f, -dist) - eye);
            Game.ShotRays(w, eye, dir, w.HipSpreadRad, seed, rays);
            var dmg = 0f;
            hits = 0;
            foreach (var r in rays)
                if (World.RayHitsBody(feet, Stance.Stand, 0, r.Origin, r.Dir, World.Range) is { } t)
                {
                    hits++;
                    var head = World.IsHeadshot(feet, Stance.Stand, 0, r.Origin + r.Dir * t);
                    dmg += r.Damage * w.DamageMultAt(t) * (head ? w.HeadMult : 1f);
                }
            return dmg;
        }
        const int Seeds = 24;
        float Median(WeaponDef w, float dist)
        {
            var v = new float[Seeds];
            for (var s = 0; s < Seeds; s++)
                v[s] = ShotDamage(w, dist, 1000u + (uint)s * 7919u, out _);
            Array.Sort(v);
            return v[Seeds / 2];
        }
        float Furthest(WeaponDef w, float need)
        {
            var best = 0f;
            for (var d = 1f; d <= 40f; d += 0.25f)
                if (Median(w, d) >= need)
                    best = d;
            return best;
        }

        var lines = new List<string>();
        foreach (var w in guns)
        {
            var row = new List<string>();
            foreach (var (kname, kit) in kits)
            {
                var k = Attachments.Apply(w, kit);
                var one = Furthest(k, World.MaxHealth);
                var two = Furthest(k, World.MaxHealth / 2f);
                row.Add($"{kname} {k.HipSpreadDeg:0.00}deg 1-shot {one:0.0} m 2-shot {two:0.0} m");
                // Short range stays short: past 3x the full-damage range a whole pattern is a small fraction of a life.
                var far = k.Pellets * k.Damage * k.MinDamageMult * k.HeadMult;
                if (far >= World.MaxHealth / 2f)
                    Fail($"Shotgun patterns: {w.Name} ({kname}) still does {far:0} past {k.RangeM * 3:0} m, should be under half a life");
                if (one > 12f)
                    Fail($"Shotgun patterns: {w.Name} ({kname}) one-shots out to {one:0.0} m, no longer a close-range gun (12 m max)");
                if (two > 30f)
                    Fail($"Shotgun patterns: {w.Name} ({kname}) two-shots out to {two:0.0} m (30 m max)");
            }
            lines.Add($"{w.Name} ({w.Pellets}x{w.Damage}, {InchesPerYard(w):0.00} in/yd): {string.Join("; ", row)}");
        }
        foreach (var l in lines)
            Line(l);

        // A real pattern: the Breacher Pump puts nearly every pellet on a body at 15 m, and one-shots up close.
        if (G("Breacher Pump") is { } breacher)
        {
            int all = 0, landed = 0;
            for (var s = 0; s < Seeds; s++)
            {
                ShotDamage(breacher, 15f, 77u + (uint)s, out var h);
                landed += h;
                all += breacher.Pellets;
            }
            Line($"Breacher Pump at 15 m: {landed}/{all} pellets on the body");
            if (landed < all * 0.9f)
                Fail($"Shotgun patterns: only {landed}/{all} Breacher Pump pellets land at 15 m; a real pattern is about 40 cm wide there");
            if (Median(breacher, 5f) < World.MaxHealth)
                Fail("Shotgun patterns: the Breacher Pump no longer one-shots at 5 m");
        }
    }
}
