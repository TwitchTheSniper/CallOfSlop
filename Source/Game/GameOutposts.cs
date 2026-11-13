using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The factions out in the world (docs/story/OUTPOSTS.md; data in Story/Outposts): small places beyond the colony, each a
/// faction's, with a building or two and a few people at posts or on patrol. Host only, in a story world.
/// <list type="bullet">
/// <item>The buildings are built once, the first time the story comes to the world (an old save gains them then), and are
/// the faction's own (wrecking one is a deed against it); a story Thing per outpost notes it, so nothing is built twice.</item>
/// <item>The people have bodies only while a player is within the file's "wake" of the outpost, and lose them once nobody is
/// within its "sleep": a far outpost costs one distance check per player four times a second, and nothing else. A
/// patrol comes back where its clock says it has got to. At most <see cref="OutpostBodiesMax"/> bodies in all.</item>
/// <item>Each person is a story Person of the faction, so killing one is the faction's "Killed our staff". What they say
/// to a player who comes up depends on the player's standing tier with the faction: the lowest tier (and anyone the
/// colony's lockdown is after, at the Ministry's) is warned and then shot at; whoever hurts one has the outpost on them.</item>
/// <item>The talk panel works on them: hello by standing, and the place, themselves, advice and news from the file.</item>
/// </list>
/// Nothing here allocates in a tick once the bodies exist; no wire change (bodies are players, buildings constructs, lines chat).
/// </summary>
public sealed partial class Game
{
    public const int OutpostBodiesMax = 8;
    public const double OutpostEvery = 0.25; // s between looks
    public const float OutpostGreetReach = 12f, OutpostHostileReach = 30f, OutpostChaseMost = 60f, OutpostIdleReach = 25f;
    public const double OutpostGreetGap = 240, OutpostWarnFor = 3, OutpostFightFor = 45, OutpostHurtGap = 3;

    /// <summary>The host's switch: off, the outposts' people have no bodies (their buildings stay).</summary>
    public bool OutpostsOn = true;

    private struct OutpostGuard
    {
        public int Site, Person, Entity;
        public byte Player;
        public int Leg; // a patrol's next spot
        public double StandUntil; // at a patrol's spot until then (0: not there yet)
        public byte Foe; // the player id + 1 they fight (0: nobody)
        public double FightAt, FightUntil; // the fight starts at FightAt (after the warning) and is over at FightUntil
        public double HurtSaidAt, FaceUntil;
        public byte FaceTo, TalkWith; // player ids + 1
        public double TalkUntil;
    }

    private sealed class OutpostPlace
    {
        public bool Ok;
        public Vector3 Middle, Up, Fwd, Right;
        public Vector3[] Posts = [];
        public Vector3[][] Routes = [];
        public Vector3[] Faces = [];
        public double[] DeadUntil = [];
        public int Faction = -1;
        public bool Awake;
        public double QuietSince = -1, IdleAt;
        public float Reach; // m: how far its people go from the middle
    }

    private readonly OutpostGuard[] _ouGuards = new OutpostGuard[OutpostBodiesMax];
    private int _ouGuardCount;
    private OutpostPlace[] _ouPlaces = [];
    private OutpostData? _ouData;
    private Map? _ouMap;
    private StoryEngine? _ouFor;
    private ushort _ouColony;
    private bool _ouSet;
    private double _ouAt;
    private readonly List<(int Site, int Building)> _ouToBuild = new(16);
    private readonly double[] _ouGreeted = new double[OutpostData.MaxSites * 256];
    private ulong _ouRng = 0x2545F4914F6CDD1DUL;

    /// <summary>This session: bodies made, lines said, buildings built (the check).</summary>
    public int OutpostBodiesMade, OutpostLinesSaid, OutpostBuilt;

    public int OutpostCount => _ouPlaces.Length;
    public int OutpostBodyCount => _ouGuardCount;
    public bool OutpostPlaced(int i) => (uint)i < (uint)_ouPlaces.Length && _ouPlaces[i].Ok;
    public Vector3 OutpostMiddle(int i) => _ouPlaces[i].Middle;
    public bool OutpostAwake(int i) => _ouPlaces[i].Awake;
    public int OutpostsToBuild => _ouToBuild.Count;
    public string OutpostName(int i) => _ouData is { } d && (uint)i < (uint)d.Sites.Length ? d.Sites[i].Name : "";

    /// <summary>A body of an outpost's person: their index in <see cref="_ouGuards"/>, or -1.</summary>
    private int OutpostGuardIndex(byte player)
    {
        for (var i = 0; i < _ouGuardCount; i++)
            if (_ouGuards[i].Player == player)
                return i;
        return -1;
    }

