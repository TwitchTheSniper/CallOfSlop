using System.Numerics;

namespace CallOfSlop;

/// <summary>A crew member's job (docs/sandbox/design/bot-brain.md, section 6). Phase 1: the helm, the guns and the spares who wait to take one.</summary>
public enum CrewJob : byte
{
    Spare,
    Pilot,
    Gunner,
}

/// <summary>What a crew member is doing.</summary>
public enum CrewState : byte
{
    Free, // aboard with no station (a spare with no couch)
    Station, // at its station (a helm or a gun works; a couch rests)
    Walking, // on the way to a station (a timed walk: CrewWorld.WalkSpeed)
    Dead, // killed; the slot is freed once a body that went with it has gone
}

/// <summary>How much a crewed body costs (bot-brain.md section 4): a bot <see cref="Player"/> per member near a person, a record at its seat further off, nothing asleep.</summary>
public enum CrewTier : byte
{
    Asleep,
    Seated,
    Bodied,
}

/// <summary>What the captain wants of its body (bot-brain.md section 3), from the faction's rules and what it sees.</summary>
public enum CrewGoal : byte
{
    Hold, // nothing to fight: hold, patrol or follow as its drivers do
    Engage,
    Flee,
}

/// <summary>What a station is for.</summary>
public enum CrewStationKind : byte
{
    Helm,
    Gun,
    Rest,
}

/// <summary>The game's side of the crew (Game/GameCrew.cs): bot bodies, people's targets. Null in a check with no game: nobody is bodied.</summary>
public interface ICrewHost
{
    /// <summary>A bot <see cref="Player"/> for member <paramref name="m"/>, sitting in seat <paramref name="part"/> of <paramref name="c"/>; 0: no body free.</summary>
    byte Promote(CrewWorld crew, int m, Construct c, int part);

    /// <summary>The body goes (the record stays).</summary>
    void Demote(byte player);

    bool Alive(byte player);

    /// <summary>A body's health as a share of a player's whole (0-1); -1: no such body.</summary>
    float HealthOf(byte player);

    /// <summary>The body sits in <paramref name="part"/> of <paramref name="c"/>, out of whatever seat it was in. False: it couldn't.</summary>
    bool Sit(byte player, Construct c, int part);

    /// <summary>The target crew <paramref name="s"/> on <paramref name="c"/> fights this tick, from the contact its last look picked (<see cref="CrewShip.Contact"/>, <see cref="CrewShip.ContactBody"/>); <paramref name="look"/>: look again.</summary>
    bool Target(Construct c, CrewShip s, bool look, out ShipAiTarget t);

    /// <summary>Player <paramref name="player"/> did something to faction <paramref name="faction"/>: its standing with them moves by <paramref name="amount"/>.</summary>
    void Deed(byte faction, byte player, int amount);
}

/// <summary>
/// One crewed body's roster and stations (pooled: a body that loses its crew hands it back). Its members are indices into the crew's
/// struct-of-arrays records (<see cref="CrewWorld"/>); its stations come from the construct's seats, guns and flight computer, scanned again
/// when the construct is rebuilt. Fixed arrays: nothing allocated once made.
/// </summary>
public sealed class CrewShip
{
    public ushort Body;
    public ShipAiMode Mode;
    public bool Air; // an aircraft flown by an AircraftPilot (GameSandboxAircraft.cs): the helm is gated, nothing else here drives it
    public CrewTier Tier;
    public ShipAi? Ai; // the helm and gun drivers (Karl's flying and gunnery), while it is awake and a driver is free
    public readonly int[] Members = new int[CrewWorld.MaxCrewPerBody];
    public int MemberCount;

    // Stations: the seat part, its stable tag, its kind, who is at it (a member index, -1) and the part's health last seen.
    public readonly short[] StPart = new short[CrewWorld.MaxCrewPerBody];
    public readonly ushort[] StTag = new ushort[CrewWorld.MaxCrewPerBody];
    public readonly CrewStationKind[] StKind = new CrewStationKind[CrewWorld.MaxCrewPerBody];
    public readonly int[] StMember = new int[CrewWorld.MaxCrewPerBody];
    public readonly float[] StHealth = new float[CrewWorld.MaxCrewPerBody];
    public int StCount;
    public int Helm = -1; // the helm's station index
    public int Stamp = -1, PartsSeen = -1; // (the construct's RebuildStamp and part count when its stations were scanned)
    public long GraceUntil; // the tick an empty helm stops keeping its last input
    public bool FromSetting; // made by its Flight Stabilizer's AI crew setting: it stands down when the setting goes off
    public bool BodiesShort; // the last try for a body found none (logged once)
    public bool AirOff; // an aircraft's autopilot was let go for want of a pilot (GameSandboxAircraft.cs)
    public bool HelmLost; // the helm emptied and nobody has taken it (an aircraft's pilot let go of the autopilot once)
    public bool SeatsDirty = true; // the stand-ins clients draw changed (GameCrew.cs sends them)
    public float Near = float.MaxValue; // m to the nearest person, last look
    public byte Contact = 255; // the player its look picked (255: none)
    public ushort ContactBody; // ... or another faction's crewed body (0: none)
    public int Slot; // its index in CrewWorld.Ships

    // The captain (phase 3): its faction kind, morale, goal, who has attacked it, and when it thinks next.
    public byte Faction; // CrewFactions id (0: none: the old Fights strangers / everyone rules)
    public float Morale = 1, Hull = 1, HullMax; // morale 0-1; the hull's share left of HullMax (its parts' health when crewed)
    public CrewGoal Goal;
    public byte Lost; // members lost since it was crewed
    public long CaptainAt; // the tick it thinks next
    public readonly ulong[] Foes = new ulong[4]; // players who have hit it (a bit each)

    public bool Foe(byte player) => (Foes[player >> 6] & (1UL << (player & 63))) != 0;
    public void MarkFoe(byte player) => Foes[player >> 6] |= 1UL << (player & 63);

    public bool HelmOn(CrewWorld w) => Helm >= 0 && StMember[Helm] is var m and >= 0 && w.State[m] == CrewState.Station;
    public bool InGrace(long tick) => tick < GraceUntil;

