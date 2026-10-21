using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "More perks" (see SelfTest.cs): the twelve later perks. The table and ids, unlock order, old and
/// new loadout files, Gun Game's fixed perks, bot picks, and each new perk's effect measured on the real code paths.
/// </summary>
public static partial class SelfTest
{
    private static readonly PerkId[] OriginalPerks =
    [
        PerkId.LowProfile, PerkId.HeadStart, PerkId.Parkour, PerkId.Afterburner, PerkId.FlakJacket, PerkId.LastStand,
        PerkId.Bloodthirst, PerkId.Entrenched, PerkId.PointBlank, PerkId.Deadeye, PerkId.Flanker, PerkId.Opener,
    ];

    private static void CheckMorePerks()
    {
        CheckPerkTable();
        CheckPerkUnlocks();
        CheckPerkLoadoutsAndModes();
        CheckNewPerkEffects();
    }

    // ---- Table, ids, categories -------------------------------------------------------------------

    private static void CheckPerkTable()
    {
        for (var i = 0; i < Perks.All.Length; i++)
            if ((int)Perks.All[i].Id != i)
                Fail($"Perks.All[{i}] is {Perks.All[i].Id}: the table must follow the enum order (Perks.Get indexes it)");
        if (Perks.All.Length != Enum.GetValues<PerkId>().Length)
            Fail($"{Perks.All.Length} perk definitions for {Enum.GetValues<PerkId>().Length} perk ids");
        // The original ids (saved in packets as bytes) must never move.
        for (var i = 0; i < OriginalPerks.Length; i++)
            if ((int)OriginalPerks[i] != i + 1)
                Fail($"{OriginalPerks[i]} moved from id {i + 1} to {(int)OriginalPerks[i]}");
        foreach (var c in new[] { PerkCategory.Movement, PerkCategory.Survival, PerkCategory.Damage })
        {
            var n = Perks.In(c).Count();
            if (n != 8)
                Fail($"{Perks.CategoryName(c)} has {n} perks (want 8: the original 4 and 4 new)");
            if (Perks.In(c).First().Id != Perks.Starters[(int)c])
                Fail($"{Perks.CategoryName(c)}: the starter isn't listed first");
        }
        var names = Perks.All.Select(p => p.Name.ToLowerInvariant()).ToArray();
        if (names.Distinct().Count() != names.Length)
            Fail("two perks share a name (loadout files store perks by name)");
        Line($"perks: {Perks.All.Length - 1} (8 per category), ids 1-12 unchanged, new ids {(int)PerkId.DeadSilence}-{(int)PerkId.Demolitions}");
    }

    // ---- Unlocks ------------------------------------------------------------------------------------

    private static void CheckPerkUnlocks()
    {
        var order = Progress.PerkUnlockOrder.ToArray();
        var expected = Perks.All.Where(p => p.Id != PerkId.None && !Perks.Starters.Contains(p.Id)).Select(p => p.Id).ToHashSet();
        if (order.Length != expected.Count || order.Distinct().Count() != order.Length || !expected.SetEquals(order))
            Fail($"the perk unlock order must list every non-starter perk exactly once ({order.Length} entries, {expected.Count} perks)");
        var levels = order.Select(Progress.PerkLevel).ToArray();
        Line("perk unlocks: " + string.Join(", ", order.Select((id, i) => $"{Perks.Get(id).Name} {levels[i]}")) + $" (max level {Progress.MaxLevel})");
        if (levels.Distinct().Count() != levels.Length)
            Fail("two perks unlock at the same level");
        for (var i = 1; i < levels.Length; i++)
            if (levels[i] <= levels[i - 1])
                Fail($"perk unlock levels don't rise along the order: {Perks.Get(order[i - 1]).Name} {levels[i - 1]}, {Perks.Get(order[i]).Name} {levels[i]}");
        if (levels.Any(l => l < 2 || l >= Progress.MaxLevel))
            Fail("a perk unlocks outside levels 2 to max-1");
        // The original nine keep the levels they always had (nobody loses a perk they already use).
        PerkId[] old = [PerkId.HeadStart, PerkId.Bloodthirst, PerkId.Opener, PerkId.Parkour, PerkId.Entrenched, PerkId.Deadeye, PerkId.Afterburner, PerkId.LastStand, PerkId.Flanker];
        for (var i = 0; i < old.Length; i++)
        {
            var want = 3 + (int)MathF.Round(i * (Progress.MaxLevel - 4) / (float)(old.Length - 1));
            if (Progress.PerkLevel(old[i]) != want)
                Fail($"{Perks.Get(old[i]).Name} moved from level {want} to {Progress.PerkLevel(old[i])}");
        }
    }

