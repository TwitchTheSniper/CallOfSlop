using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Return to lobby": the host's BACK TO LOBBY from the ordinary pause menu (board bug: it used to
/// be END MATCH, which closed the server and sent everyone to the main menu). Over loopback, a host, two clients and
/// bots in Team Deathmatch: the host goes back mid-match, everyone lands in the lobby room still connected with their
/// names, team picks, loadouts and the bots, a client leaving doesn't end it for the others, and a new match starts
/// from the room. Then Zombies: the run (zombies, round) is cleared on the host and the client. Plus the menu's labels.
/// </summary>
public static partial class SelfTest
{
    private static void CheckReturnToLobby()
    {
        CheckReturnToLobbyLabels();
        CheckReturnToLobbyLoopback();
        CheckReturnToLobbyZombies();
    }

    private static void CheckReturnToLobbyLabels()
    {
        var host = Ui.PauseExits(isServer: true, offline: false, canReturnToLobby: true);
        var hostInRoom = Ui.PauseExits(isServer: true, offline: false, canReturnToLobby: false);
        var client = Ui.PauseExits(isServer: false, offline: false, canReturnToLobby: false);
        var solo = Ui.PauseExits(isServer: true, offline: true, canReturnToLobby: false);
        Line($"pause exits: host \"{host.BackToLobby}\" / \"{host.Leave}\"; client \"{client.Leave}\"; single player \"{solo.Leave}\"");
        if (host.BackToLobby == null || !host.BackToLobby.Contains("LOBBY") || host.Leave.Contains("LOBBY") || !host.Leave.Contains("main menu"))
            Fail("the host's pause menu doesn't keep BACK TO LOBBY and the main menu apart");
        if (hostInRoom.BackToLobby != null || client.BackToLobby != null || solo.BackToLobby != null)
            Fail("BACK TO LOBBY offered where there is no lobby room to go back to (client, single player, already there)");
        if (client.Leave.Contains("LOBBY") || !client.Leave.Contains("main menu") || solo.Leave.Contains("LOBBY"))
            Fail("a leave button still says lobby for what is the main menu");
        foreach (var t in new[] { host.BackToLobby ?? "", host.Leave, host.LeaveHint, client.Leave, client.LeaveHint, solo.Leave, solo.LeaveHint })
            if (!IsPrintableAscii(t))
                Fail($"pause exit text isn't ASCII: {t}");
    }

