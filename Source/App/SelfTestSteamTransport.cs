using System.Diagnostics;
using Brutal.RakNetApi;
using CallOfSlop.Platform.Steam;

namespace CallOfSlop;

/// <summary>
/// "Steam transport" (Phase 2 of the Steam migration, docs/steam/STEAM.md "Transport"): the transport interface under
/// NetPeer with both transports (RakNet over loopback, Steam sockets on the recorded backend), a whole game over the
/// Steam transport, lobbies and the browser's rows, Rich Presence tokens against the localisation file, auth tickets
/// (good and bad), and bans. Nothing here needs a Steam client; the real binding is never touched.
/// </summary>
public static partial class SelfTest
{
    private static void CheckSteamTransport()
    {
        var real = SteamPlatform.Backend;
        try
        {
            TransportInterface();
            GameOverSteamTransport();
            SteamLobbiesAndBrowser();
            SteamRichPresence();
            SteamAuthTickets();
        }
        finally
        {
            SteamPlatform.UseBackend(real); // every later section runs without Steam, as the game does
        }
    }

    private static RecordedSteamBackend SteamUser(RecordedSteamWorld world, ulong id, string name)
    {
        var b = new RecordedSteamBackend(world) { LocalSteamId = id, PersonaName = name };
        b.Init(480, out _);
        return b;
    }

    /// <summary>Drives one transport pair through the interface: connect, every reliability both ways, a broadcast, stats, kick, dispose.</summary>
    private static void TransportInterface()
    {
        var world = new RecordedSteamWorld();
        const ulong hostId = 76561197960288001, clientId = 76561197960288002, thirdId = 76561197960288003;
        var host = SteamUser(world, hostId, "Host");
        var guest = SteamUser(world, clientId, "Guest");
        var third = SteamUser(world, thirdId, "Third");
        try
        {
            DriveTransport("raknet", () => new NetPeer(RakNetTransport.Host(26331, 1)), () => new NetPeer(RakNetTransport.Join("127.0.0.1", 26331)), null, 0);
            DriveTransport("steam", () => new NetPeer(SteamTransport.Host(host, 1)), () => new NetPeer(SteamTransport.Join(guest, hostId)), [host, guest, third], clientId);

            // A full Steam host refuses a second client (the client hears "no free connections"), and a duplicate id.
            using var server = new NetPeer(SteamTransport.Host(host, 1));
            using var first = new NetPeer(SteamTransport.Join(guest, hostId));
            var events = new List<string>();
            PumpPeers([host, guest, third], [server, first], 6, (who, s, d) => events.Add($"{who}:{(DefaultMessageIDTypes)d[0]}"));
            using var second = new NetPeer(SteamTransport.Join(third, hostId));
            PumpPeers([host, guest, third], [server, first, second], 6, (who, s, d) => events.Add($"{who}:{(DefaultMessageIDTypes)d[0]}"));
            var lanes = host.ConnectionLog.Select(c => c.Lanes).ToList();
            Line($"steam full: events {string.Join(" ", events)}; host lanes {string.Join(",", lanes)}; connections {server.ConnectionCount}");
            if (!events.Contains("2:NoFreeIncomingConnections") || server.ConnectionCount != 1 || lanes.Count == 0 || lanes.Any(l => l != NetLanes.Count))
                Fail("steam full: the second client should be refused as full, and every accepted connection gets 3 lanes");
            // The host goes: the client is told.
            events.Clear();
            server.Dispose();
            PumpPeers([host, guest, third], [first], 4, (who, s, d) => events.Add($"{who}:{(DefaultMessageIDTypes)d[0]}"));
            Line($"steam host gone: {string.Join(" ", events)}");
            if (!events.Contains("0:DisconnectionNotification"))
                Fail("steam host gone: the client should get a disconnection notice");
        }
        finally
        {
            host.Shutdown();
            guest.Shutdown();
            third.Shutdown();
        }
    }

    private delegate void PeerEvent(int who, RakNetGUID sender, ReadOnlySpan<byte> data);

