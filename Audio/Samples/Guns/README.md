# Recorded gun samples

Recorded gunshot layers for real firearms, loaded at start-up by `Source/Audio/GunSamples.cs` and baked into each
weapon's shot buffers alongside the synthesised sound. Everything here is optional: a missing or broken file, a set
with no shots left, or a missing `manifest.json` falls back to the synthesised gunshot (the log says so).

## Source and licence

**The Free Firearm Sound Library** ("Prepared SFX Library", freefirearmsfx.com, also on OpenGameArt as
"The Free Firearm Sound Library"), **CC0 1.0 Universal**: "Our team holds CC0 NO RIGHTS RESERVED for this library.
It may be used without royalty or credit ... for any application, personal or professional."
Recorded by Ben Jaszczak, Brian Nelson, Kevin Heras and Matthew Nanney (credited with thanks in `NOTICE.txt`).

## Files

- `<set>_shot<N>.wav`: a single shot recorded beside the shooter ("near distance"), 2-4 per set.
- `<set>_tail<N>.wav`: a shot recorded in front at mid distance, used for the distant layer.
- All 16-bit PCM mono, 44.1 kHz, 0.6-1.8 s, loudness-normalised (RMS of the first 200 ms) and soft-limited.
- `manifest.json`: sets (`refCaliber` = the weapon caliber the set plays unpitched at, `synthBlend` = how much of
  the synthesised shot is mixed in), rules (the first match by `class`, `sounds`, `minCaliber`/`maxCaliber`,
  `maxRpm` picks a weapon's set; Zap, Toy and Pneumatic guns match none) and samples (`file`, `set`, `role`
  `shot`/`tail`, `gain`, and the `source` file and shot in the library).

## Rebuilding

From the library's `Prepared SFX Library` folder (96 kHz / 24-bit stereo BWF):

    python3 -I tools/audio/make_gun_samples.py "<path>/Prepared SFX Library" Audio/Samples/Guns

The tool (stdlib only) finds each shot by onset detection, cuts it, high-passes at 30 Hz, mixes to mono,
resamples to 44.1 kHz, fades the tail, normalises loudness and writes the WAVs and the manifest. The picks
(which source file and which shot in it) are listed at the top of the script and in each manifest entry.
