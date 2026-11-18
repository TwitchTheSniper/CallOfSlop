using System.Numerics;

namespace CallOfSlop;

/// <summary>An order an admiral writes into a captain's <see cref="CrewShip"/> (faction-controllers.md 6.2 and the user's answers on admirals 3).</summary>
public enum OrderKind : byte
{
    None, // its captain's own judgement
    Form, // keep a slot in the flagship's frame (At: the slot, x right, y up, z ahead; Body: the flagship)
    GoTo, // go to At
    Engage, // fight Body first (focus fire: the whole group is given one)
    Escort, // keep station on Body (At: the offset), fight what comes near it
    Hold, // hold where it is, fight what comes
    Retreat, // run to At, whatever its own morale; its gunners still fire
    Board, // close on Body to boarding range and hold alongside
    Screen, // keep between Body (the protected) and Threat
    Load, // civilian: at At for the group's dwell (a dock, a depot)
    Unload,
    Mine, // ... at a field
    Deliver, // ... at the route's end
    Route, // follow the group's route (the leg's point is At)
}

/// <summary>What a group is for. Combat and civilian alike are led by an admiral (the user's answers on admirals 3).</summary>
public enum GroupTask : byte
{
    Patrol,
    Convoy,
    Strike,
    Escort,
    Defend,
    Mining,
    Haul,
    Trade,
    Construction,
}

/// <summary>A report up from a captain or the admiral's own look (faction-controllers.md 6.3).</summary>
public enum FleetReport : byte
{
    Contact,
    ShipLost,
    HitBadly,
    Boarded,
    CutOff,
    Arrived,
    AdmiralLost,
    Command, // a new admiral took command
    Retreat,
    NeedHelp,
}

/// <summary>One order: small, written into the captain's record, nothing allocated.</summary>
public struct ShipOrder
{
    public OrderKind Kind;
    public Vector3 At; // a world point, or Form's and Escort's slot (the flagship's or the protected body's frame)
    public ushort Body, Threat;
    public long Until; // the tick it lapses unless the admiral says it again (a ship cut off from the net keeps it this long)
    public float Speed; // m/s wanted (0: its own cruise)
    public bool Fight; // with something to fight it fights (false: it keeps moving; its gunners still fire)
}

public struct FleetReportEntry
{
    public FleetReport Kind;
    public ushort Body, Other;
    public long Tick;
}

/// <summary>One group: 1 to 12 ships of one power under one admiral (faction-controllers.md 6.1). Fixed arrays: made once, pooled.</summary>
public sealed class CrewGroup
{
    public const int MaxShips = 12, RingSize = 64, MaxRoute = 8;

    public int Slot;
    public bool Live;
    public int Serial; // bumped each time the slot is used again (a prisoner's intel names the group it led)
    public byte Power;
    public GroupTask Task;
    public FleetDoctrine Doctrine = new();
    public readonly ushort[] Ships = new ushort[MaxShips];
    public int Count;
    public uint AdmiralUid; // the admiral's crew record (0: none: succession runs, or the group broke)
    public ushort AdmiralName;
    public ushort Flagship; // the body the admiral is aboard (its seat: any on a ship on the net)
    public ushort Number; // its number in news ("Group 12")
    // The group's own order (from a controller later, a check or a player now): the admiral turns it into each captain's.
    public OrderKind Order = OrderKind.Hold;
    public Vector3 OrderAt, Home;
    public ushort OrderBody;
    public readonly Vector3[] Route = new Vector3[MaxRoute];
    public readonly OrderKind[] RouteWork = new OrderKind[MaxRoute];
    public int RouteCount, RouteAt;
    public float Dwell; // s at each route point
    public long DwellUntil;
    public bool Loop = true;
    // What it sees and decides.
    public float Strength0, Strength, Enemy;
    public ushort Focus; // the body its warships fire at together
    public bool Retreating;
    public int Losses;
    public long ThinkAt, DisarrayUntil, LeaderlessSince;
    public int OnNet; // members that heard the admiral at its last think
    public readonly FleetReportEntry[] Ring = new FleetReportEntry[RingSize];
    public int RingHead, RingCount, Dropped;
    public long RevealedUntil; // positions shown to those who learnt them (interrogation)
    public readonly ulong[] RevealedTo = new ulong[4];

    public bool Civilian => Task is GroupTask.Convoy or GroupTask.Mining or GroupTask.Haul or GroupTask.Trade or GroupTask.Construction;

    public void Clear()
    {
        Live = false;
        Count = 0;
        AdmiralUid = 0;
        Flagship = 0;
        Order = OrderKind.Hold;
        OrderBody = 0;
        RouteCount = RouteAt = 0;
        Dwell = 0;
        DwellUntil = 0;
        Loop = true;
        Strength0 = Strength = Enemy = 0;
        Focus = 0;
        Retreating = false;
        Losses = 0;
        ThinkAt = DisarrayUntil = LeaderlessSince = 0;
        OnNet = 0;
        RingHead = RingCount = Dropped = 0;
        RevealedUntil = 0;
        Array.Clear(RevealedTo);
    }

    public void Post(FleetReport kind, ushort body, ushort other, long tick)
    {
        if (RingCount == RingSize)
            Dropped++; // (the oldest goes: counted, logged by CrewFleet once a minute)
        else
            RingCount++;
        Ring[RingHead] = new FleetReportEntry { Kind = kind, Body = body, Other = other, Tick = tick };
        RingHead = (RingHead + 1) % RingSize;
    }
}

