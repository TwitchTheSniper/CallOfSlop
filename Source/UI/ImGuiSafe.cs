using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Drop-in replacements for the ImGui text calls that treat their text as a printf format string.
/// <para>
/// In the BRUTAL bindings, <c>ImGui.TextColored</c>, <c>TextWrapped</c>, <c>TextDisabled</c>, <c>BulletText</c>,
/// <c>SetTooltip</c> and <c>SetItemTooltip</c> pass the text straight to the C++ <c>fmt, ...</c> functions with no
/// arguments. Any '%' in the text is then read as a format specifier: "+45% range" or "Move 80% faster" turn into
/// garbage like "+45 0.000000aster", random bytes that the font draws as '?', or even a crash (a player called "%s").
/// Only <c>ImGui.Text</c> is safe (it maps to <c>TextUnformatted</c>).
/// </para>
/// <para>
/// These versions double every '%' first, so the text shows exactly as written. Use them whenever the text isn't a
/// constant u8 literal without '%' (the "Text" self-test scans the source for that). Text without '%' is passed
/// through as is, so they cost nothing extra in the normal case.
/// </para>
/// </summary>
public static class ImGuiSafe
{
    private static byte[] _buffer = new byte[256]; // UI thread only; reused for every escaped string

    /// <summary><c>ImGui.TextColored</c> without printf formatting.</summary>
    public static void TextColored(in float4 col, ImString text) => ImGui.TextColored(col, Escape(text));

    /// <summary><c>ImGui.TextWrapped</c> without printf formatting.</summary>
    public static void TextWrapped(ImString text) => ImGui.TextWrapped(Escape(text));

    /// <summary><c>ImGui.TextDisabled</c> without printf formatting.</summary>
    public static void TextDisabled(ImString text) => ImGui.TextDisabled(Escape(text));

    /// <summary><c>ImGui.BulletText</c> without printf formatting.</summary>
    public static void BulletText(ImString text) => ImGui.BulletText(Escape(text));

    /// <summary><c>ImGui.SetTooltip</c> without printf formatting.</summary>
    public static void SetTooltip(ImString text) => ImGui.SetTooltip(Escape(text));

    /// <summary><c>ImGui.SetItemTooltip</c> without printf formatting.</summary>
    public static void SetItemTooltip(ImString text) => ImGui.SetItemTooltip(Escape(text));

    /// <summary>
    /// The text with every '%' doubled, as a null-terminated UTF-8 string ready for a printf-style ImGui call.
    /// Text without '%' comes back unchanged. The result lives until the next call.
    /// </summary>
    public static ImString Escape(ImString text)
    {
        ReadOnlySpan<byte> s = text.AsSpan();
        var count = s.Count((byte)'%');
        if (count == 0)
            return text;
        var need = s.Length + count + 1;
        if (_buffer.Length < need)
            _buffer = new byte[Math.Max(need, _buffer.Length * 2)];
        var n = 0;
        foreach (var b in s)
        {
            _buffer[n++] = b;
            if (b == (byte)'%')
                _buffer[n++] = b;
        }
        _buffer[n] = 0; // native code reads up to the terminator
        return new ImString(new ReadOnlySpan<byte>(_buffer, 0, n));
    }

    /// <summary>The same escaping for a managed string (used by the self-test).</summary>
    public static string EscapeString(string text) => text.Replace("%", "%%");
}
