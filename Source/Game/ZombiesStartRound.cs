namespace CallOfSlop;

/// <summary>
/// Zombies testing shortcuts (developer request, 2026-10-27: "Getting a high round in zombies takes a lot of time";
/// docs/zombies/ZOMBIES-MODE.md, "Start round, test kit and skipping rounds"):
///   - <see cref="ZStartRound"/>: the lobby's "Start round" (Settings "zombie_start_round", <c>--zombies-round N</c>): the
///     run's first round is N, with the economy of a run that got there: points (<see cref="StartPointsFor"/>), the
///     team's power-up schedule moved on by what was earned (<see cref="AdvancePowerScheduleBy"/>); the round itself
///     brings its zombie count, health, speeds, alive cap, spawn bursts and the box and drop tables' tiers as usual.
///   - <see cref="ZTestKit"/>: the lobby's "Test kit" (maps whose section says <c>"testKit": true</c>: Horde Test):
///     everyone starts with every perk, slots 3-4 and the strongest standard guns and melee build, and the power is on,
///     so you can stand and watch the horde.
///   - <see cref="HostSkipToRound"/>: the host's pause menu (Esc > HOST): the next round now, or any round.
/// Both settings are host settings that travel in the lobby state (protocol 59); round 1 without the kit plays exactly
/// as before.
/// </summary>
public sealed partial class Game
{
    /// <summary>The lobby's choices for <see cref="ZStartRound"/>.</summary>
    public static readonly int[] StartRoundChoices = [1, 5, 10, 20, 30, 40, 50];

    public const int MaxStartRound = 100;

    private int _zStartRound = 1;
    private bool _zTestKit;

    /// <summary>The round a Zombies run starts at (host: set it; clients: the host's, from the lobby state). 1: as ever.</summary>
    public int ZStartRound
    {
        get => _zStartRound;
        set
        {
            var v = Math.Clamp(value, 1, MaxStartRound);
            if (v == _zStartRound)
                return;
            _zStartRound = v;
            if (IsServer)
                MarkLobbyDirty();
        }
    }

    /// <summary>The host's "Test kit" setting (it only acts on a map with <see cref="ZombiesMap.TestKit"/>).</summary>
    public bool ZTestKit
    {
        get => _zTestKit;
        set
        {
            if (value == _zTestKit)
                return;
            _zTestKit = value;
            if (IsServer)
                MarkLobbyDirty();
        }
    }

    /// <summary>The test kit is in force: the setting is on and the map allows it.</summary>
    public bool TestKitOn => _zTestKit && World.Map.Zombies is { TestKit: true };

    /// <summary>Points a newcomer to the run gets (raised for a run started at a later round); 0: the rules' start points.</summary>
    private int _zRunStartPoints;

    private int ZJoinPoints => _zRunStartPoints > 0 ? _zRunStartPoints : ZRules.StartPoints;

    /// <summary>
    /// What one of <paramref name="players"/> has to spend at the start of round <paramref name="round"/>: the start
    /// points plus <see cref="ZombiesRules.StartRoundKeep"/> of their share of what the skipped rounds paid (round to 10).
    /// Solo with the shipped rules: round 5 about 950, 10 about 2750, 20 about 19700, 30 about 67000, 40 about 160000.
    /// </summary>
    public static int StartPointsFor(ZombiesRules r, int round, int players)
    {
        players = Math.Max(1, players);
        var share = TeamEarnedBefore(r, round, players) / players;
        var pts = r.StartPoints + r.StartRoundKeep * share;
        return (int)Math.Min(10_000_000, Math.Round(pts / 10.0) * 10);
    }

    /// <summary>What the whole team earned in rounds 1 to <paramref name="round"/> - 1 (zombies x points per zombie).</summary>
    public static double TeamEarnedBefore(ZombiesRules r, int round, int players)
    {
        double zombies = 0;
        for (var i = 1; i < Math.Min(round, MaxStartRound + 1); i++)
            zombies += r.ZombieCount(i, players);
        return zombies * r.StartRoundPerZombie;
    }

