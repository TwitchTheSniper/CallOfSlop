using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Headless checks, run with "CallOfSlop.exe --selftest" (no window, no audio, no network).
/// Writes selftest.log next to the game and exits with the number of failures.
///   1. Every map: geometry, spawns and waypoints, nothing spawning inside walls.
///   2. Every map: a full offline match with 7 bots for 90 simulated seconds (crashes, kills, stuck bots, tick cost).
///   3. Mantling: through a Courtyard window and onto a crate.
///   4. Weapon files: the Weapons and Attachments folders load, round-trip, keep the old ids, and old saves migrate.
///      Attachments: every attachment on every weapon gives sane stats; kits survive Sanitize.
///   5. Packet sizes: a full 8-player snapshot fits in one packet.
/// It never touches settings, progress or loadout files.
/// </summary>
public static class SelfTest
{
    private static StreamWriter _log = null!;
    private static int _failures;

    private static void Line(string s)
    {
        _log.WriteLine(s);
        _log.Flush();
    }

    private static void Fail(string s)
    {
        _failures++;
        Line("  FAIL: " + s);
    }

    public static int Run()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "selftest.log");
        Brutal.GlfwApi.Glfw.Init(); // only for the clock (Glfw.Time); no window is created
        using (_log = new StreamWriter(path, false))
        {
            Line($"Call of Slop self-test {DateTime.Now}");
            var sw = Stopwatch.StartNew();
            Section("Maps", CheckMaps);
            Section("Map files", CheckMapFiles);
            Section("Map reload", CheckMapReload);
            Section("Mantling", CheckMantle);
            Section("Attachments", CheckAttachments);
            Section("Packets", CheckPackets);
            Section("Loadouts and perks", CheckLoadoutsAndPerks);
            Section("Melee", CheckMelee);
            Section("Matches", CheckMatches);
            Section("Map vote", CheckMapVote);
            Section("Server browser", CheckServerBrowser);
            Section("Weapon files", CheckWeaponFiles); // last: its file parsing and reflection mustn't disturb the tick timings above
            Line("");
            Line($"DONE in {sw.Elapsed.TotalSeconds:0.0} s with {_failures} failure(s).");
        }
        Brutal.GlfwApi.Glfw.Terminate();
        return _failures;
    }

    private static void Section(string name, Action body)
    {
        Line("");
        Line($"== {name} ==");
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Fail($"{name} threw: {ex}");
        }
    }

    // ---- 1. Maps -------------------------------------------------------------------------

    private static void CheckMaps()
    {
        var names = new HashSet<string>();
        foreach (var m in Maps.All)
        {
            if (!names.Add(m.Name))
                Fail($"duplicate map name {m.Name}");
            var mats = m.Materials;
            var blocked = m.Spawns.Count(s => m.IsBlocked(s));
            var groundSpawns = m.Spawns.Count(s => s.Y == 0);
            Line($"{m.Id,2} {m.Name,-14} {m.Size,-6} {m.HalfX * 2:0}x{m.HalfZ * 2:0} m  boxes {m.Solid.Count,5}  spawns {m.Spawns.Count,2}  waypoints {m.Waypoints.Count,4}  materials {string.Join(",", mats.GroupBy(x => x).Select(g => $"{g.Key}:{g.Count()}"))}");
            if (m.Spawns.Count < 8)
                Fail($"{m.Name}: only {m.Spawns.Count} spawns (need 8 for a full server)");
            if (blocked > 0)
                Fail($"{m.Name}: {blocked} spawn(s) still inside geometry");
            if (m.Waypoints.Count < 6)
                Fail($"{m.Name}: only {m.Waypoints.Count} waypoints");
            foreach (var b in m.Solid)
                if (!float.IsFinite(b.Center.X + b.Center.Y + b.Center.Z + b.Half.X + b.Half.Y + b.Half.Z) || b.Half.X <= 0 || b.Half.Y <= 0 || b.Half.Z <= 0)
                {
                    Fail($"{m.Name}: bad box {b.Center} {b.Half}");
                    break;
                }
            // Spawn exposure: is anyone standing on a spawn in plain sight of half the other spawns?
            _ = groundSpawns;
        }
    }

    // ---- 1b. Map files ----------------------------------------------------------------------

    /// <summary>Every map written to a file and read back must build exactly the same boxes, colours, spawns and waypoints.</summary>
    private static void CheckMapFiles()
    {
        var sources = Maps.All.Concat(Maps.Fallback()).ToList(); // the loaded maps (from files) and the code-defined fallback
        var checkedCount = 0;
        foreach (var original in sources)
        {
            var json = MapFile.ToJson(original);
            Map copy;
            try
            {
                copy = MapFile.Load(json, original.Name + " (round trip)");
            }
            catch (MapFileException ex)
            {
                Fail(ex.Message);
                continue;
            }
            if (original.SourceFile != null)
                copy.Validate(); // loaded maps have been validated; do the same to the copy
            var same = copy.Solid.Count == original.Solid.Count && copy.Solid.SequenceEqual(original.Solid)
                       && copy.Colors.SequenceEqual(original.Colors) && copy.Spawns.SequenceEqual(original.Spawns)
                       && copy.Waypoints.SequenceEqual(original.Waypoints) && copy.HalfX == original.HalfX && copy.HalfZ == original.HalfZ
                       && copy.Sky == original.Sky && copy.PrototypeGrid == original.PrototypeGrid;
            if (!same)
                Fail($"{original.Name}: map file round trip changed the map (boxes {original.Solid.Count} -> {copy.Solid.Count}, spawns {original.Spawns.Count} -> {copy.Spawns.Count})");
            checkedCount++;
        }
        Line($"{checkedCount} maps written and re-read identically; {Maps.All.Count(m => m.SourceFile != null)} of {Maps.All.Length} loaded from files in {Maps.Folder}");
        foreach (var e in Maps.LoadErrors)
            Fail("map file problem: " + e); // the shipped maps must all load
        if (Directory.Exists(Maps.Folder) && Maps.All.Count(m => m.SourceFile != null) < 24)
            Fail($"only {Maps.All.Count(m => m.SourceFile != null)} maps loaded from {Maps.Folder} (24 ship with the game)");

        // The example in the map makers' guide must actually load
        var guide = Path.Combine(Maps.Folder, "README.md");
        if (File.Exists(guide))
        {
            var text = File.ReadAllText(guide);
            var start = text.IndexOf("```json", StringComparison.Ordinal);
            var end = start < 0 ? -1 : text.IndexOf("```", start + 7, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                Fail("Maps/README.md has no ```json example");
            else
            {
                try
                {
                    var ex = MapFile.Load(text[(start + 7)..end], "README example");
                    ex.Validate();
                    Line($"guide example '{ex.Name}' loads: {ex.Solid.Count} boxes, {ex.Spawns.Count} spawns, {ex.Waypoints.Count} waypoints");
                }
                catch (MapFileException ex)
                {
                    Fail("the example in Maps/README.md doesn't load: " + ex.Message);
                }
            }
        }
        else
            Fail("Maps/README.md (the map makers' guide) wasn't copied next to the game");

        // A broken file gives a useful message instead of a crash
        foreach (var (bad, expect) in new[]
                 {
                     ("{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ { \"op\": \"Bulding\" } ] }", "unknown"),
                     ("{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ { \"op\": \"Prop\", \"x\": 1 } ] }", "needs"),
                     ("{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ { \"op\": \"Ground\", \"color\": [0.5, 0.5, 0.5] } ] }", "spawn"),
                 })
        {
            try
            {
                MapFile.Load(bad, "bad.json");
                Fail($"a broken map file loaded without complaint (expected '{expect}')");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (Exception ex)
            {
                Fail($"broken map file gave the wrong error: {ex.Message}");
            }
        }
    }

    // ---- 1c. Map reload ----------------------------------------------------------------------

    /// <summary>Reloading the folder changes nothing when no file changed, and a mid-match swap never leaves anyone inside a wall.</summary>
    private static void CheckMapReload()
    {
        var before = Maps.All.Select(m => m.Name).ToArray();
        var gen = Maps.Generation;
        var r = Maps.Reload();
        if (r.Added.Length + r.Removed.Length + r.Changed.Length > 0 || r.Count != before.Length)
            Fail("reloading unchanged map files reported changes: " + r.Summary);
        if (!Maps.All.Select(m => m.Name).SequenceEqual(before) || Maps.Generation != gen + 1)
            Fail("reload changed the map list or didn't bump Generation");
        Line("reload: " + r.Summary);

        // Single player on Proto Box; then the "edited" map puts a big block where the player stands.
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: 3, botSkill: BotDifficulty.Normal, mapId: 0);
        var name = World.Map.Name;
        var me = game.Players.Values.First(p => p.Bot == null);
        me.Position = new Vector3(0, 0, 0);
        var edited = MapFile.Load($$"""
            { "name": "{{name}}", "halfX": 20, "halfZ": 20, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Block", "x0": -3, "y0": 0, "z0": -3, "x1": 3, "y1": 6, "z1": 3, "color": "#808080" },
              { "op": "Prop", "x": 10, "z": 0, "w": 2, "h": 1, "d": 2, "color": "#f28026" },
              { "op": "RingSpawns", "count": 8, "frac": 0.8 }, { "op": "AutoWaypoints", "spacing": 6 } ] }
            """, "edited.json");
        edited.Validate();
        game.HotSwapMap(edited);
        var stuck = game.Players.Values.Where(p => p.Alive && edited.IsBlocked(p.Position)).Select(p => p.Name).ToList();
        if (World.Map != edited)
            Fail("hot swap didn't switch the map");
        if (stuck.Count > 0)
            Fail("hot swap left players inside geometry: " + string.Join(", ", stuck));
        Line($"hot swap: player moved from (0, 0) to ({me.Position.X:0.0}, {me.Position.Z:0.0}), nobody stuck");

        // Keep playing for a few seconds on the swapped map: bots must find new waypoints without errors.
        const double dt = 1.0 / World.TickRate;
        for (var t = 0.0; t < 5; t += dt)
            game.Update(t, dt);
        if (game.Players.Values.Any(p => float.IsNaN(p.Position.X) || p.Position.Y < -1))
            Fail("players went bad after the hot swap");
        World.SetMap(Maps.All[0]);
    }

    // ---- 2. Matches ----------------------------------------------------------------------

    private static void CheckMatches()
    {
        foreach (var m in Maps.All)
        {
            var sw = Stopwatch.StartNew();
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: Game.MaxPlayers - 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
            game.MapVoteEnabled = false; // stay on this map for the whole test
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            var lastPos = new Dictionary<byte, Vector3>();
            var moved = new Dictionary<byte, float>();
            var maxTickMs = 0.0;
            var phasesSeen = new HashSet<MatchPhase>();
            var killsDone = 0;
            var lastKillSum = 0;
            var lastMatch = game.MatchNumber;
            try
            {
                for (var i = 0; i < 90 * World.TickRate; i++)
                {
                    now += dt;
                    var t0 = Stopwatch.GetTimestamp();
                    game.Update(now, dt);
                    var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    if (i > 60)
                        maxTickMs = Math.Max(maxTickMs, ms);
                    phasesSeen.Add(game.Phase);
                    if (game.MatchNumber != lastMatch)
                    {
                        killsDone += lastKillSum; // scores reset at a new match
                        lastMatch = game.MatchNumber;
                    }
                    lastKillSum = game.Players.Values.Sum(p => (int)p.Kills);
                    if (i % 60 == 0)
                    {
                        foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                        {
                            if (lastPos.TryGetValue(p.Id, out var lp))
                                moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                            lastPos[p.Id] = p.Position;
                        }
                    }
                    foreach (var p in game.Players.Values)
                        if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                            throw new Exception($"player {p.Id} position is NaN");
                }
            }
            catch (Exception ex)
            {
                Fail($"{m.Name}: match crashed: {ex}");
                continue;
            }

            var bots = game.Players.Values.Where(p => p.Bot != null).ToList();
            var kills = killsDone + game.Players.Values.Sum(p => (int)p.Kills);
            var fallen = game.Players.Values.Count(p => p.Alive && p.Position.Y < -0.5f);
            var avgMove = moved.Count == 0 ? 0 : moved.Values.Average();
            var stuck = moved.Count(kv => kv.Value < 15f);
            Line($"{m.Name,-14} kills {kills,3}  matches {game.MatchNumber}  phases {string.Join("/", phasesSeen)}  avg bot travel {avgMove,5:0} m  stuck bots {stuck}  worst tick {maxTickMs,5:0.00} ms  ({sw.Elapsed.TotalSeconds:0.0} s)");
            if (bots.Count != Game.MaxPlayers - 1)
                Fail($"{m.Name}: expected {Game.MaxPlayers - 1} bots, got {bots.Count}");
            if (kills == 0 && m.Size != MapSize.Large)
                Fail($"{m.Name}: no kills in 90 s with 7 bots");
            if (fallen > 0)
                Fail($"{m.Name}: {fallen} player(s) below the floor");
            if (stuck > 3)
                Fail($"{m.Name}: {stuck} bots barely moved (stuck on geometry?)");
            if (maxTickMs > 8)
                Fail($"{m.Name}: a server tick took {maxTickMs:0.0} ms (budget 16 ms, warn above 8)");
            if (game.Local is { Alive: true } && game.MatchNumber == 1)
                Fail($"{m.Name}: the host was respawned without clicking Respawn");
        }
    }

    // ---- 2b. Map vote ---------------------------------------------------------------------

    private static void CheckMapVote()
    {
        for (var round = 0; round < 3; round++)
        {
            var start = Maps.All[round == 0 ? 0 : round * 7];
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: Game.MaxPlayers - 1, botSkill: BotDifficulty.Hard, mapId: start.Id);
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            byte[] ballot = [];
            byte[] tally = [];
            var hostVoted = false;
            for (var i = 0; i < 700 * World.TickRate && game.MatchNumber == 1; i++)
            {
                now += dt;
                game.Update(now, dt);
                if (game.VoteOpen)
                {
                    ballot = game.VoteOptions.ToArray();
                    tally = game.VoteTally.ToArray();
                    if (!hostVoted)
                    {
                        game.CastVote(0); // the host votes for the first map
                        hostVoted = true;
                    }
                }
            }
            if (ballot.Length == 0)
            {
                Fail($"{start.Name}: no vote happened (match {game.MatchNumber}, phase {game.Phase})");
                continue;
            }
            var names = string.Join(", ", ballot.Select(id => $"{Maps.Get(id).Name} ({Maps.Get(id).Size})"));
            Line($"{start.Name}: ballot {names}; final tally {string.Join("/", tally)}; next map {World.Map.Name}");
            if (ballot.Length != Game.VoteChoices)
                Fail($"ballot has {ballot.Length} maps");
            if (ballot.Contains(start.Id))
                Fail("the map just played was on the ballot");
            if (ballot.Select(id => Maps.Get(id).Size).Distinct().Count() < 3)
                Fail("ballot doesn't offer one map of each size");
            if (tally.Sum(t => t) != Game.MaxPlayers)
                Fail($"expected {Game.MaxPlayers} votes (7 bots + host), got {tally.Sum(t => t)}");
            if (!ballot.Contains(World.Map.Id))
                Fail("next map wasn't one of the options");
            else if (tally[Array.IndexOf(ballot, World.Map.Id)] != tally.Max())
                Fail("next map didn't have the most votes");
            if (game.MatchNumber != 2)
                Fail("the next match didn't start");
            // Everyone is on the new map, standing on solid ground.
            for (var i = 0; i < 2 * World.TickRate; i++)
            {
                now += dt;
                game.Update(now, dt);
            }
            var bad = game.Players.Values.Count(p => p.Alive && (World.Map.IsBlocked(p.Position) || p.Position.Y < -0.5f || MathF.Abs(p.Position.X) > World.Map.HalfX));
            if (bad > 0)
                Fail($"{bad} player(s) badly placed after switching to {World.Map.Name}");
        }
    }

    // ---- 2c. Server browser ---------------------------------------------------------------

    /// <summary>Host + master + browser on loopback: info in pongs, heartbeat, list query, ping.</summary>
    private static void CheckServerBrowser()
    {
        var info = new ServerInfo("Self-test server", "Harbor", 3, 8, 2, MatchPhase.Playing, 60123, ServerInfo.ProtocolVersion);
        var junk = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }.Concat(info.Encode()).ToArray();
        if (ServerInfo.Find(junk) != info)
            Fail("ServerInfo didn't survive encode/find");

        using var host = NetPeer.Host(60123, 4);
        using var master = NetPeer.Host(60124, 1);
        using var browser = NetPeer.Discovery();
        host.SetOfflineInfo(info.Encode());
        host.Advertise("127.0.0.1", 60124, MasterServerApp.Heartbeat(60123));
        var games = new Dictionary<string, (string Ip, ushort Port, DateTime Seen)>();
        List<(string, ushort)>? listed = null;
        ServerInfo? ponged = null;
        var asked = false;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 4 && (listed == null || ponged == null))
        {
            host.Poll((_, _) => { });
            MasterServerApp.Pump(master, games);
            if (!asked && games.Count > 0)
            {
                browser.Advertise("127.0.0.1", 60124, MasterServerApp.ListQuery);
                asked = true;
            }
            browser.PollRaw((from, data) =>
            {
                if ((DefaultMessageIDTypes)data[0] == DefaultMessageIDTypes.AdvertiseSystem && MasterServerApp.ParseList(data) is { } l)
                {
                    listed = l;
                    foreach (var (ip, port) in l)
                        browser.PingHost(ip, port);
                }
                else if ((DefaultMessageIDTypes)data[0] == DefaultMessageIDTypes.UnconnectedPong && ServerInfo.Find(data) is { } i)
                    ponged = i;
            });
            Thread.Sleep(10);
        }
        Line($"master saw {games.Count} game(s): {string.Join(", ", games.Keys)}; list reply {(listed == null ? "none" : string.Join(", ", listed.Select(x => $"{x.Item1}:{x.Item2}")))}; "
            + $"pong {(ponged is { } p ? $"'{p.Name}' map {p.MapName} {p.Players}/{p.MaxPlayers}" : "none")} after {sw.ElapsedMilliseconds} ms");
        if (games.Count != 1)
            Fail("master didn't record the heartbeat");
        if (listed is not { Count: 1 } || listed[0].Item2 != 60123)
            Fail("browser didn't get the game from the master");
        if (ponged != info)
            Fail("ping didn't return the server info");
    }

    // ---- 3. Mantling ---------------------------------------------------------------------

    private static void CheckMantle()
    {
        // Courtyard: house at (-20,-20) 14x12, north wall at z=-26..-25.7 with a window centred at x=-23.5 (sill 1.0, top 2.3).
        World.SetMap(Maps.Find("Courtyard")!);
        var s = new MoveState { Position = new Vector3(-23.5f, 0, -26.6f), Grounded = true };
        var yaw = MathF.PI; // facing +Z
        var mantled = false;
        for (var i = 0; i < 150; i++)
        {
            var b = Buttons.Forward | (i < 40 ? Buttons.Jump : Buttons.None);
            World.Simulate(ref s, new InputCmd { Buttons = b, Yaw = yaw, SpeedPct = 100 });
            mantled |= s.MantleTicks > 0;
        }
        Line($"window: mantled={mantled}, ended at ({s.Position.X:0.00}, {s.Position.Y:0.00}, {s.Position.Z:0.00}) stance {s.Stance}");
        if (!mantled)
            Fail("did not mantle into the Courtyard window");
        else if (s.Position.Z < -25.4f)
            Fail("mantled but didn't get through the window");

        // Same window, off-centre by 0.3 m: the sideways nudge should still line it up.
        s = new MoveState { Position = new Vector3(-23.2f, 0, -26.6f), Grounded = true };
        mantled = false;
        for (var i = 0; i < 150; i++)
        {
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | (i < 40 ? Buttons.Jump : Buttons.None), Yaw = yaw, SpeedPct = 100 });
            mantled |= s.MantleTicks > 0;
        }
        Line($"window off-centre: mantled={mantled}, ended at z {s.Position.Z:0.00}");
        if (!mantled || s.Position.Z < -25.4f)
            Fail("off-centre window mantle failed");

        // Proto Box crate: find any box 0.6-1.7 m tall and climb onto it.
        World.SetMap(Maps.All[0]);
        var crate = World.Map.Solid.FirstOrDefault(b => b.Min.Y < 0.01f && b.Max.Y > 0.6f && b.Max.Y < 1.7f);
        if (crate.Half == Vector3.Zero)
        {
            Line("no crate-sized box on Proto Box to test");
            return;
        }
        var start = new Vector3(crate.Center.X, 0, crate.Min.Z - World.PlayerRadius - 0.05f);
        s = new MoveState { Position = start, Grounded = true };
        mantled = false;
        var trace = new List<string>();
        for (var i = 0; i < 90; i++)
        {
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | (i < 20 ? Buttons.Jump : Buttons.None), Yaw = MathF.PI, SpeedPct = 100 });
            mantled |= s.MantleTicks > 0;
            if (i < 6 || i % 15 == 0)
                trace.Add($"t{i}:({s.Position.Y:0.00},{s.Position.Z:0.00},m{s.MantleTicks},g{(s.Grounded ? 1 : 0)})");
        }
        Line($"crate {crate.Center} half {crate.Half}: mantled={mantled}, ended at ({s.Position.X:0.00}, {s.Position.Y:0.00}, {s.Position.Z:0.00})");
        Line("  " + string.Join(" ", trace));
        if (!mantled)
            Fail("could not mantle onto a crate");
    }

    // ---- 3b. Loadouts and perks --------------------------------------------------------------

    private static void CheckLoadoutsAndPerks()
    {
        // Loadout slots: 1 at level 1, the last at max level, evenly spread, always increasing.
        var levels = Enumerable.Range(0, Progress.MaxLoadoutSlots).Select(Progress.LoadoutSlotLevel).ToArray();
        Line($"loadout slots unlock at levels {string.Join(", ", levels)} (max level {Progress.MaxLevel})");
        if (levels[0] != 1 || levels[^1] != Progress.MaxLevel || levels.Zip(levels.Skip(1)).Any(p => p.Second <= p.First))
            Fail("loadout slot levels must go 1 ... max level, strictly increasing");
        var gaps = levels.Zip(levels.Skip(1)).Select(p => p.Second - p.First).ToArray();
        if (gaps.Max() - gaps.Min() > 1)
            Fail($"loadout slots aren't evenly spaced: gaps {string.Join(",", gaps)}");
        var fresh = new Progress { ReadOnly = true };
        if (fresh.LoadoutSlots != 1)
            Fail($"a level-1 player has {fresh.LoadoutSlots} loadout slots (should be 1)");
        var maxed = new Progress { ReadOnly = true, Xp = Progress.TotalFor(Progress.MaxLevel) };
        if (maxed.LoadoutSlots != Progress.MaxLoadoutSlots)
            Fail($"a max-level player has {maxed.LoadoutSlots} loadout slots");

        // Perks: four per category, the starters at level 1, everything unlocks by max level.
        foreach (var c in new[] { PerkCategory.Movement, PerkCategory.Survival, PerkCategory.Damage })
        {
            var inCat = Perks.In(c).ToArray();
            Line($"{Perks.CategoryName(c)}: {string.Join(", ", inCat.Select(p => $"{p.Name} (lvl {Progress.PerkLevel(p.Id)})"))}");
            if (inCat.Length < 3)
                Fail($"{c} has only {inCat.Length} perks");
            if (!inCat.Any(p => Progress.PerkLevel(p.Id) == 1))
                Fail($"{c} has no starter perk");
        }
        if (Perks.All.Any(p => Progress.PerkLevel(p.Id) > Progress.MaxLevel))
            Fail("a perk unlocks past max level");

        // A level-1 player's loadout with locked things in it falls back to defaults.
        var l = new Loadout { Primary = Weapons.Find("Railspike")!.Id, Grenade = ThrowableKind.Smoke, Perks = new PerkSet(PerkId.Parkour, PerkId.LastStand, PerkId.PointBlank) };
        var eff = LoadoutStore.Effective(l, fresh);
        if (eff.Primary != Weapons.DefaultPrimary.Id || eff.Grenade != ThrowableKind.None || eff.Perks.Movement != PerkId.None
            || eff.Perks.Survival != PerkId.None || eff.Perks.Damage != PerkId.PointBlank)
            Fail($"locked items weren't swapped out: {Weapons.Get(eff.Primary).Name}, {eff.Grenade}, {eff.Perks}");

        // ---- Movement perks in the shared physics ----
        World.SetMap(Maps.All[0]);
        float Run(PerkId perk, Buttons stance, int ticks, byte boost = 0)
        {
            var st = new MoveState { Position = new Vector3(-14, 0, 14), Grounded = true, MovePerk = perk, BoostTicks = boost };
            for (var i = 0; i < ticks; i++)
                World.Simulate(ref st, new InputCmd { Buttons = Buttons.Right | stance, Yaw = 0, SpeedPct = 100 });
            return st.Position.X + 14;
        }
        var crouch = Run(PerkId.None, Buttons.Crouch, 60);
        var crouchLp = Run(PerkId.LowProfile, Buttons.Crouch, 60);
        var walk = Run(PerkId.None, Buttons.None, 40);
        var walkBoost = Run(PerkId.HeadStart, Buttons.None, 40, Perks.BoostTicks);
        Line($"1 s crouched: {crouch:0.00} m, with Low Profile {crouchLp:0.00} m;  0.67 s walking: {walk:0.00} m, boosted {walkBoost:0.00} m");
        if (crouchLp < crouch * 1.6f)
            Fail("Low Profile doesn't speed up crouching enough");
        if (walkBoost < walk * 1.25f)
            Fail("Head Start / Afterburner boost isn't faster");
        var boosted = new MoveState { Position = new Vector3(-14, 0, 14), Grounded = true, BoostTicks = 10 };
        for (var i = 0; i < 20; i++)
            World.Simulate(ref boosted, new InputCmd { Yaw = 0, SpeedPct = 100 });
        if (boosted.BoostTicks != 0)
            Fail("speed boost doesn't wear off");

        // Parkour: a 3 m wall is out of reach normally (even jumping first) but climbable with the perk.
        var wallMap = MapFile.Load("""
            { "name": "Parkour test", "halfX": 20, "halfZ": 20, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Block", "x0": -3, "y0": 0, "z0": 2, "x1": 3, "y1": 3.0, "z1": 4, "color": "#808080" },
              { "op": "Spawn", "x": 10, "z": 10 } ] }
            """, "parkour.json");
        World.SetMap(wallMap);
        bool Climbs(PerkId perk)
        {
            var st = new MoveState { Position = new Vector3(0, 0, 1.5f), Grounded = true, MovePerk = perk };
            var climbed = false;
            for (var i = 0; i < 90; i++)
            {
                World.Simulate(ref st, new InputCmd { Buttons = Buttons.Forward | (i < 30 ? Buttons.Jump : Buttons.None), Yaw = MathF.PI, SpeedPct = 100 });
                climbed |= st.Position.Y > 2.9f; // (it walks off the far side afterwards)
            }
            return climbed;
        }
        var normal = Climbs(PerkId.None);
        var parkour = Climbs(PerkId.Parkour);
        Line($"3 m wall: climbed without perk {normal}, with Parkour {parkour}");
        if (normal || !parkour)
            Fail("Parkour climb reach is wrong");
        World.SetMap(Maps.All[0]);

        // ---- Damage perks (pure maths) ----
        var all = new PerkSet(PerkId.None, PerkId.None, PerkId.PointBlank);
        if (MathF.Abs(Perks.AttackMult(all, 5, -Vector3.UnitZ, 0, 100, false) - Perks.PointBlankMult) > 0.001f || Perks.AttackMult(all, 12, -Vector3.UnitZ, 0, 100, false) != 1f)
            Fail("Point Blank range check is wrong");
        var flank = new PerkSet(PerkId.None, PerkId.None, PerkId.Flanker);
        // Victim faces -Z (yaw 0). A shot travelling -Z hits their back; travelling +Z hits their front.
        if (Perks.AttackMult(flank, 10, -Vector3.UnitZ, 0, 100, false) < 1.5f || Perks.AttackMult(flank, 10, Vector3.UnitZ, 0, 100, false) != 1f)
            Fail("Flanker back/front check is wrong");
        var opener = new PerkSet(PerkId.None, PerkId.None, PerkId.Opener);
        if (Perks.AttackMult(opener, 10, Vector3.UnitX, 0, 100, false) < 1.4f || Perks.AttackMult(opener, 10, Vector3.UnitX, 0, 99, false) != 1f)
            Fail("Opener full-health check is wrong");

        // ---- Survival perks on the server ----
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var a = game.Players[0];
        var v = game.Players.Values.First(p => p.Id != 0);
        void Reset(PerkSet attacker, PerkSet victim)
        {
            game.SelfTestPerks(a, attacker);
            game.SelfTestPerks(v, victim);
            game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
            game.SelfTestPlace(v, new Vector3(5, 0, 8), 0);
        }
        Reset(PerkSet.Empty, new PerkSet(PerkId.None, PerkId.FlakJacket, PerkId.None));
        game.SelfTestDamage(a, v, 100, DamageKind.Explosive, 1);
        var flakHp = v.Health;
        Reset(PerkSet.Empty, new PerkSet(PerkId.None, PerkId.LastStand, PerkId.None));
        game.SelfTestDamage(a, v, 150, DamageKind.Bullet, 1);
        var standHp = v.Health;
        game.SelfTestDamage(a, v, 50, DamageKind.Bullet, 1.5);
        var immuneHp = v.Health;
        var killed = game.SelfTestDamage(a, v, 50, DamageKind.Bullet, 3);
        Line($"Flak Jacket: 100 blast -> {World.MaxHealth - flakHp} damage;  Last Stand: lethal hit -> {standHp} HP, hit while immune -> {immuneHp} HP, later hit kills {killed}");
        if (flakHp != World.MaxHealth - (int)MathF.Round(100 * Perks.FlakMult))
            Fail("Flak Jacket didn't reduce blast damage");
        if (standHp != 1 || immuneHp != 1 || !killed)
            Fail("Last Stand didn't save once and then expire");

        Reset(PerkSet.Empty, new PerkSet(PerkId.None, PerkId.Entrenched, PerkId.None));
        v.Sim.Stance = Stance.Crouch;
        game.SelfTestDamage(a, v, 50, DamageKind.Bullet, 1);
        var dugInHp = v.Health;
        if (dugInHp != World.MaxHealth - (int)MathF.Round(50 * Perks.EntrenchedMult))
            Fail($"Entrenched: crouched and still took {World.MaxHealth - dugInHp} of 50");

        Reset(new PerkSet(PerkId.Afterburner, PerkId.Bloodthirst, PerkId.None), PerkSet.Empty);
        a.Health = 30;
        game.SelfTestDamage(a, v, 200, DamageKind.Bullet, 1);
        Line($"Entrenched: 50 bullet damage -> {World.MaxHealth - dugInHp};  Bloodthirst kill: 30 HP -> {a.Health} HP;  Afterburner boost {a.Sim.BoostTicks} ticks");
        if (a.Health != World.MaxHealth)
            Fail("Bloodthirst didn't heal on a kill");
        if (a.Sim.BoostTicks != Perks.BoostTicks)
            Fail("Afterburner didn't boost on a kill");
        game.SelfTestPerks(a, new PerkSet(PerkId.HeadStart, PerkId.None, PerkId.None));
        if (a.Sim.BoostTicks != Perks.BoostTicks || a.Sim.MovePerk != PerkId.HeadStart)
            Fail("Head Start doesn't boost on spawn");
    }

    // ---- 3c. Melee ------------------------------------------------------------------------------

    private static void CheckMelee()
    {
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var a = game.Players[0];
        var v = game.Players.Values.First(p => p.Id != 0);
        game.SelfTestPerks(a, PerkSet.Empty);
        game.SelfTestPerks(v, PerkSet.Empty);

        // Attacker at z=5 facing +Z (yaw pi); victim 1.3 m ahead.
        game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
        game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), 0); // facing the attacker
        game.SelfTestMelee(a, MathF.PI, 0, 1);
        var front = World.MaxHealth - v.Health;
        game.SelfTestMelee(a, MathF.PI, 0, 1.2); // still cooling down
        var spam = World.MaxHealth - v.Health;

        game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
        game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), MathF.PI); // back turned
        game.SelfTestMelee(a, MathF.PI, 0, 3);
        var backstabKill = !v.Alive;

        game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
        game.SelfTestPlace(v, new Vector3(5, 0, 9), 0); // 4 m away: out of reach
        game.SelfTestMelee(a, MathF.PI, 0, 5);
        var far = World.MaxHealth - v.Health;

        game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), 0);
        game.SelfTestMelee(a, 0, 0, 7); // facing the wrong way
        var behind = World.MaxHealth - v.Health;

        Line($"front hit {front} dmg (second swing during cooldown adds {spam - front}), backstab kills {backstabKill}, 4 m away {far} dmg, facing away {behind} dmg, kill name '{Throwables.KillName(Game.MeleeKillId)}'");
        if (front != (int)Game.MeleeDamage)
            Fail($"a front melee hit did {front} (expected {Game.MeleeDamage})");
        if (spam != front)
            Fail("melee cooldown not enforced");
        if (!backstabKill)
            Fail("melee from behind didn't kill");
        if (far != 0 || behind != 0)
            Fail("melee hit something out of reach or behind the attacker");
    }

    // ---- 4. Attachments ------------------------------------------------------------------

    private static void CheckAttachments()
    {
        var combos = 0;
        foreach (var w in Weapons.All)
        {
            var list = Attachments.For(w);
            foreach (var a in list)
            {
                var kit = Attachments.With(0, a.Slot, a.Index);
                var d = Attachments.Apply(w, kit);
                combos++;
                if (d == w || d.Id != w.Id || d.Kit != kit)
                    Fail($"{w.Name} + {a.Name}: Apply didn't produce a kitted copy");
                if (d.Mag < 1 || d.Rpm <= 0 || d.Reload <= 0 || d.RangeM <= 0 || d.HipSpreadDeg <= 0 || d.RecoilDeg < 0 || d.AdsZoom < 1 || d.AdsTime <= 0)
                    Fail($"{w.Name} + {a.Name}: bad stats (mag {d.Mag}, range {d.RangeM}, ads {d.AdsTime})");
                if (Attachments.Sanitize(w, kit) != kit)
                    Fail($"{w.Name} + {a.Name}: Sanitize dropped a fitting attachment");
                if (Attachments.UnlockXp(w, a) is var x && (x < 50 || x > Attachments.MaxXp(w)))
                    Fail($"{w.Name} + {a.Name}: unlock at {x} weapon XP (max {Attachments.MaxXp(w)})");
            }
            // A full kit: first fitting attachment in every slot
            uint full = 0;
            foreach (var a in list)
                if (Attachments.Slot(full, a.Slot) == 0)
                    full = Attachments.With(full, a.Slot, a.Index);
            var fd = Attachments.Apply(w, full);
            if (fd.Mobility is < 0.6f or > 1.25f)
                Fail($"{w.Name}: full kit mobility {fd.Mobility}");
            if (!ReferenceEquals(fd, Attachments.Apply(w, full)))
                Fail($"{w.Name}: kit cache returned a different object");
            // A kit with junk in every slot must come back clean
            if (Attachments.Sanitize(w, 0xFFFFFF) is var junk && Attachments.InKit(junk).Any(a => !a.Fits(w)))
                Fail($"{w.Name}: Sanitize let a non-fitting attachment through");
        }
        var counts = Weapons.All.Select(w => Attachments.For(w).Count).ToArray();
        Line($"{combos} weapon+attachment combinations checked; attachments per weapon {counts.Min()}-{counts.Max()}");

        // Pacing (rough targets): ~1,000 weapon XP and ~1,000 account XP per typical match (~7 kills).
        const float perMatch = 1000f;
        var matchesToMax = Weapons.All.Where(w => Attachments.For(w).Count > 0).Select(w => Attachments.MaxXp(w) / perMatch).ToArray();
        Line($"matches to max a gun: {matchesToMax.Min():0.0} - {matchesToMax.Max():0.0} (target 3-7), average {matchesToMax.Average():0.0}");
        if (matchesToMax.Min() < 2.5f || matchesToMax.Max() > 7.5f)
            Fail("attachment pacing outside 3-7 matches");
        var sixty = Weapons.All.Where(w => Attachments.For(w).Count > 0).Select(w =>
        {
            var list = Attachments.For(w);
            var k = Math.Max(0, (int)MathF.Ceiling(list.Count * 0.6f) - 1);
            return Attachments.UnlockXp(w, list[k]);
        }).ToArray();
        var levelCosts = Enumerable.Range(1, Progress.MaxLevel - 1).Select(Progress.XpForNext).ToArray();
        Line($"XP for 60% of a gun's attachments: {sixty.Min():N0}-{sixty.Max():N0}, avg {sixty.Average():N0};  XP per level (= per new unlock): {levelCosts.Min():N0}-{levelCosts.Max():N0}, avg {levelCosts.Average():N0}");
        Line($"levels {Progress.MaxLevel}, total XP to unlock everything {Progress.TotalFor(Progress.MaxLevel):N0} (~{Progress.TotalFor(Progress.MaxLevel) / perMatch:0} matches)");
        var ratio = levelCosts.Average() / sixty.Average();
        if (ratio < 0.7 || ratio > 1.4)
            Fail($"level cost vs 60%-of-a-gun ratio {ratio:0.00} is off target");
    }

    // ---- 4a. Weapon and attachment files -------------------------------------------------------

    /// <summary>Every public field that differs between two weapons, as "Field: a vs b".</summary>
    private static List<string> WeaponDiffs(WeaponDef a, WeaponDef b, bool includeId)
    {
        var diffs = new List<string>();
        foreach (var f in typeof(WeaponDef).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!includeId && f.Name == nameof(WeaponDef.Id))
                continue;
            var x = f.GetValue(a);
            var y = f.GetValue(b);
            if (!Equals(x, y))
                diffs.Add($"{f.Name}: {x} vs {y}");
        }
        return diffs;
    }

    /// <summary>The first ```json block of a guide, or null.</summary>
    private static string? GuideExample(string path)
    {
        if (!File.Exists(path))
            return null;
        var text = File.ReadAllText(path);
        var start = text.IndexOf("```json", StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf("```", start + 7, StringComparison.Ordinal);
        return start < 0 || end < 0 ? "" : text[(start + 7)..end];
    }

    private static void CheckWeaponFiles()
    {
        // ---- The shipped files all load ----
        foreach (var e in Weapons.LoadErrors.Concat(Attachments.LoadErrors))
            Fail("weapon/attachment file problem: " + e);
        var weaponFiles = Weapons.All.Count(w => w.SourceFile != null);
        var attachments = Attachments.BySlot.SelectMany(x => x).ToArray();
        Line($"{weaponFiles} of {Weapons.All.Length} weapons loaded from {Weapons.Folder}, {attachments.Length} attachments from {Attachments.Folder}");
        Line($"weapon set fingerprint {WeaponSet.Fingerprint:x8} (weapons {WeaponSet.WeaponsHash:x8}, attachments {WeaponSet.AttachmentsHash:x8})");
        if (weaponFiles < Weapons.LegacyNames.Length)
            Fail($"only {weaponFiles} weapons loaded from files ({Weapons.LegacyNames.Length} ship with the game)");
        if (attachments.Length < 21)
            Fail($"only {attachments.Length} attachments loaded from files (21 ship with the game)");
        if (attachments.Any(a => a.Index is < 1 or > Attachments.MaxPerSlot))
            Fail("an attachment index is outside 1-15 (kits pack 4 bits per slot)");

        // ---- The original 50 weapons keep their ids (old saves and anything else that stored an id) ----
        for (var i = 0; i < Weapons.LegacyNames.Length; i++)
        {
            if (Weapons.Find(Weapons.LegacyNames[i]) is not { } w)
                Fail($"'{Weapons.LegacyNames[i]}' (legacy id {i}) isn't loaded");
            else if (w.Id != i)
                Fail($"'{w.Name}' has id {w.Id}, but it was {i} before weapons were files");
        }
        if (Weapons.DefaultPrimary.Name != "BR-1 Prototype" || Weapons.DefaultSecondary.Name != "P9 Sidearm")
            Fail($"default weapons are {Weapons.DefaultPrimary.Name} / {Weapons.DefaultSecondary.Name}");

        // ---- Round trip: written out and read back, nothing changes ----
        var roundTrips = 0;
        foreach (var w in Weapons.All)
        {
            try
            {
                var copy = WeaponFile.Load(WeaponFile.ToJson(w), w.Name + " (round trip)");
                var diffs = WeaponDiffs(w, copy, includeId: false);
                if (copy.Order != w.Order || copy.UnlockLevel != w.UnlockLevel || copy.Author != w.Author)
                    diffs.Add("order/unlockLevel/author");
                foreach (var d in diffs)
                    Fail($"{w.Name}: weapon file round trip changed {d}");
                roundTrips++;
            }
            catch (WeaponFileException ex)
            {
                Fail(ex.Message);
            }
        }
        foreach (var a in attachments.Where(a => a.Rule != null))
        {
            try
            {
                var copy = AttachmentFile.Load(AttachmentFile.ToJson(a), a.Name + " (round trip)");
                if (AttachmentFile.ToJson(copy) != AttachmentFile.ToJson(a))
                    Fail($"{a.Name}: attachment file round trip changed it");
                foreach (var w in Weapons.All)
                {
                    if (copy.Fits(w) != a.Fits(w))
                        Fail($"{a.Name}: round trip changed whether it fits the {w.Name}");
                    var x = w.Clone();
                    var y = w.Clone();
                    a.Apply(x);
                    copy.Apply(y);
                    foreach (var d in WeaponDiffs(x, y, includeId: true))
                        Fail($"{a.Name} on {w.Name}: round trip changed {d}");
                }
                roundTrips++;
            }
            catch (WeaponFileException ex)
            {
                Fail(ex.Message);
            }
        }
        Line($"{roundTrips} weapon and attachment files written and re-read identically");

        // ---- TEMPORARY: the files hold exactly what the old code catalogue held ----
        CompareWithBuiltInCatalogue();

        // ---- The built-in fallback weapons (used when the folder is missing) ----
        var fallback = Weapons.Fallback();
        if (fallback.Count(w => w.Slot == Slot.Primary) != 1 || fallback.Count(w => w.Slot == Slot.Secondary) != 1)
            Fail("there must be exactly one built-in fallback weapon per slot");
        foreach (var fb in fallback)
        {
            try
            {
                WeaponFile.Validate(fb, fb.Name + " (fallback)");
            }
            catch (WeaponFileException ex)
            {
                Fail(ex.Message);
            }
            if (Weapons.Find(fb.Name) is { } file && WeaponDiffs(fb, file, includeId: false) is { Count: > 0 } d)
                Line($"note: the built-in fallback {fb.Name} differs from its file ({string.Join("; ", d)})");
        }

        // ---- Unlocks: the hand-tuned table still applies by name ----
        if (Weapons.Find("Courier") is { } courier && Progress.UnlockLevel(courier) != 2)
            Fail($"Courier unlocks at level {Progress.UnlockLevel(courier)} (should be 2)");
        if (Progress.UnlockLevel(Weapons.DefaultPrimary) != 1 || Progress.UnlockLevel(Weapons.DefaultSecondary) != 1)
            Fail("the default weapons aren't unlocked at level 1");
        if (Weapons.Find("Toy Blaster") is { } toy && Progress.UnlockLevel(toy) != Progress.MaxLevel)
            Fail($"Toy Blaster unlocks at level {Progress.UnlockLevel(toy)} (should be the max, {Progress.MaxLevel})");
        foreach (var w in Weapons.All)
            if (Progress.All.Count(u => u.Weapon == w) != 1)
                Fail($"{w.Name} appears {Progress.All.Count(u => u.Weapon == w)} times in the unlock table");

        // ---- progress.txt: old id-based weapon XP becomes name-based ----
        var old = Progress.Parse(["xp=5000", "wx16=1234", "wk9=3", "wx9=50", "wk17=2", "wx99=7", "wx.Courier=10", "wx11=999"]);
        var expect = new (string Name, int Xp)[] { ("BR-1 Prototype", 1234), ("Hornet SMG", 50), ("Vanguard AR", 200), ("Courier", 10) };
        foreach (var (name, xp) in expect)
            if (old.WeaponXp(name) != xp)
                Fail($"old save: {name} has {old.WeaponXp(name)} weapon XP (expected {xp})");
        if (Weapons.Find("BR-1 Prototype") is { } br && old.WeaponXp(br.Id) != 1234)
            Fail("weapon XP by id doesn't find the name-based entry");
        var saved = old.ToLines().ToArray();
        if (!saved.Contains("wx.BR-1 Prototype=1234") || saved.Any(l => l.Length > 2 && (l.StartsWith("wx", StringComparison.Ordinal) || l.StartsWith("wk", StringComparison.Ordinal)) && char.IsDigit(l[2])))
            Fail($"progress isn't saved with name keys: {string.Join(" | ", saved)}");
        var reread = Progress.Parse(saved);
        if (expect.Any(e => reread.WeaponXp(e.Name) != e.Xp) || reread.Xp != 5000)
            Fail("progress didn't survive a save and reload");
        if (!Progress.Parse(["wx.Some Modded Gun=77"]).ToLines().Contains("wx.Some Modded Gun=77"))
            Fail("weapon XP for a weapon that isn't loaded was dropped");
        Line($"old save migrated: {string.Join(", ", expect.Select(e => $"{e.Name} {old.WeaponXp(e.Name)}"))}");

        // ---- Multiplayer fingerprint ----
        if (WeaponSet.Mismatch(WeaponSet.WeaponsHash, (byte)Weapons.All.Length, WeaponSet.AttachmentsHash, (byte)attachments.Length) != null)
            Fail("our own weapon set doesn't match itself");
        var tweaked = Weapons.All[0].Clone();
        tweaked.Damage++;
        if (WeaponSet.Fnv([WeaponFile.ToJson(tweaked)]) == WeaponSet.Fnv([WeaponFile.ToJson(Weapons.All[0])]))
            Fail("changing a stat didn't change the fingerprint");
        var mismatch = WeaponSet.Mismatch(WeaponSet.WeaponsHash ^ 1, (byte)(Weapons.All.Length + 1), WeaponSet.AttachmentsHash, (byte)attachments.Length);
        if (mismatch == null || !mismatch.Contains("Weapons folder"))
            Fail($"a different weapon set wasn't reported clearly: {mismatch}");
        else
            Line($"mismatch message: {mismatch}");

        // ---- The examples in the guides load ----
        foreach (var (guide, isWeapon) in new[] { (Path.Combine(Weapons.Folder, "README.md"), true), (Path.Combine(Attachments.Folder, "README.md"), false) })
        {
            var json = GuideExample(guide);
            if (json == null)
                Fail($"{guide} (the modders' guide) wasn't copied next to the game");
            else if (json.Length == 0)
                Fail($"{guide} has no ```json example");
            else
            {
                try
                {
                    var name = isWeapon ? WeaponFile.Load(json, "README example").Name : AttachmentFile.Load(json, "README example").Name;
                    Line($"guide example '{name}' loads");
                }
                catch (WeaponFileException ex)
                {
                    Fail($"the example in {guide} doesn't load: {ex.Message}");
                }
            }
        }

        // ---- Broken files give useful messages ----
        const string ok = "\"name\": \"X\", \"class\": \"SMG\", \"damage\": 20, \"rangeM\": 10, \"rpm\": 600, \"mag\": 30, \"reload\": 2, \"hipSpreadDeg\": 2";
        foreach (var (bad, expectText, weapon) in new[]
                 {
                     ("{ \"name\": \"X\", \"class\": \"SMG\" }", "missing", true),
                     ("{ " + ok + ", \"damgae\": 5 }", "unknown field", true),
                     ("{ " + ok.Replace("\"rpm\": 600", "\"rpm\": 0") + " }", "between", true),
                     ("{ " + ok.Replace("\"SMG\"", "\"Laser\"") + " }", "isn't one of", true),
                     ("{ " + ok.Replace("\"X\"", "\"A=B\"") + " }", "name", true),
                     ("{ " + ok + ", }", "", true), // trailing commas are allowed
                     ("{ \"name\": \"X\", \"slot\": \"Muzzle\", \"effects\": [ { \"stat\": \"recoil\", \"mul\": 0.8 } ] }", "unknown stat", false),
                     ("{ \"name\": \"X\", \"slot\": \"Muzzle\", \"effects\": [ { \"stat\": \"recoilDeg\", \"mul\": 0.8, \"add\": 1 } ] }", "only one", false),
                     ("{ \"name\": \"X\", \"slot\": \"Magazine\", \"effects\": [ { \"stat\": \"reload\", \"grow\": 2 } ] }", "grow", false),
                     ("{ \"name\": \"X\", \"slot\": \"Hat\" }", "isn't one of", false),
                     ("{ \"name\": \"X\", \"slot\": \"Stock\", \"fits\": { \"clases\": [\"SMG\"] } }", "unknown rule", false),
                 })
        {
            try
            {
                if (weapon)
                    WeaponFile.Load(bad, "bad.json");
                else
                    AttachmentFile.Load(bad, "bad.json");
                if (expectText.Length > 0)
                    Fail($"a broken {(weapon ? "weapon" : "attachment")} file loaded without complaint (expected '{expectText}')");
            }
            catch (WeaponFileException ex) when (expectText.Length > 0 && ex.Message.Contains(expectText, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (Exception ex)
            {
                Fail($"{(weapon ? "weapon" : "attachment")} file gave the wrong error (expected '{expectText}'): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// TEMPORARY: compares every field of every weapon file with the old code catalogue, and every attachment's
    /// text, slot, index, unlock order, fit and effect on every weapon with the old code version.
    /// </summary>
    private static void CompareWithBuiltInCatalogue()
    {
        var differences = 0;
        void Diff(string s)
        {
            differences++;
            Fail(s);
        }
        var code = Weapons.BuiltInCatalogue();
        if (code.Length != Weapons.LegacyNames.Length)
            Diff($"the code catalogue has {code.Length} weapons but the legacy id table has {Weapons.LegacyNames.Length}");
        foreach (var c in code)
        {
            if (c.Id >= Weapons.LegacyNames.Length || Weapons.LegacyNames[c.Id] != c.Name)
                Diff($"legacy id table: id {c.Id} should be '{c.Name}'");
            if (Weapons.Find(c.Name) is not { } f)
            {
                Diff($"no weapon file for '{c.Name}'");
                continue;
            }
            foreach (var d in WeaponDiffs(c, f, includeId: true))
                Diff($"{c.Name}: file differs from code: {d}");
        }
        var codeAtt = Attachments.BuiltInCatalogue();
        foreach (var ca in codeAtt)
        {
            if (Attachments.Find(ca.Name) is not { } fa)
            {
                Diff($"no attachment file for '{ca.Name}'");
                continue;
            }
            if (fa.Slot != ca.Slot || fa.Index != ca.Index || fa.UnlockOrder != ca.UnlockOrder || fa.Blurb != ca.Blurb
                || !fa.Pros.SequenceEqual(ca.Pros) || !fa.Cons.SequenceEqual(ca.Cons))
                Diff($"{ca.Name}: slot/index/unlock order/text differ from code ({fa.Slot} {fa.Index} {fa.UnlockOrder} vs {ca.Slot} {ca.Index} {ca.UnlockOrder})");
            foreach (var w in code)
            {
                if (fa.Fits(w) != ca.Fits(w))
                {
                    Diff($"{ca.Name}: fits the {w.Name} is {fa.Fits(w)} in the file but {ca.Fits(w)} in code");
                    continue;
                }
                if (!ca.Fits(w))
                    continue;
                var x = w.Clone();
                var y = w.Clone();
                ca.Apply(x);
                fa.Apply(y);
                foreach (var d in WeaponDiffs(x, y, includeId: true))
                    Diff($"{ca.Name} on {w.Name}: file effect differs from code: {d}");
            }
        }
        if (Attachments.BySlot.Sum(x => x.Length) != codeAtt.Length)
            Diff($"{Attachments.BySlot.Sum(x => x.Length)} attachment files but {codeAtt.Length} attachments in code");
        // Each weapon's unlock sequence (and so MaxXp and every UnlockXp) is the same.
        var oldOrder = codeAtt.OrderBy(a => a.UnlockOrder).ToArray();
        foreach (var w in code)
        {
            var before = oldOrder.Where(a => a.Fits(w)).Select(a => a.Name).ToArray();
            var f = Weapons.Find(w.Name);
            if (f == null)
                continue;
            var after = Attachments.For(f).Select(a => a.Name).ToArray();
            if (!before.SequenceEqual(after))
                Diff($"{w.Name}: attachment unlock order changed: {string.Join(", ", after)}");
            var oldMax = before.Length == 0 ? 0 : (2000 + 260 * before.Length) / 50 * 50;
            if (Attachments.MaxXp(f) != oldMax)
                Diff($"{w.Name}: MaxXp {Attachments.MaxXp(f)} (was {oldMax})");
        }
        Line($"files vs the old code catalogue: {code.Length} weapons, {codeAtt.Length} attachments, {differences} difference(s)");
    }

    // ---- 5. Packets ----------------------------------------------------------------------

    private static void CheckPackets()
    {
        var w = new PacketWriter();
        w.Begin(Msg.State).U32(1).U32(1).U8(0).F32(1).U8(0).U8(8);
        for (var i = 0; i < 8; i++)
        {
            w.U8((byte)i).Vec(Vector3.One).Vec(Vector3.One).F32(0).F32(0).U8(100).U8(1).U16(0).U16(0).U8(0).U8(0).U8(0)
                .U24(0xABCDEF).U24(0x123456).U8(3).Vec(Vector3.One).U8(1).U8(2).U8(3).U8(4);
        }
        var r = new PacketReader(w.Span[1..]);
        r.U32();
        r.U32();
        r.U8();
        r.F32();
        r.U8();
        r.U8();
        for (var i = 0; i < 8; i++)
        {
            r.U8(); r.Vec(); r.Vec(); r.F32(); r.F32(); r.U8(); r.U8(); r.U16(); r.U16(); r.U8(); r.U8(); r.U8();
            var k1 = r.U24();
            var k2 = r.U24();
            r.U8();
            r.Vec();
            r.U8(); r.U8(); r.U8(); r.U8();
            if (k1 != 0xABCDEF || k2 != 0x123456)
                Fail($"U24 round trip broke: {k1:X} {k2:X}");
        }
        var perPlayer = (w.Length - 16) / 8;
        Line($"full 8-player snapshot: {w.Length} bytes (limit ~1400), {perPlayer} per player");
        if (perPlayer != Game.SnapPlayerBytes)
            Fail($"snapshot writes {perPlayer} bytes per player but the reader expects {Game.SnapPlayerBytes}");
        if (w.Length > 1200)
            Fail("snapshot too big for one packet");
    }
}
