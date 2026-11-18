using System.Diagnostics;
using System.Numerics;
using CallOfSlop.FactoryKit;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check --crew-mine [--seconds S] [--crew-shell] [--crew-shell-kill] [--crew-far] [--post N]</c> (factory-generator-notes.md "Tool racks and crew miners"): a host and a joined
/// client on localhost on the Proving Ground (creative). A Militia Extraction Post (seed N, with a Shell Control Chair and a Mining Shell standing on its
/// road when <c>--crew-shell</c>) is set down near the map's first ore a tier 2 drill works, as a nobody's, and crewed; the host stands by it, so its
/// crew have bodies. For S seconds (90) the miners' trips are run: each line says what they did. Then: ore tipped into the post's bin, the rack's
/// drills back (or out) on host and client alike, and the client's view of the crew bodies. <c>--crew-far</c>: the host 600 m off, so the miner is a record on timers. Exit 0 when the miners mined and the client agrees.
/// Allocates (a tool); real time, not a long simulation.
/// </summary>
public static class CrewMineCheck
{
    public static int Run(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 90;
        var shell = args.Any(a => a.Equals("--crew-shell", StringComparison.OrdinalIgnoreCase));
        var seed = int.TryParse(Arg("--post"), out var ps) ? ps : 1;
        var kill = args.Any(a => a.Equals("--crew-shell-kill", StringComparison.OrdinalIgnoreCase)); // (the shell shot out mid-trip: the miner stands up and goes on with a drill)
        shell |= kill;
        var far = args.Any(a => a.Equals("--crew-far", StringComparison.OrdinalIgnoreCase)); // (the host 600 m off: the crew are records, the trips timers)
        var fails = new List<string>();
        void Say(string line) => Console.WriteLine($"[crew-mine] {line}");
        void Check(bool ok, string what)
        {
            Say($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                fails.Add(what);
        }
        SavePaths.UseFolder(Path.Combine(Path.GetTempPath(), $"CallOfSlop-crew-mine-{Environment.ProcessId}"), "crew mine check");
        StarSystems.Reload();
        if (Maps.Find("Proving Ground") is not { } map)
        {
            Say("the Proving Ground map is missing");
            return 1;
        }
        const ushort port = 26603;
        var clock = Stopwatch.StartNew();
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false, LocalWorldKey = 11,
        };
        server.Mode = GameMode.Sandbox;
        server.WorldAttach(Game.WorldNew("Crew Mine Check", map, survival: false));
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Ann", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { LocalWorldKey = 22 };
        var now = 0.0; // (game time, stepped as fast as the box runs it: the loopback gets a millisecond every few ticks)
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
        server.Local!.ImmuneUntil = now + 1e6;
        server.RequestRespawn();
        World.Enter(client.Place);
        client.RequestRespawn();
        if (!WaitFor(() => server.Local is { Alive: true } && server.Constructs.Resources != null, 20))
        {
            Check(false, "the host's player spawned and its world has its resources");
            return 1;
        }
        World.Enter(server.Place);
        var field = server.Constructs.Resources!;
        // The map's first ore a tier 2 drill works (not deep), nearest the middle.
        // With --crew-far, the ore furthest from the map's north-west corner (where the people stand), so the crew are records.
        Vector3? ore = null;
        var corner = new Vector2(-map.HalfX * 0.95f, map.HalfZ * 0.95f);
        var bestFar = 0f;
        for (var r = 0; r < 400 && (ore == null || far); r += 6)
            for (var a = 0; a < 32 && (ore == null || far); a++)
            {
                float x = r * MathF.Cos(a * MathF.Tau / 32), z = r * MathF.Sin(a * MathF.Tau / 32);
                if (MathF.Abs(x) > map.HalfX - 40 || MathF.Abs(z) > map.HalfZ - 40)
                    continue;
                if (field.TappableAt(x, z) is { Deep: false, MinTier: <= Game.DrillTier } && field.TappableAt(x + 12, z) is { Deep: false } && field.TappableAt(x - 12, z) is { Deep: false }
                    && (!far || Vector2.Distance(new Vector2(x, z), corner) > bestFar))
                {
                    ore = new Vector3(x, field.Ground(x, z), z);
                    bestFar = Vector2.Distance(new Vector2(x, z), corner);
                }
            }
        if (ore is not { } at)
        {
            Check(false, "the Proving Ground has ore a tier 2 drill works within 400 m");
            return 1;
        }
        Say($"ore: {field.TappableAt(at.X, at.Z)!.Name} at {at.X:0}, {at.Z:0}");
        // The post with its back edge 6 m short of the ore (its plan's z runs to the back as negative; it faces the road at +z).
        var script = FactoryScript.Build(new FactoryBrief { Function = "extraction", Style = "Militia", Seed = seed }, shellChair: shell);
        var bp = ConstructBuild.ParseBlueprint(script.Text(), FactoryScript.FileName(script.Brief));
        var ground = ConstructWorld.Ground(World.Query, at.X, at.Z + script.Plan.Depth * 0.5f + 6);
        var post = ConstructBuild.BuildOnGround(server.Constructs, bp, ground, 0, out var why);
        if (post == null)
        {
            Check(false, $"{bp.Name} builds: {why}");
            return 1;
        }
        post.Owner = 255;
        post.Anchored = true;
        post.Rebuild();
        server.Constructs.StructureDirty.Add(post.Id);
        var rack = -1;
        for (var i = 0; i < post.PartCount; i++)
            if (post.DefOf(i).IsToolRack)
                rack = i;
        Check(rack >= 0, $"{bp.Name} ({post.PartCount} parts) has a Drill Rack ({(rack >= 0 ? ToolRacks.OnRack(post.Parts[rack]) : 0)} drills on it)");
        if (shell && ConstructParts.Find("Mining Shell") is { } msDef)
        {
            var standAt = ConstructWorld.Ground(World.Query, post.ToWorld(new Vector3(post.LocalMin.X + 4, 0, post.LocalMax.Z - 2)));
            var ms = ConstructBuild.PlaceNew(server.Constructs, msDef, standAt, 0);
            if (ms != null)
            {
                ms.Owner = 255;
                ConstructConfig.Put(ref ms.Parts[0], msDef, CfgKey.Channel, 1);
                server.Constructs.StructureDirty.Add(ms.Id);
            }
            Say($"a Mining Shell stands on the post's road: {(ms != null ? $"construct {ms.Id}" : "refused")}");
        }
        var stand = far ? new Vector3(corner.X, 0, corner.Y) : post.ToWorld(new Vector3((post.LocalMin.X + post.LocalMax.X) * 0.5f, 0, post.LocalMax.Z + 8));
        foreach (var person in server.Players.Values.Where(p => p.Bot == null).ToList())
            person.Teleport(ConstructWorld.Ground(World.Query, stand.X + person.Id * 2, stand.Z) + Vector3.UnitY); // (the host and the client's player both: a person near makes bodies)
        World.Enter(client.Place);
        if (client.Local is { } ann && server.Players.TryGetValue(ann.Id, out var annHost))
            ann.Teleport(annHost.Position); // (the client moves its own player: the host's word alone is undone by its next input)
        World.Enter(server.Place);
        Say($"people: {string.Join(", ", server.Players.Values.Where(p => p.Bot == null).Select(p => $"{p.Name} {(p.Alive ? "alive" : "dead")} {Vector3.Distance(p.Position, post.WorldCom):0} m off"))}");
        if (rack >= 0)
            ConstructConfig.Put(ref post.Parts[rack], post.DefOf(rack), CfgKey.ToolsOut, 4); // (hook 3 marked out with no trip, as a world saved with a miner out loads: it must come back)
        server.CrewFactionVehicle(post);
        var crew = server.Crew.ShipOf(post.Id);
        Check(crew != null, $"the post is crewed: {(crew != null ? server.Crew.Describe(crew) : "no crew")}");
        if (crew == null)
            return 1;
        var bin = -1;
        for (var i = 0; i < post.PartCount; i++)
            if (post.DefOf(i).IsVolumeStore)
                bin = i;
        var binStart = bin >= 0 && server.Constructs.StoreOf(post, bin) is { } b0 ? b0.Amount : 0;
        var until = now + seconds;
        var nextSay = 0.0;
        var outSeen = false;
        var clientOutSeen = false;
        var clientTool = false;
        var staleBack = false;
        var killed = false;
        double killedAt = 0;
        var afterKill = false; // (a drill trip after the shell was lost, the miner on its feet)
        var hopperAtKill = 0;
        while (now < until)
        {
            Frame();
            var t = now;
            World.Enter(server.Place);
            if (rack >= 0 && ToolRacks.Out(post.Parts[rack]) is var o && (o & ~4) != 0)
                outSeen = true;
            if (rack >= 0 && (ToolRacks.Out(post.Parts[rack]) & 4) == 0)
                staleBack = true;
            World.Enter(client.Place);
            if (client.Constructs.Get(post.Id) is { } cp && rack < cp.PartCount && cp.DefOf(rack).IsToolRack && ToolRacks.Out(cp.Parts[rack]) != 0)
                clientOutSeen = true;
            foreach (var p in client.Players.Values)
                if (p.Id != client.LocalId && client.SandboxHands.Hand(p.Id).Tool == ConstructTool.GroundDrill)
                    clientTool = true;
            if (kill && !killed && t - (until - seconds) > 30)
            {
                World.Enter(server.Place);
                foreach (var p in server.Players.Values)
                    if (p.CrewSlot >= 0 && p.InShell)
                    {
                        hopperAtKill = server.SandboxHands.Hoppers[p.Id].Amount;
                        Say($"the miner's shell shot out with {hopperAtKill} in its hopper: {server.CrewShoot(server.Local!, "Miner")}");
                        killed = true;
                        killedAt = t;
                        break;
                    }
            }
            if (killed && !afterKill && t > killedAt + 1)
            {
                World.Enter(server.Place);
                foreach (var p in server.Players.Values)
                    if (p.CrewSlot >= 0 && !p.InShell && !server.SandboxHands.Hand(p.Id).Seated && rack >= 0 && ToolRacks.Out(post.Parts[rack]) != 0)
                        afterKill = true;
            }
            if (t >= nextSay)
            {
                nextSay = t + 10;
                World.Enter(server.Place);
                Say($"{t - (until - seconds):0} s: {server.MineLine()}; bin {(bin >= 0 && server.Constructs.StoreOf(post, bin) is { } bs ? bs.Amount : 0)}; rack out {(rack >= 0 ? ToolRacks.Out(post.Parts[rack]) : 0)}; people {string.Join("/", server.Players.Values.Where(p => p.Bot == null).Select(p => $"{p.Name} {Vector3.Distance(p.Position, post.WorldCom):0} m"))}; {server.Crew.Describe(crew)}");
            }
        }
        World.Enter(server.Place);
        var tipped = (bin >= 0 && server.Constructs.StoreOf(post, bin) is { } b1 ? b1.Amount : 0) - binStart;
        Say($"end ({seconds} s of game time in {clock.Elapsed.TotalSeconds:0} s): {server.MineLine()}");
        Check(server.MineTrips > 0 && server.MineTipped > 0, $"miners made {server.MineTrips} trip(s), tipping {server.MineTipped} ore ({tipped} more in the {(bin >= 0 ? post.DefOf(bin).Name : "bin")}, the drills' own besides); {server.MinePutThere} put there for want of headway");
        Check(staleBack, $"a hook marked out with no miner's trip (a saved world's) got its drill back ({staleBack})");
        if (kill)
            Check(killed && afterKill, $"the shell shot out mid-trip ({killed}, {hopperAtKill} in its hopper spilt once): the miner stood up and went on with a drill ({afterKill})");
        if (!shell && !far)
            Check(outSeen && clientOutSeen, $"a drill came off the rack on the host ({outSeen}) and the client saw its hook empty ({clientOutSeen})");
        Check(shell || far || clientTool, $"the client saw a crew body with the drill out ({clientTool})");
        World.Enter(client.Place);
        var bodies = client.Players.Values.Count(p => p.Id != client.LocalId && p.Name.StartsWith('['));
        Check(far || bodies > 0, $"the client sees {bodies} crew bodies");
        Say(fails.Count == 0 ? "all did what they should" : $"{fails.Count} failed: {string.Join("; ", fails)}");
        return fails.Count;
    }
}
