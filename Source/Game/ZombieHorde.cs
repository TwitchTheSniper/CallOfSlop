using System.Numerics;

namespace CallOfSlop;

/// <summary>A zombie is a horde agent (Source/Game/Horde/): the controller owns its group, think schedule, route and queue spot.</summary>
public sealed partial class Zombie : IHordeAgent
{
    /// <summary>Its horde record (server).</summary>
    public readonly HordeMember Hm = new();

    HordeMember IHordeAgent.Member => Hm;

    public bool HordeActive => State != ZState.Dead;

    /// <summary>Outside (walking to, queueing at, tearing or climbing through a window): that window.</summary>
    public int HordePortal => State == ZState.Dead ? -1 : Window;

    public bool HordeAtPortal => Window >= 0 && State is ZState.Tearing or ZState.Climbing or ZState.Attack;

    public ushort HordeId => Id;

    /// <summary>Client: when the last <see cref="Msg.ZEnts"/> with it arrived (local clock; interest management sends far ones less often).</summary>
    public double LastHeard;
}

/// <summary>
/// The Zombies side of the horde controller (docs/zombies/ZOMBIES-MODE.md, "Hordes" and "Horde controller"): the
/// windows become portals with queue spots, the standing players targets, and the game's events stimuli (gunfire,
/// blasts, downs, grabbed power-ups, hurt zombies). The alive cap by round and the host's horde cap.
/// </summary>
public sealed partial class Game
{
    /// <summary>The horde controller for the zombies (server).</summary>
    public readonly HordeController<Zombie> HordeAI = new();

    /// <summary>
    /// The host's "Zombie horde cap" setting (Settings.ZombieHordeCap): the most zombies alive at once whatever the round
    /// and the rules say (weak PCs lower it). <see cref="MaxZombies"/>: no extra limit.
    /// </summary>
    public int HostHordeCap = MaxZombies;

    /// <summary>The choices the lobby offers for <see cref="HostHordeCap"/> (the last: no extra limit), and the least it can be.</summary>
    public static readonly int[] HordeCapChoices = [48, 100, 150, 200, 300, 400, MaxZombies];

    public const int MinHordeCap = 24;

    private Map? _hordeMap;
    private ZombiesRules? _hordeRules;
    private int[] _zDoorNode = [];
    private NavGraph? _zDoorNodeGraph;
    private bool[] _zClimbing = [];
    private readonly Stack<int> _zFreeSlots = new(MaxZombies);
    private int _zLureNode = -1, _zLureArea = -1;
    private int _zAlive;

    /// <summary>The alive cap now: the round's curve (with the extra players), the rules' hard cap, the host's setting.</summary>
    public int ZAliveCap()
    {
        var players = Math.Max(1, Players.Count - (ZHostSitsOut ? 1 : 0));
        return Math.Clamp(Math.Min(ZRules.MaxAliveFor(Math.Max(1, ZRound), players), HostHordeCap), 1, MaxZombies);
    }

    /// <summary>The map's portals (one per window, with its queue spots), set up once per map and rules.</summary>
    private void HordeSetup(ZombiesMap zm)
    {
        if (ReferenceEquals(_hordeMap, World.Map) && ReferenceEquals(_hordeRules, ZRules) && HordeAI.Portals.Length == zm.Windows.Count)
            return;
        _hordeMap = World.Map;
        _hordeRules = ZRules;
        HordeAI.Tuning = ZRules.Horde;
        var portals = new HordePortal[zm.Windows.Count];
        var spots = new List<Vector3>(24);
        for (var i = 0; i < zm.Windows.Count; i++)
        {
            var w = zm.Windows[i];
            var inward = w.Inside - w.Outside;
            inward.Y = 0;
            inward = inward.LengthSquared() > 1e-6f ? Vector3.Normalize(inward) : new Vector3(0, 0, -1);
            var side = new Vector3(-inward.Z, 0, inward.X);
            spots.Clear();
            // Rows of three behind the window, nearest first: each spot standable and in plain view of the window's outside spot.
            for (var row = 1; row <= 7 && spots.Count < 18; row++)
                for (var c = 0; c < 3; c++)
                {
                    var col = c == 0 ? 0 : c == 1 ? -1 : 1;
                    var p = w.Outside - inward * (0.95f * row) + side * (0.9f * col);
                    if (World.Blocked(p + new Vector3(0, 0.05f, 0)) || !NavGraph.Clear(w.Outside, p))
                        continue;
                    spots.Add(p);
                }
            portals[i] = new HordePortal { Front = w.Outside, In = inward, Area = w.Area, Spots = spots.ToArray() };
        }
        HordeAI.Setup(World.Map.HalfX, World.Map.HalfZ, portals);
        HordeAI.SetZones(p => World.Map.Zombies?.AreaAt(p) ?? 0);
        HordeAI.SetPortalMover(MoveToWindow);
        _zClimbing = new bool[zm.Windows.Count];
        _zDoorNodeGraph = null;
        _zFreeSlots.Clear();
        for (var s = MaxZombies - 1; s >= 0; s--)
            _zFreeSlots.Push(s);
        foreach (var z in Horde)
            if (z.Hm.Slot >= 0)
                RemoveFreeSlot(z.Hm.Slot);
    }

