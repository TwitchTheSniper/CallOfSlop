namespace CallOfSlop;

/// <summary>Host setting: how team modes keep the sides (settings.txt team_balance=auto|off|humans_vs_bots).</summary>
public enum TeamBalance : byte
{
    Auto, // the usual: autobalance moves bots (and people on Auto) to keep the sides even, picks limited by the max difference
    Off, // no autobalance and no team-size limit on picks: people go where they like, bots fill wherever they were put
    HumansVsBots, // every person on one side (the host picks which, Blue by default), every bot on the other
}

/// <summary>
/// The team-balance setting (protocol 32, replicated with the team picks in the mode state):
///   - Auto: as before (TeamPick.cs, <see cref="Game.BalanceTeams"/>);
///   - Off: <see cref="Game.TeamTooBig"/> never refuses and autobalance never moves anyone; a new match still splits
///     people on Auto and bots evenly, then nobody is moved;
///   - Humans vs Bots: people are on <see cref="Game.HumansTeam"/>, bots on the other side (new matches, joiners,
///     bots added later); picking the bots' side is refused; autobalance and SHUFFLE / BALANCE are off. The bot count
///     is the host's fill slider; score limits, spawns and the map's player limit work as in any team match.
/// </summary>
public sealed partial class Game
{
    /// <summary>Host setting: Auto, Off or Humans vs Bots. Replicated.</summary>
    public TeamBalance TeamBalance = TeamBalance.Auto;

    private byte _humansTeam;

    /// <summary>Humans vs Bots: the people's side (0 Blue, 1 Red). Replicated.</summary>
    public byte HumansTeam
    {
        get => _humansTeam;
        set => _humansTeam = value == 1 ? (byte)1 : (byte)0;
    }

    /// <summary>The setting's name on screen.</summary>
    public static string TeamBalanceName(TeamBalance b) => b switch
    {
        TeamBalance.Off => "Off",
        TeamBalance.HumansVsBots => "Humans vs Bots",
        _ => "Auto",
    };

    /// <summary>settings.txt value.</summary>
    public static string TeamBalanceKey(TeamBalance b) => b switch
    {
        TeamBalance.Off => "off",
        TeamBalance.HumansVsBots => "humans_vs_bots",
        _ => "auto",
    };

    public static TeamBalance ParseTeamBalance(string v) => v.Trim().ToLowerInvariant() switch
    {
        "off" => TeamBalance.Off,
        "humans_vs_bots" or "humansvsbots" => TeamBalance.HumansVsBots,
        _ => TeamBalance.Auto,
    };

    /// <summary>Humans vs Bots is on in a team mode.</summary>
    public bool HumansVsBots => IsTeamMode && TeamBalance == TeamBalance.HumansVsBots;

    /// <summary>Autobalance may move players (Auto only).</summary>
    private bool AutoBalanceOn => TeamBalance == TeamBalance.Auto;

    /// <summary>Humans vs Bots: the side this player belongs on.</summary>
    private byte HvbTeamOf(Player p) => p.Bot != null ? (byte)(1 - HumansTeam) : HumansTeam;

    /// <summary>Humans vs Bots: put everyone on their side (people's picks cleared to Auto). Returns how many moved.</summary>
    private int ApplyHumansVsBots()
    {
        var moved = 0;
        foreach (var p in Players.Values)
        {
            var t = HvbTeamOf(p);
            p.TeamChoice = NoTeam;
            if (p.Team != t)
            {
                p.Team = t;
                moved++;
            }
        }
        return moved;
    }

    /// <summary>Host: change the team balance (and the people's side for Humans vs Bots). Applied at once.</summary>
    public void SetTeamBalance(TeamBalance balance, byte humansTeam)
    {
        if (!IsServer)
            return;
        humansTeam = humansTeam == 1 ? (byte)1 : (byte)0;
        if (balance == TeamBalance && humansTeam == HumansTeam)
            return;
        TeamBalance = balance;
        HumansTeam = humansTeam;
        if (IsTeamMode)
        {
            Span<byte> before = stackalloc byte[MaxPlayers];
            foreach (var p in Players.Values)
                before[p.Id] = p.Team;
            AssignTeams();
            var now = Brutal.GlfwApi.Glfw.Time;
            if (Phase != MatchPhase.Ended)
                foreach (var p in Players.Values)
                    if (p.Alive && before[p.Id] != p.Team)
                    {
                        Spawn(p, ServerTick, now); // over to their new side's spawns (a respawn, not a death)
                        if (p.Id == LocalId)
                            OnLocalSpawn(p);
                    }
        }
        Log($"Team balance: {TeamBalanceName(balance)}{(balance == TeamBalance.HumansVsBots ? $" (people on {GameModes.TeamNames[HumansTeam]})" : "")}", true);
        MarkLobbyDirty();
    }
}
