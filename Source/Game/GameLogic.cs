using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The live state of a map's logic (MapLogic.cs) in one game: on the server the real thing, on a client the mirror the
/// server's <see cref="Msg.Logic"/> packets keep (what it needs to draw and to hint: doors, lamps, levers, plates, buttons
/// cooling down, disabled entities). Sized once per map; nothing is allocated while it runs.
/// </summary>
public sealed class LogicState
{
    public const byte FlagOn = 1, FlagDisabled = 2, FlagSpent = 4, FlagCooling = 8;

    public readonly Map Map;
    public readonly MapLogic Def;

    /// <summary>Per entity: <see cref="FlagOn"/> (on, open, pressed, running, latched output), disabled, spent (once), cooling down.</summary>
    public readonly byte[] Flags;

    /// <summary>Per entity: the tick it can be used again (buttons, levers), or a door's retry; counters' count; gates' input bits; timers' fires; zones' head count.</summary>
    public readonly uint[] Ready;
    public readonly int[] Count;

    /// <summary>Per entity: bumps when a timer stops or restarts, so ticks it had queued are ignored.</summary>
    public readonly int[] Gen;

    /// <summary>Per wire: a once-only wire that has fired this match.</summary>
    public readonly bool[] WireUsed;

    /// <summary>Per zone (index into <see cref="MapLogic.Zones"/>) and player id: inside it at the last sample.</summary>
    public readonly bool[] Inside;

    /// <summary>Per challenge (index into <see cref="MapLogic.Challenges"/>) and player id: in it (standing when it started, and in its zone).</summary>
    public readonly bool[] Members;

    /// <summary>Signals and timer ticks waiting for their tick: a binary heap ordered by (due tick, sequence).</summary>
    public readonly LogicItem[] Heap = new LogicItem[MaxQueued];
    public int HeapCount;
    public uint Seq;

    /// <summary>Signals delivered, events sent and signals dropped (the queue was full) since the match started.</summary>
    public int Delivered, Sent, Dropped;

    /// <summary>The match these were reset for (server), and whether "match.started" / "ended" went for it.</summary>
    public int Match = -1;
    public bool StartedSent, EndedSent;

    public const int MaxQueued = 1024, MaxPerTick = 256;

    public LogicState(Map map)
    {
        Map = map;
        Def = map.Logic!;
        var n = Def.Entities.Count;
        Flags = new byte[n];
        Ready = new uint[n];
        Count = new int[n];
        Gen = new int[n];
        WireUsed = new bool[Def.Wires.Length];
        Inside = new bool[Def.Zones.Length * 256];
        Members = new bool[Def.Challenges.Length * 256];
    }

    public bool On(int e) => (Flags[e] & FlagOn) != 0;

    public bool Enabled(int e) => (Flags[e] & FlagDisabled) == 0;
}

/// <summary>What a queued item does: deliver a signal to an input, fire a timer, retry shutting a door, say a mover arrived.</summary>
public enum LogicItemKind : byte
{
    Deliver,
    TimerFire,
    DoorRetry,
    MoverArrived,
    CoolDown,
    ChallengeEnd,
}

public struct LogicItem
{
    public uint Due, Seq;
    public short Entity;
    public LogicItemKind Kind;
    public LogicAction Action;
    public int Gen;
    public LogicSignal Signal;
}

/// <summary>What a <see cref="Msg.Logic"/> packet says (its first byte).</summary>
public enum LogicMsgKind : byte
{
    State, // server -> all: an entity's flags (u16 entity, u8 flags)
    Use, // client -> server: the interact key at a button or lever (u16 entity)
    Mover, // server -> all: a mover's control state (u8 mover, f64 base, f64 since, f64 stop)
    Sound, // server -> all: a sound entity played (u16 entity)
    Notice, // server -> one client: a text entity's line for whoever set it off (text)
    Full, // server -> joining client: every entity's flags (u16 count, then u8 each)
}

/// <summary>
/// Map logic (Maps/README.md, "Map logic"; the definitions are MapLogic.cs). Server-authoritative and deterministic: the
/// server runs it in its fixed tick order, event by event (an event puts a signal on each of its wires; signals wait in
/// a queue for their tick, zero-delay ones run later in the same tick in the order they were sent, at most
/// <see cref="LogicState.MaxPerTick"/> a tick so a loop of zero-delay wires can't stall the server). The only polling is
/// the zones and pressure plates: each tick, every living player's feet against them. Clients get what they need to draw
/// and predict (doors, lamps, levers, buttons, mover clocks) in <see cref="Msg.Logic"/>; a mover's start or stop takes
/// effect <see cref="MoverLeadTicks"/> ticks after the server decides, so a client's predicted ticks usually have it in
/// time and the platform under you doesn't jump.
/// </summary>
public sealed partial class Game
{
    /// <summary>A mover started or stopped by map logic changes course this many ticks after the server decides it.</summary>
    public const int MoverLeadTicks = 6;

    /// <summary>How far from your eyes a button or lever is found along your aim (beyond its own reach nothing is).</summary>
    public const float LogicUseSlack = 0.35f;

    private LogicState? _logic;
    private bool _logicSpawnPaused; // zombies.pause (ZombieAI.cs)

    /// <summary>This game's map logic state (null: the map has none).</summary>
    public LogicState? Logic => EnsureLogic();

    /// <summary>Test hook: every event the server's logic sends (entity, event, signal).</summary>
    internal Action<int, LogicEvent, LogicSignal>? LogicEventTap;

    private LogicState? EnsureLogic()
    {
        var map = World.Map;
        if (map.Logic == null)
            return _logic = null;
        if (_logic == null || !ReferenceEquals(_logic.Map, map))
        {
            _logic = new LogicState(map);
            ResetLogicState(_logic);
        }
        return _logic;
    }

