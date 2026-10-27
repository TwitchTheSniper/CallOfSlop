using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Map logic" (Maps/README.md "Map logic"; MapLogic.cs, GameLogic.cs, GameLogicNav.cs): bad logic sections are refused
/// with readable reasons; the Logic Test dev map's doors are solid, undrawn and fingerprinted shut, its nav links follow
/// its doors; mover control is a pure function of the tick; the map's test steps play the same twice (deterministic); the
/// logic allocates nothing while it runs; and over a real loopback a client mirrors the doors, lamps, levers and the lift's
/// clock, a joiner gets the whole state, and a client's lever pull goes through. The Map contract plays every map's
/// "events" steps (<see cref="MapContractLogic"/>).
/// </summary>
public static partial class SelfTest
{
    private static void CheckMapLogic()
    {
        CheckLogicRefusals();
        CheckLogicMoverControl();
        if (Maps.Find("Logic Test") is not { } map)
        {
            Fail("map logic: the Logic Test dev map isn't loaded");
            return;
        }
        CheckLogicMap(map);
        CheckLogicDeterminism(map);
        CheckLogicAllocations(map);
        CheckLogicLoopback(map);
        map.RestoreProps();
    }

    // ---- Refusals ---------------------------------------------------------------------------------------------

    private static void CheckLogicRefusals()
    {
        const string head = "{ \"name\": \"Logic Refusal\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ { \"op\": \"Ground\", \"color\": \"#555555\" }, { \"op\": \"Spawn\", \"x\": 0, \"z\": -10 } ], "
                            + "\"movers\": [ { \"type\": \"elevator\", \"name\": \"L\", \"at\": [10, 0.3, 10], \"rise\": 3, \"boxes\": [ { \"size\": [2, 0.3, 2] } ] } ], \"logic\": ";
        const string door = "{ \"type\": \"door\", \"name\": \"Gate\", \"box\": [0, 0, 0, 0.3, 2.5, 2] }";
        const string button = "{ \"type\": \"button\", \"name\": \"A\", \"at\": [0, 1.2, 3] }";
        string Ents(params string[] e) => "{ \"entities\": [" + string.Join(", ", e) + "]";
        string Wire(string from, string to) => $", \"wires\": [ {{ \"from\": \"{from}\", \"to\": \"{to}\" }} ] }}";
        var cases = new (string Logic, string Expect)[]
        {
            (Ents("{ \"type\": \"buton\", \"name\": \"A\" }") + " }", "unknown type \"buton\""),
            (Ents("{ \"type\": \"button\", \"at\": [0, 1, 0] }") + " }", "needs a \"name\""),
            (Ents(button, button) + " }", "two entities called \"A\""),
            (Ents("{ \"type\": \"door\", \"name\": \"My Gate\", \"box\": [0, 0, 0, 1, 1, 1] }") + " }", "no spaces"),
            (Ents(door, button) + Wire("A.pressed", "Gat.open"), "did you mean \"Gate\""),
            (Ents(door, button) + Wire("A.pressd", "Gate.open"), "has no event \"pressd\""),
            (Ents(door, button) + Wire("A.press", "Gate.open"), "is one of its actions, not an event"),
            (Ents(door, button) + Wire("A.pressed", "Gate.opened"), "is one of its events, not an action"),
            (Ents(door, button) + Wire("A.pressed", "zombies.spawn"), "only there on a map with a \"zombies\" section"),
            (Ents("{ \"type\": \"mover\", \"name\": \"M\", \"mover\": \"Nope\" }") + " }", "no mover called \"Nope\""),
            (Ents("{ \"type\": \"text\", \"name\": \"T\", \"text\": \"café\" }") + " }", "plain ASCII only"),
            (Ents("{ \"type\": \"door\", \"name\": \"D\", \"box\": [0, 0, 0, 0.01, 1, 1] }") + " }", "0.05 to 200"),
            (Ents("{ \"type\": \"lamp\", \"name\": \"La\", \"box\": [0, 2, 0, 1, 3, 1], \"colour\": \"#ffffff\" }") + " }", "unknown key \"colour\""),
            (Ents("{ \"type\": \"timer\", \"name\": \"T\" }") + " }", "needs \"interval\""),
            (Ents("{ \"type\": \"sound\", \"name\": \"S\", \"at\": [0, 1, 0], \"sound\": \"kaboom\" }") + " }", "unknown sound \"kaboom\""),
            (Ents("{ \"type\": \"breakable\", \"name\": \"W\", \"at\": [5, 0, 5] }") + " }", "no breakable prop within 1.5 m"),
            (Ents("{ \"type\": \"and\", \"name\": \"G\" }", button) + Wire("A.pressed", "G.c"), "has no action \"c\""),
            (Ents("{ \"type\": \"random\", \"name\": \"R\" }", button) + Wire("R.out3", "A.press"), "has no event \"out3\""),
            (Ents(button) + ", \"wires\": [ { \"from\": \"A.pressed\", \"to\": \"A.press\", \"delay\": -1 } ] }", "\"delay\" is 0 to 3600"),
            (Ents("{ \"type\": \"zdoor\", \"name\": \"Z\", \"door\": \"Front\" }") + " }", "a zdoor needs a map with a \"zombies\" section"),
            (Ents(button) + ", \"wirez\": [] }", "unknown key \"wirez\""),
        };
        var refused = 0;
        foreach (var (logic, expect) in cases)
        {
            try
            {
                MapFile.Load(head + logic + " }", "Dev/LogicRefusal.json");
                Fail($"map logic: accepted a bad section (expected \"{expect}\"): {logic}");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.Ordinal))
            {
                refused++;
            }
            catch (MapFileException ex)
            {
                Fail($"map logic: refused for the wrong reason (expected \"{expect}\"): {ex.Message}");
            }
        }
        // A good one loads, round-trips through the writer with the same fingerprint, and puts the door in the solids.
        var good = MapFile.Load(head + Ents(door, button) + Wire("A.pressed", "Gate.toggle") + " }", "Dev/LogicRefusal.json");
        var back = MapFile.Load(MapFile.ToJson(good), "Dev/LogicRefusal.json");
        var d = good.Logic!.Entities[good.Logic.Find("Gate")];
        var ok = good.ContentHash == back.ContentHash && d.SolidIndex >= 0 && good.Solid[d.SolidIndex] == d.Box && !good.IsDrawnBox(d.SolidIndex)
                 && good.Logic.Entities.Count == 3 && good.Logic.MatchEntity == 2 && good.Logic.Wires.Length == 1 && good.Logic.NavGates.Count == 1;
        // The door's own fingerprint is the shut box: opening it changes nothing.
        var shut = good.ContentHash;
        good.SetLogicDoor(good.Logic.Find("Gate"), true);
        ok &= good.ContentHash == shut && good.IsLogicDoorOpen(good.Logic.Find("Gate")) && !good.IsAsBuilt() && good.AsBuiltSolid()[d.SolidIndex] == d.Box;
        good.RestoreProps();
        ok &= !good.IsLogicDoorOpen(good.Logic.Find("Gate")) && good.IsAsBuilt();
        Line($"refusals: {refused}/{cases.Length} bad sections refused for the right reason; a good one round-trips (same fingerprint, the door solid and undrawn, "
             + $"opening it keeps the fingerprint and the as-built boxes, a restore shuts it): {ok}");
        if (!ok)
            Fail("map logic: a good logic section doesn't load, round-trip or restore as it should");
    }

    // ---- Mover control ----------------------------------------------------------------------------------------

    private static void CheckLogicMoverControl()
    {
        var m = new MoverDef { Kind = MoverKind.Elevator, Points = [new Vector3(0, 0.3f, 0), new Vector3(0, 3.3f, 0)], Speed = 1.5f, Dwell = 2, StartsRunning = false, Parts = [new MoverPart(new Vector3(0, -0.15f, 0), new Vector3(1, 0.15f, 1), 0, Vector3.One)] };
        m.Prepare();
        m.ResetControl();
        var still = m.PoseAt(0) == m.PoseAt(500) && m.PoseAt(500).Origin.Y == 0.3f;
        var arrive = m.ControlAdvance(600);
        var travelTicks = 3.0 / 1.5 * World.TickRate;
        var up = Math.Abs(arrive - (600 + travelTicks)) < 0.01 && m.PoseAt(600).Origin.Y == 0.3f && MathF.Abs(m.PoseAt(arrive).Origin.Y - 3.3f) < 1e-4f
                 && m.PoseAt(arrive + 300) == m.PoseAt(arrive) && m.PoseAt(600 + travelTicks / 2).Origin.Y is > 1.5f and < 2.1f;
        var arrive2 = m.ControlAdvance(arrive + 300);
        var down = MathF.Abs(m.PoseAt(arrive2).Origin.Y - 0.3f) < 1e-4f && Math.Abs(arrive2 - (arrive + 300 + travelTicks)) < 0.01;
        m.ControlStart(arrive2 + 10);
        var runs = m.PoseAt(arrive2 + 10 + 2 * World.TickRate + 60) != m.PoseAt(arrive2 + 10); // past its 2 s wait: moving
        m.ControlStop(arrive2 + 400);
        var stops = m.PoseAt(arrive2 + 400) == m.PoseAt(arrive2 + 900);
        // A mover nothing controls: exactly the clock it always had (the seeded match digests depend on it).
        var free = new MoverDef { Kind = MoverKind.Slide, Points = [Vector3.Zero, new Vector3(10, 0, 0)], Speed = 2, Dwell = 1, Spin = 0.5f, Parts = m.Parts };
        free.Prepare();
        var same = true;
        for (var t = -3.0; t < 2000; t += 37.25)
            same &= free.TimeAt(t) == t / World.TickRate;
        Line($"mover control: stopped at the start holds still {still}; advance goes up in {travelTicks / World.TickRate:0.0} s and stays {up}; advance again comes down {down}; "
             + $"start runs {runs}, stop holds {stops}; an uncontrolled mover keeps the server clock exactly {same}");
        if (!still || !up || !down || !runs || !stops || !same)
            Fail("map logic: mover control (start, stop, advance) isn't right, or an uncontrolled mover's clock changed");
    }

    // ---- The Logic Test map -----------------------------------------------------------------------------------

    private static void CheckLogicMap(Map map)
    {
        map.RestoreProps();
        World.SetMap(map);
        var l = map.Logic!;
        var doors = l.Doors.Select(d => l.Entities[d]).ToArray();
        var boxesOk = doors.All(d => d.SolidIndex >= 0 && map.Solid[d.SolidIndex] == d.Box && !map.IsDrawnBox(d.SolidIndex) && map.IsLogicBox(d.SolidIndex));
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        using var game = ModeGame(0, GameMode.FreeForAll, map.Id);
        var now = 0.0;
        Run(game, ref now, 0.5);
        var shut = game.SelfTestNavGates();
        var gate = l.Find("Gate");
        game.SelfTestLogicDeliver(gate, LogicAction.Open, LogicSignal.Pulse());
        Run(game, ref now, 0.1);
        var open = game.SelfTestNavGates();
        var gateIndex = l.Entities[gate].NavGate;
        var navOk = shut.Length == l.NavGates.Count && shut.All(x => x.Edges >= 2 && x.On == 0) && open[gateIndex].On == open[gateIndex].Edges
                    && open.Where((_, k) => k != gateIndex).All(x => x.On == 0) && map.IsLogicDoorOpen(gate);
        // A path from inside the gate yard out: through the gate while it's open, none (or round) while it's shut.
        var g = NavGraph.Current;
        var search = new NavSearch();
        var path = new int[4096];
        int inside = g.Nearest(new Vector3(-21, 0, 0), needSight: false), outside = g.Nearest(new Vector3(-10, 0, 0), needSight: false);
        var openLen = search.Find(g, inside, outside, path, maxExpand: 60000);
        game.SelfTestLogicDeliver(gate, LogicAction.Close, LogicSignal.Pulse());
        Run(game, ref now, 0.1);
        game.SelfTestNavGates();
        var shutLen = search.Find(g, inside, outside, path, maxExpand: 60000);
        var pathOk = openLen > 0 && shutLen <= 0;
        Line($"Logic Test: {l.Entities.Count} entities ({doors.Length} doors solid and undrawn {boxesOk}), {l.Wires.Length} wires; nav links {string.Join(", ", shut.Select(x => $"{x.Edges} edges"))}, "
             + $"all off while shut, the gate's on while open {navOk}; a path out of the yard {openLen} nodes while open, {shutLen} while shut");
        if (!boxesOk || !navOk)
            Fail("map logic: the Logic Test doors aren't solid undrawn boxes, or their nav links don't follow them");
        if (!pathOk)
            Fail("map logic: bots can't path out through the open gate, or still can through the shut one");
    }

    private static void CheckLogicDeterminism(Map map)
    {
        var steps = map.TestSpec?.Events ?? [];
        var a = PlayLogicSteps(map, steps, out _, quiet: true);
        var b = PlayLogicSteps(map, steps, out _, quiet: true);
        var same = a.SequenceEqual(b);
        Line($"determinism: the test steps played twice give the same {a.Count} events in the same ticks and order: {same}");
        if (!same || a.Count == 0)
            Fail("map logic: the same steps don't give the same events (or none at all)");
    }

    private static void CheckLogicAllocations(Map map)
    {
        Game.SeedNextGame = 7400;
        using var game = ModeGame(0, GameMode.FreeForAll, map.Id);
        var now = 0.0;
        Run(game, ref now, 1);
        var me = game.Local!;
        var l = map.Logic!;
        var court = l.Entities[l.Find("Courtyard")].Box;
        var leverA = l.Find("LeverA");
        var leverB = l.Find("LeverB");
        void Work(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                if (i % 120 == 0)
                    me.Position = (i / 120) % 2 == 0 ? new Vector3(court.Center.X, 0, court.Center.Z) : new Vector3(-10, 0, -19); // (the courtyard: lamps, the alarm, a notice)
                if (i % 90 == 0)
                    game.SelfTestLogicDeliver((i / 90) % 2 == 0 ? leverA : leverB, LogicAction.Toggle, LogicSignal.Pulse()); // (the gates, the vault, its lamps)
                game.SelfTestLogicTick();
            }
        }
        Work(1200); // (warm: every path once)
        var s = game.Logic!;
        var sent0 = s.Sent;
        var before = GC.GetAllocatedBytesForCurrentThread();
        Work(1800);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var sent = s.Sent - sent0;
        Line($"allocations: 1800 logic ticks with {sent} events (zone, lamps, levers, gates, the vault door, the lift timer): {bytes} bytes");
        if (sent < 20)
            Fail("map logic: the allocation run sent too few events to mean anything");
        if (bytes > 0)
            Fail($"map logic: the logic allocated {bytes} bytes in 1800 ticks (it should allocate nothing)");
    }

    private static void CheckLogicLoopback(Map map)
    {
        const ushort port = 60237;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || a.Logic is not { } ca || server.Logic is not { } ss)
        {
            Fail("map logic loopback: the client didn't join (or has no logic state)");
            return;
        }
        var l = map.Logic!;
        int gate = l.Find("Gate"), lamp = l.Find("Lamp2"), lift = l.Find("LiftControl"), lever = l.Find("LeverB");
        server.SelfTestLogicDeliver(gate, LogicAction.Open, LogicSignal.Pulse());
        server.SelfTestLogicDeliver(lamp, LogicAction.TurnOn, LogicSignal.Pulse());
        server.SelfTestLogicDeliver(lift, LogicAction.Advance, LogicSignal.Pulse());
        Pump(server, [a], 0.5, ref now, clock);
        var mirrored = ca.On(gate) && ca.On(lamp) && ss.On(gate) && ss.On(lamp) && a.LogicMoverPackets >= 1;
        // The client pulls lever B: standing in front of it on the server, the use goes over the wire (once the match is on and it's spawned).
        var sp = server.Players[a.LocalId];
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        var le = l.Entities[lever];
        sp.Position = (le.At + World.FlatForward(le.Yaw) * 0.9f) with { Y = 0 };
        a.SelfTestSendLogicUse(lever);
        Pump(server, [a], 0.5, ref now, clock);
        var pulled = ss.On(lever) && ca.On(lever);
        if (!pulled)
            Line($"  (lever pull: server phase {server.Phase}, the client's player {Vector3.Distance(sp.Eye, le.At):0.00} m from it, alive {sp.Alive}, at {sp.Position})");
        // A second client joining now gets the whole state.
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.5, ref now, clock);
        var joined = b.Logic is { } cb && Enumerable.Range(0, l.Entities.Count).Where(i => l.Entities[i].Kind is LogicKind.Door or LogicKind.Lamp or LogicKind.Lever or LogicKind.Button or LogicKind.Plate)
            .All(i => cb.Flags[i] == ss.Flags[i]) && b.LogicMoverPackets >= 1;
        Line($"loopback: a client mirrors the open gate, a lit lamp and the lift's clock {mirrored}; its lever pull goes through and comes back {pulled}; a joiner gets every door, lamp, lever and the lift {joined}; protocol {ServerInfo.ProtocolVersion}");
        if (!mirrored)
            Fail("map logic loopback: a client doesn't mirror the server's doors, lamps or mover clocks");
        if (!pulled)
            Fail("map logic loopback: a client's lever pull didn't go through");
        if (!joined)
            Fail("map logic loopback: a joining client didn't get the logic state");
    }

    // ---- The test block's steps (the Map contract) ------------------------------------------------------------

    /// <summary>The Map contract's "map logic" check: plays the map's "events" steps on a server of its own.</summary>
    private static string? MapContractLogic(Map m, MapTestSpec t)
    {
        var log = PlayLogicSteps(m, t.Events, out var checks, quiet: false);
        return $"{t.Events.Length} steps, {checks} checks, {log.Count} events";
    }

    /// <summary>Plays logic steps on a fresh offline server on <paramref name="m"/>; returns every event sent ("tick Entity.event n"), failing on what didn't hold.</summary>
    private static List<string> PlayLogicSteps(Map m, LogicTestStep[] steps, out int checks, bool quiet)
    {
        checks = 0;
        var log = new List<string>();
        if (m.Logic is not { } l)
        {
            Fail($"{m.Name}: \"test\" has \"events\" but the map has no \"logic\" section");
            return log;
        }
        Game.SeedNextGame = 7300 + m.Id;
        m.RestoreProps();
        using var game = ModeGame(0, GameMode.FreeForAll, m.Id);
        game.WarmupEnabled = true;
        var now = 0.0;
        Run(game, ref now, 1);
        game.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
        Run(game, ref now, Game.CountdownSeconds + 0.3);
        var me = game.Local;
        if (me is not { Alive: true } || game.Phase != MatchPhase.Playing || game.Logic is not { } s)
        {
            Fail($"{m.Name}: the map logic game didn't start");
            return log;
        }
        me.ImmuneUntil = double.MaxValue; // (nothing on a test map should hurt, but a step shouldn't fail to a stray blast)
        var fired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        game.LogicEventTap = (e, ev, sig) =>
        {
            var name = $"{l.Entities[e].Name}.{MapLogic.PortName(ev)}";
            fired.Add(name);
            log.Add($"{game.ServerTick} {name} {sig.N}");
        };
        var home = m.Spawns[0];
        int Ent(string name, string where)
        {
            var i = l.Find(name);
            if (i < 0)
                Fail($"{m.Name}: test.events {where}: no entity called \"{name}\"");
            return i;
        }
        (int Entity, string Port) Port(string text, string where)
        {
            var dot = text.LastIndexOf('.');
            if (dot <= 0)
            {
                Fail($"{m.Name}: test.events {where}: \"{text}\" isn't \"Entity.port\"");
                return (-1, "");
            }
            return (Ent(text[..dot], where), text[(dot + 1)..]);
        }
        Vector3 Floor(Vector3 p)
        {
            var down = World.Query.Raycast(p + new Vector3(0, 0.5f, 0), -Vector3.UnitY, 30f, QueryFilter.All);
            return down < 30f ? p + new Vector3(0, 0.5f - down, 0) : p;
        }
        for (var k = 0; k < steps.Length; k++)
        {
            var st = steps[k];
            var where = $"step {k + 1}";
            fired.Clear();
            if (st.Use is { } use && Ent(use, where) is var u and >= 0)
            {
                var e = l.Entities[u];
                me.Teleport(Floor(e.At + World.FlatForward(e.Yaw) * 0.9f));
                if (!game.ServerUseLogic(me, u, now))
                    Fail($"{m.Name}: test.events {where}: using \"{use}\" from in front of it was refused");
            }
            else if (st.Do is { } act && Port(act, where) is ({ } di and >= 0, var an))
            {
                var acts = Enum.GetValues<LogicAction>().Where(a => MapLogic.Takes(l.Entities[di], a) && MapLogic.PortName(a).Equals(an, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (acts.Length == 0)
                    Fail($"{m.Name}: test.events {where}: {l.Entities[di].Name} has no action \"{an}\"");
                else
                    game.SelfTestLogicDeliver(di, acts[0], LogicSignal.Pulse(me.Id));
            }
            else if (st.Send is { } send && Port(send, where) is ({ } si and >= 0, var en))
            {
                var evs = Enum.GetValues<LogicEvent>().Where(x => MapLogic.Sends(l.Entities[si], x) && MapLogic.PortName(x).Equals(en, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (evs.Length == 0)
                    Fail($"{m.Name}: test.events {where}: {l.Entities[si].Name} has no event \"{en}\"");
                else
                    game.SelfTestLogicEmit(si, evs[0], LogicSignal.Pulse(me.Id));
            }
            else if (st.Enter is { } enter && Ent(enter, where) is var z and >= 0)
            {
                var b = l.Entities[z].Box;
                if (l.Entities[z].Kind is not (LogicKind.Zone or LogicKind.Plate))
                    Fail($"{m.Name}: test.events {where}: \"{enter}\" isn't a zone or plate");
                me.Teleport(new Vector3(b.Center.X, b.Min.Y, b.Center.Z));
            }
            else if (st.Leave != null)
                me.Teleport(home);
            Run(game, ref now, st.Wait);
            foreach (var x in st.Expect)
            {
                checks++;
                if (!fired.Contains(x))
                    Fail($"{m.Name}: test.events {where}: expected \"{x}\" (sent: {(fired.Count == 0 ? "nothing" : string.Join(", ", fired))})");
            }
            foreach (var x in st.ExpectNot)
            {
                checks++;
                if (fired.Contains(x))
                    Fail($"{m.Name}: test.events {where}: \"{x}\" went out but shouldn't have");
            }
            foreach (var (names, want) in new[] { (st.On, true), (st.Off, false) })
                foreach (var x in names)
                {
                    checks++;
                    if (Ent(x, where) is var i and >= 0 && s.On(i) != want)
                        Fail($"{m.Name}: test.events {where}: \"{x}\" should be {(want ? "on" : "off")}");
                }
        }
        game.LogicEventTap = null;
        _ = quiet;
        m.RestoreProps();
        return log;
    }
}
