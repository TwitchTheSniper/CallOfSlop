using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// End-of-match map vote (server-authoritative).
///   - When a match ends the server picks 3 maps from the pack of the map just played (True Random: from every
///     pack in True Random), one small, one medium, one large when possible, never the map just played, and
///     broadcasts them.
///   - Player count: only maps whose player limit in the current mode (<see cref="PlayerLimit"/>) takes every human
///     here (bots don't count; people still loading do) can be offered, so fewer than 3 options when few fit. When
///     none fits: the map just played if it does, else the largest maps (logged). See VoteCandidates.
///   - Someone joins during the vote and an option no longer takes everyone: it's replaced in its slot by another
///     map that fits (its votes are dropped), or removed when none is left. See <see cref="RefitBallot"/>.
///   - Human players vote during the 12 s results screen (click, or press 1/2/3) and can change their vote until
///     the timer runs out. Bots never vote and don't count anywhere (tally, "everyone voted").
///   - Once every human here has voted, the timer drops to <see cref="VoteEarlyEndSeconds"/> (if it was longer).
///   - The next match starts on the map with the most human votes. Ties are broken at random among the tied maps;
///     no human votes at all (a bots-only match, or nobody clicked) is a tie of all three, so a random pick.
///     See <see cref="PickVoteWinner"/>.
/// </summary>
public sealed partial class Game
{
    public const int VoteChoices = 3;

    /// <summary>Seconds left on the results screen once every human has voted (when more were left).</summary>
    public const double VoteEarlyEndSeconds = 3;

    /// <summary>Map ids on the ballot (empty when no vote is running).</summary>
    public byte[] VoteOptions = [];

    /// <summary>Map names on the ballot (what's shown; a client may not have every map the host offers).</summary>
    public string[] VoteNames = [];

    /// <summary>Human votes per option, same order as <see cref="VoteOptions"/> (bots don't vote).</summary>
    public byte[] VoteTally = [];

    /// <summary>The option the local player picked (-1: none yet).</summary>
    public int MyVote = -1;

    /// <summary>Turn off to keep playing the same map (the self-test uses this).</summary>
    public bool MapVoteEnabled = true;

    /// <summary>
    /// The host picked True Random: the vote offers maps from every pack in True Random (still only maps big enough
    /// for the people here) instead of the pack of the map just played, and server browsers show "True Random".
    /// </summary>
    public bool TrueRandom;

    public bool VoteOpen => Phase == MatchPhase.Ended && VoteOptions.Length > 0;

    private readonly Dictionary<byte, int> _votes = new(); // human player id -> option

    /// <summary>True when at least one human is here and every human has a vote in (bots don't count).</summary>
    public bool AllHumansVoted => VoteOpen && HumanCount > 0
        && Players.Values.All(p => p.Bot != null || _votes.ContainsKey(p.Id));

    // ---- Server ---------------------------------------------------------------------

    private void StartMapVote(double now)
    {
        _votes.Clear();
        MyVote = -1;
        var pool = VoteCandidates(out var fallback); // never a map too small for the people here (bots don't count)
        if (fallback)
            LogVoteFallback(pool);
        SetBallot(PickBallot(pool, _rng));
        BroadcastVoteState();
    }

    /// <summary>
    /// Picks up to <see cref="VoteChoices"/> maps from <paramref name="pool"/>: one small, one medium, one large when
    /// possible, then any others; fewer when the pool is smaller. Ordered by size. Deterministic for a given seed.
    /// </summary>
    public static List<Map> PickBallot(IReadOnlyList<Map> pool, Random rng)
    {
        var picks = new List<Map>();
        // One of each size first, so there's always a choice of pace...
        foreach (var size in new[] { MapSize.Small, MapSize.Medium, MapSize.Large })
        {
            var ofSize = pool.Where(m => m.Size == size && !picks.Contains(m)).ToList();
            if (ofSize.Count > 0 && picks.Count < VoteChoices)
                picks.Add(ofSize[rng.Next(ofSize.Count)]);
        }
        // ...then fill up with anything left.
        var rest = pool.Where(m => !picks.Contains(m)).Distinct().ToList();
        while (picks.Count < VoteChoices && rest.Count > 0)
        {
            var m = rest[rng.Next(rest.Count)];
            rest.Remove(m);
            picks.Add(m);
        }
        return picks.OrderBy(m => m.Size).ToList();
    }

