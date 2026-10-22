namespace CallOfSlop;

/// <summary>
/// Weird hardbass: the hardbass core (pumping kick, off-beat bass, chants), then odd meters, tempo games, swerves into
/// lo-fi or a music box, microtones, accordion and chip leads and toy sound effects (see MusicWeird.cs).
/// </summary>
public static class MusicTracksWeird
{
    // The scales are spelled out (not MusicTracks.Minor etc.): MusicTracks.All includes these tracks, so reading
    // MusicTracks fields here would make the two static initialisers depend on each other.
    public static readonly MusicTrack[] Tracks =
    [
        new("Seven Eighths of a Squat", MusicSet.Menu, MusicStyle.Hardbass, 151.5f, 45, "A dorian", [0, 2, 3, 5, 7, 9, 10],
            [0, 3, 6, 4], 0x7E18_0011, "I8 B8 D16 K4 D16 K8 B8 D16 O4",
            new(HardbassLead.Rave, HardbassLead.Screech, "..x...o...x...f.", "..xo..x...xo..x.",
                "h.......h.......h...h.......h.h.", ReverseBassUse.Never, KickClip: 1.6f, Bounce: 5f))
        {
            Weird = new(".57m..u7.", ChipArp: true, Sfx: "......s.......s.......s...s.s...", WhistleIntoDrops: true,
                        ChantSemis: 12f, ChantCrush: 0.5f),
        },
        new("Quarter-Tone Gopnik", MusicSet.Menu, MusicStyle.Hardbass, 147.5f, 38, "D quarter-flat minor", [0, 2, 3, 5, 7, 8, 10],
            [0, 5, 6, 4], 0x9077_0013, "I8 B8 D16 K8 B8 D16 D8 O8",
            new(HardbassLead.Hoover, HardbassLead.Screech, "..x...x...x..xo.", "..x...xo..x...xo",
                "a.......a...a...a.......a.a.a.a.", ReverseBassUse.LaterDrops, KickClip: 1.5f, Bounce: 4f))
        {
            Weird = new("..5...5.", ExtraBeatSfx: 'b', ChantSemis: -7f, ChantSemisAlt: 12f, ChantCrush: 0.75f,
                        Micro: [0f, -0.5f, 0f, 0f, 0f, -0.5f, 0f], HarmonyCents: 40f),
        },
        new("Babushka Polka Overdrive", MusicSet.Lobby, MusicStyle.Hardbass, 157.5f, 34, "Bb hungarian minor", [0, 2, 3, 6, 7, 8, 11],
            [0, 4, 0, 4, 0, 4, 5, 4], 0xB0B4_0012, "I8 B8 D16 K8 B8 D16 B4 D16 O4",
            new(HardbassLead.Supersaw, HardbassLead.Hoover, "..x...f...x...f.", "..x.x.f...x.x.f.",
                "h...............h...h...h.......", ReverseBassUse.Never, KickClip: 1.7f, Bounce: 6f))
        {
            Weird = new(".u..u..r.", Lead: WeirdLead.Accordion, LeadUse: WeirdUse.Always,
                        Sfx: "...........b...............b..v.", WhistleIntoDrops: true),
        },
        new("Drunk Duck Breakdown", MusicSet.Lobby, MusicStyle.Hardbass, 149.5f, 42, "F# minor", [0, 2, 3, 5, 7, 8, 10],
            [0, 3, 5, 4], 0xD0CC_0014, "I8 B8 D16 K8 B4 D16 K4 B4 D16 O4",
            new(HardbassLead.Supersaw, HardbassLead.Rave, "..x...x...x...xo", "..x..xx...x..xo.",
                "y...............y.......y...y...", ReverseBassUse.Alternate, KickClip: 1.6f, Bounce: 3f))
        {
            Weird = new("...d..lu..", Sfx: "......q.......q.......q...q.q...", ChantSemisAlt: 5f, ChantCrush: 0.3f),
        },
        new("Lullaby for a Tracksuit", MusicSet.Lobby, MusicStyle.Hardbass, 155.5f, 43, "G melodic minor", [0, 2, 3, 5, 7, 9, 11],
            [0, 3, 1, 4], 0x1011_0015, "I8 B8 D16 K4 B4 D16 K4 D16 O4",
            new(HardbassLead.Screech, HardbassLead.Supersaw, "..o...x...o...f.", "..o...x..xo...f.",
                "h...h...........h...h...h.h.....", ReverseBassUse.Never, KickClip: 1.5f, Bounce: 5f))
        {
            Weird = new("...m57.r.", Lead: WeirdLead.Chip, LeadUse: WeirdUse.LaterDrops, ChipArp: true,
                        Sfx: "....q.......s.......q.......s..b", ExtraBeatSfx: 'q', WhistleIntoDrops: true, ChantSemis: 7f, ChantCrush: 0.4f),
        },
    ];
}
