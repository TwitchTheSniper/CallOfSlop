using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// An NPC the horde controller (<see cref="HordeController{T}"/>) can steer as part of a group: Zombies' zombies today,
/// the co-op enemies (Source/Game/Pve/) could implement it later (docs/pve/DESIGN.md, "Horde controller"). The agent
/// keeps its own state machine and body; the controller only owns its <see cref="Member"/> record (group, think
/// schedule, flow-field route, queue spot, reactions) and answers steering questions.
/// </summary>
public interface IHordeAgent
{
    /// <summary>The agent's horde record (one per agent, kept for its whole life; <see cref="HordeMember.Reset"/> on reuse).</summary>
    HordeMember Member { get; }

    /// <summary>Feet position (server).</summary>
    Vector3 Position { get; }

    /// <summary>Alive and steered by the horde (dead, rising or climbing agents are left alone).</summary>
    bool HordeActive { get; }

    /// <summary>
    /// The barrier (portal) it is working towards from outside (a Zombies window), or -1 once it is inside. Members of a
    /// portal queue up at its spots (<see cref="HordeMember.QueueRank"/>).
    /// </summary>
    int HordePortal { get; }

    /// <summary>At the front of its portal's queue working the barrier (a zombie tearing boards).</summary>
    bool HordeAtPortal { get; }

    /// <summary>Stable small id for deterministic ordering and per-agent hashing (the zombie's id).</summary>
    ushort HordeId { get; }
}

/// <summary>What a horde group is doing as a whole.</summary>
public enum HordeGroupMode : byte
{
    /// <summary>Going for its target (a player, a lure, or its portal).</summary>
    Advance,

    /// <summary>Blocked (no progress): the members close up on the group's centre and hold, then go again.</summary>
    Regroup,
}

/// <summary>A stimulus the horde reacts to as groups (<see cref="HordeController{T}.Stimulus"/>).</summary>
public enum HordeStimulusKind : byte
{
    /// <summary>A gunshot: groups in earshot that aren't already fighting turn on the shooter.</summary>
    Gunfire,

    /// <summary>A blast: members near it stagger and scatter away, then regroup; the group turns on the thrower.</summary>
    Explosion,

    /// <summary>A player went down: every group after them picks someone else.</summary>
    Downed,

    /// <summary>A power-up was grabbed: a loud noise at the grabber (like a gunshot, heard further).</summary>
    PowerUp,

    /// <summary>A member was shot: its neighbours that aren't fighting turn on the shooter.</summary>
    Hurt,

    /// <summary>A lure (a noise maker): members in range go to it and mill round it until it ends.</summary>
    Lure,
}

/// <summary>Something the horde can go for: a standing player, with its zone (Zombies area).</summary>
public struct HordeTarget
{
    /// <summary>A target this tick (the slot is in use), and last tick.</summary>
    public bool Live, WasLive;

    public byte Id;
    public Vector3 Pos;
    public int Area;

    /// <summary>Nearest nav node (the controller keeps it; -1 unknown).</summary>
    public int Node;

    /// <summary>Members of its group, and how many are within reach of it (worked out each tick).</summary>
    public int Pressure, Engaged;
}

/// <summary>A barrier the horde comes in through (a Zombies window): its outside spot, which way is in, and the queue spots behind it.</summary>
public sealed class HordePortal
{
    public Vector3 Front, In;
    public int Area;
    public bool Open;

    /// <summary>Queue spots behind the front, nearest first (checked walkable and in view of the front when the map loads).</summary>
    public Vector3[] Spots = [];

    /// <summary>Nearest nav node of <see cref="Front"/>.</summary>
    public int Node = -1;

    /// <summary>Members queueing (worked out each tick).</summary>
    public int Queue;
}

/// <summary>A horde group: the members that share a goal and decide together (fixed slots: one per portal, one per target, one lure group).</summary>
public sealed class HordeGroup
{
    public int Index;
    public HordeGroupMode Mode;
    public double ModeUntil;

    /// <summary>Members, their centre, and their mean distance to the group's goal (this tick).</summary>
    public int Count;

    public Vector3 Centre;
    public float MeanDist;

    /// <summary>The mean distance when progress was last checked, and when (the blocked check).</summary>
    public float CheckDist = float.MaxValue;

