using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>Host setting: can players pick their own team in team modes?</summary>
public enum TeamSelection : byte
{
    Manual, // players pick Blue, Red or Auto (default)
    AutoOnly, // everyone is on Auto: the game splits the teams (the old behaviour)
}

/// <summary>
/// The team-pick rules (data, shared by the server's checks and the client's buttons):
///   - a side may be at most <see cref="DefaultMaxDiff"/> players bigger than the other (host setting 0-4), counting
///     only what can't move: people who picked a side. Bots and people on Auto fill the smaller side, so they never
///     block a pick (a person joining a side with a bot on it swaps places with that bot);
///   - switching sides is allowed before the match (warmup, countdown, results) or while dead, at most once every
///     <see cref="SwitchCooldown"/> seconds, never while carrying the flag. It's a respawn, not a suicide: no death, no score change;
///   - autobalance only ever moves bots and people on Auto (people on Auto only while dead or between matches).
/// </summary>
public static class TeamRules
{
    /// <summary>Default for "Max team size difference".</summary>
    public const int DefaultMaxDiff = 2;

    /// <summary>The highest "Max team size difference" the host can set.</summary>
    public const int MaxDiffLimit = 4;

    /// <summary>Seconds between two team changes by the same player.</summary>
    public const double SwitchCooldown = 10;

    /// <summary>The setting's name on screen and in settings.txt.</summary>
    public static string Name(TeamSelection s) => s == TeamSelection.AutoOnly ? "Auto only" : "Manual";
}

/// <summary>Team selection (server-authoritative): requests, the balance rule, the reply and the replicated picks.</summary>
public sealed partial class Game
{
    /// <summary>Host setting: Manual (players pick) or Auto only. Replicated in the mode state.</summary>
    public TeamSelection TeamSelection = TeamSelection.Manual;

    private int _maxTeamDiff = TeamRules.DefaultMaxDiff;

    /// <summary>Host setting: how many players bigger than the other side a team may get by picks (0-4). Replicated.</summary>
    public int MaxTeamDiff
    {
        get => _maxTeamDiff;
        set => _maxTeamDiff = Math.Clamp(value, 0, TeamRules.MaxDiffLimit);
    }

    /// <summary>Players can pick their team right now (a team mode, and the host allows it).</summary>
    public bool ManualTeams => IsTeamMode && TeamSelection == TeamSelection.Manual;

    /// <summary>Client: the last team-change answer ("You joined Red", or why not) and when it came.</summary>
    public string TeamNotice = "";

    public double TeamNoticeAt = -100;

    /// <summary>
    /// Would <paramref name="team"/> be too big with <paramref name="mover"/> on it (null: as things stand)? Only people
    /// who picked a side count; bots and people on Auto can fill the other side, so they never make a team full.
    /// </summary>
    private bool TeamTooBig(byte team, Player? mover)
    {
        if (TeamBalance != TeamBalance.Auto)
            return false; // balance Off: no size limit; Humans vs Bots: the sides are fixed (TeamBalance.cs)
        int mine = 0, other = 0, flexible = 0;
        foreach (var p in Players.Values)
        {
            var picked = p == mover || (p.Bot == null && p.TeamChoice < 2);
            if (!picked)
            {
                flexible++;
                continue;
            }
            var t = p == mover ? team : p.TeamChoice;
            if (t == team)
                mine++;
            else
                other++;
        }
        var allowed = Math.Max(MaxTeamDiff, Players.Count % 2); // an odd count can always be one apart
        return mine - other - flexible > allowed;
    }

