# Music: a guide for making and changing it

Read this before you touch music. All of it is synthesised live from code: there are no audio files, samples or
licences. Written 2026-11-03 from the code at that date. "Inferred" marks anything the code doesn't state outright;
"measured" marks numbers from one run in the agent container (2 cores), which are a guide, not a contract.

**Nobody on the agent side can hear.** You can measure level, spectrum and cost, and you can follow the patterns that
already work. Whether it sounds good is for the user to judge ("Ask the user" near the end).

## What plays when

`MusicPlayer.MoodFor(inSession, phase)` (`Music.cs`) is the whole rule. `Program.cs` calls it every frame.

| Game state | Mood | What you hear |
| --- | --- | --- |
| No session (main menu and its pages) | `Menu` | Menu playlist |
| In a session, phase `Lobby`, `Warmup`, `Countdown` or `Ended` | `Lobby` | Lobby playlist |
| In a session, phase `Playing` | `Off` | Silence, plus the kill-frenzy layer if the local player is on a streak |

**Sandbox has no music of its own.** `MoodFor` looks only at the phase, and no mode module has a music hook. A live
Sandbox world is phase `Playing` and its module is `Endless`, so it is silent for as long as it runs. Kill frenzy
doesn't play there either: the server (`FrenzyAddKill`) and the client (`Program.Frenzy.cs`) both skip kills when the
mode's `ArenaExtras` is false, as `SandboxModule` sets it. Zombies kills (worth 0.4 of a kill) and the arena modes
still feed it. See "Giving Sandbox music" below; that work has not been done.

Now-playing text shows on the main menu (`Lobby.cs`). Settings: `music_volume` (default 0.50), `music_style`
(`Mixed` or `HardbassOnly`), `frenzy_music`, `frenzy_volume` (0.80).

## The tracks

26 tracks in `MusicTracks.All`: 14 menu, 12 lobby. README "Music" has the full table of names, tempos and keys.

| Family | Tracks | Where | Sound |
| --- | --- | --- | --- |
| Older genres | 9 | `MusicTracks.cs` | `MusicStyle` Hardstyle, Hardcore, Industrial, DrumAndBass, Darksynth |
| Classic hardbass | 7 | `MusicTracks.cs` | Clipped kick, bouncy off-beat bass, chants, four lead sounds |
| Harder hardbass | 5 | `MusicTracksHarder.cs`, `MusicRendererHarder.cs` | `HardbassFlavor.Harder`: layered gabber kick, 9 saws, sirens, pitch riser, crowd |
| Weird hardbass | 5 | `MusicTracksWeird.cs`, `MusicWeird.cs`, `MusicRenderer.Weird.cs`, `MusicWeirdVoices.cs` | `MusicTrack.Weird`: 7/8 and 5/4, tempo games, swerves, microtones, accordion, chip, toys |
| Kill frenzy | not tracks | `MusicFrenzy*.cs`, `MusicRenderer.Frenzy.cs`, `.Tempo.cs`, `.FrenzyClass.cs`, `FrenzyStyles.cs`, `FrenzyStyleRig.cs`, `Weapons/Frenzy/classes.json` | A hardbass track's drops, looped, built up by level, restyled per weapon class |

The hardbass sound: four-on-the-floor kick, bass only on the off-beats, a sidechain that pumps the bass harder than the
leads, shouted HEY/OI/HA/YAH, everything driven into a clipper and limiter. `MusicStyle.Hardbass` is one style; harder
and weird are add-ons to it, so every hardbass track shares one renderer path.

## How a track is defined

A track is one constructor call. It holds no notes: `MusicRenderer.Load` writes the melodies from `Seed` when the
track starts. This real one (`MusicTracks.cs`) is the shortest full example:

```csharp
new("Tracksuit Tactical", MusicSet.Menu, MusicStyle.Hardbass, 156, 35, "B minor", Minor,
    [0, 5, 2, 6], 0x7AC7_000A, "I8 B8 D16 K4 B8 D16 D8 O4",
    new(HardbassLead.Supersaw, HardbassLead.Supersaw, "..x...x...x...o.", "..x...o...x..xo.",
        "............................h...", ReverseBassUse.Never)),
```

| Value | Meaning |
| --- | --- |
| `"Tracksuit Tactical"` | Name; unique. Shown as "Now playing" |
| `MusicSet.Menu` | Which playlist (`Menu` or `Lobby`) |
| `MusicStyle.Hardbass` | The recipe in `ConfigureStyle`: kit, grooves, gains, sidechain |
| `156` | BPM (a float). Bar length is `4 * 60 / Bpm`, 1.54 s here |
| `35` | `Root`: MIDI note of the key in the bass octave (35 = B1). Shipped roots run 34 to 45. Leads play 24 semitones up, pads 12 |
| `"B minor"` | Display name only; nothing reads it to make sound. `Scale` sets the notes |
| `Minor` | Seven semitone offsets above the root. Must have 7 entries |
| `[0, 5, 2, 6]` | `Chords`: one scale degree per bar, 0-based, looping, restarting at each section. Here i, VI, III, VII. Use 4 or 8 entries: the lead phrase is 8 bars and is written over this loop, so other lengths drift off the phrase (inferred) |
| `0x7AC7_000A` | `Seed`: picks the lead rhythm, contour and bass accents. Give each track its own |
| `"I8 B8 ..."` | `Form`: letter + bars. Tokens are space-separated; unknown ones are skipped silently |
| The record | `HardbassFlavor`, see below. Only hardbass tracks take one; a hardbass track without one gets `DefaultHardbass` |

