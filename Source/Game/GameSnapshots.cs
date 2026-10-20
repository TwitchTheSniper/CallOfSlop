using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Snapshots (server -> each client, 30 Hz): every player's movement, aim and health, plus slow fields (score,
/// loadout, perks) when they change. Wire format and packing in Snapshot.cs; sized for 32 players in one datagram,
/// split into parts when it isn't.
/// </summary>
public sealed partial class Game
{
    /// <summary>Snapshots between full refreshes of everyone's slow fields.</summary>
    public const int SlowRefreshEvery = 8;

    /// <summary>Ticks a changed slow field keeps being resent (covers lost packets).</summary>
    private const uint SlowResendTicks = 30;

    /// <summary>
    /// Relevance: players further than this from the recipient (and dead players) go out in every other snapshot
    /// (15 Hz, alternating halves), the rest in every one. Interpolation covers the gap; lag compensation runs on the
    /// server's own history, so hits are unaffected. Cuts a 32-player lobby's traffic by roughly a third.
    /// </summary>
    public const float NearDistance = 40f;

    /// <summary>Self-test: false sends everyone in every snapshot (to compare).</summary>
    public static bool SnapshotRelevance = true;

    private readonly List<SnapPlayer> _snapFast = new(Map.HardMaxPlayers);
    private readonly List<SnapPlayer> _snapMine = new(Map.HardMaxPlayers);
    private readonly List<SnapSlow> _snapSlow = new(Map.HardMaxPlayers);
    private readonly List<PacketWriter> _snapParts = new();
    private readonly SnapSlow[] _lastSlow = new SnapSlow[256];
    private readonly uint[] _slowChangedTick = new uint[256];
    private uint _lastPresent;
    private uint _snapCounter;

    /// <summary>Snapshot statistics (server): packets and payload bytes built, and the most parts one snapshot needed.</summary>
    public long SnapPackets, SnapBytes;

    public int SnapMaxParts, SnapMaxBytes;

    private static SnapPlayer FastOf(Player p) => new()
    {
        Id = p.Id, Position = p.Position, Velocity = p.Sim.Velocity, Yaw = p.Yaw, Pitch = p.Pitch,
        Health = (byte)Math.Clamp(p.Health, 0, 255), Alive = p.Alive, Grounded = p.Sim.Grounded, Stance = p.Sim.Stance,
        Weapon = p.Weapon, Boost = p.Sim.BoostTicks, MantleTicks = p.Sim.MantleTicks, MantleTo = p.Sim.MantleTo,
    };

    private static SnapSlow SlowOf(Player p) => new()
    {
        Id = p.Id, Kills = p.Kills, Deaths = p.Deaths, Primary = p.Primary, Secondary = p.Secondary,
        PrimaryKit = p.PrimaryKit, SecondaryKit = p.SecondaryKit, Perks = p.Perks,
    };

    /// <summary>Server, each snapshot tick: works out the shared entries once, then builds and sends each client's packets.</summary>
    private void SendSnapshots()
    {
        PrepareSnapshot();
        foreach (var (guid, id) in _clients.Values)
        {
            if (!Players.TryGetValue(id, out var p))
                continue;
            var n = BuildSnapshotFor(p);
            for (var i = 0; i < n; i++)
                Net.SendTo(guid, _snapParts[i].Span, PacketReliability.UnreliableSequenced);
        }
    }

    /// <summary>Fills the shared fast list and picks which slow entries go out this time.</summary>
    private void PrepareSnapshot()
    {
        _snapCounter++;
        _snapFast.Clear();
        _snapSlow.Clear();
        uint present = 0;
        foreach (var p in Players.Values)
        {
            _snapFast.Add(FastOf(p));
            present |= 1u << (p.Id & 31);
            var slow = SlowOf(p);
            var isNew = (_lastPresent & (1u << (p.Id & 31))) == 0;
            if (isNew || !slow.Equals(_lastSlow[p.Id]))
            {
                _lastSlow[p.Id] = slow;
                _slowChangedTick[p.Id] = ServerTick;
            }
            if (ServerTick - _slowChangedTick[p.Id] <= SlowResendTicks || _snapCounter % SlowRefreshEvery == p.Id % SlowRefreshEvery)
                _snapSlow.Add(slow);
        }
        _lastPresent = present;
    }

