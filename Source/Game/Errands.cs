using System.Text.Json;

namespace CallOfSlop;

/// <summary>What completes an errand's step (Story/Errands/*.json). Judged from how things stand, not from one action, so a
/// machine someone already powered passes its power step at once.</summary>
public enum ErrandCue : byte
{
    Wait, // the step's seconds pass
    Reach, // within amount m of the spot
    Power, // the machine has power (a cable from the genset, or any link that powers it)
    Feed, // every material the step gave has gone from the pack (into the machine, or its bin)
    Recipe, // the machine (an Assembler) is set to the step's "recipe" (any, if not said)
    Carry, // they carry more of the step's "material" than when it began
    Part, // they carry more of the step's "part" than when it began (in the pack or in their hands)
}

/// <summary>A place on the errand's machine a step points at.</summary>
public enum ErrandSpot : byte
{
    None,
    Machine, // the machine itself
    Genset, // the genset beside it
    In, // the first plain bin (the one that feeds it)
    Out, // the last plain bin (the one it fills)
}

public sealed class ErrandStep
{
    public string Id = "", Title = "", Text = "", Say = "", Hint = "";
    public ErrandCue Cue;
    public ErrandSpot Spot;
    public float Seconds = 6, Amount = 6, HintAfter = 40;
    public (string Material, int Units)[] Give = []; // handed over as the step starts, topped up to the units
    public string Material = "", Part = "", Recipe = "";
}

/// <summary>
/// One errand (docs/story/DUTIES.md "Errands"; format call-of-slop-errand/1): a small optional job someone at the colony asks
/// for, which teaches one of the works yard's machines hands-on. A storylet asks and its end starts it ("hook":
/// "errand:ID"); the steps are judged on the host (GameErrands.cs). Every file in Story/Errands is one errand; a bad file is
/// refused with the reason and the others still load.
/// </summary>
public sealed class ErrandScript
{
    public const string Format = "call-of-slop-errand/1", FolderName = "Errands";
    public const int MaxSteps = 12, MaxErrands = 64, MaxGive = 3;

    public string Id = "", Title = "", Giver = "", Machine = "", Fact = "", Thanks = "", Gone = "", File = "";
    public float Reach = 300; // m from the colony's middle that the machine is looked for
    public ErrandStep[] Steps = [];

    private static ErrandScript[]? _all;
    private static readonly List<string> _problems = new();

    /// <summary>Every errand, in file-name order (an errand's index is its number on the wire, so every peer has the same).</summary>
    public static ErrandScript[] All => _all ??= LoadFolder(Path.Combine(AppFolders.Data, "Story", FolderName), _problems);

