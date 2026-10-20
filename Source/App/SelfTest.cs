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
///   6. Recoil: every weapon's profile is valid and deterministic, the classes feel as intended, recovery and
///      attachments work, and the visual shake never moves the aim.
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
            Section("Map packs", CheckMapPacks);
            Section("Mantling", CheckMantle);
            Section("Attachments", CheckAttachments);
            Section("Packets", CheckPackets);
            Section("Loadouts and perks", CheckLoadoutsAndPerks);
            Section("Melee", CheckMelee);
            Section("Matches", CheckMatches);
            Section("Map vote", CheckMapVote);
            Section("Server browser", CheckServerBrowser);
            Section("HUD layout", CheckHudLayout);
            Section("Weapon select", CheckWeaponSelect);
            Section("Loadout navigation", CheckLoadoutNav);
            Section("Recoil", CheckRecoil);
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

    // ---- 1d. Map packs -------------------------------------------------------------------------

    /// <summary>
    /// Pack folders: the shipped Classic pack, pack.json never loading as a map, folders without pack.json, loose files
    /// forming "Other", random picks and vote options staying inside the pack. Uses a throwaway folder in the temp
    /// directory (deleted afterwards), never the game's own Maps folder.
    /// </summary>
    private static void CheckMapPacks()
    {
        // The real Maps folder
        var classic = Maps.FindPack("Classic");
        if (Directory.Exists(Maps.Folder))
        {
            if (classic == null)
                Fail("no Classic pack (Maps/Classic)");
            else if (classic.Maps.Count != 24)
                Fail($"Classic has {classic.Maps.Count} maps (24 ship with the game)");
            else if (classic.Description.Length == 0 || classic.Order != 1)
                Fail("Classic/pack.json wasn't read (no description or order)");
        }
        if (Maps.All.Any(m => m.Pack == null || !m.Pack.Maps.Contains(m)))
            Fail("a loaded map has no pack");
        if (Maps.Packs.Sum(p => p.Maps.Count) != Maps.All.Length)
            Fail("the packs don't add up to the map list");
        if (Maps.All.Any(m => m.SourceFile != null && Path.GetFileName(m.SourceFile).Equals(MapPack.FileName, StringComparison.OrdinalIgnoreCase)))
            Fail("a pack.json was loaded as a map");
        Line("packs: " + string.Join("; ", Maps.Packs.Select(p => $"{p.Name} ({p.SizeSummary}){(p.Folder != null ? "" : " [loose files]")}")));

        // A made-up Maps folder
        var dir = Path.Combine(Path.GetTempPath(), "callofslop-packtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            static string MapJson(string name, float half) => $$"""
                { "name": "{{name}}", "halfX": {{half}}, "halfZ": {{half}}, "ops": [
                  { "op": "Ground", "color": [0.4, 0.4, 0.4] },
                  { "op": "Prop", "x": 0, "z": 0, "w": 2, "h": 1, "d": 2, "color": "#f28026" },
                  { "op": "RingSpawns", "count": 8, "frac": 0.8 }, { "op": "AutoWaypoints", "spacing": 6 } ] }
                """;
            void Write(string rel, string text)
            {
                var path = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            Write("Alpha/pack.json", """{ "format": "call-of-slop-pack/1", "name": "Alpha Pack", "description": "Test pack.", "author": "Self-test", "order": 5 }""");
            Write("Alpha/a1.json", MapJson("Test A1", 20));
            Write("Alpha/deeper/a2.json", MapJson("Test A2", 50)); // nested: still Alpha
            Write("Alpha/a3.json", MapJson("Test A3", 100));
            Write("Beta/b1.json", MapJson("Test B1", 20)); // no pack.json: named after the folder
            Write("Gamma/pack.json", "{ this is not json");      // broken pack.json: still a pack, with an error
            Write("Gamma/g1.json", MapJson("Test G1", 20));
            Write("loose.json", MapJson("Test Loose", 20));      // loose file: the Other pack
            Write("b1-leftover.json", MapJson("Test B1", 20));   // identical loose copy (old build output): ignored quietly

            var errors = new List<string>();
            var (all, packs) = Maps.LoadFolder(dir, errors);
            Line($"test folder: {all.Length} maps; packs {string.Join(", ", packs.Select(p => $"{p.Name} [{string.Join(", ", p.Maps.Select(m => m.Name))}]"))}; errors: {string.Join(" | ", errors)}");
            var names = packs.Select(p => p.Name).ToArray();
            if (!names.SequenceEqual(["Alpha Pack", "Beta", "Gamma", MapPack.LooseName]))
                Fail($"test packs came out as {string.Join(", ", names)} (expected Alpha Pack, Beta, Gamma, {MapPack.LooseName})");
            if (all.Length != 6)
                Fail($"test folder loaded {all.Length} maps (expected 6: pack.json files and the leftover copy aren't maps)");
            if (errors.Count != 1 || !errors[0].Contains("Gamma", StringComparison.Ordinal))
                Fail($"expected exactly one error, for Gamma/pack.json; got {errors.Count}");
            var alpha = packs.FirstOrDefault(p => p.Name == "Alpha Pack");
            var beta = packs.FirstOrDefault(p => p.Name == "Beta");
            var loose = packs.FirstOrDefault(p => p.Name == MapPack.LooseName);
            if (alpha is not { Maps.Count: 3, Order: 5, Author: "Self-test", Folder: "Alpha" })
                Fail("Alpha's pack.json fields or its nested map didn't load");
            if (loose == null || loose.Maps.Count != 1 || loose.Maps[0].Name != "Test Loose" || loose.Folder != null)
                Fail("the loose map file didn't become the Other pack");
            if (beta == null || beta.Maps.Count != 1 || all.Any(m => m.Pack == null || !m.Pack.Maps.Contains(m)))
                Fail("maps and packs don't point at each other");

            if (alpha != null && beta != null)
            {
                // Random picks stay inside the selected pack; a pack that's gone falls back to every map.
                var rng = new Random(1);
                var s = new Settings { MapRandom = true, MapPack = "alpha pack" };
                var picks = Enumerable.Range(0, 200).Select(_ => s.PickMap(packs, all, rng)).ToList();
                if (picks.Any(m => m.Pack != alpha) || picks.Distinct().Count() != 3)
                    Fail("random map picked outside the pack (or never picked some of its maps)");
                s.MapPack = "No Such Pack";
                if (Enumerable.Range(0, 200).Select(_ => s.PickMap(packs, all, rng)).Distinct().Count() != all.Length)
                    Fail("a missing pack didn't fall back to all maps");
                s.MapRandom = false;
                s.MapName = "Test B1";
                if (s.PickMap(packs, all, rng).Name != "Test B1")
                    Fail("a chosen map wasn't the one started");

                // Vote options: the pack just played; a pack without 2 other maps falls back to everything.
                var pool = Game.VotePool("Test A1", alpha, all);
                if (pool.Count != 2 || pool.Any(m => m.Pack != alpha || m.Name == "Test A1"))
                    Fail($"vote options left the pack: {string.Join(", ", pool.Select(m => m.Name))}");
                if (Game.VotePool("Test B1", beta, all).Count != all.Length - 1)
                    Fail("a one-map pack didn't fall back to all maps for the vote");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }

        // The same with the real packs
        if (classic != null)
        {
            var s = new Settings { MapRandom = true, MapPack = classic.Name };
            if (Enumerable.Range(0, 200).Any(_ => s.PickMap().Pack != classic))
                Fail("PickMap left the Classic pack");
            var played = classic.Maps[0];
            var pool = Game.VotePool(played.Name, classic, Maps.All);
            if (pool.Count != classic.Maps.Count - 1 || pool.Any(m => m.Pack != classic))
                Fail("Classic vote options left the pack");
            Line($"PickMap and vote options stay inside {classic.Name} ({pool.Count} vote candidates after {played.Name})");
        }
    }

    // ---- 2. Matches ----------------------------------------------------------------------

    private static void CheckMatches()
    {
        var botStats = new BotStats();
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
            var slowTicks = 0; // ticks over the 16 ms budget (one-off spikes from GC or a busy CI machine are fine)
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
                    {
                        maxTickMs = Math.Max(maxTickMs, ms);
                        if (ms > 16)
                            slowTicks++;
                    }
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

            botStats.Add(game.BotStats);
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
            // A real performance problem shows up as many slow ticks or a huge one; a single spike on a shared
            // CI machine doesn't. (The old rule failed on any tick over 8 ms and was flaky on GitHub's runners.)
            if (slowTicks > 54 || maxTickMs > 100)
                Fail($"{m.Name}: {slowTicks} server ticks went over the 16 ms budget (worst {maxTickMs:0.0} ms)");
            else if (maxTickMs > 16)
                Line($"  note: {slowTicks} tick(s) over 16 ms, worst {maxTickMs:0.0} ms (within tolerance)");
            if (game.Local is { Alive: true } && game.MatchNumber == 1)
                Fail($"{m.Name}: the host was respawned without clicking Respawn");
        }
        Line($"BOTSTAT headshots: {botStats.GunHeadshots} of {botStats.GunKills} bot gun kills ({botStats.HeadshotRatio:P1})");
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
            if (start.Pack is { Maps.Count: >= 3 } pack && ballot.Any(id => Maps.Get(id).Pack != pack))
                Fail($"the ballot offered maps from outside the {pack.Name} pack");
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
        if (!WeaponFile.ToJson(Weapons.All[0]).Contains("\"recoil\": {", StringComparison.Ordinal))
            Fail("a written weapon file has no \"recoil\" block");

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
                     ("{ " + ok + ", \"recoil\": { \"vertcal\": 1 } }", "unknown recoil field", true),
                     ("{ " + ok + ", \"recoil\": { \"vertical\": 9 } }", "between", true),
                     ("{ " + ok + ", \"recoil\": 2 }", "expected", true),
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

    // ---- 6. Recoil ---------------------------------------------------------------------------

    /// <summary>Result of a simulated spray: the aim kicks per shot, the highest climb, and the aim after letting go.</summary>
    private sealed record SprayResult(List<(float Pitch, float Yaw)> Kicks, float Peak, float FinalPitch, float FinalYaw, bool Finite);

    /// <summary>
    /// Holds the trigger for <paramref name="shots"/> rounds (semi guns at their max rate, bursts with their pause) with a
    /// fixed frame time, then waits <paramref name="hold"/> seconds. Starts aiming at pitch 0, yaw 0.
    /// </summary>
    private static SprayResult SimulateSpray(WeaponDef d, Stance stance, float aimT, int shots, float dt, float hold = 2f, RecoilState? state = null)
    {
        var r = state ?? new RecoilState();
        float pitch = 0, yaw = 0, peak = 0;
        var finite = true;
        var kicks = new List<(float, float)>();
        double t = 10, next = t;
        var fired = 0;
        while (fired < shots)
        {
            while (fired < shots && t >= next)
            {
                var (kp, ky) = r.Shot(d, stance, aimT, t, pitch, yaw);
                kicks.Add((kp, ky));
                pitch += kp;
                yaw += ky;
                fired++;
                next += d.FireInterval;
                if (d.Mode == FireMode.Burst && fired % d.BurstCount == 0)
                    next += d.BurstDelay;
            }
            r.Update(dt, t, ref pitch, ref yaw);
            peak = MathF.Max(peak, pitch);
            t += dt;
        }
        for (var e = 0f; e < hold; e += dt, t += dt)
        {
            r.Update(dt, t, ref pitch, ref yaw);
            finite &= float.IsFinite(r.Camera.Pitch + r.Camera.Yaw + r.Camera.Roll + r.Camera.Fov + r.Kick.Back + r.Kick.Up + r.Kick.Roll + r.Kick.Side);
        }
        finite &= float.IsFinite(pitch) && float.IsFinite(yaw) && float.IsFinite(peak);
        return new SprayResult(kicks, peak, pitch, yaw, finite);
    }

    private static float Variance(IEnumerable<float> xs)
    {
        var a = xs.ToArray();
        var m = a.Average();
        return a.Sum(x => (x - m) * (x - m)) / a.Length;
    }

    private static void CheckRecoil()
    {
        const float dt = 1f / 240f;
        var all = Weapons.All;

        // ---- Every weapon has a valid profile (the shipped files spell theirs out) ----
        foreach (var w in all)
        {
            try
            {
                Recoil.Validate(w, w.Name);
            }
            catch (WeaponFileException ex)
            {
                Fail(ex.Message);
            }
            if (w.SourceFile != null && Weapons.LegacyNames.Contains(w.Name)
                && !File.ReadAllText(Path.Combine(Weapons.Folder, w.SourceFile)).Contains("\"recoil\"", StringComparison.Ordinal))
                Fail($"{w.Name}: the shipped file has no \"recoil\" block");
            if (w.RecoilDeg > 0 && Recoil.EffectiveKickDeg(w) <= 0)
                Fail($"{w.Name}: recoil profile has no kick at all");
        }
        // A file without a block gets its class's character (an LMG barely moves sideways when crouched).
        var bare = WeaponFile.Load("{ \"name\": \"X\", \"class\": \"LMG\", \"damage\": 20, \"rangeM\": 10, \"rpm\": 600, \"mag\": 100, \"reload\": 5, \"hipSpreadDeg\": 2 }", "bare.json");
        if (bare.RecoilCrouchHorizontal > 0.2f || bare.RecoilVertical < 1f)
            Fail($"an LMG file without a recoil block didn't get LMG defaults (crouch horizontal {bare.RecoilCrouchHorizontal})");
        var partial = WeaponFile.Load("{ \"name\": \"X\", \"class\": \"SMG\", \"damage\": 20, \"rangeM\": 10, \"rpm\": 600, \"mag\": 30, \"reload\": 2, \"hipSpreadDeg\": 2, \"recoil\": { \"vertical\": 0.5 } }", "partial.json");
        var bareSmg = WeaponFile.Load("{ \"name\": \"X\", \"class\": \"SMG\", \"damage\": 20, \"rangeM\": 10, \"rpm\": 600, \"mag\": 30, \"reload\": 2, \"hipSpreadDeg\": 2 }", "bare.json");
        if (partial.RecoilVertical != 0.5f || partial.RecoilJitter != bareSmg.RecoilJitter || partial.RecoilHorizontal != bareSmg.RecoilHorizontal)
            Fail("a partial recoil block didn't keep the class defaults for the fields it leaves out");

        // ---- Patterns are deterministic per seed ----
        foreach (var w in all)
        {
            for (var i = 0; i < 40; i++)
                if (Recoil.PatternDeg(w, i, 3) != Recoil.PatternDeg(w, i, 3))
                    Fail($"{w.Name}: shot {i} isn't deterministic");
            var calm = w.Clone();
            calm.RecoilJitter = 0;
            for (var i = 0; i < 40; i++)
                if (Recoil.PatternDeg(calm, i, 1) != Recoil.PatternDeg(calm, i, 7))
                {
                    Fail($"{w.Name}: the pattern (without jitter) changes between sprays at shot {i}");
                    break;
                }
            var a = SimulateSpray(w, Stance.Stand, 1, 30, dt);
            var b = SimulateSpray(w, Stance.Stand, 1, 30, dt);
            if (!a.Kicks.SequenceEqual(b.Kicks) || a.FinalPitch != b.FinalPitch)
                Fail($"{w.Name}: two identical sprays came out differently");
            if (!a.Finite)
                Fail($"{w.Name}: spray produced NaN or infinity");
        }
        if (Weapons.Find("Vanguard AR") is { } van)
        {
            var other = van.Clone();
            other.RecoilSeed = van.RecoilSeed + 1;
            if (Enumerable.Range(0, 20).All(i => Recoil.PatternDeg(van, i, 1) == Recoil.PatternDeg(other, i, 1)))
                Fail("a different seed gave the same pattern");
        }

        // ---- LMGs: crouched sideways movement is far below standing ----
        foreach (var w in all.Where(w => w.Class == WeaponClass.LMG))
        {
            var stand = SimulateSpray(w, Stance.Stand, 1, 30, dt).Kicks.Sum(k => MathF.Abs(k.Yaw));
            var crouch = SimulateSpray(w, Stance.Crouch, 1, 30, dt).Kicks.Sum(k => MathF.Abs(k.Yaw));
            var prone = SimulateSpray(w, Stance.Prone, 1, 30, dt).Kicks.Sum(k => MathF.Abs(k.Yaw));
            Line($"{w.Name}: sideways travel over 30 rounds aimed: standing {stand * 180 / MathF.PI:0.0} deg, crouched {crouch * 180 / MathF.PI:0.00}, prone {prone * 180 / MathF.PI:0.00}");
            if (!(crouch < stand * 0.25f && prone < stand * 0.15f))
                Fail($"{w.Name}: crouched/prone horizontal recoil isn't far below standing");
        }

        // ---- SMGs jump around sideways more than assault rifles ----
        float ClassHorizontalVariance(WeaponClass c) =>
            all.Where(w => w.Class == c).Select(w => Variance(SimulateSpray(w, Stance.Stand, 0, 30, dt).Kicks.Select(k => k.Yaw * 180 / MathF.PI)))
                .DefaultIfEmpty(0).Average();
        float ClassJitterRatio(WeaponClass c) =>
            all.Where(w => w.Class == c).Select(w =>
            {
                var k = SimulateSpray(w, Stance.Stand, 0, 30, dt).Kicks;
                return MathF.Sqrt(Variance(k.Select(x => x.Yaw))) / MathF.Max(1e-6f, k.Average(x => x.Pitch));
            }).DefaultIfEmpty(0).Average();
        var smgVar = ClassHorizontalVariance(WeaponClass.SMG);
        var arVar = ClassHorizontalVariance(WeaponClass.AssaultRifle);
        var smgRatio = ClassJitterRatio(WeaponClass.SMG);
        var arRatio = ClassJitterRatio(WeaponClass.AssaultRifle);
        Line($"per-shot sideways variance: SMGs {smgVar:0.000} deg^2, rifles {arVar:0.000} deg^2; sideways/up ratio SMGs {smgRatio:0.00}, rifles {arRatio:0.00}");
        if (!(smgVar > arVar && smgRatio > arRatio * 2))
            Fail("SMG horizontal recoil isn't more erratic than assault rifles'");

        // ---- Recovery returns most of the climb, never past where you started ----
        foreach (var w in all)
        {
            // One spray: guns too slow to chain shots into a spray (snipers, the musket) recover each shot on its own.
            var shots = w.FireInterval < Recoil.ResetTime(w) ? Math.Min(20, Math.Max(w.Mag, 3)) : 1;
            var sp = SimulateSpray(w, Stance.Stand, 1, shots, dt);
            // Of everything the gun kicked you up, how much came back (semi guns recover between shots too).
            var kicked = sp.Kicks.Sum(k => k.Pitch);
            var returned = kicked > 0 ? (kicked - sp.FinalPitch) / kicked : 1;
            if (w.RecoilRecoveryAmount >= 0.5f && returned < 0.5f)
                Fail($"{w.Name}: recovery only returned {returned:P0} of the climb");
            if (sp.FinalPitch < -1e-4f)
                Fail($"{w.Name}: recovery pulled the aim below where the spray started ({sp.FinalPitch})");
        }
        if (Weapons.Find("Vanguard AR") is { } ar)
        {
            // The player pulls down against the climb: recovery must not drag the aim further down.
            var st = new RecoilState();
            float pitch = 0, yaw = 0;
            for (var i = 0; i < 10; i++)
            {
                var (kp, ky) = st.Shot(ar, Stance.Stand, 1, 10 + i * ar.FireInterval, pitch, yaw);
                pitch += kp;
                yaw += ky;
            }
            pitch = 0; // pulled all the way back down by hand
            for (var t = 0f; t < 2; t += dt)
                st.Update(dt, 11 + t, ref pitch, ref yaw);
            if (pitch < -1e-4f)
                Fail($"recovery overshot after the player had already pulled down ({pitch})");
            // Frame-rate independence: the same spray at 30 and 300 fps ends up in the same place.
            var slow = SimulateSpray(ar, Stance.Stand, 1, 20, 1 / 30f);
            var fast = SimulateSpray(ar, Stance.Stand, 1, 20, 1 / 300f);
            if (MathF.Abs(slow.FinalPitch - fast.FinalPitch) > 0.05f * MathF.Max(slow.Peak, 1e-3f))
                Fail($"recovery depends on frame rate: {slow.FinalPitch:0.0000} at 30 fps vs {fast.FinalPitch:0.0000} at 300 fps");
            var hip = SimulateSpray(ar, Stance.Stand, 0, 30, dt);
            Line($"Vanguard AR, 30 rounds from the hip: climb {hip.Peak * 180 / MathF.PI:0.0} deg, back to {hip.FinalPitch * 180 / MathF.PI:0.0} deg 2 s after letting go");
        }

        // ---- The visual shake and punch never move the actual aim ----
        foreach (var w in all)
        {
            var still = w.Clone();
            still.RecoilRecoveryAmount = 0; // no recovery: any change to the aim after firing would come from the visuals
            var st = new RecoilState();
            float pitch = 0, yaw = 0;
            var (kp, ky) = st.Shot(still, Stance.Stand, 0, 10, pitch, yaw);
            pitch += kp;
            yaw += ky;
            float p0 = pitch, y0 = yaw;
            var moved = 0f;
            for (var t = 0f; t < 0.3f; t += dt)
            {
                st.Update(dt, 10 + t, ref pitch, ref yaw);
                moved = MathF.Max(moved, MathF.Abs(st.Camera.Pitch) + MathF.Abs(st.Camera.Yaw) + MathF.Abs(st.Camera.Roll));
            }
            if (pitch != p0 || yaw != y0)
                Fail($"{w.Name}: the camera shake changed the actual aim");
            if (w.RecoilDeg > 0 && (w.RecoilShake > 0 || w.RecoilPunch > 0) && moved <= 0)
                Fail($"{w.Name}: the gun has shake/punch but the camera never moved");
        }

        // ---- Attachments cut the axes they're meant to ----
        void Expect(string attachment, Func<WeaponDef, float> stat, bool lower, string what)
        {
            if (Attachments.Find(attachment) is not { } a)
            {
                Fail($"'{attachment}' isn't loaded");
                return;
            }
            var n = 0;
            foreach (var w in all.Where(a.Fits))
            {
                var k = Attachments.Apply(w, Attachments.With(0, a.Slot, a.Index));
                if (lower ? !(stat(k) < stat(w)) : !(stat(k) > stat(w)))
                    Fail($"{attachment} on {w.Name} doesn't {(lower ? "reduce" : "raise")} {what} ({stat(w):0.###} -> {stat(k):0.###})");
                n++;
            }
            if (n == 0)
                Fail($"'{attachment}' fits no weapon");
        }
        Expect("Compensator", Recoil.VerticalDeg, true, "vertical recoil");
        Expect("Vertical Grip", Recoil.VerticalDeg, true, "vertical recoil");
        Expect("Angled Grip", w => w.RecoilFirstShot, true, "first-shot kick");
        Expect("Muzzle Brake", Recoil.HorizontalDeg, true, "horizontal recoil");
        Expect("Muzzle Brake", w => w.RecoilShake, true, "camera shake");
        Expect("Heavy Stock", w => w.RecoilRecovery, false, "recovery speed");
        Expect("Heavy Stock", Recoil.EffectiveKickDeg, true, "recoil");
        Expect("Suppressor", w => w.RecoilShake, true, "camera shake");
        if (Attachments.Find("Compensator") is { } comp && Weapons.Find("Vanguard AR") is { } v2
            && Attachments.Apply(v2, Attachments.With(0, comp.Slot, comp.Index)) is var vc && Recoil.HorizontalDeg(vc) != Recoil.HorizontalDeg(v2))
            Fail("the Compensator changed horizontal recoil (it should only fight the climb)");

        // ---- Fully stacked kits stay sane: >= 40% recoil, >= 50% hip spread, mobility in bounds ----
        foreach (var w in all)
        {
            var list = Attachments.For(w);
            uint Greedy(Func<WeaponDef, float> score)
            {
                uint kit = 0;
                for (var sl = 0; sl < Attachments.SlotCount; sl++)
                {
                    var best = list.Where(a => (int)a.Slot == sl).OrderBy(a => score(Attachments.Apply(w, Attachments.With(0, a.Slot, a.Index)))).FirstOrDefault();
                    if (best != null && score(Attachments.Apply(w, Attachments.With(0, best.Slot, best.Index))) < score(w))
                        kit = Attachments.With(kit, best.Slot, best.Index);
                }
                return kit;
            }
            var lowRecoil = Attachments.Apply(w, Greedy(Recoil.EffectiveKickDeg));
            var lowSpread = Attachments.Apply(w, Greedy(x => x.HipSpreadDeg));
            var fast = Attachments.Apply(w, Greedy(x => -x.Mobility));
            var slow = Attachments.Apply(w, Greedy(x => x.Mobility));
            if (Recoil.VerticalDeg(lowRecoil) < Recoil.VerticalDeg(w) * 0.399f || Recoil.HorizontalDeg(lowRecoil) < Recoil.HorizontalDeg(w) * 0.399f)
                Fail($"{w.Name}: a fully kitted gun has under 40% of the stock recoil ({Attachments.Describe(lowRecoil.Kit)})");
            if (lowSpread.HipSpreadDeg < w.HipSpreadDeg * 0.499f)
                Fail($"{w.Name}: a fully kitted gun has under 50% of the stock hip spread ({Attachments.Describe(lowSpread.Kit)})");
            if (fast.Mobility > 1.25f || slow.Mobility < 0.6f)
                Fail($"{w.Name}: kitted mobility {slow.Mobility:0.00}-{fast.Mobility:0.00} is outside 0.6-1.25");
            if (StatBars.Control(lowRecoil) >= 1f || StatBars.Accuracy(lowSpread) >= 1f && StatBars.Accuracy(w) < 1f)
                Fail($"{w.Name}: a kitted extreme pins a stat bar at full, hiding the difference");
        }

        // ---- No NaNs anywhere, even with odd frame times ----
        foreach (var w in all)
            foreach (var stance in new[] { Stance.Stand, Stance.Crouch, Stance.Prone })
                foreach (var aim in new[] { 0f, 0.5f, 1f })
                {
                    var st = new RecoilState();
                    float pitch = 0.3f, yaw = 3.1f;
                    for (var i = 0; i < 60; i++)
                    {
                        var (kp, ky) = st.Shot(w, stance, aim, 10 + i * 0.05, pitch, yaw);
                        pitch += kp;
                        yaw = World.WrapAngle(yaw + ky);
                        st.Update(i % 7 == 0 ? 0f : i % 11 == 0 ? 0.5f : 0.004f, 10 + i * 0.05 + 0.01, ref pitch, ref yaw);
                    }
                    var (bp, by) = Recoil.BotKick(w, new Random(1));
                    if (!float.IsFinite(pitch + yaw + bp + by + st.Camera.Pitch + st.Camera.Yaw + st.Camera.Roll + st.Camera.Fov + st.Kick.Back + st.Kick.Up + st.Kick.Roll + st.Kick.Side))
                        Fail($"{w.Name}: NaN or infinity in recoil ({stance}, aim {aim})");
                }
        Line($"{all.Length} recoil profiles checked");
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

    // ---- HUD layout ------------------------------------------------------------------------

    /// <summary>The HUD safe area: centred, full height, width capped by the HUD width setting.</summary>
    private static void CheckHudLayout()
    {
        void Expect(float w, float h, HudWidth mode, float left, float width)
        {
            var (l, sw) = HudLayout.Span(w, h, mode);
            Line($"  {w}x{h} {HudLayout.Label(mode)}: left {l}, width {sw}");
            if (MathF.Abs(l - left) > 0.5f || MathF.Abs(sw - width) > 0.5f)
                Fail($"{w}x{h} at {HudLayout.Label(mode)}: got left {l} width {sw}, expected left {left} width {width}");
        }

        // 16:9 screen: the safe area is the whole screen whatever the setting.
        Expect(1920, 1080, HudWidth.Wide16x9, 0, 1920);
        Expect(1920, 1080, HudWidth.Wide21x9, 0, 1920);
        Expect(1920, 1080, HudWidth.Full, 0, 1920);
        // 3440x1440 (Dell AW3420DW, really 21.5:9): 2560 wide at 16:9, 3360 at 21:9.
        Expect(3440, 1440, HudWidth.Wide16x9, 440, 2560);
        Expect(3440, 1440, HudWidth.Wide21x9, 40, 3360);
        Expect(3440, 1440, HudWidth.Full, 0, 3440);
        // 32:9 (Samsung Odyssey G95NC): 2560 at 16:9, 3360 at 21:9.
        Expect(5120, 1440, HudWidth.Wide16x9, 1280, 2560);
        Expect(5120, 1440, HudWidth.Wide21x9, 880, 3360);
        Expect(5120, 1440, HudWidth.Full, 0, 5120);
        // Portrait / narrow windows: the whole screen.
        Expect(1080, 1920, HudWidth.Wide16x9, 0, 1080);
        Expect(800, 900, HudWidth.Wide21x9, 0, 800);
        // Degenerate (minimised window): no NaN, no negative width.
        var (dl, dw) = HudLayout.Span(0, 0, HudWidth.Wide16x9);
        if (float.IsNaN(dl) || float.IsNaN(dw) || dw < 0)
            Fail($"0x0 screen gave left {dl} width {dw}");

        // Default (settings files from before this option have no hud_width line, so they keep it).
        if (new Settings().HudWidth != HudWidth.Wide16x9)
            Fail("HUD width should default to 16:9");
    }

    // ---- Weapon select ---------------------------------------------------------------------

    /// <summary>The weapon-select screen's tabs hold every weapon exactly once, and its stat comparison points the right way.</summary>
    /// <summary>The loadout screen's page stack: Back goes back one page, and from the overview it signals "close".</summary>
    private static void CheckLoadoutNav()
    {
        var nav = new LoadoutNav();
        void Expect(LoadoutPage page, int depth, string what)
        {
            if (nav.Current != page || nav.Depth != depth)
                Fail($"{what}: expected {page} at depth {depth}, got {nav.Current} at depth {nav.Depth}");
        }
        Expect(LoadoutPage.Overview, 1, "new stack");
        if (!nav.Back())
            Fail("Back on the overview should signal close");
        Expect(LoadoutPage.Overview, 1, "after Back on the overview");

        // Overview > weapon select > attachments: Back returns to the weapon select, then the overview, then closes.
        nav.Open(LoadoutPage.Primary);
        nav.Open(LoadoutPage.Attachments, 7);
        Expect(LoadoutPage.Attachments, 3, "weapon select > attachments");
        if (nav.Arg != 7)
            Fail($"attachments page lost its weapon id (got {nav.Arg})");
        if (nav.Back())
            Fail("Back from attachments closed the screen");
        Expect(LoadoutPage.Primary, 2, "Back from attachments opened via the weapon select");
        nav.Back();
        Expect(LoadoutPage.Overview, 1, "Back from the weapon select");
        if (!nav.Back())
            Fail("Back on the overview (after a round trip) should signal close");

        // Overview card's "Edit attachments": Back returns straight to the overview.
        nav.Open(LoadoutPage.Attachments, 3);
        nav.Back();
        Expect(LoadoutPage.Overview, 1, "Back from attachments opened from a card");

        // Progression returns to whatever was before it.
        nav.Open(LoadoutPage.Secondary);
        nav.Open(LoadoutPage.Attachments, 2);
        nav.Open(LoadoutPage.Progression);
        Expect(LoadoutPage.Progression, 4, "progression over attachments");
        nav.Back();
        Expect(LoadoutPage.Attachments, 3, "Back from progression");
        if (nav.Arg != 2)
            Fail("the attachments page under progression lost its weapon id");

        // Re-opening a page already on the stack goes back down to it (no loops, bounded depth).
        nav.Open(LoadoutPage.Progression);
        nav.Open(LoadoutPage.Progression);
        Expect(LoadoutPage.Progression, 4, "progression opened twice");
        nav.Open(LoadoutPage.Secondary);
        Expect(LoadoutPage.Secondary, 2, "re-opening the weapon select");
        for (var i = 0; i < 50; i++)
            nav.Open(i % 2 == 0 ? LoadoutPage.Attachments : LoadoutPage.Progression, i);
        if (nav.Depth > 4)
            Fail($"stack grew to {nav.Depth} pages after alternating opens");

        // Opening the overview (or Reset) clears everything.
        nav.Open(LoadoutPage.Overview);
        Expect(LoadoutPage.Overview, 1, "opening the overview");
        nav.Open(LoadoutPage.Perks);
        nav.Reset();
        Expect(LoadoutPage.Overview, 1, "Reset");
        Line("  push/pop order, attachments > weapon select > overview > close, progression > previous page: ok");
    }

    private static void CheckWeaponSelect()
    {
        var seen = new int[Weapons.All.Length];
        foreach (var slot in new[] { Slot.Primary, Slot.Secondary })
        {
            var tabs = WeaponSelect.Tabs(slot);
            Line($"  {slot}: {string.Join(", ", tabs.Select(c => $"{WeaponSelect.TabName(c)} {WeaponSelect.InTab(c).Length} ({WeaponSelect.LockedCount(c, 1)} locked at level 1)"))}");
            if (tabs.Length == 0)
                Fail($"{slot} slot has no tabs");
            if (tabs.Distinct().Count() != tabs.Length)
                Fail($"{slot} slot has a tab twice");
            foreach (var c in tabs)
            {
                var guns = WeaponSelect.InTab(c);
                if (guns.Length == 0)
                    Fail($"empty tab {WeaponSelect.TabName(c)}");
                for (var i = 0; i < guns.Length; i++)
                {
                    var w = guns[i];
                    seen[w.Id]++;
                    if (w.Class != c || w.Slot != slot)
                        Fail($"{w.Name} ({w.ClassName}, {w.Slot}) is in the {slot} tab {WeaponSelect.TabName(c)}");
                    if (i > 0 && WeaponSelect.UnlockLevel(guns[i - 1]) > WeaponSelect.UnlockLevel(w))
                        Fail($"tab {WeaponSelect.TabName(c)} isn't sorted by unlock level ({guns[i - 1].Name} before {w.Name})");
                    if (WeaponSelect.UnlockLevel(w) != Progress.UnlockLevel(w))
                        Fail($"{w.Name}: cached unlock level {WeaponSelect.UnlockLevel(w)} != {Progress.UnlockLevel(w)}");
                }
            }
        }
        if (WeaponSelect.Tabs(Slot.Secondary) is not [WeaponClass.Pistol])
            Fail("the secondary slot should have just the Pistols tab");
        if (WeaponSelect.Tabs(Slot.Primary).Contains(WeaponClass.Pistol))
            Fail("pistols showing in the primary tabs");
        for (var id = 0; id < seen.Length; id++)
            if (seen[id] != 1)
                Fail($"{Weapons.All[id].Name} appears in {seen[id]} tabs (should be exactly 1)");

        // Scores stay in 0-1; comparing a gun with itself shows no change.
        foreach (var w in Weapons.All)
            for (var i = 0; i < WeaponSelect.StatCount; i++)
            {
                var s = (SelectStat)i;
                var v = WeaponSelect.Score(w, s);
                if (!(v >= 0 && v <= 1))
                    Fail($"{w.Name}: {WeaponSelect.StatName(s)} score {v} outside 0-1");
                if (WeaponSelect.Delta(w, w, s) != 0)
                    Fail($"{w.Name}: {WeaponSelect.StatName(s)} differs from itself");
            }

        // Deltas point the right way: better is positive whichever way the raw number goes.
        var baseGun = Weapons.DefaultPrimary.Clone();
        baseGun.Damage = 25;
        baseGun.Pellets = 1;
        baseGun.Rpm = 600;
        baseGun.RangeM = 30;
        baseGun.HipSpreadDeg = 3f;
        baseGun.RecoilDeg = 2f;
        baseGun.Mobility = 0.95f;
        baseGun.EquipTime = 0.5f;
        baseGun.AdsTime = 0.2f;
        void Expect(string what, Action<WeaponDef> change, SelectStat s, int sign)
        {
            var other = baseGun.Clone();
            change(other);
            var d = WeaponSelect.Delta(baseGun, other, s);
            Line($"  {what}: {WeaponSelect.StatName(s)} {(d > 0 ? "+" : "")}{d}");
            if (Math.Sign(d) != sign)
                Fail($"{what}: {WeaponSelect.StatName(s)} delta {d}, expected sign {sign}");
        }
        Expect("more damage", w => w.Damage = 35, SelectStat.Damage, 1);
        Expect("8 pellets", w => w.Pellets = 8, SelectStat.Damage, 1);
        Expect("slower fire", w => w.Rpm = 400, SelectStat.FireRate, -1);
        Expect("longer range", w => w.RangeM = 45, SelectStat.Range, 1);
        Expect("wider spread", w => w.HipSpreadDeg = 5f, SelectStat.Accuracy, -1);
        Expect("less recoil", w => w.RecoilDeg = 1f, SelectStat.Control, 1);
        Expect("faster movement", w => w.Mobility = 1.1f, SelectStat.Mobility, 1);
        Expect("slower aim", w => w.AdsTime = 0.35f, SelectStat.Handling, -1);
        Expect("quicker draw", w => w.EquipTime = 0.3f, SelectStat.Handling, 1);
        Expect("same gun", _ => { }, SelectStat.Range, 0);

        // Weapon "level" = attachments unlocked by weapon XP.
        var kitGun = Weapons.All.FirstOrDefault(w => Attachments.For(w).Count > 1);
        if (kitGun != null)
        {
            var max = Attachments.MaxXp(kitGun);
            var n = Attachments.For(kitGun).Count;
            if (WeaponSelect.UnlockedAttachments(kitGun, 0) != 0 || WeaponSelect.UnlockedAttachments(kitGun, max) != n)
                Fail($"{kitGun.Name}: unlocked attachments at 0 / max XP are {WeaponSelect.UnlockedAttachments(kitGun, 0)} / {WeaponSelect.UnlockedAttachments(kitGun, max)}, expected 0 / {n}");
            var mid = WeaponSelect.UnlockedAttachments(kitGun, max / 2);
            if (mid <= 0 || mid >= n)
                Fail($"{kitGun.Name}: {mid}/{n} attachments at half XP");
            Line($"  {kitGun.Name}: {mid}/{n} attachments at {max / 2} of {max} weapon XP");
        }
    }
}