    /// <summary>Builds one recipient's packets into <see cref="_snapParts"/>; returns how many.</summary>
    private int BuildSnapshotFor(Player p)
    {
        var header = new SnapHeader
        {
            Tick = ServerTick, AckSeq = p.LastProcessedSeq, Phase = Phase, TimeLeft = (float)TimeLeft, Winner = Winner, Present = _lastPresent,
        };
        var own = new SnapOwn
        {
            Position = p.Position, Velocity = p.Sim.Velocity, MantleTo = p.Sim.MantleTo, Mantling = p.Sim.MantleTicks > 0, Slow = SlowOf(p),
        };
        // This recipient's selection: near and alive every time, the rest on alternate snapshots.
        _snapMine.Clear();
        foreach (var f in _snapFast)
            if (!SnapshotRelevance || f.Id == p.Id || (f.Alive && System.Numerics.Vector3.DistanceSquared(f.Position, p.Position) < NearDistance * NearDistance)
                || (_snapCounter + f.Id) % 2 == 0)
                _snapMine.Add(f);
        var ownIndex = _snapMine.FindIndex(f => f.Id == p.Id);
        var n = Snapshot.Build(header, own, _snapMine, ownIndex, _snapSlow, _snapParts);
        SnapPackets += n;
        SnapMaxParts = Math.Max(SnapMaxParts, n);
        for (var i = 0; i < n; i++)
        {
            SnapBytes += _snapParts[i].Length;
            SnapMaxBytes = Math.Max(SnapMaxBytes, _snapParts[i].Length);
        }
        return n;
    }

    /// <summary>
    /// Self-test: the snapshot packets this server would send <paramref name="recipient"/> right now (copies), as if
    /// it were a connected client. Counts towards the snapshot statistics.
    /// </summary>
    public List<byte[]> SelfTestSnapshotFor(Player recipient, bool prepare = true)
    {
        if (prepare)
            PrepareSnapshot(); // false: the same snapshot again (e.g. built another way to compare)
        var n = BuildSnapshotFor(recipient);
        var list = new List<byte[]>(n);
        for (var i = 0; i < n; i++)
            list.Add(_snapParts[i].Span.ToArray());
        return list;
    }

    /// <summary>Self-test: feed a snapshot packet (with its message id) to this client as if it came from the server.</summary>
    public void SelfTestReceive(ReadOnlySpan<byte> packet, double now)
    {
        var r = new PacketReader(packet[1..]);
        ReadState(ref r, now);
    }

    // ---- Client ------------------------------------------------------------------------------------

    private readonly Snapshot.Packet _snapIn = new();
    private int _snapPartsSeen;

