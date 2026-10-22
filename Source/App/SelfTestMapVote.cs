namespace CallOfSlop;

/// <summary>Self-test section "Map vote: humans only" (see SelfTest.cs): bots never vote, only humans decide.</summary>
public static partial class SelfTest
{
    private static void CheckMapVoteHumansOnly()
    {
        // ---- The rule itself: most votes, ties random among the tied, no votes random among all ----------------------
        if (Game.PickVoteWinner([], new Random(1)) != -1)
            Fail("an empty ballot should have no winner");
        var seen = new HashSet<int>();
        var tieSeen = new HashSet<int>();
        for (var seed = 0; seed < 200; seed++)
        {
            if (Game.PickVoteWinner([1, 3, 0], new Random(seed)) != 1)
                Fail("the option with the most votes didn't win");
            seen.Add(Game.PickVoteWinner([0, 0, 0], new Random(seed)));
            tieSeen.Add(Game.PickVoteWinner([2, 2, 0], new Random(seed)));
            if (Game.PickVoteWinner([0, 0, 0], new Random(seed)) != Game.PickVoteWinner([0, 0, 0], new Random(seed))
                || Game.PickVoteWinner([2, 2, 0], new Random(seed)) != Game.PickVoteWinner([2, 2, 0], new Random(seed)))
                Fail($"the same seed ({seed}) gave different winners");
        }
        Line($"no votes picked options {{{string.Join(",", seen.Order())}}}; a 2-2-0 tie picked {{{string.Join(",", tieSeen.Order())}}}");
        if (seen.Count != 3)
            Fail("with no votes, every option should be able to win");
        if (!tieSeen.SetEquals([0, 1]))
            Fail("a tie should pick only (and both) of the tied options");

        // ---- Nobody human votes (the solo host stays quiet): bots cast nothing, the vote still resolves -------------
        {
            using var game = VoteGame(out var now);
            if (game == null)
                return;
            var opened = now;
            var ballot = game.VoteOptions.ToArray();
            var maxVotes = 0;
            foreach (var bot in game.Players.Values.Where(p => p.Bot != null).Take(3))
                game.DebugVoteFor(bot.Id, 2); // a bot's vote is refused outright
            while (game.MatchNumber == 1 && now - opened < Game.IntermissionSeconds + 2)
            {
                maxVotes = Math.Max(maxVotes, game.VoteTally.Sum(t => t));
                now = VoteStep(game, now);
            }
            Line($"no human votes: ballot {string.Join(", ", ballot.Select(id => Maps.Get(id).Name))}, most votes seen {maxVotes}, "
                + $"next map {World.Map.Name} after {now - opened:0.0} s");
            if (maxVotes != 0)
                Fail($"bots voted ({maxVotes} vote(s) in the tally)");
            if (game.MatchNumber != 2)
                Fail("the vote didn't resolve without human votes");
            else if (!ballot.Contains(World.Map.Id))
                Fail("with no votes, the next map wasn't one of the options");
            if (now - opened < Game.IntermissionSeconds - 0.5)
                Fail("the vote ended early although no human voted");
        }

        // ---- Three humans: the timer only shortens once all of them voted, and their majority decides ---------------
        {
            using var game = VoteGame(out var now);
            if (game == null)
                return;
            var ballot = game.VoteOptions.ToArray();
            var humans = MakeHumans(game, 2);
            game.CastVote(0);
            game.DebugVoteFor(humans[0], 2);
            now = VoteStep(game, now);
            var earlyBefore = game.AllHumansVoted || game.TimeLeft < Game.IntermissionSeconds - 2;
            game.DebugVoteFor(humans[1], 2);
            now = VoteStep(game, now);
            now = VoteStep(game, now);
            var tally = game.VoteTally.ToArray();
            var left = game.TimeLeft;
            for (var i = 0; i < 6 * World.TickRate && game.MatchNumber == 1; i++)
                now = VoteStep(game, now);
            Line($"3 humans: tally {string.Join("/", tally)}, {left:0.0} s left once all voted, next map {World.Map.Name}");
            if (earlyBefore)
                Fail("the vote ended early before every human had voted");
            if (tally.Sum(t => t) != 3 || tally[0] != 1 || tally[2] != 2)
                Fail($"expected a 1/0/2 tally of human votes, got {string.Join("/", tally)}");
            if (left > Game.VoteEarlyEndSeconds + 0.05)
                Fail($"every human voted but {left:0.0} s were still left (want <= {Game.VoteEarlyEndSeconds})");
            if (game.MatchNumber != 2 || World.Map.Id != ballot[2])
                Fail($"the humans' majority ({Maps.Get(ballot[2]).Name}) didn't win: next map {World.Map.Name}");
        }

        // ---- Two humans tie: one of the two tied maps wins, never the third -----------------------------------------
        {
            using var game = VoteGame(out var now);
            if (game == null)
                return;
            var ballot = game.VoteOptions.ToArray();
            var humans = MakeHumans(game, 1);
            game.CastVote(0);
            game.DebugVoteFor(humans[0], 1);
            for (var i = 0; i < 6 * World.TickRate && game.MatchNumber == 1; i++)
                now = VoteStep(game, now);
            Line($"2 humans tied 1-1: next map {World.Map.Name}");
            if (game.MatchNumber != 2)
                Fail("a tie between two humans who both voted didn't end the vote early");
            else if (World.Map.Id != ballot[0] && World.Map.Id != ballot[1])
                Fail($"a 1-1 tie between {Maps.Get(ballot[0]).Name} and {Maps.Get(ballot[1]).Name} picked {World.Map.Name}");
        }
    }