    // ---- Loadout files, packets, Gun Game, bots -------------------------------------------------------

    private static void CheckPerkLoadoutsAndModes()
    {
        var backup = LoadoutStore.SaveLines().ToArray();
        LoadoutStore.LoadLines(["selected=0", "loadout1.name=Old", "loadout1.perks=Parkour, Last Stand, Point Blank"]);
        var oldSet = LoadoutStore.Slots[0].Perks;
        var pick = new PerkSet(PerkId.Ghost, PerkId.ColdBlooded, PerkId.Demolitions);
        LoadoutStore.Slots[1].Perks = pick;
        var lines = LoadoutStore.SaveLines();
        var line = lines.FirstOrDefault(x => x.StartsWith("loadout2.perks=")) ?? "(missing)";
        LoadoutStore.LoadLines(lines.ToArray());
        var newSet = LoadoutStore.Slots[1].Perks;
        LoadoutStore.LoadLines(["loadout1.perks=Juggernaut, sleight of hand"]);
        var junkSet = LoadoutStore.Slots[0].Perks;
        LoadoutStore.LoadLines(backup);
        Line($"loadout files: old perks -> {oldSet}; '{line}' -> {newSet}; 'Juggernaut, sleight of hand' -> {junkSet}");
        if (oldSet != new PerkSet(PerkId.Parkour, PerkId.LastStand, PerkId.PointBlank))
            Fail("a loadout file with the original perk names didn't load");
        if (newSet != pick || line != "loadout2.perks=Ghost, Cold Blooded, Demolitions")
            Fail("new perks didn't survive save/load");
        if (junkSet != new PerkSet(PerkId.None, PerkId.None, PerkId.SleightOfHand))
            Fail("an unknown perk name wasn't skipped");

        // Packets: unknown ids and perks in the wrong slot are dropped.
        var bad = new PerkSet((PerkId)200, PerkId.Ghost, PerkId.Hardline).Sanitized();
        var good = new PerkSet(PerkId.Lightweight, PerkId.QuickFix, PerkId.TightGrip);
        if (bad != PerkSet.Empty || good.Sanitized() != good)
            Fail($"perk sanitizing is wrong: {bad} / {good.Sanitized()}");

        // Gun Game: everyone gets the mode's fixed perks whatever they asked for.
        using (var gg = ModeGame(1, GameMode.GunGame))
        {
            var p = gg.Players[0];
            p.WantPerks = new PerkSet(PerkId.Stalker, PerkId.Scavenger, PerkId.Demolitions);
            gg.SelfTestSpawn(p);
            Line($"Gun Game with new perks requested: carries {p.Perks}");
            if (!p.Perks.Equals(GunGame.Perks) || p.GrenadesLeft != 0 || new[] { GunGame.Perks.Movement, GunGame.Perks.Survival, GunGame.Perks.Damage }.Any(id => id > PerkId.Opener))
                Fail("Gun Game's fixed perk set was broken by the new perks");
        }

        // Bots: every preferred perk sits in its own category; the new perks are used.
        var used = new HashSet<PerkId>();
        foreach (var prof in BotPersonalities.All)
            foreach (var (cat, list) in new[] { (PerkCategory.Movement, prof.Movement), (PerkCategory.Survival, prof.Survival), (PerkCategory.Damage, prof.Damage) })
                foreach (var id in list)
                {
                    used.Add(id);
                    if (id == PerkId.None || Perks.Get(id).Category != cat)
                        Fail($"bot {prof.Name}: {id} isn't a {Perks.CategoryName(cat)} perk");
                }
        var newUsed = used.Count(id => id > PerkId.Opener);
        Line($"bot personalities prefer {used.Count} perks, {newUsed} of them new (Chaotic picks any)");
        if (newUsed < 10)
            Fail($"only {newUsed} new perks have a bot personality that picks them");
    }

