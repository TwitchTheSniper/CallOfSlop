using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>"Flying rounds": a round shot straight up comes down, slowed, and hurts less for it; a full pool stays within its budget; a real hand gun's round flies on, on the client too.</summary>
public static partial class SelfTest
{
    private static void CheckFlyingRounds()
    {
        var yard = Maps.Find("Proving Ground")!;
        yard.RestoreProps();
        World.SetMap(yard);
        var problems = new List<string>();
        var w = new ConstructWorld();
        var ground = ConstructWorld.Ground(World.Query, 0, 0);
        w.AddRound(new CRound { Pos = ground + new Vector3(0, 2, 0), Tested = ground + new Vector3(0, 2, 0), Vel = new Vector3(0, 350, 0), Damage = 30, Muzzle = 350 });
        var top = 0f;
        var ticks = 0;
        var last = 0f;
        while (w.RoundCount > 0 && ticks < ConstructWorld.RoundAirAge + 5)
        {
            last = w.Rounds[0].Vel.Length();
            top = MathF.Max(top, w.Rounds[0].Pos.Y - ground.Y);
            w.StepRounds(World.Query, World.TickDt, server: true);
            ticks++;
        }
        Line($"straight up: {top:0} m high, down in {ticks * World.TickDt:0.0} s, landing at {last:0} m/s, {30f * last / 350f:0.0} of 30 damage");
        if (w.RoundsLanded != 1 || top < 500 || top > 1500 || last > 70 || last < 45)
            problems.Add($"a round shot straight up: landed {w.RoundsLanded}, {top:0} m, {last:0} m/s");

        // A full pool, up in the air: the tick is bounded, and nothing allocates.
        for (var i = 0; i < ConstructWorld.MaxRounds + 10; i++)
        {
            var at = ground + new Vector3(i % 40, 300, i / 40);
            w.AddRound(new CRound { Pos = at, Tested = at, Vel = new Vector3(40, 10, 0), Damage = 30, Muzzle = 350 });
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock0 = Stopwatch.GetTimestamp();
        for (var t = 0; t < 60; t++)
            w.StepRounds(World.Query, World.TickDt, server: true);
        var ms = (Stopwatch.GetTimestamp() - clock0) * 1000.0 / Stopwatch.Frequency / 60;
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"full pool: {w.RoundCount} of {ConstructWorld.MaxRounds}, {ms:0.00} ms a tick, {alloc} B allocated in 60 ticks");
        if (w.RoundCount != ConstructWorld.MaxRounds || alloc > 0)
            problems.Add("a full pool went over its cap or allocated");

        // A real gun, straight up, host and client.
        const ushort port = 26405;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.5, ref now, clock);
        if (server.Players.TryGetValue(a.LocalId, out var sp))
        {
            for (var i = 0; i < 40 && !sp.Alive; i++)
            {
                sp.WantsRespawn = true;
                Pump(server, [a], 0.2, ref now, clock);
            }
            var build = StandardBuilds.All.Select(b => b.Build()).First(b => b.Kind == BuildKind.Gun && b.Receivers.Count == 1 && b.Receivers[0].Part.Receiver!.Bore == "rifle" && PowderCharge.Takes(b.Receivers[0].Part)).ToText();
            server.SandboxBench(sp, Game.BenchSpawn, 0, build);
            Pump(server, [a], 0.6, ref now, clock);
            a.LookPitch = 1.5f;
            a.NextFireTime = 0;
            a.EquipUntil = 0;
            a.SelfTestWeaponFrame(Brutal.GlfwApi.Glfw.Time, 0.02f);
            a.UpdateTrigger(Brutal.GlfwApi.Glfw.Time, true, true);
            a.UpdateTrigger(Brutal.GlfwApi.Glfw.Time, false, false);
            Pump(server, [a], 0.8, ref now, clock);
            var sv = server.Constructs;
            var cl = a.Constructs;
            var gap = sv.RoundCount > 0 && cl.RoundCount > 0 ? Vector3.Distance(sv.Rounds[0].Pos, cl.Rounds[0].Pos) : -1f;
            Line($"a real round: {sv.RoundCount} on the host, {cl.RoundCount} on the client, {gap:0.0} m apart");
            if (sv.RoundCount == 0 || cl.RoundCount == 0 || gap > 40f)
                problems.Add("a real round didn't fly on on both peers");
        }
        else
            problems.Add("the client never joined");
        foreach (var p in problems)
            Fail($"flying rounds: {p}");
    }
}
