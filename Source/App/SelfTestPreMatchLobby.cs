using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test sections "Pre-match lobby" (PreMatchLobby.cs, LobbyChat.cs, the lobby room screen's text) and "Team
/// balance" (TeamBalance.cs). Short: a loopback host and two clients for a few seconds, offline games on simulated time.
/// </summary>
public static partial class SelfTest
{
    private static void CheckPreMatchLobby()
    {
        CheckLobbyLoopback();
        CheckLobbyOffline();
        CheckLobbyRules();
        CheckLobbySettingsFile();
        CheckLobbyViewAllocations();
    }

    // ---- Loopback: host + 2 clients ---------------------------------------------------------------------------

    private static void CheckLobbyLoopback()
    {
        var small = FindMap("Proto Box", m => m.MaxPlayers >= 8)!;
        const ushort port = 60191;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(small);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: 2, mapId: small.Id);
        server.MapVoteEnabled = false;
        server.WarmupEnabled = true;
        server.LobbyEnabled = true;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 2.0, ref now, clock);

        // Join: everyone lands in the lobby, nobody in the map.
        var inMap = server.Players.Values.Count(p => p.Alive);
        Line($"join: server {server.Phase}, clients {a.Phase}/{b.Phase}, players {server.Players.Count} (client sees {a.Players.Count}), alive in the map {inMap}");
        if (server.Phase != MatchPhase.Lobby || a.Phase != MatchPhase.Lobby || b.Phase != MatchPhase.Lobby)
            Fail("joining a hosted game didn't land everyone in the lobby room");
        if (server.Players.Count != 5 || a.Players.Count != 5 || inMap != 0)
            Fail($"the lobby has {server.Players.Count} players (client {a.Players.Count}), {inMap} in the map: expected 5, none in the map");

        // Team pick (no cooldown in the lobby) and the mode change reaching the clients.
        if (!server.LobbySetMode(GameMode.TeamDeathmatch))
            Fail("the host couldn't switch the lobby to Team Deathmatch");
        Pump(server, [a, b], 0.5, ref now, clock);
        a.RequestTeam(1);
        Pump(server, [a, b], 0.5, ref now, clock);
        a.RequestTeam(0); // straight back: no 10 s cooldown before the match
        Pump(server, [a, b], 0.5, ref now, clock);
        var sa = server.Players[a.LocalId];
        Line($"teams: clients see {a.Mode}; Alpha picked Red then Blue -> server {GameModes.TeamName(sa.Team)} (pick {sa.TeamChoice}), client {a.Local?.TeamChoice}");
        if (a.Mode != GameMode.TeamDeathmatch || b.Mode != GameMode.TeamDeathmatch)
            Fail("the lobby's mode change didn't reach the clients");
        if (sa.Team != 0 || sa.TeamChoice != 0 || a.Local is not { TeamChoice: 0 })
            Fail("a team pick in the lobby didn't apply (or the switch cooldown applied in the lobby)");

        // Ready, START, and an un-ready cancelling the countdown.
        a.SetReady(true);
        b.SetReady(true);
        Pump(server, [a, b], 0.5, ref now, clock);
        var canStart = server.CanLobbyStart;
        var started = server.LobbyStart(force: false, now);
        Pump(server, [a, b], 1.0, ref now, clock);
        var counting = server.LobbyCountdown > 0 && a.LobbyCountdown > 0;
        b.SetReady(false);
        Pump(server, [a, b], 0.5, ref now, clock);
        var cancelled = server.LobbyCountdown <= 0 && server.Phase == MatchPhase.Lobby;
        Line($"ready: {server.ReadyCount.Ready}/{server.ReadyCount.Humans} after Bravo un-readied; START enabled {canStart}, started {started}, counting on the client {counting}, cancelled {cancelled}");
        if (!canStart || !started || !counting)
            Fail("START with everyone ready didn't begin the lobby countdown everywhere");
        if (!cancelled)
            Fail("un-readying didn't cancel the lobby countdown");

        // The host changes the map; the clients follow. Map fit: never a map smaller than the people here.
        var other = FindMap("Courtyard", m => m != small && server.LobbyMapAllowed(m));
        var changed = other != null && server.LobbySetMap(other, false);
        Pump(server, [a, b], 0.6, ref now, clock);
        Line($"map change: {other?.Name ?? "none"} applied {changed}; clients see {a.LobbyMapName} / {b.LobbyMapName}, version {a.LobbyVersion} (host {server.LobbyVersion})");
        if (!changed || a.LobbyMapName != other!.Name || b.LobbyMapName != other.Name || a.LobbyVersion != server.LobbyVersion)
            Fail("the lobby's map change didn't reach the clients");
        var tooSmall = Maps.All.FirstOrDefault(m => server.PlayerLimit(m) < server.HumanCount);
        if (tooSmall != null && server.LobbySetMap(tooSmall, false))
            Fail($"the lobby took {tooSmall.Name} ({server.PlayerLimit(tooSmall)} players) for {server.HumanCount} people");

        // Chat through the network: cleaned, capped, everyone gets it.
        var sent = a.SendChat("Hi\u0001 %s " + new string('x', 300), now);
        Pump(server, [a, b], 0.5, ref now, clock);
        var got = b.Chat.Count > 0 ? b.Chat[^1].Text : "";
        Line($"chat: Alpha's line sent {sent == null}, Bravo got {got.Length} chars \"{(got.Length > 20 ? got[..20] + "..." : got)}\" from {(b.Chat.Count > 0 ? b.Chat[^1].Name : "?")}");
        if (sent != null || got.Length == 0 || got.Length > ChatRules.MaxLength || !IsPrintableAscii(got) || server.Chat.Count == 0)
            Fail("a lobby chat line didn't arrive clean and capped");

        // Host START with everyone ready: the countdown runs out and the match begins for everyone.
        b.SetReady(true);
        Pump(server, [a, b], 0.4, ref now, clock);
        var go = server.LobbyStart(force: false, now);
        Pump(server, [a, b], Game.LobbyCountdownSeconds + 0.7, ref now, clock);
        var alive = server.Players.Values.Count(p => p.Alive);
        Line($"start: {go}; after the countdown server {server.Phase}, clients {a.Phase}/{b.Phase}, alive {alive}/{server.Players.Count}");
        if (!go || server.Phase != MatchPhase.Countdown || a.Phase != MatchPhase.Countdown || b.Phase != MatchPhase.Countdown || alive != server.Players.Count)
            Fail("the lobby's countdown didn't start the match (everyone spawned, in the match countdown)");

        // A late joiner during the match skips the lobby.
        using var c = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Charlie", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b, c], 1.5, ref now, clock);
        Line($"late joiner: Charlie is in {c.Phase} as player {c.LocalId} (server {server.Phase})");
        if (c.Phase == MatchPhase.Lobby || c.LocalId == byte.MaxValue || c.Disconnected)
            Fail("a person joining during the match landed in a lobby (or didn't get in)");

        // The lobby state packet: round trip and size.
        server.LobbySetOptions(ScoreLimitMode.Fixed, 15, 20, true, false);
        var w = new PacketWriter();
        server.WriteLobbyState(w);
        var r = new PacketReader(w.Span[1..]);
        a.ReadLobbyState(ref r);
        Line($"lobby state: {w.Length} bytes for {server.Players.Count} players; read back {a.MatchMinutes} min, auto {a.AutoStartSeconds} s, return {a.ReturnToLobby}, {a.ScoreLimitMode}");
        if (a.MatchMinutes != 15 || a.AutoStartSeconds != 20 || !a.ReturnToLobby || a.ScoreLimitMode != ScoreLimitMode.Fixed || a.FrenzyBuffsEnabled)
            Fail("the lobby state didn't round-trip");
        if (w.Length > 40 + 3 * Game.MaxPlayers + 40)
            Fail($"the lobby state is {w.Length} bytes (not compact)");
    }

    // ---- Offline: disconnects, auto-start, return to lobby ---------------------------------------------------

    private static void CheckLobbyOffline()
    {
        var now = 0.0;
        using (var game = ModeGame(2, GameMode.FreeForAll))
        {
            game.LobbyEnabled = true;
            game.WarmupEnabled = true;
            game.AutoStartSeconds = 60;
            var guest = game.SelfTestAddHuman();
            Run(game, ref now, 0.3);
            game.SetReady(true);
            Run(game, ref now, 1);
            var held = game.InLobby && game.LobbyCountdown <= 0;
            game.Players.Remove(guest.Id); // the one not ready disconnects
            Run(game, ref now, 0.2);
            var counting = game.LobbyCountdown > 0;
            Run(game, ref now, Game.LobbyCountdownSeconds + 0.2);
            Line($"offline: host ready, guest not -> held {held}; the guest leaves -> counting {counting}, then {game.Phase}");
            if (!held || !counting)
                Fail("a disconnect stalled the lobby (or the countdown started with someone not ready)");
            if (game.Phase != MatchPhase.Countdown)
                Fail("the lobby countdown didn't lead to the match countdown");

            // Return to lobby between matches (host setting) vs straight into the next warmup.
            Run(game, ref now, Game.CountdownSeconds + 0.2);
            game.ReturnToLobby = true;
            game.DebugEndMatch(now);
            Run(game, ref now, Game.IntermissionSeconds + 0.3);
            var back = game.Phase;
            var anyAlive = game.Players.Values.Any(p => p.Alive);
            game.LobbyStart(force: true, now);
            Run(game, ref now, Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.4);
            game.ReturnToLobby = false;
            game.DebugEndMatch(now);
            Run(game, ref now, Game.IntermissionSeconds + 0.3);
            Line($"between matches: return on -> {back} (anyone in the map {anyAlive}); off -> {game.Phase}");
            if (back != MatchPhase.Lobby || anyAlive)
                Fail("\"Return to lobby between matches\" didn't bring everyone back to the lobby");
            if (game.Phase != MatchPhase.Warmup)
                Fail("with \"Return to lobby\" off the next match didn't go straight to the warmup");
        }

        // Single player never opens the lobby; hosting without it keeps the warmup.
        using (var sp = ModeGame(1, GameMode.FreeForAll))
        {
            sp.WarmupEnabled = true;
            Run(sp, ref now, 0.3);
            if (sp.Phase != MatchPhase.Warmup)
                Fail($"a game without the lobby opened in {sp.Phase}, not the warmup");
        }

        // Chat rate limit (server side) and the length cap.
        using (var game = ModeGame(0, GameMode.FreeForAll))
        {
            var guest = game.SelfTestAddHuman();
            for (var i = 0; i < 10; i++)
                game.SelfTestChatFrom(guest, $"spam {i}", 100);
            var burst = game.Chat.Count;
            game.SelfTestChatFrom(guest, "later", 100 + ChatRules.RefillSeconds * 2);
            var mine = 0;
            for (var i = 0; i < 10; i++)
                if (game.SendChat("me " + i, 200) == null)
                    mine++;
            Line($"chat limits: 10 lines at once -> {burst} kept, {game.Chat.Count - burst - mine} more after {ChatRules.RefillSeconds * 2:0} s; host sent {mine}/10; kept at most {ChatRules.Keep}");
            if (burst != ChatRules.Burst || mine != ChatRules.Burst || game.Chat.Count != 2 * ChatRules.Burst + 1)
                Fail("the chat rate limit didn't hold");
            for (var i = 0; i < 80; i++)
                game.SelfTestChatFrom(guest, "x", 1000 + i * 10);
            if (game.Chat.Count != ChatRules.Keep)
                Fail($"the chat kept {game.Chat.Count} lines (cap {ChatRules.Keep})");
        }
    }

    // ---- Rules: map fit, chat text ---------------------------------------------------------------------------

    private static void CheckLobbyRules()
    {
        var map = Maps.All[0];
        var fits = Game.LobbyMapAllowed(map, 4, _ => 8, _ => true);
        var tooSmall = Game.LobbyMapAllowed(map, 9, _ => 8, _ => true);
        var wrongMode = Game.LobbyMapAllowed(map, 2, _ => 8, _ => false);
        var exact = Game.LobbyMapAllowed(map, 8, _ => 8, _ => true);
        // The vote's rule for the same people agrees.
        var vote = Game.FitForPeople([map], 9, _ => 8).Count > 0 && Game.FitForPeople([map], 9, _ => 8)[0] == map; // (the vote falls back to the largest; the lobby refuses)
        Line($"map fit: 4 people on an 8-player map {fits}, exactly 8 {exact}, 9 {tooSmall}, wrong mode {wrongMode} (the vote's fallback for 9 offers it {vote}, the lobby refuses)");
        if (!fits || !exact || tooSmall || wrongMode)
            Fail("the lobby's map-fit rule is wrong");
        var clean = ChatRules.Clean("  a\u0000b\r\nc\U0001F642" + new string('z', 500));
        if (clean.Length > ChatRules.MaxLength || !IsPrintableAscii(clean) || !clean.StartsWith("ab c", StringComparison.Ordinal))
            Fail($"chat text cleaned to \"{clean[..Math.Min(clean.Length, 20)]}\"");
        if (ChatRules.Clean("\u0001\u0002 ").Length != 0)
            Fail("an unprintable chat line wasn't dropped");
    }

    // ---- Settings: the new keys save and load (temporary files); old files get the defaults ------------------

    private static void CheckLobbySettingsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cos_lobby_settings_{Environment.ProcessId}.txt");
        var old = Path.Combine(Path.GetTempPath(), $"cos_lobby_settings_old_{Environment.ProcessId}.txt");
        try
        {
            new Settings { LobbyReturn = true, MatchMinutes = 20, TeamBalance = TeamBalance.HumansVsBots, HumansTeam = 1, AutoStart = 45 }.Save(path);
            var back = Settings.Load(path);
            File.WriteAllLines(old, ["name=Old", "mode=TeamDeathmatch", "auto_start=30", "match_minutes=999", "team_balance=nonsense"]);
            var legacy = Settings.Load(old);
            Line($"settings: saved and loaded return {back.LobbyReturn}, {back.MatchMinutes} min, {back.TeamBalance} (people {back.HumansTeam}); "
                 + $"an old file: return {legacy.LobbyReturn}, {legacy.MatchMinutes} min (999 clamped), {legacy.TeamBalance}");
            if (!back.LobbyReturn || back.MatchMinutes != 20 || back.TeamBalance != TeamBalance.HumansVsBots || back.HumansTeam != 1 || back.AutoStart != 45)
                Fail("the lobby settings didn't survive a save and load");
            if (legacy.LobbyReturn || legacy.MatchMinutes != 60 || legacy.TeamBalance != TeamBalance.Auto || legacy.AutoStart != 30 || legacy.Name != "Old")
                Fail("an older settings file didn't load with the lobby defaults");
        }
        finally
        {
            try
            {
                File.Delete(path);
                File.Delete(old);
            }
            catch
            {
                // temp files: never mind
            }
        }
    }

    // ---- The lobby screen's text: nothing allocated in a quiet frame ------------------------------------------

    private static void CheckLobbyViewAllocations()
    {
        var now = 0.0;
        using var game = ModeGame(5, GameMode.TeamDeathmatch);
        game.LobbyEnabled = true;
        game.SelfTestAddHuman();
        Run(game, ref now, 0.2);
        game.SelfTestChatFrom(game.Players[0], "hello", 1);
        var view = new LobbyRoomView();
        view.Refresh(game, null);
        view.RefreshMapList(game, 0);
        var rebuilds = view.Rebuilds;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++)
        {
            view.Refresh(game, null);
            view.RefreshMapList(game, 0);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var rows = view.Rows.Count;
        game.SetReady(true); // a change: one rebuild
        view.Refresh(game, null);
        Line($"lobby screen text: {rows} rows, {view.MapList.Count} maps listed; 2000 quiet frames allocated {bytes} bytes and rebuilt {view.Rebuilds - rebuilds - 1} times; a ready tick rebuilt it");
        if (bytes > 1024) // (a little slack for the runtime itself; one string a frame would be 50+ KB)
            Fail($"the lobby screen's text allocated {bytes} bytes over 2000 frames with nothing changing");
        if (view.Rebuilds != rebuilds + 1 || rows != game.Players.Count)
            Fail("the lobby screen's text rebuilt without a change (or missed one)");
    }

    // ---- Team balance: Off and Humans vs Bots (loopback host + 2 clients + bots) -------------------------------

    private static void CheckTeamBalance()
    {
        var map = FindMap("Courtyard", m => m.MaxPlayers >= 12) ?? FindMap("Proto Box", m => m.MaxPlayers >= 8)!;
        const ushort port = 60192;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: 3, mapId: map.Id);
        server.MapVoteEnabled = false;
        server.LobbyEnabled = true;
        server.Mode = GameMode.TeamDeathmatch;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 2.0, ref now, clock);
        var limitBefore = server.ScoreLimit;

        // Off: everyone stacks Blue; no size limit, nobody moved.
        server.SetTeamBalance(TeamBalance.Off, 0);
        var botTeams = server.Players.Values.Where(p => p.Bot != null).ToDictionary(p => p.Id, p => p.Team);
        server.RequestTeam(0);
        a.RequestTeam(0);
        b.RequestTeam(0);
        Pump(server, [a, b], 0.8, ref now, clock);
        var blueHumans = server.Players.Values.Count(p => p.Bot == null && p.Team == 0);
        var botsMoved = server.Players.Values.Count(p => p.Bot != null && botTeams.TryGetValue(p.Id, out var t) && t != p.Team);
        Line($"balance Off: {blueHumans}/3 people on Blue, Blue {server.Players.Values.Count(p => p.Team == 0)} v Red {server.Players.Values.Count(p => p.Team == 1)}, "
             + $"bots moved {botsMoved}; client sees {a.TeamBalance}");
        if (blueHumans != 3 || botsMoved != 0 || a.TeamBalance != TeamBalance.Off)
            Fail("team balance Off still limited picks or moved bots");

        // Humans vs Bots, people on Red: every person Red, every bot Blue, picks of Blue refused, joiners follow.
        server.SetTeamBalance(TeamBalance.HumansVsBots, 1);
        Pump(server, [a, b], 0.6, ref now, clock);
        a.RequestTeam(0);
        Pump(server, [a, b], 0.6, ref now, clock);
        server.AddBot(BotDifficulty.Normal);
        using var c = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Charlie", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b, c], 1.5, ref now, clock);
        var humansOk = server.Players.Values.Where(p => p.Bot == null).All(p => p.Team == 1);
        var botsOk = server.Players.Values.Where(p => p.Bot != null).All(p => p.Team == 0);
        var refused = a.TeamNotice.Contains("Humans vs Bots", StringComparison.Ordinal);
        var limitAfter = server.ScoreLimit;
        var person = server.Players[a.LocalId];
        server.SelfTestSpawn(person);
        var redSpawn = server.Objectives.TeamSpawns[1].Count == 0 || server.Objectives.TeamSpawns[1].Contains(person.Position);
        Line($"Humans vs Bots: people {server.HumanCount} all Red {humansOk}, bots {server.BotCount} all Blue {botsOk}, Blue pick refused {refused} (\"{a.TeamNotice}\"), "
             + $"joiner Charlie on {GameModes.TeamName(server.Players.TryGetValue(c.LocalId, out var cp) ? cp.Team : Game.NoTeam)}, "
             + $"score limit {limitBefore} -> {limitAfter} for {server.Players.Count}, spawns on Red {redSpawn}, client sees {c.TeamBalance}");
        if (!humansOk || !botsOk)
            Fail("Humans vs Bots didn't put every person on one side and every bot on the other");
        if (!refused || person.Team != 1)
            Fail("a person could pick the bots' side in Humans vs Bots");
        if (limitAfter != server.ScoreLimitFor(server.Players.Count))
            Fail("the score limit doesn't follow the player count in Humans vs Bots");
        if (!redSpawn)
            Fail("a person spawned outside their side's spawns in Humans vs Bots");
        server.LobbyShuffleTeams(false);
        if (!server.Players.Values.Where(p => p.Bot == null).All(p => p.Team == 1))
            Fail("SHUFFLE moved people in Humans vs Bots");

        // Auto again: the usual rules come back (the "Team pick" section checks them in full).
        server.SetTeamBalance(TeamBalance.Auto, 0);
        Pump(server, [a, b, c], 0.5, ref now, clock);
        int blue = server.Players.Values.Count(p => p.Team == 0), red = server.Players.Values.Count(p => p.Team == 1);
        Line($"back to Auto: Blue {blue} v Red {red}");
        if (Math.Abs(blue - red) > 1)
            Fail("Auto balance didn't even the teams again");
    }
}
