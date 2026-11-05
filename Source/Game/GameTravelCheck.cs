using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// <c>--travel-check [LAG_MS]</c>: a headless dev check of free travel steps 2 and 3 (GameTravel.cs, GameTravelMeet.cs)
/// over the real transport, in a throwaway save folder. A host plays a new world on the Planetoid with free travel on; two
/// clients join, each in an Orbiter. The first ship is put inside Bluestone's sky: the host must hand Bluestone to that
/// client's game (the keeper), which lands it. The second is put there too: its client must visit the keeper's game
/// through the host and land. The visitor then flies out and must be back in the host's space in its ship; then the
/// keeper flies out and must be back too, Bluestone kept in the world's folder. Each client runs
/// <see cref="Game.TravelStep"/> as Program.Travel.cs does. The exit code is the number of problems; the lines are in
/// callofslop.log (<c>[travel-check]</c>).
/// </summary>
public static class GameTravelCheck
{
    private sealed class Traveller
    {
        public required Game Link;
        public Game? Here;
        public Construct? Ship;
        public Player? OnHost;
        public Game Now => Here ?? Link;
    }

    public static int Run(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--travel-check", StringComparison.OrdinalIgnoreCase));
        var lag = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var l) ? Math.Clamp(l, 0, 500) : 60;
        var problems = new List<string>();
        void Say(string s) => Console.WriteLine($"[travel-check] {s}");
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        SavePaths.UseFolder(Path.Combine(Path.GetTempPath(), $"CallOfSlop-travel-check-{Environment.ProcessId}"), "travel check");
        StarSystems.Reload();
        if (Maps.Find("Planetoid") is not { Planetoid: { } pl } home || Maps.Find("Bluestone") is not { Planetoid: { } blue } away
            || ConstructBuild.FindBlueprint("Orbiter") is not { } bp)
        {
            Say("the Planetoid or Bluestone map, or the Orbiter blueprint, is missing");
            return 1;
        }
        const ushort port = 26591;
        var clock = Stopwatch.StartNew();
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: home.Id)
        {
            MapVoteEnabled = false, LocalWorldKey = 11, FreeTravel = true,
        };
        server.Mode = GameMode.Sandbox;
        server.WorldAttach(Game.WorldNew("Travel Check", home, survival: false));
        var keeper = new Traveller
        {
            Link = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Keeper", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { LocalWorldKey = 22 },
        };
        var visitor = new Traveller
        {
            Link = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Visitor", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { LocalWorldKey = 33 },
        };
        var third = new Traveller
        {
            Link = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Third", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { LocalWorldKey = 44 },
        };
        Traveller[] both = [keeper, visitor, third];
        void Frame()
        {
            var t = clock.Elapsed.TotalSeconds;
            server.Update(t, World.TickDt);
            foreach (var tr in both)
            {
                tr.Link.Update(t, World.TickDt);
                tr.Here?.Update(t, World.TickDt);
                var next = Game.TravelStep(tr.Link, tr.Here, out var said);
                if (said != null)
                    Say($"{tr.Link.LocalName}: {said}");
                tr.Here = ReferenceEquals(next, tr.Link) ? null : next;
            }
            Thread.Sleep(4);
        }
        bool WaitFor(Func<bool> cond, double seconds)
        {
            var until = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < until && !cond())
                Frame();
            return cond();
        }
        // The seat a game's own player sits in, and the construct (null: not seated).
        static Construct? SeatOf(Game g, Player? p)
        {
            World.Enter(g.Place);
            return p != null && p.Alive && g.SandboxHands.Hand(p.Id) is { Seated: true } h ? g.Constructs.Get(h.SeatBody) : null;
        }
        // Throws a ship out of the sky of the planetoid its game plays.
        static void ThrowOut(Game g, Construct c, Planetoid p)
        {
            World.Enter(g.Place);
            c.Pos = p.Center + new Vector3(0, g.SpaceEdge + 80, 0) - Vector3.Transform(c.Com, c.Rot);
            c.Vel = new Vector3(0, 60, 0);
            c.Wake();
        }
        var blueEdge = Math.Max(Math.Max(away.HalfX, away.HalfZ), blue.Radius + 500);
        // A ship of the host's, out of the Planetoid's sky and then into Bluestone's.
        bool ToBluestone(Construct ship, double side)
        {
            ThrowOut(server, ship, pl);
            ship.Vel = Vector3.Zero;
            var left = WaitFor(() => ship.SpaceShip != 0, 3);
            World.Enter(server.Place);
            if (!left || server.Space?.Flight is not { } f || !StarSystems.ForMap("Bluestone", out _, out var blueBody))
                return false;
            var si = f.Ships.IndexOfId(ship.SpaceShip);
            f.Ships.SetState(si, blueBody, new Vec3d(side * 0.3, 0, 1).Normalized() * (blueEdge * 0.97), Vec3d.Zero);
            return true;
        }
        try
        {
            WaitFor(() => server.Phase == MatchPhase.Playing && both.All(tr => tr.Link.LocalId != byte.MaxValue) && server.WorldSession is { Applied: true }, 15);
            foreach (var tr in both)
            {
                if (tr.Link.LocalId == byte.MaxValue || !server.Players.TryGetValue(tr.Link.LocalId, out var sp))
                {
                    Say($"{tr.Link.LocalName} didn't join");
                    return 1;
                }
                tr.OnHost = sp;
                tr.Link.Net.SimulateBadNetwork(lag, lag / 5, 2);
            }
            server.Net.SimulateBadNetwork(lag, lag / 5, 2);

            // Two ships, built on the host by the pole, a traveller seated in each.
            var parts = 0;
            for (var n = 0; n < both.Length; n++)
            {
                var tr = both[n];
                var sp = tr.OnHost!;
                World.Enter(server.Place);
                server.SelfTestRespawn(sp, clock.Elapsed.TotalSeconds);
                sp.ImmuneUntil = 1e9;
                WaitFor(() => sp.Alive, 3);
                World.Enter(server.Place);
                var pad = pl.GroundUnder(pl.Center + new Vector3(30 - 40 * n, (float)pl.Radius + 400, 30));
                var ship = ConstructBuild.Build(server.Constructs, bp, pad + new Vector3(0, 2.6f, 0), Quaternion.Identity, out var whyNot);
                if (ship == null)
                {
                    Say($"the Orbiter didn't build: {whyNot}");
                    return 1;
                }
                ship.Rebuild();
                parts = ship.PartCount;
                var seat = -1;
                for (var i = 0; i < ship.PartCount; i++)
                    if (ship.Parts[i].D.Role == PartRole.Seat && seat < 0)
                        seat = i;
                ref var hand = ref server.SandboxHands.Hand(sp.Id);
                hand.SeatBody = ship.Id;
                hand.SeatPart = (ushort)seat;
                ship.Parts[seat].SitterPlus1 = (byte)(sp.Id + 1);
                ship.Wake();
                tr.Ship = ship;
            }
            WaitFor(() => false, 1.0);

            // 1. The keeper's ship into Bluestone's sky: Bluestone opens on the keeper's game and the ship comes down.
            var t0 = clock.Elapsed.TotalSeconds;
            Want(ToBluestone(keeper.Ship!, -1), "the keeper's Orbiter didn't leave the Planetoid's sky");
            var opened = WaitFor(() => keeper.Here is { Keep: not null }, 20);
            Want(opened, "the host never handed Bluestone to the keeper's game");
            if (!opened)
                return Report(problems, Say);
            Say($"Bluestone opened on the keeper's game after {clock.Elapsed.TotalSeconds - t0:0.0} s");
            var down = WaitFor(() => keeper.Here?.Keep is { Down: true }, 20);
            var kShip = SeatOf(keeper.Here!, keeper.Here!.Local);
            Want(down && kShip?.PartCount == parts, $"the keeper's ship didn't come down on Bluestone with the keeper in it (down {down}, parts {kShip?.PartCount ?? 0} of {parts})");

            // 2. Two more ships into the same sky: each visitor joins the keeper's game through the host and lands.
            var kg = keeper.Here!;
            Player? OnKeeper(Game g, string name)
            {
                World.Enter(g.Place);
                return g.Players.Values.FirstOrDefault(p => p.Name == name);
            }
            foreach (var (tr, side) in new[] { (visitor, 1.0), (third, 0.0) })
            {
                var name = tr.Link.LocalName;
                t0 = clock.Elapsed.TotalSeconds;
                Want(ToBluestone(tr.Ship!, side), $"{name}'s Orbiter didn't leave the Planetoid's sky");
                var visiting = WaitFor(() => tr.Here is { Visit: not null, Local.Alive: true }, 25);
                Want(visiting, $"{name}'s game never joined the keeper's");
                var seatedThere = WaitFor(() => SeatOf(kg, OnKeeper(kg, name)) is { PartCount: > 0 }, 15);
                Want(seatedThere, $"{name} isn't seated in their ship on the keeper's game");
                Say($"{name} joined the keeper's game after {clock.Elapsed.TotalSeconds - t0:0.0} s, seated {seatedThere}");
                var landed = WaitFor(() => SeatOf(kg, OnKeeper(kg, name)) is { SpaceShip: 0 }, 40);
                var there = SeatOf(kg, OnKeeper(kg, name));
                var seesIt = tr.Here is { } vg && SeatOf(vg, vg.Local) is { } mine && mine.PartCount == parts;
                Want(landed && there?.PartCount == parts, $"{name}'s ship didn't come down on Bluestone (down {landed}, parts {there?.PartCount ?? 0} of {parts})");
                Want(seesIt, $"{name}'s own game doesn't show them seated in their ship");
                World.Enter(server.Place);
                Want(tr.OnHost!.AwayOn == "Bluestone" && !tr.OnHost.Alive && server.Constructs.Get(tr.Ship!.Id) == null,
                    $"the host still has {name} or their ship");
            }
            World.Enter(kg.Place);
            Say($"on the keeper's game: {kg.Constructs.Bodies.Count} constructs, {kg.Players.Count} people");
            WaitFor(() => false, 1.5);

            // 3. A visitor flies out: back in the host's space, in their ship.
            if (SeatOf(kg, OnKeeper(kg, "Third")) is { } tShip)
                ThrowOut(kg, tShip, blue);
            var tBack = WaitFor(() => third.Here == null && SeatOf(server, third.OnHost) is { SpaceShip: not 0 }, 20);
            var tHostShip = SeatOf(server, third.OnHost);
            Want(tBack && tHostShip?.PartCount == parts, $"Third isn't back in their ship in the host's space (back {tBack}, visiting still {third.Here != null}, parts {tHostShip?.PartCount ?? 0})");
            Want(WaitFor(() => third.Link.Local is { Alive: true }, 5), "Third's game doesn't see them alive again");
            Say($"Third is back on the host: ship {tHostShip?.SpaceShip ?? 0}, {tHostShip?.PartCount ?? 0} parts");
            World.Enter(third.Link.Place);
            var shown = third.Link.Players.TryGetValue(visitor.OnHost!.Id, out var vSeen) ? ScoreboardModel.Status(vSeen, false) : "(not in the list)";
            Want(shown == "on Bluestone", $"Third's Tab list doesn't say the visitor is on Bluestone (\"{shown}\")");
            // A word from Bluestone in the host's chat, said through the visitor's link: everyone hears it, and not as a dead player's.
            visitor.Link.SendChat("hello from Bluestone", 1e9);
            var heard = WaitFor(() => third.Link.Chat.Count > 0 && third.Link.Chat[^1].Text == "hello from Bluestone", 5);
            Want(heard && (third.Link.Chat[^1].Flags & ChatFlags.Dead) == 0, $"Third didn't hear the visitor's chat line from Bluestone as a living player's (heard {heard})");

            // 4. The keeper flies out with the visitor still there: back on the host, and the visitor's game keeps Bluestone now.
            World.Enter(kg.Place);
            var vWas = OnKeeper(kg, "Visitor")?.Position ?? Vector3.Zero;
            if (SeatOf(kg, kg.Local) is { } kNow)
                ThrowOut(kg, kNow, blue);
            var kBack = WaitFor(() => keeper.Here == null && SeatOf(server, keeper.OnHost) is { SpaceShip: not 0 }, 20);
            var kHostShip = SeatOf(server, keeper.OnHost);
            Want(kBack && kHostShip?.PartCount == parts, $"the keeper isn't back in their ship in the host's space (back {kBack}, parts {kHostShip?.PartCount ?? 0})");
            t0 = clock.Elapsed.TotalSeconds;
            var took = WaitFor(() => visitor.Here is { Keep: not null, Local.Alive: true } vk && vk.WorldSession is { Applied: true }, 20);
            Want(took, "the visitor's game didn't take Bluestone over");
            Want(server.KeeperOf("Bluestone") == visitor.OnHost, $"the host doesn't have the visitor as Bluestone's keeper ({server.KeeperOf("Bluestone")?.Name ?? "nobody"})");
            var ng = visitor.Here;
            Construct? vShip = null;
            if (ng != null && took)
            {
                World.Enter(ng.Place);
                var me = ng.Local!;
                vShip = ng.Constructs.Bodies.Where(c => c.PartCount == parts).OrderBy(c => Vector3.Distance(c.WorldCom, me.Position)).FirstOrDefault();
                Say($"the visitor keeps Bluestone after {clock.Elapsed.TotalSeconds - t0:0.0} s: {ng.Constructs.Bodies.Count} constructs, standing {Vector3.Distance(me.Position, vWas):0.0} m from where they were, their ship {(vShip != null ? Vector3.Distance(vShip.WorldCom, me.Position) : -1):0.0} m off");
                Want(ng.Constructs.Bodies.Count == 1 && vShip != null, $"the visitor's game doesn't have their ship alone on Bluestone ({ng.Constructs.Bodies.Count} constructs)");
                Want(Vector3.Distance(me.Position, vWas) < 15, "the visitor isn't where they were on Bluestone");
            }

            // 5. The new keeper sits in their ship and flies out: back too, Bluestone kept.
            if (ng != null && vShip != null)
            {
                World.Enter(ng.Place);
                var me = ng.Local!;
                var seat = -1;
                for (var i = 0; i < vShip.PartCount && seat < 0; i++)
                    if (vShip.Parts[i].D.Role == PartRole.Seat && vShip.Parts[i].SitterPlus1 == 0)
                        seat = i;
                ref var hand = ref ng.SandboxHands.Hand(me.Id);
                hand.SeatBody = vShip.Id;
                hand.SeatPart = (ushort)seat;
                vShip.Parts[seat].SitterPlus1 = (byte)(me.Id + 1);
                vShip.Wake();
                WaitFor(() => ng.Keep is { Down: true }, 3);
                ThrowOut(ng, vShip, blue);
            }
            var vBack = WaitFor(() => visitor.Here == null && SeatOf(server, visitor.OnHost) is { SpaceShip: not 0 }, 20);
            var vHostShip = SeatOf(server, visitor.OnHost);
            World.Enter(server.Place);
            var kept = server.WorldSession is { } ws && File.Exists(WorldStore.PathOf(Path.Combine(ws.Folder, "planets", WorldStore.FolderFor("Bluestone"))));
            Want(vBack && vHostShip?.PartCount == parts, $"the visitor isn't back in their ship in the host's space (back {vBack}, parts {vHostShip?.PartCount ?? 0})");
            Want(kept, "Bluestone wasn't kept in the world's planets folder");
            Want(server.KeeperOf("Bluestone") == null, "the host still thinks Bluestone is kept on someone's game");
            Want(WaitFor(() => both.All(tr => tr.Link.Local is { Alive: true }), 5), "a traveller's game doesn't see them alive again");
            Say($"everyone is back on the host; Bluestone kept {kept}");

            // 6. Two in one ship: the first aboard keeps Bluestone, the other visits; the ship lands, and goes home with both.
            WaitFor(() => false, 1.0);
            World.Enter(server.Place);
            Traveller[] pair = visitor.OnHost.Id < third.OnHost!.Id ? [visitor, third] : [third, visitor];
            foreach (var tr in pair)
            {
                if (SeatOf(server, tr.OnHost) is { } old)
                {
                    World.Enter(server.Place);
                    var h = server.SandboxHands.Hand(tr.OnHost!.Id);
                    old.Parts[h.SeatPart].SitterPlus1 = 0;
                    server.SandboxHands.Hand(tr.OnHost.Id) = default;
                }
            }
            World.Enter(server.Place);
            var duoPad = pl.GroundUnder(pl.Center + new Vector3(-60, (float)pl.Radius + 400, -30));
            var duo = ConstructBuild.Build(server.Constructs, bp, duoPad + new Vector3(0, 2.6f, 0), Quaternion.Identity, out _);
            if (duo == null)
                return Report([.. problems, "the two-seat Orbiter didn't build"], Say);
            duo.Rebuild();
            var seats = Enumerable.Range(0, duo.PartCount).Where(i => duo.Parts[i].D.Role == PartRole.Seat).ToList();
            for (var i = 0; i < pair.Length && i < seats.Count; i++)
            {
                ref var hand = ref server.SandboxHands.Hand(pair[i].OnHost!.Id);
                hand.SeatBody = duo.Id;
                hand.SeatPart = (ushort)seats[i];
                duo.Parts[seats[i]].SitterPlus1 = (byte)(pair[i].OnHost!.Id + 1);
            }
            duo.Wake();
            WaitFor(() => false, 0.5);
            Want(ToBluestone(duo, 0.5), "the two-seat Orbiter didn't leave the Planetoid's sky");
            var duoOpen = WaitFor(() => pair[0].Here is { Keep: not null } && pair[1].Here is { Visit: not null, Local.Alive: true }, 25);
            Want(duoOpen, $"Bluestone didn't open on {pair[0].Link.LocalName}'s game with {pair[1].Link.LocalName} visiting");
            var dg = pair[0].Here;
            var bothIn = dg != null && WaitFor(() => SeatOf(dg, dg.Local) is { } a && SeatOf(dg, OnKeeper(dg, pair[1].Link.LocalName)) is { } b && a == b, 15);
            Want(bothIn, "the two aren't both seated in the one ship on the keeper's game");
            var duoDown = dg != null && WaitFor(() => dg.Keep is { Down: true }, 40);
            Want(duoDown, "the two-seat ship didn't come down on Bluestone");
            Say($"two in one ship: {pair[0].Link.LocalName} keeps Bluestone, {pair[1].Link.LocalName} visits, both seated {bothIn}, down {duoDown}");
            WaitFor(() => false, 1.0);
            if (dg != null && SeatOf(dg, dg.Local) is { } dShip)
                ThrowOut(dg, dShip, blue);
            var duoBack = WaitFor(() => pair.All(tr => tr.Here == null && SeatOf(server, tr.OnHost) is { SpaceShip: not 0 }), 20);
            var s0 = SeatOf(server, pair[0].OnHost);
            var s1 = SeatOf(server, pair[1].OnHost);
            Want(duoBack && s0 != null && s0 == s1, $"the two aren't back in one ship in the host's space (back {duoBack}, ships {s0?.Id ?? 0} and {s1?.Id ?? 0})");
            Want(server.KeeperOf("Bluestone") == null, "the host still thinks Bluestone is kept after the two came home");
            Say($"the two are back on the host in ship {s0?.SpaceShip ?? 0}");
        }
        finally
        {
            foreach (var tr in both)
            {
                tr.Here?.Dispose();
                tr.Link.Dispose();
            }
        }
        return Report(problems, Say);
    }

    private static int Report(List<string> problems, Action<string> say)
    {
        foreach (var p in problems)
            say($"PROBLEM: {p}");
        say(problems.Count == 0 ? "ok" : $"{problems.Count} problem(s)");
        return problems.Count;
    }
}
