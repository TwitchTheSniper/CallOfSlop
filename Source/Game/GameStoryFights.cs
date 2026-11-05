using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The story people's fights (docs/story/STORY.md "Fights"): a person's body that is hurt fights back or runs, by who they
/// are (Brave, Vengeful, how they feel about whoever hurt them, how hurt they are), and calls out; the people who hear
/// it (within <see cref="StoryHearing"/>) come to help someone they like, run, or carry on. A fighter may attack only
/// their foe (<see cref="StoryFightsWith"/>, read by <see cref="SandboxEnemies"/>), so the ordinary bot fighting does
/// the rest; a runner sprints away from them. Each of it is a story verb the game records when it happens: Attack (the
/// first hurt, once in <see cref="StoryRepeatGap"/>), Fight, Flee and Defend. Host only, four times a second with the
/// bodies; nothing allocates.
/// </summary>
public sealed partial class Game
{
    public const byte StoryCalm = 0, StoryFighting = 1, StoryFleeing = 2;
    public const float StoryFightAt = 50; // a person's fight score at least this: they fight back (else they run)
    public const float StoryHelpAt = 45, StoryRunAt = 35; // a bystander who hears a call: helps someone they like from this, runs below that
    public const float StoryHelpLikes = 20; // a bystander helps only someone they like at least this much
    public const float StoryFightJitter = 8; // each person's score moves this much either way (the same for the same foe)
    public const float StoryFightHurt = 0.35f; // below this share of their health a person's score drops by StoryFightHurtCost
    public const float StoryFightHurtCost = 30;
    public const double StoryFightCalm = 20, StoryFleeCalm = 12; // s since the foe last hurt anyone here before a fight, a flight, is over
    public const float StoryFightFar = 60, StoryFleeSafe = 30, StoryFleeRun = 25, StoryBystanderRun = 25; // m

    private int _vAttack = -2, _vFight, _vFlee, _vDefend;
    private int _qBrave, _qVengeful, _fRelGrudge, _fRelFears, _fRelLikes;
    private int _cFight = -1, _cFlee = -1, _cHelp = -1;

    /// <summary>Fights the people began, ran from and joined this session (the window).</summary>
    public int StoryFightsBegun, StoryFlights, StoryDefends;

