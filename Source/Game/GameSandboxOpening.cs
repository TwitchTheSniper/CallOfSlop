using System.Numerics;
using System.Text.Json;
using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>The beats of the survival opening (docs/story/OPENING.md), in order. The byte goes on the wire.</summary>
public enum OpeningStage : byte
{
    None, // not in it (done, skipped, or no opening in this world)
    Crash, // coming down: shaking, a flash, black
    Out, // out cold
    ComeTo, // eyes open for a moment, lying at the crash site, the recovery team over them
    Under, // out again, carried off
    Wake, // waking in the colony's cell block
}

/// <summary>
/// The survival opening (docs/story/OPENING.md): a new survival world's players crash-land, pass out, come to for a
/// moment with the Ministry of Slop's recovery team over them ("There's one over here!"), black out again and wake in
/// the Ministry's penal colony, where the warden's induction (the keybind tutorial, GameTutorial.cs) takes over.
/// The server runs it per player: it places the colony (a blueprint, anchored, with the start kit round it), the
/// wreck at the crash site and the colony's staff (a story cast with bodies), and walks each player through the beats.
/// Every friend gets it on first arriving in the world, late joiners too, and the jump key skips it. Clients only
/// draw it: the beat comes in <see cref="Msg.Opening"/>, the input is filtered on both sides, the lines are chat lines.
/// </summary>
public sealed partial class Game
{
    /// <summary>The tutorial's side of the seam (GameTutorial.cs, the keybind tutorial): the opening has this player, so the induction waits.</summary>
    partial void TutorialDefer(byte id);

    /// <summary>The tutorial's side of the seam: the player is awake in the colony (or skipped), the warden's induction starts.</summary>
    partial void TutorialBegin(byte id);

    private struct OpeningRun
    {
        public OpeningStage Stage;
        public double From; // game time the beat began
        public float Time; // how long it lasts
        public int Line; // the next line of the beat to say
        public byte R0, R1, R2; // the recovery team's bodies (player ids; 0: none; shared by a group)
        public int Group; // who crashed together (co-op): the same number goes through the beats on one clock
        public Vector3 Focus; // where the camera looks (sent with the beat)
    }

    private readonly OpeningRun[] _op = new OpeningRun[256];
    private readonly bool[] _opRescuer = new bool[256]; // this bot is a recovery team member (GameSandboxSpawns.cs asks)
    private readonly bool[] _opDone = new bool[256]; // no saved world: this player id has been through it this match
    private bool _opOn; // this world has the opening: a colony and a crash site
    private ushort _opColony; // the colony construct (0: none, then everyone wakes at the start)
    private Vector3 _opWake, _opMiddle, _opGate, _opCrash; // world: the cell block's floor, the yard's middle, outside the gate, the crash site
    private float _opWakeYaw;
    private double _opNextLook;
    private int _opNextGroup;

    /// <summary>How many are in this group's opening now (co-op: friends who crashed together).</summary>
    private int OpeningGroupSize(int group)
    {
        var n = 0;
        for (var id = 0; id < MaxPlayers; id++)
            if (_op[id].Stage != OpeningStage.None && _op[id].Group == group)
                n++;
        return n;
    }
    private OpeningSave? _opLoaded; // read from a world's save, applied once its constructs are in

    /// <summary>The local player's beat (both sides: the host sets it, a client hears it).</summary>
    public OpeningStage OpeningLocal { get; private set; }

    /// <summary>The local player is in the opening: the tutorial and the HUD hold back.</summary>
    public bool OpeningActive => OpeningLocal != OpeningStage.None;

    /// <summary>This machine's game time, as the last update had it (the local beat is timed on it).</summary>
    public double OpeningNow { get; private set; }

    /// <summary>When the local beat began (this machine's game time) and how long it lasts.</summary>
    public double OpeningLocalFrom { get; private set; }
    public float OpeningLocalTime { get; private set; }

    /// <summary>Where the local camera looks during the beat, the beat's caption, the line said last, and the skip hint.</summary>
    public Vector3 OpeningLocalFocus { get; private set; }
    public string OpeningCaption { get; private set; } = "";
    public string OpeningSubtitle { get; private set; } = "";
    public double OpeningSubtitleAt { get; private set; }
    public static string OpeningSkipHint => OpeningScript.Current.Skip;