    /// <summary>Everything as the map starts it (both sides): flags, counters, the queue, doors and mover clocks; on the server, running timers queued.</summary>
    private void ResetLogicState(LogicState s)
    {
        var d = s.Def;
        Array.Clear(s.Ready);
        Array.Clear(s.Count);
        Array.Clear(s.WireUsed);
        Array.Clear(s.Inside);
        Array.Clear(s.Members);
        s.HeapCount = 0;
        s.Delivered = s.Sent = s.Dropped = 0;
        s.StartedSent = s.EndedSent = false;
        _logicSpawnPaused = false;
        for (var i = 0; i < d.Entities.Count; i++)
        {
            var e = d.Entities[i];
            s.Gen[i]++;
            s.Flags[i] = (byte)((e.Enabled ? 0 : LogicState.FlagDisabled) | (e.StartOn && e.Kind is LogicKind.Lever or LogicKind.Lamp or LogicKind.Door or LogicKind.Timer ? LogicState.FlagOn : 0));
            if (e.Kind == LogicKind.Not)
                s.Flags[i] |= LogicState.FlagOn; // (not false = true, quietly: gates only send when their output changes)
            if (e.Kind == LogicKind.Door)
                s.Map.SetLogicDoor(i, e.StartOn);
        }
        foreach (var m in s.Map.Movers)
            m.ResetControl();
        MoverPhysics.Invalidate();
        if (IsServer)
            for (var i = 0; i < d.Entities.Count; i++)
                if (d.Entities[i].Kind == LogicKind.Timer && s.On(i))
                    Queue(s, i, LogicItemKind.TimerFire, 0, default, d.Entities[i].Ticks, s.Gen[i]);
        _navGateGraph = null; // the nav links follow the doors again (GameLogicNav)
    }