    /// <summary>A living member works seat <paramref name="part"/> (the rule-1 gate: bot-brain.md section 3).</summary>
    public bool Manned(CrewWorld w, int part)
    {
        for (var s = 0; s < StCount; s++)
            if (StPart[s] == part)
                return StMember[s] is var m and >= 0 && w.State[m] == CrewState.Station;
        return false;
    }

    public void Clear()
    {
        MemberCount = 0;
        StCount = 0;
        Helm = -1;
        Stamp = PartsSeen = -1;
        GraceUntil = 0;
        HelmLost = false;
        AirOff = false;
        FromSetting = false;
        SeatsDirty = true;
        Near = float.MaxValue;
        Contact = 255;
        ContactBody = 0;
        Ai = null;
        Air = false;
        Tier = CrewTier.Asleep;
        Faction = 0;
        Morale = Hull = 1;
        HullMax = 0;
        Goal = CrewGoal.Hold;
        Lost = 0;
        CaptainAt = 0;
        Array.Clear(Foes);
    }
}

/// <summary>
/// The crews of every crewed body in a world (docs/sandbox/design/bot-brain.md, phase 1), server only. Members are records in arrays
/// (struct of arrays, thousands of them; no object per member), each at a station of its body. A gate: a faction body's helm and guns
/// are worked only while a living member sits at them, so shooting the pilot stops the ship (after a few seconds of its last input,
/// <see cref="HelmGraceTicks"/>) and shooting a gunner silences its gun, until a spare walks over and takes the seat.
///
/// Cost is fixed per tick whatever the count: a slice of the world's bodies is looked at for new crews (<see cref="FindPerTick"/>),
/// a slice of the crewed bodies is thought about (<see cref="ShipsPerTick"/>: stations, hits, tiers, who takes an empty seat), and
/// only bodies within <see cref="SeatedRange"/> of a person have a driver (at most <see cref="MaxDrivers"/>). Near a person
/// (<see cref="BodyRange"/>) members become bot players in their seats (at most <see cref="MaxBodied"/>). Nothing allocated in a steady tick.
/// </summary>
public sealed class CrewWorld
{
    public const int MaxCrewPerBody = 32, MaxDrivers = 64, ShipsPerTick = 48, FindPerTick = 64, MaxBodiedCap = 64;
    public const float BodyRange = 300, BodyDrop = 380, SeatedRange = 3000, SeatedDrop = 3200;
    /// <summary>An empty helm keeps its last input this long (ticks); a seated crew member's walk to a station, in m/s, and at least this long (ticks).</summary>
    public const int HelmGraceTicks = 3 * World.TickRate, MinWalkTicks = 2 * World.TickRate, GoneTicks = 3 * World.TickRate;
    public const float WalkSpeed = 1.6f, MemberHealth = 100;
    /// <summary>A station part's lost health hurts its member this much per whole part lost (a part half shot away kills).</summary>
    public const float HitShare = 2f * MemberHealth;

    /// <summary>Bodies at once (a bot player each, out of the spawned bots' ids).</summary>
    public int MaxBodied = 32;

    // ---- The records (struct of arrays) ------------------------------------------------------------------------------------------
    public int High; // one past the highest slot used
    public int Live; // slots in use
    public uint[] Uid = [];
    public ushort[] Body = [];
    public CrewJob[] Job = [];
    public CrewState[] State = [];
    public float[] Health = [];
    public byte[] Skill = [];
    public ushort[] Tag = []; // its station's part tag (0: none)
    public short[] Station = []; // its station's index on its ship (-1: none)
    public byte[] Player = []; // its body's player id + 1 (0: no body)
    public long[] Ready = []; // a walk's end, or a dead body's going (tick)
    public ushort[] Name = [];
    public int[] Ship = []; // its ship's slot in Ships
    /// <summary>Player ids that are crew bodies (a seat they sit in is still the crew's).</summary>
    public readonly bool[] IsBody = new bool[256];
    private int[] _free = [];
    private int _freeCount;
    private uint _nextUid = 1;

    // ---- The crewed bodies ---------------------------------------------------------------------------------------------------------
    public CrewShip?[] Ships = new CrewShip?[64];
    public int ShipHigh, ShipCount;
    private readonly Dictionary<ushort, int> _shipOf = new(256);
    private readonly Stack<CrewShip> _pool = new();
    private readonly Stack<ShipAi> _aiPool = new();
    private int _aiOut;
    private readonly int[] _bodied = new int[MaxBodiedCap];
    private int _bodiedCount;
    private int _findAt, _shipAt;
    public long Tick;

    /// <summary>Gone bodies' records, for the stand-ins' network to clear (GameCrew.cs reads and empties it).</summary>
    public readonly List<ushort> GoneBodies = new(64);

    /// <summary>Lines for a log (made only on a change, never in a steady tick).</summary>
    public readonly List<string> Events = new(32);

    /// <summary>What the crew's ticks have cost: CPU (ms), bytes allocated, ticks.</summary>
    public double CpuMs, WorstMs;
    public long WorstTick;
    public double WorstThinkMs, WorstDriveMs; // (the worst tick's slices of thinking, and of the bodies' deaths plus the drivers)

    /// <summary>Bodies made at most this many a tick (a roster near a person comes to life over a few ticks, not 32 spawns in one).</summary>
    public const int PromotePerTick = 2;
    private int _promoted;
    public long Bytes;
    public int Ticks;

    public CrewWorld() => Grow(256);

    private void Grow(int n)
    {
        var was = Uid.Length;
        Array.Resize(ref Uid, n);
        Array.Resize(ref Body, n);
        Array.Resize(ref Job, n);
        Array.Resize(ref State, n);
        Array.Resize(ref Health, n);
        Array.Resize(ref Skill, n);
        Array.Resize(ref Tag, n);
        Array.Resize(ref Station, n);
        Array.Resize(ref Player, n);
        Array.Resize(ref Ready, n);
        Array.Resize(ref Name, n);
        Array.Resize(ref Ship, n);
        Array.Resize(ref _free, n);
        for (var i = was; i < n; i++)
            Ship[i] = -1;
    }