    /// <summary>The warden's body (the cast's warden), or null.</summary>
    public Player? OpeningWarden() =>
        Story is { } s && s.World.Find(OpeningScript.Current.Warden) is var e and >= 0 ? StoryBodyOf(e) : null;

    /// <summary>Is this player in the opening now (server)?</summary>
    public bool OpeningHas(byte id) => _op[id].Stage != OpeningStage.None;

    // ---- The world: colony, wreck and staff -------------------------------------------------------------------------

    /// <summary>Both sides, a new match: no beats, no colony until the server builds or loads one.</summary>
    internal void OpeningReset()
    {
        Array.Clear(_op);
        Array.Clear(_opRescuer);
        Array.Clear(_opDone);
        _opOn = false;
        _opColony = 0;
        LockdownReset(); // (and its barracks: GameColonyLockdown.cs)
        DepotReset(); // (and the Remote Works and the Orbital Depot: GameDepot.cs)
        QuarryReset(); // (and the Ministry Quarry: GameQuarry.cs)
        FragmentsReset(); // (and the data centres: GameStoryFragments.cs)
        OpeningLocal = OpeningStage.None;
        OpeningCaption = OpeningSubtitle = "";
        _storyMiddleOf = null; // (the people's middle is the colony's when there is one)
    }

    /// <summary>The story's home planetoid: where a story game starts (the Ministry's colony), in the galaxy's first system (docs/planets/WORLDS.md).</summary>
    public const string StoryHomeMap = "Planetoid";

    /// <summary>
    /// Server, a fresh survival world's start (from <see cref="SandboxStartKit"/>, before the kit): the colony on level
    /// ground near <paramref name="from"/>, the wreck out at the crash site, the staff into the story. Returns where the
    /// kit should stand round (the colony's middle), or <paramref name="from"/> when there is no colony.
    /// </summary>
    private Vector3 OpeningBuild(Vector3 from)
    {
        var script = OpeningScript.Current;
        if (script.Problem.Length > 0)
        {
            Log($"[opening] {script.Problem}: no opening");
            return from;
        }
        _opOn = true;
        if (SandboxKitPiece(script.Colony, from, level: 1.2f) is not { } colony)
        {
            Log($"[opening] no level ground for the {script.Colony} near {from}: everyone wakes at the start");
            _opMiddle = _opGate = _opWake = from;
            _opCrash = from + new Vector3(script.CrashFrom * 0.5f, 0, 0);
            return from;
        }
        _opColony = colony.Id;
        OpeningSpots(colony);
        LockdownBuildBarracks(); // (the guards' barracks beside it: GameColonyLockdown.cs)
        DepotBuild(); // (the Remote Works beside it and the Orbital Depot over that: GameDepot.cs)
        QuarryBuild(); // (the Ministry Quarry a few hundred metres out: GameQuarry.cs)
        FragmentSitesBuild(); // (the sealed data centres out past it: GameStoryFragments.cs)
        var wreck = SandboxKitPiece(script.Wreck, _opMiddle, script.CrashFrom, script.CrashTo) ?? SandboxKitPiece(script.Wreck, _opMiddle, 25, script.CrashTo);
        if (wreck != null)
        {
            _opCrash = wreck.WorldCom;
            Constructs.Events.Add($"opening: the wreck ({script.Wreck}) as construct {wreck.Id}");
        }
        else
            _opCrash = SbScatter(_opMiddle + new Vector3(0, 0.5f, script.CrashFrom * 0.5f), 10);
        if (Story is { } s && StoryData.People.Cast(script.Cast) is { } cast)
        {
            var n = s.Agents.AddCast(cast, StoryPlace(), scripted: false);
            Log($"[opening] the {cast.Name} cast: {n} people");
        }
        _storyMiddleOf = null;
        Log($"[opening] the colony is construct {colony.Id} at {_opMiddle.X:0},{_opMiddle.Z:0}; the crash site at {_opCrash.X:0},{_opCrash.Z:0}");
        return _opMiddle;
    }

