using System.Numerics;

namespace CallOfSlop;

/// <summary>Self-test section "More kill streaks" (see SelfTest.cs): the eight streaks added after the first seven, in short scenarios.</summary>
public static partial class SelfTest
{
    private static void CheckMoreKillStreaks()
    {
        // ---- Definitions, kill feed, packets, loadout file --------------------------------------------
        StreakId[] added = [StreakId.SmokeScreen, StreakId.BallisticVests, StreakId.HunterDrone, StreakId.SupplyDrop,
            StreakId.NapalmStrike, StreakId.EmpBlast, StreakId.Gunship, StreakId.Juggernaut];
        Line("added: " + string.Join(", ", added.Select(id => $"{KillStreaks.Get(id).Name} ({KillStreaks.Get(id).Kills} kills, lvl {KillStreaks.UnlockLevel(id)}, kill id {KillStreaks.KillId(id)})")));
        foreach (var id in added)
        {
            var def = KillStreaks.Get(id);
            if (def.Id != id || Throwables.KillName(KillStreaks.KillId(id)) != def.Name || !KillStreaks.IsStreakKill(KillStreaks.KillId(id)) || Game.FrenzyGunKill(KillStreaks.KillId(id)))
                Fail($"{def.Name}: kill id {KillStreaks.KillId(id)} doesn't name it in the kill feed or isn't a streak kill");
            if (StreakState.Bit(id) == 0)
                Fail($"{def.Name}: no Ready bit");
        }
        var costs = KillStreaks.All.Where(s => s.Id != StreakId.None).Select(s => s.Kills).ToArray();
        if (costs.Min() != 3 || costs.Max() != 15 || added.Count(id => KillStreaks.Get(id).Kills <= 5) < 3 || added.Count(id => KillStreaks.Get(id).Kills >= 9) < 3)
            Fail($"the added streaks should span cheap, mid and expensive costs: {string.Join(",", added.Select(id => KillStreaks.Get(id).Kills))}");

        var w = new PacketWriter();
        var st = new StreakState { Count = 14, Ready = (ushort)(StreakState.Bit(StreakId.Juggernaut) | StreakState.Bit(StreakId.SmokeScreen)), Armor = 345, ArmorHeavy = true,
            Slots = new StreakSet(StreakId.SmokeScreen, StreakId.EmpBlast, StreakId.Juggernaut).Sanitized() };
        st.Progress[(int)StreakId.EmpBlast] = 6;
        KillStreaks.WriteState(w, st);
        var stLen = w.Length - 1;
        var r = new PacketReader(w.Span[1..]);
        var st2 = new StreakState();
        KillStreaks.ReadState(ref r, st2);
        if (stLen != KillStreaks.StateBytes || st2.Ready != st.Ready || st2.Armor != 345 || !st2.ArmorHeavy || st2.Slots != st.Slots || st2.ProgressOf(StreakId.EmpBlast) != 6 || st2.Count != 14)
            Fail($"StreakState round trip with the later streaks broke (ready {st2.Ready:b}, armour {st2.Armor})");
        KillStreaks.WriteEvent(w, 4, 2, StreakId.Gunship, new Vector3(1, 2, 3), KillStreaks.GunshipRadius, 0, 255);
        r = new PacketReader(w.Span[1..]);
        var ev = KillStreaks.ReadEvent(ref r);
        if (ev != (4, 2, StreakId.Gunship, new Vector3(1, 2, 3), KillStreaks.GunshipRadius, 0u, 255))
            Fail("streak blast event round trip broke");
        var gunshipNet = new StreakEntity { Id = 9, Kind = StreakId.Gunship, Owner = 1, Pos = new Vector3(4, 16, -2), Health = 600, Firing = true };
        w.Begin(Msg.StreakEnts);
        Game.WriteEntity(w, gunshipNet);
        r = new PacketReader(w.Span[1..]);
        var gBack = Game.ReadEntity(ref r);
        if (gBack.Kind != StreakId.Gunship || Math.Abs(gBack.Health - 600) > 3 || !gBack.IsAirborne || gBack.Pos != gunshipNet.Pos)
            Fail($"gunship entity round trip broke (health {gBack.Health})");
        Line($"packets: state {stLen} B (16-bit ready, armour), blast event {KillStreaks.EventBytes} B, entity {StreakEntity.NetBytes} B; protocol {ServerInfo.ProtocolVersion}");

        var backup = LoadoutStore.SaveLines().ToArray();
        var pick = new StreakSet(StreakId.Juggernaut, StreakId.SmokeScreen, StreakId.EmpBlast).Sanitized();
        LoadoutStore.Slots[1].Streaks = pick;
        var lines = LoadoutStore.SaveLines();
        var saved = lines.FirstOrDefault(x => x.StartsWith("loadout2.streaks=")) ?? "(missing)";
        LoadoutStore.LoadLines(lines.ToArray());
        var loaded = LoadoutStore.Slots[1].Streaks;
        LoadoutStore.LoadLines(backup);
        Line($"loadout file: '{saved}' -> {loaded}");
        if (loaded != pick || saved != "loadout2.streaks=Smoke Screen, EMP Blast, Juggernaut")
            Fail("the later streaks didn't survive loadout save/load");

        // ---- Server scenarios on a flat map ------------------------------------------------------------
        var flat = MapFile.Load("""
            { "name": "More streaks test", "halfX": 40, "halfZ": 40, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] }, { "op": "Spawn", "x": 30, "z": 30 } ] }
            """, "morestreakstest.json");
        World.SetMap(flat);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2);
        game.ScoreLimitOverride = game.ScoreLimit = int.MaxValue; // the kills here must never end the match
        game.SelfTestStreakTick(0);
        var me = game.Players[0];
        var bots = game.Players.Values.Where(p => p.Bot != null).ToArray();
        Player b1 = bots[0], b2 = bots[1];
        foreach (var p in game.Players.Values)
            game.SelfTestPerks(p, PerkSet.Empty);
        const double dt = 1.0 / 60;
        var now = 10.0;
        bool Use(Player p, StreakId id, Vector3 target, float yaw)
        {
            p.Streak.Slots = new StreakSet(id, StreakId.None, StreakId.None).Sanitized();
            p.Streak.Ready = StreakState.Bit(id);
            return game.SelfTestUseStreak(p, id, target, yaw, now);
        }
        void Run(double seconds)
        {
            var end = now + seconds;
            for (; now < end; now += dt)
            {
                game.SelfTestStreakTick(now);
                game.SelfTestStreakFrame(now, (float)dt);
            }
        }
        void Reset()
        {
            foreach (var p in game.Players.Values)
            {
                p.Streak.Armor = 0;
                p.Streak.ArmorHeavy = false;
                game.SelfTestPerks(p, PerkSet.Empty);
            }
            game.Smokes.Clear();
            game.StreakEffects.Clear();
            game.SelfTestPlace(me, Vector3.Zero, MathF.PI); // facing +Z
            game.SelfTestPlace(b1, new Vector3(30, 0, -30), 0);
            game.SelfTestPlace(b2, new Vector3(-30, 0, -30), 0);
        }

