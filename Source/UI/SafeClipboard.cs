using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>
/// ImGui's clipboard hooks, replacing the ones the BRUTAL GLFW backend installs.
/// <para>
/// The bug (a playtester pasted an image into a text box and the game closed): on Ctrl+V ImGui asks the platform
/// backend for the clipboard text. With an image (or a file, or nothing) on the clipboard, glfwGetClipboardString
/// raises GLFW_FORMAT_UNAVAILABLE and returns NULL; BRUTAL's <c>Glfw.Clipboard</c> passes that on as a null string,
/// and <c>GlfwBackend.GetClipboardText</c> hands it straight to <c>Utf8.GetByteCount</c>, which throws
/// ArgumentNullException inside a native-to-managed callback (the backend has no try/catch), so the process dies.
/// </para>
/// <para>
/// These hooks never throw and never return NULL: no text means an empty paste (and one log line), and pasted text is
/// cut to <see cref="MaxPaste"/> characters of printable ASCII (tabs and line breaks become spaces; NULs, control
/// characters, emoji and broken UTF-8 are dropped) before ImGui copies it into a text box.
/// </para>
/// </summary>
public static unsafe class SafeClipboard
{
    /// <summary>The most characters one paste delivers (every text box in the game is far shorter).</summary>
    public const int MaxPaste = 4096;

    /// <summary>Where pasted text comes from: the system clipboard through GLFW (null when it holds no text). The self-test swaps in fakes.</summary>
    public static Func<string?> Source = () => Glfw.Clipboard;

    /// <summary>Where copied text goes (Ctrl+C / Ctrl+X in a text box). The self-test swaps in a fake.</summary>
    public static Action<string> Sink = text => Glfw.Clipboard = text;

    /// <summary>Pastes that found no text on the clipboard, and hook failures that were caught, since the game started.</summary>
    public static int NoText, Failures;

    /// <summary>The last line this class logged (the self-test reads it).</summary>
    public static string LastLog = "";

    // Native memory ImGui reads the pasted text from; it copies it straight away, so one buffer is enough.
    private static readonly byte* Buffer = (byte*)NativeMemory.AllocZeroed(MaxPaste + 1);
    private static readonly LogLimiter Limit = new(30);

    /// <summary>Installs the hooks. Call right after the GLFW backend is created (it sets its own hooks in its constructor).</summary>
    public static void Install()
    {
        var pio = ImGui.GetPlatformIO();
        pio.Platform_GetClipboardTextFn = (nint)(delegate* unmanaged[Cdecl]<nint, byte*>)&GetText;
        pio.Platform_SetClipboardTextFn = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, void>)&SetText;
        Console.WriteLine($"[ui] clipboard: safe paste hooks installed (text only, up to {MaxPaste} characters)");
    }

    /// <summary>The paste hook as ImGui calls it (through a native function pointer); the self-test calls it the same way.</summary>
    public static delegate* unmanaged[Cdecl]<nint, byte*> GetTextHook => &GetText;

    /// <summary>The copy hook as ImGui calls it.</summary>
    public static delegate* unmanaged[Cdecl]<nint, byte*, void> SetTextHook => &SetText;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte* GetText(nint context)
    {
        try
        {
            return Fill(Read());
        }
        catch (Exception ex)
        {
            Failures++;
            Log("paste", $"[ui] paste failed and was ignored: {ex}");
            Buffer[0] = 0;
            return Buffer;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void SetText(nint context, byte* text)
    {
        try
        {
            if (text == null)
                return;
            var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
            if (span.Length > 64 * 1024)
                span = span[..(64 * 1024)];
            Sink(Encoding.UTF8.GetString(span));
        }
        catch (Exception ex)
        {
            Failures++;
            Log("copy", $"[ui] copy to the clipboard failed and was ignored: {ex}");
        }
    }

    private static string Read()
    {
        string? raw;
        try
        {
            raw = Source();
        }
        catch (Exception ex)
        {
            Failures++;
            Log("read", $"[ui] could not read the clipboard, nothing pasted: {ex.GetType().Name}: {ex.Message}");
            return "";
        }
        if (raw == null)
        {
            NoText++;
            Log("notext", "[ui] paste ignored: the clipboard has no text (an image or a file?)");
            return "";
        }
        var clean = CleanPaste(raw);
        if (clean.Length < raw.Length)
            Log("cleaned", $"[ui] pasted text cleaned up: {raw.Length} characters in, {clean.Length} pasted (printable ASCII only, at most {MaxPaste})");
        return clean;
    }

    private static byte* Fill(string clean)
    {
        var n = Math.Min(clean.Length, MaxPaste);
        for (var i = 0; i < n; i++)
            Buffer[i] = (byte)clean[i]; // CleanPaste leaves printable ASCII only: one byte per character
        Buffer[n] = 0;
        return Buffer;
    }

    /// <summary>
    /// What a paste delivers: printable ASCII, tabs and line breaks turned into single spaces, everything else
    /// (NUL, control characters, non-ASCII, lone surrogates, U+FFFD from broken UTF-8) dropped, at most <see cref="MaxPaste"/> characters.
    /// </summary>
    public static string CleanPaste(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder(Math.Min(text.Length, MaxPaste));
        foreach (var c in text)
        {
            if (sb.Length >= MaxPaste)
                break;
            if (c is >= ' ' and <= '~')
                sb.Append(c);
            else if (c is '\t' or '\n' or '\r' && sb.Length > 0 && sb[^1] != ' ')
                sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// GLFW's error callback: logged, but at most once per 30 s per error code (an image on the clipboard raises
    /// FormatUnavailable on every paste attempt).
    /// </summary>
    public static void LogGlfwError(GlfwErrorCode code, string description) => Log($"glfw{(int)code}", $"GLFW {code}: {description}");

    private static void Log(string key, string line)
    {
        LastLog = line;
        if (Limit.Allow(key, out var held))
            Console.WriteLine(line + LogLimiter.HeldText(held));
    }
}
