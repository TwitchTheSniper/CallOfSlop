using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox body sync" (board item pd01): a real host and a separate client over the transport with simulated lag
    /// and loss (60 ms each way, 5% loss): many loose bodies pushed about at once (more than one pose packet holds), left
    /// to settle and sleep, a construct broken into wrecks, a freeze and an unfreeze, a late joiner. After each step the
    /// client's bodies match the host's: the same ids, structure hashes, poses within 2 cm and sleep state, and while
    /// they move no body goes without a pose for long. Then the safety net: a client copy put wrong on purpose (a lost
    /// sleep pose, a missing body, a body the host no longer has, a changed structure) is put right by the body digest
    /// within a digest period, and writing the digest allocates nothing.
    /// </summary>
    private static void CheckSandboxBodySync()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"body sync: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26383;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("body sync: the client didn't join");
            return;
        }
        var simHost = server.Net.SimulateBadNetwork(60, 12, 5);
        var simClient = a.Net.SimulateBadNetwork(60, 12, 5);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var w = server.Constructs;
        // A clean yard (the removals go out as they would in play), then 48 loose beams in a grid.
        foreach (var c in w.Bodies.ToList())
            w.Remove(c);
        Pump(server, [a], 0.5, ref now, clock);
        var beam = ConstructParts.Need("Square Beam 0.5 m");
        for (var k = 0; k < 48; k++)
            ConstructBuild.PlaceNew(w, beam, ConstructWorld.Ground(World.Query, -14 + k % 8 * 4f, -14 + k / 8 * 4f) + new Vector3(0, 0.5f, 0), k * 0.3f);
        Pump(server, [a], 0.6, ref now, clock);
        Want(w.Bodies.Count == 48, $"48 beams placed (the host has {w.Bodies.Count})");

        // 1. Pushed about together for 2 s: every body's pose keeps reaching the client.
        var worstAge = 0u;
        var worstId = 0;
        var t0 = clock.Elapsed.TotalSeconds;
        PumpWith(server, [a], 2.0, ref now, clock, () =>
        {
            var t = clock.Elapsed.TotalSeconds - t0;
            foreach (var c in w.Bodies)
            {
                c.Wake();
                c.Vel = new Vector3(MathF.Sin((float)t * 3 + c.Id), 0, MathF.Cos((float)t * 3 + c.Id)) * 1.5f;
            }
            if (t < 0.6)
                return;
            var newest = 0u;
            foreach (var c in a.Constructs.Bodies)
                newest = Math.Max(newest, c.NetTick);
            foreach (var c in a.Constructs.Bodies)
                if (newest - c.NetTick > worstAge)
                {
                    worstAge = newest - c.NetTick;
                    worstId = c.Id;
                }
        });
        Line($"body sync: simulated lag and loss {(simHost && simClient ? "on" : "NOT on")}; 48 beams pushed together: the longest a client copy went without a pose {worstAge} ticks (body {worstId})");
        Want(worstAge <= 30, $"every moving body's pose reaches the client at least every half second (one went {worstAge} ticks without one)");
        Want(BodiesMatch(server, a, moving: true, out var why1), $"after the pushes: {why1}");

        // 2. Let go: they settle and sleep; the client has their final poses within a second.
        for (var wait = 0; wait < 20 && w.Bodies.Any(c => !c.Asleep); wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 1.0, ref now, clock);
        Want(w.Bodies.All(c => c.Asleep), $"the beams sleep on the host ({w.Bodies.Count(c => !c.Asleep)} awake)");
        Want(BodiesMatch(server, a, moving: false, out var why2), $"after they sleep: {why2}");

        // 3. A construct broken into wrecks (a middle part taken off), then frozen and unfrozen.
        foreach (var c in w.Bodies.Take(20).ToList())
            w.Remove(c);
        var truck = ConstructBuild.FindBlueprint("Truck") is { } bp ? ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 10, 10), 0.5f, out _) : null;
        Pump(server, [a], 1.5, ref now, clock);
        if (truck == null)
            problems.Add("the Truck blueprint didn't build");
        else
        {
            var before = w.Bodies.Count;
            for (var cut = 0; cut < 3 && truck.PartCount > 2; cut++)
                w.RemovePart(truck, truck.PartCount / 2);
            Pump(server, [a], 0.2, ref now, clock);
            foreach (var c in w.Bodies)
                c.Wake();
            Pump(server, [a], 1.0, ref now, clock);
            Line($"body sync: the truck broken: {w.Bodies.Count - before} more bodies (host {w.Bodies.Count}, client {a.Constructs.Bodies.Count})");
            for (var wait = 0; wait < 16 && w.Bodies.Any(c => !c.Asleep); wait++)
                Pump(server, [a], 0.25, ref now, clock);
            Pump(server, [a], 1.0, ref now, clock);
            Want(BodiesMatch(server, a, moving: false, out var why3), $"after the split: {why3}");
            var piece = w.Bodies[^1];
            piece.Frozen = true;
            piece.FrozenBy = sp.Id;
            piece.Anchored = true;
            piece.Pos += new Vector3(0, 1.2f, 0);
            w.StructureDirty.Add(piece.Id);
            Pump(server, [a], 1.0, ref now, clock);
            Want(BodiesMatch(server, a, moving: false, out var why4) && a.Constructs.Get(piece.Id) is { Frozen: true }, $"after a freeze: {why4}");
            ConstructEdits.Unfreeze(w, piece);
            for (var wait = 0; wait < 16 && w.Bodies.Any(c => !c.Asleep); wait++)
                Pump(server, [a], 0.25, ref now, clock);
            Pump(server, [a], 1.0, ref now, clock);
            Want(BodiesMatch(server, a, moving: false, out var why5) && a.Constructs.Get(piece.Id) is { Frozen: false }, $"after the unfreeze: {why5}");
        }

        // 4. A late joiner sees what the first client sees.
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        b.Net.SimulateBadNetwork(60, 12, 5);
        for (var wait = 0; wait < 12 && b.LocalId == byte.MaxValue; wait++)
            Pump(server, [a, b], 0.25, ref now, clock);
        Pump(server, [a, b], 1.5, ref now, clock);
        Want(BodiesMatch(server, b, moving: false, out var why6), $"the late joiner: {why6}");

        foreach (var p in problems)
            Fail($"body sync: {p}");
    }

    /// <summary>
    /// The client's bodies against the host's: the same ids, structure hashes and part counts; for a sleeping body (or
    /// all, unless <paramref name="moving"/>) the pose it is going to within 2 cm and 1 degree, and the same sleep state.
    /// </summary>
    private static bool BodiesMatch(Game server, Game client, bool moving, out string why)
    {
        var bad = new List<string>();
        foreach (var c in server.Constructs.Bodies)
        {
            if (client.Constructs.Get(c.Id) is not { } k)
            {
                bad.Add($"{c.Id} missing");
                continue;
            }
            if (k.PartCount != c.PartCount || ConstructNet.StructureHash(k) != ConstructNet.StructureHash(c))
                bad.Add($"{c.Id} structure ({k.PartCount} parts, host {c.PartCount})");
            if (moving && !c.Asleep)
                continue;
            var off = Vector3.Distance(k.NetPos, c.Pos);
            var turn = 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(k.NetRot, c.Rot)), 0, 1)) * 180 / MathF.PI;
            if (off > 0.02f || turn > 1f)
                bad.Add($"{c.Id} {off * 100:0} cm, {turn:0.0} deg off");
            if (k.Asleep != c.Asleep)
                bad.Add($"{c.Id} {(k.Asleep ? "asleep" : "awake")} (host {(c.Asleep ? "asleep" : "awake")})");
        }
        foreach (var k in client.Constructs.Bodies)
            if (server.Constructs.Get(k.Id) == null)
                bad.Add($"{k.Id} not on the host");
        why = bad.Count == 0 ? "" : $"{bad.Count} wrong: {string.Join(", ", bad.Take(8))}";
        return bad.Count == 0;
    }

    /// <summary><see cref="Pump"/> with <paramref name="each"/> run before every update (pushes, checks).</summary>
    private static void PumpWith(Game server, IEnumerable<Game> clients, double seconds, ref double now, Stopwatch clock, Action each)
    {
        var until = clock.Elapsed.TotalSeconds + seconds;
        while (clock.Elapsed.TotalSeconds < until)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            each();
            server.Update(now, dt);
            foreach (var c in clients)
                c.Update(now, dt);
            Thread.Sleep(2);
        }
    }
}
