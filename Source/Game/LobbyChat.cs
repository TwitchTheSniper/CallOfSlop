using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>One chat line: when it arrived, who said it (their name at the time) and what (clean printable ASCII).</summary>
public readonly record struct ChatLine(double Time, byte From, string Name, string Text);

/// <summary>
/// The lobby chat's rules (data, shared by the server, the client and the self-test):
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

/// <summary>Lobby chat (protocol 32): <see cref="Msg.ChatSay"/> to the server, <see cref="Msg.ChatLine"/> to everyone.</summary>
public sealed partial class Game
{
    /// <summary>The chat so far (oldest first, at most <see cref="ChatRules.Keep"/>).</summary>
    public readonly List<ChatLine> Chat = new();

    /// <summary>Bumped on every new line (the lobby screen scrolls down when it moves).</summary>
    public int ChatVersion { get; private set; }

    private double _chatTokens = ChatRules.Burst, _chatLast;
    private readonly Dictionary<byte, (double Tokens, double Last)> _chatBuckets = new();

    /// <summary>Say something in the lobby chat. Returns null when sent, or why not ("slow down").</summary>
    public string? SendChat(string text) => SendChat(text, Brutal.GlfwApi.Glfw.Time);

    /// <summary><see cref="SendChat(string)"/> with an explicit clock (self-test).</summary>
    public string? SendChat(string text, double now)
    {
        var clean = ChatRules.Clean(text);
        if (clean.Length == 0)
            return "";
        if (!ChatRules.Allow(ref _chatTokens, ref _chatLast, now))
            return "Slow down: you're sending messages too fast";
        if (IsServer)
        {
            if (Local is { } me)
                ServerChat(me, clean, now);
        }
        else
        {
            _w.Begin(Msg.ChatSay).Str(clean);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
        return null;
    }

    /// <summary>Server: <paramref name="from"/> said <paramref name="text"/> (already cleaned). Rate-limited per player; everyone gets it.</summary>
    private void ServerChat(Player from, string text, double now)
    {
        if (from.Id != LocalId) // (the host's own lines were checked by SendChat)
        {
            (double Tokens, double Last) b = _chatBuckets.TryGetValue(from.Id, out var had) ? had : (ChatRules.Burst, now);
            var ok = ChatRules.Allow(ref b.Tokens, ref b.Last, now);
            _chatBuckets[from.Id] = b;
            if (!ok)
                return; // spam: dropped
        }
        AddChat(from.Id, from.Name, text, now);
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ChatLine).U8(from.Id).Str(text);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void AddChat(byte from, string name, string text, double now)
    {
        Chat.Add(new ChatLine(now, from, name, text));
        while (Chat.Count > ChatRules.Keep)
            Chat.RemoveAt(0);
        ChatVersion++;
        Console.WriteLine($"[chat] {name}: {text}");
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
                if (text.Length > 0)
                    ServerChat(p, text, now);
                break;
            }
            case (byte)Msg.ChatLine when !IsServer && r.Has(2):
            {
                var from = r.U8();
                var text = ChatRules.Clean(r.Str());
                if (text.Length > 0)
                    AddChat(from, NameOf(from), text, now);
                break;
            }
        }
    }

    /// <summary>Self-test: player <paramref name="p"/>'s chat line arriving at the server (as if from the network).</summary>
    public void SelfTestChatFrom(Player p, string text, double now)
    {
        var clean = ChatRules.Clean(text);
        if (clean.Length > 0)
            ServerChat(p, clean, now);
    }
}
