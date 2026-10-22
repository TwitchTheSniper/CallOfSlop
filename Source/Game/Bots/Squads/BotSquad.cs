using System.Numerics;

namespace CallOfSlop;

/// <summary>What an Insane bot does for its squad (picked from its personality when the squad forms, see SquadRoster.cs).</summary>
public enum SquadRole : byte
{
    None,

    /// <summary>Picks where the squad goes; a second base of fire in an assault.</summary>
    Leader,

    /// <summary>Holds a firing position and keeps the target busy (suppressive fire at its last known spot).</summary>
    Suppressor,

    /// <summary>Goes around on a route out of the target's line of fire and hits it from the side or behind.</summary>
    Flanker,

    /// <summary>Crossfire from a second angle, trades kills, carries smoke; a second flanker in a pincer.</summary>
    Support,
}

/// <summary>What the whole squad is doing (SquadPlanner.cs).</summary>
public enum SquadTactic : byte
{
    Move, // no fresh contact: travel together (formation, bounding overwatch near trouble) to the shared objective
    Regroup, // too spread out: the leader waits, the others close up
    Assault, // a fresh contact: suppress + flank, crossfire
    Retreat, // lost most of the squad: fall back (under smoke) and regroup
}

/// <summary>The kind of order a squad member is following this moment.</summary>
public enum SquadOrderKind : byte
{
    None,
    Follow, // walk to Goal (formation slot, bound point, regroup point)
    Hold, // stay at Goal and watch Look (overwatch, a flanker waiting for the timed push)
    Suppress, // get to a firing spot (Goal) and shoot at Look (the target's last known spot) when Fire is set
    Flank, // the flank route to Goal (staging point first, then the final spot)
    Trade, // a squadmate just died: go after the killer's spot
    Fallback, // retreat to Goal
}

/// <summary>One member's current order from the squad planner.</summary>
public struct SquadOrder
{
    public SquadOrderKind Kind;
    public Vector3 Goal;
    public Vector3 Look; // a point to face (chest height already added)
    public bool HasLook;
    public bool Sprint;
    public bool HoldBack; // in a fight, keep the distance instead of closing in (bases of fire)
    public bool Fire; // Suppress: shoot at Look even without seeing anyone
    public bool FlankRoute; // plan the route around the target's line of fire
    public Vector3 FlankFrom, FlankTo;
}

/// <summary>Something a squad member saw or heard. Only what members really perceived goes in (no wallhacks).</summary>
public struct SquadContact
{
    public byte Enemy; // player id, or byte.MaxValue for a sound we couldn't put a face to
    public Vector3 Pos; // where it was perceived (feet)
    public float Yaw; // which way they were facing when seen (heard: unknown, 0)
    public double Time; // when it was last perceived
    public byte Reporter; // the member who perceived it
    public bool Seen; // seen (true) or only heard (false)
    public bool Callout; // reported by a squadmate as they died ("he's over there")
}

/// <summary>
/// The squad's shared picture of the enemy: a handful of contacts, each from a member's own eyes or ears (or a dying
/// member's last look at their killer). Contacts age and are forgotten: seen ones after <see cref="SeenMemory"/>
/// seconds, heard ones after <see cref="HeardMemory"/>. The planner never looks at real enemy positions.
/// </summary>
public sealed class SquadBlackboard
{
    public const int Capacity = 8;
    public const double SeenMemory = 9;
    public const double HeardMemory = 4;

    private readonly SquadContact[] _items = new SquadContact[Capacity];

    public int Count { get; private set; }

    public SquadContact this[int i] => _items[i];

    /// <summary>Adds or refreshes a contact: by enemy id when known, else by being within 5 m of an unknown sound.</summary>
    public void Report(byte enemy, Vector3 pos, float yaw, double now, byte reporter, bool seen, bool callout = false)
    {
        var slot = -1;
        for (var i = 0; i < Count; i++)
        {
            ref var c = ref _items[i];
            if ((enemy != byte.MaxValue && c.Enemy == enemy) || (c.Enemy == byte.MaxValue && Vector3.DistanceSquared(c.Pos, pos) < 25f))
            {
                slot = i;
                break;
            }
        }
        if (slot >= 0)
        {
            ref var c = ref _items[slot];
            if (!seen && c.Seen && now - c.Time < 1.5)
                return; // a fresh sighting beats a sound
            c.Enemy = enemy != byte.MaxValue ? enemy : c.Enemy;
        }
        else
        {
            if (Count < Capacity)
                slot = Count++;
            else
            {
                slot = 0; // full: replace the oldest
                for (var i = 1; i < Count; i++)
                    if (_items[i].Time < _items[slot].Time)
                        slot = i;
            }
            _items[slot].Enemy = enemy;
        }
        ref var s = ref _items[slot];
        s.Pos = pos;
        s.Yaw = seen ? yaw : s.Yaw;
        s.Time = now;
        s.Reporter = reporter;
        s.Seen = seen;
        s.Callout = callout;
    }