    /// <summary>Server, once when a run starts (after the start gear): a later first round, its points, the power-up schedule; the test kit's power.</summary>
    private void ApplyStartRound()
    {
        if (TestKitOn)
            ZPower = true; // every machine works from the start
        if (_zStartRound <= 1)
            return;
        var players = Math.Max(1, Players.Count - (ZHostSitsOut ? 1 : 0));
        ZRound = _zStartRound - 1; // the first BeginRound is ZRound + 1
        _zRunStartPoints = StartPointsFor(ZRules, _zStartRound, players);
        foreach (var zp in ZPlayers.Values)
            zp.Points = Math.Max(zp.Points, _zRunStartPoints);
        AdvancePowerScheduleBy(TeamEarnedBefore(ZRules, _zStartRound, players));
        Console.WriteLine($"[zombies] the run starts at round {_zStartRound}: {_zRunStartPoints} points each, power-ups due at {PowerDueAt:0} of {PowerEarned:0} earned");
    }

    /// <summary>The power-up schedule as if the team had earned <paramref name="earned"/> points with every due drop taken.</summary>
    private void AdvancePowerScheduleBy(double earned)
    {
        StartPowerSchedule();
        _zPowerEarned += earned;
        for (var i = 0; i < 100_000 && ZRules.PowerFirstAt > 0 && _zPowerEarned >= _zPowerDueAt; i++)
        {
            _zPowerStep = NextPowerStep(ZRules, _zPowerStep);
            _zPowerDueAt += _zPowerStep;
        }
    }

    /// <summary>The test kit's guns: the four strongest standard guns from the box (Wonder one-offs left out), then the best melee build.</summary>
    private static StandardBuild[]? _testKitGuns;

    private static StandardBuild? _testKitMelee;

    private static StandardBuild[] TestKitGuns => _testKitGuns ??= StandardBuilds.From("box").Where(b => !b.IsMelee && b.Tier != Rarity.Wonder)
        .OrderByDescending(b => b.Tier).ThenBy(b => b.Name, StringComparer.Ordinal).Take(4).ToArray();

    private static StandardBuild? TestKitMelee => _testKitMelee ??= StandardBuilds.All.Where(b => b.IsMelee && b.Tier != Rarity.Wonder)
        .OrderByDescending(b => b.Tier).ThenBy(b => b.Name, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Server, from the start gear: every perk, slots 3-4, the test kit's guns and melee build.</summary>
    private void GiveTestKit(Player p)
    {
        var zp = ZP(p);
        var perks = 0;
        for (var k = 0; k < ZRules.Perks.Count && k < 16; k++)
            perks |= 1 << k;
        zp.Perks = (ushort)perks;
        zp.PlateReadyAt = _zNow;
        ServerBuySlots(p);
        var guns = TestKitGuns;
        for (var i = 0; i < guns.Length; i++)
            GiveBuild(p, i, guns[i].Build(), full: true);
        if (TestKitMelee is { } melee)
            GiveBuild(p, 4, melee.Build(), full: true);
        if (p.Id == LocalId)
            LoadoutNotice = $"Test kit: every perk, 4 slots ({string.Join(", ", guns.Select(g => g.Name))}) and the power on.";
    }

    /// <summary>
    /// Host (server), during a Zombies run: start round <paramref name="round"/> now. The zombies up and queued go (no
    /// points, no drops), a break or a countdown is cut short, and anyone below the round's points is raised to them.
    /// </summary>
    public bool HostSkipToRound(int round, double now)
    {
        if (!IsServer || ZMap == null || Phase != MatchPhase.Playing || _zRunMatch != MatchNumber)
            return false;
        round = Math.Clamp(round, 1, 1000);
        ClearHorde();
        _zToSpawn = 0;
        var players = Math.Max(1, Players.Count - (ZHostSitsOut ? 1 : 0));
        if (round > ZRound + 1)
        {
            var pts = StartPointsFor(ZRules, round, players);
            foreach (var zp in ZPlayers.Values)
                zp.Points = Math.Max(zp.Points, pts);
            _zRunStartPoints = Math.Max(_zRunStartPoints, pts);
            AdvancePowerScheduleBy(TeamEarnedBefore(ZRules, round, players) - TeamEarnedBefore(ZRules, ZRound + 1, players));
        }
        ModeEvent($"The host skipped to round {round}", Sfx.HitTick);
        BeginRound(round, now);
        return true;
    }
}
