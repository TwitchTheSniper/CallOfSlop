using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// What the story people's bodies see (docs/story/STORY.md "Sight"), host only, four times a second with the bodies.
/// A body sees what is in front of it (<see cref="StorySightCone"/> either side of where it faces) out to
/// <see cref="StorySightRange"/>, through no walls, smoke or foliage (the bots' own <see cref="BotSees"/>), and senses
/// anyone right beside it (<see cref="StoryAware"/>) whichever way it faces. What it sees, it reacts to:
///   - keep away from someone it fears or holds a grudge against, someone who hurt it lately, or an enemy bot: it backs
///     off to <see cref="StoryKeepAway"/> and says so;
///   - come and look at a stranger, or at someone lying dead: it walks over (only when free: idle, at work or at rest),
///     then goes back to what it was doing;
///   - greet someone it knows, seen again after a while: it stops, turns to them and says a word.
/// Players and other bodies are seen the same way; the people's living bodies are left to the story's own meetings.
/// The story's events go by sight too (<see cref="StoryAgents.Sees"/>): a person with a body only witnesses what they
/// could see or were close enough to hear. Nothing allocates once the bodies exist.
/// </summary>
public sealed partial class Game
{
    public const float StorySightRange = 40f, StoryCorpseRange = 25f; // m: how far a body sees someone, and someone lying down
    public const float StorySightCone = 1.2f; // rad either side of where a body faces (about 70 degrees)
    public const float StoryAware = 4f; // m: sensed whichever way they face
    public const float StoryKeepAway = 14f; // m: a wary body backs off to this far
    public const float StoryWitnessHear = 12f, StoryWitnessFar = 60f; // m: an event this close is heard; this far it can still be seen
    public const double StoryForgetSight = 60; // s unseen before someone counts as seen anew (a greeting, a look)
    public const double StoryLookGap = 45; // s between one body's walks over to look
    public const double StoryLookFor = 25, StoryLookLinger = 4, StoryWaveFor = 2.5, StoryWaryHold = 5; // s

    private enum SightReact : byte { None, Wave, Look, Wary }

    private struct StorySight
    {
        public int Entity; // the person whose body this was (a new person under the same player id starts afresh)
        public SightReact Kind;
        public byte Focus; // the player looked at
        public bool Corpse, Reached;
        public double Until, LookAgain, SeenFocus;
    }

    private readonly StorySight[] _sight = new StorySight[256]; // by the body's player id
    private readonly float[] _sightLast = new float[256 * 256]; // (body's player, player): game time last seen (0: never)
    private Func<int, int, bool>? _storySees;
    private int _cWave = -2, _cLook, _cWary, _cFound, _rKnows, _rLikes, _rGrudge, _rFears;

    /// <summary>Reactions begun this session (the window): greetings, walks over to look, and backing off.</summary>
    public int StorySightWaves, StorySightLooks, StorySightWary;

    /// <summary>A body's reaction now (the window): "greets", "looks at", "keeps away from" and who, or null.</summary>
    public string? StorySightOf(int entity)
    {
        for (var i = 0; i < _bodyCount; i++)
        {
            if (_bodies[i].Entity != entity)
                continue;
            ref readonly var v = ref _sight[_bodies[i].Player];
            if (v.Entity != entity || v.Kind == SightReact.None || !Players.TryGetValue(v.Focus, out var o))
                return null;
            return v.Kind switch { SightReact.Wave => "greets ", SightReact.Look => "looks at ", _ => "keeps away from " } + o.Name;
        }
        return null;
    }

    private void StorySightIds(StoryEngine s)
    {
        if (_cWave != -2)
            return;
        var lr = s.Rules.Lines;
        (_cWave, _cLook, _cWary, _cFound) = (lr.Concept("Wave"), lr.Concept("Look"), lr.Concept("Wary"), lr.Concept("Found"));
        var r = s.Rules;
        (_rKnows, _rLikes, _rGrudge, _rFears) = (RelId(r, "Knows"), RelId(r, "Likes"), RelId(r, "Grudge"), RelId(r, "Fears"));
    }

