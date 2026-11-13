using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>Client to server, <see cref="Msg.Tutorial"/>: what the player did with the induction.</summary>
public enum TutorialOp : byte
{
    State, // server -> client: u8 step, u8 TutorialState
    Cue, // client -> server: u8 TutorialCue (only the ones the client alone sees)
    Skip,
    Replay, // from the first step again
    Resume, // a skipped induction carries on where it stopped
    Group, // server -> client (co-op): u8 this person has done the step, u8 others in their group, u8 of them done, text who the group waits for
}

/// <summary>
/// The warden's induction (docs/sandbox/TUTORIAL.md): the first tutorial, one step at a time from
/// Story/Tutorial/Induction.json. The server owns each person's place in it (kept in a saved world per person), judges
/// every cue from what it already has (their input, hands and the actions it carried out) and sends the place to that
/// person when it changes and when they arrive; the client draws it (UiTutorial.cs) and reports only the windows it
/// opens. Sandbox only. A new person in a survival world starts it 3 s after arriving, unless the story opening has
/// claimed them (<see cref="TutorialDefer"/>) and starts it when they wake (<see cref="TutorialBegin"/>).
/// Costs a check per person a tick while it runs, nothing allocated.
/// </summary>
public sealed partial class Game
{
    /// <summary>A new person's induction starts this long after they arrive, unless the opening claimed them.</summary>
    public const double TutorialAutoDelay = 3;

    /// <summary>
    /// Whether a new person in a survival world starts it by itself. The self-test turns it off: the induction holds the gun and
    /// the tools, and the sections that use them in survival aren't about it.
    /// </summary>
    public static bool TutorialAutoStart = true;

    /// <summary>A step stays up at least this long before it can complete (one already done shows as noted, not a flicker).</summary>
    public const double TutorialMinStep = 1.2;

    /// <summary>
    /// Co-op: a group's step moves on when everyone in it alive has done it, or this long after the first one did (the warden
    /// doesn't wait for ever: a friend away from the keyboard can't hold the rest up).
    /// </summary>
    public const double TutorialGroupWait = 45;

    // ---- Server: each person's place, by player id
    private readonly byte[] _tutStep = new byte[MaxPlayers];
    private readonly TutorialState[] _tutState = new TutorialState[MaxPlayers];
    private readonly double[] _tutStepAt = new double[MaxPlayers];
    private readonly double[] _tutArrivedAt = new double[MaxPlayers];
    private readonly bool[] _tutDeferred = new bool[MaxPlayers];
    private readonly float[] _tutAmount = new float[MaxPlayers];
    private readonly float[] _tutLastYaw = new float[MaxPlayers];
    private readonly Vector3[] _tutLastPos = new Vector3[MaxPlayers];
    private readonly uint[] _tutSaw = new uint[MaxPlayers]; // cues seen this step (bit per TutorialCue)
    private readonly bool[] _tutWasRunning = new bool[MaxPlayers]; // (the gun and tools are held: their return is said once)
    private readonly Player?[] _tutWho = new Player?[MaxPlayers]; // whose state the slot holds (a new Player object in the slot: a new person)
    // Co-op (docs/sandbox/TUTORIAL.md "Together"): people on the same step go through it as a group, which moves on together.
    private readonly int[] _tutGroup = new int[MaxPlayers]; // the group (0: none yet: joins one at its step on the next tick)
    private readonly bool[] _tutDone = new bool[MaxPlayers]; // has done this step, and waits for the group
    private readonly double[] _tutDoneAt = new double[MaxPlayers];
    private readonly bool[] _tutGroupDirty = new bool[MaxPlayers]; // what they were last told of their group is out of date
    private readonly byte[] _tutGroupSent = new byte[MaxPlayers]; // the others in their group, as last told
    private readonly int[] _tutPassOf = new int[MaxPlayers];
    private int _tutNextGroup, _tutPass;
    private PacketWriter? _tutW;