Length: `(TotalBars + 1 tail bar) * bar length`. This form is 72 bars: 73 x 1.538 = 112.3 s. Weird tracks get theirs
from their `WeirdTimeline`.

### Sections

| Token | Part | What the sequencer does (`OnStep`) |
| --- | --- | --- |
| `I` | Intro | Muffled kick, hats from bar 3; snare, open hat and bass in the second half; pads. Hardbass keeps the kick down (0.4 level, muffled) for the first 75%, with one chant on beat 4 of the last bar |
| `B` | Build | Groove, then four-on-the-floor; a snare roll doubling up; riser; the lead's filter opens; the last 12.5% is a gap, with two chants into a hardbass drop |
| `D` | Drop | Everything. Crash and sub hit at the start, a smaller crash every 8 bars, a 70% chance of a kick-snare fill in bar 8 of each 8 |
| `K` | Break | No main beat: pads; arp or filtered lead; hat pulse in the second half; `HalfTimeBreak` adds kick on 1, clap on 3 |
| `O` | Outro | Drums thin out over the section; the last bar is one kick and a crash |

Two neighbouring drops (`D16 D8`) count as two: `_drops` rises, so `LaterLead` and `LaterDrops` reverse bass arrive
without a break. A section before a drop gets a silent gap at its end (`_gapAfter`). Groove A or B alternates every 8
bars of a section. Lead phrase A or B alternates per drop and every 16 bars within one.

### Hardbass notation

| Field | Length | Characters |
| --- | --- | --- |
| `BassA`, `BassB` | 16 steps | `.` rest, `x` chord root, `o` root an octave up, `f` chord degree + 4 ("fifth") |
| `Chants` | 32 steps, two bars | `.` none, `h` HEY, `o` OI, `a` HA, `y` YAH |

`BassA` must not play on a kick (steps 0, 4, 8, 12); steps 2, 6, 10, 14 get the accent. Other fields: `Lead` and
`LaterLead` (`Supersaw`, `Hoover`, `Screech`, `Rave`; "later" means from the second drop), `Reverse` (`Never`,
`LaterDrops`, `Alternate` every other 4 bars), `KickClip` (shipped 1.3-2.6), `Bounce` (semitones the bass drops onto
each note, shipped 2-8), `HalfTimeBreak`.

### Harder add-on

`Harder: new("x...x...x...x.xx", Master: 1.2f)`: `KickB` is the 16-step kick of every other 8 bars of a drop; it must
start on step 0 and have 4 or more kicks. Defaults: `KickDrive` 11, `KickTop` 0.5, `LeadSaws` 9, `LeadDetune` 1.5,
`BassDrive` 9, `Siren` 0.3, `SirenSemis` 5, `Riser` 0.28, `Crowd` 0.6, `Master` 1.3. Harder mixes are denser, so
`Master` is lower than classic's 1.6.

### Weird add-on

An object initialiser after the constructor, from "Lullaby for a Tracksuit":

```csharp
{
    Weird = new("...m57.r.", Lead: WeirdLead.Chip, LeadUse: WeirdUse.LaterDrops, ChipArp: true,
                Sfx: "....q.......s.......q.......s..b", ExtraBeatSfx: 'q', WhistleIntoDrops: true, ChantSemis: 7f, ChantCrush: 0.4f),
},
```

`Sections` has one code per form section; missing or unknown codes mean `.`.

| Code | Effect |
| --- | --- |
| `.` | Plain 4/4 |
| `7` | 7/8: 14 sixteenths |
| `5` | 5/4: 20 sixteenths; the bar's last beat is played again, and `ExtraBeatSfx` can mark it |
| `u` | Speeds up from 0.8x to the track tempo |
| `r` | Accelerando from the tempo to 1.12x |
| `d` | Break only: drunk half time, down to 0.55x with wow and sagging pitch, then back up |
| `l` | Break only: lo-fi at 0.6x, swung, crackle and bitcrush |
| `m` | Break only: music box that winds down |

`Sfx` is exactly 32 steps, played every other 4 bars of drops: `q` quack, `s` squeak, `w` whistle up, `v` whistle down,
`b` boing. `Lead` is `Accordion`, `Chip` or `MusicBox`; `LeadUse` is `Always`, `Drops`, `LaterDrops` or `Alternate`.
`ChantSemis`/`ChantSemisAlt` pitch the chants (the second every other 4 bars); `ChantCrush` is 0-1. `Micro` is 7 semitone
offsets per scale degree (quarter tones: -0.5); `HarmonyCents` detunes the harmony on purpose.

Nothing enforces "break only": `d`, `l` or `m` on another section changes the tempo but plays the normal part.

## How to add a track

1. **Pick the file.** `MusicTracks.All` for classic or older genres, `MusicTracksHarder.Tracks`,
   `MusicTracksWeird.Tracks`. In the last two, spell scales out (`[0, 2, 3, 5, 7, 8, 10]`): reading `MusicTracks.Minor`
   there makes the static initialisers depend on each other (the comment at the top of each file says so).
2. **Add the constructor call.** That is the only code a track needs. `MusicTracks.For` picks it up by `Set` and
   `Style`, the playlists follow, and "Hardbass only" follows `Style`. There is no list to register in.
3. **Know what follows by itself.** A hardbass track with steady drops joins the frenzy pools too (`FrenzyLayer`), so
   its drop loops during matches and goes through every weapon class's styling. Pools sort by family and BPM: `Fast` is
   160+, `Slow` is 158 or less.
