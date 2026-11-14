# Sized gun and engine parts: phase 3 notes (2026-11-13)

Phase 3 of `sized-gun-engine-parts.md` ("Sized receivers"), built on branch `claude/sized-parts-3-be9e0e`. **Protocol 293**: two die
part files (Receiver Die, Medium Receiver Die) move ids; a pressed receiver's numbers are derived on each peer, no new bytes.

## What was built

- **The Receiver die** (design 4.1): blanks `solid` or `hollow`, steel, 15 to 120 cm across; bay 2 is 1 Gear Set plus 0.02 x mass (at most 20),
  as the Drive die. Three patterns, the hand-made receivers: `rifle` (Rifle Receiver, preset `solid 20x20x40`), `heavy` (Heavy Receiver,
  `solid 25x25x50`: its file is 26x26x50, off the 5 cm lattice) and `shell` (Shell Receiver, `hollow 20x20x40`). Die parts in the Small and
  Medium classes (what 15 to 120 cm reaches), with lathe rows as every die's.
- **`"aspect": "preset"`** on a die (`PressDie.AspectPreset`, `Pressed.Proportioned`): a blank keeps its pattern preset's shape, each side the
  preset's times across over the preset's across, rounded to the 5 cm lattice. Anything else is refused with the blank it wants:
  `a Rifle Receiver blank keeps its shape (20x20x40): 0.40 x 0.40 x 0.80 m`. The sweep makes its keys by the same rule.
- **`ConstructPartDef.GunScale`** (1 on files): a pressed receiver's across over its preset's. `BuildReceiver` calls
  `ConstructMerge.ScaleGun(ref g, w, s)` when it isn't 1 (`ReceiverMergeRules.Times` takes a real number now); the sound steps take
  round(s). The bore for the barrel band is s.
- **A pressed receiver never merges** (`ConstructMerge.Candidate` leaves out every `Pressed Part (` def). Four in a square stay four guns.
- **Catalogue gun parts on a pressed receiver are drawn round(s) times bigger** (`ConstructMerge.DrawnOn`), in `DeriveScales` and in
  `ScaleFor` (placement), as on a merge.
- **`--press-part`** prints a receiver's scale line (damage, fire rate, kick, rounds a shot, magazine by the merge's law).
- **Window tool `guns`** adds rounds a shot, the magazine's drawn scale, and every gun-part link's socket gap.
- Docs: weapons.md "Merged receivers", production.md "Sized dies".

## A phase 2 fix: a catalogue part on a pressed gun part