        // Smoke Screen: a wall across the aim spot, square to the facing; blocks sight after it lands, gone when it fades.
        Reset();
        var smokeAt = now;
        var smoked = Use(me, StreakId.SmokeScreen, new Vector3(0, 0, 15), MathF.PI);
        var clouds = game.Smokes.Count;
        var blockEarly = game.SmokeBlocks(new Vector3(0, 1.5f, 5), new Vector3(0, 1.5f, 25), smokeAt + 0.3);
        var blockLanded = game.SmokeBlocks(new Vector3(0, 1.5f, 5), new Vector3(0, 1.5f, 25), smokeAt + 4);
        var blockSide = game.SmokeBlocks(new Vector3(-30, 1.5f, 15), new Vector3(30, 1.5f, 15), smokeAt + 4); // along the wall: it's in the way too
        var blockAlong = game.SmokeBlocks(new Vector3(20, 1.5f, 0), new Vector3(20, 1.5f, 30), smokeAt + 4); // beyond its end: clear
        var blockLater = game.SmokeBlocks(new Vector3(0, 1.5f, 5), new Vector3(0, 1.5f, 25), smokeAt + 5 + Throwables.SmokeDuration);
        Line($"Smoke Screen: called {smoked}, {clouds} clouds, blocks sight before landing {blockEarly}, after {blockLanded}, past its end {blockAlong}, once faded {blockLater}");
        if (!smoked || clouds != KillStreaks.SmokeScreenClouds || blockEarly || !blockLanded || !blockSide || blockAlong || blockLater)
            Fail("Smoke Screen should lay a wall of smoke across the spot that blocks sight once landed and fades");
        game.Smokes.Clear();