    private void ReadState(ref PacketReader r, double now)
    {
        if (!Snapshot.Read(ref r, _snapIn))
            return;
        var h = _snapIn.Header;
        var tick = h.Tick;
        if (_clockStarted && (tick < ServerTick || (tick == ServerTick && (_snapPartsSeen & (1 << h.Part)) != 0)))
            return; // old, or a part we already have
        if (!_clockStarted || tick > ServerTick)
        {
            _snapPartsSeen = 0;
            var wasCountdown = Phase == MatchPhase.Countdown;
            if (Phase == MatchPhase.Warmup && h.Phase == MatchPhase.Countdown)
                _countdownStartedFlag = true; // everyone's ready: the countdown opens the loadout screen
            Phase = h.Phase;
            TimeLeft = h.TimeLeft;
            if (wasCountdown && h.Phase == MatchPhase.Playing)
                OnPlayStarted();
            Winner = h.Winner;
            ServerTick = tick;
            OnSnapshotTiming(tick, now);
            // Whoever isn't in the presence mask has left.
            List<byte>? gone = null;
            foreach (var id in Players.Keys)
                if (id >= 32 || (h.Present & (1u << id)) == 0)
                    (gone ??= new List<byte>()).Add(id);
            if (gone != null)
                foreach (var id in gone)
                    Players.Remove(id);
        }
        _snapPartsSeen |= 1 << h.Part;

        foreach (var f in _snapIn.Fast)
            GetOrAddRemote(f.Id, f); // new players first, so their slow fields have somewhere to go
        // Slow fields next (the local player's loadout decides what a respawn equips).
        if (_snapIn.Own is { } own)
            ApplySlow(own.Slow with { Id = LocalId });
        foreach (var s in _snapIn.Slow)
            if (s.Id != LocalId || _snapIn.Own == null)
                ApplySlow(s);

        foreach (var f in _snapIn.Fast)
        {
            var e = f;
            var p = GetOrAddRemote(e.Id, e);
            var wasAlive = p.Alive;
            p.Health = e.Health;
            p.Alive = e.Alive;
            if (e.Id == LocalId)
            {
                if (_snapIn.Own is { } o)
                {
                    e.Position = o.Position; // exact, so prediction doesn't chase rounding errors
                    e.Velocity = o.Velocity;
                    if (o.Mantling)
                        e.MantleTo = o.MantleTo;
                }
                var respawned = !_localInitialized || (e.Alive && !wasAlive);
                _localInitialized = true;
                if (respawned)
                {
                    p.Yaw = e.Yaw;
                    p.Pitch = e.Pitch;
                    OnLocalSpawn(p);
                }
                else
                {
                    SyncLocalSlots(p, refill: false); // loadout applied mid-life (out of combat)
                }
                SteerMoverClock(tick, h.AckSeq);
                Reconcile(p, h.AckSeq, MoveOf(e, p.Perks), e.Alive, respawned);
            }
            else
            {
                p.Weapon = e.Weapon;
                p.Sim = MoveOf(e, p.Perks);
                p.Yaw = e.Yaw;
                p.Pitch = e.Pitch;
                if (e.Alive && !wasAlive)
                    p.Snaps.Clear();
                p.Snaps.Add(new Snap { Tick = tick, Position = e.Position, Velocity = e.Velocity, Yaw = e.Yaw, Pitch = e.Pitch, Stance = e.Stance });
                if (p.Snaps.Count > 64)
                    p.Snaps.RemoveAt(0);
            }
        }
    }

    private static MoveState MoveOf(in SnapPlayer e, PerkSet perks) => new()
    {
        Position = e.Position, Velocity = e.Velocity, Grounded = e.Grounded, Stance = e.Stance, MantleTicks = e.MantleTicks, MantleTo = e.MantleTo,
        MovePerk = perks.Movement, BoostTicks = e.Boost,
    };

    private Player GetOrAddRemote(byte id, in SnapPlayer e)
    {
        if (Players.TryGetValue(id, out var p))
            return p;
        p = new Player { Id = id, Yaw = e.Yaw, Pitch = e.Pitch, Name = _names.GetValueOrDefault(id) ?? "" };
        p.Teleport(e.Position);
        Players[id] = p;
        return p;
    }

    private void ApplySlow(in SnapSlow s)
    {
        if (!Players.TryGetValue(s.Id, out var p))
            return; // their movement is in another part of this snapshot; the slow fields come round again soon
        p.Kills = s.Kills;
        p.Deaths = s.Deaths;
        p.Primary = s.Primary;
        p.Secondary = s.Secondary;
        p.PrimaryKit = s.PrimaryKit;
        p.SecondaryKit = s.SecondaryKit;
        p.Perks = s.Perks;
    }
}
