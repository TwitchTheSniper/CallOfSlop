using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The story layer's appraiser (docs/story/STORY.md, "The appraiser"): on the host in Sandbox, moments of play become
/// narratable events in the story engine: joins and leaves, kills and deaths, constructs set down, boarded, wrecked,
/// stripped and towed, ships launched, landed and jumped, systems discovered. The constructs say what happened to them
/// in their notes (<see cref="ConstructWorld.Notes"/>, read once a tick); the rest is called from where it happens. Its
/// state is saved with the world (a Story section). Nothing here runs per tick beyond a look over the players' seats and
/// the notes; no event allocates once the story's arrays have grown.
/// </summary>
public sealed partial class Game
{
    private static StoryRules? _storyData;

    /// <summary>The story data (Story/*.json), loaded once.</summary>
    public static StoryRules StoryData => _storyData ??= StoryRules.LoadFolder();

    /// <summary>The story (the host in Sandbox; null elsewhere, and on every client).</summary>
    public StoryEngine? Story { get; private set; }

    /// <summary>A scenario playing into the live story from the Story window (null: none).</summary>
    public StoryScenarioRun? StoryPlaying { get; private set; }

    /// <summary>How long the same actor doing the same thing to the same target is one event (s): stripping a wreck part by part is one Strip.</summary>
    public const double StoryRepeatGap = 300;

    private readonly int[] _storyWho = new int[256]; // a player slot -> its entity (-1: not looked up yet)
    private readonly bool[] _storyIn = new bool[256]; // a person's Join recorded (their Leave is then)
    private readonly ushort[] _storySeat = new ushort[256]; // the body each player sat in last tick (0: none)
    private readonly (int Verb, int Actor, int Target, double At)[] _storyRecent = new (int, int, int, double)[32];
    private int _storyRecentNext;
    private int _storyPlace = -1;
    private double _storyLast = -1;
    private int _vJoin, _vLeave, _vFound, _vBoard, _vKill, _vDie, _vWreck, _vStrip, _vTow, _vLaunch, _vLand, _vJump, _vDiscover;
    private int _rOwns, _rRode, _rDiscovered;

    /// <summary>Server, every new match (from <see cref="ResetSandbox"/>): a fresh story in Sandbox, none elsewhere. A saved world's comes with its load.</summary>
    internal void StoryReset()
    {
        if (!IsServer || !SandboxOn)
        {
            Story = null;
            StoryPlaying = null;
            Constructs.NotesOn = false;
            Constructs.Notes.Clear();
            return;
        }
        var rules = StoryData;
        if (Story?.Rules != rules)
        {
            Story = new StoryEngine(rules);
            (_vJoin, _vLeave, _vFound, _vBoard, _vKill, _vDie, _vWreck) = (rules.VerbId("Join"), rules.VerbId("Leave"), rules.VerbId("Found"), rules.VerbId("Board"), rules.VerbId("Kill"), rules.VerbId("Die"), rules.VerbId("Wreck"));
            (_vStrip, _vTow, _vLaunch, _vLand, _vJump, _vDiscover) = (rules.VerbId("Strip"), rules.VerbId("Tow"), rules.VerbId("Launch"), rules.VerbId("Land"), rules.VerbId("Jump"), rules.VerbId("Discover"));
            (_rOwns, _rRode, _rDiscovered) = (rules.Relation("Owns")?.Id ?? -1, rules.Relation("Rode")?.Id ?? -1, rules.Relation("DiscoveredBy")?.Id ?? -1);
            StoryWorkIds(rules); // (the milestones: GameStoryWork.cs)
        }
        else
            Story.Clear();
        Story.Agents.PerTick = StoryThinksPerTick;
        StoryForget();
        Constructs.NotesOn = true;
        Constructs.Notes.Clear();
    }

