using System.Diagnostics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Frame allocations": the main loop's steady frames allocate nothing. A loopback host on Horde Stress (a mass horde
/// round under way) with one client; every frame both games update and one of them draws its HUD and the F9 window
/// (closed, the full window, the corner overlay) into a headless ImGui context (<see cref="Ui.InitHeadless"/>). The
/// main thread's bytes per frame must stay at (almost) zero, and a failure names the perf areas that allocated
/// (<see cref="Perf.TrackAllocs"/>). The 3D scene isn't drawn headless: <c>--alloc-frames</c> (Program.AllocFrames.cs)
/// measures the real window's frames, render included.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Most bytes the least-allocating of three 60-frame rounds may allocate (a one-off, such as a log line, is allowed once).</summary>
    private const int FrameAllocAllowance = 0;

    private static void CheckFrameAllocs()
    {
        if (Maps.Find("Horde Stress") is not { Zombies: not null } map)
        {
            Fail("frame allocations: no Horde Stress map");
            return;
        }
        const ushort port = 26293;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            LobbyEnabled = true,
            WarmupEnabled = true,
        };
        server.ZRules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        server.ZRules.FirstRoundDelay = 0.5f;
        server.ZSeed = 31;
        server.Mode = GameMode.Zombies;
        server.StressSize = 1000;
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [client], 1.0, ref now, clock);
        server.LobbyStart(true, now);
        PumpFast(server, [client], Game.LobbyCountdownSeconds + 0.5, ref now);
        server.ForceStart();
        PumpFast(server, [client], Game.CountdownSeconds + 2.0, ref now);
        foreach (var p in server.Players.Values)
            p.ImmuneUntil = 1e9;
        if (server.Phase != MatchPhase.Playing || client.Phase != MatchPhase.Playing || server.Local is not { Alive: true } || client.Local is not { Alive: true }
            || server.ZPhase != ZRoundPhase.Round || server.Crowd.Total == 0)
        {
            Fail($"frame allocations: the loopback Horde Stress round didn't start (server {server.Phase} {server.ZPhase}, crowd {server.Crowd.Total}, client {client.Phase}, "
                 + $"host {(server.Local?.Alive == true ? "alive" : "not alive")}, client {(client.Local?.Alive == true ? "alive" : "not alive")})");
            return;
        }

        var ui = new Ui();
        ui.InitHeadless(new float2(1600, 900));
        var settings = new Settings();
        const double dt = 1.0 / 60;
        var wasOn = Perf.On;
        var setupSeconds = clock.Elapsed.TotalSeconds;
        try
        {
            void Frames(Game hud, PerfView view, int n)
            {
                for (var i = 0; i < n; i++)
                {
                    now += dt;
                    server.Update(now, dt);
                    client.Update(now, dt);
                    ui.PerfMode = view;
                    var perf = Perf.Measure(PerfArea.Ui); // (as the main loop times them)
                    ui.BeginFrame();
                    ui.DrawHud(hud, now, "", settings);
                    ui.DrawPerf(hud, now, settings.Bind(GameAction.PerfWindow));
                    ui.EndFrame();
                    perf.Dispose();
                    Perf.SetOn(true); // the areas are charged in every view (DrawPerf turns the scopes off with the window)
                    Perf.EndFrame();
                }
            }

            var report = new List<string>();
            var failures = new List<string>();
            foreach (var (hud, who) in new[] { (server, "host"), (client, "client") })
                foreach (var view in new[] { PerfView.Off, PerfView.Window, PerfView.Overlay })
                {
                    Perf.TrackAllocs = false;
                    Frames(hud, view, 40); // warm-up: JIT, the text caches, the view's first frames
                    Thread.Sleep(200); // the tiered JIT: methods get their optimised code a moment after they're first busy
                    Frames(hud, view, 20); // (unoptimised code can box where the optimised code doesn't)
                    Thread.Sleep(100);
                    Perf.TrackAllocs = true;
                    var least = long.MaxValue;
                    var areas = "";
                    var rounds = "";
                    NetPeer.TrackAllocs = true; // which received messages allocated (Net.cs), named in a failure
                    for (var round = 0; round < 3; round++)
                    {
                        Perf.ResetAllocs();
                        NetPeer.ResetAllocs();
                        AllocProfile.Reset(); // the game's own finer sections (AllocProfile marks in Game.Update and the server tick)
                AllocProfile.On = true;
                        var before = GC.GetAllocatedBytesForCurrentThread();
                        Frames(hud, view, 60);
                        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                        rounds += $"{(round > 0 ? ", " : "")}{bytes} B"; // (after the round: every round is named in a failure)
                        if (bytes > 0)
                            rounds += $" [{AllocAreas()}{NetMsgAllocs()}]";
                        if (bytes < least)
                        {
                            least = bytes;
                            areas = AllocAreas() + NetMsgAllocs();
                        }
                    }
                    NetPeer.TrackAllocs = false;
                    report.Add($"{who} {view} {least} B");
                    if (least > FrameAllocAllowance)
                        failures.Add($"{who} with the perf window {view}: {least} B over 60 frames ({areas}; the rounds: {rounds})");
                }
            CheckInputBurst(server, client, ref now, failures, report);
            Line($"frame allocations (loopback host and client on {map.Name}, {server.Crowd.Total} in the crowd, HUD and perf window drawn headless; least of three 60-frame rounds): {string.Join(", ", report)}");
            if (failures.Count > 0)
                Fail($"steady frames should allocate nothing on the main thread: {string.Join("; ", failures)}");
        }
        finally
        {
            Perf.TrackAllocs = false;
            NetPeer.TrackAllocs = false;
            Perf.SetOn(wasOn);
            ui.DisposeHeadless();
        }
    }

    /// <summary>
    /// A network stall's burst (the "Frame allocations" failures on Windows CI, 2026-10-30: "Net receive 1560 B", the
    /// server's input queue growing from 32 to 64 commands when a backlog arrived in one poll): a full queue's worth of
    /// input commands in one go must allocate nothing, and the client's own bounded lists start at their full size.
    /// </summary>
    private static void CheckInputBurst(Game server, Game client, ref double now, List<string> failures, List<string> report)
    {
        if (!server.Players.TryGetValue(client.LocalId, out var p))
        {
            failures.Add("input burst: the server has no player for the client");
            return;
        }
        var guid = server.SelfTestClientGuid(client.LocalId);
        var w = new PacketWriter(1024);
        var packets = new List<byte[]>();
        var seq = Math.Max(p.HighestQueuedSeq, p.LastProcessedSeq);
        for (var sent = 0; sent < Player.MaxQueuedInputs + 16; sent += 16)
        {
            w.Begin(Msg.Input).U8(16);
            for (var i = 1; i <= 16; i++)
                w.U32(seq + (uint)(sent + i)).U8(0).F32(p.Yaw).F32(p.Pitch).U8(p.Weapon).U8(100);
            for (var i = 0; i < 16; i++)
                w.U32(0);
            packets.Add(w.Span.ToArray());
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var packet in packets)
            server.SelfTestPacket(guid, packet, now);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var queued = p.InputQueue.Count;
        report.Add($"input burst {queued} queued {bytes} B");
        if (queued < Player.MaxQueuedInputs || bytes > 0)
            failures.Add($"input burst: {packets.Count * 16} commands in one go queued {queued} (want {Player.MaxQueuedInputs}) and allocated {bytes} B (want 0)");
        if (client.SelfTestPendingInputCapacity <= Game.MaxPendingInputs)
            failures.Add($"input burst: the client's unacknowledged inputs start at {client.SelfTestPendingInputCapacity}, not their full size");
    }

    /// <summary>The received message ids whose handlers allocated since <see cref="NetPeer.ResetAllocs"/> (both games),
    /// as "; messages: State x3 1560 B", or "" when none did (allocates: for the report).</summary>
    private static string NetMsgAllocs()
    {
        var parts = Enumerable.Range(0, 256).Where(i => NetPeer.AllocByMsg[i] > 0).OrderByDescending(i => NetPeer.AllocByMsg[i])
            .Select(i => $"{MsgName((byte)i)} x{NetPeer.CountByMsg[i]} {NetPeer.AllocByMsg[i]} B").ToList();
        return parts.Count > 0 ? $"; messages: {string.Join(", ", parts)}" : "";
    }

    /// <summary>A message id's name: a game <see cref="Msg"/>, a RakNet system message, or the number.</summary>
    private static string MsgName(byte id) =>
        Enum.IsDefined((Msg)id) ? ((Msg)id).ToString()
        : Enum.IsDefined((Brutal.RakNetApi.DefaultMessageIDTypes)id) ? ((Brutal.RakNetApi.DefaultMessageIDTypes)id).ToString()
        : $"id {id}";

    /// <summary>The perf areas that allocated since <see cref="Perf.ResetAllocs"/>, most first (allocates: for the report).</summary>
    private static string AllocAreas()
    {
        var parts = Enumerable.Range(0, Perf.Count + 1).Where(i => Perf.AllocSelf[i] > 0).OrderByDescending(i => Perf.AllocSelf[i])
            .Select(i => $"{(i == Perf.Count ? "outside scopes" : Perf.Names[i])} {Perf.AllocSelf[i]} B").ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : "no area";
    }
}