    /// <summary>A loaded or fresh story: the ids are looked up again and nobody has been seen.</summary>
    private void StorySightForget()
    {
        _cWave = -2;
        Array.Clear(_sight);
        Array.Clear(_sightLast);
    }

    /// <summary>
    /// From <see cref="StoryBodiesTick"/>, after a body's goal is worked out: what it sees, and its reaction, which may
    /// change the goal.
    /// </summary>
    private void StorySightLook(StoryEngine s, StoryAgent a, ref StoryBody b, Player p, double now)
    {
        StorySightIds(s);
        ref var v = ref _sight[p.Id];
        if (v.Entity != a.Entity)
        {
            v = new StorySight { Entity = a.Entity };
            Array.Clear(_sightLast, p.Id * 256, 256);
        }
        var w = s.World;
        var me = a.Entity;
        var eye = p.Eye;
        var fwd = World.FlatForward(p.Yaw);
        var nowF = (float)now;

        // Look round: who is seen, and the strongest reason to react (a threat first, then the dead, a friend, a stranger).
        SightReact pick = SightReact.None;
        byte pickId = 0;
        var pickCorpse = false;
        var pickD2 = float.MaxValue;
        foreach (var o in Players.Values)
        {
            if (o.Id == p.Id || (o.Team == SbPeopleTeam && o.Alive) || (o.Bot != null && o.Alive && !SandboxEnemies(o, p)) || (o.Bot == null && !o.Alive && o.Deaths == 0))
                continue; // (living people meet in the story; friendly bots are nobody's business; a player not in yet isn't lying anywhere)
            var corpse = !o.Alive;
            var off = o.Position - p.Position;
            var d2 = Flat2(off);
            var range = corpse ? StoryCorpseRange : StorySightRange;
            if (d2 > range * range || MathF.Abs(off.Y) > range)
                continue;
            if (!StorySightSees(eye, fwd, off, d2, o, corpse))
                continue;
            ref var last = ref _sightLast[p.Id * 256 + o.Id];
            var fresh = last == 0 || nowF - last > StoryForgetSight;
            last = nowF;
            if (v.Kind != SightReact.None && v.Focus == o.Id)
                v.SeenFocus = now;

            SightReact kind;
            if (corpse)
                kind = SightReact.Look;
            else if (o.Bot != null)
                kind = SightReact.Wary; // (an enemy bot: they hunt the people too)
            else
            {
                var who = StoryEntityOf(o);
                if (who >= 0 && (Rel(w, me, _rFears, who) >= 20 || Rel(w, me, _rGrudge, who) >= 30)
                    || b.HurtBy == o.Id + 1 || (v.Kind == SightReact.Wary && v.Focus == o.Id))
                    kind = SightReact.Wary;
                else if (!fresh)
                    continue;
                else if (who >= 0 && (Rel(w, me, _rKnows, who) > 0 || Rel(w, me, _rLikes, who) > 0))
                    kind = SightReact.Wave;
                else
                    kind = SightReact.Look; // (a stranger: come and see who it is)
            }
            if (corpse && !fresh && !(v.Kind == SightReact.Look && v.Focus == o.Id))
                continue; // (a body already looked at, or let be)
            var rank = kind == SightReact.Wary ? 3 : corpse ? 2 : kind == SightReact.Wave ? 1 : 0;
            var best = pick == SightReact.None ? -1 : pick == SightReact.Wary ? 3 : pickCorpse ? 2 : pick == SightReact.Wave ? 1 : 0;
            if (rank > best || (rank == best && d2 < pickD2))
                (pick, pickId, pickCorpse, pickD2) = (kind, o.Id, corpse, d2);
        }

        // Begin a reaction (a threat takes over from anything; the rest wait for the one under way).
        var free = StorySightFree(s, a, b);
        if (pick == SightReact.Wary && !(v.Kind == SightReact.Wary && v.Focus == pickId))
        {
            StorySightBegin(ref v, SightReact.Wary, pickId, false, now, now + StoryWaryHold);
            StorySightWary++;
            StorySightSay(s, _cWary, me, pickId, now);
        }
        else if (pick == SightReact.Wary)
            v.Until = now + StoryWaryHold; // (still in sight: still wary)
        else if (v.Kind == SightReact.None && pick == SightReact.Look && free && now >= v.LookAgain)
        {
            StorySightBegin(ref v, SightReact.Look, pickId, pickCorpse, now, now + StoryLookFor);
            v.LookAgain = now + StoryLookGap;
            StorySightLooks++;
            StorySightSay(s, _cLook, me, pickId, now);
        }
        else if (v.Kind == SightReact.None && pick == SightReact.Wave)
        {
            StorySightBegin(ref v, SightReact.Wave, pickId, false, now, now + StoryWaveFor);
            StorySightWaves++;
            StorySightSay(s, _cWave, me, pickId, now);
        }

        // Carry it on: the goal it gives, until it is over.
        if (v.Kind == SightReact.None)
            return;
        if (now > v.Until || !Players.TryGetValue(v.Focus, out var f) || (f.Alive == v.Corpse) || b.Leaving)
        {
            v.Kind = SightReact.None;
            return;
        }
        var to = f.Position - p.Position;
        var dist = MathF.Sqrt(Flat2(to));
        switch (v.Kind)
        {
            case SightReact.Wary:
                if (dist >= StoryKeepAway)
                {
                    if (now - v.SeenFocus > 1)
                        v.Kind = SightReact.None; // (far enough, and out of sight: back to it)
                    break;
                }
                var away = dist > 0.1f ? new Vector3(-to.X, 0, -to.Z) / dist : -fwd;
                b.Goal = World.ClampToArena(p.Position + away * MathF.Max(3f, StoryKeepAway - dist + 2f));
                break;
            case SightReact.Look:
                if (!free || (!v.Corpse && dist > StorySightRange))
                {
                    v.Kind = SightReact.None; // (called away, or they went off)
                    break;
                }
                var stand = v.Corpse ? 1.5f : 2.5f;
                if (dist <= stand + 1f)
                {
                    if (!v.Reached)
                    {
                        v.Reached = true;
                        v.Until = Math.Min(v.Until, now + StoryLookLinger);
                        if (v.Corpse)
                            StorySightSay(s, _cFound, me, v.Focus, now, prompted: true);
                    }
                    b.Goal = p.Position;
                }
                else
                    b.Goal = f.Position - to / dist * stand;
                break;
            case SightReact.Wave:
                if (free)
                    b.Goal = p.Position; // (a beat to greet them)
                break;
        }
    }

