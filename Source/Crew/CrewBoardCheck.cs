using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check [SHIP] --crew-board [--crew-faction NAME] [--seconds S] [--mortal]</c> (Game/GameCrewDefend.cs): a host and a joined client on
/// localhost on the Proving Ground. SHIP (the Zero Day Frigate) is set down as a nobody's (left to settle on its legs first), and crewed as NAME (Raiders); the host's player stands
/// aboard as a foe (immune unless <c>--mortal</c>). For S seconds (40) the crew's defence runs: each line says who fights, where and with what.
/// Then the host shoots the crew one at a time until it gives up by its faction's rules: after a surrender nobody fights on. Exit 0 when crew stood up, took long guns
/// from the lockers, held posts, fired at the boarder, the client saw a crew body's gun change, and the crew surrendered.
/// Allocates (a tool); real time, not a long simulation.
/// </summary>
public static class CrewBoardCheck
{
    public static int Run(string[] args, string shipName)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 40;
        var mortal = args.Any(a => a.Equals("--mortal", StringComparison.OrdinalIgnoreCase));
        var factionName = Arg("--crew-faction") ?? "Raiders";
        var fails = new List<string>();
        void Say(string line) => Console.WriteLine($"[crew-board] {line}");
        void Check(bool ok, string what)
        {
            Say($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                fails.Add(what);
        }
        SavePaths.UseFolder(Path.Combine(Path.GetTempPath(), $"CallOfSlop-crew-board-{Environment.ProcessId}"), "crew board check");
        StarSystems.Reload();
        if (Maps.Find("Proving Ground") is not { } map || ConstructBuild.FindBlueprint(shipName) is not { } bp)
        {
            Say($"the Proving Ground map or the blueprint \"{shipName}\" is missing");
            return 1;
        }
        var faction = CrewPowers.Find(factionName);
        if (faction == 0)
        {
            Say($"no faction \"{factionName}\"");
            return 1;
        }
        const ushort port = 26604;
        var clock = Stopwatch.StartNew();
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false, LocalWorldKey = 11,
        };
        server.Mode = GameMode.Sandbox;
        server.WorldAttach(Game.WorldNew("Crew Board Check", map, survival: false));
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Ann", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { LocalWorldKey = 22 };
        var now = 0.0;
        var frames = 0;
        void Frame()
        {
            now += World.TickDt;
            World.Enter(server.Place);
            server.Update(now, World.TickDt);
            World.Enter(client.Place);
            client.Update(now, World.TickDt);
            if (++frames % 3 == 0)
                Thread.Sleep(1);
        }
        bool WaitFor(Func<bool> cond, double secs)
        {
            var until = now + secs;
            while (now < until && !cond())
                Frame();
            return cond();
        }
        if (!WaitFor(() => client.LocalId != byte.MaxValue && server.Local != null && server.Phase == MatchPhase.Playing, 30))
        {
            Check(false, "the client joined and the host plays");
            return 1;
        }
        World.Enter(server.Place);
        server.RequestRespawn();
        World.Enter(client.Place);
        client.RequestRespawn();
        if (!WaitFor(() => server.Local is { Alive: true }, 20))
        {
            Check(false, "the host's player spawned");
            return 1;
        }
        World.Enter(server.Place);
        var host = server.Local!;
        if (!mortal)
            host.ImmuneUntil = now + 1e6;
        foreach (var p in server.Players.Values)
            if (p != host && p.Bot == null)
                p.ImmuneUntil = now + 1e6; // (the client's player watches from the ground: the ship's guns don't end the check)
        var c = ConstructBuild.BuildOnGround(server.Constructs, bp, ConstructWorld.Ground(World.Query, 0, 40), 0, out var why);
        if (c == null)
        {
            Check(false, $"{bp.Name} builds: {why}");
            return 1;
        }
        c.Owner = 255;
        c.Rebuild();
        server.Constructs.StructureDirty.Add(c.Id);
        var crew = server.Crew.Crew(c, ShipAiMode.Strangers, faction: faction);
        Check(crew != null, $"{bp.Name} ({c.PartCount} parts) is crewed as {factionName}: {(crew != null ? server.Crew.Describe(crew) : "no crew")}");
        if (crew == null)
            return 1;
        var lockers = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Name.Contains("Locker") && !c.DefOf(i).Name.Contains("Suit"))
                lockers++;
        Say($"{lockers} lockers aboard");
        // Settled on its legs first: built over the ground it drops about 2 m at up to 7 m/s, and riders on its decks then sink into them
        // or fall off (seen; see the notes).
        var still = 0.0;
        WaitFor(() => (still = c.Vel.Length() < 0.2f ? still + World.TickDt : 0) > 2, 20);
        World.Enter(server.Place);
        Say($"settled: {c.Vel.Length():0.00} m/s at {ConstructPhysics.HoldHeight(c.WorldCom):0.0} m");
        // The host by the ship (bodies come to life near a person), then aboard at the first couch's feet.
        host.Teleport(ConstructWorld.Ground(World.Query, c.WorldCom.X + 25, c.WorldCom.Z) + Vector3.UnitY);
        WaitFor(() => server.Crew.Bodied >= Math.Min(crew.MemberCount, 4), 15);
        World.Enter(server.Place);
        var couch = -1;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Name == "Crash Couch")
            {
                couch = i;
                break;
            }
        var aboard = couch >= 0 ? ConstructEdits.SeatFeet(c, couch) : c.WorldCom;
        host.Teleport(aboard + GravityFrames.UpAt(aboard) * 0.4f + Vector3.Normalize(c.WorldCom - aboard with { Y = 0 }) * 1.0f); // (aboard at once: off it, its guns would see the host)
        // Onto the walk graph's spot nearest the couch (a floor a person stands on), once the crew's graph is built.
        WaitFor(() => crew.Nav is { } ng && crew.NavBuild == ng.Build, 10);
        World.Enter(server.Place);
        crew.MarkFoe(host.Id); // (a foe: as if they had shot at it)
        Vector3? standAt = null;
        // Half way from the couch to the helm: a corridor, not a corner by a fitting.
        var mid = crew.Helm >= 0 ? (c.ToLocal(aboard) + CrewWorld.PartFoot(c, crew.StPart[crew.Helm])) * 0.5f : c.ToLocal(aboard);
        if (crew.Nav is { } g && crew.NavBuild == g.Build && CrewNav.NearestNode(g, mid, 4f) is var node and >= 0)
        {
            standAt = g.NodePos(node);
            server.StandAboard(host, c, standAt.Value); // (on the deck's walked spot, riding it as the crew do)
            aboard = host.Position;
        }
        Say($"host aboard at {aboard.X:0.0}, {aboard.Y:0.0}, {aboard.Z:0.0} (by the {(couch >= 0 ? c.DefOf(couch).Name : "middle")}); crew bodies {server.Crew.Bodied}");
        // The client's view of the crew bodies' guns at the start.
        var clientWeapons = new Dictionary<byte, byte>();
        World.Enter(client.Place);
        foreach (var p in client.Players.Values)
            if (p.Id != client.LocalId && p.Name.StartsWith('['))
                clientWeapons[p.Id] = p.Weapon;
        var start = now;
        var until = now + seconds;
        var nextSay = now;
        int maxFighting = 0, maxArmed = 0, maxHeld = 0;
        var shots0 = server.BotStats.Shots.Sum();
        var clientChanged = false;
        var hostHealth = host.Health;
        var minHealth = host.Health;
        while (now < until)
        {
            Frame();
            World.Enter(server.Place);
            int fighting = 0, armed = 0, held = 0;
            foreach (var p in server.Players.Values)
                if (p.CrewSlot >= 0 && server.Crew.State[p.CrewSlot] == CrewState.Fighting)
                {
                    fighting++;
                    var (a, st) = server.CrewGuardOf(p.Id);
                    if (a)
                        armed++;
                    if (st is "Hold" or "Hunt")
                        held++;
                }
            maxFighting = Math.Max(maxFighting, fighting);
            maxArmed = Math.Max(maxArmed, armed);
            maxHeld = Math.Max(maxHeld, held);
            minHealth = Math.Min(minHealth, host.Alive ? host.Health : 0);
            World.Enter(client.Place);
            foreach (var p in client.Players.Values)
                if (p.Id != client.LocalId && p.Name.StartsWith('['))
                {
                    if (!clientWeapons.TryGetValue(p.Id, out var w0))
                        clientWeapons[p.Id] = p.Weapon; // (the first the client knows of it)
                    else if (p.Weapon != w0)
                        clientChanged = true;
                }
            if (standAt is { } sa && host.Alive && c.ToLocal(host.Position).Y < sa.Y - 0.1f)
            {
                // The ship lurched as its crew got going (seen: 4 m/s, and the host's player, with no input of its own, sank 0.25 m into the
                // deck): back onto it, or the crew can only see a head at floor level.
                Say($"the host sank {sa.Y - c.ToLocal(host.Position).Y:0.00} m into the deck as the ship moved ({c.Vel.Length():0.0} m/s): stood on it again");
                server.StandAboard(host, c, sa);
            }
            if (now >= nextSay)
            {
                nextSay = now + 5;
                World.Enter(server.Place);
                Say($"{now - start:0} s: ship {c.Vel.Length():0.0} m/s at {ConstructPhysics.HoldHeight(c.WorldCom):0} m, host local {c.ToLocal(host.Position)}; act {crew.Act}, morale {crew.Morale * 100:0}%, host {(host.Alive ? $"alive {host.Health} hp, aboard {host.Sim.RideBody == c.Id}" : "dead")}; {server.CrewDefendLine()}");
            }
            if (!host.Alive)
                break;
        }
        World.Enter(server.Place);
        var shots = server.BotStats.Shots.Sum() - shots0;
        Say($"after {now - start:0} s ({clock.Elapsed.TotalSeconds:0} s real): {server.Crew.Describe(crew)}");
        Check(maxFighting > 0, $"crew took up arms: {maxFighting} fighting at once");
        Check(maxArmed > 0 || lockers == 0, $"crew took long guns from the lockers: {maxArmed} armed at once ({lockers} lockers aboard)");
        Check(maxHeld > 0, $"crew held posts: {maxHeld} at once");
        Check(shots > 0, $"crew fired at the boarder: {shots} shots{(mortal ? $", the host {(host.Alive ? $"at {host.Health} of {hostHealth} hp (lowest {minHealth})" : "killed")}" : "")}");
        Check(clientChanged, $"the client saw a crew body's gun change ({clientWeapons.Count} crew bodies seen)");
        // Beaten: morale under its line; the next looks surrender, and nobody fights on.
        if (host.Alive && crew.Act is not (BoardAct.Surrender or BoardAct.Abandon))
        {
            // Losses: the host shoots one crew member at a time until the crew gives up (or none is left).
            for (var k = 0; k < 32 && crew.Act is not (BoardAct.Surrender or BoardAct.Abandon or BoardAct.Scuttle); k++)
            {
                World.Enter(server.Place);
                var shot = server.CrewShootAny(host, c.Id);
                Say($"losses: {shot ?? "nobody left to shoot"}");
                if (shot == null)
                    break;
                WaitFor(() => crew.Act is BoardAct.Surrender or BoardAct.Abandon or BoardAct.Scuttle, 2);
            }
            World.Enter(server.Place);
            var fightOn = 0;
            foreach (var p in server.Players.Values)
                if (p.CrewSlot >= 0 && server.Crew.State[p.CrewSlot] == CrewState.Fighting && crew.Act == BoardAct.Surrender)
                    fightOn++;
            Say($"after the losses: act {crew.Act}; {server.Crew.Describe(crew)}");
            var left = 0;
            for (var i = 0; i < crew.MemberCount; i++)
                if (server.Crew.State[crew.Members[i]] is not (CrewState.Dead or CrewState.Surrendered))
                    left++;
            Check((crew.Act is BoardAct.Surrender or BoardAct.Abandon or BoardAct.Scuttle || left == 0) && fightOn == 0, $"a beaten crew gives up by its faction's rules or fights to the last ({crew.Act}, {left} still up), nobody fighting on after a surrender ({fightOn})");
        }
        Say(fails.Count == 0 ? "all did what they should" : $"{fails.Count} failed: {string.Join("; ", fails)}");
        return fails.Count;
    }
}
