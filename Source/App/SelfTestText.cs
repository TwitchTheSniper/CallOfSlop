using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Brutal.ImGuiApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Player-visible text: every weapon, attachment, perk, streak, throwable, mode, map and pack text (plus key UI
    /// labels) must be plain printable ASCII (the game uses ImGui's default font, which draws anything else as '?'),
    /// free of mojibake, literal escape sequences, placeholders and stray spaces, and descriptions must be whole
    /// sentences. Attachment pros/cons must mention the percentages their effects really apply. '%' must reach
    /// printf-style ImGui calls escaped (<see cref="ImGuiSafe"/>), and a source scan makes sure every such call
    /// with non-constant text goes through it.
    /// </summary>
    private static void CheckText()
    {
        var texts = new List<(string Where, string Text, TextKind Kind)>();
        void Add(string where, string? text, TextKind kind) => texts.Add((where, text ?? "", kind));

        foreach (var w in Weapons.All)
        {
            Add($"weapon '{w.Name}' name", w.Name, TextKind.Label);
            Add($"weapon '{w.Name}' blurb", w.Blurb, TextKind.Sentence);
            Add($"weapon '{w.Name}' class", w.ClassName, TextKind.Label);
            Add($"weapon '{w.Name}' mode", w.ModeName, TextKind.Label);
        }
        var attachments = Attachments.BySlot.SelectMany(s => s).ToArray();
        foreach (var a in attachments)
        {
            Add($"attachment '{a.Name}' name", a.Name, TextKind.Label);
            Add($"attachment '{a.Name}' blurb", a.Blurb, TextKind.Sentence);
            foreach (var p in a.Pros)
                Add($"attachment '{a.Name}' pro", p, TextKind.ListItem);
            foreach (var c in a.Cons)
                Add($"attachment '{a.Name}' con", c, TextKind.ListItem);
        }
        for (var s = 0; s < Attachments.SlotCount; s++)
            Add($"attachment slot {s}", Attachments.SlotName((AttachSlot)s), TextKind.Label);
        foreach (var p in Perks.All)
        {
            Add($"perk '{p.Name}' name", p.Name, TextKind.Label);
            if (p.Id != PerkId.None)
                Add($"perk '{p.Name}' when", p.When, TextKind.Label);
            Add($"perk '{p.Name}' effect", p.Effect, TextKind.Sentence);
        }
        foreach (PerkCategory c in Enum.GetValues<PerkCategory>())
            Add($"perk category {c}", Perks.CategoryName(c), TextKind.Label);
        foreach (var k in KillStreaks.All)
        {
            Add($"streak '{k.Name}' name", k.Name, TextKind.Label);
            if (k.Id != StreakId.None)
                Add($"streak '{k.Name}' short", k.Short, TextKind.Label);
            Add($"streak '{k.Name}' effect", k.Effect, TextKind.Sentence);
        }
        foreach (var t in Throwables.All)
        {
            Add($"throwable '{t.Name}' name", t.Name, TextKind.Label);
            Add($"throwable '{t.Name}' blurb", t.Blurb, TextKind.Sentence);
        }
        foreach (var m in GameModes.All)
        {
            Add($"mode {m} name", GameModes.Name(m), TextKind.Label);
            Add($"mode {m} short", GameModes.Short(m), TextKind.Label);
            Add($"mode {m} rules", GameModes.Rules(m), TextKind.Sentence);
        }
        foreach (var n in GameModes.TeamNames)
            Add("team name", n, TextKind.Label);
        foreach (var m in Maps.All)
        {
            Add($"map '{m.Name}' name", m.Name, TextKind.Label);
            Add($"map '{m.Name}' description", m.Description, TextKind.Sentence);
            if (m.Author.Length > 0)
                Add($"map '{m.Name}' author", m.Author, TextKind.Label);
        }
        foreach (var p in Maps.Packs)
        {
            Add($"pack '{p.Name}' name", p.Name, TextKind.Label);
            if (p.Description.Length > 0)
                Add($"pack '{p.Name}' description", p.Description, TextKind.Sentence);
            if (p.Author.Length > 0)
                Add($"pack '{p.Name}' author", p.Author, TextKind.Label);
        }
        foreach (GameAction a in Enum.GetValues<GameAction>())
            Add($"key binding {a}", Keybinds.Label(a), TextKind.Label);
        Add("default loadout name", Loadout.DefaultName(0), TextKind.Label);

        var bad = 0;
        foreach (var (where, text, kind) in texts)
            foreach (var problem in TextProblems(text, kind))
            {
                Fail($"{where}: {problem} in \"{Printable(text)}\"");
                bad++;
            }
        Line($"{texts.Count} texts checked (weapons {Weapons.All.Length}, attachments {attachments.Length}, perks {Perks.All.Length}, streaks {KillStreaks.All.Length}, "
             + $"throwables {Throwables.All.Length}, modes {GameModes.All.Length}, maps {Maps.All.Length}, packs {Maps.Packs.Length}): {bad} problem(s)");

        CheckAttachmentNumbers(attachments);
        CheckFormatEscaping();
        CheckSourceTextCalls();
    }

    private enum TextKind
    {
        Label, // names and short labels
        Sentence, // blurbs, effects, descriptions: whole sentences
        ListItem, // attachment pros/cons: short phrases, no full stop
    }

    private static readonly Regex EscapeJunk = new(@"\\[nrtu""\\]|\\x[0-9A-Fa-f]{2}|&#\d+;|&(amp|lt|gt|quot|apos|nbsp|mdash|ndash|hellip);", RegexOptions.Compiled);
    private static readonly string[] Mojibake = ["Ã", "Â", "â€", "ï»¿", "�"];
    private static readonly string[] Placeholders = ["TODO", "TBD", "FIXME", "XXX", "lorem", "placeholder"];

    /// <summary>Everything wrong with one player-visible text (empty when it's fine).</summary>
    private static IEnumerable<string> TextProblems(string text, TextKind kind)
    {
        foreach (var c in text.Distinct())
            if (c < 32 || c > 126)
                yield return $"character U+{(int)c:X4} the font can't draw (shows as '?'; use plain ASCII)";
        foreach (var m in Mojibake)
            if (text.Contains(m))
                yield return "mojibake (text decoded with the wrong encoding)";
        if (EscapeJunk.Match(text) is { Success: true } e)
            yield return $"literal escape sequence '{e.Value}'";
        foreach (var p in Placeholders)
            if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
                yield return $"placeholder text '{p}'";
        if (text.Contains('{') || text.Contains('}'))
            yield return "unfilled {placeholder}";
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
            yield return "leading or trailing space";
        if (text.Contains("  "))
            yield return "doubled space";
        if (kind == TextKind.Sentence && text.Length > 0 && text[^1] is not ('.' or '!' or '?'))
            yield return "doesn't end like a sentence (cut off?)";
        if (kind == TextKind.ListItem && text.Length == 0)
            yield return "empty pro/con";
        if (kind == TextKind.ListItem && text.EndsWith('.'))
            yield return "pros/cons are phrases: no full stop";
        if (kind != TextKind.Sentence && text.Length == 0)
            yield return "empty";
    }

    private static string Printable(string s) =>
        new(s.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());

    /// <summary>
    /// The numbers in each attachment's pros/cons must match its effects: a "mul" of 0.85 on range needs a "15%"
    /// somewhere, a "grow" of 2.4 needs "2.4x", a mobility "add" of -0.04 needs "4% speed" and a zoom "set" needs "1.5x".
    /// </summary>
    private static void CheckAttachmentNumbers(AttachmentDef[] attachments)
    {
        string[] stats = ["rangeM", "hipSpreadDeg", "adsSpreadMult", "recoilDeg", "vertical", "horizontal", "shake", "punch", "firstShot",
            "recovery", "kickUp", "damage", "mag", "reload", "adsTime", "equipTime", "mobility", "adsZoom"];
        int checkedCount = 0, bad = 0;
        foreach (var a in attachments)
        {
            var said = string.Join(" | ", a.Pros.Concat(a.Cons));
            foreach (var e in a.Effects)
            {
                var key = WeaponFile.Key(e.Field);
                var stat = stats.FirstOrDefault(s => s.Equals(key, StringComparison.OrdinalIgnoreCase)
                                                     || ("recoil" + s).Equals(key, StringComparison.OrdinalIgnoreCase));
                if (stat == null || e.Value is not float v)
                    continue;
                string? expect = e.Op switch
                {
                    "mul" or "grow" => v >= 2 ? Num(v) + "x" : Num(MathF.Round(MathF.Abs(v - 1) * 100)) + "%",
                    "add" when stat == "mobility" => Num(MathF.Round(MathF.Abs(v) * 100)) + "% speed",
                    "set" when stat == "adsZoom" => Num(v) + "x",
                    _ => null,
                };
                if (expect == null)
                    continue;
                checkedCount++;
                if (!said.Contains(expect))
                {
                    Fail($"attachment '{a.Name}': its {key} {e.Op} {v:0.###} should show as \"{expect}\" in its pros/cons, which say: {said}");
                    bad++;
                }
            }
        }
        Line($"attachment pros/cons vs effects: {checkedCount} numbers checked, {bad} mismatch(es)");
    }

    private static string Num(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// ImGui.TextColored/TextWrapped/... run their text through printf, so '%' must arrive doubled
    /// and the escaped copy must still be null-terminated for the native side.
    /// </summary>
    private static void CheckFormatEscaping()
    {
        (string In, string Out)[] cases =
        [
            ("+45% range", "+45%% range"),
            ("Move 80% faster while crouched", "Move 80%% faster while crouched"),
            ("RELOADING 50%", "RELOADING 50%%"),
            ("%s%s%n", "%%s%%s%%n"), // a hostile player name
            ("no percent here", "no percent here"),
        ];
        foreach (var (input, expected) in cases)
        {
            var got = EscapedBytes(input, out var terminated);
            if (got != expected || !terminated)
                Fail($"ImGuiSafe.Escape(\"{input}\") gave \"{got}\" (null-terminated: {terminated}), expected \"{expected}\"");
            if (ImGuiSafe.EscapeString(input) != expected)
                Fail($"ImGuiSafe.EscapeString(\"{input}\") gave \"{ImGuiSafe.EscapeString(input)}\"");
        }
        Line($"'%' escaping for printf-style ImGui calls: {cases.Length} cases checked");
    }

    private static string EscapedBytes(string input, out bool terminated)
    {
        ImString escaped = ImGuiSafe.Escape(input);
        ReadOnlySpan<byte> bytes = escaped.AsSpan();
        terminated = Unsafe.Add(ref MemoryMarshal.GetReference(bytes), bytes.Length) == 0;
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly Regex FormatCall = new(@"\bImGui\.(TextColored|TextWrapped|TextDisabled|BulletText|SetTooltip|SetItemTooltip)\(", RegexOptions.Compiled);

    /// <summary>A text argument that is only constant u8 literals without '%' (optionally "cond ? "a"u8 : "b"u8").</summary>
    private static readonly Regex SafeTextArg = new(@"^\s*(?:[^""?]*\?\s*)?""[^""%\\]*""u8(?:\s*:\s*""[^""%\\]*""u8)?\s*$", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Scans the game's own source (when it's next to the build, as in CI) for printf-style ImGui text calls whose
    /// text could contain '%': those must use <see cref="ImGuiSafe"/> instead.
    /// </summary>
    private static void CheckSourceTextCalls()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Source", "UI")))
            dir = dir.Parent;
        if (dir == null)
        {
            Line("source scan skipped (no Source folder above the build)");
            return;
        }
        var root = Path.Combine(dir.FullName, "Source");
        int files = 0, calls = 0;
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == "ImGuiSafe.cs")
                continue;
            files++;
            var src = File.ReadAllText(file);
            foreach (Match m in FormatCall.Matches(src))
            {
                calls++;
                var open = m.Index + m.Length - 1;
                var close = MatchingParen(src, open);
                if (close < 0)
                    continue;
                var inner = src[(open + 1)..close];
                var text = m.Groups[1].Value == "TextColored" ? AfterFirstTopLevelComma(inner) : inner;
                if (text == null || !SafeTextArg.IsMatch(text))
                {
                    var line = src.AsSpan(0, m.Index).Count('\n') + 1;
                    Fail($"{Path.GetRelativePath(dir.FullName, file)}:{line}: ImGui.{m.Groups[1].Value} with text that can contain '%' (it's a printf format): use ImGuiSafe.{m.Groups[1].Value}");
                }
            }
        }
        Line($"source scan: {files} files, {calls} printf-style ImGui text calls with constant text");
    }

    /// <summary>Index of the ')' closing the '(' at <paramref name="open"/>, skipping string literals; -1 if none.</summary>
    private static int MatchingParen(string s, int open)
    {
        var depth = 0;
        var inString = false;
        for (var i = open; i < s.Length; i++)
        {
            var c = s[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
                inString = true;
            else if (c == '(')
                depth++;
            else if (c == ')' && --depth == 0)
                return i;
        }
        return -1;
    }

    private static string? AfterFirstTopLevelComma(string args)
    {
        var depth = 0;
        var inString = false;
        for (var i = 0; i < args.Length; i++)
        {
            var c = args[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
                inString = true;
            else if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']' or '}')
                depth--;
            else if (c == ',' && depth == 0)
                return args[(i + 1)..];
        }
        return null;
    }
}