    /// <summary>What the appraiser keeps of this session's players and places (the story's entity ids changed: a load, a reset).</summary>
    private void StoryForget()
    {
        Array.Fill(_storyWho, -1);
        Array.Clear(_storyIn);
        Array.Clear(_storySeat);
        Array.Clear(_storyRecent);
        _storyPlace = -1;
        _storyLast = -1;
        StoryPlaying = null;
        StoryWorkForget();
        StoryBodiesForget();
        StandInsForget(); // STAND-IN: the story's placeholder craft (Source/StandIns)
        StoryChatForget(); // (talking to people: GameStoryChat.cs)
        Array.Fill(_storyGroup, (-1, -1)); // (the groups: GameStoryFactions.cs)
        StoryJobsForget(); // (faction jobs: GameStoryJobs.cs)
        StoryletsForget(); // (GameStoryStorylets.cs)
        StoryTopicsForget(); // (GameStoryTopics.cs)
    }

    /// <summary>Server, the end of a Sandbox tick: the story clock, who came and who sat down, and the constructs' notes.</summary>
    internal void StoryTick(double now)
    {
        if (Story is not { } s)
            return;
        s.Advance(_storyLast < 0 ? 0 : Math.Clamp(now - _storyLast, 0, 1));
        _storyLast = now;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null)
                continue;
            if (!_storyIn[p.Id] && StoryWho(p) is var who and >= 0)
            {
                _storyIn[p.Id] = true;
                s.Record(_vJoin, who, place: StoryPlace());
            }
            ref var h = ref SandboxHands.Hand(p.Id);
            var seat = h.Seated ? h.SeatBody : (ushort)0;
            if (seat != _storySeat[p.Id])
            {
                _storySeat[p.Id] = seat;
                if (seat != 0)
                    StoryBoard(p, seat);
            }
        }
        var notes = Constructs.Notes;
        for (var i = 0; i < notes.Count; i++)
            StoryNote(notes[i]);
        notes.Clear();
        StoryWorkTick();
        StoryGroupsTick(); // (the players' groups: GameStoryFactions.cs)
        UniformsTick(now); // (who wears what: GameUniforms.cs)
        StoryJobsTick(s, now); // (faction jobs: crates, builds, couriers, the window: GameStoryJobs.cs)
        StoryBodiesTick(now); // (the people's bodies: GameStoryBodies.cs)
        OutpostsTick(s, now); // (the factions' outposts beyond the colony, and their people: GameOutposts.cs)
        StandInsTick(s, now); // STAND-IN: craft for the people's trips (Source/StandIns)
        StoryletsTick(s, now); // (the storylets' hooks, spots and talk-panel answers: GameStoryStorylets.cs)
        StoryTopicsTick(s, now); // (what each person will talk about in an open talk panel: GameStoryTopics.cs)
        StoryTalkJoinTick(); // (a joined talk that ended: GameStoryTalkJoin.cs)
        StoryPathsTick(s, now); // (each player's paths and standings for their Logbook: GameStoryPaths.cs)
        FragmentsTick(s, now); // (the data centres' terminals and what each player holds: GameStoryFragments.cs)
        LoreTick(s, now); // (the colony's things to look at, stood where they belong: GameStoryLore.cs)
        if (StoryPlaying is { } run)
        {
            run.Live(s);
            if (run.Finished)
                StoryPlaying = null;
        }
    }

    /// <summary>How many of the story's people may think in one server tick (each think is tens of microseconds).</summary>
    public const int StoryThinksPerTick = 4;

    /// <summary>Puts a cast's people in the live story (the Story window's People tab); its world place is this map. Returns how many came.</summary>
    public int StoryAddCast(CastDef cast) => Story is { } s ? s.Agents.AddCast(cast, StoryPlace(), scripted: false) : 0;

    /// <summary>Plays a scenario into the live story, an event every <see cref="StoryScenario.LiveGap"/> seconds (its entities are its own). False: no story here.</summary>
    public bool StoryPlay(StoryScenario scenario)
    {
        if (Story is not { } s)
            return false;
        StoryPlaying = new StoryScenarioRun(scenario, s);
        return true;
    }

    // ---- Who and what -------------------------------------------------------------------------------------------------

    /// <summary>A stable key from a name (a place, a person nothing better is known of).</summary>
    public static ulong StoryNameKey(string name)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in name)
            h = (h ^ char.ToLowerInvariant(ch)) * 1099511628211UL;
        return h == 0 ? 1 : h;
    }

    private static ulong StoryPersonKey(ulong worldKey, ulong steam, string name) => worldKey != 0 ? worldKey : steam != 0 ? steam : StoryNameKey("person:" + name);

    /// <summary>A player's entity (a person by their world key; a bot one of its own each time it comes). -1: not known yet (their Hello hasn't come).</summary>
    private int StoryWho(Player p)
    {
        if (Story is not { } s)
            return -1;
        var id = _storyWho[p.Id];
        if (id >= 0 && s.World.Valid(id) && s.World[id].Name == p.Name)
            return id;
        if (p.Bot != null)
            id = s.World.Create(StoryKind.Person, p.Name, 0, s.Time);
        else if (WorldKnown(p))
            id = s.World.Get(StoryKind.Player, StoryPersonKey(WorldKeyOf(p), p.SteamId, p.Name), p.Name, s.Time);
        else
            return -1;
        _storyWho[p.Id] = id;
        return id;
    }

    /// <summary>The entity for an owner or attacker byte: a player here, or a person away whose things are kept (a saved world). -1: nobody.</summary>
    private int StoryByte(byte who)
    {
        if (who == 255 || Story is not { } s)
            return -1;
        if (Players.TryGetValue(who, out var p))
            return StoryWho(p);
        if (WorldSession is { } ws)
            foreach (var rec in ws.Players)
                if (rec.Offline == who)
                    return s.World.Get(StoryKind.Player, StoryPersonKey(rec.Key, rec.SteamId, rec.Name), rec.Name, s.Time);
        return -1;
    }

    private string? StoryOwnerName(byte owner)
    {
        if (owner == 255)
            return null;
        if (Players.TryGetValue(owner, out var p))
            return p.Name;
        if (WorldSession is { } ws)
            foreach (var rec in ws.Players)
                if (rec.Offline == owner)
                    return rec.Name;
        return null;
    }

    /// <summary>This map as a place.</summary>
    private int StoryPlace()
    {
        if (Story is not { } s)
            return -1;
        if (_storyPlace < 0 || !s.World.Valid(_storyPlace))
            _storyPlace = s.World.Get(StoryKind.Place, StoryNameKey("map:" + World.Map.Name), World.Map.Name, s.Time);
        return _storyPlace;
    }

    private int StorySystem(string name) => Story is { } s ? s.World.Get(StoryKind.Place, StoryNameKey("system:" + name), name, s.Time) : -1;

    /// <summary>
    /// A construct's entity, made the first time the story meets it ("Karl's vehicle (12)", or its blueprint's name), with
    /// its owner's Owns. Its wreck pieces share it (<see cref="CNoteKind.Piece"/>). -1: no such construct.
    /// </summary>
    private int StoryConstruct(ushort body, string? what = null)
    {
        if (Story is not { } s)
            return -1;
        var id = s.World.Find(StoryKind.Construct, body);
        if (id >= 0)
            return id;
        if (Constructs.Get(body) is not { } c)
            return -1;
        var named = what;
        what ??= c.DriveSeat >= 0 ? "vehicle" : c.Anchored ? "base" : "construct";
        var owner = StoryOwnerName(c.Owner);
        id = s.World.Get(StoryKind.Construct, body, owner != null ? $"{owner}'s {what} ({body})" : $"the {what} ({body})", s.Time);
        if (StoryByte(c.Owner) is var o and >= 0 && _rOwns >= 0)
            s.World.Set(o, _rOwns, id, 1);
        if (named != null && id >= 0)
            s.Factions.Claim(id, named); // (a faction's vehicle by its name: "IT Box Truck 1" is the IT Department's)
        return id;
    }

    /// <summary>The same thing again within <see cref="StoryRepeatGap"/> (stripping part by part, coupling and uncoupling)? Remembered when not.</summary>
    private bool StoryRepeat(int verb, int actor, int target)
    {
        var now = Story!.Time;
        foreach (ref var r in _storyRecent.AsSpan())
            if (r.Verb == verb && r.Actor == actor && r.Target == target && now - r.At < StoryRepeatGap && r.At > 0)
                return true;
        _storyRecent[_storyRecentNext++ % _storyRecent.Length] = (verb, actor, target, Math.Max(now, 1e-6));
        return false;
    }

    // ---- The moments --------------------------------------------------------------------------------------------------

    /// <summary>A kill (or, the attacker the victim, a death nobody is to blame for). From <see cref="SandboxModule.OnKill"/>.</summary>
    internal void StoryKill(Player attacker, Player victim, double now)
    {
        if (Story is not { } s)
            return;
        var v = StoryWho(victim);
        var a = attacker == victim ? -1 : StoryWho(attacker);
        if (v < 0)
            return;
        if (a < 0)
            s.Record(_vDie, v, place: StoryPlace());
        else
            s.Record(_vKill, a, v, place: StoryPlace());
        StoryMoodsOnKill(s, attacker, victim, a, v, now); // (rage, panic, grief: GameStoryMoods.cs)
        if (victim.Team == SbPeopleTeam)
            OutpostHurt(victim, attacker, now, killed: true); // (an outpost's mates go for whoever did it: GameOutposts.cs)
    }

    /// <summary>A player left (from <see cref="SandboxModule.OnPlayerLeave"/>): their Leave, and the slot forgotten.</summary>
    internal void StoryLeave(Player p)
    {
        if (Story is { } s && _storyIn[p.Id] && StoryWho(p) is var who and >= 0)
            s.Record(_vLeave, who, place: StoryPlace());
        _storyWho[p.Id] = -1;
        _storyIn[p.Id] = false;
        _storySeat[p.Id] = 0;
    }

    /// <summary>A blueprint spawned by a player: its Found, named after the blueprint.</summary>
    internal void StoryFound(Player p, Construct c, string blueprint)
    {
        if (Story is not { } s || StoryWho(p) is not (var who and >= 0))
            return;
        var t = StoryConstruct(c.Id, blueprint);
        s.Record(_vFound, who, t, who, StoryPlace());
    }

    /// <summary>Sitting down: someone else's construct every time (once in <see cref="StoryRepeatGap"/>), one's own the first time.</summary>
    private void StoryBoard(Player p, ushort body)
    {
        if (Story is not { } s || Constructs.Get(body) is not { } c || StoryWho(p) is not (var who and >= 0))
            return;
        var t = StoryConstruct(body);
        if (t < 0 || c.Owner == p.Id && _rRode >= 0 && s.World.Get(who, _rRode, t) > 0 || StoryRepeat(_vBoard, who, t))
            return;
        s.Record(_vBoard, who, t, StoryByte(c.Owner), StoryPlace());
    }

    private void StoryNote(in CNote n)
    {
        var s = Story!;
        switch (n.Kind)
        {
            case CNoteKind.Gone:
                s.World.Unbind(StoryKind.Construct, n.Body);
                break;
            case CNoteKind.Piece:
                if (s.World.Find(StoryKind.Construct, n.Other) is var whole and >= 0 && s.World.Find(StoryKind.Construct, n.Body) < 0)
                    s.World.Bind(StoryKind.Construct, n.Body, whole); // (the wreck piece is its construct's remains)
                break;
            case CNoteKind.Disabled:
            {
                if (Constructs.Get(n.Body) is not { } c)
                    break;
                var actor = StoryByte(n.By);
                var owner = StoryByte(c.Owner);
                if (actor < 0 && owner < 0)
                    break; // (the map's own, falling apart by itself: nobody's story)
                s.Record(_vWreck, actor, StoryConstruct(n.Body), owner, StoryPlace());
                break;
            }
            case CNoteKind.Strip:
            {
                if (Constructs.Get(n.Body) is not { } c || StoryByte(n.By) is not (var actor and >= 0))
                    break;
                var t = StoryConstruct(n.Body);
                if (t >= 0 && (c.Owner == 255 || Factions.Shares(c.Owner, n.By)) && s.Factions.OwnerOf(t) < 0)
                    break; // (one's own or nobody's: a story only when a faction claims it)
                if (t >= 0 && !StoryRepeat(_vStrip, actor, t))
                    s.Record(_vStrip, actor, t, StoryByte(c.Owner), StoryPlace());
                break;
            }
            case CNoteKind.Made:
                StoryMade(n);
                break;
            case CNoteKind.Tow:
            {
                if (StoryByte(n.By) is not (var actor and >= 0))
                    break;
                // Whichever of the two is someone else's (both one's own: nobody's story).
                var c = Constructs.Get(n.Body);
                var towed = c != null && c.Owner != n.By && c.Owner != 255 ? c : Constructs.Get(n.Other) is { } o && o.Owner != n.By && o.Owner != 255 ? o : null;
                if (towed == null)
                    break;
                var t = StoryConstruct(towed.Id);
                if (t >= 0 && !StoryRepeat(_vTow, actor, t))
                    s.Record(_vTow, actor, t, StoryByte(towed.Owner), StoryPlace());
                break;
            }
        }
    }

    /// <summary>Who flies a ship: its driver, or else its owner.</summary>
    private int StoryPilot(Construct c) => c.Driver != 255 && StoryByte(c.Driver) is var d and >= 0 ? d : StoryByte(c.Owner);

    /// <summary>A construct left this world's sky (<paramref name="up"/>) or came back down into it (GameSpaceLaunch.cs).</summary>
    internal void StoryShip(Construct c, bool up)
    {
        if (Story is not { } s || StoryPilot(c) is not (var pilot and >= 0))
            return;
        s.Record(up ? _vLaunch : _vLand, pilot, StoryConstruct(c.Id), StoryByte(c.Owner), StoryPlace());
    }

    /// <summary>A jump drive jump to another system (GameSpaceJump.cs); the first arrival anyone made there is its discovery.</summary>
    internal void StoryJump(Construct c, string system)
    {
        if (Story is not { } s || StoryPilot(c) is not (var pilot and >= 0))
            return;
        var place = StorySystem(system);
        s.Record(_vJump, pilot, StoryConstruct(c.Id), StoryByte(c.Owner), place);
        if (_rDiscovered >= 0 && place >= 0 && s.World.Range(place, _rDiscovered) is var (a, b) && b <= a)
            s.Record(_vDiscover, pilot, place: place);
    }

    // ---- Saving -------------------------------------------------------------------------------------------------------

    /// <summary>On the tick, with the world's save: a plain copy of the story into the snapshot (its bytes are made on the save's worker). None without a story.</summary>
    private void StoryCapture(WorldSnapshot snap)
    {
        if (Story is not { } s)
        {
            snap.Story = null;
            return;
        }
        snap.Story = snap.StoryCopy ??= new StorySave();
        s.Capture(snap.Story);
    }

    /// <summary>A world's Story section. A story that doesn't read is left out (the world still loads), and starts afresh.</summary>
    private void StoryRead(SaveReader r, string world)
    {
        if (Story is not { } s)
            return;
        try
        {
            s.Read(r);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            s.Clear();
            Console.WriteLine($"[story] {world}: the story was left out: {ex.Message}");
        }
        StoryForget();
        Console.WriteLine($"[story] {world}: {s.World.Count} entities, {s.World.PairTotal} relationships, {s.Chronicle.Count} events, {s.Sifter.LiveCount} stories in progress, {s.Sifter.Done.Count} told");
    }
}