4. **Stay inside the limits** below. CI runs "Music" on every push, so a track outside them turns `main` red.
5. **Measure it** with the probe. You can build the `new MusicTrack(...)` inside the probe itself and iterate without
   rebuilding the game.
6. **Add a row to the table in README "Music"**, and ask the user to listen.

The "Music" section's rules for a new track:

| Applies to | Rule |
| --- | --- |
| Every track | Unique name; unique (`Bpm`, `Key`) pair; 7-note scale; a Build and a Drop in the form; 60-300 s |
| Older genres | 135-180 BPM |
| Every hardbass track | BPM and `Key` text unique among hardbass; (`Lead`, `LaterLead`, `BassA`, `Reverse`) unique; 16-step bass; 32-step chants with at least one chant; `BassA` off the kicks |
| Classic tempo | 145-165, or the one 195-205 track |
| Harder | 160-175 or 210-225 (exactly one 210+); 90-180 s; `KickB` rule above; `LeadSaws` 1-9 |

The rule of unique hardbass BPM is probably why the weird tracks use half BPMs like 151.5 (inferred). A second 200 BPM
track fails the check; so does a second 210+.

## Voices

All are in `MusicSynth.cs` or `MusicWeirdVoices.cs`, at 44.1 kHz, rendered a block at a time. `MusicRenderer` owns one of
each. Values below are the shipped range, not hard limits unless noted. Per-style values are in `ConfigureStyle`,
`ApplyLead` and `ApplyHarder`.

| Voice | What it is | Main parameters (shipped range) |
| --- | --- | --- |
| `KickVoice` | Pitch-swept sine, held, decayed, driven | `StartHz` 160-465, `EndHz` 41-78 (tuned to the key) or 48-62, `Drive` 1.8-14, `Clip` 1.3-2.6, `Punch`, `Click`, `Top` (layered kick). Also makes the sidechain signal |
| `SnareVoice` | Band-passed noise plus a tone; `Clap` adds 3 slaps 10 ms apart | `ToneHz` 180-225, `NoiseHz` 1300-2400, `NoiseTime` 0.1-0.2, `PitchMul` for rolls |
| `HatVoice` | High-passed noise; closed chokes open | `ClosedTime` 0.03, `OpenTime` 0.18 |
| `BassVoice` | Mono: 2 detuned saws, sine sub, resonant low-pass, drive | `Cutoff` 260-650, `Drive` 2.5-9, `Sub`, `Glide`, `Bounce`, `Wobble` |
| `SawSynth` | 4 notes of 1-9 saws or squares; lead, pad, arp | `Saws` (max 9), `HarmonySaws`, `Detune`, ADSR, `Cutoff`, `Resonance`, `Drive`, `HalfRate`, `BendSemis`, `Vibrato`, `FilterLfo`, `BandPass` |
| `FxVoices` | Crash, noise riser, sub drop | `Crash(vel)`, `SubDrop(vel)`, `RiserLevel`, `RiserHz` |
| `ReverseBassVoice` | Driven sine swelling between kicks, cut before the next | Triggered on step `s % 4 == 1` |
| `VocalVoice` | 3 saws through 3 gliding formants: HEY, OI, HA, YAH | `Drive`, `Crowd`; base pitch is key root at MIDI 48-59 |
| `SweepVoice` | 2 saws: siren or pitch riser | `Level`, `Hz`, `LfoSemis`, `LfoHz` |
| `PingPongDelay`, `RoomReverb` | Dotted-8th echo; Freeverb-style room | Sends set per sample in `RenderSamples`. No `Feedback`/`Damp` setting per track |
| `AccordionVoice`, `ChipVoice`, `MusicBoxVoice`, `ToyFxVoice`, `TapeBus` | Weird voices and the master tape deck | `Wow`, `LowpassHz`, `Crush`, `Crackle` on the tape; 8 tines; one toy at a time |
| `MasterLimiter` | 2 ms look-ahead limiter, then a soft clipper | `Ceiling` 0.93 on a track, 0.96 on the music bus; never past `SafetyCeiling` 0.985 |

The four hardbass leads (`ApplyLead`), scale-degree harmonies in brackets:

| Lead | Recipe | Harmony |
| --- | --- | --- |
| `Supersaw` | 7 saws (9 when harder), detune 0.024, drive 2.2 | +2, -7 |
| `Hoover` | 5 saws, detune 0.04, 5-semitone bend, glide, vibrato | -7 |
| `Screech` | 3 saws through a band-pass at resonance 0.88, wah in 8ths | +7 |
| `Rave` | 3 squares, short envelope | +2, +4 |

What the code treats as going together (taste from earlier work, not proof):

- The bass sits between the kicks; kick and bass decays follow the tempo so neither reaches the other, at any BPM.
- Hardstyle and Hardcore kicks end on the key's root, so the kick tail is in tune with everything. Hardbass too.
- Classic and harder hardbass use minor, harmonic minor, phrygian or phrygian dominant. Every chord loop starts on degree 0.
- `DrumAndBass` and `Darksynth` have louder intro pads (`_introPad`); `Industrial` and `Darksynth` arps come in on variant B bars.
- Pads run at half rate because they are dark and filtered; bright sounds should not.

**Hoover gotcha.** The comment says it swoops onto every note, but with `Glide` above 0 `NoteOn` only applies the bend
to a note starting from silence; the others glide, and `BendTime` is unused.

## Render path

