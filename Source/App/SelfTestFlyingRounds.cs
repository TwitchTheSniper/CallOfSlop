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
            var sv = server.Constructs;
            var cl = a.Constructs;
            for (var end = now + 5; now < end && (sv.RoundCount == 0 || cl.RoundCount == 0);) // (it passes its reach, and the event arrives, in a while: for the state, not a fixed wait)
                Pump(server, [a], 0.05, ref now, clock);
            Pump(server, [a], 0.2, ref now, clock);
            // Same arc: each at its own age, so a late start or a slow tick on one peer doesn't read as a different path.
            var gap = -1f;
            if (sv.RoundCount > 0 && cl.RoundCount > 0)
            {
                var ahead = (sv.Rounds[0].Age - cl.Rounds[0].Age) * World.TickDt;
                gap = Vector3.Distance(sv.Rounds[0].Pos, cl.Rounds[0].Pos + cl.Rounds[0].Vel * ahead);
            }
            Line($"a real round: {sv.RoundCount} on the host, {cl.RoundCount} on the client, {gap:0.0} m off its arc");
            if (sv.RoundCount == 0 || cl.RoundCount == 0 || gap < 0 || gap > 6f)
                problems.Add($"a real round didn't fly on on both peers ({sv.RoundCount}, {cl.RoundCount}, {gap:0.0} m)");
            // Coming down: on a player and a construct, with what its speed left.
            server.Net.SimulateBadNetwork(0, 0, 0);
            sp.Health = 100;
            var head = sp.Chest + new Vector3(0, 12, 0);
            sv.AddRound(new CRound { Pos = head, Tested = head, Vel = new Vector3(0, -60, 0), Damage = 60, Muzzle = 350, By = sp.Id, Weapon = sp.Primary });
            var landed0 = sv.RoundsLanded;
            for (var end = now + 4; now < end && sv.RoundsLanded == landed0;)
                Pump(server, [a], 0.05, ref now, clock);
            Line($"on a player: health 100 -> {sp.Health}");
            if (sp.Health >= 100 || sp.Health < 80)
                problems.Add($"a falling round on a player left {sp.Health}");
            var plate = ConstructParts.Need("Deck Plate");
            var c = ConstructBuild.PlaceNew(sv, plate, new FreePlacement { Pos = ConstructWorld.Ground(World.Query, 60, 60) + new Vector3(0, 2, 0), Rot = Quaternion.Identity, Anchor = true })!;
            var hp0 = c.Parts[0].Health;
            var over = c.WorldCom + new Vector3(0, 12, 0);
            sv.AddRound(new CRound { Pos = over, Tested = over, Vel = new Vector3(0, -60, 0), Damage = 300, Muzzle = 350 });
            landed0 = sv.RoundsLanded;
            for (var end = now + 4; now < end && sv.RoundsLanded == landed0;)
                Pump(server, [a], 0.05, ref now, clock);
            Line($"on a part: health {hp0:0} -> {c.Parts[0].Health:0}");
            if (c.Parts[0].Health >= hp0)
                problems.Add("a falling round did nothing to a part");
        }
        else
            problems.Add("the client never joined");
        // A planetoid: no air, and a round follows the point gravity round it.
        if ((Maps.Find("Planetoid") ?? Maps.All.FirstOrDefault(m => m.Planetoid != null)) is { Planetoid: { } planet } round)
        {
            World.SetMap(round);
            var v = new ConstructWorld();
            var start = planet.Center + new Vector3(0, (float)planet.Radius + 600f, 0);
            v.AddRound(new CRound { Pos = start, Tested = start, Vel = new Vector3(300, 0, 0), Damage = 30, Muzzle = 300 });
            for (var t = 0; t < 120; t++)
                v.StepRounds(World.Query, World.TickDt, server: false);
            var pull = ConstructShells.GravityAt(start);
            var left = v.RoundCount > 0 ? v.Rounds[0].Vel : default;
            Line($"planetoid {round.Name}: pull {pull.Length():0.0} m/s2, after 2 s speed {left.Length():0}, its round {(v.RoundCount > 0 ? "flying" : "gone")}");
            if (pull.Length() < 0.01f || v.RoundCount == 0 || Vector3.Dot(left, Vector3.Normalize(pull)) <= 0 || left.Length() < 295f)
                problems.Add("a round on a planetoid didn't fall towards it");
        }
        World.SetMap(yard);
        foreach (var p in problems)
            Fail($"flying rounds: {p}");
    }
}
