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

    /// <summary>An offline game with bots, played until the map vote opens (null after a failure).</summary>
    private static Game? VoteGame(out double now)
    {
        now = 0;
        var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: Game.ClassicPlayers - 1, botSkill: BotDifficulty.Normal, mapId: Maps.All[0].Id);
        for (var i = 0; i < World.TickRate; i++) // a new game says Playing until its first server tick starts the countdown
            now = VoteStep(game, now);
        for (var i = 0; i < 120 * World.TickRate && game.Phase != MatchPhase.Playing; i++)
            now = VoteStep(game, now);
        if (game.Phase != MatchPhase.Playing)
            Fail($"the match didn't start (phase {game.Phase})");
        game.DebugEndMatch(now);
        now = VoteStep(game, now);
        if (!game.VoteOpen || game.VoteOptions.Length != Game.VoteChoices)
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
        var picked = game.Players.Values.Where(p => p.Bot != null).Take(count).ToArray();
        foreach (var p in picked)
            p.Bot = null;
        return picked.Select(p => p.Id).ToArray();
    }
}