    /// <summary>
    /// The player-count rule for the next map (vote options, a refit during the vote, the fallback when the winner is
    /// gone): out of <paramref name="scope"/> (the pack or True Random pool, without the map just played), the maps
    /// the current mode plays (<paramref name="modeFits"/>; ignored if it would leave nothing) whose player limit
    /// (<paramref name="limit"/>: what the lobby enforces in this mode) takes every human here. Bots never count:
    /// they're trimmed or added to fit the map, as on any map change. When none takes everyone:
    ///   - the map just played (<paramref name="current"/>), if it does: playing it again beats overfilling a map;
    ///   - otherwise the largest maps (the highest limit), with <paramref name="fallback"/> set (the server logs it).
    /// </summary>
    public static List<Map> VoteCandidates(IReadOnlyList<Map> scope, Map? current, int humans, Func<Map, int> limit,
        Func<Map, bool>? modeFits, out bool fallback)
    {
        fallback = false;
        var pool = scope.ToList();
        if (modeFits != null && pool.Any(modeFits))
            pool = pool.Where(modeFits).ToList();
        var fit = pool.Where(m => limit(m) >= humans).ToList();
        if (fit.Count > 0)
            return fit;
        var currentOk = current != null && (modeFits?.Invoke(current) ?? true);
        if (currentOk && limit(current!) >= humans)
            return [current!];
        fallback = true;
        var top = pool.Count > 0 ? pool.Max(limit) : 0;
        if (currentOk && limit(current!) > top)
            return [current!];
        return pool.Where(m => limit(m) == top).ToList();
    }

    /// <summary>The next-map candidates for the people here right now (see the static overload).</summary>
    private List<Map> VoteCandidates(out bool fallback)
    {
        // By name: after a map reload the playing map (and its pack) aren't in the lists as the same objects.
        var current = Maps.Find(World.Map.Name) ?? World.Map;
        var pack = current.Pack is { } playedPack ? Maps.FindPack(playedPack.Name) : null;
        var scope = TrueRandom ? TrueRandomVotePool(World.Map.Name, Maps.Packs, Maps.All) : VotePool(World.Map.Name, pack, Maps.All);
        return VoteCandidates(scope, current, HumanCount, PlayerLimit, m => Module.Fits(m), out fallback);
    }

    private void LogVoteFallback(List<Map> pool) =>
        Log($"Map vote: no map takes all {HumanCount} players here; offering the largest ({string.Join(", ", pool.Select(m => $"{m.Name} {PlayerLimit(m)}"))})");

    /// <summary>
    /// Puts <paramref name="maps"/> on the ballot. Votes for maps still on it follow them (by name, the same way a
    /// client keeps its pick); votes for maps that left are dropped, so those people pick again.
    /// </summary>
    private void SetBallot(List<Map> maps)
    {
        var oldNames = VoteNames;
        VoteOptions = maps.Select(m => m.Id).ToArray();
        VoteNames = maps.Select(m => m.Name).ToArray();
        VoteTally = new byte[VoteOptions.Length];
        _ballotMinLimit = maps.Count > 0 ? maps.Min(m => PlayerLimit(m)) : int.MaxValue;
        foreach (var (pid, option) in _votes.ToArray())
        {
            var moved = option < oldNames.Length ? Array.IndexOf(VoteNames, oldNames[option]) : -1;
            if (moved >= 0)
                _votes[pid] = moved;
            else
                _votes.Remove(pid);
        }
        MyVote = _votes.TryGetValue(LocalId, out var mine) ? mine : -1;
        RecountVotes();
    }

