using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check --crew-relations [--no-net]</c> (docs/sandbox/design/faction-controllers.md phase 1): relations that change through play, tried
/// by hand. Three powers from a throwaway Crew/Powers folder (two Militia powers at war, a Raider power), through the relations engine
/// (Crew/CrewRelations.cs) on simulated time: a witnessed attack shifting a third party (and an unwitnessed one not), help in a fight and its cap,
/// decay towards rest (a war held at its line), an alliance forming over a common enemy, the allies joining each other's and a friendly
/// player's fights, the alliance broken by an attack and the word spreading, a ceasefire after losses and its running out, and the whole
/// surviving a save. Then a host and a joined client on localhost: the client's Faction window data (grid, pacts, news) matches the host's.
/// A line a step; exit 0 when each did what it should. Allocates (a tool).
/// </summary>
public static class CrewRelationsCheck
{
    private sealed class FakeSight : IPowerSight
    {
        public readonly bool[] Sees = new bool[FactionTable.MaxStory + 1];
        bool IPowerSight.Sees(byte power, Vector3 at) => Sees[power];
    }

    public static int Run(string[] args)
    {
        var fails = new List<string>();
        void Say(string s) => Console.WriteLine($"[crew-relations] {s}");
        void Check(bool ok, string what)
        {
            Say($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                fails.Add(what);
        }
        var dir = Path.Combine(Path.GetTempPath(), $"CallOfSlop-crew-relations-{Environment.ProcessId}", "Powers");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "NorthGuard.json"), """
            { "format": "call-of-slop-npc-power/1", "name": "North Guard", "kind": "Militia", "tag": "NG", "colour": "#4080FF",
              "parameters": { "loyalty": 0.8, "forgiveness": 0.5, "pride": 0.5 },
              "relations": { "South Guard": -70, "Red Hand": 20, "players": 0 } }
            """);
        File.WriteAllText(Path.Combine(dir, "SouthGuard.json"), """
            { "format": "call-of-slop-npc-power/1", "name": "South Guard", "kind": "Militia", "tag": "SG",
              "parameters": { "loyalty": 0.8 },
              "relations": { "North Guard": -70, "Red Hand": -60 } }
            """);
        File.WriteAllText(Path.Combine(dir, "RedHand.json"), """
            { "format": "call-of-slop-npc-power/1", "name": "Red Hand", "kind": "Raiders", "tag": "RH",
              "parameters": { "aggression": 0.6, "greed": 0.9, "loyalty": 0.6, "forgiveness": 0.3 },
              "relations": { "North Guard": 20, "South Guard": -60 } }
            """);
        CrewPowers.Use(dir);
        byte north = CrewPowers.Find("North Guard"), south = CrewPowers.Find("South Guard"), red = CrewPowers.Find("Red Hand");
        Check(CrewPowers.Count == 3 && north > 0 && south > 0 && red > 0 && CrewPowers.KindOf(north)!.Name == "Militia", $"three powers read, two of kind Militia ({CrewPowers.Count})");
        var sight = new FakeSight();
        var rel = new PowerRelations { Sight = sight, Table = new FactionTable() };
        rel.Reset();
        var T = World.TickRate;
        void Run(int seconds)
        {
            for (var k = 0; k < seconds * T; k++)
                rel.Step();
        }
        Say("start:\n" + rel.GridText());

        // 1. Two powers of one kind at war.
        Check(rel.BandOf(north, south) == RelBand.War && rel.Fights(north, south) && rel.Fights(south, north), "North Guard and South Guard (both Militia) start at war and fight on sight");

        // 2. Word gets around: Red Hand hits a South Guard ship where North Guard's crew sees it; North (South's enemy) warms to Red Hand.
        var nr0 = rel.Get(north, red);
        sight.Sees[north] = true;
        rel.Post(RelEvent.HitBody, south, red, 255, Vector3.Zero);
        var nr1 = rel.Get(north, red);
        Check(nr1 > nr0 && rel.Get(south, red) < -60, $"a witnessed attack: South sees Red Hand at {rel.Get(south, red)}, North (who saw it) {nr0} -> {nr1}");
        sight.Sees[north] = false;
        rel.Post(RelEvent.HitBody, south, red, 255, Vector3.Zero);
        Check(rel.Get(north, red) == nr1, $"an unwitnessed attack leaves North at {rel.Get(north, red)}");

