using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Space: gravity physics" self-test: what the generic "Map contract" section (SelfTestMapContract.cs, driven by the
/// maps' "test" blocks) can't express about the Space pack's gravity-zone maps, Keelhaul Yard (03) and Twin Shard (04):
/// the zones are there, each area with its own up has nav nodes in the main area, a scripted player stands in each kind
/// of gravity, a sprint jump clears the Yard's low-gravity hatch, and Twin Shard's Pebble drop loop works. The outline,
/// objectives, dressing, fair walks and the bot matches are the contract's. No bot matches here.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> SpaceGravityNotes = new();

    private static void SpaceGravityLine(string s)
    {
        Line(s);
        SpaceGravityNotes.Add(s);
    }

    private static void CheckSpaceGravityMaps()
    {
        SpaceGravityNotes.Clear();
        var before = World.Map;
        try
        {
            KeelhaulYardChecks();
            TwinShardChecks();
            GravityMapsUnderTheFloor();
            FallRescueCheck();
        }
        finally
        {
            World.SetMap(before);
            if (SpaceGravityNotes.Count > 0)
                Line("::notice title=Space: gravity physics (results)::" + string.Join("%0A", SpaceGravityNotes.Select(n => n.Replace("%", "%25"))));
        }
    }

    /// <summary>
    /// Nobody alive under the map (the "Matches" failure, 2026-10-23): several seeded 8-player matches (45 s each, as the
    /// Matches section plays) on each gravity map, sampling every player twice a second; an alive player below the fall-kill
    /// height means a fall that wasn't caught. (Before the fix: bots walked off unrailed dock edges, crawled under the
    /// Twin Shard tethers, and players who fell outside play hung alive 5 m under the map until the next match.)
    /// </summary>
    private static void GravityMapsUnderTheFloor()
    {
        const int seeds = 4;
        foreach (var name in new[] { "Keelhaul Yard", "Twin Shard" })
        {
            if (Maps.Find(name) is not { FallKillY: { } kill } m)
                continue;
            int under = 0, falls = 0, rescues = 0, stuckKills = 0;
            var example = "";
            for (var seed = 1; seed <= seeds; seed++)
            {
                Game.SeedNextGame = seed * 7919;
                using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                    bots: Game.ClassicPlayers - 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
                game.MapVoteEnabled = false;
                var now = 0.0;
                const double dt = 1.0 / World.TickRate;
                for (var i = 0; i < 45 * World.TickRate; i++)
                {
                    now += dt;
                    game.Update(now, dt);
                    if (i % 30 != 0)
                        continue;
                    foreach (var p in game.Players.Values)
                        if (p.Alive && p.Position.Y < kill - 0.5f)
                        {
                            if (under++ == 0)
                                example = $" (seed {seed}, {i / 60} s, {game.Phase}: player {p.Id} at {p.Position})";
                        }
                }
                falls += game.FallKills;
                rescues += game.FallRescues;
                stuckKills += game.BotStuckKills;
            }
            SpaceGravityLine($"{name}: {seeds} seeded 45 s matches: {under} samples of a player alive under the map{example}, {falls} fall kills, {rescues} put back on a spawn outside play, {stuckKills} stuck kills");
            if (under > 0)
                Fail($"{name}: players alive under the map in seeded matches{example}");
            if (falls > seeds * 2)
                Fail($"{name}: {falls} fall kills in {seeds} seeded 45 s matches (bots walking off edges?)");
        }
    }

    /// <summary>Outside play (warm-up: nobody can be hurt) a player who falls off is put back on their nearest spawn, alive.</summary>
    private static void FallRescueCheck()
    {
        if (Maps.Find("Keelhaul Yard") is not { FallKillY: { } kill } m)
            return;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: m.Id)
        {
            MapVoteEnabled = false,
            WarmupEnabled = true,
        };
        var now = 0.0;
        Run(game, ref now, 1);
        game.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
        Run(game, ref now, 1);
        var me = game.Local!;
        var phase = game.Phase;
        me.Teleport(new Vector3(-30, kill - 3, 0)); // fell through the hold's hatch
        game.SelfTestTickFallKill(now);
        var onSpawn = m.Spawns.Any(sp => Vector3.Distance(sp, me.Position) < 0.01f);
        SpaceGravityLine($"fall in {phase}: alive {me.Alive}, put back on a spawn {onSpawn} at {me.Position}, rescues {game.FallRescues}, fall kills {game.FallKills}");
        if (phase == MatchPhase.Playing)
            Line("  note: the rescue game started in Playing (no warm-up); not checked");
        else if (!me.Alive || !onSpawn || game.FallKills != 0)
            Fail($"a player who fell off in {phase} wasn't put back on a spawn (alive {me.Alive}, at {me.Position}, fall kills {game.FallKills})");
    }

    /// <summary>The map is a gravity-zone void map with a "test" block; returns its nav graph (built whole) or null.</summary>
    private static NavGraph? GravityMapGraph(Map m, string name)
    {
        if (m.GravityZones is not { } zones || zones.Zones.Length < 2)
        {
            Fail($"{name}: should have gravity zones");
            return null;
        }
        if (m.FallKillY == null || m.Boundary == null || m.Boundary.Spec.Shape == BoundaryShape.Rect || m.Terrain != null)
            Fail($"{name}: should be a void map with a shaped outline (fallKill, a non-rectangular boundary, no terrain)");
        if (m.TestSpec == null)
            Fail($"{name}: the map file has no \"test\" block (the Map contract section checks the rest of the map from it)");
        World.SetMap(m);
        var g = NavGraph.Build(m);
        SpaceGravityLine($"{name}: {zones.Zones.Length} gravity zones, {m.SolidMeshes.Count} solid meshes of {m.MeshPlacements.Count}; nav {g.Nodes.Length} nodes ({g.MainNodes.Length} main), fingerprint {m.ContentHash:x8}");
        return g;
    }

    /// <summary>A player dropped at <paramref name="at"/> settles on ground whose up is <paramref name="up"/> (within 6 degrees).</summary>
    private static bool StandsWithUp(Vector3 at, Vector3 up, string name, string where)
    {
        var s = Settle(at, 90);
        var u = GravityFrames.UpAt(s.Position);
        var ok = s.Grounded && Vector3.Dot(u, up) > 0.994f && World.BodyFits(s.Position, u);
        if (!ok)
            Fail($"{name}: a player at {where} ({at}) doesn't stand with up {up} (grounded {s.Grounded}, up {u}, at {s.Position})");
        return ok;
    }

    private static void KeelhaulYardChecks()
    {
        const string name = "Keelhaul Yard";
        if (Maps.Find(name) is not { } m)
        {
            Fail($"Maps/Space/03-KeelhaulYard.json ('{name}') didn't load");
            return;
        }
        if (GravityMapGraph(m, name) is not { } g)
            return;
        // Nodes in each kind of gravity, all in the main area: the hull wall (up -X), the rock (up away from its centre),
        // the hold (low gravity, up +Y) and the bowl.
        var zones = m.GravityZones!;
        int Main(Func<Vector3, bool> where) => g.MainNodes.Count(i => where(g.Nodes[i]));
        var wall = Main(n => n.X > 29.5f && n.X < 30.2f && n.Y > 1f && zones.UpAt(n) == -Vector3.UnitX);
        var rock = Main(n => Vector3.Distance(n, new Vector3(0, 9, 0)) < 6.6f);
        var rockUnder = Main(n => Vector3.Distance(n, new Vector3(0, 9, 0)) < 6.6f && n.Y < 7f);
        var hold = Main(n => n.X < -17f && n.Y < -2.5f);
        var bowl = Main(n => zones.ZoneAt(n) is var z && z >= 0 && zones.Zones[z].Name.StartsWith("bowl"));
        var masts = Main(n => n.Y > 14.8f && MathF.Abs(n.X) < 4f && MathF.Abs(n.Z) > 2f && MathF.Abs(n.Z) < 21f);
        SpaceGravityLine($"{name} nav by area (main): hull wall {wall}, bowl {bowl}, rock {rock} ({rockUnder} under it), hold {hold}, masts and piers {masts}");
        if (wall < 30 || bowl < 3 || rock < 20 || rockUnder < 4 || hold < 40 || masts < 6)
            Fail($"{name}: the bots' graph misses an area (wall {wall}, bowl {bowl}, rock {rock}/{rockUnder}, hold {hold}, masts {masts})");

        // A player stands in each kind of gravity, and a sprinting jump in the hold clears the void hatch.
        var stands = StandsWithUp(new Vector3(0, 0.3f, -20), Vector3.UnitY, name, "the yard")
                     & StandsWithUp(new Vector3(29.6f, 16, 5), -Vector3.UnitX, name, "the hull")
                     & StandsWithUp(new Vector3(-31, -2.7f, 15), Vector3.UnitY, name, "the hold")
                     & StandsWithUp(new Vector3(0, 15.4f, 0), Vector3.UnitY, name, "the rock's top")
                     & StandsWithUp(new Vector3(0, 2.4f, 0), -Vector3.UnitY, name, "the rock's underside")
                     & StandsWithUp(new Vector3(6.3f, 9, 0), Vector3.UnitX, name, "the rock's side");
        var j = Settle(new Vector3(-20.5f, -2.7f, 0), 30);
        var west = GravityFrames.YawOf(j.Position, -Vector3.UnitX);
        for (var i = 0; i < 20; i++)
            World.Simulate(ref j, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = west, SpeedPct = 100 });
        var takeoff = j.Position;
        World.Simulate(ref j, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | Buttons.Jump, Yaw = west, SpeedPct = 100 });
        var air = 0;
        var lowest = j.Position.Y;
        while (!j.Grounded && air++ < 400)
        {
            World.Simulate(ref j, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = west, SpeedPct = 100 });
            lowest = MathF.Min(lowest, j.Position.Y);
        }
        var jumped = takeoff.X - j.Position.X;
        var landedOnFloor = j.Grounded && j.Position.X < -40f && j.Position.Y > -3.1f && lowest > m.FallKillY!.Value;
        SpaceGravityLine($"{name}: stands in every gravity {stands}; a sprint jump across the hold's hatch (18 m) covers {jumped:0.0} m in {air * World.TickDt:0.00} s and lands on the far floor {landedOnFloor} at ({j.Position.X:0.0}, {j.Position.Y:0.0})");
        if (!landedOnFloor)
            Fail($"{name}: the low-gravity jump across the hatch failed (covered {jumped:0.0} m, grounded {j.Grounded}, at {j.Position}, lowest {lowest:0.0})");
    }

    private static void TwinShardChecks()
    {
        const string name = "Twin Shard";
        if (Maps.Find(name) is not { } m)
        {
            Fail($"Maps/Space/04-TwinShard.json ('{name}') didn't load");
            return;
        }
        if (GravityMapGraph(m, name) is not { } g)
            return;
        var zones = m.GravityZones!;
        int Main(Func<Vector3, bool> where) => g.MainNodes.Count(i => where(g.Nodes[i]));
        var west = Main(n => Vector3.Distance(n, new Vector3(-34, 24, 0)) < 18.6f);
        var westUnder = Main(n => Vector3.Distance(n, new Vector3(-34, 24, 0)) < 18.6f && n.Y < 24 - 9);
        var east = Main(n => Vector3.Distance(n, new Vector3(34, 24, 0)) < 18.6f);
        var line = Main(n => n.Y > 41f && n.Y < 43f && MathF.Abs(n.X) < 31f);
        var keel = Main(n => n.Y > 5.5f && n.Y < 6.1f && MathF.Abs(n.X) < 31f && zones.UpAt(n) == -Vector3.UnitY);
        SpaceGravityLine($"{name} nav by area (main): west shard {west} ({westUnder} on its lower half), east shard {east}, the Line {line}, the Keel Line (upside down) {keel}");
        if (west < 60 || east < 60 || westUnder < 15 || line < 8 || keel < 8)
            Fail($"{name}: the bots' graph misses an area (west {west}/{westUnder}, east {east}, line {line}, keel {keel})");
        var stands = StandsWithUp(new Vector3(4, 42.5f, 4), Vector3.UnitY, name, "the Knot")
                     & StandsWithUp(new Vector3(0, 5.5f, 0), -Vector3.UnitY, name, "under the Keel Line")
                     & StandsWithUp(new Vector3(-34, 24 - 18.3f, 0), -Vector3.UnitY, name, "the west shard's bottom pole")
                     & StandsWithUp(new Vector3(34 + 18.3f, 24, 0), Vector3.UnitX, name, "the east shard's outer flank")
                     & StandsWithUp(new Vector3(0, 24 + 4.3f, 0), Vector3.UnitY, name, "the Pebble's top");
        // Off the Line's middle you fall onto the Pebble; off the Pebble's underside onto the Keel Line's top.
        var f = new MoveState { Position = new Vector3(0.5f, 42.3f, 0.5f), Grounded = false }; // in the Knot's eye
        for (var i = 0; i < 240 && !(f.Grounded && i > 5); i++)
            World.Simulate(ref f, new InputCmd { SpeedPct = 100 });
        var onPebble = f.Grounded && Vector3.Distance(f.Position, new Vector3(0, 24, 0)) < 4.4f;
        var k = Settle(new Vector3(0.5f, 24 - 4.3f, 0.5f), 60);
        for (var i = 0; i < 60; i++)
            World.Simulate(ref k, new InputCmd { Buttons = i < 2 ? Buttons.Jump : 0, SpeedPct = 100 });
        var leftPebble = Vector3.Distance(k.Position, new Vector3(0, 24, 0)) > 4.8f;
        for (var i = 0; i < 300 && !k.Grounded; i++)
            World.Simulate(ref k, new InputCmd { SpeedPct = 100 });
        var onKeelTop = k.Grounded && k.Position.Y > 6.1f && k.Position.Y < 6.4f && MathF.Abs(k.Position.X) < 6f;
        SpaceGravityLine($"{name}: stands in every gravity {stands}; a drop from the Line lands on the Pebble {onPebble} at ({f.Position.X:0.0}, {f.Position.Y:0.0}, {f.Position.Z:0.0}); "
                         + $"a jump off the Pebble's underside leaves its pull {leftPebble} and lands on the Keel Line's top {onKeelTop} at ({k.Position.X:0.0}, {k.Position.Y:0.0}, {k.Position.Z:0.0})");
        if (!onPebble || !leftPebble || !onKeelTop)
            Fail($"{name}: the Pebble drop loop is wrong (on pebble {onPebble}, left {leftPebble}, on keel top {onKeelTop}, {f.Position}, {k.Position})");
    }
}
