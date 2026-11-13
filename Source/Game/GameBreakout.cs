using System.Numerics;
using System.Text.Json;
using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The break-out (docs/story/BREAKOUT.md; Karl, 2026-11-13): once a player is in with the convicts and Dot has named the
/// night (the storylet "The night", its hook "breakout", which leaves the lead "The Night"), the crew walks to the
/// Ministry's ration hauler outside the gate and waits; the player walks up to it and they are off, the colony's siren
/// behind them. The ride is the crew bus's scene (Program.Quarry.cs, the craft STAND-IN, Source/StandIns): the hauler
/// pulls away, black, and another comes in at the Ministry Spaceport, where the player and the crew are set down. The
/// story keeps what matters (the player's <c>Escaped</c>, each convict's <c>Fled</c>), so a loaded world puts everyone
/// back where they were: a player with the lead who hasn't escaped is back at the muster, a convict who fled stands at
/// the spaceport. Host only, ten looks a second, apart from each player's own state (<see cref="Msg.Breakout"/>).
/// Data: Story/Opening/Breakout.json.
/// </summary>
public sealed partial class Game
{
    public const byte BreakoutNone = 0, BreakoutMuster = 1, BreakoutSpaceport = 2;
    public const float BreakoutBoardReach = 6f; // m from the hauler to board it
    public const double BreakoutAlarmFor = 75; // s the colony's siren wails after the hauler goes

    private struct BreakoutRun
    {
        public byte Stage;
        public int Bus; // the hauler waiting at the motor pool (a stand-in craft's id, 0: none called)
        public int RideTo; // the hauler coming in at the spaceport
        public uint RideFrom; // the tick they boarded (0: not riding)
    }

    private readonly BreakoutRun[] _bk = new BreakoutRun[256];
    private readonly Player?[] _bkWho = new Player?[256];
    private readonly int[] _bkCrew = new int[BreakoutScript.MaxCrew]; // the crew's story entities (-1: not in this story)
    private StoryEngine? _bkCrewOf;
    private int _bkRelLead = -2, _bkLead = -1, _bkQEscaped = -1, _bkQFled = -1, _bkVAbscond = -1;
    private double _bkNextLook, _bkAlarmUntil;
    private bool _bkMuster; // anyone at the muster now (the crew walk to the hauler)
    private Vector3 _bkPool; // where the hauler waits (world)

    // ---- Every peer: this PC's own state, as the server tells it (Msg.Breakout)
    public byte BreakoutLocal { get; private set; }
    public Vector3 BreakoutTarget { get; private set; }
    public bool BreakoutHasTarget { get; private set; }
    public double BreakoutChangedAt { get; private set; }
    public uint BreakoutRideFrom { get; private set; }
    public ushort BreakoutRideBusA { get; private set; }
    public ushort BreakoutRideBusB { get; private set; }

    /// <summary>Both sides, a new match: nobody breaking out.</summary>
    private void BreakoutReset()
    {
        Array.Clear(_bk);
        Array.Clear(_bkWho);
        _bkCrewOf = null;
        _bkRelLead = -2;
        _bkMuster = false;
        _bkAlarmUntil = 0;
        BreakoutLocal = BreakoutNone;
        BreakoutHasTarget = false;
        BreakoutRideFrom = 0;
        BreakoutFightReset();
    }

    /// <summary>Server, a storylet's hook "breakout" (GameStoryStorylets.cs): the night is on for this player.</summary>
    private void BreakoutStart(Player p)
    {
        if (p.Id >= MaxPlayers || _bkWho[p.Id] == p && _bk[p.Id].Stage != BreakoutNone)
            return;
        _bkWho[p.Id] = p;
        _bk[p.Id] = new BreakoutRun { Stage = BreakoutMuster };
        BreakoutSend(p);
        Log($"[breakout] {p.Name}: the night is on, to the motor pool");
    }