    // ---- Client (and the host's own): this PC's player's place
    public byte TutorialStepNow { get; private set; }
    public TutorialState TutorialStateNow { get; private set; }
    public double TutorialChangedAt { get; private set; } = -1;

    /// <summary>Co-op, this PC's player: how many others go through the induction with them, whether they have done this step, how many of the others have, and who they wait for.</summary>
    public int TutorialGroupOthers { get; private set; }
    public bool TutorialGroupDone { get; private set; }
    public int TutorialGroupOthersDone { get; private set; }
    public string TutorialGroupWaiting { get; private set; } = "";
    private bool _tutCueSent;

    /// <summary>The step this PC's player is on, while it runs, or null.</summary>
    public TutorialStep? TutorialCurrent =>
        SandboxOn && TutorialStateNow == TutorialState.Running && TutorialStepNow < Tutorial.Steps.Length ? Tutorial.Steps[TutorialStepNow] : null;

    /// <summary>The story opening (GameSandboxOpening.cs) is playing for this person: the induction doesn't start by itself.</summary>
    partial void TutorialDefer(byte id)
    {
        if (id >= MaxPlayers)
            return;
        if (Players.TryGetValue(id, out var p) && _tutWho[id] != p)
            TutorialFresh(p); // (claimed before the induction first met them: the claim must outlast that)
        _tutDeferred[id] = true;
    }

    /// <summary>
    /// Server: start this person's induction now (the opening, when they wake in the colony). A person who finished or skipped
    /// it in this world isn't asked again; the Esc menu replays it.
    /// </summary>
    partial void TutorialBegin(byte id)
    {
        if (id >= MaxPlayers || !IsServer)
            return;
        Tutorial.EnsureLoaded();
        _tutDeferred[id] = false;
        if (_tutState[id] != TutorialState.None || Tutorial.Steps.Length == 0 || !StartInduction)
            return; // (a start without the induction: GameStoryStart.cs)
        TutorialGo(id, 0, TutorialState.Running);
    }

    /// <summary>Server: a person arrived in a saved world: their place from their record, sent to them.</summary>
    private void TutorialArrive(Player p, WorldPlayer rec)
    {
        TutorialFresh(p);
        if (rec.TutorialState is > TutorialState.None and <= TutorialState.Skipped)
        {
            _tutState[p.Id] = rec.TutorialState;
            _tutStep[p.Id] = rec.TutorialStep;
        }
        TutorialSend(p.Id);
    }

