using System.Collections.Concurrent;
using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// A headless client in a process of its own: <c>--net-client HOST PORT | INVITE [--lag=ms] [--loss=%] [--name NAME]
/// [--seconds S]</c> (INVITE: an invite code or callofslop:// link, as a friend joins). It joins over the real RakNet transport (with RakNet's network simulator for --lag/--loss) and
/// runs the client game at 60 Hz with no window, the way a second PC would, so a host can be tested against a client
/// that shares nothing with it but the network. The "Two-process lobby" self-test (SelfTestTwoProcessLobby.cs) starts
/// one; it is also handy by hand against a real host.
///
/// Standard output gets one <c>[netclient]</c> line whenever what the client sees changes (connected, phase, match,
/// map, mode, players) and when asked with <c>state</c>; everything else goes to netclient.log. Standard input takes
/// commands, one per line: <c>team N</c>, <c>gun ID</c> (primary weapon id), <c>ready</c>, <c>state</c>, <c>quit</c>.
/// The exit code is 0 when it never lost the host, 2 when it did (the last line says why), 1 when it couldn't start.
/// Saves go to a throwaway folder, never the real one.
/// </summary>
public static class NetClientProcess
{
    public static int Run(string[] args)
    {
        var rest = args.SkipWhile(a => !a.Equals("--net-client", StringComparison.OrdinalIgnoreCase)).Skip(1).Where(a => !a.StartsWith("--")).ToArray();
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        string host;
        ushort port;
        if (rest.Length >= 1 && InviteCode.LooksLikeInvite(rest[0]))
        {
            // An invite code or link, the way a friend joins (Program.JoinFromInvite).
            if (!InviteCode.TryParseJoin(rest[0], 27015, out host, out port, out _, out var error))
            {
                stdout.WriteLine($"[netclient] bad invite: {error}");
                return 1;
            }
            stdout.WriteLine($"[netclient] joining {host}:{port} from an invite");
        }
        else if (rest.Length >= 2 && ushort.TryParse(rest[1], out port))
            host = rest[0];
        else
        {
            stdout.WriteLine("[netclient] usage: --net-client HOST PORT | INVITE [--lag=ms] [--loss=%] [--name NAME] [--seconds S]");
            return 1;
        }
        var name = Arg(args, "--name") ?? "NetClient";
        var seconds = double.TryParse(Arg(args, "--seconds"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 120;
        var lag = Flag(args, "--lag");
        var loss = Flag(args, "--loss");

        Brutal.GlfwApi.Glfw.Init(); // the clock only (Glfw.Time); no window
        SavePaths.UseFolder(Path.Combine(Path.GetTempPath(), $"callofslop-netclient-{Guid.NewGuid():N}"), "net client");
        NavBake.Enabled = false;
        MapBake.CacheWrites = false;
        var commands = new ConcurrentQueue<string>();
        var reader = new Thread(() =>
        {
            try
            {
                for (var line = Console.In.ReadLine(); line != null; line = Console.In.ReadLine())
                    commands.Enqueue(line.Trim());
            }
            catch (IOException)
            {
            }
            commands.Enqueue("quit"); // the parent went away
        }) { IsBackground = true, Name = "netclient stdin" };
        reader.Start();

        Game game;
        try
        {
            var net = NetPeer.Join(host, port);
            if (lag > 0 || loss > 0)
                stdout.WriteLine(net.SimulateBadNetwork(lag, lag / 5, loss)
                    ? $"[netclient] simulating {lag} ms latency and {loss}% loss"
                    : "[netclient] network simulator not available in this RakNet build");
            game = new Game(net, new Audio(), name, Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        }
        catch (Exception ex)
        {
            stdout.WriteLine($"[netclient] could not start: {ex.Message}");
            return 1;
        }

        var clock = Stopwatch.StartNew();
        var last = "";
        var lost = false;
        var quit = false;
        var t0 = Brutal.GlfwApi.Glfw.Time;
        var prev = t0;
        using (game)
        {
            while (!quit && clock.Elapsed.TotalSeconds < seconds)
            {
                var now = Brutal.GlfwApi.Glfw.Time;
                var dt = Math.Clamp(now - prev, 0.001, 0.1);
                prev = now;
                while (commands.TryDequeue(out var cmd))
                {
                    var parts = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    switch (parts.FirstOrDefault())
                    {
                        case "quit":
                            quit = true;
                            break;
                        case "team" when parts.Length > 1 && byte.TryParse(parts[1], out var team):
                            game.RequestTeam(team, now);
                            break;
                        case "gun" when parts.Length > 1 && byte.TryParse(parts[1], out var gun):
                            game.ChooseLoadout(gun, game.ChosenSecondary, game.ChosenGrenade, game.ChosenEquipment, 0, 0, now);
                            break;
                        case "ready":
                            game.ToggleReady();
                            break;
                        case "state":
                            last = ""; // print it again below
                            break;
                    }
                }
                if (!game.Disconnected)
                    game.Update(now, dt);
                var line = State(game, now - t0);
                if (line != last)
                {
                    stdout.WriteLine(line);
                    last = line;
                }
                if (game.Disconnected)
                {
                    lost = true;
                    break;
                }
                Thread.Sleep(1000 / 60);
            }
        }
        stdout.WriteLine(lost ? $"[netclient] lost the host: {game.Status}" : "[netclient] done, still connected");
        Brutal.GlfwApi.Glfw.Terminate();
        return lost ? 2 : 0;
    }

    /// <summary>What the client sees, one line (no clock in it, so it only changes when something does).</summary>
    private static string State(Game g, double t)
    {
        var me = g.Local;
        return $"[netclient] {(g.Disconnected ? "disconnected" : g.LocalId == byte.MaxValue ? "joining" : "connected")} id {g.LocalId} phase {g.Phase} "
               + $"match {g.MatchNumber} mode {g.Mode} map \"{World.Map.Name}\" players {g.Players.Count} team {me?.TeamChoice} gun {me?.Primary} status \"{g.Status}\"";
    }

    private static string? Arg(string[] args, string name) =>
        args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

    private static int Flag(string[] args, string name)
    {
        foreach (var a in args)
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a[(name.Length + 1)..], out var v))
                return v;
        return 0;
    }
}