```
game thread: SetMood, Volume, Frenzy.Target/Class/Shot (Volatile writes, once a frame)
"Music" thread: StreamLoop -> Fill -> Mix -> MixBlock (1024-frame blocks)
  UpdateDirector -> decks (2 MusicRenderers) + FrenzyLayer (its own MusicRenderer) -> volume -> limiter -> short PCM -> OpenAL
```

- **Live, not pre-rendered.** `MusicRenderer.Render` synthesises in real time. The `Music` thread runs above normal
  priority, polls every 10 ms and refills `StreamBuffers` (4) of `StreamFrames` (2,048): 186 ms queued, which is the
  latency of fades, volume and frenzy changes. An underrun restarts the source.
- **Blocks.** `Render` cuts blocks at sixteenth boundaries, up to `MaxChunk` (1,024). Each voice renders a whole block in
  turn with its state in locals. Notes change only between blocks, so nothing lands inside a sixteenth.
- **Crossfades.** Two decks. `StartNext` crossfades over 3 s when a track has under 3 s left, on a mood change, or when
  Hardbass only comes on. From silence it fades in over 1.2 s; `Off` fades out over 1.5 s. Decks are never looped: a
  track plays once and hands over.
- **Playlists.** `MusicPlaylist`: every track once per round, reshuffled, never the same track twice running.
- **Frenzy loops.** `LoopDrops` jumps from a drop's end back to its start on the bar line, with voices and reverb still
  ringing. `EnterBar` restarts the section, so each loop hits a crash and sub drop. Tempo changes alter the length of a
  sixteenth, not the pitch (`MusicRenderer.Tempo.cs`); kick, bass, echo and screech wah follow.
- **Seeds.** `MusicTrack.Seed` fixes the melodies, so a track keeps its identity. The play seed passed to `Load` varies
  only fills, ghost hats and noise. Output is deterministic for (track, seed, start bar), and a reused renderer matches
  a new one. In the game `MusicPlayer` seeds from `Environment.TickCount`, so order and fills differ per launch.

### Frenzy levels

`FrenzyLayer` plays a track at `FirstDropBar`, or at `LastDropBar` from level 4. Changes land on bar lines: up at once
with half a bar of crossfade, down one level every two bars or longer. Dying, or the setting off, fades out over 2 s.

| Level | Gain | Low-pass | Leads, pads, chants | Gunfire duck | Tempo |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.5 | 380 Hz | off, not rendered | 0.3 | track |
| 2 | 0.72 | 9 kHz | off | 0.3 | track |
| 3 | 0.86 | open | on | 0.3 | track |
| 4 | 1 | open | on, jumps to the last drop | 0.3 | track |
| 5 | 1.33 | open | on | 0.15 | class `level5` |
| 6 | about 7.4 | open | on, crash and roar on arrival | 0 | class `level6` |

Tempo boosts shrink above 160 BPM, to 40% of the boost from 208 BPM (`BoostShare`). `BusLevel` 0.75 and the frenzy
volume multiply all of this.

Class styles are data, in the `"music"` section of each class in `Weapons/Frenzy/classes.json`:

| Class | Style | Pool | Tempo 5 / 6 | Trim |
| --- | --- | --- | --- | --- |
| Pistol | Western outlaw | classic | 1.10 / 1.20 | 0.96 |
| SMG | Chiptune breakcore | fast | 1.12 / 1.25 | 1.10 |
| Assault Rifle | Classic hardbass (the track as it is) | any | 1.10 / 1.20 | none |
| DMR | Marching drums and brass | any | 1.08 / 1.16 | 0.97 |
| Sniper | Cinematic braam | slow | 1.08 / 1.16 | 0.93 |
| Shotgun | Gabber stomp | harder | 1.10 / 1.22 | 0.72 |
| LMG | Industrial grind | any | 1.08 / 1.18 | 0.80 |
| Special | Glitch weird | weird | 1.12 / 1.24 | 1.04 |
| Launcher | Artillery barrage | harder | 1.08 / 1.18 | 0.80 |

Layer patterns: `.` rest or note off, `-` hold, `x` hit or root, `X` accent, `g` ghost, `r` two hits in the sixteenth,
`0`-`9` scale degrees above the bar's chord, `z` a random degree. At most 6 layers, 4 chord notes, 256 pattern
characters. Voices are the `FrenzyVoice` enum; each layer has `octave`, `level`, `from` (the frenzy level it joins at),
`reverb`, `chord`. Fx: `drive`, `crushBits`, `crushHold`, `stutter` (`.sS`), `slapMs`, `slapFeedback`, `slapMix`.
`stems.lead` is never 0: 0 means "skip the leads". A bad section is logged as `[frenzy] music:` and that class falls
back to plain. `trim` keeps level 6 loud enough, and each class had its value measured from CI (ca252a13); changing a
class's layers or fx means re-measuring it.

The weird pool has one track: only Drunk Duck Breakdown has steady drops (`WeirdTimeline.SteadyDrops`), because the
frenzy loops drops on bars of a fixed length.

## Mixing

The chain, in order:

1. Each voice into its instrument gain (`_gKick`, `_gBass`...), then lead, arp, pad, snare, hat and vox into the delay and
   reverb sends (`RenderSamples`).
2. Sidechain: `_duckDepth` on synths, plus for hardbass `_bassDuck` 0.85 on the bass. The kick triggers it and it falls
   back over 0.45 of a beat.
3. Sum, DC blocker, then `Tanh(x * 0.9)` for older styles, or `Tanh(x * _masterGain) * 1.05` then `MasterLimiter` for
   hardbass (weird voices and the tape deck go in just before the limiter).
