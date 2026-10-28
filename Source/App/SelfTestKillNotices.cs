using System.Numerics;

namespace CallOfSlop;

/// <summary>Self-test section "Kill notices and sentry armour" (see SelfTest.cs).</summary>
public static partial class SelfTest
{
    private static void CheckKillNotices()
    {
        // ---- The notice list on its own: tags, multi-kill chains, fading ----------------------------
        var list = new KillNoticeList();
        list.Add(10, "Ash", Vector3.One, KillKind.Gun, false, 12, "Vanguard AR");
        list.Add(11, "Bob", Vector3.One, KillKind.Gun, true, 12, "Vanguard AR");
        var third = list.Add(12, "Cy", Vector3.One, KillKind.Melee, false, 1, "Melee");
        Line($"chain: {string.Join(" / ", list.Lines.Select(l => $"KILLED {l.Victim} [{l.Tags}]"))}; alpha at +1.0 s {list.Alpha(13):0.00}, +1.9 s {list.Alpha(13.9):0.00}, +2.3 s {list.Alpha(14.3):0.00}");
        if (list.Lines.Count != 3 || list.Lines[0].Tags != "" || list.Lines[1].Tags != "HEADSHOT  DOUBLE KILL" || third.Tags != "MELEE  TRIPLE KILL")
            Fail("multi-kill stacking / tags are wrong");
        if (list.Alpha(13) != 1f || list.Alpha(13.9) is <= 0f or >= 1f || list.Alpha(14.3) != 0f)
            Fail("kill notices must hold, then fade out about 2 s after the last kill");
        list.Add(20, "Dee", Vector3.One, KillKind.Gun, false, 5, "Vanguard AR");
        if (list.Lines.Count != 1 || list.Multi != 1)
            Fail("a kill long after the last one must start a new chain");
        for (var i = 0; i < 6; i++)
            list.Add(20.1 + i * 0.1, $"P{i}", Vector3.One, KillKind.Gun, false, 5, "Vanguard AR");
        if (list.Lines.Count != KillNoticeList.MaxLines || !list.Lines[^1].Tags.Contains("MULTI KILL x7"))
            Fail($"long chain: {list.Lines.Count} lines, last tags '{list.Lines[^1].Tags}'");
        var tagChecks = new[]
        {
            (KillNoticeList.Tags(KillKind.Gun, false, 45.4f, "Raptor", 1, 0, false), "LONGSHOT 45 m"),
            (KillNoticeList.Tags(KillKind.Throwable, false, 45f, "Frag Grenade", 1, 0, false), "FRAG GRENADE"),
            (KillNoticeList.Tags(KillKind.Streak, false, 30f, "Sentry Turret", 1, 0, false), "SENTRY TURRET"),
            (KillNoticeList.Tags(KillKind.Gun, false, 5f, "Raptor", 1, 10, true), "REVENGE  10 KILL STREAK"),
        };
        foreach (var (tags, want) in tagChecks)
            if (tags != want)
                Fail($"tags '{tags}' (want '{want}')");
        if (KillNoticeList.Tags(KillKind.Throwable, false, 45f, "Frag Grenade", 1, 0, false).Contains("LONGSHOT"))
            Fail("only gun kills are longshots");

        // ---- Host: the damage path ----------------------------------------------------------------------
        var flat = MapFile.Load("""
            { "name": "Notice test", "halfX": 60, "halfZ": 60, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] }, { "op": "Spawn", "x": 50, "z": 50 } ] }
            """, "noticetest.json");
        World.SetMap(flat);
        var vanguard = Weapons.Find("Vanguard AR") ?? Weapons.DefaultPrimary;
        using (var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2))
        {
            var me = game.Players[game.LocalId];
            var bots = game.Players.Values.Where(p => p.Bot != null).ToArray();
            Player b1 = bots[0], b2 = bots[1];
            foreach (var p in game.Players.Values)
                game.SelfTestPerks(p, PerkSet.Empty);
            void PlaceAll()
            {
                game.SelfTestPlace(me, Vector3.Zero, 0);
                game.SelfTestPlace(b1, new Vector3(0, 0, 8), 0);
                game.SelfTestPlace(b2, new Vector3(4, 0, 8), 0);
            }
            PlaceAll();
            game.KillNotices.Clear();
            game.DebugKill(me, b1, vanguard.Id, true, 1);
            var first = game.KillNotices.Lines.LastOrDefault();
            game.DebugKill(b1, b2, vanguard.Id, false, 1); // bots killing each other: no notice for us
            var feedLine = game.Feed.LastOrDefault().Text ?? "";
            var afterBots = game.KillNotices.Lines.Count;
            PlaceAll();
            game.DebugKill(me, b1, Game.MeleeKillId, false, 1);
            game.DebugKill(me, b2, (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag), false, 1);
            var melee = game.KillNotices.Lines[^2];
            var frag = game.KillNotices.Lines[^1];
            Line($"host: '{first.Victim}' [{first.Tags}], bot-on-bot feed '{feedLine}' (notices {afterBots}), then [{melee.Tags}] and [{frag.Tags}]");
            if (game.KillNotices.Lines.Count < 3 || first.Victim != b1.Name || !first.Tags.Contains("HEADSHOT") || afterBots != 1)
                Fail("host: the kill notice must name the victim, for the killer only");
            if (first.Color != b1.Color)
                Fail("the notice should be in the victim's colour");
            if (!feedLine.Contains(b1.Name) || !feedLine.Contains(b2.Name))
                Fail("the kill feed must name both players");
            if (!melee.Tags.StartsWith("MELEE") || !melee.Tags.Contains("DOUBLE KILL") || !frag.Tags.Contains("FRAG GRENADE") || !frag.Tags.Contains("TRIPLE KILL"))
                Fail("melee / grenade tags or the multi-kill count are wrong");

            // Streak kills, self-kills, revenge and longshots.
            game.KillNotices.Clear();
            PlaceAll();
            game.DebugKill(me, b1, KillStreaks.KillId(StreakId.MortarBarrage), false, 2);
            var mortar = game.KillNotices.Lines.LastOrDefault();
            game.DebugKill(me, me, (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag), false, 2);
            var afterSelf = game.KillNotices.Lines.Count;
            PlaceAll();
            game.DebugKill(b2, me, vanguard.Id, false, 2);
            PlaceAll();
            game.KillNotices.Clear();
            game.DebugKill(me, b2, vanguard.Id, false, 3);
            var revenge = game.KillNotices.Lines.LastOrDefault();
            game.KillNotices.Clear();
            game.SelfTestPlace(b1, new Vector3(30, 0, 40), 0);
            game.DebugKill(me, b1, vanguard.Id, false, 4);
            var longshot = game.KillNotices.Lines.LastOrDefault();
            Line($"host: mortar [{mortar.Tags}], own grenade added {afterSelf - 1} notice(s), revenge [{revenge.Tags}], 50 m kill [{longshot.Tags}]");
            if (!mortar.Tags.Contains("MORTAR BARRAGE") || afterSelf != 1)
                Fail("streak kill tag missing, or killing yourself made a notice");
            if (!revenge.Tags.Contains("REVENGE") || !longshot.Tags.Contains("LONGSHOT 5"))
                Fail("revenge / longshot tags are wrong");
            foreach (var l in game.KillNotices.Lines)
                if (l.Tags.Any(c => c > 126))
                    Fail($"non-ASCII tag '{l.Tags}'");
        }