    // ---- Effects --------------------------------------------------------------------------------------

    private static void CheckNewPerkEffects()
    {
        // Dead Silence: footstep loudness and range for other listeners, bots' all-round awareness.
        var (g0, r0) = Audio.StepLoudness(StepKind.Run, 1f, false);
        var (g1, r1) = Audio.StepLoudness(StepKind.Run, 1f, true);
        var (wg0, wr0) = Audio.StepLoudness(StepKind.Walk, 1f, false);
        var (wg1, wr1) = Audio.StepLoudness(StepKind.Walk, 1f, true);
        var sense = Perks.BotSenseRange(new PerkSet(PerkId.DeadSilence, PerkId.None, PerkId.None));
        Line($"Dead Silence: sprint step gain {g0:0.00} -> {g1:0.00}, heard within {r0:0} m -> {r1:0} m; walk {wg0:0.00}/{wr0:0} m -> {wg1:0.00}/{wr1:0} m; "
             + $"bots sense you behind them at {Perks.BotSenseRange(PerkSet.Empty):0} m -> {sense:0} m");
        if (MathF.Abs(g1 - g0 * Perks.DeadSilenceGain) > 1e-4f || MathF.Abs(r1 - r0 * Perks.DeadSilenceRange) > 1e-3f || sense >= Perks.BotCloseSense)
            Fail("Dead Silence doesn't quieten steps or shrink the bots' close-range sense");

        // Stalker and Lightweight: the input speed factor, then real distance in the shared mover.
        World.SetMap(Maps.All[0]);
        var heavy = 0.82f;
        var stalker = new PerkSet(PerkId.Stalker, PerkId.None, PerkId.None);
        var light = new PerkSet(PerkId.Lightweight, PerkId.None, PerkId.None);
        float Walk(float factor)
        {
            var st = new MoveState { Position = new Vector3(-14, 0, 14), Grounded = true };
            for (var i = 0; i < 60; i++)
                World.Simulate(ref st, new InputCmd { Buttons = Buttons.Right, Yaw = 0, SpeedPct = (byte)Math.Clamp((int)MathF.Round(factor * 100), 1, 200) });
            return st.Position.X + 14;
        }
        var adsNone = Perks.MoveSpeed(PerkSet.Empty, 1f, true);
        var adsStalker = Perks.MoveSpeed(stalker, 1f, true);
        var heavyNone = Perks.MoveSpeed(PerkSet.Empty, heavy, false);
        var heavyLight = Perks.MoveSpeed(light, heavy, false);
        Line($"Stalker: aiming speed x{adsNone:0.00} -> x{adsStalker:0.00} (1 s aim-walk {Walk(adsNone):0.00} m -> {Walk(adsStalker):0.00} m);  "
             + $"Lightweight: an {heavy:0.00}-mobility LMG x{heavyNone:0.00} -> x{heavyLight:0.00} (1 s walk {Walk(heavyNone):0.00} m -> {Walk(heavyLight):0.00} m), "
             + $"an SMG x{Perks.MoveSpeed(light, 1.05f, false):0.00}");
        if (adsStalker < adsNone * 1.3f || Perks.MoveSpeed(stalker, 1f, false) != 1f || Walk(adsStalker) < Walk(adsNone) * 1.25f)
            Fail("Stalker doesn't speed up aimed movement (or changes hip movement)");
        if (MathF.Abs(heavyLight - 1f) > 1e-4f || Perks.MoveSpeed(light, 1.05f, false) != 1.05f || Walk(heavyLight) < Walk(heavyNone) * 1.15f)
            Fail("Lightweight doesn't lift a heavy gun to full speed (or changes light guns)");

        // Steady Aim and Tight Grip: hip spread, aimed recoil (pure maths on the real recoil code).
        var def = Weapons.DefaultPrimary;
        float RecoilSum(float mult)
        {
            var rs = new RecoilState();
            var sum = 0f;
            for (var i = 0; i < 6; i++)
            {
                var (p, y) = rs.Shot(def, Stance.Stand, 1f, 1 + i * def.FireInterval, 0, 0, mult);
                sum += MathF.Abs(p) + MathF.Abs(y);
            }
            return sum;
        }
        var grip = new PerkSet(PerkId.None, PerkId.None, PerkId.TightGrip);
        var recoilNone = RecoilSum(1f);
        var recoilGrip = RecoilSum(Perks.RecoilMult(grip, 1f));
        Line($"Tight Grip: 6 aimed {def.Name} shots kick {recoilNone * 180 / MathF.PI:0.00} deg -> {recoilGrip * 180 / MathF.PI:0.00} deg; hip-fire multiplier {Perks.RecoilMult(grip, 0f):0.00}");
        if (recoilNone <= 0 || MathF.Abs(recoilGrip / recoilNone - Perks.TightGripRecoilMult) > 0.01f || Perks.RecoilMult(grip, 0f) != 1f)
            Fail("Tight Grip doesn't cut aimed recoil by 30% (or changes hip fire)");

        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2);
        game.SelfTestStreakTick(0); // first tick: match reset
        var me = game.Players[0];
        var bots = game.Players.Values.Where(p => p.Bot != null).ToArray();
        Player b1 = bots[0], b2 = bots[1];
        foreach (var p in game.Players.Values)
            game.SelfTestPerks(p, PerkSet.Empty);

