# Ministry duties

Karl, 2026-11-10: the warden's induction (docs/sandbox/TUTORIAL.md) teaches the person; the machines are taught later, in
stages, while working for the Ministry of Slop, and the tutorial goes on with company-approved transport that takes the
inmates out to a mining area. Karl chose riding as passengers on a crew bus (2026-11-10) while the vehicle system is redone.

## The first duty: the quarry

Code `Source/Game/GameQuarry.cs`, words `Story/Duties/Quarry.json` (format `call-of-slop-duty/1`), panel `Source/UI/UiQuarry.cs`,
the ride's camera `Source/App/Program.Quarry.cs`.

- **The place.** A fresh story world (the opening's colony) also gets the **Ministry Quarry Works** (a blueprint: the Starter
  Smelter's machines, a Smelter fed from a Materials Bin and filling a second, and a Small Genset on a Fuel Tank with no
  cable), 180 to 320 m from the colony (`from`, `to`), on level ground. Its bus stop is 14 m from it on the colony's side; the
  colony's is 8 m out from the gate. A world saved before the quarry gets one when it loads (the opening section's last u16).
- **The mine face** (2026-11-11). Behind the works stands the Mine Face (two more slabs, a rock face with a footing): the
  induction now rides the same bus out and has the inmate prop and light it (docs/sandbox/TUTORIAL.md, "The mine shift").
  Someone within `QuarryAtWorks` (90 m) of the quarry's stop when the duty comes starts at its second step (`power`), with no
  memo and no bus out; the last step before `done` is `bunk` (lie in a Bunk back at the colony; the marker is a free one).
- **Who.** Once a person's induction is finished or skipped (and the opening is over, and they haven't started the Orbital
  Depot), the Ministry's memo sends them to the colony's bus stop. The depot (docs/story/DEPOT.md) waits until this duty is done.
- **Steps.** `bus` (ride from the colony), `power` (a power cable laid on the works, or a world cable beside them), `feed`
  (anything tipped into the works: Foreman Grubb shovels 20 iron ore into the inmate's pack as the step starts, the step's
  `give`; Interact on the Materials Bin), `metal` (cue `carry`: they carry some metal, taken from the bin the Smelter fills),
  `home` (ride back), `bunk`, `done`. Karl, 2026-11-12: the first machine is a Smelter, not the Assembler; a world saved with
  the older works (an Assembler) passes the `metal` step at once. Foreman Grubb says each step's line. Every step has an
  orange marker (the stop, the bus once it is called, the genset, the bins) with its distance, and a hint after
  `hintAfter` seconds (default 40). Cues come from the actions the server carried out (`QuarrySawAct`, beside the induction's).
- **Play areas** (2026-11-12). Each step names its `area` (the colony or the quarry); out of it the step waits and the
  Ministry's tannoy calls them back (docs/sandbox/TUTORIAL.md "Play areas", Story/Tutorial/Bounds.json).
- **Skip.** The Esc menu's "Skip this Ministry duty" counts it as done (not while riding).
- **Co-op.** Each person has their own run and their own bus; the works are shared, so a friend's cable counts only for the
  one who laid it.

## The crew bus

A STAND-IN (Source/StandIns): the hover hauler, a picture on a scripted path. At a ride step the host calls one to the stop
(12 s in, waiting up to 10 minutes). Once it has stopped, walking within 6 m of it boards: that bus pulls away, the other
stop's bus is called to come in as the scene ends, and for `QuarryRideSeconds` (10 s) the rider's input does nothing and the
camera follows the leaving bus, goes black ("Some time later..."), then watches the other bus arrive; then the host sets them
down beside it. The ride is a cinematic: letterbox bars, no HUD, hands, crosshair or health bars, only its caption in the
story text's size and colour (Settings > Video "Story text", `UiSubtitles.cs`). The body stays where it boarded until then (others see it standing at the stop: a stand-in's limit). When
real vehicles carry players, a real crew bus driven by Ministry staff replaces this.

