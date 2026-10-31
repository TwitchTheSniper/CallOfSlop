using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct scale" (board item cl01): the fixed caps are gone (body 65 and part 1,201 go in; the host's safety cap
    /// refuses with a reason); 2,000 bodies placed (mostly asleep) with 100 awake: the server tick stays inside its budget
    /// (CPU time, Windows), allocates nothing and simulates only the awake ones and what they touch; a sleeping body woken
    /// by a push simulates and sleeps again, and with everything asleep the tick is nearly free; ray tests and player
    /// blocking look at the bodies near them, not all 2,000; a real host and a separate client: a joiner gets all 2,000
    /// streamed within a few seconds, every copy exactly the host's (the world's state round-trips), and the poses reach a
    /// client by its interest. Short and seeded: no long simulation.
    /// </summary>
    private static void CheckConstructScale()
    {
        ScaleLimitsGone();
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        ScaleWorld(w, sleeping: 1900, awake: 100);
        ScaleTick(w, q);
        ScaleWake(w, q);
        ScaleQueries(w);
        ScaleNet();
    }

    /// <summary>The old limits (64 bodies, 1,200 parts) are gone; the host's safety cap refuses past it, with a reason.</summary>
    private static void ScaleLimitsGone()
    {
        var w = new ConstructWorld();
        var block = ConstructParts.Need("Frame Block");
        for (var i = 0; i < 64; i++)
            ConstructBuild.PlaceNew(w, block, new Vector3(i * 2, 0, 0), 0);
        var b65 = ConstructBuild.PlaceNew(w, block, new Vector3(0, 0, 4), 0);
        Line($"scale: body 65 {(b65 != null ? "goes in" : "is refused")} ({w.Bodies.Count} bodies; safety cap {w.BodyCap} bodies, {w.PartCap} parts)");
        if (b65 == null)
            Fail("construct scale: the 65th body was refused (the old MaxBodies = 64 cap is still there)");
        // More than 1,200 parts in all: 14 bodies of 90 parts each.
        var big = new ConstructWorld();
        var parts = 0;
        for (var k = 0; k < 14; k++)
        {
            var c = big.Create(new Vector3(k * 30, 0, 0), Quaternion.Identity);
            for (var i = 0; c != null && i < 90; i++)
                if (big.AddPart(c, block, new Vector3(i % 10 * 0.5f, i / 10 * 0.5f, 0), 0) >= 0)
                    parts++;
        }
        Line($"scale: {parts} parts in {big.Bodies.Count} bodies (the old cap was 1,200)");
        if (parts < 1260)
            Fail($"construct scale: only {parts} of 1,260 parts went in (the old MaxTotalParts = 1200 cap is still there)");
        // The safety cap: a host's low limit refuses the next body, and says why.
        var capped = new ConstructWorld { BodyCap = 3 };
        for (var i = 0; i < 3; i++)
            ConstructBuild.PlaceNew(capped, block, new Vector3(i * 2, 0, 0), 0);
        var over = ConstructBuild.PlaceNew(capped, block, new Vector3(0, 0, 4), 0);
        var why = ConstructPlace.LimitRefusal(capped, 0) ?? "";
        Line($"scale: with a host cap of 3 the 4th body {(over == null ? "is refused" : "goes in")}: \"{why}\"");
        if (over != null || !why.Contains('3'))
            Fail($"construct scale: the host's safety cap didn't refuse the 4th body with its reason (\"{why}\")");
    }

    /// <summary>
    /// A world of <paramref name="sleeping"/> resting bodies (single blocks in rows, every 20th a Buggy) and
    /// <paramref name="awake"/> blocks falling onto an open patch beside them (seeded positions and spins).
    /// </summary>
    private static void ScaleWorld(ConstructWorld w, int sleeping, int awake)
    {
        var block = ConstructParts.Need("Frame Block");
        var buggy = ConstructBuild.FindBlueprint("Buggy");
        var side = (int)MathF.Ceiling(MathF.Sqrt(sleeping));
        for (var i = 0; i < sleeping; i++)
        {
            var at = new Vector3(i % side * 3f, 0, i / side * 3f);
            var c = i % 20 == 10 && buggy != null ? ConstructBuild.BuildOnGround(w, buggy, at, 0, out _) : ConstructBuild.PlaceNew(w, block, at, 0);
            if (c == null)
                continue;
            c.Rebuild();
            c.Asleep = true; // (at rest, as a world that settled is)
        }
        var rng = new Random(1234);
        var aside = (int)MathF.Ceiling(MathF.Sqrt(awake));
        for (var i = 0; i < awake; i++)
        {
            var at = new Vector3(-60 - i % aside * 2.5f, 1.5f + (float)rng.NextDouble() * 1.5f, i / aside * 2.5f);
            if (ConstructBuild.PlaceNew(w, block, at, (float)rng.NextDouble() * 6) is { } c)
            {
                c.Rebuild();
                c.AngVel = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 3;
            }
        }
    }

    /// <summary>The tick with 2,000 bodies (100 awake): inside its budget, no allocation, only the awake ones and what they touch simulated.</summary>
    private static void ScaleTick(ConstructWorld w, IWorldQuery q)
    {
        var cpu = new TickCpu();
        for (var t = 0; t < 6; t++)
            w.Step(q, World.TickDt); // (warm-up: JIT, the arrays grow to the world)
        var awake = w.Bodies.Count(b => !b.Asleep);
        double worstWall = 0, sumWall = 0;
        var maxSim = 0;
        const int ticks = 30;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < ticks; t++)
        {
            var sw = Stopwatch.StartNew();
            cpu.Start();
            w.Step(q, World.TickDt);
            var ms = sw.Elapsed.TotalMilliseconds;
            cpu.Stop(ms, true);
            worstWall = Math.Max(worstWall, ms);
            sumWall += ms;
            maxSim = Math.Max(maxSim, ConstructPhysics.LastSimulated);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"scale tick: {w.Bodies.Count} bodies, {w.TotalParts} parts, {awake} awake; up to {maxSim} simulated a tick; "
            + $"wall {sumWall / ticks:0.000} ms a tick (worst {worstWall:0.000}), CPU worst {cpu.WorstText} ({cpu.WorstDetail}); {bytes} bytes allocated in {ticks} ticks");
        if (w.Bodies.Count < 2000)
            Fail($"construct scale: only {w.Bodies.Count} of 2,000 bodies were placed");
        if (bytes > 0)
            Fail($"construct scale: {bytes} bytes allocated in {ticks} ticks of 2,000 bodies (should be none)");
        if (maxSim > awake * 3)
            Fail($"construct scale: {maxSim} bodies simulated a tick with {awake} awake (sleeping ones should cost nothing)");
        if (TickCpu.Available && cpu.WorstMs > MaxTickWorkMs)
            Fail($"construct scale: the busiest tick took {cpu.WorstMs:0.00} ms of CPU with 2,000 bodies (budget {MaxTickWorkMs} ms; {cpu.WorstDetail})");
    }

    /// <summary>A sleeping body in the middle of the rows, pushed: it simulates, comes to rest and sleeps again; then everything asleep costs next to nothing.</summary>
    private static void ScaleWake(ConstructWorld w, IWorldQuery q)
    {
        // The middle of the sleeping rows: a plain block.
        var target = w.Bodies.Where(b => b.Asleep && b.PartCount == 1 && b.Pos.X > 40 && b.Pos.Z > 40).OrderBy(b => b.Id).FirstOrDefault();
        if (target == null)
        {
            Fail("construct scale: no sleeping block in the rows to push");
            return;
        }
        var from = target.Pos;
        target.Wake();
        target.Vel = new Vector3(2.5f, 1.5f, 0); // (a push: a tool's shove, a blast's or a contact's all wake a body so)
        var simulated = false;
        var slept = -1;
        for (var t = 0; t < 900 && slept < 0; t++)
        {
            w.Step(q, World.TickDt);
            simulated |= target.Pos != from;
            if (simulated && target.Asleep)
                slept = t;
        }
        var moved = Vector3.Distance(from, target.Pos);
        Line($"scale wake: the pushed block moved {moved:0.00} m and slept again after {slept} ticks; {w.Bodies.Count(b => !b.Asleep)} bodies awake");
        if (!simulated || moved < 0.2f)
            Fail($"construct scale: a sleeping body pushed awake didn't simulate (moved {moved:0.00} m)");
        if (slept < 0)
            Fail("construct scale: a pushed body never slept again (15 s)");
        // Everything at rest: the tick does next to nothing.
        for (var t = 0; t < 600 && w.Bodies.Any(b => !b.Asleep && !b.Anchored); t++)
            w.Step(q, World.TickDt);
        var sw = Stopwatch.StartNew();
        const int ticks = 30;
        var sim = 0;
        for (var t = 0; t < ticks; t++)
        {
            w.Step(q, World.TickDt);
            sim = Math.Max(sim, ConstructPhysics.LastSimulated);
        }
        var ms = sw.Elapsed.TotalMilliseconds / ticks;
        Line($"scale rest: {w.Bodies.Count(b => !b.Asleep && !b.Anchored)} free bodies awake, {sim} simulated, {ms:0.000} ms a tick (wall)");
        if (sim > 5)
            Fail($"construct scale: {sim} bodies simulated with the world at rest");
    }

    /// <summary>Ray tests and player blocking look at the bodies near them (the broadphase), not at every body in the world.</summary>
    private static void ScaleQueries(ConstructWorld w)
    {
        var block = w.Bodies.Where(b => b.Asleep && b.PartCount == 1 && b.Pos.X > 30 && b.Pos.Z > 30).OrderBy(b => b.Id).First();
        var top = block.WorldCom + new Vector3(0, 5, 0);
        var hitDown = w.Raycast(top, -Vector3.UnitY, 10, out var hit);
        var downCand = w.LastCandidates;
        var flat = w.Raycast(block.WorldCom + new Vector3(-1.5f, 0, 0.01f), Vector3.UnitX, 40, out var hit2);
        var flatCand = w.LastCandidates;
        var s = new MoveState { Position = block.Pos + new Vector3(0.9f, 0, 0) };
        w.BlockPlayer(ref s, World.PlayerHeight, server: true);
        var blockCand = w.LastCandidates;
        Line($"scale queries: of {w.Bodies.Count} bodies, a ray down looks at {downCand} (hit {(hitDown && hit.Body == block ? "the block" : "something else")}), "
            + $"a 40 m ray along a row at {flatCand} (hit {(flat ? hit2.Body.Id.ToString() : "nothing")}), a player's blocking at {blockCand}");
        if (!hitDown || hit.Body != block)
            Fail("construct scale: a ray straight down onto a block in the rows missed it");
        if (!flat)
            Fail("construct scale: a ray along a row of blocks hit nothing");
        if (downCand > 20 || blockCand > 20 || flatCand > 60)
            Fail($"construct scale: the queries looked at too many bodies (ray down {downCand}, ray along {flatCand}, blocking {blockCand}): the broadphase isn't used");
        // The same answers as walking every body: a few hundred seeded rays through the rows.
        var rng = new Random(77);
        var mismatches = 0;
        for (var k = 0; k < 200; k++)
        {
            var o = new Vector3((float)rng.NextDouble() * 140 - 5, (float)rng.NextDouble() * 3, (float)rng.NextDouble() * 140 - 5);
            var d = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, -(float)rng.NextDouble() * 0.3f, (float)rng.NextDouble() - 0.5f));
            var a = w.Raycast(o, d, 30, out var ha);
            var b = RaycastAll(w, o, d, 30, out var hb);
            if (a != b || a && (ha.Body != hb.Body || MathF.Abs(ha.Distance - hb.Distance) > 1e-4f))
                mismatches++;
        }
        Line($"scale queries: 200 seeded rays through the rows, {mismatches} answered differently from a walk over every body");
        if (mismatches > 0)
            Fail($"construct scale: {mismatches} of 200 rays gave another answer through the broadphase than over every body");
    }

    /// <summary>The nearest hit over every body (what the broadphase must agree with): each body's own boxes.</summary>
    private static bool RaycastAll(ConstructWorld w, Vector3 o, Vector3 d, float max, out ConstructHit best)
    {
        best = default;
        var found = false;
        var one = new ConstructWorld();
        foreach (var c in w.Bodies)
        {
            // (a one-body world walks its list: the old way)
            one.Bodies.Clear();
            one.Bodies.Add(c);
            if (one.Raycast(o, d, max, out var h) && (!found || h.Distance < best.Distance))
            {
                best = h;
                found = true;
            }
        }
        return found;
    }

    /// <summary>A real host and a separate client over the transport: a joiner gets all 2,000 within a few seconds, each copy the host's; poses by interest.</summary>
    private static void ScaleNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"construct scale: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26391;
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
            Fail("construct scale: the first client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        // 2,000 bodies on the host: the rows, asleep, round the client's player, and a few awake near and far.
        var w = server.Constructs;
        var block = ConstructParts.Need("Frame Block");
        var origin = sp.Position + new Vector3(-30, 0, -30);
        var have = w.Bodies.Count;
        for (var i = 0; w.Bodies.Count < 2000 && i < 4000; i++)
        {
            var at = origin + new Vector3(i % 50 * 1.5f, 0, i / 50 * 1.5f);
            if (ConstructBuild.PlaceNew(w, block, new Vector3(at.X, -20, at.Z), 0) is { } c)
            {
                c.Pinned = true; // (held where they are: below the yard, out of everything's way)
                c.Rebuild();
                c.Asleep = true;
            }
        }
        Pump(server, [a], 1.0, ref now, clock);
        var aHas = a.Constructs.Bodies.Count;
        Line($"scale net: the host has {w.Bodies.Count} bodies ({have} of the yard's), the connected client {aHas}");
        if (aHas != w.Bodies.Count)
            Fail($"construct scale: the connected client has {aHas} of the host's {w.Bodies.Count} bodies");

        // A joiner: the world streamed.
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        var t0 = clock.Elapsed.TotalSeconds;
        double? all = null;
        var maxStreaming = 0;
        while (clock.Elapsed.TotalSeconds - t0 < 10 && all == null)
        {
            Pump(server, [a, b], 0.1, ref now, clock);
            maxStreaming = Math.Max(maxStreaming, server.SandboxStreaming);
            if (b.Constructs.Bodies.Count == w.Bodies.Count && server.SandboxStreaming == 0)
                all = clock.Elapsed.TotalSeconds - t0;
        }
        var same = 0;
        foreach (var c in w.Bodies)
            if (b.Constructs.Get(c.Id) is { } copy && copy.PartCount == c.PartCount && copy.Parts[0].Def == c.Parts[0].Def && copy.Parts[0].Pos == c.Parts[0].Pos
                && Vector3.Distance(copy.Pos, c.Pos) < 0.01f && copy.Pinned == c.Pinned)
                same++;
        Line($"scale net: a joiner had all {b.Constructs.Bodies.Count} of {w.Bodies.Count} bodies after {(all is { } s ? $"{s:0.00} s" : "more than 10 s")} (streamed: {maxStreaming} joiner at a time); {same} copies match the host's");
        if (all == null || all > 5)
            Fail($"construct scale: a joiner didn't get all {w.Bodies.Count} bodies within 5 s ({b.Constructs.Bodies.Count} after {clock.Elapsed.TotalSeconds - t0:0.0} s)");
        if (maxStreaming == 0)
            Fail("construct scale: the joiner wasn't streamed the world (one burst)");
        if (same != w.Bodies.Count)
            Fail($"construct scale: {w.Bodies.Count - same} of the joiner's copies differ from the host's (the world's state didn't round-trip)");

        // Poses by interest: a body set moving near the first client's player and one far off both reach it.
        var near = ConstructBuild.PlaceNew(w, block, sp.Position + new Vector3(3, 2, 0), 0);
        var far = ConstructBuild.PlaceNew(w, block, sp.Position + new Vector3(260, 30, 0), 0);
        if (near == null || far == null)
        {
            Fail("construct scale: no room for the two moving blocks");
            return;
        }
        near.Rebuild();
        far.Rebuild();
        Pump(server, [a, b], 0.6, ref now, clock);
        var nearCopy = a.Constructs.Get(near.Id);
        var farCopy = a.Constructs.Get(far.Id);
        var nearOff = nearCopy == null ? 99f : Vector3.Distance(nearCopy.NetPos, near.Pos);
        var farOff = farCopy == null ? 99f : Vector3.Distance(farCopy.NetPos, far.Pos);
        Line($"scale net: poses by interest: a falling block near the client {nearOff * 100:0.0} cm from the host's, one {260} m off {farOff * 100:0.0} cm (sent less often); {ConstructNet.MoverCount} movers");
        if (nearOff > 0.6f)
            Fail($"construct scale: the client's copy of a body moving near it is {nearOff:0.00} m from the host's");
        if (farOff > 3f)
            Fail($"construct scale: the client's copy of a far moving body is {farOff:0.00} m from the host's (never sent?)");
    }
}
