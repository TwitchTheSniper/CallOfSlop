namespace CallOfSlop;

/// <summary>The genre recipe a track is built from: drum grooves, bass style, lead sound and sidechain.</summary>
public enum MusicStyle
{
    Hardstyle, // 150 BPM: tonal distorted kick, off-beat bass, screaming supersaw leads
    Hardcore, // 165-175 BPM gabber: the kick is the bass, hoover-like gliding lead
    Industrial, // 140-145 BPM: rolling 16th bass, metallic square stabs and arps
    DrumAndBass, // 170-175 BPM: two-step breaks, a long wobbling reese bass
    Darksynth, // 155-160 BPM metal-flavoured synth: double kicks, chugging distorted bass, fuzz lead
    Hardbass, // 145-165 BPM (and one 200 BPM monster): clipped pounding kick, bouncy pumping off-beat bass, chants, hoovers and screeches
}

/// <summary>The lead sound of a hardbass track (it can change for the later drops).</summary>
public enum HardbassLead : byte
{
    Supersaw, // seven detuned saws with a third on top: the anthem
    Hoover, // wide detuned saws that swoop down onto every note, with vibrato and an octave below
    Screech, // a resonant band-passed saw with a wobbling filter: the hardstyle screech
    Rave, // a hard square chord stab
}

/// <summary>When a hardbass track swaps its off-beat bass for a hardstyle reverse bass.</summary>
public enum ReverseBassUse : byte
{
    Never,
    LaterDrops, // the second and later drops
    Alternate, // every other four bars of every drop
}

/// <summary>
/// What makes one hardbass track different from the next: lead sounds, bassline, chants, reverse bass and kick.
/// Bass patterns are 16 steps ('.' rest, 'x' root, 'o' root an octave up, 'f' fifth); the chant pattern is 32 steps
/// over two bars ('.' none, 'h' HEY, 'o' OI, 'a' HA, 'y' YAH).
/// </summary>
public sealed record HardbassFlavor(
    HardbassLead Lead,
    HardbassLead LaterLead,
    string BassA,
    string BassB,
    string Chants,
    ReverseBassUse Reverse,
    float KickClip = 1.6f,
    float Bounce = 5f,
    bool HalfTimeBreak = false,
    HardbassHarder? Harder = null);

/// <summary>Settings > Audio > Music style: every track, or only the hardbass ones.</summary>
public enum MusicChoice
{
    Mixed,
    HardbassOnly,
}

/// <summary>Which playlist a track belongs to.</summary>
public enum MusicSet
{
    Menu, // the main menu and its pages
    Lobby, // in a session before and between matches: warmup, ready-up, countdown, results and map vote
}

/// <summary>A section of a track's arrangement.</summary>
public enum MusicPart : byte
{
    Intro,
    Build, // snare roll, riser and a lead whose filter opens, then a gap before the drop
    Drop, // everything
    Break, // no drums: pads and a filtered lead
    Outro,
}

/// <summary>
/// One procedurally generated track: its genre, tempo, key, chord loop, arrangement and the seed its melodies are
/// written from. The audio itself is synthesised in real time by <see cref="MusicRenderer"/>.
/// </summary>
public sealed class MusicTrack
{
    public string Name { get; }
    public MusicSet Set { get; }
    public MusicStyle Style { get; }
    public float Bpm { get; }

    /// <summary>MIDI note of the key's root in the bass octave (leads play two octaves up).</summary>
    public int Root { get; }

    /// <summary>Display name of the key, e.g. "F# harmonic minor".</summary>
    public string Key { get; }

    /// <summary>The scale as semitones above the root (seven notes).</summary>
    public int[] Scale { get; }

    /// <summary>Chord roots as scale degrees, one per bar, looping (4 or 8 bars).</summary>
    public int[] Chords { get; }

    /// <summary>Seed for the melodies, riff choices and fills.</summary>
    public uint Seed { get; }

    /// <summary>The arrangement, e.g. "I8 B8 D16 K8 B8 D16 O8" (Intro, Build, Drop, breaK, Outro and their bars).</summary>
    public string Form { get; }

    public (MusicPart Part, int Bars)[] Sections { get; }
    public int TotalBars { get; }

    /// <summary>Bars of reverb/delay tail rendered after the last bar.</summary>
    public const int TailBars = 1;

    public double SecondsPerBar => 4 * 60.0 / Bpm;

    /// <summary>Length including the tail.</summary>
    public double Seconds => (TotalBars + TailBars) * SecondsPerBar;

    /// <summary>The hardbass recipe (non-null for <see cref="MusicStyle.Hardbass"/> tracks).</summary>
    public HardbassFlavor? Hardbass { get; }