## Wire and save (protocol 236)

`Msg.Quarry`, server to one client: u8 state (0 none, 1 report, 2 + step, 255 done), u8 has a target, vec the target, u32 the
tick they boarded (0: not riding), u16 the leaving bus, u16 the arriving bus, u8 outside the step's play area (protocol 258). `CActKind.QuarrySkip` from a client. Each
person's state is saved in the world's `QuarryTag` section (`WorldPlayer.QuarryState`); a ride under way resumes its step.

## Errands

Karl, 2026-11-14: more side quests that teach the machines at the bases. An errand is a small optional job someone at the
colony asks for, on one of the works yard's start kit machines (the Starter Foundry, Sawmill and Assembler stand inside the
fence, `GameColonySite.ColonySiteKit`). Code `Source/Game/GameErrands.cs` (host runner and wire), `Source/Game/Errands.cs`
(the files), panel `Source/UI/UiErrand.cs`; words `Story/Errands/*.json` (format `call-of-slop-errand/1`, one errand a file).

- **Who asks.** A storylet in `Story/Storylets/Errands.json` (the `side` lane, so a duty or the break-out doesn't hold it up):
  Ambrose Thale and the Foundry (steel) once the first shift is done (`FirstShift`, which the game sets when the quarry duty is
  done or skipped); then Officer Nettles and the Assembler (a wall clock for her requisition form) and Hester Mossop and the
  Sawmill (planks, and she won't say what for), both after the Foundry, so the machines come in stages. Its choice "Show me"
  ends it with `"hook": "errand:ID"`; "Not now" or no answer, and it asks again after 20 minutes.
- **The steps** are judged four times a second from how things stand, not from one action: `reach` (within `amount` m of
  the spot), `power` (the machine has power), `feed` (everything the step's `give` handed over has gone from the pack: into
  the machine or its bin), `recipe` (the Assembler set to `recipe`), `carry` (more of `material` than when the step began),
  `part` (more of `part`, in the pack or the hands), `wait`. Spots are on the machine's own construct: `machine`, `genset`,
  `in` (its first plain bin), `out` (its last). `give` is `{"material": units}`, topped up to the units. The giver says each
  step's `say`; `thanks` at the end, `gone` if the machine is taken away (the nearest other of its kind is used first).
  The start kit's machines are powered through their slabs as built, so no errand has a power step; a `carry` hint says what
  to do if the power has gone.
- **The machine** is the nearest working part named `machine` to the colony's middle (within `reach`, 300 m), never the
  quarry's works.
- **What it leaves.** Done, the story records an `Errand` (`Story/Verbs/Errands.json`): the player is `Taught` the machine
  (the file's `fact`, "the Foundry", a Thing the storylets ask for), and the giver `Likes` them 8 more, which also counts
  towards the break-out's trust. One at a time: the one under way is the exclusive relation `OnErrand`
  (`Story/Relations/Errands.json`), so it saves with the story and starts again from its first step after a load or a rejoin.
- **The panel** sits where the duties' panels do and waits while the induction, the quarry, the break-out or the depot's
  steps hold that place; the depot's report memo and the "what next" goal wait for it. Esc menu: "Drop this errand".
- **Wire (protocol 314).** `Msg.Errand`, server to one client: u8 errand + 1 (its index in `Story/Errands` by file name, 0
  none), u8 step (255 done), u8 has a target, vec target. `CActKind.ErrandDrop` from a client.
- **Checks.** `--check-story` loads the errands, warns when a giver is in no cast and refuses a storylet whose `errand:` hook
  names no errand. The window tool's `errand ID` starts one on the host, `errand` prints where it stands, `errand at` looks
  at its marker.

## Next

More duties in the same pattern, each teaching a machine: refining, the Requisition Crate and faction jobs (the Foundry,
the Sawmill and the Assembler are errands now, above). Errands not written yet: the Drill Rig (a vein to stand it over), the
Press and its dies, the Saw Rig. Standing with the Ministry for a duty done is not paid yet.