    /// <summary>A new match (both sides; the server also tells everyone): the logic starts again.</summary>
    private void ResetLogicForMatch()
    {
        if (EnsureLogic() is not { } s)
            return;
        ResetLogicState(s);
        s.Match = MatchNumber;
        if (IsServer && !Net.IsOffline)
        {
            WriteLogicFull(_w, s);
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

    // ---- Server: the tick -------------------------------------------------------------------------------------

    /// <summary>Server, every tick: the match events, the zones and plates, then the signals due now.</summary>
    private void ServerTickLogic(double now)
    {
        _logicNow = now;
        if (EnsureLogic() is not { } s || Phase == MatchPhase.Lobby)
            return;
        if (s.Match != MatchNumber)
            ResetLogicForMatch();
        var d = s.Def;
        if (Phase == MatchPhase.Playing && !s.StartedSent)
        {
            s.StartedSent = true;
            Emit(s, d.MatchEntity, LogicEvent.Started, LogicSignal.Pulse());
        }
        else if (Phase == MatchPhase.Ended && s.StartedSent && !s.EndedSent)
        {
            s.EndedSent = true;
            Emit(s, d.MatchEntity, LogicEvent.Ended, LogicSignal.Pulse(n: Winner));
        }
        if (d.Zones.Length > 0 && Phase is MatchPhase.Playing or MatchPhase.Warmup)
            SampleZones(s);
        for (var c = 0; c < d.Challenges.Length; c++)
            if (s.On(d.Challenges[c]) && ChallengeStanding(s, c) == 0)
                EndChallenge(s, d.Challenges[c], false, LogicSignal.Pulse()); // everyone in it went down, died or left its zone
        var budget = LogicState.MaxPerTick;
        while (s.HeapCount > 0 && s.Heap[0].Due <= ServerTick && budget-- > 0)
            Run(s, Pop(s));
        SyncNavGates(s);
    }

    /// <summary>Every zone and plate against every living player's feet: entered, left, the first in, the last out.</summary>
    private void SampleZones(LogicState s)
    {
        var d = s.Def;
        for (var z = 0; z < d.Zones.Length; z++)
        {
            var i = d.Zones[z];
            var e = d.Entities[i];
            var b = e.Box;
            var count = 0;
            var enabled = s.Enabled(i);
            foreach (var p in Players.Values)
            {
                var feet = p.Position;
                var counts = p.Alive && enabled && (e.Who == LogicWho.Players || (e.Who == LogicWho.Bots) == (p.Bot != null))
                    && feet.X >= b.Min.X && feet.X <= b.Max.X && feet.Z >= b.Min.Z && feet.Z <= b.Max.Z && feet.Y >= b.Min.Y - 0.1f && feet.Y <= b.Max.Y;
                ref var was = ref s.Inside[z * 256 + p.Id];
                if (counts)
                    count++;
                if (counts == was)
                    continue;
                was = counts;
                if (e.Kind == LogicKind.Zone)
                    Emit(s, i, counts ? LogicEvent.Entered : LogicEvent.Left, new LogicSignal(counts, count, p.Id));
            }
            // A player who left the game while inside: forget them.
            if (count == 0 && s.Count[i] > 0)
                for (var k = 0; k < 256; k++)
                    s.Inside[z * 256 + k] = false;
            var before = s.Count[i];
            s.Count[i] = count;
            if ((before == 0) == (count == 0))
                continue;
            var on = count > 0;
            SetFlag(s, i, LogicState.FlagOn, on);
            var by = LastIn(s, z);
            if (e.Kind == LogicKind.Zone)
                Emit(s, i, on ? LogicEvent.Occupied : LogicEvent.Empty, new LogicSignal(on, count, by));
            else
                Emit(s, i, on ? LogicEvent.Pressed : LogicEvent.Released, new LogicSignal(on, count, by));
            Emit(s, i, LogicEvent.Changed, new LogicSignal(on, count, by));
        }
    }

    private static byte LastIn(LogicState s, int z)
    {
        for (var k = 0; k < 256; k++)
            if (s.Inside[z * 256 + k])
                return (byte)k;
        return LogicSignal.Nobody;
    }

    // ---- Server: events and signals ---------------------------------------------------------------------------

    /// <summary>Server: entity <paramref name="entity"/> sent <paramref name="ev"/>: a signal goes on each of its wires.</summary>
    internal void Emit(LogicState s, int entity, LogicEvent ev, LogicSignal sig)
    {
        if (entity < 0)
            return;
        s.Sent++;
        LogicEventTap?.Invoke(entity, ev, sig);
        var wires = s.Def.WiresOut(entity, ev);
        var first = s.Def.WireFirst[entity * MapLogic.EventCount + (int)ev];
        for (var k = 0; k < wires.Length; k++)
        {
            ref readonly var w = ref wires[k];
            if (w.When != LogicWire.NoNumber && sig.N != w.When)
                continue;
            if (w.Once)
            {
                if (s.WireUsed[first + k])
                    continue;
                s.WireUsed[first + k] = true;
            }
            var out1 = new LogicSignal(w.Value < 0 ? sig.Value : w.Value == 1, w.N == LogicWire.NoNumber ? sig.N : w.N, sig.Activator);
            Queue(s, w.To, LogicItemKind.Deliver, w.Action, out1, w.DelayTicks, 0);
        }
    }

    /// <summary>Server, from the game's own systems (Zombies, modes, props): an event of the first entity of a kind bound to something.</summary>
    internal void LogicEvent0(LogicKind kind, int target, LogicEvent ev, LogicSignal sig)
    {
        if (!IsServer || EnsureLogic() is not { } s)
            return;
        var d = s.Def;
        for (var i = 0; i < d.Entities.Count; i++)
        {
            var e = d.Entities[i];
            if (e.Kind != kind || !s.Enabled(i) || (kind is not (LogicKind.Zombies or LogicKind.Match) && e.Target != target && e.Target != -1))
                continue;
            if (kind == LogicKind.ZMachine && e.Machine != (LogicMachine)(sig.N >> 16))
                continue;
            Emit(s, i, ev, kind == LogicKind.ZMachine ? sig with { N = sig.N & 0xFFFF } : sig);
        }
    }

    private void Queue(LogicState s, int entity, LogicItemKind kind, LogicAction action, LogicSignal sig, int delay, int gen)
    {
        if (s.HeapCount >= LogicState.MaxQueued)
        {
            if (s.Dropped++ == 0)
                Log($"[logic] the signal queue is full ({LogicState.MaxQueued}): signals dropped (a loop of wires?)", false);
            return;
        }
        var item = new LogicItem { Due = ServerTick + (uint)Math.Max(0, delay), Seq = s.Seq++, Entity = (short)entity, Kind = kind, Action = action, Signal = sig, Gen = gen };
        var i = s.HeapCount++;
        s.Heap[i] = item;
        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (!Before(s.Heap[i], s.Heap[parent]))
                break;
            (s.Heap[i], s.Heap[parent]) = (s.Heap[parent], s.Heap[i]);
            i = parent;
        }
    }

    private static bool Before(in LogicItem a, in LogicItem b) => a.Due != b.Due ? a.Due < b.Due : a.Seq < b.Seq;

    private static LogicItem Pop(LogicState s)
    {
        var top = s.Heap[0];
        s.Heap[0] = s.Heap[--s.HeapCount];
        var i = 0;
        while (true)
        {
            int l = i * 2 + 1, r = l + 1, m = i;
            if (l < s.HeapCount && Before(s.Heap[l], s.Heap[m]))
                m = l;
            if (r < s.HeapCount && Before(s.Heap[r], s.Heap[m]))
                m = r;
            if (m == i)
                break;
            (s.Heap[i], s.Heap[m]) = (s.Heap[m], s.Heap[i]);
            i = m;
        }
        return top;
    }

    private void Run(LogicState s, LogicItem it)
    {
        var i = it.Entity;
        var e = s.Def.Entities[i];
        switch (it.Kind)
        {
            case LogicItemKind.Deliver:
                s.Delivered++;
                Deliver(s, i, it.Action, it.Signal);
                break;
            case LogicItemKind.TimerFire when it.Gen == s.Gen[i] && s.On(i) && s.Enabled(i):
                s.Count[i]++;
                Emit(s, i, LogicEvent.Fired, LogicSignal.Pulse(n: s.Count[i]));
                if (e.Repeat)
                    Queue(s, i, LogicItemKind.TimerFire, 0, default, e.Ticks, s.Gen[i]);
                else
                    SetFlag(s, i, LogicState.FlagOn, false);
                break;
            case LogicItemKind.DoorRetry when it.Gen == s.Gen[i] && s.On(i):
                CloseLogicDoor(s, i, it.Signal);
                break;
            case LogicItemKind.ChallengeEnd when it.Gen == s.Gen[i] && s.On(i):
                EndChallenge(s, i, true, it.Signal); // the time ran out with someone still standing: survived
                break;
            case LogicItemKind.CoolDown:
                SetFlag(s, i, LogicState.FlagCooling, false);
                break;
            case LogicItemKind.MoverArrived when it.Gen == s.Gen[i]:
                Emit(s, i, LogicEvent.Arrived, it.Signal);
                Emit(s, i, LogicEvent.Stopped, it.Signal);
                break;
        }
    }

    /// <summary>Server: entity <paramref name="i"/> gets action <paramref name="a"/>.</summary>
    private void Deliver(LogicState s, int i, LogicAction a, LogicSignal sig)
    {
        var d = s.Def;
        var e = d.Entities[i];
        if (a == LogicAction.Enable || a == LogicAction.Disable)
        {
            SetFlag(s, i, LogicState.FlagDisabled, a == LogicAction.Disable);
            if (e.Kind == LogicKind.Timer && a == LogicAction.Enable && s.On(i))
            {
                s.Gen[i]++;
                Queue(s, i, LogicItemKind.TimerFire, 0, default, e.Ticks, s.Gen[i]); // (a timer counts again from its interval)
            }
            return;
        }
        if (!s.Enabled(i))
            return;
        switch (e.Kind)
        {
            case LogicKind.Button when a == LogicAction.Press:
                Emit(s, i, LogicEvent.Pressed, sig with { Value = true });
                break;
            case LogicKind.Lever:
                SetLever(s, i, a switch { LogicAction.Toggle => !s.On(i), LogicAction.TurnOn => true, LogicAction.TurnOff => false, _ => sig.Value }, sig.Activator);
                break;
            case LogicKind.Timer:
            {
                var run = a switch { LogicAction.Start => true, LogicAction.Stop => false, LogicAction.Toggle => !s.On(i), _ => true };
                if (a == LogicAction.Reset)
                    s.Count[i] = 0;
                if (run == s.On(i) && a != LogicAction.Reset)
                    break;
                s.Gen[i]++;
                SetFlag(s, i, LogicState.FlagOn, run);
                if (run)
                    Queue(s, i, LogicItemKind.TimerFire, 0, default, e.Ticks, s.Gen[i]);
                break;
            }
            case LogicKind.Relay:
                Emit(s, i, LogicEvent.Out, sig);
                break;
            case LogicKind.And or LogicKind.Or:
            {
                if (a == LogicAction.Reset)
                    s.Count[i] = 0;
                else
                {
                    var bit = 1 << (a - LogicAction.A);
                    s.Count[i] = sig.Value ? s.Count[i] | bit : s.Count[i] & ~bit;
                }
                var all = (1 << e.Target) - 1;
                var result = e.Kind == LogicKind.And ? (s.Count[i] & all) == all : (s.Count[i] & all) != 0;
                GateOut(s, i, result, sig);
                break;
            }
            case LogicKind.Not:
                GateOut(s, i, !sig.Value, sig);
                break;
            case LogicKind.Counter:
            {
                var before = s.Count[i];
                s.Count[i] = a switch
                {
                    LogicAction.Add => before + (sig.N > 0 ? sig.N : 1),
                    LogicAction.Subtract => before - (sig.N > 0 ? sig.N : 1),
                    LogicAction.Set => sig.N,
                    _ => 0,
                };
                if (s.Count[i] == before)
                    break;
                Emit(s, i, LogicEvent.Changed, sig with { N = s.Count[i] });
                if (s.Count[i] == e.Target || (before < e.Target && s.Count[i] > e.Target))
                {
                    Emit(s, i, LogicEvent.Reached, sig with { Value = true, N = s.Count[i] });
                    if (e.Repeat)
                        s.Count[i] = 0;
                }
                break;
            }
            case LogicKind.Random:
            {
                var h = (uint)(World.Map.ContentHash ^ (uint)MatchNumber * 2654435761u ^ (uint)(s.Count[i]++) * 40503u ^ (uint)i * 97u);
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                Emit(s, i, (LogicEvent)((int)LogicEvent.Out1 + (int)(h % (uint)e.Target)), sig);
                break;
            }
            case LogicKind.Door:
            {
                var open = a switch { LogicAction.Open => true, LogicAction.Close => false, LogicAction.Toggle => !s.On(i), _ => sig.Value };
                if (open)
                {
                    s.Gen[i]++; // (a retry to shut it is called off)
                    if (!s.On(i))
                    {
                        SetFlag(s, i, LogicState.FlagOn, true);
                        s.Map.SetLogicDoor(i, true);
                        Emit(s, i, LogicEvent.Opened, sig with { Value = true });
                    }
                }
                else if (s.On(i))
                    CloseLogicDoor(s, i, sig);
                break;
            }
            case LogicKind.Lamp:
                SetLamp(s, i, a switch { LogicAction.TurnOn => true, LogicAction.TurnOff => false, LogicAction.Toggle => !s.On(i), _ => sig.Value }, sig);
                break;
            case LogicKind.Mover:
                ControlMover(s, i, a, sig);
                break;
            case LogicKind.Sound:
                PlayLogicSound(e);
                if (IsServer && !Net.IsOffline)
                {
                    _w.Begin(Msg.Logic).U8((byte)LogicMsgKind.Sound).U16((ushort)i);
                    Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
                }
                break;
            case LogicKind.Text:
                if (!e.ToActivator)
                    ModeEvent(e.Text, Sfx.HitTick);
                else if (Players.TryGetValue(sig.Activator, out var who))
                    LogicNoticeTo(who, e.Text);
                break;
            case LogicKind.Teleport:
                if (Players.TryGetValue(sig.Activator, out var tp) && tp.Alive)
                {
                    tp.Teleport(e.At + new Vector3(0, 0.05f, 0));
                    tp.Yaw = e.Yaw;
                    Emit(s, i, LogicEvent.Sent, sig);
                }
                break;
            case LogicKind.Breakable:
                if (!World.Map.IsPropBroken(e.Target))
                    ServerBreakProp(e.Target, e.At, PropDamage.Explosion, null, 0, env: true); // (its "destroyed" goes from ServerBreakProp)
                break;
            case LogicKind.Challenge:
                if (a == LogicAction.Start && !s.On(i))
                    StartChallenge(s, i, sig);
                else if (a is LogicAction.Stop or LogicAction.Fail && s.On(i))
                    EndChallenge(s, i, false, sig);
                else if (a == LogicAction.Win && s.On(i))
                    EndChallenge(s, i, true, sig);
                break;
            case LogicKind.Reward:
                GiveReward(s, i, a, sig);
                break;
            case LogicKind.ZDoor when ZMap != null:
                OpenDoor(e.Target, Players.GetValueOrDefault(sig.Activator)); // (its "opened" goes from OpenDoor)
                break;
            case LogicKind.Zombies when ZMap != null:
                switch (a)
                {
                    case LogicAction.Power when !ZPower:
                        ZPower = true;
                        ModeEvent("The power is on!", Sfx.StreakReady);
                        ZBanner("THE POWER IS ON");
                        Emit(s, i, LogicEvent.Power, sig);
                        break;
                    case LogicAction.Spawn when ZPhase == ZRoundPhase.Round && sig.N > 0:
                        _zToSpawn += Math.Min(sig.N, 1000);
                        break;
                    case LogicAction.Pause:
                        _logicSpawnPaused = true;
                        break;
                    case LogicAction.Resume:
                        _logicSpawnPaused = false;
                        break;
                }
                break;
        }
    }

    // ---- Challenges and rewards ------------------------------------------------------------------------------

    private double _logicNow;

    /// <summary>Does <paramref name="p"/> count in challenge <paramref name="e"/> now: alive (in Zombies: standing), the right kind of player, in its zone if it has one?</summary>
    private bool InChallenge(LogicState s, LogicEntity e, Player p)
    {
        if (!p.Alive || (Mode == GameMode.Zombies && ZMap != null && IsDowned(p)))
            return false;
        if (e.Who != LogicWho.Players && (e.Who == LogicWho.Bots) != (p.Bot != null))
            return false;
        if (e.Zone < 0)
            return true;
        var b = s.Def.Entities[e.Zone].Box;
        var f = p.Position;
        return f.X >= b.Min.X && f.X <= b.Max.X && f.Z >= b.Min.Z && f.Z <= b.Max.Z && f.Y >= b.Min.Y - 0.1f && f.Y <= b.Max.Y;
    }

    /// <summary>How many of challenge <paramref name="c"/>'s players still count (a player out of it stays out).</summary>
    private int ChallengeStanding(LogicState s, int c)
    {
        var e = s.Def.Entities[s.Def.Challenges[c]];
        var n = 0;
        foreach (var p in Players.Values)
        {
            ref var m = ref s.Members[c * 256 + p.Id];
            if (m && !InChallenge(s, e, p))
                m = false;
            if (m)
                n++;
        }
        return n;
    }

    private void StartChallenge(LogicState s, int i, LogicSignal sig)
    {
        var e = s.Def.Entities[i];
        var c = Array.IndexOf(s.Def.Challenges, i);
        var n = 0;
        for (var k = 0; k < 256; k++)
            s.Members[c * 256 + k] = false;
        foreach (var p in Players.Values)
            if (InChallenge(s, e, p))
            {
                s.Members[c * 256 + p.Id] = true;
                n++;
            }
        if (n == 0)
        {
            Emit(s, i, LogicEvent.Failed, sig with { Value = false, N = 0 }); // nobody to take it on
            Emit(s, i, LogicEvent.Ended, sig with { Value = false, N = 0 });
            return;
        }
        s.Gen[i]++;
        SetFlag(s, i, LogicState.FlagOn, true);
        Queue(s, i, LogicItemKind.ChallengeEnd, 0, sig, e.Ticks, s.Gen[i]);
        Emit(s, i, LogicEvent.Started, sig with { N = n });
    }

    /// <summary>Server: a challenge is over: survived pays every player still in it its reward (Zombies points), failed pays nothing.</summary>
    private void EndChallenge(LogicState s, int i, bool survived, LogicSignal sig)
    {
        var e = s.Def.Entities[i];
        var c = Array.IndexOf(s.Def.Challenges, i);
        var n = ChallengeStanding(s, c);
        survived &= n > 0;
        s.Gen[i]++;
        SetFlag(s, i, LogicState.FlagOn, false);
        if (survived && e.Points > 0 && Mode == GameMode.Zombies && ZMap != null)
            foreach (var p in Players.Values)
                if (s.Members[c * 256 + p.Id])
                    AddPoints(p, e.Points);
        Emit(s, i, survived ? LogicEvent.Survived : LogicEvent.Failed, sig with { Value = survived, N = n });
        Emit(s, i, LogicEvent.Ended, sig with { Value = survived, N = n });
    }

    /// <summary>Server: a reward: points to whoever set it off ("give") or to everyone standing ("giveAll"), and/or a Zombies power-up dropped ("drop", or with either).</summary>
    private void GiveReward(LogicState s, int i, LogicAction a, LogicSignal sig)
    {
        var e = s.Def.Entities[i];
        var zombies = Mode == GameMode.Zombies && ZMap != null;
        if (zombies && e.Points > 0 && a != LogicAction.Drop) // (points and power-ups are the Zombies mode's: elsewhere only "given" goes out)
        {
            if (a == LogicAction.Give)
            {
                if (Players.TryGetValue(sig.Activator, out var p))
                    AddPoints(p, e.Points);
            }
            else
                foreach (var p in Players.Values)
                    if (IsStanding(p))
                        AddPoints(p, e.Points);
        }
        if (zombies && e.Zone >= 0)
            SpawnPowerUp(e.Zone >= (int)ZPowerKind.COUNT ? PickPowerKind(ZRand()) is var k && k != ZPowerKind.COUNT ? k : ZPowerKind.MaxAmmo : (ZPowerKind)e.Zone, e.At, _logicNow);
        Emit(s, i, LogicEvent.Given, sig);
    }

    private void GateOut(LogicState s, int i, bool result, LogicSignal sig)
    {
        if (result == s.On(i))
            return;
        SetFlag(s, i, LogicState.FlagOn, result);
        var o = sig with { Value = result };
        Emit(s, i, LogicEvent.Out, o);
        Emit(s, i, result ? LogicEvent.True : LogicEvent.False, o);
    }

    private void SetLever(LogicState s, int i, bool on, byte by)
    {
        if (on == s.On(i))
            return;
        SetFlag(s, i, LogicState.FlagOn, on);
        var sig = new LogicSignal(on, on ? 1 : 0, by);
        Emit(s, i, on ? LogicEvent.On : LogicEvent.Off, sig);
        Emit(s, i, LogicEvent.Changed, sig);
    }

    private void SetLamp(LogicState s, int i, bool on, LogicSignal sig)
    {
        if (on == s.On(i))
            return;
        SetFlag(s, i, LogicState.FlagOn, on);
        Emit(s, i, on ? LogicEvent.On : LogicEvent.Off, sig with { Value = on });
    }

    /// <summary>Server: shuts a door unless someone stands in it (then it tries again every half second until it can, or is told to open).</summary>
    private void CloseLogicDoor(LogicState s, int i, LogicSignal sig)
    {
        var box = s.Def.Entities[i].Box;
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var hb = World.Hitbox(p.Position, p.Sim.Stance, p.Yaw);
            if (hb.Min.X < box.Max.X && hb.Max.X > box.Min.X && hb.Min.Y < box.Max.Y && hb.Max.Y > box.Min.Y && hb.Min.Z < box.Max.Z && hb.Max.Z > box.Min.Z)
            {
                RetryDoor(s, i, sig); // someone's in the doorway: it shuts the moment it's clear
                return;
            }
        }
        if (NpcInLeaf(box))
        {
            RetryDoor(s, i, sig);
            return;
        }
        s.Map.SetLogicDoor(i, false);
        SetFlag(s, i, LogicState.FlagOn, false); // (clients put the box back too)
        Emit(s, i, LogicEvent.Closed, sig with { Value = false });
    }