    public MusicTrack(string name, MusicSet set, MusicStyle style, float bpm, int root, string key, int[] scale, int[] chords, uint seed, string form,
                      HardbassFlavor? hardbass = null)
    {
        Hardbass = style == MusicStyle.Hardbass ? hardbass ?? MusicTracks.DefaultHardbass : null;
        Name = name;
        Set = set;
        Style = style;
        Bpm = bpm;
        Root = root;
        Key = key;
        Scale = scale;
        Chords = chords;
        Seed = seed;
        Form = form;
        Sections = ParseForm(form);
        TotalBars = Sections.Sum(s => s.Bars);
    }

    /// <summary>Parses an arrangement string; unknown tokens are skipped.</summary>
    public static (MusicPart Part, int Bars)[] ParseForm(string form)
    {
        var list = new List<(MusicPart, int)>();
        foreach (var tok in form.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok.Length < 2 || !int.TryParse(tok[1..], out var bars) || bars <= 0)
                continue;
            MusicPart? part = char.ToUpperInvariant(tok[0]) switch
            {
                'I' => MusicPart.Intro,
                'B' => MusicPart.Build,
                'D' => MusicPart.Drop,
                'K' => MusicPart.Break,
                'O' => MusicPart.Outro,
                _ => null,
            };
            if (part is { } p)
                list.Add((p, bars));
        }
        return list.ToArray();
    }

    /// <summary>The first bar of the first drop (the self-test listens there).</summary>
    public int FirstDropBar
    {
        get
        {
            var bar = 0;
            foreach (var (part, bars) in Sections)
            {
                if (part == MusicPart.Drop)
                    return bar;
                bar += bars;
            }
            return 0;
        }
    }

    public override string ToString() => $"{Name} ({Style}, {Bpm:0} BPM, {Key})";
}

/// <summary>The built-in tracks: original, generated in code, so there's nothing to license or ship.</summary>
public static class MusicTracks
{
    public static readonly int[] Minor = [0, 2, 3, 5, 7, 8, 10];
    public static readonly int[] HarmonicMinor = [0, 2, 3, 5, 7, 8, 11];
    public static readonly int[] Phrygian = [0, 1, 3, 5, 7, 8, 10];
    public static readonly int[] PhrygianDominant = [0, 1, 4, 5, 7, 8, 10];
    public static readonly int[] Dorian = [0, 2, 3, 5, 7, 9, 10];

    /// <summary>The recipe for a hardbass track made without one.</summary>
    public static readonly HardbassFlavor DefaultHardbass =
        new(HardbassLead.Supersaw, HardbassLead.Supersaw, "..x...x...x...x.", "..x...o...x...o.", "............................h...", ReverseBassUse.Never);

