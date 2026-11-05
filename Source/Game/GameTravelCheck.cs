using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// <c>--travel-check [LAG_MS]</c> (default 60, with 2% loss): a headless dev check of free travel, route 1 (GameTravel.cs,
/// GameTravelPlace.cs, GameTravelVisit.cs), over the real transport in a throwaway save folder. A host plays a new world on
/// the Planetoid with free travel on; Ann, Bob and Cat join, each in an Orbiter. All three ships go into Bluestone's sky:
/// the host must open Bluestone beside its main game, and each must join it through their own connection, sit in their
/// ship and land. Cat flies out and must be back in the host's space; Ann flies out and Bob must play on undisturbed;
/// Bob flies out, and Bluestone must be saved into the world's folder and closed. Then Bob and Cat fly in together in
/// one ship and home again in it. Each client runs <see cref="Game.TravelStep"/> as Program.Travel.cs does. The exit
/// code is the number of problems; the lines are in callofslop.log (<c>[travel-check]</c>).
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
        Traveller Join(string name, ulong key) => new()
        {
            Link = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), name, Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { LocalWorldKey = key },
        };
        var ann = Join("Ann", 22);
        var bob = Join("Bob", 33);
        var cat = Join("Cat", 44);
        Traveller[] both = [ann, bob, cat];
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
        // A ship back in the host's space, sent far out of Bluestone's sky so it doesn't fall back in while the check goes on.
        void Park(Construct? ship)
        {
            World.Enter(server.Place);
            if (ship is not { SpaceShip: not 0 } || server.Space?.Flight is not { } f || !StarSystems.ForMap("Bluestone", out _, out var blueBody)
                || f.Ships.IndexOfId(ship.SpaceShip) is not (var si and >= 0))
                return;
            f.Ships.SetState(si, blueBody, new Vec3d(0, 1, 0) * (blueEdge * 40), Vec3d.Zero);
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

            // 1. Ann's ship into Bluestone's sky: Bluestone opens on the host beside its main game, Ann joins it and lands.
            Player? OnPlace(Game g, string name)
            {
                World.Enter(g.Place);
                return g.Players.Values.FirstOrDefault(p => p.Name == name);
            }
            Game? Blue() => server.PlaceOf("Bluestone");
            bool Down(string name) => Blue() is { } bg && SeatOf(bg, OnPlace(bg, name)) is { SpaceShip: 0, PartCount: > 0 };
            Traveller[] trio = [ann, bob, cat];
            foreach (var (tr, side) in new[] { (ann, -1.0), (bob, 1.0), (cat, 0.0) })
            {
                var name = tr.Link.LocalName;
                var t0 = clock.Elapsed.TotalSeconds;
                Want(ToBluestone(tr.Ship!, side), $"{name}'s Orbiter didn't leave the Planetoid's sky");
                var visiting = WaitFor(() => tr.Here is { Visit: not null, Local.Alive: true }, 25);
                Want(visiting, $"{name}'s game never joined Bluestone on the host");
                Want(server.Places.Count == 1, $"the host runs {server.Places.Count} planetoids, not one");
                var seatedThere = WaitFor(() => Blue() is { } bg && SeatOf(bg, OnPlace(bg, name)) is { PartCount: > 0 }, 15);
                Want(seatedThere, $"{name} isn't seated in their ship on Bluestone's game");
                Say($"{name} joined Bluestone on the host after {clock.Elapsed.TotalSeconds - t0:0.0} s, seated {seatedThere}");
                var landed = WaitFor(() => Down(name), 40);
                var seesIt = tr.Here is { } vg && SeatOf(vg, vg.Local) is { } mine && mine.PartCount == parts;
                Want(landed, $"{name}'s ship didn't come down on Bluestone");
                Want(seesIt, $"{name}'s own game doesn't show them seated in their ship");
                World.Enter(server.Place);
                Want(tr.OnHost!.AwayOn == "Bluestone" && !tr.OnHost.Alive && server.Constructs.Get(tr.Ship!.Id) == null,
                    $"the host's main game still has {name} or their ship");
            }
            if (Blue() is { } b1)
            {
                World.Enter(b1.Place);
                Say($"on Bluestone's game: {b1.Constructs.Bodies.Count} constructs, {b1.Players.Count} people");
            }
            WaitFor(() => false, 1.5);

            // 2. Cat flies out: back in the host's space, in their ship; the Tab list and chat still reach Bluestone.
            if (Blue() is { } b2 && SeatOf(b2, OnPlace(b2, "Cat")) is { } cShip)
                ThrowOut(b2, cShip, blue);
            var cBack = WaitFor(() => cat.Here == null && SeatOf(server, cat.OnHost) is { SpaceShip: not 0 }, 20);
            var cHostShip = SeatOf(server, cat.OnHost);
            Want(cBack && cHostShip?.PartCount == parts, $"Cat isn't back in their ship in the host's space (back {cBack}, visiting still {cat.Here != null}, parts {cHostShip?.PartCount ?? 0})");
            Want(WaitFor(() => cat.Link.Local is { Alive: true }, 5), "Cat's game doesn't see them alive again");
            Park(cHostShip);
            Say($"Cat is back on the host: ship {cHostShip?.SpaceShip ?? 0}, {cHostShip?.PartCount ?? 0} parts");
            World.Enter(cat.Link.Place);
            var shown = cat.Link.Players.TryGetValue(bob.OnHost!.Id, out var bSeen) ? ScoreboardModel.Status(bSeen, false) : "(not in the list)";
            Want(shown == "on Bluestone", $"Cat's Tab list doesn't say Bob is on Bluestone (\"{shown}\")");
            bob.Link.SendChat("hello from Bluestone", 1e9);
            var heard = WaitFor(() => cat.Link.Chat.Count > 0 && cat.Link.Chat[^1].Text == "hello from Bluestone", 5);
            Want(heard && (cat.Link.Chat[^1].Flags & ChatFlags.Dead) == 0, $"Cat didn't hear Bob's chat line from Bluestone as a living player's (heard {heard})");

            // 3. Ann flies out with Bob still there: Ann is back, and Bob plays on, undisturbed, on the same game.
            var bg3 = Blue();
            var bWas = bg3 != null ? OnPlace(bg3, "Bob")?.Position ?? Vector3.Zero : Vector3.Zero;
            if (bg3 != null && SeatOf(bg3, OnPlace(bg3, "Ann")) is { } aShip)
                ThrowOut(bg3, aShip, blue);
            var aBack = WaitFor(() => ann.Here == null && SeatOf(server, ann.OnHost) is { SpaceShip: not 0 }, 20);
            var aHostShip = SeatOf(server, ann.OnHost);
            Want(aBack && aHostShip?.PartCount == parts, $"Ann isn't back in their ship in the host's space (back {aBack}, parts {aHostShip?.PartCount ?? 0})");
            Park(aHostShip);
            WaitFor(() => false, 1.0);
            Want(ReferenceEquals(Blue(), bg3) && bob.Here is { Visit: not null, Local.Alive: true }, "Bob's visit to Bluestone didn't carry on as Ann left");
            if (bg3 != null)
            {
                var bNow = OnPlace(bg3, "Bob")?.Position ?? Vector3.Zero;
                Want(Vector3.Distance(bNow, bWas) < 15, $"Bob moved {Vector3.Distance(bNow, bWas):0.0} m as Ann left");
            }

            // 4. Bob flies out too: back, and Bluestone, with nobody on it, is saved into the world's folder and closed.
            if (Blue() is { } b4 && SeatOf(b4, OnPlace(b4, "Bob")) is { } bShip)
                ThrowOut(b4, bShip, blue);
            var bBack = WaitFor(() => bob.Here == null && SeatOf(server, bob.OnHost) is { SpaceShip: not 0 }, 20);
            var bHostShip = SeatOf(server, bob.OnHost);
            Want(bBack && bHostShip?.PartCount == parts, $"Bob isn't back in their ship in the host's space (back {bBack}, parts {bHostShip?.PartCount ?? 0})");
            Park(bHostShip);
            var closed = WaitFor(() => server.Places.Count == 0, Game.PlaceEmptyClose + 10);
            World.Enter(server.Place);
            var kept = server.WorldSession is { } ws && File.Exists(WorldStore.PathOf(Path.Combine(ws.Folder, "planets", WorldStore.FolderFor("Bluestone"))));
            Want(closed, "Bluestone's game didn't close with nobody on it");
            Want(kept, "Bluestone wasn't kept in the world's planets folder");
            Want(WaitFor(() => trio.All(tr => tr.Link.Local is { Alive: true }), 5), "a traveller's game doesn't see them alive again");
            Say($"everyone is back on the host; Bluestone closed {closed}, kept {kept}");

            // 5. Two in one ship: Bluestone opens again from its copy, both join and sit in the one ship, and both go home in it.
            WaitFor(() => false, 1.0);
            World.Enter(server.Place);
            Traveller[] pair = [bob, cat];
            foreach (var tr in pair)
                if (SeatOf(server, tr.OnHost) is { } old)
                {
                    World.Enter(server.Place);
                    var h = server.SandboxHands.Hand(tr.OnHost!.Id);
                    old.Parts[h.SeatPart].SitterPlus1 = 0;
                    server.SandboxHands.Hand(tr.OnHost.Id) = default;
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
            var duoOpen = WaitFor(() => pair.All(tr => tr.Here is { Visit: not null, Local.Alive: true }), 25);
            Want(duoOpen, "Bluestone didn't open again with both aboard joining it");
            var bothIn = WaitFor(() => Blue() is { } dg && SeatOf(dg, OnPlace(dg, "Bob")) is { } a && SeatOf(dg, OnPlace(dg, "Cat")) is { } b && a == b, 15);
            Want(bothIn, "the two aren't both seated in the one ship on Bluestone's game");
            var duoDown = WaitFor(() => Down("Bob"), 40);
            Want(duoDown, "the two-seat ship didn't come down on Bluestone");
            if (Blue() is { } dg2)
            {
                World.Enter(dg2.Place);
                Say($"two in one ship: both seated {bothIn}, down {duoDown}; Bluestone's game has {dg2.Constructs.Bodies.Count} constructs");
            }
            WaitFor(() => false, 1.0);
            if (Blue() is { } dg3 && SeatOf(dg3, OnPlace(dg3, "Bob")) is { } dShip)
                ThrowOut(dg3, dShip, blue);
            var duoBack = WaitFor(() => pair.All(tr => tr.Here == null && SeatOf(server, tr.OnHost) is { SpaceShip: not 0 }), 20);
            var s0 = SeatOf(server, pair[0].OnHost);
            var s1 = SeatOf(server, pair[1].OnHost);
            Want(duoBack && s0 != null && s0 == s1, $"the two aren't back in one ship in the host's space (back {duoBack}, ships {s0?.Id ?? 0} and {s1?.Id ?? 0})");
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