4. Deck gain (fades) times `MenuTrim` or `LobbyTrim`; the frenzy layer adds in.
5. Music volume **squared**, so the default 0.50 is 0.25 linear.
6. The music-bus limiter, only while the frenzy plays or a trim boosts above 0 dB. So at default settings menu and
   lobby output is bit-for-bit what it was without the limiter.
7. Hard clamp to 16-bit, then OpenAL, where the listener gain (master volume) applies.

| Gain | Default | Hardbass |
| --- | --- | --- |
| kick / snare / hat | 0.85 / 0.45 / 0.16 | 0.72 / 0.5 / 0.14 |
| bass / pad / arp | 0.4 / 0.16 / 0.12 | 0.55 / 0.12 / 0.12 |
| lead | 0.3 | 0.3-0.36 by lead sound |
| reverse bass / vox | none | 0.5 / 0.45 |
| master | 0.9 | 1.6 (harder 1.2-1.3) |
| duck depth | 0.3-0.65 by style | 0.55, bass 0.85 |

Hardbass gains were last staged at 4d3c496a: `_gKick` 0.95 to 0.72, `_gBass` 0.45 to 0.55, leads up, master 1.5 to 1.6.

**F8 mixer** (`AudioMix*.cs`, `Audio.Mixer.cs`, `UiAudioMixer.cs`; also Settings > Audio > Open audio mixer). Rows
`music.menu`, `music.lobby`, `frenzy.1` to `frenzy.6`, and `music.limiter_ceiling`. All are at 0 dB or factory in the
baked `SeabMix2` mix, -50 to +50 dB. "Preview frenzy" plays a level anywhere. It meters the music (measured, not
estimated) and shows the limiter's deepest gain reduction. `AudioMixDefaults` is the one table of defaults; keys are JSON
names and must never be renamed. A new `MixBus` goes anywhere before `COUNT`, and needs a row at the same position in
`Entries`.

### Loudness targets

These are what the self-tests hold (`SelfTestMusic.cs`, `SelfTestHardbassHarder.cs`, `SelfTestFrenzy.cs`). There is no
LUFS target; everything is RMS on the float output, stereo samples together.

| What | Target |
| --- | --- |
| Any track | No NaN; peak at most 1.0; under 0.5% of samples above 0.99; drop RMS 0.08-0.6; intro RMS at least 0.01 and under the drop; drop at least 1.2x the break |
| Hardbass | Peak at most 0.985; no sample above 0.99; drop RMS 0.3-0.56; drop at least 1.25x intro; limiter min gain 0.25-0.97 (it must work, but not hard) |
| Hardbass groove | Kick under 0.1 of its peak at the next 8th; bass under 0.1 at the next kick |
| Harder | Drop within 0.8-1.25x the classic hardbass average; kick heavier (1.05x) |
| Distinct tracks | Drop-to-drop correlation under 0.5 for every pair |
| Frenzy pool | Median drop within 10% of `DropRms` 0.50 (the code comment gives 0.47-0.54) |
| Level 6 | At default sliders, 1.05-1.6x the measured gunfire-and-explosion RMS (`SfxReferenceRms` 0.46) before the limiter; level 4 stays under it |

Measured for reference: hardbass drops 0.44-0.53, peak 0.97-0.985, limiter down to 0.74-0.90; older tracks' drops
0.34-0.56 with peak 0.91-0.95. The two lowest drops, Polka 0.45 and Lullaby 0.46, are the weird tracks with a weird lead.

**Avoiding clipping.** On a hardbass track, turn `Master` down first if the limiter dips under about 0.7 or the drop goes
above 0.56. Don't lift a single gain to fix a weak drop: the clipper eats it. Level 6 depends on `DropRms`, so a batch
of louder tracks shifts frenzy loudness too.

## Limits

**CPU.** "Music CPU must stay at most 1.5x the baseline" is not one number. The checks:

| Candidate | Baseline | Limit | Check |
| --- | --- | --- | --- |
| Any whole track, and all tracks on average | absolute | 75 ms per second of audio | "Music" |
| A harder track's drop | Heaviest drop among tracks neither harder nor weird | 1.5x | "Music" |
| Frenzy level 4 and 6, classic | Heaviest hardbass drop | 1.5x, level 6 at most 1.25x level 4, level 1 at most level 4 | "Frenzy music" |
| Each class, level 4 and 6 | Heaviest hardbass drop | 1.5x | "Frenzy classes" |
| Boosted music | The same music at 0 dB | 1.5x | "Audio mixer" |

Ratios come from `CompareCpu` (`SelfTestCpuRatio.cs`): both sides on this thread's CPU clock, interleaved, after a
warm-up, each side's quickest run, 1.5 s from `FirstDropBar`. Don't use a median of ratios: Windows runners gave 1.51x,
1.38x and 1.07x for one commit that way. Measured whole-track cost: 6-16 ms/s; the heaviest are the weird
tracks and Babushka Bunker Buster. About 1% of a core on a PC (README).

Two gaps: the check that held weird tracks to 1.5x went with the "Weird hardbass" section on 2026-11-03, so only the
75 ms/s cap covers them; and every frenzy ratio is against the heaviest track, so a heavier track loosens those checks
(inferred). Compare by hand. During a crossfade two decks render, and the frenzy adds a third renderer if a deck is
still fading out.

**Allocation.** Zero, in `Load` as well as `Render`: `Load` runs on the music thread. No LINQ, `new`, closures, boxing
or `string` work there (`Enum.IsDefined` boxes; the `Class` setter avoids it). Buffers are made in constructors; arrays
are sized for the worst case. "Music", "Frenzy classes" and "Audio mixer" measure it.

