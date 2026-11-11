using System.Numerics;
using System.Text.Json;
using Brutal.RakNetApi;


namespace CallOfSlop;

/// <summary>
/// The first Ministry duty (docs/story/DUTIES.md; Karl, 2026-11-10): the warden's induction teaches the person, and the
/// machines are taught later, on the job, working for the Ministry of Slop. Once the induction is over (finished or skipped)
/// the Ministry sends the inmate to the <b>Ministry Quarry</b> a few hundred metres out: a crew bus collects them at the stop
/// outside the colony gate, and at the quarry Foreman Grubb has them power the works (a power cable), dig a quota into its
/// Materials Bin and set its Assembler's recipe, then the bus takes them home. The steps are Story/Duties/Quarry.json, judged
/// here on the host ten times a second; the Orbital Depot (GameDepot.cs) waits until this duty is done.
/// <para>The crew bus is a STAND-IN (Source/StandIns): a picture on a scripted path, no construct. Riding it is a short
/// scene: the player walks up to it once it has stopped, the camera follows it away (Program.Quarry.cs), and when the bus at
/// the other stop has come in they are set down beside it. In co-op each person has their own bus and their own run; the
/// quarry is shared.</para>
/// </summary>
public sealed partial class Game
{
    public const byte QuarryNone = 0, QuarryReport = 1, QuarryFirstStep = 2, QuarryDone = 255;
    public const float QuarryBoardReach = 6f; // m from the bus's stop to board
    public const float QuarryAtWorks = 90f; // m from the quarry's stop: already at the quarry when the duty comes
    public const double QuarryRideSeconds = 10; // s from boarding to being set down
    public const double QuarryRideAway = 5, QuarryRideBlack = 1.5; // s of the scene: the bus leaving, then a moment of black

    private struct QuarryRun
    {
        public byte State;
        public int Bus; // the bus waiting at the stop (a stand-in craft's id, 0: none called)
        public uint RideFrom; // the tick they boarded (0: not riding)
        public int RideTo; // the bus coming in at the other stop
        public uint Saw; // what they did this step (bit per QuarryCue)
        public double StepAt;
        public QuarrySpotKind RideOff; // the stop the ride under way left from
        public bool Rode; // the induction's ride step (TutorialCue.Ride) has been ridden
    }

    private readonly QuarryRun[] _qr = new QuarryRun[256];
    private readonly Player?[] _qrWho = new Player?[256];
    private ushort _qrWorks; // the Ministry Quarry Works construct (0: none)
    private Vector3 _qrStop; // the quarry's bus stop
    private double _qrNextLook;

    // ---- Every peer: this PC's own run, as the server tells it (Msg.Quarry)
    public byte QuarryLocal { get; private set; }
    public Vector3 QuarryTarget { get; private set; }
    public bool QuarryHasTarget { get; private set; }
    public double QuarryChangedAt { get; private set; }
    /// <summary>This PC's player rides the crew bus: the tick they boarded (0: not riding), and the two buses' ids.</summary>
    public uint QuarryRideFrom { get; private set; }
    public ushort QuarryRideBusA { get; private set; }
    public ushort QuarryRideBusB { get; private set; }

    /// <summary>The step this PC's player is on, or null.</summary>
    public QuarryStep? QuarryStepNow => QuarryLocal is >= QuarryFirstStep and < QuarryDone && QuarryLocal - QuarryFirstStep < QuarryScript.Current.Steps.Length
        ? QuarryScript.Current.Steps[QuarryLocal - QuarryFirstStep] : null;

    /// <summary>This PC's player is in the bus scene (the camera and the input are the scene's).</summary>
    public bool QuarryRiding => QuarryRideFrom != 0 && SandboxOn && (MoverRenderTick - QuarryRideFrom) < QuarryRideSeconds * World.TickRate + 2;

