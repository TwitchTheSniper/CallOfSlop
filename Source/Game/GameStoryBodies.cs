using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The story's people in the world (docs/story/STORY.md "Bodies"; phase 4 of the story systems): on the host in Sandbox,
/// each person the story has in this world (In the map's place) walks about it as a bot of the people's side
/// (<see cref="SbPeopleTeam"/>), named after them. A person on the road here appears at that road's end a little before
/// the trip is over and walks in; one setting off walks out to the road's end and goes. A step they take is acted out:
/// work at the work spot, rest at home, and a step with someone in mind next to them, which doesn't end until the two
/// bodies have met (<see cref="StoryAgents.Meet"/>). The story's clock still says how long a step takes; the body only
/// shows it. A body killed is the person killed, for good (Karl, 2026-11-04: the appraiser's Kill reaches
/// <see cref="StoryAgents.Die"/>), and a dead person has no body again.
/// People fight nobody (<see cref="SandboxEnemies"/>): anyone may hurt them but friendly bots, and enemy bots hunt them as
/// they hunt everyone. Bodies aren't saved: a loaded world puts the people back where the story says they are. At most
/// <see cref="StoryBodiesMax"/>, looked at four times a second; nothing here allocates once the bodies exist.
/// </summary>
public sealed partial class Game
{
    /// <summary>The story people's team (the Sandbox's sides: GameSandboxSpawns.cs).</summary>
    public const byte SbPeopleTeam = 12;

    public const int StoryBodiesMax = 8;
    public const float StoryMeetReach = 4f, StoryBeside = 1.3f; // m: two bodies this close are together; one goes this far from the other
    public const float StoryHomeMin = 12, StoryHomeMax = 26, StoryWorkMin = 6, StoryWorkMax = 10, StoryRoadMin = 40, StoryRoadMax = 50; // m from the place's middle
    public const double StoryArriveWalk = 20; // s: a trip here shows its body at the road's end this long before it ends
    public const double StoryBodiesEvery = 0.25; // s between looks

    /// <summary>The host's switch (the Story window's People tab): off, the people have no bodies and steps keep the story's clock alone.</summary>
    public bool StoryBodiesOn = true;

    private struct StoryBody
    {
        public int Entity;
        public byte Player;
        public Vector3 Home, Work, Goal, RoadEnd;
        public int RoadTo; // the place RoadEnd leads to (0: not worked out yet)
        public bool Leaving, LivesHere;
        public double SpokeFor; // the step (its start on the story clock) whose line was said (GameStoryTalk.cs)
        public double HurtSaidAt; // story clock
        public short HurtBy; // hurt since the last look: the player id + 1 (0: not)
    }

    private readonly StoryBody[] _bodies = new StoryBody[StoryBodiesMax];
    private int _bodyCount;
    private double _bodiesAt;
    private Vector3 _storyMiddle;
    private bool _storyMiddleOk;
    private Map? _storyMiddleOf;
    private Func<int, int, bool>? _storyMeet;
    private int _vWork = -2, _vRest = -2;

    /// <summary>Bodies made, gone (walked off or put away) and killed this session (the window).</summary>
    public int StoryBodiesMade, StoryBodiesGone;

    /// <summary>How many people have a body now.</summary>
    public int StoryBodyCount => _bodyCount;

