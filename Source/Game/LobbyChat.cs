using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What kind of chat line (several can be set: a dead player's team line is Team | Dead).</summary>
[Flags]
public enum ChatFlags : byte
{
    None = 0,

    /// <summary>Team chat: only the sender's team got it ([TEAM]).</summary>
    Team = 1,

    /// <summary>Said by a dead player in a live match ([DEAD]).</summary>
    Dead = 2,

    /// <summary>A system line (joins, leaves): no sender.</summary>
    System = 4,
}

/// <summary>
/// One chat line: when it arrived, who said it (their name at the time) and what (clean printable ASCII); plus the kind
/// (<see cref="ChatFlags"/>) and the sender's team then (for the colour).
/// </summary>
public readonly record struct ChatLine(double Time, byte From, string Name, string Text, ChatFlags Flags = ChatFlags.None, byte Team = Game.NoTeam);

/// <summary>
/// The chat's rules (data, shared by the server, the client and the self-test; one chat for the lobby room and the match):
///   - text is printable ASCII, trimmed, at most <see cref="MaxLength"/> characters (the text box holds no more, and
///     the server cleans it again: nothing a client sends is trusted);
///   - each person may send <see cref="Burst"/> lines at once, then one every <see cref="RefillSeconds"/> (a token
///     bucket, checked on the client to say "slow down" and on the server to drop the rest);
///   - the last <see cref="Keep"/> lines are kept.
/// </summary>
public static class ChatRules
{
    public const int MaxLength = 120;

    /// <summary>The text box's buffer (one more for the terminating NUL).</summary>
    public const int BoxCapacity = MaxLength + 1;

    public const int Burst = 4;
    public const double RefillSeconds = 1.5;
    public const int Keep = 50;

    /// <summary>A chat line as it may be shown or sent (empty: nothing to send).</summary>
    public static string Clean(string? text) => NetSafety.CleanText(text, MaxLength);

    /// <summary>The sender id of system lines (joins and leaves).</summary>
    public const byte SystemId = byte.MaxValue;

    /// <summary>In-match chat lines fade after this long (the full history shows while typing).</summary>
    public const double ShowSeconds = 8;

    /// <summary>What team chat says in free-for-all (there is no team to talk to).</summary>
    public const string NoTeamHint = "Team chat needs a team mode: in free-for-all everyone is an enemy (use all chat)";

    /// <summary>A line as shown: "[TEAM] [DEAD] Name: text", or a system line's text alone.</summary>
    public static string Format(in ChatLine c) =>
        (c.Flags & ChatFlags.System) != 0 ? c.Text
        : $"{((c.Flags & ChatFlags.Team) != 0 ? "[TEAM] " : "")}{((c.Flags & ChatFlags.Dead) != 0 ? "[DEAD] " : "")}{c.Name}: {c.Text}";

    /// <summary>
    /// The rate limit: with <paramref name="tokens"/> lines left at <paramref name="last"/>, may one more go at
    /// <paramref name="now"/>? Updates both when it may.
    /// </summary>
    public static bool Allow(ref double tokens, ref double last, double now)
    {
        tokens = Math.Min(Burst, tokens + Math.Max(0, now - last) / RefillSeconds);
        last = now;
        if (tokens < 1)
            return false;
        tokens -= 1;
        return true;
    }
}

/// <summary>
/// The chat (protocol 32, extended in 36): <see cref="Msg.ChatSay"/> to the server (text + flags: team), and
/// <see cref="Msg.ChatLine"/> to each player who should see it (sender, text, flags, the sender's team). The lobby room and
/// the in-match HUD chat are the same chat. All chat reaches everyone; team chat only the sender's team (team modes, or
/// everyone in co-op Zombies; in free-for-all it isn't sent); with the host's "Dead chat: dead only" a dead player's lines
/// in a live match only reach the dead. Players you muted (<see cref="MutedChat"/>) are dropped when their lines arrive.
/// </summary>
public sealed partial class Game
{
    /// <summary>The chat so far (oldest first, at most <see cref="ChatRules.Keep"/>).</summary>
    public readonly List<ChatLine> Chat = new();

    /// <summary>Bumped on every new line (the lobby screen scrolls down when it moves).</summary>
    public int ChatVersion { get; private set; }

    /// <summary>Host setting: a dead player's chat in a live match reaches only the dead (off: everyone, tagged [DEAD]).</summary>
    public bool DeadChatPrivate;

    /// <summary>People whose chat you don't want to see (by name; this session only).</summary>
    public readonly HashSet<string> MutedChat = new(StringComparer.Ordinal);

    /// <summary>Is there a team to talk to (team modes, co-op)?</summary>
    public bool TeamChatAvailable => IsTeamMode || Module.CoOp;

    private double _chatTokens = ChatRules.Burst, _chatLast;
    private readonly Dictionary<byte, (double Tokens, double Last)> _chatBuckets = new();

    /// <summary>Say something in the chat. Returns null when sent, or why not ("slow down").</summary>
    public string? SendChat(string text) => SendChat(text, Brutal.GlfwApi.Glfw.Time);

    /// <summary><see cref="SendChat(string)"/> with an explicit clock (self-test).</summary>
    public string? SendChat(string text, double now) => SendChat(text, now, team: false);

    /// <summary>Say something to everyone, or (<paramref name="team"/>) to your team only. Returns null when sent, or why not.</summary>
    public string? SendChat(string text, double now, bool team)
    {
        var clean = ChatRules.Clean(text);
        if (clean.Length == 0)
            return "";
        if (team && !TeamChatAvailable)
            return ChatRules.NoTeamHint;
        if (!ChatRules.Allow(ref _chatTokens, ref _chatLast, now))
            return "Slow down: you're sending messages too fast";
        if (IsServer)
        {
            if (Local is { } me)
                ServerChat(me, clean, now, team);
        }
        else
        {
            _w.Begin(Msg.ChatSay).Str(clean).U8(team ? (byte)ChatFlags.Team : (byte)0);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
        return null;
    }

    /// <summary>Does a line from <paramref name="from"/> reach <paramref name="to"/>?</summary>
    private bool ChatReaches(Player from, Player to, bool team, bool dead)
    {
        if (to == from)
            return true;
        if (to.Bot != null)
            return false;
        if (team && !VoiceRules.Mates(from.Team, to.Team, IsTeamMode, Module.CoOp))
            return false;
        if (dead && DeadChatPrivate && to.Alive)
            return false;
        return true;
    }

    /// <summary>Server: <paramref name="from"/> said <paramref name="text"/> (already cleaned). Rate-limited per player; sent to whoever it reaches.</summary>
    private void ServerChat(Player from, string text, double now, bool team = false)
    {
        if (from.Id != LocalId) // (the host's own lines were checked by SendChat)
        {
            (double Tokens, double Last) b = _chatBuckets.TryGetValue(from.Id, out var had) ? had : (ChatRules.Burst, now);
            var ok = ChatRules.Allow(ref b.Tokens, ref b.Last, now);
            _chatBuckets[from.Id] = b;
            if (!ok)
                return; // spam: dropped
        }
        if (team && !TeamChatAvailable)
            return; // nobody to send it to
        var dead = Phase == MatchPhase.Playing && !from.Alive;
        var flags = (team ? ChatFlags.Team : ChatFlags.None) | (dead ? ChatFlags.Dead : ChatFlags.None);
        var teamOf = IsTeamMode ? from.Team : NoTeam;
        ChatsRouted++;
        if (Local is { } me && ChatReaches(from, me, team, dead))
            AddChat(from.Id, from.Name, text, now, flags, teamOf);
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ChatLine).U8(from.Id).Str(text).U8((byte)flags).U8(teamOf);
        if (!team && !(dead && DeadChatPrivate))
        {
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
            return;
        }
        foreach (var c in _clients.Values)
            if (Players.TryGetValue(c.Id, out var to) && ChatReaches(from, to, team, dead))
                Net.SendTo(c.Guid, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Lines the server has routed (self-test).</summary>
    public int ChatsRouted;

    /// <summary>Server: a system line for everyone ("Ash joined", "Ash left").</summary>
    public void ServerSystemChat(string text, double now)
    {
        if (!IsServer)
            return;
        text = ChatRules.Clean(text);
        if (text.Length == 0)
            return;
        AddChat(ChatRules.SystemId, "", text, now, ChatFlags.System);
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ChatLine).U8(ChatRules.SystemId).Str(text).U8((byte)ChatFlags.System).U8(NoTeam);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void AddChat(byte from, string name, string text, double now, ChatFlags flags = ChatFlags.None, byte team = NoTeam)
    {
        if ((flags & ChatFlags.System) == 0 && from != LocalId && MutedChat.Contains(name))
            return; // muted by you
        Chat.Add(new ChatLine(now, from, name, text, flags, team));
        while (Chat.Count > ChatRules.Keep)
            Chat.RemoveAt(0);
        ChatVersion++;
        Console.WriteLine($"[chat] {ChatRules.Format(Chat[^1])}");
    }

    private void OnChatPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.ChatSay when IsServer && r.Has(1):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                var text = ChatRules.Clean(r.Str());
                var team = r.Has(1) && (r.U8() & (byte)ChatFlags.Team) != 0; // protocol 36
                if (text.Length > 0)
                    ServerChat(p, text, now, team);
                break;
            }
            case (byte)Msg.ChatLine when !IsServer && r.Has(2):
            {
                var from = r.U8();
                var text = ChatRules.Clean(r.Str());
                var flags = r.Has(1) ? (ChatFlags)(r.U8() & 7) : ChatFlags.None;
                var team = r.Has(1) ? r.U8() : NoTeam;
                if (from == ChatRules.SystemId)
                    flags = ChatFlags.System;
                if (text.Length > 0)
                    AddChat(from, (flags & ChatFlags.System) != 0 ? "" : NameOf(from), text, now, flags, team);
                break;
            }
        }
    }

    /// <summary>Self-test: player <paramref name="p"/>'s chat line arriving at the server (as if from the network).</summary>
    public void SelfTestChatFrom(Player p, string text, double now, bool team = false)
    {
        var clean = ChatRules.Clean(text);
        if (clean.Length > 0)
            ServerChat(p, clean, now, team);
    }
}