    /// <summary>Both sides, a new match (from <see cref="OpeningReset"/>): no quarry, no runs.</summary>
    private void QuarryReset()
    {
        Array.Clear(_qr);
        Array.Clear(_qrWho);
        _qrWorks = 0;
        QuarryLocal = QuarryNone;
        QuarryHasTarget = false;
        QuarryRideFrom = 0;
    }

    // ---- Building it ------------------------------------------------------------------------------------------------

    /// <summary>Server, a story world's start or a world from before the quarry (after the colony): the quarry's works.</summary>
    private void QuarryBuild()
    {
        var script = QuarryScript.Current;
        if (script.Problem.Length > 0)
        {
            Log($"[quarry] {script.Problem}: no quarry");
            return;
        }
        if (_opColony == 0 || Constructs.Get(_qrWorks) != null)
            return;
        _qrWorks = SandboxKitPiece(script.Works, _opMiddle, script.From, script.To, level: 1.2f) is { } works ? works.Id : (ushort)0;
        if (_qrWorks == 0)
        {
            Log($"[quarry] no level ground {script.From:0} to {script.To:0} m out for the {script.Works}: no quarry");
            return;
        }
        QuarrySpots();
        Log($"[quarry] the {script.Works} is construct {_qrWorks}, {Vector3.Distance(_opMiddle, _qrStop):0} m from the colony");
    }

    /// <summary>The quarry's stop: beside the works, on the colony's side.</summary>
    private void QuarrySpots()
    {
        if (Constructs.Get(_qrWorks) is not { } w)
            return;
        var mid = w.WorldCom;
        var toColony = StandInPath.Along(_opMiddle - mid, mid);
        _qrStop = StandInPath.Ground(mid + toColony * 14f + StandInPath.Up(mid) * 30f);
    }

    /// <summary>The colony's stop: beside the gate, a little to the side so the bus clears the fence.</summary>
    private Vector3 QuarryColonyStop()
    {
        var up = StandInPath.Up(_opGate);
        var outward = StandInPath.Along(_opGate - _opMiddle, _opGate);
        return StandInPath.Ground(_opGate + outward * 8f + up * 30f);
    }

    private Construct? QuarryWorks => Constructs.Get(_qrWorks);

    /// <summary>A spot's place now (null: there isn't one).</summary>
    private Vector3? QuarrySpot(QuarrySpotKind spot)
    {
        switch (spot)
        {
            case QuarrySpotKind.Stop:
            case QuarrySpotKind.Colony:
                return _opColony != 0 ? QuarryColonyStop() : null;
            case QuarrySpotKind.Quarry:
                return QuarryWorks != null ? _qrStop : null;
            case QuarrySpotKind.Genset:
            case QuarrySpotKind.Bin:
            case QuarrySpotKind.Assembler:
            case QuarrySpotKind.Wall:
                if (QuarryWorks is not { } w)
                    return null;
                for (var k = 0; k < w.PartCount; k++)
                {
                    var d = w.DefOf(k);
                    if (spot == QuarrySpotKind.Wall)
                    {
                        if (d.Name == "Mine Face")
                            return w.SegToWorld(w.Parts[k].Seg, w.Parts[k].Pos + Vector3.Transform(new Vector3(0, 1.4f, -1.2f), Rot24.Quat(w.Parts[k].Rot))); // (in front of the face, chest high)
                        continue;
                    }
                    if (spot == QuarrySpotKind.Assembler ? d.IsAssembler : spot == QuarrySpotKind.Bin ? d.IsBin && !d.IsAssembler && !d.IsRefiner : d.Name.Contains("Genset", StringComparison.Ordinal))
                        return w.SegToWorld(w.Parts[k].Seg, w.Parts[k].Pos);
                }
                return w.WorldCom;
        }
        return null;
    }

    // ---- The steps --------------------------------------------------------------------------------------------------