        // 3. Help in a fight: Red Hand hurts someone fighting North's crew; North thanks it, once a minute, at most 10 a fight.
        var before = rel.Get(north, red);
        rel.Help(north, red, 255, Vector3.Zero);
        rel.Help(north, red, 255, Vector3.Zero); // (the same minute: nothing)
        var once = rel.Get(north, red) - before;
        for (var k = 0; k < 8; k++)
        {
            Run(61);
            rel.Help(north, red, 255, Vector3.Zero);
        }
        var total = rel.Get(north, red) - before;
        var each = rel.Amount(RelEvent.Help, CrewPowers.Of(north)!);
        Check(once == each && total <= rel.Rules.HelpMax, $"help in a fight: +{once} the first minute (twice asked), +{total} over nine minutes of one fight (most {rel.Rules.HelpMax}, decay aside)");

        // 4. Decay: a player's grudge fades towards rest; a war holds at its line.
        rel.Table.Of[7] = 0;
        for (var k = 0; k < 6; k++)
            rel.Post(RelEvent.HitBody, north, 0, 7, Vector3.Zero);
        var grudge = rel.Peek(north, 7);
        rel.Post(RelEvent.KillCrew, north, south, 255, Vector3.Zero);
        var war0 = rel.Get(north, south);
        Run(20 * 60);
        var grudge1 = rel.Peek(north, 7);
        Check(grudge < 0 && grudge1 > grudge && grudge1 <= 0, $"decay: player 7's grudge with North {grudge} -> {grudge1} after 20 minutes (forgiveness 0.5: about 10 points)");
        Check(rel.Get(north, south) <= rel.Rules.WarLine && rel.BandOf(north, south) == RelBand.War, $"a war doesn't decay away: North sees South {war0} -> {rel.Get(north, south)}");

        // 5. An alliance: North and Red Hand friends (scripted), with a common enemy at war (South).
        rel.Move(north, red, 70 - rel.Get(north, red));
        rel.Move(red, north, 70 - rel.Get(red, north));
        Run(12);
        Check(rel.PactOf(north, red) == PactKind.Alliance, $"North Guard and Red Hand, friends with South as their common enemy, are allies ({rel.PactOf(north, red)})");
        Check(rel.Helps(red, north) && rel.ShipFights(red, south, north, 255), "Red Hand's crews join a fight against South Guard's crew fighting North's");
        rel.MovePlayer(red, 7, 80 - rel.Peek(red, 7));
        rel.Move(red, south, 30 - rel.Get(red, south));
        Check(rel.ShipFights(red, south, 0, 7) && !rel.ShipFights(red, south, 0, 255), "... even at peace with South (+30) it fights South's crew that fights its friend, and leaves South alone otherwise");
        rel.Move(red, south, -60 - rel.Get(red, south));

        // 6. The alliance broken by an attack; South's crew saw it.
        sight.Sees[south] = true;
        var sn0 = rel.Get(south, north);
        rel.Post(RelEvent.HitBody, red, north, 255, Vector3.Zero);
        sight.Sees[south] = false;
        Check(rel.PactOf(north, red) == PactKind.None && rel.Get(red, north) < 40, $"North hits Red Hand: the alliance is broken, Red Hand sees North at {rel.Get(red, north)}");
        Check(rel.Get(south, north) < sn0, $"the word spreads: South, who saw it, {sn0} -> {rel.Get(south, north)}");

        // 7. A ceasefire after losses, and its end.
        for (var k = 0; k < 12; k++)
            rel.Post(RelEvent.KillCrew, north, south, 255, Vector3.Zero);
        Run(12);
        Check(rel.PactOf(north, south) == PactKind.Ceasefire && !rel.Fights(north, south) && rel.Get(north, south) >= rel.Rules.CeasefireRelation,
            $"North, losing, signs a ceasefire with South ({rel.PactOf(north, south)}; North sees South at {rel.Get(north, south)}, South North at {rel.Get(south, north)})");