        // Steady Aim: the local player's spread from the hip and aimed.
        game.SelfTestPlace(me, new Vector3(5, 0, 5), 0);
        float Spread(PerkSet perks, float aimT)
        {
            game.SelfTestPerks(me, perks);
            me.Sim.Velocity = Vector3.Zero;
            me.Sim.Grounded = true;
            me.Sim.Stance = Stance.Stand;
            game.Bloom = 0;
            game.AimT = aimT;
            return game.CurrentSpread();
        }
        var steady = new PerkSet(PerkId.None, PerkId.None, PerkId.SteadyAim);
        var hip0 = Spread(PerkSet.Empty, 0);
        var hip1 = Spread(steady, 0);
        var ads0 = Spread(PerkSet.Empty, 1);
        var ads1 = Spread(steady, 1);
        Line($"Steady Aim: hip spread {hip0 * 1000:0.0} -> {hip1 * 1000:0.0} mrad; aimed {ads0 * 1000:0.0} -> {ads1 * 1000:0.0} mrad");
        if (MathF.Abs(hip1 - hip0 * Perks.SteadyAimHipMult) > 1e-4f || MathF.Abs(ads1 - ads0) > 1e-5f)
            Fail("Steady Aim doesn't tighten hip fire by 35% (or changes aimed fire)");
        game.AimT = 0;