    /// <summary>The colony's spots from its pose (the blueprint's cells: docs/story/OPENING.md).</summary>
    private void OpeningSpots(Construct colony)
    {
        _opWake = colony.ToWorld(new Vector3(3.875f, 0.42f, 7.875f)); // (the cell block's floor, cells 8 to 23 by 24 to 39)
        _opMiddle = colony.ToWorld(new Vector3(5.875f, 0.42f, 5.875f));
        var gate = colony.ToWorld(new Vector3(5.875f, 0.42f, -3.5f)); // (outside the gateway, on the ground)
        _opGate = SandboxSpawnSpot(gate + new Vector3(0, 1f, 0), out var feet) == null ? feet : gate;
        var toGate = colony.ToWorld(new Vector3(0, 0, -1)) - colony.ToWorld(Vector3.Zero);
        _opWakeYaw = MathF.Atan2(toGate.X, -toGate.Z);
    }

    /// <summary>The story people's middle (GameStoryBodies.cs): just outside the colony's gate, when there is one.</summary>
    private Vector3? OpeningStoryMiddle() => _opColony != 0 ? _opGate : null;

    // ---- The beats --------------------------------------------------------------------------------------------------

    /// <summary>Server, every tick in Sandbox: new arrivals start, beats move on, lines are said. Ten looks a second.</summary>
    internal void OpeningTick(double now)
    {
        if (!_opOn || now < _opNextLook || Phase != MatchPhase.Playing)
            return;
        _opNextLook = now + 0.1;
        for (var id = 0; id < MaxPlayers; id++)
        {
            if (!Players.TryGetValue((byte)id, out var p) || p.Bot != null)
                continue;
            ref var run = ref _op[id];
            if (run.Stage == OpeningStage.None)
            {
                if (p.Alive && OpeningFirstTime(p))
                    OpeningStart(p, now);
                continue;
            }
            if (!p.Alive)
            {
                OpeningEnd(p, skipped: true); // (killed lying there: the respawn is the start)
                continue;
            }
            OpeningLines(p, ref run, now);
            if (now - run.From >= run.Time)
                OpeningNext(p, ref run, now);
        }
    }

    /// <summary>Server: has this player not been through the opening in this world? True once: it marks them.</summary>
    private bool OpeningFirstTime(Player p)
    {
        if (WorldSession is { } ws)
        {
            if (!ws.Applied || !WorldKnown(p))
                return false; // (their record comes with their Hello, or the world is still loading)
            var rec = WorldRecordOf(ws, p);
            if (rec.Opened)
                return false;
            rec.Opened = true;
            return true;
        }
        if (_opDone[p.Id])
            return false;
        _opDone[p.Id] = true;
        return true;
    }