    /// <summary>Server, every tick in Sandbox: who is assigned, which steps are done. Ten looks a second (a ride's end on its tick).</summary>
    private void QuarryTick(double now)
    {
        if (_qrWorks == 0 || Phase != MatchPhase.Playing)
            return;
        var look = now >= _qrNextLook;
        if (look)
            _qrNextLook = now + 0.1;
        var steps = QuarryScript.Current.Steps;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers)
                continue;
            if (_qrWho[p.Id] != p)
            {
                _qrWho[p.Id] = p;
                _qr[p.Id] = default;
            }
            ref var run = ref _qr[p.Id];
            if (run.RideFrom != 0)
            {
                if (ServerTick - run.RideFrom >= QuarryRideSeconds * World.TickRate)
                    QuarryRideEnd(p, ref run, now);
                continue;
            }
            if (!look || !p.Alive)
                continue;
            if (run.State == QuarryNone)
            {
                if (_tutState[p.Id] is TutorialState.Finished or TutorialState.Skipped && !OpeningHas(p.Id) && _dp[p.Id].State == DepotNone)
                {
                    // The induction ends at the mine: there the duty starts at the works, with no report and no bus out.
                    var there = steps.Length > 1 && steps[0].Cue == QuarryCue.Ride && Vector3.Distance(p.Position, _qrStop) < QuarryAtWorks;
                    QuarrySet(p, there ? (byte)(QuarryFirstStep + 1) : QuarryReport, now, there ? null : QuarryScript.Current.Memo.Replace("{name}", p.Name));
                }
                continue;
            }
            if (run.State == QuarryDone || run.State != QuarryReport && run.State - QuarryFirstStep >= steps.Length)
                continue;
            if (run.State == QuarryReport)
            {
                if (QuarrySpot(QuarryScript.Current.ReportSpot) is { } at && Vector3.Distance(p.Position, at) <= 8f)
                    QuarrySet(p, QuarryFirstStep, now, null);
                continue;
            }
            var k = run.State - QuarryFirstStep;
            if (QuarryJudge(p, ref run, steps[k], now))
            {
                if (k + 1 < steps.Length)
                    QuarrySet(p, (byte)(run.State + 1), now, null);
                else
                    QuarryFinish(p);
            }
        }
    }

    /// <summary>Server: is this step done? (A ride step starts the bus scene and is done when they are set down.)</summary>
    private bool QuarryJudge(Player p, ref QuarryRun run, QuarryStep step, double now)
    {
        switch (step.Cue)
        {
            case QuarryCue.Wait:
                return now - run.StepAt >= step.Seconds;
            case QuarryCue.Reach:
                return QuarrySpot(step.Spot) is { } at && Vector3.Distance(p.Position, at) <= step.Amount;
            case QuarryCue.Ride:
                if ((run.Saw & QuarryBit(QuarryCue.Ride)) != 0)
                    return true;
                QuarryBusWait(p, ref run, step.Spot);
                return false;
            default:
                return (run.Saw & QuarryBit(step.Cue)) != 0;
        }
    }

    private static uint QuarryBit(QuarryCue c) => 1u << (int)c;

    /// <summary>Server, a ride step: their bus at the stop (called when the step starts, again if it went), and boarding once it has stopped and they are at it.</summary>
    private void QuarryBusWait(Player p, ref QuarryRun run, QuarrySpotKind from)
    {
        var bus = StandInSlotOf(run.Bus);
        if (bus < 0)
        {
            if (QuarrySpot(from) is not { } stop || QuarrySpot(from == QuarrySpotKind.Colony ? QuarrySpotKind.Quarry : QuarrySpotKind.Colony) is not { } other)
                return;
            var way = StandInPath.Along(other - stop, stop);
            var i = StandInCall(StandInKind.Hauler, stop, way, 12, 600, StandInFor.Duty, p.Id, 0, jump: false);
            run.Bus = i >= 0 ? StandIns[i].Id : 0;
            QuarrySend(p); // (the marker moves to the bus: another craft at the stop puts it alongside)
            return;
        }
        ref var c = ref StandIns[bus];
        if (!c.Landed(ServerTick) || ServerTick >= c.LeavesAt || Vector3.Distance(p.Position, c.Land) > QuarryBoardReach)
            return;
        // Aboard: this bus pulls away, the other stop's comes in as the scene ends, and they are set down beside it.
        if (QuarrySpot(from == QuarrySpotKind.Colony ? QuarrySpotKind.Quarry : QuarrySpotKind.Colony) is not { } to)
            return;
        var wayOn = StandInPath.Along(to - c.Land, to);
        StandInLeave(bus, 0.1);
        var j = StandInCall(StandInKind.Hauler, to, wayOn, QuarryRideSeconds - 1, 6, StandInFor.Duty, p.Id, 1, jump: false);
        run.RideTo = j >= 0 ? StandIns[j].Id : 0;
        run.RideFrom = ServerTick;
        run.RideOff = from;
        SandboxStand(p, teleport: false); // (out of any seat first)
        Log($"[quarry] {p.Name} boards the crew bus");
        QuarrySend(p);
    }

    /// <summary>
    /// Server, the induction's ride step (TutorialCue.Ride, GameTutorial.cs): the same crew bus, from <paramref name="from"/>'s
    /// stop to the other. True once they have been set down (or this world has no quarry to ride to).
    /// </summary>
    private bool QuarryRideStep(Player p, QuarrySpotKind from)
    {
        if (_qrWorks == 0)
            return true;
        if (_qrWho[p.Id] != p)
        {
            _qrWho[p.Id] = p;
            _qr[p.Id] = default;
        }
        ref var run = ref _qr[p.Id];
        if (run.Rode)
        {
            run.Rode = false;
            return true;
        }
        if (run.RideFrom == 0 && p.Alive)
            QuarryBusWait(p, ref run, from);
        return false;
    }

    /// <summary>Server: the induction moved to another step: a bus called for the last one isn't this one's, and the marker may have moved.</summary>
    private void QuarryTutorialStep(Player p)
    {
        if (_qrWorks == 0 || p.Id >= MaxPlayers || _qrWho[p.Id] != p)
            return;
        ref var run = ref _qr[p.Id];
        run.Rode = false;
        if (run.RideFrom == 0)
            run.Bus = 0;
        QuarrySend(p);
    }

    /// <summary>Server: something the duty counts happened for this person outside an action (a bunk lain in).</summary>
    private void QuarrySee(byte id, QuarryCue cue)
    {
        if (id < MaxPlayers && _qr[id].State is >= QuarryFirstStep and < QuarryDone)
            _qr[id].Saw |= QuarryBit(cue);
    }

    /// <summary>A Bunk in the colony for a marker: a free one when there is one.</summary>
    private Vector3? QuarryBunk()
    {
        if (Constructs.Get(_opColony) is not { } c)
            return null;
        Vector3? any = null;
        for (var k = 0; k < c.PartCount; k++)
        {
            ref var part = ref c.Parts[k];
            if (!part.D.Bunk || part.Broken)
                continue;
            any = c.SegToWorld(part.Seg, part.Pos);
            if (part.SitterPlus1 == 0)
                break;
        }
        return any;
    }

    private int StandInSlotOf(int id)
    {
        if (id == 0)
            return -1;
        for (var k = 0; k < StandInCount; k++)
            if (StandIns[k].Id == id && ServerTick < StandIns[k].EndsAt)
                return k;
        return -1;
    }

    /// <summary>Server: the scene is over: set down beside the bus at the other stop, and the ride step is done.</summary>
    private void QuarryRideEnd(Player p, ref QuarryRun run, double now)
    {
        var to = run.RideOff == QuarrySpotKind.Colony ? QuarrySpot(QuarrySpotKind.Quarry) : QuarrySpot(QuarrySpotKind.Colony);
        run.RideFrom = 0;
        run.Bus = 0;
        var bus = StandInSlotOf(run.RideTo);
        if (bus >= 0)
            to = StandIns[bus].Land; // (alongside, when another craft was at the stop)
        if (to is { } at && p.Alive)
        {
            var side = bus >= 0 ? Vector3.Cross(StandInPath.Up(at), StandIns[bus].Way) : Vector3.UnitX;
            var feet = SbScatter(at + side * 3.5f + StandInPath.Up(at) * 0.6f, 0.6f);
            p.Teleport(feet);
            p.Yaw = MathF.Atan2(side.X, -side.Z); // (facing away from the bus; our yaw turns -Z towards +X)
        }
        if (_tutState[p.Id] == TutorialState.Running)
            run.Rode = true; // (the induction's ride: GameTutorial.cs)
        else
            run.Saw |= QuarryBit(QuarryCue.Ride);
        Log($"[quarry] {p.Name} set down by the crew bus");
        QuarrySend(p);
    }

    /// <summary>
    /// Server: an action the server carried out for this person (<paramref name="r"/> reads it from its kind): what the quarry's
    /// steps count, on the quarry's works only.
    /// </summary>
    private void QuarrySawAct(Player p, ref PacketReader r)
    {
        if (p.Id >= MaxPlayers || _qrWorks == 0 || _qr[p.Id].State is < QuarryFirstStep or QuarryDone || !r.Has(1))
            return;
        var saw = 0u;
        switch ((CActKind)r.U8())
        {
            case CActKind.Route when r.Has(3):
            {
                var body = r.U16();
                if (body == _qrWorks && (RouteType)(r.U8() & 15) == RouteType.Power)
                    saw = QuarryBit(QuarryCue.Power);
                break;
            }
            case CActKind.WorldRoute when r.Has(1):
                if ((RouteType)(r.U8() & 15) == RouteType.Power && QuarrySpot(QuarrySpotKind.Genset) is { } g && Vector3.Distance(p.Position, g) < 30)
                    saw = QuarryBit(QuarryCue.Power); // (a cable from the works to something beside them)
                break;
            case CActKind.Configure when r.Has(5):
            {
                var body = r.U16();
                r.U8();
                if (body == _qrWorks && IsAssemblerPart(body, r.U16()))
                    saw = QuarryBit(QuarryCue.Recipe);
                break;
            }
            case CActKind.Use when r.Has(4):
            case CActKind.Bin when r.Has(4):
                if (r.U16() == _qrWorks)
                    saw = QuarryBit(QuarryCue.Feed);
                break;
        }
        _qr[p.Id].Saw |= saw;
    }

    /// <summary>Server: on to a state (the report or a step), with its line, and the client told.</summary>
    private void QuarrySet(Player p, byte state, double now, string? memo)
    {
        ref var run = ref _qr[p.Id];
        run.State = state;
        run.StepAt = now;
        run.Saw = 0;
        run.Bus = 0;
        if (memo != null)
            OpeningSay(p, null, "[MINISTRY MEMO]", memo, now);
        if (state >= QuarryFirstStep && state != QuarryDone && state - QuarryFirstStep < QuarryScript.Current.Steps.Length)
        {
            var step = QuarryScript.Current.Steps[state - QuarryFirstStep];
            if (step.Say.Length > 0)
                OpeningSay(p, null, QuarryScript.Current.Speaker, step.Say, now);
            Log($"[quarry] {p.Name}: step {state - QuarryFirstStep + 1}, {step.Id}");
        }
        QuarrySend(p);
    }

    /// <summary>Server: the Orbital Depot (GameDepot.cs) comes after this duty: done or skipped, or there is no quarry in this world.</summary>
    private bool QuarryLetsDepot(byte id) => _qrWorks == 0 || QuarryScript.Current.Problem.Length > 0 || _qrWho[id] != null && _qr[id].State == QuarryDone;

    private void QuarryFinish(Player p)
    {
        _qr[p.Id].State = QuarryDone;
        QuarrySend(p);
        Log($"[quarry] {p.Name}: the first duty is done");
    }

    /// <summary>Server: a person asks to skip the duty (the Esc menu): it counts as done, so the next one can come.</summary>
    private void QuarrySkip(Player p)
    {
        ref var run = ref _qr[p.Id];
        if (run.State is QuarryNone or QuarryDone || run.RideFrom != 0)
            return;
        Log($"[quarry] {p.Name} skips the duty");
        QuarryFinish(p);
    }

    /// <summary>This PC's player: skip the duty (the host's own goes straight in).</summary>
    public void QuarryAskSkip()
    {
        if (IsServer)
        {
            if (Local is { } me)
                QuarrySkip(me);
            return;
        }
        SandboxSend(CActKind.QuarrySkip);
    }

    // ---- The wire -----------------------------------------------------------------------------------------------------

    private Vector3? QuarryTargetOf(Player p)
    {
        ref var run = ref _qr[p.Id];
        var script = QuarryScript.Current;
        if (_tutState[p.Id] == TutorialState.Running && _tutStep[p.Id] < Tutorial.Steps.Length)
        {
            var ts = Tutorial.Steps[_tutStep[p.Id]]; // (the induction's own marker: a ride's bus, or the step's spot)
            if (ts.Cue == TutorialCue.Ride && StandInSlotOf(run.Bus) is var tb and >= 0)
                return StandIns[tb].Land;
            return ts.Spot == QuarrySpotKind.None ? null : QuarrySpot(ts.Spot);
        }
        if (run.State == QuarryReport)
            return QuarrySpot(script.ReportSpot);
        if (run.State is < QuarryFirstStep or QuarryDone || run.State - QuarryFirstStep >= script.Steps.Length)
            return null;
        var step = script.Steps[run.State - QuarryFirstStep];
        if (step.Cue == QuarryCue.Ride && StandInSlotOf(run.Bus) is var bus and >= 0)
            return StandIns[bus].Land; // (the bus itself, once it is called)
        if (step.Cue == QuarryCue.Bunk)
            return QuarryBunk();
        return step.Spot == QuarrySpotKind.None ? null : QuarrySpot(step.Spot);
    }

    /// <summary>Server: their state, to them (<see cref="Msg.Quarry"/>: u8 state, u8 has a target, vec the target, u32 ride tick, u16 bus, u16 bus). The host's own goes straight in.</summary>
    private void QuarrySend(Player p)
    {
        var target = QuarryTargetOf(p);
        ref var run = ref _qr[p.Id];
        var (a, b) = ((ushort)run.Bus, (ushort)run.RideTo);
        if (run.RideFrom == 0)
            (a, b) = (0, 0);
        if (p.Id == LocalId)
        {
            QuarryHear(run.State, target, run.RideFrom, a, b);
            return;
        }
        if (GuidOf(p) is not { } guid)
            return;
        var w = SbW;
        w.Begin(Msg.Quarry).U8(run.State).U8(target != null ? (byte)1 : (byte)0).Vec(target ?? default).U32(run.RideFrom).U16(a).U16(b);
        Net.SendTo(guid, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.Quarry"/> (after its id).</summary>
    private void ReadQuarry(ref PacketReader r)
    {
        var state = r.U8();
        var has = r.U8() != 0;
        var at = r.Vec();
        var from = r.U32();
        var a = r.U16();
        var b = r.U16();
        QuarryHear(state, has && float.IsFinite(at.X + at.Y + at.Z) ? at : null, from, a, b);
    }

    private void QuarryHear(byte state, Vector3? target, uint rideFrom, ushort a, ushort b)
    {
        if (state != QuarryLocal)
            QuarryChangedAt = Brutal.GlfwApi.Glfw.Time;
        QuarryLocal = state;
        QuarryHasTarget = target != null;
        QuarryTarget = target ?? default;
        QuarryRideFrom = rideFrom;
        (QuarryRideBusA, QuarryRideBusB) = (a, b);
    }

    // ---- The save -----------------------------------------------------------------------------------------------------

    /// <summary>Server: a person arrives in a saved world: where they were on the duty (a ride under way starts its step again).</summary>
    private void QuarryArrive(Player p, WorldPlayer rec)
    {
        _qrWho[p.Id] = p;
        _qr[p.Id] = new QuarryRun { State = rec.QuarryState, StepAt = SandboxNow };
        QuarrySend(p);
    }

    /// <summary>Server: what goes in a person's record.</summary>
    private void QuarryCatch(Player p, WorldPlayer rec)
    {
        if (_qrWho[p.Id] == p)
            rec.QuarryState = _qr[p.Id].State;
    }

    /// <summary>A saved world's quarry section (WorldSaveWriter.QuarryTag): each person's state, onto their record.</summary>
    private static void WorldReadQuarry(SaveReader r, WorldSession ws)
    {
        var n = r.Count(17);
        for (var k = 0; k < n; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var state = r.U8();
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam)
                {
                    rec.QuarryState = state;
                    break;
                }
        }
    }

    /// <summary>Server, loading a world (from <see cref="OpeningLoad"/>): its quarry, or a new one for a world from before it.</summary>
    private void QuarryLoad(ushort works)
    {
        _qrWorks = Constructs.Get(works) != null ? works : (ushort)0;
        if (_qrWorks != 0)
            QuarrySpots();
        else if (_opColony != 0)
            QuarryBuild();
    }
}

