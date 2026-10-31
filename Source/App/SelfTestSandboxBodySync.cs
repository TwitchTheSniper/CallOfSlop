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
        Want(MatchWithin(server, a, ref now, clock, out var why2), $"after they sleep: {why2}");

        // 3. A construct broken into wrecks (a middle part taken off), then frozen and unfrozen.
        foreach (var c in w.Bodies.Take(20).ToList())
            w.Remove(c);
        var whyNot = "no 4x4 Truck blueprint, or no open ground";
        var truck = ConstructBuild.FindBlueprint("4x4 Truck") is { } bp && OpenSpot(w) is { } truckAt ? ConstructBuild.BuildOnGround(w, bp, truckAt, 0.5f, out whyNot) : null;
        Pump(server, [a], 1.5, ref now, clock);
        if (truck == null)
            problems.Add($"the Truck blueprint didn't build ({whyNot})");
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
            Want(MatchWithin(server, a, ref now, clock, out var why3), $"after the split: {why3}");
            var piece = w.Bodies[^1];
            piece.Frozen = true;
            piece.FrozenBy = sp.Id;
            piece.Anchored = true;
            piece.Pos += new Vector3(0, 1.2f, 0);
            w.StructureDirty.Add(piece.Id);
            Pump(server, [a], 1.0, ref now, clock);
            Want(MatchWithin(server, a, ref now, clock, out var why4) && a.Constructs.Get(piece.Id) is { Frozen: true }, $"after a freeze: {why4}");
            ConstructEdits.Unfreeze(w, piece);
            for (var wait = 0; wait < 16 && w.Bodies.Any(c => !c.Asleep); wait++)
                Pump(server, [a], 0.25, ref now, clock);
            Pump(server, [a], 1.0, ref now, clock);
            Want(MatchWithin(server, a, ref now, clock, out var why5) && a.Constructs.Get(piece.Id) is { Frozen: false }, $"after the unfreeze: {why5}");
        }

        // 4. A late joiner sees what the first client sees.
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        b.Net.SimulateBadNetwork(60, 12, 5);
        for (var wait = 0; wait < 12 && b.LocalId == byte.MaxValue; wait++)
            Pump(server, [a, b], 0.25, ref now, clock);
        // (Under 5% loss one lost reliable packet holds the joiner's ordered stream until it is sent again, so give it
        // up to 4 s; on a quiet link it matches within a few hundred ms.)
        var tJoin = clock.Elapsed.TotalSeconds;
        var joined = false;
        for (var wait = 0; wait < 16 && !joined; wait++)
        {
            Pump(server, [a, b], 0.25, ref now, clock);
            joined = BodiesMatch(server, b, moving: false, out _);
        }
        BodiesMatch(server, b, moving: false, out var why6);
        Line($"body sync: the late joiner matches the host {(joined ? $"after {clock.Elapsed.TotalSeconds - tJoin:0.0} s" : "NOT")}");
        Want(joined, $"the late joiner: {why6}");

        // 5. The safety net: the first client's copy put wrong on purpose (a sleep pose lost, a body missing, a body the
        // host no longer has, a structure changed) is put right by the digest within one digest period.
        var ac = a.Constructs;
        var sleepers = w.Bodies.Where(c => c.Asleep && c.PartCount > 0 && ac.Get(c.Id) != null).Take(3).ToList();
        if (sleepers.Count < 3)
            problems.Add($"only {sleepers.Count} sleeping bodies to put wrong");
        else
        {
            var lost = ac.Get(sleepers[0].Id)!;
            lost.NetPos += new Vector3(1, 0, 0);
            lost.NetFromPos = lost.Pos = lost.NetPos;
            lost.Asleep = false;
            ac.Remove(ac.Get(sleepers[1].Id)!);
            var changed = ac.Get(sleepers[2].Id)!;
            changed.Parts[0].Rot = (byte)((changed.Parts[0].Rot + 1) % Rot24.Count);
            const ushort phantomId = 60001;
            if (ac.Create(sleepers[0].Pos + new Vector3(0, 3, 0), Quaternion.Identity, phantomId) is { } phantom)
            {
                ac.AddPart(phantom, beam, Vector3.Zero, 0);
                phantom.Rebuild();
            }
            ac.Removed.Clear();
            ac.StructureDirty.Clear();
            var asked = a.ResyncAsked;
            var fixedIn = -1.0;
            var tFix = clock.Elapsed.TotalSeconds;
            for (var wait = 0; wait < 18 && fixedIn < 0; wait++)
            {
                Pump(server, [a, b], 0.25, ref now, clock);
                if (BodiesMatch(server, a, moving: false, out _))
                    fixedIn = clock.Elapsed.TotalSeconds - tFix;
            }
            BodiesMatch(server, a, moving: false, out var why7);
            Line($"body sync: a client copy put wrong (a lost sleep pose, a missing body, a phantom, a changed structure): put right in {(fixedIn < 0 ? "NOT" : $"{fixedIn:0.0} s")}, {a.ResyncAsked - asked} bodies asked for again");
            Want(fixedIn >= 0 && fixedIn <= ConstructNet.DigestEvery / (double)World.TickRate + 1.5, $"the digest puts a wrong client copy right within a digest period: {why7}");
            Want(a.ResyncAsked - asked >= 2, $"the client asked for the missing and the changed body again ({a.ResyncAsked - asked}; the lost pose may come back with the sleeping bodies' turn first)");
        }

        // 6. No allocation from the digest or the poses (the server writes them every few ticks).
        var dw = new PacketWriter();
        var cursor = 0;
        for (var warm = 0; warm < 3; warm++)
        {
            ConstructNet.WriteDigest(dw, w, 1, 0);
            ConstructNet.WritePoses(dw, w, 1, ref cursor);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 200; k++)
        {
            for (var from = 0; from < w.Bodies.Count;)
                from = ConstructNet.WriteDigest(dw, w, (uint)k, from);
            ConstructNet.WritePoses(dw, w, (uint)k, ref cursor);
        }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Line($"body sync: 200 digests and pose packets of {w.Bodies.Count} bodies: {bytes} bytes allocated");
        Want(bytes == 0, $"the digest and the poses allocate nothing ({bytes} bytes)");

        foreach (var p in problems)
            Fail($"body sync: {p}");
    }

    /// <summary>
    /// The client's bodies against the host's: the same ids, structure hashes and part counts; for a body asleep on the
    /// host the pose it is going to within 2 cm and 1 degree, and asleep; for one awake (unless <paramref name="moving"/>,
    /// when the caller checks the pose ages itself) a pose at most half a second old.
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
            if (!c.Asleep)
            {
                // Awake on the host: its poses stream, so the client's newest is recent (lag and one lost packet or two).
                if (!moving && server.ServerTick - k.NetTick > 30)
                    bad.Add($"{c.Id} awake, its last pose {server.ServerTick - k.NetTick} ticks old");
                continue;
            }
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

    /// <summary>
    /// <see cref="BodiesMatch"/> once the link has had its say: a lost pose or a body still settling (awake on a slow runner, its
    /// refresh a lost packet late) is not a wrong copy yet, so it is re-checked every quarter second for up to
    /// <paramref name="seconds"/> and fails with the last reason only if it never matches.
    /// </summary>
    private static bool MatchWithin(Game server, Game client, ref double now, Stopwatch clock, out string why, double seconds = 6.0)
    {
        var ok = BodiesMatch(server, client, moving: false, out why);
        for (var waited = 0.0; !ok && waited < seconds; waited += 0.25)
        {
            Pump(server, [client], 0.25, ref now, clock);
            ok = BodiesMatch(server, client, moving: false, out why);
        }
        return ok;
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
