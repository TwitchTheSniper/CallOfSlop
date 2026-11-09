using System.Numerics;
using System.Text.Json;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The Orbital Depot (docs/story/DEPOT.md; Karl, 2026-11-08): a story-mode player's first meeting with robot shells, and their
/// first trip off the ground. Once the warden's induction is over, the Ministry assigns them remote labour: they sit in a chair
/// in the <b>Remote Works</b> beside the penal colony and wake in a Robot Shell (GameRobotShell.cs) on the <b>Orbital Depot</b>,
/// a station that hangs <see cref="DepotScript.Height"/> over it. Depot Control talks them through the steps in
/// Story/Depot/Depot.json, judged here on the host: out of the shell bay, a look over the edge, the Driving Module off the stores
/// tower, fitted and linked into their lander, then flown down to the Remote Works' apron. The lander is theirs; the link
/// closes and they wake in their own body beside their first spacecraft. A lost shell or a wrecked lander is made good when they
/// sit down again. Each player has their own run, shell, module and lander, so it works the same in co-op. Server state is a
/// fixed array by player id and the checks run ten times a second: nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    public const byte DepotNone = 0, DepotReport = 1, DepotFirstStep = 2, DepotDone = 255;
    public const int DepotSlots = 4; // shells and landers side by side on the deck

    private struct DepotRun
    {
        public byte State; // DepotNone, DepotReport, DepotFirstStep + the step, DepotDone
        public ushort Lander, Module; // their lander and Driving Module (0: none yet)
        public Vector3 WalkFrom; // where a walk step started
        public double StepAt, SlowSince, ModuleAt;
    }

    private readonly DepotRun[] _dp = new DepotRun[256];
    private readonly Player?[] _dpWho = new Player?[256]; // whose run the slot holds (a new Player object in the slot: a new person)
    private ushort _dpWorks, _dpDepot; // the Remote Works and the Orbital Depot (0: none)
    private double _dpNextLook;

    // ---- Every peer: this PC's own run, as the server tells it (Msg.Depot)
    public byte DepotLocal { get; private set; }
    public Vector3 DepotTarget { get; private set; }
    public bool DepotHasTarget { get; private set; }
    public double DepotChangedAt { get; private set; }

    /// <summary>The step this PC's player is on, or null (not on the depot's steps).</summary>
    public DepotStep? DepotStepNow => DepotLocal is >= DepotFirstStep and < DepotDone && DepotLocal - DepotFirstStep < DepotScript.Current.Steps.Length
        ? DepotScript.Current.Steps[DepotLocal - DepotFirstStep] : null;

    /// <summary>Both sides, a new match (from <see cref="OpeningReset"/>): no depot, no runs.</summary>
    private void DepotReset()
    {
        Array.Clear(_dp);
        Array.Clear(_dpWho);
        _dpWorks = _dpDepot = 0;
        DepotLocal = DepotNone;
        DepotHasTarget = false;
    }

    // ---- Building it ------------------------------------------------------------------------------------------------

    /// <summary>Server, a story world's start or a world from before the depot (after the colony): the Remote Works beside it, the depot over it.</summary>
    private void DepotBuild()
    {
        var script = DepotScript.Current;
        if (script.Problem.Length > 0)
        {
            Log($"[depot] {script.Problem}: no depot");
            return;
        }
        if (_opColony == 0)
            return;
        if (Constructs.Get(_dpWorks) == null)
        {
            _dpWorks = SandboxKitPiece("Remote Works", _opMiddle, 22, 90, level: 1.2f) is { } works ? works.Id : (ushort)0;
            if (_dpWorks == 0)
            {
                Log("[depot] no level ground for the Remote Works: no depot");
                return;
            }
        }
        if (Constructs.Get(_dpDepot) != null || Constructs.Get(_dpWorks) is not { } w || ConstructBuild.FindBlueprint("Orbital Depot") is not { } bp)
            return;
        var apron = w.ToWorld(ApronLocal);
        var over = apron + GravityFrames.UpAt(apron) * script.Height; // (the hangar deck right over the apron)
        if (ConstructBuild.BuildOnGround(Constructs, bp, over, 0, out var why) is not { } depot)
        {
            Log($"[depot] the Orbital Depot doesn't build: {why}");
            return;
        }
        depot.Rot = w.Rot; // (square with the Remote Works below it)
        depot.Pos += over - depot.ToWorld(DepotHangarLocal);
        depot.PrevPos = depot.NetPos = depot.NetFromPos = depot.Pos;
        depot.PrevRot = depot.Rot;
        depot.NetHistReset();
        depot.Frozen = true;
        depot.Rebuild();
        _dpDepot = depot.Id;
        Constructs.Events.Add($"depot: the Remote Works as construct {w.Id}, the Orbital Depot as construct {depot.Id}");
        Log($"[depot] the Remote Works is construct {w.Id}, the Orbital Depot construct {depot.Id}, {script.Height:0} m up");
    }

    // The blueprints' places (tools/constructs/building_parts.py: cells of 0.25 m, the first pad's first cell at the origin; floors at 0.375 m).
    private static readonly Vector3 ApronLocal = new(1.875f, 0.4f, -4.125f); // the Remote Works' apron's middle
    private static readonly Vector3 WorksDoorLocal = new(1.875f, 0.4f, 0.6f);
    private static readonly Vector3 DepotHangarLocal = new(7.875f, 0.4f, 3.875f); // the depot's hangar deck's middle
    private static readonly Vector3 DepotBayLocal = new(1.875f, 0.4f, 9.875f); // inside the shell bay
    private static readonly Vector3 DepotEdgeLocal = new(7.875f, 0.4f, -0.1f); // the hangar deck's front edge
    private static readonly Vector3 DepotStoresLocal = new(14.875f, 4.4f, 10.875f); // on top of the stores tower

    private static Vector3 DepotSlot(int k) => new(k % 2 == 0 ? 3.875f : 11.875f, 0.4f, k < 2 ? 3.875f : 1.4f);

    private Construct? DepotBody => Constructs.Get(_dpDepot);

    /// <summary>A place on the depot or the Remote Works, in the world (null: that one isn't there).</summary>
    private Vector3? DepotAt(Vector3 local) => DepotBody is { } d ? d.ToWorld(local) : null;

    private Vector3? WorksAt(Vector3 local) => Constructs.Get(_dpWorks) is { } w ? w.ToWorld(local) : null;

    // ---- The steps --------------------------------------------------------------------------------------------------

    /// <summary>Server, every tick in Sandbox: who is assigned, which steps are done. Ten looks a second.</summary>
    private void DepotTick(double now)
    {
        if (_dpDepot == 0 || now < _dpNextLook || Phase != MatchPhase.Playing)
            return;
        _dpNextLook = now + 0.1;
        var steps = DepotScript.Current.Steps;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || !p.Alive)
                continue;
            if (_dpWho[p.Id] != p)
            {
                _dpWho[p.Id] = p;
                _dp[p.Id] = default;
            }
            ref var run = ref _dp[p.Id];
            if (run.State == DepotNone)
            {
                if (_tutState[p.Id] is TutorialState.Finished or TutorialState.Skipped && !OpeningHas(p.Id))
                    DepotSet(p, DepotReport, now, DepotScript.Current.Memo.Replace("{name}", p.Name));
                continue;
            }
            if (run.State is DepotReport or DepotDone || run.State - DepotFirstStep >= steps.Length || !_sh[p.Id].On)
                continue;
            var k = run.State - DepotFirstStep;
            if (DepotJudge(p, ref run, steps[k], now))
            {
                if (k + 1 < steps.Length)
                    DepotSet(p, (byte)(run.State + 1), now, null);
                else
                    DepotFinish(p, now);
            }
        }
    }

    /// <summary>Server: is this step done?</summary>
    private bool DepotJudge(Player p, ref DepotRun run, DepotStep step, double now)
    {
        var lander = Constructs.Get(run.Lander);
        if (step.Cue is DepotCue.Fit or DepotCue.Link or DepotCue.Board or DepotCue.Clear or DepotCue.Land && lander == null)
        {
            DepotSay(p, "That lander is scrap. A new one is on the hangar deck: fetch another module.", now);
            DepotBackTo(p, ref run, DepotCue.Hold, now);
            return false;
        }
        ref var h = ref SandboxHands.Hand(p.Id);
        switch (step.Cue)
        {
            case DepotCue.Wait:
                return now - run.StepAt >= step.Seconds;
            case DepotCue.Walk:
                return Vector3.Distance(p.Position, run.WalkFrom) >= step.Amount;
            case DepotCue.Reach:
                return DepotAt(DepotEdgeLocal) is { } edge && Vector3.Distance(p.Position, edge) <= step.Amount;
            case DepotCue.Hold:
                if (lander == null)
                {
                    DepotPutLander(p, ref run); // (the hangar was full: a slot may have come free)
                    return false;
                }
                if (DepotHasModule(lander))
                    return true;
                if (ConstructParts.Get(h.Held) is { } held && held.Name == step.Part)
                    return true;
                if (Constructs.Get(run.Module) == null && now - run.ModuleAt > 5)
                    DepotPutModule(p, ref run, now, step.Part); // (it fell off, or a friend took it)
                return false;
            case DepotCue.Fit:
                return DepotHasModule(lander!);
            case DepotCue.Link:
                if (lander!.Dirty)
                    lander.Rebuild();
                return lander.DriveSeat >= 0;
            case DepotCue.Board:
                return h.Seated && h.SeatBody == lander!.Id && lander.SeatOf(p.Id) == lander.DriveSeat && lander.DriveSeat >= 0;
            case DepotCue.Clear:
                return DepotBody is { } d && Vector3.Dot(d.ToWorld(DepotHangarLocal) - lander!.WorldCom, GravityFrames.UpAt(lander.WorldCom)) >= step.Amount;
            case DepotCue.Land:
            {
                if (WorksAt(ApronLocal) is not { } apron)
                    return false;
                var off = lander!.WorldCom - apron;
                var up = GravityFrames.UpAt(apron);
                var height = Vector3.Dot(off, up);
                var flat = (off - up * height).Length();
                if (flat > step.Amount || height > 8 || lander.Vel.Length() > 1.5f)
                {
                    run.SlowSince = 0;
                    return false;
                }
                if (run.SlowSince <= 0)
                    run.SlowSince = now;
                return now - run.SlowSince >= 1.5;
            }
        }
        return false;
    }

    private static bool DepotHasModule(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Logic == CLogic.DriveModule && !c.Parts[i].Broken)
                return true;
        return false;
    }

    /// <summary>Server: on to a state (a step, or the report), with its line and the client told.</summary>
    private void DepotSet(Player p, byte state, double now, string? memo)
    {
        ref var run = ref _dp[p.Id];
        run.State = state;
        run.StepAt = now;
        run.SlowSince = 0;
        run.WalkFrom = p.Position;
        if (memo != null)
            OpeningSay(p, null, "[MINISTRY MEMO]", memo, now);
        if (state >= DepotFirstStep && state != DepotDone && state - DepotFirstStep < DepotScript.Current.Steps.Length)
        {
            var step = DepotScript.Current.Steps[state - DepotFirstStep];
            if (step.Say.Length > 0)
                DepotSay(p, step.Say, now);
            if (step.Cue == DepotCue.Hold)
                DepotPutModule(p, ref run, now, step.Part);
            DepotPutLander(p, ref run);
            Log($"[depot] {p.Name}: step {state - DepotFirstStep + 1}, {step.Id}");
        }
        DepotSend(p);
    }

    private void DepotSay(Player p, string text, double now) => OpeningSay(p, null, DepotScript.Current.Speaker, text, now);

    /// <summary>Server: the last step is done: the lander is theirs, the link closes and they wake in their body.</summary>
    private void DepotFinish(Player p, double now)
    {
        ref var run = ref _dp[p.Id];
        if (Constructs.Get(run.Lander) is { } lander)
        {
            lander.Owner = p.Id;
            Constructs.StructureDirty.Add(lander.Id);
        }
        run.State = DepotDone;
        DepotSend(p);
        Log($"[depot] {p.Name}: delivered their lander (construct {run.Lander})");
        ShellReturn(p, lost: false, "The link closes. You wake in your own body: your lander waits on the apron outside.");
    }

    /// <summary>Server: back to an earlier step (the lander wrecked or lost): the cue's first step, with a new lander and module.</summary>
    private void DepotBackTo(Player p, ref DepotRun run, DepotCue cue, double now)
    {
        var steps = DepotScript.Current.Steps;
        var k = Array.FindIndex(steps, s => s.Cue == cue);
        if (k < 0)
            return;
        if (Constructs.Get(run.Lander) is { } old)
            Constructs.Remove(old);
        if (Constructs.Get(run.Module) is { } mod)
            Constructs.Remove(mod);
        run.Lander = run.Module = 0;
        DepotSet(p, (byte)(DepotFirstStep + k), now, null);
    }

    /// <summary>Server: their Driving Module on top of the stores tower (a loose part), unless it's already there.</summary>
    private void DepotPutModule(Player p, ref DepotRun run, double now, string part)
    {
        run.ModuleAt = now;
        if (Constructs.Get(run.Module) != null || DepotBody is not { } d || ConstructParts.Find(part) is not { } def)
            return;
        var slot = p.Id % DepotSlots;
        var at = d.ToWorld(DepotStoresLocal + new Vector3(-0.45f * (slot % 2), 0, -0.45f * (slot / 2)));
        if (ConstructBuild.PlaceNew(Constructs, def, at, 0) is { } m)
        {
            m.Owner = p.Id;
            m.Frozen = true; // (it waits on the shelf until someone picks it up)
            m.Rebuild();
            run.Module = m.Id;
        }
    }

    /// <summary>Server: their lander on the hangar deck, unless they have one already.</summary>
    private void DepotPutLander(Player p, ref DepotRun run)
    {
        if (Constructs.Get(run.Lander) != null || DepotBody is not { } d || ConstructBuild.FindBlueprint("Depot Lander") is not { } bp)
            return;
        var at = Vector3.Zero;
        var found = false;
        for (var i = 0; i < DepotSlots && !found; i++)
        {
            at = d.ToWorld(DepotSlot((p.Id + i) % DepotSlots));
            found = true;
            foreach (var b in Constructs.Bodies)
                if (b != d && b.PartCount > 1 && Vector3.DistanceSquared(b.WorldCom, at) < 4)
                {
                    found = false; // (another player's lander stands there)
                    break;
                }
        }
        if (!found)
            return; // (all four slots taken: the Hold step tries again)
        if (ConstructBuild.BuildOnGround(Constructs, bp, at, 0, out var why) is not { } lander)
        {
            Log($"[depot] the Depot Lander doesn't build: {why}");
            return;
        }
        lander.Owner = p.Id;
        run.Lander = lander.Id;
    }

    // ---- The shells' hooks (GameRobotShell.cs) -----------------------------------------------------------------------

    /// <summary>A Remote Works chair: a player on the depot's run gets a shell in the bay if they have none to wake in.</summary>
    partial void ShellWanted(Player p, Construct chair, int seat)
    {
        ref var run = ref _dp[p.Id];
        if (chair.Id != _dpWorks || run.State is DepotNone or DepotDone || DepotBody is not { } d || ConstructParts.Find("Robot Shell") is not { } def)
            return;
        foreach (var b in Constructs.Bodies)
            if (b.Owner == p.Id && ConstructShell.Wakeable(b))
                return; // (the one they left: they go back to it)
        var slot = p.Id % DepotSlots;
        var at = d.ToWorld(DepotBayLocal + new Vector3((slot % 2 - 0.5f) * 1.2f, 0, (slot / 2 - 0.5f) * 1.2f));
        if (ConstructBuild.PlaceNew(Constructs, def, at, 0) is { } shell)
        {
            shell.Owner = p.Id;
            shell.Frozen = true;
            shell.Rebuild();
        }
    }

    partial void ShellWoke(Player p, Construct shell)
    {
        ref var run = ref _dp[p.Id];
        if (run.State == DepotReport && DepotBody is { } d && Vector3.Distance(shell.WorldCom, d.WorldCom) < 80)
            DepotSet(p, DepotFirstStep, SandboxNow, null);
        else if (run.State is >= DepotFirstStep and < DepotDone)
        {
            DepotPutLander(p, ref run); // (a wrecked one is made good)
            DepotSend(p);
        }
    }

    partial void ShellBack(Player p, bool lost)
    {
        ref var run = ref _dp[p.Id];
        if (!lost || run.State is < DepotFirstStep or DepotDone)
        {
            DepotSend(p);
            return;
        }
        var steps = DepotScript.Current.Steps;
        var k = run.State - DepotFirstStep;
        if (k < steps.Length && steps[k].Cue is DepotCue.Board or DepotCue.Clear or DepotCue.Land)
        {
            DepotSay(p, "We lost that unit, and the lander with it. Sit down again for a new shell: the module is back on the stores tower.", SandboxNow);
            DepotBackTo(p, ref run, DepotCue.Hold, SandboxNow); // (the lander went down without them: start the flight's work again)
        }
        else
            DepotSay(p, "Unit lost. Sit down again and the depot will wake you in a new one.", SandboxNow);
    }

    // ---- The wire ---------------------------------------------------------------------------------------------------

    /// <summary>Where the current state points: the Remote Works, the deck's edge, the stores, the lander or the apron.</summary>
    private Vector3? DepotTargetOf(Player p)
    {
        ref var run = ref _dp[p.Id];
        if (run.State == DepotReport)
            return WorksAt(WorksDoorLocal);
        if (run.State is < DepotFirstStep or DepotDone || run.State - DepotFirstStep >= DepotScript.Current.Steps.Length)
            return null;
        if (!_sh[p.Id].On)
            return WorksAt(WorksDoorLocal);
        return DepotScript.Current.Steps[run.State - DepotFirstStep].Cue switch
        {
            DepotCue.Reach => DepotAt(DepotEdgeLocal),
            DepotCue.Hold => Constructs.Get(run.Module)?.WorldCom ?? DepotAt(DepotStoresLocal),
            DepotCue.Fit or DepotCue.Link or DepotCue.Board => Constructs.Get(run.Lander)?.WorldCom,
            DepotCue.Clear or DepotCue.Land => WorksAt(ApronLocal),
            _ => null,
        };
    }

    /// <summary>Server: their state, to them (<see cref="Msg.Depot"/>: u8 state, u8 has a target, vec the target). The host's own goes straight in.</summary>
    private void DepotSend(Player p)
    {
        var target = DepotTargetOf(p);
        var state = _dp[p.Id].State;
        if (p.Id == LocalId)
        {
            DepotHear(state, target);
            return;
        }
        if (GuidOf(p) is not { } guid)
            return;
        var w = SbW;
        w.Begin(Msg.Depot).U8(state).U8(target != null ? (byte)1 : (byte)0).Vec(target ?? default);
        Net.SendTo(guid, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.Depot"/> (after its id).</summary>
    private void ReadDepot(ref PacketReader r)
    {
        var state = r.U8();
        var has = r.U8() != 0;
        var at = r.Vec();
        DepotHear(state, has && float.IsFinite(at.X + at.Y + at.Z) ? at : null);
    }

    private void DepotHear(byte state, Vector3? target)
    {
        if (state != DepotLocal)
            DepotChangedAt = Brutal.GlfwApi.Glfw.Time;
        DepotLocal = state;
        DepotHasTarget = target != null;
        DepotTarget = target ?? default;
    }

    // ---- The save ---------------------------------------------------------------------------------------------------

    /// <summary>Server: a person arrives in a saved world: where they were on the depot's run.</summary>
    private void DepotArrive(Player p, WorldPlayer rec)
    {
        _dpWho[p.Id] = p;
        _dp[p.Id] = new DepotRun { State = rec.DepotState, Lander = rec.DepotLander };
        DepotSend(p);
    }

    /// <summary>Server: what goes in a person's record.</summary>
    private void DepotCatch(Player p, WorldPlayer rec)
    {
        if (_dpWho[p.Id] != p)
            return;
        rec.DepotState = _dp[p.Id].State;
        rec.DepotLander = _dp[p.Id].Lander;
    }

    /// <summary>A saved world's depot section (WorldSaveWriter.DepotTag): each person's state and lander, onto their record.</summary>
    private static void WorldReadDepot(SaveReader r, WorldSession ws)
    {
        var n = r.Count(19);
        for (var k = 0; k < n; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var state = r.U8();
            var lander = r.U16();
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam)
                {
                    (rec.DepotState, rec.DepotLander) = (state, lander);
                    break;
                }
        }
    }

    /// <summary>Server, loading a world (from <see cref="OpeningLoad"/>): its depot, or a new one for a world from before it.</summary>
    private void DepotLoad(ushort works, ushort depot)
    {
        _dpWorks = Constructs.Get(works) != null ? works : (ushort)0;
        _dpDepot = Constructs.Get(depot) != null ? depot : (ushort)0;
        if (_dpDepot == 0 && _opColony != 0)
            DepotBuild();
    }
}

