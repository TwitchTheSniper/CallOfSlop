using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Player separation" (Source/Game/PlayerSeparation.cs; board item ps01), on a small lab map: two bots
/// put on the same spot are a body apart within half a second and neither is in a wall; a player walking through a
/// teammate standing in a 1 m doorway gets through; a bot pushed at a wall isn't pushed into it, and one at a platform's
/// edge isn't pushed off; the bots' steering term parts two stacked bots in opposite ways; 32 bodies clumped spread out,
/// within the tick budget and with no allocation; over the real transport, a client's player pushed by a bot is predicted
/// as the server moves it (no correction).
/// </summary>
public static partial class SelfTest
{
    private const string SepLabJson = """
        { "name": "Separation Lab", "halfX": 30, "halfZ": 30, "maxPlayers": 32,
          "ops": [
            { "op": "Ground", "color": [0.4, 0.4, 0.4] },
            { "op": "Block", "x0": -8, "y0": 0, "z0": -10.15, "x1": -0.5, "y1": 3, "z1": -9.85, "color": [0.7, 0.6, 0.5] },
            { "op": "Block", "x0": 0.5, "y0": 0, "z0": -10.15, "x1": 8, "y1": 3, "z1": -9.85, "color": [0.7, 0.6, 0.5] },
            { "op": "Block", "x0": 10, "y0": 0, "z0": -5, "x1": 10.3, "y1": 3, "z1": 5, "color": [0.7, 0.6, 0.5] },
            { "op": "Block", "x0": -20, "y0": 0, "z0": -3, "x1": -14, "y1": 2, "z1": 3, "color": [0.5, 0.5, 0.6] },
            SPAWNS
          ]
        }
        """;

    private static void CheckPlayerSeparation()
    {
        var previous = World.Map;
        try
        {
            SepScripted();
        }
        finally
        {
            World.SetMap(previous);
        }
        SepLoopback();
    }

    private static bool SepInWall(Player p) =>
        MoverPhysics.BlockedAll(p.Position + new Vector3(0, 0.03f, 0), World.HeightOf(p.Sim.Stance) - 0.06f, double.NaN);

    private static float SepFlat(Player a, Player b)
    {
        var d = a.Position - b.Position;
        return MathF.Sqrt(d.X * d.X + d.Z * d.Z);
    }

    /// <summary>The snapshots' position rounding (1/256 m an axis) across both axes.</summary>
    private const float Quantum = 1.4143f / 256;

    private static float SepFlat2(Vector3 d) => MathF.Sqrt(d.X * d.X + d.Z * d.Z);

    private static void SepScripted()
    {
        var spawns = string.Join(", ", Enumerable.Range(0, 32).Select(i => $"{{ \"op\": \"Spawn\", \"x\": {-24 + i % 8 * 6}, \"z\": {18 + i / 8 * 3} }}"));
        var lab = MapFile.Load(SepLabJson.Replace("SPAWNS", spawns), "Separation Lab");
        World.SetMap(Maps.All[0]);
        using var g = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: Maps.All[0].Id)
        {
            MapVoteEnabled = false,
        };
        g.Mode = GameMode.FreeForAll;
        g.Phase = MatchPhase.Playing;
        World.SetMap(lab);
        var me = g.Local!;
        g.AddBot(BotDifficulty.Normal, quiet: true);
        g.AddBot(BotDifficulty.Normal, quiet: true);
        var bots = g.Players.Values.Where(p => p.Bot != null).ToList();
        var bodies = new List<Player> { me };
        bodies.AddRange(bots);
        const float contact = World.PlayerRadius * 2;
        var ok = true;
        var lines = new List<string>();
        void Check(string what, bool pass, string detail)
        {
            lines.Add($"{what} {(pass ? "ok" : "FAILED")} ({detail})");
            ok &= pass;
        }
        void Put(Player p, float x, float z, float y = 0)
        {
            p.Alive = true;
            p.Teleport(new Vector3(x, y, z));
            p.Sim.Velocity = Vector3.Zero;
            p.Sim.Grounded = true;
            p.Sim.Stance = Stance.Stand;
            p.Sim.MantleTicks = 0;
        }
        void Park(Player p, float x) => Put(p, x, 25); // out of the way
        // One tick: every listed body moves (the given input for the walker, none for the rest), then its push.
        void Tick(List<Player> who, Player? walker = null, float yaw = 0)
        {
            g.SelfTestSepBuild();
            foreach (var p in who)
                g.SelfTestSepMove(p, p == walker ? new InputCmd { Buttons = Buttons.Forward, Yaw = yaw, SpeedPct = 100 } : new InputCmd { Yaw = p.Yaw, SpeedPct = 100 });
        }
        var a = bots[0];
        var b = bots[1];

