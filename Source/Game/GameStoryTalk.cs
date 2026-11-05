using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The story people's words in the world (docs/story/STORY.md "Lines"; phase 5 of the story systems). The story decides
/// what is said (<see cref="StoryTalk"/>, the response rules in Story/Lines); this decides when, and who hears it:
///   - a step plays out when the body reaches whoever it is with (or its spot; a trip as it sets off), and the person
///     says that step's line;
///   - a player walking up to someone (within <see cref="StoryNearReach"/>) gets a word from them, and a body that is hurt
///     says so;
///   - what people saw happen and their replies come from the story itself.
/// A line said by someone with a body here reaches the players within <see cref="StoryHearing"/> of them, in the chat and
/// over their head (<see cref="ChatFlags.Say"/>); a broadcast reaches every player as a [RADIO] line, whoever has a body.
/// The words change nothing in the story. Host only, four times a second with the bodies; nothing allocates unless
/// something is said.
/// </summary>
public sealed partial class Game
{
    public const float StoryHearing = 30f; // m: players this close to a speaker hear them
    public const float StoryNearReach = 5f, StoryNearLeave = 8f; // m: a player comes up to someone, and walks off again
    public const double StoryHurtGap = 3; // s between one body's words about being hurt

    private long _talkSeen; // the story's lines delivered so far (StoryTalk.Lines)
    private readonly bool[] _storyNear = new bool[256 * 256]; // (body's player, player): within reach at the last look

    /// <summary>Lines heard in this world (by at least one player) and broadcasts sent this session (the window).</summary>
    public int StoryLinesHeard, StoryBroadcasts;

    /// <summary>A person's body was hurt (<see cref="DamagePlayer"/>; not killed): they say so at the next look.</summary>
    private void StoryBodyHurt(Player body, Player attacker)
    {
        var i = StoryBodyIndex(body.Id);
        if (i >= 0)
            _bodies[i].HurtBy = (short)(attacker.Id + 1);
    }

    /// <summary>From <see cref="StoryBodiesTick"/>: the occasions bodies give (a step plays out, a player comes up, a hurt), then who hears what was said.</summary>
    private void StoryTalkTick(StoryEngine s, double now)
    {
        var talk = s.Talk;
        var ag = s.Agents;
        var clock = s.Time;
        if (talk.On)
            for (var i = 0; i < _bodyCount; i++)
            {
                ref var b = ref _bodies[i];
                if (ag.Of(b.Entity) is not { Dead: false } a || !Players.TryGetValue(b.Player, out var p) || !p.Alive)
                    continue;
                // A step plays out: with someone, once they have met; alone, at its spot; a trip as it sets off.
                if (a.Busy && b.SpokeFor != a.DoingFrom)
                {
                    var act = s.Rules.People.Actions[a.Doing.Action];
                    var t = a.Doing.Target;
                    if (act.Road)
                    {
                        b.SpokeFor = a.DoingFrom; // (setting off says it; walking in from the road says nothing)
                        if (b.Leaving)
                            talk.ForStep(a, clock);
                    }
                    else if (t >= 0 ? StoryPosOf(t) is { } them && Flat2(them - p.Position) <= StoryMeetReach * StoryMeetReach
                             : Flat2(b.Goal - p.Position) < 2.5f * 2.5f)
                    {
                        b.SpokeFor = a.DoingFrom;
                        talk.ForStep(a, clock);
                    }
                }
                // Hurt (not killed): who did it, when known.
                if (b.HurtBy > 0)
                {
                    var by = (byte)(b.HurtBy - 1);
                    b.HurtBy = 0;
                    if (clock - b.HurtSaidAt >= StoryHurtGap)
                    {
                        b.HurtSaidAt = clock;
                        var f = LineFacts.Of(b.Entity);
                        f.Them = Players.TryGetValue(by, out var hurter) && by != p.Id ? StoryEntityOf(hurter) : -1;
                        (f.Actor, f.Target, f.Here) = (f.Them, b.Entity, ag.Where(b.Entity));
                        talk.Say(LineRules.Hurt, f, clock, prompted: true);
                    }
                }
                // Players coming up to them (once as they come within reach; again after they have walked off).
                foreach (var h in Players.Values)
                {
                    if (h.Bot != null)
                        continue;
                    ref var near = ref _storyNear[b.Player * 256 + h.Id];
                    var d2 = h.Alive ? Flat2(h.Position - p.Position) : float.MaxValue;
                    if (near)
                    {
                        if (d2 > StoryNearLeave * StoryNearLeave)
                            near = false;
                        continue;
                    }
                    if (d2 > StoryNearReach * StoryNearReach || StoryEntityOf(h) is not (var who and >= 0))
                        continue;
                    near = true;
                    if (a.Busy && a.Doing.Target == who)
                        continue; // (their step with this player says it)
                    var f = LineFacts.Of(b.Entity);
                    (f.Them, f.Actor, f.Target, f.Here) = (who, b.Entity, who, ag.Where(b.Entity));
                    talk.Say(LineRules.Near, f, clock);
                }
            }

        // Who hears what was said (the last few, if a lot was said at once).
        if (_talkSeen > talk.Lines)
            _talkSeen = talk.Lines; // (a fresh or loaded story)
        for (var n = Math.Max(_talkSeen, talk.Lines - StoryTalk.Keep); n < talk.Lines; n++)
        {
            ref readonly var line = ref talk.Said[n % StoryTalk.Keep];
            if (line.Radio)
                StoryRadio(s.World.NameOf(line.Speaker), line.Text, now);
            else if (StoryBodyOf(line.Speaker) is { Alive: true } speaker)
                StorySpeak(speaker, line.Text, now);
        }
        _talkSeen = talk.Lines;
    }

