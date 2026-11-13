using System.Numerics;
using System.Text.Json;
using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>The beats of the survival opening (docs/story/OPENING.md), in order. The byte goes on the wire.</summary>
public enum OpeningStage : byte
{
    None, // not in it (done, skipped, or no opening in this world)
    Crash, // (unused since the placeholder, 2026-11-12: the crash scene; the byte stays for the wire)
    Out, // the placeholder: black, a caption saying what happened, already in the cell block
    ComeTo, // (unused since the placeholder)
    Under, // (unused since the placeholder)
    Wake, // waking in the colony's cell block
}

/// <summary>
/// The survival opening (docs/story/OPENING.md): a new survival world's players see a placeholder card for the crash
/// landing (Karl, 2026-11-12: the falling-down scene is to be redone) and wake in the Ministry of Slop's penal colony,
/// where the warden's induction (the keybind tutorial, GameTutorial.cs) takes over.
/// The server runs it per player: it places the colony (a blueprint, anchored, with the start kit round it), the
/// wreck out past it and the colony's staff (a story cast with bodies), and walks each player through the beats.
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
        public int Group; // who crashed together (co-op): the same number goes through the beats on one clock
        public Vector3 Focus; // where the camera looks (sent with the beat)
    }

    private readonly OpeningRun[] _op = new OpeningRun[256];
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
        Story is { } s && OpScript.Warden.Length > 0 && s.World.Find(OpScript.Warden) is var e and >= 0 ? StoryBodyOf(e) : null;

    /// <summary>Is this player in the opening now (server)?</summary>
    public bool OpeningHas(byte id) => _op[id].Stage != OpeningStage.None;

    // ---- The world: colony, wreck and staff -------------------------------------------------------------------------

    /// <summary>Both sides, a new match: no beats, no colony until the server builds or loads one.</summary>
    internal void OpeningReset()
    {
        Array.Clear(_op);
        Array.Clear(_opDone);
        _opOn = false;
        _opColony = 0;
        LockdownReset(); // (and its barracks: GameColonyLockdown.cs)
        ColonyBuildingsReset(); // (and the Ministry's buildings round the colony: GameColonyBuildings.cs)
        DepotReset(); // (and the Remote Works and the Orbital Depot: GameDepot.cs)
        QuarryReset(); // (and the Ministry Quarry: GameQuarry.cs)
        FragmentsReset(); // (and the data centres: GameStoryFragments.cs)
        LoreReset(); // (and the things to look at: GameStoryLore.cs)
        OpeningLocal = OpeningStage.None;
        OpeningCaption = OpeningSubtitle = "";
        _storyMiddleOf = null; // (the people's middle is the colony's when there is one)
        _start = null; // (the story start, worked out again on this match's map: GameStoryStart.cs)
    }

    /// <summary>
    /// The story's home planetoid: where a story game starts (the Ministry's colony). Glacier, the ice world of Frostreach
    /// (Karl, 2026-11-12: the story starts on the cold planet; docs/planets/WORLDS.md).
    /// </summary>
    public const string StoryHomeMap = "Glacier";

    /// <summary>
    /// Server, a fresh survival world's start (from <see cref="SandboxStartKit"/>, before the kit): the colony on level
    /// ground near <paramref name="from"/>, the wreck out at the crash site, the staff into the story. Returns where the
    /// kit should stand round (the colony's middle), or <paramref name="from"/> when there is no colony.
    /// </summary>
    private Vector3 OpeningBuild(Vector3 from)
    {
        var script = OpScript;
        if (script.Problem.Length > 0)
        {
            Log($"[opening] {script.Problem}: no opening");
            return from;
        }
        _opOn = true;
        Log($"[opening] the {PlayedStart.Name} start on {World.Map.Name} (GameStoryStart.cs)");
        if (script.Colony.Length == 0)
            return OpeningBuildAlone(script, from);
        if (SandboxKitPiece(script.Colony, from, level: 1.2f) is not { } colony)
        {
            Log($"[opening] no level ground for the {script.Colony} near {from}: everyone wakes at the start");
            _opMiddle = _opGate = _opWake = from;
            _opCrash = from + new Vector3(script.CrashFrom * 0.5f, 0, 0);
            return from;
        }
        _opColony = colony.Id;
        OpeningSpots(colony);
        _csBench = ColonySiteGrade(); // (the ground under the whole site graded level, before anything stands on it: GameColonySite.cs)
        if (_csBench)
            OpeningSpots(colony); // (the gate's spot again, on the bench)
        ColonyBuildingsBuild(); // (the Ministry's street in front of it, first, so the rest find room round them: GameColonyBuildings.cs)
        LockdownBuildBarracks(); // (the guards' barracks beside it: GameColonyLockdown.cs)
        DepotBuild(); // (the Remote Works beside it and the Orbital Depot over that: GameDepot.cs)
        ColonySiteFurnish(); // (the street, the square, the covered way, the heating mains: the site's pieces, GameColonySite.cs)
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
        ColonySiteLog();
        Log($"[opening] the colony is construct {colony.Id} at {_opMiddle.X:0},{_opMiddle.Z:0}; the crash site at {_opCrash.X:0},{_opCrash.Z:0}");
        return _opMiddle;
    }

    /// <summary>
    /// Server, an opening with no colony (a start that isn't the penal colony's, docs/story/STARTS.md): the players wake
    /// at the start, the wreck (when the script names one) lies out past them, and the cast (when it names one) joins the
    /// story. None of the Ministry's colony, quarry, depot or data centres is built: those are the penal colony start's.
    /// </summary>
    private Vector3 OpeningBuildAlone(OpeningScript script, Vector3 from)
    {
        _opMiddle = _opGate = _opWake = from;
        _opCrash = from + new Vector3(script.CrashFrom * 0.5f, 0, 0);
        if (script.Wreck.Length > 0 && SandboxKitPiece(script.Wreck, from, script.CrashFrom, script.CrashTo) is { } wreck)
        {
            _opCrash = wreck.WorldCom;
            Constructs.Events.Add($"opening: the wreck ({script.Wreck}) as construct {wreck.Id}");
        }
        if (script.Cast.Length > 0 && Story is { } s && StoryData.People.Cast(script.Cast) is { } cast)
            Log($"[opening] the {cast.Name} cast: {s.Agents.AddCast(cast, StoryPlace(), scripted: false)} people");
        Log($"[opening] no colony: everyone wakes at {from.X:0},{from.Z:0}; the crash site at {_opCrash.X:0},{_opCrash.Z:0}");
        return from;
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
        // Straight into the cell block, under the placeholder card. Co-op: friends arriving while a group is still on
        // the card join it, on its clock, so they wake together; anyone later has a card of their own.
        OpeningToColony(p);
        for (var id = 0; id < MaxPlayers; id++)
        {
            ref var g = ref _op[id];
            if (id == p.Id || g.Stage != OpeningStage.Out || !Players.TryGetValue((byte)id, out var lead))
                continue;
            ref var run = ref _op[p.Id];
            run = g; // (the same beat, begun at the same time)
            Log($"[opening] {p.Name}: with {lead.Name}'s group ({OpeningGroupSize(g.Group)} now)");
            OpeningSend(p, run.Stage, (float)(now - run.From), run.Time, run.Focus, OpScript.Beat(run.Stage).Caption, "");
            return;
        }
        Log($"[opening] {p.Name}: the placeholder crash, then the cell block");
        _op[p.Id].Group = ++_opNextGroup;
        OpeningBeat(p, OpeningStage.Out, now);
    }

    /// <summary>Server: the next beat (the placeholder card, then waking).</summary>
    private void OpeningNext(Player p, ref OpeningRun run, double now)
    {
        if (run.Stage == OpeningStage.Out)
            OpeningBeat(p, OpeningStage.Wake, now);
        else
            OpeningEnd(p, skipped: false);
    }

    private void OpeningBeat(Player p, OpeningStage stage, double now)
    {
        ref var run = ref _op[p.Id];
        var beat = OpScript.Beat(stage);
        run.Stage = stage;
        run.From = now;
        run.Time = beat.Time;
        run.Line = 0;
        run.Focus = p.Position + new Vector3(MathF.Sin(_opWakeYaw), 0, -MathF.Cos(_opWakeYaw)) * 3f + GravityFrames.UpAt(p.Position) * 1.4f; // (towards the cell block's door)
        OpeningSend(p, stage, 0, beat.Time, run.Focus, beat.Caption, "");
    }

    /// <summary>Server: the beat's lines due by now, said to this player only (in their chat, over the speaker's head).</summary>
    private void OpeningLines(Player p, ref OpeningRun run, double now)
    {
        var beat = OpScript.Beat(run.Stage);
        while (run.Line < beat.Lines.Length && now - run.From >= beat.Lines[run.Line].At)
        {
            var line = beat.Lines[run.Line++];
            if (line.Group && OpeningGroupSize(run.Group) < 2)
                continue; // (a line for friends found together)
            var speaker = OpeningWarden(); // (every line is the warden's since the placeholder)
            var name = speaker?.Name ?? OpScript.Warden;
            OpeningSay(p, speaker, name, line.Text, now);
            OpeningSend(p, run.Stage, (float)(now - run.From), run.Time, run.Focus, OpScript.Beat(run.Stage).Caption, name.Length > 0 ? $"{name}: {line.Text}" : line.Text);
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
        if (skipped && p.Alive && run.Stage != OpeningStage.Wake)
            OpeningToColony(p);
        run.Stage = OpeningStage.None;
        OpeningSend(p, OpeningStage.None, 0, 0, default, "", "");
        Log($"[opening] {p.Name}: {(skipped ? "skipped" : "awake")} in the colony");
        SandboxTellKit(p);
        StartStanding(p); // (the start's standing with its faction, when it sets one: GameStoryStart.cs)
        TutorialBegin(p.Id);
    }

    /// <summary>Server: the player pressed skip (<see cref="CActKind.OpeningSkip"/>).</summary>
    private string? OpeningSkipAct(Player p)
    {
        OpeningEnd(p, skipped: true);
        return null;
    }

    /// <summary>Server: a player leaves mid-opening: they are kept as if they had woken (their record's place).</summary>
    internal void OpeningLeave(Player p)
    {
        ref var run = ref _op[p.Id];
        if (run.Stage == OpeningStage.None)
            return;
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
        else if (stage is OpeningStage.None or OpeningStage.Out)
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
        Sites = FragmentSitesCapture(), Quarry = _qrWorks, Buildings = ColonyBuildingsCapture(), BuildingsListed = true,
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
        ColonyBuildingsLoad(o.Buildings, o.BuildingsListed); // (GameColonyBuildings.cs)
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
    public (string Name, ushort Body)[] Buildings = []; // the Ministry's buildings round the colony (GameColonyBuildings.cs; after the quarry, likewise)
    public bool BuildingsListed; // the save had the list (a world from before it gets the buildings when loaded)
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
            if (t.Colony.Length > 0 && ConstructBuild.FindBlueprint(t.Colony) == null)
                throw new FormatException($"no blueprint named \"{t.Colony}\" (the colony)");
            if (t.Wreck.Length > 0 && ConstructBuild.FindBlueprint(t.Wreck) == null && ConstructParts.Find(t.Wreck) == null)
                throw new FormatException($"no blueprint or part named \"{t.Wreck}\" (the wreck)");
            var beats = root.GetProperty("beats");
            foreach (var (key, stage) in new[] { ("placeholder", OpeningStage.Out), ("wake", OpeningStage.Wake) })
            {
                if (!beats.TryGetProperty(key, out var b))
                    throw new FormatException($"beats: no \"{key}\"");
                var def = t.Beats[(int)stage];
                def.Time = Math.Clamp(b.GetProperty("time").GetSingle(), 0.5f, 30f);
                def.Caption = Text(b, "caption", "");
                if (b.TryGetProperty("lines", out var ls))
                    def.Lines = [.. ls.EnumerateArray().Select(l => new Line(
                        Math.Clamp(l.GetProperty("at").GetSingle(), 0, def.Time),
                        l.TryGetProperty("by", out var by) && by.GetString() != "warden" ? throw new FormatException($"beats.{key}: \"by\" is \"warden\"") : ByWarden,
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
