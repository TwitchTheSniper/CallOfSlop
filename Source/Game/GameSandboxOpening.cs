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
    Wake, // waking (aboard the start's ship)
}

/// <summary>
/// The survival opening (docs/story/OPENING.md): a new world's players see the start's card and wake where it puts them
/// (aboard its ship in orbit, GameSpaceStart.cs, or at the start with a wreck out past them). The penal colony and the
/// warden's induction that once followed were removed (2026-11-17).
/// The server runs it per player: it places the start's ship or wreck (and a story cast, when the script names one), and
/// walks each player through the beats.
/// Every friend gets it on first arriving in the world, late joiners too, and the jump key skips it. Clients only
/// draw it: the beat comes in <see cref="Msg.Opening"/>, the input is filtered on both sides, the lines are chat lines.
/// </summary>
public sealed partial class Game
{
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
    private bool _opOn; // this world has the opening: a ship or a crash site
    private Vector3 _opWake, _opMiddle, _opGate, _opCrash; // world: where people wake, the start's middle, the people's middle, the crash site
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

    // ---- The world: the ship or the wreck -------------------------------------------------------------------------

    /// <summary>Both sides, a new match: no beats until the server builds or loads an opening.</summary>
    internal void OpeningReset()
    {
        Array.Clear(_op);
        Array.Clear(_opDone);
        _opOn = false;
        _opShip = 0;
        _opHolds.Clear();
        BreakdownReset(); // (GameSandboxBreakdowns.cs)
        FragmentsReset(); // (and the data centres: GameStoryFragments.cs)
        LoreReset(); // (and the things to look at: GameStoryLore.cs)
        OpeningLocal = OpeningStage.None;
        OpeningCaption = OpeningSubtitle = "";
        _storyMiddleOf = null; // (the people's middle, worked out again)
        _start = null; // (the story start, worked out again on this match's map: GameStoryStart.cs)
    }

    /// <summary>
    /// The story's home planetoid: where a story game starts. Glacier, the ice world of Frostreach (Karl, 2026-11-12: the
    /// story starts on the cold planet; docs/planets/WORLDS.md).
    /// </summary>
    public const string StoryHomeMap = "Glacier";

    /// <summary>
    /// Server, a fresh survival world's start (from <see cref="SandboxStartKit"/>, before the kit): the start's ship in
    /// orbit, or the wreck out at the crash site. Returns where the kit should stand round (<paramref name="from"/>).
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
        if (script.Ship.Length > 0 && OpeningShip(script, from))
            return from; // (aboard a ship in orbit: GameSpaceStart.cs)
        return OpeningBuildAlone(script, from);
    }

    /// <summary>
    /// Server, an opening with no ship (docs/story/STARTS.md): the players wake at the start, the wreck (when the script
    /// names one) lies out past them, and the cast (when it names one) joins the story.
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
        Log($"[opening] everyone wakes at {from.X:0},{from.Z:0}; the crash site at {_opCrash.X:0},{_opCrash.Z:0}");
        return from;
    }

    /// <summary>The story people's middle (GameStoryBodies.cs): none of the opening's since the colony went.</summary>
    private Vector3? OpeningStoryMiddle() => null;

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
        // Straight to where they wake, under the placeholder card. Co-op: friends arriving while a group is still on
        // the card join it, on its clock, so they wake together; anyone later has a card of their own.
        OpeningToWake(p);
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
        Log($"[opening] {p.Name}: the card, then waking");
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
            var speaker = OpeningWarden(); // (every line is the warden's since the placeholder: with no warden, a plain line)
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

    /// <summary>Server: to where they wake, lying down (each a little to their own side, so friends don't stack).</summary>
    private void OpeningToWake(Player p)
    {
        if (_opShip != 0 && OpeningAboard(p))
            return; // (a ship start: aboard, in its frame: GameSpaceStart.cs)
        var at = SbScatter(_opGate + new Vector3(0, 0.5f, 0), 3f);
        p.Teleport(at);
        p.Yaw = _opWakeYaw;
    }

    /// <summary>Server: the opening is over for them (the last beat, a skip, a death).</summary>
    private void OpeningEnd(Player p, bool skipped)
    {
        ref var run = ref _op[p.Id];
        if (run.Stage == OpeningStage.None)
            return;
        if (skipped && p.Alive && run.Stage != OpeningStage.Wake)
            OpeningToWake(p);
        run.Stage = OpeningStage.None;
        OpeningSend(p, OpeningStage.None, 0, 0, default, "", "");
        Log($"[opening] {p.Name}: {(skipped ? "skipped" : "awake")}");
        SandboxTellKit(p);
        StartStanding(p); // (the start's standing with its faction, when it sets one: GameStoryStart.cs)
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
            OpeningToWake(p);
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

    /// <summary>Server, taking a save: the start's ship and crash site (with the map they are on), or null with no opening.</summary>
    private OpeningSave? OpeningCapture() => _opOn ? new OpeningSave { Map = World.Map.Name, Colony = _opShip, Crash = _opCrash } : null;

    /// <summary>
    /// Server, loading a world: its opening, when it was saved on this map (another planetoid's has none here) and the start
    /// has one. A world from before starts (StoryStarts.Legacy, an old penal colony world) has none: its colony stays as
    /// plain constructs and nobody goes through an opening.
    /// </summary>
    private void OpeningLoad(OpeningSave o)
    {
        if (o.Map != World.Map.Name || OpScript.Problem.Length > 0)
            return;
        _opOn = true;
        _opCrash = o.Crash;
        _opMiddle = _opGate = _opWake = o.Crash;
        _storyMiddleOf = null;
        if (OpScript.Ship.Length > 0) // (a ship start keeps its ship: late friends wake aboard it, GameSpaceStart.cs)
            _opShip = Constructs.Get(o.Colony) != null ? o.Colony : (ushort)0;
    }
}