/// <summary>
/// Groups and admirals (docs/sandbox/design/faction-controllers.md phase 2, the user's answers on admirals). An admiral is a role in the hierarchy,
/// not a combat post: a crew record (job <see cref="CrewJob.Admiral"/>) aboard any ship of its group that is on the <b>fleet command network</b>,
/// commanding from whatever seat it has there. It leads 1 to 12 captains, combat or civilian: each think it reads its members, their contacts and
/// reports, weighs the strength it has left against the enemy's (no fixed break line), and writes one <see cref="ShipOrder"/> into each captain
/// that hears it. Captains keep their own judgement inside an order (their Flee is held off by the doctrine's discipline). Losing the admiral
/// (killed, taken, cut off) runs the doctrine's succession. Server only; fixed arrays, a fixed number of group slots looked at a tick.
/// </summary>
public sealed class CrewFleet
{
    public const int MaxGroups = 256;

    public readonly CrewGroup[] Groups = new CrewGroup[MaxGroups];
    public int Live;
    private int _at, _number;
    private long _droppedSaidAt;
    public FleetRules Rules = FleetRules.Current;

    /// <summary>News for the host (wars of groups: an admiral fallen, command passed, a group broken), drained by Game; a check reads them.</summary>
    public readonly List<string> News = new(16);

    // Its cost (the far layer must stay fixed per tick at 256 groups).
    public double CpuMs, WorstMs;
    public int Ticks, Thinks;
    public long Bytes;

    public CrewFleet()
    {
        for (var i = 0; i < MaxGroups; i++)
            Groups[i] = new CrewGroup { Slot = i };
    }

    public CrewGroup? Of(int slot) => slot >= 0 && slot < MaxGroups && Groups[slot].Live ? Groups[slot] : null;

    public void Clear()
    {
        foreach (var g in Groups)
            g.Clear();
        Live = 0;
        _number = 0;
        News.Clear();
    }