**Denormals.** Filter and feedback state is zeroed below 1e-15; float maths on denormals is very slow. Every new
recursive filter needs the same.

**Reset.** A voice resets all its state in `Reset()`, so one renderer can play track after track. Adding a field to a voice
means adding it to `Reset()`.

**Tests only when asked (user, 2026-11-03).** Add no self-tests, and don't run the suite as a matter of course. A build must
pass, you try the change by hand in a throwaway run (the probe), and you report what you didn't verify. The sections
above exist and CI runs them, so you still need to meet them. "Weird hardbass" and "Frenzy tempo" were deleted on
2026-11-03: nothing automated checks weird timelines or frenzy tempo now. If the user asks, the command is
`--selftest-only "Music, Frenzy music, Frenzy classes, Audio mixer"`.

## Inspecting a track without ears

No CLI renders music to a file. `MusicRenderer`, `MusicTrack` and `MusicTracks` are public, so a throwaway project
outside the repo can drive them with no audio device, and without touching saves. Keep it out of the repo. It has no `PackageReference`, so it downloads nothing. Any built
`CallOfSlop.dll` works, so you don't need a new game build until you edit voices. (Also useful: in a real run each new
track logs `[music] now playing ...` with its mixing cost, and the F9 window shows `music thread N ms/s`.)

`musicprobe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="CallOfSlop"><HintPath>$(GameDll)</HintPath></Reference>
  </ItemGroup>
</Project>
```

`Program.cs`:

```csharp
using CallOfSlop;

const int Sr = 44100, Block = 1024;
if (args.Length == 0)
{
    foreach (var x in MusicTracks.All)
        Report(x, 1234, null);
    return;
}
// To try a new track without rebuilding the game, replace this line with: var t = new MusicTrack(...);
var t = MusicTracks.All.First(x => x.Name == args[0]);
Report(t, 1234, args.Length > 1 ? args[1] : null);

static void Report(MusicTrack t, uint seed, string? wav)
{
    var r = new MusicRenderer();
    r.Load(t, seed);
    var pcm = new List<float>();
    var buf = new float[Block * 2];
    long ticks = 0;
    while (!r.Finished && pcm.Count < (t.Seconds + 10) * Sr * 2)
    {
        var a = System.Diagnostics.Stopwatch.GetTimestamp();
        r.Render(buf);
        ticks += System.Diagnostics.Stopwatch.GetTimestamp() - a;
        pcm.AddRange(buf);
    }
    var s = pcm.ToArray();
    var secs = s.Length / 2.0 / Sr;
    Console.WriteLine($"{t.Name}: {t.Bpm} BPM, {secs:0.0} s (Seconds {t.Seconds:0.0}), {ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / secs:0.0} ms/s, "
        + $"peak {s.Max(MathF.Abs):0.000}, above 0.99: {s.Count(x => MathF.Abs(x) > 0.99f)}, NaN: {s.Count(x => !float.IsFinite(x))}, limiter min gain {r.LimiterMinGain:0.00}");
    var bar = 0;
    foreach (var (part, bars) in t.Sections)
    {
        Console.WriteLine($"  {part,-6} bars {bar,3}+{bars,-3} rms {Rms(s, At(t, bar), At(t, bar + bars)):0.000}");
        bar += bars;
    }
    Console.WriteLine($"  tail rms {Rms(s, At(t, t.TotalBars), s.Length):0.0000}, last 1024 frames {Rms(s, s.Length - Block * 2, s.Length):0.0000}");
    Console.WriteLine("  drop spectrum, dB:" + Bands(s, At(t, t.FirstDropBar), Sr * 6));
    Seam(t);
    if (wav != null)
        Wav(wav, s);
}

static int At(MusicTrack t, int bar) => (int)(t.BarStart(bar) * Sr) * 2;

static double Rms(float[] s, int a, int b)
{
    double e = 0;
    b = Math.Min(b, s.Length);
    for (var i = a; i < b; i++)
        e += s[i] * (double)s[i];
    return Math.Sqrt(e / Math.Max(1, b - a));
}

// Crude bands: each is the difference of two one-pole low-passes, in dB against full scale. Good enough to compare tracks.
static string Bands(float[] s, int from, int samples)
{
    float[] edges = [60, 120, 250, 500, 2000, 6000, 16000];
    var c = edges.Select(hz => 1f - MathF.Exp(-2 * MathF.PI * hz / Sr)).ToArray();
    var lp = new float[edges.Length];
    var e = new double[edges.Length + 1];
    for (var i = from; i + 1 < from + samples && i + 1 < s.Length; i += 2)
    {
        var x = 0.5f * (s[i] + s[i + 1]);
        var low = 0f;
        for (var b = 0; b < edges.Length; b++)
        {
            lp[b] += c[b] * (x - lp[b]);
            e[b] += (lp[b] - low) * (double)(lp[b] - low);
            low = lp[b];
        }
        e[^1] += (x - low) * (double)(x - low);
    }
    return string.Concat(e.Select((v, b) => $" {(b == 0 ? 0 : edges[b - 1]):0}+ {10 * Math.Log10(v / (samples / 2) + 1e-12):0}"));
}

// Tracks don't loop; the frenzy's drops do. Compares the bar before the loop point with the bar after it.
static void Seam(MusicTrack t)
{
    if (t.Style != MusicStyle.Hardbass || t.Timeline is { SteadyDrops: false })
        return; // the frenzy never plays these
    var r = new MusicRenderer();
    r.Load(t, 1, MusicRenderer.LastDropBar(t));
    r.LoopDrops = true;
    var bars = t.Sections.Reverse().First(x => x.Part == MusicPart.Drop).Bars;
    var s = new float[(int)(r.FramesPerBar * (bars + 1)) * 2];
    for (var i = 0; i < s.Length; i += Block * 2)
        r.Render(s.AsSpan(i, Math.Min(Block * 2, s.Length - i)));
    int seam = (int)(r.FramesPerBar * bars) * 2, bar = (int)r.FramesPerBar * 2;
    Console.WriteLine($"  loop seam: rms {Rms(s, seam - bar, seam):0.000} before, {Rms(s, seam, seam + bar):0.000} after");
}

static void Wav(string path, float[] s)
{
    using var f = new BinaryWriter(File.Create(path));
    f.Write("RIFF"u8); f.Write(36 + s.Length * 2); f.Write("WAVEfmt "u8); f.Write(16); f.Write((short)1); f.Write((short)2);
    f.Write(Sr); f.Write(Sr * 4); f.Write((short)4); f.Write((short)16); f.Write("data"u8); f.Write(s.Length * 2);
    foreach (var x in s)
        f.Write((short)(Math.Clamp(x, -1f, 1f) * 32767f));
}
```