    /// <summary>
    /// Why <paramref name="p"/> can't pick <paramref name="team"/> (0 Blue, 1 Red, <see cref="NoTeam"/> Auto) right now,
    /// or null if they can. The server decides with it; the client greys out buttons with the same rules.
    /// </summary>
    public string? TeamRefusal(Player p, byte team, double now)
    {
        if (!IsTeamMode)
            return "There are no teams in this mode";
        if (team > 1)
            return null; // Auto: always allowed (it doesn't move you; autobalance may later)
        if (TeamSelection == TeamSelection.AutoOnly)
            return "The host set team selection to Auto only";
        if (HumansVsBots && team != HumansTeam)
            return $"Humans vs Bots: people play on {GameModes.TeamNames[HumansTeam]}, bots on {GameModes.TeamNames[1 - HumansTeam]}";
        if (p.TeamChoice == team)
            return $"You're already on {GameModes.TeamNames[team]}";
        if (team != p.Team)
        {
            if (CarriedFlagOf(p) >= 0)
                return "You can't switch teams while carrying the flag";
            if (Phase == MatchPhase.Playing && p.Alive)
                return "Switch teams while dead or before the match starts";
            var wait = Phase == MatchPhase.Lobby ? 0 : TeamRules.SwitchCooldown - (now - p.TeamSwitchAt); // no cooldown in the lobby room
            if (wait > 0)
                return $"Wait {Math.Ceiling(wait):0} s before switching again";
        }
        if (TeamTooBig(team, p))
            return $"{GameModes.TeamNames[team]} is full: teams can differ by at most {MaxTeamDiff} player{(MaxTeamDiff == 1 ? "" : "s")}";
        return null;
    }

    /// <summary>
    /// Server: <paramref name="p"/> asks for a team. Returns null when it was applied, or the reason it wasn't. A real
    /// switch respawns them on the new side if they're up (warmup/countdown) and counts as a respawn: no death, no score
    /// change. Bots then give way straight away so the sides stay even.
    /// </summary>
    private string? ServerSetTeam(Player p, byte team, double now)
    {
        if (TeamRefusal(p, team, now) is { } why)
            return why;
        if (team > 1)
        {
            p.TeamChoice = NoTeam;
            return null;
        }
        p.TeamChoice = team;
        if (p.Team == team)
            return null; // stayed put, now a pick that autobalance leaves alone
        p.Team = team;
        p.TeamSwitchAt = now;
        if (p.Alive && Phase is MatchPhase.Warmup or MatchPhase.Countdown)
        {
            Spawn(p, ServerTick, now); // over to the new side's spawns (a respawn, not a death)
            if (p.Id == LocalId)
                OnLocalSpawn(p);
        }
        ModeEvent($"{p.Name} joined {GameModes.TeamNames[team]}", Sfx.HitTick);
        for (var i = 0; i < Players.Count && BalanceTeams(now); i++)
        {
            // bots (and people on Auto) give way until the sides are even again
        }
        return null;
    }

    /// <summary>Host: change the team rules mid-session. Auto only puts everyone back on Auto.</summary>
    public void SetTeamRules(TeamSelection selection, int maxDiff)
    {
        if (!IsServer || (selection == TeamSelection && Math.Clamp(maxDiff, 0, TeamRules.MaxDiffLimit) == MaxTeamDiff))
            return;
        TeamSelection = selection;
        MaxTeamDiff = maxDiff;
        if (selection == TeamSelection.AutoOnly)
            foreach (var p in Players.Values)
                p.TeamChoice = NoTeam;
        Log($"Team selection: {TeamRules.Name(selection)}, max difference {MaxTeamDiff}", true);
    }

    /// <summary>Join Blue / Join Red / Auto from the menus. The host applies it at once; a client asks the server.</summary>
    public void RequestTeam(byte team) => RequestTeam(team, Brutal.GlfwApi.Glfw.Time);