    // ---- Forming and leaving -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A group of the crewed bodies <paramref name="bodies"/> (one power: the first's), for <paramref name="task"/>: the strongest on the net by radio is
    /// the flagship and gets an admiral (a new record, else its senior spare). -1 (logged): no slot, no crewed body, or none of that power.
    /// </summary>
    public int Form(CrewWorld crew, ConstructWorld w, GroupTask task, ReadOnlySpan<ushort> bodies, string why, uint admiralUid = 0, ushort admiralName = 0xFFFF)
    {
        var slot = -1;
        for (var i = 0; i < MaxGroups; i++)
            if (!Groups[i].Live)
            {
                slot = i;
                break;
            }
        if (slot < 0)
        {
            crew.Events.Add($"fleet: no group formed for {why}: all {MaxGroups} group slots are in use");
            return -1;
        }
        var g = Groups[slot];
        g.Clear();
        byte power = 0;
        foreach (var b in bodies)
        {
            if (g.Count >= CrewGroup.MaxShips)
            {
                crew.Events.Add($"fleet: construct {b} left out of a group: {CrewGroup.MaxShips} ships at most");
                continue;
            }
            if (crew.ShipOf(b) is not { } s || s.Own || s.Prize || s.Faction == 0 || w.Get(b) is not { Wreck: false } c)
            {
                crew.Events.Add($"fleet: construct {b} left out of a group: no NPC crew aboard");
                continue;
            }
            if (power == 0)
                power = s.Faction;
            else if (s.Faction != power)
            {
                crew.Events.Add($"fleet: construct {b} left out of a group: another power's ({CrewPowers.Of(s.Faction)?.Name})");
                continue;
            }
            if (s.Group >= 0)
                Leave(crew, s);
            Look(crew, c, s);
            g.Ships[g.Count++] = b;
            s.Group = (short)slot;
        }
        if (g.Count == 0)
        {
            crew.Events.Add($"fleet: no group formed for {why}: no crewed ship of an NPC power");
            return -1;
        }
        g.Live = true;
        g.Serial++;
        g.Power = power;
        g.Task = task;
        g.Doctrine = Rules.DoctrineOf(power);
        g.Number = (ushort)++_number;
        g.ThinkAt = crew.Tick;
        if (w.Get(g.Ships[0]) is { } first)
            g.Home = g.OrderAt = first.WorldCom;
        Live++;
        // The flagship: a convoy's leader; else a ship that can reach the net by radio first, then the strongest.
        var best = task == GroupTask.Convoy ? 0 : -1;
        var bestScore = float.MinValue;
        for (var k = 0; k < g.Count && task != GroupTask.Convoy; k++)
        {
            var s = crew.ShipOf(g.Ships[k])!;
            var score = s.Strength + (s.NetCount > 0 ? 1e6f : 0);
            if (score > bestScore)
            {
                bestScore = score;
                best = k;
            }
        }
        var flag = crew.ShipOf(g.Ships[best])!;
        var m = admiralUid != 0 ? crew.Add(flag, CrewJob.Admiral, -1, admiralUid, name: admiralName) : crew.Add(flag, CrewJob.Admiral, -1, skill: 220);
        if (m < 0)
            m = SeniorSpare(crew, flag);
        if (m >= 0)
        {
            crew.Job[m] = CrewJob.Admiral;
            g.AdmiralUid = crew.Uid[m];
            g.AdmiralName = crew.Name[m];
            g.Flagship = flag.Body;
        }
        foreach (var b in g.Ships.AsSpan(0, g.Count))
            g.Strength0 += crew.ShipOf(b)!.Strength;
        crew.Events.Add($"fleet: group {g.Number} ({task}, {CrewPowers.Of(power)?.Name}) formed for {why}: {g.Count} ships, {(m >= 0 ? $"Admiral {CrewNames.Of(g.AdmiralName)} aboard construct {g.Flagship}" : "NO ADMIRAL (the flagship's roster is full and has no spare)")}");
        return slot;
    }

    /// <summary>A crewed body joins group <paramref name="slot"/> (a convoy's member set down after its leader). False (logged): full, another power's, or no NPC crew.</summary>
    public bool Join(CrewWorld crew, ConstructWorld w, int slot, ushort body)
    {
        if (Of(slot) is not { } g || crew.ShipOf(body) is not { Own: false, Prize: false } s || w.Get(body) is not { Wreck: false } c || s.Faction != g.Power || g.Count >= CrewGroup.MaxShips)
        {
            crew.Events.Add($"fleet: construct {body} can't join group slot {slot} (full, another power's, or no NPC crew)");
            return false;
        }
        if (s.Group == slot)
            return true;
        if (s.Group >= 0)
            Leave(crew, s);
        Look(crew, c, s);
        g.Ships[g.Count++] = body;
        s.Group = (short)slot;
        g.Strength0 += s.Strength;
        g.ThinkAt = 0;
        crew.Events.Add($"fleet: construct {body} joins group {g.Number} ({g.Count} ships)");
        return true;
    }

    /// <summary>A ship leaves its group (its captain on its own again).</summary>
    public void Leave(CrewWorld crew, CrewShip s)
    {
        if (Of(s.Group) is { } g)
        {
            for (var k = 0; k < g.Count; k++)
                if (g.Ships[k] == s.Body)
                {
                    g.Ships[k] = g.Ships[--g.Count];
                    break;
                }
            if (g.Count == 0)
                Dissolve(crew, g, "its last ship left");
        }
        s.Group = -1;
        s.Order = default;
    }

    /// <summary>A crewed body's crew is gone (its body went or was wrecked: CrewWorld.Disband): its group counts a loss.</summary>
    public void ShipGone(CrewWorld crew, CrewShip s, string why)
    {
        if (Of(s.Group) is not { } g)
            return;
        g.Losses++;
        g.Post(FleetReport.ShipLost, s.Body, s.DoerBody, crew.Tick);
        crew.Events.Add($"fleet: group {g.Number} lost construct {s.Body} (its crew {why})");
        Leave(crew, s);
    }

    private void Dissolve(CrewWorld crew, CrewGroup g, string why)
    {
        for (var k = 0; k < g.Count; k++)
            if (crew.ShipOf(g.Ships[k]) is { } s)
            {
                s.Group = -1;
                if (s.Order.Kind != OrderKind.Retreat)
                    s.Order = default; // (a broken group's runners keep running until their order lapses)
            }
        crew.Events.Add($"fleet: group {g.Number} dissolved: {why}");
        g.Clear();
        Live--;
    }

    /// <summary>The group's order: an admiral turns it into each captain's at its next think (now, if <paramref name="now"/>).</summary>
    public void Command(int slot, OrderKind kind, Vector3 at = default, ushort body = 0, bool now = true)
    {
        if (Of(slot) is not { } g)
            return;
        g.Order = kind;
        g.OrderAt = at;
        g.OrderBody = body;
        g.RouteCount = 0;
        if (now)
            g.ThinkAt = 0;
    }

    /// <summary>A route for a civilian group: each point with its work (Load, Unload, Mine, Deliver, or Route to pass through), <paramref name="dwell"/> s at each.</summary>
    public void SetRoute(int slot, ReadOnlySpan<Vector3> points, ReadOnlySpan<OrderKind> work, float dwell, bool loop = true)
    {
        if (Of(slot) is not { } g)
            return;
        g.RouteCount = Math.Min(points.Length, CrewGroup.MaxRoute);
        for (var i = 0; i < g.RouteCount; i++)
        {
            g.Route[i] = points[i];
            g.RouteWork[i] = i < work.Length ? work[i] : OrderKind.Route;
        }
        g.RouteAt = 0;
        g.Dwell = dwell;
        g.DwellUntil = 0;
        g.Loop = loop;
        g.Order = OrderKind.Route;
        g.ThinkAt = 0;
    }

    // ---- A ship's fit: its access to the net, its strength --------------------------------------------------------------------------

    /// <summary>A body's fight in one number: its guns' damage a second (pellets x damage x rate) plus its hull's worth (a hundredth of its parts' health).</summary>
    public static float StrengthOf(Construct c)
    {
        var dps = 0f;
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var g = ref c.Guns[k];
            if (g.Def == null || g.Part >= c.PartCount || c.Parts[g.Part].Broken)
                continue;
            dps += (g.Def.Damage * Math.Max(1, g.Shots) + g.BlastDamage) * World.TickRate / Math.Max(1, (int)g.Interval);
        }
        return dps + CrewWorld.HullOf(c) * 0.01f;
    }

    /// <summary>After a rebuild: which parts give it access to the net by radio (at most four kept), and its strength.</summary>
    private void Look(CrewWorld crew, Construct c, CrewShip s)
    {
        s.NetStamp = c.RebuildStamp;
        s.NetCount = 0;
        for (var i = 0; i < c.PartCount && s.NetCount < s.NetPart.Length; i++)
        {
            var d = c.Parts[i].D;
            if (d.RadarRange > 0 || Array.IndexOf(Rules.NetParts, d.Name) >= 0)
                s.NetPart[s.NetCount++] = (short)i;
        }
        s.Strength = StrengthOf(c);
    }

    /// <summary>How far the ship reaches its group: by radio while a net part is whole, else its crew's hand sets.</summary>
    private float Reach(Construct c, CrewShip s)
    {
        for (var k = 0; k < s.NetCount; k++)
            if (s.NetPart[k] < c.PartCount && !c.Parts[s.NetPart[k]].Broken && c.Parts[s.NetPart[k]].Health > 0)
                return Rules.RadioRange;
        return Rules.VoiceRange;
    }

    // ---- The admirals' thinking ---------------------------------------------------------------------------------------------------

    private readonly float[] _reach = new float[CrewGroup.MaxShips];
    private readonly Vector3[] _pos = new Vector3[CrewGroup.MaxShips];
    private readonly bool[] _heard = new bool[CrewGroup.MaxShips];
    private readonly ushort[] _foes = new ushort[CrewGroup.MaxShips];
    private readonly Vector3[] _foeAt = new Vector3[CrewGroup.MaxShips];
    private readonly float[] _foeStr = new float[CrewGroup.MaxShips];

    /// <summary>A tick: a fixed slice of group slots; each thinks when due (1 s with a member awake, 5 s with none).</summary>
    public void Step(CrewWorld crew, ConstructWorld w, ICrewHost? host)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        var e0 = crew.Events.Count + News.Count;
        if (Live > 0)
            for (var k = 0; k < Rules.GroupsPerTick; k++)
            {
                if (_at >= MaxGroups)
                    _at = 0;
                var g = Groups[_at++];
                if (g.Live && crew.Tick >= g.ThinkAt)
                {
                    Think(crew, w, host, g);
                    Thinks++;
                }
            }
        var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (crew.Events.Count + News.Count == e0)
            Bytes += GC.GetAllocatedBytesForCurrentThread() - b0; // (a think that said nothing allocates nothing)
        CpuMs += ms;
        if (Ticks++ > 10)
            WorstMs = Math.Max(WorstMs, ms);
    }