        // 1. Two bots on the same spot, 1 m from a wall: a body apart within half a second, neither in the wall.
        Park(me, -25);
        Put(a, 8.95f, 0);
        Put(b, 8.95f, 0);
        var pair = new List<Player> { a, b };
        var apartAt = -1;
        var walled = false;
        for (var i = 0; i < 30; i++)
        {
            Tick(pair);
            walled |= SepInWall(a) || SepInWall(b);
            if (apartAt < 0 && SepFlat(a, b) >= contact - 1e-3f)
                apartAt = i + 1;
        }
        var stackDist = SepFlat(a, b);
        Check("stacked", apartAt > 0 && !walled, $"apart {stackDist:0.000} m after {apartAt} ticks, in a wall: {walled}");
        // and it settles: no creeping apart past contact
        for (var i = 0; i < 30; i++)
            Tick(pair);
        Check("settled", SepFlat(a, b) is >= contact - 1e-3f and < contact + 0.05f, $"{SepFlat(a, b):0.000} m after another half second");

        // 2. Walking through a teammate standing in a 1 m doorway (gap x -0.5..0.5 at z = -10).
        Park(b, 25);
        Put(a, 0, -10);
        Put(me, 0, -7);
        var duo = new List<Player> { me, a };
        var through = -1;
        walled = false;
        var minGap = float.MaxValue;
        for (var i = 0; i < 180 && through < 0; i++)
        {
            Tick(duo, me, 0); // yaw 0: towards -z
            walled |= SepInWall(me) || SepInWall(a);
            minGap = MathF.Min(minGap, SepFlat(me, a));
            if (me.Position.Z < -12f)
                through = i + 1;
        }
        Check("doorway", through > 0 && through < 90 && !walled && a.Position.Z > -11.5f,
            $"through after {through} ticks (a walk is ~50), teammate at z {a.Position.Z:0.00}, closest {minGap:0.00} m, in a wall: {walled}");

        // 3. A bot touching a wall (face at x = 10) walked into by a player going +x: never pushed into it.
        Put(a, 10f - World.PlayerRadius - 0.01f, 0);
        Put(me, 7, 0);
        var refused0 = g.SepWallRefused;
        walled = false;
        for (var i = 0; i < 90; i++)
        {
            Tick(duo, me, MathF.PI / 2);
            walled |= SepInWall(a) || SepInWall(me);
        }
        Check("wall", !walled && a.Position.X <= 10f - World.PlayerRadius + 1e-3f && g.SepWallRefused > refused0,
            $"bot at x {a.Position.X:0.000}, player at x {me.Position.X:0.00}, wall refusals {g.SepWallRefused - refused0}, in a wall: {walled}");

        // 4. A bot at a 2 m platform's edge (x = -14) walked into by a player on the platform: not pushed off.
        Put(a, -14.45f, 0, 2);
        Put(me, -17, 0, 2);
        var ledge0 = g.SepLedgeRefused;
        var lowest = float.MaxValue;
        for (var i = 0; i < 90; i++)
        {
            var walking = me.Position.X < -14.95f;
            Tick(duo, walking ? me : null, MathF.PI / 2);
            lowest = MathF.Min(lowest, a.Position.Y);
        }
        Check("ledge", lowest > 1.95f && a.Position.X < -14f && g.SepLedgeRefused > ledge0,
            $"bot at x {a.Position.X:0.000} y {a.Position.Y:0.00} (lowest {lowest:0.00}), ledge refusals {g.SepLedgeRefused - ledge0}");