    public static int IndexOf(string id)
    {
        var all = All;
        for (var k = 0; k < all.Length; k++)
            if (all[k].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return k;
        return -1;
    }

    /// <summary>The errands in a folder; each refused file adds its reason to <paramref name="errors"/> and is logged.</summary>
    public static ErrandScript[] LoadFolder(string dir, List<string> errors)
    {
        var list = new List<ErrandScript>();
        if (!Directory.Exists(dir))
            return [];
        foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = $"{FolderName}/{Path.GetFileName(path)}";
            try
            {
                var e = Parse(System.IO.File.ReadAllText(path), name);
                if (list.Any(o => o.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException($"the id \"{e.Id}\" is another errand's too");
                list.Add(e);
                if (list.Count > MaxErrands)
                    throw new FormatException($"more than {MaxErrands} errands");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
            {
                errors.Add($"{name}: {ex.Message}");
                Console.WriteLine($"REFUSED {name}: {ex.Message}");
            }
        }
        if (list.Count > 0)
            Console.WriteLine($"[errand] {list.Count} errands ({string.Join(", ", list.Select(e => e.Id))})");
        return [.. list];
    }

    public static ErrandScript Parse(string json, string file)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        static string Str(JsonElement e, string key, string where, bool need = false)
        {
            if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString() ?? "";
                foreach (var c in s)
                    if (c is < ' ' or > '~')
                        throw new FormatException($"{where}.{key}: player text is ASCII only");
                return s;
            }
            return need ? throw new FormatException($"{where} has no \"{key}\"") : "";
        }
        var e = new ErrandScript
        {
            File = file,
            Id = Str(root, "id", "file", true),
            Title = Str(root, "title", "file", true),
            Giver = Str(root, "giver", "file", true),
            Machine = Str(root, "machine", "file", true),
            Thanks = Str(root, "thanks", "file"),
            Gone = Str(root, "gone", "file"),
        };
        if (e.Id.Length == 0 || e.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new FormatException("\"id\": letters, digits, - and _");
        e.Fact = Str(root, "fact", "file") is { Length: > 0 } fact ? fact : e.Machine;
        if (root.TryGetProperty("reach", out var re) && re.ValueKind == JsonValueKind.Number)
            e.Reach = re.GetSingle();
        if (e.Reach is < 10 or > 3000)
            throw new FormatException("\"reach\": 10 to 3000 m");
        if (!root.TryGetProperty("steps", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("no \"steps\" list");
        var steps = new List<ErrandStep>();
        foreach (var x in list.EnumerateArray())
        {
            var where = $"steps[{steps.Count}]";
            var st = new ErrandStep { Id = Str(x, "id", where, true), Title = Str(x, "title", where, true), Text = Str(x, "text", where, true), Say = Str(x, "say", where), Hint = Str(x, "hint", where) };
            where = $"{where} ({st.Id})";
            var cue = Str(x, "cue", where, true);
            if (!Enum.TryParse(cue, true, out st.Cue) || int.TryParse(cue, out _))
                throw new FormatException($"{where}: no such cue \"{cue}\" (wait, reach, power, feed, recipe, carry, part)");
            var spot = Str(x, "spot", where);
            st.Spot = spot.Length == 0 ? ErrandSpot.None
                : Enum.TryParse<ErrandSpot>(spot, true, out var sk) && !int.TryParse(spot, out _) && sk != ErrandSpot.None ? sk
                : throw new FormatException($"{where}: no such spot \"{spot}\" (machine, genset, in, out)");
            st.Material = Str(x, "material", where);
            st.Part = Str(x, "part", where);
            st.Recipe = Str(x, "recipe", where);
            if (x.TryGetProperty("give", out var gv))
            {
                if (gv.ValueKind != JsonValueKind.Object)
                    throw new FormatException($"{where}: \"give\" is {{\"material\": units, ...}}");
                var give = new List<(string, int)>();
                foreach (var g in gv.EnumerateObject())
                {
                    if (g.Value.ValueKind != JsonValueKind.Number || g.Value.GetInt32() is < 1 or > 200)
                        throw new FormatException($"{where}: \"give\": {g.Name} takes 1 to 200 units");
                    if (RecyclingTable.Current.IdOf(g.Name) == 0)
                        throw new FormatException($"{where}: \"give\": no material \"{g.Name}\" (Constructs/Recycling.json)");
                    give.Add((g.Name, g.Value.GetInt32()));
                }
                if (give.Count is 0 or > MaxGive)
                    throw new FormatException($"{where}: \"give\": 1 to {MaxGive} materials");
                st.Give = [.. give];
            }
            if (st.Material.Length > 0 && RecyclingTable.Current.IdOf(st.Material) == 0)
                throw new FormatException($"{where}: no material \"{st.Material}\" (Constructs/Recycling.json)");
            if (st.Cue == ErrandCue.Carry && st.Material.Length == 0)
                throw new FormatException($"{where}: a carry needs a \"material\"");
            if (st.Cue == ErrandCue.Part && st.Part.Length == 0)
                throw new FormatException($"{where}: a part step needs a \"part\"");
            if (st.Cue == ErrandCue.Feed && st.Give.Length == 0)
                throw new FormatException($"{where}: a feed needs a \"give\" (what is tipped in)");
            if (st.Cue == ErrandCue.Reach && st.Spot == ErrandSpot.None)
                throw new FormatException($"{where}: a reach needs a spot");
            if (x.TryGetProperty("seconds", out var sec) && sec.ValueKind == JsonValueKind.Number)
                st.Seconds = sec.GetSingle();
            if (x.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number)
                st.Amount = am.GetSingle();
            if (x.TryGetProperty("hintAfter", out var ha) && ha.ValueKind == JsonValueKind.Number)
                st.HintAfter = ha.GetSingle();
            if (st.Seconds is not (> 0 and <= 120) || st.Amount is not (> 0 and <= 1000) || st.HintAfter is not (>= 0 and <= 600))
                throw new FormatException($"{where}: seconds 0 to 120, amount 0 to 1000, hintAfter 0 to 600");
            steps.Add(st);
            if (steps.Count > MaxSteps)
                throw new FormatException($"more than {MaxSteps} steps");
        }
        if (steps.Count == 0)
            throw new FormatException("no steps");
        e.Steps = [.. steps];
        return e;
    }
}
