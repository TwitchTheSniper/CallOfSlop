using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Shot origins" (Source/Game/GameMuzzle.cs): every shot is seen to leave the end of the barrel that
/// fired it. For every classic weapon and every standard build:
/// - each muzzle (<see cref="GunMuzzles.Local"/>) is the front end of a barrel: a box on or around its bore line ends there, past
///   any muzzle device;
/// - your own shots, fired through the real server path with the view model drawn: each tracer starts where that ray's
///   barrel's muzzle is drawn on screen (view model camera, its own field of view), hip and zoomed;
/// - another player's: each tracer starts at the muzzle of the gun as drawn in their hands, where its flash node is;
/// - rotary receivers turn the firing barrel shot by shot, and the flash shows only the barrels that fired;
/// - Msg.Shot's barrel bytes round-trip.
/// </summary>
public static partial class SelfTest
{
    /// <summary>How far (metres) a muzzle may sit from a barrel's front face.</summary>
    private const float MuzzleFaceTolerance = 0.03f;

    /// <summary>How far (metres) off the bore line a box may be and still frame the muzzle (a Dragon Mouth's jaws).</summary>
    private const float BoreReach = 0.04f;

    /// <summary>How far apart (normalised device units: 2 = the screen's height) a tracer's start and the drawn muzzle may be.</summary>
    private const float MuzzleScreenTolerance = 0.002f;

    private static void CheckShotOrigins()
    {
        var defs = new List<(string Name, WeaponDef Def)>();
        foreach (var w in Weapons.All)
            if (!w.IsLauncher)
                defs.Add((w.Name, w));
        var classic = defs.Count;
        foreach (var sb in StandardBuilds.All)
        {
            var b = sb.Build();
            if (b.IsMelee)
                continue;
            var a = WeaponBuilder.Assemble(b);
            if (a.Def is { IsLauncher: false } d && !Game.IsEmptyGun(d))
                defs.Add((sb.Name, d));
        }
        Line($"{classic} classic weapons, {defs.Count - classic} standard builds");

        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0);
        var me = game.Local!;
        me.Alive = true;
        var rig = new GunRig();
        var anim = new ViewModelAnimator();
        var vmProj = Sights.ViewModelProjection(16f / 9f);
        var rays = new List<ShotRay>();

