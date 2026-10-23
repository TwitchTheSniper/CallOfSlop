namespace CallOfSlop;

/// <summary>
/// What the scoreboard (Tab) shows, kept apart from the ImGui drawing so the self-test can check it: the column headers
/// (a Level column; no health, which only your own HUD shows), each player's Level cell (prestige and level as in the
/// lobby, "BOT" for bots, the hat they wear drawn beside it) and the ranking. Everything here is allocation-free once
/// warmed up (cached strings, reused lists, a cached sort delegate), because the scoreboard is drawn every frame Tab is held.
/// </summary>
public static class ScoreboardModel
{
    /// <summary>The Level column's header.</summary>
    public const string LevelHeader = "Level";

    /// <summary>The Level cell of a bot. (Bots have no account level; if bots ever get an effective level, show it here.)</summary>
    public const string BotLevel = "BOT";

    /// <summary>The Level cell of a person who didn't report a level (a very old client).</summary>
    public const string UnknownLevel = "-";

    /// <summary>One player's Level cell: the label ("P2 L13", "L7", "BOT", "-"), the hat to draw beside it, and whether it's a bot.</summary>
    public readonly record struct LevelCell(string Label, HatId Hat, bool Bot);

    private static string[]? _labels;
    private static string[]? _numbers;
    private static readonly Dictionary<int, string> Fractions = new();
    private static readonly Dictionary<(Type, bool), string[]> HeaderCache = new();
    private static readonly Dictionary<Type, string> UpperNames = new();

    /// <summary>The team names in capitals for the team header rows ("BLUE", "RED").</summary>
    public static readonly string[] TeamUpper = GameModes.TeamNames.Select(n => n.ToUpperInvariant()).ToArray();

    /// <summary>
    /// The scoreboard's column headers for a mode. Full size: Player, Level, the mode's two columns, Weapon, Status.
    /// Compact (big lobbies, two side-by-side tables): #, Player, Level, the mode's short columns, and a narrow status
    /// column (empty header; "dead" while dead). Cached per mode type (each game makes its own module instances).
    /// </summary>
    public static string[] Headers(GameModeModule mode, bool compact)
    {
        if (HeaderCache.TryGetValue((mode.GetType(), compact), out var h))
            return h;
        var (c1, c2) = compact ? mode.ScoreboardColumnsShort : mode.ScoreboardColumns;
        h = compact ? new[] { "#", "Player", LevelHeader, c1, c2, "" } : new[] { "Player", LevelHeader, c1, c2, "Weapon", "Status" };
        HeaderCache[(mode.GetType(), compact)] = h;
        return h;
    }

    /// <summary>The status cell: "alive" / "dead" (full size) or "" / "dead" (compact). Never the health.</summary>
    public static string Status(Player p, bool compact) => p.Alive ? (compact ? "" : "alive") : "dead";

    /// <summary>"P2 L13" / "L13" (as the lobby shows it), or "-" for an unknown level. Cached: no allocation.</summary>
    public static string LevelLabel(int prestige, int level)
    {
        if (level <= 0)
            return UnknownLevel;
        var maxL = Progress.MaxLevel;
        if (prestige < 0 || prestige > Cosmetics.MaxPrestige || level > maxL)
            return Progress.RankLabel(prestige, level); // out of range (can't happen with a valid roster)
        _labels ??= new string[(Cosmetics.MaxPrestige + 1) * (maxL + 1)];
        var i = prestige * (maxL + 1) + level;
        if ((uint)i >= (uint)_labels.Length)
            return Progress.RankLabel(prestige, level);
        return _labels[i] ??= Progress.RankLabel(prestige, level);
    }

    /// <summary>
    /// A player's Level cell. Bots: "BOT". Yourself: your live progression. Others: the prestige and level from the roster
    /// (sent on joining and refreshed by the server when a player's level changes, see GameCosmetics.cs).
    /// </summary>
    public static LevelCell Level(Game game, Player p)
    {
        if (p.Bot != null)
            return new LevelCell(BotLevel, HatId.None, true);
        if (p.Id == game.LocalId && game.Progress is { } prog)
            return new LevelCell(LevelLabel(prog.Prestige, prog.Level), p.Hat, false);
        return new LevelCell(LevelLabel(p.Prestige, p.Level), p.Hat, false);
    }

    /// <summary>A number as text, cached for 0..4095 (kills, deaths, points).</summary>
    public static string Num(int n)
    {
        if (n < 0 || n >= 4096)
            return n.ToString();
        _numbers ??= new string[4096];
        return _numbers[n] ??= n.ToString();
    }

    /// <summary>"a/b" (Gun Game's ladder step), cached.</summary>
    public static string Fraction(int a, int b)
    {
        if (a < 0 || b < 0 || a > 0xFFFF || b > 0x7FFF)
            return $"{a}/{b}";
        var key = (b << 16) | a;
        if (!Fractions.TryGetValue(key, out var s))
            Fractions[key] = s = $"{a}/{b}";
        return s;
    }

    /// <summary>The mode's name in capitals for the title, cached.</summary>
    public static string UpperName(GameModeModule mode)
    {
        if (!UpperNames.TryGetValue(mode.GetType(), out var s))
            UpperNames[mode.GetType()] = s = mode.Name.ToUpperInvariant();
        return s;
    }

    private static GameModeModule? _sortMode;
    private static readonly Comparison<Player> ByRank = CompareRank;

    private static int CompareRank(Player a, Player b)
    {
        var mode = _sortMode!;
        var c = mode.RankScore(b).CompareTo(mode.RankScore(a));
        if (c == 0)
            c = b.Kills.CompareTo(a.Kills);
        if (c == 0)
            c = a.Deaths.CompareTo(b.Deaths);
        return c != 0 ? c : a.Id.CompareTo(b.Id);
    }

    /// <summary>Fills <paramref name="into"/> with every player, best first (the mode's score, then kills, fewer deaths, id). No allocation.</summary>
    public static void Rank(Game game, List<Player> into)
    {
        into.Clear();
        foreach (var p in game.Players.Values)
            into.Add(p);
        _sortMode = game.Module;
        into.Sort(ByRank);
        _sortMode = null;
    }

    /// <summary>
    /// The big scoreboard's two columns from a ranking: team modes Blue left and Red right; otherwise the ranking in
    /// halves (1-16 left, 17-32 right). Fills the given lists (no allocation).
    /// </summary>
    public static void Split(Game game, List<Player> ranked, List<Player> left, List<Player> right)
    {
        left.Clear();
        right.Clear();
        if (game.IsTeamMode)
        {
            foreach (var p in ranked)
                (p.Team == 0 ? left : right).Add(p);
            return;
        }
        var half = (ranked.Count + 1) / 2;
        for (var i = 0; i < ranked.Count; i++)
            (i < half ? left : right).Add(ranked[i]);
    }

    /// <summary>The number of bots in the game (no LINQ: no boxed enumerator).</summary>
    public static int BotCount(Game game)
    {
        var n = 0;
        foreach (var p in game.Players.Values)
            if (p.Bot != null)
                n++;
        return n;
    }
}