        // Ballistic Vests: body-shot bullets only (free-for-all: only the caller gets one).
        Reset();
        Use(me, StreakId.BallisticVests, me.Position, 0);
        var vest = me.Streak.Armor;
        var enemyVest = b1.Streak.Armor;
        game.SelfTestHit(b1, me, 30, false, now, DamageKind.Bullet);
        var afterBody = (me.Health, me.Streak.Armor);
        game.SelfTestHit(b1, me, 20, true, now, DamageKind.Bullet);
        var afterHead = (me.Health, me.Streak.Armor);
        game.SelfTestHit(b1, me, 20, false, now, DamageKind.Explosive);
        var afterBlast = (me.Health, me.Streak.Armor);
        game.SelfTestHit(b1, me, 50, false, now, DamageKind.Bullet); // 30 left on the vest: 20 goes through
        var afterBreak = (me.Health, me.Streak.Armor);
        Line($"Ballistic Vests: armour {vest} (enemy {enemyVest}); body shot 30 -> {afterBody}, headshot 20 -> {afterHead}, blast 20 -> {afterBlast}, body 50 -> {afterBreak}");
        if (vest != KillStreaks.VestArmor || enemyVest != 0 || afterBody != (100, 30) || afterHead != (80, 30) || afterBlast != (60, 30) || afterBreak != (40, 0))
            Fail("Ballistic Vests should soak body-shot bullets only, for the caller (and teammates), not enemies");

        // Juggernaut: soaks bullets and blasts (blasts 1.5x), slows you, melee still goes through, breaks and ends.
        Reset();
        Use(b1, StreakId.Juggernaut, b1.Position, 0);
        var jugShown = game.JuggernautShown(b1, now);
        var slow = game.SelfTestStreakMoveFilter(b1, new InputCmd { SpeedPct = 100 }).SpeedPct;
        var meNormal = game.SelfTestStreakMoveFilter(me, new InputCmd { SpeedPct = 100 }).SpeedPct;
        game.SelfTestHit(me, b1, 100, true, now, DamageKind.Bullet);
        var jHead = (b1.Health, b1.Streak.Armor);
        game.SelfTestHit(me, b1, 100, false, now, DamageKind.Explosive);
        var jBlast = (b1.Health, b1.Streak.Armor);
        game.SelfTestHit(me, b1, 200, false, now, DamageKind.Bullet); // 150 left: 50 goes through
        var jBreak = (b1.Health, b1.Streak.Armor, game.JuggernautShown(b1, now), Game.JuggernautOn(b1));
        game.SelfTestPlace(b1, b1.Position, 0); // back to full health
        Use(b1, StreakId.Juggernaut, b1.Position, 0);
        game.SelfTestHit(me, b1, 60, false, now, DamageKind.Melee);
        var jMelee = (b1.Health, b1.Streak.Armor);
        game.SelfTestKill(me, b1, Weapons.DefaultPrimary.Id, now); // 1000 damage: the suit soaks 400 of it, the rest kills (and the suit is gone)
        var diedOrHeld = (b1.Alive, b1.Streak.Armor);
        Line($"Juggernaut: shown {jugShown}, bot speed 100 -> {slow} (others {meNormal}); headshot 100 -> {jHead}, blast 100 -> {jBlast}, bullets 200 -> {jBreak}; melee 60 -> {jMelee}; 1000 dmg -> alive/armour {diedOrHeld}");
        if (!jugShown || slow != (byte)MathF.Round(100 * KillStreaks.JuggernautMove) || meNormal != 100 || jHead != (100, 300) || jBlast != (100, 150)
            || jBreak != (50, 0, false, false) || jMelee != (40, KillStreaks.JuggernautArmor) || diedOrHeld.Alive || diedOrHeld.Armor != 0)
            Fail("Juggernaut armour, slowdown or ending is wrong");