        // ---- Client: the replicated kill message ------------------------------------------------------------
        using (var client = new Game(NetPeer.Join("127.0.0.1", 26177), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id))
        {
            client.LocalId = 3;
            client.Players[3] = new Player { Id = 3, Name = "Guest" };
            client.Players[5] = new Player { Id = 5, Name = "Ash" };
            client.Players[6] = new Player { Id = 6, Name = "Bob%s" }; // names come from the network
            var w = new PacketWriter();
            void Receive(KillPacket k)
            {
                k.Write(w);
                client.SelfTestPacket(w.Span.ToArray(), 5);
            }
            Receive(new KillPacket(3, 5, true, vanguard.Id, 0, 100, new Vector3(0, 1.6f, 0)));
            var mine = client.KillNotices.Lines.LastOrDefault();
            var feedMine = client.Feed.LastOrDefault().Text ?? "";
            Receive(new KillPacket(5, 6, false, vanguard.Id, 0, 100, new Vector3(0, 1.6f, 0)));
            var feedOthers = client.Feed.LastOrDefault().Text ?? "";
            Receive(new KillPacket(6, 3, false, vanguard.Id, 0, 100, new Vector3(0, 1.6f, 0)));
            Receive(new KillPacket(3, 6, false, Game.MeleeKillId, 0, 100, new Vector3(0, 1.6f, 0)));
            var second = client.KillNotices.Lines.LastOrDefault();
            Line($"client: notice '{mine.Victim}' [{mine.Tags}], then '{second.Victim}' [{second.Tags}] ({client.KillNotices.Lines.Count} lines); feed '{feedMine}' / '{feedOthers}'");
            if (mine.Victim != "Ash" || !mine.Tags.Contains("HEADSHOT"))
                Fail("client: the kill notice must name the victim");
            if (client.KillNotices.Lines.Count != 2 || second.Victim != "Bob%s" || !second.Tags.Contains("MELEE") || !second.Tags.Contains("REVENGE"))
                Fail("client: other players' kills and our death must not add notices; our next kill must");
            if (!feedMine.Contains("Ash") || !feedOthers.Contains("Ash") || !feedOthers.Contains("Bob%s"))
                Fail("client: the kill feed must name both players");
        }

