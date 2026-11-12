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
tick they boarded (0: not riding), u16 the leaving bus, u16 the arriving bus. `CActKind.QuarrySkip` from a client. Each
person's state is saved in the world's `QuarryTag` section (`WorldPlayer.QuarryState`); a ride under way resumes its step.

## Next

More duties in the same pattern, each teaching a machine: the foundry (steel), the Assembler, refining, the Requisition
Crate and faction jobs. Standing with the Ministry for a duty done is not paid yet.