Run it (the `GameDll` path is any built game):

```
. /home/claude/dotnet-env.sh
dotnet build -c Release -p:GameDll=/path/to/bin/Release/net10.0/CallOfSlop.dll
dotnet bin/Release/net10.0/musicprobe.dll                                  # every track
dotnet bin/Release/net10.0/musicprobe.dll "Tracksuit Tactical" out.wav     # one track, and a 16-bit WAV for the user
```

Reading it: run all tracks and compare your track against neighbours of the same family, in the same run (the ms/s
figure is one wall-clock pass, and a lone run reads high while the JIT warms up, so only comparisons mean anything). Check the targets above. A section's RMS shows the
arrangement: intro 0.3, build 0.46, drop 0.5, break 0.2, outro 0.4 is a normal hardbass shape. Compare spectrum
lines against a track that works; a large gap in one band is the thing to look at. Seam RMS should stay within about
10% (measured: 0.46-0.53 either side). This probe doesn't check drop-to-drop correlation or the frenzy. Send the
user a WAV only if they ask.

## Pipe tunes

The Clan MacSlop's bagpipes (`MusicPipes.cs`, added 2026-11-06). Not a `MusicRenderer` style: a `PipeTune` is written out note by note, as
pipers write tunes, and `PipeRenderer` plays it. `MusicTrack.Pipes` holds the tune, and `MusicRenderer.Load` hands such a track to its own
`PipeRenderer` (`_pipeOn`), so the media voice, the menu's music player and a joiner's seek work unchanged. The tracks are
`PipeTunes.Tracks`, **never in `MusicTracks.All`**: no menu or lobby playlist, no frenzy pool and no "Music" rule sees them (they are not
hardbass and have no build or drop). `MediaEntry.Track` and `MusicCatalogue` look in both lists.

| Voice | What it is |
| --- | --- |
| Drones | Two tenors an octave under low A (0.04% apart, so they beat) and a bass two octaves under: squares with some saw, darkened. They strike in rising from 0.82x pitch with a 17 Hz growl, and once the bag is empty they sag 30% and fade over 0.9 s |
| Chanter | Low G to high A just against the drones (7/8, 1, 9/8, 5/4, 4/3, 3/2, 5/3, 7/4, 2 over low A at 477 Hz), a saw and a 30% pulse through a 300 Hz high-pass and a peak near 1.7 kHz. It never stops: notes change in a 1.5 ms glide, and grace notes (24 ms) separate them |
| Snare, bass drum | A pipe band's: flams, taps and buzz rolls by the metre's patterns, the bass drum on every beat. The first time through only the bass drum plays, on the bar and softly, with the roll into the second time. A slow air has none |

**Notation** (`PipeTune` constructor): the chanter's letters `G A B c d e f g a`, a digit after a note for its length in units (the tune's
`UnitsPerBeat` to a beat), `|` for bar lines (each bar must add up, or the tune throws on load). `^` before a note is a doubling, `~` a birl on
low A, `!` no grace. Every other note on a beat, and every repeated note, gets a G grace note by itself. A tune is: two bars of drones and the
long E, the form (`"AABB"` by default) played `times` times (2), low A held for a bar, then the drones die in the tail bar. Its `MusicTrack`'s
bars are the tune's bars (its "BPM" is 240 / bar seconds), so `BarAt` and `Seconds` are right for the media clock.

**Measured** (the probe above, which works on pipe tracks): 6-8 ms per second of audio (Tracksuit Tactical: 10), peak 0.74-0.85, RMS about
0.3 (a hardbass track is 0.45), no NaN, nothing above 0.99, no allocation in `Load` or `Render`. Nobody here has heard them: whether they sound
like pipes is the user's call.

## Giving Sandbox music

Not built, and whether to is a user decision. The code shows what would have to change (the design around it is inferred):

- A new `MusicMood`, `MusicSet`, playlists in the `MusicPlayer` constructor, and `MoodFor`, which needs the mode.
  `UpdateDirector` and `StartNext` assume two sets (`mood == Menu ? Menu : Lobby`).
- `MixBlock` picks the trim with `deck.Set == Menu ? MenuTrim : LobbyTrim`, and `MeterDeck` has two sets. A third set needs
  a trim, a meter and a `MixBus` row (`music.sandbox`, say).
