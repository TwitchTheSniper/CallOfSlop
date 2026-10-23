using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CallOfSlop;

/// <summary>
/// Makes <c>callofslop://join/CODE</c> invite links open this game (Windows, for the current user only: HKCU, no admin
/// rights). Nothing is registered until the player clicks the button for it in Settings > Multiplayer, and the same
/// place removes it. The link only ever passes the invite code, which is checked like a typed one (InviteCode.TryDecode).
/// </summary>
public static class UrlProtocol
{
    private const string KeyPath = @"Software\Classes\" + InviteCode.Scheme;

    public static bool Supported => OperatingSystem.IsWindows();

    /// <summary>The program links open now, or null when not registered (or not on Windows).</summary>
    public static string? RegisteredExe()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return Read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? Read()
    {
        using var cmd = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command");
        var line = cmd?.GetValue("") as string;
        if (string.IsNullOrEmpty(line) || !line.StartsWith('"'))
            return null;
        var end = line.IndexOf('"', 1);
        return end > 1 ? line[1..end] : null;
    }

    /// <summary>True when links open this very copy of the game.</summary>
    public static bool IsThisBuild => RegisteredExe() is { } exe && Environment.ProcessPath is { } me
                                      && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(me), StringComparison.OrdinalIgnoreCase);

    /// <summary>Points callofslop:// links at this copy of the game. Returns an error message, or "" on success.</summary>
    public static string Register()
    {
        if (!OperatingSystem.IsWindows())
            return "Invite links can only be set up on Windows.";
        if (Environment.ProcessPath is not { } exe)
            return "Couldn't find this game's program file.";
        try
        {
            Write(exe);
            Console.WriteLine($"[invite] callofslop:// links now open {exe}");
            return "";
        }
        catch (Exception ex)
        {
            return $"Couldn't set up invite links: {ex.Message}";
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Write(string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("", "URL:Call of Slop invite");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon"))
            icon.SetValue("", $"\"{exe}\",0");
        using var cmd = key.CreateSubKey(@"shell\open\command");
        cmd.SetValue("", $"\"{exe}\" \"%1\"");
    }

    /// <summary>Removes the link registration. Returns an error message, or "".</summary>
    public static string Unregister()
    {
        if (!OperatingSystem.IsWindows())
            return "";
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
            Console.WriteLine("[invite] callofslop:// links no longer open the game");
            return "";
        }
        catch (Exception ex)
        {
            return $"Couldn't remove invite links: {ex.Message}";
        }
    }

    /// <summary>The invite in the command line when Windows started the game from a link (callofslop://join/CODE), or null.</summary>
    public static string? FindLink(IEnumerable<string> args) =>
        args.FirstOrDefault(a => a.TrimStart().StartsWith(InviteCode.Scheme + ":", StringComparison.OrdinalIgnoreCase));
}
