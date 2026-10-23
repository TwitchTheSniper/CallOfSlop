namespace CallOfSlop;

/// <summary>
/// Insane squads: who is in which squad and with what role. In team modes the Insane bots of each team are split into
/// squads of 3-4 (2 when there are only two; one bot alone stays solo). Membership is by bot id, so it's stable and
/// deterministic, and it's only redone when the set of Insane bots on a team changes (a bot joins, leaves or switches
/// sides). Dead members stay in their squad and rejoin it when they respawn. Free-for-all and co-op modes have no squads:
/// Insane bots there are just Hard+ individuals (Zombies co-op bots cooperate in ZombieBots.cs).
/// </summary>
public sealed partial class Game
{
    /// <summary>Most members in one squad.</summary>
    public const int SquadMax = 4;

    private readonly List<BotSquad> _squads = new();
    private readonly List<Player> _squadScratch = new(Map.HardMaxPlayers);
    private ulong _squadRosterKey;
    private double _nextSquadRoster;
    private int _squadPlanCursor;

    /// <summary>The current Insane squads (server only).</summary>
    public IReadOnlyList<BotSquad> Squads => _squads;

    /// <summary>Insane squad statistics for this session.</summary>
    public readonly SquadStats SquadStats = new();

    /// <summary>Self-test: Insane bots play as Hard+ individuals without squads (to measure what the squads add).</summary>
    public bool SquadsDisabled;

    /// <summary>Once a second: forms or re-forms the squads when the Insane bots on a team changed.</summary>
    private void SquadRoster(double now)
    {
        if (now < _nextSquadRoster)
            return;
        _nextSquadRoster = now + 1;
        var teamPlay = IsTeamMode && !Module.CoOp && !SquadsDisabled && !GravityFrames.Curved; // (squad tactics plan on flat ground: not on a planet)
        // A key over (team, id) of every Insane bot: unchanged key, unchanged squads.
        var key = teamPlay ? 1469598103934665603UL : 0;
        if (teamPlay)
            foreach (var p in Players.Values)
                if (p.Bot is { Insane: true, Dummy: false } && p.Team < 2)
                {
                    key ^= (ulong)(p.Id * 2 + p.Team + 1);
                    key *= 1099511628211UL;
                }
        if (key == _squadRosterKey && (_squads.Count > 0 || key == 0))
            return;
        _squadRosterKey = key;
        foreach (var s in _squads)
            foreach (var m in s.Members)
                LeaveSquad(m);
        _squads.Clear();
        if (!teamPlay)
            return;
        for (byte team = 0; team < 2; team++)
        {
            _squadScratch.Clear();
            foreach (var p in Players.Values)
                if (p.Bot is { Insane: true, Dummy: false } && p.Team == team)
                    _squadScratch.Add(p);
            _squadScratch.Sort((a, b) => a.Id.CompareTo(b.Id));
            var n = _squadScratch.Count;
            if (n < 2)
                continue;
            var count = (n + SquadMax - 1) / SquadMax;
            var at = 0;
            for (var k = 0; k < count; k++)
            {
                var size = n / count + (k < n % count ? 1 : 0);
                var s = new BotSquad(_squads.Count, team) { NextPlan = now + 0.1 * k };
                for (var i = 0; i < size; i++)
                    s.Members.Add(_squadScratch[at++]);
                AssignRoles(s);
                _squads.Add(s);
            }
        }
    }

    private static void LeaveSquad(Player m)
    {
        if (m.Bot is not { } b)
            return;
        b.Squad = null;
        b.Role = SquadRole.None;
        b.Order = default;
        b.HoldFire = false;
        b.SquadSlot = byte.MaxValue;
        b.FlankWidth = 0;
        if (b.Mode == BotMode.Squad)
            b.Mode = BotMode.Roam;
    }

    // How well each personality fits each role (Rusher, Marksman, Flanker, Anchor, Tactician, Chaotic).
    private static readonly float[] LeadFit = [1f, 1.5f, 1f, 2f, 3f, 0.5f];
    private static readonly float[] FlankFit = [2.5f, 0f, 3f, 0.2f, 1f, 1.5f];
    private static readonly float[] SuppressFit = [0.5f, 3f, 0.3f, 3f, 1.5f, 1f];

    /// <summary>
    /// Roles from personalities: the best leader (tacticians first), then the best flanker, then the best base of fire
    /// (marksmen, anchors); a fourth member supports. Two members: a leader (who also suppresses) and a flanker.
    /// </summary>
    private static void AssignRoles(BotSquad s)
    {
        foreach (var m in s.Members)
            m.Bot!.Role = SquadRole.None;
        Player? Best(float[] fit)
        {
            Player? best = null;
            var bestScore = float.MinValue;
            foreach (var m in s.Members)
            {
                var mb = m.Bot!;
                if (mb.Role != SquadRole.None)
                    continue;
                var score = fit[(int)mb.Personality];
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            return best;
        }
        var leader = Best(LeadFit)!;
        leader.Bot!.Role = SquadRole.Leader;
        s.Leader = leader;
        if (Best(FlankFit) is { } flanker)
            flanker.Bot!.Role = SquadRole.Flanker;
        if (Best(SuppressFit) is { } suppressor)
            suppressor.Bot!.Role = SquadRole.Suppressor;
        foreach (var m in s.Members)
        {
            var mb = m.Bot!;
            if (mb.Role == SquadRole.None)
                mb.Role = SquadRole.Support;
        }
        for (var i = 0; i < s.Members.Count; i++)
        {
            var b = s.Members[i].Bot!;
            b.Squad = s;
            b.SquadSlot = (byte)i;
            b.Order = default;
        }
    }

    /// <summary>A support member who likes to flank makes the assault a pincer (two flankers, opposite sides).</summary>
    private static bool PincerSupport(BotBrain b) => b.Role == SquadRole.Support && b.Personality is BotPersonality.Flanker or BotPersonality.Rusher;
}
