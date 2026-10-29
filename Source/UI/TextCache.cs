using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// Text made every frame without a new string every frame. <c>TextCache.Of($"Hold [E] to buy the {name} [{cost}]")</c>
/// formats into a reused buffer and hands back the string it made last time it saw the same characters, so text that
/// only changes now and then (a prompt, a label, a count) costs a string when it changes, not once per frame. Use it
/// where a string has to come out (a field, a method that returns one, a ?: between two texts); where the text goes
/// straight into an ImGui call, a <c>$"..."</c> argument already formats into ImGui's frame arena for free. The cache
/// is per thread and forgets everything past <see cref="MaxEntries"/> texts (a text that changes every frame, like a
/// timer with decimals, just keeps refilling it: still one string per change, as before).
/// </summary>
public static class TextCache
{
    /// <summary>Texts kept per thread before the cache starts again.</summary>
    public const int MaxEntries = 2048;

    [ThreadStatic] private static char[]? _buffer;
    [ThreadStatic] private static Dictionary<string, string>? _texts;
    [ThreadStatic] private static int _open; // handlers being filled: a TextCache.Of inside another's hole gets its own buffer

    /// <summary>The text, as a string that is reused while the same characters come back.</summary>
    public static string Of(ref Handler text)
    {
        _open--;
        return Intern(text.Written);
    }

    /// <summary>The same for characters already in a span.</summary>
    public static string Of(ReadOnlySpan<char> text) => Intern(text);

    /// <summary>Texts cached on this thread now (self-test).</summary>
    public static int Count => _texts?.Count ?? 0;

    private static string Intern(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return "";
        var texts = _texts ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var lookup = texts.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(text, out var found))
            return found;
        if (texts.Count >= MaxEntries)
            texts.Clear();
        var made = new string(text);
        texts[made] = made;
        return made;
    }

    private static char[] Buffer(int atLeast)
    {
        var b = _buffer;
        if (b == null || b.Length < atLeast)
            _buffer = b = new char[Math.Max(atLeast, Math.Max(256, (b?.Length ?? 0) * 2))];
        return b;
    }

    /// <summary>Formats an interpolated string into the thread's reused buffer (growing it when a text doesn't fit).</summary>
    [InterpolatedStringHandler]
    public ref struct Handler
    {
        private char[] _chars;
        private int _pos;

        public Handler(int literalLength, int formattedCount)
        {
            _chars = _open++ == 0 ? Buffer(literalLength + formattedCount * 11) : new char[literalLength + formattedCount * 11 + 16];
            _pos = 0;
        }

        internal readonly ReadOnlySpan<char> Written => _chars.AsSpan(0, _pos);

        public void AppendLiteral(string value) => AppendSpan(value.AsSpan());

        public void AppendFormatted(string? value) => AppendSpan(value.AsSpan());

        public void AppendFormatted(ReadOnlySpan<char> value) => AppendSpan(value);

        public void AppendFormatted<T>(T value) => AppendFormatted(value, null);

        public void AppendFormatted<T>(T value, string? format)
        {
            if (value is ISpanFormattable)
            {
                int written;
                while (!((ISpanFormattable)value).TryFormat(_chars.AsSpan(_pos), out written, format, null))
                    Grow(_chars.Length);
                _pos += written;
            }
            else if (value is IFormattable)
                AppendSpan(((IFormattable)value).ToString(format, null).AsSpan()); // (not span-formattable: allocates, as a plain $"..." would)
            else
                AppendSpan((value?.ToString()).AsSpan());
        }

        private void AppendSpan(ReadOnlySpan<char> s)
        {
            if (_pos + s.Length > _chars.Length)
                Grow(s.Length);
            s.CopyTo(_chars.AsSpan(_pos));
            _pos += s.Length;
        }

        private void Grow(int more)
        {
            var bigger = new char[Math.Max(_chars.Length * 2, _pos + more)];
            _chars.AsSpan(0, _pos).CopyTo(bigger);
            if (ReferenceEquals(_chars, _buffer))
                _buffer = bigger;
            _chars = bigger;
        }
    }
}
