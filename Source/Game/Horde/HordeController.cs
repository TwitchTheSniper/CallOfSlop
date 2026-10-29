using System.Numerics;

namespace CallOfSlop;

/// <summary>The horde controller's numbers (the rules file's "horde" section: Parts/Tables/ZombiesRules.json).</summary>
public sealed class HordeTuning
{
    /// <summary>Level of detail: members nearer a target than these (m) are tier 0, 1, 2; the rest tier 3.</summary>
    public float NearRange = 12f, MidRange = 30f, FarRange = 60f;

    /// <summary>Ticks between thinks per tier (route, line-of-sight checks, target picks); movement runs every tick.</summary>
    public int[] ThinkEvery = [1, 2, 4, 8];

    /// <summary>Seconds between each group's own decisions (staggered over the groups).</summary>
    public float GroupThinkSeconds = 0.5f;

    /// <summary>Members working a barrier at once (the rest queue behind it), and the queue length past which the back of it goes round to another barrier.</summary>
    public int Tearers = 2, QueueSplitAt = 6;

    /// <summary>The furthest (route metres) a queueing member walks to another barrier, and what a member already queueing there counts as (m).</summary>
    public float FlankRange = 30f, QueueCost = 1.5f;

    /// <summary>Surrounding: members between these distances of their target close in from their own angle, on a ring this wide.</summary>
    public float SurroundMin = 2.5f, SurroundRange = 9f, SurroundRadius = 1.8f;

    /// <summary>Spreading out over the players: a member counts each other member already on a target as this many metres further (past the first few free ones).</summary>
    public float PressureWeight = 0.4f;

    public int PressureFree = 8;

    /// <summary>Another zone (area) than the target's counts as this much further (m).</summary>
    public float AreaPenalty = 20f;

    /// <summary>How far a gunshot, a grabbed power-up and a hurt member are heard (m), and the distance within which a member counts as already fighting (it doesn't turn).</summary>
    public float HearGunfire = 35f, HearPowerUp = 45f, HurtAlert = 12f, EngagedRange = 10f;

    /// <summary>Blasts: members within the blast radius x this scatter away for <see cref="ScatterSeconds"/>, then their group regroups.</summary>
    public float ScatterScale = 1.5f, ScatterSeconds = 0.6f, ExplosionStagger = 0.3f;

    /// <summary>Blocked groups: a group of at least <see cref="StuckMinMembers"/> that got less than <see cref="StuckProgress"/> m closer in <see cref="StuckSeconds"/> regroups for <see cref="RegroupSeconds"/>.</summary>
    public float StuckSeconds = 2.5f, StuckProgress = 0.6f, RegroupSeconds = 1.2f;

    public int StuckMinMembers = 4;

    /// <summary>Lures: members within this range go to it.</summary>
    public float LureRange = 25f;

    /// <summary>Separation between members: radius (m) and strength.</summary>
    public float SeparationRadius = 0.9f, SeparationStrength = 1.6f;

    /// <summary>Separation's strength on stairs and ramps, as a share of <see cref="SeparationStrength"/> (HordeMember.Climbing).</summary>
    public float StairSeparation = 0.35f;

    /// <summary>Flow fields kept, new ones built per tick, and how old (seconds) one may get before it's rebuilt.</summary>
    public int FlowFields = 24, FlowBuildsPerTick = 1;

    public float FlowRefreshSeconds = 1f;
}

/// <summary>
/// The horde controller (docs/zombies/ZOMBIES-MODE.md, "Horde controller"): owns large groups of NPCs as groups that
/// decide together, so a few hundred agents cost about what a few dozen did. Generic over the agent
/// (<see cref="IHordeAgent"/>): Zombies wires its zombies in (ZombieHorde.cs); the co-op enemies could later.
/// <para>
/// Every tick (<see cref="BeginTick"/>): a spatial grid of the members (<see cref="HordeGrid"/>), each member's level
/// of detail (near members think every tick, far ones every 2, 4 or 8 ticks, staggered by id), group membership and
/// counts, the portal queues (members line up behind a barrier instead of jamming it), the stimuli heard since the
/// last tick, and the decisions of the groups whose turn it is: a portal group whose queue is long sends its back
/// members round to another open barrier (splitting; they merge into that barrier's group), a group that is getting
/// nowhere regroups (closes up and holds, then goes again), members spread over the players by pressure.
/// </para>
/// <para>
/// Groups are fixed slots: one per portal (members outside, queueing), one per target (members inside, after that
/// player) and one lure group. Members move between them (split, merge) by the rules above and by stimuli: a gunshot,
/// a grabbed power-up or a member being shot turns members in earshot that aren't already fighting on the shooter; a
/// blast scatters the members near it, then their group regroups; a player going down sends their group to the
/// others; a lure draws the members in range to it. Per member the agent's own code still does the local part
/// (separation from the grid, attacking, climbing in), asking <see cref="Steer"/> where to walk: straight when the
/// goal is near and in view, else along a shared flow field (<see cref="HordeFlowFields"/>) towards the goal's node.
/// Deterministic (no random numbers: everything is ordered by list index and id) and allocation-free per tick.
/// </para>
/// </summary>
public sealed class HordeController<T> where T : class, IHordeAgent
{
    public const int MaxTargets = 8, MaxStimuli = 32, ScratchSize = 1024;

    public HordeTuning Tuning = new();
    public readonly HordeGrid Grid = new();
    public readonly HordeFlowFields Flow = new();

    /// <summary>
    /// What the horde can go for this tick (the game fills it with <see cref="ClearTargets"/> / <see cref="AddTarget"/>).
    /// Slots are stable: a player keeps their slot (and so their group) while they stay a target; a slot that isn't
    /// <see cref="HordeTarget.Live"/> this tick is empty.
    /// </summary>
    public readonly HordeTarget[] Targets = new HordeTarget[MaxTargets];

