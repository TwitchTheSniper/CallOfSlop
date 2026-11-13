using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The topics in the talk panel (docs/story/STORY.md "Topics"; the engine is Source/Story/StoryTopics.cs): while a player
/// talks to one of the story's people, the host sends them what that person will talk about with them now
/// (<see cref="Msg.TalkTopics"/>, when it changes), each a button; asking one is <see cref="TalkAsk.Topic"/> with the
/// topic's index, answered in the person's own words. Every peer has the topics' texts, so only indices travel. Host
/// decides; nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    public const int TalkTopicsMax = TopicRules.Offered;

    private double _topicsAt;
    private readonly int[] _topicKey = new int[256]; // per player: what their talk panel's topics were last sent as
    private readonly (ushort Topic, bool Asked)[] _topicBuf = new (ushort, bool)[TalkTopicsMax];
    private PacketWriter? _topicW;

    /// <summary>Client (and host): the topics the talk panel with <see cref="TalkTopicsBody"/> offers now.</summary>
    public readonly ushort[] TalkTopics = new ushort[TalkTopicsMax];
    public readonly bool[] TalkTopicAsked = new bool[TalkTopicsMax]; // asked before (a topic that stays on offer)
    public readonly string[] TalkTopicLabels = new string[TalkTopicsMax]; // the buttons' labels ("text##talktopicN")
    public int TalkTopicCount;
    public byte TalkTopicsBody = 255;

    private void StoryTopicsForget() => Array.Clear(_topicKey);

    /// <summary>Server, from <see cref="StoryTick"/>: twice a second, each player talking to someone gets the topics that person offers them, when they changed.</summary>
    private void StoryTopicsTick(StoryEngine s, double now)
    {
        if (now < _topicsAt || s.Rules.Topics.Topics.Length == 0)
            return;
        _topicsAt = now + 0.5;
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Id < _topicKey.Length)
                StoryTopicsSend(s, p);
    }

    /// <summary>The topics a player has from the person whose body they talk to: into <see cref="_topicBuf"/>, how many.</summary>
    private int StoryTopicsOf(StoryEngine s, Player p, byte bodyPlayer)
    {
        if (StoryWho(p) is not (var who and >= 0) || StoryBodyIndex(bodyPlayer) is not (var i and >= 0))
            return 0;
        return s.Topics.Offer(_bodies[i].Entity, who, _topicBuf);
    }

    private void StoryTopicsSend(StoryEngine s, Player p, bool force = false)
    {
        var body = (byte)255;
        for (var b = 0; b < _talkWith.Length; b++)
            if (_talkWith[b] == p.Id + 1)
                body = (byte)b;
        var n = body == 255 ? 0 : StoryTopicsOf(s, p, body);
        var key = body * 31 + n;
        for (var k = 0; k < n; k++)
            key = key * 131 + _topicBuf[k].Topic * 2 + (_topicBuf[k].Asked ? 1 : 0);
        if (!force && _topicKey[p.Id] == key + 1)
            return;
        _topicKey[p.Id] = key + 1;
        var w = _topicW ??= new PacketWriter(64);
        w.Begin(Msg.TalkTopics).U8(body).U8((byte)n);
        for (var k = 0; k < n; k++)
            w.U16((ushort)(_topicBuf[k].Topic | (_topicBuf[k].Asked ? 0x8000 : 0)));
        if (p.Id == LocalId)
        {
            var r = new PacketReader(w.Span[1..]);
            ReadTalkTopics(ref r);
        }
        else if (!Net.IsOffline && GuidOf(p) is { } g)
            Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client (and host): a <see cref="Msg.TalkTopics"/> after its id: u8 the body, u8 n, n u16 (a topic's index, the top bit: asked before).</summary>
    private void ReadTalkTopics(ref PacketReader r)
    {
        if (!r.Has(2))
            return;
        TalkTopicsBody = r.U8();
        var n = Math.Min((int)r.U8(), TalkTopicsMax);
        var topics = StoryData.Topics.Topics;
        var count = 0;
        for (var k = 0; k < n && r.Has(2); k++)
        {
            var v = r.U16();
            var t = v & 0x7FFF;
            if (t >= topics.Length)
                continue; // (a host with other story files: left out)
            TalkTopics[count] = (ushort)t;
            TalkTopicAsked[count] = (v & 0x8000) != 0;
            TalkTopicLabels[count] = $"{topics[t].Ask}##talktopic{count}";
            count++;
        }
        TalkTopicCount = count;
    }

    /// <summary>Server: a player asks the person about a topic (its index in Story/Topics). Returns why not, or null.</summary>
    private string? StoryTopicAct(StoryEngine s, StoryAgent a, Player p, int who, ushort topic)
    {
        if (s.Topics.Ask(a.Entity, who, topic, out var line) is not { } answer)
            return $"{s.World.NameOf(a.Entity)} won't talk about that now";
        if (answer.Line >= 0)
            StoryAnswer(s, a, who, answer.Line);
        else if (line != null)
            s.Talk.SayFor(a.Entity, who, line, s.Time, -1, -1);
        SpaceportTopic(s, p, who, a.Entity, topic, answer); // (the courier's handover: GameSpaceport.cs)
        StoryTopicsSend(s, p); // (what is on offer now: a follow-up, or the topic gone)
        return null;
    }
}