    /// <summary>Is this player an outpost's person (the people's side, but not one of the story's bodies)?</summary>
    public bool OutpostPerson(byte player) => OutpostGuardIndex(player) >= 0;

    private float OutpostRand()
    {
        _ouRng ^= _ouRng << 13;
        _ouRng ^= _ouRng >> 7;
        _ouRng ^= _ouRng << 17;
        return (_ouRng >> 40) / 16777216f;
    }

    private string? OutpostPick(string[] lines) => lines.Length == 0 ? null : lines[(int)(OutpostRand() * lines.Length) % lines.Length];

    /// <summary>Server, from <see cref="StoryTick"/>: four times a second, the outposts of this world.</summary>
    private void OutpostsTick(StoryEngine s, double now)
    {
        if (now < _ouAt)
            return;
        _ouAt = now + OutpostEvery;
        if (!ReferenceEquals(_ouFor, s) || _ouMap != World.Map || (_ouSet && _ouColony != _opColony))
            OutpostsForget(s);
        if (!_ouSet)
        {
            if (Phase != MatchPhase.Playing || _landing != null || WorldSession is not (null or { Applied: true }))
                return;
            OutpostsSetUp(s);
            if (!_ouSet)
                return;
        }
        if (_ouToBuild.Count > 0)
            OutpostBuildNext(s);
        if (_ouData is not { } data)
            return;

        // Bodies whose player went (killed and taken away) are forgotten; one killed marks their post empty for a while.
        var roster = false;
        for (var i = _ouGuardCount - 1; i >= 0; i--)
        {
            ref var g = ref _ouGuards[i];
            if (Players.TryGetValue(g.Player, out var p) && p.Team == SbPeopleTeam && p.Alive)
                continue;
            if (p != null && !p.Alive && _ouPlaces[g.Site].DeadUntil[g.Person] < now)
            {
                _ouPlaces[g.Site].DeadUntil[g.Person] = now + data.Sites[g.Site].Relief;
                Log($"[outpost] {p.Name} of {data.Sites[g.Site].Name} was killed: relieved in {data.Sites[g.Site].Relief:0} s at the soonest");
            }
            if (p == null || p.Team != SbPeopleTeam)
                OutpostDrop(i);
        }

        var on = OutpostsOn && StoryBodiesOn && Phase == MatchPhase.Playing;
        var spawned = false;
        for (var k = 0; k < _ouPlaces.Length; k++)
        {
            var pl = _ouPlaces[k];
            if (!pl.Ok)
                continue;
            var near = on ? OutpostNearest(pl.Middle) : float.MaxValue;
            var wake = data.Wake + pl.Reach;
            var sleep = data.Sleep + pl.Reach;
            if (near <= wake * wake)
            {
                if (!pl.Awake)
                    Log($"[outpost] {data.Sites[k].Name} wakes: a player {MathF.Sqrt(near):0} m off");
                pl.Awake = true;
                pl.QuietSince = -1;
            }
            else if (pl.Awake && near > sleep * sleep)
            {
                if (pl.QuietSince < 0)
                    pl.QuietSince = now;
                else if (now - pl.QuietSince > 3)
                {
                    pl.Awake = false;
                    roster |= OutpostSleep(k);
                    Log($"[outpost] {data.Sites[k].Name} sleeps: nobody within {sleep:0} m");
                }
            }
            if (pl.Awake && !spawned && _ouGuardCount < OutpostBodiesMax)
                for (var j = 0; j < pl.Posts.Length; j++)
                {
                    if (pl.DeadUntil[j] > now || OutpostHasBody(k, j))
                        continue;
                    if (OutpostSpawn(s, data, k, j, now))
                        spawned = roster = true;
                    break;
                }
        }
        if (roster)
            BroadcastRoster();
        if (_ouGuardCount > 0)
            OutpostPeopleLook(s, data, now);
    }

