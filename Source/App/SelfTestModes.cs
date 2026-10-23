using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>Self-test sections for game modes, map objectives and ready-up (offline games, mostly driven directly).</summary>
public static partial class SelfTest
{
    private static Game ModeGame(int bots, GameMode mode, byte mapId = 0)
    {
        World.SetMap(Maps.Get(mapId));
        var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: bots, mapId: mapId)
        {
            MapVoteEnabled = false,
        };
        game.Mode = mode;
        return game;
    }

    private static void Run(Game game, ref double now, double seconds)
    {
        const double dt = 1.0 / World.TickRate;
        for (var t = 0.0; t < seconds; t += dt)
        {
            now += dt;
            game.Update(now, dt);
        }
    }

    // ---- Map objectives -----------------------------------------------------------------------

    private static void CheckMapObjectives()
    {
        var problems = 0;
        foreach (var m in Maps.All)
        {
            var o = m.Objectives;
            var again = MapObjectives.Build(m);
            var same = o.TeamSpawns[0].SequenceEqual(again.TeamSpawns[0]) && o.TeamSpawns[1].SequenceEqual(again.TeamSpawns[1])
                       && o.Hills.SequenceEqual(again.Hills) && o.Flags.SequenceEqual(again.Flags);
            if (!same)
                Fail($"{m.Name}: objectives aren't deterministic");
            if (o.TeamSpawns[0].Count == 0 || o.TeamSpawns[1].Count == 0 || (o.TeamSpawnsGenerated && o.TeamSpawns[0].Count + o.TeamSpawns[1].Count != m.Spawns.Count))
                Fail($"{m.Name}: bad team spawns ({o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} of {m.Spawns.Count})");
            if (Math.Abs(o.TeamSpawns[0].Count - o.TeamSpawns[1].Count) > 1 && o.TeamSpawnsGenerated)
                Fail($"{m.Name}: team spawns uneven");
            if (o.Hills.Count == 0 || o.Hills.Any(h => m.IsBlocked(h.Center)))
                Fail($"{m.Name}: no hill, or a hill inside geometry");
            if (o.Flags.Any(f => m.IsBlocked(f)))
                Fail($"{m.Name}: a flag is inside geometry");
            var flagGap = m.PointGravity != null ? Vector3.Distance(o.Flags[0], o.Flags[1]) // a planet: straight through it (MapPlanet.cs)
                : new Vector2(o.Flags[0].X - o.Flags[1].X, o.Flags[0].Z - o.Flags[1].Z).Length();
            var span = MathF.Min(m.HalfX, m.HalfZ) * 2;
            if (flagGap < span * 0.35f)
            {
                problems++;
                Fail($"{m.Name}: the flags are only {flagGap:0} m apart");
            }
            // The two teams' halves should really be apart: centroids well separated.
            var c0 = o.TeamSpawns[0].Aggregate(Vector3.Zero, (a, b) => a + b) / o.TeamSpawns[0].Count;
            var c1 = o.TeamSpawns[1].Aggregate(Vector3.Zero, (a, b) => a + b) / o.TeamSpawns[1].Count;
            if (Vector3.Distance(c0, c1) < span * 0.2f)
                Fail($"{m.Name}: team spawn groups overlap (centres {Vector3.Distance(c0, c1):0} m apart)");
            if (m.Id % 6 == 0)
                Line($"{m.Name,-14} teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count}  hills {o.Hills.Count} ({string.Join(" ", o.Hills.Select(h => $"({h.Center.X:0},{h.Center.Z:0}) r{h.Radius:0.#}"))})  flags {flagGap:0} m apart");
        }
        Line($"objectives generated for {Maps.All.Length} maps");

        // Hand-placed objective fields survive a file round trip and change the fingerprint.
        var src = Maps.All[0];
        var copy = MapFile.Load(MapFile.ToJson(src), "objective copy");
        var plain = copy.ContentHash;
        copy.TeamSpawnData = [src.Objectives.TeamSpawns[1].ToList(), src.Objectives.TeamSpawns[0].ToList()];
        copy.HillData = [new HillZone(new Vector3(1, 0, 2), 5f), new HillZone(new Vector3(-3, 0, -4), 3.5f)];
        copy.FlagData = [new Vector3(5, 0, 6), new Vector3(-5, 0, -6)];
        copy.InvalidateObjectives();
        var back = MapFile.Load(MapFile.ToJson(copy), "objective round trip");
        if (back.TeamSpawnData is not { Length: 2 } || !back.TeamSpawnData[0].SequenceEqual(copy.TeamSpawnData[0]) || !back.TeamSpawnData[1].SequenceEqual(copy.TeamSpawnData[1])
            || back.HillData == null || !back.HillData.SequenceEqual(copy.HillData) || back.FlagData == null || !back.FlagData.SequenceEqual(copy.FlagData))
            Fail("hand-placed objectives didn't survive a map file round trip");
        if (back.Objectives.HillsGenerated || back.Objectives.Hills[1].Radius != 3.5f || back.Objectives.Flags[0] != new Vector3(5, 0, 6))
            Fail("hand-placed objectives weren't used");
        if (back.ContentHash != copy.ContentHash || copy.ContentHash == plain)
            Fail("objective fields aren't part of the map fingerprint");
        try
        {
            MapFile.Load("""{ "name": "X", "halfX": 20, "halfZ": 20, "flags": [[1, 2, 3]], "ops": [ { "op": "Spawn", "x": 1, "z": 1 } ] }""", "bad flags");
            Fail("a map with one flag loaded without complaint");
        }
        catch (MapFileException)
        {
        }
        Line("hand-placed teamSpawns / hills / flags round-trip and are fingerprinted");
    }

    // ---- Ready-up -------------------------------------------------------------------------------

    private static void CheckReadyUp()
    {
        var now = 0.0;
        // Single player: Ready starts the countdown right away; everyone is put at a spawn, alive.
        using (var game = ModeGame(3, GameMode.FreeForAll))
        {
            game.WarmupEnabled = true;
            Run(game, ref now, 2);
            var held = game.Phase;
            var anyHurt = game.Players.Values.Any(p => p.Alive && p.Health < World.MaxHealth);
            game.SetReady(true);
            Run(game, ref now, 0.1);
            var started = game.Phase;
            var allAlive = game.Players.Values.All(p => p.Alive);
            Run(game, ref now, Game.CountdownSeconds + 0.2);
            Line($"single player: {held} for 2 s, Ready -> {started} (everyone alive {allAlive}), then {game.Phase}");
            if (held != MatchPhase.Warmup || anyHurt)
                Fail("the first match didn't wait in warmup (or someone got hurt in it)");
            if (started != MatchPhase.Countdown || !allAlive)
                Fail("Ready didn't start the countdown with everyone spawned");
            if (game.Phase != MatchPhase.Playing)
                Fail("the countdown didn't lead to play");

            // The next match (after the results screen) is held in warmup again, with ready flags cleared.
            game.DebugEndMatch(now);
            Run(game, ref now, Game.IntermissionSeconds + 0.5);
            if (game.Phase != MatchPhase.Warmup || game.Local is not { Ready: false } || game.MatchNumber != 2)
                Fail($"match 2 didn't open with a warmup (phase {game.Phase}, match {game.MatchNumber})");
            // Deploy counts as ready in single player.
            game.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
            Run(game, ref now, 0.1);
            if (game.Phase != MatchPhase.Countdown)
                Fail("Deploy in single player didn't start the countdown");
        }

        // A second person who isn't ready holds everyone; leaving never stalls the match.
        using (var game = ModeGame(2, GameMode.FreeForAll))
        {
            game.WarmupEnabled = true;
            var guest = game.SelfTestAddHuman();
            Run(game, ref now, 0.5);
            game.SetReady(true);
            Run(game, ref now, 2);
            var heldByGuest = game.Phase == MatchPhase.Warmup && game.ReadyCount == (1, 2);
            game.Players.Remove(guest.Id); // disconnects
            Run(game, ref now, 0.1);
            Line($"two people: one ready -> held {heldByGuest}; the other leaves -> {game.Phase}");
            if (!heldByGuest)
                Fail("the countdown started while a person wasn't ready");
            if (game.Phase != MatchPhase.Countdown)
                Fail("a disconnect stalled the warmup");
        }

        // Force start, and the auto-start timer.
        using (var game = ModeGame(1, GameMode.FreeForAll))
        {
            game.WarmupEnabled = true;
            game.SelfTestAddHuman();
            Run(game, ref now, 0.5);
            game.ForceStart();
            Run(game, ref now, 0.1);
            if (game.Phase != MatchPhase.Countdown)
                Fail("force start didn't start the countdown");
        }
        using (var game = ModeGame(1, GameMode.FreeForAll))
        {
            game.WarmupEnabled = true;
            game.AutoStartSeconds = 2;
            game.SelfTestAddHuman();
            Run(game, ref now, 0.5);
            game.SetReady(true);
            Run(game, ref now, 1);
            var waiting = game.Phase == MatchPhase.Warmup && game.AutoStartIn > 0;
            Run(game, ref now, 1.5);
            Line($"force start works; auto-start after 2 s: waiting at 1 s {waiting}, then {game.Phase}");
            if (!waiting || game.Phase != MatchPhase.Countdown)
                Fail("the auto-start timer didn't work");
        }
    }

    // ---- Team Deathmatch ------------------------------------------------------------------------

    private static void CheckTeamDeathmatch()
    {
        using var game = ModeGame(3, GameMode.TeamDeathmatch);
        var a = game.Players[0];
        var mate = game.Players.Values.First(p => p != a && p.Team == a.Team);
        var foe = game.Players.Values.First(p => p.Team != a.Team);
        var sizes = $"{game.Players.Values.Count(p => p.Team == 0)}v{game.Players.Values.Count(p => p.Team == 1)}";
        if (sizes != "2v2")
            Fail($"teams came out {sizes} (expected 2v2)");
        foreach (var p in game.Players.Values)
            game.SelfTestPerks(p, PerkSet.Empty);
        game.SelfTestPlace(a, new Vector3(5, 0, 5), 0);
        game.SelfTestPlace(mate, new Vector3(6, 0, 5), 0);
        game.SelfTestPlace(foe, new Vector3(8, 0, 5), 0);
        game.SelfTestDamage(a, mate, 60, DamageKind.Bullet, 1);
        game.SelfTestDamage(a, mate, 90, DamageKind.Explosive, 1);
        game.SelfTestDamage(a, mate, 90, DamageKind.Melee, 1);
        var mateHp = mate.Health;
        game.SelfTestDamage(a, a, 30, DamageKind.Explosive, 1);
        var selfHp = a.Health;
        var killed = game.SelfTestDamage(a, foe, 200, DamageKind.Bullet, 2);
        Line($"teams {sizes}; teammate after bullet+grenade+melee {mateHp} HP; own grenade {World.MaxHealth - selfHp} dmg; enemy killed {killed}; team score {game.TeamScore[a.Team]}");
        if (mateHp != World.MaxHealth)
            Fail("friendly fire hurt a teammate");
        if (selfHp != World.MaxHealth - 30)
            Fail("your own grenade should still hurt you");
        // Grenades have to be able to get kills (user: "the AoE is not large enough to get kills with").
        var fragKill = Throwables.LethalRange(Throwables.Get(ThrowableKind.Frag), World.MaxHealth);
        var impactKill = Throwables.LethalRange(Throwables.Get(ThrowableKind.Impact), World.MaxHealth);
        Line($"grenades: frag kills within {fragKill:0.0} m of the chest (hurts to {Throwables.Get(ThrowableKind.Frag).Radius:0} m), impact within {impactKill:0.0} m");
        // The user asked for a 4.5 m frag kill range; it has to survive max health going up (100 -> 120).
        if (fragKill < 4.5f - 0.01f || impactKill < 2.3f)
            Fail($"grenade kill range too small: frag {fragKill:0.00} m (want 4.5+), impact {impactKill:0.00} m (want 2.3+)");
        if (!killed || game.TeamScore[a.Team] != 1 || game.TeamScore[foe.Team] != 0)
            Fail("an enemy kill didn't score for the team");
        game.SelfTestSpawn(mate);
        if (!game.Objectives.TeamSpawns[mate.Team].Contains(mate.Position))
            Fail("a player spawned outside their team's spawns");
        if (!a.Color.Equals(GameModes.TeamColors[a.Team]))
            Fail("players aren't drawn in team colours");
    }

    // ---- King of the Hill -----------------------------------------------------------------------

    private static void CheckKingOfTheHill()
    {
        using var game = ModeGame(1, GameMode.KingOfTheHill, mapId: 1);
        var a = game.Players[0];
        var b = game.Players.Values.First(p => p != a);
        if (a.Team == b.Team || game.ActiveHill is not { } hill)
        {
            Fail("KotH: no teams or no hill");
            return;
        }
        var away = game.Objectives.TeamSpawns[b.Team].OrderByDescending(s => Vector3.Distance(s, hill.Center)).First();
        game.SelfTestPlace(a, hill.Center, 0);
        game.SelfTestPlace(b, away, 0);
        var now = 0.0;
        void Tick(double seconds)
        {
            for (var t = 0.0; t < seconds - 1e-9; t += World.TickDt)
            {
                now += World.TickDt;
                game.SelfTestModeTick(now);
            }
        }
        Tick(5);
        var alone = game.TeamScore[a.Team];
        game.SelfTestPlace(b, hill.Center + new Vector3(0.8f, 0, 0), 0);
        Tick(3);
        var contested = game.TeamScore[a.Team] - alone + game.TeamScore[b.Team];
        var wasContested = game.HillContested;
        game.SelfTestPlace(a, away, 0);
        Tick(2);
        var bScore = game.TeamScore[b.Team];
        var firstHill = game.HillIndex;
        Tick(GameModes.HillMoveSeconds - 9);
        var moved = game.HillIndex != firstHill;
        Line($"hill {game.Objectives.Hills.Count} zone(s): 5 s alone -> {alone} pts; 3 s contested -> +{contested} (contested {wasContested}); other team 2 s -> {bScore}; moved after 60 s {moved}");
        if (alone is < 4 or > 5)
            Fail($"holding the hill alone for 5 s scored {alone}");
        if (contested != 0 || !wasContested)
            Fail("a contested hill scored");
        if (bScore is < 1 or > 2)
            Fail($"the other team holding for 2 s scored {bScore}");
        if (game.Objectives.Hills.Count > 1 && !moved)
            Fail("the hill didn't move after 60 s");
        // Win at the limit.
        game.TeamScore[b.Team] = GameModes.HillScoreLimit;
        Tick(0.05);
        if (game.Phase != MatchPhase.Ended || game.Winner != Game.TeamWinBase + b.Team)
            Fail("reaching the hill score limit didn't win");
    }

    // ---- Capture the Flag -----------------------------------------------------------------------

    private static void CheckCaptureTheFlag()
    {
        using var game = ModeGame(1, GameMode.CaptureTheFlag, mapId: 1);
        var a = game.Players[0];
        var b = game.Players.Values.First(p => p != a);
        foreach (var p in game.Players.Values)
            game.SelfTestPerks(p, PerkSet.Empty);
        int ta = a.Team, tb = b.Team;
        var homeA = game.Objectives.Flags[ta];
        var homeB = game.Objectives.Flags[tb];
        var park = (homeA + homeB) / 2;
        var now = 0.0;
        void Tick()
        {
            now += World.TickDt;
            game.SelfTestModeTick(now);
        }
        game.SelfTestPlace(b, park + new Vector3(0, 0, 3), 0);

        // a takes the enemy flag and brings it home.
        game.SelfTestPlace(a, homeB, 0);
        Tick();
        var took = game.Flags[tb].Carrier == a.Id;
        game.SelfTestPlace(a, park, 0);
        Tick();
        var carried = Vector3.Distance(game.Flags[tb].Position, park) < 0.01f;
        game.SelfTestPlace(a, homeA, 0);
        Tick();
        var captured = game.TeamScore[ta] == 1 && game.Flags[tb].Home && !game.Flags[tb].Carried;

        // Standoff: both flags taken; no capture while your own flag is away.
        game.SelfTestPlace(a, homeB, 0);
        Tick();
        game.SelfTestPlace(a, park, 0);
        game.SelfTestPlace(b, homeA, 0);
        Tick();
        game.SelfTestPlace(b, park + new Vector3(0, 0, 3), 0);
        game.SelfTestPlace(a, homeA, 0);
        Tick();
        var blocked = game.TeamScore[ta] == 1 && game.Flags[tb].Carrier == a.Id && game.Flags[ta].Carrier == b.Id;

        // b kills a: the flag drops; b returns their own flag by touching it, then captures with a's flag.
        game.SelfTestDamage(b, a, 500, DamageKind.Bullet, now);
        Tick();
        var dropped = game.Flags[tb].Dropped && Vector3.Distance(game.Flags[tb].Position, homeA) < 0.5f;
        game.SelfTestPlace(b, game.Flags[tb].Position, 0);
        Tick();
        var returned = game.Flags[tb].Home;
        game.SelfTestPlace(b, homeB, 0);
        Tick();
        var bCaptured = game.TeamScore[tb] == 1 && game.Flags[ta].Home;

        // A dropped flag nobody touches goes home on its own after 20 s.
        game.SelfTestPlace(b, park + new Vector3(0, 0, 3), 0);
        game.SelfTestPlace(a, homeB, 0);
        Tick();
        game.SelfTestPlace(a, park, 0);
        Tick();
        game.SelfTestDamage(b, a, 500, DamageKind.Bullet, now);
        Tick();
        var droppedAgain = game.Flags[tb].Dropped;
        for (var t = 0.0; t < GameModes.FlagReturnSeconds + 0.5; t += World.TickDt)
            Tick();
        var autoHome = game.Flags[tb].Home;

        Line($"take {took}, carried {carried}, capture {captured}, standoff blocks {blocked}, drop on death {dropped}, teammate return {returned}, " +
             $"other capture {bCaptured}, auto-return {droppedAgain}->{autoHome}; score {game.TeamScore[ta]}-{game.TeamScore[tb]}");
        if (!took || !carried || !captured)
            Fail("CTF: taking and capturing the enemy flag didn't work");
        if (!blocked)
            Fail("CTF: captured while the team's own flag was away");
        if (!dropped || !returned || !bCaptured)
            Fail("CTF: drop on death / return by touch / capture didn't work");
        if (!droppedAgain || !autoHome)
            Fail("CTF: a dropped flag didn't return on its own");
        game.TeamScore[ta] = GameModes.CtfCaptureLimit;
        Tick();
        if (game.Phase != MatchPhase.Ended || game.Winner != Game.TeamWinBase + ta)
            Fail("CTF: reaching the capture limit didn't win");
    }

    // ---- Gun Game -------------------------------------------------------------------------------

    private static void CheckGunGame()
    {
        var ladder = GunGame.Ladder;
        var first = Progress.All.First(u => u.Weapon != null).Weapon!;
        Line($"ladder ({ladder.Length}): {string.Join(", ", ladder.Select(w => w.Name))}");
        if (ladder.Length != Math.Min(GunGame.Steps, Weapons.All.Count(w => !w.IsLauncher)) || ladder[0] != first || ladder.Any(w => w.IsLauncher) || ladder.Distinct().Count() != ladder.Length)
            Fail("the Gun Game ladder is wrong (length, first weapon or repeats)");

        using var game = ModeGame(1, GameMode.GunGame);
        if (game.ModeAllowsStreaks)
            Fail("kill streaks must be off in Gun Game");
        var a = game.Players[0];
        var v = game.Players.Values.First(p => p != a);
        game.SelfTestSpawn(a);
        game.SelfTestSpawn(v);
        var startOk = a.Primary == ladder[0].Id && a.Secondary == ladder[0].Id && a.Grenade == ThrowableKind.None && a.GrenadesLeft == 0
                      && a.Equipment == ThrowableKind.None && a.EquipmentLeft == 0 && a.Perks.Equals(GunGame.Perks)
                      && v.Perks.Equals(GunGame.Perks) && v.Grenade == ThrowableKind.None && v.Equipment == ThrowableKind.None
                      && v.Primary == ladder[0].Id && game.SkipsLoadout;
        game.SelfTestPlace(a, new Vector3(5, 0, 5), 0);
        game.SelfTestPlace(v, new Vector3(7, 0, 5), 0);
        var now = 1.0;
        game.SelfTestDamageWith(a, v, 500, ladder[0].Id, now);
        var advanced = a.GunLevel == 1 && a.Primary == ladder[1].Id && a.Weapon == ladder[1].Id;

        game.SelfTestPlace(v, new Vector3(7, 0, 5), 0);
        v.GunLevel = 3;
        game.SelfTestDamageWith(a, v, 500, Game.MeleeKillId, now += 1, DamageKind.Melee);
        var demoted = v.GunLevel == 2 && a.GunLevel == 2;

        a.GunLevel = (byte)GunGame.Final;
        game.SelfTestSpawn(a);
        game.SelfTestPlace(a, new Vector3(5, 0, 5), 0);
        game.SelfTestPlace(v, new Vector3(7, 0, 5), 0);
        game.SelfTestDamageWith(a, v, 500, Game.MeleeKillId, now += 1, DamageKind.Melee);
        game.SelfTestModeTick(now);
        var meleeNoWin = game.Phase == MatchPhase.Playing;
        game.SelfTestPlace(v, new Vector3(7, 0, 5), 0);
        game.SelfTestDamageWith(a, v, 500, ladder[^1].Id, now += 1);
        game.SelfTestModeTick(now);
        var won = game.Phase == MatchPhase.Ended && game.Winner == a.Id;
        Line($"start on {ladder[0].Name} {startOk}; kill -> next gun {advanced}; melee kill demotes victim {demoted}; melee on the last gun doesn't win {meleeNoWin}; last-gun kill wins {won}");
        if (!startOk)
            Fail("Gun Game didn't hand out the first ladder gun (or left grenades/perks on)");
        if (!advanced)
            Fail("a kill didn't advance the ladder");
        if (!demoted)
            Fail("a melee kill didn't demote the victim");
        if (!meleeNoWin || !won)
            Fail("the Gun Game win rule is wrong");
    }

    // ---- Replication, the server browser, and quick bot matches in each mode --------------------------------

    private static void CheckModeMatches()
    {
        // The mode state replicates teams, ready flags, levels, scores and flags.
        using (var server = ModeGame(3, GameMode.CaptureTheFlag))
        using (var client = ModeGame(3, GameMode.FreeForAll))
        {
            server.TeamScore[0] = 2;
            server.TeamScore[1] = 1;
            server.Players[0].Ready = true;
            server.Players[1].GunLevel = 7;
            server.Flags[1].Carrier = 0;
            server.Flags[1].Home = false;
            server.Flags[1].Position = new Vector3(3, 0, 4);
            var w = new PacketWriter();
            server.WriteModeState(w);
            var r = new PacketReader(w.Span[1..]);
            client.ReadModeState(ref r);
            var ok = client.Mode == GameMode.CaptureTheFlag && client.TeamScore[0] == 2 && client.TeamScore[1] == 1 && client.Players[0].Ready
                     && client.Players[1].GunLevel == 7 && client.Flags[1].Carrier == 0 && !client.Flags[1].Home && client.Flags[1].Position == new Vector3(3, 0, 4)
                     && server.Players.Values.All(p => client.Players[p.Id].Team == p.Team);
            Line($"mode state: {w.Length} bytes for {server.Players.Count} players, replicated {ok}");
            if (!ok || w.Length > 200)
                Fail("the mode state didn't replicate (or is too big)");
        }
        var info = new ServerInfo("Modes", "Courtyard", 2, 8, 1, MatchPhase.Warmup, 60123, ServerInfo.ProtocolVersion, GameMode.KingOfTheHill);
        if (ServerInfo.Find(info.Encode()) != info)
            Fail("the game mode didn't survive the server info");

        // 30 s of 8-player bot play in each mode on a medium-ish map: nothing breaks, objectives get played.
        var map = Maps.All.FirstOrDefault(m => m.Size == MapSize.Small && m.Id > 0) ?? Maps.All[0];
        foreach (var mode in GameModes.All.Where(m => m != GameMode.FreeForAll))
        {
            var sw = Stopwatch.StartNew();
            using var game = ModeGame(Game.ClassicPlayers - 1, mode, map.Id);
            var now = 0.0;
            var held = 0;
            var maxLevel = 0;
            var takes = 0;
            var wasCarried = false;
            try
            {
                const double dt = 1.0 / World.TickRate;
                for (var t = 0.0; t < 35; t += dt)
                {
                    now += dt;
                    game.Update(now, dt);
                    if (game.HillOwner != Game.NoTeam)
                        held++;
                    var carried = game.Flags[0].Carried || game.Flags[1].Carried;
                    if (carried && !wasCarried)
                        takes++;
                    wasCarried = carried;
                    maxLevel = Math.Max(maxLevel, game.Players.Values.Max(p => (int)p.GunLevel));
                    foreach (var p in game.Players.Values)
                        if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                            throw new Exception($"player {p.Id} position is NaN");
                }
            }
            catch (Exception ex)
            {
                Fail($"{GameModes.Name(mode)}: bot match crashed: {ex}");
                continue;
            }
            var kills = game.Players.Values.Sum(p => (int)p.Kills);
            Line($"{GameModes.Name(mode),-17} on {map.Name}: kills {kills}, team score {game.TeamScore[0]}-{game.TeamScore[1]}, hill held {held / (double)World.TickRate:0} s, " +
                 $"flag takes {takes}, top gun {maxLevel + 1}, phase {game.Phase} ({sw.Elapsed.TotalSeconds:0.0} s)");
            if (mode == GameMode.TeamDeathmatch && game.Phase == MatchPhase.Playing && game.TeamScore[0] + game.TeamScore[1] != kills)
                Fail($"TDM: team score {game.TeamScore[0] + game.TeamScore[1]} doesn't match {kills} kills (a teamkill counted?)");
            if (mode == GameMode.GunGame && kills > 0 && maxLevel == 0)
                Fail("Gun Game: kills happened but nobody moved up the ladder");
            if (mode == GameMode.KingOfTheHill && held == 0)
                Line("  note: no team held the hill alone in 30 s");
        }
    }
}
