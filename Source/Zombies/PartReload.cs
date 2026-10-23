using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace CallOfSlop;

/// <summary>What a <see cref="Parts.Reload()"/> found, compared with the parts before it.</summary>
public sealed record PartReload(int Count, string[] Added, string[] Removed, string[] Changed, string[] Kept, string[] Errors, bool FingerprintChanged)
{
    /// <summary>A one-line summary for the pause menu, the HUD feed and the log.</summary>
    public string Summary
    {
        get
        {
            var bits = new List<string> { $"{Count} part(s)" };
            if (Changed.Length > 0)
                bits.Add($"changed: {List(Changed)}");
            if (Added.Length > 0)
                bits.Add($"new: {List(Added)}");
            if (Removed.Length > 0)
                bits.Add($"removed: {List(Removed)}");
            if (Kept.Length > 0)
                bits.Add($"kept the old version of {List(Kept)} (file has errors)");
            if (Changed.Length + Added.Length + Removed.Length == 0)
                bits.Add("no changes");
            if (Errors.Length > 0)
                bits.Add($"{Errors.Length} error(s): {Errors[0]}{(Errors.Length > 1 ? " (more in callofslop.log)" : "")}");
            return "Parts reloaded: " + string.Join("; ", bits);
        }
    }

    private static string List(string[] names) => names.Length <= 4 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(4))} and {names.Length - 4} more";
}

/// <summary>
/// A part's full content as text (every field, nested blocks and model boxes included; not its file name). Equal
/// digests mean the parts behave and look the same however their files were written (in full, with fragments or with
/// "extends"). Used by hot reload (what changed) and the parts fingerprint (<see cref="PartSet"/>).
/// </summary>
public static class PartDigest
{
    public static string Of(PartDef p)
    {
        var sb = new StringBuilder(256);
        Dump(sb, p, 0);
        return sb.ToString();
    }

    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();

    private static void Dump(StringBuilder sb, object? v, int depth)
    {
        switch (v)
        {
            case null: sb.Append('~'); return;
            case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
            case string s: sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"'); return;
            case bool b: sb.Append(b ? 'T' : 'F'); return;
            case Enum e: sb.Append(e.ToString()); return;
            case IFormattable n when v.GetType().IsPrimitive: sb.Append(n.ToString(null, CultureInfo.InvariantCulture)); return;
            case MemberInfo m: sb.Append(m.Name); return;
            case Delegate: return;
            case Array a:
                sb.Append('[');
                foreach (var x in a)
                {
                    Dump(sb, x, depth + 1);
                    sb.Append(',');
                }
                sb.Append(']');
                return;
        }
        if (depth > 8)
        {
            sb.Append('?');
            return;
        }
        var t = v.GetType();
        FieldInfo[]? fields;
        lock (FieldCache)
            if (!FieldCache.TryGetValue(t, out fields))
                FieldCache[t] = fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !(t == typeof(PartDef) && f.Name == nameof(PartDef.SourceFile)))
                    .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        sb.Append('{');
        if (fields.Length == 0 && t.IsValueType)
        {
            // A record struct (GunColor): its positional properties.
            foreach (var pr in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(pr => pr.CanRead && pr.GetIndexParameters().Length == 0).OrderBy(pr => pr.Name, StringComparer.Ordinal))
            {
                sb.Append(pr.Name).Append('=');
                Dump(sb, pr.GetValue(v), depth + 1);
                sb.Append(';');
            }
        }
        foreach (var f in fields)
        {
            sb.Append(f.Name).Append('=');
            Dump(sb, f.GetValue(v), depth + 1);
            sb.Append(';');
        }
        sb.Append('}');
    }
}

/// <summary>
/// The loaded part set as a whole: one fingerprint host and clients compare when someone joins (Zombies sends parts by
/// their position in <see cref="Parts.All"/> and builds by name, so both sides need the same Parts folder).
/// </summary>
public static class PartSet
{
    private static uint? _hash;

    /// <summary>FNV-1a over every part's <see cref="PartDigest"/>, in <see cref="Parts.All"/> order.</summary>
    public static uint Hash => _hash ??= WeaponSet.Fnv(Parts.All.Select(PartDigest.Of));

    /// <summary>Forgets the cached fingerprint (after a reload).</summary>
    internal static void Forget() => _hash = null;