    /// <summary>The nearest living player's distance squared from a spot (bodies don't count).</summary>
    private float OutpostNearest(Vector3 at)
    {
        var best = float.MaxValue;
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Alive && Vector3.DistanceSquared(p.Position, at) is var d && d < best)
                best = d;
        return best;
    }

    private bool OutpostHasBody(int site, int person)
    {
        for (var i = 0; i < _ouGuardCount; i++)
            if (_ouGuards[i].Site == site && _ouGuards[i].Person == person)
                return true;
        return false;
    }

    private void OutpostDrop(int i)
    {
        _ouGuards[i] = _ouGuards[--_ouGuardCount];
        _ouGuards[_ouGuardCount] = default;
    }

    /// <summary>An outpost nobody is near: its people's bodies go. True: the roster changed.</summary>
    private bool OutpostSleep(int site)
    {
        var any = false;
        for (var i = _ouGuardCount - 1; i >= 0; i--)
        {
            if (_ouGuards[i].Site != site)
                continue;
            if (Players.TryGetValue(_ouGuards[i].Player, out var p))
            {
                _storyIn[p.Id] = false;
                Players.Remove(p.Id);
                OnModePlayerLeave(p);
                any = true;
            }
            OutpostDrop(i);
        }
        return any;
    }

    /// <summary>A new story or world: the outposts are found again (their bodies are put away by the story's own look: GameStoryBodies.cs).</summary>
    private void OutpostsForget(StoryEngine s)
    {
        _ouFor = s;
        _ouMap = World.Map;
        _ouSet = false;
        _ouGuardCount = 0;
        _ouPlaces = [];
        _ouToBuild.Clear();
        Array.Clear(_ouGreeted);
    }

    // ---- Where they are -------------------------------------------------------------------------------------------------

    /// <summary>Server, once a world: where each outpost is (level ground near its spot), and what is to be built.</summary>
    private void OutpostsSetUp(StoryEngine s)
    {
        _ouData = OutpostData.For(World.Map.Name);
        var data = _ouData;
        if (data == null)
        {
            _ouSet = true;
            return;
        }
        var colony = _opColony != 0 && Constructs.Get(_opColony) != null;
        if (!colony && data.Sites.Any(st => st.FromColony) && _opOn)
            return; // (the colony is still going up: wait for it)
        _ouSet = true;
        _ouColony = _opColony;
        _ouPlaces = new OutpostPlace[data.Sites.Length];
        for (var k = 0; k < data.Sites.Length; k++)
        {
            var st = data.Sites[k];
            var pl = _ouPlaces[k] = new OutpostPlace { Faction = s.Factions.Find(st.Faction) };
            if (pl.Faction < 0)
                Log($"[outpost] {st.Name}: no faction called {st.Faction}; its people belong to nobody");
            if (!OutpostFind(st, colony, out pl.Middle, out var flat))
            {
                Log($"[outpost] {st.Name}: nowhere to put it ({(st.FromColony ? "no colony on this world" : "not a planetoid")})");
                continue;
            }
            pl.Ok = true;
            pl.Up = StandInPath.Up(pl.Middle);
            var towards = st.FromColony ? _opGate - pl.Middle : OutpostNorth(pl.Middle);
            pl.Fwd = StandInPath.Along(towards, pl.Middle);
            pl.Right = Vector3.Normalize(Vector3.Cross(pl.Fwd, pl.Up));
            var n = st.People.Length;
            (pl.Posts, pl.Routes, pl.Faces, pl.DeadUntil) = (new Vector3[n], new Vector3[n][], new Vector3[n], new double[n]);
            for (var j = 0; j < n; j++)
            {
                var person = st.People[j];
                pl.Posts[j] = OutpostAt(pl, person.PostX, person.PostZ);
                var a = person.Face * MathF.PI / 180f;
                pl.Faces[j] = pl.Fwd * MathF.Cos(a) + pl.Right * MathF.Sin(a);
                pl.Routes[j] = new Vector3[person.Route.Length];
                for (var r = 0; r < person.Route.Length; r++)
                {
                    pl.Routes[j][r] = OutpostAt(pl, person.Route[r].X, person.Route[r].Z);
                    pl.Reach = MathF.Max(pl.Reach, Vector3.Distance(pl.Routes[j][r], pl.Middle));
                }
            }
            var key = StoryNameKey("outpost:" + st.Name);
            var fresh = s.World.Find(StoryKind.Thing, key) < 0;
            for (var b = 0; b < st.Buildings.Length; b++)
                _ouToBuild.Add((k, fresh ? b : -1 - b)); // (an old outpost's buildings are only found again and claimed)
            if (fresh)
                s.World.Get(StoryKind.Thing, key, st.Name, s.Time);
            Log($"[outpost] {st.Name} ({st.Faction}) at {pl.Middle.X:0},{pl.Middle.Y:0},{pl.Middle.Z:0}{(st.FromColony ? $", {Vector3.Distance(pl.Middle, _opGate):0} m from the colony's gate" : "")}, ground {flat:0.0} m out of level; {n} people{(fresh ? $", {st.Buildings.Length} buildings to build" : "")}");
        }
    }

    private static Vector3 OutpostNorth(Vector3 at)
    {
        var basis = GravityFrames.Basis(at);
        return new Vector3(-basis.M31, -basis.M32, -basis.M33);
    }

    /// <summary>A spot in an outpost's frame (m to its right, and towards its facing), on the ground.</summary>
    private static Vector3 OutpostAt(OutpostPlace pl, float x, float z) =>
        StandInPath.Ground(pl.Middle + pl.Right * x + pl.Fwd * z + pl.Up * 40f);

    /// <summary>
    /// Where an outpost goes: its spot, or the nearest of a few round it (a little round by bearing, a little nearer or
    /// further) where the ground within its "flat" is most nearly level. The same ground gives the same spot every load.
    /// </summary>
    private bool OutpostFind(OutpostSite st, bool colony, out Vector3 spot, out float flat)
    {
        spot = default;
        flat = float.MaxValue;
        Vector3 Candidate(float turn, float far)
        {
            if (st.FromColony)
            {
                var middle = _opGate;
                var up = StandInPath.Up(middle);
                var east = StandInPath.Along(Vector3.Cross(Vector3.UnitY, up), middle);
                var north = Vector3.Cross(up, east);
                var a = (st.Bearing + turn) * MathF.PI / 180f;
                return StandInPath.Ground(middle + (north * MathF.Cos(a) + east * MathF.Sin(a)) * st.Distance * far + up * 80f);
            }
            var p = World.Map.Planetoid!;
            double lat = st.Lat * Math.PI / 180, lon = (st.Lon + turn * 0.05) * Math.PI / 180;
            var dir = new Vector3((float)(Math.Cos(lat) * Math.Sin(lon)), (float)Math.Sin(lat), (float)(Math.Cos(lat) * Math.Cos(lon)));
            return p.GroundUnder(p.Center + dir * (float)(p.Radius + p.MaxHeight + 50));
        }
        if (st.FromColony ? !colony : World.Map.Planetoid == null)
            return false;
        foreach (var far in new[] { 1f, 0.92f, 1.08f })
            foreach (var turn in new[] { 0f, 4f, -4f, 8f, -8f })
            {
                var at = Candidate(turn, far);
                var level = OutpostLevel(at, st.Flat);
                if (level < flat - 0.05f)
                    (spot, flat) = (at, level);
                if (flat <= 1f)
                    return true;
            }
        return true;
    }

    /// <summary>How far out of level the ground is round a spot: the highest and lowest of eight points on a ring, against the middle.</summary>
    private static float OutpostLevel(Vector3 at, float radius)
    {
        var up = StandInPath.Up(at);
        var e = StandInPath.Along(Vector3.Cross(Vector3.UnitY, up), at);
        var n = Vector3.Cross(up, e);
        float lo = 0, hi = 0;
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.PI / 4;
            var g = StandInPath.Ground(at + (e * MathF.Cos(a) + n * MathF.Sin(a)) * radius + up * 40f);
            var h = Vector3.Dot(g - at, up);
            (lo, hi) = (MathF.Min(lo, h), MathF.Max(hi, h));
        }
        return hi - lo;
    }

    /// <summary>One building a look: built (a fresh outpost), or found again where it stands; either way the faction's own.</summary>
    private void OutpostBuildNext(StoryEngine s)
    {
        var (k, b) = _ouToBuild[0];
        _ouToBuild.RemoveAt(0);
        var st = _ouData!.Sites[k];
        var pl = _ouPlaces[k];
        var build = b >= 0;
        var def = st.Buildings[build ? b : -1 - b];
        var ground = OutpostAt(pl, def.X, def.Z);
        Construct? c = null;
        foreach (var o in Constructs.Bodies)
            if ((o.Anchored || o.Frozen) && Vector3.DistanceSquared(o.Pos, ground) < SettlementNear * SettlementNear)
                c = o;
        if (c == null && build)
        {
            if (ConstructBuild.FindBlueprint(def.Blueprint) is not { } bp)
            {
                Log($"[outpost] {st.Name}: no blueprint called {def.Blueprint}");
                return;
            }
            var basis = GravityFrames.Basis(ground);
            var r = new Vector3(basis.M11, basis.M12, basis.M13);
            var north = new Vector3(-basis.M31, -basis.M32, -basis.M33);
            var yaw = MathF.Atan2(Vector3.Dot(pl.Fwd, r), Vector3.Dot(pl.Fwd, north)) + def.Yaw * MathF.PI / 180f; // (yaw 0: its front, -z, to the outpost's facing)
            c = ConstructBuild.BuildOnGround(Constructs, bp, ground, yaw, out var why);
            if (c == null)
            {
                Log($"[outpost] {st.Name}: the {def.Blueprint} wasn't built: {why}");
                return;
            }
            if (!c.Anchored)
            {
                c.Frozen = true;
                c.Rebuild();
            }
            OutpostBuilt++;
            Log($"[outpost] {st.Name}: built the {def.Blueprint} ({c.PartCount} parts)");
        }
        if (c != null && pl.Faction >= 0)
            s.Factions.ClaimFor(pl.Faction, StoryConstruct(c.Id, def.Blueprint));
    }

    // ---- Their bodies ---------------------------------------------------------------------------------------------------

    /// <summary>A body for an outpost's person: at their post, or where their patrol's clock has got to.</summary>
    private bool OutpostSpawn(StoryEngine s, OutpostData data, int site, int person, double now)
    {
        var id = FreeNpcId();
        if (id == 0)
            return false;
        var st = data.Sites[site];
        var pl = _ouPlaces[site];
        var who = st.People[person];
        var route = pl.Routes[person];
        var leg = 0;
        var at = pl.Posts[person];
        if (route.Length > 1)
        {
            // Where the patrol has got to: legs walked at a stroll with a stand at each spot, round and round.
            var lap = 0.0;
            for (var r = 0; r < route.Length; r++)
                lap += Vector3.Distance(route[r], route[(r + 1) % route.Length]) / 1.6 + who.Stand;
            var t = now % Math.Max(1, lap);
            for (leg = 0; leg < route.Length; leg++)
            {
                var len = Vector3.Distance(route[leg], route[(leg + 1) % route.Length]) / 1.6 + who.Stand;
                if (t < len)
                    break;
                t -= len;
            }
            leg %= route.Length;
            at = route[leg];
            leg = (leg + 1) % route.Length;
        }
        var feet = OutpostFeet(at);
        var bot = new Player
        {
            Id = id,
            Name = who.Name,
            Bot = new BotBrain(BotDifficulty.Easy, BotPersonality.Anchor, _rng.Next()),
            Team = SbPeopleTeam,
            SbSpawned = true,
        };
        Players[id] = bot;
        Spawn(bot, ServerTick, Brutal.GlfwApi.Glfw.Time);
        bot.Teleport(feet);
        bot.SbHome = feet;
        bot.Yaw = bot.RenderYaw = GravityFrames.YawOf(feet, pl.Faces[person]);
        for (var i = 0; i < Player.HistorySize; i++)
            bot.History[i] = new HistoryEntry { Position = feet, Yaw = bot.Yaw };
        SandboxBotGun(bot, SbLoadKind.Class, (byte)WeaponClass.Pistol);
        // Their story person: the same one every time they come, the faction's (so killing them is a deed against it).
        var entity = s.World.Get(StoryKind.Person, StoryNameKey("outpost:" + st.Name + ":" + who.Name), who.Name, s.Time);
        if (pl.Faction >= 0)
            s.Factions.Enlist(entity, pl.Faction);
        _storyWho[id] = entity;
        _storyIn[id] = false;
        StoryNearForget(id);
        _ouGuards[_ouGuardCount++] = new OutpostGuard { Site = site, Person = person, Entity = entity, Player = id, Leg = leg, HurtSaidAt = -1e9 };
        OutpostBodiesMade++;
        Log($"[outpost] {who.Name} of {st.Name}: a body (player {id}) {(route.Length > 1 ? $"on patrol, to spot {leg + 1} of {route.Length}" : "at their post")}");
        return true;
    }

    /// <summary>Where a body stands at a spot: on the ground, or on top of a pad or a deck built there (an outpost's forecourt).</summary>
    private Vector3 OutpostFeet(Vector3 at)
    {
        var up = StandInPath.Up(at);
        if (Constructs.Raycast(at + up * 3f, -up, 3.6f, out var hit) && Vector3.Dot(hit.Normal, up) > 0.9f && !Constructs.Raycast(at + up * (3.1f - hit.Distance), up, 1.8f, out _))
            return at + up * (3f - hit.Distance + 0.02f);
        return at + up * 0.02f;
    }

    /// <summary>
    /// Four times a second, with bodies about: who comes up to them (a word by standing; the lowest tier warned, then shot
    /// at), fights that are over, now and then a line to pass the time.
    /// </summary>
    private void OutpostPeopleLook(StoryEngine s, OutpostData data, double now)
    {
        for (var i = 0; i < _ouGuardCount; i++)
        {
            ref var g = ref _ouGuards[i];
            if (!Players.TryGetValue(g.Player, out var me) || !me.Alive)
                continue;
            if (g.Foe > 0 && (!Players.TryGetValue((byte)(g.Foe - 1), out var foe) || !foe.Alive || now > g.FightUntil
                || Vector3.DistanceSquared(foe.Position, me.Position) > OutpostChaseMost * OutpostChaseMost))
                g.Foe = 0; // (the fight is over: gone, dead, outrun, or quiet long enough)
            if (g.TalkWith > 0 && (now > g.TalkUntil || !Players.TryGetValue((byte)(g.TalkWith - 1), out var talker)
                || Vector3.DistanceSquared(talker.Position, me.Position) > StoryTalkLeave * StoryTalkLeave))
                g.TalkWith = 0;
        }
        for (var k = 0; k < _ouPlaces.Length; k++)
        {
            var pl = _ouPlaces[k];
            if (!pl.Awake)
                continue;
            var st = data.Sites[k];
            foreach (var h in Players.Values)
            {
                if (h.Bot != null || !h.Alive || h.Id >= 256)
                    continue;
                // The nearest of this outpost's people to them.
                int best = -1;
                var bestD = float.MaxValue;
                for (var i = 0; i < _ouGuardCount; i++)
                    if (_ouGuards[i].Site == k && Players.TryGetValue(_ouGuards[i].Player, out var gp) && gp.Alive
                        && Vector3.DistanceSquared(gp.Position, h.Position) is var d && d < bestD)
                        (best, bestD) = (i, d);
                if (best < 0)
                    continue;
                var band = OutpostBand(s, pl, h);
                var reach = band == 0 ? OutpostHostileReach : OutpostGreetReach;
                if (bestD > reach * reach)
                    continue;
                ref var g = ref _ouGuards[best];
                if (band == 0 && g.Foe != h.Id + 1)
                {
                    // The lowest standing (or wanted by the colony): warned, and everyone here goes for them.
                    var said = OutpostSay(ref g, st, OutpostPick(st.Lines.Hostile) ?? "You! Stop right there!", h, now);
                    for (var i = 0; i < _ouGuardCount; i++)
                        if (_ouGuards[i].Site == k && _ouGuards[i].Foe == 0)
                            OutpostFight(ref _ouGuards[i], h, now + (said ? OutpostWarnFor : 0), now);
                    _ouGreeted[k * 256 + h.Id] = now;
                    continue;
                }
                if (band == 0)
                {
                    g.FightUntil = Math.Max(g.FightUntil, now + 10); // (still about: the fight goes on)
                    continue;
                }
                if (now - _ouGreeted[k * 256 + h.Id] < OutpostGreetGap && _ouGreeted[k * 256 + h.Id] != 0)
                    continue;
                _ouGreeted[k * 256 + h.Id] = now;
                var lines = band switch { 1 => st.Lines.Cold, 3 => st.Lines.Friendly, _ => st.Lines.Neutral };
                OutpostSay(ref g, st, OutpostPick(lines.Length > 0 ? lines : st.Lines.Neutral), h, now);
            }
            // Now and then a line to pass the time, with somebody near enough to hear it.
            if (now >= pl.IdleAt)
            {
                pl.IdleAt = now + 50 + OutpostRand() * 40;
                if (st.Lines.Idle.Length == 0)
                    continue;
                for (var i = 0; i < _ouGuardCount; i++)
                {
                    ref var g = ref _ouGuards[i];
                    if (g.Site != k || g.Foe > 0 || g.TalkWith > 0 || !Players.TryGetValue(g.Player, out var me) || !me.Alive
                        || OutpostNearest(me.Position) > OutpostIdleReach * OutpostIdleReach)
                        continue;
                    OutpostSay(ref g, st, OutpostPick(st.Lines.Idle), null, now);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// A player's standing with an outpost's faction, as a band: 0 its lowest tier (or Notorious, or wanted by the colony's
    /// lockdown, for the Ministry), 1 the next, 2 the middle (where a stranger starts), 3 above.
    /// </summary>
    private int OutpostBand(StoryEngine s, OutpostPlace pl, Player h)
    {
        if (pl.Faction < 0)
            return 2;
        var def = s.Factions.Def(pl.Faction);
        if (def.Name == "Ministry of Slop" && LockdownWanted(h))
            return 0;
        var who = StoryWho(h);
        if (who < 0)
            return 2;
        var tier = def.TierOf(s.Factions.Standing(pl.Faction, s.Factions.Party(who)));
        return tier <= 0 ? 0 : tier == 1 ? 1 : tier == 2 ? 2 : 3;
    }

    private void OutpostFight(ref OutpostGuard g, Player foe, double at, double now)
    {
        g.Foe = (byte)(foe.Id + 1);
        g.FightAt = at;
        g.FightUntil = Math.Max(g.FightUntil, now + OutpostFightFor);
    }

    /// <summary>A line said aloud ({name}: the player's, {site}: the outpost's), and turned to whoever it is for. False: nothing to say.</summary>
    private bool OutpostSay(ref OutpostGuard g, OutpostSite st, string? line, Player? to, double now)
    {
        if (line == null || !Players.TryGetValue(g.Player, out var me))
            return false;
        if (line.Contains('{'))
            line = line.Replace("{name}", to?.Name ?? "you").Replace("{site}", st.Name);
        StorySpeak(me, line, now);
        OutpostLinesSaid++;
        if (to != null)
        {
            g.FaceTo = (byte)(to.Id + 1);
            g.FaceUntil = now + StoryFaceFor;
        }
        return true;
    }

    /// <summary>Server, from DamagePlayer: an outpost's person was hurt. They say so, and everyone at the outpost turns on whoever did it.</summary>
    internal void OutpostHurt(Player victim, Player by, double now, bool killed = false)
    {
        if (by == victim || OutpostGuardIndex(victim.Id) is not (var i and >= 0) || _ouData is not { } data)
            return;
        ref var g = ref _ouGuards[i];
        var st = data.Sites[g.Site];
        if (!killed && now - g.HurtSaidAt > OutpostHurtGap)
        {
            g.HurtSaidAt = now;
            OutpostSay(ref g, st, OutpostPick(st.Lines.Hurt) ?? "Ow! Right, that's it!", by, now);
        }
        if (by.Team == SbPeopleTeam)
            return;
        for (var k = 0; k < _ouGuardCount; k++)
            if (_ouGuards[k].Site == g.Site && Players.TryGetValue(_ouGuards[k].Player, out var mate) && mate.Alive
                && Vector3.DistanceSquared(mate.Position, victim.Position) < OutpostChaseMost * OutpostChaseMost)
                OutpostFight(ref _ouGuards[k], by, now, now);
    }

    /// <summary>The people's side's question (SandboxEnemies): an outpost's person attacks only whoever they fight, once the warning is over.</summary>
    private bool OutpostFightsWith(Player person, Player foe)
    {
        var i = OutpostGuardIndex(person.Id);
        return i >= 0 && _ouGuards[i].Foe == foe.Id + 1 && SandboxNow >= _ouGuards[i].FightAt;
    }

    /// <summary>
    /// An outpost person's brain, before the bot tick (from SandboxBotTick). False: not an outpost's. In a fight they go for
    /// their foe (the bot's own aim does the rest); talking, they stand and face the player; otherwise they keep their post,
    /// facing out, or walk their patrol at a stroll, standing a while at each spot.
    /// </summary>
    private bool OutpostBot(Player p, BotBrain b, double now)
    {
        var i = OutpostGuardIndex(p.Id);
        if (i < 0)
            return false;
        ref var g = ref _ouGuards[i];
        var pl = _ouPlaces[g.Site];
        if (g.Foe > 0 && now >= g.FightAt && Players.TryGetValue((byte)(g.Foe - 1), out var foe) && foe.Alive)
        {
            b.HoldUntil = 0;
            b.ForcedGoal = foe.Position;
            return true;
        }
        b.HeardTime = b.LastSeenTime = -100;
        b.Target = byte.MaxValue;
        var route = pl.Routes[g.Person];
        var goal = pl.Posts[g.Person];
        Vector3? face = null;
        if (g.TalkWith > 0 && Players.TryGetValue((byte)(g.TalkWith - 1), out var talker))
            (goal, face) = (p.Position, talker.Position);
        else if (g.Foe > 0 && Players.TryGetValue((byte)(g.Foe - 1), out var warned))
            (goal, face) = (p.Position, warned.Position); // (the warning: they stand and stare)
        else if (route.Length > 1)
        {
            goal = route[g.Leg];
            if (Vector3.DistanceSquared(goal, p.Position) < 1.5f * 1.5f)
            {
                if (g.StandUntil == 0)
                    g.StandUntil = now + _ouData!.Sites[g.Site].People[g.Person].Stand;
                else if (now >= g.StandUntil)
                {
                    g.StandUntil = 0;
                    g.Leg = (g.Leg + 1) % route.Length;
                    goal = route[g.Leg];
                }
            }
        }
        if (face == null && g.FaceTo > 0 && now < g.FaceUntil && Players.TryGetValue((byte)(g.FaceTo - 1), out var heard))
            face = heard.Position;
        var there = Vector3.DistanceSquared(goal, p.Position) < 1.2f * 1.2f;
        b.ForcedGoal = there ? p.Position : goal;
        b.HoldUntil = now + 1;
        b.HoldAnchor = goal;
        if (face == null && there && (route.Length <= 1 || g.StandUntil > 0))
            face = p.Position + pl.Faces[g.Person] * 10f; // (at their post: looking out)
        if (face is { } at && Vector3.DistanceSquared(at, p.Position) > 0.04f)
        {
            b.GlanceYaw = GravityFrames.YawOf(p.Position, at - p.Position);
            b.NextGlance = b.GlanceUntil = now + 0.5;
        }
        return true;
    }

    // ---- Talking to them ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server, from <see cref="StoryTalkAct"/>: a player asks an outpost's person something. False: not one of theirs.
    /// Hello goes by standing; the place, themselves, advice and news come from the file. Someone they mean to shoot gets a snub.
    /// </summary>
    private bool OutpostTalk(Player p, byte bodyPlayer, TalkAsk ask, out string? why)
    {
        why = null;
        if (OutpostGuardIndex(bodyPlayer) is not (var i and >= 0) || _ouData is not { } data || Story is not { } s)
            return false;
        ref var g = ref _ouGuards[i];
        if (!Players.TryGetValue(bodyPlayer, out var body) || !body.Alive)
        {
            why = ask == TalkAsk.Bye ? null : "nobody to talk to";
            return true;
        }
        var now = SandboxNow;
        if (ask == TalkAsk.Bye)
        {
            if (g.TalkWith == p.Id + 1)
                g.TalkWith = 0;
            return true;
        }
        if (ask == TalkAsk.Join)
        {
            why = "nothing to listen in on";
            return true;
        }
        if (Vector3.DistanceSquared(body.Position, p.Position) > (StoryTalkReach + 1) * (StoryTalkReach + 1))
        {
            why = "too far away to talk";
            return true;
        }
        var st = data.Sites[g.Site];
        var pl = _ouPlaces[g.Site];
        var band = OutpostBand(s, pl, p);
        g.TalkWith = (byte)(p.Id + 1);
        g.TalkUntil = now + StoryTalkHold;
        var person = st.People[g.Person];
        var line = band == 0 ? OutpostPick(st.Lines.Snub) ?? OutpostPick(st.Lines.Hostile) : ask switch
        {
            TalkAsk.Open => null,
            TalkAsk.Greet => OutpostPick(band switch { 1 => st.Lines.Cold, 3 => st.Lines.Friendly, _ => st.Lines.Neutral }) ?? OutpostPick(st.Lines.Neutral),
            TalkAsk.Place => OutpostPick(st.Lines.Place) ?? st.About,
            TalkAsk.Self => OutpostPick(person.Self) ?? OutpostPick(st.Lines.Self) ?? person.About,
            TalkAsk.Advice => OutpostPick(st.Lines.Advice) ?? "Keep your head down.",
            TalkAsk.News => OutpostPick(st.Lines.News) ?? "Nothing to report.",
            TalkAsk.Trade => "I'm not a shop.",
            _ => "No idea what you're on about.",
        };
        if (line is { Length: > 0 })
            OutpostSay(ref g, st, line, p, now);
        StoryTalkAsks++;
        return true;
    }

    // ---- The check (--outpost-check) ----------------------------------------------------------------------------------

    /// <summary>The look, for the check's timing: everything a far outpost costs.</summary>
    internal void OutpostTickForCheck(double now)
    {
        _ouAt = 0;
        if (Story is { } s)
            OutpostsTick(s, now);
    }

    /// <summary>The check: a player's standing with a faction set outright.</summary>
    internal void OutpostStandingForCheck(Player p, string faction, int standing)
    {
        if (Story is not { } s || s.Factions.Find(faction) is not (var f and >= 0) || StoryWho(p) is not (var who and >= 0))
            return;
        s.Factions.SetStandingForCheck(f, s.Factions.Party(who), standing);
    }

    /// <summary>Each outpost's people with bodies now (the check), as lines.</summary>
    internal void OutpostDescribe(List<string> into)
    {
        for (var i = 0; i < _ouGuardCount; i++)
        {
            ref var g = ref _ouGuards[i];
            if (Players.TryGetValue(g.Player, out var p))
                into.Add($"{p.Name} of {OutpostName(g.Site)}: {Vector3.Distance(p.Position, _ouPlaces[g.Site].Middle):0} m from its middle{(g.Foe > 0 ? $", fighting player {g.Foe - 1}" : "")}{(_ouPlaces[g.Site].Routes[g.Person].Length > 1 ? $", patrol spot {g.Leg + 1}" : "")}");
        }
    }
}