        int faceBad = 0, screenBad = 0, worldBad = 0, flashBad = 0, rayBad = 0, shots = 0, tracers = 0, multi = 0;
        var worstFace = 0f;
        var worstScreen = 0f;
        var worstWorld = 0f;
        foreach (var (name, def) in defs)
        {
            var muzzles = GunMuzzles.Count(def);
            if (def.MuzzlePoints is { Length: > 1 })
                multi++;
            if (def.MuzzlePoints is { } pts && pts.Length != muzzles)
            {
                rayBad++;
                Fail($"{name}: {pts.Length} barrels but {muzzles} muzzles on the model");
                continue;
            }

            // 1. Each muzzle is at the front face of a box on its bore line (the barrel, or the muzzle device on it).
            var parts = GunModel.RecordParts(def, 0f);
            for (var i = 0; i < muzzles; i++)
            {
                var m = GunMuzzles.Local(def, i);
                var best = float.MaxValue;
                foreach (var p in parts)
                    if (!p.Optic && !p.Clear && m.X >= p.Min.X - BoreReach && m.X <= p.Max.X + BoreReach && m.Y >= p.Min.Y - BoreReach && m.Y <= p.Max.Y + BoreReach)
                        best = MathF.Min(best, MathF.Abs(p.Min.Z - m.Z));
                worstFace = MathF.Max(worstFace, best == float.MaxValue ? 9f : best);
                if (best > MuzzleFaceTolerance && faceBad++ < 8)
                    Fail($"{name}: muzzle {i + 1} at {FmtMuzzle(m)} isn't at the end of a barrel (nearest front face on its bore line {(best == float.MaxValue ? "none" : $"{best * 100:0.0} cm")} away)");
            }

            if (Game.IsCart(def))
                continue; // a push cart fires only while manned, from its gun in the world (Program.Workbench.cs)

            // 2. Your own shots: the view model drawn at the hip (and zoomed), shots through the server path.
            anim.Update(new ViewModelInput { Def = def, Now = 10, Dt = 1f / 60, Reload = -1, Melee = -1, Grounded = true, Eye = me.RenderEye });
            var pose = anim.GunPose;
            game.UseBuiltWeapons(def, Weapons.All.First(w => w.Id != def.Id && !w.IsLauncher)); // the server only resolves a gun you carry
            var eye = me.Eye;
            var view = Matrix4x4.CreateLookAt(eye, eye - Vector3.UnitZ, Vector3.UnitY);
            Matrix4x4.Invert(view, out var inv);
            foreach (var fov in new[] { 1.2f, 0.45f })
            {
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, 16f / 9f, 0.05f, 500f);
                game.ViewGun = new Game.FirstPersonGun { Def = def, Pose = pose, InvView = inv, TanHalfFov = MathF.Tan(fov / 2) };
                for (uint seed = 0; seed < (muzzles > 1 ? 3u : 1u); seed++)
                {
                    game.Tracers.Clear();
                    game.TestServerShot(def, -Vector3.UnitZ, seed);
                    Game.ShotRays(def, eye, -Vector3.UnitZ, 0f, seed, rays);
                    shots++;
                    if (game.Tracers.Count != Math.Min(rays.Count, 255))
                    {
                        if (rayBad++ < 8)
                            Fail($"{name}: a shot of {rays.Count} rays drew {game.Tracers.Count} tracers");
                        continue;
                    }
                    for (var i = 0; i < game.Tracers.Count; i++)
                    {
                        tracers++;
                        var drawn = Ndc(Vector3.Transform(GunMuzzles.Local(def, rays[i].Barrel), pose), Matrix4x4.Identity, vmProj);
                        var seen = Ndc(game.Tracers[i].From, view, proj);
                        var off = Vector2.Distance(drawn, seen);
                        worstScreen = MathF.Max(worstScreen, off);
                        if (off > MuzzleScreenTolerance && screenBad++ < 8)
                            Fail($"{name}: your tracer {i + 1} (barrel {rays[i].Barrel + 1}, fov {fov}) starts {off:0.000} screen units from that barrel's muzzle on the view model");
                    }
                    // Only the barrels that fired flash.
                    ulong want = 0;
                    foreach (var r in rays)
                        want |= 1UL << (r.Barrel & 63);
                    var got = game.LastShotBarrels(me.Id);
                    if (got != want && flashBad++ < 8)
                        Fail($"{name}: the flash shows barrels {got:X} after a shot from barrels {want:X}");
                }
            }

