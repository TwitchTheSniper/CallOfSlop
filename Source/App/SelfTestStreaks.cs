using System.Numerics;

namespace CallOfSlop;

/// <summary>Self-test section "Kill streaks" (see SelfTest.cs).</summary>
public static partial class SelfTest
{
    private static void CheckKillStreaks()
    {
        // ---- Definitions, kill ids and unlocks -------------------------------------------------
        var defs = KillStreaks.All.Where(s => s.Id != StreakId.None).ToArray();
        Line("streaks: " + string.Join(", ", defs.Select(s => $"{s.Name} ({s.Kills} kills, lvl {KillStreaks.UnlockLevel(s.Id)})")));
        if (defs.Length is < 6 or > 8)
            Fail($"{defs.Length} streaks (want 6-8)");
        if (defs.Select(s => s.Name).Distinct().Count() != defs.Length || defs.Any(s => s.Kills < 2 || s.Effect.Length < 20))
            Fail("streak names must be unique, cost at least 2 kills, and have a description");
        if (KillStreaks.All.Select((s, i) => (int)s.Id != i).Any(x => x))
            Fail("KillStreaks.All must be indexed by id");
        if (KillStreaks.KillId(defs.Max(s => s.Id)) >= Game.MeleeKillId || KillStreaks.KillId(StreakId.None) != 230)
            Fail("streak kill ids must sit in 230-238");
        if (Throwables.KillName(KillStreaks.KillId(StreakId.SentryTurret)) != "Sentry Turret" || Throwables.KillName(Game.MeleeKillId) != "Melee")
            Fail($"kill feed name for a turret kill: '{Throwables.KillName(KillStreaks.KillId(StreakId.SentryTurret))}'");
        if (KillStreaks.IsStreakKill(Game.MeleeKillId) || KillStreaks.IsStreakKill(Weapons.DefaultPrimary.Id) || !KillStreaks.IsStreakKill(KillStreaks.KillId(StreakId.StrafeRun)))
            Fail("IsStreakKill is wrong");
        var levels = KillStreaks.UnlockOrder.Select(KillStreaks.UnlockLevel).ToArray();
        if (KillStreaks.Starters.Length != 3 || KillStreaks.Starters.Any(s => KillStreaks.UnlockLevel(s) != 1))
            Fail("there must be three starter streaks at level 1");
        if (levels.Any(l => l < 2 || l > Progress.MaxLevel) || levels.Zip(levels.Skip(1)).Any(p => p.Second <= p.First))
            Fail($"streak unlock levels {string.Join(",", levels)} must rise within 2..{Progress.MaxLevel}");
        var gaps = levels.Zip(levels.Skip(1)).Select(p => p.Second - p.First).ToArray();
        if (gaps.Length > 0 && gaps.Max() - gaps.Min() > 1)
            Fail($"streak unlocks aren't evenly spread: gaps {string.Join(",", gaps)}");
        if (defs.Any(s => !Progress.All.Any(u => u.Streak == s.Id && u.Level == KillStreaks.UnlockLevel(s.Id))))
            Fail("a streak is missing from the unlock table");
        var fresh = new Progress { ReadOnly = true };
        var maxed = new Progress { ReadOnly = true, Xp = Progress.TotalFor(Progress.MaxLevel) };
        if (!KillStreaks.Starters.All(fresh.IsUnlocked) || fresh.IsUnlocked(StreakId.StrafeRun) || !defs.All(s => maxed.IsUnlocked(s.Id)))
            Fail("streak unlock checks are wrong");
        Line($"unlocks: starters {StreakSet.Starter}; then {string.Join(", ", KillStreaks.UnlockOrder.Select(s => $"{KillStreaks.Get(s).Name} at {KillStreaks.UnlockLevel(s)}"))}");

        // Streak kills give normal kill XP (no weapon XP, no grenade bonus).
        var xpCheck = new Progress { ReadOnly = true };
        var turretKill = KillStreaks.KillId(StreakId.SentryTurret);
        xpCheck.AwardKill(false, turretKill >= Throwables.KillIdBase, 0, turretKill);
        if (xpCheck.Xp != Progress.KillXp)
            Fail($"a streak kill gave {xpCheck.Xp} XP (should be {Progress.KillXp})");

        // ---- Streak sets ------------------------------------------------------------------------
        var messy = new StreakSet(StreakId.StrafeRun, StreakId.ReconSweep, StreakId.ReconSweep).Sanitized();
        if (messy != new StreakSet(StreakId.ReconSweep, StreakId.StrafeRun, StreakId.None))
            Fail($"Sanitized didn't dedupe and sort: {messy}");
        var full = new StreakSet(StreakId.ReconSweep, StreakId.MortarBarrage, StreakId.AttackDrone).Sanitized();
        if (full.With(StreakId.SentryTurret, StreakId.MortarBarrage) != new StreakSet(StreakId.ReconSweep, StreakId.SentryTurret, StreakId.AttackDrone)
            || full.With(StreakId.ReconSweep) != full || full.Without(StreakId.MortarBarrage).Count != 2
            || full.Without(StreakId.MortarBarrage).With(StreakId.StrafeRun) != new StreakSet(StreakId.ReconSweep, StreakId.AttackDrone, StreakId.StrafeRun))
            Fail("StreakSet With/Without are wrong");

        // ---- Loadout file: save, load, migration --------------------------------------------------
        var backup = LoadoutStore.SaveLines().ToArray();
        LoadoutStore.LoadLines(["selected=0", "loadout1.name=Old", "loadout1.primary=Hornet SMG", "loadout1.perks=Low Profile"]);
        var oldSlots = LoadoutStore.Slots[0].Streaks;
        LoadoutStore.LoadLines(["primary=Hornet SMG", "grenade=Frag"]); // the single-loadout format
        var olderSlots = LoadoutStore.Slots[0].Streaks;
        if (oldSlots != StreakSet.Starter || olderSlots != StreakSet.Starter)
            Fail($"loadout files from before streaks should get the starters: {oldSlots} / {olderSlots}");
        var pick = new StreakSet(StreakId.StrafeRun, StreakId.SentryTurret, StreakId.CounterSweep).Sanitized();
        LoadoutStore.Slots[1].Streaks = pick;
        var lines = LoadoutStore.SaveLines();
        var line = lines.FirstOrDefault(x => x.StartsWith("loadout2.streaks=")) ?? "(missing)";
        LoadoutStore.LoadLines(lines.ToArray());
        if (LoadoutStore.Slots[1].Streaks != pick || line != "loadout2.streaks=Counter Sweep, Sentry Turret, Strafe Run")
            Fail($"streaks didn't survive save/load: '{line}' -> {LoadoutStore.Slots[1].Streaks}");
        LoadoutStore.LoadLines(["loadout1.streaks=Recon Sweep, Nonsense, recon sweep"]);
        if (LoadoutStore.Slots[0].Streaks != new StreakSet(StreakId.ReconSweep, StreakId.None, StreakId.None))
            Fail($"junk in the streaks line wasn't cleaned: {LoadoutStore.Slots[0].Streaks}");
        var lockedEff = LoadoutStore.Effective(new Loadout { Streaks = pick }, fresh).Streaks;
        if (lockedEff != StreakSet.Starter)
            Fail($"locked streaks should fall back to starters at level 1: {lockedEff}");
        Line($"loadout file: old files -> {oldSlots}; saved '{line}'; locked at level 1 -> {lockedEff}");
        LoadoutStore.LoadLines(backup);

        // ---- Packets ----------------------------------------------------------------------------
        var w = new PacketWriter();
        KillStreaks.WriteUse(w, StreakId.MortarBarrage, new Vector3(1, 2, 3), 0.5f);
        var useLen = w.Length - 1;
        var r = new PacketReader(w.Span[1..]);
        var use = KillStreaks.ReadUse(ref r);
        if (useLen != KillStreaks.UseBytes || use.Id != StreakId.MortarBarrage || use.Target != new Vector3(1, 2, 3) || use.Yaw != 0.5f)
            Fail("StreakUse round trip broke");
        KillStreaks.WriteEvent(w, 1, 3, StreakId.SentryTurret, new Vector3(4, 5, 6), -1f, 0xDEADBEEF, 7);
        var evtLen = w.Length - 1;
        r = new PacketReader(w.Span[1..]);
        var evt = KillStreaks.ReadEvent(ref r);
        if (evtLen != KillStreaks.EventBytes || evt != (1, 3, StreakId.SentryTurret, new Vector3(4, 5, 6), -1f, 0xDEADBEEF, 7))
            Fail("StreakEvent round trip broke");
        var st = new StreakState { Count = 9, Ready = 0b1010, Slots = pick };
        st.Progress[(int)StreakId.StrafeRun] = 7;
        st.Progress[(int)StreakId.CounterSweep] = 2;
        KillStreaks.WriteState(w, st);
        var stLen = w.Length - 1;
        r = new PacketReader(w.Span[1..]);
        var st2 = new StreakState();
        KillStreaks.ReadState(ref r, st2);
        if (stLen != KillStreaks.StateBytes || st2.Count != 9 || st2.Ready != 0b1010 || st2.Slots != pick
            || st2.ProgressOf(StreakId.StrafeRun) != 7 || st2.ProgressOf(StreakId.CounterSweep) != 2 || st2.ProgressOf(StreakId.SentryTurret) != 0)
            Fail("StreakState round trip broke");
        var ent = new StreakEntity { Id = 4, Kind = StreakId.AttackDrone, Owner = 2, Pos = new Vector3(-3, 20, 7), Yaw = 2.5f, Pitch = -0.4f, Health = 150, Target = 5, Firing = true };
        w.Begin(Msg.StreakEnts).U32(1).U8(16);
        for (var i = 0; i < 16; i++)
            Game.WriteEntity(w, ent);
        r = new PacketReader(w.Span[1..]);
        r.U32();
        r.U8();
        var back = Game.ReadEntity(ref r);
        var entsLen = w.Length;
        if (back.Id != 4 || back.Kind != StreakId.AttackDrone || back.Owner != 2 || back.Pos != ent.Pos || MathF.Abs(back.Yaw - 2.5f) > 0.001f
            || MathF.Abs(back.Pitch + 0.4f) > 0.001f || Math.Abs(back.Health - 150) > 1 || back.Target != 5 || !back.Firing)
            Fail($"entity round trip broke: yaw {back.Yaw}, pitch {back.Pitch}, health {back.Health}");
        if ((entsLen - 6) / 16 != StreakEntity.NetBytes || entsLen > 1200)
            Fail($"entity update: {(entsLen - 6) / 16} bytes each (expected {StreakEntity.NetBytes}), {entsLen} for 16");
        Line($"packets: use {useLen} B, event {evtLen} B, state {stLen} B, entity {StreakEntity.NetBytes} B each ({entsLen} B for 16)");

        // ---- Server: counting, earning, reset on death, activation ---------------------------------
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2);
        game.SelfTestStreakTick(0); // first tick: match reset
        var me = game.Players[0];
        var bots = game.Players.Values.Where(p => p.Bot != null).ToArray();
        Player b1 = bots[0], b2 = bots[1];
        foreach (var p in game.Players.Values)
            game.SelfTestPerks(p, PerkSet.Empty);
        game.SelfTestStreakRequest(me, StreakSet.Starter); // not spawned yet: applies at once
        game.SelfTestPlace(me, new Vector3(5, 0, 5), 0);
        var weapon = Weapons.DefaultPrimary.Id;
        var earnedAt = new List<string>();
        for (var k = 1; k <= 5; k++)
        {
            game.SelfTestPlace(b1, new Vector3(8, 0, 8), 0);
            game.SelfTestKill(me, b1, weapon, k);
            foreach (var id in me.Streak.Slots.Ids)
                if (me.Streak.IsReady(id) && !earnedAt.Any(e => e.StartsWith(KillStreaks.Get(id).Name)))
                    earnedAt.Add($"{KillStreaks.Get(id).Name} at {k}");
            var expectReady = me.Streak.Slots.Ids.Where(id => KillStreaks.Get(id).Kills <= k).Aggregate((byte)0, (m, id) => (byte)(m | StreakState.Bit(id)));
            if (me.Streak.Count != k || me.Streak.Ready != expectReady)
                Fail($"after {k} kills: count {me.Streak.Count}, ready {me.Streak.Ready:b} (expected {expectReady:b})");
        }
        if (b1.Streak.Count != 0)
            Fail("the victim's streak didn't reset");
        var readyBefore = me.Streak.Ready;
        game.SelfTestPlace(b2, new Vector3(2, 0, 2), 0);
        game.SelfTestKill(b2, me, weapon, 6);
        Line($"earned: {string.Join(", ", earnedAt)}; after dying: count {me.Streak.Count}, still ready {me.Streak.Ready == readyBefore}; killer's count {b2.Streak.Count}");
        if (me.Streak.Count != 0 || me.Streak.Ready != readyBefore)
            Fail("dying must reset the count but keep earned streaks");
        if (b2.Streak.Count != 1)
            Fail($"the killer's count is {b2.Streak.Count} (expected 1)");
        if (game.SelfTestUseStreak(me, StreakId.ReconSweep, me.Position, 0, 7))
            Fail("a dead player used a streak");
        game.SelfTestPlace(me, new Vector3(5, 0, 5), 0);
        game.SelfTestPlace(b1, new Vector3(-10, 0, -10), 0);
        if (game.SelfTestUseStreak(me, StreakId.SentryTurret, me.Position, 0, 7))
            Fail("used a streak that isn't carried");
        if (game.IsRevealed(b1, 7))
            Fail("an enemy was revealed with no recon running");
        var sweep = game.SelfTestUseStreak(me, StreakId.ReconSweep, me.Position, 0, 7);
        var again = game.SelfTestUseStreak(me, StreakId.ReconSweep, me.Position, 0, 7.1);
        var revealed = game.IsRevealed(b1, 8);
        var revealedLater = game.IsRevealed(b1, 7 + KillStreaks.ReconSweepSeconds + 0.5);
        Line($"Recon Sweep: used {sweep}, used twice {again}, enemy revealed {revealed}, after it ends {revealedLater}");
        if (!sweep || again || !revealed || revealedLater || me.Streak.IsReady(StreakId.ReconSweep))
            Fail("Recon Sweep activation / reveal is wrong");