    private void Think(CrewWorld crew, ConstructWorld w, ICrewHost? host, CrewGroup g)
    {
        var awake = false;
        // Members: gone ones leave; positions, reach, strength (again after a rebuild).
        for (var k = g.Count - 1; k >= 0; k--)
        {
            if (crew.ShipOf(g.Ships[k]) is not { } s || w.Get(g.Ships[k]) is not { Wreck: false } c || s.Prize || s.Own)
            {
                if (crew.ShipOf(g.Ships[k]) is { } gone)
                {
                    g.Post(gone.Prize ? FleetReport.Boarded : FleetReport.ShipLost, g.Ships[k], 0, crew.Tick);
                    g.Losses++;
                    gone.Group = -1;
                    gone.Order = default;
                }
                g.Ships[k] = g.Ships[--g.Count];
                continue;
            }
            if (s.NetStamp != c.RebuildStamp)
                Look(crew, c, s);
            awake |= s.Tier != CrewTier.Asleep;
        }
        if (g.Count == 0)
        {
            News.Add($"{PowerName(g.Power)}'s group {g.Number} is gone");
            Dissolve(crew, g, "no ship left");
            return;
        }
        g.ThinkAt = crew.Tick + (int)((awake ? Rules.ThinkNear : Rules.ThinkFar) * World.TickRate);
        var strength = 0f;
        for (var k = 0; k < g.Count; k++)
        {
            var s = crew.ShipOf(g.Ships[k])!;
            var c = w.Get(g.Ships[k])!;
            _pos[k] = c.WorldCom;
            _reach[k] = Reach(c, s);
            var alive = crew.Alive(s);
            strength += s.Strength * MathF.Max(0.1f, s.Hull) * (alive > 0 ? 1 : 0);
        }
        g.Strength = strength;
        if (g.Dropped > 0 && crew.Tick - _droppedSaidAt > 60 * World.TickRate)
        {
            crew.Events.Add($"fleet: group {g.Number}'s report ring was full: {g.Dropped} oldest reports dropped");
            _droppedSaidAt = crew.Tick;
            g.Dropped = 0;
        }

        // The admiral: alive, free and aboard a member; else succession by doctrine.
        var adm = AdmiralOf(crew, g);
        if (adm < 0 && !Succeed(crew, w, g, host))
            return;
        adm = AdmiralOf(crew, g);
        if (adm < 0 || crew.Tick < g.DisarrayUntil)
            return; // (disarray: no new orders; captains finish their last ones, then are on their own)

        // The net: who hears the admiral, from its ship outwards through members in reach of each other.
        var root = -1;
        for (var k = 0; k < g.Count; k++)
        {
            _heard[k] = false;
            if (g.Ships[k] == g.Flagship)
                root = k;
        }
        if (root < 0)
            return;
        _heard[root] = true;
        for (var changed = true; changed;)
        {
            changed = false;
            for (var a = 0; a < g.Count; a++)
                if (_heard[a])
                    for (var b = 0; b < g.Count; b++)
                        if (!_heard[b] && Vector3.DistanceSquared(_pos[a], _pos[b]) <= MathF.Max(_reach[a], _reach[b]) * MathF.Max(_reach[a], _reach[b]))
                            _heard[b] = changed = true;
        }
        g.OnNet = 0;
        for (var k = 0; k < g.Count; k++)
        {
            var s = crew.ShipOf(g.Ships[k])!;
            if (s.OnNet && !_heard[k])
            {
                g.Post(FleetReport.CutOff, s.Body, 0, crew.Tick);
                crew.Events.Add($"fleet: group {g.Number}: construct {s.Body} is cut off from the fleet net (it keeps its last order {Rules.CutGraceSeconds:0} s)");
            }
            s.OnNet = _heard[k];
            if (_heard[k])
                g.OnNet++;
        }

        // Contacts: what its captains are fighting (each one's target, fresh), and their strength.
        var foes = 0;
        var enemy = 0f;
        for (var k = 0; k < g.Count; k++)
        {
            var s = crew.ShipOf(g.Ships[k])!;
            if (!s.OnNet || crew.Tick - s.TargetTick > 2 * World.TickRate)
                continue;
            var known = false;
            for (var f = 0; f < foes && !known; f++)
                known = s.TargetBody != 0 ? _foes[f] == s.TargetBody : Vector3.DistanceSquared(_foeAt[f], s.TargetAt) < 25;
            if (known || foes == _foes.Length)
                continue;
            _foes[foes] = s.TargetBody;
            _foeAt[foes] = s.TargetAt;
            var str = s.TargetBody != 0 && w.Get(s.TargetBody) is { } tc ? (crew.ShipOf(s.TargetBody) is { } ts ? ts.Strength * MathF.Max(0.1f, ts.Hull) : StrengthOf(tc)) : 2f; // (a person on foot: 2)
            _foeStr[foes++] = str;
            enemy += str;
        }
        if (foes > 0 && g.Enemy == 0)
            g.Post(FleetReport.Contact, _foes[0], 0, crew.Tick);
        g.Enemy = enemy;

        // No fixed break line: it pulls back when what it has left falls under half the ratio its doctrine wants, and comes in again past it.
        var ratio = enemy > 0 ? strength / enemy : float.MaxValue;
        var want = g.Doctrine.Caution * (g.Civilian ? 1.5f : 1f);
        var retreat = enemy > 0 && (g.Retreating ? ratio < want : ratio < want * 0.5f);
        if (retreat != g.Retreating)
        {
            g.Retreating = retreat;
            g.Post(retreat ? FleetReport.Retreat : FleetReport.Command, g.Flagship, 0, crew.Tick);
            crew.Events.Add($"fleet: group {g.Number}: Admiral {CrewNames.Of(g.AdmiralName)} {(retreat ? "pulls back" : "turns to fight")} (strength {strength:0} against {enemy:0}, wants {want:0.0} to 1)");
            if (retreat)
                News.Add($"{PowerName(g.Power)}'s group {g.Number} pulls back under Admiral {CrewNames.Of(g.AdmiralName)}");
        }

        // The focus: the best target by threat over what it takes to kill (a low skill: the nearest).
        g.Focus = 0;
        var centre = _pos[root];
        if (foes > 0)
        {
            var best = float.MinValue;
            for (var f = 0; f < foes; f++)
            {
                if (_foes[f] == 0)
                    continue;
                var d = Vector3.Distance(_foeAt[f], centre);
                var hp = w.Get(_foes[f]) is { } fc ? CrewWorld.HullOf(fc) * 0.01f + 1 : 1;
                var score = g.Doctrine.Skill * (_foeStr[f] / hp) * 1000 - (1 - g.Doctrine.Skill) * d;
                if (score > best)
                {
                    best = score;
                    g.Focus = _foes[f];
                }
            }
        }

        // Orders, to each captain that hears the admiral.
        var flag = w.Get(g.Flagship)!;
        var until = crew.Tick + (long)(MathF.Max(Rules.CutGraceSeconds, Rules.ThinkFar * 2) * World.TickRate);
        var slotN = 0;
        var away = foes > 0 ? Vector3.Normalize(centre - _foeAt[0] + new Vector3(0.01f, 0, 0)) : Vector3.UnitZ;
        GroupOrder(crew, g, flag, out var groupKind, out var groupAt);
        for (var k = 0; k < g.Count; k++)
        {
            if (!_heard[k])
                continue;
            var s = crew.ShipOf(g.Ships[k])!;
            var isFlag = g.Ships[k] == g.Flagship;
            var armed = s.Strength > CrewWorld.HullOf(w.Get(s.Body)!) * 0.01f + 0.5f;
            ShipOrder o = default;
            if (g.Retreating)
                o = new ShipOrder { Kind = OrderKind.Retreat, At = g.Order == OrderKind.Retreat ? g.OrderAt : _pos[k] + away * 2000 };
            else if (g.Order == OrderKind.Board && g.OrderBody != 0 && armed)
                o = new ShipOrder { Kind = OrderKind.Board, Body = g.OrderBody, Fight = true };
            else if (g.Focus != 0 && armed && !(g.Civilian && isFlag))
            {
                // Warships fight: focus fire, or with civilians to keep, a screen between them and the threat.
                o = g.Civilian && foes > 0
                    ? new ShipOrder { Kind = OrderKind.Screen, Body = g.Flagship, Threat = g.Focus, At = new Vector3(0, 0, g.Doctrine.Spacing * (1 + slotN % 3)), Fight = true }
                    : new ShipOrder { Kind = OrderKind.Engage, Body = g.Focus, Fight = true };
                slotN++;
            }
            else if (isFlag)
                o = new ShipOrder { Kind = groupKind, At = groupAt, Body = g.OrderBody, Fight = !g.Civilian };
            else
            {
                o = new ShipOrder { Kind = OrderKind.Form, Body = g.Flagship, At = Slot(g.Doctrine, ++slotN), Fight = !g.Civilian || armed };
                if (g.Order == OrderKind.Escort && g.OrderBody != 0)
                    o = new ShipOrder { Kind = OrderKind.Escort, Body = g.OrderBody, At = Slot(g.Doctrine, slotN), Fight = true };
            }
            o.Until = until;
            s.Order = o;
        }
    }