    /// <summary>The story entity of a player here (a person's body, or a player whose Join is recorded), or -1.</summary>
    private int StoryEntityOf(Player p) => _storyWho[p.Id];

    /// <summary>A body says a line: the players within <see cref="StoryHearing"/> get it in their chat and over its head.</summary>
    private void StorySpeak(Player speaker, string text, double now)
    {
        text = ChatRules.Clean(text);
        if (text.Length == 0)
            return;
        var heard = false;
        if (Local is { Bot: null } me && Vector3.DistanceSquared(me.Position, speaker.Position) <= StoryHearing * StoryHearing)
        {
            AddChat(speaker.Id, speaker.Name, text, now, ChatFlags.Say);
            heard = true;
        }
        if (!Net.IsOffline)
        {
            _w.Begin(Msg.ChatLine).U8(speaker.Id).Str(text).U8((byte)ChatFlags.Say).U8(NoTeam);
            foreach (var c in _clients.Values)
                if (Players.TryGetValue(c.Id, out var to) && to.Bot == null && Vector3.DistanceSquared(to.Position, speaker.Position) <= StoryHearing * StoryHearing)
                {
                    Net.SendTo(c.Guid, _w.Span, Brutal.RakNetApi.PacketReliability.ReliableOrdered);
                    heard = true;
                }
        }
        if (heard)
            StoryLinesHeard++;
    }

    /// <summary>A broadcast: every player hears it, as a [RADIO] line ("Nell Ashby: ...").</summary>
    private void StoryRadio(string who, string text, double now)
    {
        text = ChatRules.Clean($"{who}: {text}");
        if (text.Length == 0)
            return;
        StoryBroadcasts++;
        AddChat(ChatRules.SystemId, "", text, now, ChatFlags.System | ChatFlags.Radio);
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ChatLine).U8(ChatRules.SystemId).Str(text).U8((byte)(ChatFlags.System | ChatFlags.Radio)).U8(NoTeam);
        Net.Broadcast(_w.Span, Brutal.RakNetApi.PacketReliability.ReliableOrdered);
    }

    /// <summary>A body of the people's side came (or its player id was taken again): nobody is near it yet.</summary>
    private void StoryNearForget(byte bodyPlayer) => Array.Clear(_storyNear, bodyPlayer * 256, 256);
}