    /// <summary>Forgets contacts that are too old, and any about a player who has died since (they respawn elsewhere).</summary>
    public void Decay(double now, Func<byte, bool> stillAlive)
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            var c = _items[i];
            var age = now - c.Time;
            if (age > (c.Seen ? SeenMemory : HeardMemory) || (c.Enemy != byte.MaxValue && !stillAlive(c.Enemy)))
                _items[i] = _items[--Count];
        }
    }

    /// <summary>Removes one enemy's contact (they were killed).</summary>
    public void Forget(byte enemy)
    {
        for (var i = Count - 1; i >= 0; i--)
            if (_items[i].Enemy == enemy)
                _items[i] = _items[--Count];
    }

    public bool TryGet(byte enemy, out SquadContact contact)
    {
        for (var i = 0; i < Count; i++)
            if (_items[i].Enemy == enemy)
            {
                contact = _items[i];
                return true;
            }
        contact = default;
        return false;
    }

    public void Clear() => Count = 0;
}

/// <summary>
/// A squad of Insane bots on one team (2-4 members, usually 3-4): roles, the shared blackboard, the current tactic and
/// the per-node scratch the planner and the members' route searches share. Owned by Game (SquadRoster.cs).
/// </summary>
public sealed class BotSquad
{
    public readonly int Id;
    public byte Team;
    public readonly List<Player> Members = new(4);
    public readonly SquadBlackboard Board = new();

    public Player? Leader; // by role
    public Player? Acting; // who leads right now (the leader, or the next one alive)
    public SquadTactic Tactic;
    public double TacticSince;
    public double NextPlan;

    // ---- Move ----
    public Vector3 Goal;
    public double GoalUntil;
    public bool GoalIsObjective;
    public int BoundGroup; // bounding overwatch: which element moves (0: leader + suppressor, 1: the rest)
    public double BoundSince;
    public Vector3 BoundPoint;
    public bool Bounding;
    public double LastContactTime = -100;
    public Vector3 LastContactPos;

    // ---- Assault ----
    public byte Target = byte.MaxValue; // the contact being assaulted (enemy id)
    public Vector3 TargetAt; // where the plan was made around
    public double AssaultPlannedAt;
    public bool Executing; // flankers released from their staging points
    public double ExecuteAt;
    public bool ThrewFrag;
    public readonly Vector3[] FlankStage = new Vector3[4];
    public readonly Vector3[] FlankGoal = new Vector3[4];
    public readonly Vector3[] FireSpot = new Vector3[4];
    public readonly float[] FlankEta = new float[4];

    // ---- Trades and retreat ----
    public Player? Trader;
    public Vector3 TradeAt;
    public byte TradeTarget = byte.MaxValue;
    public double TradeUntil;
    public Vector3 Rally;

    // ---- Route scratch: which nodes are in the target's line of fire, which nodes a squadmate's route already uses ----
    internal byte[] Exposure = []; // 1 safe, 2 exposed (valid when ExposureStamp == ExposureGen)
    internal int[] ExposureStamp = [];
    internal int ExposureGen = 1;
    internal Vector3 ExposureEye;
    internal bool ExposureOn;
    internal int ExposureRays; // ray casts left for the current search
    internal int[] ClaimStamp = [];
    internal byte[] ClaimBy = [];
    internal int ClaimGen = 1;
    internal NavGraph? ScratchGraph;

    public BotSquad(int id, byte team)
    {
        Id = id;
        Team = team;
    }

    public int Alive
    {
        get
        {
            var n = 0;
            foreach (var m in Members)
                if (m.Alive)
                    n++;
            return n;
        }
    }

    /// <summary>Sizes the per-node scratch for this nav graph (once per map).</summary>
    internal void EnsureScratch(NavGraph g)
    {
        if (ScratchGraph == g)
            return;
        ScratchGraph = g;
        var n = g.Nodes.Length;
        Exposure = new byte[n];
        ExposureStamp = new int[n];
        ClaimStamp = new int[n];
        ClaimBy = new byte[n];
        ExposureGen = ClaimGen = 1;
    }

    /// <summary>Index of a member in <see cref="Members"/> (slot for the per-member arrays), or -1.</summary>
    public int SlotOf(Player p)
    {
        for (var i = 0; i < Members.Count; i++)
            if (Members[i] == p)
                return i;
        return -1;
    }
}

/// <summary>Counters for the Insane squads this session (server only; read by the self-test and logged).</summary>
public sealed class SquadStats
{
    public int Plans, Assaults, Executes, Regroups, Retreats, Trades, TradedKills, SuppressShots, Frags, Smokes, ExposureRays;
    public double PlanMs; // wall-clock time spent planning (not used for any decision)
    public int FlankKills;
    public long HeldFireTicks, SuppressTicks, SuppressFireTicks, SuppressLosTicks; // diagnostics: flankers holding fire; bases of fire with a Suppress order (with Fire set, with a line)
}
