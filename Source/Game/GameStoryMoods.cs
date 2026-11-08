using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The story people's moods (docs/story/STORY.md "Moods"): buffs and debuffs from Story/Moods. At a moment of a fight
/// (someone killed near them, a kill of their own, hurt, badly hurt, coming to help) a person's body may take on a mood
/// whose query holds over the story world (they liked the dead, they are brave, vengeful...). While it lasts it scales the
/// damage they take and deal (<see cref="DamagePlayer"/>), their aim, reactions and turning (<see cref="BotBrain"/>'s
/// mood fields), and may make them fight, or run from, whoever caused it whatever the fight rules would say
/// (GameStoryFights.cs). One mood at a time; it ends on its time, or when whoever it is aimed at is gone, and may lead to
/// another (rage to grief). Taking hold, it records its verb, the person says the mood's line and players nearby read
/// what came over them. Host only; everything runs on the server's own bots and damage, so every player in a co-op game
/// meets the same rage. Nothing here runs each tick but a look at the bodies' clocks four times a second.
/// </summary>
public sealed partial class Game
{
    private StoryRules? _moodRules;
    private int[] _moodConcept = []; // per mood, its occasion in the lines (-1: none)

    /// <summary>Moods taken on this session (the window).</summary>
    public int StoryMoodsBegun;