    /// <summary>The story's ids this needs, looked up once per story.</summary>
    private bool BreakoutIds(StoryEngine s)
    {
        if (ReferenceEquals(_bkCrewOf, s) && _bkRelLead != -2)
            return _bkLead >= 0;
        _bkCrewOf = s;
        var script = BreakoutScript.Current;
        for (var i = 0; i < _bkCrew.Length; i++)
            _bkCrew[i] = i < script.Crew.Length ? s.World.Find(script.Crew[i]) : -1;
        _bkRelLead = s.Rules.Relation("Lead")?.Id ?? -1;
        _bkLead = s.World.Find(script.Lead);
        _bkQEscaped = s.Rules.Quality("Escaped")?.Id ?? -1;
        _bkQFled = s.Rules.Quality("Fled")?.Id ?? -1;
        _bkVAbscond = s.Rules.VerbId("Abscond");
        if (_bkLead < 0)
            _bkRelLead = -1; // (looked again when the lead first exists: it is made when somebody learns it)
        return _bkLead >= 0;
    }

    /// <summary>Server, every Sandbox tick (beside the quarry's): who is at the muster, who boards, and the ride's end.</summary>
    private void BreakoutTick(double now)
    {
        if (Story is not { } s || _opColony == 0 || Phase != MatchPhase.Playing || BreakoutScript.Current.Problem.Length > 0)
            return;
        if (_bkAlarmUntil > 0 && now >= _bkAlarmUntil)
        {
            _bkAlarmUntil = 0;
            if (!_ldOn && ColonyAlarmOn)
                LockdownAlarm(false); // (the siren that saw them off; a real lockdown keeps its own)
        }
        var look = now >= _bkNextLook;
        if (look)
            _bkNextLook = now + 0.1;
        if (_bkCrewOf != s || _bkRelLead == -1 && look)
            _bkRelLead = -2; // (a new story, or the lead not made yet: look again)
        var ready = BreakoutIds(s);
        var muster = false;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers)
                continue;
            if (_bkWho[p.Id] != p)
            {
                _bkWho[p.Id] = p;
                _bk[p.Id] = default;
            }
            ref var run = ref _bk[p.Id];
            if (run.RideFrom != 0)
            {
                if (ServerTick - run.RideFrom >= QuarryRideSeconds * World.TickRate)
                    BreakoutRideEnd(s, p, ref run, now);
                continue;
            }
            if (!look || !p.Alive)
            {
                muster |= run.Stage == BreakoutMuster;
                continue;
            }
            var who = StoryWho(p);
            if (who < 0)
                continue;
            if (run.Stage == BreakoutNone && ready)
            {
                var escaped = _bkQEscaped >= 0 && s.World.Quality(who, _bkQEscaped) > 0;
                // A loaded world (or a joiner): the lead without the escape is the muster again; the escape is the spaceport.
                if (escaped && BreakoutFreeOf(s, who))
                    run.Stage = BreakoutAway;
                else if (escaped)
                    run.Stage = BreakoutSpaceport;
                else if (_bkRelLead >= 0 && s.World.Get(who, _bkRelLead, _bkLead) > 0)
                    run.Stage = BreakoutMuster;
                if (run.Stage != BreakoutNone)
                    BreakoutSend(p);
            }
            if (run.Stage == BreakoutMuster)
            {
                muster = true;
                BreakoutWait(s, p, ref run, who);
            }
            else if (run.Stage == BreakoutSpaceport)
                BreakoutShipLook(s, p, ref run, who); // (GameBreakoutShip.cs)
        }
        _bkMuster = muster;
        if (look)
        {
            BreakoutCrewTick(s);
            BreakoutFightTick(s, now); // (GameBreakoutFight.cs)
        }
    }

    /// <summary>Server: the hauler at the motor pool for this player (called once, shared by everyone mustering), and boarding it.</summary>
    private void BreakoutWait(StoryEngine s, Player p, ref BreakoutRun run, int who)
    {
        if (BreakoutPool() is not { } pool || BreakoutLanding() is not { } landing)
            return;
        var bus = StandInSlotOf(run.Bus);
        if (bus < 0)
        {
            foreach (var q in Players.Values)
                if (q != p && q.Id < MaxPlayers && _bkWho[q.Id] == q && _bk[q.Id].RideFrom == 0 && StandInSlotOf(_bk[q.Id].Bus) is var k and >= 0
                    && ServerTick < StandIns[k].LeavesAt)
                {
                    run.Bus = _bk[q.Id].Bus; // (one hauler for the whole crew)
                    BreakoutSend(p);
                    return;
                }
            var way = StandInPath.Along(landing - pool, pool);
            var i = StandInCall(StandInKind.Hauler, pool, way, 10, 900, StandInFor.Duty, p.Id, 0, jump: false);
            run.Bus = i >= 0 ? StandIns[i].Id : 0;
            BreakoutSend(p);
            return;
        }
        ref var c = ref StandIns[bus];
        if (!c.Landed(ServerTick) || ServerTick >= c.LeavesAt || Vector3.Distance(p.Position, c.Land) > BreakoutBoardReach)
            return;
        // Aboard: the hauler goes, the siren starts behind them, and another comes in at the spaceport as the scene ends.
        var script = BreakoutScript.Current;
        var wayOn = StandInPath.Along(landing - c.Land, landing);
        StandInLeave(bus, 0.1);
        var j = StandInCall(StandInKind.Hauler, landing, wayOn, QuarryRideSeconds - 1, 30, StandInFor.Duty, p.Id, 1, jump: false);
        run.RideTo = j >= 0 ? StandIns[j].Id : 0;
        run.RideFrom = ServerTick;
        SandboxStand(p, teleport: false);
        if (BreakoutCrewAt(0) is var driver and >= 0 && s.Agents.Of(driver) is { Dead: false })
            s.Talk.SayFor(driver, who, script.DriverSays, s.Time, -1, -1);
        if (!_ldOn)
        {
            LockdownAlarm(true);
            _bkAlarmUntil = SandboxNow + BreakoutAlarmFor;
        }
        if (script.Memo.Length > 0)
            ServerSystemChat(script.Memo.Replace("{name}", p.Name), SandboxNow);
        Log($"[breakout] {p.Name} is away in the hauler");
        BreakoutSend(p);
        foreach (var q in Players.Values)
        {
            // Anyone else standing at it goes too (the same scene, the same hauler in).
            if (q == p || q.Bot != null || q.Id >= MaxPlayers || !q.Alive || Vector3.Distance(q.Position, c.Land) > BreakoutBoardReach)
                continue;
            if (_bkWho[q.Id] != q)
                _bkWho[q.Id] = q;
            ref var other = ref _bk[q.Id];
            if (other.RideFrom != 0)
                continue;
            (other.Stage, other.Bus, other.RideTo, other.RideFrom) = (BreakoutMuster, run.Bus, run.RideTo, run.RideFrom);
            SandboxStand(q, teleport: false);
            Log($"[breakout] {q.Name} is away in the hauler too");
            BreakoutSend(q);
        }
    }

    /// <summary>Server: the scene is over: set down at the spaceport beside the hauler, the crew round them, and the story told.</summary>
    private void BreakoutRideEnd(StoryEngine s, Player p, ref BreakoutRun run, double now)
    {
        run.RideFrom = 0;
        run.Bus = 0;
        run.Stage = BreakoutSpaceport;
        var bus = StandInSlotOf(run.RideTo);
        var at = bus >= 0 ? StandIns[bus].Land : BreakoutLanding();
        Vector3 side = Vector3.UnitX;
        if (at is { } land && p.Alive)
        {
            side = bus >= 0 ? Vector3.Cross(StandInPath.Up(land), StandIns[bus].Way) : Vector3.UnitX;
            var feet = SbScatter(land + side * 3.5f + StandInPath.Up(land) * 0.6f, 0.6f);
            p.Teleport(feet);
            p.Yaw = MathF.Atan2(side.X, -side.Z);
        }
        var who = StoryWho(p);
        if (who >= 0)
        {
            if (_bkQEscaped >= 0)
                s.World.SetQuality(who, _bkQEscaped, 1);
            if (_bkVAbscond >= 0)
                s.Record(_bkVAbscond, who, -1, -1, StoryPlace());
        }
        // The crew who are alive came too (once: a second player's ride finds them fled already).
        for (var i = 0; i < _bkCrew.Length; i++)
        {
            var e = _bkCrew[i];
            if (e < 0 || _bkQFled < 0 || s.Agents.Of(e) is not { Dead: false } || s.World.Quality(e, _bkQFled) > 0)
                continue;
            s.World.SetQuality(e, _bkQFled, 1);
            if (StoryBodyOf(e) is { Alive: true } body && at is { } l)
                body.Teleport(SbScatter(BreakoutCrewSpot(l, i), 0.5f));
        }
        Log($"[breakout] {p.Name} set down at the spaceport");
        BreakoutSend(p);
    }

    // ---- The crew ---------------------------------------------------------------------------------------------------

    private int BreakoutCrewAt(int i) => i < _bkCrew.Length ? _bkCrew[i] : -1;

    /// <summary>The crew's place round a spot: a loose half ring behind it.</summary>
    private static Vector3 BreakoutCrewSpot(Vector3 at, int i)
    {
        var a = (i - 1.5f) * 0.7f;
        return at + new Vector3(MathF.Sin(a) * 3f, 0.5f, -MathF.Cos(a) * 3f + 1f);
    }

    /// <summary>
    /// The goal of a crew member's body (from <see cref="StoryBodyGoal"/>): to the hauler while anyone musters, at the
    /// spaceport once fled; null: their own life.
    /// </summary>
    private Vector3? BreakoutGoalOf(int entity)
    {
        if (_bkCrewOf == null || _bkCrewOf != Story)
            return null;
        for (var i = 0; i < _bkCrew.Length; i++)
        {
            if (_bkCrew[i] != entity || entity < 0)
                continue;
            if (_bkQFled >= 0 && _bkCrewOf.World.Quality(entity, _bkQFled) > 0)
            {
                // Round the courier once the guards are down (or before they ever came out), else at the landing.
                if (!_bfOn && BreakoutCourier() is { } ship)
                {
                    var at = ship.WorldCom;
                    return BreakoutCrewSpot(StandInPath.Ground(at + StandInPath.Up(at) * 10f), i) + new Vector3(0, 0, -6f);
                }
                return BreakoutLanding() is { } l ? BreakoutCrewSpot(l, i) : null;
            }
            if (_bkMuster && BreakoutPool() is { } pool)
                return BreakoutCrewSpot(pool, i);
            return null;
        }
        return null;
    }

    /// <summary>Server, ten times a second: a fled convict's body far from the spaceport (a loaded world, a new body) is put there.</summary>
    private void BreakoutCrewTick(StoryEngine s)
    {
        if (_bkQFled < 0 || BreakoutLanding() is not { } l)
            return;
        for (var i = 0; i < _bkCrew.Length; i++)
        {
            var e = _bkCrew[i];
            if (e < 0 || s.World.Quality(e, _bkQFled) == 0 || StoryBodyOf(e) is not { Alive: true } body)
                continue;
            var spot = BreakoutCrewSpot(l, i);
            if (Flat2(body.Position - spot) > 150f * 150f)
                body.Teleport(SbScatter(spot, 0.5f));
        }
    }

    // ---- Places -----------------------------------------------------------------------------------------------------

    /// <summary>Where the hauler waits: the file's spot in the colony's frame, on the ground.</summary>
    private Vector3? BreakoutPool()
    {
        if (Constructs.Get(_opColony) is not { } c)
            return null;
        var at = BreakoutScript.Current.MotorPool;
        var w = c.ToWorld(new Vector3(at.X, 0, at.Y));
        return _bkPool = StandInPath.Ground(w + StandInPath.Up(w) * 30f);
    }

    /// <summary>Where the hauler sets them down: the file's spot in the spaceport's frame, on the ground.</summary>
    private Vector3? BreakoutLanding()
    {
        if (!SpaceportFrame(out var origin, out var rot))
            return null;
        var at = BreakoutScript.Current.Landing;
        var w = origin + Vector3.Transform(new Vector3(at.X, 0, at.Y), rot);
        return StandInPath.Ground(w + StandInPath.Up(w) * 30f);
    }

    /// <summary>Server: where this player's marker is: the hauler (once called), the motor pool, or the courier at the spaceport.</summary>
    private Vector3? BreakoutTargetOf(Player p)
    {
        ref var run = ref _bk[p.Id];
        if (run.Stage == BreakoutMuster)
            return StandInSlotOf(run.Bus) is var bus and >= 0 ? StandIns[bus].Land : BreakoutPool();
        if (run.Stage == BreakoutSpaceport)
        {
            foreach (var (n, b) in _cbBuilt)
                if (n.Equals(SpaceportScript.Current.Courier, StringComparison.OrdinalIgnoreCase) && Constructs.Get(b) is { } c)
                    return c.ToWorld(Vector3.Zero);
        }
        return null;
    }

    // ---- The wire (Msg.Breakout) ------------------------------------------------------------------------------------

    /// <summary>Server: their state, to them (u8 stage, u8 has a target, vec target, u32 ride tick, u16 hauler, u16 hauler). The host's own goes straight in.</summary>
    private void BreakoutSend(Player p)
    {
        var target = BreakoutTargetOf(p);
        ref var run = ref _bk[p.Id];
        var (a, b) = run.RideFrom == 0 ? ((ushort)0, (ushort)0) : ((ushort)run.Bus, (ushort)run.RideTo);
        if (p.Id == LocalId)
        {
            BreakoutHear(run.Stage, target, run.RideFrom, a, b);
            return;
        }
        if (GuidOf(p) is not { } guid)
            return;
        var w = SbW;
        w.Begin(Msg.Breakout).U8(run.Stage).U8(target != null ? (byte)1 : (byte)0).Vec(target ?? default).U32(run.RideFrom).U16(a).U16(b);
        Net.SendTo(guid, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.Breakout"/> (after its id).</summary>
    private void ReadBreakout(ref PacketReader r)
    {
        if (!r.Has(22))
            return;
        var stage = r.U8();
        var has = r.U8() != 0;
        var at = r.Vec();
        var from = r.U32();
        var a = r.U16();
        var b = r.U16();
        BreakoutHear(stage, has && float.IsFinite(at.X + at.Y + at.Z) ? at : null, from, a, b);
    }

    private void BreakoutHear(byte stage, Vector3? target, uint rideFrom, ushort a, ushort b)
    {
        if (stage != BreakoutLocal)
            BreakoutChangedAt = Brutal.GlfwApi.Glfw.Time;
        BreakoutLocal = stage;
        BreakoutHasTarget = target != null;
        BreakoutTarget = target ?? default;
        BreakoutRideFrom = rideFrom;
        (BreakoutRideBusA, BreakoutRideBusB) = (a, b);
    }
}

/// <summary>
/// Story/Opening/Breakout.json (format <see cref="Format"/>; docs/story/BREAKOUT.md): the crew, the lead that starts the
/// night, where the hauler waits and sets down, and the words. A bad file is refused with a reason and there is no break-out.
/// </summary>
public sealed class BreakoutScript
{
    public const string Format = "call-of-slop-breakout/1";
    public const int MaxCrew = 6;

    public string[] Crew = [];
    public string Lead = "The Night", Caption = "", DriverSays = "", Memo = "";
    public Vector2 MotorPool, Landing;
    public string MusterTitle = "", MusterText = "", SpaceportTitle = "", SpaceportText = "";
    public string Problem = "";

    /// <summary>A guard's name (a title gives the uniform: Cosmetics/uniforms.json) and post (the spaceport's frame, m).</summary>
    public sealed class FightPlan
    {
        public (string Name, Vector2 At)[] Guards = [];
        public float Wake = 120;
        public string Shout = "", Won = "";
        public Death[] Deaths = [];
    }

    /// <summary>A scripted death: due "after" s into the fight, or once "down" guards are, whichever is first.</summary>
    public sealed class Death
    {
        public string Who = "", Lead = "";
        public double After = 10;
        public int Down;
        public string[] Words = [];
    }

    public FightPlan Fight = new();

    /// <summary>The courier: a convict's line going aboard, each one's line once away ((name, line)), the memo, the panel's words.</summary>
    public sealed class ShipPlan
    {
        public string Aboard = "", Memo = "", Title = "", Text = "";
        public (string Name, string Line)[] Away = [];
    }

    public ShipPlan Ship = new();

    private static BreakoutScript? _current;
    public static BreakoutScript Current => _current ??= Load(Path.Combine(AppFolders.Data, "Story", "Opening", "Breakout.json"));

    public static BreakoutScript Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
        {
            Console.WriteLine($"[breakout] refused: {ex.Message}");
            return new BreakoutScript { Problem = $"Breakout.json: {ex.Message}" };
        }
    }

    public static BreakoutScript Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        Vector2 Vec(string key)
        {
            if (!root.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2)
                throw new FormatException($"\"{key}\" is [x, z]");
            return new Vector2(v[0].GetSingle(), v[1].GetSingle());
        }
        string Line(JsonElement e, string key)
        {
            var s = Str(e, key);
            if (s.Length > 110 || s.Any(ch => ch is < ' ' or > '~'))
                throw new FormatException($"\"{key}\": printable ASCII, at most 110 characters");
            return s;
        }
        var d = new BreakoutScript();
        if (!root.TryGetProperty("crew", out var crew) || crew.ValueKind != JsonValueKind.Array || crew.GetArrayLength() is 0 or > MaxCrew)
            throw new FormatException($"\"crew\": 1 to {MaxCrew} names, the driver first");
        d.Crew = [.. crew.EnumerateArray().Select(c => c.GetString() ?? "")];
        if (d.Crew.Any(c => c.Length == 0))
            throw new FormatException("\"crew\": names");
        d.Lead = Str(root, "lead");
        if (d.Lead.Length == 0)
            throw new FormatException("\"lead\": the lead that starts the night");
        d.MotorPool = Vec("motorPool");
        d.Landing = Vec("landing");
        d.Caption = Line(root, "caption");
        d.DriverSays = Line(root, "driverSays");
        d.Memo = Line(root, "memo");
        if (root.TryGetProperty("muster", out var m))
            (d.MusterTitle, d.MusterText) = (Line(m, "title"), Str(m, "text"));
        if (root.TryGetProperty("spaceport", out var sp))
            (d.SpaceportTitle, d.SpaceportText) = (Line(sp, "title"), Str(sp, "text"));
        if (root.TryGetProperty("ship", out var sh))
        {
            d.Ship.Aboard = Line(sh, "aboard");
            d.Ship.Memo = Line(sh, "memo");
            d.Ship.Title = Line(sh, "title");
            d.Ship.Text = Str(sh, "text");
            if (sh.TryGetProperty("away", out var aw))
                d.Ship.Away = [.. aw.EnumerateArray().Select(x =>
                {
                    var (who, says) = (Line(x, "who"), Line(x, "says"));
                    if (who.Length == 0 || says.Length == 0)
                        throw new FormatException("\"ship\": an \"away\" line has \"who\" and \"says\"");
                    return (who, says);
                })];
        }
        if (root.TryGetProperty("fight", out var fi))
        {
            var plan = d.Fight;
            if (!fi.TryGetProperty("guards", out var gs) || gs.ValueKind != JsonValueKind.Array || gs.GetArrayLength() is 0 or > 12)
                throw new FormatException("\"fight\": \"guards\" is 1 to 12 guards");
            plan.Guards = [.. gs.EnumerateArray().Select(g =>
            {
                var name = Line(g, "name");
                if (name.Length == 0 || !g.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.Array || at.GetArrayLength() != 2)
                    throw new FormatException("\"fight\": a guard has a \"name\" and \"at\": [x, z]");
                return (name, new Vector2(at[0].GetSingle(), at[1].GetSingle()));
            })];
            if (fi.TryGetProperty("wake", out var wk))
                plan.Wake = Math.Clamp(wk.GetSingle(), 20, 400);
            plan.Shout = Line(fi, "shout");
            plan.Won = Line(fi, "won");
            if (fi.TryGetProperty("deaths", out var ds))
                plan.Deaths = [.. ds.EnumerateArray().Select(x =>
                {
                    var death = new Death { Who = Line(x, "who"), Lead = Line(x, "lead") };
                    if (death.Who.Length == 0)
                        throw new FormatException("\"fight\": a death names \"who\"");
                    if (x.TryGetProperty("after", out var af))
                        death.After = Math.Clamp(af.GetDouble(), 0, 600);
                    if (x.TryGetProperty("down", out var dn))
                        death.Down = Math.Clamp(dn.GetInt32(), 0, 12);
                    if (!x.TryGetProperty("words", out var ws) || ws.ValueKind != JsonValueKind.Array || ws.GetArrayLength() is 0 or > 6)
                        throw new FormatException($"\"fight\": {death.Who}'s \"words\" are 1 to 6 lines");
                    death.Words = [.. ws.EnumerateArray().Select(l =>
                    {
                        var s = l.GetString() ?? "";
                        if (s.Length is 0 or > 110 || s.Any(ch => ch is < ' ' or > '~'))
                            throw new FormatException($"\"fight\": {death.Who}'s words: printable ASCII, 1 to 110 characters a line");
                        return s;
                    })];
                    return death;
                })];
        }
        return d;
    }
}