    /// <summary>
    /// Self-test section "Map vote: player fit": the next map always takes every human here (bots don't count),
    /// with fewer options when few maps fit, a fallback when none does, and a refit when someone joins mid-vote.
    /// </summary>
    private static void CheckMapVotePlayerFit()
    {
        static int Cap(Map m) => Math.Min(m.MaxPlayers, Game.MaxPlayers); // the free-for-all limit
        var largest = Maps.All.MaxBy(m => Cap(m))!;

        // ---- The rule on the real maps, for many player counts ---------------------------------------------------
        {
            var played = Maps.All.FirstOrDefault(m => m.Pack is { InTrueRandom: true }) ?? Maps.All[0];
            var scope = Game.TrueRandomVotePool(played.Name, Maps.Packs, Maps.All);
            var counts = new List<string>();
            foreach (var n in new[] { 1, 2, 5, 8, 9, 11, 12, 16, 17, 20, 24, 25, 31, 32, 40 })
            {
                var c = Game.VoteCandidates(scope, played, n, Cap, null, out var fb);
                var ballot = Game.PickBallot(c, new Random(n));
                var again = Game.PickBallot(c, new Random(n));
                counts.Add($"{n}:{c.Count}");
                if (!ballot.SequenceEqual(again))
                    Fail($"{n} humans: the same seed gave different ballots");
                if (ballot.Count != Math.Min(Game.VoteChoices, c.Count) || ballot.Distinct().Count() != ballot.Count || ballot.Any(m => !c.Contains(m)))
                    Fail($"{n} humans: ballot of {ballot.Count} from {c.Count} candidates is wrong");
                if (scope.Any(m => Cap(m) >= n))
                {
                    if (fb || c.Any(m => Cap(m) < n))
                        Fail($"{n} humans: a candidate takes fewer ({string.Join(", ", c.Where(m => Cap(m) < n).Select(m => m.Name))})");
                }
                else if (Cap(played) >= n)
                {
                    if (fb || c.Count != 1 || c[0] != played)
                        Fail($"{n} humans: only the map just played takes them, but it wasn't the one candidate");
                }
                else if (!fb || c.Count == 0 || c.Any(m => Cap(m) != scope.Max(m => Cap(m))))
                    Fail($"{n} humans: no map takes them, but the fallback isn't the largest maps");
            }
            Line($"True Random after {played.Name}, humans:candidates {string.Join(" ", counts)}");

            // "Random" (the lobby's first map) keeps to maps that take the players too.
            var s = new Settings { MapRandom = true, MapPack = MapPack.TrueRandomKey };
            var rng = new Random(7);
            foreach (var n in new[] { 4, 12, 20, 32 })
                if (Enumerable.Range(0, 40).Select(_ => s.PickMap(Maps.Packs, Maps.All, rng, n)).Any(m => m.MaxPlayers < n && scope.Concat([played]).Any(x => x.MaxPlayers >= n)))
                    Fail($"random from True Random picked a map too small for {n} players");
        }

        // ---- Made-up limits: fewer options, the map just played, the largest, the mode filter ---------------------
        if (Maps.All.Length >= 7)
        {
            var maps = Maps.All.Take(7).ToArray();
            var fake = new Dictionary<Map, int> { [maps[0]] = 4, [maps[1]] = 6, [maps[2]] = 10, [maps[3]] = 16, [maps[4]] = 24, [maps[5]] = 24, [maps[6]] = 12 };
            int Limit(Map m) => fake[m];
            var scope = maps[..6].ToList();
            var current = maps[6];
            var cases = new (int Humans, Map Current, Func<Map, bool>? Mode, Map[] Expect, bool Fallback)[]
            {
                (8, current, null, [maps[2], maps[3], maps[4], maps[5]], false),
                (14, current, null, [maps[3], maps[4], maps[5]], false),
                (20, current, null, [maps[4], maps[5]], false),
                (20, current, m => m != maps[4] && m != maps[5], [], true), // the mode plays neither 24: largest it plays (16)
                (26, current, null, [maps[4], maps[5]], true),
                (40, current, null, [maps[4], maps[5]], true),
            };
            foreach (var (humans, cur, mode, expect, fallback) in cases)
            {
                var got = Game.VoteCandidates(scope, cur, humans, Limit, mode, out var fb);
                var want = expect.Length > 0 ? expect : [maps[3]];
                if (fb != fallback || !got.ToHashSet().SetEquals(want))
                    Fail($"made-up limits, {humans} humans: got {string.Join("/", got.Select(Limit))} (fallback {fb}), want {string.Join("/", want.Select(Limit))} (fallback {fallback})");
            }
            fake[current] = 30; // the map just played takes 26 when nothing else does: play it again
            var replay = Game.VoteCandidates(scope, current, 26, Limit, null, out var replayFb);
            if (replayFb || replay.Count != 1 || replay[0] != current)
                Fail("with only the map just played taking everyone, it wasn't offered again");
            var few = Game.PickBallot(Game.VoteCandidates(scope, current, 20, Limit, null, out _), new Random(1));
            Line($"made-up limits 4/6/10/16/24/24: 8 humans -> 4 candidates, 20 -> {few.Count} options, 26 -> largest or the map just played");
            if (few.Count != 2)
                Fail($"only 2 maps take 20 but the ballot has {few.Count} options");
        }

        // ---- In a game: lots of bots don't count, people do ---------------------------------------------------------
        foreach (var (humans, trueRandom) in new[] { (0, false), (11, false), (Cap(largest) - 1, false), (15, true) })
        {
            using var game = VoteGame(out var now, bots: Game.MaxPlayers - 1, mapId: largest.Id, humans: humans, trueRandom: trueRandom);
            if (game == null)
                return;
            var people = game.HumanCount;
            var players = game.Players.Count;
            var ballot = game.VoteOptions.Select(id => Maps.Get(id)).ToArray();
            Line($"{people} human(s) + {game.BotCount} bots on {largest.Name}{(trueRandom ? " (True Random)" : "")}: ballot "
                + string.Join(", ", ballot.Select(m => $"{m.Name} {game.PlayerLimit(m)}")));
            if (people != humans + 1)
                Fail($"expected {humans + 1} humans, counted {people}");
            if (ballot.Any(m => game.PlayerLimit(m) < people))
                Fail($"{people} humans: the ballot offers a map that takes fewer");
            if (trueRandom && ballot.Any(m => m.Pack is { InTrueRandom: false }))
                Fail("True Random's ballot offered a Dev map");
            var scope = Game.VotePool(largest.Name, largest.Pack, Maps.All);
            if (humans == 0 && scope.Any(m => m.Size == MapSize.Small && game.PlayerLimit(m) < players) && ballot.All(m => game.PlayerLimit(m) >= players))
                Fail($"1 human and {game.BotCount} bots: bots counted (no option smaller than {players} although small maps exist)");
            var opened = now;
            while (game.MatchNumber == 1 && now - opened < Game.IntermissionSeconds + 2)
                now = VoteStep(game, now);
            if (game.MatchNumber != 2 || game.PlayerLimit(World.Map) < people || game.HumanCount != people || game.Players.Count > game.PlayerLimit(World.Map))
                Fail($"{people} humans: next map {World.Map.Name} takes {game.PlayerLimit(World.Map)} ({game.HumanCount} humans, {game.Players.Count} players)");
        }

        // ---- Someone joins during the vote: a too-small option is replaced, the other votes keep counting --------------
        {
            using var game = VoteGame(out var now, bots: Game.MaxPlayers - 1, mapId: largest.Id);
            if (game == null)
                return;
            var before = game.VoteOptions.Select(id => Maps.Get(id)).ToArray();
            var smallest = Enumerable.Range(0, before.Length).MinBy(i => game.PlayerLimit(before[i]));
            var biggest = Enumerable.Range(0, before.Length).MaxBy(i => game.PlayerLimit(before[i]));
            var newCount = game.PlayerLimit(before[smallest]) + 1;
            if (smallest == biggest || newCount > game.PlayerLimit(before[biggest]) || newCount > game.Players.Count)
                Line($"join refit skipped: ballot limits {string.Join("/", before.Select(m => game.PlayerLimit(m)))}");
            else
            {
                game.CastVote(smallest); // the host votes for the small map...
                var voter = MakeHumans(game, 1)[0];
                game.DebugVoteFor(voter, biggest); // ...another person for the big one
                MakeHumans(game, newCount - game.HumanCount); // then more people arrive
                now = VoteStep(game, now);
                var after = game.VoteOptions.Select(id => Maps.Get(id)).ToArray();
                Line($"{newCount} humans now: ballot {string.Join(", ", before.Select(m => m.Name))} -> {string.Join(", ", after.Select(m => m.Name))}, "
                    + $"tally {string.Join("/", game.VoteTally)}, my vote {game.MyVote}");
                if (after.Any(m => game.PlayerLimit(m) < newCount))
                    Fail("after people joined, the ballot still offers a map too small for them");
                if (after.Length == 0 || after.Any(m => m == before[smallest]))
                    Fail("the too-small option wasn't replaced");
                if (Array.IndexOf(after, before[biggest]) is var kept and >= 0)
                {
                    if (game.VoteTally[kept] != 1 || (after.Length == before.Length && kept != biggest))
                        Fail("the vote for the option that still fits didn't keep counting in its slot");
                }
                else
                    Fail("an option that still fits was removed");
                if (game.MyVote != -1 || game.VoteTally.Sum(t => t) != 1)
                    Fail("the vote for the replaced option still counts");
                var opened = now;
                while (game.MatchNumber == 1 && now - opened < Game.IntermissionSeconds + 2)
                    now = VoteStep(game, now);
                if (game.MatchNumber != 2 || game.PlayerLimit(World.Map) < newCount)
                    Fail($"after the refit the next map {World.Map.Name} takes {game.PlayerLimit(World.Map)}, fewer than {newCount}");
            }
        }
        World.SetMap(Maps.All[0]);
    }

