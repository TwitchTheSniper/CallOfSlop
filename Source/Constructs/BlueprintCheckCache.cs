using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Dev builds remember which blueprint files have already passed their build check (ConstructBuild.LoadBlueprints), so a
/// second start doesn't build all of them again: about ten seconds of every headless check and self-test run. A file is
/// skipped only while its own text, the program and every data file a build reads are the same as when it passed.
/// Releases, a fresh checkout (CI) and games with mods check every blueprint, as before. The list is
/// <c>Cache/blueprints-checked.txt</c> next to the program; delete it to check everything again.
/// </summary>
public static class BlueprintCheckCache
{
    /// <summary>Data folders whose files decide whether a blueprint builds (Constructs/Blueprints itself is left out: each file has its own line).</summary>
    private static readonly string[] Folders = ["Constructs", "Parts", "Weapons", "Attachments", "Materials"];

    public static string File => Path.Combine(AppFolders.Exe, "Cache", "blueprints-checked.txt");

    private static string? _stamp;
    private static HashSet<string>? _passed;
    private static bool _dirty;

    public static bool Enabled(IReadOnlyList<Mods.Mod>? mods) => !BuildInfo.IsRelease && (mods == null || mods.Count == 0);

    /// <summary>True when this exact text passed its check under this program and this data.</summary>
    public static bool Passed(string text) => Load().Contains(Key(text));

    public static void Add(string text) => _dirty |= Load().Add(Key(text));

    /// <summary>Writes the list if it grew. A folder that can't be written just means no list.</summary>
    public static void Save()
    {
        if (!_dirty || _passed == null || _stamp == null)
            return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            var tmp = File + "." + Environment.ProcessId + ".tmp";
            System.IO.File.WriteAllLines(tmp, _passed.Order(StringComparer.Ordinal).Prepend(_stamp));
            System.IO.File.Move(tmp, File, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static HashSet<string> Load()
    {
        if (_passed != null)
            return _passed;
        _stamp = Stamp();
        _passed = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (System.IO.File.Exists(File) && System.IO.File.ReadAllLines(File) is { Length: > 0 } lines && lines[0] == _stamp)
                _passed.UnionWith(lines.Skip(1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return _passed;
    }

    private static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>The program's build id and the name and content of every file under <see cref="Folders"/>.</summary>
    private static string Stamp()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId.ToByteArray());
        var blueprints = Path.GetFullPath(ConstructBuild.BlueprintFolder) + Path.DirectorySeparatorChar;
        foreach (var folder in Folders)
        {
            var root = Path.Combine(AppFolders.Data, folder);
            if (!Directory.Exists(root))
                continue;
            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (Path.GetFullPath(path).StartsWith(blueprints, StringComparison.Ordinal))
                    continue;
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(AppFolders.Data, path).Replace('\\', '/')));
                hash.AppendData(System.IO.File.ReadAllBytes(path));
            }
        }
        return "v1 " + Convert.ToHexString(hash.GetHashAndReset());
    }
}