/// <summary>What completes a depot step (Story/Depot/Depot.json).</summary>
public enum DepotCue : byte
{
    Wait, // the step's seconds pass
    Walk, // this many metres from where the step began
    Reach, // within this many metres of the deck's front edge
    Hold, // the named part in the hands (or already fitted)
    Fit, // a Driving Module on their lander
    Link, // the lander's seat drives it (linked into the module)
    Board, // in the lander's driving seat
    Clear, // the lander this many metres below the deck
    Land, // the lander at rest within this many metres of the Remote Works' apron
}

public sealed class DepotStep
{
    public string Id = "", Title = "", Text = "", Say = "", Part = "";
    public DepotCue Cue;
    public float Seconds = 6, Amount = 1;
}

/// <summary>Story/Depot/Depot.json (format call-of-slop-depot/1): the depot's speaker, height, memo and steps, the same on every peer. A bad file is refused with a reason (Problem) and there is no depot.</summary>
public sealed class DepotScript
{
    public const string Format = "call-of-slop-depot/1";
    public const int MaxSteps = 32;

    public string Speaker = "Depot Control", Memo = "", ReportTitle = "", ReportText = "", Problem = "";
    public float Height = 1500;
    public DepotStep[] Steps = [];

    private static DepotScript? _current;
    public static DepotScript Current => _current ??= Load(Path.Combine(AppFolders.Data, "Story", "Depot", "Depot.json"));