        // Using a streak empties its stored kills; fresh kills fill it again (the others keep their own count).
        var reconCost = KillStreaks.Get(StreakId.ReconSweep).Kills;
        var afterUse = me.Streak.ProgressOf(StreakId.ReconSweep);
        var refilled = new List<int>();
        for (var k = 0; k < reconCost; k++)
        {
            game.SelfTestPlace(b1, new Vector3(8, 0, 8), 0);
            game.SelfTestKill(me, b1, weapon, 7.2 + k * 0.01);
            refilled.Add(me.Streak.ProgressOf(StreakId.ReconSweep));
        }
        game.SelfTestPlace(b1, new Vector3(-10, 0, -10), 0);
        Line($"after using Recon Sweep: stored kills {afterUse}, refilled {string.Join(" -> ", refilled)}, ready again {me.Streak.IsReady(StreakId.ReconSweep)}");
        if (afterUse != 0 || !me.Streak.IsReady(StreakId.ReconSweep) || refilled[^1] != reconCost || refilled[0] != Math.Min(1, reconCost))
            Fail("a used streak must empty and be earned again by fresh kills");

        b2.Streak.Slots = new StreakSet(StreakId.CounterSweep, StreakId.None, StreakId.None);
        b2.Streak.Ready = StreakState.Bit(StreakId.CounterSweep);
        game.SelfTestUseStreak(b2, StreakId.CounterSweep, b2.Position, 0, 8);
        var jammedReveal = game.IsRevealed(b1, 8.5);
        Line($"Counter Sweep: I'm jammed {game.StreakJammed(me, 8.5)}, its owner jammed {game.StreakJammed(b2, 8.5)}, my sweep still reveals {jammedReveal}");
        if (!game.StreakJammed(me, 8.5) || game.StreakJammed(b2, 8.5) || jammedReveal)
            Fail("Counter Sweep should jam its owner's enemies (and only them)");