        // Hunter Drone: finds the nearest enemy, dives and kills; Flak Jacket survives; Cold Blooded hides; shot down.
        Reset();
        game.SelfTestPlace(b1, new Vector3(0, 0, 25), 0);
        game.SelfTestPlace(b2, new Vector3(35, 0, 35), 0);
        var countBefore = me.Streak.Count;
        var hunted = Use(me, StreakId.HunterDrone, me.Position, MathF.PI);
        var hunter = game.StreakEntities.Values.FirstOrDefault(e => e.Kind == StreakId.HunterDrone);
        var airborne = hunter?.IsAirborne ?? false;
        var t0 = now;
        var hitAt = -1.0;
        while (now < t0 + 10 && hitAt < 0)
        {
            Run(dt * 6);
            if (!b1.Alive || b1.Health < World.MaxHealth)
                hitAt = now - t0;
        }
        Run(0.2);
        var hunterGone = !game.StreakEntities.Values.Any(e => e.Kind == StreakId.HunterDrone);
        Line($"Hunter Drone: launched {hunted} (airborne {airborne}), hit its target at {hitAt:0.0} s: {(b1.Alive ? b1.Health + " HP" : "dead")}, far enemy {b2.Health} HP, gone after {hunterGone}, owner's streak {countBefore} -> {me.Streak.Count}");
        if (!hunted || !airborne || hitAt < 0 || b1.Alive || b2.Health != World.MaxHealth || !hunterGone || me.Streak.Count != countBefore)
            Fail("the Hunter Drone should seek the nearest enemy, dive and kill them, then be gone (a streak kill)");

        Reset();
        game.SelfTestPerks(b1, new PerkSet(PerkId.None, PerkId.FlakJacket, PerkId.None));
        game.SelfTestPlace(b1, new Vector3(0, 0, 25), 0);
        game.SelfTestPlace(b2, new Vector3(35, 0, 35), 0);
        game.SelfTestPerks(b2, new PerkSet(PerkId.None, PerkId.ColdBlooded, PerkId.None));
        Use(me, StreakId.HunterDrone, me.Position, MathF.PI);
        Run(8);
        var flakHp = b1.Health;
        Reset();
        game.SelfTestPerks(b1, new PerkSet(PerkId.None, PerkId.ColdBlooded, PerkId.None));
        game.SelfTestPerks(b2, new PerkSet(PerkId.None, PerkId.ColdBlooded, PerkId.None));
        game.SelfTestPlace(b1, new Vector3(0, 0, 25), 0);
        Use(me, StreakId.HunterDrone, me.Position, MathF.PI);
        Run(5);
        var coldHp = b1.Health;
        var hovering = game.StreakEntities.Values.FirstOrDefault(e => e.Kind == StreakId.HunterDrone);
        var shotDown = hovering != null && game.DamageStreakMachine(hovering, b1, 60, DamageKind.Bullet, now);
        var ownCant = true;
        Use(me, StreakId.HunterDrone, me.Position, MathF.PI);
        if (game.StreakEntities.Values.FirstOrDefault(e => e.Kind == StreakId.HunterDrone) is { } mine)
            ownCant = !game.DamageStreakMachine(mine, me, 500, DamageKind.Explosive, now);
        Run(KillStreaks.HunterSeconds + 1); // nobody to hunt: it runs out of fuel and goes off over its owner, hurting nobody
        Line($"Hunter Drone counters: Flak Jacket left {flakHp} HP; Cold Blooded {coldHp} HP (it hovered: {hovering != null}); shot down {shotDown}; owner can't hurt it {ownCant}; owner after it ran out {me.Health} HP, entities {game.StreakEntities.Count}");
        if (flakHp <= 0 || flakHp >= World.MaxHealth || !b1.Alive && flakHp <= 0 || coldHp != World.MaxHealth || !shotDown || !ownCant || me.Health != World.MaxHealth || game.StreakEntities.Count != 0)
            Fail("Hunter Drone counterplay is wrong (Flak Jacket survives, Cold Blooded hides, bullets bring it down, it never hurts its owner)");

