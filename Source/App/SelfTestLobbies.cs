using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Big lobbies (up to 32 players): per-map limits and spawns, the lobby's slider cap and random map choice, snapshots
/// (32-player round trip, quantisation, splitting, every packet under the MTU), join refusal and bots fitting the
/// map, real loopback games at 8 / 16 / 32 players with their bandwidth, and 32-player bot matches within the tick budget.
/// </summary>
public static partial class SelfTest
{
    private static void CheckBigLobbies()
    {
        CheckMapLimits();
        CheckLobbySlider();
        CheckSnapshotCodec();
        CheckBotsFitMap();
        CheckJoinRefusal();
        CheckLoopbackLobbies();
        CheckBigBotMatches();
    }

    private static Map? FindMap(string name, Func<Map, bool> fallback) => Maps.Find(name) ?? Maps.All.FirstOrDefault(fallback);

    // ---- Per-map limits and spawns -------------------------------------------------------------------

    private static void CheckMapLimits()
    {
        Line("map              size    area m2  limit  spawns (hand + generated)");
        foreach (var m in Maps.All)
        {
            Line($"  {m.Name,-15} {m.Size,-6} {m.HalfX * 2 * m.HalfZ * 2,8:0}  {m.MaxPlayers,5}  {m.Spawns.Count,3} ({m.AuthoredSpawns} + {m.GeneratedSpawns})"
                 + (m.MaxPlayersSetting > 0 ? "  [maxPlayers set in the file]" : ""));
            if (m.Spawns.Count < m.WantedMaxPlayers)
                Fail($"{m.Name}: {m.Spawns.Count} spawns for {m.WantedMaxPlayers} players (generation couldn't fit enough)");
            if (m.MaxPlayers < Game.ClassicPlayers || m.MaxPlayers > Game.MaxPlayers)
                Fail($"{m.Name}: limit {m.MaxPlayers} is outside {Game.ClassicPlayers}-{Game.MaxPlayers}");
            var expected = m.Size switch { MapSize.Small => (8, 10), MapSize.Medium => (12, 16), _ => (24, 32) };
            if (m.MaxPlayersSetting == 0 && (m.MaxPlayers < expected.Item1 || m.MaxPlayers > expected.Item2))
                Fail($"{m.Name}: default limit {m.MaxPlayers} doesn't match its size ({m.Size}: {expected.Item1}-{expected.Item2})");
            for (var i = m.AuthoredSpawns; i < m.Spawns.Count; i++)
            {
                var s = m.Spawns[i];
                if (m.IsBlocked(s) || s.Y < m.GroundHeight(s.X, s.Z) - 0.05f)
                    Fail($"{m.Name}: generated spawn ({s.X:0.0}, {s.Y:0.0}, {s.Z:0.0}) is inside geometry or under the ground");
                for (var j = 0; j < i; j++)
                    if (Vector3.Distance(m.Spawns[j], s) < 1.5f)
                    {
                        Fail($"{m.Name}: generated spawn ({s.X:0.0}, {s.Z:0.0}) is on top of another spawn");
                        break;
                    }
            }
            // Team modes need half the players on each side.
            var o = m.Objectives;
            if (o.TeamSpawnsGenerated && Math.Min(o.TeamSpawns[0].Count, o.TeamSpawns[1].Count) < m.MaxPlayers / 2)
                Fail($"{m.Name}: team spawns {o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} for {m.MaxPlayers} players");
        }

        // Generation is deterministic and leaves the fingerprint alone (the hand-placed spawns are what's hashed).
        if (Maps.All.FirstOrDefault(m => m.GeneratedSpawns > 0 && m.SourceFile != null) is { } g)
        {
            var again = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, g.SourceFile!)), g.SourceFile!);
            var hashBefore = again.ContentHash;
            again.Validate();
            if (!again.Spawns.SequenceEqual(g.Spawns))
                Fail($"{g.Name}: generated spawns differ between two loads");
            if (again.ContentHash != hashBefore || again.ContentHash != g.ContentHash)
                Fail($"{g.Name}: generating spawns changed the map's fingerprint");
            Line($"{g.Name}: the same {g.GeneratedSpawns} generated spawns on a second load; fingerprint {g.ContentHash:x8} unchanged");
        }

        // "maxPlayers" in a file: read, clamped to the spawns that fit, written back, and bad values refused.
        var json = """{ "name": "Limit test", "halfX": 20, "halfZ": 20, "maxPlayers": 6, "ops": [ { "op": "Ground", "color": [0.4, 0.4, 0.4] }, { "op": "RingSpawns", "count": 8, "frac": 0.8 } ] }""";
        var lt = MapFile.Load(json, "limit.json");
        lt.Validate();
        if (lt.MaxPlayers != 6 || !MapFile.ToJson(lt).Contains("\"maxPlayers\": 6"))
            Fail($"\"maxPlayers\": 6 gave a limit of {lt.MaxPlayers} (or wasn't written back)");
        var big = MapFile.Load(json.Replace("\"maxPlayers\": 6", "\"maxPlayers\": 32"), "limit32.json");
        big.Validate();
        Line($"maxPlayers 6 -> limit {lt.MaxPlayers}; maxPlayers 32 on a 40x40 m box -> {big.Spawns.Count} spawns ({big.GeneratedSpawns} generated), limit {big.MaxPlayers}");
        if (big.MaxPlayers > big.Spawns.Count)
            Fail("a map's limit is above its spawn count");
        try
        {
            MapFile.Load(json.Replace("\"maxPlayers\": 6", "\"maxPlayers\": 40"), "bad.json");
            Fail("\"maxPlayers\": 40 loaded without complaint");
        }
        catch (MapFileException)
        {
        }
    }

    // ---- Lobby slider and random map choice ------------------------------------------------------------

    private static void CheckLobbySlider()
    {
        var classic = Maps.FindPack("Classic") ?? Maps.Packs[0];
        var s = new Settings { MapRandom = true, MapPack = classic.Name };
        var packMax = s.LobbyMaxPlayers(Maps.Packs, Maps.All);
        if (packMax != classic.Maps.Max(m => m.MaxPlayers))
            Fail($"random from {classic.Name}: slider max {packMax}, biggest map takes {classic.Maps.Max(m => m.MaxPlayers)}");
        var rng = new Random(3);
        foreach (var players in new[] { 2, 8, 12, 20, 32 })
        {
            var picks = Enumerable.Range(0, 60).Select(_ => s.PickMap(Maps.Packs, Maps.All, rng, players)).ToList();
            var tooSmall = picks.Count(m => m.MaxPlayers < players && classic.Maps.Any(x => x.MaxPlayers >= players));
            Line($"random from {classic.Name} for {players,2} players: {picks.Select(m => m.Name).Distinct().Count()} different maps, smallest limit {picks.Min(m => m.MaxPlayers)}");
            if (tooSmall > 0 || picks.Any(m => m.Pack != classic))
                Fail($"random map for {players} players picked a map that doesn't take them (or left the pack)");
        }
        var chosen = classic.Maps.OrderBy(m => m.MaxPlayers).First();
        var c = new Settings { MapRandom = false, MapName = chosen.Name, MapPack = classic.Name };
        if (c.LobbyMaxPlayers(Maps.Packs, Maps.All) != chosen.MaxPlayers)
            Fail($"chosen map {chosen.Name}: slider max isn't its limit {chosen.MaxPlayers}");
        Line($"slider: random from {classic.Name} up to {packMax} players; {chosen.Name} chosen: up to {chosen.MaxPlayers}");

        // A game asked for more bots than the map takes gets the map's limit, and remembers the rest.
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: Game.MaxPlayers - 1, mapId: chosen.Id);
        if (game.Players.Count != chosen.MaxPlayers || game.WantedBots != Game.MaxPlayers - 1)
            Fail($"31 bots asked on {chosen.Name}: {game.Players.Count} players (limit {chosen.MaxPlayers}), wanted {game.WantedBots}");
        if (game.AddBot(BotDifficulty.Normal))
            Fail("AddBot went over the map's limit");
    }

    // ---- Snapshots -------------------------------------------------------------------------------------

    private static SnapPlayer RandomSnap(Random rng, byte id, bool mantle) => new()
    {
        Id = id,
        Position = new Vector3((float)rng.NextDouble() * 400 - 200, (float)rng.NextDouble() * 60 - 5, (float)rng.NextDouble() * 400 - 200),
        Velocity = new Vector3((float)rng.NextDouble() * 30 - 15, (float)rng.NextDouble() * 20 - 10, (float)rng.NextDouble() * 30 - 15),
        Yaw = (float)(rng.NextDouble() * Math.Tau - Math.PI), Pitch = (float)(rng.NextDouble() * 3 - 1.5),
        Health = (byte)rng.Next(256), Alive = rng.Next(2) == 0, Grounded = rng.Next(2) == 0, Stance = (Stance)rng.Next(3),
        Weapon = (byte)rng.Next(60), Boost = (byte)rng.Next(200),
        MantleTicks = mantle ? (byte)rng.Next(1, 30) : (byte)0, MantleTo = new Vector3(1.5f, 2.25f, -3.75f),
    };

    private static void CheckSnapshotCodec()
    {
        var rng = new Random(11);
        var fast = Enumerable.Range(0, Game.MaxPlayers).Select(i => RandomSnap(rng, (byte)i, i % 5 == 0)).ToList();
        var slow = fast.Select(f => new SnapSlow
        {
            Id = f.Id, Kills = (ushort)rng.Next(70000 & 0xFFFF), Deaths = (ushort)rng.Next(500), Primary = (byte)rng.Next(60), Secondary = (byte)rng.Next(60),
            PrimaryKit = (uint)rng.Next(1 << 24), SecondaryKit = (uint)rng.Next(1 << 24), Perks = new PerkSet((PerkId)1, (PerkId)2, (PerkId)3),
        }).ToList();
        var own = new SnapOwn { Position = new Vector3(12.3456f, 1.2345f, -98.7654f), Velocity = new Vector3(1.11f, -2.22f, 3.33f), Slow = slow[7] };
        var header = new SnapHeader { Tick = 123456, AckSeq = 99, Phase = MatchPhase.Playing, TimeLeft = 321.5f, Winner = 255, Present = uint.MaxValue };

        // A normal snapshot carries the slow fields of an eighth of the players (plus any that changed); a forced split carries all.
        foreach (var (budget, slowSet) in new[] { (PacketWriter.Mtu, slow.Where(x => x.Id % Game.SlowRefreshEvery == 3).ToList()), (400, slow) })
        {
            var parts = new List<PacketWriter>();
            var n = Snapshot.Build(header, own, fast, 7, slowSet, parts, budget);
            var gotFast = new Dictionary<byte, SnapPlayer>();
            var gotSlow = new Dictionary<byte, SnapSlow>();
            var into = new Snapshot.Packet();
            var biggest = 0;
            for (var i = 0; i < n; i++)
            {
                biggest = Math.Max(biggest, parts[i].Length);
                var r = new PacketReader(parts[i].Span[1..]);
                if (!Snapshot.Read(ref r, into))
                    Fail($"snapshot part {i} didn't decode");
                if (into.Header.Part != i || into.Header.Parts != n || into.Header.Tick != header.Tick || into.Header.Present != header.Present)
                    Fail($"snapshot part {i}: header came back as part {into.Header.Part}/{into.Header.Parts}, tick {into.Header.Tick}");
                if ((i == 0) != (into.Own != null) || (i == 0 && (into.Fast.Count == 0 || into.Fast[0].Id != 7)))
                    Fail($"snapshot part {i}: the recipient's own block/entry isn't (only) first in part 0");
                if (into.Own is { } o && (o.Position != own.Position || o.Velocity != own.Velocity || !o.Slow.Equals(own.Slow)))
                    Fail("own block didn't round-trip exactly");
                foreach (var f in into.Fast)
                    gotFast[f.Id] = f;
                foreach (var s in into.Slow)
                    gotSlow[s.Id] = s;
            }
            var worstPos = 0f;
            var worstYaw = 0f;
            foreach (var f in fast)
            {
                if (!gotFast.TryGetValue(f.Id, out var g))
                {
                    Fail($"player {f.Id} missing from the snapshot (budget {budget})");
                    continue;
                }
                worstPos = MathF.Max(worstPos, (g.Position - f.Position).Length());
                worstYaw = MathF.Max(worstYaw, MathF.Abs(World.WrapAngle(g.Yaw - f.Yaw)));
                if ((g.Velocity - f.Velocity).Length() > 0.01f || MathF.Abs(g.Pitch - f.Pitch) > 0.001f || g.Health != f.Health || g.Alive != f.Alive
                    || g.Grounded != f.Grounded || g.Stance != f.Stance || g.Weapon != f.Weapon || g.Boost != f.Boost || g.MantleTicks != f.MantleTicks
                    || (f.MantleTicks > 0 && (g.MantleTo - f.MantleTo).Length() > 0.01f))
                    Fail($"player {f.Id}: snapshot fields didn't round-trip");
                if (slowSet.Any(x => x.Id == f.Id) && (!gotSlow.TryGetValue(f.Id, out var gs) || !gs.Equals(slow[f.Id])))
                    Fail($"player {f.Id}: slow fields didn't round-trip");
            }
            Line($"32-player snapshot, budget {budget}: {n} part(s), biggest {biggest} bytes; position error {worstPos * 1000:0.0} mm, yaw error {worstYaw * 1000:0.00} mrad");
            if (biggest > budget)
                Fail($"a snapshot part is {biggest} bytes, over the {budget}-byte budget");
            if (budget == PacketWriter.Mtu && n != 1)
                Fail($"a 32-player snapshot (6 mantling) needed {n} packets");
            if (budget == 400 && n < 2)
                Fail("a 400-byte budget didn't split the snapshot");
            if (worstPos > 0.004f || worstYaw > 0.0002f)
                Fail("snapshot quantisation is coarser than 1/256 m and 0.1 mrad");
        }
    }

    // ---- Bots fitting the map -------------------------------------------------------------------------

    private static void CheckBotsFitMap()
    {
        var large = FindMap("Farmland", m => m.MaxPlayers == Game.MaxPlayers);
        var small = Maps.All.OrderBy(m => m.MaxPlayers).First();
        if (large == null)
        {
            Fail("no 32-player map");
            return;
        }
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: Game.MaxPlayers - 1, mapId: large.Id);
        var onLarge = game.Players.Count;
        game.SelfTestChangeMap(small);
        var onSmall = game.Players.Count;
        var hostKept = game.Players.ContainsKey(0);
        game.SelfTestChangeMap(large);
        var back = game.Players.Count;
        var ids = game.Players.Keys.Distinct().Count();
        Line($"31 bots wanted: {onLarge} players on {large.Name}, {onSmall} on {small.Name} (limit {small.MaxPlayers}), {back} back on {large.Name}");
        if (onLarge != Game.MaxPlayers || onSmall != small.MaxPlayers || !hostKept || back != Game.MaxPlayers || ids != back
            || game.Players.Keys.Any(id => id >= Game.MaxPlayers))
            Fail("bots didn't leave the smaller map / come back on the bigger one (or the host was removed, or ids clash)");

        // The vote only offers maps big enough for the people playing.
        var pool = Game.FitForPeople(Maps.All.ToList(), 12);
        if (pool.Any(m => m.MaxPlayers < 12) || pool.Count == 0)
            Fail("the vote pool for 12 people offered a map that takes fewer");
        Line($"vote pool for 12 people: {pool.Count} of {Maps.All.Length} maps");
    }

    // ---- Join refusal (real loopback) ------------------------------------------------------------------

    private static void Pump(Game server, IEnumerable<Game> clients, double seconds, ref double now, Stopwatch clock)
    {
        var until = clock.Elapsed.TotalSeconds + seconds;
        while (clock.Elapsed.TotalSeconds < until)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            server.Update(now, dt);
            foreach (var c in clients)
                c.Update(now, dt);
            Thread.Sleep(2);
        }
    }

    private static void CheckJoinRefusal()
    {
        var small = Maps.Find("Proto Box") ?? Maps.All.OrderBy(m => m.MaxPlayers).First();
        const ushort port = 60141;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: 0, mapId: small.Id);
        server.MapVoteEnabled = false;
        while (server.Players.Count < small.MaxPlayers - 1)
            server.SelfTestAddHuman();
        server.AddBot(BotDifficulty.Normal); // full: people and one bot
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 2.0, ref now, clock);
        var aIn = a.LocalId != byte.MaxValue && !a.Disconnected;
        var botsLeft = server.BotCount;
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 2.0, ref now, clock);
        Line($"{small.Name} full ({small.MaxPlayers}): first joiner {(aIn ? "took the bot's place" : "NOT in")} (bots left {botsLeft}); second: \"{b.Status}\"");
        if (!aIn || botsLeft != 0)
            Fail("a person joining a full map didn't take a bot's place");
        if (!b.Disconnected || !b.Status.Contains("full", StringComparison.OrdinalIgnoreCase) || server.Players.Count != small.MaxPlayers)
            Fail("a join to a map full of people wasn't refused with a 'full' message");
    }

    // ---- Loopback games at 8 / 16 / 32 ----------------------------------------------------------------

    private static void CheckLoopbackLobbies()
    {
        var cases = new (int Players, Map? Map)[]
        {
            (8, FindMap("Proto Box", m => m.MaxPlayers == 8)),
            (16, FindMap("Quarry", m => m.MaxPlayers == 16)),
            (32, FindMap("Farmland", m => m.MaxPlayers == 32)),
        };
        ushort port = 60150;
        foreach (var (players, map) in cases)
        {
            if (map == null)
            {
                Fail($"no map for {players} players");
                continue;
            }
            port++;
            var clock = Stopwatch.StartNew();
            var now = 0.0;
            using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: players - 2, botSkill: BotDifficulty.Normal, mapId: map.Id);
            server.MapVoteEnabled = false;
            server.Mode = players == 16 ? GameMode.TeamDeathmatch : GameMode.FreeForAll; // one team game among them
            using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [client], 1.5, ref now, clock);
            server.RequestRespawn();
            client.RequestRespawn();
            Pump(server, [client], Game.CountdownSeconds + 0.5, ref now, clock); // the countdown
            var rx0 = client.Net.BytesReceived;
            var snapBytes0 = server.SnapBytes;
            var snapPackets0 = server.SnapPackets;
            var t0 = clock.Elapsed.TotalSeconds;
            Pump(server, [client], 5, ref now, clock);
            var secs = clock.Elapsed.TotalSeconds - t0;
            var total = (client.Net.BytesReceived - rx0) / secs;
            var snaps = (server.SnapBytes - snapBytes0) / secs;
            var packets = (server.SnapPackets - snapPackets0) / secs;
            // What the client sees: everyone, with names, scores and positions matching the server.
            var missing = server.Players.Keys.Count(id => !client.Players.ContainsKey(id));
            var scoreOff = server.Players.Values.Count(p => client.Players.TryGetValue(p.Id, out var c) && Math.Abs(c.Kills - p.Kills) + Math.Abs(c.Deaths - p.Deaths) > 1);
            var far = server.Players.Values.Count(p => p.Alive && p.Id != client.LocalId && client.Players.TryGetValue(p.Id, out var c) && Vector3.Distance(c.Position, p.Position) > 4f);
            var kills = server.Players.Values.Sum(p => (int)p.Kills);
            Line($"loopback {players,2} players on {map.Name,-10} ({GameModes.Short(server.Mode)}): client sees {client.Players.Count}, kills {kills}; "
                 + $"snapshots {snaps / 1024:0.0} KB/s in {packets:0} packets/s (biggest {server.SnapMaxBytes} B, max {server.SnapMaxParts} part(s)); "
                 + $"everything the client received {total / 1024:0.0} KB/s "
                 + $"(protocol 10's snapshots + 30 Hz mode state would be {(16 + 65 * players + 55 + 4 * players) * 30 / 1024.0:0.0} KB/s); correction {client.LastCorrection * 100:0.0} cm");
            if (server.Players.Count != players)
                Fail($"loopback {players}: the server has {server.Players.Count} players");
            if (missing > 0 || client.Players.Count != server.Players.Count)
                Fail($"loopback {players}: the client sees {client.Players.Count} players, the server has {server.Players.Count}");
            if (scoreOff > 0 || far > 0)
                Fail($"loopback {players}: {scoreOff} scores and {far} positions differ between client and server");
            if (client.Players.Values.Any(p => p.Name.Length == 0))
                Fail($"loopback {players}: a player has no name on the client (roster)");
            if (server.SnapMaxBytes > PacketWriter.Mtu)
                Fail($"loopback {players}: a snapshot packet was {server.SnapMaxBytes} bytes (over {PacketWriter.Mtu})");
            if (client.Disconnected)
                Fail($"loopback {players}: the client was disconnected ({client.Status})");
        }
    }

    // ---- 32-player bot matches: the tick budget -------------------------------------------------------

    private static void CheckBigBotMatches()
    {
        foreach (var map in new[] { FindMap("Farmland", m => m.MaxPlayers == 32), FindMap("Quarry", m => m.Size == MapSize.Medium && m.MaxPlayers == 16) })
        {
            if (map == null)
                continue;
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: Game.MaxPlayers - 1, botSkill: BotDifficulty.Normal, mapId: map.Id);
            game.MapVoteEnabled = false;
            Game.SettleMemory();
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            var cpu = new TickCpu();
            var wall = new List<double>();
            var kills = 0;
            var lastMatch = game.MatchNumber;
            var lastKills = 0;
            var recipient = game.Players.Values.First(p => p.Bot != null);
            long snapBytes = 0, allBytes = 0;
            var snapCount = 0;
            var lastSnapTick = 0u;
            var modeBytes = 0L;
            var pw = new PacketWriter();
            for (var i = 0; i < 75 * World.TickRate; i++)
            {
                now += dt;
                cpu.Start();
                var t0 = Stopwatch.GetTimestamp();
                game.Update(now, dt);
                var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                var tick = i;
                cpu.Stop(ms, count: i > 60, () => $"t={tick / 60.0:0.00} s, {game.Phase}");
                if (i > 60)
                    wall.Add(ms);
                if (game.MatchNumber != lastMatch)
                {
                    kills += lastKills;
                    lastMatch = game.MatchNumber;
                }
                lastKills = game.Players.Values.Sum(p => (int)p.Kills);
                // Bandwidth as one client would get it, once play is on (snapshots at 30 Hz, mode state at 10 Hz).
                if (game.Phase == MatchPhase.Playing && game.ServerTick % World.SnapshotEvery == 0 && game.ServerTick != lastSnapTick)
                {
                    lastSnapTick = game.ServerTick;
                    foreach (var pkt in game.SelfTestSnapshotFor(recipient))
                        snapBytes += pkt.Length;
                    Game.SnapshotRelevance = false; // the same snapshot with everyone in it, to compare
                    foreach (var pkt in game.SelfTestSnapshotFor(recipient, prepare: false))
                        allBytes += pkt.Length;
                    Game.SnapshotRelevance = true;
                    snapCount++;
                    if (snapCount % 3 == 0)
                    {
                        game.WriteModeState(pw);
                        modeBytes += pw.Length;
                    }
                }
            }
            kills += lastKills;
            wall.Sort();
            var seconds = snapCount / (double)(World.TickRate / World.SnapshotEvery);
            var perSec = seconds > 0 ? (snapBytes + modeBytes) / seconds : 0;
            var perSecAll = seconds > 0 ? (allBytes + modeBytes) / seconds : 0;
            Line($"{game.Players.Count} players on {map.Name} (limit {map.MaxPlayers}, {game.BotCount} bots): kills {kills}, server tick wall median {wall[wall.Count / 2]:0.00} ms, "
                 + $"p99 {wall[(int)(wall.Count * 0.99)]:0.00} ms, worst work {cpu.WorstText}; snapshots + mode state per client {perSec / 1024:0.0} KB/s ({perSecAll / 1024:0.0} KB/s without relevance) "
                 + $"(biggest snapshot {game.SnapMaxBytes} B, {game.SnapMaxParts} part(s))");
            if (game.Players.Count != map.MaxPlayers)
                Fail($"{map.Name}: {game.Players.Count} players, expected the map's limit {map.MaxPlayers}");
            if (kills == 0)
                Fail($"{map.Name}: no kills in 75 s with {game.BotCount} bots");
            if (cpu.WorstMs > MaxTickWorkMs)
                Fail($"{map.Name}: a server tick with {game.Players.Count} players did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
            if (game.SnapMaxBytes > PacketWriter.Mtu)
                Fail($"{map.Name}: a snapshot packet was {game.SnapMaxBytes} bytes (over {PacketWriter.Mtu})");
        }
    }
}