- `Frenzy.MixInto` is allowed only in mood `Off`. Decide whether frenzy plays over Sandbox music, or at all there.
- The "Music" section asserts `MoodFor` and the mood changes, and "Audio mixer" asserts the defaults table.
- Every track is 140+ BPM with hard drops. Background music for hours of building likely needs a calmer, sparser style:
  a new `MusicStyle` case in `ConfigureStyle` and `LeadRhythms`. Beware: `ConfigureStyle`'s `default:` is Darksynth, and
  `LeadRhythms` falls back to it too, so a new enum value with no case silently sounds like Darksynth.
- A track ends and crossfades in 1.5-3 minutes. A world that plays for hours would mean more tracks or silence between them.

## Rules and gotchas

- **No music pins.** No self-test holds a hash of the audio, so a change breaks no digest; the seeded match digests in
  `SelfTestModeModules.cs` have nothing to do with music. So nothing tells you that you changed other tracks' sound.
  Check by comparing probe output before and after.
- **Keep shipped tracks as they are** unless asked. Comments promise "exactly as before" for classic tracks with no
  harder or weird recipe: hooks are `if (_harder != null)`, `if (_wd)`. Follow that pattern for new features.
- **Don't reorder the melody RNG.** `Load` draws from `new MusicRng(track.Seed)` in a fixed order. Adding a rhythm to a
  `*Rhythms` array, or another draw, rewrites the melody of every track using it. Rhythms are exactly 32 characters.
  Changing a `Seed` rewrites that one track.
- **Fix a problem in the shared voice only if every track should change.** Otherwise add a field, default it to off.
- **New per-tempo sound** must follow `RetimeVoices`, or it drifts when the frenzy speeds up. `FramesToNextBar` assumes 16
  sixteenths, which is why the frenzy needs steady drops.
- **Set a voice's fields, then `Configure()`.** Coefficients are computed there, and `ApplyLead` mid-track must be
  followed by `_lead.Configure()`. `ResetHarder` and `ApplyLead` clear what a previous track set.
- **A weird swerve restores what it changed** on leaving (`WeirdTiming`). `DrunkStep` changes lead vibrato; `LofiStep`
  changes pad cutoff.
- **Threads.** `MusicPlayer` and `FrenzyLayer` setters use `Volatile`. Don't call into a renderer from the game thread.
- **Open:** menu and lobby music can click once as a trim crosses 0 dB and the limiter switches on.

### What went wrong before

| Commit | Problem | Lesson |
| --- | --- | --- |
| ce00ffab | Tape deck read position rounded up to `Len` and indexed past the buffer | Bound any fractional read index |
| ce00ffab | Music box too loud: gain cut from 0.5 to 0.16 ("a soft interlude, not a wall") | Balance like this needs an ear; ask the user |
| 4d3c496a | Intro kick too loud: a driven kick is loud at any velocity | Muffling needs a level cut too (`_kickLevel`) |
| 20f85f3c | Harder tracks too loud at `Master` 1.5 | Denser mix, lower `Master` |
| 3c551155 | Reverb reset used LINQ and allocated on the music thread; voices kept state between tracks | Plain loops in `Load`; reset everything |
| 3099635e, 14359391 | Frenzy classes over the CPU rule; Launcher's braam went to a power chord and synths to half rate | Thin a sound rather than raise the limit |
| ca252a13 | Level 6 per class needed trims from real measurements, not guesses (0.72-1.1) | Re-measure after changing a style |
| 2587dd13 | Mixer trims were capped at 4x, and boosts past that needed a limiter, not a hard clip | Boosted music runs the limiter |
| b0c57df2 and others | `SfxReferenceRms` went 0.55, 0.48, 0.44, 0.46 as gunshots changed | Gun or explosion changes can move level 6's calibration |

### Weird hardbass history

Added 2026-10-21 on `feature/hardbass-weird` (7360bfde), then merged with the harder tracks and the first frenzy
layer. Its own section checked the odd meters, swerves, toys and cost; that section was deleted 2026-11-03 with 129 others.
Because the frenzy loops drops of fixed length, four of the five can't play there, and the class Special ("Glitch
weird") has only Drunk Duck Breakdown to pick.

## Ask the user

An agent can't judge these. Say so in your report instead of claiming them.

- Does it sound like hardbass, and is it any good? Is the melody catchy or random?
- Is the new voice's tone right: chants like shouting, a kick that punches, a lead that isn't harsh in a bad way?
- Do the leads sit with the bass, or does the mix sound muddy, thin or fatiguing?
- Is the build's tension and the drop's hit satisfying?
- Is a weird swerve funny rather than broken? Out-of-tune parts are meant to sound wrong, and only an ear tells how wrong.
- Are level changes and crossfades smooth, with no clicks?
- Is the frenzy too loud against guns on real speakers?

## Checklist

1. Read the section for your kind of change; check the target file's style against neighbours.
2. For a track: pick an unused BPM and key (README table), 4 or 8 chords, a form with builds and drops, a new `Seed`.
3. Use hooks and off-by-default fields; existing tracks stay unchanged.
4. No allocation in `Load` or `Render`; denormal guards; new state in `Reset()`; tempo-bound sound in `RetimeVoices`.
5. `dotnet build -c Release --no-restore` passes.
6. Run the probe on all tracks: limits and loudness targets met, cost near its neighbours, seam fine. Compare before and after for tracks you didn't mean to change.
7. Hardbass with steady drops: remember it joins the frenzy pools.
8. Update the README "Music" table. Add no tests unless the user asks.
9. In your report, list what you measured and what you couldn't check, and ask the user to listen.