        // Supply Drop: falls, lands, an enemy can steal it: full health, restock, +1 kill on unearned streaks.
        Reset();
        var dropped = Use(me, StreakId.SupplyDrop, me.Position, MathF.PI);
        var crate = game.StreakEntities.Values.FirstOrDefault(e => e.Kind == StreakId.SupplyDrop);
        var startY = crate?.Pos.Y ?? 0;
        Run(3.5);
        var landed = crate is { Landed: true } && MathF.Abs(crate.Pos.Y - crate.GroundY) < 0.01f && crate.GroundY < 0.5f;
        b1.Streak.Slots = StreakSet.Starter;
        b1.Streak.Ready = 0;
        Array.Clear(b1.Streak.Progress);
        game.SelfTestHit(me, b1, 70, false, now, DamageKind.Bullet);
        var hurt = b1.Health;
        b1.GrenadesLeft = 0;
        game.SelfTestPlace(b1, crate?.Pos ?? Vector3.Zero, 0);
        b1.Health = hurt;
        Run(0.2);
        var taken = !game.StreakEntities.Values.Any(e => e.Kind == StreakId.SupplyDrop);
        var prog = StreakSet.Starter.Ids.Select(b1.Streak.ProgressOf).ToArray();
        Line($"Supply Drop: called {dropped}, fell from {startY:0.0} m to {crate?.GroundY:0.00}, landed {landed}; an enemy on {hurt} HP took it: taken {taken}, health {b1.Health}, grenades {b1.GrenadesLeft}, streak kills {string.Join(",", prog)}");
        if (!dropped || crate == null || startY < 20 || !landed || !taken || b1.Health != World.MaxHealth || b1.GrenadesLeft != Throwables.Get(b1.Grenade).PerLife || prog.Any(x => x != 1))
            Fail("Supply Drop should fall, land and be taken by whoever touches it first, with its rewards");

        // Napalm Strike: burns a line; people off it are fine; roof-less fire runs out and is cleaned up.
        Reset();
        game.SelfTestPlace(b1, new Vector3(0, 0, 22), 0); // on the line
        game.SelfTestPlace(b2, new Vector3(9, 0, 22), 0); // 9 m to the side
        var napalm = Use(me, StreakId.NapalmStrike, new Vector3(0, 0, 20), MathF.PI);
        var zones = game.FireZones.Count;
        Run(KillStreaks.NapalmDelay + 1.5);
        var burning = b1.Alive ? b1.Health : 0;
        Run(KillStreaks.FireSeconds + 2);
        Line($"Napalm Strike: called {napalm}, {zones} fire patches; on the line {(b1.Alive ? b1.Health + " HP" : "dead")} (1.5 s after landing {burning}), 9 m off it {b2.Health} HP; patches left after {game.FireZones.Count}");
        if (!napalm || zones != KillStreaks.NapalmHits || b1.Alive && b1.Health >= World.MaxHealth || b2.Health != World.MaxHealth || game.FireZones.Count != 0)
            Fail("Napalm Strike should burn the line (not beside it) and burn out");