    /// <summary>
    /// An offline game with bots, played until the map vote opens (null after a failure). <paramref name="humans"/>
    /// bots are turned into stand-ins for connected people just before the match ends; <paramref name="trueRandom"/>
    /// sets the host's True Random rotation.
    /// </summary>
    private static Game? VoteGame(out double now, int bots = Game.ClassicPlayers - 1, byte? mapId = null, int humans = 0, bool trueRandom = false)
    {
        now = 0;
        var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: bots, botSkill: BotDifficulty.Normal, mapId: mapId ?? Maps.All[0].Id);
        game.TrueRandom = trueRandom;
        for (var i = 0; i < World.TickRate; i++) // a new game says Playing until its first server tick starts the countdown
            now = VoteStep(game, now);
        for (var i = 0; i < 120 * World.TickRate && game.Phase != MatchPhase.Playing; i++)
            now = VoteStep(game, now);
        if (game.Phase != MatchPhase.Playing)
            Fail($"the match didn't start (phase {game.Phase})");
        MakeHumans(game, humans);
        game.DebugEndMatch(now);
        now = VoteStep(game, now);
        if (!game.VoteOpen || game.VoteOptions.Length == 0 || (humans == 0 && game.VoteOptions.Length != Game.VoteChoices))
        {
            Fail($"the map vote didn't open (phase {game.Phase}, {game.VoteOptions.Length} options)");
            game.Dispose();
            return null;
        }
        return game;
    }

    private static double VoteStep(Game game, double now)
    {
        const double dt = 1.0 / World.TickRate;
        game.Update(now + dt, dt);
        return now + dt;
    }

    /// <summary>Turns <paramref name="count"/> bots into stand-ins for connected humans (they stop thinking, and can vote).</summary>
    private static byte[] MakeHumans(Game game, int count)
    {
        var picked = game.Players.Values.Where(p => p.Bot != null).Take(Math.Max(count, 0)).ToArray();
        foreach (var p in picked)
            p.Bot = null;
        return picked.Select(p => p.Id).ToArray();
    }
}