    /// <summary>Live targets this tick.</summary>
    public int TargetCount;

    /// <summary>Is slot <paramref name="slot"/> a live target this tick?</summary>
    public bool IsLive(int slot) => (uint)slot < MaxTargets && Targets[slot].Live;

    /// <summary>The barriers (Zombies windows), set once per map (<see cref="Setup"/>); the game keeps <see cref="HordePortal.Open"/> current.</summary>
    public HordePortal[] Portals = [];

    /// <summary>The groups: one per portal, then one per target slot, then the lure group.</summary>
    public HordeGroup[] Groups = [];

    public int LureGroup => Portals.Length + MaxTargets;

    public int TargetGroup(int slot) => Portals.Length + slot;

    /// <summary>The lure (if one is going): where, how far it draws, until when.</summary>
    public Vector3 LurePos;

    public double LureUntil = -1;

    /// <summary>Reactions counted (self-test and log): members turned by noise, scattered by blasts, sent round to another portal, retargeted after a down, lured.</summary>
    public int Turned, Scattered, Flanked, Retargeted, Lured, Regrouped;

    private readonly HordeStimulus[] _stimuli = new HordeStimulus[MaxStimuli];
    private int _stimCount;
    private readonly double[] _lastNoise = new double[256];
    private Vector3[] _pos = new Vector3[64];
    private bool[] _active = new bool[64];
    private readonly int[] _scratch = new int[ScratchSize];
    private float[] _keys = new float[64];
    private int[] _idx = new int[64];
    private readonly Vector3[] _targetNodePos = new Vector3[MaxTargets];
    private List<T>? _agents;
    private NavGraph _nav = NavGraph.Empty;
    private long _tick;
    private double _now;

    /// <summary>The tick being worked on, and the time.</summary>
    public long Tick => _tick;

    public double Now => _now;

    /// <summary>The nav graph in use this tick.</summary>
    public NavGraph Nav => _nav;

    /// <summary>Once per map: the area covered and the portals (with their queue spots).</summary>
    public void Setup(float halfX, float halfZ, HordePortal[] portals)
    {
        Grid.Setup(halfX, halfZ);
        Portals = portals;
        Groups = new HordeGroup[portals.Length + MaxTargets + 1];
        for (var i = 0; i < Groups.Length; i++)
            Groups[i] = new HordeGroup { Index = i };
        Flow.Clear();
        Reset();
    }

    /// <summary>Sizes the per-agent arrays (and the grid's) for <paramref name="n"/> agents now, so a growing horde never allocates mid-round.</summary>
    public void Reserve(int n)
    {
        Grid.Reserve(n);
        if (_pos.Length >= n)
            return;
        _pos = new Vector3[n];
        _active = new bool[n];
        _keys = new float[n];
        _idx = new int[n];
    }

    /// <summary>A new run: no stimuli, no lure, counters back to zero (the portals and fields stay).</summary>
    public void Reset()
    {
        _stimCount = 0;
        LureUntil = -1;
        Array.Fill(_lastNoise, -1e9);
        Turned = Scattered = Flanked = Retargeted = Lured = Regrouped = 0;
        foreach (var g in Groups)
        {
            g.Mode = HordeGroupMode.Advance;
            g.CheckDist = float.MaxValue;
            g.CheckAt = 0;
            g.Regroups = g.SplitOut = g.Joined = 0;
        }
        Array.Clear(Targets);
        TargetCount = 0;
    }

    public void ClearTargets()
    {
        for (var i = 0; i < MaxTargets; i++)
        {
            Targets[i].WasLive = Targets[i].Live;
            Targets[i].Live = false;
        }
        TargetCount = 0;
    }

    /// <summary>A target for this tick (players standing, in id order). Keeps its nav node while it stays near it.</summary>
    public void AddTarget(byte id, Vector3 pos, int area)
    {
        // Its slot from last tick, else a slot nobody had last tick, else any free one.
        var slot = -1;
        for (var i = 0; i < MaxTargets && slot < 0; i++)
            if (Targets[i].WasLive && !Targets[i].Live && Targets[i].Id == id)
                slot = i;
        for (var i = 0; i < MaxTargets && slot < 0; i++)
            if (!Targets[i].WasLive && !Targets[i].Live)
                slot = i;
        for (var i = 0; i < MaxTargets && slot < 0; i++)
            if (!Targets[i].Live)
                slot = i;
        if (slot < 0)
            return;
        TargetCount++;
        ref var t = ref Targets[slot];
        var same = t.WasLive && t.Id == id;
        if (!same && Groups.Length > TargetGroup(slot))
        {
            var g = Groups[TargetGroup(slot)]; // someone new in this slot: its group starts afresh
            g.Mode = HordeGroupMode.Advance;
            g.CheckDist = float.MaxValue;
        }
        t.Live = true;
        var keep = same && t.Node >= 0 && t.Node < _nav.Nodes.Length;
        t.Id = id;
        t.Pos = pos;
        t.Area = area;
        if (!keep || Flat(_targetNodePos[slot] - pos) > 1.5f)
        {
            t.Node = -1;
            _targetNodePos[slot] = pos;
        }
    }