    private void OpeningStart(Player p, double now)
    {
        TutorialDefer(p.Id);
        // Co-op: friends arriving while a group is still coming down or out cold crashed with it, on its clock, lying
        // beside the first of them; anyone later has a crash of their own (found on their own, then woken in the colony).
        for (var id = 0; id < MaxPlayers; id++)
        {
            ref var g = ref _op[id];
            if (id == p.Id || g.Stage is not (OpeningStage.Crash or OpeningStage.Out) || !Players.TryGetValue((byte)id, out var lead))
                continue;
            var n = OpeningGroupSize(g.Group);
            var a = lead.Yaw + MathF.PI * 0.5f * (n % 2 == 0 ? 1 : -1);
            var beside = SbScatter(lead.Position + new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)) * (1.6f + (n - 1) / 2 * 1.4f) + new Vector3(0, 0.5f, 0), 0.4f);
            p.Teleport(beside);
            var dc = _opCrash - beside;
            p.Yaw = MathF.Atan2(dc.X, -dc.Z);
            ref var run = ref _op[p.Id];
            run = g; // (the same beat, begun at the same time, with the same team once it comes)
            Log($"[opening] {p.Name}: crash-landed with {lead.Name}'s group ({n + 1} now)");
            OpeningSend(p, run.Stage, (float)(now - run.From), run.Time, run.Focus, OpeningScript.Current.Beat(run.Stage).Caption, "");
            return;
        }
        // At the crash site, lying near the wreck, facing it.
        var off = new Vector3(MathF.Sin(p.Id * 2.39996f), 0, MathF.Cos(p.Id * 2.39996f));
        var feet = SbScatter(_opCrash + off * 6f + new Vector3(0, 0.5f, 0), 2.5f);
        p.Teleport(feet);
        var d = _opCrash - feet;
        p.Yaw = MathF.Atan2(d.X, -d.Z);
        Log($"[opening] {p.Name}: crash-landed at {feet.X:0},{feet.Z:0}");
        _op[p.Id].Group = ++_opNextGroup;
        OpeningBeat(p, OpeningStage.Crash, now);
    }

    /// <summary>Server: the next beat (moving them to the colony while they are under).</summary>
    private void OpeningNext(Player p, ref OpeningRun run, double now)
    {
        switch (run.Stage)
        {
            case OpeningStage.Crash:
                OpeningBeat(p, OpeningStage.Out, now);
                break;
            case OpeningStage.Out:
                OpeningRescue(p, ref run);
                OpeningBeat(p, OpeningStage.ComeTo, now);
                break;
            case OpeningStage.ComeTo:
                OpeningBeat(p, OpeningStage.Under, now);
                break;
            case OpeningStage.Under:
                OpeningRescueGone(p.Id);
                OpeningToColony(p);
                OpeningBeat(p, OpeningStage.Wake, now);
                break;
            default:
                OpeningEnd(p, skipped: false);
                break;
        }
    }

    private void OpeningBeat(Player p, OpeningStage stage, double now)
    {
        ref var run = ref _op[p.Id];
        var beat = OpeningScript.Current.Beat(stage);
        run.Stage = stage;
        run.From = now;
        run.Time = beat.Time;
        run.Line = 0;
        run.Focus = stage switch
        {
            OpeningStage.ComeTo when Players.TryGetValue(run.R0, out var r) && run.R0 != 0 => r.Position + GravityFrames.UpAt(r.Position) * 1.35f,
            OpeningStage.Wake => p.Position + new Vector3(MathF.Sin(_opWakeYaw), 0, -MathF.Cos(_opWakeYaw)) * 3f + GravityFrames.UpAt(p.Position) * 1.4f, // (towards the cell block's door)
            _ => _opCrash,
        };
        OpeningSend(p, stage, 0, beat.Time, run.Focus, beat.Caption, "");
    }

    /// <summary>Server: the beat's lines due by now, said to this player only (in their chat, over the speaker's head).</summary>
    private void OpeningLines(Player p, ref OpeningRun run, double now)
    {
        var beat = OpeningScript.Current.Beat(run.Stage);
        while (run.Line < beat.Lines.Length && now - run.From >= beat.Lines[run.Line].At)
        {
            var line = beat.Lines[run.Line++];
            if (line.Group && OpeningGroupSize(run.Group) < 2)
                continue; // (a line for friends found together)
            var speaker = line.By switch
            {
                OpeningScript.ByWarden => OpeningWarden(),
                0 => run.R0 != 0 && Players.TryGetValue(run.R0, out var a) ? a : null,
                1 => run.R1 != 0 && Players.TryGetValue(run.R1, out var b) ? b : null,
                _ => run.R2 != 0 && Players.TryGetValue(run.R2, out var c) ? c : null,
            };
            var name = speaker?.Name ?? (line.By == OpeningScript.ByWarden ? OpeningScript.Current.Warden : "");
            OpeningSay(p, speaker, name, line.Text, now);
            OpeningSend(p, run.Stage, (float)(now - run.From), run.Time, run.Focus, OpeningScript.Current.Beat(run.Stage).Caption, name.Length > 0 ? $"{name}: {line.Text}" : line.Text);
        }
    }

    /// <summary>Server: a line to one player, from a body (over its head) or, with no body here, as a plain line.</summary>
    private void OpeningSay(Player to, Player? speaker, string name, string text, double now)
    {
        text = ChatRules.Clean(text);
        if (text.Length == 0)
            return;
        var from = speaker?.Id ?? ChatRules.SystemId;
        var flags = speaker != null ? ChatFlags.Say : ChatFlags.System;
        var said = speaker != null ? text : $"{name}: {text}";
        if (to.Id == LocalId)
        {
            AddChat(from, speaker?.Name ?? "", said, now, flags);
            return;
        }
        if (GuidOf(to) is not { } guid)
            return;
        _w.Begin(Msg.ChatLine).U8(from).Str(said).U8((byte)flags).U8(NoTeam);
        Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: the recovery team round a player lying at the crash site, facing them.</summary>
    private void OpeningRescue(Player p, ref OpeningRun run)
    {
        for (var id = 0; id < MaxPlayers; id++)
            if (id != p.Id && _op[id].Stage != OpeningStage.None && _op[id].Group == run.Group && _op[id].R0 != 0)
            {
                (run.R0, run.R1, run.R2) = (_op[id].R0, _op[id].R1, _op[id].R2); // (co-op: one team stands over the group)
                return;
            }
        var names = OpeningScript.Current.Rescuers;
        run.R0 = OpeningRescuer(p, names.Length > 0 ? names[0] : "Recovery Officer", 0f);
        run.R1 = OpeningRescuer(p, names.Length > 1 ? names[1] : "Recovery Officer", -0.75f);
        run.R2 = OpeningRescuer(p, names.Length > 2 ? names[2] : "Recovery Officer", 0.75f);
        BroadcastRoster();
    }

    private byte OpeningRescuer(Player p, string name, float turn)
    {
        var id = FreeNpcId();
        if (id == 0)
            return 0;
        var a = p.Yaw + turn;
        var at = p.Position + new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)) * 1.5f;
        var feet = SandboxSpawnSpot(at + new Vector3(0, 1f, 0), out var f) == null ? f : at;
        var bot = new Player
        {
            Id = id,
            Name = name,
            Bot = new BotBrain(BotDifficulty.Easy, BotPersonality.Anchor, _rng.Next()),
            Team = SbPeopleTeam, // (the people's side: nobody's enemy, no spawner's)
            SbSpawned = true, // (never respawns)
        };
        Players[id] = bot;
        Spawn(bot, ServerTick, Brutal.GlfwApi.Glfw.Time);
        bot.Teleport(feet);
        bot.SbHome = feet;
        var d = p.Position - feet;
        bot.Yaw = bot.RenderYaw = MathF.Atan2(d.X, -d.Z);
        for (var i = 0; i < Player.HistorySize; i++)
            bot.History[i] = new HistoryEntry { Position = feet, Yaw = bot.Yaw };
        _opRescuer[id] = true;
        _storyWho[id] = -1; // (nobody in the story: a kill of one is a kill of a stranger)
        return id;
    }

    /// <summary>Server, before the bot tick (GameSandboxSpawns.cs): a recovery team member stands over the one they found.</summary>
    internal bool OpeningRescuerBot(Player p, BotBrain b, double now)
    {
        if (!_opRescuer[p.Id])
            return false;
        b.HeardTime = b.LastSeenTime = -100;
        b.Target = byte.MaxValue;
        b.ForcedGoal = p.Position;
        b.HoldUntil = now + 1;
        b.HoldAnchor = p.SbHome;
        for (var id = 0; id < MaxPlayers; id++)
        {
            ref var run = ref _op[id];
            if (run.Stage != OpeningStage.None && (run.R0 == p.Id || run.R1 == p.Id || run.R2 == p.Id) && Players.TryGetValue((byte)id, out var them))
            {
                var d = them.Position - p.Position;
                b.GlanceYaw = GravityFrames.Curved ? GravityFrames.YawOf(p.Position, d) : MathF.Atan2(d.X, -d.Z);
                b.GlanceUntil = now + 0.3;
                b.NextGlance = now + 0.3;
                break;
            }
        }
        return true;
    }

    /// <summary>Is this player id a recovery team member (the people's bodies leave them be)?</summary>
    private bool OpeningRescuerId(byte id) => _opRescuer[id];

    /// <summary>Server: this player's recovery team is done with them; the bodies go once nobody else in the group still has them.</summary>
    private void OpeningRescueGone(byte self)
    {
        ref var run = ref _op[self];
        var any = false;
        foreach (var id in (ReadOnlySpan<byte>)[run.R0, run.R1, run.R2])
            if (id != 0 && _opRescuer[id] && !OpeningRescuerShared(self, id))
            {
                _opRescuer[id] = false;
                if (Players.TryGetValue(id, out var bot) && bot.Bot != null)
                {
                    Players.Remove(id);
                    OnModePlayerLeave(bot);
                    any = true;
                }
            }
        run.R0 = run.R1 = run.R2 = 0;
        if (any)
            BroadcastRoster();
    }

    private bool OpeningRescuerShared(byte self, byte bot)
    {
        for (var id = 0; id < MaxPlayers; id++)
            if (id != self && _op[id].Stage != OpeningStage.None && (_op[id].R0 == bot || _op[id].R1 == bot || _op[id].R2 == bot))
                return true;
        return false;
    }

    /// <summary>Server: into the cell block, lying down (each a little to their own side, so friends don't stack).</summary>
    private void OpeningToColony(Player p)
    {
        var side = new Vector3(MathF.Sin(p.Id * 2.39996f), 0, MathF.Cos(p.Id * 2.39996f)) * (0.4f + p.Id % 3 * 0.35f);
        var at = _opColony != 0 && Constructs.Get(_opColony) != null ? _opWake + side : SbScatter(_opGate + new Vector3(0, 0.5f, 0), 3f);
        p.Teleport(at);
        p.Yaw = _opWakeYaw;
    }

    /// <summary>Server: the opening is over for them (the last beat, a skip, a death): the induction starts.</summary>
    private void OpeningEnd(Player p, bool skipped)
    {
        ref var run = ref _op[p.Id];
        if (run.Stage == OpeningStage.None)
            return;
        OpeningRescueGone(p.Id);
        if (skipped && p.Alive && run.Stage != OpeningStage.Wake)
            OpeningToColony(p);
        run.Stage = OpeningStage.None;
        OpeningSend(p, OpeningStage.None, 0, 0, default, "", "");
        Log($"[opening] {p.Name}: {(skipped ? "skipped" : "awake")} in the colony");
        SandboxTellKit(p);
        TutorialBegin(p.Id);
    }

    /// <summary>Server: the player pressed skip (<see cref="CActKind.OpeningSkip"/>).</summary>
    private string? OpeningSkipAct(Player p)
    {
        OpeningEnd(p, skipped: true);
        return null;
    }

    /// <summary>Server: a player leaves mid-opening: the team goes, and they are kept as if they had woken (their record's place).</summary>
    internal void OpeningLeave(Player p)
    {
        ref var run = ref _op[p.Id];
        if (run.Stage == OpeningStage.None)
            return;
        OpeningRescueGone(p.Id);
        if (p.Alive && run.Stage != OpeningStage.Wake)
            OpeningToColony(p);
        run.Stage = OpeningStage.None;
    }

    /// <summary>Both sides: the input of someone in the opening: lying still (prone), standing up at the end of waking.</summary>
    private InputCmd OpeningFilter(Player? p, InputCmd cmd)
    {
        if (p == null)
            return cmd;
        var stage = p.Id == LocalId ? OpeningLocal : _op[p.Id].Stage;
        if (stage == OpeningStage.None)
            return cmd;
        var up = stage == OpeningStage.Wake && (p.Id == LocalId ? OpeningNow - OpeningLocalFrom > OpeningLocalTime - 1.2 : SandboxNow - _op[p.Id].From > _op[p.Id].Time - 1.2);
        cmd.Buttons = up ? Buttons.None : Buttons.Prone;
        cmd.Noclip = false;
        return cmd;
    }

    // ---- The wire ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server: a player's beat, to them (<see cref="Msg.Opening"/>: u8 stage, f32 time into it, f32 its length, vec the
    /// camera's focus, text caption, text the line just said). The host's own goes straight in.
    /// </summary>
    private void OpeningSend(Player p, OpeningStage stage, float into, float time, Vector3 focus, string caption, string subtitle)
    {
        if (p.Id == LocalId)
        {
            OpeningHear(stage, into, time, focus, caption, subtitle);
            return;
        }
        if (GuidOf(p) is not { } guid)
            return;
        _w.Begin(Msg.Opening).U8((byte)stage).F32(into).F32(time).Vec(focus).Str(caption).Str(subtitle);
        Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.Opening"/> (after the id).</summary>
    private void ReadOpening(ref PacketReader r)
    {
        var stage = (OpeningStage)r.U8();
        var into = r.F32();
        var time = r.F32();
        var focus = r.Vec();
        var caption = r.Str();
        var subtitle = r.Str();
        if (stage > OpeningStage.Wake || !float.IsFinite(into + time + focus.X + focus.Y + focus.Z))
            return;
        OpeningHear(stage, into, time, focus, caption, subtitle);
    }

    private void OpeningHear(OpeningStage stage, float into, float time, Vector3 focus, string caption, string subtitle)
    {
        var now = OpeningNow; // (this machine's clock: the beat's times are lengths, not the server's)
        if (stage != OpeningLocal || subtitle.Length == 0)
        {
            OpeningLocal = stage;
            OpeningLocalFrom = now - into;
            OpeningLocalTime = time;
        }
        OpeningLocalFocus = focus;
        OpeningCaption = caption;
        if (subtitle.Length > 0)
        {
            OpeningSubtitle = subtitle;
            OpeningSubtitleAt = now;
        }
        else if (stage == OpeningStage.None || stage == OpeningStage.Crash)
            OpeningSubtitle = "";
    }

    /// <summary>Client (or the host): skip the rest of the opening.</summary>
    public void OpeningSkip()
    {
        if (OpeningActive)
            SandboxSend(CActKind.OpeningSkip);
    }

    // ---- The save ---------------------------------------------------------------------------------------------------

    /// <summary>Server, taking a save: the colony and crash site (with the map they are on), or null with no opening.</summary>
    private OpeningSave? OpeningCapture() => _opOn ? new OpeningSave
    {
        Map = World.Map.Name, Colony = _opColony, Crash = _opCrash, Barracks = _opBarracks, Works = _dpWorks, Depot = _dpDepot,
        Sites = FragmentSitesCapture(), Quarry = _qrWorks,
    } : null;

    /// <summary>Server, loading a world: its opening, when it was saved on this map (another planetoid's has none here).</summary>
    private void OpeningLoad(OpeningSave o)
    {
        if (o.Map != World.Map.Name)
            return;
        _opOn = true;
        _opColony = o.Colony;
        _opCrash = o.Crash;
        if (Constructs.Get(o.Colony) is { } colony)
            OpeningSpots(colony);
        else
        {
            _opColony = 0;
            _opMiddle = _opGate = _opWake = o.Crash;
        }
        LockdownLoadBarracks(o.Barracks); // (GameColonyLockdown.cs)
        DepotLoad(o.Works, o.Depot); // (GameDepot.cs)
        QuarryLoad(o.Quarry); // (GameQuarry.cs)
        FragmentSitesLoad(o.Sites); // (the data centres, or built now for a world from before them: GameStoryFragments.cs)
        _storyMiddleOf = null;
    }
}