    /// <summary>A person's body (its player), or null.</summary>
    public Player? StoryBodyOf(int entity)
    {
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Entity == entity)
                return Players.TryGetValue(_bodies[i].Player, out var p) ? p : null;
        return null;
    }

    /// <summary>Where a person's body is going (the window), or null.</summary>
    public System.Numerics.Vector3? StoryBodyGoalOf(int entity)
    {
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Entity == entity)
                return _bodies[i].Goal;
        return null;
    }

    /// <summary>A loaded or fresh story: the bodies are forgotten (their players are put away at the next look).</summary>
    private void StoryBodiesForget()
    {
        _bodyCount = 0;
        _bodiesAt = 0;
    }

    /// <summary>Server, from <see cref="StoryTick"/>: four times a second, who has a body, and where each body is going.</summary>
    private void StoryBodiesTick(double now)
    {
        if (Story is not { } s || now < _bodiesAt)
            return;
        _bodiesAt = now + StoryBodiesEvery;
        var ag = s.Agents;
        var on = StoryBodiesOn && Phase == MatchPhase.Playing && StoryMiddle();
        ag.Meet = on ? _storyMeet ??= StoryMeet : null;
        if (_vWork == -2)
            (_vWork, _vRest) = (s.Rules.VerbId("Work"), s.Rules.VerbId("Rest"));
        var place = StoryPlace();
        var clock = s.Time; // (the story's clock: a step's times are on it, not the game's)
        var removed = false;

        // Bodies whose player went (killed and taken away, cleared) are forgotten; those whose person isn't here any more go.
        for (var i = _bodyCount - 1; i >= 0; i--)
        {
            ref var b = ref _bodies[i];
            var a = ag.Of(b.Entity);
            if (!Players.TryGetValue(b.Player, out var p) || p.Team != SbPeopleTeam || a == null)
            {
                StoryBodyDrop(i);
                continue;
            }
            if (!p.Alive)
                continue; // (lying there: the spawns tick takes it away, GameSandboxSpawns.cs)
            var leaving = StoryLeaving(s, a, place);
            b.Leaving = leaving;
            var gone = !on || a.Dead || !(ag.Where(a.Entity) == place || StoryArriving(s, a, place, clock) || leaving);
            if (!gone && leaving && Flat2(p.Position - StoryRoadEnd(s, ref b, a.Doing.Place)) < 3f * 3f)
                gone = true; // (at the road's end: off they go)
            if (gone)
            {
                Console.WriteLine($"[story] {p.Name}: the body goes ({(a.Dead ? "dead" : !on ? "bodies off" : leaving ? "off by the road" : "no longer here")})");
                StoryBodyRemove(p);
                StoryBodyDrop(i);
                removed = true;
            }
        }

        // Players of the people's side that no body names (a load, a reset): put away.
        foreach (var p in Players.Values)
            if (p.Team == SbPeopleTeam && p.SbSpawned && StoryBodyIndex(p.Id) < 0)
            {
                StoryBodyRemove(p);
                removed = true;
                break; // (the roster changed under the loop: the rest at the next look)
            }

        // People here (or nearly here) with no body get one, one a look.
        if (on && _bodyCount < StoryBodiesMax && place >= 0)
            foreach (var a in ag.All)
            {
                if (a.Dead || StoryBodyIndexOf(a.Entity) >= 0)
                    continue;
                var here = ag.Where(a.Entity) == place;
                var arriving = !here && StoryArriving(s, a, place, clock);
                if (!here && !arriving)
                    continue;
                if (StoryBodySpawn(s, a, place, arriving) != null)
                    removed = true; // (the roster changed: it is sent below)
                break;
            }
        if (removed)
            BroadcastRoster();

        // Where each body is going.
        for (var i = 0; i < _bodyCount; i++)
        {
            ref var b = ref _bodies[i];
            if (ag.Of(b.Entity) is { } a && Players.TryGetValue(b.Player, out var p) && p.Alive)
                b.Goal = StoryBodyGoal(s, a, ref b, p, place);
        }
        StoryTalkTick(s, now); // (what they say, and who hears it: GameStoryTalk.cs)
    }

    private static float Flat2(Vector3 v) => v.X * v.X + v.Z * v.Z;

    /// <summary>On the road away from here (they set off from this place).</summary>
    private static bool StoryLeaving(StoryEngine s, StoryAgent a, int place) =>
        a.Busy && a.DoingFromPlace == place && s.Rules.People.Actions[a.Doing.Action].Road;

    /// <summary>On the road here, with the trip nearly over (their body walks in from the road's end).</summary>
    private static bool StoryArriving(StoryEngine s, StoryAgent a, int place, double now) =>
        a.Busy && a.Doing.Place == place && a.DoingFromPlace >= 0 && a.DoingFromPlace != place && s.Rules.People.Actions[a.Doing.Action].Road
        && a.DoingUntil - now <= StoryArriveWalk;

    private int StoryBodyIndex(byte player)
    {
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Player == player)
                return i;
        return -1;
    }

    private int StoryBodyIndexOf(int entity)
    {
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Entity == entity)
                return i;
        return -1;
    }

    private void StoryBodyDrop(int i)
    {
        _bodies[i] = _bodies[--_bodyCount];
        _bodies[_bodyCount] = default;
    }

    /// <summary>A body goes (walked off, put away): its player leaves the game, with no Leave for the story (their trip is the record).</summary>
    private void StoryBodyRemove(Player p)
    {
        _storyIn[p.Id] = false; // (never set for a body: StoryLeave records nothing)
        Players.Remove(p.Id);
        OnModePlayerLeave(p);
        StoryBodiesGone++;
    }

    // ---- Places on the map ----------------------------------------------------------------------------------------------

    /// <summary>The middle of this world's place: the parts rack, else the first spawn, on the ground. False: nowhere to stand (no bodies).</summary>
    private bool StoryMiddle()
    {
        if (_storyMiddleOf == World.Map)
            return _storyMiddleOk;
        _storyMiddleOf = World.Map;
        var sb = World.Map.Sandbox;
        var at = sb is { RackParts.Count: > 0 } ? sb.Rack : World.Map.Spawns.Count > 0 ? World.Map.Spawns[0] : Vector3.Zero;
        _storyMiddleOk = SandboxSpawnSpot(at + new Vector3(0, 0.5f, 0), out _storyMiddle) == null;
        if (!_storyMiddleOk)
            Console.WriteLine($"[story] {World.Map.Name}: nowhere for the people's bodies to stand near {at}; they stay in the story only");
        return _storyMiddleOk;
    }

    private static ulong StoryMix(ulong x)
    {
        x ^= x >> 33;
        x *= 0xFF51AFD7ED558CCDUL;
        x ^= x >> 33;
        x *= 0xC4CEB9FE1A85EC53UL;
        return x ^ (x >> 33);
    }

    /// <summary>A spot from <paramref name="min"/> to <paramref name="max"/> m from the middle, the same for the same key: the first of a few that a body can stand on.</summary>
    private Vector3 StorySpot(ulong key, float min, float max)
    {
        for (var tries = 0; tries < 10; tries++)
        {
            key = StoryMix(key + (ulong)tries * 0x9E3779B97F4A7C15UL);
            var angle = (key & 0xFFFF) / 65536f * MathF.Tau;
            var r = (min + ((key >> 16) & 0xFFFF) / 65536f * (max - min)) * (tries < 5 ? 1f : 0.6f); // (then nearer in)
            var at = _storyMiddle + new Vector3(MathF.Sin(angle) * r, 0, MathF.Cos(angle) * r);
            if (Vector3.DistanceSquared(World.ClampToArena(at), at) > 0.01f)
                continue; // (off the map)
            if (World.Map.Terrain != null)
                at.Y = World.Map.GroundHeight(at.X, at.Z);
            if (SandboxSpawnSpot(at + new Vector3(0, 0.5f, 0), out var feet) == null)
                return feet;
        }
        return _storyMiddle;
    }

    /// <summary>Where the road to (or from) another place meets this one: the same spot for the same place (kept by the body).</summary>
    private Vector3 StoryRoadEnd(StoryEngine s, ref StoryBody b, int other)
    {
        if (b.RoadTo != other + 1)
            (b.RoadTo, b.RoadEnd) = (other + 1, StorySpot(StoryNameKey(s.World.NameOf(other)) ^ 0x524F4144UL, StoryRoadMin, StoryRoadMax));
        return b.RoadEnd;
    }

    // ---- Coming and going ---------------------------------------------------------------------------------------------

    /// <summary>A body for a person here (at the spot their step is at) or arriving (at the road's end), or null when there's no room.</summary>
    private Player? StoryBodySpawn(StoryEngine s, StoryAgent a, int place, bool arriving)
    {
        if (FreeBotIds() <= 0)
            return null; // (one id is always kept for a person joining)
        byte id = 0;
        for (var k = 1; k < MaxPlayers; k++)
            if (!Players.ContainsKey((byte)k))
            {
                id = (byte)k;
                break;
            }
        if (id == 0)
            return null;
        var w = s.World;
        var name = w.NameOf(a.Entity);
        var key = StoryNameKey(name);
        var b = new StoryBody
        {
            Entity = a.Entity,
            Player = id,
            Home = StorySpot(key ^ 0x484F4D45UL, StoryHomeMin, StoryHomeMax),
            SpokeFor = a.Busy ? a.DoingFrom : -1, // (a step already under way when they appear isn't said again)
            HurtSaidAt = -1e9,
        };
        StoryNearForget(id);
        var p = s.Rules.People;
        b.LivesHere = p.Home >= 0 && w.Get(a.Entity, p.Home, place) > 0;
        var works = p.WorksAt >= 0 && w.Get(a.Entity, p.WorksAt, place) > 0;
        if (works)
        {
            // One work spot for the place, everyone at it a little to their own side.
            var spot = StorySpot(StoryNameKey(w.NameOf(place)) ^ 0x574F524BUL, StoryWorkMin, StoryWorkMax);
            var turn = (key & 0xFFFF) / 65536f * MathF.Tau;
            b.Work = spot + new Vector3(MathF.Sin(turn), 0, MathF.Cos(turn)) * 1.8f;
        }
        else
            b.Work = b.Home;
        var at = arriving ? StoryRoadEnd(s, ref b, a.DoingFromPlace) : StoryStepSpot(s, a, ref b);
        var bot = new Player
        {
            Id = id,
            Name = name, // (as the story calls them: StoryWho knows the body by its name)
            Bot = new BotBrain(BotDifficulty.Easy, BotPersonality.Anchor, _rng.Next()),
            Team = SbPeopleTeam,
            SbSpawned = true, // (never respawns: killed, the body goes and the person is dead)
        };
        Players[id] = bot;
        Spawn(bot, ServerTick, Brutal.GlfwApi.Glfw.Time);
        var feet = SbScatter(at, 1.5f);
        bot.Teleport(feet);
        bot.SbHome = feet;
        bot.Yaw = bot.RenderYaw = (float)((key >> 40) & 0xFFFF) / 65536f * MathF.Tau;
        for (var i = 0; i < Player.HistorySize; i++)
            bot.History[i] = new HistoryEntry { Position = feet, Yaw = bot.Yaw };
        SandboxBotGun(bot, SbLoadKind.Class, (byte)WeaponClass.Pistol); // (a sidearm they never draw on anyone)
        b.Goal = feet;
        _bodies[_bodyCount++] = b;
        _storyWho[id] = a.Entity;
        _storyIn[id] = false;
        StoryBodiesMade++;
        Console.WriteLine($"[story] {name} {(arriving ? "arrives by the road" : "is here")}: a body (player {id}) at {feet.X:0},{feet.Z:0} (home {b.Home.X:0},{b.Home.Z:0}, work {b.Work.X:0},{b.Work.Z:0}; the middle {_storyMiddle.X:0},{_storyMiddle.Z:0})");
        return bot;
    }

    /// <summary>Where a person's step happens: work at the work spot, rest at home, else where they are now (their own spot, when new).</summary>
    private Vector3 StoryStepSpot(StoryEngine s, StoryAgent a, ref StoryBody b)
    {
        if (!a.Busy)
            return b.LivesHere ? b.Home : b.Work; // (idle: at home, or about their work when they live elsewhere)
        var verb = s.Rules.People.Actions[a.Doing.Action].Verb;
        if (verb == _vWork)
            return b.Work;
        return verb == _vRest ? b.Home : b.Goal == default ? b.Home : b.Goal;
    }

    /// <summary>Where a body is going now: out by the road, in from it, to whoever their step is with, or to their step's spot.</summary>
    private Vector3 StoryBodyGoal(StoryEngine s, StoryAgent a, ref StoryBody b, Player p, int place)
    {
        if (b.Leaving)
            return StoryRoadEnd(s, ref b, a.Doing.Place);
        var with = s.Plays.PartnerOf(a.Entity); // (in a play: beside whoever they are with in it)
        if (with < 0 && a.Busy && a.Doing.Target >= 0 && !s.Rules.People.Actions[a.Doing.Action].Road)
            with = a.Doing.Target;
        if (with >= 0)
        {
            // Beside the one they're with (on this side of them): a body, or a player.
            if (StoryPosOf(with) is { } them)
            {
                var off = new Vector3(p.Position.X - them.X, 0, p.Position.Z - them.Z);
                var len = off.Length();
                return len < StoryBeside ? p.Position : them + off / len * StoryBeside;
            }
            return p.Position;
        }
        if (a.Busy && s.Rules.People.Actions[a.Doing.Action].Road)
            return b.Home; // (arriving: in from the road's end towards home)
        return StoryStepSpot(s, a, ref b);
    }

    /// <summary>Where an entity stands in this world: a person's body, or a player here. Null: nowhere to be seen.</summary>
    private Vector3? StoryPosOf(int entity)
    {
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Entity == entity)
                return Players.TryGetValue(_bodies[i].Player, out var p) && p.Alive ? p.Position : null;
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Alive && _storyWho[p.Id] == entity)
                return p.Position;
        return null;
    }

    /// <summary>The story's question (<see cref="StoryAgents.Meet"/>): are these two together, or is either nowhere to be seen?</summary>
    private bool StoryMeet(int a, int b)
    {
        if (StoryPosOf(a) is not { } pa || StoryPosOf(b) is not { } pb)
            return true;
        return Flat2(pa - pb) <= StoryMeetReach * StoryMeetReach;
    }

    // ---- Each body's brain (from SandboxBotTick) ------------------------------------------------------------------------

    /// <summary>
    /// A person's body, before the bot tick: it walks (never sprints) to its goal and stands there; it hears and sees no
    /// fights (nothing to go and look at), and has nobody to shoot.
    /// </summary>
    private void StoryBodyBot(Player p, BotBrain b, double now)
    {
        var i = StoryBodyIndex(p.Id);
        var goal = i >= 0 ? _bodies[i].Goal : p.SbHome;
        b.HeardTime = b.LastSeenTime = -100;
        b.Target = byte.MaxValue;
        b.ForcedGoal = Flat2(goal - p.Position) < 1.2f * 1.2f ? p.Position : goal;
        b.HoldUntil = now + 1; // (a hold walks, and the stuck watch leaves a body standing at its spot alone)
        b.HoldAnchor = goal;
        StoryBodyFace(p, b, now); // (turned to whoever they talk to, and stopped for a player: GameStoryChat.cs)
    }
}