    public static readonly MusicTrack[] All =
    [
        // ---- Main menu -------------------------------------------------------------------------------
        new("Slop Protocol", MusicSet.Menu, MusicStyle.Hardstyle, 150, 42, "F# harmonic minor", HarmonicMinor,
            [0, 5, 3, 4], 0x51A9_0001, "I8 B8 D16 K16 B8 D16 D8 O8"),
        new("Grinder", MusicSet.Menu, MusicStyle.Industrial, 140, 38, "D phrygian", Phrygian,
            [0, 0, 1, 0, 5, 5, 1, 6], 0x61D3_0002, "I16 B8 D16 K8 B8 D24 O8"),
        new("Lead Rain", MusicSet.Menu, MusicStyle.DrumAndBass, 174, 40, "E minor", Minor,
            [0, 5, 2, 6], 0x1EAD_0003, "I16 B8 D32 K16 B8 D32 O8"),
        new("Kill Feed Overdrive", MusicSet.Menu, MusicStyle.Darksynth, 160, 37, "C# phrygian dominant", PhrygianDominant,
            [0, 1, 0, 6, 0, 1, 5, 6], 0xF33D_0004, "I8 B8 D16 K8 B8 D16 D16 O8"),
        new("Frag Frenzy", MusicSet.Menu, MusicStyle.Hardcore, 172, 45, "A harmonic minor", HarmonicMinor,
            [0, 6, 5, 4], 0xF4A6_0005, "I8 B8 D16 K8 B8 D32 O8"),

        // ---- Lobby (warmup, ready-up, countdown, results, map vote) ----------------------------------
        new("Ready Up", MusicSet.Lobby, MusicStyle.Hardcore, 165, 43, "G minor", Minor,
            [0, 2, 5, 6], 0x2EAD_0006, "I8 B4 D16 K8 B4 D16 O8"),
        new("Warmup Riot", MusicSet.Lobby, MusicStyle.Industrial, 145, 35, "B phrygian", Phrygian,
            [0, 1, 0, 6], 0x3A2F_0007, "I8 B8 D16 K8 B4 D16 O8"),
        new("Respawn Engine", MusicSet.Lobby, MusicStyle.DrumAndBass, 172, 39, "D# dorian", Dorian,
            [0, 3, 0, 6], 0x4E59_0008, "I8 B8 D32 K8 B8 D16 O8"),
        new("Results Screen of Death", MusicSet.Lobby, MusicStyle.Darksynth, 155, 34, "A# harmonic minor", HarmonicMinor,
            [0, 5, 3, 4], 0xDEAD_0009, "I8 B8 D16 K8 B8 D16 O8"),

        // ---- Hardbass (menu) -------------------------------------------------------------------------
        new("Tracksuit Tactical", MusicSet.Menu, MusicStyle.Hardbass, 156, 35, "B minor", Minor,
            [0, 5, 2, 6], 0x7AC7_000A, "I8 B8 D16 K4 B8 D16 D8 O4",
            new(HardbassLead.Supersaw, HardbassLead.Supersaw, "..x...x...x...o.", "..x...o...x..xo.",
                "............................h...", ReverseBassUse.Never)),
        new("Ushanka Uprising", MusicSet.Menu, MusicStyle.Hardbass, 146, 38, "D harmonic minor", HarmonicMinor,
            [0, 3, 4, 0, 5, 3, 4, 4], 0x05A1_000B, "I8 B4 D16 K8 B8 D16 D16 O8",
            new(HardbassLead.Hoover, HardbassLead.Hoover, "..x...x...x...x.", "..x...x...f...o.",
                "o...............o.......o.......", ReverseBassUse.LaterDrops, KickClip: 1.4f, Bounce: 3f)),
        new("Panelka Pressure", MusicSet.Menu, MusicStyle.Hardbass, 161, 41, "F phrygian", Phrygian,
            [0, 0, 1, 1, 6, 6, 5, 1], 0x9A7E_000C, "I4 B8 D16 K4 B4 D16 D16 O4",
            new(HardbassLead.Screech, HardbassLead.Screech, "..xx..xx..xx..xo", "..xo..xx..xo..xx",
                "........a.......a.......a...a.a.", ReverseBassUse.Alternate, KickClip: 2f, Bounce: 7f)),
        new("Squat Speedrun", MusicSet.Menu, MusicStyle.Hardbass, 200, 37, "C# minor", Minor,
            [0, 6, 5, 4], 0x200B_000D, "I8 B8 D16 K8 B8 D16 D16 O8",
            new(HardbassLead.Hoover, HardbassLead.Supersaw, "..x...x...x...x.", "..x...o...x...o.",
                "h...............h.......h...h...", ReverseBassUse.LaterDrops, KickClip: 1.8f, Bounce: 4f, HalfTimeBreak: true)),

        // ---- Hardbass (lobby) ------------------------------------------------------------------------
        new("Hey Hey Headshot", MusicSet.Lobby, MusicStyle.Hardbass, 152, 44, "G# minor", Minor,
            [0, 6, 5, 6], 0x4E4E_000E, "I8 B8 D8 D8 K4 B8 D16 O4",
            new(HardbassLead.Rave, HardbassLead.Supersaw, "..o...x...o...x.", "..o.x.o...o.x.o.",
                "h.......h.......h...h...h.h.h...", ReverseBassUse.Never, KickClip: 1.5f, Bounce: 6f)),
        new("Squat Before Respawn", MusicSet.Lobby, MusicStyle.Hardbass, 158, 36, "C harmonic minor", HarmonicMinor,
            [0, 5, 3, 4], 0x5B4F_000F, "I8 B8 D16 K8 B8 D16 D8 O4",
            new(HardbassLead.Supersaw, HardbassLead.Screech, "..x...x...x...x.", "..x..xx...x..xo.",
                "............a...............a.a.", ReverseBassUse.Alternate, KickClip: 1.7f)),
        new("Kvass Overflow", MusicSet.Lobby, MusicStyle.Hardbass, 164, 40, "E phrygian dominant", PhrygianDominant,
            [0, 1, 0, 6, 0, 1, 5, 1], 0x6A55_0010, "I4 B8 D16 K4 B8 D24 O8",
            new(HardbassLead.Hoover, HardbassLead.Rave, "..x...x...x...x.", "..x.o.x...x.o.x.",
                "y.......y.......y.......y...o...", ReverseBassUse.LaterDrops, KickClip: 1.3f, Bounce: 2f)),

        // ---- Harder hardbass (menu and lobby; MusicTracksHarder.cs) ----------------------------------
        .. MusicTracksHarder.Tracks,
    ];

    public static MusicTrack[] For(MusicSet set) => All.Where(t => t.Set == set).ToArray();

    /// <summary>The tracks of a set, or only its hardbass tracks.</summary>
    public static MusicTrack[] For(MusicSet set, bool hardbassOnly) =>
        All.Where(t => t.Set == set && (!hardbassOnly || t.Style == MusicStyle.Hardbass)).ToArray();
}
