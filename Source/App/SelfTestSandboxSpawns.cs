using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox spawns" (GameSandboxSpawns.cs; docs/sandbox/CONSTRUCTS.md, "Spawning bots and zombies"): the spawner parts'
    /// files (settings, data ports, the Spawn group); friendly and enemy bots take the right sides; a small zombie group
    /// spawns exactly that many; refusals (mid-air, on a construct, a survival world, the caps); clear all; 500,000 zombies
    /// accepted and routed into the mass tier over a few ticks (no long simulation); a Zombie Spawner wired to a switch
    /// spawning only while on, at its rate, up to its max alive and total, the spawn-now input, its alive and spawned
    /// outputs; a Bot Spawner; spawners round-tripping through blueprint links and the construct message; bots turning
    /// round a construct; and over the real transport a client refused until the host allows everyone, then spawning,
    /// seeing its zombies and clearing them. All short, seeded and scripted.
    /// </summary>
    private static void CheckSandboxSpawns()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"spawns: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        SpawnParts();
        SpawnDirect(yard);
        SpawnSpawners(yard);
        SpawnRoundTrip();
        SpawnOverWire(yard);
    }

    private static void SpawnParts()
    {
        var bot = ConstructParts.Find("Bot Spawner");
        var zom = ConstructParts.Find("Zombie Spawner");
        var problems = new List<string>();
        foreach (var d in new[] { bot, zom })
        {
            if (d == null)
            {
                problems.Add("a spawner part is missing");
                continue;
            }
            if (!d.IsSpawner || d.DataIn.Length != 3 || d.DataOut.Length != 2 || d.Group != "Spawn" || !d.HasOutput || d.MaxInputs < 3)
                problems.Add($"{d.Name}: spawner {d.IsSpawner}, ports {d.DataIn.Length}/{d.DataOut.Length}, group \"{d.Group}\"");
            foreach (var k in new[] { CfgKey.Rate, CfgKey.Group, CfgKey.MaxAlive, CfgKey.Total, CfgKey.Radius, CfgKey.StartOn })
                if (!ConstructConfig.Has(d, k))
                    problems.Add($"{d.Name}: no {ConstructConfig.KeyName(k)} setting");
        }
        if (bot != null && !(ConstructConfig.Has(bot, CfgKey.Team) && ConstructConfig.Has(bot, CfgKey.Difficulty) && ConstructConfig.Has(bot, CfgKey.Loadout)))
            problems.Add("the Bot Spawner lacks team, difficulty or gun");
        var port = ConstructRoutes.DataPortByte(2);
        var named = bot != null ? ConstructRoutes.PortName(bot, RouteType.Data, port, true) : "";
        var byName = bot != null ? ConstructRoutes.PortByName(bot, RouteType.Data, "spawn now", true) : (byte)0;
        Line($"spawns parts: {bot?.Config.Length} and {zom?.Config.Length} settings; port {port:x2} is \"{named}\", \"spawn now\" is {byName:x2}; {(problems.Count == 0 ? "ok" : string.Join("; ", problems))}");
        if (problems.Count > 0 || named != "rate" || byName != ConstructRoutes.DataPortByte(1))
            Fail("spawns: the spawner parts' files or data ports are wrong");
    }

    private static Player? SpawnHost(Game g, ref double now, Vector3 at)
    {
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
            return null;
        me.Teleport(at);
        me.ImmuneUntil = now + 1e6; // (zombies come for the host: it mustn't die mid-check)
        return me;
    }

    private static void SpawnDirect(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        var hostAt = ConstructWorld.Ground(World.Query, 10, -14);
        if (SpawnHost(g, ref now, hostAt) is not { } me)
        {
            Fail("spawns: no local player");
            return;
        }
        // Bots: 3 friendly to one side, 3 enemy to the other.
        var a = ConstructWorld.Ground(World.Query, 4, -14);
        var b = ConstructWorld.Ground(World.Query, 16, -14);
        var r1 = g.SandboxSpawnRequest(me, SbSpawnKind.FriendlyBot, BotDifficulty.Normal, SbLoadKind.Class, (byte)WeaponClass.SMG, 0, 3, a);
        var r2 = g.SandboxSpawnRequest(me, SbSpawnKind.EnemyBot, BotDifficulty.Easy, SbLoadKind.Any, 0, 0, 3, b);
        Run(g, ref now, 0.2);
        var friends = g.Players.Values.Where(p => p.SbSpawned && p.Team == Game.SbFriendlyTeam).ToList();
        var foes = g.Players.Values.Where(p => p.SbSpawned && p.Team == Game.SbHostileTeam).ToList();
        var sides = friends.Count == 3 && foes.Count == 3
                    && friends.All(f => !g.AreEnemies(f, me) && foes.All(e => g.AreEnemies(f, e))) && foes.All(e => g.AreEnemies(e, me) && foes.All(o => o == e || !g.AreEnemies(e, o)))
                    && !g.AreEnemies(friends[0], friends[1]);
        var smg = friends.All(f => Weapons.Get(f.Weapon).Class == WeaponClass.SMG);
        // A few seconds: the friendly bots pick enemy bots, never the host.
        var crossTargets = 0;
        var hostTargeted = false;
        for (var t = 0; t < 10; t++)
        {
            Run(g, ref now, 0.5);
            foreach (var f in friends)
                if (f.Bot is { Target: var tg } && tg != byte.MaxValue)
                {
                    hostTargeted |= tg == me.Id;
                    if (g.Players.TryGetValue(tg, out var tp) && tp.Team == Game.SbHostileTeam)
                        crossTargets++;
                }
        }
        Line($"spawns bots: \"{r1 ?? "ok"}\", \"{r2 ?? "ok"}\"; {friends.Count} friendly, {foes.Count} enemy; sides right {sides}; friends with SMGs {smg}; friendly bots aimed at enemy bots {crossTargets} times, at the host {hostTargeted}");
        if (!sides || !smg || hostTargeted || crossTargets == 0)
            Fail("spawns: friendly and enemy bots don't take sides correctly");

        // Refusals.
        var midAir = g.SandboxSpawnRequest(me, SbSpawnKind.Zombies, 0, 0, 0, 0, 5, hostAt + new Vector3(0, 30, 0));
        yard.Sandbox!.InfiniteParts = false;
        var survival = g.SandboxSpawnRequest(me, SbSpawnKind.Zombies, 0, 0, 0, 0, 5, hostAt);
        yard.Sandbox.InfiniteParts = true;
        var cap = g.SandboxBotCap;
        g.SandboxBotCap = g.SandboxBotCount();
        var capped = g.SandboxSpawnRequest(me, SbSpawnKind.EnemyBot, 0, 0, 0, 0, 1, b);
        g.SandboxBotCap = cap;

        // Clear all, then a small zombie group: exactly that many.
        g.SandboxClearAll();
        Run(g, ref now, 0.1);
        var botsLeft = g.Players.Values.Count(p => p.SbSpawned);
        var zAt = ConstructWorld.Ground(World.Query, 10, -4);
        var r3 = g.SandboxSpawnRequest(me, SbSpawnKind.Zombies, 0, 0, 0, 0, 7, zAt);
        Run(g, ref now, 0.5);
        var seven = g.Horde.Count(z => z.Alive);
        var counted = g.SandboxZombieCount();
        g.SandboxClearAll();
        Run(g, ref now, 0.1);
        var gone = g.Horde.Count + g.Crowd.Total;
        Line($"spawns refusals: mid-air \"{midAir}\", survival \"{survival}\", the bot cap \"{capped}\"; clear all left {botsLeft} bots; 7 zombies \"{r3 ?? "ok"}\": {seven} alive ({counted} counted), {gone} after clear all");
        if (midAir == null || !midAir.Contains("mid-air") || survival == null || !survival.Contains("survival") || capped == null || !capped.Contains("cap") || botsLeft != 0 || seven != 7 || counted != 7 || gone != 0)
            Fail("spawns: a refusal, clear all or a small zombie group went wrong");

        // 500,000: accepted, into the mass tier over a few ticks (a short scripted run).
        var r4 = g.SandboxSpawnRequest(me, SbSpawnKind.Zombies, 0, 0, 0, 0, 500000, ConstructWorld.Ground(World.Query, -10, 10));
        var worst = 0.0;
        var ticks = 0;
        const double dt = 1.0 / World.TickRate;
        var sw = new Stopwatch();
        var mem0 = GC.GetTotalMemory(false);
        while (ticks < 60)
        {
            sw.Restart();
            now += dt;
            g.Update(now, dt);
            if (ticks > 0)
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            ticks++;
        }
        var total = g.SandboxZombieCount(queued: false);
        var mass = g.Crowd.MassTotal;
        // A steady second with the half million there (and a few awake near the host): the spawn tick allocates nothing.
        Run(g, ref now, 0.5);
        var bytes0 = GC.GetAllocatedBytesForCurrentThread();
        var crowdMs0 = g.Crowd.TickMs;
        var steady = 0.0;
        for (var k = 0; k < 60; k++)
        {
            sw.Restart();
            g.SandboxSpawnTick(now += dt);
            steady = Math.Max(steady, sw.Elapsed.TotalMilliseconds);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        var crowdMs = (g.Crowd.TickMs - crowdMs0) / 60;
        var mem = (GC.GetTotalMemory(false) - mem0) / (1024 * 1024.0);
        // The cap: lowered, a big ask is cut down to it.
        g.SandboxClearAll();
        g.SandboxZombieCap = 1000;
        var r5 = g.SandboxSpawnRequest(me, SbSpawnKind.Zombies, 0, 0, 0, 0, 5000, zAt);
        Run(g, ref now, 0.3);
        var underCap = g.SandboxZombieCount();
        g.SandboxZombieCap = Game.SpawnZombieMax;
        g.SandboxClearAll();
        Line($"spawns 500k: \"{r4 ?? "ok"}\"; after {ticks} ticks {total:N0} zombies ({mass:N0} in the mass tier, {g.Crowd.Count} particles), worst tick {worst:0.0} ms while spawning, {steady:0.0} ms steady (the crowd's own {crowdMs:0.00} ms a tick, {g.Crowd.FlowCells} flow cells, {g.Crowd.MassCount.Length} mass cells), +{mem:0.0} MB, {allocated} B allocated in 60 spawn ticks; a cap of 1,000: \"{r5}\", {underCap} spawned");
        if (r4 != null || total != 500000 || mass < 400000 || underCap != 1000 || r5 == null || allocated > 0)
            Fail("spawns: 500,000 zombies weren't accepted into the mass tier (or the cap didn't hold)");
        if (OperatingSystem.IsWindows() && (worst > 30 || steady > 20)) // (wall clock: only the Windows runners are quiet enough to judge it)
            Fail($"spawns: a tick with 500,000 zombies took {worst:0.0} ms");

        // Bots and constructs: a friendly bot walking to the host round a construct in the way.
        var wallAt = ConstructWorld.Ground(World.Query, 10, 6);
        var bp = ConstructBuild.FindBlueprint("Wired Buggy");
        var obstacle = bp != null ? ConstructBuild.BuildOnGround(g.Constructs, bp, wallAt, MathF.PI / 2, out _) : null;
        if (obstacle == null)
        {
            Fail("spawns: the obstacle construct didn't build");
            return;
        }
        obstacle.Pinned = true;
        me.Teleport(ConstructWorld.Ground(World.Query, 10, 13));
        var from = ConstructWorld.Ground(World.Query, 10, 0);
        var hit = ConstructSpawners.Obstacle(g.Constructs, from + new Vector3(0, 0, obstacle.WorldCom.Z - from.Z - 1.5f), Vector3.UnitZ, 1.1f, out _, out _);
        var clear = ConstructSpawners.Obstacle(g.Constructs, from, -Vector3.UnitZ, 1.1f, out _, out _);
        g.SandboxSpawnRequest(me, SbSpawnKind.FriendlyBot, BotDifficulty.Normal, 0, 0, 0, 1, from);
        Run(g, ref now, 0.1);
        var follower = g.Players.Values.FirstOrDefault(p => p.SbSpawned);
        var start = follower != null ? Vector3.Distance(follower.Position, me.Position) : 0;
        Run(g, ref now, 8);
        var end = follower is { Alive: true } ? Vector3.Distance(follower.Position, me.Position) : float.MaxValue;
        Line($"spawns obstacles: a box ahead {hit}, none behind {!clear}; a friendly bot to the host past a construct: {start:0.0} m -> {end:0.0} m, {g.SandboxBotDetours} detour ticks, {g.SandboxBotReplans} replans");
        if (!hit || clear || follower == null || end > 6f)
            Fail("spawns: bots don't get round constructs");
    }

    private const string SpawnerBench = """
        { "format": "call-of-slop-construct-blueprint/1", "name": "Spawner Bench",
          "steps": [ { "part": "Foundation" },
                     { "part": "Switch", "on": 0, "socket": "top00", "by": "-y" },
                     { "part": "Zombie Spawner", "on": 0, "socket": "top22", "by": "-y",
                       "config": { "rate": 60, "group": 1, "maxAlive": 3, "total": 5, "radius": 3, "startOn": false } },
                     { "part": "Button", "on": 0, "socket": "top03", "by": "-y" },
                     { "part": "Toggle", "on": 0, "socket": "top30", "by": "-y" } ],
          "links": [ { "type": "data", "from": 1, "to": 2, "toPort": "enabled" },
                     { "type": "data", "from": 3, "to": 2, "toPort": "spawn now" },
                     { "type": "data", "from": 2, "fromPort": "spawned", "to": 4 } ] }
        """;

    private static void SpawnSpawners(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnHost(g, ref now, ConstructWorld.Ground(World.Query, 10, -14)) is not { } me)
        {
            Fail("spawns: no local player");
            return;
        }
        var bp = ConstructBuild.ParseBlueprint(SpawnerBench, "bench.json");
        if (ConstructBuild.BuildOnGround(g.Constructs, bp, ConstructWorld.Ground(World.Query, 10, 4), 0, out var why) is not { } c)
        {
            Fail($"spawns: the spawner bench doesn't build: {why}");
            return;
        }
        var sw = PartNamed(c, "Switch");
        var sp = PartNamed(c, "Zombie Spawner");
        var btn = PartNamed(c, "Button");
        var tog = PartNamed(c, "Toggle");
        var ports = c.Wires.AsSpan(0, c.WireCount).ToArray().Select(w => $"{w.From}.{w.FromPort}->{w.To}.{w.ToPort}").ToArray();
        int Mine() => g.Horde.Count(z => z.Alive && z.SbBody == c.Id);
        // Off: nothing.
        Run(g, ref now, 2);
        var whileOff = Mine();
        // On: one a second (the first at once), up to 3 alive.
        c.Parts[sw].State = 1;
        Run(g, ref now, 0.6);
        var after06 = Mine();
        Run(g, ref now, 1.0);
        var after16 = Mine();
        Run(g, ref now, 2.5);
        var capped = Mine();
        var aliveOut = c.Parts[sp].Out;
        var toggled = c.Parts[tog].State & 1;
        // Some die: it fills up again, only to its total (5).
        g.SandboxClearAll();
        Run(g, ref now, 4);
        var afterTotal = Mine();
        var total = c.Parts[sp].SpawnTotal;
        // Off again, total raised: a spawn-now pulse still spawns one group.
        c.Parts[sw].State = 0;
        Run(g, ref now, 0.2); // (the switch's signal reaches it a tick later)
        ConstructConfig.Set(ref c.Parts[sp], c.DefOf(sp), c.DefOf(sp).CfgSlot[(int)CfgKey.Total], 0);
        g.SandboxClearAll();
        Run(g, ref now, 1);
        var offAgain = Mine();
        c.Parts[btn].Count = 20;
        Run(g, ref now, 0.5);
        var pulsed = Mine();
        Line($"spawns spawner: wires [{string.Join(", ", ports)}]; off {whileOff}; on: {after06} at 0.6 s, {after16} at 1.6 s, {capped} at 4 s (max 3; its alive output {aliveOut}, the spawned pulse toggled a toggle to {toggled}); "
             + $"refilled to its total: {afterTotal} more ({total} in all); off {offAgain}, then spawn now: {pulsed}");
        if (whileOff != 0 || after06 != 1 || after16 != 2 || capped != 3 || aliveOut != 3 || afterTotal != 2 || total != 5 || offAgain != 0 || pulsed != 1)
            Fail("spawns: the zombie spawner doesn't follow its switch, rate, max alive, total or spawn-now input");

        // A Bot Spawner: enemy bots, 2 at most, on from the start.
        g.SandboxClearAll();
        var bd = ConstructParts.Find("Bot Spawner")!;
        var bbp = ConstructBuild.ParseBlueprint("""
            { "name": "Bot Bench", "steps": [ { "part": "Foundation" },
              { "part": "Bot Spawner", "on": 0, "socket": "top11", "by": "-y", "config": { "team": "enemy", "difficulty": "Easy", "loadout": "shotgun", "rate": 120, "maxAlive": 2 } } ] }
            """, "bots.json");
        var bc = ConstructBuild.BuildOnGround(g.Constructs, bbp, ConstructWorld.Ground(World.Query, -8, 4), 0, out why);
        Run(g, ref now, 2);
        var bots = g.Players.Values.Where(p => p.SbSpawned && p.SbBody == bc?.Id).ToList();
        var right = bots.All(p => p.Team == Game.SbHostileTeam && p.Bot!.Skill == BotDifficulty.Easy && Weapons.Get(p.Weapon).Class == WeaponClass.Shotgun);
        Line($"spawns bot spawner: {bots.Count} bots ({why ?? "built"}), enemy Easy shotguns {right}");
        if (bc == null || bots.Count != 2 || !right)
            Fail("spawns: the bot spawner didn't spawn its two enemy bots");
        _ = bd;
    }

    private static void SpawnRoundTrip()
    {
        var w = new ConstructWorld();
        var bp = ConstructBuild.ParseBlueprint(SpawnerBench, "bench.json");
        if (ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 18, 10), 0, out var why) is not { } c)
        {
            Fail($"spawns: the bench doesn't build ({why})");
            return;
        }
        var links = ConstructRoutes.LinksJson(c);
        var sp = PartNamed(c, "Zombie Spawner");
        var cfg = ConstructConfig.ToJson(c.Parts[sp], c.DefOf(sp));
        // Written back as a blueprint and built again: the same ports and settings.
        var again = SpawnerBench[..SpawnerBench.IndexOf("\"links\"", StringComparison.Ordinal)] + "\"links\": " + links + " }";
        var c2 = ConstructBuild.BuildOnGround(w, ConstructBuild.ParseBlueprint(again, "again.json"), ConstructWorld.Ground(World.Query, 24, 10), 0, out why);
        static string Wires(Construct k) => string.Join(",", k.Wires.AsSpan(0, k.WireCount).ToArray().Select(x => $"{x.From}{x.FromPort}{x.To}{x.ToPort}").Order());
        var same = c2 != null && Wires(c2) == Wires(c) && ConstructConfig.ToJson(c2.Parts[sp], c2.DefOf(sp)) == cfg;
        // The construct message.
        var pw = new PacketWriter(8192);
        ConstructNet.WriteFull(pw, c);
        var client = new ConstructWorld();
        var r = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref r, client, 0);
        var copy = client.Get(c.Id);
        copy?.Rebuild();
        var net = copy != null && Wires(copy) == Wires(c) && ConstructConfig.ToJson(copy.Parts[sp], copy.DefOf(sp)) == cfg;
        Line($"spawns round trip: links {links}; settings {cfg}; blueprint again the same {same} ({why ?? "built"}); construct message the same {net}");
        if (!links.Contains("\"toPort\":\"spawn now\"") || !links.Contains("\"fromPort\":\"spawned\"") || !same || !net)
            Fail("spawns: spawners don't round-trip through blueprints and the construct message");
    }

    private static void SpawnOverWire(Map yard)
    {
        const ushort port = 26351;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("spawns over the wire: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        var at = ConstructWorld.Ground(World.Query, 10, -14);
        sp.Teleport(at);
        sp.ImmuneUntil = now + 1e6;
        Pump(server, [a], 0.3, ref now, clock);
        var spot = ConstructWorld.Ground(World.Query, 10, -6);
        a.SandboxSendSpawn(SbSpawnKind.Zombies, 0, 0, 0, 0, 5, spot);
        Pump(server, [a], 0.4, ref now, clock);
        var refused = a.SandboxNotice;
        var none = server.Horde.Count;
        server.SandboxSpawnAnyone = true;
        a.SandboxSendSpawn(SbSpawnKind.Zombies, 0, 0, 0, 0, 5, spot);
        Pump(server, [a], 1.0, ref now, clock);
        var onServer = server.Horde.Count(z => z.Alive);
        var onClient = a.Horde.Count(z => z.Alive);
        a.SandboxSendSpawn(SbSpawnKind.FriendlyBot, BotDifficulty.Normal, 0, 0, 0, 1, at);
        Pump(server, [a], 0.6, ref now, clock);
        var friendOnClient = a.Players.Values.Count(p => p.Team == Game.SbFriendlyTeam);
        a.SandboxSendClearSpawns();
        Pump(server, [a], 0.4, ref now, clock);
        var cleared = server.Horde.Count + server.Players.Values.Count(p => p.SbSpawned);
        Line($"spawns over the wire: before the host allows it \"{refused}\" ({none} zombies); then 5 zombies on the server {onServer}, on the client {onClient}; the friendly bot on the client {friendOnClient}; after the client's clear all {cleared}");
        if (!refused.Contains("only the host") || none != 0 || onServer != 5 || onClient != 5 || friendOnClient != 1 || cleared != 0)
            Fail("spawns: a client's spawns didn't go over the wire (or weren't refused before the host allowed them)");
    }
}
