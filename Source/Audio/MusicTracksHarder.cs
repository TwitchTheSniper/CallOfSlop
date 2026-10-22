namespace CallOfSlop;

/// <summary>
/// The "harder" hardbass recipe on top of a <see cref="HardbassFlavor"/> (see MusicRendererHarder.cs): a layered,
/// harder driven gabber kick with its own drop pattern, more and wider detuned supersaw voices, a growling bass, a bigger
/// crowd, sirens and a pitch riser. Kick patterns are 16 steps ('.' rest, anything else a kick).
/// </summary>
public sealed record HardbassHarder(
    string KickB, // the kick of every other eight bars of a drop: gabber variations ("x...x...x...x.xx")
    float KickDrive = 11f,
    float KickTop = 0.5f, // level of the layered top kick
    int LeadSaws = 9,
    float LeadDetune = 1.5f, // multiplies the lead's detune: harsher, wider supersaws
    float BassDrive = 9f,
    float Siren = 0.3f, // siren level over the builds and the first two bars of every drop
    float SirenSemis = 5f, // how far the siren wails up and down
    float Riser = 0.28f, // pitch riser level through the builds
    float Crowd = 0.6f, // the octave-down crowd under every chant
    float Master = 1.5f); // drive into the clipper and limiter (denser mixes need a little less to stay as loud as the rest)

/// <summary>Hardbass that goes even harder: 163-175 BPM and one 215 BPM monster.</summary>
public static class MusicTracksHarder
{
    // The scales are spelled out (not MusicTracks.Minor etc.): MusicTracks.All includes these tracks, so reading
    // MusicTracks fields here would make the two static initialisers depend on each other.
    public static readonly MusicTrack[] Tracks =
    [
        new("Babushka Bunker Buster", MusicSet.Menu, MusicStyle.Hardbass, 168, 42, "F# phrygian", [0, 1, 3, 5, 7, 8, 10],
            [0, 1, 0, 6, 0, 1, 5, 6], 0xBAB0_0011, "I4 B8 D16 K8 B8 D16 K4 B8 D16 O4",
            new(HardbassLead.Supersaw, HardbassLead.Hoover, "..xo..xo..xo..xf", "..xo..xo..xo.oxo",
                "h...h...h...h...h.h.h.h.hhh.h...", ReverseBassUse.Alternate, KickClip: 2.4f, Bounce: 6f,
                Harder: new("x...x...x...x.xx"))),
        new("Kettlebell Killstreak", MusicSet.Menu, MusicStyle.Hardbass, 175, 39, "D# phrygian dominant", [0, 1, 4, 5, 7, 8, 10],
            [0, 1, 0, 1, 6, 6, 5, 1], 0x4E77_0012, "I4 B8 D16 K8 B8 D16 K8 B8 D16 O4",
            new(HardbassLead.Hoover, HardbassLead.Screech, "..xx..xx..xx..xx", "..xo..xo..xf..xo",
                "h...h.h.h...h.h.y...y.y.y...hhhh", ReverseBassUse.Alternate, KickClip: 2.6f, Bounce: 8f, HalfTimeBreak: true,
                Harder: new("x...x..xx...x...", KickDrive: 12f, Siren: 0.25f))),
        new("Marshrutka Max Overdrive", MusicSet.Menu, MusicStyle.Hardbass, 215, 45, "A phrygian", [0, 1, 3, 5, 7, 8, 10],
            [0, 1, 6, 5], 0x215A_0013, "I8 B8 D16 K8 B8 D16 K8 B8 D16 O8",
            new(HardbassLead.Supersaw, HardbassLead.Rave, "..x...x...x..xo.", "..x..xo...x..xo.",
                "h.......h.......h...h...h.h.hhhh", ReverseBassUse.LaterDrops, KickClip: 2.2f, Bounce: 5f, HalfTimeBreak: true,
                Harder: new("x...x...x...x.x.", KickDrive: 12f, KickTop: 0.6f, Riser: 0.32f))),
        new("Semechki Shrapnel", MusicSet.Lobby, MusicStyle.Hardbass, 171, 34, "A# minor", [0, 2, 3, 5, 7, 8, 10],
            [0, 5, 3, 6], 0x5E3E_0014, "I8 B8 D16 K8 B8 D24 O4",
            new(HardbassLead.Screech, HardbassLead.Supersaw, ".xx..xx..xx..xx.", ".xx..xo..xx..xof",
                "a.......a...a...a.a.....a.a.a.a.", ReverseBassUse.LaterDrops, KickClip: 2.3f, Bounce: 7f,
                Harder: new("x...x...x..xx...", Siren: 0.22f, Crowd: 0.5f))),
        new("Dvor Siren Drill", MusicSet.Lobby, MusicStyle.Hardbass, 163, 43, "G harmonic minor", [0, 2, 3, 5, 7, 8, 11],
            [0, 3, 4, 4], 0xD5D0_0015, "I8 B8 D16 K8 B8 D16 K4 B4 D16 O4",
            new(HardbassLead.Rave, HardbassLead.Hoover, "..x...o...x...o.", "..x.x.o...x.x.o.",
                "o.......h.......o.......h.h.h.h.", ReverseBassUse.Never, KickClip: 2.2f, Bounce: 8f,
                Harder: new("x...x...x...x..x", Siren: 0.4f, SirenSemis: 7f, Crowd: 0.7f))),
    ];
}