    /// <summary>A saved world's tutorial section (WorldSaveWriter.TutorialTag): each person's place, onto their record.</summary>
    private static void WorldReadTutorial(SaveReader r, WorldSession ws)
    {
        var n = r.Count(18);
        for (var k = 0; k < n; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var step = r.U8();
            var state = (TutorialState)r.U8();
            if (state > TutorialState.Skipped)
                continue;
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam)
                {
                    rec.TutorialStep = step;
                    rec.TutorialState = state;
                    break;
                }
        }
    }

    /// <summary>Server: what goes in a person's record when the world is saved or they leave.</summary>
    private void TutorialCatch(Player p, WorldPlayer rec)
    {
        if (_tutWho[p.Id] != p)
            return;
        rec.TutorialState = _tutState[p.Id];
        rec.TutorialStep = _tutStep[p.Id];
    }

    private void TutorialFresh(Player p)
    {
        var id = p.Id;
        _tutDeferred[id] = _tutWho[id] == p && _tutDeferred[id]; // (the same person met again on arriving in a world: the opening's claim stands)
        _tutWho[id] = p;
        _tutState[id] = TutorialState.None;
        _tutStep[id] = 0;
        _tutWasRunning[id] = false;
        _tutArrivedAt[id] = SandboxNow;
        _tutSaw[id] = 0;
        _tutAmount[id] = 0;
        _tutGroup[id] = 0;
        _tutDone[id] = false;
        _tutGroupSent[id] = 0;
    }

    /// <summary>Server: a person to a step and state. <paramref name="keepGroup"/>: their group moves on with them; otherwise they find a group at that step afresh.</summary>
    private void TutorialGo(byte id, int step, TutorialState state, bool keepGroup = false)
    {
        _tutStep[id] = (byte)step;
        _tutState[id] = state;
        if (!keepGroup || state != TutorialState.Running)
            _tutGroup[id] = 0;
        _tutDone[id] = false;
        _tutGroupDirty[id] = true;
        _tutStepAt[id] = SandboxNow;
        _tutSaw[id] = 0;
        _tutAmount[id] = 0;
        if (Players.TryGetValue(id, out var p))
        {
            _tutLastYaw[id] = p.Yaw;
            _tutLastPos[id] = p.Position;
            TutorialIssue(p, step, state);
            if (state == TutorialState.Running && step < Tutorial.Steps.Length && Tutorial.Steps[step].Drops.Length > 0 && p.Alive)
                TutorialDrop(p, Tutorial.Steps[step]);
            QuarryTutorialStep(p); // (the step's marker, and a bus called for the last step)
        }
        TutorialSend(id);
    }

    /// <summary>
    /// Server: the warden sets the step's parts ("drops") down in a row in front of the person, so a step that needs a part
    /// always has one to hand (they belong to that person, like a part they dropped).
    /// </summary>
    private void TutorialDrop(Player p, TutorialStep step)
    {
        var fwd = GravityFrames.FlatForward(p.Position, p.Yaw);
        var up = GravityFrames.UpAt(p.Position);
        var side = Vector3.Normalize(Vector3.Cross(fwd, up));
        for (var k = 0; k < step.Drops.Length; k++)
        {
            if (ConstructParts.Find(step.Drops[k]) is not { } def)
            {
                Console.WriteLine($"[tutorial] {step.Id}: no part named \"{step.Drops[k]}\" to drop");
                continue;
            }
            var at = p.Position + fwd * 2f + side * ((k - (step.Drops.Length - 1) * 0.5f) * 1.2f) + up * 0.4f;
            if (ConstructBuild.PlaceNew(Constructs, def, at, p.Yaw) is { } c)
                c.Owner = p.Id;
        }
        var who = step.Speaker.Length > 0 ? step.Speaker : WardenName;
        SandboxHands.Notices.Add((p.Id, step.DropNote.Length > 0 ? step.DropNote : step.Drops.Length == 1 ? $"{who} sets a {step.Drops[0]} down in front of you" : $"{who} sets some parts down in front of you"));
    }

    /// <summary>Server: something the induction counts happened for this person outside an action (a bunk lain in).</summary>
    private void TutorialSee(byte id, TutorialCue cue)
    {
        if (id < MaxPlayers && _tutState[id] == TutorialState.Running && !PlayAreaHolds(id))
            _tutSaw[id] |= 1u << (int)cue;
    }

    /// <summary>
    /// Server: the induction holds the gun and the tools (Karl, 2026-11-08) and hands each over at the step that needs it
    /// (a step's "gives"). Whatever is out that isn't issued yet goes back to bare hands (a part in them stays); the end or a
    /// skip gives everything back.
    /// </summary>
    private void TutorialIssue(Player p, int step, TutorialState state)
    {
        var id = p.Id;
        ref var hand = ref SandboxHands.Hand(id);
        if (state == TutorialState.Running && step < Tutorial.Steps.Length)
        {
            if (Tutorial.Gate(state, step, hand.Tool) != hand.Tool)
                SandboxHands.SetTool(id, ConstructTool.Hands, Constructs, p.Eye, p.Yaw);
            var gives = Tutorial.Steps[step].Gives;
            var who = Tutorial.Steps[step].Speaker.Length > 0 ? Tutorial.Steps[step].Speaker : WardenName;
            for (var t = 0; gives != 0 && t < 32; t++)
                if ((gives & (1u << t)) != 0)
                    SandboxHands.Notices.Add((id, $"{who} issues you the {Tutorial.ToolName((ConstructTool)t)}"));
            if (Tutorial.Steps[step].GivesPack && !SandboxHands.HasPack(id) && !SandboxInfiniteParts)
                SandboxHands.Notices.Add((id, $"{who} issues you a backpack"));
        }
        else if (state is TutorialState.Finished or TutorialState.Skipped && (_tutWasRunning[id] || !SandboxHands.HasPack(id) && !SandboxInfiniteParts))
            SandboxHands.Notices.Add((id, !SandboxHands.HasPack(id) && !SandboxInfiniteParts
                ? $"{WardenName} gives you back your gun and tools, and issues you a backpack"
                : $"{WardenName} gives you back your gun and tools"));
        _tutWasRunning[id] = state == TutorialState.Running;
    }

    private static string WardenName => Tutorial.Speaker.Length > 0 ? Tutorial.Speaker : "The warden";

    /// <summary>Server: the tool a person's request gets while their induction runs (<see cref="Tutorial.Gate"/>), or null with why.</summary>
    private ConstructTool? TutorialToolFor(byte id, ConstructTool asked) =>
        id < MaxPlayers && _tutWho[id] != null ? Tutorial.Gate(_tutState[id], _tutStep[id], asked) : asked;

    /// <summary>This PC's player: the same gate from the place the server last sent.</summary>
    public ConstructTool? TutorialToolLocal(ConstructTool asked) =>
        SandboxOn ? Tutorial.Gate(TutorialStateNow, TutorialStepNow, asked) : asked;

    /// <summary>Server, each Sandbox tick: who starts, and whether each running step's cue happened.</summary>
    private void TutorialTick(double now)
    {
        Tutorial.EnsureLoaded();
        var steps = Tutorial.Steps;
        if (steps.Length == 0)
            return;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers)
                continue;
            var id = p.Id;
            if (_tutWho[id] != p)
            {
                TutorialFresh(p); // (a person not met before: no saved world, or the world not applied yet)
                TutorialSend(id);
            }
            TutorialPack(p);
            var state = _tutState[id];
            if (state == TutorialState.None)
            {
                if (TutorialAutoStart && StartInduction && !_tutDeferred[id] && !OpeningHas(id) && !SandboxInfiniteParts && p.Alive && now - _tutArrivedAt[id] >= TutorialAutoDelay && Phase == MatchPhase.Playing)
                    TutorialGo(id, 0, TutorialState.Running); // (a new person in a survival world)
                continue;
            }
            if (state != TutorialState.Running)
                continue;
            if (_tutStep[id] >= steps.Length)
            {
                TutorialGo(id, steps.Length, TutorialState.Finished);
                continue;
            }
            if (_tutGroup[id] == 0)
                TutorialJoin(id);
            if (!p.Alive || _tutDone[id])
                continue;
            ref var hand = ref SandboxHands.Hand(id);
            if (Tutorial.Gate(state, _tutStep[id], hand.Tool) != hand.Tool)
                SandboxHands.SetTool(id, ConstructTool.Hands, Constructs, p.Eye, p.Yaw); // (a respawn or a saved world's tool: the gun stays with the warden)
            if (PlayAreaHolds(id))
                continue; // (out of the step's area: it waits, GameTutorialBounds.cs)
            if (TutorialStepDone(p, steps[_tutStep[id]], now) && now - _tutStepAt[id] >= TutorialMinStep)
            {
                _tutDone[id] = true; // (the group moves on below, together)
                _tutDoneAt[id] = now;
                _tutGroupDirty[id] = true;
            }
        }
        // Each group once, from its lowest id: on when everyone alive has done the step (or the warden has waited long enough).
        _tutPass++;
        for (var id = 0; id < MaxPlayers; id++)
            if (_tutState[id] == TutorialState.Running && _tutGroup[id] != 0 && _tutPassOf[id] != _tutPass && TutorialInGroup(id, out _))
                TutorialGroupTick(id, now, steps.Length);
    }

    /// <summary>Server: is this slot a person here now (the same Player the state was kept for)?</summary>
    private bool TutorialInGroup(int id, out Player p) =>
        Players.TryGetValue((byte)id, out p!) && p.Bot == null && _tutWho[id] == p;

    private bool TutorialMember(int id, int group, byte step) =>
        TutorialInGroup(id, out _) && _tutState[id] == TutorialState.Running && _tutGroup[id] == group && _tutStep[id] == step;

    /// <summary>Server: a running person with no group joins the group on their step (co-op), or starts one of their own.</summary>
    private void TutorialJoin(byte id)
    {
        for (var j = 0; j < MaxPlayers; j++)
            if (j != id && _tutGroup[j] != 0 && TutorialMember(j, _tutGroup[j], _tutStep[id]))
            {
                _tutGroup[id] = _tutGroup[j];
                Log($"[tutorial] {(Players.TryGetValue(id, out var p) ? p.Name : id.ToString())} joins the induction at step {_tutStep[id] + 1} with {(Players.TryGetValue((byte)j, out var q) ? q.Name : j.ToString())}");
                return;
            }
        _tutGroup[id] = ++_tutNextGroup;
    }

    /// <summary>Server: one group's step, from its first member (<paramref name="first"/>): moves everyone on together, and tells them who is still to do it.</summary>
    private void TutorialGroupTick(int first, double now, int stepCount)
    {
        var group = _tutGroup[first];
        var step = _tutStep[first];
        int members = 0, done = 0, waiting = 0;
        var firstDone = double.MaxValue;
        var dirty = false;
        for (var j = first; j < MaxPlayers; j++)
        {
            if (!TutorialMember(j, group, step))
                continue;
            _tutPassOf[j] = _tutPass;
            members++;
            dirty |= _tutGroupDirty[j];
            if (_tutDone[j])
            {
                done++;
                firstDone = Math.Min(firstDone, _tutDoneAt[j]);
            }
            else if (Players[(byte)j].Alive)
                waiting++;
        }
        if (done > 0 && (waiting == 0 || now - firstDone >= TutorialGroupWait))
        {
            var next = step + 1;
            for (var j = first; j < MaxPlayers; j++)
            {
                if (!TutorialMember(j, group, step))
                    continue;
                var p = Players[(byte)j];
                if (!_tutDone[j] && members > 1)
                    SandboxHands.Notices.Add(((byte)j, $"{WardenName} moves on without waiting for you"));
                if (step < Tutorial.Steps.Length)
                    PaceStep(p, PaceInduction, Tutorial.Steps[step].Id, now - _tutStepAt[j], next >= stepCount); // (GamePace.cs: how long the step took them)
                TutorialGo((byte)j, next, next >= stepCount ? TutorialState.Finished : TutorialState.Running, keepGroup: true);
                if (next >= stepCount)
                    Console.WriteLine($"[tutorial] {p.Name} finished the induction");
            }
            return;
        }
        if (!dirty && !TutorialGroupResized(first, group, step, members))
            return;
        for (var j = first; j < MaxPlayers; j++)
            if (TutorialMember(j, group, step))
            {
                _tutGroupDirty[j] = false;
                _tutGroupSent[j] = (byte)Math.Min(members - 1, 255);
                if (members > 1 || _tutGroupSent[j] != 0)
                    TutorialSendGroup((byte)j, group, step, members - 1, done - (_tutDone[j] ? 1 : 0));
            }
    }

    private bool TutorialGroupResized(int first, int group, byte step, int members)
    {
        for (var j = first; j < MaxPlayers; j++)
            if (TutorialMember(j, group, step) && _tutGroupSent[j] != Math.Min(members - 1, 255))
                return true;
        return false;
    }

    /// <summary>Server: what a person's group is doing (<see cref="TutorialOp.Group"/>), when it changes. A short list of who they wait for (allocates: only on a change).</summary>
    private void TutorialSendGroup(byte id, int group, byte step, int others, int othersDone)
    {
        var names = "";
        var listed = 0;
        for (var j = 0; j < MaxPlayers; j++)
        {
            if (j == id || !TutorialMember(j, group, step) || _tutDone[j] || !Players[(byte)j].Alive)
                continue;
            if (listed++ < 3)
                names = names.Length == 0 ? Players[(byte)j].Name : $"{names}, {Players[(byte)j].Name}";
        }
        if (listed > 3)
            names += $" and {listed - 3} more";
        var meDone = _tutDone[id];
        if (id == LocalId)
        {
            TutorialGroupHeard(meDone, others, othersDone, names);
            return;
        }
        if (Net.IsOffline || !Players.TryGetValue(id, out var p) || GuidOf(p) is not { } g)
            return;
        var w = _tutW ??= new PacketWriter(320);
        w.Begin(Msg.Tutorial).U8((byte)TutorialOp.Group).U8(meDone ? (byte)1 : (byte)0).U8((byte)Math.Min(others, 255)).U8((byte)Math.Min(othersDone, 255)).Str(names);
        Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
    }

    private void TutorialGroupHeard(bool meDone, int others, int othersDone, string waiting)
    {
        TutorialGroupDone = meDone;
        TutorialGroupOthers = others;
        TutorialGroupOthersDone = Math.Min(othersDone, others);
        TutorialGroupWaiting = waiting;
    }

    /// <summary>
    /// Server, each tick: story mode starts without the backpack (Karl, 2026-11-08). It is held back through the opening and
    /// the induction until the step that gives it ("gives": ["backpack"]), and is theirs from then on, and after a finish or
    /// a skip. Creative has none to give. A pack that holds something is never taken (a replay, an old save).
    /// </summary>
    private void TutorialPack(Player p)
    {
        var id = p.Id;
        var state = _tutState[id];
        var held = !SandboxInfiniteParts && (state == TutorialState.Running
            ? _tutStep[id] < Tutorial.Steps.Length && !Tutorial.Steps[_tutStep[id]].PackIssued
            : state == TutorialState.None && (TutorialAutoStart && StartInduction || _tutDeferred[id] || OpeningHas(id)));
        if (SandboxHands.IssuePack(id, !held))
            Console.WriteLine($"[tutorial] {p.Name}: backpack {(held ? "held back" : "issued")}");
    }

    /// <summary>Server: whether the step's cue has happened for this person (the movement cues add up as they go).</summary>
    private bool TutorialStepDone(Player p, TutorialStep step, double now)
    {
        var id = p.Id;
        var buttons = p.LastInput.Buttons;
        ref var hand = ref SandboxHands.Hand(id);
        switch (step.Cue)
        {
            case TutorialCue.Wait:
                return now - _tutStepAt[id] >= step.Seconds;
            case TutorialCue.Look:
            {
                var d = MathF.Abs(p.Yaw - _tutLastYaw[id]) % MathF.Tau;
                _tutLastYaw[id] = p.Yaw;
                _tutAmount[id] += MathF.Min(d, MathF.Tau - d); // (the short way round)
                return _tutAmount[id] >= step.Amount;
            }
            case TutorialCue.Move:
            {
                var d = p.Position - _tutLastPos[id];
                _tutLastPos[id] = p.Position;
                var flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
                if ((buttons & (Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right)) != 0 && flat < 1)
                    _tutAmount[id] += flat; // (walking: a teleport or a ride doesn't count)
                return _tutAmount[id] >= step.Amount;
            }
            case TutorialCue.Sprint:
                if ((buttons & Buttons.Sprint) != 0 && (buttons & (Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right)) != 0)
                    _tutAmount[id] += World.TickDt;
                return _tutAmount[id] >= step.Amount;
            case TutorialCue.Jump:
                return (buttons & Buttons.Jump) != 0 || !p.Sim.Grounded && p.Sim.Velocity.Y > 2.5f; // (a press between ticks still leaves them rising)
            case TutorialCue.Crouch:
                return (buttons & (Buttons.Crouch | Buttons.Prone)) != 0 || p.Sim.Stance != Stance.Stand;
            case TutorialCue.Hold:
                return hand.Held != ConstructParts.None;
            case TutorialCue.Tool:
                return hand.Tool == step.Tool;
            case TutorialCue.Ride:
                return QuarryRideStep(p, step.Spot); // (the crew bus: GameQuarry.cs)
            default:
                return (_tutSaw[id] & (1u << (int)step.Cue)) != 0;
        }
    }

    /// <summary>
    /// Server: an action the server carried out for this person (<paramref name="r"/> reads it from its kind): the cues it
    /// counts for. Only while their induction runs.
    /// </summary>
    private void TutorialSawAct(Player p, ref PacketReader r)
    {
        if (p.Id >= MaxPlayers || _tutState[p.Id] != TutorialState.Running || !r.Has(1) || !PlayAreaLets(p))
            return; // (done out of the step's area: it doesn't count, GameTutorialBounds.cs)
        var saw = 0u;
        switch ((CActKind)r.U8())
        {
            case CActKind.Attach:
            case CActKind.PlaceNew:
                saw = Bit(TutorialCue.Place);
                break;
            case CActKind.Join:
            case CActKind.FreeformWeld:
            case CActKind.SocketWeld:
            case CActKind.PointWeld:
                saw = Bit(TutorialCue.Weld);
                break;
            case CActKind.Sculpt:
                saw = Bit(TutorialCue.Drill);
                break;
            case CActKind.Grab:
                saw = Bit(TutorialCue.Carry);
                break;
            case CActKind.Stow:
                saw = Bit(TutorialCue.Stow);
                break;
            case CActKind.Draw:
                saw = Bit(TutorialCue.Draw);
                break;
            case CActKind.Route when r.Has(3):
            {
                r.U16();
                saw = Bit(TutorialCue.Link) | ((RouteType)(r.U8() & 15) == RouteType.Power ? Bit(TutorialCue.Power) : 0);
                break;
            }
            case CActKind.WorldRoute when r.Has(1):
                saw = Bit(TutorialCue.Link) | ((RouteType)(r.U8() & 15) == RouteType.Power ? Bit(TutorialCue.Power) : 0);
                break;
            case CActKind.Configure when r.Has(5):
            {
                var body = r.U16();
                r.U8();
                saw = Bit(TutorialCue.Configure) | (IsAssemblerPart(body, r.U16()) ? Bit(TutorialCue.Assembler) : 0);
                break;
            }
            case CActKind.Use when r.Has(4):
            case CActKind.Bin when r.Has(4):
            {
                var body = r.U16();
                saw = IsAssemblerPart(body, r.U16()) ? Bit(TutorialCue.Assembler) : 0;
                break;
            }
        }
        _tutSaw[p.Id] |= saw;

        static uint Bit(TutorialCue c) => 1u << (int)c;
    }

    private bool IsAssemblerPart(ushort body, ushort part) => Constructs.Get(body) is { } c && part < c.PartCount && c.DefOf(part).IsAssembler;

    /// <summary>Server: what a person asked of their induction (the host's own come straight here).</summary>
    private void TutorialHear(byte id, TutorialOp op, byte arg)
    {
        if (id >= MaxPlayers || !SandboxOn)
            return;
        Tutorial.EnsureLoaded();
        var steps = Tutorial.Steps;
        if (steps.Length == 0)
            return;
        switch (op)
        {
            case TutorialOp.Cue:
                if (_tutState[id] == TutorialState.Running && _tutStep[id] < steps.Length && arg < (byte)TutorialCue.COUNT
                    && Tutorial.ClientCue((TutorialCue)arg) && steps[_tutStep[id]].Cue == (TutorialCue)arg)
                    _tutSaw[id] |= 1u << arg;
                break;
            case TutorialOp.Skip:
                if (_tutState[id] is TutorialState.Running or TutorialState.None)
                {
                    _tutDeferred[id] = false;
                    TutorialGo(id, _tutStep[id], TutorialState.Skipped);
                }
                break;
            case TutorialOp.Replay:
                TutorialGo(id, 0, TutorialState.Running);
                break;
            case TutorialOp.Resume:
                if (_tutState[id] == TutorialState.Skipped)
                    TutorialGo(id, Math.Min(_tutStep[id], steps.Length - 1), TutorialState.Running);
                break;
        }
    }

    /// <summary>Server: a person's place, to them (the host's own: straight into this PC's view).</summary>
    private void TutorialSend(byte id)
    {
        if (id == LocalId)
        {
            TutorialHeard(_tutStep[id], _tutState[id]);
            return;
        }
        if (Net.IsOffline || !Players.TryGetValue(id, out var p) || p.Bot != null || GuidOf(p) is not { } g)
            return;
        var w = _tutW ??= new PacketWriter(320);
        w.Begin(Msg.Tutorial).U8((byte)TutorialOp.State).U8(_tutStep[id]).U8((byte)_tutState[id]);
        Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
    }

    private void TutorialHeard(byte step, TutorialState state)
    {
        if (step == TutorialStepNow && state == TutorialStateNow)
            return;
        TutorialStepNow = step;
        TutorialStateNow = state > TutorialState.Skipped ? TutorialState.None : state;
        TutorialChangedAt = Brutal.GlfwApi.Glfw.Time;
        _tutCueSent = false;
        TutorialGroupDone = false; // (a new step: the group's word on it follows)
        TutorialGroupOthersDone = 0;
        TutorialGroupWaiting = "";
        if (state != TutorialState.Running)
            TutorialGroupOthers = 0;
    }

    /// <summary>A <see cref="Msg.Tutorial"/> (after its id): the server hears a client's op, a client its place.</summary>
    private void OnTutorialPacket(RakNetGUID sender, ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        var op = (TutorialOp)r.U8();
        if (IsServer)
        {
            if (op != TutorialOp.State && _clients.TryGetValue(sender.G, out var cl))
                TutorialHear(cl.Id, op, r.Has(1) ? r.U8() : (byte)0);
        }
        else if (op == TutorialOp.State && r.Has(2))
        {
            var step = r.U8();
            TutorialHeard(step, (TutorialState)r.U8());
        }
        else if (op == TutorialOp.Group && r.Has(3))
        {
            var done = r.U8() != 0;
            var others = r.U8();
            var othersDone = r.U8();
            TutorialGroupHeard(done, others, othersDone, r.Text(120));
        }
    }

    /// <summary>This PC's player asks: skip, replay or resume the induction.</summary>
    public void TutorialAsk(TutorialOp op, byte arg = 0)
    {
        if (op is TutorialOp.State or TutorialOp.Group)
            return;
        if (IsServer)
        {
            TutorialHear(LocalId, op, arg);
            return;
        }
        var w = _tutW ??= new PacketWriter(320);
        w.Begin(Msg.Tutorial).U8((byte)op).U8(arg);
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>This PC, each frame in Sandbox: the windows only it sees, reported once for the step that asks for them.</summary>
    public void TutorialClientFrame(bool inventoryOpen, bool toolWheelOpen)
    {
        if (_tutCueSent || TutorialCurrent is not { } step || !Tutorial.ClientCue(step.Cue))
            return;
        if (step.Cue == TutorialCue.Inventory && inventoryOpen || step.Cue == TutorialCue.ToolWheel && toolWheelOpen)
        {
            _tutCueSent = true;
            TutorialAsk(TutorialOp.Cue, (byte)step.Cue);
        }
    }
}