/// <summary>What a world keeps of its opening (WorldSaveWriter's Opening section).</summary>
public sealed class OpeningSave
{
    public string Map = "";
    public ushort Colony; // the start's ship (the name is the old one: an older world kept its penal colony here)
    public Vector3 Crash;
}

/// <summary>A file in Story/Opening (docs/story/OPENING.md): the opening's names, times and lines. A bad file is refused with a reason and there is no opening.</summary>
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

    public string Cast = "", Warden = "", Wreck = "", Skip = "skip the opening";
    public float CrashFrom = 70, CrashTo = 140;
    public string Ship = ""; // a ship start: the blueprint everyone wakes aboard, in orbit (GameSpaceStart.cs)
    public float Orbit = 3000; // its height over the ground (m; at least past the sky's edge)
    public Vector3[] Wake = []; // where people wake in it, in the ship's own metres (from its first part's middle), one each in turn
    /// <summary>A crewed ship of an NPC faction kind going round with the start's ship, <c>Behind</c> m back along its orbit (GameSpaceStart.cs).</summary>
    public readonly record struct Companion(string Ship, string Faction, float Behind, float Away = 0);
    public Companion[] Company = [];
    /// <summary>A station built on its own circular orbit, <c>Orbit</c> m over the ground above the start, <c>Ahead</c> m on along it (GameSpaceStart.cs).</summary>
    public readonly record struct Station(string Blueprint, float Orbit, float Ahead);
    public Station[] Stations = [];
    public readonly BeatDef[] Beats = new BeatDef[(int)OpeningStage.Wake + 1];
    public string Problem = "";

    public BeatDef Beat(OpeningStage s) => Beats[(int)s];

    /// <summary>The default start's opening (a client's skip hint).</summary>
    public static OpeningScript Current => StoryStarts.Default.Script;

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
            t.Cast = Text(root, "cast", t.Cast);
            t.Warden = Text(root, "warden", t.Warden);
            t.Wreck = Text(root, "wreck", t.Wreck);
            t.Skip = Text(root, "skip", t.Skip);
            if (root.TryGetProperty("crashFrom", out var cf))
                t.CrashFrom = Math.Clamp(cf.GetSingle(), 10, 500);
            if (root.TryGetProperty("crashTo", out var ct))
                t.CrashTo = Math.Clamp(ct.GetSingle(), t.CrashFrom + 5, 600);
            if (Text(root, "colony", "").Length > 0)
                throw new FormatException("\"colony\": the penal colony was removed (2026-11-17)");
            if (t.Wreck.Length > 0 && ConstructBuild.FindBlueprint(t.Wreck) == null && ConstructParts.Find(t.Wreck) == null)
                throw new FormatException($"no blueprint or part named \"{t.Wreck}\" (the wreck)");
            t.Ship = Text(root, "ship", "");
            if (t.Ship.Length > 0 && ConstructBuild.FindBlueprint(t.Ship) == null)
                throw new FormatException($"no blueprint named \"{t.Ship}\" (the ship)");
            if (root.TryGetProperty("orbit", out var ob))
                t.Orbit = Math.Clamp(ob.GetSingle(), 500, 50000);
            if (root.TryGetProperty("wake", out var wk))
                t.Wake = [.. wk.EnumerateArray().Select(v => v.GetArrayLength() == 3 ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : throw new FormatException("\"wake\": [x, y, z] spots"))];
            if (root.TryGetProperty("company", out var co))
                t.Company = [.. co.EnumerateArray().Select(ReadCompanion)];
            if (t.Company.Length > 0 && t.Ship.Length == 0)
                throw new FormatException("\"company\" goes with a ship start (\"ship\")");
            if (root.TryGetProperty("stations", out var st))
                t.Stations = [.. st.EnumerateArray().Select(ReadStation)];
            if (t.Stations.Length > 0 && t.Ship.Length == 0)
                throw new FormatException("\"stations\" go with a ship start (\"ship\")");
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

    private static Companion ReadCompanion(JsonElement e)
    {
        var ship = Text(e, "ship", "");
        var faction = Text(e, "faction", "");
        if (ConstructBuild.FindBlueprint(ship) == null)
            throw new FormatException($"company: no blueprint named \"{ship}\"");
        if (CrewFactions.Find(faction) == 0)
            throw new FormatException($"company: no NPC faction kind named \"{faction}\" (Crew/Factions)");
        // (at least 150 m clear either way, so two frigates never build into each other; at most 5 km, still in sight)
        var behind = Math.Clamp(e.TryGetProperty("behind", out var b) ? b.GetSingle() : 400f, -5000f, 5000f);
        if (MathF.Abs(behind) < 150f)
            behind = behind < 0 ? -150f : 150f;
        // "away": held still that far out instead (m straight out from the planet past the player's ship, not on the orbit:
        // Karl, 2026-11-18, a rival to find on the radar), under the far distance where a ship is parked (GameSpaceJump.cs)
        var away = Math.Clamp(e.TryGetProperty("away", out var w) ? w.GetSingle() : 0f, 0f, 150_000f);
        if (away > 0 && away < 1000f)
            away = 1000f;
        return new Companion(ship, faction, behind, away);
    }

    private static Station ReadStation(JsonElement e)
    {
        var bp = Text(e, "blueprint", "");
        if (ConstructBuild.FindBlueprint(bp) == null)
            throw new FormatException($"stations: no blueprint named \"{bp}\"");
        // (high enough to clear the start's ship and its company by kilometres; at most 150 km, inside the bubble's 200 km reach from
        // the origin on the ground (GameSpaceJump.SpaceFarDistance), past which a ship is parked away)
        var orbit = Math.Clamp(e.TryGetProperty("orbit", out var o) ? o.GetSingle() : 40000f, 5000f, 150000f);
        var ahead = Math.Clamp(e.TryGetProperty("ahead", out var a) ? a.GetSingle() : 0f, -50000f, 50000f);
        return new Station(bp, orbit, ahead);
    }

    private static string Text(JsonElement e, string key, string fallback) =>
        e.TryGetProperty(key, out var v) ? Ascii(v.GetString() ?? "", key) : fallback;

    private static string Ascii(string s, string where) =>
        s.All(c => c >= ' ' && c < 127) ? s : throw new FormatException($"{where}: player-visible text is ASCII (\"{s}\")");
}
