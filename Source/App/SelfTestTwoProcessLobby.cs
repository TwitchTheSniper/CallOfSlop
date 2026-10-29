using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Two-process lobby": the host's BACK TO LOBBY against a client in a process of its own
/// (<c>--net-client</c>, NetClientProcess.cs) over the real RakNet transport with simulated lag, so the two share no
/// statics (World.Map, the clock, the map library) the way two PCs don't. The in-process "Return to lobby" section
/// can't see a client that disagrees with the host about the map, or a reliable packet stuck behind a flood.
/// Host here, client there: Team Deathmatch match -> BACK TO LOBBY -> Zombies on Zombies Test -> BACK TO LOBBY ->
/// Horde Stress (its smallest crowd) -> BACK TO LOBBY -> a new match. After every return the client must be connected,
/// in the lobby, on the host's map and mode, with its name, team pick and gun kept on the host.
/// </summary>
public static partial class SelfTest
{
    private const ushort TwoProcessPort = 26197;

    private static void CheckTwoProcessLobby()
    {
        var tdmMap = FindMap("Proto Box", m => m.MaxPlayers >= 8)!;
        var zMap = ZTestMap;
        var stress = Maps.Find("Horde Stress");
        if (zMap == null || stress == null)
        {
            Fail("two-process lobby: the Zombies Test or Horde Stress map is missing");
            return;
        }
        World.SetMap(tdmMap);
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(TwoProcessPort, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: 1, mapId: tdmMap.Id)
        {
            MapVoteEnabled = true,
            LobbyEnabled = true,
            StressSize = Game.StressSizeChoices[0],
        };
        using var child = NetChild.Start(TwoProcessPort, lagMs: 80, name: "Remote");
        if (child == null)
        {
            Fail("two-process lobby: couldn't start the client process (--net-client)");
            return;
        }
        try
        {
            // Join: the child loads its own data first (a few seconds), then connects through the simulator.
            var joined = PumpUntil(server, child, NetChild.Windowed ? 120 : 40, ref now, clock, () => server.HumanCount == 2 && child.Is("connected", MatchPhase.Lobby));
            Line($"two-process lobby: joined {joined} after {clock.Elapsed.TotalSeconds:0.0} s: {child.Last}");
            if (!joined)
            {
                Fail($"two-process lobby: the client process didn't join the lobby room ({child.Last}; {child.Tail()})");
                return;
            }
            var remote = server.Players.Values.First(p => p.Bot == null && p.Id != server.LocalId);
            server.LobbySetMode(GameMode.TeamDeathmatch);
            PumpUntil(server, child, 5, ref now, clock, () => child.Has("mode TeamDeathmatch"));
            var gun = Weapons.All.FirstOrDefault(w => w.Id != Weapons.DefaultPrimary.Id && w.Slot == Slot.Primary && w.UnlockLevel is null or <= 1)?.Id ?? Weapons.DefaultPrimary.Id;
            child.Send("team 1");
            child.Send($"gun {gun}");
            var picked = PumpUntil(server, child, 5, ref now, clock, () => remote.TeamChoice == 1 && remote.Primary == gun);
            Line($"two-process lobby: Remote picks team 1 and gun {gun}: {picked}");

            var steps = new (string Label, Map Map, GameMode Mode)[]
            {
                ("Team Deathmatch", tdmMap, GameMode.TeamDeathmatch),
                ("Zombies", zMap, GameMode.Zombies),
                ("Horde Stress", stress, GameMode.Zombies),
                ("Team Deathmatch again", tdmMap, GameMode.TeamDeathmatch),
            };
            var match = server.MatchNumber;
            foreach (var (label, map, mode) in steps)
            {
                if (World.Map != map && !server.LobbySetMap(map, false))
                {
                    server.LobbySetMode(GameMode.FreeForAll); // (the mode must play on the map: Zombies needs a Zombies map)
                    server.LobbySetMap(map, false);
                }
                server.LobbySetMode(mode);
                var onMap = PumpUntil(server, child, 15, ref now, clock, () => child.Has($"map \"{map.Name}\"") && child.Has($"mode {mode} "));
                var sentStart = server.Net.BytesSent;
                server.LobbyStart(true, now);
                var playing = PumpUntil(server, child, 25, ref now, clock, () => server.Phase == MatchPhase.Playing && child.Is("connected", MatchPhase.Playing), speed: 4);
                sentStart = server.Net.BytesSent - sentStart;
                var sent0 = server.Net.BytesSent;
                var t0 = clock.Elapsed.TotalSeconds;
                if (mode == GameMode.Zombies)
                {
                    server.ZForceRound();
                    PumpUntil(server, child, 3, ref now, clock, () => false, speed: 2); // zombies on the way
                }
                else
                    PumpUntil(server, child, 1.5, ref now, clock, () => false);
                var inMatch = server.Players.TryGetValue(remote.Id, out var rm) ? (rm.TeamChoice, rm.Primary) : default;
                var rate = (server.Net.BytesSent - sent0) / 1024.0 / Math.Max(0.1, clock.Elapsed.TotalSeconds - t0);
                var sentBack = server.Net.BytesSent;
                var back = server.ReturnToLobbyNow(now);
                server.Update(now, 0.001);
                sentBack = server.Net.BytesSent - sentBack;
                var inRoom = PumpUntil(server, child, 10, ref now, clock, () => server.InLobby && child.Is("connected", MatchPhase.Lobby) && child.Has($"match {server.MatchNumber} "));
                PumpUntil(server, child, 2.5, ref now, clock, () => false); // stays connected a while in the room
                var stillThere = server.Players.TryGetValue(remote.Id, out var r) ? r : null;
                Line($"two-process lobby: {label} on {map.Name}: map synced {onMap}, playing {playing} ({sentStart / 1024.0:0} KB to get there; Remote: team {inMatch.TeamChoice}, gun {inMatch.Primary}; host sending {rate:0} KB/s), back {back} ({sentBack / 1024.0:0.0} KB at once), in the room {inRoom}; "
                     + $"host sees {server.HumanCount} people, Remote {(stillThere == null ? "gone" : $"team {stillThere.TeamChoice} gun {stillThere.Primary} name {stillThere.Name}")}; client: {child.Last}");
                if (!playing)
                    Fail($"two-process lobby: the {label} match didn't start on both ({child.Last})");
                else if (!back || !inRoom || child.Lost)
                    Fail($"two-process lobby: BACK TO LOBBY in {label} didn't keep the client process connected in the lobby room ({child.Last}; {child.Tail()})");
                else if (stillThere == null || stillThere.Name != "Remote")
                    Fail($"two-process lobby: BACK TO LOBBY in {label} lost the client or its name");
                else if (mode == GameMode.TeamDeathmatch && (inMatch.TeamChoice != 1 || inMatch.Primary != gun || stillThere.TeamChoice != 1))
                    Fail($"two-process lobby: the client's team pick or gun didn't carry into the {label} match and back");
                if (child.Lost)
                    return;
            }

            // And a new match from the room.
            server.LobbyStart(true, now);
            var again = PumpUntil(server, child, 25, ref now, clock, () => server.Phase == MatchPhase.Playing && child.Is("connected", MatchPhase.Playing), speed: 4);
            Line($"two-process lobby: new match {again}, matches {match}->{server.MatchNumber}, {clock.Elapsed.TotalSeconds:0.0} s in all; client: {child.Last}");
            if (!again || child.Lost)
                Fail($"two-process lobby: no new match from the room after BACK TO LOBBY ({child.Last})");
        }
        finally
        {
            child.Quit();
            World.SetMap(Maps.Get(0));
        }
    }

