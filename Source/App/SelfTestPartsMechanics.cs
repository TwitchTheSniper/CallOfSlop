using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The splitters, barrels and muzzle devices of the 2026-10-27 parts pass (developer request).</summary>
    private static readonly string[] MechanicsSplitters =
    [
        "Narrow Twin Splitter", "Wide Y Splitter", "Diagonal Splitter", "Stepped Splitter", "Tower Splitter", "Pyramid Splitter",
        "Quad Line Splitter", "Totem Splitter", "Cross Splitter", "Ladder Splitter", "Rosette Splitter", "Wall Splitter",
    ];

    private static readonly string[] MechanicsOffsets = ["Riser Offset", "Drop Offset", "Sidestep Offset", "Dogleg Offset", "Canted Offset"];

    private static readonly string[] MechanicsBarrels =
    [
        "Heavy Bore Barrel", "Bloat Barrel", "Cannon Sleeve", "Squeeze Bore Barrel", "Needle Barrel", "Flechette Sleeve",
        "Sawn-Off Barrel", "Ported Barrel", "Heatsink Barrel", "Marksman Barrel",
    ];

    private static readonly string[] MechanicsMuzzles =
        ["Recoil Eater", "Dragon Mouth", "Velocity Booster", "Long Choke", "Fork Crown", "Tumbler Ring", "Hush Baffles"];

    private const string MechRifle = "Rifle Grip{receiver_1:Workhorse Receiver{barrel:%B%,feed:Box Mag,ammo:FMJ}}";

    /// <summary>
    /// Splitters (vertical, grids, rosettes, offsets), barrels that change the round's size and speed, and the stronger
    /// muzzle devices (docs/zombies/WEAPON-BUILDER.md, "Splitters, barrels and muzzles, 2026-10-27"): every new part loads
    /// and fits, models stay connected, splitters fire from each real barrel (vertical ones up and down), offsets move the
    /// barrel without adding one and don't count towards the stacking limit, bullet size grows the hit (a seeded server
    /// shot past a dummy), speed scales the range, the muzzles do what they say, and a shot allocates nothing.
    /// </summary>
    private static void CheckPartsMechanics()
    {
        WeaponDef? Def(string tree, string what)
        {
            var b = BuildOf(MechRifle.Replace("%B%", tree), what);
            var a = WeaponBuilder.Assemble(b);
            if (a.Def == null)
                Fail($"mechanics: {what} isn't legal: {string.Join("; ", a.Problems.Select(p => p.Message))}");
            else
                CheckBuildConnected(what, b);
            return a.Def;
        }
        string Outs(PartDef s, string barrel) => string.Join(",", s.Sockets.Where(x => x.Type == "barrel").Select(x => $"{x.Name}:{barrel}"));

        // ---- Every new part loads, in its category ----
        foreach (var (names, cat) in new[] { (MechanicsSplitters, PartCategory.Splitter), (MechanicsOffsets, PartCategory.Splitter), (MechanicsBarrels, PartCategory.Barrel), (MechanicsMuzzles, PartCategory.Muzzle) })
            foreach (var n in names)
                if (Parts.Find(n) is not { } p || p.Category != cat)
                    Fail($"mechanics: {n} didn't load as a {cat}");
        var stock = Def("Standard Barrel{muzzle:Flash Hider}", "plain rifle");
        var plain = Def("Standard Barrel", "bare rifle");
        if (stock == null || plain == null)
            return;
        var eye = new Vector3(0, 1.6f, 0);
        var dir = -Vector3.UnitZ;

        // ---- Multi-output splitters: one barrel per output, each fires from its own muzzle; vertical ones fan up/down ----
        foreach (var n in MechanicsSplitters)
        {
            var s = Parts.Find(n)!;
            var outs = s.BarrelOutputs;
            var d = Def($"{n}{{{Outs(s, "Standard Barrel{muzzle:Compensator}")}}}", n);
            if (d == null)
                continue;
            var rays = Game.ShotRays(d, eye, dir, 0f, 3);
            var origins = rays.Select(r => r.Origin).Distinct().Count();
            var muzzles = d.MuzzlePoints!.Select(p => p.Muzzle).Distinct().Count();
            var ys = d.MuzzlePoints!.Select(p => MathF.Round(p.Muzzle.Y, 3)).Distinct().Count();
            var vertical = s.Sockets.Where(x => x.Type == "barrel").Select(x => MathF.Round(x.Pos.Y, 3)).Distinct().Count();
            Line($"mechanics: {n} ({s.Rarity}, {outs} outputs, spread x{F(s.Split!.SpreadMult)}, splay {F(s.Split.SplayDeg)}): {d.BarrelCount} barrels, {origins} ray origins, {muzzles} muzzles at {ys} heights, "
                 + $"pitch {string.Join("/", d.MuzzlePoints!.Select(p => F(p.PitchDeg)))}, {F(d.ShotDamage)} per shot (one barrel {F(stock.ShotDamage)})");
            if (d.BarrelCount != outs || origins != outs || muzzles != outs || ys != vertical)
                Fail($"mechanics: {n} should fire {outs} barrels from {outs} muzzles at {vertical} heights ({d.BarrelCount} barrels, {origins} origins, {muzzles} muzzles, {ys} heights)");
            if (MathF.Abs(d.ShotDamage - stock.ShotDamage) > 0.5f * outs)
                Fail($"mechanics: {n} doesn't conserve the shot's damage ({F(d.ShotDamage)} vs {F(stock.ShotDamage)})");
            if (vertical > 1 && s.Split.SplayDeg > 0)
            {
                // Off a row, the splay fans outward from the centre: the top output turns up, the bottom one down.
                var top = d.MuzzlePoints!.MaxBy(p => p.Muzzle.Y);
                var bottom = d.MuzzlePoints!.MinBy(p => p.Muzzle.Y);
                if (!(top.PitchDeg > 0.1f && bottom.PitchDeg < -0.1f))
                    Fail($"mechanics: {n}'s top barrel should fan up and its bottom one down (pitch {F(top.PitchDeg)} / {F(bottom.PitchDeg)})");
            }
        }
        // The row rule is unchanged for the old fans: the Hydra still fans -4..+4 degrees sideways, never up or down.
        if (Def($"Hydra Splitter{{{Outs(Parts.Find("Hydra Splitter")!, "Stub Barrel")}}}", "Hydra") is { } hydra
            && (hydra.MuzzlePoints!.Any(p => p.PitchDeg != 0) || MathF.Abs(hydra.MuzzlePoints![0].YawDeg + 4) > 1e-4f || MathF.Abs(hydra.MuzzlePoints![^1].YawDeg - 4) > 1e-4f))
            Fail("mechanics: the Hydra's sideways fan changed");
        // The Tower fires three rays stacked straight up: one origin column, three heights.
        if (Def("Tower Splitter{barrel_1:Standard Barrel,barrel_2:Standard Barrel,barrel_3:Standard Barrel}", "Tower") is { } tower)
        {
            var rays = Game.ShotRays(tower, eye, dir, 0f, 1);
            var xs = rays.Select(r => MathF.Round(r.Origin.X, 4)).Distinct().Count();
            var hs = rays.Select(r => MathF.Round(r.Origin.Y, 4)).Distinct().Count();
            Line($"mechanics: Tower: rays from x {string.Join("/", rays.Select(r => F(r.Origin.X)))}, y {string.Join("/", rays.Select(r => F(r.Origin.Y)))}");
            if (rays.Count != 3 || xs != 1 || hs != 3)
                Fail($"mechanics: the Tower should fire 3 rays in one vertical column ({rays.Count} rays, {xs} columns, {hs} heights)");
        }

        // ---- Offsets: one output, the barrel moves, nothing is added ----
        var plainMuzzle = WeaponBuilder.Assemble(BuildOf(MechRifle.Replace("%B%", "Standard Barrel"))).Model!.Muzzles[0];
        foreach (var n in MechanicsOffsets)
        {
            var s = Parts.Find(n)!;
            var b = BuildOf(MechRifle.Replace("%B%", $"{n}{{barrel_1:Standard Barrel}}"), n);
            var a = WeaponBuilder.Assemble(b);
            if (a.Def is not { } d)
            {
                Fail($"mechanics: {n} isn't legal: {string.Join("; ", a.Problems.Select(p => p.Message))}");
                continue;
            }
            CheckBuildConnected(n, b);
            var moved = a.Model!.Muzzles[0] - plainMuzzle;
            var want = s.Sockets[0].Pos - new Vector3(0, 0, -0.06f);
            Line($"mechanics: {n} ({s.Rarity}): muzzle moved {FmtPos(moved)}, {d.BarrelCount} barrel, {F(d.ShotDamage)} per shot (plain {F(plain.ShotDamage)})");
            if (!s.IsOffset || d.BarrelCount != 1 || d.MuzzlePoints != null || a.Model.Muzzles.Length != 1)
                Fail($"mechanics: {n} should leave one barrel ({d.BarrelCount} barrels, {a.Model.Muzzles.Length} muzzles)");
            if (MathF.Abs(moved.X - want.X) > 0.012f || MathF.Abs(moved.Y - want.Y) > 0.012f || moved.Z > -0.03f)
                Fail($"mechanics: {n} should move the muzzle by about {FmtPos(want)} sideways/up and forward by its length (moved {FmtPos(moved)})");
            if (MathF.Abs(d.ShotDamage - plain.ShotDamage) > 0.5f)
                Fail($"mechanics: {n} changed the shot's damage ({F(d.ShotDamage)} vs {F(plain.ShotDamage)})");
        }
        // Offsets on a splitter's outputs: still two barrels, now at two heights; and the stacking limit ignores them.
        if (Def("Y-Splitter{barrel_1:Riser Offset{barrel_1:Standard Barrel},barrel_2:Drop Offset{barrel_1:Standard Barrel}}", "Y of offsets") is { } yo)
        {
            var rays = Game.ShotRays(yo, eye, dir, 0f, 1);
            if (yo.BarrelCount != 2 || rays.Select(r => MathF.Round(r.Lateral.Y, 3)).Distinct().Count() != 2)
                Fail($"mechanics: a Y-Splitter of a Riser and a Drop should fire 2 barrels at 2 heights ({yo.BarrelCount} barrels)");
        }
        var depthOk = Def("Riser Offset{barrel_1:Y-Splitter{barrel_1:Y-Splitter{barrel_1:Standard Barrel,barrel_2:Standard Barrel},barrel_2:Sidestep Offset{barrel_1:Y-Splitter{barrel_1:Standard Barrel,barrel_2:Standard Barrel}}}}", "offsets between stacked splitters");
        if (depthOk is { BarrelCount: not 4 })
            Fail($"mechanics: offsets between two stacked splitters should give 4 barrels ({depthOk.BarrelCount})");
        var tooDeep = WeaponBuilder.Assemble(BuildOf(MechRifle.Replace("%B%", "Y-Splitter{barrel_1:Riser Offset{barrel_1:Y-Splitter{barrel_1:Y-Splitter{barrel_1:Stub Barrel,barrel_2:Stub Barrel},barrel_2:Stub Barrel}},barrel_2:Stub Barrel}")));
        if (tooDeep.Def != null || !tooDeep.Problems.Any(p => p.Message.Contains("deep")))
            Fail("mechanics: three real splitters deep should still be refused with an offset among them");

        // ---- Barrels: size and speed are real stats; every barrel takes a muzzle device and stays in one piece ----
        foreach (var n in MechanicsBarrels)
        {
            var d = Def($"{n}{{muzzle:Compensator}}", n);
            if (d == null)
                continue;
            Line($"mechanics: {n}: size {F(d.BulletSize)}, speed {F(d.BulletSpeed)}, damage {d.Damage} (stock {plain.Damage}), range {F(d.RangeM)} (stock {F(plain.RangeM)}), "
                 + $"hip {F(d.HipSpreadDeg)} (stock {F(plain.HipSpreadDeg)}), recoil {F(d.RecoilDeg)}, rpm {d.Rpm}, pen x{F(d.PenetrationMult)}");
        }
        WeaponDef? Bare(string n) => Def(n, n);
        if (Bare("Bloat Barrel") is { } bloat)
            if (bloat.BulletSize < 2f || bloat.BulletSpeed > 0.55f || bloat.Damage <= plain.Damage * 1.3f || bloat.RangeM > plain.RangeM * 0.55f || bloat.PenetrationMult > 0.6f)
                Fail("mechanics: the Bloat Barrel should make the round about twice as big, half as fast (half the range), harder hitting and worse through walls");
        var cannon = Bare("Cannon Sleeve");
        if (cannon != null && (cannon.BulletSize < 3f || cannon.BulletSpeed > 0.45f || cannon.Damage < plain.Damage * 1.8f))
            Fail("mechanics: the Cannon Sleeve should fire a round 3x the size at under half the speed and nearly twice the damage");
        if (Bare("Needle Barrel") is { } needle)
            if (needle.BulletSize > 0.55f || needle.BulletSpeed < 1.6f || needle.Damage >= plain.Damage || needle.RangeM < plain.RangeM * 1.6f || needle.PenetrationMult < 1.7f)
                Fail("mechanics: the Needle Barrel should make the round half the size, ~1.7x as fast (and as far), weaker, better through walls");
        if (Bare("Heatsink Barrel") is { } heat && heat.Rpm <= plain.Rpm)
            Fail("mechanics: the Heatsink Barrel should raise the fire rate");
        if (Bare("Ported Barrel") is { } ported && ported.RecoilDeg > plain.RecoilDeg * 0.8f)
            Fail("mechanics: the Ported Barrel should cut recoil by about a quarter");
        // Per barrel: a Y of a Bloat and a Needle fires one fat and one thin round.
        if (Def("Y-Splitter{barrel_1:Bloat Barrel,barrel_2:Needle Barrel}", "Bloat and Needle") is { } mix)
        {
            var rays = Game.ShotRays(mix, eye, dir, 0f, 1);
            if (rays.Count != 2 || MathF.Abs(rays[0].Size - 2.2f) > 0.01f || MathF.Abs(rays[1].Size - 0.5f) > 0.01f)
                Fail($"mechanics: a Bloat and a Needle on one splitter should fire rounds of 2.2 and 0.5 ({string.Join("/", rays.Select(r => F(r.Size)))})");
        }
        // The hit: a ray passing 6 cm beside a hit box misses with the stock round and hits with a big one.
        var box = World.Hitbox(new Vector3(0, 0, -5));
        var pastO = new Vector3(World.PlayerRadius + 0.06f, 1f, 0);
        if (box.Grown(Game.BulletRadius(plain.BulletSize)).Raycast(pastO, dir, 50) != null || box.Grown(Game.BulletRadius(3.2f)).Raycast(pastO, dir, 50) == null
            || Game.BulletRadius(0.5f) != 0 || Game.BulletRadius(1f) != 0)
            Fail("mechanics: bullet radius: a stock or small round 6 cm off a hit box should miss it, a size-3.2 round hit it");
        if (CannonServerShot(cannon) is { } why)
            Fail($"mechanics: server shot: {why}");

        // ---- Muzzle devices: big, readable effects ----
        WeaponDef? Mz(string m, string barrel = "Standard Barrel") => Def($"{barrel}{{muzzle:{m}}}", m);
        void Want(string what, bool ok, string detail)
        {
            Line($"mechanics: {what}: {detail}");
            if (!ok)
                Fail($"mechanics: {what}: {detail}");
        }
        if (Mz("Recoil Eater") is { } eater)
            Want("Recoil Eater", eater.RecoilDeg <= plain.RecoilDeg * 0.5f && eater.HipSpreadDeg >= plain.HipSpreadDeg * 1.4f, $"recoil {F(eater.RecoilDeg)} vs {F(plain.RecoilDeg)}, hip {F(eater.HipSpreadDeg)} vs {F(plain.HipSpreadDeg)}");
        if (Mz("Dragon Mouth") is { } dragon)
            Want("Dragon Mouth", dragon.Pellets == plain.Pellets * 4 && dragon.BuildFlags.Contains("incendiary") && dragon.RangeM < plain.RangeM * 0.35f && dragon.HipSpreadDeg >= plain.HipSpreadDeg * 2.5f,
                $"{dragon.Pellets} projectiles, range {F(dragon.RangeM)}, hip {F(dragon.HipSpreadDeg)}, flags {string.Join(",", dragon.BuildFlags)}");
        if (Mz("Velocity Booster") is { } boost)
            Want("Velocity Booster", boost.BulletSpeed > 1.55f && boost.RangeM > plain.RangeM * 1.55f, $"speed {F(boost.BulletSpeed)}, range {F(boost.RangeM)} vs {F(plain.RangeM)}");
        if (Mz("Long Choke") is { } choke)
            Want("Long Choke", choke.HipSpreadDeg <= plain.HipSpreadDeg * 0.6f, $"hip {F(choke.HipSpreadDeg)} vs {F(plain.HipSpreadDeg)}");
        if (Mz("Fork Crown") is { } fork)
            Want("Fork Crown", fork.Pellets == plain.Pellets * 2 && MathF.Abs(fork.ShotDamage / plain.ShotDamage - 1.2f) < 0.1f, $"{fork.Pellets} projectiles, {F(fork.ShotDamage)} per shot vs {F(plain.ShotDamage)}");
        if (Mz("Hush Baffles") is { } hush)
            Want("Hush Baffles", hush.Loudness <= 0.26f && hush.Suppressor, $"loudness {F(hush.Loudness)}");
        if (Mz("Compensator") is { } comp)
            Want("Compensator", comp.RecoilVertical <= plain.RecoilVertical * 0.6f, $"vertical {F(comp.RecoilVertical)} vs {F(plain.RecoilVertical)}");
        if (Mz("Muzzle Brake") is { } brake)
            Want("Muzzle Brake", brake.RecoilHorizontal <= plain.RecoilHorizontal * 0.55f, $"horizontal {F(brake.RecoilHorizontal)} vs {F(plain.RecoilHorizontal)}");
        foreach (var m in MechanicsMuzzles)
            Mz(m, "Needle Barrel"); // and on another barrel: legal and connected
        // The projectile limit counts what each muzzle makes: two shotgun barrels with Fork Crowns fire 2 x 16.
        var forked = BuildOf("Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Y-Splitter{barrel_1:Scattermouth Barrel{muzzle:Fork Crown},barrel_2:Scattermouth Barrel},feed:Shell Tube,ammo:Buckshot}}");
        var per = BuildRules.ProjectilesPerPull(forked);
        var fd = WeaponBuilder.Assemble(forked).Def;
        var real = fd == null ? -1 : Game.ShotRays(fd, eye, dir, 0f, 1).Count;
        Line($"mechanics: Fork Crown on one of two shotgun barrels: {per} projectiles counted, {real} fired");
        if (per != real)
            Fail($"mechanics: the projectile limit counted {per} per pull, the gun fires {real}");

        // ---- A shot allocates nothing (vertical splitter, per-barrel sizes) ----
        if (Def("Totem Splitter{barrel_1:Bloat Barrel,barrel_2:Needle Barrel,barrel_3:Standard Barrel,barrel_4:Cannon Sleeve}", "Totem mix") is { } totem)
        {
            var into = new List<ShotRay>(64);
            Game.ShotRays(totem, eye, dir, 0.01f, 0, into);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (uint i = 0; i < 200; i++)
                Game.ShotRays(totem, eye, dir, 0.01f, i, into);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Line($"mechanics: 200 Totem shots allocated {bytes} bytes");
            if (bytes > 0)
                Fail($"mechanics: shots of a vertical splitter allocated {bytes} bytes");
        }
    }

    /// <summary>
    /// The real server path: a practice dummy 1.5 m away, a shot aimed 8 cm past its side. The stock rifle misses it, the
    /// Cannon Sleeve's fat round clips it (its tracer ends at the dummy). Null when it behaves.
    /// </summary>
    private static string? CannonServerShot(WeaponDef? cannon)
    {
        // Both through a Long Choke on a tight barrel, so the server's spread floor stays well inside the 8 cm miss.
        var rifle = WeaponBuilder.Assemble(BuildOf(MechRifle.Replace("%B%", "Marksman Barrel{muzzle:Long Choke}"))).Def;
        cannon = WeaponBuilder.Assemble(BuildOf(MechRifle.Replace("%B%", "Cannon Sleeve{muzzle:Long Choke}"))).Def;
        if (cannon == null || rifle == null)
            return "no guns";
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: Maps.Find("Workbench Range")?.Id ?? 0);
        game.UseBuiltWeapons(rifle, cannon);
        var me = game.Local!;
        me.Alive = true;
        game.AddPracticeDummies(1);
        var dummy = game.Players.Values.FirstOrDefault(p => p.Bot is { Dummy: true });
        if (dummy == null)
            return "no dummy";
        var feet = new Vector3(me.Eye.X + World.PlayerRadius + 0.08f, me.Position.Y, me.Eye.Z - 1.5f); // its side 8 cm right of a level shot
        dummy.Teleport(feet);
        dummy.Alive = true;
        dummy.Yaw = 0;
        dummy.SpawnTick = 0;
        for (var i = 0; i < Player.HistorySize; i++)
            dummy.History[i] = new HistoryEntry { Position = feet, Yaw = 0 };
        float End(WeaponDef def)
        {
            var before = game.Tracers.Count;
            game.TestServerShot(def, -Vector3.UnitZ, 11);
            return game.Tracers.Count > before ? Vector3.Distance(game.Tracers[^1].To, me.Eye) : -1;
        }
        var miss = End(rifle);
        var hit = End(cannon);
        Line($"mechanics: server shot 8 cm past a dummy 1.5 m away: stock round ends at {F(miss)} m, Cannon Sleeve at {F(hit)} m");
        if (miss is > 0 and < 2f)
            return $"the stock round hit the dummy ({F(miss)} m)";
        if (hit is < 0 or > 2f)
            return $"the Cannon Sleeve's round should clip the dummy ({F(hit)} m)";
        return null;
    }
}