    /// <summary>What the flagship does with the group's order: a place to go, a route's leg (its work there and the dwell), hold.</summary>
    private static void GroupOrder(CrewWorld crew, CrewGroup g, Construct flag, out OrderKind kind, out Vector3 at)
    {
        kind = g.Order;
        at = g.OrderAt;
        if (g.Order == OrderKind.Route && g.RouteCount > 0)
        {
            var p = g.Route[g.RouteAt];
            var work = g.RouteWork[g.RouteAt];
            kind = work == OrderKind.Route ? OrderKind.GoTo : work;
            at = p;
            if (g.DwellUntil != 0 || Vector3.Distance(Flat(flag.WorldCom), Flat(p)) < ArriveBand + flag.Radius * 0.5f)
            {
                if (g.DwellUntil == 0)
                {
                    g.DwellUntil = crew.Tick + (long)((work == OrderKind.Route ? 0 : g.Dwell) * World.TickRate);
                    g.Post(FleetReport.Arrived, flag.Id, (ushort)g.RouteAt, crew.Tick);
                    crew.Events.Add($"fleet: group {g.Number} at its route's point {g.RouteAt + 1} of {g.RouteCount} ({work}{(work != OrderKind.Route ? $", {g.Dwell:0} s" : "")})");
                }
                if (crew.Tick >= g.DwellUntil)
                {
                    g.DwellUntil = 0;
                    g.RouteAt = g.RouteAt + 1 < g.RouteCount ? g.RouteAt + 1 : g.Loop ? 0 : g.RouteAt;
                }
            }
        }
        else if (g.Order == OrderKind.Retreat)
            at = g.OrderAt;
        else if (g.Order is OrderKind.Hold or OrderKind.None)
            kind = OrderKind.None; // (the flagship's captain holds, patrols or drives out and back as its drivers do: a spawner's convoy's leader)
    }