    /// <summary>Client side of the Welcome check: null when our parts match the host's, otherwise what to do about it.</summary>
    public static string? Mismatch(uint hostHash, int hostCount)
    {
        if (hostHash == Hash)
            return null;
        var mine = Parts.All.Length;
        var what = hostCount != mine
            ? $"your Parts folder has {mine} parts but the host's has {hostCount}"
            : $"your Parts folder has the same {mine} parts as the host's, but they're set up differently";
        return $"Can't join: {what}. Everyone in a game needs identical Parts folders (Zombies weapon builder parts): copy the host's over yours. "
               + $"(part set {hostHash:x8} on the host, {Hash:x8} here)";
    }
}

public static partial class Parts
{
    /// <summary>Goes up by one on every <see cref="Reload()"/>, so screens can tell the parts changed.</summary>
    public static int Generation { get; private set; }

    /// <summary>
    /// Raised after every <see cref="Reload()"/> (on the thread that called it): screens re-assemble what they show
    /// (the workbench preview, Ui.OnPartsReloaded), caches of built weapons are dropped.
    /// </summary>
    public static event Action<PartReload>? Reloaded;

    /// <summary>
    /// Re-reads the Parts folder while the game runs ("Reload part files" in the pause menu, --watch-parts). A part that
    /// already existed keeps its object: its fields are updated in place, so every build, workbench slot and standard
    /// build holding it sees the new version at once (only cached weapons need re-assembling, see <see cref="Reloaded"/>).
    /// A part whose file now has errors keeps its previous version (so a typo mid-edit doesn't pull it off your gun).
    /// New parts are added, removed ones leave the list (builds holding them keep working until they're rebuilt).
    /// Changes what <see cref="All"/> holds: offline or hosting with nobody connected only (see Game.ReloadPartFiles).
    /// </summary>
    public static PartReload Reload() => Reload(Folder);