    public bool Has(ushort body) => _shipOf.ContainsKey(body);
    public CrewShip? ShipOf(ushort body) => _shipOf.TryGetValue(body, out var s) ? Ships[s] : null;

    /// <summary>Members alive in the world, and how many have bodies.</summary>
    public int Bodied => _bodiedCount;

    // ---- Making and losing crews ---------------------------------------------------------------------------------------------------

    /// <summary>A roster for <paramref name="c"/> (none yet): one member per station it can work now, and spares (<paramref name="spares"/>; -1: by its couches).</summary>
    public CrewShip? Crew(Construct c, ShipAiMode mode, bool air = false, int spares = -1, byte faction = 0)
    {
        if (_shipOf.ContainsKey(c.Id))
            return ShipOf(c.Id);
        var s = NewShip(c.Id, mode, air);
        SetFaction(c, s, faction);
        Scan(c, s);
        var def = CrewFactions.Of(faction);
        if (spares < 0 && def != null)
            spares = def.SparesMin + (int)(c.Id * 2654435761u % (uint)(def.SparesMax - def.SparesMin + 1));
        var rest = 0;
        for (var k = 0; k < s.StCount; k++)
        {
            if (s.StKind[k] == CrewStationKind.Rest)
            {
                rest++;
                continue;
            }
            Add(s, s.StKind[k] == CrewStationKind.Helm ? CrewJob.Pilot : CrewJob.Gunner, k);
        }
        var want = spares >= 0 ? spares : Math.Max(1, Math.Min(rest, 2 + (s.StCount - rest) / 2));
        for (var k = 0; k < s.StCount && want > 0 && s.MemberCount < MaxCrewPerBody; k++)
            if (s.StKind[k] == CrewStationKind.Rest)
            {
                Add(s, CrewJob.Spare, k);
                want--;
            }
        for (; want > 0 && s.MemberCount < MaxCrewPerBody; want--)
            Add(s, CrewJob.Spare, -1); // (no couch: aboard, waiting)
        if (def != null)
            for (var i = 0; i < s.MemberCount; i++)
                Skill[s.Members[i]] = (byte)(def.SkillMin + (Uid[s.Members[i]] * 40503u) % (uint)(def.SkillMax - def.SkillMin + 1));
        Events.Add($"construct {c.Id}: a crew of {s.MemberCount} took it ({mode}{(air ? ", aircraft" : "")}{(def != null ? $", {def.Name}" : "")})");
        return s;
    }

    /// <summary>Its faction kind (0: none), on the roster and the construct, and the hull it starts from.</summary>
    public static void SetFaction(Construct c, CrewShip s, byte faction)
    {
        s.Faction = faction;
        c.NpcFaction = faction;
        s.Morale = CrewFactions.Of(faction)?.MoraleStart ?? 1;
        s.HullMax = HullOf(c);
        s.Hull = 1;
    }

    /// <summary>Its parts' health, whole (O(parts): the captain's look, once a second at most).</summary>
    public static float HullOf(Construct c)
    {
        var h = 0f;
        for (var i = 0; i < c.PartCount; i++)
            if (!c.Parts[i].Broken)
                h += c.Parts[i].Health;
        return h;
    }

    private CrewShip NewShip(ushort body, ShipAiMode mode, bool air)
    {
        var s = _pool.Count > 0 ? _pool.Pop() : new CrewShip();
        s.Clear();
        s.Body = body;
        s.Mode = mode;
        s.Air = air;
        var slot = -1;
        for (var i = 0; i < ShipHigh; i++)
            if (Ships[i] == null)
            {
                slot = i;
                break;
            }
        if (slot < 0)
        {
            if (ShipHigh == Ships.Length)
                Array.Resize(ref Ships, Ships.Length * 2);
            slot = ShipHigh++;
        }
        Ships[slot] = s;
        s.Slot = slot;
        _shipOf[body] = slot;
        ShipCount++;
        return s;
    }

    /// <summary>A new member on <paramref name="s"/> at station <paramref name="station"/> (-1: none). -1: the roster is full.</summary>
    public int Add(CrewShip s, CrewJob job, int station, uint uid = 0, float health = MemberHealth, byte skill = 128, ushort name = 0xFFFF)
    {
        if (s.MemberCount >= MaxCrewPerBody)
            return -1;
        int m;
        if (_freeCount > 0)
            m = _free[--_freeCount];
        else
        {
            if (High == Uid.Length)
                Grow(Uid.Length * 2);
            m = High++;
        }
        Uid[m] = uid != 0 ? uid : _nextUid++;
        if (Uid[m] >= _nextUid)
            _nextUid = Uid[m] + 1;
        Body[m] = s.Body;
        Ship[m] = s.Slot;
        Job[m] = job;
        Health[m] = health;
        Skill[m] = skill;
        Player[m] = 0;
        Ready[m] = 0;
        Name[m] = name != 0xFFFF ? name : (ushort)(Uid[m] * 7919 % 65521);
        Station[m] = -1;
        Tag[m] = 0;
        State[m] = CrewState.Free;
        if (station >= 0 && station < s.StCount && s.StMember[station] < 0)
            Seat(s, m, station);
        s.Members[s.MemberCount++] = m;
        Live++;
        s.SeatsDirty = true;
        return m;
    }

    private void Seat(CrewShip s, int m, int station)
    {
        s.StMember[station] = m;
        Station[m] = (short)station;
        Tag[m] = s.StTag[station];
        State[m] = CrewState.Station;
        if (station == s.Helm)
            s.HelmLost = false;
        s.SeatsDirty = true;
    }

    /// <summary>The member leaves its station (if it has one): an empty helm keeps its last input a while.</summary>
    private void Unseat(CrewShip s, int m)
    {
        var st = Station[m];
        if (st >= 0 && st < s.StCount && s.StMember[st] == m)
        {
            s.StMember[st] = -1;
            if (st == s.Helm)
                s.GraceUntil = Tick + HelmGraceTicks;
        }
        Station[m] = -1;
        s.SeatsDirty = true;
    }

