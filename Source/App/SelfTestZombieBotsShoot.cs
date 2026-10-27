using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie bots shoot" (developer bug, 2026-10-27: "bots can't shoot guns in the zombie game mode").
/// The cause: a real match starts with a warmup, and the warmup's end respawns everyone; Spawn put each bot's freshly
/// drawn multiplayer loadout ids back in Primary/Secondary while the built Zombies guns stayed in BuiltPrimary and
/// BuiltSecondary, so the gun in a bot's hands had an id the bot didn't own and the server dropped every shot
/// (ZombiesMode.cs, KeepBuiltGuns). Short seeded scenarios on Zombies Test, through the warmup like a real match: each
/// co-op bot fires, hits and kills zombies with its start gun, after a reload, and with a wall-bought (one bot) or a
/// mystery box gun (the other); then a loopback host and client: the client hears and sees the bots' shots.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombieBotsShoot()
    {
        var now = 0.0;
        if (ZTestMap is not { Zombies: { } zm } map)
        {
            Fail("Zombies Test is missing");
            return;
        }
        try
        {
            ZBotsShootOffline(map, zm, ref now);
            ZBotsShootLoopback(map);
        }
        finally
        {
            World.SetMap(Maps.Get(0));
        }
    }

    /// <summary>Spots in Zombies Test's start room (the AI cost check's grid).</summary>
    private static Vector3 ZStartRoomSpot(int i) => new(-7f + (i % 6) * 2.4f, 0, new[] { -5f, -3.6f, 2.5f, 4.5f }[i / 6 % 4]);

    /// <summary>
    /// A zombie 4-7 m from <paramref name="bot"/> in the start room, chasing (the n-th such spot, so it's deterministic).
    /// </summary>
    private static void ZSpawnNear(Game g, ZombiesMap zm, Player bot, int n, double now)
    {
        var spots = new List<Vector3>();
        for (var i = 0; i < 24; i++)
        {
            var d = ZFlat(ZStartRoomSpot(i) - bot.Position);
            if (d is >= 4f and <= 7f)
                spots.Add(ZStartRoomSpot(i));
        }
        var z = g.SelfTestSpawnZombie(zm.Spawners.FindIndex(s => s.Window == 0), now);
        z.State = ZState.Chase;
        z.Window = -1;
        z.Area = zm.StartArea;
        z.Sim.Position = spots.Count > 0 ? spots[n % spots.Count] : bot.Position + new Vector3(5, 0, 0);
    }

    private sealed class ZFightStats
    {
        public int Shots, Kills, Reloads, KillsAfterReload;
        public string Gun = "";
        public bool Owned = true;
    }

    /// <summary>
    /// <paramref name="seconds"/> of a round with a zombie appearing near each bot every 0.7 s (at most 6 alive): each
    /// bot's trigger pulls the server accepted, kills, finished reloads and kills after one.
    /// </summary>
    private static List<ZFightStats> ZBotFight(Game g, ZombiesMap zm, List<Player> bots, double seconds, ref double now)
    {
        var stats = bots.Select(b => new ZFightStats { Gun = b.WeaponDef.Name }).ToList();
        var lastShot = bots.Select(b => b.LastShotTime).ToArray();
        var kills = bots.Select(b => g.ZP(b).Kills).ToArray();
        var reloading = bots.Select(b => b.Bot!.ReloadUntil > 0).ToArray();
        var n = 0;
        var ticks = (int)(seconds * World.TickRate);
        for (var t = 0; t < ticks; t++)
        {
            if (t % 42 == 0)
                foreach (var b in bots)
                    if (g.AliveZombies < 6)
                        ZSpawnNear(g, zm, b, n++, now);
            Run(g, ref now, 1.0 / World.TickRate);
            for (var i = 0; i < bots.Count; i++)
            {
                var b = bots[i];
                var s = stats[i];
                s.Owned &= b.Owns(b.WeaponDef.Id) && b.WeaponDef.Name == s.Gun;
                if (b.LastShotTime != lastShot[i])
                {
                    s.Shots++;
                    lastShot[i] = b.LastShotTime;
                }
                var k = g.ZP(b).Kills;
                if (k > kills[i])
                {
                    s.Kills += k - kills[i];
                    if (s.Reloads > 0)
                        s.KillsAfterReload += k - kills[i];
                    kills[i] = k;
                }
                var r = b.Bot!.ReloadUntil > 0;
                if (reloading[i] && !r)
                    s.Reloads++;
                reloading[i] = r;
            }
        }
        return stats;
    }

    private static string ZFightLine(List<Player> bots, List<ZFightStats> s) =>
        string.Join("; ", bots.Select((b, i) => $"{b.Name} {s[i].Gun}: {s[i].Shots} shots, {s[i].Kills} kills, {s[i].Reloads} reloads ({s[i].KillsAfterReload} kills after){(s[i].Owned ? "" : ", NOT OWNED")}"));

    private static void ZBotsShootOffline(Map map, ZombiesMap zm, ref double now)
    {
        // A match like a real one: the warmup first (everyone spawned with the start gear), then the countdown respawns everyone.
        World.SetMap(map);
        using var g = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2, mapId: map.Id)
        {
            MapVoteEnabled = false,
            WarmupEnabled = true,
        };
        g.ZRules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        g.ZRules.FirstRoundDelay = 9999;
        g.ZRules.BotsBuy = false; // (this is about shooting: they stay at their posts)
        g.ZSeed = 4321;
        g.Mode = GameMode.Zombies;
        Run(g, ref now, 1.0);
        var warmup = g.Phase;
        g.ForceStart();
        Run(g, ref now, Game.CountdownSeconds + 1.0);
        var bots = g.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToList();
        foreach (var p in g.Players.Values)
            p.ImmuneUntil = 1e9;
        var held = string.Join(", ", bots.Select(b => $"{b.Name} holds {b.WeaponDef.Name} (id {b.WeaponDef.Id}, slots {b.Primary}/{b.Secondary}, owned {b.Owns(b.WeaponDef.Id)})"));
        Line($"after the warmup ({warmup}) -> {g.Phase}: {held}");
        if (warmup != MatchPhase.Warmup || g.Phase != MatchPhase.Playing || bots.Count != 2)
        {
            Fail($"the Zombies match didn't go warmup -> playing with two bots ({warmup} -> {g.Phase}, {bots.Count} bots)");
            return;
        }
        if (bots.Any(b => !b.Owns(b.WeaponDef.Id) || b.BuiltPrimary == null || b.Weapon != b.BuiltPrimary.Id))
            Fail($"after the warmup a bot holds a gun it doesn't own (the server drops its shots): {held}");
        g.SelfTestBeginRound(1, now);

        // (a) The start gun.
        var a = ZBotFight(g, zm, bots, 5, ref now);
        Line($"start gun: {ZFightLine(bots, a)}");
        if (a.Any(s => s.Shots == 0 || s.Kills == 0 || !s.Owned))
            Fail($"a co-op bot didn't shoot and kill zombies with its start gun: {ZFightLine(bots, a)}");

        // (c) After a reload: two rounds left, so each bot reloads within the first second.
        foreach (var b in bots)
            b.Bot!.Ammo = Math.Min(b.Bot.Ammo, 2);
        var c = ZBotFight(g, zm, bots, 6, ref now);
        Line($"after a reload: {ZFightLine(bots, c)}");
        if (c.Any(s => s.Reloads == 0 || s.KillsAfterReload == 0 || !s.Owned))
            Fail($"a co-op bot didn't reload and go on killing zombies: {ZFightLine(bots, c)}");

        // (b) A wall gun bought at the chalk outline (the first bot) and a mystery box gun (the second).
        var buyer = bots[0];
        var smg = zm.WallBuys.First(w => w.Build == "Wall SMG");
        var post = buyer.Position;
        buyer.Teleport(smg.At with { Y = 0 } - World.FlatForward(smg.Yaw) * -0.7f);
        g.ZP(buyer).Points = 5000;
        g.SelfTestUse(buyer, now);
        buyer.Teleport(post);
        var boxer = bots[1];
        var all = StandardBuilds.All;
        var pick = -1;
        for (var seed = 1u; seed < 200 && pick < 0; seed++)
        {
            var i = g.RollBoxBuild(boxer, 1, seed * 2654435761u);
            if (i >= 0 && WeaponBuilder.Assemble(all[i].Build()).Def is { IsLauncher: false })
                pick = i;
        }
        var boxed = pick >= 0 && g.GiveBuild(boxer, Game.TargetSlot(boxer), all[pick].Build(), full: true); // what taking the box's offer does
        var bought = buyer.WeaponDef.Name;
        Line($"bought: {buyer.Name} at the Wall SMG outline -> {bought} (points {g.ZP(buyer).Points}); {boxer.Name} from the box -> {(boxed ? boxer.WeaponDef.Name : "nothing")}");
        if (bought != "Wall SMG" || !boxed)
            Fail("the wall buy or the box gun didn't reach the bots' hands");
        var b2 = ZBotFight(g, zm, bots, 5, ref now);
        Line($"bought guns: {ZFightLine(bots, b2)}");
        if (b2.Any(s => s.Shots == 0 || s.Kills == 0 || !s.Owned))
            Fail($"a co-op bot didn't shoot and kill zombies with a bought gun: {ZFightLine(bots, b2)}");
    }

    /// <summary>A loopback host (2 bots) and a client through the lobby, warmup and countdown: the client gets the bots' shots.</summary>
    private static void ZBotsShootLoopback(Map map)
    {
        const ushort port = 60261;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2, mapId: map.Id)
        {
            MapVoteEnabled = false,
            LobbyEnabled = true,
            WarmupEnabled = true,
        };
        server.ZRules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        server.ZRules.FirstRoundDelay = 9999;
        server.ZRules.BotsBuy = false;
        server.ZSeed = 99;
        server.Mode = GameMode.Zombies;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + 0.5, ref now);
        server.ForceStart();
        PumpFast(server, [a], Game.CountdownSeconds + 0.7, ref now);
        var bots = server.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToList();
        if (server.Phase != MatchPhase.Playing || a.Phase != MatchPhase.Playing || bots.Count != 2 || World.Map.Zombies is not { } zm)
        {
            Fail($"the loopback Zombies match didn't start (server {server.Phase}, client {a.Phase}, {bots.Count} bots)");
            return;
        }
        foreach (var p in server.Players.Values)
            p.ImmuneUntil = 1e9;
        server.SelfTestBeginRound(1, now);
        var sent = new int[bots.Count];
        var lastShot = bots.Select(b => b.LastShotTime).ToArray();
        var seen0 = bots.Select(b => a.ShotsSeen[b.Id]).ToArray();
        var n = 0;
        const double dt = 0.05;
        for (var t = 0.0; t < 5; t += dt)
        {
            if ((int)(t / dt) % 14 == 0)
                foreach (var b in bots)
                    if (server.AliveZombies < 6)
                        ZSpawnNear(server, zm, b, n++, now);
            now += dt;
            server.Update(now, dt);
            a.Update(now, dt);
            for (var i = 0; i < bots.Count; i++)
                if (bots[i].LastShotTime != lastShot[i])
                {
                    sent[i]++;
                    lastShot[i] = bots[i].LastShotTime;
                }
            Thread.Sleep(2);
        }
        Pump(server, [a], 0.3, ref now, clock); // the last shots arrive
        var seen = bots.Select((b, i) => a.ShotsSeen[b.Id] - seen0[i]).ToArray();
        var kills = bots.Select(b => server.ZP(b).Kills).ToArray();
        Line($"loopback: {string.Join("; ", bots.Select((b, i) => $"{b.Name} {b.WeaponDef.Name}: server shots {sent[i]}+ (one or more per update), client got {seen[i]} Shot messages, kills {kills[i]}"))}");
        if (seen.Any(s => s == 0) || kills.Any(k => k == 0))
            Fail("a client didn't get a co-op bot's shots (or the bots didn't kill anything) in a hosted Zombies match");
    }
}