        // ---- Sentry turret armour ------------------------------------------------------------------------------
        var def = KillStreaks.Get(StreakId.SentryTurret);
        var perBullet = Game.StreakEntityDamage(StreakId.SentryTurret, (int)vanguard.Damage, DamageKind.Bullet);
        var expectShots = (def.Health + perBullet - 1) / perBullet;
        Line($"sentry: {def.Health} HP (was 260), bullets x{def.BulletDamageMult}, explosives x{def.ExplosiveDamageMult}; {vanguard.Name} {vanguard.Damage} dmg -> {perBullet} a hit, {expectShots} hits (was {(260 + (int)vanguard.Damage - 1) / (int)vanguard.Damage}); mag {vanguard.Mag}");
        if (def.Health < 400 || def.BulletDamageMult >= 1f || def.ExplosiveDamageMult <= 1f || expectShots <= vanguard.Mag)
            Fail("the sentry must survive a full assault rifle magazine and take extra damage from explosives");
        if (Game.StreakEntityDamage(StreakId.SentryTurret, 100, DamageKind.Explosive) != 150 || Game.StreakEntityDamage(StreakId.SentryTurret, 100, DamageKind.Bullet) != 60
            || Game.StreakEntityDamage(StreakId.AttackDrone, 100, DamageKind.Explosive) != 100 || Game.StreakEntityDamage(StreakId.SentryTurret, 1, DamageKind.Bullet) != 1)
            Fail("streak entity damage multipliers are wrong");
        var hb = new StreakEntity { Kind = StreakId.SentryTurret }.Hitbox;
        Line($"sentry hitbox: {hb.Max.X - hb.Min.X:0.00} x {hb.Max.Y - hb.Min.Y:0.00} x {hb.Max.Z - hb.Min.Z:0.00} m");
        if (hb.Max.X - hb.Min.X > 1.0f || hb.Max.Y - hb.Min.Y > 1.3f)
            Fail("the sentry's hitbox is bigger than the model");

        using (var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2))
        {
            World.SetMap(flat);
            game.SelfTestStreakTick(0);
            var me = game.Players[game.LocalId];
            var shooter = game.Players.Values.First(p => p.Bot != null);
            foreach (var p in game.Players.Values)
                game.SelfTestPerks(p, PerkSet.Empty);
            shooter.Primary = shooter.Weapon = vanguard.Id;
            shooter.PrimaryKit = 0;
            shooter.BuiltPrimary = null;
            StreakEntity Place(double now)
            {
                game.SelfTestPlace(me, Vector3.Zero, MathF.PI);
                me.Streak.Slots = new StreakSet(StreakId.SentryTurret, StreakId.None, StreakId.None).Sanitized();
                me.Streak.Ready = 0xFF;
                game.SelfTestUseStreak(me, StreakId.SentryTurret, me.Position, MathF.PI, now);
                game.SelfTestPlace(me, new Vector3(-30, 0, -30), 0);
                game.SelfTestPlace(shooter, new Vector3(0, 0, 9), 0);
                return game.StreakEntities.Values.Last();
            }
            var t = Place(10);
            var center = t.Hitbox.Center;
            game.SelfTestShoot(shooter, Vector3.Normalize(center - shooter.Eye), 10.1);
            var bodyHit = t.MaxHealth - t.Health;
            var top = center + new Vector3(0, 0.5f, 0); // the "head" end of the box: no headshot bonus
            game.SelfTestShoot(shooter, Vector3.Normalize(top - shooter.Eye), 10.2);
            var topHit = t.MaxHealth - t.Health - bodyHit;
            var shots = 2;
            while (game.StreakEntities.ContainsKey(t.Id) && shots < 200)
            {
                game.SelfTestShoot(shooter, Vector3.Normalize(center - shooter.Eye), 10.3 + shots * 0.01);
                shots++;
            }
            var t2 = Place(20);
            game.SelfTestDamageStreakEntity(t2, shooter, 100, 20.1, DamageKind.Explosive);
            var blast = t2.MaxHealth - t2.Health;
            game.SelfTestDamageStreakEntity(t2, shooter, 100, 20.2, DamageKind.Bullet);
            var bullet = t2.MaxHealth - t2.Health - blast;
            Line($"sentry in game: body hit {bodyHit}, top-of-box hit {topHit}, destroyed after {shots} {vanguard.Name} hits; 100 explosive -> {blast}, 100 bullet -> {bullet}");
            if (bodyHit != perBullet || topHit != perBullet)
                Fail("sentry bullet damage is wrong (or a headshot multiplier applied)");
            if (shots != expectShots || game.StreakEntities.ContainsKey(t.Id))
                Fail($"the sentry took {shots} hits (expected {expectShots})");
            if (blast != 150 || bullet != 60)
                Fail("sentry explosive / bullet multipliers are wrong in game");
        }
        World.SetMap(Maps.All[0]);
    }
}
