using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Lobby size (server-authoritative). Up to <see cref="MaxPlayers"/> (32) players, people and bots, but never more
/// than the map's own limit (<see cref="Map.MaxPlayers"/>, MapPlayers.cs):
///   - bots are added up to the limit; the number asked for is remembered (<see cref="WantedBots"/>), so a bigger map
///     after the vote gets them back and a smaller one sends the extras home;
///   - a person joining a full map takes a bot's place; when only people are left, the join is refused with a message;
///   - the map vote only offers maps big enough for the people playing (people are never removed; MapVote.cs).
/// </summary>
public sealed partial class Game
{
    /// <summary>The old full lobby (the self-test's standard bot matches still use it: 7 bots + you fit every map).</summary>
    public const int ClassicPlayers = 8;

    /// <summary>How many bots the host asked for (bots actually playing can be fewer when the map is smaller).</summary>
    public int WantedBots;

    private BotDifficulty _botSkill = BotDifficulty.Normal;

    /// <summary>The current map's player limit (people + bots).</summary>
    public int MapMaxPlayers => PlayerLimit(World.Map); // (Zombies: co-op for up to 4)

    /// <summary>The player limit (people + bots) the lobby would enforce on <paramref name="map"/> in the current mode.</summary>
    public int PlayerLimit(Map map) => Math.Min(Module.MaxPlayers(map), MaxPlayers);

    /// <summary>People in the game (not bots).</summary>
    public int HumanCount => Players.Values.Count(p => p.Bot == null);

    public int BotCount => Players.Values.Count(p => p.Bot != null);

    /// <summary>The map is full (a newcomer would need a bot's place).</summary>
    public bool IsFull => Players.Count >= MapMaxPlayers;

    /// <summary>
    /// Server: after a map change, removes bots the new map has no room for, or brings back bots the host asked for
    /// that a smaller map had sent away. People are never removed.
    /// </summary>
    private void FitPlayersToMap()
    {
        if (!IsServer)
            return;
        var removed = 0;
        while (Players.Count > MapMaxPlayers)
        {
            // The dead first, then the newest.
            var bot = Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Alive).ThenByDescending(p => p.Id).FirstOrDefault();
            if (bot == null)
                break;
            Players.Remove(bot.Id);
            OnModePlayerLeave(bot);
            removed++;
        }
        if (removed > 0)
        {
            Log($"{removed} bot{(removed == 1 ? "" : "s")} left: {World.Map.Name} takes {MapMaxPlayers} players", true);
            BroadcastRoster();
        }
        var added = 0;
        while (BotCount < WantedBots && FreeId() != null && AddBot(_botSkill, quiet: true))
            added++;
        if (added > 0)
            Log($"{added} bot{(added == 1 ? "" : "s")} joined: {World.Map.Name} takes {MapMaxPlayers} players", true);
    }

    /// <summary>
    /// The maps a vote may offer for this many people (humans; bots don't count): those whose player limit
    /// (<paramref name="limit"/>, by default the map's own) takes them all, or the largest ones (highest limit) if none does.
    /// </summary>
    public static List<Map> FitForPeople(List<Map> pool, int humans, Func<Map, int>? limit = null)
    {
        limit ??= m => m.MaxPlayers;
        var fit = pool.Where(m => limit(m) >= humans).ToList();
        if (fit.Count > 0 || pool.Count == 0)
            return fit;
        var top = pool.Max(limit);
        return pool.Where(m => limit(m) == top).ToList();
    }

    /// <summary>Server: a connection arrived but the map is full of people. Tell them why, then close it.</summary>
    private void RefuseFull(RakNetGUID who)
    {
        _w.Begin(Msg.Full).U8((byte)Players.Count).U8((byte)MapMaxPlayers).Str(World.Map.Name);
        Net.SendTo(who, _w.Span, PacketReliability.ReliableOrdered);
        Net.Kick(who);
        Log($"Refused a player: {World.Map.Name} is full ({Players.Count}/{MapMaxPlayers})");
    }

    private void OnFullPacket(ref PacketReader r)
    {
        if (IsServer || !r.Has(2))
            return;
        int n = r.U8(), max = r.U8();
        var map = r.Has(1) ? r.Text(40) : "the map";
        Status = $"The server is full: {n}/{max} players on {map}";
        Disconnected = true;
        Log(Status, true);
    }

    // ---- Joined / left lines: grouped so a full lobby filling up doesn't flood the kill feed ----

    private string _joinFirst = "";
    private int _joinCount;
    private double _joinAt = -100;

    /// <summary>"X joined" for the feed. Joins within 3 s of each other share one line ("X and 5 others joined").</summary>
    private void LogJoin(string name, string detail = "")
    {
        var now = Brutal.GlfwApi.Glfw.Time;
        Console.WriteLine($"{name} joined{detail}");
        if (now - _joinAt < 3 && _joinCount > 0 && Feed.Count > 0 && Feed[^1].Text.StartsWith(_joinFirst, StringComparison.Ordinal))
        {
            _joinCount++;
            _joinAt = now;
            Feed[^1] = new FeedEntry(now, $"{_joinFirst} and {_joinCount - 1} other{(_joinCount == 2 ? "" : "s")} joined", false);
            return;
        }
        _joinFirst = name;
        _joinCount = 1;
        _joinAt = now;
        Feed.Add(new FeedEntry(now, $"{name} joined{detail}", false));
    }

    /// <summary>Self-test: switch maps the way a map vote does (bots leave or come back to fit).</summary>
    public void SelfTestChangeMap(Map map) => ApplyMapChange(map);
}
