using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Joining a talk (docs/story/STORY.md "Talking to people"; co-op): when a player begins talking to one of the story's
/// people, every other player gets a notice (<see cref="Msg.TalkInvite"/>) they may answer or ignore. One who joins has
/// the same panel wherever they are: they hear what the person says while the talk lasts (GameStoryTalk.cs sends them
/// the lines) and may say hello or ask for news themselves; a trade still wants them there. The talk stays the first
/// player's: when it ends, the joiners' panels close. Host only; nothing allocates in a tick.
/// </summary>
public sealed partial class Game
{
    public const double TalkInviteGap = 20; // s before the same talk is offered again

    private readonly byte[] _talkJoined = new byte[256]; // per player: the body (its player id + 1) whose talk they joined (0: none)
    private readonly double[] _talkInviteAt = new double[256]; // per body: when its talk was last offered round
    private readonly byte[] _talkInviteFor = new byte[256]; // ...and whose talk that was (the player's id + 1)
    private PacketWriter? _inviteW;

    /// <summary>Client (and host): a talk the local player may join: who is talking, and to whose body (255: none yet).</summary>
    public byte TalkInviteFrom = 255, TalkInviteBody = 255;

    /// <summary>Client (and host): counts up with each invitation, and each time a joined talk is over (Program.StoryTalk.cs watches them).</summary>
    public int TalkInviteSerial, TalkJoinOverSerial;

    /// <summary>Server: <paramref name="talker"/> began talking to <paramref name="body"/>: the other players may join.</summary>
    private void StoryTalkInvite(Player talker, Player body)
    {
        if (_talkInviteFor[body.Id] == talker.Id + 1 && SbClock < _talkInviteAt[body.Id] + TalkInviteGap && SbClock >= _talkInviteAt[body.Id])
            return;
        _talkInviteFor[body.Id] = (byte)(talker.Id + 1);
        _talkInviteAt[body.Id] = SbClock;
        foreach (var q in Players.Values)
            if (q.Bot == null && q.Id < MaxPlayers && q != talker)
                TalkInviteSend(q, 1, talker.Id, body.Id);
    }

    private void TalkInviteSend(Player to, byte kind, byte talker, byte body)
    {
        if (to.Id == LocalId)
        {
            ReadTalkInvite(kind, talker, body);
            return;
        }
        if (Net.IsOffline || GuidOf(to) is not { } guid)
            return;
        var w = _inviteW ??= new PacketWriter();
        w.Begin(Msg.TalkInvite).U8(kind).U8(talker).U8(body);
        Net.SendTo(guid, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: a player joins the talk someone else is having with a body (<see cref="TalkAsk.Join"/>). Why not, or null.</summary>
    private string? StoryTalkJoin(Player p, byte bodyPlayer)
    {
        if (_talkWith[bodyPlayer] is not (var t and > 0) || t == p.Id + 1 || !Players.TryGetValue((byte)(t - 1), out var talker) || !talker.Alive || SbClock >= _talkUntil[bodyPlayer])
        {
            TalkInviteSend(p, 0, 0, bodyPlayer); // (their panel closes)
            return "that chat is over";
        }
        _talkJoined[p.Id] = (byte)(bodyPlayer + 1);
        return null;
    }

    /// <summary>Server, from the story's tick: a joiner whose talk is over (its player said goodbye, walked off, or the person left) is told.</summary>
    private void StoryTalkJoinTick()
    {
        for (var id = 0; id < MaxPlayers; id++)
        {
            if (_talkJoined[id] == 0)
                continue;
            var body = (byte)(_talkJoined[id] - 1);
            if (_talkWith[body] > 0 && _talkWith[body] != id + 1 && Players.ContainsKey((byte)id))
                continue;
            _talkJoined[id] = 0;
            if (Players.TryGetValue((byte)id, out var p))
                TalkInviteSend(p, 0, 0, body);
        }
    }

    /// <summary>Client (and the host's own): a <see cref="Msg.TalkInvite"/>'s three bytes.</summary>
    private void ReadTalkInvite(byte kind, byte talker, byte body)
    {
        if (kind == 0)
        {
            TalkJoinOverSerial++;
            return;
        }
        (TalkInviteFrom, TalkInviteBody) = (talker, body);
        TalkInviteSerial++;
    }
}