        // EMP Blast: an enemy turret goes quiet while it runs, then fires again; their HUD is jammed, the caller's isn't.
        Reset();
        game.SelfTestPlace(b2, Vector3.Zero, MathF.PI);
        game.SelfTestPlace(me, new Vector3(0, 0, 12), 0);
        game.SelfTestPlace(b1, new Vector3(-30, 0, -30), 0);
        Use(b2, StreakId.SentryTurret, b2.Position, MathF.PI);
        game.SelfTestPlace(b2, new Vector3(-20, 0, 30), 0);
        Use(b2, StreakId.HunterDrone, b2.Position, 0);
        var hunterBefore = game.StreakEntities.Values.Count(e => e.Kind == StreakId.HunterDrone);
        Use(me, StreakId.EmpBlast, me.Position, 0);
        var hunterAfter = game.StreakEntities.Values.Count(e => e.Kind == StreakId.HunterDrone);
        var jammed = (game.StreakJammed(b2, now + 1), game.StreakJammed(me, now + 1));
        Run(4);
        var hpDuringEmp = me.Health;
        Run(KillStreaks.EmpSeconds - 4 + 0.5);
        game.SelfTestPlace(me, new Vector3(0, 0, 12), 0);
        Run(3);
        var hpAfterEmp = me.Alive ? me.Health : 0;
        Line($"EMP Blast: enemy hunter drones {hunterBefore} -> {hunterAfter}; jammed (enemy, caller) {jammed}; in front of an enemy turret during it {hpDuringEmp} HP, after it {hpAfterEmp} HP");
        if (hunterBefore != 1 || hunterAfter != 0 || jammed != (true, false) || hpDuringEmp != World.MaxHealth || hpAfterEmp >= World.MaxHealth)
            Fail("EMP Blast should destroy enemy hunter drones, jam the enemy and silence their turret for its duration only");
        game.SelfTestStreakTick(now + 100); // clear the turret
        Run(0.1);

        // Gunship: shells an enemy in the open; can be shot down (not by its owner).
        Reset();
        game.SelfTestPlace(me, new Vector3(0, 0, 0), 0);
        var called = Use(b1, StreakId.Gunship, b1.Position, 0);
        var ship = game.StreakEntities.Values.FirstOrDefault(e => e.Kind == StreakId.Gunship);
        Run(KillStreaks.GunshipWarmup + 4);
        var shelled = me.Alive ? me.Health : 0;
        var ownerHit = ship != null && game.DamageStreakMachine(ship, b1, 5000, DamageKind.Explosive, now);
        game.SelfTestPlace(me, new Vector3(0, 0, 0), 0);
        var hitsToDown = 0;
        while (ship != null && game.StreakEntities.ContainsKey(ship.Id) && hitsToDown < 200)
        {
            game.DamageStreakMachine(ship, me, 24, DamageKind.Bullet, now); // a Vanguard AR body shot
            hitsToDown++;
        }
        Line($"Gunship: called {called} (airborne {ship?.IsAirborne}, {ship?.MaxHealth} HP, at {ship?.Pos.Y:0} m); enemy in the open after {KillStreaks.GunshipWarmup + 4:0.0} s: {(shelled > 0 ? shelled + " HP" : "dead")}; owner can hurt it {ownerHit}; down after {hitsToDown} rifle hits");
        if (!called || ship == null || !ship.IsAirborne || shelled >= World.MaxHealth || ownerHit || hitsToDown is < 20 or >= 200)
            Fail("the gunship should shell enemies in the open and be shot down by enemies (not its owner)");
        Run(0.2);