        // Sleight of Hand: the local reload, as started by the reload key.
        double Reload(PerkSet perks)
        {
            game.SelfTestPerks(me, perks);
            game.Active.Ammo = 0;
            game.Active.ReloadUntil = 0;
            game.EquipUntil = 0;
            game.StartReload(100);
            var t = game.Active.ReloadUntil - 100;
            game.Active.ReloadUntil = 0;
            game.Active.Ammo = game.Active.Def.Mag;
            return t;
        }
        var rel0 = Reload(PerkSet.Empty);
        var rel1 = Reload(new PerkSet(PerkId.None, PerkId.None, PerkId.SleightOfHand));
        Line($"Sleight of Hand: {game.Active.Def.Name} reload {rel0:0.00} s -> {rel1:0.00} s");
        if (rel0 <= 0 || Math.Abs(rel1 - rel0 * Perks.SleightReloadMult) > 0.01)
            Fail("Sleight of Hand doesn't make reloads 35% faster");
        game.SelfTestPerks(me, PerkSet.Empty);

        // Quick Fix: when regeneration starts after a hit.
        (double Start, int Hp) Regen6(PerkSet perks)
        {
            game.SelfTestPerks(b2, perks);
            b2.Alive = true;
            b2.Health = 50;
            b2.LastHurtTime = 0;
            b2.Regenerating = false;
            b2.RegenCarry = 0;
            var start = -1.0;
            for (var tick = 0; tick <= 6 * World.TickRate; tick++)
            {
                var now = tick * (double)World.TickDt;
                Game.RegenTick(b2, now);
                if (b2.Regenerating && start < 0)
                    start = now;
            }
            return (start, b2.Health);
        }
        var (start0, hp0) = Regen6(PerkSet.Empty);
        var (start1, hp1) = Regen6(new PerkSet(PerkId.None, PerkId.QuickFix, PerkId.None));
        Line($"Quick Fix: regen starts {start0:0.00} s -> {start1:0.00} s after the hit; 50 HP after 6 s: {hp0} -> {hp1} HP");
        if (Math.Abs(start0 - Regen.Delay) > 0.05 || Math.Abs(start1 - Perks.QuickFixDelay) > 0.05 || hp1 <= hp0)
            Fail("Quick Fix doesn't start health regeneration sooner");
        game.SelfTestPerks(b2, PerkSet.Empty);

        // Ghost (and Low Profile next to it): who a Recon Sweep shows, moving or still.
        using (var tdm = ModeGame(3, GameMode.TeamDeathmatch))
        {
            var all = tdm.Players.Values.OrderBy(p => p.Id).ToArray();
            Player viewer = all[0], foe = all[2];
            for (var i = 0; i < all.Length; i++)
            {
                all[i].Team = (byte)(i < 2 ? 0 : 1);
                tdm.SelfTestPerks(all[i], PerkSet.Empty);
                tdm.SelfTestPlace(all[i], new Vector3(2 * i, 0, 3), 0);
            }
            tdm.LocalId = viewer.Id;
            tdm.StreakEffects.Add(new StreakEffect(StreakId.ReconSweep, viewer.Id, 0, KillStreaks.ReconSweepSeconds));
            bool Shown(PerkSet perks, Vector3 vel, Stance stance)
            {
                tdm.SelfTestPerks(foe, perks);
                foe.RenderVelocity = vel;
                foe.RenderStance = stance;
                return tdm.IsRevealed(foe, 1);
            }
            var ghost = new PerkSet(PerkId.Ghost, PerkId.None, PerkId.None);
            var lowProfile = new PerkSet(PerkId.LowProfile, PerkId.None, PerkId.None);
            var run = new Vector3(5, 0, 0);
            var plainMoving = Shown(PerkSet.Empty, run, Stance.Stand);
            var ghostMoving = Shown(ghost, run, Stance.Stand);
            var ghostStill = Shown(ghost, Vector3.Zero, Stance.Stand);
            var ghostCreep = Shown(ghost, new Vector3(0.5f, 0, 0), Stance.Stand);
            var lpLow = Shown(lowProfile, Vector3.Zero, Stance.Crouch);
            var lpUp = Shown(lowProfile, run, Stance.Stand);
            Line($"Recon Sweep shows: no perk moving {plainMoving}; Ghost moving 5 m/s {ghostMoving}, still {ghostStill}, creeping 0.5 m/s {ghostCreep}; "
                 + $"Low Profile crouched {lpLow}, standing {lpUp}");
            if (!plainMoving || ghostMoving || !ghostStill || !ghostCreep)
                Fail("Ghost must hide you from recon while moving (faster than 1 m/s) and only then");
            if (lpLow || !lpUp)
                Fail("Low Profile's recon cover changed");
        }

