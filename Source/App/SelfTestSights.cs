using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Sight pictures" (Source/Weapons/Sights.cs, Source/Render/SightLens.cs): for every optic, how much
/// of a 16:9 screen its lens covers at full aim (1x reflex sights unchanged, rising with magnification, 8x and up at
/// least 90%), the sight line through the middle of the lens, a clear sight picture (no part of the gun between the
/// eye and the lens, so the lens pass shows only the view and the reticle), continuity (the lens grows without a pop
/// as the aim goes from 0 to 1), the steadied lens under recoil, and the "sight" blocks of optic files.
/// </summary>
public static partial class SelfTest
{
    private static void CheckSightPictures()
    {
        const float aspect = 16f / 9f;
        var optics = Attachments.BySlot[(int)AttachSlot.Optic];
        var reference = Weapons.Find("Needle Bolt-Action") ?? Weapons.All.First(w => w.Class == WeaponClass.Sniper);

        // ---- Coverage per optic at full aim ----------------------------------------------------------------
        // The eye distances the reflex sights had before the lens pass (their window must look exactly as it did).
        var legacy = new Dictionary<OpticKind, float> { [OpticKind.RedDot] = 0.1547f, [OpticKind.MiniDot] = 0.1227f, [OpticKind.Holo] = 0.1622f };
        var setups = new List<(string Name, WeaponDef Def)>();
        foreach (var o in optics)
        {
            if (o.Sight == null)
                Fail($"{o.Name}: the optic file has no \"sight\" block");
            var gun = o.Fits(reference) ? reference : Weapons.All.FirstOrDefault(o.Fits);
            if (gun == null)
            {
                Fail($"{o.Name}: fits no weapon");
                continue;
            }
            setups.Add((o.Name, Attachments.Apply(gun, Attachments.With(0, AttachSlot.Optic, o.Index))));
        }
        foreach (var w in Weapons.All.Where(GunModel.HasBuiltInScope))
            setups.Add(($"{w.Name} built-in scope", w));

        var magnified = new List<(string Name, float Zoom, float Cover)>();
        foreach (var (name, d) in setups)
        {
            var v = Sights.ProjectAimed(d, aspect);
            var cover = Sights.Coverage(v, aspect);
            var spec = Sights.Spec(d);
            var e = Sights.EyeDistance(d);
            Line($"  {name} ({d.Optic}, {d.AdsZoom:0.#}x): lens covers {cover * 100:0.0}% of a 16:9 screen at full aim (radius {v.HalfH:0.00} of half the height, "
                + $"{e * 100:0.0} cm from the eye), reticle {spec.Reticle}");
            if (!v.Valid || v.Center.Length() > 1e-3f || v.Reticle.Length() > 1e-3f)
                Fail($"{name}: at full aim the lens isn't centred on the sight line (centre {v.Center}, reticle {v.Reticle})");
            if (!(e >= Sights.MinEyeDistance - 1e-5f) || e > 0.4f)
                Fail($"{name}: eye distance {e * 100:0.00} cm");
            if (Sights.WorldOpacity(d, 1f) < 0.999f || Sights.ReticleAlpha(d, 1f) < 0.999f)
                Fail($"{name}: at full aim the lens doesn't show only the view and the reticle");
            if (!Sights.Magnified(d))
            {
                if (legacy.TryGetValue(d.Optic, out var old) && MathF.Abs(e - old) > 0.001f)
                    Fail($"{name}: the 1x window moved: {e * 100:0.00} cm from the eye, was {old * 100:0.00} cm");
                if (cover > 0.08f)
                    Fail($"{name}: a reflex window covers {cover * 100:0}% of the screen");
                continue;
            }
            if (spec.Screen > 0 && spec.EyeDistance == 0 && e > Sights.MinEyeDistance + 1e-4f && MathF.Abs(v.HalfH - spec.Screen) > spec.Screen * 0.02f)
                Fail($"{name}: lens radius {v.HalfH:0.000} at full aim, its sight asks for {spec.Screen}");
            var (lo, hi) = d.AdsZoom >= 8 ? (0.9f, 1f) : d.AdsZoom >= 4 ? (0.6f, 1f) : (0.15f, 0.6f);
            if (cover < lo || cover > hi)
                Fail($"{name}: a {d.AdsZoom:0.#}x lens covers {cover * 100:0.0}% of the screen (expected {lo * 100:0}-{hi * 100:0}%)");
            if (d.Optic != OpticKind.Iron)
                magnified.Add((name, d.AdsZoom, cover));
        }
        // More magnification, more screen.
        var byZoom = magnified.OrderBy(m => m.Zoom).ToArray();
        for (var i = 1; i < byZoom.Length; i++)
            if (byZoom[i].Zoom > byZoom[i - 1].Zoom && byZoom[i].Cover < byZoom[i - 1].Cover - 0.005f)
                Fail($"{byZoom[i].Name} ({byZoom[i].Zoom}x) covers less of the screen ({byZoom[i].Cover * 100:0}%) than {byZoom[i - 1].Name} ({byZoom[i - 1].Zoom}x, {byZoom[i - 1].Cover * 100:0}%)");
        Line($"  coverage by zoom: {string.Join(", ", byZoom.Select(m => $"{m.Zoom:0.#}x {m.Cover * 100:0}%"))}");

        // ---- Clear sight picture: nothing between the eye and the lens, on every weapon x optic ----------------
        int combos = 0, hidden = 0;
        var clearBad = 0;
        foreach (var w in Weapons.All)
        {
            var defs = Attachments.For(w).Where(a => a.Slot == AttachSlot.Optic).Select(a => Attachments.Apply(w, Attachments.With(0, AttachSlot.Optic, a.Index))).ToList();
            if (GunModel.HasBuiltInScope(w))
                defs.Add(w);
            foreach (var d in defs)
            {
                combos++;
                if (ClearSightPicture(d, aspect, ref hidden) is { } why)
                {
                    Fail($"{w.Name} [{Attachments.Describe(d.Kit)}]: {why}");
                    if (++clearBad > 10)
                        return;
                }
            }
        }
        Line($"  clear sight picture: {combos} weapon x optic setups, no gun part between the eye and the lens; {hidden} gun parts behind a lens are covered by the view through it");

        // ---- Continuity: the lens grows smoothly from the hip to full aim ----------------------------------------
        foreach (var (name, d) in setups)
        {
            var anim = new ViewModelAnimator();
            var input = BaseInput(d);
            var prev = 0f;
            float at50 = 0, at90 = 0;
            const int steps = 50;
            for (var k = 0; k <= steps; k++)
            {
                input.Aim = k / (float)steps;
                anim.Update(input);
                var v = Sights.Project(d, anim.GunPose, aspect);
                if (!v.Valid)
                {
                    Fail($"{name}: the lens goes behind the eye at aim {input.Aim:0.00}");
                    break;
                }
                if (v.HalfH < prev * (1 - 1e-4f))
                {
                    Fail($"{name}: the lens shrinks at aim {input.Aim:0.00} ({prev:0.000} -> {v.HalfH:0.000})");
                    break;
                }
                prev = v.HalfH;
                if (k == steps / 2)
                    at50 = v.HalfH;
                if (k == steps * 9 / 10)
                    at90 = v.HalfH;
            }
            var full = Sights.ProjectAimed(d, aspect).HalfH;
            if (MathF.Abs(prev - full) > full * 0.01f)
                Fail($"{name}: the animated lens ends at {prev:0.000}, the aimed lens is {full:0.000}");
            if (d.Optic is OpticKind.RedDot or OpticKind.Hybrid3 or OpticKind.Scope12 || ReferenceEquals(d, setups[^1].Def))
                Line($"  {name}: lens radius {at50:0.00} at half aim, {at90:0.00} at 90%, {prev:0.00} aimed (half screen heights)");
        }

        // ---- Steadied lens: recoil can't throw a high-power lens off the screen or into the eye --------------------
        foreach (var (name, d) in setups.Where(s => s.Def.Optic is OpticKind.RedDot or OpticKind.Scope4 or OpticKind.Scope8 or OpticKind.Scope12))
        {
            var anim = new ViewModelAnimator();
            var input = BaseInput(d);
            input.Aim = 1;
            anim.Update(input);
            var rest = Sights.Project(d, anim.GunPose, aspect);
            input.Kick = new WeaponKick { Back = 0.04f, Up = 0.05f, Roll = 0.03f, Side = 0.004f };
            anim.Update(input);
            var kicked = Sights.Project(d, anim.GunPose, aspect);
            var shift = (kicked.Center - rest.Center) * new Vector2(aspect, 1);
            Line($"  {name} under a hard kick: lens {rest.Depth * 100:0.0} -> {kicked.Depth * 100:0.0} cm from the eye, centre moves {shift.Length():0.00} half heights, "
                + $"reticle {(kicked.Reticle - rest.Reticle).Length():0.000}");
            if (!kicked.Valid || kicked.Depth < MathF.Max(Sights.NearPlane * 1.5f, rest.Depth * 0.5f))
                Fail($"{name}: recoil brings the lens to {kicked.Depth * 100:0.00} cm from the eye (aimed {rest.Depth * 100:0.00} cm)");
            if (Sights.Magnified(d) && shift.Length() > 0.35f)
                Fail($"{name}: recoil throws the lens {shift.Length():0.00} half heights across the screen");
        }

        CheckSightFiles();
    }

    /// <summary>
    /// Why the lens of <paramref name="d"/> at full aim doesn't give a clear sight picture (null if it does): a box of
    /// the gun (or the optic) between the eye and the lens plane that covers part of the lens on screen. Boxes behind
    /// the lens plane are covered by the view through the lens (<paramref name="hidden"/> counts those).
    /// </summary>
    private static string? ClearSightPicture(WeaponDef d, float aspect, ref int hidden)
    {
        var view = Sights.ProjectAimed(d, aspect);
        if (!view.Valid)
            return "no lens in front of the eye at full aim";
        var lensZ = -view.Depth;
        var shift = new Vector3(0, -GunModel.SightHeight(d), GunModel.AdsZ(d));
        const float eps = 1e-3f; // half heights
        foreach (var p in GunModel.RecordParts(d, Sights.Cutaway(d, 1f)))
        {
            if (p.Clear)
                continue;
            Vector3 min = p.Min + shift, max = p.Max + shift;
            var zFar = MathF.Max(min.Z, lensZ + 1e-4f);
            var zNear = MathF.Min(max.Z, -Sights.NearPlane);
            var inFront = zFar < zNear;
            var (z0, z1) = inFront ? (zFar, zNear) : (MathF.Max(min.Z, -10f), MathF.Min(max.Z, -Sights.NearPlane));
            if (z0 >= z1)
                continue; // behind the eye
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var z = (i & 4) != 0 ? z1 : z0;
                var k = 1f / (-z * Sights.TanHalfFov);
                float x = ((i & 1) != 0 ? max.X : min.X) * k, y = ((i & 2) != 0 ? max.Y : min.Y) * k;
                (x0, y0, x1, y1) = (MathF.Min(x0, x), MathF.Min(y0, y), MathF.Max(x1, x), MathF.Max(y1, y));
            }
            (x0, y0, x1, y1) = (x0 + eps, y0 + eps, x1 - eps, y1 - eps);
            if (x0 >= x1 || y0 >= y1)
                continue;
            bool overlaps;
            if (view.Round)
            {
                float cx = Math.Clamp(0f, x0, x1), cy = Math.Clamp(0f, y0, y1);
                overlaps = cx * cx + cy * cy < view.HalfH * view.HalfH;
            }
            else
                overlaps = x0 < view.HalfW && x1 > -view.HalfW && y0 < view.HalfH && y1 > -view.HalfH;
            if (!overlaps)
                continue;
            if (!inFront)
            {
                hidden++;
                continue;
            }
            return $"a{(p.Optic ? "n optic" : " gun")} part (y {p.Min.Y:0.000}..{p.Max.Y:0.000}, z {p.Min.Z:0.000}..{p.Max.Z:0.000}) sits between the eye and the lens and covers part of it";
        }
        return null;
    }

    /// <summary>The optic files' "sight" blocks: defaults merge in, overrides work, mistakes give readable errors.</summary>
    private static void CheckSightFiles()
    {
        static string SightFile(string slot, string effects, string sight) =>
            $$"""{ "format": "call-of-slop-attachment/1", "name": "Sight Test", "slot": "{{slot}}", "effects": [{{effects}}]{{(sight.Length > 0 ? ", \"sight\": " + sight : "")}} }""";
        const string scope4 = """{ "stat": "optic", "set": "Scope4" }, { "stat": "adsZoom", "set": 4 }""";

        var a = AttachmentFile.Load(SightFile("Optic", scope4, """{ "screen": 1.4 }"""), "sight test");
        var d = Attachments.Apply(reference(), 0).Clone();
        a.Apply(d);
        var v = Sights.ProjectAimed(d, 16f / 9f);
        if (a.Sight is not { Reticle: GunModel.Reticle.Duplex } || MathF.Abs(v.HalfH - 1.4f) > 0.03f)
            Fail($"a sight block with only \"screen\" should keep the 4x scope's duplex and give a lens of 1.4 (got {a.Sight?.Reticle}, {v.HalfH:0.000})");
        var round = AttachmentFile.Load(AttachmentFile.ToJson(a), "sight test (round trip)");
        if (AttachmentFile.ToJson(round) != AttachmentFile.ToJson(a))
            Fail("a sight block doesn't survive a round trip");

        var e = AttachmentFile.Load(SightFile("Optic", scope4, """{ "eyeDistance": 0.03, "reticle": "MilDot", "reticleColor": "#00ff00" }"""), "sight test");
        var d2 = Attachments.Apply(reference(), 0).Clone();
        e.Apply(d2);
        if (MathF.Abs(Sights.EyeDistance(d2) - 0.03f) > 1e-4f || Sights.Spec(d2).Reticle != GunModel.Reticle.MilDot || Sights.Spec(d2).ReticleColor != new Vector3(0, 1, 0))
            Fail($"\"eyeDistance\" / \"reticle\" / \"reticleColor\" don't take effect (eye {Sights.EyeDistance(d2)}, reticle {Sights.Spec(d2).Reticle})");

        var bad = new (string Json, string Expect)[]
        {
            (SightFile("Muzzle", """{ "stat": "recoilDeg", "mul": 0.9 }""", """{ "reticle": "Dot" }"""), "only an optic"),
            (SightFile("Optic", """{ "stat": "adsZoom", "set": 2 }""", """{ "reticle": "Dot" }"""), "sets \"optic\""),
            (SightFile("Optic", scope4, """{ "zoom": 3 }"""), "unknown key"),
            (SightFile("Optic", scope4, """{ "screen": 5 }"""), "outside"),
            (SightFile("Optic", scope4, """{ "reticle": "None" }"""), "needs a reticle"),
            (SightFile("Optic", scope4, """{ "tint": [2, 0, 0] }"""), "0 to 1"),
            (SightFile("Optic", scope4, """[1, 2]"""), "expected"),
        };
        foreach (var (json, expect) in bad)
        {
            try
            {
                AttachmentFile.Load(json, "sight test");
                Fail($"a broken sight block loaded (expected an error about '{expect}')");
            }
            catch (WeaponFileException ex) when (ex.Message.Contains(expect))
            {
            }
            catch (WeaponFileException ex)
            {
                Fail($"a broken sight block gave '{ex.Message}' (expected '{expect}')");
            }
        }
        Line($"  sight blocks: defaults merge, screen / eye distance / reticle overrides work, {bad.Length} broken blocks rejected with readable errors");

        static WeaponDef reference() => Weapons.Find("Needle Bolt-Action") ?? Weapons.All.First(w => w.Class == WeaponClass.Sniper);
    }
}