        if (game.Phase != MatchPhase.Playing)
            Fail($"the match left Playing during the streak scenarios ({game.Phase})");

        // ---- Bots call them --------------------------------------------------------------------------
        Reset();
        game.SelfTestPlace(me, new Vector3(0, 0, 25), 0);
        var bot = b1;
        game.SelfTestPlace(bot, Vector3.Zero, MathF.PI);
        var botSet = new StreakSet(StreakId.BallisticVests, StreakId.SupplyDrop, StreakId.Gunship).Sanitized();
        bot.Streak.Slots = botSet;
        bot.Streak.Ready = (ushort)(StreakState.Bit(StreakId.BallisticVests) | StreakState.Bit(StreakId.SupplyDrop) | StreakState.Bit(StreakId.Gunship));
        for (var i = 0; i < 3; i++)
            game.SelfTestBotStreaks(bot, now + i);
        var botUsedAll = bot.Streak.Ready == 0;
        bot.Streak.Slots = new StreakSet(StreakId.NapalmStrike, StreakId.HunterDrone, StreakId.Juggernaut).Sanitized();
        bot.Streak.Ready = (ushort)(StreakState.Bit(StreakId.NapalmStrike) | StreakState.Bit(StreakId.HunterDrone) | StreakState.Bit(StreakId.Juggernaut));
        for (var i = 0; i < 3; i++)
            game.SelfTestBotStreaks(bot, now + 3 + i);
        var botUsedMore = bot.Streak.Ready == 0;
        bot.Streak.Slots = new StreakSet(StreakId.SmokeScreen, StreakId.EmpBlast, StreakId.None).Sanitized();
        bot.Streak.Ready = (ushort)(StreakState.Bit(StreakId.SmokeScreen) | StreakState.Bit(StreakId.EmpBlast));
        bot.Health = 50; // hurt: it wants cover
        bot.Bot!.LastSeenAt = me.Position;
        bot.Bot.LastSeenTime = now + 6;
        game.SelfTestBotStreaks(bot, now + 6);
        game.SelfTestBotStreaks(bot, now + 6.5);
        var botUsedSmoke = bot.Streak.Ready == 0;
        Line($"bots: used vests/crate/gunship {botUsedAll}, napalm/hunter/juggernaut {botUsedMore}, smoke/EMP {botUsedSmoke}; entities now {game.StreakEntities.Count}");
        if (!botUsedAll || !botUsedMore || !botUsedSmoke)
            Fail("a bot didn't use one of the later streaks");
        game.SelfTestStreakTick(now + 200);
        now += 200;
        Run(0.1);

        // ---- Steady state allocates nothing: a gunship, a hovering hunter drone and burning napalm, nobody to shoot --------
        Reset();
        game.SelfTestPerks(me, new PerkSet(PerkId.None, PerkId.ColdBlooded, PerkId.None));
        game.SelfTestPerks(b2, new PerkSet(PerkId.None, PerkId.ColdBlooded, PerkId.None));
        game.SelfTestPlace(me, new Vector3(-30, 0, 30), 0);
        game.SelfTestPlace(b2, new Vector3(30, 0, 30), 0);
        Use(b1, StreakId.Gunship, b1.Position, 0);
        Use(b1, StreakId.HunterDrone, b1.Position, 0);
        Use(b1, StreakId.NapalmStrike, new Vector3(0, 0, -5), 0);
        Run(KillStreaks.NapalmDelay + 1); // warm up (and the napalm lands)
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 600; i++, now += dt)
        {
            game.SelfTestStreakTick(now);
            game.SelfTestStreakFrame(now, (float)dt);
        }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Line($"steady state: {game.StreakEntities.Count} entities, {game.FireZones.Count} fire patches, {bytes} bytes allocated over 600 ticks");
        if (bytes > 16 * 1024)
            Fail($"the later streaks allocated {bytes} bytes over 600 ticks (should be allocation-free)");
        World.SetMap(Maps.All[0]);
    }
}