    /// <summary>Pumps the recorded Steam users' callbacks and every peer a few rounds.</summary>
    private static void PumpPeers(RecordedSteamBackend[]? users, NetPeer[] peers, int rounds, PeerEvent on)
    {
        for (var r = 0; r < rounds; r++)
        {
            if (users != null)
                foreach (var u in users)
                    u.RunCallbacks();
            for (var i = 0; i < peers.Length; i++)
            {
                var who = i;
                peers[i].Poll((s, d) => on(who, s, d));
            }
            if (users == null)
                Thread.Sleep(15); // RakNet: real sockets
        }
    }

    private static void DriveTransport(string kind, Func<NetPeer> makeHost, Func<NetPeer> makeClient, RecordedSteamBackend[]? users, ulong expectClientSteamId)
    {
        using var server = makeHost();
        using var client = makeClient();
        var events = new List<string>();
        var got = new List<(int Who, byte Msg, int Len)>();
        RakNetGUID clientGuid = default;
        void On(int who, RakNetGUID sender, ReadOnlySpan<byte> data)
        {
            if (data[0] < (byte)DefaultMessageIDTypes.UserPacketEnum)
            {
                events.Add($"{who}:{(DefaultMessageIDTypes)data[0]}");
                if (who == 0 && data[0] == (byte)DefaultMessageIDTypes.NewIncomingConnection)
                    clientGuid = sender;
            }
            else
                got.Add((who, data[0], data.Length));
        }
        PumpPeers(users, [server, client], users == null ? 60 : 6, On);
        var connected = events.Contains("0:NewIncomingConnection") && events.Contains("1:ConnectionRequestAccepted");
        Line($"{kind}: connect events {string.Join(" ", events)}; server sees client {clientGuid.G:x}, client sees server {client.ServerGuid.G:x}, kind {server.Kind}/{client.Kind}");
        if (!connected || server.ConnectionCount != 1 || client.ConnectionCount != 1)
        {
            Fail($"{kind}: host and client should connect through the interface");
            return;
        }
        if (server.SteamIdOf(clientGuid) != expectClientSteamId || client.SteamIdOf(client.ServerGuid) != (expectClientSteamId == 0 ? 0UL : 76561197960288001UL))
            Fail($"{kind}: SteamIdOf should name the peer over Steam and 0 over RakNet");
        // Every reliability, both ways; a big reliable message (over an MTU); a broadcast.
        var w = new PacketWriter();
        foreach (var rel in new[] { PacketReliability.ReliableOrdered, PacketReliability.Reliable, PacketReliability.UnreliableSequenced, PacketReliability.Unreliable })
        {
            client.SendToServer(w.Begin(Msg.Input).U8((byte)rel).Span, rel);
            server.SendTo(clientGuid, w.Begin(Msg.State).U8((byte)rel).Span, rel);
        }
        w.Begin(Msg.PropState);
        for (var i = 0; i < 3000; i++)
            w.U8((byte)i);
        server.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        server.Broadcast(w.Begin(Msg.Roster).U8(1).Span, PacketReliability.ReliableOrdered);
        PumpPeers(users, [server, client], users == null ? 60 : 6, On);
        var serverGot = got.Where(g => g.Who == 0).Select(g => (Msg)g.Msg).ToList();
        var clientGot = got.Where(g => g.Who == 1).Select(g => (Msg)g.Msg).ToList();
        var big = got.FirstOrDefault(g => g.Who == 1 && g.Msg == (byte)Msg.PropState);
        var stats = server.StatsFor(clientGuid, out var st);
        var cstats = client.StatsFor(client.ServerGuid, out var cst);
        Line($"{kind}: server got {string.Join(",", serverGot)}; client got {string.Join(",", clientGot)}; big {big.Len} B; ping {server.PingTo(clientGuid)}/{client.PingTo(client.ServerGuid)}; "
             + $"stats {stats}/{cstats} ({st.Transport} {st.PingMs} ms q {st.QualityLocal:0.00}); bytes sent {server.BytesSent}/{client.BytesSent} received {server.BytesReceived}/{client.BytesReceived}");
        if (serverGot.Count(m => m == Msg.Input) < 2 || clientGot.Count(m => m == Msg.State) < 2 || big.Len != 3001 || clientGot.Count(m => m == Msg.Roster) != 1)
            Fail($"{kind}: reliable messages must arrive both ways, a 3 KB message whole, and a broadcast once");
        if (users != null && (!stats || !cstats || st.Transport != "steam" || serverGot.Count != 4 || clientGot.Count != 6))
            Fail($"{kind}: the recorded Steam loses nothing and reports stats on both ends");
        if (server.BytesSent == 0 || client.BytesReceived == 0 || server.BytesReceived == 0)
            Fail($"{kind}: NetPeer counts payload bytes on both sides");
        // Kick: the client hears a disconnection.
        events.Clear();
        server.Kick(clientGuid);
        PumpPeers(users, [server, client], users == null ? 60 : 6, On);
        Line($"{kind}: after kick {string.Join(" ", events)}; connections {server.ConnectionCount}/{client.ConnectionCount}");
        if (!events.Contains("1:DisconnectionNotification") || server.ConnectionCount != 0)
            Fail($"{kind}: a kicked client should get a disconnection notice and the host should forget it");
    }