/// <summary>What completes a duty step (Story/Duties/Quarry.json).</summary>
public enum QuarryCue : byte
{
    Wait, // the step's seconds pass
    Reach, // within amount m of the spot
    Ride, // board the crew bus at the spot's stop and ride it to the other
    Power, // a power cable laid at the quarry's works
    Feed, // something tipped into (or used on) the works: its Materials Bin
    Recipe, // the works' Assembler's recipe set
    Bunk, // lay in a Bunk (the day's end; the marker is the colony's nearest free one)
}

/// <summary>A place a duty step points at.</summary>
public enum QuarrySpotKind : byte
{
    None,
    Stop, // the colony's bus stop (the report)
    Colony, // a ride from the colony's stop
    Quarry, // a ride from the quarry's stop
    Genset,
    Bin,
    Assembler,
    Wall, // the mine face behind the works (the induction's supports and lamp)
}

public sealed class QuarryStep
{
    public string Id = "", Title = "", Text = "", Say = "", Hint = "";
    public QuarryCue Cue;
    public QuarrySpotKind Spot;
    public float Seconds = 6, Amount = 6, HintAfter = 40;
}

/// <summary>Story/Duties/Quarry.json (format call-of-slop-duty/1): the first Ministry duty. A bad file is refused with a reason (Problem) and there is no quarry.</summary>
public sealed class QuarryScript
{
    public const string Format = "call-of-slop-duty/1";
    public const int MaxSteps = 32;

