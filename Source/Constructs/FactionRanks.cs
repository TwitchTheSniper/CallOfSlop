using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a rank lets a faction member do (docs/sandbox/CONSTRUCTS.md, "Factions", "Ranks").</summary>
[Flags]
public enum FactionMay : ushort
{
    None = 0,
    Dismantle = 1, // take apart (remover, salvage) what a mate owns; using, driving and building on it needs no rank
    Give = 2, // give a construct you own to a mate
    Invite = 4, // invite people, and answer those who ask to join
    Remove = 8, // take out a member of a lower rank
    Promote = 16, // move a member of a lower rank up or down, to below one's own
    Relations = 32, // declare the faction's stance (allied, neutral, at war) toward another faction
    Looks = 64, // the faction's colour and tag, and whether anyone may join
    All = 0x7F,
}

/// <summary>One rank: its name and what it may do. A set's last rank is its leader's, who may do everything.</summary>
public sealed record FactionRank(string Name, FactionMay May);

/// <summary>
/// The ranks of a kind of faction (Constructs/FactionRanks.json; docs/sandbox/CONSTRUCTS.md, "Factions", "Ranks"): lowest
/// first, the last one the leader's. A set is generic data, so a story faction's ladder (later the Ministry's colony:
/// inmate, trusty, guard...) is one more set. Player factions use the set named <see cref="PlayerSet"/>.
/// </summary>
public sealed class FactionRankSet
{
    public const string Format = "call-of-slop-faction-ranks/1";
    public const string PlayerSet = "Player";
    public const int MaxRanks = 8;

    public string Name = "";
    public FactionRank[] Ranks = [];

    /// <summary>The rank a new member gets (an index into <see cref="Ranks"/>).</summary>
    public byte Join;

    public int Top => Ranks.Length - 1;

    public string RankName(int r) => (uint)r < (uint)Ranks.Length ? Ranks[r].Name : "?";

    public FactionMay May(int r) => r >= Top ? FactionMay.All : (uint)r < (uint)Ranks.Length ? Ranks[r].May : FactionMay.None;

    /// <summary>The built-in ladder, used when the file is refused.</summary>
    public static FactionRankSet Default() => new()
    {
        Name = PlayerSet,
        Ranks =
        [
            new("Recruit", FactionMay.None),
            new("Member", FactionMay.Dismantle | FactionMay.Give),
            new("Officer", FactionMay.Dismantle | FactionMay.Give | FactionMay.Invite | FactionMay.Remove | FactionMay.Promote),
            new("Leader", FactionMay.All),
        ],
        Join = 0,
    };
}

/// <summary>The rank sets loaded from Constructs/FactionRanks.json (once); a bad file is refused with a reason and the built-in ladder is used.</summary>
public static class FactionRanks
{
    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "FactionRanks.json");

    public static string Problem = "";

    private static Dictionary<string, FactionRankSet>? _sets;

    /// <summary>The player factions' ladder.</summary>
    public static FactionRankSet Player => Set(FactionRankSet.PlayerSet) ?? (_player ??= FactionRankSet.Default());
    private static FactionRankSet? _player;

    public static FactionRankSet? Set(string name)
    {
        _sets ??= Load(FilePath, out Problem);
        return _sets.TryGetValue(name, out var s) ? s : null;
    }

    public static Dictionary<string, FactionRankSet> Load(string path, out string problem)
    {
        var sets = new Dictionary<string, FactionRankSet>(StringComparer.OrdinalIgnoreCase);
        problem = "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != FactionRankSet.Format)
                throw new FormatException($"\"format\" must be \"{FactionRankSet.Format}\"");
            foreach (var e in root.GetProperty("sets").EnumerateArray())
            {
                var set = new FactionRankSet { Name = e.GetProperty("name").GetString() ?? "" };
                if (set.Name.Length == 0 || sets.ContainsKey(set.Name))
                    throw new FormatException($"a set needs a name of its own (\"{set.Name}\")");
                var ranks = new List<FactionRank>();
                foreach (var r in e.GetProperty("ranks").EnumerateArray())
                {
                    var name = r.GetProperty("name").GetString() ?? "";
                    if (name.Length is 0 or > 16 || name.Any(ch => ch is < ' ' or > '~') || ranks.Any(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        throw new FormatException($"{set.Name}: a rank needs a name of its own, 1 to 16 ASCII characters (\"{name}\")");
                    var may = FactionMay.None;
                    if (r.TryGetProperty("may", out var m))
                        foreach (var x in m.EnumerateArray())
                            may |= Enum.TryParse<FactionMay>(x.GetString(), true, out var bit) && bit != FactionMay.None ? bit : throw new FormatException($"{set.Name} {name}: no such permission \"{x.GetString()}\"");
                    ranks.Add(new FactionRank(name, may));
                }
                if (ranks.Count is < 2 or > FactionRankSet.MaxRanks)
                    throw new FormatException($"{set.Name}: 2 to {FactionRankSet.MaxRanks} ranks, the last the leader's");
                set.Ranks = ranks.ToArray();
                var join = e.TryGetProperty("join", out var j) ? j.GetString() ?? "" : ranks[0].Name;
                var ji = ranks.FindIndex(o => o.Name.Equals(join, StringComparison.OrdinalIgnoreCase));
                if (ji < 0 || ji == ranks.Count - 1)
                    throw new FormatException($"{set.Name}: \"join\" must name a rank below the leader's (\"{join}\")");
                set.Join = (byte)ji;
                sets[set.Name] = set;
            }
            if (!sets.ContainsKey(FactionRankSet.PlayerSet))
                throw new FormatException($"no \"{FactionRankSet.PlayerSet}\" set (player factions use it)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            sets.Clear();
            problem = $"{Path.GetFileName(path)}: {ex.Message}";
            Console.WriteLine($"[factions] refused {problem}; using the built-in ranks");
        }
        return sets;
    }
}