    /// <summary>Does this person's body fight <paramref name="foe"/> now? (<see cref="SandboxEnemies"/>: a person may attack only their foe.)</summary>
    private bool StoryFightsWith(Player person, Player foe)
    {
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Player == person.Id)
                return _bodies[i].Fight == StoryFighting && _bodies[i].Foe == foe.Id;
        return false;
    }

    /// <summary>What a person's body is doing in a fight (the window): calm, fighting or fleeing, and whom.</summary>
    public (byte State, int Foe) StoryFightOf(int entity)
    {
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Entity == entity)
                return (_bodies[i].Fight, _bodies[i].FoeEntity);
        return (StoryCalm, -1);
    }

    private void StoryFightIds(StoryEngine s)
    {
        var r = s.Rules;
        (_vAttack, _vFight, _vFlee, _vDefend) = (r.VerbId("Attack"), r.VerbId("Fight"), r.VerbId("Flee"), r.VerbId("Defend"));
        (_qBrave, _qVengeful) = (r.Quality("Brave")?.Id ?? -1, r.Quality("Vengeful")?.Id ?? -1);
        (_fRelGrudge, _fRelFears, _fRelLikes) = (r.Relation("Grudge")?.Id ?? -1, r.Relation("Fears")?.Id ?? -1, r.Relation("Likes")?.Id ?? -1);
        (_cFight, _cFlee, _cHelp) = (r.Lines.Concept("Fight"), r.Lines.Concept("Flee"), r.Lines.Concept("Help"));
    }

    /// <summary>
    /// How ready a person is to fight <paramref name="foe"/>: Brave counts most, then Vengeful and a grudge against them;
    /// fear of them takes it away, and so does being badly hurt. Helping someone, liking them adds.
    /// </summary>
    private float StoryFightScore(StoryWorld w, int me, int foe, Player body, int helping)
    {
        var score = 0.7f * Q(w, me, _qBrave) + 0.3f * Q(w, me, _qVengeful);
        if (foe >= 0)
            score += 0.3f * Math.Min(R(w, me, _fRelGrudge, foe), 100) - 0.5f * Math.Min(R(w, me, _fRelFears, foe), 100);
        if (helping >= 0)
            score += 0.2f * Math.Min(R(w, me, _fRelLikes, helping), 100);
        if (body.Health < World.MaxHealth * StoryFightHurt)
            score -= StoryFightHurtCost;
        var h = StoryMix((ulong)(uint)me << 32 ^ (uint)foe ^ 0x46494748UL);
        return score + ((h & 0xFFFF) / 65535f * 2 - 1) * StoryFightJitter;

        static float Q(StoryWorld w, int id, int q) => q >= 0 ? w.Quality(id, q) : 0;
        static float R(StoryWorld w, int id, int rel, int to) => rel >= 0 ? w.Get(id, rel, to) : 0;
    }

    /// <summary>From <see cref="StoryBodiesTick"/>, before the talk: who was hurt and by whom, what each does about it, and fights that are over.</summary>
    private void StoryFightsTick(StoryEngine s, double now)
    {
        if (_vAttack == -2)
            StoryFightIds(s);
        var w = s.World;
        var ag = s.Agents;
        var clock = s.Time;
        var place = StoryPlace();
        for (var i = 0; i < _bodyCount; i++)
        {
            ref var b = ref _bodies[i];
            if (ag.Of(b.Entity) is not { Dead: false } || !Players.TryGetValue(b.Player, out var p) || !p.Alive)
                continue;

            // Hurt since the last look: by whom, and (the first time, or by someone new) what they do about it.
            if (b.HurtBy > 0 && Players.TryGetValue((byte)(b.HurtBy - 1), out var by) && by != p && by.Alive && by.Team != SbPeopleTeam
                && StoryWho(by) is var foe and >= 0)
            {
                if (_vAttack >= 0 && !StoryRepeat(_vAttack, foe, b.Entity))
                    s.Record(_vAttack, foe, b.Entity, place: place);
                if (b.Fight != StoryCalm && b.Foe == by.Id)
                {
                    b.FightUntil = now + (b.Fight == StoryFighting ? StoryFightCalm : StoryFleeCalm);
                    if (b.Fight == StoryFighting && StoryFightScore(w, b.Entity, foe, p, -1) < StoryFightAt)
                        StoryFightStart(s, ref b, p, by, foe, StoryFleeing, -1, clock, now); // (too hurt to go on: they run)
                }
                else if (b.Fight != StoryFleeing) // (someone running keeps running; a fighter shot by someone else turns on them)
                {
                    var fight = StoryFightScore(w, b.Entity, foe, p, -1) >= StoryFightAt;
                    StoryFightStart(s, ref b, p, by, foe, fight ? StoryFighting : StoryFleeing, -1, clock, now);
                    StoryFightCall(s, i, by, foe, clock, now);
                }
            }

            // Over: the foe gone, dead, far off, or quiet for a while.
            if (b.Fight == StoryCalm)
                continue;
            if (!Players.TryGetValue(b.Foe, out var f) || !f.Alive || StoryEntityOf(f) != b.FoeEntity)
            {
                StoryFightEnd(ref b, p, "their foe is gone");
                continue;
            }
            var d2 = Flat2(f.Position - p.Position);
            if (b.Fight == StoryFighting && d2 > StoryFightFar * StoryFightFar)
                StoryFightEnd(ref b, p, "their foe got away");
            else if (now >= b.FightUntil && (b.Fight == StoryFighting || d2 > StoryFleeSafe * StoryFleeSafe))
                StoryFightEnd(ref b, p, b.Fight == StoryFighting ? "it went quiet" : "safe");
        }
    }

    /// <summary>A body begins to fight or run from <paramref name="by"/> (helping <paramref name="helping"/>, when not -1): recorded, and said.</summary>
    private void StoryFightStart(StoryEngine s, ref StoryBody b, Player p, Player by, int foe, byte how, int helping, double clock, double now)
    {
        var (was, wasFoe) = (b.Fight, b.FoeEntity);
        (b.Fight, b.Foe, b.FoeEntity) = (how, by.Id, foe);
        b.FightUntil = now + (how == StoryFighting ? StoryFightCalm : StoryFleeCalm);
        if (was == how && wasFoe == foe)
            return;
        var place = StoryPlace();
        int verb, concept;
        if (how == StoryFleeing)
            (verb, concept) = (_vFlee, _cFlee);
        else if (helping >= 0)
            (verb, concept) = (_vDefend, _cHelp);
        else
            (verb, concept) = (_vFight, _cFight);
        if (verb >= 0 && !StoryRepeat(verb, b.Entity, foe))
            s.Record(verb, b.Entity, foe, owner: helping, place: place);
        if (how == StoryFleeing)
            StoryFlights++;
        else if (helping >= 0)
            StoryDefends++;
        else
            StoryFightsBegun++;
        Console.WriteLine($"[story] {p.Name} {(how == StoryFleeing ? "runs from" : helping >= 0 ? $"helps {s.World.NameOf(helping)} against" : "fights back against")} {by.Name}");
        if (concept < 0)
            return;
        var lf = LineFacts.Of(b.Entity);
        (lf.Them, lf.Actor, lf.Target, lf.Owner, lf.Here) = (foe, foe, helping >= 0 ? helping : b.Entity, helping, s.Agents.Where(b.Entity));
        if (s.Talk.Say(concept, lf, clock, prompted: true) != null)
            b.HurtSaidAt = clock; // (instead of the Hurt line this look)
    }

    private static void StoryFightEnd(ref StoryBody b, Player p, string why)
    {
        Console.WriteLine($"[story] {p.Name}: the fight is over ({why})");
        b.Fight = StoryCalm;
        b.Foe = 0;
        b.FoeEntity = -1;
    }

    /// <summary>
    /// The one hurt calls out: everyone else with a body who hears it and isn't in a fight already helps them (likes them
    /// and is ready to fight <paramref name="by"/>), runs (not brave enough, and near the shooting), or carries on.
    /// </summary>
    private void StoryFightCall(StoryEngine s, int hurt, Player by, int foe, double clock, double now)
    {
        var w = s.World;
        var victim = _bodies[hurt].Entity;
        if (!Players.TryGetValue(_bodies[hurt].Player, out var vp))
            return;
        for (var i = 0; i < _bodyCount; i++)
        {
            ref var b = ref _bodies[i];
            if (i == hurt || b.Fight != StoryCalm || s.Agents.Of(b.Entity) is not { Dead: false } || !Players.TryGetValue(b.Player, out var p) || !p.Alive)
                continue;
            if (Flat2(p.Position - vp.Position) > StoryHearing * StoryHearing)
                continue;
            var likes = _fRelLikes >= 0 ? w.Get(b.Entity, _fRelLikes, victim) : 0;
            var score = StoryFightScore(w, b.Entity, foe, p, likes >= StoryHelpLikes ? victim : -1);
            if (likes >= StoryHelpLikes && score >= StoryHelpAt)
                StoryFightStart(s, ref b, p, by, foe, StoryFighting, victim, clock, now);
            else if (score < StoryRunAt && Flat2(p.Position - by.Position) < StoryBystanderRun * StoryBystanderRun)
                StoryFightStart(s, ref b, p, by, foe, StoryFleeing, -1, clock, now);
        }
    }

    /// <summary>
    /// A body in a fight (from <see cref="StoryBodyBot"/>): a fighter is an ordinary bot whose only enemy is their foe,
    /// heading for them when they are out of sight; a runner sprints away from them. False: not in a fight.
    /// </summary>
    private bool StoryFightBot(Player p, BotBrain bot, int i, double now)
    {
        if (i < 0 || _bodies[i].Fight == StoryCalm || !Players.TryGetValue(_bodies[i].Foe, out var foe))
            return false;
        bot.HoldUntil = 0; // (no hold: they may run)
        if (_bodies[i].Fight == StoryFighting)
        {
            bot.ForcedGoal = foe.Position;
            return true;
        }
        bot.HeardTime = bot.LastSeenTime = -100;
        bot.Target = byte.MaxValue;
        var away = new Vector3(p.Position.X - foe.Position.X, 0, p.Position.Z - foe.Position.Z);
        var len = away.Length();
        away = len > 0.01f ? away / len : new Vector3(MathF.Sin(p.Yaw), 0, -MathF.Cos(p.Yaw));
        var to = World.ClampToArena(p.Position + away * StoryFleeRun);
        if (World.Map.Terrain != null)
            to.Y = World.Map.GroundHeight(to.X, to.Z);
        bot.ForcedGoal = Flat2(to - p.Position) < 3f * 3f ? p.Position : to; // (cornered at the map's edge: they cower)
        return true;
    }
}