        // Cold Blooded: what an enemy turret or drone locks onto.
        game.SelfTestPlace(me, new Vector3(5, 0, 5), 0);
        game.SelfTestPlace(b1, new Vector3(5, 0, 9), 0);
        b2.Alive = false;
        var from = new Vector3(5, 1.2f, 5.6f);
        var lockPlain = game.SelfTestMachineTarget(me, from, KillStreaks.TurretRange);
        game.SelfTestPerks(b1, new PerkSet(PerkId.None, PerkId.ColdBlooded, PerkId.None));
        var lockCold = game.SelfTestMachineTarget(me, from, KillStreaks.TurretRange);
        Line($"Cold Blooded: a turret 3.4 m away locks onto {(lockPlain == b1 ? "the enemy" : lockPlain == null ? "nobody" : "someone else")} without it, "
             + $"{(lockCold == null ? "nobody" : "someone")} with it");
        if (lockPlain != b1)
            Fail("turret target test setup: the plain enemy wasn't targeted");
        else if (lockCold != null)
            Fail("Cold Blooded: a turret still locked onto the player");
        game.SelfTestPerks(b1, PerkSet.Empty);

        // Hardline: when each starter streak is earned.
        game.SelfTestPerks(me, new PerkSet(PerkId.None, PerkId.Hardline, PerkId.None));
        game.SelfTestStreakRequest(me, StreakSet.Starter);
        game.SelfTestStreakSpawn(me);
        var weapon = Weapons.DefaultPrimary.Id;
        var earnedAt = new Dictionary<StreakId, int>();
        for (var k = 1; k <= 5; k++)
        {
            game.SelfTestPlace(b1, new Vector3(5, 0, 9), 0);
            game.SelfTestKill(me, b1, weapon, 10 + k);
            foreach (var id in me.Streak.Slots.Ids)
                if (me.Streak.IsReady(id) && !earnedAt.ContainsKey(id))
                    earnedAt[id] = k;
        }
        Line("Hardline: " + string.Join(", ", StreakSet.Starter.Ids.Select(id => $"{KillStreaks.Get(id).Name} {KillStreaks.Get(id).Kills} -> {earnedAt.GetValueOrDefault(id)} kills"))
             + "; costs " + string.Join(", ", KillStreaks.All.Where(s => s.Id != StreakId.None).Select(s => $"{s.Kills}->{Perks.StreakCost(me.Perks, s.Kills)}")));
        foreach (var id in StreakSet.Starter.Ids)
            if (earnedAt.GetValueOrDefault(id) != KillStreaks.Get(id).Kills - 1)
                Fail($"Hardline: {KillStreaks.Get(id).Name} earned after {earnedAt.GetValueOrDefault(id)} kills (want {KillStreaks.Get(id).Kills - 1})");
        if (KillStreaks.All.Any(s => s.Id != StreakId.None && Perks.StreakCost(me.Perks, s.Kills) < Math.Min(s.Kills, Perks.HardlineMinKills)))
            Fail("Hardline made a streak cheaper than 2 kills");