        // 5. The bots' steering term: two stacked bots step opposite ways; one 2 m off adds nothing.
        Put(a, 0, 10);
        Put(b, 0, 10);
        Park(me, -25);
        g.SelfTestSepBuild();
        var wa = g.SelfTestBotSeparate(a, Vector3.Zero);
        var wb = g.SelfTestBotSeparate(b, Vector3.Zero);
        Put(b, 2, 10);
        g.SelfTestSepBuild();
        var far = g.SelfTestBotSeparate(a, Vector3.Zero);
        Check("steering", wa.Length() > 0.5f && Vector3.Dot(wa, wb) < -0.9f * wa.LengthSquared() && far == Vector3.Zero,
            $"stacked {wa.Length():0.00} and {wb.Length():0.00}, dot {Vector3.Dot(Vector3.Normalize(wa), Vector3.Normalize(wb)):0.00}; 2 m apart {far.Length():0.00}");

        // 6. 32 bodies clumped in 2 m: they spread out; the push costs little and allocates nothing.
        while (g.Players.Count < 32)
            g.SelfTestAddHuman();
        var all = g.Players.Values.ToList();
        var rng = new Random(7);
        foreach (var p in all)
            Put(p, (float)rng.NextDouble() * 2 - 1, 10 + (float)rng.NextDouble() * 2 - 1);
        Put(all[1], all[0].Position.X, all[0].Position.Z); // and one exact stack among them
        g.SelfTestSepBuild();
        foreach (var p in all)
            g.SelfTestSepStep(p); // (warm: JIT)
        const int ticks = 120;
        var clock = Stopwatch.StartNew();
        var bytes0 = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < ticks; i++)
        {
            g.SelfTestSepBuild();
            foreach (var p in all)
                g.SelfTestSepStep(p);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        var msPerTick = clock.Elapsed.TotalMilliseconds / ticks;
        var worst = 0f;
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
                worst = MathF.Max(worst, contact - SepFlat(all[i], all[j]));
        Check("crowd", worst < 0.15f && bytes == 0 && msPerTick < 1.0,
            $"32 bodies after 2 s: deepest overlap {worst:0.000} m, {msPerTick * 1000:0} us a tick (budget 8 ms), {bytes} B allocated");

        Line($"player separation: {string.Join("; ", lines)}");
        if (!ok)
            Fail("player separation: " + string.Join("; ", lines.Where(l => l.Contains("FAILED"))));
    }

    /// <summary>Over the real transport: a held bot put half into a client's player; the client predicts the push.</summary>
    private static void SepLoopback()
    {
        var map = Maps.Find("Proto Box") ?? Maps.All[0];
        const ushort port = 26401;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: 0, mapId: map.Id);
        server.MapVoteEnabled = false;
        server.Mode = GameMode.FreeForAll;
        server.AddBot(BotDifficulty.Normal, quiet: true);
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        var bot = server.Players.Values.FirstOrDefault(p => p.Bot != null);
        if (bot != null)
            bot.StunnedUntil = 1e9; // held still from the start: no shooting, no walking (it is still a body)
        Pump(server, [client], 1.5, ref now, clock);
        server.RequestRespawn();
        client.RequestRespawn();
        foreach (var p in server.Players.Values)
            p.ImmuneUntil = 1e9;
        Pump(server, [client], Game.CountdownSeconds + 1.0, ref now, clock);
        var guest = server.Players.GetValueOrDefault(client.LocalId);
        if (client.Local is not { Alive: true } mine || bot is not { Alive: true } || guest is not { Alive: true })
        {
            Fail($"player separation (loopback): no live client player and bot (phase {server.Phase}, client local {client.Local?.Alive}, bot {bot?.Alive}, guest {guest?.Alive}, players {server.Players.Count}, {clock.Elapsed.TotalSeconds:0.0} s)");
            return;
        }
        // The host's own player out of the way, the bot held still (stunned) a third of a body from the guest.
        if (server.Local is { } host)
            host.Teleport(guest.Position + new Vector3(0, 0, 6));
        // The baseline: the client's corrections standing still, before any push.
        var baseline = 0f;
        var until = clock.Elapsed.TotalSeconds + 0.4;
        while (clock.Elapsed.TotalSeconds < until)
        {
            Pump(server, [client], 0.004, ref now, clock);
            baseline = MathF.Max(baseline, client.LastCorrection);
        }
        // The bot crawls (prone, 1.3 m/s: slower than the push) into the guest from 0.6 m off, so it keeps herding them
        // along for the rest of the run however slow the round trips are.
        var start = guest.Position;
        bot.Teleport(start + new Vector3(0.6f, 0, 0));
        bot.Sim.Velocity = Vector3.Zero;
        bot.Sim.Stance = Stance.Prone;
        bot.Bot!.SelfTestHeldCmd = new InputCmd { Buttons = Buttons.Forward | Buttons.Prone, Yaw = -MathF.PI / 2, SpeedPct = 100 }; // (towards -x)
        // Contact latency (as with zombie bodies): until the client hears the bot is inside the guest its predictions lack
        // the push, and the inputs it predicts before its first reconcile that includes the server's push still start
        // from a position without it. After that the prediction must agree with the server while the push goes on: the
        // corrections stay within the snapshots' rounding of the guest's own state (1/256 m an axis: every reconcile of a
        // moving player starts from a rounded position), and none comes near half a tick's push (a missed or doubled step).
        var corrections = new List<float>();
        var contact = 0f;
        var worst = 0f;
        var informed = 0;
        var heardSeq = 0u;
        var settledSeq = 0u;
        var pushedInformed = 0f;
        var spot = bot.Position;
        until = clock.Elapsed.TotalSeconds + 1.5;
        while (clock.Elapsed.TotalSeconds < until)
        {
            var was = guest.Position;
            Pump(server, [client], 0.004, ref now, clock);
            var (acked, next) = client.SelfTestInputSeqs;
            if (heardSeq == 0 && client.Players.TryGetValue(bot.Id, out var seen) && Vector3.Distance(seen.Position, spot) < 0.05f)
                heardSeq = next;
            if (heardSeq != 0 && settledSeq == 0 && acked >= heardSeq)
                settledSeq = next;
            if (settledSeq != 0 && acked >= settledSeq)
            {
                worst = MathF.Max(worst, client.LastCorrection);
                corrections.Add(client.LastCorrection);
                informed++;
                pushedInformed += SepFlat2(guest.Position - was);
            }
            else
                contact = MathF.Max(contact, client.LastCorrection);
        }
        corrections.Sort();
        var median = corrections.Count > 0 ? corrections[corrections.Count / 2] : 0;
        var moved = Vector3.Distance(mine.Position with { Y = 0 }, start with { Y = 0 });
        var apart = SepFlat(guest, bot);
        var agree = Vector3.Distance(mine.Position, guest.Position);
        Line($"player separation (loopback, {map.Name}): guest pushed {moved:0.00} m, apart {apart:0.00} m, client vs server {agree * 100:0.0} cm; corrections: standing {baseline * 100:0.0} cm, first contact {contact * 100:0.0} cm, then {worst * 100:0.00} cm at worst, median {median * 1000:0.00} mm ({informed} checks, while the push moved the guest {pushedInformed * 100:0.0} cm)");
        if (moved < 0.3f)
            Fail($"player separation (loopback): the crawling bot didn't push the guest along (guest moved {moved:0.00} m, apart {apart:0.00} m)");
        if (settledSeq == 0 || informed < 20 || pushedInformed < 0.1f || worst > baseline + 0.5f * Game.SepMaxPush + 0.006f || median > Quantum)
            Fail($"player separation (loopback): the client's prediction disagreed with the server (worst correction after first contact {worst * 100:0.00} cm, median {median * 1000:0.00} mm over {informed} checks while the guest was pushed {pushedInformed * 100:0.0} cm, standing {baseline * 100:0.0} cm; now {agree * 100:0.0} cm)");
    }
}
