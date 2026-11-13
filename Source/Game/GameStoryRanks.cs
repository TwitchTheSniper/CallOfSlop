using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// Rank-up moments (docs/story/STORY.md "Ranks"): a promotion moment when a player's standing with a story faction first
/// rises into a higher tier: the tier's name, what it brings (the uniform it issues, the technologies it now teaches, the
/// jobs it opens) and the faction's own words for it (a tier's "promotion" in Story/Factions). Host: once a second from
/// <see cref="StoryGroupsTick"/>. A player's first look, a new group (they joined or left a player faction) or a new story
/// is taken quietly, so loading a save or joining a well-liked faction is not a promotion. The Faction window's bars to
/// the next tier (UiRanks.cs) read the standings every peer already has (<see cref="FactionTable.PlayerStanding"/>).
/// </summary>
public sealed partial class Game
{
    private readonly Player?[] _rankFor = new Player?[256]; // per slot: the player the row below was taken for
    private readonly int[] _rankParty = new int[256]; // per slot: their party (player or group) then
    private readonly sbyte[] _rankTier = new sbyte[256 * FactionTable.MaxStory]; // per slot and faction: the tier then
    private StoryEngine? _rankStory;

    /// <summary>Both sides, a new match: nothing known.</summary>
    private void StoryRanksReset()
    {
        Array.Clear(_rankFor);
        _rankStory = null;
    }

    /// <summary>Server, once a second: each player's tiers, and a promotion for each that rose. No allocation.</summary>
    private void StoryRanksTick(StoryEngine s)
    {
        if (!ReferenceEquals(_rankStory, s))
        {
            _rankStory = s;
            Array.Clear(_rankFor); // (a new story: everyone is looked at afresh)
        }
        var count = Math.Min(s.Factions.Count, FactionTable.MaxStory);
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers || StoryWho(p) is not (var who and >= 0))
                continue;
            var party = s.Factions.Party(who);
            var fresh = _rankFor[p.Id] != p || _rankParty[p.Id] != party;
            var row = p.Id * FactionTable.MaxStory;
            for (var f = 0; f < count; f++)
            {
                var def = s.Factions.Def(f);
                var tier = (sbyte)def.TierOf(s.Factions.Standing(f, party));
                if (!fresh && tier > _rankTier[row + f] && tier >= 0)
                    StoryPromote(p, def, _rankTier[row + f], tier);
                _rankTier[row + f] = tier;
            }
            _rankFor[p.Id] = p;
            _rankParty[p.Id] = party;
        }
    }

    /// <summary>
    /// A player's standing rose into a higher tier of a faction: the promotion moment on their screen ("PROMOTED", "Trusty,
    /// Ministry of Slop", then what it brings: the uniform issued, the technologies it now teaches, the jobs it opens; each
    /// technology gets its own moment after, from the technologies' side), and the faction's words for the tier (its
    /// "promotion" in Story/Factions) in their chat, in its voice ("[MINISTRY MEMO] ..."). A rise that is still below where
    /// they started from (out of a grudge) is "RECONSIDERED", not a promotion.
    /// </summary>
    private void StoryPromote(Player p, FactionDef def, int was, int tier)
    {
        var t = def.Tiers[tier];
        var title = tier > def.TierOf(0) ? "PROMOTED" : "RECONSIDERED";
        var line = RankUnlockLine(def, Math.Max(was, -1), tier, _opOn && p.Look.WearsUniform, "Issued", "Now teaches");
        if (line.Length == 0)
            line = t.Promotion;
        else if (t.Promotion.Length > 0)
            RankSay(p, def, t.Promotion);
        if (line.Length > CelebrateLineMax) // (the moment's line holds 200 characters: cut between words)
            line = line[..line.LastIndexOf(' ', CelebrateLineMax - 4)] + " ...";
        Console.WriteLine($"[story] {p.Name} rose to {t.Name} with {def.Name}");
        RankCelebrate(p, title, $"{t.Name}, {def.Name}", line);
    }

    private const int CelebrateLineMax = 200;

    /// <summary>A faction's words to one player, in its voice, in their chat (a long one as several lines, broken between words).</summary>
    private void RankSay(Player p, FactionDef def, string text)
    {
        var prefix = $"[{(def.Voice.Length > 0 ? def.Voice : def.Name.ToUpperInvariant())}] ";
        var room = ChatRules.MaxLength - prefix.Length;
        var rest = text.AsSpan().Trim();
        while (rest.Length > 0)
        {
            var cut = rest.Length <= room ? rest.Length : rest[..room].LastIndexOf(' ') is var sp and > 0 ? sp : room;
            OfficeLine(p.Position, 1, prefix + rest[..cut].ToString(), Math.Max(0, _storyLast), p);
            rest = rest[cut..].TrimStart();
        }
    }

    /// <summary>
    /// What reaching a tier brings, in a line ("Issued: Trusty's Boiler Suit and Flat Cap. Now teaches: Explosives Licence.
    /// New work: Fit Out the Cell Block."): the uniform of that tier (when <paramref name="uniforms"/>), the technologies and
    /// the jobs above <paramref name="from"/> up to it. Both sides (the window's bar says it of the next tier). Empty: nothing.
    /// </summary>
    public static string RankUnlockLine(FactionDef def, int from, int tier, bool uniforms, string issued, string teaches)
    {
        var name = def.Tiers[tier].Name;
        var parts = new List<string>(3);
        if (uniforms)
            foreach (var u in Uniforms.All)
                if (u.Faction.Equals(def.Name, StringComparison.OrdinalIgnoreCase) && Array.Exists(u.Tiers, x => x.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    parts.Add($"{issued}: {u.Name}.");
                    break;
                }
        var techs = def.Teaches.Where(x => x.Tier > from && x.Tier <= tier).Select(x => x.Name).ToArray();
        if (techs.Length > 0)
            parts.Add($"{teaches}: {string.Join(", ", techs)}.");
        var jobs = def.Jobs.Where(x => x.Tier > from && x.Tier <= tier).Select(x => x.Name).ToArray();
        if (jobs.Length > 0)
            parts.Add($"New work: {string.Join(", ", jobs)}.");
        return string.Join(" ", parts);
    }

    /// <summary>The promotion moment on one player's screen (the shared celebration: GameCelebrate.cs).</summary>
    private void RankCelebrate(Player p, string title, string name, string line) => Celebrate(p.Id, title, name, line);
}