    public double CheckAt;

    /// <summary>
    /// Where the group's target was when progress was last checked: a target that walked off since isn't a blocked group
    /// (the members were chasing it, not stuck), so the check starts again instead of regrouping.
    /// </summary>
    public Vector3 CheckTarget;

    /// <summary>Times this group regrouped, split members off and took members in (self-test and log).</summary>
    public int Regroups, SplitOut, Joined;

    internal Vector3 SumPos;
    internal float SumDist;
}

/// <summary>One stimulus in the controller's ring.</summary>
public struct HordeStimulus
{
    public HordeStimulusKind Kind;
    public Vector3 Pos;
    public float Radius;

    /// <summary>The player behind it (shooter, thrower, grabber, the downed one), or 255.</summary>
    public byte Source;

    public double At, Until;
}

/// <summary>
/// The controller's record of one agent: its group, its think schedule (level of detail), its route on a shared flow
/// field, its spot in a portal queue and its reactions. Plain fields, reset on reuse (no allocation).
/// </summary>
public sealed class HordeMember
{
    /// <summary>A stable slot (0..capacity-1) while alive: per-client network state is kept by slot.</summary>
    public int Slot = -1;

    /// <summary>Group index (<see cref="HordeController{T}.Groups"/>), -1 none.</summary>
    public int Group = -1;

    /// <summary>Level of detail: 0 near (thinks every tick) .. 3 very far; <see cref="ThinkNow"/>: this tick it thinks.</summary>
    public byte Tier;

    public bool ThinkNow;

    /// <summary>Straight distance to the nearest target this tick.</summary>
    public float NearestDist = float.MaxValue;

    /// <summary>The target slot its group goes for (index into <see cref="HordeController{T}.Targets"/>), -1 none.</summary>
    public int TargetSlot = -1;

    /// <summary>Route: the nav node it is at or leaving, and the goal node of the flow field it follows.</summary>
    public int Node = -1, FlowGoal = -1;

    /// <summary>Reactions: scattering away from a blast until <see cref="FleeUntil"/>; holding (regroup) until <see cref="HoldUntil"/>.</summary>
    public double FleeUntil, HoldUntil;

    public Vector3 FleeFrom;

    /// <summary>Its place in its portal's queue (0 at the front; -1 none) and the spot that goes with it.</summary>
    public int QueueRank = -1;

    public Vector3 QueueSpot;

    /// <summary>The angle it closes in from when surrounding its target (from its id).</summary>
    public float SurroundAngle;

    /// <summary>When it last changed group (hysteresis: it doesn't flip every think).</summary>
    public double GroupSince;

    /// <summary>Its index in the agent list this tick (the grid's id for it).</summary>
    public int Index;

    /// <summary>The last think found its goal close and in plain view: it walks straight at it.</summary>
    public bool Straight;

    /// <summary>
    /// The stuck watch's recovery (board item dev-zombies-stuck-railings-zr01): no walking straight at the goal until
    /// <see cref="NoStraightUntil"/> (follow the field round whatever blocked it), and back to its route node first until
    /// <see cref="ToNodeUntil"/> (off the edge's line it was pushing into a corner or a railing).
    /// </summary>
    public double NoStraightUntil, ToNodeUntil;

    /// <summary>What it slides along (the flat normal of the wall or railing ahead; zero: nothing) until tick <see cref="SlideUntil"/> (HordeController.Slide).</summary>
    public Vector3 SlideNormal;

    public long SlideUntil;

    /// <summary>Its next step is up or down a staircase or ramp this tick (narrower separation there: crowding pushes into the railings).</summary>
    public bool Climbing;

    public void Reset()
    {
        Slot = -1;
        Group = -1;
        Tier = 0;
        ThinkNow = true;
        NearestDist = float.MaxValue;
        TargetSlot = -1;
        Node = FlowGoal = -1;
        FleeUntil = HoldUntil = 0;
        FleeFrom = default;
        QueueRank = -1;
        QueueSpot = default;
        SurroundAngle = 0;
        GroupSince = 0;
        Index = 0;
        Straight = false;
        NoStraightUntil = ToNodeUntil = 0;
        Climbing = false;
        SlideNormal = default;
        SlideUntil = 0;
    }
}
