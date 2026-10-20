using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Gun models": every shipped weapon is built from its "model" block, no two look the same, the
/// boxes are sane and few, the hand and muzzle spots sit on the model, the models match the preview script they were
/// designed with (part counts and bounds), a weapon file without a model block still loads and draws, and building
/// is cheap and cached.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The design reference (part count, min Y, min Z, max Y, max Z of the stock gun), from the preview script.</summary>
    private static readonly (string Name, int Parts, float MinY, float MinZ, float MaxY, float MaxZ)[] GunModelReference =
    [
        ("P9 Sidearm", 14, -0.153f, -0.138f, 0.040f, 0.104f),
        ("Mk.23 Tactical", 16, -0.162f, -0.190f, 0.051f, 0.123f),
        ("Viper .45", 15, -0.160f, -0.138f, 0.045f, 0.115f),
        ("Rattler Revolver", 16, -0.143f, -0.183f, 0.056f, 0.085f),
        ("Bastion Hand Cannon", 18, -0.168f, -0.200f, 0.057f, 0.134f),
        ("Wasp Machine Pistol", 20, -0.223f, -0.194f, 0.042f, 0.120f),
        ("Stinger Burst Pistol", 24, -0.198f, -0.206f, 0.052f, 0.122f),
        ("Hush SD", 18, -0.153f, -0.268f, 0.045f, 0.104f),
        ("Pocket Derringer", 15, -0.124f, -0.132f, 0.037f, 0.073f),
        ("Hornet SMG", 34, -0.173f, -0.321f, 0.073f, 0.273f),
        ("Buzzsaw", 19, -0.261f, -0.175f, 0.070f, 0.269f),
        ("Courier", 28, -0.193f, -0.345f, 0.075f, 0.316f),
        ("Whisper SD", 27, -0.173f, -0.407f, 0.079f, 0.302f),
        ("Kestrel PDW", 17, -0.162f, -0.222f, 0.094f, 0.278f),
        ("Tempest", 29, -0.224f, -0.324f, 0.065f, 0.329f),
        ("Ratchet Burst SMG", 29, -0.235f, -0.244f, 0.098f, 0.282f),
        ("BR-1 Prototype", 21, -0.125f, -0.412f, 0.072f, 0.322f),
        ("Vanguard AR", 35, -0.213f, -0.504f, 0.075f, 0.301f),
        ("Sentinel", 27, -0.248f, -0.574f, 0.070f, 0.331f),
        ("Raptor", 39, -0.200f, -0.508f, 0.086f, 0.310f),
        ("Bulldog", 29, -0.195f, -0.474f, 0.077f, 0.315f),
        ("Tri-Shot", 41, -0.203f, -0.524f, 0.095f, 0.346f),
        ("Phalanx Bullpup", 30, -0.191f, -0.302f, 0.079f, 0.227f),
        ("Specter SD", 28, -0.155f, -0.536f, 0.076f, 0.330f),
        ("Marksman M14", 36, -0.152f, -0.631f, 0.130f, 0.349f),
        ("Longbow DMR", 51, -0.152f, -0.735f, 0.140f, 0.341f),
        ("Reaper Battle Rifle", 29, -0.190f, -0.634f, 0.111f, 0.351f),
        ("Talon Burst DMR", 36, -0.162f, -0.474f, 0.145f, 0.326f),
        ("Hawk Semi", 32, -0.147f, -0.555f, 0.125f, 0.318f),
        ("Warden Lever-Action", 20, -0.094f, -0.521f, 0.059f, 0.324f),
        ("Needle Bolt-Action", 31, -0.147f, -0.598f, 0.137f, 0.366f),
        ("Leviathan .50", 43, -0.168f, -0.786f, 0.155f, 0.365f),
        ("Kite Semi-Sniper", 36, -0.153f, -0.603f, 0.128f, 0.350f),
        ("Ghost SD", 47, -0.147f, -0.781f, 0.125f, 0.340f),
        ("Railspike", 38, -0.170f, -0.693f, 0.159f, 0.364f),
        ("Breacher Pump", 39, -0.165f, -0.412f, 0.091f, 0.286f),
        ("Thunder Auto", 31, -0.164f, -0.393f, 0.095f, 0.309f),
        ("Double Barrel", 17, -0.122f, -0.397f, 0.055f, 0.324f),
        ("Slug Pump", 35, -0.165f, -0.472f, 0.085f, 0.351f),
        ("Street Sweeper", 29, -0.176f, -0.365f, 0.088f, 0.300f),
        ("Scattergun Lever", 23, -0.119f, -0.357f, 0.068f, 0.324f),
        ("Anvil LMG", 37, -0.246f, -0.548f, 0.134f, 0.334f),
        ("Hailstorm", 34, -0.210f, -0.529f, 0.075f, 0.346f),
        ("Bastille", 31, -0.249f, -0.586f, 0.138f, 0.374f),
        ("Grinder Rotary", 37, -0.283f, -0.546f, 0.169f, 0.220f),
        ("Torrent", 31, -0.256f, -0.492f, 0.074f, 0.308f),
        ("Nailgun", 19, -0.259f, -0.270f, 0.113f, 0.181f),
        ("Arc Carbine", 26, -0.155f, -0.400f, 0.076f, 0.303f),
        ("Old Faithful Musket", 22, -0.107f, -0.839f, 0.050f, 0.350f),
        ("Toy Blaster", 24, -0.170f, -0.272f, 0.115f, 0.222f),
    ];

    /// <summary>Boxes a gun body may use at most (the old per-class models used 15-40; optics add up to 30).</summary>
    private const int MaxGunParts = 64;

    private static void CheckGunModels()
    {
        var sigs = new Dictionary<string, string>();
        var counts = new List<int>();
        foreach (var w in Weapons.All)
        {
            if (w.Model == null)
            {
                if (w.SourceFile != null && w.Order <= 50)
                    Fail($"{w.Name}: shipped weapon without a \"model\" block");
                continue;
            }
            var mesh = GunModel.Mesh(w)!;
            counts.Add(mesh.Parts.Length);
            if (mesh.Parts.Length > MaxGunParts)
                Fail($"{w.Name}: {mesh.Parts.Length} boxes (at most {MaxGunParts})");
            var sig = string.Join(";", mesh.Parts.Select(p => $"{p.Sx:0.0000},{p.Sy:0.0000},{p.Sz:0.0000},{p.X:0.0000},{p.Y:0.0000},{p.Z:0.0000},{p.Color}"));
            if (sigs.TryGetValue(sig, out var twin))
                Fail($"{w.Name} has the same model as {twin}");
            sigs[sig] = w.Name;
            foreach (var p in mesh.Parts)
                if (!(p.Sx > 0 && p.Sy > 0 && p.Sz > 0 && p.Sx < 7 && p.Sy < 7 && p.Sz < 7))
                    Fail($"{w.Name}: a box has size {p.Sx} x {p.Sy} x {p.Sz}");

            // Attach points on (or right next to) the model.
            var parts = GunModel.RecordParts(w, 0f);
            var lo = parts.Aggregate(new Vector3(float.MaxValue), (a, p) => Vector3.Min(a, p.Min));
            var hi = parts.Aggregate(new Vector3(float.MinValue), (a, p) => Vector3.Max(a, p.Max));
            foreach (var (name, pt) in new[] { ("grip", mesh.Grip), ("support", mesh.Support), ("magazine", mesh.MagWell), ("muzzle", new Vector3(0, mesh.Bore, mesh.MuzzleZ)), ("underbarrel", mesh.Underbarrel) })
                if (Vector3.Min(pt - lo, hi - pt) is var m && (m.X < -0.05f || m.Y < -0.05f || m.Z < -0.05f))
                    Fail($"{w.Name}: the {name} spot {pt} is off the model ({lo} .. {hi})");
            if (w.Class != WeaponClass.Pistol && !(mesh.MuzzleZ < mesh.Support.Z && mesh.Support.Z < mesh.Grip.Z))
                Fail($"{w.Name}: muzzle {mesh.MuzzleZ:0.000}, support {mesh.Support.Z:0.000}, grip {mesh.Grip.Z:0.000} aren't in order front to back");

            // The design reference.
            var r = GunModelReference.FirstOrDefault(x => x.Name == w.Name);
            if (r.Name == null)
                Line($"  {w.Name}: no design reference (a new weapon file)");
            else if (r.Parts != parts.Count || MathF.Abs(r.MinY - lo.Y) > 0.003f || MathF.Abs(r.MinZ - lo.Z) > 0.003f || MathF.Abs(r.MaxY - hi.Y) > 0.003f || MathF.Abs(r.MaxZ - hi.Z) > 0.003f)
                Fail($"{w.Name}: {parts.Count} boxes, y {lo.Y:0.000}..{hi.Y:0.000}, z {lo.Z:0.000}..{hi.Z:0.000}; the design has {r.Parts}, y {r.MinY:0.000}..{r.MaxY:0.000}, z {r.MinZ:0.000}..{r.MaxZ:0.000}");
        }
        if (counts.Count > 0)
            Line($"{counts.Count} built models, {sigs.Count} distinct; boxes per gun body {counts.Min()}-{counts.Max()} (average {counts.Average():0.0})");

        // Distinct within each class even with the colours ignored (shapes, not paint jobs).
        foreach (var cls in Enum.GetValues<WeaponClass>())
        {
            var shapes = new Dictionary<string, string>();
            foreach (var w in Weapons.All.Where(x => x.Class == cls && x.Model != null))
            {
                var shape = string.Join(";", GunModel.Mesh(w)!.Parts.Select(p => $"{p.Sx:0.000},{p.Sy:0.000},{p.Sz:0.000},{p.X:0.000},{p.Y:0.000},{p.Z:0.000}"));
                if (shapes.TryGetValue(shape, out var twin))
                    Fail($"{w.Name} has the same shape as {twin} (only the colours differ)");
                shapes[shape] = w.Name;
            }
        }

        // Every kit builds (box sizes sane, nothing NaN).
        var kits = 0;
        foreach (var w in Weapons.All.Where(x => x.Model != null))
            foreach (var a in Attachments.For(w))
            {
                var d = Attachments.Apply(w, Attachments.With(0, a.Slot, a.Index));
                kits++;
                var m = GunModel.Mesh(d)!;
                if (m.Parts.Length > MaxGunParts + 8 || m.Parts.Any(p => !(p.Sx > 0 && p.Sy > 0 && p.Sz > 0 && float.IsFinite(p.X + p.Y + p.Z))))
                    Fail($"{w.Name} + {a.Name}: bad model ({m.Parts.Length} boxes)");
            }
        Line($"{kits} single-attachment models built");

        // Round trip: the model block survives writing the weapon back out.
        foreach (var w in Weapons.All.Where(x => x.Model != null).Take(50))
        {
            var again = WeaponFile.Load(WeaponFile.ToJson(w), "roundtrip");
            var a = GunModel.RecordParts(w, 0f);
            var b = GunModel.RecordParts(again, 0f);
            if (!a.SequenceEqual(b))
                Fail($"{w.Name}: the model changes when the weapon is written out and read back");
        }

        // An old weapon file (no model block) still loads and draws with the per-class model; bad blocks are rejected.
        var old = Weapons.All.First(x => x.Model != null);
        var json = WeaponFile.ToJson(old);
        var cut = json.IndexOf(",\n  \"model\"", StringComparison.Ordinal);
        var plain = WeaponFile.Load(cut > 0 ? json[..cut] + "\n}\n" : json, "old");
        if (plain.Model != null || GunModel.RecordParts(plain, 0f).Count < 8 || GunModel.HandPoints(plain) != null)
            Fail("a weapon file without a model block doesn't fall back to the per-class model");
        foreach (var (bad, why) in new[]
                 {
                     ("{ \"base\": \"blaster\" }", "unknown base"), ("{ \"barrels\": 99 }", "out of range"), ("{ \"stock\": \"banana\" }", "unknown stock"),
                     ("{ \"wings\": true }", "unknown key"), ("{ \"finish\": \"plaid\" }", "unknown colour"), ("[1, 2]", "not an object"),
                 })
        {
            try
            {
                WeaponFile.Load(json[..cut] + ",\n  \"model\": " + bad + "\n}\n", "bad");
                Fail($"a model block with {why} loaded");
            }
            catch (WeaponFileException)
            {
            }
        }

        // Reloads take the magazine out: box-magazine, pistol and LMG styles drop it before the fresh one is carried in, the
        // carried one is never drawn twice (the gun's is hidden all through the carry), and it's back in its seat at the end.
        var magChecked = 0;
        foreach (var w in Weapons.All)
        {
            var style = ArmAnchors.StyleOf(w);
            if (style is not (ReloadStyle.BoxMag or ReloadStyle.PistolMag or ReloadStyle.LmgBox))
                continue;
            magChecked++;
            if (GunModel.Mesh(w) is { } mm && !mm.Parts.Any(x => (x.Flags & GunPartFlags.Mag) != 0))
                Fail($"{w.Name}: {style} reload but the model has no removable magazine");
            var timing = ViewModelAnimator.MagTiming(style)!.Value;
            if (ViewModelAnimator.PhaseAt(style, 0, w.Reload) != ViewModelAnimator.MagPhase.Seated
                || ViewModelAnimator.PhaseAt(style, 0.999f, w.Reload) != ViewModelAnimator.MagPhase.Seated)
                Fail($"{w.Name}: the magazine isn't seated before and after a reload");
            // Play the reload frame by frame: the magazine must ride in the off hand (old one out, fresh one in),
            // never be drawn twice (no separate carried box), and be back in the well at the end.
            var anim = new ViewModelAnimator();
            var vin = new ViewModelInput { Def = w, Now = 10, Dt = 1f / 60, Reload = -1, Melee = -1, Grounded = true, Eye = new Vector3(0, 1.6f, 0) };
            anim.Update(vin);
            var frames = Math.Max(30, (int)(w.Reload * 60));
            var worstGap = 0f;
            var sawOld = timing.Grab < 0;
            bool sawNew = false, sawGone = false, doubled = false;
            for (var f = 0; f <= frames; f++)
            {
                vin.Reload = f / (float)frames * 0.999f;
                vin.Now += 1.0 / 60;
                anim.Update(vin);
                doubled |= anim.Carrying;
                if (anim.Mag is ViewModelAnimator.MagPhase.OldInHand or ViewModelAnimator.MagPhase.NewInHand)
                {
                    sawOld |= anim.Mag == ViewModelAnimator.MagPhase.OldInHand;
                    sawNew |= anim.Mag == ViewModelAnimator.MagPhase.NewInHand;
                    Matrix4x4.Invert(anim.GunPose, out var inv);
                    var hand = Vector3.Transform(anim.Left.HandWorld.Translation, inv);
                    worstGap = MathF.Max(worstGap, Vector3.Distance(hand, anim.Anchors.MagWell + anim.MagOffset));
                }
                sawGone |= anim.Mag is ViewModelAnimator.MagPhase.Falling or ViewModelAnimator.MagPhase.Gone; // let go (quick reloads swap before it's fallen far)
            }
            if (!sawOld || !sawNew || !sawGone || doubled || worstGap > 0.01f || anim.MagOffset != Vector3.Zero || anim.MagHidden)
                Fail($"{w.Name}: magazine reload wrong (old in hand {sawOld}, fresh in hand {sawNew}, dropped {sawGone}, drawn twice {doubled}, "
                     + $"hand to magazine {worstGap * 100:0.0} cm, seated at the end {anim.MagOffset == Vector3.Zero && !anim.MagHidden})");
        }
        Line($"{magChecked} box-magazine reloads: the old magazine comes out in the off hand and the fresh one rides it in");
        foreach (var style in new[] { ReloadStyle.TubeShells, ReloadStyle.Revolver, ReloadStyle.BreakOpen, ReloadStyle.BoltAction, ReloadStyle.MuzzleLoad })
            for (var k = 0; k <= 20; k++)
                if (ViewModelAnimator.PhaseAt(style, k / 20f, 2f) != ViewModelAnimator.MagPhase.Seated)
                    Fail($"{style} reloads move the magazine (they keep their own logic)");

        // Cost: building is cheap, and drawing uses the cache.
        var sw = Stopwatch.StartNew();
        var builds = 0;
        foreach (var w in Weapons.All.Where(x => x.Model != null))
        {
            GunBuilder.Build(w);
            builds++;
        }
        var us = builds == 0 ? 0 : sw.Elapsed.TotalMilliseconds * 1000 / builds;
        Line($"build: {us:0.0} us per gun (once per weapon copy, then cached)");
        if (us > 2000)
            Fail($"building a gun model takes {us:0} us");
        if (Weapons.All.FirstOrDefault(x => x.Model != null) is { } c && !ReferenceEquals(GunModel.Mesh(c), GunModel.Mesh(c)))
            Fail("gun models aren't cached");
    }
}