/// <summary>What a world keeps of its opening (WorldSaveWriter's Opening section).</summary>
public sealed class OpeningSave
{
    public string Map = "";
    public ushort Colony;
    public Vector3 Crash;
    public ushort Barracks; // the Colony Barracks construct (0: none; after the players in the section, so an older reader stops before it)
    public ushort Works, Depot; // the Remote Works and the Orbital Depot (GameDepot.cs; after the barracks, so an older reader stops before them)
    public (string Site, ushort Body)[] Sites = []; // the sealed data centres (GameStoryFragments.cs; after the depot, likewise)
    public ushort Quarry; // the Ministry Quarry Works (GameQuarry.cs; after the data centres, likewise)
}

/// <summary>Story/Opening/Opening.json (docs/story/OPENING.md): the opening's names, times and lines. A bad file is refused with a reason and there is no opening.</summary>
public sealed class OpeningScript
{
    public const string Format = "call-of-slop-opening/1";
    public const int ByWarden = -1;

    public readonly record struct Line(float At, int By, string Text, bool Group = false);

    public sealed class BeatDef
    {
        public float Time = 3;
        public string Caption = "";
        public Line[] Lines = [];
    }

    public string Colony = "Penal Colony", Cast = "Penal Colony", Warden = "", Wreck = "Crashed Plane", Skip = "skip the opening";
    public float CrashFrom = 70, CrashTo = 140;
    public string[] Rescuers = [];
    public readonly BeatDef[] Beats = new BeatDef[(int)OpeningStage.Wake + 1];
    public string Problem = "";