    private void RemoveFreeSlot(int slot)
    {
        // (only after a map change with zombies still in the list; rare)
        var keep = _zFreeSlots.Where(s => s != slot).Reverse().ToArray();
        _zFreeSlots.Clear();
        foreach (var s in keep)
            _zFreeSlots.Push(s);
    }

    /// <summary>The controller sends a queueing zombie round to another window (the back of a long queue).</summary>
    private bool MoveToWindow(Zombie z, int window)
    {
        if (z.State != ZState.ToWindow || World.Map.Zombies is not { } zm || window < 0 || window >= zm.Windows.Count || !AreaOpen(zm.Windows[window].Area))
            return false;
        z.Window = window;
        z.StuckAt = _zNow;
        z.StuckPos = z.Position;
        return true;
    }

    /// <summary>The nav node of a door's side in <paramref name="area"/> (worked out once per graph).</summary>
    private int DoorSideNode(int door, int area, bool outSide)
    {
        if (World.Map.Zombies is not { } zm || door < 0 || door >= zm.Doors.Count)
            return -1;
        var nav = NavGraph.Current;
        if (!ReferenceEquals(_zDoorNodeGraph, nav) || _zDoorNode.Length != zm.Doors.Count * 2)
        {
            _zDoorNodeGraph = nav;
            _zDoorNode = new int[zm.Doors.Count * 2];
            for (var d = 0; d < zm.Doors.Count; d++)
            {
                _zDoorNode[d * 2] = nav.Nodes.Length > 0 ? nav.Nearest(zm.Doors[d].SideA, needSight: false) : -1;
                _zDoorNode[d * 2 + 1] = nav.Nodes.Length > 0 ? nav.Nearest(zm.Doors[d].SideB, needSight: false) : -1;
            }
        }
        var dr = zm.Doors[door];
        var isA = (area == dr.AreaA) != outSide;
        return _zDoorNode[door * 2 + (isA ? 0 : 1)];
    }

