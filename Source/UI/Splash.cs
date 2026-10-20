using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The BRUTAL logo shown at launch, before the menus. The BRUTAL licence (section 2.2) asks for the official logo,
/// unaltered except for scaling, before gameplay, for long enough to be legible and no less prominent than any other
/// logo. The file in Branding/ is the official LOGO_BRUTAL_WHT.png, byte for byte (the self-test checks its hash).
/// It fades in on a dark screen, holds, and fades out; a click or key skips it at any time (it then fades out quickly).
/// </summary>
public static unsafe class Splash
{
    public const string LogoFile = "Branding/LOGO_BRUTAL_WHT.png";

    /// <summary>SHA-256 of the official logo file, so a changed or re-saved copy is caught.</summary>
    public const string LogoSha256 = "6e259ecd95b6e0cbc52744b59ffeee3646be9ba4d8b640d9864938c49fdb9556";

    public const double FadeIn = 0.35, Hold = 2.2, FadeOut = 0.45, SkipFade = 0.2;
    public const double Total = FadeIn + Hold + FadeOut;

    /// <summary>How long the logo is up before a click or key can skip it (0 = straight away). A skip still fades the logo out.</summary>
    public const double MinSeconds = 0;

    private static double _start = -1, _skipAt = -1;
    private static uint _texture;
    private static int _w, _h;
    private static bool _loaded, _done;

    /// <summary>Seconds since the splash first showed (0 before).</summary>
    public static double Elapsed(double now) => _start < 0 ? 0 : now - _start;

    /// <summary>Still showing (call once a frame before drawing the menus).</summary>
    public static bool Active(double now)
    {
        if (_done)
            return false;
        if (_start < 0)
            _start = now;
        var end = _skipAt >= 0 ? _skipAt + SkipFade : _start + Total;
        if (now >= end)
        {
            _done = true;
            Release();
            return false;
        }
        return true;
    }

    /// <summary>Draws the splash over everything (inside the ImGui frame). <paramref name="allowSkip"/> = clicks and keys can end it early.</summary>
    public static void Draw(double now, bool allowSkip)
    {
        if (!_loaded)
            Load();
        var t = now - _start;
        if (allowSkip && _skipAt < 0 && t >= MinSeconds
            && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsKeyPressed(ImGuiKey.Escape, false)
                || ImGui.IsKeyPressed(ImGuiKey.Space, false) || ImGui.IsKeyPressed(ImGuiKey.Enter, false)))
            _skipAt = now;
        var fadeOutStart = _skipAt >= 0 ? _skipAt : _start + FadeIn + Hold;
        var a = (float)Math.Clamp(Math.Min(t / FadeIn, 1 - (now - fadeOutStart) / (_skipAt >= 0 ? SkipFade : FadeOut)), 0, 1);

        var size = ImGui.GetIO().DisplaySize;
        var dl = ImGui.GetForegroundDrawList();
        dl.AddRectFilled(new float2(0, 0), size, new ImColor8(10, 10, 12, 255)); // opaque: nothing else shows through
        var alpha = (byte)(a * 255);
        if (_texture != 0)
        {
            // Big and centred: about 45% of the screen width, at most 4x the file's size, aspect kept.
            var w = MathF.Min(size.X * 0.45f, _w * 4f);
            w = MathF.Min(w, size.Y * 0.8f * _w / _h);
            var h = w * _h / _w;
            var p0 = new float2((size.X - w) / 2, (size.Y - h) / 2);
            var tex = new ImTextureRef { _TexID = new ImTextureID((nint)_texture) };
            dl.AddImage(tex, p0, p0 + new float2(w, h), col: new ImColor8(255, 255, 255, alpha));
        }
        else
        {
            dl.AddText(new float2(size.X / 2 - 60, size.Y / 2 - 10), new ImColor8(240, 196, 16, alpha), "BRUTAL");
        }
    }

    private static void Load()
    {
        _loaded = true;
        var path = Path.Combine(AppContext.BaseDirectory, LogoFile);
        try
        {
            var (w, h, rgba) = Png.Decode(File.ReadAllBytes(path));
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            _texture = tex.Id;
            _w = w;
            _h = h;
            Native.glBindTexture(GL_TEXTURE_2D, _texture);
            Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
            fixed (byte* p = rgba)
                Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            Native.glBindTexture(GL_TEXTURE_2D, 0);
            Console.WriteLine($"[splash] BRUTAL logo {w}x{h}, GL error 0x{(uint)Native.glGetError():X}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[splash] couldn't load {path}: {ex.Message} (the BRUTAL licence needs this logo shown; reinstall the game files)");
        }
    }

    private static void Release()
    {
        if (_texture == 0)
            return;
        var tex = new GlTexture(_texture);
        Native.glDeleteTextures(1, &tex);
        _texture = 0;
    }
}