        // 8. A save and a load.
        var bytes = rel.SaveBytes();
        var back = new PowerRelations { Sight = sight, Table = new FactionTable() };
        back.Reset();
        back.Load(bytes);
        var same = back.Rel.AsSpan().SequenceEqual(rel.Rel) && back.PactOf(north, south) == PactKind.Ceasefire && back.NewsTotal == Math.Min(rel.NewsTotal, PowerRelations.MaxNews);
        Check(same, $"the grid, pacts and news survive a save ({bytes.Length} bytes)");
        var sw = Stopwatch.StartNew();
        var bytes0 = GC.GetAllocatedBytesForCurrentThread();
        var news0 = rel.NewsTotal;
        Run(rel.Rules.CeasefireMinutes * 60 + 10);
        var ticks = (rel.Rules.CeasefireMinutes * 60 + 10) * T;
        Say($"{ticks} ticks of the powers thinking: {sw.Elapsed.TotalMilliseconds * 1000 / ticks:0.00} us a tick, {GC.GetAllocatedBytesForCurrentThread() - bytes0} bytes allocated ({rel.NewsTotal - news0} news lines)");
        Check(rel.PactOf(north, south) == PactKind.None, "the ceasefire runs out");
        Say("end:\n" + rel.GridText());
        Say("news: " + string.Join(" | ", Enumerable.Range(Math.Max(0, rel.NewsTotal - PowerRelations.MaxNews), Math.Min(rel.NewsTotal, PowerRelations.MaxNews)).Select(rel.NewsLine)));

        // 9. A host and a joined client: the client's window data matches the host's.
        if (!args.Any(a => a.Equals("--no-net", StringComparison.OrdinalIgnoreCase)))
            Net(Say, Check);
        Say(fails.Count == 0 ? "all steps did what they should" : $"{fails.Count} failed: {string.Join("; ", fails)}");
        try
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, true);
        }
        catch (IOException)
        {
        }
        return fails.Count == 0 ? 0 : 1;
    }

    private static void Net(Action<string> say, Action<bool, string> check)
    {
        SavePaths.UseFolder(Path.Combine(Path.GetTempPath(), $"CallOfSlop-crew-relations-{Environment.ProcessId}", "saves"), "crew relations check");
        StarSystems.Reload();
        if (Maps.Find(Game.StoryHomeMap) is not { } home)
        {
            check(false, $"the {Game.StoryHomeMap} map is missing");
            return;
        }
        const ushort port = 26597;
        var clock = Stopwatch.StartNew();
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: home.Id)
        {
            MapVoteEnabled = false, LocalWorldKey = 11,
        };
        server.WorldAttach(Game.WorldNew("Relations Check", home, survival: true));
        server.Mode = GameMode.Sandbox;
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Ann", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { LocalWorldKey = 22 };
        double last = 0;
        var poked = false;
        var seen = false;
        byte north = CrewPowers.Find("North Guard"), red = CrewPowers.Find("Red Hand");
        while (clock.Elapsed.TotalSeconds < 90)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Min(t - last, 0.1);
            last = t;
            server.Update(t, dt);
            client.Update(t, dt);
            if (!poked && client.LocalId != byte.MaxValue && server.FactionTable.PowerCount == CrewPowers.Count && t > 5)
            {
                poked = true;
                World.Enter(server.Place);
                server.Relations.Move(north, red, -100); // (North declares war on Red Hand: a news line and a new grid)
                say($"host: North Guard now sees Red Hand at {server.Relations.Get(north, red)}");
            }
            if (poked && client.FactionTable.PowerCount == CrewPowers.Count
                && client.FactionTable.PowerRel[(north - 1) * FactionTable.MaxStory + red - 1] == server.FactionTable.PowerRel[(north - 1) * FactionTable.MaxStory + red - 1]
                && client.PowerNews.Exists(l => l.Contains("declares war on Red Hand")))
            {
                seen = true;
                break;
            }
            Thread.Sleep(2);
        }
        var st = server.FactionTable;
        var ct = client.FactionTable;
        var gridSame = ct.PowerCount == st.PowerCount && st.PowerRel.AsSpan().SequenceEqual(ct.PowerRel) && ct.PactCount == st.PactCount;
        check(seen && gridSame, $"a joined client sees the host's grid ({ct.PowerCount} powers, {ct.PactCount} pacts) and news ({client.PowerNews.Count} lines: \"{client.PowerNews.LastOrDefault()}\") after {clock.Elapsed.TotalSeconds:0} s");
        check(ct.StoryNames.Length == CrewPowers.Count && ct.StoryNames.Contains("Red Hand"), $"the client's NPC rows name the powers: {string.Join(", ", ct.StoryNames)}");
    }
}
