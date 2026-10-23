using System.Globalization;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The <c>.callofslop-build</c> file the updater writes into every build folder it unzips (UpdateInstaller.Extract). It is
/// the proof that a folder was made by the updater, which is what lets <see cref="UpdateCleanup"/> remove old versions:
/// a folder without it (a build you unzipped yourself, a dev copy, anything else) is never deleted.
/// <code>
/// # Call of Slop build folder made by the in-game updater (do not copy this file into other folders)
/// format=1
/// version=0.2026.1022.5
/// folder=CallOfSlop-0.2026.1022.5
/// installed=2026-10-23T12:00:00.0000000Z
/// state=done            (partial while unzipping)
/// confirmed=...         (written when the build first reaches the main menu)
/// </code>
/// The <c>folder=</c> line must match the folder's own name, so a renamed or copied folder ("CallOfSlop-0.2026.1022.5 -
/// Copy", "Keep me") is no longer proven and is left alone.
/// </summary>
public static class BuildMarker
{
    public const string FileName = ".callofslop-build";

    public sealed record Info(Version Version, string Folder, DateTime InstalledUtc, bool Partial, DateTime? ConfirmedUtc);

    /// <summary>Writes the marker into <paramref name="folder"/> (whose final name is <paramref name="finalName"/>).</summary>
    public static void Write(string folder, Version version, string finalName, bool partial, DateTime installedUtc, DateTime? confirmedUtc = null)
    {
        var sb = new StringBuilder();
        sb.Append("# Call of Slop build folder made by the in-game updater (do not copy this file into other folders)\n");
        sb.Append("format=1\n");
        sb.Append("version=").Append(version).Append('\n');
        sb.Append("folder=").Append(finalName).Append('\n');
        sb.Append("installed=").Append(installedUtc.ToString("o", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("state=").Append(partial ? "partial" : "done").Append('\n');
        if (confirmedUtc is { } c)
            sb.Append("confirmed=").Append(c.ToString("o", CultureInfo.InvariantCulture)).Append('\n');
        var path = Path.Combine(folder, FileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The marker in <paramref name="folder"/>, or null when there is none or it doesn't parse.</summary>
    public static Info? Read(string folder)
    {
        try
        {
            var path = Path.Combine(folder, FileName);
            if (!File.Exists(path) || new FileInfo(path).Length > 4096)
                return null;
            Version? version = null;
            string? name = null;
            var installed = DateTime.MinValue;
            DateTime? confirmed = null;
            var partial = false;
            var format = false;
            foreach (var line in File.ReadLines(path).Take(40))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith('#'))
                    continue;
                var (k, v) = (line[..eq].Trim(), line[(eq + 1)..].Trim());
                switch (k)
                {
                    case "format": format = v == "1"; break;
                    case "version": version = BuildInfo.ParseRelease(v); break;
                    case "folder": name = v; break;
                    case "state": partial = v != "done"; break;
                    case "installed" when TryTime(v, out var t): installed = t; break;
                    case "confirmed" when TryTime(v, out var t): confirmed = t; break;
                }
            }
            return format && version != null && !string.IsNullOrEmpty(name) ? new Info(version, name, installed, partial, confirmed) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryTime(string v, out DateTime t) =>
        DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t);

    /// <summary>
    /// The build reached the main menu: records it in its own marker (once), so the cleanup knows this build runs and
    /// prefers it as the rollback copy. False when the folder has no marker (not an updater build) or it can't be written.
    /// </summary>
    public static bool Confirm(string buildFolder, DateTime nowUtc)
    {
        var m = Read(buildFolder);
        if (m == null || m.Partial)
            return false;
        if (m.ConfirmedUtc != null)
            return true;
        try
        {
            Write(buildFolder, m.Version, m.Folder, partial: false, m.InstalledUtc, nowUtc);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[update] couldn't mark {buildFolder} as working: {ex.Message}");
            return false;
        }
    }
}