Phase 2 drew a muzzle device on a pressed barrel on a merge N times bigger (`DeriveScales`) but placed it at scale 1 (`ScaleFor` read the
barrel's own scale, 1), and `ScaleFor` gave a pressed barrel itself the merge's N at placement while `DeriveScales` gave it 1. Now:
`ScaleFor` gives a pressed gun part 1, and a catalogue part bolted onto a pressed gun part is placed and drawn at that part's bore, rounded
(`ConstructMerge.DrawnOnSized`; a 0.20 m barrel's device is 2x). `DeriveScales` seeds merges and pressed receivers first, then what hangs
off a pressed part, so a merge's own scale always wins and a splitter is never sized from its barrel. (The before-state was read from
the code, not measured.)

## What was tried, and seen

All in throwaway runs (`--save-dir` to a temp folder; test blueprints copied into `bin/` only and removed after).

- **Stop point**: `--window-shot ... --do guns` on two blueprints. A 2x2 Rifle Receiver merge with a Gun Barrel, and a pressed
  `receiver.rifle solid 40x40x80` with a pressed `barrel.plain round 20x20x100`: both `fires, reach 300 m, spread 0.9 deg, damage 104, every
  12 ticks, 4 rounds a shot, magazine drawn 2x`.
- One body, eight guns: a 15 cm Rifle Receiver with a Gun Barrel: damage 15 (x0.56 of 26), every 4 ticks (was 6); a 15 cm one with a pressed
  `20x20x100`: refused, `the barrel is 0.20 m across: this receiver takes 0.05 to 0.11 m`; a 15 cm one with a pressed `10x10x70`: reach
  360 m; a 40 cm one with a catalogue Gun Barrel: the merge's numbers, magazine drawn 2x; four 40 cm ones in a square: four guns.
- **Host and joined client** (two processes, `--net host:27931` / `join:127.0.0.1:27931`): the client printed all eight guns exactly as
  the host did.
- **Phase 2 item, muzzle device on a pressed barrel: closed.** A Gun Muzzle Brake on a pressed barrel on a plain Rifle Receiver, on a 2x2
  merge and on a pressed 40 cm receiver: every gun-part link's sockets 0 m apart; the brake drawn 1x on the 10 cm barrel, 2x on the 20 cm
  ones; spread 0.9 to 0.81 deg on both 2x guns.
- `--press-part presets`: 0 problems (the three receivers come off a Small Press on Auto). `--press-part sweep receiver`: 27 blanks,
  0 problems, slowest make 0.001 ms. `--gen-parts all --check`: clean. `--check-construct-parts`: exit 0.
- Self-test sections by name: Construct weapons, Construct gun recoil, Construct merged receivers, Powder charge, Sandbox weapon parts,
  Press stock: 0 failures.

## Not verified

- **Phase 2 item, a barrel pressed on a real press and fitted by hand: still open.** On a copy of the Production Showcase with a Barrel Die in
  the Press, `feed Press 1 Extrusion round 10x10x70 steel` was taken, but no barrel came out within 300 frames (`stock Press`: nothing, nothing
  loose). Not traced: likely the press waits on a start, power or its output line. Fitting by hand and firing weren't reached.
- A pressed receiver fired at a plate; the 26 generated receivers as patterns (not built: below); a pressed Heavy or Shell Receiver in a
  gun (only the presets and the sweep); `--gen-vehicles` (no generator or blueprint changed, and no shipped vehicle has a pressed part).
- A receiver at s = 1.5 is drawn 2x, at 2.5 drawn 3x (half up). No picture was looked at to judge how a 2x catalogue barrel sits on it.

## Choices made

1. **Mass is the blank's** (as the design says): a pressed 40x40x80 Rifle Receiver is 82 kg, against 56 kg for the four receivers of a 2x2 merge;
   a pressed 20x20x40 (made outside the preset) would be 20 kg against the file's 14.
2. **Shell Receiver's preset is the hollow blank**, so it doesn't share the Rifle's (phase 2's question 2 had the same shape).
3. **Only the three hand-made receivers are patterns.** The generated 26 need preset blanks from their generated bounds (`ConstructWeaponParts`)
   on the 5 cm lattice; that is the next step (design 4.1 calls them "data").

## Questions for the user (each with a recommendation; they can be asked together)

1. **A pressed receiver's mass.** It is the steel blank's: a 40 cm Rifle Receiver weighs 82 kg, the 2x2 merge it equals 56 kg.
   - A (recommended): keep the blank's mass. Pressing is the cheap door to a big gun, so it should be the heavy one.
   - B: the merge's mass rule (the file's mass times s squared: 56 kg), so the two doors build the same turret.
2. **Rounding of the drawn scale.** Catalogue parts on a pressed receiver at s = 1.5 are drawn 2x, at 1.25 drawn 1x.
   - A (recommended): keep round half up; a magazine twice the size on a 1.5 receiver reads well.
   - B: never draw a catalogue part bigger than the receiver's scale (floor), so it never overhangs a 1.5 receiver.
3. **The generated receivers.** 26 builder receivers (Courier Carbine, Thumper Chain Gun...) are not yet patterns.
   - A (recommended): make them patterns in the next phase, preset blank = their construct bounds to the 5 cm lattice.
   - B: leave them catalogue only; the Receiver die stays at three patterns.

Phase 2's three questions were answered (2026-11-13, all A: devices keep changing reach, shared blanks stay, no reach cap; recorded in
`sized-parts-phase2-notes.md`). None changes this phase's code.

## What is left of phase 3

- The 26 generated receivers as patterns (question 3).
- A pressed receiver fired at a plate in the window; the press-in-a-world run traced to a barrel coming out.

## The user's answers (2026-11-13)

1. **A pressed receiver's mass is the merge's** (B, not the recommendation): the catalogue file's mass times s squared, so the 40 cm Rifle
   Receiver weighs 56 kg, as the 2x2 merge it equals. Built in phase 4 (`Pressed`: `ReceiverMergeRules.Times(s, partMass)`; no bay 2 mass).
2. **The drawn scale stays round half up** (A).
3. **The 26 generated receivers stay catalogue only** (B, not the recommendation): "No, catalogue only". The Receiver die keeps its three
   patterns. (Phase 4 had built them as patterns before the answer came; that commit was reverted.)