    private static int RelId(StoryRules r, string name) => r.Relation(name)?.Id ?? -1;

    private static int Rel(StoryWorld w, int me, int rel, int them) => rel < 0 ? 0 : w.Get(me, rel, them);

    private static void StorySightBegin(ref StorySight v, SightReact kind, byte focus, bool corpse, double now, double until)
    {
        (v.Kind, v.Focus, v.Corpse, v.Reached, v.Until, v.SeenFocus) = (kind, focus, corpse, false, until, now);
    }

    /// <summary>Free to wander over and look, or stop to greet: idle, or working or resting alone (never on the road, in a play, or meeting someone).</summary>
    private bool StorySightFree(StoryEngine s, StoryAgent a, in StoryBody b)
    {
        if (b.Leaving || s.Plays.Holds(a.Entity) || s.Plays.PartnerOf(a.Entity) >= 0)
            return false;
        if (!a.Busy)
            return true;
        var act = s.Rules.People.Actions[a.Doing.Action];
        return !act.Road && a.Doing.Target < 0 && (act.Verb == _vWork || act.Verb == _vRest);
    }

    /// <summary>Does a body with its eyes at <paramref name="eye"/>, facing <paramref name="fwd"/>, see <paramref name="o"/> (<paramref name="off"/> away)?</summary>
    private bool StorySightSees(Vector3 eye, Vector3 fwd, Vector3 off, float d2, Player o, bool corpse)
    {
        if (d2 <= StoryAware * StoryAware)
            return true; // (right beside them: heard, or caught out of the corner of an eye)
        var len = MathF.Sqrt(d2);
        if ((off.X * fwd.X + off.Z * fwd.Z) / len < MathF.Cos(StorySightCone))
            return false;
        if (corpse)
            return SightLine(eye, o.Position + new Vector3(0, 0.3f, 0)) == BotSightLine.Clear;
        return BotSees(eye, o) == BotSightLine.Clear;
    }