    /// <summary>Server, at the start of the horde's tick: the targets (players standing), open windows, climbing windows, then the controller's tick.</summary>
    private void HordeBegin(ZombiesMap zm, double now)
    {
        HordeSetup(zm);
        var ai = HordeAI;
        ai.ClearTargets();
        // Players standing, in id order (deterministic).
        for (var id = 0; id < 256 && ai.TargetCount < HordeController<Zombie>.MaxTargets; id++)
            if (Players.TryGetValue((byte)id, out var p) && IsStanding(p))
                ai.AddTarget(p.Id, p.Position, zm.AreaAt(p.Position));
            else if (id > 0 && Players.Count == 0)
                break;
        for (var i = 0; i < ai.Portals.Length; i++)
            ai.Portals[i].Open = AreaOpen(zm.Windows[i].Area);
        Array.Clear(_zClimbing);
        _zAlive = 0;
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            _zAlive++;
            if (z.State == ZState.Climbing && z.Window >= 0 && z.Window < _zClimbing.Length)
                _zClimbing[z.Window] = true;
        }
        var nav = NavGraph.Current;
        ai.BeginTick(Horde, nav, ServerTick, now);
        if (ai.LureUntil >= 0)
        {
            _zLureNode = nav.Nodes.Length > 0 ? nav.Nearest(ai.LurePos, needSight: false) : -1;
            _zLureArea = zm.AreaAt(ai.LurePos);
        }
    }

    // ---- Stimuli from the game ---------------------------------------------------------------------------

    /// <summary>Server: a player fired a gun in Zombies: groups in earshot that aren't fighting turn on them.</summary>
    private void HordeGunfire(Player shooter, double now)
    {
        if (Horde.Count > 0)
            HordeAI.Stimulus(HordeStimulusKind.Gunfire, shooter.Position, 0, shooter.Id, now);
    }

    /// <summary>Server: a blast at <paramref name="at"/>: the zombies near it stagger and scatter, then regroup and turn on the thrower.</summary>
    private void HordeExplosion(Player? owner, Vector3 at, float radius, double now)
    {
        if (Horde.Count == 0)
            return;
        HordeAI.Stimulus(HordeStimulusKind.Explosion, at, radius, owner?.Id ?? 255, now);
        var stagger = ZRules.Horde.ExplosionStagger;
        if (stagger <= 0)
            return;
        var r2 = radius * radius * ZRules.Horde.ScatterScale * ZRules.Horde.ScatterScale;
        foreach (var z in Horde)
            if (z.State is ZState.Chase or ZState.ToWindow && Vector3.DistanceSquared(z.Position, at) < r2)
                z.StunnedUntil = Math.Max(z.StunnedUntil, now + stagger);
    }

    /// <summary>Server: <paramref name="p"/> went down: every group after them picks someone else.</summary>
    private void HordeDowned(Player p, double now) => HordeAI.Stimulus(HordeStimulusKind.Downed, p.Position, 0, p.Id, now);

    /// <summary>Server: <paramref name="p"/> grabbed a power-up: a loud noise (heard further than a gunshot).</summary>
    private void HordePowerUp(Player p, double now) => HordeAI.Stimulus(HordeStimulusKind.PowerUp, p.Position, 0, p.Id, now);

    /// <summary>Server: zombie <paramref name="z"/> was shot by <paramref name="by"/>: its neighbours that aren't fighting turn on them.</summary>
    private void HordeHurt(Zombie z, Player by, double now) => HordeAI.Stimulus(HordeStimulusKind.Hurt, z.Position, 0, by.Id, now);

    /// <summary>Server: a lure at <paramref name="at"/> for <paramref name="seconds"/>: zombies in range go to it (the controller's lure group).</summary>
    public void HordeLure(Vector3 at, float range, double seconds, double now) => HordeAI.Stimulus(HordeStimulusKind.Lure, at, range, 255, now, seconds);

    // ---- Slots --------------------------------------------------------------------------------------------

    /// <summary>A free network slot for a new zombie (per-client state is kept by slot; ZombiesNet.cs).</summary>
    private int TakeZombieSlot() => _zFreeSlots.Count > 0 ? _zFreeSlots.Pop() : -1;

    private void FreeZombieSlot(Zombie z)
    {
        if (z.Hm.Slot >= 0 && _zFreeSlots.Count < MaxZombies)
            _zFreeSlots.Push(z.Hm.Slot);
        z.Hm.Slot = -1;
    }

    /// <summary>Every zombie gone at once (a new run or round reset): their slots back.</summary>
    private void ClearHorde()
    {
        foreach (var z in Horde)
            FreeZombieSlot(z);
        Horde.Clear();
        _zById.Clear();
        HordeAI.Reset();
        ClimbReset(); // (a siege up a face ends with the horde: ZombiesClimb.cs)
    }

    /// <summary>Self-test: the controller's numbers and group state in one line.</summary>
    public string HordeSummary()
    {
        var ai = HordeAI;
        return $"turned {ai.Turned}, scattered {ai.Scattered}, flanked {ai.Flanked}, retargeted {ai.Retargeted}, lured {ai.Lured}, regrouped {ai.Regrouped}; "
               + $"flow fields built {ai.Flow.Built} ({ai.Flow.BuildMs:0.0} ms), deferred {ai.Flow.Deferred}";
    }
}