    private int _ballotMinLimit = int.MaxValue;

    /// <summary>
    /// Server: someone joined during the vote and an option no longer takes every human here. The rule: each option
    /// that's now too small is replaced in its slot by a random fitting map that isn't on the ballot yet (votes for it
    /// are dropped, so those people vote again); with no such map left, that option is removed. When nothing takes
    /// everyone any more, the ballot is redrawn from the fallback (the largest maps). Votes for maps that stay keep counting.
    /// </summary>
    private void RefitBallot()
    {
        if (!VoteOpen || HumanCount <= _ballotMinLimit)
            return;
        var humans = HumanCount;
        var options = VoteNames.Select(n => Maps.Find(n)).ToArray();
        var candidates = VoteCandidates(out var fallback);
        List<Map> slots;
        if (fallback)
        {
            if (options.All(o => o != null && candidates.Any(c => c.Name == o.Name)))
            {
                _ballotMinLimit = int.MaxValue; // already the fallback ballot: nothing better to offer
                return;
            }
            LogVoteFallback(candidates);
            slots = PickBallot(candidates, _rng);
        }
        else
        {
            var spare = candidates.Where(c => !options.Any(o => o?.Name == c.Name)).ToList();
            slots = [];
            foreach (var o in options)
            {
                if (o != null && PlayerLimit(o) >= humans)
                    slots.Add(o);
                else if (spare.Count > 0)
                {
                    var r = spare[_rng.Next(spare.Count)];
                    spare.Remove(r);
                    slots.Add(r);
                }
            }
        }
        Log($"Map vote: {humans} players now; ballot changed to {string.Join(", ", slots.Select(m => m.Name))}");
        SetBallot(slots);
        BroadcastVoteState();
    }

    /// <summary>Self-test only (server): re-check the ballot against the people here now (the server does it every tick).</summary>
    public void DebugRefitBallot()
    {
        if (IsServer)
            RefitBallot();
    }

    /// <summary>
    /// The maps a ballot may offer: the other maps of the pack just played, or every other map when that pack
    /// has fewer than 2 others (or the map has no pack).
    /// </summary>
    public static List<Map> VotePool(string current, MapPack? pack, IReadOnlyList<Map> all)
    {
        bool Other(Map m) => !m.Name.Equals(current, StringComparison.OrdinalIgnoreCase);
        var inPack = pack?.Maps.Where(Other).ToList();
        return inPack is { Count: >= 2 } ? inPack : all.Where(Other).ToList();
    }

    /// <summary>True Random's ballot pool: every other map of the packs True Random draws from (Dev test maps stay out).</summary>
    public static List<Map> TrueRandomVotePool(string current, IReadOnlyList<MapPack> packs, IReadOnlyList<Map> all) =>
        VotePool(current, null, MapPack.TrueRandomPool(packs, all));

    private void ServerTickVotes(double now)
    {
        RefitBallot(); // someone joined: no option may be too small for the people here
        // Every human has voted: no need to sit through the rest of the timer.
        if (AllHumansVoted && _intermissionUntil - now > VoteEarlyEndSeconds)
            _intermissionUntil = now + VoteEarlyEndSeconds;
    }

    private void ServerVote(byte playerId, int option)
    {
        if (!VoteOpen || option < 0 || option >= VoteOptions.Length)
            return;
        if (!Players.TryGetValue(playerId, out var voter) || voter.Bot != null)
            return; // bots don't vote
        _votes[playerId] = option;
        RecountVotes();
        BroadcastVoteState();
    }

    /// <summary>Self-test only (server): player <paramref name="playerId"/> votes as if their vote packet arrived.</summary>
    public void DebugVoteFor(byte playerId, int option)
    {
        if (IsServer)
            ServerVote(playerId, option);
    }