    /// <summary>A whole game over the Steam transport: join, roster, the host learns the client's Steam ID from the connection, a ban refuses the next one.</summary>
    private static void GameOverSteamTransport()
    {
        var world = new RecordedSteamWorld { DropEveryUnreliable = 7 }; // a lossy route: the game copes with unreliable loss
        const ulong hostId = 76561197960288011, clientId = 76561197960288012, bannedId = 76561197960288013;
        var host = SteamUser(world, hostId, "Host");
        var guest = SteamUser(world, clientId, "Guest");
        var banned = SteamUser(world, bannedId, "Banned");
        try
        {
            var map = FindMap("Proto Box", m => m.MaxPlayers >= 4)!;
            World.SetMap(map);
            var clock = Stopwatch.StartNew();
            var now = 0.0;
            using var server = new Game(new NetPeer(SteamTransport.Host(host, Game.MaxPlayers - 1)), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1, mapId: map.Id);
            server.MapVoteEnabled = false;
            server.WarmupEnabled = true;
            server.SteamBans.Add(bannedId);
            using var client = new Game(new NetPeer(SteamTransport.Join(guest, hostId)), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            using var refused = new Game(new NetPeer(SteamTransport.Join(banned, hostId)), new Audio(), "Banned", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            PumpSteamGames([host, guest, banned], server, [client, refused], 1.0, ref now, clock);
            var joined = client.LocalId != byte.MaxValue && server.Players.ContainsKey(client.LocalId);
            var idOnServer = joined ? server.Players[client.LocalId].SteamId : 0;
            var hostOnClient = client.HostSteamId;
            var roster = client.Players.Count;
            Line($"game over steam: joined {joined} as {client.LocalId}, id on server {idOnServer}, host id on client {hostOnClient}, client roster {roster}, kind {server.TransportKind}/{client.TransportKind}; "
                 + $"refused: '{refused.Status}' disconnected {refused.Disconnected}; server players {server.Players.Count}");
            if (!joined || idOnServer != clientId || roster < 3 || server.TransportKind != "steam" || client.TransportKind != "steam")
                Fail("game over steam: the client should join over Steam sockets and the host should know its Steam ID from the connection");
            if (!refused.Disconnected || !refused.Status.Contains("banned") || server.Players.Values.Any(p => p.SteamId == bannedId))
                Fail("game over steam: a banned Steam ID should be refused with the reason and never become a player");
            // Kick by Steam ID: the client hears why.
            var kicked = server.KickSteam(clientId);
            PumpSteamGames([host, guest, banned], server, [client], 0.5, ref now, clock);
            Line($"kick by steam id: {kicked}; client '{client.Status}' disconnected {client.Disconnected}; server players {server.Players.Count}");
            if (!kicked || !client.Disconnected || !client.Status.Contains("kicked") || server.Players.ContainsKey(client.LocalId))
                Fail("kick by steam id: the player should be gone and told why");
        }
        finally
        {
            host.Shutdown();
            guest.Shutdown();
            banned.Shutdown();
        }
    }

    private static void PumpSteamGames(RecordedSteamBackend[] users, Game server, IEnumerable<Game> clients, double seconds, ref double now, Stopwatch clock)
    {
        var until = clock.Elapsed.TotalSeconds + seconds;
        while (clock.Elapsed.TotalSeconds < until)
        {
            foreach (var u in users)
                u.RunCallbacks();
            SteamPlatform.RunCallbacks();
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            server.Update(now, dt);
            foreach (var c in clients)
                c.Update(now, dt);
            Thread.Sleep(2);
        }
    }

    /// <summary>The host's lobby carries the server info; a browser on another Steam user lists it and joins to the host's id; private lobbies stay unlisted; invites and +connect_lobby parse.</summary>
    private static void SteamLobbiesAndBrowser()
    {
        var world = new RecordedSteamWorld();
        const ulong hostId = 76561197960288021, clientId = 76561197960288022;
        var host = SteamUser(world, hostId, "Host");
        var guest = SteamUser(world, clientId, "Guest");
        var hostLobbies = new SteamLobbies();
        var guestLobbies = new SteamLobbies();
        hostLobbies.Attach(host, hostId);
        guestLobbies.Attach(guest, clientId);
        try
        {
            ulong created = 0;
            hostLobbies.Created += id => created = id;
            hostLobbies.Host(SteamLobbyKind.Public, 8);
            host.RunCallbacks();
            var info = new ServerInfo("Steam host", "Harbor", 3, 8, 1, MatchPhase.Lobby, 0, ServerInfo.ProtocolVersion, GameMode.TeamDeathmatch, PhysicsMode.Classic, "", 0x1234, "", 0, "", 100);
            hostLobbies.UpdateHostData(info, SteamLobbyKind.Public, true);
            var listChanged = 0;
            guestLobbies.ListChanged += () => listChanged++;
            guestLobbies.RequestList();
            guest.RunCallbacks();
            var listed = guestLobbies.Listed;
            var row = listed.FirstOrDefault(l => l.LobbyId == created);
            Line($"lobby: created {created}, status '{hostLobbies.Status}', owner {hostLobbies.IsOwner}; guest listed {listed.Count} (changed {listChanged}): "
                 + $"{(row == null ? "none" : $"'{row.Info.Name}' {row.Info.MapName} {row.Info.Players}/{row.Info.MaxPlayers} {row.Info.Mode} host {row.HostSteamId} members {row.Members}/{row.MemberLimit} address {row.Address}")}");
            if (created == 0 || !hostLobbies.IsOwner || row == null || row.Info != info || row.HostSteamId != hostId || row.Members != 1 || row.MemberLimit != 8)
                Fail("lobby: the browser on another Steam user should list the host's lobby with its server info and host id");
            // Join through the lobby: the host's id comes back.
            (ulong Lobby, ulong Host, string Msg) joined = default;
            guestLobbies.Joined += (l, h, m) => joined = (l, h, m);
            guestLobbies.Join(created);
            guest.RunCallbacks();
            host.RunCallbacks();
            var membersAfter = host.LobbyMemberCount(created);
            Line($"lobby join: {joined}; members now {membersAfter}; guest in {guestLobbies.LobbyId}");
            if (joined.Lobby != created || joined.Host != hostId || membersAfter != 2 || guestLobbies.LobbyId != created)
                Fail("lobby join: joining the lobby should name the host and add the member");
            guestLobbies.Leave();
            // A friends-only lobby is not in the public list; an invite still names it.
            hostLobbies.Leave();
            hostLobbies.Host(SteamLobbyKind.FriendsOnly, 8);
            host.RunCallbacks();
            hostLobbies.UpdateHostData(info, SteamLobbyKind.FriendsOnly, true);
            guestLobbies.RequestList();
            guest.RunCallbacks();
            var publicRows = guestLobbies.Listed.Count;
            ulong requested = 0;
            guestLobbies.JoinRequested += id => requested = id;
            guest.RaiseLobbyJoinRequested(hostLobbies.LobbyId, hostId);
            guest.RunCallbacks();
            var viaInvite = requested;
            requested = 0;
            guest.RaiseRichPresenceJoin(SteamLobbies.ConnectStringOf(hostLobbies.LobbyId));
            guest.RunCallbacks();
            var viaPresence = requested;
            var viaArgs = SteamLobbies.LobbyOfArgs(["--steam", "+connect_lobby", "109775240000000009"]);
            var viaArgs2 = SteamLobbies.LobbyOfArgs(["+connect_lobby=5"]);
            var viaAddress = SteamLobbies.LobbyOfAddress(SteamLobbies.AddressOf(77));
            var notAddress = SteamLobbies.LobbyOfAddress("192.168.1.5");
            Line($"friends lobby: public rows {publicRows}; invite {viaInvite}, presence {viaPresence}, args {viaArgs}/{viaArgs2}, address {viaAddress}/{notAddress}; kind {hostLobbies.Kind} '{hostLobbies.Status}'");
            if (publicRows != 0 || viaInvite != hostLobbies.LobbyId || viaPresence != hostLobbies.LobbyId || viaArgs != 109775240000000009 || viaArgs2 != 5 || viaAddress != 77 || notAddress != 0)
                Fail("friends lobby: unlisted publicly, reachable by invite, Rich Presence, +connect_lobby and the browser address");
            // The browser merges a Steam row with the same game's LAN row (same hosting id) and keeps the direct address.
            var lan = new ServerBrowser.Entry { Address = "192.168.1.9", Info = info, PingMs = 3, Sources = ServerSource.Lan };
            var steamRow = new ServerBrowser.Entry { Address = SteamLobbies.AddressOf(created), Info = info, PingMs = 0, Sources = ServerSource.Steam };
            var merged = ServerBrowser.Merge([steamRow, lan]);
            Line($"browser merge: {merged.Count} row(s): {merged[0].Address} sources {merged[0].Sources}");
            if (merged.Count != 1 || merged[0].Address != "192.168.1.9" || merged[0].Sources != (ServerSource.Lan | ServerSource.Steam))
                Fail("browser merge: one row per game, the direct address first, both sources shown");
        }
        finally
        {
            hostLobbies.Detach(host);
            guestLobbies.Detach(guest);
            host.Shutdown();
            guest.Shutdown();
        }
    }

    /// <summary>Rich Presence: the keys set per state, and every token the game uses is in the localisation file the developer uploads.</summary>
    private static void SteamRichPresence()
    {
        var rec = new RecordedSteamBackend { LocalSteamId = 76561197960288031 };
        rec.Init(480, out _);
        var presence = new SteamPresence();
        presence.Attach(rec);
        try
        {
            presence.SetMenu();
            var menu = rec.RichPresence.GetValueOrDefault(SteamPresence.DisplayKey, "");
            presence.SetLobby("Team Deathmatch", "Harbor", 3, 8, 109775240000000001);
            var lobby = string.Join(" ", rec.RichPresence.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            presence.SetPlaying("Zombies", "Blackmoor", 2, 4, 12, 109775240000000001);
            var zombies = rec.RichPresence.GetValueOrDefault(SteamPresence.DisplayKey, "") + " round " + rec.RichPresence.GetValueOrDefault("round", "");
            presence.SetSinglePlayer("Free-for-all", "Quarry");
            var single = rec.RichPresence.ContainsKey(SteamPresence.ConnectKey);
            presence.Clear();
            Line($"presence: menu {menu}; lobby {lobby}; zombies {zombies}; single has connect {single}; cleared {rec.RichPresence.Count}");
            if (menu != SteamPresence.TokenMenu || !lobby.Contains("connect=+connect_lobby 109775240000000001") || !lobby.Contains("steam_display=#Status_Lobby") || !lobby.Contains("players=3") || !lobby.Contains("steam_player_group=") || zombies != "#Status_Zombies round 12" || single || rec.RichPresence.Count != 0)
                Fail("presence: the display token, substitutions and connect key should follow the session");
            // The localisation file (docs/steam/richpresence_english.txt) defines every token with every key it uses.
            var file = FindRepoFile(Path.Combine("docs", "steam", "richpresence_english.txt"));
            if (file == null)
            {
                Fail("presence: docs/steam/richpresence_english.txt is missing (the developer uploads it on the Steamworks site)");
                return;
            }
            var text = File.ReadAllText(file);
            var missing = SteamPresence.Tokens.Where(t => !text.Contains($"\"{t}\"")).ToList();
            var badKeys = System.Text.RegularExpressions.Regex.Matches(text, "%([a-z_]+)%").Select(m => m.Groups[1].Value).Distinct().Where(k => !SteamPresence.Keys.Contains(k)).ToList();
            Line($"presence file: {Path.GetFileName(file)}, {text.Length} chars; missing tokens {missing.Count}; unknown keys {string.Join(",", badKeys)}; ascii {text.All(c => c < 128)}");
            if (missing.Count > 0 || badKeys.Count > 0 || !text.Contains("\"english\"") || !text.Contains("\"tokens\"") || !text.All(c => c < 128))
                Fail($"presence file: every token the game sets must be defined ({string.Join(",", missing)}), using only the keys the game sets, in the Steam 'lang' format, ASCII");
        }
        finally
        {
            presence.Detach(rec);
            rec.Shutdown();
        }
    }

    private static string? FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>Auth tickets end to end: a client on Steam proves itself to a host on Steam over a loopback game (good ticket: verified; bad: kicked with the reason).</summary>
    private static void SteamAuthTickets()
    {
        // The game shares one Steam user in this process, so the client's ticket is for the host's own id: the recorded
        // Steam accepts that (user == claimed id, server == the host) exactly as a real one would for two users.
        var world = new RecordedSteamWorld();
        var rec = new RecordedSteamBackend(world) { LocalSteamId = 76561197960288041 };
        SteamPlatform.UseBackend(rec);
        var folder = Path.Combine(Path.GetTempPath(), "cos-steam-auth-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        try
        {
            SteamPlatform.Init(["--steam"], folder, 0);
            if (!SteamPlatform.Available)
            {
                Fail("auth: the recorded backend didn't start");
                return;
            }
            var map = FindMap("Proto Box", m => m.MaxPlayers >= 4)!;
            World.SetMap(map);
            for (var round = 0; round < 2; round++)
            {
                var bad = round == 1;
                if (bad)
                    world.BadTickets.Add(rec.LocalSteamId);
                const ushort port = 26332;
                var clock = Stopwatch.StartNew();
                var now = 0.0;
                var logged = new List<string>();
                using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id);
                server.MapVoteEnabled = false;
                server.WarmupEnabled = true;
                using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
                PumpSteamGames([rec], server, [client], 1.5, ref now, clock);
                var sessions = rec.AuthSessions.Count;
                var begun = rec.Calls.Count(c => c.StartsWith("BeginAuthSession("));
                var tickets = rec.Calls.Count(c => c.StartsWith("GetAuthSessionTicket("));
                var feed = string.Join(" | ", server.Feed.Select(f => f.Text).Where(t => t.Contains("Steam")));
                Line($"auth {(bad ? "bad" : "good")}: tickets {tickets}, begun {begun}, open sessions {sessions}; client '{client.Status}' disconnected {client.Disconnected}; host id on client {client.HostSteamId}; server feed: {feed}");
                if (tickets < 1 || begun < 1 || client.HostSteamId != rec.LocalSteamId)
                    Fail($"auth {(bad ? "bad" : "good")}: the client should ask for a ticket for the host and the host should begin a session with it");
                if (!bad && (client.Disconnected || sessions != 1 || !feed.Contains("verified")))
                    Fail("auth good: Steam's OK verdict keeps the player and the session open");
                if (bad && (!client.Disconnected || !client.Status.Contains("invalid ticket") || sessions != 0 || server.Players.Count != 1))
                    Fail("auth bad: a bad verdict kicks the player with the reason and ends the session");
            }
            var afterCancel = rec.Calls.Count(c => c.StartsWith("CancelAuthTicket(")) + rec.Calls.Count(c => c.StartsWith("EndAuthSession("));
            Line($"auth cleanup: cancel + end calls {afterCancel}, sessions open {rec.AuthSessions.Count}");
            if (afterCancel < 2 || rec.AuthSessions.Count != 0)
                Fail("auth cleanup: leaving cancels the ticket and ends the session");
        }
        finally
        {
            SteamPlatform.Shutdown();
            Directory.Delete(folder, true);
        }
    }
}