    /// <summary><see cref="Reload()"/> from another folder (the self-test reloads from a temporary copy, then from the real folder again).</summary>
    public static PartReload Reload(string folder)
    {
        PartReload r;
        lock (LoadLock)
        {
            var old = All;
            var oldDigest = old.ToDictionary(p => p.Name, PartDigest.Of, StringComparer.OrdinalIgnoreCase);
            var oldHash = PartSet.Hash;
            var errors = new List<string>();
            var (fresh, lib, failed) = LoadDetailed(folder, errors);
            var byName = old.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            var list = new List<PartDef>();
            List<string> added = [], changed = [], kept = [];
            foreach (var f in fresh)
            {
                if (byName.Remove(f.Name, out var o))
                {
                    if (PartDigest.Of(f) != oldDigest[o.Name])
                    {
                        CopyInto(f, o);
                        changed.Add(o.Name);
                    }
                    o.SourceFile = f.SourceFile;
                    list.Add(o);
                }
                else
                {
                    list.Add(f);
                    added.Add(f.Name);
                }
            }
            foreach (var o in byName.Values.ToList())
                if (o.SourceFile != null && failed.Contains(o.SourceFile.Replace('\\', '/')) && !list.Any(p => p.Name.Equals(o.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(o); // its file is broken right now: keep what we had
                    kept.Add(o.Name);
                    byName.Remove(o.Name);
                }
            _all = Sort(list);
            _library = lib;
            LoadErrors.Clear();
            LoadErrors.AddRange(errors);
            Generation++;
            PartSet.Forget();
            r = new PartReload(_all.Length, added.ToArray(), byName.Keys.Order().ToArray(), changed.ToArray(), kept.ToArray(), errors.ToArray(), PartSet.Hash != oldHash);
        }
        StandardBuilds.Reload(); // Parts/Builds: re-read too (they name parts)
        Game.ForgetAssembled(); // cached weapons were assembled from the old versions
        Console.WriteLine("[parts] " + r.Summary);
        Reloaded?.Invoke(r);
        return r;
    }

    /// <summary>Copies every field of <paramref name="from"/> into <paramref name="to"/> (hot reload: same object, new content).</summary>
    internal static void CopyInto<T>(T from, T to) where T : class
    {
        foreach (var f in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (!f.IsInitOnly)
                f.SetValue(to, f.GetValue(from));
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.SetMethod!.IsPublic)
                p.SetValue(to, p.GetValue(from));
    }

    /// <summary>
    /// A built weapon assembled again from its <see cref="WeaponDef.BuildText"/> with the current parts and copied into
    /// <paramref name="def"/> in place (its id stays, so slots, kits and the network see the same weapon). False if it
    /// isn't a built weapon or no longer assembles (a part was removed or a rule now refuses it): then it stays as it was.
    /// </summary>
    public static bool Rebuild(WeaponDef def, out string? problem)
    {
        problem = null;
        if (def.BuildText is not { } text)
            return false;
        var dropped = new List<string>();
        var b = WeaponBuild.Parse(text, Find, dropped);
        var a = WeaponBuilder.Assemble(b);
        if (a.Def == null || dropped.Count > 0)
        {
            problem = $"{def.Name}: " + (dropped.Count > 0 ? $"dropped {string.Join(", ", dropped)}" : (a.Problems.Count > 0 ? a.Problems[0].Message : "doesn't assemble"));
            return false;
        }
        var id = def.Id;
        CopyInto(a.Def, def);
        def.Id = id;
        def.BuildText = text;
        return true;
    }

    /// <inheritdoc cref="Rebuild(WeaponDef, out string?)"/>
    public static bool Rebuild(MeleeDef def, out string? problem)
    {
        problem = null;
        if (def.BuildText is not { } text)
            return false;
        var dropped = new List<string>();
        var a = WeaponBuilder.Assemble(WeaponBuild.Parse(text, Find, dropped));
        if (a.Melee == null || dropped.Count > 0)
        {
            problem = $"{def.Name}: " + (dropped.Count > 0 ? $"dropped {string.Join(", ", dropped)}" : (a.Problems.Count > 0 ? a.Problems[0].Message : "doesn't assemble"));
            return false;
        }
        var view = def.View;
        CopyInto(a.Melee.View, view); // the first-person stand-in, kept as the same object too
        CopyInto(a.Melee, def);
        def.View = view;
        return true;
    }

    // ---- Cross-part checks ----------------------------------------------------------------------------------------

    /// <summary>
    /// Checks the set as a whole (one file can't see these): a part that fits nowhere (no socket of its mount type takes
    /// its category), a socket nothing fits (no part mounts on its type with a category it accepts). <paramref name="notes"/>
    /// gets softer findings: registry socket types nothing uses, and "accepts" entries no part of that category can fill.
    /// </summary>
    public static List<string> Lint(IReadOnlyList<PartDef> parts, PartRegistry registry, List<string>? notes = null)
    {
        var problems = new List<string>();
        bool Takes(PartSocket s, PartDef p) => s.Type == p.Mount && (s.Accepts.Length == 0 || s.Accepts.Contains(p.Category));
        foreach (var p in parts)
        {
            if (p.Mount == "")
                continue; // a grip or hilt: weapons are built on it
            if (!parts.Any(q => q.Sockets.Any(s => Takes(s, p))))
                problems.Add($"{p.Name} ({p.SourceFile}) fits nowhere: no part has a \"{p.Mount}\" socket that takes {p.Category.ToString().ToLowerInvariant()} parts");
        }
        foreach (var q in parts)
            foreach (var s in q.Sockets)
            {
                if (!parts.Any(p => Takes(s, p)))
                    problems.Add($"{q.Name} ({q.SourceFile}): socket \"{s.Name}\" takes nothing: no part mounts on \"{s.Type}\""
                                 + (s.Accepts.Length > 0 ? $" with category {string.Join("/", s.Accepts.Select(c => c.ToString().ToLowerInvariant()))}" : ""));
                else
                    foreach (var c in s.Accepts)
                        if (!parts.Any(p => p.Category == c && p.Mount == s.Type))
                            notes?.Add($"{q.Name}: socket \"{s.Name}\" accepts {c.ToString().ToLowerInvariant()} parts, but none mounts on \"{s.Type}\"");
            }
        foreach (var t in registry.SocketTypes)
            if (!parts.Any(p => p.Mount == t || p.Sockets.Any(s => s.Type == t)))
                notes?.Add($"socket type \"{t}\" ({registry.Source ?? "built-in"}) isn't used by any part yet");
        return problems;
    }
}

/// <summary>
/// The modders' command-line helpers for parts: --check-part FILE, --check-parts [DIR], --export-parts DIR [--resolved].
/// Like --check-map and --check-weapon: exit code 0 = fine, 1 = problems (printed).
/// </summary>
public static class PartTools
{
    /// <summary>The Parts folder <paramref name="file"/> sits in (the nearest folder up called Parts), or null.</summary>
    public static string? PartsFolderOf(string file)
    {
        for (var d = Path.GetDirectoryName(Path.GetFullPath(file)); d != null; d = Path.GetDirectoryName(d))
            if (Path.GetFileName(d).Equals("Parts", StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    /// <summary>"CallOfSlop.exe --check-part FILE": a part, fragment, base or registry file. Prints what it is and where it fits.</summary>
    public static int Check(string file)
    {
        try
        {
            var folder = PartsFolderOf(file) ?? Parts.Folder;
            var libErrors = new List<string>();
            var lib = PartLibrary.FromFolder(folder, libErrors);
            var full = Path.GetFullPath(file);
            var inside = full.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            var rel = inside ? Path.GetRelativePath(folder, full).Replace('\\', '/') : Path.GetFileName(file);
            var top = rel.Split('/')[0];
            var text = File.ReadAllText(file);
            Console.WriteLine($"library: {folder} ({lib.Fragments.Count} fragment(s), {lib.Bases.Count} base(s), registry {lib.Registry.Source ?? "built-in"})");
            if (rel.Equals(PartLibrary.RegistryFile, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).Equals(PartLibrary.RegistryFile, StringComparison.OrdinalIgnoreCase))
            {
                var reg = PartRegistry.Load(text, rel);
                Console.WriteLine($"OK: registry: {reg.SocketTypes.Length} socket types ({string.Join(", ", reg.SocketTypes)}), {reg.Bores.Length} bores ({string.Join(", ", reg.Bores)})");
                return 0;
            }
            if (top.Equals(PartLibrary.FragmentFolder, StringComparison.OrdinalIgnoreCase))
            {
                lib.Fragments.Remove(FragmentName(text) ?? "");
                var f = lib.AddFragment(text, rel);
                using var use = System.Text.Json.JsonDocument.Parse($"{{\"use\": {WeaponFile.Str(f.Name)}}}");
                var boxes = lib.Use(use.RootElement, rel, 0).ToArray();
                var users = UsersOf(lib, f.Name);
                Console.WriteLine($"OK: fragment '{f.Name}': {boxes.Length} box(es){(f.About.Length > 0 ? $" - {f.About}" : "")}");
                Console.WriteLine($"used by {users.Count} part(s){(users.Count > 0 ? ": " + string.Join(", ", users.Take(20)) : "")}");
                return 0;
            }
            if (top.Equals(PartLibrary.BaseFolder, StringComparison.OrdinalIgnoreCase))
            {
                var users = lib.PartTexts.Where(t => t.Text.Contains("\"extends\"", StringComparison.OrdinalIgnoreCase)).Select(t => t.Source).ToList();
                Console.WriteLine($"OK: base file (bases are checked through the parts that extend them; {users.Count} part file(s) use \"extends\")");
                return 0;
            }
            var p = PartFile.Load(text, rel, lib);
            var errors = new List<string>();
            var all = Parts.LoadDetailed(folder, errors).Parts.Where(x => !x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)).Append(p).ToList();
            Console.WriteLine($"OK: '{p.Name}' ({PartDef.CategoryName(p.Category)}, {p.Rarity}{(p.Mount != "" ? $", mounts on \"{p.Mount}\"" : ", root")}): {p.Boxes.Length} box(es), "
                              + $"{p.Sockets.Length} socket(s){(p.Muzzles.Length > 0 ? $", {p.Muzzles.Length} muzzle(s)" : "")}, {p.Weight:0.###} kg, scrap {p.Scrap}, "
                              + $"fingerprint {WeaponSet.Fnv([PartDigest.Of(p)]):x8}");
            if (p.Sockets.Length > 0)
                Console.WriteLine("sockets: " + string.Join(", ", p.Sockets.Select(s => $"{s.Name} ({s.Type}{(s.Required ? ", required" : "")})")));
            if (p.Mount != "")
            {
                var fits = all.SelectMany(q => q.Sockets.Where(s => s.Type == p.Mount && (s.Accepts.Length == 0 || s.Accepts.Contains(p.Category))).Select(s => $"{q.Name}.{s.Name}")).ToList();
                Console.WriteLine($"fits {fits.Count} socket(s){(fits.Count > 0 ? ": " + string.Join(", ", fits.Take(12)) + (fits.Count > 12 ? ", ..." : "") : "")}");
            }
            var problems = Parts.Lint(all, lib.Registry).Where(x => x.StartsWith(p.Name + " ", StringComparison.Ordinal)).ToList();
            foreach (var x in problems)
                Console.WriteLine("PROBLEM: " + x);
            if (!inside && Parts.Find(p.Name) is { SourceFile: { } other })
                Console.WriteLine($"note: '{p.Name}' is already in the Parts folder ({other}); names must be unique, so only one of them would load");
            return problems.Count == 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static string? FragmentName(string text)
    {
        try
        {
            using var doc = WeaponFile.Parse(text, "");
            return PartLibrary.Get(doc.RootElement, "name")?.GetString();
        }
        catch (Exception ex) when (ex is WeaponFileException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Part files whose text uses fragment <paramref name="name"/> (directly).</summary>
    private static List<string> UsersOf(PartLibrary lib, string name) =>
        lib.PartTexts.Where(t => t.Text.Contains($"\"{name}\"", StringComparison.OrdinalIgnoreCase)).Select(t => t.Source).ToList();

    /// <summary>"CallOfSlop.exe --check-parts [DIR]": every part, fragment, base and template, plus the cross-part checks.</summary>
    public static int CheckAll(string? dir)
    {
        var folder = dir ?? Parts.Folder;
        var errors = new List<string>();
        var (parts, lib, _) = Parts.LoadDetailed(folder, errors);
        Console.WriteLine($"{parts.Length} part(s) in {folder}: " + string.Join(", ", Enum.GetValues<PartCategory>().Select(c => $"{PartDef.CategoryName(c)} {parts.Count(p => p.Category == c)}")));
        Console.WriteLine($"{lib.Fragments.Count} fragment(s), {lib.Bases.Count} base(s), registry {lib.Registry.Source ?? "built-in"}: {lib.Registry.SocketTypes.Length} socket types, {lib.Registry.Bores.Length} bores");
        // Fragments nobody uses still have to expand.
        foreach (var f in lib.Fragments.Values)
            try
            {
                using var use = System.Text.Json.JsonDocument.Parse($"{{\"use\": {WeaponFile.Str(f.Name)}}}");
                _ = lib.Use(use.RootElement, f.Source, 0).ToArray();
            }
            catch (WeaponFileException ex)
            {
                errors.Add(ex.Message);
            }
        foreach (var (rel, text) in TemplateFiles(folder))
            try
            {
                PartFile.Load(text, rel, lib);
            }
            catch (WeaponFileException ex)
            {
                errors.Add(ex.Message);
            }
        var notes = new List<string>();
        var problems = Parts.Lint(parts, lib.Registry, notes);
        foreach (var e in errors)
            Console.WriteLine("ERROR: " + e);
        foreach (var p in problems)
            Console.WriteLine("PROBLEM: " + p);
        foreach (var n in notes)
            Console.WriteLine("note: " + n);
        Console.WriteLine($"part set fingerprint {WeaponSet.Fnv(parts.Select(PartDigest.Of)):x8}");
        Console.WriteLine(errors.Count + problems.Count == 0 ? "OK" : $"{errors.Count} error(s), {problems.Count} problem(s)");
        return errors.Count + problems.Count == 0 ? 0 : 1;
    }

    /// <summary>The files in Parts/_Template (commented examples; loaded as parts by the checks, never by the game).</summary>
    public static List<(string Source, string Text)> TemplateFiles(string folder)
    {
        var dir = Path.Combine(folder, PartLibrary.TemplateFolder);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase).Select(f => ($"{PartLibrary.TemplateFolder}/{Path.GetFileName(f)}", File.ReadAllText(f))).ToList()
            : [];
    }

    /// <summary>
    /// "CallOfSlop.exe --export-parts DIR [--resolved]": writes every part file (and fragment and base) normalised (one
    /// key per line, boxes and sockets one per line, comments dropped) into DIR, for tidy diffs. With --resolved, each part
    /// is written out in full instead: "extends" merged and fragments expanded into plain boxes.
    /// </summary>
    public static int Export(string dir, bool resolved)
    {
        var errors = new List<string>();
        var lib = PartLibrary.FromFolder(Parts.Folder, errors);
        var n = 0;
        void Write(string rel, string text)
        {
            var path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            n++;
        }
        foreach (var (rel, text) in lib.PartTexts)
            try
            {
                Write(rel, resolved ? PartJson.Resolved(text, rel, lib) : PartJson.Normalise(text, rel));
            }
            catch (WeaponFileException ex)
            {
                errors.Add(ex.Message);
            }
        if (!resolved)
            foreach (var (rel, text) in lib.Fragments.Values.Select(f => (f.Source, File.ReadAllText(Path.Combine(Parts.Folder, f.Source))))
                         .Concat(lib.Bases.Values))
                try
                {
                    Write(rel, PartJson.Normalise(text, rel));
                }
                catch (WeaponFileException ex)
                {
                    errors.Add(ex.Message);
                }
        foreach (var e in errors)
            Console.WriteLine("ERROR: " + e);
        Console.WriteLine($"wrote {n} file(s) to {dir}{(resolved ? " (resolved: extends merged, fragments expanded)" : " (normalised)")}");
        return errors.Count == 0 ? 0 : 1;
    }
}

/// <summary>Part file text helpers: the normalised layout (--export-parts) and the fully resolved form.</summary>
public static class PartJson
{
    /// <summary>The order keys are written in; anything else follows in the file's own order.</summary>
    public static readonly string[] KeyOrder =
    [
        "format", "extends", "name", "category", "rarity", "flavor", "author", "about", "mount", "bores", "sizedFor", "requiresAction", "weight", "scrap", "carry", "flags", "hand",
        "gripAngle", "muzzles", "optic", "sight", "feed", "receiver", "frame", "split", "shared", "hilt", "melee", "sockets", "model", "stats",
    ];

    /// <summary>
    /// A part (or fragment, base) file in the shared layout: top-level keys one per line in <see cref="KeyOrder"/>, lists
    /// of objects (sockets, model, stats) one entry per line, everything else on one line; numbers and strings exactly as
    /// written. Comments and trailing commas are dropped. Normalising twice changes nothing.
    /// </summary>
    public static string Normalise(string text, string source)
    {
        using var doc = WeaponFile.Parse(text, source);
        var root = doc.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new WeaponFileException($"{source}: expected one JSON object");
        var props = root.EnumerateObject().ToList();
        int Rank(string k)
        {
            var bare = k.TrimStart('+', '-');
            var i = Array.FindIndex(KeyOrder, x => x.Equals(bare, StringComparison.OrdinalIgnoreCase));
            return (i < 0 ? KeyOrder.Length : i) * 3 + (k.StartsWith('-') ? 1 : k.StartsWith('+') ? 2 : 0);
        }
        var sb = new StringBuilder("{\n");
        var ordered = props.Select((p, i) => (p, i)).OrderBy(x => Rank(x.p.Name)).ThenBy(x => x.i).Select(x => x.p).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var p = ordered[i];
            sb.Append("  ").Append(WeaponFile.Str(p.Name)).Append(": ");
            var v = p.Value;
            if (v.ValueKind == System.Text.Json.JsonValueKind.Array && v.GetArrayLength() > 0 && v.EnumerateArray().All(x => x.ValueKind == System.Text.Json.JsonValueKind.Object))
            {
                sb.Append("[\n");
                var k = 0;
                foreach (var x in v.EnumerateArray())
                {
                    sb.Append("    ");
                    Inline(sb, x);
                    sb.Append(++k < v.GetArrayLength() ? ",\n" : "\n");
                }
                sb.Append("  ]");
            }
            else
                Inline(sb, v);
            sb.Append(i + 1 < ordered.Count ? ",\n" : "\n");
        }
        return sb.Append("}\n").ToString();
    }

    private static void Inline(StringBuilder sb, System.Text.Json.JsonElement e)
    {
        switch (e.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
            {
                sb.Append('{');
                var first = true;
                foreach (var p in e.EnumerateObject())
                {
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    sb.Append(WeaponFile.Str(p.Name)).Append(": ");
                    Inline(sb, p.Value);
                }
                sb.Append('}');
                break;
            }
            case System.Text.Json.JsonValueKind.Array:
            {
                sb.Append('[');
                var first = true;
                foreach (var x in e.EnumerateArray())
                {
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    Inline(sb, x);
                }
                sb.Append(']');
                break;
            }
            default:
                sb.Append(e.GetRawText());
                break;
        }
    }

    /// <summary>A part file written out in full: "extends" merged in and every fragment use expanded into plain boxes.</summary>
    public static string Resolved(string text, string source, PartLibrary lib)
    {
        var part = PartFile.Load(text, source, lib); // checks it, and gives the expanded boxes
        using var raw = WeaponFile.Parse(text, source);
        var merged = PartLibrary.Get(raw.RootElement, "extends") != null ? lib.ResolveExtends(raw.RootElement, source) : raw.RootElement.GetRawText();
        var obj = System.Text.Json.Nodes.JsonNode.Parse(merged, null, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        if (obj.Any(kv => kv.Key.Equals("model", StringComparison.OrdinalIgnoreCase)))
        {
            var key = obj.First(kv => kv.Key.Equals("model", StringComparison.OrdinalIgnoreCase)).Key;
            var model = new System.Text.Json.Nodes.JsonArray();
            foreach (var b in part.Boxes)
                model.Add(System.Text.Json.Nodes.JsonNode.Parse(BoxJson(b)));
            obj[key] = model;
        }
        obj.Remove("extends");
        return Normalise(obj.ToJsonString(), source);
    }

    /// <summary>One model box as JSON (defaults left out; a fragment box's turn as Euler angles).</summary>
    public static string BoxJson(PartBox b)
    {
        var sb = new StringBuilder("{");
        if (b.Shape != "box")
            sb.Append("\"shape\": ").Append(WeaponFile.Str(b.Shape)).Append(", ");
        sb.Append("\"size\": ").Append(V(b.Size));
        if (b.Pos != Vector3.Zero)
            sb.Append(", \"pos\": ").Append(V(b.Pos));
        var rot = b.Orient is { } q ? EulerDeg(q) : b.RotDeg;
        if (rot.Length() > 1e-4f)
            sb.Append(", \"rot\": ").Append(V(rot));
        sb.Append(", \"color\": ").Append(b.Color.Name is { } name ? WeaponFile.Str(name) : V(b.Color.Rgb));
        if (b.Alpha < 0.9999f)
            sb.Append(", \"alpha\": ").Append(N(b.Alpha));
        if (b.Tag != "")
            sb.Append(", \"tag\": ").Append(WeaponFile.Str(b.Tag));
        return sb.Append('}').ToString();
    }

    private static string N(float v) => Math.Round(v, 5).ToString(CultureInfo.InvariantCulture);

    private static string V(Vector3 v) => $"[{N(v.X)}, {N(v.Y)}, {N(v.Z)}]";

    /// <summary>
    /// Euler angles (degrees, X Y Z, applied Z then X then Y as in <see cref="PartDef.Rot"/>) of a rotation:
    /// <c>PartDef.Rot(EulerDeg(q))</c> turns things the same way as <paramref name="q"/>.
    /// </summary>
    public static Vector3 EulerDeg(Quaternion q)
    {
        // PartDef.Rot is Rz * Rx * Ry (column vectors). Its matrix: M[2][1] = sin x, M[2][0] = -cos x sin y,
        // M[2][2] = cos x cos y, M[0][1] = -sin z cos x, M[1][1] = cos z cos x. Column j = q applied to axis j.
        var ex = Vector3.Transform(Vector3.UnitX, q);
        var ey = Vector3.Transform(Vector3.UnitY, q);
        var ez = Vector3.Transform(Vector3.UnitZ, q);
        var sx = Math.Clamp(ey.Z, -1f, 1f);
        var x = MathF.Asin(sx);
        float y, z;
        if (MathF.Abs(MathF.Cos(x)) > 1e-5f)
        {
            y = MathF.Atan2(-ex.Z, ez.Z);
            z = MathF.Atan2(-ey.X, ey.Y);
        }
        else
        {
            y = 0; // gimbal lock: put the whole turn about Z
            z = MathF.Atan2(ex.Y, ex.X);
        }
        const float k = 180f / MathF.PI;
        return new Vector3(x * k, y * k, z * k);
    }
}
