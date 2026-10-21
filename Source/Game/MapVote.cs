using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// End-of-match map vote (server-authoritative).
///   - When a match ends the server picks 3 maps from the pack of the map just played (True Random: from every
///     pack in True Random), one small, one medium, one large when possible, never the map just played, and
///     broadcasts them.
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

        // By name: after a map reload the playing map (and its pack) aren't in the lists as the same objects.
        var pack = (Maps.Find(World.Map.Name) ?? World.Map).Pack is { } playedPack ? Maps.FindPack(playedPack.Name) : null;
        var pool = FitForPeople(TrueRandom ? TrueRandomVotePool(World.Map.Name, Maps.Packs, Maps.All) : VotePool(World.Map.Name, pack, Maps.All),
            HumanCount); // never a map too small for the people here
        var picks = new List<Map>();
        // One of each size first, so there's always a choice of pace...
        foreach (var size in new[] { MapSize.Small, MapSize.Medium, MapSize.Large })
        {
            var ofSize = pool.Where(m => m.Size == size && !picks.Contains(m)).ToList();
            if (ofSize.Count > 0 && picks.Count < VoteChoices)
                picks.Add(ofSize[_rng.Next(ofSize.Count)]);
        }
        // ...then fill up with anything left.
        while (picks.Count < VoteChoices && picks.Count < pool.Count)
        {
            var m = pool[_rng.Next(pool.Count)];
            if (!picks.Contains(m))
                picks.Add(m);
        }
        VoteOptions = picks.OrderBy(m => m.Size).Select(m => m.Id).ToArray();
        VoteNames = VoteOptions.Select(id => Maps.Get(id).Name).ToArray();
        VoteTally = new byte[VoteOptions.Length];
        BroadcastVoteState();
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
        RecountVotes();
        var pick = PickVoteWinner(VoteTally, _rng);
        // By name, so a map reload between the vote and now picks up the current version of the winner.
        var winner = VoteNames.Length == VoteOptions.Length ? Maps.Find(VoteNames[pick]) : null;
        return winner ?? Maps.All[_rng.Next(Maps.All.Length)]; // the winner's file was removed meanwhile
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
                if (!VoteNames.SequenceEqual(names))
                    MyVote = -1; // a new ballot
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