        me.Sim.BoostTicks = 0;
        game.SelfTestUseStreak(me, StreakId.Overdrive, me.Position, 0, 9);
        game.SelfTestStreakTick(9.1);
        var boost = me.Sim.BoostTicks;
        var reload = game.StreakReloadMult(me, 9.2);
        game.SelfTestKill(b2, me, weapon, 9.3);
        Line($"Overdrive: boost {boost} ticks, reload x{reload}, active after death {game.OverdriveActive(me, 9.4)}");
        if (boost < 20 || reload != KillStreaks.OverdriveReloadMult || game.OverdriveActive(me, 9.4))
            Fail("Overdrive should boost speed and reload, and end on death");

        // ---- Mortar: blasts respect walls; streak kills don't chain ------------------------------
        var wallMap = MapFile.Load("""
            { "name": "Streak test", "halfX": 40, "halfZ": 40, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Block", "x0": -4, "y0": 0, "z0": 1.5, "x1": 4, "y1": 4.0, "z1": 2.5, "color": "#808080" },
              { "op": "Spawn", "x": 30, "z": 30 } ] }
            """, "streaktest.json");
        World.SetMap(wallMap);
        game.SelfTestPlace(me, new Vector3(-3, 0, 0), 0);
        game.SelfTestPlace(b1, new Vector3(3, 0, 0), 0); // in the open, 3 m away
        game.SelfTestPlace(b2, new Vector3(0, 0, 3), 0); // 3 m away behind the wall
        game.SelfTestStreakBlast(me, Vector3.Zero, KillStreaks.MortarRadius, KillStreaks.MortarDamage, 20);
        var openDmg = World.MaxHealth - b1.Health;
        var wallDmg = World.MaxHealth - b2.Health;
        var selfDmg = World.MaxHealth - me.Health;
        Line($"mortar shell at 3 m: in the open {openDmg} dmg, behind a wall {wallDmg} dmg, its owner {selfDmg} dmg");
        if (openDmg <= 0 || wallDmg != 0 || selfDmg != 0)
            Fail("a mortar blast must hurt enemies in the open, never through walls, and not its owner");