    private static void CheckReturnToLobbyLoopback()
    {
        var map = FindMap("Proto Box", m => m.MaxPlayers >= 8)!;
        const ushort port = 60195;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2, mapId: map.Id)
        {
            MapVoteEnabled = true,
            LobbyEnabled = true,
        };
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.5, ref now, clock);
        server.LobbySetMode(GameMode.TeamDeathmatch);
        Pump(server, [a, b], 0.4, ref now, clock);
        a.RequestTeam(1);
        b.RequestTeam(0);
        var other = Weapons.All.FirstOrDefault(w => w.Id != Weapons.DefaultPrimary.Id && w.Slot == Slot.Primary);
        if (other != null)
            a.ChooseLoadout(other.Id, Weapons.DefaultSecondary.Id, a.ChosenGrenade, a.ChosenEquipment, 0, 0, now);
        Pump(server, [a, b], 0.5, ref now, clock);
        server.LobbyStart(true, now);
        PumpFast(server, [a, b], Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.7, ref now);
        var playing = server.Phase == MatchPhase.Playing && a.Phase == MatchPhase.Playing && b.Phase == MatchPhase.Playing;
        var match = server.MatchNumber;
        var names = string.Join(",", server.Players.Values.OrderBy(p => p.Id).Select(p => p.Name));
        var bots = server.BotCount;
        server.Players[a.LocalId].Kills = 3;

        // A client can't take everyone back; only the host can.
        var clientRefused = !a.CanReturnToLobby && !a.ReturnToLobbyNow(now) && a.Phase == MatchPhase.Playing;
        var back = server.ReturnToLobbyNow(now);
        Pump(server, [a, b], 1.0, ref now, clock);
        var room = (server.Phase, a.Phase, b.Phase);
        var inMap = server.Players.Values.Count(p => p.Alive);
        var namesAfter = string.Join(",", server.Players.Values.OrderBy(p => p.Id).Select(p => p.Name));
        var sa = server.Players[a.LocalId];
        var sb = server.Players[b.LocalId];
        var kept = (Mode: a.Mode, TeamA: sa.TeamChoice, TeamB: sb.TeamChoice, Gun: sa.Primary, Kills: sa.Kills, Bots: server.BotCount);
        Line($"return to lobby: playing {playing}, client refused {clientRefused}, host back {back} -> {room}, connected {!a.Disconnected}/{!b.Disconnected}, "
             + $"{server.Players.Count} players ({a.Players.Count}/{b.Players.Count} on the clients), {inMap} in the map, vote {server.VoteOpen}; names same {names == namesAfter}, "
             + $"{kept.Mode}, teams {kept.TeamA}/{kept.TeamB}, Alpha's gun on the host {kept.Gun} (picked {other?.Id}), kills {kept.Kills}, bots {bots}->{kept.Bots}");
        if (!playing)
            Fail("the hosted match didn't get going with two clients");
        else if (!clientRefused)
            Fail("a client could take everyone back to the lobby");
        else if (!back || room != (MatchPhase.Lobby, MatchPhase.Lobby, MatchPhase.Lobby) || inMap != 0 || server.VoteOpen)
            Fail("the host's BACK TO LOBBY didn't put everyone in the lobby room");
        else if (a.Disconnected || b.Disconnected || a.Players.Count != server.Players.Count || b.Players.Count != server.Players.Count || names != namesAfter)
            Fail("BACK TO LOBBY lost a player or a name");
        else if (kept.Mode != GameMode.TeamDeathmatch || kept.TeamA != 1 || kept.TeamB != 0 || kept.Bots != bots || kept.Kills != 0
                 || (other != null && kept.Gun != other.Id))
            Fail("BACK TO LOBBY didn't keep the mode, team picks, loadout and bots (or kept the old score)");

        // A client leaving (LEAVE GAME) doesn't end it for the others.
        var bId = b.LocalId;
        b.Dispose();
        Pump(server, [a], 1.0, ref now, clock);
        var afterLeave = (server.Phase, a.Phase, Gone: !server.Players.ContainsKey(bId), Connected: !a.Disconnected);
        var again = server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.7, ref now);
        Line($"client leaves: {afterLeave}; start again {again} -> {server.Phase}/{a.Phase}, match {match}->{server.MatchNumber}");
        if (afterLeave != (MatchPhase.Lobby, MatchPhase.Lobby, true, true))
            Fail("a client leaving disturbed the lobby for the others");
        if (!again || server.Phase != MatchPhase.Playing || a.Phase != MatchPhase.Playing || a.Disconnected || server.MatchNumber != match + 1)
            Fail("a new match didn't start from the lobby room after BACK TO LOBBY");
        World.SetMap(Maps.Get(0));
    }

    private static void CheckReturnToLobbyZombies()
    {
        if (ZTestMap is not { } map)
        {
            Fail("no Zombies Test map for the Zombies return to lobby check");
            return;
        }
        const ushort port = 60196;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1, mapId: map.Id)
        {
            MapVoteEnabled = true,
            LobbyEnabled = true,
        };
        server.Mode = GameMode.Zombies;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.5, ref now);
        server.ZForceRound();
        PumpFast(server, [a], ZombiesRules.Current.FirstRoundDelay + 2.5, ref now);
        var run = (Round: server.ZRound, Horde: server.Horde.Count, Client: a.Horde.Count, server.Phase);
        var back = server.ReturnToLobbyNow(now);
        Pump(server, [a], 0.8, ref now, clock);
        var after = (Round: server.ZRound, Horde: server.Horde.Count, Client: a.Horde.Count, server.Phase, ClientPhase: a.Phase);
        var again = server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.5, ref now);
        Line($"zombies: run {run}; back {back} -> {after}, connected {!a.Disconnected}, {server.Players.Count} players; start again {again} -> {server.Phase}/{a.Phase} in {a.Mode}");
        if (run.Phase != MatchPhase.Playing || (run.Round < 1 && run.Horde == 0))
            Fail("the hosted Zombies run didn't get going");
        else if (!back || after.Phase != MatchPhase.Lobby || after.ClientPhase != MatchPhase.Lobby || a.Disconnected)
            Fail("BACK TO LOBBY in Zombies didn't take everyone to the lobby room");
        else if (after.Round != 0 || after.Horde != 0 || after.Client != 0)
            Fail("BACK TO LOBBY in Zombies left the run's zombies or round behind");
        else if (!again || server.Phase != MatchPhase.Playing || a.Phase != MatchPhase.Playing || a.Mode != GameMode.Zombies)
            Fail("a new Zombies match didn't start from the lobby room");
        World.SetMap(Maps.Get(0));
    }
}