    private void RetryDoor(LogicState s, int i, LogicSignal sig)
    {
        s.Gen[i]++;
        Queue(s, i, LogicItemKind.DoorRetry, 0, sig, World.TickRate / 2, s.Gen[i]);
    }

    private void ControlMover(LogicState s, int i, LogicAction a, LogicSignal sig)
    {
        var e = s.Def.Entities[i];
        var m = s.Map.Movers[e.Target];
        var at = (double)ServerTick + MoverLeadTicks;
        var wasRunning = m.RunningAt(at);
        double arrive = -1;
        switch (a)
        {
            case LogicAction.Start:
                m.ControlStart(at);
                break;
            case LogicAction.Stop:
                m.ControlStop(at);
                break;
            case LogicAction.Toggle:
                if (wasRunning)
                    m.ControlStop(at);
                else
                    m.ControlStart(at);
                break;
            case LogicAction.Advance:
                arrive = m.ControlAdvance(at);
                break;
            case LogicAction.Reset:
                m.ControlReset(at);
                break;
        }
        MoverPhysics.Invalidate();
        s.Gen[i]++;
        SetFlag(s, i, LogicState.FlagOn, m.RunningAt(at), broadcast: false);
        BroadcastMover(e.Target, m);
        var running = !double.IsPositiveInfinity(m.RunStop) ? arrive >= 0 : m.RunningAt(at);
        if (running && !wasRunning)
            Emit(s, i, LogicEvent.Started, sig);
        else if (!running && wasRunning)
            Emit(s, i, LogicEvent.Stopped, sig with { Value = false });
        if (arrive >= 0)
            Queue(s, i, LogicItemKind.MoverArrived, 0, sig, (int)Math.Ceiling(arrive - ServerTick), s.Gen[i]);
    }