    /// <summary>A body's word on what it saw; <paramref name="focus"/> is the player seen (them in the line, when the story knows them).</summary>
    private void StorySightSay(StoryEngine s, int concept, int me, byte focus, double now, bool prompted = false)
    {
        if (concept < 0 || !s.Talk.On)
            return;
        var f = LineFacts.Of(me);
        var who = Players.TryGetValue(focus, out var o) ? StoryEntityOf(o) : -1;
        (f.Them, f.Actor, f.Target, f.Here) = (who, me, who, s.Agents.Where(me));
        s.Talk.Say(concept, f, s.Time, prompted);
    }

    /// <summary>Turns a reacting body to face whoever it reacts to (from <see cref="StoryBodyBot"/>; a backing-off body looks back now and then).</summary>
    private void StorySightFace(Player p, BotBrain b, double now)
    {
        ref readonly var v = ref _sight[p.Id];
        if (v.Kind == SightReact.None || !Players.TryGetValue(v.Focus, out var f))
            return;
        if (v.Kind == SightReact.Wary && ((int)(now * 0.8) & 1) == 0)
            return; // (backing off: half the time eyes on the way out)
        var d = f.Position - p.Position;
        if (Flat2(d) < 0.04f)
            return;
        b.GlanceYaw = GravityFrames.Curved ? GravityFrames.YawOf(p.Position, d) : MathF.Atan2(d.X, -d.Z);
        b.GlanceUntil = now + 0.3;
        b.NextGlance = now + 0.3; // (the corner-checking glance waits)
    }

    // ---- The story's events, by sight -----------------------------------------------------------------------------------

    /// <summary>
    /// The story's question (<see cref="StoryAgents.Sees"/>): could <paramref name="viewer"/> see or hear what
    /// <paramref name="other"/> did? True when either has no body here (the story's place decides), when they are close
    /// enough to hear it, or when the viewer has a clear line to them (whichever way they face: it turns heads).
    /// </summary>
    private bool StorySees(int viewer, int other)
    {
        if (StoryPlayerOf(viewer) is not { } pv || StoryPlayerOf(other) is not { } po || pv == po)
            return true;
        var d2 = Vector3.DistanceSquared(pv.Position, po.Position);
        if (d2 <= StoryWitnessHear * StoryWitnessHear)
            return true;
        if (d2 > StoryWitnessFar * StoryWitnessFar)
            return false;
        return SightLine(pv.Eye, po.Alive ? po.Chest : po.Position + new Vector3(0, 0.3f, 0)) == BotSightLine.Clear;
    }

    /// <summary>The player standing (or lying) for an entity here: a person's body, or a player whose Join is recorded. Null: none.</summary>
    private Player? StoryPlayerOf(int entity)
    {
        if (entity < 0)
            return null;
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Entity == entity)
                return Players.TryGetValue(_bodies[i].Player, out var p) ? p : null;
        foreach (var p in Players.Values)
            if (p.Bot == null && _storyWho[p.Id] == entity)
                return p;
        return null;
    }
}