    private void RecountVotes()
    {
        VoteTally = new byte[VoteOptions.Length];
        foreach (var (pid, option) in _votes)
            if (Players.TryGetValue(pid, out var p) && p.Bot == null && option < VoteTally.Length)
                VoteTally[option]++;
    }

    /// <summary>
    /// The vote rule: the option with the most (human) votes wins. A tie is broken at random among the tied options,
    /// so no votes at all means a random pick among every option. Deterministic for a given <paramref name="rng"/> seed.
    /// Returns -1 for an empty ballot.
    /// </summary>
    public static int PickVoteWinner(IReadOnlyList<byte> tally, Random rng)
    {
        if (tally.Count == 0)
            return -1;
        var best = tally.Max();
        var top = Enumerable.Range(0, tally.Count).Where(i => tally[i] == best).ToList();
        return top[rng.Next(top.Count)];
    }

    /// <summary>Picks the next map with <see cref="PickVoteWinner"/> from the human votes.</summary>
    private Map? ResolveVote()
    {
        if (VoteOptions.Length == 0)
            return null;
        RefitBallot(); // a last check: never start a map with more people than it takes
        RecountVotes();
        var pick = PickVoteWinner(VoteTally, _rng);
        // By name, so a map reload between the vote and now picks up the current version of the winner.
        var winner = pick >= 0 && VoteNames.Length == VoteOptions.Length ? Maps.Find(VoteNames[pick]) : null;
        if (winner == null || PlayerLimit(winner) < HumanCount)
        {
            // The winner's file was removed meanwhile (or it shrank on a reload): a random map that fits instead.
            var pool = VoteCandidates(out _);
            winner = pool.Count > 0 ? pool[_rng.Next(pool.Count)] : null;
        }
        return winner;
    }

    private void BroadcastVoteState() => WriteVoteState(null);

    private void SendVoteState(RakNetGUID to) => WriteVoteState(to);