    public string Speaker = "Foreman Grubb", Memo = "", ReportTitle = "", ReportText = "", Problem = "", Works = "Ministry Quarry Works";
    public QuarrySpotKind ReportSpot = QuarrySpotKind.Stop;
    public float From = 180, To = 320;
    public QuarryStep[] Steps = [];

    private static QuarryScript? _current;
    public static QuarryScript Current => _current ??= Load(Path.Combine(AppFolders.Data, "Story", "Duties", "Quarry.json"));

    public static QuarryScript Load(string path)
    {
        try
        {
            var s = Parse(File.ReadAllText(path));
            Console.WriteLine($"[quarry] {s.Steps.Length} steps");
            return s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            Console.WriteLine($"[quarry] refused: {ex.Message}");
            return new QuarryScript { Problem = $"Quarry.json: {ex.Message}" };
        }
    }

    public static QuarryScript Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        var s = new QuarryScript();
        string Str(JsonElement e, string key, string where, bool need = false)
        {
            if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return Ascii(v.GetString() ?? "", $"{where}.{key}");
            return need ? throw new FormatException($"{where} has no \"{key}\"") : "";
        }
        static QuarrySpotKind Spot(string name, string where) => name.Length == 0 ? QuarrySpotKind.None
            : Enum.TryParse<QuarrySpotKind>(name, true, out var k) && !int.TryParse(name, out _) && k != QuarrySpotKind.None ? k
            : throw new FormatException($"{where}: no such spot \"{name}\" (stop, colony, quarry, genset, bin, assembler)");
        s.Speaker = Str(root, "speaker", "file") is { Length: > 0 } sp ? sp : s.Speaker;
        s.Memo = Str(root, "memo", "file");
        s.Works = Str(root, "works", "file") is { Length: > 0 } wk ? wk : s.Works;
        if (root.TryGetProperty("from", out var fr) && fr.ValueKind == JsonValueKind.Number)
            s.From = fr.GetSingle();
        if (root.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Number)
            s.To = to.GetSingle();
        if (s.From is < 20 or > 2000 || s.To < s.From + 10 || s.To > 3000)
            throw new FormatException("from and to: 20 to 3000 m, to at least 10 m past from");
        if (root.TryGetProperty("report", out var rep))
        {
            s.ReportTitle = Str(rep, "title", "report", true);
            s.ReportText = Str(rep, "text", "report", true);
            s.ReportSpot = Spot(Str(rep, "spot", "report"), "report");
            if (s.ReportSpot == QuarrySpotKind.None)
                s.ReportSpot = QuarrySpotKind.Stop;
        }
        if (!root.TryGetProperty("steps", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("no \"steps\" list");
        var steps = new List<QuarryStep>();
        foreach (var e in list.EnumerateArray())
        {
            var where = $"steps[{steps.Count}]";
            var st = new QuarryStep { Id = Str(e, "id", where, true), Title = Str(e, "title", where, true), Text = Str(e, "text", where, true), Say = Str(e, "say", where), Hint = Str(e, "hint", where) };
            var cue = Str(e, "cue", where, true);
            if (!Enum.TryParse(cue, true, out st.Cue) || int.TryParse(cue, out _))
                throw new FormatException($"{where} ({st.Id}): no such cue \"{cue}\"");
            st.Spot = Spot(Str(e, "spot", where), $"{where} ({st.Id})");
            if (st.Cue == QuarryCue.Ride && st.Spot is not (QuarrySpotKind.Colony or QuarrySpotKind.Quarry))
                throw new FormatException($"{where} ({st.Id}): a ride's spot is colony or quarry (where the bus is boarded)");
            if (st.Cue == QuarryCue.Reach && st.Spot == QuarrySpotKind.None)
                throw new FormatException($"{where} ({st.Id}): a reach needs a spot");
            if (e.TryGetProperty("seconds", out var sec) && sec.ValueKind == JsonValueKind.Number)
                st.Seconds = sec.GetSingle();
            if (e.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number)
                st.Amount = am.GetSingle();
            if (e.TryGetProperty("hintAfter", out var ha) && ha.ValueKind == JsonValueKind.Number)
                st.HintAfter = ha.GetSingle();
            if (st.Seconds is not (> 0 and <= 120) || st.Amount is not (> 0 and <= 1000) || st.HintAfter is not (>= 0 and <= 600))
                throw new FormatException($"{where} ({st.Id}): seconds 0 to 120, amount 0 to 1000, hintAfter 0 to 600");
            steps.Add(st);
            if (steps.Count > MaxSteps)
                throw new FormatException($"more than {MaxSteps} steps");
        }
        if (steps.Count == 0)
            throw new FormatException("no steps");
        s.Steps = [.. steps];
        return s;
    }

    private static string Ascii(string s, string where)
    {
        foreach (var c in s)
            if (c is < ' ' or > '~')
                throw new FormatException($"{where}: player text is ASCII only");
        return s;
    }
}