        game.SelfTestPlace(me, new Vector3(-20, 0, -20), 0);
        game.SelfTestPlace(b1, new Vector3(10, 0, -12), 0);
        me.Streak.Slots = new StreakSet(StreakId.MortarBarrage, StreakId.SentryTurret, StreakId.StrafeRun).Sanitized();
        me.Streak.Ready = 0xFF;
        var countBefore = me.Streak.Count;
        var killsBefore = me.Kills;
        var called = game.SelfTestUseStreak(me, StreakId.MortarBarrage, new Vector3(10, 0, -12), 0.3f, 21);
        var pending = game.SelfTestPendingStrikes;
        var hpAtCall = b1.Health;
        for (var t = 21.0; t < 21 + 6; t += 1.0 / 60)
            game.SelfTestStreakTick(t);
        Line($"Mortar Barrage: called {called}, {pending} shells scheduled, target {hpAtCall} -> {(b1.Alive ? b1.Health + " HP" : "dead")}, owner's streak {countBefore} -> {me.Streak.Count}, kills {killsBefore} -> {me.Kills}");
        if (!called || pending != KillStreaks.MortarShells || game.SelfTestPendingStrikes != 0)
            Fail("Mortar Barrage didn't schedule and fire its shells");
        if (b1.Alive && b1.Health >= hpAtCall)
            Fail("the mortar never hurt someone standing on the spot");
        game.SelfTestPlace(b1, new Vector3(10, 0, -12), 0);
        game.SelfTestKill(me, b1, KillStreaks.KillId(StreakId.MortarBarrage), 28);
        if (me.Streak.Count != countBefore)
            Fail("a kill by a streak counted toward the next streak");
        var plan1 = KillStreaks.Plan(StreakId.StrafeRun, new Vector3(0, 0, -10), 1f, 1234);
        var plan2 = KillStreaks.Plan(StreakId.StrafeRun, new Vector3(0, 0, -10), 1f, 1234);
        if (plan1.Count != KillStreaks.StrafeHits || !plan1.SequenceEqual(plan2))
            Fail("strike plans must be deterministic");