    private static Vector3 Flat(Vector3 v) => v with { Y = 0 };

    /// <summary>A flagship this close to a route's point (m, plus half its size) is there.</summary>
    public const float ArriveBand = 50;

    /// <summary>A follower's slot (x right, z ahead of the flagship) by the doctrine's shape.</summary>
    public static Vector3 Slot(FleetDoctrine d, int n)
    {
        var s = d.Spacing;
        var side = (n & 1) == 1 ? 1 : -1;
        var rank = (n + 1) / 2;
        return d.Formation switch
        {
            FleetFormation.Column => new Vector3(0, 0, -n * s),
            FleetFormation.Line => new Vector3(side * rank * s, 0, 0),
            FleetFormation.Screen => new Vector3(MathF.Sin(n * 1.2566f) * s, 0, MathF.Cos(n * 1.2566f) * s),
            _ => new Vector3(side * rank * s, 0, -rank * s),
        };
    }

    // ---- The admiral and succession -----------------------------------------------------------------------------------------------

    /// <summary>The admiral's record if alive, free and aboard a member (its flagship kept up to date); -1 else.</summary>
    public int AdmiralOf(CrewWorld crew, CrewGroup g)
    {
        if (g.AdmiralUid == 0)
            return -1;
        for (var k = 0; k < g.Count; k++)
            if (crew.ShipOf(g.Ships[k]) is { } s)
                for (var i = 0; i < s.MemberCount; i++)
                {
                    var m = s.Members[i];
                    if (crew.Uid[m] != g.AdmiralUid)
                        continue;
                    if (crew.State[m] is CrewState.Dead or CrewState.Surrendered)
                        return -1;
                    g.Flagship = s.Body;
                    return m;
                }
        return -1;
    }

    /// <summary>
    /// The admiral is lost (killed, taken with its ship, or its ship gone): the doctrine decides. Captain: after the delay the senior captain
    /// on the strongest ship steps up. Break: every ship runs from the enemy, then its captain is on its own. Flagship: only a ship with a radio
    /// fit carries an admiral; none, the group goes home. False: no admiral this think.
    /// </summary>
    private bool Succeed(CrewWorld crew, ConstructWorld w, CrewGroup g, ICrewHost? host)
    {
        if (g.AdmiralUid != 0)
        {
            var name = CrewNames.Of(g.AdmiralName);
            g.Post(FleetReport.AdmiralLost, g.Flagship, 0, crew.Tick);
            crew.Events.Add($"fleet: group {g.Number} lost Admiral {name} ({g.Doctrine.Name} doctrine: {g.Doctrine.Succession})");
            News.Add($"Admiral {name} of {PowerName(g.Power)} is lost: group {g.Number} {(g.Doctrine.Succession == FleetSuccession.Break ? "scatters" : "is in disarray")}");
            g.AdmiralUid = 0;
            g.LeaderlessSince = crew.Tick;
            g.DisarrayUntil = crew.Tick + (long)(g.Doctrine.Delay * World.TickRate);
            if (g.Doctrine.Succession == FleetSuccession.Break)
            {
                Scatter(crew, w, g);
                return false;
            }
        }
        if (crew.Tick < g.DisarrayUntil)
            return false;
        // The senior captain on the strongest ship that can carry an admiral.
        CrewShip? best = null;
        var bestScore = float.MinValue;
        for (var k = 0; k < g.Count; k++)
        {
            var s = crew.ShipOf(g.Ships[k])!;
            if (g.Doctrine.Succession == FleetSuccession.Flagship && s.NetCount == 0)
                continue;
            if (CaptainOf(crew, s) < 0)
                continue;
            var score = s.Strength * s.Hull + crew.Alive(s);
            if (score > bestScore)
            {
                bestScore = score;
                best = s;
            }
        }
        if (best == null)
        {
            crew.Events.Add($"fleet: group {g.Number}: no ship can carry an admiral ({g.Doctrine.Succession}): it goes home");
            News.Add($"{PowerName(g.Power)}'s group {g.Number} turns for home, leaderless");
            for (var k = 0; k < g.Count; k++)
                if (crew.ShipOf(g.Ships[k]) is { } s)
                    s.Order = new ShipOrder { Kind = OrderKind.Retreat, At = g.Home, Until = crew.Tick + 120 * World.TickRate };
            Dissolve(crew, g, "leaderless");
            return false;
        }
        var m = CaptainOf(crew, best);
        crew.Job[m] = CrewJob.Admiral;
        g.AdmiralUid = crew.Uid[m];
        g.AdmiralName = crew.Name[m];
        g.Flagship = best.Body;
        best.CaptainUid = 0; // (its ship needs another captain)
        CaptainOf(crew, best);
        g.Post(FleetReport.Command, best.Body, 0, crew.Tick);
        var secs = (crew.Tick - g.LeaderlessSince) / (float)World.TickRate;
        crew.Events.Add($"fleet: group {g.Number}: Captain {CrewNames.Of(g.AdmiralName)} (construct {best.Body}) takes command, {secs:0.0} s after the admiral was lost");
        News.Add($"Captain {CrewNames.Of(g.AdmiralName)} takes command of {PowerName(g.Power)}'s group {g.Number}");
        return true;
    }

