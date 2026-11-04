using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// <c>--travel-check [LAG_MS]</c>: a headless dev check of free travel step 2 (GameTravel.cs) over the real transport,
/// in a throwaway save folder. A host plays a new world on the Planetoid with free travel on; a client joins, sits in an
/// Orbiter, and the ship is put in space inside Bluestone's sky. The host must hand Bluestone to the client's game,
/// which opens it beside its link (as Program.Travel.cs does) and lands the ship; then the ship is thrown out of that sky
/// and must come back to the host's space with the traveller seated in it, Bluestone kept in the world's folder. The
/// exit code is the number of problems; the lines are in callofslop.log (<c>[travel-check]</c>).
/// </summary>
public static class GameTravelCheck
{
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
        Game? keeper = null;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: home.Id)
        {
            MapVoteEnabled = false, LocalWorldKey = 11, FreeTravel = true,
        };
        server.Mode = GameMode.Sandbox;
        server.WorldAttach(Game.WorldNew("Travel Check", home, survival: false));
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Traveller", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id)
        {
            LocalWorldKey = 22,
        };
        var opened = -1.0;
        var back = -1.0;
        void Frame()
        {
            var t = clock.Elapsed.TotalSeconds;
            server.Update(t, World.TickDt);
            a.Update(t, World.TickDt);
            if (keeper == null && a.PendingTravel is { } open)
            {
                a.PendingTravel = null;
                keeper = Game.OpenKeeper(a, open, out var why);
                Want(keeper != null, $"the client couldn't open {open.Map}: {why}");
                opened = t;
            }
            if (keeper != null)
            {
                keeper.Update(t, World.TickDt);
                if (keeper.PendingReturn is { } payload)
                {
                    a.TravelReturn(payload);
                    keeper.Dispose();
                    keeper = null;
                    back = t;
                }
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
        try
        {
            WaitFor(() => server.Phase == MatchPhase.Playing && a.LocalId != byte.MaxValue && server.WorldSession is { Applied: true }, 15);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
            {
                Say("the client didn't join");
                return 1;
            }
            server.Net.SimulateBadNetwork(lag, lag / 5, 2);
            a.Net.SimulateBadNetwork(lag, lag / 5, 2);
            World.Enter(server.Place);
            server.SelfTestRespawn(sp, clock.Elapsed.TotalSeconds);
            sp.ImmuneUntil = 1e9;
            WaitFor(() => sp.Alive, 3);

            // The ship, built on the host by the pole, the traveller seated in it.
            World.Enter(server.Place);
            var pad = pl.GroundUnder(pl.Center + new Vector3(30, (float)pl.Radius + 400, 30));
            var ship = ConstructBuild.Build(server.Constructs, bp, pad + new Vector3(0, 2.6f, 0), Quaternion.Identity, out var whyNot);
            if (ship == null)
            {
                Say($"the Orbiter didn't build: {whyNot}");
                return 1;
            }
            ship.Rebuild();
            var parts = ship.PartCount;
            var seat = -1;
            for (var i = 0; i < ship.PartCount; i++)
                if (ship.Parts[i].D.Role == PartRole.Seat && seat < 0)
                    seat = i;
            ref var hand = ref server.SandboxHands.Hand(sp.Id);
            hand.SeatBody = ship.Id;
            hand.SeatPart = (ushort)seat;
            ship.Parts[seat].SitterPlus1 = (byte)(sp.Id + 1);
            ship.Wake();
            WaitFor(() => false, 1.0);

            // Out of the Planetoid's sky, then into Bluestone's.
            World.Enter(server.Place);
            var edge = server.SpaceEdge;
            ship.Pos = pl.Center + new Vector3(0, edge + 80, 0) - Vector3.Transform(ship.Com, ship.Rot);
            ship.Vel = Vector3.Zero;
            ship.Wake();
            WaitFor(() => ship.SpaceShip != 0, 3);
            Want(ship.SpaceShip != 0, "the Orbiter didn't leave the Planetoid's sky");
            if (server.Space?.Flight is not { } f || !StarSystems.ForMap("Bluestone", out _, out var blueBody))
            {
                Say("no space flight, or Bluestone isn't in the system");
                return 1;
            }
            var si = f.Ships.IndexOfId(ship.SpaceShip);
            var blueEdge = Math.Max(Math.Max(away.HalfX, away.HalfZ), blue.Radius + 500);
            f.Ships.SetState(si, blueBody, new Vec3d(0, 0, 1) * (blueEdge * 0.97), Vec3d.Zero);
            var sent = WaitFor(() => opened >= 0, 20);
            Want(sent, "the host never handed Bluestone to the traveller's game");
            World.Enter(server.Place);
            Want(server.Constructs.Get(ship.Id) == null && sp.AwayOn == "Bluestone" && !sp.Alive,
                $"the host still has the ship or the traveller (ship {server.Constructs.Get(ship.Id) != null}, away on {sp.AwayOn ?? "nothing"}, alive {sp.Alive})");
            Say($"handed over after {opened:0.0} s: host away mark {sp.AwayOn}, ship gone from the host {server.Constructs.Get(ship.Id) == null}");
            if (!sent || keeper == null)
                return Report(problems, Say);

            // On the traveller's game: the ship comes down, the traveller in it.
            var down = WaitFor(() => keeper?.Keep is { Down: true }, 20);
            Construct? kShip = null;
            if (keeper != null)
            {
                World.Enter(keeper.Place);
                var me = keeper.Local;
                kShip = me != null && keeper.SandboxHands.Hand(me.Id) is { Seated: true } kh ? keeper.Constructs.Get(kh.SeatBody) : null;
                Say($"on the traveller's game: {World.Map.Name}, {keeper.Constructs.Bodies.Count} constructs, the traveller alive {me?.Alive}, seated in {kShip?.Id ?? 0} ({kShip?.PartCount ?? 0} parts), down {keeper.Keep?.Down}, {(kShip != null ? $"{ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(kShip.WorldCom)):0} m up" : "")}");
            }
            Want(down && kShip != null && kShip.PartCount == parts, $"the ship didn't come down on Bluestone with the traveller in it (down {down}, seated {kShip != null}, parts {kShip?.PartCount ?? 0} of {parts})");
            WaitFor(() => false, 2.0);

            // Home: thrown out of Bluestone's sky.
            if (keeper != null && kShip != null)
            {
                World.Enter(keeper.Place);
                var kEdge = keeper.SpaceEdge;
                var centre = blue.Center;
                kShip.Pos = centre + new Vector3(0, kEdge + 80, 0) - Vector3.Transform(kShip.Com, kShip.Rot);
                kShip.Vel = new Vector3(0, 60, 0);
                kShip.Wake();
            }
            var home2 = WaitFor(() => back >= 0, 15);
            Want(home2, "the traveller's game never sent the ship home");
            var seated = WaitFor(() =>
            {
                World.Enter(server.Place);
                return sp.Alive && sp.AwayOn == null && server.SandboxHands.Hand(sp.Id) is { Seated: true } h2 && server.Constructs.Get(h2.SeatBody) is { SpaceShip: not 0 };
            }, 15);
            World.Enter(server.Place);
            var hShip = server.SandboxHands.Hand(sp.Id) is { Seated: true } h3 ? server.Constructs.Get(h3.SeatBody) : null;
            var kept = server.WorldSession is { } ws && File.Exists(WorldStore.PathOf(Path.Combine(ws.Folder, "planets", WorldStore.FolderFor("Bluestone"))));
            Say($"back on the host after {back - opened:0.0} s on Bluestone: alive {sp.Alive}, away {sp.AwayOn ?? "no"}, seated in {hShip?.Id ?? 0} (ship {hShip?.SpaceShip ?? 0}, {hShip?.PartCount ?? 0} parts), Bluestone kept {kept}; the client sees itself alive {a.Local?.Alive}");
            Want(seated && hShip != null && hShip.PartCount == parts, $"the traveller isn't back in the ship in the host's space (seated {seated}, parts {hShip?.PartCount ?? 0} of {parts})");
            Want(kept, "Bluestone wasn't kept in the world's planets folder");
            WaitFor(() => a.Local is { Alive: true }, 5);
            Want(a.Local is { Alive: true }, "the client doesn't see itself alive again");
        }
        finally
        {
            keeper?.Dispose();
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