        // Scavenger: the server's counts and the local player's own counts after kills.
        game.SelfTestPerks(me, new PerkSet(PerkId.None, PerkId.Scavenger, PerkId.None));
        game.SelfTestPlace(me, new Vector3(5, 0, 5), 0);
        me.Grenade = game.LocalGrenade = ThrowableKind.Frag;
        me.Equipment = game.LocalEquipment = ThrowableKind.Recon;
        me.GrenadesLeft = me.EquipmentLeft = game.GrenadesLeft = game.EquipmentLeft = 0;
        var counts = new List<string>();
        for (var k = 0; k < 3; k++)
        {
            game.SelfTestPlace(b1, new Vector3(5, 0, 9), 0);
            game.SelfTestKill(me, b1, weapon, 20 + k);
            counts.Add($"{me.GrenadesLeft}/{me.EquipmentLeft} (local {game.GrenadesLeft}/{game.EquipmentLeft})");
        }
        game.SelfTestPerks(me, PerkSet.Empty);
        me.GrenadesLeft = me.EquipmentLeft = 0;
        game.SelfTestPlace(b1, new Vector3(5, 0, 9), 0);
        game.SelfTestKill(me, b1, weapon, 30);
        var frag = Throwables.Get(ThrowableKind.Frag).PerLife;
        var recon = Throwables.Get(ThrowableKind.Recon).PerLife;
        Line($"Scavenger: grenades/equipment after kills 1-3 from empty: {string.Join(", ", counts)} (carry {frag}/{recon}); without it {me.GrenadesLeft}/{me.EquipmentLeft}");
        if (counts[0] != $"1/1 (local 1/1)" || counts[2] != $"{Math.Min(3, frag)}/{recon} (local {Math.Min(3, frag)}/{recon})" || me.GrenadesLeft != 0)
            Fail("Scavenger doesn't restock one grenade and one equipment per kill up to the per-life amount (server and local alike)");

        // Demolitions (last: the blast may hurt the thrower): a Frag 8 m and 10 m from the victim.
        game.SelfTestPlace(me, new Vector3(-14, 0, 14), 0);
        var demo = new PerkSet(PerkId.None, PerkId.None, PerkId.Demolitions);
        int? BlastHurt(PerkSet perks, float dist)
        {
            game.SelfTestPerks(me, perks);
            game.SelfTestPlace(b1, new Vector3(5, 0, 5), 0);
            var chest = b1.Chest;
            foreach (var dir in new[] { Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
            {
                if (World.Query.Raycast(chest, dir, 12f, QueryFilter.All) < 12f)
                    continue; // a wall that way: try another
                game.SelfTestDetonate(me, ThrowableKind.Frag, chest + dir * dist - new Vector3(0, 0.15f, 0), 40);
                return World.MaxHealth - (b1.Alive ? b1.Health : 0);
            }
            return null;
        }
        var d8 = BlastHurt(PerkSet.Empty, 8);
        var d8Demo = BlastHurt(demo, 8);
        var d10 = BlastHurt(PerkSet.Empty, 10);
        var d10Demo = BlastHurt(demo, 10);
        var fragDef = Throwables.Get(ThrowableKind.Frag);
        Line($"Demolitions: Frag radius {fragDef.Radius:0} m -> {fragDef.Radius * Perks.DemolitionsRadiusMult:0.0} m; damage at 8 m {d8} -> {d8Demo}, at 10 m {d10} -> {d10Demo}");
        if (d8 == null)
            Line("Demolitions: no open direction around the test spot, blast check skipped");
        else if (d8 != (int)MathF.Round(Throwables.BlastDamage(fragDef, 8)) || d8Demo != (int)MathF.Round(Throwables.BlastDamage(fragDef, 8 / Perks.DemolitionsRadiusMult))
                 || d10 != 0 || d10Demo <= 0)
            Fail("Demolitions doesn't widen the grenade blast as described");

        // The new perks never change bullet damage.
        foreach (var id in new[] { PerkId.SleightOfHand, PerkId.SteadyAim, PerkId.TightGrip, PerkId.Demolitions })
            if (Perks.AttackMult(new PerkSet(PerkId.None, PerkId.None, id), 5, Vector3.UnitZ, 0, 100, false) != 1f)
                Fail($"{Perks.Get(id).Name} changes bullet damage");
    }
}
