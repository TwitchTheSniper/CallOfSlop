using System.Globalization;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// What a part says in words (docs/sandbox/CONSTRUCTS.md, "Displays"): a sign's two messages, a lamp bank's labels, held on the
/// part (<see cref="CPart.Text"/>, <see cref="CPart.Text2"/>) so they move with it. They ride in CBody, a blueprint step's
/// <c>"text"</c> list and a world save's settings: a world file has settings by key and value, so a message goes there as three
/// characters a float under the keys "text0.0", "text0.1"..., which an older reader skips.
/// </summary>
public static class LogicText
{
    public const int MaxChars = 60;

    public static string Clean(string s)
    {
        var b = new StringBuilder(Math.Min(s.Length, MaxChars));
        foreach (var ch in s)
            if (ch is >= ' ' and <= '~' && b.Length < MaxChars)
                b.Append(ch);
        return b.ToString();
    }

    public static string Get(in CPart p, int slot) => (slot == 0 ? p.Text : p.Text2) ?? "";

    public static void Set(ref CPart p, int slot, string text)
    {
        var clean = Clean(text);
        var v = clean.Length == 0 ? null : clean;
        if (slot == 0)
            p.Text = v;
        else
            p.Text2 = v;
    }

    /// <summary>CBody's tail: every message set, as (part, slot, text).</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        var at = w.Length;
        w.U16(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Text != null)
            {
                w.U16((ushort)i).U8(0).Str(p.Text);
                n++;
            }
            if (p.Text2 != null)
            {
                w.U16((ushort)i).U8(1).Str(p.Text2);
                n++;
            }
        }
        w.PatchU16(at, (ushort)n);
    }

    public static void Read(ref PacketReader r, Construct c)
    {
        int n = r.U16();
        for (var k = 0; k < n; k++)
        {
            var part = r.U16();
            var slot = r.U8();
            var text = r.Text(MaxChars);
            if (part >= c.PartCount || slot >= c.Parts[part].D.TextSlots)
                throw new MalformedPacketException($"a message on part {part}");
            Set(ref c.Parts[part], slot, text);
        }
    }

    /// <summary>A blueprint step's "text" value: the messages as a JSON list, or "".</summary>
    public static string ToJson(in CPart p)
    {
        if (p.Text == null && p.Text2 == null)
            return "";
        return "[" + System.Text.Json.JsonSerializer.Serialize(p.Text ?? "") + (p.Text2 != null ? ", " + System.Text.Json.JsonSerializer.Serialize(p.Text2) : "") + "]";
    }

    /// <summary>A world save: the settings key of chunk <paramref name="chunk"/> of message <paramref name="slot"/>.</summary>
    public static string SaveKey(int slot, int chunk) => string.Create(CultureInfo.InvariantCulture, $"text{slot}.{chunk}");

    /// <summary>Three characters of a message in a float's exact range (a null end marks a short chunk).</summary>
    public static float Pack(string text, int chunk)
    {
        var at = chunk * 3;
        int C(int k) => at + k < text.Length ? text[at + k] & 0x7F : 0;
        return C(0) | C(1) << 7 | C(2) << 14;
    }

    /// <summary>A world save's setting that may be a chunk of a message: true when it was, and it went on the part.</summary>
    public static bool LoadChunk(ref CPart p, string key, float v)
    {
        if (key.Length < 7 || !key.StartsWith("text", StringComparison.Ordinal) || key[5] != '.' || !int.TryParse(key.AsSpan(6), out var chunk))
            return false;
        var slot = key[4] - '0';
        if (slot is not (0 or 1) || chunk is < 0 or >= MaxChars)
            return false;
        var chars = Get(p, slot).PadRight(chunk * 3, ' ').ToCharArray();
        var b = new StringBuilder(new string(chars[..Math.Min(chars.Length, chunk * 3)]));
        var bits = (int)v;
        for (var k = 0; k < 3; k++)
        {
            var ch = (char)(bits >> (7 * k) & 0x7F);
            if (ch != 0)
                b.Append(ch);
        }
        Set(ref p, slot, b.ToString());
        return true;
    }
}