            // 3. Seen from outside: the gun as drawn in the player's hands (the same pose the scene graph's gun gets).
            game.ViewGun = default;
            const double now = 50;
            var body = Matrix4x4.CreateRotationY(-me.RenderYaw) * Matrix4x4.CreateTranslation(me.RenderPosition);
            var gunPose = ThirdPersonArms.Animate(me, def, ThirdPersonArms.RestGun(me), body, now);
            rig.Ensure(def);
            GunModel.FlashBarrels = GunModel.AllBarrels;
            rig.Pose(gunPose, 0f, Vector3.Zero, false, true, 0f);
            rig.Root.UpdateWorld();
            if (rig.FlashCount != muzzles && flashBad++ < 8)
                Fail($"{name}: {rig.FlashCount} muzzle flashes for {muzzles} muzzles");
            for (var b = 0; b < muzzles; b++)
            {
                var flashAt = rig.FlashOf(b).Parent!.WorldPosition;
                var off = Vector3.Distance(game.MuzzlePoint(me, def, b, now), flashAt);
                worstWorld = MathF.Max(worstWorld, off);
                if (off > 0.005f && worldBad++ < 8)
                    Fail($"{name}: seen from outside, barrel {b + 1}'s tracer starts {off * 100:0.0} cm from its muzzle flash");
            }
        }
        GunModel.FlashBarrels = GunModel.AllBarrels;
        Line($"{shots} shots, {tracers} tracers ({multi} guns with several barrels): worst muzzle-to-face {worstFace * 100:0.0} cm, your tracer to the drawn muzzle {worstScreen:0.0000} screen units, seen from outside {worstWorld * 100:0.00} cm");

        CheckRotaryShotOrigins(defs);
        CheckShotBarrelBytes(defs);
    }

    /// <summary>A rotary receiver fires one barrel a shot, each in turn: the tracer moves round the barrels.</summary>
    private static void CheckRotaryShotOrigins(List<(string Name, WeaponDef Def)> defs)
    {
        var rays = new List<ShotRay>();
        var rotaries = 0;
        foreach (var (name, def) in defs)
        {
            if (def.MuzzlePoints is not { Length: > 1 } pts || !pts.Any(p => p.Rotary))
                continue;
            rotaries++;
            var seen = new HashSet<int>();
            for (uint seed = 0; seed < (uint)pts.Length * 4; seed++)
            {
                Game.ShotRays(def, Vector3.Zero, -Vector3.UnitZ, 0f, seed, rays);
                foreach (var r in rays)
                    seen.Add(r.Barrel);
            }
            var rotaryBarrels = Enumerable.Range(0, pts.Length).Where(i => pts[i].Rotary).ToList();
            if (rotaryBarrels.Any(b => !seen.Contains(b)))
                Fail($"{name}: rotary barrels {string.Join(", ", rotaryBarrels.Where(b => !seen.Contains(b)).Select(b => b + 1))} never fire");
        }
        Line($"rotary builds: {rotaries}, every rotary barrel takes its turn");
    }

    /// <summary>Msg.Shot's barrel bytes: written for multi-barrel guns only, read back the same, absent reads as barrel 0.</summary>
    private static void CheckShotBarrelBytes(List<(string Name, WeaponDef Def)> defs)
    {
        var multi = defs.FirstOrDefault(d => d.Def.MuzzlePoints is { Length: > 2 }).Def;
        if (multi == null)
        {
            Fail("no standard build with three or more barrels to test Msg.Shot's barrel bytes with");
            return;
        }
        var w = new PacketWriter();
        var barrels = new List<int> { 2, 0, 1, 2 };
        Game.WriteShotBarrels(w, multi, barrels);
        var into = new byte[256];
        var r = new PacketReader(w.Span);
        var back = Game.ReadShotBarrels(ref r, 4, into).ToArray();
        var single = new PacketWriter();
        Game.WriteShotBarrels(single, Weapons.DefaultPrimary, [0]);
        var r2 = new PacketReader(single.Span);
        var none = Game.ReadShotBarrels(ref r2, 3, into).ToArray();
        if (!back.Select(b => (int)b).SequenceEqual(barrels) || single.Span.Length != 0 || none.Any(b => b != 0) || none.Length != 3)
            Fail($"Msg.Shot barrel bytes: wrote {string.Join(",", barrels)}, read {string.Join(",", back)}; single barrel wrote {single.Span.Length} byte(s)");
        else
            Line($"Msg.Shot barrel bytes round-trip ({w.Span.Length} bytes for 4 ends of a {multi.MuzzlePoints!.Length}-barrel gun; none for one barrel)");
    }

    private static Vector2 Ndc(Vector3 p, in Matrix4x4 view, in Matrix4x4 proj)
    {
        var c = Vector4.Transform(new Vector4(p, 1f), view * proj);
        return new Vector2(c.X / c.W, c.Y / c.W);
    }

    private static string FmtMuzzle(Vector3 v) => $"({v.X:0.000}, {v.Y:0.000}, {v.Z:0.000})";
}
