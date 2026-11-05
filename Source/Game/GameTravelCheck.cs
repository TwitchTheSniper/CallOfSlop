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
        Traveller[] both = [keeper, visitor];
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
                var pad = pl.GroundUnder(pl.Center + new Vector3(30 - 60 * n, (float)pl.Radius + 400, 30));
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

            // 2. The visitor's ship into the same sky: the visitor joins the keeper's game through the host and lands.
            t0 = clock.Elapsed.TotalSeconds;
            Want(ToBluestone(visitor.Ship!, 1), "the visitor's Orbiter didn't leave the Planetoid's sky");
            var visiting = WaitFor(() => visitor.Here is { Visit: not null, Local.Alive: true }, 25);
            Want(visiting, "the visitor's game never joined the keeper's");
            Game? kg = keeper.Here;
            Player? vOnKeeper = null;
            var seatedThere = WaitFor(() =>
            {
                if (kg == null)
                    return false;
                World.Enter(kg.Place);
                vOnKeeper = kg.Players.Values.FirstOrDefault(p => p.Name == "Visitor");
                return SeatOf(kg, vOnKeeper) is { PartCount: > 0 };
            }, 15);
            Want(seatedThere, "the visitor isn't seated in their ship on the keeper's game");
            Say($"the visitor joined the keeper's game after {clock.Elapsed.TotalSeconds - t0:0.0} s, seated {seatedThere}; ping through the host {visitor.Here?.Net.PingTo(visitor.Here.Net.ServerGuid)} ms");
            var vDown = WaitFor(() => kg != null && SeatOf(kg, vOnKeeper) is { SpaceShip: 0 }, 40);
            var vShip = kg != null ? SeatOf(kg, vOnKeeper) : null;
            var seesIt = visitor.Here is { } vg && SeatOf(vg, vg.Local) is { } mine && mine.PartCount == parts;
            Want(vDown && vShip?.PartCount == parts, $"the visitor's ship didn't come down on Bluestone (down {vDown}, parts {vShip?.PartCount ?? 0} of {parts})");
            Want(seesIt, "the visitor's own game doesn't show them seated in their ship");
            World.Enter(server.Place);
            Want(visitor.OnHost!.AwayOn == "Bluestone" && !visitor.OnHost.Alive && server.Constructs.Get(visitor.Ship!.Id) == null,
                "the host still has the visitor or their ship");
            Say($"on the keeper's game: {kg?.Constructs.Bodies.Count} constructs, {kg?.Players.Count} people, the visitor's ship down {vDown}");
            WaitFor(() => false, 1.5);

            // 3. The visitor flies out: back in the host's space, in their ship.
            if (kg != null && vShip != null)
                ThrowOut(kg, vShip, blue);
            var vBack = WaitFor(() => visitor.Here == null && SeatOf(server, visitor.OnHost) is { SpaceShip: not 0 }, 20);
            var vHostShip = SeatOf(server, visitor.OnHost);
            Want(vBack && vHostShip?.PartCount == parts, $"the visitor isn't back in their ship in the host's space (back {vBack}, visiting still {visitor.Here != null}, parts {vHostShip?.PartCount ?? 0})");
            Want(WaitFor(() => visitor.Link.Local is { Alive: true }, 5), "the visitor's game doesn't see them alive again");
            Say($"the visitor is back on the host: ship {vHostShip?.SpaceShip ?? 0}, {vHostShip?.PartCount ?? 0} parts");

            // 4. The keeper flies out: back too, Bluestone kept.
            kg = keeper.Here;
            var kNow = kg != null ? SeatOf(kg, kg.Local) : null;
            if (kg != null && kNow != null)
                ThrowOut(kg, kNow, blue);
            var kBack = WaitFor(() => keeper.Here == null && SeatOf(server, keeper.OnHost) is { SpaceShip: not 0 }, 20);
            var kHostShip = SeatOf(server, keeper.OnHost);
            World.Enter(server.Place);
            var kept = server.WorldSession is { } ws && File.Exists(WorldStore.PathOf(Path.Combine(ws.Folder, "planets", WorldStore.FolderFor("Bluestone"))));
            Want(kBack && kHostShip?.PartCount == parts, $"the keeper isn't back in their ship in the host's space (back {kBack}, parts {kHostShip?.PartCount ?? 0})");
            Want(kept, "Bluestone wasn't kept in the world's planets folder");
            Want(server.KeeperOf("Bluestone") == null, "the host still thinks Bluestone is kept on someone's game");
            Want(WaitFor(() => keeper.Link.Local is { Alive: true }, 5), "the keeper's game doesn't see them alive again");
            Say($"the keeper is back on the host: ship {kHostShip?.SpaceShip ?? 0}, Bluestone kept {kept}");
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
