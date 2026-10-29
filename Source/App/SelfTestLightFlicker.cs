using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Light flicker (Source/Maps/LightFlicker.cs, docs/render/LIGHTING.md "Flicker"): every flickering light wavers in its
/// own way (its own seed, phase, strength and pace), slowly and softly, on clocks that never jump when shader time wraps;
/// the bake keeps each cell's blend of the flames reaching it (the brightest flame's own wave right by it, no pops between
/// cells, no flicker where no light is); the average brightness is the old flicker's; the map and prop "flickerSpeed" and
/// "flickerAmount" keys are read and checked; and a torch's flame part follows the very light its torch throws.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Six flames in a row 3 m apart (range 5), one steady and one at "flickerAmount" 0, and an Iron Torch.</summary>
    private const string FlickerScene = """
        {
          "format": "call-of-slop-map/1", "name": "Flicker Scene", "halfX": 16, "halfZ": 12,
          "ops": [
            { "op": "Ground", "color": [0.5, 0.5, 0.5] },
            { "op": "BakedLighting", "cell": 0.5, "indoor": 0.2 },
            { "op": "LightSource", "x": -9, "y": 2, "z": 0, "color": [1, 0.7, 0.4], "intensity": 1.5, "range": 5, "flicker": true },
            { "op": "LightSource", "x": -6, "y": 2, "z": 0, "color": [1, 0.7, 0.4], "intensity": 1.5, "range": 5, "flicker": true },
            { "op": "LightSource", "x": -3, "y": 2, "z": 0, "color": [1, 0.7, 0.4], "intensity": 1.5, "range": 5, "flicker": true, "flickerSpeed": 2 },
            { "op": "LightSource", "x": 0, "y": 2, "z": 0, "color": [1, 0.7, 0.4], "intensity": 1.5, "range": 5, "flicker": true },
            { "op": "LightSource", "x": 3, "y": 2, "z": 0, "color": [1, 0.7, 0.4], "intensity": 1.5, "range": 5, "flicker": true, "flickerAmount": 0.5 },
            { "op": "LightSource", "x": 6, "y": 2, "z": 0, "color": [1, 0.7, 0.4], "intensity": 1.5, "range": 5, "flicker": true },
            { "op": "LightSource", "x": 0, "y": 2, "z": 8, "color": [1, 1, 1], "range": 4 },
            { "op": "LightSource", "x": -9, "y": 2, "z": 8, "color": [1, 1, 1], "range": 4, "flicker": true, "flickerAmount": 0 },
            { "op": "Prop", "name": "Iron Torch", "x": 10, "z": 8, "y": 2, "yaw": 90 },
            { "op": "Spawn", "x": 12, "z": -10 },
            { "op": "Spawn", "x": -12, "z": 10 }
          ]
        }
        """;

    private static void CheckLightFlicker()
    {
        // ---- The clocks: whole cycles per shader-time wrap (no jump), the GLSL twin uses the same numbers ----
        var c1 = LightFlicker.Cycles1 * PropAnim.TimeWrap;
        var c2 = LightFlicker.Cycles2 * PropAnim.TimeWrap;
        if (MathF.Abs((float)(c1 - Math.Round(c1))) > 1e-3f || MathF.Abs((float)(c2 - Math.Round(c2))) > 1e-3f)
            Fail($"Light flicker: the waves aren't whole cycles per {PropAnim.TimeWrap} s ({c1:0.###}, {c2:0.###}): the flicker jumps when shader time wraps");
        foreach (var needle in new[] { "fract(t * 0.5)", "fract(t * 1.300048828125)", "0.15 * amount", "seed * 13.71 + 0.29", "seed * 5.17 + 0.61" })
            if (!LightFlicker.Glsl.Contains(needle))
                Fail($"Light flicker: the GLSL twin lacks \"{needle}\" (keep LightFlicker.Params/Wave and FlameParams/FlameWave in step)");
        if (LightFlicker.Cycles2 > 1.35f || LightFlicker.Cycles1 > 0.6f)
            Fail($"Light flicker: waves at {LightFlicker.Cycles1:0.##} and {LightFlicker.Cycles2:0.##} Hz (the developer asked for a gentle flame: at most ~0.6 and 1.35 Hz)");

        // ---- One light's wave: around 0 on average, soft, slow ----
        var (z0, mix0) = LightFlicker.Params(0.37f, 1f, 1f);
        double mean = 0;
        float peak = 0, slope = 0, prev = LightFlicker.Wave(z0, mix0, 0f);
        const float dt = 0.02f;
        var steps = (int)(PropAnim.TimeWrap / 8 / dt); // (512 s: whole cycles of both waves)
        for (var i = 1; i <= steps; i++)
        {
            var w = LightFlicker.Wave(z0, mix0, i * dt);
            mean += w;
            peak = MathF.Max(peak, MathF.Abs(w));
            slope = MathF.Max(slope, MathF.Abs(w - prev) / dt);
            prev = w;
        }
        mean /= steps;
        var wrapJump = MathF.Abs(LightFlicker.Wave(z0, mix0, 0f) - LightFlicker.Wave(z0, mix0, (float)PropAnim.TimeWrap - 1e-3f));
        if (Math.Abs(mean) > 2e-3 || peak > 0.25f || wrapJump > 0.01f)
            Fail($"Light flicker: one light's wave averages {mean:0.0000} (want 0: the old average brightness), peaks at {peak:0.000} (want a soft flame, under 0.25), jumps {wrapJump:0.000} at the wrap");
        var oldSlope = 0f; // the old LitFlicker's fastest change, a share of its light (its share x 0.6 x the wave's slope)
        for (var i = 0; i < 2000; i++)
        {
            float x0 = i * 0.001f * 7.3f, x1 = (i + 1) * 0.001f * 7.3f;
            float F(float x) => 0.5f + 0.25f * MathF.Sin(x) + 0.15f * MathF.Sin(x * 2.17f + 1.3f) + 0.1f * MathF.Sin(x * 5.03f + 4.1f);
            oldSlope = MathF.Max(oldSlope, 0.6f * MathF.Abs(F(x1) - F(x0)) / 0.001f);
        }
        if (!(slope < oldSlope / 3f))
            Fail($"Light flicker: the light changes by up to {slope:0.00} of itself a second, the old flicker {oldSlope:0.00} (want at least 3x calmer)");
        Line($"one light: mean {mean:+0.0000;-0.0000}, peak {peak:0.000} of its light, fastest change {slope:0.00}/s (old flicker {oldSlope:0.00}/s), no jump at the {PropAnim.TimeWrap:0} s wrap");

        // ---- Overrides and their checks ----
        var paces = new[] { 0.25f, 0.5f, 1f, 2f, 4f }.Select(s => LightFlicker.Params(0.5f, s, 1f).Mix).ToArray();
        for (var i = 1; i < paces.Length; i++)
            if (!(paces[i] >= paces[i - 1]) || paces[^1] - paces[0] < 0.5f)
                Fail($"Light flicker: \"flickerSpeed\" 0.25-4 gives paces {string.Join(", ", paces.Select(p => p.ToString("0.00")))} (want rising, by at least 0.5)");
        if (LightFlicker.Params(0.5f, 1f, 0f).Z != Vector2.Zero || !(LightFlicker.Params(0.5f, 1f, 2f).Z.Length() > LightFlicker.Params(0.5f, 1f, 1f).Z.Length() * 1.9f))
            Fail("Light flicker: \"flickerAmount\" doesn't scale the swing (0: none, 2: twice)");
        if (LightFlicker.Params(0.99f, LightFlicker.MaxSpeed, LightFlicker.MaxAmount).Z.Length() > LightFlicker.ZRange)
            Fail("Light flicker: the strongest light's phasor doesn't fit the bake's byte range");
        foreach (var (key, bad) in new[] { ("flickerSpeed", "9"), ("flickerAmount", "-1") })
        {
            var text = $$"""
                { "format": "call-of-slop-map/1", "name": "Bad Flicker", "halfX": 8, "halfZ": 8, "ops": [ { "op": "Ground", "color": [0.5, 0.5, 0.5] },
                  { "op": "LightSource", "x": 0, "y": 2, "z": 0, "color": [1, 1, 1], "flicker": true, "{{key}}": {{bad}} }, { "op": "Spawn", "x": 4, "z": 4 }, { "op": "Spawn", "x": -4, "z": -4 } ] }
                """;
            try
            {
                var bm = MapFile.Load(text, "Dev/BadFlicker.json", "");
                bm.Validate();
                Fail($"Light flicker: a map light with \"{key}\": {bad} loads (want it refused)");
            }
            catch (Exception ex) when (ex.Message.Contains(key))
            {
            }
            catch (Exception ex)
            {
                Fail($"Light flicker: \"{key}\": {bad} is refused without naming the key: {ex.Message}");
            }
        }
        var kit = new MapKit.Kit("Kit Flicker", "", 1000);
        var kl = kit.Light(0, 2, 0, "#ffb060", flicker: true, flickerSpeed: 0.5, flickerAmount: 1.5).ToString() ?? "";
        if (!kl.Contains("\"flickerSpeed\"") || !kl.Contains("\"flickerAmount\""))
            Fail($"Light flicker: the map kit's Light doesn't write flickerSpeed / flickerAmount: {kl}");

        // ---- The scene: each light its own, the bake blends them ----
        var m = MapFile.Load(FlickerScene, "Dev/FlickerScene.json", "");
        m.Validate();
        if (m.LightBake is not { } b)
        {
            Fail("Light flicker: the scene has no light bake");
            return;
        }
        var row = b.Sources.Where(l => l.Flicker && MathF.Abs(l.Pos.Z) < 0.1f).OrderBy(l => l.Pos.X).ToArray();
        if (row.Length != 6 || row.Select(l => l.Seed).Distinct().Count() != 6)
            Fail($"Light flicker: {row.Length} lights in the row, {row.Select(l => l.Seed).Distinct().Count()} different seeds (want 6 and 6)");
        if (row.Length > 0 && (row[2].FlickerSpeed != 2f || row[4].FlickerAmount != 0.5f))
            Fail("Light flicker: the LightSource op's flickerSpeed / flickerAmount didn't reach the light");

        // Neighbours waver apart: over a minute, two flames' waves are not one wave.
        var corr = new List<float>();
        for (var i = 0; i + 1 < row.Length; i++)
        {
            var (za, ma) = LightFlicker.Params(row[i].Seed, row[i].FlickerSpeed, row[i].FlickerAmount);
            var (zb, mb) = LightFlicker.Params(row[i + 1].Seed, row[i + 1].FlickerSpeed, row[i + 1].FlickerAmount);
            double sab = 0, saa = 0, sbb = 0;
            for (var t = 0f; t < 64f; t += 0.05f)
            {
                double wa = LightFlicker.Wave(za, ma, t), wb = LightFlicker.Wave(zb, mb, t);
                sab += wa * wb;
                saa += wa * wa;
                sbb += wb * wb;
            }
            corr.Add((float)(sab / Math.Sqrt(saa * sbb + 1e-12)));
        }
        if (corr.Count > 0 && (corr.Max() > 0.9f || MathF.Abs(corr.Average()) > 0.5f))
            Fail($"Light flicker: neighbouring flames move together (correlations {string.Join(", ", corr.Select(c => c.ToString("+0.00;-0.00")))}; want none over 0.9, average within +-0.5)");

        // Right by each light, the cell's flicker is that light's own (its phase and pace).
        var near = new List<string>();
        foreach (var l in row)
        {
            var (zl, ml) = LightFlicker.Params(l.Seed, l.FlickerSpeed, l.FlickerAmount);
            b.Sample(l.Pos + new Vector3(0, -0.6f, 0.3f), out var la, out _, out var s);
            var zc = new Vector2(LightFlicker.ZOf(s.Y), LightFlicker.ZOf(s.Z));
            var dPhase = MathF.Abs(MathF.IEEERemainder(MathF.Atan2(zc.Y, zc.X) - MathF.Atan2(zl.Y, zl.X), MathF.Tau));
            if (dPhase > 0.35f || MathF.Abs(s.W - ml) > 0.12f || la.W < 0.95f)
                near.Add($"({l.Pos.X:0}, {l.Pos.Z:0}): phase off by {dPhase:0.00} rad, pace {s.W:0.00} vs {ml:0.00}, flicker share {la.W:0.00}");
        }
        if (near.Count > 0)
            Fail("Light flicker: right by a flame the bake doesn't flicker as that flame does: " + string.Join("; ", near));

        // Between flames: smooth (no pop from one cell to the next), at a few moments.
        var worstStep = 0f;
        foreach (var t in new[] { 0.3f, 1.7f, 2.9f, 100.45f })
        {
            float? last = null;
            for (var x = -9f; x <= 6f; x += 0.05f)
            {
                b.Sample(new Vector3(x, 1.2f, 0.5f), out var la, out _, out var s);
                var f = MathF.Max(1f - LightFlicker.Dip * la.W + LightFlicker.Wave(new Vector2(LightFlicker.ZOf(s.Y), LightFlicker.ZOf(s.Z)), LightBake.PaceOf(s.W), t), 0f);
                if (last is { } lf)
                    worstStep = MathF.Max(worstStep, MathF.Abs(f - lf));
                last = f;
            }
        }
        if (worstStep > 0.02f)
            Fail($"Light flicker: along the row of flames the flicker changes by {worstStep:0.000} in 5 cm (want under 0.02: no popping between cells)");

        // No flicker where nothing flickers: a steady light, a light at "flickerAmount" 0, and every unlit cell (give or
        // take a cell inside a wall, which takes its open neighbours' average).
        foreach (var at in new[] { new Vector3(0, 1.5f, 8.3f), new Vector3(-9, 1.5f, 8.3f) })
        {
            b.Sample(at, out _, out _, out var s);
            var z = new Vector2(LightFlicker.ZOf(s.Y), LightFlicker.ZOf(s.Z));
            if (z.Length() > 0.01f)
                Fail($"Light flicker: at ({at.X:0}, {at.Z:0}) (a steady light / flickerAmount 0) the bake still flickers (phasor {z.Length():0.000})");
        }
        var darkFlicker = 0;
        for (var i = 0; i < b.Cells; i++)
            if (b.Light[i * 4] == 0 && b.Light[i * 4 + 1] == 0 && b.Light[i * 4 + 2] == 0 && (Math.Abs(b.Sun[i * 4 + 1] - 128) > 8 || Math.Abs(b.Sun[i * 4 + 2] - 128) > 8))
                darkFlicker++;
        if (darkFlicker > 0)
            Fail($"Light flicker: {darkFlicker} cells without any light keep a flicker phasor (want about 128, 128: none; the old bake left garbage there), which bleeds into lit cells when filtered");

        // ---- The torch's flame part follows its own light ----
        var torchLight = b.Sources.FirstOrDefault(l => l.Flicker && Vector3.Distance(l.Pos, new Vector3(10, 2.6f, 8)) < 1f);
        var torchDef = Props.Find("Iron Torch");
        var torch = m.PropInstances.FirstOrDefault(p => p.Def.Name == "Iron Torch");
        if (torchLight == default || torchDef == null || torch == null)
            Fail("Light flicker: the scene's Iron Torch or its light is missing");
        else
        {
            var seed = PropAnim.Seed(torch.Position); // (what the prop shader computes for this copy)
            if (MathF.Abs(torchLight.Seed - LightFlicker.PropLightSeed(seed, 0)) > 1e-6f)
                Fail($"Light flicker: the torch's light has seed {torchLight.Seed:0.0000}, its flame parts use {LightFlicker.PropLightSeed(seed, 0):0.0000}");
            var meshes = PropMeshes.Build(Props.All);
            var data = PropAnimData.Build(meshes);
            var d = Array.IndexOf(meshes.Defs, torchDef);
            var flames = 0;
            var (zt, mt) = LightFlicker.Params(torchLight.Seed, torchLight.FlickerSpeed, torchLight.FlickerAmount);
            for (var i = 0; d >= 0 && i < torchDef.Parts.Count; i++)
            {
                var rec = data.PartRecord[d][i];
                if (rec < 0 || torchDef.Parts[i].Anim?.Type != PropAnimType.Flicker)
                    continue;
                flames++;
                double sab = 0, saa = 0, sbb = 0;
                var range = 0f;
                float lo = 1, hi = 0;
                for (var t = 0f; t < 20f; t += 0.05f)
                {
                    PropAnim.Animate(data.Records, rec, torchDef.Parts[i].Offset, t, seed, 0f, 5f, 80f, out var level);
                    double wl = LightFlicker.Wave(zt, mt, t), wf = level - 0.72;
                    sab += wl * wf;
                    saa += wl * wl;
                    sbb += wf * wf;
                    lo = MathF.Min(lo, level);
                    hi = MathF.Max(hi, level);
                }
                range = hi - lo;
                var c = sab / Math.Sqrt(saa * sbb + 1e-12);
                if (c < 0.999 || range < 0.2f)
                    Fail($"Light flicker: the Iron Torch's flame part #{i + 1} doesn't follow its light (correlation {c:0.000}, level {lo:0.00}-{hi:0.00})");
            }
            if (flames == 0)
                Fail("Light flicker: the Iron Torch has no flickering flame part");
            Line($"torch: {flames} flame parts follow the light's own wave (seed {torchLight.Seed:0.000})");
        }
        Line($"scene: {row.Length} flames in a row, seeds {string.Join(", ", row.Select(l => l.Seed.ToString("0.00")))}; neighbour correlations {string.Join(", ", corr.Select(c => c.ToString("+0.00;-0.00")))}; "
             + $"worst 5 cm step {worstStep:0.0000}; bake {b.Bytes / 1024} KB");
    }
}
