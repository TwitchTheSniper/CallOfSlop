using Brutal.GlfwApi;

namespace CallOfSlop;

public static unsafe partial class Program
{
    /// <summary>The window's and taskbar's icon (tools/branding/make_icon.py). A platform that has none (macOS, Wayland) or a missing file only keeps the default.</summary>
    private static void SetWindowIcon()
    {
        try
        {
            var (w, h, rgba) = Png.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Branding", "CallOfSlop.png")));
            fixed (byte* pixels = rgba)
                _window.SetIcon(1, new GlfwImage { Width = w, Height = h, Pixels = pixels });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[icon] {ex.Message}");
        }
    }
}
