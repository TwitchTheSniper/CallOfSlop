using System.Text;

namespace CallOfSlop;

/// <summary>A line in the music player: a track, or a frenzy class (which plays at a level, on a track from its pool).</summary>
public sealed class MusicEntry
{
    public required string Group { get; init; }
    public required string Name { get; init; }
    public required string Style { get; init; }

    /// <summary>Where the game plays it: Menu, Lobby or Match.</summary>
    public required string Mode { get; init; }

    public required string Bpm { get; init; }
    public required string Length { get; init; }
    public required string Key { get; init; }

    /// <summary>A longer description (tooltip); empty for most tracks.</summary>
    public string About { get; init; } = "";

    /// <summary>The track (null for a frenzy class).</summary>
    public MusicTrack? Track { get; init; }

    /// <summary>The frenzy class of a frenzy line.</summary>
    public WeaponClass Class { get; init; }

    /// <summary>The tracks a frenzy class picks from (empty for a track).</summary>
    public MusicTrack[] Pool { get; init; } = [];

    /// <summary>The row's label with a unique ImGui id.</summary>
    public string RowId { get; set; } = "";

    /// <summary>What the filter searches, lower case.</summary>
    public string Search { get; set; } = "";

    public bool IsFrenzy => Track == null;
}

/// <summary>
/// Every piece of music in the game for the music player, grouped: all of <see cref="MusicTracks.All"/> and one line per
/// frenzy class style, read from the game's own tables, so a new track or class shows up by itself. All the game's music is
/// generated in code; the files in Audio/ are short gun samples, so there is nothing else to list.
/// </summary>
public static class MusicCatalogue
{
    public const string GroupMenu = "Menu tracks", GroupLobby = "Lobby tracks", GroupHardbass = "Hardbass", GroupHarder = "Harder hardbass",
        GroupWeird = "Weird hardbass", GroupFrenzy = "Kill frenzy (pick a level below)";

    private static readonly string[] Groups = [GroupMenu, GroupLobby, GroupHardbass, GroupHarder, GroupWeird, GroupFrenzy];

    /// <summary>The list, in order, group by group.</summary>
    public static MusicEntry[] Build()
    {
        var list = new List<MusicEntry>();
        foreach (var t in MusicTracks.All)
            list.Add(new MusicEntry
            {
                Group = GroupOf(t),
                Name = t.Name,
                Style = StyleName(t),
                Mode = t.Set == MusicSet.Menu ? "Menu" : "Lobby",
                Bpm = t.Bpm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                Length = Time(t.Seconds),
                Key = t.Key,
                Track = t,
            });

        var steady = MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass && (t.Timeline?.SteadyDrops ?? true)).ToArray();
        var styles = FrenzyStyles.All;
        for (var c = 0; c < styles.Length; c++)
        {
            var style = styles[c];
            var pool = FrenzyLayer.PoolTracks(steady, style.Pool);
            if (pool.Length == 0)
                pool = steady; // as the layer does
            var lo = pool.Min(t => t.Bpm);
            var hi = pool.Max(t => t.Bpm);
            list.Add(new MusicEntry
            {
                Group = GroupFrenzy,
                Name = $"{ClassName((WeaponClass)c)} frenzy",
                Style = style.Name,
                Mode = "Match",
                Bpm = lo == hi ? $"{lo:0.#}" : $"{lo:0}-{hi:0}",
                Length = "loops",
                Key = "by track",
                About = style.Describe,
                Class = (WeaponClass)c,
                Pool = pool,
            });
        }

        var entries = list.OrderBy(e => Array.IndexOf(Groups, e.Group)).ToArray(); // stable: a group keeps the tables' order
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            e.RowId = $"{e.Name}##mp{i}";
            e.Search = $"{e.Name} {e.Style} {e.Mode} {e.Key} {e.Bpm} {e.Group} {e.About}".ToLowerInvariant();
        }
        return entries;
    }

    private static string GroupOf(MusicTrack t) =>
        t.Weird != null ? GroupWeird
        : t.Hardbass?.Harder != null ? GroupHarder
        : t.Style == MusicStyle.Hardbass ? GroupHardbass
        : t.Set == MusicSet.Menu ? GroupMenu : GroupLobby;

    private static string StyleName(MusicTrack t) =>
        t.Weird != null ? "Weird hardbass"
        : t.Hardbass?.Harder != null ? "Harder hardbass"
        : t.Style switch
        {
            MusicStyle.DrumAndBass => "Drum and bass",
            _ => t.Style.ToString(),
        };

    /// <summary>"AssaultRifle" as "Assault Rifle"; "SMG" stays.</summary>
    private static string ClassName(WeaponClass c)
    {
        var name = c.ToString();
        var b = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                b.Append(' ');
            b.Append(name[i]);
        }
        return b.ToString();
    }

    /// <summary>Seconds as m:ss.</summary>
    public static string Time(double seconds)
    {
        var s = (int)Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }
}