        // ---- Sentry turret: targets in its cone, lifetime, destroyable --------------------------
        var flat = MapFile.Load("""
            { "name": "Turret test", "halfX": 40, "halfZ": 40, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] }, { "op": "Spawn", "x": 30, "z": 30 } ] }
            """, "turrettest.json");
        World.SetMap(flat);
        game.SelfTestPlace(me, Vector3.Zero, MathF.PI); // facing +Z
        game.SelfTestPlace(b1, new Vector3(0, 0, 13), 0); // in front
        game.SelfTestPlace(b2, new Vector3(0, 0, -10), 0); // behind: outside its cone
        var placed = game.SelfTestUseStreak(me, StreakId.SentryTurret, me.Position, MathF.PI, 30);
        var turret = game.StreakEntities.Values.FirstOrDefault();
        countBefore = me.Streak.Count;
        var t0 = 30.0;
        for (var t = t0; t < t0 + 4; t += 1.0 / 60)
            game.SelfTestStreakTick(t);
        Line($"Sentry Turret: placed {placed} at {turret?.Pos}, after 4 s: target in front {(b1.Alive ? b1.Health + " HP" : "dead")}, behind it {b2.Health} HP, owner's streak {countBefore} -> {me.Streak.Count}");
        if (!placed || turret == null || turret.Pos.Z < 0.8f)
            Fail("the turret wasn't placed in front of its owner");
        if (b1.Alive && b1.Health >= World.MaxHealth)
            Fail("the turret didn't shoot the enemy in front of it");
        if (b2.Health != World.MaxHealth)
            Fail("the turret shot someone outside its sight cone");
        if (me.Streak.Count != countBefore)
            Fail("turret kills counted toward the next streak");
        game.SelfTestStreakTick(t0 + KillStreaks.TurretSeconds + 1);
        if (game.StreakEntities.Count != 0)
            Fail("the turret outlived its 45 seconds");