    /// <summary>A person takes the member's seat: it leaves its station and finds another place at the next look (its body is stood up by the game).</summary>
    public void StandAside(int m)
    {
        if (m < 0 || m >= High || Ship[m] < 0 || Ships[Ship[m]] is not { } s || State[m] == CrewState.Dead)
            return;
        Unseat(s, m);
        State[m] = CrewState.Free;
    }

    /// <summary>The member is hurt (<paramref name="amount"/> health); at nothing left it dies, and leaves its station.</summary>
    public void Hurt(int m, float amount, ICrewHost? host)
    {
        if (m < 0 || m >= High || State[m] == CrewState.Dead || Ship[m] < 0)
            return;
        Health[m] -= amount;
        if (Health[m] <= 0)
            Kill(m, host);
    }

    /// <summary>The member dies. <paramref name="bodyDied"/>: its body was killed as a player is (it lies a while, then goes); else a body goes at once with the record.</summary>
    public void Kill(int m, ICrewHost? host, bool bodyDied = false)
    {
        if (State[m] == CrewState.Dead || Ship[m] < 0 || Ships[Ship[m]] is not { } s)
            return;
        Unseat(s, m);
        Health[m] = 0;
        State[m] = CrewState.Dead;
        if (s.Lost < 255)
            s.Lost++;
        Ready[m] = Tick + GoneTicks;
        if (!bodyDied)
            Remove(s, m, host);
    }

    /// <summary>A member's record goes (its body with it).</summary>
    private void Remove(CrewShip s, int m, ICrewHost? host)
    {
        if (State[m] != CrewState.Dead)
            Unseat(s, m);
        DropBody(m, host);
        for (var i = 0; i < s.MemberCount; i++)
            if (s.Members[i] == m)
            {
                s.Members[i] = s.Members[--s.MemberCount];
                break;
            }
        Ship[m] = -1;
        _free[_freeCount++] = m;
        Live--;
        s.SeatsDirty = true;
    }

    private void DropBody(int m, ICrewHost? host)
    {
        if (Player[m] == 0)
            return;
        IsBody[Player[m] - 1] = false;
        if (host != null && State[m] != CrewState.Dead && host.HealthOf((byte)(Player[m] - 1)) is var f and >= 0)
            Health[m] = MathF.Max(1, f * MemberHealth); // (its body's wounds go back to its record)
        host?.Demote((byte)(Player[m] - 1));
        Player[m] = 0;
        for (var i = 0; i < _bodiedCount; i++)
            if (_bodied[i] == m)
            {
                _bodied[i] = _bodied[--_bodiedCount];
                break;
            }
    }

    /// <summary>The whole crew goes (the setting went off, the body went or was wrecked).</summary>
    public void Disband(CrewShip s, ConstructWorld? w, ICrewHost? host, string why)
    {
        while (s.MemberCount > 0)
            Remove(s, s.Members[s.MemberCount - 1], host);
        if (s.Ai != null)
        {
            if (w?.Get(s.Body) is { } c)
                s.Ai.Release(c);
            FreeAi(s);
        }
        Events.Add($"construct {s.Body}: its crew {why}");
        _shipOf.Remove(s.Body);
        Ships[s.Slot] = null;
        ShipCount--;
        GoneBodies.Add(s.Body);
        _pool.Push(s);
    }

    private void FreeAi(CrewShip s)
    {
        if (s.Ai == null)
            return;
        s.Ai.Crew = null;
        _aiPool.Push(s.Ai);
        s.Ai = null;
        _aiOut--;
    }