    public static DepotScript Load(string path)
    {
        try
        {
            var s = Parse(File.ReadAllText(path));
            Console.WriteLine($"[depot] {s.Steps.Length} steps");
            return s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
        {
            Console.WriteLine($"[depot] refused: {ex.Message}");
            return new DepotScript { Problem = $"Depot.json: {ex.Message}" };
        }
    }

    public static DepotScript Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        string Str(JsonElement e, string key, string where, bool need)
        {
            if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return Ascii(v.GetString() ?? "", $"{where}.{key}");
            return need ? throw new FormatException($"{where} has no \"{key}\"") : "";
        }
        var s = new DepotScript
        {
            Speaker = Str(root, "speaker", "the file", false) is { Length: > 0 } sp ? sp : "Depot Control",
            Memo = Str(root, "memo", "the file", true),
        };
        if (root.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number)
            s.Height = h.GetSingle();
        if (s.Height is < 100 or > 2400)
            throw new FormatException("height must be 100 to 2400 m (under a planetoid's sky and the shell chair's range)");
        if (root.TryGetProperty("report", out var rep) && rep.ValueKind == JsonValueKind.Object)
        {
            s.ReportTitle = Str(rep, "title", "report", true);
            s.ReportText = Str(rep, "text", "report", true);
        }
        if (!root.TryGetProperty("steps", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("no \"steps\" list");
        var steps = new List<DepotStep>();
        foreach (var e in list.EnumerateArray())
        {
            var where = $"steps[{steps.Count}]";
            var st = new DepotStep { Id = Str(e, "id", where, true), Title = Str(e, "title", where, true), Text = Str(e, "text", where, true), Say = Str(e, "say", where, false) };
            var cue = Str(e, "cue", where, true);
            if (!Enum.TryParse(cue, true, out st.Cue) || int.TryParse(cue, out _))
                throw new FormatException($"{where} ({st.Id}): no such cue \"{cue}\"");
            if (e.TryGetProperty("seconds", out var sec) && sec.ValueKind == JsonValueKind.Number)
                st.Seconds = sec.GetSingle();
            if (e.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number)
                st.Amount = am.GetSingle();
            if (st.Seconds is <= 0 or > 120 || st.Amount is <= 0 or > 1000)
                throw new FormatException($"{where} ({st.Id}): seconds must be over 0 and at most 120, amount over 0 and at most 1000");
            if (st.Cue == DepotCue.Hold)
            {
                st.Part = Str(e, "part", where, true);
                if (ConstructParts.Find(st.Part) == null)
                    throw new FormatException($"{where} ({st.Id}): no part named \"{st.Part}\"");
            }
            steps.Add(st);
            if (steps.Count > MaxSteps)
                throw new FormatException($"more than {MaxSteps} steps");
        }
        if (steps.Count == 0)
            throw new FormatException("no steps");
        s.Steps = steps.ToArray();
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