    private void WriteVoteState(RakNetGUID? to)
    {
        _w.Begin(Msg.VoteState).U8((byte)VoteNames.Length);
        foreach (var name in VoteNames)
            _w.Str(name);
        foreach (var n in VoteTally)
            _w.U8(n);
        if (to is { } guid)
            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
        else
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    // ---- Both sides -----------------------------------------------------------------

    /// <summary>The local player votes (results screen button or 1/2/3).</summary>
    public void CastVote(int option)
    {
        if (!VoteOpen || option < 0 || option >= VoteOptions.Length || option == MyVote)
            return;
        MyVote = option;
        Audio.Effect(Sfx.HitTick, 0.5f);
        if (IsServer)
        {
            ServerVote(LocalId, option);
        }
        else
        {
            _w.Begin(Msg.MapVote).U8((byte)option);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

    private void ClearVote()
    {
        _ballotMinLimit = int.MaxValue;
        VoteOptions = [];
        VoteNames = [];
        VoteTally = [];
        MyVote = -1;
        _votes.Clear();
    }

    private void OnVotePacket(byte id, ref PacketReader r, RakNetGUID sender)
    {
        switch (id)
        {
            case (byte)Msg.MapVote when IsServer && r.Has(1):
                if (_clients.TryGetValue(sender.G, out var c))
                    ServerVote(c.Id, r.U8());
                break;
            case (byte)Msg.VoteState when !IsServer && r.Has(1):
            {
                int n = r.U8();
                var names = new string[n];
                Array.Fill(names, ""); // a short packet leaves the rest empty, never null
                for (var i = 0; i < n && r.Has(1); i++)
                    names[i] = r.Str();
                if (!r.Has(n))
                    break;
                var tally = new byte[n];
                for (var i = 0; i < n; i++)
                    tally[i] = r.U8();
                if (!VoteNames.SequenceEqual(names)) // a new ballot, or one refit after a join: keep my pick if its map is still on it
                    MyVote = MyVote >= 0 && MyVote < VoteNames.Length ? Array.IndexOf(names, VoteNames[MyVote]) : -1;
                VoteNames = names;
                VoteOptions = names.Select(nm => Maps.Find(nm)?.Id ?? byte.MaxValue).ToArray(); // byte.MaxValue: we don't have that map
                VoteTally = tally;
                break;
            }
        }
    }

    /// <summary>
    /// Client: switch to the map the host named. Maps are matched by name and checked with a
    /// fingerprint. If we don't have it, we leave with a message saying which file to get.
    /// </summary>
    private bool UseHostMap(string name, uint hash, bool initial)
    {
        if (Maps.Find(name) is not { } map)
        {
            Status = $"The host is playing '{name}', which you don't have. Get its map file from the host and put it in your Maps folder.";
            Log(Status, true);
            Disconnected = true;
            return false;
        }
        if (hash != 0 && map.ContentHash != hash)
            Log($"Warning: your copy of '{name}' differs from the host's. Movement may stutter; get the host's map file.", true);
        if (initial)
            World.SetMap(map);
        else
            ApplyMapChange(map);
        return true;
    }

    /// <summary>
    /// "Reload map files" from the pause menu (server side only). Re-reads the Maps folder. In single player,
    /// the map you're on is rebuilt from its file straight away, so map makers can edit, reload and look.
    /// When hosting, people are connected with the version they loaded, so a changed map waits for the next match.
    /// </summary>
    public string ReloadMapFiles()
    {
        var r = Maps.Reload();
        var name = World.Map.Name;
        var msg = r.Summary;
        if (Maps.Find(name) is not { } fresh)
            msg += $". '{name}' is no longer in the Maps folder; this match carries on with the copy in memory.";
        else if (!r.IsChanged(name))
            World.SetMap(fresh); // identical geometry, just the new list's object
        else if (Net.IsOffline)
        {
            HotSwapMap(fresh);
            msg += $". Rebuilt '{name}' from its file.";
        }
        else
            msg += $". '{name}' changed; players keep the old version until the next match.";
        Log("Maps reloaded: " + msg, true);
        return msg;
    }

    /// <summary>
    /// Replaces the map under everyone's feet mid-match (single player only). Anyone now inside a wall is lifted
    /// onto it or pushed to the nearest open spot; bots pick fresh waypoints.
    /// </summary>
    public void HotSwapMap(Map fresh)
    {
        World.SetMap(fresh);
        ResetObjectives();
        foreach (var p in Players.Values)
        {
            p.Snaps.Clear();
            if (p.Bot != null)
                p.Bot.Waypoint = Vector3.Zero;
            if (!fresh.IsBlocked(p.Position))
                continue;
            var free = (Vector3?)null;
            for (var up = 0.5f; up <= 3f && free == null; up += 0.5f) // a raised floor: step up onto it
                if (!fresh.IsBlocked(p.Position + new Vector3(0, up, 0)))
                    free = p.Position + new Vector3(0, up, 0);
            free ??= fresh.NearestFree(p.Position) ?? (fresh.Spawns.Count > 0 ? fresh.Spawns[0] : Vector3.Zero);
            p.Position = free.Value;
            p.PrevPosition = free.Value;
            p.Sim.Velocity = Vector3.Zero;
        }
    }

    /// <summary>Switches every client to the voted map at the start of the next match.</summary>
    private void ApplyMapChange(Map map)
    {
        if (map == World.Map)
            return;
        World.SetMap(map);
        Log($"Next map: {map.Name}", true);
        foreach (var p in Players.Values)
        {
            p.Snaps.Clear();
            if (p.Bot != null)
                p.Bot.Waypoint = Vector3.Zero; // old map's waypoints are meaningless now
        }
        FitPlayersToMap(); // server: bots leave a smaller map, or come back on a bigger one (GameLobby.cs)
    }
}