    /// <summary>A broken group: each ship runs from what it was fighting for a minute, then its captain is on its own; the group is gone.</summary>
    private void Scatter(CrewWorld crew, ConstructWorld w, CrewGroup g)
    {
        for (var k = 0; k < g.Count; k++)
            if (crew.ShipOf(g.Ships[k]) is { } s && w.Get(s.Body) is { } c)
            {
                var from = crew.Tick - s.TargetTick < 5 * World.TickRate ? s.TargetAt : c.WorldCom + Vector3.UnitX;
                var way = c.WorldCom - from;
                way.Y = 0;
                way = way.LengthSquared() > 1 ? Vector3.Normalize(way) : Vector3.UnitX;
                s.Order = new ShipOrder { Kind = OrderKind.Retreat, At = c.WorldCom + way * 2000, Until = crew.Tick + 60 * World.TickRate };
            }
        Dissolve(crew, g, "it broke when its admiral was lost");
    }

    /// <summary>A ship's captain (the senior member: at the helm first, then the most skilled), chosen again when lost; -1: nobody left to captain it.</summary>
    public static int CaptainOf(CrewWorld crew, CrewShip s)
    {
        var best = -1;
        var bestScore = -1;
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (crew.State[m] is CrewState.Dead or CrewState.Surrendered || crew.Job[m] == CrewJob.Admiral)
                continue;
            if (s.CaptainUid != 0 && crew.Uid[m] == s.CaptainUid)
                return m;
            var score = crew.Skill[m] + (crew.Job[m] == CrewJob.Pilot ? 1000 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                best = m;
            }
        }
        if (best >= 0)
            s.CaptainUid = crew.Uid[best];
        return best;
    }

    private static int SeniorSpare(CrewWorld crew, CrewShip s)
    {
        var best = -1;
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (crew.Job[m] == CrewJob.Spare && crew.State[m] is CrewState.Free or CrewState.Station && (best < 0 || crew.Skill[m] > crew.Skill[best]))
                best = m;
        }
        return best;
    }

    private static string PowerName(byte power) => CrewPowers.Of(power)?.Name ?? "Someone";

    /// <summary>A report from a captain (the crew's look): into its group's ring.</summary>
    public void Report(CrewWorld crew, CrewShip s, FleetReport kind, ushort other)
    {
        if (Of(s.Group) is { } g)
            g.Post(kind, s.Body, other, crew.Tick);
    }

    /// <summary>An officer's resistance to questioning: its doctrine's, give or take a fifth by the person (its uid), 0.05 to 0.95.</summary>
    public static float ResistanceOf(uint uid, FleetDoctrine d) => Math.Clamp(d.Resistance + ((int)(uid * 2654435761u >> 24) % 41 - 20) / 100f, 0.05f, 0.95f);

    /// <summary>Seconds of questioning to break an officer of <paramref name="resistance"/>: with an Interrogator of <paramref name="skill"/> (0-255), or alone (skill -1: the rules' aloneTimes as long).</summary>
    public float InterrogationSeconds(float resistance, int skill) =>
        Rules.InterrogateSeconds * (resistance / 0.5f) * (skill < 0 ? Rules.InterrogateAlone : 1f / (0.5f + skill / 255f)); // (a skilled interrogator at 255 takes two thirds as long, an unskilled one twice)

    // ---- Saves (the world's Fleet section: WorldSaveWriter.FleetTag; each ship's group in its crew block) -------------------------------

    public const byte SaveVersion = 1;

    /// <summary>Every live group's header (its ships rejoin from their crew blocks); null: none.</summary>
    public byte[]? SaveBytes()
    {
        if (Live == 0)
            return null;
        var wr = new SaveWriter(256 + Live * 160);
        wr.U8(SaveVersion).U16((ushort)_number).U16((ushort)Live);
        foreach (var g in Groups)
        {
            if (!g.Live)
                continue;
            wr.U8((byte)g.Slot).Str(CrewPowers.Of(g.Power)?.Name ?? "").U8((byte)g.Task).Str(g.Doctrine.Name).U32(g.AdmiralUid).U16(g.AdmiralName).U16(g.Number);
            wr.U8((byte)g.Order).Vec(g.OrderAt).Vec(g.Home).U8((byte)g.RouteCount);
            for (var i = 0; i < g.RouteCount; i++)
                wr.Vec(g.Route[i]).U8((byte)g.RouteWork[i]);
            wr.U8((byte)g.RouteAt).F32(g.Dwell).U8((byte)((g.Loop ? 1 : 0) | (g.Retreating ? 2 : 0))).F32(g.Strength0).I32(g.Losses);
        }
        return wr.Span.ToArray();
    }

    /// <summary>A saved world's groups: headers into their slots, waiting <see cref="LoadGraceSeconds"/> for their ships. A section that doesn't read leaves no groups (logged).</summary>
    public void Load(CrewWorld crew, byte[] bytes)
    {
        Clear();
        try
        {
            var r = new SaveReader(bytes);
            var v = r.U8();
            if (v != SaveVersion)
            {
                Console.WriteLine($"[fleet] the Fleet section is of version {v} (this build reads {SaveVersion}): no groups");
                return;
            }
            _number = r.U16();
            int n = r.U16();
            for (var k = 0; k < n; k++)
            {
                var g = Groups[r.U8()];
                g.Clear();
                var pname = r.Str();
                g.Power = CrewPowers.Find(pname);
                g.Task = (GroupTask)Math.Min((int)r.U8(), (int)GroupTask.Construction);
                var dname = r.Str();
                g.Doctrine = Rules.Doctrines.TryGetValue(dname, out var d) ? d : Rules.DoctrineOf(g.Power);
                g.AdmiralUid = r.U32();
                g.AdmiralName = r.U16();
                g.Number = r.U16();
                g.Order = (OrderKind)Math.Min((int)r.U8(), (int)OrderKind.Route);
                g.OrderAt = r.Vec();
                g.Home = r.Vec();
                g.RouteCount = Math.Min((int)r.U8(), CrewGroup.MaxRoute);
                for (var i = 0; i < g.RouteCount; i++)
                {
                    g.Route[i] = r.Vec();
                    g.RouteWork[i] = (OrderKind)Math.Min((int)r.U8(), (int)OrderKind.Route);
                }
                g.RouteAt = Math.Min((int)r.U8(), Math.Max(0, g.RouteCount - 1));
                g.Dwell = r.F32();
                var flags = r.U8();
                g.Loop = (flags & 1) != 0;
                g.Retreating = (flags & 2) != 0;
                g.Strength0 = r.F32();
                g.Losses = r.I32();
                if (g.Power == 0)
                {
                    Console.WriteLine($"[fleet] saved group {g.Number} was of power \"{pname}\", which isn't read now: left off");
                    g.Clear();
                    continue;
                }
                g.Live = true;
                g.Serial++;
                g.ThinkAt = crew.Tick + (long)(LoadGraceSeconds * World.TickRate); // (its ships rejoin from their crew blocks first)
                Live++;
            }
            Console.WriteLine($"[fleet] {Live} groups came back from the save");
        }
        catch (WorldFileException ex)
        {
            Console.WriteLine($"[fleet] the Fleet section didn't read ({ex.Message}): no groups");
            Clear();
        }
    }

    /// <summary>A loaded group waits this long for its ships before its admiral thinks (s).</summary>
    public const float LoadGraceSeconds = 20;

    /// <summary>A ship from a save rejoins its group's slot (the Fleet section is read before bodies' crews).</summary>
    public void Rejoin(CrewWorld crew, Construct c, CrewShip s, int slot)
    {
        if (Of(slot) is not { } g || g.Power != s.Faction || g.Count >= CrewGroup.MaxShips)
        {
            Console.WriteLine($"[fleet] construct {c.Id}'s saved group (slot {slot}) isn't there or isn't its power's: its captain is on its own");
            return;
        }
        for (var k = 0; k < g.Count; k++)
            if (g.Ships[k] == c.Id)
                return;
        Look(crew, c, s);
        g.Ships[g.Count++] = c.Id;
        s.Group = (short)slot;
    }

    // ---- Intelligence (the user's answers on admirals 7) -----------------------------------------------------------------------------

    /// <summary>
    /// What an officer gives up when broken: an admiral its group's captains and ships (where they are now) and the other admirals of its power it
    /// coordinates with; a captain its ship, its crew and where any of them are off it and what they do. Lines and marks (world points with a label)
    /// for the asker. Allocates (a rare event, not a tick).
    /// </summary>
    public void Intel(CrewWorld crew, ConstructWorld w, ICrewHost? host, uint uid, byte power, short groupSlot, int groupSerial, ushort shipBody, bool admiral,
        List<string> lines, List<(Vector3 At, string Label)> marks)
    {
        if (admiral)
        {
            if (Of(groupSlot) is { } g && g.Serial == groupSerial)
            {
                lines.Add($"Group {g.Number} ({g.Task}, {PowerName(g.Power)}): {g.Count} ships, {(g.AdmiralUid != 0 ? $"now under Admiral {CrewNames.Of(g.AdmiralName)}" : "leaderless")}, order {g.Order}.");
                for (var k = 0; k < g.Count; k++)
                    if (crew.ShipOf(g.Ships[k]) is { } s && w.Get(s.Body) is { } c)
                    {
                        var cap = CaptainOf(crew, s);
                        var who = cap >= 0 ? $"Captain {CrewNames.Of(crew.Name[cap])}" : "no captain";
                        lines.Add($"  {who}: crew of {crew.Alive(s)}, {(s.Group == g.Slot && s.Order.Kind != OrderKind.None ? s.Order.Kind.ToString().ToLowerInvariant() : "on its own")}.");
                        marks.Add((c.WorldCom, $"{who} ({PowerName(s.Faction)})"));
                    }
            }
            else
                lines.Add("Its group is gone: nothing it knows of it is current.");
            var others = 0;
            foreach (var o in Groups)
                if (o.Live && o.Power == power && o.Slot != groupSlot && o.AdmiralUid != 0 && w.Get(o.Flagship) is { } of)
                {
                    lines.Add($"  Admiral {CrewNames.Of(o.AdmiralName)} leads group {o.Number} ({o.Task}, {o.Count} ships).");
                    marks.Add((of.WorldCom, $"Admiral {CrewNames.Of(o.AdmiralName)}"));
                    others++;
                }
            if (others == 0)
                lines.Add("It coordinates with no other admiral now.");
            return;
        }
        if (crew.ShipOf(shipBody) is { } sh && w.Get(shipBody) is { } shc)
        {
            lines.Add($"Its ship (construct {shipBody}): crew of {crew.Alive(sh)}{(sh.Group >= 0 && Of(sh.Group) is { } sg ? $", in group {sg.Number} under Admiral {CrewNames.Of(sg.AdmiralName)}" : ", on its own")}.");
            marks.Add((shc.WorldCom, $"its ship ({PowerName(sh.Faction)})"));
            var away = 0;
            for (var i = 0; i < sh.MemberCount; i++)
            {
                var m = sh.Members[i];
                if (crew.State[m] == CrewState.Dead || crew.Uid[m] == uid)
                    continue;
                var off = crew.Player[m] != 0 && host != null && host.Feet((byte)(crew.Player[m] - 1), out var feet) && Vector3.Distance(feet, shc.WorldCom) > shc.Radius + 20;
                lines.Add($"  {CrewNames.Of(crew.Name[m])}, {crew.Job[m]}: {crew.State[m].ToString().ToLowerInvariant()}{(off ? ", off the ship" : "")}.");
                if (off && host!.Feet((byte)(crew.Player[m] - 1), out var f2))
                {
                    marks.Add((f2, $"{CrewNames.Of(crew.Name[m])} ({crew.State[m].ToString().ToLowerInvariant()})"));
                    away++;
                }
            }
            if (away == 0)
                lines.Add("  None of its crew are off the ship.");
        }
        else
            lines.Add("Its ship is gone: nothing it knows of it is current.");
    }
}