    public BeatDef Beat(OpeningStage s) => Beats[(int)s];

    public static string FilePath => Path.Combine(AppFolders.Data, "Story", "Opening", "Opening.json");

    private static OpeningScript? _current;
    public static OpeningScript Current => _current ??= Load(FilePath);

    public static OpeningScript Load(string path)
    {
        var t = new OpeningScript();
        for (var i = 0; i < t.Beats.Length; i++)
            t.Beats[i] = new BeatDef();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            t.Colony = Text(root, "colony", t.Colony);
            t.Cast = Text(root, "cast", t.Cast);
            t.Warden = Text(root, "warden", t.Warden);
            t.Wreck = Text(root, "wreck", t.Wreck);
            t.Skip = Text(root, "skip", t.Skip);
            if (root.TryGetProperty("crashFrom", out var cf))
                t.CrashFrom = Math.Clamp(cf.GetSingle(), 10, 500);
            if (root.TryGetProperty("crashTo", out var ct))
                t.CrashTo = Math.Clamp(ct.GetSingle(), t.CrashFrom + 5, 600);
            if (root.TryGetProperty("rescuers", out var rs))
                t.Rescuers = [.. rs.EnumerateArray().Take(3).Select(e => Ascii(e.GetString() ?? "", "rescuers"))];
            if (ConstructBuild.FindBlueprint(t.Colony) == null)
                throw new FormatException($"no blueprint named \"{t.Colony}\" (the colony)");
            if (ConstructBuild.FindBlueprint(t.Wreck) == null && ConstructParts.Find(t.Wreck) == null)
                throw new FormatException($"no blueprint or part named \"{t.Wreck}\" (the wreck)");
            var beats = root.GetProperty("beats");
            foreach (var (key, stage) in new[] { ("crash", OpeningStage.Crash), ("out", OpeningStage.Out), ("cometo", OpeningStage.ComeTo), ("under", OpeningStage.Under), ("wake", OpeningStage.Wake) })
            {
                if (!beats.TryGetProperty(key, out var b))
                    throw new FormatException($"beats: no \"{key}\"");
                var def = t.Beats[(int)stage];
                def.Time = Math.Clamp(b.GetProperty("time").GetSingle(), 0.5f, 30f);
                def.Caption = Text(b, "caption", "");
                if (b.TryGetProperty("lines", out var ls))
                    def.Lines = [.. ls.EnumerateArray().Select(l => new Line(
                        Math.Clamp(l.GetProperty("at").GetSingle(), 0, def.Time),
                        l.GetProperty("by").ValueKind == JsonValueKind.String ? l.GetProperty("by").GetString() == "warden" ? ByWarden : throw new FormatException($"beats.{key}: \"by\" is a rescuer's number or \"warden\"") : Math.Clamp(l.GetProperty("by").GetInt32(), 0, 2),
                        Ascii(l.GetProperty("text").GetString() ?? "", $"beats.{key}"),
                        l.TryGetProperty("group", out var gl) && gl.GetBoolean())).OrderBy(l => l.At)];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            t.Problem = $"{Path.GetFileName(path)}: {ex.Message}";
        }
        return t;
    }

    private static string Text(JsonElement e, string key, string fallback) =>
        e.TryGetProperty(key, out var v) ? Ascii(v.GetString() ?? "", key) : fallback;

    private static string Ascii(string s, string where) =>
        s.All(c => c >= ' ' && c < 127) ? s : throw new FormatException($"{where}: player-visible text is ASCII (\"{s}\")");
}
