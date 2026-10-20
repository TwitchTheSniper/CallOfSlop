namespace CallOfSlop;

/// <summary>The genre recipe a track is built from: drum grooves, bass style, lead sound and sidechain.</summary>
public enum MusicStyle
{
    Hardstyle, // 150 BPM: tonal distorted kick, off-beat bass, screaming supersaw leads
    Hardcore, // 165-175 BPM gabber: the kick is the bass, hoover-like gliding lead
    Industrial, // 140-145 BPM: rolling 16th bass, metallic square stabs and arps
    DrumAndBass, // 170-175 BPM: two-step breaks, a long wobbling reese bass
    Darksynth, // 155-160 BPM metal-flavoured synth: double kicks, chugging distorted bass, fuzz lead
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

    public MusicTrack(string name, MusicSet set, MusicStyle style, float bpm, int root, string key, int[] scale, int[] chords, uint seed, string form)
    {
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
    ];

    public static MusicTrack[] For(MusicSet set) => All.Where(t => t.Set == set).ToArray();
}