    // ---- Stations ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Its stations from the construct as it is: the helm (the seat its flight computer reads, else its drive seat), each seat that
    /// works a gun, then every other seat (where spares wait). Members keep their station by part tag; a member whose station has gone
    /// dies with it (the user's answer 3). Once per rebuild.
    /// </summary>
    private void Scan(Construct c, CrewShip s, ICrewHost? host = null)
    {
        s.Stamp = c.RebuildStamp;
        s.PartsSeen = c.PartCount;
        Span<ushort> was = stackalloc ushort[MaxCrewPerBody];
        Span<int> who = stackalloc int[MaxCrewPerBody];
        var wasCount = s.StCount;
        var wasHelm = s.Helm;
        for (var k = 0; k < wasCount; k++)
        {
            was[k] = s.StTag[k];
            who[k] = s.StMember[k];
        }
        s.StCount = 0;
        s.Helm = -1;
        var stab = c.StabCount > 0 && c.Stabs[0].Part < c.PartCount ? c.Stabs[0].Part : -1;
        var helm = stab >= 0 ? ConstructFlight.SeatFor(c, stab) : c.DriveSeat;
        if (helm >= 0 && helm < c.PartCount && Usable(c, helm))
        {
            s.Helm = 0;
            AddStation(c, s, helm, CrewStationKind.Helm);
        }
        for (var k = 0; k < c.GunCount && s.StCount < MaxCrewPerBody; k++)
        {
            int seat = c.Guns[k].Seat;
            if (seat >= 0 && seat < c.PartCount && seat != helm && Usable(c, seat) && IndexOf(s, seat) < 0)
                AddStation(c, s, seat, CrewStationKind.Gun);
        }
        for (var i = 0; i < c.PartCount && s.StCount < MaxCrewPerBody; i++)
            if (i != helm && c.Parts[i].D.Role == PartRole.Seat && Usable(c, i) && IndexOf(s, i) < 0)
                AddStation(c, s, i, CrewStationKind.Rest);
        // Members back at their stations by tag; a member whose station went goes down with it.
        for (var k = 0; k < wasCount; k++)
        {
            var m = who[k];
            if (m < 0 || State[m] == CrewState.Dead)
                continue;
            var now = -1;
            for (var j = 0; j < s.StCount; j++)
                if (s.StTag[j] == was[k] && was[k] != 0)
                {
                    now = j;
                    break;
                }
            if (now >= 0)
            {
                s.StMember[now] = m;
                Station[m] = (short)now;
            }
            else
            {
                Station[m] = -1;
                if (k == wasHelm)
                    s.GraceUntil = Tick + HelmGraceTicks;
                Events.Add($"construct {c.Id}: crew member {Uid[m]} went down with their station");
                Kill(m, host);
            }
        }
        // A walker's goal by tag too (its station's index may have moved).
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (State[m] == CrewState.Walking)
                Station[m] = (short)TagIndex(s, Tag[m]);
        }
        s.SeatsDirty = true;
    }

    private static bool Usable(Construct c, int part) => !c.Parts[part].Broken && c.Parts[part].D.Role == PartRole.Seat && !c.Parts[part].D.RemoteReceiver && !c.Parts[part].D.LifterCab;

    private static int IndexOf(CrewShip s, int part)
    {
        for (var k = 0; k < s.StCount; k++)
            if (s.StPart[k] == part)
                return k;
        return -1;
    }

    private static int TagIndex(CrewShip s, ushort tag)
    {
        if (tag == 0)
            return -1;
        for (var k = 0; k < s.StCount; k++)
            if (s.StTag[k] == tag)
                return k;
        return -1;
    }

    private static void AddStation(Construct c, CrewShip s, int part, CrewStationKind kind)
    {
        var k = s.StCount++;
        s.StPart[k] = (short)part;
        s.StTag[k] = c.Parts[part].Tag;
        s.StKind[k] = kind;
        s.StMember[k] = -1;
        s.StHealth[k] = c.Parts[part].Health;
    }

    // ---- The tick --------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// One tick, after the people's seats and before the constructs step (server). <paramref name="people"/>: where the people are (the
    /// tiers go by them). <paramref name="crewOn"/>: whether the Flight Stabilizer's AI crew setting makes new crews (a check makes its own).
    /// </summary>
    public void Step(ConstructWorld w, IWorldQuery q, ICrewHost? host, ReadOnlySpan<Vector3> people, bool crewOn = true)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        Tick++;
        _promoted = 0;
        if (crewOn)
            FindSlice(w);
        var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        ShipSlice(w, host, people);
        var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        BodiedTick(w, host);
        Drive(w, q, host);
        var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Ticks > 10)
        {
            WorstThinkMs = Math.Max(WorstThinkMs, (t2 - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            WorstDriveMs = Math.Max(WorstDriveMs, (t3 - t2) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }
        var b1 = GC.GetAllocatedBytesForCurrentThread();
        if (Events.Count == 0)
            Bytes += b1 - b0;
        var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        CpuMs += ms;
        if (Ticks > 10 && ms > WorstMs)
        {
            WorstMs = ms;
            WorstTick = Tick;
        }
        Ticks++;
    }

    /// <summary>A slice of the world's bodies: a Flight Stabilizer set to an AI crew gets one; a body from a save brings its own.</summary>
    private void FindSlice(ConstructWorld w)
    {
        var n = w.Bodies.Count;
        if (n == 0)
            return;
        for (var k = 0; k < Math.Min(FindPerTick, n); k++)
        {
            if (_findAt >= n)
                _findAt = 0;
            var c = w.Bodies[_findAt++];
            if (c.CrewLoad is { } bytes)
            {
                c.CrewLoad = null;
                if (!_shipOf.ContainsKey(c.Id))
                    Load(c, bytes);
                continue;
            }
            if (c.StabCount == 0 || c.Wreck || c.SpaceShip != 0 || _shipOf.ContainsKey(c.Id))
                continue;
            var mode = ShipAi.ModeOf(c);
            if (mode != ShipAiMode.Off && Crew(c, mode, faction: mode == ShipAiMode.Everyone ? CrewFactions.Find("Test Hostile") : c.Owner == 255 ? CrewFactions.Find("Mercenaries") : (byte)0) is { } made)
                made.FromSetting = true;
        }
    }

    /// <summary>A slice of the crewed bodies: gone or wrecked ones lose their crew, stations follow rebuilds, hit stations hurt their members, tiers follow the people, and spares fill empty seats.</summary>
    private void ShipSlice(ConstructWorld w, ICrewHost? host, ReadOnlySpan<Vector3> people)
    {
        if (ShipHigh == 0)
            return;
        var n = Math.Min(ShipsPerTick, ShipHigh);
        for (var k = 0; k < n; k++)
        {
            if (_shipAt >= ShipHigh)
                _shipAt = 0;
            if (Ships[_shipAt++] is { } s)
                Think(w, host, s, people);
        }
    }

    private void Think(ConstructWorld w, ICrewHost? host, CrewShip s, ReadOnlySpan<Vector3> people)
    {
        var c = w.Get(s.Body);
        if (c == null || c.Wreck)
        {
            Disband(s, w, host, c == null ? "went with its body" : "died with its ship");
            return;
        }
        if (s.FromSetting && c.StabCount > 0 && ShipAi.ModeOf(c) is var mode && mode != s.Mode)
        {
            if (mode == ShipAiMode.Off)
            {
                Disband(s, w, host, "stood down");
                return;
            }
            s.Mode = mode;
        }
        if (c.Dirty)
            c.Rebuild();
        if (s.Stamp != c.RebuildStamp || s.PartsSeen != c.PartCount)
            Scan(c, s, host);
        // Hits: a station part broken takes its member with it; a part shot about hurts them by the share it lost (bodies are hit as players are).
        for (var k = 0; k < s.StCount; k++)
        {
            int part = s.StPart[k];
            if (part >= c.PartCount)
                continue;
            ref var p = ref c.Parts[part];
            var m = s.StMember[k];
            if (m >= 0 && Player[m] == 0)
            {
                if (p.Broken)
                {
                    Events.Add($"construct {c.Id}: crew member {Uid[m]} died with their station");
                    Kill(m, host);
                }
                else if (p.Health < s.StHealth[k] - 0.01f && p.D.Health > 0)
                    Hurt(m, (s.StHealth[k] - p.Health) / p.D.Health * HitShare, host);
            }
            s.StHealth[k] = p.Health;
        }
        // The dead go once their body has.
        for (var i = s.MemberCount - 1; i >= 0; i--)
        {
            var m = s.Members[i];
            if (State[m] == CrewState.Dead && Tick >= Ready[m])
                Remove(s, m, host);
        }
        // How near the people are, with hysteresis.
        var near = float.MaxValue;
        var com = c.WorldCom;
        foreach (var at in people)
            near = MathF.Min(near, Vector3.Distance(at, com));
        s.Near = near;
        var tier = c.SpaceShip != 0 ? CrewTier.Asleep
            : near < BodyRange || s.Tier == CrewTier.Bodied && near < BodyDrop ? CrewTier.Bodied
            : near < SeatedRange || s.Tier != CrewTier.Asleep && near < SeatedDrop ? CrewTier.Seated
            : CrewTier.Asleep;
        if (tier != s.Tier)
        {
            if (s.Tier == CrewTier.Bodied)
                Unbody(s, host);
            Events.Add($"construct {c.Id}: its crew is {tier.ToString().ToLowerInvariant()} ({(near < float.MaxValue ? $"{near:0} m from the nearest person" : "nobody about")})");
            s.Tier = tier;
            s.SeatsDirty = true;
        }
        if (s.Tier == CrewTier.Asleep)
        {
            if (s.Ai != null)
            {
                s.Ai.Release(c);
                FreeAi(s);
            }
            return;
        }
        if (!s.Air && s.Ai == null && _aiOut < MaxDrivers)
        {
            s.Ai = _aiPool.Count > 0 ? _aiPool.Pop() : new ShipAi(s.Body, s.Mode);
            s.Ai.Reset(s.Body, s.Mode);
            s.Ai.Crew = this;
            s.Ai.Ship = s;
            _aiOut++;
        }
        if (s.Ai != null)
            s.Ai.Mode = s.Mode;
        Fill(c, s, host);
        if (Tick >= s.CaptainAt)
            Captain(c, s, host);
        if (s.Tier == CrewTier.Bodied && host != null)
            Embody(c, s, host);
    }

    /// <summary>
    /// The captain (bot-brain.md section 3, phase 3): once a second near a person, every five seconds further, it reads who has hit the body (a
    /// deed against its faction), its hull and losses into morale, and picks its goal by its faction's rules: with something to fight it engages,
    /// or flees once its hull or morale falls under the faction's lines; with nothing, it holds (its drivers hold, patrol or follow).
    /// </summary>
    private void Captain(Construct c, CrewShip s, ICrewHost? host)
    {
        s.CaptainAt = Tick + (s.Tier == CrewTier.Bodied ? World.TickRate : 5 * World.TickRate);
        var def = CrewFactions.Of(s.Faction);
        if (c.LastHitBy != 255)
        {
            var by = c.LastHitBy;
            c.LastHitBy = 255;
            if (!s.Foe(by))
            {
                s.MarkFoe(by);
                Events.Add($"construct {c.Id}: player {by} attacked its crew{(def != null ? $" ({def.Name})" : "")}");
            }
            if (def != null)
                host?.Deed(s.Faction, by, -5);
        }
        s.Hull = s.HullMax > 0 ? Math.Clamp(HullOf(c) / s.HullMax, 0, 1) : 1;
        if (def != null)
            s.Morale = Math.Clamp(def.MoraleStart - def.MoraleCrewLost * s.Lost - def.MoraleHullLost * (1 - s.Hull), 0, 1);
        var contact = s.Contact != 255 || s.ContactBody != 0;
        var goal = !contact ? CrewGoal.Hold
            : def != null && (s.Hull < def.FleeHull || s.Morale < def.FleeMorale) ? CrewGoal.Flee
            : CrewGoal.Engage;
        if (goal != s.Goal)
        {
            Events.Add($"construct {c.Id}: its captain {(goal == CrewGoal.Flee ? "runs" : goal == CrewGoal.Engage ? "fights" : "holds")} (hull {s.Hull * 100:0}%, morale {s.Morale * 100:0}%, {s.Lost} crew lost{(def != null ? $", {def.Name}" : "")})");
            s.Goal = goal;
        }
    }

    /// <summary>
    /// Empty stations get the nearest free member: the helm first (a spare, else a gunner leaves their gun), then the guns (spares),
    /// then spares to couches. A seat a person sits in is theirs: its member stands aside. The walk is timed (no paths inside yet:
    /// bot-brain.md phase 4).
    /// </summary>
    private void Fill(Construct c, CrewShip s, ICrewHost? host)
    {
        // Walks that ended: at the station, if it is still free.
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (State[m] != CrewState.Walking || Tick < Ready[m])
                continue;
            var st = Station[m];
            if (st < 0 || st >= s.StCount || s.StMember[st] >= 0 || Sitter(c, s.StPart[st]) is var who && who != 0 && (Player[m] == 0 || who != Player[m]))
            {
                State[m] = CrewState.Free;
                Station[m] = -1;
                continue;
            }
            if (Player[m] != 0 && host != null && !host.Sit((byte)(Player[m] - 1), c, s.StPart[st]))
            {
                State[m] = CrewState.Free;
                Station[m] = -1;
                continue;
            }
            Seat(s, m, st);
            Events.Add($"construct {c.Id}: crew member {Uid[m]} took the {(st == s.Helm ? "helm" : s.StKind[st] == CrewStationKind.Gun ? "gun seat" : "couch")}");
        }
        for (var st = 0; st < s.StCount; st++)
        {
            // A person in a member's seat: it is theirs.
            var m0 = s.StMember[st];
            int part = s.StPart[st];
            var who = Sitter(c, part);
            if (m0 >= 0 && who != 0 && who != Player[m0])
            {
                Unseat(s, m0);
                State[m0] = CrewState.Free;
            }
        }
        for (var pass = 0; pass < 3; pass++)
        {
            var kind = pass == 0 ? CrewStationKind.Helm : pass == 1 ? CrewStationKind.Gun : CrewStationKind.Rest;
            for (var st = 0; st < s.StCount; st++)
            {
                if (s.StKind[st] != kind || s.StMember[st] >= 0 || Sitter(c, s.StPart[st]) != 0 || c.Parts[s.StPart[st]].Broken || Coming(s, st))
                    continue;
                var m = Nearest(c, s, st, kind);
                if (m < 0)
                {
                    if (kind == CrewStationKind.Helm && !s.HelmLost && !s.InGrace(Tick))
                    {
                        s.HelmLost = true;
                        Events.Add($"construct {c.Id}: nobody left to take the helm");
                    }
                    continue;
                }
                var from = Station[m] >= 0 ? s.StPart[Station[m]] : -1;
                var dist = from >= 0 ? Vector3.Distance(ConstructRender.PartCentre(c, from), ConstructRender.PartCentre(c, s.StPart[st])) : 6f;
                if (Station[m] >= 0)
                    Unseat(s, m);
                if (kind == CrewStationKind.Helm)
                    Job[m] = CrewJob.Pilot;
                else if (kind == CrewStationKind.Gun)
                    Job[m] = CrewJob.Gunner;
                State[m] = CrewState.Walking;
                Station[m] = (short)st;
                Tag[m] = s.StTag[st];
                Ready[m] = Tick + Math.Max(MinWalkTicks, (int)(dist / WalkSpeed * World.TickRate));
                Events.Add($"construct {c.Id}: crew member {Uid[m]} goes for the {(kind == CrewStationKind.Helm ? "helm" : kind == CrewStationKind.Gun ? "gun seat" : "couch")} ({dist:0} m)");
            }
        }
    }

    /// <summary>Someone walks to station <paramref name="st"/> already.</summary>
    private bool Coming(CrewShip s, int st)
    {
        for (var i = 0; i < s.MemberCount; i++)
            if (State[s.Members[i]] == CrewState.Walking && Station[s.Members[i]] == st)
                return true;
        return false;
    }

    /// <summary>Who would take an empty <paramref name="kind"/> station: a spare (free or on a couch), nearest; for the helm, a gunner if no spare is left.</summary>
    private int Nearest(Construct c, CrewShip s, int st, CrewStationKind kind)
    {
        var to = ConstructRender.PartCentre(c, s.StPart[st]);
        var best = -1;
        var bestScore = float.MaxValue;
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            var cur = Station[m];
            var curKind = cur >= 0 && State[m] == CrewState.Station ? s.StKind[cur] : CrewStationKind.Rest;
            if (State[m] is CrewState.Dead or CrewState.Walking)
                continue;
            if (kind == CrewStationKind.Rest && State[m] != CrewState.Free)
                continue; // (only a member with nowhere to be goes to a couch)
            var penalty = curKind switch
            {
                CrewStationKind.Rest => 0f,
                CrewStationKind.Gun when kind == CrewStationKind.Helm => 1000f, // (a gunner leaves their gun for the helm only with no spare)
                _ => -1f,
            };
            if (penalty < 0)
                continue;
            var d = cur >= 0 ? Vector3.Distance(ConstructRender.PartCentre(c, s.StPart[cur]), to) : 6f;
            if (d + penalty < bestScore)
            {
                bestScore = d + penalty;
                best = m;
            }
        }
        return best;
    }

    private static byte Sitter(Construct c, int part) => part >= 0 && part < c.PartCount ? c.Parts[part].SitterPlus1 : (byte)0;

    /// <summary>Near a person: each member in a seat gets a bot body there, nearest bodies first while any are free.</summary>
    private void Embody(Construct c, CrewShip s, ICrewHost host)
    {
        for (var i = 0; i < s.MemberCount && _bodiedCount < Math.Min(MaxBodied, MaxBodiedCap) && _promoted < PromotePerTick; i++)
        {
            var m = s.Members[i];
            if (Player[m] != 0 || State[m] != CrewState.Station || Station[m] < 0)
                continue;
            int part = s.StPart[Station[m]];
            if (Sitter(c, part) != 0)
                continue;
            var id = host.Promote(this, m, c, part);
            if (id == 0)
            {
                if (!s.BodiesShort)
                    Events.Add($"construct {c.Id}: no body free for crew member {Uid[m]} (a record at its seat)");
                s.BodiesShort = true;
                return;
            }
            s.BodiesShort = false;
            Player[m] = (byte)(id + 1);
            IsBody[id] = true;
            _promoted++;
            Events.Add($"construct {c.Id}: crew member {Uid[m]} has a body (player {id}) at seat {part}");
            _bodied[_bodiedCount++] = m;
            s.SeatsDirty = true;
        }
    }

    /// <summary>Every body goes back to a record (a tool, or the cap lowered); the next looks give bodies again up to <see cref="MaxBodied"/>.</summary>
    public void UnbodyAll(ICrewHost? host)
    {
        for (var i = 0; i < ShipHigh; i++)
            if (Ships[i] is { } s)
                Unbody(s, host);
    }

    private void Unbody(CrewShip s, ICrewHost? host)
    {
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (Player[m] == 0)
                continue;
            if (State[m] == CrewState.Dead)
                continue; // (its body goes with its record)
            DropBody(m, host);
        }
        s.SeatsDirty = true;
    }

    /// <summary>Every tick: a body that died takes its member with it.</summary>
    private void BodiedTick(ConstructWorld w, ICrewHost? host)
    {
        if (host == null)
            return;
        for (var i = _bodiedCount - 1; i >= 0; i--)
        {
            var m = _bodied[i];
            if (State[m] == CrewState.Dead || host.Alive((byte)(Player[m] - 1)))
                continue;
            Events.Add($"crew member {Uid[m]} on construct {Body[m]} was killed");
            Kill(m, host, bodyDied: true);
        }
    }

    /// <summary>The drivers of every awake crewed ship: a bodied ship's every tick, a seated one's thinking every other tick and its last keys held between.</summary>
    private void Drive(ConstructWorld w, IWorldQuery q, ICrewHost? host)
    {
        for (var i = 0; i < ShipHigh; i++)
        {
            if (Ships[i] is not { Ai: { } ai } s || w.Get(s.Body) is not { } c || c.Wreck)
                continue;
            if (ai.Boarded(c, this))
            {
                if (c.CrewAims || c.CrewDrives)
                    ai.Release(c); // (a person has the helm: their stabilizer's own Mouse aim setting, not the crew's)
                continue;
            }
            var look = (Tick + i) % 30 == 0 || s.Contact == 255 && s.ContactBody == 0 && (Tick + i) % 10 == 0;
            if (s.Tier == CrewTier.Seated && (Tick + i) % 2 == 1)
            {
                ai.Replay(c);
                continue;
            }
            ShipAiTarget t = default;
            var has = host != null ? host.Target(c, s, look, out t) : CheckTarget(c, out t);
            if (host == null)
                s.Contact = has ? (byte)0 : (byte)255; // (a check's fixed target is the captain's contact)
            ai.Step(w, c, q, has, t);
        }
    }

    /// <summary>A check with no game: its target is set from outside.</summary>
    public ShipAiTarget? Fixed;

    private bool CheckTarget(Construct c, out ShipAiTarget t)
    {
        t = Fixed ?? default;
        return Fixed.HasValue;
    }

    // ---- Saves (the CREW block of a body: WorldSaveBodies.cs) ------------------------------------------------------------------------

    public const byte SaveVersion = 2; // (2: its faction kind, by name, morale and losses)

    /// <summary>A body's crew as bytes for its save (null: none). Bodied members are written as records; walks as free.</summary>
    public byte[]? SaveBytes(ushort body)
    {
        if (ShipOf(body) is not { } s)
            return null;
        var wr = new SaveWriter(64 + s.MemberCount * 12);
        wr.U8(SaveVersion).U8((byte)s.Mode).U8((byte)((s.Air ? 1 : 0) | (s.FromSetting ? 2 : 0)));
        wr.Str(CrewFactions.Of(s.Faction)?.Name ?? "").U8((byte)(s.Morale * 255)).U8(s.Lost);
        var n = 0;
        for (var i = 0; i < s.MemberCount; i++)
            if (State[s.Members[i]] != CrewState.Dead)
                n++;
        wr.U8((byte)n);
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (State[m] == CrewState.Dead)
                continue;
            var tag = State[m] == CrewState.Station ? Tag[m] : (ushort)0;
            wr.U32(Uid[m]).U8((byte)Job[m]).U8((byte)Math.Clamp(Health[m] / MemberHealth * 255, 1, 255)).U8(Skill[m]).U16(tag).U16(Name[m]);
        }
        return wr.Span.ToArray();
    }

    /// <summary>A body's crew from its save's bytes: a crew that doesn't read is left off with a log line, never the body.</summary>
    public void Load(Construct c, byte[] bytes)
    {
        try
        {
            var r = new SaveReader(bytes);
            var v = r.U8();
            if (v is not (1 or SaveVersion))
            {
                Console.WriteLine($"[crew] construct {c.Id}'s crew is of version {v} (this build reads {SaveVersion}): left off");
                return;
            }
            var mode = (ShipAiMode)Math.Min((int)r.U8(), 2);
            var flags = r.U8();
            var faction = (byte)0;
            float morale = -1;
            byte lost = 0;
            if (v >= 2)
            {
                var fname = r.Str();
                faction = CrewFactions.Find(fname);
                if (fname.Length > 0 && faction == 0)
                    Console.WriteLine($"[crew] construct {c.Id}'s crew was of faction \"{fname}\", which isn't read now: it has none");
                morale = r.U8() / 255f;
                lost = r.U8();
            }
            int n = r.U8();
            var s = NewShip(c.Id, mode, (flags & 1) != 0);
            s.FromSetting = (flags & 2) != 0;
            SetFaction(c, s, faction);
            if (morale >= 0)
                s.Morale = morale;
            s.Lost = lost;
            Scan(c, s);
            for (var i = 0; i < n && i < MaxCrewPerBody; i++)
            {
                var uid = r.U32();
                var job = (CrewJob)Math.Min((int)r.U8(), 2);
                var health = r.U8() / 255f * MemberHealth;
                var skill = r.U8();
                var tag = r.U16();
                var name = r.U16();
                Add(s, job, TagIndex(s, tag), uid, health, skill, name);
            }
            Events.Add($"construct {c.Id}: its crew of {s.MemberCount} came back from the save");
        }
        catch (WorldFileException ex)
        {
            Console.WriteLine($"[crew] construct {c.Id}'s crew didn't read ({ex.Message}): left off");
        }
    }

    /// <summary>Everything goes (a new world).</summary>
    public void Clear(ICrewHost? host)
    {
        for (var i = 0; i < ShipHigh; i++)
            if (Ships[i] is { } s)
                Disband(s, null, host, "went with the world");
        ShipHigh = 0;
        GoneBodies.Clear();
        Events.Clear();
    }

    // ---- Lines for checks and the stand-ins -------------------------------------------------------------------------------------------

    /// <summary>A roster line (allocates: checks).</summary>
    public string Describe(CrewShip s)
    {
        var parts = new List<string>();
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            var st = Station[m];
            var at = st >= 0 && st < s.StCount ? $"{s.StKind[st]} seat {s.StPart[st]}" : "aboard";
            parts.Add($"#{Uid[m]} {Job[m]} {State[m]} at {at}{(Player[m] != 0 ? $" (body {Player[m] - 1})" : "")} {Health[m]:0} hp");
        }
        return $"{s.MemberCount} crew, tier {s.Tier}: {string.Join("; ", parts)}";
    }

    /// <summary>Its members alive.</summary>
    public int Alive(CrewShip s)
    {
        var n = 0;
        for (var i = 0; i < s.MemberCount; i++)
            if (State[s.Members[i]] != CrewState.Dead)
                n++;
        return n;
    }

    /// <summary>The seats with a member at them and no body (what clients draw as stand-ins), into <paramref name="parts"/>. Returns how many.</summary>
    public int StandIns(CrewShip s, Span<ushort> parts)
    {
        var n = 0;
        for (var st = 0; st < s.StCount && n < parts.Length; st++)
            if (s.StMember[st] is var m and >= 0 && State[m] == CrewState.Station && Player[m] == 0)
                parts[n++] = (ushort)s.StPart[st];
        return n;
    }
}