        // Shot down: the owner's bullets pass through it, an enemy's destroy it.
        game.SelfTestPlace(me, Vector3.Zero, MathF.PI);
        game.SelfTestPlace(b1, new Vector3(30, 0, 30), 0);
        me.Streak.Ready = 0xFF;
        game.SelfTestUseStreak(me, StreakId.SentryTurret, me.Position, MathF.PI, 81);
        var t2 = game.StreakEntities.Values.First();
        game.SelfTestPlace(me, new Vector3(0, 0, 8), 0);
        var center = t2.Hitbox.Center;
        game.SelfTestShoot(me, Vector3.Normalize(center - me.Eye), 82);
        var ownHit = t2.Health != t2.MaxHealth;
        game.SelfTestPlace(b2, new Vector3(0, 0, 8), 0);
        game.SelfTestPlace(me, new Vector3(-20, 0, -20), 0);
        var shots = 0;
        while (game.StreakEntities.ContainsKey(t2.Id) && shots < 60)
        {
            game.SelfTestShoot(b2, Vector3.Normalize(center - b2.Eye), 82 + shots * 0.2);
            shots++;
        }
        Line($"turret health {t2.MaxHealth}: owner's shot hurt it {ownHit}; an enemy destroyed it in {shots} shots");
        if (ownHit || game.StreakEntities.ContainsKey(t2.Id))
            Fail("turret damage is wrong (the owner can't hurt it, enemies can destroy it)");

        // ---- Bots use their streaks --------------------------------------------------------------
        World.SetMap(Maps.All[0]);
        using var botGame = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 5);
        botGame.MapVoteEnabled = false;
        var bot = botGame.Players.Values.First(p => p.Bot != null);
        var botSet = new StreakSet(StreakId.ReconSweep, StreakId.MortarBarrage, StreakId.SentryTurret).Sanitized();
        bot.Streak.Want = bot.Streak.Slots = botSet;
        bot.Streak.BotPicked = true;
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var usedAt = new Dictionary<StreakId, double>();
        var sawTurret = false;
        var granted = false;
        for (var i = 0; i < 30 * World.TickRate; i++)
        {
            now += dt;
            if (!granted && now > Game.CountdownSeconds + 0.5 && botGame.Phase == MatchPhase.Playing)
            {
                // Earned all three at once (as if by kills), once the match is under way.
                granted = true;
                bot.Streak.Ready = (byte)(StreakState.Bit(StreakId.ReconSweep) | StreakState.Bit(StreakId.MortarBarrage) | StreakState.Bit(StreakId.SentryTurret));
            }
            botGame.Update(now, dt);
            if (granted)
                foreach (var id in botSet.Ids)
                    if (!usedAt.ContainsKey(id) && !bot.Streak.IsReady(id))
                        usedAt[id] = now;
            sawTurret |= botGame.StreakEntities.Values.Any(e => e.Owner == bot.Id);
        }
        Line($"bot {bot.Name}: used {string.Join(", ", usedAt.Select(kv => $"{KillStreaks.Get(kv.Key).Name} at {kv.Value:0.0} s"))}; turret seen {sawTurret}; still ready {bot.Streak.Ready:b}");
        if (usedAt.Count < 2 || !sawTurret && usedAt.ContainsKey(StreakId.SentryTurret))
            Fail("a bot didn't use its earned streaks");
        World.SetMap(Maps.All[0]);
    }
}
