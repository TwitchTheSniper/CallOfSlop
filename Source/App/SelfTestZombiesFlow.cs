using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies flow" (ZombiesFlow.cs, ZombieBots.cs; user requests 2026-10-23): a downed player crawls
/// (slowly, prone, no jump / stand / buy / workbench), a revive is interrupted by crawling out of reach and finishes when
/// they crawl back; a downed bot crawls to its teammate; a dead player spectates only living teammates and cycles through
/// them; the next round waits for ready-up (bots count as ready, the dead don't block), the fallback timer and the host's
/// force start; and the dead come back when the round's last zombie dies, before the break.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesFlow()
    {
        if (ZTestMap is not { Zombies: { } zm } map)
        {
            Fail("Zombies flow: Maps/Dev/ZombiesTest.json is missing");
            return;
        }
        var now = 0.0;

        // An open line on the floor near (6, 0, 3) in the start room: the yaw with the most free floor ahead.
        static float OpenYaw(Vector3 at)
        {
            var best = 0f;
            var bestFree = -1f;
            for (var i = 0; i < 16; i++)
            {
                var yaw = i * MathF.Tau / 16;
                var free = World.Query.Raycast(at + new Vector3(0, 0.3f, 0), World.FlatForward(yaw), 6f, QueryFilter.All);
                if (free > bestFree + 0.01f)
                {
                    bestFree = free;
                    best = yaw;
                }
            }
            return best;
        }

        // ---- Crawling: slow, prone, no jumping or standing; no buying, no workbench ----
        using (var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BleedoutSeconds = 120; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var mate = g.SelfTestAddHuman();
            g.SelfTestSpawn(mate);
            mate.ImmuneUntil = 1e9;
            var start = new Vector3(6, 0, 3);
            me.Teleport(start);
            mate.Teleport(start + new Vector3(-4, 0, 0));
            g.SelfTestDown(me, now);
            g.LookYaw = OpenYaw(start);
            g.LookPitch = 0;
            Run(g, ref now, 0.5); // lie down
            var from = me.Position;
            g.HeldButtons = Buttons.Forward | Buttons.Sprint | Buttons.Jump;
            var topY = from.Y;
            var standingTicks = 0;
            const double crawlSeconds = 2.0;
            for (var t = 0.0; t < crawlSeconds; t += World.TickDt)
            {
                now += World.TickDt;
                g.Update(now, World.TickDt);
                topY = MathF.Max(topY, me.Position.Y);
                if (me.Sim.Stance != Stance.Prone)
                    standingTicks++;
            }
            g.HeldButtons = Buttons.None;
            var crawled = Flat2(me.Position - from);
            var speed = crawled / (float)crawlSeconds;
            var prone = World.WalkSpeed * World.SpeedOf(Stance.Prone);
            // Nothing to buy or use while down: next to the start room's workbench and a wall buy, the prompt is empty and the bench refuses.
            me.Teleport(zm.Benches[0].At + new Vector3(0, 0, -1.2f));
            var benchUse = g.FindZUse(me);
            var bench = g.ServerBench(me, 1, 0, "Hot Load Kit");
            var wb = zm.WallBuys[0];
            me.Teleport(wb.At with { Y = 0 } + World.FlatForward(wb.Yaw) * 0.7f);
            var buyUse = g.FindZUse(me);
            var pts = g.ZP(me).Points;
            g.ServerSetUse(me, true, now - 1);
            ZTicks(g, ref now, 0.5);
            g.ServerSetUse(me, false, now);
            var bought = g.ZP(me).Points != pts || Game.OwnsBuild(me, wb.Build) >= 0 && wb.Build != "Wall Rifle";
            // Nor revive a teammate who's down too.
            g.SelfTestDown(mate, now);
            me.Teleport(mate.Position + new Vector3(0.8f, 0, 0));
            var reviveUse = g.FindZUse(me);
            Line($"crawl: {crawled:0.00} m in {crawlSeconds:0.0} s ({speed:0.00} m/s; prone {prone:0.00}, rules {g.ZRules.CrawlSpeed:0.00} x prone) holding forward + sprint + jump; "
                  + $"off prone {standingTicks} ticks, rose {topY - from.Y:0.00} m; down at a bench: prompt '{benchUse?.Text}', bench '{bench}'; at a wall buy '{buyUse?.Text}', bought {bought}; revive prompt '{reviveUse?.Text}'");
            if (speed < prone * 0.5f || speed > prone * 1.0f)
                Fail($"a downed player should crawl at about {g.ZRules.CrawlSpeed} x the prone speed ({prone * g.ZRules.CrawlSpeed:0.00} m/s), crawled {speed:0.00} m/s");
            if (standingTicks > 0 || topY - from.Y > 0.05f)
                Fail("a downed player stood up or jumped while crawling");
            if (benchUse != null || bench == null || buyUse != null || bought || reviveUse != null)
                Fail("a downed player could use the workbench, buy or revive");
        }

        // ---- A revive is interrupted by crawling out of reach, and finishes once they crawl back ----
        using (var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BleedoutSeconds = 120; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var mate = g.SelfTestAddHuman();
            g.SelfTestSpawn(mate);
            mate.ImmuneUntil = 1e9;
            var start = new Vector3(6, 0, 3);
            me.Teleport(start);
            var yaw = OpenYaw(start);
            mate.Teleport(start - World.FlatForward(yaw) * 0.8f);
            g.SelfTestDown(me, now);
            g.LookYaw = yaw;
            Run(g, ref now, 0.3);
            g.ServerSetUse(mate, true, now);
            Run(g, ref now, 0.6);
            var progressed = g.ZP(mate).ReviveProgress;
            var shown = g.ZP(me).BeingRevived;
            g.HeldButtons = Buttons.Forward;
            Run(g, ref now, 2.2); // out of reach (1.8 m) after about a second
            g.HeldButtons = Buttons.None;
            var gap = Flat2(me.Position - mate.Position);
            Run(g, ref now, 1.5); // longer than the whole revive: it must not have gone on
            var interrupted = g.IsDowned(me) && g.ZP(mate).ReviveProgress == 0 && g.ZP(me).BeingRevived == 0;
            g.LookYaw = World.WrapAngle(yaw + MathF.PI);
            g.HeldButtons = Buttons.Forward;
            for (var i = 0; i < 60 * 4 && Flat2(me.Position - mate.Position) > 0.9f; i++)
                Run(g, ref now, World.TickDt);
            g.HeldButtons = Buttons.None;
            Run(g, ref now, g.ZRules.ReviveSeconds + 0.4);
            g.ServerSetUse(mate, false, now);
            var revived = !g.IsDowned(me) && me.Alive;
            Line($"revive vs crawling: {progressed * 100:0}% after 0.6 s (shown {shown * 100:0}%), crawled to {gap:0.00} m -> interrupted {interrupted}; crawled back -> revived {revived}");
            if (progressed <= 0 || shown <= 0 || gap <= g.ZRules.ReviveRange || !interrupted)
                Fail("crawling out of the reviver's reach should interrupt the revive (and reset its bar)");
            if (!revived)
                Fail("crawling back into reach should let the revive finish");
        }

        // ---- A downed bot crawls to the nearest teammate standing ----
        using (var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BleedoutSeconds = 120; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var bot = g.Players.Values.First(p => p.Bot != null);
            bot.ImmuneUntil = 1e9;
            var start = new Vector3(6, 0, 3);
            var yaw = OpenYaw(start);
            me.Teleport(start + World.FlatForward(yaw) * 3.5f);
            bot.Teleport(start);
            g.SelfTestDown(bot, now);
            var d0 = Flat2(bot.Position - me.Position);
            Run(g, ref now, 4);
            var d1 = Flat2(bot.Position - me.Position);
            Line($"downed bot: {d0:0.00} m from its teammate, {d1:0.00} m after 4 s (still down {g.IsDowned(bot)})");
            if (!g.IsDowned(bot) || d1 > d0 - 1.5f && d1 > g.ZRules.ReviveRange * 0.7f)
                Fail("a downed bot should crawl towards its nearest teammate standing");
        }

        // ---- Spectating: only living teammates, cycling forward and back by id ----
        using (var g = ZGame(3, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            var bots = g.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToList();
            me.Alive = false;
            bots[1].Alive = false; // a dead teammate is never watched
            var first = ZSpectate.Pick(g, byte.MaxValue, 0);
            var next = ZSpectate.Pick(g, first, 1);
            var wrap = ZSpectate.Pick(g, next, 1);
            var back = ZSpectate.Pick(g, first, -1);
            var keep = ZSpectate.Pick(g, next, 0);
            bots[0].Alive = false; // the one being watched dies: on to the next living one
            var moved = ZSpectate.Pick(g, bots[0].Id, 0);
            bots[2].Alive = false;
            var nobody = ZSpectate.Pick(g, moved, 1);
            var alive = new[] { bots[0].Id, bots[2].Id };
            // The camera: behind, up and right of the eye in the open; pulled in (never through) a wall right behind.
            var eye = new Vector3(0, 1.6f, 0);
            var open = ZSpectate.ShoulderEye(eye, 0, 0, (o, d, max) => max);
            var walled = ZSpectate.ShoulderEye(eye, 0, 0, (o, d, max) => d.Z > 0.5f ? 0.6f : max); // a wall 0.6 m behind (yaw 0 looks along -Z)
            Line($"spectate: first {first}, next {next}, wraps to {wrap}, back from first {back}, kept {keep}; watched one dies -> {moved}; nobody left -> {(nobody == byte.MaxValue ? "nobody" : nobody.ToString())}; "
                  + $"camera {open.X:0.00},{open.Y:0.00},{open.Z:0.00} in the open, {walled.Z:0.00} m back against a wall");
            if (first != bots[0].Id || next != bots[2].Id || wrap != bots[0].Id || back != bots[2].Id || keep != bots[2].Id || moved != bots[2].Id || nobody != byte.MaxValue)
                Fail($"spectating should cycle through the living teammates only (alive {string.Join(",", alive)})");
            if (open.Z < 1.5f || open.Y <= eye.Y || open.X <= 0.2f || walled.Z > 0.45f || walled.Z < 0)
                Fail("the spectator camera should sit over the shoulder and stay in front of walls");
        }

        // ---- Ready-up: the next round waits for every living person; bots count as ready ----
        using (var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.ReadyFallbackSeconds = 0; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var mate = g.SelfTestAddHuman();
            g.SelfTestSpawn(mate);
            mate.ImmuneUntil = 1e9;
            g.SelfTestBeginRound(1, now);
            g.SelfTestEndRound(now);
            ZTicks(g, ref now, g.ZRules.BreakSeconds + 5);
            var waited = g.ZPhase == ZRoundPhase.Break && g.ZWaitingReady && g.ZRound == 1;
            var pressed = g.ZToggleRoundReady(); // the host's Ready key
            ZTicks(g, ref now, 1);
            var (r1, n1) = g.ZReadyCount;
            var oneReady = g.ZWaitingReady && g.ZP(me).RoundReady;
            var tag = g.Module.ScoreboardTag(g, mate) + "/" + g.Module.ScoreboardTag(g, me);
            // A client's copy shows the same ready list from ZWorld.
            var w = new PacketWriter();
            g.WriteZWorld(w);
            var world = w.Span.ToArray();
            var ents = new PacketWriter();
            g.WriteZEnts(ents);
            var entsBytes = ents.Span.ToArray();
            string clientSees;
            using (var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
            {
                c.SelfTestReadZombies(entsBytes, world);
                var cm = c.ZP(mate.Id);
                var ch = c.ZP(me.Id);
                var cb = c.ZP(g.Players.Values.First(p => p.Bot != null).Id);
                clientSees = $"mate {cm.ReadyVoter}/{cm.RoundReady}, host {ch.ReadyVoter}/{ch.RoundReady}, bot {cb.ReadyVoter}, waiting {c.ZWaitingReady}";
            }
            World.SetMap(map);
            g.SelfTestRoundReady(mate, true);
            ZTicks(g, ref now, g.ZRules.ReadyGoSeconds - 0.2);
            var countdown = g.ZPhase == ZRoundPhase.Break && !g.ZWaitingReady;
            ZTicks(g, ref now, 0.4);
            var started = g.ZPhase == ZRoundPhase.Round && g.ZRound == 2 && g.ZBannerText == "ROUND 2";
            Line($"ready-up: still waiting after {g.ZRules.BreakSeconds + 5:0} s {waited}; host's key {pressed} -> {r1}/{n1} ready (bot not counted), tags '{tag}', a client sees {clientSees}; "
                  + $"all ready -> countdown {countdown}, round 2 after {g.ZRules.ReadyGoSeconds:0} s with its banner {started}; {world.Length} bytes of ZWorld");
            if (!waited || !pressed || !oneReady || r1 != 1 || n1 != 2 || tag != " [not ready]/ [READY]" || clientSees != "mate True/False, host True/True, bot False, waiting True")
                Fail("the break should wait for every living person's Ready (bots count as ready), and clients should see who's ready");
            if (!countdown || !started)
                Fail("once everyone is ready the next round should start after the short countdown, with its banner");
        }

        // ---- The fallback timer (one player never readies), the host's force start, and the dead don't block ----
        using (var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.ReadyFallbackSeconds = 20; r.BleedoutSeconds = 2; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var mate = g.SelfTestAddHuman();
            g.SelfTestSpawn(mate);
            mate.ImmuneUntil = 1e9;
            g.SelfTestBeginRound(1, now);
            g.SelfTestEndRound(now);
            g.SelfTestRoundReady(me, true);
            ZTicks(g, ref now, 19.8);
            var stillWaiting = g.ZWaitingReady && g.ZPhaseLeft is > 0 and < 1;
            ZTicks(g, ref now, 0.3 + g.ZRules.ReadyGoSeconds);
            var fallback = g.ZPhase == ZRoundPhase.Round && g.ZRound == 2;
            // Force start: nobody ready.
            g.SelfTestEndRound(now);
            ZTicks(g, ref now, 2);
            var waitingAgain = g.ZWaitingReady;
            g.ZForceRound();
            ZTicks(g, ref now, g.ZRules.ReadyGoSeconds + 0.2);
            var forced = g.ZPhase == ZRoundPhase.Round && g.ZRound == 3;
            // The dead don't block: the mate bleeds out during the break, the host's Ready alone starts the round.
            g.SelfTestEndRound(now);
            g.SelfTestDown(mate, now);
            ZTicks(g, ref now, g.ZRules.BleedoutSeconds + 0.3);
            var dead = !mate.Alive;
            g.SelfTestRoundReady(me, true);
            ZTicks(g, ref now, g.ZRules.ReadyGoSeconds + 0.2);
            var deadDontBlock = g.ZPhase == ZRoundPhase.Round && g.ZRound == 4;
            Line($"ready-up fallback: waiting at 19.8 s {stillWaiting}, started after 20 s {fallback}; host force start {waitingAgain} -> {forced}; teammate bled out in the break {dead} -> host alone starts it {deadDontBlock}");
            if (!stillWaiting || !fallback)
                Fail("the ready-up fallback should start the round when it runs out (and not before)");
            if (!waitingAgain || !forced)
                Fail("the host should be able to force the next round's start");
            if (!dead || !deadDontBlock)
                Fail("a dead player shouldn't block the ready-up");
        }

        // ---- The dead come back when the round's last zombie dies, before the break ----
        using (var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BleedoutSeconds = 2; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var mate = g.SelfTestAddHuman();
            g.SelfTestSpawn(mate);
            mate.ImmuneUntil = 1e9;
            mate.Teleport(new Vector3(6, 0, 3));
            g.SelfTestBeginRound(1, now);
            g.SelfTestDown(mate, now);
            Run(g, ref now, g.ZRules.BleedoutSeconds + 0.5);
            var bled = !mate.Alive;
            Run(g, ref now, 3);
            var stillOut = !mate.Alive && g.ZPhase == ZRoundPhase.Round;
            // The round's last zombie dies (killed through the normal path).
            foreach (var z in g.Horde.ToList())
                g.DamageZombie(me, z, 100000, false, false, now);
            for (var i = 0; i < 60 * 30 && g.ZPhase == ZRoundPhase.Round; i++)
            {
                Run(g, ref now, World.TickDt);
                foreach (var z in g.Horde.ToList())
                    if (z.State != ZState.Dead)
                        g.DamageZombie(me, z, 100000, false, false, now);
            }
            var ended = g.ZPhase == ZRoundPhase.Break;
            Run(g, ref now, 0.2);
            var zp = g.ZP(mate);
            var back = mate.Alive && !g.IsDowned(mate) && g.ZPhase == ZRoundPhase.Break && g.ZWaitingReady && g.ZRound == 1;
            var gear = zp.Slots[0] != null && StandardBuilds.From("start").Any(b => b.Name == zp.Slots[0]!.Name) && !zp.NeedsStartGear;
            var inStart = zm.Areas[zm.StartArea].Contains(mate.Position);
            Line($"respawn at the round's end: bled out {bled}, still out mid-round {stillOut}; round over {ended} -> back {back} with the start gear {gear} ({zp.Slots[0]?.Name}) in the start area {inStart}, voter {zp.ReadyVoter}");
            if (!bled || !stillOut || !ended || !back || !gear || !inStart)
                Fail("a player who bled out should come back with the start gear when the round's last zombie dies, before the break");
        }

        // ---- Player-visible text is plain ASCII ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var texts = new List<string> { Ui.ReadyPromptText(g, "F3"), Keybinds.Label(GameAction.SpectateNext), Keybinds.Label(GameAction.SpectatePrev), Keybinds.Label(GameAction.Ready) };
            g.ZSpectateId = g.LocalId;
            texts.Add(Ui.SpectateText(g, new Settings()));
            foreach (var s in texts)
                if (s.Any(ch => ch < 32 || ch > 126))
                    Fail($"Zombies flow text isn't plain ASCII: {s}");
            Line($"text: '{texts[0]}', '{texts[^1]}'");
        }
    }
}
