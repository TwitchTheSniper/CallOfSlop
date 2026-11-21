namespace CallOfSlop;

/// <summary>A line in the music player: a track.</summary>
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

    /// <summary>The track.</summary>
    public required MusicTrack Track { get; init; }

    /// <summary>The row's label with a unique ImGui id.</summary>
    public string RowId { get; set; } = "";

    /// <summary>What the filter searches, lower case.</summary>
    public string Search { get; set; } = "";
}

/// <summary>
/// Every piece of music in the game for the music player, grouped: all of <see cref="MusicTracks.All"/>, read from the
/// game's own table, so a new track shows up by itself. All the game's music is
/// generated in code; the files in Audio/ are short gun samples, so there is nothing else to list.
/// </summary>
public static class MusicCatalogue
{
    public const string GroupMenu = "Menu tracks", GroupLobby = "Lobby tracks", GroupHardbass = "Hardbass", GroupHarder = "Harder hardbass",
        GroupWeird = "Weird hardbass";

    private static readonly string[] Groups = [GroupMenu, GroupLobby, GroupHardbass, GroupHarder, GroupWeird];

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

    /// <summary>Seconds as m:ss.</summary>
    public static string Time(double seconds)
    {
        var s = (int)Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }
}