    /// <summary>The target slot for player <paramref name="id"/>, or -1.</summary>
    public int SlotOf(byte id)
    {
        for (var i = 0; i < MaxTargets; i++)
            if (Targets[i].Live && Targets[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>
    /// Something the horde should react to (handled at the start of the next tick, in order). Gunfire and power-up
    /// noises from the same source come at most five times a second.
    /// </summary>
    public void Stimulus(HordeStimulusKind kind, Vector3 pos, float radius, byte source, double now, double seconds = 0)
    {
        if (kind is HordeStimulusKind.Gunfire or HordeStimulusKind.PowerUp)
        {
            if (now - _lastNoise[source] < 0.2)
                return;
            _lastNoise[source] = now;
        }
        if (_stimCount == MaxStimuli)
            return; // a flood in one tick: the rest are dropped
        _stimuli[_stimCount++] = new HordeStimulus { Kind = kind, Pos = pos, Radius = radius, Source = source, At = now, Until = now + seconds };
    }

    /// <summary>Stimuli waiting for the next tick (self-test).</summary>
    public int PendingStimuli => _stimCount;

    // ---- The tick ---------------------------------------------------------------------------------------

    /// <summary>
    /// Once per server tick, before the members think: the grid, the targets' nodes, each member's level of detail and
    /// group, group counts, the portal queues, the stimuli and the groups' decisions.
    /// </summary>
    public void BeginTick(List<T> agents, NavGraph nav, long tick, double now)
    {
        _agents = agents;
        _nav = nav;
        _tick = tick;
        _now = now;
        var tu = Tuning;
        Flow.Capacity = tu.FlowFields;
        Flow.BuildsPerTick = tu.FlowBuildsPerTick;
        Flow.RefreshTicks = (long)MathF.Round(tu.FlowRefreshSeconds * World.TickRate);
        Flow.BeginTick(nav, tick);

        var n = agents.Count;
        if (_pos.Length < n)
        {
            var cap = Math.Max(n, _pos.Length * 2);
            _pos = new Vector3[cap];
            _active = new bool[cap];
            _keys = new float[cap];
            _idx = new int[cap];
        }
        for (var i = 0; i < n; i++)
        {
            var a = agents[i];
            _pos[i] = a.Position;
            _active[i] = a.HordeActive;
            a.Member.Index = i;
        }
        Grid.Build(_pos.AsSpan(0, n), _active.AsSpan(0, n));

        for (var t = 0; t < MaxTargets; t++)
        {
            ref var tg = ref Targets[t];
            if (!tg.Live)
                continue;
            if (tg.Node < 0 && nav.Nodes.Length > 0)
                tg.Node = nav.Nearest(tg.Pos, needSight: false);
            tg.Pressure = tg.Engaged = 0;
        }
        foreach (var g in Groups)
        {
            g.Count = 0;
            g.SumPos = default;
            g.SumDist = 0;
        }
        foreach (var p in Portals)
        {
            p.Queue = 0;
            if (p.Node < 0 && nav.Nodes.Length > 0)
                p.Node = nav.Nearest(p.Front, needSight: false);
        }
        if (LureUntil >= 0 && now >= LureUntil)
            LureUntil = -1; // the lure is over: its members go back to the players (below)

        // Members: level of detail, group, counts.
        for (var i = 0; i < n; i++)
        {
            if (!_active[i])
                continue;
            var a = agents[i];
            var m = a.Member;
            var pos = _pos[i];
            var nearest = float.MaxValue;
            var nearSlot = -1;
            for (var t = 0; t < MaxTargets; t++)
            {
                if (!Targets[t].Live)
                    continue;
                var d = Flat(Targets[t].Pos - pos);
                if (d < nearest)
                {
                    nearest = d;
                    nearSlot = t;
                }
            }
            m.NearestDist = nearest;
            m.Tier = (byte)(nearest < tu.NearRange ? 0 : nearest < tu.MidRange ? 1 : nearest < tu.FarRange ? 2 : 3);
            var every = Math.Max(1, tu.ThinkEvery[Math.Min(m.Tier, tu.ThinkEvery.Length - 1)]);
            m.ThinkNow = (tick + Hash(a.HordeId)) % every == 0;

            var portal = a.HordePortal;
            if (portal >= 0 && portal < Portals.Length)
            {
                m.Group = portal;
                m.TargetSlot = nearSlot;
            }
            else if (m.Group == LureGroup && LureUntil >= 0)
                m.TargetSlot = nearSlot; // (swipes at whoever is near the lure)
            else
            {
                var valid = m.Group >= Portals.Length && m.Group != LureGroup && IsLive(m.Group - Portals.Length);
                if (!valid)
                {
                    if (m.Group >= Portals.Length && m.Group != LureGroup && TargetCount > 0)
                        Retargeted++; // its target went down or left: merged into another group
                    Join(m, BestTarget(pos, a.HordeId, -1), now);
                }
                else if (m.ThinkNow && now - m.GroupSince > 0.75 && (tick + Hash(a.HordeId)) % 30 == 0)
                {
                    // Spread over the players: switch when another one is clearly better (hysteresis).
                    var cur = m.Group - Portals.Length;
                    var best = BestTarget(pos, a.HordeId, cur);
                    if (best >= 0 && best != cur && Score(pos, best, cur) < Score(pos, cur, cur) * 0.8f - 1f)
                        Join(m, best, now);
                }
                m.TargetSlot = m.Group >= Portals.Length && m.Group != LureGroup && IsLive(m.Group - Portals.Length) ? m.Group - Portals.Length : -1;
            }
            if (m.Group < 0 || m.Group >= Groups.Length)
                continue;
            var g = Groups[m.Group];
            g.Count++;
            g.SumPos += pos;
            var goal = GoalOf(m.Group);
            g.SumDist += Flat(goal - pos);
            if (m.TargetSlot >= 0 && m.Group >= Portals.Length && m.Group != LureGroup)
            {
                ref var tg = ref Targets[m.TargetSlot];
                tg.Pressure++;
                if (Flat(tg.Pos - pos) < 2.5f)
                    tg.Engaged++;
            }
        }
        foreach (var g in Groups)
            if (g.Count > 0)
            {
                g.Centre = g.SumPos / g.Count;
                g.MeanDist = g.SumDist / g.Count;
            }

        Queues(agents, n);
        Stimuli(agents, now);

        // The groups whose turn it is decide (each about every GroupThinkSeconds).
        var period = Math.Max(1, (int)MathF.Round(tu.GroupThinkSeconds * World.TickRate));
        for (var gi = 0; gi < Groups.Length; gi++)
        {
            if ((tick + gi * 7) % period != 0)
                continue;
            var g = Groups[gi];
            if (gi < Portals.Length)
                SplitQueue(agents, gi, now);
            else
                CheckBlocked(agents, g, now);
        }
    }

    /// <summary>Where group <paramref name="gi"/> is going: its portal's front, its target, or the lure.</summary>
    public Vector3 GoalOf(int gi)
    {
        if (gi < Portals.Length)
            return Portals[gi].Front;
        if (gi == LureGroup)
            return LurePos;
        var slot = gi - Portals.Length;
        return IsLive(slot) ? Targets[slot].Pos : default;
    }

    private void Join(HordeMember m, int targetSlot, double now)
    {
        var to = targetSlot >= 0 ? TargetGroup(targetSlot) : -1;
        if (to == m.Group)
            return;
        if (to >= 0)
            Groups[to].Joined++;
        m.Group = to;
        m.GroupSince = now;
        m.TargetSlot = targetSlot;
    }

    /// <summary>The best target for a member at <paramref name="pos"/>: near, same zone, not swamped already.</summary>
    private int BestTarget(Vector3 pos, ushort id, int current)
    {
        var best = -1;
        var bestScore = float.MaxValue;
        for (var t = 0; t < MaxTargets; t++)
        {
            if (!Targets[t].Live)
                continue;
            var s = Score(pos, t, current);
            if (s < bestScore)
            {
                bestScore = s;
                best = t;
            }
        }
        return best;
    }

    private float Score(Vector3 pos, int slot, int current)
    {
        ref var t = ref Targets[slot];
        var d = Flat(t.Pos - pos);
        if (_zoneOf != null && _zoneOf(pos) != t.Area)
            d += Tuning.AreaPenalty;
        var others = t.Pressure - (slot == current ? 1 : 0);
        return d + Tuning.PressureWeight * Math.Max(0, others - Tuning.PressureFree);
    }

    private Func<Vector3, int>? _zoneOf;

    /// <summary>How the game tells which zone (Zombies area) a point is in, for the target scores (set once).</summary>
    public void SetZones(Func<Vector3, int> zoneOf) => _zoneOf = zoneOf;

    // ---- Portal queues -----------------------------------------------------------------------------------

    private void Queues(List<T> agents, int n)
    {
        if (Portals.Length == 0)
            return;
        var tearers = Math.Max(1, Tuning.Tearers);
        for (var p = 0; p < Portals.Length; p++)
        {
            var portal = Portals[p];
            var k = 0;
            for (var i = 0; i < n; i++)
            {
                if (!_active[i] || agents[i].HordePortal != p)
                    continue;
                var a = agents[i];
                _keys[k] = (a.HordeAtPortal ? -1000f : 0f) + Flat(_pos[i] - portal.Front) + a.HordeId * 1e-5f;
                _idx[k] = i;
                k++;
            }
            portal.Queue = k;
            if (k == 0)
                continue;
            if (k > 1)
                Array.Sort(_keys, _idx, 0, k);
            for (var r = 0; r < k; r++)
            {
                var m = agents[_idx[r]].Member;
                m.QueueRank = r;
                m.QueueSpot = r < tearers || portal.Spots.Length == 0 ? portal.Front : portal.Spots[Math.Min(r - tearers, portal.Spots.Length - 1)];
            }
        }
    }

    /// <summary>A portal group with a long queue: its back members go round to the best other open portal within reach.</summary>
    private void SplitQueue(List<T> agents, int p, double now)
    {
        var portal = Portals[p];
        var tu = Tuning;
        if (portal.Queue <= tu.QueueSplitAt || Portals.Length < 2)
            return;
        var moved = 0;
        for (var i = 0; i < agents.Count && moved < 4; i++)
        {
            var a = agents[i];
            var m = a.Member;
            if (!_active[i] || a.HordePortal != p || a.HordeAtPortal || m.QueueRank < tu.QueueSplitAt)
                continue;
            if (m.Node < 0 && _nav.Nodes.Length > 0)
                m.Node = _nav.Nearest(_pos[i], needSight: false);
            var best = -1;
            var bestCost = tu.FlankRange + portal.Queue * tu.QueueCost;
            for (var q = 0; q < Portals.Length; q++)
            {
                var other = Portals[q];
                if (q == p || !other.Open || other.Node < 0 || other.Queue + 2 >= portal.Queue - 1)
                    continue;
                if (Flat(other.Front - _pos[i]) > tu.FlankRange)
                    continue;
                var f = Flow.Get(other.Node);
                if (f == null)
                    continue;
                var c = HordeFlowFields.CostFrom(f, m.Node);
                if (c > tu.FlankRange)
                    continue;
                c += other.Queue * tu.QueueCost;
                if (c < bestCost)
                {
                    bestCost = c;
                    best = q;
                }
            }
            if (best < 0)
                continue;
            if (_movePortal == null || !_movePortal(a, best))
                continue;
            Portals[best].Queue++;
            portal.Queue--;
            Groups[p].SplitOut++;
            Groups[best].Joined++;
            m.Group = best;
            m.GroupSince = now;
            m.Node = -1;
            Flanked++;
            moved++;
        }
    }

    private Func<T, int, bool>? _movePortal;

    /// <summary>How the game sends an agent to another portal (returns false if it can't go; set once).</summary>
    public void SetPortalMover(Func<T, int, bool> move) => _movePortal = move;

    /// <summary>A target group that got nowhere for a while (and isn't fighting) regroups: close up, hold, go again.</summary>
    private void CheckBlocked(List<T> agents, HordeGroup g, double now)
    {
        var tu = Tuning;
        var slot = g.Index - Portals.Length;
        if (g.Mode == HordeGroupMode.Regroup)
        {
            if (now >= g.ModeUntil)
            {
                g.Mode = HordeGroupMode.Advance;
                RestartCheck(g, slot, now);
            }
            return;
        }
        if (g.Index == LureGroup || g.Count < tu.StuckMinMembers || !IsLive(slot) || Targets[slot].Engaged > 0)
        {
            RestartCheck(g, slot, now);
            return;
        }
        if (now - g.CheckAt < tu.StuckSeconds)
            return;
        // Blocked: no closer, and not because the target walked off (a player kiting the horde round the map is chased,
        // not a blocked group: regrouping then turned the whole horde to its own centre every few seconds, 2026-10-27).
        if (g.CheckDist - g.MeanDist < tu.StuckProgress && g.MeanDist > tu.SurroundRange && Flat(Targets[slot].Pos - g.CheckTarget) < StuckTargetMove)
            StartRegroup(agents, g, now);
        RestartCheck(g, slot, now);
    }

    /// <summary>How far (m) a target may move during a blocked check and the group still count as blocked.</summary>
    public const float StuckTargetMove = 1.5f;

    private void RestartCheck(HordeGroup g, int slot, double now)
    {
        g.CheckDist = g.MeanDist;
        g.CheckAt = now;
        if (IsLive(slot))
            g.CheckTarget = Targets[slot].Pos;
    }

    private void StartRegroup(List<T> agents, HordeGroup g, double now)
    {
        g.Mode = HordeGroupMode.Regroup;
        g.ModeUntil = now + Tuning.RegroupSeconds;
        g.Regroups++;
        Regrouped++;
        for (var i = 0; i < agents.Count; i++)
        {
            if (!_active[i])
                continue;
            var m = agents[i].Member;
            if (m.Group != g.Index)
                continue;
            m.Node = -1; // plan the way again afterwards
            if (Flat(_pos[i] - g.Centre) < 3f)
                m.HoldUntil = now + Tuning.RegroupSeconds * 0.5;
        }
    }

    // ---- Stimuli ------------------------------------------------------------------------------------------

    private void Stimuli(List<T> agents, double now)
    {
        var tu = Tuning;
        for (var s = 0; s < _stimCount; s++)
        {
            var st = _stimuli[s];
            switch (st.Kind)
            {
                case HordeStimulusKind.Gunfire:
                case HordeStimulusKind.PowerUp:
                case HordeStimulusKind.Hurt:
                {
                    var slot = SlotOf(st.Source);
                    if (slot < 0)
                        break;
                    var r = st.Radius > 0 ? st.Radius : st.Kind == HordeStimulusKind.Gunfire ? tu.HearGunfire : st.Kind == HordeStimulusKind.PowerUp ? tu.HearPowerUp : tu.HurtAlert;
                    var k = Grid.Query(st.Pos, r, _scratch);
                    for (var j = 0; j < k; j++)
                    {
                        var a = agents[_scratch[j]];
                        var m = a.Member;
                        if (a.HordePortal >= 0 || m.Group == LureGroup || m.TargetSlot == slot)
                            continue;
                        if (m.TargetSlot >= 0 && Flat(Targets[m.TargetSlot].Pos - a.Position) < tu.EngagedRange)
                            continue; // already fighting someone
                        Join(m, slot, now);
                        Turned++;
                    }
                    break;
                }
                case HordeStimulusKind.Explosion:
                {
                    var slot = SlotOf(st.Source);
                    var k = Grid.Query(st.Pos, st.Radius * tu.ScatterScale, _scratch);
                    for (var j = 0; j < k; j++)
                    {
                        var a = agents[_scratch[j]];
                        var m = a.Member;
                        m.FleeUntil = now + tu.ExplosionStagger + tu.ScatterSeconds; // (staggered first, then away)
                        m.FleeFrom = st.Pos;
                        m.Node = -1;
                        Scattered++;
                        if (a.HordePortal < 0 && slot >= 0 && m.Group != LureGroup)
                            Join(m, slot, now);
                        if (m.Group >= Portals.Length && m.Group < Groups.Length && m.Group != LureGroup)
                        {
                            var g = Groups[m.Group];
                            if (g.Mode != HordeGroupMode.Regroup || g.ModeUntil < now + tu.ExplosionStagger + tu.ScatterSeconds + tu.RegroupSeconds)
                            {
                                g.Mode = HordeGroupMode.Regroup; // after the scatter: close up again
                                g.ModeUntil = now + tu.ExplosionStagger + tu.ScatterSeconds + tu.RegroupSeconds;
                                g.Regroups++;
                                Regrouped++;
                            }
                        }
                    }
                    break;
                }
                case HordeStimulusKind.Downed:
                {
                    // Everyone after them picks again now (the target list no longer has them).
                    for (var i = 0; i < agents.Count; i++)
                    {
                        if (!_active[i])
                            continue;
                        var a = agents[i];
                        var m = a.Member;
                        if (a.HordePortal >= 0 || m.Group == LureGroup)
                            continue;
                        if (!IsLive(m.TargetSlot) || Targets[m.TargetSlot].Id == st.Source)
                        {
                            Join(m, BestTarget(a.Position, a.HordeId, -1), now);
                            m.Node = -1;
                            Retargeted++;
                        }
                    }
                    break;
                }
                case HordeStimulusKind.Lure:
                {
                    LurePos = st.Pos;
                    LureUntil = st.Until;
                    var k = Grid.Query(st.Pos, st.Radius > 0 ? st.Radius : tu.LureRange, _scratch);
                    for (var j = 0; j < k; j++)
                    {
                        var a = agents[_scratch[j]];
                        if (a.HordePortal >= 0)
                            continue;
                        var m = a.Member;
                        m.Group = LureGroup;
                        m.GroupSince = now;
                        m.Node = -1;
                        Groups[LureGroup].Joined++;
                        Lured++;
                    }
                    break;
                }
            }
        }
        _stimCount = 0;
    }

    // ---- Steering (asked by each member) ---------------------------------------------------------------------

    /// <summary>
    /// The goal for an inside member after its target at <paramref name="target"/>: between <see cref="HordeTuning.SurroundMin"/>
    /// and <see cref="HordeTuning.SurroundRange"/> a point on a ring round the target at the member's own angle, so a group
    /// closes in from all sides; nearer, the target itself.
    /// </summary>
    public Vector3 SurroundGoal(T a, Vector3 target)
    {
        var tu = Tuning;
        var m = a.Member;
        var to = target - a.Position;
        var d = Flat(to);
        if (d < tu.SurroundMin || d > tu.SurroundRange || tu.SurroundRadius <= 0)
            return target;
        // The ring shrinks as it closes in, so the last metres are straight at the target.
        var r = tu.SurroundRadius * Math.Clamp((d - tu.SurroundMin) / MathF.Max(0.1f, tu.SurroundRange - tu.SurroundMin) * 2f, 0f, 1f);
        var ang = m.SurroundAngle;
        return target + new Vector3(MathF.Sin(ang) * r, 0, MathF.Cos(ang) * r);
    }

    /// <summary>
    /// Where a member should walk this tick to reach <paramref name="goal"/>: straight at it when it is close and its whole
    /// body has a clear way there (<see cref="WalkClear"/>; checked on think ticks), else the next node on the shared flow
    /// field to <paramref name="goalNode"/> (with a corner cut when the node after is clear the same way; checked on think
    /// ticks). <paramref name="jump"/>: the next node is a step up the member has to jump, or the edge to it climbs over a
    /// ledge and the member is at it. The stuck watch's recovery (<see cref="HordeMember.NoStraightUntil"/>,
    /// <see cref="HordeMember.ToNodeUntil"/>) is kept here.
    /// </summary>
    public Vector3 Steer(T a, Vector3 goal, int goalNode, out bool jump)
    {
        var p = SteerRoute(a, goal, goalNode, out jump);
        return Slide(a, p);
    }

    /// <summary>
    /// Slides along what the body is pressed against (board item dev-zombies-stuck-railings-zr01): on some think ticks
    /// three short knee rays (the middle and the body's two sides) look along the way to <paramref name="p"/>; a wall,
    /// railing or post within reach turns the way along its face (kept a few ticks), so a member walks round a pew's end or
    /// along a railing instead of pushing into it. No change when nothing is in the way.
    /// </summary>
    private Vector3 Slide(T a, Vector3 p)
    {
        var m = a.Member;
        var pos = a.Position;
        var d = p - pos;
        d.Y = 0;
        var len = d.Length();
        if (len < 0.05f || GravityFrames.Curved)
            return p;
        var dir = d / len;
        if (m.ThinkNow && (m.Tier > 0 || ((_tick + a.HordeId) & 1) == 0))
        {
            m.SlideNormal = Vector3.Zero;
            var side = new Vector3(-dir.Z, 0, dir.X) * WalkClearSide;
            var knee = pos + new Vector3(0, 0.6f, 0);
            var best = SlideReach;
            for (var k = -1; k <= 1; k++)
                if (World.Query.Raycast(knee + side * k, dir, SlideReach, QueryFilter.Navigation, out var hit) && hit.Distance < best)
                {
                    var n = new Vector3(hit.Normal.X, 0, hit.Normal.Z);
                    var nl = n.Length();
                    if (nl < 0.3f)
                        continue; // (a floor or a ramp: not a wall)
                    best = hit.Distance;
                    m.SlideNormal = n / nl;
                }
            m.SlideUntil = _tick + 12;
        }
        if (_tick > m.SlideUntil || m.SlideNormal == Vector3.Zero)
            return p;
        var into = Vector3.Dot(dir, m.SlideNormal);
        if (into >= -0.05f)
            return p; // (heading away from it, or along it)
        var along = dir - m.SlideNormal * into;
        var al = along.Length();
        if (al < 0.05f)
            return p; // (square on: the stuck watch's side-step decides which way)
        return pos + along / al * MathF.Max(len, 1f) + new Vector3(0, p.Y - pos.Y, 0);
    }

    /// <summary>How far ahead <see cref="Slide"/> looks for something to slide along (a body's radius and a stride).</summary>
    public const float SlideReach = 0.75f;

    /// <summary>The route part of <see cref="Steer"/>: the straight goal or the field's next node (no sliding yet).</summary>
    private Vector3 SteerRoute(T a, Vector3 goal, int goalNode, out bool jump)
    {
        jump = false;
        var m = a.Member;
        var pos = a.Position;
        var nav = _nav;
        m.Climbing = false;
        if (m.ThinkNow)
        {
            var to = goal - pos;
            m.Straight = _now >= m.NoStraightUntil && Flat(to) < 14f && MathF.Abs(to.Y) < 1.2f && WalkClear(pos, goal);
        }
        if (m.Straight || goalNode < 0 || nav.Nodes.Length == 0)
            return goal;
        var field = Flow.Get(goalNode);
        if (field == null)
            return goal;
        m.FlowGoal = goalNode;
        if (m.Node < 0 || m.Node >= nav.Nodes.Length || (m.ThinkNow && OffRoute(nav, m.Node, field.Next[m.Node], pos)))
            m.Node = nav.Nearest(pos, needSight: true); // (in plain view: never the floor below a stair or the far side of a railing)
        if (m.Node < 0)
            return goal;
        if (_now < m.ToNodeUntil)
        {
            // Recovering: back to its route node first, then on along the field.
            var back = nav.Nodes[m.Node];
            if (Flat(back - pos) > 0.5f)
            {
                m.Climbing = MathF.Abs(back.Y - pos.Y) > 0.3f;
                jump = back.Y - pos.Y > World.StepHeight && Flat(back - pos) < 1.5f;
                return back;
            }
            m.ToNodeUntil = 0;
        }
        var next = field.Next[m.Node];
        for (var hop = 0; hop < 4 && next >= 0 && Reached(nav.Nodes[m.Node], nav.Nodes[next], pos); hop++)
        {
            m.Node = next;
            next = field.Next[next];
        }
        if (next < 0)
            return goal; // at the goal's node (or no way from here: straight, and the stuck watch)
        var edge = EdgeBetween(nav, m.Node, next);
        if (edge >= 0 && (nav.EdgeFlags[edge] & NavGraph.FlagLastResort) != 0 && m.ThinkNow)
        {
            // The field only has a last resort from here (through a Zombies door the graph was built shut, say): straight
            // at the goal after all when the plain sight test allows it, as before the body-wide test.
            var to = goal - pos;
            if (Flat(to) < 14f && MathF.Abs(to.Y) < 1.2f && NavGraph.Clear(pos, goal))
            {
                m.Straight = true;
                return goal;
            }
        }
        var climb = edge >= 0 && (nav.EdgeFlags[edge] & (NavGraph.FlagMantle | NavGraph.FlagDrop)) != 0;
        if (!climb && m.ThinkNow && ((_tick + a.HordeId) & 1) == 0)
        {
            var after = field.Next[next];
            var e2 = after >= 0 ? EdgeBetween(nav, next, after) : -1;
            if (after >= 0 && MathF.Abs(nav.Nodes[after].Y - pos.Y) < 0.6f && (e2 < 0 || (nav.EdgeFlags[e2] & (NavGraph.FlagMantle | NavGraph.FlagDrop)) == 0) && WalkClear(pos, nav.Nodes[after]))
            {
                m.Node = next;
                next = after;
                edge = e2;
            }
        }
        var p = nav.Nodes[next];
        if (!climb && m.ThinkNow && (m.Tier > 0 || ((_tick + a.HordeId) & 3) == 1) && MathF.Abs(p.Y - pos.Y) < 0.3f && Flat(nav.Nodes[m.Node] - pos) > 0.6f && !KneeClear(pos, p))
        {
            // Off the edge's line with a corner (or a railing's post) between it and the next node: back onto the line
            // at its node first instead of grinding along the corner.
            m.ToNodeUntil = _now + 0.5;
            var back = nav.Nodes[m.Node];
            m.Climbing = MathF.Abs(back.Y - pos.Y) > 0.3f;
            jump = back.Y - pos.Y > World.StepHeight && Flat(back - pos) < 1.5f;
            return back;
        }
        m.Climbing = MathF.Abs(p.Y - pos.Y) > 0.3f;
        jump = p.Y - pos.Y > World.StepHeight && Flat(p - pos) < 1.5f; // (a ledge right ahead, not the top of a long flight)
        if (edge >= 0 && (nav.EdgeFlags[edge] & NavGraph.FlagMantle) != 0 && edge < nav.EdgeLedge.Length)
        {
            // A climb over a ledge (the field's vault penalty makes these rare): jump at the ledge, as the bots do.
            var ledge = nav.EdgeLedge[edge];
            if (Flat(ledge - pos) < 1.4f && ledge.Y > pos.Y + World.StepHeight)
                jump = true;
        }
        return p;
    }

    /// <summary>
    /// Is the member too far off its route to go on from node <paramref name="node"/>: more than 2.2 m across the ground
    /// or 1.4 m up or down from the edge to <paramref name="next"/> (the line between them, so a member halfway up a long
    /// stair flight keeps its route instead of being given the landing above, whose next node is across the stairwell)?
    /// </summary>
    private static bool OffRoute(NavGraph nav, int node, int next, Vector3 pos)
    {
        var a = nav.Nodes[node];
        if (next < 0 || next >= nav.Nodes.Length)
            return Flat(a - pos) > 2.2f || MathF.Abs(a.Y - pos.Y) > 1.4f;
        var b = nav.Nodes[next];
        float dx = b.X - a.X, dz = b.Z - a.Z;
        var len2 = dx * dx + dz * dz;
        var t = len2 < 1e-4f ? 0f : Math.Clamp(((pos.X - a.X) * dx + (pos.Z - a.Z) * dz) / len2, 0f, 1f);
        var on = Vector3.Lerp(a, b, t);
        return Flat(on - pos) > 2.2f || MathF.Abs(on.Y - pos.Y) > 1.4f;
    }

    /// <summary>
    /// Has a member at <paramref name="pos"/> reached the node <paramref name="b"/> it walks to from <paramref name="a"/>:
    /// within 0.65 m of it, or past it along the edge and within 1.6 m (a member pushed or cutting a corner never walks
    /// back to a node it has gone by)? On its floor either way.
    /// </summary>
    private static bool Reached(Vector3 a, Vector3 b, Vector3 pos)
    {
        if (MathF.Abs(b.Y - pos.Y) >= 1.2f)
            return false;
        var d = Flat(b - pos);
        if (d < 0.65f)
            return true;
        if (d > 1.6f)
            return false;
        float ex = b.X - a.X, ez = b.Z - a.Z;
        return ex * (pos.X - b.X) + ez * (pos.Z - b.Z) > 0;
    }

    /// <summary>The edge from node <paramref name="from"/> to node <paramref name="to"/>, or -1.</summary>
    public static int EdgeBetween(NavGraph nav, int from, int to)
    {
        if (from < 0 || from + 1 >= nav.EdgeStart.Length)
            return -1;
        for (var e = nav.EdgeStart[from]; e < nav.EdgeStart[from + 1]; e++)
            if (nav.EdgeTo[e] == to)
                return e;
        return -1;
    }

    /// <summary>One ray at knee height between two feet positions (the cheap corner test while following the field).</summary>
    private static bool KneeClear(Vector3 a, Vector3 b)
    {
        var from = a + new Vector3(0, 0.6f, 0);
        var d = b + new Vector3(0, 0.6f, 0) - from;
        var len = d.Length();
        return len < 0.01f || World.Query.Raycast(from, d / len, len, QueryFilter.Navigation) >= len - 0.05f;
    }

    /// <summary>Half the width between the side rays of <see cref="WalkClear"/>: just under a body's radius (0.4 m), so a corner the body would clip stops the line.</summary>
    public const float WalkClearSide = 0.38f;

    /// <summary>
    /// Can a body walk straight from feet <paramref name="a"/> to feet <paramref name="b"/>? <see cref="NavGraph.Clear"/>
    /// (knee and chest), a ray at railing height (a rail band between the two), and two knee rays a body's width apart (the
    /// end of a pew, a pillar's corner or a baluster the middle ray slips past). Seven rays at most, most of the time two
    /// (a blocked way stops at the first), then the floor under the line (a ray down every couple of metres).
    /// </summary>
    public static bool WalkClear(Vector3 a, Vector3 b)
    {
        if (!NavGraph.Clear(a, b))
            return false;
        if (GravityFrames.Curved)
            return true;
        var d = b - a;
        var flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        if (flat < 0.05f)
            return true;
        if (!Ray(a + new Vector3(0, 0.95f, 0), b + new Vector3(0, 0.95f, 0)))
            return false;
        var side = new Vector3(-d.Z / flat * WalkClearSide, 0.6f, d.X / flat * WalkClearSide);
        var other = new Vector3(-side.X, 0.6f, -side.Z);
        if (!Ray(a + side, b + side) || !Ray(a + other, b + other))
            return false;
        // Floor all the way: no line across a stairwell, a gallery's open side or a gap between floors (a ray down at
        // every couple of metres finds the floor near the line).
        var samples = Math.Min(6, (int)(flat / 2f));
        for (var i = 1; i <= samples; i++)
        {
            var p = Vector3.Lerp(a, b, i / (samples + 1f));
            if (World.Query.Raycast(p + new Vector3(0, 0.5f, 0), -Vector3.UnitY, 1.2f, QueryFilter.Navigation) > 1.15f)
                return false;
        }
        return true;

        static bool Ray(Vector3 from, Vector3 to)
        {
            var dd = to - from;
            var len = dd.Length();
            return len < 0.01f || World.Query.Raycast(from, dd / len, len, QueryFilter.Navigation) >= len - 0.05f;
        }
    }

    /// <summary>The walking yaw turned away from the members close by (from the grid; the old all-pairs maths).</summary>
    public float Separate(T a, float yaw)
    {
        var tu = Tuning;
        // Narrower on stairs and ramps: a crowd pushing sideways there shoves its members into the railings.
        var push = Grid.Push(a.Member.Index, a.Position, tu.SeparationRadius, a.Member.Climbing ? tu.SeparationStrength * tu.StairSeparation : tu.SeparationStrength);
        if (push.LengthSquared() < 1e-6f)
            return yaw;
        var w = World.FlatForward(yaw) + push;
        return MathF.Atan2(w.X, -w.Z);
    }

    /// <summary>Is the member scattering from a blast now? (<paramref name="yaw"/>: the way away from it.)</summary>
    public bool Fleeing(T a, out float yaw)
    {
        var m = a.Member;
        yaw = 0;
        if (m.FleeUntil <= _now)
            return false;
        var away = a.Position - m.FleeFrom;
        away.Y = 0;
        if (away.LengthSquared() < 1e-4f)
            away = new Vector3(MathF.Sin(m.SurroundAngle), 0, MathF.Cos(m.SurroundAngle));
        yaw = MathF.Atan2(away.X, -away.Z);
        return true;
    }

    /// <summary>
    /// Is the member's group regrouping, and is it holding still (<paramref name="hold"/>) or closing up on the centre
    /// (<paramref name="centre"/>)? Only the stragglers close up: a member at least as near the group's goal as the centre
    /// holds, so closing up never walks anyone away from the target (a group spread round its target has its centre
    /// somewhere else entirely, such as the middle of the map: the whole horde turned there, 2026-10-27).
    /// </summary>
    public bool Regrouping(T a, out bool hold, out Vector3 centre)
    {
        var m = a.Member;
        hold = false;
        centre = default;
        if (m.Group < 0 || m.Group >= Groups.Length || m.Group == LureGroup)
            return false;
        var g = Groups[m.Group];
        if (g.Mode != HordeGroupMode.Regroup || _now >= g.ModeUntil || g.Count == 0)
            return false;
        var slot = m.Group - Portals.Length;
        if (m.Group >= Portals.Length && !IsLive(slot))
            return false; // (its target is gone: it picks another next tick)
        centre = g.Centre;
        var goal = GoalOf(m.Group);
        hold = m.HoldUntil > _now || Flat(a.Position - g.Centre) < 2f || Flat(goal - a.Position) <= Flat(goal - g.Centre) + 0.5f;
        return true;
    }

    /// <summary>A member joined the horde (spawned): its surround angle from its id.</summary>
    public static void Joined(T a)
    {
        var m = a.Member;
        m.SurroundAngle = Hash(a.HordeId) / 64f * MathF.Tau;
    }

    // ---- Helpers ------------------------------------------------------------------------------------------

    /// <summary>A small stable hash of an id (0-63): think staggering and surround angles.</summary>
    public static int Hash(ushort id)
    {
        var h = (uint)id * 2654435761u;
        return (int)((h >> 16) & 63);
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