    private void SetFlag(LogicState s, int i, byte flag, bool on, bool broadcast = true)
    {
        var f = (byte)(on ? s.Flags[i] | flag : s.Flags[i] & ~flag);
        if (f == s.Flags[i])
            return;
        s.Flags[i] = f;
        if (broadcast)
            BroadcastLogicState(s, i);
    }

    private void BroadcastLogicState(LogicState s, int i)
    {
        if (!IsServer || Net.IsOffline || !Visible(s.Def.Entities[i].Kind))
            return;
        _w.Begin(Msg.Logic).U8((byte)LogicMsgKind.State).U16((ushort)i).U8(s.Flags[i]);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Do clients need this kind's flags (to draw it or hint it)? The rest live on the server only.</summary>
    private static bool Visible(LogicKind k) => k is LogicKind.Button or LogicKind.Lever or LogicKind.Plate or LogicKind.Door or LogicKind.Lamp;

    private void BroadcastMover(int index, MoverDef m)
    {
        if (!IsServer || Net.IsOffline)
            return;
        WriteLogicMover(_w, index, m);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void PlayLogicSound(LogicEntity e)
    {
        var sfx = (Sfx)e.Target;
        if (e.Range <= 0)
            Audio.Effect(sfx, 0.7f * e.Volume, MixBus.Notify);
        else
            Audio.Effect3DSmall(sfx, e.At, e.Volume, e.Range);
    }

    private void LogicNoticeTo(Player p, string text)
    {
        if (p.Id == LocalId)
        {
            LoadoutNotice = text;
            Log(text, false);
        }
        else if (p.Bot == null && !Net.IsOffline && GuidOf(p) is { } g)
        {
            _w.Begin(Msg.Logic).U8((byte)LogicMsgKind.Notice).Str(text);
            Net.SendTo(g, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    // ---- Hooks from the rest of the game (server) -------------------------------------------------------------

    /// <summary>Server: a breakable prop broke (GameProps.cs).</summary>
    internal void LogicPropBroken(int prop, Player? by) => LogicEvent0(LogicKind.Breakable, prop, LogicEvent.Destroyed, LogicSignal.Pulse(by?.Id ?? LogicSignal.Nobody));

    /// <summary>Server: a Zombies door or debris opened (bought, or by logic).</summary>
    internal void LogicZDoorOpened(int door, Player? by) => LogicEvent0(LogicKind.ZDoor, door, LogicEvent.Opened, LogicSignal.Pulse(by?.Id ?? LogicSignal.Nobody));

    /// <summary>Server: a Zombies machine was used (a buy went through).</summary>
    internal void LogicZMachineUsed(LogicMachine machine, int index, Player p) =>
        LogicEvent0(LogicKind.ZMachine, index, LogicEvent.Used, LogicSignal.Pulse(p.Id, ((int)machine << 16) | (index & 0xFFFF)));

    /// <summary>Server: the Zombies power came on, a round started or ended.</summary>
    internal void LogicZombies(LogicEvent ev, int n, Player? by = null) => LogicEvent0(LogicKind.Zombies, 0, ev, LogicSignal.Pulse(by?.Id ?? LogicSignal.Nobody, n));

    /// <summary>Server: a flag was taken or captured, a hill taken or moved (the modes; n: the team).</summary>
    internal void LogicObjective(LogicEvent ev, int team, Player? by = null) => LogicEvent0(LogicKind.Match, 0, ev, LogicSignal.Pulse(by?.Id ?? LogicSignal.Nobody, team));

    /// <summary>Is this Zombies door or machine switched off by map logic (a disabled zdoor / zmachine)?</summary>
    internal bool LogicLocksZ(LogicKind kind, LogicMachine machine, int index)
    {
        if (_logic is not { } s || !ReferenceEquals(s.Map, World.Map))
            return false;
        var d = s.Def;
        for (var i = 0; i < d.Entities.Count; i++)
        {
            var e = d.Entities[i];
            if (e.Kind == kind && (kind == LogicKind.ZDoor || e.Machine == machine) && (e.Target == index || e.Target == -1) && !s.Enabled(i))
                return true;
        }
        return false;
    }

    // ---- The interact key (both sides) ------------------------------------------------------------------------

    /// <summary>The button or lever the local player is looking at and close enough to use, or -1.</summary>
    public int AimedLogic()
    {
        if (Local is not { Alive: true } me || EnsureLogic() is not { } s)
            return -1;
        return LogicAlong(s, me.Eye, LookDir(LookYaw, LookPitch));
    }

    /// <summary>The usable button or lever nearest along a look ray (its hot spot is a 0.5 m ball round it), or -1.</summary>
    public static int LogicAlong(LogicState s, Vector3 eye, Vector3 dir)
    {
        var best = -1;
        var bestT = float.MaxValue;
        var d = s.Def;
        for (var i = 0; i < d.Entities.Count; i++)
        {
            var e = d.Entities[i];
            if (e.Kind is not (LogicKind.Button or LogicKind.Lever))
                continue;
            var to = e.At - eye;
            var t = Vector3.Dot(to, dir);
            if (t < 0 || t > e.Reach + LogicUseSlack || Vector3.DistanceSquared(eye + dir * t, e.At) > 0.5f * 0.5f || t >= bestT)
                continue;
            if (World.Query.Raycast(eye, dir, t - 0.3f, QueryFilter.All) < t - 0.35f)
                continue; // a wall in the way
            bestT = t;
            best = i;
        }
        return best;
    }

    /// <summary>The interact key at a button or lever: uses it. False if you're not looking at one.</summary>
    public bool TryUseLogic(double now)
    {
        var i = AimedLogic();
        if (i < 0)
            return false;
        if (IsServer)
        {
            if (Local is { } me)
                ServerUseLogic(me, i, now);
        }
        else
        {
            _w.Begin(Msg.Logic).U8((byte)LogicMsgKind.Use).U16((ushort)i);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
        return true;
    }

    /// <summary>The HUD hint for the button or lever you're looking at, or null.</summary>
    public string? LogicHint()
    {
        var i = AimedLogic();
        if (i < 0 || _logic is not { } s)
            return null;
        var e = s.Def.Entities[i];
        if (!s.Enabled(i) || (s.Flags[i] & LogicState.FlagSpent) != 0)
            return null;
        if (e.NeedsPower && !ZPower && Mode == GameMode.Zombies)
            return $"{e.Text}: needs the power on";
        if ((s.Flags[i] & LogicState.FlagCooling) != 0)
            return $"{e.Text} (not yet)";
        return e.Cost > 0 && Mode == GameMode.Zombies ? $"{e.Text} [{e.Cost}]" : e.Text;
    }

    /// <summary>Server: <paramref name="p"/> pressed the interact key at button or lever <paramref name="i"/>.</summary>
    internal bool ServerUseLogic(Player p, int i, double now)
    {
        _ = now;
        if (EnsureLogic() is not { } s || (uint)i >= (uint)s.Def.Entities.Count || !p.Alive || Phase is not (MatchPhase.Playing or MatchPhase.Warmup))
            return false;
        var e = s.Def.Entities[i];
        if (e.Kind is not (LogicKind.Button or LogicKind.Lever) || !s.Enabled(i) || (s.Flags[i] & LogicState.FlagSpent) != 0)
            return false;
        if (Vector3.Distance(p.Eye, e.At) > e.Reach + 1.2f) // (a little slack for lag)
            return false;
        if (ServerTick < s.Ready[i])
            return false;
        var zombies = Mode == GameMode.Zombies && ZMap != null;
        if (zombies && e.NeedsPower && !ZPower)
            return false;
        if (zombies && e.Cost > 0)
        {
            var zp = ZP(p);
            if (zp.Points < e.Cost)
            {
                if (p.Id == LocalId)
                    LoadoutNotice = $"Not enough points ({zp.Points} of {e.Cost}).";
                else
                    LogicNoticeTo(p, $"Not enough points ({zp.Points} of {e.Cost}).");
                return false;
            }
            zp.Points -= e.Cost;
        }
        s.Ready[i] = ServerTick + (uint)e.Ticks;
        if (e.Once)
            SetFlag(s, i, LogicState.FlagSpent, true);
        else if (e.Ticks > 0)
        {
            SetFlag(s, i, LogicState.FlagCooling, true);
            Queue(s, i, LogicItemKind.CoolDown, 0, default, e.Ticks, 0);
        }
        var sig = LogicSignal.Pulse(p.Id);
        if (e.Kind == LogicKind.Button)
            Emit(s, i, LogicEvent.Pressed, sig);
        else
            SetLever(s, i, !s.On(i), p.Id);
        Audio.Effect3DSmall(Sfx.HitTick, e.At, 0.6f, 20f);
        return true;
    }

    // ---- Self-test hooks ---------------------------------------------------------------------------------------

    /// <summary>Self-test: entity <paramref name="entity"/> gets <paramref name="action"/> on the next logic tick (as if a wire delivered it).</summary>
    internal void SelfTestLogicDeliver(int entity, LogicAction action, LogicSignal sig)
    {
        if (EnsureLogic() is { } s)
            Queue(s, entity, LogicItemKind.Deliver, action, sig, 0, 0);
    }

    /// <summary>Self-test: entity <paramref name="entity"/> sends <paramref name="ev"/> now.</summary>
    internal void SelfTestLogicEmit(int entity, LogicEvent ev, LogicSignal sig)
    {
        if (EnsureLogic() is { } s)
            Emit(s, entity, ev, sig);
    }

    /// <summary>Self-test (a client): sends the interact key at button or lever <paramref name="entity"/> as if you were looking at it.</summary>
    internal void SelfTestSendLogicUse(int entity)
    {
        _w.Begin(Msg.Logic).U8((byte)LogicMsgKind.Use).U16((ushort)entity);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Self-test: one logic tick on its own (the tick counter moves on by one).</summary>
    internal void SelfTestLogicTick()
    {
        ServerTick++;
        ServerTickLogic(_logicNow + 1.0 / World.TickRate);
    }

    // ---- Packets ----------------------------------------------------------------------------------------------

    public static void WriteLogicMover(PacketWriter w, int index, MoverDef m) =>
        w.Begin(Msg.Logic).U8((byte)LogicMsgKind.Mover).U8((byte)index).F64(m.RunBase).F64(m.RunSince).F64(m.RunStop);

    /// <summary>Every entity's flags (one packet: at most 256 entities), then a packet per controlled mover (sent by the caller after).</summary>
    public static void WriteLogicFull(PacketWriter w, LogicState s)
    {
        w.Begin(Msg.Logic).U8((byte)LogicMsgKind.Full).U16((ushort)s.Flags.Length);
        foreach (var f in s.Flags)
            w.U8(f);
    }

    /// <summary>Server: a joining player gets the logic as it is now (flags, then every mover map logic has touched).</summary>
    private void SendLogicState(RakNetGUID to)
    {
        if (EnsureLogic() is not { } s)
            return;
        WriteLogicFull(_w, s);
        Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
        for (var m = 0; m < s.Map.Movers.Count; m++)
        {
            var mv = s.Map.Movers[m];
            if (mv.RunSince == 0 && mv.RunBase == 0 && double.IsPositiveInfinity(mv.RunStop) && mv.StartsRunning)
                continue;
            WriteLogicMover(_w, m, mv);
            Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    private void OnLogicPacket(ref PacketReader r, RakNetGUID sender, double now)
    {
        if (!r.Has(1))
            return;
        var kind = (LogicMsgKind)r.U8();
        if (kind == LogicMsgKind.Use)
        {
            if (IsServer && r.Has(2) && _clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p))
                ServerUseLogic(p, r.U16(), now);
            return;
        }
        if (IsServer)
            return;
        ApplyLogicPacket(kind, ref r);
    }

    /// <summary>Client: applies one <see cref="Msg.Logic"/> packet (after its id and kind). Short or garbage packets do nothing.</summary>
    internal void ApplyLogicPacket(LogicMsgKind kind, ref PacketReader r)
    {
        var s = EnsureLogic();
        switch (kind)
        {
            case LogicMsgKind.State when s != null && r.Has(3):
            {
                int i = r.U16();
                var f = r.U8();
                if ((uint)i < (uint)s.Flags.Length)
                    ClientSetFlags(s, i, f);
                break;
            }
            case LogicMsgKind.Full when s != null && r.Has(2):
            {
                int n = r.U16();
                for (var i = 0; i < n && r.Has(1); i++)
                {
                    var f = r.U8();
                    if (i < s.Flags.Length)
                        ClientSetFlags(s, i, f);
                }
                break;
            }
            case LogicMsgKind.Mover when r.Has(25):
            {
                int m = r.U8();
                double b = r.F64(), since = r.F64(), stop = r.F64();
                if ((uint)m < (uint)World.Map.Movers.Count && double.IsFinite(b) && double.IsFinite(since) && !double.IsNaN(stop))
                {
                    var mv = World.Map.Movers[m];
                    (mv.RunBase, mv.RunSince, mv.RunStop) = (b, since, stop);
                    MoverPhysics.Invalidate();
                    LogicMoverPackets++;
                }
                break;
            }
            case LogicMsgKind.Sound when s != null && r.Has(2):
            {
                int i = r.U16();
                if ((uint)i < (uint)s.Def.Entities.Count && s.Def.Entities[i].Kind == LogicKind.Sound)
                    PlayLogicSound(s.Def.Entities[i]);
                break;
            }
            case LogicMsgKind.Notice when r.Has(1):
                LoadoutNotice = r.Str();
                break;
        }
    }

    /// <summary>Client: mover control packets applied (self-test).</summary>
    internal int LogicMoverPackets;

    private static void ClientSetFlags(LogicState s, int i, byte f)
    {
        s.Flags[i] = f;
        if (s.Def.Entities[i].Kind == LogicKind.Door)
            s.Map.SetLogicDoor(i, (f & LogicState.FlagOn) != 0);
    }
}