    /// <summary>A person's mood now (the window): the mood and its seconds left, or null.</summary>
    public (MoodDef Mood, double Left)? StoryMoodOf(int entity, double now)
    {
        if (Story is not { } s)
            return null;
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Entity == entity && _bodies[i].MoodOn > 0 && _bodies[i].MoodOn <= s.Rules.Moods.Moods.Length)
                return (s.Rules.Moods.Moods[_bodies[i].MoodOn - 1], Math.Max(0, _bodies[i].MoodUntil - now));
        return null;
    }

    private MoodDef? StoryMood(StoryEngine s, in StoryBody b) =>
        b.MoodOn > 0 && b.MoodOn <= s.Rules.Moods.Moods.Length ? s.Rules.Moods.Moods[b.MoodOn - 1] : null;

    /// <summary>Does a mood hold this body to its fight (or flight) against whoever caused it, now?</summary>
    private bool StoryMoodHolds(StoryEngine s, in StoryBody b) =>
        StoryMood(s, b) is { Act: not MoodDef.ActNone } m && b.Foe == b.MoodCause && b.FoeEntity == b.MoodCauseEntity
        && b.Fight == (m.Act == MoodDef.ActFight ? StoryFighting : StoryFleeing);

    /// <summary>
    /// A moment for body <paramref name="i"/>: of the moment's moods whose query holds, the most specific takes hold,
    /// unless they are in a mood of a higher rank. <paramref name="cause"/> is whoever caused it (?them), <paramref name="about"/>
    /// whom it is about (?target); either may be missing (-1, null).
    /// </summary>
    private void StoryMoodAt(StoryEngine s, int i, int moment, Player? cause, int causeEntity, int about, double now)
    {
        var mr = s.Rules.Moods;
        if ((uint)moment >= (uint)mr.OfMoment.Length || mr.OfMoment[moment].Length == 0)
            return;
        ref var b = ref _bodies[i];
        if (s.Agents.Of(b.Entity) is not { Dead: false } || !Players.TryGetValue(b.Player, out var p) || !p.Alive)
            return;
        if (cause == p)
            (cause, causeEntity) = (null, -1);
        var frame = Ints8.Empty();
        (frame[MoodDef.Me], frame[MoodDef.Them], frame[MoodDef.Target], frame[MoodDef.Here]) = (b.Entity, causeEntity, about, s.Agents.Where(b.Entity));
        var have = 1 << MoodDef.Me | (causeEntity >= 0 ? 1 << MoodDef.Them : 0) | (about >= 0 ? 1 << MoodDef.Target : 0) | (frame[MoodDef.Here] >= 0 ? 1 << MoodDef.Here : 0);
        var causeHere = cause is { Alive: true };
        foreach (var id in mr.OfMoment[moment])
        {
            var m = mr.Moods[id];
            if ((m.Needs & ~have) != 0 || m.Act != MoodDef.ActNone && !causeHere || m.If != null && !m.If.Any(s.World, frame))
                continue;
            // The most specific that holds wins, and then takes hold only so often, and only over a mood of no higher rank.
            if (m.Chance < 1 && _rng.NextDouble() >= m.Chance)
                return;
            if (StoryMood(s, b) is { } cur && cur.Rank > m.Rank)
                return;
            StoryMoodStart(s, ref b, p, m, cause, causeEntity, about, now);
            return;
        }
    }

    private void StoryMoodStart(StoryEngine s, ref StoryBody b, Player p, MoodDef m, Player? cause, int causeEntity, int about, double now)
    {
        if (_moodRules != s.Rules)
        {
            _moodRules = s.Rules;
            _moodConcept = [.. s.Rules.Moods.Moods.Select(x => s.Rules.Lines.Concept(x.Name))];
        }
        var again = b.MoodOn == m.Id + 1 && b.MoodCauseEntity == causeEntity;
        b.MoodOn = (short)(m.Id + 1);
        b.MoodUntil = now + m.Seconds;
        (b.MoodCause, b.MoodCauseEntity, b.MoodAbout) = (cause?.Id ?? 0, cause != null ? causeEntity : -1, about);
        if (p.Bot is { } bot)
            (bot.MoodAim, bot.MoodReact, bot.MoodTurn) = (m.Aim, m.React, m.Turn);
        if (m.Act != MoodDef.ActNone && cause != null)
        {
            (b.Fight, b.Foe, b.FoeEntity) = (m.Act == MoodDef.ActFight ? StoryFighting : StoryFleeing, cause.Id, causeEntity);
            b.FightUntil = b.MoodUntil;
        }
        if (again)
            return; // (the same mood at the same one again: it only lasts longer)
        StoryMoodsBegun++;
        var clock = s.Time;
        if (m.Verb >= 0 && b.MoodCauseEntity >= 0)
            s.Record(m.Verb, b.Entity, b.MoodCauseEntity, owner: about, place: StoryPlace());
        Console.WriteLine($"[story] {p.Name} {m.Label} ({m.Name}{(cause != null ? $", at {cause.Name}" : "")}{(about >= 0 ? $", over {s.World.NameOf(about)}" : "")}; {m.Seconds:0} s: {MoodRules.Effects(m)})");
        StoryNotice(p, $"{p.Name} {m.Label}.", now);
        if ((uint)m.Id < (uint)_moodConcept.Length && _moodConcept[m.Id] >= 0)
        {
            var lf = LineFacts.Of(b.Entity);
            (lf.Them, lf.Actor, lf.Target, lf.Owner, lf.Here) = (b.MoodCauseEntity, b.MoodCauseEntity, about, about, s.Agents.Where(b.Entity));
            if (s.Talk.Say(_moodConcept[m.Id], lf, clock, prompted: true) != null)
                b.HurtSaidAt = clock; // (instead of the Hurt line this look)
        }
    }

    /// <summary>A mood ends: the body is itself again, unless the mood leads to another.</summary>
    private void StoryMoodEnd(StoryEngine s, ref StoryBody b, Player p, string why, double now)
    {
        var m = StoryMood(s, b);
        var held = StoryMoodHolds(s, b);
        b.MoodOn = 0;
        if (p.Bot is { } bot)
            (bot.MoodAim, bot.MoodReact, bot.MoodTurn) = (1, 1, 1);
        if (m == null)
            return;
        Console.WriteLine($"[story] {p.Name}: {m.Name} is over ({why})");
        if (held && b.Fight != StoryCalm)
            b.FightUntil = now + (b.Fight == StoryFighting ? StoryFightCalm : StoryFleeCalm); // (the fight goes on by its own rules)
        if (m.Then >= 0 && p.Alive)
            StoryMoodStart(s, ref b, p, s.Rules.Moods.Moods[m.Then], null, -1, b.MoodAbout, now);
    }

    /// <summary>From <see cref="StoryFightsTick"/>, four times a second: moods run out, or end with whoever they were aimed at; a held fight stays on.</summary>
    private void StoryMoodsTick(StoryEngine s, ref StoryBody b, Player p, double now)
    {
        if (StoryMood(s, b) is not { } m)
        {
            b.MoodOn = 0;
            return;
        }
        if (now >= b.MoodUntil)
        {
            StoryMoodEnd(s, ref b, p, "it wore off", now);
            return;
        }
        if (m.Act == MoodDef.ActNone)
            return;
        if (!Players.TryGetValue(b.MoodCause, out var c) || !c.Alive || StoryEntityOf(c) != b.MoodCauseEntity)
        {
            StoryMoodEnd(s, ref b, p, m.Act == MoodDef.ActFight ? "whoever it was for is down or gone" : "they got away", now);
            return;
        }
        // Held to it: back on whoever caused it (another's shot turned them, the fight rules called it off), for as long as it lasts.
        (b.Fight, b.Foe, b.FoeEntity) = (m.Act == MoodDef.ActFight ? StoryFighting : StoryFleeing, c.Id, b.MoodCauseEntity);
        b.FightUntil = b.MoodUntil;
    }

    /// <summary>Someone was killed (from <see cref="StoryKill"/>, before the body is taken away): Killed for every body within hearing, Kill for a killer with a body.</summary>
    private void StoryMoodsOnKill(StoryEngine s, Player attacker, Player victim, int killer, int dead, double now)
    {
        if (_bodyCount == 0 || s.Rules.Moods.Moods.Length == 0)
            return;
        var by = attacker == victim ? null : attacker;
        for (var i = 0; i < _bodyCount; i++)
        {
            if (_bodies[i].Player == victim.Id || !Players.TryGetValue(_bodies[i].Player, out var p) || !p.Alive)
                continue;
            if (p == attacker)
                StoryMoodAt(s, i, MoodRules.Kill, null, -1, dead, now);
            else if (Flat2(p.Position - victim.Position) <= StoryHearing * StoryHearing)
                StoryMoodAt(s, i, MoodRules.Killed, by, by != null ? killer : -1, dead, now);
        }
    }

    /// <summary>
    /// Damage between a person's body and anyone (from <see cref="DamagePlayer"/>): the attacker's mood scales what they
    /// deal, the victim's what they take; at least 1 is left of a hit.
    /// </summary>
    private int StoryMoodDamage(Player attacker, Player victim, int amount)
    {
        if (Story is not { } s)
            return amount;
        var mult = 1f;
        for (var i = 0; i < _bodyCount; i++)
        {
            if (_bodies[i].MoodOn == 0)
                continue;
            if (_bodies[i].Player == attacker.Id && StoryMood(s, _bodies[i]) is { } a)
                mult *= a.Dealt;
            if (_bodies[i].Player == victim.Id && StoryMood(s, _bodies[i]) is { } v)
                mult *= v.Taken;
        }
        return mult == 1 ? amount : Math.Max(1, (int)MathF.Round(amount * mult));
    }

    /// <summary>What came over a person, for the players within <see cref="StoryHearing"/>: a system line in their chat.</summary>
    private void StoryNotice(Player near, string text, double now)
    {
        text = ChatRules.Clean(text);
        if (text.Length == 0)
            return;
        if (Local is { Bot: null } me && System.Numerics.Vector3.DistanceSquared(me.Position, near.Position) <= StoryHearing * StoryHearing)
            AddChat(ChatRules.SystemId, "", text, now, ChatFlags.System);
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ChatLine).U8(ChatRules.SystemId).Str(text).U8((byte)ChatFlags.System).U8(NoTeam);
        foreach (var c in _clients.Values)
            if (Players.TryGetValue(c.Id, out var to) && to.Bot == null && System.Numerics.Vector3.DistanceSquared(to.Position, near.Position) <= StoryHearing * StoryHearing)
                Net.SendTo(c.Guid, _w.Span, Brutal.RakNetApi.PacketReliability.ReliableOrdered);
    }
}