    /// <summary>Pumps the host (its clock <paramref name="speed"/> times real time; the network always real time) until <paramref name="done"/> or the client process gives up.</summary>
    private static bool PumpUntil(Game server, NetChild child, double seconds, ref double now, Stopwatch clock, Func<bool> done, double speed = 1)
    {
        var until = clock.Elapsed.TotalSeconds + seconds;
        var real = clock.Elapsed.TotalSeconds;
        while (clock.Elapsed.TotalSeconds < until)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp((t - real) * speed, 0.001, 0.1);
            real = t;
            now += dt;
            server.Update(now, dt);
            child.Drain();
            if (done())
                return true;
            if (child.Lost)
                return false;
            Thread.Sleep(2);
        }
        return done();
    }

    /// <summary>The client process: its [netclient] lines (the last state, a short tail), commands to its standard input.</summary>
    private sealed class NetChild : IDisposable
    {
        private readonly Process _p;
        private readonly ConcurrentQueue<string> _lines = new();
        private readonly List<string> _tail = [];
        public string Last { get; private set; } = "(nothing yet)";
        public bool Lost { get; private set; }

        private NetChild(Process p)
        {
            _p = p;
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    _lines.Enqueue(e.Data);
            };
            p.BeginOutputReadLine();
        }

        public static NetChild? Start(ushort port, int lagMs, string name)
        {
            try
            {
                var exe = Environment.ProcessPath ?? "";
                if (Windowed)
                {
                    var w = new ProcessStartInfo("xvfb-run") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, WorkingDirectory = AppContext.BaseDirectory };
                    foreach (var a in new[] { "-a", "-s", "-screen 0 1280x720x24", exe, typeof(SelfTest).Assembly.Location, "join", "127.0.0.1", port.ToString(), $"--lag={lagMs}", "--save-dir", "/tmp/twoproc-save" })
                        w.ArgumentList.Add(a);
                    return new NetChild(Process.Start(w)!);
                }
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    psi.ArgumentList.Add(typeof(SelfTest).Assembly.Location); // dotnet CallOfSlop.dll
                foreach (var a in new[] { "--net-client", "127.0.0.1", port.ToString(), $"--lag={lagMs}", "--name", name, "--seconds", "150" })
                    psi.ArgumentList.Add(a);
                var p = Process.Start(psi);
                return p == null ? null : new NetChild(p);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                Line($"two-process lobby: {ex.Message}");
                return null;
            }
        }

        public void Drain()
        {
            while (_lines.TryDequeue(out var line))
            {
                _tail.Add(line);
                if (_tail.Count > 6)
                    _tail.RemoveAt(0);
                if (line.StartsWith("[netclient] connected", StringComparison.Ordinal) || line.StartsWith("[netclient] joining", StringComparison.Ordinal)
                    || line.StartsWith("[netclient] disconnected", StringComparison.Ordinal))
                    Last = line;
                if (line.StartsWith("[netclient] lost the host", StringComparison.Ordinal) || line.StartsWith("[netclient] disconnected", StringComparison.Ordinal))
                    Lost = true;
            }
            if (_p.HasExited && _lines.IsEmpty)
                Lost = true;
        }

        public static bool Windowed = Environment.GetEnvironmentVariable("TWOPROC_WINDOW") == "1"; // TEMP experiment
        public bool Is(string state, MatchPhase phase) => Windowed || Last.StartsWith($"[netclient] {state} ", StringComparison.Ordinal) && Last.Contains($" phase {phase} ");
        public bool Has(string text) => Windowed || Last.Contains(text, StringComparison.Ordinal);
        public string Tail() => string.Join(" | ", _tail);

        public void Send(string command)
        {
            try
            {
                _p.StandardInput.WriteLine(command);
                _p.StandardInput.Flush();
            }
            catch (IOException)
            {
            }
        }

        public void Quit()
        {
            Send("quit");
            if (!_p.WaitForExit(5000))
                _p.Kill(true);
        }

        public void Dispose()
        {
            if (!_p.HasExited)
                _p.Kill(true);
            _p.Dispose();
        }
    }
}
