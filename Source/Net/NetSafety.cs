using System.Diagnostics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// A packet ended before its reader finished: <see cref="PacketReader"/> throws this instead of an
/// IndexOutOfRangeException, so the packet safety net can tell a short (malformed) packet from a real bug.
/// </summary>
public sealed class MalformedPacketException(string message) : Exception(message);

/// <summary>
/// Lets a repeated log line through at most once per <paramref name="everySeconds"/> per key, counting what it held
/// back, so a flood (a broken client sending garbage, a paste every frame) costs one line, not thousands.
/// </summary>
public sealed class LogLimiter(double everySeconds)
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private readonly Dictionary<string, (double Next, int Held)> _keys = new();

    /// <summary>True if a line for <paramref name="key"/> may be written now; <paramref name="held"/> is how many were held back since the last one.</summary>
    public bool Allow(string key, out int held)
    {
        var now = Clock.Elapsed.TotalSeconds;
        lock (_keys)
        {
            if (_keys.TryGetValue(key, out var k) && now < k.Next)
            {
                _keys[key] = (k.Next, k.Held + 1);
                held = 0;
                return false;
            }
            held = k.Held;
            if (_keys.Count > 256)
                _keys.Clear(); // can't grow without bound however many keys a flood makes up
            _keys[key] = (now + everySeconds, 0);
            return true;
        }
    }

    /// <summary>" (N more since the last report)" when lines were held back, else "".</summary>
    public static string HeldText(int held) => held > 0 ? $" ({held} more since the last report)" : "";
}

/// <summary>
/// Hardening for text and packets that come from other players: every string received is capped and cleaned before
/// it is shown or stored, and a packet whose handler throws is dropped with one (rate-limited) log line instead of
/// taking the game down. See <c>Game.OnPacketSafe</c> and the "Paste and packets" self-test.
/// </summary>
public static class NetSafety
{
    private static readonly LogLimiter Limit = new(10);

    /// <summary>Packets dropped because their handler threw (malformed or not), since the game started.</summary>
    public static int Dropped;

    /// <summary>The last drop's log line (the self-test reads it).</summary>
    public static string LastDrop = "";

    /// <summary>
    /// Logs a dropped packet: one line for a short packet, the exception with its stack for anything else (that's a
    /// bug worth reporting), at most once per 10 s per message type and exception type.
    /// </summary>
    public static void Drop(string from, ReadOnlySpan<byte> data, Exception ex)
    {
        Dropped++;
        var id = data.Length > 0 ? data[0] : (byte)0;
        var malformed = ex is MalformedPacketException;
        LastDrop = $"[net] dropped a bad {MsgName(id)} packet ({data.Length} bytes) from {from}: {ex.GetType().Name}: {ex.Message}";
        if (!Limit.Allow($"{id}/{ex.GetType().Name}", out var held))
            return;
        Console.WriteLine(LastDrop + LogLimiter.HeldText(held) + (malformed ? "" : Environment.NewLine + ex.StackTrace));
    }

    /// <summary>A message id's name for the log ("ZBuild", or "#200" for an unknown one).</summary>
    public static string MsgName(byte id) => Enum.IsDefined((Msg)id) ? ((Msg)id).ToString() : $"#{id}";

    /// <summary>
    /// Text for the screen or a file: printable ASCII only (the font draws nothing else; control characters, NULs and
    /// broken UTF-8 are dropped, tabs and line breaks become spaces), trimmed, at most <paramref name="max"/> characters.
    /// </summary>
    public static string CleanText(string? s, int max)
    {
        if (string.IsNullOrEmpty(s) || max <= 0)
            return "";
        var clean = true;
        foreach (var c in s)
            if (c is < ' ' or > '~')
            {
                clean = false;
                break;
            }
        if (clean && s.Length <= max && s[0] != ' ' && s[^1] != ' ')
            return s; // the usual case: nothing to do, no allocation
        var sb = new StringBuilder(Math.Min(s.Length, max));
        foreach (var c in s)
        {
            if (sb.Length >= max)
                break;
            if (c is >= ' ' and <= '~')
                sb.Append(c);
            else if (c is '\t' or '\n' or '\r' && sb.Length > 0 && sb[^1] != ' ')
                sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    /// <summary>Removes control characters (NUL, line breaks, escapes) but keeps everything else, for text used as a key (map and part names).</summary>
    public static string StripControls(string s)
    {
        foreach (var c in s)
            if (char.IsControl(c))
                return new string(s.Where(ch => !char.IsControl(ch)).ToArray());
        return s;
    }
}