    /// <summary><see cref="RequestTeam(byte)"/> with an explicit clock (self-test).</summary>
    public void RequestTeam(byte team, double now)
    {
        if (Local is not { } me)
            return;
        team = team > 1 ? NoTeam : team;
        if (IsServer)
        {
            var before = me.Team;
            var why = ServerSetTeam(me, team, now);
            ShowTeamNotice(why ?? TeamDoneText(me, before), now);
            return;
        }
        if (TeamRefusal(me, team, now) is { } refused)
        {
            ShowTeamNotice(refused, now);
            return;
        }
        _w.Begin(Msg.TeamPick).U8(team);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    private static string TeamDoneText(Player p, byte before) =>
        p.TeamChoice > 1 ? "Auto: the game picks your side to keep the teams even"
        : p.Team != before ? $"You joined {GameModes.TeamNames[p.Team]}"
        : $"You're staying on {GameModes.TeamNames[p.Team]}";

    private void ShowTeamNotice(string text, double now)
    {
        TeamNotice = text;
        TeamNoticeAt = now;
    }

    /// <summary>Team-pick messages: a client's request (server) and the server's answer (client).</summary>
    private void OnTeamPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.TeamPick when IsServer && r.Has(1):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                var want = r.U8();
                var before = p.Team;
                var why = ServerSetTeam(p, want > 1 ? NoTeam : want, now);
                var flags = (byte)((why == null ? 1 : 0) | (p.Team != before ? 2 : 0));
                var text = why ?? TeamDoneText(p, before);
                _w.Begin(Msg.TeamReply).U8(flags).U8(p.Team).U8(p.TeamChoice).Str(text);
                Net.SendTo(sender, _w.Span, PacketReliability.ReliableOrdered);
                break;
            }
            case (byte)Msg.TeamReply when !IsServer && r.Has(3):
            {
                var flags = r.U8();
                var team = r.U8();
                var choice = r.U8();
                var text = r.Has(1) ? new string(r.Str().Where(ch => ch >= 32 && ch < 127).Take(100).ToArray()) : ""; // printable ASCII only
                if (Local is { } me && (flags & 1) != 0)
                {
                    me.Team = team <= 1 ? team : me.Team;
                    me.TeamChoice = choice <= 1 ? choice : NoTeam;
                    if ((flags & 2) != 0)
                        me.TeamSwitchAt = now; // the cooldown runs on this side too
                }
                ShowTeamNotice(text, now);
                break;
            }
        }
    }

    /// <summary>Mode state tail (protocol 20): the team rules and every player's pick.</summary>
    private void WriteTeamPicks(PacketWriter w)
    {
        w.U8((byte)TeamSelection).U8((byte)MaxTeamDiff).U8((byte)Players.Count);
        foreach (var p in Players.Values)
            w.U8(p.Id).U8(p.TeamChoice);
        w.U8((byte)((byte)TeamBalance | (HumansTeam << 4))); // protocol 32: the team balance (TeamBalance.cs)
    }

    private void ReadTeamPicks(ref PacketReader r)
    {
        if (!r.Has(3))
            return;
        TeamSelection = r.U8() == (byte)TeamSelection.AutoOnly ? TeamSelection.AutoOnly : TeamSelection.Manual;
        MaxTeamDiff = r.U8();
        int n = r.U8();
        for (var i = 0; i < n && r.Has(2); i++)
        {
            var id = r.U8();
            var choice = r.U8();
            if (Players.TryGetValue(id, out var p))
                p.TeamChoice = choice <= 1 ? choice : NoTeam;
        }
        if (r.Has(1))
        {
            var b = r.U8();
            TeamBalance = (b & 15) is var v && v <= (int)TeamBalance.HumansVsBots ? (TeamBalance)v : TeamBalance.Auto;
            HumansTeam = (byte)(b >> 4);
        }
    }

    // ---- Self-test hooks -------------------------------------------------------------------------

    /// <summary>Self-test: the server handling a team request from <paramref name="p"/> (null: applied).</summary>
    public string? SelfTestSetTeam(Player p, byte team, double now) => ServerSetTeam(p, team, now);

    /// <summary>Self-test: run autobalance until it has nothing left to move. Returns how many players it moved.</summary>
    public int SelfTestBalance(double now)
    {
        var moved = 0;
        while (moved < Players.Count && BalanceTeams(now))
            moved++;
        return moved;
    }

    /// <summary>Self-test: split the teams the way a new match does.</summary>
    public void SelfTestAssignTeams() => AssignTeams();
}
